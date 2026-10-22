// Flint 静态站点生成器
// Fluid（Liquid）模板渲染器（FLUID-MIGRATION-PLAN P1-1~P1-5）：新站点主力引擎，
// 与 ScribanTemplateRenderer 平行实现 ITemplateRenderer，不共享缓存/依赖表/
// 资源追踪器（隔离硬规则见 docs/CONVENTIONS.md §8.4.2）。
//
// 第一版能力边界（方案 §二.3）：
// - 模板加载/查找/缓存/mtime 失效（.liquid 扩展名，主题回退同序）
// - 渲染期依赖追踪（顶层模板 + include 实际命中的物理路径）
// - 模型直绑 PageContext/SiteContext/Params（snake_case 成员映射 +
//   Params 用 OrdinalIgnoreCase 字典）
// - partial/include 的 Liquid 版（Fluid include/render 标签 → Flint 模板加载器）
// - GeneratedResources 返回空（资源函数 Phase 4 再接）

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Fluid;
using FlintTemplateContext = Flint.Core.Abstractions.TemplateContext;
using FluidTemplateContext = Fluid.TemplateContext;

namespace Flint.Core.Templates;

/// <summary>
/// 基于 Fluid 的 Liquid 模板渲染器：只认 <c>.liquid</c> 物理模板，
/// Hugo 主题 <c>.html</c> 模板由 Scriban 渲染器负责（路由见 TemplateEngineRouter）。
/// 内容管线两接口（IShortcodeRenderer/IRenderHookRenderer）与 Scriban 侧对称
/// 实现（2026-10-22 接口对齐）：裸渲染器用法（测试/非 router 消费方）不再
/// 静默丢失 .liquid 短代码与钩子；只认 .liquid 原生形态
/// </summary>
public sealed partial class FluidTemplateRenderer : ITemplateRenderer, IShortcodeRenderer, IRenderHookRenderer
{
    /// <summary>缓存条目：编译结果 + 源文件路径 + mtime（增量/长驻渲染的失效依据）</summary>
    private sealed record CachedTemplate(IFluidTemplate Template, string? SourcePath, DateTime ModifiedTimeUtc);

    private readonly string _templatesPath;
    // 主题布局回退目录（与 Scriban 侧同序：站点优先、主题按序回退）
    private readonly string[] _themeTemplatePaths;
    // 站点基础 URL（构造期给定）：CreateFluidContext 里 Site.BaseURL 为
    // 空时的 AmbientValues 回落（真实渲染 Site.BaseURL 恒有值）
    private readonly string _baseUrl;
    private readonly TemplateLookup _lookup;
    private readonly FluidParser _parser;
    private readonly TemplateOptions _options;
    private readonly IBuiltinTemplateProvider? _builtins;
    // 模板资源提供者（Hugo Pipes）：路径形态的资源过滤器经 AmbientValues 传给
    // 每渲染上下文（实例字段是来源，见 CreateFluidContext）
    private readonly ITemplateResourceProvider? _resources;
    private readonly ConcurrentDictionary<string, CachedTemplate> _templateCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _dependencyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// include 路径解析缓存（对齐 Scriban 侧 FileTemplateLoader._pathCache）：
    /// 键 = include 名（解析结果对名字与模板文件集确定，构建内不变）。
    /// 此前每次 include 渲染都跑 roots × forms 的 File.Exists 循环——
    /// 1000 页 × 5 include × 8 候选 = 4 万次 stat（2026-10-21 审计修复）
    /// </summary>
    private readonly ConcurrentDictionary<string, string?> _includePathCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// include 模板文本缓存（对齐 FileTemplateLoader._textCache）：
    /// 键 = 物理路径，值 = (文本, mtime, length)。mtime+length 双字段验效
    /// （同时间粒度改写时 mtime 可能不前进，length 作第二判据）；
    /// 保留 serve 模式热更新语义。此前每次 include 渲染都全量读盘
    /// </summary>
    private readonly ConcurrentDictionary<string, (string Text, DateTime MtimeUtc, long Length)> _includeTextCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// include/render 的模板解析缓存（ITemplateCache 实现，2026-10-21）：
    /// 键 = 物理路径，值 = (模板, mtime)。Fluid 的 TemplateLoader 经
    /// TemplateOptions.TemplateCache 查询——命中即免重复 parser.TryParse。
    /// mtime 不符即失效（与 _includeTextCache 同口径的 serve 热更新语义）
    /// </summary>
    private readonly IncludeTemplateCache _includeTemplateCache = new();

    /// <summary>AmbientValues 键：本次渲染收集的依赖物理路径集合（随 TemplateContext 生命周期）</summary>
    private const string DepsKey = "__flint_fluid_deps";

    /// <summary>mtime 短窗缓存（与 Scriban 渲染器同口径：NTFS 元数据查询每次 20-50μs）</summary>
    private readonly ConcurrentDictionary<string, (DateTime CachedAtUtc, DateTime MtimeUtc)> _mtimeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan MtimeCacheTtl = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 创建 Fluid 模板渲染器
    /// </summary>
    /// <param name="templatesPath">模板目录路径（站点 layouts）</param>
    /// <param name="baseUrl">站点基础 URL（abs_url/rel_url 过滤器用）</param>
    /// <param name="builtins">内置模板提供者（缺省回退模板的 Liquid 版）；null = 无内置兜底</param>
    /// <param name="resources">模板资源提供者（Hugo Pipes，resources.Get/Match/ByType 过滤器）；
    /// null = 资源过滤器返回空</param>
    /// <param name="themeTemplatePaths">主题布局回退目录（按序回退）</param>
    // NativeAOT 保留清单：Fluid 的 DefaultMemberAccessStrategy 经反射
    // （Type.GetProperty）解析模型成员，裁剪后属性元数据缺失时**静默失败**
    // （模板成员全部取空、结构正常——AOT 冒烟实证，2026-10-19）。对模型类型
    // 逐一保留 PublicProperties 是精准解；整程序集 TrimmerRootAssembly 是
    // 粗放解（体积极小化优先，本清单随模型类型演进而维护）
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(PageContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(SiteContext))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(PaginatorView))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(PaginatorPageGroup))]
    // 分组分页边界（paginator.group_boundaries 的条目）：record struct，
    // 属性元数据同样会被裁剪（2026-10-21 审计补入）
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(PaginatorGroupBoundary))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TaxonomyTerm))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TaxonomyCollection))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(MenuItem))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(MenuCollection))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(MarkdownHeading))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(MenuItemConfig))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(SiteConfig))]
    // TimeSpan/DateTimeOffset：page.reading_time / page.date 等 corelib 成员的
    // 属性元数据同样会被裁剪（AOT 冒烟实证 TotalMinutes 取空）；
    // ReadingTimeView 是 reading_time 的注册访问器返回类型
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(TimeSpan))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(DateTimeOffset))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(ReadingTimeView))]
    // hugo 信息视图（hugo.version / is_production / is_server / environment）：
    // 4 个 getter 无静态引用，裁剪后模板静默取空（2026-10-22 审计补入）
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(HugoInfoView))]
    public FluidTemplateRenderer(
        string templatesPath,
        string baseUrl = "",
        IBuiltinTemplateProvider? builtins = null,
        ITemplateResourceProvider? resources = null,
        params string[] themeTemplatePaths)
        : this(templatesPath, baseUrl, TimeProvider.System, builtins, resources, themeTemplatePaths)
    {
    }

    /// <summary>测试专用构造：时间源可注入</summary>
    public FluidTemplateRenderer(
        string templatesPath,
        string baseUrl,
        TimeProvider timeProvider,
        IBuiltinTemplateProvider? builtins = null,
        ITemplateResourceProvider? resources = null,
        params string[] themeTemplatePaths)
    {
        _templatesPath = templatesPath;
        _themeTemplatePaths = themeTemplatePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        _baseUrl = baseUrl ?? "";
        _timeProvider = timeProvider;
        _builtins = builtins ?? new FluidBuiltinTemplateProvider();
        _resources = resources;
        _lookup = TemplateLookup.Create(templatesPath, "liquid", _themeTemplatePaths);
        // AllowLiquidTag（2026-10-21）：解锁 {% liquid %} 整块标签——Liquid 生态
        // （Jekyll/Eleventy/Shopify 迁移产物）高频使用，Fluid 已完整实现该标签。
        // AllowParentheses（2026-10-22）：解锁括号分组表达式 `{{ (a or b) and c }}`。
        // 注意这是 Fluid 对标准 Liquid 的扩展（Shopify Liquid 条件求值不支持
        // 分组，Hugo Go 模板有自己的管道语法）——区间字面量 (1..3) 本就可用，
        // 此开关只影响条件/赋值表达式的分组求值，LIQUID-GUIDE §4 差异清单登记
        _parser = new FluidParser(new FluidParserOptions
        {
            AllowLiquidTag = true,
            AllowParentheses = true
        });

        // page.reading_time 注册 ReadingTimeView 访问器：Fluid 的
        // FluidValue.Create 会把 TimeSpan 转成 DateTimeValue（纪元+时长），
        // 使 reading_time.total_minutes 取空（探针 + AOT 冒烟实证）——
        // 注册访问器优先于反射，返回保真视图
        var memberStrategy = new FlintMemberAccessStrategy();
        MemberAccessStrategyExtensions.Register<PageContext, ReadingTimeView>(
            memberStrategy, "ReadingTime", static (page, _) => new ReadingTimeView(page.ReadingTime));
        // paginator.total_number_of_elements 别名（2026-10-21：Hugo 分页器
        // 字段名是 TotalNumberOfElements，Flint 暴露 total_items——
        // 注册别名访问器让 Hugo 模板的两种写法都命中）
        MemberAccessStrategyExtensions.Register<PaginatorView, int>(
            memberStrategy, "TotalNumberOfElements", static (p, _) => p.TotalItems);
        // page.next / page.prev 别名（2026-10-21：Hugo 模板用 .Next/.Prev，
        // Flint 暴露 next_page/prev_page——ToPascalCase 不会把 next 映射到
        // NextPage，注册别名让 Hugo 惯用名命中）
        MemberAccessStrategyExtensions.Register<PageContext, object>(
            memberStrategy, "Next", static (page, _) => page.NextPage ?? (object)"");
        MemberAccessStrategyExtensions.Register<PageContext, object>(
            memberStrategy, "Prev", static (page, _) => page.PrevPage ?? (object)"");
        // site.menus.main / site.taxonomies.categories：C# 索引器不能经 Fluid
        // 成员语法访问，注册类型级访问器把成员名路由到索引器（缺失键空集合，
        // 与 Scriban 的 ScriptObject 键查找同语义）
        MemberAccessStrategyExtensions.Register<MenuCollection>(memberStrategy, new IndexerMemberAccessor(
            static (inst, name) => ((MenuCollection)inst)[name]));
        MemberAccessStrategyExtensions.Register<TaxonomyCollection>(memberStrategy, new IndexerMemberAccessor(
            static (inst, name) => ((TaxonomyCollection)inst)[name]));
        // page.params / site.params：上游 Params 字典多为 Ordinal，模板写
        // params.Foo / params.foo 两种形态都应命中——注册访问器返回
        // OrdinalIgnoreCase 副本（Fluid 的字典成员访问走字典自身比较器，
        // ModelNamesComparer 管不了 CLR 字典）
        MemberAccessStrategyExtensions.Register<PageContext, object>(memberStrategy, "Params",
            static (page, _) => CaseInsensitiveCopy(page.Params));
        MemberAccessStrategyExtensions.Register<SiteContext, object>(memberStrategy, "Params",
            static (site, _) => CaseInsensitiveCopy(site.Params));

        _options = new TemplateOptions
        {
            // snake_case 成员映射（page.word_count → WordCount）+ 默认大小写不敏感
            MemberAccessStrategy = memberStrategy,
            // 模型名比较器：OrdinalIgnoreCase（Fluid 默认值，显式声明契约）
            ModelNamesComparer = StringComparer.OrdinalIgnoreCase,
            // include/render 的模板解析委派 Flint 模板加载器（根序 + partials 形态 +
            // 内置回退），不用 Fluid 的文件系统提供者
            FileProvider = new DelegateTemplateFileProvider(ResolveIncludeAsync),
            // include/render 的模板缓存（2026-10-21 性能优化：Fluid beta.8 的
            // TemplateLoader 在 TemplateCache==null 时每次 include 执行都重新
            // parser.TryParse——1000 页 × 5 include = 5000+ 次重复解析。
            // 接入 ITemplateCache 后解析结果按路径缓存，mtime 不符即失效）
            TemplateCache = _includeTemplateCache,
            // 万页站点的列表/taxonomy 循环（与 Scriban 侧同口径放开步数限制）；
            // 递归深度必须有限：0 在 Fluid 语义里等于不限制（TemplateContext 判
            // `MaxRecursion > 0` 才生效），自 include / A→B→A 会直坠不可捕获的
            // StackOverflow 让进程消失。500 层足以覆盖正常嵌套 partial
            MaxRecursion = 500,
            MaxSteps = 0
        };

        // URL 过滤器家族（abs_url/rel_url）：Hugo 内置 partial（twitter_cards）
        // 与 Liquid 模板依赖；语义对齐 Scriban 侧 BuiltinTemplateFunctions。
        // baseUrl 每渲染经 AmbientValues 传入（不用静态——并行多站点竞态）
        FluidTemplateFilters.Register(_options);
        // Hugo 名过滤器族（P4-2）：titleize/humanize/markdownify/delimit/
        // union/i18n 等 Fluid 内置 72 个之外的新站点高频名
        FluidHugoFilters.Register(_options);
        // resources.Get/Match/ByType 的过滤器形态（P4-3 只读档）
        FluidResourceFilters.Register(_options, resources);
    }

    /// <summary>
    /// 页面感知渲染（A 组查找链）：候选链解析 → 物理 <c>.liquid</c> 模板 /
    /// 内置兜底 → 渲染。候选链全部未命中且无内置兜底时抛
    /// <see cref="TemplateNotFoundException"/>（SiteBuilder 据此 Skip/Error）
    /// </summary>
    public async ValueTask<string> RenderPageAsync(
        PageTemplateQuery query,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePageTemplatePath(query);
        if (path is null)
        {
            // 内置兜底（对齐 Hugo embedded templates）：kind → 内置模板名。
            // 仅 html 格式——内置模板是 HTML 形态，格式变体不适用
            var builtinName = BuiltinNameForKind(query.Kind);
            if (builtinName is not null && IsHtmlOutputFormat(query.OutputFormat) &&
                _builtins is not null && _builtins.TryGetTemplate(builtinName, out var builtinContent))
            {
                return await RenderBuiltinAsync(builtinName, builtinContent, context, cancellationToken);
            }

            throw new TemplateNotFoundException(
                DescribePageQuery(query),
                PageTemplateCandidates.Build(query)
                    .SelectMany(level => new[]
                    {
                        level.Name + ".liquid",
                        "_default/" + level.Name + ".liquid"
                    })
                    .ToList());
        }

        var template = await GetOrLoadTemplateAtPathAsync(path, cancellationToken);
        return await RenderLoadedAsync(template, path, path, context, cancellationToken);
    }

    /// <summary>页面感知的物理模板存在性（不含内置兜底）</summary>
    public bool PageTemplateExists(PageTemplateQuery query) => ResolvePageTemplatePath(query) is not null;

    /// <summary>
    /// 站点是否持有任一 <c>.liquid</c> 物理模板（Fluid 原生站点判定）。
    /// 只扫站点 layouts 根（不含主题根——Hugo 主题恒 Scriban）；
    /// 路由层的 auto 内置回退归属用
    /// </summary>
    internal bool HasLiquidTemplates() =>
        Directory.Exists(_templatesPath) &&
        Directory.EnumerateFiles(_templatesPath, "*.liquid", SearchOption.AllDirectories).Any();

    /// <summary>站点模板根（router 的 .html 存在性判定用，P2-4 懒构造决策）</summary>
    internal string TemplatesPath => _templatesPath;

    /// <summary>主题模板回退根（同上）</summary>
    internal IReadOnlyList<string> ThemeTemplatePaths => _themeTemplatePaths;

    /// <summary>
    /// 页面查询能否由本渲染器的**内置兜底**渲染（无物理 .liquid 时的 fallback 判定）：
    /// kind 有对应内置模板且为 html 输出格式。路由层据此决定 auto 模式下
    /// 无物理模板时的归属（Fluid 原生站点走 Fluid 内置，Hugo 主题站点走 Scriban 内置）
    /// </summary>
    internal bool CanRenderPageFallback(PageTemplateQuery query) =>
        BuiltinNameForKind(query.Kind) is { } builtinName &&
        IsHtmlOutputFormat(query.OutputFormat) &&
        _builtins is not null &&
        _builtins.TryGetTemplate(builtinName, out _);

    private string? ResolvePageTemplatePath(PageTemplateQuery query) =>
        _lookup.ResolveLayered(PageTemplateCandidates.Build(query));

    /// <summary>kind → 内置兜底模板名（仅 section/taxonomy/term 有内置；page/home 无）</summary>
    private static string? BuiltinNameForKind(string kind) => kind.ToLowerInvariant() switch
    {
        "section" => "list",
        "taxonomy" => "taxonomy",
        "term" => "term",
        _ => null
    };

    private static bool IsHtmlOutputFormat(string? format) =>
        string.IsNullOrEmpty(format) || format.Equals("html", StringComparison.OrdinalIgnoreCase);

    /// <summary>渲染内置模板（无物理文件路径，缓存键加 builtin: 前缀避免与同名物理模板冲突）</summary>
    private async ValueTask<string> RenderBuiltinAsync(
        string builtinName,
        string builtinContent,
        FlintTemplateContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = "builtin:" + builtinName;
        if (!_templateCache.TryGetValue(cacheKey, out var cached))
        {
            cached = new CachedTemplate(ParseOrThrow(builtinContent, cacheKey), null, DateTime.MinValue);
            _templateCache[cacheKey] = cached;
        }

        return await RenderLoadedAsync(cached.Template, cacheKey, null, context, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<string> RenderAsync(
        string templateName,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        var template = await GetOrLoadTemplateAsync(templateName, cancellationToken);
        var physicalPath = TryResolveTemplatePath(templateName);
        return await RenderLoadedAsync(template, templateName, physicalPath, context, cancellationToken);
    }

    /// <summary>
    /// 渲染指定模板文件（不走模板名查找链，用于 robots.txt 等非 .html 形态辅助模板）。
    /// <c>.liquid</c> 文件由本渲染器渲染；其余扩展名（.html/.txt 等）不在 Fluid
    /// 职责内——路由层保证只把 .liquid 路径发到这里
    /// </summary>
    public async ValueTask<string> RenderTemplateFileAsync(
        string filePath,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        var template = ParseOrThrow(content, filePath);
        return await RenderLoadedAsync(template, filePath, filePath, context, cancellationToken);
    }

    /// <summary>
    /// 直接渲染 Liquid 模板**内容**（无物理文件）：短代码/render hook 等
    /// 内容内联场景（FLUID-MIGRATION-PLAN P5-4/P5-5）。缓存键为调用方给的
    /// 逻辑名（短代码名等），无 mtime 失效语义——内容由调用方生命周期管理。
    /// **走 _templateCache**（2026-10-21 性能优化：此前每次调用都重新
    /// ParseOrThrow——50 张图的文章 = render-image.liquid 解析 50 次；
    /// Scriban 侧对称实现全部解析一次复用）
    /// </summary>
    internal async ValueTask<string> RenderLiquidContentAsync(
        string content,
        string sourceName,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        IFluidTemplate template;
        if (_templateCache.TryGetValue(sourceName, out var cached) && !IsStale(cached))
        {
            template = cached.Template;
        }
        else
        {
            template = ParseOrThrow(content, sourceName);
            _templateCache[sourceName] = new CachedTemplate(template, null, DateTime.MinValue);
        }
        return await RenderLoadedAsync(template, sourceName, null, context, cancellationToken);
    }

    /// <summary>
    /// 共用渲染入口：每渲染独立 TemplateContext（方案 §二.3），依赖追踪
    /// （逻辑名 + 物理路径 + include 实际命中），运行时异常包装
    /// </summary>
    private async ValueTask<string> RenderLoadedAsync(
        IFluidTemplate template,
        string templateName,
        string? physicalPath,
        FlintTemplateContext context,
        CancellationToken cancellationToken)
    {
        var fluidContext = CreateFluidContext(context, cancellationToken);
        var deps = (HashSet<string>)fluidContext.AmbientValues[DepsKey];
        deps.Add(templateName);
        if (physicalPath is not null)
        {
            deps.Add(Path.GetFullPath(physicalPath));
        }

        try
        {
            var result = await template.RenderAsync(fluidContext);
            context.RenderedDependencies = deps;
            return result;
        }
        catch (Exception ex) when (ex is FluidException or ParseException or IOException)
        {
            // 携带模板名，避免裸的 Fluid 内部堆栈直达用户；
            // OperationCanceledException 不在捕获列表——取消必须逃逸
            throw new TemplateRenderException(templateName, ex.Message, innerException: ex);
        }
    }

    /// <summary>
    /// 构造每渲染独立的 Fluid 上下文：模型直绑 PageContext/SiteContext/Params。
    /// Params/Data 复制为 OrdinalIgnoreCase 字典——探针实测 Fluid 的字典成员
    /// 访问走**字典自身比较器**（ModelNamesComparer 只作用于 CLR 对象成员），
    /// 上游 Params 多为 Ordinal 字典，不复制则 params.foo 与 params.Foo 行为不一致
    /// </summary>
    private FluidTemplateContext CreateFluidContext(FlintTemplateContext context, CancellationToken cancellationToken)
    {
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["page"] = context.Page,
            ["site"] = context.Site,
            ["params"] = CaseInsensitiveCopy(context.Params),
            ["data"] = CaseInsensitiveCopy(context.Data),
            // Hugo .Pages 语义：term 页为词条页面列表；未设置时回落全站 regular_pages
            ["pages"] = context.Pages is not null ? context.Pages : context.Site.RegularPages,
            ["language"] = context.Language,
            ["output_format"] = context.Page.OutputFormat,
            ["is_home"] = context.IsHome,
            ["is_list"] = context.IsList,
            ["is_single"] = context.IsSingle,
            // 分页器（C1）：逐页绑定的 pager；非分页渲染为 null（模板取空）
            ["paginator"] = context.Page.Paginator,
            // hugo 常量对象（2026-10-21：Scriban 侧有 hugo.Version/IsProduction/
            // IsServer/Environment，Fluid 侧缺失——主题模板写 hugo.IsProduction
            // 得空。经 AmbientValues 注入，值由渲染器构造期给定）
            ["hugo"] = HugoInfoView.Instance
        };

        // 附加模型值（短代码/render hook 的内联变量表，P5-4/P5-5）：
        // 合并进顶层键；后写覆盖标准键（hook 的 text/level 等不与标准键冲突，
        // 冲突时以内联表为意图方——调用方显式传入优先）
        if (context.ExtraValues is { Count: > 0 } extra)
        {
            foreach (var (key, value) in extra)
            {
                model[key] = value;
            }
        }

        var fluidContext = new FluidTemplateContext(model, _options)
        {
            CancellationToken = cancellationToken
        };
        // URL 过滤器的站点 baseURL（abs_url/rel_url 经 AmbientValues 取）：
        // Site.BaseURL 优先，为空时回落构造期 baseUrl
        var siteBaseUrl = string.IsNullOrEmpty(context.Site.BaseURL) ? _baseUrl : context.Site.BaseURL;
        fluidContext.AmbientValues[FluidTemplateFilters.BaseUrlKey] = siteBaseUrl;
        // 资源/资产变换过滤器（P4-3/P4-4）：baseURL（产物 RelPermalink）、
        // 产物收集队列（GeneratedResources 通道）、资源提供者（路径形态解析）
        fluidContext.AmbientValues[FluidResourceFilters.BaseUrlKey] = siteBaseUrl;
        fluidContext.AmbientValues[FluidResourceFilters.GeneratedKey] = _generatedResources;
        if (_resources is not null)
        {
            fluidContext.AmbientValues[FluidResourceFilters.ProviderKey] = _resources;
        }
        // i18n 过滤器的翻译表（缺键返回空串，对齐 Hugo）
        fluidContext.AmbientValues[FluidHugoFilters.TranslationsKey] = context.Site.Translations;
        // 当前页面 RelPermalink（paginate 过滤器登记分页追踪用，2026-10-21）
        fluidContext.AmbientValues[FluidHugoFilters.CurrentPageRelPermalinkKey] = context.Page.RelPermalink;
        // 页面/站点 params（param 过滤器查找用，2026-10-21：Hugo .Param 语义——
        // 页面参数未命中时回落站点参数）
        fluidContext.AmbientValues[FluidHugoFilters.PageParamsKey] = context.Page.Params;
        fluidContext.AmbientValues[FluidHugoFilters.SiteParamsKey] = context.Site.Params;
        // 依赖收集容器：AmbientValues 随 TemplateContext 生命周期，include 解析
        // 回调经同一 context 写入，并行渲染互不干扰（每渲染独立实例）
        fluidContext.AmbientValues[DepsKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return fluidContext;
    }

    /// <summary>复制为 OrdinalIgnoreCase 字典（源已忽略大小写时原样返回，免复制）</summary>
    internal static object CaseInsensitiveCopy(IReadOnlyDictionary<string, object> source)
    {
        if (source is Dictionary<string, object> dict &&
            dict.Comparer == StringComparer.OrdinalIgnoreCase)
        {
            return dict;
        }

        // 按源字典身份缓存副本（ConditionalWeakTable：源被回收时条目自动清除，
        // 不泄漏）。同一构建内全部页面共享 SiteContext.Params/Data 同一实例，
        // 缓存后每页零复制（此前每渲染复制一次——1000 页 = 1000 次冗余复制，
        // 2026-10-21 审计修复）
        return CiCopyCache.GetValue(source, static s =>
        {
            var copy = new Dictionary<string, object>(s.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in s)
            {
                copy[kv.Key] = kv.Value;
            }
            return copy;
        });
    }

    /// <summary>CI 副本缓存（键 = 源字典引用，值 = OrdinalIgnoreCase 副本）</summary>
    private static readonly ConditionalWeakTable<IReadOnlyDictionary<string, object>, object> CiCopyCache = new();

    /// <summary>
    /// include/render 的模板解析（Fluid FileProvider 回调）：按根序解析
    /// <c>.liquid</c> 物理模板，未命中回退 Fluid 内置模板；解析到的物理路径
    /// 记入本次渲染依赖（增量构建漏重渲防护）
    /// </summary>
    private ValueTask<TemplateSourceInfo> ResolveIncludeAsync(
        string path,
        FluidTemplateContext context,
        CancellationToken cancellationToken)
    {
        var resolved = ResolveIncludePath(path);
        if (resolved is null)
        {
            throw new FileNotFoundException($"Liquid 模板 include 未找到: {path}");
        }

        // 内置模板哨兵无物理文件：不进依赖集合（增量反查只认物理路径），
        // 内容由流解析回调经内置提供者再取
        var isBuiltin = resolved.StartsWith(BuiltinPrefix, StringComparison.Ordinal);
        if (!isBuiltin &&
            context.AmbientValues.TryGetValue(DepsKey, out var value) && value is HashSet<string> deps)
        {
            deps.Add(resolved);
        }

        // mtime 经短窗缓存（GetMtimeUtc）而非直查磁盘：include 每次执行都
        // 经本回调，Fluid beta.8 无每上下文模板缓存，{% for %} 循环内的
        // include 每迭代一次就 stat 一次——1000 页 × 5 include = 5000+ 次
        // stat（2026-10-21 审计修复：此前 File.GetLastWriteTimeUtc 直查）
        var mtimeUtc = isBuiltin
            ? DateTimeOffset.MinValue
            : new DateTimeOffset(GetMtimeUtc(resolved), TimeSpan.Zero);
        return ValueTask.FromResult(new TemplateSourceInfo(
            mtimeUtc,
            token => ReadTemplateStreamAsync(resolved, token),
            resolved));
    }

    private ValueTask<Stream> ReadTemplateStreamAsync(string resolvedPath, CancellationToken cancellationToken)
    {
        if (resolvedPath.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
        {
            var builtinKey = resolvedPath[BuiltinPrefix.Length..];
            if (_builtins is null || !_builtins.TryGetTemplate(builtinKey, out var builtinContent))
            {
                throw new FileNotFoundException($"Fluid 内置模板未找到: {builtinKey}");
            }
            return ValueTask.FromResult<Stream>(EncodeUtf8NoBom(builtinContent));
        }

        return ReadFileStreamAsync(resolvedPath, cancellationToken);
    }

    private async ValueTask<Stream> ReadFileStreamAsync(string path, CancellationToken cancellationToken)
    {
        // 文本缓存：mtime+length 双字段验效（同 Scriban 侧口径）——
        // 命中即免全量读盘（1000 页 × 5 include 的全量读盘是纯浪费）；
        // 未命中或验效失败才读盘并更新缓存。保留 serve 热更新语义
        var info = new FileInfo(path);
        if (_includeTextCache.TryGetValue(path, out var tc) &&
            tc.MtimeUtc == info.LastWriteTimeUtc && tc.Length == info.Length)
        {
            return EncodeUtf8NoBom(tc.Text);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        // 剥 BOM：Fluid 对带头 BOM 的模板文本直接在行首留下不可见字符
        var text = bytes is [0xEF, 0xBB, 0xBF, ..]
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.UTF8.GetString(bytes);
        _includeTextCache[path] = (text, info.LastWriteTimeUtc, info.Length);
        return EncodeUtf8NoBom(text);
    }

    /// <summary>UTF-8 无 BOM 内存流：带头 BOM 的字节剥掉 3 字节前缀，其余原样</summary>
    private static Stream EncodeUtf8NoBom(ReadOnlySpan<byte> bytes) =>
        new MemoryStream(
            bytes is [0xEF, 0xBB, 0xBF, ..] ? bytes[3..].ToArray() : bytes.ToArray(),
            writable: false);

    private static Stream EncodeUtf8NoBom(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text), writable: false);

    /// <summary>
    /// include 名 → 物理路径（根序：站点 → 主题按序）。候选形态对齐 Scriban 侧
    /// <see cref="FileTemplateLoader.ResolvePath"/> 的文件名约定：
    /// 带 partials 前缀的名字只探两个目录形态（partials/ 与 _partials/， Hugo
    /// v0.146+ 新旧约定并存）；裸名探根形态、_default 兜底与两个 partials 目录。
    /// 未命中回退 Fluid 内置模板（哨兵路径形态，内容由解析回调再取一次）
    /// </summary>
    private string? ResolveIncludePath(string templateName)
    {
        // 路径缓存：解析结果对 include 名确定（构建内模板文件集不变，
        // 与 Scriban 侧同口径）；null 也缓存（未命中不重复探测）
        if (_includePathCache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var resolved = ResolveIncludePathUncached(templateName);
        _includePathCache[templateName] = resolved;
        return resolved;
    }

    private string? ResolveIncludePathUncached(string templateName)
    {
        foreach (var root in new[] { _templatesPath }.Concat(_themeTemplatePaths))
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                continue;
            }

            foreach (var form in IncludeCandidateForms(templateName))
            {
                // GetFullPath 归一化分隔符（候选形态用 / 拼接，Windows 上
                // Path.Combine 产出混合分隔符——依赖键必须与物理路径同形态）
                var candidate = Path.GetFullPath(Path.Combine(root, form));
                // 根内校验（对齐 Scriban 侧 FileTemplateLoader）：include 名来自
                // 模板内容，`{% include "../../x" %}` 若不拦会读出模板根之外的文件
                if (!IsUnderRoot(candidate, root))
                {
                    continue;
                }
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        // 内置模板回退：Hugo embedded partials（include "pagination" 这类）
        var builtinKey = NormalizeBuiltinKey(templateName);
        if (_builtins is not null && builtinKey.Length > 0 &&
            _builtins.TryGetTemplate(builtinKey, out _))
        {
            return BuiltinPrefix + builtinKey;
        }

        return null;
    }

    /// <summary>候选路径是否位于模板根内（防 include 名穿越出根）</summary>
    private static bool IsUnderRoot(string fullPath, string root)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>include 名的候选文件形态（有序，对齐 FileTemplateLoader 的搜索序）</summary>
    private static IEnumerable<string> IncludeCandidateForms(string templateName)
    {
        var name = templateName.Replace('\\', '/').Trim('/');
        var hasPartialsPrefix = name.StartsWith("partials/", StringComparison.OrdinalIgnoreCase) ||
                                name.StartsWith("_partials/", StringComparison.OrdinalIgnoreCase);
        var withExt = name.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase) ? name : name + ".liquid";

        if (hasPartialsPrefix)
        {
            // 名字已带前缀：只探两个目录形态（不再试裸名——那会让 include
            // "partials/x" 命中 layouts/x.liquid 这类非 partial 文件）
            var rest = StripPartialsPrefix(name);
            var restWithExt = rest.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase) ? rest : rest + ".liquid";
            yield return withExt;
            yield return "_partials/" + restWithExt;
        }
        else
        {
            yield return withExt;
            yield return "_default/" + withExt;
            yield return "partials/" + withExt;
            yield return "_partials/" + withExt;
        }
    }

    private static string StripPartialsPrefix(string name)
    {
        if (name.StartsWith("_partials/", StringComparison.OrdinalIgnoreCase))
        {
            return name["_partials/".Length..];
        }
        if (name.StartsWith("partials/", StringComparison.OrdinalIgnoreCase))
        {
            return name["partials/".Length..];
        }
        return name;
    }

    /// <summary>内置模板的 include 哨兵前缀（无物理文件，内容由内置提供者再取）</summary>
    internal const string BuiltinPrefix = "\u0001fluid-builtin:";

    /// <summary>include 名 → 内置模板键：剥 .liquid 扩展名与 partials/_internal 前缀</summary>
    private static string NormalizeBuiltinKey(string templateName)
    {
        var key = templateName.Replace('\\', '/');
        if (key.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase))
        {
            key = key[..^".liquid".Length];
        }
        if (key.StartsWith("_internal/", StringComparison.OrdinalIgnoreCase))
        {
            key = key["_internal/".Length..];
        }
        return StripPartialsPrefix(key);
    }

    /// <inheritdoc />
    public bool TemplateExists(string templateName) => TryResolveTemplatePath(templateName) is not null;

    /// <summary>
    /// 模板文件是否为**空文件**（0 字节）。与 Scriban 侧同口径：404 这类
    /// "空模板视为缺失"的判定用它
    /// </summary>
    public bool TemplateFileIsEmpty(string templateName)
    {
        var path = TryResolveTemplatePath(templateName);
        return path is not null && new FileInfo(path) is { Exists: true, Length: 0 };
    }

    /// <summary>
    /// 逻辑名 → 物理路径（加权查找：根形态 / _default 兜底 / 输出格式变体）；
    /// 未命中返回 null（内置模板无物理路径，由 <see cref="RenderAsync"/> 的
    /// 缓存键 builtin: 前缀处理）
    /// </summary>
    private string? TryResolveTemplatePath(string templateName)
    {
        try
        {
            return _lookup.Resolve(templateName, _templatesPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// 模板依赖闭包（增量构建注册用）：include/render 的静态引用递归收集。
    /// 与 Scriban 侧同口径的正则近似——精确依赖以渲染期
    /// <see cref="FlintTemplateContext.RenderedDependencies"/> 为准
    /// </summary>
    /// <inheritdoc />
    public IReadOnlyList<string> GetDependencies(string templateName)
    {
        if (_dependencyCache.TryGetValue(templateName, out var cached))
        {
            return cached;
        }

        var dependencies = new List<string>();
        CollectDependencies(templateName, dependencies, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var result = dependencies.AsReadOnly();
        _dependencyCache.TryAdd(templateName, result);
        return result;
    }

    /// <summary>
    /// 静态依赖闭包（增量构建注册用）：解析 include/render 引用递归收集。
    /// 与 Scriban 侧同口径的正则近似——精确依赖以渲染期
    /// <see cref="FlintTemplateContext.RenderedDependencies"/> 为准
    /// </summary>
    private void CollectDependencies(
        string templateName,
        List<string> dependencies,
        HashSet<string> visited)
    {
        if (!visited.Add(templateName))
        {
            return;
        }

        string content;
        try
        {
            var path = TryResolveTemplatePath(templateName);
            if (path is null)
            {
                // 内置模板：无物理文件，无 further 依赖
                return;
            }
            content = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (System.Text.RegularExpressions.Match reference in System.Text.RegularExpressions.Regex.Matches(
                     content, @"\{%-?\s*(?:include|render)\s+[""']([^""']+)[""']"))
        {
            var depName = reference.Groups[1].Value;
            if (!dependencies.Contains(depName))
            {
                dependencies.Add(depName);
                CollectDependencies(depName, dependencies, visited);
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> PrecompileTemplatesAsync(CancellationToken cancellationToken = default)
    {
        // 缓存键是逻辑名（相对路径去 .liquid）：站点与主题同名模板必须站点优先。
        // 站点文件全量扫描，主题文件剔除与站点同名的逻辑名后追加。
        // **单次枚举**（2026-10-21 性能优化：此前站点目录枚举两次——
        // 一次建 siteRelativeNames，一次建 templateFiles）
        var siteRelativeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var templateFiles = new List<(string Path, string Root)>();
        if (Directory.Exists(_templatesPath))
        {
            foreach (var f in Directory.EnumerateFiles(_templatesPath, "*.liquid", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(_templatesPath, f).Replace('\\', '/');
                siteRelativeNames.Add(rel);
                templateFiles.Add((f, _templatesPath));
            }
        }

        foreach (var themePath in _themeTemplatePaths)
        {
            if (!Directory.Exists(themePath))
            {
                continue;
            }
            foreach (var f in Directory.EnumerateFiles(themePath, "*.liquid", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(themePath, f).Replace('\\', '/');
                if (!siteRelativeNames.Contains(rel))
                {
                    templateFiles.Add((f, themePath));
                }
            }
        }

        await Parallel.ForEachAsync(
            templateFiles,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (entry, ct) =>
            {
                var (file, root) = entry;
                try
                {
                    var relativePath = Path.GetRelativePath(root, file);
                    var templateName = relativePath.Replace('\\', '/');
                    if (templateName.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase))
                    {
                        templateName = templateName[..^".liquid".Length];
                    }

                    var content = await File.ReadAllTextAsync(file, ct);
                    if (_parser.TryParse(content, out var template, out _))
                    {
                        _templateCache[templateName] = new CachedTemplate(
                            template, file, File.GetLastWriteTimeUtc(file));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // 解析失败的模板静默缺席缓存；渲染期按需加载仍会暴露真实错误
                }
            });

        return _templateCache.Count;
    }

    /// <inheritdoc />
    public void InvalidateMtimeCache()
    {
        _mtimeCache.Clear();
        _lookup.Invalidate();
        // include 路径/文本缓存随构建边界失效（构建内模板文件集不变是缓存
        // 的安全假设；构建间可能增删改——与 Scriban 侧 ClearTextCache 同口径）
        _includePathCache.Clear();
        _includeTextCache.Clear();
        // include 模板解析缓存（ITemplateCache）同步清空
        _includeTemplateCache.Clear();
        // 静态依赖闭包缓存同清（2026-10-22 修复：此前永不清空——长驻 serve
        // 进程增删/改名模板后闭包过期，模板新增的 include 关系不上外部
        // 变更事件，只能靠构建边界整体失效）
        _dependencyCache.Clear();
        // 产物收集队列按构建清空（SiteBuilder 在渲染前调用本方法；渲染期产物
        // 在渲染后经 GeneratedResources 取走——跨构建不累积）
        _generatedResources.Clear();
    }

    /// <summary>
    /// 按已知物理路径加载模板（页面感知查找链命中后的加载）：
    /// 缓存键为逻辑名（相对站点根），与 <see cref="GetOrLoadTemplateAsync"/>
    /// 的 mtime 失效语义一致。读前取 mtime、读后复核（有界重读）
    /// </summary>
    private async ValueTask<IFluidTemplate> GetOrLoadTemplateAtPathAsync(
        string templatePath,
        CancellationToken cancellationToken)
    {
        var logicalName = ToLogicalName(templatePath);
        if (_templateCache.TryGetValue(logicalName, out var cached) && !IsStale(cached))
        {
            return cached.Template;
        }

        string content;
        DateTime mtimeUtc;
        for (var attempt = 0; ; attempt++)
        {
            var mtimeBefore = File.GetLastWriteTimeUtc(templatePath);
            content = await File.ReadAllTextAsync(templatePath, cancellationToken);
            mtimeUtc = File.GetLastWriteTimeUtc(templatePath);
            if (mtimeUtc == mtimeBefore || attempt >= 2)
            {
                break;
            }
        }

        var template = ParseOrThrow(content, templatePath);
        _templateCache[logicalName] = new CachedTemplate(template, templatePath, mtimeUtc);
        return template;
    }

    /// <summary>按逻辑名加载（RenderAsync 用）：物理文件优先，内置兜底</summary>
    private async ValueTask<IFluidTemplate> GetOrLoadTemplateAsync(
        string templateName,
        CancellationToken cancellationToken)
    {
        if (_templateCache.TryGetValue(templateName, out var cached) && !IsStale(cached))
        {
            return cached.Template;
        }

        var path = TryResolveTemplatePath(templateName);
        if (path is not null)
        {
            return await GetOrLoadTemplateAtPathAsync(path, cancellationToken);
        }

        // 内置兜底模板
        if (_builtins is not null && _builtins.TryGetTemplate(templateName, out var builtinContent))
        {
            var builtinTemplate = ParseOrThrow(builtinContent, "builtin:" + templateName);
            _templateCache[templateName] = new CachedTemplate(builtinTemplate, null, DateTime.MinValue);
            return builtinTemplate;
        }

        throw new TemplateNotFoundException(templateName,
            [Path.Combine(_templatesPath, templateName + ".liquid")]);
    }

    private IFluidTemplate ParseOrThrow(string content, string sourcePath)
    {
        if (_parser.TryParse(content, out var template, out var error))
        {
            return template;
        }
        throw new TemplateParseException(sourcePath, [error ?? "Liquid 模板解析失败"]);
    }

    /// <summary>物理路径 → 逻辑名（相对站点根，统一 / 分隔；跨根时取相对该根的路径）</summary>
    private string ToLogicalName(string physicalPath)
    {
        var full = Path.GetFullPath(physicalPath);
        foreach (var root in new[] { _templatesPath }.Concat(_themeTemplatePaths))
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                var rel = full[rootFull.Length..].Replace('\\', '/');
                return rel.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)
                    ? rel[..^".liquid".Length]
                    : rel;
            }
        }

        return Path.GetFileName(full);
    }

    private static string DescribePageQuery(PageTemplateQuery query)
    {
        var parts = new List<string> { "kind=" + query.Kind };
        if (!string.IsNullOrEmpty(query.Layout))
        {
            parts.Add("layout=" + query.Layout);
        }
        if (!string.IsNullOrEmpty(query.DeclaredType))
        {
            parts.Add("type=" + query.DeclaredType);
        }
        if (!string.IsNullOrEmpty(query.Section))
        {
            parts.Add("section=" + query.Section);
        }
        if (!string.IsNullOrEmpty(query.Taxonomy))
        {
            parts.Add("taxonomy=" + query.Taxonomy);
        }
        if (!IsHtmlOutputFormat(query.OutputFormat))
        {
            parts.Add("format=" + query.OutputFormat);
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// 缓存条目是否过期：源文件 mtime 与编译时不一致说明模板已被修改。
    /// 内置模板 SourcePath 为 null → 恒不过期
    /// </summary>
    private bool IsStale(CachedTemplate cached)
    {
        return cached.SourcePath is not null &&
               GetMtimeUtc(cached.SourcePath) != cached.ModifiedTimeUtc;
    }

    /// <summary>mtime 查询的短窗缓存（与 Scriban 渲染器同口径）</summary>
    private DateTime GetMtimeUtc(string path)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (_mtimeCache.TryGetValue(path, out var entry) && now - entry.CachedAtUtc < MtimeCacheTtl)
        {
            return entry.MtimeUtc;
        }
        var mtime = File.GetLastWriteTimeUtc(path);
        _mtimeCache[path] = (now, mtime);
        return mtime;
    }

    /// <summary>
    /// 本构建内模板函数产生的资源产物（resources.Concat/Fingerprint/Sass/Build
    /// 等 P4-3/P4-4 变换结果），由 SiteBuilder 输出阶段写盘——否则模板引用的
    /// RelPermalink 会 404。渲染期经 AmbientValues 的收集队列登记，
    /// InvalidateMtimeCache（构建入口）清空
    /// </summary>
    public IReadOnlyList<TemplateResource> GeneratedResources =>
        _generatedResources.IsEmpty ? [] : _generatedResources.ToArray();

    private readonly System.Collections.Concurrent.ConcurrentQueue<TemplateResource> _generatedResources = new();

    /// <summary>
    /// 模板 errorf 记录的错误：Fluid 侧无 errorf 函数（Phase 4 函数平面），恒空
    /// </summary>
    public IReadOnlyList<string> DrainTemplateErrors() => [];
}

/// <summary>
/// include/render 的模板解析缓存（Fluid ITemplateCache 实现）：
/// 键 = 物理路径，值 = (IFluidTemplate, mtime)。Fluid 的 TemplateLoader
/// 每次 include 执行时经此查询——命中即免重复 parser.TryParse
/// （2026-10-21：1000 页 × 5 include = 5000+ 次重复解析是 Fluid 路径
/// 最大的纯解析浪费）
/// </summary>
internal sealed class IncludeTemplateCache : ITemplateCache
{
    private readonly ConcurrentDictionary<string, (IFluidTemplate Template, DateTimeOffset Mtime)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGetTemplate(string subpath, DateTimeOffset lastModified, out IFluidTemplate template)
    {
        if (_cache.TryGetValue(subpath, out var entry) && entry.Mtime == lastModified)
        {
            template = entry.Template;
            return true;
        }
        template = null!;
        return false;
    }

    public void SetTemplate(string subpath, DateTimeOffset lastModified, IFluidTemplate template)
    {
        _cache[subpath] = (template, lastModified);
    }

    /// <summary>清空缓存（构建边界 InvalidateMtimeCache 时调用）</summary>
    public void Clear() => _cache.Clear();
}
