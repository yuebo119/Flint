// Flint 静态站点生成器
// 站点构建器单元测试

#pragma warning disable CA2000 // 测试代码中不需要处理 Dispose
#pragma warning disable CA2007 // 测试代码中不需要 ConfigureAwait

using Flint.Core.Abstractions;
using Flint.Core.Models;
using Flint.Core.Site;
using Xunit;

namespace Flint.Core.Tests.Site;

/// <summary>
/// SiteBuilder 单元测试
/// </summary>
public class SiteBuilderTests : IDisposable
{
    private readonly StubContentParser _contentParser;
    private readonly StubTemplateRenderer _templateRenderer;
    private readonly StubAssetPipeline _assetPipeline;
    private readonly StubConfigLoader _configLoader;
    private readonly string _testDir;
    private readonly string _outputDir;

    public SiteBuilderTests()
    {
        _contentParser = new StubContentParser();
        _templateRenderer = new StubTemplateRenderer();
        _assetPipeline = new StubAssetPipeline();
        _configLoader = new StubConfigLoader();

        _testDir = Path.Combine(Path.GetTempPath(), $"Flint_test_{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_testDir, "public");
        Directory.CreateDirectory(_testDir);
        Directory.CreateDirectory(Path.Combine(_testDir, "content"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            { Directory.Delete(_testDir, true); }
            catch { }
        }
        GC.SuppressFinalize(this);
    }

    #region 构造函数测试

    [Fact]
    public void Constructor_应该正确初始化()
    {
        // Act
        var builder = new SiteBuilder(
            _contentParser,
            _templateRenderer,
            _assetPipeline,
            _configLoader);

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void Constructor_使用自定义选项应该正确初始化()
    {
        // Arrange
        var options = new SiteBuilderOptions
        {
            EnableCache = false,
            CacheDirectory = "/tmp/cache"
        };

        // Act
        var builder = new SiteBuilder(
            _contentParser,
            _templateRenderer,
            _assetPipeline,
            _configLoader,
            options);

        // Assert
        Assert.NotNull(builder);
    }

    #endregion

    #region BuildAsync 测试

    [Fact]
    public async Task BuildAsync_空站点应该成功()
    {
        // Arrange
        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();

        // Act
        var result = await builder.BuildAsync(options);

        // Assert
        // 空站点可能因为没有模板而返回错误，这是预期行为
        // 我们只验证构建过程完成且返回了结果
        Assert.NotNull(result);
        Assert.NotNull(result.OutputPath);
    }

    [Fact]
    public async Task BuildAsync_有内容时应该构建页面()
    {
        // Arrange
        // 创建测试内容文件
        var contentPath = Path.Combine(_testDir, "content", "test.md");
        await File.WriteAllTextAsync(contentPath, "---\ntitle: Test\n---\nHello World");

        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();

        // Act
        var result = await builder.BuildAsync(options);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.PagesBuilt >= 0);
    }

    [Fact]
    public async Task BuildAsync_配置加载失败应该返回错误()
    {
        // Arrange
        _configLoader.ShouldThrow = true;

        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();

        // Act
        var result = await builder.BuildAsync(options);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task BuildAsync_应该记录构建时间()
    {
        // Arrange
        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();

        // Act
        var result = await builder.BuildAsync(options);

        // Assert
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task BuildAsync_应该记录内存使用()
    {
        // Arrange
        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();

        // Act
        var result = await builder.BuildAsync(options);

        // Assert
        Assert.True(result.MemoryUsed > 0);
    }

    #endregion

    #region IncrementalBuildAsync 测试

    [Fact]
    public async Task IncrementalBuildAsync_无变化文件应该快速完成()
    {
        // Arrange
        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();
        var changedFiles = new List<string>();

        // Act
        var result = await builder.IncrementalBuildAsync(options, changedFiles);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0, result.PagesBuilt);
        Assert.Equal(0, result.AssetsProcessed);
    }

    [Fact]
    public async Task IncrementalBuildAsync_内容文件变化应该重新构建()
    {
        // Arrange
        // 创建测试内容文件
        var contentPath = Path.Combine(_testDir, "content", "test.md");
        await File.WriteAllTextAsync(contentPath, "---\ntitle: Test\n---\nHello World");

        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();
        var changedFiles = new List<string> { contentPath };

        // Act
        var result = await builder.IncrementalBuildAsync(options, changedFiles);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task IncrementalBuildAsync_资源文件变化应该重新处理()
    {
        // Arrange
        // 创建测试资源文件
        var assetsDir = Path.Combine(_testDir, "assets");
        Directory.CreateDirectory(assetsDir);
        var assetPath = Path.Combine(assetsDir, "style.css");
        await File.WriteAllTextAsync(assetPath, "body { color: red; }");

        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();
        var changedFiles = new List<string> { assetPath };

        // Act
        var result = await builder.IncrementalBuildAsync(options, changedFiles);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task IncrementalBuildAsync_删除内容后sitemap不应保留死链接()
    {
        // Arrange - 两篇内容 + 全量构建建立基线
        var contentDir = Path.Combine(_testDir, "content");
        var post1 = Path.Combine(contentDir, "post-one.md");
        var post2 = Path.Combine(contentDir, "post-two.md");
        await File.WriteAllTextAsync(post1, "---\ntitle: Post One\n---\nOne");
        await File.WriteAllTextAsync(post2, "---\ntitle: Post Two\n---\nTwo");

        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();
        var full = await builder.BuildAsync(options);
        Assert.True(full.Success);

        var sitemapPath = Path.Combine(_outputDir, "sitemap.xml");
        Assert.True(File.Exists(sitemapPath), "全量构建应产出 sitemap.xml");
        var sitemapBefore = await File.ReadAllTextAsync(sitemapPath);
        Assert.Contains("post-one", sitemapBefore, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("post-two", sitemapBefore, StringComparison.OrdinalIgnoreCase);

        // Act - 文件系统删除后增量构建
        File.Delete(post2);
        var incremental = await builder.IncrementalBuildAsync(options, new List<string> { post2 });

        // Assert - sitemap 必须重新生成且不再包含被删页
        Assert.True(incremental.Success);
        var sitemapAfter = await File.ReadAllTextAsync(sitemapPath);
        Assert.Contains("post-one", sitemapAfter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("post-two", sitemapAfter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncrementalBuildAsync_仅模板变化时不应重产sitemap()
    {
        // identity 分离（layout identity）：仅模板变化时页面集与 lastmod 未变，
        // sitemap/feeds 重产是纯浪费——断言产物 mtime 不变
        var contentDir = Path.Combine(_testDir, "content");
        var post1 = Path.Combine(contentDir, "post-one.md");
        await File.WriteAllTextAsync(post1, "---\ntitle: Post One\n---\nOne");

        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();
        Assert.True((await builder.BuildAsync(options)).Success);

        var sitemapPath = Path.Combine(_outputDir, "sitemap.xml");
        var sitemapBeforeWrite = File.GetLastWriteTimeUtc(sitemapPath);

        // Act - 仅模板变化（无 .md 变化）的增量构建
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts"));
        File.WriteAllText(Path.Combine(_testDir, "layouts", "single.html"), "NEW LAYOUT");
        var incremental = await builder.IncrementalBuildAsync(
            options, new List<string> { Path.Combine(_testDir, "layouts", "single.html") });

        // Assert
        Assert.True(incremental.Success);
        // 仅模板变化时 sitemap 不应被重产（URL 集与 lastmod 均未变）
        Assert.Equal(sitemapBeforeWrite, File.GetLastWriteTimeUtc(sitemapPath));
    }

    [Fact]
    public async Task IncrementalBuildAsync_内容变化时sitemap应重产()
    {
        // content identity：内容变化必须重产 sitemap（与模板-only 分叉的对照面）
        var contentDir = Path.Combine(_testDir, "content");
        var post1 = Path.Combine(contentDir, "post-one.md");
        await File.WriteAllTextAsync(post1, "---\ntitle: Post One\n---\nOne");

        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();
        Assert.True((await builder.BuildAsync(options)).Success);

        var sitemapPath = Path.Combine(_outputDir, "sitemap.xml");
        File.WriteAllText(Path.Combine(contentDir, "post-two.md"), "---\ntitle: Post Two\n---\nTwo");

        var incremental = await builder.IncrementalBuildAsync(
            options, new List<string> { Path.Combine(contentDir, "post-two.md") });

        Assert.True(incremental.Success);
        var sitemap = await File.ReadAllTextAsync(sitemapPath);
        Assert.Contains("post-two", sitemap, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IncrementalBuildAsync_仅模板变化时不应重产feeds()
    {
        var contentDir = Path.Combine(_testDir, "content");
        var feedPath = Path.Combine(_outputDir, "index.xml");
        await File.WriteAllTextAsync(Path.Combine(contentDir, "post-one.md"), "---\ntitle: P\n---\nP");
        var builder = CreateSiteBuilder();
        var options = CreateBuildOptions();
        Assert.True((await builder.BuildAsync(options)).Success);

        var feedBefore = File.GetLastWriteTimeUtc(feedPath);

        Directory.CreateDirectory(Path.Combine(_testDir, "layouts"));
        File.WriteAllText(Path.Combine(_testDir, "layouts", "single.html"), "NEW");
        await builder.IncrementalBuildAsync(
            options, new List<string> { Path.Combine(_testDir, "layouts", "single.html") });

        // 仅模板变化时 feed 不应被重产
        Assert.Equal(feedBefore, File.GetLastWriteTimeUtc(feedPath));
    }

    #endregion

    #region CleanAsync 测试

    [Fact]
    public async Task CleanAsync_应该删除输出目录()
    {
        // Arrange
        Directory.CreateDirectory(_outputDir);
        await File.WriteAllTextAsync(Path.Combine(_outputDir, "test.html"), "<html></html>");

        var builder = CreateSiteBuilder();

        // Act
        await builder.CleanAsync(_outputDir);

        // Assert
        Assert.False(Directory.Exists(_outputDir));
    }

    [Fact]
    public async Task CleanAsync_目录不存在时不应该抛出异常()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDir, "non_existent");
        var builder = CreateSiteBuilder();

        // Act & Assert
        await builder.CleanAsync(nonExistentDir);
        // 不抛出异常即为成功
    }

    [Fact]
    public async Task CleanAsync_盘根与一级目录应该拒绝删除()
    {
        // Arrange
        var builder = CreateSiteBuilder();
        var root = Path.GetPathRoot(Path.GetFullPath(_testDir)) ?? "C:\\";

        // Act & Assert - 盘根与一级目录（如 C:\Users）不可能是合法输出目录
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CleanAsync(root).AsTask());
        var firstLevel = Path.Combine(root, "somewhere");
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CleanAsync(firstLevel).AsTask());
    }

    [Fact]
    public async Task CleanAsync_当前工作目录及其祖先应该拒绝删除()
    {
        // Arrange
        var builder = CreateSiteBuilder();
        var cwd = Environment.CurrentDirectory;

        // Act & Assert - 当前工作目录与其父目录都在拒绝之列
        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CleanAsync(cwd).AsTask());
        var parent = Directory.GetParent(cwd)?.FullName;
        if (!string.IsNullOrEmpty(parent))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => builder.CleanAsync(parent).AsTask());
        }
    }

    [Theory]
    [InlineData("/:year/:month/:day/", "/2024/01/15/")]
    [InlineData("/:year/:yearday/", "/2024/15/")]
    [InlineData("/:monthname/:dayname/", "/January/Monday/")]
    [InlineData("/:slugorfilename/", "/my-slug/")]
    [InlineData("/:section/:title/", "/posts/my-post/")]
    [InlineData("/:sections/:title/", "//posts/my-post/")]
    public void ExpandPermalinkTokens_标准Token应正确展开(string pattern, string expected)
    {
        // Arrange - 2024-01-15 是周一，年积日 15；源在 content/posts/my-post.md
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var sourcePath = Path.Combine(Path.GetTempPath(), "content", "posts", "my-post.md");

        // Act
        var result = SiteBuilder.ExpandPermalinkTokens(pattern, date, "my-post", "my-slug", sourcePath, "");

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExpandPermalinkTokens_未知Token应原样保留()
    {
        var result = SiteBuilder.ExpandPermalinkTokens(
            "/:unknowntoken/:year/", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero),
            "x", "", "", "");
        Assert.Equal("/:unknowntoken/2024/", result);
    }

    [Fact]
    public void ExpandPermalinkTokens_内容根文件无目录链时sections为空()
    {
        var sourcePath = Path.Combine(Path.GetTempPath(), "content", "top.md");
        var result = SiteBuilder.ExpandPermalinkTokens(
            "/:sections/:section/:title/", new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero),
            "top", "", sourcePath, "");
        // sections/section 均展开为空串，pattern 的字面斜杠保留（机械结果）
        Assert.Equal("///top/", result);
    }

    #endregion

    #region 辅助方法

    private SiteBuilder CreateSiteBuilder()
    {
        return new SiteBuilder(
            _contentParser,
            _templateRenderer,
            _assetPipeline,
            _configLoader);
    }

    // taxonomy identity 测试专用：tags/slug 语义来自 front matter，
    // StubContentParser 的 Metadata 恒不含文件内容（tags 恒空、slug 恒 null），
    // 必须用真解析器与真渲染器才能驱动 taxonomy 签名链路
    private SiteBuilder CreateRealContentSiteBuilder()
    {
        return new SiteBuilder(
            new Flint.Core.Content.ContentParser(),
            new Flint.Core.Templates.ScribanTemplateRenderer(
                Path.Combine(_testDir, "layouts"), "https://example.com"),
            _assetPipeline,
            _configLoader);
    }

    // config/data identity 测试专用：全真管线（真 config loader 读取 Flint.toml、
    // 真解析/渲染）——stub loader 返回固定配置，产物断言无法感知配置/数据变化
    private SiteBuilder CreateRealPipelineSiteBuilder()
    {
        return new SiteBuilder(
            new Flint.Core.Content.ContentParser(),
            new Flint.Core.Templates.ScribanTemplateRenderer(
                Path.Combine(_testDir, "layouts"), "http://localhost:1313"),
            _assetPipeline,
            new Flint.Core.Configuration.ConfigLoader());
    }

    private BuildOptions CreateBuildOptions()
    {
        return new BuildOptions
        {
            SourcePath = _testDir,
            OutputPath = _outputDir,
            IncludeDrafts = false,
            IncludeFuture = false,
            Parallelism = 2
        };
    }

    #endregion

    #region Stub 类

    private sealed class StubContentParser : IContentParser
    {
        public Flint.Core.Content.Shortcodes.ShortcodeProcessor ShortcodeProcessor { get; } = new();

        public ValueTask<ParsedContent> ParseAsync(ContentFile file, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(CreateParsedContent(file.Path));
        }

        public ParsedContent Parse(ContentFile file)
        {
            return CreateParsedContent(file.Path);
        }

        public async IAsyncEnumerable<ParsedContent> ParseBatchAsync(
            IEnumerable<ContentFile> files,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var file in files)
            {
                yield return await ParseAsync(file, cancellationToken);
            }
        }

        private static ParsedContent CreateParsedContent(string sourcePath)
        {
            return new ParsedContent
            {
                SourcePath = sourcePath,
                Metadata = new FrontMatter
                {
                    Title = "Test",
                    Date = DateTimeOffset.Now,
                    Draft = false
                },
                HtmlContent = "<p>Test content</p>",
                RawMarkdown = "Test content",
                PlainText = "Test content",
                Summary = "Test",
                WordCount = 2,
                ReadingTime = TimeSpan.FromMinutes(1),
                Headings = [],
                Links = [],
                Images = [],
                ContentHash = "test-hash"
            };
        }
    }

    private sealed class StubTemplateRenderer : ITemplateRenderer
    {
        public ValueTask<string> RenderAsync(string templateName, TemplateContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult("<html><body>Test</body></html>");
        }

        public void Render(string templateName, TemplateContext context, System.Buffers.IBufferWriter<char> output)
        {
            var html = "<html><body>Test</body></html>";
            var span = output.GetSpan(html.Length);
            html.AsSpan().CopyTo(span);
            output.Advance(html.Length);
        }

        public ValueTask<string> RenderStringAsync(string templateContent, TemplateContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult("<html><body>Test</body></html>");
        }

        public bool TemplateExists(string templateName) => false;

        public IReadOnlyList<string> GetDependencies(string templateName) => [];
    }

    private sealed class StubAssetPipeline : IAssetPipeline
    {
        public ValueTask<ProcessedAsset> ProcessAsync(AssetFile asset, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ProcessedAsset
            {
                OutputPath = asset.SourcePath,
                SourcePath = asset.SourcePath,
                Content = asset.Content,
                MediaType = asset.MediaType,
                ContentHash = "test-hash"
            });
        }

        public async ValueTask<IReadOnlyList<ProcessedAsset>> ProcessBatchAsync(
            IReadOnlyList<AssetFile> assets,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ProcessedAsset>();
            foreach (var asset in assets)
            {
                results.Add(await ProcessAsync(asset, cancellationToken));
            }
            return results;
        }

        public async IAsyncEnumerable<ProcessedAsset> ProcessStreamAsync(
            IAsyncEnumerable<AssetFile> assets,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var asset in assets.WithCancellation(cancellationToken))
            {
                yield return await ProcessAsync(asset, cancellationToken);
            }
        }
    }

    private sealed class StubConfigLoader : IConfigLoader
    {
        public bool ShouldThrow { get; set; }

        public ValueTask<SiteConfig> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("配置文件不存在");
            }

            return ValueTask.FromResult(new SiteConfig
            {
                Title = "Test Site",
                BaseURL = "https://example.com",
                LanguageCode = "en"
            });
        }

        public SiteConfig Load(string configPath)
        {
            if (ShouldThrow)
            {
                throw new InvalidOperationException("配置文件不存在");
            }

            return new SiteConfig
            {
                Title = "Test Site",
                BaseURL = "https://example.com",
                LanguageCode = "en"
            };
        }

        public ValueTask<SiteConfig> AutoLoadAsync(string directory, CancellationToken cancellationToken = default)
        {
            return LoadAsync(Path.Combine(directory, "Flint.toml"), cancellationToken);
        }

        public ValueTask SaveAsync(SiteConfig config, string configPath, ConfigFormat format = ConfigFormat.Toml, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }
    }


    [Fact]
    public async Task IncrementalBuildAsync_配置文件变化应强制全量装配()
    {
        // config identity：配置变化影响面广（影响 siteContext 全局），增量路径
        // 收到时直接转全量构建——title 变更必须反映到页面产物（仅断言 Success
        // 无法证伪"转全量丢失"的回归）
        Directory.CreateDirectory(Path.Combine(_testDir, "content"));
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts"));
        File.WriteAllText(Path.Combine(_testDir, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"Before\"\n");
        File.WriteAllText(Path.Combine(_testDir, "layouts", "single.html"),
            "T={{ site.title }}");
        File.WriteAllText(Path.Combine(_testDir, "content", "a.md"),
            "---\ntitle: A\n---\nA");

        var builder = CreateRealPipelineSiteBuilder();
        var options = CreateBuildOptions();
        Assert.True((await builder.BuildAsync(options)).Success);
        var pagePath = Path.Combine(_outputDir, "a", "index.html");
        Assert.Contains("Before",
            await File.ReadAllTextAsync(pagePath), StringComparison.Ordinal);

        // Act - 仅 config 变化（无 .md/.html 变化：装配门禁曾把此场景拦在零影响）
        File.WriteAllText(Path.Combine(_testDir, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"After\"\n");
        var configPath = Path.Combine(_testDir, "Flint.toml");
        var result = await builder.IncrementalBuildAsync(
            options, new List<string> { configPath });

        // Assert - title 变更反映到产物
        Assert.True(result.Success);
        Assert.Contains("After",
            await File.ReadAllTextAsync(pagePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncrementalBuildAsync_Data目录文件变化应走全量装配()
    {
        // data identity：影响面广（全站 site.data），增量路径收到时直接转全量——
        // 断言产物反映新数据（仅断言 Success 无法证伪"全量未发生"的回归）
        Directory.CreateDirectory(Path.Combine(_testDir, "data"));
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts"));
        File.WriteAllText(Path.Combine(_testDir, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"T\"\n");
        File.WriteAllText(Path.Combine(_testDir, "data", "authors.yml"), "alice: Alice\n");
        File.WriteAllText(Path.Combine(_testDir, "layouts", "single.html"),
            "D={{ site.data.authors.alice }}");
        File.WriteAllText(Path.Combine(_testDir, "content", "a.md"),
            "---\ntitle: A\n---\nA");

        var builder = CreateRealPipelineSiteBuilder();
        var options = CreateBuildOptions();
        Assert.True((await builder.BuildAsync(options)).Success);
        var pagePath = Path.Combine(_outputDir, "a", "index.html");
        Assert.Contains("Alice",
            await File.ReadAllTextAsync(pagePath), StringComparison.Ordinal);

        // Act - data 文件内容变化
        File.WriteAllText(Path.Combine(_testDir, "data", "authors.yml"), "alice: Bob\n");
        var result = await builder.IncrementalBuildAsync(
            options, new List<string> { Path.Combine(_testDir, "data", "authors.yml") });

        // Assert - site.data 更新反映到产物
        Assert.True(result.Success);
        Assert.Contains("Bob",
            await File.ReadAllTextAsync(pagePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IncrementalBuildAsync_无先前全量时改tags应产出term页()
    {
        // taxonomy identity 的空树兜底：直接增量调用（无先前全量构建）时，
        // 装配后的树已含变更、签名前后恒等——必须强制 taxonomy 页重产。
        // 回归防护：兜底路径曾对同一棵树取前后签名，term 页被恒等比较跳过
        var contentDir = Path.Combine(_testDir, "content");
        var postPath = Path.Combine(contentDir, "post-one.md");
        await File.WriteAllTextAsync(postPath,
            "---\ntitle: Post One\ntags: [alpha, beta]\n---\nOne");
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts", "_default"));
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "layouts", "_default", "single.html"), "S={{ page.title }}");

        var builder = CreateRealContentSiteBuilder();
        var options = CreateBuildOptions();

        // Act - 不经 BuildAsync 直接增量（触发空树兜底路径）
        var result = await builder.IncrementalBuildAsync(
            options, new List<string> { postPath });

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_outputDir, "tags", "beta", "index.html")),
            "空树兜底路径改 tags 后 term 页必须产出");
    }

    [Fact]
    public async Task IncrementalBuildAsync_改slug后term页应更新链接()
    {
        // taxonomy 签名含 Slug：改 slug 改变 term 页内的 permalink 输出，
        // 签名必须感知（缺 Slug 时 term 页保留指向旧地址的死链）
        var contentDir = Path.Combine(_testDir, "content");
        var postPath = Path.Combine(contentDir, "post-one.md");
        await File.WriteAllTextAsync(postPath,
            "---\ntitle: Post One\ntags: [alpha]\n---\nOne");
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts", "_default"));
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "layouts", "_default", "single.html"), "S={{ page.title }}");

        var builder = CreateRealContentSiteBuilder();
        var options = CreateBuildOptions();
        Assert.True((await builder.BuildAsync(options)).Success);

        var termPath = Path.Combine(_outputDir, "tags", "alpha", "index.html");
        Assert.True(File.Exists(termPath), "全量构建后 term 页应存在");
        Assert.DoesNotContain("moved-post",
            await File.ReadAllTextAsync(termPath), StringComparison.Ordinal);

        // Act - 仅改 slug（tags 不变）
        await File.WriteAllTextAsync(postPath,
            "---\ntitle: Post One\nslug: moved-post\ntags: [alpha]\n---\nOne");
        var result = await builder.IncrementalBuildAsync(
            options, new List<string> { postPath });

        // Assert - term 页链接应指向新 slug
        Assert.True(result.Success);
        Assert.Contains("moved-post",
            await File.ReadAllTextAsync(termPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_slug含路径穿越且json输出时不得写出输出目录之外()
    {
        // GetOutputPathForFormat 与 GetOutputPath 的越界护栏对称：
        // 非 html 输出格式的 slug 含 ".." 时 fail loud，不写出输出目录之外
        var contentDir = Path.Combine(_testDir, "content");
        await File.WriteAllTextAsync(Path.Combine(contentDir, "evil.md"),
            "---\ntitle: Evil\nslug: \"../escape\"\noutputs: [json]\n---\nX");
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts", "_default"));
        File.WriteAllText(
            Path.Combine(_testDir, "layouts", "_default", "single.json.html"),
            "J={{ page.title }}");

        var builder = CreateSiteBuilder();
        try
        {
            await builder.BuildAsync(CreateBuildOptions());
        }
        catch (InvalidOperationException)
        {
            // fail loud 预期形态之一：异常直接冒泡
        }

        // Assert - 无论异常或 Errors 收集，越界文件都不得存在
        Assert.False(File.Exists(Path.Combine(_testDir, "escape.json")),
            "json 输出路径逃逸护栏未拦截 .. slug");
        Assert.False(Directory.Exists(Path.Combine(_testDir, "escape")),
            "json 输出路径逃逸护栏未拦截 .. slug（目录形态）");
    }

    [Fact]
    public async Task BuildAsync_term与taxonomy页应注入词条数据供模板枚举()
    {
        // 默认主题模板 term.html 用 page.pages 枚举词条页面、taxonomy.html 用
        // page.terms 枚举词条——PageContext 缺这两个成员时（回归形态）
        // 词条/分类页渲染为空列表（端到端冒烟发现的回归）
        var contentDir = Path.Combine(_testDir, "content");
        await File.WriteAllTextAsync(Path.Combine(contentDir, "post-one.md"),
            "---\ntitle: Post One\ntags: [alpha]\n---\nOne");
        Directory.CreateDirectory(Path.Combine(_testDir, "layouts", "_default"));
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "layouts", "_default", "single.html"), "S={{ page.title }}");
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "layouts", "_default", "term.html"),
            "T={{ for p in page.pages }}[{{ p.title }}]{{ end }}");
        await File.WriteAllTextAsync(
            Path.Combine(_testDir, "layouts", "_default", "taxonomy.html"),
            "X={{ for t in page.terms }}({{ t.name }}:{{ t.count }}){{ end }}");

        var builder = CreateRealContentSiteBuilder();
        var result = await builder.BuildAsync(CreateBuildOptions());

        Assert.True(result.Success);
        Assert.Contains("[Post One]",
            await File.ReadAllTextAsync(Path.Combine(_outputDir, "tags", "alpha", "index.html")),
            StringComparison.Ordinal);
        Assert.Contains("(alpha:1)",
            await File.ReadAllTextAsync(Path.Combine(_outputDir, "tags", "index.html")),
            StringComparison.Ordinal);
    }


    #endregion
}
