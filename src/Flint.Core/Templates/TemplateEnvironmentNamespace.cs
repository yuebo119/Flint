// Flint 静态站点生成器
// hugo.* 环境对象与 time.* 补充（Hugo 0.146+ 命名空间）
//
// hugo.* 是常量对象（Version/Environment/IsProduction 等），零成本；
// 模板据此做环境分支（Ananke/PaperMod 实测 30 次调用，是命名空间里
// 用量最大的一类）。

using System.Globalization;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// 模板环境信息（hugo.* 对象的取值来源）。
/// 由构建入口按运行配置构造；未提供时用 Default（生产语义）。
/// </summary>
public sealed record TemplateEnvironmentInfo
{
    /// <summary>Hugo 兼容版本号（主题常做 min_version 校验）</summary>
    public string Version { get; init; } = "0.166.0";

    /// <summary>环境名：production / development</summary>
    public string Environment { get; init; } = "production";

    /// <summary>是否 extended 构建（主题据此决定是否可用 Sass）</summary>
    public bool IsExtended { get; init; } = true;

    /// <summary>是否多语言站点</summary>
    public bool IsMultilingual { get; init; }

    /// <summary>是否多主机部署</summary>
    public bool IsMultihost { get; init; }

    /// <summary>工作目录</summary>
    public string WorkingDir { get; init; } = "";

    /// <summary>站点数据（hugo.Data）</summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();

    /// <summary>默认实例（生产环境语义）</summary>
    public static TemplateEnvironmentInfo Default { get; } = new();
}

public sealed partial class BuiltinTemplateFunctions
{
    /// <summary>
    /// hugo.Store 的实例（Hugo 语义：全构建共享的 Scratch）。
    /// 每个 BuiltinTemplateFunctions 一个（构建器每次构建新建），
    /// 因此构建之间互不串味，构建内所有模板共用
    /// </summary>
    private readonly PageStoreObject _hugoStore = new();

    /// <summary>
    /// 注册 hugo.* 与 time.* 命名空间。
    /// hugo.* 的值取自 <see cref="TemplateEnvironmentInfo"/>（构建入口注入）。
    /// </summary>
    internal void RegisterEnvironmentNamespace(ScriptObject root)
    {
        var env = _environment;
        var isProduction = env.Environment.Equals("production", StringComparison.OrdinalIgnoreCase);
        var isDevelopment = env.Environment.Equals("development", StringComparison.OrdinalIgnoreCase);

        var hugo = new ScriptObject
        {
            ["Version"] = env.Version,
            ["version"] = env.Version,
            ["Environment"] = env.Environment,
            ["environment"] = env.Environment,
            ["IsExtended"] = env.IsExtended,
            ["is_extended"] = env.IsExtended,
            ["IsProduction"] = isProduction,
            ["is_production"] = isProduction,
            ["IsDevelopment"] = isDevelopment,
            ["is_development"] = isDevelopment,
            ["IsServer"] = false,
            ["is_server"] = false,
            ["IsMultilingual"] = env.IsMultilingual,
            ["is_multilingual"] = env.IsMultilingual,
            ["IsMultihost"] = env.IsMultihost,
            ["is_multihost"] = env.IsMultihost,
            ["WorkingDir"] = env.WorkingDir,
            ["working_dir"] = env.WorkingDir,
            // hugo.Generator：Hugo 返回的是**整段 meta 标签**（不是版本串）——
            // 主题写 `{{ hugo.Generator }}` 直接输出 `<meta name="generator" content="Hugo x.y">`。
            // 此前只返回版本串 → 审计里 100 页缺 meta[generator]。
            // 品牌名用 Flint（如实标识，不冒充 Hugo —— 值与 Hugo 不同属有意差异）
            ["Generator"] = $"<meta name=\"generator\" content=\"Flint {env.Version}\">",
            ["generator"] = $"<meta name=\"generator\" content=\"Flint {env.Version}\">",
            ["BuildDate"] = DateTimeOffset.UtcNow,
            ["build_date"] = DateTimeOffset.UtcNow,
            ["CommitHash"] = "",
            ["commit_hash"] = "",
            ["GoVersion"] = "",
            ["go_version"] = "",
            ["Data"] = env.Data,
            ["data"] = env.Data,
            // hugo.Store（Hugo 的全局 Scratch：Set/Get/Add/SetInMap/…）——
            // 空 ScriptObject 曾让 `hugo.Store.Set "k" v` 报 "function not found"，
            // 模板渲染随之整体失败（FixIt 的 _partials/init/index.html 实测 6 处）。
            // 每次注册共用同一实例，语义与 Hugo 的"构建内全局暂存"一致
            ["Store"] = _hugoStore,
            ["store"] = _hugoStore,
            // hugo.Context：Hugo v0.146+ 的标记渲染上下文全局，render hook 用它做
            // 作用域判定（FixIt 各 render-*.html 的 `ne hugo.Context.MarkupScope "home"`）。
            // 缺它时报 "Cannot get the member hugo.Context.MarkupScope for a null object"
            ["Context"] = new ScriptObject
            {
                ["MarkupScope"] = "page",
                ["markup_scope"] = "page"
            },
            // hugo.Sites（Hugo 的站点集合）：单语言站点即"只含本站"，
            // `.Default`/`.First`/`.Last`/`index 0` 都指本站——站点对象渲染期才装配，
            // 且 hugo 对象在多渲染间共享（不能就地改写），故成员在访问时按上下文解析
            ["Sites"] = new HugoSitesObject(),
            ["sites"] = new HugoSitesObject()
        };
        root.TrySetValue(null, default, "hugo", hugo, readOnly: true);

        // time.*：Scriban 内置 time 对象已提供 now/format 等；
        // 补 Hugo 的 AsTime / In / ParseDuration 别名（不覆盖内置成员）
        if (root.TryGetValue(null, default, "time", out var timeValue) && timeValue is ScriptObject timeObj)
        {
            if (!timeObj.ContainsKey("AsTime"))
            {
                timeObj.TrySetValue(null, default, "AsTime", (object? v) => v, readOnly: true);
            }
            if (!timeObj.ContainsKey("In"))
            {
                timeObj.TrySetValue(null, default, "In", (object? v, string? tz) => v, readOnly: true);
            }
            if (!timeObj.ContainsKey("ParseDuration"))
            {
                timeObj.TrySetValue(null, default, "ParseDuration", (string? s) => s ?? "", readOnly: true);
            }
        }
    }
}

/// <summary>
/// <c>hugo.Sites</c>：Hugo 的站点集合。Flint 目前单语言单站点，故集合只含本站，
/// <c>.Default</c>/<c>.First</c>/<c>.Last</c>/<c>index 0</c> 全部解析为当前站点对象。
/// 站点对象在渲染期装配（且共享的 hugo 对象不能就地改写），因此这里按上下文惰性取
/// <c>site</c> 全局——hugo-book 的 <c>_partials/docs/links/home.html</c> 用
/// <c>hugo.Sites.Default.Home.RelPermalink</c>，此前报 "for a null object"
/// </summary>
internal sealed class HugoSitesObject : ScriptObject, IFlintNonDataObject
{
    public override bool TryGetValue(
        Scriban.TemplateContext? context, Scriban.Parsing.SourceSpan span, string member, out object? value)
    {
        switch (member)
        {
            case "Default" or "default" or "First" or "first" or "Last" or "last" or "0":
                value = ResolveSite(context);
                return true;
            case "Len" or "len" or "Count" or "count":
                value = 1;
                return true;
        }
        return base.TryGetValue(context, span, member, out value);
    }

    /// <summary>从渲染上下文取 <c>site</c> 全局（无上下文/未装配时返回 null）</summary>
    private static object? ResolveSite(Scriban.TemplateContext? context)
    {
        if (context is null)
        {
            return null;
        }
        try
        {
            return context.Evaluate(new Scriban.Syntax.ScriptVariableGlobal("site"));
        }
        catch (Scriban.Syntax.ScriptRuntimeException)
        {
            return null;
        }
    }
}
