// Flint 静态站点生成器
// 磁盘资源缓存实现 - 跨进程持久的资源处理产物缓存

using System.Text.Json;
using System.Text.Json.Serialization;
using Flint.Core.Abstractions;
using Flint.Core.Models;

namespace Flint.Core.Assets;

/// <summary>
/// 磁盘资源缓存
/// 以"内容哈希 + 选项指纹"为键（键由 AssetPipeline 组成），产物落盘跨进程复用；
/// 目录布局：<c>cacheDir/aa/&lt;key&gt;.json</c>（元数据）+ <c>cacheDir/aa/&lt;key&gt;.bin</c>（内容字节）。
/// 参考 Hugo filecache 的命名缓存 + MaxAge + pruner 设计。
/// </summary>
public sealed class DiskAssetCache : IContentAddressableCache
{
    private readonly string _cacheDir;
    private readonly TimeSpan? _maxAge;
    private long _hitCount;
    private long _missCount;

    /// <summary>
    /// 创建磁盘资源缓存
    /// </summary>
    /// <param name="cacheDir">缓存目录（不存在时自动创建）</param>
    /// <param name="maxAge">条目最大年龄；null 表示永不过期（Hugo 语义 -1）</param>
    public DiskAssetCache(string cacheDir, TimeSpan? maxAge = null)
    {
        _cacheDir = cacheDir;
        _maxAge = maxAge;
        Directory.CreateDirectory(cacheDir);
    }

    /// <summary>
    /// 默认缓存目录（跨站点共享，对齐 Hugo 的用户级 filecache 位置）
    /// </summary>
    public static string DefaultCacheDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(string.IsNullOrEmpty(root) ? Path.GetTempPath() : root, "Flint", "cache", "assets");
        }
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string contentHash, CancellationToken cancellationToken = default)
        where T : class
    {
        if (typeof(T) != typeof(ProcessedAsset))
        {
            throw new NotSupportedException($"{nameof(DiskAssetCache)} 只缓存 {nameof(ProcessedAsset)}，不支持 {typeof(T).Name}");
        }

        var (metaPath, binPath) = GetEntryPaths(contentHash);
        try
        {
            if (!File.Exists(metaPath) || !File.Exists(binPath))
            {
                Interlocked.Increment(ref _missCount);
                return null;
            }

            if (_maxAge is { } maxAge &&
                File.GetLastWriteTimeUtc(metaPath) + maxAge < DateTime.UtcNow)
            {
                Interlocked.Increment(ref _missCount);
                await RemoveEntryAsync(metaPath, binPath, cancellationToken);
                return null;
            }

            var metaJson = await File.ReadAllTextAsync(metaPath, cancellationToken);
            var meta = JsonSerializer.Deserialize(metaJson, AssetCacheJsonContext.Default.CachedAssetMetadata);
            if (meta is null)
            {
                Interlocked.Increment(ref _missCount);
                return null;
            }

            var content = await File.ReadAllBytesAsync(binPath, cancellationToken);
            Interlocked.Increment(ref _hitCount);
            return new ProcessedAsset
            {
                OutputPath = meta.OutputPath,
                MediaType = meta.MediaType,
                Content = content,
                ContentHash = meta.ContentHash,
                Integrity = meta.Integrity,
                FingerprintedPath = meta.FingerprintedPath,
                SourcePath = meta.SourcePath,
                ProcessingTime = TimeSpan.Zero, // 命中缓存无处理耗时
                IsMinified = meta.IsMinified,
                OriginalSize = meta.OriginalSize
            } as T;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 缓存损坏按未命中处理并清理该条目，不影响构建；
            // 清理自身的权限/IO 异常同样吞掉（对齐"缓存损坏不影响构建"的注释承诺）
            Interlocked.Increment(ref _missCount);
            try
            { await RemoveEntryAsync(metaPath, binPath, cancellationToken); }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(string contentHash, T value, CancellationToken cancellationToken = default)
        where T : class
    {
        if (value is not ProcessedAsset asset)
        {
            throw new NotSupportedException($"{nameof(DiskAssetCache)} 只缓存 {nameof(ProcessedAsset)}，不支持 {typeof(T).Name}");
        }

        var (metaPath, binPath) = GetEntryPaths(contentHash);
        var dir = Path.GetDirectoryName(metaPath)!;
        Directory.CreateDirectory(dir);

        // 临时文件 + 原子替换，避免并发写产生损坏条目
        var tempMeta = metaPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var tempBin = binPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var meta = new CachedAssetMetadata
            {
                OutputPath = asset.OutputPath,
                MediaType = asset.MediaType,
                ContentHash = asset.ContentHash,
                Integrity = asset.Integrity,
                FingerprintedPath = asset.FingerprintedPath,
                SourcePath = asset.SourcePath,
                IsMinified = asset.IsMinified,
                OriginalSize = asset.OriginalSize
            };
            await File.WriteAllTextAsync(tempMeta, JsonSerializer.Serialize(meta, AssetCacheJsonContext.Default.CachedAssetMetadata), cancellationToken);
            await File.WriteAllBytesAsync(tempBin, asset.Content.ToArray(), cancellationToken);

            File.Move(tempMeta, metaPath, overwrite: true);
            File.Move(tempBin, binPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 磁盘满/被锁/并发覆盖竞争（Windows 上 Move overwrite 目标被占用即拒绝访问）：
            // 缓存写是尽力而为，失败不影响构建，读取方会用旧条目或重新计算
            TryDelete(tempMeta);
            TryDelete(tempBin);
        }
        finally
        {
            TryDelete(tempMeta);
            TryDelete(tempBin);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> ContainsAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        var (metaPath, binPath) = GetEntryPaths(contentHash);
        return ValueTask.FromResult(File.Exists(metaPath) && File.Exists(binPath));
    }

    /// <inheritdoc />
    public ValueTask<bool> RemoveAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        var (metaPath, binPath) = GetEntryPaths(contentHash);
        if (!File.Exists(metaPath) && !File.Exists(binPath))
        {
            return ValueTask.FromResult(false);
        }
        RemoveEntryAsync(metaPath, binPath, cancellationToken).AsTask().GetAwaiter().GetResult();
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
            Directory.CreateDirectory(_cacheDir);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public CacheStatistics GetStatistics()
    {
        var hit = Interlocked.Read(ref _hitCount);
        var miss = Interlocked.Read(ref _missCount);
        var (count, size) = ScanEntries();
        return new CacheStatistics
        {
            ItemCount = count,
            TotalSize = size,
            HitCount = hit,
            MissCount = miss
        };
    }

    /// <summary>
    /// 清理超过 MaxAge 的过期条目（对齐 Hugo 的 filecache pruner），返回删除的条目数
    /// </summary>
    public int Prune()
    {
        if (_maxAge is not { } maxAge)
            return 0;
        var cutoff = DateTime.UtcNow - maxAge;
        var removed = 0;

        foreach (var metaPath in Directory.EnumerateFiles(_cacheDir, "*.json", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(metaPath) >= cutoff)
                continue;
            var binPath = Path.ChangeExtension(metaPath, ".bin");
            try
            {
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
                if (File.Exists(binPath))
                    File.Delete(binPath);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // 文件被占用/只读：跳过留待下次清理（与 GetAsync 清理路径的扩捕口径一致）
                _ = ex;
            }
        }
        return removed;
    }

    private (string MetaPath, string BinPath) GetEntryPaths(string key)
    {
        // 键含"|"与"："等（选项指纹:类型:哈希），先转为安全文件名
        var safeKey = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(key)));
        var bucket = safeKey[..2];
        var dir = Path.Combine(_cacheDir, bucket);
        return (Path.Combine(dir, safeKey + ".json"), Path.Combine(dir, safeKey + ".bin"));
    }

    private static async ValueTask RemoveEntryAsync(string metaPath, string binPath, CancellationToken ct)
    {
        try
        {
            if (File.Exists(metaPath))
                await Task.Run(() => File.Delete(metaPath), ct);
            if (File.Exists(binPath))
                await Task.Run(() => File.Delete(binPath), ct);
        }
        catch (IOException)
        {
            // 删除失败不影响主流程
        }
    }

    private static void TryDelete(string path)
    {
        try
        { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
    }

    private (int Count, long Size) ScanEntries()
    {
        var count = 0;
        long size = 0;
        if (!Directory.Exists(_cacheDir))
            return (0, 0);
        foreach (var file in Directory.EnumerateFiles(_cacheDir, "*", SearchOption.AllDirectories))
        {
            count++;
            try
            { size += new FileInfo(file).Length; }
            catch (IOException) { }
        }
        return (count, size);
    }
}

/// <summary>
/// 缓存条目元数据（Content 单独存 .bin 文件，避免大内容经 JSON base64 膨胀）
/// </summary>
internal sealed record CachedAssetMetadata
{
    public required string OutputPath { get; init; }
    public required string MediaType { get; init; }
    public required string ContentHash { get; init; }
    public string? Integrity { get; init; }
    public string? FingerprintedPath { get; init; }
    public required string SourcePath { get; init; }
    public bool IsMinified { get; init; }
    public int OriginalSize { get; init; }
}

/// <summary>
/// 缓存元数据序列化上下文（source-gen，AOT/trim 友好）
/// </summary>
[JsonSerializable(typeof(CachedAssetMetadata))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal sealed partial class AssetCacheJsonContext : JsonSerializerContext
{
}
