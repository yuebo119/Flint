// Flint 静态站点生成器
// Deploy 命令处理器

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Flint.Cli;

/// <summary>
/// Deploy 命令处理器
/// 支持多种部署目标：S3、GitHub Pages、Netlify、Vercel
/// </summary>
internal static partial class DeployHandler
{
    /// <summary>
    /// 执行部署命令
    /// </summary>
    public static async Task<int> ExecuteAsync(
        string target,
        string source,
        bool dryRun,
        bool verbose)
    {
        var sourcePath = Path.GetFullPath(source);

        if (!Directory.Exists(sourcePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 源目录不存在: {sourcePath}");
            Console.WriteLine("请先运行 'Flint build' 构建站点");
            Console.ResetColor();
            return 1;
        }

        // 解析部署目标
        var deployTarget = ParseTarget(target);
        if (deployTarget == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 无效的部署目标: {target}");
            Console.WriteLine("支持的目标格式:");
            Console.WriteLine("  s3://bucket-name          - 部署到 AWS S3");
            Console.WriteLine("  gh-pages                  - 部署到 GitHub Pages");
            Console.WriteLine("  netlify                   - 部署到 Netlify");
            Console.WriteLine("  vercel                    - 部署到 Vercel");
            Console.ResetColor();
            return 1;
        }

        if (verbose)
        {
            Console.WriteLine($"源目录: {sourcePath}");
            Console.WriteLine($"部署目标: {deployTarget.Type} - {deployTarget.Destination}");
            Console.WriteLine($"模拟运行: {dryRun}");
            Console.WriteLine();
        }

        try
        {
            return deployTarget.Type switch
            {
                DeployType.S3 => await DeployToS3Async(sourcePath, deployTarget.Destination, dryRun, verbose),
                DeployType.GitHubPages => await DeployToGitHubPagesAsync(sourcePath, dryRun, verbose),
                DeployType.Netlify => await DeployToNetlifyAsync(sourcePath, dryRun, verbose),
                DeployType.Vercel => await DeployToVercelAsync(sourcePath, dryRun, verbose),
                _ => throw new NotSupportedException($"不支持的部署类型: {deployTarget.Type}")
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"部署失败: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }

    private static DeployTarget? ParseTarget(string target)
    {
        // S3: s3://bucket-name
        var s3Match = S3Regex().Match(target);
        if (s3Match.Success)
        {
            return new DeployTarget(DeployType.S3, s3Match.Groups[1].Value);
        }

        // GitHub Pages
        if (target.Equals("gh-pages", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("github-pages", StringComparison.OrdinalIgnoreCase))
        {
            return new DeployTarget(DeployType.GitHubPages, "gh-pages");
        }

        // Netlify
        if (target.Equals("netlify", StringComparison.OrdinalIgnoreCase))
        {
            return new DeployTarget(DeployType.Netlify, "netlify");
        }

        // Vercel
        if (target.Equals("vercel", StringComparison.OrdinalIgnoreCase))
        {
            return new DeployTarget(DeployType.Vercel, "vercel");
        }

        return null;
    }

    private static async Task<int> DeployToS3Async(string sourcePath, string bucket, bool dryRun, bool verbose)
    {
        Console.WriteLine($"部署到 AWS S3: {bucket}");

        // 检查 AWS CLI 是否可用
        if (!await IsCommandAvailableAsync("aws"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 未找到 AWS CLI，请先安装 AWS CLI");
            Console.WriteLine("安装指南: https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html");
            Console.ResetColor();
            return 1;
        }

        var args = $"s3 sync \"{sourcePath}\" \"s3://{bucket}\" --delete";
        if (dryRun)
            args += " --dryrun";

        if (verbose)
            Console.WriteLine($"执行: aws {args}");

        var result = await RunCommandAsync("aws", args, verbose);

        if (result == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(dryRun ? "模拟部署完成" : "部署成功！");
            Console.ResetColor();
        }

        return result;
    }

    private static async Task<int> DeployToGitHubPagesAsync(string sourcePath, bool dryRun, bool verbose)
    {
        Console.WriteLine("部署到 GitHub Pages");

        // 检查 git 是否可用
        if (!await IsCommandAvailableAsync("git"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 未找到 git，请先安装 git");
            Console.ResetColor();
            return 1;
        }

        if (dryRun)
        {
            Console.WriteLine("模拟部署: 将执行以下操作:");
            Console.WriteLine("  1. 创建 gh-pages 分支");
            Console.WriteLine("  2. 复制构建产物");
            Console.WriteLine("  3. 提交并推送到远程仓库");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("模拟部署完成");
            Console.ResetColor();
            return 0;
        }

        // 创建临时目录
        var tempDir = Path.Combine(Path.GetTempPath(), $"Flint-deploy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 获取当前仓库的远程 URL
            var remoteUrl = await GetGitRemoteUrlAsync();
            if (string.IsNullOrEmpty(remoteUrl))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("错误: 未找到 git 远程仓库");
                Console.ResetColor();
                return 1;
            }

            if (verbose)
                Console.WriteLine($"远程仓库: {SanitizeRemoteUrl(remoteUrl)}");

            // 克隆 gh-pages 分支或创建新分支
            var cloneResult = await RunCommandAsync("git", $"clone --branch gh-pages --single-branch \"{remoteUrl}\" \"{tempDir}\"", verbose);
            if (cloneResult != 0)
            {
                // 分支不存在（或克隆失败——网络/权限错误同样落到这里）
                if (verbose)
                    Console.WriteLine("克隆 gh-pages 失败（分支不存在或网络/权限问题），初始化新的 gh-pages 分支...");
                await RunCommandAsync("git", $"init \"{tempDir}\"", verbose);
                await RunCommandAsync("git", $"-C \"{tempDir}\" checkout --orphan gh-pages", verbose);
                await RunCommandAsync("git", $"-C \"{tempDir}\" remote add origin \"{remoteUrl}\"", verbose);
            }

            // 清空目录（保留 .git）
            foreach (var file in Directory.GetFiles(tempDir))
            {
                File.Delete(file);
            }
            foreach (var dir in Directory.GetDirectories(tempDir).Where(d => !d.EndsWith(".git", StringComparison.Ordinal)))
            {
                Directory.Delete(dir, true);
            }

            // 复制构建产物
            CopyDirectory(sourcePath, tempDir);

            // 添加 .nojekyll 文件
            await File.WriteAllTextAsync(Path.Combine(tempDir, ".nojekyll"), "");

            // 提交并推送（逐步检查返回值，失败时让根因可见而非只暴露最后的 push 错误）
            var addResult = await RunCommandAsync("git", $"-C \"{tempDir}\" add -A", verbose);
            if (addResult != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"错误: git add 失败 (退出码 {addResult})");
                Console.ResetColor();
                return addResult;
            }

            var commitResult = await RunCommandAsync("git", $"-C \"{tempDir}\" commit -m \"Deploy from Flint\"", verbose);
            if (commitResult != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"错误: git commit 失败 (退出码 {commitResult})，常见原因: 未配置 user.name/user.email");
                Console.ResetColor();
                return commitResult;
            }

            var pushResult = await RunCommandAsync("git", $"-C \"{tempDir}\" push origin gh-pages --force", verbose);

            if (pushResult == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("部署成功！");
                Console.ResetColor();
            }

            return pushResult;
        }
        finally
        {
            // 清理临时目录
            try
            { Directory.Delete(tempDir, true); }
            catch { }
        }
    }

    private static async Task<int> DeployToNetlifyAsync(string sourcePath, bool dryRun, bool verbose)
    {
        Console.WriteLine("部署到 Netlify");

        // 检查 netlify CLI 是否可用
        if (!await IsCommandAvailableAsync("netlify"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 未找到 Netlify CLI，请先安装");
            Console.WriteLine("安装命令: npm install -g netlify-cli");
            Console.ResetColor();
            return 1;
        }

        var args = $"deploy --dir=\"{sourcePath}\"";
        if (!dryRun)
        {
            args += " --prod";
        }
        else
        {
            // dry-run 只打印将执行的命令：真实调用会把站点上传到 preview 环境，
            // 与 Program.cs 选项描述"模拟部署，不实际上传"相悖
            Console.WriteLine($"[dry-run] 将执行: netlify {args}");
            return 0;
        }

        if (verbose)
            Console.WriteLine($"执行: netlify {args}");

        var result = await RunCommandAsync("netlify", args, verbose);

        if (result == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("生产部署成功！");
            Console.ResetColor();
        }

        return result;
    }

    private static async Task<int> DeployToVercelAsync(string sourcePath, bool dryRun, bool verbose)
    {
        Console.WriteLine("部署到 Vercel");

        // 检查 vercel CLI 是否可用
        if (!await IsCommandAvailableAsync("vercel"))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 未找到 Vercel CLI，请先安装");
            Console.WriteLine("安装命令: npm install -g vercel");
            Console.ResetColor();
            return 1;
        }

        // 加引号与 S3/Netlify 分支对齐：路径含空格时 Arguments 按空格拆碎
        var args = $"\"{sourcePath}\"";
        if (!dryRun)
        {
            args += " --prod";
        }
        else
        {
            // dry-run 只打印将执行的命令（同 netlify 分支）
            Console.WriteLine($"[dry-run] 将执行: vercel {args}");
            return 0;
        }

        if (verbose)
            Console.WriteLine($"执行: vercel {args}");

        var result = await RunCommandAsync("vercel", args, verbose);

        if (result == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("生产部署成功！");
            Console.ResetColor();
        }

        return result;
    }

    private static async Task<bool> IsCommandAvailableAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int> RunCommandAsync(string command, string args, bool verbose)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return 1;

        // 始终读取重定向的输出流：不读取会在输出超过 OS 管道缓冲（如 aws s3 sync 逐文件输出）时死锁
        var outputTask = DrainAsync(process.StandardOutput, verbose, Console.Out);
        var errorTask = DrainAsync(process.StandardError, verbose, Console.Error);

        await process.WaitForExitAsync();
        await Task.WhenAll(outputTask, errorTask);
        return process.ExitCode;
    }

    /// <summary>
    /// 消费子进程输出流；verbose 时实时转发到控制台，否则丢弃（必须消费，防止管道积满死锁）
    /// </summary>
    private static async Task DrainAsync(StreamReader reader, bool forward, TextWriter target)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            if (forward)
            {
                await target.WriteAsync(buffer, 0, read);
                await target.FlushAsync();
            }
        }
    }

    private static async Task<string?> GetGitRemoteUrlAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "remote get-url origin",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? output.Trim() : null;
    }

    /// <summary>
    /// 脱敏远程仓库 URL 中嵌入的凭据（如 https://user:PAT@github.com/...）
    /// </summary>
    private static string SanitizeRemoteUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            return url.Replace($"{uri.UserInfo}@", "***@", StringComparison.Ordinal);
        }
        return url;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relativePath);
            var destDirPath = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDirPath))
            {
                Directory.CreateDirectory(destDirPath);
            }
            File.Copy(file, destPath, true);
        }
    }

    [GeneratedRegex(@"^s3://(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex S3Regex();

    private sealed record DeployTarget(DeployType Type, string Destination);

    private enum DeployType
    {
        S3,
        GitHubPages,
        Netlify,
        Vercel
    }
}
