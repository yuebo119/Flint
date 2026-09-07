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

    // 描述符集缓存：渲染热路径每页 Resolve 都进来，全树枚举必须复用。
    // 作用域为单次构建（构建入口经渲染器 Invalidate 同步失效），构建内
    // 模板文件集不变是安全假设
    private IReadOnlyList<TemplateDescriptor>? _descriptorCache;

    /// <summary>清空描述符缓存（构建边界由渲染器调用）</summary>
    public void Invalidate() => _descriptorCache = null;

    private IReadOnlyList<TemplateDescriptor> ScanAll()
    {
        var list = new List<TemplateDescriptor>();
        for (var i = 0; i < _roots.Length; i++)
        {
            if (!Directory.Exists(_roots[i]))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(_roots[i], "*.html", SearchOption.AllDirectories))
            {
                list.Add(Describe(
                    Path.GetRelativePath(_roots[i], file).Replace('\\', '/'),
                    file, i));
            }
        }
        return list;
    }

    /// <summary>
    /// 加权查找：返回最优匹配的物理路径；无匹配返回 null。
    /// requestName 支持 "{kind/layout 名}" 与 "{名}.{输出格式}"（如 single.json）两种形态
    /// </summary>
    public string? Resolve(string requestName, string rootPath)
    {
        var (baseName, outputFormat) = SplitRequest(requestName);
        var descriptors = _descriptorCache ??= ScanAll();
        TemplateDescriptor? best = null;
        var bestScore = -1;

        foreach (var descriptor in descriptors)
        {
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

        // 直查仅对无格式请求生效：发现描述符缓存快照之后新增的模板文件
        //（渲染器单飞场景无构建边界失效钩子）。带格式请求（"single.json"）
        // 禁止直查——ChangeExtension 会把 ".json" 替换为 ".html"，让 html
        // 模板冒充 json 变体（PageOutputFormats 集成测试实证）
        if (best is null && outputFormat is null)
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
        }

        return best?.PhysicalPath;
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
        // 名字是匹配的必要证据：全无关联的描述符（根序保底分曾使其竞选成功、
        // 返回第一个被枚举的文件）不得参与——名字不沾边直接判负
        var nameMatched =
            descriptor.MatchName.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(descriptor.MatchName).Equals(baseName, StringComparison.OrdinalIgnoreCase);
        if (!nameMatched)
        {
            return -1;
        }

        // 格式硬约束：请求带输出格式时描述符格式必须一致，请求无格式（html
        // 主路径）时带格式声明的变体不参与——否则 html 模板会冒充 json 变体
        // （无变体模板时 json 输出错误产出，PageOutputFormats 集成测试实证）
        if (outputFormat is not null
                ? !string.Equals(descriptor.OutputFormat, outputFormat, StringComparison.OrdinalIgnoreCase)
                : descriptor.OutputFormat is not null)
        {
            Console.Error.WriteLine($"[SCORE-FMT-REJECT] desc={descriptor.RelativePath} fmt={outputFormat}");
            return -1;
        }

        var score = 0;
        // 逻辑名精确（含目录路径）：+10；请求侧只给短名（"single"）时退而按
        // 文件名段匹配：+6（低于全串精确，保证请求带 "_default/" 前缀时
        // _default 形态仍胜过根形态）
        if (descriptor.MatchName.Equals(baseName, StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }
        else
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
