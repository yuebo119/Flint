// Flint 静态站点生成器
// Hugo 兼容 Liquid 过滤器（FLUID-MIGRATION-PLAN P4-2 第一批）：
// 新站点 Liquid 模板高频使用的文本/集合/i18n 函数族。Fluid 内置 72 个
// 过滤器（where/sort/map/reject/slice/join/replace/truncate/round/date 等）
// 之外的 Hugo 名在此补齐；文本函数经 TemplateTextFunctions 与 Scriban 侧
// 共用单一事实源。
//
// 分级（P4-1）：本批 = 新站点常用（T1）；deferred（T2，差异清单登记）：
// findRE/replaceRE（正则，AOT 下需 source-gen）、group/apply/seq、
// path.*/strings.* 命名空间形态、resources.*/css.*/js.*（P4-3/P4-4）。

using System.Globalization;
using Flint.Core.Content;
using Fluid;
using Fluid.Values;

namespace Flint.Core.Templates;

/// <summary>
/// Hugo 名过滤器注册：text/content/collections/i18n 家族。
/// 全部为纯函数或经 AmbientValues 取每渲染数据，AOT 安全
/// </summary>
internal static class FluidHugoFilters
{
    /// <summary>AmbientValues 键：站点翻译表（i18n 过滤器用）</summary>
    internal const string TranslationsKey = "__flint_fluid_translations";

    private static readonly MarkdownParser SharedMarkdown = new();

    /// <summary>注册全部 Hugo 名过滤器（渲染器构造时调用一次）</summary>
    public static void Register(TemplateOptions options)
    {
        var f = options.Filters;
        // ---- 文本族（与 Scriban 侧共用 TemplateTextFunctions）----
        f.AddFilter("titleize", Titleize);
        f.AddFilter("title", Titleize);
        f.AddFilter("humanize", Humanize);
        f.AddFilter("urlize", Urlize);
        f.AddFilter("anchorize", Urlize);
        f.AddFilter("singularize", Singularize);
        f.AddFilter("pluralize", Pluralize);
        // ---- 内容族 ----
        f.AddFilter("markdownify", Markdownify);
        f.AddFilter("plainify", Plainify);
        f.AddFilter("jsonify", Jsonify);
        f.AddFilter("safe_html", Identity);
        f.AddFilter("safeHTML", Identity);
        f.AddFilter("safe_css", Identity);
        f.AddFilter("safe_js", Identity);
        f.AddFilter("safe_url", Identity);
        f.AddFilter("safe_attr", Identity);
        // ---- 集合族（Fluid 内置 where/sort/map/reject/slice 之外的 Hugo 名）----
        f.AddFilter("delimit", Delimit);
        f.AddFilter("after", After);
        f.AddFilter("union", Union);
        f.AddFilter("intersect", Intersect);
        f.AddFilter("complement", Complement);
        // ---- i18n ----
        f.AddFilter("i18n", I18n);
        f.AddFilter("T", I18n);
    }

    // ---- 文本族 ----

    private static ValueTask<FluidValue> Titleize(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(TemplateTextFunctions.Titleize(ToString(input, ctx)), ctx.Options));

    private static ValueTask<FluidValue> Humanize(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(TemplateTextFunctions.Humanize(ToString(input, ctx)), ctx.Options));

    private static ValueTask<FluidValue> Urlize(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(TemplateTextFunctions.Urlize(ToString(input, ctx)), ctx.Options));

    private static ValueTask<FluidValue> Singularize(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(TemplateTextFunctions.Singularize(ToString(input, ctx)), ctx.Options));

    private static ValueTask<FluidValue> Pluralize(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        // {{ count | pluralize: singular, plural }}：计数形态（与 Scriban 侧
        // pluralize count singular plural 同语义）
        var count = ToNumber(input, ctx);
        var singular = args.Count > 0 ? ToString(args.At(0), ctx) : "";
        var plural = args.Count > 1 ? ToString(args.At(1), ctx) : null;
        return new(FluidValue.Create(TemplateTextFunctions.Pluralize((int)count, singular, plural), ctx.Options));
    }

    // ---- 内容族 ----

    private static ValueTask<FluidValue> Markdownify(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var markdown = ToString(input, ctx);
        if (markdown.Length == 0)
        {
            return new(FluidValue.Create("", ctx.Options));
        }
        // 共享无钩子管道实例（MarkdownParser 管道只读，并发安全）
        return new(FluidValue.Create(SharedMarkdown.ToHtml(markdown), ctx.Options));
    }

    private static ValueTask<FluidValue> Plainify(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(TemplateTextFunctions.Plainify(ToString(input, ctx)), ctx.Options));

    private static ValueTask<FluidValue> Jsonify(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
#pragma warning disable IL2026, IL3050 // jsonify 的输入是模板值对象（字符串/字典/标量），
        // JsonSerializer(object) 的反射序列化在 NativeAOT 下对任意图形有裁剪风险；
        // 与 VYaml 配置解析路径同口径文件级抑制——模型图形由模板作者保证
        var json = System.Text.Json.JsonSerializer.Serialize(input.ToObjectValue(ctx));
#pragma warning restore IL2026, IL3050
        return new(FluidValue.Create(json, ctx.Options));
    }

    private static ValueTask<FluidValue> Identity(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(input);

    // ---- 集合族 ----

    private static async ValueTask<FluidValue> Delimit(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        // Hugo delimit：delimit SEP [LAST]——末分隔符默认同 SEP
        var sep = args.Count > 0 ? ToString(args.At(0), ctx) : ", ";
        var last = args.Count > 1 ? ToString(args.At(1), ctx) : sep;
        var items = await ToStringListAsync(input, ctx);
        if (items.Count <= 1)
        {
            return FluidValue.Create(string.Join(sep, items), ctx.Options);
        }
        var head = string.Join(sep, items.Take(items.Count - 1));
        return FluidValue.Create(head + last + items[^1], ctx.Options);
    }

    private static async ValueTask<FluidValue> After(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        // Hugo after：after N——跳过前 N 个元素
        var n = args.Count > 0 ? (int)ToNumber(args.At(0), ctx) : 0;
        var items = await ToStringListAsync(input, ctx);
        return FluidValue.Create(items.Skip(Math.Max(0, n)).ToList(), ctx.Options);
    }

    private static ValueTask<FluidValue> Union(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        SetOp(input, args, ctx, union: true);

    private static ValueTask<FluidValue> Intersect(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        SetOp(input, args, ctx, union: false);

    private static async ValueTask<FluidValue> Complement(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        // complement：input 中不在 args 集合里的元素
        var items = await ToStringListAsync(input, ctx);
        var others = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++)
        {
            others.UnionWith(await ToStringListAsync(args.At(i), ctx));
        }
        return FluidValue.Create(items.Where(x => !others.Contains(x)).ToList(), ctx.Options);
    }

    private static async ValueTask<FluidValue> SetOp(
        FluidValue input, FilterArguments args, TemplateContext ctx, bool union)
    {
        var items = await ToStringListAsync(input, ctx);
        var others = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            others.AddRange(await ToStringListAsync(args.At(i), ctx));
        }
        List<string> result;
        if (union)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            result = [];
            foreach (var x in items.Concat(others))
            {
                if (seen.Add(x))
                {
                    result.Add(x);
                }
            }
        }
        else
        {
            var otherSet = new HashSet<string>(others, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            result = [];
            foreach (var x in items)
            {
                if (otherSet.Contains(x) && seen.Add(x))
                {
                    result.Add(x);
                }
            }
        }
        return FluidValue.Create(result, ctx.Options);
    }

    // ---- i18n ----

    private static ValueTask<FluidValue> I18n(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        // {{ "key" | i18n }}：经 AmbientValues 取站点翻译表，缺键返回空串
        // （对齐 Hugo i18n 语义）
        var key = ToString(input, ctx);
        if (ctx.AmbientValues.TryGetValue(TranslationsKey, out var table) &&
            table is IReadOnlyDictionary<string, string> translations &&
            translations.TryGetValue(key, out var text))
        {
            return new(FluidValue.Create(text, ctx.Options));
        }
        return new(FluidValue.Create("", ctx.Options));
    }

    // ---- 转换辅助 ----

    private static string ToString(FluidValue value, TemplateContext ctx) =>
        value.IsNil() ? "" : value.ToStringValue(ctx);

    private static double ToNumber(FluidValue value, TemplateContext ctx) =>
        (double)value.ToNumberValue(ctx);

    private static async ValueTask<List<string>> ToStringListAsync(FluidValue value, TemplateContext ctx)
    {
        if (value.IsNil())
        {
            return [];
        }
        if (value.ToObjectValue(ctx) is string s)
        {
            return [s];
        }
        var list = new List<string>();
        await foreach (var item in value.EnumerateAsync(ctx))
        {
            list.Add(item.IsNil() ? "" : item.ToStringValue(ctx));
        }
        return list;
    }
}
