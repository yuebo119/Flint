// Flint 静态站点生成器
// 模块描述符

namespace Flint.Core.Abstractions;

/// <summary>
/// 模块描述
/// </summary>
public sealed class ModuleDescriptor
{
    /// <summary>
    /// 模块名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 模块版本
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 模块仓库 URL
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// 本地路径
    /// </summary>
    public string? LocalPath { get; init; }
}
