// Flint 静态站点生成器
// 配置验证器单元测试

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Xunit;

namespace Flint.Core.Tests.Configuration;

/// <summary>
/// ConfigValidator 单元测试
/// </summary>
public class ConfigValidatorTests
{
    private readonly ConfigValidator _validator = new();

    #region 必填字段验证

    [Fact]
    public void Validate_有效配置应该通过验证()
    {
        // Arrange
        var config = CreateValidConfig();

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_BaseURL为空时应该失败()
    {
        // Arrange
        var config = CreateConfig(baseUrl: "", title: "Test");

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "baseURL" && e.ErrorCode == "CFG001");
    }

    [Fact]
    public void Validate_Title为空时应该失败()
    {
        // Arrange
        var config = CreateConfig(baseUrl: "https://example.com/", title: "");

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "title" && e.ErrorCode == "CFG002");
    }

    #endregion

    #region URL 格式验证

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://localhost:1313/")]
    [InlineData("https://sub.domain.example.com/path/")]
    public void Validate_有效URL应该通过(string siteAddress)
    {
        // Arrange
        var config = CreateConfig(baseUrl: siteAddress, title: "Test");

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ftp://example.com/")]
    [InlineData("not a url")]
    public void Validate_无效URL应该失败(string siteAddress)
    {
        // Arrange
        var config = CreateConfig(baseUrl: siteAddress, title: "Test");

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "baseURL" && e.ErrorCode == "CFG003");
    }

    #endregion

    #region 数值范围验证

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(-1)]
    public void Validate_Paginate超出范围应该失败(int paginate)
    {
        // Arrange
        var config = CreateConfig(paginate: paginate);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "paginate" && e.ErrorCode == "CFG004");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Validate_Paginate在有效范围内应该通过(int paginate)
    {
        // Arrange
        var config = CreateConfig(paginate: paginate);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1001)]
    public void Validate_SummaryLength超出范围应该失败(int length)
    {
        // Arrange
        var config = CreateConfig(summaryLength: length);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "summaryLength");
    }

    #endregion

    #region 目录级别验证

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Validate_TableOfContents_StartLevel超出范围应该失败(int level)
    {
        // Arrange
        var config = CreateConfig(tocStartLevel: level, tocEndLevel: 4);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "markup.tableOfContents.startLevel");
    }

    [Fact]
    public void Validate_StartLevel大于EndLevel应该失败()
    {
        // Arrange
        var config = CreateConfig(tocStartLevel: 4, tocEndLevel: 2);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "markup.tableOfContents" && e.ErrorCode == "CFG005");
    }

    #endregion

    #region 路径验证

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_路径为空应该失败(string path)
    {
        // Arrange
        var config = CreateConfig(contentDir: path);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "contentDir" && e.ErrorCode == "CFG006");
    }

    [Theory]
    [InlineData("../parent")]
    [InlineData("path/../other")]
    [InlineData("..")]
    public void Validate_包含父目录引用应该失败(string path)
    {
        // Arrange
        var config = CreateConfig(contentDir: path);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "contentDir" && e.ErrorCode == "CFG008");
    }

    [Theory]
    [InlineData("content")]
    [InlineData("src/content")]
    [InlineData("my-content")]
    public void Validate_有效路径应该通过(string path)
    {
        // Arrange
        var config = CreateConfig(contentDir: path);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region 语言代码验证

    [Theory]
    [InlineData("en")]
    [InlineData("zh")]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void Validate_有效语言代码应该通过(string code)
    {
        // Arrange
        var config = CreateConfig(languageCode: code);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("english")]
    [InlineData("e")]
    [InlineData("en_US")]
    [InlineData("123")]
    public void Validate_无效语言代码应该失败(string code)
    {
        // Arrange
        var config = CreateConfig(languageCode: code);

        // Act
        var result = _validator.Validate(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "languageCode" && e.ErrorCode == "CFG009");
    }

    #endregion

    #region 辅助方法

    private static SiteConfig CreateValidConfig()
    {
        return CreateConfig();
    }

    private static SiteConfig CreateConfig(
        string baseUrl = "https://example.com/",
        string title = "Test Site",
        string languageCode = "en",
        string contentDir = "content",
        string layoutDir = "layouts",
        string staticDir = "static",
        string assetDir = "assets",
        string dataDir = "data",
        string publishDir = "public",
        string archetypeDir = "archetypes",
        int paginate = 10,
        int summaryLength = 70,
        int tocStartLevel = 2,
        int tocEndLevel = 3)
    {
        return new SiteConfig
        {
            BaseURL = baseUrl,
            Title = title,
            LanguageCode = languageCode,
            ContentDir = contentDir,
            LayoutDir = layoutDir,
            StaticDir = staticDir,
            AssetDir = assetDir,
            DataDir = dataDir,
            PublishDir = publishDir,
            ArchetypeDir = archetypeDir,
            Paginate = paginate,
            SummaryLength = summaryLength,
            Markup = new MarkupConfig
            {
                TableOfContents = new TableOfContentsConfig
                {
                    StartLevel = tocStartLevel,
                    EndLevel = tocEndLevel
                }
            }
        };
    }

    #endregion
}
