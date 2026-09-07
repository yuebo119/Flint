// Flint 静态站点生成器
// 配置树归一化 - 三种格式统一为字典树中间表示

using Tomlyn.Model;

namespace Flint.Core.Configuration;

/// <summary>
/// 配置树归一化器
/// 将 TOML/JSON 解析产物统一为 <c>Dictionary&lt;string, object&gt;</c> 字典树
/// （嵌套表 → 字典、数组 → List&lt;object&gt;、TomlDateTime → DateTimeOffset、键保留原样），
/// 使 ConfigParser 只需一份字典映射（对齐 Hugo 的 map 合并层 + 单份解码思路）。
/// GetDict* 辅助同时兼容 camelCase 与全小写键，故此处不强制小写化。
/// </summary>
public static class ConfigNormalizer
{
    /// <summary>
    /// 将 Tomlyn 解析模型归一化为字典树
    /// </summary>
    public static Dictionary<string, object> NormalizeToml(TomlTable table)
    {
        var dict = new Dictionary<string, object>(table.Count, StringComparer.Ordinal);
        foreach (var (key, value) in table)
        {
            // null ≈ 未设置：跳过该键（TOML 本无 null，防御一致）
            var normalized = NormalizeTomlValue(value);
            if (normalized is not null)
            {
                dict[key] = normalized;
            }
        }
        return dict;
    }

    private static object NormalizeTomlValue(object value)
    {
        if (value is null)
        {
            // null ≈ 未设置：交由上层跳过键（TOML 格式本身无 null，此为防御分支）
            return null!;
        }
        return value switch
        {
            // array-of-tables（[[key]]）须先于 TomlTable 判断：
            // TomlTableArray 继承自 TomlTable，走 TomlTable 分支会被当空表归一化，内容全部丢失
            TomlTableArray ta => ta.Select(t => (object)NormalizeToml(t)).ToList(),
            TomlTable t => NormalizeToml(t),
            TomlArray a => NormalizeTomlArray(a),
            // Tomlyn 的 TomlDateTime 类型携带 DateTimeOffset（公共属性 DateTime），
            // 以动态探测避免对特定 Tomlyn 内部形态的编译期耦合
            _ when value.GetType().Name == "TomlDateTime" => NormalizeTomlDateTime(value),
            _ => value
        };
    }

    private static List<object> NormalizeTomlArray(TomlArray array)
    {
        var list = new List<object>(array.Count);
        foreach (var item in array)
        {
            // null 元素 ≈ 不存在（TOML 本无 null，防御一致）
            if (item is null)
            {
                continue;
            }
            list.Add(item is TomlTable t ? NormalizeToml(t) : item);
        }
        return list;
    }

    private static object NormalizeTomlDateTime(object value)
    {
        // IL2075 局部容忍：动态探测 TomlDateTime.DateTime（见 NormalizeTomlValue 注释），
        // 反射目标类型不在编译期可见，AOT 裁剪下该路径回退原值
#pragma warning disable IL2075
        var dtProp = value.GetType().GetProperty("DateTime");
#pragma warning restore IL2075
        if (dtProp?.GetValue(value) is DateTimeOffset dto)
        {
            return dto;
        }
        return value;
    }

    /// <summary>
    /// 将 System.Text.Json 元素归一化为字典树
    /// </summary>
    public static Dictionary<string, object> NormalizeJson(
        System.Text.Json.JsonElement element)
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var prop in element.EnumerateObject())
        {
            // JSON null ≈ 字段未设置：跳过该键，由取值层 ?? 默认值链统一回退
            //（此前归一化为空串，导致 title: null 在 JSON 下是 "" 而在 YAML 下
            //  回退默认值——同一语义两种结果，与 Hugo 的 null 语义相悖）
            var value = NormalizeJsonValue(prop.Value);
            if (value is not null)
            {
                dict[prop.Name] = value;
            }
        }
        return dict;
    }

    private static object? NormalizeJsonValue(System.Text.Json.JsonElement element) => element.ValueKind switch
    {
        System.Text.Json.JsonValueKind.Object => NormalizeJson(element),
        System.Text.Json.JsonValueKind.Array => element.EnumerateArray()
            .Select(NormalizeJsonValue)
            .Where(v => v is not null)
            .ToList(),
        System.Text.Json.JsonValueKind.String => element.GetString() ?? string.Empty,
        System.Text.Json.JsonValueKind.Number => NormalizeJsonNumber(element),
        System.Text.Json.JsonValueKind.True => true,
        System.Text.Json.JsonValueKind.False => false,
        _ => null // null（及其他未知 Kind）→ 未设置
    };

    /// <summary>
    /// 整数保留 long，非整数才转 double——
    /// 注意三元表达式的类型提升会把 long 统一成 double（49 → 49.0），必须分支返回
    /// </summary>
    private static object NormalizeJsonNumber(System.Text.Json.JsonElement element)
    {
        if (element.TryGetInt64(out var l))
        {
            return l;
        }
        return element.GetDouble();
    }
}
