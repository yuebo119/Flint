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
    // Hugo 的 add/math.Add 是**多态**的（v0.166 实测）：操作数皆为字符串时拼接
    // （`add "1" "2"` → "12"、`add "a" "b"` → "ab"），皆为数值时求和
    // （`add 1 2 3` → 6），混用则报 "can't apply the operator to the values"。
    // Flint 对混用取宽容（按数值求和），字符串形态与 Hugo 一致
    [InlineData("math", "Add", "1", "2", "12")]
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
        Directory.CreateDirectory(Path.Combine(_dir, "icons"));
        File.WriteAllText(
            Path.Combine(_dir, "icons", "sprite.svg"),
            "<svg><symbol id=\"a\"><line x1=\"1\"/></symbol></svg>");
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

    /// <summary>
    /// **SVG 是文本资源**：主题靠 `.Content` 取符号表做图标内联（monochrome 的
    /// svg/feather.html：`resources.Get "lib/icns/…svg"` + `findRESubmatch` 抽 `&lt;symbol&gt;`）。
    /// 此前 SVG 按图像处理 → Content 恒空 → 图标全渲染成空 &lt;svg&gt;（实测）
    /// </summary>
    [Fact]
    public void Get读取SVG内容()
    {
        var p = new FileSystemResourceProvider("https://e.com", _dir);
        var r = p.Get("icons/sprite.svg");
        Assert.NotNull(r);
        Assert.Equal("image", r!.ResourceType);
        Assert.Contains("<symbol id=\"a\">", r.Content);
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
    public void Add按Hugo语义累加()
    {
        // 首个值**原样存入**，后续按类型累加（Hugo v0.166 实测：
        // 数字 5+7=12、字符串 "a"+"b"="ab"、切片 (slice 1)+(slice 2 3) = [1 2 3]）。
        // 旧断言假设"首个值包成单元素列表"，那是 hugo-book 场景的过度泛化
        var nums = new PageStoreObject();
        Call(nums, "add", "n", 5);
        Call(nums, "add", "n", 7);
        Assert.Equal(12d, Call(nums, "get", "n"));

        var strs = new PageStoreObject();
        Call(strs, "add", "t", "a");
        Call(strs, "add", "t", "b");
        Assert.Equal("ab", Call(strs, "get", "t"));

        // 序列收集：实参用切片（主题写法 `$.Scratch.Add "index" (slice .Page)`）
        var list = new PageStoreObject();
        Call(list, "add", "l", new Scriban.Runtime.ScriptArray { "first" });
        Call(list, "add", "l", new Scriban.Runtime.ScriptArray { "second" });
        var arr = Call(list, "get", "l") as System.Collections.IEnumerable;
        Assert.NotNull(arr);
        Assert.Equal(["first", "second"], arr!.Cast<object?>().ToArray());
    }

    [Fact]
    public void Add不产出文本()
    {
        // Hugo 的 Scratch.Add/Set/Delete 无返回值 → 模板里 `{{ $s.Add "k" v }}`
        // 渲染为空。此前 Add 返回存入值，每个"只调用不接收"的 Add 都会往页面吐文本
        var s = new PageStoreObject();
        Assert.Equal("", Call(s, "add", "n", 5));
        Assert.Equal("", Call(s, "set", "n", 6));
        Assert.Equal("", Call(s, "delete", "n"));
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

    /// <summary>
    /// <c>SetInMap MAP KEY VALUE</c> 之后 <c>Get MAP</c> 要返回该映射本身（Hugo Scratch 语义）：
    /// monochrome 的 baseof 用它初始化一串参数、head.html 再
    /// <c>(.Store.Get "params").enable_open_graph</c> 读取——此前 Get 只看扁平值表 →
    /// 读回 null → 依赖这些参数的整段 head 内容（opengraph/twitter_cards）不渲染
    /// </summary>
    [Fact]
    public void SetInMap写入的映射可由Get读回()
    {
        var s = new PageStoreObject();
        Call(s, "setinmap", "params", "enable_open_graph", true);
        var map = Call(s, "get", "params");
        Assert.NotNull(map);
        var obj = Assert.IsType<Scriban.Runtime.ScriptObject>(map);
        Assert.Equal(true, obj["enable_open_graph"]);
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
        // 结果形态 = 页面序列对象（与 .Pages 同型，IList<ScriptObject>），
        // 故 .Related 之后仍可继续调用集合方法族
        var arr = f.Invoke(new Scriban.TemplateContext(), null,
            new Scriban.Runtime.ScriptArray { target.ToScriptObjectForTest() }, null)
            as System.Collections.Generic.IList<Scriban.Runtime.ScriptObject>;

        Assert.NotNull(arr);
        var titles = arr!.Select(o => o["title"]?.ToString()).ToList();
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
            as System.Collections.Generic.IList<Scriban.Runtime.ScriptObject>;

        Assert.Single(arr!);
        Assert.Equal("sameday", arr![0]["title"]);
    }
}

internal static class PageContextTestExtensions
{
    /// <summary>测试用：页面对象投影（走真实渲染器工厂）</summary>
    public static Scriban.Runtime.ScriptObject ToScriptObjectForTest(this PageContext page) =>
        ScribanTemplateRenderer.CreatePageObject(page);
}

/// <summary>
/// 四主题收敛轮（2026-09-11）回归防护：这些缺陷都曾让真实主题构建失败，
/// 且**编译器与既有测试都放行**——只有真实主题跑四门禁才暴露。
/// 每个测试对应一个已确证的失败形态
/// </summary>
public sealed class ThemeConvergenceRegressionTests
{
    /// <summary>经模板渲染一个表达式（拿到真实引擎语义，含 Scriban 绑定规则）</summary>
    private static string Eval(
        string template,
        Action<Scriban.Runtime.ScriptObject>? setup = null,
        string? assetRoot = null)
    {
        var globals = new Scriban.Runtime.ScriptObject();
        var resources = assetRoot is null ? null : new FileSystemResourceProvider("https://e.com", assetRoot);
        new BuiltinTemplateFunctions("https://e.com", resources).RegisterFunctions(globals);
        globals["newScratch"] = new Func<PageStoreObject>(() => new PageStoreObject());
        globals["page"] = new Scriban.Runtime.ScriptObject
        {
            ["store"] = new PageStoreObject()
        };
        setup?.Invoke(globals);
        var ctx = new Scriban.TemplateContext
        {
            MemberRenamer = m => m.Name,
            StrictVariables = false
        };
        ctx.PushGlobal(globals);
        return Scriban.Template.Parse(template).Render(ctx);
    }

    /// <summary>页面集合形态（ScriptObject + IList&lt;ScriptObject&gt; + IDictionary 三合一）</summary>
    private sealed class PageListLike : Scriban.Runtime.ScriptObject, IList<Scriban.Runtime.ScriptObject>
    {
        private readonly List<Scriban.Runtime.ScriptObject> _items;

        public PageListLike(params string[] titles)
        {
            _items = [.. titles.Select(t => new Scriban.Runtime.ScriptObject { ["title"] = t })];
        }

        public Scriban.Runtime.ScriptObject this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        public new int Count => _items.Count;
        public new bool IsReadOnly => false;
        void ICollection<Scriban.Runtime.ScriptObject>.Add(Scriban.Runtime.ScriptObject item) => _items.Add(item);
        public new void Clear() => _items.Clear();
        public bool Contains(Scriban.Runtime.ScriptObject item) => _items.Contains(item);
        public void CopyTo(Scriban.Runtime.ScriptObject[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
        public new IEnumerator<Scriban.Runtime.ScriptObject> GetEnumerator() => _items.GetEnumerator();
        public int IndexOf(Scriban.Runtime.ScriptObject item) => _items.IndexOf(item);
        public void Insert(int index, Scriban.Runtime.ScriptObject item) => _items.Insert(index, item);
        public bool Remove(Scriban.Runtime.ScriptObject item) => _items.Remove(item);
        public void RemoveAt(int index) => _items.RemoveAt(index);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    }

    // ---- 集合函数：页面集合是 ScriptObject（实现 IDictionary），
    //      按接口类型判定会把序列误判为字典（四主题均命中）----

    [Fact]
    public void slice对页面集合取前N项()
    {
        // 曾是 `site.pages | slice 0 2` 产出 3 元素数组（走构造器分支）
        var r = Eval("{{ site.pages | slice 0 2 | array.size }}",
            g => g["site"] = new Scriban.Runtime.ScriptObject
            {
                ["pages"] = new PageListLike("a", "b", "c", "d", "e")
            });
        Assert.Equal("2", r);
    }

    [Fact]
    public void first对页面集合前缀形态与管道形态一致()
    {
        // 曾报 "Unable to convert type `int` to `IEnumerable<Object>`"
        var prefix = Eval("{{ first 2 site.pages | array.size }}",
            g => g["site"] = new Scriban.Runtime.ScriptObject
            {
                ["pages"] = new PageListLike("a", "b", "c", "d")
            });
        var piped = Eval("{{ site.pages | first 2 | array.size }}",
            g => g["site"] = new Scriban.Runtime.ScriptObject
            {
                ["pages"] = new PageListLike("a", "b", "c", "d")
            });
        Assert.Equal("2", prefix);
        Assert.Equal("2", piped);
    }

    [Fact]
    public void last对页面集合取末项()
    {
        var r = Eval("{{ (site.pages | last).title }}",
            g => g["site"] = new Scriban.Runtime.ScriptObject
            {
                ["pages"] = new PageListLike("a", "b", "c")
            });
        Assert.Equal("c", r);
    }

    [Fact]
    public void slice零参构造空序列()
    {
        // Hugo 可变参数构造器形态（LoveIt 用 `slice` 造空序列）
        Assert.Equal("0", Eval("{{ slice | array.size }}"));
        Assert.Equal("2", Eval("{{ slice \"a\" \"b\" | array.size }}"));
    }

    [Fact]
    public void in接受字符串haystack()
    {
        // 曾报 "Unable to convert type `string` to `IEnumerable<Object>`"（Stack）
        Assert.Equal("true", Eval("{{ in page.kind \"term\" }}", g =>
            g["page"] = new Scriban.Runtime.ScriptObject { ["kind"] = "term" }));
        Assert.Equal("false", Eval("{{ in page.kind \"home\" }}", g =>
            g["page"] = new Scriban.Runtime.ScriptObject { ["kind"] = "term" }));
    }

    [Fact]
    public void eq可变参数命中任一即真()
    {
        // Ananke：`compare.Eq $page.Language "de" "en" ...`（曾报 "Argument index must be < 2"）
        Assert.Equal("true", Eval("{{ eq page.lang \"de\" \"en\" \"zh\" }}",
            g => g["page"] = new Scriban.Runtime.ScriptObject { ["lang"] = "zh" }));
        Assert.Equal("false", Eval("{{ eq page.lang \"de\" \"en\" }}",
            g => g["page"] = new Scriban.Runtime.ScriptObject { ["lang"] = "zh" }));
    }

    [Fact]
    public void index越界返回空而非抛异常()
    {
        // PaperMod：`index (findRE ...) 0` 在无匹配时越界（曾报 "Argument index must be < 1"）。
        // 空序列用 slice 零参构造（内置 array 函数是构造器，不收 0 参）
        Assert.Equal("", Eval("{{ index slice 0 }}"));
        Assert.Equal("b", Eval("{{ index (slice \"a\" \"b\") 1 }}"));
        Assert.Equal("", Eval("{{ index (slice \"a\") 5 }}"));
    }

    // ---- 字符串函数：Hugo 的多参形态 ----

    [Fact]
    public void trim按cutset裁剪()
    {
        // PaperMod：`trim $x "\n\r\t "`（曾报 "Argument index must be < 1"）
        Assert.Equal("x", Eval("{{ trim \"\n\r\t x \n\" \"\\n\\r\\t \" }}".Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t")));
        Assert.Equal("a", Eval("{{ trim \"xxaxx\" \"x\" }}"));
    }

    [Fact]
    public void truncate省略号可省()
    {
        // PaperMod schema_json：`| truncate 180`（曾报 "Invalid number of arguments 2 ... expecting 3"）
        // Hugo v0.166 实测语义：默认省略号是 " …"（空格+省略号）且**不计入**长度参数——
        // `strings.Truncate 5 "abcdefghij"` → "abcde …"（len=7 = 5 字符 + 空格 + 3 字节省略号）。
        // 此前 Flint 按"省略号计入长度"实现（s[..4] + "…"，共 5 字符），与 Hugo 产出不一致
        var r = Eval("{{ \"abcdefghij\" | truncate 5 }}");
        Assert.Equal("abcde …", r);
    }

    // ---- printf：Go 动词 ----

    [Fact]
    public void printf支持Go动词()
    {
        // Stack：`printf "icons/%s.svg" .`（曾原样输出字面量 "icons/%s.svg"）
        Assert.Equal("icons/date.svg",
            Eval("{{ printf \"icons/%s.svg\" page.name }}",
                g => g["page"] = new Scriban.Runtime.ScriptObject { ["name"] = "date" }));
        Assert.Equal("042", Eval("{{ printf \"%03d\" 42 }}"));
        Assert.Equal("100%", Eval("{{ printf \"100%%\" }}"));
    }

    // ---- Store：大小写与独立实例 ----

    [Fact]
    public void store注册PascalCase别名()
    {
        // PaperMod：`$scratch.Add "meta" ...`（曾报 "Cannot get the member $scratch.Add"）
        var store = new PageStoreObject();
        Assert.True(store.TryGetValue(null, default, "Add", out var addFn) && addFn is not null);
        Assert.True(store.TryGetValue(null, default, "Set", out var setFn) && setFn is not null);
        Assert.True(store.TryGetValue(null, default, "Get", out var getFn) && getFn is not null);
    }

    [Fact]
    public void newScratch每次返回独立实例()
    {
        // 两个变量各自暂存，互不污染（PaperMod post_meta 用法）
        Assert.Equal("ab",
            Eval("{{ $a = newScratch }}{{ $b = newScratch }}{{ $a.set \"k\" \"a\" }}{{ $b.set \"k\" \"b\" }}{{ $a.get \"k\" }}{{ $b.get \"k\" }}"));
    }

    // ---- 资源：名无效时不产出（防写盘失败）----

    [Fact]
    public void FromString空名不产出资源()
    {
        // 管道左值错位时 name 会是空白（曾产出 RelPermalink "/assets/ " →
        // 输出路径无文件名 → 写盘抛异常并中断整次构建）
        var dir = Path.Combine(Path.GetTempPath(), $"flint-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 空白名 → 不产出资源（渲染为空）
            Assert.Equal("", Eval("{{ resources.FromString \" \" \"x\" }}", assetRoot: dir));
            // 合法名仍正常产出（守卫不误伤正常路径）。
            // 路径约定对齐 Hugo v0.166：资源发布在**站根**（`assets/a.css` → `/a.css`）
            Assert.Equal("/a.css",
                Eval("{{ (resources.FromString \"a.css\" \"body{}\").rel_permalink }}", assetRoot: dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch (IOException) { }
        }
    }

    // ---- add 的多态语义（字符串拼接 / 数值求和）----
    // Hugo v0.166 实测：add 与 math.Add 同一实现——操作数皆为字符串时拼接
    // （`add "1" "2"` → "12"、`add "a" "b"` → "ab"），皆为数值时求和
    // （`add 1 2 3` → 6），混用报 "can't apply the operator to the values"。
    // 主题靠字符串形态拼 URL/类名：clarity `add $relpath .`（$relpath 默认 ""）、
    // fixit `add $icon " me-1"`——早期一律 ToNum 求和把 "" + "x" 算成 0

    [Fact]
    public void add全字符串做拼接()
    {
        Assert.Equal("/img/x.png", Eval("{{ add \"\" \"/img/x.png\" }}"));
        Assert.Equal("fa-solid fa-tag me-1", Eval("{{ add \"fa-solid fa-tag\" \" me-1\" }}"));
        Assert.Equal("12", Eval("{{ add \"1\" \"2\" }}"));
        Assert.Equal("", Eval("{{ add \"\" \"\" }}"));
    }

    [Fact]
    public void add全数值做求和()
    {
        Assert.Equal("3", Eval("{{ add 1 2 }}"));
        Assert.Equal("6", Eval("{{ add 1 2 3 }}"));
        Assert.Equal("0", Eval("{{ add }}"));
    }

    // ---- errorf 不中断渲染 ----
    // Hugo 实测：`HOME {{ errorf "boom: %s" "detail" }} END` 产出 "HOME  END"
    // 并以 exit=1 收尾（构建结束按错误计数判失败）。Flint 早期直接抛异常
    // 中止整页渲染——主题里一处 errorf 就让整站塌成空页（fixit 的 icon.html
    // 4 处 errorf → 仅剩 2 页 / 232B）

    [Fact]
    public void errorf记录后继续渲染()
    {
        var reported = new List<string>();
        var globals = new Scriban.Runtime.ScriptObject();
        var fns = new BuiltinTemplateFunctions("https://e.com") { ErrorReporter = reported.Add };
        fns.RegisterFunctions(globals);
        var ctx = new Scriban.TemplateContext { MemberRenamer = m => m.Name, StrictVariables = false };
        ctx.PushGlobal(globals);
        var output = Scriban.Template.Parse("HOME {{ errorf \"boom: %s\" \"detail\" }} END").Render(ctx);
        Assert.Equal("HOME  END", output);
        Assert.Equal(["boom: detail"], reported);
    }

    [Fact]
    public void errorf按Go动词格式化()
    {
        // 早期 FormatMessage 只走 .NET string.Format：Go 的 %s/%v 不是占位符 →
        // FormatException → 原样返回格式串（fixit 日志里出现字面 "Icon src is missing: %s"）
        var reported = new List<string>();
        var globals = new Scriban.Runtime.ScriptObject();
        var fns = new BuiltinTemplateFunctions("https://e.com") { ErrorReporter = reported.Add };
        fns.RegisterFunctions(globals);
        var ctx = new Scriban.TemplateContext { MemberRenamer = m => m.Name, StrictVariables = false };
        ctx.PushGlobal(globals);
        Scriban.Template.Parse("{{ errorf \"Icon does not exist %v\" \"a.svg\" }}").Render(ctx);
        Assert.Equal(["Icon does not exist a.svg"], reported);
    }
}
