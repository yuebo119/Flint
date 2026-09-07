// Flint 静态站点生成器
// 缓存接口

namespace Flint.Core.Abstractions;

/// <summary>
/// 内容寻址缓存接口
/// </summary>
public interface IContentAddressableCache
{
    /// <summary>
    /// 获取缓存项
    /// </summary>
    /// <typeparam name="T">缓存项类型</typeparam>
    /// <param name="contentHash">内容哈希</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>缓存项，如果不存在则返回 null</returns>
    ValueTask<T?> GetAsync<T>(
        string contentHash,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 设置缓存项
    /// </summary>
    /// <typeparam name="T">缓存项类型</typeparam>
    /// <param name="contentHash">内容哈希</param>
    /// <param name="value">缓存值</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask SetAsync<T>(
        string contentHash,
        T value,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 检查缓存项是否存在
    /// </summary>
    /// <param name="contentHash">内容哈希</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否存在</returns>
    ValueTask<bool> ContainsAsync(
        string contentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除缓存项
    /// </summary>
    /// <param name="contentHash">内容哈希</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功删除</returns>
    ValueTask<bool> RemoveAsync(
        string contentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空缓存
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取缓存统计信息
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// 缓存统计信息
/// </summary>
public readonly record struct CacheStatistics
{
    /// <summary>
    /// 缓存项数量
    /// </summary>
    public required int ItemCount { get; init; }

    /// <summary>
    /// 缓存大小（字节）
    /// </summary>
    public required long TotalSize { get; init; }

    /// <summary>
    /// 命中次数
    /// </summary>
    public required long HitCount { get; init; }

    /// <summary>
    /// 未命中次数
    /// </summary>
    public required long MissCount { get; init; }

    /// <summary>
    /// 命中率
    /// </summary>
    public double HitRate => HitCount + MissCount > 0
        ? (double)HitCount / (HitCount + MissCount)
        : 0;
}

