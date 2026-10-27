// Flint 静态站点生成器
// 增量构建的分类页裁剪：改 tags 只重产受影响词条页，未受影响词条页保持原样
//
// 背景（2026-10-27）：此前 taxonomy 签名一变（任何 tags/categories 改动）
// 就走全量分类页重产——fixit 106 页 / 508 词条页实测一次 tags 编辑 16.5s。
// 现按"变化页涉及的全部词条（旧 + 新）"裁剪，列表页仍全量。

using AwesomeAssertions;
using Flint.Core.Models;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
public sealed class TermScopedIncrementalTests : IDisposable
{
    private readonly string _siteRoot;

    public TermScopedIncrementalTests()
    {
        _siteRoot = Path.Combine(Path.GetTempPath(), $"flint-termincr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_siteRoot))
            {
                Directory.Delete(_siteRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // 杀软瞬时锁：清理失败不影响测试结论
        }
    }

    private BuildOptions Options() => new()
    {
        SourcePath = _siteRoot,
        OutputPath = Path.Combine(_siteRoot, "public")
    };

    private void WriteSite()
    {
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "posts"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));

        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"term-incr\"\n");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<article>{{ page.title }}</article>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "list.html"),
            "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "term.html"),
            "<div class=\"term\">{{ page.title }}|{{ for p in page.pages }}{{ p.title }};{{ end }}</div>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "taxonomy.html"),
            "<div class=\"tax\">{{ for t in page.pages }}{{ t.title }}={{ len t.pages }};{{ end }}</div>");

        WritePost("p1", "P1", "alpha");
        WritePost("p2", "P2", "beta");
        WritePost("p3", "P3", "beta");
    }

    private void WritePost(string name, string title, string tags) =>
        File.WriteAllText(
            Path.Combine(_siteRoot, "content", "posts", name + ".md"),
            $"---\ntitle: \"{title}\"\ndate: 2026-01-0{name[^1]}T10:00:00+08:00\ntags: [{tags}]\n---\n\nBody {title}\n");

    private string OutPath(params string[] parts) =>
        Path.Combine([_siteRoot, "public", .. parts]);

    private static DateTime FixedOldTime() => new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task 改tags_只重产受影响词条页_未受影响词条页不重写()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var alpha = OutPath("tags", "alpha", "index.html");
        var beta = OutPath("tags", "beta", "index.html");
        var list = OutPath("tags", "index.html");
        File.Exists(alpha).Should().BeTrue("前置条件：alpha 词条页已产出");
        File.Exists(beta).Should().BeTrue("前置条件：beta 词条页已产出");
        File.Exists(list).Should().BeTrue("前置条件：tags 列表页已产出");

        // 固定 mtime：未受影响词条页若被重写，mtime 会变化
        File.SetLastWriteTimeUtc(beta, FixedOldTime());

        // p1 从 [alpha] 改为 [alpha, gamma]：受影响词条 = alpha（旧+新）、gamma（新）
        WritePost("p1", "P1", "alpha, gamma");
        var result = await builder.IncrementalBuildAsync(
            Options(), [Path.Combine(_siteRoot, "content", "posts", "p1.md")]);
        result.Success.Should().BeTrue();

        File.Exists(OutPath("tags", "gamma", "index.html"))
            .Should().BeTrue("新词条必须产出页面");
        File.ReadAllText(OutPath("tags", "gamma", "index.html"))
            .Should().Contain("P1", "新词条页必须包含该文章");
        File.GetLastWriteTimeUtc(alpha).Should().BeAfter(FixedOldTime(),
            "受影响词条页必须重写（alpha 属于变化页）");
        File.ReadAllText(list).Should().Contain("gamma", "列表页必须反映新词条");
        File.GetLastWriteTimeUtc(beta).Should().Be(FixedOldTime(),
            "未受影响词条页不得重写（增量裁剪的核心契约）");
    }

    [Fact]
    public async Task 删除页面_原词条页重产且不含被删页()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var beta = OutPath("tags", "beta", "index.html");
        File.ReadAllText(beta).Should().Contain("P2").And.Contain("P3");

        // 删除 p2：beta 词条仍有 p3，词条页必须重产且去掉 P2（旧侧词条也计入裁剪集）
        var p2 = Path.Combine(_siteRoot, "content", "posts", "p2.md");
        File.Delete(p2);
        var result = await builder.IncrementalBuildAsync(Options(), [p2]);
        result.Success.Should().BeTrue();

        File.Exists(beta).Should().BeTrue("beta 词条仍有 p3，页面必须存在");
        var html = File.ReadAllText(beta);
        html.Should().Contain("P3");
        html.Should().NotContain("P2", "被删页必须从词条页消失");
    }

    [Fact]
    public async Task 模板变化_词条页全量重产_不做裁剪()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var beta = OutPath("tags", "beta", "index.html");
        File.SetLastWriteTimeUtc(beta, FixedOldTime());

        // 改 term 模板：所有词条页外观都可能变化，裁剪必须整体关闭
        var termTemplate = Path.Combine(_siteRoot, "layouts", "term.html");
        File.WriteAllText(termTemplate,
            "<div class=\"term v2\">{{ page.title }}|{{ for p in page.pages }}{{ p.title }};{{ end }}</div>");
        var result = await builder.IncrementalBuildAsync(Options(), [termTemplate]);
        result.Success.Should().BeTrue();

        File.GetLastWriteTimeUtc(beta).Should().BeAfter(FixedOldTime(),
            "模板变化时不得裁剪，未受内容影响的词条页也要按新模板重产");
        File.ReadAllText(beta).Should().Contain("term v2");
    }

    [Fact]
    public async Task 自定义taxonomy改值_触发分类页重产()
    {
        // 回归锁定：taxonomy 签名此前只聚合 tags/categories，改自定义分类
        // （值在 front matter 的 Params 里）不改变签名 → 增量判定"分类页无需重产"
        // → watch 下留旧产物。签名必须覆盖站点声明的全部分类
        WriteSite();
        var configPath = Path.Combine(_siteRoot, "Flint.toml");
        File.WriteAllText(configPath,
            "baseURL = \"http://localhost:1313/\"\ntitle = \"term-incr\"\n\n[taxonomies]\n  series = \"series\"\n");
        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "p1.md"),
            "---\ntitle: \"P1\"\ndate: 2026-01-01T10:00:00+08:00\ntags: [alpha]\nseries: [s1]\n---\n\nBody P1\n");

        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var seriesPage = OutPath("series", "s1", "index.html");
        File.Exists(seriesPage).Should().BeTrue("前置条件：自定义分类词条页已产出");

        // p1 换到 s2：s1 与 s2 两个词条页都必须重产
        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "p1.md"),
            "---\ntitle: \"P1\"\ndate: 2026-01-01T10:00:00+08:00\ntags: [alpha]\nseries: [s2]\n---\n\nBody P1\n");
        var result = await builder.IncrementalBuildAsync(
            Options(), [Path.Combine(_siteRoot, "content", "posts", "p1.md")]);
        result.Success.Should().BeTrue();

        File.Exists(OutPath("series", "s2", "index.html"))
            .Should().BeTrue("新自定义词条必须产出页面（签名需覆盖自定义 taxonomy）");
        File.ReadAllText(OutPath("series", "s2", "index.html")).Should().Contain("P1");
        File.Exists(seriesPage).Should().BeTrue("旧词条页仍应存在（其他页面可能仍属于它）");
    }
}
