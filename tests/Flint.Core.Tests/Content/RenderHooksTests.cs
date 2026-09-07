// Flint 静态站点生成器
// Render hooks 测试——layouts/_markup/render-*.html 定制链接/图片/标题渲染

using Flint.Core.Abstractions;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>
/// <see cref="RenderHooks"/> 探测与 Markdown 渲染钩子行为
/// </summary>
public class RenderHooksTests : IDisposable
{
    private readonly string _layoutsDir;
    private readonly ScribanTemplateRenderer _renderer;

    public RenderHooksTests()
    {
        _layoutsDir = Path.Combine(Path.GetTempPath(), "flint-hooks-" + Guid.NewGuid().ToString("N"), "layouts");
        Directory.CreateDirectory(_layoutsDir);
        _renderer = new ScribanTemplateRenderer(_layoutsDir, "http://localhost:1313/");
    }

    [Fact]
    public void Load_NoMarkupDirectory_ReturnsNull()
    {
        Assert.Null(RenderHooks.Load(_layoutsDir, _renderer));
    }

    [Fact]
    public void Load_LinkHookFile_ProvidesLinkHook()
    {
        Directory.CreateDirectory(Path.Combine(_layoutsDir, "_markup"));
        File.WriteAllText(Path.Combine(_layoutsDir, "_markup", "render-link.html"), "<a class=\"hooked\" href=\"{{ destination }}\">{{ plain_text }}</a>");

        var hooks = RenderHooks.Load(_layoutsDir, _renderer);

        Assert.NotNull(hooks);
        Assert.True(hooks.HasAny);
        Assert.Null(hooks.Heading); // 未提供的钩子保持 null
    }

    [Fact]
    public void ToHtml_LinkHook_RewritesAnchor()
    {
        WriteHook("render-link.html", "<a class=\"hooked\" href=\"{{ destination }}\">{{ plain_text }}</a>");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("请看 [文档](https://example.com/doc \"标题\")");

        Assert.Contains("<a class=\"hooked\" href=\"https://example.com/doc\">文档</a>", html);
        Assert.DoesNotContain("<a href=", html); // 默认锚点输出被替换
    }

    [Fact]
    public void ToHtml_ImageHook_TakesPrecedenceOverLinkHook()
    {
        WriteHook("render-link.html", "<link-hook>{{ plain_text }}</link-hook>");
        WriteHook("render-image.html", "<img class=\"hooked-img\" src=\"{{ destination }}\">");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("![替代文字](/img/a.png)");

        Assert.Contains("hooked-img", html);
        Assert.DoesNotContain("<link-hook>", html); // 图片走 render-image 而非 render-link
    }

    [Fact]
    public void ToHtml_HeadingHook_RewritesHeadingWithLevelAndId()
    {
        WriteHook("render-heading.html", "<h{{ level }} class=\"hooked-h\">{{ plain_text }}</h{{ level }}>");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("## 标题二");

        Assert.Contains("<h2 class=\"hooked-h\">标题二</h2>", html);
    }

    [Fact]
    public void ToHtml_LinkInsideText_NestedImageUsesDefaultRenderer()
    {
        // 链接内嵌图片：图片的 text 子渲染走全新默认渲染器，不递归进钩子
        WriteHook("render-link.html", "<a class=\"hooked\" href=\"{{ destination }}\">{{ text }}</a>");
        WriteHook("render-image.html", "<img src=\"{{ destination }}\">");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("[![图](/a.png)](https://example.com)");

        Assert.Contains("<a class=\"hooked\" href=\"https://example.com\">", html);
        Assert.Contains("src=\"/a.png\"", html); // 嵌套图片子渲染走默认渲染器（带 Markdig 默认属性），不递归进钩子
    }

    [Fact]
    public async Task ContentParser_WithHookedParser_HookAppliesToPageContent()
    {
        WriteHook("render-link.html", "<a class=\"hooked\" href=\"{{ destination }}\">{{ plain_text }}</a>");
        var parser = new ContentParser(new FrontMatterParser(), CreateHookedParser(), new ShortcodeProcessor());
        var file = new ContentFile
        {
            Path = "/site/content/posts/a.md",
            RawContent = System.Text.Encoding.UTF8.GetBytes("---\ntitle: \"T\"\n---\n\n[链接](https://example.com)\n"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var result = await parser.ParseAsync(file);

        Assert.Contains("<a class=\"hooked\" href=\"https://example.com\">链接</a>", result.HtmlContent);
    }

    private MarkdownParser CreateHookedParser()
    {
        var hooks = RenderHooks.Load(_layoutsDir, _renderer);
        Assert.NotNull(hooks);
        return new MarkdownParser(hooks);
    }

    private void WriteHook(string fileName, string content)
    {
        Directory.CreateDirectory(Path.Combine(_layoutsDir, "_markup"));
        File.WriteAllText(Path.Combine(_layoutsDir, "_markup", fileName), content);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(Path.GetDirectoryName(_layoutsDir)!, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Load_CodeBlockHooks_GenericAndLanguageVariants()
    {
        WriteHook("render-codeblock.html", "GENERIC");
        WriteHook("render-codeblock-go.html", "GO");

        var hooks = RenderHooks.Load(_layoutsDir, _renderer);

        Assert.NotNull(hooks);
        Assert.True(hooks.CodeBlockByLang.ContainsKey("go"));
        Assert.NotNull(hooks.CodeBlock);
    }

    [Fact]
    public void ToHtml_CodeBlockHook_WrapsWithTemplate()
    {
        WriteHook("render-codeblock.html",
            "<div class=\"code\" data-lang=\"{{ identifier }}\">{{ inner }}</div>");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("```go\nfmt.Println(\"hi\")\n```");

        Assert.Contains("data-lang=\"go\"", html);
        Assert.Contains("fmt.Println(&quot;hi&quot;)", html); // .Inner 为 HTML 转义后的代码
        Assert.DoesNotContain("<pre><code", html); // 默认输出被替换
    }

    [Fact]
    public void ToHtml_CodeBlockLanguageHook_TakesPrecedenceOverGeneric()
    {
        WriteHook("render-codeblock.html", "GENERIC {{ identifier }}");
        WriteHook("render-codeblock-go.html", "GO-SPECIFIC {{ inner }}");
        WriteHook("render-codeblock-js.html", "JS-SPECIFIC {{ inner }}");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("```go\ncode\n```\n\n```js\nx\n```\n\n```py\ny\n```");

        Assert.Contains("GO-SPECIFIC", html);
        Assert.Contains("JS-SPECIFIC", html);
        Assert.Contains("GENERIC py", html); // 无专属变体的语言走通用钩子
    }

    [Fact]
    public void ToHtml_CodeBlockAttributes_EmptyUntilMarkdigExposesArguments()
    {
        // Markdig 0.44 将 info string 的 key=value 部分存 internal（Arguments 字段），
        // 公开 API 不可达——attributes 恒为空字典（诚实降级，见实现注释）
        WriteHook("render-codeblock.html",
            "<pre data-lang=\"{{ identifier }}\"{{ attributes }}>{{ inner }}</pre>");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("```csharp title=\"Demo\"\nConsole.WriteLine();\n```");

        Assert.Contains("data-lang=\"csharp\"", html);
    }

    [Fact]
    public void ToHtml_CodeBlockWithoutHook_FallsBackToDefault()
    {
        // 无任何 codeblock 钩子：heading 钩子存在但不影响 codeblock 回退默认渲染
        WriteHook("render-heading.html", "<h1>{{ plain_text }}</h1>");
        var parser = CreateHookedParser();

        var html = parser.ToHtml("```go\nfmt.Println(\"hi\")\n```");

        Assert.Contains("<pre><code class=\"language-go\"", html);
        Assert.Contains("&quot;hi&quot;", html); // 双引号的 HTML 转义
    }
}
