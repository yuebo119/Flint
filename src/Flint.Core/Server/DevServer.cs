// Flint 静态站点生成器
// 开发服务器实现 - 基于 Kestrel

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Flint.Core.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Flint.Core.Server;

/// <summary>
/// 开发服务器
/// 使用 Kestrel 提供 HTTP/2 支持和热重载功能
/// </summary>
public sealed class DevServer : IDevServer, IDisposable
{
    private readonly ISiteBuilder _siteBuilder;
    private readonly LiveReloadNotifier _liveReloadNotifier;
    private readonly Server.RecentUrlQueue _recentUrls = new();
    private IFileWatcher? _fileWatcher;
    private CancellationTokenSource? _cts;
    private WebApplication? _app;
    private bool _isDisposed;

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public string? ServerUrl { get; private set; }

    /// <summary>
    /// 创建开发服务器
    /// </summary>
    public DevServer(ISiteBuilder siteBuilder)
    {
        _siteBuilder = siteBuilder;
        _liveReloadNotifier = new LiveReloadNotifier();
    }

    /// <inheritdoc />
    public async Task StartAsync(DevServerOptions options, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("服务器已在运行");

        var protocol = options.UseHttps ? "https" : "http";

        // 探测/watcher/Kestrel/初始构建全部纳入失败清理范围——任一环节抛出，
        // 已创建的 cts/watcher/app 不就地清理即永久泄漏（StopAsync 的
        // IsRunning 闸门会直接返回）
        try
        {
            // 局部变量承载：可空字段 _cts 的 null 性编译器不可证伪
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cts = cts;

            // 初始构建（可能耗时数秒）
            await InitialBuildAsync(options, cts.Token);

            var port = FindAvailablePort(options.Port);
            // 端口探测必须紧贴 Kestrel 绑定：探测与绑定之间隔整个初始构建的话，
            // 并行 server（测试/多实例）会互相抢到同一端口（TOCTOU 窗口 = 构建时长）
            ServerUrl = $"{protocol}://{options.BindAddress}:{port}";

            // 启动文件监视
            _fileWatcher = new FileWatcher();
            StartFileWatching(options, cts.Token);

            // 启动 Kestrel 服务器
            await StartKestrelServerAsync(options, port, cts.Token);
        }
        catch
        {
            _fileWatcher?.Stop();
            if (_app is not null)
            {
                try { await _app.DisposeAsync(); } catch { /* 尽力清理 */ }
                _app = null;
            }
            // _cts 在 try 首句赋值，但若赋值本身前抛出（防御性）仍可能为 null
            if (_cts is not null)
            {
                await _cts.CancelAsync();
                _cts.Dispose();
                _cts = null;
            }
            ServerUrl = null;
            throw;
        }
        IsRunning = true;

        if (options.OpenBrowser)
            OpenBrowser(ServerUrl);
        if (options.Verbose)
            await Console.Out.WriteLineAsync($"开发服务器已启动: {ServerUrl}\n支持 HTTP/2\n按 Ctrl+C 停止服务器");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
            return;

        // 停机前通知浏览器端：收到 server-stopping 的客户端不再 2 秒重连刷新，
        // 否则 dev server 关闭后浏览器陷入每 2 秒刷新循环。
        // 5 秒上限：TCP 半开/发送缓冲已满的坏客户端不能把 Ctrl+C 卡住
        if (_liveReloadNotifier.ConnectedClients > 0)
        {
            using var notifyCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
            notifyCts.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await _liveReloadNotifier.NotifyServerStoppingAsync(notifyCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 5 秒超时：尽力而为的通知到此为止，继续走停机清理
            }
            catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException)
            {
                // 通知失败（socket 类异常）不阻断停机
                _ = ex;
            }
        }

        await _cts!.CancelAsync();
        _fileWatcher?.Stop();

        if (_app != null)
        {
            try
            {
                await _app.StopAsync(cancellationToken);
                await _app.DisposeAsync();
            }
            catch (OperationCanceledException) { }
        }

        IsRunning = false;
        ServerUrl = null;
        _cts.Dispose();
        _cts = null;
    }

    private async Task InitialBuildAsync(DevServerOptions options, CancellationToken cancellationToken)
    {
        var buildOptions = new Models.BuildOptions
        {
            SourcePath = options.SourcePath,
            OutputPath = options.OutputPath,
            IncludeDrafts = options.IncludeDrafts,
            IncludeFuture = options.IncludeFuture,
            Parallelism = Environment.ProcessorCount
        };

        var result = await _siteBuilder.BuildAsync(buildOptions, cancellationToken);

        if (!result.Success)
        {
            var errors = string.Join("\n", result.Errors.Select(e => e.Message));
            await Console.Error.WriteLineAsync($"初始构建失败:\n{errors}");
        }
        else if (options.Verbose)
        {
            await Console.Out.WriteLineAsync($"初始构建完成: {result.PagesBuilt} 页面, {result.AssetsProcessed} 资源, 耗时 {result.Duration.TotalMilliseconds:F0}ms");
        }
    }

    private void StartFileWatching(DevServerOptions options, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // 补尾分隔符再前缀比较：否则 public2 这类兄弟目录的前缀
                // 会被误判为输出目录内，改动静默不重建
                var outputRoot = Path.GetFullPath(options.OutputPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                // mass edit 防护（对齐 Hugo Batcher 思路）：短窗口内事件数超阈值（git checkout/
                // 切分支/批量保存）→ 停止逐事件增量，改为 2 秒节流的全量重建
                var recentEvents = new Queue<DateTimeOffset>();
                var lastFullRebuild = DateTimeOffset.MinValue;
                var lastEventAt = DateTimeOffset.MinValue;
                const int massEditThreshold = 50;
                var massEditMode = false;

                await foreach (var evt in _fileWatcher!.WatchAsync(options.SourcePath, cancellationToken))
                {
                    // 排除输出目录内的事件：初始/增量构建写入 public/** 会再次触发监视，
                    // 形成构建反馈循环
                    if (evt.Path.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 10 秒滑动窗口事件计数
                    var now = DateTimeOffset.Now;
                    recentEvents.Enqueue(now);
                    while (recentEvents.Count > 0 && now - recentEvents.Peek() > TimeSpan.FromSeconds(10))
                    {
                        recentEvents.Dequeue();
                    }

                    if (!massEditMode && recentEvents.Count > massEditThreshold)
                    {
                        massEditMode = true;
                        await Console.Error.WriteLineAsync(
                            $"检测到大量文件变化（10 秒内 {recentEvents.Count} 个），切换为节流全量重建模式");
                    }

                    if (massEditMode)
                    {
                        // 风暴结束判定：事件间隔超过节流窗口 → 退出风暴模式，
                        // 本事件（风暴尾部改动）落到下方正常增量处理，不丢弃；
                        // 风暴期间按 2 秒节流全量重建，窗口内中间态被合并是预期行为
                        if (now - lastEventAt > TimeSpan.FromSeconds(2))
                        {
                            massEditMode = false;
                        }
                        else
                        {
                            lastEventAt = now;
                            if (now - lastFullRebuild >= TimeSpan.FromSeconds(2))
                            {
                                lastFullRebuild = now;
                                await HandleFileChangeAsync(evt, options, cancellationToken, fullRebuild: true);
                            }
                            continue;
                        }
                    }

                    lastEventAt = now;
                    await HandleFileChangeAsync(evt, options, cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { await Console.Error.WriteLineAsync($"文件监视错误: {ex.Message}"); }
        }, cancellationToken);
    }

    private async Task HandleFileChangeAsync(
        FileChangeEvent evt,
        DevServerOptions options,
        CancellationToken cancellationToken,
        bool fullRebuild = false)
    {
        if (options.Verbose)
            await Console.Out.WriteLineAsync($"文件变化: {evt.ChangeType} - {evt.Path}");

        await _liveReloadNotifier.NotifyBuildStartAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var buildOptions = new Models.BuildOptions
            {
                SourcePath = options.SourcePath,
                OutputPath = options.OutputPath,
                IncludeDrafts = options.IncludeDrafts,
                IncludeFuture = options.IncludeFuture,
                Parallelism = Environment.ProcessorCount,
                // fast render mode（T4.4）：模板变化的增量构建只重渲染浏览器访问中的页面
                PreferredUrls = options.FastRenderMode ? _recentUrls.Snapshot() : null
            };

            // 模板/配置/数据文件变化影响全站（首页、分类页等非内容页面也依赖它们），走全量重建；
            // mass edit 风暴期同样全量（合并窗口内全部变化）；其余内容/资源变化走增量构建
            var result = fullRebuild || IsGlobalChange(evt.Path, options.SourcePath)
                ? await _siteBuilder.BuildAsync(buildOptions, cancellationToken)
                : await _siteBuilder.IncrementalBuildAsync(buildOptions, [evt.Path], cancellationToken);
            stopwatch.Stop();

            if (result.Success)
            {
                var ext = Path.GetExtension(evt.Path).ToLowerInvariant();
                if (ext is ".css" or ".scss" or ".sass")
                    await _liveReloadNotifier.NotifyCssUpdateAsync(evt.Path, cancellationToken);
                else
                    await _liveReloadNotifier.NotifyReloadAsync(cancellationToken);

                await _liveReloadNotifier.NotifyBuildCompleteAsync(stopwatch.Elapsed, cancellationToken);
                if (options.Verbose)
                    await Console.Out.WriteLineAsync($"增量构建完成: 耗时 {stopwatch.Elapsed.TotalMilliseconds:F0}ms");
            }
            else
            {
                var errors = string.Join("\n", result.Errors.Select(e => e.Message));
                await _liveReloadNotifier.NotifyErrorAsync(errors, cancellationToken);
                await Console.Error.WriteLineAsync($"构建错误:\n{errors}");
            }
        }
        catch (Exception ex)
        {
            await _liveReloadNotifier.NotifyErrorAsync(ex.Message, cancellationToken);
            await Console.Error.WriteLineAsync($"构建异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 判断是否为影响全站的变更（模板/配置/数据文件）
    /// </summary>
    private static bool IsGlobalChange(string path, string sourcePath)
    {
        var normalized = Path.GetFullPath(path);
        var root = Path.GetFullPath(sourcePath);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }
        if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalized[root.Length..];
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length == 0)
        {
            return false;
        }

        // 顶层配置文件（flint.toml / config.* / hugo.*）
        var fileName = segments[^1];
        if (segments.Length == 1 &&
            (fileName.StartsWith("flint.", StringComparison.OrdinalIgnoreCase)
             || fileName.StartsWith("config.", StringComparison.OrdinalIgnoreCase)
             || fileName.StartsWith("hugo.", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // layouts 目录不再全量（T4.2）：模板变化由依赖图反查受影响页 + section/home/分类页
        // 重渲染覆盖（IncrementalBuildAsync）；data/archetypes 变化影响面广，维持全量
        return segments[0] is "data" or "archetypes";
    }

    private async Task StartKestrelServerAsync(DevServerOptions options, int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // 配置 Kestrel
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.Listen(IPAddress.Parse(options.BindAddress == "localhost" ? "127.0.0.1" : options.BindAddress), port, listenOptions =>
            {
                // 启用 HTTP/2
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;

                if (options.UseHttps)
                {
                    // 使用开发证书或自签名证书
                    var cert = GetOrCreateDevCertificate();
                    if (cert != null)
                    {
                        listenOptions.UseHttps(cert);
                    }
                    else
                    {
                        listenOptions.UseHttps();
                    }
                }
            });
        });

        // 禁用默认日志（除非 verbose 模式）
        if (!options.Verbose)
        {
            builder.Logging.ClearProviders();
        }

        _app = builder.Build();

        // 启用 WebSocket 支持
        _app.UseWebSockets();

        // 配置路由
        ConfigureRoutes(_app, options);

        // 启动服务器
        await _app.StartAsync(cancellationToken);
    }

    private void ConfigureRoutes(WebApplication app, DevServerOptions options)
    {
        // 使用中间件处理所有请求
        app.Use(async (HttpContext context, RequestDelegate next) =>
        {
            var path = context.Request.Path.Value ?? "/";

            // fast render mode（T4.4）：记录浏览器实际访问的页面 URL（livereload 端点除外）
            if (options.FastRenderMode && path != "/__livereload")
            {
                _recentUrls.Record(path);
            }

            // WebSocket 热重载端点
            if (path == "/__livereload")
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    var clientId = Guid.NewGuid().ToString();
                    _liveReloadNotifier.AddClient(clientId, webSocket);

                    try
                    {
                        var buffer = new byte[1024];
                        while (webSocket.State == WebSocketState.Open)
                        {
                            var result = await webSocket.ReceiveAsync(buffer, context.RequestAborted);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                break;
                            }
                        }
                    }
                    catch (WebSocketException)
                    {
                        // 客户端断开连接
                    }
                    finally
                    {
                        _liveReloadNotifier.RemoveClient(clientId);
                        // 完成关闭握手并释放：缺 CloseAsync 会让浏览器端走 onerror 而非 onclose
                        if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                        {
                            try
                            {
                                await webSocket.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                            }
                            catch (WebSocketException) { }
                        }
                        webSocket.Dispose();
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("WebSocket 请求需要升级", context.RequestAborted);
                }
                return;
            }

            // 静态文件服务
            var filePath = ResolveFilePath(path, options.OutputPath);

            if (filePath is null)
            {
                // 路径逃逸输出目录（如 /../ 或绝对路径注入），拒绝访问
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("403 Forbidden", context.RequestAborted);
                return;
            }

            if (File.Exists(filePath))
            {
                var content = await File.ReadAllBytesAsync(filePath, context.RequestAborted);
                context.Response.ContentType = GetContentType(filePath);

                // 注入热重载脚本
                if (options.LiveReload && filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    var html = Encoding.UTF8.GetString(content);
                    var wsProtocol = options.UseHttps ? "wss" : "ws";
                    var wsUrl = $"{wsProtocol}://{context.Request.Host}/__livereload";
                    var script = $"<script>{LiveReloadScript.Generate(wsUrl)}</script>";
                    html = html.Replace("</body>", $"{script}</body>");
                    content = Encoding.UTF8.GetBytes(html);
                }

                await context.Response.Body.WriteAsync(content, context.RequestAborted);
            }
            else
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("404 Not Found", context.RequestAborted);
            }
        });
    }

    private static X509Certificate2? GetOrCreateDevCertificate()
    {
        try
        {
            // 尝试使用 ASP.NET Core 开发证书
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var certs = store.Certificates.Find(
                X509FindType.FindBySubjectName,
                "localhost",
                validOnly: true);

            if (certs.Count > 0)
            {
                return certs[0];
            }
        }
        catch
        {
            // 忽略证书查找错误
        }

        return null;
    }

    private static string? ResolveFilePath(string path, string outputPath)
    {
        path = path.TrimStart('/');
        if (string.IsNullOrEmpty(path) || path.EndsWith('/'))
        {
            // 对于根路径或目录路径，返回 index.html
            return ResolveWithinOutput(Path.Combine(outputPath, path.TrimEnd('/'), "index.html"), outputPath);
        }
        else if (!Path.HasExtension(path))
        {
            // 尝试添加 index.html
            var withIndex = Path.Combine(outputPath, path, "index.html");
            if (File.Exists(withIndex))
            {
                return ResolveWithinOutput(withIndex, outputPath);
            }
        }
        return ResolveWithinOutput(Path.Combine(outputPath, path), outputPath);
    }

    /// <summary>
    /// 将拼接后的路径规范化并校验仍位于输出目录内，防止路径穿越（../、绝对路径注入）
    /// </summary>
    private static string? ResolveWithinOutput(string combinedPath, string outputPath)
    {
        var outputRoot = Path.GetFullPath(outputPath);
        if (!outputRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            outputRoot += Path.DirectorySeparatorChar;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(combinedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // URL 含非法路径字符（如 <>|"）：视为不存在的资源（404），
            // 而非让异常逃逸变成 500
            fullPath = Path.Combine(outputRoot, "__flint_invalid_path__");
        }

        return fullPath.StartsWith(outputRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : null;
    }

    private static string GetContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".xml" => "application/xml; charset=utf-8",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".eot" => "application/vnd.ms-fontobject",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };

    private static int FindAvailablePort(int preferredPort)
    {
        if (IsPortAvailable(preferredPort))
            return preferredPort;
        for (var port = preferredPort + 1; port < preferredPort + 100; port++)
        {
            if (IsPortAvailable(port))
                return port;
        }
        throw new InvalidOperationException($"无法找到可用端口（从 {preferredPort} 开始）");
    }

    private static bool IsPortAvailable(int port)
    {
        var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpEndPoints = ipProperties.GetActiveTcpListeners();
        return !tcpEndPoints.Any(ep => ep.Port == port);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // 忽略浏览器打开错误
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _fileWatcher?.Dispose();
        _liveReloadNotifier.Dispose();
        _app?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
    }
}
