// Flint 静态站点生成器
// 页面感知模板查找（A 组）行为测试：候选链生成 + 分层解析。
//
// 回归防护对象（2026-09-10 实测确证的三个缺陷）：
// 1. 根序越权——站点 layouts/_default/single.html 曾永久压过主题
//    layouts/posts/single.html，使主题 section 布局 100% 失效；
// 2. 无页面上下文——layouts/posts/single.html 曾对全站页面生效（跨 section 污染）；
// 3. 一级形态名缺失——home.html 曾被忽略（TemplateLookup.InferKind 不认），
//    仅用 home.html 的主题首页渲染为空而构建报"成功"。

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>候选链生成（纯函数，无 IO）</summary>
public sealed class PageTemplateCandidatesTests
{
    private static List<string> Names(IReadOnlyList<TemplateCandidate> levels) =>
        levels.Select(l => l.Name).ToList();

    [Fact]
    public void 普通页候选链_显式type与section同名时去重()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery
        {
            Kind = "page",
            DeclaredType = "posts",
            Section = "posts"
        });

        // type 与 section 相同时重复级被去重（page 等价名同样只出现一次）
        Assert.Equal(["posts/single", "posts/page", "single", "page", "all"], Names(levels));
    }

    [Fact]
    public void 普通页候选链_显式type优先于section()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery
        {
            Kind = "page",
            DeclaredType = "blog",   // 显式 type
            Section = "posts"        // 不同 section
        });

        var names = Names(levels);
        // type 目录级先于 section 目录级（对齐 Hugo：type 在 section 之前）
        Assert.True(names.IndexOf("blog/single") < names.IndexOf("posts/single"));
        // kind 等价名 `page`：Hugo v0.166 实测 layouts/page.html 可渲染普通页，
        // 故每级 single 之后补同名 page（fixit 只有根级 page.html，缺这级会整站找不到模板）
        Assert.Equal(
            ["blog/single", "blog/page", "posts/single", "posts/page", "single", "page", "all"],
            names);
    }

    [Fact]
    public void 普通页候选链_自定义layout置于默认名前()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery
        {
            Kind = "page",
            Layout = "wide",
            Section = "posts"
        });

        // layout 为"优先提示"：{section}/{layout} 先于 {section}/single
        Assert.Equal(
            ["posts/wide", "posts/single", "posts/page", "wide", "single", "page", "all"],
            Names(levels));
    }

    [Fact]
    public void 首页候选链_含home与list且index在前()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery { Kind = "home" });

        // home.html 是一级标准名（此前缺失导致首页渲染为空）
        Assert.Equal(["index", "home", "list", "all", "single"], Names(levels));
    }

    [Fact]
    public void section候选链_字面section目录在_default之后()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery
        {
            Kind = "section",
            Section = "posts"
        });

        var names = Names(levels);
        Assert.Equal(
            ["posts/section", "posts/list", "section/section", "section/list", "list", "all", "single"],
            names);
        // 页面自身 section 目录优先于字面 layouts/section/ 目录
        Assert.True(names.IndexOf("posts/list") < names.IndexOf("section/list"));
    }

    [Fact]
    public void taxonomy术语页候选链_不含single兜底()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery
        {
            Kind = "term",
            Taxonomy = "categories"
        });

        var names = Names(levels);
        // taxonomy/term 必须留给内置模板兜底，不得落到 single（会截胡内置列表模板）
        Assert.DoesNotContain("single", names);
        Assert.Equal("categories/term", names[0]);
        Assert.Contains("term", names);
        Assert.Contains("taxonomy", names);
        Assert.Contains("list", names);
    }

    [Fact]
    public void taxonomy列表页候选链_含terms与taxonomy()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery
        {
            Kind = "taxonomy",
            Taxonomy = "tags"
        });

        var names = Names(levels);
        // Hugo v0.166：kind=taxonomy（/tags/）优先 taxonomy.html，terms.html 是旧名（次之）
        Assert.Equal("tags/taxonomy", names[0]);
        Assert.Contains("terms", names);
        Assert.Contains("taxonomy", names);
        Assert.DoesNotContain("single", names);
    }

    [Fact]
    public void 非html格式_全部候选名带格式后缀()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery
        {
            Kind = "section",
            Section = "posts",
            OutputFormat = "rss"
        });

        Assert.All(Names(levels), name => Assert.EndsWith(".rss", name, StringComparison.Ordinal));
        Assert.Contains("posts/list.rss", Names(levels));
    }

    [Fact]
    public void 未知kind按普通页处理_不抛异常()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery { Kind = "mystery" });

        Assert.Contains("single", Names(levels));
        Assert.Contains("all", Names(levels));
    }

    [Fact]
    public void 空section与空type_不产出噪音候选()
    {
        var levels = PageTemplateCandidates.Build(new PageTemplateQuery { Kind = "page" });

        // 根级内容页（Section 为空）只产出单页链（含 kind 等价名 page）
        Assert.Equal(["single", "page", "all"], Names(levels));
    }
}

/// <summary>分层解析：候选级优先于根序（修复根序越权缺陷）</summary>
public sealed class TemplateLookupLayeredTests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _themeDir;

    public TemplateLookupLayeredTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"flint-layered-{Guid.NewGuid():N}");
        _themeDir = Path.Combine(_siteDir, "themes", "t1", "layouts");
        Directory.CreateDirectory(Path.Combine(_siteDir, "layouts"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_siteDir)) Directory.Delete(_siteDir, recursive: true); }
        catch (IOException) { }
    }

    private string SiteLayouts() => Path.Combine(_siteDir, "layouts");

    private void WriteSite(string relative, string content)
    {
        var path = Path.Combine(SiteLayouts(), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteTheme(string relative, string content)
    {
        var path = Path.Combine(_themeDir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private TemplateLookup CreateLookup() => new(SiteLayouts(), _themeDir);

    private static List<TemplateCandidate> Levels(params string[] names) =>
        names.Select(n => new TemplateCandidate(n)).ToList();

    [Fact]
    public void 候选路径特异性优先于根序_主题section胜过站点_default()
    {
        // 缺陷 1 回归防护：站点 _default/single.html 曾永久压过主题 posts/single
        WriteSite(Path.Combine("_default", "single.html"), "SITE-DEFAULT");
        WriteTheme(Path.Combine("posts", "single.html"), "THEME-POSTS");

        var resolved = CreateLookup().ResolveLayered(Levels("posts/single", "single"));

        Assert.NotNull(resolved);
        Assert.StartsWith(_themeDir, resolved, StringComparison.Ordinal);
        Assert.Contains("posts", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 跨section隔离_其他section模板不命中()
    {
        // 缺陷 2 回归防护：docs 页面不得命中 posts/single
        WriteSite(Path.Combine("posts", "single.html"), "POSTS-SINGLE");
        WriteSite(Path.Combine("single.html"), "ROOT-SINGLE");

        // docs 页面的候选链不含 posts 级
        var resolved = CreateLookup().ResolveLayered(Levels("docs/single", "single"));

        Assert.NotNull(resolved);
        Assert.EndsWith("single.html", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("posts", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 一级home模板可命中()
    {
        // 缺陷 3 回归防护：home.html 曾被 InferKind 忽略
        WriteSite("home.html", "HOME-TEMPLATE");

        var resolved = CreateLookup().ResolveLayered(Levels("index", "home", "list"));

        Assert.NotNull(resolved);
        Assert.EndsWith("home.html", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void type目录模板可命中()
    {
        WriteSite(Path.Combine("page", "single.html"), "TYPE-PAGE-SINGLE");

        var resolved = CreateLookup().ResolveLayered(Levels("page/single", "single"));

        Assert.NotNull(resolved);
        Assert.Contains(Path.DirectorySeparatorChar + "page" + Path.DirectorySeparatorChar,
            resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 同根内根形态优先于_default兜底形态()
    {
        WriteSite("single.html", "ROOT");
        WriteSite(Path.Combine("_default", "single.html"), "DEFAULT");

        var resolved = CreateLookup().ResolveLayered(Levels("single"));

        Assert.EndsWith("single.html", resolved, StringComparison.Ordinal);
        Assert.DoesNotContain("_default", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 站点优先于主题_同候选级()
    {
        WriteSite("single.html", "SITE");
        WriteTheme("single.html", "THEME");

        var resolved = CreateLookup().ResolveLayered(Levels("single"));

        Assert.StartsWith(SiteLayouts(), resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 全部未命中返回空()
    {
        WriteSite("single.html", "ROOT");

        Assert.Null(CreateLookup().ResolveLayered(Levels("nosuch", "alsomissing")));
    }

    [Fact]
    public void 主题_default形态作为同根最末兜底()
    {
        WriteTheme(Path.Combine("_default", "single.html"), "THEME-DEFAULT");

        var resolved = CreateLookup().ResolveLayered(Levels("single"));

        Assert.NotNull(resolved);
        Assert.Contains("_default", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void 描述符缓存失效后能看到新增模板()
    {
        var lookup = CreateLookup();
        Assert.Null(lookup.ResolveLayered(Levels("single")));

        WriteSite("single.html", "ADDED");
        // 未失效前索引快照不含新文件
        Assert.Null(lookup.ResolveLayered(Levels("single")));

        lookup.Invalidate();
        Assert.NotNull(lookup.ResolveLayered(Levels("single")));
    }
}
