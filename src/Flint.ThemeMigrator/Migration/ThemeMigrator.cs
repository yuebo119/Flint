// Flint 主题迁移工具
// 迁移编排：目录遍历 → 解析 → 转换 → 预检 → 报告
//
// 四门禁中的前两关在此实现：
//   ① 表达式级：AST 转换完整（无未处理节点）+ 结构守恒检查
//   ② 模板级：Scriban.Parse 预检（引用 Flint.Core 的 Scriban）
// ③ 构建级与 ④ 产物级由 CLI 后续阶段实现。

using Flint.ThemeMigrator.Conversion;
using Flint.ThemeMigrator.Parsing;
using Scriban;

namespace Flint.ThemeMigrator.Migration;

/// <summary>单文件迁移结果</summary>
internal sealed record FileMigrationResult(
    string RelativePath,
    string OutputPath,
    bool ParseOk,
    string? ParseError,
    int Actions,
    int Expressions,
    int Unsupported,
    int Downgraded,
    IReadOnlyList<string> Notes);

/// <summary>迁移汇总</summary>
internal sealed class MigrationSummary
{
    public int FilesSeen { get; set; }
    public int FilesConverted { get; set; }
    public int FilesCopied { get; set; }
    public int ParseFailures { get; set; }
    public int TotalActions { get; set; }
    public int TotalExpressions { get; set; }
    public int TotalUnsupported { get; set; }
    public int TotalDowngraded { get; set; }
    public List<FileMigrationResult> Results { get; } = [];
    public List<string> GlobalDiagnostics { get; } = [];

    /// <summary>机械可转换率（表达式级）</summary>
    public double MechanicalRate =>
        TotalExpressions == 0 ? 1d : 1d - (double)TotalUnsupported / TotalExpressions;
}

/// <summary>
/// 迁移器：Hugo 主题目录 → Flint 主题目录。
/// </summary>
internal sealed class ThemeMigrator
{
    private static readonly HashSet<string> TemplateExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".html", ".xml", ".json", ".txt", ".gotmpl" };

    private readonly MigrationMap _map = MigrationMap.CreateDefault();

    /// <summary>执行迁移</summary>
    /// <param name="sourceRoot">Hugo 主题根目录</param>
    /// <param name="targetRoot">输出目录（Flint 主题布局）</param>
    public MigrationSummary Migrate(string sourceRoot, string targetRoot)
    {
        var summary = new MigrationSummary();
        if (!Directory.Exists(sourceRoot))
        {
            summary.GlobalDiagnostics.Add($"源目录不存在: {sourceRoot}");
            return summary;
        }

        if (Directory.Exists(targetRoot))
        {
            Directory.Delete(targetRoot, recursive: true);
        }
        Directory.CreateDirectory(targetRoot);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceRoot, file);

            // 跳过 VCS / 构建产物 / Hugo Modules 元数据
            if (rel.StartsWith(".git", StringComparison.Ordinal) ||
                rel.Contains("/.git/", StringComparison.Ordinal) ||
                rel.StartsWith("resources/_gen", StringComparison.Ordinal) ||
                rel.EndsWith("go.sum", StringComparison.OrdinalIgnoreCase) ||
                rel.EndsWith("go.mod", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            summary.FilesSeen++;
            var targetPath = Path.Combine(targetRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            var ext = Path.GetExtension(rel);
            if (!TemplateExtensions.Contains(ext))
            {
                // 非模板：原样复制（static/assets 等）
                File.Copy(file, targetPath, overwrite: true);
                summary.FilesCopied++;
                continue;
            }

            var text = File.ReadAllText(file);
            var result = ConvertTemplate(rel, text);
            File.WriteAllText(targetPath, result.Text);

            summary.FilesConverted++;
            summary.TotalActions += result.Stats.Actions;
            summary.TotalExpressions += result.Stats.Expresssions;
            summary.TotalUnsupported += result.Stats.Unsupported;
            summary.TotalDowngraded += result.Stats.Downgraded;

            var parseError = Precheck(result.Text);
            if (parseError is not null)
            {
                summary.ParseFailures++;
            }

            summary.Results.Add(new FileMigrationResult(
                rel, targetPath, parseError is null, parseError,
                result.Stats.Actions, result.Stats.Expresssions,
                result.Stats.Unsupported, result.Stats.Downgraded,
                [.. result.Stats.Notes, .. result.Diagnostics]));
        }

        return summary;
    }

    /// <summary>转换单个模板文本</summary>
    internal (string Text, TemplateConversionStats Stats, IReadOnlyList<string> Diagnostics) ConvertTemplate(
        string relPath, string text)
    {
        var lexer = new GoTemplateLexer(text);
        var tokens = lexer.Tokenize();

        var parser = new GoTemplateParser(tokens);
        var parts = parser.Parse();

        var converter = new TemplateConverter(_map);
        var output = converter.Convert(parts);

        return (output, converter.Stats, [.. parser.Diagnostics, .. converter.Diagnostics]);
    }

    /// <summary>
    /// 模板级预检：Scriban 能否解析。返回错误描述（null = 通过）。
    /// 这是把"不可靠产出无法进入产物"落地的机制。
    /// </summary>
    internal static string? Precheck(string scribanText)
    {
        try
        {
            var template = Template.Parse(scribanText);
            if (!template.HasErrors)
            {
                return null;
            }
            var first = template.Messages.FirstOrDefault();
            return first is null
                ? "解析失败（无消息）"
                : $"行 {first.Span.Start.Line + 1}: {first.Message}";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }
}
