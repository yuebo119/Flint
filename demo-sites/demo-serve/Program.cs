// 单进程多端口静态服务——demo-sites.sh 的服务端（.NET 10 实现，
// 2026-09-25 起全面替代 python 版 demo-serve.py，行为逐项对齐）。
//
// 动机沿革：21 主题 + 画廊曾用 22 个独立 python http.server（490MB 工作集），
// 合并为单进程后内存显著下降，且并发取文件不再排队。
// 实现：单进程内每端口一个 TcpListener（127.0.0.1，backlog 256），每连接一个
// 异步任务手写最小 HTTP/1.1（Connection: close）——与原 asyncio 版同构，
// 不用 HttpListener（规避 Windows URL-ACL 非管理员授权问题）。
//
// 用法（仓库根执行）:
//   dotnet run --project demo-sites/demo-serve -- 8400=demo-sites/gallery/public 8401=demo-sites/ananke/public ...
//   dotnet run --project demo-sites/demo-serve -- --list demo-serve.map   # 或用 --list 指定映射文件
//
// 映射行格式: <端口>=<文档根目录>

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

const int Backlog = 256;
const int ReadTimeoutMs = 10_000;

var mappings = ParseMappings(args);
var listeners = new List<TcpListener>(mappings.Count);
var acceptTasks = new List<Task>(mappings.Count);
var connections = new ConcurrentBag<Task>();

foreach (var (port, root) in mappings.OrderBy(kv => kv.Key))
{
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start(Backlog);
    listeners.Add(listener);
    // CA2025：关停时序为 Stop（解除 Accept 阻塞）→ WhenAll 排空 → 进程退出，
    // 任务必然在进程结束前完成；该跨方法时序分析器无法证明，定点抑制
#pragma warning disable CA2025
    acceptTasks.Add(AcceptLoopAsync(listener, root, connections));
#pragma warning restore CA2025
    Console.WriteLine($"  http://127.0.0.1:{port}/  ->  {root}");
}

Console.WriteLine($"共 {mappings.Count} 个端口，单进程服务中（Ctrl+C 停止）");

var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();

await stop.Task.ConfigureAwait(false);

// 关停顺序：先停监听（接受循环随之退出）→ 排空在途连接任务 → 进程结束
foreach (var listener in listeners)
{
    listener.Stop();
}
try
{
    await Task.WhenAll(acceptTasks).ConfigureAwait(false);
    await Task.WhenAll(connections.ToArray()).ConfigureAwait(false);
}
catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException
                           or IOException or OperationCanceledException)
{
    // 关停期间的在途连接异常：忽略
}

return 0;

// ---- 端口→目录映射解析（相对目录按 CWD → 仓库根 → 工作区根 依次探测） ----
static Dictionary<int, string> ParseMappings(string[] argv)
{
    var entries = new List<string>();
    for (var i = 0; i < argv.Length; i++)
    {
        if (argv[i] == "--list" && i + 1 < argv.Length)
        {
            foreach (var line in File.ReadLines(argv[i + 1]))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !line.StartsWith('#'))
                {
                    entries.Add(trimmed);
                }
            }
            i++;
            continue;
        }
        entries.Add(argv[i]);
    }

    if (entries.Count == 0)
    {
        throw new InvalidOperationException("未提供任何 端口=目录 映射");
    }

    var repoRoot = FindRepoRoot();
    var bases = new[] { Directory.GetCurrentDirectory(), repoRoot,
                        repoRoot is null ? null : Path.GetDirectoryName(repoRoot) }
                .Where(b => !string.IsNullOrEmpty(b)).Cast<string>().ToArray();

    var map = new Dictionary<int, string>();
    foreach (var entry in entries)
    {
        var sep = entry.IndexOf('=');
        if (sep <= 0)
        {
            throw new InvalidOperationException($"映射格式错误（应为 端口=目录）: {entry}");
        }
        if (!int.TryParse(entry[..sep].Trim(), out var port))
        {
            throw new InvalidOperationException($"映射格式错误（端口非数字）: {entry}");
        }
        var root = entry[(sep + 1)..].Trim();
        if (!Path.IsPathRooted(root))
        {
            // 依次探测多个基准，取第一个存在的（对齐 python 版语义）
            var resolved = bases
                .Select(b => Path.GetFullPath(Path.Combine(b, root)))
                .FirstOrDefault(Path.Exists);
            root = resolved ?? Path.GetFullPath(root);
        }
        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"文档根不存在: {root}（端口 {port}）");
        }
        map[port] = root;
    }
    return map;
}

// 从程序所在目录向上寻找 Flint.slnx 定位仓库根（找不到返回 null，仅用 CWD 兜底）
static string? FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Flint.slnx")))
        {
            return dir.FullName;
        }
        dir = dir.Parent;
    }
    return null;
}

// ---- 每端口接受循环：新连接建任务入队（并发取文件不排队），关停时统一排空 ----
static async Task AcceptLoopAsync(TcpListener listener, string root, ConcurrentBag<Task> connections)
{
    while (true)
    {
        TcpClient client;
        try
        {
            client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            return; // 监听已停止（关停流程）
        }
        // CA2025：client 的释放（using）就发生在该任务自身的结尾，任务完成即释放，
        // 不存在"先释放后完成"；句柄不逃逸到任务外，定点抑制
#pragma warning disable CA2025
        connections.Add(HandleAsync(client, root));
#pragma warning restore CA2025
    }
}

// ---- 单连接处理：最小 HTTP/1.1（读请求行 + 丢弃请求头，Connection: close） ----
static async Task HandleAsync(TcpClient client, string root)
{
    using var _ = client;
    try
    {
        using var cts = new CancellationTokenSource(ReadTimeoutMs);
        using var stream = client.GetStream();

        var requestLine = await ReadLineAsync(stream, cts.Token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestLine))
        {
            return;
        }
        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await SendAsync(stream, 400, "Bad Request", "bad request"u8.ToArray(),
                "text/plain; charset=utf-8", headOnly: false).ConfigureAwait(false);
            return;
        }
        var method = parts[0];
        var target = parts[1];

        // 丢弃剩余请求头（保持连接简单，同 python 版）
        while (await ReadLineAsync(stream, cts.Token).ConfigureAwait(false) is { } line && line.Length > 0)
        {
        }

        var full = SafeJoin(root, target);
        if (full is null || !File.Exists(full))
        {
            // 404 页存在则返回其内容（主题自带 404.html）
            var notFound = Path.Combine(root, "404.html");
            if (File.Exists(notFound))
            {
                var nfBody = await File.ReadAllBytesAsync(notFound, cts.Token).ConfigureAwait(false);
                await SendAsync(stream, 404, "Not Found", nfBody,
                    "text/html; charset=utf-8", method == "HEAD").ConfigureAwait(false);
                return;
            }
            await SendAsync(stream, 404, "Not Found", "404 not found"u8.ToArray(),
                "text/plain; charset=utf-8", method == "HEAD").ConfigureAwait(false);
            return;
        }

        byte[] body;
        try
        {
            body = await File.ReadAllBytesAsync(full, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await SendAsync(stream, 404, "Not Found", "404 not found"u8.ToArray(),
                "text/plain; charset=utf-8", method == "HEAD").ConfigureAwait(false);
            return;
        }
        await SendAsync(stream, 200, "OK", body, MimeOf(full), method == "HEAD").ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException
                               or ObjectDisposedException or InvalidOperationException)
    {
        // 超时/断连/对端异常：静默丢弃（同 python 版）
    }
}

// URL 路径 → 磁盘路径；越界（..）与不存在返回 null
static string? SafeJoin(string root, string urlPath)
{
    var path = Uri.UnescapeDataString(urlPath.Split('?')[0].Split('#')[0]);
    if (path.EndsWith('/'))
    {
        path += "index.html";
    }
    var full = Path.GetFullPath(Path.Combine(root, path.TrimStart('/')));
    var rootFull = Path.GetFullPath(root);
    var ok = full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
             || full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    return ok ? full : null;
}

static string MimeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
{
    ".html" or ".htm" => "text/html; charset=utf-8",
    ".css" => "text/css; charset=utf-8",
    ".js" or ".mjs" => "application/javascript; charset=utf-8",
    ".json" => "application/json; charset=utf-8",
    ".xml" => "application/xml; charset=utf-8",
    ".txt" => "text/plain; charset=utf-8",
    ".svg" => "image/svg+xml",
    ".png" => "image/png",
    ".jpg" or ".jpeg" => "image/jpeg",
    ".gif" => "image/gif",
    ".webp" => "image/webp",
    ".avif" => "image/avif",
    ".ico" => "image/x-icon",
    ".woff" => "font/woff",
    ".woff2" => "font/woff2",
    ".ttf" => "font/ttf",
    ".otf" => "font/otf",
    ".pdf" => "application/pdf",
    ".mp4" => "video/mp4",
    ".webm" => "video/webm",
    ".mp3" => "audio/mpeg",
    ".wasm" => "application/wasm",
    ".map" => "application/json",
    _ => "application/octet-stream",
};

// 读一行（\r\n 或 \n 结尾），超时/断流返回 null
static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
{
    var buffer = new List<byte>(128);
    var one = new byte[1];
    while (true)
    {
        var n = await stream.ReadAsync(one.AsMemory(0, 1), ct).ConfigureAwait(false);
        if (n == 0)
        {
            return buffer.Count == 0 ? null : Encoding.Latin1.GetString(buffer.ToArray());
        }
        if (one[0] == (byte)'\n')
        {
            if (buffer.Count > 0 && buffer[^1] == (byte)'\r')
            {
                buffer.RemoveAt(buffer.Count - 1);
            }
            return Encoding.Latin1.GetString(buffer.ToArray());
        }
        buffer.Add(one[0]);
    }
}

static async Task SendAsync(Stream stream, int status, string reason, byte[] body, string mime, bool headOnly)
{
    var header = Encoding.ASCII.GetBytes(
        $"HTTP/1.1 {status} {reason}\r\n" +
        $"Content-Type: {mime}\r\n" +
        $"Content-Length: {body.Length}\r\n" +
        "Cache-Control: no-store\r\n" +
        "Connection: close\r\n\r\n");
    if (headOnly)
    {
        await stream.WriteAsync(header).ConfigureAwait(false);
    }
    else
    {
        var payload = new byte[header.Length + body.Length];
        header.CopyTo(payload, 0);
        body.CopyTo(payload, header.Length);
        await stream.WriteAsync(payload).ConfigureAwait(false);
    }
}
