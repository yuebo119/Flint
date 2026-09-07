// Flint 静态站点生成器
// 图像处理器实现 - 基于 ImageSharp 库

using System.Diagnostics;
using System.Security.Cryptography;
using Flint.Core.Abstractions;
using Flint.Core.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// 使用别名解决类型冲突
using CoreImageInfo = Flint.Core.Abstractions.ImageInfo;
using CoreResizeMode = Flint.Core.Models.ResizeMode;
using SharpImageInfo = SixLabors.ImageSharp.ImageInfo;
using SharpResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;

namespace Flint.Core.Assets;

/// <summary>
/// 图像处理器实现
/// 支持图像缩放、裁剪、旋转、格式转换和响应式图像生成
/// </summary>
public sealed class ImageProcessor : IImageProcessor
{
    /// <summary>
    /// 默认响应式图像宽度
    /// </summary>
    private static readonly int[] DefaultResponsiveWidths = [320, 640, 768, 1024, 1280, 1920];

    /// <summary>
    /// 处理图像
    /// </summary>
    /// <param name="image">图像资源</param>
    /// <param name="options">处理选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的资源</returns>
    public async ValueTask<ProcessedAsset> ProcessAsync(
        AssetFile image,
        ImageProcessingOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // 加载图像
        using var img = await LoadImageAsync(image.Content, cancellationToken);

        // 应用变换
        ApplyTransformations(img, options);

        // 编码输出
        var (content, mediaType, extension) = await EncodeImageAsync(
            img,
            options.OutputFormat,
            options.Quality,
            image.Extension,
            cancellationToken);

        stopwatch.Stop();

        // 计算哈希
        var contentHash = ComputeHash(content.Span);
        var integrity = ComputeSriHash(content.Span);

        // 生成输出路径
        var outputPath = GenerateOutputPath(image.SourcePath, extension);
        var fingerprintedPath = GenerateFingerprintedPath(outputPath, contentHash);

        return new ProcessedAsset
        {
            OutputPath = outputPath,
            MediaType = mediaType,
            Content = content,
            ContentHash = contentHash,
            Integrity = integrity,
            FingerprintedPath = fingerprintedPath,
            SourcePath = image.SourcePath,
            ProcessingTime = stopwatch.Elapsed,
            OriginalSize = image.Content.Length,
            IsMinified = false
        };
    }

    /// <summary>
    /// 生成响应式图像
    /// </summary>
    /// <param name="image">图像资源</param>
    /// <param name="widths">目标宽度列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>不同尺寸的图像列表</returns>
    public async ValueTask<IReadOnlyList<ProcessedAsset>> GenerateResponsiveAsync(
        AssetFile image,
        int[] widths,
        CancellationToken cancellationToken = default)
    {
        // 使用默认宽度（如果未指定）
        var targetWidths = widths.Length > 0 ? widths : DefaultResponsiveWidths;

        // 加载原始图像获取尺寸
        using var originalImage = await LoadImageAsync(image.Content, cancellationToken);
        var originalWidth = originalImage.Width;

        // 过滤掉非正值与大于原始宽度的目标宽度（0/负宽度使 ImageSharp 抛异常，整批响应式图失败）
        var validWidths = targetWidths.Where(w => w > 0 && w <= originalWidth).ToArray();

        // 如果没有有效宽度，至少保留原始尺寸
        if (validWidths.Length == 0)
        {
            validWidths = [originalWidth];
        }

        var results = new List<ProcessedAsset>(validWidths.Length);

        foreach (var width in validWidths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var options = new ImageProcessingOptions
            {
                Width = width,
                ResizeMode = CoreResizeMode.Fit,
                OutputFormat = ImageFormat.Original,
                Quality = 85
            };

            var processed = await ProcessAsync(image, options, cancellationToken);

            // 修改输出路径以包含宽度后缀
            var pathWithWidth = InsertWidthSuffix(processed.OutputPath, width);
            var fingerprintedWithWidth = processed.FingerprintedPath != null
                ? InsertWidthSuffix(processed.FingerprintedPath, width)
                : null;

            results.Add(new ProcessedAsset
            {
                OutputPath = pathWithWidth,
                MediaType = processed.MediaType,
                Content = processed.Content,
                ContentHash = processed.ContentHash,
                Integrity = processed.Integrity,
                FingerprintedPath = fingerprintedWithWidth,
                SourcePath = processed.SourcePath,
                ProcessingTime = processed.ProcessingTime,
                OriginalSize = processed.OriginalSize,
                IsMinified = processed.IsMinified
            });
        }

        return results;
    }

    /// <summary>
    /// 获取图像信息
    /// </summary>
    /// <param name="image">图像资源</param>
    /// <returns>图像信息</returns>
    public CoreImageInfo GetImageInfo(AssetFile image)
    {
        using var stream = new MemoryStream(image.Content.ToArray());
        var imageInfo = Image.Identify(stream);

        if (imageInfo == null)
        {
            throw new InvalidOperationException($"无法识别图像格式: {image.SourcePath}");
        }

        return new CoreImageInfo
        {
            Width = imageInfo.Width,
            Height = imageInfo.Height,
            Format = imageInfo.Metadata.DecodedImageFormat?.Name ?? "Unknown",
            HasAlpha = HasAlphaChannel(imageInfo),
            BitsPerPixel = imageInfo.PixelType.BitsPerPixel
        };
    }

    /// <summary>
    /// 加载图像
    /// </summary>
    private static async Task<Image<Rgba32>> LoadImageAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content.ToArray());
        return await Image.LoadAsync<Rgba32>(stream, cancellationToken);
    }

    /// <summary>
    /// 应用图像变换
    /// </summary>
    private static void ApplyTransformations(Image<Rgba32> image, ImageProcessingOptions options)
    {
        // 如果指定了尺寸，进行缩放
        if (options.Width.HasValue || options.Height.HasValue)
        {
            var resizeOptions = CreateResizeOptions(
                image.Width,
                image.Height,
                options.Width,
                options.Height,
                options.ResizeMode);

            image.Mutate(ctx => ctx.Resize(resizeOptions));
        }
    }

    /// <summary>
    /// 创建缩放选项
    /// </summary>
    private static ResizeOptions CreateResizeOptions(
        int originalWidth,
        int originalHeight,
        int? targetWidth,
        int? targetHeight,
        CoreResizeMode mode)
    {
        // 计算目标尺寸
        var (width, height) = CalculateTargetSize(
            originalWidth,
            originalHeight,
            targetWidth,
            targetHeight,
            mode);

        var resizeMode = mode switch
        {
            CoreResizeMode.Fit => SharpResizeMode.Max,
            CoreResizeMode.Fill => SharpResizeMode.Min,
            CoreResizeMode.Crop => SharpResizeMode.Crop,
            CoreResizeMode.Pad => SharpResizeMode.Pad,
            CoreResizeMode.Stretch => SharpResizeMode.Stretch,
            _ => SharpResizeMode.Max
        };

        return new ResizeOptions
        {
            Size = new Size(width, height),
            Mode = resizeMode,
            Sampler = KnownResamplers.Lanczos3, // 高质量重采样
            PadColor = Color.Transparent
        };
    }

    /// <summary>
    /// 计算目标尺寸
    /// </summary>
    private static (int Width, int Height) CalculateTargetSize(
        int originalWidth,
        int originalHeight,
        int? targetWidth,
        int? targetHeight,
        CoreResizeMode mode)
    {
        // 如果两个尺寸都指定了
        if (targetWidth.HasValue && targetHeight.HasValue)
        {
            return (targetWidth.Value, targetHeight.Value);
        }

        // 只指定宽度，按比例计算高度
        if (targetWidth.HasValue)
        {
            var ratio = (double)targetWidth.Value / originalWidth;
            return (targetWidth.Value, (int)Math.Round(originalHeight * ratio));
        }

        // 只指定高度，按比例计算宽度
        if (targetHeight.HasValue)
        {
            var ratio = (double)targetHeight.Value / originalHeight;
            return ((int)Math.Round(originalWidth * ratio), targetHeight.Value);
        }

        // 都没指定，保持原始尺寸
        return (originalWidth, originalHeight);
    }

    /// <summary>
    /// 编码图像
    /// </summary>
    private static async Task<(ReadOnlyMemory<byte> Content, string MediaType, string Extension)> EncodeImageAsync(
        Image<Rgba32> image,
        ImageFormat format,
        int quality,
        string originalExtension,
        CancellationToken cancellationToken)
    {
        using var outputStream = new MemoryStream();

        var (encoder, mediaType, extension) = GetEncoder(format, quality, originalExtension);
        await image.SaveAsync(outputStream, encoder, cancellationToken);

        return (new ReadOnlyMemory<byte>(outputStream.ToArray()), mediaType, extension);
    }

    /// <summary>
    /// 获取编码器
    /// </summary>
    private static (IImageEncoder Encoder, string MediaType, string Extension) GetEncoder(
        ImageFormat format,
        int quality,
        string originalExtension)
    {
        // 如果是 Original 格式，根据原始扩展名决定
        var effectiveFormat = format == ImageFormat.Original
            ? GetFormatFromExtension(originalExtension)
            : format;

        return effectiveFormat switch
        {
            ImageFormat.Jpeg => (
                new JpegEncoder { Quality = quality },
                "image/jpeg",
                ".jpg"),

            ImageFormat.Png => (
                new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression },
                "image/png",
                ".png"),

            ImageFormat.WebP => (
                new WebpEncoder { Quality = quality, FileFormat = WebpFileFormatType.Lossy },
                "image/webp",
                ".webp"),

            ImageFormat.Gif => (
                new GifEncoder(),
                "image/gif",
                ".gif"),

            // AVIF 目前 ImageSharp 不直接支持，回退到 WebP
            ImageFormat.Avif => (
                new WebpEncoder { Quality = quality, FileFormat = WebpFileFormatType.Lossy },
                "image/webp",
                ".webp"),

            _ => (
                new JpegEncoder { Quality = quality },
                "image/jpeg",
                ".jpg")
        };
    }

    /// <summary>
    /// 根据扩展名获取格式
    /// </summary>
    private static ImageFormat GetFormatFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".webp" => ImageFormat.WebP,
            ".gif" => ImageFormat.Gif,
            ".avif" => ImageFormat.Avif,
            _ => ImageFormat.Jpeg
        };
    }

    /// <summary>
    /// 检查是否有透明通道
    /// </summary>
    private static bool HasAlphaChannel(SharpImageInfo info)
    {
        // PNG 和 WebP 通常支持透明
        var format = info.Metadata.DecodedImageFormat?.Name?.ToUpperInvariant();
        return format is "PNG" or "WEBP" or "GIF";
    }

    /// <summary>
    /// 计算内容哈希（SHA256）
    /// </summary>
    private static string ComputeHash(ReadOnlySpan<byte> content)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(content, hashBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// 计算 SRI 哈希
    /// </summary>
    private static string ComputeSriHash(ReadOnlySpan<byte> content)
    {
        Span<byte> hashBytes = stackalloc byte[32];
        SHA256.HashData(content, hashBytes);
        return $"sha256-{Convert.ToBase64String(hashBytes)}";
    }

    /// <summary>
    /// 生成输出路径
    /// </summary>
    private static string GenerateOutputPath(string sourcePath, string newExtension)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(directory, fileName + newExtension);
    }

    /// <summary>
    /// 生成带指纹的路径
    /// </summary>
    private static string GenerateFingerprintedPath(string outputPath, string hash)
    {
        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(outputPath);
        var extension = Path.GetExtension(outputPath);
        // 使用哈希的前 8 位作为指纹
        var fingerprint = hash[..8];
        return Path.Combine(directory, $"{fileName}.{fingerprint}{extension}");
    }

    /// <summary>
    /// 在文件名中插入宽度后缀
    /// </summary>
    private static string InsertWidthSuffix(string path, int width)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory, $"{fileName}-{width}w{extension}");
    }
}
