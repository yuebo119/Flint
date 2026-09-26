// 基准进程执行器：外部高精度计时（含进程启动，两引擎同口径）、退出码断言、
// 内存峰值采样（RSS 用 Process.WorkingSet64；USS 用 PrivateMemorySize64 作代理口径，
// 与 Python 版 psutil.uss 实现不同，报告中已标注 API 来源，对拍容差 ±15%）。

using System.Diagnostics;

namespace Flint.DevTools.Bench;

/// <summary>
/// 一次外部进程运行的结果
/// </summary>
internal sealed record BenchResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    double ElapsedMs,
    long PeakRssBytes,
    long PeakUssProxyBytes)
{
    /// <summary>退出码非 0 时断言失败（携带输出尾部便于定位）</summary>
    public void AssertSuccess(string context)
    {
        if (ExitCode == 0)
        {
            return;
        }

        var tail = string.Join(" | ", new[] { Tail(Stdout), Tail(Stderr) }
            .Where(s => s.Length > 0));
        throw new InvalidOperationException($"{context}: 退出码 {ExitCode}。输出尾部: {tail}");
    }

    private static string Tail(string s)
    {
        return s.Length <= 400 ? s : s[^400..];
    }
}

/// <summary>
/// 外部进程执行与采样
/// </summary>
internal static class ProcessRunner
{
    /// <summary>
    /// 运行外部进程：计时从 Start 前起算（含进程启动，与 Python perf_counter 口径一致），
    /// 退出时返回峰值内存。仅 sampleUss=true 时解析 "Working Set - Private" 计数器
    /// （该解析要枚举性能计数器实例，约数百毫秒，计时型基准不启用以免污染耗时读数）
    /// </summary>
    public static async Task<BenchResult> RunTimedAsync(
        string fileName,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? extraEnv = null,
        bool sampleUss = false,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        if (extraEnv is not null)
        {
            foreach (var (key, value) in extraEnv)
            {
                psi.Environment[key] = value;
            }
        }

        var sw = Stopwatch.StartNew();
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"启动失败: {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        var peakRss = 0L;
        var peakUssProxy = 0L;
        using var privateWs = sampleUss ? TryCreatePrivateWsCounter(process) : null;
        while (!process.HasExited)
        {
            Sample(process, privateWs, ref peakRss, ref peakUssProxy);
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();

        return new BenchResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false),
            sw.Elapsed.TotalMilliseconds,
            peakRss,
            peakUssProxy);
    }

    private static void Sample(Process process, PerformanceCounter? privateWs, ref long peakRss, ref long peakUssProxy)
    {
        try
        {
            process.Refresh();
            if (process.HasExited)
            {
                return;
            }

            peakRss = Math.Max(peakRss, process.WorkingSet64);

            long uss;
            if (privateWs is not null && OperatingSystem.IsWindows())
            {
                // Working Set - Private：进程私有工作集，与 psutil.uss 语义最接近
                uss = (long)privateWs.RawValue;
            }
            else
            {
                uss = process.PrivateMemorySize64;
            }

            peakUssProxy = Math.Max(peakUssProxy, uss);
        }
        catch (InvalidOperationException)
        {
            // 进程恰在此期间退出：采样竞争，忽略（psutil 版以 except 同样处理）
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 性能计数器瞬时不可用：跳过本轮（下一轮继续）
        }
    }

    /// <summary>
    /// 解析进程对应的 "Working Set - Private" 计数器实例。按进程名前缀过滤候选
    /// （否则要机器上每个实例都建一个 ID 计数器，本机 452 实例实测约 7 秒）；
    /// 任何一步失败（含实例在枚举期间被回收的竞态）都返回 null，退回
    /// PrivateMemorySize64 口径
    /// </summary>
    private static PerformanceCounter? TryCreatePrivateWsCounter(Process process)
    {
        if (!OperatingSystem.IsWindows() || process.HasExited)
        {
            return null;
        }

        try
        {
            var processName = process.ProcessName;
            var category = new PerformanceCounterCategory("Process");
            var candidates = category.GetInstanceNames()
                .Where(name => name.Equals(processName, StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(processName + "#", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var name in candidates)
            {
                try
                {
                    using var idCounter = new PerformanceCounter("Process", "ID Process", name, readOnly: true);
                    if ((long)idCounter.RawValue == process.Id)
                    {
                        return new PerformanceCounter("Process", "Working Set - Private", name, readOnly: true);
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 实例在枚举间隙被回收：跳过该候选
                }
                catch (InvalidOperationException)
                {
                    // 同上：残留同名进程退出导致的瞬时不存在
                }
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 计数器族整体不可用（精简服务器等）：退回默认口径
        }

        return null;
    }

    /// <summary>中位数（与 Python statistics.median 一致：偶数长度取中间两值平均）</summary>
    public static double Median(IReadOnlyCollection<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("中位数需要至少一个值", nameof(values));
        }

        var sorted = values.ToList();
        sorted.Sort();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>可执行文件存在性前置检查（比进程启动异常更早给出可读错误）</summary>
    public static void RequireExe(string exe)
    {
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException($"可执行文件不存在: {exe}");
        }
    }

    /// <summary>毫秒取整显示（与 Python f"{t:.0f}ms" 一致）</summary>
    public static string Ms(double value) => $"{value:F0}ms";
}
