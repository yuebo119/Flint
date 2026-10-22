// Flint 静态站点生成器
// FluidTemplateRenderer 的内容管线接口实现（2026-10-22 接口对齐）：
// 裸 Fluid 渲染器（不经 router 的用法：测试/非 router 消费方）也实现
// IShortcodeRenderer/IRenderHookRenderer——只认 .liquid 原生形态，
// .html 内容由 Scriban 渲染器/router 的双引擎分发处理
// （见 TemplateEngineRouter.Content.cs 与 ScribanTemplateRenderer.Content.cs）。
// 此前只有 Scriban 侧实现这两个接口，SiteBuilder 的
// `is not IShortcodeRenderer` 早退让裸 Fluid 装配静默丢失全部短代码注册

using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;

namespace Flint.Core.Templates;

public sealed partial class FluidTemplateRenderer
{
    /// <inheritdoc />
    public void RegisterShortcodesFrom(
        IShortcodeRegistry registry,
        string shortcodesDir,
        string origin,
        bool isSite)
    {
        if (!Directory.Exists(shortcodesDir))
        {
            return;
        }

        // 内置 C# 短代码在 ShortcodeRegistry 构造时已注册，与引擎无关——
        // 本方法只管 .liquid 模板短代码（与 Scriban 侧只管 .html 对称）
        foreach (var file in Directory.EnumerateFiles(shortcodesDir, "*.liquid"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            try
            {
                registry.Register(new LiquidTemplateShortcode(name, content, this));
            }
            catch (FormatException ex) when (!isSite)
            {
                // 主题短码为第三方内容：单个短码语法错误不应阻断整个构建
                // （Hugo 对主题短码同样容忍；站点短码保持 fail-fast）
                Console.Error.WriteLine($"[警告] 跳过无法解析的主题短代码（{origin}）: {file} — {ex.Message}");
            }
            catch (FormatException ex)
            {
                throw new FormatException($"加载短代码失败（{origin}）: {file}", ex);
            }
        }

        // .html 短代码不在本渲染器职责内（无 Scriban 渲染器）——裸 Fluid
        // 装配下 .html 短代码不注册（与 router 的 fluid 模式同语义）
    }

    /// <inheritdoc />
    public RenderHooks? LoadRenderHooks(
        string layoutsDirectory,
        IEnumerable<string>? themeLayoutDirectories = null)
        // LoadFluid 签名不涉 Scriban 类型：本类型是引擎隔离门禁的热路径
        // 类型，IL 不得引用 Scriban 程序集（EngineIsolationTests）
        => RenderHooks.LoadFluid(layoutsDirectory, this, themeLayoutDirectories);
}
