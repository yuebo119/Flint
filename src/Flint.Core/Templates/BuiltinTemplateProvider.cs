// Flint 静态站点生成器
// 内置模板提供者抽象（FLUID-MIGRATION-PLAN P2-1）：内置回退模板（Hugo embedded
// templates 的等价物）从 Scriban 渲染器类里拆出，Fluid/Scriban 各持一套实现，
// 两条引擎热路径不共享内置模板字典。

namespace Flint.Core.Templates;

/// <summary>
/// 内置模板提供者：按逻辑名取内置模板内容（无物理文件）。
/// Scriban 与 Fluid 各一套实现，查找方（渲染器/router）不直接依赖具体引擎类
/// </summary>
public interface IBuiltinTemplateProvider
{
    /// <summary>取内置模板内容；不存在返回 false</summary>
    bool TryGetTemplate(string name, out string content);

    /// <summary>全部内置模板名（诊断/测试用）</summary>
    IReadOnlyCollection<string> Names { get; }
}

/// <summary>
/// Scriban 侧内置模板提供者（P2-1）：包住现有
/// <see cref="ScribanTemplateRenderer.BuiltinTemplates"/>（Hugo embedded
/// templates 的 Scriban 等价）。内容字典仍是单一事实源，本类只做接口适配——
/// FileTemplateLoader 与渲染器经本提供者取内置模板，不再直接依赖渲染器类的
/// 静态字典
/// </summary>
public sealed class ScribanBuiltinTemplateProvider : IBuiltinTemplateProvider
{
    /// <summary>共享单例（内置内容静态不可变，全部实例等价）</summary>
    public static ScribanBuiltinTemplateProvider Default { get; } = new();

    public bool TryGetTemplate(string name, out string content) =>
        ScribanTemplateRenderer.BuiltinTemplates.TryGetValue(name, out content!);

    public IReadOnlyCollection<string> Names => ScribanTemplateRenderer.BuiltinTemplates.Keys;
}

/// <summary>
/// Fluid 侧内置模板提供者：新站点（Fluid 原生）缺布局时的 Liquid 版兜底。
/// 内容与 <see cref="ScribanBuiltinTemplateProvider"/> 逐条同构（对齐 Hugo
/// embedded templates 的最小集；语义对比测试见 EngineBuiltinParityTests）：
/// list/term 遍历 pages（词条/列表页页面集合，未设置时回落全站
/// regular_pages——绑定规则见 FluidTemplateRenderer 的 pages 全局变量），
/// taxonomy 遍历 site.regular_pages，pagination 为 Hugo 内置分页导航的
/// Liquid 等价（Pager 字段由 PaginatorView 对象提供）；
/// opengraph/schema/twitter_cards/_funcs/get-page-images 为 Hugo 内置
/// partial 的 Liquid 等价（P3-1/P3-2）
/// </summary>
public sealed class FluidBuiltinTemplateProvider : IBuiltinTemplateProvider
{
    private readonly Dictionary<string, string> _templates;

    public FluidBuiltinTemplateProvider()
    {
        _templates = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["taxonomy"] = "<ul>{% for p in site.regular_pages %}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{% endfor %}</ul>",
            // term 用 pages（词条页面集合，渲染期未设置时回落全站 regular_pages）
            ["term"] = "<ul>{% for p in pages %}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{% endfor %}</ul>",
            ["list"] = "<ul>{% for p in pages %}<li><a href=\"{{ p.permalink }}\">{{ p.title }}</a></li>{% endfor %}</ul>",
            ["pagination"] = BuildPaginationTemplate(),

            // ---- Hugo embedded partials（P3-1：Liquid 等价）----
            // opengraph：空值不输出标签（与 Scriban 内置版同口径）；
            // locale 链 page.params.locale → site.language（Scriban 的
            // site.language.locale 在 Fluid 侧即 site.language 语言码本身）；
            // 日期经 %:z 保留 +08:00 形态（对齐 Scriban 的 zzz 格式）
            ["opengraph"] = """
                <meta property="og:url" content="{{ page.permalink }}">
                {% if site.title %}<meta property="og:site_name" content="{{ site.title }}">{% endif %}
                {% if page.title %}<meta property="og:title" content="{{ page.title }}">{% endif %}
                {% assign __desc = page.description | default: page.summary | default: site.params.description %}{% if __desc %}<meta property="og:description" content="{{ __desc }}">{% endif %}
                {% assign __locale = page.params.locale | default: site.language %}{% if __locale %}<meta property="og:locale" content="{{ __locale }}">{% endif %}
                <meta property="og:type" content="{% if is_home %}website{% else %}article{% endif %}">
                {% if is_single %}{% if page.date %}<meta property="article:published_time" content="{{ page.date | date: '%Y-%m-%dT%H:%M:%S%:z' }}">{% endif %}
                {% if page.lastmod %}<meta property="article:modified_time" content="{{ page.lastmod | date: '%Y-%m-%dT%H:%M:%S%:z' }}">{% endif %}
                {% if page.section %}<meta property="article:section" content="{{ page.section }}">{% endif %}
                {% for __t in page.tags %}<meta property="article:tag" content="{{ __t }}">{% endfor %}{% endif %}
                """,
            // schema：Hugo v0.166 实测产物是 itemprop meta（非 JSON-LD）；
            // description 无条件输出（空值也输出标签，与 Scriban 版同口径）
            ["schema"] = """
                <meta itemprop="name" content="{{ page.title }}">
                <meta itemprop="description" content="{{ page.description | default: page.summary | default: site.params.description }}">
                {% if page.publish_date %}<meta itemprop="datePublished" content="{{ page.publish_date | date: '%Y-%m-%dT%H:%M:%S%:z' }}">{% endif %}
                {% if page.lastmod %}<meta itemprop="dateModified" content="{{ page.lastmod | date: '%Y-%m-%dT%H:%M:%S%:z' }}">{% endif %}
                {% if page.word_count %}<meta itemprop="wordCount" content="{{ page.word_count }}">{% endif %}
                """,
            // twitter_cards：images 非空 → summary_large_image + 逐图 abs_url
            ["twitter_cards"] = """
                {% if page.params.images %}<meta name="twitter:card" content="summary_large_image">{% for __img in page.params.images %}<meta name="twitter:image" content="{{ __img | abs_url }}">{% endfor %}{% else %}<meta name="twitter:card" content="summary">{% endif %}
                {% if page.title %}<meta name="twitter:title" content="{{ page.title }}">{% endif %}
                {% assign __tdesc = page.description | default: page.summary %}{% if __tdesc %}<meta name="twitter:description" content="{{ __tdesc }}">{% endif %}
                """,
            ["google_analytics"] = "",
            ["disqus"] = "",
            // _funcs/get-page-images（P3-2）：include 共享作用域，经 assign 把
            // 结果写回调用方变量 page_images（Liquid 的"返回值通道"）。
            // **已知语义差异**（登记 FLINT-VS-HUGO/迁移差异清单）：Liquid 无
            // 对象字面量，本版赋**原始图片路径数组**（front matter images），
            // 调用方须自行 abs_url；Scriban 版经 partialValue 返回
            // {Permalink, RelPermalink} 对象。 richer 对象通道归 Phase 4-5
            ["_funcs/get-page-images"] = """
                {% if page.params.images %}{% assign page_images = page.params.images %}{% endif %}
                """
        };
    }

    /// <summary>
    /// Hugo 内置 pagination（default 格式）的 Liquid 翻译。读 <c>page.paginator</c>
    /// 而不是全局 <c>paginator</c>（与 Scriban 内置版同口径：模板 .Paginate 实际
    /// 创建的分页器才是该页的分页器）。5 槽页码窗口居中于当前页，首末页与
    /// 前后页按条件渲染。算术经 plus/minus filter（Liquid 无中辍运算符），
    /// 区间循环经变量区间 (start..finish)
    /// </summary>
    private static string BuildPaginationTemplate()
    {
        return """
        {% if page.paginator and page.paginator.total_pages > 1 %}
        <ul class="pagination pagination-default">
        {% if page.paginator.first and page.paginator.page_number != page.paginator.first.page_number %}<li class="page-item"><a href="{{ page.paginator.first.url }}" aria-label="First" class="page-link" role="button"><span aria-hidden="true">&laquo;&laquo;</span></a></li>{% else %}<li class="page-item disabled"><a aria-disabled="true" aria-label="First" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&laquo;&laquo;</span></a></li>{% endif %}
        {% if page.paginator.prev %}<li class="page-item"><a href="{{ page.paginator.prev.url }}" aria-label="Previous" class="page-link" role="button"><span aria-hidden="true">&laquo;</span></a></li>{% else %}<li class="page-item disabled"><a aria-disabled="true" aria-label="Previous" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&laquo;</span></a></li>{% endif %}
        {% assign slots = 5 %}{% assign start = page.paginator.page_number | minus: 2 %}{% if start < 1 %}{% assign start = 1 %}{% endif %}{% assign finish = start | plus: slots | minus: 1 %}{% if finish > page.paginator.total_pages %}{% assign finish = page.paginator.total_pages %}{% endif %}{% assign gap = finish | minus: start %}{% if gap < 4 %}{% assign start = finish | minus: 4 %}{% if start < 1 %}{% assign start = 1 %}{% endif %}{% endif %}
        {% for k in (start..finish) %}{% if page.paginator.page_number == k %}<li class="page-item active"><a aria-current="page" aria-label="Page {{ k }}" class="page-link" role="button">{{ k }}</a></li>{% else %}{% assign idx = k | minus: 1 %}<li class="page-item"><a href="{{ page.paginator.pagers[idx].url }}" aria-label="Page {{ k }}" class="page-link" role="button">{{ k }}</a></li>{% endif %}{% endfor %}
        {% if page.paginator.next %}<li class="page-item"><a href="{{ page.paginator.next.url }}" aria-label="Next" class="page-link" role="button"><span aria-hidden="true">&raquo;</span></a></li>{% else %}<li class="page-item disabled"><a aria-disabled="true" aria-label="Next" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&raquo;</span></a></li>{% endif %}
        {% if page.paginator.last and page.paginator.page_number != page.paginator.last.page_number %}<li class="page-item"><a href="{{ page.paginator.last.url }}" aria-label="Last" class="page-link" role="button"><span aria-hidden="true">&raquo;&raquo;</span></a></li>{% else %}<li class="page-item disabled"><a aria-disabled="true" aria-label="Last" class="page-link" role="button" tabindex="-1"><span aria-hidden="true">&raquo;&raquo;</span></a></li>{% endif %}
        </ul>
        {% endif %}
        """;
    }

    public bool TryGetTemplate(string name, out string content) =>
        _templates.TryGetValue(name, out content!);

    public IReadOnlyCollection<string> Names => _templates.Keys;
}
