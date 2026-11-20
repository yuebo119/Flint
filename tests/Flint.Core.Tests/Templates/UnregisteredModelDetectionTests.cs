// Flint 静态站点生成器
// 未登记类型检测通道的自验证（R1 审计 HYG-C2）：
// 若检测失灵，"19 主题零警告"就是假阴性。本测试构造一次已知未登记 Flint 类型的
// 成员访问，断言"拒绝 + 记录"——证明封闭通道有效后，"真实站点零命中"才可作证据。

using Flint.Core.Models;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class UnregisteredModelDetectionTests
{
    [Fact]
    public void 未登记Flint类型的成员访问_被拒绝并记录()
    {
        FlintMemberAccessStrategy.UnregisteredModelAccesses.Clear();
        try
        {
            var strategy = new FlintMemberAccessStrategy();
            // FrontMatter 是 Flint.Core.Models 下的模型类型，不在 FluidRegister 清单里
            var accessor = strategy.GetAccessor(typeof(FrontMatter), "Title", StringComparer.Ordinal);

            // 封闭式策略：未登记类型拒绝访问（模板取空值），并留证据
            Assert.Null(accessor);
            Assert.True(
                FlintMemberAccessStrategy.UnregisteredModelAccesses.ContainsKey(typeof(FrontMatter)),
                "检测通道失灵：未登记类型的访问未被记录");
        }
        finally
        {
            FlintMemberAccessStrategy.UnregisteredModelAccesses.Clear();
        }
    }

    [Fact]
    public void 已登记类型的成员访问_不进入检测集合()
    {
        FlintMemberAccessStrategy.UnregisteredModelAccesses.Clear();
        try
        {
            var strategy = new FlintMemberAccessStrategy();
            _ = strategy.GetAccessor(typeof(ReadingTimeView), "TotalMinutes", StringComparer.Ordinal);
            Assert.False(
                FlintMemberAccessStrategy.UnregisteredModelAccesses.ContainsKey(typeof(ReadingTimeView)),
                "已登记类型被误判为未登记");
        }
        finally
        {
            FlintMemberAccessStrategy.UnregisteredModelAccesses.Clear();
        }
    }

    [Fact]
    public void 非Flint命名空间类型_不进入检测集合()
    {
        FlintMemberAccessStrategy.UnregisteredModelAccesses.Clear();
        try
        {
            var strategy = new FlintMemberAccessStrategy();
            _ = strategy.GetAccessor(typeof(string), "Length", StringComparer.Ordinal);
            Assert.Empty(FlintMemberAccessStrategy.UnregisteredModelAccesses);
        }
        finally
        {
            FlintMemberAccessStrategy.UnregisteredModelAccesses.Clear();
        }
    }
}
