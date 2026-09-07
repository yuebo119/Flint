// Flint 静态站点生成器
// 资源管道集成测试
// 验证 SCSS 编译、CSS/JS 压缩、图片处理等功能

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Models;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace Flint.IntegrationTests.Assets;

/// <summary>
/// 资源管道集成测试
/// 验证从资源输入到处理输出的完整流程
/// _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6_
/// </summary>
[Collection("AssetPipeline")]
public class AssetPipelineIntegrationTests : IDisposable
{
    private readonly SassCompiler _sassCompiler;
    private readonly JavaScriptBundler _jsBundler;
    private readonly ImageProcessor _imageProcessor;
    private readonly AssetPipeline _pipeline;

    public AssetPipelineIntegrationTests()
    {
        _sassCompiler = new SassCompiler();
        _jsBundler = new JavaScriptBundler();
        _imageProcessor = new ImageProcessor();
        _pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                CompileSass = true,
                ProcessImages = true,
                ProcessScripts = true,
                MinifyCss = false,
                FailOnProcessingError = true
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

    #region SCSS 编译测试

    /// <summary>
    /// 测试 SCSS 变量编译
    /// 验证 SCSS 变量能正确编译为 CSS
    /// </summary>
    [Fact]
    public async Task CompileScss_WithVariables_ShouldProduceValidCss()
    {
        // Arrange - 包含变量的 SCSS
        var scss = """
            $primary-color: #007bff;
            $secondary-color: #6c757d;
            $font-size: 16px;

            body {
                color: $primary-color;
                background-color: $secondary-color;
                font-size: $font-size;
            }
            """;

        var asset = CreateScssAsset("variables.scss", scss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        result.Should().NotBeNull();
        result.MediaType.Should().Be("text/css");
        result.OutputPath.Should().EndWith(".css");

        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().Contain("#007bff");
        css.Should().Contain("#6c757d");
        css.Should().Contain("16px");
        css.Should().NotContain("$primary-color");
        css.Should().NotContain("$secondary-color");
    }

    /// <summary>
    /// 测试 SCSS 嵌套规则编译
    /// 验证嵌套选择器能正确展开
    /// </summary>
    [Fact]
    public async Task CompileScss_WithNesting_ShouldExpandSelectors()
    {
        // Arrange - 包含嵌套的 SCSS
        var scss = """
            .container {
                padding: 20px;

                .header {
                    background: #fff;

                    h1 {
                        font-size: 24px;
                    }
                }

                .content {
                    margin-top: 10px;
                }
            }
            """;

        var asset = CreateScssAsset("nesting.scss", scss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().Contain(".container");
        css.Should().Contain(".container .header");
        css.Should().Contain(".container .header h1");
        css.Should().Contain(".container .content");
    }

    /// <summary>
    /// 测试 SCSS Mixin 编译
    /// 验证 Mixin 能正确展开
    /// </summary>
    [Fact]
    public async Task CompileScss_WithMixin_ShouldExpandMixin()
    {
        // Arrange - 包含 Mixin 的 SCSS
        var scss = """
            @mixin flex-center {
                display: flex;
                justify-content: center;
                align-items: center;
            }

            @mixin button-style($bg-color, $text-color: #fff) {
                background-color: $bg-color;
                color: $text-color;
                padding: 10px 20px;
                border: none;
                cursor: pointer;
            }

            .centered-box {
                @include flex-center;
                width: 100%;
            }

            .primary-button {
                @include button-style(#007bff);
            }

            .secondary-button {
                @include button-style(#6c757d, #000);
            }
            """;

        var asset = CreateScssAsset("mixin.scss", scss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().Contain("display: flex");
        css.Should().Contain("justify-content: center");
        css.Should().Contain("align-items: center");
        css.Should().Contain("background-color: #007bff");
        css.Should().Contain("background-color: #6c757d");
        css.Should().NotContain("@mixin");
        css.Should().NotContain("@include");
    }

    /// <summary>
    /// 测试 SCSS 函数编译
    /// 验证内置函数能正确执行
    /// </summary>
    [Fact]
    public async Task CompileScss_WithFunctions_ShouldExecuteFunctions()
    {
        // Arrange - 包含函数的 SCSS
        var scss = """
            $base-color: #007bff;

            .lighten-box {
                background-color: lighten($base-color, 20%);
            }

            .darken-box {
                background-color: darken($base-color, 20%);
            }

            .transparent-box {
                background-color: rgba($base-color, 0.5);
            }

            .calculated-width {
                width: percentage(0.5);
                margin: round(10.6px);
            }
            """;

        var asset = CreateScssAsset("functions.scss", scss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().NotContain("lighten(");
        css.Should().NotContain("darken(");
        css.Should().NotContain("rgba($base-color");
        css.Should().Contain("50%"); // percentage(0.5)
        css.Should().Contain("11px"); // round(10.6px)
    }

    /// <summary>
    /// 测试 SCSS @import 相对路径解析
    /// 验证相对路径导入能正确解析
    /// </summary>
    [Fact]
    public async Task CompileScss_WithRelativeImport_ShouldResolveImport()
    {
        // Arrange - 创建临时目录和文件
        var tempDir = Path.Combine(Path.GetTempPath(), $"Flint_scss_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 创建被导入的文件
            var variablesScss = "$primary: #007bff;\n$secondary: #6c757d;";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "_variables.scss"), variablesScss);

            // 创建主文件
            var mainScss = """
                @import 'variables';

                body {
                    color: $primary;
                    background: $secondary;
                }
                """;
            var mainPath = Path.Combine(tempDir, "main.scss");
            await File.WriteAllTextAsync(mainPath, mainScss);

            var asset = new AssetFile
            {
                SourcePath = mainPath,
                MediaType = "text/x-scss",
                Content = Encoding.UTF8.GetBytes(mainScss),
                ModifiedTime = DateTimeOffset.UtcNow
            };

            // Act
            var result = await _sassCompiler.CompileAsync(asset, new SassOptions
            {
                IncludePaths = [tempDir]
            });

            // Assert
            var css = Encoding.UTF8.GetString(result.Content.Span);
            css.Should().Contain("#007bff");
            css.Should().Contain("#6c757d");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region CSS 压缩测试

    /// <summary>
    /// 测试 CSS 压缩
    /// 验证压缩后的 CSS 体积减小且仍然有效
    /// </summary>
    [Fact]
    public async Task CompileScss_WithMinify_ShouldProduceCompressedCss()
    {
        // Arrange
        var scss = """
            /* 这是一个注释 */
            .container {
                display: flex;
                flex-direction: column;
                align-items: center;
                justify-content: center;
                padding: 20px;
                margin: 10px;
            }

            .header {
                background-color: #ffffff;
                border-bottom: 1px solid #eeeeee;
            }
            """;

        var asset = CreateScssAsset("minify.scss", scss);

        // Act - 编译不压缩版本
        var normalResult = await _sassCompiler.CompileAsync(asset, new SassOptions
        {
            Minify = false,
            OutputStyle = SassOutputStyle.Expanded
        });

        // Act - 编译压缩版本
        var minifiedResult = await _sassCompiler.CompileAsync(asset, new SassOptions
        {
            Minify = true,
            OutputStyle = SassOutputStyle.Compressed
        });

        // Assert
        var normalCss = Encoding.UTF8.GetString(normalResult.Content.Span);
        var minifiedCss = Encoding.UTF8.GetString(minifiedResult.Content.Span);

        // 压缩版本应该更小
        minifiedCss.Length.Should().BeLessThan(normalCss.Length);

        // 压缩版本不应包含注释
        minifiedCss.Should().NotContain("这是一个注释");

        // 压缩版本应该包含所有必要的样式
        minifiedCss.Should().Contain(".container");
        minifiedCss.Should().Contain(".header");
        minifiedCss.Should().Contain("display:flex");

        // 验证压缩率
        var compressionRatio = (double)minifiedCss.Length / normalCss.Length;
        compressionRatio.Should().BeLessThan(0.8); // 至少压缩 20%
    }

    #endregion

    #region JavaScript 压缩测试

    /// <summary>
    /// 测试 JavaScript 压缩
    /// 验证压缩后的 JS 体积减小且仍然有效
    /// </summary>
    [Fact]
    public async Task TranspileJs_WithMinify_ShouldProduceCompressedJs()
    {
        // Arrange
        var js = """
            // 这是一个注释
            function greet(name) {
                const greeting = 'Hello, ' + name + '!';
                console.log(greeting);
                return greeting;
            }

            /* 多行注释
               这里是更多说明 */
            const users = ['Alice', 'Bob', 'Charlie'];
            users.forEach(function(user) {
                greet(user);
            });
            """;

        var asset = CreateJsAsset("app.js", js);

        // Act - 不压缩版本
        var normalResult = await _jsBundler.TranspileAsync(asset, new BundleOptions
        {
            Minify = false
        });

        // Act - 压缩版本
        var minifiedResult = await _jsBundler.TranspileAsync(asset, new BundleOptions
        {
            Minify = true
        });

        // Assert
        var normalJs = Encoding.UTF8.GetString(normalResult.Content.Span);
        var minifiedJs = Encoding.UTF8.GetString(minifiedResult.Content.Span);

        // 压缩版本应该更小
        minifiedJs.Length.Should().BeLessThan(normalJs.Length);

        // 压缩版本不应包含注释
        minifiedJs.Should().NotContain("这是一个注释");
        minifiedJs.Should().NotContain("多行注释");

        // 压缩版本应该包含核心代码
        minifiedJs.Should().Contain("greet");
        minifiedJs.Should().Contain("console.log");
    }

    #endregion

    #region 图片处理测试

    /// <summary>
    /// 测试 PNG 图片处理
    /// 验证 PNG 图片能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessImage_Png_ShouldProcessCorrectly()
    {
        // Arrange - 创建一个简单的 PNG 图片
        var pngContent = CreateMinimalPng(100, 100);
        var asset = new AssetFile
        {
            SourcePath = "images/test.png",
            MediaType = "image/png",
            Content = pngContent,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result.MediaType.Should().Be("image/png");
        result.Content.Length.Should().BeGreaterThan(0);
        result.ContentHash.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// 测试 JPEG 图片处理
    /// 验证 JPEG 图片能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessImage_Jpeg_ShouldProcessCorrectly()
    {
        // Arrange - 创建一个简单的 JPEG 图片
        var jpegContent = CreateMinimalJpeg(100, 100);
        var asset = new AssetFile
        {
            SourcePath = "images/photo.jpg",
            MediaType = "image/jpeg",
            Content = jpegContent,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result.MediaType.Should().Be("image/jpeg");
        result.Content.Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试 GIF 图片处理
    /// 验证 GIF 图片能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessImage_Gif_ShouldProcessCorrectly()
    {
        // Arrange - 创建一个简单的 GIF 图片
        var gifContent = CreateMinimalGif(50, 50);
        var asset = new AssetFile
        {
            SourcePath = "images/animation.gif",
            MediaType = "image/gif",
            Content = gifContent,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result.MediaType.Should().Be("image/gif");
        result.Content.Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试 WebP 图片处理
    /// 验证 WebP 图片能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessImage_WebP_ShouldProcessCorrectly()
    {
        // Arrange - 创建一个简单的 WebP 图片
        var webpContent = CreateMinimalWebP(80, 80);
        var asset = new AssetFile
        {
            SourcePath = "images/modern.webp",
            MediaType = "image/webp",
            Content = webpContent,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _imageProcessor.ProcessAsync(asset, ImageProcessingOptions.Default);

        // Assert
        result.Should().NotBeNull();
        result.MediaType.Should().Be("image/webp");
        result.Content.Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// 测试 SVG 图片处理
    /// 验证 SVG 图片能正确处理（直通，不经过 ImageProcessor）
    /// 注意：ImageSharp 不支持 SVG 格式，所以 SVG 应该直接传递
    /// </summary>
    [Fact]
    public async Task ProcessAsset_Svg_ShouldPassthrough()
    {
        // Arrange - SVG 是文本格式，应该直接传递（不经过图片处理器）
        var svgContent = """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">
                <circle cx="50" cy="50" r="40" fill="#007bff"/>
            </svg>
            """;

        // 创建一个不处理图片的管道
        var passthroughPipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                ProcessImages = false, // 禁用图片处理，SVG 直接传递
                CompileSass = false,
                ProcessScripts = false
            });

        var asset = new AssetFile
        {
            SourcePath = "images/icon.svg",
            MediaType = "image/svg+xml",
            Content = Encoding.UTF8.GetBytes(svgContent),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await passthroughPipeline.ProcessAsync(asset);

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().BeGreaterThan(0);
        var outputSvg = Encoding.UTF8.GetString(result.Content.Span);
        outputSvg.Should().Contain("<svg");
        outputSvg.Should().Contain("circle");
    }

    #endregion

    #region 字体文件处理测试

    /// <summary>
    /// 测试 WOFF 字体文件处理
    /// 验证 WOFF 字体能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_Woff_ShouldPassthrough()
    {
        // Arrange
        var woffContent = CreateDummyFontContent("woff");
        var asset = new AssetFile
        {
            SourcePath = "fonts/roboto.woff",
            MediaType = "font/woff",
            Content = woffContent,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _pipeline.ProcessAsync(asset);

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().Be(woffContent.Length);
        result.ContentHash.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// 测试 WOFF2 字体文件处理
    /// 验证 WOFF2 字体能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_Woff2_ShouldPassthrough()
    {
        // Arrange
        var woff2Content = CreateDummyFontContent("woff2");
        var asset = new AssetFile
        {
            SourcePath = "fonts/opensans.woff2",
            MediaType = "font/woff2",
            Content = woff2Content,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _pipeline.ProcessAsync(asset);

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().Be(woff2Content.Length);
    }

    /// <summary>
    /// 测试 TTF 字体文件处理
    /// 验证 TTF 字体能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_Ttf_ShouldPassthrough()
    {
        // Arrange
        var ttfContent = CreateDummyFontContent("ttf");
        var asset = new AssetFile
        {
            SourcePath = "fonts/arial.ttf",
            MediaType = "font/ttf",
            Content = ttfContent,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _pipeline.ProcessAsync(asset);

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().Be(ttfContent.Length);
    }

    /// <summary>
    /// 测试 EOT 字体文件处理
    /// 验证 EOT 字体能正确处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_Eot_ShouldPassthrough()
    {
        // Arrange
        var eotContent = CreateDummyFontContent("eot");
        var asset = new AssetFile
        {
            SourcePath = "fonts/legacy.eot",
            MediaType = "application/vnd.ms-fontobject",
            Content = eotContent,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _pipeline.ProcessAsync(asset);

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().Be(eotContent.Length);
    }

    #endregion

    #region 源映射生成测试

    /// <summary>
    /// 测试 SCSS 编译生成源映射
    /// 验证源映射能正确生成
    /// </summary>
    [Fact]
    public async Task CompileScss_WithSourceMap_ShouldGenerateSourceMap()
    {
        // Arrange
        var scss = """
            $color: #007bff;
            .test { color: $color; }
            """;
        var asset = CreateScssAsset("sourcemap.scss", scss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions
        {
            GenerateSourceMap = true
        });

        // Assert
        var css = Encoding.UTF8.GetString(result.Content.Span);
        // 源映射引用应该在 CSS 末尾
        css.Should().Contain("sourceMappingURL");
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

    /// <summary>
    /// 创建 JavaScript 资源文件
    /// </summary>
    private static AssetFile CreateJsAsset(string name, string content)
    {
        return new AssetFile
        {
            SourcePath = $"scripts/{name}",
            MediaType = "application/javascript",
            Content = Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 创建最小有效的 PNG 图片
    /// 使用 ImageSharp 创建真实的 PNG 图片
    /// </summary>
    private static ReadOnlyMemory<byte> CreateMinimalPng(int width, int height)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        // 填充一个简单的颜色
        image.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.Blue));

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 创建最小有效的 JPEG 图片
    /// </summary>
    private static ReadOnlyMemory<byte> CreateMinimalJpeg(int width, int height)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        image.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.Red));

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 创建最小有效的 GIF 图片
    /// </summary>
    private static ReadOnlyMemory<byte> CreateMinimalGif(int width, int height)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        image.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.Green));

        using var ms = new MemoryStream();
        image.SaveAsGif(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 创建最小有效的 WebP 图片
    /// </summary>
    private static ReadOnlyMemory<byte> CreateMinimalWebP(int width, int height)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        image.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.Yellow));

        using var ms = new MemoryStream();
        image.SaveAsWebp(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// 创建虚拟字体内容
    /// </summary>
    private static ReadOnlyMemory<byte> CreateDummyFontContent(string type)
    {
        // 创建一个简单的虚拟字体数据
        var header = type switch
        {
            "woff" => new byte[] { 0x77, 0x4F, 0x46, 0x46 }, // wOFF
            "woff2" => new byte[] { 0x77, 0x4F, 0x46, 0x32 }, // wOF2
            "ttf" => new byte[] { 0x00, 0x01, 0x00, 0x00 }, // TrueType
            "eot" => new byte[] { 0x00, 0x00, 0x01, 0x00 }, // EOT
            _ => new byte[] { 0x00, 0x00, 0x00, 0x00 }
        };

        // 添加一些填充数据
        var content = new byte[256];
        Array.Copy(header, content, header.Length);
        return content;
    }

    #endregion
}
