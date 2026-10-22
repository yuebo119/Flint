// 扫描并行度探针：判定「读文件是否无法并行」及其成因
// 假设 A：AV 过滤驱动串行化每次文件打开（与目录布局无关）
// 假设 B：NTFS 同目录内并发打开被目录锁串行化（多目录散布应改善）
// 假设 C：本来就并行，之前观测是别的原因
using System.Diagnostics;

const int N = 2000;
const int FileSize = 4096;
var root = Path.Combine(Path.GetTempPath(), "scanprobe-" + Guid.NewGuid().ToString("n"));

// 布局 a：全部文件在一个目录
var flat = Path.Combine(root, "flat");
Directory.CreateDirectory(flat);
// 布局 b：文件散布在 20 个子目录
var spreadDirs = Enumerable.Range(0, 20).Select(i => Path.Combine(root, "spread", $"s{i:D2}")).ToArray();
foreach (var d in spreadDirs) Directory.CreateDirectory(d);

var payload = new byte[FileSize];
#pragma warning disable CA5394
Random.Shared.NextBytes(payload);
#pragma warning restore CA5394

for (var i = 0; i < N; i++)
{
    File.WriteAllBytes(Path.Combine(flat, $"f{i:D5}.bin"), payload);
    File.WriteAllBytes(Path.Combine(spreadDirs[i % 20], $"f{i:D5}.bin"), payload);
}

string[] Enumerate(string dir) =>
    Directory.EnumerateFiles(dir, "*.bin", SearchOption.AllDirectories).ToArray();

var flatFiles = Enumerate(flat);
var spreadFiles = Enumerate(Path.Combine(root, "spread"));

// 预热（OS 缓存 + AV 判定缓存）
foreach (var f in flatFiles) File.ReadAllBytes(f);
foreach (var f in spreadFiles) File.ReadAllBytes(f);

async Task<double> ReadAllAsync(string[] files, int dop)
{
    // 与产品扫描同构：Parallel.ForEachAsync + File.ReadAllBytesAsync
    var sw = Stopwatch.StartNew();
    await Parallel.ForEachAsync(
        files,
        new ParallelOptions { MaxDegreeOfParallelism = dop },
        async (f, ct) => { _ = await File.ReadAllBytesAsync(f, ct).ConfigureAwait(false); })
        .ConfigureAwait(false);
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds;
}

// 对照臂：File.OpenHandle + RandomAccess.Read（绕开 FileStream 构造）
async Task<double> ReadHandlesAsync(string[] files, int dop)
{
    var sw = Stopwatch.StartNew();
    await Parallel.ForEachAsync(
        files,
        new ParallelOptions { MaxDegreeOfParallelism = dop },
        async (f, ct) =>
        {
            using var h = File.OpenHandle(f, FileMode.Open, FileAccess.Read, FileShare.Read,
                FileOptions.Asynchronous);
            var len = (int)RandomAccess.GetLength(h);
            var buf = new byte[len];
            await RandomAccess.ReadAsync(h, buf, 0, ct).ConfigureAwait(false);
        })
        .ConfigureAwait(false);
    sw.Stop();
    return sw.Elapsed.TotalMilliseconds;
}

Console.WriteLine($"N={N} 文件 × {FileSize}B；目录布局：单目录 vs 20 目录散布");
Console.WriteLine();
foreach (var (label, files) in new[] { ("单目录", flatFiles), ("20 目录散布", spreadFiles) })
{
    var seq = await ReadAllAsync(files, 1).ConfigureAwait(false);
    var par = await ReadAllAsync(files, Environment.ProcessorCount).ConfigureAwait(false);
    var seq2 = await ReadAllAsync(files, 1).ConfigureAwait(false);
    var par2 = await ReadAllAsync(files, Environment.ProcessorCount).ConfigureAwait(false);
    var h1 = await ReadHandlesAsync(files, Environment.ProcessorCount).ConfigureAwait(false);
    var h2 = await ReadHandlesAsync(files, Environment.ProcessorCount).ConfigureAwait(false);
    Console.WriteLine($"{label,-14} ReadAllBytesAsync 顺序 {seq:F1}/{seq2:F1}ms ({(seq + seq2) / 2 / N * 1000:F1}μs/文件)");
    Console.WriteLine($"{label,-14} ReadAllBytesAsync 并行 {(par + par2) / 2:F1}ms ({(par + par2) / 2 / N * 1000:F1}μs/文件) 加速比 {(seq + seq2) / (par + par2):F2}x");
    Console.WriteLine($"{label,-14} OpenHandle+RandomAccess 并行 {(h1 + h2) / 2:F1}ms ({(h1 + h2) / 2 / N * 1000:F1}μs/文件) vs ReadAllBytesAsync {(par + par2) / 2 / (h1 + h2) * 2:F2}x");
    Console.WriteLine();
}

try { Directory.Delete(root, true); }
catch (IOException) { }