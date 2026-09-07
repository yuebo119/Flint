// Flint 静态站点生成器
// 增量构建属性测试

using Flint.Core.Site;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Server;

/// <summary>
/// 增量构建属性测试
/// **Property 10: 增量构建正确性**
/// **验证: 需求 6.3, 6.4**
/// </summary>
public class IncrementalBuildPropertyTests
{
    /// <summary>
    /// **Property 10: 变化的文件及其依赖应该被重新构建**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(IncrementalBuildArbitrary)])]
    public Property ChangedFiles_AndDependents_ShouldBeRebuilt(
        ValidDependencyGraph graph,
        ValidChangedFiles changedFiles)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(changedFiles);

        // Arrange
        var builder = new IncrementalBuilder();

        // 注册依赖关系
        foreach (var (file, deps) in graph.Dependencies)
        {
            builder.RegisterDependency(file, deps);
        }

        // Act
        var affected = builder.GetAffectedFiles(changedFiles.Value);

        // Assert - 所有变化的文件都应该在受影响列表中
        foreach (var changed in changedFiles.Value)
        {
            if (!affected.Contains(changed))
            {
                return false.ToProperty()
                    .Label($"变化的文件 '{changed}' 应该在受影响列表中");
            }
        }

        return true.ToProperty()
            .Label($"所有 {changedFiles.Value.Count} 个变化的文件都在受影响列表中");
    }

    /// <summary>
    /// **Property 10: 依赖变化文件的文件也应该被重新构建**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(IncrementalBuildArbitrary)])]
    public Property Dependents_ShouldBeIncluded(ValidDependencyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Arrange
        var builder = new IncrementalBuilder();

        foreach (var (file, deps) in graph.Dependencies)
        {
            builder.RegisterDependency(file, deps);
        }

        // 选择一个有依赖者的文件
        var fileWithDependents = graph.Dependencies
            .SelectMany(kvp => kvp.Value)
            .FirstOrDefault();

        if (fileWithDependents == null)
        {
            return true.ToProperty().Label("没有依赖关系");
        }

        // Act
        var affected = builder.GetAffectedFiles([fileWithDependents]);

        // Assert - 依赖此文件的文件也应该在受影响列表中
        var expectedDependents = graph.Dependencies
            .Where(kvp => kvp.Value.Contains(fileWithDependents))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var dependent in expectedDependents)
        {
            if (!affected.Contains(dependent))
            {
                return false.ToProperty()
                    .Label($"依赖 '{fileWithDependents}' 的文件 '{dependent}' 应该在受影响列表中");
            }
        }

        return true.ToProperty()
            .Label($"所有依赖者都在受影响列表中");
    }


    /// <summary>
    /// **Property 10: 传递依赖应该被正确追踪**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(IncrementalBuildArbitrary)])]
    public Property TransitiveDependencies_ShouldBeTracked(ValidDependencyChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        // Arrange
        var builder = new IncrementalBuilder();

        // 创建链式依赖: A -> B -> C
        for (var i = 0; i < chain.Files.Count - 1; i++)
        {
            builder.RegisterDependency(chain.Files[i], [chain.Files[i + 1]]);
        }

        // Act - 修改链的最后一个文件
        var lastFile = chain.Files[^1];
        var affected = builder.GetAffectedFiles([lastFile]);

        // Assert - 链中的所有文件都应该受影响
        foreach (var file in chain.Files)
        {
            if (!affected.Contains(file))
            {
                return false.ToProperty()
                    .Label($"链中的文件 '{file}' 应该在受影响列表中");
            }
        }

        return true.ToProperty()
            .Label($"链中所有 {chain.Files.Count} 个文件都受影响");
    }
}

#region 测试数据类型

/// <summary>
/// 有效的依赖图
/// </summary>
public sealed class ValidDependencyGraph
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; }

    public ValidDependencyGraph(IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        Dependencies = dependencies;
    }

    public override string ToString() => $"DependencyGraph(Files={Dependencies.Count})";
}

/// <summary>
/// 有效的变化文件列表
/// </summary>
public sealed class ValidChangedFiles
{
    public IReadOnlyList<string> Value { get; }

    public ValidChangedFiles(IReadOnlyList<string> value)
    {
        Value = value;
    }

    public override string ToString() => $"ChangedFiles({string.Join(", ", Value)})";
}

/// <summary>
/// 有效的依赖链
/// </summary>
public sealed class ValidDependencyChain
{
    public IReadOnlyList<string> Files { get; }

    public ValidDependencyChain(IReadOnlyList<string> files)
    {
        Files = files;
    }

    public override string ToString() => $"DependencyChain({string.Join(" -> ", Files)})";
}

#endregion

#region 生成器

/// <summary>
/// 增量构建测试数据生成器
/// </summary>
public static class IncrementalBuildArbitrary
{
    private static Gen<string> GenFileName()
    {
        return Gen.Elements(
            "content/post1.md", "content/post2.md", "content/post3.md",
            "content/page1.md", "content/page2.md",
            "layouts/single.html", "layouts/list.html", "layouts/base.html",
            "partials/header.html", "partials/footer.html",
            "assets/style.css", "assets/script.js"
        );
    }

    public static Arbitrary<ValidDependencyGraph> ValidDependencyGraph()
    {
        var gen = Gen.Choose(2, 8)
            .SelectMany(fileCount =>
            {
                var files = Enumerable.Range(0, fileCount)
                    .Select(i => $"file{i}.md")
                    .ToList();

                var deps = new Dictionary<string, IReadOnlyList<string>>();

                // 为每个文件随机分配依赖
                var rng = new System.Random();
                foreach (var file in files)
                {
                    var possibleDeps = files.Where(f => f != file).ToList();
                    var depCount = rng.Next(0, Math.Min(3, possibleDeps.Count));
                    var fileDeps = possibleDeps.Take(depCount).ToList();
                    deps[file] = fileDeps;
                }

                return Gen.Constant(new ValidDependencyGraph(deps));
            });

        return gen.ToArbitrary();
    }

    public static Arbitrary<ValidChangedFiles> ValidChangedFiles()
    {
        var gen = Gen.Choose(1, 3)
            .SelectMany(count =>
            {
                var files = Enumerable.Range(0, count)
                    .Select(i => $"file{i}.md")
                    .ToList();
                return Gen.Constant(new ValidChangedFiles(files));
            });

        return gen.ToArbitrary();
    }

    public static Arbitrary<ValidDependencyChain> ValidDependencyChain()
    {
        var gen = Gen.Choose(2, 5)
            .Select(length =>
            {
                var files = Enumerable.Range(0, length)
                    .Select(i => $"chain{i}.md")
                    .ToList();
                return new ValidDependencyChain(files);
            });

        return gen.ToArbitrary();
    }
}

#endregion
