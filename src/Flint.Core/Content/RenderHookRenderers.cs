// Flint 静态站点生成器
// RenderHookRenderers——Markdig 自定义渲染器，把链接/图片/标题的 HTML 输出
// 交给 layouts/_markup/render-*.html 模板（对齐 Hugo render hooks 的最小集）

using System.Text;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Flint.Core.Content;

/// <summary>
/// 链接与图片的钩子渲染器：图片优先走 render-image，其次 render-link
/// （Hugo 中两者独立，Flint 简化为 image 未配置时回落 link 模板，链接内嵌图片不受影响）
/// </summary>
public sealed class HookedLinkRenderer : MarkdownObjectRenderer<HtmlRenderer, LinkInline>
{
    private readonly RenderHooks _hooks;

    public HookedLinkRenderer(RenderHooks hooks)
    {
        _hooks = hooks;
    }

    protected override void Write(HtmlRenderer renderer, LinkInline link)
    {
        var hook = link.IsImage ? (_hooks.Image ?? _hooks.Link) : _hooks.Link;
        if (hook is null)
        {
            // 只配置 render-image 未配置 render-link 时，非图片链接回落 Markdig 默认渲染
            //（注册钩子渲染器时默认 LinkInlineRenderer 已被移除，必须显式回落）
            new LinkInlineRenderer().Write(renderer, link);
            return;
        }
        var vars = new Dictionary<string, object>
        {
            ["destination"] = link.Url ?? "",
            ["title"] = link.Title ?? "",
            ["text"] = HookedRendererHelpers.RenderInlineHtml(link),
            ["plain_text"] = HookedRendererHelpers.GetPlainText(link)
        };
        renderer.Write(hook(vars));
    }
}

/// <summary>
/// 代码块的钩子渲染器（对齐 Hugo render-codeblock）：
/// 语言专属钩子（render-codeblock-&lt;lang&gt;.html）优先于通用钩子（render-codeblock.html）；
/// 两者皆缺时回退 Markdig 默认渲染。模板变量：identifier（语言）/inner（HTML 转义后
/// 的代码文本）/attributes（恒空——Markdig 0.44 将 info string 的 key=value 部分
/// 存于 internal 字段，公开 API 不可达）/ordinal/type
/// </summary>
public sealed class HookedCodeBlockRenderer : MarkdownObjectRenderer<HtmlRenderer, FencedCodeBlock>
{
    private readonly RenderHooks _hooks;
    private int _ordinal;

    public HookedCodeBlockRenderer(RenderHooks hooks)
    {
        _hooks = hooks;
    }

    protected override void Write(HtmlRenderer renderer, FencedCodeBlock block)
    {
        var identifier = block.Info?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] ?? "";
        var hook = identifier.Length > 0 && _hooks.CodeBlockByLang.TryGetValue(identifier, out var byLang)
            ? byLang
            : _hooks.CodeBlock;
        if (hook is null)
        {
            // 回退 Markdig 默认渲染（pre/code + 转义代码 + language class）
            new CodeBlockRenderer().Write(renderer, block);
            return;
        }

        var vars = new Dictionary<string, object>
        {
            ["identifier"] = identifier,
            ["inner"] = HookedRendererHelpers.GetEscapedCodeText(block),
            ["ordinal"] = _ordinal++,
            ["type"] = "codeblock",
            ["attributes"] = new Dictionary<string, object>()
        };
        renderer.Write(hook(vars));
    }
}

/// <summary>
/// 标题的钩子渲染器。id 取自动锚点扩展写入的 HtmlAttributes（可能为空）
/// </summary>
public sealed class HookedHeadingRenderer : MarkdownObjectRenderer<HtmlRenderer, HeadingBlock>
{
    private readonly RenderHooks _hooks;

    public HookedHeadingRenderer(RenderHooks hooks)
    {
        _hooks = hooks;
    }

    protected override void Write(HtmlRenderer renderer, HeadingBlock heading)
    {
        var vars = new Dictionary<string, object>
        {
            ["level"] = heading.Level,
            ["id"] = heading.GetAttributes()?.Id ?? "",
            ["text"] = HookedRendererHelpers.RenderInlineHtml(heading.Inline),
            ["plain_text"] = HookedRendererHelpers.GetPlainText(heading.Inline)
        };
        renderer.Write(_hooks.Heading!(vars));
    }
}

/// <summary>
/// 钩子上下文的子内容渲染：用全新的默认 HtmlRenderer 渲染子内联，
/// 因此链接内嵌图片等嵌套结构走默认渲染，不会递归进钩子
/// </summary>
internal static class HookedRendererHelpers
{
    public static string RenderInlineHtml(MarkdownObject? inline)
    {
        if (inline is null)
        {
            return "";
        }

        var writer = new StringWriter();
        var htmlRenderer = new HtmlRenderer(writer);
        htmlRenderer.Write(inline);
        return writer.ToString();
    }

    public static string GetPlainText(MarkdownObject? inline)
    {
        if (inline is null)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach (var literal in inline.Descendants().OfType<LiteralInline>())
        {
            sb.Append(literal.Content.ToString());
        }

        return sb.ToString();
    }

    /// <summary>
    /// 代码块内容提取：拼接全部源行并做 HTML 转义（对齐 Hugo hook 的
    /// .Inner 语义——模板收到的是可安全内嵌 &lt;pre&gt; 的已转义代码）
    /// </summary>
    public static string GetEscapedCodeText(FencedCodeBlock block)
    {
        var raw = block.Lines.ToString();
        return System.Net.WebUtility.HtmlEncode(raw);
    }
}
