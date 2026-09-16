// Flint 静态站点生成器
// 主题默认参数合并测试（主题系统 P1-2）：theme.toml [params] 作为默认值，站点深覆盖

using AwesomeAssertions;
using Flint.Core.Configuration;
using Xunit;

namespace Flint.Core.Tests.Configuration;

public sealed class ThemeParamsMergeTests : IDisposable
{
    private readonly string _testDir;

    public ThemeParamsMergeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_theme_params_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void DeepMerge_默认值补缺且站点优先()
    {
        var site = new Dictionary<string, object>
        {
            ["author"] = "site-author",
            ["nested"] = new Dictionary<string, object> { ["keep"] = "site-nested" }
        };
        var theme = new Dictionary<string, object>
        {
            ["author"] = "theme-author",
            ["themeOnly"] = "theme-default",
            ["nested"] = new Dictionary<string, object>
            {
                ["keep"] = "theme-nested",
                ["default"] = "theme-nested-default"
            }
        };

        var merged = ThemeParamsMerger.DeepMerge(site, theme);

        merged["author"].Should().Be("site-author", "站点值覆盖主题默认");
        merged["themeOnly"].Should().Be("theme-default", "仅主题有的键补为默认");
        var nested = (Dictionary<string, object>)merged["nested"];
        nested["keep"].Should().Be("site-nested", "嵌套表递归合并：站点优先");
        nested["default"].Should().Be("theme-nested-default", "嵌套表递归合并：主题默认补缺");
    }

    [Fact]
    public async Task AutoLoadAsync_主题params应作为默认被站点覆盖()
    {
        // Arrange
        var themeDir = Path.Combine(_testDir, "themes", "t-params");
        Directory.CreateDirectory(themeDir);
        File.WriteAllText(Path.Combine(themeDir, "theme.toml"),
            "name = \"t-params\"\n\n[params]\nauthor = \"theme-author\"\nsubtitle = \"theme-sub\"\n");
        File.WriteAllText(Path.Combine(_testDir, "Flint.toml"),
            "baseURL = \"http://x/\"\ntitle = \"T\"\ntheme = \"t-params\"\n\n[params]\nauthor = \"site-author\"\n");
        var loader = new ConfigLoader();

        // Act
        var config = await loader.AutoLoadAsync(_testDir);

        // Assert
        config.Params["author"].Should().Be("site-author", "站点覆盖主题默认");
        config.Params["subtitle"].Should().Be("theme-sub", "主题默认补缺");
    }

    [Fact]
    public async Task AutoLoadAsync_无主题时params应原样()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testDir, "Flint.toml"),
            "baseURL = \"http://x/\"\ntitle = \"T\"\n\n[params]\nauthor = \"only-site\"\n");
        var loader = new ConfigLoader();

        // Act
        var config = await loader.AutoLoadAsync(_testDir);

        // Assert
        config.Params["author"].Should().Be("only-site");
        config.Params.Count.Should().Be(1, "无主题时不引入任何默认参数");
    }

    [Fact]
    public void 主题级taxonomies会合并且跳过Merge指令()
    {
        // Hugo 会把主题配置的 [taxonomies] 合并进站点配置（FixIt 的 hugo.toml 声明
        // `collection = "collections"`，同段还有一行 `_merge = "shallow"`）。
        // `_` 打头的是配置指令**不是分类名**——漏掉这条会让 `_merge = "shallow"`
        // 变成名为 shallow 的分类（实测产出多余的 /shallow/ 页面）
        var themeDir = Path.Combine(_testDir, "themes", "t1");
        Directory.CreateDirectory(themeDir);
        File.WriteAllText(Path.Combine(themeDir, "hugo.toml"),
            "[taxonomies]\n_merge = \"shallow\"\ntag = \"tags\"\ncollection = \"collections\"\n");

        var taxonomies = ThemeParamsMerger.ReadThemeRootConfigTaxonomies(themeDir);

        Assert.NotNull(taxonomies);
        Assert.Equal(["collection", "tag"], taxonomies.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal("collections", taxonomies["collection"]);
    }
}
