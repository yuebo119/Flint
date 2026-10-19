// Flint 静态站点生成器
// 模板引擎路由器（FLUID-MIGRATION-PLAN P1-6 / §一.3）：ITemplateRenderer 的
// 唯一路由入口，按策略把渲染请求分派给 Fluid/Scriban 渲染器。
// SiteBuilder 只依赖 ITemplateRenderer，不认识两个具体渲染器——引擎增删
// 不触及构建管线。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
// Flint 的 TemplateContext 与 Fluid.TemplateContext 同名：本文件统一指 Flint 侧
using TemplateContext = Flint.Core.Abstractions.TemplateContext;

namespace Flint.Core.Templates;

/// <summary>
/// 双引擎路由器：auto 模式按"模板物理扩展名 + 内置模板提供者"双轨路由
/// （<c>.liquid</c> 与 Fluid 原生站点的内置回退 → Fluid；Hugo 主题
/// <c>.html</c> 与 Scriban 内置 → Scriban）；fluid/scriban 模式全站单引擎
/// </summary>
public sealed class TemplateEngineRouter : ITemplateRenderer
{
    private readonly FluidTemplateRenderer _fluid;
    private readonly ScribanTemplateRenderer _scriban;
    private readonly TemplateEnginePolicy _policy;

    /// <summary>Fluid 原生站点判定缓存（layouts 根存在任一 .liquid 文件）；
    /// InvalidateMtimeCache 时重算（构建边界模板文件可能增删）。
    /// bool? 的读写是原子引用赋值，幂等懒缓存无需 volatile</summary>
    private bool? _fluidNative;

    public TemplateEngineRouter(
        FluidTemplateRenderer fluidRenderer,
        ScribanTemplateRenderer scribanRenderer,
        TemplateEnginePolicy policy)
    {
        _fluid = fluidRenderer;
        _scriban = scribanRenderer;
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
                return _scriban.RenderPageAsync(query, context, cancellationToken);
            default:
                // auto：物理模板按扩展名优先（.liquid 先于 .html——同根内
                // .liquid 是新站点主力形态，站点加 .liquid 覆盖 .html 是
                // 既定的迁移路径）；无物理模板时 Fluid 原生站点走 Fluid 内置
                if (_fluid.PageTemplateExists(query))
                {
                    return _fluid.RenderPageAsync(query, context, cancellationToken);
                }
                if (_scriban.PageTemplateExists(query))
                {
                    return _scriban.RenderPageAsync(query, context, cancellationToken);
                }
                return IsFluidNativeSite && _fluid.CanRenderPageFallback(query)
                    ? _fluid.RenderPageAsync(query, context, cancellationToken)
                    : _scriban.RenderPageAsync(query, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public bool PageTemplateExists(PageTemplateQuery query) => _policy switch
    {
        TemplateEnginePolicy.Fluid => _fluid.PageTemplateExists(query),
        TemplateEnginePolicy.Scriban => _scriban.PageTemplateExists(query),
        _ => _fluid.PageTemplateExists(query) || _scriban.PageTemplateExists(query)
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
                return _scriban.RenderAsync(templateName, context, cancellationToken);
            default:
                // auto：.liquid 物理模板 → Fluid；其余（含 Scriban 内置兜底）→ Scriban
                return _fluid.TemplateExists(templateName)
                    ? _fluid.RenderAsync(templateName, context, cancellationToken)
                    : _scriban.RenderAsync(templateName, context, cancellationToken);
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
                return _scriban.RenderTemplateFileAsync(filePath, context, cancellationToken);
            default:
                return filePath.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)
                    ? _fluid.RenderTemplateFileAsync(filePath, context, cancellationToken)
                    : _scriban.RenderTemplateFileAsync(filePath, context, cancellationToken);
        }
    }

    /// <inheritdoc />
    public bool TemplateExists(string templateName) => _policy switch
    {
        TemplateEnginePolicy.Fluid => _fluid.TemplateExists(templateName),
        TemplateEnginePolicy.Scriban => _scriban.TemplateExists(templateName),
        _ => _fluid.TemplateExists(templateName) || _scriban.TemplateExists(templateName)
    };

    /// <inheritdoc />
    public bool TemplateFileIsEmpty(string templateName) => _policy switch
    {
        TemplateEnginePolicy.Fluid => _fluid.TemplateFileIsEmpty(templateName),
        TemplateEnginePolicy.Scriban => _scriban.TemplateFileIsEmpty(templateName),
        _ => _fluid.TemplateFileIsEmpty(templateName) || _scriban.TemplateFileIsEmpty(templateName)
    };

    /// <inheritdoc />
    public IReadOnlyList<string> GetDependencies(string templateName)
    {
        // 两个引擎各自收集（同一逻辑名不会同时存在于两个引擎的物理文件，
        // 但静态闭包按各自解析计算，合并保证增量反查不漏）
        var fluidDeps = _policy == TemplateEnginePolicy.Scriban
            ? []
            : _fluid.GetDependencies(templateName);
        var scribanDeps = _policy == TemplateEnginePolicy.Fluid
            ? []
            : _scriban.GetDependencies(templateName);

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
        if (_policy != TemplateEnginePolicy.Fluid)
        {
            count += await _scriban.PrecompileTemplatesAsync(cancellationToken);
        }
        return count;
    }

    /// <inheritdoc />
    public void InvalidateMtimeCache()
    {
        _fluid.InvalidateMtimeCache();
        _scriban.InvalidateMtimeCache();
        // Fluid 原生判定随模板文件集刷新（构建边界可能新增/删除 .liquid）
        _fluidNative = null;
    }

    /// <inheritdoc />
    public IReadOnlyList<TemplateResource> GeneratedResources =>
        _policy == TemplateEnginePolicy.Fluid
            ? []
            : _policy == TemplateEnginePolicy.Scriban
                ? _scriban.GeneratedResources
                : [.. _fluid.GeneratedResources, .. _scriban.GeneratedResources];

    /// <inheritdoc />
    public IReadOnlyList<string> DrainTemplateErrors()
    {
        var fluidErrors = _policy == TemplateEnginePolicy.Scriban ? [] : _fluid.DrainTemplateErrors();
        var scribanErrors = _policy == TemplateEnginePolicy.Fluid ? [] : _scriban.DrainTemplateErrors();
        return fluidErrors.Count == 0 ? scribanErrors : [.. fluidErrors, .. scribanErrors];
    }
}
