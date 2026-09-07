// Flint 静态站点生成器
// 性能报告生成器
// 生成 HTML 性能报告，包含图表和趋势分析

using System.Text;
using System.Text.Json;

namespace Flint.IntegrationTests.Performance;

/// <summary>
/// 性能报告生成器
/// 生成 HTML 性能报告，包含图表和趋势分析
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 9.8: 生成性能报告
/// </remarks>
public sealed class PerformanceReportGenerator
{
    private readonly List<PerformanceMetrics> _metrics;
    private readonly PerformanceBaseline _baseline;
    private readonly string _reportTitle;

    /// <summary>
    /// 创建性能报告生成器
    /// </summary>
    /// <param name="reportTitle">报告标题</param>
    /// <param name="baseline">性能基线（可选）</param>
    public PerformanceReportGenerator(string reportTitle = "Flint 性能测试报告", PerformanceBaseline? baseline = null)
    {
        _reportTitle = reportTitle;
        _baseline = baseline ?? new PerformanceBaseline();
        _metrics = [];
    }

    /// <summary>
    /// 添加性能指标
    /// </summary>
    /// <param name="metrics">性能指标</param>
    public void AddMetrics(PerformanceMetrics metrics)
    {
        _metrics.Add(metrics);
    }

    /// <summary>
    /// 添加多个性能指标
    /// </summary>
    /// <param name="metricsList">性能指标列表</param>
    public void AddMetrics(IEnumerable<PerformanceMetrics> metricsList)
    {
        _metrics.AddRange(metricsList);
    }

    /// <summary>
    /// 生成 HTML 报告
    /// </summary>
    /// <param name="outputPath">输出路径</param>
    /// <returns>报告文件路径</returns>
    public async ValueTask<string> GenerateHtmlReportAsync(string outputPath)
    {
        var html = GenerateHtmlContent();

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8);
        return outputPath;
    }

    /// <summary>
    /// 生成 JSON 报告
    /// </summary>
    /// <param name="outputPath">输出路径</param>
    /// <returns>报告文件路径</returns>
    public async ValueTask<string> GenerateJsonReportAsync(string outputPath)
    {
        var report = new PerformanceReport
        {
            Title = _reportTitle,
            GeneratedAt = DateTimeOffset.UtcNow,
            Metrics = _metrics,
            Comparisons = _metrics.Select(m => _baseline.Compare(m)).ToList(),
            Summary = GenerateSummary()
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8);
        return outputPath;
    }

    /// <summary>
    /// 生成 HTML 内容
    /// </summary>
    private string GenerateHtmlContent()
    {
        var comparisons = _metrics.Select(m => _baseline.Compare(m)).ToList();
        var summary = GenerateSummary();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"    <title>{_reportTitle}</title>");
        sb.AppendLine("    <script src=\"https://cdn.jsdelivr.net/npm/chart.js\"></script>");
        sb.AppendLine("    <style>");
        sb.AppendLine(GetCssStyles());
        sb.AppendLine("    </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // 标题
        sb.AppendLine($"    <h1>{_reportTitle}</h1>");
        sb.AppendLine($"    <p class=\"timestamp\">生成时间: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");

        // 摘要
        sb.AppendLine("    <div class=\"summary\">");
        sb.AppendLine("        <h2>测试摘要</h2>");
        sb.AppendLine("        <div class=\"summary-grid\">");
        sb.AppendLine($"            <div class=\"summary-item\"><span class=\"label\">测试数量</span><span class=\"value\">{summary.TotalTests}</span></div>");
        sb.AppendLine($"            <div class=\"summary-item\"><span class=\"label\">通过</span><span class=\"value success\">{summary.PassedTests}</span></div>");
        sb.AppendLine($"            <div class=\"summary-item\"><span class=\"label\">退化</span><span class=\"value {(summary.RegressionTests > 0 ? "danger" : "")}\">{summary.RegressionTests}</span></div>");
        sb.AppendLine($"            <div class=\"summary-item\"><span class=\"label\">提升</span><span class=\"value {(summary.ImprovedTests > 0 ? "success" : "")}\">{summary.ImprovedTests}</span></div>");
        sb.AppendLine($"            <div class=\"summary-item\"><span class=\"label\">平均构建时间</span><span class=\"value\">{summary.AverageBuildTimeMs:F2} ms</span></div>");
        sb.AppendLine($"            <div class=\"summary-item\"><span class=\"label\">总吞吐量</span><span class=\"value\">{summary.TotalThroughput:F2} 文件/秒</span></div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("    </div>");

        // 详细结果表格
        sb.AppendLine("    <div class=\"results\">");
        sb.AppendLine("        <h2>详细结果</h2>");
        sb.AppendLine("        <table>");
        sb.AppendLine("            <thead>");
        sb.AppendLine("                <tr>");
        sb.AppendLine("                    <th>测试名称</th>");
        sb.AppendLine("                    <th>构建时间 (ms)</th>");
        sb.AppendLine("                    <th>基线 (ms)</th>");
        sb.AppendLine("                    <th>偏差</th>");
        sb.AppendLine("                    <th>内存 (MB)</th>");
        sb.AppendLine("                    <th>文件数</th>");
        sb.AppendLine("                    <th>吞吐量</th>");
        sb.AppendLine("                    <th>状态</th>");
        sb.AppendLine("                </tr>");
        sb.AppendLine("            </thead>");
        sb.AppendLine("            <tbody>");

        foreach (var comparison in comparisons)
        {
            var statusClass = comparison.IsRegression ? "danger" : comparison.IsImprovement ? "success" : "";
            var statusText = comparison.IsRegression ? "❌ 退化" : comparison.IsImprovement ? "✅ 提升" : "✓ 正常";
            var baselineText = comparison.HasBaseline ? $"{comparison.Baseline!.BuildTimeMs:F2}" : "-";
            var deviationText = comparison.HasBaseline ? $"{comparison.BuildTimeDeviation:+0.0;-0.0}%" : "-";

            sb.AppendLine("                <tr>");
            sb.AppendLine($"                    <td>{comparison.TestName}</td>");
            sb.AppendLine($"                    <td>{comparison.Current.BuildTimeMs:F2}</td>");
            sb.AppendLine($"                    <td>{baselineText}</td>");
            sb.AppendLine($"                    <td class=\"{statusClass}\">{deviationText}</td>");
            sb.AppendLine($"                    <td>{comparison.Current.PeakMemoryBytes / 1024.0 / 1024.0:F2}</td>");
            sb.AppendLine($"                    <td>{comparison.Current.FilesProcessed}</td>");
            sb.AppendLine($"                    <td>{comparison.Current.Throughput:F2}</td>");
            sb.AppendLine($"                    <td class=\"{statusClass}\">{statusText}</td>");
            sb.AppendLine("                </tr>");
        }

        sb.AppendLine("            </tbody>");
        sb.AppendLine("        </table>");
        sb.AppendLine("    </div>");

        // 图表
        if (_metrics.Count > 0)
        {
            sb.AppendLine("    <div class=\"charts\">");
            sb.AppendLine("        <h2>性能图表</h2>");
            sb.AppendLine("        <div class=\"chart-container\">");
            sb.AppendLine("            <canvas id=\"buildTimeChart\"></canvas>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"chart-container\">");
            sb.AppendLine("            <canvas id=\"throughputChart\"></canvas>");
            sb.AppendLine("        </div>");
            sb.AppendLine("    </div>");

            // 图表脚本
            sb.AppendLine("    <script>");
            sb.AppendLine(GenerateChartScript(comparisons));
            sb.AppendLine("    </script>");
        }

        // GC 统计
        sb.AppendLine("    <div class=\"gc-stats\">");
        sb.AppendLine("        <h2>GC 统计</h2>");
        sb.AppendLine("        <table>");
        sb.AppendLine("            <thead>");
        sb.AppendLine("                <tr>");
        sb.AppendLine("                    <th>测试名称</th>");
        sb.AppendLine("                    <th>Gen0</th>");
        sb.AppendLine("                    <th>Gen1</th>");
        sb.AppendLine("                    <th>Gen2</th>");
        sb.AppendLine("                </tr>");
        sb.AppendLine("            </thead>");
        sb.AppendLine("            <tbody>");

        foreach (var metrics in _metrics)
        {
            sb.AppendLine("                <tr>");
            sb.AppendLine($"                    <td>{metrics.TestName}</td>");
            sb.AppendLine($"                    <td>{metrics.Gen0Collections}</td>");
            sb.AppendLine($"                    <td>{metrics.Gen1Collections}</td>");
            sb.AppendLine($"                    <td class=\"{(metrics.Gen2Collections > 5 ? "warning" : "")}\">{metrics.Gen2Collections}</td>");
            sb.AppendLine("                </tr>");
        }

        sb.AppendLine("            </tbody>");
        sb.AppendLine("        </table>");
        sb.AppendLine("    </div>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// 生成 CSS 样式
    /// </summary>
    private static string GetCssStyles()
    {
        return """
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
                    max-width: 1200px;
                    margin: 0 auto;
                    padding: 20px;
                    background: #f5f5f5;
                    color: #333;
                }
                h1 {
                    color: #2c3e50;
                    border-bottom: 2px solid #3498db;
                    padding-bottom: 10px;
                }
                h2 {
                    color: #34495e;
                    margin-top: 30px;
                }
                .timestamp {
                    color: #7f8c8d;
                    font-size: 0.9em;
                }
                .summary {
                    background: white;
                    padding: 20px;
                    border-radius: 8px;
                    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                    margin: 20px 0;
                }
                .summary-grid {
                    display: grid;
                    grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
                    gap: 15px;
                }
                .summary-item {
                    text-align: center;
                    padding: 15px;
                    background: #f8f9fa;
                    border-radius: 4px;
                }
                .summary-item .label {
                    display: block;
                    font-size: 0.85em;
                    color: #7f8c8d;
                    margin-bottom: 5px;
                }
                .summary-item .value {
                    display: block;
                    font-size: 1.5em;
                    font-weight: bold;
                    color: #2c3e50;
                }
                .results, .gc-stats {
                    background: white;
                    padding: 20px;
                    border-radius: 8px;
                    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                    margin: 20px 0;
                    overflow-x: auto;
                }
                table {
                    width: 100%;
                    border-collapse: collapse;
                }
                th, td {
                    padding: 12px;
                    text-align: left;
                    border-bottom: 1px solid #ecf0f1;
                }
                th {
                    background: #3498db;
                    color: white;
                    font-weight: 500;
                }
                tr:hover {
                    background: #f8f9fa;
                }
                .success { color: #27ae60; }
                .danger { color: #e74c3c; }
                .warning { color: #f39c12; }
                .charts {
                    background: white;
                    padding: 20px;
                    border-radius: 8px;
                    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                    margin: 20px 0;
                }
                .chart-container {
                    position: relative;
                    height: 300px;
                    margin: 20px 0;
                }
            """;
    }

    /// <summary>
    /// 生成图表脚本
    /// </summary>
    private string GenerateChartScript(List<BaselineComparisonResult> comparisons)
    {
        var labels = JsonSerializer.Serialize(_metrics.Select(m => m.TestName).ToArray());
        var buildTimes = JsonSerializer.Serialize(_metrics.Select(m => m.BuildTimeMs).ToArray());
        var baselineTimes = JsonSerializer.Serialize(comparisons.Select(c => c.Baseline?.BuildTimeMs ?? 0).ToArray());
        var throughputs = JsonSerializer.Serialize(_metrics.Select(m => m.Throughput).ToArray());

        return $$"""
            // 构建时间图表
            new Chart(document.getElementById('buildTimeChart'), {
                type: 'bar',
                data: {
                    labels: {{labels}},
                    datasets: [
                        {
                            label: '当前构建时间 (ms)',
                            data: {{buildTimes}},
                            backgroundColor: 'rgba(52, 152, 219, 0.7)',
                            borderColor: 'rgba(52, 152, 219, 1)',
                            borderWidth: 1
                        },
                        {
                            label: '基线构建时间 (ms)',
                            data: {{baselineTimes}},
                            backgroundColor: 'rgba(149, 165, 166, 0.7)',
                            borderColor: 'rgba(149, 165, 166, 1)',
                            borderWidth: 1
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        title: {
                            display: true,
                            text: '构建时间对比'
                        }
                    },
                    scales: {
                        y: {
                            beginAtZero: true,
                            title: {
                                display: true,
                                text: '时间 (ms)'
                            }
                        }
                    }
                }
            });

            // 吞吐量图表
            new Chart(document.getElementById('throughputChart'), {
                type: 'line',
                data: {
                    labels: {{labels}},
                    datasets: [{
                        label: '吞吐量 (文件/秒)',
                        data: {{throughputs}},
                        borderColor: 'rgba(46, 204, 113, 1)',
                        backgroundColor: 'rgba(46, 204, 113, 0.2)',
                        fill: true,
                        tension: 0.4
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        title: {
                            display: true,
                            text: '构建吞吐量'
                        }
                    },
                    scales: {
                        y: {
                            beginAtZero: true,
                            title: {
                                display: true,
                                text: '文件/秒'
                            }
                        }
                    }
                }
            });
            """;
    }

    /// <summary>
    /// 生成摘要
    /// </summary>
    private PerformanceSummary GenerateSummary()
    {
        var comparisons = _metrics.Select(m => _baseline.Compare(m)).ToList();

        return new PerformanceSummary
        {
            TotalTests = _metrics.Count,
            PassedTests = comparisons.Count(c => !c.IsRegression),
            RegressionTests = comparisons.Count(c => c.IsRegression),
            ImprovedTests = comparisons.Count(c => c.IsImprovement),
            AverageBuildTimeMs = _metrics.Count > 0 ? _metrics.Average(m => m.BuildTimeMs) : 0,
            TotalThroughput = _metrics.Count > 0 ? _metrics.Sum(m => m.Throughput) : 0,
            TotalFilesProcessed = _metrics.Sum(m => m.FilesProcessed),
            TotalMemoryUsedBytes = _metrics.Sum(m => m.PeakMemoryBytes)
        };
    }
}

/// <summary>
/// 性能报告
/// </summary>
public sealed class PerformanceReport
{
    /// <summary>
    /// 报告标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 生成时间
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>
    /// 性能指标列表
    /// </summary>
    public required IReadOnlyList<PerformanceMetrics> Metrics { get; init; }

    /// <summary>
    /// 基线比较结果
    /// </summary>
    public required IReadOnlyList<BaselineComparisonResult> Comparisons { get; init; }

    /// <summary>
    /// 摘要
    /// </summary>
    public required PerformanceSummary Summary { get; init; }
}

/// <summary>
/// 性能摘要
/// </summary>
public sealed class PerformanceSummary
{
    /// <summary>
    /// 总测试数
    /// </summary>
    public int TotalTests { get; init; }

    /// <summary>
    /// 通过测试数
    /// </summary>
    public int PassedTests { get; init; }

    /// <summary>
    /// 退化测试数
    /// </summary>
    public int RegressionTests { get; init; }

    /// <summary>
    /// 提升测试数
    /// </summary>
    public int ImprovedTests { get; init; }

    /// <summary>
    /// 平均构建时间
    /// </summary>
    public double AverageBuildTimeMs { get; init; }

    /// <summary>
    /// 总吞吐量
    /// </summary>
    public double TotalThroughput { get; init; }

    /// <summary>
    /// 总处理文件数
    /// </summary>
    public int TotalFilesProcessed { get; init; }

    /// <summary>
    /// 总内存使用
    /// </summary>
    public long TotalMemoryUsedBytes { get; init; }
}
