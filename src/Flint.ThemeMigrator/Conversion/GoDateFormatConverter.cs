// Flint 主题迁移工具
// Go 时间布局 → .NET 格式串转换（**委托引擎实现**，单一来源）
//
// Go 用"参考时间"（Mon Jan 2 15:04:05 MST 2006）表示格式，.NET 用占位符。
// 表与运行期转换都在 Flint.Core.Templates.GoDateFormat：引擎侧必须能转换布局
// （Hugo 的 time.Format 常把布局放在 site.Params.dateFormat 之类的**运行期表达式**里，
// 迁移期看不到），迁移器只是同一张表的另一个调用方——两处各执一份表会漂移。

namespace Flint.ThemeMigrator.Conversion;

/// <summary>Go layout token → .NET 格式串（转发 <see cref="Flint.Core.Templates.GoDateFormat"/>）</summary>
internal static class GoDateFormatConverter
{
    /// <summary>转换 Go 布局串为 .NET 格式串</summary>
    public static string Convert(string goLayout) =>
        Flint.Core.Templates.GoDateFormat.Convert(goLayout);

    /// <summary>
    /// 供**产出**用的转换：**含时区 token 的布局原样保留**（Go 形态），交由引擎在运行期
    /// 转换——.NET 的自定义格式串没有"无冒号偏移"（Go 的 <c>-0700</c> 输出 "+0000"、
    /// <c>zzz</c> 输出 "+00:00"），也没有时区缩写（Go 的 <c>MST</c> → "UTC"），
    /// 只有引擎在拿到**原始 Go 布局**时才能做这两处后处理。
    /// 不含时区 token 的布局照旧编译期转换（产物里是 .NET 串，读起来更直观）
    /// </summary>
    public static string ConvertForEmit(string goLayout) =>
        HasTimeZoneToken(goLayout) ? goLayout : Convert(goLayout);

    /// <summary>是否含 Go 的时区 token（<c>-07</c>/<c>Z07</c>/<c>MST</c> 族）</summary>
    private static bool HasTimeZoneToken(string layout) =>
        layout.Contains("-07", StringComparison.Ordinal) ||
        layout.Contains("Z07", StringComparison.Ordinal) ||
        layout.Contains("MST", StringComparison.Ordinal);

    /// <summary>是否像 Go 布局串（含 2006/15:04 等特征）</summary>
    public static bool LooksLikeGoLayout(string s) =>
        Flint.Core.Templates.GoDateFormat.LooksLikeGoLayout(s);
}
