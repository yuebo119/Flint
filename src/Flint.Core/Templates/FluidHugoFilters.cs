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
        // ---- path 族（Hugo path.* 的过滤器形态；P4-2 补齐）----
        f.AddFilter("path_base", PathBase);
        f.AddFilter("path_dir", PathDir);
        f.AddFilter("path_ext", PathExt);
        f.AddFilter("path_join", PathJoin);
        f.AddFilter("path_split", PathSplit);
        // ---- 正则族（Hugo findRE/replaceRE；[GeneratedRegex] source-gen，AOT 安全）----
        f.AddFilter("find_re", FindRe);
        f.AddFilter("findre", FindRe);
        f.AddFilter("replace_re", ReplaceRe);
        f.AddFilter("replacere", ReplaceRe);
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

    // ---- path 族 ----

    /// <summary>path.Base：末段（含扩展名）</summary>
    private static ValueTask<FluidValue> PathBase(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(Path.GetFileName(ToString(input, ctx).Replace('\\', '/')), ctx.Options));

    /// <summary>path.Dir：目录部分（不含末段；根级返回 "."）</summary>
    private static ValueTask<FluidValue> PathDir(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var path = ToString(input, ctx).Replace('\\', '/').TrimEnd('/');
        var slash = path.LastIndexOf('/');
        return new(FluidValue.Create(slash < 0 ? "." : slash == 0 ? "/" : path[..slash], ctx.Options));
    }

    /// <summary>path.Ext：扩展名（含点；无扩展名为空串）</summary>
    private static ValueTask<FluidValue> PathExt(FluidValue input, FilterArguments args, TemplateContext ctx) =>
        new(FluidValue.Create(Path.GetExtension(ToString(input, ctx).Replace('\\', '/')), ctx.Options));

    /// <summary>path.Join：多段拼接（实参为段；空段跳过）</summary>
    private static ValueTask<FluidValue> PathJoin(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var parts = new List<string> { ToString(input, ctx) };
        for (var i = 0; i < args.Count; i++)
        {
            var part = ToString(args.At(i), ctx);
            if (part.Length > 0)
            {
                parts.Add(part);
            }
        }
        // 手工拼接（Path.Combine 在 Windows 上产反斜杠，与模板的可预期形态不符）
        var joined = string.Join('/', parts.Select(p => p.Trim('/', '\\')));
        return new(FluidValue.Create(joined.Length == 0 ? "/" : "/" + joined, ctx.Options));
    }

    /// <summary>path.Split：返回 (dir, file) 二元组（Liquid 经二元索引消费）</summary>
    private static ValueTask<FluidValue> PathSplit(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var path = ToString(input, ctx).Replace('\\', '/');
        var slash = path.LastIndexOf('/');
        var dir = slash < 0 ? "" : slash == 0 ? "/" : path[..slash];
        var file = slash < 0 ? path : path[(slash + 1)..];
        return new(FluidValue.Create(new List<string> { dir, file }, ctx.Options));
    }

    // ---- 正则族（Hugo findRE/replaceRE；用户 pattern 运行期构造，
    //      [GeneratedRegex] 只覆盖固定 pattern 故此处用 Regex 非 source-gen，
    //      NativeAOT 下对不可静态分析的 pattern 有裁剪风险——与 VYaml 配置
    //      解析路径同口径文件级 pragma 抑制，pattern 来自模板作者（可信源））----

    /// <summary>findRE：返回输入中匹配给定 pattern 的全部子串（Hugo 语义：
    /// 无匹配返回空数组——模板经 size/for 消费）</summary>
    private static ValueTask<FluidValue> FindRe(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var pattern = args.Count > 0 ? ToString(args.At(0), ctx) : "";
        var text = ToString(input, ctx);
        if (pattern.Length == 0 || text.Length == 0)
        {
            return new(FluidValue.Create(new List<string>(), ctx.Options));
        }
        try
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern)
                .Select(m => m.Value).ToList();
            return new(FluidValue.Create(matches, ctx.Options));
        }
        catch (System.Text.RegularExpressions.RegexParseException)
        {
            // 坏 pattern 返回空（与 Hugo 的宽容度对齐；不中断渲染）
            return new(FluidValue.Create(new List<string>(), ctx.Options));
        }
    }

    /// <summary>replaceRE：把匹配 pattern 的子串替换为 replacement（$1 分组引用）</summary>
    private static ValueTask<FluidValue> ReplaceRe(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var pattern = args.Count > 0 ? ToString(args.At(0), ctx) : "";
        var replacement = args.Count > 1 ? ToString(args.At(1), ctx) : "";
        var text = ToString(input, ctx);
        if (pattern.Length == 0)
        {
            return new(FluidValue.Create(text, ctx.Options));
        }
        try
        {
            return new(FluidValue.Create(
                System.Text.RegularExpressions.Regex.Replace(text, pattern, replacement), ctx.Options));
        }
        catch (System.Text.RegularExpressions.RegexParseException)
        {
            return new(FluidValue.Create(text, ctx.Options));
        }
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
