// Flint 静态站点生成器
// 磁盘资源缓存测试

using Flint.Core.Assets;
using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Assets;

public sealed class DiskAssetCacheTests : IDisposable
{
    private readonly string _tempDir;

    public DiskAssetCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint-cache-tests-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
        }
    }

    private static ProcessedAsset CreateAsset(string content = "body { color: red; }") => new()
    {
        OutputPath = "public/assets/main.css",
        MediaType = "text/css",
        Content = System.Text.Encoding.UTF8.GetBytes(content),
        ContentHash = "abc123",
        Integrity = "sha256-xyz",
        FingerprintedPath = "public/assets/main.abc123.css",
        SourcePath = "assets/main.scss",
        IsMinified = true,
        OriginalSize = content.Length
    };

    [Fact]
    public async Task SetGet_RoundTrip_PreservesAllMetadata()
    {
        var cache = new DiskAssetCache(_tempDir);
        var original = CreateAsset();

        await cache.SetAsync("key1", original);
        var restored = await cache.GetAsync<ProcessedAsset>("key1");

        restored.Should().NotBeNull();
        restored!.OutputPath.Should().Be(original.OutputPath);
        restored.MediaType.Should().Be(original.MediaType);
        restored.ContentHash.Should().Be(original.ContentHash);
        restored.Integrity.Should().Be(original.Integrity);
        restored.FingerprintedPath.Should().Be(original.FingerprintedPath);
        restored.SourcePath.Should().Be(original.SourcePath);
        restored.IsMinified.Should().BeTrue();
        restored.OriginalSize.Should().Be(original.OriginalSize);
        System.Text.Encoding.UTF8.GetString(restored.Content.Span)
            .Should().Be(System.Text.Encoding.UTF8.GetString(original.Content.Span));
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsNull()
    {
        var cache = new DiskAssetCache(_tempDir);
        (await cache.GetAsync<ProcessedAsset>("nonexistent")).Should().BeNull();
    }

    [Fact]
    public async Task MaxAge_ExpiredEntry_TreatedAsMissAndRemoved()
    {
        var cache = new DiskAssetCache(_tempDir, maxAge: TimeSpan.FromMilliseconds(50));
        await cache.SetAsync("k", CreateAsset());

        await Task.Delay(120);

        (await cache.GetAsync<ProcessedAsset>("k")).Should().BeNull("过期条目应视为未命中");
        (await cache.ContainsAsync("k")).Should().BeFalse("过期条目应被删除");
    }

    [Fact]
    public async Task CorruptedEntry_TreatedAsMiss_NotException()
    {
        var cache = new DiskAssetCache(_tempDir);
        await cache.SetAsync("k", CreateAsset());

        // 破坏元数据文件
        var files = Directory.GetFiles(_tempDir, "*.json", SearchOption.AllDirectories);
        files.Should().HaveCount(1);
        await File.WriteAllTextAsync(files[0], "{ not valid json !!!");

        (await cache.GetAsync<ProcessedAsset>("k")).Should().BeNull("损坏条目应降级为未命中");
    }

    [Fact]
    public void Prune_RemovesOnlyExpiredEntries()
    {
        var fresh = new DiskAssetCache(Path.Combine(_tempDir, "fresh"), maxAge: TimeSpan.FromHours(1));
        var stale = new DiskAssetCache(Path.Combine(_tempDir, "stale"), maxAge: TimeSpan.FromMilliseconds(1));
        fresh.SetAsync("fresh-key", CreateAsset()).AsTask().GetAwaiter().GetResult();
        stale.SetAsync("stale-key", CreateAsset()).AsTask().GetAwaiter().GetResult();
        Thread.Sleep(50);

        stale.Prune().Should().Be(1, "过期条目应被清理");
        fresh.Prune().Should().Be(0, "未过期条目不应被清理");
    }

    [Fact]
    public async Task Statistics_TrackHitsAndMisses()
    {
        var cache = new DiskAssetCache(_tempDir);
        await cache.SetAsync("k", CreateAsset());
        await cache.GetAsync<ProcessedAsset>("k"); // hit
        await cache.GetAsync<ProcessedAsset>("missing"); // miss

        var stats = cache.GetStatistics();
        stats.HitCount.Should().Be(1);
        stats.MissCount.Should().Be(1);
    }

    [Fact]
    public async Task Pipeline_SecondProcess_HitsDiskCache()
    {
        // 跨实例验证：两个独立 AssetPipeline 共享磁盘缓存（模拟二次构建）
        var cacheDir = Path.Combine(_tempDir, "shared");
        var asset = new AssetFile
        {
            SourcePath = "assets/style.css",
            MediaType = "text/css",
            Content = System.Text.Encoding.UTF8.GetBytes("body { color: blue; }"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var cache = new DiskAssetCache(cacheDir);
        var first = new AssetPipeline(new AssetPipelineOptions { MinifyCss = true }, cache: cache);
        var processed1 = await first.ProcessAsync(asset);
        first.ClearMemoryCache();

        var second = new AssetPipeline(new AssetPipelineOptions { MinifyCss = true }, cache: cache);
        var processed2 = await second.ProcessAsync(asset);

        processed2.ContentHash.Should().Be(processed1.ContentHash, "相同内容+选项应命中磁盘缓存");
        System.Text.Encoding.UTF8.GetString(processed2.Content.Span)
            .Should().Be(System.Text.Encoding.UTF8.GetString(processed1.Content.Span));
        cache.GetStatistics().HitCount.Should().BeGreaterThan(0, "第二次处理应命中磁盘缓存");
    }

    [Fact]
    public async Task Pipeline_DifferentOptions_DoesNotShareCacheEntry()
    {
        var cacheDir = Path.Combine(_tempDir, "opt-sep");
        var asset = new AssetFile
        {
            SourcePath = "assets/style.css",
            MediaType = "text/css",
            Content = System.Text.Encoding.UTF8.GetBytes("body { color: blue; }"),
            ModifiedTime = DateTimeOffset.UtcNow
        };

        var minify = new AssetPipeline(new AssetPipelineOptions { MinifyCss = true }, cache: new DiskAssetCache(cacheDir));
        var noMinify = new AssetPipeline(new AssetPipelineOptions { MinifyCss = false }, cache: new DiskAssetCache(cacheDir));

        var minified = await minify.ProcessAsync(asset);
        var plain = await noMinify.ProcessAsync(asset);

        minified.IsMinified.Should().BeTrue();
        plain.IsMinified.Should().BeFalse("不同选项不得互相命中缓存");
    }
}
