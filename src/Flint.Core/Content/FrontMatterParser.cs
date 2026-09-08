// Flint 静态站点生成器
// Front Matter 解析器——主体：类声明、分隔符常量与 DetectFormat 公开方法
// （读取部分：FrontMatterParser.Reader.cs；写回：Writer.cs；
//   字典映射：DictMapping.cs；日期时区：DateParsing.cs）

using Flint.Core.Abstractions;
using Flint.Core.Models;

namespace Flint.Core.Content;

/// <summary>
/// Front Matter 解析器
/// 支持 YAML (---)、TOML (+++)、JSON ({}) 三种格式
/// </summary>
#pragma warning disable IL2026, IL3050 // AOT 警告在此类中被抑制
public sealed partial class FrontMatterParser : IFrontMatterParser
{
    /// <summary>
    /// YAML 分隔符
    /// </summary>
    private const string YamlDelimiter = "---";

    /// <summary>
    /// TOML 分隔符
    /// </summary>
    private const string TomlDelimiter = "+++";

    /// <inheritdoc />
    public FrontMatterFormat DetectFormat(ReadOnlySpan<char> content)
    {
        return DetectFormatInternal(content) ?? FrontMatterFormat.Yaml;
    }
}
#pragma warning restore IL2026, IL3050
