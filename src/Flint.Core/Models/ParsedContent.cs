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
    /// 标题列表（用于生成目录）
    /// </summary>
    public required IReadOnlyList<Heading> Headings { get; init; }

    /// <summary>
    /// 内容中的链接列表
    /// </summary>
    public required IReadOnlyList<ContentLink> Links { get; init; }

    /// <summary>
    /// 内容中的图片列表
    /// </summary>
    public required IReadOnlyList<ContentImage> Images { get; init; }

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

    /// <summary>
    /// 内容哈希（用于缓存）
    /// </summary>
    public required string ContentHash { get; init; }
}

/// <summary>
/// 标题信息
/// </summary>
public sealed class Heading
{
    /// <summary>
    /// 标题级别（1-6）
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// 标题文本
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 标题 ID（用于锚点链接）
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 子标题列表
    /// </summary>
    public IReadOnlyList<Heading> Children { get; init; } = [];
}

/// <summary>
/// 内容链接信息
/// </summary>
public sealed class ContentLink
{
    /// <summary>
    /// 链接 URL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// 链接文本
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 链接标题
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// 是否为外部链接
    /// </summary>
    public required bool IsExternal { get; init; }
}

/// <summary>
/// 内容图片信息
/// </summary>
public sealed class ContentImage
{
    /// <summary>
    /// 图片 URL
    /// </summary>
    public required string Src { get; init; }

    /// <summary>
    /// 图片替代文本
    /// </summary>
    public required string Alt { get; init; }

    /// <summary>
    /// 图片标题
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// 是否为外部图片
    /// </summary>
    public required bool IsExternal { get; init; }
}
