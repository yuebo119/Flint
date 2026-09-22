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

    /// <summary>baseof 序幕 partial 的规范名（无扩展名）</summary>
    private const string BaseofProloguePath = "_partials/__baseof_prologue";

    /// <summary>二进制资产（位图/字体/压缩包等）：压缩数据里随机出现 <c>{{</c>
    /// 两字节序列是常态（blowfish 的 blowfish_logo.png 实测含 9 处），按模板
    /// 解析二进制数据会让 Go 词法/语法分析器空转不归（CPU 100% 假死）。
    /// 双判据：已知二进制扩展名 + 头部含 NUL 字节（文本资产两者都不中）</summary>
    private static readonly HashSet<string> BinaryAssetExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif", ".bmp", ".ico", ".tiff",
            ".woff", ".woff2", ".ttf", ".otf", ".eot",
            ".pdf", ".zip", ".gz", ".mp4", ".webm", ".mp3", ".ogg", ".wasm"
        };

    private static bool IsBinaryAsset(string filePath)
    {
        if (BinaryAssetExtensions.Contains(Path.GetExtension(filePath)))
        {
            return true;
        }
        // 内容兜底：无扩展名/扩展名不在表内的资产，读前 8KB 探 NUL 字节
        // （JS/CSS/JSON/SVG 等文本资产不会命中）。空文件 Read 返回 0 不抛——
        // ReadAtLeast 对 0 字节文件抛 EndOfStreamException（narrow 主题实测）
        using var stream = File.OpenRead(filePath);
        Span<byte> head = stackalloc byte[8192];
        var read = stream.Read(head);
        return read > 0 && head[..read].IndexOf((byte)0) >= 0;
    }

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
        // 跨文件 block（partial 里的 `{{ block "X" }}` 指向别的 partial 的 define）
        var crossFileBlocks = ScanCrossFileBlocks(sourceRoot);
        // baseof 序幕（纯副作用动作）：页面模板要在块体捕获**之前**先跑它
        var (baseofPrologueText, baseofPrologueParts) = ScanBaseofPrologue(sourceRoot);
        if (baseofPrologueText is not null)
        {
            summary.GlobalDiagnostics.Add("baseof 序幕：已提到块体之前（纯副作用动作）");
        }
        if (crossFileBlocks.Count > 0)
        {
            summary.GlobalDiagnostics.Add($"跨文件命名模板 block: {crossFileBlocks.Count} 个");
        }
        // 槽位命名模板（多文件同名 define）：hugo-book 类主题的 baseof 定义默认体
        // 并用 `{{ template "main" . }}` 调用，各页面模板用同名 define **覆盖**。
        // 它们不能按名字提取到同一个 partial（会互相覆盖，实测 posts/list.html 的
        // 分页列表体被 book.html 的正文体顶掉、全部页面渲染同一个 main），
        // 故从提取集合剔除，改走"就地 capture + 调用点条件输出"的槽位机制
        var slotNames = ScanMultiDefinedNames(sourceRoot);
        namedTemplates.ExceptWith(slotNames);
        summary.GlobalDiagnostics.Add($"命名模板（define + template 调用）: {namedTemplates.Count} 个");
        if (slotNames.Count > 0)
        {
            summary.GlobalDiagnostics.Add($"槽位命名模板（多文件同名 define）: {slotNames.Count} 个");
        }

        // baseof 序幕 partial 落盘（页面模板会在块体捕获之前 include 它）
        if (baseofPrologueText is not null)
        {
            var prologueRel = "layouts/" + BaseofProloguePath + ".html";
            var prologueTarget = Path.Combine(
                targetRoot, prologueRel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(prologueTarget)!);
            var prologueConverted = ConvertTemplate(prologueRel, baseofPrologueText);
            File.WriteAllText(prologueTarget, prologueConverted.Text);
            summary.FilesConverted++;
        }

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
                // 资产文件含 Go 模板动作时按模板转换（`resources.ExecuteAsTemplate`
                // 引用的资产——narrow 的 theme-init.js、hugo-book 的搜索配置实测）：
                // **解析干净且有动作**才落转换结果；JS 里的 `{{` 可能是对象字面量/
                // 字符串（解析必然报错），误转比不转更糟 → 落到下方原样复制
                var normalizedAssetRel = rel.Replace((char)92, '/');
                if (normalizedAssetRel.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) &&
                    !IsBinaryAsset(file))
                {
                    var assetText = File.ReadAllText(file);
                    if (assetText.Contains("{{", StringComparison.Ordinal))
                    {
                    try
                    {
                        var assetResult = ConvertTemplate(normalizedAssetRel, assetText);
                        if (assetResult.Diagnostics.Count == 0 && assetResult.Stats.Actions > 0)
                        {
                            File.WriteAllText(targetPath, assetResult.Text);
                            summary.FilesConverted++;
                            continue;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // 转换异常 → 原样复制（资产转换是**尽力而为**：JS 里的
                        // `{{` 形态千奇百怪，单个文件的边角缺陷不能让迁移整体失败
                        // ——迁移中途崩溃会让全部后续文件停在未转换状态，整站构建失败）
                        summary.GlobalDiagnostics.Add(
                            $"资产 {normalizedAssetRel} 转换异常，已按原文保留: {ex.Message}");
                    }
                    }
                }

                // 非模板：原样复制（static/assets 等）
                File.Copy(file, targetPath, overwrite: true);
                summary.FilesCopied++;
                continue;
            }

            var text = File.ReadAllText(file);

            // **空白模板按原文保留**：Hugo 不把 0 字节文件当模板（v0.166 实测：
            // `layouts/404.html` 为空文件时 Hugo 不产出 404.html，而白空格/仅注释的
            // 模板仍会产出）。若照常转换，转换器的"内容为空 → 补 include baseof"
            // 规则会把它变成一个**真模板**（实测 monochrome 的 0 字节 404.html 被写成
            // `{{ include "baseof.html" }}` → Flint 多产出 404.html、门禁④报不对称）
            if (string.IsNullOrWhiteSpace(text))
            {
                File.WriteAllText(targetPath, text);
                summary.FilesCopied++;
                continue;
            }

            // 内联 partial 提取（Hugo 的 define "_partials/X.html"）：
            // Scriban 无此机制，必须提取为独立文件使 include 可命中
            var (remainingText, inlinePartials) = InlinePartialExtractor.Extract(text, namedTemplates, rel);
            var selfPartial = SelfPartialNameOf(rel);
            // 主题范围是否存在 baseof 骨架（Hugo 的模板继承外壳）：
            // 仅 define 触发继承的模板（hugo-book 的 single.html）转换后为空，
            // 需要补 include baseof，否则整站空页
            var layoutsRoot = Path.Combine(sourceRoot, "layouts");
            var baseofAvailable = File.Exists(Path.Combine(layoutsRoot, "baseof.html")) &&
                !rel.Replace((char)92, '/').Contains("partials/", StringComparison.OrdinalIgnoreCase);
            // 本文件内同名命名模板是否被提取改名（决定 partial 自调用是否要加后缀）：
            // 只有提取器真的改名过才加——否则会把"partial 递归调用自己"改成不存在的文件
            var selfNamedExtracted = inlinePartials.Any(ip =>
                ip.RelativePath.Contains(
                    InlinePartialExtractor.NamedTemplateSelfSuffix, StringComparison.Ordinal));
            // rel 相对**主题根**（如 "layouts/baseof.html"），故两种形态都认
            var normalizedRel = rel.Replace((char)92, '/');
            var isBaseTemplate =
                normalizedRel.Equals("baseof.html", StringComparison.OrdinalIgnoreCase) ||
                normalizedRel.Equals("layouts/baseof.html", StringComparison.OrdinalIgnoreCase);
            // 跨文件 block：本文件定义了别的 partial 里 `block` 引用的命名模板 →
            // 把块体提取为 `_partials/<名>__block.html`（body 走同一转换），
            // 供 block 调用点渲染。定义文件自身照旧转换（不含该 define）
            foreach (var (blockName, defFile) in crossFileBlocks)
            {
                if (!string.Equals(
                        defFile,
                        rel.Replace((char)92, '/'),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var (strippedText, extracted) = InlinePartialExtractor.Extract(
                    remainingText, new HashSet<string>(StringComparer.Ordinal) { blockName }, rel);
                if (extracted.Count == 0)
                {
                    continue;
                }

                remainingText = strippedText;
                foreach (var ex in extracted)
                {
                    // 文件名与调用点必须用**同一套标识符净化**：块名常带连字符
                    // （fixit 的 `block "custom-assets"`），调用点经 SanitizeIdent 会变成
                    // `custom_assets` → 两侧不一致时报 "partial 未找到"
                    var blockRel =
                        $"layouts/_partials/{TemplateConverter.SanitizeIdent(blockName)}__block.html";
                    var blockTarget = Path.Combine(
                        targetRoot, blockRel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(blockTarget)!);
                    var converted = ConvertTemplate(
                        blockRel, ex.Content, null, null, false, false, null, false);
                    File.WriteAllText(blockTarget, converted.Text);
                    summary.FilesConverted++;
                }
            }

            var result = ConvertTemplate(
                rel, remainingText, valueReturning, selfPartial, baseofAvailable, selfNamedExtracted,
                slotNames, isBaseTemplate, crossFileBlocks.Keys,
                baseofPrologueText is null ? null : BaseofProloguePath,
                isBaseTemplate ? baseofPrologueParts : 0);
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

                // 提取内容仍走转换（保持与其他模板一致的语法）。
                // **自名标志要一并传下去**：提取出的 `<name>__named.html` 就是原文件里
                // `{{ define "<name>" }}` 的落点，其体内的**递归调用**
                // `{{ template "<name>" … }}` 必须解析到 `__named` 文件本身——漏传该标志时
                // 会解析回外层包装文件，形成 wrapper↔named 互相调用
                //（techdoc 的 pagination.html 实测："partial 嵌套深度超过 200"）
                var ipSelfPartial = ip.RelativePath.Contains(
                    InlinePartialExtractor.NamedTemplateSelfSuffix, StringComparison.Ordinal)
                    ? Path.GetFileName(ip.RelativePath)
                        .Replace(InlinePartialExtractor.NamedTemplateSelfSuffix, "", StringComparison.Ordinal)
                    : null;
                var ipConverted = ConvertTemplate(
                    ipRel, ip.Content,
                    selfPartialName: ipSelfPartial,
                    selfNamedTemplateExtracted: ipSelfPartial is not null);
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
        string? selfPartialName = null,
        bool baseofAvailable = false,
        bool selfNamedTemplateExtracted = false,
        IReadOnlySet<string>? slotNames = null,
        bool isBaseTemplate = false,
        IEnumerable<string>? crossFileBlocks = null,
        string? baseofProloguePath = null,
        int baseofProloguePartCount = 0)
    {
        var lexer = new GoTemplateLexer(text);
        var tokens = lexer.Tokenize();

        var parser = new GoTemplateParser(tokens);
        var parts = parser.Parse();

        var converter = new TemplateConverter(
            _map, valueReturning, selfPartialName, baseofAvailable, selfNamedTemplateExtracted,
            slotNames, isBaseTemplate, crossFileBlocks, baseofProloguePath, baseofProloguePartCount);
        var output = converter.Convert(parts);

        return (output, converter.Stats, [.. parser.Diagnostics, .. converter.Diagnostics]);
    }

    /// <summary>
    /// 扫描含 {{ return }} 的 partial（返回任意类型者需走 partialValue 机制）。
    /// 返回规范化名集合（与 ScribanConverter.CanonicalPartialName 同规则）
    /// </summary>
    /// <summary>
    /// Hugo **内置**（embedded）返回值型 partial：主题文件里没有它们，但调用点同样
    /// 需要走 partialValue 通道才能拿到对象（Hugo 的 `_funcs/get-page-images` 返回
    /// 页面图片切片，FixIt 的 twitter-cards 用 `index $images 0` 取首图）
    /// </summary>
    private static readonly string[] BuiltinValueReturningPartials =
    [
        "_funcs/get-page-images"
    ];

    internal static HashSet<string> ScanValueReturningPartials(string sourceRoot)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var builtin in BuiltinValueReturningPartials)
        {
            result.Add(builtin);
        }
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
    /// <summary>
    /// 扫描**多文件同名**的简单名 define（"槽位"命名模板）：
    /// hugo-book 类主题的 baseof 定义 <c>main/toc/footer</c> 等默认体并用
    /// <c>{{ template "main" . }}</c> 调用，各页面模板以同名 define 覆盖。
    /// 这类名字不能提取成单一 partial（互相覆盖），调用点也需按"覆盖优先、
    /// 默认兜底"的条件输出处理。
    /// </summary>
    internal static HashSet<string> ScanMultiDefinedNames(string sourceRoot)
    {
        var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
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
                var name = m.Groups[1].Value;
                if (name.Contains('/', StringComparison.Ordinal))
                {
                    continue;
                }
                if (!owners.TryGetValue(name, out var set))
                {
                    owners[name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                set.Add(Path.GetFullPath(file));
            }
        }

        return owners.Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// **跨文件命名模板的 block**：`partials/` 下的文件里写 `{{ block "X" . }}`，而 X 的
    /// <c>{{ define }}</c> 在**另一个** partial 文件里。Hugo 的命名模板是全局的
    /// （`block` 会渲染任何文件 define 的 X），而转换器的槽位机制只覆盖
    /// "baseof 声明 + 页面模板覆盖"的同名形态 → 这类 block 落到空兜底
    /// （github-style 的 user-profile.html：`block "posts"` 指向 partials/posts.html
    /// 的 define，此前整段文章列表渲染为空）。返回 名字 → 定义文件（相对主题根）
    /// </summary>
    internal static Dictionary<string, string> ScanCrossFileBlocks(string sourceRoot)
    {
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        var blocksInPartials = new Dictionary<string, string>(StringComparer.Ordinal);
        var localDefines = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.html", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceRoot, file).Replace((char)92, '/');
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            var local = localDefines[rel] = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    text, @"\{\{-?\s*define\s+""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                var n = m.Groups[1].Value;
                if (n.Contains('/', StringComparison.Ordinal))
                {
                    continue;
                }

                local.Add(n);
                defines[n] = rel;
            }

            // block 只在 **partials/ 下的文件**里才按跨文件命名模板处理：
            // baseof 里的 block 是"声明 + 覆盖"槽位形态（同名 define 在页面模板里，
            // 由 include 的命名参数传递），走既有机制
            if (!rel.Contains("partials/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    text, @"\{\{-?\s*block\s+""([^""]+)""",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            {
                blocksInPartials[m.Groups[1].Value] = rel;
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, blockFile) in blocksInPartials)
        {
            // 本文件自己也 define 了同名模板（同文件约定）→ 走既有就地 capture 路径
            if (localDefines.TryGetValue(blockFile, out var local) && local.Contains(name))
            {
                continue;
            }

            if (defines.TryGetValue(name, out var defFile) && defFile != blockFile)
            {
                result[name] = defFile;
            }
        }

        return result;
    }

    /// <summary>
    /// **baseof 序幕**：baseof.html 顶部连续的"纯副作用动作"（partial 调用 / store 写入等，
    /// **不定义也不引用局部变量**）。Hugo 的块体在 baseof 之内求值——序幕先跑、块体随后读
    /// store；而转换把块体捕获提到 include 之前，块体读 store 就是空值
    /// （fixit 的 home.html 读 `.Site.Store.Get "mainSectionPages"` → 首页文章列表整段不渲染）。
    /// 返回（序幕文本, 覆盖的 part 数）；无可用序幕时返回 (null, 0)
    /// </summary>
    internal static (string? Text, int PartCount) ScanBaseofPrologue(string sourceRoot)
    {
        foreach (var relCandidate in new[] { "layouts/baseof.html", "baseof.html" })
        {
            var path = Path.Combine(sourceRoot, relCandidate.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (IOException)
            {
                return (null, 0);
            }

            IReadOnlyList<TemplatePart> parts;
            try
            {
                parts = new GoTemplateParser(new GoTemplateLexer(text).Tokenize()).Parse();
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                return (null, 0);
            }

            var prologue = new List<TemplatePart>();
            foreach (var part in parts)
            {
                // 只吸收**动作**：夹在中间的空文本放过，遇到非空文本或注释即停
                if (part is ActionPart action)
                {
                    if (ReferencesVariables(action))
                    {
                        return (null, 0);
                    }

                    prologue.Add(action);
                    continue;
                }

                if (part is TextPart { Text: var txt } && string.IsNullOrWhiteSpace(txt))
                {
                    continue;
                }

                break;
            }

            if (prologue.Count == 0)
            {
                return (null, 0);
            }

            // 序幕在 parts 里的**前导长度**（含跳过的空白文本）用于在原文件里跳过
            var consumed = 0;
            for (var i = 0; i < parts.Count; i++)
            {
                if (i < prologue.Count)
                {
                    consumed = i + 1;
                    continue;
                }

                if (parts[i] is TextPart { Text: var gap } && string.IsNullOrWhiteSpace(gap))
                {
                    consumed = i + 1;
                    continue;
                }

                break;
            }

            return (string.Concat(prologue.Select(SerializePart)), consumed);
        }

        return (null, 0);
    }

    /// <summary>动作里是否出现局部变量（<c>$x</c> / <c>$.x</c> 除外——<c>$</c> 是页面根）</summary>
    private static bool ReferencesVariables(TemplatePart part)
    {
        var text = SerializePart(part);
        for (var i = 0; i < text.Length - 1; i++)
        {
            if (text[i] != '$')
            {
                continue;
            }

            var next = text[i + 1];
            // `$.` 是 Hugo 的页面根（转换器映射为 page），不算局部变量
            if (next is '.' or ' ' or '{' or '}')
            {
                continue;
            }

            if (char.IsLetter(next) || next == '_')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>TemplatePart → 源文本（序幕重排用）</summary>
    private static string SerializePart(TemplatePart part) => part switch
    {
        TextPart t => t.Text,
        ActionPart a => "{{" + a.Raw + "}}",
        _ => ""
    };

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
