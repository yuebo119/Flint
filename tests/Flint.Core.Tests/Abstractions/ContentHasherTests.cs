// Flint 核心库测试
// ContentHasher 内容哈希计算器单元测试

using System.Text;
using Flint.Core.Abstractions;
using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Abstractions;

/// <summary>
/// ContentHasher 静态类的单元测试
/// </summary>
public class ContentHasherTests
{
    [Fact]
    public void ComputeHash_字节数组_应该返回正确的SHA256哈希()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();

        // Act
        var hash = ContentHasher.ComputeHash(content.AsSpan());

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64); // SHA256 = 32 bytes = 64 hex chars
        hash.Should().MatchRegex("^[a-f0-9]{64}$");
    }

    [Fact]
    public void ComputeHash_相同内容应该产生相同哈希()
    {
        // Arrange
        var content1 = "测试内容"u8.ToArray();
        var content2 = "测试内容"u8.ToArray();

        // Act
        var hash1 = ContentHasher.ComputeHash(content1.AsSpan());
        var hash2 = ContentHasher.ComputeHash(content2.AsSpan());

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeHash_不同内容应该产生不同哈希()
    {
        // Arrange
        var content1 = "内容1"u8.ToArray();
        var content2 = "内容2"u8.ToArray();

        // Act
        var hash1 = ContentHasher.ComputeHash(content1.AsSpan());
        var hash2 = ContentHasher.ComputeHash(content2.AsSpan());

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_字符串_应该返回正确的哈希()
    {
        // Arrange
        var content = "Hello, World!";

        // Act
        var hash = ContentHasher.ComputeHash(content);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeHash_字符串和字节数组应该产生相同哈希()
    {
        // Arrange
        var stringContent = "测试内容";
        var byteContent = Encoding.UTF8.GetBytes(stringContent);

        // Act
        var hashFromString = ContentHasher.ComputeHash(stringContent);
        var hashFromBytes = ContentHasher.ComputeHash(byteContent.AsSpan());

        // Assert
        hashFromString.Should().Be(hashFromBytes);
    }

    [Fact]
    public void ComputeHash_空内容应该返回有效哈希()
    {
        // Arrange
        var emptyContent = Array.Empty<byte>();

        // Act
        var hash = ContentHasher.ComputeHash(emptyContent.AsSpan());

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
        // SHA256 of empty string is well-known
        hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public void ComputeSriHash_应该返回正确的SRI格式()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();

        // Act
        var sriHash = ContentHasher.ComputeSriHash(content.AsSpan());

        // Assert
        sriHash.Should().StartWith("sha256-");
        sriHash.Should().MatchRegex(@"^sha256-[A-Za-z0-9+/]+=*$");
    }

    [Fact]
    public void ComputeSriHash_相同内容应该产生相同SRI哈希()
    {
        // Arrange
        var content1 = "测试内容"u8.ToArray();
        var content2 = "测试内容"u8.ToArray();

        // Act
        var sri1 = ContentHasher.ComputeSriHash(content1.AsSpan());
        var sri2 = ContentHasher.ComputeSriHash(content2.AsSpan());

        // Assert
        sri1.Should().Be(sri2);
    }

    [Fact]
    public void ComputeShortHash_应该返回指定长度的哈希()
    {
        // Arrange
        var content = "Hello, World!"u8.ToArray();

        // Act
        var shortHash = ContentHasher.ComputeShortHash(content.AsSpan(), length: 8);

        // Assert
        shortHash.Should().HaveLength(8);
        shortHash.Should().MatchRegex("^[a-f0-9]{8}$");
    }

    [Fact]
    public void ComputeShortHash_默认长度应该是8()
    {
        // Arrange
        var content = "Test"u8.ToArray();

        // Act
        var shortHash = ContentHasher.ComputeShortHash(content.AsSpan());

        // Assert
        shortHash.Should().HaveLength(8);
    }

    [Fact]
    public void ComputeShortHash_长度超过64应该返回完整哈希()
    {
        // Arrange
        var content = "Test"u8.ToArray();

        // Act
        var shortHash = ContentHasher.ComputeShortHash(content.AsSpan(), length: 100);

        // Assert
        shortHash.Should().HaveLength(64);
    }

    [Fact]
    public void GenerateFingerprintedPath_应该生成正确的指纹路径()
    {
        // Arrange
        var originalPath = "assets/styles/main.css";
        var content = "body { color: red; }"u8.ToArray();

        // Act
        var fingerprintedPath = ContentHasher.GenerateFingerprintedPath(originalPath, content.AsSpan());

        // Assert
        fingerprintedPath.Should().MatchRegex(@"assets[\\/]styles[\\/]main\.[a-f0-9]{8}\.css$");
    }

    [Fact]
    public void GenerateFingerprintedPath_应该保留文件扩展名()
    {
        // Arrange
        var originalPath = "scripts/app.bundle.js";
        var content = "console.log('hello');"u8.ToArray();

        // Act
        var fingerprintedPath = ContentHasher.GenerateFingerprintedPath(originalPath, content.AsSpan());

        // Assert
        fingerprintedPath.Should().EndWith(".js");
    }

    [Fact]
    public void GenerateFingerprintedPath_相同内容应该产生相同指纹()
    {
        // Arrange
        var path = "style.css";
        var content = "body { margin: 0; }"u8.ToArray();

        // Act
        var path1 = ContentHasher.GenerateFingerprintedPath(path, content.AsSpan());
        var path2 = ContentHasher.GenerateFingerprintedPath(path, content.AsSpan());

        // Assert
        path1.Should().Be(path2);
    }

    [Fact]
    public void GenerateFingerprintedPath_不同内容应该产生不同指纹()
    {
        // Arrange
        var path = "style.css";
        var content1 = "body { margin: 0; }"u8.ToArray();
        var content2 = "body { padding: 0; }"u8.ToArray();

        // Act
        var path1 = ContentHasher.GenerateFingerprintedPath(path, content1.AsSpan());
        var path2 = ContentHasher.GenerateFingerprintedPath(path, content2.AsSpan());

        // Assert
        path1.Should().NotBe(path2);
    }

    [Fact]
    public void ComputeHash_大内容应该正确处理()
    {
        // Arrange
        var largeContent = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(largeContent);

        // Act
        var hash = ContentHasher.ComputeHash(largeContent.AsSpan());

        // Assert
        hash.Should().HaveLength(64);
    }

    [Fact]
    public void ComputeHash_CharSpan_应该正确处理()
    {
        // Arrange
        var content = "Hello, World!".AsSpan();

        // Act
        var hash = ContentHasher.ComputeHash(content);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().HaveLength(64);
    }
}
