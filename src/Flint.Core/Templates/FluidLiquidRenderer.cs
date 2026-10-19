// Flint 静态站点生成器核心库
// Fluid Liquid 备用渲染器：用于 .liquid 模板文件，默认模板引擎仍为 Scriban。

using Fluid;
using Flint.Core.Abstractions;
using FlintTemplateContext = Flint.Core.Abstractions.TemplateContext;
using FluidTemplateContext = Fluid.TemplateContext;

namespace Flint.Core.Templates;

/// <summary>
/// Fluid Liquid 渲染入口。当前仅接入 RenderTemplateFileAsync 的 .liquid 分支，
/// 作为 Scriban 的备用引擎，不改变现有 Scriban 模板行为。
/// </summary>
public static class FluidLiquidRenderer
{
    public static ValueTask<string> RenderAsync(
        string liquidTemplate,
        FlintTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(liquidTemplate);
        ArgumentNullException.ThrowIfNull(context);

        var parser = new FluidParser();
        var template = parser.Parse(liquidTemplate);

        var model = new Dictionary<string, object?>
        {
            ["page"] = context.Page,
            ["site"] = context.Site,
            ["params"] = context.Params
        };

        var fluidContext = new FluidTemplateContext(model, TemplateOptions.Default)
        {
            CancellationToken = cancellationToken
        };

        return template.RenderAsync(fluidContext);
    }
}
