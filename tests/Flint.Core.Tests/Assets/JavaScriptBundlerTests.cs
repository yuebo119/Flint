// Flint 静态站点生成器
// JavaScript 打包器单元测试

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Models;
using Xunit;

namespace Flint.Core.Tests.Assets;

/// <summary>
/// JavaScript 打包器单元测试
/// </summary>
public class JavaScriptBundlerTests
{
    private readonly JavaScriptBundler _bundler;

    public JavaScriptBundlerTests()
    {
        _bundler = new JavaScriptBundler();
    }

    #region 辅助方法

    /// <summary>
    /// 创建测试用的 JavaScript 文件
    /// </summary>
    private static AssetFile CreateJsFile(string content, string path = "test.js")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new AssetFile
        {
            SourcePath = path,
            MediaType = "application/javascript",
            Content = bytes,
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// 获取打包结果的 JavaScript 内容
    /// </summary>
    private static string GetJsContent(ProcessedAsset result)
    {
        return Encoding.UTF8.GetString(result.Content.Span);
    }

    #endregion

    #region 基本打包测试

    [Fact]
    public async Task BundleAsync_应该合并多个文件()
    {
        // Arrange
        var file1 = CreateJsFile("const a = 1;", "a.js");
        var file2 = CreateJsFile("const b = 2;", "b.js");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.BundleAsync([file1, file2], options);

        // Assert
        Assert.NotNull(result);
        var content = GetJsContent(result);
        Assert.Contains("const a = 1", content);
        Assert.Contains("const b = 2", content);
    }

    [Fact]
    public async Task BundleAsync_空文件列表应该抛出异常()
    {
        // Arrange
        var options = new BundleOptions();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _bundler.BundleAsync([], options).AsTask());
    }

    [Fact]
    public async Task BundleAsync_应该生成正确的媒体类型()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        Assert.Equal("application/javascript", result.MediaType);
    }

    #endregion

    #region 转译测试

    [Fact]
    public async Task TranspileAsync_应该处理单个文件()
    {
        // Arrange
        var file = CreateJsFile("const x = 1; const y = 2;");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.TranspileAsync(file, options);

        // Assert
        Assert.NotNull(result);
        var content = GetJsContent(result);
        Assert.Contains("const x = 1", content);
    }

    [Fact]
    public async Task TranspileAsync_应该保留源路径()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;", "scripts/main.js");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.TranspileAsync(file, options);

        // Assert
        Assert.Equal("scripts/main.js", result.SourcePath);
    }

    #endregion

    #region 压缩测试

    [Fact]
    public async Task BundleAsync_Minify应该压缩输出()
    {
        // Arrange
        var file = CreateJsFile(@"
// 这是注释
const   x   =   1;
const   y   =   2;
");
        var options = new BundleOptions { Minify = true };

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        Assert.True(result.IsMinified);
        var content = GetJsContent(result);
        // 压缩后应该没有多余空格
        Assert.DoesNotContain("   ", content);
    }

    [Fact]
    public async Task TranspileAsync_Minify应该移除注释()
    {
        // Arrange
        var file = CreateJsFile(@"
// 单行注释
const x = 1;
/* 多行
   注释 */
const y = 2;
");
        var options = new BundleOptions { Minify = true };

        // Act
        var result = await _bundler.TranspileAsync(file, options);

        // Assert
        var content = GetJsContent(result);
        Assert.DoesNotContain("单行注释", content);
        Assert.DoesNotContain("多行", content);
    }

    #endregion

    #region 输出格式测试

    [Fact]
    public async Task BundleAsync_IIFE格式应该包装代码()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;");
        var options = new BundleOptions { Format = JsOutputFormat.Iife };

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        var content = GetJsContent(result);
        Assert.Contains("(function()", content);
        Assert.Contains("})();", content);
    }

    #endregion

    #region 哈希测试

    [Fact]
    public async Task BundleAsync_应该生成内容哈希()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        Assert.NotNull(result.ContentHash);
        Assert.Equal(64, result.ContentHash.Length); // SHA256 = 64 hex chars
    }

    [Fact]
    public async Task BundleAsync_应该生成SRI哈希()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        Assert.NotNull(result.Integrity);
        Assert.StartsWith("sha256-", result.Integrity);
    }

    [Fact]
    public async Task BundleAsync_相同输入应该产生相同哈希()
    {
        // Arrange
        var file1 = CreateJsFile("const x = 1;");
        var file2 = CreateJsFile("const x = 1;");
        var options = new BundleOptions();

        // Act
        var result1 = await _bundler.BundleAsync([file1], options);
        var result2 = await _bundler.BundleAsync([file2], options);

        // Assert
        Assert.Equal(result1.ContentHash, result2.ContentHash);
    }

    [Fact]
    public async Task BundleAsync_不同输入应该产生不同哈希()
    {
        // Arrange
        var file1 = CreateJsFile("const x = 1;");
        var file2 = CreateJsFile("const x = 2;");
        var options = new BundleOptions();

        // Act
        var result1 = await _bundler.BundleAsync([file1], options);
        var result2 = await _bundler.BundleAsync([file2], options);

        // Assert
        Assert.NotEqual(result1.ContentHash, result2.ContentHash);
    }

    #endregion

    #region 输出路径测试

    [Fact]
    public async Task BundleAsync_应该生成正确的输出路径()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;", "scripts/main.js");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        Assert.EndsWith(".js", result.OutputPath);
        Assert.Contains("main", result.OutputPath);
    }

    [Fact]
    public async Task BundleAsync_应该生成带指纹的路径()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;", "scripts/main.js");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        Assert.NotNull(result.FingerprintedPath);
        Assert.Contains("main.", result.FingerprintedPath);
        Assert.EndsWith(".js", result.FingerprintedPath);
    }

    #endregion

    #region 取消操作测试

    [Fact]
    public async Task BundleAsync_支持取消操作()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;");
        var options = new BundleOptions();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _bundler.BundleAsync([file], options, cts.Token).AsTask());
    }

    [Fact]
    public async Task TranspileAsync_支持取消操作()
    {
        // Arrange
        var file = CreateJsFile("const x = 1;");
        var options = new BundleOptions();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _bundler.TranspileAsync(file, options, cts.Token).AsTask());
    }

    #endregion

    #region 复杂场景测试

    [Fact]
    public async Task BundleAsync_应该处理复杂的JavaScript()
    {
        // Arrange
        var js = @"
// 模块导入
import { helper } from './helper.js';

// 类定义
class Calculator {
    constructor() {
        this.result = 0;
    }

    add(value) {
        this.result += value;
        return this;
    }

    subtract(value) {
        this.result -= value;
        return this;
    }

    getResult() {
        return this.result;
    }
}

// 箭头函数
const multiply = (a, b) => a * b;

// 模板字符串
const greeting = (name) => `Hello, ${name}!`;

// 解构赋值
const { x, y } = { x: 1, y: 2 };

// 展开运算符
const arr = [1, 2, 3];
const newArr = [...arr, 4, 5];

// 异步函数
async function fetchData() {
    const response = await fetch('/api/data');
    return response.json();
}

export { Calculator, multiply, greeting };
";
        var file = CreateJsFile(js, "app.js");
        var options = new BundleOptions();

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        var content = GetJsContent(result);
        Assert.Contains("class Calculator", content);
        Assert.Contains("async function", content);
    }

    [Fact]
    public async Task BundleAsync_应该保留字符串内容()
    {
        // Arrange
        var js = @"
const str1 = 'Hello World';
const str2 = ""Double quotes"";
const str3 = `Template literal`;
const str4 = 'String with // comment inside';
const str5 = ""String with /* comment */ inside"";
";
        var file = CreateJsFile(js);
        var options = new BundleOptions { Minify = true };

        // Act
        var result = await _bundler.BundleAsync([file], options);

        // Assert
        var content = GetJsContent(result);
        Assert.Contains("Hello World", content);
        Assert.Contains("Double quotes", content);
        Assert.Contains("Template literal", content);
        Assert.Contains("// comment inside", content);
        Assert.Contains("/* comment */", content);
    }

    #endregion
}
