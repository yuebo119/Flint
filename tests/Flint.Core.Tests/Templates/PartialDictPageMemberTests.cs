// Flint 静态站点生成器
// 锁定单测：partial dict 上下文的页面成员访问链（T6.2 C 组断裂根因调查的复现器）。
// 场景 = fixit get-cover 的最小形态：partialValue 传 dict "Page" page，
// partial 内经 `page.page ?? page` 取页面对象后访问 .resources / .params / .data
//
// 历史：C 组（resources/params/data/...）TryGetValue 惰性化时此链断裂
// （矩阵 16/21：fixit/loveit/ananke/blowfish/papermod 的 $page.resources.getmatch
// 在 dict 上下文取空），2026-10-12 回退 C 组急切后本测试作为回归护栏

using AwesomeAssertions;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class PartialDictPageMemberTests : IDisposable
{
    private readonly string _siteRoot;

    public PartialDictPageMemberTests()
    {
        _siteRoot = Path.Combine(Path.GetTempPath(), $"flint-pdict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteRoot)) Directory.Delete(_siteRoot, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task PartialDictContext_PageMembers_ResolveThroughPageKey()
    {
        Write("Flint.toml", "baseURL = \"http://localhost:1313/\"\ntitle = \"t\"\n");
        // 两层嵌套 = fixit 真实链路：list -> summary(dict) -> get-cover(dict)。
        // 调用者（summary）的 page 全局已是 overlay dict，get-cover 收到的
        // dict "Page" page 的 Page 值是 **dict 而非 LazyPageObject**
        Write(Path.Combine("layouts", "single.html"),
            "{{ $s = partialValue \"_partials/summary\" (dict \"Page\" page) }}[{{ $s }}]");
        Write(Path.Combine("layouts", "_partials", "summary.html"),
            "{{- $cover = partialValue \"_partials/get-cover\" (dict \"Page\" page) -}}{{ $cover }}");
        // fixit get-cover 的最小形态：page.page ?? page → .resources/.params/.data 三连访问
        Write(Path.Combine("layouts", "_partials", "get-cover.html"),
            "{{- $p = (page.page ?? page) -}}" +
            "{{- $m = \"none\" -}}" +
            "{{- if $p.params -}}{{- $m = $m + \"+params\" -}}{{- end -}}" +
            "{{- if $p.resources.getmatch \"x\" -}}{{- $m = $m + \"+gm\" -}}{{- else -}}{{- $m = $m + \"+gmempty\" -}}{{- end -}}" +
            "{{- $p.store.set \"k\" $m -}}{{ $m }}");
        Write(Path.Combine("content", "posts", "a.md"),
            "---\ntitle: A\ndate: 2026-01-01\n---\nbody");

        var html = await BuildSinglePageAsync();
        // resources 恒为集合（空集合也是真值链上的可判定对象）；params/data 必有
        // 空集合/空 map 的 if 判定为假是正确语义（ScriptArray/ScriptObject 空=假）：
        // 普通页 resources 空集合、.Data 空 map（Hugo 语义）。可达性判据 =
        // params 非空（map）+ resources.getmatch 可调用（+gmempty，无匹配但不报错）
        html.Should().Contain("none+params+gmempty",
            "partial dict 上下文内经 page.page ?? page 取到的页面对象，params 可达且 resources.getmatch 可调用");
    }

    [Fact]
    public async Task PartialDictContext_ResourcesGetMatch_Callable()
    {
        Write("Flint.toml", "baseURL = \"http://localhost:1313/\"\ntitle = \"t\"\n");
        Write(Path.Combine("layouts", "single.html"),
            "{{ $r = partialValue \"_partials/probe\" (dict \"Page\" page) }}[{{ $r }}]");
        // fixit 的确切形态：$page.resources.getmatch（方法调用，非仅成员访问）
        Write(Path.Combine("layouts", "_partials", "probe.html"),
            "{{- $p = (page.page ?? page) -}}" +
            "{{- $hit = $p.resources.getmatch \"featured-image\" -}}" +
            "{{- if $hit -}}HIT{{- else -}}MISS{{- end -}}");
        Write(Path.Combine("content", "posts", "a.md"),
            "---\ntitle: A\ndate: 2026-01-01\n---\nbody");

        var html = await BuildSinglePageAsync();
        // 无资源文件时 getmatch 返回空（MISS），但**不能**报 function not found
        html.Should().Contain("MISS");
    }

    /// <summary>
    /// 404 页场景：辅助输出入口（RenderAsync 按模板名渲染，Page = 手建 PageContext）
    /// 与常规页（RenderPageAsync 候选链）走同一 CreateScribanContext，dict 链一致
    /// </summary>
    [Fact]
    public async Task PartialDictContext_404Page_MembersResolve()
    {
        Write("Flint.toml", "baseURL = \"http://localhost:1313/\"\ntitle = \"t\"\n");
        Write(Path.Combine("layouts", "single.html"), "<p>{{ page.title }}</p>");
        Write(Path.Combine("layouts", "404.html"),
            "{{ $r = partialValue \"_partials/probe\" (dict \"Page\" page) }}[{{ $r }}]");
        Write(Path.Combine("layouts", "_partials", "probe.html"),
            "{{- $p = (page.page ?? page) -}}" +
            "{{- $hit = $p.resources.getmatch \"featured-image\" -}}" +
            "{{- if $hit -}}HIT{{- else -}}MISS{{- end -}}");
        Write(Path.Combine("content", "posts", "a.md"),
            "---\ntitle: A\ndate: 2026-01-01\n---\nbody");

        var builder = CreateBuilder();
        var options = new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public")
        };
        var result = await builder.BuildAsync(options);
        result.Success.Should().BeTrue("构建应成功: {0}",
            string.Join("; ", result.Errors.Select(e => $"{e.ErrorCode} {e.FilePath}: {e.Message}")));

        var notFound = File.ReadAllText(Path.Combine(_siteRoot, "public", "404.html"));
        notFound.Should().Contain("MISS");
    }

    private async Task<string> BuildSinglePageAsync()
    {
        var builder = CreateBuilder();
        var options = new Flint.Core.Models.BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public")
        };
        var result = await builder.BuildAsync(options).ConfigureAwait(false);
        result.Success.Should().BeTrue("构建应成功: {0}",
            string.Join("; ", result.Errors.Select(e => $"{e.ErrorCode} {e.FilePath}: {e.Message}")));
        return File.ReadAllText(Path.Combine(_siteRoot, "public", "posts", "a", "index.html"));
    }

    private SiteBuilder CreateBuilder()
    {
        var configLoader = new ConfigLoader();
        var contentParser = new ContentParser();
        var templateRenderer = new ScribanTemplateRenderer(Path.Combine(_siteRoot, "layouts"));
        var assetPipeline = new Flint.Core.Assets.AssetPipeline();
        return new SiteBuilder(contentParser, templateRenderer, assetPipeline, configLoader);
    }

    private void Write(string relativePath, string content)
    {
        var full = Path.Combine(_siteRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
