// Flint 静态站点生成器
// Sass/SCSS 编译器实现 - 调用外部 Dart Sass CLI

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Models;

namespace Flint.Core.Assets;

/// <summary>
/// Sass/SCSS 编译器实现
/// 统一调用外部 Dart Sass CLI（FLINT_SASS / PATH / 发布目录），
/// 支持 .sass 与 .scss、压缩输出与内联 source map。
/// </summary>
public sealed class SassCompiler : ISassCompiler, IDisposable
{
    private bool _disposed;

    public async ValueTask<ProcessedAsset> CompileAsync(
        AssetFile sassFile,
        SassOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var sourceContent = Encoding.UTF8.GetString(sassFile.Content.Span);
        var compressed = options.Minify || options.OutputStyle == SassOutputStyle.Compressed;

        var cssContent = await CompileViaCliAsync(
            sourceContent,
            sassFile.SourcePath,
            indented: sassFile.Extension == ".sass",
            compressed: compressed,
            generateSourceMap: options.GenerateSourceMap,
            includePaths: options.IncludePaths,
            cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        var outputPath = GenerateOutputPath(sassFile.SourcePath);
        var contentBytes = Encoding.UTF8.GetBytes(cssContent);
        var content = new ReadOnlyMemory<byte>(contentBytes);
        var contentHash = ComputeHash(content.Span);
        var integrity = ComputeSriHash(content.Span);
        var fingerprintedPath = GenerateFingerprintedPath(outputPath, contentHash);

        return new ProcessedAsset
        {
            OutputPath = outputPath,
            MediaType = "text/css",
            Content = content,
            ContentHash = contentHash,
            Integrity = integrity,
            FingerprintedPath = fingerprintedPath,
            SourcePath = sassFile.SourcePath,
            ProcessingTime = stopwatch.Elapsed,
            OriginalSize = sassFile.Content.Length,
            IsMinified = compressed
        };
    }

    /// <summary>
    /// 调用外部 Dart Sass 编译内存中的 SCSS/Sass 内容。
    /// 内容先写入 UTF-8 临时文件再按文件编译：Windows 下 Dart Sass 的 stdin
    /// 按系统代码页解释，中文内容会变乱码；文件输入同时保留正确的 import 基准。
    /// source map 使用 <c>--embed-source-map</c> 内联为 data URI，保持单文件产物模型。
    /// </summary>
    private static async ValueTask<string> CompileViaCliAsync(
        string content,
        string sourcePath,
        bool indented,
        bool compressed,
        bool generateSourceMap,
        IReadOnlyList<string> includePaths,
        CancellationToken cancellationToken)
    {
        var sass = Flint.Core.Templates.BuiltinTemplateFunctions.LocateSassExecutable()
            ?? throw new InvalidOperationException(
                "找不到 Dart Sass 可执行文件。请设置 FLINT_SASS、把 sass 加入 PATH，" +
                "或确保发布目录包含 dart-sass.win-x64/sass.bat、dart-sass.linux-x64/sass " +
                "或 dart-sass.macos-x64/sass。");

        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "Flint_sass_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var inputFile = Path.Combine(tempDirectory, indented ? "input.sass" : "input.scss");

        try
        {
            await File.WriteAllTextAsync(
                inputFile,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            var args = CreateArguments(sourcePath, inputFile, compressed, generateSourceMap, includePaths);
            using var process = StartProcess(sass, args);

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException($"Dart Sass 编译超时（{sourcePath}）");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(error) ? "未知错误" : error.Trim();
                throw new SassCompilationException(
                    $"Dart Sass 编译失败（{sourcePath}）：{details}",
                    sourcePath,
                    error);
            }

            return output;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static List<string> CreateArguments(
        string sourcePath,
        string inputFile,
        bool compressed,
        bool generateSourceMap,
        IReadOnlyList<string> includePaths)
    {
        var args = new List<string>
        {
            compressed ? "--style=compressed" : "--style=expanded"
        };

        if (generateSourceMap)
        {
            args.Add("--source-map");
            args.Add("--embed-source-map");
        }
        else
        {
            args.Add("--no-source-map");
        }

        args.Add("--quiet");

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var loadPaths = new List<string>();

        void AddLoadPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
            if (!loadPaths.Contains(fullPath, comparer))
            {
                loadPaths.Add(fullPath);
            }
        }

        if (!string.IsNullOrEmpty(sourcePath))
        {
            AddLoadPath(Path.GetDirectoryName(Path.GetFullPath(sourcePath)));
        }

        foreach (var includePath in includePaths)
        {
            AddLoadPath(includePath);
        }

        foreach (var loadPath in loadPaths)
        {
            args.Add("--load-path=" + loadPath);
        }

        args.Add(inputFile);
        return args;
    }

    private static Process StartProcess(string sass, List<string> args)
    {
        var isCmdShim = sass.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
                        sass.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
        // sass（当前 npm 包 bin 名）与 dart-sass（废弃旧包 bin 名，2026-10-22）
        // 双形态：类 Unix 上都是 Node shebang 脚本，经 /bin/sh 驱动并确保
        // 可执行位（NuGet 拷贝场景会丢 exec bit）
        var sassFileName = Path.GetFileName(sass);
        var isShellScript = !isCmdShim &&
                            !OperatingSystem.IsWindows() &&
                            (sassFileName == "sass" || sassFileName == "dart-sass");

        if (isShellScript)
        {
            Flint.Core.Templates.BuiltinTemplateFunctions.EnsureDartExecutable(sass);
        }

        var psi = new ProcessStartInfo
        {
            FileName = isCmdShim ? "cmd.exe" : isShellScript ? "/bin/sh" : sass,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CreateNoWindow = true
        };

        if (isCmdShim)
        {
            var quotedArgs = string.Join(' ', args.Select(QuoteArgument));
            psi.Arguments = "/c \"\"" + sass + "\" " + quotedArgs + "\"";
        }
        else
        {
            if (isShellScript)
            {
                psi.ArgumentList.Add(sass);
            }

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    private static string QuoteArgument(string arg) =>
        arg.Contains(' ') ? "\"" + arg + "\"" : arg;

    private static void TryKill(Process process)
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
            // 进程已退出或句柄已释放，无需处理
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响编译结果
        }
    }

    private static string ComputeHash(ReadOnlySpan<byte> content)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(content, hashBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ComputeSriHash(ReadOnlySpan<byte> content)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(content, hashBytes);
        return $"sha256-{Convert.ToBase64String(hashBytes)}";
    }

    private static string GenerateOutputPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(directory, fileName + ".css");
    }

    private static string GenerateFingerprintedPath(string outputPath, string hash)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        return Path.Combine(directory, $"{fileName}.{hash[..8]}{extension}");
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
