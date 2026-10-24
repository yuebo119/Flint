// Flint 静态站点生成器
// 模块身份与版本显示契约测试
//
// 回归防护：zip/Releases 安装把模块落在 `themes/<仓库名>`、lockfile 键也取仓库名，
// 而 theme.toml 的自述名常与仓库名不同（panr/hugo-theme-terminal 自述名为 Terminal）。
// 此前 `mod list` 按自述名显示、版本取 theme.toml 的默认 1.0.0，于是
// ① list 显示的名字不能用于 `theme = "..."`；② 显示的版本不是实际安装版本；
// ③ `mod remove <显示的名字>` 报"模块未安装"。

using Flint.Core.Modules;
using Xunit;

namespace Flint.Core.Tests.Modules;

public sealed class ModuleIdentityTests : IDisposable
{
    private readonly string _siteDir;
    private readonly ModuleManager _manager;

    public ModuleIdentityTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"Flint_modid_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteDir);
        _manager = new ModuleManager(_siteDir);
    }

    public void Dispose()
    {
        _manager.Dispose();
        if (Directory.Exists(_siteDir))
        {
            Directory.Delete(_siteDir, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>模拟 zip 安装形态：目录名=仓库名，theme.toml 自述名不同</summary>
    private void SeedZipInstalledModule()
    {
        var dir = Path.Combine(_siteDir, "themes", "hugo-theme-terminal");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "theme.toml"),
            "name = \"Terminal\"\ndescription = \"主题自述\"\nlicense = \"MIT\"\n");
        File.WriteAllText(Path.Combine(_siteDir, "Flint.lock"),
            "[\"hugo-theme-terminal\"]\n\"version\" = \"v4.2.6\"\n\"repository\" = \"\"\n" +
            "\"sha256\" = \"cf0aedf141411c38fed4289ece3c8e1775acb8529f4cc54792d58b6eb5683b89\"\n");
    }

    [Fact]
    public async Task list显示自述名与lockfile里的实际版本并给出目录名()
    {
        SeedZipInstalledModule();

        var modules = await _manager.ListAsync();

        var module = Assert.Single(modules);
        // 自述名留给人读；身份看 LocalPath 的目录名；版本取实际安装版本
        Assert.Equal("Terminal", module.Name);
        Assert.Equal("v4.2.6", module.Version);
        Assert.EndsWith("hugo-theme-terminal", module.LocalPath, StringComparison.Ordinal);
        Assert.Equal("主题自述", module.Description);
    }

    [Fact]
    public async Task remove接受自述名并清掉目录与lock条目()
    {
        SeedZipInstalledModule();

        await _manager.RemoveAsync("Terminal");

        Assert.False(Directory.Exists(Path.Combine(_siteDir, "themes", "hugo-theme-terminal")));
        Assert.DoesNotContain("hugo-theme-terminal", File.ReadAllText(Path.Combine(_siteDir, "Flint.lock")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task remove接受目录名()
    {
        SeedZipInstalledModule();

        await _manager.RemoveAsync("hugo-theme-terminal");

        Assert.Empty(Directory.GetDirectories(Path.Combine(_siteDir, "themes")));
    }

    [Fact]
    public async Task remove名字不存在时报模块未安装()
    {
        SeedZipInstalledModule();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await _manager.RemoveAsync("no-such-theme").ConfigureAwait(false));

        Assert.Contains("模块未安装", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task list在无lock条目时回退theme的自述版本()
    {
        var dir = Path.Combine(_siteDir, "themes", "local-theme");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "theme.toml"), "name = \"LocalTheme\"\nversion = \"0.3.0\"\n");

        var modules = await _manager.ListAsync();

        var module = Assert.Single(modules);
        Assert.Equal("LocalTheme", module.Name);
        Assert.Equal("0.3.0", module.Version);
        Assert.EndsWith("local-theme", module.LocalPath, StringComparison.Ordinal);
    }
}