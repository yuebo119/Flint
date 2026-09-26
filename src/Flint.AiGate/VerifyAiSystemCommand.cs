// verify-ai-system：.ai 系统机械校验（.ai/scripts/verify-ai-system.sh 的 C# 移植）
// 迁移期适配：已 C# 化的门禁改查 src/Flint.AiGate 源码文件；未迁移的 .sh 仍查存在性与语法。

using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flint.AiGate;

/// <summary>
/// verify-ai-system 命令
/// </summary>
internal static class VerifyAiSystemCommand
{
    // 已 C# 化的门禁（验证源码文件存在替代原 .sh 存在性）
    private static readonly string[] PortedGates =
    {
        "src/Flint.AiGate/GateCheckCommand.cs",
        "src/Flint.AiGate/TestGateCommand.cs",
        "src/Flint.AiGate/FlakyGateCommand.cs",
        "src/Flint.AiGate/ReviewGatesCommand.cs",
        "src/Flint.AiGate/VerifyAiSystemCommand.cs",
    };

    // .ai/scripts/ 已全部 C# 化（2026-09-26，CS-41 完成）：无剩余 bash 脚本。
    // 后续新增门禁一律进 src/Flint.AiGate 子命令（CONVENTIONS §14）。
    private static readonly string[] RemainingBashScripts = Array.Empty<string>();

    internal static Command Build()
    {
        var cmd = new Command("verify-ai-system", ".ai 系统一致性校验（V1-V16）");
        cmd.SetAction(_ => Run());
        return cmd;
    }

    private static int Run()
    {
        var codeRoot = RepoLocator.FindCodeRoot();
        var workspaceRoot = RepoLocator.FindWorkspaceRoot(codeRoot); // .ai 与 Flint/ 平级或内嵌
        var ai = Path.Combine(workspaceRoot, ".ai");
        var reporter = new Reporter();

        Console.WriteLine("═══════ .ai 系统一致性校验（Flint）═══════");
        Console.WriteLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

        // V1 四系统入口 + 引擎 + 账本齐全
        var v1Files = new[]
        {
            ".ai/README.md", ".ai/lessons.md", ".ai/review/engine.md", ".ai/review/prompt.md",
            ".ai/review/metrics.md", ".ai/review/known-false-positives.md", ".ai/review/perspective-stats.md",
            ".ai/review/sibling-map.md", ".ai/review/action-items-p3-backlog.md", ".ai/review/baseline-unified-v2.md",
            ".ai/review/review-charter-v2.md", ".ai/review/fix-protocol.md", ".ai/review/lessons-learned-v51.md",
            ".ai/gate/prompt.md", ".ai/gate/sensor-ledger.md", ".ai/refine/prompt.md", ".ai/test/prompt.md",
        };
        CheckFileList("V1", "四系统入口/引擎/账本齐全", "系统文件缺失", workspaceRoot, v1Files, reporter);

        // V2 提示词引用的工具全部存在（C# 化门禁查源码；未迁移 .sh 查存在）
        var v2Missing = new List<string>();
        foreach (var src in PortedGates)
        {
            if (!File.Exists(Path.Combine(workspaceRoot, "Flint", src)))
            {
                v2Missing.Add($"Flint/{src}");
            }
        }

        foreach (var s in RemainingBashScripts)
        {
            if (!File.Exists(Path.Combine(ai, "scripts", s)))
            {
                v2Missing.Add($"scripts/{s}");
            }
        }

        if (v2Missing.Count == 0)
        {
            reporter.Pass("V2: 提示词引用的工具全部存在（C# 门禁源码 + 未迁移 .sh）");
        }
        else
        {
            reporter.Fail($"V2: 被引用工具缺失（{string.Join(" ", v2Missing)}）");
        }

        // V3 README 文件地图与实际目录一致
        var readmePath = Path.Combine(ai, "README.md");
        var v3Missing = new List<string>();
        if (File.Exists(readmePath))
        {
            var readme = Io.ReadAllText(readmePath);
            foreach (Match m in Regex.Matches(readme, @"(gate|refine|review|test|scripts)/[a-zA-Z0-9._/-]+\.(md|sh)"))
            {
                var rel = m.Value;
                if (!File.Exists(Path.Combine(ai, rel)))
                {
                    v3Missing.Add($".ai/{rel}");
                }
            }
        }

        if (v3Missing.Count == 0)
        {
            reporter.Pass("V3: README 文件地图与实际一致");
        }
        else
        {
            reporter.Fail($"V3: README 文件地图指向不存在的文件（{string.Join(" ", v3Missing)}）");
        }

        // V4 门禁编号三方一致：gate 提示词、C# 门禁源码、README 的最大 G 编号
        var gPrompt = MaxNumber(Regex.Matches(ReadIfExists(Path.Combine(ai, "gate", "prompt.md")), @"FLINT-G[0-9]+"));
        var gScript = MaxNumber(Regex.Matches(ReadIfExists(Path.Combine(workspaceRoot, "Flint", "src", "Flint.AiGate", "GateCheckCommand.cs")), @"FLINT-G[0-9]+"));
        var gReadme = MaxNumber(Regex.Matches(ReadIfExists(readmePath), @"FLINT-G1\.\.G[0-9]+"));
        if (gPrompt == gScript && gScript == gReadme && gScript > 0)
        {
            reporter.Pass($"V4: 门禁编号三方一致（FLINT-G1..G{gScript}）");
        }
        else
        {
            reporter.Fail($"V4: 门禁编号不同步（提示词 G{gPrompt}，源码 G{gScript}，README G{gReadme}）");
        }

        // V5 误判知识库可加载且通用模式齐备（通用 ≥8）
        var kbPath = Path.Combine(ai, "review", "known-false-positives.md");
        var kbCommon = Regex.Matches(ReadIfExists(kbPath), @"(?m)^### 模式 [0-9]+：").Count;
        if (kbCommon >= 8)
        {
            reporter.Pass($"V5: 误判知识库加载正常（通用模式 {kbCommon} 条）");
        }
        else
        {
            reporter.Fail($"V5: 误判知识库通用模式不足（仅 {kbCommon} 条，预期 ≥8）");
        }

        // V6 metrics 账本存在且结构完整
        var metrics = ReadIfExists(Path.Combine(ai, "review", "metrics.md"));
        if (metrics.Contains("## 轮次记录", StringComparison.Ordinal)
            && metrics.Contains("## 缺陷逃逸账本", StringComparison.Ordinal)
            && metrics.Contains("## 根因分类", StringComparison.Ordinal))
        {
            reporter.Pass("V6: metrics 账本存在且结构完整");
        }
        else
        {
            reporter.Fail("V6: metrics 账本缺失或结构不完整（需含「轮次记录」「缺陷逃逸账本」「根因分类」）");
        }

        // V7 perspective-stats 含七流记录
        var perspectiveStats = ReadIfExists(Path.Combine(ai, "review", "perspective-stats.md"));
        var flowRows = Regex.Matches(perspectiveStats, @"^\| (架构|安全|资源|并发|错误|构建语义) ?流|^\| AOT\/裁剪流", RegexOptions.Multiline).Count;
        if (flowRows >= 7)
        {
            reporter.Pass("V7: 视角发现率账本存在且七流全记录");
        }
        else
        {
            reporter.Fail("V7: 视角发现率账本缺失或七流记录不全");
        }

        // V8 lessons.md 结构完整
        var lessons = ReadIfExists(Path.Combine(ai, "lessons.md"));
        if (lessons.Contains("## I. AI 协作铁律", StringComparison.Ordinal)
            && lessons.Contains("V.3 反向验证", StringComparison.Ordinal)
            && lessons.Contains("## 维护规则", StringComparison.Ordinal)
            && lessons.Contains("II.2 起板时已知的存量债务", StringComparison.Ordinal))
        {
            reporter.Pass("V8: lessons.md 结构完整（铁律/诊断三步骤/维护规则/存量债务台账）");
        }
        else
        {
            reporter.Fail("V8: lessons.md 结构异常（需含铁律表/诊断三步骤/维护规则/II.2 存量债务）");
        }

        // V9 剩余 .ai 脚本 bash -n 语法检查（直读退出码，不经管道）
        var syntaxBad = new List<string>();
        foreach (var s in RemainingBashScripts)
        {
            var path = Path.Combine(ai, "scripts", s);
            if (!File.Exists(path))
            {
                continue;
            }

            if (Bench.RunTimedQuiet("bash", new[] { "-n", path }) != 0)
            {
                syntaxBad.Add(s);
            }
        }

        if (syntaxBad.Count == 0)
        {
            reporter.Pass("V9: 剩余 .ai 脚本 bash -n 语法通过");
        }
        else
        {
            reporter.Fail($"V9: .ai 脚本语法失败（{string.Join(" ", syntaxBad)}）");
        }

        // V10 修复门协议在档
        var engine = ReadIfExists(Path.Combine(ai, "review", "engine.md"));
        if (engine.Contains("修复门两问", StringComparison.Ordinal) && engine.Contains("s ≤ p'", StringComparison.Ordinal))
        {
            reporter.Pass("V10: 修复门两问协议在档（engine.md 评审-修复循环协议）");
        }
        else
        {
            reporter.Fail("V10: 修复门协议缺失（engine.md 需含「修复门两问」与 s ≤ p' 关键词）");
        }

        // V11 传感器台账结构完整
        var sensorLedger = ReadIfExists(Path.Combine(ai, "gate", "sensor-ledger.md"));
        if (sensorLedger.Contains("定标状态", StringComparison.Ordinal) && sensorLedger.Contains("UNKNOWN", StringComparison.Ordinal))
        {
            reporter.Pass("V11: 传感器台账存在且含定标状态机");
        }
        else
        {
            reporter.Fail("V11: 传感器台账缺失/结构异常（需含定标状态列与 UNKNOWN 态）");
        }

        // V12 P3 账本老化校验：未勾选条目（- [ ] P3-...）发现日期距今 >30 天即应升 P2
        var v12Bad = new List<string>();
        var backlogPath = Path.Combine(ai, "review", "action-items-p3-backlog.md");
        if (File.Exists(backlogPath))
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            foreach (var line in Io.ReadAllLines(backlogPath))
            {
                if (!line.StartsWith("- [ ] P3-", StringComparison.Ordinal))
                {
                    continue;
                }

                var m = Regex.Match(line, @"(\d{4}-\d{2}-\d{2})");
                if (!m.Success || !DateOnly.TryParse(m.Groups[1].Value, out var d))
                {
                    continue;
                }

                var age = today.DayNumber - d.DayNumber;
                if (age > 30 && !line.Contains("过期", StringComparison.Ordinal))
                {
                    var head = line.Length > 22 ? line[6..22] : line[6..];
                    v12Bad.Add($"{head}…：未勾选已 {age} 天({m.Groups[1].Value})，按规则应升 P2");
                }
            }

            if (v12Bad.Count == 0)
            {
                reporter.Pass("V12: P3 账本无超期未升级条目（30 天老化周期）");
            }
            else
            {
                reporter.Fail($"V12: P3 账本存在超期未处置条目（{string.Join("; ", v12Bad)}）");
            }
        }
        else
        {
            reporter.Fail("V12: action-items-p3-backlog.md 缺失");
        }

        // V13 sibling-map.md 结构完整
        var siblingMap = ReadIfExists(Path.Combine(ai, "review", "sibling-map.md"));
        if (siblingMap.Contains("轴 A", StringComparison.Ordinal) && siblingMap.Contains("轴 B", StringComparison.Ordinal)
            && siblingMap.Contains("联动规则", StringComparison.Ordinal))
        {
            reporter.Pass("V13: sibling-map 结构完整（轴 A/轴 B/联动规则）");
        }
        else
        {
            reporter.Fail("V13: sibling-map 结构异常（需含轴 A/轴 B/联动规则节）");
        }

        // V14 metrics 轮次记录表结构（日期列合法且单调不减、行结构完整）
        var v14Rows = 0;
        var v14Bad = new List<string>();
        string? v14Prev = null;
        var inSection = false;
        foreach (var line in Io.ReadAllLines(Path.Combine(ai, "review", "metrics.md")))
        {
            if (line.StartsWith("## 轮次记录", StringComparison.Ordinal))
            {
                inSection = true;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = false;
                continue;
            }

            if (!inSection || !line.TrimStart().StartsWith('|'))
            {
                continue;
            }

            var dm = Regex.Match(line, @"[0-9]{4}-[0-9]{2}-[0-9]{2}");
            if (!dm.Success || !Regex.IsMatch(dm.Value, @"^\d{4}-\d{2}-\d{2}$"))
            {
                continue;
            }

            v14Rows++;
            var pipes = line.Count(c => c == '|');
            if (pipes < 12)
            {
                v14Bad.Add($"{dm.Value}:列数不足({pipes})");
            }

            if (v14Prev is not null && string.CompareOrdinal(dm.Value, v14Prev) < 0)
            {
                v14Bad.Add($"{dm.Value}:乱序(前值{v14Prev})");
            }

            v14Prev = dm.Value;
        }

        if (v14Rows >= 1 && v14Bad.Count == 0)
        {
            reporter.Pass($"V14: 账本轮次结构完整（{v14Rows} 行日期单调）");
        }
        else
        {
            reporter.Fail($"V14: 账本轮次结构异常（rows={v14Rows} bad={string.Join(";", v14Bad)}）");
        }

        // V15 oscillation-state.json 可解析且 schema 正确（hist/fp 两键）
        var oscillationPath = Path.Combine(ai, "gate", "oscillation-state.json");
        string? v15Bad = null;
        if (File.Exists(oscillationPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(Io.ReadAllText(oscillationPath));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("hist", out _) || !root.TryGetProperty("fp", out _))
                {
                    v15Bad = "schema 缺键";
                }
            }
            catch (JsonException ex)
            {
                v15Bad = ex.Message;
            }
        }
        else
        {
            v15Bad = "文件缺失";
        }

        if (v15Bad is null)
        {
            reporter.Pass("V15: oscillation-state.json 可解析且 schema 正确");
        }
        else
        {
            reporter.Fail($"V15: oscillation-state.json 异常（{v15Bad}）");
        }

        // V16 .gitignore 覆盖运行时产物
        var gitignore = ReadIfExists(Path.Combine(ai, ".gitignore"));
        var giOk = gitignore.Contains("brain-data/", StringComparison.Ordinal)
            && gitignore.Contains(".serena/", StringComparison.Ordinal)
            && gitignore.Contains("gate/oscillation-state.json", StringComparison.Ordinal);
        if (giOk)
        {
            reporter.Pass("V16: .gitignore 覆盖运行时产物（brain-data/.serena/oscillation-state）");
        }
        else
        {
            reporter.Fail("V16: .gitignore 缺运行时产物条目（需含 brain-data/ .serena/ gate/oscillation-state.json）");
        }

        Console.WriteLine($"\n通过：{reporter.Passed}  失败：{reporter.Failed}  总计：{reporter.Passed + reporter.Failed}");
        Console.WriteLine("═══════ 校验完成 ═══════");
        return reporter.Failed == 0 ? 0 : 1;
    }

    private static void CheckFileList(
        string id, string passDesc, string failDesc, string workspaceRoot, string[] files, Reporter reporter)
    {
        var missing = new List<string>();
        foreach (var f in files)
        {
            if (!File.Exists(Path.Combine(workspaceRoot, f)))
            {
                missing.Add(f);
            }
        }

        if (missing.Count == 0)
        {
            reporter.Pass($"{id}: {passDesc}");
        }
        else
        {
            reporter.Fail($"{id}: {failDesc}（{string.Join(" ", missing)}）");
        }
    }

    private static string ReadIfExists(string path)
    {
        return File.Exists(path) ? Io.ReadAllText(path) : string.Empty;
    }

    private static int MaxNumber(MatchCollection matches)
    {
        return matches
            .Select(m => int.Parse(Regex.Match(m.Value, @"[0-9]+$").Value))
            .DefaultIfEmpty(0)
            .Max();
    }
}
