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

    private static ScriptObject CreatePageObject(FlintPageContext page)
    {
        return SharedPageObjects.GetValue(page, static p => new LazyPageObject(p));
    }

    /// <summary>
    /// 惰性页面对象：常规键构造时直接绑定；高成本的 prev/next 递归页对象延迟到
    /// 模板实际访问时构建（默认主题不访问 prev/next——万页构建可省 2×N 次全键
    /// 构建），并经 SharedPageObjects 复用。TryGetValue 只读不回写（并发渲染下
    /// ScriptObject 的写入非线程安全），全部可变状态在构造期完成
    /// </summary>
    private sealed class LazyPageObject : ScriptObject
    {
        private readonly FlintPageContext _page;
        private readonly object? _pagesValue;
        private readonly object? _termsValue;
        private readonly object? _paginatorValue;

        public LazyPageObject(FlintPageContext page)
        {
            _page = page;
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
            SetValue("reading_time", page.ReadingTime, false);
            SetValue("description", page.Description, false);
            SetValue("summary", page.Summary, false);
            SetValue("type", page.Type, false);
            SetValue("layout", page.Layout, false);
            SetValue("draft", page.Draft, false);
            SetValue("weight", page.Weight, false);
            SetValue("params", page.Params, false);
            SetValue("resources", page.Resources, false);
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
            var pageData = BuildPageDataObject(page);
            if (pageData is not null)
            {
                SetValue("data", pageData, false);
                SetValue("Data", pageData, false);
            }

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
            SetValue("ReadingTime", page.ReadingTime, false);
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
            return base.TryGetValue(context, span, member, out value);
        }

        /// <summary>
        /// 包装的页面上下文（C4 视图接收者）：render "view" &lt;page&gt; 需要从
        /// 模板传入的页面对象反查真实 PageContext 作为渲染上下文
        /// </summary>
        internal FlintPageContext PageContext => _page;
    }

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

        // 依赖跟踪站点对象：site.* 成员访问被记录为 data:site.* 依赖键（T4.1）
        return new DependencyTrackingScriptObject
        {
            ["title"] = site.Title,
            ["base_url"] = site.BaseURL,
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
            // 设置 count/length 属性，这些是常用的且不需要转换所有页面
            SetValue("count", pages.Count, false);
            SetValue("length", pages.Count, false);
            SetValue("size", pages.Count, false);
            SetValue("Count", pages.Count, false);
            SetValue("Length", pages.Count, false);
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
                return _convertedPages ??= _pages.Select(CreatePageObject).ToList();
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
        dateObject.Import("to_string", (object? date, string? format) =>
        {
            var dt = ConvertToDateTimeOffset(date);
            if (dt == null)
                return "";

            // 使用 .NET 标准格式化
            return dt.Value.ToString(format ?? "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
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
