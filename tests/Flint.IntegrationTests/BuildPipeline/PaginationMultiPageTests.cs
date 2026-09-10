// Flint 静态站点生成器
// 分页多页产出集成测试（C1）
// 验证列表页按 paginate 切片产出 /page/N/，Paginator 逐页绑定

using Flint.IntegrationTests.Fixtures;
using AwesomeAssertions;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// C1 分页多页产出：home/section 列表页按站点 paginate 切片，
/// 逐页产出 /page/2/、/page/3/ 且每页 Paginator 绑定各自切片
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
[Trait("TestType", "Pagination")]
public class PaginationMultiPageTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;

    public PaginationMultiPageTests()
    {
        _fixture = new TestSiteFixture();
    }

    public async ValueTask InitializeAsync() => await _fixture.InitializeAsync();

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    private const string ListTemplate = """
        LIST n={{ page.paginator.page_number }}/{{ page.paginator.total_pages }} url={{ page.paginator.url }}
        ITEMS:{{ for p in page.paginator.pages }}[{{ p.title }}]{{ end }}
        FIRST={{ paginator.first.url }} LAST={{ paginator.last.url }}
        PREV={{ if paginator.prev }}{{ paginator.prev.url }}{{ else }}NONE{{ end }} NEXT={{ if paginator.next }}{{ paginator.next.url }}{{ else }}NONE{{ end }}
        """;

    private async Task PrepareSiteAsync(int postCount, int paginate)
    {
        // minimal 夹具自带 content/index.md（home）与各 kind 模板；只覆盖需要断言的
        await _fixture.CreateSiteAsync("minimal");
        await _fixture.SetConfigAsync($$"""
            baseURL = "https://example.org/"
            title = "Pagination Test"
            paginate = {{paginate}}
            paginatePath = "page"
            """);
        await _fixture.AddTemplateAsync("_default/list.html", ListTemplate);
        await _fixture.AddTemplateAsync("index.html", ListTemplate);
        await _fixture.AddTemplateAsync("_default/single.html", "SINGLE {{ page.title }}");
        await _fixture.AddContentAsync("posts/_index.md", "---\ntitle: Posts\n---\nPosts");

        for (var i = 1; i <= postCount; i++)
        {
            // 日期倒序保证分页顺序稳定（P{n} 最新在前）
            await _fixture.AddContentAsync(
                $"posts/post-{i:00}.md",
                $"---\ntitle: P{i}\ndate: 2026-01-{i:00}T00:00:00+00:00\n---\nBody {i}");
        }
    }

    [Fact]
    public async Task 分页多页_产出列表页与分页页()
    {
        await PrepareSiteAsync(postCount: 8, paginate: 3);

        var result = await _fixture.BuildAsync();

        result.Success.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        _fixture.OutputFileExists(Path.Combine("posts", "index.html")).Should().BeTrue();
        _fixture.OutputFileExists(Path.Combine("posts", "page", "2", "index.html")).Should().BeTrue();
        _fixture.OutputFileExists(Path.Combine("posts", "page", "3", "index.html")).Should().BeTrue();
        // 8 篇 / 每页 3 → 3 页；第 4 页不应存在
        _fixture.OutputFileExists(Path.Combine("posts", "page", "4", "index.html")).Should().BeFalse();
    }

    [Fact]
    public async Task 分页逐页绑定_每页切片与页码互不相同()
    {
        await PrepareSiteAsync(postCount: 8, paginate: 3);

        await _fixture.BuildAsync();

        var page1 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "index.html"));
        var page2 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "page", "2", "index.html"));
        var page3 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "page", "3", "index.html"));

        page1.Should().Contain("LIST n=1/3").And.Contain("url=/posts/");
        page2.Should().Contain("LIST n=2/3").And.Contain("url=/posts/page/2/");
        page3.Should().Contain("LIST n=3/3").And.Contain("url=/posts/page/3/");

        // 切片互不相同且合计覆盖全部
        page1.Should().Contain("[P8][P7][P6]");
        page2.Should().Contain("[P5][P4][P3]");
        page3.Should().Contain("[P2][P1]");
    }

    [Fact]
    public async Task 分页导航_首末前后URL正确()
    {
        await PrepareSiteAsync(postCount: 8, paginate: 3);

        await _fixture.BuildAsync();

        var page1 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "index.html"));
        var page2 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "page", "2", "index.html"));
        var page3 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "page", "3", "index.html"));

        page1.Should().Contain("FIRST=/posts/ LAST=/posts/page/3/")
            .And.Contain("PREV=NONE NEXT=/posts/page/2/");
        page2.Should().Contain("PREV=/posts/ NEXT=/posts/page/3/");
        page3.Should().Contain("PREV=/posts/page/2/ NEXT=NONE");
    }

    [Fact]
    public async Task 首页分页_根路径产出page目录()
    {
        await PrepareSiteAsync(postCount: 8, paginate: 3);

        await _fixture.BuildAsync();

        _fixture.OutputFileExists("index.html").Should().BeTrue();
        _fixture.OutputFileExists(Path.Combine("page", "2", "index.html")).Should().BeTrue();
        _fixture.OutputFileExists(Path.Combine("page", "3", "index.html")).Should().BeTrue();

        var home = await _fixture.GetOutputFileAsync("index.html");
        home.Should().Contain("url=/");
    }

    [Fact]
    public async Task 内容不足一页_只有列表页本身()
    {
        await PrepareSiteAsync(postCount: 2, paginate: 3);

        await _fixture.BuildAsync();

        _fixture.OutputFileExists(Path.Combine("posts", "index.html")).Should().BeTrue();
        _fixture.OutputFileExists(Path.Combine("posts", "page", "2", "index.html")).Should().BeFalse();

        var page1 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "index.html"));
        page1.Should().Contain("LIST n=1/1").And.Contain("PREV=NONE NEXT=NONE");
    }

    [Fact]
    public async Task 单页也绑定分页器_总数为一()
    {
        await PrepareSiteAsync(postCount: 2, paginate: 3);

        await _fixture.BuildAsync();

        var page1 = await _fixture.GetOutputFileAsync(Path.Combine("posts", "index.html"));
        page1.Should().Contain("FIRST=/posts/ LAST=/posts/");
    }

    [Fact]
    public async Task 非列表页不受分页影响_单页走single()
    {
        await PrepareSiteAsync(postCount: 4, paginate: 2);

        await _fixture.BuildAsync();

        var single = await _fixture.GetOutputFileAsync(Path.Combine("posts", "post-01", "index.html"));
        single.Should().Be("SINGLE P1");
    }
}
