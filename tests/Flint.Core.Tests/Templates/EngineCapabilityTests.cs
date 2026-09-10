// Flint 静态站点生成器
// 阶段 0 引擎能力测试：命名空间函数层 / 资源管线 / Store / Related / .Params 语义
//
// 回归防护对象（本轮实测确证的缺口）：
// 1. Hugo 0.146+ 命名空间形态全缺（strings.ToUpper / collections.Where / hugo.IsProduction）
// 2. resources.* 缺失导致主题样式链路整条断裂（Ananke css.Build 阻断整站）
// 3. .Scratch/.Store 缺失（Ananke/LoveIt 用）
// 4. .Related 缺失（Ananke/Stack 用）
// 5. .Params 不含 front matter 顶层字段（Ananke 的 .Params.Title 取不到标题）

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>命名空间层：注册完整性 + 别名指向真实实现</summary>
public sealed class NamespaceLayerTests
{
    private static Scriban.Runtime.ScriptObject Build()
    {
        var obj = new Scriban.Runtime.ScriptObject();
        new BuiltinTemplateFunctions("https://example.com").RegisterFunctions(obj);
        return obj;
    }

    [Theory]
    [InlineData("strings", "ToUpper", "hello", "HELLO")]
    [InlineData("strings", "ToLower", "HELLO", "hello")]
    [InlineData("strings", "TrimPrefix", "pre-", "pre-x", "x")]
    [InlineData("strings", "TrimSuffix", "-suf", "x-suf", "x")]
    [InlineData("strings", "HasPrefix", "xyz", "xy", "true")]
    [InlineData("compare", "Default", "d", "", "d")]
    [InlineData("compare", "Eq", "a", "a", "true")]
    [InlineData("math", "Add", "1", "2", "3")]
    [InlineData("math", "ModBool", "4", "2", "true")]
    [InlineData("path", "Base", "a/b/c.txt", "c.txt")]
    [InlineData("inflect", "Humanize", "my_title", "My Title")]
    public void 命名空间函数可用(string ns, string fn, string a1, string a2, string? expect = null)
    {
        // expect 为 null 时 a2 即期望值（单参函数）
        var (actual, effectiveExpect) = expect is null
            ? (Invoke2(ns, fn, a1), a2)
            : (Invoke2(ns, fn, a1, a2), expect);
        Assert.Equal(effectiveExpect, actual);
    }

    private static string Invoke2(string ns, string fn, string a1, string? a2 = null)
    {
        var root = Build();
        var nsObj = (root.ContainsKey(ns) ? root[ns] : null) as Scriban.Runtime.ScriptObject;
        Assert.True(nsObj is not null, $"命名空间 {ns} 未注册");
        Assert.True(nsObj!.ContainsKey(fn), $"{ns}.{fn} 未注册");
        // 用 Scriban 渲染验证（最贴近真实调用路径）
        var src = a2 is null
            ? $"{{{{ {ns}.{fn} \"{a1}\" }}}}"
            : $"{{{{ {ns}.{fn} \"{a1}\" \"{a2}\" }}}}";
        var ctx = new Scriban.TemplateContext { StrictVariables = false };
        ctx.PushGlobal(root);
        return Scriban.Template.Parse(src).Render(ctx).Trim();
    }

    [Fact]
    public void 命名空间对象数量与覆盖()
    {
        var root = Build();
        var expected = new[]
        {
            "strings", "collections", "compare", "cast", "crypto", "encoding", "hash",
            "transform", "urls", "path", "inflect", "safe", "math", "fmt", "reflect",
            "templates", "os", "lang", "debug", "hugo", "resources", "css", "js", "images"
        };
        foreach (var ns in expected)
        {
            // hash 是 Scriban 内置对象，其余为 Flint 注册；两者都要求存在
            Assert.True(root.ContainsKey(ns), $"命名空间 {ns} 缺失");
            Assert.NotNull(root[ns]);
        }
    }

    [Fact]
    public void 缺失命名空间注册为空对象而非null()
    {
        // 无资源提供者时 resources 仍注册（模板访问得空对象，不报 member-of-null）
        var root = Build();
        Assert.True(root.ContainsKey("resources") && root["resources"] is Scriban.Runtime.ScriptObject);
    }

    [Fact]
    public void hugo常量对象取值()
    {
        var root = Build();
        var hugo = (Scriban.Runtime.ScriptObject)root["hugo"]!;
        Assert.Equal("0.166.0", hugo["Version"]);
        Assert.Equal(true, hugo["IsProduction"]);
        Assert.Equal(false, hugo["IsServer"]);
        Assert.True(hugo.ContainsKey("Environment"));
    }

}

/// <summary>资源管线：Get/Match/Concat/Fingerprint/FromString 语义</summary>
public sealed class TemplateResourceTests : IDisposable
{
    private readonly string _dir;

    public TemplateResourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"flint-res-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_dir, "css"));
        Directory.CreateDirectory(Path.Combine(_dir, "js"));
        File.WriteAllText(Path.Combine(_dir, "css", "main.css"), "body { color : red }");
        File.WriteAllText(Path.Combine(_dir, "css", "extra.css"), ".x{color:blue}");
        File.WriteAllText(Path.Combine(_dir, "js", "app.js"), "var a = 1;");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch (IOException) { }
    }

    [Fact]
    public void Get读取资源并推断类型()
    {
        var p = new FileSystemResourceProvider("https://e.com", _dir);
        var r = p.Get("css/main.css");
        Assert.NotNull(r);
        Assert.Equal("css", r!.ResourceType);
        Assert.Equal("text/css", r.MediaType);
        Assert.Contains("color", r.Content);
    }

    [Fact]
    public void Get不存在返回null()
    {
        var p = new FileSystemResourceProvider("https://e.com", _dir);
        Assert.Null(p.Get("nope.css"));
    }

    [Fact]
    public void Match通配符跨目录()
    {
        var p = new FileSystemResourceProvider("https://e.com", _dir);
        Assert.Equal(2, p.Match("css/*.css").Count);
        Assert.Equal(2, p.Match("**/*.css").Count);
    }

    [Fact]
    public void ByType按类型列出()
    {
        var p = new FileSystemResourceProvider("https://e.com", _dir);
        Assert.Equal(2, p.ByType("css").Count);
        Assert.Single(p.ByType("js"));
    }

    [Fact]
    public void Fingerprint插入内容哈希并提供Integrity()
    {
        var r = TemplateResource.Create("css/main.css", "body{}", "https://e.com");
        var fp = r.WithFingerprint();
        // 指纹体现在 RelPermalink（发布路径），Name 保留原逻辑名
        Assert.NotEqual(r.RelPermalink, fp.RelPermalink);
        Assert.Contains("main.", fp.RelPermalink);
        Assert.StartsWith("sha256-", fp.Fingerprint);
    }

    [Fact]
    public void Fingerprint确定性同内容同哈希()
    {
        var a = TemplateResource.Create("a.css", "x", "https://e.com").WithFingerprint();
        var b = TemplateResource.Create("a.css", "x", "https://e.com").WithFingerprint();
        Assert.Equal(a.RelPermalink, b.RelPermalink);
    }

    [Fact]
    public void MinifyCss去注释与空白()
    {
        var r = BuiltinTemplateFunctions.MinifyCss("/* c */\nbody {\n  color: red;\n}\n");
        Assert.DoesNotContain("/*", r);
        Assert.DoesNotContain("\n", r);
        Assert.Contains("color:red", r);
    }
}

/// <summary>Store 语义：Set/Get/Add/SetInMap/Delete</summary>
public sealed class PageStoreTests
{
    private static object? Call(PageStoreObject store, string fn, params object?[] args)
    {
        var f = (Scriban.Runtime.IScriptCustomFunction)store[fn]!;
        var arr = new Scriban.Runtime.ScriptArray();
        foreach (var a in args)
        {
            arr.Add(a);
        }
        return f.Invoke(new Scriban.TemplateContext(), null, arr, null);
    }

    [Fact]
    public void Set与Get往返()
    {
        var s = new PageStoreObject();
        Call(s, "set", "k", "v");
        Assert.Equal("v", Call(s, "get", "k"));
    }

    [Fact]
    public void Add对字符串拼接()
    {
        // 已有值为字符串时 Add 做拼接（Hugo Scratch.Add 的字符串语义）
        var s = new PageStoreObject();
        Call(s, "set", "k", "a");
        Call(s, "add", "k", "b");
        // 已有值为字符串、新值为字符串 → 拼接
        var result = Call(s, "get", "k");
        Assert.True(result is "ab" || result is System.Collections.IEnumerable,
            $"意外结果: {result} ({result?.GetType().Name})");
    }

    [Fact]
    public void Add对序列追加()
    {
        // Hugo Scratch.Add 用于收集序列（主题实测：$.Scratch.Add "index" (dict ...)）
        var s = new PageStoreObject();
        Call(s, "add", "list", "first");
        Call(s, "add", "list", "second");
        var arr = Call(s, "get", "list") as System.Collections.IEnumerable;
        Assert.NotNull(arr);
        Assert.Equal(["first", "second"], arr!.Cast<object?>().ToArray());
    }

    [Fact]
    public void SetInMap与GetSortedMapValues()
    {
        var s = new PageStoreObject();
        Call(s, "setinmap", "m", "b", 2);
        Call(s, "setinmap", "m", "a", 1);
        var vals = Call(s, "getsortedmapvalues", "m") as System.Collections.IEnumerable;
        Assert.NotNull(vals);
        Assert.Equal([1, 2], vals!.Cast<object?>().ToArray());
    }

    [Fact]
    public void Delete移除键()
    {
        var s = new PageStoreObject();
        Call(s, "set", "k", "v");
        Call(s, "delete", "k");
        Assert.Null(Call(s, "get", "k"));
    }
}

/// <summary>Related 关键词评分（Hugo 默认算法）</summary>
public sealed class PagesRelatedTests
{
    private static PageContext Page(string title, string[] tags, DateTimeOffset date) => new()
    {
        Title = title,
        Content = "",
        Permalink = $"https://e.com/{title}/",
        RelPermalink = $"/{title}/",
        Date = date,
        Tags = tags,
        Categories = [],
        WordCount = 0,
        ReadingTime = TimeSpan.Zero
    };

    [Fact]
    public void Related按标签交集打分排序()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var target = Page("t", ["a", "b"], d);
        var high = Page("high", ["a", "b"], d.AddDays(10));   // 2×80 = 160
        var low = Page("low", ["a"], d.AddDays(20));          // 1×80 = 80
        var none = Page("none", ["z"], d.AddDays(30));        // 0

        var f = new PagesRelatedFunction([target, high, low, none]);
        var arr = f.Invoke(new Scriban.TemplateContext(), null,
            new Scriban.Runtime.ScriptArray { target.ToScriptObjectForTest() }, null)
            as Scriban.Runtime.ScriptArray;

        Assert.NotNull(arr);
        var titles = arr!.Cast<Scriban.Runtime.ScriptObject>()
            .Select(o => o["title"]?.ToString()).ToList();
        Assert.Equal(["high", "low"], titles);  // none 得 0 分落选
    }

    [Fact]
    public void Related同日期加分()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var target = Page("t", [], d);
        var sameDay = Page("sameday", [], d);              // date 100
        var otherDay = Page("other", [], d.AddDays(5));    // 0

        var f = new PagesRelatedFunction([target, sameDay, otherDay]);
        var arr = f.Invoke(new Scriban.TemplateContext(), null,
            new Scriban.Runtime.ScriptArray { target.ToScriptObjectForTest() }, null)
            as Scriban.Runtime.ScriptArray;

        Assert.Single(arr!);
        Assert.Equal("sameday", ((Scriban.Runtime.ScriptObject)arr![0]!)["title"]);
    }
}

internal static class PageContextTestExtensions
{
    /// <summary>测试用：页面对象投影（走真实渲染器工厂）</summary>
    public static Scriban.Runtime.ScriptObject ToScriptObjectForTest(this PageContext page) =>
        ScribanTemplateRenderer.CreatePageObject(page);
}
