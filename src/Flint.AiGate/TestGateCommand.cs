// test-gate + encoding-gate：测试规范门禁（T4/T6/T-DEF-1/框架单一）与编码一致性门禁（E1-E4）
// .ai/scripts/test-gate.sh 与 encoding-gate.sh 的 C# 移植。

using System.CommandLine;
using System.Text;
using System.Text.RegularExpressions;

namespace Flint.AiGate;

/// <summary>
/// test-gate 命令（T4/T6/T-DEF-1/框架单一）
/// </summary>
internal static class TestGateCommand
{
    private const int T4Baseline = 58; // 2026-09-04 起板实测（中文描述式命名存量）

    internal static Command Build()
    {
        var cmd = new Command("test-gate", "测试规范门禁（T4/T6/T-DEF-1/框架单一）");
        cmd.SetAction(_ => Run());
        return cmd;
    }

    private static int Run()
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var reporter = new Reporter();

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine(" Flint 测试规范门禁");
        Console.WriteLine($" 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        // T4：[Fact]/[Theory] 测试方法命名三段式（棘轮：存量 58 处合法，新增禁）
        var t4Regex = new Regex(@"\[(Fact|Theory)\][^{]*?(?:async\s+)?(?:Task|void|ValueTask)\s+(\w+)\s*\(", RegexOptions.Singleline);
        var t4Bad = new List<string>();
        foreach (var rel in Scanner.CsFiles(rootDir, "tests"))
        {
            var text = Io.ReadAllText(Path.Combine(rootDir, rel));
            foreach (Match m in t4Regex.Matches(text))
            {
                var name = m.Groups[2].Value;
                if (!name.Contains('_'))
                {
                    t4Bad.Add($"{rel}:{name}");
                }
            }
        }

        if (t4Bad.Count <= T4Baseline)
        {
            reporter.Pass($"T4  测试命名棘轮（当前 {t4Bad.Count} ≤ 基线 {T4Baseline}，新增必须 Method_Scenario_ExpectedResult 三段式）");
        }
        else
        {
            reporter.Fail($"T4  测试命名新增违规（当前 {t4Bad.Count} > 基线 {T4Baseline}）：");
            foreach (var line in t4Bad.Take(10))
            {
                Console.WriteLine($"      {line}");
            }
        }

        // T6：外部资源清理模式（new DevServer 的测试必须出现 StopAsync；线索级）
        var t6Missing = new List<string>();
        foreach (var rel in Scanner.CsFiles(rootDir, "tests"))
        {
            var text = Io.ReadAllText(Path.Combine(rootDir, rel));
            if (Regex.IsMatch(text, @"new\s+DevServer") && !text.Contains("StopAsync", StringComparison.Ordinal))
            {
                t6Missing.Add($"{rel}: 创建 DevServer 但无 StopAsync");
            }
        }

        if (t6Missing.Count == 0)
        {
            reporter.Pass("T6  DevServer 类测试含资源关停");
        }
        else
        {
            reporter.Warn($"T6  资源释放线索 {t6Missing.Count} 处（人工核对是否为 client 侧测试）：");
            foreach (var line in t6Missing.Take(10))
            {
                Console.WriteLine($"      {line}");
            }
        }

        // T-DEF-1：.ai 脚本 set 统一（set -uo pipefail / set -euo pipefail）
        var scriptsDir = Path.Combine(Path.GetDirectoryName(rootDir)!, ".ai", "scripts");
        var tdefBad = new List<string>();
        if (Directory.Exists(scriptsDir))
        {
            foreach (var s in Directory.GetFiles(scriptsDir, "*.sh"))
            {
                var text = Io.ReadAllText(s);
                if (!Regex.IsMatch(text, @"(?m)^set -(euo|uo) pipefail"))
                {
                    tdefBad.Add(Path.GetFileName(s));
                }
            }
        }

        if (tdefBad.Count == 0)
        {
            reporter.Pass("T-DEF-1  全部脚本含 set 保护");
        }
        else
        {
            reporter.Fail($"T-DEF-1  脚本缺 set 保护：{string.Join(" ", tdefBad)}");
        }

        // 测试项目引用健全性（xunit 铁律：不引用 Microsoft.NET.Test.Sdk 之外的多 runner）
        var runnerBad = new List<string>();
        foreach (var csproj in Directory.EnumerateFiles(Path.Combine(rootDir, "tests"), "*.csproj", SearchOption.AllDirectories))
        {
            var normalized = csproj.Replace('\\', '/');
            if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
            {
                continue;
            }

            var rel = Path.GetRelativePath(rootDir, csproj).Replace('\\', '/');
            var lines = Io.ReadAllLines(csproj);
            for (var i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], "TUnit|NUnit|MSTest"))
                {
                    runnerBad.Add($"{rel}:{i + 1}:{lines[i]}");
                }
            }
        }

        if (runnerBad.Count == 0)
        {
            reporter.Pass("框架  测试框架单一（xunit 系，无 TUnit/NUnit/MSTest 混入）");
        }
        else
        {
            reporter.Fail("框架  测试框架混入：");
            foreach (var line in runnerBad)
            {
                Console.WriteLine($"      {line}");
            }
        }

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine($" 结果: {reporter.Failed} 失败 / 通过 {reporter.Passed}");
        Console.WriteLine(reporter.Failed == 0 ? " ✅ 测试规范门禁通过" : " ❌ 存在违规，修复后重跑");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        return reporter.Failed == 0 ? 0 : 1;
    }
}

/// <summary>
/// encoding-gate 命令（E1-E4）
/// </summary>
internal static class EncodingGateCommand
{
    internal static Command Build()
    {
        var cmd = new Command("encoding-gate", "编码一致性门禁（E1-E4）");
        cmd.SetAction(_ => Run());
        return cmd;
    }

    private static int Run()
    {
        var rootDir = RepoLocator.FindCodeRoot();
        var workspaceRoot = Path.GetDirectoryName(rootDir)!;
        var fail = 0;

        Console.WriteLine("═══ 编码一致性门禁 ═══");

        // E1: .sh 不混 CRLF（.ai 脚本 + Flint 项目脚本）
        var badSh = new List<string>();
        foreach (var dir in new[] { Path.Combine(workspaceRoot, ".ai", "scripts"), Path.Combine(rootDir, "scripts") })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var f in Directory.EnumerateFiles(dir, "*.sh", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(f);
                if (bytes.Contains((byte)'\r'))
                {
                    badSh.Add(f);
                }
            }
        }

        if (badSh.Count > 0)
        {
            Console.WriteLine($"\u001b[0;31mFAIL E1\u001b[0m 脚本混 CRLF（Linux bash 必死，报 pipefail: invalid option）：\n{string.Join("\n", badSh.Take(5))}");
            fail++;
        }
        else
        {
            Console.WriteLine("\u001b[0;32mPASS E1\u001b[0m 脚本无 CRLF");
        }

        // E2: src/tests 的 .cs 无 BOM
        var badBom = new List<string>();
        foreach (var rel in Scanner.CsFiles(rootDir, "src", "tests"))
        {
            var path = Path.Combine(rootDir, rel);
            using var fs = File.OpenRead(path);
            var head = new byte[3];
            if (fs.Read(head, 0, 3) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            {
                badBom.Add(rel);
            }
        }

        if (badBom.Count > 0)
        {
            Console.WriteLine($"\u001b[0;31mFAIL E2\u001b[0m .cs 含 UTF-8 BOM：\n{string.Join("\n", badBom.Take(5))}");
            fail++;
        }
        else
        {
            Console.WriteLine("\u001b[0;32mPASS E2\u001b[0m .cs 无 BOM");
        }

        // E3: .cs 无 mojibake 指纹（UTF-8→GBK 双重编码常见产物）
        var mojiPattern = "鈺|鏈嶅|鍚庡|鐢熷|娴嬭|宸叉湁|鍋滄|鎵涘|灏忛|閰嶇疆|缁堟|寮傚|涓嶅|鍣ㄦ|鐢ㄤ|鈫|姝ラ|鍒涘缓|琛ュ伩|妯℃|瀹炲|妫€|闅旂|鍙傛暟|搿|ʵʾ";
        var mojiRegex = new Regex(mojiPattern);
        var badMoji = new List<string>();
        foreach (var rel in Scanner.CsFiles(rootDir, "src", "tests"))
        {
            var text = Io.ReadAllText(Path.Combine(rootDir, rel));
            if (mojiRegex.IsMatch(text))
            {
                badMoji.Add(rel);
            }
        }

        if (badMoji.Count > 0)
        {
            Console.WriteLine($"\u001b[0;31mFAIL E3\u001b[0m .cs 含 mojibake 指纹（UTF-8→GBK 双重编码产物）：\n{string.Join("\n", badMoji.Take(5))}");
            fail++;
        }
        else
        {
            Console.WriteLine("\u001b[0;32mPASS E3\u001b[0m .cs 无 mojibake");
        }

        // E4: .cs 可正常 UTF-8 解码（严校验，等价 iconv -f UTF-8 -t UTF-8）
        var badUtf8 = new List<string>();
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        foreach (var rel in Scanner.CsFiles(rootDir, "src", "tests"))
        {
            var bytes = File.ReadAllBytes(Path.Combine(rootDir, rel));
            try
            {
                strictUtf8.GetCharCount(bytes);
            }
            catch (System.Text.DecoderFallbackException)
            {
                badUtf8.Add(rel);
            }
        }

        if (badUtf8.Count > 0)
        {
            Console.WriteLine($"\u001b[0;31mFAIL E4\u001b[0m .cs 非 UTF-8 可解码（编码损伤）：\n{string.Join("\n", badUtf8.Take(5))}");
            fail++;
        }
        else
        {
            Console.WriteLine("\u001b[0;32mPASS E4\u001b[0m .cs UTF-8 解码全部通过");
        }

        Console.WriteLine($"═══ 结果：{fail} 失败 ═══");
        return fail == 0 ? 0 : 1;
    }
}
