// Flint 静态站点生成器
// 热重载通知器单元测试

#pragma warning disable CA2000 // 测试代码中的 Dispose 警告

using System.Net.WebSockets;
using Flint.Core.Server;
using Xunit;

namespace Flint.Core.Tests.Server;

/// <summary>
/// LiveReloadNotifier 单元测试
/// </summary>
public class LiveReloadNotifierTests : IDisposable
{
    private readonly LiveReloadNotifier _notifier;

    public LiveReloadNotifierTests()
    {
        _notifier = new LiveReloadNotifier();
    }

    public void Dispose()
    {
        _notifier.Dispose();
        GC.SuppressFinalize(this);
    }

    #region 客户端管理测试

    [Fact]
    public void ConnectedClients_初始应该为零()
    {
        // Assert
        Assert.Equal(0, _notifier.ConnectedClients);
    }

    [Fact]
    public void AddClient_应该增加连接数()
    {
        // Arrange
        var mockSocket = new MockWebSocket();

        // Act
        _notifier.AddClient("client1", mockSocket);

        // Assert
        Assert.Equal(1, _notifier.ConnectedClients);
    }

    [Fact]
    public void AddClient_多个客户端应该正确计数()
    {
        // Arrange & Act
        _notifier.AddClient("client1", new MockWebSocket());
        _notifier.AddClient("client2", new MockWebSocket());
        _notifier.AddClient("client3", new MockWebSocket());

        // Assert
        Assert.Equal(3, _notifier.ConnectedClients);
    }

    [Fact]
    public void RemoveClient_应该减少连接数()
    {
        // Arrange
        _notifier.AddClient("client1", new MockWebSocket());
        _notifier.AddClient("client2", new MockWebSocket());

        // Act
        _notifier.RemoveClient("client1");

        // Assert
        Assert.Equal(1, _notifier.ConnectedClients);
    }

    [Fact]
    public void RemoveClient_移除不存在的客户端不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        _notifier.RemoveClient("nonexistent");
    }

    #endregion

    #region 通知测试

    [Fact]
    public async Task NotifyReloadAsync_无客户端时不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        await _notifier.NotifyReloadAsync();
    }

    [Fact]
    public async Task NotifyCssUpdateAsync_无客户端时不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        await _notifier.NotifyCssUpdateAsync("/styles/main.css");
    }

    [Fact]
    public async Task NotifyJsUpdateAsync_无客户端时不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        await _notifier.NotifyJsUpdateAsync("/scripts/app.js");
    }

    [Fact]
    public async Task NotifyErrorAsync_无客户端时不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        await _notifier.NotifyErrorAsync("构建错误");
    }

    [Fact]
    public async Task NotifyBuildStartAsync_无客户端时不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        await _notifier.NotifyBuildStartAsync();
    }

    [Fact]
    public async Task NotifyBuildCompleteAsync_无客户端时不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        await _notifier.NotifyBuildCompleteAsync(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task NotifyReloadAsync_应该向所有客户端发送消息()
    {
        // Arrange
        var socket1 = new MockWebSocket();
        var socket2 = new MockWebSocket();
        _notifier.AddClient("client1", socket1);
        _notifier.AddClient("client2", socket2);

        // Act
        await _notifier.NotifyReloadAsync();

        // Assert
        Assert.True(socket1.MessagesSent > 0);
        Assert.True(socket2.MessagesSent > 0);
    }

    [Fact]
    public async Task NotifyReloadAsync_应该清理断开的客户端()
    {
        // Arrange
        var openSocket = new MockWebSocket();
        var closedSocket = new MockWebSocket();
        closedSocket.SetState(WebSocketState.Closed);
        _notifier.AddClient("open", openSocket);
        _notifier.AddClient("closed", closedSocket);

        // Act
        await _notifier.NotifyReloadAsync();

        // Assert - 断开的客户端应该被移除
        Assert.Equal(1, _notifier.ConnectedClients);
    }

    #endregion

    #region Dispose 测试

    [Fact]
    public void Dispose_应该清理所有客户端()
    {
        // Arrange
        _notifier.AddClient("client1", new MockWebSocket());
        _notifier.AddClient("client2", new MockWebSocket());

        // Act
        _notifier.Dispose();

        // Assert
        Assert.Equal(0, _notifier.ConnectedClients);
    }

    [Fact]
    public void Dispose_多次调用不应抛出异常()
    {
        // Act & Assert - 不应抛出异常
        _notifier.Dispose();
        _notifier.Dispose();
    }

    #endregion
}

/// <summary>
/// LiveReloadScript 单元测试
/// </summary>
public class LiveReloadScriptTests
{
    [Fact]
    public void Generate_应该生成包含WebSocket连接的脚本()
    {
        // Arrange
        var wsUrl = "ws://localhost:8080/livereload";

        // Act
        var script = LiveReloadScript.Generate(wsUrl);

        // Assert
        Assert.Contains(wsUrl, script);
        Assert.Contains("WebSocket", script);
        Assert.Contains("onmessage", script);
    }

    [Fact]
    public void Generate_应该包含reload处理逻辑()
    {
        // Act
        var script = LiveReloadScript.Generate("ws://localhost/lr");

        // Assert
        Assert.Contains("reload", script);
        Assert.Contains("location.reload()", script);
    }

    [Fact]
    public void Generate_应该包含CSS热更新逻辑()
    {
        // Act
        var script = LiveReloadScript.Generate("ws://localhost/lr");

        // Assert
        Assert.Contains("css", script);
        Assert.Contains("reloadCss", script);
    }

    [Fact]
    public void Generate_应该包含错误显示逻辑()
    {
        // Act
        var script = LiveReloadScript.Generate("ws://localhost/lr");

        // Assert
        Assert.Contains("error", script);
        Assert.Contains("showError", script);
    }

    [Fact]
    public void Generate_应该包含构建状态显示逻辑()
    {
        // Act
        var script = LiveReloadScript.Generate("ws://localhost/lr");

        // Assert
        Assert.Contains("building", script);
        Assert.Contains("built", script);
    }
}

/// <summary>
/// LiveReloadMessage 单元测试
/// </summary>
public class LiveReloadMessageTests
{
    [Fact]
    public void LiveReloadMessage_应该正确设置属性()
    {
        // Arrange & Act
        var message = new LiveReloadMessage
        {
            Type = "reload",
            Path = "/test/path",
            Error = "测试错误",
            Duration = 100.5,
            Timestamp = DateTimeOffset.Now
        };

        // Assert
        Assert.Equal("reload", message.Type);
        Assert.Equal("/test/path", message.Path);
        Assert.Equal("测试错误", message.Error);
        Assert.Equal(100.5, message.Duration);
    }

    [Fact]
    public void LiveReloadMessage_可选属性可以为null()
    {
        // Arrange & Act
        var message = new LiveReloadMessage
        {
            Type = "reload",
            Timestamp = DateTimeOffset.Now
        };

        // Assert
        Assert.Null(message.Path);
        Assert.Null(message.Error);
        Assert.Null(message.Duration);
    }
}

/// <summary>
/// 模拟 WebSocket 用于测试
/// </summary>
#pragma warning disable CA1852 // 测试类不需要密封
internal class MockWebSocket : WebSocket
#pragma warning restore CA1852
{
    public int MessagesSent { get; private set; }
    private WebSocketState _state = WebSocketState.Open;

    public override WebSocketCloseStatus? CloseStatus => null;
    public override string? CloseStatusDescription => null;
    public override string? SubProtocol => null;
    public override WebSocketState State => _state;

    public void SetState(WebSocketState state) => _state = state;

    public override void Abort() { }

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override void Dispose() { }

    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        MessagesSent++;
        return Task.CompletedTask;
    }
}
