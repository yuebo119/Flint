// Flint.AiGate 基础设施：仓库根双布局定位、PASS/FAIL 报告器（ANSI 原样输出）、
// 文件扫描辅助（对应 bash 的 collect_hits/grep_count/棘轮检查）、git 辅助。
// 输出格式与 bash 版逐字对齐（含 ANSI 转义与汇总行），供新旧对拍。

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Flint.AiGate;

/// <summary>
/// 仓库根定位：布局 A（Flint.slnx 在 .ai 祖先链）/ 布局 B（.ai 与 Flint/ 平级）
/// </summary>
internal static class RepoLocator
{
    /// <summary>从当前目录逐级向上找 Flint.slnx；找不到回退脚本目录的兄弟 Flint/（布局 B）</summary>
    public static string FindCodeRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Flint.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        // 布局 B：工具在 <workspace>/Flint/src/...，代码根在 <workspace>/Flint
        var asmDir = AppContext.BaseDirectory;
        var sibling = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", ".."));
        if (File.Exists(Path.Combine(sibling, "Flint.slnx")))
        {
            return sibling;
        }

        throw new InvalidOperationException(
            $"ERROR: 无法定位 Flint.slnx（布局 A：Flint.slnx 在 .ai 祖先链；布局 B：.ai 与 Flint/ 平级）");
    }

    /// <summary>工作区根（.ai 所在层）：布局 A 在代码根内，布局 B 在代码根上一级</summary>
    public static string FindWorkspaceRoot(string codeRoot)
    {
        return Directory.Exists(Path.Combine(codeRoot, ".ai"))
            ? codeRoot
            : Path.GetDirectoryName(codeRoot)!;
    }
}

/// <summary>
/// 门禁报告器：PASS/FAIL/WARN/SKIP 计数 + ANSI 原样输出（与 bash printf 行为一致）
/// </summary>
internal sealed class Reporter
{
    private int _passed;
    private int _failed;
    private int _warned;
    private int _skipped;

    public int Failed => _failed;

    public int Passed => _passed;

    public void Pass(string message)
    {
        Console.WriteLine($"\u001b[0;32mPASS\u001b[0m {message}");
        _passed++;
    }

    public void Pass(string id, string desc)
    {
        Pass($"{id}: {desc}");
    }

    public void Fail(string message)
    {
        Console.WriteLine($"\u001b[0;31mFAIL\u001b[0m {message}");
        _failed++;
    }

    public void Fail(string id, string desc, string count)
    {
        Fail($"{id}: {desc}（违规数：{count}）");
    }

    public void Warn(string message)
    {
        Console.WriteLine($"\u001b[0;33mWARN\u001b[0m {message}");
        _warned++;
    }

    public void Skip(string id, string desc)
    {
        Console.WriteLine($"\u001b[0;33mSKIP\u001b[0m {id}: {desc}");
        _skipped++;
    }

    /// <summary>计数为 0 → PASS，否则 FAIL（对应 bash check_zero）</summary>
    public void CheckZero(string id, string desc, int count)
    {
        if (count == 0)
        {
            Pass($"{id}: {desc}");
        }
        else
        {
            Fail($"{id}: {desc}（违规数：{count}）");
        }
    }

    /// <summary>棘轮检查：count ≤ baseline 才过；超出列新增明细（对应 bash check_ratchet）</summary>
    public void CheckRatchet(string id, string desc, int count, int baseline, string hits)
    {
        if (count <= baseline)
        {
            Pass($"{id}: {desc}（当前 {count} ≤ 基线 {baseline}，只减不增）");
        }
        else
        {
            Fail($"{id}: {desc}（当前 {count} > 基线 {baseline}——新增违规：）");
            foreach (var line in hits.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(10))
            {
                Console.WriteLine(line);
            }
        }
    }

    public void Summary(int totalChecks)
    {
        Console.WriteLine($"通过：{_passed}  警告：{_warned}  失败：{_failed}  跳过：{_skipped}  总计：{totalChecks}");
    }

    public void PlainSummary(string passedLabel = "通过", string failedLabel = "失败")
    {
        Console.WriteLine($"\n{passedLabel}：{_passed}  {failedLabel}：{_failed}  总计：{_passed + _failed + _warned + _skipped}");
    }
}

/// <summary>
/// 文件扫描：对应 bash 的 collect_hits / grep_count
/// </summary>
internal static class Scanner
{
    /// <summary>
    /// 枚举目录下 .cs 文件（排除 obj/bin），按行做正则匹配，输出 "相对路径:行号:行内容" 列表；
    /// 跳过整行注释（对应 bash 的 grep -v ':[[:space:]]*//'）
    /// </summary>
    public static List<string> CollectHits(string codeRoot, string pattern, params string[] relDirs)
    {
        var regex = new Regex(pattern);
        var hits = new List<string>();

        foreach (var relDir in relDirs)
        {
            var dir = Path.Combine(codeRoot, relDir);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
                {
                    continue;
                }

                if (IsSelfReference(normalized))
                {
                    continue;
                }

                var rel = Path.GetRelativePath(codeRoot, file).Replace('\\', '/');
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        var trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("//", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        hits.Add($"{rel}:{i + 1}:{lines[i]}");
                    }
                }
            }
        }

        return hits;
    }

    /// <summary>命中数（对应 bash grep_count：非空行计数）</summary>
    public static int CountHits(List<string> hits) => hits.Count(h => h.Trim().Length > 0);

    /// <summary>枚举目录下 .cs 文件（排除 obj/bin）相对路径</summary>
    public static List<string> CsFiles(string codeRoot, params string[] relDirs)
    {
        var files = new List<string>();
        foreach (var relDir in relDirs)
        {
            var dir = Path.Combine(codeRoot, relDir);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
                {
                    continue;
                }

                if (IsSelfReference(normalized))
                {
                    continue;
                }

                files.Add(Path.GetRelativePath(codeRoot, file).Replace('\\', '/'));
            }
        }

        return files;
    }

    /// <summary>
    /// 门禁工具自身（src/Flint.AiGate）从产品代码扫描中排除：C# 版把检测模式字面量
    /// （如 "TODO: "、"throw ex;"）写在门禁源码里会被 G5/G10/G11 等扫中——bash 版模式
    /// 在 .sh 中天然免疫。门禁工具不是产品代码，不适用产品规范门禁。
    /// </summary>
    public static bool IsSelfReference(string normalizedPath)
    {
        return normalizedPath.Replace('\\', '/').Contains("/src/Flint.AiGate/", StringComparison.Ordinal);
    }
}

/// <summary>
/// git 辅助（返回不可用时由调用方 SKIP 并声明，不静默 no-op）
/// </summary>
internal static class GitHelper
{
    public static bool IsRepo(string dir)
    {
        try
        {
            return Run(dir, "rev-parse", "--git-dir") == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static int Run(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi);
        if (p is null)
        {
            return 1;
        }

        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    public static string Output(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi);
        if (p is null)
        {
            return string.Empty;
        }

        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return stdout;
    }
}

/// <summary>
/// UTF-8 无 BOM 读写（门禁产物与源同口径）
/// </summary>
internal static class Io
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string ReadAllText(string path) => File.ReadAllText(path, Utf8NoBom);

    public static void WriteAllText(string path, string content) => File.WriteAllText(path, content, Utf8NoBom);

    public static string[] ReadAllLines(string path) => File.ReadAllLines(path, Utf8NoBom);
}

/// <summary>
/// 外部进程运行（git/dotnet 等），返回退出码；可静默丢弃输出
/// </summary>
internal static class Bench
{
    public static int RunTimed(string fileName, string[] args, bool quiet = false)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return 1;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!quiet)
        {
            if (stdout.Length > 0)
            {
                Console.WriteLine(stdout);
            }

            if (stderr.Length > 0)
            {
                Console.Error.WriteLine(stderr);
            }
        }

        return process.ExitCode;
    }

    /// <summary>运行外部进程并捕获 stdout（不打印）</summary>
    public static string RunTimedCapture(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return string.Empty;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return stdout;
    }

    /// <summary>运行外部进程并完全静默，只取退出码</summary>
    public static int RunTimedQuiet(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return 1;
        }

        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
