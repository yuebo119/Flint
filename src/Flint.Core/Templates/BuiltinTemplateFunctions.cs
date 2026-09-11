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
        // 注意：first/last/uniq/shuffle/index 已有 Scriban 风格注册（单元素/单参数语义），
        // 此处**不重复注册**（重复会覆盖既有语义致类型转换失败——实测 first 2 SEQ 报
        // "Unable to convert int to IEnumerable"）。Hugo 的前缀形态 first N SEQ 由
        // 转换器映射为 Scriban 管道形态（SEQ | array.limit N）。
        // 真正新增的集合函数（Hugo 独有）：
        obj.Import("after", (object? seq, object? count) =>
            SliceSeq(seq, int.TryParse(count?.ToString(), out var n) ? n : 0, int.MaxValue));
        obj.Import("where", (object? seq, object? key, object? value) =>
            WhereSeq(seq, key?.ToString() ?? "", value));
        obj.Import("sortBy", (object? seq, object? key) => SortSeq(seq, key?.ToString()));
        obj.Import("in", (object? needle, object? haystack) => InSeq(needle, haystack));

        // ---- B8 构造与合并 ----
        // 注意：slice/index/sort 已有 Scriban 风格注册（含区间切片与字段排序），不重复注册。
        // 真正新增（Hugo 独有）：
        obj.Import("dict", (params object?[] args) => BuildDict(args));
        obj.Import("merge", (params object?[] args) => MergeDicts(args));

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
        System.Collections.IDictionary => [seq],   // 字典视作单元素（Hugo range 语义不同，此处保守）
        System.Collections.IEnumerable e => e.Cast<object?>().ToList(),
        _ => [seq]
    };

    /// <summary>是否为集合形态（非字符串/非字典的 IEnumerable）</summary>
    private static bool IsCollection(object? value) =>
        value is System.Collections.IEnumerable and not string and not System.Collections.IDictionary;

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
