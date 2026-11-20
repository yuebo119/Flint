// Flint 静态站点生成器
// Fluid 模板模型的访问器生成器宿主（Fluid 3.0 source generator）。
//
// 模板渲染的成员读取原本走 DefaultMemberAccessStrategy 的反射访问器：
// AOT 下靠 DynamicDependency 保活属性元数据，运行期首次访问还要构建委托。
// 在 partial 的 TemplateOptionsBuilder 上标注 [FluidRegister]，生成器在编译期
// 把下列类型的公共属性编译成直接调用，运行期不再反射。
//
// 与 FlintMemberAccessStrategy 的 snake_case 归一策略可叠加：策略先把
// word_count 归一为 WordCount，再命中生成器注册的访问器（实测组合方案比
// 纯反射快约一倍，且 snake_case 语义完全保留）。
//
// 新增模板可见的模型类型时在此登记，否则该类型回退到反射（功能不变，只是慢）。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Fluid;

namespace Flint.Core.Templates;

/// <summary>
/// Flint 的 Fluid 模板选项构建器：为模板可见的模型类型预生成成员访问器
/// </summary>
[FluidRegister(typeof(PageContext))]
[FluidRegister(typeof(SiteContext))]
[FluidRegister(typeof(SiteConfig))]
[FluidRegister(typeof(PaginatorView))]
[FluidRegister(typeof(PaginatorPageGroup))]
[FluidRegister(typeof(PaginatorGroupBoundary))]
[FluidRegister(typeof(TaxonomyCollection))]
[FluidRegister(typeof(TaxonomyTerm))]
[FluidRegister(typeof(MenuCollection))]
[FluidRegister(typeof(MenuItem))]
[FluidRegister(typeof(MenuItemConfig))]
[FluidRegister(typeof(MarkdownHeading))]
[FluidRegister(typeof(ReadingTimeView))]
[FluidRegister(typeof(HugoInfoView))]
[FluidRegister(typeof(TranslationLink))] // 0.4.0 多语言：page.translations 元素（AOT 下反射元数据被裁剪致 permalink 空）
[FluidRegister(typeof(TemplateResource))] // resources.*/qr/resize/srcset 产物（封闭化后 6 项新功能测试抓出漏登记）
public partial class FlintTemplateOptionsBuilder : TemplateOptionsBuilder
{
    /// <summary>
    /// 已登记类型的集合形态（与上方 [FluidRegister] 清单**必须同步**）。
    /// 用途：FlintMemberAccessStrategy 判定"模板访问了未登记的 Flint 模型类型"
    /// （R1 审计 HYG-C2 的证据通道）。新增登记时两处一起改。
    /// </summary>
    internal static readonly HashSet<Type> RegisteredTypes =
    [
        typeof(PageContext),
        typeof(SiteContext),
        typeof(SiteConfig),
        typeof(PaginatorView),
        typeof(PaginatorPageGroup),
        typeof(PaginatorGroupBoundary),
        typeof(TaxonomyCollection),
        typeof(TaxonomyTerm),
        typeof(MenuCollection),
        typeof(MenuItem),
        typeof(MenuItemConfig),
        typeof(MarkdownHeading),
        typeof(ReadingTimeView),
        typeof(HugoInfoView),
        typeof(TranslationLink),
        typeof(TemplateResource),
    ];
}
