// Flint 静态站点生成器
// 持久内容缓存（2026-10-22）：把内容文件的**原始字节**存成单个 blob，
// 重复构建时用一次顺序读替代 N 次按文件 open/read。
//
// 为什么缓存原始字节而非解析结果：
// - 实测（scripts/probes/scan-read-parallelism-probe.cs）读文件在 OS 层被
//   过滤驱动逐个 IRP 串行化，32 worker 仅 1.24~1.47x 加速——真正的成本是
//   「每文件一次 CreateFile+Read+Close」的 IRP 处理（60~103μs/文件），
//   而解析只有 5~8μs/文件且能真并行。缓存原始字节把 N 次 IRP 降为 1 次
//   顺序读，解析照常并行跑（且用当前构建的绝对路径，跨机器/checkout 可移植）
// - 解析结果缓存要多存 3 倍体积（HtmlContent+RawMarkdown+PlainText）且
//   反序列化成本接近解析成本，收益被吃掉
//
// 键 = （相对路径, mtime, size）：三者任一变化即重读重解析。size 来自
// 目录枚举缓冲区（FileSystemEntry.Length，零额外 syscall）；相对路径保证
// 缓存文件不含绝对路径（用户可能把 .flint-cache 提交进仓库）
//
// 已知边界（与项目既有 mtime 粒度教训一致）：同一 mtime tick 内、同长度
// 的内容改写会漏检（NTFS 时间片下编辑器原子替换可能命中）。因此本缓存
// 只在 --cache 显式开启时生效，默认构建路径不受影响

using System.Buffers.Binary;
using System.Text;

namespace Flint.Core.Content;

/// <summary>
/// 持久内容缓存（单 blob 文件，全量重写；条目 = 相对路径 + mtime + 原始字节）
/// </summary>
public sealed class ContentCacheStore
{
    /// <summary>blob 魔数（版本号随格式演进，不匹配即视为无缓存）</summary>
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FLCONT1\n");

    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    /// <summary>blob 读取的整块缓冲区（条目内容以 (Offset, Length) 切片引用，零逐条分配）</summary>
    private byte[] _buffer = [];

    private sealed record CacheEntry(long MtimeTicks, int Size, int Offset, int Length);

    /// <summary>加载的条目数（诊断/测试用）</summary>
    public int Count => _entries.Count;

    /// <summary>本次构建是否命中过缓存（诊断用）</summary>
    public int HitCount { get; private set; }

    /// <summary>本次构建需要重读的条目数（诊断用）</summary>
    public int MissCount { get; private set; }

    /// <summary>
    /// 从 blob 文件加载缓存；文件不存在/格式不符/损坏时静默返回空缓存
    /// （缓存永远不阻断构建——首次构建、清理后、格式升级均落到这条路径）
    /// </summary>
    public static ContentCacheStore Load(string blobPath)
    {
        var store = new ContentCacheStore();
        if (!File.Exists(blobPath))
        {
            return store;
        }

        try
        {
            var buffer = File.ReadAllBytes(blobPath);
            store.Parse(buffer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException)
        {
            // 损坏的缓存按无缓存处理（重建即自愈）；不静默吞错之外的可能异常
            store._entries.Clear();
        }

        return store;
    }

    private void Parse(byte[] buffer)
    {
        if (buffer.Length < Magic.Length + 4)
        {
            return;
        }

        var span = buffer.AsSpan();
        if (!span[..Magic.Length].SequenceEqual(Magic))
        {
            return;
        }

        var pos = Magic.Length;
        var count = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos, 4));
        pos += 4;

        for (var i = 0; i < count; i++)
        {
            var pathLen = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos, 4));
            pos += 4;
            var relPath = Encoding.UTF8.GetString(span.Slice(pos, pathLen));
            pos += pathLen;

            var mtimeTicks = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(pos, 8));
            pos += 8;
            var size = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos, 4));
            pos += 4;
            var len = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos, 4));
            pos += 4;

            _entries[relPath] = new CacheEntry(mtimeTicks, size, pos, len);
            pos += len;
        }

        _buffer = buffer;
    }

    /// <summary>
    /// 命中查询：键 = （相对路径, mtime, size）三者全等才命中；
    /// 内容以 <see cref="ReadOnlyMemory{T}"/> 切片返回（引用加载缓冲区，零拷贝）
    /// </summary>
    public bool TryGet(string relativePath, long mtimeTicks, int size, out ReadOnlyMemory<byte> content)
    {
        if (_entries.TryGetValue(relativePath, out var entry) &&
            entry.MtimeTicks == mtimeTicks &&
            entry.Size == size)
        {
            HitCount++;
            content = _buffer.AsMemory(entry.Offset, entry.Length);
            return true;
        }

        MissCount++;
        content = default;
        return false;
    }

    /// <summary>
    /// 仅按路径命中（输出写盘跳过用）：输出产物的键是路径 + 内容哈希
    /// （哈希即 payload），无 mtime/size 语义——写入时两字段填 0
    /// </summary>
    public bool TryGetByPath(string relativePath, out ReadOnlyMemory<byte> content)
    {
        if (_entries.TryGetValue(relativePath, out var entry))
        {
            HitCount++;
            content = _buffer.AsMemory(entry.Offset, entry.Length);
            return true;
        }

        MissCount++;
        content = default;
        return false;
    }

    /// <summary>
    /// 以本次构建的全部条目重写 blob（原子：临时文件 + 替换）。
    /// 只在调用方判定「有变化」时调用，未变构建零写盘
    /// </summary>
    public void Save(string blobPath, IEnumerable<(string RelPath, long MtimeTicks, ReadOnlyMemory<byte> Content)> entries)
    {
        var dir = Path.GetDirectoryName(blobPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // 先算总长再一次写入（单次顺序写，避免逐条小写）
        var list = new List<(string RelPath, long MtimeTicks, byte[] PathBytes, ReadOnlyMemory<byte> Content)>();
        var total = Magic.Length + 4;
        foreach (var (relPath, mtimeTicks, content) in entries)
        {
            var pathBytes = Encoding.UTF8.GetBytes(relPath);
            list.Add((relPath, mtimeTicks, pathBytes, content));
            total += 4 + pathBytes.Length + 8 + 4 + 4 + content.Length;
        }

        var buffer = new byte[total];
        var span = buffer.AsSpan();
        Magic.CopyTo(span);
        var pos = Magic.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), list.Count);
        pos += 4;

        foreach (var (_, mtimeTicks, pathBytes, content) in list)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), pathBytes.Length);
            pos += 4;
            pathBytes.CopyTo(span.Slice(pos));
            pos += pathBytes.Length;

            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(pos, 8), mtimeTicks);
            pos += 8;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), content.Length);
            pos += 4;
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), content.Length);
            pos += 4;
            content.Span.CopyTo(span.Slice(pos));
            pos += content.Length;
        }

        // 临时文件名带随机后缀：同一站点上并发跑两个 --cache 构建时
        // 各写各的临时文件（固定名会互相覆盖，让 Move 失败或写出半截）
        var tempPath = $"{blobPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, buffer);
            File.Move(tempPath, blobPath, overwrite: true);
        }
        catch
        {
            // 失败时清掉自己的临时文件（避免残留堆积），原异常照抛给调用方
            // 转成构建告警（CACHE001）
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}