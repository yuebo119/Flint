// Flint 静态站点生成器
// Fluid 增量构建重渲测试（FLUID-MIGRATION-PLAN P1-3 实质化）：
// .liquid partial 变更必须触发依赖它的页面重渲染——此前增量路径的
// templateChanges 过滤只认 .html，改 .liquid partial 落入 assetChanges，
// 页面不重渲（2026-10-21 修复）。本测试锁定双形态行为。

using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>.liquid 模板变更的增量重渲回归</summary>
public sealed class FluidIncrementalTests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _outputDir;
    private readonly string _partialPath;

    public FluidIncrementalTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"Flint-fluid-inc-{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_siteDir, "public");
        Directory.CreateDirectory(_siteDir);
        CreateSite();
        _partialPath = Path.Combine(_siteDir, "layouts", "partials", "badge.liquid");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_siteDir))
            {
                Directory.Delete(_siteDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
        GC.SuppressFinalize(this);
    }

    private void CreateSite()
    {
        File.WriteAllText(Path.Combine(_siteDir, "Flint.toml"), """
            baseURL = "https://example.com/"
            title = "增量测试站"
            """);

        Directory.CreateDirectory(Path.Combine(_siteDir, "content", "posts"));
        File.WriteAllText(Path.Combine(_siteDir, "content", "posts", "p1.md"), """
            +++
            title = "文章一"
            date = 2026-01-01T00:00:00Z
            draft = false
            +++

            正文一。
            """);

        var layouts = Path.Combine(_siteDir, "layouts");
        Directory.CreateDirectory(Path.Combine(layouts, "partials"));
        Directory.CreateDirectory(Path.Combine(layouts, "_default"));

        // partial 内容即版本标记（v1 → v2 变更可观测）
        File.WriteAllText(Path.Combine(layouts, "partials", "badge.liquid"),
            "<span class=\"badge\">v1</span>");

        File.WriteAllText(Path.Combine(layouts, "index.liquid"),
            "<h1>{{ site.title }}</h1>{% include 'partials/badge' %}");
        File.WriteAllText(Path.Combine(layouts, "_default", "single.liquid"),
            "<article><h1>{{ page.title }}</h1>{% include 'partials/badge' %}</article>");
    }

    private SiteBuilder CreateBuilder() =>
        new(
            new ContentParser(),
            new FluidTemplateRenderer(Path.Combine(_siteDir, "layouts"), "https://example.com"),
            new StubAssetPipeline(),
            new ConfigLoader());

    private BuildOptions CreateOptions() => new()
    {
        SourcePath = _siteDir,
        OutputPath = _outputDir,
        Parallelism = 1
    };

    private static string ReadOutput(string siteDir, string relative) =>
        File.ReadAllText(Path.Combine(siteDir, "public", relative));

    [Fact]
    public async Task liquid_partial_变更_触发依赖页面增量重渲()
    {
        var builder = CreateBuilder();
        var options = CreateOptions();

        // 1. 全量构建（依赖追踪默认开）：产物含 v1
        var full = await builder.BuildAsync(options);
        Assert.True(full.Success, $"全量构建应成功: {string.Join(" | ", full.Errors.Select(e => e.Message))}");
        Assert.Contains("v1", ReadOutput(_siteDir, "index.html"));
        Assert.Contains("v1", ReadOutput(_siteDir, "posts/p1/index.html"));

        // 2. 改 partial：v1 → v2
        File.WriteAllText(_partialPath, "<span class=\"badge\">v2</span>");

        // 3. 增量构建（变更文件 = partial 物理路径）
        var incremental = await builder.IncrementalBuildAsync(options, [_partialPath]);
        Assert.True(incremental.Success,
            $"增量构建应成功: {string.Join(" | ", incremental.Errors.Select(e => e.Message))}");

        // 4. 依赖 partial 的页面必须重渲到 v2（P1-3 核心断言）
        Assert.Contains("v2", ReadOutput(_siteDir, "index.html"));
        Assert.Contains("v2", ReadOutput(_siteDir, "posts/p1/index.html"));
    }

    [Fact]
    public async Task liquid_partial_未变更_增量不重渲()
    {
        var builder = CreateBuilder();
        var options = CreateOptions();

        var full = await builder.BuildAsync(options);
        Assert.True(full.Success);

        // 变更一个无关内容文件：partial 未动，产物保持 v1
        var other = Path.Combine(_siteDir, "content", "posts", "p2.md");
        File.WriteAllText(other, """
            +++
            title = "文章二"
            date = 2026-01-02T00:00:00Z
            draft = false
            +++

            正文二。
            """);

        var incremental = await builder.IncrementalBuildAsync(options, [other]);
        Assert.True(incremental.Success);

        // 原页面未被 partial 变更波及（仍 v1）；新页面正常产出
        Assert.Contains("v1", ReadOutput(_siteDir, "index.html"));
        Assert.True(File.Exists(Path.Combine(_siteDir, "public", "posts", "p2", "index.html")));
    }

    private sealed class StubAssetPipeline : IAssetPipeline
    {
        public ValueTask<ProcessedAsset> ProcessAsync(AssetFile asset, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ProcessedAsset
            {
                OutputPath = asset.SourcePath,
                SourcePath = asset.SourcePath,
                Content = asset.Content,
                MediaType = asset.MediaType,
                ContentHash = "test-hash"
            });

        public async ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
            IReadOnlyList<AssetFile> assets, CancellationToken cancellationToken)
        {
            var results = new List<ProcessedAsset>();
            foreach (var asset in assets)
            {
                results.Add(await ProcessAsync(asset, cancellationToken).ConfigureAwait(false));
            }
            return results;
        }

        public async IAsyncEnumerable<ProcessedAsset> ProcessStreamAsync(
            IAsyncEnumerable<AssetFile> assets,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var asset in assets.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return await ProcessAsync(asset, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
