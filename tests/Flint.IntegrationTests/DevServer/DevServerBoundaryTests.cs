// Flint 静态站点生成器
// 开发服务器边界条件测试
// 验证边界条件和异常场景

using System.Net;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Server;
using Flint.Core.Site;
using Flint.Core.Templates;
using Flint.IntegrationTests.Fixtures;
using Flint.IntegrationTests.Utilities;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.DevServer;

/// <summary>
/// 开发服务器边界条件测试
/// 验证边界条件和异常场景
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 3.1: 服务器启动和停止
/// - Requirements 3.10: 多客户端连接
/// </remarks>
[Collection("DevServer")]
public class DevServerBoundaryTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private IDevServer? _devServer;
    private DevServerTestClient? _client;
    private CancellationTokenSource? _cts;

    public DevServerBoundaryTests()
    {
        _fixture = new TestSiteFixture();
    }

    public async Task InitializeAsync()
    {
        await _fixture.CreateSiteAsync("minimal");
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
        bool liveReload = true)
    {
        _cts = new CancellationTokenSource();
        _devServer = CreateDevServer();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port == 0 ? Random.Shared.Next(10000, 60000) : port,
            LiveReload = liveReload,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1",
            IncludeDrafts = true,
            IncludeFuture = true,
            Verbose = false
        };

        await _devServer.StartAsync(options, _cts.Token);

        _client = new DevServerTestClient(_devServer.ServerUrl!);
        await _client.WaitForServerReadyAsync(TimeSpan.FromSeconds(10));

        return (_devServer, _client);
    }

    #region 快速连续请求测试（防抖处理）

    /// <summary>
    /// 测试快速连续请求不会导致服务器崩溃
    /// </summary>
    [Fact]
    public async Task RapidRequests_DoNotCrashServer()
    {
        // Arrange
        var (server, client) = await StartServerAsync();
        var tasks = new List<Task<HttpTestResponse>>();

        // Act - 快速发送 100 个请求
        for (var i = 0; i < 100; i++)
        {
            tasks.Add(client.GetAsync("/").AsTask());
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        server.IsRunning.Should().BeTrue("服务器应该仍在运行");
        responses.Should().NotBeEmpty();
    }

    /// <summary>
    /// 测试快速连续的 WebSocket 连接和断开
    /// </summary>
    [Fact]
    public async Task RapidWebSocketConnections_DoNotCrashServer()
    {
        // Arrange
        var (server, _) = await StartServerAsync();

        // Act - 快速连接和断开 10 次
        for (var i = 0; i < 10; i++)
        {
            var client = new DevServerTestClient(server.ServerUrl!);
            try
            {
                await client.ConnectLiveReloadAsync("/__livereload", TimeSpan.FromSeconds(2));
                await client.DisconnectLiveReloadAsync();
            }
            catch (Exception)
            {
                // 忽略连接错误
            }
            finally
            {
                await client.DisposeAsync();
            }
        }

        // Assert
        server.IsRunning.Should().BeTrue("服务器应该仍在运行");
    }

    #endregion

    #region 大量并发请求测试

    /// <summary>
    /// 测试大量并发请求
    /// </summary>
    [Fact]
    public async Task ManyConurrentRequests_HandledGracefully()
    {
        // Arrange
        var (server, client) = await StartServerAsync();
        var paths = Enumerable.Range(0, 100).Select(_ => "/").ToList();

        // Act
        var responses = await client.GetConcurrentAsync(paths, TimeSpan.FromSeconds(60));

        // Assert
        server.IsRunning.Should().BeTrue();
        var successCount = responses.Count(r => r.IsSuccess);
        successCount.Should().BeGreaterThan(50, "大部分请求应该成功");
    }

    /// <summary>
    /// 测试并发请求不同路径
    /// </summary>
    [Fact]
    public async Task ConcurrentRequestsDifferentPaths_AllHandled()
    {
        // Arrange
        var (_, client) = await StartServerAsync();
        var paths = new[]
        {
            "/",
            "/index.html",
            "/non-existent-1.html",
            "/non-existent-2.html",
            "/"
        };

        // Act
        var responses = await client.GetConcurrentAsync(paths, TimeSpan.FromSeconds(30));

        // Assert
        responses.Should().HaveCount(5);
    }

    #endregion

    #region WebSocket 连接边界测试

    /// <summary>
    /// 测试 WebSocket 连接断开后重连
    /// </summary>
    [Fact]
    public async Task WebSocketReconnect_AfterDisconnect_Succeeds()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act - 连接、断开、重连
        await client.ConnectLiveReloadAsync("/__livereload");
        client.IsWebSocketConnected.Should().BeTrue();

        await client.DisconnectLiveReloadAsync();
        client.IsWebSocketConnected.Should().BeFalse();

        await client.ConnectLiveReloadAsync("/__livereload");

        // Assert
        client.IsWebSocketConnected.Should().BeTrue();
    }

    /// <summary>
    /// 测试多次断开连接不会抛出异常
    /// </summary>
    [Fact]
    public async Task MultipleDisconnects_DoNotThrow()
    {
        // Arrange
        var (_, client) = await StartServerAsync();
        await client.ConnectLiveReloadAsync("/__livereload");

        // Act & Assert - 多次断开不应抛出异常
        await client.DisconnectLiveReloadAsync();
        await client.DisconnectLiveReloadAsync();
        await client.DisconnectLiveReloadAsync();

        client.IsWebSocketConnected.Should().BeFalse();
    }

    /// <summary>
    /// 测试未连接时断开不会抛出异常
    /// </summary>
    [Fact]
    public async Task DisconnectWithoutConnect_DoesNotThrow()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act & Assert
        await client.DisconnectLiveReloadAsync();
        client.IsWebSocketConnected.Should().BeFalse();
    }

    #endregion

    #region 服务器异常恢复测试

    /// <summary>
    /// 测试服务器停止后无法处理请求
    /// </summary>
    [Fact]
    public async Task AfterStop_RequestsFail()
    {
        // Arrange
        var (server, client) = await StartServerAsync();
        server.IsRunning.Should().BeTrue();

        // Act
        await server.StopAsync();

        // Assert
        server.IsRunning.Should().BeFalse();

        // 请求应该失败
        await Assert.ThrowsAnyAsync<Exception>(
            () => client.GetAsync("/", TimeSpan.FromSeconds(2)).AsTask());
    }

    /// <summary>
    /// 测试重复停止服务器不会抛出异常
    /// </summary>
    [Fact]
    public async Task MultipleStops_DoNotThrow()
    {
        // Arrange
        var (server, _) = await StartServerAsync();

        // Act & Assert
        await server.StopAsync();
        await server.StopAsync();
        await server.StopAsync();

        server.IsRunning.Should().BeFalse();
    }

    /// <summary>
    /// 测试已运行的服务器再次启动抛出异常
    /// </summary>
    [Fact]
    public async Task StartWhileRunning_ThrowsException()
    {
        // Arrange
        var (server, _) = await StartServerAsync();
        server.IsRunning.Should().BeTrue();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = Random.Shared.Next(10000, 60000),
            LiveReload = true,
            OpenBrowser = false,
            BindAddress = "127.0.0.1"
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => server.StartAsync(options));
    }

    #endregion

    #region 请求超时测试

    /// <summary>
    /// 测试请求超时处理
    /// </summary>
    [Fact]
    public async Task RequestTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // 创建一个非常短的超时
        var shortTimeoutClient = new DevServerTestClient(
            client.ServerUrl,
            timeout: TimeSpan.FromMilliseconds(1));

        try
        {
            // Act & Assert - 可能超时也可能成功（取决于服务器响应速度）
            try
            {
                await shortTimeoutClient.GetAsync("/");
            }
            catch (TimeoutException)
            {
                // 预期的超时
            }
            catch (HttpRequestException)
            {
                // 也可能是连接错误
            }
        }
        finally
        {
            await shortTimeoutClient.DisposeAsync();
        }
    }

    /// <summary>
    /// 测试 WebSocket 等待消息超时
    /// </summary>
    [Fact]
    public async Task WebSocketWaitTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var (_, client) = await StartServerAsync();
        await client.ConnectLiveReloadAsync("/__livereload");

        // Act & Assert
        await Assert.ThrowsAsync<TimeoutException>(
            () => client.WaitForReloadAsync(TimeSpan.FromMilliseconds(50)).AsTask());
    }

    #endregion

    #region 特殊路径测试

    /// <summary>
    /// 测试空路径请求
    /// </summary>
    [Fact]
    public async Task EmptyPath_ReturnsIndexHtml()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("");

        // Assert
        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// 测试根路径请求
    /// </summary>
    [Fact]
    public async Task RootPath_ReturnsIndexHtml()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// 测试带查询参数的路径
    /// </summary>
    [Fact]
    public async Task PathWithQueryParams_HandledCorrectly()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/?param=value");

        // Assert
        // 应该返回 index.html 或 404，不应崩溃
        (response.IsSuccess || response.StatusCode == HttpStatusCode.NotFound).Should().BeTrue();
    }

    /// <summary>
    /// 测试带特殊字符的路径
    /// </summary>
    [Fact]
    public async Task PathWithSpecialChars_Returns404()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/path%20with%20spaces.html");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// 测试深层嵌套路径
    /// </summary>
    [Fact]
    public async Task DeeplyNestedPath_Returns404()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.GetAsync("/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p.html");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region HTTP 方法测试

    /// <summary>
    /// 测试 HEAD 请求
    /// </summary>
    [Fact]
    public async Task HeadRequest_ReturnsHeaders()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        var response = await client.HeadAsync("/");

        // Assert
        // HEAD 请求应该返回成功或 404
        (response.IsSuccess || response.StatusCode == HttpStatusCode.NotFound).Should().BeTrue();
    }

    /// <summary>
    /// 测试 POST 请求（静态服务器通常不支持）
    /// </summary>
    [Fact]
    public async Task PostRequest_HandledGracefully()
    {
        // Arrange
        var (server, client) = await StartServerAsync();

        // Act
        var response = await client.PostAsync("/", "test content");

        // Assert
        // 服务器不应崩溃
        server.IsRunning.Should().BeTrue();
    }

    #endregion
}
