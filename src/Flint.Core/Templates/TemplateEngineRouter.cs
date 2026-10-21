// Flint 静态站点生成器
// 模板引擎路由器（FLUID-MIGRATION-PLAN P1-6 / §一.3）：ITemplateRenderer 的
// 唯一路由入口，按策略把渲染请求分派给 Fluid/Scriban 渲染器。
// SiteBuilder 只依赖 ITemplateRenderer，不认识两个具体渲染器——引擎增删
// 不触及构建管线。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
// Flint 的 TemplateContext 与 Fluid.TemplateContext 同名：本文件统一指 Flint 侧
using TemplateContext = Flint.Core.Abstractions.TemplateContext;

namespace Flint.Core.Templates;

/// <summary>
/// 双引擎路由器：auto 模式按"模板物理扩展名 + 内置模板提供者"双轨路由
/// （<c>.liquid</c> 与 Fluid 原生站点的内置回退 → Fluid；Hugo 主题
/// <c>.html</c> 与 Scriban 内置 → Scriban）；fluid/scriban 模式全站单引擎。
/// 同时承担内容管线的两个渲染器接口（P5-1/P5-2）：短代码注册与
/// render hook 装配按扩展名分发
/// </summary>
public sealed partial class TemplateEngineRouter : ITemplateRenderer, IShortcodeRenderer, IRenderHookRenderer
{
    private readonly FluidTemplateRenderer _fluid;

    /// <summary>
    /// Scriban 渲染器**懒构造**（P2-4 实质化）：纯 Fluid 站点（无 .html 模板、
    /// 无 _markup 钩子）的构建全程不构造 Scriban 渲染器——Scriban 程序集
    /// 因此不被加载（engine-probe 实证）。Hugo 主题站点首次触碰 Scriban 路径
    /// （.html 渲染/hook 装配/依赖判定）时经工厂构造。翻译表经
    /// <see cref="SetTranslations"/> 预存，构造时应用
    /// </summary>
    private ScribanTemplateRenderer? _scriban;
    private readonly Func<ScribanTemplateRenderer> _scribanFactory;
    private IReadOnlyDictionary<string, string>? _translations;
    private readonly TemplateEnginePolicy _policy;

    /// <summary>Fluid 原生站点判定缓存（layouts 根存在任一 .liquid 文件）；
    /// InvalidateMtimeCache 时重算（构建边界模板文件可能增删）。
    /// bool? 的读写是原子引用赋值，幂等懒缓存无需 volatile</summary>
    private bool? _fluidNative;

    public TemplateEngineRouter(
        FluidTemplateRenderer fluidRenderer,
        ScribanTemplateRenderer scribanRenderer,
        TemplateEnginePolicy policy)
        : this(fluidRenderer, () => scribanRenderer, policy)
    {
    }

    /// <summary>
    /// 工厂构造（P2-4）：Scriban 渲染器延迟到首次需要时构造——
    /// 纯 Fluid 站点构建不加载 Scriban 程序集
    /// </summary>
    public TemplateEngineRouter(
        FluidTemplateRenderer fluidRenderer,
        Func<ScribanTemplateRenderer> scribanRendererFactory,
        TemplateEnginePolicy policy)
    {
        _fluid = fluidRenderer;
        _scribanFactory = scribanRendererFactory;
        _policy = policy;
    }

    /// <summary>从配置字符串解析策略（无法识别按 auto——现有站点行为不变）</summary>
    public static TemplateEnginePolicy ParsePolicy(string? engine) =>
        engine?.Trim().ToLowerInvariant() switch
        {
            "fluid" or "liquid" => TemplateEnginePolicy.Fluid,
            "scriban" => TemplateEnginePolicy.Scriban,
            _ => TemplateEnginePolicy.Auto
        };

    /// <summary>当前策略（内容管线的短代码/render hook 扩展名分发据此判定）</summary>
    public TemplateEnginePolicy Policy => _policy;

    /// <summary>
    /// Fluid 内容渲染是否启用（短代码/render hook 的 .liquid 分发开关）：
    /// Scriban 兼容模式下内容管线整体走 Scriban，不认 .liquid 短代码
    /// </summary>
    public bool AllowsFluidContent => _policy != TemplateEnginePolicy.Scriban;

    /// <summary>Fluid 渲染器（内容管线渲染 .liquid 短代码/hook 用；共享缓存）</summary>
    public FluidTemplateRenderer FluidRenderer => _fluid;

    /// <summary>
    /// Scriban 渲染器（懒构造；P2-4）。纯 Fluid 站点不触碰本属性即不构造。
    /// 翻译表回填经 <see cref="SetTranslations"/>（不触发构造）
    /// </summary>
    public ScribanTemplateRenderer ScribanRenderer => _scriban ??= ConstructScriban();

    /// <summary>Scriban 渲染器是否已构造（依赖判定的零 IO 优化据此决定是否可用）</summary>
    public bool IsScribanConstructed => _scriban is not null;

    /// <summary>
    /// **按需构造中枢**（P2-4）：auto 模式下仅当"站点可能需要 Scriban"时
    /// 返回 Scriban 渲染器——非 Fluid 原生站点（纯 Scriban/空站点，内置兜底
    /// 走 Scriban）或存在 .html 模板（混合站点）或已构造；**纯 Fluid 站点**
    /// （有 .liquid 无 .html）返回 null——Scriban 程序集不加载。
    /// fluid/scriban 策略模式按策略定（scriban 必构造）
    /// </summary>
    private ScribanTemplateRenderer? ScribanIfNeeded => _policy switch
    {
        TemplateEnginePolicy.Scriban => ScribanRenderer,
        TemplateEnginePolicy.Fluid => null,
        _ => _scriban is not null || !IsFluidNativeSite || SiteHasHtmlTemplates
            ? ScribanRenderer
            : null
    };

    /// <summary>站点是否存在 .html 模板（懒判定缓存；纯 Fluid 站点为 false）</summary>
    private bool? _siteHasHtmlTemplates;

    private bool SiteHasHtmlTemplates => _siteHasHtmlTemplates ??= CheckHtmlTemplates();

    private bool CheckHtmlTemplates()
    {
        foreach (var root in new[] { _fluid.TemplatesPath }.Concat(_fluid.ThemeTemplatePaths))
        {
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root) &&
                Directory.EnumerateFiles(root, "*.html", SearchOption.AllDirectories).Any())
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 翻译表回填（render hook 的 i18n 用）：预存不触发构造——
    /// 纯 Fluid 站点无 .html hook，翻译表用不上；Scriban 路径首次构造时应用
    /// </summary>
    public void SetTranslations(IReadOnlyDictionary<string, string>? translations)
    {
        _translations = translations;
        if (_scriban is not null)
        {
            _scriban.Translations = translations;
        }
    }

    private ScribanTemplateRenderer ConstructScriban()
    {
        var renderer = _scribanFactory();
        renderer.Translations = _translations;
        return renderer;
    }

    /// <summary>从站点配置解析策略</summary>
    public static TemplateEnginePolicy ParsePolicy(SiteConfig config) =>
        ParsePolicy(config.Engine);

    /// <summary>
    /// 站点是否 Fluid 原生：layouts 根存在任一 <c>.liquid</c> 物理模板。
    /// 主题根的 .html 不算（Hugo 主题恒 Scriban）；auto 模式下内置回退模板
    /// 的归属由此判定
    /// </summary>
    private bool IsFluidNativeSite => _fluidNative ??= _fluid.HasLiquidTemplates();

    /// <inheritdoc />
    public ValueTask<string> RenderPageAsync(
        PageTemplateQuery query,
        TemplateContext context,
        CancellationToken cancellationToken = default)
    {
        switch (_policy)
        {
            case TemplateEnginePolicy.Fluid:
                return _fluid.RenderPageAsync(query, context, cancellationToken);
            case TemplateEnginePolicy.Scriban:
                return ScribanRenderer.RenderPageAsync(query, context, cancellationToken);
            default:
                // auto：物理模板按扩展名优先（.liquid 先于 .html——同根内
                // .liquid 是新站点主力形态，站点加 .liquid 覆盖 .html 是
                // 既定的迁移路径）；无物理模板时 Fluid 原生站点走 Fluid 内置。
                // Scriban 侧经 ScribanIfNeeded（P2-4：纯 Fluid 站点不构造）
                if (_fluid.PageTemplateExists(query))
                {
                    return _fluid.RenderPageAsync(query, context, cancellationToken);
                }
                if (ScribanIfNeeded?.PageTemplateExists(query) == true)
                {
                    return ScribanRenderer.RenderPageAsync(query, context, cancellationToken);
                }
                return IsFluidNativeSite && _fluid.CanRenderPageFallback(query)
                    ? _fluid.RenderPageAsync(query, context, cancellationToken)
                    : ScribanIfNeeded is null
                        // 纯 Fluid 站点无物理模板也无 Fluid 内置回退：直接抛，
                        // 不为报错构造 Scriban（P2-4）
                        ? throw new TemplateNotFoundException(
                            $"页面模板未找到（kind={query.Kind}）", [])
                        : ScribanRenderer.RenderPageAsync(query, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public bool PageTemplateExists(PageTemplateQuery query) => _policy switch
    {
        TemplateEnginePolicy.Fluid => _fluid.PageTemplateExists(query),
        TemplateEnginePolicy.Scriban => ScribanRenderer.PageTemplateExists(query),
        _ => _fluid.PageTemplateExists(query) || ScribanIfNeeded?.PageTemplateExists(query) == true
    };

    /// <inheritdoc />
    public ValueTask<string> RenderAsync(
        string templateName,
        TemplateContext context,
        CancellationToken cancellationToken = default)
    {
        switch (_policy)
        {
            case TemplateEnginePolicy.Fluid:
                return _fluid.RenderAsync(templateName, context, cancellationToken);
            case TemplateEnginePolicy.Scriban:
                return ScribanRenderer.RenderAsync(templateName, context, cancellationToken);
            default:
                // auto：.liquid 物理模板 → Fluid；其余（含 Scriban 内置兜底）→ Scriban。
                // 纯 Fluid 站点（ScribanIfNeeded 为 null）且 Fluid 无该模板时
                // 抛 TemplateNotFound——不构造 Scriban（P2-4）
                return _fluid.TemplateExists(templateName)
                    ? _fluid.RenderAsync(templateName, context, cancellationToken)
                    : ScribanIfNeeded is null
                        ? throw new TemplateNotFoundException(templateName, [])
                        : ScribanRenderer.RenderAsync(templateName, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public ValueTask<string> RenderTemplateFileAsync(
        string filePath,
        TemplateContext context,
        CancellationToken cancellationToken = default)
    {
        switch (_policy)
        {
            case TemplateEnginePolicy.Fluid:
                return _fluid.RenderTemplateFileAsync(filePath, context, cancellationToken);
            case TemplateEnginePolicy.Scriban:
                return ScribanRenderer.RenderTemplateFileAsync(filePath, context, cancellationToken);
            default:
                // 非 .liquid 辅助模板（robots.txt 等）经 Scriban 渲染——
                // 文件存在即真实 Scriban 需求，按需构造（P2-4 不阻止）
                return filePath.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)
                    ? _fluid.RenderTemplateFileAsync(filePath, context, cancellationToken)
                    : ScribanRenderer.RenderTemplateFileAsync(filePath, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public bool TemplateExists(string templateName) => _policy switch
    {
        TemplateEnginePolicy.Fluid => _fluid.TemplateExists(templateName),
        TemplateEnginePolicy.Scriban => ScribanRenderer.TemplateExists(templateName),
        _ => _fluid.TemplateExists(templateName) || ScribanIfNeeded?.TemplateExists(templateName) == true
    };

    /// <inheritdoc />
    public bool TemplateFileIsEmpty(string templateName) => _policy switch
    {
        TemplateEnginePolicy.Fluid => _fluid.TemplateFileIsEmpty(templateName),
        TemplateEnginePolicy.Scriban => ScribanRenderer.TemplateFileIsEmpty(templateName),
        _ => _fluid.TemplateFileIsEmpty(templateName) || ScribanIfNeeded?.TemplateFileIsEmpty(templateName) == true
    };

    /// <inheritdoc />
    public IReadOnlyList<string> GetDependencies(string templateName)
    {
        // 两个引擎各自收集（同一逻辑名不会同时存在于两个引擎的物理文件，
        // 但静态闭包按各自解析计算，合并保证增量反查不漏）。
        // Scriban 侧经 ScribanIfNeeded（P2-4：纯 Fluid 站点只取 Fluid 依赖）
        var fluidDeps = _policy == TemplateEnginePolicy.Scriban
            ? []
            : _fluid.GetDependencies(templateName);
        var scribanDeps = ScribanIfNeeded?.GetDependencies(templateName) ?? [];

        if (fluidDeps.Count == 0)
        {
            return scribanDeps;
        }
        if (scribanDeps.Count == 0)
        {
            return fluidDeps;
        }

        var merged = new List<string>(fluidDeps.Count + scribanDeps.Count);
        merged.AddRange(fluidDeps);
        foreach (var dep in scribanDeps)
        {
            if (!merged.Contains(dep, StringComparer.OrdinalIgnoreCase))
            {
                merged.Add(dep);
            }
        }
        return merged;
    }

    /// <inheritdoc />
    public async Task<int> PrecompileTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var count = 0;
        if (_policy != TemplateEnginePolicy.Scriban)
        {
            count += await _fluid.PrecompileTemplatesAsync(cancellationToken);
        }
        // Scriban 预编译**懒决策**（P2-4）：scriban 策略必编译；auto 模式下
        // 仅当站点存在 .html 模板（Hugo 主题/混合站点）才编译——纯 Fluid
        // 站点跳过，Scriban 渲染器保持未构造（程序集不加载），混合站点的
        // .html 模板首次渲染时按需编译（正确，仅首渲略慢）
        if (_policy == TemplateEnginePolicy.Scriban ||
            (_policy == TemplateEnginePolicy.Auto && SiteHasHtmlTemplates))
        {
            count += await ScribanRenderer.PrecompileTemplatesAsync(cancellationToken);
        }
        return count;
    }

    /// <inheritdoc />
    public void InvalidateMtimeCache()
    {
        _fluid.InvalidateMtimeCache();
        // 仅已构造的 Scriban 渲染器需要失效（P2-4：未构造即无状态可失效，
        // 不因构建入口的失效调用触发懒构造）
        if (_scriban is not null)
        {
            _scriban.InvalidateMtimeCache();
        }
        // Fluid 原生判定随模板文件集刷新（构建边界可能新增/删除 .liquid）
        _fluidNative = null;
        _siteHasHtmlTemplates = null;
    }

    /// <inheritdoc />
    public IReadOnlyList<TemplateResource> GeneratedResources =>
        _policy switch
        {
            TemplateEnginePolicy.Fluid => _fluid.GeneratedResources,
            TemplateEnginePolicy.Scriban => ScribanRenderer.GeneratedResources,
            // auto：未构造 Scriban 时其产物集为空（纯 Fluid 站点无 Scriban 产物）
            _ => _scriban is null
                ? _fluid.GeneratedResources
                : [.. _fluid.GeneratedResources, .. _scriban.GeneratedResources]
        };

    /// <inheritdoc />
    public IReadOnlyList<string> DrainTemplateErrors()
    {
        var fluidErrors = _policy == TemplateEnginePolicy.Scriban ? [] : _fluid.DrainTemplateErrors();
        var scribanErrors = _policy == TemplateEnginePolicy.Fluid || _scriban is null
            ? []
            : _scriban.DrainTemplateErrors();
        return fluidErrors.Count == 0 ? scribanErrors : [.. fluidErrors, .. scribanErrors];
    }
}
