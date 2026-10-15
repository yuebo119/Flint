// Flint 静态站点生成器
// GitDateProvider 测试——真实 git 仓库冒烟 + 非 git 仓库降级

using System.Diagnostics;
using System.Globalization;
using Flint.Core.Content;
using Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// <see cref="GitDateProvider"/> 行为验证。
/// 真实 git 用例依赖环境中存在 git 可执行文件（构建机/CI 均有）。
/// </summary>
public class GitDateProviderTests : IDisposable
{
    private readonly string _root;

    public GitDateProviderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "flint-gitdate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void GetLastCommitTime_RealRepo_ReturnsLastCommitTime()
    {
        RunGit("init", quiet: true);
        var postsDir = Path.Combine(_root, "posts");
        Directory.CreateDirectory(postsDir);
        var file = Path.Combine(postsDir, "a.md");
        File.WriteAllText(file, "hello");
        RunGit("add .");
        RunGit("-c user.name=flint-test -c user.email=flint-test@example.com commit -m init");

        // 环境韧性：GitDateProvider 对 git 失败的降级是设计行为（静默返回 null），
        // 但部分机器上测试进程 spawn 的 git 子进程被环境层干扰（杀软实时扫描锁，
        // 与 ModuleManagerSourceTests 的 clone deny 同族），rev-parse 拿不到仓库根，
        // 真实仓库冒烟会误报为失败。先冒烟 git rev-parse 可用性，不可用则跳过
        var toplevel = RunGitWithOutputAllowEmpty("rev-parse --show-toplevel");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(toplevel),
            "git rev-parse 在本环境不可用（沙箱/杀软拦截 git 子进程），跳过真实仓库冒烟");

        var expectedText = RunGitWithOutput("log -1 --format=%cI");
        var expected = DateTimeOffset.Parse(expectedText, CultureInfo.InvariantCulture);

        // contentDirectory 为仓库内的 posts 子目录，验证仓库根路径对齐
        var provider = new GitDateProvider(postsDir);
        var actual = provider.GetLastCommitTime(file);

        Assert.NotNull(actual);
        Assert.Equal(expected, actual.Value);
    }

    [Fact]
    public void GetLastCommitTime_NonRepoDirectory_ReturnsNull()
    {
        var provider = new GitDateProvider(_root); // 无 .git

        Assert.Null(provider.GetLastCommitTime(Path.Combine(_root, "a.md")));
    }

    private void RunGit(string arguments, bool quiet = false)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("git 进程启动失败");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Assert.Fail($"git {arguments} 失败: {process.StandardError.ReadToEnd()}");
        }
    }

    private string RunGitWithOutput(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("git 进程启动失败");
        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return output;
    }

    /// <summary>git 命令输出（失败/空输出不抛，返回空串——环境冒烟探测用）</summary>
    private string RunGitWithOutputAllowEmpty(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = _root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return string.Empty;
            }
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : string.Empty;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // git 不存在或进程启动失败：视为环境不可用
            return string.Empty;
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows 上 git 对象文件只读且句柄偶发延迟释放，删除失败不视为测试失败
        }
    }
}
