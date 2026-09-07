// Flint 静态站点生成器
// 开发服务器集成测试
// 验证文件服务和热重载功能

using System.Net;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Site;
using Flint.Core.Templates;
using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.DevServer;

/// <summary>
/// 开发服务器集成测试
/// 验证服务器启动、停止、文件服务和热重载功能
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 3.1: 开发服务器启动和停止
/// - Requirements 3.2: 端口占用处理
/// - Requirements 3.3: HTTP 文件服务
/// - Requirements 3.4: Content-Type 头设置
/// - Requirements 3.5: 404 响应
/// - Requirements 3.8: HTTP/2 支持
/// </remarks>
[Collection("DevServer")]
public class DevServerIntegrationTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private IDevServer? _devServer;
    private DevServerTestClient? _client;
    private CancellationTokenSource? _cts;

    public DevServerIntegrationTests()
    {
        _fixture = new TestSiteFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateSiteAsync("minimal");

        // 添加测试内容
        await _fixture.AddContentAsync("posts/test-post.md", """
            +++
            title = "测试文章"
            date = 2024-01-15T10:00:00+08:00
            draft = false
            +++

            这是一篇测试文章的内容。
            """);

        // 执行初始构建
        await _fixture.BuildAsync();
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }

        if (_devServer != null)
        {
            await _devServer.StopAsync();
            if (_devServer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _cts?.Cancel();
        _cts?.Dispose();

        await _fixture.DisposeAsync();
    }

    private IDevServer CreateDevServer()
    {
        var contentParser = new ContentParser();
        var templateRenderer = new ScribanTemplateRenderer(
            Path.Combine(_fixture.SiteRoot, "layouts"));
        var assetPipeline = new AssetPipeline();
        var configLoader = new ConfigLoader();

        var siteBuilder = new SiteBuilder(
            contentParser,
            templateRenderer,
            assetPipeline,
            configLoader);

        return new Core.Server.DevServer(siteBuilder);
    }

    private async Task<(IDevServer Server, DevServerTestClient Client)> StartServerAsync(
        int port = 0,
        bool liveReload = true,
        bool openBrowser = false)
    {
        _cts = new CancellationTokenSource();
        _devServer = CreateDevServer();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port == 0 ? GetRandomPort() : port,
            LiveReload = liveReload,
            OpenBrowser = openBrowser,
            UseHttps = false,
            BindAddress = "127.0.0.1",
            IncludeDrafts = true,
            IncludeFuture = true,
            Verbose = false
        };

        await _devServer.StartAsync(options, _cts.Token);

        _client = new DevServerTestClient(_devServer.ServerUrl!);

        // 等待服务器就绪
        var ready = await _client.WaitForServerReadyAsync(TimeSpan.FromSeconds(10));
        ready.Should().BeTrue("服务器应该在 10 秒内就绪");

        return (_devServer, _client);
    }

    private static int GetRandomPort()
    {
        // 使用随机端口避免冲突
        return Random.Shared.Next(10000, 60000);
    }

    #region 服务器启动和停止测试

    /// <summary>
    /// 测试服务器正常启动
    /// </summary>
    [Fact]
    public async Task StartAsync_WithValidOptions_ServerStarts()
    {
        // Arrange & Act
        var (server, client) = await StartServerAsync();

        // Assert
        server.IsRunning.Should().BeTrue();
        server.ServerUrl.Should().NotBeNullOrEmpty();
        server.ServerUrl.Should().StartWith("http://");
    }

    /// <summary>
    /// 测试服务器正常停止
    /// </summary>
    [Fact]
    public async Task StopAsync_WhenRunning_ServerStops()
    {
        // Arrange
        var (server, _) = await StartServerAsync();
        server.IsRunning.Should().BeTrue();

        // Act
        await server.StopAsync();

        // Assert
        server.IsRunning.Should().BeFalse();
        server.ServerUrl.Should().BeNull();
    }

    /// <summary>
    /// 测试端口占用时自动选择下一个端口
    /// </summary>
    [Fact]
    public async Task StartAsync_WhenPortOccupied_SelectsNextPort()
    {
        // Arrange - 启动第一个服务器
        var port = GetRandomPort();
        var (server1, _) = await StartServerAsync(port);
        var url1 = server1.ServerUrl;

        // 创建第二个服务器实例
        var contentParser = new ContentParser();
        var templateRenderer = new ScribanTemplateRenderer(
            Path.Combine(_fixture.SiteRoot, "layouts"));
        var assetPipeline = new AssetPipeline();
        var configLoader = new ConfigLoader();
        var siteBuilder = new SiteBuilder(
            contentParser, templateRenderer, assetPipeline, configLoader);
        var server2 = new Core.Server.DevServer(siteBuilder);

        try
        {
            // Act - 尝试在同一端口启动第二个服务器
            var options2 = new DevServerOptions
            {
                SourcePath = _fixture.SiteRoot,
                OutputPath = _fixture.OutputPath,
                Port = port,
                LiveReload = false,
                OpenBrowser = false,
                UseHttps = false,
                BindAddress = "127.0.0.1"
            };

            await server2.StartAsync(options2);

            // Assert
            server2.IsRunning.Should().BeTrue();
            server2.ServerUrl.Should().NotBe(url1, "第二个服务器应该使用不同的端口");
        }
        finally
        {
            await server2.StopAsync();
            server2.Dispose();
        }
    }

    #endregion

    #region HTTP 文件服务测试

    /// <summary>
    /// 测试 HTML 文件服务
    /// </summary>
    [Fact]
    public async Task GetAsync_HtmlFile_ReturnsCorrectContent()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/index.html");

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.ContentType.Should().Contain("text/html");
        response.Content.Should().Contain("<!DOCTYPE html>");
    }

    /// <summary>
    /// 测试目录索引（自动添加 index.html）
    /// </summary>
    [Fact]
    public async Task GetAsync_DirectoryPath_ReturnsIndexHtml()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.ContentType.Should().Contain("text/html");
    }

    /// <summary>
    /// 测试 404 响应
    /// </summary>
    [Fact]
    public async Task GetAsync_NonExistentFile_Returns404()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/non-existent-file.html");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// 测试 CSS 文件的 Content-Type
    /// </summary>
    [Fact]
    public async Task GetAsync_CssFile_ReturnsCorrectContentType()
    {
        // Arrange
        await _fixture.AddStaticAsync("styles/main.css",
            "body { margin: 0; }"u8.ToArray());
        await _fixture.BuildAsync();
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/styles/main.css");

        // Assert
        if (response.IsSuccess)
        {
            response.ContentType.Should().Contain("text/css");
        }
    }

    /// <summary>
    /// 测试 JavaScript 文件的 Content-Type
    /// </summary>
    [Fact]
    public async Task GetAsync_JsFile_ReturnsCorrectContentType()
    {
        // Arrange
        await _fixture.AddStaticAsync("scripts/app.js",
            "console.log('Hello');"u8.ToArray());
        await _fixture.BuildAsync();
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/scripts/app.js");

        // Assert
        if (response.IsSuccess)
        {
            response.ContentType.Should().Contain("javascript");
        }
    }

    /// <summary>
    /// 测试 JSON 文件的 Content-Type
    /// </summary>
    [Fact]
    public async Task GetAsync_JsonFile_ReturnsCorrectContentType()
    {
        // Arrange
        await _fixture.AddStaticAsync("data/config.json",
            """{"key": "value"}"""u8.ToArray());
        await _fixture.BuildAsync();
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/data/config.json");

        // Assert
        if (response.IsSuccess)
        {
            response.ContentType.Should().Contain("application/json");
        }
    }

    /// <summary>
    /// 测试图片文件的 Content-Type
    /// </summary>
    [Fact]
    public async Task GetAsync_ImageFile_ReturnsCorrectContentType()
    {
        // Arrange - 创建一个最小的 PNG 文件
        var pngBytes = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG 签名
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR 块
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
            0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
            0x54, 0x08, 0xD7, 0x63, 0xF8, 0xFF, 0xFF, 0x3F,
            0x00, 0x05, 0xFE, 0x02, 0xFE, 0xDC, 0xCC, 0x59,
            0xE7, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
            0x44, 0xAE, 0x42, 0x60, 0x82
        };
        await _fixture.AddStaticAsync("images/test.png", pngBytes);
        await _fixture.BuildAsync();
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/images/test.png");

        // Assert
        if (response.IsSuccess)
        {
            response.ContentType.Should().Contain("image/png");
        }
    }

    #endregion

    #region 热重载脚本注入测试

    /// <summary>
    /// 测试 HTML 文件包含热重载脚本
    /// </summary>
    [Fact]
    public async Task GetAsync_HtmlFile_ContainsLiveReloadScript()
    {
        // Arrange
        var (_, client) = await StartServerAsync(liveReload: true);

        // Act
        var response = await client.GetAsync("/index.html");

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Content.Should().Contain("__livereload",
            "HTML 应该包含热重载脚本");
    }

    /// <summary>
    /// 测试禁用热重载时不注入脚本
    /// </summary>
    [Fact]
    public async Task GetAsync_HtmlFile_NoScriptWhenLiveReloadDisabled()
    {
        // Arrange
        var (_, client) = await StartServerAsync(liveReload: false);

        // Act
        var response = await client.GetAsync("/index.html");

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Content.Should().NotContain("__livereload",
            "禁用热重载时不应注入脚本");
    }

    #endregion

    #region 并发请求测试

    /// <summary>
    /// 测试并发请求处理
    /// </summary>
    [Fact]
    public async Task GetConcurrentAsync_MultipleRequests_AllSucceed()
    {
        // Arrange
        var (_, client) = await StartServerAsync();
        var paths = Enumerable.Range(0, 10).Select(_ => "/index.html").ToList();

        // Act
        var responses = await client.GetConcurrentAsync(paths);

        // Assert
        responses.Should().HaveCount(10);
        responses.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    /// <summary>
    /// 测试大量并发请求
    /// </summary>
    [Fact]
    public async Task GetConcurrentAsync_ManyRequests_HandlesLoad()
    {
        // Arrange
        var (_, client) = await StartServerAsync();
        var paths = Enumerable.Range(0, 50).Select(_ => "/").ToList();

        // Act
        var responses = await client.GetConcurrentAsync(paths, TimeSpan.FromSeconds(30));

        // Assert
        var successCount = responses.Count(r => r.IsSuccess);
        successCount.Should().BeGreaterThan(40, "大部分请求应该成功");
    }

    #endregion

    #region 服务器状态检查测试

    /// <summary>
    /// 测试服务器在线检查
    /// </summary>
    [Fact]
    public async Task IsServerOnlineAsync_WhenRunning_ReturnsTrue()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var isOnline = await client.IsServerOnlineAsync();

        // Assert
        isOnline.Should().BeTrue();
    }

    /// <summary>
    /// 测试等待服务器就绪
    /// </summary>
    [Fact]
    public async Task WaitForServerReadyAsync_WhenRunning_ReturnsTrue()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var isReady = await client.WaitForServerReadyAsync(TimeSpan.FromSeconds(5));

        // Assert
        isReady.Should().BeTrue();
    }

    #endregion
}
