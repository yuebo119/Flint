// Flint 核心库测试
// BuildResult 数据类型单元测试

using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Models;

/// <summary>
/// BuildResult 类的单元测试
/// </summary>
public class BuildResultTests
{
    [Fact]
    public void BuildError_应该正确存储所有属性()
    {
        // Arrange & Act
        var error = new BuildError
        {
            ErrorCode = "TPL002",
            Message = "模板类型错误",
            FilePath = "layouts/single.html",
            Line = 25,
            Column = 10,
            Context = "{{ .Title | upper }}",
            Suggestion = "检查变量类型是否正确",
            Severity = ErrorSeverity.Error
        };

        // Assert
        error.ErrorCode.Should().Be("TPL002");
        error.Message.Should().Be("模板类型错误");
        error.FilePath.Should().Be("layouts/single.html");
        error.Line.Should().Be(25);
        error.Column.Should().Be(10);
        error.Context.Should().Be("{{ .Title | upper }}");
        error.Suggestion.Should().Be("检查变量类型是否正确");
        error.Severity.Should().Be(ErrorSeverity.Error);
    }

    [Fact]
    public void BuildWarning_应该正确存储所有属性()
    {
        // Arrange & Act
        var warning = new BuildWarning
        {
            WarningCode = "W002",
            Message = "图片缺少 alt 属性",
            FilePath = "content/posts/test.md",
            Line = 30,
            Column = 5
        };

        // Assert
        warning.WarningCode.Should().Be("W002");
        warning.Message.Should().Be("图片缺少 alt 属性");
        warning.FilePath.Should().Be("content/posts/test.md");
        warning.Line.Should().Be(30);
        warning.Column.Should().Be(5);
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Error)]
    [InlineData(ErrorSeverity.Fatal)]
    public void BuildError_应该支持所有严重程度(ErrorSeverity severity)
    {
        // Arrange & Act
        var error = new BuildError
        {
            ErrorCode = "E001",
            Message = "测试错误",
            FilePath = "test.md",
            Line = 1,
            Column = 1,
            Severity = severity
        };

        // Assert
        error.Severity.Should().Be(severity);
    }

    [Fact]
    public void BuildResult_应该正确计算压缩比()
    {
        // Arrange
        var asset = new ProcessedAsset
        {
            OutputPath = "styles.css",
            MediaType = "text/css",
            Content = new byte[800],
            ContentHash = "abc123",
            SourcePath = "styles.scss",
            IsMinified = true,
            OriginalSize = 1000
        };

        // Act & Assert
        asset.CompressionRatio.Should().BeApproximately(0.8, 0.001);
    }

    [Fact]
    public void ProcessedAsset_原始大小为零时压缩比应该为1()
    {
        // Arrange
        var asset = new ProcessedAsset
        {
            OutputPath = "test.css",
            MediaType = "text/css",
            Content = new byte[100],
            ContentHash = "abc123",
            SourcePath = "test.scss",
            IsMinified = false,
            OriginalSize = 0
        };

        // Act & Assert
        asset.CompressionRatio.Should().Be(1.0);
    }
}
