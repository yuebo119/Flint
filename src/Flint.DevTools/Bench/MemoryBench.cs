// bench memory：三语料 × 双引擎构建进程峰值 RSS/USS 采样（scripts/memory-bench.py 的 C# 移植）
// 口径：每语料 2 轮取中位数。RSS = Process.WorkingSet64；USS 代理 = Process.PrivateMemorySize64
// （与 Python 版 psutil.uss 实现不同，报告中已标注 API 来源，对拍容差 ±15%）。

using System.CommandLine;

namespace Flint.DevTools.Bench;

/// <summary>
/// 内存峰值采样基准
/// </summary>
internal static class MemoryBench
{
    private sealed record Job(string Name, string Exe, string[] Args, string Pub);

    internal static Command BuildCommand()
    {
        var runsOpt = new Option<int>("--runs") { Description = "每语料采样轮数", DefaultValueFactory = _ => 2 };

        var cmd = new Command("memory", "构建进程峰值内存采样（2 轮中位数）");
        cmd.Options.Add(runsOpt);

        cmd.SetAction(async parseResult =>
        {
            var runs = parseResult.GetValue(runsOpt);
            var corpus = RepoGuard.CorpusRoot;
            var ssg = Path.Combine(corpus, "ssg-bench");
            var merged = Path.Combine(corpus, "corpus-merged");

            // hbench-merged 已停用：双引擎产出页数口径差异不可比（与 Python 版同一裁决）
            var jobs = new[]
            {
                new Job("万页合成-Hugo", DefaultPaths.HugoExe,
                    new[] { "-s", Path.Combine(ssg, "hugo"), "-d", Path.Combine(ssg, "hugo-pub"), "--quiet" },
                    Path.Combine(ssg, "hugo-pub")),
                new Job("万页合成-Flint", DefaultPaths.FlintExe,
                    new[] { "build", "-s", Path.Combine(ssg, "flint"), "-o", Path.Combine(ssg, "flint-pub") },
                    Path.Combine(ssg, "flint-pub")),
                new Job("MDN14621-Hugo", DefaultPaths.HugoExe,
                    new[] { "-s", Path.Combine(merged, "hugo"), "-d", Path.Combine(merged, "hugo-pub"), "--quiet" },
                    Path.Combine(merged, "hugo-pub")),
                new Job("MDN14621-Flint", DefaultPaths.FlintExe,
                    new[] { "build", "-s", Path.Combine(merged, "flint"), "-o", Path.Combine(merged, "flint-pub") },
                    Path.Combine(merged, "flint-pub")),
            };

            Console.WriteLine($"=== 内存峰值采样（{runs} 轮中位数，MB）===");
            foreach (var job in jobs)
            {
                ProcessRunner.RequireExe(job.Exe);

                var rssList = new List<double>(runs);
                var ussList = new List<double>(runs);
                for (var i = 0; i < runs; i++)
                {
                    RepoGuard.DeleteTree(job.Pub);
                    // memory 基准不读计时，允许做 Working Set - Private 计数器解析（数百毫秒级）
                    var result = await ProcessRunner.RunTimedAsync(job.Exe, job.Args, null, sampleUss: true).ConfigureAwait(false);
                    result.AssertSuccess(job.Name);
                    rssList.Add(result.PeakRssBytes / 1048576.0);
                    ussList.Add(result.PeakUssProxyBytes / 1048576.0);
                }

                Console.WriteLine(
                    $"{job.Name}: rss中位={ProcessRunner.Median(rssList):F0}MB uss中位={ProcessRunner.Median(ussList):F0}MB");
            }

            return 0;
        });

        return cmd;
    }
}
