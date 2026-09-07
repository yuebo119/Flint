// Flint 静态站点生成器
// 资源管道单元测试

using System.Text;
using Flint.Core.Assets;
using Flint.Core.Models;
using Xunit;

namespace Flint.Core.Tests.Assets;

/// <summary>
/// AssetPipeline 单元测试
/// </summary>
public class AssetPipelineTests
{
    #region 基本处理测试

    [Fact]
    public async Task ProcessAsync_WithCssFile_ReturnsProcessedAsset()
    {
        // Arrange
        var pipeline = new AssetPipeline();
        var cssContent = "body { color: red; }";
        var asset = CreateAsset("styles/main.css", "text/css", cssContent);

        // Act
        var result = await pipeline.ProcessAsync(asset);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("styles/main.css", result.SourcePath);
        Assert.Equal("text/css", result.MediaType);
        Assert.NotEmpty(result.ContentHash);
        Assert.NotNull(result.Integrity);
        Assert.StartsWith("sha256-", result.Integrity);
    }

    [Fact]
    public async Task ProcessAsync_WithJsFile_ReturnsProcessedAsset()
    {
        // Arrange
        var pipeline = new AssetPipeline(new AssetPipelineOptions { ProcessScripts = false });
        var jsContent = "console.log('hello');";
        var asset = CreateAsset("scripts/app.js", "application/javascript", jsContent);

        // Act
        var result = await pipeline.ProcessAsync(asset);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("scripts/app.js", result.SourcePath);
        Assert.NotEmpty(result.ContentHash);
    }


    [Fact]
    public async Task ProcessAsync_WithUnknownType_ReturnsPassthroughAsset()
    {
        // Arrange
        var pipeline = new AssetPipeline();
        var content = "some data";
        var asset = CreateAsset("data/file.xyz", "application/octet-stream", content);

        // Act
        var result = await pipeline.ProcessAsync(asset);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(content.Length, result.Content.Length);
    }

    #endregion

    #region 缓存测试

    [Fact]
    public async Task ProcessAsync_WithMemoryCache_ReturnsCachedResult()
    {
        // Arrange
        var pipeline = new AssetPipeline(new AssetPipelineOptions { EnableMemoryCache = true });
        var content = "cached content";
        var asset = CreateAsset("test.css", "text/css", content);

        // Act - 处理两次
        var result1 = await pipeline.ProcessAsync(asset);
        var result2 = await pipeline.ProcessAsync(asset);

        // Assert - 应该返回相同的结果
        Assert.Equal(result1.ContentHash, result2.ContentHash);
        Assert.Equal(1, pipeline.MemoryCacheCount);
    }

    [Fact]
    public async Task ProcessAsync_WithDifferentContent_ProducesDifferentHash()
    {
        // Arrange
        var pipeline = new AssetPipeline();
        var asset1 = CreateAsset("test.css", "text/css", "content1");
        var asset2 = CreateAsset("test.css", "text/css", "content2");

        // Act
        var result1 = await pipeline.ProcessAsync(asset1);
        var result2 = await pipeline.ProcessAsync(asset2);

        // Assert
        Assert.NotEqual(result1.ContentHash, result2.ContentHash);
    }

    [Fact]
    public void ClearMemoryCache_ClearsAllCachedItems()
    {
        // Arrange
        var pipeline = new AssetPipeline(new AssetPipelineOptions { EnableMemoryCache = true });
        var asset = CreateAsset("test.css", "text/css", "content");
        _ = pipeline.ProcessAsync(asset).AsTask().Result;
        Assert.Equal(1, pipeline.MemoryCacheCount);

        // Act
        pipeline.ClearMemoryCache();

        // Assert
        Assert.Equal(0, pipeline.MemoryCacheCount);
    }

    #endregion

    #region 批量处理测试

    [Fact]
    public async Task ProcessBatchAsync_WithEmptyList_ReturnsEmptyList()
    {
        // Arrange
        var pipeline = new AssetPipeline();

        // Act
        var results = await pipeline.ProcessBatchAsync([]);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ProcessBatchAsync_WithMultipleAssets_ProcessesAll()
    {
        // Arrange
        var pipeline = new AssetPipeline();
        var assets = new[]
        {
            CreateAsset("file1.css", "text/css", "content1"),
            CreateAsset("file2.css", "text/css", "content2"),
            CreateAsset("file3.css", "text/css", "content3")
        };

        // Act
        var results = await pipeline.ProcessBatchAsync(assets);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.NotEmpty(r.ContentHash));
    }

    [Fact]
    public async Task ProcessBatchAsync_WithParallelism_ProcessesInParallel()
    {
        // Arrange
        var options = new AssetPipelineOptions { MaxParallelism = 4 };
        var pipeline = new AssetPipeline(options);
        var assets = Enumerable.Range(0, 10)
            .Select(i => CreateAsset($"file{i}.css", "text/css", $"content{i}"))
            .ToList();

        // Act
        var results = await pipeline.ProcessBatchAsync(assets);

        // Assert
        Assert.Equal(10, results.Count);
    }

    #endregion

    #region 流式处理测试

    [Fact]
    public async Task ProcessStreamAsync_WithAsyncEnumerable_ProcessesAll()
    {
        // Arrange
        var pipeline = new AssetPipeline();
        var assets = GenerateAssetsAsync(5);

        // Act
        var results = new List<ProcessedAsset>();
        await foreach (var result in pipeline.ProcessStreamAsync(assets))
        {
            results.Add(result);
        }

        // Assert
        Assert.Equal(5, results.Count);
    }

    private static async IAsyncEnumerable<AssetFile> GenerateAssetsAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return CreateAsset($"file{i}.css", "text/css", $"content{i}");
            await Task.Yield();
        }
    }

    #endregion

    #region 资源类型检测测试

    [Theory]
    [InlineData("image.png", AssetType.Image)]
    [InlineData("image.jpg", AssetType.Image)]
    [InlineData("image.webp", AssetType.Image)]
    [InlineData("style.css", AssetType.Style)]
    [InlineData("style.scss", AssetType.Style)]
    [InlineData("script.js", AssetType.Script)]
    [InlineData("script.ts", AssetType.Script)]
    [InlineData("font.woff2", AssetType.Font)]
    [InlineData("video.mp4", AssetType.Video)]
    [InlineData("audio.mp3", AssetType.Audio)]
    [InlineData("data.json", AssetType.Data)]
    [InlineData("unknown.xyz", AssetType.Other)]
    public void AssetFile_Type_DetectsCorrectly(string path, AssetType expectedType)
    {
        // Arrange
        var asset = CreateAsset(path, "application/octet-stream", "content");

        // Assert
        Assert.Equal(expectedType, asset.Type);
    }

    #endregion

    #region 输出路径测试

    [Fact]
    public async Task ProcessAsync_WithSourceDirectory_RemovesPrefix()
    {
        // Arrange
        var options = new AssetPipelineOptions
        {
            SourceDirectory = "assets",
            OutputDirectory = "public"
        };
        var pipeline = new AssetPipeline(options);
        var asset = CreateAsset("assets/styles/main.css", "text/css", "content");

        // Act
        var result = await pipeline.ProcessAsync(asset);

        // Assert
        Assert.StartsWith("public", result.OutputPath);
    }

    #endregion

    #region 指纹路径测试

    [Fact]
    public async Task ProcessAsync_GeneratesFingerprintedPath()
    {
        // Arrange
        var pipeline = new AssetPipeline();
        var asset = CreateAsset("styles/main.css", "text/css", "body { color: red; }");

        // Act
        var result = await pipeline.ProcessAsync(asset);

        // Assert
        Assert.NotNull(result.FingerprintedPath);
        Assert.Contains(".css", result.FingerprintedPath);
        Assert.NotEqual(asset.SourcePath, result.FingerprintedPath);
    }

    [Fact]
    public async Task ProcessAsync_SameContent_SameFingerprintedPath()
    {
        // Arrange
        var pipeline = new AssetPipeline();
        var content = "body { color: blue; }";
        var asset1 = CreateAsset("file1.css", "text/css", content);
        var asset2 = CreateAsset("file2.css", "text/css", content);

        // Act
        var result1 = await pipeline.ProcessAsync(asset1);
        var result2 = await pipeline.ProcessAsync(asset2);

        // Assert - 相同内容应该有相同的哈希部分
        Assert.Equal(result1.ContentHash, result2.ContentHash);
    }

    #endregion

    #region 辅助方法

    private static AssetFile CreateAsset(string path, string mediaType, string content)
    {
        return new AssetFile
        {
            SourcePath = path,
            MediaType = mediaType,
            Content = Encoding.UTF8.GetBytes(content),
            ModifiedTime = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
