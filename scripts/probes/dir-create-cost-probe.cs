// 目录创建成本探针：判定「mkdir 是不是墙、并行度有没有用、已存在目录的空操作有多贵」
// 假设 A：单父目录下海量子目录被 NTFS/过滤驱动串行化，并行度无感
// 假设 B：目录已存在时 CreateDirectory 仍是完整一次系统调用（热重建白付一次）
// 假设 C：把目录散到多个父目录能显著降本（输出布局由 URL 决定，产品上无法利用）
//
// 运行：先复制到仓外再跑——仓内 Directory.Build.props 的中央包管理会干扰文件型 app
//   cp scripts/probes/dir-create-cost-probe.cs "$TEMP/" && cd "$TEMP"
//   dotnet run dir-create-cost-probe.cs -- par-single 14622 32 "$TEMP/dirprobe"
// mode：seq-single | par-single | par-group | par-existing | exists | touch
//   末尾 -group 的 mode 会把目标散到 32 个父目录
//   par-existing：先建好再重复 CreateDirectory（热重建的空操作成本）
//   exists：先建好再 Directory.Exists
//   touch：先建好再往每个目录写 20KB 文件（写盘成本）
using System.Diagnostics;

var mode = args[0];
var n = int.Parse(args[1]);
var dop = int.Parse(args[2]);
var root = args[3];

if (Directory.Exists(root))
{
    Directory.Delete(root, recursive: true);
}
Directory.CreateDirectory(root);

var groups = mode.EndsWith("-group", StringComparison.Ordinal) ? 32 : 1;
var dirs = new string[n];
for (var i = 0; i < n; i++)
{
    var g = i % groups;
    dirs[i] = groups == 1
        ? Path.Combine(root, "d" + i)
        : Path.Combine(root, "g" + g, "d" + i);
}

// 20KB 典型 HTML 载荷
var payload = new string('x', 20 * 1024);
var doWrite = mode.StartsWith("write", StringComparison.Ordinal);

// 预热：par-existing / exists / touch 需要目录先存在
if (mode is "par-existing" or "exists" or "touch" or "write-single" or "write-group")
{
    Parallel.ForEach(dirs, new ParallelOptions { MaxDegreeOfParallelism = 32 },
        d => Directory.CreateDirectory(d));
}

var sw = Stopwatch.StartNew();
if (mode.StartsWith("seq", StringComparison.Ordinal))
{
    foreach (var d in dirs)
    {
        Directory.CreateDirectory(d);
    }
}
else if (mode == "exists")
{
    var hits = 0;
    Parallel.ForEach(dirs, new ParallelOptions { MaxDegreeOfParallelism = dop }, d =>
    {
        if (Directory.Exists(d))
        {
            Interlocked.Increment(ref hits);
        }
    });
    Console.WriteLine($"exists hits={hits}");
}
else
{
    Parallel.ForEach(dirs, new ParallelOptions { MaxDegreeOfParallelism = dop }, d =>
    {
        switch (mode)
        {
            case "touch":
            case "write-single":
            case "write-group":
                File.WriteAllText(Path.Combine(d, "index.html"), payload);
                break;
            default:
                Directory.CreateDirectory(d);
                break;
        }
    });
}
sw.Stop();
var perOp = n > 0 ? sw.Elapsed.TotalMilliseconds * 1000 / n : 0;
Console.WriteLine($"{mode} n={n} dop={dop} groups={groups} elapsed={sw.ElapsedMilliseconds}ms perOp={perOp:F0}us");
