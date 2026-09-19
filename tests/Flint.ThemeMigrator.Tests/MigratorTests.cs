// Flint 主题迁移工具测试
// 回归防护对象（本轮实测确证，均为正则版无法正确处理的情形）：
// 1. 引号内含 }} / {{ 时正确切分（正则版会静默损坏）
// 2. 嵌套结构产出正确语义（正则版产出 `(len < page.pages) 10` 这类垃圾）
// 3. Scriban 的 for 不接受裸参数列表（须括号化）
// 4. 注释必须完全丢弃（保留会污染产物致解析失败）
// 5. 双变量 range 展开为单变量 + .key/.value 解构
// 6. dict 空值/非标识符键的特殊处理

using Flint.ThemeMigrator.Conversion;
using Flint.ThemeMigrator.Migration;
using Flint.ThemeMigrator.Parsing;
using Xunit;

namespace Flint.ThemeMigrator.Tests;

/// <summary>词法器：正确的引号/注释/嵌套处理</summary>
public sealed class LexerTests
{
    [Fact]
    public void 引号内含右定界符不被切开()
    {
        // 正则版的静默损坏点：{{ printf "}}" }} 曾被切在引号内，残渣泄漏成正文
        var tokens = new GoTemplateLexer("A{{ printf \"}}\" }}B").Tokenize();

        var strings = tokens.Where(t => t.Type == TokenType.String).Select(t => t.Value).ToList();
        Assert.Contains("\"}}\"", strings);
        Assert.Equal(2, tokens.Count(t => t.Type == TokenType.Text));
    }

    [Fact]
    public void 引号内含左定界符不被切开()
    {
        var tokens = new GoTemplateLexer("{{ printf \"{{\" }}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.String && t.Value == "\"{{\"");
    }

    [Fact]
    public void 注释识别含裁剪标记形态()
    {
        var tokens = new GoTemplateLexer("{{- /* note */ -}}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.Comment);
    }

    [Fact]
    public void 字段与变量区分()
    {
        var tokens = new GoTemplateLexer("{{ .Params.Title $x }}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.Field && t.Value == ".Params.Title");
        Assert.Contains(tokens, t => t.Type == TokenType.Variable && t.Value == "$x");
    }

    [Fact]
    public void 反引号原始串不被转义干扰()
    {
        var tokens = new GoTemplateLexer("{{ printf `a\"b` }}").Tokenize();
        Assert.Contains(tokens, t => t.Type == TokenType.RawString);
    }
}

/// <summary>解析器 + 转换器：结构驱动转换的正确性</summary>
public sealed class ParserConverterTests
{
    private static string Convert(string template)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault()).Convert(parts);
    }

    /// <summary>带"本文件同名命名模板已提取"标记的转换（partial 调用是否加 __named 后缀）</summary>
    private static string ConvertWithSelfNamed(string template, string selfPartial, bool selfNamedExtracted)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(
            MigrationMap.CreateDefault(), null, selfPartial, false, selfNamedExtracted).Convert(parts);
    }

    [Fact]
    public void partial自调用在无同名define时不加后缀()
    {
        // fixit 的 camel-case-keys 递归调用自己处理嵌套 map——它不是命名模板。
        // 若按"名字与所在文件相同"就加后缀，会指向不存在的文件：
        // "Could not find a part of the path …camel-case-keys__named"
        var result = ConvertWithSelfNamed(
            "{{ partial \"function/camel-case-keys.html\" . }}",
            "function/camel-case-keys",
            selfNamedExtracted: false);

        Assert.Contains("_partials/function/camel-case-keys\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("__named", result, StringComparison.Ordinal);
    }

    [Fact]
    public void partial自调用在确有同名define时加后缀()
    {
        // techdoc 的 pagination.html 内有 {{ define "pagination" }}：
        // 提取器把它改名为 pagination__named.html，调用侧必须同步
        var result = ConvertWithSelfNamed(
            "{{ partial \"pagination.html\" (dict \"a\" 1) }}",
            "pagination",
            selfNamedExtracted: true);

        Assert.Contains("_partials/pagination__named", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 嵌套括号结构正确()
    {
        // 正则版的静默损坏点：lt (len .Pages) 10 → (len < page.pages) 10
        var result = Convert("{{ lt (len .Pages) 10 }}");

        // Scriban 的函数是前缀形态 len <arg>（不是 C 风格 len(...)），
        // 关键是结构与运算符正确
        Assert.Contains("len page.pages", result, StringComparison.Ordinal);
        // 比较运算符产出宽容比较函数 num_lt（Scriban 的 < 在两侧类型不一致时抛
        // "Unable to convert type object to int"；Hugo 语义是类型强制后比较）
        Assert.Contains("num_lt", result, StringComparison.Ordinal);
        Assert.Contains("10", result, StringComparison.Ordinal);
        Assert.DoesNotContain("(len < page.pages) 10", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 管道逐段转换()
    {
        var result = Convert("{{ .Title | markdownify }}");
        Assert.Contains("page.title", result, StringComparison.Ordinal);
        Assert.Contains("markdownify", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 命名空间调用保持完整()
    {
        // 曾被错误拆成 "strings page.ToUpper"（Field 当参数）
        var result = Convert("{{ strings.ToUpper \"a\" }}");
        Assert.Contains("strings.ToUpper", result, StringComparison.Ordinal);
        Assert.DoesNotContain("strings page", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 比较函数转中缀()
    {
        var result = Convert("{{ eq .Kind \"home\" }}");
        Assert.Contains("page.kind == \"home\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 逻辑函数转惰性取值()
    {
        // Go 的 and/or 既**惰性短路**又**返回操作数本身**（Hugo v0.166 实测：
        // `and "a" "b"` → "b"、`and 1 0 2` → 0、`and "a" "b" "c"` → "c"、
        // `or "" 0` → 0、`or "a" "b"` → "a"），故产出**取值三元链**：
        // 急切求值的 `&&`/`||` 会打掉"靠短路保护 nil"的写法，
        // 而只产出布尔常量会让赋值语境的 74 处 `$x := or A B` 拿到 "true"
        var result = Convert("{{ and .Draft (not .Title) }}");
        Assert.Contains("!(page.title)", result, StringComparison.Ordinal);
        Assert.Contains("? ", result, StringComparison.Ordinal);
        Assert.DoesNotContain("&&", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 取值or保留操作数而非布尔()
    {
        // `or A B` → `(A) ? (A) : (B)`：A 真取 A、否则取 B（Hugo `or "a" "b"` → "a"）
        var result = Convert("{{ $t := or \"a\" \"b\" }}");
        Assert.Contains("(\"a\") ? (\"a\") : (\"b\")", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 取值and返回首个假值否则末值()
    {
        // `and A B` → `(A) ? (B) : (A)`（Hugo `and 1 0 2` → 0、`and "a" "b"` → "b"）
        var result = Convert("{{ $t := and \"a\" \"b\" }}");
        Assert.Contains("(\"a\") ? (\"b\") : (\"a\")", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 双变量range展开为解构()
    {
        // Scriban 不支持 for k, v in（实测 PARSE-ERR）
        var result = Convert("{{ range $k, $v := .Params }}{{ $k }}{{ end }}");
        Assert.Contains("for ", result, StringComparison.Ordinal);
        // Scriban 迭代**映射**产出 {Key, Value}（PascalCase——`x.key` 取不到，实测）；
        // 迭代**序列**产出元素本身，故用 `?? for.index` / `?? pair` 统一两种形态
        Assert.Contains(".Key", result, StringComparison.Ordinal);
        Assert.Contains(".Value", result, StringComparison.Ordinal);
        Assert.DoesNotContain("for $k, $v", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range集合加括号()
    {
        // Scriban 的 for 不接受裸参数列表（实测 PARSE-ERR）
        var result = Convert("{{ range split . \",\" }}X{{ end }}");
        Assert.Contains("for ", result, StringComparison.Ordinal);
        Assert.Contains("(split ", result, StringComparison.Ordinal);
    }

    [Fact]
    public void partial上下文参数写入产物()
    {
        // Hugo 的 `partial "x" CONTEXT` 把 CONTEXT 作为 partial 内的 `.`；
        // Flint 的 partial 函数接收第二参数并临时绑成 page（Hugo dot 语义等价），
        // 故内容**写入产物**而非仅记诊断（旧行为丢弃参数，致 partial 内 `.` 取不到值）
        var tokens = new GoTemplateLexer("{{ partial \"x\" (dict \"k\" 1) }}").Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        var converter = new TemplateConverter(MigrationMap.CreateDefault());
        var output = converter.Convert(parts);

        Assert.Contains("partial \"_partials/x\"", output, StringComparison.Ordinal);
        Assert.Contains("dict \"k\" 1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void dict直接构造为对象字面量()
    {
        // Scriban **没有对象字面量**：`{ k: v }` 被当语句块（作函数实参时 jsonify
        // 收到 0 参 → "Argument index must be < 1"，Congo 实测），故一律产 dict 函数
        var result = Convert("{{ $d := dict \"k\" 1 }}");
        Assert.Contains("dict \"k\" 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 空dict用函数形态()
    {
        // Scriban 无法解析空对象字面量 { }
        var result = Convert("{{ $d := dict }}");
        Assert.Contains("$d = dict", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{ }", result, StringComparison.Ordinal);
    }

    [Fact]
    public void dict非标识符键加引号()
    {
        var result = Convert("{{ dict \"a=1\" \"v\" }}");
        Assert.Contains("dict \"a=1\" \"v\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 多参dict中的括号实参不触发结构守卫()
    {
        // 括号出现在**实参**位置是合法形态。此前结构守卫的畸形状检查是全局正则
        // `\)\s*[A-Za-z0-9_"']`，只有标识符**紧邻**左括号（`f(`）时才放行——
        // `dict "k" (slice 1 2) "k2" "v2"` 的括号前还有其他实参，放行条件匹配不上，
        // 整条表达式被判 Unsupported 并**静默产出空字符串**
        //（fixit 的 get-taxonomy-icon → `$defaults = ""` → index 越界；stack 的 helper/image 同源）
        var result = Convert("{{ $d := dict \"a\" (slice \"1\" \"2\") \"b\" \"9\" }}");

        Assert.Contains("dict \"a\"", result, StringComparison.Ordinal);
        Assert.Contains("[\"1\", \"2\"]", result, StringComparison.Ordinal);
        Assert.Contains("\"b\" \"9\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("$d = \"\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void dict变量键不再判不支持()
    {
        // `dict $newKey $newValue`（键是变量）此前被判 Unsupported 并**静默产出空串**：
        // FixIt 的 camel-case-keys.html 因此把
        // `$output = merge $output (dict $newKey $newValue)` 转成 `$output = ""`，
        // 返回值通道发空、下游 `index $output 0` 越界
        //（"Index was outside the bounds of the array"）。Scriban 的 dict 接受任意
        // 表达式作键（实测 `dict $k $v` → {"myKey":7}）
        var result = Convert("{{ $o = merge $o (dict $newKey $newValue) }}");

        Assert.Contains("dict $newKey $newValue", result, StringComparison.Ordinal);
        Assert.DoesNotContain("= \"\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void dictLiteral键仍加引号()
    {
        // 字面量键保持既有行为（非标识符键裸写会触发 Scriban 解析错误）
        var result = Convert("{{ $d := dict \"data-src\" 1 }}");
        Assert.Contains("dict \"data-src\" 1", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 多行dict逐对转换()
    {
        // 主题里 dict 常跨多行书写（fixit 的 get-taxonomy-icon 三对键值各占一行）
        var result = Convert(
            "{{- $d := dict\n  \"a\" (slice \"1\" \"2\")\n  \"b\" (slice \"3\" \"4\")\n-}}");

        Assert.Contains("dict \"a\"", result, StringComparison.Ordinal);
        Assert.Contains("[\"1\", \"2\"]", result, StringComparison.Ordinal);
        Assert.Contains("[\"3\", \"4\"]", result, StringComparison.Ordinal);
        Assert.DoesNotContain("= \"\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 注释完全丢弃()
    {
        var result = Convert("A{{/* note */}}B");
        Assert.Equal("AB", result);
    }

    [Fact]
    public void define转capture并生成include()
    {
        // Scriban 无 extends/block 语句，用 capture + 命名参数 include
        var result = Convert("{{ define \"main\" }}X{{ end }}");
        Assert.Contains("capture blk_main", result, StringComparison.Ordinal);
        Assert.Contains("include \"baseof.html\" blk_main: blk_main", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 参数式Param转paramLookup()
    {
        // $.Param 是页面参数查询；Scriban 的 $ 是函数参数数组，直接改名会失败
        var result = Convert("{{ .Param \"x\" }}");
        Assert.Contains("paramLookup", result, StringComparison.Ordinal);
        Assert.Contains("\"x\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 日期格式化方法转换()
    {
        var result = Convert("{{ .Date.Format \"2006-01-02\" }}");
        Assert.Contains("date.to_string", result, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// Hugo 的 `time.Format` 是 **(布局, 值)**，Flint 的 `date.to_string` 是 **(值, 布局)**，
    /// 且 Scriban 管道把左值注入**首参**——两种形态分开处理（探针与产物形态见
    /// docs/HUGO-COMPAT-MATRIX §Q）：
    /// </summary>
    [Fact]
    public void 管道形态的时间格式化不换序()
    {
        // `{{ .Date | time.Format "2006-01-02" }}`：管道补左值到首参 → 只传布局串
        var result = Convert("{{ .Date | time.Format \"2006-01-02\" }}");
        Assert.Contains("date.to_string", result, StringComparison.Ordinal);
        Assert.Contains("\"2006-01-02\"", result, StringComparison.Ordinal);
        // 布局串**保持 Go 原样**交给引擎（运行期布局也要吃下）
        Assert.DoesNotContain("yyyy-MM-dd", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 日期值方法改写为引擎函数()
    {
        // Hugo 的 `.Date.IsZero`/`.Lastmod.Unix` 是 time.Time 上的方法；
        // Flint 的点号链取不到值成员（静默空值 → 守卫恒真 → 无日期页也渲染日期）
        Assert.Contains("date.is_zero page.date", Convert("{{ if not .Date.IsZero }}x{{ end }}"),
            StringComparison.Ordinal);
        Assert.Contains("date.unix page.lastmod", Convert("{{ .Lastmod.Unix }}"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void 日期值方法在变量接收者上也改写()
    {
        var result = Convert("{{ $p.Date.IsZero }}");
        Assert.Contains("date.is_zero", result, StringComparison.Ordinal);
        Assert.Contains("$p", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 渲染视图在循环内用迭代项作上下文()
    {
        // Hugo 的 `.Render` 是页面方法，渲染**点号所在的那一页**：range 体内每项渲染自己。
        // 此前接收者按页面根处理 → ananke 首页三张 summary 全部渲染成 Home（实测）
        var result = Convert("{{ range .Pages }}{{ .Render \"summary\" }}{{ end }}");
        Assert.Contains("render \"summary\" $__it0", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 渲染视图在循环外用页面根作上下文()
    {
        var result = Convert("{{ .Render \"summary\" }}");
        Assert.Contains("render \"summary\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("$__it", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// 跨文件命名模板的 block：`partials/` 下的文件里 `{{ block "X" . }}` 指的是
    /// **另一个** partial 里 define 的命名模板（Hugo 的命名模板是全局的）。
    /// 同文件约定（baseof 的声明 + 覆盖）走槽位机制，不受影响。
    /// github-style 的 user-profile.html 依赖此形态渲染文章列表
    /// </summary>
    [Fact]
    public void 跨文件block改为渲染提取出的块体()
    {
        var parts = new GoTemplateParser(
            new GoTemplateLexer("{{ block \"posts\" . }}{{ end }}").Tokenize()).Parse();
        var converter = new TemplateConverter(
            MigrationMap.CreateDefault(), crossFileBlockNames: ["posts"]);
        var result = converter.Convert(parts);
        Assert.Contains("partial \"_partials/posts__block\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("blk_posts", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// **partial 体内**的 dot 上下文要显式传出：Hugo 的 partial 不带参数时以调用方的 dot
    /// 渲染，Flint 的 `partial` 不带上下文取的是**渲染页**——卡片 partial（dot = 文章）里
    /// 再调元信息 partial 会拿到列表页（blowfish 的卡片日期整段消失，实测）
    /// </summary>
    [Fact]
    public void partial体内的dot上下文显式传出()
    {
        var parts = new GoTemplateParser(
            new GoTemplateLexer("{{ partial \"article-meta/basic.html\" . }}").Tokenize()).Parse();
        var converter = new TemplateConverter(
            MigrationMap.CreateDefault(), selfPartialName: "_partials/article-link/simple");
        var result = converter.Convert(parts);
        Assert.Contains("partial \"_partials/article-meta/basic\" page", result,
            StringComparison.Ordinal);
    }

    [Fact]
    public void 页面模板内的dot上下文保持省略()
    {
        // 页面模板里 dot 与 page 本就相等：保持原形态（不引入无意义的参数）
        var parts = new GoTemplateParser(
            new GoTemplateLexer("{{ partial \"foo.html\" . }}").Tokenize()).Parse();
        var converter = new TemplateConverter(MigrationMap.CreateDefault());
        var result = converter.Convert(parts);
        Assert.Contains("partial \"_partials/foo\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("page", result, StringComparison.Ordinal);
    }

    /// <summary>
    /// 槽位默认体**上提到文件最前**（hugo-book 的 baseof 形态）：`{{ template "X" . }}`
    /// 出现在 `{{ define "X" }}` 之前时，就地 capture 的 `__def_X` 尚未赋值 →
    /// 兜底拿到空值（侧边菜单/toc/header 全部不渲染，实测）。Go 的 define 是解析期注册
    /// </summary>
    [Fact]
    public void 槽位默认体上提到文件最前()
    {
        var parts = new GoTemplateParser(
            new GoTemplateLexer("{{ template \"menu\" . }}\n{{ define \"menu\" }}M{{ end }}").Tokenize()).Parse();
        var converter = new TemplateConverter(
            MigrationMap.CreateDefault(),
            slotNames: new HashSet<string>(StringComparer.Ordinal) { "menu" },
            isBaseTemplate: true);
        var result = converter.Convert(parts);
        var captureIndex = result.IndexOf("capture __def_menu", StringComparison.Ordinal);
        var useIndex = result.IndexOf("blk_menu ?? __def_menu", StringComparison.Ordinal);
        Assert.True(captureIndex >= 0, $"应产出兜底 capture，实际：{result}");
        Assert.True(useIndex > captureIndex, $"capture 应在使用点之前，实际：{result}");
    }

    /// <summary>
    /// baseof 序幕与块体的求值顺序：Hugo 的块体在 baseof 之内求值（init 链先写
    /// site.store、块体随后读），而转换把块体捕获提到 include 之前 → 块体读 store
    /// 拿到空值（fixit 的首页文章列表整段不渲染，实测）。序幕须**提到块体之前**
    /// </summary>
    [Fact]
    public void baseof序幕提到块体之前()
    {
        var parts = new GoTemplateParser(
            new GoTemplateLexer(
                "{{- partial \"init/index.html\" . -}}\n<div>{{ block \"main\" . }}{{ end }}</div>\n{{ define \"main\" }}X{{ end }}")
                .Tokenize()).Parse();
        // baseof 自身：跳过序幕段（序幕移到独立 partial，避免执行两次）
        var baseofConverter = new TemplateConverter(
            MigrationMap.CreateDefault(),
            slotNames: new HashSet<string>(StringComparer.Ordinal) { "main" },
            isBaseTemplate: true,
            baseofProloguePath: "_partials/__baseof_prologue",
            baseofProloguePartCount: 1);
        var baseofResult = baseofConverter.Convert(parts);
        Assert.DoesNotContain("init/index.html", baseofResult, StringComparison.Ordinal);

        // 页面模板：序幕调用在最前、先于块体捕获（init 链先写 store、块体随后读）
        var pageParts = new GoTemplateParser(
            new GoTemplateLexer("{{ define \"main\" }}X{{ end }}\n{{ include \"baseof.html\" blk_main: blk_main }}")
                .Tokenize()).Parse();
        var pageConverter = new TemplateConverter(
            MigrationMap.CreateDefault(),
            slotNames: new HashSet<string>(StringComparer.Ordinal) { "main" },
            baseofProloguePath: "_partials/__baseof_prologue");
        var pageResult = pageConverter.Convert(pageParts);
        var prologueIndex = pageResult.IndexOf(
            "partial \"_partials/__baseof_prologue\"", StringComparison.Ordinal);
        var captureIndex = pageResult.IndexOf("capture blk_main", StringComparison.Ordinal);
        Assert.True(prologueIndex >= 0, $"页面模板应 include 序幕，实际：{pageResult}");
        Assert.True(captureIndex > prologueIndex, $"序幕应先于块体捕获，实际：{pageResult}");
    }

    [Fact]
    public void 变量接收者的Format保留引号()
    {
        // `$x.Date.Format "…"` 走的是链式名分支（与 `.Date.Format` 不同的代码路径）：
        // 漏引号会让 Scriban 把格式串当变量表达式 → 求值为 null → 落到默认格式
        // （stack 的 datetime 属性实测产出 '2026-01-15' 而非 RFC3339）
        var result = Convert("{{ $x.Date.Format \"January 2, 2006\" }}");
        Assert.Contains("\"MMMM d, yyyy\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 带时区的Format布局原样保留交引擎()
    {
        // 含时区 token 的布局不编译期转换：.NET 没有"无冒号偏移"（Go 的 -0700 → "+0000"）
        // 也没有时区缩写（Go 的 MST → "UTC"），只有引擎拿到**原始 Go 布局**才能后处理
        // （github-style 的 RFC1123 输出实测：`+0000`）
        var result = Convert("{{ $x.Date.Format \"Mon, 02 Jan 2006 15:04:05 -0700\" }}");
        Assert.Contains("\"Mon, 02 Jan 2006 15:04:05 -0700\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("zzz", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 直接调用形态的时间格式化换序()
    {
        // `{{ time.Format "2006-01-02" .Date }}`：实参已就位 → 换成 (值, 布局)
        var result = Convert("{{ time.Format \"2006-01-02\" .Date }}");
        var idxValue = result.IndexOf("page.date", StringComparison.Ordinal);
        var idxLayout = result.IndexOf("\"2006-01-02\"", StringComparison.Ordinal);
        Assert.Contains("date.to_string", result, StringComparison.Ordinal);
        Assert.True(idxValue >= 0 && idxLayout > idxValue,
            $"值应在布局之前，实际产物：{result}");
    }

    [Fact]
    public void 布局参数内嵌default管道不被外提()
    {
        // `{{ return time.Format (site.Params.dateFormat | default ":date_long") . }}`：
        // 内层 default 作用于**格式串**。ParenthesizeIfCallWithArgs 此前用
        // IndexOf(" | ") 盲切——把嵌在括号里的管道切到调用外层，default 的作用
        // 对象变成 date.to_string 的**结果**（结果非空 → default 永不生效 →
        // :date_long 具名格式被吞，blowfish 日期卡显示 ISO 而非 "January 15, 2026"）
        var result = Convert("{{ return time.Format (site.Language.Params.dateFormat | default \":date_long\") . }}");
        Assert.Contains(
            "(site?.language?.params?.date_format | default \":date_long\")",
            result, StringComparison.Ordinal);
    }

    [Fact]
    public void 资源方法在资源上下文补前缀()
    {
        // with .Resources.ByType 块内的裸 .GetMatch：隐式接收者是资源对象
        var result = Convert(
            "{{ with .Resources.ByType \"image\" }}{{ with .GetMatch \"x*\" }}Y{{ end }}{{ end }}");
        // nil 安全化后形态为 `page?.resources?.bytype`（行为等价，仅点号安全化）
        Assert.Contains("resources", result, StringComparison.Ordinal);
        Assert.Contains("bytype", result, StringComparison.Ordinal);
        Assert.Contains("getmatch", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 翻译函数映射为i18n()
    {
        var result = Convert("{{ lang.Translate \"readMore\" }}");
        Assert.Contains("i18n", result, StringComparison.Ordinal);
        Assert.DoesNotContain("lang.Translate", result, StringComparison.Ordinal);
    }

    [Fact]
    public void partial上下文参数省略()
    {
        // dot 上下文即调用者页面 → 走 Flint 的 partial（共享调用者上下文），
        // 不传上下文参数。用 partial 而非 Scriban 内置 include：内置 include 在
        // 模板缺失时硬抛，而主题常引用由 Hugo Module 提供的 partial
        var result = Convert("{{ partial \"cover\" . }}");
        Assert.Contains("partial \"_partials/cover\"", result, StringComparison.Ordinal);
    }
}

/// <summary>结构守卫：静默损坏防线</summary>
public sealed class StructuralGuardTests
{
    [Theory]
    [InlineData("(a < b) c", true)]   // 括号后跟操作数 → 可疑
    [InlineData("(a == b)", false)]   // 正常括号表达式
    [InlineData("(a", true)]          // 括号不平衡
    [InlineData("", true)]            // 空产出
    [InlineData("f(page.title)", false)]
    public void 结构检查识别可疑产出(string text, bool shouldFlag)
    {
        var problem = StructuralGuard.Check(text);
        Assert.Equal(shouldFlag, problem is not null);
    }

    [Fact]
    public void 引号不平衡被识别()
    {
        Assert.NotNull(StructuralGuard.Check("f \"unclosed"));
    }
}

/// <summary>Go 日期布局转换（确定性映射）</summary>
public sealed class DateFormatConverterTests
{
    [Theory]
    [InlineData("2006-01-02", "yyyy-MM-dd")]
    [InlineData("2006-01-02T15:04:05Z07:00", "yyyy-MM-ddTHH:mm:sszzz")]
    [InlineData("January 2, 2006", "MMMM d, yyyy")]
    [InlineData("15:04:05", "HH:mm:ss")]
    public void Go布局转Net格式(string go, string net)
    {
        Assert.Equal(net, GoDateFormatConverter.Convert(go));
    }

    [Fact]
    public void 长模式优先于短模式()
    {
        // "2006-01-02" 必须整体匹配，不能被 "2006" 抢先替换
        var result = GoDateFormatConverter.Convert("2006-01-02");
        Assert.Equal("yyyy-MM-dd", result);
        Assert.DoesNotContain("MM-dd", result.Replace("yyyy-MM-dd", "", StringComparison.Ordinal),
            StringComparison.Ordinal);
    }
}

/// <summary>partial 返回值语义（Hugo 返回对象 vs Scriban 文本化）</summary>
public sealed class PartialReturnValueTests
{
    private static string Convert(string template, string? selfPartial = null,
        IReadOnlySet<string>? valueReturning = null)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault(), valueReturning, selfPartial).Convert(parts);
    }

    [Fact]
    public void return在partial内改写为store通道()
    {
        // Hugo 的 return 返回任意对象；Scriban 的 include 只能文本化，
        // 故用页面 Store 作对象通道（实测：跨 include 保真且零序列化）
        var result = Convert("{{ return $x }}", selfPartial: "func/maker");
        // 通道落在**渲染上下文**的 store（`__partial_ret_set`）而非 page.store：
        // 短代码/render hook/被覆盖 page 的 partial 里 page 可能为 null（实测）
        Assert.Contains("__partial_ret_set", result, StringComparison.Ordinal);
        Assert.Contains("__partial_ret_func/maker", result, StringComparison.Ordinal);
        Assert.Contains("ret", result, StringComparison.Ordinal);
    }

    [Fact]
    public void return在非partial内保持ret()
    {
        var result = Convert("{{ return $x }}");
        Assert.Contains("ret $x", result, StringComparison.Ordinal);
        Assert.DoesNotContain("page.store.set", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 无参return提前退出()
    {
        var result = Convert("{{ return }}", selfPartial: "func/maker");
        Assert.Contains("ret", result, StringComparison.Ordinal);
        Assert.DoesNotContain("page.store.set", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 返回值型partial的调用点改为partialValue()
    {
        var vr = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "func/maker" };
        var result = Convert("{{ $v := partial \"func/maker.html\" . }}", valueReturning: vr);
        Assert.Contains("partialValue", result, StringComparison.Ordinal);
        Assert.DoesNotContain("include", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 非返回值型partial走文本渲染路径()
    {
        var vr = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "func/other" };
        var result = Convert("{{ partial \"func/maker.html\" . }}", valueReturning: vr);
        Assert.Contains("partial \"_partials/func/maker\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("partialValue", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 返回值型partial上下文非dot时传上下文()
    {
        // FixIt 实测：丢弃上下文会让 partial 内的 `.` 落到外层 page
        // （`split page "_"` 拿到页面对象 → 递归深度上限）。引擎侧 partialValue
        // 已支持第二参数，故产物是 `partialValue NAME CONTEXT`
        var vr = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "func/maker" };
        var result = Convert("{{ partial \"func/maker.html\" $otherPage }}", valueReturning: vr);
        Assert.Contains("partialValue", result, StringComparison.Ordinal);
        Assert.Contains("$otherPage", result, StringComparison.Ordinal);
    }

    [Fact]
    public void partial名规范化对齐()
    {
        Assert.Equal("func/x", ScribanConverter.CanonicalPartialName("func/x.html"));
        Assert.Equal("func/x", ScribanConverter.CanonicalPartialName("layouts/_partials/func/x.html"));
        Assert.Equal("func/x", ScribanConverter.CanonicalPartialName("_partials/func/x.html"));
        Assert.Equal("x", ScribanConverter.CanonicalPartialName("x.html"));
    }

    [Fact]
    public void return被识别为关键字而非标识符()
    {
        // 回归防护：return 不在 Go 标准库 key 表中（Hugo 扩展），
        // 未识别会产出 "IdentifierExpr { Raw = return }" 这类 TODO
        var result = Convert("{{ return 1 }}");
        Assert.DoesNotContain("TODO-HUGO", result, StringComparison.Ordinal);
    }
}

/// <summary>块内裸点映射（range/with 作用域）</summary>
public sealed class BlockScopeMappingTests
{
    private static string Convert(string template)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault()).Convert(parts);
    }

    [Fact]
    public void range内裸点映射到循环变量()
    {
        // 回归防护：`{{ range .Pages }}{{ .Title }}` 曾产出 page.title
        // （循环变量被忽略，渲染错误页面的标题）
        var result = Convert("{{ range .Pages }}{{ .Title }}{{ end }}");
        Assert.Contains("for $__it", result, StringComparison.Ordinal);
        Assert.Contains(".title", result, StringComparison.Ordinal);
        // 关键：不得出现裸 page.title（应为循环变量.title）
        Assert.DoesNotContain("{{ page.title }}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range内PageNumber不被Page前缀误伤()
    {
        // 回归防护：`.PageNumber` 曾因以 `.Page` 开头被排除条件误伤，
        // 产出裸 page_number（应映射为循环变量的 .page_number）
        var result = Convert("{{ range .Pagers }}{{ .PageNumber }}{{ end }}");
        Assert.Contains("$__it", result, StringComparison.Ordinal);
        Assert.Contains("page_number", result, StringComparison.Ordinal);
        Assert.DoesNotContain("{{ page_number }}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range内Pages不被Page前缀误伤()
    {
        var result = Convert("{{ range .Sections }}{{ range .Pages }}{{ .Title }}{{ end }}{{ end }}");
        Assert.DoesNotContain("{{ page.pages }}", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 显式根不受作用域影响()
    {
        // .Site.* / $.* 是显式根，即使在 range 内也不应映射到循环变量
        var result = Convert("{{ range .Pages }}{{ .Site.Title }}{{ end }}");
        Assert.Contains("site.title", result, StringComparison.Ordinal);
    }

    [Fact]
    public void with内裸点映射到上下文变量()
    {
        var result = Convert("{{ with .Params.author }}{{ . }}{{ end }}");
        Assert.Contains("if $__w", result, StringComparison.Ordinal);
    }

    [Fact]
    public void range外层裸点仍映射到page()
    {
        // 无作用域时保持原语义
        var result = Convert("{{ .Title }}");
        Assert.Contains("page.title", result, StringComparison.Ordinal);
    }
}

/// <summary>
/// 四主题收敛轮（2026-09-11）转换层回归：以下形态都曾让真实主题构建失败
/// </summary>
public sealed class PipeAndParserRegressionTests
{
    private static string Convert(string template)
    {
        var tokens = new GoTemplateLexer(template).Tokenize();
        var parts = new GoTemplateParser(tokens).Parse();
        return new TemplateConverter(MigrationMap.CreateDefault()).Convert(parts);
    }

    [Fact]
    public void 管道左值作末参改写为显式调用()
    {
        // Go/Hugo：`X | f A` == `f A X`；Scriban 注入**首参**。
        // PaperMod 实测：`" " | resources.FromString "a.css"` 曾产出名为 " " 的资源
        var result = Convert("{{ \"\" | resources.FromString \"a.css\" }}");
        Assert.Contains("resources.FromString \"a.css\" \"\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 管道左值为多token表达式时加括号()
    {
        // 不加括号会与前参连成一串（FromString 收到 4 个实参）
        var result = Convert("{{ delimit .Params.x \", \" | resources.FromString \"a.css\" }}");
        Assert.Contains("resources.FromString \"a.css\" (", result, StringComparison.Ordinal);
    }

    [Fact]
    public void printf管道形态参数顺序正确()
    {
        // PaperMod：`X | printf "content=%q"` → printf "content=%q" X
        var result = Convert("{{ .Title | printf \"%s\" }}");
        Assert.Contains("printf \"%s\" page.title", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 反引号原始串转义为双引号串()
    {
        // Go 原始串（反引号）不做转义，内容可以含 `"` 与 `\`；而 Scriban 里反引号不是
        // 字符串定界符 → 原样输出会被结构检查拦下（"引号不平衡"）并**整行回退成 Go 原文**。
        // 语料实测 12 处：narrow 的 `find_re `id="([^"]*)"``、fixit 的 `replace … `"` ""`、
        // yinyang 的 `<img[^>]+src="([^"]+)"`
        Assert.Equal(
            """{{ find_re "id=\"([^\"]*)\"" x }}""",
            Convert("""{{ findRE `id="([^"]*)"` x }}""").Trim());
        // 反斜杠同样要转义：正则的 \s / \n 在 Scriban 串里必须写成 \\s / \\n
        Assert.Equal(
            """{{ replace_re "\\s+width=\"[^\"]*\"" "" page.content }}""",
            Convert("""{{ replaceRE `\s+width="[^"]*"` "" .Content }}""").Trim());
    }

    [Fact]
    public void 管道not折叠为取反()
    {
        // Hugo 的 `X | not` = `not X`（管道值作唯一实参）。此前未折叠 → 产出 0 参 `not`
        // → 条件回退成 `false`，判断块被**静默丢弃**（LoveIt 的
        // `if (urls.Parse $src).Host | not`、FixIt 的 `| and (not $x)`）
        Assert.Equal(
            "{{ if !($x) }}T{{ end }}",
            Convert("{{ if $x | not }}T{{ end }}").Trim());
        // 带第二实参的 `not`（Hugo 下非法：not 只接受 1 参）不走折叠，
        // 条件按"不可转换"**安全降级为 false**（并记诊断）——不猜语义
        Assert.Equal(
            "{{ if false }}T{{ end }}",
            Convert("{{ if $x | not $y }}T{{ end }}").Trim());
    }

    [Fact]
    public void 命名空间调用原样保留()
    {
        // `compare.*`/`collections.*`/`math.*` 在 Flint 引擎里有同名命名空间，
        // 故转换**保留原写法**（MigrationMap 末尾的自映射有意覆盖早先的
        // "命名空间 → 全局"映射）：人工比对更直观，也不踩成员调用的歧义。
        // 引擎侧可用性由 Core 的 `比较函数不再抛Int32装箱异常` 锁定
        //（`compare.Ge 5 (math.add 3 1)` 曾报 "Object must be of type Int32"，
        //  根因是比较函数的 CompareTo 装箱缺陷，与调用形态无关）
        Assert.Equal(
            "{{ if compare.Ge 5 (math.add 3 1) }}T{{ end }}",
            Convert("{{ if compare.Ge 5 (math.add 3 1) }}T{{ end }}").Trim());
    }

    [Fact]
    public void 短代码调用块原样保留()
    {
        // Hugo **只在内容里**支持 `{{< … >}}` / `{{% … %}}`；放 layouts 里 Hugo 自己报
        // `unexpected "<" in command`（v0.166 实测）。此前按动作解析会把定界符悄悄吃掉：
        // `{{< sc x="1" >}}body{{< /sc >}}` → `{{ sc x "1" }}body{{ sc }}`（静默损坏）。
        // 现在整段原样保留 + 一条诊断；且不能吞后续正文（扫描完须退出动作态，
        // 否则 `TAIL` 会被当动作内容切词）
        var result = Convert("{{< sc x=\"1\" >}}body{{< /sc >}}{{% sc %}}md{{% /sc %}}TAIL");
        Assert.Contains("{{< sc x=\"1\" >}}body{{< /sc >}}", result, StringComparison.Ordinal);
        Assert.Contains("{{% sc %}}md{{% /sc %}}", result, StringComparison.Ordinal);
        Assert.Contains("TAIL", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 管道比较方向与Hugo一致()
    {
        // Go 管道把左值追加为**末参**：`1 | gt 2` = gt(2, 1) = true（Hugo v0.166 实测）。
        // 旧实现按 `(左 op 右)` 拼接 → 产出 `(1 > 2)`（false），方向反了。
        // 21 主题语料里没有 `| gt/ge/lt/le` 写法，故这属于潜伏错误（eq/ne 可交换不受影响）
        Assert.Contains("(2 > 1)", Convert("{{ 1 | gt 2 }}"), StringComparison.Ordinal);
        Assert.Contains("(2 < 1)", Convert("{{ 1 | lt 2 }}"), StringComparison.Ordinal);
        Assert.Contains("(2 >= 1)", Convert("{{ 1 | ge 2 }}"), StringComparison.Ordinal);
    }

    [Fact]
    public void 管道逻辑取值方向与Hugo一致()
    {
        // `"A" | or "B"` = or("B", "A") → 首个真值 = "B"；`"A" | and "B"` → 全真取末值 = "A"
        Assert.Contains("(\"B\") ? (\"B\") : (\"A\")", Convert("{{ \"A\" | or \"B\" }}"), StringComparison.Ordinal);
        Assert.Contains("(\"B\") ? (\"A\") : (\"B\")", Convert("{{ \"A\" | and \"B\" }}"), StringComparison.Ordinal);
    }

    [Fact]
    public void 括号内管道不被外层切分()
    {
        // ParsePipeline 曾不跟踪括号深度，`slice "a" (X | default "y") $z`
        // 会从内层 | 断开 → 数组提前闭合（Ananke baseof 的 body_classes 实测）
        var result = Convert("{{ slice \"ma0\" (.Param \"cls\" | default \"d\") .Kind }}");
        Assert.Contains("[\"ma0\"", result, StringComparison.Ordinal);
        Assert.Contains("default", result, StringComparison.Ordinal);
        Assert.Contains("page.kind", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 块名含连字符归一为合法标识符()
    {
        // Stack：`block "body-class"` → Scriban 变量名不接受连字符
        //（曾产出 `__def_body-class` → "Unsupported target expression for assignment"）
        var result = Convert("{{ block \"body-class\" }}x{{ end }}");
        Assert.Contains("__def_body_class", result, StringComparison.Ordinal);
        Assert.DoesNotContain("__def_body-class", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 局部变量接收者不被换根()
    {
        // `$scratch.Add` 的 head 是局部变量本身（曾产出 `pagescratch.add`）；
        // 裸 `$.GetTerms` 仍需换到 page 根
        Assert.Contains("$scratch.add", Convert("{{ $scratch.Add \"k\" 1 }}"), StringComparison.Ordinal);
        Assert.Contains("page.get_terms", Convert("{{ $.GetTerms \"tags\" }}"), StringComparison.Ordinal);
    }

    [Fact]
    public void 局部变量成员访问用nil安全()
    {
        // `$posts.paginate` 在变量为 nil 时 Hugo 返回 nil、Scriban 抛异常
        Assert.Contains("$posts?.paginate", Convert("{{ $posts.paginate }}"), StringComparison.Ordinal);
    }

    [Fact]
    public void partial调用点产出带前缀路径()
    {
        // Hugo 的 partial 只在 partials/_partials 目录查找，不会命中原目录同名页面模板。
        // 裸名会让 `partial "404.html"` 解析到 layouts/404.html（自身）→ 自递归
        //（hugo-coder 实测 "Exceeding number of recursive depth limit"）
        var result = Convert("{{ partial \"404.html\" . }}");
        Assert.Contains("_partials/404", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 命名模板的template调用改走partial()
    {
        // Go 的 `template "X" CTX` 调用 define 出来的命名模板；
        // 简单名 → 提取为 _partials/X.html，调用点走 partial 路径。
        // **上下文必须一起传**：此前解析器只取名字、把 CTX 整体丢弃
        // （Pipeline 恒为 null），转换器里"传上下文"的分支成了死代码，
        // 被调模板里的 `.Field` 落到外层 page 上
        //（techdoc 的 pagination.html 实测 68 处）
        var result = Convert("{{ template \"integrity\" $styles }}");
        Assert.Contains("_partials/integrity", result, StringComparison.Ordinal);
        Assert.Contains("$styles", result, StringComparison.Ordinal);
    }

    [Fact]
    public void template调用的dict上下文完整保留()
    {
        // CTX 是 (dict …) 时，按"位置"切分（名字之后的全部 token）——
        // 若按 token 类型过滤会把 dict 的键字符串一并删掉，dict 变成奇数参数，
        // 整条表达式判 Unsupported 并产出 `false`
        var result = Convert(
            "{{ template \"pagination\" (dict \"menu\" .Site.Home \"currentnode\" .) }}");

        Assert.Contains("_partials/pagination", result, StringComparison.Ordinal);
        Assert.Contains("dict \"menu\"", result, StringComparison.Ordinal);
        Assert.Contains("\"currentnode\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain(" false", result, StringComparison.Ordinal);
    }

    [Fact]
    public void template调用无上下文时仍走include分支()
    {
        // 无 CTX 时保持原行为（include，不带上下文）
        var result = Convert("{{ template \"head\" }}");
        Assert.Contains("include \"_partials/head\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 内置模板的template调用保持include()
    {
        // `_internal/...` 是 Hugo embedded template（无物理文件），走 include 命中引擎内置表
        var result = Convert("{{ template \"_internal/opengraph.html\" . }}");
        Assert.Contains("include \"_internal/opengraph.html\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 非管道default重排参数顺序()
    {
        // Hugo 的 default 是 `default DEFAULT GIVEN`，而 Flint 侧按 Scriban 语义取首参
        //（管道注入位），故非管道形态需重排为 (GIVEN, DEFAULT)。
        // 源模板里的 `page` 是**全局变量**（Hugo 语义），迁移后写作引擎的 __page
        //（点号形态 `.` 才映射为 page 关键字），故断言用 __page
        var nonPiped = Convert("{{ default \"x\" page.title }}");
        Assert.Contains("default __page?.title \"x\"", nonPiped, StringComparison.Ordinal);
        // 管道形态已是 (左值, 默认值)，不得重排
        var piped = Convert("{{ page.title | default \"x\" }}");
        Assert.Contains("default \"x\"", piped, StringComparison.Ordinal);
        Assert.DoesNotContain("default page.title", piped, StringComparison.Ordinal);
    }

    [Fact]
    public void 数据路径段名不被snake化()
    {
        // Stack：`hugo.Data.external.PhotoSwipe.Style` 的键是作者定义的驼峰
        var result = Convert("{{ hugo.Data.external.PhotoSwipe.Style }}");
        Assert.Contains("site?.data?.external?.PhotoSwipe?.Style", result, StringComparison.Ordinal);
    }

    [Fact]
    public void 文件内同名命名模板提取改名不覆盖自身()
    {
        // Hugo 允许在 `_partials/pagination.html` 里定义 `{{ define "pagination" }}`。
        // 提取时若不改名，提取出的 define 体会落到**同一个路径**，把外层内容整体覆盖
        //（techdoc 实测：外层 nav 与 prev/next 全丢，只剩 define 体，
        //  68 处 "Cannot get the member $currentNode.scratch for a null object"）
        const string source = """
            {{- $currentNode := . -}}
            <nav>{{ template "pagination" (dict "menu" .Site.Home) }}</nav>
            {{- define "pagination" -}}X{{- end -}}
            """;

        var names = new HashSet<string>(StringComparer.Ordinal) { "pagination" };
        var (remaining, partials) = InlinePartialExtractor.Extract(
            source, names, "layouts/_partials/pagination.html");

        var rel = Assert.Single(partials).RelativePath;
        Assert.Equal("_partials/pagination__named.html", rel);
        // 外层内容仍在（未被 define 体覆盖）
        Assert.Contains("<nav>", remaining, StringComparison.Ordinal);
    }

    [Fact]
    public void 非同名命名模板提取不改名()
    {
        // 只有"提取路径 == 所在文件"才加后缀，其余保持原名
        const string source = """
            {{- define "integrity" -}}X{{- end -}}
            """;

        var names = new HashSet<string>(StringComparer.Ordinal) { "integrity" };
        var (_, partials) = InlinePartialExtractor.Extract(
            source, names, "layouts/_partials/other.html");

        Assert.Equal("_partials/integrity.html", Assert.Single(partials).RelativePath);
    }
}
