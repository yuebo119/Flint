// Flint 静态站点生成器
// 多语言服务单元测试

using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// MultilingualService 单元测试
/// </summary>
public class MultilingualServiceTests
{
    private readonly List<LanguageConfig> _languages;

    public MultilingualServiceTests()
    {
        _languages =
        [
            new() { Code = "en", Name = "English", NativeName = "English", Weight = 0 },
            new() { Code = "zh", Name = "Chinese", NativeName = "中文", Weight = 1 },
            new() { Code = "ja", Name = "Japanese", NativeName = "日本語", Weight = 2 }
        ];
    }

    #region 构造函数测试

    [Fact]
    public void Constructor_应该正确设置默认语言()
    {
        // Act
        var service = new MultilingualService(_languages, "en");

        // Assert
        Assert.Equal("en", service.DefaultLanguage.Code);
        Assert.Equal("English", service.DefaultLanguage.Name);
    }

    [Fact]
    public void Constructor_默认语言不存在时应该创建()
    {
        // Act
        var service = new MultilingualService(_languages, "fr");

        // Assert
        Assert.Equal("fr", service.DefaultLanguage.Code);
    }

    [Fact]
    public void Constructor_应该按权重排序语言()
    {
        // Act
        var service = new MultilingualService(_languages, "en");

        // Assert
        Assert.Equal("en", service.Languages[0].Code);
        Assert.Equal("zh", service.Languages[1].Code);
        Assert.Equal("ja", service.Languages[2].Code);
    }

    #endregion

    #region IsMultilingual 测试

    [Fact]
    public void IsMultilingual_多语言时应该返回true()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Assert
        Assert.True(service.IsMultilingual);
    }

    [Fact]
    public void IsMultilingual_单语言时应该返回false()
    {
        // Arrange
        var singleLang = new List<LanguageConfig>
        {
            new() { Code = "en", Name = "English" }
        };
        var service = new MultilingualService(singleLang, "en");

        // Assert
        Assert.False(service.IsMultilingual);
    }

    #endregion

    #region GetLanguage 测试

    [Fact]
    public void GetLanguage_存在的语言应该返回配置()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var lang = service.GetLanguage("zh");

        // Assert
        Assert.NotNull(lang);
        Assert.Equal("Chinese", lang.Name);
    }

    [Fact]
    public void GetLanguage_不存在的语言应该返回null()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var lang = service.GetLanguage("fr");

        // Assert
        Assert.Null(lang);
    }

    [Fact]
    public void GetLanguage_应该不区分大小写()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var lang = service.GetLanguage("ZH");

        // Assert
        Assert.NotNull(lang);
    }

    #endregion

    #region GenerateUrl 测试 - PathPrefix 策略

    [Fact]
    public void GenerateUrl_PathPrefix_默认语言不添加前缀()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.PathPrefix);

        // Act
        var url = service.GenerateUrl("/about", "en");

        // Assert
        Assert.Equal("/about", url);
    }

    [Fact]
    public void GenerateUrl_PathPrefix_非默认语言添加前缀()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.PathPrefix);

        // Act
        var url = service.GenerateUrl("/about", "zh");

        // Assert
        Assert.Equal("/zh/about", url);
    }

    [Fact]
    public void GenerateUrl_PathPrefix_应该规范化路径()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.PathPrefix);

        // Act
        var url = service.GenerateUrl("about", "zh");

        // Assert
        Assert.Equal("/zh/about", url);
    }

    #endregion

    #region GenerateUrl 测试 - QueryParameter 策略

    [Fact]
    public void GenerateUrl_QueryParameter_应该添加查询参数()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.QueryParameter);

        // Act
        var url = service.GenerateUrl("/about", "zh");

        // Assert
        Assert.Equal("/about?lang=zh", url);
    }

    [Fact]
    public void GenerateUrl_QueryParameter_已有查询参数时应该追加()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.QueryParameter);

        // Act
        var url = service.GenerateUrl("/search?q=test", "zh");

        // Assert
        Assert.Equal("/search?q=test&lang=zh", url);
    }

    #endregion

    #region ExtractLanguageCode 测试

    [Fact]
    public void ExtractLanguageCode_应该从路径提取语言代码()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var code = service.ExtractLanguageCode("/zh/about");

        // Assert
        Assert.Equal("zh", code);
    }

    [Fact]
    public void ExtractLanguageCode_无语言前缀时应该返回null()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var code = service.ExtractLanguageCode("/about");

        // Assert
        Assert.Null(code);
    }

    [Fact]
    public void ExtractLanguageCode_空路径应该返回null()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var code = service.ExtractLanguageCode("");

        // Assert
        Assert.Null(code);
    }

    #endregion

    #region RemoveLanguagePrefix 测试

    [Fact]
    public void RemoveLanguagePrefix_应该移除语言前缀()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var path = service.RemoveLanguagePrefix("/zh/about");

        // Assert
        Assert.Equal("/about", path);
    }

    [Fact]
    public void RemoveLanguagePrefix_无前缀时应该返回原路径()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var path = service.RemoveLanguagePrefix("/about");

        // Assert
        Assert.Equal("/about", path);
    }

    [Fact]
    public void RemoveLanguagePrefix_空路径应该返回根路径()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var path = service.RemoveLanguagePrefix("");

        // Assert
        Assert.Equal("/", path);
    }

    #endregion

    #region GenerateLanguageSwitchLinks 测试

    [Fact]
    public void GenerateLanguageSwitchLinks_应该生成所有语言的链接()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.PathPrefix);

        // Act
        var links = service.GenerateLanguageSwitchLinks("/about", "en");

        // Assert
        Assert.Equal(3, links.Count);
    }

    [Fact]
    public void GenerateLanguageSwitchLinks_应该标记当前语言()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.PathPrefix);

        // Act
        var links = service.GenerateLanguageSwitchLinks("/about", "zh");

        // Assert
        var currentLink = links.Single(l => l.IsCurrent);
        Assert.Equal("zh", currentLink.LanguageCode);
    }

    [Fact]
    public void GenerateLanguageSwitchLinks_应该标记默认语言()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en", MultilingualUrlStrategy.PathPrefix);

        // Act
        var links = service.GenerateLanguageSwitchLinks("/about", "zh");

        // Assert
        var defaultLink = links.Single(l => l.IsDefault);
        Assert.Equal("en", defaultLink.LanguageCode);
    }

    #endregion

    #region GetTranslationPaths 测试

    [Fact]
    public void GetTranslationPaths_应该生成所有语言的路径()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var paths = service.GetTranslationPaths("content/about.md");

        // Assert
        Assert.Equal(3, paths.Count);
        Assert.Contains("en", paths.Keys);
        Assert.Contains("zh", paths.Keys);
        Assert.Contains("ja", paths.Keys);
    }

    [Fact]
    public void GetTranslationPaths_默认语言不添加后缀()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var paths = service.GetTranslationPaths("content/about.md");

        // Assert
        Assert.EndsWith(".md", paths["en"]);
        Assert.DoesNotContain(".en.", paths["en"]);
    }

    [Fact]
    public void GetTranslationPaths_非默认语言添加后缀()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var paths = service.GetTranslationPaths("content/about.md");

        // Assert
        Assert.Contains(".zh.", paths["zh"]);
        Assert.Contains(".ja.", paths["ja"]);
    }

    #endregion

    #region ExtractLanguageFromFileName 测试

    [Fact]
    public void ExtractLanguageFromFileName_应该从文件名提取语言()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act - 正则表达式匹配末尾的语言代码（不含扩展名）
        var lang = service.ExtractLanguageFromFileName("about.zh");

        // Assert
        Assert.Equal("zh", lang);
    }

    [Fact]
    public void ExtractLanguageFromFileName_无语言后缀时返回null()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var lang = service.ExtractLanguageFromFileName("about.md");

        // Assert
        Assert.Null(lang);
    }

    [Fact]
    public void ExtractLanguageFromFileName_未配置的语言返回null()
    {
        // Arrange
        var service = new MultilingualService(_languages, "en");

        // Act
        var lang = service.ExtractLanguageFromFileName("about.fr");

        // Assert
        Assert.Null(lang);
    }

    #endregion
}

/// <summary>
/// LanguageConfig 单元测试
/// </summary>
public class LanguageConfigTests
{
    [Fact]
    public void LanguageConfig_应该正确设置属性()
    {
        // Arrange & Act
        var config = new LanguageConfig
        {
            Code = "zh",
            Name = "Chinese",
            NativeName = "中文",
            Weight = 1,
            IncludeInUrl = true,
            Direction = "ltr",
            DateFormat = "yyyy年MM月dd日",
            TimeFormat = "HH:mm:ss"
        };

        // Assert
        Assert.Equal("zh", config.Code);
        Assert.Equal("Chinese", config.Name);
        Assert.Equal("中文", config.NativeName);
        Assert.Equal(1, config.Weight);
        Assert.True(config.IncludeInUrl);
        Assert.Equal("ltr", config.Direction);
        Assert.Equal("yyyy年MM月dd日", config.DateFormat);
    }

    [Fact]
    public void LanguageConfig_Direction默认为ltr()
    {
        // Arrange & Act
        var config = new LanguageConfig
        {
            Code = "en",
            Name = "English"
        };

        // Assert
        Assert.Equal("ltr", config.Direction);
    }
}

/// <summary>
/// LanguageSwitchLink 单元测试
/// </summary>
public class LanguageSwitchLinkTests
{
    [Fact]
    public void LanguageSwitchLink_应该正确设置属性()
    {
        // Arrange & Act
        var link = new LanguageSwitchLink
        {
            LanguageCode = "zh",
            LanguageName = "Chinese",
            NativeName = "中文",
            Url = "/zh/about",
            IsCurrent = true,
            IsDefault = false
        };

        // Assert
        Assert.Equal("zh", link.LanguageCode);
        Assert.Equal("Chinese", link.LanguageName);
        Assert.Equal("中文", link.NativeName);
        Assert.Equal("/zh/about", link.Url);
        Assert.True(link.IsCurrent);
        Assert.False(link.IsDefault);
    }
}
