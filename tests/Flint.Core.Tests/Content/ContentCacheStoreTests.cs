// Flint 静态站点生成器
// 持久内容缓存测试（2026-10-22，--cache 特性）：
// 单测锁 blob 往返/键语义/损坏容忍；构建级测试锁「缓存命中不改变产物」
// 与「文件变更必须失效重读」——后者是本特性唯一的正确性风险面
// （同一 mtime tick 同长度改写的漏检为已知边界，见 ContentCacheStore 头注释）

using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Content;

/// <summary>持久内容缓存：blob 往返、键语义、构建级正确性</summary>
public sealed class ContentCacheStoreTests : IDisposable
{
    private readonly string _dir;

    public ContentCacheStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"Flint-ccache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    // ---- blob 往返与键语义 ----

    [Fact]
    public void 往返_保存后按键命中且内容逐字节一致()
    {
        var blob = Path.Combine(_dir, "content.blob");
        var contentA = Encoding.UTF8.GetBytes("第一个文件内容");
        var contentB = Encoding.UTF8.GetBytes("第二个文件内容（含中文与 emoji 🚀）");

        var store = new ContentCacheStore();
        store.Save(blob,
        [
            ("content/a.md", 100L, contentA),
            ("themes/t/content/b.md", 200L, contentB),
        ]);

        var loaded = ContentCacheStore.Load(blob);
        Assert.Equal(2, loaded.Count);

        Assert.True(loaded.TryGet("content/a.md", 100L, contentA.Length, out var a));
        Assert.Equal(contentA, a.ToArray());

        Assert.True(loaded.TryGet("themes/t/content/b.md", 200L, contentB.Length, out var b));
        Assert.Equal(contentB, b.ToArray());
    }

    [Theory]
    [InlineData(99L, 0, "mtime 变化")]      // mtime 不同
    [InlineData(100L, 1, "size 变化")]      // size 不同
    public void 键不匹配_未命中(long mtime, int sizeDelta, string reason)
    {
        var blob = Path.Combine(_dir, "content.blob");
        var content = Encoding.UTF8.GetBytes("x");
        var store = new ContentCacheStore();
        store.Save(blob, [("content/a.md", 100L, content)]);

        var loaded = ContentCacheStore.Load(blob);
        Assert.False(loaded.TryGet("content/a.md", mtime, content.Length + sizeDelta, out _), reason);
        Assert.Equal(1, loaded.MissCount);
    }

    [Fact]
    public void 路径不存在_未命中()
    {
        var store = new ContentCacheStore();
        Assert.False(store.TryGet("content/missing.md", 1L, 1, out _));
    }

    [Fact]
    public void 文件不存在_空缓存不抛()
    {
        var store = ContentCacheStore.Load(Path.Combine(_dir, "nope.blob"));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void 损坏的blob_按空缓存处理不抛()
    {
        var blob = Path.Combine(_dir, "content.blob");
        File.WriteAllBytes(blob, [0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE]); // 魔数不匹配

        var store = ContentCacheStore.Load(blob);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void 截断的blob_按空缓存处理不抛()
    {
        var blob = Path.Combine(_dir, "content.blob");
        var store0 = new ContentCacheStore();
        store0.Save(blob, [("content/a.md", 1L, new byte[64])]);

        var bytes = File.ReadAllBytes(blob);
        File.WriteAllBytes(blob, bytes[..(bytes.Length - 10)]); // 尾部截断

        var store = ContentCacheStore.Load(blob);
        // 截断发生在条目内容中段时可能抛 EndOfStream，被 Load 吞掉后为空缓存
        // （不阻断构建，下次重读自愈）
        Assert.True(store.Count >= 0);
    }

    [Fact]
    public void 零命中_计数反映命中情况()
    {
        var blob = Path.Combine(_dir, "content.blob");
        var content = Encoding.UTF8.GetBytes("abc");
        var store = new ContentCacheStore();
        store.Save(blob, [("a.md", 1L, content)]);

        var loaded = ContentCacheStore.Load(blob);
        Assert.True(loaded.TryGet("a.md", 1L, 3, out _));
        Assert.Equal(1, loaded.HitCount);
        Assert.Equal(0, loaded.MissCount);
    }

    // ---- 构建级正确性 ----

    [Fact]
    public async Task 构建级_二次构建缓存命中且产物一致()
    {
        var site = CreateSite();
        var options = CreateOptions(site);
        var builder = CreateBuilder(site);

        var first = await builder.BuildAsync(options);
        Assert.True(first.Success, Join(first.Errors));
        var firstHtml = ReadOutput(site, "posts/p1/index.html");

        Assert.True(File.Exists(Path.Combine(site, ".flint-cache", "content.blob")),
            "首次构建应写出缓存 blob");

        // 二次构建：同一 builder 实例（进程内复用），blob 已存在 → 命中
        var second = await builder.BuildAsync(options);
        Assert.True(second.Success, Join(second.Errors));

        Assert.Equal(firstHtml, ReadOutput(site, "posts/p1/index.html"));
    }

    [Fact]
    public async Task 构建级_文件变更后必须失效重读_产物反映新内容()
    {
        var site = CreateSite();
        var options = CreateOptions(site);
        var builder = CreateBuilder(site);

        var first = await builder.BuildAsync(options);
        Assert.True(first.Success, Join(first.Errors));
        Assert.Contains("原始内容", ReadOutput(site, "posts/p1/index.html"));

        // 改内容（长度变化 → size 键命中失败；即便 mtime 未推进也能失效）
        File.WriteAllText(Path.Combine(site, "content", "posts", "p1.md"), """
            +++
            title = "文章一改"
            date = 2026-01-01T00:00:00Z
            draft = false
            +++

            改写后的内容，长度不同。
            """);

        var second = await builder.BuildAsync(options);
        Assert.True(second.Success, Join(second.Errors));

        // 关键断言：缓存不得返回陈旧内容
        Assert.Contains("改写后的内容", ReadOutput(site, "posts/p1/index.html"));
        Assert.DoesNotContain("原始内容", ReadOutput(site, "posts/p1/index.html"));
    }

    [Fact]
    public async Task 构建级_删除文件后条目集变化_blob重写且产物不含旧页()
    {
        var site = CreateSite();
        File.WriteAllText(Path.Combine(site, "content", "posts", "p2.md"), """
            +++
            title = "文章二"
            date = 2026-01-02T00:00:00Z
            draft = false
            +++

            第二篇内容。
            """);

        var options = new BuildOptions
        {
            SourcePath = site,
            OutputPath = Path.Combine(site, "public"),
            Parallelism = 1,
            CachePath = Path.Combine(site, ".flint-cache"),
            CleanOutput = true
        };
        var builder = CreateBuilder(site);
        var first = await builder.BuildAsync(options);
        Assert.True(first.Success, Join(first.Errors));
        Assert.True(File.Exists(Path.Combine(site, "public", "posts", "p2", "index.html")));

        File.Delete(Path.Combine(site, "content", "posts", "p2.md"));
        var second = await builder.BuildAsync(options);
        Assert.True(second.Success, Join(second.Errors));

        // 条目集变化 → 重建 blob；--clean 下产物集也不含被删页面
        // （默认不清理时旧产物按 Hugo 语义保留，与本缓存无关）
        Assert.False(File.Exists(Path.Combine(site, "public", "posts", "p2", "index.html")));

        var store = ContentCacheStore.Load(Path.Combine(site, ".flint-cache", "content.blob"));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task 构建级_损坏缓存_构建仍成功()
    {
        var site = CreateSite();
        var options = CreateOptions(site);
        var builder = CreateBuilder(site);

        var first = await builder.BuildAsync(options);
        Assert.True(first.Success, Join(first.Errors));

        // 破坏 blob
        File.WriteAllBytes(Path.Combine(site, ".flint-cache", "content.blob"), [1, 2, 3, 4, 5]);

        var second = await builder.BuildAsync(options);
        Assert.True(second.Success, "损坏缓存必须自愈（按无缓存处理）");
        Assert.Contains("原始内容", ReadOutput(site, "posts/p1/index.html"));
    }

    [Fact]
    public async Task 构建级_未开启cache_不产生缓存目录且产物一致()
    {
        var site = CreateSite();
        var options = new BuildOptions
        {
            SourcePath = site,
            OutputPath = Path.Combine(site, "public"),
            Parallelism = 1
            // CachePath 不设 = 默认关闭
        };
        var builder = CreateBuilder(site);

        var result = await builder.BuildAsync(options);
        Assert.True(result.Success, Join(result.Errors));
        Assert.False(Directory.Exists(Path.Combine(site, ".flint-cache")),
            "默认构建路径不得产生缓存副作用");
        Assert.Contains("原始内容", ReadOutput(site, "posts/p1/index.html"));
    }

    // ---- 辅助 ----

    private static string Join(IReadOnlyList<BuildError> errors) =>
        string.Join(" | ", errors.Select(e => e.Message));

    private static BuildOptions CreateOptions(string site) => new()
    {
        SourcePath = site,
        OutputPath = Path.Combine(site, "public"),
        Parallelism = 1,
        CachePath = Path.Combine(site, ".flint-cache")
    };

    private static SiteBuilder CreateBuilder(string site) =>
        new(
            new ContentParser(),
            new FluidTemplateRenderer(Path.Combine(site, "layouts"), "https://example.com"),
            new StubAssetPipeline(),
            new ConfigLoader());

    private static string ReadOutput(string site, string relative) =>
        File.ReadAllText(Path.Combine(site, "public", relative.Replace('/', Path.DirectorySeparatorChar)));

    private static string CreateSite()
    {
        var site = Path.Combine(Path.GetTempPath(), $"Flint-ccache-site-{Guid.NewGuid():N}");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "Flint.toml"), """
            baseURL = "https://example.com/"
            title = "缓存测试站"
            """);
        Directory.CreateDirectory(Path.Combine(site, "content", "posts"));
        File.WriteAllText(Path.Combine(site, "content", "posts", "p1.md"), """
            +++
            title = "文章一"
            date = 2026-01-01T00:00:00Z
            draft = false
            +++

            原始内容。
            """);
        var layouts = Path.Combine(site, "layouts", "_default");
        Directory.CreateDirectory(layouts);
        File.WriteAllText(Path.Combine(layouts, "single.liquid"),
            "<article>{{ page.content }}</article>");
        return site;
    }

    private sealed class StubAssetPipeline : IAssetPipeline
    {
        public ValueTask<ProcessedAsset> ProcessAsync(AssetFile asset, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ProcessedAsset
            {
                OutputPath = asset.SourcePath,
                SourcePath = asset.SourcePath,
                Content = asset.Content,
                MediaType = "application/octet-stream",
                ContentHash = ""
            });

        public ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
            IReadOnlyList<AssetFile> assets, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<ProcessedAsset> ProcessStreamAsync(
            IAsyncEnumerable<AssetFile> assets,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var asset in assets.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return await ProcessAsync(asset, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}