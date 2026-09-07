// Flint 核心库测试
// ContentFile 数据类型单元测试

using System.Text;
using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Models;

/// <summary>
/// ContentFile 记录结构的单元测试
/// </summary>
public class ContentFileTests
{
    [Fact]
    public void ContentFile_应该正确存储路径和内容()
    {
        // Arrange
        var path = "content/posts/hello.md";
        var content = "# Hello World"u8.ToArray();
        var modifiedTime = DateTimeOffset.Now;

        // Act
        var file = new ContentFile
        {
            Path = path,
            RawContent = content,
            ModifiedTime = modifiedTime
        };

        // Assert
        file.Path.Should().Be(path);
        file.RawContent.ToArray().Should().BeEquivalentTo(content);
        file.ModifiedTime.Should().Be(modifiedTime);
    }

    [Fact]
    public void ContentFile_应该正确计算内容长度()
    {
        // Arrange
        var content = "测试内容"u8.ToArray();
        var file = new ContentFile
        {
            Path = "test.md",
            RawContent = content,
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        file.Length.Should().Be(content.Length);
    }

    [Fact]
    public void ContentFile_应该正确获取文件扩展名()
    {
        // Arrange
        var file = new ContentFile
        {
            Path = "content/posts/hello.MD",
            RawContent = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        file.Extension.Should().Be(".md");
    }

    [Fact]
    public void ContentFile_应该正确获取文件名()
    {
        // Arrange
        var file = new ContentFile
        {
            Path = "content/posts/hello-world.md",
            RawContent = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        file.FileName.Should().Be("hello-world.md");
    }

    [Fact]
    public void ContentFile_应该正确转换内容为字符串()
    {
        // Arrange
        var originalContent = "# 你好世界\n\n这是测试内容。";
        var bytes = Encoding.UTF8.GetBytes(originalContent);
        var file = new ContentFile
        {
            Path = "test.md",
            RawContent = bytes,
            ModifiedTime = DateTimeOffset.Now
        };

        // Act
        var result = file.GetContentAsString();

        // Assert
        result.Should().Be(originalContent);
    }

    [Fact]
    public void ContentFile_记录类型应该支持相等性比较()
    {
        // Arrange
        var content = "test"u8.ToArray();
        var time = DateTimeOffset.Now;

        var file1 = new ContentFile
        {
            Path = "test.md",
            RawContent = content,
            ModifiedTime = time
        };

        var file2 = new ContentFile
        {
            Path = "test.md",
            RawContent = content,
            ModifiedTime = time
        };

        // Act & Assert
        file1.Should().Be(file2);
        (file1 == file2).Should().BeTrue();
    }

    [Fact]
    public void ContentFile_不同内容应该不相等()
    {
        // Arrange
        var time = DateTimeOffset.Now;

        var file1 = new ContentFile
        {
            Path = "test.md",
            RawContent = "content1"u8.ToArray(),
            ModifiedTime = time
        };

        var file2 = new ContentFile
        {
            Path = "test.md",
            RawContent = "content2"u8.ToArray(),
            ModifiedTime = time
        };

        // Act & Assert
        file1.Should().NotBe(file2);
    }

    [Fact]
    public void ContentFile_ContentSpan应该返回正确的Span()
    {
        // Arrange
        var content = "Hello"u8.ToArray();
        var file = new ContentFile
        {
            Path = "test.md",
            RawContent = content,
            ModifiedTime = DateTimeOffset.Now
        };

        // Act
        var span = file.ContentSpan;

        // Assert
        span.Length.Should().Be(5);
        span.SequenceEqual(content).Should().BeTrue();
    }
}
