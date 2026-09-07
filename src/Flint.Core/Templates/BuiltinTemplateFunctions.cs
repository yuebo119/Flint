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
    public BuiltinTemplateFunctions(string baseUrl = "")
    {
        _baseUrl = baseUrl.TrimEnd('/');
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

        // trim - 去除首尾空白
        obj.Import("trim", (string? s) => s?.Trim() ?? "");

        // trim_left - 去除左侧空白
        obj.Import("trim_left", (string? s) => s?.TrimStart() ?? "");
        obj.Import("trimleft", (string? s) => s?.TrimStart() ?? "");

        // trim_right - 去除右侧空白
        obj.Import("trim_right", (string? s) => s?.TrimEnd() ?? "");
        obj.Import("trimright", (string? s) => s?.TrimEnd() ?? "");

        // truncate - 截断字符串
        obj.Import("truncate", (string? s, int length, string ellipsis) =>
        {
            if (string.IsNullOrEmpty(s) || s.Length <= length)
                return s ?? "";
            ellipsis ??= "...";
            // 目标长度容不下省略号时，返回截断到目标长度的省略号（Hugo 语义）
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

        // first - 获取第一个元素
        obj.Import("first", (IEnumerable<object>? collection) =>
            collection?.FirstOrDefault());

        // last - 获取最后一个元素
        obj.Import("last", (IEnumerable<object>? collection) =>
            collection?.LastOrDefault());

        // index - 获取指定索引的元素
        obj.Import("index", (IEnumerable<object>? collection, int idx) =>
            collection?.ElementAtOrDefault(idx));

        // slice - 切片
        obj.Import("slice", (IEnumerable<object>? collection, int start, int? length) =>
        {
            if (collection == null)
                return Enumerable.Empty<object>();
            var list = collection.ToList();
            if (start < 0)
                start = 0;
            if (start >= list.Count)
                return Enumerable.Empty<object>();
            var len = length ?? (list.Count - start);
            return list.Skip(start).Take(len);
        });

        // after - 跳过前 N 个元素
        obj.Import("after", (IEnumerable<object>? collection, int n) =>
            collection?.Skip(n) ?? Enumerable.Empty<object>());

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
        obj.Import("sort", (IEnumerable<object>? collection, string? key) =>
        {
            if (collection == null)
                return Enumerable.Empty<object>();
            if (string.IsNullOrEmpty(key))
                return collection.OrderBy(x => x);

            return collection.OrderBy(x =>
            {
                if (x is ScriptObject so && so.TryGetValue(key, out var value))
                    return value;
                return x;
            });
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
        obj.Import("where", (IEnumerable<object>? collection, string? key, object? value) =>
        {
            if (collection == null)
                return Enumerable.Empty<object>();
            if (string.IsNullOrEmpty(key))
                return collection;

            return collection.Where(x =>
            {
                if (x is ScriptObject so && so.TryGetValue(key, out var v))
                    return Equals(v, value);
                return false;
            });
        });

        // append - 追加元素
        obj.Import("append", (IEnumerable<object>? collection, object? item) =>
        {
            if (collection == null)
                return item != null ? new[] { item } : [];
            return item != null ? collection.Append(item) : collection;
        });

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

        // in - 检查是否在集合中
        obj.Import("in", (object? item, IEnumerable<object>? collection) =>
            collection?.Contains(item) ?? false);

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
        obj.Import("add", (double a, double b) => a + b);

        // sub - 减法
        obj.Import("sub", (double a, double b) => a - b);

        // mul - 乘法
        obj.Import("mul", (double a, double b) => a * b);

        // div - 除法
        obj.Import("div", (double a, double b) => b != 0 ? a / b : 0);

        // mod - 取模
        obj.Import("mod", (int a, int b) => b != 0 ? a % b : 0);
        obj.Import("modBool", (int a, int b) => b != 0 && a % b == 0);

        // ceil - 向上取整
        obj.Import("ceil", (double n) => Math.Ceiling(n));

        // floor - 向下取整
        obj.Import("floor", (double n) => Math.Floor(n));

        // round - 四舍五入
        obj.Import("round", (double n, int? decimals) =>
            Math.Round(n, decimals ?? 0));

        // abs - 绝对值
        obj.Import("abs", (double n) => Math.Abs(n));

        // max - 最大值
        obj.Import("max", (params double[] nums) =>
            nums.Length > 0 ? nums.Max() : 0);

        // min - 最小值
        obj.Import("min", (params double[] nums) =>
            nums.Length > 0 ? nums.Min() : 0);

        // pow - 幂运算
        obj.Import("pow", (double @base, double exp) => Math.Pow(@base, exp));

        // sqrt - 平方根
        obj.Import("sqrt", (double n) => Math.Sqrt(n));

        // log - 对数
        obj.Import("log", (double n) => Math.Log(n));

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
        // eq - 相等
        obj.Import("eq", (object? a, object? b) => Equals(a, b));

        // ne - 不相等
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

        // default - 默认值
        obj.Import("default", (object? value, object? defaultValue) =>
            value ?? defaultValue);

        // cond - 条件表达式
        obj.Import("cond", (bool condition, object? trueValue, object? falseValue) =>
            condition ? trueValue : falseValue);

        // isset - 检查是否设置
        obj.Import("isset", (object? value) => value != null);

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
        // printf - 格式化输出
        obj.Import("printf", (string? format, params object[] args) =>
        {
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format ?? "", args);
            }
            catch
            {
                return format ?? "";
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
