// Flint 静态站点生成器
// 资源管道边界条件测试
// 测试空文件、超大文件、深层嵌套、循环导入、特殊字符等边界条件
// _Requirements: 6.1, 6.7_

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
/// 资源管道边界条件测试
/// 验证各种边界条件下的行为
/// _Requirements: 6.1, 6.7_
/// </summary>
[Collection("AssetPipeline")]
public class AssetPipelineBoundaryTests : IDisposable
{
    private readonly FlintSassCompiler _sassCompiler;
    private readonly JavaScriptBundler _jsBundler;
    private readonly AssetPipeline _pipeline;

    public AssetPipelineBoundaryTests()
    {
        _sassCompiler = new FlintSassCompiler();
        _jsBundler = new JavaScriptBundler();
        _pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                CompileSass = true,
                ProcessScripts = true,
                ProcessImages = false,
                FailOnProcessingError = true
            },
            null,
            _sassCompiler,
            _jsBundler);
    }

    public void Dispose()
    {
        _sassCompiler.Dispose();
        GC.SuppressFinalize(this);
    }

    #region 空文件测试

    /// <summary>
    /// 测试空 SCSS 文件
    /// 验证空 SCSS 文件能正确处理（DartSassHost 不接受空内容，应该抛出异常）
    /// </summary>
    [Fact]
    public async Task CompileScss_WithEmptyFile_ShouldThrowArgumentException()
    {
        // Arrange
        var emptyScss = "";
        var asset = CreateScssAsset("empty.scss", emptyScss);

        // Act & Assert - DartSassHost 不接受空内容
        var act = async () => await _sassCompiler.CompileAsync(asset, new SassOptions());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// 测试只有注释的 SCSS 文件
    /// 验证只有注释的 SCSS 文件能正确处理
    /// 注意：Sass 编译器会保留多行注释（/* */），但会移除单行注释（//）
    /// </summary>
    [Fact]
    public async Task CompileScss_WithOnlyComments_ShouldProduceCssWithMultilineComments()
    {
        // Arrange
        var commentOnlyScss = """
            // 这是单行注释
            /* 这是多行注释
               跨越多行 */
            """;
        var asset = CreateScssAsset("comments-only.scss", commentOnlyScss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        result.Should().NotBeNull();
        var css = Encoding.UTF8.GetString(result.Content.Span);
        // Sass 保留多行注释，但移除单行注释
        css.Should().NotContain("这是单行注释");
        css.Should().Contain("这是多行注释"); // 多行注释被保留
    }

    /// <summary>
    /// 测试只有空白的 SCSS 文件
    /// 验证只有空白字符的 SCSS 文件会抛出异常（DartSassHost 不接受空内容）
    /// </summary>
    [Fact]
    public async Task CompileScss_WithOnlyWhitespace_ShouldThrowArgumentException()
    {
        // Arrange
        var whitespaceScss = "   \n\t\n   \r\n   ";
        var asset = CreateScssAsset("whitespace.scss", whitespaceScss);

        // Act & Assert - DartSassHost 将空白视为空内容
        var act = async () => await _sassCompiler.CompileAsync(asset, new SassOptions());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// 测试空 JavaScript 文件
    /// 验证空 JS 文件能正确处理
    /// </summary>
    [Fact]
    public async Task TranspileJs_WithEmptyFile_ShouldProduceEmptyJs()
    {
        // Arrange
        var emptyJs = "";
        var asset = CreateJsAsset("empty.js", emptyJs);

        // Act
        var result = await _jsBundler.TranspileAsync(asset, new BundleOptions());

        // Assert
        result.Should().NotBeNull();
        result.MediaType.Should().Be("application/javascript");
    }

    #endregion

    #region 超大文件测试

    /// <summary>
    /// 测试超大 SCSS 文件
    /// 验证大型 SCSS 文件能正确处理
    /// </summary>
    [Fact]
    public async Task CompileScss_WithLargeFile_ShouldProcessCorrectly()
    {
        // Arrange - 生成大型 SCSS 文件（约 100KB）
        var sb = new StringBuilder();
        for (var i = 0; i < 1000; i++)
        {
            sb.AppendLine($".class-{i} {{");
            sb.AppendLine($"    color: #{i:x6};");
            sb.AppendLine($"    padding: {i % 100}px;");
            sb.AppendLine($"    margin: {i % 50}px;");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        var largeScss = sb.ToString();
        var asset = CreateScssAsset("large.scss", largeScss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().BeGreaterThan(0);
        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().Contain(".class-0");
        css.Should().Contain(".class-999");
    }

    /// <summary>
    /// 测试超大 JavaScript 文件
    /// 验证大型 JS 文件能正确处理
    /// </summary>
    [Fact]
    public async Task TranspileJs_WithLargeFile_ShouldProcessCorrectly()
    {
        // Arrange - 生成大型 JS 文件
        var sb = new StringBuilder();
        for (var i = 0; i < 1000; i++)
        {
            sb.AppendLine($"const variable{i} = {i};");
            sb.AppendLine($"function func{i}() {{ return variable{i}; }}");
        }

        var largeJs = sb.ToString();
        var asset = CreateJsAsset("large.js", largeJs);

        // Act
        var result = await _jsBundler.TranspileAsync(asset, new BundleOptions());

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().BeGreaterThan(0);
        var js = Encoding.UTF8.GetString(result.Content.Span);
        js.Should().Contain("variable0");
        js.Should().Contain("variable999");
    }

    #endregion

    #region 深层嵌套测试

    /// <summary>
    /// 测试深层嵌套 SCSS
    /// 验证深层嵌套的 SCSS 能正确处理
    /// </summary>
    [Fact]
    public async Task CompileScss_WithDeepNesting_ShouldProcessCorrectly()
    {
        // Arrange - 生成深层嵌套的 SCSS（10 层）
        var sb = new StringBuilder();
        var indent = "";

        for (var i = 0; i < 10; i++)
        {
            sb.AppendLine($"{indent}.level-{i} {{");
            indent += "    ";
        }

        sb.AppendLine($"{indent}color: #007bff;");

        for (var i = 9; i >= 0; i--)
        {
            indent = new string(' ', i * 4);
            sb.AppendLine($"{indent}}}");
        }

        var deepScss = sb.ToString();
        var asset = CreateScssAsset("deep-nesting.scss", deepScss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        result.Should().NotBeNull();
        var css = Encoding.UTF8.GetString(result.Content.Span);
        // 验证选择器被正确展开
        css.Should().Contain(".level-0");
        css.Should().Contain(".level-9");
    }

    /// <summary>
    /// 测试深层嵌套 @import
    /// 验证深层嵌套的 @import 能正确处理
    /// </summary>
    [Fact]
    public async Task CompileScss_WithDeepImportNesting_ShouldProcessCorrectly()
    {
        // Arrange - 创建临时目录和多层导入文件
        var tempDir = Path.Combine(Path.GetTempPath(), $"Flint_deep_import_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 创建 5 层嵌套的导入
            for (var i = 4; i >= 0; i--)
            {
                var content = i == 4
                    ? "$deep-color: #007bff;\n.deep { color: $deep-color; }"
                    : $"@import 'level{i + 1}';\n.level{i} {{ padding: {i}px; }}";

                await File.WriteAllTextAsync(
                    Path.Combine(tempDir, $"_level{i}.scss"),
                    content);
            }

            // 创建主文件
            var mainScss = "@import 'level0';\n.main { margin: 10px; }";
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
            css.Should().Contain(".deep");
            css.Should().Contain(".level0");
            css.Should().Contain(".main");
            css.Should().Contain("#007bff");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region 循环导入测试

    /// <summary>
    /// 测试循环 @import 错误处理
    /// 验证循环导入能被正确检测和报告
    /// </summary>
    [Fact]
    public async Task CompileScss_WithCircularImport_ShouldHandleGracefully()
    {
        // Arrange - 创建循环导入
        var tempDir = Path.Combine(Path.GetTempPath(), $"Flint_circular_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 创建循环导入：a -> b -> a
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "_a.scss"),
                "@import 'b';\n.a { color: red; }");

            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "_b.scss"),
                "@import 'a';\n.b { color: blue; }");

            var mainScss = "@import 'a';";
            var mainPath = Path.Combine(tempDir, "main.scss");
            await File.WriteAllTextAsync(mainPath, mainScss);

            var asset = new AssetFile
            {
                SourcePath = mainPath,
                MediaType = "text/x-scss",
                Content = Encoding.UTF8.GetBytes(mainScss),
                ModifiedTime = DateTimeOffset.UtcNow
            };

            // Act - Sass 编译器应该能处理循环导入（通过只导入一次）
            // 或者抛出错误
            try
            {
                var result = await _sassCompiler.CompileAsync(asset, new SassOptions
                {
                    IncludePaths = [tempDir]
                });

                // 如果成功，验证输出包含预期内容
                var css = Encoding.UTF8.GetString(result.Content.Span);
                css.Should().Contain(".a");
                css.Should().Contain(".b");
            }
            catch (SassCompilationException)
            {
                // 循环导入错误也是可接受的行为
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region 特殊字符文件名测试

    /// <summary>
    /// 测试包含特殊字符的文件名
    /// 验证特殊字符文件名能正确处理
    /// </summary>
    [Theory]
    [InlineData("style-with-dash.scss")]
    [InlineData("style_with_underscore.scss")]
    [InlineData("style.min.scss")]
    [InlineData("样式文件.scss")]
    [InlineData("スタイル.scss")]
    public async Task CompileScss_WithSpecialFilename_ShouldProcessCorrectly(string filename)
    {
        // Arrange
        var scss = ".test { color: #007bff; }";
        var asset = new AssetFile
        {
            SourcePath = $"styles/{filename}",
            MediaType = "text/x-scss",
            Content = Encoding.UTF8.GetBytes(scss),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        result.Should().NotBeNull();
        result.OutputPath.Should().EndWith(".css");
        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().Contain(".test");
    }

    /// <summary>
    /// 测试包含空格的文件名
    /// 验证包含空格的文件名能正确处理
    /// </summary>
    [Fact]
    public async Task CompileScss_WithSpaceInFilename_ShouldProcessCorrectly()
    {
        // Arrange
        var scss = ".test { color: #007bff; }";
        var asset = new AssetFile
        {
            SourcePath = "styles/my style file.scss",
            MediaType = "text/x-scss",
            Content = Encoding.UTF8.GetBytes(scss),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        result.Should().NotBeNull();
        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().Contain(".test");
    }

    #endregion

    #region 二进制文件处理测试

    /// <summary>
    /// 测试二进制文件直通处理
    /// 验证二进制文件能正确直通处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_BinaryFile_ShouldPassthrough()
    {
        // Arrange - 创建二进制数据
        var binaryData = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            binaryData[i] = (byte)i;
        }

        var asset = new AssetFile
        {
            SourcePath = "data/binary.dat",
            MediaType = "application/octet-stream",
            Content = binaryData,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _pipeline.ProcessAsync(asset);

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().Be(binaryData.Length);
        result.Content.ToArray().Should().BeEquivalentTo(binaryData);
    }

    /// <summary>
    /// 测试 PDF 文件直通处理
    /// 验证 PDF 文件能正确直通处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_PdfFile_ShouldPassthrough()
    {
        // Arrange - 创建简单的 PDF 文件头
        var pdfData = Encoding.ASCII.GetBytes("%PDF-1.4\n%test content");
        var asset = new AssetFile
        {
            SourcePath = "docs/document.pdf",
            MediaType = "application/pdf",
            Content = pdfData,
            ModifiedTime = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _pipeline.ProcessAsync(asset);

        // Assert
        result.Should().NotBeNull();
        result.Content.Length.Should().Be(pdfData.Length);
    }

    #endregion

    #region Unicode 内容测试

    /// <summary>
    /// 测试包含 Unicode 字符的 SCSS
    /// 验证 Unicode 字符能正确处理
    /// </summary>
    [Fact]
    public async Task CompileScss_WithUnicodeContent_ShouldProcessCorrectly()
    {
        // Arrange - 包含各种 Unicode 字符的 SCSS
        var unicodeScss = """
            // 中文注释
            /* 日本語コメント */
            .中文类名 {
                content: "你好世界";
            }
            
            .emoji-test {
                content: "🎉🚀💻";
            }
            
            .special-chars {
                content: "αβγδ ∑∏∫";
            }
            """;

        var asset = CreateScssAsset("unicode.scss", unicodeScss);

        // Act
        var result = await _sassCompiler.CompileAsync(asset, new SassOptions());

        // Assert
        result.Should().NotBeNull();
        var css = Encoding.UTF8.GetString(result.Content.Span);
        css.Should().Contain("你好世界");
        css.Should().Contain("🎉🚀💻");
        css.Should().Contain("αβγδ");
    }

    /// <summary>
    /// 测试包含 Unicode 字符的 JavaScript
    /// 验证 Unicode 字符能正确处理
    /// </summary>
    [Fact]
    public async Task TranspileJs_WithUnicodeContent_ShouldProcessCorrectly()
    {
        // Arrange
        var unicodeJs = """
            // 中文注释
            const 变量 = "你好世界";
            const emoji = "🎉🚀💻";
            console.log(变量, emoji);
            """;

        var asset = CreateJsAsset("unicode.js", unicodeJs);

        // Act
        var result = await _jsBundler.TranspileAsync(asset, new BundleOptions());

        // Assert
        result.Should().NotBeNull();
        var js = Encoding.UTF8.GetString(result.Content.Span);
        js.Should().Contain("你好世界");
        js.Should().Contain("🎉🚀💻");
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

    #endregion
}
