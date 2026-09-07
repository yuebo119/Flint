// Flint 静态站点生成器
// 测试数据验证测试

using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.TestData;

/// <summary>
/// 测试数据验证测试
/// 确保所有预定义的测试数据文件存在且有效
/// </summary>
public class TestDataValidationTests
{
    [Fact]
    public void TestData目录应该存在()
    {
        // Act
        var exists = TestDataPaths.Exists();

        // Assert
        exists.Should().BeTrue("测试数据目录应该存在");
    }

    [Fact]
    public void 所有预期的测试数据文件应该存在()
    {
        // Act
        var (isValid, missingFiles) = TestDataPaths.Validate();

        // Assert
        isValid.Should().BeTrue(
            $"以下测试数据文件缺失: {string.Join(", ", missingFiles)}");
    }

    #region 最小站点模板验证

    [Fact]
    public void 最小站点模板目录应该存在()
    {
        // Assert
        Directory.Exists(TestDataPaths.MinimalSite).Should().BeTrue();
    }

    [Fact]
    public void 最小站点模板应该包含配置文件()
    {
        // Arrange
        var configPath = Path.Combine(TestDataPaths.MinimalSite, "Flint.toml");

        // Assert
        File.Exists(configPath).Should().BeTrue();
    }

    [Fact]
    public void 最小站点模板应该包含首页内容()
    {
        // Arrange
        var indexPath = Path.Combine(TestDataPaths.MinimalSite, "content", "index.md");

        // Assert
        File.Exists(indexPath).Should().BeTrue();
    }

    [Fact]
    public void 最小站点模板应该包含默认布局()
    {
        // Arrange
        var layoutPath = Path.Combine(TestDataPaths.MinimalSite, "layouts", "default.html");

        // Assert
        File.Exists(layoutPath).Should().BeTrue();
    }

    #endregion

    #region 完整站点模板验证

    [Fact]
    public void 完整站点模板目录应该存在()
    {
        // Assert
        Directory.Exists(TestDataPaths.CompleteSite).Should().BeTrue();
    }

    [Fact]
    public void 完整站点模板应该包含配置文件()
    {
        // Arrange
        var configPath = Path.Combine(TestDataPaths.CompleteSite, "Flint.toml");

        // Assert
        File.Exists(configPath).Should().BeTrue();
    }

    [Theory]
    [InlineData("content/posts/yaml-frontmatter.md")]
    [InlineData("content/posts/toml-frontmatter.md")]
    [InlineData("content/posts/json-frontmatter.md")]
    [InlineData("content/posts/shortcodes-demo.md")]
    [InlineData("content/posts/draft-post.md")]
    [InlineData("content/posts/future-post.md")]
    [InlineData("content/about.md")]
    [InlineData("content/_index.md")]
    public void 完整站点模板应该包含内容文件(string relativePath)
    {
        // Arrange
        var filePath = Path.Combine(TestDataPaths.CompleteSite, relativePath);

        // Assert
        File.Exists(filePath).Should().BeTrue($"文件 {relativePath} 应该存在");
    }

    [Theory]
    [InlineData("layouts/index.html")]
    [InlineData("layouts/_default/baseof.html")]
    [InlineData("layouts/_default/single.html")]
    [InlineData("layouts/_default/list.html")]
    [InlineData("layouts/_default/taxonomy.html")]
    [InlineData("layouts/_default/term.html")]
    [InlineData("layouts/partials/head.html")]
    [InlineData("layouts/partials/header.html")]
    [InlineData("layouts/partials/footer.html")]
    public void 完整站点模板应该包含布局文件(string relativePath)
    {
        // Arrange
        var filePath = Path.Combine(TestDataPaths.CompleteSite, relativePath);

        // Assert
        File.Exists(filePath).Should().BeTrue($"文件 {relativePath} 应该存在");
    }

    [Theory]
    [InlineData("assets/scss/main.scss")]
    [InlineData("assets/scss/_variables.scss")]
    [InlineData("assets/scss/_mixins.scss")]
    [InlineData("assets/js/main.js")]
    public void 完整站点模板应该包含资源文件(string relativePath)
    {
        // Arrange
        var filePath = Path.Combine(TestDataPaths.CompleteSite, relativePath);

        // Assert
        File.Exists(filePath).Should().BeTrue($"文件 {relativePath} 应该存在");
    }

    #endregion

    #region 多语言站点模板验证

    [Fact]
    public void 多语言站点模板目录应该存在()
    {
        // Assert
        Directory.Exists(TestDataPaths.MultilingualSite).Should().BeTrue();
    }

    [Fact]
    public void 多语言站点模板应该包含配置文件()
    {
        // Arrange
        var configPath = Path.Combine(TestDataPaths.MultilingualSite, "Flint.toml");

        // Assert
        File.Exists(configPath).Should().BeTrue();
    }

    [Theory]
    [InlineData("zh")]
    [InlineData("en")]
    [InlineData("ja")]
    public void 多语言站点模板应该包含各语言内容目录(string language)
    {
        // Arrange
        var contentDir = Path.Combine(TestDataPaths.MultilingualSite, "content", language);

        // Assert
        Directory.Exists(contentDir).Should().BeTrue($"语言 {language} 的内容目录应该存在");
    }

    [Theory]
    [InlineData("content/zh/_index.md")]
    [InlineData("content/zh/about.md")]
    [InlineData("content/zh/posts/hello-world.md")]
    [InlineData("content/en/_index.md")]
    [InlineData("content/en/about.md")]
    [InlineData("content/en/posts/hello-world.md")]
    [InlineData("content/ja/_index.md")]
    [InlineData("content/ja/about.md")]
    [InlineData("content/ja/posts/hello-world.md")]
    public void 多语言站点模板应该包含各语言内容文件(string relativePath)
    {
        // Arrange
        var filePath = Path.Combine(TestDataPaths.MultilingualSite, relativePath);

        // Assert
        File.Exists(filePath).Should().BeTrue($"文件 {relativePath} 应该存在");
    }

    #endregion

    #region 边界条件测试数据验证

    [Fact]
    public void 边界条件测试数据目录应该存在()
    {
        // Assert
        Directory.Exists(TestDataPaths.EdgeCases).Should().BeTrue();
    }

    [Fact]
    public void 空内容文件应该存在()
    {
        // Assert
        File.Exists(TestDataPaths.EmptyContent).Should().BeTrue();
    }

    [Fact]
    public void 空配置文件应该存在()
    {
        // Assert
        File.Exists(TestDataPaths.EmptyConfig).Should().BeTrue();
    }

    [Fact]
    public void Unicode内容文件应该存在()
    {
        // Assert
        File.Exists(TestDataPaths.UnicodeContent).Should().BeTrue();
    }

    [Fact]
    public void Emoji内容文件应该存在()
    {
        // Assert
        File.Exists(TestDataPaths.EmojiContent).Should().BeTrue();
    }

    [Fact]
    public void 特殊字符文件名内容应该存在()
    {
        // Assert
        File.Exists(TestDataPaths.SpecialCharactersFilename).Should().BeTrue();
    }

    [Fact]
    public void 深层嵌套内容文件应该存在()
    {
        // Assert
        File.Exists(TestDataPaths.DeeplyNestedContent).Should().BeTrue();
    }

    #endregion

    #region 内容有效性验证

    [Fact]
    public void 空内容文件应该只包含FrontMatter()
    {
        // Arrange
        var content = File.ReadAllText(TestDataPaths.EmptyContent);

        // Assert
        content.Should().Contain("+++");
        content.Should().Contain("title");

        // 内容部分应该为空或只有空白
        // 文件格式: +++\nfront matter\n+++\n（可能有空白）
        var parts = content.Split("+++", StringSplitOptions.RemoveEmptyEntries);
        parts.Should().HaveCountGreaterThanOrEqualTo(1, "应该至少有 Front Matter 部分");

        // 如果有第二部分（正文），应该是空的或只有空白
        if (parts.Length > 1)
        {
            parts[1].Trim().Should().BeEmpty("空内容文件的正文部分应该为空");
        }
    }

    [Fact]
    public void Unicode内容文件应该包含各种Unicode字符()
    {
        // Arrange
        var content = File.ReadAllText(TestDataPaths.UnicodeContent);

        // Assert
        content.Should().Contain("你好世界");  // 中文
        content.Should().Contain("こんにちは"); // 日文
        content.Should().Contain("안녕하세요"); // 韩文
        content.Should().Contain("∑");         // 数学符号
        content.Should().Contain("α");         // 希腊字母
        content.Should().Contain("😀");        // Emoji
    }

    [Fact]
    public void Emoji内容文件应该包含各种Emoji()
    {
        // Arrange
        var content = File.ReadAllText(TestDataPaths.EmojiContent);

        // Assert
        content.Should().Contain("🎉");  // 标题中的 Emoji
        content.Should().Contain("😀");  // 笑脸
        content.Should().Contain("❤️");  // 爱心
        content.Should().Contain("👨‍👩‍👧"); // 复杂 Emoji 序列
        content.Should().Contain("🇨🇳"); // 旗帜
    }

    [Fact]
    public void 深层嵌套内容文件应该包含多层嵌套结构()
    {
        // Arrange
        var content = File.ReadAllText(TestDataPaths.DeeplyNestedContent);

        // Assert
        content.Should().Contain("第十层");  // 深层嵌套列表
        content.Should().Contain("> > > > >"); // 深层嵌套引用
    }

    #endregion

    #region 配置文件有效性验证

    [Fact]
    public void 最小站点配置应该包含必需字段()
    {
        // Arrange
        var configPath = Path.Combine(TestDataPaths.MinimalSite, "Flint.toml");
        var content = File.ReadAllText(configPath);

        // Assert
        content.Should().Contain("baseURL");
        content.Should().Contain("title");
    }

    [Fact]
    public void 完整站点配置应该包含所有功能配置()
    {
        // Arrange
        var configPath = Path.Combine(TestDataPaths.CompleteSite, "Flint.toml");
        var content = File.ReadAllText(configPath);

        // Assert
        content.Should().Contain("baseURL");
        content.Should().Contain("title");
        content.Should().Contain("[taxonomies]");
        content.Should().Contain("[permalinks]");
        content.Should().Contain("[menus]");
        content.Should().Contain("[params]");
        content.Should().Contain("[markup]");
        content.Should().Contain("[outputs]");
    }

    [Fact]
    public void 多语言站点配置应该包含语言配置()
    {
        // Arrange
        var configPath = Path.Combine(TestDataPaths.MultilingualSite, "Flint.toml");
        var content = File.ReadAllText(configPath);

        // Assert
        content.Should().Contain("[languages]");
        content.Should().Contain("[languages.zh]");
        content.Should().Contain("[languages.en]");
        content.Should().Contain("[languages.ja]");
    }

    #endregion
}
