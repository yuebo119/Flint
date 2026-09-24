// Flint 静态站点生成器
// OutputDirectoryEnsurer 行为测试：段级缓存的核心契约——
// 创建整链 / 幂等重复 / 删除后 Reset 可重建（CleanOutput 场景，头注释约束）

using Flint.Core.IO;
using AwesomeAssertions;
using Xunit;

namespace Flint.Core.Tests.IO;

public class OutputDirectoryEnsurerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "flint-ensurer-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        OutputDirectoryEnsurer.Reset();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 输出目录_不存在时创建整条链()
    {
        var deep = Path.Combine(_root, "a", "b", "c");

        OutputDirectoryEnsurer.Ensure(deep);

        Directory.Exists(deep).Should().BeTrue("Ensure 应创建从根到目标的整条目录链");
        Directory.Exists(Path.Combine(_root, "a")).Should().BeTrue();
        Directory.Exists(Path.Combine(_root, "a", "b")).Should().BeTrue();
    }

    [Fact]
    public void 已存在目录_重复Ensure幂等不抛()
    {
        var dir = Path.Combine(_root, "x", "y");
        OutputDirectoryEnsurer.Ensure(dir);

        var act = () => OutputDirectoryEnsurer.Ensure(dir);

        act.Should().NotThrow("缓存命中路径必须零副作用返回");
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void 删除后Reset_可重建_CleanOutput场景回归()
    {
        // 头注释约束的失效场景：构建入口 CleanOutput 删除输出目录后，
        // 旧缓存若命中会跳过重建 → 写入阶段报"目录不存在"
        var dir = Path.Combine(_root, "public", "posts");
        OutputDirectoryEnsurer.Ensure(dir);
        Directory.Delete(Path.Combine(_root, "public"), recursive: true);

        OutputDirectoryEnsurer.Reset();

        var act = () => OutputDirectoryEnsurer.Ensure(dir);
        act.Should().NotThrow("Reset 后缓存为空，必须真正重建被清理的目录");
        Directory.Exists(dir).Should().BeTrue();
    }

    [Fact]
    public void 空与null与尾分隔符_安全忽略()
    {
        var act = () =>
        {
            OutputDirectoryEnsurer.Ensure(null);
            OutputDirectoryEnsurer.Ensure("");
            OutputDirectoryEnsurer.Ensure(Path.DirectorySeparatorChar.ToString());
        };

        act.Should().NotThrow("空输入必须短路，不得抛异常中断构建");
    }

    [Fact]
    public void 兄弟目录_共享父段各自建叶子()
    {
        var d1 = Path.Combine(_root, "posts", "page-1");
        var d2 = Path.Combine(_root, "posts", "page-2");

        OutputDirectoryEnsurer.Ensure(d1);
        OutputDirectoryEnsurer.Ensure(d2);

        Directory.Exists(d1).Should().BeTrue();
        Directory.Exists(d2).Should().BeTrue("共享父段缓存命中后，兄弟叶子仍须各自创建");
    }
}
