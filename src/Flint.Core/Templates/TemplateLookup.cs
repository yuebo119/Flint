// Flint 静态站点生成器
// TemplateLookup——模板加权匹配查找器（对齐 Hugo 0.146+ 描述符加权模型的简化版）。
// 与 ScribanTemplateRenderer.ResolveTemplatePath 的文件名约定查找并存，
// 作为方案五特性开关的查找后端；一致性由 TemplateLookupSnapshotTests 矩阵守护。

using System.Security.Cryptography;
using System.Text;

namespace Flint.Core.Templates;

/// <summary>
/// 模板描述符：一个物理模板文件的匹配能力描述
/// （kind 匹配器 / 输出格式 / 所在根序 / 相对路径）
/// </summary>
public sealed record TemplateDescriptor(
    string LogicalName,
    string PhysicalPath,
    string? Kind,
    string? OutputFormat,
    int RootOrder,
    string RelativePath,
    string MatchName);

/// <summary>
/// 模板加权查找器：扫描全部根目录（站点优先、主题按序）的模板文件，
/// 按 (请求名, 输出格式) 解析为描述符，加权取最优。
///
/// 加权规则（分高者胜；对齐 Hugo 的相对量级）：
/// - 逻辑名精确匹配：+10
/// - 输出格式精确匹配：+4
/// - 根序越前越高（站点根 100，主题依次递减）
/// - 同分并列：站点根优先 → 路径字典序
///
/// 物理文件布局约定（与 ResolveTemplatePath 一致）：{root}/{name}[.html]、
/// {root}/_default/{name}[.html]；{name}.{format}.html 视为输出格式变体。
/// </summary>
public sealed class TemplateLookup
{
    private readonly string[] _roots;

    public TemplateLookup(string siteTemplatesPath, params string[] themeTemplatePaths)
    {
        _roots = new[] { siteTemplatesPath }
            .Concat(themeTemplatePaths.Where(p => !string.IsNullOrEmpty(p)))
            .ToArray();
    }

    /// <summary>
    /// 加权查找：返回最优匹配的物理路径；无匹配返回 null。
    /// requestName 支持 "{kind/layout 名}" 与 "{名}.{输出格式}"（如 single.json）两种形态
    /// </summary>
    public string? Resolve(string requestName, string rootPath)
    {
        var (baseName, outputFormat) = SplitRequest(requestName);
        TemplateDescriptor? best = null;
        var bestScore = -1;

        foreach (var root in _roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var descriptor = Describe(relative, file, Array.IndexOf(_roots, root));
                var score = Score(descriptor, baseName, outputFormat);
                // 同分并列兑现文档契约：路径字典序（同分只发生在同根内——
                // 根序分按根递减 10，跨根必不同分）
                if (score > bestScore ||
                    (score == bestScore && best is not null &&
                     string.CompareOrdinal(descriptor.RelativePath, best.RelativePath) < 0))
                {
                    bestScore = score;
                    best = descriptor;
                }
            }
        }

        // 请求名指向站点根之外的文件（rootPath 形态）时不参与加权——保持直查语义
        if (best is null)
        {
            var direct = Path.Combine(rootPath, requestName);
            if (File.Exists(direct))
            {
                return direct;
            }
            var directHtml = Path.ChangeExtension(direct, ".html");
            if (File.Exists(directHtml))
            {
                return directHtml;
            }
            return null;
        }

        return best.PhysicalPath;
    }

    /// <summary>枚举全部描述符（诊断/对比用）</summary>
    public IReadOnlyList<TemplateDescriptor> DescribeAll()
    {
        var list = new List<TemplateDescriptor>();
        for (var i = 0; i < _roots.Length; i++)
        {
            var root = _roots[i];
            if (!Directory.Exists(root))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories))
            {
                list.Add(Describe(Path.GetRelativePath(root, file).Replace('\\', '/'), file, i));
            }
        }
        return list;
    }

    /// <summary>
    /// "{name}.{format}.html" → (baseName="name", format)；"name.html"/"name" → (name, null)。
    /// 只切最后一个双段（a/b/single.json.html → base=a/b/single、format=json）
    /// </summary>
    internal static (string BaseName, string? OutputFormat) SplitRequest(string requestName)
    {
        var normalized = requestName.Replace('\\', '/');
        // 剥 .html 扩展名
        if (normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^5];
        }
        var lastDot = normalized.LastIndexOf('.');
        return lastDot > 0
            ? (normalized[..lastDot], normalized[(lastDot + 1)..])
            : (normalized, null);
    }

    private static TemplateDescriptor Describe(string relative, string physicalPath, int rootOrder)
    {
        // 相对路径段：layouts/_default/single.html → _default/single.html（Flint 约定中
        // 构造参数即 layouts 目录，故段 0 就是 kind/输出格式载体）
        var normalized = relative.Replace('\\', '/');
        var kind = InferKind(normalized);
        var outputFormat = InferOutputFormat(normalized);
        return new TemplateDescriptor(normalized, physicalPath, kind, outputFormat, rootOrder,
            normalized, BuildMatchName(normalized));
    }

    /// <summary>
    /// "_default/single.json.html" → "_default/single"：剥 .html 扩展名与格式段
    /// （首段切割，与 InferKind 同法）、保留目录——Score 逻辑名精确匹配的基准，
    /// 与请求侧 SplitRequest 产出的 baseName 同构（LogicalName 恒带 .html，
    /// 直接比较恒 false，会使精确分永不触发）
    /// </summary>
    private static string BuildMatchName(string normalized)
    {
        var noHtml = normalized.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^5]
            : normalized;
        var fileName = Path.GetFileName(noHtml);
        var dot = fileName.IndexOf('.', StringComparison.Ordinal);
        var kindPart = dot > 0 ? fileName[..dot] : fileName;
        var dir = Path.GetDirectoryName(noHtml);
        return string.IsNullOrEmpty(dir) ? kindPart : dir.Replace('\\', '/') + "/" + kindPart;
    }

    /// <summary>文件名首段推断 kind（single/list/index/taxonomy/term，未知为 null）</summary>
    private static string? InferKind(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var dot = fileName.IndexOf('.', StringComparison.Ordinal);
        var baseName = dot > 0 ? fileName[..dot] : fileName;
        return baseName.ToLowerInvariant() switch
        {
            "single" => "page",
            "list" => "section",
            "index" => "home",
            "taxonomy" => "taxonomy",
            "term" => "term",
            _ => null
        };
    }

    private static string? InferOutputFormat(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var dot = fileName.LastIndexOf('.');
        return dot > 0 ? fileName[(dot + 1)..].ToLowerInvariant() : null;
    }

    private static int Score(TemplateDescriptor descriptor, string baseName, string? outputFormat)
    {
        var score = 0;
        // 逻辑名精确（含目录路径）：+10；请求侧只给短名（"single"）时退而按
        // 文件名段匹配：+6（低于全串精确，保证请求带 "_default/" 前缀时
        // _default 形态仍胜过根形态）
        if (descriptor.MatchName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }
        else if (Path.GetFileName(descriptor.MatchName)
            .Equals(baseName, StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }
        // _default/ 是兜底形态（对齐 ResolveTemplatePath 契约：根形态 > _default 形态），
        // 兜底目录扣分，避免与根形态同分后由枚举顺序决定胜者
        if (descriptor.MatchName.StartsWith("_default/", StringComparison.OrdinalIgnoreCase))
        {
            score -= 2;
        }
        // 输出格式精确：+4（无格式声明的模板视为 html）
        if (string.Equals(descriptor.OutputFormat, outputFormat, StringComparison.OrdinalIgnoreCase) ||
            (outputFormat is null && descriptor.OutputFormat is null))
        {
            score += 4;
        }
        // 根序：站点根（0）最高，主题递减
        score += Math.Max(0, 100 - descriptor.RootOrder * 10);
        return score;
    }
}
