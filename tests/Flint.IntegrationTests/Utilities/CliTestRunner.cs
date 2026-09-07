// Flint 静态站点生成器
// CLI 测试运行器
// 提供执行和验证 CLI 命令的功能

using System.Diagnostics;
using System.Text;

namespace Flint.IntegrationTests.Utilities;

/// <summary>
/// CLI 执行结果
/// </summary>
public sealed record CliResult
{
    /// <summary>
    /// 退出代码
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// 标准输出
    /// </summary>
    public string StandardOutput { get; init; } = "";

    /// <summary>
    /// 错误输出
    /// </summary>
    public string ErrorOutput { get; init; } = "";

    /// <summary>
    /// 执行时间
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess => ExitCode == 0;

    /// <summary>
    /// 是否超时
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// 执行的命令
    /// </summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// 工作目录
    /// </summary>
    public string WorkingDirectory { get; init; } = "";
}

/// <summary>
/// CLI 测试运行器
/// 提供执行和验证 CLI 命令的功能
/// </summary>
public sealed class CliTestRunner : IDisposable
{
    private readonly string _cliPath;
    private readonly Dictionary<string, string> _environmentVariables;
    private readonly TimeSpan _defaultTimeout;
    private bool _disposed;

    /// <summary>
    /// 默认超时时间（30秒）
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 创建 CLI 测试运行器
    /// </summary>
    /// <param name="cliPath">CLI 可执行文件路径（可选，默认自动查找）</param>
    /// <param name="defaultTimeout">默认超时时间</param>
    public CliTestRunner(string? cliPath = null, TimeSpan? defaultTimeout = null)
    {
        _cliPath = cliPath ?? FindCliPath();
        _environmentVariables = new Dictionary<string, string>();
        _defaultTimeout = defaultTimeout ?? DefaultTimeout;
    }

    /// <summary>
    /// CLI 可执行文件路径
    /// </summary>
    public string CliPath => _cliPath;

    /// <summary>
    /// 设置环境变量
    /// </summary>
    /// <param name="name">变量名</param>
    /// <param name="value">变量值</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public CliTestRunner WithEnvironmentVariable(string name, string value)
    {
        _environmentVariables[name] = value;
        return this;
    }

    /// <summary>
    /// 设置多个环境变量
    /// </summary>
    /// <param name="variables">环境变量字典</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public CliTestRunner WithEnvironmentVariables(IDictionary<string, string> variables)
    {
        foreach (var (name, value) in variables)
        {
            _environmentVariables[name] = value;
        }
        return this;
    }

    /// <summary>
    /// 清除所有自定义环境变量
    /// </summary>
    /// <returns>当前实例（支持链式调用）</returns>
    public CliTestRunner ClearEnvironmentVariables()
    {
        _environmentVariables.Clear();
        return this;
    }

    /// <summary>
    /// 执行 CLI 命令
    /// </summary>
    /// <param name="args">命令参数</param>
    /// <param name="workingDirectory">工作目录（可选）</param>
    /// <param name="timeout">超时时间（可选，默认使用构造函数指定的超时）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public async ValueTask<CliResult> RunAsync(
        string[] args,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout ?? _defaultTimeout;
        var effectiveWorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory();
        var commandLine = string.Join(" ", args);

        var startInfo = CreateProcessStartInfo(args, effectiveWorkingDirectory);
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = startInfo };
        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        // 设置输出处理
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stdoutBuilder)
                {
                    stdoutBuilder.AppendLine(e.Data);
                }
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (stderrBuilder)
                {
                    stderrBuilder.AppendLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 使用超时和取消令牌等待进程完成
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
                stopwatch.Stop();

                return new CliResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = stdoutBuilder.ToString().TrimEnd(),
                    ErrorOutput = stderrBuilder.ToString().TrimEnd(),
                    Duration = stopwatch.Elapsed,
                    TimedOut = false,
                    Command = commandLine,
                    WorkingDirectory = effectiveWorkingDirectory
                };
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                // 超时
                stopwatch.Stop();
                TryKillProcess(process);

                return new CliResult
                {
                    ExitCode = -1,
                    StandardOutput = stdoutBuilder.ToString().TrimEnd(),
                    ErrorOutput = stderrBuilder.ToString().TrimEnd(),
                    Duration = stopwatch.Elapsed,
                    TimedOut = true,
                    Command = commandLine,
                    WorkingDirectory = effectiveWorkingDirectory
                };
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            TryKillProcess(process);

            return new CliResult
            {
                ExitCode = -1,
                StandardOutput = stdoutBuilder.ToString().TrimEnd(),
                ErrorOutput = $"进程启动失败: {ex.Message}",
                Duration = stopwatch.Elapsed,
                TimedOut = false,
                Command = commandLine,
                WorkingDirectory = effectiveWorkingDirectory
            };
        }
    }

    /// <summary>
    /// 执行 CLI 命令（使用字符串参数）
    /// </summary>
    /// <param name="commandLine">命令行字符串</param>
    /// <param name="workingDirectory">工作目录（可选）</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public ValueTask<CliResult> RunAsync(
        string commandLine,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var args = ParseCommandLine(commandLine);
        return RunAsync(args, workingDirectory, timeout, cancellationToken);
    }

    /// <summary>
    /// 执行 CLI 命令并验证成功
    /// </summary>
    /// <param name="args">命令参数</param>
    /// <param name="workingDirectory">工作目录（可选）</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    /// <exception cref="CliExecutionException">命令执行失败时抛出</exception>
    public async ValueTask<CliResult> RunSuccessfullyAsync(
        string[] args,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(args, workingDirectory, timeout, cancellationToken);

        if (!result.IsSuccess)
        {
            throw new CliExecutionException(result);
        }

        return result;
    }

    /// <summary>
    /// 执行 version 命令
    /// </summary>
    /// <param name="verbose">是否显示详细信息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public ValueTask<CliResult> GetVersionAsync(
        bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        var args = verbose
            ? new[] { "version", "--verbose" }
            : new[] { "version" };
        return RunAsync(args, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 执行 new site 命令
    /// </summary>
    /// <param name="siteName">站点名称</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public ValueTask<CliResult> NewSiteAsync(
        string siteName,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(new[] { "new", "site", siteName }, workingDirectory, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 执行 new theme 命令
    /// </summary>
    /// <param name="themeName">主题名称</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public ValueTask<CliResult> NewThemeAsync(
        string themeName,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(new[] { "new", "theme", themeName }, workingDirectory, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 执行 new content 命令
    /// </summary>
    /// <param name="contentPath">内容路径</param>
    /// <param name="kind">内容类型（可选）</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public ValueTask<CliResult> NewContentAsync(
        string contentPath,
        string? kind = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var args = kind != null
            ? new[] { "new", "content", contentPath, "--kind", kind }
            : new[] { "new", "content", contentPath };
        return RunAsync(args, workingDirectory, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 执行 build 命令
    /// </summary>
    /// <param name="options">构建选项</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public ValueTask<CliResult> BuildAsync(
        CliBuildOptions? options = null,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "build" };

        if (options != null)
        {
            if (options.Minify)
                args.Add("--minify");
            if (options.IncludeDrafts)
                args.Add("--drafts");
            if (options.IncludeFuture)
                args.Add("--future");
            if (options.Clean)
                args.Add("--clean");
            if (options.Verbose)
                args.Add("--verbose");
            if (!string.IsNullOrEmpty(options.OutputDirectory))
            {
                args.Add("--output");
                args.Add(options.OutputDirectory);
            }
            if (!string.IsNullOrEmpty(options.SourceDirectory))
            {
                args.Add("--source");
                args.Add(options.SourceDirectory);
            }
        }

        return RunAsync(args.ToArray(), workingDirectory, timeout, cancellationToken);
    }

    /// <summary>
    /// 执行 serve 命令（启动开发服务器）
    /// </summary>
    /// <param name="options">服务器选项</param>
    /// <param name="workingDirectory">工作目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>命令执行结果</returns>
    public ValueTask<CliResult> ServeAsync(
        CliServeOptions? options = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "serve" };

        if (options != null)
        {
            if (options.Port.HasValue)
            {
                args.Add("--port");
                args.Add(options.Port.Value.ToString());
            }
            if (!string.IsNullOrEmpty(options.Host))
            {
                args.Add("--host");
                args.Add(options.Host);
            }
            if (options.DisableLiveReload)
            {
                args.Add("--livereload");
                args.Add("false");
            }
            if (options.OpenBrowser)
                args.Add("--open");
        }

        return RunAsync(args.ToArray(), workingDirectory, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 创建进程启动信息
    /// </summary>
    private ProcessStartInfo CreateProcessStartInfo(string[] args, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _cliPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // 添加命令参数
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // 添加自定义环境变量
        foreach (var (name, value) in _environmentVariables)
        {
            startInfo.Environment[name] = value;
        }

        return startInfo;
    }

    /// <summary>
    /// 尝试终止进程
    /// </summary>
    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 忽略终止进程时的错误
        }
    }

    /// <summary>
    /// 解析命令行字符串为参数数组
    /// </summary>
    private static string[] ParseCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escapeNext = false;

        foreach (var c in commandLine)
        {
            if (escapeNext)
            {
                current.Append(c);
                escapeNext = false;
                continue;
            }

            switch (c)
            {
                case '\\':
                    escapeNext = true;
                    break;
                case '"':
                    inQuotes = !inQuotes;
                    break;
                case ' ' when !inQuotes:
                    if (current.Length > 0)
                    {
                        args.Add(current.ToString());
                        current.Clear();
                    }
                    break;
                default:
                    current.Append(c);
                    break;
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args.ToArray();
    }

    /// <summary>
    /// 查找 CLI 可执行文件路径
    /// </summary>
    private static string FindCliPath()
    {
        // 首先尝试从环境变量获取
        var envPath = Environment.GetEnvironmentVariable("Flint_CLI_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
        {
            return envPath;
        }

        // 尝试在项目输出目录中查找
        var baseDir = AppContext.BaseDirectory;
        var possiblePaths = new[]
        {
            // 优先使用发布目录（AOT 编译的独立可执行文件）
            // 从 tests/Flint.IntegrationTests/bin/Debug/net10.0 到 src/publish 需要向上 5 级
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "publish", "Flint.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "publish", "Flint"),
            
            // 在解决方案目录结构中查找（非 AOT 版本，带 RID 子目录）
            // 从 tests/Flint.IntegrationTests/bin/Debug/net10.0 到 src/Flint.Cli/bin/Debug/net10.0/win-x64 需要向上 5 级
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Debug", "net10.0", "win-x64", "Flint.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Debug", "net10.0", "win-x64", "Flint"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Release", "net10.0", "win-x64", "Flint.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Release", "net10.0", "win-x64", "Flint"),
            
            // 在解决方案目录结构中查找（非 AOT 版本，不带 RID 子目录）
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Debug", "net10.0", "Flint.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Debug", "net10.0", "Flint"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Release", "net10.0", "Flint.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "..", "src", "Flint.Cli", "bin", "Release", "net10.0", "Flint"),
            
            // 注意：不使用测试输出目录中的 Flint.exe，因为它的 runtimeconfig.json 配置不正确
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        // 尝试在 PATH 中查找
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var pathDirs = pathEnv.Split(Path.PathSeparator);

        foreach (var dir in pathDirs)
        {
            var FlintPath = Path.Combine(dir, OperatingSystem.IsWindows() ? "Flint.exe" : "Flint");
            if (File.Exists(FlintPath))
            {
                return FlintPath;
            }
        }

        // 找不到 CLI 时直接失败：返回占位路径会让所有 E2E 测试静默变绿（进程启动失败 → IsSuccess=false → 空转断言跳过）
        throw new FileNotFoundException(
            $"未找到 Flint CLI 可执行文件。请先构建 CLI（dotnet build src/Flint.Cli）或设置环境变量 Flint_CLI_PATH。" +
            $"已尝试: 环境变量 Flint_CLI_PATH、src/publish、src/Flint.Cli/bin/**、PATH");
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }
}


/// <summary>
/// CLI 构建选项
/// </summary>
public sealed record CliBuildOptions
{
    /// <summary>
    /// 是否压缩输出
    /// </summary>
    public bool Minify { get; init; }

    /// <summary>
    /// 是否包含草稿
    /// </summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>
    /// 是否包含未来内容
    /// </summary>
    public bool IncludeFuture { get; init; }

    /// <summary>
    /// 是否清理输出目录
    /// </summary>
    public bool Clean { get; init; }

    /// <summary>
    /// 是否显示详细信息
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// 输出目录
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// 源目录
    /// </summary>
    public string? SourceDirectory { get; init; }
}

/// <summary>
/// CLI 服务器选项
/// </summary>
public sealed record CliServeOptions
{
    /// <summary>
    /// 端口号
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// 主机地址
    /// </summary>
    public string? Host { get; init; }

    /// <summary>
    /// 是否禁用热重载
    /// </summary>
    public bool DisableLiveReload { get; init; }

    /// <summary>
    /// 是否自动打开浏览器
    /// </summary>
    public bool OpenBrowser { get; init; }
}

/// <summary>
/// CLI 执行异常
/// </summary>
public sealed class CliExecutionException : Exception
{
    /// <summary>
    /// CLI 执行结果
    /// </summary>
    public CliResult? Result { get; }

    /// <summary>
    /// 创建 CLI 执行异常
    /// </summary>
    public CliExecutionException()
        : base("CLI 命令执行失败")
    {
    }

    /// <summary>
    /// 创建 CLI 执行异常
    /// </summary>
    /// <param name="message">错误消息</param>
    public CliExecutionException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 创建 CLI 执行异常
    /// </summary>
    /// <param name="message">错误消息</param>
    /// <param name="innerException">内部异常</param>
    public CliExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// 创建 CLI 执行异常
    /// </summary>
    /// <param name="result">执行结果</param>
    public CliExecutionException(CliResult result)
        : base(FormatMessage(result))
    {
        Result = result;
    }

    private static string FormatMessage(CliResult result)
    {
        var message = $"CLI 命令执行失败: {result.Command}";

        if (result.TimedOut)
        {
            message += $"\n超时: {result.Duration.TotalSeconds:F1}秒";
        }
        else
        {
            message += $"\n退出代码: {result.ExitCode}";
        }

        if (!string.IsNullOrEmpty(result.ErrorOutput))
        {
            message += $"\n错误输出:\n{result.ErrorOutput}";
        }

        return message;
    }
}
