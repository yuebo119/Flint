// Flint 静态站点生成器
// 热重载通知器实现

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flint.Core.Abstractions;

namespace Flint.Core.Server;

/// <summary>
/// 热重载通知器
/// 通过 WebSocket 通知浏览器刷新
/// </summary>
public sealed class LiveReloadNotifier : ILiveReloadNotifier, IDisposable
{
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
    // WebSocket 不允许对同一 socket 并发 SendAsync——广播必须串行化，
    // 否则并发通知互相抛 InvalidOperationException，健康客户端被误标 dead 移除
    private readonly SemaphoreSlim _broadcastLock = new(1, 1);
    private bool _isDisposed;

    /// <inheritdoc />
    public int ConnectedClients => _clients.Count;

    /// <summary>
    /// 添加 WebSocket 客户端
    /// </summary>
    public void AddClient(string clientId, WebSocket webSocket)
    {
        _clients.TryAdd(clientId, webSocket);
    }

    /// <summary>
    /// 移除 WebSocket 客户端
    /// </summary>
    public void RemoveClient(string clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    /// <inheritdoc />
    public async ValueTask NotifyReloadAsync(CancellationToken cancellationToken = default)
    {
        var message = new LiveReloadMessage
        {
            Type = "reload",
            Timestamp = DateTimeOffset.Now
        };

        await BroadcastAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask NotifyCssUpdateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var message = new LiveReloadMessage
        {
            Type = "css",
            Path = path,
            Timestamp = DateTimeOffset.Now
        };

        await BroadcastAsync(message, cancellationToken);
    }


    /// <summary>
    /// 通知 JavaScript 更新
    /// </summary>
    public async ValueTask NotifyJsUpdateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var message = new LiveReloadMessage
        {
            Type = "js",
            Path = path,
            Timestamp = DateTimeOffset.Now
        };

        await BroadcastAsync(message, cancellationToken);
    }

    /// <summary>
    /// 通知所有客户端服务器即将停止。
    /// 客户端收到后停止断线重连刷新，避免 dev server 关闭后浏览器陷入刷新循环
    /// </summary>
    public async ValueTask NotifyServerStoppingAsync(
        CancellationToken cancellationToken = default)
    {
        var message = new LiveReloadMessage
        {
            Type = "server-stopping",
            Timestamp = DateTimeOffset.Now
        };

        await BroadcastAsync(message, cancellationToken);
    }

    /// <summary>
    /// 通知构建错误
    /// </summary>
    public async ValueTask NotifyErrorAsync(
        string error,
        CancellationToken cancellationToken = default)
    {
        var message = new LiveReloadMessage
        {
            Type = "error",
            Error = error,
            Timestamp = DateTimeOffset.Now
        };

        await BroadcastAsync(message, cancellationToken);
    }

    /// <summary>
    /// 通知构建开始
    /// </summary>
    public async ValueTask NotifyBuildStartAsync(CancellationToken cancellationToken = default)
    {
        var message = new LiveReloadMessage
        {
            Type = "building",
            Timestamp = DateTimeOffset.Now
        };

        await BroadcastAsync(message, cancellationToken);
    }

    /// <summary>
    /// 通知构建完成
    /// </summary>
    public async ValueTask NotifyBuildCompleteAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var message = new LiveReloadMessage
        {
            Type = "built",
            Duration = duration.TotalMilliseconds,
            Timestamp = DateTimeOffset.Now
        };

        await BroadcastAsync(message, cancellationToken);
    }

    private async Task BroadcastAsync(
        LiveReloadMessage message,
        CancellationToken cancellationToken)
    {
        // 使用 source-gen 序列化上下文（AOT/trim 安全；反射序列化在 NativeAOT 下会运行时失败）
        var json = JsonSerializer.Serialize(message, LiveReloadJsonContext.Default.LiveReloadMessage);
        var bytes = Encoding.UTF8.GetBytes(json);
        var segment = new ArraySegment<byte>(bytes);

        await _broadcastLock.WaitAsync(cancellationToken);
        try
        {
            var deadClients = new List<string>();

            foreach (var (clientId, socket) in _clients)
            {
                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        await socket.SendAsync(
                            segment,
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            cancellationToken);
                    }
                    else
                    {
                        deadClients.Add(clientId);
                    }
                }
                catch
                {
                    deadClients.Add(clientId);
                }
            }

            // 清理断开的客户端
            foreach (var clientId in deadClients)
            {
                _clients.TryRemove(clientId, out _);
            }
        }
        finally
        {
            _broadcastLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        foreach (var (_, socket) in _clients)
        {
            try
            {
                socket.Dispose();
            }
            catch
            {
                // 忽略清理错误
            }
        }

        _clients.Clear();
        _broadcastLock.Dispose();
    }
}

/// <summary>
/// 热重载消息
/// </summary>
public sealed class LiveReloadMessage
{
    /// <summary>
    /// 消息类型: reload, css, js, error, building, built
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 文件路径（用于 CSS/JS 更新）
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// 构建耗时（毫秒）
    /// </summary>
    public double? Duration { get; init; }

    /// <summary>
    /// 时间戳
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// 热重载消息的 JSON 序列化上下文（source-gen，AOT/trim 友好）
/// </summary>
[JsonSerializable(typeof(LiveReloadMessage))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class LiveReloadJsonContext : JsonSerializerContext
{
}

/// <summary>
/// 热重载客户端脚本生成器
/// </summary>
public static class LiveReloadScript
{
    /// <summary>
    /// 生成热重载客户端脚本
    /// </summary>
    /// <param name="wsUrl">WebSocket URL</param>
    /// <returns>JavaScript 脚本</returns>
    public static string Generate(string wsUrl)
    {
        return $@"
(function() {{
    var ws = new WebSocket('{wsUrl}');
    var overlay = null;
    var serverStopping = false;

    ws.onmessage = function(event) {{
        var msg = JSON.parse(event.data);

        switch(msg.type) {{
            case 'reload':
                location.reload();
                break;
            case 'css':
                reloadCss(msg.path);
                break;
            case 'js':
                location.reload();
                break;
            case 'error':
                showError(msg.error);
                break;
            case 'building':
                showBuilding();
                break;
            case 'built':
                hideOverlay();
                break;
            case 'server-stopping':
                serverStopping = true;
                showServerStopping();
                break;
        }}
    }};

    ws.onclose = function() {{
        if (serverStopping) {{
            console.log('LiveReload: server stopped');
            return;
        }}
        console.log('LiveReload disconnected, retrying...');
        setTimeout(function() {{ location.reload(); }}, 2000);
    }};
    
    function reloadCss(path) {{
        var links = document.querySelectorAll('link[rel=""stylesheet""]');
        links.forEach(function(link) {{
            if (link.href.includes(path)) {{
                var newHref = link.href.split('?')[0] + '?t=' + Date.now();
                link.href = newHref;
            }}
        }});
    }}
    
    function showError(error) {{
        hideOverlay();
        overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:0;left:0;right:0;bottom:0;background:rgba(0,0,0,0.9);color:#ff6b6b;padding:20px;font-family:monospace;white-space:pre-wrap;z-index:99999;overflow:auto;';
        overlay.textContent = 'Build Error:\\n\\n' + error;
        document.body.appendChild(overlay);
    }}
    
    function showBuilding() {{
        hideOverlay();
        overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:10px;right:10px;background:#333;color:#fff;padding:10px 20px;border-radius:4px;font-family:sans-serif;z-index:99999;';
        overlay.textContent = 'Building...';
        document.body.appendChild(overlay);
    }}

    function showServerStopping() {{
        hideOverlay();
        overlay = document.createElement('div');
        overlay.style.cssText = 'position:fixed;top:10px;right:10px;background:#333;color:#fff;padding:10px 20px;border-radius:4px;font-family:sans-serif;z-index:99999;';
        overlay.textContent = 'Dev server stopped';
        document.body.appendChild(overlay);
    }}

    function hideOverlay() {{
        if (overlay) {{
            overlay.remove();
            overlay = null;
        }}
    }}
}})();
";
    }
}
