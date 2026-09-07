// Flint 静态站点生成器
// GitDateProvider——一次 git log 全量遍历构建 文件→最后提交时间 映射

using System.Collections.Frozen;
using System.Diagnostics;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;

namespace Flint.Core.Content;

/// <summary>
/// Hugo <c>:git</c> 日期源实现。
/// 首次查询时对站点仓库执行一次 <c>git log --format=%cI --name-only</c>
/// （log 从新到旧，每个路径首见行即其最后提交时间），构建
/// 仓库相对路径 → committer date 的映射；后续查询 O(1)。
/// 路径对齐：查询传入绝对路径，内部以仓库根换算为相对路径再查映射。
/// 失败降级：非 git 仓库、git 不存在、命令失败——映射为空，查询恒返回 null。
/// </summary>
public sealed class GitDateProvider : IGitDateProvider
{
    private readonly string _contentDirectory;
    private FrozenDictionary<string, DateTimeOffset>? _lastCommitTimes;
    private string? _repoRoot;
    // 查询方在 Parallel.ForEachAsync 内并发首访，??= 会重复启动多次 git log 全量遍历
    private readonly object _loadLock = new();

    // 路径比较对齐文件系统语义：Windows 不区分大小写，其余平台区分
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <param name="contentDirectory">站点内容目录（绝对路径），git 命令的工作目录</param>
    public GitDateProvider(string contentDirectory)
    {
        _contentDirectory = Path.GetFullPath(contentDirectory);
    }

    /// <inheritdoc />
    public DateTimeOffset? GetLastCommitTime(string filePath)
    {
        var times = _lastCommitTimes;
        if (times is null)
        {
            lock (_loadLock)
            {
                times = _lastCommitTimes ??= LoadLastCommitTimes();
            }
        }
        var repoRoot = _repoRoot;
        if (repoRoot is null)
        {
            return null;
        }

        var key = Path.GetRelativePath(repoRoot, Path.GetFullPath(filePath)).Replace('\\', '/');
        return times.TryGetValue(key, out var time) ? time : null;
    }

    private FrozenDictionary<string, DateTimeOffset> LoadLastCommitTimes()
    {
        _repoRoot = GetRepoRoot();
        if (_repoRoot is null)
        {
            return FrozenDictionary<string, DateTimeOffset>.Empty;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                // -c core.quotepath=off：git 默认把非 ASCII 路径输出为 C 风格转义
                //（"\346\226\207..."），映射 key 与查询侧真实路径永不匹配，中文文件名下 :git 源整体失效
                Arguments = "-c core.quotepath=off log --format=%cI --name-only --no-renames",
                WorkingDirectory = _repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return FrozenDictionary<string, DateTimeOffset>.Empty;
            }

            // 排空 stderr：Redirect 后不读取，git 输出大时管道缓冲区满会死锁子进程
            process.BeginErrorReadLine();

            var map = ParseGitLog(process.StandardOutput);
            process.WaitForExit();
            return map;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // git 不存在或进程启动失败：:git 源静默缺省（与 Hugo 非 git 仓库行为一致）
            _repoRoot = null;
            return FrozenDictionary<string, DateTimeOffset>.Empty;
        }
    }

    /// <summary>
    /// 解析 <c>git log --format=%cI --name-only</c> 输出。
    /// 实测块结构：时间行 → 空行 → 文件行… → 空行（提交分隔）；
    /// log 从新到旧，路径首见时间即最后提交时间。key 为仓库相对路径。
    /// 空行仅作分隔不清当前时间——下一个提交块的时间行会覆盖它。
    /// </summary>
    private static FrozenDictionary<string, DateTimeOffset> ParseGitLog(StreamReader output)
    {
        var map = new Dictionary<string, DateTimeOffset>(PathComparer);
        DateTimeOffset? currentTime = null;
        string? line;

        while ((line = output.ReadLine()) is not null)
        {
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if (DateTimeOffset.TryParseExact(
                    line, "yyyy-MM-dd'T'HH:mm:sszzz",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var commitTime))
            {
                currentTime = commitTime;
                continue;
            }

            if (currentTime.HasValue && !map.ContainsKey(line))
            {
                map[line] = currentTime.Value;
            }
        }

        return map.ToFrozenDictionary(PathComparer);
    }

    private string? GetRepoRoot()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --show-toplevel",
                WorkingDirectory = _contentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            // 排空 stderr（同 LoadLastCommitTimes）
            process.BeginErrorReadLine();

            var root = process.StandardOutput.ReadLine()?.Trim();
            process.WaitForExit();
            // Path.GetFullPath 归一化分隔符：git 输出正斜杠，GetRelativePath 需与查询路径同形
            return process.ExitCode == 0 && !string.IsNullOrEmpty(root) ? Path.GetFullPath(root) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// 站点装配辅助：把 Hugo 日期处理链配置（<c>timeZone</c>/<c>enableGitInfo</c>）应用到内容解析器。
/// 无效时区名 fail-fast（对齐 Hugo：timeZone 配置错误构建即失败），在装配点而非解析中途暴露。
/// </summary>
public static class ContentParserDateSetup
{
    /// <param name="parser">内容解析器（装配点新建的实例）</param>
    /// <param name="config">站点配置</param>
    /// <param name="contentDirectory">站点内容目录绝对路径（<c>:git</c> 源的 git 工作目录）</param>
    public static void Apply(ContentParser parser, Configuration.SiteConfig config, string contentDirectory)
    {
        if (!string.IsNullOrWhiteSpace(config.TimeZone))
        {
            parser.SiteTimeZone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZone);
        }

        if (config.EnableGitInfo)
        {
            parser.GitDates = new GitDateProvider(contentDirectory);
        }
    }
}
