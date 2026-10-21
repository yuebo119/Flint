// Flint 性能测试入口
// 运行所有性能测试并生成 HTML 报告
// `bench` 子命令跑 BenchmarkDotNet 基准（FLUID-MIGRATION-PLAN P6-1 双引擎渲染）

using System.Text;
using BenchmarkDotNet.Running;

namespace Flint.PerformanceTests;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // bench 子命令：BDN 基准（必须 Release 构建——BDN 拒绝 Debug）。
        // 用法：dotnet run -c Release --project tests/Flint.PerformanceTests -- bench --filter *EngineRenderBenchmarks*
        if (args.Length > 0 && args[0] == "bench")
        {
            var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args.Skip(1).ToArray());
            // 任一基准未产出有效报告即非零退出（BDN 自身失败时 summaries 为空）
            return summaries.Any(s => s.Reports.Any(r => !r.Success)) ? 1 : 0;
        }

        var runner = new PerformanceTestRunner();
        var report = await runner.RunAllTestsAsync();

        // 生成报告
        var reportPath = args.Length > 0
            ? args[0]
            : Path.Combine(Directory.GetCurrentDirectory(), "performance-report.html");

        await HtmlReportGenerator.SaveAsync(report, reportPath);

        Console.WriteLine($"\n📊 性能测试完成!");
        Console.WriteLine($"   总测试数: {report.TotalTests}");
        Console.WriteLine($"   通过: {report.PassedTests}");
        Console.WriteLine($"   失败: {report.FailedTests}");
        Console.WriteLine($"\n📄 报告已保存到: {reportPath}");

        return report.OverallPassed ? 0 : 1;
    }
}
