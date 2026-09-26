// Flint 主题迁移工具
// 四门禁后两关：构建验证 + 产物差分验证
//
// 门禁设计（阶段 2）：
//   ① 表达式级：AST 转换完整           —— 阶段 1 已实现
//   ② 模板级：Scriban.Parse 通过        —— 阶段 1 已实现
//   ③ 站点级：flint build 零错误        —— 本文件
//   ④ 产物级：与 Hugo 产物 diff 一致    —— 本文件（准确性的唯一强凭据）
//
// 为什么必须有 ④ 的实测依据：本项目两次假成功（批次五 EXIT=0 但模板归属错、
// MDN 对比产物不对称）都发生在"只看了前 3 关"的情况下。

using System.Diagnostics;
using System.Text;

namespace Flint.ThemeMigrator.Validation;

/// <summary>构建门禁结果</summary>
internal sealed record BuildGateResult(
    bool Success,
    int ExitCode,
    string Output,
    IReadOnlyList<string> Errors);

/// <summary>产物对比结果</summary>
internal sealed record DiffGateResult(
    bool Symmetric,
    IReadOnlyList<string> OnlyInHugo,
    IReadOnlyList<string> OnlyInFlint,
    int CommonPages,
    double AverageStructuralSimilarity,
    double AverageTextCoverage,
    IReadOnlyList<(string Page, double Structural, double Text)> PerPage,
    IReadOnlyList<ElementSignatureCount> TopMissingElements,
    IReadOnlyList<ElementSignatureCount> TopExtraElements,
    IReadOnlyList<PageElementDiff> WorstPages)
{
    /// <summary>缺元素总量（聚合后）</summary>
    public int MissingTotal => TopMissingElements.Sum(m => m.Delta);

    /// <summary>多元素总量（聚合后）</summary>
    public int ExtraTotal => TopExtraElements.Sum(m => m.Delta);

    /// <summary>逐页差异为空的构造（门禁未跑时的占位）</summary>
    public static DiffGateResult Empty { get; } = new(
        true, [], [], 0, 0, 0, [], [], [], []);
}

/// <summary>门禁执行器</summary>
internal static class Gates
{
    /// <summary>
    /// 门禁③：构建验证。调用 Flint CLI 构建迁移后的站点。
    /// </summary>
    /// <param name="flintExe">Flint CLI 可执行文件路径</param>
    /// <param name="siteDir">站点目录（含 Flint.toml 与 themes/）</param>
    /// <param name="outputDir">输出目录</param>
    public static BuildGateResult RunBuildGate(string flintExe, string siteDir, string outputDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = flintExe,
            ArgumentList =
            {
                "build", "-s", siteDir, "-o", outputDir, "--clean", "--missing-layout", "skip"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 Flint CLI");

        // **并发排水两根管道**：先 ReadToEnd(stdout) 再 ReadToEnd(stderr) 会在
        // 子进程 stderr 写满管道缓冲区（4KB）时互相等死——loveit 的构建向 stderr
        // 写大量渲染诊断，实测把门禁③构建挂死（5 分钟超时被杀）
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        process.WaitForExit(TimeSpan.FromMinutes(5));

        var combined = stdout + "\n" + stderr;
        var errors = combined
            .Split('\n')
            .Where(l => l.Contains("RENDER", StringComparison.Ordinal) ||
                        l.Contains("TAXONOMY", StringComparison.Ordinal) ||
                        l.Contains("BUILD", StringComparison.Ordinal) ||
                        l.Contains("[ERROR]", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return new BuildGateResult(process.ExitCode == 0, process.ExitCode, combined, errors);
    }

    /// <summary>
    /// 门禁④：产物差分验证（含对称性审计）。
    ///
    /// 对称性审计先行（MDN 教训）：页面集合不一致即为无效对比，先报集合差异。
    /// </summary>
    /// <param name="hugoOutput">Hugo 侧产物目录</param>
    /// <param name="flintOutput">Flint 侧产物目录</param>
    public static DiffGateResult RunDiffGate(string hugoOutput, string flintOutput)
    {
        var hugoPages = EnumeratePages(hugoOutput);
        var flintPages = EnumeratePages(flintOutput);

        var onlyHugo = hugoPages.Keys.Except(flintPages.Keys, StringComparer.Ordinal).OrderBy(k => k).ToList();
        var onlyFlint = flintPages.Keys.Except(hugoPages.Keys, StringComparer.Ordinal).OrderBy(k => k).ToList();
        var common = hugoPages.Keys.Intersect(flintPages.Keys, StringComparer.Ordinal).OrderBy(k => k).ToList();

        var perPage = new List<(string, double, double)>();
        var pageDiffs = new List<PageElementDiff>();
        foreach (var page in common)
        {
            var hugoText = Normalize(ReadText(hugoPages[page]));
            var flintText = Normalize(ReadText(flintPages[page]));
            perPage.Add((page, StructuralSimilarity(hugoText, flintText), TextCoverage(hugoText, flintText)));
            // 结构化元素差：把标量分数落到"缺什么/多什么"（同一份文本，无需二次读盘）
            var missingTokens = MissingWords(hugoText, flintText);
            pageDiffs.Add(ElementDiff.Build(page, ElementDiff.SignatureCounts(hugoText), ElementDiff.SignatureCounts(flintText))
                with { MissingTextTokens = missingTokens });
        }

        var avgStructural = perPage.Count == 0 ? 0 : perPage.Average(p => p.Item2);
        var avgText = perPage.Count == 0 ? 0 : perPage.Average(p => p.Item3);

        // 主题级聚合：最多 20 条签名差 + 缺得最多的 20 个页面
        var topMissing = ElementDiff.Aggregate(pageDiffs, missing: true, topN: 20);
        var topExtra = ElementDiff.Aggregate(pageDiffs, missing: false, topN: 20);
        var worst = pageDiffs
            .Where(d => d.Missing.Count > 0 || d.Extra.Count > 0)
            .OrderByDescending(d => d.MissingTotal)
            .ThenBy(d => d.Page, StringComparer.Ordinal)
            .Take(20)
            .ToList();

        // 对称：**必须有共有页面**且无单侧页面。
        // 空对比（两侧都 0 页，通常因构建失败）不算对称——否则"构建失败"会被
        // 误报为 symmetric=1（矩阵验证实测：loveit 构建失败却报对称）
        // Hugo 的 page/1/ 是第 1 页副本，不参与单侧判定
        var effectiveOnlyHugo = onlyHugo.Where(p => !p.Contains("page/1/", StringComparison.Ordinal)).ToList();
        var symmetric = common.Count > 0 && effectiveOnlyHugo.Count == 0 && onlyFlint.Count == 0;

        return new DiffGateResult(
            symmetric, onlyHugo, onlyFlint, common.Count, avgStructural, avgText, perPage,
            topMissing, topExtra, worst);
    }

    /// <summary>Hugo 侧出现而 Flint 侧缺失的文本词（前 20，供"文案丢失"定位）</summary>
    private static IReadOnlyList<string> MissingWords(string hugo, string flint)
    {
        var hw = System.Text.RegularExpressions.Regex
            .Matches(hugo, @"[A-Za-z]{4,}")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);
        var fw = System.Text.RegularExpressions.Regex
            .Matches(flint, @"[A-Za-z]{4,}")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);
        return hw.Where(w => !fw.Contains(w)).OrderBy(w => w, StringComparer.Ordinal).Take(20).ToList();
    }

    private static Dictionary<string, string> EnumeratePages(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(root))
        {
            return result;
        }

        foreach (var f in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            result[rel] = f;
        }
        return result;
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return "";
        }
    }

    /// <summary>
    /// 归一化：剔除不稳定内容（脚本块、哈希、时间戳、空白），
    /// 使对比聚焦于结构而非动态值
    /// </summary>
    internal static string Normalize(string html)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(
            html, "<script[^>]*>.*?</script>", "<script/>",
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[0-9a-f]{8,64}", "HASH");
        s = System.Text.RegularExpressions.Regex.Replace(
            s, @"\d{4}-\d{2}-\d{2}T[\d:.]+Z?", "TS");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    /// <summary>结构相似度：标签序列逐位一致比例</summary>
    internal static double StructuralSimilarity(string a, string b)
    {
        var ta = Tags(a);
        var tb = Tags(b);
        if (ta.Count == 0 && tb.Count == 0)
        {
            return 1;
        }
        var n = Math.Min(ta.Count, tb.Count);
        var same = 0;
        for (var i = 0; i < n; i++)
        {
            if (ta[i] == tb[i])
            {
                same++;
            }
        }
        return (double)same / Math.Max(ta.Count, tb.Count);
    }

    /// <summary>文本覆盖度：Hugo 侧词集合被 Flint 覆盖的比例</summary>
    internal static double TextCoverage(string a, string b)
    {
        var wa = Words(a);
        var wb = Words(b);
        if (wa.Count == 0)
        {
            return 1;
        }
        return (double)wa.Count(w => wb.Contains(w)) / wa.Count;
    }

    private static List<string> Tags(string s) =>
        System.Text.RegularExpressions.Regex
            .Matches(s, @"</?([a-zA-Z][\w-]*)")
            .Select(m => m.Groups[1].Value)
            .ToList();

    private static HashSet<string> Words(string s) =>
        System.Text.RegularExpressions.Regex
            .Matches(s, @"[A-Za-z]{4,}")
            .Select(m => m.Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>生成差分报告</summary>
    public static string FormatReport(DiffGateResult diff, BuildGateResult? build)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## 门禁结果");
        sb.AppendLine();

        if (build is not null)
        {
            sb.AppendLine($"### ③ 构建门禁: {(build.Success ? "通过" : "失败")}（exit={build.ExitCode}）");
            sb.AppendLine();
            if (!build.Success && build.Errors.Count > 0)
            {
                sb.AppendLine("错误（前 10）:");
                foreach (var e in build.Errors.Take(10))
                {
                    sb.AppendLine($"- {e}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine($"### ④ 产物门禁: {(diff.Symmetric ? "对称" : "不对称")}");
        sb.AppendLine();
        sb.AppendLine($"- 共有页面: {diff.CommonPages}");
        sb.AppendLine($"- 仅 Hugo: {diff.OnlyInHugo.Count}" +
                      (diff.OnlyInHugo.Count > 0 ? $" ({string.Join(", ", diff.OnlyInHugo.Take(5))})" : ""));
        sb.AppendLine($"- 仅 Flint: {diff.OnlyInFlint.Count}" +
                      (diff.OnlyInFlint.Count > 0 ? $" ({string.Join(", ", diff.OnlyInFlint.Take(5))})" : ""));
        sb.AppendLine($"- 平均结构相似度: {diff.AverageStructuralSimilarity * 100:F1}%");
        sb.AppendLine($"- 平均文本覆盖度: {diff.AverageTextCoverage * 100:F1}%");
        sb.AppendLine();

        // 结构化差异：相似度只能说明"像不像"，这一节说明"差在哪"（可直接执行）
        sb.AppendLine("### 结构化元素差异（缺什么 / 多什么）");
        sb.AppendLine();
        if (diff.TopMissingElements.Count == 0 && diff.TopExtraElements.Count == 0)
        {
            sb.AppendLine("两侧元素签名多重集完全一致。");
        }
        else
        {
            if (diff.TopMissingElements.Count > 0)
            {
                sb.AppendLine($"**缺元素**（Hugo 有、Flint 缺，共 {diff.MissingTotal} 处）:");
                foreach (var m in diff.TopMissingElements)
                {
                    sb.AppendLine($"- `{ElementDiff.Describe(m)}`");
                }
                sb.AppendLine();
            }

            if (diff.TopExtraElements.Count > 0)
            {
                sb.AppendLine($"**多元素**（Flint 多出，共 {diff.ExtraTotal} 处）:");
                foreach (var e in diff.TopExtraElements)
                {
                    sb.AppendLine($"- `{ElementDiff.Describe(e)}`");
                }
                sb.AppendLine();
            }

            if (diff.WorstPages.Count > 0)
            {
                sb.AppendLine("| 页面 | 缺元素（前 3） | 多元素（前 3） | 缺文本词（前 3） |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var p in diff.WorstPages)
                {
                    var miss = p.Missing.Count == 0
                        ? "—"
                        : string.Join("、", p.Missing.Take(3).Select(m => $"`{m.Signature}`×{m.Delta}"));
                    var extra = p.Extra.Count == 0
                        ? "—"
                        : string.Join("、", p.Extra.Take(3).Select(e => $"`{e.Signature}`×{e.Delta}"));
                    var words = p.MissingTextTokens.Count == 0
                        ? "—"
                        : string.Join("、", p.MissingTextTokens.Take(3));
                    sb.AppendLine($"| `{p.Page}` | {miss} | {extra} | {words} |");
                }
                sb.AppendLine();
            }
        }

        if (diff.PerPage.Count > 0)
        {
            sb.AppendLine("| 页面 | 结构相似 | 文本覆盖 |");
            sb.AppendLine("|---|---|---|");
            foreach (var (page, structural, text) in diff.PerPage.OrderBy(p => p.Item2).Take(20))
            {
                sb.AppendLine($"| `{page}` | {structural * 100:F0}% | {text * 100:F0}% |");
            }
        }

        return sb.ToString();
    }
}
