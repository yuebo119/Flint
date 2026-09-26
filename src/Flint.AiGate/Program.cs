// Flint.AiGate 程序入口：.ai 工作流门禁工具集
// 替代原 .ai/scripts/*.sh（迁移进度见 docs/C-SHARP-MIGRATION-PLAN.md）。

using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;

namespace Flint.AiGate;

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
        // 统一 LF 行尾 + UTF-8 无 BOM（与 Flint.DevTools 同口径，供新旧对拍）
#pragma warning disable CA2000
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n",
            AutoFlush = true,
        });
#pragma warning restore CA2000

        var rootCommand = new RootCommand("Flint .ai 工作流门禁工具（替代 .ai/scripts/*.sh）");

        rootCommand.Subcommands.Add(GateCheckCommand.Build());
        rootCommand.Subcommands.Add(TestGateCommand.Build());
        rootCommand.Subcommands.Add(EncodingGateCommand.Build());
        rootCommand.Subcommands.Add(FlakyGateCommand.Build());
        rootCommand.Subcommands.Add(AssertionStrengthCommand.Build());
        rootCommand.Subcommands.Add(DocConsistencyCommand.Build());
        rootCommand.Subcommands.Add(TechDebtScanCommand.Build());
        rootCommand.Subcommands.Add(ReviewSnapshotCommand.Build());
        rootCommand.Subcommands.Add(ReviewGateCommand.Build());
        rootCommand.Subcommands.Add(ReviewScopeCommand.Build());
        rootCommand.Subcommands.Add(VerifyAiSystemCommand.Build());

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync().ConfigureAwait(false);
    }
}
