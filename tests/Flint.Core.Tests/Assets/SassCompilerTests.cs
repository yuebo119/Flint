// Flint 静态站点生成器
// Sass/SCSS 编译器单元测试

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Models;
using Xunit;

namespace Flint.Core.Tests.Assets;

/// <summary>
/// Sass/SCSS 编译器单元测试
/// 注意：这些测试需要 DartSass.Native 包正确安装
/// 在某些 CI 环境中可能需要跳过
/// </summary>
[Collection("SassCompiler")]
public class SassCompilerTests : IDisposable
{
    private readonly SassCompiler _compiler;

    public SassCompilerTests()
    {
        _compiler = new SassCompiler();
    }

    public void Dispose()
    {
        _compiler.Dispose();
        GC.SuppressFinalize(this);
    }

    #region 辅助方法

    /// <summary>
    /// 创建测试用的 SCSS 文件
    /// </summary>
    private static AssetFile CreateScssFile(string content, string path = "test.scss")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new AssetFile
        {
            SourcePath = path,
            MediaType = "text/x-scss",
            Content = bytes,
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 创建测试用的 SASS 文件（缩进语法）
    /// </summary>
    private static AssetFile CreateSassFile(string content, string path = "test.sass")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new AssetFile
        {
            SourcePath = path,
            MediaType = "text/x-sass",
            Content = bytes,
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 获取编译结果的 CSS 内容
    /// </summary>
    private static string GetCssContent(ProcessedAsset result)
    {
        return Encoding.UTF8.GetString(result.Content.Span);
    }

    #endregion

    #region 基本编译测试

    [Fact]
    public async Task CompileAsync_应该编译简单的SCSS()
    {
        // Arrange
        var scss = @"
$primary-color: #333;

body {
    color: $primary-color;
}
";
        var file = CreateScssFile(scss);
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("text/css", result.MediaType);
        var css = GetCssContent(result);
        Assert.Contains("color:", css);
        Assert.Contains("#333", css);
    }

    [Fact]
    public async Task CompileAsync_应该编译嵌套规则()
    {
        // Arrange
        var scss = @"
nav {
    ul {
        margin: 0;
        padding: 0;
        list-style: none;
    }

    li { display: inline-block; }

    a {
        display: block;
        padding: 6px 12px;
        text-decoration: none;
    }
}
";
        var file = CreateScssFile(scss);
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        Assert.Contains("nav ul", css);
        Assert.Contains("nav li", css);
        Assert.Contains("nav a", css);
    }

    [Fact]
    public async Task CompileAsync_应该处理Mixin()
    {
        // Arrange
        var scss = @"
@mixin border-radius($radius) {
    -webkit-border-radius: $radius;
    -moz-border-radius: $radius;
    border-radius: $radius;
}

.box { @include border-radius(10px); }
";
        var file = CreateScssFile(scss);
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        Assert.Contains("-webkit-border-radius: 10px", css);
        Assert.Contains("-moz-border-radius: 10px", css);
        Assert.Contains("border-radius: 10px", css);
    }

    [Fact]
    public async Task CompileAsync_应该处理继承()
    {
        // Arrange
        var scss = @"
%message-shared {
    border: 1px solid #ccc;
    padding: 10px;
    color: #333;
}

.success {
    @extend %message-shared;
    border-color: green;
}

.error {
    @extend %message-shared;
    border-color: red;
}
";
        var file = CreateScssFile(scss);
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        Assert.Contains(".success", css);
        Assert.Contains(".error", css);
        Assert.Contains("border-color: green", css);
        Assert.Contains("border-color: red", css);
    }

    #endregion

    #region 输出格式测试

    [Fact]
    public async Task CompileAsync_Expanded输出样式应该格式化()
    {
        // Arrange
        var scss = "body { color: red; margin: 0; }";
        var file = CreateScssFile(scss);
        var options = new SassOptions { OutputStyle = SassOutputStyle.Expanded };

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        // Expanded 格式应该有换行
        Assert.Contains("\n", css);
    }

    [Fact]
    public async Task CompileAsync_Compressed输出样式应该压缩()
    {
        // Arrange
        var scss = @"
body {
    color: red;
    margin: 0;
    padding: 0;
}
";
        var file = CreateScssFile(scss);
        var options = new SassOptions { OutputStyle = SassOutputStyle.Compressed };

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        // Compressed 格式应该没有多余空白
        Assert.DoesNotContain("  ", css); // 没有多个连续空格
        Assert.True(result.IsMinified);
    }

    [Fact]
    public async Task CompileAsync_Minify选项应该压缩输出()
    {
        // Arrange
        var scss = @"
body {
    color: red;
    margin: 0;
}
";
        var file = CreateScssFile(scss);
        var options = new SassOptions { Minify = true };

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        Assert.True(result.IsMinified);
    }

    #endregion

    #region Source Map 测试

    [Fact]
    public async Task CompileAsync_应该生成SourceMap()
    {
        // Arrange
        var scss = "body { color: red; }";
        var file = CreateScssFile(scss, "styles/main.scss");
        var options = new SassOptions { GenerateSourceMap = true };

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        Assert.Contains("sourceMappingURL=", css);
        Assert.Contains("main.css.map", css);
    }

    [Fact]
    public async Task CompileAsync_不生成SourceMap时不应包含注释()
    {
        // Arrange
        var scss = "body { color: red; }";
        var file = CreateScssFile(scss);
        var options = new SassOptions { GenerateSourceMap = false };

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        Assert.DoesNotContain("sourceMappingURL=", css);
    }

    #endregion

    #region 输出路径测试

    [Fact]
    public async Task CompileAsync_应该将scss扩展名改为css()
    {
        // Arrange
        var file = CreateScssFile("body { color: red; }", "styles/main.scss");
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        Assert.EndsWith(".css", result.OutputPath);
        Assert.Contains("main.css", result.OutputPath);
    }

    [Fact]
    public async Task CompileAsync_应该将sass扩展名改为css()
    {
        // Arrange
        var sass = @"body
  color: red
  margin: 0
";
        var file = CreateSassFile(sass, "styles/main.sass");
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        Assert.EndsWith(".css", result.OutputPath);
    }

    [Fact]
    public async Task CompileAsync_应该生成带指纹的路径()
    {
        // Arrange
        var file = CreateScssFile("body { color: red; }", "styles/main.scss");
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        Assert.NotNull(result.FingerprintedPath);
        Assert.Contains("main.", result.FingerprintedPath);
        Assert.EndsWith(".css", result.FingerprintedPath);
    }

    #endregion

    #region 哈希测试

    [Fact]
    public async Task CompileAsync_应该生成内容哈希()
    {
        // Arrange
        var file = CreateScssFile("body { color: red; }");
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        Assert.NotNull(result.ContentHash);
        Assert.Equal(64, result.ContentHash.Length); // SHA256 = 64 hex chars
    }

    [Fact]
    public async Task CompileAsync_应该生成SRI哈希()
    {
        // Arrange
        var file = CreateScssFile("body { color: red; }");
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        Assert.NotNull(result.Integrity);
        Assert.StartsWith("sha256-", result.Integrity);
    }

    [Fact]
    public async Task CompileAsync_相同输入应该产生相同哈希()
    {
        // Arrange
        var scss = "body { color: blue; }";
        var file1 = CreateScssFile(scss);
        var file2 = CreateScssFile(scss);
        var options = new SassOptions();

        // Act
        var result1 = await _compiler.CompileAsync(file1, options);
        var result2 = await _compiler.CompileAsync(file2, options);

        // Assert
        Assert.Equal(result1.ContentHash, result2.ContentHash);
    }

    [Fact]
    public async Task CompileAsync_不同输入应该产生不同哈希()
    {
        // Arrange
        var file1 = CreateScssFile("body { color: red; }");
        var file2 = CreateScssFile("body { color: blue; }");
        var options = new SassOptions();

        // Act
        var result1 = await _compiler.CompileAsync(file1, options);
        var result2 = await _compiler.CompileAsync(file2, options);

        // Assert
        Assert.NotEqual(result1.ContentHash, result2.ContentHash);
    }

    #endregion

    #region 取消操作测试

    [Fact]
    public async Task CompileAsync_支持取消操作()
    {
        // Arrange
        var file = CreateScssFile("body { color: red; }");
        var options = new SassOptions();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _compiler.CompileAsync(file, options, cts.Token).AsTask());
    }

    #endregion

    #region 复杂场景测试

    [Fact]
    public async Task CompileAsync_应该处理复杂的SCSS()
    {
        // Arrange
        var scss = @"
// 变量定义
$font-stack: Helvetica, sans-serif;
$primary-color: #333;
$secondary-color: #666;
$border-radius: 4px;

// Mixin 定义
@mixin flex-center {
    display: flex;
    justify-content: center;
    align-items: center;
}

@mixin button-style($bg-color, $text-color: white) {
    background-color: $bg-color;
    color: $text-color;
    padding: 10px 20px;
    border: none;
    border-radius: $border-radius;
    cursor: pointer;
    
    &:hover {
        opacity: 0.9;
    }
}

// 基础样式
body {
    font: 100% $font-stack;
    color: $primary-color;
}

// 容器
.container {
    @include flex-center;
    max-width: 1200px;
    margin: 0 auto;
}

// 按钮
.btn {
    &-primary {
        @include button-style($primary-color);
    }
    
    &-secondary {
        @include button-style($secondary-color);
    }
}

// 媒体查询
@media (max-width: 768px) {
    .container {
        padding: 0 15px;
    }
}
";
        var file = CreateScssFile(scss);
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        Assert.Contains("font:", css);
        Assert.Contains("display: flex", css);
        Assert.Contains(".btn-primary", css);
        Assert.Contains(".btn-secondary", css);
        Assert.Contains("@media", css);
    }

    [Fact]
    public async Task CompileAsync_应该处理函数()
    {
        // Arrange
        var scss = @"
@use 'sass:math';

.container {
    width: math.div(100%, 3);
}
";
        var file = CreateScssFile(scss);
        var options = new SassOptions();

        // Act
        var result = await _compiler.CompileAsync(file, options);

        // Assert
        var css = GetCssContent(result);
        Assert.Contains("width:", css);
        Assert.Contains("33.333", css);
    }

    #endregion

    #region 资源释放测试

    [Fact]
    public void Dispose_多次调用不应抛出异常()
    {
        // Arrange
        var compiler = new SassCompiler();

        // Act & Assert
        compiler.Dispose();
        compiler.Dispose(); // 第二次调用不应抛出异常
    }

    [Fact]
    public async Task CompileAsync_释放后调用应该抛出异常()
    {
        // Arrange
        var compiler = new SassCompiler();
        var file = CreateScssFile("body { color: red; }");
        var options = new SassOptions();

        // 先编译一次以初始化
        await compiler.CompileAsync(file, options);

        // 释放
        compiler.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => compiler.CompileAsync(file, options).AsTask());
    }

    #endregion
}
