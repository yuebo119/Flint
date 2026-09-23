// Flint 静态站点生成器
// hugo:vars 虚拟导入（css.Sass 的 vars 选项）单元测试。
//
// Hugo 的 css.Sass 支持把变量字典经 `vars` 选项传给 Dart Sass，SCSS 侧用
// `@forward "hugo:vars"` / `@forward "hugo:vars/internal"` 取用。Flint 调外部
// sass CLI（无自定义 importer，且 `hugo:` 前缀含冒号不是合法 Windows 文件名），
// 故在源码镜像上做文本替换——这里覆盖替换、值格式化与汇总逻辑。

using Flint.Core.Templates;
using Scriban.Runtime;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>hugo:vars 替换与值格式化（BuiltinTemplateFunctions 的 internal 助手）</summary>
public class HugoVarsTests
{
    private static ScriptObject Vars(params (string Key, object? Value)[] entries)
    {
        var so = new ScriptObject();
        foreach (var (k, v) in entries)
        {
            so[k] = v;
        }
        return so;
    }

    [Fact]
    public void 替换_forward与use的hugo_vars导入()
    {
        var vars = BuiltinTemplateFunctions.CollectHugoVars(
            Vars(("header_height", "60px"), ("global_font_family", "Noto Sans SC")), null);

        var src = "@forward \"hugo:vars\";\n$prefix: fi- !default;\n";
        var out1 = BuiltinTemplateFunctions.RewriteHugoVarsImports(src, vars);
        Assert.Contains("$header_height: 60px;", out1);
        Assert.Contains("$global_font_family: \"Noto Sans SC\";", out1);
        Assert.DoesNotContain("hugo:vars", out1);

        var src2 = "@use \"hugo:vars\";\n";
        Assert.Contains("$header_height: 60px;",
            BuiltinTemplateFunctions.RewriteHugoVarsImports(src2, vars));
    }

    [Fact]
    public void 替换_hugo_vars子映射导入()
    {
        var vars = BuiltinTemplateFunctions.CollectHugoVars(
            Vars(("internal", Vars(("base_url", "/"), ("logo_img", "/logo.svg")))), null);

        var src = "@forward \"hugo:vars/internal\";\n";
        var result = BuiltinTemplateFunctions.RewriteHugoVarsImports(src, vars);
        Assert.Contains("$base_url: \"/\";", result);
        Assert.Contains("$logo_img: \"/logo.svg\";", result);
        // 顶层标量命名空间不该泄漏到子映射导入里
        Assert.DoesNotContain("$header_height", result);
    }

    [Fact]
    public void varsInternal选项合并进internal命名空间()
    {
        var vars = BuiltinTemplateFunctions.CollectHugoVars(
            null, Vars(("loading_img", "/loading.svg")));
        var result = BuiltinTemplateFunctions.RewriteHugoVarsImports(
            "@forward \"hugo:vars/internal\";", vars);
        Assert.Contains("$loading_img: \"/loading.svg\";", result);
    }

    [Fact]
    public void 无数据的导入替换为空注释而非报错()
    {
        var vars = BuiltinTemplateFunctions.CollectHugoVars(Vars(("a", "1")), null);
        var result = BuiltinTemplateFunctions.RewriteHugoVarsImports("@forward \"hugo:vars/internal\";", vars);
        Assert.DoesNotContain("hugo:", result);
        Assert.Contains("flint: vars", result);
    }

    [Theory]
    // hex / CSS 函数 / 数字+单位 / 裸关键字原样；其余引号化（对齐 Hugo isTypedCSSValue）
    [InlineData("#ff0000", "#ff0000")]
    [InlineData("rgba(0, 0, 0, 0.5)", "rgba(0, 0, 0, 0.5)")]
    [InlineData("0.875em", "0.875em")]
    [InlineData("60px", "60px")]
    [InlineData("center", "center")]
    [InlineData("Noto Sans SC", "\"Noto Sans SC\"")]
    [InlineData("/logo.svg", "\"/logo.svg\"")]
    [InlineData("var(--x)", "var(--x)")]
    public void 值格式化_按Hugo规则(string input, string expected)
    {
        Assert.Equal(expected, BuiltinTemplateFunctions.FormatSassValue(input));
    }

    [Fact]
    public void 缓存摘要_不同vars不同摘要同vars同摘要()
    {
        var a = BuiltinTemplateFunctions.VarsDigest(Vars(("x", "1")), null);
        var b = BuiltinTemplateFunctions.VarsDigest(Vars(("x", "2")), null);
        var c = BuiltinTemplateFunctions.VarsDigest(Vars(("x", "1")), null);
        Assert.NotEqual(a, b);
        Assert.Equal(a, c);
        Assert.Equal("novars", BuiltinTemplateFunctions.VarsDigest(null, null));
    }
}
