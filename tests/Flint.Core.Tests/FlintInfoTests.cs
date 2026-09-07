// Flint 核心库测试
// FlintInfo 类的单元测试

using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests;

/// <summary>
/// FlintInfo 类的单元测试
/// </summary>
public class FlintInfoTests
{
    [Fact]
    public void ProductName_应该返回Flint()
    {
        // Assert
        FlintInfo.ProductName.Should().Be("Flint");
    }

    [Fact]
    public void Version_应该是有效的语义化版本()
    {
        // Arrange
        var version = FlintInfo.Version;

        // Assert
        version.Should().NotBeNullOrEmpty();
        version.Should().MatchRegex(@"^\d+\.\d+\.\d+(-[\w\.]+)?$");
    }

    [Fact]
    public void VersionInfo_应该包含产品名称和版本()
    {
        // Act
        var versionInfo = FlintInfo.VersionInfo;

        // Assert
        versionInfo.Should().Contain(FlintInfo.ProductName);
        versionInfo.Should().Contain(FlintInfo.Version);
    }

    [Fact]
    public void DetailedVersionInfo_应该包含运行时信息()
    {
        // Act
        var detailedInfo = FlintInfo.DetailedVersionInfo;

        // Assert
        detailedInfo.Should().Contain(FlintInfo.ProductName);
        detailedInfo.Should().Contain("运行时:");
        detailedInfo.Should().Contain("操作系统:");
        detailedInfo.Should().Contain("架构:");
    }

    [Fact]
    public void Description_应该不为空()
    {
        // Assert
        FlintInfo.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Runtime_应该是NET10()
    {
        // Assert
        FlintInfo.Runtime.Should().Contain(".NET 10");
    }
}
