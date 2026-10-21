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
using System.Runtime.InteropServices;
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
        // ---- 序列/分组族（Hugo group/apply/seq/shuffle；P4-2 补齐）----
        f.AddFilter("group", Group);
        f.AddFilter("apply", Apply);
        f.AddFilter("seq", Seq);
        f.AddFilter("shuffle", Shuffle);
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
        // AOT 安全：JsonSerializer.Serialize(object) 对复杂 CLR 对象图形
        // （PageContext/SiteContext 等）在 NativeAOT 裁剪后可能抛
        // MissingMetadataException 或产生不完整 JSON。先尝试序列化，
        // 失败时回退到 ToString 形态（2026-10-21 审计修复）。
        // pragma 抑制：与 VYaml 配置解析路径同口径——模型图形由模板作者
        // 保证，try-catch 兜底运行时失败
#pragma warning disable IL2026, IL3050
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(input.ToObjectValue(ctx));
            return new(FluidValue.Create(json, ctx.Options));
        }
        catch (Exception)
        {
            return new(FluidValue.Create(input.ToStringValue(ctx), ctx.Options));
        }
#pragma warning restore IL2026, IL3050
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

    // ---- 序列/分组族 ----

    /// <summary>
    /// group KEY：按成员值分组（Hugo .GroupBy 语义）。Liquid 无对象字面量，
    /// 返回**二元组列表** [key, items]（每项两个元素），组序 = 首现顺序。
    /// 例：{{ pages | group: 'section' }} → [['posts', [...]], ['about', [...]]]
    /// </summary>
    private static async ValueTask<FluidValue> Group(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var key = args.Count > 0 ? ToString(args.At(0), ctx) : "";
        var items = await ToObjectListAsync(input, ctx);
        var groups = new List<(string Key, List<object?> Items)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var member = await MemberOfAsync(ctx, item, key);
            var k = member?.ToString() ?? "";
            if (!index.TryGetValue(k, out var slot))
            {
                slot = groups.Count;
                index[k] = slot;
                groups.Add((k, []));
            }
            groups[slot].Items.Add(item);
        }
        // 二元组形态：List<List<object?>>，每组 [key, items]
        var result = groups.Select(g => (object?)new List<object?> { g.Key, g.Items }).ToList();
        return FluidValue.Create(result, ctx.Options);
    }

    /// <summary>
    /// apply 'filter'：对集合每个元素应用指定名的过滤器（Hugo apply 语义）。
    /// 过滤器名经 TemplateOptions.Filters 解析——已注册的任意过滤器可用。
    /// **额外参数转发**（Hugo apply SLICE FUNCTION PARAM... 语义）：args.At(1)
    /// 之后的参数原样传给目标过滤器（2026-10-21 审计修复：此前传
    /// FilterArguments.Empty，额外参数全部丢弃）
    /// </summary>
    private static async ValueTask<FluidValue> Apply(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var filterName = args.Count > 0 ? ToString(args.At(0), ctx) : "";
        if (filterName.Length == 0 || !ctx.Options.Filters.TryGetValue(filterName, out var filter))
        {
            return input;
        }
        // 收集额外参数（args.At(1) 之后的位置参数 + 全部命名参数），
        // 经 FilterArguments.Add 逐个构建（FilterArguments 无接受
        // List<FluidValue> 的构造函数，只有 FluidValue[] 与 Add 方法）
        var forwarded = FilterArguments.Empty;
        for (var i = 1; i < args.Count; i++)
        {
            forwarded = forwarded.Add(args.At(i));
        }
        foreach (var name in args.Names)
        {
            forwarded = forwarded.Add(name, args[name]);
        }
        var items = await ToObjectListAsync(input, ctx);
        var result = new List<object?>(items.Count);
        foreach (var item in items)
        {
            var value = FluidValue.Create(item, ctx.Options);
            var transformed = await filter(value, forwarded, ctx);
            result.Add(transformed.ToObjectValue(ctx));
        }
        return FluidValue.Create(result, ctx.Options);
    }

    /// <summary>
    /// seq：整数序列（Hugo seq 语义）。输入 = 末值；first 命名实参 = 首值
    /// （缺省 1）；step 命名实参 = 步长（缺省 1，负值倒序）
    /// </summary>
    private static ValueTask<FluidValue> Seq(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var last = (int)ToNumber(input, ctx);
        var first = args.HasNamed("first") ? (int)ToNumber(args["first"], ctx) : 1;
        var step = args.HasNamed("step") ? (int)ToNumber(args["step"], ctx) : 1;
        if (step == 0)
        {
            step = 1;
        }
        var result = new List<object?>();
        if (step > 0)
        {
            for (var v = first; v <= last; v += step)
            {
                result.Add(v);
            }
        }
        else
        {
            for (var v = first; v >= last; v += step)
            {
                result.Add(v);
            }
        }
        return new(FluidValue.Create(result, ctx.Options));
    }

    /// <summary>shuffle：随机乱序（Random.Shared，AOT 安全；测试经种子不可复现，属预期）</summary>
    private static async ValueTask<FluidValue> Shuffle(FluidValue input, FilterArguments args, TemplateContext ctx)
    {
        var items = await ToObjectListAsync(input, ctx);
        Random.Shared.Shuffle(CollectionsMarshal.AsSpan(items));
        return FluidValue.Create(items, ctx.Options);
    }

    /// <summary>对象列表化（字符串 → 单元素；集合 → 元素表；标量 → 单元素）</summary>
    private static async ValueTask<List<object?>> ToObjectListAsync(FluidValue value, TemplateContext ctx)
    {
        if (value.IsNil())
        {
            return [];
        }
        var list = new List<object?>();
        await foreach (var item in value.EnumerateAsync(ctx))
        {
            list.Add(item.IsNil() ? null : item.ToObjectValue(ctx));
        }
        return list;
    }

    /// <summary>
    /// 取对象的成员值：经**当前上下文**的成员访问策略（Flint 策略，
    /// snake_case 可命中；不用 TemplateOptions.Default——丢了 snake 映射）。
    /// **await 而非 GetResult**（2026-10-21 审计修复：同步阻塞在
    /// Parallel.ForEachAsync 下有线程池饥饿风险）
    /// </summary>
    private static async ValueTask<object?> MemberOfAsync(TemplateContext ctx, object? item, string memberName)
    {
        if (item is null || memberName.Length == 0)
        {
            return null;
        }
        var accessor = ctx.Options.MemberAccessStrategy.GetAccessor(
            item.GetType(), memberName, ctx.ModelNamesComparer);
        if (accessor is null)
        {
            return null;
        }
        var value = await accessor.GetAsync(item, memberName, ctx);
        return value.IsNil() ? null : value.ToObjectValue(ctx);
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

    /// <summary>path.Join：多段拼接（实参为段；空段跳过）。
    /// **保留相对性**（Hugo path.Join 委托 Go path.Join 语义）：首段以 / 开头
    /// 时产物带 / 前缀，否则产物为相对路径（2026-10-21 审计修复：此前无条件
    /// 前置 /，把 Hugo 的相对路径输出变成绝对路径）</summary>
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
        if (joined.Length == 0)
        {
            return new(FluidValue.Create("/", ctx.Options));
        }
        // 首段带 / 时保留绝对路径形态，否则相对路径（对齐 Go path.Join）
        var isAbsolute = parts[0].StartsWith('/');
        return new(FluidValue.Create(isAbsolute ? "/" + joined : joined, ctx.Options));
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
