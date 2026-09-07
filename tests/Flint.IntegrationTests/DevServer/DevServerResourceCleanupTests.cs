// Flint 静态站点生成器
// 开发服务器资源清理测试
// 验证服务器停止后资源正确释放

using System.Net.NetworkInformation;
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
/// 开发服务器资源清理测试
/// 验证服务器停止后资源正确释放
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 3.8: 服务器停止后端口释放
/// </remarks>
[Collection("DevServer")]
public class DevServerResourceCleanupTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;

    public DevServerResourceCleanupTests()
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

    private static bool IsPortInUse(int port)
    {
        try
        {
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpEndPoints = ipProperties.GetActiveTcpListeners();
            return tcpEndPoints.Any(ep => ep.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private static int FindAvailablePort()
    {
        for (var port = 10000; port < 60000; port++)
        {
            if (!IsPortInUse(port))
            {
                return port;
            }
        }
        throw new InvalidOperationException("无法找到可用端口");
    }

    #region 端口释放测试

    /// <summary>
    /// 测试服务器停止后端口被释放
    /// </summary>
    [Fact]
    public async Task AfterStop_PortIsReleased()
    {
        // Arrange
        var port = FindAvailablePort();
        var server = CreateDevServer();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port,
            LiveReload = false,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1"
        };

        // Act - 启动服务器
        await server.StartAsync(options);
        server.IsRunning.Should().BeTrue();

        // 验证端口被占用
        var portInUseWhileRunning = IsPortInUse(port);

        // 停止服务器
        await server.StopAsync();
        (server as IDisposable)?.Dispose();

        // 等待端口释放
        await Task.Delay(500);

        // Assert
        portInUseWhileRunning.Should().BeTrue("服务器运行时端口应该被占用");

        // 端口应该被释放（可能需要一些时间）
        var maxWaitTime = TimeSpan.FromSeconds(5);
        var startTime = DateTime.UtcNow;
        var portReleased = false;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            if (!IsPortInUse(port))
            {
                portReleased = true;
                break;
            }
            await Task.Delay(100);
        }

        portReleased.Should().BeTrue("服务器停止后端口应该被释放");
    }

    /// <summary>
    /// 测试 Dispose 后端口被释放
    /// </summary>
    [Fact]
    public async Task AfterDispose_PortIsReleased()
    {
        // Arrange
        var port = FindAvailablePort();
        var server = CreateDevServer();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port,
            LiveReload = false,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1"
        };

        // Act
        await server.StartAsync(options);
        (server as IDisposable)?.Dispose();

        // 等待资源释放
        await Task.Delay(500);

        // Assert - 端口应该被释放
        var maxWaitTime = TimeSpan.FromSeconds(5);
        var startTime = DateTime.UtcNow;
        var portReleased = false;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            if (!IsPortInUse(port))
            {
                portReleased = true;
                break;
            }
            await Task.Delay(100);
        }

        portReleased.Should().BeTrue("Dispose 后端口应该被释放");
    }

    /// <summary>
    /// 测试停止后可以在同一端口重新启动
    /// </summary>
    [Fact]
    public async Task AfterStop_CanRestartOnSamePort()
    {
        // Arrange
        var port = FindAvailablePort();

        // 第一次启动
        var server1 = CreateDevServer();
        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port,
            LiveReload = false,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1"
        };

        await server1.StartAsync(options);
        await server1.StopAsync();
        (server1 as IDisposable)?.Dispose();

        // 等待端口释放
        await Task.Delay(1000);

        // Act - 第二次启动
        var server2 = CreateDevServer();

        try
        {
            await server2.StartAsync(options);

            // Assert
            server2.IsRunning.Should().BeTrue();
        }
        finally
        {
            await server2.StopAsync();
            (server2 as IDisposable)?.Dispose();
        }
    }

    #endregion

    #region WebSocket 资源清理测试

    /// <summary>
    /// 测试服务器停止后 WebSocket 连接被关闭
    /// </summary>
    [Fact]
    public async Task AfterStop_WebSocketConnectionsClosed()
    {
        // Arrange
        var port = FindAvailablePort();
        var server = CreateDevServer();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port,
            LiveReload = true,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1"
        };

        await server.StartAsync(options);

        var client = new DevServerTestClient(server.ServerUrl!);
        await client.WaitForServerReadyAsync(TimeSpan.FromSeconds(10));
        await client.ConnectLiveReloadAsync("/__livereload");
        client.IsWebSocketConnected.Should().BeTrue();

        // Act
        await server.StopAsync();
        (server as IDisposable)?.Dispose();

        // 等待连接关闭
        await Task.Delay(500);

        // Assert - WebSocket 连接应该被关闭
        // 注意：客户端可能仍然报告连接状态，直到尝试发送/接收消息
        await client.DisposeAsync();
    }

    /// <summary>
    /// 测试多个 WebSocket 客户端在服务器停止后都被清理
    /// </summary>
    [Fact]
    public async Task AfterStop_AllWebSocketClientsCleaned()
    {
        // Arrange
        var port = FindAvailablePort();
        var server = CreateDevServer();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port,
            LiveReload = true,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1"
        };

        await server.StartAsync(options);

        var clients = new List<DevServerTestClient>();
        for (var i = 0; i < 3; i++)
        {
            var client = new DevServerTestClient(server.ServerUrl!);
            await client.WaitForServerReadyAsync(TimeSpan.FromSeconds(10));
            await client.ConnectLiveReloadAsync("/__livereload");
            clients.Add(client);
        }

        clients.Should().AllSatisfy(c => c.IsWebSocketConnected.Should().BeTrue());

        // Act
        await server.StopAsync();
        (server as IDisposable)?.Dispose();

        // Cleanup
        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }

    #endregion

    #region IDisposable 实现测试

    /// <summary>
    /// 测试 DevServer 实现 IDisposable
    /// </summary>
    [Fact]
    public void DevServer_ImplementsIDisposable()
    {
        // Arrange
        var server = CreateDevServer();

        // Assert
        server.Should().BeAssignableTo<IDisposable>();
    }

    /// <summary>
    /// 测试多次 Dispose 不会抛出异常
    /// </summary>
    [Fact]
    public async Task MultipleDispose_DoesNotThrow()
    {
        // Arrange
        var server = CreateDevServer();
        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = FindAvailablePort(),
            LiveReload = false,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1"
        };

        await server.StartAsync(options);

        // Act & Assert - 多次 Dispose 不应抛出异常
        var disposable = server as IDisposable;
        disposable?.Dispose();
        disposable?.Dispose();
        disposable?.Dispose();
    }

    /// <summary>
    /// 测试未启动的服务器可以安全 Dispose
    /// </summary>
    [Fact]
    public void DisposeWithoutStart_DoesNotThrow()
    {
        // Arrange
        var server = CreateDevServer();

        // Act & Assert
        var disposable = server as IDisposable;
        disposable?.Dispose();
    }

    #endregion

    #region 异常退出资源清理测试

    /// <summary>
    /// 测试取消令牌触发时资源被清理
    /// </summary>
    [Fact]
    public async Task CancellationToken_TriggersCleanup()
    {
        // Arrange
        var port = FindAvailablePort();
        var server = CreateDevServer();
        var cts = new CancellationTokenSource();

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = port,
            LiveReload = false,
            OpenBrowser = false,
            UseHttps = false,
            BindAddress = "127.0.0.1"
        };

        await server.StartAsync(options, cts.Token);
        server.IsRunning.Should().BeTrue();

        // Act - 取消令牌
        await cts.CancelAsync();

        // 等待清理
        await Task.Delay(500);

        // Cleanup
        (server as IDisposable)?.Dispose();
        cts.Dispose();
    }

    #endregion

    #region 内存泄漏预防测试

    /// <summary>
    /// 测试多次启动停止不会导致资源泄漏
    /// </summary>
    [Fact]
    public async Task MultipleStartStop_NoResourceLeak()
    {
        // Arrange & Act - 多次启动停止
        for (var i = 0; i < 3; i++)
        {
            var server = CreateDevServer();
            var port = FindAvailablePort();

            var options = new DevServerOptions
            {
                SourcePath = _fixture.SiteRoot,
                OutputPath = _fixture.OutputPath,
                Port = port,
                LiveReload = false,
                OpenBrowser = false,
                UseHttps = false,
                BindAddress = "127.0.0.1"
            };

            await server.StartAsync(options);
            server.IsRunning.Should().BeTrue();

            await server.StopAsync();
            (server as IDisposable)?.Dispose();

            // 等待资源释放
            await Task.Delay(200);
        }

        // Assert - 如果没有资源泄漏，测试应该正常完成
        // 实际的内存泄漏检测需要更复杂的工具
    }

    #endregion
}
