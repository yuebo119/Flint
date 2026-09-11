// Flint 主题迁移工具 CLI
//
// 用法: Flint.ThemeMigrator <源主题目录> <输出目录> [--report <md>]
//
// 阶段 1 实现四门禁的前两关（AST 完整性 + Scriban 预检），
// 后两关（构建 / 产物 diff）由编排的后续步骤完成。

using Flint.ThemeMigrator.Migration;

if (args.Length < 2)
{
    Console.Error.WriteLine("用法: Flint.ThemeMigrator <源主题目录> <输出目录> [--report <md>]");
    return 1;
}

var source = args[0];
var target = args[1];
string? reportPath = null;
for (var i = 2; i + 1 < args.Length; i++)
{
    if (args[i] == "--report")
    {
        reportPath = args[i + 1];
    }
}

Console.WriteLine($"迁移: {source} → {target}");
var sw = System.Diagnostics.Stopwatch.StartNew();
var migrator = new ThemeMigrator();
var summary = migrator.Migrate(source, target);
sw.Stop();

Console.WriteLine();
Console.WriteLine("=== 迁移结果 ===");
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

if (reportPath is not null)
{
    WriteReport(reportPath, summary, source, target);
    Console.WriteLine($"\n报告: {reportPath}");
}

// 机器可读汇总（ASCII，供脚本采集；避免中文编码问题）
Console.WriteLine(
    $"SUMMARY exprs={summary.TotalExpressions} unsupported={summary.TotalUnsupported} " +
    $"downgraded={summary.TotalDowngraded} parsefail={summary.ParseFailures} " +
    $"files={summary.FilesConverted} rate={summary.MechanicalRate * 100:F1}");

// 有预检失败 → 非零退出（门禁语义）
return summary.ParseFailures > 0 ? 2 : 0;

static void WriteReport(string path, MigrationSummary summary, string source, string target)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# 主题迁移报告");
    sb.AppendLine();
    sb.AppendLine($"- 源: `{source}`");
    sb.AppendLine($"- 输出: `{target}`");
    sb.AppendLine($"- 生成: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
    sb.AppendLine();
    sb.AppendLine("## 汇总");
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
