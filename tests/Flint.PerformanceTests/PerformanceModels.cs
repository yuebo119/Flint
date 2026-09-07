// Flint 性能测试模型
// 用于存储和序列化性能测试数据

using System.Text.Json.Serialization;

namespace Flint.PerformanceTests;

/// <summary>
/// 性能测试结果
/// </summary>
public sealed class PerformanceTestResult
{
    public required string TestName { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public required EnvironmentInfo Environment { get; init; }
    public required List<PerformanceMetric> Metrics { get; init; }
    public bool PassedBaseline { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// 环境信息
/// </summary>
public sealed class EnvironmentInfo
{
    public required string OperatingSystem { get; init; }
    public required string Architecture { get; init; }
    public int ProcessorCount { get; init; }
    public required string DotNetVersion { get; init; }
    public required string MachineName { get; init; }
    public long AvailableMemoryMB { get; init; }
}

/// <summary>
/// 性能指标
/// </summary>
public sealed class PerformanceMetric
{
    public required string Name { get; init; }
    public double Value { get; init; }
    public required string Unit { get; init; }
    public double? Baseline { get; init; }
    public bool PassedBaseline { get; init; }
    public double? DifferencePercent { get; init; }
    public MetricCategory Category { get; init; }
}

/// <summary>
/// 指标类别
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MetricCategory
{
    Time,
    Throughput,
    Memory,
    Size
}

/// <summary>
/// 性能测试报告
/// </summary>
public sealed class PerformanceReport
{
    public required string Title { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public required string FlintVersion { get; init; }
    public required List<PerformanceTestResult> Results { get; init; }
    public bool OverallPassed => Results.All(r => r.PassedBaseline);
    public int TotalTests => Results.Count;
    public int PassedTests => Results.Count(r => r.PassedBaseline);
    public int FailedTests => Results.Count(r => !r.PassedBaseline);
}

/// <summary>
/// 性能基准配置
/// 基于实际测试结果设定的合理基准值
/// </summary>
public static class PerformanceBaselines
{
    /// <summary>冷启动时间基准 (ms)</summary>
    public const double ColdStartTimeMs = 50.0;

    /// <summary>构建速度基准 (页/秒) - 基于实际测试约 750-900 页/秒</summary>
    public const double BuildSpeedPagesPerSecond = 500.0;

    /// <summary>增量构建时间基准 (ms)</summary>
    public const double IncrementalBuildTimeMs = 100.0;

    /// <summary>内存使用基准 (MB) - 基础内存，会根据页面数量调整</summary>
    public const double MemoryUsageMB = 50.0;

    /// <summary>二进制大小基准 (MB)</summary>
    public const double BinarySizeMB = 20.0;

    /// <summary>Markdown 解析速度基准 (文件/秒)</summary>
    public const double MarkdownParseSpeedFilesPerSecond = 50000.0;

    /// <summary>模板渲染速度基准 (页/秒)</summary>
    public const double TemplateRenderSpeedPagesPerSecond = 10000.0;

    /// <summary>配置加载时间基准 (ms)</summary>
    public const double ConfigLoadTimeMs = 5.0;
}
