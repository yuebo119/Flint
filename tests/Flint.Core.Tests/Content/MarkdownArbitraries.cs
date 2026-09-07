// Flint 静态站点生成器
// Markdown 属性测试生成器

using FsCheck;
using FsCheck.Fluent;

namespace Flint.Core.Tests.Content;

/// <summary>
/// 有效 Markdown 包装类型
/// </summary>
public sealed class ValidMarkdown
{
    public string Value { get; }

    public ValidMarkdown(string value)
    {
        Value = value;
    }

    public override string ToString() =>
        $"Markdown(Length={Value.Length}, Preview='{(Value.Length > 50 ? Value[..50] + "..." : Value)}')";
}

/// <summary>
/// 带标题的有效 Markdown 包装类型
/// </summary>
public sealed class ValidMarkdownWithHeadings
{
    public string Value { get; }
    public IReadOnlyList<ExpectedHeading> ExpectedHeadings { get; }

    public ValidMarkdownWithHeadings(string value, IReadOnlyList<ExpectedHeading> expectedHeadings)
    {
        Value = value;
        ExpectedHeadings = expectedHeadings;
    }

    public override string ToString() =>
        $"MarkdownWithHeadings(Headings={ExpectedHeadings.Count})";
}

/// <summary>
/// 带链接的有效 Markdown 包装类型
/// </summary>
public sealed class ValidMarkdownWithLinks
{
    public string Value { get; }
    public IReadOnlyList<ExpectedLink> ExpectedLinks { get; }

    public ValidMarkdownWithLinks(string value, IReadOnlyList<ExpectedLink> expectedLinks)
    {
        Value = value;
        ExpectedLinks = expectedLinks;
    }

    public override string ToString() =>
        $"MarkdownWithLinks(Links={ExpectedLinks.Count})";
}

/// <summary>
/// 带图片的有效 Markdown 包装类型
/// </summary>
public sealed class ValidMarkdownWithImages
{
    public string Value { get; }
    public IReadOnlyList<ExpectedImage> ExpectedImages { get; }

    public ValidMarkdownWithImages(string value, IReadOnlyList<ExpectedImage> expectedImages)
    {
        Value = value;
        ExpectedImages = expectedImages;
    }

    public override string ToString() =>
        $"MarkdownWithImages(Images={ExpectedImages.Count})";
}

/// <summary>
/// 带文本内容的有效 Markdown 包装类型
/// </summary>
public sealed class ValidMarkdownWithText
{
    public string Value { get; }
    public IReadOnlyList<string> ExpectedTexts { get; }

    public ValidMarkdownWithText(string value, IReadOnlyList<string> expectedTexts)
    {
        Value = value;
        ExpectedTexts = expectedTexts;
    }

    public override string ToString() =>
        $"MarkdownWithText(Texts={ExpectedTexts.Count})";
}

/// <summary>
/// 预期的标题信息
/// </summary>
public sealed class ExpectedHeading
{
    public int Level { get; }
    public string Text { get; }

    public ExpectedHeading(int level, string text)
    {
        Level = level;
        Text = text;
    }
}

/// <summary>
/// 预期的链接信息
/// </summary>
public sealed class ExpectedLink
{
    public string TargetAddress { get; }
    public string Text { get; }

    public ExpectedLink(string targetAddress, string text)
    {
        TargetAddress = targetAddress;
        Text = text;
    }
}

/// <summary>
/// 预期的图片信息
/// </summary>
public sealed class ExpectedImage
{
    public string Src { get; }
    public string Alt { get; }

    public ExpectedImage(string src, string alt)
    {
        Src = src;
        Alt = alt;
    }
}

/// <summary>
/// 有效 Markdown 生成器
/// 生成符合规范的 Markdown 文本用于属性测试
/// </summary>
public static class ValidMarkdownArbitrary
{
    #region 基础文本生成器

    /// <summary>
    /// 生成简单的单词
    /// </summary>
    private static Gen<string> GenWord()
    {
        return Gen.Elements(
            "Hello", "World", "Test", "Example", "Sample",
            "Document", "Content", "Article", "Post", "Page",
            "Introduction", "Overview", "Summary", "Details", "Conclusion",
            "First", "Second", "Third", "Important", "Note");
    }

    /// <summary>
    /// 生成中文单词
    /// </summary>
    private static Gen<string> GenChineseWord()
    {
        return Gen.Elements(
            "你好", "世界", "测试", "示例", "文档",
            "内容", "文章", "页面", "介绍", "概述",
            "总结", "详情", "结论", "重要", "注意");
    }

    /// <summary>
    /// 生成简单的句子
    /// </summary>
    private static Gen<string> GenSentence()
    {
        return Gen.Choose(3, 8).SelectMany(wordCount =>
            Gen.ListOf<string>(GenWord(), wordCount).Select(words =>
                string.Join(" ", words) + "."));
    }

    /// <summary>
    /// 生成段落
    /// </summary>
    private static Gen<string> GenParagraph()
    {
        return Gen.Choose(1, 3).SelectMany(sentenceCount =>
            Gen.ListOf<string>(GenSentence(), sentenceCount).Select(sentences =>
                string.Join(" ", sentences)));
    }

    #endregion

    #region Markdown 元素生成器

    /// <summary>
    /// 生成标题级别
    /// </summary>
    private static Gen<int> GenHeadingLevel()
    {
        return Gen.Choose(1, 6);
    }

    /// <summary>
    /// 生成标题文本
    /// </summary>
    private static Gen<string> GenHeadingText()
    {
        return Gen.Elements(
            "Introduction",
            "Getting Started",
            "Overview",
            "Installation",
            "Configuration",
            "Usage",
            "Examples",
            "API Reference",
            "Troubleshooting",
            "FAQ",
            "Conclusion",
            "Summary",
            "Next Steps",
            "Related Topics",
            "Appendix");
    }

    /// <summary>
    /// 生成标题 Markdown
    /// </summary>
    private static Gen<(string Markdown, ExpectedHeading Heading)> GenHeading()
    {
        return from level in GenHeadingLevel()
               from text in GenHeadingText()
               let prefix = new string('#', level)
               let markdown = $"{prefix} {text}"
               select (markdown, new ExpectedHeading(level, text));
    }

    /// <summary>
    /// 生成链接 URL
    /// </summary>
    private static Gen<string> GenLinkUrl()
    {
        return Gen.Elements(
            "https://example.com",
            "https://github.com",
            "https://docs.microsoft.com",
            "/about",
            "/contact",
            "/docs/guide",
            "#section-1",
            "mailto:test@example.com");
    }

    /// <summary>
    /// 生成链接文本
    /// </summary>
    private static Gen<string> GenLinkText()
    {
        return Gen.Elements(
            "Click here",
            "Learn more",
            "Documentation",
            "GitHub",
            "Contact us",
            "Read more",
            "Visit site",
            "Download");
    }

    /// <summary>
    /// 生成链接 Markdown
    /// </summary>
    private static Gen<(string Markdown, ExpectedLink Link)> GenLink()
    {
        return from linkTarget in GenLinkUrl()
               from text in GenLinkText()
               let markdown = $"[{text}]({linkTarget})"
               select (markdown, new ExpectedLink(linkTarget, text));
    }

    /// <summary>
    /// 生成图片 URL
    /// </summary>
    private static Gen<string> GenImageSrc()
    {
        return Gen.Elements(
            "image.png",
            "photo.jpg",
            "diagram.svg",
            "screenshot.png",
            "logo.png",
            "banner.jpg",
            "icon.svg",
            "avatar.png");
    }

    /// <summary>
    /// 生成图片替代文本
    /// </summary>
    private static Gen<string> GenImageAlt()
    {
        return Gen.Elements(
            "Example image",
            "Screenshot",
            "Diagram",
            "Logo",
            "Banner",
            "Icon",
            "Avatar",
            "Photo");
    }

    /// <summary>
    /// 生成图片 Markdown
    /// </summary>
    private static Gen<(string Markdown, ExpectedImage Image)> GenImage()
    {
        return from src in GenImageSrc()
               from alt in GenImageAlt()
               let markdown = $"![{alt}]({src})"
               select (markdown, new ExpectedImage(src, alt));
    }

    /// <summary>
    /// 生成代码块
    /// </summary>
    private static Gen<string> GenCodeBlock()
    {
        var languages = Gen.Elements("csharp", "javascript", "python", "json", "yaml", "");
        var codeContent = Gen.Elements(
            "var x = 1;",
            "console.log('hello');",
            "print('world')",
            "{ \"key\": \"value\" }",
            "name: test");

        return from lang in languages
               from code in codeContent
               select $"```{lang}\n{code}\n```";
    }

    /// <summary>
    /// 生成列表项
    /// </summary>
    private static Gen<string> GenListItem()
    {
        return GenWord().Select(word => $"- {word}");
    }

    /// <summary>
    /// 生成无序列表
    /// </summary>
    private static Gen<string> GenUnorderedList()
    {
        return Gen.Choose(2, 5).SelectMany(count =>
            Gen.ListOf<string>(GenListItem(), count).Select(items =>
                string.Join("\n", items)));
    }

    /// <summary>
    /// 生成引用块
    /// </summary>
    private static Gen<string> GenBlockquote()
    {
        return GenSentence().Select(sentence => $"> {sentence}");
    }

    /// <summary>
    /// 生成粗体文本
    /// </summary>
    private static Gen<string> GenBoldText()
    {
        return GenWord().Select(word => $"**{word}**");
    }

    /// <summary>
    /// 生成斜体文本
    /// </summary>
    private static Gen<string> GenItalicText()
    {
        return GenWord().Select(word => $"*{word}*");
    }

    /// <summary>
    /// 生成行内代码
    /// </summary>
    private static Gen<string> GenInlineCode()
    {
        return Gen.Elements("code", "function", "variable", "class", "method")
            .Select(code => $"`{code}`");
    }

    #endregion

    #region Arbitrary 实现

    /// <summary>
    /// 生成有效的 Markdown 文本
    /// </summary>
    public static Arbitrary<ValidMarkdown> ValidMarkdown()
    {
        var gen = Gen.Choose(1, 5).SelectMany(elementCount =>
        {
            var elements = new List<Gen<string>>
            {
                GenParagraph(),
                GenHeading().Select(h => h.Markdown),
                GenCodeBlock(),
                GenUnorderedList(),
                GenBlockquote()
            };

            return Gen.ListOf<string>(Gen.OneOf(elements), elementCount)
                .Select(parts => new ValidMarkdown(string.Join("\n\n", parts)));
        });

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带标题的 Markdown
    /// </summary>
    public static Arbitrary<ValidMarkdownWithHeadings> ValidMarkdownWithHeadings()
    {
        var gen = Gen.Choose(1, 5).SelectMany(headingCount =>
            Gen.ListOf<(string Markdown, ExpectedHeading Heading)>(GenHeading(), headingCount).SelectMany(headingsList =>
                Gen.ListOf<string>(GenParagraph(), headingCount).Select(paragraphsList =>
                {
                    var headings = headingsList.ToList();
                    var paragraphs = paragraphsList.ToList();
                    var parts = new List<string>();
                    var expectedHeadings = new List<ExpectedHeading>();

                    for (int i = 0; i < headings.Count; i++)
                    {
                        parts.Add(headings[i].Markdown);
                        expectedHeadings.Add(headings[i].Heading);
                        if (i < paragraphs.Count)
                        {
                            parts.Add(paragraphs[i]);
                        }
                    }

                    return new ValidMarkdownWithHeadings(
                        string.Join("\n\n", parts),
                        expectedHeadings);
                })));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带链接的 Markdown
    /// </summary>
    public static Arbitrary<ValidMarkdownWithLinks> ValidMarkdownWithLinks()
    {
        var gen = Gen.Choose(1, 4).SelectMany(linkCount =>
            Gen.ListOf<(string Markdown, ExpectedLink Link)>(GenLink(), linkCount).SelectMany(linksList =>
                Gen.ListOf<string>(GenSentence(), linkCount).Select(sentencesList =>
                {
                    var links = linksList.ToList();
                    var sentences = sentencesList.ToList();
                    var parts = new List<string>();
                    var expectedLinks = new List<ExpectedLink>();

                    for (int i = 0; i < links.Count; i++)
                    {
                        var sentence = i < sentences.Count ? sentences[i] : "Some text.";
                        parts.Add($"{sentence} {links[i].Markdown}");
                        expectedLinks.Add(links[i].Link);
                    }

                    return new ValidMarkdownWithLinks(
                        string.Join("\n\n", parts),
                        expectedLinks);
                })));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带图片的 Markdown
    /// </summary>
    public static Arbitrary<ValidMarkdownWithImages> ValidMarkdownWithImages()
    {
        var gen = Gen.Choose(1, 3).SelectMany(imageCount =>
            Gen.ListOf<(string Markdown, ExpectedImage Image)>(GenImage(), imageCount).SelectMany(imagesList =>
                Gen.ListOf<string>(GenSentence(), imageCount).Select(sentencesList =>
                {
                    var images = imagesList.ToList();
                    var sentences = sentencesList.ToList();
                    var parts = new List<string>();
                    var expectedImages = new List<ExpectedImage>();

                    for (int i = 0; i < images.Count; i++)
                    {
                        var sentence = i < sentences.Count ? sentences[i] : "Some text.";
                        parts.Add($"{sentence}\n\n{images[i].Markdown}");
                        expectedImages.Add(images[i].Image);
                    }

                    return new ValidMarkdownWithImages(
                        string.Join("\n\n", parts),
                        expectedImages);
                })));

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成带文本内容的 Markdown
    /// </summary>
    public static Arbitrary<ValidMarkdownWithText> ValidMarkdownWithText()
    {
        var gen = Gen.Choose(2, 5).SelectMany(textCount =>
            Gen.ListOf<string>(GenWord(), textCount).Select(words =>
            {
                var parts = new List<string>();
                var expectedTexts = new List<string>();

                foreach (var word in words)
                {
                    parts.Add($"This is about {word}.");
                    expectedTexts.Add(word);
                }

                return new ValidMarkdownWithText(
                    string.Join("\n\n", parts),
                    expectedTexts);
            }));

        return gen.ToArbitrary();
    }

    #endregion
}
