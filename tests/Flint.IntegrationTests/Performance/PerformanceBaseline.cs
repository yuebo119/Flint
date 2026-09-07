// Flint 静态站点生成器
// 性能基线类
// 定义性能指标结构和基线比较逻辑

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flint.IntegrationTests.Performance;

/// <summary>
/// 性能指标
/// 记录单次性能测试的结果
/// </summary>
public sealed record PerformanceMetrics
{
    /// <summary>
    /// 测试名称
    /// </summary>
    public required string TestName { get; init; }

    /// <summary>
    /// 测试时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 构建时间（毫秒）
    /// </summary>
    public double BuildTimeMs { get; init; }

    /// <summary>
    /// 内存使用峰值（字节）
    /// </summary>
    public long PeakMemoryBytes { get; init; }

    /// <summary>
    /// GC 集合次数（Gen0）
    /// </summary>
    public int Gen0Collections { get; init; }

    /// <summary>
    /// GC 集合次数（Gen1）
    /// </summary>
    public int Gen1Collections { get; init; }

    /// <summary>
    /// GC 集合次数（Gen2）
    /// </summary>
    public int Gen2Collections { get; init; }

    /// <summary>
    /// 处理的文件数量
    /// </summary>
    public int FilesProcessed { get; init; }

    /// <summary>
    /// 输出文件数量
    /// </summary>
    public int OutputFilesGenerated { get; init; }

    /// <summary>
    /// 每个文件的平均处理时间（毫秒）
    /// </summary>
    public double AverageTimePerFileMs => FilesProcessed > 0 ? BuildTimeMs / FilesProcessed : 0;

    /// <summary>
    /// 吞吐量（文件/秒）
    /// </summary>
    public double Throughput => BuildTimeMs > 0 ? FilesProcessed / (BuildTimeMs / 1000.0) : 0;

    /// <summary>
    /// 额外的自定义指标
    /// </summary>
    public Dictionary<string, double> CustomMetrics { get; init; } = [];
}

/// <summary>
/// 性能基线
/// 存储和比较性能基线数据
/// </summary>
public sealed class PerformanceBaseline
{
    private readonly Dictionary<string, PerformanceMetrics> _baselines;
    private readonly string _baselinePath;
    private readonly double _defaultThreshold;

    /// <summary>
    /// 默认阈值（百分比偏差）
    /// </summary>
    public const double DefaultThresholdPercent = 20.0;

    /// <summary>
    /// 创建性能基线实例
    /// </summary>
    /// <param name="baselinePath">基线文件路径</param>
    /// <param name="defaultThreshold">默认阈值（百分比）</param>
    public PerformanceBaseline(string? baselinePath = null, double defaultThreshold = DefaultThresholdPercent)
    {
        _baselinePath = baselinePath ?? GetDefaultBaselinePath();
        _defaultThreshold = defaultThreshold;
        _baselines = [];
    }

    /// <summary>
    /// 基线数据
    /// </summary>
    public IReadOnlyDictionary<string, PerformanceMetrics> Baselines => _baselines;

    /// <summary>
    /// 基线文件路径
    /// </summary>
    public string BaselinePath => _baselinePath;

    /// <summary>
    /// 默认阈值
    /// </summary>
    public double DefaultThreshold => _defaultThreshold;

    #region 基线加载和保存

    /// <summary>
    /// 从文件加载基线
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功加载</returns>
    public async ValueTask<bool> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_baselinePath))
        {
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_baselinePath, cancellationToken);
            var data = JsonSerializer.Deserialize<BaselineData>(json, JsonOptions);

            if (data?.Baselines != null)
            {
                _baselines.Clear();
                foreach (var (key, value) in data.Baselines)
                {
                    _baselines[key] = value;
                }
                return true;
            }
        }
        catch (JsonException)
        {
            // 基线文件格式错误，忽略
        }

        return false;
    }

    /// <summary>
    /// 保存基线到文件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        var data = new BaselineData
        {
            Version = "1.0",
            LastUpdated = DateTimeOffset.UtcNow,
            Baselines = new Dictionary<string, PerformanceMetrics>(_baselines)
        };

        var directory = Path.GetDirectoryName(_baselinePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(_baselinePath, json, cancellationToken);
    }

    #endregion

    #region 基线更新

    /// <summary>
    /// 更新基线
    /// </summary>
    /// <param name="metrics">性能指标</param>
    public void UpdateBaseline(PerformanceMetrics metrics)
    {
        _baselines[metrics.TestName] = metrics;
    }

    /// <summary>
    /// 更新基线（如果新值更好）
    /// </summary>
    /// <param name="metrics">性能指标</param>
    /// <returns>是否更新了基线</returns>
    public bool UpdateBaselineIfBetter(PerformanceMetrics metrics)
    {
        if (!_baselines.TryGetValue(metrics.TestName, out var existing))
        {
            _baselines[metrics.TestName] = metrics;
            return true;
        }

        // 如果新的构建时间更短，更新基线
        if (metrics.BuildTimeMs < existing.BuildTimeMs)
        {
            _baselines[metrics.TestName] = metrics;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 移除基线
    /// </summary>
    /// <param name="testName">测试名称</param>
    /// <returns>是否成功移除</returns>
    public bool RemoveBaseline(string testName)
    {
        return _baselines.Remove(testName);
    }

    /// <summary>
    /// 清除所有基线
    /// </summary>
    public void ClearBaselines()
    {
        _baselines.Clear();
    }

    #endregion

    #region 基线比较

    /// <summary>
    /// 比较性能指标与基线
    /// </summary>
    /// <param name="metrics">当前性能指标</param>
    /// <param name="threshold">阈值（百分比），默认使用构造函数指定的阈值</param>
    /// <returns>比较结果</returns>
    public BaselineComparisonResult Compare(PerformanceMetrics metrics, double? threshold = null)
    {
        var effectiveThreshold = threshold ?? _defaultThreshold;

        if (!_baselines.TryGetValue(metrics.TestName, out var baseline))
        {
            return new BaselineComparisonResult
            {
                TestName = metrics.TestName,
                HasBaseline = false,
                Current = metrics,
                Baseline = null,
                BuildTimeDeviation = 0,
                MemoryDeviation = 0,
                IsWithinThreshold = true,
                Threshold = effectiveThreshold
            };
        }

        var buildTimeDeviation = CalculateDeviation(metrics.BuildTimeMs, baseline.BuildTimeMs);
        var memoryDeviation = CalculateDeviation(metrics.PeakMemoryBytes, baseline.PeakMemoryBytes);

        // 构建时间超过阈值则认为性能退化
        var isWithinThreshold = buildTimeDeviation <= effectiveThreshold;

        return new BaselineComparisonResult
        {
            TestName = metrics.TestName,
            HasBaseline = true,
            Current = metrics,
            Baseline = baseline,
            BuildTimeDeviation = buildTimeDeviation,
            MemoryDeviation = memoryDeviation,
            IsWithinThreshold = isWithinThreshold,
            Threshold = effectiveThreshold
        };
    }

    /// <summary>
    /// 批量比较性能指标
    /// </summary>
    /// <param name="metricsList">性能指标列表</param>
    /// <param name="threshold">阈值（百分比）</param>
    /// <returns>比较结果列表</returns>
    public IReadOnlyList<BaselineComparisonResult> CompareAll(
        IEnumerable<PerformanceMetrics> metricsList,
        double? threshold = null)
    {
        return metricsList.Select(m => Compare(m, threshold)).ToList();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 计算偏差百分比
    /// </summary>
    /// <param name="current">当前值</param>
    /// <param name="baseline">基线值</param>
    /// <returns>偏差百分比（正值表示增加，负值表示减少）</returns>
    private static double CalculateDeviation(double current, double baseline)
    {
        if (baseline == 0)
        {
            return current == 0 ? 0 : 100;
        }

        return ((current - baseline) / baseline) * 100;
    }

    /// <summary>
    /// 获取默认基线文件路径
    /// </summary>
    private static string GetDefaultBaselinePath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "TestData", "performance-baseline.json");
    }

    /// <summary>
    /// JSON 序列化选项
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    #endregion
}

/// <summary>
/// 基线数据结构
/// </summary>
internal sealed class BaselineData
{
    /// <summary>
    /// 版本号
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; }

    /// <summary>
    /// 基线数据
    /// </summary>
    public Dictionary<string, PerformanceMetrics> Baselines { get; init; } = [];
}

/// <summary>
/// 基线比较结果
/// </summary>
public sealed record BaselineComparisonResult
{
    /// <summary>
    /// 测试名称
    /// </summary>
    public required string TestName { get; init; }

    /// <summary>
    /// 是否有基线数据
    /// </summary>
    public bool HasBaseline { get; init; }

    /// <summary>
    /// 当前性能指标
    /// </summary>
    public required PerformanceMetrics Current { get; init; }

    /// <summary>
    /// 基线性能指标
    /// </summary>
    public PerformanceMetrics? Baseline { get; init; }

    /// <summary>
    /// 构建时间偏差（百分比）
    /// </summary>
    public double BuildTimeDeviation { get; init; }

    /// <summary>
    /// 内存使用偏差（百分比）
    /// </summary>
    public double MemoryDeviation { get; init; }

    /// <summary>
    /// 是否在阈值范围内
    /// </summary>
    public bool IsWithinThreshold { get; init; }

    /// <summary>
    /// 使用的阈值
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>
    /// 是否性能退化
    /// </summary>
    public bool IsRegression => HasBaseline && !IsWithinThreshold && BuildTimeDeviation > 0;

    /// <summary>
    /// 是否性能提升
    /// </summary>
    public bool IsImprovement => HasBaseline && BuildTimeDeviation < -5; // 超过 5% 的提升

    /// <summary>
    /// 获取结果摘要
    /// </summary>
    public string GetSummary()
    {
        if (!HasBaseline)
        {
            return $"[{TestName}] 无基线数据，当前: {Current.BuildTimeMs:F2}ms";
        }

        var status = IsRegression ? "❌ 退化" : IsImprovement ? "✅ 提升" : "✓ 正常";
        return $"[{TestName}] {status} - 当前: {Current.BuildTimeMs:F2}ms, 基线: {Baseline!.BuildTimeMs:F2}ms, 偏差: {BuildTimeDeviation:+0.0;-0.0}%";
    }
}

/// <summary>
/// 性能测试辅助类
/// </summary>
public static class PerformanceTestHelper
{
    /// <summary>
    /// 测量操作的性能
    /// </summary>
    /// <param name="testName">测试名称</param>
    /// <param name="action">要测量的操作</param>
    /// <param name="filesProcessed">处理的文件数量</param>
    /// <param name="outputFilesGenerated">生成的输出文件数量</param>
    /// <returns>性能指标</returns>
    public static async ValueTask<PerformanceMetrics> MeasureAsync(
        string testName,
        Func<Task> action,
        int filesProcessed = 0,
        int outputFilesGenerated = 0)
    {
        // 强制 GC 以获得更准确的内存测量
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);
        var memoryBefore = GC.GetTotalMemory(false);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await action();

        stopwatch.Stop();

        var memoryAfter = GC.GetTotalMemory(false);
        var gen0After = GC.CollectionCount(0);
        var gen1After = GC.CollectionCount(1);
        var gen2After = GC.CollectionCount(2);

        return new PerformanceMetrics
        {
            TestName = testName,
            BuildTimeMs = stopwatch.Elapsed.TotalMilliseconds,
            PeakMemoryBytes = Math.Max(memoryAfter - memoryBefore, 0),
            Gen0Collections = gen0After - gen0Before,
            Gen1Collections = gen1After - gen1Before,
            Gen2Collections = gen2After - gen2Before,
            FilesProcessed = filesProcessed,
            OutputFilesGenerated = outputFilesGenerated
        };
    }

    /// <summary>
    /// 测量同步操作的性能
    /// </summary>
    /// <param name="testName">测试名称</param>
    /// <param name="action">要测量的操作</param>
    /// <param name="filesProcessed">处理的文件数量</param>
    /// <param name="outputFilesGenerated">生成的输出文件数量</param>
    /// <returns>性能指标</returns>
    public static PerformanceMetrics Measure(
        string testName,
        Action action,
        int filesProcessed = 0,
        int outputFilesGenerated = 0)
    {
        return MeasureAsync(testName, () =>
        {
            action();
            return Task.CompletedTask;
        }, filesProcessed, outputFilesGenerated).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 多次运行并取平均值
    /// </summary>
    /// <param name="testName">测试名称</param>
    /// <param name="action">要测量的操作</param>
    /// <param name="iterations">迭代次数</param>
    /// <param name="warmupIterations">预热迭代次数</param>
    /// <returns>平均性能指标</returns>
    public static async ValueTask<PerformanceMetrics> MeasureAverageAsync(
        string testName,
        Func<Task> action,
        int iterations = 5,
        int warmupIterations = 2)
    {
        // 预热
        for (int i = 0; i < warmupIterations; i++)
        {
            await action();
        }

        var results = new List<PerformanceMetrics>();

        for (int i = 0; i < iterations; i++)
        {
            var metrics = await MeasureAsync($"{testName}_iter{i}", action);
            results.Add(metrics);
        }

        // 计算平均值
        return new PerformanceMetrics
        {
            TestName = testName,
            BuildTimeMs = results.Average(r => r.BuildTimeMs),
            PeakMemoryBytes = (long)results.Average(r => r.PeakMemoryBytes),
            Gen0Collections = (int)results.Average(r => r.Gen0Collections),
            Gen1Collections = (int)results.Average(r => r.Gen1Collections),
            Gen2Collections = (int)results.Average(r => r.Gen2Collections),
            FilesProcessed = results.FirstOrDefault()?.FilesProcessed ?? 0,
            OutputFilesGenerated = results.FirstOrDefault()?.OutputFilesGenerated ?? 0
        };
    }
}
