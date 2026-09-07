// Flint 静态站点生成器
// 开发服务器测试客户端
// 提供与开发服务器交互的功能

using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Flint.IntegrationTests.Utilities;

/// <summary>
/// 热重载消息类型
/// </summary>
public enum LiveReloadMessageType
{
    /// <summary>
    /// 构建开始
    /// </summary>
    BuildStart,

    /// <summary>
    /// 构建完成
    /// </summary>
    BuildComplete,

    /// <summary>
    /// 页面刷新
    /// </summary>
    Reload,

    /// <summary>
    /// CSS 更新
    /// </summary>
    CssUpdate,

    /// <summary>
    /// 错误
    /// </summary>
    Error,

    /// <summary>
    /// 未知类型
    /// </summary>
    Unknown
}

/// <summary>
/// 热重载消息
/// </summary>
public sealed record LiveReloadMessage
{
    /// <summary>
    /// 消息类型
    /// </summary>
    public LiveReloadMessageType Type { get; init; }

    /// <summary>
    /// 消息数据
    /// </summary>
    public string? Data { get; init; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 原始消息
    /// </summary>
    public string? RawMessage { get; init; }
}


/// <summary>
/// HTTP 响应结果
/// </summary>
public sealed record HttpTestResponse
{
    /// <summary>
    /// HTTP 状态码
    /// </summary>
    public HttpStatusCode StatusCode { get; init; }

    /// <summary>
    /// 响应内容
    /// </summary>
    public string Content { get; init; } = "";

    /// <summary>
    /// 响应头
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Content-Type 头
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// 响应时间
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// 是否成功（2xx 状态码）
    /// </summary>
    public bool IsSuccess => (int)StatusCode >= 200 && (int)StatusCode < 300;
}

/// <summary>
/// 开发服务器测试客户端
/// 提供与开发服务器交互的功能
/// </summary>
public sealed class DevServerTestClient : IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly TimeSpan _defaultTimeout;
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private ClientWebSocket? _webSocket;
    private bool _disposed;

    /// <summary>
    /// 默认超时时间（10秒）
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 默认重试次数
    /// </summary>
    public const int DefaultMaxRetries = 3;

    /// <summary>
    /// 默认重试延迟
    /// </summary>
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 创建开发服务器测试客户端
    /// </summary>
    /// <param name="baseUri">服务器基础 URI</param>
    /// <param name="timeout">默认超时时间</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="retryDelay">重试延迟</param>
    public DevServerTestClient(
        Uri baseUri,
        TimeSpan? timeout = null,
        int maxRetries = DefaultMaxRetries,
        TimeSpan? retryDelay = null)
    {
        _baseUri = baseUri;
        _defaultTimeout = timeout ?? DefaultTimeout;
        _maxRetries = maxRetries;
        _retryDelay = retryDelay ?? DefaultRetryDelay;

        _httpClient = new HttpClient
        {
            BaseAddress = _baseUri,
            Timeout = _defaultTimeout
        };
    }

    /// <summary>
    /// 创建开发服务器测试客户端（使用字符串 URL）
    /// </summary>
    /// <param name="baseUrl">服务器基础 URL</param>
    /// <param name="timeout">默认超时时间</param>
    /// <param name="maxRetries">最大重试次数</param>
    /// <param name="retryDelay">重试延迟</param>
    public DevServerTestClient(
        string baseUrl,
        TimeSpan? timeout = null,
        int maxRetries = DefaultMaxRetries,
        TimeSpan? retryDelay = null)
        : this(new Uri(baseUrl.TrimEnd('/')), timeout, maxRetries, retryDelay)
    {
    }

    /// <summary>
    /// 服务器 URI
    /// </summary>
    public Uri ServerUri => _baseUri;

    /// <summary>
    /// 服务器 URL（字符串形式）
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "提供便捷的字符串访问")]
    public string ServerUrl => _baseUri.ToString().TrimEnd('/');

    /// <summary>
    /// WebSocket 是否已连接
    /// </summary>
    public bool IsWebSocketConnected =>
        _webSocket?.State == WebSocketState.Open;


    #region HTTP 请求方法

    /// <summary>
    /// 发送 HTTP GET 请求
    /// </summary>
    /// <param name="path">请求路径</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    public async ValueTask<HttpTestResponse> GetAsync(
        string path,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync(HttpMethod.Get, path, null, timeout, cancellationToken);
    }

    /// <summary>
    /// 发送 HTTP POST 请求
    /// </summary>
    /// <param name="path">请求路径</param>
    /// <param name="content">请求内容</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    public async ValueTask<HttpTestResponse> PostAsync(
        string path,
        string? content = null,
        string contentType = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        HttpContent? httpContent = null;
        if (content != null)
        {
            httpContent = new StringContent(content, Encoding.UTF8, contentType);
        }
        return await SendRequestAsync(HttpMethod.Post, path, httpContent, timeout, cancellationToken);
    }

    /// <summary>
    /// 发送 HTTP PUT 请求
    /// </summary>
    /// <param name="path">请求路径</param>
    /// <param name="content">请求内容</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    public async ValueTask<HttpTestResponse> PutAsync(
        string path,
        string? content = null,
        string contentType = "application/json",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        HttpContent? httpContent = null;
        if (content != null)
        {
            httpContent = new StringContent(content, Encoding.UTF8, contentType);
        }
        return await SendRequestAsync(HttpMethod.Put, path, httpContent, timeout, cancellationToken);
    }

    /// <summary>
    /// 发送 HTTP DELETE 请求
    /// </summary>
    /// <param name="path">请求路径</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    public async ValueTask<HttpTestResponse> DeleteAsync(
        string path,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync(HttpMethod.Delete, path, null, timeout, cancellationToken);
    }

    /// <summary>
    /// 发送 HTTP HEAD 请求
    /// </summary>
    /// <param name="path">请求路径</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    public async ValueTask<HttpTestResponse> HeadAsync(
        string path,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync(HttpMethod.Head, path, null, timeout, cancellationToken);
    }

    /// <summary>
    /// 发送自定义 HTTP 请求
    /// </summary>
    /// <param name="method">HTTP 方法</param>
    /// <param name="path">请求路径</param>
    /// <param name="content">请求内容（可选）</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    public async ValueTask<HttpTestResponse> SendRequestAsync(
        HttpMethod method,
        string path,
        HttpContent? content = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout ?? _defaultTimeout;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (content != null)
            {
                request.Content = content;
            }

            using var response = await _httpClient.SendAsync(request, cts.Token);
            stopwatch.Stop();

            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);
            var headers = response.Headers
                .Concat(response.Content.Headers)
                .ToDictionary(
                    h => h.Key,
                    h => string.Join(", ", h.Value));

            return new HttpTestResponse
            {
                StatusCode = response.StatusCode,
                Content = responseContent,
                Headers = headers,
                ContentType = response.Content.Headers.ContentType?.ToString(),
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            throw new TimeoutException($"请求超时: {path} ({effectiveTimeout.TotalSeconds:F1}秒)");
        }
    }

    #endregion


    #region WebSocket 方法

    /// <summary>
    /// 连接 WebSocket 热重载端点
    /// </summary>
    /// <param name="path">WebSocket 路径（默认 /livereload）</param>
    /// <param name="timeout">连接超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async ValueTask ConnectLiveReloadAsync(
        string path = "/livereload",
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket != null)
        {
            await DisconnectLiveReloadAsync();
        }

        var effectiveTimeout = timeout ?? _defaultTimeout;
        var wsUrl = ServerUrl.Replace("http://", "ws://").Replace("https://", "wss://") + path;

        _webSocket = new ClientWebSocket();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        var retryCount = 0;
        while (retryCount < _maxRetries)
        {
            try
            {
                await _webSocket.ConnectAsync(new Uri(wsUrl), cts.Token);
                return;
            }
            catch (WebSocketException) when (retryCount < _maxRetries - 1)
            {
                retryCount++;
                await Task.Delay(_retryDelay, cts.Token);
                _webSocket.Dispose();
                _webSocket = new ClientWebSocket();
            }
        }

        throw new WebSocketException($"无法连接到 WebSocket: {wsUrl}");
    }

    /// <summary>
    /// 断开 WebSocket 连接
    /// </summary>
    public async ValueTask DisconnectLiveReloadAsync()
    {
        if (_webSocket == null)
            return;

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "测试完成",
                    CancellationToken.None);
            }
        }
        catch
        {
            // 忽略关闭时的错误
        }
        finally
        {
            _webSocket.Dispose();
            _webSocket = null;
        }
    }

    /// <summary>
    /// 等待热重载通知
    /// </summary>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>热重载消息</returns>
    public async ValueTask<LiveReloadMessage> WaitForReloadAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket 未连接，请先调用 ConnectLiveReloadAsync");
        }

        var effectiveTimeout = timeout ?? _defaultTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();

        try
        {
            while (true)
            {
                var result = await _webSocket.ReceiveAsync(buffer, cts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("WebSocket 连接已关闭");
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var rawMessage = messageBuilder.ToString();
                    return ParseLiveReloadMessage(rawMessage);
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"等待热重载消息超时 ({effectiveTimeout.TotalSeconds:F1}秒)");
        }
    }

    /// <summary>
    /// 等待指定类型的热重载通知
    /// </summary>
    /// <param name="expectedType">期望的消息类型</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>热重载消息</returns>
    public async ValueTask<LiveReloadMessage> WaitForReloadTypeAsync(
        LiveReloadMessageType expectedType,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? _defaultTimeout;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            var remainingTime = effectiveTimeout - stopwatch.Elapsed;
            if (remainingTime <= TimeSpan.Zero)
                break;

            var message = await WaitForReloadAsync(remainingTime, cancellationToken);
            if (message.Type == expectedType)
            {
                return message;
            }
        }

        throw new TimeoutException($"等待 {expectedType} 消息超时 ({effectiveTimeout.TotalSeconds:F1}秒)");
    }

    /// <summary>
    /// 解析热重载消息
    /// </summary>
    private static LiveReloadMessage ParseLiveReloadMessage(string rawMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;

            var typeStr = root.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString()
                : null;

            var data = root.TryGetProperty("data", out var dataProp)
                ? dataProp.ToString()
                : null;

            var type = typeStr?.ToLowerInvariant() switch
            {
                "buildstart" or "build_start" => LiveReloadMessageType.BuildStart,
                "buildcomplete" or "build_complete" => LiveReloadMessageType.BuildComplete,
                "reload" => LiveReloadMessageType.Reload,
                "cssupdate" or "css_update" or "css" => LiveReloadMessageType.CssUpdate,
                "error" => LiveReloadMessageType.Error,
                _ => LiveReloadMessageType.Unknown
            };

            return new LiveReloadMessage
            {
                Type = type,
                Data = data,
                Timestamp = DateTimeOffset.UtcNow,
                RawMessage = rawMessage
            };
        }
        catch
        {
            return new LiveReloadMessage
            {
                Type = LiveReloadMessageType.Unknown,
                RawMessage = rawMessage,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }

    #endregion


    #region 并发请求方法

    /// <summary>
    /// 并发发送多个 GET 请求
    /// </summary>
    /// <param name="paths">请求路径列表</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应列表</returns>
    public async ValueTask<IReadOnlyList<HttpTestResponse>> GetConcurrentAsync(
        IEnumerable<string> paths,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = paths.Select(path => GetAsync(path, timeout, cancellationToken).AsTask());
        var results = await Task.WhenAll(tasks);
        return results;
    }

    /// <summary>
    /// 并发发送多个自定义请求
    /// </summary>
    /// <param name="requests">请求列表（方法和路径）</param>
    /// <param name="timeout">超时时间（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应列表</returns>
    public async ValueTask<IReadOnlyList<HttpTestResponse>> SendConcurrentAsync(
        IEnumerable<(HttpMethod Method, string Path)> requests,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = requests.Select(r =>
            SendRequestAsync(r.Method, r.Path, null, timeout, cancellationToken).AsTask());
        var results = await Task.WhenAll(tasks);
        return results;
    }

    #endregion

    #region 服务器状态检查

    /// <summary>
    /// 等待服务器就绪
    /// </summary>
    /// <param name="timeout">超时时间</param>
    /// <param name="checkPath">检查路径（默认 /）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器是否就绪</returns>
    public async ValueTask<bool> WaitForServerReadyAsync(
        TimeSpan? timeout = null,
        string checkPath = "/",
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < effectiveTimeout)
        {
            try
            {
                var response = await GetAsync(checkPath, TimeSpan.FromSeconds(2), cancellationToken);
                if (response.IsSuccess || response.StatusCode == HttpStatusCode.NotFound)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // 服务器尚未就绪，继续重试
            }
            catch (TimeoutException)
            {
                // 请求超时，继续重试
            }

            await Task.Delay(_retryDelay, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// 检查服务器是否在线
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器是否在线</returns>
    public async ValueTask<bool> IsServerOnlineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await GetAsync("/", TimeSpan.FromSeconds(2), cancellationToken);
            return response.StatusCode != HttpStatusCode.ServiceUnavailable;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region IAsyncDisposable

    /// <summary>
    /// 释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        await DisconnectLiveReloadAsync();
        _httpClient.Dispose();
    }

    #endregion
}
