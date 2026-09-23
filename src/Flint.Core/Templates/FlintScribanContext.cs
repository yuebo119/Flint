// Flint 静态站点生成器
// Scriban 渲染上下文（Hugo 真值语义）
//
// 背景：Scriban 的真值判定与 Go 模板不同——数字 0、空串、空集合在 Scriban 里都算
// **真**（探针实测：`{{ if 0 }}` / `{{ if "" }}` / `{{ if [] }}` 全为真），而 Hugo
// 按 Go 模板语义全为假。主题里 `{{ if len X }}`（"非空才渲染"）是高频写法，
// 沿用 Scriban 语义会让空集合的分支照样进入（FixIt 的
// `{{ if len $errors }}` 因此误报 errorf，构建失败）。
//
// 修法：覆盖 `TemplateContext.ToBool`（Scriban 文档明确标注
// "Called when evaluating a value to a boolean. Can be overriden"），按 Go 模板的
// IsTruthful 规则判定：nil/false/0/空串/空集合为假，其余为真。
//
// 两个必须显式豁免为真的类型（否则会被"空集合为假"误伤）：
//   · 页面对象 LazyPageObject——成员惰性装配，按集合判空会把页面判成假；
//     Hugo 里 `.Page` 是结构体指针，恒真
//   · 引擎内部投影 IFlintNonDataObject（store / 内容片段 / 词条映射 / 分页器）
//     ——对模板是不透明标量，Hugo 侧对应结构体，同样恒真

namespace Flint.Core.Templates;

/// <summary>
/// Flint 的 Scriban 上下文：<c>if</c>/<c>with</c>/<c>and</c>/<c>or</c>/<c>not</c>
/// 使用 Hugo（Go 模板）真值语义
/// </summary>
internal sealed class FlintScribanContext : Scriban.TemplateContext
{
    /// <inheritdoc />
    public override bool ToBool(Scriban.Parsing.SourceSpan span, object? value) => IsHugoTruthy(value);

    /// <summary>
    /// 值 → 文本（Hugo/Go 的 <c>fmt</c> 默认格式）：
    /// <list type="bullet">
    /// <item>时间 → Go 的 <c>time.Time.String()</c>（<c>2026-01-15 00:00:00 +0000 UTC</c>）——
    /// github-style 的 <c>{{ time .Date }}</c>、techdoc 的日期行实测：.NET 默认输出
    /// <c>01/15/2026 00:00:00 +00:00</c>，与 Hugo 的 meta 内容整段不同</item>
    /// <item>字典 → Go 的 <c>map[k1:v1 k2:v2]</c>（**键排序**，fmt 自 1.12 起对 map 排序）——
    /// hugo-coder/hugo-paper 打印 <c>{name, link}</c> 参数时实测：Scriban 默认给
    /// <c>{name: "Tester", link: "..."}</c>，Hugo 给 <c>map[link:… name:Tester]</c></item>
    /// <item>列表 → Go 的 <c>[a b c]</c></item>
    /// </list>
    /// 页面对象与引擎内部投影（<see cref="IFlintNonDataObject"/>、页面集合）**不套用**：
    /// 它们在 Hugo 侧是结构体，Hugo 打印成 <c>Page(…)</c> 一类；沿用 Scriban 的表示即可，
    /// 避免把页面集合印成 map。
    /// </summary>
    public override string ObjectToString(object? value, bool nested) => value switch
    {
        DateTimeOffset dto => GoTimeString(dto),
        DateTime dt => GoTimeString(new DateTimeOffset(dt)),
        // 语言对象（Hugo .Site.Language）：String() 是语言码而非 map 转储
        // （hugo-coder baseof 的 `<html lang="{{ site.language }}">` 实测）
        ILanguageCode lang => lang.LanguageCodeValue,
        Scriban.Runtime.ScriptObject obj
            when obj is not IFlintNonDataObject
                && obj is not ScribanTemplateRenderer.LazyPageObject
                && obj is not IList<Scriban.Runtime.ScriptObject> => GoMapString(obj),
        System.Collections.IList list when value is not string && value is not Scriban.Runtime.ScriptObject
            => GoSliceString(list),
        _ => base.ObjectToString(value, nested) ?? ""
    };

    /// <summary>Go 的 time.String()：<c>2006-01-02 15:04:05[.999999999] -0700 MST</c></summary>
    private static string GoTimeString(DateTimeOffset dto)
    {
        var sb = new System.Text.StringBuilder(32);
        sb.Append(dto.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        var fraction = dto.Ticks % TimeSpan.TicksPerSecond;
        if (fraction != 0)
        {
            // Go 去掉尾随零（.999999999 的语义）
            var text = fraction.ToString("D7", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
            sb.Append('.').Append(text);
        }

        sb.Append(' ').Append(dto.Offset == TimeSpan.Zero
            ? "+0000"
            : (dto.Offset < TimeSpan.Zero ? "-" : "+") +
              dto.Offset.Duration().ToString("hhmm", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(' ').Append(dto.Offset == TimeSpan.Zero
            ? "UTC"
            : (dto.Offset < TimeSpan.Zero ? "-" : "+") +
              dto.Offset.Duration().ToString("hhmm", System.Globalization.CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>Go 的 map 打印：<c>map[k1:v1 k2:v2]</c>（键按序）</summary>
    private static string GoMapString(Scriban.Runtime.ScriptObject obj)
    {
        var entries = new List<string>(obj.Count);
        foreach (var key in obj.Keys)
        {
            obj.TryGetValue(null, default, key, out var item);
            entries.Add(key + ":" + GoValueString(item));
        }

        entries.Sort(StringComparer.Ordinal);
        return "map[" + string.Join(' ', entries) + "]";
    }

    /// <summary>Go 的切片打印：<c>[a b c]</c></summary>
    private static string GoSliceString(System.Collections.IList list)
    {
        var items = new List<string>(list.Count);
        foreach (var item in list)
        {
            items.Add(GoValueString(item));
        }

        return "[" + string.Join(' ', items) + "]";
    }

    /// <summary>Go 的 <c>%v</c> 单值：nil → <c>&lt;nil&gt;</c>，字符串原样，容器递归</summary>
    private static string GoValueString(object? value) => value switch
    {
        null => "<nil>",
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset dto => GoTimeString(dto),
        DateTime dt => GoTimeString(new DateTimeOffset(dt)),
        Scriban.Runtime.ScriptObject obj
            when obj is not IFlintNonDataObject
                && obj is not ScribanTemplateRenderer.LazyPageObject
                && obj is not IList<Scriban.Runtime.ScriptObject> => GoMapString(obj),
        System.Collections.IList list when value is not Scriban.Runtime.ScriptObject => GoSliceString(list),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };

    /// <summary>Hugo 真值（Go 模板 <c>IsTruthful</c>）：0 / "" / 空集合为假，其余为真</summary>
    internal static bool IsHugoTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        sbyte v => v != 0,
        byte v => v != 0,
        short v => v != 0,
        ushort v => v != 0,
        int v => v != 0,
        uint v => v != 0,
        long v => v != 0,
        ulong v => v != 0,
        float v => v != 0,
        double v => v != 0,
        decimal v => v != 0,
        // 页面对象：Hugo 里是结构体指针 → 恒真（成员惰性，不能按集合判空）
        ScribanTemplateRenderer.LazyPageObject => true,
        // 引擎内部投影（store/内容片段/词条映射/分页器）→ 不透明标量，恒真
        IFlintNonDataObject => true,
        // 页面集合（LazyPageList 只实现泛型 IList<ScriptObject>，非泛型 IList 会漏）
        IList<Scriban.Runtime.ScriptObject> pageList => pageList.Count > 0,
        System.Collections.IDictionary dict => dict.Count > 0,
        System.Collections.ICollection collection => collection.Count > 0,
        System.Collections.IEnumerable sequence => sequence.Cast<object?>().Any(),
        _ => true
    };
}
