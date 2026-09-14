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
