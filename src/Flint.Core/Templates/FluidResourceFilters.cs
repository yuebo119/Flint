// Flint 静态站点生成器
// Liquid 版 resources 命名空间（FLUID-MIGRATION-PLAN P4-3 第一档）：
// Hugo 的 resources.Get/Match/ByType 经 Liquid 过滤器形态暴露——
// {{ 'css/main.css' | resource }}、{{ 'css/*.css' | resources_match }}、
// {{ 'css' | resources_by_type }}。返回的 TemplateResource 对象成员
// （rel_permalink/permalink/content/media_type/resource_type/title/fingerprint）
// 经 snake_case 成员映射直读。
//
// 分级说明（P4-1）：本档 = 新站点高频的**只读**资源操作；
// Concat/Copy/Fingerprint/FromString/Minify 等**产物型**变换需接入
// GeneratedResources 输出管道（SiteBuilder 输出阶段落盘），归后续批次
// ——差异清单登记于 docs/FLINT-VS-HUGO.md。

using Fluid;
using Fluid.Values;

namespace Flint.Core.Templates;

/// <summary>
/// Liquid 资源过滤器：resources.Get/Match/ByType 的过滤器形态。
/// 资源提供者为 null 时全部返回空（未接 Hugo Pipes 的站点，模板得 nil 而非崩溃）
/// </summary>
internal static class FluidResourceFilters
{
    /// <summary>注册资源过滤器（渲染器构造时调用；provider 可为 null）</summary>
    public static void Register(TemplateOptions options, ITemplateResourceProvider? provider)
    {
        options.Filters.AddFilter("resource", (input, args, ctx) => Get(input, args, ctx, provider));
        options.Filters.AddFilter("resources_get", (input, args, ctx) => Get(input, args, ctx, provider));
        options.Filters.AddFilter("resources_match", (input, args, ctx) => Match(input, args, ctx, provider));
        options.Filters.AddFilter("resources_by_type", (input, args, ctx) => ByType(input, args, ctx, provider));
        options.Filters.AddFilter("resources_bytype", (input, args, ctx) => ByType(input, args, ctx, provider));
    }

    private static ValueTask<FluidValue> Get(
        FluidValue input, FilterArguments args, TemplateContext ctx, ITemplateResourceProvider? provider)
    {
        if (provider is null)
        {
            return new(FluidValue.Create(null, ctx.Options));
        }
        var path = ToString(input, ctx);
        var resource = provider.Get(path);
        return new(FluidValue.Create(resource, ctx.Options));
    }

    private static ValueTask<FluidValue> Match(
        FluidValue input, FilterArguments args, TemplateContext ctx, ITemplateResourceProvider? provider)
    {
        if (provider is null)
        {
            return new(FluidValue.Create(new List<TemplateResource>(), ctx.Options));
        }
        return new(FluidValue.Create(provider.Match(ToString(input, ctx)), ctx.Options));
    }

    private static ValueTask<FluidValue> ByType(
        FluidValue input, FilterArguments args, TemplateContext ctx, ITemplateResourceProvider? provider)
    {
        if (provider is null)
        {
            return new(FluidValue.Create(new List<TemplateResource>(), ctx.Options));
        }
        return new(FluidValue.Create(provider.ByType(ToString(input, ctx)), ctx.Options));
    }

    private static string ToString(FluidValue value, TemplateContext ctx) =>
        value.IsNil() ? "" : value.ToStringValue(ctx);
}
