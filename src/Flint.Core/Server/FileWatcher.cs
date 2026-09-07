// Flint 静态站点生成器
// 文件监视器实现

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Flint.Core.Abstractions;

namespace Flint.Core.Server;

/// <summary>
/// 文件监视器
/// 使用 FileSystemWatcher 监听文件变化，支持防抖动处理
/// </summary>
public sealed class FileWatcher : IFileWatcher
{
    private FileSystemWatcher? _watcher;
    private readonly Channel<FileChangeEvent> _channel;
    private readonly TimeSpan _debounceDelay;
    private readonly Dictionary<string, DateTimeOffset> _lastEventTimes = new();
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// 创建文件监视器
    /// </summary>
    /// <param name="debounceDelay">防抖动延迟，默认 100ms</param>
    public FileWatcher(TimeSpan? debounceDelay = null)
    {
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(100);
        _channel = Channel.CreateUnbounded<FileChangeEvent>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FileChangeEvent> WatchAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Start(path);

        await foreach (var evt in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public void Start(string path)
    {
        if (_watcher != null)
        {
            Stop();
        }

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnCreated;
        _watcher.Changed += OnChanged;
        _watcher.Deleted += OnDeleted;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnCreated;
            _watcher.Changed -= OnChanged;
            _watcher.Deleted -= OnDeleted;
            _watcher.Renamed -= OnRenamed;
            _watcher.Error -= OnError;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        Stop();
        _channel.Writer.Complete();
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        EnqueueEvent(e.FullPath, FileChangeType.Created);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        EnqueueEvent(e.FullPath, FileChangeType.Modified);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        EnqueueEvent(e.FullPath, FileChangeType.Deleted);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        EnqueueEvent(e.FullPath, FileChangeType.Renamed, e.OldFullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // FileSystemWatcher Error（内部缓冲区溢出/句柄错误）后事件流不可靠，
        // 只打日志会让 dev server 静默失聪——就地重建 watcher 恢复监视。
        // 重建结果合并进原有错误行输出（守 G13 Console 棘轮，不新增直写）
        var path = _watcher?.Path;
        var rebuildNote = string.Empty;
        if (path is not null && !_isDisposed)
        {
            try
            {
                Start(path);
                rebuildNote = "（监视器已重建）";
            }
            catch (Exception ex)
            {
                rebuildNote = $"（监视器重建失败: {ex.Message}）";
            }
        }
        Console.Error.WriteLine($"文件监视器错误{rebuildNote}: {e.GetException().Message}");
    }

    private void EnqueueEvent(string path, FileChangeType changeType, string? oldPath = null)
    {
        // 防抖动处理
        lock (_lock)
        {
            var now = DateTimeOffset.Now;
            var key = $"{path}:{changeType}";

            if (_lastEventTimes.TryGetValue(key, out var lastTime))
            {
                if (now - lastTime < _debounceDelay)
                {
                    return; // 忽略重复事件
                }
            }

            _lastEventTimes[key] = now;

            // 防抖字典永不清理会在长驻 dev server 中缓慢泄漏
            if (_lastEventTimes.Count > 1024)
            {
                var cutoff = now - _debounceDelay - TimeSpan.FromSeconds(10);
                var staleKeys = _lastEventTimes
                    .Where(kv => kv.Value < cutoff)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var staleKey in staleKeys)
                {
                    _lastEventTimes.Remove(staleKey);
                }
            }
        }

        var evt = new FileChangeEvent
        {
            Path = path,
            ChangeType = changeType,
            Timestamp = DateTimeOffset.Now,
            OldPath = oldPath
        };

        // 非阻塞写入
        _channel.Writer.TryWrite(evt);
    }
}

/// <summary>
/// 文件监视器扩展方法
/// </summary>
public static class FileWatcherExtensions
{
    /// <summary>
    /// 过滤指定扩展名的文件变化
    /// </summary>
    public static async IAsyncEnumerable<FileChangeEvent> FilterByExtension(
        this IAsyncEnumerable<FileChangeEvent> events,
        params string[] extensions)
    {
        var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);

        await foreach (var evt in events)
        {
            var ext = Path.GetExtension(evt.Path);
            if (extSet.Contains(ext))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// 过滤指定目录的文件变化
    /// </summary>
    public static async IAsyncEnumerable<FileChangeEvent> FilterByDirectory(
        this IAsyncEnumerable<FileChangeEvent> events,
        string directory)
    {
        await foreach (var evt in events)
        {
            if (evt.Path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            {
                yield return evt;
            }
        }
    }

    /// <summary>
    /// 批量处理文件变化（收集一段时间内的变化）
    /// </summary>
    public static async IAsyncEnumerable<IReadOnlyList<FileChangeEvent>> Batch(
        this IAsyncEnumerable<FileChangeEvent> events,
        TimeSpan batchWindow,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var batch = new List<FileChangeEvent>();
        var lastYield = DateTimeOffset.Now;

        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            batch.Add(evt);

            if (DateTimeOffset.Now - lastYield >= batchWindow && batch.Count > 0)
            {
                yield return batch.ToList();
                batch.Clear();
                lastYield = DateTimeOffset.Now;
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
