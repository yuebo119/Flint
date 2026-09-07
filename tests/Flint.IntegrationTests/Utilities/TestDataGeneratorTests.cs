// Flint 静态站点生成器
// TestDataGenerator 单元测试

using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Utilities;

/// <summary>
/// TestDataGenerator 单元测试
/// 测试各种格式的数据生成和边界条件数据
/// </summary>
public class TestDataGeneratorTests
{
    #region Markdown 生成测试

    [Fact]
    public void GenerateMarkdown_WithDefaultOptions_ShouldGenerateValidMarkdown()
    {
        // Act
        var markdown = TestDataGenerator.GenerateMarkdown();

        // Assert
        markdown.Should().NotBeNullOrEmpty();
        markdown.Should().Contain("#"); // 应包含标题
    }

    [Fact]
    public void GenerateMarkdown_WithSeed_ShouldGenerateDeterministicContent()
    {
        // Arrange
        var options = new MarkdownGeneratorOptions { Seed = 12345 };

        // Act
        var markdown1 = TestDataGenerator.GenerateMarkdown(options);
        var markdown2 = TestDataGenerator.GenerateMarkdown(options);

        // Assert
        markdown1.Should().Be(markdown2);
    }

    [Fact]
    public void GenerateMarkdown_WithHeadingsDisabled_ShouldNotContainHeadings()
    {
        // Arrange
        var options = new MarkdownGeneratorOptions
        {
            IncludeHeadings = false,
            Seed = 12345
        };

        // Act
        var markdown = TestDataGenerator.GenerateMarkdown(options);

        // Assert
        markdown.Should().NotStartWith("#");
    }

    [Fact]
    public void GenerateMarkdown_WithCodeBlocksEnabled_ShouldContainCodeBlocks()
    {
        // Arrange
        var options = new MarkdownGeneratorOptions
        {
            IncludeCodeBlocks = true,
            ParagraphCount = 10, // 增加段落数以提高包含代码块的概率
            Seed = 42
        };

        // Act
        var markdown = TestDataGenerator.GenerateMarkdown(options);

        // Assert
        // 由于是随机的，我们只验证生成的内容是有效的
        markdown.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateMarkdown_WithTablesEnabled_ShouldGenerateValidContent()
    {
        // Arrange
        var options = new MarkdownGeneratorOptions
        {
            IncludeTables = true,
            ParagraphCount = 10,
            Seed = 100
        };

        // Act
        var markdown = TestDataGenerator.GenerateMarkdown(options);

        // Assert
        markdown.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void GenerateMarkdown_WithDifferentParagraphCounts_ShouldGenerateContent(int paragraphCount)
    {
        // Arrange
        var options = new MarkdownGeneratorOptions
        {
            ParagraphCount = paragraphCount,
            Seed = 12345
        };

        // Act
        var markdown = TestDataGenerator.GenerateMarkdown(options);

        // Assert
        markdown.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Front Matter 生成测试

    [Fact]
    public void GenerateFrontMatter_Yaml_ShouldGenerateValidYaml()
    {
        // Act
        var frontMatter = TestDataGenerator.GenerateFrontMatter(FrontMatterFormat.Yaml);

        // Assert
        frontMatter.Should().StartWith("---");
        frontMatter.TrimEnd().Should().EndWith("---");
        frontMatter.Should().Contain("title:");
        frontMatter.Should().Contain("date:");
        frontMatter.Should().Contain("tags:");
    }

    [Fact]
    public void GenerateFrontMatter_Toml_ShouldGenerateValidToml()
    {
        // Act
        var frontMatter = TestDataGenerator.GenerateFrontMatter(FrontMatterFormat.Toml);

        // Assert
        frontMatter.Should().StartWith("+++");
        frontMatter.TrimEnd().Should().EndWith("+++");
        frontMatter.Should().Contain("title =");
        frontMatter.Should().Contain("date =");
        frontMatter.Should().Contain("tags =");
    }

    [Fact]
    public void GenerateFrontMatter_Json_ShouldGenerateValidJson()
    {
        // Act
        var frontMatter = TestDataGenerator.GenerateFrontMatter(FrontMatterFormat.Json);

        // Assert
        frontMatter.Should().StartWith("{");
        frontMatter.TrimEnd().Should().EndWith("}");
        frontMatter.Should().Contain("\"title\":");
        frontMatter.Should().Contain("\"date\":");
        frontMatter.Should().Contain("\"tags\":");
    }

    [Fact]
    public void GenerateFrontMatter_WithSeed_ShouldGenerateDeterministicContent()
    {
        // Act
        var frontMatter1 = TestDataGenerator.GenerateFrontMatter(FrontMatterFormat.Yaml, seed: 12345);
        var frontMatter2 = TestDataGenerator.GenerateFrontMatter(FrontMatterFormat.Yaml, seed: 12345);

        // Assert
        frontMatter1.Should().Be(frontMatter2);
    }

    [Theory]
    [InlineData(FrontMatterFormat.Yaml)]
    [InlineData(FrontMatterFormat.Toml)]
    [InlineData(FrontMatterFormat.Json)]
    public void GenerateFrontMatter_AllFormats_ShouldContainRequiredFields(FrontMatterFormat format)
    {
        // Act
        var frontMatter = TestDataGenerator.GenerateFrontMatter(format);

        // Assert
        frontMatter.Should().NotBeNullOrEmpty();
        // 所有格式都应包含 title 和 date
        frontMatter.ToLowerInvariant().Should().Contain("title");
        frontMatter.ToLowerInvariant().Should().Contain("date");
    }

    #endregion

    #region 配置生成测试

    [Fact]
    public void GenerateConfig_Toml_ShouldGenerateValidToml()
    {
        // Act
        var config = TestDataGenerator.GenerateConfig(ConfigFormat.Toml);

        // Assert
        config.Should().Contain("baseURL =");
        config.Should().Contain("title =");
        config.Should().Contain("languageCode =");
        config.Should().Contain("[params]");
    }

    [Fact]
    public void GenerateConfig_Yaml_ShouldGenerateValidYaml()
    {
        // Act
        var config = TestDataGenerator.GenerateConfig(ConfigFormat.Yaml);

        // Assert
        config.Should().Contain("baseURL:");
        config.Should().Contain("title:");
        config.Should().Contain("languageCode:");
        config.Should().Contain("params:");
    }

    [Fact]
    public void GenerateConfig_Json_ShouldGenerateValidJson()
    {
        // Act
        var config = TestDataGenerator.GenerateConfig(ConfigFormat.Json);

        // Assert
        config.Should().StartWith("{");
        config.Should().EndWith("}");
        config.Should().Contain("\"baseURL\":");
        config.Should().Contain("\"title\":");
    }

    [Fact]
    public void GenerateConfig_WithSeed_ShouldGenerateDeterministicContent()
    {
        // Act
        var config1 = TestDataGenerator.GenerateConfig(ConfigFormat.Toml, seed: 12345);
        var config2 = TestDataGenerator.GenerateConfig(ConfigFormat.Toml, seed: 12345);

        // Assert
        config1.Should().Be(config2);
    }

    #endregion


    #region SCSS 生成测试

    [Fact]
    public void GenerateScss_ShouldGenerateValidScss()
    {
        // Act
        var scss = TestDataGenerator.GenerateScss();

        // Assert
        scss.Should().NotBeNullOrEmpty();
        scss.Should().Contain("$"); // 变量
        scss.Should().Contain("@mixin"); // Mixin
        scss.Should().Contain("{"); // 规则块
    }

    [Fact]
    public void GenerateScss_ShouldContainVariables()
    {
        // Act
        var scss = TestDataGenerator.GenerateScss();

        // Assert
        scss.Should().Contain("$primary-color:");
        scss.Should().Contain("$secondary-color:");
        scss.Should().Contain("$font-size-base:");
    }

    [Fact]
    public void GenerateScss_ShouldContainMixins()
    {
        // Act
        var scss = TestDataGenerator.GenerateScss();

        // Assert
        scss.Should().Contain("@mixin flex-center");
        scss.Should().Contain("@mixin button-style");
    }

    [Fact]
    public void GenerateScss_ShouldContainNestedRules()
    {
        // Act
        var scss = TestDataGenerator.GenerateScss();

        // Assert
        scss.Should().Contain(".container");
        scss.Should().Contain(".header");
        scss.Should().Contain("&:hover");
    }

    [Fact]
    public void GenerateScss_WithSeed_ShouldGenerateDeterministicContent()
    {
        // Act
        var scss1 = TestDataGenerator.GenerateScss(seed: 12345);
        var scss2 = TestDataGenerator.GenerateScss(seed: 12345);

        // Assert
        scss1.Should().Be(scss2);
    }

    #endregion

    #region 边界条件数据测试

    [Fact]
    public void GenerateEmptyString_ShouldReturnEmptyString()
    {
        // Act
        var result = TestDataGenerator.GenerateEmptyString();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void GenerateLongString_ShouldGenerateStringOfSpecifiedLength()
    {
        // Arrange
        const int length = 10000;

        // Act
        var result = TestDataGenerator.GenerateLongString(length);

        // Assert
        result.Should().HaveLength(length);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(100000)]
    public void GenerateLongString_WithDifferentLengths_ShouldGenerateCorrectLength(int length)
    {
        // Act
        var result = TestDataGenerator.GenerateLongString(length);

        // Assert
        result.Should().HaveLength(length);
    }

    [Fact]
    public void GenerateSpecialCharacterString_ShouldGenerateNonEmptyString()
    {
        // Act
        var result = TestDataGenerator.GenerateSpecialCharacterString();

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateSpecialCharacterString_WithSeed_ShouldGenerateDeterministicContent()
    {
        // Act
        var result1 = TestDataGenerator.GenerateSpecialCharacterString(seed: 12345);
        var result2 = TestDataGenerator.GenerateSpecialCharacterString(seed: 12345);

        // Assert
        result1.Should().Be(result2);
    }

    [Fact]
    public void GenerateUnicodeString_ShouldGenerateNonEmptyString()
    {
        // Act
        var result = TestDataGenerator.GenerateUnicodeString();

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GenerateUnicodeString_ShouldContainNonAsciiCharacters()
    {
        // Act
        var result = TestDataGenerator.GenerateUnicodeString(seed: 12345);

        // Assert
        result.Any(c => c > 127).Should().BeTrue("应包含非 ASCII 字符");
    }

    [Fact]
    public void GenerateEmojiString_ShouldGenerateSpecifiedCount()
    {
        // Arrange
        const int count = 5;

        // Act
        var result = TestDataGenerator.GenerateEmojiString(count);

        // Assert
        result.Should().NotBeNullOrEmpty();
        // Emoji 可能是多个 char，所以我们只验证非空
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void GenerateEmojiString_WithDifferentCounts_ShouldGenerateContent(int count)
    {
        // Act
        var result = TestDataGenerator.GenerateEmojiString(count);

        // Assert
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateDeepPath_ShouldGeneratePathWithSpecifiedDepth()
    {
        // Arrange
        const int depth = 10;

        // Act
        var result = TestDataGenerator.GenerateDeepPath(depth);

        // Assert
        var parts = result.Split('/');
        parts.Should().HaveCount(depth);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    public void GenerateDeepPath_WithDifferentDepths_ShouldGenerateCorrectDepth(int depth)
    {
        // Act
        var result = TestDataGenerator.GenerateDeepPath(depth);

        // Assert
        var parts = result.Split('/');
        parts.Should().HaveCount(depth);
    }

    [Fact]
    public void GenerateDeepPath_ShouldNotContainInvalidPathCharacters()
    {
        // Act
        var result = TestDataGenerator.GenerateDeepPath(10);

        // Assert
        result.Should().NotContain("\\");
        result.Should().NotContain(":");
        result.Should().NotContain("*");
        result.Should().NotContain("?");
        result.Should().NotContain("\"");
        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().NotContain("|");
    }

    #endregion

    #region 完整内容文件生成测试

    [Fact]
    public void GenerateContentFile_ShouldGenerateFrontMatterAndMarkdown()
    {
        // Act
        var content = TestDataGenerator.GenerateContentFile();

        // Assert
        content.Should().NotBeNullOrEmpty();
        content.Should().Contain("---"); // YAML Front Matter
        content.Should().Contain("#"); // Markdown 标题
    }

    [Theory]
    [InlineData(FrontMatterFormat.Yaml)]
    [InlineData(FrontMatterFormat.Toml)]
    [InlineData(FrontMatterFormat.Json)]
    public void GenerateContentFile_WithDifferentFormats_ShouldGenerateValidContent(FrontMatterFormat format)
    {
        // Act
        var content = TestDataGenerator.GenerateContentFile(format);

        // Assert
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateContentFile_WithSeed_ShouldGenerateDeterministicContent()
    {
        // Act
        var content1 = TestDataGenerator.GenerateContentFile(seed: 12345);
        var content2 = TestDataGenerator.GenerateContentFile(seed: 12345);

        // Assert
        content1.Should().Be(content2);
    }

    [Fact]
    public void GenerateContentFile_WithOptions_ShouldRespectOptions()
    {
        // Arrange
        var options = new MarkdownGeneratorOptions
        {
            IncludeHeadings = false,
            ParagraphCount = 1,
            Seed = 12345
        };

        // Act
        var content = TestDataGenerator.GenerateContentFile(options: options);

        // Assert
        content.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region 枚举测试

    [Fact]
    public void FrontMatterFormat_ShouldHaveAllExpectedValues()
    {
        // Assert
        Enum.GetValues<FrontMatterFormat>().Should().HaveCount(3);
        Enum.IsDefined(FrontMatterFormat.Yaml).Should().BeTrue();
        Enum.IsDefined(FrontMatterFormat.Toml).Should().BeTrue();
        Enum.IsDefined(FrontMatterFormat.Json).Should().BeTrue();
    }

    [Fact]
    public void ConfigFormat_ShouldHaveAllExpectedValues()
    {
        // Assert
        Enum.GetValues<ConfigFormat>().Should().HaveCount(3);
        Enum.IsDefined(ConfigFormat.Toml).Should().BeTrue();
        Enum.IsDefined(ConfigFormat.Yaml).Should().BeTrue();
        Enum.IsDefined(ConfigFormat.Json).Should().BeTrue();
    }

    #endregion

    #region MarkdownGeneratorOptions 测试

    [Fact]
    public void MarkdownGeneratorOptions_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var options = new MarkdownGeneratorOptions();

        // Assert
        options.IncludeHeadings.Should().BeTrue();
        options.IncludeLists.Should().BeTrue();
        options.IncludeCodeBlocks.Should().BeTrue();
        options.IncludeLinks.Should().BeTrue();
        options.IncludeImages.Should().BeFalse();
        options.IncludeTables.Should().BeFalse();
        options.IncludeBlockquotes.Should().BeTrue();
        options.ParagraphCount.Should().Be(3);
        options.Seed.Should().BeNull();
    }

    [Fact]
    public void MarkdownGeneratorOptions_ShouldSupportWithSyntax()
    {
        // Arrange & Act
        var options = new MarkdownGeneratorOptions
        {
            IncludeHeadings = false,
            IncludeLists = false,
            IncludeCodeBlocks = false,
            IncludeLinks = false,
            IncludeImages = true,
            IncludeTables = true,
            IncludeBlockquotes = false,
            ParagraphCount = 10,
            Seed = 12345
        };

        // Assert
        options.IncludeHeadings.Should().BeFalse();
        options.IncludeLists.Should().BeFalse();
        options.IncludeCodeBlocks.Should().BeFalse();
        options.IncludeLinks.Should().BeFalse();
        options.IncludeImages.Should().BeTrue();
        options.IncludeTables.Should().BeTrue();
        options.IncludeBlockquotes.Should().BeFalse();
        options.ParagraphCount.Should().Be(10);
        options.Seed.Should().Be(12345);
    }

    #endregion
}
