// Flint 静态站点生成器
// Go 时间布局串 → .NET 格式串（Hugo 的 time.Format / .Date.Format 语义）

namespace Flint.Core.Templates;

/// <summary>
/// Go 用"参考时间"（<c>Mon Jan 2 15:04:05 MST 2006</c>）表示日期布局，.NET 用占位符。
/// 两者是确定性映射（表驱动，无歧义），但**运行期**才拿到布局串的场景（Hugo 的
/// <c>time.Format LAYOUT VALUE</c> 常把布局放在 <c>site.Params.dateFormat</c> 之类的
/// 表达式里）无法靠迁移期转换覆盖——故转换落在引擎侧，迁移器只做同一张表的调用方。
/// </summary>
/// <remarks>
/// 迁移期转换（<c>.Date.Format "2006-01-02"</c>）仍然保留：它把字面布局直接写成 .NET
/// 串（读起来更直观，且与既有产物形态一致）。两条路径不会互相破坏——
/// <see cref="LooksLikeGoLayout"/> 只认 Go 特征 token，已转换的 .NET 串（如
/// <c>yyyy-MM-ddTHH:mm:sszzz</c>）不含这些 token，不会被二次转换。
/// </remarks>
public static class GoDateFormat
{
    /// <summary>长模式优先（避免 "2006" 先于 "2006-01-02" 命中）</summary>
    private static readonly (string Go, string Net)[] Tokens =
    [
        ("2006-01-02T15:04:05Z07:00", "yyyy-MM-ddTHH:mm:sszzz"),
        ("2006-01-02T15:04:05Z", "yyyy-MM-ddTHH:mm:ss'Z'"),
        ("2006-01-02 15:04:05", "yyyy-MM-dd HH:mm:ss"),
        ("Mon, 02 Jan 2006 15:04:05 MST", "ddd, dd MMM yyyy HH:mm:ss 'GMT'"),
        ("Mon Jan 2 15:04:05 2006", "ddd MMM d HH:mm:ss yyyy"),
        ("Monday, January 2, 2006", "dddd, MMMM d, yyyy"),
        ("Mon, 02 Jan 2006", "ddd, dd MMM yyyy"),
        ("January 2, 2006", "MMMM d, yyyy"),
        ("Jan 2, 2006", "MMM d, yyyy"),
        ("2 January 2006", "d MMMM yyyy"),
        // 缩略月 + 日月年（Go 的 `2 Jan 2006` 是常见布局；只有 `2 January 2006`
        // 不在表里时会退化到单 token（"Jan"→"MMM"），日位数字原样留下 →
        // 渲染成 `2 Jan 2024` 而不是 `15 Jan 2024`（bearblog/blog-awesome 的
        // `<time>` 实测布局即此族）
        ("02 January 2006", "dd MMMM yyyy"),
        ("02 Jan, 2006", "dd MMM, yyyy"),
        ("02 Jan 2006", "dd MMM yyyy"),
        ("2 Jan 2006", "d MMM yyyy"),
        ("Jan 2 2006", "MMM d yyyy"),
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
