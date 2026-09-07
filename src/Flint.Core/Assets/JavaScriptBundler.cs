// Flint 静态站点生成器
// JavaScript 打包器实现 - 基于 esbuild

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Models;

namespace Flint.Core.Assets;

/// <summary>
/// JavaScript 打包器实现
/// 使用 esbuild 进行 JavaScript 打包、转译和压缩
/// </summary>
public sealed class JavaScriptBundler : IJavaScriptBundler
{
    private readonly string? _esbuildPath;
    private readonly bool _esbuildAvailable;

    /// <summary>
    /// 创建 JavaScript 打包器
    /// </summary>
    public JavaScriptBundler()
    {
        _esbuildPath = FindEsbuild();
        _esbuildAvailable = _esbuildPath != null;
    }

    /// <summary>
    /// 打包多个 JavaScript 文件
    /// </summary>
    public async ValueTask<ProcessedAsset> BundleAsync(
        IReadOnlyList<AssetFile> scripts,
        BundleOptions options,
        CancellationToken cancellationToken = default)
    {
        if (scripts.Count == 0)
        {
            throw new ArgumentException("至少需要一个脚本文件", nameof(scripts));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        // 如果 esbuild 可用，使用 esbuild 打包
        if (_esbuildAvailable)
        {
            return await BundleWithEsbuildAsync(scripts, options, stopwatch, cancellationToken);
        }

        // 否则使用简单的合并方式
        return await SimpleBundleAsync(scripts, options, stopwatch, cancellationToken);
    }

    /// <summary>
    /// 转译单个 JavaScript 文件
    /// </summary>
    public async ValueTask<ProcessedAsset> TranspileAsync(
        AssetFile script,
        BundleOptions options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();

        if (_esbuildAvailable)
        {
            return await TranspileWithEsbuildAsync(script, options, stopwatch, cancellationToken);
        }

        // 如果 esbuild 不可用，只进行简单处理
        return await SimpleTranspileAsync(script, options, stopwatch, cancellationToken);
    }


    /// <summary>
    /// 使用 esbuild 打包
    /// </summary>
    private async Task<ProcessedAsset> BundleWithEsbuildAsync(
        IReadOnlyList<AssetFile> scripts,
        BundleOptions options,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        // 创建临时目录
        var tempDir = Path.Combine(Path.GetTempPath(), $"Flint_bundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 写入所有脚本文件到临时目录（加序号前缀避免不同目录下的同名脚本互相覆盖）
            var entryPoints = new List<string>();
            for (var i = 0; i < scripts.Count; i++)
            {
                var tempPath = Path.Combine(tempDir, $"{i}_{Path.GetFileName(scripts[i].SourcePath)}");
                await File.WriteAllBytesAsync(tempPath, scripts[i].Content.ToArray(), cancellationToken);
                entryPoints.Add(tempPath);
            }

            byte[] content;
            if (entryPoints.Count == 1)
            {
                // 单入口：直接 bundle 到 --outfile
                var outputPath = Path.Combine(tempDir, "bundle.js");
                var args = BuildEsbuildArgs(entryPoints, outputPath, options);
                var result = await RunEsbuildAsync(args, cancellationToken);
                if (!result.Success)
                {
                    throw new JavaScriptBundleException($"esbuild 打包失败: {result.Error}");
                }
                content = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            }
            else
            {
                // 多入口：esbuild 禁止多入口配 --outfile，必须 --outdir。
                // 逐入口各自 bundle（依赖树独立解析，语义最接近"合并"名义），
                // 按入口顺序拼接——与 esbuild 缺席时的 SimpleMinify 降级路径语义一致
                var chunks = new List<byte[]>(entryPoints.Count);
                for (var i = 0; i < entryPoints.Count; i++)
                {
                    var outputPath = Path.Combine(tempDir, $"bundle_{i}.js");
                    var args = BuildEsbuildArgs([entryPoints[i]], outputPath, options);
                    var result = await RunEsbuildAsync(args, cancellationToken);
                    if (!result.Success)
                    {
                        throw new JavaScriptBundleException($"esbuild 打包失败（入口 {i}）: {result.Error}");
                    }
                    chunks.Add(await File.ReadAllBytesAsync(outputPath, cancellationToken));
                }

                var total = chunks.Sum(c => c.Length);
                content = new byte[total];
                var offset = 0;
                foreach (var chunk in chunks)
                {
                    Buffer.BlockCopy(chunk, 0, content, offset, chunk.Length);
                    offset += chunk.Length;
                }
            }

            stopwatch.Stop();

            return CreateProcessedAsset(
                scripts[0].SourcePath,
                content,
                options,
                stopwatch.Elapsed,
                (int)scripts.Sum(s => (long)s.Content.Length));
        }
        finally
        {
            // 清理临时目录
            try
            { Directory.Delete(tempDir, true); }
            catch { /* 忽略清理错误 */ }
        }
    }

    /// <summary>
    /// 使用 esbuild 转译
    /// </summary>
    private async Task<ProcessedAsset> TranspileWithEsbuildAsync(
        AssetFile script,
        BundleOptions options,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"Flint_transpile_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var inputPath = Path.Combine(tempDir, Path.GetFileName(script.SourcePath));
            var outputPath = Path.Combine(tempDir, "output.js");

            await File.WriteAllBytesAsync(inputPath, script.Content.ToArray(), cancellationToken);

            var modifiedOptions = new BundleOptions
            {
                Minify = options.Minify,
                GenerateSourceMap = options.GenerateSourceMap,
                Target = options.Target,
                Format = options.Format,
                External = options.External,
                TreeShaking = false
            };
            var args = BuildEsbuildArgs([inputPath], outputPath, modifiedOptions);

            var result = await RunEsbuildAsync(args, cancellationToken);
            if (!result.Success)
            {
                throw new JavaScriptBundleException($"esbuild 转译失败: {result.Error}");
            }

            var content = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            stopwatch.Stop();

            return CreateProcessedAsset(
                script.SourcePath,
                content,
                options,
                stopwatch.Elapsed,
                script.Content.Length);
        }
        finally
        {
            try
            { Directory.Delete(tempDir, true); }
            catch { /* 忽略清理错误 */ }
        }
    }

    /// <summary>
    /// 简单打包（不使用 esbuild）
    /// </summary>
    private async Task<ProcessedAsset> SimpleBundleAsync(
        IReadOnlyList<AssetFile> scripts,
        BundleOptions options,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        // 添加 IIFE 包装（如果需要）
        if (options.Format == JsOutputFormat.Iife)
        {
            sb.AppendLine("(function() {");
            sb.AppendLine("'use strict';");
        }

        foreach (var script in scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = Encoding.UTF8.GetString(script.Content.Span);
            sb.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"// Source: {script.SourcePath}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        if (options.Format == JsOutputFormat.Iife)
        {
            sb.AppendLine("})();");
        }

        var bundledContent = sb.ToString();

        // 简单压缩
        if (options.Minify)
        {
            bundledContent = SimpleMinify(bundledContent);
        }

        stopwatch.Stop();
        var contentBytes = Encoding.UTF8.GetBytes(bundledContent);

        return CreateProcessedAsset(
            scripts[0].SourcePath,
            contentBytes,
            options,
            stopwatch.Elapsed,
            (int)scripts.Sum(s => (long)s.Content.Length));
    }

    /// <summary>
    /// 简单转译（不使用 esbuild）
    /// </summary>
    private Task<ProcessedAsset> SimpleTranspileAsync(
        AssetFile script,
        BundleOptions options,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var content = Encoding.UTF8.GetString(script.Content.Span);

        if (options.Minify)
        {
            content = SimpleMinify(content);
        }

        stopwatch.Stop();
        var contentBytes = Encoding.UTF8.GetBytes(content);

        return Task.FromResult(CreateProcessedAsset(
            script.SourcePath,
            contentBytes,
            options,
            stopwatch.Elapsed,
            script.Content.Length));
    }


    /// <summary>
    /// 构建 esbuild 命令行参数
    /// </summary>
    private static List<string> BuildEsbuildArgs(
        List<string> entryPoints,
        string outputPath,
        BundleOptions options)
    {
        var args = new List<string>
        {
            "--bundle",
            $"--outfile={outputPath}",
            $"--target={options.Target}"
        };

        // 输出格式
        var format = options.Format switch
        {
            JsOutputFormat.Esm => "esm",
            JsOutputFormat.Cjs => "cjs",
            JsOutputFormat.Iife => "iife",
            _ => "esm"
        };
        args.Add($"--format={format}");

        // 压缩
        if (options.Minify)
        {
            args.Add("--minify");
        }

        // Source Map
        if (options.GenerateSourceMap)
        {
            args.Add("--sourcemap");
        }

        // Tree Shaking
        if (options.TreeShaking)
        {
            args.Add("--tree-shaking=true");
        }

        // 外部依赖
        foreach (var external in options.External)
        {
            args.Add($"--external:{external}");
        }

        // 入口点
        args.AddRange(entryPoints);

        return args;
    }

    /// <summary>
    /// 执行 esbuild
    /// </summary>
    private async Task<EsbuildResult> RunEsbuildAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        // Windows 的 npm 全局安装 esbuild 是 .cmd 批处理 shim，
        // UseShellExecute=false 直接启动会抛 Win32Exception——经 cmd.exe /c 间接执行
        var exePath = _esbuildPath!;
        var isCmdShim = exePath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                        exePath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo
        {
            FileName = isCmdShim ? "cmd.exe" : exePath,
            Arguments = isCmdShim ? "/c" : "",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (isCmdShim)
        {
            // cmd /c 引号规则：/c 后首尾引号会被 cmd 剥除，参数含空格时
            // exe 路径在首个空格处被截断——整体再包一层外引号，
            // 形成 cmd 标准的 "/c ""exe" args"" 双层引号形态
            var quotedArgs = string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            psi.Arguments = $"/c \"\"{exePath}\" {quotedArgs}\"";
        }
        else
        {
            // 使用 ArgumentList 逐项传递，避免路径含空格时因手工拼接导致的参数破碎
            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 取消时终止子进程，避免遗留孤儿 esbuild 进程
            try
            { process.Kill(entireProcessTree: true); }
            catch { /* 进程可能已退出 */ }
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;

        return new EsbuildResult
        {
            Success = process.ExitCode == 0,
            Output = output,
            Error = error
        };
    }

    /// <summary>
    /// 查找 esbuild 可执行文件
    /// </summary>
    private static string? FindEsbuild()
    {
        // 检查常见位置
        var possiblePaths = new[]
        {
            "esbuild",
            "esbuild.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "esbuild.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "esbuild"),
            "/usr/local/bin/esbuild",
            "/usr/bin/esbuild"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }

            // 尝试在 PATH 中查找
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    // 探测超时：结束进程避免遗留孤儿；超时后读 ExitCode 会抛 InvalidOperationException
                    if (!process.WaitForExit(1000))
                    {
                        try { process.Kill(entireProcessTree: true); }
                        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                        continue;
                    }
                    if (process.ExitCode == 0)
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // 忽略错误，继续尝试下一个路径
            }
        }

        return null;
    }

    /// <summary>
    /// 简单的 JavaScript 压缩
    /// </summary>
    private static string SimpleMinify(string js)
    {
        if (string.IsNullOrWhiteSpace(js))
            return string.Empty;

        var sb = new StringBuilder(js.Length);
        var inString = false;
        var stringChar = '\0';
        var inSingleLineComment = false;
        var inMultiLineComment = false;
        var lastChar = '\0';
        var lastNonWhitespace = '\0';

        for (var i = 0; i < js.Length; i++)
        {
            var c = js[i];
            var nextChar = i + 1 < js.Length ? js[i + 1] : '\0';

            // 处理单行注释
            if (!inString && !inMultiLineComment && c == '/' && nextChar == '/')
            {
                inSingleLineComment = true;
                i++;
                continue;
            }

            if (inSingleLineComment)
            {
                if (c == '\n' || c == '\r')
                {
                    inSingleLineComment = false;
                }
                continue;
            }

            // 处理多行注释
            if (!inString && !inSingleLineComment && c == '/' && nextChar == '*')
            {
                inMultiLineComment = true;
                i++;
                continue;
            }

            if (inMultiLineComment)
            {
                if (c == '*' && nextChar == '/')
                {
                    inMultiLineComment = false;
                    i++;
                }
                continue;
            }

            // 处理字符串
            if (!inString && (c == '"' || c == '\'' || c == '`'))
            {
                inString = true;
                stringChar = c;
                sb.Append(c);
                lastNonWhitespace = c;
            }
            else if (inString)
            {
                sb.Append(c);
                if (c == stringChar)
                {
                    // 引号前有奇数个连续反斜杠时该引号是被转义的（如 "a\\\\" 的结束引号），
                    // 仅检查 lastChar 一次会把 "a\\\\" 场景的真正结束引号误判为转义
                    var backslashes = 0;
                    for (var j = sb.Length - 2; j >= 0 && sb[j] == '\\'; j--)
                    {
                        backslashes++;
                    }
                    if (backslashes % 2 == 0)
                    {
                        inString = false;
                    }
                }
                lastNonWhitespace = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                // 只在需要时保留空格
                if (NeedsSpaceBetween(lastNonWhitespace, nextChar))
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(c);
                lastNonWhitespace = c;
            }

            lastChar = c;
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// 判断两个字符之间是否需要空格
    /// </summary>
    private static bool NeedsSpaceBetween(char left, char right)
    {
        if (left == '\0' || right == '\0')
            return false;

        // 标识符或关键字之间需要空格
        var leftIsIdent = char.IsLetterOrDigit(left) || left == '_' || left == '$';
        var rightIsIdent = char.IsLetterOrDigit(right) || right == '_' || right == '$';

        return leftIsIdent && rightIsIdent;
    }

    /// <summary>
    /// 创建处理后的资源
    /// </summary>
    private static ProcessedAsset CreateProcessedAsset(
        string sourcePath,
        byte[] content,
        BundleOptions options,
        TimeSpan processingTime,
        int originalSize)
    {
        var contentHash = ComputeHash(content);
        var integrity = ComputeSriHash(content);
        var outputPath = GenerateOutputPath(sourcePath);
        var fingerprintedPath = GenerateFingerprintedPath(outputPath, contentHash);

        return new ProcessedAsset
        {
            OutputPath = outputPath,
            MediaType = "application/javascript",
            Content = new ReadOnlyMemory<byte>(content),
            ContentHash = contentHash,
            Integrity = integrity,
            FingerprintedPath = fingerprintedPath,
            SourcePath = sourcePath,
            ProcessingTime = processingTime,
            OriginalSize = originalSize,
            IsMinified = options.Minify
        };
    }

    private static string ComputeHash(byte[] content)
    {
        var hashBytes = SHA256.HashData(content);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string ComputeSriHash(byte[] content)
    {
        var hashBytes = SHA256.HashData(content);
        return $"sha256-{Convert.ToBase64String(hashBytes)}";
    }

    private static string GenerateOutputPath(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(directory, fileName + ".js");
    }

    private static string GenerateFingerprintedPath(string outputPath, string hash)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        return Path.Combine(directory, $"{fileName}.{hash[..8]}{extension}");
    }

    private readonly record struct EsbuildResult
    {
        public bool Success { get; init; }
        public string Output { get; init; }
        public string Error { get; init; }
    }
}

/// <summary>
/// JavaScript 打包异常
/// </summary>
public class JavaScriptBundleException : Exception
{
    public JavaScriptBundleException(string message) : base(message) { }
    public JavaScriptBundleException(string message, Exception innerException)
        : base(message, innerException) { }
}
