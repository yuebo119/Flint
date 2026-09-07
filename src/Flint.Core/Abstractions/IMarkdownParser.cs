// Flint 静态站点生成器
// Markdown 解析器接口

namespace Flint.Core.Abstractions;

/// <summary>
/// Markdown 解析器接口
/// 负责将 Markdown 文本转换为 HTML
/// </summary>
public interface IMarkdownParser
{
    /// <summary>
    /// 将 Markdown 文本解析为 HTML
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>HTML 字符串</returns>
    string ToHtml(string markdown);

    /// <summary>
    /// 将 Markdown 文本解析为 HTML（使用 Span 优化）
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>HTML 字符串</returns>
    string ToHtml(ReadOnlySpan<char> markdown);

    /// <summary>
    /// 从 Markdown 文本中提取标题列表
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>标题列表</returns>
    IReadOnlyList<MarkdownHeading> ExtractHeadings(string markdown);

    /// <summary>
    /// 从 Markdown 文本中提取纯文本（去除所有标记）
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>纯文本</returns>
    string ToPlainText(string markdown);

    /// <summary>
    /// 统计 Markdown 文本的字数
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>字数</returns>
    int CountWords(string markdown);

    /// <summary>
    /// 计算预计阅读时间
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <param name="wordsPerMinute">每分钟阅读字数（默认 200）</param>
    /// <returns>预计阅读时间</returns>
    TimeSpan CalculateReadingTime(string markdown, int wordsPerMinute = 200);

    /// <summary>
    /// 从 Markdown 文本中提取链接列表
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>链接列表</returns>
    IReadOnlyList<MarkdownLink> ExtractLinks(string markdown);

    /// <summary>
    /// 从 Markdown 文本中提取图片列表
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>图片列表</returns>
    IReadOnlyList<MarkdownImage> ExtractImages(string markdown);

    /// <summary>
    /// 生成目录（Table of Contents）HTML
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <param name="maxLevel">最大标题级别（默认 3）</param>
    /// <returns>目录 HTML</returns>
    string GenerateTableOfContents(string markdown, int maxLevel = 3);

    /// <summary>
    /// 单次解析并产出全部派生信息（HTML/标题/链接/图片/纯文本/字数/阅读时间）
    /// 构建管线应优先使用此方法，避免对同一文档重复执行完整的 Markdown 解析
    /// </summary>
    /// <param name="markdown">Markdown 文本</param>
    /// <returns>完整分析结果</returns>
    MarkdownAnalysis Analyze(string markdown);
}

/// <summary>
/// Markdown 单次解析的完整分析结果
/// </summary>
public sealed record MarkdownAnalysis
{
    /// <summary>
    /// 渲染后的 HTML
    /// </summary>
    public required string Html { get; init; }

    /// <summary>
    /// 标题列表
    /// </summary>
    public required IReadOnlyList<MarkdownHeading> Headings { get; init; }

    /// <summary>
    /// 链接列表
    /// </summary>
    public required IReadOnlyList<MarkdownLink> Links { get; init; }

    /// <summary>
    /// 图片列表
    /// </summary>
    public required IReadOnlyList<MarkdownImage> Images { get; init; }

    /// <summary>
    /// 纯文本（去除所有标记）
    /// </summary>
    public required string PlainText { get; init; }

    /// <summary>
    /// 字数（中英文混合统计）
    /// </summary>
    public required int WordCount { get; init; }

    /// <summary>
    /// 预计阅读时间
    /// </summary>
    public required TimeSpan ReadingTime { get; init; }
}

/// <summary>
/// Markdown 标题信息
/// </summary>
public sealed class MarkdownHeading
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
    /// 标题在文档中的行号
    /// </summary>
    public int Line { get; init; }
}

/// <summary>
/// Markdown 链接信息
/// </summary>
public sealed class MarkdownLink
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
    public bool IsExternal => Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                           || Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Markdown 图片信息
/// </summary>
public sealed class MarkdownImage
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
    public bool IsExternal => Src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                           || Src.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
