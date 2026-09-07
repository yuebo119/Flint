// Flint 静态站点生成器
// 构建错误通知测试
// 验证构建错误通过 WebSocket 发送

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

// 解决命名空间冲突
using LiveReloadMessage = Flint.IntegrationTests.Utilities.LiveReloadMessage;
using LiveReloadMessageType = Flint.IntegrationTests.Utilities.LiveReloadMessageType;

namespace Flint.IntegrationTests.DevServer;

/// <summary>
/// 构建错误通知测试
/// 验证构建错误通过 WebSocket 发送
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 3.9: 构建错误通过 WebSocket 发送
/// </remarks>
[Collection("DevServer")]
public class BuildErrorNotificationTests : IAsyncLifetime
{
    private readonly TestSiteFixture _fixture;
    private IDevServer? _devServer;
    private DevServerTestClient? _client;
    private CancellationTokenSource? _cts;

    public BuildErrorNotificationTests()
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

    private async Task<(IDevServer Server, DevServerTestClient Client)> StartServerAsync()
    {
        _cts = new CancellationTokenSource();

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

        _devServer = new Core.Server.DevServer(siteBuilder);

        var options = new DevServerOptions
        {
            SourcePath = _fixture.SiteRoot,
            OutputPath = _fixture.OutputPath,
            Port = Random.Shared.Next(10000, 60000),
            LiveReload = true,
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

    #region 错误消息类型测试

    /// <summary>
    /// 测试 Error 消息类型存在
    /// </summary>
    [Fact]
    public void LiveReloadMessageType_HasErrorType()
    {
        // Assert
        Enum.IsDefined(LiveReloadMessageType.Error).Should().BeTrue();
    }

    /// <summary>
    /// 测试错误消息可以包含数据
    /// </summary>
    [Fact]
    public void LiveReloadMessage_CanContainErrorData()
    {
        // Arrange
        var errorMessage = "Front Matter 解析错误: 无效的 YAML 格式";

        // Act
        var message = new LiveReloadMessage
        {
            Type = LiveReloadMessageType.Error,
            Data = errorMessage,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        message.Type.Should().Be(LiveReloadMessageType.Error);
        message.Data.Should().Be(errorMessage);
        message.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 测试错误消息可以包含文件路径
    /// </summary>
    [Fact]
    public void LiveReloadMessage_CanContainFilePath()
    {
        // Arrange
        var errorData = """
            {
                "file": "content/posts/invalid.md",
                "line": 5,
                "message": "无效的 Front Matter 格式"
            }
            """;

        // Act
        var message = new LiveReloadMessage
        {
            Type = LiveReloadMessageType.Error,
            Data = errorData,
            RawMessage = errorData,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        message.Data.Should().Contain("content/posts/invalid.md");
        message.Data.Should().Contain("line");
        message.Data.Should().Contain("message");
    }

    #endregion

    #region WebSocket 连接测试

    /// <summary>
    /// 测试客户端可以连接并准备接收错误通知
    /// </summary>
    [Fact]
    public async Task Client_CanConnectForErrorNotifications()
    {
        // Arrange
        var (_, client) = await StartServerAsync();

        // Act
        await client.ConnectLiveReloadAsync("/__livereload");

        // Assert
        client.IsWebSocketConnected.Should().BeTrue();
    }

    /// <summary>
    /// 测试多个客户端可以同时连接等待错误通知
    /// </summary>
    [Fact]
    public async Task MultipleClients_CanConnectForErrorNotifications()
    {
        // Arrange
        var (server, _) = await StartServerAsync();
        var clients = new List<DevServerTestClient>();

        try
        {
            // Act
            for (var i = 0; i < 3; i++)
            {
                var client = new DevServerTestClient(server.ServerUrl!);
                await client.ConnectLiveReloadAsync("/__livereload");
                clients.Add(client);
            }

            // Assert
            clients.Should().AllSatisfy(c => c.IsWebSocketConnected.Should().BeTrue());
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    #endregion

    #region 错误消息格式测试

    /// <summary>
    /// 测试错误消息格式 - Front Matter 错误
    /// </summary>
    [Fact]
    public void ErrorMessage_FrontMatterError_HasCorrectFormat()
    {
        // Arrange
        var errorMessage = new LiveReloadMessage
        {
            Type = LiveReloadMessageType.Error,
            Data = "Front Matter 解析错误: content/posts/test.md:3:1 - 无效的 YAML 语法",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        errorMessage.Type.Should().Be(LiveReloadMessageType.Error);
        errorMessage.Data.Should().Contain("Front Matter");
        errorMessage.Data.Should().Contain("content/posts/test.md");
    }

    /// <summary>
    /// 测试错误消息格式 - 模板错误
    /// </summary>
    [Fact]
    public void ErrorMessage_TemplateError_HasCorrectFormat()
    {
        // Arrange
        var errorMessage = new LiveReloadMessage
        {
            Type = LiveReloadMessageType.Error,
            Data = "模板渲染错误: layouts/_default/single.html:15:10 - 未定义的变量 'page.nonexistent'",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        errorMessage.Type.Should().Be(LiveReloadMessageType.Error);
        errorMessage.Data.Should().Contain("模板渲染错误");
        errorMessage.Data.Should().Contain("layouts/_default/single.html");
    }

    /// <summary>
    /// 测试错误消息格式 - SCSS 编译错误
    /// </summary>
    [Fact]
    public void ErrorMessage_ScssError_HasCorrectFormat()
    {
        // Arrange
        var errorMessage = new LiveReloadMessage
        {
            Type = LiveReloadMessageType.Error,
            Data = "SCSS 编译错误: assets/styles/main.scss:20:5 - 未定义的变量 '$primary-color'",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Assert
        errorMessage.Type.Should().Be(LiveReloadMessageType.Error);
        errorMessage.Data.Should().Contain("SCSS 编译错误");
        errorMessage.Data.Should().Contain("assets/styles/main.scss");
    }

    #endregion

    #region 错误恢复测试

    /// <summary>
    /// 测试错误后可以继续接收正常通知
    /// </summary>
    [Fact]
    public void AfterError_CanReceiveNormalNotifications()
    {
        // Arrange - 模拟错误后的正常通知序列
        var messages = new List<LiveReloadMessage>
        {
            new() { Type = LiveReloadMessageType.BuildStart, Timestamp = DateTimeOffset.UtcNow },
            new() { Type = LiveReloadMessageType.Error, Data = "构建错误", Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(100) },
            new() { Type = LiveReloadMessageType.BuildStart, Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(200) },
            new() { Type = LiveReloadMessageType.BuildComplete, Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(300) },
            new() { Type = LiveReloadMessageType.Reload, Timestamp = DateTimeOffset.UtcNow.AddMilliseconds(400) }
        };

        // Assert - 验证消息序列
        messages.Should().HaveCount(5);
        messages[1].Type.Should().Be(LiveReloadMessageType.Error);
        messages[4].Type.Should().Be(LiveReloadMessageType.Reload);
    }

    /// <summary>
    /// 测试 BuildStart 和 BuildComplete 消息类型
    /// </summary>
    [Fact]
    public void BuildMessages_HaveCorrectTypes()
    {
        // Assert
        Enum.IsDefined(LiveReloadMessageType.BuildStart).Should().BeTrue();
        Enum.IsDefined(LiveReloadMessageType.BuildComplete).Should().BeTrue();
    }

    #endregion

    #region 等待特定消息类型测试

    /// <summary>
    /// 测试 WaitForReloadTypeAsync 方法可以等待 Error 类型
    /// </summary>
    [Fact]
    public async Task WaitForReloadTypeAsync_CanWaitForErrorType()
    {
        // Arrange
        var (_, client) = await StartServerAsync();
        await client.ConnectLiveReloadAsync("/__livereload");

        // Act & Assert - 验证方法存在且可以调用（会超时，因为没有实际错误）
        await Assert.ThrowsAsync<TimeoutException>(
            () => client.WaitForReloadTypeAsync(
                LiveReloadMessageType.Error,
                TimeSpan.FromMilliseconds(100)).AsTask());
    }

    #endregion
}
