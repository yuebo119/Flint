// Flint 静态站点生成器
// Front matter 键名大小写契约测试
//
// 回归防护：快速路径白名单用 OrdinalIgnoreCase 放行 `Title:`/`Draft:`，
// 取值却是精确键查找，导致标题静默变 Untitled、`Draft: true` 的草稿照常发布。
// Hugo 侧 mapstructure 反序列化对键名大小写不敏感，两种写法必须等价。

using Flint.Core.Content;
using Xunit;

namespace Flint.Core.Tests.Content;

public sealed class FrontMatterKeyCaseTests
{
    private static string Parse(string text)
    {
        var parser = new FrontMatterParser();
        Assert.True(parser.TryParse(text, out var fm, out _), "front matter 应解析成功");
        return fm!.Title;
    }

    private static bool ParseDraft(string text)
    {
        var parser = new FrontMatterParser();
        Assert.True(parser.TryParse(text, out var fm, out _), "front matter 应解析成功");
        return fm!.Draft;
    }

    [Fact]
    public void 大写Title键与小写等价()
    {
        Assert.Equal("大写标题", Parse("---\nTitle: \"大写标题\"\ndate: 2024-01-02\n---\n正文\n"));
        Assert.Equal("大写标题", Parse("---\ntitle: \"大写标题\"\ndate: 2024-01-02\n---\n正文\n"));
    }

    [Fact]
    public void 大写Draft键与小写等价()
    {
        Assert.True(ParseDraft("---\ntitle: \"T\"\nDraft: true\n---\n正文\n"));
        Assert.True(ParseDraft("---\ntitle: \"T\"\ndraft: true\n---\n正文\n"));
        Assert.False(ParseDraft("---\ntitle: \"T\"\nDraft: false\n---\n正文\n"));
    }

    [Fact]
    public void 混合大小写键publishDate与expiryDate生效()
    {
        var parser = new FrontMatterParser();
        Assert.True(parser.TryParse(
            "---\ntitle: \"T\"\nPublishDate: 2024-02-03\nExpiryDate: 2030-01-01\n---\n正文\n",
            out var fm,
            out _));

        Assert.Equal(2024, fm!.PublishDate?.Year);
        Assert.Equal(2030, fm.ExpiryDate?.Year);
    }

    [Fact]
    public void 大写Date键参与日期解析()
    {
        var parser = new FrontMatterParser();
        Assert.True(parser.TryParse(
            "---\ntitle: \"T\"\nDate: 2024-05-06\n---\n正文\n",
            out var fm,
            out _));

        Assert.Equal(2024, fm!.Date?.Year);
        Assert.Equal(5, fm.Date?.Month);
    }

    [Fact]
    public void 全小写内容不受归一化影响()
    {
        var parser = new FrontMatterParser();
        Assert.True(parser.TryParse(
            "---\ntitle: \"T\"\ndraft: true\ntags: [\"a\", \"b\"]\n---\n正文\n",
            out var fm,
            out _));

        Assert.Equal("T", fm!.Title);
        Assert.True(fm.Draft);
        Assert.Equal(2, fm.Tags.Count);
    }
}