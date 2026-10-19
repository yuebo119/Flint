// Flint 静态站点生成器
// Liquid 模板短代码（FLUID-MIGRATION-PLAN P5-4）：layouts/shortcodes/*.liquid
// 的 Fluid 等价渲染。与 Scriban 的 TemplateShortcode 平行实现
// IShortcodeProcessor，短代码按**物理扩展名**分派——新站点写 .liquid 短代码，
// Hugo 主题的 .html 短代码仍走 Scriban（内容管线解耦，引擎选择权在模板作者）。
//
// 变量约定对齐 Scriban 侧（迁移产物两种引擎通用）：
// params（命名+位置数字键）/ positional / inner / name / page / site /
// ordinal / parent，snake_case 成员访问经 FlintMemberAccessStrategy 映射。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using System.Globalization;

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// Liquid 短代码：经 <see cref="FluidTemplateRenderer"/> 渲染
/// <c>layouts/shortcodes/*.liquid</c> 内容
/// </summary>
public sealed class LiquidTemplateShortcode : IShortcodeProcessor
{
    private readonly FluidTemplateRenderer _renderer;
    private readonly string _content;

    public LiquidTemplateShortcode(string name, string content, FluidTemplateRenderer renderer)
    {
        Name = name;
        _content = content;
        _renderer = renderer;
        Description = $"Liquid 短代码 {name}";
    }

    public string Name { get; }

    public string Description { get; }

    public async ValueTask<string> ProcessAsync(
        ShortcodeContext context,
        CancellationToken cancellationToken = default)
    {
        // 变量表经 ExtraValues 通道合并进 Fluid 模型顶层键
        // （FluidTemplateRenderer.CreateFluidContext 消费）：
        // params（命名+位置数字键）/ positional / inner / name / ordinal /
        // parent + 命名参数平铺（Hugo .Get 直取）
        var model = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["params"] = BuildParams(context),
            ["name"] = context.Name,
            ["inner"] = context.InnerContent ?? string.Empty,
            ["positional"] = context.PositionalArgs,
            ["page"] = context.Page,
            ["site"] = context.Site,
            ["ordinal"] = context.Ordinal,
            ["parent"] = context.Parent is { } parent
                ? new LiquidShortcodeParentView(parent)
                : null
        };
        foreach (var (key, value) in context.Parameters)
        {
            model[key] = value;
        }

        var templateContext = new TemplateContext
        {
            ExtraValues = model,
            Page = context.Page as PageContext ?? new PageContext
            {
                Title = context.Name, Content = "", Permalink = "", RelPermalink = "",
                Date = DateTimeOffset.MinValue, Tags = [], Categories = [],
                WordCount = 0, ReadingTime = TimeSpan.Zero
            },
            Site = context.Site as SiteContext ?? new SiteContext
            {
                Title = "", BaseURL = "", Language = "",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "", Title = "" }
            }
        };

        return await _renderer.RenderLiquidContentAsync(
            _content, $"shortcode:{context.Name}", templateContext, cancellationToken);
    }

    /// <summary>全参数表：命名参数 + 位置参数（数字键），OrdinalIgnoreCase</summary>
    private static Dictionary<string, object?> BuildParams(ShortcodeContext context)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in context.Parameters)
        {
            dict[key] = value;
        }
        for (var i = 0; i < context.PositionalArgs.Count; i++)
        {
            dict[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] = context.PositionalArgs[i];
        }
        return dict;
    }
}

/// <summary>父短代码上下文视图（Hugo .Parent 的 Liquid 等价）</summary>
public sealed class LiquidShortcodeParentView
{
    private readonly ShortcodeParent _parent;

    public LiquidShortcodeParentView(ShortcodeParent parent) => _parent = parent;

    public string Name => _parent.Name;

    public int Ordinal => _parent.Ordinal;
}
