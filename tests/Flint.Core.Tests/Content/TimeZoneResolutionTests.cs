// Flint 静态站点生成器
// timeZone 配置解析契约测试
//
// 回归防护：Release 发布形态开启 InvariantGlobalization，ICU 的 IANA 数据库不可用，
// FindSystemTimeZoneById("Asia/Shanghai")（README 示例值）会抛异常并中止构建。
// ResolveTimeZone 先直查、再走内嵌静态映射表换 Windows ID 后复查。

using Flint.Core.Content;
using Xunit;

namespace Flint.Core.Tests.Content;

public sealed class TimeZoneResolutionTests
{
    [Fact]
    public void IANA时区名可解析()
    {
        var zone = ContentParserDateSetup.ResolveTimeZone("Asia/Shanghai");

        Assert.False(string.IsNullOrEmpty(zone.Id));
    }

    [Fact]
    public void Windows时区ID可解析()
    {
        var zone = ContentParserDateSetup.ResolveTimeZone("China Standard Time");

        Assert.False(string.IsNullOrEmpty(zone.Id));
    }

    [Fact]
    public void UTC可解析()
    {
        Assert.Equal(TimeSpan.Zero, ContentParserDateSetup.ResolveTimeZone("UTC").BaseUtcOffset);
    }

    [Fact]
    public void 非法时区名给出可定位的错误()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ContentParserDateSetup.ResolveTimeZone("Mars/Olympus"));

        Assert.Contains("Mars/Olympus", ex.Message, StringComparison.Ordinal);
        Assert.Contains("timeZone", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IANA到Windows映射表在进程内可用()
    {
        // 该映射表是 InvariantGlobalization 下 IANA 解析的唯一依靠，
        // 若它不存在（被裁剪/换实现）则 IANA 配置在 AOT 形态必然失败
        Assert.True(TimeZoneInfo.TryConvertIanaIdToWindowsId("Asia/Shanghai", out var windowsId));
        Assert.False(string.IsNullOrEmpty(windowsId));
        Assert.True(TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out _));
    }
}