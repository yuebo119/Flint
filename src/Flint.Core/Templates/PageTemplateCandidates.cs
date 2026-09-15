// Flint 静态站点生成器
// 页面模板候选链——对齐 Hugo lookup order 的页面感知查找（A 组核心）。
//
// 与 TemplateLookup.Resolve 的区别（两者职责不同，不可互相替代）：
// - Resolve：纯名字匹配（partial / render hook / 视图等无页面上下文场景），
//   根序优先正确（站点 partial 覆盖主题 partial）。
// - 本文件的候选链：页面布局查找需要 kind/type/section/layout 维度，
//   候选级顺序即 specificity，级内站点优先，同根内根形态优先于 _default 形态。
//
// Hugo 语义（官方 lookup-order + templatedescriptor.go）：
// 1. distance（候选路径具体度）是第一判据，站点/主题根序是第二判据；
// 2. 站点与主题 layout 目录 interleave 查找（不是先穷尽站点再找主题）；
// 3. `_default/` 是兜底形态，与同名根形态同级（v0.146+ 由 TrimPrefix 归一）。

namespace Flint.Core.Templates;

/// <summary>
/// 候选级：一个逻辑模板名 + 是否允许 <c>_default/</c> 兜底形态。
/// 同根内先探根形态（<c>{root}/{name}.html</c>）后探兜底形态
/// （<c>{root}/_default/{name}.html</c>），对齐"根形态优先于 _default 形态"契约。
/// </summary>
public readonly record struct TemplateCandidate(string Name, bool AllowDefaultForm = true);

/// <summary>
/// 页面模板查询：页面感知候选链的输入（全部为页面自身特征，无外部状态）。
/// </summary>
public sealed record PageTemplateQuery
{
    /// <summary>页面 kind：home / page / section / taxonomy / term</summary>
    public required string Kind { get; init; }

    /// <summary>front matter <c>layout</c>（优先提示，非强制）</summary>
    public string? Layout { get; init; }

    /// <summary>
    /// front matter 显式声明的 type（未声明为 null）。不传 kind 名替代——
    /// Hugo 的 type 维度只认显式声明或根 section 名，见 <see cref="EffectiveType"/>
    /// </summary>
    public string? DeclaredType { get; init; }

    /// <summary>页面所属 section（一级路径段）</summary>
    public string? Section { get; init; }

    /// <summary>taxonomy 名（taxonomy/term 页专用，如 categories）</summary>
    public string? Taxonomy { get; init; }

    /// <summary>
    /// 输出格式名（html / rss / json）。非 html 时全部候选名带
    /// <c>.{format}</c> 后缀（物理文件 <c>{name}.{format}.html</c>）——
    /// 对齐 Hugo 输出格式变体模板，且不回退 html 主形态（格式互不替代）
    /// </summary>
    public string OutputFormat { get; init; } = "html";

    /// <summary>
    /// 生效 type：显式声明优先，其次根 section 名，最后 null（不产出 type 级候选）。
    /// 对齐 Hugo：type 总有值（默认 section 名），但根级页面的 section 为空，
    /// 此时不产出该级以免噪音候选
    /// </summary>
    public string? EffectiveType =>
        !string.IsNullOrEmpty(DeclaredType) ? DeclaredType
        : !string.IsNullOrEmpty(Section) ? Section
        : null;
}

/// <summary>
/// 页面模板候选链生成器（纯函数，无 IO；AOT 安全）。
/// 输出顺序即查找优先级：靠前 = 更具体。每级内由 <see cref="TemplateLookup.ResolveLayered"/>
/// 按"站点 → 主题"根序解析，同根内根形态优先于 _default 形态。
/// </summary>
public static class PageTemplateCandidates
{
    /// <summary>Hugo 的 catch-all 兜底层（v0.146+ <c>layouts/all.html</c>）</summary>
    private const string CatchAll = "all";

    /// <summary>
    /// 构造候选链。kind 未知时按 page 处理（宽容：不抛异常，退化为最泛链）。
    /// </summary>
    public static IReadOnlyList<TemplateCandidate> Build(PageTemplateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var levels = new List<TemplateCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 非 html 格式：候选名统一带 .{format} 后缀（html 主形态为 {name}.html）
        var suffix = IsHtmlFormat(query.OutputFormat)
            ? string.Empty
            : "." + query.OutputFormat.TrimStart('.').ToLowerInvariant();

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var normalized = name.Replace('\\', '/').Trim('/');
            if (normalized.Length == 0)
            {
                return;
            }

            normalized += suffix;
            if (!seen.Add(normalized))
            {
                return;
            }

            levels.Add(new TemplateCandidate(normalized));
        }

        switch (query.Kind.ToLowerInvariant())
        {
            case "home":
                BuildHome(query, Add);
                break;
            case "section":
                BuildSection(query, Add);
                break;
            case "taxonomy":
                BuildTaxonomy(query, Add);
                break;
            case "term":
                BuildTerm(query, Add);
                break;
            default:
                BuildPage(query, Add);
                break;
        }

        // 最泛兜底：Hugo 0.146+ 的 all.html（可渲染任意 kind 的 HTML 页面）
        Add(CatchAll);
        // Flint 兼容兜底：仅有 single.html 的极简站点历史上也能渲染 home/section
        //（旧逻辑 home→"index"?:"single"、section→"list"?:"single"）。
        // 置于 all 之后——Hugo 主题的 all.html 仍优先。
        // 仅 home/section：taxonomy/term 不得落到 single（旧路径靠内置
        // taxonomy/term/list 模板产出列表，加 single 会截胡内置兜底）
        var kind = query.Kind.ToLowerInvariant();
        if (kind is "home" or "section")
        {
            Add("single");
        }

        return levels;
    }

    private static bool IsHtmlFormat(string? format) =>
        string.IsNullOrEmpty(format) ||
        format.Equals("html", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 普通内容页（single）。Hugo 候选链：
    /// {type}/{layout} → {type}/single → {section}/{layout} → {section}/single
    /// → {layout} → single
    /// </summary>
    private static void BuildPage(PageTemplateQuery query, Action<string?> add)
    {
        var type = query.EffectiveType;
        var section = query.Section;

        add(Join(type, query.Layout));
        add(Join(type, "single"));
        // kind 等价名 `page`：Hugo v0.166 实测 layouts/page.html 可渲染普通页
        //（fixit 没有 _default/，普通页模板就是根级 page.html——缺这一级时
        //  /about/、/docs/getting-started/ 报 "模板未找到"，9 处）
        add(Join(type, "page"));
        // type 与 section 相同时以下两级会被去重（常见：content/posts/ 且无显式 type）
        add(Join(section, query.Layout));
        add(Join(section, "single"));
        add(Join(section, "page"));
        add(query.Layout);
        add("single");
        add("page");
    }

    /// <summary>
    /// 首页。Hugo 候选链：index → home → list
    /// （index 为旧式标准名，home 为 v0.146+ 标准名，list 为最终兜底）
    /// </summary>
    private static void BuildHome(PageTemplateQuery query, Action<string?> add)
    {
        var type = query.EffectiveType;
        add(Join(type, query.Layout));
        add(Join(type, "index"));
        add(Join(type, "home"));
        add(Join(type, "list"));
        add(query.Layout);
        add("index");
        add("home");
        add("list");
    }

    /// <summary>
    /// section 列表页。Hugo 候选链（type 目录 → section 目录 → section/ 字面目录）：
    /// {type}/{layout} → {type}/section → {type}/list
    /// → {section}/{layout} → {section}/section → {section}/list
    /// → section/{layout} → section/section → section/list
    /// → {layout} → list
    /// </summary>
    private static void BuildSection(PageTemplateQuery query, Action<string?> add)
    {
        var type = query.EffectiveType;
        var section = query.Section;

        add(Join(type, query.Layout));
        add(Join(type, "section"));
        add(Join(type, "list"));

        add(Join(section, query.Layout));
        add(Join(section, "section"));
        add(Join(section, "list"));

        add(Join("section", query.Layout));
        add(Join("section", "section"));
        add(Join("section", "list"));

        add(query.Layout);
        add("list");
        // **kind 名 `section.html`**（Hugo 0.146+ 的命名，与 page.html/home.html 同族）：
        // 主题只提供 `layouts/section.html` 时（FixIt 的 section 页由该模板渲染）候选链
        // 必须有它，否则整类页面回退内置列表模板（docs/posts 只剩 95/143 字节）。
        // 前置条件已满足：`and`/`or` 的**惰性**等价（分支惰性三元）由转换器保证——
        // even 的 `_default/section.html` 依赖 Go 的短路来跳过 nil 接收者的 `.Date`
        add("section");
    }

    /// <summary>
    /// taxonomy 列表页（词条索引）。Hugo 候选链：
    /// {taxonomy}/taxonomy → {taxonomy}/terms → {taxonomy}/list
    /// → taxonomy/taxonomy → taxonomy/terms → taxonomy/list
    /// → {layout} → taxonomy → terms → list
    ///
    /// 顺序依据（Hugo v0.166 实测，真实主题 ananke）：/tags/ 在同时存在
    /// taxonomy.html 与 terms.html 时渲染 **taxonomy.html**——0.146 起
    /// kind=taxonomy 的新名字是 taxonomy.html，terms.html 是旧名（优先级低）
    /// </summary>
    private static void BuildTaxonomy(PageTemplateQuery query, Action<string?> add)
    {
        var taxonomy = query.Taxonomy;

        add(Join(taxonomy, query.Layout));
        add(Join(taxonomy, "taxonomy"));
        add(Join(taxonomy, "terms"));
        add(Join(taxonomy, "list"));

        add(Join("taxonomy", query.Layout));
        add(Join("taxonomy", "taxonomy"));
        add(Join("taxonomy", "terms"));
        add(Join("taxonomy", "list"));

        add(query.Layout);
        add("taxonomy");
        add("terms");
        add("list");
    }

    /// <summary>
    /// term 词条页。Hugo 候选链：
    /// {taxonomy}/term → {taxonomy}/list → {taxonomy}/taxonomy
    /// → term/term → term/list → term/taxonomy
    /// → taxonomy/term → taxonomy/list → taxonomy/taxonomy
    /// → {layout} → term → list → taxonomy
    ///
    /// 顺序依据（Hugo v0.166 实测，真实主题 ananke）：/tags/<词条>/ 在同时存在
    /// taxonomy.html 与 list.html 时渲染 **list.html**——0.146 起 kind=term 的新名字
    /// 是 term.html，taxonomy.html 让位；主题依赖 list.html 的 .Paginator 才会分页，
    /// 选错模板会让词条页丢失列表与 /page/N/（实测 ananke 词条页内容为空）
    /// </summary>
    private static void BuildTerm(PageTemplateQuery query, Action<string?> add)
    {
        var taxonomy = query.Taxonomy;

        add(Join(taxonomy, query.Layout));
        add(Join(taxonomy, "term"));
        add(Join(taxonomy, "list"));
        add(Join(taxonomy, "taxonomy"));

        add(Join("term", query.Layout));
        add(Join("term", "term"));
        add(Join("term", "list"));
        add(Join("term", "taxonomy"));

        add(Join("taxonomy", query.Layout));
        add(Join("taxonomy", "term"));
        add(Join("taxonomy", "list"));
        add(Join("taxonomy", "taxonomy"));

        add(query.Layout);
        add("term");
        add("list");
        add("taxonomy");
    }

    /// <summary>目录与名字拼接；任一段为空返回 null（由调用方 Add 过滤）</summary>
    private static string? Join(string? prefix, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(prefix) ? name : prefix + "/" + name;
    }
}
