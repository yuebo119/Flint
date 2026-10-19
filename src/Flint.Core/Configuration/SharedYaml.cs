// Flint 静态站点生成器核心库
// 共享 YAML 解析/序列化：底层 VYaml，对外保持 Dictionary<string, object> 形态。
// 解析走 VYaml.Parser 事件流并保留原始标量文本，对齐原 YamlDotNet object 模式：
// 标量一律 String、映射为 Dictionary<object, object>、序列为 List<object>。

using System.Text;
using VYaml;
using VYaml.Annotations;
using VYaml.Parser;
using VYaml.Serialization;

namespace Flint.Core.Configuration;

/// <summary>
/// YAML 反/序列化共享实例：LowerCamelCase 命名 + 序列化省略默认值。
/// 站点配置 / Front Matter / 主题参数 / 数据文件共用，避免逐字重复初始化块。
/// </summary>
internal static class SharedYaml
{
    private static readonly YamlSerializerOptions SerializerOptions = new()
    {
        Resolver = StandardResolver.Instance,
        NamingConvention = NamingConvention.LowerCamelCase,
        DefaultIgnoreCondition = YamlIgnoreCondition.WhenWritingDefault
    };

    /// <summary>解析 YAML 为字典；根节点不是映射时抛 YamlException。</summary>
    public static Dictionary<string, object> DeserializeDictionary(string content)
    {
        var value = DeserializeObject(content);
        return value switch
        {
            null => new Dictionary<string, object>(StringComparer.Ordinal),
            Dictionary<object, object?> map => ToDictionary(map),
            _ => throw new YamlException("YAML 根节点不是映射")
        };
    }

    /// <summary>解析 YAML 为任意对象（data 文件可能是列表或标量）。</summary>
    public static object? DeserializeObject(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var bytes = Encoding.UTF8.GetBytes(content);
        var parser = YamlParser.FromBytes(bytes);
        try
        {
            parser.Read(); // StreamStart
            if (parser.CurrentEventType == ParseEventType.StreamStart)
            {
                parser.Read();
            }

            if (parser.CurrentEventType == ParseEventType.DocumentStart)
            {
                parser.Read();
            }

            if (parser.CurrentEventType is ParseEventType.DocumentEnd or ParseEventType.StreamEnd)
            {
                return null;
            }

            return ParseNode(ref parser);
        }
        catch (Exception ex) when (ex is YamlParserException or YamlTokenizerException)
        {
            throw new YamlException(ex.Message, ex);
        }
    }

    /// <summary>序列化为 YAML。</summary>
    public static string Serialize(Dictionary<string, object> dict)
    {
        ArgumentNullException.ThrowIfNull(dict);
        return YamlSerializer.SerializeToString(dict, SerializerOptions);
    }

    private static Dictionary<string, object> ToDictionary(Dictionary<object, object?> map)
    {
        var result = new Dictionary<string, object>(map.Count, StringComparer.Ordinal);
        foreach (var (key, value) in map)
        {
            result[key?.ToString() ?? ""] = value!;
        }
        return result;
    }

    private static object? ParseNode(ref YamlParser parser)
    {
        switch (parser.CurrentEventType)
        {
            case ParseEventType.MappingStart:
                var map = new Dictionary<object, object?>();
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.MappingEnd)
                {
                    var key = parser.ReadScalarAsString() ?? "";
                    map[key] = ParseNode(ref parser);
                }
                parser.Read();
                return map;

            case ParseEventType.SequenceStart:
                var list = new List<object?>();
                parser.Read();
                while (parser.CurrentEventType != ParseEventType.SequenceEnd)
                {
                    list.Add(ParseNode(ref parser));
                }
                parser.Read();
                return list;

            case ParseEventType.Scalar:
                return parser.ReadScalarAsString();

            default:
                parser.SkipCurrentNode();
                return null;
        }
    }
}
