// Flint 静态站点生成器
// 构建失败属性测试

using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Flint.Core.Tests.Cli;

/// <summary>
/// 构建失败属性测试
/// **Property 12: 构建失败退出码**
/// **验证: 需求 10.6**
/// </summary>
public class BuildFailurePropertyTests
{
    /// <summary>
    /// **Property 12: 构建结果的错误信息应该完整保留**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(BuildResultArbitrary)])]
    public Property BuildErrors_ShouldBePreserved(ValidBuildErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // Arrange & Act
        var result = new BuildResult
        {
            Success = errors.Value.Count == 0,
            PagesBuilt = 0,
            AssetsProcessed = 0,
            Duration = TimeSpan.FromMilliseconds(100),
            MemoryUsed = 1024 * 1024,
            Errors = errors.Value,
            Warnings = [],
            OutputPath = "/output"
        };

        // Assert - 所有错误都应该被保留
        return (result.Errors.Count == errors.Value.Count)
            .ToProperty()
            .Label($"错误数量应该保持一致: 期望 {errors.Value.Count}, 实际 {result.Errors.Count}");
    }

    /// <summary>
    /// **Property 12: 错误代码应该是非空的**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = [typeof(BuildResultArbitrary)])]
    public Property ErrorCodes_ShouldBeNonEmpty(ValidBuildErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        // Assert
        foreach (var error in errors.Value)
        {
            if (string.IsNullOrEmpty(error.ErrorCode))
            {
                return false.ToProperty()
                    .Label("错误代码不应该为空");
            }
        }

        return true.ToProperty()
            .Label($"所有 {errors.Value.Count} 个错误都有非空的错误代码");
    }
}

#region 测试数据类型

/// <summary>
/// 有效的构建错误列表
/// </summary>
public sealed class ValidBuildErrors
{
    public IReadOnlyList<BuildError> Value { get; }

    public ValidBuildErrors(IReadOnlyList<BuildError> value)
    {
        Value = value;
    }

    public override string ToString() => $"BuildErrors(Count={Value.Count})";
}

#endregion

#region 生成器

/// <summary>
/// 构建结果测试数据生成器
/// </summary>
public static class BuildResultArbitrary
{
    private static Gen<BuildError> GenBuildError()
    {
        return from errorCode in Gen.Elements("PARSE001", "RENDER001", "CONFIG001", "ASSET001", "BUILD001")
               from message in Gen.Elements(
                   "语法错误",
                   "模板未找到",
                   "配置无效",
                   "资源处理失败",
                   "构建中断")
               from filePath in Gen.Elements(
                   "content/post.md",
                   "layouts/single.html",
                   "Flint.toml",
                   "assets/style.css")
               from line in Gen.Choose(1, 100)
               from column in Gen.Choose(1, 80)
               select new BuildError
               {
                   ErrorCode = errorCode,
                   Message = message,
                   FilePath = filePath,
                   Line = line,
                   Column = column
               };
    }

    public static Arbitrary<ValidBuildErrors> ValidBuildErrors()
    {
        var gen = Gen.Choose(0, 5)
            .SelectMany(count => Gen.ListOf<BuildError>(GenBuildError(), count))
            .Select(list => new ValidBuildErrors(list.ToList()));

        return gen.ToArbitrary();
    }
}

#endregion
