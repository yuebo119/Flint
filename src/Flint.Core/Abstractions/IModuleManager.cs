// Flint 静态站点生成器
// 模块管理器接口

namespace Flint.Core.Abstractions;

/// <summary>
/// 模块管理器接口
/// </summary>
public interface IModuleManager
{
    /// <summary>
    /// 获取并安装模块
    /// </summary>
    /// <param name="repository">仓库地址</param>
    /// <param name="version">版本（可选，默认最新）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模块描述</returns>
    ValueTask<ModuleDescriptor> GetAsync(
        string repository,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 更新模块
    /// </summary>
    /// <param name="moduleName">模块名称</param>
    /// <param name="version">目标版本（可选，默认最新）</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask UpdateAsync(
        string moduleName,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出已安装的模块
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>模块列表</returns>
    ValueTask<IReadOnlyList<ModuleDescriptor>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 初始化模块配置
    /// </summary>
    /// <param name="path">站点路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask InitAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除模块
    /// </summary>
    /// <param name="moduleName">模块名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask RemoveAsync(
        string moduleName,
        CancellationToken cancellationToken = default);
}

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
    /// 模块描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 模块依赖
    /// </summary>
    public required IReadOnlyList<ModuleDependency> Dependencies { get; init; }

    /// <summary>
    /// 本地路径
    /// </summary>
    public string? LocalPath { get; init; }

    /// <summary>
    /// 是否为本地模块
    /// </summary>
    public bool IsLocal { get; init; }

    /// <summary>
    /// 挂载点配置
    /// </summary>
    public IReadOnlyList<ModuleMountPoint> Mounts { get; init; } = [];
}

/// <summary>
/// 模块依赖
/// </summary>
public readonly record struct ModuleDependency
{
    /// <summary>
    /// 依赖模块名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 版本约束
    /// </summary>
    public required string VersionConstraint { get; init; }

    /// <summary>
    /// 是否为可选依赖
    /// </summary>
    public bool Optional { get; init; }
}

/// <summary>
/// 模块挂载点
/// </summary>
public readonly record struct ModuleMountPoint
{
    /// <summary>
    /// 源路径（模块内）
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// 目标路径（站点内）
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// 挂载类型
    /// </summary>
    public MountType Type { get; init; }
}

/// <summary>
/// 挂载类型
/// </summary>
public enum MountType
{
    /// <summary>
    /// 内容
    /// </summary>
    Content,

    /// <summary>
    /// 静态文件
    /// </summary>
    Static,

    /// <summary>
    /// 布局
    /// </summary>
    Layouts,

    /// <summary>
    /// 资源
    /// </summary>
    Assets,

    /// <summary>
    /// 数据
    /// </summary>
    Data,

    /// <summary>
    /// 原型
    /// </summary>
    Archetypes,

    /// <summary>
    /// 国际化
    /// </summary>
    I18n
}
