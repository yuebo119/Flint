// Flint 核心库测试
// AssetFile 数据类型单元测试

using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Models;

/// <summary>
/// AssetFile 记录结构的单元测试
/// </summary>
public class AssetFileTests
{
    [Fact]
    public void AssetFile_应该正确存储基本属性()
    {
        // Arrange
        var path = "assets/images/logo.png";
        var mediaType = "image/png";
        var content = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG 魔数
        var modifiedTime = DateTimeOffset.Now;

        // Act
        var asset = new AssetFile
        {
            SourcePath = path,
            MediaType = mediaType,
            Content = content,
            ModifiedTime = modifiedTime
        };

        // Assert
        asset.SourcePath.Should().Be(path);
        asset.MediaType.Should().Be(mediaType);
        asset.Content.ToArray().Should().BeEquivalentTo(content);
        asset.ModifiedTime.Should().Be(modifiedTime);
    }

    [Fact]
    public void AssetFile_应该正确计算内容长度()
    {
        // Arrange
        var content = new byte[1024];
        var asset = new AssetFile
        {
            SourcePath = "test.png",
            MediaType = "image/png",
            Content = content,
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Length.Should().Be(1024);
    }

    [Fact]
    public void AssetFile_应该正确获取文件扩展名()
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = "assets/styles/main.CSS",
            MediaType = "text/css",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Extension.Should().Be(".css");
    }

    [Fact]
    public void AssetFile_应该正确获取文件名()
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = "assets/scripts/app.bundle.js",
            MediaType = "application/javascript",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.FileName.Should().Be("app.bundle.js");
    }

    [Theory]
    [InlineData(".jpg", AssetType.Image)]
    [InlineData(".jpeg", AssetType.Image)]
    [InlineData(".png", AssetType.Image)]
    [InlineData(".gif", AssetType.Image)]
    [InlineData(".webp", AssetType.Image)]
    [InlineData(".avif", AssetType.Image)]
    [InlineData(".svg", AssetType.Image)]
    [InlineData(".ico", AssetType.Image)]
    public void AssetFile_应该正确识别图片类型(string extension, AssetType expectedType)
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = $"test{extension}",
            MediaType = "image/unknown",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(".css", AssetType.Style)]
    [InlineData(".scss", AssetType.Style)]
    [InlineData(".sass", AssetType.Style)]
    [InlineData(".less", AssetType.Style)]
    public void AssetFile_应该正确识别样式类型(string extension, AssetType expectedType)
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = $"test{extension}",
            MediaType = "text/css",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(".js", AssetType.Script)]
    [InlineData(".mjs", AssetType.Script)]
    [InlineData(".ts", AssetType.Script)]
    [InlineData(".tsx", AssetType.Script)]
    [InlineData(".jsx", AssetType.Script)]
    public void AssetFile_应该正确识别脚本类型(string extension, AssetType expectedType)
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = $"test{extension}",
            MediaType = "application/javascript",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(".woff", AssetType.Font)]
    [InlineData(".woff2", AssetType.Font)]
    [InlineData(".ttf", AssetType.Font)]
    [InlineData(".otf", AssetType.Font)]
    [InlineData(".eot", AssetType.Font)]
    public void AssetFile_应该正确识别字体类型(string extension, AssetType expectedType)
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = $"test{extension}",
            MediaType = "font/unknown",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(".mp4", AssetType.Video)]
    [InlineData(".webm", AssetType.Video)]
    [InlineData(".ogg", AssetType.Video)]
    [InlineData(".mov", AssetType.Video)]
    public void AssetFile_应该正确识别视频类型(string extension, AssetType expectedType)
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = $"test{extension}",
            MediaType = "video/unknown",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(".mp3", AssetType.Audio)]
    [InlineData(".wav", AssetType.Audio)]
    [InlineData(".flac", AssetType.Audio)]
    [InlineData(".aac", AssetType.Audio)]
    public void AssetFile_应该正确识别音频类型(string extension, AssetType expectedType)
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = $"test{extension}",
            MediaType = "audio/unknown",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(".json", AssetType.Data)]
    [InlineData(".xml", AssetType.Data)]
    [InlineData(".yaml", AssetType.Data)]
    [InlineData(".yml", AssetType.Data)]
    [InlineData(".toml", AssetType.Data)]
    public void AssetFile_应该正确识别数据类型(string extension, AssetType expectedType)
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = $"test{extension}",
            MediaType = "application/json",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(expectedType);
    }

    [Fact]
    public void AssetFile_未知扩展名应该返回Other类型()
    {
        // Arrange
        var asset = new AssetFile
        {
            SourcePath = "test.unknown",
            MediaType = "application/octet-stream",
            Content = Array.Empty<byte>(),
            ModifiedTime = DateTimeOffset.Now
        };

        // Act & Assert
        asset.Type.Should().Be(AssetType.Other);
    }

    [Fact]
    public void AssetFile_记录类型应该支持相等性比较()
    {
        // Arrange
        var content = new byte[] { 1, 2, 3 };
        var time = DateTimeOffset.Now;

        var asset1 = new AssetFile
        {
            SourcePath = "test.png",
            MediaType = "image/png",
            Content = content,
            ModifiedTime = time
        };

        var asset2 = new AssetFile
        {
            SourcePath = "test.png",
            MediaType = "image/png",
            Content = content,
            ModifiedTime = time
        };

        // Act & Assert
        asset1.Should().Be(asset2);
    }
}
