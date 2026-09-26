// Flint.DevTools 程序入口：开发运维工具集（基准/审计/语料/发布/性能/主题/演示编排）
// 定位：替代原 Python/Bash/PowerShell 开发脚本（见 docs/C-SHARP-MIGRATION-PLAN.md）。
// 刻意与产品 Flint.Cli 分离：产品 AOT 发布面不掺入开发工具。

using System.CommandLine;
using System.Reflection;
using System.Runtime.InteropServices;
using Flint.DevTools.Commands;

namespace Flint.DevTools;

/// <summary>
/// 程序入口
/// </summary>
internal static class Program
{
    /// <summary>
    /// 程序入口点
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Flint 开发运维工具集（bench/audit/corpus/release/perf/theme/demo）");

        rootCommand.Subcommands.Add(BuildVersionCommand());
        rootCommand.Subcommands.Add(AuditCommand.Build());
        rootCommand.Subcommands.Add(CorpusCommand.Build());
        rootCommand.Subcommands.Add(BenchCommand.Build());

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync().ConfigureAwait(false);
    }

    private static Command BuildVersionCommand()
    {
        var cmd = new Command("version", "显示 DevTools 版本与运行时信息");

        cmd.SetAction(_ =>
        {
            var asm = typeof(Program).Assembly;
            var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = informational ?? asm.GetName().Version?.ToString() ?? "unknown";

            Console.WriteLine($"Flint.DevTools {version}");
            Console.WriteLine($"运行时: {Environment.Version} ({RuntimeInformation.FrameworkDescription})");
            Console.WriteLine($"OS: {Environment.OSVersion}");
        });

        return cmd;
    }
}
