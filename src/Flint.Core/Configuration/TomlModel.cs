// Flint 静态站点生成器核心库
// TOML 兼容模型：保留历史 TomlTable/TomlArray/TomlTableArray 形态，
// 底层切换到 Tommy，解析适配层负责转换，业务调用点不变。

namespace Flint.Core.Configuration;

/// <summary>
/// TOML 解析失败。替代原 TomlException，保留 catch 语义。
/// </summary>
public sealed class TomlException : Exception
{
    public TomlException(string message)
        : base(message)
    {
    }

    public TomlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>TOML 表：键值对，值可以是标量、TomlTable、TomlArray、TomlTableArray。</summary>
public sealed class TomlTable : Dictionary<string, object?>
{
}

/// <summary>TOML 数组：普通数组。</summary>
public sealed class TomlArray : List<object?>
{
}

/// <summary>TOML 表数组：[[key]] 形态。</summary>
public sealed class TomlTableArray : List<TomlTable>
{
}
