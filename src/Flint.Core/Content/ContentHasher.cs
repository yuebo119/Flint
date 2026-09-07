// Flint 静态站点生成器
// 内容哈希计算器

namespace Flint.Core.Content;

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
