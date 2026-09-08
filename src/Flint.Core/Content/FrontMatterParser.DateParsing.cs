// Flint 静态站点生成器
// Front Matter 解析器——日期与时区语义部分：Hugo 特殊日期源识别与
// 无偏移日期按站点时区补偏移（从 FrontMatterParser.cs 按 partial 拆出）

using System.Globalization;

namespace Flint.Core.Content;

/// <summary>
/// Front Matter 解析器——日期与时区语义部分
/// （主文件：FrontMatterParser.cs，含类声明与 DetectFormat 公开方法）
/// </summary>
#pragma warning disable IL2026, IL3050 // AOT 警告在此类中被抑制
public sealed partial class FrontMatterParser
{
    private static DateTimeOffset? GetDictDateTime(Dictionary<string, object> dict, string key, TimeZoneInfo? siteTimeZone)
    {
        return ToDateTimeOffset(dict, key, siteTimeZone);
    }

    /// <summary>
    /// 读取日期键并识别 Hugo 特殊日期源字符串（":git"/":filemodtime"/":filename"，大小写不敏感）。
    /// 特殊源时返回 (null, 源)；普通日期返回 (值, null)；键缺失返回 (null, null)。
    /// </summary>
    private static (DateTimeOffset? Value, string? Source) GetDictDateWithSource(
        Dictionary<string, object> dict, string key, TimeZoneInfo? siteTimeZone)
    {
        if (!dict.TryGetValue(key, out var value))
        {
            return (null, null);
        }

        if (value is string s && s.StartsWith(':'))
        {
            var source = s.ToLowerInvariant() switch
            {
                ":git" or ":filemodtime" or ":filename" => s.ToLowerInvariant(),
                _ => null // 未知源不记录，按缺省处理（与 Hugo 对未知源的宽容一致）
            };
            return (null, source);
        }

        return (ToDateTimeOffset(dict, key, siteTimeZone), null);
    }

    private static DateTimeOffset? ToDateTimeOffset(Dictionary<string, object> dict, string key, TimeZoneInfo? siteTimeZone)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => ApplyTimeZone(dt, siteTimeZone),
            string s => ParseDateString(s, siteTimeZone),
            _ => null
        };
    }

    private static DateTimeOffset ApplyTimeZone(DateTime dt, TimeZoneInfo? siteTimeZone)
    {
        if (dt.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(dt.Ticks, TimeSpan.Zero);
        }

        // Unspecified/Local：无偏移信息，Hugo 语义为按站点时区解释；未配置站点时区时沿用本机时区（旧行为）
        var offset = siteTimeZone?.GetUtcOffset(dt) ?? TimeZoneInfo.Local.GetUtcOffset(dt);
        return new DateTimeOffset(dt.Ticks, offset);
    }

    private static DateTimeOffset? ParseDateString(string s, TimeZoneInfo? siteTimeZone)
    {
        // 带显式偏移（Z / ±hh:mm / ±hhmm）：偏移是作者意图，不重解释
        if (HasExplicitOffset(s))
        {
            return DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset)
                ? withOffset
                : null;
        }

        // 无偏移：按站点时区补偏移；未配置时本机时区（与 DateTimeOffset.TryParse 旧行为等价）
        if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, DateTimeStyles.None, out var noOffset))
        {
            var offset = siteTimeZone?.GetUtcOffset(noOffset) ?? TimeZoneInfo.Local.GetUtcOffset(noOffset);
            return new DateTimeOffset(noOffset, offset);
        }

        return null;
    }

    /// <summary>
    /// 判断日期字符串是否带显式 UTC 偏移或 Z 后缀
    /// </summary>
    private static bool HasExplicitOffset(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        var last = s[^1];
        if (last is 'Z' or 'z')
        {
            return true;
        }

        // ±hh:mm（末 6 位，第 3 位是冒号）或 ±hhmm（末 5 位）
        if (s.Length >= 6 && s[^6] is '+' or '-' && s[^3] == ':')
        {
            return true;
        }

        return s.Length >= 5 && s[^5] is '+' or '-';
    }
}
#pragma warning restore IL2026, IL3050
