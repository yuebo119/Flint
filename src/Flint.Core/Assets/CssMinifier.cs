// Flint 静态站点生成器核心库
// CSS 压缩：优先外部 Lightning CSS CLI，找不到 CLI 时回退内置简单压缩，
// 保证无 Node/npm 环境也能构建（不改变原有失败回退语义）。

using System.Diagnostics;
using System.Text;

namespace Flint.Core.Assets;

/// <summary>
/// CSS 压缩器。Lightning CSS 不可用时回退简单压缩，避免强依赖外部 CLI。
/// </summary>
public static class CssMinifier
{
    /// <summary>压缩 CSS；外部 CLI 失败时抛异常，由调用方按 FailOnProcessingError 处理。</summary>
    public static string Minify(string css)
    {
        ArgumentNullException.ThrowIfNull(css);

        var lightning = LocateLightningCss();
        if (lightning is not null)
        {
            return MinifyViaLightningCss(css, lightning);
        }

        return SimpleMinify(css);
    }

    private static string MinifyViaLightningCss(string css, string executable)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "Flint_lightningcss_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var inputFile = Path.Combine(tempDirectory, "input.css");
        var outputFile = Path.Combine(tempDirectory, "output.css");

        try
        {
            File.WriteAllText(inputFile, css, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var args = $"--minify \"{inputFile}\" -o \"{outputFile}\"";
            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CreateNoWindow = true
            };

            if (executable.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                executable.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c \"\"" + executable + "\" " + args + "\"";
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                throw new InvalidOperationException("无法启动 Lightning CSS 进程");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(120_000);

            if (process.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException($"Lightning CSS 压缩失败: {details.Trim()}");
            }

            return File.Exists(outputFile)
                ? File.ReadAllText(outputFile)
                : throw new InvalidOperationException("Lightning CSS 未产出输出文件");
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响压缩结果
            }
        }
    }

    private static string? LocateLightningCss()
    {
        if (Environment.GetEnvironmentVariable("LIGHTNINGCSS") is { Length: > 0 } env &&
            File.Exists(env))
        {
            return env;
        }

        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var dir in pathDirs)
        {
            foreach (var name in new[] { "lightningcss.cmd", "lightningcss.exe", "lightningcss" })
            {
                var full = Path.Combine(dir.Trim(), name);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 简单 CSS 压缩：去注释/压缩空白，保留字符串与转义语义。
    /// 与历史 SassCompiler.MinifyCss 同规则，作为 Lightning CSS 缺失时的回退。
    /// </summary>
    private static string SimpleMinify(string css)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(css.Length);
        var inComment = false;
        var inString = false;
        var stringChar = '\0';
        var lastNonWhitespace = '\0';

        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            var nextChar = i + 1 < css.Length ? css[i + 1] : '\0';

            if (!inComment && (c == '"' || c == '\''))
            {
                if (!inString)
                {
                    inString = true;
                    stringChar = c;
                }
                else if (c == stringChar)
                {
                    var backslashes = 0;
                    for (var j = i - 1; j >= 0 && css[j] == '\\'; j--)
                    {
                        backslashes++;
                    }
                    if (backslashes % 2 == 0)
                    {
                        inString = false;
                    }
                }
                sb.Append(c);
                lastNonWhitespace = c;
            }
            else if (!inString && c == '/' && nextChar == '*')
            {
                inComment = true;
                i++;
            }
            else if (inComment && c == '*' && nextChar == '/')
            {
                inComment = false;
                i++;
            }
            else if (inComment)
            {
            }
            else if (inString)
            {
                sb.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                if (lastNonWhitespace != '\0' && !IsNoSpaceNeededAfter(lastNonWhitespace) &&
                    nextChar != '\0' && !char.IsWhiteSpace(nextChar) && !IsNoSpaceNeededBefore(nextChar))
                {
                    sb.Append(' ');
                }
            }
            else
            {
                sb.Append(c);
                lastNonWhitespace = c;
            }
        }

        return sb.ToString().Trim();
    }

    private static bool IsNoSpaceNeededAfter(char c) =>
        c is '{' or '}' or ';' or ':' or ',' or '(' or '[' or '>' or '+' or '~' or '*' or '/' or '!' or '@';

    private static bool IsNoSpaceNeededBefore(char c) =>
        c is '{' or '}' or ';' or ':' or ')' or ']' or '>' or '+' or '~' or '*' or '/' or '!';
}
