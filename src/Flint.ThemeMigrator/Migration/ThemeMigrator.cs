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
    // .svg 也算模板：主题常把含 `{{ .width }}` 之类占位符的 SVG 放进 _partials 供
    // partial 调用（Hugo 会渲染它）。不入表则被整体复制、Hugo 语法残留，
    // 运行时 Scriban 解析报 "Unexpected token ."（blog-awesome 63 处实测）
    private static readonly HashSet<string> TemplateExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".html", ".xml", ".json", ".txt", ".gotmpl", ".svg" };

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

        // 第一遍：扫描含 {{ return }} 的 partial（Hugo 返回值语义）——
        // 调用点需改用 partialValue（Scriban 的 include 只能文本化），
        // 而该判定是跨文件的，故必须先全局扫描
        var valueReturning = ScanValueReturningPartials(sourceRoot);
        summary.GlobalDiagnostics.Add($"返回值型 partial: {valueReturning.Count} 个");
        var namedTemplates = ScanNamedTemplates(sourceRoot);
        summary.GlobalDiagnostics.Add($"命名模板（define + template 调用）: {namedTemplates.Count} 个");

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

            // 内联 partial 提取（Hugo 的 define "_partials/X.html"）：
            // Scriban 无此机制，必须提取为独立文件使 include 可命中
            var (remainingText, inlinePartials) = InlinePartialExtractor.Extract(text, namedTemplates);
            var selfPartial = SelfPartialNameOf(rel);
            var result = ConvertTemplate(rel, remainingText, valueReturning, selfPartial);
            File.WriteAllText(targetPath, result.Text);

            // 提取的内联 partial 作为独立模板文件写出（路径相对主题 layouts/）
            foreach (var ip in inlinePartials)
            {
                // Hugo 的虚拟路径是相对 layouts 的（_partials/X.html），
                // 须落到产物主题的 layouts/ 下（与源码目录结构一致）
                var ipRel = ip.RelativePath.StartsWith("layouts/", StringComparison.OrdinalIgnoreCase)
                    ? ip.RelativePath
                    : "layouts/" + ip.RelativePath;
                var ipTarget = Path.Combine(targetRoot, ipRel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(ipTarget)!);

                // 提取内容仍走转换（保持与其他模板一致的语法）
                var ipConverted = ConvertTemplate(ipRel, ip.Content);
                File.WriteAllText(ipTarget, ipConverted.Text);
                summary.FilesConverted++;
                summary.TotalActions += ipConverted.Stats.Actions;
                summary.TotalExpressions += ipConverted.Stats.Expresssions;
                summary.TotalUnsupported += ipConverted.Stats.Unsupported;
                summary.TotalDowngraded += ipConverted.Stats.Downgraded;

                summary.Results.Add(new FileMigrationResult(
                    ipRel, ipTarget, true, null,
                    ipConverted.Stats.Actions, ipConverted.Stats.Expresssions,
                    ipConverted.Stats.Unsupported, ipConverted.Stats.Downgraded,
                    [$"内联 partial 提取为独立文件（原 #{rel}）", .. ipConverted.Stats.Notes]));
            }

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
        string relPath,
        string text,
        IReadOnlySet<string>? valueReturning = null,
        string? selfPartialName = null)
    {
        var lexer = new GoTemplateLexer(text);
        var tokens = lexer.Tokenize();

        var parser = new GoTemplateParser(tokens);
        var parts = parser.Parse();

        var converter = new TemplateConverter(_map, valueReturning, selfPartialName);
        var output = converter.Convert(parts);

        return (output, converter.Stats, [.. parser.Diagnostics, .. converter.Diagnostics]);
    }

    /// <summary>
    /// 扫描含 {{ return }} 的 partial（返回任意类型者需走 partialValue 机制）。
    /// 返回规范化名集合（与 ScribanConverter.CanonicalPartialName 同规则）
    /// </summary>
    internal static HashSet<string> ScanValueReturningPartials(string sourceRoot)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.html", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceRoot, file).Replace((char)92, '/');
            // 仅 layouts/ 下的模板可能是 partial
            if (!rel.Contains("partials/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var text = File.ReadAllText(file);
                // 检测 partial 级的 return（{{ return ... }} 或 {{- return ... -}}）
                if (System.Text.RegularExpressions.Regex.IsMatch(
                        text, @"\{\{-?\s*return\b",
                        System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                {
                    result.Add(ScribanConverter.CanonicalPartialName(rel));
                }
            }
            catch (IOException)
            {
                // 跳过不可读文件
            }
        }

        return result;
    }

    /// <summary>
    /// 扫描**命名模板**：Hugo 的 <c>{{ define "X" }}</c> 与 <c>{{ block "X" }}</c> 共用
    /// define 语法，但语义分两类：
    ///   1. baseof 继承：<c>block "X"</c> 声明 + 子模板 <c>define "X"</c> 覆盖 → 走 capture blk_X
    ///   2. 命名模板库：<c>define "X"</c>（无同名 block）+ 任意处的
    ///      <c>template "X" ctx</c> 调用 → 必须提为独立 partial 文件（Scriban 无此机制）
    /// 本方法返回第 2 类的名字集合（20/20 流行主题都使用该机制，hugo-book 55 处）
    /// </summary>
    internal static HashSet<string> ScanNamedTemplates(string sourceRoot)
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.html", SearchOption.AllDirectories))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    text, @"\{\{-?\s*define\s+""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                var n = m.Groups[1].Value;
                if (!n.Contains('/', StringComparison.Ordinal))
                {
                    defined.Add(n);
                }
            }

            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    text, @"\{\{-?\s*block\s+""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                blocked.Add(m.Groups[1].Value);
            }
        }

        // 命名模板 = 有 define 但无同名 block 声明
        defined.ExceptWith(blocked);
        return defined;
    }

    /// <summary>文件相对路径 → partial 规范名（非 partial 返回 null）</summary>
    internal static string? SelfPartialNameOf(string relPath)
    {
        var rel = relPath.Replace((char)92, '/');
        if (!rel.Contains("partials/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return ScribanConverter.CanonicalPartialName(rel);
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
