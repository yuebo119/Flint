// Flint 主题迁移工具 CLI
//
// 用法:
//   Flint.ThemeMigrator <源主题目录> <输出目录> [选项]
//
// 选项:
//   --report <md>       迁移报告输出路径
//   --verify <站点目录>  四门禁后两关：构建验证（站点含 Flint.toml）
//   --site-output <dir>  构建输出目录（默认 <站点>/public）
//   --flint <exe>        Flint CLI 路径（默认自动探测）
//   --hugo-output <dir>  Hugo 侧产物目录（给出则启用产物差分门禁）
//
// 四门禁：
//   ① 表达式级（AST 转换完整）      —— 迁移时执行
//   ② 模板级（Scriban.Parse 通过）  —— 迁移时执行，有失败则退出码 2
//   ③ 站点级（flint build 零错误）  —— --verify 时执行
//   ④ 产物级（与 Hugo diff 一致）   —— --verify + --hugo-output 时执行

using Flint.ThemeMigrator.Migration;
using Flint.ThemeMigrator.Validation;

string? reportPath = null;
string? verifySite = null;
string? siteOutput = null;
string? flintExe = null;
string? hugoOutput = null;

// 解析：前两个位置参数为源/目标，其余为选项
var positional = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--report" when i + 1 < args.Length:
            reportPath = args[++i];
            break;
        case "--verify" when i + 1 < args.Length:
            verifySite = args[++i];
            break;
        case "--site-output" when i + 1 < args.Length:
            siteOutput = args[++i];
            break;
        case "--flint" when i + 1 < args.Length:
            flintExe = args[++i];
            break;
        case "--hugo-output" when i + 1 < args.Length:
            hugoOutput = args[++i];
            break;
        default:
            positional.Add(args[i]);
            break;
    }
}

if (positional.Count < 2)
{
    Console.Error.WriteLine(
        "用法: Flint.ThemeMigrator <源主题目录> <输出目录> [--report <md>] " +
        "[--verify <站点目录>] [--site-output <dir>] [--flint <exe>] [--hugo-output <dir>]");
    return 1;
}

var source = positional[0];
var target = positional[1];

Console.WriteLine($"迁移: {source} → {target}");
var sw = System.Diagnostics.Stopwatch.StartNew();
var migrator = new ThemeMigrator();
var summary = migrator.Migrate(source, target);
sw.Stop();

Console.WriteLine();
Console.WriteLine("=== 门禁①② 迁移与预检 ===");
Console.WriteLine($"  文件: 扫描 {summary.FilesSeen}，转换 {summary.FilesConverted}，复制 {summary.FilesCopied}");
Console.WriteLine($"  表达式: {summary.TotalExpressions}，不支持 {summary.TotalUnsupported}，" +
                  $"降级 {summary.TotalDowngraded}");
Console.WriteLine($"  机械转换率: {summary.MechanicalRate * 100:F1}%");
Console.WriteLine($"  Scriban 预检失败: {summary.ParseFailures}");
Console.WriteLine($"  耗时: {sw.ElapsedMilliseconds}ms");

if (summary.ParseFailures > 0)
{
    Console.WriteLine();
    Console.WriteLine("=== 预检失败明细（前 10）===");
    foreach (var r in summary.Results.Where(r => !r.ParseOk).Take(10))
    {
        Console.WriteLine($"  [{r.RelativePath}] {r.ParseError}");
    }
}

// ---- 门禁③④：构建与产物 diff ----
BuildGateResult? buildResult = null;
DiffGateResult? diffResult = null;

if (verifySite is not null)
{
    var resolvedFlint = flintExe ?? DetectFlintExe();
    if (resolvedFlint is null)
    {
        Console.Error.WriteLine("\n[门禁③] 未找到 Flint CLI（用 --flint <exe> 显式指定）");
    }
    else
    {
        var outDir = siteOutput ?? Path.Combine(verifySite, "public");
        Console.WriteLine();
        Console.WriteLine($"=== 门禁③ 构建验证 ===");
        buildResult = Gates.RunBuildGate(resolvedFlint, verifySite, outDir);
        Console.WriteLine($"  结果: {(buildResult.Success ? "通过" : "失败")}（exit={buildResult.ExitCode}）");
        if (!buildResult.Success)
        {
            foreach (var e in buildResult.Errors.Take(8))
            {
                Console.WriteLine($"    {e}");
            }
        }

        if (hugoOutput is not null && Directory.Exists(hugoOutput))
        {
            Console.WriteLine();
            Console.WriteLine("=== 门禁④ 产物差分验证 ===");
            diffResult = Gates.RunDiffGate(hugoOutput, outDir);
            Console.WriteLine($"  对称性: {(diffResult.Symmetric ? "通过" : "不通过")}");
            Console.WriteLine($"  共有页面: {diffResult.CommonPages}");
            Console.WriteLine($"  仅 Hugo: {diffResult.OnlyInHugo.Count} / 仅 Flint: {diffResult.OnlyInFlint.Count}");
            Console.WriteLine($"  平均结构相似度: {diffResult.AverageStructuralSimilarity * 100:F1}%");
            Console.WriteLine($"  平均文本覆盖度: {diffResult.AverageTextCoverage * 100:F1}%");
        }
    }
}

if (reportPath is not null)
{
    var extra = diffResult is not null || buildResult is not null
        ? Gates.FormatReport(diffResult ?? new DiffGateResult(true, [], [], 0, 0, 0, []), buildResult)
        : "";
    WriteReport(reportPath, summary, source, target, extra);
    Console.WriteLine($"\n报告: {reportPath}");
}

Console.WriteLine();
Console.WriteLine(
    $"SUMMARY exprs={summary.TotalExpressions} unsupported={summary.TotalUnsupported} " +
    $"downgraded={summary.TotalDowngraded} parsefail={summary.ParseFailures} " +
    $"files={summary.FilesConverted} rate={summary.MechanicalRate * 100:F1}" +
    (buildResult is not null ? $" build={(buildResult.Success ? 1 : 0)}" : "") +
    (diffResult is not null ? $" symmetric={(diffResult.Symmetric ? 1 : 0)}" +
        $" struct={diffResult.AverageStructuralSimilarity * 100:F1}" +
        $" text={diffResult.AverageTextCoverage * 100:F1}" : ""));

// 退出码：预检失败=2；构建失败=3；产物不对称=4；全通过=0
if (summary.ParseFailures > 0)
{
    return 2;
}
if (buildResult is { Success: false })
{
    return 3;
}
if (diffResult is { Symmetric: false })
{
    return 4;
}
return 0;

static string? DetectFlintExe()
{
    // 相对本程序探测 Flint CLI（源码树布局）
    var baseDir = AppContext.BaseDirectory;
    var candidates = new[]
    {
        Path.Combine(baseDir, "..", "..", "..", "..", "Flint.Cli", "bin", "Release", "net10.0", "win-x64", "Flint.exe"),
        Path.Combine(baseDir, "..", "..", "..", "..", "Flint.Cli", "bin", "Release", "net10.0", "Flint.exe"),
        Path.Combine(baseDir, "..", "..", "..", "..", "Flint.Cli", "bin", "Debug", "net10.0", "Flint.exe")
    };
    foreach (var c in candidates)
    {
        var full = Path.GetFullPath(c);
        if (File.Exists(full))
        {
            return full;
        }
    }
    return null;
}

static void WriteReport(string path, MigrationSummary summary, string source, string target, string extra)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# 主题迁移报告");
    sb.AppendLine();
    sb.AppendLine($"- 源: `{source}`");
    sb.AppendLine($"- 输出: `{target}`");
    sb.AppendLine($"- 生成: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine();
    sb.AppendLine("## 门禁①② 迁移汇总");
    sb.AppendLine();
    sb.AppendLine("| 指标 | 数值 |");
    sb.AppendLine("|---|---|");
    sb.AppendLine($"| 扫描文件 | {summary.FilesSeen} |");
    sb.AppendLine($"| 转换文件 | {summary.FilesConverted} |");
    sb.AppendLine($"| 复制文件 | {summary.FilesCopied} |");
    sb.AppendLine($"| 表达式总数 | {summary.TotalExpressions} |");
    sb.AppendLine($"| 不支持（TODO） | {summary.TotalUnsupported} |");
    sb.AppendLine($"| 降级 | {summary.TotalDowngraded} |");
    sb.AppendLine($"| **机械转换率** | **{summary.MechanicalRate * 100:F1}%** |");
    sb.AppendLine($"| Scriban 预检失败 | {summary.ParseFailures} |");
    sb.AppendLine();

    if (!string.IsNullOrEmpty(extra))
    {
        sb.AppendLine(extra);
    }

    if (summary.ParseFailures > 0)
    {
        sb.AppendLine("## 预检失败（需人工处理）");
        sb.AppendLine();
        foreach (var r in summary.Results.Where(r => !r.ParseOk))
        {
            sb.AppendLine($"- `{r.RelativePath}`: {r.ParseError}");
        }
        sb.AppendLine();
    }

    var withNotes = summary.Results.Where(r => r.Notes.Count > 0).ToList();
    if (withNotes.Count > 0)
    {
        sb.AppendLine("## 降级/不支持明细（可审计）");
        sb.AppendLine();
        foreach (var r in withNotes.Take(50))
        {
            sb.AppendLine($"### {r.RelativePath}");
            foreach (var n in r.Notes.Distinct().Take(20))
            {
                sb.AppendLine($"- {n}");
            }
            sb.AppendLine();
        }
    }

    File.WriteAllText(path, sb.ToString());
}
