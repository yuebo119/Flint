// Flint 静态站点生成器
// FluidRegister 清单一致性锁定（R1 审计 HYG-C2 的前置保障）：
// FlintTemplateOptionsBuilder 上的 [FluidRegister] 特性与同类的 RegisteredTypes
// 集合必须完全一致——两者漂移会让"未登记类型检测"给出错误结论（漏登记的类型
// 被当已登记，封闭化后静默失效）。

using System.Reflection;
using Flint.Core.Templates;
using Fluid;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class FluidRegisterConsistencyTests
{
    [Fact]
    public void 特性清单与RegisteredTypes集合完全一致()
    {
        var fromAttributes = typeof(FlintTemplateOptionsBuilder)
            .GetCustomAttributes<FluidRegisterAttribute>()
            .Select(a => a.Type)
            .ToHashSet();

        var fromSet = FlintTemplateOptionsBuilder.RegisteredTypes;

        Assert.True(
            fromAttributes.SetEquals(fromSet),
            "漂移：仅在特性中 = [" + string.Join(", ", fromAttributes.Except(fromSet).Select(t => t.Name))
            + "]；仅在集合中 = [" + string.Join(", ", fromSet.Except(fromAttributes).Select(t => t.Name)) + "]");
    }

    [Fact]
    public void 登记清单非空且无重复()
    {
        var types = typeof(FlintTemplateOptionsBuilder)
            .GetCustomAttributes<FluidRegisterAttribute>()
            .Select(a => a.Type)
            .ToList();
        Assert.NotEmpty(types);
        Assert.Equal(types.Count, types.Distinct().Count());
    }
}
