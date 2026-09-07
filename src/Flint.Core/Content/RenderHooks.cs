// Flint 静态站点生成器
// RenderHooks——layouts/_markup/render-*.html 渲染钩子（对齐 Hugo render hooks 的最小集）

using Flint.Core.Templates;

namespace Flint.Core.Content;

/// <summary>
/// Markdown 渲染钩子集合：链接/图片/标题的定制渲染模板。
/// 每个委托把钩子变量（destination/text/plain_text/level/id 等）交给
/// <see cref="ScribanTemplateRenderer.RenderHookTemplate"/> 渲染，返回输出 HTML；
/// 模板渲染失败按构建错误 fail-fast。仅存在对应 _markup/ 模板时才非 null。
/// </summary>
public sealed class RenderHooks
{
    /// <summary>render-link.html（同时拦截 <c>&lt;https://…&gt;</c> 自动链接）</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? Link { get; init; }

    /// <summary>render-image.html（优先于 render-link 处理图片）</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? Image { get; init; }

    /// <summary>render-heading.html</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? Heading { get; init; }

    /// <summary>render-codeblock.html（全部代码块的通用钩子；可被语言专属钩子覆盖）</summary>
    public Func<IReadOnlyDictionary<string, object>, string>? CodeBlock { get; init; }

    /// <summary>render-codeblock-&lt;lang&gt;.html（语言专属钩子，优先于通用钩子）</summary>
    public IReadOnlyDictionary<string, Func<IReadOnlyDictionary<string, object>, string>> CodeBlockByLang { get; init; } =
        new Dictionary<string, Func<IReadOnlyDictionary<string, object>, string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否装有任一钩子</summary>
    public bool HasAny => Link is not null || Image is not null || Heading is not null ||
                          CodeBlock is not null || CodeBlockByLang.Count > 0;

    /// <summary>
    /// 从 layouts/_markup/ 探测钩子模板并构建钩子集合；
    /// 目录或模板不存在时返回 null（走 Markdig 默认渲染，零开销）
    /// </summary>
    public static RenderHooks? Load(string layoutsDirectory, ScribanTemplateRenderer renderer)
    {
        var markupDir = Path.Combine(layoutsDirectory, "_markup");
        if (!Directory.Exists(markupDir))
        {
            return null;
        }

        var link = TryLoad(markupDir, "render-link", renderer);
        var image = TryLoad(markupDir, "render-image", renderer);
        var heading = TryLoad(markupDir, "render-heading", renderer);
        var codeBlock = TryLoad(markupDir, "render-codeblock", renderer);

        // 语言专属变体：render-codeblock-<lang>.html（优先于通用 codeblock 钩子）
        var byLang = new Dictionary<string, Func<IReadOnlyDictionary<string, object>, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(markupDir, "render-codeblock-*.html"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var lang = name["render-codeblock-".Length..];
            if (lang.Length > 0)
            {
                byLang[lang] = vars => renderer.RenderHookTemplate("_markup/" + name, vars)
                    ?? throw new InvalidOperationException($"渲染钩子模板 {name} 未产出内容");
            }
        }

        var hooks = new RenderHooks
        {
            Link = link,
            Image = image,
            Heading = heading,
            CodeBlock = codeBlock,
            CodeBlockByLang = byLang
        };
        return hooks.HasAny ? hooks : null;
    }

    private static Func<IReadOnlyDictionary<string, object>, string>? TryLoad(
        string markupDir, string hookName, ScribanTemplateRenderer renderer)
    {
        // 模板名经 renderer 的路径解析命中 <hookName>.html
        if (!File.Exists(Path.Combine(markupDir, hookName + ".html")))
        {
            return null;
        }

        return vars => renderer.RenderHookTemplate("_markup/" + hookName, vars)
            ?? throw new InvalidOperationException($"渲染钩子模板 {hookName} 未产出内容");
    }
}
