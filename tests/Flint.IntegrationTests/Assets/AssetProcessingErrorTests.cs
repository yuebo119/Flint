// Flint 静态站点生成器
// 资源处理错误测试
// 测试 SCSS 编译错误报告、无效图片文件处理、损坏文件处理
// _Requirements: 6.7_

using System.Text;
using DartSassHost;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Models;
using FluentAssertions;
using Xunit;
// 使用别名解决命名冲突
using FlintSassCompiler = Flint.Core.Assets.SassCompiler;

namespace Flint.IntegrationTests.Assets;

/// <summary>
/// 资源处理错误测试
/// 验证错误处理和错误报告功能
/// _Requirements: 6.7_
/// </summary>
[Collection("AssetPipeline")]
public class AssetProcessingErrorTests : IDisposable
{
    private readonly FlintSassCompiler _sassCompiler;
    private readonly JavaScriptBundler _jsBundler;
    private readonly ImageProcessor _imageProcessor;
    private readonly AssetPipeline _pipeline;

    public AssetProcessingErrorTests()
    {
        _sassCompiler = new FlintSassCompiler();
        _jsBundler = new JavaScriptBundler();
        _imageProcessor = new ImageProcessor();
        _pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                CompileSass = true,
                ProcessImages = true,
                ProcessScripts = true,
                FailOnProcessingError = true // 启用错误抛出
            },
            _imageProcessor,
            _sassCompiler,
            _jsBundler);
    }

    public void Dispose()
    {
        _sassCompiler.Dispose();
        GC.SuppressFinalize(this);
    }

    #region SCSS 编译错误测试

    /// <summary>
    /// 测试 SCSS 语法错误报告
    /// 验证语法错误能被正确捕获和报告
    /// </summary>
    [Fact]
    public async Task CompileScss_WithSyntaxError_ShouldThrowWithDetails()
    {
        // Arrange - 包含语法错误的 SCSS
        var invalidScss = """
            .container {
                color: #007bff
                background: #fff; // 缺少分号
            }
            """;

        var asset = CreateScssAsset("syntax-error.scss", invalidScss);

        // Act & Assert
        var act = async () => await _sassCompiler.CompileAsync(asset, new SassOptions());

        await act.Should().ThrowAsync<SassCompilationException>();
    }

    /// <summary>
    /// 测试 SCSS 未定义变量错误
    /// 验证使用未定义变量时能正确报告错误
    /// </summary>
    [Fact]
    public async Task CompileScss_WithUndefinedVariable_ShouldThrowWithDetails()
    {
        // Arrange - 使用未定义的变量
        var invalidScss = """
            .container {
                color: $undefined-variable;
            }
            """;

        var asset = CreateScssAsset("undefined-var.scss", invalidScss);

        // Act & Assert
        var act = async () => await _sassCompiler.CompileAsync(asset, new SassOptions());

        await act.Should().ThrowAsync<SassCompilationException>();
    }

    /// <summary>
    /// 测试 SCSS 导入错误
    /// 验证导入不存在的文件时能正确报告错误
    /// </summary>
    [Fact]
    public async Task CompileScss_WithMissingImport_ShouldThrowWithDetails()
    {
        // Arrange - 导入不存在的文件
        var invalidScss = """
            @import 'non-existent-file';
            
            .container {
                color: #007bff;
            }
            """;

        var asset = CreateScssAsset("missing-import.scss", invalidScss);

        // Act & Assert
        var act = async () => await _sassCompiler.CompileAsync(asset, new SassOptions());

        await act.Should().ThrowAsync<SassCompilationException>();
    }

    /// <summary>
    /// 测试 SCSS 无效函数调用错误
    /// 验证调用无效函数时能正确报告错误
    /// </summary>
    [Fact]
    public async Task CompileScss_WithInvalidFunction_ShouldThrowWithDetails()
    {
        // Arrange - 调用无效的函数
        var invalidScss = """
            .container {
                color: nonexistent-function(#007bff);
            }
            """;

        var asset = CreateScssAsset("invalid-function.scss", invalidScss);

        // Act & Assert - 注意：某些无效函数可能被当作 CSS 函数处理
        // 这里测试明显的语法错误
        var invalidScss2 = """
            .container {
                color: lighten(); // 缺少参数
            }
            """;

        var asset2 = CreateScssAsset("invalid-function2.scss", invalidScss2);
        var act = async () => await _sassCompiler.CompileAsync(asset2, new SassOptions());

        await act.Should().ThrowAsync<SassCompilationException>();
    }

    /// <summary>
    /// 测试 SCSS Mixin 参数错误
    /// 验证 Mixin 参数不匹配时能正确报告错误
    /// </summary>
    [Fact]
    public async Task CompileScss_WithMixinArgumentError_ShouldThrowWithDetails()
    {
        // Arrange - Mixin 参数不匹配
        var invalidScss = """
            @mixin button-style($color, $size) {
                color: $color;
                font-size: $size;
            }
            
            .button {
                @include button-style(#007bff); // 缺少第二个参数
            }
            """;

        var asset = CreateScssAsset("mixin-error.scss", invalidScss);

        // Act & Assert
        var act = async () => await _sassCompiler.CompileAsync(asset, new SassOptions());

        await act.Should().ThrowAsync<SassCompilationException>();
    }

    #endregion

    #region 图片处理错误测试

    /// <summary>
    /// 测试无效图片文件处理
    /// 验证处理无效图片数据时能正确报告错误
    /// </summary>
    [Fact]
    public async Task ProcessImage_WithInvalidData_ShouldThrow()
    {
        // Arrange - 无效的图片数据
        var invalidImageData = Encoding.UTF8.GetBytes("This is not an image");
        var asset = new AssetFile
        {
            SourcePath = "images/invalid.png",
            MediaType = "image/png",
            Content = invalidImageData,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act & Assert
        var act = async () => await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// 测试损坏的 PNG 文件处理
    /// 验证处理损坏的 PNG 文件时能正确报告错误
    /// </summary>
    [Fact]
    public async Task ProcessImage_WithCorruptedPng_ShouldThrow()
    {
        // Arrange - 损坏的 PNG 文件（只有文件头，没有完整数据）
        var corruptedPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        var asset = new AssetFile
        {
            SourcePath = "images/corrupted.png",
            MediaType = "image/png",
            Content = corruptedPng,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act & Assert
        var act = async () => await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// 测试损坏的 JPEG 文件处理
    /// 验证处理损坏的 JPEG 文件时能正确报告错误
    /// </summary>
    [Fact]
    public async Task ProcessImage_WithCorruptedJpeg_ShouldThrow()
    {
        // Arrange - 损坏的 JPEG 文件
        var corruptedJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var asset = new AssetFile
        {
            SourcePath = "images/corrupted.jpg",
            MediaType = "image/jpeg",
            Content = corruptedJpeg,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act & Assert
        var act = async () => await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// 测试空图片文件处理
    /// 验证处理空图片文件时能正确报告错误
    /// </summary>
    [Fact]
    public async Task ProcessImage_WithEmptyData_ShouldThrow()
    {
        // Arrange - 空数据
        var emptyData = Array.Empty<byte>();
        var asset = new AssetFile
        {
            SourcePath = "images/empty.png",
            MediaType = "image/png",
            Content = emptyData,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act & Assert
        var act = async () => await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region 管道错误处理测试

    /// <summary>
    /// 测试管道在 FailOnProcessingError=false 时的行为
    /// 验证错误被静默处理，返回原始资源
    /// </summary>
    [Fact]
    public async Task Pipeline_WithFailOnProcessingErrorFalse_ShouldReturnOriginal()
    {
        // Arrange
        var silentPipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                CompileSass = true,
                FailOnProcessingError = false // 不抛出错误
            },
            null,
            _sassCompiler,
            null);

        var invalidScss = """
            .container {
                color: $undefined-variable;
            }
            """;

        var asset = CreateScssAsset("silent-error.scss", invalidScss);

        // Act
        var result = await silentPipeline.ProcessAsync(asset);

        // Assert - 应该返回原始资源（直通）
        result.Should().NotBeNull();
        result.SourcePath.Should().Be(asset.SourcePath);
    }

    /// <summary>
    /// 测试管道在 FailOnProcessingError=true 时的行为
    /// 验证错误被正确抛出
    /// </summary>
    [Fact]
    public async Task Pipeline_WithFailOnProcessingErrorTrue_ShouldThrow()
    {
        // Arrange
        var strictPipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                CompileSass = true,
                FailOnProcessingError = true // 抛出错误
            },
            null,
            _sassCompiler,
            null);

        var invalidScss = """
            .container {
                color: $undefined-variable;
            }
            """;

        var asset = CreateScssAsset("strict-error.scss", invalidScss);

        // Act & Assert
        var act = async () => await strictPipeline.ProcessAsync(asset);

        await act.Should().ThrowAsync<Exception>();
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 创建 SCSS 资源文件
    /// </summary>
    private static AssetFile CreateScssAsset(string name, string content)
    {
        return new AssetFile
        {
            SourcePath = $"styles/{name}",
            MediaType = "text/x-scss",
            Content = Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
