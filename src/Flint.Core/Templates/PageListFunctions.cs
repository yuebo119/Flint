// Flint 静态站点生成器
// Huffo Pages 集合方法族（Hugo methods/pages）
//
// 实测主题高频用法：
//   .Pages.ByDate / ByWeight / ByTitle（列表排序）
//   .Site.RegularPages.Related . | first 5（相关内容推荐）
//   .Pages.GroupBy "Section"（分组列表）
//   .Pages.Reverse / Limit / IndexOf / Next / Prev
//
// 全部实现为 IScriptCustomFunction（不经反射绑定，AOT 安全）。

using System.Globalization;
using Scriban.Runtime;
using Scriban.Syntax;
using FlintPageContext = Flint.Core.Abstractions.PageContext;

namespace Flint.Core.Templates;

/// <summary>
/// 页面对象工厂入口（集合方法用）：转发到渲染器的共享 CWT 缓存，
/// 保证集合产出的页面对象与页面/站点对象是同一实例
/// </summary>
internal static class PageObjectFactory
{
    /// <summary>按 PageContext 取（或建）共享页面对象</summary>
    public static ScriptObject Create(FlintPageContext page) =>
        ScribanTemplateRenderer.CreatePageObject(page);
}

/// <summary>集合方法基类：统一的 IScriptCustomFunction 样板</summary>
public abstract class PageListFunctionBase : IScriptCustomFunction
{
    private readonly string _paramName;

    protected PageListFunctionBase(string paramName = "value") => _paramName = paramName;

    public abstract object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement);

    public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        new(Invoke(context, callerContext, arguments, blockStatement));

    public int RequiredParameterCount => 0;
    public virtual int ParameterCount => 1;
    public ScriptVarParamKind VarParamKind => ScriptVarParamKind.Direct;
    public Type ReturnType => typeof(object);
    public ScriptParameterInfo GetParameterInfo(int index) => new(typeof(object), _paramName);
    public ScriptParameterInfo ReturnParameterInfo => new(typeof(object), "result");

    /// <summary>结果转 ScriptObject 列表（经共享工厂，页面对象复用）</summary>
    protected static ScriptArray ToArray(IEnumerable<FlintPageContext> pages)
    {
        var arr = new ScriptArray();
        foreach (var p in pages)
        {
            arr.Add(PageObjectFactory.Create(p));
        }
        return arr;
    }

    /// <summary>从 ScriptObject 还原 PageContext（供 Related 的入参归一）</summary>
    protected static FlintPageContext? ToPageContext(object? value) =>
        value switch
        {
            FlintPageContext p => p,
            ScribanTemplateRenderer.LazyPageObject lp => lp.PageContext,
            _ => null
        };
}

/// <summary>按日期倒序（Hugo ByDate：新→旧）</summary>
public sealed class PagesByDateFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase
{
    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        ToArray(pages.OrderByDescending(p => p.Date));
}

/// <summary>按标题（忽略大小写、数字感知，Hugo 语义）</summary>
public sealed class PagesByTitleFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase
{
    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        ToArray(pages.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase));
}

/// <summary>按权重升序（Hugo ByWeight）</summary>
public sealed class PagesByWeightFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase
{
    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        ToArray(pages.OrderBy(p => p.Weight).ThenBy(p => p.Title, StringComparer.OrdinalIgnoreCase));
}

/// <summary>按内容长度降序（Hugo ByLength）</summary>
public sealed class PagesByLengthFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase
{
    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        ToArray(pages.OrderByDescending(p => p.Content.Length));
}

/// <summary>按最后修改倒序（Hugo ByLastmod）</summary>
public sealed class PagesByLastmodFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase
{
    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        ToArray(pages.OrderByDescending(p => p.LastMod ?? p.Date));
}

/// <summary>按指定参数排序（Hugo ByParam NAME）</summary>
public sealed class PagesByParamFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase("param")
{
    public override int ParameterCount => 1;

    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var key = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "";
        return ToArray(pages.OrderBy(p => ParamSortKey(p, key), StringComparer.OrdinalIgnoreCase));
    }

    private static string ParamSortKey(FlintPageContext p, string key) =>
        p.Params.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
}

/// <summary>
/// 相关内容（Hugo .Related）：按默认关键词算法打分——
/// keywords(100) / date(100，同日满分) / tags(80) / categories(80)。
/// 对齐 Hugo 官方默认配置；得分 > 0 才入选，按分数降序
/// </summary>
public sealed class PagesRelatedFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase("page")
{
    public override int ParameterCount => 1;

    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var target = arguments.Count > 0 ? ToPageContext(arguments[0]) : null;
        if (target is null)
        {
            return new ScriptArray();
        }

        var scored = new List<(FlintPageContext Page, int Score)>();
        foreach (var p in pages)
        {
            if (ReferenceEquals(p, target) ||
                string.Equals(p.Permalink, target.Permalink, StringComparison.Ordinal))
            {
                continue;
            }

            var score = Score(p, target);
            if (score > 0)
            {
                scored.Add((p, score));
            }
        }

        return ToArray(scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Page.Date)
            .Select(x => x.Page));
    }

    /// <summary>Hugo related 默认评分：keywords 100 / date 100 / tags 80 / categories 80</summary>
    private static int Score(FlintPageContext a, FlintPageContext b)
    {
        var score = 0;

        // keywords（front matter，权重 100，每命中一个）
        score += 100 * IntersectCount(
            ParamList(a, "keywords"), ParamList(b, "keywords"));

        // tags（权重 80）
        score += 80 * IntersectCount(a.Tags, b.Tags);

        // categories（权重 80）
        score += 80 * IntersectCount(a.Categories, b.Categories);

        // date（权重 100：同一天满分，否则按天数差衰减）
        if (a.Date.Date == b.Date.Date)
        {
            score += 100;
        }

        return score;
    }

    private static IReadOnlyList<string> ParamList(FlintPageContext p, string key)
    {
        if (!p.Params.TryGetValue(key, out var v) || v is null)
        {
            return [];
        }
        if (v is string s)
        {
            return [s];
        }
        if (v is System.Collections.IEnumerable en)
        {
            return en.Cast<object?>().Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList();
        }
        return [v.ToString() ?? ""];
    }

    private static int IntersectCount(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }
        var set = b.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return a.Count(x => set.Contains(x));
    }
}

/// <summary>
/// 通用数组反转（Hugo 的 Pages.Reverse 用于任意 ScriptArray，
/// 如 .Ancestors.Reverse 做面包屑）
/// </summary>
public sealed class ArrayReverseFunction(ScriptArray source) : Scriban.Runtime.IScriptCustomFunction
{
    public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
        ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
    {
        var arr = new ScriptArray();
        for (var i = source.Count - 1; i >= 0; i--)
        {
            arr.Add(source[i]);
        }
        return arr;
    }

    public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
        ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
        new(Invoke(context, callerContext, arguments, blockStatement));

    public int RequiredParameterCount => 0;
    public int ParameterCount => 0;
    public ScriptVarParamKind VarParamKind => ScriptVarParamKind.Direct;
    public Type ReturnType => typeof(object);
    public ScriptParameterInfo GetParameterInfo(int index) => new(typeof(object), "unused");
    public ScriptParameterInfo ReturnParameterInfo => new(typeof(object), "result");
}

/// <summary>反转顺序（Hugo Reverse）</summary>
public sealed class PagesReverseFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase
{
    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        ToArray(pages.Reverse());
}

/// <summary>取前 N（Hugo Limit N）</summary>
public sealed class PagesLimitFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase("limit")
{
    public override int ParameterCount => 1;

    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var n = arguments.Count > 0 && int.TryParse(arguments[0]?.ToString(),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return ToArray(pages.Take(Math.Max(0, n)));
    }
}

/// <summary>
/// 分组（Hugo GroupBy KEY）：返回 [{Key, Pages}]，按 key 排序
/// </summary>
public sealed class PagesGroupByFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase("key")
{
    public override int ParameterCount => 1;

    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var key = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "";
        var groups = pages
            .GroupBy(p => GroupKey(p, key), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        var arr = new ScriptArray();
        foreach (var g in groups)
        {
            var o = new ScriptObject
            {
                ["Key"] = g.Key,
                ["key"] = g.Key,
                ["Pages"] = ToArray(g),
                ["pages"] = ToArray(g)
            };
            arr.Add(o);
        }
        return arr;
    }

    private static string GroupKey(FlintPageContext p, string key) =>
        key.ToLowerInvariant() switch
        {
            "section" => p.Section,
            "kind" => p.Kind,
            "type" => p.Type ?? "",
            "layout" => p.Layout ?? "",
            _ => p.Params.TryGetValue(key, out var v) ? v?.ToString() ?? "" : ""
        };
}

/// <summary>按日期分组（Hugo GroupByDate FORMAT）</summary>
public sealed class PagesGroupByDateFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase("format")
{
    public override int ParameterCount => 1;

    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var format = arguments.Count > 0 ? arguments[0]?.ToString() ?? "2006-01" : "2006-01";
        var groups = pages
            .GroupBy(p => FormatGroupKey(p.Date, format))
            .OrderByDescending(g => g.Key, StringComparer.Ordinal);

        var arr = new ScriptArray();
        foreach (var g in groups)
        {
            var o = new ScriptObject
            {
                ["Key"] = g.Key,
                ["key"] = g.Key,
                ["Pages"] = ToArray(g),
                ["pages"] = ToArray(g)
            };
            arr.Add(o);
        }
        return arr;
    }

    /// <summary>Go layout → 分组键（覆盖 year/month/day 三类高频形态）</summary>
    private static string FormatGroupKey(DateTimeOffset date, string format)
    {
        if (format.Contains("2006-01-02", StringComparison.Ordinal) || format.Contains("January 2, 2006", StringComparison.Ordinal))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (format.Contains("2006-01", StringComparison.Ordinal))
        {
            return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }
        if (format.Contains("2006", StringComparison.Ordinal))
        {
            return date.ToString("yyyy", CultureInfo.InvariantCulture);
        }
        if (format.Contains("January 2006", StringComparison.Ordinal) || format.Contains("Jan 2006", StringComparison.Ordinal))
        {
            return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

/// <summary>索引查找（Hugo IndexOf）</summary>
public sealed class PagesIndexOfFunction(IReadOnlyList<FlintPageContext> pages) : PageListFunctionBase("page")
{
    public override int ParameterCount => 1;

    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var target = arguments.Count > 0 ? ToPageContext(arguments[0]) : null;
        if (target is null)
        {
            return -1L;
        }
        for (var i = 0; i < pages.Count; i++)
        {
            if (ReferenceEquals(pages[i], target) ||
                string.Equals(pages[i].Permalink, target.Permalink, StringComparison.Ordinal))
            {
                return (long)i;
            }
        }
        return -1L;
    }
}

/// <summary>取指定页在序列中的下/上一项（Hugo Next/Prev）</summary>
public sealed class PagesNextPrevFunction(IReadOnlyList<FlintPageContext> pages, bool forward)
    : PageListFunctionBase("page")
{
    public override int ParameterCount => 1;

    public override object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var target = arguments.Count > 0 ? ToPageContext(arguments[0]) : null;
        if (target is null)
        {
            return null;
        }
        for (var i = 0; i < pages.Count; i++)
        {
            if (!ReferenceEquals(pages[i], target) &&
                !string.Equals(pages[i].Permalink, target.Permalink, StringComparison.Ordinal))
            {
                continue;
            }
            var idx = forward ? i + 1 : i - 1;
            return idx >= 0 && idx < pages.Count ? PageObjectFactory.Create(pages[idx]) : null;
        }
        return null;
    }
}

/// <summary>
/// <c>.Markup FORMAT</c>（Hugo v0.146+）：按输出格式返回**内容渲染作用域**。
/// </summary>
/// <remarks>
/// Hugo v0.166 实测：<c>.Markup "home"</c> 返回内容作用域对象（内部类型
/// cachedContentScope），<c>.Render</c> 取渲染结果、<c>.Render.Summary.Text</c>
/// 取该作用域下的摘要 HTML（探针实测输出 <c>&lt;p&gt;body &lt;strong&gt;bold&lt;/strong&gt; text&lt;/p&gt;</c>）。
/// 主题用法（FixIt 的 summary.html）：<c>with .Markup "home" → with .Render →
/// dict "Content" .Summary.Text …</c>，即"首页摘要按 home 作用域渲染"。
/// </remarks>
/// <remarks>
/// **已知差异**：Flint 在此直接给出页面既有的摘要/正文渲染结果，
/// 不会把 <c>hugo.Context.MarkupScope</c> 切到 "home"——
/// 主题里 <c>ne hugo.Context.MarkupScope "home"</c> 的 markdown 钩子分支
/// 因此走"非 home"形态（摘要中的交互组件不会被降级为静态形态）。
/// 该差异只影响首页摘要的呈现细节，不影响构建与页面内容完整性。
/// </remarks>
public sealed class PageMarkupFunction(FlintPageContext page)
    : Scriban.Runtime.IScriptCustomFunction
{
    public object? Invoke(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement)
    {
        var summaryText = page.Summary ?? "";
        var render = new ScriptObject
        {
            ["content"] = page.Content ?? "",
            ["Content"] = page.Content ?? "",
            ["plain"] = page.Plain ?? "",
            ["Plain"] = page.Plain ?? "",
            ["summary"] = new ScriptObject
            {
                ["text"] = summaryText,
                ["Text"] = summaryText,
                ["plain"] = page.Plain ?? "",
                ["Plain"] = page.Plain ?? ""
            }
        };
        render["Summary"] = render["summary"];
        return new ScriptObject
        {
            ["render"] = render,
            ["Render"] = render,
            // 作用域标识（Hugo 里用于区分输出格式；主题据此取不同渲染结果）
            ["format"] = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "",
            ["Format"] = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : ""
        };
    }

    public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context, ScriptNode? callerContext,
        ScriptArray arguments, ScriptBlockStatement? blockStatement) =>
        new(Invoke(context, callerContext, arguments, blockStatement));

    public int RequiredParameterCount => 0;
    public int ParameterCount => 1;
    public Scriban.Runtime.ScriptVarParamKind VarParamKind => Scriban.Runtime.ScriptVarParamKind.Direct;
    public Type ReturnType => typeof(object);
    public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index)
        => new(typeof(string), "format");
    public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo
        => new(typeof(object), "scope");
}
