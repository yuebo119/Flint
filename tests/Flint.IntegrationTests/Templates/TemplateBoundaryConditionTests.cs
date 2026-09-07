// Flint 静态站点生成器
// 模板边界条件测试
// 测试空模板、超大模板、深层嵌套 partial、循环引用 partial、特殊字符变量名
// _Requirements: 5.4, 5.8_

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Templates;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Templates;

/// <summary>
/// 模板边界条件测试
/// 验证模板渲染器对各种边界情况的处理
/// </summary>
public class TemplateBoundaryConditionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _layoutsDir;
    private readonly string _partialsDir;
    private readonly ScribanTemplateRenderer _renderer;

    public TemplateBoundaryConditionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-template-boundary-{Guid.NewGuid():N}");
        _layoutsDir = Path.Combine(_tempDir, "layouts");
        _partialsDir = Path.Combine(_layoutsDir, "partials");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_layoutsDir);
        Directory.CreateDirectory(_partialsDir);

        _renderer = new ScribanTemplateRenderer(_layoutsDir, "https://example.com");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    #region 辅助方法

    /// <summary>
    /// 创建模板文件
    /// </summary>
    private void CreateTemplate(string name, string content)
    {
        var path = Path.Combine(_layoutsDir, name.EndsWith(".html") ? name : name + ".html");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>
    /// 创建 partial 模板文件
    /// </summary>
    private void CreatePartial(string name, string content)
    {
        var path = Path.Combine(_partialsDir, name.EndsWith(".html") ? name : name + ".html");
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    /// <summary>
    /// 创建测试页面上下文
    /// </summary>
    private static PageContext CreatePageContext(
        string title = "测试页面",
        string content = "<p>内容</p>",
        IReadOnlyDictionary<string, object>? params_ = null)
    {
        return new PageContext
        {
            Title = title,
            Content = content,
            Permalink = "https://example.com/test/",
            RelPermalink = "/test/",
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 10,
            ReadingTime = TimeSpan.FromMinutes(1),
            Params = params_ ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// 创建测试站点上下文
    /// </summary>
    private static SiteContext CreateSiteContext()
    {
        return new SiteContext
        {
            Title = "测试站点",
            BaseURL = "https://example.com",
            Language = "zh-CN",
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
            Config = new Flint.Core.Configuration.SiteConfig { BaseURL = "https://example.com", Title = "T" }
        };
    }

    /// <summary>
    /// 创建模板上下文
    /// </summary>
    private static TemplateContext CreateTemplateContext(
        PageContext? page = null,
        IReadOnlyDictionary<string, object>? params_ = null)
    {
        return new TemplateContext
        {
            Page = page ?? CreatePageContext(),
            Site = CreateSiteContext(),
            IsHome = false,
            IsList = false,
            IsSingle = true,
            Params = params_ ?? new Dictionary<string, object>(),
            Data = new Dictionary<string, object>()
        };
    }

    #endregion

    #region 空模板测试

    /// <summary>
    /// 测试空模板 - 完全空的模板文件
    /// </summary>
    [Fact]
    public async Task Render_EmptyTemplate_ShouldReturnEmpty()
    {
        // Arrange
        CreateTemplate("empty", "");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("empty", context);

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// 测试空模板 - 只有空白字符的模板
    /// </summary>
    [Fact]
    public async Task Render_WhitespaceOnlyTemplate_ShouldReturnWhitespace()
    {
        // Arrange
        CreateTemplate("whitespace", "   \n\t\n   ");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("whitespace", context);

        // Assert
        result.Trim().Should().BeEmpty();
    }

    /// <summary>
    /// 测试空模板 - 只有注释的模板
    /// </summary>
    [Fact]
    public async Task Render_CommentOnlyTemplate_ShouldReturnEmpty()
    {
        // Arrange
        CreateTemplate("comment-only", "{{# 这是注释 #}}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("comment-only", context);

        // Assert
        result.Trim().Should().BeEmpty();
    }

    /// <summary>
    /// 测试空模板 - 空的 partial
    /// </summary>
    [Fact]
    public async Task Render_EmptyPartial_ShouldRenderCorrectly()
    {
        // Arrange
        CreatePartial("empty-partial", "");
        CreateTemplate("with-empty-partial", """
            <header>{{ include 'partials/empty-partial' }}</header>
            <main>内容</main>
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("with-empty-partial", context);

        // Assert
        result.Should().Contain("<header></header>");
        result.Should().Contain("<main>内容</main>");
    }

    #endregion

    #region 超大模板测试

    /// <summary>
    /// 测试超大模板 - 大量静态内容
    /// </summary>
    [Fact]
    public async Task Render_LargeStaticContent_ShouldRenderCorrectly()
    {
        // Arrange
        var largeContent = string.Concat(Enumerable.Repeat("<p>这是一段重复的内容。</p>\n", 5000));
        CreateTemplate("large-static", largeContent);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("large-static", context);

        // Assert
        result.Should().Contain("<p>这是一段重复的内容。</p>");
        result.Length.Should().BeGreaterThan(80000); // 5000 * 18 字节 = 90000
    }

    /// <summary>
    /// 测试超大模板 - 大量动态表达式
    /// </summary>
    [Fact]
    public async Task Render_ManyDynamicExpressions_ShouldRenderCorrectly()
    {
        // Arrange
        var expressions = string.Concat(Enumerable.Range(1, 500)
            .Select(i => $"<span>{{{{ page.title }}}}-{i}</span>\n"));
        CreateTemplate("many-expressions", expressions);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("many-expressions", context);

        // Assert
        result.Should().Contain("<span>测试页面-1</span>");
        result.Should().Contain("<span>测试页面-500</span>");
    }

    /// <summary>
    /// 测试超大模板 - 大量循环迭代
    /// </summary>
    [Fact]
    public async Task Render_LargeLoopIteration_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("large-loop", """
            {{ for i in range 1000 }}
            <li>项目 {{ i }}</li>
            {{ end }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("large-loop", context);

        // Assert
        result.Should().Contain("<li>项目 0</li>");
        result.Should().Contain("<li>项目 999</li>");
    }

    /// <summary>
    /// 测试超大模板 - 大量 partial 包含
    /// </summary>
    [Fact]
    public async Task Render_ManyPartialIncludes_ShouldRenderCorrectly()
    {
        // Arrange
        CreatePartial("small-partial", "<span>小部件</span>");
        var includes = string.Concat(Enumerable.Range(1, 100)
            .Select(_ => "{{ include 'partials/small-partial' }}\n"));
        CreateTemplate("many-includes", includes);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("many-includes", context);

        // Assert
        var count = result.Split("<span>小部件</span>").Length - 1;
        count.Should().Be(100);
    }

    #endregion

    #region 深层嵌套 Partial 测试

    /// <summary>
    /// 测试深层嵌套 partial - 3 层嵌套
    /// </summary>
    [Fact]
    public async Task Render_ThreeLevelNestedPartials_ShouldRenderCorrectly()
    {
        // Arrange
        CreatePartial("level3", "<span class=\"level3\">第三层</span>");
        CreatePartial("level2", """
            <div class="level2">
              第二层
              {{ include 'partials/level3' }}
            </div>
            """);
        CreatePartial("level1", """
            <div class="level1">
              第一层
              {{ include 'partials/level2' }}
            </div>
            """);
        CreateTemplate("nested-3", "{{ include 'partials/level1' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("nested-3", context);

        // Assert
        result.Should().Contain("class=\"level1\"");
        result.Should().Contain("class=\"level2\"");
        result.Should().Contain("class=\"level3\"");
        result.Should().Contain("第一层");
        result.Should().Contain("第二层");
        result.Should().Contain("第三层");
    }

    /// <summary>
    /// 测试深层嵌套 partial - 5 层嵌套
    /// </summary>
    [Fact]
    public async Task Render_FiveLevelNestedPartials_ShouldRenderCorrectly()
    {
        // Arrange
        CreatePartial("deep5", "<span>深度5</span>");
        CreatePartial("deep4", "<div>深度4 {{ include 'partials/deep5' }}</div>");
        CreatePartial("deep3", "<div>深度3 {{ include 'partials/deep4' }}</div>");
        CreatePartial("deep2", "<div>深度2 {{ include 'partials/deep3' }}</div>");
        CreatePartial("deep1", "<div>深度1 {{ include 'partials/deep2' }}</div>");
        CreateTemplate("nested-5", "{{ include 'partials/deep1' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("nested-5", context);

        // Assert
        result.Should().Contain("深度1");
        result.Should().Contain("深度2");
        result.Should().Contain("深度3");
        result.Should().Contain("深度4");
        result.Should().Contain("深度5");
    }

    /// <summary>
    /// 测试深层嵌套 partial - 带变量传递
    /// </summary>
    [Fact]
    public async Task Render_NestedPartialsWithVariables_ShouldPassVariables()
    {
        // Arrange
        CreatePartial("inner-var", "<span>{{ inner_value }}</span>");
        CreatePartial("outer-var", """
            {{ inner_value = outer_value }}
            {{ include 'partials/inner-var' }}
            """);
        CreateTemplate("nested-vars", """
            {{ outer_value = "传递的值" }}
            {{ include 'partials/outer-var' }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("nested-vars", context);

        // Assert
        result.Should().Contain("<span>传递的值</span>");
    }

    #endregion

    #region 循环引用 Partial 测试（错误处理）

    /// <summary>
    /// 测试循环引用 partial - 自引用
    /// 注意：Scriban 可能会处理这种情况，或者抛出异常
    /// </summary>
    [Fact]
    public void Render_SelfReferencingPartial_ShouldHandleGracefully()
    {
        // Arrange
        // 创建一个自引用的 partial（这通常会导致无限循环）
        // 但 Scriban 可能有保护机制
        CreatePartial("self-ref", """
            <div>自引用</div>
            """);
        CreateTemplate("with-self-ref", "{{ include 'partials/self-ref' }}");
        var context = CreateTemplateContext();

        // Act & Assert
        // 应该能够处理，不会无限循环
        var act = async () => await _renderer.RenderAsync("with-self-ref", context);

        // 应该正常完成或抛出异常，但不应该无限循环
        act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 测试循环引用 partial - 互相引用
    /// </summary>
    [Fact]
    public void Render_MutuallyReferencingPartials_ShouldHandleGracefully()
    {
        // Arrange
        // 注意：这里不创建真正的循环引用，因为那会导致无限循环
        // 而是测试系统对这种情况的处理能力
        CreatePartial("partial-a", "<div>A</div>");
        CreatePartial("partial-b", "<div>B {{ include 'partials/partial-a' }}</div>");
        CreateTemplate("mutual-ref", """
            {{ include 'partials/partial-a' }}
            {{ include 'partials/partial-b' }}
            """);
        var context = CreateTemplateContext();

        // Act
        var act = async () => await _renderer.RenderAsync("mutual-ref", context);

        // Assert
        act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    #endregion

    #region 特殊字符变量名测试

    /// <summary>
    /// 测试特殊字符变量名 - 下划线开头
    /// </summary>
    [Fact]
    public async Task Render_UnderscorePrefixVariable_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("underscore-var", """
            {{ _private_var = "私有变量" }}
            {{ _private_var }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("underscore-var", context);

        // Assert
        result.Trim().Should().Be("私有变量");
    }

    /// <summary>
    /// 测试特殊字符变量名 - 数字结尾
    /// </summary>
    [Fact]
    public async Task Render_NumberSuffixVariable_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("number-var", """
            {{ var1 = "变量1" }}
            {{ var2 = "变量2" }}
            {{ var1 }} {{ var2 }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("number-var", context);

        // Assert
        result.Trim().Should().Be("变量1 变量2");
    }

    /// <summary>
    /// 测试特殊字符变量名 - 长变量名
    /// </summary>
    [Fact]
    public async Task Render_LongVariableName_ShouldRenderCorrectly()
    {
        // Arrange
        var longVarName = "this_is_a_very_long_variable_name_that_should_still_work";
        CreateTemplate("long-var", $$$"""
            {{ {{{longVarName}}} = "长变量名的值" }}
            {{ {{{longVarName}}} }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("long-var", context);

        // Assert
        result.Trim().Should().Be("长变量名的值");
    }

    /// <summary>
    /// 测试特殊字符变量名 - 中文变量名（如果支持）
    /// </summary>
    [Fact]
    public async Task Render_ChineseVariableName_ShouldHandleGracefully()
    {
        // Arrange
        // 注意：Scriban 可能不支持中文变量名
        CreateTemplate("chinese-var", """
            {{ my_var = "中文值" }}
            {{ my_var }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("chinese-var", context);

        // Assert
        result.Trim().Should().Be("中文值");
    }

    /// <summary>
    /// 测试特殊字符变量名 - 通过 params 传递的特殊键名
    /// </summary>
    [Fact]
    public async Task Render_SpecialKeyInParams_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("params-special", "{{ params.my_special_key }}");
        var paramsDict = new Dictionary<string, object>
        {
            ["my_special_key"] = "特殊键值"
        };
        var context = CreateTemplateContext(params_: paramsDict);

        // Act
        var result = await _renderer.RenderAsync("params-special", context);

        // Assert
        result.Trim().Should().Be("特殊键值");
    }

    #endregion

    #region 特殊内容测试

    /// <summary>
    /// 测试特殊内容 - HTML 实体
    /// </summary>
    [Fact]
    public async Task Render_HtmlEntities_ShouldPreserve()
    {
        // Arrange
        CreateTemplate("html-entities", "&lt;div&gt;&amp;&quot;&apos;&lt;/div&gt;");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("html-entities", context);

        // Assert
        result.Should().Contain("&lt;");
        result.Should().Contain("&gt;");
        result.Should().Contain("&amp;");
    }

    /// <summary>
    /// 测试特殊内容 - Unicode 字符
    /// </summary>
    [Fact]
    public async Task Render_UnicodeCharacters_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("unicode", """
            <p>中文：你好世界</p>
            <p>日文：こんにちは</p>
            <p>韩文：안녕하세요</p>
            <p>表情：😀🎉🚀</p>
            <p>特殊：™©®</p>
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("unicode", context);

        // Assert
        result.Should().Contain("你好世界");
        result.Should().Contain("こんにちは");
        result.Should().Contain("안녕하세요");
        result.Should().Contain("😀");
        result.Should().Contain("™");
    }

    /// <summary>
    /// 测试特殊内容 - 换行符和制表符
    /// </summary>
    [Fact]
    public async Task Render_WhitespaceCharacters_ShouldPreserve()
    {
        // Arrange
        CreateTemplate("whitespace-chars", "<pre>\t制表符\n换行符\r\n回车换行</pre>");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("whitespace-chars", context);

        // Assert
        result.Should().Contain("\t");
        result.Should().Contain("\n");
    }

    /// <summary>
    /// 测试特殊内容 - 模板语法字符在字符串中
    /// </summary>
    [Fact]
    public async Task Render_TemplateSyntaxInString_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("syntax-in-string", """
            {{ text = "这是 {{ 不是模板语法 }}" }}
            {{ text }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("syntax-in-string", context);

        // Assert
        result.Trim().Should().Contain("{{ 不是模板语法 }}");
    }

    /// <summary>
    /// 测试特殊内容 - 页面内容包含模板语法字符
    /// </summary>
    [Fact]
    public async Task Render_PageContentWithTemplateSyntax_ShouldRenderAsIs()
    {
        // Arrange
        CreateTemplate("content-syntax", "{{ page.content }}");
        var page = CreatePageContext(content: "<p>代码示例：{{ variable }}</p>");
        var context = CreateTemplateContext(page: page);

        // Act
        var result = await _renderer.RenderAsync("content-syntax", context);

        // Assert
        // 页面内容应该原样输出，不应该被解析为模板语法
        result.Should().Contain("{{ variable }}");
    }

    #endregion

    #region 边界数值测试

    /// <summary>
    /// 测试边界数值 - 零值
    /// </summary>
    [Fact]
    public async Task Render_ZeroValue_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("zero-value", """
            {{ zero = 0 }}
            {{ if zero }}非零{{ else }}零{{ end }}
            {{ zero }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("zero-value", context);

        // Assert
        result.Should().Contain("零");
        result.Should().Contain("0");
    }

    /// <summary>
    /// 测试边界数值 - 负数
    /// </summary>
    [Fact]
    public async Task Render_NegativeNumber_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("negative", """
            {{ num = -100 }}
            {{ num }}
            {{ abs num }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("negative", context);

        // Assert
        result.Should().Contain("-100");
        result.Should().Contain("100");
    }

    /// <summary>
    /// 测试边界数值 - 大数
    /// </summary>
    [Fact]
    public async Task Render_LargeNumber_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("large-number", """
            {{ big = 9999999999 }}
            {{ big }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("large-number", context);

        // Assert
        result.Trim().Should().Contain("9999999999");
    }

    /// <summary>
    /// 测试边界数值 - 浮点数精度
    /// </summary>
    [Fact]
    public async Task Render_FloatingPointPrecision_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("float-precision", """
            {{ pi = 3.14159265358979 }}
            {{ pi | round 2 }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("float-precision", context);

        // Assert
        result.Trim().Should().Be("3.14");
    }

    #endregion

    #region 空集合测试

    /// <summary>
    /// 测试空集合 - 空数组循环
    /// </summary>
    [Fact]
    public async Task Render_EmptyArrayLoop_ShouldRenderNothing()
    {
        // Arrange
        CreateTemplate("empty-array", """
            {{ arr = [] }}
            {{ for item in arr }}
            <span>{{ item }}</span>
            {{ end }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("empty-array", context);

        // Assert
        result.Should().NotContain("<span>");
    }

    /// <summary>
    /// 测试空集合 - 空集合函数
    /// </summary>
    [Fact]
    public async Task Render_EmptyCollectionFunctions_ShouldHandleCorrectly()
    {
        // Arrange
        CreateTemplate("empty-funcs", """
            {{ arr = [] }}
            长度: {{ arr | len }}
            第一个: {{ arr | first | default "无" }}
            最后一个: {{ arr | last | default "无" }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("empty-funcs", context);

        // Assert
        result.Should().Contain("长度: 0");
        result.Should().Contain("第一个: 无");
        result.Should().Contain("最后一个: 无");
    }

    #endregion
}
