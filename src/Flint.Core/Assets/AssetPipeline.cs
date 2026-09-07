// Flint 静态站点生成器
// 资源处理管道实现

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Models;
using NUglify;

namespace Flint.Core.Assets;

/// <summary>
/// 资源处理管道实现
/// 整合图像处理、Sass 编译、JavaScript 打包等资源处理器
/// 支持并行处理和缓存
/// </summary>
public sealed class AssetPipeline : IAssetPipeline
{
    /// <summary>内存缓存条目上限：dev server 长驻期间键含内容哈希只增不减，超限整体清空</summary>
    private const int MaxMemoryCacheEntries = 2048;

    private readonly IImageProcessor? _imageProcessor;
    private readonly ISassCompiler? _sassCompiler;
    private readonly IJavaScriptBundler? _jsBundler;
    private readonly IContentAddressableCache? _cache;
    private readonly AssetPipelineOptions _options;
    private readonly ConcurrentDictionary<string, ProcessedAsset> _memoryCache = new();

    /// <summary>
    /// 处理选项指纹（预计算）：缓存键必须区分不同处理配置，
    /// 否则不同选项的管线（尤其共享持久化缓存时）会互相命中错误结果
    /// </summary>
    private readonly string _optionsFingerprint;

    /// <summary>
    /// 创建资源管道
    /// </summary>
    /// <param name="options">管道选项</param>
    /// <param name="imageProcessor">图像处理器（可选）</param>
    /// <param name="sassCompiler">Sass 编译器（可选）</param>
    /// <param name="jsBundler">JavaScript 打包器（可选）</param>
    /// <param name="cache">缓存（可选）</param>
    public AssetPipeline(
        AssetPipelineOptions? options = null,
        IImageProcessor? imageProcessor = null,
        ISassCompiler? sassCompiler = null,
        IJavaScriptBundler? jsBundler = null,
        IContentAddressableCache? cache = null)
    {
        _options = options ?? new AssetPipelineOptions();
        _imageProcessor = imageProcessor;
        _sassCompiler = sassCompiler;
        _jsBundler = jsBundler;
        // 持久化缓存默认启用磁盘实现（跨进程复用图片变换/Sass 编译产物）
        _cache = cache ?? (_options.EnablePersistentCache
            ? new DiskAssetCache(DiskAssetCache.DefaultCacheDirectory)
            : null);
        _optionsFingerprint = BuildOptionsFingerprint(_options);
    }

    /// <summary>
    /// 构建影响处理输出的选项指纹（用于缓存键）
    /// </summary>
    private static string BuildOptionsFingerprint(AssetPipelineOptions o)
    {
        var img = o.DefaultImageOptions;
        var sass = o.DefaultSassOptions;
        var bundle = o.DefaultBundleOptions;
        return string.Join('|',
            o.ProcessImages, o.CompileSass, o.ProcessScripts, o.MinifyCss,
            img.Width, img.Height, img.ResizeMode, img.OutputFormat, img.Quality,
            (int)sass.OutputStyle, sass.Minify, sass.GenerateSourceMap,
            string.Join(',', sass.IncludePaths),
            bundle.Minify, bundle.GenerateSourceMap, bundle.Target, (int)bundle.Format,
            bundle.TreeShaking, string.Join(',', bundle.External));
    }

    /// <inheritdoc/>
    public async ValueTask<ProcessedAsset> ProcessAsync(
        AssetFile asset,
        CancellationToken cancellationToken = default)
    {
        // 计算内容哈希用于缓存；键必须组合选项指纹与资源类型
        var cacheKey = $"{_optionsFingerprint}:{asset.Type}:{ContentHasher.ComputeHash(asset.Content.Span)}";

        // 检查内存缓存
        if (_options.EnableMemoryCache && _memoryCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // 检查持久化缓存
        if (_cache != null && _options.EnablePersistentCache)
        {
            var cachedAsset = await _cache.GetAsync<ProcessedAsset>(cacheKey, cancellationToken);
            if (cachedAsset != null)
            {
                if (_options.EnableMemoryCache)
                {
                    _memoryCache.TryAdd(cacheKey, cachedAsset);
                }
                return cachedAsset;
            }
        }

        // 根据资源类型处理；处理失败降级为 passthrough 时不写缓存
        // （降级产物 ≠ 真实处理结果，写入会以假结果污染不同失败策略的管线）
        var startTime = DateTime.UtcNow;
        var (processed, isFallback) = asset.Type switch
        {
            AssetType.Image => await ProcessImageWithFallbackAsync(asset, cancellationToken),
            AssetType.Style => await ProcessStyleWithFallbackAsync(asset, cancellationToken),
            AssetType.Script => await ProcessScriptWithFallbackAsync(asset, cancellationToken),
            _ => (CreatePassthroughAsset(asset), false)
        };

        // 更新处理时间
        var processingTime = DateTime.UtcNow - startTime;
        processed = processed with { ProcessingTime = processingTime };

        if (isFallback)
        {
            return processed;
        }

        // 存入缓存（上限见 MaxMemoryCacheEntries）
        if (_options.EnableMemoryCache)
        {
            if (_memoryCache.Count >= MaxMemoryCacheEntries)
            {
                _memoryCache.Clear();
            }
            _memoryCache.TryAdd(cacheKey, processed);
        }

        if (_cache != null && _options.EnablePersistentCache)
        {
            await _cache.SetAsync(cacheKey, processed, cancellationToken);
        }

        return processed;
    }

    private async ValueTask<(ProcessedAsset Asset, bool IsFallback)> ProcessImageWithFallbackAsync(
        AssetFile asset, CancellationToken cancellationToken)
    {
        if (_imageProcessor == null || !_options.ProcessImages)
        {
            return (CreatePassthroughAsset(asset), false);
        }

        try
        {
            return (await _imageProcessor.ProcessAsync(asset, _options.DefaultImageOptions, cancellationToken), false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_options.FailOnProcessingError)
            {
                throw;
            }
            return (CreatePassthroughAsset(asset), true);
        }
    }

    private async ValueTask<(ProcessedAsset Asset, bool IsFallback)> ProcessStyleWithFallbackAsync(
        AssetFile asset, CancellationToken cancellationToken)
    {
        var extension = asset.Extension;
        if ((extension == ".scss" || extension == ".sass") && _sassCompiler != null && _options.CompileSass)
        {
            try
            {
                return (await _sassCompiler.CompileAsync(asset, _options.DefaultSassOptions, cancellationToken), false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (_options.FailOnProcessingError)
                {
                    throw;
                }
                return (CreatePassthroughAsset(asset), true);
            }
        }

        // CSS 文件：启用 MinifyCss 时真实压缩
        if (_options.MinifyCss)
        {
            var css = Encoding.UTF8.GetString(asset.Content.Span);
            var result = Uglify.Css(css);
            if (result.HasErrors)
            {
                // 压缩失败按 FailOnProcessingError 策略处理，不静默标记为已压缩
                if (_options.FailOnProcessingError)
                {
                    throw new InvalidOperationException(
                        $"CSS 压缩失败 ({asset.SourcePath}): {string.Join("; ", result.Errors.Select(e => e.Message))}");
                }
                return (CreatePassthroughAsset(asset), true);
            }

            var minified = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(result.Code));
            return (CreateProcessedAssetFromContent(asset, minified, isMinified: true), false);
        }

        return (CreatePassthroughAsset(asset), false);
    }

    private async ValueTask<(ProcessedAsset Asset, bool IsFallback)> ProcessScriptWithFallbackAsync(
        AssetFile asset, CancellationToken cancellationToken)
    {
        if (_jsBundler == null || !_options.ProcessScripts)
        {
            return (CreatePassthroughAsset(asset), false);
        }

        try
        {
            return (await _jsBundler.TranspileAsync(asset, _options.DefaultBundleOptions, cancellationToken), false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (_options.FailOnProcessingError)
            {
                throw;
            }
            return (CreatePassthroughAsset(asset), true);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
        IReadOnlyList<AssetFile> assets,
        CancellationToken cancellationToken = default)
    {
        if (assets.Count == 0)
        {
            return [];
        }

        // 使用并行处理
        var results = new ProcessedAsset[assets.Count];
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, assets.Count),
            parallelOptions,
            async (index, ct) =>
            {
                results[index] = await ProcessAsync(assets[index], ct);
            });

        return results;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ProcessedAsset> ProcessStreamAsync(
        IAsyncEnumerable<AssetFile> assets,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // 使用 Channel 实现并行流式处理
        var channel = Channel.CreateBounded<ProcessedAsset>(
            new BoundedChannelOptions(_options.MaxParallelism * 2)
            {
                SingleReader = true,
                SingleWriter = false
            });

        // 启动生产者任务
        var producerTask = Task.Run(async () =>
        {
            try
            {
                var semaphore = new SemaphoreSlim(_options.MaxParallelism);
                var tasks = new List<Task>();

                await foreach (var asset in assets.WithCancellation(cancellationToken))
                {
                    await semaphore.WaitAsync(cancellationToken);

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            var processed = await ProcessAsync(asset, cancellationToken);
                            await channel.Writer.WriteAsync(processed, cancellationToken);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken);

                    tasks.Add(task);
                }

                await Task.WhenAll(tasks);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // 消费处理结果
        await foreach (var processed in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return processed;
        }

        await producerTask;
    }





    /// <summary>
    /// 创建直通资源（不处理，只添加哈希）
    /// </summary>
    private ProcessedAsset CreatePassthroughAsset(AssetFile asset, bool minify = false)
    {
        return CreateProcessedAssetFromContent(asset, asset.Content, minify);
    }

    /// <summary>
    /// 基于已处理内容构建 ProcessedAsset（计算哈希/SRI/指纹）
    /// </summary>
    private ProcessedAsset CreateProcessedAssetFromContent(AssetFile asset, ReadOnlyMemory<byte> content, bool isMinified)
    {
        var contentHash = ContentHasher.ComputeHash(content.Span);
        var sriHash = ContentHasher.ComputeSriHash(content.Span);
        var fingerprintedPath = ContentHasher.GenerateFingerprintedPath(asset.SourcePath, content.Span);

        // 确定输出路径
        var outputPath = DetermineOutputPath(asset.SourcePath);

        return new ProcessedAsset
        {
            OutputPath = outputPath,
            MediaType = asset.MediaType,
            Content = content,
            ContentHash = contentHash,
            Integrity = sriHash,
            FingerprintedPath = fingerprintedPath,
            SourcePath = asset.SourcePath,
            IsMinified = isMinified,
            OriginalSize = asset.Content.Length
        };
    }

    /// <summary>
    /// 确定输出路径
    /// </summary>
    private string DetermineOutputPath(string sourcePath)
    {
        // 移除源目录前缀（后必须紧跟分隔符或完全相等，否则 "assets" 会误匹配 "assetsX/foo.css"）
        var relativePath = sourcePath;
        if (!string.IsNullOrEmpty(_options.SourceDirectory) &&
            sourcePath.StartsWith(_options.SourceDirectory, StringComparison.OrdinalIgnoreCase) &&
            (sourcePath.Length == _options.SourceDirectory.Length ||
             sourcePath[_options.SourceDirectory.Length] is '/' or '\\'))
        {
            relativePath = sourcePath[_options.SourceDirectory.Length..].TrimStart('/', '\\');
        }

        // 前缀不匹配的绝对路径不能参与 Combine：Path.Combine 第二参为
        // rooted 路径时直接返回该路径，输出会逃出输出目录——fail loud。
        // 边界判定与上方剥离块一致（长度相等或后随分隔符，防 "C:\siteX" 误判在源目录内）
        var withinSource = !string.IsNullOrEmpty(_options.SourceDirectory) &&
            relativePath.StartsWith(_options.SourceDirectory, StringComparison.OrdinalIgnoreCase) &&
            (relativePath.Length == _options.SourceDirectory.Length ||
             relativePath[_options.SourceDirectory.Length] is '/' or '\\');
        if (!string.IsNullOrEmpty(_options.OutputDirectory) &&
            !withinSource &&
            Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                $"资源路径不在源目录内，拒绝写出输出目录之外: {sourcePath}");
        }

        // 添加输出目录前缀
        if (!string.IsNullOrEmpty(_options.OutputDirectory))
        {
            return Path.Combine(_options.OutputDirectory, relativePath);
        }

        return relativePath;
    }

    /// <summary>
    /// 清除内存缓存
    /// </summary>
    public void ClearMemoryCache()
    {
        _memoryCache.Clear();
    }

    /// <summary>
    /// 获取内存缓存统计
    /// </summary>
    public int MemoryCacheCount => _memoryCache.Count;
}


/// <summary>
/// 资源管道配置选项
/// </summary>
public sealed class AssetPipelineOptions
{
    /// <summary>
    /// 最大并行度
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// 是否启用内存缓存
    /// </summary>
    public bool EnableMemoryCache { get; init; } = true;

    /// <summary>
    /// 是否启用持久化缓存
    /// </summary>
    public bool EnablePersistentCache { get; init; } = true;

    /// <summary>
    /// 是否处理图像
    /// </summary>
    public bool ProcessImages { get; init; } = true;

    /// <summary>
    /// 是否编译 Sass
    /// </summary>
    public bool CompileSass { get; init; } = true;

    /// <summary>
    /// 是否处理脚本
    /// </summary>
    public bool ProcessScripts { get; init; } = true;

    /// <summary>
    /// 是否压缩 CSS
    /// </summary>
    public bool MinifyCss { get; init; }

    /// <summary>
    /// 处理错误时是否抛出异常
    /// </summary>
    public bool FailOnProcessingError { get; init; }

    /// <summary>
    /// 源目录
    /// </summary>
    public string SourceDirectory { get; init; } = "assets";

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDirectory { get; init; } = "public/assets";

    /// <summary>
    /// 默认图像处理选项
    /// </summary>
    public ImageProcessingOptions DefaultImageOptions { get; init; } = ImageProcessingOptions.Default;

    /// <summary>
    /// 默认 Sass 编译选项
    /// </summary>
    public SassOptions DefaultSassOptions { get; init; } = new();

    /// <summary>
    /// 默认 JavaScript 打包选项
    /// </summary>
    public BundleOptions DefaultBundleOptions { get; init; } = new();
}
