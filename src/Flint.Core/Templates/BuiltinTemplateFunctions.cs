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
    /// 模板存在性探测（templates.Exists 用）：由渲染器注入（需 loader 才能判断）。
    /// 未注入时退回"名字非空即存在"的宽松语义
    /// </summary>
    internal Func<string, bool>? TemplateExistsProbe { get; set; }

    /// <summary>
    /// 模板 errorf 的错误接收器：由渲染器注入，汇聚到构建结果的 Errors。
    /// 未注入时退化为写标准错误（独立使用 BuiltinTemplateFunctions 的场景）
    /// </summary>
    internal Action<string>? ErrorReporter { get; set; }

    private void ReportTemplateError(string message)
    {
        if (ErrorReporter is not null)
        {
            ErrorReporter(message);
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }

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

    /// <summary>
    /// 可选 LIMIT 参数的解析：容忍任意输入，非法值按 0（不限制）处理。
    /// limit 是 Hugo 这些函数的**可选尾参**，不应因类型不匹配让整页渲染失败
    /// （对比 ToNum：那是数学函数的必需参数，非数值应当报错）
    /// </summary>
    private static int ToLimitOrZero(object? v)
    {
        switch (v)
        {
            case null:
                return 0;
            case int i:
                return i;
            case long l:
                return (int)Math.Clamp(l, 0, int.MaxValue);
            case double d:
                return (int)Math.Clamp(d, 0d, int.MaxValue);
            case decimal m:
                return (int)Math.Clamp(m, 0m, int.MaxValue);
            case float f:
                return (int)Math.Clamp(f, 0f, int.MaxValue);
            case bool b:
                return b ? 1 : 0;
        }

        return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? (int)Math.Clamp(parsed, 0d, int.MaxValue)
            : 0;
    }

    /// <summary>
    /// 有界正则替换（Hugo 的 <c>replaceRE ... LIMIT</c> 语义：最多替换 LIMIT 次）。
    /// 用 MatchEvaluator 计数并复用 <see cref="Match.Result"/>，
    /// 以便替换串里的 <c>$1</c> 反向引用与 Regex.Replace 保持同一套语义
    /// </summary>
    private static string ReplaceWithLimit(string input, string pattern, string replacement, int limit)
    {
        var done = 0;
        return Regex.Replace(input, pattern, m =>
        {
            if (done >= limit)
            {
                return m.Value;
            }
            done++;
            return m.Result(replacement);
        });
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
        // truncate —— 同样兼容两种参数序：
        //   Hugo 的 truncate LENGTH [ELLIPSIS] STRING（实测 `truncate 3 "abcdef"` → "abc …"）
        //   Flint 历史的 truncate STRING LENGTH [ELLIPSIS]（`"abcdef" | truncate 3`）
        // 主题里几乎都是管道形态（矩阵实测 9 处全部如此），但非管道形态按 Hugo
        // 文档写就是 (长度, 文本)，故按类型判定方向，两种都能用
        obj.Import("truncate", (params object?[] a) =>
        {
            if (a.Length < 2)
            {
                return a.Length > 0 ? a[0]?.ToString() ?? "" : "";
            }
            string? s;
            int length;
            // 判方向：Hugo 序是 (长度, 文本)，Flint 序是 (文本, 长度)；省略号都在末位
            if (LooksLikeNumber(a[0]) && a[1] is string text)
            {
                length = ToInt(a[0]);
                s = text;
            }
            else
            {
                s = a[0]?.ToString();
                length = ToInt(a[1]);
            }
            if (string.IsNullOrEmpty(s) || s.Length <= length)
                return s ?? "";
            if (a.Length > 2 && a[2]?.ToString() is { Length: > 0 } explicitEllipsis)
            {
                // 显式省略号：保持 Flint 语义（省略号计入长度）——不复制 Hugo 的实现，
                // 因为 Hugo v0.166 在显式省略号下行为不自洽：实测
                // `truncate 5 s "..."` → "..."（整段被省略号取代）、
                // `truncate 2 s "..."` → ".." + 全文。主题几乎不用显式省略号形态
                if (length <= explicitEllipsis.Length)
                    return explicitEllipsis[..length];
                return s[..(length - explicitEllipsis.Length)] + explicitEllipsis;
            }
            // 默认省略号：Hugo v0.166 实测语义——取前 N 个字符 + " …"（省略号不计入 N）：
            // `truncate 3 "abcdef"` → "abc …"（len=7：3 + 空格 + 3 字节省略号）
            return s[..length] + DefaultTruncateEllipsis;
        });

        // replace - 替换字符串
        obj.Import("replace", (string? s, string? old, string? @new) =>
            s?.Replace(old ?? "", @new ?? "") ?? "");

        // replace_re - 正则替换
        // 参数序按 Hugo 文档：replaceRE PATTERN REPLACEMENT INPUT [LIMIT]。
        // 注意与同族的 replace 相反——Hugo 的 replace 是 INPUT OLD NEW（输入在前），
        // 而 replaceRE 的 PATTERN 在前。此前 Flint 把 replace_re 也注册成输入在前，
        // 于是任何 Hugo 形态的调用都静默错位（`replaceRE "a" "" $s` 会把模式当输入），
        // 带第 4 参 limit 时更直接报 "Argument index must be < 3"
        // （narrow 的 icon.html 实测 33 处）。
        // limit：Hugo 语义为最多替换次数，缺省或 ≤0 表示不限制
        obj.Import("replace_re", (string? pattern, string? replacement, string? s, params object?[] rest) =>
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(pattern))
                return s ?? "";
            var repl = replacement ?? "";
            var limit = rest.Length > 0 ? ToLimitOrZero(rest[0]) : 0;
            return limit > 0 ? ReplaceWithLimit(s, pattern, repl, limit) : Regex.Replace(s, pattern, repl);
        });
        obj.Import("replaceRE", (string? pattern, string? replacement, string? s, params object?[] rest) =>
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(pattern))
                return s ?? "";
            var repl = replacement ?? "";
            var limit = rest.Length > 0 ? ToLimitOrZero(rest[0]) : 0;
            return limit > 0 ? ReplaceWithLimit(s, pattern, repl, limit) : Regex.Replace(s, pattern, repl);
        });

        // split - 分割字符串
        // split - 分隔字符串。首参宽容为 object：Scriban 把复杂对象隐式转 string 时
        // 会遍历成员（脚本对象可达递归深度上限并报 "Exceeding number of recursive
        // depth limit 100"，FixIt 的 camel-case.html 实测整页渲染失败）。
        // 此处只接受标量形态，集合/对象按空串处理（Hugo 侧同样要求字符串）
        obj.Import("split", (object? s, string? sep) =>
        {
            var text = ToFlatString(s);
            return text.Length == 0 ? [] : text.Split(sep ?? " ", StringSplitOptions.RemoveEmptyEntries);
        });

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
        // substr - 子串。Hugo 的 length 可省略（省略即取到末尾）——FixIt 的
        // camel-case.html 写 `substr $part 1`，三参严格形参报
        // "Invalid number of arguments 2 ... expecting 3"（整页渲染失败）
        obj.Import("substr", (string? s, int start, params int[] rest) =>
        {
            if (string.IsNullOrEmpty(s))
                return "";
            if (start < 0)
                start = 0;
            if (start >= s.Length)
                return "";
            var length = rest.Length > 0 ? rest[0] : s.Length - start;
            if (length < 0 || start + length > s.Length)
                length = s.Length - start;
            return s.Substring(start, length);
        });

        // repeat —— **两种参数序都接受**：
        //   Hugo 的 strings.Repeat COUNT STRING（Hugo v0.166 实测 `strings.Repeat 3 "ab"`
        //   → "ababab"）
        //   Flint 历史的 repeat STRING COUNT（`"ab" | repeat 3` 的管道形态）
        // 靠**类型**判定方向（一边是数字、一边是文本），故 smol 的 header.html
        // （`strings.Repeat (site.title | len | add 6) "="`，17 处
        // "Unable to convert type string to int"）与既有管道写法都能过。
        // 两边同为文本/数字时按 Flint 历史序（字符串在前）
        obj.Import("repeat", (params object?[] a) =>
        {
            if (a.Length < 2)
            {
                return a.Length > 0 ? a[0]?.ToString() ?? "" : "";
            }
            string s;
            int count;
            // 判方向：Hugo 序是 (计数, 文本)，Flint 序是 (文本, 计数)
            if (LooksLikeNumber(a[0]) && a[1] is string second)
            {
                count = ToInt(a[0]);
                s = second;
            }
            else
            {
                s = a[0]?.ToString() ?? "";
                count = ToInt(a[1]);
            }
            if (s.Length == 0 || count <= 0)
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
            return PageSeqResult(a, a.Where(x => !bSet.Contains(x)));
        });

        // intersect - 交集
        obj.Import("intersect", (IEnumerable<object>? a, IEnumerable<object>? b) =>
        {
            if (a == null || b == null)
                return Enumerable.Empty<object>();
            return PageSeqResult(a, a.Intersect(b));
        });

        // union - 并集
        obj.Import("union", (IEnumerable<object>? a, IEnumerable<object>? b) =>
        {
            if (a == null)
                return b ?? Enumerable.Empty<object>();
            if (b == null)
                return a;
            return PageSeqResult(a, a.Union(b));
        });

        // uniq - 去重
        obj.Import("uniq", (IEnumerable<object>? collection) =>
            collection is null
                ? Enumerable.Empty<object>()
                : PageSeqResult(collection, collection.Distinct()));

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
            return PageSeqResult(collection, list);
        });

        // reverse - 反转
        obj.Import("reverse", (IEnumerable<object>? collection) =>
            collection is null
                ? Enumerable.Empty<object>()
                : PageSeqResult(collection, collection.Reverse()));

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
                return PageSeqResult(a[0], (desc ? byDefault.Reverse() : byDefault).Cast<object>());
            }

            var sorted = collection.OrderBy(
                x => SortKeyOf(GetMember(x, key)), StringComparer.Ordinal);
            return PageSeqResult(a[0], (desc ? sorted.Reverse() : sorted).Cast<object>());
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
        // seq - 数字序列。**Hugo 支持 1/2/3 参三种形态**：
        //   seq LAST · seq FIRST LAST · seq FIRST INCREMENT LAST
        // 此前注册为 (int, int, int?) —— Scriban 对委托形参不做"可省略"处理，
        // 两参调用直接报 "Invalid number of arguments 2 passed to seq … expecting 3"
        //（monochrome 的 inline/pagination/default.html 实测 8 处），
        // 故改用 params 收参后按个数分派（与 slicestr / find_re 同一处理方式）
        obj.Import("seq", (params object?[] a) =>
        {
            if (a.Length == 0)
            {
                return Enumerable.Empty<int>();
            }
            int first;
            int last;
            var step = 1;
            if (a.Length == 1)
            {
                first = 1;
                last = ToInt(a[0]);
            }
            else if (a.Length == 2)
            {
                first = ToInt(a[0]);
                last = ToInt(a[1]);
            }
            else
            {
                first = ToInt(a[0]);
                step = ToInt(a[1]);
                last = ToInt(a[2]);
            }
            if (step == 0)
            {
                return Enumerable.Empty<int>();
            }
            var count = step > 0
                ? (last - first) / step + 1
                : (first - last) / (-step) + 1;
            return count <= 0
                ? Enumerable.Empty<int>()
                : Enumerable.Range(0, count).Select(i => first + i * step);
        });

        // range - 生成范围
        obj.Import("range", (int count) => Enumerable.Range(0, count));

        // in - 检查是否在集合中（Hugo 语义：haystack 是字符串时按子串，
        // 是序列时按元素）。形参用 object? 不用 IEnumerable——严格形参会被
        // Scriban 首个重载绑定，把 `in page.kind "term"`（haystack 为字符串）
        // 打成 "Unable to convert type `string` to `IEnumerable<Object>`"（Stack 实测）
        obj.Import("in", (object? a, object? b) =>
        {
            // **参数序按 Hugo 语义自适应**：Hugo 的签名是 `in SET ITEM`（集合在前），
            // Flint 历史实现是 (item, set)——主题与转换器两侧两种顺序都会出现，靠类型定方向：
            //   恰有一侧是集合 → 集合即 SET，另一侧是 ITEM
            //   两侧同类（两个字符串 / 两个字典）→ 按 Hugo 顺序：第一个是 SET
            //（monochrome 的 `in $validFormats $format`：集合在前，历史实现把集合当
            //  needle、"default" 当 haystack → 恒 false → 误报 format 非法 21 次）
            if (IsCollection(a) && !IsCollection(b))
            {
                return ContainsIn(a, b);
            }
            if (IsCollection(b) && !IsCollection(a))
            {
                return ContainsIn(b, a);
            }
            return ContainsIn(a, b);
        });

        // apply - Hugo 语义：`apply SEQ FUNC_NAME [ARGS...]`。目标函数以**名字字符串**
        // 给出，参数里的 "." 是元素占位（全部替换；**无占位时元素不传入**——
        // Hugo v0.166 实测 `apply (slice "a" "b") "upper" "x"` → X,X，
        // `apply (slice "a" "b") "replace" "." "a" "z"` → z,b）。
        // 旧实现形参是 Func<object,object> → 主题传字符串时 Scriban 绑定直接抛
        // "Unable to convert type `string` to `Func<Object, Object>`"
        //（hugo-coder 的 _partials/header.html：
        //  `apply (slice .URL) (.Params.urlFunc | default "relLangURL") "."`）。
        // 用自定义函数对象（而非 lambda）是为了拿到 TemplateContext——被调函数
        // 可能是 IScriptCustomFunction（页面集合方法族/命名空间函数），其 Invoke 需要它
        obj.SetValue("apply", new ApplyFunction(obj), false);
    }

    /// <summary>
    /// <c>apply</c>：把目标函数逐元素应用到集合（Hugo 语义，见注册处注释）
    /// </summary>
    private sealed class ApplyFunction(ScriptObject globals) : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return null;
            }

            var collection = arguments[0];
            if (arguments.Count < 2)
            {
                return collection;
            }

            var target = arguments[1];
            var rest = new object?[Math.Max(0, arguments.Count - 2)];
            for (var i = 0; i < rest.Length; i++)
            {
                rest[i] = arguments[i + 2];
            }

            return target switch
            {
                string name => MapApply(collection, item =>
                    CallFunction(context, ResolveFunction(name), SubstitutePlaceholder(rest, item))),
                // Scriban 风格：直接传可调用值（lambda / 自定义函数）
                Scriban.Runtime.IScriptCustomFunction or Delegate =>
                    MapApply(collection, item => CallFunction(context, target, [item])),
                _ => collection
            };
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 2;
        public int ParameterCount => 2;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind => Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(object), index == 0 ? "seq" : "fn");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo => new(typeof(object), "result");

        private Dictionary<string, string>? _nameIndex;

        /// <summary>
        /// 按名字取全局函数（找不到时报错，对齐 Hugo 的 fail-fast）。
        /// 先用原名，再退化到"去下划线 + 小写"归一化比较——主题把函数名当**字符串**
        /// 传给 apply（`default "relLangURL"`），转换器无从改写，故需要运行期归一化：
        /// `relLangURL` → `rel_lang_url`、`htmlEscape` → `html_escape`、
        /// `safeHTML` → `safe_html`（hugo-coder 实测）
        /// </summary>
        private object ResolveFunction(string name)
        {
            if (globals.TryGetValue(null, default, name, out var direct) && direct is not null)
            {
                return direct;
            }

            _nameIndex ??= BuildNameIndex();
            if (_nameIndex.TryGetValue(NormalizeName(name), out var actual) &&
                globals.TryGetValue(null, default, actual, out var aliased) && aliased is not null)
            {
                return aliased;
            }

            throw new Scriban.Syntax.ScriptRuntimeException(default, $"apply: 函数 `{name}` 未找到");
        }

        private Dictionary<string, string> BuildNameIndex()
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in globals.Keys)
            {
                var normalized = NormalizeName(key);
                if (!index.ContainsKey(normalized))
                {
                    index[normalized] = key;
                }
            }
            return index;
        }

        private static string NormalizeName(string name) =>
            name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

        /// <summary>参数里的 "." 替换成当前元素（Hugo 语义：全部替换）</summary>
        private static object?[] SubstitutePlaceholder(object?[] args, object? item)
        {
            var replaced = new object?[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                replaced[i] = args[i] is string s && s == "." ? item : args[i];
            }
            return replaced;
        }

        private static object? CallFunction(Scriban.TemplateContext context, object function, object?[] args)
        {
            var callArgs = new ScriptArray();
            foreach (var arg in args)
            {
                callArgs.Add(arg);
            }

            return function switch
            {
                // 走 Scriban 自己的调用入口（而非直接 Invoke）：它负责形参绑定，
                // 含 **params 形参展开**——直接 Invoke 时 `printf "格式" 值` 的值
                // 到不了 params 数组（实测：apply 内 `printf "<%s>" .` 得 "<%s>"，
                // 正是 string.Format 抛异常后的兜底返回值）
                Scriban.Runtime.IScriptCustomFunction custom =>
                    Scriban.Syntax.ScriptFunctionCall.Call(context, null, custom, callArgs),
                // 委托形态：与 Import 同一条 Scriban 反射路径（IL2026/IL3050 已在本文件
                // 顶部压制；委托都是本程序集显式引用的 lambda，linker 不裁剪）
                Delegate d => Scriban.Syntax.ScriptFunctionCall.Call(context, null,
                    Scriban.Runtime.DynamicCustomFunction.Create(d), callArgs),
                _ => null
            };
        }

        private static object MapApply(object? collection, Func<object?, object?> map)
        {
            if (collection is null)
            {
                return new ScriptArray();
            }

            if (collection is System.Collections.IEnumerable seq and not string)
            {
                var result = new ScriptArray();
                foreach (var item in seq)
                {
                    result.Add(map(item));
                }
                return result;
            }

            return collection;
        }
    }

    #endregion

    #region URL 函数 (10+)

    /// <summary>相对 URL：前导 <c>/</c>，绝对 URL 原样返回（Hugo relURL 语义）</summary>
    private static string RelUrl(string? path) =>
        string.IsNullOrEmpty(path) ? "/"
        : IsAbsoluteUrl(path) ? path
        : "/" + path.TrimStart('/');

    /// <summary>绝对 URL：拼到 baseURL（Hugo absURL 语义，baseURL 已含语言前缀时等价 AbsLangURL）</summary>
    private string AbsUrl(string? path) =>
        string.IsNullOrEmpty(path) ? _baseUrl
        : IsAbsoluteUrl(path) ? path
        : _baseUrl + "/" + path.TrimStart('/');

    private static bool IsAbsoluteUrl(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private void RegisterUrlFunctions(ScriptObject obj)
    {
        // absURL - 绝对 URL（与 urls.AbsLangURL 共用 AbsUrl：单语言站点等价，
        // 两条路径此前是各写一遍的重复实现）
        obj.Import("absURL", (string? path) => AbsUrl(path));
        obj.Import("abs_url", (string? path) => AbsUrl(path));

        // relURL - 相对 URL（与 urls.RelLangURL 共用 RelUrl）
        obj.Import("relURL", (string? path) => RelUrl(path));
        obj.Import("rel_url", (string? path) => RelUrl(path));

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
        // add - 求和 / 字符串拼接。Hugo 的 add 是 **variadic**（`add 1 2 3` = 6，
        // 单参返回自身），严格双参形参会把单参调用打成
        // "Invalid number of arguments 1 ... expecting 2"（even 主题 37 处实测）。
        // 且 Hugo 的 add **多态**（实测 v0.166：全字符串→拼接 `add "fa-solid fa-tag" " me-1"`
        // ⇒ "fa-solid fa-tag me-1"、全数值→求和、混合同现→报错）。
        // 主题大量用 add 拼 URL/类名（clarity `add $relpath .`、fixit `add $icon " me-1"`），
        // 一律 ToNum 求和会把 "" + "x" 算成 0
        obj.Import("add", (Func<object?[], object>)(a =>
        {
            if (a.Length == 0)
            {
                return 0d;
            }
            if (a.All(v => v is string))
            {
                return string.Concat(a.Select(v => (string)v!));
            }
            return a.Aggregate(0d, (acc, v) => acc + ToNum(v));
        }));

        // sub - 减法
        // sub - 差。同样 variadic：`sub 10 2 3` = 5（首参减其余），单参返回自身
        obj.Import("sub", (params object?[] a) => a.Length switch
        {
            0 => 0d,
            1 => ToNum(a[0]),
            _ => a.Skip(1).Aggregate(ToNum(a[0]), (acc, v) => acc - ToNum(v))
        });

        // mul - 乘法
        // mul - 积（variadic，单参返回自身）
        obj.Import("mul", (params object?[] a) =>
            a.Aggregate(1d, (acc, v) => acc * ToNum(v)));

        // div - 除法
        // div - 商（variadic，单参返回自身；除零保持旧行为返回 0）
        obj.Import("div", (params object?[] a) => a.Length switch
        {
            0 => 0d,
            1 => ToNum(a[0]),
            _ => a.Skip(1).Aggregate(ToNum(a[0]),
                (acc, v) => ToNum(v) == 0 ? 0d : acc / ToNum(v))
        });

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

        // transform.XMLEscape - XML 转义 + 丢弃非法 XML 字符
        // （Hugo v0.166 实测：`&<>`→实体，`"`→`&#34;`、`'`→`&#39;`、
        //  \t/\n/\r→`&#x9;`/`&#xA;`/`&#xD;`，非法字符（如 U+0001）**丢弃**而非替换；
        //  全局 `xmlEscape` 在 v0.166 已移除，仅命名空间形态存在，此处注册全局
        //  实现供 transform.XMLEscape 解析——命名空间表按全局名查找实现。
        //  FixIt 的 rss.xml `transform.XMLEscape .Summary` 实测命中）
        obj.Import("xml_escape", (string? s) => XmlEscape(s ?? ""));
        obj.Import("xmlEscape", (string? s) => XmlEscape(s ?? ""));

        // jsonify - JSON 序列化
        // jsonify 序列化的是运行时任意对象（模板变量），类型无法静态已知，
        // source-gen 不适用；NativeAOT 下仅此函数受限（异常时返回 "null"）
        // Hugo 的 jsonify 签名是 `jsonify [OPTIONS] VALUE`（选项在前）：
        // 管道形态 `$scratch.Get "x" | jsonify (dict "indent" "  ")` 里模板侧
        // 把管道值注入**首参**，于是实到参数是 (值, 选项) 两个——单参严格形参
        // 会抛 "Argument index must be < 1"（Congo 的 schema.html 实测 86 处）。
        // 故收 params 并按字典形态识别选项（含 indent/prefix 键者）
        obj.Import("jsonify", (params object?[] args) =>
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            object? payload = args.Length > 0 ? args[0] : null;
            foreach (var arg in args)
            {
                if (arg is not ScriptObject opt)
                {
                    continue;
                }
                var indent = GetMember(opt, "indent") ?? GetMember(opt, "Indent");
                if (indent is null && GetMember(opt, "prefix") is null && GetMember(opt, "Prefix") is null)
                {
                    continue;
                }
                if (indent is not null && indent.ToString()!.Length > 0)
                {
                    options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                }
                // 选项对象在前时（Go 的 `jsonify OPTIONS VALUE`），载荷是下一个参数
                if (ReferenceEquals(arg, payload))
                {
                    payload = args.Length > 1 ? args[1] : null;
                }
                break;
            }
            try
            {
                // 手写遍历而非 JsonSerializer：模板对象（ScriptObject/ScriptArray）
                // 的反射序列化在 NativeAOT 下被禁用，此前一律抛异常回退成字面 "null"
                // （Congo 的 schema.html / 搜索索引实测）
                return SerializeToJson(payload, options.WriteIndented);
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

        // lt / le / gt / ge - 比较（Hugo v0.166 实测语义）
        //
        // 必须走**跨类型比较**而不是 `IComparable.CompareTo`：装箱的 Int32 与 Double
        // 相比时 `int.CompareTo(object)` 抛 "Object must be of type Int32"
        //（实测：`{{ compare.Ge 5 (math.add 3 1) }}` 报错、`{{ compare.Ge 5 4 }}` 正常——
        //  `math.add` 产出 double；ananke 的 home.html 就死在这一行）
        obj.Import("lt", (object? a, object? b) => CompareHugo(a, b) < 0);
        obj.Import("le", (object? a, object? b) => CompareHugo(a, b) <= 0);
        obj.Import("gt", (object? a, object? b) => CompareHugo(a, b) > 0);
        obj.Import("ge", (object? a, object? b) => CompareHugo(a, b) >= 0);

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
        // default VALUE FALLBACK（Hugo 语义：**空值**取兜底）。空值判据见
        // IsEmptyForDefault —— 与 `if` 的真值判定**不同**：Hugo v0.166 实测
        // `default 3 ""` / `default 3 0` / `default 3 (slice)` 都取 3，
        // 而 `default 3 false` 保留 false。此前只判 null，使"配置里写了空串"的
        // 场景不再兜底（ananke 的 `$.Param "recent_posts_number" | compare.Default 3`
        // 拿到 "" → `math.add "" 1` 报 "Object must be of type Int32"）
        obj.Import("default", (params object?[] a) => a.Length switch
        {
            0 => null,
            1 => a[0],
            _ => IsEmptyForDefault(a[0]) ? a[1] : a[0]
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

    private void RegisterDebugFunctions(ScriptObject obj)
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
            // 含 `%` 的一律按 **Go fmt 语义**格式化（Hugo 的 printf 就是 Go 的
            // Sprintf）：`%v` 的复合值渲染、`%q` 的转义、类型不符的 `%!d(...)` 标记
            // 都是 .NET 复合格式表达不出来的
            if (f.Contains('%', StringComparison.Ordinal))
            {
                return GoPrintf.Format(f, args);
            }
            try
            {
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

        // errorf - 记录错误后**继续渲染**（对齐 Hugo 实测语义 v0.166：errorf 只记录
        // 错误日志，页面照常产出，构建结束按错误数判失败——`HOME {{ errorf … }} END`
        // 实测产出 "HOME  END" + exit=1）。早期实现直接抛异常中止整页渲染，
        // 主题里一处 errorf 就让整站塌成空页（fixit 的 icon.html 实测 4 处 → 仅剩 2 页）。
        // params 与 printf/warnf 对齐：裸 object[] 时 Scriban 按严格绑定处理，带参调用报参数错误
        obj.Import("errorf", (string? format, params object[] args) =>
        {
            ReportTemplateError(FormatMessage(format, args));
            return "";
        });

        // debug - 调试输出
        obj.Import("debug", (object? value) =>
            $"<!-- DEBUG: {value} -->");

        // typeof - 获取类型
        obj.Import("typeof", (object? value) =>
            value?.GetType().Name ?? "null");
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
        // fingerprint - 资源指纹。**必须收下可选算法参数**：Scriban 只绑定首个注册的
        // 重载，旧实现是 `(string? path)` 单参版，主题写 `$res | fingerprint "sha512"`
        // 时形参不符 → "Argument index must be < 1"（LoveIt 的 plugin/style.html 实测）。
        // 资源对象走资源指纹（与 resources.Fingerprint 同实现、产物登记落盘）；
        // 字符串路径保留 "?v=hash" 兼容行为
        obj.Import("fingerprint", (Func<object?, object?[], object?>)((value, rest) =>
        {
            var algorithm = rest.Length > 0 ? rest[0]?.ToString() ?? "sha256" : "sha256";
            var resource = ToResource(value);
            if (resource is not null)
            {
                var fingerprinted = resource.WithFingerprint(algorithm);
                Track(fingerprinted);
                return fingerprinted.ToScriptObject();
            }
            var path = value?.ToString();
            if (string.IsNullOrEmpty(path))
            {
                return "";
            }
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
            var shortHash = Convert.ToHexStringLower(hash)[..8];
            return path.Contains('?') ? $"{path}&v={shortHash}" : $"{path}?v={shortHash}";
        }));

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

        // as_list - range 语义的集合归一（Hugo 的 range 对 nil/false 不迭代、
        // 对标量迭代一次、对集合逐项迭代）。Scriban 的 `for x in false` 会抛
        // "Unexpected type `System.Boolean` for iterator"——转换器对 range 的集合
        // 表达式统一包本函数（Blowfish 的 `range (or .social .links)` 两值为空时实测）
        obj.Import("as_list", (object? v) => AsList(v));

        // as_pairs：Hugo 的**双变量** range 语义（v0.166 实测）——map 产出 (key, value)、
        // slice 产出 (index, value)、标量/空产出单元素或空。转换器把
        // `range $k, $v := X` 产成 `for $pair in as_pairs (X)`。
        // 早期实现双变量 range 靠 `$pair.Key ?? for.index` / `$pair.Value ?? $pair` 兜底，
        // 但 as_list 当时把 map 当**标量**（单元素）→ $pair 就是那个 map 本身 →
        // `reflect.IsMap $pair` 为真 → 主题的"递归转换 map 键"辅助函数无限自递归
        //（FixIt 的 camel-case-keys.html 实测：200 层后由深度守卫捕获）
        obj.Import("as_pairs", (object? v) => AsPairs(v));

        // num_gt / num_ge / num_lt / num_le：**宽容数值比较**（Hugo 的比较语义）。
        // Hugo 的 gt/lt 对字符串数字与数值做类型强制，而 Scriban 的 `>` 运算符与
        // 严格 gt（IComparable.CompareTo）在类型不一致时抛
        // "Unable to convert type `object` to int"（Clarity 的 `$value > 0`，
        // map 值为字符串时实测 29 处）。转换器把比较运算符改产出这些函数
        obj.Import("num_gt", (object? a, object? b) => CompareNumeric(a, b) > 0);
        obj.Import("num_ge", (object? a, object? b) => CompareNumeric(a, b) >= 0);
        obj.Import("num_lt", (object? a, object? b) => CompareNumeric(a, b) < 0);
        obj.Import("num_le", (object? a, object? b) => CompareNumeric(a, b) <= 0);

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
    /// <summary>
    /// SET 是否包含 ITEM（Hugo <c>in</c> 的判定）：字符串按子串、字典按键、
    /// 其余序列按元素（元素比较沿用 ToString 归一，与 InSeq 同口径）
    /// </summary>
    private static bool ContainsIn(object? set, object? item) => set switch
    {
        null => false,
        System.Collections.IDictionary map => map.Keys.Cast<object?>()
            .Any(k => string.Equals(k?.ToString(), item?.ToString(), StringComparison.Ordinal)),
        string text => text.Contains(item?.ToString() ?? "", StringComparison.Ordinal),
        _ => ToList(set).Any(x => string.Equals(x?.ToString(), item?.ToString(), StringComparison.Ordinal))
    };

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
        return fromEnd
            ? PageSeqResult(seq, list.Skip(Math.Max(0, list.Count - count)))
            : PageSeqResult(seq, list.Take(count));
    }

    /// <summary>
    /// index：Hugo 双语义——集合按整数下标取值，映射按字符串键取值。
    /// 越界/缺键返回 null（Hugo 宽容语义），不再抛 ArgumentOutOfRange
    /// </summary>
    /// <summary>
    /// <c>default</c> 的"空值"判据（Hugo v0.166 实测）：<c>nil</c> / 空串 / 数值 0 /
    /// 空集合为"空"→ 取兜底；<c>false</c> **不算空**（原样返回）。
    /// 与 <c>if</c> 的真值判定刻意分开——那里 false 是假值
    /// </summary>
    internal static bool IsEmptyForDefault(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        sbyte v => v == 0,
        byte v => v == 0,
        short v => v == 0,
        ushort v => v == 0,
        int v => v == 0,
        uint v => v == 0,
        long v => v == 0,
        ulong v => v == 0,
        float v => v == 0,
        double v => v == 0,
        decimal v => v == 0,
        bool => false,
        ScribanTemplateRenderer.LazyPageObject => false,
        IFlintNonDataObject => false,
        System.Collections.ICollection c => c.Count == 0,
        System.Collections.IEnumerable e => !e.Cast<object?>().Any(),
        _ => false
    };

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

        // 列表按整数下标取值，**必须先于**下面的字典/成员分支：页面集合
        // LazyPageList 同时是 ScriptObject 与 System.Collections.IDictionary
        //（Scriban 成员字典视图），走字典分支时 `index $pages 0` 恒为 null
        //（实测：FixIt RSS 的 `(index $pages.ByLastmod.Reverse 0).LastMod`
        //  以及 `(index $pages 0)`；同一对象的 `$pages[0]` 语法本就可取，
        //  两条取值路径此前不一致）。字符串键仍走字典/成员查找
        //（`index $pages "len"` = 5 依赖后者）
        if (target is IList<ScriptObject> listTarget && TryKeyAsIndex(key, out var listIndex))
        {
            return listIndex >= 0 && listIndex < listTarget.Count ? listTarget[listIndex] : null;
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
    /// index 的键是否为整数下标：整数/长整数/整数字符串都算；
    /// 其他类型（如成员名 <c>"count"</c>）不算——它们走字典/成员查找分支
    /// </summary>
    private static bool TryKeyAsIndex(object? key, out int index)
    {
        switch (key)
        {
            case int i:
                index = i;
                return true;
            case long l when l is >= int.MinValue and <= int.MaxValue:
                index = (int)l;
                return true;
            // 浮点/十进制也算（Scriban 的算术可能产出 double）：`index $pages (add $i -1)`
            // 的键若是 double 会漏到这里之外 → 走成员查找 → 恒 null
            //（even 的 section.html 用 `index $paginator.Pages (add $index -1)` 取前一项）
            case double d when d is >= int.MinValue and <= int.MaxValue:
                index = (int)d;
                return true;
            case float f when f is >= int.MinValue and <= int.MaxValue:
                index = (int)f;
                return true;
            case decimal m when m is >= int.MinValue and <= int.MaxValue:
                index = (int)m;
                return true;
            case string s when int.TryParse(s, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed):
                index = parsed;
                return true;
            default:
                index = 0;
                return false;
        }
    }

    /// <summary>
    /// XML 文本转义（transform.XMLEscape）：转义 <c>&amp; &lt; &gt; " '</c> 与
    /// 制表/换行/回车，**丢弃**其余非法 XML 字符（对齐 Hugo v0.166 实测行为，
    /// 非法区间见 XML 1.0 字符集）
    /// </summary>
    private static string XmlEscape(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length + 16);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&#34;"); break;
                case '\'': sb.Append("&#39;"); break;
                case '\t': sb.Append("&#x9;"); break;
                case '\n': sb.Append("&#xA;"); break;
                case '\r': sb.Append("&#xD;"); break;
                default:
                    if (IsValidXmlChar(ch))
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>XML 1.0 合法字符（合法代理项保留——成对出现即辅助平面字符）</summary>
    private static bool IsValidXmlChar(char ch) =>
        ch == '\t' || ch == '\n' || ch == '\r' ||
        (ch >= ' ' && ch <= '\uD7FF') ||
        (ch >= '\uE000') || char.IsSurrogate(ch);

    /// <summary>
    /// slice：Flint 序列切片（<c>slice SEQ START [LEN]</c>）与 Hugo 可变参数
    /// 构造器（<c>slice</c>/<c>slice A</c>/<c>slice A B</c>）。
    /// 首参是集合时按切片处理，否则按构造器（Hugo 语义，LoveIt 用零参 slice 造空序列）
    /// </summary>
    /// <summary>
    /// 序列形态判定（slice 的两义消歧用）：字符串/布尔/数值/页面与字典都**不是**
    /// 序列——页面是 ScriptObject（非 IEnumerable）、字典同此，故
    /// `slice PAGE 0 2` 走 Hugo 的构造器语义。页面集合只实现泛型
    /// <c>IList&lt;ScriptObject&gt;</c>（如 LazyPageList），非泛型 IList 判定会漏
    /// </summary>
    private static bool IsSequenceLike(object? v) => v switch
    {
        null or string or bool => false,
        ScriptArray => true,
        System.Collections.IList => true,
        IList<ScriptObject> => true,
        IReadOnlyList<ScriptObject> => true,
        System.Collections.IEnumerable => true,
        _ => false
    };

    private static object SeqSlice(object?[] args)
    {
        // 两义消歧（首参是否为**列表**）：
        //  · 首参是列表 + 第二参为数值 → `slice SEQ START [LEN]` 子序列（Flint 的
        //    宽松形态，`site.pages | slice 0 2` 取前两项；页面集合是
        //    LazyPageList（IList<ScriptObject>）故按 IList 判定而非 IsCollection）
        //  · 否则 → Hugo 的构造器语义：`slice 1 2` ⇒ 长度 2（v0.166 实测）。
        //    早期实现无此分支，把 `slice 1 2` 解成 SEQ=1/START=2 → 空数组
        //（Congo 的 jsonify 参数实测）
        if (args.Length >= 2 && IsNumericLike(args[1]) && IsSequenceLike(args[0]))
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
            return PageSeqResult(args[0], ToList(SliceSeq(list, start, len)));
        }

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
        return PageSeqResult(seq, ToList(seq).Skip(Math.Max(0, count)));
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

    /// <summary>
    /// 宽容比较（Hugo 语义）：两侧都能解析为数值时按数值比较，
    /// 否则按 Ordinal 字符串比较——不抛类型异常
    /// </summary>
    /// <summary>
    /// Hugo 的比较语义（v0.166 探针实测），lt/le/gt/ge 与 num_* 共用：
    /// 1. 两侧都能数值化（数值类型、bool → 0/1、**数字串**）→ 按数值比：
    ///    `lt 1 2.5` → true、`ge 3 3.0` → true、`gt "5" 0` → true
    ///    （数字串必须被强制，Clarity 的 `$value > 0` 依赖它）；
    /// 2. 一侧数值化、另一侧不能 → **数值 &gt; 非数字串**：`lt "B" 3` → true、`gt "B" 3` → false；
    /// 3. 两侧都不能数值化 → 序号比较（Go 的 `&lt;` 是字节序）：`lt "a" "b"` → true；
    /// 4. nil 排在所有值之前。
    /// 不能用 <c>IComparable.CompareTo</c>：装箱的 Int32 与 Double 相比时
    /// <c>int.CompareTo(object)</c> 抛 "Object must be of type Int32"
    /// （实测：`compare.Ge 5 (math.add 3 1)` 报错、`compare.Ge 5 4` 正常——`math.add` 产出 double；
    /// ananke 的 home.html 就死在这一行）
    /// </summary>
    private static int CompareHugo(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return 0;
        }
        if (a is null)
        {
            return -1;
        }
        if (b is null)
        {
            return 1;
        }

        var an = TryNum(a, out var na);
        var bn = TryNum(b, out var nb);
        if (an && bn)
        {
            return na.CompareTo(nb);
        }
        if (an != bn)
        {
            return an ? 1 : -1;
        }
        return string.CompareOrdinal(a.ToString(), b.ToString());
    }

    private static bool IsNumericValue(object? v) =>
        v is bool or int or long or double or float or decimal or short or byte or sbyte
            or uint or ulong or ushort;

    /// <summary>数值比较：与 <see cref="CompareHugo"/> 同口径（Hugo 的比较语义只有一套）</summary>
    private static int CompareNumeric(object? a, object? b) => CompareHugo(a, b);

    private static bool TryNum(object? v, out double num)
    {
        switch (v)
        {
            case null:
                num = 0;
                return true;
            case bool bo:
                num = bo ? 1 : 0;
                return true;
            case int or long or double or float or decimal or short or byte:
                num = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return true;
            default:
                return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
                    CultureInfo.InvariantCulture, out num);
        }
    }

    /// <summary>
    /// 扁平化字符串（字符串模板函数的宽容入参）：标量走不变文化 ToString，
    /// 集合/对象等复合值返回空串——**不**走隐式转换，避免 Scriban 遍历成员
    /// 触发递归深度上限（见 split 的注释）
    /// </summary>
    /// <summary>
    /// 是否是可迭代**数据**之外的成员：函数值（页面/上下文对象的 store 方法、
    /// 页面方法族等）与引擎内部键（`__` 前缀）。Hugo 的映射从不含函数，
    /// 而 Scriban 在取值位置会**自动调用**函数成员（0 参调用 → 参数校验失败
    /// "Invalid number of arguments `0` passed to `$pair.Value`"），故一律跳过
    /// </summary>
    private static bool IsNonDataMember(string key, object? value) =>
        value is Scriban.Runtime.IScriptCustomFunction ||
        key.StartsWith("__", StringComparison.Ordinal);

    /// <summary>
    /// 归一为可迭代的值序列（Hugo 单变量 range 语义，v0.166 实测）：
    /// nil/false → 空、字符串 → 单元素、映射 → **值序列**、序列 → 元素、标量 → 单元素
    /// </summary>
    private static List<object?> AsList(object? v) => v switch
    {
        null => [],
        // 引擎内部投影不展开（见 IFlintNonDataObject）：展开即把内部成员当数据，
        // 主题的递归辅助函数会顺着有环的引用图无限下钻
        IFlintNonDataObject => [],
        bool b => b ? [true] : [],
        string str => [str],
        // **列表优先于映射**：页面集合是 `ScriptObject + IList<ScriptObject>`（LazyPageList），
        // 若按 ScriptObject 判定会去迭代它的**成员**（bydate/bytitle/count… 一堆函数值）
        // 而不是页面本列——迭代出函数值后 Scriban 会自动调用它们（"Argument index
        // must be < 1"、"Unable to convert type object to int"，loveit/hugo-coder/blowfish 实测）
        IList<ScriptObject> pageList => pageList.Cast<object?>().ToList(),
        System.Collections.IList list => list.Cast<object?>().ToList(),
        // 纯映射：ScriptObject 的 IDictionary.Values / DictionaryEntry.Value 返回
        // Scriban 的 InternalValue 包装（渲染成类型名），只有 Keys + 索引器能拿到真实值
        ScriptObject o => o.Keys.Where(k => !IsNonDataMember(k, o[k])).Select(k => o[k]).ToList(),
        System.Collections.IDictionary d => d.Values.Cast<object?>().ToList(),
        System.Collections.IEnumerable e => e.Cast<object?>().ToList(),
        _ => [v]
    };

    /// <summary>
    /// 集合变换结果归一：源是页面集合时，结果**仍是页面集合**（带 Pages 方法族）。
    /// Hugo 里 where/union/first/reverse/uniq/sort… 的结果都是 Pages，主题会在其上
    /// 继续调用 .Prev/.Next/.ByDate；只对 where 做归一是不够的——
    /// FixIt 的 init/global.html 把 `where … | union (where …)` 的结果存进 site.store，
    /// 由 footer.html 取出后调 `$pages.Prev`，实测报 "The function `$pages.Prev` was not found"
    /// </summary>
    private static object PageSeqResult(object? source, IEnumerable<object?> items)
    {
        var list = items.ToList();
        return ScribanTemplateRenderer.RewrapPageSequence(source, list) ?? (object)list;
    }

    /// <summary>
    /// 归一为 (Key, Value) 对序列（Hugo 双变量 range 语义）：映射 → (键, 值)、
    /// 序列 → (索引, 元素)、nil/false → 空、标量 → 单个 (0, 标量)
    /// </summary>
    private static List<object?> AsPairs(object? v)
    {
        var result = new List<object?>();
        // 引擎内部投影不展开（见 IFlintNonDataObject）：展开会把内部成员当数据，
        // 主题的递归辅助函数顺着有环引用图无限下钻（FixIt 的 camel-case-keys 实测）
        if (v is IFlintNonDataObject)
        {
            return result;
        }
        switch (v)
        {
            case null:
                return result;
            case bool b:
                if (b)
                {
                    result.Add(new ScriptObject { ["Key"] = 0, ["Value"] = true });
                }
                return result;
            case string str:
                result.Add(new ScriptObject { ["Key"] = 0, ["Value"] = str });
                return result;
            // **列表优先于映射**（同 AsList）：页面集合是 ScriptObject + IList<ScriptObject>，
            // 按映射判定会把它的成员当键值对（迭代出函数值 → 自动调用报错）
            case IList<ScriptObject> pageList:
                for (var i = 0; i < pageList.Count; i++)
                {
                    result.Add(new ScriptObject { ["Key"] = i, ["Value"] = pageList[i] });
                }
                return result;
            case System.Collections.IList list:
                for (var i = 0; i < list.Count; i++)
                {
                    result.Add(new ScriptObject { ["Key"] = i, ["Value"] = list[i] });
                }
                return result;
            // 映射：ScriptObject 的成员要经 Keys + 索引器取真值（见 AsList 注释）
            case ScriptObject obj:
                foreach (var key in obj.Keys)
                {
                    var member = obj[key];
                    if (IsNonDataMember(key, member))
                    {
                        continue;
                    }
                    result.Add(new ScriptObject { ["Key"] = key, ["Value"] = member });
                }
                return result;
            case System.Collections.IDictionary dict:
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    result.Add(new ScriptObject { ["Key"] = entry.Key, ["Value"] = entry.Value });
                }
                return result;
            case System.Collections.IEnumerable seq:
                var index = 0;
                foreach (var item in seq)
                {
                    result.Add(new ScriptObject { ["Key"] = index, ["Value"] = item });
                    index++;
                }
                return result;
            default:
                result.Add(new ScriptObject { ["Key"] = 0, ["Value"] = v });
                return result;
        }
    }

    internal static string ToFlatString(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        char c => c.ToString(),
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        // 复合对象提前拦截：ScriptObject 的 ToString 会遍历成员（可达 Scriban 的
        // 递归深度上限），故不落到下面的 IFormattable 分支
        ScriptObject => "",
        System.Collections.IEnumerable => "",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => ""
    };

    /// <summary>
    /// Hugo 模板真值语义（供同程序集的页面方法函数复用）：
    /// null/false/0/空串/空集合为假，其余为真
    /// </summary>
    internal static bool IsTruthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        sbyte or byte or short or ushort or int or uint or long or ulong => ToNum(v) != 0,
        float f => f != 0,
        double d => d != 0,
        decimal m => m != 0,
        System.Collections.ICollection c => c.Count > 0,
        System.Collections.IEnumerable e => e.GetEnumerator().MoveNext(),
        _ => true
    };

    /// <summary>truncate 默认省略号（Hugo v0.166 实测：空格 + 省略号，且不计入长度参数）</summary>
    private const string DefaultTruncateEllipsis = " …";

    /// <summary>
    /// 参数序判定用：是否为"数值形态"（数值类型，或能整体解析为数字的字符串）。
    /// 用于 repeat/truncate 这类 Hugo 与 Flint 参数序相反、需要按类型判方向的函数
    /// </summary>
    private static bool LooksLikeNumber(object? v)
    {
        if (v is null)
        {
            return false;
        }
        switch (v)
        {
            case int or long or double or float or decimal or short or byte:
                return true;
        }
        return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    /// <summary>宽松转 int（数值直转；数字字符串可解析；其余 0）</summary>
    internal static int ToInt(object? v)
    {        if (v is null)
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
        }).ToList();
        // 页面集合的筛选结果仍须带 Pages 方法族（Hugo 语义）——
        // `(where …).GroupByDate "2006"` 这类链式调用实测 22 处失败
        return ScribanTemplateRenderer.RewrapPageSequence(seq, filtered) ?? (object)filtered;
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
                // 字符串包含是宽容分支（Hugo 要求 target 为切片）。
                // 但 target 为空/null 时**不能**返回 true：`a.Contains("")` 恒真，
                // 会把整个集合判成命中。Hugo v0.166 实测 `where .Pages "Type" "in" nil`
                // 与 `… "in" (slice)` 均返回 0 条
                var inTarget = target?.ToString() ?? "";
                return inTarget.Length > 0
                    && a.Contains(inTarget, StringComparison.OrdinalIgnoreCase);
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
        // 同 4 参分支：页面集合的筛选结果保留 Pages 方法族
        return ScribanTemplateRenderer.RewrapPageSequence(seq, result) ?? (object)result;
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
    /// <summary>
    /// 手写 JSON 序列化（NativeAOT 安全）：模板对象是 ScriptObject/ScriptArray，
    /// 反射序列化在 AOT 下不可用。覆盖 null/布尔/数值/时间/字符串/映射/序列，
    /// 其余按字符串兜底
    /// </summary>
    private static string SerializeToJson(object? value, bool indented)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
        {
            WriteJson(writer, value);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJson(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case bool b:
                writer.WriteBooleanValue(b);
                return;
            case string s:
                writer.WriteStringValue(s);
                return;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto);
                return;
            case DateTime dt:
                writer.WriteStringValue(dt);
                return;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return;
            case float or double or decimal:
                writer.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                return;
            case IDictionary<string, object?> map:
                writer.WriteStartObject();
                foreach (var (k, v) in map)
                {
                    writer.WritePropertyName(k);
                    WriteJson(writer, v);
                }
                writer.WriteEndObject();
                return;
            case System.Collections.IDictionary dict:
                writer.WriteStartObject();
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    writer.WritePropertyName(entry.Key?.ToString() ?? "");
                    WriteJson(writer, entry.Value);
                }
                writer.WriteEndObject();
                return;
            case System.Collections.IEnumerable seq:
                writer.WriteStartArray();
                foreach (var item in seq)
                {
                    WriteJson(writer, item);
                }
                writer.WriteEndArray();
                return;
            default:
                writer.WriteStringValue(value.ToString());
                return;
        }
    }

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
            // Go 动词（%s/%v/%T/%q…）不是 .NET 占位符：直接交给 string.Format 会抛
            // FormatException 并被下面的 catch 吞成"原样返回格式串"——主题日志里的
            // errorf "Icon src is missing: %s" 因此输出字面 %s（fixit 实测）
            if (format.Contains('%', StringComparison.Ordinal))
            {
                // 无参调用保持原样：Go 的 Sprintf 在缺参时输出 %!v(MISSING)，
                // 主题里的 "100% 完成" 这类文本不该被动词转换改写
                return args.Length > 0
                    ? GoPrintf.Format(format, args)
                    : format;
            }
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
