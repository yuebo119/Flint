// Flint 主题迁移工具
// Go 时间布局 → .NET 格式串转换
//
// Go 用"参考时间"（Mon Jan 2 15:04:05 MST 2006）表示格式，.NET 用占位符。
// 这是确定性映射（无歧义），表驱动。

namespace Flint.ThemeMigrator.Conversion;

/// <summary>Go layout token → .NET 格式串</summary>
internal static class GoDateFormatConverter
{
    /// <summary>长模式优先（避免 "2006" 先于 "2006-01-02" 命中）</summary>
    private static readonly (string Go, string Net)[] Tokens =
    [
        ("2006-01-02T15:04:05Z07:00", "yyyy-MM-ddTHH:mm:sszzz"),
        ("2006-01-02T15:04:05Z", "yyyy-MM-ddTHH:mm:ss'Z'"),
        ("2006-01-02 15:04:05", "yyyy-MM-dd HH:mm:ss"),
        ("Mon, 02 Jan 2006 15:04:05 MST", "ddd, dd MMM yyyy HH:mm:ss 'GMT'"),
        ("Mon Jan 2 15:04:05 2006", "ddd MMM d HH:mm:ss yyyy"),
        ("January 2, 2006", "MMMM d, yyyy"),
        ("Jan 2, 2006", "MMM d, yyyy"),
        ("2 January 2006", "d MMMM yyyy"),
        ("January 2006", "MMMM yyyy"),
        ("Jan 2006", "MMM yyyy"),
        ("2006-01-02", "yyyy-MM-dd"),
        ("15:04:05", "HH:mm:ss"),
        ("01/02/2006", "MM/dd/yyyy"),
        ("02/01/2006", "dd/MM/yyyy"),
        ("2006/01/02", "yyyy/MM/dd"),
        // 单 token（最短，最后匹配）
        ("2006", "yyyy"),
        ("01", "MM"),
        ("02", "dd"),
        ("15", "HH"),
        ("04", "mm"),
        ("05", "ss"),
        ("Jan", "MMM"),
        ("Mon", "ddd"),
    ];

    /// <summary>转换 Go 布局串为 .NET 格式串</summary>
    public static string Convert(string goLayout)
    {
        if (string.IsNullOrEmpty(goLayout))
        {
            return goLayout;
        }

        var result = goLayout;
        // 先替换长模式（已按长度降序排列），再替换残留单词边界上的短 token
        foreach (var (go, net) in Tokens)
        {
            result = result.Replace(go, net, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>是否像 Go 布局串（含 2006/15:04 等特征）</summary>
    public static bool LooksLikeGoLayout(string s) =>
        !string.IsNullOrEmpty(s) &&
        (s.Contains("2006", StringComparison.Ordinal) ||
         s.Contains("15:04", StringComparison.Ordinal) ||
         s.Contains("Jan 2", StringComparison.Ordinal));
}
