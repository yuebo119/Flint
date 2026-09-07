// Flint 静态站点生成器
// 增量资源处理测试
// 测试未修改文件跳过处理、修改文件重新处理、依赖文件变化触发重新处理
// _Requirements: 6.10_

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Assets;

/// <summary>
/// 增量资源处理测试
/// 验证增量处理功能的正确性
/// _Requirements: 6.10_
/// </summary>
[Collection("AssetPipeline")]
public class IncrementalAssetProcessingTests : IDisposable
{
    private readonly SassCompiler _sassCompiler;
    private readonly JavaScriptBundler _jsBundler;

    public IncrementalAssetProcessingTests()
    {
        _sassCompiler = new SassCompiler();
        _jsBundler = new JavaScriptBundler();
    }

    public void Dispose()
    {
        _sassCompiler.Dispose();
        GC.SuppressFinalize(this);
    }

    #region 缓存命中测试

    /// <summary>
    /// 测试未修改文件使用缓存
    /// 验证相同内容的文件能从缓存中获取结果
    /// </summary>
    [Fact]
    public async Task ProcessAsset_SameContent_ShouldUseCacheOnSecondCall()
    {
        // Arrange
        var pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                EnableMemoryCache = true,
                CompileSass = true,
                ProcessScripts = true
            },
            null,
            _sassCompiler,
            _jsBundler);

        var scss = ".test { color: #007bff; }";
        var asset = CreateScssAsset("cached.scss", scss);

        // Act - 第一次处理
        var result1 = await pipeline.ProcessAsync(asset);
        var cacheCountAfterFirst = pipeline.MemoryCacheCount;

        // Act - 第二次处理相同内容
        var result2 = await pipeline.ProcessAsync(asset);
        var cacheCountAfterSecond = pipeline.MemoryCacheCount;

        // Assert
        result1.ContentHash.Should().Be(result2.ContentHash);
        // 缓存数量应该相同（第二次从缓存获取）
        cacheCountAfterSecond.Should().Be(cacheCountAfterFirst);
    }

    /// <summary>
    /// 测试修改文件重新处理
    /// 验证内容变化后会重新处理而不是使用缓存
    /// </summary>
    [Fact]
    public async Task ProcessAsset_ModifiedContent_ShouldReprocess()
    {
        // Arrange
        var pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                EnableMemoryCache = true,
                CompileSass = true,
                ProcessScripts = true
            },
            null,
            _sassCompiler,
            _jsBundler);

        var scss1 = ".test { color: #007bff; }";
        var scss2 = ".test { color: #dc3545; }"; // 修改了颜色

        var asset1 = CreateScssAsset("modified.scss", scss1);
        var asset2 = CreateScssAsset("modified.scss", scss2);

        // Act
        var result1 = await pipeline.ProcessAsync(asset1);
        var result2 = await pipeline.ProcessAsync(asset2);

        // Assert
        result1.ContentHash.Should().NotBe(result2.ContentHash);

        var css1 = Encoding.UTF8.GetString(result1.Content.Span);
        var css2 = Encoding.UTF8.GetString(result2.Content.Span);

        css1.Should().Contain("#007bff");
        css2.Should().Contain("#dc3545");
    }

    /// <summary>
    /// 测试缓存清除后重新处理
    /// 验证清除缓存后会重新处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_AfterCacheClear_ShouldReprocess()
    {
        // Arrange
        var pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                EnableMemoryCache = true,
                CompileSass = true
            },
            null,
            _sassCompiler,
            null);

        var scss = ".test { color: #007bff; }";
        var asset = CreateScssAsset("cache-clear.scss", scss);

        // Act - 第一次处理
        var result1 = await pipeline.ProcessAsync(asset);
        var cacheCountBefore = pipeline.MemoryCacheCount;

        // 清除缓存
        pipeline.ClearMemoryCache();
        var cacheCountAfterClear = pipeline.MemoryCacheCount;

        // 第二次处理
        var result2 = await pipeline.ProcessAsync(asset);
        var cacheCountAfterSecond = pipeline.MemoryCacheCount;

        // Assert
        cacheCountBefore.Should().BeGreaterThan(0);
        cacheCountAfterClear.Should().Be(0);
        cacheCountAfterSecond.Should().BeGreaterThan(0);
        result1.ContentHash.Should().Be(result2.ContentHash);
    }

    #endregion

    #region 批量处理缓存测试

    /// <summary>
    /// 测试批量处理中的缓存行为
    /// 验证批量处理中相同内容的文件能共享缓存
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WithDuplicateContent_ShouldShareCache()
    {
        // Arrange
        var pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                EnableMemoryCache = true,
                CompileSass = true,
                MaxParallelism = 1 // 串行处理以确保缓存行为可预测
            },
            null,
            _sassCompiler,
            null);

        var scss = ".test { color: #007bff; }";

        // 创建多个内容相同但路径不同的文件
        var assets = new List<AssetFile>
        {
            CreateScssAsset("file1.scss", scss),
            CreateScssAsset("file2.scss", scss),
            CreateScssAsset("file3.scss", scss)
        };

        // Act
        var results = await pipeline.ProcessBatchAsync(assets);

        // Assert
        results.Should().HaveCount(3);

        // 所有结果的内容哈希应该相同
        var hashes = results.Select(r => r.ContentHash).Distinct().ToList();
        hashes.Should().HaveCount(1);

        // 缓存中应该只有一个条目（因为内容相同）
        pipeline.MemoryCacheCount.Should().Be(1);
    }

    /// <summary>
    /// 测试批量处理中的增量更新
    /// 验证只有修改的文件会被重新处理
    /// </summary>
    [Fact]
    public async Task ProcessBatch_WithPartialModification_ShouldOnlyReprocessModified()
    {
        // Arrange
        var pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                EnableMemoryCache = true,
                CompileSass = true,
                MaxParallelism = 1
            },
            null,
            _sassCompiler,
            null);

        var scss1 = ".file1 { color: #007bff; }";
        var scss2 = ".file2 { color: #6c757d; }";
        var scss3 = ".file3 { color: #28a745; }";

        var assets1 = new List<AssetFile>
        {
            CreateScssAsset("file1.scss", scss1),
            CreateScssAsset("file2.scss", scss2),
            CreateScssAsset("file3.scss", scss3)
        };

        // 第一次处理
        var results1 = await pipeline.ProcessBatchAsync(assets1);
        var cacheCountAfterFirst = pipeline.MemoryCacheCount;

        // 修改其中一个文件
        var scss2Modified = ".file2 { color: #dc3545; }"; // 修改了颜色
        var assets2 = new List<AssetFile>
        {
            CreateScssAsset("file1.scss", scss1), // 未修改
            CreateScssAsset("file2.scss", scss2Modified), // 已修改
            CreateScssAsset("file3.scss", scss3) // 未修改
        };

        // 第二次处理
        var results2 = await pipeline.ProcessBatchAsync(assets2);
        var cacheCountAfterSecond = pipeline.MemoryCacheCount;

        // Assert
        results2.Should().HaveCount(3);

        // file1 和 file3 的哈希应该相同
        results1[0].ContentHash.Should().Be(results2[0].ContentHash);
        results1[2].ContentHash.Should().Be(results2[2].ContentHash);

        // file2 的哈希应该不同
        results1[1].ContentHash.Should().NotBe(results2[1].ContentHash);

        // 缓存应该增加了一个条目（修改后的 file2）
        cacheCountAfterSecond.Should().Be(cacheCountAfterFirst + 1);
    }

    #endregion

    #region 依赖文件变化测试

    /// <summary>
    /// 测试依赖文件变化触发重新处理
    /// 验证当导入的文件变化时，主文件会被重新处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_WithDependencyChange_ShouldReprocess()
    {
        // Arrange - 创建临时目录
        var tempDir = Path.Combine(Path.GetTempPath(), $"Flint_dep_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 创建被导入的变量文件
            var variablesV1 = "$primary: #007bff;";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "_variables.scss"), variablesV1);

            // 创建主文件
            var mainScss = "@import 'variables';\n.test { color: $primary; }";
            var mainPath = Path.Combine(tempDir, "main.scss");
            await File.WriteAllTextAsync(mainPath, mainScss);

            var asset1 = new AssetFile
            {
                SourcePath = mainPath,
                MediaType = "text/x-scss",
                Content = Encoding.UTF8.GetBytes(mainScss),
                ModifiedTime = DateTimeOffset.UtcNow
            };

            // 第一次编译
            var result1 = await _sassCompiler.CompileAsync(asset1, new SassOptions
            {
                IncludePaths = [tempDir]
            });

            // 修改依赖文件
            var variablesV2 = "$primary: #dc3545;"; // 修改颜色
            await File.WriteAllTextAsync(Path.Combine(tempDir, "_variables.scss"), variablesV2);

            // 重新读取主文件（内容相同，但依赖已变化）
            var asset2 = new AssetFile
            {
                SourcePath = mainPath,
                MediaType = "text/x-scss",
                Content = Encoding.UTF8.GetBytes(mainScss),
                ModifiedTime = DateTimeOffset.UtcNow.AddSeconds(1) // 更新时间戳
            };

            // 第二次编译
            var result2 = await _sassCompiler.CompileAsync(asset2, new SassOptions
            {
                IncludePaths = [tempDir]
            });

            // Assert
            var css1 = Encoding.UTF8.GetString(result1.Content.Span);
            var css2 = Encoding.UTF8.GetString(result2.Content.Span);

            css1.Should().Contain("#007bff");
            css2.Should().Contain("#dc3545");

            // 内容哈希应该不同
            result1.ContentHash.Should().NotBe(result2.ContentHash);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region 禁用缓存测试

    /// <summary>
    /// 测试禁用缓存时的行为
    /// 验证禁用缓存后每次都会重新处理
    /// </summary>
    [Fact]
    public async Task ProcessAsset_WithCacheDisabled_ShouldAlwaysReprocess()
    {
        // Arrange
        var pipeline = new AssetPipeline(
            new AssetPipelineOptions
            {
                EnableMemoryCache = false, // 禁用缓存
                CompileSass = true
            },
            null,
            _sassCompiler,
            null);

        var scss = ".test { color: #007bff; }";
        var asset = CreateScssAsset("no-cache.scss", scss);

        // Act
        var result1 = await pipeline.ProcessAsync(asset);
        var result2 = await pipeline.ProcessAsync(asset);

        // Assert
        result1.ContentHash.Should().Be(result2.ContentHash);
        // 缓存应该为空
        pipeline.MemoryCacheCount.Should().Be(0);
    }

    #endregion

    #region 内容哈希一致性测试

    /// <summary>
    /// 测试内容哈希的一致性
    /// 验证相同内容总是产生相同的哈希
    /// </summary>
    [Fact]
    public async Task ProcessAsset_SameContent_ShouldProduceSameHash()
    {
        // Arrange
        var pipeline1 = new AssetPipeline(
            new AssetPipelineOptions { EnableMemoryCache = false, CompileSass = true },
            null, _sassCompiler, null);

        var pipeline2 = new AssetPipeline(
            new AssetPipelineOptions { EnableMemoryCache = false, CompileSass = true },
            null, _sassCompiler, null);

        var scss = ".test { color: #007bff; padding: 10px; }";
        var asset1 = CreateScssAsset("hash1.scss", scss);
        var asset2 = CreateScssAsset("hash2.scss", scss); // 不同路径，相同内容

        // Act
        var result1 = await pipeline1.ProcessAsync(asset1);
        var result2 = await pipeline2.ProcessAsync(asset2);

        // Assert
        result1.ContentHash.Should().Be(result2.ContentHash);
    }

    /// <summary>
    /// 测试不同内容产生不同哈希
    /// 验证内容变化会导致哈希变化
    /// </summary>
    [Fact]
    public async Task ProcessAsset_DifferentContent_ShouldProduceDifferentHash()
    {
        // Arrange
        var pipeline = new AssetPipeline(
            new AssetPipelineOptions { EnableMemoryCache = false, CompileSass = true },
            null, _sassCompiler, null);

        var scss1 = ".test { color: #007bff; }";
        var scss2 = ".test { color: #007bfe; }"; // 只差一个字符

        var asset1 = CreateScssAsset("diff1.scss", scss1);
        var asset2 = CreateScssAsset("diff2.scss", scss2);

        // Act
        var result1 = await pipeline.ProcessAsync(asset1);
        var result2 = await pipeline.ProcessAsync(asset2);

        // Assert
        result1.ContentHash.Should().NotBe(result2.ContentHash);
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
