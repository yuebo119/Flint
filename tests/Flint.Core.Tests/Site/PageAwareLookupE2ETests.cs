// Flint 静态站点生成器
// 页面感知查找 + 分类数据模型的端到端行为测试（A 组实施验收）。
//
// 覆盖四个实测确证的缺陷修复：
// 1. 根序越权 → 主题 section 布局永久失效（站点 _default 曾压过主题 section）；
// 2. 无页面上下文 → 跨 section/type 模板污染；
// 3. 一级形态名缺失（home.html）→ 首页渲染为空而构建报"成功"；
// 4. taxonomy .Data 数据模型缺失 → terms.html 报 null 对象。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>端到端：候选链驱动真实站点构建</summary>
public sealed class PageAwareLookupE2ETests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _outputDir;
    private readonly IAssetPipeline _assetPipeline = new StubAssetPipeline();

    public PageAwareLookupE2ETests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"flint-paw-{Guid.NewGuid():N}");
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

    /// <summary>构建：渲染器按 BuildHandler 同序挂载站点 layouts + 主题 layouts</summary>
    private async Task<BuildResult> BuildAsync(params string[] themeNames)
    {
        var themeLayoutDirs = themeNames
            .Select(t => Path.Combine(_siteDir, "themes", t, "layouts"))
            .ToArray();
        var builder = new SiteBuilder(
            new ContentParser(),
            new ScribanTemplateRenderer(
                Path.Combine(_siteDir, "layouts"), "https://example.com", themeLayoutDirs),
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

    private string Read(string relative) => File.ReadAllText(Path.Combine(_outputDir, relative));

    [Fact]
    public async Task 站点_default不压过主题section模板()
    {
        // 缺陷 1 端到端：SiteCreator 默认生成站点 _default/single.html，
        // 曾使任何 Flint 站点的主题 section 布局 100% 失效
        Write("Flint.toml", "baseURL = \"https://example.com/\"\ntitle = \"T\"\n");
        Write(Path.Combine("layouts", "_default", "single.html"), "SITE-DEFAULT");
        Write(Path.Combine("themes", "t1", "layouts", "posts", "single.html"), "THEME-POSTS");
        Write(Path.Combine("content", "posts", "a.md"), "---\ntitle: A\ndate: 2026-01-01\n---\nbody");

        var result = await BuildAsync("t1");

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Contains("THEME-POSTS", Read(Path.Combine("posts", "a", "index.html")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 跨section模板不污染()
    {
        // 缺陷 2 端到端：posts/single.html 曾对 docs 页生效
        Write("Flint.toml", "baseURL = \"https://example.com/\"\ntitle = \"T\"\n");
        Write(Path.Combine("layouts", "posts", "single.html"), "POSTS-TPL");
        Write(Path.Combine("layouts", "docs", "single.html"), "DOCS-TPL");
        Write(Path.Combine("layouts", "_default", "single.html"), "DEFAULT-TPL");
        Write(Path.Combine("content", "posts", "a.md"), "---\ntitle: A\ndate: 2026-01-01\n---\nb");
        Write(Path.Combine("content", "docs", "b.md"), "---\ntitle: B\ndate: 2026-01-02\n---\nb");

        var result = await BuildAsync();

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Contains("POSTS-TPL", Read(Path.Combine("posts", "a", "index.html")), StringComparison.Ordinal);
        Assert.Contains("DOCS-TPL", Read(Path.Combine("docs", "b", "index.html")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 一级home模板可渲染首页()
    {
        // 缺陷 3 端到端：仅用 home.html 时首页曾渲染为空且构建报"成功"
        Write("Flint.toml", "baseURL = \"https://example.com/\"\ntitle = \"T\"\n");
        Write(Path.Combine("layouts", "home.html"), "HOME-LEVEL-TEMPLATE {{ site.title }}");
        Write(Path.Combine("layouts", "_default", "single.html"), "S");
        Write(Path.Combine("content", "a.md"), "---\ntitle: A\ndate: 2026-01-01\n---\nb");

        var result = await BuildAsync();

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Contains("HOME-LEVEL-TEMPLATE", Read("index.html"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 声明type的页面命中type目录模板()
    {
        Write("Flint.toml", "baseURL = \"https://example.com/\"\ntitle = \"T\"\n");
        Write(Path.Combine("layouts", "page", "single.html"), "TYPE-PAGE-TPL");
        Write(Path.Combine("layouts", "_default", "single.html"), "DEFAULT-TPL");
        Write(Path.Combine("content", "misc", "a.md"),
            "---\ntitle: A\ndate: 2026-01-01\ntype: page\n---\nb");

        var result = await BuildAsync();

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Contains("TYPE-PAGE-TPL", Read(Path.Combine("misc", "a", "index.html")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task 分类页Data对象提供词条与内容页()
    {
        // 缺陷 4 端到端：terms.html 迭代 page.Data.pages 曾报 null 对象
        Write("Flint.toml", "baseURL = \"https://example.com/\"\ntitle = \"T\"\n");
        Write(Path.Combine("layouts", "_default", "single.html"), "S {{ page.title }}");
        Write(Path.Combine("layouts", "_default", "terms.html"),
            "TERMS s={{ page.data.singular }} p={{ page.data.plural }} " +
            "{{ for t in page.data.pages }}[{{ t.title }}@{{ t.rel_permalink }}]{{ end }}");
        Write(Path.Combine("layouts", "_default", "taxonomy.html"),
            "TERMPAGE {{ for x in pages }}{{ x.title }};{{ end }}");
        Write(Path.Combine("content", "posts", "one.md"),
            "---\ntitle: One\ndate: 2026-01-01\ntags: [\"alpha\"]\n---\nb");
        Write(Path.Combine("content", "posts", "two.md"),
            "---\ntitle: Two\ndate: 2026-01-02\ntags: [\"alpha\"]\n---\nb");

        var result = await BuildAsync();

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Message)));
        var terms = Read(Path.Combine("tags", "index.html"));
        Assert.Contains("s=tag", terms, StringComparison.Ordinal);
        Assert.Contains("p=tags", terms, StringComparison.Ordinal);
        Assert.Contains("alpha@/tags/alpha/", terms, StringComparison.Ordinal);

        var termPage = Read(Path.Combine("tags", "alpha", "index.html"));
        Assert.Contains("One", termPage, StringComparison.Ordinal);
        Assert.Contains("Two", termPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 输出格式变体模板按后缀命中()
    {
        Write("Flint.toml",
            "baseURL = \"https://example.com/\"\ntitle = \"T\"\n" +
            "[outputs]\nhome = [\"html\", \"rss\"]\n");
        Write(Path.Combine("layouts", "home.html"), "HOME-HTML");
        Write(Path.Combine("layouts", "home.rss.html"), "HOME-RSS");
        Write(Path.Combine("layouts", "single.html"), "S");
        Write(Path.Combine("content", "a.md"), "---\ntitle: A\ndate: 2026-01-01\n---\nb");

        var result = await BuildAsync();

        Assert.True(result.Success, string.Join("; ", result.Errors.Select(e => e.Message)));
        Assert.Contains("HOME-HTML", Read("index.html"), StringComparison.Ordinal);
    }

    private sealed class StubAssetPipeline : IAssetPipeline
    {
        public ValueTask<ProcessedAsset> ProcessAsync(AssetFile asset, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ProcessedAsset
            {
                OutputPath = asset.SourcePath,
                SourcePath = asset.SourcePath,
                Content = asset.Content,
                MediaType = asset.MediaType,
                ContentHash = "test-hash"
            });

        public async ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
            IReadOnlyList<AssetFile> assets, CancellationToken cancellationToken = default)
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
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var asset in assets.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return await ProcessAsync(asset, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
