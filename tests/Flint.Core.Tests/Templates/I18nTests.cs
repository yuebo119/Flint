// Flint 静态站点生成器
// i18n 翻译加载与模板函数端到端测试（主题系统 P3）

using AwesomeAssertions;
using Flint.Core.Configuration;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>
/// i18n：站点 i18n 覆盖主题、Hugo 形态 [key] other、语言回退链、缺键空串
/// </summary>
public sealed class I18nTests : IDisposable
{
    private readonly string _testDir;

    public I18nTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_i18n_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Load_站点应覆盖主题翻译()
    {
        // Arrange
        var themeI18n = Path.Combine(_testDir, "themes", "t1", "i18n");
        var siteI18n = Path.Combine(_testDir, "i18n");
        Directory.CreateDirectory(themeI18n);
        Directory.CreateDirectory(siteI18n);
        File.WriteAllText(Path.Combine(themeI18n, "zh-cn.toml"),
            "hello = \"主题你好\"\nthemeOnly = \"仅主题\"\n");
        File.WriteAllText(Path.Combine(siteI18n, "zh-cn.toml"),
            "hello = \"站点你好\"\n");

        // Act
        var translations = Translations.Load(_testDir, ["t1"], "zh-cn");

        // Assert
        translations["hello"].Should().Be("站点你好", "站点覆盖主题");
        translations["themeOnly"].Should().Be("仅主题", "主题独有的键补缺");
    }

    [Fact]
    public void Load_Hugo形态other键应被采用()
    {
        // Arrange
        var i18n = Path.Combine(_testDir, "i18n");
        Directory.CreateDirectory(i18n);
        File.WriteAllText(Path.Combine(i18n, "en.toml"),
            "[greeting]\nother = \"Hello\"\n");

        // Act
        var translations = Translations.Load(_testDir, null, "en");

        // Assert
        translations["greeting"].Should().Be("Hello", "Hugo [key] other 形态兼容");
    }

    [Fact]
    public void Load_精确语言缺失应回退主段()
    {
        // Arrange：只有 zh.toml，请求 zh-cn
        var i18n = Path.Combine(_testDir, "i18n");
        Directory.CreateDirectory(i18n);
        File.WriteAllText(Path.Combine(i18n, "zh.toml"), "hello = \"你好\"\n");

        // Act
        var translations = Translations.Load(_testDir, null, "zh-cn");

        // Assert
        translations["hello"].Should().Be("你好", "zh-cn 缺文件时回退主段 zh");
    }
}
