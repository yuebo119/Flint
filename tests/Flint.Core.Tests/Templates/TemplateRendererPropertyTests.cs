// Flint 静态站点生成器
// 模板渲染器属性测试
// **Property 5: 模板渲染确定性**

using Flint.Core.Abstractions;
using Flint.Core.Templates;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>
/// 模板渲染器属性测试
/// **Validates: Requirements 3.4, 3.8, 3.9**
/// </summary>
public class TemplateRendererPropertyTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScribanTemplateRenderer _renderer;

    public TemplateRendererPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _renderer = new ScribanTemplateRenderer(_tempDir, "https://example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// **Property 5: 模板渲染确定性**
    /// 对于任意有效的模板和上下文数据，相同的输入应该始终产生相同的输出
    /// </summary>
    [Property(MaxTest = 50)]
    public Property TemplateRendering_IsDeterministic()
    {
        return Prop.ForAll(
            TemplateArbitraries.ValidTemplateContent(),
            TemplateArbitraries.ValidPageContext(),
            (templateContent, pageContext) =>
            {
                // Arrange
                var templateName = $"test_{Guid.NewGuid():N}.html";
                var templatePath = Path.Combine(_tempDir, templateName);
                File.WriteAllText(templatePath, templateContent);

                var context = CreateContext(pageContext);

                // Act - 渲染两次
                var result1 = _renderer.RenderAsync(templateName, context).AsTask().Result;
                var result2 = _renderer.RenderAsync(templateName, context).AsTask().Result;

                // Assert - 结果应该相同
                return result1 == result2;
            });
    }

    private TemplateContext CreateContext(TestPageContext pageContext)
    {
        return new TemplateContext
        {
            Page = new PageContext
            {
                Title = pageContext.Title,
                Content = pageContext.Content,
                Permalink = "https://example.com/test/",
                RelPermalink = "/test/",
                Date = DateTimeOffset.Now,
                Tags = pageContext.Tags?.ToList().AsReadOnly() ?? [],
                Categories = [],
                WordCount = 100,
                ReadingTime = TimeSpan.FromMinutes(1),
                Draft = pageContext.Draft
            },
            Site = new SiteContext
            {
                Title = "Test Site",
                BaseURL = "https://example.com",
                Language = "en",
                Pages = [],
                RegularPages = [],
                Taxonomies = new TaxonomyCollection
                {
                    Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>()
                },
                Menus = new MenuCollection
                {
                    Menus = new Dictionary<string, IReadOnlyList<MenuItem>>()
                },
                Config = new { }
            }
        };
    }
}


/// <summary>
/// 测试用页面上下文
/// </summary>
public class TestPageContext
{
    public required string Title { get; init; }
    public required string Content { get; init; }
    public bool Draft { get; init; }
    public IList<string>? Tags { get; init; }
}

/// <summary>
/// 模板测试数据生成器
/// </summary>
public static class TemplateArbitraries
{
    /// <summary>
    /// 生成有效的模板内容
    /// </summary>
    public static Arbitrary<string> ValidTemplateContent()
    {
        var simpleTemplates = Gen.Elements(
            "{{ page.title }}",
            "{{ page.content }}",
            "{{ site.title }}",
            "{{ page.title | upper }}",
            "{{ page.title | lower }}",
            "{{ page.word_count }}",
            "{{ if page.draft }}Draft{{ end }}",
            "{{ for tag in page.tags }}{{ tag }}{{ end }}",
            "<h1>{{ page.title }}</h1>",
            "<article>{{ page.content }}</article>",
            "{{ page.title }} - {{ site.title }}",
            "{{ page.date | date_format 'yyyy-MM-dd' }}"
        );

        return simpleTemplates.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的页面上下文
    /// </summary>
    public static Arbitrary<TestPageContext> ValidPageContext()
    {
        var gen = from title in SafeStringGen()
                  from content in SafeHtmlContentGen()
                  from draft in ArbMap.Default.GeneratorFor<bool>()
                  from tags in Gen.ListOf(SafeStringGen())
                  select new TestPageContext
                  {
                      Title = title,
                      Content = content,
                      Draft = draft,
                      Tags = tags.ToList()
                  };

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成安全的字符串（不包含特殊字符）
    /// </summary>
    private static Gen<string> SafeStringGen()
    {
        return Gen.Elements(
            "Hello", "World", "Test", "Page", "Article",
            "Blog", "Post", "News", "Update", "Feature",
            "Tutorial", "Guide", "Review", "Analysis", "Report"
        );
    }

    /// <summary>
    /// 生成安全的 HTML 内容
    /// </summary>
    private static Gen<string> SafeHtmlContentGen()
    {
        return Gen.Elements(
            "<p>Hello World</p>",
            "<p>This is a test.</p>",
            "<h1>Title</h1><p>Content</p>",
            "<article><p>Article content</p></article>",
            "<div><p>Nested content</p></div>",
            "<p>Simple paragraph</p>",
            "<ul><li>Item 1</li><li>Item 2</li></ul>",
            "<blockquote>Quote</blockquote>"
        );
    }
}
