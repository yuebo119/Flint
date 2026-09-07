// Flint 静态站点生成器
// 处理后的资源类型

namespace Flint.Core.Models;

/// <summary>
/// 处理后的资源
/// </summary>
public sealed record ProcessedAsset
{
    /// <summary>
    /// 输出文件路径
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// 媒体类型（MIME 类型）
    /// </summary>
    public required string MediaType { get; init; }

    /// <summary>
    /// 处理后的内容
    /// </summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>
    /// 内容哈希（SHA256）
    /// </summary>
    public required string ContentHash { get; init; }

    /// <summary>
    /// SRI 完整性哈希（可选）
    /// </summary>
    public string? Integrity { get; init; }

    /// <summary>
    /// 带指纹的文件名
    /// </summary>
    public string? FingerprintedPath { get; init; }

    /// <summary>
    /// 原始资源路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 处理耗时
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// 内容长度（字节数）
    /// </summary>
    public int Length => Content.Length;

    /// <summary>
    /// 是否已压缩
    /// </summary>
    public bool IsMinified { get; init; }

    /// <summary>
    /// 原始大小（压缩前）
    /// </summary>
    public int OriginalSize { get; init; }

    /// <summary>
    /// 压缩比（如果已压缩）
    /// </summary>
    public double CompressionRatio => OriginalSize > 0 ? (double)Length / OriginalSize : 1.0;
}

/// <summary>
/// 图像处理选项
/// </summary>
public readonly record struct ImageProcessingOptions
{
    /// <summary>
    /// 目标宽度（像素）
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// 目标高度（像素）
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// 缩放模式
    /// </summary>
    public ResizeMode ResizeMode { get; init; }

    /// <summary>
    /// 输出格式
    /// </summary>
    public ImageFormat OutputFormat { get; init; }

    /// <summary>
    /// 输出质量（1-100）
    /// </summary>
    public int Quality { get; init; }

    /// <summary>
    /// 是否生成 WebP 版本
    /// </summary>
    public bool GenerateWebP { get; init; }

    /// <summary>
    /// 是否生成 AVIF 版本
    /// </summary>
    public bool GenerateAvif { get; init; }

    /// <summary>
    /// 默认选项
    /// </summary>
    public static ImageProcessingOptions Default => new()
    {
        ResizeMode = ResizeMode.Fit,
        OutputFormat = ImageFormat.Original,
        Quality = 85,
        GenerateWebP = false,
        GenerateAvif = false
    };
}

/// <summary>
/// 图像缩放模式
/// </summary>
public enum ResizeMode
{
    /// <summary>
    /// 适应：保持宽高比，图像完全在目标尺寸内
    /// </summary>
    Fit,

    /// <summary>
    /// 填充：保持宽高比，填满目标尺寸（可能裁剪）
    /// </summary>
    Fill,

    /// <summary>
    /// 裁剪：从中心裁剪到目标尺寸
    /// </summary>
    Crop,

    /// <summary>
    /// 填充：保持宽高比，添加边距填充到目标尺寸
    /// </summary>
    Pad,

    /// <summary>
    /// 拉伸：不保持宽高比，拉伸到目标尺寸
    /// </summary>
    Stretch
}

/// <summary>
/// 图像输出格式
/// </summary>
public enum ImageFormat
{
    /// <summary>
    /// 保持原始格式
    /// </summary>
    Original,

    /// <summary>
    /// JPEG 格式
    /// </summary>
    Jpeg,

    /// <summary>
    /// PNG 格式
    /// </summary>
    Png,

    /// <summary>
    /// WebP 格式
    /// </summary>
    WebP,

    /// <summary>
    /// AVIF 格式
    /// </summary>
    Avif,

    /// <summary>
    /// GIF 格式
    /// </summary>
    Gif
}
