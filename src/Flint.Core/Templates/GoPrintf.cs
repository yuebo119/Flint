// Flint 静态站点生成器
// Go fmt 语义的格式化器（Hugo 的 printf/warnf/errorf 就是 Go 的 fmt.Sprintf）
//
// 为什么不能用 .NET 的 string.Format 翻译 Go 动词：两者的**值渲染规则**不同——
//   `%v` 在 Go 里是"默认格式"：切片 → `[1 a true]`、映射 → `map[a:1 b:x]`（键排序）、
//        bool → `true/false`（小写）、nil → `<nil>`；.NET 的 ToString() 给出
//        `System.Collections.Generic.List…` 这类类型名。
//   `%q` 在 Go 里是**带转义的引号串**（`"a\"b"`），不是"两侧加引号"。
//   `%s/%d/%t` 遇到类型不符在 Go 里产出 `%!d(string=5)` 标记（不是异常、也不是数字）。
// 语料实测（21 主题 873 个格式动词）：`%s` 464、`%v` 241、`%q` 97、`%d` 55，
// 其余为 `%T/%#v/%g/%.2f/%02d/%5s` 等零头——`%v`/`%q` 合计 39% 走的是 .NET 翻译层
// 表达不出来的语义。
//
// 实现取舍（与 Go 的差异只有一处，见下）：
//   * 数值类型归一：Go 的 `%d` 只接受整数（float64 → `%!d(float64=5.7)`），
//     Flint 对**数值族**（int/long/double/…）一律按请求的动词渲染，只有**非数值**
//     才产 `%!` 标记。原因是两侧内部类型不同（同一个 Hugo 值在 Flint 里可能是
//     double 而 Hugo 里是 int64），严格按类型名会让标记无谓地多出来。
//   * 其余（复合值渲染、引号转义、`%T` 类型名、宽度/精度/标志）与 Go 对齐，
//     期望值来自 Hugo v0.166 探针（见 GoPrintfTests）。

using System.Globalization;
using System.Text;

namespace Flint.Core.Templates;

/// <summary>Go <c>fmt</c> 语义的格式化</summary>
internal static class GoPrintf
{
    /// <summary>按 Go 语义格式化（未知动词按 Go 的 <c>%!v(...)</c> 标记处理）</summary>
    public static string Format(string format, IReadOnlyList<object?> args)
    {
        var sb = new StringBuilder(format.Length + 16);
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

            // 解析：标志 / 宽度 / 精度
            var specStart = i;
            var flagLeft = false;
            var flagZero = false;
            var flagPlus = false;
            var flagSharp = false;
            for (; i < format.Length; i++)
            {
                switch (format[i])
                {
                    case '-': flagLeft = true; break;
                    case '0': flagZero = true; break;
                    case '+': flagPlus = true; break;
                    case '#': flagSharp = true; break;
                    case ' ': break;
                    default: goto flagsDone;
                }
            }
        flagsDone:
            var width = 0;
            while (i < format.Length && char.IsAsciiDigit(format[i]))
            {
                width = (width * 10) + (format[i] - '0');
                i++;
            }

            var precision = -1;
            if (i < format.Length && format[i] == '.')
            {
                i++;
                precision = 0;
                while (i < format.Length && char.IsAsciiDigit(format[i]))
                {
                    precision = (precision * 10) + (format[i] - '0');
                    i++;
                }
            }

            if (i >= format.Length)
            {
                break;
            }

            var verb = format[i];
            var spec = new Spec(verb, width, precision, flagLeft, flagZero, flagPlus, flagSharp);
            if (argIndex >= args.Count)
            {
                sb.Append("%!").Append(verb).Append("(MISSING)");
                _ = specStart;
                continue;
            }

            sb.Append(Render(spec, args[argIndex++]));
        }

        if (argIndex < args.Count)
        {
            // Go 的"多余实参"提示：%!(EXTRA type=value, …)
            var extra = new StringBuilder();
            for (var i = argIndex; i < args.Count; i++)
            {
                if (extra.Length > 0)
                {
                    extra.Append(", ");
                }
                extra.Append(TypeName(args[i])).Append('=').Append(FormatValue(args[i]));
            }
            sb.Append("%!(EXTRA ").Append(extra).Append(')');
        }

        return sb.ToString();
    }

    private readonly record struct Spec(
        char Verb, int Width, int Precision, bool Left, bool Zero, bool Plus, bool Sharp);

    private static string Render(Spec spec, object? value)
    {
        // nil 的处理是**有意偏离 Go**：Go 会打 `%!s(<nil>)` 这类标记，而标记落进
        // class/属性/URL 里会把标记本身写进 HTML（实测：ananke 的
        // `$post_class = printf "page-%s" .ContentBaseName` 在 Flint 侧取值缺失时
        // 产出 `class="page-%!s(<nil>=<nil>)"` → CSS class 被污染、门禁④结构分下跌）。
        // 标记的用途是暴露**程序错误**，而模板里"某个值缺失"是常态（Flint 的数据面
        // 可能比 Hugo 少），故除 `%v`/`%T`（Go 本身把 nil 当值渲染为 `<nil>`）外
        // 一律渲染空串
        if (value is null)
        {
            return spec.Verb is 'v' or 'T' ? "<nil>" : "";
        }

        return RenderCore(spec, value);
    }

    private static string RenderCore(Spec spec, object? value) => spec.Verb switch
    {
        'v' => Pad(spec, spec.Sharp ? FormatGoSyntax(value) : FormatValue(value)),
        's' => value is string s ? Pad(spec, Truncate(s, spec.Precision))
            : IsNumeric(value) ? Pad(spec, Truncate(NumberText(value), spec.Precision))
            : BadVerb(spec, value),
        'q' => Pad(spec, QuoteGo(value)),
        'd' => IsNumeric(value) ? Pad(spec, Pad(spec, IntegerText(value)))
            : BadVerb(spec, value),
        'f' or 'F' => IsNumeric(value)
            ? Pad(spec, FloatText(value, spec.Precision < 0 ? 6 : spec.Precision, fixedNotation: true))
            : BadVerb(spec, value),
        'e' or 'E' => IsNumeric(value)
            ? Pad(spec, FloatText(value, spec.Precision < 0 ? 6 : spec.Precision, fixedNotation: false))
            : BadVerb(spec, value),
        'g' or 'G' => IsNumeric(value)
            ? Pad(spec, FloatText(value, spec.Precision, fixedNotation: true))
            : BadVerb(spec, value),
        't' => value is bool b ? Pad(spec, b ? "true" : "false") : BadVerb(spec, value),
        'x' => HexValue(spec, value, upper: false),
        'X' => HexValue(spec, value, upper: true),
        'o' => IsNumeric(value) ? Pad(spec, Convert.ToString(ToInt64(value), 8)) : BadVerb(spec, value),
        'b' => IsNumeric(value) ? Pad(spec, Convert.ToString(ToInt64(value), 2)) : BadVerb(spec, value),
        'T' => TypeName(value),
        _ => BadVerb(spec, value)
    };

    private static string BadVerb(Spec spec, object? value) =>
        $"%!{spec.Verb}({TypeName(value)}={FormatValue(value)})";


    /// <summary>
    /// Go 的默认格式（<c>%v</c>）：切片 <c>[a b]</c>、映射 <c>map[k:v …]</c>（键排序）、
    /// nil <c>&lt;nil&gt;</c>、bool 小写、浮点用最短往返表示
    /// </summary>
    internal static string FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return "<nil>";
            case bool b:
                return b ? "true" : "false";
            case string s:
                return s;
            case char c:
                return c.ToString();
            case double or float or decimal:
                return FloatText(value, precision: -1, fixedNotation: true);
            case int or long or short or byte or sbyte or uint or ulong or ushort:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            case DateTimeOffset dto:
                return dto.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            case DateTime dt:
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case System.Collections.IDictionary map:
                return "map[" + string.Join(" ", SortedMapEntries(map)) + "]";
            case System.Collections.IEnumerable seq:
                return "[" + string.Join(" ", ElementTexts(seq)) + "]";
            default:
                return value.ToString() ?? "";
        }
    }

    /// <summary>Go 语法表示（<c>%#v</c>）：近似为 <c>[]int{1, 2}</c> / <c>map[string]interface {}{"a":1}</c></summary>
    internal static string FormatGoSyntax(object? value)
    {
        switch (value)
        {
            case null:
                return "<nil>";
            case bool or string or char or int or long or short or byte or sbyte or uint or ulong or ushort:
                return FormatValue(value);
            case double or float or decimal:
                return FloatText(value, precision: -1, fixedNotation: true);
            case System.Collections.IDictionary map:
            {
                var entries = SortedMapEntries(map)
                    .Select(e =>
                    {
                        var colon = e.IndexOf(':', StringComparison.Ordinal);
                        var key = colon < 0 ? e : e[..colon];
                        var val = colon < 0 ? "" : e[(colon + 1)..];
                        return $"\"{key}\":{val}";
                    });
                return "map[string]interface {}{" + string.Join(", ", entries) + "}";
            }
            case System.Collections.IEnumerable seq:
                return SliceTypeName(seq) + "{" + string.Join(", ", ElementTexts(seq).Select(FormatGoSyntaxLiteral)) + "}";
            default:
                return $"\"{value}\"";
        }
    }

    private static string FormatGoSyntaxLiteral(string text) =>
        text is "true" or "false" || IsNumericText(text) ? text : $"\"{text}\"";

    private static bool IsNumericText(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static IEnumerable<string> SortedMapEntries(System.Collections.IDictionary map)
    {
        var keys = new List<string>();
        foreach (var key in map.Keys)
        {
            keys.Add(key?.ToString() ?? "");
        }
        keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            yield return key + ":" + FormatValue(map[key]);
        }
    }

    private static IEnumerable<string> ElementTexts(System.Collections.IEnumerable seq)
    {
        foreach (var item in seq)
        {
            yield return FormatValue(item);
        }
    }

    /// <summary>
    /// Go 的 <c>%q</c>：字符串加引号并转义、数值按 rune 字面量、
    /// 复合值**逐元素**引号化（Hugo 实测：`printf "%q" (slice "x")` → <c>["x"]</c>，
    /// 而不是给整个 <c>%v</c> 结果再包一层引号）
    /// </summary>
    internal static string QuoteGo(object? value)
    {
        switch (value)
        {
            case null:
                return "<nil>";
            case string s:
                return "\"" + EscapeGo(s) + "\"";
            case bool b:
                return b ? "true" : "false";
            case int or long or short or byte or sbyte or uint or ulong or ushort:
            {
                var code = ToInt64(value);
                return code is >= 32 and < 127
                    ? $"'{(char)code}'"
                    : $"'\\x{code:x2}'";
            }
            case double or float or decimal:
                return FloatText(value, precision: -1, fixedNotation: true);
            case System.Collections.IDictionary map:
                return "map[" + string.Join(" ", SortedMapEntries(map)) + "]";
            case System.Collections.IEnumerable seq:
                return "[" + string.Join(" ", QuoteElements(seq)) + "]";
            default:
                return "\"" + EscapeGo(FormatValue(value)) + "\"";
        }
    }

    private static IEnumerable<string> QuoteElements(System.Collections.IEnumerable seq)
    {
        foreach (var item in seq)
        {
            yield return QuoteGo(item);
        }
    }

    private static string EscapeGo(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32)
                    {
                        sb.Append($"\\x{(int)ch:x2}");
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Go 的 <c>%T</c> 类型名</summary>
    internal static string TypeName(object? value) => value switch
    {
        null => "<nil>",
        bool => "bool",
        string => "string",
        char => "int32",
        int or short or sbyte => "int",
        long => "int64",
        byte or ushort => "uint8",
        uint => "uint",
        ulong => "uint64",
        double => "float64",
        float => "float32",
        decimal => "float64",
        System.Collections.IDictionary => "map[string]interface {}",
        System.Collections.IEnumerable seq => SliceTypeName(seq),
        _ => value.GetType().Name
    };

    /// <summary>
    /// 切片元素类型推断（Go 的 <c>%T</c>/<c>%#v</c> 打的是静态类型）：
    /// 全整数 → <c>[]int</c>、全字符串 → <c>[]string</c>、全布尔 → <c>[]bool</c>，
    /// 混合或空 → <c>[]interface {}</c>
    /// </summary>
    private static string SliceTypeName(System.Collections.IEnumerable seq)
    {
        var allInt = true;
        var allString = true;
        var allBool = true;
        var any = false;
        foreach (var item in seq)
        {
            any = true;
            allInt &= item is int or long or short or byte or sbyte or uint or ulong or ushort;
            allString &= item is string;
            allBool &= item is bool;
        }
        if (!any)
        {
            return "[]interface {}";
        }
        if (allInt)
        {
            return "[]int";
        }
        if (allString)
        {
            return "[]string";
        }
        return allBool ? "[]bool" : "[]interface {}";
    }

    private static string HexValue(Spec spec, object? value, bool upper)
    {
        if (value is string s)
        {
            var sb = new StringBuilder(s.Length * 2);
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                sb.Append(b.ToString(upper ? "X2" : "x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
        if (IsNumeric(value))
        {
            var hex = Convert.ToString(ToInt64(value), 16);
            return Pad(spec, upper ? hex.ToUpperInvariant() : hex);
        }
        return BadVerb(spec, value);
    }

    private static string Pad(Spec spec, string text)
    {
        if (spec.Width <= text.Length)
        {
            return text;
        }
        var fill = spec.Zero && IsNumericText(text) ? '0' : ' ';
        return spec.Left
            ? text.PadRight(spec.Width, fill)
            : text.PadLeft(spec.Width, fill);
    }

    private static string Truncate(string text, int precision) =>
        precision >= 0 && text.Length > precision ? text[..precision] : text;

    private static string NumberText(object? value) =>
        value is double or float or decimal
            ? FloatText(value, precision: -1, fixedNotation: true)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static string IntegerText(object? value)
    {
        if (value is double d)
        {
            return Math.Truncate(d).ToString("0", CultureInfo.InvariantCulture);
        }
        if (value is float f)
        {
            return Math.Truncate(f).ToString("0", CultureInfo.InvariantCulture);
        }
        if (value is decimal m)
        {
            return Math.Truncate(m).ToString("0", CultureInfo.InvariantCulture);
        }
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    /// <summary>
    /// 浮点文本：精度 &lt; 0 用**最短往返**表示（Go 的 <c>%v</c>/<c>%g</c> 行为）；
    /// <paramref name="fixedNotation"/> 为真时优先定点表示（Go 的 <c>%f</c>/<c>%g</c>）
    /// </summary>
    private static string FloatText(object? value, int precision, bool fixedNotation)
    {
        var d = ToDouble(value);
        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            return d.ToString(CultureInfo.InvariantCulture);
        }
        if (precision >= 0)
        {
            return fixedNotation
                ? d.ToString("F" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)
                : d.ToString("e" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        // 最短往返（Go 的 %v/%g 默认）：.NET 用 "R"，指数标记按 Go 用**小写 e**
        return d.ToString("R", CultureInfo.InvariantCulture)
            .Replace("E", "e", StringComparison.Ordinal);
    }

    private static bool IsNumeric(object? value) =>
        value is int or long or short or byte or sbyte or uint or ulong or ushort
            or double or float or decimal;

    private static double ToDouble(object? value) =>
        Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static long ToInt64(object? value) =>
        value is double d ? (long)Math.Truncate(d)
        : value is float f ? (long)Math.Truncate(f)
        : value is decimal m ? (long)Math.Truncate(m)
        : Convert.ToInt64(value, CultureInfo.InvariantCulture);
}
