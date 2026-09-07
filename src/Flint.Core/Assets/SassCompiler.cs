// Flint 静态站点生成器
// Sass/SCSS 编译器实现 - 基于 DartSassHost 库

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DartSassHost;
using Flint.Core.Abstractions;
using Flint.Core.Models;

namespace Flint.Core.Assets;

/// <summary>
/// Sass/SCSS 编译器实现
/// 支持 .sass 和 .scss 两种语法，支持 source map 生成和压缩输出
/// </summary>
public sealed class SassCompiler : ISassCompiler, IDisposable
{
    private readonly Lazy<SassCompilerWrapper> _compiler;
    // DartSassHost 编译器实例非线程安全，AssetPipeline 并行调用时必须串行化
    private readonly SemaphoreSlim _compileLock = new(1, 1);
    private bool _disposed;

    public SassCompiler()
    {
        _compiler = new Lazy<SassCompilerWrapper>(
            () => new SassCompilerWrapper(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async ValueTask<ProcessedAsset> CompileAsync(
        AssetFile sassFile,
        SassOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        var sourceContent = Encoding.UTF8.GetString(sassFile.Content.Span);
        var compileOptions = CreateCompileOptions(sassFile, options);

        CompilationResult result;
        await _compileLock.WaitAsync(cancellationToken);
        try
        {
            result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _compiler.Value.Compile(sourceContent, sassFile.SourcePath, compileOptions);
            }, cancellationToken);
        }
        finally
        {
            _compileLock.Release();
        }

        stopwatch.Stop();
        var cssContent = result.CompiledContent;

        if (options.Minify && options.OutputStyle != SassOutputStyle.Compressed)
            cssContent = MinifyCss(cssContent);

        var outputPath = GenerateOutputPath(sassFile.SourcePath);
        // source map 局限：ProcessedAsset 是单文件产物模型，map 内容无法随产物落地；
        // 此前写 sourceMappingURL 注释但 map 文件不存在，浏览器端表现为 404——
        // 在管线支持多文件产物之前省略引用注释
        if (options.GenerateSourceMap && !string.IsNullOrEmpty(result.SourceMap))
        {
            _ = result.SourceMap;
        }

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
            IsMinified = options.Minify || options.OutputStyle == SassOutputStyle.Compressed
        };
    }

    private static CompilationOptions CreateCompileOptions(AssetFile sassFile, SassOptions options)
    {
        var outputStyle = options.OutputStyle switch
        {
            SassOutputStyle.Compressed => DartSassHost.OutputStyle.Compressed,
            SassOutputStyle.Expanded => DartSassHost.OutputStyle.Expanded,
            _ => DartSassHost.OutputStyle.Expanded
        };

        if (options.Minify)
            outputStyle = DartSassHost.OutputStyle.Compressed;

        var includePaths = new List<string>(options.IncludePaths);
        var sourceDirectory = Path.GetDirectoryName(sassFile.SourcePath);
        if (!string.IsNullOrEmpty(sourceDirectory) && !includePaths.Contains(sourceDirectory))
            includePaths.Insert(0, sourceDirectory);

        return new CompilationOptions
        {
            OutputStyle = outputStyle,
            SourceMap = options.GenerateSourceMap,
            IncludePaths = includePaths
        };
    }

    private static string MinifyCss(string css)
    {
        if (string.IsNullOrWhiteSpace(css))
            return string.Empty;
        var sb = new StringBuilder(css.Length);
        var inComment = false;
        var inString = false;
        var stringChar = '\0';
        var lastChar = '\0';
        var lastNonWhitespace = '\0';

        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            var nextChar = i + 1 < css.Length ? css[i + 1] : '\0';

            if (!inComment && (c == '"' || c == '\''))
            {
                if (!inString)
                { inString = true; stringChar = c; }
                else if (c == stringChar)
                {
                    // 引号前奇数个连续反斜杠 = 被转义的引号（同 JS 端 SimpleMinify 修复）：
                    // 仅看前一字符会把 content: "\\" 的真正结束引号误判为转义
                    var backslashes = 0;
                    for (var j = i - 1; j >= 0 && css[j] == '\\'; j--)
                    {
                        backslashes++;
                    }
                    if (backslashes % 2 == 0)
                        inString = false;
                }
                sb.Append(c);
                lastNonWhitespace = c;
            }
            else if (!inString && c == '/' && nextChar == '*')
            { inComment = true; i++; }
            else if (inComment && c == '*' && nextChar == '/')
            { inComment = false; i++; }
            else if (inComment)
            { }
            else if (inString)
                sb.Append(c);
            else if (char.IsWhiteSpace(c))
            {
                if (lastNonWhitespace != '\0' && !IsNoSpaceNeededAfter(lastNonWhitespace) &&
                    nextChar != '\0' && !char.IsWhiteSpace(nextChar) && !IsNoSpaceNeededBefore(nextChar))
                    sb.Append(' ');
            }
            else
            { sb.Append(c); lastNonWhitespace = c; }
            lastChar = c;
        }
        return sb.ToString().Trim();
    }

    private static bool IsNoSpaceNeededAfter(char c) =>
        c is '{' or '}' or ';' or ':' or ',' or '(' or '[' or '>' or '+' or '~' or '*' or '/' or '!' or '@';

    private static bool IsNoSpaceNeededBefore(char c) =>
        c is '{' or '}' or ';' or ':' or ',' or ')' or ']' or '>' or '+' or '~' or '*' or '/' or '!';

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
        if (_disposed)
            return;
        if (_compiler.IsValueCreated)
            _compiler.Value.Dispose();
        _compileLock.Dispose();
        _disposed = true;
    }
}

internal sealed class SassCompilerWrapper : IDisposable
{
    private readonly DartSassHost.SassCompiler _compiler;
    private bool _disposed;

    public SassCompilerWrapper() => _compiler = new DartSassHost.SassCompiler();

    public CompilationResult Compile(string content, string inputPath, CompilationOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _compiler.Compile(content, inputPath, options: options);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _compiler.Dispose();
        _disposed = true;
    }
}
