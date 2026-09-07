// Flint 静态站点生成器
// 数据文件加载器（AOT 兼容）

using System.Text.Json;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Serialization;

namespace Flint.Core.Site;

/// <summary>
/// 数据文件加载器
/// 加载 data/ 目录下的 YAML/TOML/JSON 文件
/// </summary>
#pragma warning disable IL2026, IL3050 // YAML 反序列化需要反射
public sealed class DataFileLoader
{
    private readonly IDeserializer _yamlDeserializer;

    public DataFileLoader()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithAttemptingUnquotedStringTypeDeserialization()
            .Build();
    }

    /// <summary>
    /// 加载数据目录
    /// </summary>
    public async Task<IReadOnlyDictionary<string, object>> LoadAsync(
        string dataPath,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object>();

        if (!Directory.Exists(dataPath))
        {
            return result;
        }

        await LoadDirectoryAsync(dataPath, result, "", cancellationToken);
        return result;
    }

    private async Task LoadDirectoryAsync(
        string directory,
        Dictionary<string, object> target,
        string prefix,
        CancellationToken cancellationToken)
    {
        // 加载文件
        foreach (var file in Directory.GetFiles(directory))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var name = Path.GetFileNameWithoutExtension(file);
            var key = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";

            try
            {
                var data = ext switch
                {
                    ".yaml" or ".yml" => await LoadYamlAsync(file, cancellationToken),
                    ".toml" => await LoadTomlAsync(file, cancellationToken),
                    ".json" => await LoadJsonAsync(file, cancellationToken),
                    _ => null
                };

                if (data != null)
                {
                    target[key] = data;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 坏数据文件静默跳过（模板侧 .Site.Data 缺键可见）；
                // 取消必须传播，不能被当成"无法解析的文件"吞掉
                _ = ex;
            }
        }

        // 递归加载子目录
        foreach (var subDir in Directory.GetDirectories(directory))
        {
            var dirName = Path.GetFileName(subDir);
            var newPrefix = string.IsNullOrEmpty(prefix) ? dirName : $"{prefix}.{dirName}";
            await LoadDirectoryAsync(subDir, target, newPrefix, cancellationToken);
        }
    }

    private async Task<object?> LoadYamlAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return _yamlDeserializer.Deserialize<object>(content);
    }

    private static async Task<object?> LoadTomlAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        // 使用 Tomlyn 低级 API（AOT 兼容）
        var doc = Tomlyn.Toml.Parse(content);
        if (doc.HasErrors)
            return null;
        return ConvertTomlTableToDict(doc.ToModel());
    }

    private static async Task<object?> LoadJsonAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        using var doc = JsonDocument.Parse(content);
        return ConvertJsonElementToObject(doc.RootElement);
    }

    // AOT 兼容的 TOML 转换
    private static Dictionary<string, object> ConvertTomlTableToDict(TomlTable table)
    {
        var dict = new Dictionary<string, object>();
        foreach (var kvp in table)
        {
            dict[kvp.Key] = ConvertTomlValue(kvp.Value);
        }
        return dict;
    }

    private static object ConvertTomlValue(object? value)
    {
        return value switch
        {
            // array-of-tables（[[key]]）须先于 TomlTable 判断：TomlTableArray 继承自
            // TomlTable，走 TomlTable 分支会被当空表，data 目录的数组表内容全部丢失
            //（与 ConfigNormalizer.NormalizeTomlValue 同根因同修法）
            TomlTableArray ta => ta.Select(ConvertTomlTableToDict).ToList(),
            TomlTable t => ConvertTomlTableToDict(t),
            TomlArray a => a.Select(ConvertTomlValue).ToList(),
            null => "",
            _ => value
        };
    }

    // AOT 兼容的 JSON 转换
    private static object? ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElementToObject(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElementToObject)
                .ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }
}
#pragma warning restore IL2026, IL3050
