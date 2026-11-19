// Flint 静态站点生成器
// Hugo 0.170.1 对齐批次的杂项锁定测试：
// strings.Count/ContainsNonSpace/FirstLower、time.Format/Duration/Now、
// collections.NewScratch、hugo.Version 对齐 0.170.1

using Flint.Core.Templates;
using Scriban;
using Scriban.Runtime;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class Hugo0170CompatibilityTests
{
    private static string Render(string template)
    {
        var globals = new ScriptObject();
        new BuiltinTemplateFunctions("https://example.com").RegisterFunctions(globals);
        var ctx = new TemplateContext();
        ctx.PushGlobal(globals);
        var parsed = Template.Parse(template);
        Assert.False(parsed.HasErrors, string.Join("; ", parsed.Messages));
        return parsed.Render(ctx);
    }

    // ---- strings 三补（0.170.1 对齐）----

    [Fact]
    public void strings_Count_统计子串次数()
    {
        Assert.Equal("2", Render("{{ strings.Count \"ss\" \"assessment\" }}"));
        // Hugo 参数序：子串在前
        Assert.Equal("2", Render("{{ strings.Count \"na\" \"banana\" }}"));
        Assert.Equal("0", Render("{{ strings.Count \"z\" \"banana\" }}"));
    }

    [Fact]
    public void strings_ContainsNonSpace_判定非空白()
    {
        Assert.Equal("true", Render("{{ strings.ContainsNonSpace \"a b\" }}"));
        Assert.Equal("false", Render("{{ strings.ContainsNonSpace \" \\t\\n \" }}"));
        Assert.Equal("false", Render("{{ strings.ContainsNonSpace \"\" }}"));
    }

    [Fact]
    public void strings_FirstLower_首字母小写()
    {
        Assert.Equal("hello", Render("{{ strings.FirstLower \"Hello\" }}"));
        Assert.Equal("hELLO", Render("{{ strings.FirstLower \"HELLO\" }}"));
        // FirstUpper 镜像回归
        Assert.Equal("Hello", Render("{{ strings.FirstUpper \"hello\" }}"));
    }

    // ---- time：Scriban 引擎限制说明 ----
    // Hugo 的 time.X 命名空间形态（Format/Duration/Now）在 Scriban 引擎层不可直呼
    //（IScriptCustomFunction 对象被求值劫持为函数调用，见 TemplateEnvironmentNamespace
    // 注释的探针记录），兼容由迁移器承担：time.Format → date.to_string（FormatHugoDate
    // 共用）、time.Duration → duration。此处锁定全局等价物语义。

    [Fact]
    public void date_format_接受字符串日期()
    {
        // time.Format 的全局等价物（迁移器产物 date.to_string 的同源实现，
        // 渲染器层装配的 date 对象由 HugoCompatSemanticsTests 覆盖）
        Assert.Equal("2024", Render("{{ date_format \"2024-01-15\" \"yyyy\" }}"));
    }

    [Fact]
    public void duration_Go_串形态()
    {
        // "2h45m" 全序列求和（TimeSpan.TryParse 解析不出，Go 形态兜底）；
        // TimeSpan 直接打印 = CLR ToString()
        Assert.Equal("02:45:00", Render("{{ duration \"2h45m\" }}"));
        Assert.Equal("-01:30:00", Render("{{ duration \"-1.5h\" }}"));
        // 标准形态仍走 TryParse
        Assert.Equal("00:00:30", Render("{{ duration \"00:00:30\" }}"));
        // 非法串回退 Zero
        Assert.Equal("00:00:00", Render("{{ duration \"nope\" }}"));
    }

    // ---- collections / resources / hugo ----

    [Fact]
    public void collections_NewScratch_命名空间形态()
    {
        var o = Render(
            "{{ s = collections.NewScratch }}{{ s.Set \"k\" 7 }}{{ s.Get \"k\" }}");
        Assert.Equal("7", o);
    }

    [Fact]
    public void hugo_Version_对齐_01701()
    {
        Assert.Equal("0.170.1", Render("{{ hugo.Version }}"));
        Assert.Contains("Flint 0.170.1", Render("{{ hugo.Generator }}"));
    }
}
