// Flint 静态站点生成器
// TOML front matter 写回的嵌套结构契约测试。
// 回归防护：IEnumerable 分支曾用标量 Where 滤空嵌套 dict/list，
// 写出 params = [] 的空壳（加载→保存往返丢数据）。

using Flint.Core.Content;
using Flint.Core.Models;
using Xunit;

namespace Flint.Core.Tests.Content;

public sealed class FrontMatterTomlRoundTripTests
{
    [Fact]
    public void Serialize_TomlParams含嵌套字典时拒绝静默滤空()
    {
        var parser = new FrontMatterParser();
        var fm = new FrontMatter
        {
            Title = "T",
            Params = new Dictionary<string, object>
            {
                ["nested"] = new Dictionary<string, object> { ["k"] = "v" }
            }
        };

        var ex = Assert.Throws<NotSupportedException>(
            () => parser.Serialize(fm, FrontMatterFormat.Toml));

        Assert.Contains("nested", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_TomlParams标量数组仍正常写出()
    {
        var parser = new FrontMatterParser();
        var fm = new FrontMatter
        {
            Title = "T",
            Params = new Dictionary<string, object>
            {
                ["labels"] = new List<object> { "a", "b" }
            }
        };

        var toml = parser.Serialize(fm, FrontMatterFormat.Toml);

        Assert.Contains("labels = [\"a\", \"b\"]", toml, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_TomlParams含嵌套列表时拒绝静默滤空()
    {
        var parser = new FrontMatterParser();
        var fm = new FrontMatter
        {
            Title = "T",
            Params = new Dictionary<string, object>
            {
                ["matrix"] = new List<object>
                {
                    new List<object> { 1, 2 }
                }
            }
        };

        Assert.Throws<NotSupportedException>(
            () => parser.Serialize(fm, FrontMatterFormat.Toml));
    }
}
