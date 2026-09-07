// Flint 静态站点生成器
// 内容解析边界条件测试
// 测试空内容、只有 Front Matter、超大文件、特殊 Unicode、无效 UTF-8、深层嵌套等边界条件
// _Requirements: 5.1, 8.1_

using System.Text;
using Flint.Core.Content;
using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Content;

/// <summary>
/// 内容解析边界条件测试
/// 验证 ContentParser 在各种边界条件下的行为
/// 确保系统在边界条件下不会崩溃，并正确处理错误
/// </summary>
public class ContentParsingEdgeCasesTests : IDisposable
{
    private readonly ContentParser _parser;
    private readonly FrontMatterParser _frontMatterParser;
    private readonly string _tempDir;

    public ContentParsingEdgeCasesTests()
    {
        _parser = new ContentParser();
        _frontMatterParser = new FrontMatterParser();
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-edge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }


    #region 辅助方法

    /// <summary>
    /// 创建临时内容文件
    /// </summary>
    private ContentFile CreateContentFile(string content, string fileName = "test.md")
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, content, Encoding.UTF8);
        return new ContentFile
        {
            Path = filePath,
            RawContent = Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.Now
        };
    }

    /// <summary>
    /// 创建临时内容文件（使用字节数组）
    /// </summary>
    private ContentFile CreateContentFileFromBytes(byte[] content, string fileName = "test.md")
    {
        var filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(filePath, content);
        return new ContentFile
        {
            Path = filePath,
            RawContent = content,
            ModifiedTime = DateTimeOffset.Now
        };
    }

    /// <summary>
    /// 创建带有 YAML Front Matter 的内容
    /// </summary>
    private static string CreateYamlContent(string title, string markdownBody = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: \"{EscapeYamlString(title)}\"");
        sb.AppendLine($"date: {DateTimeOffset.Now:yyyy-MM-ddTHH:mm:sszzz}");
        sb.AppendLine("draft: false");
        sb.AppendLine("---");
        if (!string.IsNullOrEmpty(markdownBody))
        {
            sb.AppendLine();
            sb.AppendLine(markdownBody);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 转义 YAML 字符串中的特殊字符
    /// </summary>
    private static string EscapeYamlString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    #endregion


    #region 空内容文件测试

    /// <summary>
    /// 测试完全空的内容文件
    /// 系统应该能够处理空文件而不崩溃
    /// </summary>
    [Fact]
    public async Task ParseAsync_EmptyFile_ShouldHandleGracefully()
    {
        // Arrange
        var content = string.Empty;
        var file = CreateContentFile(content, "empty.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata.Title.Should().Be("Empty"); // 从文件名生成的默认标题
        result.HtmlContent.Should().BeEmpty();
        result.RawMarkdown.Should().BeEmpty();
        result.WordCount.Should().Be(0);
    }

    /// <summary>
    /// 测试只包含空白字符的内容文件
    /// </summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("  ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("   \n   \t   ")]
    public async Task ParseAsync_WhitespaceOnlyFile_ShouldHandleGracefully(string whitespace)
    {
        // Arrange
        var file = CreateContentFile(whitespace, "whitespace.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        result.Metadata.Title.Should().Be("Whitespace"); // 从文件名生成的默认标题
    }

    /// <summary>
    /// 测试只有换行符的内容文件
    /// </summary>
    [Theory]
    [InlineData("\n\n\n")]
    [InlineData("\r\n\r\n\r\n")]
    [InlineData("\r\r\r")]
    public async Task ParseAsync_NewlinesOnlyFile_ShouldHandleGracefully(string newlines)
    {
        // Arrange
        var file = CreateContentFile(newlines, "newlines.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
    }

    #endregion


    #region 只有 Front Matter 没有正文的文件测试

    /// <summary>
    /// 测试只有 YAML Front Matter 没有正文的文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_YamlFrontMatterOnly_NoBody_ShouldParseCorrectly()
    {
        // Arrange
        var content = """
            ---
            title: "只有元数据的文章"
            date: 2024-06-15T10:30:00+08:00
            draft: false
            tags:
              - 测试
            ---
            """;
        var file = CreateContentFile(content, "frontmatter-only.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("只有元数据的文章");
        result.Metadata.Tags.Should().Contain("测试");
        result.HtmlContent.Should().BeEmpty();
        result.RawMarkdown.Should().BeEmpty();
        result.WordCount.Should().Be(0);
    }

    /// <summary>
    /// 测试只有 TOML Front Matter 没有正文的文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_TomlFrontMatterOnly_NoBody_ShouldParseCorrectly()
    {
        // Arrange
        var content = """
            +++
            title = "TOML 元数据文章"
            date = 2024-06-15T10:30:00+08:00
            draft = false
            tags = ["测试", "TOML"]
            +++
            """;
        var file = CreateContentFile(content, "toml-frontmatter-only.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("TOML 元数据文章");
        result.Metadata.Tags.Should().HaveCount(2);
        result.HtmlContent.Should().BeEmpty();
    }

    /// <summary>
    /// 测试只有 JSON Front Matter 没有正文的文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_JsonFrontMatterOnly_NoBody_ShouldParseCorrectly()
    {
        // Arrange
        var content = """
            {
              "title": "JSON 元数据文章",
              "date": "2024-06-15T10:30:00+08:00",
              "draft": false,
              "tags": ["测试", "JSON"]
            }
            """;
        var file = CreateContentFile(content, "json-frontmatter-only.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("JSON 元数据文章");
        result.Metadata.Tags.Should().HaveCount(2);
        result.HtmlContent.Should().BeEmpty();
    }

    /// <summary>
    /// 测试 Front Matter 后只有空白的文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_FrontMatterWithTrailingWhitespace_ShouldParseCorrectly()
    {
        // Arrange
        var content = """
            ---
            title: "带尾部空白的文章"
            ---
            
            
            
            """;
        var file = CreateContentFile(content, "trailing-whitespace.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("带尾部空白的文章");
        result.HtmlContent.Should().BeEmpty();
    }

    #endregion


    #region 超大内容文件测试

    /// <summary>
    /// 测试包含大量文本的内容文件（1MB）
    /// </summary>
    [Fact]
    public async Task ParseAsync_LargeTextContent_1MB_ShouldParseCorrectly()
    {
        // Arrange
        var paragraph = "这是一段测试文本，用于测试大文件解析性能。" + new string('测', 100) + "\n\n";
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: \"大文件测试\"");
        sb.AppendLine("---");
        sb.AppendLine();

        // 生成约 1MB 的内容
        while (sb.Length < 1024 * 1024)
        {
            sb.Append(paragraph);
        }

        var content = sb.ToString();
        var file = CreateContentFile(content, "large-1mb.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("大文件测试");
        result.HtmlContent.Should().NotBeEmpty();
        result.WordCount.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试包含超长单行的内容文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_VeryLongLine_ShouldParseCorrectly()
    {
        // Arrange
        var longLine = new string('字', 100000); // 10万个字符的单行
        var content = CreateYamlContent("超长行测试", longLine);
        var file = CreateContentFile(content, "long-line.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("超长行测试");
        result.HtmlContent.Should().Contain("字");
        result.WordCount.Should().BeGreaterThan(0);
    }

    #endregion


    #region 特殊 Unicode 字符测试

    /// <summary>
    /// 测试包含中文字符的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_ChineseCharacters_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "中文标题测试",
            """
            # 中文标题
            
            这是一段中文内容，包含各种中文标点符号：，。！？、；：""''【】
            
            ## 繁体中文
            
            這是繁體中文內容。
            
            ## 日文混合
            
            日本語のテキストも含まれています。
            """);
        var file = CreateContentFile(content, "chinese.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("中文标题测试");
        result.HtmlContent.Should().Contain("中文标题");
        result.HtmlContent.Should().Contain("繁體中文");
        result.HtmlContent.Should().Contain("日本語");
    }

    /// <summary>
    /// 测试包含 Emoji 字符的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_EmojiCharacters_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "Emoji 测试 🎉",
            """
            # 欢迎 👋
            
            这是一篇包含 Emoji 的文章 😀🎉🚀
            
            ## 各种 Emoji
            
            - 表情：😀😃😄😁😆😅🤣😂
            - 动物：🐶🐱🐭🐹🐰🦊🐻🐼
            - 食物：🍎🍐🍊🍋🍌🍉🍇🍓
            - 活动：⚽🏀🏈⚾🎾🏐🏉🎱
            - 旗帜：🇨🇳🇺🇸🇯🇵🇬🇧🇫🇷🇩🇪
            
            ## 复合 Emoji
            
            👨‍👩‍👧‍👦 家庭
            👩‍💻 女程序员
            🏳️‍🌈 彩虹旗
            """);
        var file = CreateContentFile(content, "emoji.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("👋");
        result.HtmlContent.Should().Contain("😀");
        result.HtmlContent.Should().Contain("🐶");
    }

    /// <summary>
    /// 测试包含阿拉伯文（RTL）的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_ArabicRTL_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "阿拉伯文测试",
            """
            # 阿拉伯文内容
            
            مرحبا بالعالم
            
            هذا نص باللغة العربية
            
            ## 混合内容
            
            English text mixed with العربية text.
            """);
        var file = CreateContentFile(content, "arabic.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("مرحبا");
        result.HtmlContent.Should().Contain("العربية");
    }

    /// <summary>
    /// 测试包含希伯来文的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_HebrewText_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "希伯来文测试",
            """
            # 希伯来文内容
            
            שלום עולם
            
            זהו טקסט בעברית
            """);
        var file = CreateContentFile(content, "hebrew.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("שלום");
    }

    /// <summary>
    /// 测试包含特殊 Unicode 符号的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_SpecialUnicodeSymbols_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "特殊符号测试",
            """
            # 特殊 Unicode 符号
            
            ## 数学符号
            
            ∀x∈ℝ: x² ≥ 0
            
            ∑(i=1 to n) = n(n+1)/2
            
            ∫f(x)dx = F(x) + C
            
            ## 货币符号
            
            $ € £ ¥ ₹ ₽ ₿
            
            ## 箭头符号
            
            → ← ↑ ↓ ↔ ⇒ ⇐ ⇔
            
            ## 其他符号
            
            © ® ™ § ¶ † ‡ • ‣ ※
            
            ## 音乐符号
            
            ♩ ♪ ♫ ♬ 𝄞 𝄢
            
            ## 国际象棋
            
            ♔ ♕ ♖ ♗ ♘ ♙ ♚ ♛ ♜ ♝ ♞ ♟
            """);
        var file = CreateContentFile(content, "symbols.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("∀");
        result.HtmlContent.Should().Contain("∑");
        result.HtmlContent.Should().Contain("€");
        result.HtmlContent.Should().Contain("→");
    }

    /// <summary>
    /// 测试包含零宽字符的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_ZeroWidthCharacters_ShouldParseCorrectly()
    {
        // Arrange
        // 零宽空格 (U+200B)、零宽非连接符 (U+200C)、零宽连接符 (U+200D)
        var content = CreateYamlContent(
            "零宽字符测试",
            "这是\u200B包含\u200C零宽\u200D字符的文本。");
        var file = CreateContentFile(content, "zero-width.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试包含组合字符的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_CombiningCharacters_ShouldParseCorrectly()
    {
        // Arrange
        // 组合字符：é = e + ́ (U+0301)
        var content = CreateYamlContent(
            "组合字符测试",
            """
            # 组合字符
            
            café (预组合)
            cafe\u0301 (组合形式)
            
            naïve (预组合)
            nai\u0308ve (组合形式)
            
            ## 越南语
            
            Việt Nam
            """);
        var file = CreateContentFile(content, "combining.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("café");
    }

    /// <summary>
    /// 测试包含代理对字符的内容（如 Emoji 和罕见汉字）
    /// </summary>
    [Fact]
    public async Task ParseAsync_SurrogatePairs_ShouldParseCorrectly()
    {
        // Arrange
        // 代理对字符：𠀀 (U+20000)、𝄞 (U+1D11E)
        var content = CreateYamlContent(
            "代理对测试",
            """
            # 代理对字符
            
            ## CJK 扩展 B 汉字
            
            𠀀𠀁𠀂𠀃
            
            ## 音乐符号
            
            𝄞𝄢𝄪𝄫
            
            ## 数学字母
            
            𝔸𝔹ℂ𝔻
            """);
        var file = CreateContentFile(content, "surrogate.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("𠀀");
        result.HtmlContent.Should().Contain("𝄞");
    }

    #endregion


    #region 无效 UTF-8 内容测试（错误处理）

    /// <summary>
    /// 测试包含无效 UTF-8 序列的内容
    /// 系统应该能够处理或报告错误，而不是崩溃
    /// </summary>
    [Fact]
    public void ParseAsync_InvalidUtf8Sequence_ShouldHandleGracefully()
    {
        // Arrange
        // 创建包含无效 UTF-8 序列的字节数组
        // 0xFF 0xFE 是无效的 UTF-8 起始字节
        var invalidBytes = new byte[]
        {
            0x2D, 0x2D, 0x2D, 0x0A, // ---\n
            0x74, 0x69, 0x74, 0x6C, 0x65, 0x3A, 0x20, // title: 
            0xFF, 0xFE, // 无效 UTF-8
            0x0A, // \n
            0x2D, 0x2D, 0x2D, 0x0A, // ---\n
            0x48, 0x65, 0x6C, 0x6C, 0x6F // Hello
        };
        var file = CreateContentFileFromBytes(invalidBytes, "invalid-utf8.md");

        // Act & Assert
        // 系统应该能够处理无效 UTF-8，可能抛出异常或返回替换字符
        var act = () => _parser.Parse(file);

        // 不应该抛出未处理的异常导致崩溃
        // 可能抛出 DecoderFallbackException 或返回带有替换字符的结果
        act.Should().NotThrow<NullReferenceException>();
        act.Should().NotThrow<AccessViolationException>();
    }

    /// <summary>
    /// 测试包含截断的 UTF-8 多字节序列的内容
    /// </summary>
    [Fact]
    public void ParseAsync_TruncatedUtf8Sequence_ShouldHandleGracefully()
    {
        // Arrange
        // 创建截断的 UTF-8 序列（中文字符 "中" 的 UTF-8 是 E4 B8 AD，这里只有前两个字节）
        var truncatedBytes = new byte[]
        {
            0x2D, 0x2D, 0x2D, 0x0A, // ---\n
            0x74, 0x69, 0x74, 0x6C, 0x65, 0x3A, 0x20, 0x22, // title: "
            0xE4, 0xB8, // 截断的 UTF-8（缺少第三个字节）
            0x22, 0x0A, // "\n
            0x2D, 0x2D, 0x2D, 0x0A // ---\n
        };
        var file = CreateContentFileFromBytes(truncatedBytes, "truncated-utf8.md");

        // Act & Assert
        var act = () => _parser.Parse(file);

        // 不应该导致崩溃
        act.Should().NotThrow<NullReferenceException>();
        act.Should().NotThrow<AccessViolationException>();
    }

    /// <summary>
    /// 测试包含过长 UTF-8 编码的内容
    /// </summary>
    [Fact]
    public void ParseAsync_OverlongUtf8Encoding_ShouldHandleGracefully()
    {
        // Arrange
        // 过长编码：用 2 字节编码 ASCII 字符 '/' (0x2F)
        // 正确编码是 0x2F，过长编码是 0xC0 0xAF
        var overlongBytes = new byte[]
        {
            0x2D, 0x2D, 0x2D, 0x0A, // ---\n
            0x74, 0x69, 0x74, 0x6C, 0x65, 0x3A, 0x20, 0x22, // title: "
            0x74, 0x65, 0x73, 0x74, // test
            0x22, 0x0A, // "\n
            0x2D, 0x2D, 0x2D, 0x0A, // ---\n
            0xC0, 0xAF // 过长编码的 '/'
        };
        var file = CreateContentFileFromBytes(overlongBytes, "overlong-utf8.md");

        // Act & Assert
        var act = () => _parser.Parse(file);

        // 不应该导致崩溃
        act.Should().NotThrow<NullReferenceException>();
        act.Should().NotThrow<AccessViolationException>();
    }

    /// <summary>
    /// 测试包含 BOM（字节顺序标记）的内容
    /// BOM 可能导致 Front Matter 解析失败，系统应该能够处理这种情况
    /// </summary>
    [Fact]
    public async Task ParseAsync_WithBOM_ShouldHandleGracefully()
    {
        // Arrange
        // UTF-8 BOM: EF BB BF
        var contentWithBom = "\uFEFF" + CreateYamlContent("带 BOM 的文件", "这是正文内容。");
        var file = CreateContentFile(contentWithBom, "with-bom.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        // BOM 可能导致 Front Matter 解析失败，但系统不应崩溃
        result.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
        // 如果 Front Matter 解析成功，标题应该是 "带 BOM 的文件"
        // 如果解析失败，标题应该从文件名生成
        result.Metadata.Title.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// 测试包含 NULL 字符的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_WithNullCharacters_ShouldHandleGracefully()
    {
        // Arrange
        var content = CreateYamlContent("NULL 字符测试", "这是\0包含\0NULL\0字符的内容。");
        var file = CreateContentFile(content, "null-chars.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("NULL 字符测试");
    }

    /// <summary>
    /// 测试包含控制字符的内容
    /// </summary>
    [Fact]
    public async Task ParseAsync_WithControlCharacters_ShouldHandleGracefully()
    {
        // Arrange
        // 包含各种控制字符：退格(0x08)、响铃(0x07)、垂直制表符(0x0B)
        var content = CreateYamlContent("控制字符测试", "这是\x07包含\x08控制\x0B字符的内容。");
        var file = CreateContentFile(content, "control-chars.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("控制字符测试");
    }

    #endregion


    #region 深层嵌套 Markdown 结构测试

    /// <summary>
    /// 测试深层嵌套的列表结构
    /// </summary>
    [Fact]
    public async Task ParseAsync_DeeplyNestedLists_ShouldParseCorrectly()
    {
        // Arrange
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: \"深层嵌套列表测试\"");
        sb.AppendLine("---");
        sb.AppendLine();

        // 生成 20 层嵌套的列表
        for (int i = 0; i < 20; i++)
        {
            sb.Append(new string(' ', i * 2));
            sb.AppendLine($"- 第 {i + 1} 层列表项");
        }

        var content = sb.ToString();
        var file = CreateContentFile(content, "nested-lists.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("深层嵌套列表测试");
        result.HtmlContent.Should().Contain("<ul>");
        result.HtmlContent.Should().Contain("<li>");
        result.HtmlContent.Should().Contain("第 1 层");
        result.HtmlContent.Should().Contain("第 20 层");
    }

    /// <summary>
    /// 测试深层嵌套的引用块结构
    /// </summary>
    [Fact]
    public async Task ParseAsync_DeeplyNestedBlockquotes_ShouldParseCorrectly()
    {
        // Arrange
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: \"深层嵌套引用测试\"");
        sb.AppendLine("---");
        sb.AppendLine();

        // 生成 10 层嵌套的引用
        for (int i = 1; i <= 10; i++)
        {
            sb.Append(new string('>', i));
            sb.AppendLine($" 第 {i} 层引用");
        }

        var content = sb.ToString();
        var file = CreateContentFile(content, "nested-blockquotes.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("深层嵌套引用测试");
        result.HtmlContent.Should().Contain("<blockquote>");
        result.HtmlContent.Should().Contain("第 1 层引用");
    }

    /// <summary>
    /// 测试混合嵌套结构（列表中包含引用，引用中包含代码块）
    /// </summary>
    [Fact]
    public async Task ParseAsync_MixedNestedStructures_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "混合嵌套测试",
            """
            # 混合嵌套结构

            - 列表项 1
              > 引用内容
              > 
              > ```csharp
              > Console.WriteLine("嵌套代码");
              > ```
              
            - 列表项 2
              - 子列表项 2.1
                > 嵌套引用
                > - 引用中的列表
                > - 另一个列表项
              - 子列表项 2.2
            
            > 外层引用
            > - 引用中的列表 1
            > - 引用中的列表 2
            >   - 嵌套列表
            """);
        var file = CreateContentFile(content, "mixed-nested.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("<ul>");
        result.HtmlContent.Should().Contain("<blockquote>");
        // 代码块在 <pre><code> 中，检查 pre 标签即可
        result.HtmlContent.Should().Contain("<pre>");
    }

    /// <summary>
    /// 测试深层嵌套的标题结构
    /// </summary>
    [Fact]
    public async Task ParseAsync_AllHeadingLevels_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "标题层级测试",
            """
            # 一级标题

            内容...

            ## 二级标题

            内容...

            ### 三级标题

            内容...

            #### 四级标题

            内容...

            ##### 五级标题

            内容...

            ###### 六级标题

            内容...

            ####### 七级标题（无效，应作为普通文本）

            内容...
            """);
        var file = CreateContentFile(content, "all-headings.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("<h1");
        result.HtmlContent.Should().Contain("<h2");
        result.HtmlContent.Should().Contain("<h3");
        result.HtmlContent.Should().Contain("<h4");
        result.HtmlContent.Should().Contain("<h5");
        result.HtmlContent.Should().Contain("<h6");
        // H7 不存在，应该作为普通文本处理
    }

    /// <summary>
    /// 测试复杂表格结构
    /// </summary>
    [Fact]
    public async Task ParseAsync_ComplexTable_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "复杂表格测试",
            """
            # 复杂表格

            | 左对齐 | 居中对齐 | 右对齐 | 默认对齐 |
            |:-------|:--------:|-------:|----------|
            | 单元格 | **粗体** | *斜体* | `代码` |
            | [链接](https://example.com) | ![图片](/img.png) | ~~删除线~~ | 普通文本 |
            | 多行<br>内容 | 包含\|管道符 | 包含\\反斜杠 | 最后一行 |

            ## 宽表格

            | A | B | C | D | E | F | G | H | I | J |
            |---|---|---|---|---|---|---|---|---|---|
            | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
            """);
        var file = CreateContentFile(content, "complex-table.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("<table");
        result.HtmlContent.Should().Contain("<th");
        result.HtmlContent.Should().Contain("<td");
    }

    /// <summary>
    /// 测试嵌套代码块（代码块中包含代码块标记）
    /// </summary>
    [Fact]
    public async Task ParseAsync_NestedCodeBlockMarkers_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "嵌套代码块测试",
            """
            # 代码块中的代码块标记

            ````markdown
            这是一个 Markdown 代码块示例：

            ```csharp
            Console.WriteLine("Hello");
            ```

            上面是 C# 代码。
            ````

            ## 四个反引号的代码块

            `````
            ````
            ```
            代码
            ```
            ````
            `````
            """);
        var file = CreateContentFile(content, "nested-codeblocks.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("<pre");
        result.HtmlContent.Should().Contain("<code");
    }

    #endregion


    #region Front Matter 边界条件测试

    /// <summary>
    /// 测试 Front Matter 中包含特殊字符的标题
    /// </summary>
    [Theory]
    [InlineData("包含\"引号\"的标题")]
    [InlineData("包含'单引号'的标题")]
    [InlineData("包含:冒号:的标题")]
    [InlineData("包含#井号#的标题")]
    [InlineData("包含[方括号]的标题")]
    [InlineData("包含{花括号}的标题")]
    public async Task ParseAsync_FrontMatterSpecialCharTitle_ShouldParseCorrectly(string title)
    {
        // Arrange
        // 使用 YAML 多行字符串语法来避免转义问题
        var content = $"""
            ---
            title: |
              {title}
            ---

            正文内容。
            """;
        var file = CreateContentFile(content, "special-title.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Contain(title.Substring(0, 5)); // 至少包含部分标题
    }

    /// <summary>
    /// 测试 Front Matter 中包含多行描述
    /// </summary>
    [Fact]
    public async Task ParseAsync_FrontMatterMultilineDescription_ShouldParseCorrectly()
    {
        // Arrange
        var content = """
            ---
            title: "多行描述测试"
            description: |
              这是第一行描述。
              这是第二行描述。
              这是第三行描述。
            ---

            正文内容。
            """;
        var file = CreateContentFile(content, "multiline-desc.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("多行描述测试");
        result.Metadata.Description.Should().Contain("第一行");
    }

    /// <summary>
    /// 测试 Front Matter 中包含空数组
    /// </summary>
    [Fact]
    public async Task ParseAsync_FrontMatterEmptyArrays_ShouldParseCorrectly()
    {
        // Arrange
        var content = """
            ---
            title: "空数组测试"
            tags: []
            categories: []
            aliases: []
            ---

            正文内容。
            """;
        var file = CreateContentFile(content, "empty-arrays.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("空数组测试");
        result.Metadata.Tags.Should().BeEmpty();
        result.Metadata.Categories.Should().BeEmpty();
        result.Metadata.Aliases.Should().BeEmpty();
    }

    /// <summary>
    /// 测试 Front Matter 中包含非常长的标签列表
    /// </summary>
    [Fact]
    public async Task ParseAsync_FrontMatterManyTags_ShouldParseCorrectly()
    {
        // Arrange
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine("title: \"多标签测试\"");
        sb.AppendLine("tags:");
        for (int i = 1; i <= 100; i++)
        {
            sb.AppendLine($"  - \"标签{i}\"");
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("正文内容。");

        var content = sb.ToString();
        var file = CreateContentFile(content, "many-tags.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Tags.Should().HaveCount(100);
        result.Metadata.Tags.Should().Contain("标签1");
        result.Metadata.Tags.Should().Contain("标签100");
    }

    /// <summary>
    /// 测试不完整的 Front Matter（缺少结束分隔符）
    /// </summary>
    [Fact]
    public async Task ParseAsync_IncompleteFrontMatter_ShouldHandleGracefully()
    {
        // Arrange
        var content = """
            ---
            title: "不完整的 Front Matter"
            date: 2024-06-15
            
            这里没有结束分隔符，所以整个内容应该被当作正文处理。
            """;
        var file = CreateContentFile(content, "incomplete-fm.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        // 由于没有结束分隔符，整个内容应该被当作 Markdown 处理
        // 标题应该从文件名生成
        result.Metadata.Title.Should().Be("Incomplete fm");
    }

    /// <summary>
    /// 测试格式错误的 YAML Front Matter
    /// </summary>
    [Fact]
    public async Task ParseAsync_MalformedYamlFrontMatter_ShouldThrow()
    {
        // Arrange
        var content = """
            ---
            title: "格式错误的 YAML
            date: 2024-06-15
            tags:
            - tag1
              - nested (invalid)
            ---

            正文内容。
            """;
        var file = CreateContentFile(content, "malformed-yaml.md");

        // Act & Assert：分隔符完整但内容格式错误时必须 fail-fast（防止草稿等元数据被静默丢弃）
        var act = async () => await _parser.ParseAsync(file);
        await act.Should().ThrowAsync<Flint.Core.Content.FrontMatterParseException>();
    }

    /// <summary>
    /// 测试格式错误的 TOML Front Matter
    /// </summary>
    [Fact]
    public async Task ParseAsync_MalformedTomlFrontMatter_ShouldThrow()
    {
        // Arrange
        var content = """
            +++
            title = "格式错误的 TOML
            date = 2024-06-15
            tags = ["tag1", "tag2"
            +++

            正文内容。
            """;
        var file = CreateContentFile(content, "malformed-toml.md");

        // Act & Assert
        var act = async () => await _parser.ParseAsync(file);
        await act.Should().ThrowAsync<Flint.Core.Content.FrontMatterParseException>();
    }

    /// <summary>
    /// 测试格式错误的 JSON Front Matter
    /// </summary>
    [Fact]
    public async Task ParseAsync_MalformedJsonFrontMatter_ShouldThrow()
    {
        // Arrange
        var content = """
            {
              "title": "格式错误的 JSON",
              "date": "2024-06-15",
              "tags": ["tag1", "tag2"
            }

            正文内容。
            """;
        var file = CreateContentFile(content, "malformed-json.md");

        // Act & Assert
        var act = async () => await _parser.ParseAsync(file);
        await act.Should().ThrowAsync<Flint.Core.Content.FrontMatterParseException>();
    }

    #endregion


    #region Markdown 特殊语法边界测试

    /// <summary>
    /// 测试连续的分隔线
    /// </summary>
    [Fact]
    public async Task ParseAsync_ConsecutiveHorizontalRules_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "连续分隔线测试",
            """
            第一段

            ---

            ---

            ---

            第二段

            ***

            ***

            第三段
            """);
        var file = CreateContentFile(content, "consecutive-hr.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("<hr");
        result.HtmlContent.Should().Contain("第一段");
        result.HtmlContent.Should().Contain("第二段");
    }

    /// <summary>
    /// 测试空链接和空图片
    /// </summary>
    [Fact]
    public async Task ParseAsync_EmptyLinksAndImages_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "空链接测试",
            """
            # 空链接和图片

            [空链接]()

            [空文本链接](https://example.com)

            []()

            ![空图片]()

            ![]()

            ![](https://example.com/image.png)
            """);
        var file = CreateContentFile(content, "empty-links.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试特殊的链接格式
    /// </summary>
    [Fact]
    public async Task ParseAsync_SpecialLinkFormats_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "特殊链接测试",
            """
            # 特殊链接格式

            ## 自动链接

            <https://example.com>

            <user@example.com>

            ## 引用链接

            这是一个[引用链接][ref1]。

            这是另一个[引用链接][ref2]。

            [ref1]: https://example.com "示例网站"
            [ref2]: https://example.org

            ## 脚注链接

            这是一个脚注[^1]。

            [^1]: 这是脚注内容。

            ## 锚点链接

            [跳转到标题](#特殊链接格式)
            """);
        var file = CreateContentFile(content, "special-links.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("<a");
    }

    /// <summary>
    /// 测试转义字符
    /// </summary>
    [Fact]
    public async Task ParseAsync_EscapedCharacters_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "转义字符测试",
            """
            # 转义字符

            \*不是斜体\*

            \*\*不是粗体\*\*

            \# 不是标题

            \- 不是列表

            \> 不是引用

            \`不是代码\`

            \[不是链接\](url)

            \!\[不是图片\](url)

            \\反斜杠本身

            \| 不是表格分隔符 \|
            """);
        var file = CreateContentFile(content, "escaped-chars.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("*不是斜体*");
        result.HtmlContent.Should().NotContain("<em>不是斜体</em>");
    }

    /// <summary>
    /// 测试 HTML 实体
    /// </summary>
    [Fact]
    public async Task ParseAsync_HtmlEntities_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "HTML 实体测试",
            """
            # HTML 实体

            &amp; &lt; &gt; &quot; &apos;

            &copy; &reg; &trade;

            &nbsp; &mdash; &ndash;

            &#169; &#174; &#8482;

            &#x00A9; &#x00AE; &#x2122;
            """);
        var file = CreateContentFile(content, "html-entities.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试内联 HTML
    /// </summary>
    [Fact]
    public async Task ParseAsync_InlineHtml_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "内联 HTML 测试",
            """
            # 内联 HTML

            这是一段包含 <strong>HTML 标签</strong> 的文本。

            <div class="custom">
              <p>这是一个自定义 div。</p>
            </div>

            <details>
              <summary>点击展开</summary>
              <p>隐藏的内容。</p>
            </details>

            <table>
              <tr>
                <td>HTML 表格</td>
              </tr>
            </table>
            """);
        var file = CreateContentFile(content, "inline-html.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("<strong>");
        result.HtmlContent.Should().Contain("<div");
    }

    /// <summary>
    /// 测试任务列表的各种状态
    /// </summary>
    [Fact]
    public async Task ParseAsync_TaskListVariations_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent(
            "任务列表测试",
            """
            # 任务列表

            - [x] 已完成任务
            - [X] 大写 X 完成
            - [ ] 未完成任务
            - [-] 取消的任务（某些解析器支持）
            - [>] 推迟的任务（某些解析器支持）

            ## 嵌套任务列表

            - [ ] 父任务
              - [x] 子任务 1
              - [ ] 子任务 2
                - [x] 孙任务
            """);
        var file = CreateContentFile(content, "task-lists.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.HtmlContent.Should().Contain("checkbox");
    }

    #endregion


    #region 文件路径边界测试

    /// <summary>
    /// 测试包含空格的文件路径
    /// </summary>
    [Fact]
    public async Task ParseAsync_FilePathWithSpaces_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent("空格路径测试", "正文内容。");
        var file = CreateContentFile(content, "file with spaces.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("空格路径测试");
        result.SourcePath.Should().Contain("file with spaces.md");
    }

    /// <summary>
    /// 测试包含中文的文件路径
    /// </summary>
    [Fact]
    public async Task ParseAsync_FilePathWithChinese_ShouldParseCorrectly()
    {
        // Arrange
        var content = CreateYamlContent("中文路径测试", "正文内容。");
        var file = CreateContentFile(content, "中文文件名.md");

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("中文路径测试");
        result.SourcePath.Should().Contain("中文文件名.md");
    }

    /// <summary>
    /// 测试包含特殊字符的文件路径
    /// </summary>
    [Theory]
    [InlineData("file-with-dash.md")]
    [InlineData("file_with_underscore.md")]
    [InlineData("file.multiple.dots.md")]
    [InlineData("file(with)parens.md")]
    [InlineData("file[with]brackets.md")]
    public async Task ParseAsync_FilePathWithSpecialChars_ShouldParseCorrectly(string fileName)
    {
        // Arrange
        var content = CreateYamlContent("特殊字符路径测试", "正文内容。");
        var file = CreateContentFile(content, fileName);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("特殊字符路径测试");
    }

    /// <summary>
    /// 测试非常长的文件名
    /// </summary>
    [Fact]
    public async Task ParseAsync_VeryLongFileName_ShouldParseCorrectly()
    {
        // Arrange
        // 创建一个接近文件系统限制的长文件名（通常是 255 字符）
        var longName = new string('a', 200) + ".md";
        var content = CreateYamlContent("长文件名测试", "正文内容。");
        var file = CreateContentFile(content, longName);

        // Act
        var result = await _parser.ParseAsync(file);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Title.Should().Be("长文件名测试");
    }

    #endregion

    #region 并发解析测试

    /// <summary>
    /// 测试并发解析多个文件
    /// </summary>
    [Fact]
    public async Task ParseAsync_ConcurrentParsing_ShouldBeThreadSafe()
    {
        // Arrange
        var files = new List<ContentFile>();
        for (int i = 0; i < 50; i++)
        {
            var content = CreateYamlContent($"并发测试文件 {i}", $"这是文件 {i} 的内容。");
            files.Add(CreateContentFile(content, $"concurrent-{i}.md"));
        }

        // Act
        var tasks = files.Select(f => _parser.ParseAsync(f).AsTask());
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(50);
        results.Should().AllSatisfy(r =>
        {
            r.Should().NotBeNull();
            r.Metadata.Should().NotBeNull();
            r.HtmlContent.Should().NotBeNull();
        });
    }

    /// <summary>
    /// 测试批量解析
    /// </summary>
    [Fact]
    public async Task ParseBatchAsync_MultipleFiles_ShouldParseAll()
    {
        // Arrange
        var files = new List<ContentFile>();
        for (int i = 0; i < 20; i++)
        {
            var content = CreateYamlContent($"批量测试文件 {i}", $"这是文件 {i} 的内容。");
            files.Add(CreateContentFile(content, $"batch-{i}.md"));
        }

        // Act
        var results = new List<ParsedContent>();
        await foreach (var result in _parser.ParseBatchAsync(files))
        {
            results.Add(result);
        }

        // Assert
        results.Should().HaveCount(20);
        for (int i = 0; i < 20; i++)
        {
            results[i].Metadata.Title.Should().Be($"批量测试文件 {i}");
        }
    }

    #endregion
}
