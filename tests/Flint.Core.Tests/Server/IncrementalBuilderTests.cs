// Flint 静态站点生成器
// 增量构建器单元测试

#pragma warning disable CA2000 // 测试代码中不需要处理 Dispose
#pragma warning disable CA2007 // 测试代码中不需要 ConfigureAwait

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
}
