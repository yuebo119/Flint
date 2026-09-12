// Flint 静态站点生成器
// ScribanTemplateRenderer 的对象构建部分：page/site/menus ScriptObject 工厂、
// 惰加载包装器与日期对象注册（从 ScribanTemplateRenderer.cs 按 partial 拆出）

using System.Runtime.CompilerServices;
using Scriban.Runtime;
using Scriban.Parsing;
using Flint.Core.Abstractions;
using FlintMenuCollection = Flint.Core.Abstractions.MenuCollection;
using FlintMenuItem = Flint.Core.Abstractions.MenuItem;
using FlintPageContext = Flint.Core.Abstractions.PageContext;
using FlintSiteContext = Flint.Core.Abstractions.SiteContext;
using FlintTaxonomyCollection = Flint.Core.Abstractions.TaxonomyCollection;

namespace Flint.Core.Templates;

/// <summary>
/// 基于 Scriban 的模板渲染器实现——对象构建部分
/// （主文件：ScribanTemplateRenderer.cs，含编译缓存/渲染入口/依赖收集）
/// </summary>
public sealed partial class ScribanTemplateRenderer
{
    // 页面集合的跨渲染包装缓存（T4.3 值缓存的分层落地）：
    // 同一源列表（同一次构建的 SiteContext.Pages 等）复用同一 LazyPageList——
    // PageContext→ScriptObject 转换全构建只做一次（万页站点每次渲染省 O(n) 转换）；
    // 新构建产生新列表实例，按引用自动失效。CWT 防列表驻留导致缓存泄漏
    private static readonly ConditionalWeakTable<IReadOnlyList<FlintPageContext>, LazyPageList> SharedPageLists = new();

    // 页面对象复用缓存：同一 PageContext 的 ScriptObject 跨渲染共享——
    // prev/next 交叉引用（A 的 prev 是 B）时复用 B 的对象避免重复全键构建；
    // CWT 键为 PageContext 引用，随构建周期回收
    private static readonly ConditionalWeakTable<FlintPageContext, LazyPageObject> SharedPageObjects = new();

    // 站点对象复用缓存：同一 SiteContext 的 site ScriptObject 跨渲染共享——
    // 站点级数据（title/params/menus/taxonomies）在单次构建内对所有页面相同，
    // 万页构建下每页重建 ~30 键 ScriptObject + menus 双份列表是纯浪费。
    // 依赖记录写入 per-render TemplateContext.Tags（非对象自身状态），复用安全；
    // 模板对 site.* 显式赋值属非常规用法（与 SharedPageObjects 同一风险模型）。
    // CWT 键为 SiteContext 引用，随构建周期回收
    private static readonly ConditionalWeakTable<FlintSiteContext, ScriptObject> SharedSiteObjects = new();

    private static ScriptObject CreateSiteObject(FlintSiteContext site)
    {
        return SharedSiteObjects.GetValue(site, static s => BuildSiteObject(s));
    }

    internal static ScriptObject CreatePageObject(
        FlintPageContext page,
        IReadOnlyList<FlintPageContext>? siteRegularPages = null,
        int paginateSize = 0,
        string paginatePath = "page",
        IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? siteTaxonomies = null)
    {
        return SharedPageObjects.GetValue(
            page, p => new LazyPageObject(p, siteRegularPages, paginateSize, paginatePath, siteTaxonomies));
    }

    /// <summary>同上但返回具体类型（partialValue 需访问 LazyPageObject.Store）</summary>
    internal static LazyPageObject CreatePageObjectTyped(FlintPageContext page)
    {
        return SharedPageObjects.GetValue(page, static p => new LazyPageObject(p));
    }

    /// <summary>
    /// 惰性页面对象：常规键构造时直接绑定；高成本的 prev/next 递归页对象延迟到
    /// 模板实际访问时构建（默认主题不访问 prev/next——万页构建可省 2×N 次全键
    /// 构建），并经 SharedPageObjects 复用。TryGetValue 只读不回写（并发渲染下
    /// ScriptObject 的写入非线程安全），全部可变状态在构造期完成
    /// </summary>
    internal sealed class LazyPageObject : ScriptObject
    {
        private readonly FlintPageContext _page;

        /// <summary>站点常规页集合（Hugo 的 .RegularPages 在任何页面都可用）</summary>
        private readonly IReadOnlyList<FlintPageContext>? _siteRegularPages;
        private readonly object? _pagesValue;
        private readonly object? _termsValue;
        private readonly object? _paginatorValue;

        /// <summary>页面级 Store（partial 返回值的传递通道，供 partialValue 读取）</summary>
        private PageStoreObject? _store;

        /// <summary>页面级 Store 实例（partialValue 机制用；构造后非 null）</summary>
        internal PageStoreObject? Store => _store;

        private readonly int _paginateSize;
        private readonly string _paginatePath;

        /// <summary>站点分类表（.GetTerms 需按当前页过滤词条）</summary>
        private readonly IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? _siteTaxonomies;

        public LazyPageObject(
            FlintPageContext page,
            IReadOnlyList<FlintPageContext>? siteRegularPages = null,
            int paginateSize = 0,
            string paginatePath = "page",
            IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? siteTaxonomies = null)
        {
            _page = page;
            _siteRegularPages = siteRegularPages;
            _paginateSize = paginateSize;
            _paginatePath = paginatePath;
            _siteTaxonomies = siteTaxonomies;
            _pagesValue = page.Pages is not null ? GetSharedPageList(page.Pages) : null;
            _termsValue = page.Terms is not null
                ? page.Terms.Select(t => (object)new LazyTaxonomyTerm(t)).ToList()
                : null;
            // C1：列表页的 pager 渲染实例携带本页分页器（page.paginator，对齐 Hugo）
            _paginatorValue = page.Paginator is not null
                ? BuildPaginatorObject(page.Paginator)
                : null;

            SetValue("title", page.Title, false);
            SetValue("content", page.Content, false);
            SetValue("output_format", page.OutputFormat, false);
            SetValue("permalink", page.Permalink, false);
            SetValue("rel_permalink", page.RelPermalink, false);
            SetValue("date", page.Date, false);
            SetValue("lastmod", page.LastMod, false);
            SetValue("tags", page.Tags, false);
            SetValue("categories", page.Categories, false);
            SetValue("word_count", page.WordCount, false);
            SetValue("reading_time", ReadingMinutes(page.ReadingTime), false);
            SetValue("description", page.Description, false);
            SetValue("summary", page.Summary, false);
            SetValue("type", page.Type, false);
            SetValue("layout", page.Layout, false);
            SetValue("draft", page.Draft, false);
            SetValue("weight", page.Weight, false);
            // .Params：Hugo 语义是「front matter 全量并入 + 自定义参数」，
            // 故 .Params.Title / .Params.Date 也可用（Ananke 用 .Params.Title 取标题，
            // 缺此兼容时 baseof 的 <title> 退化为站点名——差分验证实测发现）
            SetValue("params", BuildParamsDict(page), false);
            // .Resources：包装为带方法的集合（Hugo 的 .Resources.ByType/GetMatch/Match
            // 是 method 调用；裸列表无这些方法，主题会报 "function ... not found"）
            SetValue("resources", new PageResourcesObject(page.Resources), false);
            SetValue("pages", _pagesValue, false);
            SetValue("terms", _termsValue, false);
            SetValue("paginator", _paginatorValue, false);
            SetValue("section", page.Section, false);
            SetValue("table_of_contents", page.TableOfContents, false);
            SetValue("plain", page.Plain, false);
            SetValue("raw_content", page.RawContent, false);
            // B4：Hugo .File.* 方法族（主题常用 .File.Path / .File.ContentBaseName）
            SetValue("file", BuildFileObject(page.SourcePath), false);

            // taxonomy 页数据对象（对齐 Hugo .Data）：Singular/Plural/Terms/Pages。
            // Ananke 的 terms.html 迭代 page.Data.pages 枚举词条（词条对象含
            // title/rel_permalink/pages），taxonomy.html 用 page.pages 枚举内容页——
            // 缺此对象时 terms.html 报 "Cannot get the member page.Data.pages for a null object"
            // Hugo 语义：.Data 在任何页面都存在（普通页为空 map，taxonomy/term 页含
            // Singular/Plural/Terms/Pages）。此前非分类页不注册，导致主题的
            // `.Data.Integrity` 等链式访问报 "Cannot get the member ... for a null object"
            var pageData = BuildPageDataObject(page) ?? new ScriptObject();
            SetValue("data", pageData, false);
            SetValue("Data", pageData, false);

            // ---- Hugo Page 派生键投影（D 组）----
            // kind 判定谓词（主题高频：{{ if .IsPage }} / {{ if .IsHome }}）
            var kind = page.Kind.ToLowerInvariant();
            var isHome = kind == "home";
            var isPage = kind == "page";
            var isSection = kind == "section";
            var isNode = isHome || isSection || kind is "taxonomy" or "term";
            SetValue("is_home", isHome, false);
            SetValue("IsHome", isHome, false);
            SetValue("is_page", isPage, false);
            SetValue("IsPage", isPage, false);
            SetValue("is_section", isSection, false);
            SetValue("IsSection", isSection, false);
            SetValue("is_node", isNode, false);
            SetValue("IsNode", isNode, false);
            SetValue("is_branch", isNode, false);
            SetValue("IsBranch", isNode, false);
            SetValue("kind", kind, false);
            SetValue("Kind", kind, false);

            // 元数据投影（缺省回退，避免主题直接取用时 null 报错）
            SetValue("link_title", page.LinkTitle ?? page.Title, false);
            SetValue("LinkTitle", page.LinkTitle ?? page.Title, false);
            SetValue("truncated", page.Truncated, false);
            SetValue("Truncated", page.Truncated, false);
            SetValue("path", page.PagePath ?? "/", false);
            // .CurrentSection：页面的所属 section（自身即 section 时为自己）。
            // 仅能拿字符串（父页对象需树导航，见 site.sections），主题多用于取 .Title
            var currentSectionTitle = string.IsNullOrEmpty(page.Section)
                ? page.Title
                : page.Section;
            var currentSectionObj = new ScriptObject
            {
                ["title"] = currentSectionTitle,
                ["Title"] = currentSectionTitle,
                ["rel_permalink"] = "/" + (page.Section ?? "").Trim('/') + "/",
                ["RelPermalink"] = "/" + (page.Section ?? "").Trim('/') + "/"
            };
            SetValue("current_section", currentSectionObj, false);
            SetValue("CurrentSection", currentSectionObj, false);

            // ---- 矩阵验证暴露的缺失属性（多主题共性）----
            // .Level：页面在树中的深度（home=0，/posts/=1，/posts/x/=2）
            var level = (page.Section ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).Length
                        + (isPage ? 1 : 0);
            SetValue("level", level, false);
            SetValue("Level", level, false);

            // .Ancestors：祖先链（home → 各层 section → 自身），主题用 `.Ancestors.Reverse`
            // 做面包屑（PaperMod 实测）。按路径逐级构造最简投影
            var ancestors = BuildAncestorsObject(page);
            SetValue("ancestors", ancestors, false);
            SetValue("Ancestors", ancestors, false);

            // .Language：语言对象（主题用 .Language.LanguageDirection 判断 rtl）
            var langObj = new ScriptObject
            {
                ["lang"] = page.Language ?? "",
                ["Lang"] = page.Language ?? "",
                ["language_code"] = page.Language ?? "",
                ["LanguageCode"] = page.Language ?? "",
                ["language_name"] = page.Language ?? "",
                ["LanguageName"] = page.Language ?? "",
                // RTL 判断：主题读 language_direction；未知时给 ltr（安全默认）
                ["language_direction"] = "ltr",
                ["LanguageDirection"] = "ltr"
            };
            SetValue("language", langObj, false);
            SetValue("Language", langObj, false);
            SetValue("Path", page.PagePath ?? "/", false);
            SetValue("bundle_type", page.BundleType ?? "", false);
            SetValue("BundleType", page.BundleType ?? "", false);
            SetValue("keywords", page.Params.TryGetValue("keywords", out var kw) ? kw : new ScriptArray(), false);
            SetValue("Keywords", page.Params.TryGetValue("keywords", out var kw2) ? kw2 : new ScriptArray(), false);
            SetValue("publish_date", page.PublishDate ?? page.Date, false);
            SetValue("PublishDate", page.PublishDate ?? page.Date, false);
            SetValue("expiry_date", page.ExpiryDate, false);
            SetValue("ExpiryDate", page.ExpiryDate, false);
            SetValue("aliases", page.Aliases, false);
            SetValue("Aliases", page.Aliases, false);

            // .OutputFormats：输出格式集合 + Get(NAME) 方法（Hugo 语义；
            // 主题用 `{{ with .OutputFormats.Get "RSS" }}` 判断/取链接）
            var outputFormats = BuildOutputFormatsObject(page);
            SetValue("output_formats", outputFormats, false);
            SetValue("OutputFormats", outputFormats, false);

            // .Plain 的派生：词列表与模糊字数
            var plainWords = page.Plain is null
                ? new ScriptArray()
                : new ScriptArray(page.Plain
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Select(w => (object)w).ToArray());
            SetValue("plain_words", plainWords, false);
            SetValue("PlainWords", plainWords, false);
            SetValue("fuzzy_word_count", page.FuzzyWordCount, false);
            SetValue("FuzzyWordCount", page.FuzzyWordCount, false);

            // .Len（Hugo Page.Len：节点页为子页数，普通页为 0）
            var len = page.Pages?.Count ?? 0;
            SetValue("len", len, false);
            SetValue("Len", len, false);

            // .Scratch / .Store：页面级可变暂存（Hugo 语义，跨块/跨 partial 状态传递）。
            // 同时作为 partial 返回值的传递通道（partialValue 机制）——Store 是
            // 真实对象容器，跨 include 保真（无需序列化往返）
            var store = new PageStoreObject();
            _store = store;
            SetValue("store", store, false);
            SetValue("Store", store, false);
            SetValue("scratch", store, false);
            SetValue("Scratch", store, false);

            // .GetPage / .Paginate：Hugo 的**页面方法**（矩阵验证中 Ananke/Stack/
            // PaperMod/LoveIt 均命中——主题用 `.GetPage "section" .Section` 取 section 页，
            // 用 `.Paginate .Pages` 做分页）。引擎此前只在 site 对象上暴露 get_page
            var sitePages = _siteRegularPages ?? [];
            SetValue("get_page", new GetPageFunction(sitePages), false);
            SetValue("GetPage", new GetPageFunction(sitePages), false);
            SetValue("paginate", new PagePaginateFunction(_page, _paginateSize, _paginatePath), false);
            SetValue("Paginate", new PagePaginateFunction(_page, _paginateSize, _paginatePath), false);

            // .GetTerms / .GetTerms "taxonomy"：Hugo 页面方法——返回**当前页所属**的
            // 该分类词条页（按站点分类表过滤 Pages 含本页的词条）。Stack/PaperMod
            // 用 `$Page.GetTerms "tags"` 取标签页列表（缺此方法报 function not found）
            var getTerms = new PageGetTermsFunction(_page, _siteTaxonomies);
            SetValue("get_terms", getTerms, false);
            SetValue("GetTerms", getTerms, false);

            // .HasShortcode / .RenderString / .Param：Hugo 的页面方法族
            //（hugo-coder 42 处 `page?.has_shortcode`、archie `page.render_string`、
            //  fixit `page?.param` 实测——主题按方法调用，缺则整页渲染失败）
            var hasShortcode = new PageHasShortcodeFunction(_page);
            SetValue("has_shortcode", hasShortcode, false);
            SetValue("HasShortcode", hasShortcode, false);

            var renderString = new PageRenderStringFunction();
            SetValue("render_string", renderString, false);
            SetValue("RenderString", renderString, false);

            var pageParam = new PageParamFunction(_page);
            SetValue("param", pageParam, false);
            SetValue("Param", pageParam, false);
            // Hugo 兼容别名（大写开头）
            SetValue("Title", page.Title, false);
            SetValue("Content", page.Content, false);
            SetValue("Permalink", page.Permalink, false);
            SetValue("RelPermalink", page.RelPermalink, false);
            SetValue("Date", page.Date, false);
            SetValue("Lastmod", page.LastMod, false);
            SetValue("Tags", page.Tags, false);
            SetValue("Categories", page.Categories, false);
            SetValue("WordCount", page.WordCount, false);
            SetValue("ReadingTime", ReadingMinutes(page.ReadingTime), false);
            SetValue("Description", page.Description, false);
            SetValue("Summary", page.Summary, false);
            SetValue("Type", page.Type, false);
            SetValue("Layout", page.Layout, false);
            SetValue("Draft", page.Draft, false);
            SetValue("Weight", page.Weight, false);
            SetValue("Params", page.Params, false);
            SetValue("Resources", page.Resources, false);
            SetValue("Pages", _pagesValue, false);
            SetValue("Terms", _termsValue, false);
            SetValue("Paginator", _paginatorValue, false);
            SetValue("Section", page.Section, false);
            SetValue("TableOfContents", page.TableOfContents, false);
            SetValue("Plain", page.Plain, false);
            SetValue("RawContent", page.RawContent, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // prev/next 懒构建：CWT 复用（相邻页面对象全构建内共享），只读不回写
            if (member is "prev_page" or "PrevPage")
            {
                value = _page.PrevPage is not null ? CreatePageObject(_page.PrevPage) : null;
                return true;
            }
            if (member is "next_page" or "NextPage")
            {
                value = _page.NextPage is not null ? CreatePageObject(_page.NextPage) : null;
                return true;
            }

            // .RegularPages：Hugo 在任何页面都提供（single 页为站点级常规页集合，
            // 主题用 `{{ .RegularPages.Related . }}` 做相关推荐——Ananke 实证）
            if (member is "regular_pages" or "RegularPages")
            {
                if (_page.Pages is not null)
                {
                    // 节点页：自身子页集合（Hugo 语义优先）
                    value = GetSharedPageList(_page.Pages);
                    return true;
                }
                if (_siteRegularPages is not null)
                {
                    value = GetSharedPageList(_siteRegularPages);
                    return true;
                }
                value = new LazyPageList(Array.Empty<FlintPageContext>());
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }

        /// <summary>
        /// 包装的页面上下文（C4 视图接收者）：render "view" &lt;page&gt; 需要从
        /// 模板传入的页面对象反查真实 PageContext 作为渲染上下文
        /// </summary>
        internal FlintPageContext PageContext => _page;
    }

    /// <summary>
    /// 阅读时间投影：Hugo 的 <c>.ReadingTime</c> 是**分钟数整数**（向上取整），
    /// 而内部类型是 <see cref="TimeSpan"/>——主题会做算术与比较
    /// （`reading_time != 0`、`add reading_time 1`），直接暴露 TimeSpan 会报
    /// "Unable to convert type `TimeSpan` to int"（Blowfish 实测 1571 处）
    /// </summary>
    private static int ReadingMinutes(TimeSpan readingTime) =>
        (int)Math.Ceiling(readingTime.TotalMinutes);

    /// <summary>
    /// 绝对 URL（或已是相对路径）→ 相对路径（对齐 Hugo <c>.RelPermalink</c>）。
    /// 用 <see cref="Uri.PathAndQuery"/> 取路径部分，不依赖站点 baseURL 拼接形态；
    /// 解析失败时原样返回（宽容，不因脏数据中断渲染）
    /// </summary>
    private static string ToRelPermalink(string? permalink)
    {
        if (string.IsNullOrEmpty(permalink))
        {
            return "/";
        }

        if (permalink.StartsWith('/'))
        {
            return permalink;
        }

        return Uri.TryCreate(permalink, UriKind.Absolute, out var uri)
            ? uri.PathAndQuery
            : permalink;
    }

    /// <summary>
    /// .Paginate（Hugo 页面方法）：对给定集合建分页器。
    /// 主题用 `{{ $pag := .Paginate .Pages }}` 显式分页（Stack/PaperMod/LoveIt 实测）；
    /// 返回首个 pager（Hugo 语义：.Paginate 返回当前页的 pager）
    /// </summary>
    internal sealed class PagePaginateFunction(
        FlintPageContext page, int pageSize, string paginatePath)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            // 显式传入集合（默认用当前页 Pages）
            var items = page.Pages ?? [];
            if (arguments.Count > 0 && arguments[0] is Scriban.Runtime.ScriptArray arr)
            {
                // 从 ScriptObject 列表还原 PageContext 不可行（对象已投影）——
                // 故仅当传入的就是页面集合时复用；否则按当前页 Pages 分页
                _ = arr;
            }

            var size = pageSize > 0 ? pageSize : 10;
            var pager = PaginatorView.Create(items, 1, size,
                string.IsNullOrEmpty(page.RelPermalink) ? "/" : page.RelPermalink, paginatePath);
            return BuildPaginatorObject(pager);
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(object), "pages");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "paginator");
    }

    /// <summary>
    /// .HasShortcode（Hugo 页面方法）：内容是否含指定名字的短代码。
    /// 主题用它为含特定短代码的页面附加资源（如 mermaid/katex 的脚本）——
    /// 判定宽松（内容里出现 <c>{{&lt; name</c> 或 <c>{{% name</c> 即算命中），
    /// 与 Hugo 的"短代码名为路径最后一段"语义一致
    /// </summary>
    internal sealed class PageHasShortcodeFunction(FlintPageContext page)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return false;
            }
            var name = arguments[0]?.ToString() ?? "";
            if (name.Length == 0)
            {
                return false;
            }
            var raw = page.RawContent ?? "";
            if (raw.Length == 0)
            {
                return false;
            }
            // 短代码名可能是路径（"foo/bar"），Hugo 按末段匹配文件名
            var lastSeg = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..] : name;
            foreach (var marker in new[] { "{{< ", "{{% ", "{{<" , "{{%" })
            {
                if (raw.Contains(marker + lastSeg, StringComparison.Ordinal) ||
                    raw.Contains(marker + name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(bool);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "name");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(bool), "result");
    }

    /// <summary>
    /// .RenderString（Hugo 页面方法）：把字符串按 Markdown 渲染为 HTML。
    /// Hugo 的签名是 <c>RenderString STRING [OPTS]</c>（可选 display/type 选项）。
    /// Flint 侧用内容管线的 Markdown 渲染器（与正文渲染一致）
    /// </summary>
    internal sealed class PageRenderStringFunction()
        : Scriban.Runtime.IScriptCustomFunction
    {
        private static readonly Content.MarkdownParser Parser = new();

        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return "";
            }
            var text = arguments[0]?.ToString() ?? "";
            if (text.Length == 0)
            {
                return "";
            }
            try
            {
                return Parser.ToHtml(text);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 渲染失败返回原文（Hugo 对渲染错误同样不中断页面渲染）
                return text;
            }
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 2;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(string);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(index == 0 ? typeof(string) : typeof(object), index == 0 ? "text" : "options");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(string), "html");
    }

    /// <summary>
    /// .Param（Hugo 页面方法）：按点路径取参数（页面 params → 站点 params 回落）。
    /// 与全局 paramLookup 同语义，登记为页面方法使 <c>page.Param "x"</c> 可直接调用
    /// </summary>
    internal sealed class PageParamFunction(FlintPageContext page)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return null;
            }
            var path = arguments[0]?.ToString() ?? "";
            if (path.Length == 0 || page.Params is null)
            {
                return null;
            }
            // 页面参数点路径查找（Hugo .Param 语义；站点回落由模板侧的 paramLookup 覆盖）
            return BuiltinTemplateFunctions.GetMember(page.Params, path);
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "path");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "value");
    }

    /// <summary>
    /// .GetTerms（Hugo 页面方法）：返回当前页在指定分类下的词条页列表。
    /// 从站点分类表取出该分类全部词条，过滤出 Pages 含当前页者
    /// （Hugo 语义：页面的 .GetTerms "tags" 给该页用到的标签页）
    /// </summary>
    internal sealed class PageGetTermsFunction(
        FlintPageContext page,
        IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>>? siteTaxonomies)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var result = new List<object>();
            if (siteTaxonomies is null || arguments.Count == 0)
            {
                return result;
            }

            var name = arguments[0]?.ToString() ?? "";
            if (name.Length == 0 || !siteTaxonomies.TryGetValue(name, out var terms))
            {
                return result;
            }

            foreach (var term in terms)
            {
                if (term.Pages.Any(p => ReferenceEquals(p, page) ||
                        string.Equals(p.SourcePath, page.SourcePath, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new LazyTermPage(term));
                }
            }
            return result;
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 0;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "taxonomy");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "terms");
    }

    /// <summary>
    /// .Site.GetPage / .Page.GetPage：按路径或 (kind, 名) 查页。
    /// 未命中返回 null（Hugo 语义；主题通常用 with 包裹）
    /// </summary>
    internal sealed class GetPageFunction(IReadOnlyList<FlintPageContext> pages)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            if (arguments.Count == 0)
            {
                return null;
            }

            var a0 = arguments[0]?.ToString() ?? "";
            var a1 = arguments.Count > 1 ? arguments[1]?.ToString() : null;

            FlintPageContext? found = null;
            if (a1 is not null)
            {
                // (kind, 名) 形态：section 按 Section 段匹配，page 按标题/slug 匹配
                found = a0.ToLowerInvariant() switch
                {
                    "section" or "sections" => pages.FirstOrDefault(p =>
                        p.Kind.Equals("section", StringComparison.OrdinalIgnoreCase) &&
                        (p.Section.Equals(a1, StringComparison.OrdinalIgnoreCase) ||
                         p.RelPermalink.Trim('/').Equals(a1, StringComparison.OrdinalIgnoreCase))),
                    "home" => pages.FirstOrDefault(p => p.Kind.Equals("home", StringComparison.OrdinalIgnoreCase)),
                    "page" => pages.FirstOrDefault(p => p.Title.Equals(a1, StringComparison.OrdinalIgnoreCase)),
                    _ => null
                };
            }
            else
            {
                // 路径形态：归一后比对 RelPermalink
                var path = a0.Trim('/');
                found = pages.FirstOrDefault(p =>
                    p.RelPermalink.Trim('/').Equals(path, StringComparison.OrdinalIgnoreCase) ||
                    p.PagePath?.Trim('/').Equals(path, StringComparison.OrdinalIgnoreCase) == true ||
                    (path.Length == 0 && p.Kind.Equals("home", StringComparison.OrdinalIgnoreCase)));
            }

            return found is null ? null : CreatePageObject(found);
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 2;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), index == 0 ? "pathOrKind" : "name");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "page");
    }

    /// <summary>
    /// .Ancestors：祖先链对象（含 Reverse 方法，供面包屑）。
    /// 按相对路径逐级上溯构造（home → /a/ → /a/b/），对齐 Hugo 的祖先语义
    /// </summary>
    private static ScriptObject BuildAncestorsObject(FlintPageContext page)
    {
        var segs = (page.RelPermalink ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries);
        var chain = new ScriptArray();
        // home
        chain.Add(new ScriptObject
        {
            ["title"] = page.Title,
            ["Title"] = page.Title,
            ["rel_permalink"] = "/",
            ["RelPermalink"] = "/",
            ["is_home"] = true,
            ["IsHome"] = true
        });

        var acc = "";
        for (var i = 0; i < Math.Max(0, segs.Length - 1); i++)
        {
            acc += "/" + segs[i];
            chain.Add(new ScriptObject
            {
                ["title"] = segs[i],
                ["Title"] = segs[i],
                ["rel_permalink"] = acc + "/",
                ["RelPermalink"] = acc + "/",
                ["is_section"] = true,
                ["IsSection"] = true
            });
        }

        var o = new ScriptObject();
        foreach (var i in Enumerable.Range(0, chain.Count))
        {
            o[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = chain[i];
        }
        o["count"] = chain.Count;
        o["Count"] = chain.Count;

        // .Reverse（Hugo 的 Pages.Reverse）：面包屑常反向输出
        o["reverse"] = new ArrayReverseFunction(chain);
        o["Reverse"] = o["reverse"];
        return o;
    }

    /// <summary>
    /// .OutputFormats 对象：格式列表 + Get(NAME) 方法。
    /// Hugo 的 `.OutputFormats.Get "RSS"` 返回格式对象（含 RelPermalink），
    /// 未命中返回 null（主题用 with 包裹）
    /// </summary>
    private static ScriptObject BuildOutputFormatsObject(FlintPageContext page)
    {
        var formats = page.Outputs.Count > 0 ? page.Outputs : (IReadOnlyList<string>)["html"];
        var arr = new ScriptArray();
        foreach (var name in formats)
        {
            arr.Add(BuildFormatObject(page, name));
        }

        var o = new ScriptObject();
        foreach (var item in arr)
        {
            o[(string)((ScriptObject)item!)["name"]!] = item;
        }

        o["get"] = new OutputFormatsGetFunction(arr);
        o["Get"] = o["get"];
        o["count"] = arr.Count;
        o["Count"] = arr.Count;

        // 使对象可迭代（for fmt in page.output_formats）
        foreach (var i in Enumerable.Range(0, arr.Count))
        {
            o[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = arr[i];
        }

        return o;
    }

    private static ScriptObject BuildFormatObject(FlintPageContext page, string name)
    {
        var suffix = name.ToLowerInvariant() switch
        {
            "rss" => "xml",
            "json" => "json",
            _ => "html"
        };
        var rel = name.Equals("html", StringComparison.OrdinalIgnoreCase)
            ? page.RelPermalink
            : page.RelPermalink.TrimEnd('/') + "/index." + suffix;
        return new ScriptObject
        {
            ["name"] = name, ["Name"] = name,
            ["media_type"] = name.ToLowerInvariant() switch
            {
                "rss" => "application/rss+xml",
                "json" => "application/json",
                _ => "text/html"
            },
            ["rel_permalink"] = rel, ["RelPermalink"] = rel,
            ["permalink"] = page.Permalink, ["Permalink"] = page.Permalink,
            ["rel"] = "alternate", ["Rel"] = "alternate"
        };
    }

    /// <summary>OutputFormats.Get(NAME)：按名取格式（未命中返回 null）</summary>
    private sealed class OutputFormatsGetFunction(ScriptArray formats)
        : Scriban.Runtime.IScriptCustomFunction
    {
        public object? Invoke(Scriban.TemplateContext context, Scriban.Syntax.ScriptNode? callerContext,
            Scriban.Runtime.ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement? blockStatement)
        {
            var want = arguments.Count > 0 ? arguments[0]?.ToString() ?? "" : "";
            foreach (var item in formats)
            {
                if (item is ScriptObject o &&
                    string.Equals(o["name"]?.ToString(), want, StringComparison.OrdinalIgnoreCase))
                {
                    return o;
                }
            }
            return null;
        }

        public ValueTask<object?> InvokeAsync(Scriban.TemplateContext context,
            Scriban.Syntax.ScriptNode? callerContext, Scriban.Runtime.ScriptArray arguments,
            Scriban.Syntax.ScriptBlockStatement? blockStatement) =>
            new(Invoke(context, callerContext, arguments, blockStatement));

        public int RequiredParameterCount => 1;
        public int ParameterCount => 1;
        public Scriban.Runtime.ScriptVarParamKind VarParamKind =>
            Scriban.Runtime.ScriptVarParamKind.Direct;
        public Type ReturnType => typeof(object);
        public Scriban.Runtime.ScriptParameterInfo GetParameterInfo(int index) =>
            new(typeof(string), "name");
        public Scriban.Runtime.ScriptParameterInfo ReturnParameterInfo =>
            new(typeof(object), "format");
    }

    /// <summary>
    /// 构造 .Params 视图（Hugo 语义）：front matter 顶层字段并入自定义参数，
    /// 页面自身字段优先于同名自定义参数。键同时提供小写与首字母大写形态
    /// （主题两种写法都常见：<c>.Params.Title</c> / <c>.Params.title</c>）
    /// </summary>
    private static Dictionary<string, object> BuildParamsDict(FlintPageContext page)
    {
        var o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // 1) 自定义参数（front matter 的 params 段 + 未被识别的顶层键）
        foreach (var kv in page.Params)
        {
            o[kv.Key] = kv.Value;
        }

        // 2) front matter 顶层字段（页面字段优先）
        void SetBoth(string name, object? value)
        {
            if (value is null)
            {
                return;
            }
            o[name] = value;
            o[name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name] = value;
        }

        SetBoth("title", page.Title);
        SetBoth("description", page.Description);
        SetBoth("summary", page.Summary);
        SetBoth("date", page.Date);
        SetBoth("lastmod", page.LastMod);
        SetBoth("publishdate", page.PublishDate);
        SetBoth("expirydate", page.ExpiryDate);
        SetBoth("type", page.Type);
        SetBoth("layout", page.Layout);
        SetBoth("weight", page.Weight);
        SetBoth("draft", page.Draft);
        SetBoth("section", page.Section);
        SetBoth("kind", page.Kind);
        if (page.Tags.Count > 0)
        {
            SetBoth("tags", page.Tags);
        }
        if (page.Categories.Count > 0)
        {
            SetBoth("categories", page.Categories);
        }
        if (page.Aliases.Count > 0)
        {
            SetBoth("aliases", page.Aliases);
        }

        return o;
    }

    /// <summary>
    /// 分类页数据对象（对齐 Hugo <c>.Data</c>）：
    /// <c>singular</c>/<c>plural</c>/<c>terms</c>（含 Alphabetical/ByCount）/<c>pages</c>。
    /// kind=taxonomy（terms.html）的 <c>pages</c> 为词条对象列表；
    /// kind=term（taxonomy.html）的 <c>pages</c> 为内容页列表。
    /// 非分类页返回 null（不注册 <c>data</c> 键，模板访问得 null 而非误值）
    /// </summary>
    private static ScriptObject? BuildPageDataObject(FlintPageContext page)
    {
        var isTaxonomyList = page.Kind.Equals("taxonomy", StringComparison.OrdinalIgnoreCase);
        var isTerm = page.Kind.Equals("term", StringComparison.OrdinalIgnoreCase);
        if (!isTaxonomyList && !isTerm)
        {
            return null;
        }

        var data = new ScriptObject
        {
            ["singular"] = page.TaxonomySingular ?? page.TaxonomyName,
            ["plural"] = page.TaxonomyPlural ?? page.TaxonomyName,
            ["Singular"] = page.TaxonomySingular ?? page.TaxonomyName,
            ["Plural"] = page.TaxonomyPlural ?? page.TaxonomyName
        };

        // Terms：词条名 → 词条对象，另提供 Alphabetical / ByCount 排序视图
        var terms = page.Terms ?? [];
        var termsMap = new LazyTermsMap(terms);
        data["terms"] = termsMap;
        data["Terms"] = termsMap;

        // Pages：taxonomy 列表页给词条对象（Ananke terms.html 语义），
        // term 页给内容页集合（Ananke taxonomy.html 用 page.pages，此处同源）
        var pagesValue = isTaxonomyList
            ? terms.Select(t => (object)new LazyTermPage(t)).ToList()
            : (object?)GetSharedPageList(page.Pages ?? []);
        data["pages"] = pagesValue;
        data["Pages"] = pagesValue;

        return data;
    }

    /// <summary>
    /// 词条对象（Hugo 词条页视角）：title/rel_permalink/permalink/pages/count。
    /// terms.html 迭代 <c>Data.Pages</c> 时按此形状访问
    /// </summary>
    private sealed class LazyTermPage : ScriptObject
    {
        private readonly TaxonomyTerm _term;
        private LazyPageList? _lazyPages;

        public LazyTermPage(TaxonomyTerm term)
        {
            _term = term;
            // RelPermalink 为相对路径（对齐 Hugo .RelPermalink 语义）——主题把它
            // 放进 href 或与锚点拼接时，绝对 URL 会产出错误链接
            var rel = ToRelPermalink(term.Permalink);
            SetValue("title", term.Name, false);
            SetValue("name", term.Name, false);
            SetValue("rel_permalink", rel, false);
            SetValue("permalink", term.Permalink, false);
            SetValue("count", term.Count, false);
            SetValue("slug", term.Slug, false);

            SetValue("Title", term.Name, false);
            SetValue("Name", term.Name, false);
            SetValue("RelPermalink", rel, false);
            SetValue("Permalink", term.Permalink, false);
            SetValue("Count", term.Count, false);
            SetValue("Slug", term.Slug, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (member.Equals("pages", StringComparison.OrdinalIgnoreCase))
            {
                _lazyPages ??= GetSharedPageList(_term.Pages);
                value = _lazyPages;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 词条映射（Hugo <c>.Data.Terms</c>）：按词条名取值，另有
    /// <c>Alphabetical</c>（按名序）与 <c>ByCount</c>（按数量降序）两个排序视图，
    /// 对齐 Hugo 文档的 taxonomy 用法
    /// </summary>
    private sealed class LazyTermsMap : ScriptObject
    {
        private readonly IReadOnlyList<TaxonomyTerm> _terms;
        private List<object>? _alphabetical;
        private List<object>? _byCount;

        public LazyTermsMap(IReadOnlyList<TaxonomyTerm> terms)
        {
            _terms = terms;
            SetValue("count", terms.Count, false);
            SetValue("Count", terms.Count, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            switch (member.ToLowerInvariant())
            {
                case "alphabetical":
                    _alphabetical ??= _terms
                        .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(t => (object)new LazyTermPage(t))
                        .ToList();
                    value = _alphabetical;
                    return true;
                case "bycount":
                    _byCount ??= _terms
                        .OrderByDescending(t => t.Count)
                        .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(t => (object)new LazyTermPage(t))
                        .ToList();
                    value = _byCount;
                    return true;
            }

            // 词条名直接取值（.Data.Terms.foo）
            var match = _terms.FirstOrDefault(t =>
                t.Name.Equals(member, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                value = new LazyTermPage(match);
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    private static ScriptObject BuildSiteObject(FlintSiteContext site)
    {
        // 惰加载包装器跨渲染共享（见 SharedPageLists）；GetConvertedPages 内部有锁保证并发安全
        var lazyPages = GetSharedPageList(site.Pages);
        var lazyRegularPages = GetSharedPageList(site.RegularPages);
        var lazyTaxonomies = new LazyTaxonomies(site.Taxonomies);

        // 站点级暂存：同一 SiteContext 的所有页面共享（site 对象本身跨渲染复用缓存，
        // 故该 Store 在整次构建内被所有页面看到——与 Hugo .Site.Store 语义一致）
        var siteStore = new PageStoreObject();

        // 依赖跟踪站点对象：site.* 成员访问被记录为 data:site.* 依赖键（T4.1）
        // site.home：home 页的页面对象（Hugo 语义；主题用 .Site.Home.RelPermalink 等）。
        // 惰构建 + 共享缓存——无 home 页时返回 null（主题通常配合 ?. 或 with 使用）
        var homePage = site.Pages.FirstOrDefault(p =>
            string.Equals(p.Kind, "home", StringComparison.OrdinalIgnoreCase));
        var lazyHome = homePage is null ? null : (object)CreatePageObject(homePage);

        return new DependencyTrackingScriptObject
        {
            ["title"] = site.Title,
            ["base_url"] = site.BaseURL,
            ["home"] = lazyHome,
            ["Home"] = lazyHome,
            // .Site.GetPage（Hugo 路径/kind 查询）：主题用它取 section 页做导航
            // （Ananke/Stack 实测）。签名兼容两种形态：
            //   GetPage "/posts"            按路径
            //   GetPage "section" "posts"   按 kind + 名
            ["get_page"] = new GetPageFunction(site.Pages),
            ["GetPage"] = new GetPageFunction(site.Pages),
            ["language"] = site.Language,
            ["pages"] = lazyPages,
            ["regular_pages"] = lazyRegularPages,
            ["taxonomies"] = lazyTaxonomies,
            ["menus"] = CreateMenusObject(site.Menus),
            ["paginator"] = BuildPaginatorObject(site),
            ["config"] = site.Config,
            ["data"] = site.Data,
            ["params"] = site.Params,
            ["build_date"] = site.BuildDate,
            ["last_change"] = site.LastChange,
            ["is_multilingual"] = site.IsMultiLingual,
            ["languages"] = site.Languages,

            // 站点级暂存（Hugo 0.113+ 的 .Site.Store / .Site.Scratch）：
            // 与页面级 Store 同型（Set/Get/Add/SetInMap/DeleteInMap/GetSortedMapValues）。
            // 此前只有页面有 Store，主题用 `site.Store.SetInMap "pagination" ...` 时
            // 报 "Cannot get the member site.store.setinmap for a null object"
            //（hugo-book / relearn / jane 等命中）
            ["store"] = siteStore,
            ["Store"] = siteStore,
            ["scratch"] = siteStore,
            ["Scratch"] = siteStore,

            // Hugo 兼容别名
            ["Title"] = site.Title,
            ["BaseURL"] = site.BaseURL,
            ["Language"] = site.Language,
            ["Pages"] = lazyPages,
            ["RegularPages"] = lazyRegularPages,
            ["Taxonomies"] = lazyTaxonomies,
            ["Menus"] = CreateMenusObject(site.Menus),
            ["Config"] = site.Config,
            ["Data"] = site.Data,
            ["Params"] = site.Params,
            ["BuildDate"] = site.BuildDate,
            ["LastChange"] = site.LastChange,
            ["IsMultiLingual"] = site.IsMultiLingual,
            ["Languages"] = site.Languages,
        };
    }

    /// <summary>
    /// 懒加载页面列表包装器
    /// 只有在实际访问时才转换页面对象，避免 O(n²) 问题
    /// </summary>
    private static LazyPageList GetSharedPageList(IReadOnlyList<FlintPageContext> pages)
    {
        return SharedPageLists.GetValue(pages, static p => new LazyPageList(p));
    }

    private sealed class LazyPageList : ScriptObject, IEnumerable<ScriptObject>, IList<ScriptObject>
    {
        private readonly IReadOnlyList<FlintPageContext> _pages;
        private List<ScriptObject>? _convertedPages;

        public LazyPageList(IReadOnlyList<FlintPageContext> pages)
        {
            _pages = pages;
            // 集合方法（Hugo Pages 方法族）：主题在集合上调用，如
            // {{ range .Pages.ByDate }} / {{ .Site.RegularPages.Related . }} / {{ first 5 .Pages }}
            // 集合方法（Hugo Pages 方法族）：主题在集合上调用，如
            // {{ range .Pages.ByDate }} / {{ .Site.RegularPages.Related . }}。
            // Scriban 成员查找不区分大小写，单一注册即可覆盖 ByDate/by_date 两种写法
            SetValue("bydate", new PagesByDateFunction(pages), false);
            SetValue("bytitle", new PagesByTitleFunction(pages), false);
            SetValue("byweight", new PagesByWeightFunction(pages), false);
            SetValue("bylength", new PagesByLengthFunction(pages), false);
            SetValue("bylastmod", new PagesByLastmodFunction(pages), false);
            SetValue("bypublishdate", new PagesByDateFunction(pages), false);
            SetValue("byexpirydate", new PagesByDateFunction(pages), false);
            SetValue("byparam", new PagesByParamFunction(pages), false);
            SetValue("bylinktitle", new PagesByTitleFunction(pages), false);
            SetValue("related", new PagesRelatedFunction(pages), false);
            SetValue("reverse", new PagesReverseFunction(pages), false);
            SetValue("limit", new PagesLimitFunction(pages), false);
            SetValue("groupby", new PagesGroupByFunction(pages), false);
            SetValue("groupbydate", new PagesGroupByDateFunction(pages), false);
            SetValue("indexof", new PagesIndexOfFunction(pages), false);
            SetValue("next", new PagesNextPrevFunction(pages, forward: true), false);
            SetValue("prev", new PagesNextPrevFunction(pages, forward: false), false);
            // 设置 count/length 属性，这些是常用的且不需要转换所有页面
            SetValue("count", pages.Count, false);
            SetValue("length", pages.Count, false);
            SetValue("size", pages.Count, false);
            SetValue("len", pages.Count, false);
            SetValue("Count", pages.Count, false);
            SetValue("Length", pages.Count, false);
            SetValue("Len", pages.Count, false);
        }

        // IList<ScriptObject> 实现 - 用于 Scriban 的 len 过滤器
        int ICollection<ScriptObject>.Count => _pages.Count;
        bool ICollection<ScriptObject>.IsReadOnly => true;

        public ScriptObject this[int index]
        {
            get => GetConvertedPages()[index];
            set => throw new NotSupportedException();
        }

        public int IndexOf(ScriptObject item) => GetConvertedPages().IndexOf(item);
        public bool Contains(ScriptObject item) => GetConvertedPages().Contains(item);
        public void CopyTo(ScriptObject[] array, int arrayIndex) => GetConvertedPages().CopyTo(array, arrayIndex);
        void ICollection<ScriptObject>.Add(ScriptObject item) => throw new NotSupportedException();
        void ICollection<ScriptObject>.Clear() => throw new NotSupportedException();
        bool ICollection<ScriptObject>.Remove(ScriptObject item) => throw new NotSupportedException();
        public void Insert(int index, ScriptObject item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();

        private readonly object _conversionLock = new();

        private List<ScriptObject> GetConvertedPages()
        {
            // 跨渲染共享后的并发保护：并行页面渲染同时枚举同一实例
            if (_convertedPages is not null)
            {
                return _convertedPages;
            }

            lock (_conversionLock)
            {
                return _convertedPages ??= _pages.Select(p => CreatePageObject(p)).ToList();
            }
        }

        public new IEnumerator<ScriptObject> GetEnumerator()
        {
            return GetConvertedPages().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetConvertedPages().GetEnumerator();
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // 处理索引访问
            if (int.TryParse(member, out var index) && index >= 0 && index < _pages.Count)
            {
                value = GetConvertedPages()[index];
                return true;
            }

            // 处理 first/last 等常用属性
            switch (member.ToLowerInvariant())
            {
                case "first":
                    value = _pages.Count > 0 ? CreatePageObject(_pages[0]) : null;
                    return true;
                case "last":
                    value = _pages.Count > 0 ? CreatePageObject(_pages[^1]) : null;
                    return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 懒加载分类集合包装器
    /// </summary>
    private sealed class LazyTaxonomies : ScriptObject
    {
        private readonly FlintTaxonomyCollection _taxonomies;
        private readonly Dictionary<string, object> _convertedTaxonomies = new();

        public LazyTaxonomies(FlintTaxonomyCollection taxonomies)
        {
            _taxonomies = taxonomies;
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (_convertedTaxonomies.TryGetValue(member, out var cached))
            {
                value = cached;
                return true;
            }

            if (_taxonomies.Taxonomies.TryGetValue(member, out var terms))
            {
                // 使用懒加载的术语列表
                var lazyTerms = terms.Select(t => new LazyTaxonomyTerm(t)).ToList();
                _convertedTaxonomies[member] = lazyTerms;
                value = lazyTerms;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 懒加载分类术语包装器
    /// </summary>
    private sealed class LazyTaxonomyTerm : ScriptObject
    {
        private readonly TaxonomyTerm _term;
        private LazyPageList? _lazyPages;

        public LazyTaxonomyTerm(TaxonomyTerm term)
        {
            _term = term;
            // 设置基本属性（不需要转换页面）
            SetValue("name", term.Name, false);
            SetValue("slug", term.Slug, false);
            SetValue("count", term.Count, false);
            SetValue("permalink", term.Permalink, false);

            // Hugo 兼容
            SetValue("Name", term.Name, false);
            SetValue("Slug", term.Slug, false);
            SetValue("Count", term.Count, false);
            SetValue("Permalink", term.Permalink, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            // 只有访问 pages/Pages 时才懒加载
            if (member.Equals("pages", StringComparison.OrdinalIgnoreCase))
            {
                _lazyPages ??= new LazyPageList(_term.Pages);
                value = _lazyPages;
                return true;
            }

            return base.TryGetValue(context, span, member, out value);
        }
    }


    // CreateTaxonomiesObject 已被 LazyTaxonomies 替代，不再需要

    /// <summary>
    /// Hugo .File 对象（B4）：从页面源路径派生 path/dirname/basename/content_base_name/
    /// filename/extension/unique_id/translation_base_name。无源路径（合成页）返回空对象
    /// </summary>
    private static ScriptObject BuildFileObject(string? sourcePath)
    {
        var file = new ScriptObject();
        if (string.IsNullOrEmpty(sourcePath))
        {
            file["path"] = "";
            file["Path"] = "";
            return file;
        }

        var normalized = sourcePath.Replace('\\', '/');
        // Hugo .File.Path 相对 content/ 目录（无前导斜杠）
        var contentIdx = normalized.LastIndexOf("/content/", StringComparison.OrdinalIgnoreCase);
        var relPath = contentIdx >= 0
            ? normalized[(contentIdx + "/content/".Length)..]
            : normalized.TrimStart('/');
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        // index/_index 的 ContentBaseName 取父目录名（Hugo 语义）
        var contentBaseName = baseName is "index" or "_index"
            ? Path.GetFileName(Path.GetDirectoryName(sourcePath) ?? "") ?? ""
            : baseName;

        var values = new (string Key, object Value)[]
        {
            ("path", relPath),
            ("dirname", relPath.Contains('/') ? relPath[..relPath.LastIndexOf('/')] : ""),
            ("basename", baseName),
            ("content_base_name", contentBaseName),
            ("filename", Path.GetFileName(sourcePath)),
            ("extension", Path.GetExtension(sourcePath).TrimStart('.')),
            ("unique_id", relPath)
        };
        foreach (var (key, value) in values)
        {
            file[key] = value;
            file[char.ToUpperInvariant(key[0]) + key[1..]] = value; // Hugo 大写别名
        }
        return file;
    }

    private static ScriptObject CreateMenusObject(FlintMenuCollection menus)
    {
        var obj = new ScriptObject();
        foreach (var (name, items) in menus.Menus)
        {
            obj[name] = items.Select(CreateMenuItemObject).ToList();
        }
        return obj;
    }

    /// <summary>
    /// 站点级分页对象（无逐页绑定时回落，保持既有行为）：
    /// pages = 首版首页切片，total_pages/page_number/has_prev/has_next
    /// </summary>
    private static ScriptObject BuildPaginatorObject(FlintSiteContext site)
    {
        var so = new ScriptObject();
        so["pages"] = GetSharedPageList(site.PaginatorPages);
        so["total_pages"] = site.PaginatorTotalPages;
        so["page_number"] = site.PaginatorPageNumber;
        so["has_prev"] = site.PaginatorPageNumber > 1;
        so["has_next"] = site.PaginatorPageNumber < site.PaginatorTotalPages;
        return so;
    }

    // 逐页绑定的分页对象复用缓存：同一 PaginatorView（一个列表页的所有 pager 共享）
    // 跨渲染/跨 site.paginator 与 page.paginator 只构建一次；CWT 键为视图引用，
    // 随构建周期回收（与 SharedPageObjects 同一模型）
    private static readonly ConditionalWeakTable<PaginatorView, ScriptObject> SharedPaginatorObjects = new();

    private static ScriptObject BuildPaginatorObject(PaginatorView view)
    {
        return SharedPaginatorObjects.GetValue(view, static v => BuildPaginatorObjectCore(v));
    }

    /// <summary>
    /// 分页器对象（C1，对齐 Hugo Pager 字段子集）：
    /// pages/page_number/total_pages/pager_size/number_of_elements/total_number_of_elements/
    /// has_prev/has_next/is_first/is_last/url，以及 first/last/prev/next/pagers（均 pager 对象）。
    /// pagers 为惰性列表——模板未访问时不物化（大站点避免 O(N) 分页对象构造）
    /// </summary>
    private static ScriptObject BuildPaginatorObjectCore(PaginatorView view)
    {
        var so = new ScriptObject();
        var pages = GetSharedPageList(view.Pages);
        so["pages"] = pages;
        so["page_number"] = view.PageNumber;
        so["total_pages"] = view.TotalPages;
        so["pager_size"] = view.PageSize;
        so["number_of_elements"] = view.NumberOfElements;
        so["total_number_of_elements"] = view.TotalItems;
        so["has_prev"] = view.HasPrev;
        so["has_next"] = view.HasNext;
        so["is_first"] = view.IsFirst;
        so["is_last"] = view.IsLast;
        so["url"] = view.URL;
        so["first"] = new PagerObject(view.First);
        so["last"] = new PagerObject(view.Last);
        so["prev"] = view.Prev is not null ? new PagerObject(view.Prev) : null;
        so["next"] = view.Next is not null ? new PagerObject(view.Next) : null;
        so["pagers"] = new LazyPagers(view.Pagers);

        // Hugo 兼容别名（PascalCase）：Hugo 的 Pager 字段是 Pascal（.TotalPages/
        // .HasPrev/.Prev.URL），而 Scriban 成员查找大小写敏感——只注册 snake
        // 会使模板的 `$pag.HasPrev`/`$pag.Prev.URL` 取到 null（实测 mini 主题）
        foreach (var (snake, pascal) in new (string, string)[]
                 {
                     ("pages", "Pages"), ("page_number", "PageNumber"),
                     ("total_pages", "TotalPages"), ("pager_size", "PagerSize"),
                     ("number_of_elements", "NumberOfElements"),
                     ("total_number_of_elements", "TotalNumberOfElements"),
                     ("has_prev", "HasPrev"), ("has_next", "HasNext"),
                     ("is_first", "IsFirst"), ("is_last", "IsLast"),
                     ("url", "URL"), ("first", "First"), ("last", "Last"),
                     ("prev", "Prev"), ("next", "Next"), ("pagers", "Pagers")
                 })
        {
            if (so.ContainsKey(snake) && !so.ContainsKey(pascal))
            {
                so[pascal] = so[snake];
            }
        }

        // Hugo 兼容大写别名
        so["Pages"] = pages;
        so["PageNumber"] = view.PageNumber;
        so["TotalPages"] = view.TotalPages;
        so["PagerSize"] = view.PageSize;
        so["NumberOfElements"] = view.NumberOfElements;
        so["TotalNumberOfElements"] = view.TotalItems;
        so["HasPrev"] = view.HasPrev;
        so["HasNext"] = view.HasNext;
        so["URL"] = view.URL;
        return so;
    }

    /// <summary>
    /// 单个 pager 对象：标量元数据即时绑定，pages 切片惰性求值
    /// （视图共享，切片本身在 PaginatorView 内已缓存）
    /// </summary>
    private sealed class PagerObject : ScriptObject
    {
        private readonly PaginatorView _pager;
        private LazyPageList? _pages;

        public PagerObject(PaginatorView pager)
        {
            _pager = pager;
            var url = pager.URL;
            SetValue("url", url, false);
            SetValue("page_number", pager.PageNumber, false);
            SetValue("total_pages", pager.TotalPages, false);
            SetValue("pager_size", pager.PageSize, false);
            SetValue("has_prev", pager.HasPrev, false);
            SetValue("has_next", pager.HasNext, false);
            SetValue("is_first", pager.IsFirst, false);
            SetValue("is_last", pager.IsLast, false);

            SetValue("URL", url, false);
            SetValue("PageNumber", pager.PageNumber, false);
            SetValue("TotalPages", pager.TotalPages, false);
            SetValue("PagerSize", pager.PageSize, false);
            SetValue("HasPrev", pager.HasPrev, false);
            SetValue("HasNext", pager.HasNext, false);
        }

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (member is "pages" or "Pages")
            {
                _pages ??= GetSharedPageList(_pager.Pages);
                value = _pages;
                return true;
            }
            return base.TryGetValue(context, span, member, out value);
        }
    }

    /// <summary>
    /// 惰性 pager 列表（脚本对象包装）：模板访问 pagers 时才把视图列表转成对象列表，
    /// 未访问零开销。IList&lt;ScriptObject&gt; 实现供 Scriban 的索引/len/size 消费
    /// （与 LazyPageList 同一模式）
    /// </summary>
    private sealed class LazyPagers : ScriptObject, IEnumerable<ScriptObject>, IList<ScriptObject>
    {
        private readonly IReadOnlyList<PaginatorView> _pagers;
        private List<ScriptObject>? _items;
        private readonly object _lock = new();

        public LazyPagers(IReadOnlyList<PaginatorView> pagers)
        {
            _pagers = pagers;
            SetValue("count", pagers.Count, false);
            SetValue("length", pagers.Count, false);
            SetValue("size", pagers.Count, false);
            SetValue("Count", pagers.Count, false);
            SetValue("Length", pagers.Count, false);
        }

        int ICollection<ScriptObject>.Count => _pagers.Count;
        bool ICollection<ScriptObject>.IsReadOnly => true;

        public ScriptObject this[int index]
        {
            get => GetItems()[index];
            set => throw new NotSupportedException();
        }

        public int IndexOf(ScriptObject item) => GetItems().IndexOf(item);
        public bool Contains(ScriptObject item) => GetItems().Contains(item);
        public void CopyTo(ScriptObject[] array, int arrayIndex) => GetItems().CopyTo(array, arrayIndex);
        void ICollection<ScriptObject>.Add(ScriptObject item) => throw new NotSupportedException();
        void ICollection<ScriptObject>.Clear() => throw new NotSupportedException();
        bool ICollection<ScriptObject>.Remove(ScriptObject item) => throw new NotSupportedException();
        public void Insert(int index, ScriptObject item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();

        private List<ScriptObject> GetItems()
        {
            if (_items is not null)
            {
                return _items;
            }
            lock (_lock)
            {
                return _items ??= _pagers.Select(p => (ScriptObject)new PagerObject(p)).ToList();
            }
        }

        public new IEnumerator<ScriptObject> GetEnumerator() => GetItems().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetItems().GetEnumerator();

        public override bool TryGetValue(Scriban.TemplateContext? context, SourceSpan span, string member, out object? value)
        {
            if (int.TryParse(member, out var index) && index >= 0 && index < _pagers.Count)
            {
                value = GetItems()[index];
                return true;
            }
            switch (member.ToLowerInvariant())
            {
                case "first":
                    value = _pagers.Count > 0 ? GetItems()[0] : null;
                    return true;
                case "last":
                    value = _pagers.Count > 0 ? GetItems()[^1] : null;
                    return true;
            }
            return base.TryGetValue(context, span, member, out value);
        }
    }

    private static ScriptObject CreateMenuItemObject(FlintMenuItem item)
    {
        return new ScriptObject
        {
            ["name"] = item.Name,
            ["url"] = item.URL,
            ["weight"] = item.Weight,
            ["identifier"] = item.Identifier,
            ["parent"] = item.Parent,
            ["pre"] = item.Pre,
            ["post"] = item.Post,
            ["children"] = item.Children.Select(CreateMenuItemObject).ToList(),
            ["has_children"] = item.HasChildren,
            ["is_active"] = item.IsActive,

            // Hugo 兼容
            ["Name"] = item.Name,
            ["URL"] = item.URL,
            ["Weight"] = item.Weight,
            ["Identifier"] = item.Identifier,
            ["Parent"] = item.Parent,
            ["Pre"] = item.Pre,
            ["Post"] = item.Post,
            ["Children"] = item.Children.Select(CreateMenuItemObject).ToList(),
            ["HasChildren"] = item.HasChildren,
            ["IsActive"] = item.IsActive,
        };
    }

    /// <summary>
    /// 注册自定义日期对象，支持 DateTimeOffset 类型
    /// 覆盖 Scriban 内置的 date 对象
    /// </summary>
    private static void RegisterDateObject(ScriptObject dateObject)
    {
        // Scriban 7 起 Import 标注 RequiresUnreferencedCode（反射创建
        // DynamicCustomFunction）；Import 目标是本方法显式引用的 lambda，
        // linker 不会裁剪被引用成员——运行时安全，方法级压制
#pragma warning disable IL2026
        // to_string - 格式化日期，支持 DateTimeOffset
        // 形参用 params：Hugo 的 time.Format/dateFormat 在部分应用形态下只给格式串
        //（`{{ date.to_string $config }}`），严格双参形参会报
        // "Invalid number of arguments 1 ... expecting 2"（doit 21 处实测）
        dateObject.Import("to_string", (params object?[] a) =>
        {
            var date = a.Length > 0 ? a[0] : null;
            var format = a.Length > 1 ? a[1]?.ToString() : null;
            var dt = ConvertToDateTimeOffset(date);
            if (dt == null)
                return "";

            return dt.Value.ToString(
                string.IsNullOrEmpty(format) ? "yyyy-MM-dd" : format,
                System.Globalization.CultureInfo.InvariantCulture);
        });

        // now - 当前时间
        dateObject.Import("now", () => DateTimeOffset.Now);

        // parse - 解析日期字符串
        dateObject.Import("parse", (string? s) =>
        {
            if (DateTimeOffset.TryParse(s, out var result))
                return result;
            return DateTimeOffset.MinValue;
        });

        // add_days - 添加天数
        dateObject.Import("add_days", (object? date, int days) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddDays(days);
        });

        // add_months - 添加月数
        dateObject.Import("add_months", (object? date, int months) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddMonths(months);
        });

        // add_years - 添加年数
        dateObject.Import("add_years", (object? date, int years) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            return dt?.AddYears(years);
        });
#pragma warning restore IL2026
    }

    /// <summary>
    /// 将各种日期类型转换为 DateTimeOffset
    /// </summary>
    private static DateTimeOffset? ConvertToDateTimeOffset(object? value)
    {
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt),
            string s when DateTimeOffset.TryParse(s, out var result) => result,
            long unix => DateTimeOffset.FromUnixTimeSeconds(unix),
            _ => null
        };
    }
}
