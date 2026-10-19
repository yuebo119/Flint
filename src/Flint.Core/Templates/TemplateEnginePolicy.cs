// Flint 静态站点生成器
// 模板引擎路由策略（FLUID-MIGRATION-PLAN P0-3）

namespace Flint.Core.Templates;

/// <summary>
/// 模板引擎策略（FLUID-MIGRATION-PLAN §一.3 引擎路由规则）：
/// <list type="bullet">
/// <item><see cref="Auto"/>：双轨路由——<c>.liquid</c> 模板与 Fluid 原生站点的内置
/// 回退模板走 Fluid；Hugo 主题 <c>.html</c> 模板、ThemeMigrator 产物、Scriban
/// 内置模板走 Scriban（默认值，现有站点行为不变）</item>
/// <item><see cref="Fluid"/>：全站 Fluid（实验模式：新站点整体用 Liquid）</item>
/// <item><see cref="Scriban"/>：全站 Scriban（兼容模式：Hugo 主题站点显式声明）</item>
/// </list>
/// </summary>
public enum TemplateEnginePolicy
{
    /// <summary>双轨路由（默认）：按模板物理扩展名 + 内置模板提供者路由</summary>
    Auto,

    /// <summary>全站 Fluid（实验模式）</summary>
    Fluid,

    /// <summary>全站 Scriban（兼容模式）</summary>
    Scriban
}
