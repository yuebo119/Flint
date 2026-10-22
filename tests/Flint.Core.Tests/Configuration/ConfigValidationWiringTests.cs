// Flint 静态站点生成器
// 配置加载链终态化契约测试
//
// 回归防护：
//   1. 校验对象必须是**应用环境变量覆盖之后**的配置（此前先校验后覆盖，
//      `Flint_CONTENTDIR=../../x` 可绕过全部校验）
//   2. 目录式配置（config/_default/）此前直接返回，校验与覆盖双双失效
//   3. theme 名参与 themes/<name> 路径拼接，必须与其余目录同口径校验
//   4. 文档化的 FLINT_PARAMS_* / FLINT_TIME_ZONE 覆盖必须有实现

using Flint.Core.Configuration;
using Xunit;

namespace Flint.Core.Tests.Configuration;

[Collection("EnvironmentSensitive")]
public sealed class ConfigValidationWiringTests : IDisposable
{
    private readonly string _siteDir;
    private readonly List<string> _setEnvVars = [];

    public ConfigValidationWiringTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"Flint_cfgwire_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteDir);
    }

    public void Dispose()
    {
        foreach (var key in _setEnvVars)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        if (Directory.Exists(_siteDir))
        {
            Directory.Delete(_siteDir, true);
        }

        GC.SuppressFinalize(this);
    }

    private void SetEnvVar(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
        _setEnvVars.Add(key);
    }

    private string WriteConfig(string toml)
    {
        var path = Path.Combine(_siteDir, "hugo.toml");
        File.WriteAllText(path, toml);
        return path;
    }

    [Fact]
    public async Task 环境变量覆盖后仍受校验约束()
    {
        var path = WriteConfig("baseURL = \"https://example.com/\"\ntitle = \"T\"\n");
        SetEnvVar("Flint_CONTENTDIR", "../../escape");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new ConfigLoader().LoadAsync(path).ConfigureAwait(false));

        Assert.Contains("contentDir", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 目录式配置同样走校验()
    {
        var configDir = Path.Combine(_siteDir, "config", "_default");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(
            Path.Combine(configDir, "hugo.toml"),
            "baseURL = \"https://example.com/\"\ntitle = \"T\"\npublishDir = \"../outside\"\n");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new ConfigLoader().AutoLoadAsync(_siteDir).ConfigureAwait(false));

        Assert.Contains("publishDir", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task theme名路径穿越被拒绝()
    {
        var path = WriteConfig("baseURL = \"https://example.com/\"\ntitle = \"T\"\ntheme = \"../../evil\"\n");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await new ConfigLoader().LoadAsync(path).ConfigureAwait(false));

        Assert.Contains("theme", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 合法theme名不受影响()
    {
        var path = WriteConfig("baseURL = \"https://example.com/\"\ntitle = \"T\"\ntheme = \"ananke\"\n");

        var config = await new ConfigLoader().LoadAsync(path);

        Assert.Equal("ananke", config.Theme);
    }

    [Fact]
    public async Task FLINT_PARAMS前缀注入站点参数()
    {
        var path = WriteConfig("baseURL = \"https://example.com/\"\ntitle = \"T\"\n[params]\n  author = \"config\"\n");
        SetEnvVar("Flint_PARAMS_AUTHOR", "env-author");

        var config = await new ConfigLoader().LoadAsync(path);

        Assert.Equal("env-author", config.Params["author"]?.ToString());
    }

    [Fact]
    public async Task FLINT_TIME_ZONE生效()
    {
        var path = WriteConfig("baseURL = \"https://example.com/\"\ntitle = \"T\"\n");
        SetEnvVar("Flint_TIME_ZONE", "UTC");

        var config = await new ConfigLoader().LoadAsync(path);

        Assert.Equal("UTC", config.TimeZone);
    }
}