// Flint 静态站点生成器
// 配置加载器接口

using Flint.Core.Configuration;

namespace Flint.Core.Abstractions;

/// <summary>
/// 配置加载器接口
/// </summary>
public interface IConfigLoader
{
    /// <summary>
    /// 加载站点配置
    /// </summary>
    /// <param name="configPath">配置文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>站点配置</returns>
    ValueTask<SiteConfig> LoadAsync(
        string configPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步加载配置
    /// </summary>
    /// <param name="configPath">配置文件路径</param>
    /// <returns>站点配置</returns>
    SiteConfig Load(string configPath);

    /// <summary>
    /// 自动检测并加载配置文件
    /// </summary>
    /// <param name="directory">站点目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>站点配置</returns>
    ValueTask<SiteConfig> AutoLoadAsync(
        string directory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <param name="configPath">配置文件路径</param>
    /// <param name="format">配置格式</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask SaveAsync(
        SiteConfig config,
        string configPath,
        ConfigFormat format = ConfigFormat.Toml,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 配置验证器接口
/// </summary>
public interface IConfigValidator
{
    /// <summary>
    /// 验证配置
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <returns>验证结果</returns>
    ValidationResult Validate(SiteConfig config);
}

/// <summary>
/// 验证结果
/// </summary>
public readonly record struct ValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// 验证错误列表
    /// </summary>
    public required IReadOnlyList<ValidationError> Errors { get; init; }

    /// <summary>
    /// 创建成功的验证结果
    /// </summary>
    public static ValidationResult Success => new()
    {
        IsValid = true,
        Errors = []
    };

    /// <summary>
    /// 创建失败的验证结果
    /// </summary>
    public static ValidationResult Failure(params ValidationError[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}

/// <summary>
/// 验证错误
/// </summary>
public readonly record struct ValidationError
{
    /// <summary>
    /// 属性路径
    /// </summary>
    public required string PropertyPath { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 期望值
    /// </summary>
    public string? ExpectedValue { get; init; }

    /// <summary>
    /// 实际值
    /// </summary>
    public string? ActualValue { get; init; }
}
