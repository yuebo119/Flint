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

/// <summary>
/// 内容哈希计算器
/// </summary>
public static class ContentHasher
{
    /// <summary>
    /// 计算字节内容的 SHA256 哈希
    /// </summary>
    /// <param name="content">内容</param>
    /// <returns>十六进制哈希字符串</returns>
    public static string ComputeHash(ReadOnlySpan<byte> content)
    {
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(content, hash);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 计算字符串内容的 SHA256 哈希
    /// </summary>
    /// <param name="content">内容</param>
    /// <returns>十六进制哈希字符串</returns>
    public static string ComputeHash(ReadOnlySpan<char> content)
    {
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(content);
        Span<byte> bytes = byteCount <= 1024
            ? stackalloc byte[byteCount]
            : new byte[byteCount];
        System.Text.Encoding.UTF8.GetBytes(content, bytes);
        return ComputeHash(bytes);
    }

    /// <summary>
    /// 计算字符串内容的 SHA256 哈希
    /// </summary>
    /// <param name="content">内容</param>
    /// <returns>十六进制哈希字符串</returns>
    public static string ComputeHash(string content)
    {
        return ComputeHash(content.AsSpan());
    }

    /// <summary>
    /// 计算 SRI（子资源完整性）哈希
    /// </summary>
    /// <param name="content">内容</param>
    /// <returns>SRI 哈希字符串（sha256-base64）</returns>
    public static string ComputeSriHash(ReadOnlySpan<byte> content)
    {
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(content, hash);
        return $"sha256-{Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// 计算短哈希（用于文件名指纹）
    /// </summary>
    /// <param name="content">内容</param>
    /// <param name="length">哈希长度（默认 8）</param>
    /// <returns>短哈希字符串</returns>
    public static string ComputeShortHash(ReadOnlySpan<byte> content, int length = 8)
    {
        var fullHash = ComputeHash(content);
        return fullHash[..Math.Min(length, fullHash.Length)];
    }

    /// <summary>
    /// 生成带指纹的文件名
    /// </summary>
    /// <param name="originalPath">原始文件路径</param>
    /// <param name="content">文件内容</param>
    /// <returns>带指纹的文件名</returns>
    public static string GenerateFingerprintedPath(string originalPath, ReadOnlySpan<byte> content)
    {
        var hash = ComputeShortHash(content);
        var extension = Path.GetExtension(originalPath);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
        var directory = Path.GetDirectoryName(originalPath) ?? "";
        return Path.Combine(directory, $"{nameWithoutExtension}.{hash}{extension}");
    }
}
