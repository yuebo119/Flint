// Flint 静态站点生成器
// 共享 YAML 序列化器（ConfigParser / FrontMatterParser 共用的延迟初始化实例）

#pragma warning disable IL2026, IL3050 // YamlDotNet 构建路径依赖反射，消费方（ConfigParser/FrontMatterParser）已在调用点给出 AOT 指引

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Flint.Core.Configuration;

/// <summary>
/// YAML 反/序列化器共享实例：CamelCase 命名 + 忽略未匹配属性 + 序列化省略默认值。
/// 两处解析管线（站点配置 / Front Matter）共用同一配置，避免逐字重复的延迟初始化块
/// </summary>
internal static class SharedYaml
{
    private static IDeserializer? _deserializer;

    /// <summary>YAML 反序列化器（延迟初始化）</summary>
    internal static IDeserializer Deserializer => _deserializer ??= new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static ISerializer? _serializer;

    /// <summary>YAML 序列化器（延迟初始化）</summary>
    internal static ISerializer Serializer => _serializer ??= new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();
}
