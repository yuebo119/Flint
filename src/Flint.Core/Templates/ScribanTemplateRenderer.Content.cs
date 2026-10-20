// Flint 静态站点生成器
// ScribanTemplateRenderer 的内容管线接口实现（FLUID-MIGRATION-PLAN P5-1/P5-2）：
// 裸 Scriban 渲染器（不经 router 的用法：测试/旧装配）也实现
// IShortcodeRenderer/IRenderHookRenderer——只认 .html 原生形态，
// .liquid 内容由 router 的双引擎分发处理（见 TemplateEngineRouter.Content.cs）。

using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;

namespace Flint.Core.Templates;

public sealed partial class ScribanTemplateRenderer
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

        foreach (var file in Directory.EnumerateFiles(shortcodesDir, "*.html"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            try
            {
                registry.Register(new TemplateShortcode(name, content));
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

        // .liquid 短代码不在本渲染器职责内（无 Fluid 渲染器）——裸 Scriban
        // 装配下 .liquid 短代码不注册（与 router 的 Scriban 兼容模式同语义）
    }

    /// <inheritdoc />
    public RenderHooks? LoadRenderHooks(
        string layoutsDirectory,
        IEnumerable<string>? themeLayoutDirectories = null) =>
        RenderHooks.Load(layoutsDirectory, this, themeLayoutDirectories);
}
