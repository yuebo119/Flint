// Flint 静态站点生成器
// 解析后的内容类型

namespace Flint.Core.Models;

/// <summary>
/// 解析后的内容
/// 包含 Front Matter、HTML 内容和元数据
/// </summary>
public sealed class ParsedContent
{
    /// <summary>
    /// 源文件路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Front Matter 元数据
    /// </summary>
    public required FrontMatter Metadata { get; init; }

    /// <summary>
    /// 渲染后的 HTML 内容
    /// </summary>
    public required string HtmlContent { get; init; }

    /// <summary>
    /// 原始 Markdown 内容（不含 Front Matter）
    /// </summary>
    public required string RawMarkdown { get; init; }

    /// <summary>
    /// 预计阅读时间
    /// </summary>
    public required TimeSpan ReadingTime { get; init; }

    /// <summary>
    /// 字数统计
    /// </summary>
    public required int WordCount { get; init; }

    /// <summary>
    /// 纯文本内容（用于搜索索引）
    /// </summary>
    public required string PlainText { get; init; }

    /// <summary>
    /// 内容摘要（自动生成或从 Front Matter 获取）
    /// </summary>
    public required string Summary { get; init; }
}

