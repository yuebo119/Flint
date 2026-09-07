// Flint 静态站点生成器
// 构建结果类型

namespace Flint.Core.Models;

/// <summary>
/// 构建结果
/// </summary>
public sealed class BuildResult
{
    /// <summary>
    /// 构建是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 构建的页面数量
    /// </summary>
    public required int PagesBuilt { get; init; }

    /// <summary>
    /// 处理的资源数量
    /// </summary>
    public required int AssetsProcessed { get; init; }

    /// <summary>
    /// 构建耗时
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// 内存使用量（字节）
    /// </summary>
    public required long MemoryUsed { get; init; }

    /// <summary>
    /// 错误列表
    /// </summary>
    public required IReadOnlyList<BuildError> Errors { get; init; }

    /// <summary>
    /// 警告列表
    /// </summary>
    public required IReadOnlyList<BuildWarning> Warnings { get; init; }

    /// <summary>
    /// 输出目录
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// 输出文件总大小（字节）
    /// </summary>
    public long TotalOutputSize { get; init; }

    /// <summary>
    /// 构建统计信息
    /// </summary>
    public BuildStatistics? Statistics { get; init; }

    /// <summary>
    /// 创建成功的构建结果
    /// </summary>
    public static BuildResult Successful(
        int pagesBuilt,
        int assetsProcessed,
        TimeSpan duration,
        long memoryUsed,
        string outputPath,
        IReadOnlyList<BuildWarning>? warnings = null) => new()
        {
            Success = true,
            PagesBuilt = pagesBuilt,
            AssetsProcessed = assetsProcessed,
            Duration = duration,
            MemoryUsed = memoryUsed,
            OutputPath = outputPath,
            Errors = [],
            Warnings = warnings ?? []
        };

    /// <summary>
    /// 创建失败的构建结果
    /// </summary>
    public static BuildResult Failed(
        IReadOnlyList<BuildError> errors,
        TimeSpan duration,
        long memoryUsed,
        string outputPath,
        IReadOnlyList<BuildWarning>? warnings = null) => new()
        {
            Success = false,
            PagesBuilt = 0,
            AssetsProcessed = 0,
            Duration = duration,
            MemoryUsed = memoryUsed,
            OutputPath = outputPath,
            Errors = errors,
            Warnings = warnings ?? []
        };
}

/// <summary>
/// 构建错误
/// </summary>
public readonly record struct BuildError
{
    /// <summary>
    /// 错误代码
    /// </summary>
    public required string ErrorCode { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 行号（从 1 开始）
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// 列号（从 1 开始）
    /// </summary>
    public required int Column { get; init; }

    /// <summary>
    /// 错误上下文（相关代码片段）
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// 修复建议
    /// </summary>
    public string? Suggestion { get; init; }

    /// <summary>
    /// 错误严重程度
    /// </summary>
    public ErrorSeverity Severity { get; init; }
}

/// <summary>
/// 构建警告
/// </summary>
public readonly record struct BuildWarning
{
    /// <summary>
    /// 警告代码
    /// </summary>
    public required string WarningCode { get; init; }

    /// <summary>
    /// 警告消息
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 文件路径
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// 行号
    /// </summary>
    public int? Line { get; init; }

    /// <summary>
    /// 列号
    /// </summary>
    public int? Column { get; init; }
}

/// <summary>
/// 错误严重程度
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// 信息
    /// </summary>
    Info,

    /// <summary>
    /// 警告
    /// </summary>
    Warning,

    /// <summary>
    /// 错误
    /// </summary>
    Error,

    /// <summary>
    /// 致命错误
    /// </summary>
    Fatal
}

/// <summary>
/// 构建统计信息
/// </summary>
public sealed class BuildStatistics
{
    /// <summary>
    /// 内容解析耗时
    /// </summary>
    public TimeSpan ParsingTime { get; init; }

    /// <summary>
    /// 模板渲染耗时
    /// </summary>
    public TimeSpan RenderingTime { get; init; }

    /// <summary>
    /// 资源处理耗时
    /// </summary>
    public TimeSpan AssetProcessingTime { get; init; }

    /// <summary>
    /// 文件写入耗时
    /// </summary>
    public TimeSpan WritingTime { get; init; }

    /// <summary>
    /// 各类型页面数量
    /// </summary>
    public IReadOnlyDictionary<string, int> PagesByType { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// 各类型资源数量
    /// </summary>
    public IReadOnlyDictionary<string, int> AssetsByType { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// 缓存命中次数
    /// </summary>
    public int CacheHits { get; init; }

    /// <summary>
    /// 缓存未命中次数
    /// </summary>
    public int CacheMisses { get; init; }

    /// <summary>
    /// 缓存命中率
    /// </summary>
    public double CacheHitRate => CacheHits + CacheMisses > 0
        ? (double)CacheHits / (CacheHits + CacheMisses)
        : 0;
}
