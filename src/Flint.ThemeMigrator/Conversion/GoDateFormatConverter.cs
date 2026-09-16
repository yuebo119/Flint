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

    /// <summary>是否像 Go 布局串（含 2006/15:04 等特征）</summary>
    public static bool LooksLikeGoLayout(string s) =>
        Flint.Core.Templates.GoDateFormat.LooksLikeGoLayout(s);
}
