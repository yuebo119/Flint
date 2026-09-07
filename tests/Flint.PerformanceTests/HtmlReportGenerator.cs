// Flint 性能测试 HTML 报告生成器
// 优化版本：更直观、简洁的报告展示

using System.Text;
using System.Text.Json;

namespace Flint.PerformanceTests;

/// <summary>
/// HTML 报告生成器 - 优化版
/// </summary>
public static class HtmlReportGenerator
{
    public static string Generate(PerformanceReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-CN\">");
        sb.AppendLine("<head>");
        sb.AppendLine("    <meta charset=\"UTF-8\">");
        sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"    <title>{report.Title}</title>");
        sb.AppendLine(GetStyles());
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine(GenerateHeader(report));
        sb.AppendLine(GenerateSummaryCards(report));
        sb.AppendLine(GenerateQuickStats(report));
        sb.AppendLine(GenerateTestResults(report));
        sb.AppendLine(GenerateCharts(report));

        if (report.Results.Count > 0)
        {
            sb.AppendLine(GenerateEnvironmentInfo(report.Results[0].Environment));
        }

        sb.AppendLine(GenerateFooter(report));
        sb.AppendLine(GetScripts(report));

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string GetStyles()
    {
        return @"
    <style>
        :root {
            --primary: #6366f1;
            --success: #10b981;
            --warning: #f59e0b;
            --danger: #ef4444;
            --bg: #0f172a;
            --bg-card: #1e293b;
            --bg-card-hover: #334155;
            --text: #f1f5f9;
            --text-muted: #94a3b8;
            --text-dim: #64748b;
            --border: #334155;
        }
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: var(--bg);
            color: var(--text);
            line-height: 1.6;
        }
        .container { max-width: 1200px; margin: 0 auto; padding: 0 1.5rem; }
        
        .header {
            background: linear-gradient(135deg, rgba(99, 102, 241, 0.15) 0%, rgba(139, 92, 246, 0.1) 100%);
            border-bottom: 1px solid var(--border);
            padding: 2rem 0;
        }
        .header-content { display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 1rem; }
        .header-left { display: flex; align-items: center; gap: 1rem; }
        .logo { font-size: 2.5rem; }
        .header h1 { font-size: 1.75rem; font-weight: 700; }
        .version { font-size: 0.75rem; padding: 0.25rem 0.5rem; background: var(--primary); border-radius: 4px; font-weight: 600; margin-left: 0.5rem; }
        .header-right { text-align: right; }
        .timestamp { font-size: 0.875rem; color: var(--text-muted); }
        
        .status-pill {
            display: inline-flex; align-items: center; gap: 0.5rem;
            padding: 0.5rem 1rem; border-radius: 9999px;
            font-weight: 600; font-size: 0.875rem;
        }
        .status-pill.success { background: rgba(16, 185, 129, 0.2); color: #34d399; }
        .status-pill.danger { background: rgba(239, 68, 68, 0.2); color: #f87171; }
        
        .summary-section { padding: 2rem 0; }
        .summary-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1rem; }
        .stat-card {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.25rem;
            transition: all 0.2s ease;
            position: relative;
            overflow: hidden;
        }
        .stat-card::before {
            content: '';
            position: absolute;
            top: 0; left: 0; right: 0;
            height: 3px;
            background: var(--card-accent, var(--primary));
        }
        .stat-card:hover { transform: translateY(-2px); border-color: var(--card-accent, var(--primary)); }
        .stat-card.success { --card-accent: var(--success); }
        .stat-card.danger { --card-accent: var(--danger); }
        .stat-card.warning { --card-accent: var(--warning); }
        .stat-label { font-size: 0.75rem; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 0.5rem; }
        .stat-value { font-size: 2rem; font-weight: 700; }
        .stat-card.success .stat-value { color: var(--success); }
        .stat-card.danger .stat-value { color: var(--danger); }
        
        .pass-rate-ring { display: flex; align-items: center; gap: 1rem; }
        .ring-chart {
            width: 60px; height: 60px;
            border-radius: 50%;
            background: conic-gradient(var(--ring-color) var(--ring-percent), var(--border) 0);
            display: flex; align-items: center; justify-content: center;
        }
        .ring-chart::before { content: ''; width: 44px; height: 44px; background: var(--bg-card); border-radius: 50%; }
        .ring-value { font-size: 1.5rem; font-weight: 700; }

        .quick-stats {
            display: flex; gap: 2rem; padding: 1rem 1.5rem;
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 12px;
            margin-bottom: 2rem;
        }
        .quick-stat { display: flex; align-items: center; gap: 0.5rem; }
        .quick-stat-icon { font-size: 1.25rem; }
        .quick-stat-label { font-size: 0.875rem; color: var(--text-muted); }
        .quick-stat-value { font-weight: 600; }
        
        .filter-bar { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
        .filter-btn {
            padding: 0.5rem 1rem;
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 8px;
            color: var(--text-muted);
            font-size: 0.875rem;
            cursor: pointer;
            transition: all 0.2s;
        }
        .filter-btn:hover { border-color: var(--primary); color: var(--text); }
        .filter-btn.active { background: var(--primary); border-color: var(--primary); color: white; }
        .filter-btn .count { margin-left: 0.5rem; padding: 0.125rem 0.5rem; background: rgba(255,255,255,0.2); border-radius: 9999px; font-size: 0.75rem; }
        
        .results-section { padding-bottom: 2rem; }
        .section-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 1.5rem; }
        .section-title { font-size: 1.25rem; font-weight: 600; display: flex; align-items: center; gap: 0.5rem; }
        
        .test-card {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 12px;
            margin-bottom: 0.75rem;
            overflow: hidden;
            transition: all 0.2s;
        }
        .test-card:hover { border-color: var(--text-dim); }
        .test-card.passed { border-left: 3px solid var(--success); }
        .test-card.failed { border-left: 3px solid var(--danger); }
        .test-card.expanded { box-shadow: 0 4px 16px rgba(0,0,0,0.2); }
        
        .test-header {
            display: grid;
            grid-template-columns: auto 1fr auto auto;
            align-items: center;
            gap: 1rem;
            padding: 1rem 1.25rem;
            cursor: pointer;
            transition: background 0.2s;
        }
        .test-header:hover { background: var(--bg-card-hover); }
        
        .test-status-icon {
            width: 32px; height: 32px;
            border-radius: 8px;
            display: flex; align-items: center; justify-content: center;
            font-size: 1rem;
        }
        .test-status-icon.pass { background: rgba(16, 185, 129, 0.2); color: var(--success); }
        .test-status-icon.fail { background: rgba(239, 68, 68, 0.2); color: var(--danger); }
        
        .test-info h3 { font-size: 0.9375rem; font-weight: 600; margin-bottom: 0.125rem; }
        .test-info p { font-size: 0.8125rem; color: var(--text-dim); }
        
        .test-metrics-preview { display: flex; gap: 1.5rem; }
        .metric-preview { text-align: right; }
        .metric-preview-value { font-size: 0.9375rem; font-weight: 600; font-family: monospace; }
        .metric-preview-label { font-size: 0.6875rem; color: var(--text-dim); text-transform: uppercase; }
        
        .expand-icon {
            width: 24px; height: 24px;
            display: flex; align-items: center; justify-content: center;
            color: var(--text-dim);
            transition: transform 0.2s;
        }
        .test-card.expanded .expand-icon { transform: rotate(180deg); }

        .test-body {
            display: none;
            padding: 0 1.25rem 1.25rem;
            border-top: 1px solid var(--border);
            background: rgba(0,0,0,0.2);
        }
        .test-card.expanded .test-body { display: block; }
        
        .metrics-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 0.75rem;
            padding-top: 1rem;
        }
        .metric-item {
            background: var(--bg);
            border-radius: 8px;
            padding: 1rem;
            position: relative;
        }
        .metric-item.pass { border-left: 2px solid var(--success); }
        .metric-item.fail { border-left: 2px solid var(--danger); }
        
        .metric-name { font-size: 0.75rem; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.03em; margin-bottom: 0.5rem; }
        .metric-value-row { display: flex; align-items: baseline; gap: 0.5rem; }
        .metric-actual { font-size: 1.25rem; font-weight: 700; font-family: monospace; }
        .metric-unit { font-size: 0.75rem; color: var(--text-muted); }
        .metric-baseline { font-size: 0.75rem; color: var(--text-dim); margin-top: 0.25rem; }
        .metric-diff {
            position: absolute;
            top: 0.75rem; right: 0.75rem;
            font-size: 0.75rem; font-weight: 600;
            padding: 0.125rem 0.5rem;
            border-radius: 4px;
        }
        .metric-diff.positive { background: rgba(16, 185, 129, 0.2); color: var(--success); }
        .metric-diff.negative { background: rgba(239, 68, 68, 0.2); color: var(--danger); }
        
        .charts-section { padding-bottom: 2rem; }
        .charts-grid { display: grid; grid-template-columns: 2fr 1fr; gap: 1rem; }
        .chart-card {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 12px;
            padding: 1.25rem;
        }
        .chart-title { font-size: 0.875rem; font-weight: 600; margin-bottom: 1rem; color: var(--text-muted); }
        .chart-wrapper { position: relative; height: 280px; width: 100%; }
        .chart-wrapper canvas { position: absolute; top: 0; left: 0; }
        
        .env-section { padding-bottom: 2rem; }
        .env-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 0.75rem; }
        .env-item {
            background: var(--bg-card);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 1rem;
        }
        .env-label { font-size: 0.6875rem; color: var(--text-dim); text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 0.25rem; }
        .env-value { font-size: 0.875rem; font-weight: 500; }
        
        .footer {
            text-align: center;
            padding: 2rem;
            border-top: 1px solid var(--border);
            color: var(--text-dim);
            font-size: 0.8125rem;
        }
        
        @media (max-width: 768px) {
            .summary-grid { grid-template-columns: repeat(2, 1fr); }
            .charts-grid { grid-template-columns: 1fr; }
            .test-header { grid-template-columns: auto 1fr auto; }
            .test-metrics-preview { display: none; }
            .quick-stats { flex-wrap: wrap; gap: 1rem; }
        }
        @media (max-width: 480px) {
            .summary-grid { grid-template-columns: 1fr; }
            .header-content { flex-direction: column; text-align: center; }
            .header-right { text-align: center; }
        }
    </style>
";
    }

    private static string GenerateHeader(PerformanceReport report)
    {
        var statusClass = report.OverallPassed ? "success" : "danger";
        var statusIcon = report.OverallPassed ? "✓" : "✗";
        var statusText = report.OverallPassed ? "全部通过" : "存在失败";

        return $@"
    <header class=""header"">
        <div class=""container"">
            <div class=""header-content"">
                <div class=""header-left"">
                    <span class=""logo"">🚀</span>
                    <div>
                        <h1>Flint 性能测试<span class=""version"">v{report.FlintVersion}</span></h1>
                    </div>
                </div>
                <div class=""header-right"">
                    <div class=""status-pill {statusClass}"">
                        <span>{statusIcon}</span>
                        <span>{statusText}</span>
                    </div>
                    <p class=""timestamp"">📅 {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}</p>
                </div>
            </div>
        </div>
    </header>
";
    }

    private static string GenerateSummaryCards(PerformanceReport report)
    {
        var passRate = report.TotalTests > 0 ? (double)report.PassedTests / report.TotalTests * 100 : 0;
        var ringColor = passRate >= 70 ? "var(--success)" : passRate >= 40 ? "var(--warning)" : "var(--danger)";

        return $@"
    <section class=""summary-section"">
        <div class=""container"">
            <div class=""summary-grid"">
                <div class=""stat-card"">
                    <div class=""stat-label"">总测试数</div>
                    <div class=""stat-value"">{report.TotalTests}</div>
                </div>
                <div class=""stat-card success"">
                    <div class=""stat-label"">通过</div>
                    <div class=""stat-value"">{report.PassedTests}</div>
                </div>
                <div class=""stat-card danger"">
                    <div class=""stat-label"">失败</div>
                    <div class=""stat-value"">{report.FailedTests}</div>
                </div>
                <div class=""stat-card warning"">
                    <div class=""stat-label"">通过率</div>
                    <div class=""pass-rate-ring"">
                        <div class=""ring-chart"" style=""--ring-percent: {passRate}%; --ring-color: {ringColor};""></div>
                        <div class=""ring-value"">{passRate:F0}%</div>
                    </div>
                </div>
            </div>
        </div>
    </section>
";
    }

    private static string GenerateQuickStats(PerformanceReport report)
    {
        var totalDuration = report.Results.Sum(r => r.Metrics.FirstOrDefault(m => m.Name.Contains("时间"))?.Value ?? 0);
        var avgSpeed = report.Results
            .SelectMany(r => r.Metrics)
            .Where(m => m.Name.Contains("速度") && m.Value > 0)
            .Select(m => m.Value)
            .DefaultIfEmpty(0)
            .Average();

        return $@"
    <div class=""container"">
        <div class=""quick-stats"">
            <div class=""quick-stat"">
                <span class=""quick-stat-icon"">⏱️</span>
                <span class=""quick-stat-label"">总耗时</span>
                <span class=""quick-stat-value"">{FormatDuration(totalDuration)}</span>
            </div>
            <div class=""quick-stat"">
                <span class=""quick-stat-icon"">⚡</span>
                <span class=""quick-stat-label"">平均速度</span>
                <span class=""quick-stat-value"">{avgSpeed:F0} 项/秒</span>
            </div>
            <div class=""quick-stat"">
                <span class=""quick-stat-icon"">🔧</span>
                <span class=""quick-stat-label"">测试项目</span>
                <span class=""quick-stat-value"">{report.Results.Count} 个</span>
            </div>
        </div>
    </div>
";
    }

    private static string FormatDuration(double ms)
    {
        if (ms < 1000)
            return $"{ms:F0}ms";
        if (ms < 60000)
            return $"{ms / 1000:F1}s";
        return $"{ms / 60000:F1}min";
    }

    private static string GenerateTestResults(PerformanceReport report)
    {
        var sb = new StringBuilder();

        sb.AppendLine($@"
    <section class=""results-section"">
        <div class=""container"">
            <div class=""section-header"">
                <h2 class=""section-title"">📋 测试详情</h2>
                <div class=""filter-bar"">
                    <button class=""filter-btn active"" onclick=""filterTests('all')"">
                        全部<span class=""count"">{report.TotalTests}</span>
                    </button>
                    <button class=""filter-btn"" onclick=""filterTests('passed')"">
                        通过<span class=""count"">{report.PassedTests}</span>
                    </button>
                    <button class=""filter-btn"" onclick=""filterTests('failed')"">
                        失败<span class=""count"">{report.FailedTests}</span>
                    </button>
                </div>
            </div>
            <div id=""test-list"">");

        foreach (var result in report.Results)
        {
            var statusClass = result.PassedBaseline ? "passed" : "failed";
            var iconClass = result.PassedBaseline ? "pass" : "fail";
            var statusIcon = result.PassedBaseline ? "✓" : "✗";
            var expandedClass = result.PassedBaseline ? "" : " expanded";
            var previewMetrics = result.Metrics.Take(2).ToList();

            sb.AppendLine($@"
                <div class=""test-card {statusClass}{expandedClass}"" data-status=""{statusClass}"">
                    <div class=""test-header"" onclick=""toggleTest(this)"">
                        <div class=""test-status-icon {iconClass}"">{statusIcon}</div>
                        <div class=""test-info"">
                            <h3>{result.TestName}</h3>
                            <p>{result.Description}</p>
                        </div>
                        <div class=""test-metrics-preview"">");

            foreach (var metric in previewMetrics)
            {
                sb.AppendLine($@"
                            <div class=""metric-preview"">
                                <div class=""metric-preview-value"">{FormatMetricValue(metric.Value)}</div>
                                <div class=""metric-preview-label"">{metric.Name}</div>
                            </div>");
            }

            sb.AppendLine(@"
                        </div>
                        <div class=""expand-icon"">▼</div>
                    </div>
                    <div class=""test-body"">
                        <div class=""metrics-grid"">");

            foreach (var metric in result.Metrics)
            {
                var metricClass = metric.PassedBaseline ? "pass" : "fail";
                var diffHtml = "";
                if (metric.DifferencePercent.HasValue)
                {
                    var diffClass = metric.DifferencePercent > 0 ? "positive" : "negative";
                    var diffSign = metric.DifferencePercent > 0 ? "+" : "";
                    diffHtml = $@"<span class=""metric-diff {diffClass}"">{diffSign}{metric.DifferencePercent:F1}%</span>";
                }

                var baselineHtml = metric.Baseline.HasValue
                    ? $"基准: {metric.Baseline:F2} {metric.Unit}"
                    : "";

                sb.AppendLine($@"
                            <div class=""metric-item {metricClass}"">
                                <div class=""metric-name"">{metric.Name}</div>
                                <div class=""metric-value-row"">
                                    <span class=""metric-actual"">{metric.Value:F2}</span>
                                    <span class=""metric-unit"">{metric.Unit}</span>
                                </div>
                                <div class=""metric-baseline"">{baselineHtml}</div>
                                {diffHtml}
                            </div>");
            }

            sb.AppendLine(@"
                        </div>
                    </div>
                </div>");
        }

        sb.AppendLine(@"
            </div>
        </div>
    </section>");

        return sb.ToString();
    }

    private static string FormatMetricValue(double value)
    {
        if (value >= 10000)
            return $"{value / 1000:F1}K";
        if (value >= 1000)
            return $"{value:F0}";
        if (value >= 100)
            return $"{value:F1}";
        return $"{value:F2}";
    }

    private static string GenerateCharts(PerformanceReport report)
    {
        return @"
    <section class=""charts-section"">
        <div class=""container"">
            <div class=""section-header"">
                <h2 class=""section-title"">📈 性能概览</h2>
            </div>
            <div class=""charts-grid"">
                <div class=""chart-card"">
                    <div class=""chart-title"">构建速度对比 (页/秒)</div>
                    <div class=""chart-wrapper"">
                        <canvas id=""speedChart""></canvas>
                    </div>
                </div>
                <div class=""chart-card"">
                    <div class=""chart-title"">测试结果分布</div>
                    <div class=""chart-wrapper"">
                        <canvas id=""resultChart""></canvas>
                    </div>
                </div>
            </div>
            <div class=""charts-grid"" style=""margin-top: 1rem;"">
                <div class=""chart-card"">
                    <div class=""chart-title"">内存使用对比 (总分配量 MB)</div>
                    <div class=""chart-wrapper"">
                        <canvas id=""memoryChart""></canvas>
                    </div>
                </div>
                <div class=""chart-card"">
                    <div class=""chart-title"">内存增量对比 (MB)</div>
                    <div class=""chart-wrapper"">
                        <canvas id=""memoryDeltaChart""></canvas>
                    </div>
                </div>
            </div>
            <div class=""charts-grid"" style=""margin-top: 1rem;"">
                <div class=""chart-card"">
                    <div class=""chart-title"">Markdown 解析性能 (文件/秒)</div>
                    <div class=""chart-wrapper"">
                        <canvas id=""markdownChart""></canvas>
                    </div>
                </div>
                <div class=""chart-card"">
                    <div class=""chart-title"">模板渲染性能 (页/秒)</div>
                    <div class=""chart-wrapper"">
                        <canvas id=""templateChart""></canvas>
                    </div>
                </div>
            </div>
        </div>
    </section>
";
    }

    private static string GenerateEnvironmentInfo(EnvironmentInfo env)
    {
        return $@"
    <section class=""env-section"">
        <div class=""container"">
            <div class=""section-header"">
                <h2 class=""section-title"">💻 测试环境</h2>
            </div>
            <div class=""env-grid"">
                <div class=""env-item"">
                    <div class=""env-label"">操作系统</div>
                    <div class=""env-value"">{env.OperatingSystem}</div>
                </div>
                <div class=""env-item"">
                    <div class=""env-label"">处理器架构</div>
                    <div class=""env-value"">{env.Architecture}</div>
                </div>
                <div class=""env-item"">
                    <div class=""env-label"">CPU 核心</div>
                    <div class=""env-value"">{env.ProcessorCount} 核</div>
                </div>
                <div class=""env-item"">
                    <div class=""env-label"">.NET 版本</div>
                    <div class=""env-value"">{env.DotNetVersion}</div>
                </div>
                <div class=""env-item"">
                    <div class=""env-label"">机器名称</div>
                    <div class=""env-value"">{env.MachineName}</div>
                </div>
                <div class=""env-item"">
                    <div class=""env-label"">可用内存</div>
                    <div class=""env-value"">{env.AvailableMemoryMB:N0} MB</div>
                </div>
            </div>
        </div>
    </section>
";
    }

    private static string GenerateFooter(PerformanceReport report)
    {
        return $@"
    <footer class=""footer"">
        <p>Flint 性能测试报告 · {report.GeneratedAt:yyyy-MM-dd HH:mm:ss}</p>
        <p>Powered by Flint v{report.FlintVersion}</p>
    </footer>
";
    }

    private static string GetScripts(PerformanceReport report)
    {
        // 收集构建速度数据（站点构建测试）
        var speedData = new List<(string label, double speed, double? baseline)>();
        foreach (var result in report.Results.Where(r => r.TestName.Contains("站点构建")))
        {
            var speedMetric = result.Metrics.FirstOrDefault(m => m.Name == "构建速度");
            if (speedMetric != null)
            {
                var label = result.TestName.Split('(')[0].Trim();
                speedData.Add((label, speedMetric.Value, speedMetric.Baseline));
            }
        }

        // 收集内存数据（总分配量）- 显示基准
        var memoryData = new List<(string label, double totalAlloc, double? baseline)>();
        foreach (var result in report.Results.Where(r => r.TestName.Contains("站点构建")))
        {
            var allocMetric = result.Metrics.FirstOrDefault(m => m.Name == "总分配量");
            if (allocMetric != null)
            {
                var label = result.TestName.Split('(')[0].Trim();
                // 使用总分配量的基准值
                memoryData.Add((label, allocMetric.Value, allocMetric.Baseline));
            }
        }

        // 收集内存增量数据 - 不显示基准
        var memoryDeltaData = new List<(string label, double delta)>();
        foreach (var result in report.Results.Where(r => r.TestName.Contains("站点构建")))
        {
            var deltaMetric = result.Metrics.FirstOrDefault(m => m.Name == "内存增量");
            if (deltaMetric != null)
            {
                var label = result.TestName.Split('(')[0].Trim();
                memoryDeltaData.Add((label, deltaMetric.Value));
            }
        }

        // 收集 Markdown 解析性能数据
        var markdownResult = report.Results.FirstOrDefault(r => r.TestName.Contains("Markdown"));
        var markdownSpeed = markdownResult?.Metrics.FirstOrDefault(m => m.Name == "解析速度");
        var markdownBaseline = markdownSpeed?.Baseline ?? 50000;

        // 收集模板渲染性能数据
        var templateResult = report.Results.FirstOrDefault(r => r.TestName.Contains("模板渲染"));
        var templateSpeed = templateResult?.Metrics.FirstOrDefault(m => m.Name == "渲染速度");
        var templateBaseline = templateSpeed?.Baseline ?? 10000;

        // 生成 JSON 数据
        var speedLabelsJson = JsonSerializer.Serialize(speedData.Select(d => d.label).ToArray());
        var speedValuesJson = JsonSerializer.Serialize(speedData.Select(d => Math.Round(d.speed, 1)).ToArray());
        var speedBaselinesJson = JsonSerializer.Serialize(speedData.Select(d => d.baseline ?? 0).ToArray());

        var memoryLabelsJson = JsonSerializer.Serialize(memoryData.Select(d => d.label).ToArray());
        var memoryValuesJson = JsonSerializer.Serialize(memoryData.Select(d => Math.Round(d.totalAlloc, 2)).ToArray());
        var memoryBaselinesJson = JsonSerializer.Serialize(memoryData.Select(d => d.baseline ?? 0).ToArray());

        var memoryDeltaLabelsJson = JsonSerializer.Serialize(memoryDeltaData.Select(d => d.label).ToArray());
        var memoryDeltaValuesJson = JsonSerializer.Serialize(memoryDeltaData.Select(d => Math.Round(d.delta, 2)).ToArray());

        return $@"
    <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>
    <script>
        function toggleTest(header) {{
            header.closest('.test-card').classList.toggle('expanded');
        }}
        
        function filterTests(status) {{
            document.querySelectorAll('.filter-btn').forEach(btn => btn.classList.remove('active'));
            event.target.closest('.filter-btn').classList.add('active');
            
            document.querySelectorAll('.test-card').forEach(card => {{
                if (status === 'all') {{
                    card.style.display = '';
                }} else {{
                    card.style.display = card.dataset.status === status ? '' : 'none';
                }}
            }});
        }}
        
        // 全局禁用动画，防止图表不断重绘
        Chart.defaults.animation = false;
        Chart.defaults.color = '#94a3b8';
        Chart.defaults.borderColor = '#334155';
        
        // 构建速度对比图表
        new Chart(document.getElementById('speedChart'), {{
            type: 'bar',
            data: {{
                labels: {speedLabelsJson},
                datasets: [
                    {{
                        label: '实际速度',
                        data: {speedValuesJson},
                        backgroundColor: '#6366f1',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }},
                    {{
                        label: '基准速度',
                        data: {speedBaselinesJson},
                        backgroundColor: '#64748b',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }}
                ]
            }},
            options: {{
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                plugins: {{
                    legend: {{ position: 'top' }},
                    tooltip: {{
                        callbacks: {{
                            label: function(ctx) {{
                                return ctx.dataset.label + ': ' + ctx.raw + ' 页/秒';
                            }}
                        }}
                    }}
                }},
                scales: {{
                    x: {{ grid: {{ color: '#1e293b' }} }},
                    y: {{
                        beginAtZero: true,
                        grid: {{ color: '#1e293b' }},
                        ticks: {{
                            callback: function(v) {{ return v + ' 页/秒'; }}
                        }}
                    }}
                }}
            }}
        }});
        
        // 测试结果分布图
        new Chart(document.getElementById('resultChart'), {{
            type: 'doughnut',
            data: {{
                labels: ['通过', '失败'],
                datasets: [{{
                    data: [{report.PassedTests}, {report.FailedTests}],
                    backgroundColor: ['#10b981', '#ef4444'],
                    borderWidth: 0,
                    spacing: 2
                }}]
            }},
            options: {{
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                cutout: '65%',
                plugins: {{
                    legend: {{ position: 'bottom', labels: {{ padding: 20 }} }}
                }}
            }}
        }});

        // 内存使用对比图表（总分配量）- 显示基准
        new Chart(document.getElementById('memoryChart'), {{
            type: 'bar',
            data: {{
                labels: {memoryLabelsJson},
                datasets: [
                    {{
                        label: '总分配量',
                        data: {memoryValuesJson},
                        backgroundColor: '#8b5cf6',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }},
                    {{
                        label: '基准限制',
                        data: {memoryBaselinesJson},
                        backgroundColor: '#64748b',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }}
                ]
            }},
            options: {{
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                plugins: {{
                    legend: {{ position: 'top' }},
                    tooltip: {{
                        callbacks: {{
                            label: function(ctx) {{
                                return ctx.dataset.label + ': ' + ctx.raw + ' MB';
                            }}
                        }}
                    }}
                }},
                scales: {{
                    x: {{ grid: {{ color: '#1e293b' }} }},
                    y: {{
                        beginAtZero: true,
                        grid: {{ color: '#1e293b' }},
                        ticks: {{
                            callback: function(v) {{ return v + ' MB'; }}
                        }}
                    }}
                }}
            }}
        }});

        // 内存增量图表 - 不显示基准
        new Chart(document.getElementById('memoryDeltaChart'), {{
            type: 'bar',
            data: {{
                labels: {memoryDeltaLabelsJson},
                datasets: [{{
                    label: '内存增量',
                    data: {memoryDeltaValuesJson},
                    backgroundColor: '#ec4899',
                    borderRadius: 4,
                    barPercentage: 0.6
                }}]
            }},
            options: {{
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                plugins: {{
                    legend: {{ display: false }},
                    tooltip: {{
                        callbacks: {{
                            label: function(ctx) {{
                                return '内存增量: ' + ctx.raw + ' MB';
                            }}
                        }}
                    }}
                }},
                scales: {{
                    x: {{ grid: {{ color: '#1e293b' }} }},
                    y: {{
                        beginAtZero: true,
                        grid: {{ color: '#1e293b' }},
                        ticks: {{
                            callback: function(v) {{ return v + ' MB'; }}
                        }}
                    }}
                }}
            }}
        }});

        // Markdown 解析性能图表
        new Chart(document.getElementById('markdownChart'), {{
            type: 'bar',
            data: {{
                labels: ['Markdown 解析'],
                datasets: [
                    {{
                        label: '实际速度',
                        data: [{markdownSpeed?.Value ?? 0:F0}],
                        backgroundColor: '#10b981',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }},
                    {{
                        label: '基准速度',
                        data: [{markdownBaseline:F0}],
                        backgroundColor: '#64748b',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }}
                ]
            }},
            options: {{
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                indexAxis: 'y',
                plugins: {{
                    legend: {{ position: 'top' }},
                    tooltip: {{
                        callbacks: {{
                            label: function(ctx) {{
                                return ctx.dataset.label + ': ' + ctx.raw.toLocaleString() + ' 文件/秒';
                            }}
                        }}
                    }}
                }},
                scales: {{
                    x: {{
                        beginAtZero: true,
                        grid: {{ color: '#1e293b' }},
                        ticks: {{
                            callback: function(v) {{ return (v/1000).toFixed(0) + 'K'; }}
                        }}
                    }},
                    y: {{ grid: {{ display: false }} }}
                }}
            }}
        }});

        // 模板渲染性能图表
        new Chart(document.getElementById('templateChart'), {{
            type: 'bar',
            data: {{
                labels: ['模板渲染'],
                datasets: [
                    {{
                        label: '实际速度',
                        data: [{templateSpeed?.Value ?? 0:F0}],
                        backgroundColor: '#f59e0b',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }},
                    {{
                        label: '基准速度',
                        data: [{templateBaseline:F0}],
                        backgroundColor: '#64748b',
                        borderRadius: 4,
                        barPercentage: 0.4
                    }}
                ]
            }},
            options: {{
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                indexAxis: 'y',
                plugins: {{
                    legend: {{ position: 'top' }},
                    tooltip: {{
                        callbacks: {{
                            label: function(ctx) {{
                                return ctx.dataset.label + ': ' + ctx.raw.toLocaleString() + ' 页/秒';
                            }}
                        }}
                    }}
                }},
                scales: {{
                    x: {{
                        beginAtZero: true,
                        grid: {{ color: '#1e293b' }},
                        ticks: {{
                            callback: function(v) {{ return (v/1000).toFixed(0) + 'K'; }}
                        }}
                    }},
                    y: {{ grid: {{ display: false }} }}
                }}
            }}
        }});
    </script>
";
    }

    public static async Task SaveAsync(PerformanceReport report, string outputPath)
    {
        var html = Generate(report);
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(outputPath, html);
    }
}
