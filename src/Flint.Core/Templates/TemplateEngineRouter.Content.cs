// Flint 静态站点生成器
// TemplateEngineRouter 的内容管线接口实现（FLUID-MIGRATION-PLAN P5-1/P5-2）：
// 短代码注册与 render hook 装配按物理扩展名分发（.html → Scriban，
// .liquid → Fluid）。逻辑自 SiteBuilder.Render.RegisterShortcodesFrom 与
// BuildHandler/ServeHandler 的 RenderHooks.Load 调用搬入——SiteBuilder 与
// CLI 只依赖接口，引擎分发细节收敛在 router。

using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;

namespace Flint.Core.Templates;

public sealed partial class TemplateEngineRouter
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

        // 内置 C# 短代码（figure/highlight/ref/relref/details/qr/gist/youtube）
        // 在 ShortcodeRegistry 构造时已注册，与引擎无关——本方法只管模板短代码

        // Scriban 短代码（.html，Hugo 主题形态）
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

        // Liquid 短代码（.liquid，新站点主力形态；P5-4 的内置短代码是 C# 实现，
        // 两种引擎站点都可用）。注册序在 .html 之后：同根内 .liquid 覆盖 .html。
        // Scriban 兼容模式（engine: scriban）不认 .liquid 短代码
        if (!AllowsFluidContent)
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(shortcodesDir, "*.liquid"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            try
            {
                registry.Register(new LiquidTemplateShortcode(name, content, _fluid));
            }
            catch (FormatException ex) when (!isSite)
            {
                Console.Error.WriteLine($"[警告] 跳过无法解析的主题 Liquid 短代码（{origin}）: {file} — {ex.Message}");
            }
            catch (FormatException ex)
            {
                throw new FormatException($"加载 Liquid 短代码失败（{origin}）: {file}", ex);
            }
        }
    }

    /// <inheritdoc />
    public RenderHooks? LoadRenderHooks(
        string layoutsDirectory,
        IEnumerable<string>? themeLayoutDirectories = null)
    {
        // 先探 _markup 目录（P2-4）：纯 Fluid 站点无钩子目录时直接返回 null，
        // 不触碰 ScribanRenderer 懒构造——Scriban 程序集不被加载
        var markupDirs = new List<string> { Path.Combine(layoutsDirectory, "_markup") };
        if (themeLayoutDirectories is not null)
        {
            markupDirs.AddRange(themeLayoutDirectories.Select(d => Path.Combine(d, "_markup")));
        }
        if (markupDirs.All(d => !Directory.Exists(d)))
        {
            return null;
        }

        // Scriban 兼容模式下只认 .html 钩子（传 null = 不探测 .liquid）
        return RenderHooks.Load(
            layoutsDirectory,
            ScribanRenderer,
            themeLayoutDirectories,
            AllowsFluidContent ? _fluid : null);
    }
}
