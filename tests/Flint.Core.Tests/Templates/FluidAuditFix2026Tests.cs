// Flint 静态站点生成器
// 2026-10-22 「全面适配 Fluid」审计修复的锁定测试（FLUID-MIGRATION-PLAN 后时代）。
// 覆盖五项修复，每项对应审计发现的一个真实缺陷：
//   1. where 过滤器委托失效（两参退化自研相等、一参静默不过滤）
//   2. _dependencyCache 无失效路径（长驻 serve 进程闭包过期）
//   3. 裸 FluidTemplateRenderer 接口不对称（短代码/hooks 静默丢失）
//   4. 静态依赖闭包 .html 后缀硬编码（Fluid 站点幻影路径）
//   5. watch  themes/*/layouts 变更落入资产（模板副本进 public/）

using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>2026-10-22 审计五项修复的回归锁定</summary>
public sealed class FluidAuditFix2026Tests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _layoutsDir;

    public FluidAuditFix2026Tests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"Flint-auditfix-{Guid.NewGuid():N}");
        _layoutsDir = Path.Combine(_siteDir, "layouts");
        Directory.CreateDirectory(_layoutsDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_siteDir))
            {
                Directory.Delete(_siteDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    // ---- 1. where 过滤器三形态 ----

    [Fact]
    public async Task where_两参相等形态_委托内置不过滤退化()
    {
        // 修复前：__builtin_where 键不存在 → 两参走自研字符串相等。
        // 自研路径对数值成员也退化为字符串比较，"10" vs "9" 类比较失真；
        // 委托 Fluid 内置后两参按成员值语义过滤
        var renderer = CreateRenderer();
        var template = """
            {% assign items = pages | where: 'kind', 'section' %}{{ items.size }}
            """;
        var result = await RenderWithPagesAsync(renderer, template);

        // 两篇文章 + 一个 section 页 = kind 为 section 的 1 个
        Assert.Equal("1", result.Trim());
    }

    [Fact]
    public async Task where_一参真值形态_按真值过滤()
    {
        // 修复前：一参形态静默 return input（全量不过滤）
        var renderer = CreateRenderer();
        var template = """
            {% assign items = pages | where: 'draft' %}{{ items.size }}
            """;
        var result = await RenderWithPagesAsync(renderer, template);

        // draft 为 false 的页被过滤掉，只剩 draft=true 的 1 篇
        Assert.Equal("1", result.Trim());
    }

    [Fact]
    public async Task where_三参运算符形态_Hugo语义不变()
    {
        // 回归保护：三参运算符形态（2026-10-21 的包装）行为不能被委托改动破坏
        var renderer = CreateRenderer();
        var template = """
            {% assign items = pages | where: 'word_count', '>', '5' %}{{ items.size }}
            """;
        var result = await RenderWithPagesAsync(renderer, template);

        // word_count 10 与 3：> 5 命中 1 篇
        Assert.Equal("1", result.Trim());
    }

    // ---- 2. _dependencyCache 构建边界失效 ----

    [Fact]
    public void 依赖闭包缓存_构建边界失效_反映新增include()
    {
        var renderer = CreateRenderer();
        Directory.CreateDirectory(Path.Combine(_layoutsDir, "partials"));
        File.WriteAllText(Path.Combine(_layoutsDir, "partials", "a.liquid"), "A");
        File.WriteAllText(Path.Combine(_layoutsDir, "index.liquid"),
            "<h1>{% include 'partials/a' %}</h1>");

        // 首次取闭包：只含 partials/a
        var first = renderer.GetDependencies("index");
        Assert.Contains("partials/a", first);

        // 主模板新增一个 include（模拟编辑器改动）
        File.WriteAllText(Path.Combine(_layoutsDir, "index.liquid"),
            "<h1>{% include 'partials/a' %}{% include 'partials/b' %}</h1>");
        File.WriteAllText(Path.Combine(_layoutsDir, "partials", "b.liquid"), "B");

        // 修复前：_dependencyCache 永不清空 → 仍返回旧闭包（漏 partials/b）。
        // 构建边界 InvalidateMtimeCache 后必须反映新闭包
        renderer.InvalidateMtimeCache();
        var second = renderer.GetDependencies("index");
        Assert.Contains("partials/b", second);
    }

    // ---- 3. 裸 Fluid 渲染器的内容管线接口 ----

    [Fact]
    public void 裸Fluid渲染器_实现内容管线两接口()
    {
        // 修复前：FluidTemplateRenderer 只实现 ITemplateRenderer，
        // SiteBuilder 的 `is not IShortcodeRenderer` 早退让裸装配静默丢失
        ITemplateRenderer renderer = CreateRenderer();
        Assert.IsAssignableFrom<IShortcodeRenderer>(renderer);
        Assert.IsAssignableFrom<IRenderHookRenderer>(renderer);
    }

    [Fact]
    public void 裸Fluid渲染器_注册liquid短代码()
    {
        var renderer = CreateRenderer();
        var shortcodesDir = Path.Combine(_layoutsDir, "shortcodes");
        Directory.CreateDirectory(shortcodesDir);
        File.WriteAllText(Path.Combine(shortcodesDir, "note.liquid"),
            "<aside>{{ text }}</aside>");

        var registry = new ShortcodeRegistry(registerBuiltins: false);
        ((IShortcodeRenderer)renderer).RegisterShortcodesFrom(
            registry, shortcodesDir, "站点", isSite: true);

        Assert.True(registry.Contains("note"), ".liquid 短代码应被注册");
        Assert.NotNull(registry.Get("note"));
    }

    [Fact]
    public void 裸Fluid渲染器_不注册html短代码()
    {
        // 与裸 Scriban 渲染器只认 .html 对称：裸 Fluid 只认 .liquid
        var renderer = CreateRenderer();
        var shortcodesDir = Path.Combine(_layoutsDir, "shortcodes");
        Directory.CreateDirectory(shortcodesDir);
        File.WriteAllText(Path.Combine(shortcodesDir, "note.html"),
            "<aside>{{ text }}</aside>");

        var registry = new ShortcodeRegistry(registerBuiltins: false);
        ((IShortcodeRenderer)renderer).RegisterShortcodesFrom(
            registry, shortcodesDir, "站点", isSite: true);

        Assert.False(registry.Contains("note"), ".html 短代码不在 Fluid 渲染器职责内");
    }

    [Fact]
    public void 裸Fluid渲染器_装配liquid渲染钩子()
    {
        var renderer = CreateRenderer();
        var markupDir = Path.Combine(_layoutsDir, "_markup");
        Directory.CreateDirectory(markupDir);
        File.WriteAllText(Path.Combine(markupDir, "render-heading.liquid"),
            "<h{{ level }} id=\"{{ id }}\">{{ text }}</h{{ level }}>");

        var hooks = ((IRenderHookRenderer)renderer).LoadRenderHooks(_layoutsDir);
        Assert.NotNull(hooks);
        Assert.NotNull(hooks!.Heading);
    }

    // ---- 4. 静态依赖闭包路径解析 ----

    [Fact]
    public void 闭包路径解析_无扩展名_liquid优先()
    {
        Directory.CreateDirectory(Path.Combine(_layoutsDir, "partials"));
        File.WriteAllText(Path.Combine(_layoutsDir, "partials", "badge.liquid"), "B");

        var resolved = SiteBuilder.ResolveClosurePath(_layoutsDir, "partials/badge");

        // 修复前：无条件补 .html → 幻影路径 layouts/partials/badge.html
        Assert.EndsWith("badge.liquid", resolved.Replace('\\', '/'));
        Assert.True(File.Exists(resolved), "解析结果必须是盘上真实文件");
    }

    [Fact]
    public void 闭包路径解析_无扩展名_仅html时回落html()
    {
        // Scriban 站点（Hugo 主题）行为不能变：include 名无扩展名 + 只有 .html
        Directory.CreateDirectory(Path.Combine(_layoutsDir, "partials"));
        File.WriteAllText(Path.Combine(_layoutsDir, "partials", "head.html"), "H");

        var resolved = SiteBuilder.ResolveClosurePath(_layoutsDir, "partials/head");

        Assert.EndsWith("head.html", resolved.Replace('\\', '/'));
    }

    [Fact]
    public void 闭包路径解析_带扩展名原样保留()
    {
        var withHtml = SiteBuilder.ResolveClosurePath(_layoutsDir, "partials/x.html");
        var withLiquid = SiteBuilder.ResolveClosurePath(_layoutsDir, "partials/x.liquid");

        Assert.EndsWith("x.html", withHtml.Replace('\\', '/'));
        Assert.EndsWith("x.liquid", withLiquid.Replace('\\', '/'));
    }

    // ---- 5. watch 主题 layouts 变更分类 ----

    [Fact]
    public async Task 主题layouts变更_不走资产路径_不拷进public()
    {
        // 修复前：themes/<t>/layouts/** 落入 assetChanges 被资产管线
        // 写进 public/（watch 产物比全量多一份模板副本）
        File.WriteAllText(Path.Combine(_siteDir, "Flint.toml"), """
            baseURL = "https://example.com/"
            title = "主题测试站"
            theme = "demo"
            """);
        Directory.CreateDirectory(Path.Combine(_siteDir, "content", "posts"));
        File.WriteAllText(Path.Combine(_siteDir, "content", "posts", "p1.md"), """
            +++
            title = "文章一"
            date = 2026-01-01T00:00:00Z
            draft = false
            +++

            正文一。
            """);
        var themeLayouts = Path.Combine(_siteDir, "themes", "demo", "layouts");
        Directory.CreateDirectory(themeLayouts);
        File.WriteAllText(Path.Combine(themeLayouts, "index.liquid"),
            "<h1>theme v1</h1>");
        Directory.CreateDirectory(Path.Combine(_layoutsDir, "_default"));
        File.WriteAllText(Path.Combine(_layoutsDir, "_default", "single.liquid"),
            "<article>{{ page.title }}</article>");

        var builder = CreateBuilder();
        var options = CreateOptions();
        var full = await builder.BuildAsync(options);
        Assert.True(full.Success, $"全量构建应成功: {string.Join(" | ", full.Errors.Select(e => e.Message))}");

        // 主题布局文件变更
        File.WriteAllText(Path.Combine(themeLayouts, "index.liquid"),
            "<h1>theme v2</h1>");

        var incremental = await builder.IncrementalBuildAsync(
            options, [Path.Combine(themeLayouts, "index.liquid")]);
        Assert.True(incremental.Success,
            $"增量构建应成功: {string.Join(" | ", incremental.Errors.Select(e => e.Message))}");

        // 模板文件不得被当资产拷进 public/
        var leaked = Path.Combine(_siteDir, "public", "themes", "demo", "layouts", "index.liquid");
        Assert.False(File.Exists(leaked), "主题布局不应作为资产产物出现在 public/");
    }

    // ---- 辅助 ----

    private FluidTemplateRenderer CreateRenderer() =>
        new(_layoutsDir, "https://example.com");

    private SiteBuilder CreateBuilder() =>
        new(
            new ContentParser(),
            new FluidTemplateRenderer(_layoutsDir, "https://example.com"),
            new StubAssetPipeline(),
            new ConfigLoader());

    private BuildOptions CreateOptions() => new()
    {
        SourcePath = _siteDir,
        OutputPath = Path.Combine(_siteDir, "public"),
        Parallelism = 1
    };

    /// <summary>构造含 pages 集合（两篇 regular + 一个 section 页）的上下文并渲染</summary>
    private async Task<string> RenderWithPagesAsync(FluidTemplateRenderer renderer, string template)
    {
        var pages = new List<PageContext>
        {
            NewPage("posts/a", "section", wordCount: 10, draft: false),
            NewPage("posts/b", "page", wordCount: 3, draft: false),
            NewPage("posts/c", "page", wordCount: 3, draft: true),
        };

        var file = Path.Combine(_layoutsDir, "probe.liquid");
        await File.WriteAllTextAsync(file, template).ConfigureAwait(false);
        var context = new TemplateContext
        {
            Page = pages[1],
            Site = new SiteContext
            {
                Title = "S", BaseURL = "https://example.com", Language = "zh-cn",
                Pages = pages, RegularPages = pages,
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Translations = new Dictionary<string, string>(),
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            },
            Pages = pages
        };
        return await renderer.RenderTemplateFileAsync(file, context).ConfigureAwait(false);
    }

    private static PageContext NewPage(
        string relPermalink, string kind, int wordCount, bool draft) => new()
        {
            Title = kind + relPermalink,
            Content = "",
            Permalink = "https://example.com" + relPermalink,
            RelPermalink = relPermalink,
            Date = DateTimeOffset.UnixEpoch,
            Tags = [], Categories = [],
            WordCount = wordCount,
            ReadingTime = TimeSpan.Zero,
            Kind = kind,
            Draft = draft,
            Section = kind == "section" ? "posts" : ""
        };

    private sealed class StubAssetPipeline : IAssetPipeline
    {
        public ValueTask<ProcessedAsset> ProcessAsync(AssetFile asset, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ProcessedAsset
            {
                OutputPath = asset.SourcePath,
                SourcePath = asset.SourcePath,
                Content = asset.Content,
                MediaType = "application/octet-stream",
                ContentHash = ""
            });

        public ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
            IReadOnlyList<AssetFile> assets, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

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
