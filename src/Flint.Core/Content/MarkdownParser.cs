// Flint 静态站点生成器
// Markdown 解析器实现

using System.Text;
using Flint.Core.Abstractions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Flint.Core.Content;

/// <summary>
/// Markdown 解析器实现
/// 使用 Markdig 库，支持 CommonMark、GFM 扩展、代码高亮和数学公式
/// </summary>
public sealed partial class MarkdownParser : IMarkdownParser
{
    /// <summary>
    /// 默认每分钟阅读字数
    /// </summary>
    private const int DefaultWordsPerMinute = 200;

    /// <summary>
    /// 中文每分钟阅读字数（中文阅读速度通常较慢）
    /// </summary>
    private const int ChineseWordsPerMinute = 300;

    /// <summary>
    /// Markdig 管道（缓存以提高性能）
    /// </summary>
    private readonly MarkdownPipeline _pipeline;

    /// <summary>
    /// 纯文本渲染管道
    /// </summary>
    private readonly MarkdownPipeline _plainTextPipeline;

    /// <summary>
    /// 轻量级管道（用于快速渲染）
    /// </summary>
    private static readonly MarkdownPipeline LightweightPipeline = CreateLightweightPipeline();

    /// <summary>
    /// 完整管道（懒加载，按需创建）
    /// </summary>
    private static readonly Lazy<MarkdownPipeline> FullPipeline = new(CreateFullPipeline);

    /// <summary>
    /// 是否启用自动管道选择
    /// </summary>
    private readonly bool _autoSelectPipeline;

    /// <summary>
    /// 渲染钩子（render hooks）；null 时走 Markdown.ToHtml 默认路径
    /// </summary>
    private readonly RenderHooks? _renderHooks;

    /// <summary>
    /// 创建 Markdown 解析器实例
    /// </summary>
    public MarkdownParser() : this(CreateDefaultPipeline(), autoSelectPipeline: true)
    {
    }

    /// <summary>
    /// 创建 Markdown 解析器实例（可选择轻量级模式）
    /// </summary>
    /// <param name="useLightweight">是否使用轻量级管道</param>
    public MarkdownParser(bool useLightweight)
        : this(useLightweight ? LightweightPipeline : CreateDefaultPipeline(), autoSelectPipeline: false)
    {
    }

    /// <summary>
    /// 使用自定义管道创建 Markdown 解析器实例
    /// </summary>
    /// <param name="pipeline">Markdig 管道</param>
    public MarkdownParser(MarkdownPipeline pipeline) : this(pipeline, autoSelectPipeline: false)
    {
    }

    /// <summary>
    /// 创建带渲染钩子的 Markdown 解析器实例
    /// （layouts/_markup/render-*.html 存在时由站点装配点构造）
    /// </summary>
    /// <remarks>
    /// 钩子渲染走"Parse + HtmlRenderer + 渲染器替换"路径；autoSelect 关闭——
    /// 内容触发全管道切换会绕开钩子路径，二者暂不并存（边界已声明）
    /// </remarks>
    /// <param name="renderHooks">渲染钩子集合（HasAny 为 true）</param>
    public MarkdownParser(RenderHooks renderHooks)
        : this(CreateDefaultPipeline(), autoSelectPipeline: false)
    {
        _renderHooks = renderHooks ?? throw new ArgumentNullException(nameof(renderHooks));
    }

    /// <summary>
    /// 内部构造函数
    /// </summary>
    private MarkdownParser(MarkdownPipeline pipeline, bool autoSelectPipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _plainTextPipeline = CreatePlainTextPipeline();
        _autoSelectPipeline = autoSelectPipeline;
    }

    /// <summary>
    /// 创建默认的 Markdig 管道（轻量级，用于大多数内容）
    /// </summary>
    private static MarkdownPipeline CreateDefaultPipeline()
    {
        return new MarkdownPipelineBuilder()
            // 自动生成标题 ID（用于锚点链接）
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            // 表格支持（常用）
            .UsePipeTables()
            // 任务列表支持（常用）
            .UseTaskLists()
            // 删除线支持（常用）
            .UseEmphasisExtras()
            // 自动链接（常用）
            .UseAutoLinks()
            // YAML Front Matter 支持
            .UseYamlFrontMatter()
            .Build();
    }

    /// <summary>
    /// 创建完整的 Markdig 管道（包含所有扩展，按需使用）
    /// </summary>
    private static MarkdownPipeline CreateFullPipeline()
    {
        return new MarkdownPipelineBuilder()
            // CommonMark 基础支持（默认启用）
            // GFM 扩展
            .UseAdvancedExtensions()
            // 自动生成标题 ID（用于锚点链接）
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            // 表格支持
            .UsePipeTables()
            // 任务列表支持
            .UseTaskLists()
            // 删除线支持
            .UseEmphasisExtras()
            // 数学公式支持（KaTeX）
            .UseMathematics()
            // 自动链接
            .UseAutoLinks()
            // 脚注支持
            .UseFootnotes()
            // 缩写支持
            .UseAbbreviations()
            // 引用块支持
            .UseCustomContainers()
            // 定义列表支持
            .UseDefinitionLists()
            // 图表支持
            .UseFigures()
            // 网格表格支持
            .UseGridTables()
            // 媒体链接支持
            .UseMediaLinks()
            // 智能引号
            .UseSmartyPants()
            // YAML Front Matter 支持（虽然我们单独处理，但保留兼容性）
            .UseYamlFrontMatter()
            // 启用精确源码位置追踪
            .UsePreciseSourceLocation()
            .Build();
    }

    /// <summary>
    /// 创建轻量级 Markdig 管道
    /// 只启用最常用的扩展，提升解析速度
    /// </summary>
    private static MarkdownPipeline CreateLightweightPipeline()
    {
        return new MarkdownPipelineBuilder()
            // 自动生成标题 ID（用于锚点链接）
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            // 表格支持（常用）
            .UsePipeTables()
            // 任务列表支持（常用）
            .UseTaskLists()
            // 自动链接（常用）
            .UseAutoLinks()
            // YAML Front Matter 支持
            .UseYamlFrontMatter()
            .Build();
    }

    /// <summary>
    /// 创建纯文本渲染管道
    /// </summary>
    private static MarkdownPipeline CreatePlainTextPipeline()
    {
        // 优化：使用更轻量的管道
        return new MarkdownPipelineBuilder()
            .UsePipeTables()
            .Build();
    }

    /// <inheritdoc />
    public string ToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        // P4 优化：根据内容特征自动选择管道
        var pipeline = _autoSelectPipeline && NeedsFullPipeline(markdown)
            ? FullPipeline.Value
            : _pipeline;

        // 渲染钩子路径（T5.1）：Parse 后在 per-call HtmlRenderer 上替换链接/标题渲染器，
        // 既有扩展的渲染注册经 pipeline.Setup 保留
        if (_renderHooks is not null)
        {
            return RenderWithHooks(markdown, pipeline, _renderHooks);
        }

        return Markdown.ToHtml(markdown, pipeline);
    }

    private static string RenderWithHooks(string markdown, MarkdownPipeline pipeline, RenderHooks hooks)
    {
        var document = Markdown.Parse(markdown, pipeline);
        return RenderDocumentWithHooks(document, pipeline, hooks);
    }

    private static string RenderDocumentWithHooks(Markdig.Syntax.MarkdownDocument document, MarkdownPipeline pipeline, RenderHooks hooks)
    {
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);

        if (hooks.Link is not null || hooks.Image is not null)
        {
            var defaultLink = renderer.ObjectRenderers.FirstOrDefault(r => r is LinkInlineRenderer);
            if (defaultLink is not null)
            {
                renderer.ObjectRenderers.Remove(defaultLink);
            }

            renderer.ObjectRenderers.Insert(0, new HookedLinkRenderer(hooks));
        }

        if (hooks.Heading is not null)
        {
            var defaultHeading = renderer.ObjectRenderers.FirstOrDefault(r => r is HeadingRenderer);
            if (defaultHeading is not null)
            {
                renderer.ObjectRenderers.Remove(defaultHeading);
            }

            renderer.ObjectRenderers.Insert(0, new HookedHeadingRenderer(hooks));
        }

        // codeblock hook（对齐 Hugo render-codeblock）：语言专属与通用钩子共存时
        // 由 HookedCodeBlockRenderer 内部按 identifier 分派；注册后移除默认
        // CodeBlockRenderer（pre/code 输出），未命中 hook 时回退默认渲染
        if (hooks.CodeBlock is not null || hooks.CodeBlockByLang.Count > 0)
        {
            var defaultCode = renderer.ObjectRenderers.FirstOrDefault(r => r is CodeBlockRenderer);
            if (defaultCode is not null)
            {
                renderer.ObjectRenderers.Remove(defaultCode);
            }

            renderer.ObjectRenderers.Insert(0, new HookedCodeBlockRenderer(hooks));
        }

        renderer.Render(document);
        return writer.ToString();
    }

    /// <inheritdoc />
    public string ToHtml(ReadOnlySpan<char> markdown)
    {
        if (markdown.IsEmpty)
        {
            return string.Empty;
        }

        // Markdig 目前不直接支持 Span，需要转换为字符串
        // 未来可以考虑优化
        return ToHtml(markdown.ToString());
    }

    /// <summary>
    /// P4 优化：检测内容是否需要完整管道
    /// </summary>
    private static bool NeedsFullPipeline(string markdown)
    {
        // 快速检测是否包含需要完整管道的特性
        // 使用 Contains 进行简单检测

        // 数学公式（$...$）- 单个 $ 符号即可触发
        if (markdown.Contains('$'))
            return true;

        // 脚注（[^...）
        if (markdown.Contains("[^"))
            return true;

        // 自定义容器（:::）
        if (markdown.Contains(":::"))
            return true;

        // 缩写定义（*[...）
        if (markdown.Contains("*["))
            return true;

        // 定义列表（: 开头的行）
        if (markdown.Contains("\n: ") || markdown.StartsWith(": "))
            return true;

        // 网格表格（+---+）
        if (markdown.Contains("+---"))
            return true;

        // 图表（```mermaid）
        if (markdown.Contains("```mermaid"))
            return true;

        return false;
    }
    /// <inheritdoc />
    public IReadOnlyList<MarkdownHeading> ExtractHeadings(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        return ExtractHeadingsFromDocument(Markdown.Parse(markdown, _pipeline));
    }

    /// <summary>
    /// 从已解析的文档中提取标题
    /// </summary>
    private static IReadOnlyList<MarkdownHeading> ExtractHeadingsFromDocument(MarkdownDocument document)
    {
        var headings = new List<MarkdownHeading>();

        foreach (var block in document.Descendants<HeadingBlock>())
        {
            var text = GetInlineText(block.Inline);
            var id = GenerateHeadingId(text);

            // 尝试从 HTML 属性获取 ID（如果已生成）
            var htmlAttributes = block.TryGetAttributes();
            if (htmlAttributes?.Id != null)
            {
                id = htmlAttributes.Id;
            }

            headings.Add(new MarkdownHeading
            {
                Level = block.Level,
                Text = text,
                Id = id,
                Line = block.Line + 1 // 转换为 1-based 行号
            });
        }

        return headings;
    }

    /// <inheritdoc />
    public string ToPlainText(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var document = Markdown.Parse(markdown, _plainTextPipeline);
        var sb = new StringBuilder();

        ExtractPlainText(document, sb);

        return sb.ToString().Trim();
    }

    /// <inheritdoc />
    public MarkdownAnalysis Analyze(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return new MarkdownAnalysis
            {
                Html = string.Empty,
                Headings = [],
                Links = [],
                Images = [],
                PlainText = string.Empty,
                WordCount = 0,
                ReadingTime = TimeSpan.Zero
            };
        }

        // 单次解析复用同一文档对象：此前 HTML/标题/链接/图片/纯文本各自独立解析，
        // 同一文档在构建管线中会被完整解析 7 次
        var pipeline = _autoSelectPipeline && NeedsFullPipeline(markdown)
            ? FullPipeline.Value
            : _pipeline;
        var document = Markdown.Parse(markdown, pipeline);

        // 渲染钩子路径（T5.1）：文档已解析，直接在 per-call HtmlRenderer 上替换渲染器
        var html = _renderHooks is not null
            ? RenderDocumentWithHooks(document, pipeline, _renderHooks)
            : Markdown.ToHtml(document, pipeline);
        var headings = ExtractHeadingsFromDocument(document);
        var links = ExtractLinksFromDocument(document);
        var images = ExtractImagesFromDocument(document);

        var plainSb = new StringBuilder();
        ExtractPlainText(document, plainSb);
        var plainText = plainSb.ToString().Trim();

        var wordCount = CountWordsInText(plainText);
        var readingTime = CalculateReadingTimeFromText(plainText, wordCount, DefaultWordsPerMinute);

        return new MarkdownAnalysis
        {
            Html = html,
            Headings = headings,
            Links = links,
            Images = images,
            PlainText = plainText,
            WordCount = wordCount,
            ReadingTime = readingTime
        };
    }

    /// <summary>
    /// 递归提取纯文本
    /// </summary>
    private static void ExtractPlainText(MarkdownObject obj, StringBuilder sb)
    {
        switch (obj)
        {
            case LiteralInline literal:
                sb.Append(literal.Content);
                break;

            case CodeInline code:
                sb.Append(code.Content);
                break;

            case LineBreakInline:
                sb.Append(' ');
                break;

            case HtmlInline:
                // 忽略 HTML 内联元素
                break;

            case HtmlBlock:
                // 忽略 HTML 块
                break;

            case FencedCodeBlock fencedCode:
                // 代码块内容
                if (fencedCode.Lines.Count > 0)
                {
                    foreach (var line in fencedCode.Lines)
                    {
                        sb.Append(line.ToString());
                        sb.Append(' ');
                    }
                }
                break;

            case CodeBlock codeBlock:
                // 代码块内容
                if (codeBlock.Lines.Count > 0)
                {
                    foreach (var line in codeBlock.Lines)
                    {
                        sb.Append(line.ToString());
                        sb.Append(' ');
                    }
                }
                break;

            case ParagraphBlock paragraph:
                if (paragraph.Inline != null)
                {
                    ExtractPlainText(paragraph.Inline, sb);
                }
                sb.Append(' ');
                break;

            case HeadingBlock heading:
                if (heading.Inline != null)
                {
                    ExtractPlainText(heading.Inline, sb);
                }
                sb.Append(' ');
                break;

            case ListBlock list:
                foreach (var item in list)
                {
                    ExtractPlainText(item, sb);
                }
                break;

            case ListItemBlock listItem:
                foreach (var child in listItem)
                {
                    ExtractPlainText(child, sb);
                }
                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    ExtractPlainText(child, sb);
                }
                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    ExtractPlainText(child, sb);
                }
                break;

            case ContainerBlock containerBlock:
                foreach (var child in containerBlock)
                {
                    ExtractPlainText(child, sb);
                }
                break;
        }
    }

    /// <inheritdoc />
    public int CountWords(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return 0;
        }

        var plainText = ToPlainText(markdown);
        return CountWordsInText(plainText);
    }

    /// <summary>
    /// 统计文本中的字数
    /// 支持中英文混合统计
    /// P6 优化：使用 Span 避免字符串分配
    /// </summary>
    private static int CountWordsInText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return CountWordsInSpan(text.AsSpan());
    }

    /// <summary>
    /// P6 优化：使用 Span 统计字数
    /// </summary>
    private static int CountWordsInSpan(ReadOnlySpan<char> text)
    {
        var wordCount = 0;
        var inWord = false;
        var chineseCount = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // 检查是否为中文字符
            if (IsChinese(c))
            {
                chineseCount++;
                inWord = false;
            }
            // 检查是否为单词字符（字母、数字）
            else if (char.IsLetterOrDigit(c))
            {
                if (!inWord)
                {
                    wordCount++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        // 中文字符每个算一个字
        return wordCount + chineseCount;
    }

    /// <summary>
    /// 检查字符是否为中文
    /// </summary>
    private static bool IsChinese(char c)
    {
        // CJK 统一汉字范围。
        // 局限：CJK 扩展 B（U+20000+）超出 char.MaxValue（0xFFFF），逐 char 检查
        // 无法覆盖——生僻字不计入中文比例；如需支持须改用 Rune 处理代理对
        return c >= 0x4E00 && c <= 0x9FFF
            || c >= 0x3400 && c <= 0x4DBF  // CJK 扩展 A
            || c >= 0xF900 && c <= 0xFAFF; // CJK 兼容汉字
    }

    /// <inheritdoc />
    public TimeSpan CalculateReadingTime(string markdown, int wordsPerMinute = DefaultWordsPerMinute)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return TimeSpan.Zero;
        }

        var plainText = ToPlainText(markdown);
        return CalculateReadingTimeFromText(plainText, CountWordsInText(plainText), wordsPerMinute);
    }

    /// <summary>
    /// 基于已提取的纯文本和字数计算阅读时间
    /// </summary>
    private static TimeSpan CalculateReadingTimeFromText(string plainText, int wordCount, int wordsPerMinute)
    {
        // 检测是否主要为中文内容
        var chineseRatio = CalculateChineseRatio(plainText);
        // 调用方可传任意 wordsPerMinute，≤0 会使除法得 ±Infinity，
        // TimeSpan.FromMinutes(Infinity) 抛 OverflowException——钳制到正值
        var effectiveWpm = Math.Max(1, chineseRatio > 0.5
            ? ChineseWordsPerMinute
            : wordsPerMinute);

        var minutes = (double)wordCount / effectiveWpm;
        return TimeSpan.FromMinutes(Math.Max(1, Math.Ceiling(minutes)));
    }

    /// <summary>
    /// 计算中文字符比例
    /// P6 优化：使用 Span 避免字符串分配
    /// </summary>
    private static double CalculateChineseRatio(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return CalculateChineseRatioSpan(text.AsSpan());
    }

    /// <summary>
    /// P6 优化：使用 Span 计算中文字符比例
    /// </summary>
    private static double CalculateChineseRatioSpan(ReadOnlySpan<char> text)
    {
        var totalChars = 0;
        var chineseChars = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || IsChinese(c))
            {
                totalChars++;
                if (IsChinese(c))
                {
                    chineseChars++;
                }
            }
        }

        return totalChars > 0 ? (double)chineseChars / totalChars : 0;
    }

    /// <inheritdoc />
    public IReadOnlyList<MarkdownLink> ExtractLinks(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        return ExtractLinksFromDocument(Markdown.Parse(markdown, _pipeline));
    }

    /// <summary>
    /// 从已解析的文档中提取链接
    /// </summary>
    private static IReadOnlyList<MarkdownLink> ExtractLinksFromDocument(MarkdownDocument document)
    {
        var links = new List<MarkdownLink>();

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage)
            {
                continue; // 跳过图片
            }

            var text = GetInlineText(link);
            links.Add(new MarkdownLink
            {
                Url = link.Url ?? string.Empty,
                Text = text,
                Title = link.Title
            });
        }

        // 提取自动链接
        foreach (var autoLink in document.Descendants<AutolinkInline>())
        {
            links.Add(new MarkdownLink
            {
                Url = autoLink.Url,
                Text = autoLink.Url,
                Title = null
            });
        }

        return links;
    }

    /// <inheritdoc />
    public IReadOnlyList<MarkdownImage> ExtractImages(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return [];
        }

        return ExtractImagesFromDocument(Markdown.Parse(markdown, _pipeline));
    }

    /// <summary>
    /// 从已解析的文档中提取图片
    /// </summary>
    private static IReadOnlyList<MarkdownImage> ExtractImagesFromDocument(MarkdownDocument document)
    {
        var images = new List<MarkdownImage>();

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!link.IsImage)
            {
                continue; // 只处理图片
            }

            var alt = GetInlineText(link);
            images.Add(new MarkdownImage
            {
                Src = link.Url ?? string.Empty,
                Alt = alt,
                Title = link.Title
            });
        }

        return images;
    }

    /// <inheritdoc />
    public string GenerateTableOfContents(string markdown, int maxLevel = 3)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var headings = ExtractHeadings(markdown)
            .Where(h => h.Level <= maxLevel)
            .ToList();

        if (headings.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        sb.AppendLine("<nav class=\"toc\">");
        sb.AppendLine("<ul>");

        var minLevel = headings.Min(h => h.Level);

        foreach (var heading in headings)
        {
            var indent = new string(' ', (heading.Level - minLevel) * 2);
            sb.AppendLine(culture, $"{indent}<li><a href=\"#{heading.Id}\">{HtmlEncode(heading.Text)}</a></li>");
        }

        sb.AppendLine("</ul>");
        sb.AppendLine("</nav>");

        return sb.ToString();
    }

    /// <summary>
    /// 获取内联元素的纯文本
    /// </summary>
    private static string GetInlineText(ContainerInline? inline)
    {
        if (inline == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var child in inline)
        {
            switch (child)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content);
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline container:
                    sb.Append(GetInlineText(container));
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 生成标题 ID（GitHub 风格）
    /// P6 优化：使用 Span 和 stackalloc 减少内存分配
    /// </summary>
    private static string GenerateHeadingId(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // P6 优化：使用 stackalloc 避免堆分配（对于较短的标题）
        var maxLength = text.Length * 2; // 最坏情况：每个字符变成连字符
        Span<char> buffer = maxLength <= 256
            ? stackalloc char[maxLength]
            : new char[maxLength];

        var writeIndex = 0;
        var lastWasHyphen = true; // 避免开头的连字符

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var lower = char.ToLowerInvariant(c);

            // 保留字母、数字、中文
            if (char.IsLetterOrDigit(lower) || IsChinese(lower))
            {
                buffer[writeIndex++] = lower;
                lastWasHyphen = false;
            }
            // 空格和连字符转换为单个连字符
            else if (c == ' ' || c == '-' || c == '_')
            {
                if (!lastWasHyphen && writeIndex > 0)
                {
                    buffer[writeIndex++] = '-';
                    lastWasHyphen = true;
                }
            }
            // 其他字符忽略
        }

        // 移除末尾的连字符
        while (writeIndex > 0 && buffer[writeIndex - 1] == '-')
        {
            writeIndex--;
        }

        return writeIndex > 0 ? new string(buffer[..writeIndex]) : string.Empty;
    }

    /// <summary>
    /// HTML 编码
    /// </summary>
    private static string HtmlEncode(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }
}
