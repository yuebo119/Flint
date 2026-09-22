// Flint 静态站点生成器
// baseURL 子路径（多主题站归并单一端口的形态）回归测试：
//   1. aux 输出（robots.txt / 404.html）落盘在 `<sub>/` 下且保持**文件形态**
//      （不走 GetOutputPath 的 index.html 目录化——ananke/bearblog 实测报
//      "找不到路径的一部分"）
//   2. WithPaginator 的绝对 URL 只拼源站，不二次叠加子路径（stack 主题首页
//      canonical 实测 /stack/stack/）

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>baseURL 子路径构建的辅助输出与分页 URL 回归</summary>
public sealed class SubpathAuxOutputTests : IDisposable
{
    private sealed class StubAssetPipeline : IAssetPipeline
    {
        public ValueTask<ProcessedAsset> ProcessAsync(AssetFile asset, System.Threading.CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProcessedAsset
            {
                OutputPath = asset.SourcePath,
                SourcePath = asset.SourcePath,
                Content = asset.Content,
                MediaType = asset.MediaType,
                ContentHash = "test-hash"
            });

        public async ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
            IReadOnlyList<AssetFile> assets, System.Threading.CancellationToken cancellationToken = default)
        {
            var results = new List<ProcessedAsset>();
            foreach (var asset in assets)
            {
                results.Add(await ProcessAsync(asset, cancellationToken).ConfigureAwait(false));
            }
            return results;
        }

        public async IAsyncEnumerable<ProcessedAsset> ProcessStreamAsync(
            IAsyncEnumerable<AssetFile> assets,
            [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken cancellationToken = default)
        {
            await foreach (var asset in assets.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return await ProcessAsync(asset, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private readonly string _siteDir;
    private readonly string _outputDir;
    private readonly IAssetPipeline _assetPipeline = new StubAssetPipeline();

    public SubpathAuxOutputTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"flint-sub-{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_siteDir, "public");
        Directory.CreateDirectory(_siteDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteDir)) Directory.Delete(_siteDir, recursive: true); }
        catch (IOException) { }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_siteDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<BuildResult> BuildAsync(string baseUrl, params string[] themeNames)
    {
        var themeLayoutDirs = themeNames
            .Select(t => Path.Combine(_siteDir, "themes", t, "layouts"))
            .ToArray();
        var builder = new SiteBuilder(
            new ContentParser(),
            new ScribanTemplateRenderer(
                Path.Combine(_siteDir, "layouts"), baseUrl, themeLayoutDirs),
            _assetPipeline,
            new ConfigLoader());
        var options = new BuildOptions
        {
            SourcePath = _siteDir,
            OutputPath = _outputDir,
            Parallelism = 1
        };
        return await builder.BuildAsync(options).ConfigureAwait(false);
    }

    [Fact]
    public async Task 子路径构建_robots与404落到子目录且保持文件形态()
    {
        Write("Flint.toml", "baseURL = \"https://example.com/stack/\"\ntitle = \"T\"\ntheme = \"t1\"\n");
        Write(Path.Combine("themes", "t1", "layouts", "robots.txt"), "User-agent: *\nDisallow:\n");
        Write(Path.Combine("themes", "t1", "layouts", "404.html"), "<h1>404</h1>");

        var result = await BuildAsync("https://example.com/stack/", "t1");
        if (!result.Success)
        {
            Assert.Fail("构建失败: " + string.Join(" | ", result.Errors.Select(e => $"{e.ErrorCode}:{e.FilePath}:{e.Message}")));
        }

        // 文件形态：robots.txt 是文件不是目录（旧路径解析成 robots.txt/index.html
        // 会因父目录缺失写出失败——ananke/bearblog 带 robots.txt 模板时实测）
        Assert.True(File.Exists(Path.Combine(_outputDir, "stack", "robots.txt")));
        Assert.False(File.Exists(Path.Combine(_outputDir, "stack", "robots.txt", "index.html")));
        Assert.True(File.Exists(Path.Combine(_outputDir, "stack", "404.html")));
        // 子路径前缀外的根级不得有残留（统一树按子目录归并的前提）
        Assert.False(File.Exists(Path.Combine(_outputDir, "robots.txt")));
        Assert.False(File.Exists(Path.Combine(_outputDir, "404.html")));
    }

    [Fact]
    public async Task 子路径构建_资源与页面链接均带子路径且不叠加()
    {
        Write("Flint.toml", "baseURL = \"https://example.com/stack/\"\ntitle = \"T\"\n");
        Write(Path.Combine("layouts", "_default", "single.html"),
            "<html><head><link rel=\"canonical\" href=\"{{ page.permalink }}\"></head>" +
            "<body>{{ page.rel_permalink }}</body></html>");
        Write(Path.Combine("content", "posts", "_index.md"), "---\ntitle: Posts\n---\n");
        Write(Path.Combine("content", "posts", "a.md"), "---\ntitle: A\ndate: 2026-01-01\n---\nbody");

        var result = await BuildAsync("https://example.com/stack/");
        Assert.True(result.Success, string.Join(";", result.Errors.Select(e => e.Message)));

        var html = File.ReadAllText(Path.Combine(_outputDir, "stack", "posts", "a", "index.html"));
        Assert.Contains("<link rel=\"canonical\" href=\"https://example.com/stack/posts/a/\">", html);
        Assert.Contains("/stack/posts/a/", html);
    }

    [Fact]
    public async Task 子路径构建_GetPage按内容根路径仍能命中段页()
    {
        // hugo-book 的 menu-section：`.GetPage .Params.BookSection`（值 "posts"）——
        // 子路径构建时段页 RelPermalink 是 `/stack/posts/`，查询路径 "posts" 必须先剥掉
        // baseURL 子路径才能命中。修复前返回 null → errorf "Section 'posts' not found"
        Write("Flint.toml", "baseURL = \"https://example.com/stack/\"\ntitle = \"T\"\n");
        Write(Path.Combine("layouts", "_default", "single.html"),
            "{{ $s = site.get_page \"posts\" }}section=[{{ $s?.title }}] rel=[{{ $s?.rel_permalink }}]");
        Write(Path.Combine("content", "posts", "_index.md"), "---\ntitle: 文章段\n---\n");
        Write(Path.Combine("content", "posts", "a.md"), "---\ntitle: A\ndate: 2026-01-01\n---\nbody");

        var result = await BuildAsync("https://example.com/stack/");
        Assert.True(result.Success, string.Join(";", result.Errors.Select(e => e.Message)));

        var html = File.ReadAllText(Path.Combine(_outputDir, "stack", "posts", "a", "index.html"));
        Assert.Contains("section=[文章段]", html);
        Assert.Contains("rel=[/stack/posts/]", html);
    }

    private static PageContext MakePage(string title, string rel) => new()
    {
        Title = title,
        Content = "c",
        Permalink = $"https://example.com{rel}",
        RelPermalink = rel,
        Date = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Tags = [],
        Categories = [],
        WordCount = 1,
        ReadingTime = TimeSpan.FromMinutes(1)
    };

    [Fact]
    public void WithPaginator_子路径baseURL不二次叠加前缀()
    {
        var posts = new[] { MakePage("A", "/stack/posts/a/") };
        var listPage = MakePage("Posts", "/stack/posts/");

        var paged = listPage.WithPaginator(
            PaginatorView.Create(posts, 1, 10, "/stack/posts/", "page"),
            "https://example.com/stack/");

        Assert.Equal("/stack/posts/", paged.RelPermalink);
        Assert.Equal("https://example.com/stack/posts/", paged.Permalink);
        Assert.DoesNotContain("/stack/stack/", paged.Permalink);
    }

    [Fact]
    public void WithPaginator_根baseURL行为不变()
    {
        var listPage = MakePage("Posts", "/posts/"); // MakePage 已置 Permalink=https://example.com/posts/
        var paged = listPage.WithPaginator(
            PaginatorView.Create(Array.Empty<PageContext>(), 1, 10, "/posts/", "page"),
            "https://example.com");

        Assert.Equal("https://example.com/posts/", paged.Permalink);
    }
}
