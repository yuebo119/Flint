// Flint 静态站点生成器
// 内容文件数据类型

namespace Flint.Core.Models;

/// <summary>
/// 表示一个内容文件的只读记录结构
/// 使用 ReadOnlyMemory 避免内存分配
/// </summary>
public readonly record struct ContentFile
{
    /// <summary>
    /// 文件相对路径
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 原始文件内容（字节形式）
    /// </summary>
    public required ReadOnlyMemory<byte> RawContent { get; init; }

    /// <summary>
    /// 文件最后修改时间
    /// </summary>
    public required DateTimeOffset ModifiedTime { get; init; }

    /// <summary>
    /// 获取内容的字符串表示（UTF-8 解码，自动去除 BOM）
    /// </summary>
    public string GetContentAsString()
    {
        var span = RawContent.Span;

        // 检查并跳过 UTF-8 BOM (EF BB BF)
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
        {
            span = span[3..];
        }

        return System.Text.Encoding.UTF8.GetString(span);
    }

    /// <summary>
    /// 获取内容的 Span 表示
    /// </summary>
    public ReadOnlySpan<byte> ContentSpan => RawContent.Span;

    /// <summary>
    /// 内容长度（字节数）
    /// </summary>
    public int Length => RawContent.Length;

    /// <summary>
    /// 文件扩展名（小写）
    /// </summary>
    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();

    /// <summary>
    /// 文件名（不含路径）
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(Path);
}
