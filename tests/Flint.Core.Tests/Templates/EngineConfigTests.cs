// Flint 静态站点生成器
// engine 配置键测试（FLUID-MIGRATION-PLAN P0-3）：TemplateEnginePolicy
// 经 SiteConfig.Engine 从三种配置格式读入，无法识别回落 auto。

using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class EngineConfigTests
{
    [Theory]
    [InlineData("engine = \"fluid\"", "fluid")]
    [InlineData("engine = \"scriban\"", "scriban")]
    [InlineData("engine = \"auto\"", "auto")]
    [InlineData("templateEngine = \"fluid\"", "fluid")]
    public void TOML_engine_键读入(string engineLine, string expected)
    {
        var config = ConfigParser.ParseToml($"""
            baseURL = "http://localhost:1313/"
            title = "t"
            {engineLine}
            """);

        Assert.Equal(expected, config.Engine);
    }

    [Fact]
    public void 缺省_engine_为_auto()
    {
        var config = ConfigParser.ParseToml("""
            baseURL = "http://localhost:1313/"
            title = "t"
            """);

        Assert.Equal("auto", config.Engine);
        Assert.Equal(TemplateEnginePolicy.Auto, TemplateEngineRouter.ParsePolicy(config));
    }

    [Theory]
    [InlineData("fluid", TemplateEnginePolicy.Fluid)]
    [InlineData("FLUID", TemplateEnginePolicy.Fluid)]
    [InlineData("liquid", TemplateEnginePolicy.Fluid)]
    [InlineData("scriban", TemplateEnginePolicy.Scriban)]
    [InlineData("auto", TemplateEnginePolicy.Auto)]
    [InlineData("garbage", TemplateEnginePolicy.Auto)]
    [InlineData("", TemplateEnginePolicy.Auto)]
    public void ParsePolicy_识别与回落(string engine, TemplateEnginePolicy expected)
    {
        Assert.Equal(expected, TemplateEngineRouter.ParsePolicy(engine));
    }

    [Fact]
    public void 序列化_非默认_engine_才写回()
    {
        var auto = ConfigParser.ParseToml("baseURL = \"http://x/\"\ntitle = \"t\"");
        var fluid = ConfigParser.ParseToml("baseURL = \"http://x/\"\ntitle = \"t\"\nengine = \"fluid\"");

        Assert.DoesNotContain("engine", ConfigParser.SerializeToml(auto));
        Assert.Contains("engine", ConfigParser.SerializeToml(fluid));
    }

    [Fact]
    public void ThemeParamsMerger_重建保留_engine()
    {
        // 回归锁（2026-10-19）：RebuildWithParams 显式重建 SiteConfig 时曾丢
        // Engine 属性——解析出来的 engine=scriban 经主题参数合并后静默回落
        // auto，gating（.liquid 短代码分发）失效。EnvironmentOverrides 的
        // 重建块同族，一并覆盖
        var scriban = ConfigParser.ParseToml("baseURL = \"http://x/\"\ntitle = \"t\"\nengine = \"scriban\"");
        var merged = ThemeParamsMerger.Merge(scriban, Path.GetTempPath());

        Assert.Equal("scriban", merged.Engine);
        Assert.Equal(TemplateEnginePolicy.Scriban, TemplateEngineRouter.ParsePolicy(merged));
    }
}
