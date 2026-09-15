// Flint 静态站点生成器
// 页面模板候选链——对齐 Hugo lookup order 的页面感知查找（A 组核心）。
//
// 与 TemplateLookup.Resolve 的区别（两者职责不同，不可互相替代）：
// - Resolve：纯名字匹配（partial / render hook / 视图等无页面上下文场景），
//   根序优先正确（站点 partial 覆盖主题 partial）。
// - 本文件的候选链：页面布局查找需要 kind/type/section/layout 维度，
//   候选级顺序即 specificity，级内站点优先；**裸名**的两形态顺序为
//   `_default/{name}` → `{name}`（Hugo v0.166 实测，见 Build 内的 Add）
//
// Hugo 语义（官方 lookup-order + templatedescriptor.go）：
// 1. distance（候选路径具体度）是第一判据，站点/主题根序是第二判据；
// 2. 站点与主题 layout 目录 interleave 查找（不是先穷尽站点再找主题）；
// 3. 裸名的两个形态成对相邻，`_default/` 形态在前；带斜杠的名字没有该形态。

namespace Flint.Core.Templates;

/// <summary>
/// 候选级：一个逻辑模板名 + 是否允许 <c>_default/</c> 兜底形态。
/// 裸名会由 <see cref="PageTemplateCandidates.Build"/> 展开成
/// <c>_default/{name}</c>（在前）+ <c>{name}</c>（在后）两级，
/// 与 Hugo v0.166 的实测顺序一致；级内仍先探直接形态再探兜底形态
/// （对 <c>_default/{name}</c> 级而言，兜底形态 <c>_default/_default/{name}</c> 不存在，
/// 属无害空探）。
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
            // **裸名**的 `_default/` 形态优先于根形态（Hugo v0.166 逐对隔离实测：
            // `_default/section.html` > `section.html`、`_default/list.html` > `list.html`、
            // `_default/all.html` > `all.html`、`_default/home.html` > `home.html`/`index.html`、
            // `_default/foo.html`（layout: foo）> `foo.html`）。
            // 只在**两种形态同时存在**时才改变结果（同一个名字的两个形态原本相邻，
            // 把兜底形态提前一位不影响它与其它名字的相对顺序）——21 主题语料里
            // 没有任何裸名同时具备两种形态，故对现有主题零影响。
            // 带斜杠的名字（如 `posts/section`）没有 `_default` 形态，保持原样
            if (!normalized.Contains('/'))
            {
                AddLevel("_default/" + normalized);
            }

            AddLevel(normalized);
        }

        void AddLevel(string normalized)
        {
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
        // kind 等价名 `page`：Hugo v0.166 实测 `layouts/page.html` 可渲染普通页
        //（fixit 没有 `_default/`，普通页模板就是根级 page.html——缺这一级时
        //  /about/、/docs/getting-started/ 报 "模板未找到"，9 处），
        // 且 `page` **优先于** `single`（逐级淘汰实测：posts/page → posts/single →
        // _default/page → page → _default/single → single → _default/all → all）
        add(Join(type, "page"));
        add(Join(type, "single"));
        // type 与 section 相同时以下两级会被去重（常见：content/posts/ 且无显式 type）
        add(Join(section, query.Layout));
        add(Join(section, "page"));
        add(Join(section, "single"));
        // 裸名 "page"/"single" 各展开为 `_default/{name}` + `{name}`，顺序即实测所得
        add(query.Layout);
        add("page");
        add("single");
    }

    /// <summary>
    /// 首页。Hugo v0.166 逐级淘汰实测（八个候选逐个删胜出者）：
    /// <c>_default/home</c> → <c>_default/index</c> → <c>home</c> → <c>index</c>
    /// → <c>_default/list</c> → <c>list</c> → <c>_default/all</c> → <c>all</c>。
    /// 注意 `home` 在**每个形态内**都优先于 `index`（隔离对探：`home.html` 胜
    /// `index.html`、`_default/home.html` 胜 `_default/index.html`），
    /// 且 `_default/` 形态先于同名的根形态
    /// </summary>
    private static void BuildHome(PageTemplateQuery query, Action<string?> add)
    {
        var type = query.EffectiveType;
        // type 级候选只在显式声明 type 时产出：`Join(null, x)` 会退化成裸名 x，
        // 与下面的裸名级重复（首页通常无 section，type 为 null）
        if (!string.IsNullOrEmpty(type))
        {
            add(Join(type, query.Layout));
            add(Join(type, "home"));
            add(Join(type, "index"));
            add(Join(type, "list"));
        }
        add(query.Layout);
        // 显式 `_default/` 级：实测顺序里两形态**不成对相邻**（`_default/index` 在
        // 根 `home` 之前），故逐级写出，裸名 "home"/"index" 的兜底形态会被上面的
        // 显式级去重（`seen`）
        add("_default/home");
        add("_default/index");
        add("home");
        add("index");
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
        // **kind 名 `section.html`**（Hugo 0.146+ 的命名，与 page.html/home.html 同族）：
        // 主题只提供 `layouts/section.html` 时（FixIt 的 section 页由该模板渲染）候选链
        // 必须有它，否则整类页面回退内置列表模板（docs/posts 只剩 95/143 字节）。
        // 前置条件已满足：`and`/`or` 的**惰性**等价（分支惰性三元）由转换器保证——
        // even 的 `_default/section.html` 依赖 Go 的短路来跳过 nil 接收者的 `.Date`
        // 位置由 Hugo v0.166 逐级淘汰实测确定：`section.html`（含 `_default/` 形态）
        // 排在 `list.html` 之前——同样都是 `_default/section` → `section` →
        // `_default/list` → `list` → `_default/all` → `all`
        add("section");
        add("list");
    }

    /// <summary>
    /// taxonomy 列表页（词条索引，如 /tags/）。Hugo v0.166 逐级淘汰实测
    /// （十二个候选逐个删胜出者）：
    /// <c>{taxonomy}/terms</c> → <c>{taxonomy}/taxonomy</c> → <c>{taxonomy}/list</c>
    /// → <c>taxonomy/taxonomy</c> → <c>taxonomy/list</c>
    /// → <c>_default/terms</c> → <c>_default/taxonomy</c> → <c>taxonomy</c>（根）
    /// → <c>_default/list</c> → <c>list</c> → <c>_default/all</c> → <c>all</c>。
    /// 两点与直觉相反但实测确凿：
    /// 1. `terms` 在同形态内**优先于** `taxonomy`（旧名反而在前，隔离对探复现：
    ///    `_default/terms.html` 胜 `_default/taxonomy.html`——even 主题两文件俱全）；
    /// 2. 根级 `terms.html` **不是**候选（仅 `taxonomy.html` 是；hugo-coder/xmin
    ///    只有根级 terms.html，Hugo 对 /tags/ 报 "found no layout file for
    ///    kind taxonomy" 并回落到 `_default/list.html`）
    /// </summary>
    private static void BuildTaxonomy(PageTemplateQuery query, Action<string?> add)
    {
        var taxonomy = query.Taxonomy;

        add(Join(taxonomy, query.Layout));
        add(Join(taxonomy, "terms"));
        add(Join(taxonomy, "taxonomy"));
        add(Join(taxonomy, "list"));

        // 字面 `taxonomy/` 目录级：实测仅 taxonomy/taxonomy 与 taxonomy/list 两项，
        // 其余按同形态内 terms 在前的次序镜像（[推断]，未逐项淘汰）
        add(Join("taxonomy", query.Layout));
        add(Join("taxonomy", "terms"));
        add(Join("taxonomy", "taxonomy"));
        add(Join("taxonomy", "list"));

        add(query.Layout);
        add("_default/terms");
        add("_default/taxonomy");
        add("taxonomy");
        add("list");
    }

    /// <summary>
    /// term 词条页（如 /tags/词条/）。Hugo v0.166 逐级淘汰实测（十三个候选，
    /// 逐个删胜出者）：
    /// <c>{taxonomy}/term</c> → <c>{taxonomy}/list</c> → <c>term/term</c>
    /// → <c>term/list</c> → <c>taxonomy/term</c> → <c>_default/taxonomy</c>
    /// → <c>_default/term</c> → <c>term</c>（根） → <c>_default/list</c> → <c>list</c>
    /// → <c>_default/all</c> → <c>all</c>。
    /// 三处容易猜错的地方（均有探针）：
    /// 1. 字面 `term/` 目录级**排在** `taxonomy/` 字面目录级之前；
    /// 2. `_default/taxonomy` 先于 `_default/term`（kind 名让位：term 页仍可由
    ///    taxonomy.html 渲染）；
    /// 3. **不是**候选的：`{taxonomy}/terms`、`{taxonomy}/taxonomy`、
    ///    `taxonomy/list`、`_default/terms`、根级 `taxonomy.html`、根级 `terms.html`
    ///    （隔离对探：只放 `taxonomy.html` 时 Hugo 报 "no layout file for kind term"）
    /// </summary>
    private static void BuildTerm(PageTemplateQuery query, Action<string?> add)
    {
        var taxonomy = query.Taxonomy;

        add(Join(taxonomy, query.Layout));
        add(Join(taxonomy, "term"));
        add(Join(taxonomy, "list"));

        add(Join("term", query.Layout));
        add(Join("term", "term"));
        add(Join("term", "list"));

        add(Join("taxonomy", query.Layout));
        add(Join("taxonomy", "term"));

        add(query.Layout);
        add("_default/taxonomy");
        add("_default/term");
        add("term");
        add("list");
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
