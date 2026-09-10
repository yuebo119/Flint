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

    /// <summary>
    /// 主题描述（theme.toml description，A4 元数据）
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 许可证标识（theme.toml license，如 MIT）
    /// </summary>
    public string? License { get; init; }

    /// <summary>
    /// 主题声明的最低 Hugo 版本（theme.toml min_version；Flint 用于兼容性提示）
    /// </summary>
    public string? MinVersion { get; init; }

    /// <summary>
    /// 主题标签（theme.toml tags，mod list 展示用）
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
