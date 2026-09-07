// Flint 静态站点生成器
// RecentUrlQueue——fast render mode 的最近访问 URL 队列（对齐 Hugo EvictingQueue(20)）

namespace Flint.Core.Server;

/// <summary>
/// 容量受限的最近访问 URL 环形队列：满后覆盖最旧条目。
/// DevServer 在 HTTP 请求处理时记录浏览器实际访问的页面 URL，
/// 模板变化的增量构建只重渲染这些页面（fast render mode）。
/// 线程安全：HTTP 请求与 watcher 回调在不同线程。
/// </summary>
public sealed class RecentUrlQueue
{
    private readonly object _lock = new();
    private readonly string[] _items;
    private int _head; // 下一个写入位置（即最旧条目，仅当已满时成立）

    /// <param name="capacity">队列容量（Hugo fast render 为 20）</param>
    public RecentUrlQueue(int capacity = 20)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new string[capacity];
    }

    /// <summary>记录一次访问；队列满时覆盖最旧条目</summary>
    public void Record(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        lock (_lock)
        {
            _items[_head] = url;
            _head = (_head + 1) % _items.Length;
        }
    }

    /// <summary>当前队列快照（去重）；空队列返回空集</summary>
    public IReadOnlySet<string> Snapshot()
    {
        lock (_lock)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in _items)
            {
                if (item is not null)
                {
                    set.Add(item);
                }
            }

            return set;
        }
    }
}
