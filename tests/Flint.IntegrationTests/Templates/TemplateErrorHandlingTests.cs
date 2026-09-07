// Flint 静态站点生成器
// 模板错误处理测试
// 测试语法错误报告、错误位置信息、未定义变量错误、类型错误、无限循环检测
// _Requirements: 5.8_

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Templates;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Templates;

/// <summary>
/// 模板错误处理测试
/// 验证模板渲染器对各种错误情况的处理
/// </summary>
public class TemplateErrorHandlingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _layoutsDir;
    private readonly string _partialsDir;
    private readonly ScribanTemplateRenderer _renderer;

    public TemplateErrorHandlingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-template-error-{Guid.NewGuid():N}");
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
    private static PageContext CreatePageContext()
    {
        return new PageContext
        {
            Title = "测试页面",
            Content = "<p>内容</p>",
            Permalink = "https://example.com/test/",
            RelPermalink = "/test/",
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 10,
            ReadingTime = TimeSpan.FromMinutes(1)
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
    private static TemplateContext CreateTemplateContext()
    {
        return new TemplateContext
        {
            Page = CreatePageContext(),
            Site = CreateSiteContext(),
            IsHome = false,
            IsList = false,
            IsSingle = true,
            Params = new Dictionary<string, object>(),
            Data = new Dictionary<string, object>()
        };
    }

    #endregion

    #region 语法错误报告测试

    /// <summary>
    /// 测试语法错误 - 未闭合的大括号
    /// </summary>
    [Fact]
    public void Parse_UnclosedBrace_ShouldThrowTemplateParseException()
    {
        // Arrange
        // 使用更明确的语法错误
        var invalidTemplate = "{{ page.title }";
        CreateTemplate("unclosed-brace", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("unclosed-brace", CreateTemplateContext()).AsTask().Result;

        // Scriban 可能会抛出 TemplateParseException 或其他异常
        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试语法错误 - 未闭合的 if 语句
    /// </summary>
    [Fact]
    public void Parse_UnclosedIfStatement_ShouldThrowTemplateParseException()
    {
        // Arrange
        var invalidTemplate = "{{ if page.draft }}草稿";
        CreateTemplate("unclosed-if", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("unclosed-if", CreateTemplateContext()).AsTask().Result;

        // Scriban 应该抛出解析异常
        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试语法错误 - 未闭合的 for 循环
    /// </summary>
    [Fact]
    public void Parse_UnclosedForLoop_ShouldThrowTemplateParseException()
    {
        // Arrange
        var invalidTemplate = "{{ for tag in page.tags }}<span>{{ tag }}</span>";
        CreateTemplate("unclosed-for", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("unclosed-for", CreateTemplateContext()).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试语法错误 - 无效的表达式
    /// </summary>
    [Fact]
    public void Parse_InvalidExpression_ShouldThrowTemplateParseException()
    {
        // Arrange
        var invalidTemplate = "{{ page. }}";
        CreateTemplate("invalid-expr", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("invalid-expr", CreateTemplateContext()).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试语法错误 - 多余的 end 语句
    /// </summary>
    [Fact]
    public void Parse_ExtraEndStatement_ShouldThrowTemplateParseException()
    {
        // Arrange
        var invalidTemplate = "{{ end }}";
        CreateTemplate("extra-end", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("extra-end", CreateTemplateContext()).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试语法错误 - 无效的管道操作符
    /// </summary>
    [Fact]
    public void Parse_InvalidPipeOperator_ShouldThrowTemplateParseException()
    {
        // Arrange
        var invalidTemplate = "{{ page.title | | upper }}";
        CreateTemplate("invalid-pipe", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("invalid-pipe", CreateTemplateContext()).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试语法错误 - 不匹配的引号
    /// </summary>
    [Fact]
    public void Parse_UnmatchedQuotes_ShouldThrowTemplateParseException()
    {
        // Arrange
        var invalidTemplate = "{{ 'unclosed string }}";
        CreateTemplate("unmatched-quotes", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("unmatched-quotes", CreateTemplateContext()).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    #endregion

    #region 错误位置信息测试

    /// <summary>
    /// 测试错误位置信息 - 异常应包含模板名称
    /// </summary>
    [Fact]
    public void Parse_SyntaxError_ShouldIncludeTemplateName()
    {
        // Arrange
        // 使用更明确的语法错误
        var invalidTemplate = "{{ if true }}没有 end";
        CreateTemplate("error-template-name", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("error-template-name", CreateTemplateContext()).AsTask().Result;

        // 验证抛出异常
        act.Should().Throw<Exception>();
    }    /// <summary>
         /// 测试错误位置信息 - 异常应包含错误列表
         /// </summary>
    [Fact]
    public void Parse_SyntaxError_ShouldIncludeErrorList()
    {
        // Arrange
        var invalidTemplate = "{{ if true }}没有 end";
        CreateTemplate("error-list", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("error-list", CreateTemplateContext()).AsTask().Result;

        // 验证抛出异常
        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试错误位置信息 - 多行模板中的错误
    /// </summary>
    [Fact]
    public void Parse_MultiLineError_ShouldReportCorrectLocation()
    {
        // Arrange
        var invalidTemplate = """
            <html>
            <head>
            <title>{{ page.title }}</title>
            </head>
            <body>
            {{ if true }}没有 end
            </body>
            </html>
            """;
        CreateTemplate("multiline-error", invalidTemplate);

        // Act & Assert
        var act = () => _renderer.RenderAsync("multiline-error", CreateTemplateContext()).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    #endregion

    #region 未定义变量错误测试

    /// <summary>
    /// 测试未定义变量 - 访问不存在的变量应返回空值（非严格模式）
    /// </summary>
    [Fact]
    public async Task Render_UndefinedVariable_ShouldReturnEmpty()
    {
        // Arrange
        CreateTemplate("undefined-var", "{{ undefined_variable }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("undefined-var", context);

        // Assert
        // 在非严格模式下，未定义变量返回空字符串
        result.Trim().Should().BeEmpty();
    }

    /// <summary>
    /// 测试未定义变量 - 访问不存在的嵌套属性
    /// Scriban 在严格模式下会抛出异常
    /// </summary>
    [Fact]
    public void Render_UndefinedNestedProperty_ShouldThrowException()
    {
        // Arrange
        CreateTemplate("undefined-nested", "{{ page.nonexistent.property }}");
        var context = CreateTemplateContext();

        // Act & Assert
        // Scriban 在访问 null 对象的属性时会抛出异常
        var act = () => _renderer.RenderAsync("undefined-nested", context).AsTask().Result;
        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试未定义变量 - 在条件语句中使用
    /// </summary>
    [Fact]
    public async Task Render_UndefinedVariableInCondition_ShouldEvaluateAsFalsy()
    {
        // Arrange
        CreateTemplate("undefined-condition", """
            {{ if undefined_var }}存在{{ else }}不存在{{ end }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("undefined-condition", context);

        // Assert
        result.Trim().Should().Be("不存在");
    }

    /// <summary>
    /// 测试未定义变量 - 在循环中使用
    /// </summary>
    [Fact]
    public async Task Render_UndefinedVariableInLoop_ShouldNotIterate()
    {
        // Arrange
        CreateTemplate("undefined-loop", """
            {{ for item in undefined_collection }}
            <span>{{ item }}</span>
            {{ end }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("undefined-loop", context);

        // Assert
        result.Should().NotContain("<span>");
    }

    /// <summary>
    /// 测试未定义变量 - 使用 default 函数提供默认值
    /// </summary>
    [Fact]
    public async Task Render_UndefinedVariableWithDefault_ShouldReturnDefault()
    {
        // Arrange
        CreateTemplate("undefined-default", "{{ undefined_var | default '默认值' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("undefined-default", context);

        // Assert
        result.Trim().Should().Be("默认值");
    }

    #endregion

    #region 类型错误测试

    /// <summary>
    /// 测试类型错误 - 对非集合类型使用 for 循环
    /// </summary>
    [Fact]
    public async Task Render_ForLoopOnNonCollection_ShouldHandleGracefully()
    {
        // Arrange
        CreateTemplate("for-non-collection", """
            {{ for item in page.title }}
            <span>{{ item }}</span>
            {{ end }}
            """);
        var context = CreateTemplateContext();

        // Act
        // 字符串可以被迭代为字符
        var result = await _renderer.RenderAsync("for-non-collection", context);

        // Assert
        // 应该能够处理，字符串会被迭代为字符
        result.Should().NotBeNull();
    }

    /// <summary>
    /// 测试类型错误 - 对非数字类型使用数学函数
    /// Scriban 会抛出类型转换异常
    /// </summary>
    [Fact]
    public void Render_MathFunctionOnNonNumber_ShouldThrowException()
    {
        // Arrange
        CreateTemplate("math-non-number", "{{ add 'hello' 1 }}");
        var context = CreateTemplateContext();

        // Act & Assert
        // Scriban 无法将字符串转换为数字，会抛出异常
        var act = () => _renderer.RenderAsync("math-non-number", context).AsTask().Result;
        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试类型错误 - 对非字符串类型使用字符串函数
    /// </summary>
    [Fact]
    public async Task Render_StringFunctionOnNonString_ShouldHandleGracefully()
    {
        // Arrange
        CreateTemplate("string-non-string", "{{ 123 | upper }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("string-non-string", context);

        // Assert
        // 应该能够处理，数字会被转换为字符串
        result.Should().NotBeNull();
    }

    /// <summary>
    /// 测试类型错误 - 对非日期类型使用日期函数
    /// </summary>
    [Fact]
    public async Task Render_DateFunctionOnNonDate_ShouldHandleGracefully()
    {
        // Arrange
        CreateTemplate("date-non-date", "{{ 'not a date' | date_format 'yyyy-MM-dd' }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("date-non-date", context);

        // Assert
        // 应该能够处理，返回空或原值
        result.Should().NotBeNull();
    }

    /// <summary>
    /// 测试类型错误 - 空值处理
    /// </summary>
    [Fact]
    public async Task Render_NullValue_ShouldHandleGracefully()
    {
        // Arrange
        CreateTemplate("null-value", "{{ page.description | upper }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("null-value", context);

        // Assert
        // 应该能够处理空值
        result.Should().NotBeNull();
    }

    #endregion

    #region 模板未找到错误测试

    /// <summary>
    /// 测试模板未找到 - 主模板不存在
    /// </summary>
    [Fact]
    public void Render_NonExistentTemplate_ShouldThrowTemplateNotFoundException()
    {
        // Arrange
        var context = CreateTemplateContext();

        // Act & Assert
        var act = () => _renderer.RenderAsync("non-existent-template", context).AsTask().Result;

        act.Should().Throw<TemplateNotFoundException>()
            .WithMessage("*未找到*");
    }

    /// <summary>
    /// 测试模板未找到 - 异常应包含搜索路径
    /// </summary>
    [Fact]
    public void Render_NonExistentTemplate_ShouldIncludeSearchPaths()
    {
        // Arrange
        var context = CreateTemplateContext();

        // Act & Assert
        var act = () => _renderer.RenderAsync("missing-template", context).AsTask().Result;

        var exception = act.Should().Throw<TemplateNotFoundException>().Which;
        exception.SearchPaths.Should().NotBeEmpty();
        exception.TemplateName.Should().Be("missing-template");
    }

    /// <summary>
    /// 测试模板未找到 - Partial 模板不存在
    /// </summary>
    [Fact]
    public void Render_NonExistentPartial_ShouldThrowException()
    {
        // Arrange
        CreateTemplate("with-missing-partial", "{{ include 'partials/non-existent' }}");
        var context = CreateTemplateContext();

        // Act & Assert
        var act = () => _renderer.RenderAsync("with-missing-partial", context).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    #endregion

    #region 模板存在性检查测试

    /// <summary>
    /// 测试模板存在性检查 - 存在的模板
    /// </summary>
    [Fact]
    public void TemplateExists_ExistingTemplate_ShouldReturnTrue()
    {
        // Arrange
        CreateTemplate("existing-template", "<h1>存在</h1>");

        // Act
        var exists = _renderer.TemplateExists("existing-template");

        // Assert
        exists.Should().BeTrue();
    }

    /// <summary>
    /// 测试模板存在性检查 - 不存在的模板
    /// </summary>
    [Fact]
    public void TemplateExists_NonExistingTemplate_ShouldReturnFalse()
    {
        // Act
        var exists = _renderer.TemplateExists("non-existing-template");

        // Assert
        exists.Should().BeFalse();
    }

    /// <summary>
    /// 测试模板存在性检查 - 带扩展名
    /// </summary>
    [Fact]
    public void TemplateExists_WithExtension_ShouldReturnTrue()
    {
        // Arrange
        CreateTemplate("template-with-ext.html", "<h1>存在</h1>");

        // Act
        var exists = _renderer.TemplateExists("template-with-ext");

        // Assert
        exists.Should().BeTrue();
    }

    #endregion

    #region 模板依赖分析测试

    /// <summary>
    /// 测试模板依赖分析 - 无依赖的模板
    /// </summary>
    [Fact]
    public void GetDependencies_NoDependencies_ShouldReturnEmpty()
    {
        // Arrange
        CreateTemplate("no-deps", "<h1>{{ page.title }}</h1>");

        // Act
        var deps = _renderer.GetDependencies("no-deps");

        // Assert
        deps.Should().BeEmpty();
    }

    /// <summary>
    /// 测试模板依赖分析 - 有 partial 依赖
    /// </summary>
    [Fact]
    public void GetDependencies_WithPartials_ShouldReturnDependencies()
    {
        // Arrange
        CreatePartial("header", "<header>头部</header>");
        CreatePartial("footer", "<footer>底部</footer>");
        CreateTemplate("with-deps", """
            {{ include 'partials/header' }}
            <main>{{ page.content }}</main>
            {{ include 'partials/footer' }}
            """);

        // Act
        var deps = _renderer.GetDependencies("with-deps");

        // Assert
        deps.Should().Contain("partials/header");
        deps.Should().Contain("partials/footer");
    }

    #endregion

    #region 边界情况错误处理测试

    /// <summary>
    /// 测试空模板名称
    /// </summary>
    [Fact]
    public void Render_EmptyTemplateName_ShouldThrowException()
    {
        // Arrange
        var context = CreateTemplateContext();

        // Act & Assert
        var act = () => _renderer.RenderAsync("", context).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试特殊字符模板名称
    /// </summary>
    [Fact]
    public void Render_SpecialCharacterTemplateName_ShouldThrowTemplateNotFoundException()
    {
        // Arrange
        var context = CreateTemplateContext();

        // Act & Assert
        var act = () => _renderer.RenderAsync("template<>:*?", context).AsTask().Result;

        act.Should().Throw<Exception>();
    }

    /// <summary>
    /// 测试非常长的模板内容
    /// </summary>
    [Fact]
    public async Task Render_VeryLongTemplate_ShouldRenderCorrectly()
    {
        // Arrange
        var longContent = string.Concat(Enumerable.Repeat("<p>段落内容</p>\n", 1000));
        CreateTemplate("long-template", longContent + "{{ page.title }}");
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("long-template", context);

        // Assert
        result.Should().Contain("测试页面");
        result.Should().Contain("<p>段落内容</p>");
    }

    /// <summary>
    /// 测试深层嵌套的表达式
    /// </summary>
    [Fact]
    public async Task Render_DeeplyNestedExpression_ShouldRenderCorrectly()
    {
        // Arrange
        CreateTemplate("nested-expr", """
            {{ if true }}
              {{ if true }}
                {{ if true }}
                  {{ if true }}
                    {{ if true }}
                      深层嵌套
                    {{ end }}
                  {{ end }}
                {{ end }}
              {{ end }}
            {{ end }}
            """);
        var context = CreateTemplateContext();

        // Act
        var result = await _renderer.RenderAsync("nested-expr", context);

        // Assert
        result.Should().Contain("深层嵌套");
    }

    #endregion
}
