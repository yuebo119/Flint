// Flint 静态站点生成器
// 模板渲染器接口

using Flint.Core.Configuration;

namespace Flint.Core.Abstractions;

/// <summary>
/// 模板渲染器接口
/// </summary>
public interface ITemplateRenderer
{
    /// <summary>
    /// 异步渲染模板
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <param name="context">模板上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>渲染后的 HTML</returns>
    ValueTask<string> RenderAsync(
        string templateName,
        TemplateContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查模板是否存在
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <returns>是否存在</returns>
    bool TemplateExists(string templateName);

    /// <summary>
    /// 获取模板的依赖列表
    /// </summary>
    /// <param name="templateName">模板名称</param>
    /// <returns>依赖的模板名称列表</returns>
    IReadOnlyList<string> GetDependencies(string templateName);

    /// <summary>
    /// 预编译所有模板（可选优化：首次构建时把模板加载进内存）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>预编译的模板数量</returns>
    Task<int> PrecompileTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空模板 mtime 短窗缓存（构建边界调用，保证每次构建看到模板最新状态）
    /// </summary>
    void InvalidateMtimeCache();
}

/// <summary>
/// 模板上下文
/// </summary>
public sealed class TemplateContext
{
    /// <summary>
    /// 页面上下文
    /// </summary>
    public required PageContext Page { get; init; }

    /// <summary>
    /// 站点上下文
    /// </summary>
    public required SiteContext Site { get; init; }

    /// <summary>
    /// 自定义参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 数据文件内容
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 当前语言
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// 是否为首页
    /// </summary>
    public bool IsHome { get; init; }

    /// <summary>
    /// 是否为列表页
    /// </summary>
    public bool IsList { get; init; }

    /// <summary>
    /// 是否为单页
    /// </summary>
    public bool IsSingle { get; init; }

    /// <summary>
    /// 当前页面集合（Hugo <c>.Pages</c> 语义）：term 页为该词条的页面列表；
    /// null 时模板变量 <c>pages</c> 回落为全站 regular_pages（旧行为）
    /// </summary>
    public IReadOnlyList<PageContext>? Pages { get; init; }

    /// <summary>
    /// 渲染期依赖收集结果（T4.1）：渲染完成后由渲染器填充——
    /// 实际 include 的模板物理路径与 <c>data:site.*</c> 数据访问键
    /// </summary>
    public IReadOnlySet<string>? RenderedDependencies { get; set; }
}

/// <summary>
/// 页面上下文
/// </summary>
public sealed class PageContext
{
    /// <summary>
    /// 页面标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 本次渲染的输出格式（html/rss/json，对齐 Hugo per-output-format）；
    /// 同一页面的不同格式实例共享页面不变部分（含 Markdown 转换结果）
    /// </summary>
    public string OutputFormat { get; init; } = "html";

    /// <summary>
    /// 页面内容（HTML）
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 永久链接（绝对 URL）
    /// </summary>
    public required string Permalink { get; init; }

    /// <summary>
    /// 相对永久链接
    /// </summary>
    public required string RelPermalink { get; init; }

    /// <summary>
    /// 发布日期
    /// </summary>
    public required DateTimeOffset Date { get; init; }

    /// <summary>
    /// 最后修改日期
    /// </summary>
    public DateTimeOffset? LastMod { get; init; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>
    /// 分类列表
    /// </summary>
    public required IReadOnlyList<string> Categories { get; init; }

    /// <summary>
    /// 字数统计
    /// </summary>
    public required int WordCount { get; init; }

    /// <summary>
    /// 阅读时间
    /// </summary>
    public required TimeSpan ReadingTime { get; init; }

    /// <summary>
    /// 页面描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 页面摘要
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 上一页
    /// </summary>
    public PageContext? PrevPage { get; init; }

    /// <summary>
    /// 下一页
    /// </summary>
    public PageContext? NextPage { get; init; }

    /// <summary>
    /// 页面类型
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// 页面布局
    /// </summary>
    public string? Layout { get; init; }

    /// <summary>
    /// 页面输出格式（对齐 Hugo 页面级 outputs 覆盖）：空 = 按 kind 默认（html）。
    /// 非 html 格式（如 json）需存在对应输出格式变体模板（如 single.json.html）
    /// 才会产出
    /// </summary>
    public IReadOnlyList<string> Outputs { get; init; } = [];

    /// <summary>
    /// 源内容文件路径（用于增量构建的依赖追踪）
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// 是否为草稿
    /// </summary>
    public bool Draft { get; init; }

    /// <summary>
    /// 页面权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 当前页面集合（Hugo <c>.Pages</c> 语义）：term 页为该词条下的页面列表；
    /// 其余页面 null——模板 page.pages 无值时应回落 site.regular_pages
    /// </summary>
    public IReadOnlyList<PageContext>? Pages { get; init; }

    /// <summary>
    /// taxonomy 列表页的词条集合（对齐 Hugo <c>.Data.Terms</c>）：仅 taxonomy 页
    /// 非 null，供模板 page.terms 枚举词条（默认主题模板 page.pages/page.terms
    /// 依赖此二者，缺省时词条/分类页渲染为空列表）
    /// </summary>
    public IReadOnlyList<TaxonomyTerm>? Terms { get; init; }

    /// <summary>
    /// 页面 front matter 声明的菜单条目（menu 标识 → 条目配置）：
    /// 供 MenuBuilder 汇入全站菜单；URL 缺省时由页面自身 RelPermalink 补齐
    /// </summary>
    public IReadOnlyDictionary<string, MenuItemConfig>? MenuEntries { get; init; }

    /// <summary>
    /// 自定义参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 页面资源
    /// </summary>
    public IReadOnlyList<object> Resources { get; init; } = [];

    /// <summary>
    /// 目录（标题列表）
    /// </summary>
    public IReadOnlyList<object> TableOfContents { get; init; } = [];

    /// <summary>
    /// 纯文本内容
    /// </summary>
    public string? Plain { get; init; }

    /// <summary>
    /// 原始 Markdown 内容
    /// </summary>
    public string? RawContent { get; init; }
}

/// <summary>
/// 站点上下文
/// </summary>
public sealed class SiteContext
{
    /// <summary>
    /// 站点标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 基础 URL
    /// </summary>
    public required string BaseURL { get; init; }

    /// <summary>
    /// 语言代码
    /// </summary>
    public required string Language { get; init; }

    /// <summary>
    /// 所有页面
    /// </summary>
    public required IReadOnlyList<PageContext> Pages { get; init; }

    /// <summary>
    /// 常规页面（不含列表页）
    /// </summary>
    public required IReadOnlyList<PageContext> RegularPages { get; init; }

    /// <summary>
    /// 分类系统
    /// </summary>
    public required TaxonomyCollection Taxonomies { get; init; }

    /// <summary>
    /// 菜单集合
    /// </summary>
    public required MenuCollection Menus { get; init; }

    /// <summary>
    /// 站点配置
    /// </summary>
    public required SiteConfig Config { get; init; }

    /// <summary>
    /// 数据文件
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 站点参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 构建时间
    /// </summary>
    public DateTimeOffset BuildDate { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTimeOffset? LastChange { get; init; }

    /// <summary>
    /// 是否为多语言站点
    /// </summary>
    public bool IsMultiLingual { get; init; }

    /// <summary>
    /// 可用语言列表
    /// </summary>
    public IReadOnlyList<string> Languages { get; init; } = [];
}

/// <summary>
/// 分类系统集合
/// </summary>
public sealed class TaxonomyCollection
{
    /// <summary>
    /// 分类字典
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<TaxonomyTerm>> Taxonomies { get; init; }

    /// <summary>
    /// 获取指定分类的所有术语
    /// </summary>
    public IReadOnlyList<TaxonomyTerm> this[string taxonomy] =>
        Taxonomies.TryGetValue(taxonomy, out var terms) ? terms : [];
}

/// <summary>
/// 分类术语
/// </summary>
public sealed class TaxonomyTerm
{
    /// <summary>
    /// 术语名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// URL 友好的名称
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// 关联的页面列表
    /// </summary>
    public required IReadOnlyList<PageContext> Pages { get; init; }

    /// <summary>
    /// 页面数量
    /// </summary>
    public int Count => Pages.Count;

    /// <summary>
    /// 永久链接
    /// </summary>
    public string? Permalink { get; init; }
}

/// <summary>
/// 菜单集合
/// </summary>
public sealed class MenuCollection
{
    /// <summary>
    /// 菜单字典
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<MenuItem>> Menus { get; init; }

    /// <summary>
    /// 获取指定名称的菜单
    /// </summary>
    public IReadOnlyList<MenuItem> this[string name] =>
        Menus.TryGetValue(name, out var items) ? items : [];
}

/// <summary>
/// 菜单项
/// </summary>
public sealed record MenuItem
{
    /// <summary>
    /// 菜单项名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 菜单项 URL
    /// </summary>
    public required string URL { get; init; }

    /// <summary>
    /// 菜单项权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 菜单项标识
    /// </summary>
    public string? Identifier { get; init; }

    /// <summary>
    /// 父菜单项标识
    /// </summary>
    public string? Parent { get; init; }

    /// <summary>
    /// 前置内容
    /// </summary>
    public string? Pre { get; init; }

    /// <summary>
    /// 后置内容
    /// </summary>
    public string? Post { get; init; }

    /// <summary>
    /// 子菜单项
    /// </summary>
    public IReadOnlyList<MenuItem> Children { get; init; } = [];

    /// <summary>
    /// 是否有子菜单
    /// </summary>
    public bool HasChildren => Children.Count > 0;

    /// <summary>
    /// 是否为当前活动项
    /// </summary>
    public bool IsActive { get; init; }
}
