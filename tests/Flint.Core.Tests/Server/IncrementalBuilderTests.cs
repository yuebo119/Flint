// Flint 静态站点生成器
// 增量构建器单元测试

#pragma warning disable CA2000 // 测试代码中不需要处理 Dispose
#pragma warning disable CA2007 // 测试代码中不需要 ConfigureAwait

using Flint.Core.Abstractions;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Server;

/// <summary>
/// IncrementalBuilder 单元测试
/// </summary>
public class IncrementalBuilderTests : IDisposable
{
    private readonly IncrementalBuilder _builder;
    private readonly string _testDir;

    public IncrementalBuilderTests()
    {
        _builder = new IncrementalBuilder();
        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_incr_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            { Directory.Delete(_testDir, true); }
            catch { }
        }
        GC.SuppressFinalize(this);
    }

    #region RegisterDependency 测试

    [Fact]
    public void RegisterDependency_应该正确注册依赖()
    {
        // Arrange
        var file = "content/post.md";
        var deps = new[] { "layouts/single.html", "layouts/base.html" };

        // Act
        _builder.RegisterDependency(file, deps);

        // Assert
        var dependencies = _builder.GetDependencies(file);
        Assert.Contains("layouts/single.html", dependencies);
        Assert.Contains("layouts/base.html", dependencies);
    }

    [Fact]
    public void RegisterDependency_应该更新反向依赖()
    {
        // Arrange
        var file1 = "content/post1.md";
        var file2 = "content/post2.md";
        var template = "layouts/single.html";

        // Act
        _builder.RegisterDependency(file1, new[] { template });
        _builder.RegisterDependency(file2, new[] { template });

        // Assert
        var dependents = _builder.GetDependents(template);
        Assert.Contains(file1, dependents);
        Assert.Contains(file2, dependents);
    }

    [Fact]
    public void RegisterDependency_空依赖列表应该正常工作()
    {
        // Arrange
        var file = "content/standalone.md";

        // Act
        _builder.RegisterDependency(file, Array.Empty<string>());

        // Assert
        var dependencies = _builder.GetDependencies(file);
        Assert.Empty(dependencies);
    }

    #endregion

    #region GetAffectedFiles 测试

    [Fact]
    public void GetAffectedFiles_应该返回变化的文件本身()
    {
        // Arrange
        var changedFile = "content/post.md";

        // Act
        var affected = _builder.GetAffectedFiles(new[] { changedFile });

        // Assert
        Assert.Contains(changedFile, affected);
    }

    [Fact]
    public void GetAffectedFiles_应该返回依赖变化文件的文件()
    {
        // Arrange
        var template = "layouts/single.html";
        var post1 = "content/post1.md";
        var post2 = "content/post2.md";

        _builder.RegisterDependency(post1, new[] { template });
        _builder.RegisterDependency(post2, new[] { template });

        // Act
        var affected = _builder.GetAffectedFiles(new[] { template });

        // Assert
        Assert.Contains(template, affected);
        Assert.Contains(post1, affected);
        Assert.Contains(post2, affected);
    }

    [Fact]
    public void GetAffectedFiles_应该递归查找依赖()
    {
        // Arrange
        var baseTemplate = "layouts/base.html";
        var singleTemplate = "layouts/single.html";
        var post = "content/post.md";

        _builder.RegisterDependency(singleTemplate, new[] { baseTemplate });
        _builder.RegisterDependency(post, new[] { singleTemplate });

        // Act
        var affected = _builder.GetAffectedFiles(new[] { baseTemplate });

        // Assert
        Assert.Contains(baseTemplate, affected);
        Assert.Contains(singleTemplate, affected);
        Assert.Contains(post, affected);
    }

    [Fact]
    public void GetAffectedFiles_应该避免循环依赖()
    {
        // Arrange
        var file1 = "file1.md";
        var file2 = "file2.md";

        _builder.RegisterDependency(file1, new[] { file2 });
        _builder.RegisterDependency(file2, new[] { file1 });

        // Act
        var affected = _builder.GetAffectedFiles(new[] { file1 });

        // Assert
        Assert.Contains(file1, affected);
        Assert.Contains(file2, affected);
        Assert.Equal(2, affected.Count);
    }

    [Fact]
    public void GetAffectedFiles_多个变化文件应该合并结果()
    {
        // Arrange
        var template1 = "layouts/single.html";
        var template2 = "layouts/list.html";
        var post = "content/post.md";
        var list = "content/_index.md";

        _builder.RegisterDependency(post, new[] { template1 });
        _builder.RegisterDependency(list, new[] { template2 });

        // Act
        var affected = _builder.GetAffectedFiles(new[] { template1, template2 });

        // Assert
        Assert.Contains(template1, affected);
        Assert.Contains(template2, affected);
        Assert.Contains(post, affected);
        Assert.Contains(list, affected);
    }

    #endregion

    #region NeedsRebuild 测试

    [Fact]
    public void NeedsRebuild_文件不存在应该返回true()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDir, "non_existent.md");

        // Act
        var needsRebuild = _builder.NeedsRebuild(nonExistentFile);

        // Assert
        Assert.True(needsRebuild);
    }

    [Fact]
    public void NeedsRebuild_未缓存的文件应该返回true()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.md");
        File.WriteAllText(filePath, "test content");

        // Act
        var needsRebuild = _builder.NeedsRebuild(filePath);

        // Assert
        Assert.True(needsRebuild);
    }

    [Fact]
    public void NeedsRebuild_缓存后未修改应该返回false()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.md");
        File.WriteAllText(filePath, "test content");
        _builder.UpdateCache(filePath);

        // Act
        var needsRebuild = _builder.NeedsRebuild(filePath);

        // Assert
        Assert.False(needsRebuild);
    }

    [Fact]
    public async Task NeedsRebuild_文件修改后应该返回true()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.md");
        File.WriteAllText(filePath, "test content");
        _builder.UpdateCache(filePath);

        // 等待一小段时间确保时间戳不同
        await Task.Delay(100);

        // 修改文件
        File.WriteAllText(filePath, "modified content");

        // Act
        var needsRebuild = _builder.NeedsRebuild(filePath);

        // Assert
        Assert.True(needsRebuild);
    }

    #endregion

    #region UpdateCache 测试

    [Fact]
    public void UpdateCache_应该缓存文件信息()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.md");
        File.WriteAllText(filePath, "test content");

        // Act
        _builder.UpdateCache(filePath);

        // Assert
        Assert.False(_builder.NeedsRebuild(filePath));
    }

    [Fact]
    public void UpdateCache_文件不存在应该移除缓存()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.md");
        File.WriteAllText(filePath, "test content");
        _builder.UpdateCache(filePath);

        // 删除文件
        File.Delete(filePath);

        // Act
        _builder.UpdateCache(filePath);

        // Assert
        Assert.True(_builder.NeedsRebuild(filePath));
    }

    #endregion

    #region ClearCache 测试

    [Fact]
    public void ClearCache_应该清除所有缓存()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "test.md");
        File.WriteAllText(filePath, "test content");
        _builder.UpdateCache(filePath);
        _builder.RegisterDependency("file1.md", new[] { "file2.md" });

        // Act
        _builder.ClearCache();

        // Assert
        Assert.True(_builder.NeedsRebuild(filePath));
        Assert.Empty(_builder.GetDependencies("file1.md"));
    }

    #endregion

    #region GetDependencies 测试

    [Fact]
    public void GetDependencies_未注册的文件应该返回空集合()
    {
        // Act
        var deps = _builder.GetDependencies("unknown.md");

        // Assert
        Assert.Empty(deps);
    }

    #endregion

    #region GetDependents 测试

    [Fact]
    public void GetDependents_未被依赖的文件应该返回空集合()
    {
        // Act
        var dependents = _builder.GetDependents("unknown.md");

        // Assert
        Assert.Empty(dependents);
    }

    #endregion
}

/// <summary>
/// TemplateDependencyAnalyzer 单元测试
/// </summary>
public class TemplateDependencyAnalyzerTests
{
    [Fact]
    public void AnalyzeDependencies_应该返回模板依赖()
    {
        // Arrange
        var templateRenderer = new StubTemplateRenderer(["base", "header", "footer"]);
        var analyzer = new TemplateDependencyAnalyzer(templateRenderer);

        // Act
        var result = analyzer.AnalyzeDependencies("single");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("base", result);
        Assert.Contains("header", result);
        Assert.Contains("footer", result);
    }

    [Fact]
    public void AnalyzeDependencies_无依赖时应该返回空列表()
    {
        // Arrange
        var templateRenderer = new StubTemplateRenderer([]);
        var analyzer = new TemplateDependencyAnalyzer(templateRenderer);

        // Act
        var result = analyzer.AnalyzeDependencies("standalone");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetContentUsingTemplate_应该返回使用指定模板的内容()
    {
        // Arrange
        var templateRenderer = new StubTemplateRenderer([]);
        var analyzer = new TemplateDependencyAnalyzer(templateRenderer);
        var contentFiles = new[] { "post1.md", "post2.md", "page1.md" };

        // Act
        var result = analyzer.GetContentUsingTemplate(
            "single",
            contentFiles,
            file => file.StartsWith("post") ? "single" : "page");

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains("post1.md", result);
        Assert.Contains("post2.md", result);
    }

    [Fact]
    public void GetContentUsingTemplate_应该包含依赖模板的内容()
    {
        // Arrange
        var templateRenderer = new StubTemplateRenderer(["single"]);
        var analyzer = new TemplateDependencyAnalyzer(templateRenderer);
        var contentFiles = new[] { "post1.md", "post2.md" };

        // Act
        var result = analyzer.GetContentUsingTemplate(
            "base",
            contentFiles,
            _ => "single");

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetContentUsingTemplate_默认布局应该是single()
    {
        // Arrange
        var templateRenderer = new StubTemplateRenderer([]);
        var analyzer = new TemplateDependencyAnalyzer(templateRenderer);
        var contentFiles = new[] { "post1.md" };

        // Act
        var result = analyzer.GetContentUsingTemplate(
            "single",
            contentFiles,
            _ => null); // 返回 null 表示使用默认布局

        // Assert
        Assert.Single(result);
        Assert.Contains("post1.md", result);
    }

    #region Stub 类

    private sealed class StubTemplateRenderer : ITemplateRenderer
    {
        private readonly IReadOnlyList<string> _dependencies;

        public StubTemplateRenderer(IReadOnlyList<string> dependencies)
        {
            _dependencies = dependencies;
        }

        public ValueTask<string> RenderAsync(string templateName, TemplateContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult("<html></html>");
        }

        public void Render(string templateName, TemplateContext context, System.Buffers.IBufferWriter<char> output)
        {
            var html = "<html></html>";
            var span = output.GetSpan(html.Length);
            html.AsSpan().CopyTo(span);
            output.Advance(html.Length);
        }

        public ValueTask<string> RenderStringAsync(string templateContent, TemplateContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult("<html></html>");
        }

        public bool TemplateExists(string templateName) => true;

        public IReadOnlyList<string> GetDependencies(string templateName) => _dependencies;
    }

    #endregion
}
