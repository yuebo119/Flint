// Flint 静态站点生成器
// XML 产物写出约定（sitemap / RSS / Atom 共用）

using System.Text;
using System.Xml;

namespace Flint.Core.Site;

/// <summary>
/// XML 产物声明约定：声明中的编码必须与实际落盘字节一致。
/// <para>
/// <see cref="XmlWriter.Create(StringBuilder, XmlWriterSettings)"/> 对字符串目标是
/// 忽略 <c>settings.Encoding</c> 的：声明恒写 <c>utf-16</c>，而调用方用
/// <see cref="File.WriteAllTextAsync(string, string, CancellationToken)"/>（UTF-8 无 BOM）
/// 落盘，严格解析器（XmlReader、sitemap 校验器、feed 阅读器）会直接拒绝。
/// 故统一关闭自动声明，由本类型补一条 UTF-8 声明。
/// </para>
/// </summary>
internal static class XmlOutput
{
    internal const string Utf8Preamble = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n";

    internal static XmlWriterSettings CreateSettings(bool indent) => new()
    {
        Indent = indent,
        // 声明由调用方手工拼接，见类型注释
        OmitXmlDeclaration = true
    };

    internal static string WithPreamble(StringBuilder content) =>
        string.Concat(Utf8Preamble, content.ToString());
}
