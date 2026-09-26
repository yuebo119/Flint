// bench 子命令组：ssg / complexity / memory / asset 四个基准（替代 scripts/*.py）

using System.CommandLine;

namespace Flint.DevTools.Commands;

/// <summary>
/// bench 子命令组
/// </summary>
internal static class BenchCommand
{
    internal static Command Build()
    {
        var cmd = new Command("bench", "基准测试族（ssg/complexity/memory/asset）");
        cmd.Subcommands.Add(Bench.SsgBench.BuildCommand());
        cmd.Subcommands.Add(Bench.ComplexityBench.BuildCommand());
        cmd.Subcommands.Add(Bench.MemoryBench.BuildCommand());
        cmd.Subcommands.Add(Bench.AssetBench.BuildCommand());
        return cmd;
    }
}
