// Flint 静态站点生成器
// 图像处理器单元测试

using Flint.Core.Assets;
using Flint.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

// 使用别名解决类型冲突
using CoreResizeMode = Flint.Core.Models.ResizeMode;

namespace Flint.Core.Tests.Assets;

/// <summary>
/// 图像处理器单元测试
/// </summary>
public class ImageProcessorTests
{
    private readonly ImageProcessor _processor;

    public ImageProcessorTests()
    {
        _processor = new ImageProcessor();
    }

    #region 辅助方法

    /// <summary>
    /// 创建测试用的 JPEG 图像
    /// </summary>
    private static AssetFile CreateTestJpegImage(int width, int height, string path = "test.jpg")
    {
        using var image = new Image<Rgba32>(width, height);
        // 填充一些颜色以便测试
        image.Mutate(ctx => ctx.BackgroundColor(Color.Blue));

        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder { Quality = 90 });

        return new AssetFile
        {
            SourcePath = path,
            MediaType = "image/jpeg",
            Content = stream.ToArray(),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 创建测试用的 PNG 图像
    /// </summary>
    private static AssetFile CreateTestPngImage(int width, int height, string path = "test.png")
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(ctx => ctx.BackgroundColor(Color.Red));

        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());

        return new AssetFile
        {
            SourcePath = path,
            MediaType = "image/png",
            Content = stream.ToArray(),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    #endregion

    #region GetImageInfo 测试

    [Fact]
    public void GetImageInfo_应该返回正确的图像尺寸()
    {
        // Arrange
        var image = CreateTestJpegImage(800, 600);

        // Act
        var info = _processor.GetImageInfo(image);

        // Assert
        Assert.Equal(800, info.Width);
        Assert.Equal(600, info.Height);
    }

    [Fact]
    public void GetImageInfo_应该返回正确的格式()
    {
        // Arrange
        var jpegImage = CreateTestJpegImage(100, 100);
        var pngImage = CreateTestPngImage(100, 100);

        // Act
        var jpegInfo = _processor.GetImageInfo(jpegImage);
        var pngInfo = _processor.GetImageInfo(pngImage);

        // Assert
        Assert.Equal("JPEG", jpegInfo.Format, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("PNG", pngInfo.Format, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetImageInfo_应该计算正确的宽高比()
    {
        // Arrange
        var image = CreateTestJpegImage(1920, 1080);

        // Act
        var info = _processor.GetImageInfo(image);

        // Assert
        Assert.Equal(16.0 / 9.0, info.AspectRatio, 2);
    }

    [Fact]
    public void GetImageInfo_PNG图像应该标记有透明通道()
    {
        // Arrange
        var pngImage = CreateTestPngImage(100, 100);

        // Act
        var info = _processor.GetImageInfo(pngImage);

        // Assert
        Assert.True(info.HasAlpha);
    }

    #endregion

    #region ProcessAsync 测试

    [Fact]
    public async Task ProcessAsync_不指定尺寸应该保持原始大小()
    {
        // Arrange
        var image = CreateTestJpegImage(800, 600);
        var options = ImageProcessingOptions.Default;

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.ContentHash);
        Assert.NotEmpty(result.Integrity!);
    }

    [Fact]
    public async Task ProcessAsync_指定宽度应该按比例缩放()
    {
        // Arrange
        var image = CreateTestJpegImage(800, 600);
        var options = new ImageProcessingOptions
        {
            Width = 400,
            ResizeMode = CoreResizeMode.Fit,
            Quality = 85
        };

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        // 验证输出图像尺寸
        using var outputStream = new MemoryStream(result.Content.ToArray());
        using var outputImage = await Image.LoadAsync(outputStream);
        Assert.Equal(400, outputImage.Width);
        Assert.Equal(300, outputImage.Height); // 保持 4:3 比例
    }

    [Fact]
    public async Task ProcessAsync_指定高度应该按比例缩放()
    {
        // Arrange
        var image = CreateTestJpegImage(800, 600);
        var options = new ImageProcessingOptions
        {
            Height = 300,
            ResizeMode = CoreResizeMode.Fit,
            Quality = 85
        };

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        using var outputStream = new MemoryStream(result.Content.ToArray());
        using var outputImage = await Image.LoadAsync(outputStream);
        Assert.Equal(400, outputImage.Width);
        Assert.Equal(300, outputImage.Height);
    }

    [Fact]
    public async Task ProcessAsync_转换为WebP格式()
    {
        // Arrange
        var image = CreateTestJpegImage(400, 300);
        var options = new ImageProcessingOptions
        {
            OutputFormat = ImageFormat.WebP,
            Quality = 80
        };

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.Equal("image/webp", result.MediaType);
        Assert.EndsWith(".webp", result.OutputPath);
    }

    [Fact]
    public async Task ProcessAsync_转换为PNG格式()
    {
        // Arrange
        var image = CreateTestJpegImage(400, 300);
        var options = new ImageProcessingOptions
        {
            OutputFormat = ImageFormat.Png,
            Quality = 85
        };

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.Equal("image/png", result.MediaType);
        Assert.EndsWith(".png", result.OutputPath);
    }

    [Fact]
    public async Task ProcessAsync_应该生成内容哈希()
    {
        // Arrange
        var image = CreateTestJpegImage(200, 200);
        var options = ImageProcessingOptions.Default;

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.NotNull(result.ContentHash);
        Assert.Equal(64, result.ContentHash.Length); // SHA256 = 64 hex chars
    }

    [Fact]
    public async Task ProcessAsync_应该生成SRI哈希()
    {
        // Arrange
        var image = CreateTestJpegImage(200, 200);
        var options = ImageProcessingOptions.Default;

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.NotNull(result.Integrity);
        Assert.StartsWith("sha256-", result.Integrity);
    }

    [Fact]
    public async Task ProcessAsync_应该生成带指纹的路径()
    {
        // Arrange
        var image = CreateTestJpegImage(200, 200, "images/photo.jpg");
        var options = ImageProcessingOptions.Default;

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.NotNull(result.FingerprintedPath);
        Assert.Contains("photo.", result.FingerprintedPath);
    }

    [Fact]
    public async Task ProcessAsync_相同输入应该产生相同哈希()
    {
        // Arrange
        var image = CreateTestJpegImage(200, 200);
        var options = new ImageProcessingOptions
        {
            Width = 100,
            Quality = 85
        };

        // Act
        var result1 = await _processor.ProcessAsync(image, options);
        var result2 = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.Equal(result1.ContentHash, result2.ContentHash);
    }

    [Fact]
    public async Task ProcessAsync_支持取消操作()
    {
        // Arrange
        var image = CreateTestJpegImage(1000, 1000);
        var options = ImageProcessingOptions.Default;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // TaskCanceledException 继承自 OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _processor.ProcessAsync(image, options, cts.Token).AsTask());
    }

    #endregion

    #region GenerateResponsiveAsync 测试

    [Fact]
    public async Task GenerateResponsiveAsync_应该生成多个尺寸()
    {
        // Arrange
        var image = CreateTestJpegImage(1920, 1080);
        var widths = new[] { 320, 640, 1280 };

        // Act
        var results = await _processor.GenerateResponsiveAsync(image, widths);

        // Assert
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public async Task GenerateResponsiveAsync_应该过滤大于原始宽度的尺寸()
    {
        // Arrange
        var image = CreateTestJpegImage(500, 400);
        var widths = new[] { 320, 640, 1280, 1920 };

        // Act
        var results = await _processor.GenerateResponsiveAsync(image, widths);

        // Assert
        Assert.Single(results); // 只有 320 小于 500
    }

    [Fact]
    public async Task GenerateResponsiveAsync_空宽度数组应该使用默认值()
    {
        // Arrange
        var image = CreateTestJpegImage(2000, 1500);
        var widths = Array.Empty<int>();

        // Act
        var results = await _processor.GenerateResponsiveAsync(image, widths);

        // Assert
        Assert.True(results.Count > 0);
    }

    [Fact]
    public async Task GenerateResponsiveAsync_输出路径应该包含宽度后缀()
    {
        // Arrange
        var image = CreateTestJpegImage(1000, 800, "photo.jpg");
        var widths = new[] { 400 };

        // Act
        var results = await _processor.GenerateResponsiveAsync(image, widths);

        // Assert
        Assert.Single(results);
        Assert.Contains("-400w", results[0].OutputPath);
    }

    #endregion

    #region 缩放模式测试

    [Fact]
    public async Task ProcessAsync_Fit模式应该保持宽高比()
    {
        // Arrange
        var image = CreateTestJpegImage(800, 600);
        var options = new ImageProcessingOptions
        {
            Width = 400,
            Height = 400,
            ResizeMode = CoreResizeMode.Fit,
            Quality = 85
        };

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        using var outputStream = new MemoryStream(result.Content.ToArray());
        using var outputImage = await Image.LoadAsync(outputStream);
        // Fit 模式下，图像应该完全在目标尺寸内
        Assert.True(outputImage.Width <= 400);
        Assert.True(outputImage.Height <= 400);
    }

    [Fact]
    public async Task ProcessAsync_Stretch模式应该拉伸到目标尺寸()
    {
        // Arrange
        var image = CreateTestJpegImage(800, 600);
        var options = new ImageProcessingOptions
        {
            Width = 400,
            Height = 400,
            ResizeMode = CoreResizeMode.Stretch,
            Quality = 85
        };

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        using var outputStream = new MemoryStream(result.Content.ToArray());
        using var outputImage = await Image.LoadAsync(outputStream);
        Assert.Equal(400, outputImage.Width);
        Assert.Equal(400, outputImage.Height);
    }

    #endregion

    #region 边界情况测试

    [Fact]
    public async Task ProcessAsync_非常小的图像应该正常处理()
    {
        // Arrange
        var image = CreateTestJpegImage(10, 10);
        var options = new ImageProcessingOptions
        {
            Width = 5,
            Quality = 85
        };

        // Act
        var result = await _processor.ProcessAsync(image, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Content.Length > 0);
    }

    [Fact]
    public async Task ProcessAsync_质量参数应该影响输出大小()
    {
        // Arrange
        var image = CreateTestJpegImage(500, 500);
        var highQualityOptions = new ImageProcessingOptions
        {
            OutputFormat = ImageFormat.Jpeg,
            Quality = 95
        };
        var lowQualityOptions = new ImageProcessingOptions
        {
            OutputFormat = ImageFormat.Jpeg,
            Quality = 30
        };

        // Act
        var highQualityResult = await _processor.ProcessAsync(image, highQualityOptions);
        var lowQualityResult = await _processor.ProcessAsync(image, lowQualityOptions);

        // Assert
        Assert.True(highQualityResult.Content.Length > lowQualityResult.Content.Length);
    }

    #endregion
}
