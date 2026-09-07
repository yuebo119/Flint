// Flint 静态站点生成器
// Markdown 解析器属性测试
// **Property 1: Markdown 解析往返一致性**
// **验证: 需求 2.11**

using Flint.Core.Content;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// Markdown 解析器属性测试
/// 验证 Markdown 解析的语义一致性和确定性
/// </summary>
public class MarkdownParserPropertyTests
{
    private readonly MarkdownParser _parser = new();

    #region Property 1: Markdown 解析往返一致性

    /// <summary>
    /// **Property 1: Markdown 解析往返一致性 - 解析确定性**
    /// 对于任意有效的 Markdown 文本，多次解析应该产生相同的 HTML 输出
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property ToHtml_ShouldBeDeterministic(ValidMarkdown validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;

        // Act - 多次解析同一 Markdown
        var html1 = _parser.ToHtml(markdown);
        var html2 = _parser.ToHtml(markdown);
        var html3 = _parser.ToHtml(markdown);

        // Assert - 所有输出应该完全相同
        return (html1 == html2 && html2 == html3)
            .ToProperty()
            .Label($"解析确定性: 输入长度={markdown.Length}, 输出长度={html1.Length}");
    }

    /// <summary>
    /// **Property 1: Markdown 解析往返一致性 - 标题语义保持**
    /// 对于任意包含标题的 Markdown，提取的标题应该与原始标题文本一致
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property ExtractHeadings_ShouldPreserveHeadingText(ValidMarkdownWithHeadings validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;
        var expectedHeadings = validMd.ExpectedHeadings;

        // Act
        var extractedHeadings = _parser.ExtractHeadings(markdown);

        // Assert - 提取的标题数量和文本应该与预期一致
        if (extractedHeadings.Count != expectedHeadings.Count)
        {
            return false.ToProperty()
                .Label($"标题数量不匹配: 期望 {expectedHeadings.Count}, 实际 {extractedHeadings.Count}");
        }

        for (int i = 0; i < expectedHeadings.Count; i++)
        {
            var expected = expectedHeadings[i];
            var actual = extractedHeadings[i];

            if (actual.Level != expected.Level || actual.Text != expected.Text)
            {
                return false.ToProperty()
                    .Label($"标题不匹配: 期望 (Level={expected.Level}, Text='{expected.Text}'), " +
                           $"实际 (Level={actual.Level}, Text='{actual.Text}')");
            }
        }

        return true.ToProperty()
            .Label($"标题语义保持: {extractedHeadings.Count} 个标题");
    }

    /// <summary>
    /// **Property 1: Markdown 解析往返一致性 - 链接语义保持**
    /// 对于任意包含链接的 Markdown，提取的链接应该与原始链接一致
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property ExtractLinks_ShouldPreserveLinkInfo(ValidMarkdownWithLinks validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;
        var expectedLinks = validMd.ExpectedLinks;

        // Act
        var extractedLinks = _parser.ExtractLinks(markdown);

        // Assert - 提取的链接应该包含所有预期的链接
        // 注意：自动链接可能会被额外提取，所以只验证预期的链接存在
        foreach (var expected in expectedLinks)
        {
            var found = extractedLinks.Any(l =>
                l.Url == expected.TargetAddress &&
                l.Text == expected.Text);

            if (!found)
            {
                return false.ToProperty()
                    .Label($"链接未找到: URL='{expected.TargetAddress}', Text='{expected.Text}'");
            }
        }

        return true.ToProperty()
            .Label($"链接语义保持: {expectedLinks.Count} 个链接");
    }

    /// <summary>
    /// **Property 1: Markdown 解析往返一致性 - 图片语义保持**
    /// 对于任意包含图片的 Markdown，提取的图片应该与原始图片一致
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property ExtractImages_ShouldPreserveImageInfo(ValidMarkdownWithImages validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;
        var expectedImages = validMd.ExpectedImages;

        // Act
        var extractedImages = _parser.ExtractImages(markdown);

        // Assert - 提取的图片数量和信息应该与预期一致
        if (extractedImages.Count != expectedImages.Count)
        {
            return false.ToProperty()
                .Label($"图片数量不匹配: 期望 {expectedImages.Count}, 实际 {extractedImages.Count}");
        }

        for (int i = 0; i < expectedImages.Count; i++)
        {
            var expected = expectedImages[i];
            var actual = extractedImages[i];

            if (actual.Src != expected.Src || actual.Alt != expected.Alt)
            {
                return false.ToProperty()
                    .Label($"图片不匹配: 期望 (Src='{expected.Src}', Alt='{expected.Alt}'), " +
                           $"实际 (Src='{actual.Src}', Alt='{actual.Alt}')");
            }
        }

        return true.ToProperty()
            .Label($"图片语义保持: {extractedImages.Count} 个图片");
    }

    /// <summary>
    /// **Property 1: Markdown 解析往返一致性 - 纯文本内容保持**
    /// 对于任意 Markdown，提取的纯文本应该包含所有原始文本内容
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property ToPlainText_ShouldPreserveTextContent(ValidMarkdownWithText validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;
        var expectedTexts = validMd.ExpectedTexts;

        // Act
        var plainText = _parser.ToPlainText(markdown);

        // Assert - 纯文本应该包含所有预期的文本片段
        foreach (var expectedText in expectedTexts)
        {
            if (!plainText.Contains(expectedText, StringComparison.Ordinal))
            {
                return false.ToProperty()
                    .Label($"文本未找到: '{expectedText}' 不在纯文本中");
            }
        }

        return true.ToProperty()
            .Label($"纯文本内容保持: {expectedTexts.Count} 个文本片段");
    }

    /// <summary>
    /// **Property 1: Markdown 解析往返一致性 - HTML 输出包含原始内容**
    /// 对于任意 Markdown，生成的 HTML 应该包含所有原始文本内容
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property ToHtml_ShouldContainOriginalContent(ValidMarkdownWithText validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;
        var expectedTexts = validMd.ExpectedTexts;

        // Act
        var html = _parser.ToHtml(markdown);

        // Assert - HTML 应该包含所有预期的文本片段
        foreach (var expectedText in expectedTexts)
        {
            // HTML 编码可能会改变某些字符，所以使用 HtmlDecode 后比较
            var decodedHtml = System.Net.WebUtility.HtmlDecode(html);
            if (!decodedHtml.Contains(expectedText, StringComparison.Ordinal))
            {
                return false.ToProperty()
                    .Label($"文本未找到: '{expectedText}' 不在 HTML 中");
            }
        }

        return true.ToProperty()
            .Label($"HTML 内容保持: {expectedTexts.Count} 个文本片段");
    }

    #endregion

    #region 字数统计和阅读时间一致性

    /// <summary>
    /// 字数统计应该是非负的
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property CountWords_ShouldBeNonNegative(ValidMarkdown validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;

        // Act
        var wordCount = _parser.CountWords(markdown);

        // Assert
        return (wordCount >= 0)
            .ToProperty()
            .Label($"字数统计非负: {wordCount}");
    }

    /// <summary>
    /// 阅读时间应该与字数成正比
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property CalculateReadingTime_ShouldBeConsistentWithWordCount(ValidMarkdown validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;

        // Act
        var wordCount = _parser.CountWords(markdown);
        var readingTime = _parser.CalculateReadingTime(markdown);

        // Assert - 如果有字数，阅读时间应该至少为 1 分钟
        if (wordCount > 0)
        {
            return (readingTime >= TimeSpan.FromMinutes(1))
                .ToProperty()
                .Label($"阅读时间一致性: {wordCount} 字, {readingTime.TotalMinutes} 分钟");
        }

        // 空内容的阅读时间应该为零
        return (readingTime == TimeSpan.Zero)
            .ToProperty()
            .Label("空内容阅读时间为零");
    }

    /// <summary>
    /// 字数统计应该是确定性的
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property CountWords_ShouldBeDeterministic(ValidMarkdown validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;

        // Act
        var count1 = _parser.CountWords(markdown);
        var count2 = _parser.CountWords(markdown);
        var count3 = _parser.CountWords(markdown);

        // Assert
        return (count1 == count2 && count2 == count3)
            .ToProperty()
            .Label($"字数统计确定性: {count1}");
    }

    #endregion

    #region 目录生成一致性

    /// <summary>
    /// 目录生成应该是确定性的
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property GenerateTableOfContents_ShouldBeDeterministic(ValidMarkdownWithHeadings validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;

        // Act
        var toc1 = _parser.GenerateTableOfContents(markdown);
        var toc2 = _parser.GenerateTableOfContents(markdown);

        // Assert
        return (toc1 == toc2)
            .ToProperty()
            .Label($"目录生成确定性: 长度={toc1.Length}");
    }

    /// <summary>
    /// 目录应该包含所有符合级别限制的标题
    /// **验证: 需求 2.11**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(ValidMarkdownArbitrary)])]
    public Property GenerateTableOfContents_ShouldContainHeadingsWithinLevel(ValidMarkdownWithHeadings validMd)
    {
        ArgumentNullException.ThrowIfNull(validMd);

        // Arrange
        var markdown = validMd.Value;
        var expectedHeadings = validMd.ExpectedHeadings;
        const int maxLevel = 3;

        // Act
        var toc = _parser.GenerateTableOfContents(markdown, maxLevel);

        // Assert - 目录应该包含所有级别 <= maxLevel 的标题文本
        foreach (var heading in expectedHeadings.Where(h => h.Level <= maxLevel))
        {
            // 目录中的标题文本应该被 HTML 编码
            var encodedText = System.Net.WebUtility.HtmlEncode(heading.Text);
            if (!toc.Contains(encodedText, StringComparison.Ordinal))
            {
                return false.ToProperty()
                    .Label($"目录缺少标题: Level={heading.Level}, Text='{heading.Text}'");
            }
        }

        return true.ToProperty()
            .Label($"目录包含所有标题: {expectedHeadings.Count(h => h.Level <= maxLevel)} 个");
    }

    #endregion
}
