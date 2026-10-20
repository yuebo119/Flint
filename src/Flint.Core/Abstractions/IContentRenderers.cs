// Flint 静态站点生成器
// 内容管线渲染器抽象（FLUID-MIGRATION-PLAN P5-1/P5-2）：
// 短代码与 render hook 的注册/渲染抽象为接口，默认实现由
// TemplateEngineRouter 承担（按物理扩展名分发：.html → Scriban，
// .liquid → Fluid；内置 C# 短代码引擎无关）。SiteBuilder 只依赖接口，
// 不认识具体引擎——后续要换渲染策略（如纯 Fluid 站点禁 Scriban 短代码）
// 不需要改构建管线。

using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;

namespace Flint.Core.Templates;

/// <summary>
/// 短代码渲染器：把站点/主题短代码目录注册进短代码注册表。
/// 默认实现（TemplateEngineRouter）按扩展名分发；内置短代码
/// （figure/highlight/ref/relref 等）是 C# 实现，与引擎无关
/// </summary>
public interface IShortcodeRenderer
{
    /// <summary>
    /// 从短代码目录注册全部短代码。站点目录优先（站点覆盖主题同名），
    /// 同目录内 <c>.liquid</c> 覆盖 <c>.html</c>（新站点主力形态）
    /// </summary>
    /// <param name="registry">短代码注册表</param>
    /// <param name="shortcodesDir">短代码目录（layouts/shortcodes 或 _shortcodes）</param>
    /// <param name="origin">来源标识（站点/主题名，错误信息用）</param>
    /// <param name="isSite">是否站点目录（站点短代码语法错误 fail-fast，主题容忍）</param>
    void RegisterShortcodesFrom(
        IShortcodeRegistry registry,
        string shortcodesDir,
        string origin,
        bool isSite);
}

/// <summary>
/// render hook 渲染器：从 layouts/_markup/ 探测并装配链接/图片/标题/
/// 代码块钩子。默认实现（TemplateEngineRouter）按扩展名分发
/// （.liquid 优先于 .html）
/// </summary>
public interface IRenderHookRenderer
{
    /// <summary>
    /// 加载 render hooks；无任何钩子目录/模板时返回 null（走 Markdig 默认渲染）
    /// </summary>
    /// <param name="layoutsDirectory">站点 layouts 目录</param>
    /// <param name="themeLayoutDirectories">主题 layouts 回退目录</param>
    RenderHooks? LoadRenderHooks(
        string layoutsDirectory,
        IEnumerable<string>? themeLayoutDirectories = null);
}
