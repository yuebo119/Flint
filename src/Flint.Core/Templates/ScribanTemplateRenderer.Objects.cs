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

        public LazyPageObject(FlintPageContext page)
        {
            _page = page;
            _pagesValue = page.Pages is not null ? GetSharedPageList(page.Pages) : null;
            _termsValue = page.Terms is not null
                ? page.Terms.Select(t => (object)new LazyTaxonomyTerm(t)).ToList()
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
            SetValue("table_of_contents", page.TableOfContents, false);
            SetValue("plain", page.Plain, false);
            SetValue("raw_content", page.RawContent, false);

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
    }

    private static ScriptObject CreateSiteObject(FlintSiteContext site)
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

    private static ScriptObject CreateMenusObject(FlintMenuCollection menus)
    {
        var obj = new ScriptObject();
        foreach (var (name, items) in menus.Menus)
        {
            obj[name] = items.Select(CreateMenuItemObject).ToList();
        }
        return obj;
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
