// Flint 静态站点生成器
// 图像处理属性测试数据生成器
// **Feature: Flint, Property 6: 图像处理不变性**

using Flint.Core.Models;
using FsCheck;
using FsCheck.Fluent;

// 使用别名解决类型冲突
using CoreResizeMode = Flint.Core.Models.ResizeMode;

namespace Flint.Core.Tests.Assets;

/// <summary>
/// 图像测试数据生成器
/// 为属性测试提供有效的图像尺寸、处理选项等测试数据
/// </summary>
public static class ImageArbitraries
{
    /// <summary>
    /// 生成有效的图像尺寸
    /// 范围：50-500 像素，避免过大的图像导致测试过慢
    /// </summary>
    public static Arbitrary<ImageDimensions> ValidImageDimensions()
    {
        var gen = from width in Gen.Choose(50, 500)
                  from height in Gen.Choose(50, 500)
                  select new ImageDimensions(width, height);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成较大的图像尺寸（用于响应式图像测试）
    /// 范围：800-2000 像素
    /// </summary>
    public static Arbitrary<ImageDimensions> LargeImageDimensions()
    {
        var gen = from width in Gen.Choose(800, 2000)
                  from height in Gen.Choose(600, 1500)
                  select new ImageDimensions(width, height);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的目标宽度
    /// 范围：50-400 像素
    /// </summary>
    public static Arbitrary<int> ValidTargetWidth()
    {
        return Gen.Choose(50, 400).ToArbitrary();
    }

    /// <summary>
    /// 生成有效的目标高度
    /// 范围：50-400 像素
    /// </summary>
    public static Arbitrary<int> ValidTargetHeight()
    {
        return Gen.Choose(50, 400).ToArbitrary();
    }

    /// <summary>
    /// 生成确保目标宽度小于原始宽度的测试数据
    /// </summary>
    public static Arbitrary<WidthResizeTestData> SmallTargetDimensions()
    {
        var gen = from originalWidth in Gen.Choose(200, 500)
                  from originalHeight in Gen.Choose(200, 500)
                  from targetWidth in Gen.Choose(50, 199)
                  select new WidthResizeTestData(originalWidth, originalHeight, targetWidth);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成确保目标高度小于原始高度的测试数据
    /// </summary>
    public static Arbitrary<HeightResizeTestData> SmallTargetHeightDimensions()
    {
        var gen = from originalWidth in Gen.Choose(200, 500)
                  from originalHeight in Gen.Choose(200, 500)
                  from targetHeight in Gen.Choose(50, 199)
                  select new HeightResizeTestData(originalWidth, originalHeight, targetHeight);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的图像处理选项
    /// </summary>
    public static Arbitrary<ImageProcessingOptions> ValidProcessingOptions()
    {
        var gen = from width in Gen.OneOf(
                      Gen.Constant<int?>(null),
                      Gen.Choose(50, 400).Select(w => (int?)w))
                  from height in Gen.OneOf(
                      Gen.Constant<int?>(null),
                      Gen.Choose(50, 400).Select(h => (int?)h))
                  from resizeMode in Gen.Elements(
                      CoreResizeMode.Fit,
                      CoreResizeMode.Fill,
                      CoreResizeMode.Crop,
                      CoreResizeMode.Pad)
                  from quality in Gen.Choose(50, 95)
                  from format in Gen.Elements(
                      ImageFormat.Original,
                      ImageFormat.Jpeg,
                      ImageFormat.Png,
                      ImageFormat.WebP)
                  select new ImageProcessingOptions
                  {
                      Width = width,
                      Height = height,
                      ResizeMode = resizeMode,
                      Quality = quality,
                      OutputFormat = format
                  };

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的输出格式
    /// </summary>
    public static Arbitrary<ImageFormat> ValidOutputFormat()
    {
        return Gen.Elements(
            ImageFormat.Original,
            ImageFormat.Jpeg,
            ImageFormat.Png,
            ImageFormat.WebP
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成有效的质量参数
    /// 范围：30-95
    /// </summary>
    public static Arbitrary<int> ValidQuality()
    {
        return Gen.Choose(30, 95).ToArbitrary();
    }

    /// <summary>
    /// 生成有效的响应式图像宽度数组
    /// </summary>
    public static Arbitrary<int[]> ValidResponsiveWidths()
    {
        // 生成 2-5 个不同的宽度值
        var gen = from count in Gen.Choose(2, 5)
                  from widths in Gen.ArrayOf<int>(Gen.Choose(200, 1600), count)
                  select widths.Distinct().OrderBy(w => w).ToArray();

        return gen.ToArbitrary();
    }

    /// <summary>
    /// 生成有效的缩放模式
    /// </summary>
    public static Arbitrary<CoreResizeMode> ValidResizeMode()
    {
        return Gen.Elements(
            CoreResizeMode.Fit,
            CoreResizeMode.Fill,
            CoreResizeMode.Crop,
            CoreResizeMode.Pad,
            CoreResizeMode.Stretch
        ).ToArbitrary();
    }
}

/// <summary>
/// 图像尺寸记录
/// </summary>
/// <param name="Width">宽度（像素）</param>
/// <param name="Height">高度（像素）</param>
public readonly record struct ImageDimensions(int Width, int Height)
{
    /// <summary>
    /// 计算宽高比
    /// </summary>
    public double AspectRatio => (double)Width / Height;

    /// <summary>
    /// 是否为横向图像
    /// </summary>
    public bool IsLandscape => Width > Height;

    /// <summary>
    /// 是否为纵向图像
    /// </summary>
    public bool IsPortrait => Height > Width;

    /// <summary>
    /// 是否为正方形图像
    /// </summary>
    public bool IsSquare => Width == Height;

    public override string ToString() => $"{Width}x{Height}";
}


/// <summary>
/// 宽度缩放测试数据
/// </summary>
/// <param name="OriginalWidth">原始宽度</param>
/// <param name="OriginalHeight">原始高度</param>
/// <param name="TargetWidth">目标宽度</param>
public readonly record struct WidthResizeTestData(int OriginalWidth, int OriginalHeight, int TargetWidth)
{
    public override string ToString() => $"{OriginalWidth}x{OriginalHeight} -> width={TargetWidth}";
}

/// <summary>
/// 高度缩放测试数据
/// </summary>
/// <param name="OriginalWidth">原始宽度</param>
/// <param name="OriginalHeight">原始高度</param>
/// <param name="TargetHeight">目标高度</param>
public readonly record struct HeightResizeTestData(int OriginalWidth, int OriginalHeight, int TargetHeight)
{
    public override string ToString() => $"{OriginalWidth}x{OriginalHeight} -> height={TargetHeight}";
}
