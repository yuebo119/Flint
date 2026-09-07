// Flint 静态站点生成器
// 内容解析器实现 - 整合 Front Matter、Markdown、短代码解析

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;

namespace Flint.Core.Content;

/// <summary>
/// 内容解析器实现
/// 整合 Front Matter、Markdown、短代码解析，构建 ParsedContent
/// </summary>
public sealed class ContentParser : IContentParser
{
    /// <summary>
    /// 默认摘要长度（字符数）
    /// </summary>
    private const int DefaultSummaryLength = 200;

    /// <summary>
    /// 摘要分隔符
    /// </summary>
    private const string SummaryDivider = "<!--more-->";

    /// <summary>
    /// SHA256 哈希缓冲区大小
    /// </summary>
    private const int HashBufferSize = 32;

    private readonly IFrontMatterParser _frontMatterParser;
    private readonly IMarkdownParser _markdownParser;
    private readonly ShortcodeProcessor _shortcodeProcessor;

    /// <summary>
    /// 站点时区（Hugo <c>timeZone</c> 配置）：无偏移的日期值按此时区解释。
    /// 由站点装配点在构建前设置；null 时沿用本机时区
    /// </summary>
    public TimeZoneInfo? SiteTimeZone { get; set; }

    /// <summary>
    /// Hugo <c>:git</c> 特殊日期源的数据提供方。
    /// 由站点装配点在构建前设置；null 时 ":git" 源静默缺省
    /// </summary>
    public IGitDateProvider? GitDates { get; set; }

    /// <summary>
    /// 创建内容解析器实例（使用默认组件）
    /// </summary>
    public ContentParser()
        : this(new FrontMatterParser(), new MarkdownParser(), new ShortcodeProcessor())
    {
    }

    /// <summary>
    /// 创建内容解析器实例（使用自定义组件）
    /// </summary>
    /// <param name="frontMatterParser">Front Matter 解析器</param>
    /// <param name="markdownParser">Markdown 解析器</param>
    /// <param name="shortcodeProcessor">短代码处理器</param>
    public ContentParser(
        IFrontMatterParser frontMatterParser,
        IMarkdownParser markdownParser,
        ShortcodeProcessor shortcodeProcessor)
    {
        _frontMatterParser = frontMatterParser ?? throw new ArgumentNullException(nameof(frontMatterParser));
        _markdownParser = markdownParser ?? throw new ArgumentNullException(nameof(markdownParser));
        _shortcodeProcessor = shortcodeProcessor ?? throw new ArgumentNullException(nameof(shortcodeProcessor));
    }

    /// <summary>
    /// 获取短代码处理器（用于注册自定义短代码）
    /// </summary>
    public ShortcodeProcessor ShortcodeProcessor => _shortcodeProcessor;

    /// <inheritdoc />
    public async ValueTask<ParsedContent> ParseAsync(
        ContentFile file,
        CancellationToken cancellationToken = default)
    {
        // 获取文件内容字符串
        var content = file.GetContentAsString();

        // 1. 解析 Front Matter
        FrontMatter metadata;
        string markdownContent;

        if (_frontMatterParser.TryParse(content.AsSpan(), out var frontMatter, out var remaining, SiteTimeZone))
        {
            metadata = frontMatter!
                .WithFilenameConvention(file.Path)
                .WithDateSources(file.Path, file.ModifiedTime, GitDates);
            markdownContent = remaining.ToString();
        }
        else
        {
            // 没有 Front Matter，使用默认值
            metadata = CreateDefaultFrontMatter(file.Path);
            markdownContent = content;
        }

        // 2. 处理短代码（双语义对齐 Hugo：{{< >}} 占位符后置展开避开 Markdown；{{% %}} 内联参与渲染）
        var (markdownWithTokens, deferredShortcodes) = await _shortcodeProcessor.ProcessForMarkdownAsync(
            markdownContent,
            pageContext: null,
            siteContext: null,
            cancellationToken).ConfigureAwait(false);

        // 3+4. 单次解析产出 HTML 与全部元数据（此前各步骤独立解析，同一文档被完整解析 7 次）
        var analysis = _markdownParser.Analyze(markdownWithTokens);
        var htmlContent = ShortcodeProcessor.ExpandShortcodeTokens(analysis.Html, deferredShortcodes);
        var headings = ExtractHeadings(analysis.Headings);
        var links = ExtractLinks(analysis.Links);
        var images = ExtractImages(analysis.Images);
        var wordCount = analysis.WordCount;
        var readingTime = analysis.ReadingTime;
        var plainText = analysis.PlainText;

        // 5. 生成摘要
        var summary = GenerateSummary(metadata, markdownContent, plainText);

        // 6. 计算内容哈希
        var contentHash = ComputeContentHash(content);

        // 7. 构建 ParsedContent
        return new ParsedContent
        {
            SourcePath = file.Path,
            Metadata = metadata,
            HtmlContent = htmlContent,
            RawMarkdown = markdownContent,
            Headings = headings,
            Links = links,
            Images = images,
            ReadingTime = readingTime,
            WordCount = wordCount,
            PlainText = plainText,
            Summary = summary,
            ContentHash = contentHash
        };
    }

    /// <inheritdoc />
    public ParsedContent Parse(ContentFile file)
    {
        // 同步版本，直接调用异步方法
        return ParseAsync(file).AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ParsedContent> ParseBatchAsync(
        IEnumerable<ContentFile> files,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await ParseAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 创建默认的 Front Matter（当文件没有 Front Matter 时使用）
    /// </summary>
    private static FrontMatter CreateDefaultFrontMatter(string filePath)
    {
        // 从文件名生成标题
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var title = ConvertFileNameToTitle(fileName);

        return new FrontMatter
        {
            Title = title,
            Draft = false
        };
    }

    /// <summary>
    /// 将文件名转换为标题
    /// </summary>
    private static string ConvertFileNameToTitle(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "Untitled";
        }

        // 将连字符和下划线替换为空格
        var title = fileName.Replace('-', ' ').Replace('_', ' ');

        // 首字母大写
        if (title.Length > 0)
        {
            title = char.ToUpperInvariant(title[0]) + title[1..];
        }

        return title;
    }

    /// <summary>
    /// 将 MarkdownHeading 转换为 Heading
    /// </summary>
    private static List<Heading> ExtractHeadings(IReadOnlyList<MarkdownHeading> markdownHeadings)
    {
        return markdownHeadings.Select(h => new Heading
        {
            Level = h.Level,
            Text = h.Text,
            Id = h.Id
        }).ToList();
    }

    /// <summary>
    /// 将 MarkdownLink 转换为 ContentLink
    /// </summary>
    private static List<ContentLink> ExtractLinks(IReadOnlyList<MarkdownLink> markdownLinks)
    {
        return markdownLinks.Select(l => new ContentLink
        {
            Url = l.Url,
            Text = l.Text,
            Title = l.Title,
            IsExternal = l.IsExternal
        }).ToList();
    }

    /// <summary>
    /// 将 MarkdownImage 转换为 ContentImage
    /// </summary>
    private static List<ContentImage> ExtractImages(IReadOnlyList<MarkdownImage> markdownImages)
    {
        return markdownImages.Select(i => new ContentImage
        {
            Src = i.Src,
            Alt = i.Alt,
            Title = i.Title,
            IsExternal = i.IsExternal
        }).ToList();
    }

    /// <summary>
    /// 生成内容摘要
    /// </summary>
    private static string GenerateSummary(FrontMatter metadata, string markdownContent, string plainText)
    {
        // 优先使用 Front Matter 中的摘要
        if (!string.IsNullOrWhiteSpace(metadata.Summary))
        {
            return metadata.Summary;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Description))
        {
            return metadata.Description;
        }

        // 检查是否有摘要分隔符
        var dividerIndex = markdownContent.IndexOf(SummaryDivider, StringComparison.OrdinalIgnoreCase);
        if (dividerIndex >= 0)
        {
            // 提取分隔符之前的内容作为摘要
            var summaryMarkdown = markdownContent[..dividerIndex].Trim();
            // 简单处理：移除 Markdown 标记
            return StripMarkdownForSummary(summaryMarkdown);
        }

        // 从纯文本中截取摘要
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        if (plainText.Length <= DefaultSummaryLength)
        {
            return plainText;
        }

        // 在单词边界处截断
        var summary = plainText[..DefaultSummaryLength];
        var lastSpace = summary.LastIndexOf(' ');
        if (lastSpace > DefaultSummaryLength / 2)
        {
            summary = summary[..lastSpace];
        }

        return summary.TrimEnd() + "...";
    }

    /// <summary>
    /// 简单移除 Markdown 标记用于摘要
    /// </summary>
    private static string StripMarkdownForSummary(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var result = markdown;

        // 移除标题标记
        result = System.Text.RegularExpressions.Regex.Replace(result, @"^#{1,6}\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);

        // 移除粗体和斜体
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\*\*([^*]+)\*\*", "$1");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\*([^*]+)\*", "$1");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"__([^_]+)__", "$1");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"_([^_]+)_", "$1");

        // 移除链接，保留文本
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\[([^\]]+)\]\([^)]+\)", "$1");

        // 移除图片
        result = System.Text.RegularExpressions.Regex.Replace(result, @"!\[([^\]]*)\]\([^)]+\)", "");

        // 移除代码块
        result = System.Text.RegularExpressions.Regex.Replace(result, @"```[\s\S]*?```", "");
        result = System.Text.RegularExpressions.Regex.Replace(result, @"`([^`]+)`", "$1");

        // 移除多余空白
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+", " ");

        return result.Trim();
    }

    /// <summary>
    /// 计算内容哈希（SHA256）- 使用 ArrayPool 优化
    /// </summary>
    private static string ComputeContentHash(string content)
    {
        // 优化：使用 ArrayPool 减少内存分配
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(content.Length);
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
        var hashBuffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);

        try
        {
            var byteCount = Encoding.UTF8.GetBytes(content, rentedBuffer);
            SHA256.HashData(rentedBuffer.AsSpan(0, byteCount), hashBuffer);
            return Convert.ToHexString(hashBuffer.AsSpan(0, HashBufferSize)).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
            ArrayPool<byte>.Shared.Return(hashBuffer);
        }
    }
}
