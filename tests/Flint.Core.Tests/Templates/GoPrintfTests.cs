// Flint 静态站点生成器
// Go fmt 语义的 printf 回归（期望值全部来自 Hugo v0.166 探针）
//
// 探针模板（Hugo 侧 layouts/index.html 逐条渲染）：
//   v-slice=[{{ printf "%v" (slice 1 "a" true) }}] → [[1 a true]]
//   v-dict=[{{ printf "%v" (dict "a" 1 "b" "x") }}] → [map[a:1 b:x]]
//   q-str=[{{ printf "%q" "a\"b" }}]                → ["a\"b"]
//   T-slice=[{{ printf "%T" (slice 1) }}]           → [[]int]
// 完整对照见 docs/HUGO-COMPAT-MATRIX.md 第二节 M。
//
// 与 Go 的**唯一有意差异**：`%s`/`%d` 对数值族做归一（Go 的 %d 遇 float64 会产
// `%!d(float64=5.7)`），因为同一值在 Hugo 与 Flint 内部类型不同，严格按类型名
// 会让标记无谓增多；非数值仍产 `%!d(string=5)` 标记（与 Go 同）。

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>Go fmt 语义的 printf</summary>
public sealed class GoPrintfTests
{
    private static string Fmt(string format, params object?[] args) => GoPrintf.Format(format, args);

    [Fact]
    public void 百分号v的默认渲染与Go一致()
    {
        Assert.Equal("5", Fmt("%v", 5));
        Assert.Equal("s", Fmt("%v", "s"));
        Assert.Equal("true", Fmt("%v", true));
        Assert.Equal("<nil>", Fmt("%v", (object?)null));
        Assert.Equal("1.5", Fmt("%v", 1.5));
        // 切片：Go 的 [1 a true]
        Assert.Equal("[1 a true]", Fmt("%v", (object?)new object?[] { 1, "a", true }));
        // 映射：Go 的 map[a:1 b:x]（键排序）
        var dict = new Dictionary<string, object?> { ["b"] = "x", ["a"] = 1 };
        Assert.Equal("map[a:1 b:x]", Fmt("%v", dict));
        Assert.Equal("map[a:[1 2]]", Fmt("%v", new Dictionary<string, object?> { ["a"] = new object?[] { 1, 2 } }));
    }

    [Fact]
    public void 井号v打Go语法表示()
    {
        // Hugo 探针：printf "%#v" (slice 1 2) → []int{1, 2}
        Assert.Equal("[]int{1, 2}", Fmt("%#v", (object?)new object?[] { 1, 2 }));
        Assert.Equal("[]string{\"a\"}", Fmt("%#v", (object?)new object?[] { "a" }));
    }

    [Fact]
    public void 百分号q按Go转义引号()
    {
        // Hugo 探针：%q 是"带转义的引号串"，不是两侧加引号
        Assert.Equal("\"abc\"", Fmt("%q", "abc"));
        Assert.Equal("\"a\\\"b\"", Fmt("%q", "a\"b"));
        Assert.Equal("\"a\\\\b\"", Fmt("%q", "a\\b"));
        // 数值按 rune 字面量（Go：printf "%q" 5 → '\x05'）
        Assert.Equal("'\\x05'", Fmt("%q", 5));
        // 切片逐元素引号化（Go：printf "%q" (slice "x") → ["x"]）
        Assert.Equal("[\"x\"]", Fmt("%q", (object?)new object?[] { "x" }));
    }

    [Fact]
    public void 类型不符产Go的感叹号标记()
    {
        // 与 Go 完全一致的两条（非数值）
        Assert.Equal("%!d(string=5)", Fmt("%d", "5"));
        Assert.Equal("%!t(int=1)", Fmt("%t", 1));
        // 有意差异：数值族归一（Go 会打 %!s(int=5) / %!d(float64=5.7)），见文件头说明
        Assert.Equal("5", Fmt("%s", 5));
        Assert.Equal("5", Fmt("%d", 5.7));
    }

    [Fact]
    public void 宽度精度标志与Go一致()
    {
        Assert.Equal("05", Fmt("%02d", 5));
        Assert.Equal("[   ab]", Fmt("[%5s]", "ab"));
        Assert.Equal("[ab   ]", Fmt("[%-5s]", "ab"));
        Assert.Equal("3.14", Fmt("%.2f", 3.14159));
        Assert.Equal("3.141593", Fmt("%f", 3.141593));
        // Go 的 %g 指数用小写 e（Hugo 探针：printf "%g" 0.0000123 → 1.23e-05）
        Assert.Equal("1.23e-05", Fmt("%g", 0.0000123));
        Assert.Equal("abc", Fmt("%s", "abcdef").Substring(0, 3));
        Assert.Equal("abc", Fmt("%.3s", "abcdef"));
    }

    [Fact]
    public void nil按空串渲染而非Go的标记()
    {
        // 有意偏离 Go：Go 对 `%s` 遇 nil 打 `%!s(<nil>)`，而模板里"值缺失"是常态
        // （Flint 数据面可能比 Hugo 少），标记会被写进 class/属性/URL
        //（实测：ananke 的 `printf "page-%s" .ContentBaseName` 曾产出
        //  `class="page-%!s(<nil>=<nil>)"` → CSS class 污染、门禁④结构分下跌）。
        // `%v`/`%T` 与 Go 一致（Go 本身把 nil 当值渲染为 <nil>）
        Assert.Equal("", Fmt("%s", (object?)null));
        Assert.Equal("", Fmt("%d", (object?)null));
        Assert.Equal("", Fmt("%q", (object?)null));
        Assert.Equal("<nil>", Fmt("%v", (object?)null));
        Assert.Equal("<nil>", Fmt("%T", (object?)null));
    }

    [Fact]
    public void 百分号T打Go类型名()
    {
        Assert.Equal("int", Fmt("%T", 5));
        Assert.Equal("int64", Fmt("%T", 5L));
        Assert.Equal("float64", Fmt("%T", 1.5));
        Assert.Equal("string", Fmt("%T", "s"));
        Assert.Equal("bool", Fmt("%T", true));
        Assert.Equal("[]int", Fmt("%T", (object?)new object?[] { 1 }));
        Assert.Equal("[]string", Fmt("%T", (object?)new object?[] { "a" }));
        Assert.Equal("map[string]interface {}", Fmt("%T", new Dictionary<string, object?>()));
    }

    [Fact]
    public void 转义与多余实参与Go一致()
    {
        Assert.Equal("100%", Fmt("100%%"));
        Assert.Equal("%!v(MISSING)", Fmt("%v"));
        Assert.Equal("5%!(EXTRA string=x)", Fmt("%v", 5, "x"));
        // 无 % 的格式串不受影响（主题里的"100% 完成"类文本）
        Assert.Equal("hello", Fmt("hello"));
    }

    [Fact]
    public void 常用主题写法端到端()
    {
        // 语料高频：`printf "icons/%s.svg" .`、`printf "%v" $x`、`printf "%q" $s`
        Assert.Equal("icons/star.svg", Fmt("icons/%s.svg", "star"));
        Assert.Equal("commit 12ab by A", Fmt("commit %s by %s", "12ab", "A"));
        Assert.Equal("0.5K", Fmt("%.1fK", 500.0 / 1000));
    }
}
