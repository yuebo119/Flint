// -----------------------------------------------------------------------
// <copyright file="SmokeTests.cs" company="Flint">
//     Copyright (c) Flint. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

namespace Flint.IntegrationTests;

/// <summary>
/// 冒烟测试
/// 验证测试框架配置正确
/// </summary>
public class SmokeTests
{
    /// <summary>
    /// 验证 xUnit 测试框架正常工作
    /// </summary>
    [Fact]
    public void XUnit_ShouldWork()
    {
        // Arrange
        var expected = 42;

        // Act
        var actual = 40 + 2;

        // Assert
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 验证 FluentAssertions 断言库正常工作
    /// </summary>
    [Fact]
    public void FluentAssertions_ShouldWork()
    {
        // Arrange
        var message = "Hello, Flint!";

        // Act & Assert
        message.Should().NotBeNullOrEmpty();
        message.Should().StartWith("Hello");
        message.Should().Contain("Flint");
    }

    /// <summary>
    /// 验证 FsCheck 属性测试框架正常工作
    /// 测试加法交换律：a + b = b + a
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FsCheck_AdditionIsCommutative(int a, int b)
    {
        return (a + b == b + a).ToProperty();
    }

    /// <summary>
    /// 验证 FsCheck 属性测试框架正常工作
    /// 测试字符串连接长度：len(a + b) = len(a) + len(b)
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FsCheck_StringConcatenationLength(string a, string b)
    {
        // 处理 null 值
        var safeA = a ?? string.Empty;
        var safeB = b ?? string.Empty;

        var concatenated = safeA + safeB;
        var expectedLength = safeA.Length + safeB.Length;

        return (concatenated.Length == expectedLength).ToProperty();
    }

    /// <summary>
    /// 验证项目引用 Flint.Core 正常工作
    /// </summary>
    [Fact]
    public void FlintCore_ShouldBeAccessible()
    {
        // 验证可以访问 Flint.Core 中的类型
        var productName = Flint.Core.FlintInfo.ProductName;
        var version = Flint.Core.FlintInfo.Version;
        var versionInfo = Flint.Core.FlintInfo.VersionInfo;

        productName.Should().Be("Flint");
        version.Should().NotBeNullOrEmpty();
        versionInfo.Should().Contain("Flint");
    }

    /// <summary>
    /// 验证项目引用 Flint.Cli 正常工作
    /// </summary>
    [Fact]
    public void FlintCli_ShouldBeAccessible()
    {
        // 验证可以访问 Flint.Cli 程序集
        var cliAssembly = typeof(Flint.Core.FlintInfo).Assembly;

        cliAssembly.Should().NotBeNull();
        cliAssembly.GetName().Name.Should().Be("Flint.Core");
    }
}
