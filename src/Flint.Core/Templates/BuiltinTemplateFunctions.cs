// Flint 静态站点生成器
// 内置模板函数实现
//
// IL2026/IL3050：Scriban 7 起 ScriptObjectExtensions.Import 标注
// RequiresUnreferencedCode（反射创建 DynamicCustomFunction）。此处全部
// Import 目标是本类显式引用的 lambda，linker 不会裁剪被引用成员——
// 运行时安全，文件级压制与 ConfigParser 同模式（disable/restore 配对，G9 门禁验证）
#pragma warning disable IL2026, IL3050

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Scriban.Runtime;

namespace Flint.Core.Templates;

/// <summary>
/// 内置模板函数
/// 提供 80+ 个常用函数，语法兼容 Hugo 模板函数；
/// 部分依赖页面索引/资源管线的 Hugo 函数（如 emojify、resources.*）未提供——调用会报函数未定义而非静默空转
/// </summary>
public sealed partial class BuiltinTemplateFunctions
{
    private readonly string _baseUrl;

    /// <summary>
    /// markdownify 用的 Markdown 解析器（无状态，管道构建一次复用）
    /// </summary>
    private static readonly Content.MarkdownParser MarkdownifyParser = new();

    /// <summary>
    /// 创建内置函数实例
    /// </summary>
    /// <param name="baseUrl">站点基址（URL 函数用）</param>
    /// <param name="resources">模板资源提供者（resources.*/css.*/images.* 用；null 时命名空间注册为空对象）</param>
    /// <param name="environment">模板环境信息（hugo.* 常量对象用）</param>
    public BuiltinTemplateFunctions(
        string baseUrl = "",
        ITemplateResourceProvider? resources = null,
        TemplateEnvironmentInfo? environment = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _resources = resources;
        _environment = environment ?? TemplateEnvironmentInfo.Default;
    }

    private readonly TemplateEnvironmentInfo _environment;

    /// <summary>
    /// 注册所有内置函数到 ScriptObject
    /// </summary>
    public void RegisterFunctions(ScriptObject scriptObject)
    {
        // 字符串函数
        RegisterStringFunctions(scriptObject);

        // 日期函数
        RegisterDateFunctions(scriptObject);

        // 集合函数
        RegisterCollectionFunctions(scriptObject);

        // URL 函数
        RegisterUrlFunctions(scriptObject);

        // 数学函数
        RegisterMathFunctions(scriptObject);

        // 编码函数
        RegisterEncodingFunctions(scriptObject);

        // 比较函数
        RegisterComparisonFunctions(scriptObject);

        // 调试函数
        RegisterDebugFunctions(scriptObject);

        // 安全函数
        RegisterSafeFunctions(scriptObject);

        // HTML 转义/剥标签：Scriban 7.2 内置 html 对象已提供
        //（html.escape/html.strip/html.newline_to_br——转义契约见 README），
        // 不重复注册避免与内置 html 对象遮蔽冲突

        // 资源函数
        RegisterResourceFunctions(scriptObject);

        // Hugo 主题兼容函数组（B6~B12）：内容/URL/集合/构造/查询
        RegisterHugoCompatFunctions(scriptObject);

        // ---- 命名空间层（Hugo 0.146+ 形态）----
        // 顺序：先补缺失的全局实现 → 再建别名命名空间对象 → 最后建资源/环境命名空间
        RegisterMissingFunctions(scriptObject);
        RegisterNamespaceAliases(scriptObject);
        RegisterEnvironmentNamespace(scriptObject);
        RegisterResourceNamespaces(scriptObject);
    }

    /// <summary>
    /// 数值归一：真实数值直接转；**数字字符串**也接受（Flint 的 front matter
    /// 标量存为字符串，Hugo 则类型化为数值——故需容忍 "3" 这类输入）；
    /// 真非数字（"hello"）抛异常，保持与 Hugo 一致的类型错误语义
    /// </summary>
    private static double ToNum(object? v)
    {
        switch (v)
        {
            case null:
                return 0d;
            case double d:
                return d;
            case float f:
                return f;
            case int i:
                return i;
            case long l:
                return l;
            case decimal m:
                return (double)m;
            case bool b:
                return b ? 1d : 0d;
        }

        var text = v.ToString();
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"数学函数收到非数值参数: '{text}'");
    }

    #region 字符串函数 (20+)

    private void RegisterStringFunctions(ScriptObject obj)
    {
        // upper - 转大写
        obj.Import("upper", (string? s) => s?.ToUpperInvariant() ?? "");

        // lower - 转小写
        obj.Import("lower", (string? s) => s?.ToLowerInvariant() ?? "");

        // title - 首字母大写
        obj.Import("title", (string? s) =>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s?.ToLowerInvariant() ?? ""));

        // trim - 去首尾空白；带第二参时按 cutset 字符集裁剪
        //（Hugo `trim STRING CUTSET` / `strings.Trim STRING CUTSET`；
        // 严格单参形参导致 PaperMod 的 `trim $x "\n\r\t "` 报
        // "Argument index must be < 1"——Scriban 拒绝多余实参而非忽略）
        obj.Import("trim", (params object?[] a) =>
        {
            var s = a.Length > 0 ? a[0]?.ToString() ?? "" : "";
            if (a.Length < 2 || a[1] is not { } cutsetArg)
            {
                return s.Trim();
            }
            // Hugo 按 Unicode 码点集合裁剪（非子串），与 strings.Trim 一致
            var set = (cutsetArg.ToString() ?? "").ToHashSet();
            return s.Trim([.. set]);
        });

        // trim_left - 去除左侧空白
        obj.Import("trim_left", (string? s) => s?.TrimStart() ?? "");
        obj.Import("trimleft", (string? s) => s?.TrimStart() ?? "");

        // trim_right - 去除右侧空白
        obj.Import("trim_right", (string? s) => s?.TrimEnd() ?? "");
        obj.Import("trimright", (string? s) => s?.TrimEnd() ?? "");

        // truncate - 截断字符串（省略号可省，默认 "…"；Hugo `truncate LEN STRING` 经
        // 管道形态 `X | truncate LEN` 只给两参——严格三参形参报
        // "Invalid number of arguments 2 passed to `truncate`"，PaperMod schema_json 实测）
        obj.Import("truncate", (params object?[] a) =>
        {
            if (a.Length < 2)
            {
                return a.Length > 0 ? a[0]?.ToString() ?? "" : "";
            }
            var s = a[0]?.ToString();
            var length = ToInt(a[1]);
            var ellipsis = a.Length > 2 ? a[2]?.ToString() ?? "…" : "…";
            if (string.IsNullOrEmpty(s) || s.Length <= length)
                return s ?? "";
            if (length <= ellipsis.Length)
                return ellipsis[..length];
            return s[..(length - ellipsis.Length)] + ellipsis;
        });

        // replace - 替换字符串
        obj.Import("replace", (string? s, string? old, string? @new) =>
            s?.Replace(old ?? "", @new ?? "") ?? "");

        // replace_re - 正则替换
        obj.Import("replace_re", (string? s, string? pattern, string? replacement) =>
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(pattern))
                return s ?? "";
            return Regex.Replace(s, pattern, replacement ?? "");
        });
        obj.Import("replaceRE", (string? s, string? pattern, string? replacement) =>
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(pattern))
                return s ?? "";
            return Regex.Replace(s, pattern, replacement ?? "");
        });

        // split - 分割字符串
        obj.Import("split", (string? s, string? sep) =>
            s?.Split(sep ?? " ", StringSplitOptions.RemoveEmptyEntries) ?? []);

        // join - 连接字符串
        obj.Import("join", (IEnumerable<object>? items, string? sep) =>
            items != null ? string.Join(sep ?? "", items) : "");
        obj.Import("delimit", (IEnumerable<object>? items, string? sep) =>
            items != null ? string.Join(sep ?? "", items) : "");


        // has_prefix - 检查前缀
        obj.Import("has_prefix", (string? s, string? prefix) =>
            s?.StartsWith(prefix ?? "", StringComparison.Ordinal) ?? false);
        obj.Import("hasPrefix", (string? s, string? prefix) =>
            s?.StartsWith(prefix ?? "", StringComparison.Ordinal) ?? false);

        // has_suffix - 检查后缀
        obj.Import("has_suffix", (string? s, string? suffix) =>
            s?.EndsWith(suffix ?? "", StringComparison.Ordinal) ?? false);
        obj.Import("hasSuffix", (string? s, string? suffix) =>
            s?.EndsWith(suffix ?? "", StringComparison.Ordinal) ?? false);

        // contains - 检查包含
        obj.Import("contains", (string? s, string? substr) =>
            s?.Contains(substr ?? "", StringComparison.Ordinal) ?? false);

        // substr - 子字符串
        obj.Import("substr", (string? s, int start, int length) =>
        {
            if (string.IsNullOrEmpty(s))
                return "";
            if (start < 0)
                start = 0;
            if (start >= s.Length)
                return "";
            if (length < 0 || start + length > s.Length)
                length = s.Length - start;
            return s.Substring(start, length);
        });

        // repeat - 重复字符串
        obj.Import("repeat", (string? s, int count) =>
        {
            if (string.IsNullOrEmpty(s) || count <= 0)
                return "";
            return string.Concat(Enumerable.Repeat(s, count));
        });

        // count_words - 统计单词数
        obj.Import("count_words", (string? s) =>
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0;
            return s.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        });
        obj.Import("countwords", (string? s) =>
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0;
            return s.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        });

        // count_runes - 统计字符数
        obj.Import("count_runes", (string? s) => s?.Length ?? 0);
        obj.Import("countrunes", (string? s) => s?.Length ?? 0);

        // humanize - 人性化显示
        obj.Import("humanize", (string? s) =>
        {
            if (string.IsNullOrEmpty(s))
                return "";
            // 将 camelCase 或 snake_case 转为空格分隔
            var result = HumanizeRegex().Replace(s, " $1");
            result = result.Replace('_', ' ').Replace('-', ' ');
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(result.ToLowerInvariant());
        });

        // pluralize - 复数化
        obj.Import("pluralize", (int count, string? singular, string? plural) =>
            count == 1 ? singular ?? "" : plural ?? "");

        // singularize - 单数化（简单实现）
        obj.Import("singularize", (string? s) =>
        {
            if (string.IsNullOrEmpty(s))
                return "";
            if (s.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
                return s[..^3] + "y";
            if (s.EndsWith("es", StringComparison.OrdinalIgnoreCase))
                return s[..^2];
            if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return s[..^1];
            return s;
        });

        // urlize - URL 友好化
        obj.Import("urlize", (string? s) =>
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var result = s.ToLowerInvariant();
            result = UrlizeRegex().Replace(result, "-");
            result = MultiDashRegex().Replace(result, "-");
            return result.Trim('-');
        });

        // anchorize - 锚点友好化
        obj.Import("anchorize", (string? s) =>
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var result = s.ToLowerInvariant();
            result = AnchorizeRegex().Replace(result, "-");
            result = MultiDashRegex().Replace(result, "-");
            return result.Trim('-');
        });

        // emojify - 未提供（需要完整 emoji 短名映射表），不注册：调用报函数未定义

        // markdownify - Markdown 转 HTML
        obj.Import("markdownify", (string? s) =>
            string.IsNullOrEmpty(s) ? "" : MarkdownifyParser.ToHtml(s));

        // plainify - 去除 HTML 标签
        obj.Import("plainify", (string? s) =>
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return HtmlTagRegex().Replace(s, "");
        });

        // safeHTML - 标记为安全 HTML
        obj.Import("safeHTML", (string? s) => s ?? "");
        obj.Import("safe_html", (string? s) => s ?? "");
    }

    [GeneratedRegex(@"([A-Z])")]
    private static partial Regex HumanizeRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex UrlizeRegex();

    [GeneratedRegex(@"[^a-z0-9-]+")]
    private static partial Regex AnchorizeRegex();

    [GeneratedRegex(@"-+")]
    private static partial Regex MultiDashRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    #endregion


    #region 日期函数 (10+)

    private static void RegisterDateFunctions(ScriptObject obj)
    {
        // now - 当前时间
        obj.Import("now", () => DateTimeOffset.Now);

        // date_format - 格式化日期
        obj.Import("date_format", (object? date, string? format) =>
        {
            var dt = ToDateTimeOffset(date);
            return dt?.ToString(format ?? "yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        });
        obj.Import("dateFormat", (object? date, string? format) =>
        {
            var dt = ToDateTimeOffset(date);
            return dt?.ToString(format ?? "yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        });

        // time - 解析时间
        obj.Import("time", (string? s) =>
        {
            if (DateTimeOffset.TryParse(s, out var result))
                return result;
            return DateTimeOffset.MinValue;
        });

        // unix - Unix 时间戳
        obj.Import("unix", (object? date) =>
        {
            var dt = ToDateTimeOffset(date);
            return dt?.ToUnixTimeSeconds() ?? 0;
        });

        // duration - 时间间隔
        obj.Import("duration", (string? s) =>
        {
            if (TimeSpan.TryParse(s, out var result))
                return result;
            return TimeSpan.Zero;
        });

        // add_date - 添加日期
        obj.Import("add_date", (object? date, int years, int months, int days) =>
        {
            var dt = ToDateTimeOffset(date);
            return dt?.AddYears(years).AddMonths(months).AddDays(days);
        });

        // sub_date - 减去日期
        obj.Import("sub_date", (object? date, int years, int months, int days) =>
        {
            var dt = ToDateTimeOffset(date);
            return dt?.AddYears(-years).AddMonths(-months).AddDays(-days);
        });

        // date_modify - 修改日期
        obj.Import("date_modify", (object? date, string? modifier) =>
        {
            var dt = ToDateTimeOffset(date);
            if (dt == null || string.IsNullOrEmpty(modifier))
                return dt;

            // 解析修改器，如 "+1 day", "-2 months"
            var match = Regex.Match(modifier, @"([+-]?\d+)\s*(\w+)");
            if (!match.Success)
                return dt;

            // 超长数字串 TryParse 失败时回退原日期（与本函数"解析失败静默回退"语义一致）
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                return dt;
            var unit = match.Groups[2].Value.ToLowerInvariant();

            return unit switch
            {
                "year" or "years" => dt.Value.AddYears(amount),
                "month" or "months" => dt.Value.AddMonths(amount),
                "day" or "days" => dt.Value.AddDays(amount),
                "hour" or "hours" => dt.Value.AddHours(amount),
                "minute" or "minutes" => dt.Value.AddMinutes(amount),
                "second" or "seconds" => dt.Value.AddSeconds(amount),
                _ => dt
            };
        });

        // is_future - 是否为未来日期
        obj.Import("is_future", (object? date) =>
        {
            var dt = ToDateTimeOffset(date);
            return dt > DateTimeOffset.Now;
        });

        // is_past - 是否为过去日期
        obj.Import("is_past", (object? date) =>
        {
            var dt = ToDateTimeOffset(date);
            return dt < DateTimeOffset.Now;
        });
    }

    private static DateTimeOffset? ToDateTimeOffset(object? value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s when DateTimeOffset.TryParse(s, out var result) => result,
            long unix => DateTimeOffset.FromUnixTimeSeconds(unix),
            _ => null
        };
    }

    #endregion

    #region 集合函数 (20+)

    private static void RegisterCollectionFunctions(ScriptObject obj)
    {
        // len - 获取长度
        obj.Import("len", (object? collection) =>
        {
            return collection switch
            {
                string s => s.Length,
                IList<ScriptObject> list => list.Count,  // 优先处理 LazyPageList
                ICollection<object> c => c.Count,
                IEnumerable<object> e => e.Count(),
                System.Collections.ICollection c => c.Count,
                System.Collections.IEnumerable e => e.Cast<object>().Count(),
                _ => 0
            };
        });

        // first/last/index/slice/after：统一接收 `object?` 而非 `IEnumerable<object>`。
        // 原因（实测确认）：Scriban 绑定**第一个**注册的重载且类型不符时直接抛异常，
        // 不做重载回退——严格形参把 Hugo 的 `first N SEQ`（首参是 int）、
        // `in page.kind "term"`（首参是 string）全打成
        // "Unable to convert type `int` to `IEnumerable<Object>`"（Ananke 17 处、
        // Stack 12 处命中）。改为宽松形参后在函数体内做形态判定。

        // first/last：单参取首/末元素（Scriban `SEQ | first`），双参取前/后 N 项
        // 子序列（Hugo 前缀 `first N SEQ` 与 Scriban 管道 `SEQ | first N` 皆可）
        obj.Import("first", (params object?[] a) => SeqFirstLast(a, fromEnd: false));
        obj.Import("last", (params object?[] a) => SeqFirstLast(a, fromEnd: true));

        // index：SEQ INDEX 与 MAP KEY（Hugo index 双语义）
        obj.Import("index", (params object?[] a) => SeqIndex(a));

        // slice：Flint 序列切片（slice SEQ START [LEN]）与 Hugo 可变参数构造器
        //（slice / slice A / slice A B），按首参是否为集合区分
        obj.Import("slice", (params object?[] a) => SeqSlice(a));

        // after：SEQ N / N SEQ 双形态
        obj.Import("after", (params object?[] a) => SeqAfter(a));

        // complement - 补集
        obj.Import("complement", (IEnumerable<object>? a, IEnumerable<object>? b) =>
        {
            if (a == null)
                return Enumerable.Empty<object>();
            if (b == null)
                return a;
            var bSet = b.ToHashSet();
            return a.Where(x => !bSet.Contains(x));
        });

        // intersect - 交集
        obj.Import("intersect", (IEnumerable<object>? a, IEnumerable<object>? b) =>
        {
            if (a == null || b == null)
                return Enumerable.Empty<object>();
            return a.Intersect(b);
        });

        // union - 并集
        obj.Import("union", (IEnumerable<object>? a, IEnumerable<object>? b) =>
        {
            if (a == null)
                return b ?? Enumerable.Empty<object>();
            if (b == null)
                return a;
            return a.Union(b);
        });

        // uniq - 去重
        obj.Import("uniq", (IEnumerable<object>? collection) =>
            collection?.Distinct() ?? Enumerable.Empty<object>());

        // shuffle - 随机排序
        obj.Import("shuffle", (IEnumerable<object>? collection) =>
        {
            if (collection == null)
                return Enumerable.Empty<object>();
            var list = collection.ToList();
            var rng = Random.Shared;
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        });

        // reverse - 反转
        obj.Import("reverse", (IEnumerable<object>? collection) =>
            collection?.Reverse() ?? Enumerable.Empty<object>());

        // sort - 排序
        // sort - 单参（Hugo `sort SEQ`）或带键（`sort SEQ KEY` / `sort SEQ KEY ORDER`）。
        // 此前严格双参形参，Hugo 的单参/三参形态会报参数数错误
        obj.Import("sort", (params object?[] a) =>
        {
            if (a.Length == 0)
            {
                return Enumerable.Empty<object>();
            }
            var collection = ToObjectSeq(a[0]);
            var key = a.Length > 1 ? a[1]?.ToString() : null;
            var desc = a.Length > 2 &&
                string.Equals(a[2]?.ToString(), "desc", StringComparison.OrdinalIgnoreCase);
            // 排序键统一投影为可比较的字符串：ScriptObject 等类型直接 OrderBy
            // 会抛 "Failed to compare two elements in the array"（hugo-book/hugo-coder 实测）。
            // 页面对象按 Hugo 默认排序规则投影（Weight → Date → Title → 路径），
            // 其余按 Ordinal 字符串——不抛异常且顺序稳定
            if (string.IsNullOrEmpty(key))
            {
                var byDefault = collection.OrderBy(SortKeyOf, StringComparer.Ordinal);
                return (desc ? byDefault.Reverse() : byDefault).Cast<object>().ToList();
            }

            var sorted = collection.OrderBy(
                x => SortKeyOf(GetMember(x, key)), StringComparer.Ordinal);
            return (desc ? sorted.Reverse() : sorted).Cast<object>().ToList();
        });

        // group - 分组
        obj.Import("group", (IEnumerable<object>? collection, string? key) =>
        {
            if (collection == null || string.IsNullOrEmpty(key))
                return new Dictionary<object, List<object>>();

            return collection.GroupBy(x =>
            {
                if (x is ScriptObject so && so.TryGetValue(key, out var value))
                    return value ?? "";
                return "";
            }).ToDictionary(g => g.Key, g => g.ToList());
        });


        // where - 过滤
        // where（3 参基础形态）——4 参 operator 形态由 Hugo 兼容组覆盖注册
        // Hugo where 支持两种形态：
        //   where COLLECTION KEY VALUE
        //   where COLLECTION KEY OPERATOR VALUE（operator: in/not in/==/!=/>=/<=/>/</like）
        // 用两个重载无法区分（Scriban 按元数匹配），故用 params 收参后内部分派
        obj.Import("where", (object? seq, object? key, params object?[] rest) =>
            WhereSeqOp(seq, key?.ToString() ?? "", rest));

        // append - 追加元素
        obj.Import("append", (Func<object?, object?, object?>)((collection, item) =>
        {
            // Hugo 语义：append 对**字符串**做拼接（$s = $s | append "x" 的常见用法），
            // 对序列做追加。此前签名限定 IEnumerable，字符串初值的 $body_classes
            // 会报 "Unable to convert type string to IEnumerable<Object>"（Ananke 实证）
            if (collection is string s)
            {
                return s + (item?.ToString() ?? "");
            }
            if (collection is null)
            {
                return item != null ? new object[] { item } : Array.Empty<object>();
            }
            if (collection is System.Collections.IEnumerable en)
            {
                var list = en.Cast<object?>().ToList();
                if (item != null)
                {
                    list.Add(item);
                }
                return list;
            }
            return item != null ? new[] { collection, item } : new[] { collection };
        }));

        // prepend - 前置元素
        obj.Import("prepend", (IEnumerable<object>? collection, object? item) =>
        {
            if (collection == null)
                return item != null ? new[] { item } : [];
            return item != null ? collection.Prepend(item) : collection;
        });

        // seq - 生成序列
        obj.Import("seq", (int start, int end, int? step) =>
        {
            var s = step ?? 1;
            if (s == 0)
                return Enumerable.Empty<int>();
            if (s > 0)
                return Enumerable.Range(0, (end - start) / s + 1).Select(i => start + i * s);
            else
                return Enumerable.Range(0, (start - end) / (-s) + 1).Select(i => start + i * s);
        });

        // range - 生成范围
        obj.Import("range", (int count) => Enumerable.Range(0, count));

        // in - 检查是否在集合中（Hugo 语义：haystack 是字符串时按子串，
        // 是序列时按元素）。形参用 object? 不用 IEnumerable——严格形参会被
        // Scriban 首个重载绑定，把 `in page.kind "term"`（haystack 为字符串）
        // 打成 "Unable to convert type `string` to `IEnumerable<Object>`"（Stack 实测）
        obj.Import("in", (object? item, object? collection) => InSeq(item, collection));

        // apply - 应用函数到每个元素
        obj.Import("apply", (IEnumerable<object>? collection, Func<object, object>? func) =>
        {
            if (collection == null || func == null)
                return collection ?? Enumerable.Empty<object>();
            return collection.Select(func);
        });
    }

    #endregion

    #region URL 函数 (10+)

    private void RegisterUrlFunctions(ScriptObject obj)
    {
        // absURL - 绝对 URL
        obj.Import("absURL", (string? path) =>
        {
            if (string.IsNullOrEmpty(path))
                return _baseUrl;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;
            return _baseUrl + "/" + path.TrimStart('/');
        });
        obj.Import("abs_url", (string? path) =>
        {
            if (string.IsNullOrEmpty(path))
                return _baseUrl;
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;
            return _baseUrl + "/" + path.TrimStart('/');
        });

        // relURL - 相对 URL
        obj.Import("relURL", (string? path) =>
        {
            if (string.IsNullOrEmpty(path))
                return "/";
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;
            return "/" + path.TrimStart('/');
        });
        obj.Import("rel_url", (string? path) =>
        {
            if (string.IsNullOrEmpty(path))
                return "/";
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;
            return "/" + path.TrimStart('/');
        });

        // safeURL - 安全 URL
        obj.Import("safeURL", (string? url) => url ?? "");
        obj.Import("safe_url", (string? url) => url ?? "");

        // ref / relref - 页面引用（简化实现：仅路径规范化，不做页面查找；
        // 完整实现需要页面索引，见任务清单 T2.1 页面树）
        obj.Import("ref", (string? path) => "/" + (path?.TrimStart('/') ?? ""));
        obj.Import("relref", (string? path) => "/" + (path?.TrimStart('/') ?? ""));

        // urlquery - URL 查询参数编码
        obj.Import("urlquery", (string? s) => HttpUtility.UrlEncode(s ?? ""));
        obj.Import("querify", (params object[] args) =>
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Length - 1; i += 2)
            {
                if (sb.Length > 0)
                    sb.Append('&');
                sb.Append(HttpUtility.UrlEncode(args[i]?.ToString() ?? ""));
                sb.Append('=');
                sb.Append(HttpUtility.UrlEncode(args[i + 1]?.ToString() ?? ""));
            }
            return sb.ToString();
        });

        // path.Base - 获取路径基名
        obj.Import("path_base", (string? path) =>
            Path.GetFileName(path ?? ""));

        // path.Dir - 获取目录
        obj.Import("path_dir", (string? path) =>
            Path.GetDirectoryName(path ?? "") ?? "");

        // path.Ext - 获取扩展名
        obj.Import("path_ext", (string? path) =>
            Path.GetExtension(path ?? ""));

        // path.Join - 连接路径
        obj.Import("path_join", (params string[] paths) =>
            string.Join("/", paths.Where(p => !string.IsNullOrEmpty(p))));

        // path.Clean - 清理路径
        obj.Import("path_clean", (string? path) =>
        {
            if (string.IsNullOrEmpty(path))
                return "";
            return path.Replace("//", "/").TrimEnd('/');
        });
    }

    #endregion

    #region 数学函数 (15+)

    private static void RegisterMathFunctions(ScriptObject obj)
    {
        // add - 加法
        obj.Import("add", (object? a, object? b) => ToNum(a) + ToNum(b));

        // sub - 减法
        obj.Import("sub", (object? a, object? b) => ToNum(a) - ToNum(b));

        // mul - 乘法
        obj.Import("mul", (object? a, object? b) => ToNum(a) * ToNum(b));

        // div - 除法
        obj.Import("div", (object? a, object? b) => ToNum(b) == 0 ? 0d : ToNum(a) / ToNum(b));

        // mod - 取模
        obj.Import("mod", (int a, int b) => b != 0 ? a % b : 0);
        obj.Import("modBool", (int a, int b) => b != 0 && a % b == 0);

        // ceil - 向上取整
        // 数学函数：参数用 object + ToNum 归一（模板变量常来自 front matter 字符串），
        // 且按 Hugo 语义接受可选第二参（math.Round 单参即可，无需显式精度）
        obj.Import("ceil", (object? n) => Math.Ceiling(ToNum(n)));

        // floor - 向下取整
        obj.Import("floor", (object? n) => Math.Floor(ToNum(n)));

        // round - 四舍五入（第二参精度可选，对齐 Hugo math.Round）
        obj.Import("round", (object? n, object? decimals = null) =>
            Math.Round(ToNum(n), decimals is null ? 0 : (int)ToNum(decimals)));

        // abs - 绝对值
        obj.Import("abs", (object? n) => Math.Abs(ToNum(n)));

        // max - 最大值
        obj.Import("max", (params object?[] nums) =>
            nums.Length > 0 ? nums.Select(ToNum).Max() : 0d);

        // min - 最小值
        obj.Import("min", (params object?[] nums) =>
            nums.Length > 0 ? nums.Select(ToNum).Min() : 0d);

        // pow - 幂运算
        obj.Import("pow", (object? @base, object? exp) => Math.Pow(ToNum(@base), ToNum(exp)));

        // sqrt - 平方根
        obj.Import("sqrt", (object? n) => Math.Sqrt(ToNum(n)));

        // log - 对数
        obj.Import("log", (object? n) => Math.Log(ToNum(n)));

        // counter - 计数器（全局递增；并行渲染下必须原子递增）
        var counter = 0L;
        obj.Import("counter", () => Interlocked.Increment(ref counter));
    }

    #endregion


    #region 编码函数 (10+)

    private static void RegisterEncodingFunctions(ScriptObject obj)
    {
        // base64Encode - Base64 编码
        obj.Import("base64Encode", (string? s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")));
        obj.Import("base64_encode", (string? s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")));

        // base64Decode - Base64 解码
        obj.Import("base64Decode", (string? s) =>
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(s ?? ""));
            }
            catch
            {
                return "";
            }
        });
        obj.Import("base64_decode", (string? s) =>
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(s ?? ""));
            }
            catch
            {
                return "";
            }
        });

        // htmlEscape - HTML 转义
        obj.Import("htmlEscape", (string? s) =>
            HtmlEncoder.Default.Encode(s ?? ""));
        obj.Import("html_escape", (string? s) =>
            HtmlEncoder.Default.Encode(s ?? ""));

        // htmlUnescape - HTML 反转义
        obj.Import("htmlUnescape", (string? s) =>
            HttpUtility.HtmlDecode(s ?? ""));
        obj.Import("html_unescape", (string? s) =>
            HttpUtility.HtmlDecode(s ?? ""));

        // jsonify - JSON 序列化
        // jsonify 序列化的是运行时任意对象（模板变量），类型无法静态已知，
        // source-gen 不适用；NativeAOT 下仅此函数受限（异常时返回 "null"）
        obj.Import("jsonify", (object? value) =>
        {
            try
            {
                return JsonSerializer.Serialize(value, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }
            catch
            {
                return "null";
            }
        });

        // md5 - MD5 哈希
        obj.Import("md5", (string? s) =>
        {
            var bytes = MD5.HashData(Encoding.UTF8.GetBytes(s ?? ""));
            return Convert.ToHexStringLower(bytes);
        });

        // sha1 - SHA1 哈希
        obj.Import("sha1", (string? s) =>
        {
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s ?? ""));
            return Convert.ToHexStringLower(bytes);
        });

        // sha256 - SHA256 哈希
        obj.Import("sha256", (string? s) =>
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
            return Convert.ToHexStringLower(bytes);
        });
    }

    #endregion

    #region 比较函数 (10+)

    private static void RegisterComparisonFunctions(ScriptObject obj)
    {
        // eq - 相等。Hugo 的 eq 是**可变参数**：`eq X a b c` 表示 X 等于
        // 其中任一（Ananke single.html 用 `compare.Eq $page.Language "de" "en" ...`
        // 列出语言集，严格双参形参报 "Argument index must be < 2"）
        obj.Import("eq", (params object?[] a) =>
        {
            if (a.Length < 2)
            {
                return a.Length == 0;
            }
            for (var i = 1; i < a.Length; i++)
            {
                if (Equals(a[0], a[i]))
                {
                    return true;
                }
            }
            return false;
        });

        // ne - 不相等（Hugo 双参语义；可变参数形态 Hugo 未定义，取首两个）
        obj.Import("ne", (object? a, object? b) => !Equals(a, b));

        // lt - 小于
        obj.Import("lt", (IComparable? a, IComparable? b) =>
            a != null && b != null && a.CompareTo(b) < 0);

        // le - 小于等于
        obj.Import("le", (IComparable? a, IComparable? b) =>
            a != null && b != null && a.CompareTo(b) <= 0);

        // gt - 大于
        obj.Import("gt", (IComparable? a, IComparable? b) =>
            a != null && b != null && a.CompareTo(b) > 0);

        // ge - 大于等于
        obj.Import("ge", (IComparable? a, IComparable? b) =>
            a != null && b != null && a.CompareTo(b) >= 0);

        // and - 逻辑与
        obj.Import("and", (params bool[] values) => values.All(v => v));

        // or - 逻辑或
        obj.Import("or", (params bool[] values) => values.Any(v => v));

        // not - 逻辑非
        obj.Import("not", (bool value) => !value);

        // default - 默认值。Hugo 的签名是 variadic：`default DEFAULT [GIVEN...]`，
        // 只给一个参数时返回它自身（主题实测 `{{ $scope := default nil }}`——
        // 严格双参形参会报 "Invalid number of arguments 1 passed to `default null`"）。
        // 两参形态服务于管道：Scriban 的 `|` 把左值注入**首参**，故
        // `X | default Y` → (X, Y) → 取 X 若非空，否则 Y（与 Hugo 管道语义一致）；
        // 非管道形态 `default Y X` 的参数顺序由转换器重排（见 ScribanConverter）
        obj.Import("default", (params object?[] a) => a.Length switch
        {
            0 => null,
            1 => a[0],
            _ => a[0] ?? a[1]
        });

        // cond - 条件表达式
        obj.Import("cond", (bool condition, object? trueValue, object? falseValue) =>
            condition ? trueValue : falseValue);

        // isset - Hugo 双形态：`isset MAP KEY`（键/索引存在性）与 `isset VALUE`（非空）。
        // 此前只注册单参形态 → 主题的 `{{ if isset site.Params "twitter" }}` 报
        // "Argument index must be < 1 (Parameter 'index')"（Scriban 绑定单参函数却收到
        // 两个实参时在参数表访问越界）。这是 20 主题矩阵最高频的阻断原因（9 个主题命中）
        obj.Import("isset", (params object?[] a) =>
        {
            if (a.Length == 0)
            {
                return false;
            }
            if (a.Length == 1)
            {
                return a[0] is not null;
            }

            var target = a[0];
            var key = a[1]?.ToString() ?? "";
            return target switch
            {
                ScriptObject so => so.ContainsKey(key),
                System.Collections.IDictionary d => d.Contains(key),
                // 集合按整数下标判定（Hugo：isset $arr 0）
                System.Collections.IList list => int.TryParse(key, out var idx) &&
                                                  idx >= 0 && idx < list.Count,
                _ => false
            };
        });

        // isempty - 检查是否为空
        obj.Import("isempty", (object? value) =>
        {
            return value switch
            {
                null => true,
                string s => string.IsNullOrEmpty(s),
                ICollection<object> c => c.Count == 0,
                System.Collections.ICollection c => c.Count == 0,
                _ => false
            };
        });
    }

    #endregion

    #region 调试函数 (5+)

    private static void RegisterDebugFunctions(ScriptObject obj)
    {
        // printf - 格式化输出。**同时接受 Go 与 .NET 两种占位符**：
        // Hugo 模板写 Go 动词（`%s`/`%d`/`%v`/`%q`…，Hugo 全生态通用），
        // 而 Flint 早期只支持 .NET 的 `{0}`——混用时 Go 格式串不被替换，
        // 直接原样输出（Stack 的 `printf "icons/%s.svg" .` 产出字面量
        // "icons/%s.svg" → 图标查找失败，实测 9 处）。
        // 含 `%` 动词时按 Go 语义转换，否则走 .NET 原路径
        obj.Import("printf", (string? format, params object[] args) =>
        {
            var f = format ?? "";
            try
            {
                if (f.Contains('%', StringComparison.Ordinal))
                {
                    return string.Format(CultureInfo.InvariantCulture, GoFormatToDotNet(f), args);
                }
                return string.Format(CultureInfo.InvariantCulture, f, args);
            }
            catch
            {
                return f;
            }
        });

        // print - 打印
        obj.Import("print", (params object[] args) =>
            string.Join(" ", args.Select(a => a?.ToString() ?? "")));

        // warnf - 警告输出到标准错误（构建可见）
        obj.Import("warnf", (string? format, params object[] args) =>
        {
            Console.Error.WriteLine(FormatMessage(format, args));
            return "";
        });

        // errorf - 中止渲染（对齐 Hugo：errorf 使构建失败）；
        // 异常经 ScribanTemplateRenderer 包装为 TemplateRenderException → BuildError。
        // params 与 printf/warnf 对齐：裸 object[] 时 Scriban 按严格绑定处理，带参调用报参数错误
        obj.Import("errorf", (string? format, params object[] args) =>
        {
            throw new InvalidOperationException(FormatMessage(format, args));
        });

        // debug - 调试输出
        obj.Import("debug", (object? value) =>
            $"<!-- DEBUG: {value} -->");

        // typeof - 获取类型
        obj.Import("typeof", (object? value) =>
            value?.GetType().Name ?? "null");
    }

    /// <summary>
    /// Go printf 动词 → .NET 复合格式转换。Go 模板生态通用动词：
    /// <c>%s/%v/%d/%t/%f</c>→<c>{n}</c>、<c>%q</c>→<c>"{n}"</c>、
    /// <c>%x</c>→<c>{n:x}</c>、<c>%%</c>→<c>%</c>；宽度/精度修饰透传为
    /// .NET 形式（<c>%02d</c>→<c>{n:D2}</c>、<c>%.2f</c>→<c>{n:F2}</c>）。
    /// 未知动词按 <c>{n}</c> 兜底
    /// </summary>
    private static string GoFormatToDotNet(string format)
    {
        var sb = new StringBuilder(format.Length + 8);
        var argIndex = 0;
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                sb.Append(format[i]);
                continue;
            }

            i++;
            if (format[i] == '%')
            {
                sb.Append('%');
                continue;
            }

            var modStart = i;
            while (i < format.Length &&
                   (char.IsDigit(format[i]) || format[i] is '-' or '+' or '#' or ' ' or '.' or '*'))
            {
                i++;
            }
            if (i >= format.Length)
            {
                break;
            }

            var verb = format[i];
            var mod = format[modStart..i];
            var n = argIndex++;
            switch (verb)
            {
                case 's' or 'v' or 'd' or 'i' or 't' or 'f' or 'g' or 'e' or 'b' or 'o':
                    sb.Append('{').Append(n);
                    AppendGoWidth(sb, mod);
                    sb.Append('}');
                    break;
                case 'q':
                    sb.Append("\"{").Append(n);
                    AppendGoWidth(sb, mod);
                    sb.Append("}\"");
                    break;
                case 'x':
                    sb.Append('{').Append(n).Append(":x}");
                    break;
                case 'X':
                    sb.Append('{').Append(n).Append(":X}");
                    break;
                default:
                    sb.Append('{').Append(n).Append('}');
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Go 宽度/精度修饰 → .NET 格式后缀（%02d → :D2；%.2f → :F2）</summary>
    private static void AppendGoWidth(StringBuilder sb, string mod)
    {
        if (mod.Length == 0)
        {
            return;
        }
        var dot = mod.IndexOf('.');
        if (dot >= 0 && int.TryParse(mod[(dot + 1)..], out var precision))
        {
            sb.Append(":F").Append(precision);
            return;
        }
        if (mod.Contains('0', StringComparison.Ordinal) &&
            int.TryParse(mod.Replace("-", ""), out var width))
        {
            sb.Append(":D").Append(width);
        }
    }

    #endregion

    #region 安全函数 (5+)

    private static void RegisterSafeFunctions(ScriptObject obj)
    {
        // safe* 系列在 Scriban 语境下为恒等：Scriban 不对输出做自动 HTML 转义，
        // "标记为安全"即原文输出——行为正确，保留注册以兼容 Hugo 模板
        // safeCSS - 安全 CSS
        obj.Import("safeCSS", (string? s) => s ?? "");
        obj.Import("safe_css", (string? s) => s ?? "");

        // safeJS - 安全 JavaScript
        obj.Import("safeJS", (string? s) => s ?? "");
        obj.Import("safe_js", (string? s) => s ?? "");

        // safeHTMLAttr - 安全 HTML 属性（safe* 系列恒等语义，与 Hugo 一致：
        // 标记值"已安全"以免被二次转义，不做实际编码）
        obj.Import("safeHTMLAttr", (string? s) => s ?? "");
        obj.Import("safe_html_attr", (string? s) => s ?? "");
    }

    #endregion

    #region 资源函数 (5+)

    private void RegisterResourceFunctions(ScriptObject obj)
    {
        // fingerprint - 资源指纹
        obj.Import("fingerprint", (string? path) =>
        {
            if (string.IsNullOrEmpty(path))
                return "";
            // 简单实现：添加查询参数
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
            var shortHash = Convert.ToHexStringLower(hash)[..8];
            return path.Contains('?') ? $"{path}&v={shortHash}" : $"{path}?v={shortHash}";
        });

        // resources.Get / resources.Match / resources.GetMatch / minify 未提供
        // （依赖资源对象模型），不注册：调用报函数未定义而非静默空转
    }

    /// <summary>
    /// Hugo 主题兼容函数组（B6~B12）：内容处理、URL 处理、集合操作、构造合并、参数查询。
    /// 命名与参数序保持 Hugo 一致以便主题模板直接迁移；声明为 static 便于独立测试
    /// </summary>
    private static void RegisterHugoCompatFunctions(ScriptObject obj)
    {
        // ---- B9 内容处理 ----
        obj.Import("markdownify", (string? text) => MarkdownifyInline(text ?? ""));
        obj.Import("plainify", (string? text) =>
            string.IsNullOrEmpty(text) ? "" : Regex.Replace(text, "<[^>]+>", ""));
        obj.Import("emojify", (string? text) => Emojify(text ?? ""));
        obj.Import("htmlUnescape", (string? text) =>
            string.IsNullOrEmpty(text) ? "" : System.Net.WebUtility.HtmlDecode(text));
        obj.Import("htmlEscape", (string? text) =>
            string.IsNullOrEmpty(text) ? "" : System.Net.WebUtility.HtmlEncode(text));

        // ---- B10 URL 处理（Hugo 前缀函数语义）----
        obj.Import("urlize", (string? text) => Urlize(text ?? ""));
        obj.Import("anchorize", (string? text) => Urlize(text ?? ""));
        obj.Import("humanize", (string? text) => Humanize(text ?? ""));
        obj.Import("singularize", (string? text) => Singularize(text ?? ""));

        // ---- B7 集合操作 ----
        // 注意：first/last/uniq/shuffle/index/slice/after/in 已在上面注册
        // （宽松形参 + 形态分派，Hugo 前缀形态与 Scriban 管道形态皆可用），
        // 此处**不重复注册**（Scriban 绑定首个注册的重载，重复注册是死代码）。
        // 真正新增的集合函数（Hugo 独有）：
        obj.Import("where", (object? seq, object? key, object? value) =>
            WhereSeq(seq, key?.ToString() ?? "", value));
        obj.Import("sortBy", (object? seq, object? key) => SortSeq(seq, key?.ToString()));

        // ---- B8 构造与合并 ----
        // 注意：slice/index/sort 已有 Scriban 风格注册（含区间切片与字段排序），不重复注册。
        // 真正新增（Hugo 独有）：
        obj.Import("dict", (params object?[] args) => BuildDict(args));
        obj.Import("merge", (params object?[] args) => MergeDicts(args));

        // newScratch / newScratch 别名：Hugo 的独立暂存构造器（区别于页面级 .Scratch）。
        // Scriban 对零参函数自动求值，故模板里的 `{{ $s = newScratch }}` 会即时
        // 得到**全新** PageStoreObject（每次访问新建，不共享——PaperMod 实测用法）
        obj.Import("newScratch", () => new PageStoreObject());
        obj.Import("new_scratch", () => new PageStoreObject());

        // is_menu_current / has_menu_current：Hugo 页面方法 IsMenuCurrent/HasMenuCurrent
        // 的全局形态。二者都问"当前页是否这个菜单项（或其后代）指向的页"——
        // 菜单项的 is_active 在构建期已按当前页路径算出，故此处直接读该标志，
        // 无需回传菜单集合（Hugo 首参 MENUNAME 仅为对称性保留）。
        // has_menu_current 额外向下递归子项（Hugo 语义：菜单项是高亮页的祖先时亦为真）
        obj.Import("is_menu_current", (object? menu, object? entry) => MenuEntryActive(entry, false));
        obj.Import("has_menu_current", (object? menu, object? entry) => MenuEntryActive(entry, true));

        // ---- B12 参数点路径查询 ----
        obj.Import("paramLookup", (object? ctx, string path) => ParamLookup(ctx, path));
    }

    /// <summary>markdownify 行内受限实现（行内代码/粗体/斜体/链接——主题最常用形态）</summary>
    private static string MarkdownifyInline(string text)
    {
        if (text.Length == 0)
            return "";
        var html = Regex.Replace(text, "`([^`]+)`", "<code>$1</code>");
        html = Regex.Replace(html, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
        html = Regex.Replace(html, @"\*([^*]+)\*", "<em>$1</em>");
        html = Regex.Replace(html, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\">$1</a>");
        return html;
    }

    private static readonly Dictionary<string, string> EmojiMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["smile"] = "😄", ["heart"] = "❤️", ["thumbsup"] = "👍", ["+1"] = "👍",
        ["tada"] = "🎉", ["rocket"] = "🚀", ["fire"] = "🔥", ["star"] = "⭐",
        ["warning"] = "⚠️", ["bulb"] = "💡", ["book"] = "📖",
        ["check"] = "✅", ["x"] = "❌", ["sparkles"] = "✨", ["eyes"] = "👀"
    };

    private static string Emojify(string text) =>
        Regex.Replace(text, @":([a-z0-9_+\-]+):", m =>
            EmojiMap.TryGetValue(m.Groups[1].Value, out var emoji) ? emoji : m.Value);

    /// <summary>urlize/anchorize：小写、非字母数字转连字符、去首尾连字符（保留非 ASCII）</summary>
    private static string Urlize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastDash = false;
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch > 127)
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }

    /// <summary>humanize：连字符/下划线转空格并首字母大写（Hugo 语义）</summary>
    private static string Humanize(string text)
    {
        if (text.Length == 0)
            return "";
        var spaced = text.Replace('-', ' ').Replace('_', ' ');
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    private static string Singularize(string text) =>
        text.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && text.Length > 3
            ? text[..^3] + "y"
            : text.EndsWith('s') && !text.EndsWith("ss", StringComparison.OrdinalIgnoreCase)
                ? text[..^1]
                : text;

    private static IReadOnlyList<object?> ToList(object? seq) => seq switch
    {
        null => [],
        string => [seq],
        // 列表型 ScriptObject（LazyPageList 等）优先按**序列**展开：
        // 它实现 IList<ScriptObject> 也实现 IDictionary，字典分支会把它当单元素
        IList<ScriptObject> objList => objList.Cast<object?>().ToList(),
        // 其余字典仍视作单元素（Hugo range 对 map 语义特殊，此处保守）
        System.Collections.IDictionary => [seq],
        System.Collections.IEnumerable e => e.Cast<object?>().ToList(),
        _ => [seq]
    };

    /// <summary>是否为集合形态（非字符串/非字典的 IEnumerable）</summary>
    private static bool IsCollection(object? value) =>
        value is System.Collections.IEnumerable and not string and not System.Collections.IDictionary;

    /// <summary>是否为数值形态（数值或数字字符串）——用于把"计数"参数与"序列"参数分开</summary>
    private static bool IsNumericLike(object? value)
    {
        if (value is null || value is bool || value is string and not { Length: > 0 })
        {
            return false;
        }
        if (value is int or long or double or float or decimal or short or byte)
        {
            return true;
        }
        return value is string s &&
               double.TryParse(s, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// 从两个参数中分出（序列, 计数）。**用"哪个不是数值"判定序列**，而非
    /// <c>IsCollection</c>——页面集合是 <c>LazyPageList : ScriptObject</c>，
    /// 而 <c>ScriptObject</c> 实现 <c>IDictionary</c>，会被 <c>IsCollection</c>
    /// 判为字典（实测：`site.pages | slice 0 2` 误走构造器分支，产出 3 元素数组）
    /// </summary>
    private static (object? Seq, int Count) SplitSeqCount(object? a, object? b) =>
        IsNumericLike(a) && !IsNumericLike(b)
            ? (b, ToInt(a))
            : (a, ToInt(b));

    /// <summary>in：needle 是否在 haystack 中（Hugo in 语义，字符串按子串、集合按元素）</summary>
    private static bool InSeq(object? needle, object? haystack)
    {
        if (haystack is string text)
        {
            return text.Contains(needle?.ToString() ?? "", StringComparison.Ordinal);
        }
        foreach (var item in ToList(haystack))
        {
            if (string.Equals(item?.ToString(), needle?.ToString(), StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>从两参中挑出集合（Hugo 前缀形态与管道形态的顺序自适配）</summary>
    private static object? PickSeq(object? p1, object? p2) => IsCollection(p1) ? p1 : p2;


    private static object SliceSeq(object? seq, int start, int count)
    {
        var list = ToList(seq);
        var begin = start < 0 ? Math.Max(0, list.Count + start) : Math.Min(start, list.Count);
        var take = Math.Min(Math.Max(0, count), list.Count - begin);
        return list.Skip(begin).Take(take).ToList();
    }

    /// <summary>
    /// first/last：单参取首/末元素，双参取前/后 N 项子序列。
    /// 参数顺序自适应——Hugo 前缀形态是 <c>first N SEQ</c>（int 在前），
    /// Scriban 管道形态是 <c>SEQ | first N</c>（集合在前）
    /// </summary>
    private static object? SeqFirstLast(object?[] args, bool fromEnd)
    {
        if (args.Length == 0)
        {
            return null;
        }
        if (args.Length == 1)
        {
            var one = ToList(args[0]);
            if (one.Count == 0)
            {
                return null;
            }
            return fromEnd ? one[^1] : one[0];
        }

        // 双参：分出集合与计数（用数值位判定，兼容 LazyPageList 等对象型集合）
        var (seq, count) = SplitSeqCount(args[0], args[1]);
        var list = ToList(seq);
        if (count <= 0)
        {
            return new List<object?>();
        }
        return fromEnd ? list.Skip(Math.Max(0, list.Count - count)).ToList()
                       : list.Take(count).ToList();
    }

    /// <summary>
    /// index：Hugo 双语义——集合按整数下标取值，映射按字符串键取值。
    /// 越界/缺键返回 null（Hugo 宽容语义），不再抛 ArgumentOutOfRange
    /// </summary>
    private static object? SeqIndex(object?[] args)
    {
        if (args.Length < 2)
        {
            return null;
        }
        var target = args[0];
        var key = args[1];

        if (target is string text)
        {
            var idx = ToInt(key);
            return idx >= 0 && idx < text.Length ? text[idx].ToString() : null;
        }

        if (target is System.Collections.IDictionary dict)
        {
            return dict.Contains(key?.ToString() ?? "") ? dict[key?.ToString() ?? ""] : null;
        }

        if (target is ScriptObject so)
        {
            return so.TryGetValue(null, default, key?.ToString() ?? "", out var v) ? v : null;
        }

        if (target is System.Collections.IEnumerable e and not string)
        {
            var list = e.Cast<object?>().ToList();
            var idx = ToInt(key);
            return idx >= 0 && idx < list.Count ? list[idx] : null;
        }

        return null;
    }

    /// <summary>
    /// slice：Flint 序列切片（<c>slice SEQ START [LEN]</c>）与 Hugo 可变参数
    /// 构造器（<c>slice</c>/<c>slice A</c>/<c>slice A B</c>）。
    /// 首参是集合时按切片处理，否则按构造器（Hugo 语义，LoveIt 用零参 slice 造空序列）
    /// </summary>
    private static object SeqSlice(object?[] args)
    {
        // 切片形态判定：第二参是**数值**则为 `slice SEQ START [LEN]`；否则按 Hugo
        // 可变参数构造器（`slice` / `slice A` / `slice A B`，如 LoveIt 的
        // `slice "a" "b"` 或零参造空序列）。用数值位判定而非 IsCollection——
        // 页面集合是 LazyPageList : ScriptObject（实现 IDictionary），
        // 会被 IsCollection 误判为字典
        if (args.Length >= 2 && IsNumericLike(args[1]))
        {
            var list = ToList(args[0]);
            var start = ToInt(args[1]);
            if (start < 0)
            {
                start = Math.Max(0, list.Count + start);
            }
            var len = args.Length >= 3 && args[2] is not null
                ? ToInt(args[2])
                : list.Count - start;
            return SliceSeq(list, start, len);
        }

        // 构造器形态（含零参 → 空序列）
        var arr = new ScriptArray();
        foreach (var a in args)
        {
            arr.Add(a);
        }
        return arr;
    }

    /// <summary>after：<c>after SEQ N</c> 与 <c>N SEQ</c> 双形态</summary>
    private static object SeqAfter(object?[] args)
    {
        if (args.Length < 2)
        {
            return args.Length == 1 ? args[0] ?? new List<object?>() : new List<object?>();
        }
        var (seq, count) = SplitSeqCount(args[0], args[1]);
        return ToList(seq).Skip(Math.Max(0, count)).ToList();
    }

    /// <summary>
    /// 排序键投影：把任意值映射为可比较的字符串（避免不可比类型抛异常）。
    /// 页面对象（ScriptObject）按 Hugo 默认排序规则投影为 Weight｜Date｜Title，
    /// 使 `sort $pages` 得到与 Hugo 一致的顺序
    /// </summary>
    private static string SortKeyOf(object? v)
    {
        if (v is ScriptObject so)
        {
            var weight = ToNumSafeText(GetMember(so, "weight") ?? GetMember(so, "Weight"));
            var date = (GetMember(so, "date") ?? GetMember(so, "Date"))?.ToString() ?? "";
            var title = (GetMember(so, "linktitle") ?? GetMember(so, "link_title")
                        ?? GetMember(so, "title") ?? GetMember(so, "Title"))?.ToString() ?? "";
            var path = GetMember(so, "path")?.ToString()
                       ?? GetMember(so, "rel_permalink")?.ToString() ?? "";
            // 权重补零使字符串排序等价数值排序；Date 为 ISO 串，字典序即时间序
            return $"{weight,10:0000000000}|{date}|{title}|{path}";
        }

        return v switch
        {
            null => "",
            IComparable c => PadNumeric(c),
            _ => v.ToString() ?? ""
        };
    }

    /// <summary>数值类型补零（字符串序等价数值序）；非数值原样字符串化</summary>
    private static string PadNumeric(IComparable c) => c switch
    {
        int i => $"{i,10:0000000000}",
        long l => $"{l,10:0000000000}",
        double d => $"{d,20:0000000000.0000000}",
        decimal m => $"{m,20:0000000000.0000000}",
        _ => c.ToString() ?? ""
    };

    /// <summary>同 PadNumeric，但接受任意值（null → 全零）</summary>
    private static string ToNumSafeText(object? v)
    {
        if (v is null)
        {
            return "0000000000";
        }
        if (v is int or long or double or decimal or float or short or byte)
        {
            try
            {
                return ((int)Convert.ToDouble(v, CultureInfo.InvariantCulture))
                    .ToString("D10", CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return "0000000000";
            }
        }
        return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
            CultureInfo.InvariantCulture, out var d) ? ((int)d).ToString("D10", CultureInfo.InvariantCulture)
            : "0000000000";
    }

    /// <summary>宽松转 int（数值直转；数字字符串可解析；其余 0）</summary>
    private static int ToInt(object? v)
    {
        if (v is null)
        {
            return 0;
        }
        return v switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            decimal m => (int)m,
            _ => int.TryParse(v.ToString(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                ? parsed : 0
        };
    }



    /// <summary>where：按字段等值过滤；value 为 null 时筛选空值（Hugo 语义子集）</summary>
    /// <summary>where 序列过滤（Hugo 双形态：KEY VALUE 与 KEY OPERATOR VALUE）</summary>
    private static object WhereSeqOp(object? seq, string key, object?[] rest)
    {
        if (rest.Length == 0)
        {
            return seq ?? Array.Empty<object>();
        }

        if (rest.Length == 1)
        {
            return WhereSeq(seq, key, rest[0]);
        }

        // 4 参形态：rest = [operator, value]
        var op = rest[0]?.ToString() ?? "";
        var target = rest[1];
        var items = ToObjectSeq(seq);
        var filtered = items.Where(item =>
        {
            var actual = GetMember(item, key);
            return CompareByOperator(actual, op, target);
        });
        return filtered.ToList();
    }

    /// <summary>按 Hugo operator 比较</summary>
    private static bool CompareByOperator(object? actual, string op, object? target)
    {
        var a = actual?.ToString() ?? "";
        switch (op.ToLowerInvariant())
        {
            case "in":
                // actual 是集合时：target 是否在其中；target 是集合时：actual 是否在 target 中
                if (actual is System.Collections.IEnumerable ae and not string)
                {
                    return ae.Cast<object?>().Any(x => Eq(x, target));
                }
                if (target is System.Collections.IEnumerable te and not string)
                {
                    return te.Cast<object?>().Any(x => Eq(x, actual));
                }
                return a.Contains(target?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            case "not in":
                return !CompareByOperator(actual, "in", target);
            case "like":
                return a.Contains(target?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            case "==" or "eq":
                return Eq(actual, target);
            case "!=" or "ne":
                return !Eq(actual, target);
            default:
                return CompareOrdered(actual, op, target);
        }
    }

    private static bool Eq(object? x, object? y) =>
        string.Equals(x?.ToString(), y?.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool CompareOrdered(object? actual, string op, object? target)
    {
        var x = ToNumSafe(actual);
        var y = ToNumSafe(target);
        return op switch
        {
            ">" => x > y,
            ">=" => x >= y,
            "<" => x < y,
            "<=" => x <= y,
            _ => false
        };
    }

    private static double ToNumSafe(object? v) =>
        double.TryParse(v?.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0d;

    private static IEnumerable<object?> ToObjectSeq(object? v) =>
        v is System.Collections.IEnumerable e and not string ? e.Cast<object?>() : [];

    /// <summary>where 序列过滤（3 参基础形态）</summary>
    private static object WhereSeq(object? seq, string key, object? value)
    {
        var result = new List<object?>();
        var wantEmpty = value is null or "";
        foreach (var item in ToList(seq))
        {
            var actual = GetMember(item, key);
            var isMatch = wantEmpty
                ? actual is null || (actual as string)?.Length == 0
                : string.Equals(actual?.ToString(), value?.ToString(), StringComparison.Ordinal);
            if (isMatch)
            {
                result.Add(item);
            }
        }
        return result;
    }

    /// <summary>
    /// 菜单归属判断（Hugo IsMenuCurrent/HasMenuCurrent 的全局形态）。
    /// 菜单项的 is_active 在构建期已按当前页路径计算，故直接读该标志；
    /// recursive=true 时向下递归子项（Hugo 的 HasMenuCurrent 语义：
    /// 菜单项是当前高亮页的祖先时同样为真）
    /// </summary>
    private static bool MenuEntryActive(object? entry, bool recursive)
    {
        if (entry is null)
        {
            return false;
        }
        if (GetMember(entry, "is_active") is bool active && active)
        {
            return true;
        }
        if (!recursive)
        {
            return false;
        }
        var children = GetMember(entry, "children");
        if (children is System.Collections.IEnumerable e and not string)
        {
            foreach (var child in e)
            {
                if (MenuEntryActive(child, true))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>成员访问（Hugo 的 where/sort 键路径）：支持点路径与命名变体（下划线/PascalCase）</summary>
    internal static object? GetMember(object? item, string path)
    {
        if (item is null || path.Length == 0)
        {
            return null;
        }
        object? current = item;
        foreach (var segment in path.Split('.'))
        {
            current = GetSingleMember(current, segment);
            if (current is null)
            {
                return null;
            }
        }
        return current;
    }

    private static object? GetSingleMember(object? item, string name)
    {
        if (item is null)
        {
            return null;
        }
        if (item is Scriban.Runtime.ScriptObject so)
        {
            foreach (var candidate in NameVariants(name))
            {
                if (so.TryGetValue(null, default, candidate, out var v))
                {
                    return v;
                }
            }
            return null;
        }
        if (item is IDictionary<string, object> dict)
        {
            foreach (var candidate in NameVariants(name))
            {
                if (dict.TryGetValue(candidate, out var v))
                {
                    return v;
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> NameVariants(string name)
    {
        yield return name;
        if (name.Length == 0)
        {
            yield break;
        }
        yield return char.ToUpperInvariant(name[0]) + name[1..];
        var parts = name.Split('_');
        if (parts.Length > 1)
        {
            // content_base_name → ContentBaseName（Hugo 的 PascalCase 别名）
            yield return string.Concat(parts.Select(p => p.Length > 0
                ? char.ToUpperInvariant(p[0]) + p[1..]
                : p));
        }
    }

    private static object SortSeq(object? seq, string? key)
    {
        var list = ToList(seq).ToList();
        return string.IsNullOrEmpty(key)
            ? list.OrderBy(v => v?.ToString() ?? "", StringComparer.Ordinal).ToList()
            : list.OrderBy(v => GetMember(v, key)?.ToString() ?? "", StringComparer.Ordinal).ToList();
    }

    /// <summary>dict "k" v ... → 字典（奇数参数时末键配空串）</summary>
    private static object BuildDict(object?[] args)
    {
        var dict = new ScriptObject();
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            dict[args[i]?.ToString() ?? ""] = args[i + 1];
        }
        return dict;
    }

    /// <summary>merge：Hugo 语义为"前参优先"——从后往前填充缺失键</summary>
    private static object MergeDicts(object?[] args)
    {
        var result = new ScriptObject();
        for (var i = args.Length - 1; i >= 0; i--)
        {
            if (args[i] is ScriptObject so)
            {
                foreach (var kv in so)
                {
                    if (!result.ContainsKey(kv.Key))
                    {
                        result[kv.Key] = kv.Value;
                    }
                }
            }
        }
        return result;
    }


    /// <summary>paramLookup：ctx 为 page/site 对象，path 点路径查 params（Hugo .Param 语义）</summary>
    private static object? ParamLookup(object? ctx, string path)
    {
        if (ctx is ScriptObject so && so.TryGetValue(null, default, "params", out var prm) && prm is not null)
        {
            return GetMember(prm, path);
        }
        return null;
    }

    /// <summary>
    /// 格式化 errorf/warnf 的消息（string.Format 语义）
    /// </summary>
    private static string FormatMessage(string? format, object?[] args)
    {
        if (string.IsNullOrEmpty(format))
            return string.Empty;
        try
        {
            return args.Length > 0
                ? string.Format(CultureInfo.InvariantCulture, format, args)
                : format;
        }
        catch (FormatException)
        {
            // 格式串与参数不匹配时原样返回格式串
            return format;
        }
    }

    #endregion
}
#pragma warning restore IL2026, IL3050
