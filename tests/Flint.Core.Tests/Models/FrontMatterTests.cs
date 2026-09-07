// Flint 核心库测试
// FrontMatter 数据类型单元测试

using Flint.Core.Models;
using FluentAssertions;
using Xunit;

namespace Flint.Core.Tests.Models;

/// <summary>
/// FrontMatter 类的单元测试
/// </summary>
public class FrontMatterTests
{
    [Fact]
    public void FrontMatter_应该正确存储必需属性()
    {
        // Arrange & Act
        var frontMatter = new FrontMatter
        {
            Title = "测试标题"
        };

        // Assert
        frontMatter.Title.Should().Be("测试标题");
    }

    [Fact]
    public void FrontMatter_应该有正确的默认值()
    {
        // Arrange & Act
        var frontMatter = new FrontMatter
        {
            Title = "测试"
        };

        // Assert
        frontMatter.Draft.Should().BeFalse();
        frontMatter.Tags.Should().BeEmpty();
        frontMatter.Categories.Should().BeEmpty();
        frontMatter.Aliases.Should().BeEmpty();
        frontMatter.Authors.Should().BeEmpty();
        frontMatter.Keywords.Should().BeEmpty();
        frontMatter.Outputs.Should().BeEmpty();
        frontMatter.Weight.Should().Be(0);
        frontMatter.Format.Should().Be(FrontMatterFormat.Yaml);
    }

    [Fact]
    public void FrontMatter_应该正确存储日期属性()
    {
        // Arrange
        var date = new DateTimeOffset(2024, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var lastMod = new DateTimeOffset(2024, 1, 20, 14, 0, 0, TimeSpan.Zero);

        // Act
        var frontMatter = new FrontMatter
        {
            Title = "测试",
            Date = date,
            LastMod = lastMod
        };

        // Assert
        frontMatter.Date.Should().Be(date);
        frontMatter.LastMod.Should().Be(lastMod);
    }

    [Fact]
    public void FrontMatter_应该正确存储标签和分类()
    {
        // Arrange
        var tags = new[] { "csharp", "dotnet", "性能优化" };
        var categories = new[] { "技术", "编程" };

        // Act
        var frontMatter = new FrontMatter
        {
            Title = "测试",
            Tags = tags,
            Categories = categories
        };

        // Assert
        frontMatter.Tags.Should().BeEquivalentTo(tags);
        frontMatter.Categories.Should().BeEquivalentTo(categories);
    }

    [Fact]
    public void FrontMatter_应该正确存储自定义参数()
    {
        // Arrange
        var customParams = new Dictionary<string, object>
        {
            ["featured"] = true,
            ["image"] = "/images/cover.jpg",
            ["rating"] = 5
        };

        // Act
        var frontMatter = new FrontMatter
        {
            Title = "测试",
            Params = customParams
        };

        // Assert
        frontMatter.Params.Should().ContainKey("featured");
        frontMatter.Params["featured"].Should().Be(true);
        frontMatter.Params["image"].Should().Be("/images/cover.jpg");
        frontMatter.Params["rating"].Should().Be(5);
    }

    [Fact]
    public void FrontMatter_应该正确存储草稿状态()
    {
        // Arrange & Act
        var draftPost = new FrontMatter
        {
            Title = "草稿文章",
            Draft = true
        };

        var publishedPost = new FrontMatter
        {
            Title = "已发布文章",
            Draft = false
        };

        // Assert
        draftPost.Draft.Should().BeTrue();
        publishedPost.Draft.Should().BeFalse();
    }

    [Fact]
    public void FrontMatter_应该正确存储布局和类型()
    {
        // Arrange & Act
        var frontMatter = new FrontMatter
        {
            Title = "测试",
            Layout = "single",
            Type = "post"
        };

        // Assert
        frontMatter.Layout.Should().Be("single");
        frontMatter.Type.Should().Be("post");
    }

    [Fact]
    public void FrontMatter_应该正确存储URL别名()
    {
        // Arrange
        var aliases = new[] { "/old-url/", "/another-old-url/" };

        // Act
        var frontMatter = new FrontMatter
        {
            Title = "测试",
            Slug = "custom-slug",
            Aliases = aliases
        };

        // Assert
        frontMatter.Slug.Should().Be("custom-slug");
        frontMatter.Aliases.Should().BeEquivalentTo(aliases);
    }

    [Fact]
    public void FrontMatter_应该正确存储作者信息()
    {
        // Arrange
        var authors = new[] { "张三", "李四" };

        // Act
        var frontMatter = new FrontMatter
        {
            Title = "测试",
            Author = "张三",
            Authors = authors
        };

        // Assert
        frontMatter.Author.Should().Be("张三");
        frontMatter.Authors.Should().BeEquivalentTo(authors);
    }

    [Fact]
    public void FrontMatter_应该正确存储格式类型()
    {
        // Arrange & Act
        var yamlFrontMatter = new FrontMatter
        {
            Title = "YAML",
            Format = FrontMatterFormat.Yaml
        };

        var tomlFrontMatter = new FrontMatter
        {
            Title = "TOML",
            Format = FrontMatterFormat.Toml
        };

        var jsonFrontMatter = new FrontMatter
        {
            Title = "JSON",
            Format = FrontMatterFormat.Json
        };

        // Assert
        yamlFrontMatter.Format.Should().Be(FrontMatterFormat.Yaml);
        tomlFrontMatter.Format.Should().Be(FrontMatterFormat.Toml);
        jsonFrontMatter.Format.Should().Be(FrontMatterFormat.Json);
    }

    [Fact]
    public void FrontMatter_应该正确存储菜单配置()
    {
        // Arrange
        var menus = new Dictionary<string, MenuEntry>
        {
            ["main"] = new MenuEntry
            {
                Name = "首页",
                Weight = 1,
                Identifier = "home"
            }
        };

        // Act
        var frontMatter = new FrontMatter
        {
            Title = "测试",
            Menus = menus
        };

        // Assert
        frontMatter.Menus.Should().ContainKey("main");
        frontMatter.Menus!["main"].Name.Should().Be("首页");
        frontMatter.Menus["main"].Weight.Should().Be(1);
    }
}
