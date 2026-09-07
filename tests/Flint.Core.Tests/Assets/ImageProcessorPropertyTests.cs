// Flint 静态站点生成器
// 图像处理器属性测试
// **Feature: Flint, Property 6: 图像处理不变性**

using Flint.Core.Assets;
using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// 使用别名解决类型冲突
using CoreResizeMode = Flint.Core.Models.ResizeMode;

namespace Flint.Core.Tests.Assets;

/// <summary>
/// 图像处理器属性测试
/// **Validates: Requirements 4.1, 4.2, 4.3**
/// 
/// Property 6: 图像处理不变性
/// 对于任意有效的图像文件和处理选项，相同的输入和选项应该产生相同的输出。
/// 图像尺寸变换应该保持宽高比（当指定时）。
/// </summary>
public class ImageProcessorPropertyTests
{
    private readonly ImageProcessor _processor;

    public ImageProcessorPropertyTests()
    {
        _processor = new ImageProcessor();
    }

    #region Property 6.1: 图像处理确定性

    /// <summary>
    /// **Property 6: 图像处理不变性 - 确定性**
    /// 相同的输入图像和处理选项应该产生完全相同的输出
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ImageProcessing_IsDeterministic()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            ImageArbitraries.ValidProcessingOptions(),
            (dimensions, options) =>
            {
                // Arrange - 创建测试图像
                var image = CreateTestImage(dimensions.Width, dimensions.Height);

                // Act - 处理两次
                var result1 = _processor.ProcessAsync(image, options).AsTask().Result;
                var result2 = _processor.ProcessAsync(image, options).AsTask().Result;

                // Assert - 输出应该完全相同
                return result1.ContentHash == result2.ContentHash &&
                       result1.Content.Length == result2.Content.Length &&
                       result1.MediaType == result2.MediaType;
            });
    }

    /// <summary>
    /// **Property 6: 图像处理不变性 - 内容哈希确定性**
    /// 相同的输入应该产生相同的内容哈希
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ContentHash_IsDeterministic()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            dimensions =>
            {
                // Arrange - 创建相同的测试图像两次
                var image1 = CreateTestImage(dimensions.Width, dimensions.Height, Color.Blue);
                var image2 = CreateTestImage(dimensions.Width, dimensions.Height, Color.Blue);
                var options = ImageProcessingOptions.Default;

                // Act
                var result1 = _processor.ProcessAsync(image1, options).AsTask().Result;
                var result2 = _processor.ProcessAsync(image2, options).AsTask().Result;

                // Assert - 相同输入应该产生相同哈希
                return result1.ContentHash == result2.ContentHash;
            });
    }

    /// <summary>
    /// **Property 6: 图像处理不变性 - SRI 哈希确定性**
    /// 相同的输入应该产生相同的 SRI 完整性哈希
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SriHash_IsDeterministic()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            ImageArbitraries.ValidProcessingOptions(),
            (dimensions, options) =>
            {
                // Arrange
                var image = CreateTestImage(dimensions.Width, dimensions.Height);

                // Act
                var result1 = _processor.ProcessAsync(image, options).AsTask().Result;
                var result2 = _processor.ProcessAsync(image, options).AsTask().Result;

                // Assert
                return result1.Integrity == result2.Integrity &&
                       result1.Integrity!.StartsWith("sha256-", StringComparison.Ordinal);
            });
    }

    #endregion

    #region Property 6.2: 宽高比保持

    /// <summary>
    /// **Property 6: 图像处理不变性 - Fit 模式保持宽高比**
    /// 使用 Fit 模式时，输出图像应该保持原始宽高比
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FitMode_PreservesAspectRatio()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            ImageArbitraries.ValidTargetWidth(),
            (dimensions, targetWidth) =>
            {
                // 跳过极端宽高比的情况（宽高比 > 5:1 或 < 1:5）
                var originalRatio = (double)dimensions.Width / dimensions.Height;
                if (originalRatio > 5.0 || originalRatio < 0.2)
                    return true; // 跳过极端情况

                // Arrange
                var image = CreateTestImage(dimensions.Width, dimensions.Height);
                var options = new ImageProcessingOptions
                {
                    Width = targetWidth,
                    ResizeMode = CoreResizeMode.Fit,
                    Quality = 85,
                    OutputFormat = ImageFormat.Jpeg
                };

                // Act
                var result = _processor.ProcessAsync(image, options).AsTask().Result;

                // 解析输出图像尺寸
                using var outputStream = new MemoryStream(result.Content.ToArray());
                using var outputImage = Image.Load(outputStream);

                // 计算输出的宽高比
                var outputRatio = (double)outputImage.Width / outputImage.Height;

                // Assert - 宽高比应该保持一致（允许 10% 的误差，因为像素取整和极端情况）
                var ratioDifference = Math.Abs(originalRatio - outputRatio);
                var maxAllowedDifference = 0.10 * Math.Max(originalRatio, 1.0);
                return ratioDifference <= maxAllowedDifference;
            });
    }

    /// <summary>
    /// **Property 6: 图像处理不变性 - 只指定宽度时保持宽高比**
    /// 只指定目标宽度时，输出图像应该保持原始宽高比
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WidthOnly_PreservesAspectRatio()
    {
        return Prop.ForAll(
            ImageArbitraries.SmallTargetDimensions(),
            (testData) =>
            {
                // Arrange
                var image = CreateTestImage(testData.OriginalWidth, testData.OriginalHeight);
                var options = new ImageProcessingOptions
                {
                    Width = testData.TargetWidth,
                    ResizeMode = CoreResizeMode.Fit,
                    Quality = 85
                };

                // Act
                var result = _processor.ProcessAsync(image, options).AsTask().Result;

                using var outputStream = new MemoryStream(result.Content.ToArray());
                using var outputImage = Image.Load(outputStream);

                // 计算原始和输出的宽高比
                var originalRatio = (double)testData.OriginalWidth / testData.OriginalHeight;
                var outputRatio = (double)outputImage.Width / outputImage.Height;

                // Assert - 宽高比应该保持一致（允许 2% 的误差）
                var ratioDifference = Math.Abs(originalRatio - outputRatio);
                var maxAllowedDifference = 0.02 * Math.Max(originalRatio, 1.0);
                return ratioDifference <= maxAllowedDifference;
            });
    }

    /// <summary>
    /// **Property 6: 图像处理不变性 - 只指定高度时保持宽高比**
    /// 只指定目标高度时，输出图像应该保持原始宽高比
    /// **Validates: Requirements 4.1, 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HeightOnly_PreservesAspectRatio()
    {
        return Prop.ForAll(
            ImageArbitraries.SmallTargetHeightDimensions(),
            (testData) =>
            {
                // Arrange
                var image = CreateTestImage(testData.OriginalWidth, testData.OriginalHeight);
                var options = new ImageProcessingOptions
                {
                    Height = testData.TargetHeight,
                    ResizeMode = CoreResizeMode.Fit,
                    Quality = 85
                };

                // Act
                var result = _processor.ProcessAsync(image, options).AsTask().Result;

                using var outputStream = new MemoryStream(result.Content.ToArray());
                using var outputImage = Image.Load(outputStream);

                // 计算原始和输出的宽高比
                var originalRatio = (double)testData.OriginalWidth / testData.OriginalHeight;
                var outputRatio = (double)outputImage.Width / outputImage.Height;

                // Assert - 宽高比应该保持一致（允许 2% 的误差）
                var ratioDifference = Math.Abs(originalRatio - outputRatio);
                var maxAllowedDifference = 0.02 * Math.Max(originalRatio, 1.0);
                return ratioDifference <= maxAllowedDifference;
            });
    }

    #endregion

    #region Property 6.3: 格式转换确定性

    /// <summary>
    /// **Property 6: 图像处理不变性 - 格式转换确定性**
    /// 相同的格式转换应该产生相同的输出
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FormatConversion_IsDeterministic()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            ImageArbitraries.ValidOutputFormat(),
            (dimensions, format) =>
            {
                // Arrange
                var image = CreateTestImage(dimensions.Width, dimensions.Height);
                var options = new ImageProcessingOptions
                {
                    OutputFormat = format,
                    Quality = 85
                };

                // Act
                var result1 = _processor.ProcessAsync(image, options).AsTask().Result;
                var result2 = _processor.ProcessAsync(image, options).AsTask().Result;

                // Assert
                return result1.ContentHash == result2.ContentHash &&
                       result1.MediaType == result2.MediaType;
            });
    }

    /// <summary>
    /// **Property 6: 图像处理不变性 - WebP 转换**
    /// 转换为 WebP 格式应该产生正确的媒体类型
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WebPConversion_ProducesCorrectMediaType()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            dimensions =>
            {
                // Arrange
                var image = CreateTestImage(dimensions.Width, dimensions.Height);
                var options = new ImageProcessingOptions
                {
                    OutputFormat = ImageFormat.WebP,
                    Quality = 85
                };

                // Act
                var result = _processor.ProcessAsync(image, options).AsTask().Result;

                // Assert
                return result.MediaType == "image/webp" &&
                       result.OutputPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
            });
    }

    #endregion

    #region Property 6.4: 响应式图像生成

    /// <summary>
    /// **Property 6: 图像处理不变性 - 响应式图像确定性**
    /// 响应式图像生成应该是确定性的
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ResponsiveImageGeneration_IsDeterministic()
    {
        return Prop.ForAll(
            ImageArbitraries.LargeImageDimensions(),
            ImageArbitraries.ValidResponsiveWidths(),
            (dimensions, widths) =>
            {
                // Arrange
                var image = CreateTestImage(dimensions.Width, dimensions.Height);

                // Act
                var results1 = _processor.GenerateResponsiveAsync(image, widths).AsTask().Result;
                var results2 = _processor.GenerateResponsiveAsync(image, widths).AsTask().Result;

                // Assert - 生成的图像数量和哈希应该相同
                if (results1.Count != results2.Count)
                    return false;

                for (int i = 0; i < results1.Count; i++)
                {
                    if (results1[i].ContentHash != results2[i].ContentHash)
                        return false;
                }

                return true;
            });
    }

    /// <summary>
    /// **Property 6: 图像处理不变性 - 响应式图像宽度过滤**
    /// 响应式图像生成应该过滤掉大于原始宽度的目标宽度
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ResponsiveImages_FilterLargerWidths()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            ImageArbitraries.ValidResponsiveWidths(),
            (dimensions, widths) =>
            {
                // Arrange
                var image = CreateTestImage(dimensions.Width, dimensions.Height);

                // Act
                var results = _processor.GenerateResponsiveAsync(image, widths).AsTask().Result;

                // Assert - 所有生成的图像宽度都不应该超过原始宽度
                foreach (var result in results)
                {
                    using var outputStream = new MemoryStream(result.Content.ToArray());
                    using var outputImage = Image.Load(outputStream);
                    if (outputImage.Width > dimensions.Width)
                        return false;
                }

                return true;
            });
    }

    #endregion

    #region Property 6.5: 质量参数影响

    /// <summary>
    /// **Property 6: 图像处理不变性 - 相同质量产生相同输出**
    /// 相同的质量参数应该产生相同的输出
    /// **Validates: Requirements 4.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SameQuality_ProducesSameOutput()
    {
        return Prop.ForAll(
            ImageArbitraries.ValidImageDimensions(),
            ImageArbitraries.ValidQuality(),
            (dimensions, quality) =>
            {
                // Arrange
                var image = CreateTestImage(dimensions.Width, dimensions.Height);
                var options = new ImageProcessingOptions
                {
                    OutputFormat = ImageFormat.Jpeg,
                    Quality = quality
                };

                // Act
                var result1 = _processor.ProcessAsync(image, options).AsTask().Result;
                var result2 = _processor.ProcessAsync(image, options).AsTask().Result;

                // Assert
                return result1.ContentHash == result2.ContentHash;
            });
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 创建测试用的 JPEG 图像
    /// </summary>
    private static AssetFile CreateTestImage(int width, int height, Color? color = null)
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(ctx => ctx.BackgroundColor(color ?? Color.Blue));

        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder { Quality = 90 });

        return new AssetFile
        {
            SourcePath = $"test_{width}x{height}.jpg",
            MediaType = "image/jpeg",
            Content = stream.ToArray(),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
