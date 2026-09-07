// Flint 静态站点生成器
// RenderDependencyTracker——渲染期依赖收集（T4.1，对齐 Hugo trackDependencies 的 Flint 折中）

using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// 渲染期依赖收集：单个页面渲染过程中实际接触的依赖键集合。
/// 键的形态：模板/部分的实际物理路径；站点数据访问为 <c>data:site.&lt;member&gt;</c>。
/// 存放于 Scriban <see cref="TemplateContext.Tags"/>（官方 per-render 挂载点），随上下文生命周期结束。
/// 拦截点均为 Scriban 公开 API（ScriptObject.TryGetValue 虚方法覆写 + ITemplateLoader），
/// 不依赖 fork 模板引擎（清单"明确不做"边界内）。
/// </summary>
public static class RenderDependencyTracker
{
    /// <summary>Tags 键：当前渲染的页面源文件路径</summary>
    public const string SourceKey = "__flint_source";

    /// <summary>Tags 键：依赖键集合（HashSet&lt;string&gt;）</summary>
    public const string DepsKey = "__flint_deps";

    /// <summary>站点数据依赖键前缀</summary>
    public const string DataPrefix = "data:";

    /// <summary>
    /// 初始化渲染上下文的依赖收集（CreateScribanContext 时调用）
    /// </summary>
    public static void Initialize(TemplateContext context, string? pageSourcePath)
    {
        context.Tags[SourceKey] = pageSourcePath ?? "";
        context.Tags[DepsKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>记录一个依赖键（include 路径 / data 键）</summary>
    public static void Track(TemplateContext context, string key)
    {
        if (context.Tags.TryGetValue(DepsKey, out var value) && value is HashSet<string> deps)
        {
            deps.Add(key);
        }
    }

    /// <summary>提取本次渲染收集到的依赖键；无快照时返回空集</summary>
    public static IReadOnlySet<string> Extract(TemplateContext context)
    {
        return context.Tags.TryGetValue(DepsKey, out var value) && value is HashSet<string> deps
            ? new HashSet<string>(deps, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 带依赖记录的站点对象：模板访问 site.* 成员时记录 <c>data:site.&lt;member&gt;</c>。
/// 仅挂站点对象——页面自身的文件是天然依赖，访问 page.* 不需要记录
/// </summary>
internal sealed class DependencyTrackingScriptObject : ScriptObject
{
    public override bool TryGetValue(TemplateContext? context, SourceSpan span, string member, out object? value)
    {
        RenderDependencyTracker.Track(context!, RenderDependencyTracker.DataPrefix + "site." + member);
        return base.TryGetValue(context, span, member, out value);
    }
}
