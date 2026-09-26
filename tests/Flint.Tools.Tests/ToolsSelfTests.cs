// Flint.Tools.Tests：DevTools/AiGate 自身行为回归（CS-42）。
// 覆盖本轮移植实测踩过的边界：OrderedCounter 同序语义、PadRightBytes 字节宽对齐、
// RepoGuard 删除白名单、ProcessRunner 退出码断言、Scanner 注释行过滤、
// 零断言花括号不配对时文末截断（防 Substring 越界回归）。

using System.Text;
using AwesomeAssertions;
using Flint.AiGate;
using Flint.DevTools;
using Flint.DevTools.Bench;
using Xunit;

namespace Flint.Tools.Tests;

/// <summary>
/// OrderedCounter：复刻 Python Counter.most_common 语义（计数降序 + 同计数保插入序）
/// </summary>
public class OrderedCounterTests
{
    [Fact]
    public void MostCommon_按计数降序且同计数保插入序()
    {
        var counter = new OrderedCounter();
        counter.Add("a");
        counter.Add("b");
        counter.Add("a");
        counter.Add("c");
        counter.Add("b"); // a=2, b=2, c=1；a 先于 b 插入

        var top = counter.MostCommon(int.MaxValue);

        top.Select(p => p.Key).Should().BeEquivalentTo(new[] { "a", "b", "c" }, options => options.WithStrictOrdering());
        top[0].Value.Should().Be(2);
        top[1].Value.Should().Be(2);
        top[2].Value.Should().Be(1);
    }

    [Fact]
    public void Minus_仅保留正差值且保左侧插入序()
    {
        var left = new OrderedCounter();
        left.Add("x");
        left.Add("x");
        left.Add("y");
        var right = new OrderedCounter();
        right.Add("x");
        right.Add("z");

        // Python Counter 语义：Counter(x=2,y=1) - Counter(x=1,z=1) = {x:1, y:1}
        // （y 右侧不存在，差值 1-0=1 仍为正；z 在左侧不存在不计）
        var diff = left.Minus(right);

        diff.Should().HaveCount(2);
        diff[0].Should().Be(new KeyValuePair<string, int>("x", 1));
        diff[1].Should().Be(new KeyValuePair<string, int>("y", 1));
    }
}

/// <summary>
/// PyCompat.PadRightBytes：bash printf %-Ns 的 UTF-8 字节宽对齐（CJK 按 3 字节）
/// </summary>
public class PyCompatTests
{
    [Fact]
    public void PadRightBytes_按UTF8字节宽补齐而非字符数()
    {
        // "结论" 2 个字符 = 6 字节；对齐到 10 字节应补 4 个空格（字符数口径只会补 8 个）
        PyCompat.PadRightBytes("结论", 10).Should().Be("结论    ");
        PyCompat.PadRightBytes("结论", 10).Length.Should().Be(6);

        // ASCII 与 bash 字符口径一致
        PyCompat.PadRightBytes("abc", 6).Should().Be("abc   ");

        // 超宽不截断
        PyCompat.PadRightBytes("abcdef", 3).Should().Be("abcdef");
    }
}

/// <summary>
/// ProcessRunner：外部进程退出码与断言（bench 四件套的计时/断言基础）
/// </summary>
public class ProcessRunnerTests
{
    [Fact]
    public async Task RunTimedAsync_返回退出码与输出_成功与失败两路()
    {
        var ok = await ProcessRunner.RunTimedAsync("git", new[] { "rev-parse", "--git-dir" });
        ok.ExitCode.Should().Be(0);
        ok.AssertSuccess("git rev-parse"); // 不抛

        var bad = await ProcessRunner.RunTimedAsync("git", new[] { "definitely-not-a-git-command" });
        bad.ExitCode.Should().NotBe(0);

        var act = () => bad.AssertSuccess("git bogus");
        act.Should().Throw<InvalidOperationException>().WithMessage("*退出码*");
    }

    [Fact]
    public async Task RunTimedAsync_非零退出时输出含错误尾部()
    {
        var bad = await ProcessRunner.RunTimedAsync("git", new[] { "definitely-not-a-git-command" });
        // AssertSuccess 的消息携带 stdout/stderr 尾部（截断到 400 字符）
        var act = () => bad.AssertSuccess("ctx");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*输出尾部*");
    }
}

/// <summary>
/// RepoGuard：语料删除白名单（仓外放行/语料根内放行/仓内其他位置拒绝）
/// </summary>
[Collection("RepoRoot")] // 串行：需要设置 CurrentDirectory 供 RepoPaths 定位
public class RepoGuardTests
{
    public RepoGuardTests()
    {
        // 从测试程序集目录向上找 Flint.slnx 锚定仓库根，再切 cwd（RepoPaths 走 cwd 逐级上溯）
        if (Environment.CurrentDirectory != RepoRoot)
        {
            Environment.CurrentDirectory = RepoRoot;
        }
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Flint.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("测试程序集目录上溯未找到 Flint.slnx");
        }
    }

    [Fact]
    public void EnsureDeletable_仓外路径_不抛异常()
    {
        var outside = Path.Combine(Path.GetTempPath(), "flint-guard-probe-outside");
        var act = () => RepoGuard.EnsureDeletable(outside);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureDeletable_语料根内_不抛异常()
    {
        var inside = Path.Combine(RepoGuard.CorpusRoot, "ssg-bench", "hugo-pub");
        var act = () => RepoGuard.EnsureDeletable(inside);
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureDeletable_仓内非语料路径拒绝()
    {
        var notCorpus = Path.Combine(RepoRoot, "src", "Flint.Core");
        var act = () => RepoGuard.EnsureDeletable(notCorpus);
        act.Should().Throw<InvalidOperationException>().WithMessage("*拒绝删除*");
    }
}

/// <summary>
/// Scanner：collect_hits 的注释行过滤（对应 bash grep -v ':[[:space:]]*//'）
/// </summary>
public class ScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("flint-scanner-probe.").FullName;

    [Fact]
    public void CollectHits_过滤整行注释()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllLines(
            Path.Combine(_root, "src", "Probe.cs"),
            new[]
            {
                "// Console.Write(\"commented\");",
                "/// <summary>Console.Write(\"doc\");</summary>",
                "Console.Write(\"real\");",
                "    // Console.Write(\"indented\");",
            },
            Encoding.UTF8);

        var hits = Scanner.CollectHits(_root, @"Console\.Write", "src");

        hits.Should().ContainSingle().Which.Should().Contain("real");
    }

    [Fact]
    public void CollectHits_排除obj与bin目录()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "obj"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "bin"));
        File.WriteAllText(Path.Combine(_root, "src", "obj", "Generated.cs"), "Console.Write(\"gen\");");
        File.WriteAllText(Path.Combine(_root, "src", "bin", "Copied.cs"), "Console.Write(\"bin\");");
        File.WriteAllText(Path.Combine(_root, "src", "Real.cs"), "Console.Write(\"real\");");

        var hits = Scanner.CollectHits(_root, @"Console\.Write", "src");

        hits.Should().ContainSingle().Which.Should().Contain("Real.cs");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响结论
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// AssertionStrengthCommand.FindZeroAssertionMethods：花括号不配对时按 Python 切片语义
/// 截断到文末（曾因 Substring 越界抛 ArgumentOutOfRangeException，白盒防回归）
/// </summary>
public class ZeroAssertionBraceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("flint-zero-assert-probe.").FullName;

    [Fact]
    public void 花括号不配对_截断到文末且不抛异常()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tests"));
        File.WriteAllText(
            Path.Combine(_root, "tests", "Broken.cs"),
            """
            namespace Probe;

            public class Broken
            {
                [Fact]
                public void 零断言_未闭合花括号()
                {
                    var x = 1;
                    // 方法花括号永不闭合：块取到文末，无断言调用 → 命中
            """,
            Encoding.UTF8);

        var hits = AssertionStrengthCommand.FindZeroAssertionMethods(_root);

        hits.Should().ContainSingle().Which.Should().Contain("零断言_未闭合花括号");
    }

    [Fact]
    public void 含断言的方法_不计入零断言()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tests2"));
        File.WriteAllText(
            Path.Combine(_root, "tests2", "Normal.cs"),
            """
            namespace Probe;

            public class Normal
            {
                [Fact]
                public void 有断言_花括号配对()
                {
                    true.Should().BeTrue();
                }
            }
            """,
            Encoding.UTF8);

        var hits = AssertionStrengthCommand.FindZeroAssertionMethods(_root);

        hits.Should().BeEmpty();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 同上
        }

        GC.SuppressFinalize(this);
    }
}
