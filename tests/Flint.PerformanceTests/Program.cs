// Flint 性能测试入口
// 运行所有性能测试并生成 HTML 报告

using System.Text;

namespace Flint.PerformanceTests;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

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
