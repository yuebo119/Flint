// Flint 静态站点生成器
// 资源处理管道接口

using Flint.Core.Models;

namespace Flint.Core.Abstractions;

/// <summary>
/// 资源处理管道接口
/// </summary>
public interface IAssetPipeline
{
    /// <summary>
    /// 处理单个资源
    /// </summary>
    /// <param name="asset">资源文件</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的资源</returns>
    ValueTask<ProcessedAsset> ProcessAsync(
        AssetFile asset,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量处理资源
    /// </summary>
    /// <param name="assets">资源文件列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的资源列表</returns>
    ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
        IReadOnlyList<AssetFile> assets,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式处理资源
    /// </summary>
    /// <param name="assets">资源文件流</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的资源流</returns>
    IAsyncEnumerable<ProcessedAsset> ProcessStreamAsync(
        IAsyncEnumerable<AssetFile> assets,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 图像处理器接口
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    /// 处理图像
    /// </summary>
    /// <param name="image">图像资源</param>
    /// <param name="options">处理选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>处理后的资源</returns>
    ValueTask<ProcessedAsset> ProcessAsync(
        AssetFile image,
        ImageProcessingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 生成响应式图像
    /// </summary>
    /// <param name="image">图像资源</param>
    /// <param name="widths">目标宽度列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>不同尺寸的图像列表</returns>
    ValueTask<IReadOnlyList<ProcessedAsset>> GenerateResponsiveAsync(
        AssetFile image,
        int[] widths,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取图像信息
    /// </summary>
    /// <param name="image">图像资源</param>
    /// <returns>图像信息</returns>
    ImageInfo GetImageInfo(AssetFile image);
}

/// <summary>
/// 图像信息
/// </summary>
public readonly record struct ImageInfo
{
    /// <summary>
    /// 宽度（像素）
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// 高度（像素）
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// 图像格式
    /// </summary>
    public required string Format { get; init; }

    /// <summary>
    /// 是否有透明通道
    /// </summary>
    public bool HasAlpha { get; init; }

    /// <summary>
    /// 色彩深度
    /// </summary>
    public int BitsPerPixel { get; init; }

    /// <summary>
    /// 宽高比
    /// </summary>
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;
}

/// <summary>
/// Sass 编译器接口
/// </summary>
public interface ISassCompiler
{
    /// <summary>
    /// 编译 Sass/SCSS 文件
    /// </summary>
    /// <param name="sassFile">Sass 文件</param>
    /// <param name="options">编译选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>编译后的 CSS</returns>
    ValueTask<ProcessedAsset> CompileAsync(
        AssetFile sassFile,
        SassOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Sass 编译选项
/// </summary>
public sealed class SassOptions
{
    /// <summary>
    /// 输出样式
    /// </summary>
    public SassOutputStyle OutputStyle { get; init; } = SassOutputStyle.Expanded;

    /// <summary>
    /// 是否生成 Source Map
    /// </summary>
    public bool GenerateSourceMap { get; init; }

    /// <summary>
    /// 包含路径
    /// </summary>
    public IReadOnlyList<string> IncludePaths { get; init; } = [];

    /// <summary>
    /// 是否压缩输出
    /// </summary>
    public bool Minify { get; init; }
}

/// <summary>
/// Sass 输出样式
/// </summary>
public enum SassOutputStyle
{
    /// <summary>
    /// 展开格式
    /// </summary>
    Expanded,

    /// <summary>
    /// 压缩格式
    /// </summary>
    Compressed
}

/// <summary>
/// JavaScript 打包器接口
/// </summary>
public interface IJavaScriptBundler
{
    /// <summary>
    /// 打包 JavaScript 文件
    /// </summary>
    /// <param name="scripts">脚本文件列表</param>
    /// <param name="options">打包选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>打包后的资源</returns>
    ValueTask<ProcessedAsset> BundleAsync(
        IReadOnlyList<AssetFile> scripts,
        BundleOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 转译单个 JavaScript 文件
    /// </summary>
    /// <param name="script">脚本文件</param>
    /// <param name="options">打包选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>转译后的资源</returns>
    ValueTask<ProcessedAsset> TranspileAsync(
        AssetFile script,
        BundleOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// JavaScript 打包选项
/// </summary>
public sealed class BundleOptions
{
    /// <summary>
    /// 是否压缩
    /// </summary>
    public bool Minify { get; init; }

    /// <summary>
    /// 是否生成 Source Map
    /// </summary>
    public bool GenerateSourceMap { get; init; }

    /// <summary>
    /// 目标 ES 版本
    /// </summary>
    public string Target { get; init; } = "es2020";

    /// <summary>
    /// 输出格式
    /// </summary>
    public JsOutputFormat Format { get; init; } = JsOutputFormat.Esm;

    /// <summary>
    /// 外部依赖（不打包）
    /// </summary>
    public IReadOnlyList<string> External { get; init; } = [];

    /// <summary>
    /// 是否启用 Tree Shaking
    /// </summary>
    public bool TreeShaking { get; init; } = true;
}

/// <summary>
/// JavaScript 输出格式
/// </summary>
public enum JsOutputFormat
{
    /// <summary>
    /// ES 模块
    /// </summary>
    Esm,

    /// <summary>
    /// CommonJS
    /// </summary>
    Cjs,

    /// <summary>
    /// 立即执行函数
    /// </summary>
    Iife
}
