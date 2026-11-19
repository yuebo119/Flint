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
    public string Version { get; init; } = "0.170.1";

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


        // time.*：Hugo 的 time 兼有裸函数（`time .Date`，github-style 实测）与命名空间
        //（time.Format/Duration/Now，0.146+ 官方形态）两种身份。Scriban 引擎层无法二合一：
        // 实测（tmp/qr-verify 探针，2026-11）实现 IScriptCustomFunction 的对象在任何
        // 求值位置都会被劫持为函数调用（连裸 `{{ obj }}` 打印都报"0 参调用"），而
        // RegisterDateFunctions 注册的 ScriptFunction 无成员语义——`time.Format` 会
        // 回落成调用 time 本身报 "Invalid number of arguments 0 passed to time"。
        // 结论：time 命名空间（AsTime/In/ParseDuration/Format/Duration/Now）的兼容
        // 由**迁移器**承担（time.Format → date.to_string 已实现，共用 FormatHugoDate；
        // time.Duration → duration）。本方法不再触碰 root["time"]；
        // Go 时长语义的增强落在全局 duration（见 RegisterDateFunctions）
    }

    /// <summary>Go 的 ParseDuration 简易解析："2h45m"、"-1.5h"、"300ms"；
    /// 非法串返回 Zero（与全局 duration 的失败语义一致）</summary>
    private static TimeSpan ParseGoDuration(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return TimeSpan.Zero;
        }
        var rest = s.AsSpan().Trim();
        var neg = false;
        if (rest.Length > 0 && (rest[0] == '+' || rest[0] == '-'))
        {
            neg = rest[0] == '-';
            rest = rest[1..];
        }
        double total = 0;
        var any = false;
        while (rest.Length > 0)
        {
            // 数字部分（可含小数）
            var numEnd = 0;
            while (numEnd < rest.Length && (char.IsDigit(rest[numEnd]) || rest[numEnd] == '.'))
            {
                numEnd++;
            }
            if (numEnd == 0 || !double.TryParse(rest[..numEnd], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var n))
            {
                return TimeSpan.Zero;
            }
            rest = rest[numEnd..];
            // 单位部分
            var unit = "";
            foreach (var u in new[] { "ns", "us", "µs", "ms", "s", "m", "h" })
            {
                if (rest.StartsWith(u, StringComparison.Ordinal))
                {
                    unit = u;
                    break;
                }
            }
            // 注意 "ms" 已在 "s" 之前匹配；但 "m" 会让 "ms" 截半——按长度排序的
            // 表已保证 "ms" 先于 "m"/"s" 试探
            if (unit.Length == 0)
            {
                return TimeSpan.Zero;
            }
            rest = rest[unit.Length..];
            total += unit switch
            {
                "ns" => n / 100,
                "us" or "µs" => n / 10_000,
                "ms" => n / 1000,
                "s" => n,
                "m" => n * 60,
                "h" => n * 3600,
                _ => 0
            };
            any = true;
        }
        if (!any)
        {
            return TimeSpan.Zero;
        }
        return neg ? TimeSpan.FromSeconds(-total) : TimeSpan.FromSeconds(total);
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
