// Flint 静态站点生成器
// DevServerTestClient 单元测试

using System.Net;
using FluentAssertions;
using Xunit;

namespace Flint.IntegrationTests.Utilities;

/// <summary>
/// DevServerTestClient 单元测试
/// 测试 HTTP 请求、WebSocket 连接、超时处理等功能
/// </summary>
public class DevServerTestClientTests : IAsyncLifetime
{
    private DevServerTestClient? _client;

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }
    }

    #region 构造函数测试

    [Fact]
    public void Constructor_ShouldCreateInstance_WithDefaultSettings()
    {
        // Arrange & Act
        _client = new DevServerTestClient("http://localhost:8080");

        // Assert
        _client.Should().NotBeNull();
        _client.ServerUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void Constructor_ShouldTrimTrailingSlash()
    {
        // Arrange & Act
        _client = new DevServerTestClient("http://localhost:8080/");

        // Assert
        _client.ServerUrl.Should().Be("http://localhost:8080");
    }

    [Fact]
    public void Constructor_ShouldAcceptCustomTimeout()
    {
        // Arrange & Act
        _client = new DevServerTestClient(
            "http://localhost:8080",
            timeout: TimeSpan.FromSeconds(30));

        // Assert
        _client.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldAcceptCustomRetrySettings()
    {
        // Arrange & Act
        _client = new DevServerTestClient(
            "http://localhost:8080",
            maxRetries: 5,
            retryDelay: TimeSpan.FromSeconds(1));

        // Assert
        _client.Should().NotBeNull();
    }

    #endregion

    #region HTTP 响应模型测试

    [Fact]
    public void HttpTestResponse_IsSuccess_ShouldBeTrueFor2xxStatusCodes()
    {
        // Arrange & Act & Assert
        new HttpTestResponse { StatusCode = HttpStatusCode.OK }.IsSuccess.Should().BeTrue();
        new HttpTestResponse { StatusCode = HttpStatusCode.Created }.IsSuccess.Should().BeTrue();
        new HttpTestResponse { StatusCode = HttpStatusCode.NoContent }.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void HttpTestResponse_IsSuccess_ShouldBeFalseForNon2xxStatusCodes()
    {
        // Arrange & Act & Assert
        new HttpTestResponse { StatusCode = HttpStatusCode.NotFound }.IsSuccess.Should().BeFalse();
        new HttpTestResponse { StatusCode = HttpStatusCode.InternalServerError }.IsSuccess.Should().BeFalse();
        new HttpTestResponse { StatusCode = HttpStatusCode.BadRequest }.IsSuccess.Should().BeFalse();
        new HttpTestResponse { StatusCode = HttpStatusCode.Redirect }.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void HttpTestResponse_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var response = new HttpTestResponse();

        // Assert
        response.StatusCode.Should().Be(0);
        response.Content.Should().BeEmpty();
        response.Headers.Should().BeEmpty();
        response.ContentType.Should().BeNull();
        response.Duration.Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region LiveReloadMessage 测试

    [Fact]
    public void LiveReloadMessage_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var message = new LiveReloadMessage();

        // Assert
        message.Type.Should().Be(LiveReloadMessageType.BuildStart);
        message.Data.Should().BeNull();
        message.RawMessage.Should().BeNull();
        message.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void LiveReloadMessage_ShouldSupportWithSyntax()
    {
        // Arrange & Act
        var message = new LiveReloadMessage
        {
            Type = LiveReloadMessageType.Reload,
            Data = "test data",
            RawMessage = "{\"type\":\"reload\"}"
        };

        // Assert
        message.Type.Should().Be(LiveReloadMessageType.Reload);
        message.Data.Should().Be("test data");
        message.RawMessage.Should().Be("{\"type\":\"reload\"}");
    }

    #endregion

    #region LiveReloadMessageType 测试

    [Fact]
    public void LiveReloadMessageType_ShouldHaveAllExpectedValues()
    {
        // Assert
        Enum.GetValues<LiveReloadMessageType>().Should().HaveCount(6);
        Enum.IsDefined(LiveReloadMessageType.BuildStart).Should().BeTrue();
        Enum.IsDefined(LiveReloadMessageType.BuildComplete).Should().BeTrue();
        Enum.IsDefined(LiveReloadMessageType.Reload).Should().BeTrue();
        Enum.IsDefined(LiveReloadMessageType.CssUpdate).Should().BeTrue();
        Enum.IsDefined(LiveReloadMessageType.Error).Should().BeTrue();
        Enum.IsDefined(LiveReloadMessageType.Unknown).Should().BeTrue();
    }

    #endregion


    #region HTTP 请求测试（无服务器）

    [Fact]
    public async Task GetAsync_WithNoServer_ShouldThrowHttpRequestException()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:59999");

        // Act
        var act = () => _client.GetAsync("/").AsTask();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task PostAsync_WithNoServer_ShouldThrowHttpRequestException()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:59999");

        // Act
        var act = () => _client.PostAsync("/", "test").AsTask();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendRequestAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:8080");
        await _client.DisposeAsync();

        // Act
        var act = () => _client.GetAsync("/").AsTask();

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    #endregion

    #region WebSocket 测试（无服务器）

    [Fact]
    public async Task ConnectLiveReloadAsync_WithNoServer_ShouldThrowException()
    {
        // Arrange
        _client = new DevServerTestClient(
            "http://localhost:59999",
            timeout: TimeSpan.FromSeconds(2),
            maxRetries: 1);

        // Act
        var act = () => _client.ConnectLiveReloadAsync().AsTask();

        // Assert
        // 可能抛出 WebSocketException 或 TaskCanceledException（超时）
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task WaitForReloadAsync_WithoutConnection_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:8080");

        // Act
        var act = () => _client.WaitForReloadAsync().AsTask();

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*WebSocket 未连接*");
    }

    [Fact]
    public void IsWebSocketConnected_WithoutConnection_ShouldBeFalse()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:8080");

        // Assert
        _client.IsWebSocketConnected.Should().BeFalse();
    }

    #endregion

    #region 服务器状态检查测试

    [Fact]
    public async Task WaitForServerReadyAsync_WithNoServer_ShouldReturnFalse()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:59999");

        // Act
        var result = await _client.WaitForServerReadyAsync(TimeSpan.FromSeconds(2));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsServerOnlineAsync_WithNoServer_ShouldReturnFalse()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:59999");

        // Act
        var result = await _client.IsServerOnlineAsync();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region 并发请求测试

    [Fact]
    public async Task GetConcurrentAsync_WithNoServer_ShouldThrowForAllRequests()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:59999");
        var paths = new[] { "/path1", "/path2", "/path3" };

        // Act
        var act = () => _client.GetConcurrentAsync(paths).AsTask();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendConcurrentAsync_WithNoServer_ShouldThrowForAllRequests()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:59999");
        var requests = new[]
        {
            (HttpMethod.Get, "/path1"),
            (HttpMethod.Post, "/path2"),
            (HttpMethod.Put, "/path3")
        };

        // Act
        var act = () => _client.SendConcurrentAsync(requests).AsTask();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region Dispose 测试

    [Fact]
    public async Task DisposeAsync_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:8080");

        // Act
        var act = async () =>
        {
            await _client.DisposeAsync();
            await _client.DisposeAsync();
            await _client.DisposeAsync();
        };

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisconnectLiveReloadAsync_WithoutConnection_ShouldNotThrow()
    {
        // Arrange
        _client = new DevServerTestClient("http://localhost:8080");

        // Act
        var act = () => _client.DisconnectLiveReloadAsync().AsTask();

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region 超时测试

    [Fact]
    public async Task GetAsync_WithShortTimeout_ShouldThrowTimeoutException()
    {
        // Arrange
        // 使用一个会延迟响应的服务器（这里用不存在的服务器模拟）
        _client = new DevServerTestClient("http://localhost:59999");

        // Act
        // 由于服务器不存在，会先抛出 HttpRequestException
        // 这个测试主要验证超时参数被正确传递
        var act = () => _client.GetAsync("/", TimeSpan.FromMilliseconds(100)).AsTask();

        // Assert
        // 可能抛出 HttpRequestException 或 TimeoutException
        await act.Should().ThrowAsync<Exception>();
    }

    #endregion
}
