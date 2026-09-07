// Flint 静态站点生成器核心库
// Tomlyn 2.x 解析适配层

using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Flint.Core;

/// <summary>
/// Tomlyn 2.x source-gen 上下文：反射模式 Deserialize 带
/// RequiresUnreferencedCode/RequiresDynamicCode（NativeAOT 不可用），
/// 只注册 TOML 原生模型类型即可覆盖全部解析场景
/// </summary>
[TomlSourceGenerationOptions]
[TomlSerializable(typeof(TomlTable))]
internal sealed partial class FlintTomlContext : TomlSerializerContext
{
}

/// <summary>
/// Tomlyn 2.x 适配：0.20 的 Toml.Parse/ToModel 便捷入口在 2.0 重写中移除，
/// 统一收敛到 TomlSerializer.Deserialize；错误语义对齐旧 HasErrors / 异常两形态
/// </summary>
internal static class TomlynCompat
{
    /// <summary>解析文本为 TomlTable，语法错误返回 null（对齐 0.20 doc.HasErrors 语义）</summary>
    public static TomlTable? TryParseTable(string content)
    {
        try
        {
            return TomlSerializer.Deserialize(content, FlintTomlContext.Default.TomlTable);
        }
        catch (Tomlyn.TomlException)
        {
            return null;
        }
    }

    /// <summary>解析文本为 TomlTable，语法错误抛 TomlException（对齐 0.20 Toml.ToModel 语义）</summary>
    public static TomlTable ParseTable(string content)
    {
        return TomlSerializer.Deserialize(content, FlintTomlContext.Default.TomlTable)
            ?? throw new Tomlyn.TomlException("TOML 解析结果为空");
    }
}
