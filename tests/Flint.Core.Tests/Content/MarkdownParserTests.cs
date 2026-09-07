// Flint 静态站点生成器
// Markdown 解析器单元测试

using Flint.Core.Content;
using Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// Markdown 解析器单元测试
/// 验证 CommonMark、GFM 扩展、代码高亮和数学公式支持
/// </summary>
public class MarkdownParserTests
{
    private readonly MarkdownParser _parser;

    public MarkdownParserTests()
    {
        _parser = new MarkdownParser();
    }

    #region ToHtml 基础测试

    [Fact]
    public void ToHtml_空字符串_返回空字符串()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ToHtml_Null字符串_返回空字符串()
    {
        // Arrange
        string? markdown = null;

        // Act
        var result = _parser.ToHtml(markdown!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ToHtml_简单段落_正确转换()
    {
        // Arrange
        var markdown = "Hello, World!";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<p>Hello, World!</p>", result);
    }

    [Fact]
    public void ToHtml_标题_正确转换()
    {
        // Arrange
        var markdown = "# 一级标题\n## 二级标题\n### 三级标题";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<h1", result);
        Assert.Contains("一级标题", result);
        Assert.Contains("<h2", result);
        Assert.Contains("二级标题", result);
        Assert.Contains("<h3", result);
        Assert.Contains("三级标题", result);
    }

    [Fact]
    public void ToHtml_粗体和斜体_正确转换()
    {
        // Arrange
        var markdown = "**粗体** 和 *斜体* 以及 ***粗斜体***";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<strong>粗体</strong>", result);
        Assert.Contains("<em>斜体</em>", result);
    }

    [Fact]
    public void ToHtml_链接_正确转换()
    {
        // Arrange
        var markdown = "[链接文本](https://example.com \"链接标题\")";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<a href=\"https://example.com\"", result);
        Assert.Contains("链接文本", result);
        Assert.Contains("title=\"链接标题\"", result);
    }

    [Fact]
    public void ToHtml_图片_正确转换()
    {
        // Arrange
        var markdown = "![替代文本](image.png \"图片标题\")";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<img", result);
        Assert.Contains("src=\"image.png\"", result);
        Assert.Contains("alt=\"替代文本\"", result);
    }

    [Fact]
    public void ToHtml_无序列表_正确转换()
    {
        // Arrange
        var markdown = "- 项目一\n- 项目二\n- 项目三";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<ul>", result);
        Assert.Contains("<li>项目一</li>", result);
        Assert.Contains("<li>项目二</li>", result);
        Assert.Contains("<li>项目三</li>", result);
        Assert.Contains("</ul>", result);
    }

    [Fact]
    public void ToHtml_有序列表_正确转换()
    {
        // Arrange
        var markdown = "1. 第一项\n2. 第二项\n3. 第三项";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<ol>", result);
        Assert.Contains("<li>第一项</li>", result);
        Assert.Contains("<li>第二项</li>", result);
        Assert.Contains("<li>第三项</li>", result);
        Assert.Contains("</ol>", result);
    }

    [Fact]
    public void ToHtml_引用块_正确转换()
    {
        // Arrange
        var markdown = "> 这是一段引用\n> 第二行引用";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<blockquote>", result);
        Assert.Contains("这是一段引用", result);
        Assert.Contains("</blockquote>", result);
    }

    [Fact]
    public void ToHtml_行内代码_正确转换()
    {
        // Arrange
        var markdown = "使用 `Console.WriteLine()` 输出";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<code>Console.WriteLine()</code>", result);
    }

    [Fact]
    public void ToHtml_代码块_正确转换()
    {
        // Arrange
        var markdown = "```csharp\nvar x = 1;\n```";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<pre>", result);
        Assert.Contains("<code", result);
        Assert.Contains("var x = 1;", result);
    }

    [Fact]
    public void ToHtml_水平线_正确转换()
    {
        // Arrange
        var markdown = "上面的内容\n\n---\n\n下面的内容";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<hr", result);
    }

    #endregion

    #region GFM 扩展测试

    [Fact]
    public void ToHtml_GFM表格_正确转换()
    {
        // Arrange
        var markdown = @"| 列1 | 列2 | 列3 |
|-----|-----|-----|
| A   | B   | C   |
| D   | E   | F   |";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<table>", result);
        Assert.Contains("<thead>", result);
        Assert.Contains("<tbody>", result);
        Assert.Contains("<th>列1</th>", result);
        Assert.Contains("<td>A</td>", result);
    }

    [Fact]
    public void ToHtml_GFM任务列表_正确转换()
    {
        // Arrange
        var markdown = "- [x] 已完成任务\n- [ ] 未完成任务";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<input", result);
        Assert.Contains("type=\"checkbox\"", result);
        Assert.Contains("checked", result);
        Assert.Contains("已完成任务", result);
        Assert.Contains("未完成任务", result);
    }

    [Fact]
    public void ToHtml_GFM删除线_正确转换()
    {
        // Arrange
        var markdown = "~~删除的文本~~";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<del>删除的文本</del>", result);
    }

    [Fact]
    public void ToHtml_GFM自动链接_正确转换()
    {
        // Arrange
        var markdown = "访问 https://example.com 获取更多信息";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<a href=\"https://example.com\"", result);
    }

    #endregion

    #region 数学公式测试

    [Fact]
    public void ToHtml_行内数学公式_正确转换()
    {
        // Arrange
        var markdown = "爱因斯坦公式 $E=mc^2$ 很著名";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("E=mc^2", result);
        // Markdig 数学扩展会生成 <span class="math"> 或类似标记
        Assert.Contains("math", result);
    }

    [Fact]
    public void ToHtml_块级数学公式_正确转换()
    {
        // Arrange
        var markdown = @"$$
\int_{-\infty}^{\infty} e^{-x^2} dx = \sqrt{\pi}
$$";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("int", result);
        Assert.Contains("sqrt", result);
    }

    #endregion

    #region 标题提取测试

    [Fact]
    public void ExtractHeadings_空字符串_返回空列表()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.ExtractHeadings(markdown);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractHeadings_多级标题_正确提取()
    {
        // Arrange
        var markdown = @"# 一级标题
## 二级标题A
### 三级标题
## 二级标题B
# 另一个一级标题";

        // Act
        var result = _parser.ExtractHeadings(markdown);

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal(1, result[0].Level);
        Assert.Equal("一级标题", result[0].Text);
        Assert.Equal(2, result[1].Level);
        Assert.Equal("二级标题A", result[1].Text);
        Assert.Equal(3, result[2].Level);
        Assert.Equal("三级标题", result[2].Text);
    }

    [Fact]
    public void ExtractHeadings_标题ID_正确生成()
    {
        // Arrange
        var markdown = "# Hello World\n## 中文标题";

        // Act
        var result = _parser.ExtractHeadings(markdown);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.NotEmpty(result[0].Id);
        Assert.NotEmpty(result[1].Id);
    }

    [Fact]
    public void ExtractHeadings_包含特殊字符的标题_正确处理()
    {
        // Arrange
        var markdown = "# Hello `code` World!\n## 标题 (带括号)";

        // Act
        var result = _parser.ExtractHeadings(markdown);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("code", result[0].Text);
    }

    #endregion

    #region 纯文本提取测试

    [Fact]
    public void ToPlainText_空字符串_返回空字符串()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.ToPlainText(markdown);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ToPlainText_简单文本_正确提取()
    {
        // Arrange
        var markdown = "Hello, World!";

        // Act
        var result = _parser.ToPlainText(markdown);

        // Assert
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void ToPlainText_带格式的文本_去除格式()
    {
        // Arrange
        var markdown = "**粗体** 和 *斜体* 以及 `代码`";

        // Act
        var result = _parser.ToPlainText(markdown);

        // Assert
        Assert.Contains("粗体", result);
        Assert.Contains("斜体", result);
        Assert.Contains("代码", result);
        Assert.DoesNotContain("**", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("`", result);
    }

    [Fact]
    public void ToPlainText_带链接的文本_提取链接文本()
    {
        // Arrange
        var markdown = "点击 [这里](https://example.com) 访问";

        // Act
        var result = _parser.ToPlainText(markdown);

        // Assert
        Assert.Contains("这里", result);
        Assert.DoesNotContain("https://", result);
    }

    #endregion

    #region 字数统计测试

    [Fact]
    public void CountWords_空字符串_返回零()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.CountWords(markdown);

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void CountWords_英文文本_正确统计()
    {
        // Arrange
        var markdown = "Hello World, this is a test.";

        // Act
        var result = _parser.CountWords(markdown);

        // Assert
        Assert.Equal(6, result); // Hello, World, this, is, a, test
    }

    [Fact]
    public void CountWords_中文文本_正确统计()
    {
        // Arrange
        var markdown = "这是一段中文测试文本";

        // Act
        var result = _parser.CountWords(markdown);

        // Assert
        Assert.Equal(10, result); // 每个中文字符算一个字
    }

    [Fact]
    public void CountWords_中英混合文本_正确统计()
    {
        // Arrange
        var markdown = "Hello 世界，这是 test";

        // Act
        var result = _parser.CountWords(markdown);

        // Assert
        // Hello(1) + 世(1) + 界(1) + 这(1) + 是(1) + test(1) = 6
        Assert.True(result >= 6);
    }

    [Fact]
    public void CountWords_带Markdown格式_正确统计()
    {
        // Arrange
        var markdown = "**粗体** 和 *斜体*";

        // Act
        var result = _parser.CountWords(markdown);

        // Assert
        // 粗体(2) + 和(1) + 斜体(2) = 5
        Assert.Equal(5, result);
    }

    #endregion

    #region 阅读时间计算测试

    [Fact]
    public void CalculateReadingTime_空字符串_返回零()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.CalculateReadingTime(markdown);

        // Assert
        Assert.Equal(TimeSpan.Zero, result);
    }

    [Fact]
    public void CalculateReadingTime_短文本_至少一分钟()
    {
        // Arrange
        var markdown = "Hello World";

        // Act
        var result = _parser.CalculateReadingTime(markdown);

        // Assert
        Assert.True(result >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void CalculateReadingTime_长文本_正确计算()
    {
        // Arrange
        // 创建约 400 个单词的文本（默认 200 wpm，应该约 2 分钟）
        var words = string.Join(" ", Enumerable.Repeat("word", 400));

        // Act
        var result = _parser.CalculateReadingTime(words);

        // Assert
        Assert.True(result >= TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void CalculateReadingTime_自定义阅读速度_正确计算()
    {
        // Arrange
        var words = string.Join(" ", Enumerable.Repeat("word", 100));

        // Act
        var result = _parser.CalculateReadingTime(words, wordsPerMinute: 100);

        // Assert
        Assert.True(result >= TimeSpan.FromMinutes(1));
    }

    #endregion

    #region 链接提取测试

    [Fact]
    public void ExtractLinks_空字符串_返回空列表()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.ExtractLinks(markdown);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractLinks_单个链接_正确提取()
    {
        // Arrange
        var markdown = "[链接文本](https://example.com \"链接标题\")";

        // Act
        var result = _parser.ExtractLinks(markdown);

        // Assert
        Assert.Single(result);
        Assert.Equal("https://example.com", result[0].Url);
        Assert.Equal("链接文本", result[0].Text);
        Assert.Equal("链接标题", result[0].Title);
        Assert.True(result[0].IsExternal);
    }

    [Fact]
    public void ExtractLinks_多个链接_全部提取()
    {
        // Arrange
        var markdown = "[链接1](https://a.com) 和 [链接2](https://b.com)";

        // Act
        var result = _parser.ExtractLinks(markdown);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ExtractLinks_内部链接_正确识别()
    {
        // Arrange
        var markdown = "[内部链接](/about)";

        // Act
        var result = _parser.ExtractLinks(markdown);

        // Assert
        Assert.Single(result);
        Assert.False(result[0].IsExternal);
    }

    [Fact]
    public void ExtractLinks_不包含图片_正确过滤()
    {
        // Arrange
        var markdown = "[链接](https://example.com) 和 ![图片](image.png)";

        // Act
        var result = _parser.ExtractLinks(markdown);

        // Assert
        Assert.Single(result);
        Assert.Equal("链接", result[0].Text);
    }

    #endregion

    #region 图片提取测试

    [Fact]
    public void ExtractImages_空字符串_返回空列表()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.ExtractImages(markdown);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ExtractImages_单个图片_正确提取()
    {
        // Arrange
        var markdown = "![替代文本](image.png \"图片标题\")";

        // Act
        var result = _parser.ExtractImages(markdown);

        // Assert
        Assert.Single(result);
        Assert.Equal("image.png", result[0].Src);
        Assert.Equal("替代文本", result[0].Alt);
        Assert.Equal("图片标题", result[0].Title);
        Assert.False(result[0].IsExternal);
    }

    [Fact]
    public void ExtractImages_外部图片_正确识别()
    {
        // Arrange
        var markdown = "![外部图片](https://example.com/image.png)";

        // Act
        var result = _parser.ExtractImages(markdown);

        // Assert
        Assert.Single(result);
        Assert.True(result[0].IsExternal);
    }

    [Fact]
    public void ExtractImages_多个图片_全部提取()
    {
        // Arrange
        var markdown = "![图片1](a.png) 和 ![图片2](b.png)";

        // Act
        var result = _parser.ExtractImages(markdown);

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region 目录生成测试

    [Fact]
    public void GenerateTableOfContents_空字符串_返回空字符串()
    {
        // Arrange
        var markdown = string.Empty;

        // Act
        var result = _parser.GenerateTableOfContents(markdown);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GenerateTableOfContents_无标题_返回空字符串()
    {
        // Arrange
        var markdown = "这是一段没有标题的文本。";

        // Act
        var result = _parser.GenerateTableOfContents(markdown);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GenerateTableOfContents_有标题_生成目录()
    {
        // Arrange
        var markdown = @"# 标题一
## 标题二
### 标题三";

        // Act
        var result = _parser.GenerateTableOfContents(markdown);

        // Assert
        Assert.Contains("<nav class=\"toc\">", result);
        Assert.Contains("<ul>", result);
        Assert.Contains("<li>", result);
        Assert.Contains("标题一", result);
        Assert.Contains("标题二", result);
        Assert.Contains("标题三", result);
        Assert.Contains("href=\"#", result);
    }

    [Fact]
    public void GenerateTableOfContents_限制级别_正确过滤()
    {
        // Arrange
        var markdown = @"# 标题一
## 标题二
### 标题三
#### 标题四";

        // Act
        var result = _parser.GenerateTableOfContents(markdown, maxLevel: 2);

        // Assert
        Assert.Contains("标题一", result);
        Assert.Contains("标题二", result);
        Assert.DoesNotContain("标题三", result);
        Assert.DoesNotContain("标题四", result);
    }

    #endregion

    #region Span 重载测试

    [Fact]
    public void ToHtml_Span重载_正确转换()
    {
        // Arrange
        var markdown = "Hello, World!".AsSpan();

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<p>Hello, World!</p>", result);
    }

    [Fact]
    public void ToHtml_空Span_返回空字符串()
    {
        // Arrange
        var markdown = ReadOnlySpan<char>.Empty;

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region 复杂文档测试

    [Fact]
    public void ToHtml_复杂文档_正确转换()
    {
        // Arrange
        var markdown = @"# 文档标题

这是一段介绍文字，包含 **粗体** 和 *斜体*。

## 代码示例

```csharp
public class Hello
{
    public void World() => Console.WriteLine(""Hello, World!"");
}
```

## 列表

- 项目一
- 项目二
  - 子项目
- 项目三

## 表格

| 名称 | 值 |
|------|-----|
| A    | 1   |
| B    | 2   |

## 数学公式

行内公式：$E=mc^2$

块级公式：

$$
\sum_{i=1}^{n} i = \frac{n(n+1)}{2}
$$

## 链接和图片

[访问网站](https://example.com)

![示例图片](example.png)

> 这是一段引用

---

结束。";

        // Act
        var result = _parser.ToHtml(markdown);

        // Assert
        Assert.Contains("<h1", result);
        Assert.Contains("<h2", result);
        Assert.Contains("<strong>粗体</strong>", result);
        Assert.Contains("<em>斜体</em>", result);
        Assert.Contains("<pre>", result);
        Assert.Contains("<code", result);
        Assert.Contains("<ul>", result);
        Assert.Contains("<li>", result);
        Assert.Contains("<table>", result);
        Assert.Contains("<a href=", result);
        Assert.Contains("<img", result);
        Assert.Contains("<blockquote>", result);
        Assert.Contains("<hr", result);
    }

    #endregion
}
