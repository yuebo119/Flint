// Flint 静态站点生成器
// 内容哈希缓存 - 用于增量构建优化

using System.Collections.Concurrent;
using Flint.Core.Models;

namespace Flint.Core.Content;

/// <summary>
/// 内容哈希缓存（内存版，随构建器生命周期）
/// 用于跳过未变化的文件，加速增量构建
/// </summary>
public sealed class ContentHashCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>
    /// 缓存条目
    /// </summary>
    private sealed class CacheEntry
    {
        public string Hash { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTimeOffset LastModified { get; set; }
        public ParsedContent? Content { get; set; }
    }

    /// <summary>
    /// 尝试从缓存获取已解析的内容
    /// </summary>
    /// <param name="file">内容文件</param>
    /// <param name="cached">缓存的解析结果</param>
    /// <returns>是否命中缓存</returns>
    public bool TryGetCached(ContentFile file, out ParsedContent? cached)
    {
        cached = null;

        if (!_cache.TryGetValue(file.Path, out var entry))
        {
            return false;
        }

        // 快速检查：文件大小和修改时间
        if (entry.FileSize != file.RawContent.Length ||
            entry.LastModified != file.ModifiedTime)
        {
            return false;
        }

        // 完整检查：内容哈希
        var currentHash = ComputeHash(file.RawContent);
        if (entry.Hash != currentHash)
        {
            return false;
        }

        cached = entry.Content;
        return cached != null;
    }

    /// <summary>
    /// 添加或更新缓存
    /// </summary>
    /// <param name="file">内容文件</param>
    /// <param name="content">解析结果</param>
    public void Set(ContentFile file, ParsedContent content)
    {
        var hash = ComputeHash(file.RawContent);

        _cache[file.Path] = new CacheEntry
        {
            Hash = hash,
            FileSize = file.RawContent.Length,
            LastModified = file.ModifiedTime,
            Content = content
        };
    }

    /// <summary>
    /// 清除缓存
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// 获取缓存条目数
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// 计算内容哈希（FNV-1a 64 位，用于缓存变更检测足够）
    /// </summary>
    private static string ComputeHash(ReadOnlyMemory<byte> content)
    {
        ulong hash = 14695981039346656037UL;
        var span = content.Span;

        foreach (var b in span)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash.ToString("X16");
    }
}
