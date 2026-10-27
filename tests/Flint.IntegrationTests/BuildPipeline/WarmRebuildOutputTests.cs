// Flint 静态站点生成器
// 热重建（输出目录已存在、不清理）写盘契约：目录未预建也必须落盘
//
// 背景（2026-10-27）：热重建改走"写优先、缺目录再补建"——已存在目录的
// CreateDirectory 在 NTFS + 过滤驱动下仍是一次完整系统调用（实测 70μs/次），
// 万页站点等于每页白付一次。补建兜底必须对下列两种真实场景成立：
// 新增页面（新目录）与被外部删除的产物目录。

using AwesomeAssertions;
using Flint.Core.Models;
using Xunit;

namespace Flint.IntegrationTests.BuildPipeline;

/// <summary>
/// 热重建写盘：预建目录被跳过时，缺目录由写入路径补建重试
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "BuildPipeline")]
public sealed class WarmRebuildOutputTests : IDisposable
{
    private readonly string _siteRoot;

    public WarmRebuildOutputTests()
    {
        _siteRoot = Path.Combine(Path.GetTempPath(), $"flint-warm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_siteRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_siteRoot))
            {
                Directory.Delete(_siteRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // 杀软瞬时锁：清理失败不影响测试结论
        }
    }

    private BuildOptions Options() => new()
    {
        SourcePath = _siteRoot,
        OutputPath = Path.Combine(_siteRoot, "public")
    };

    private void WriteSite()
    {
        Directory.CreateDirectory(Path.Combine(_siteRoot, "content", "posts"));
        Directory.CreateDirectory(Path.Combine(_siteRoot, "layouts"));

        File.WriteAllText(Path.Combine(_siteRoot, "Flint.toml"),
            "baseURL = \"http://localhost:1313/\"\ntitle = \"warm-test\"\n");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "single.html"),
            "<article>{{ page.title }}:{{ page.content }}</article>");
        File.WriteAllText(Path.Combine(_siteRoot, "layouts", "list.html"),
            "<ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>");
        File.WriteAllText(Path.Combine(_siteRoot, "content", "posts", "a.md"),
            "---\ntitle: \"A\"\n---\n\nContent A\n");
    }

    [Fact]
    public async Task 热重建_新增页面目录未预建_仍落盘()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var newPage = Path.Combine(_siteRoot, "content", "posts", "b.md");
        File.WriteAllText(newPage, "---\ntitle: \"B\"\n---\n\nContent B\n");

        // 输出根已存在且不清理 → 本次构建走写优先路径；b 的目录此前不存在
        var outputDir = Path.Combine(_siteRoot, "public", "posts", "b");
        Directory.Exists(outputDir).Should().BeFalse("前置条件：新页面的输出目录尚不存在");

        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var html = Path.Combine(outputDir, "index.html");
        File.Exists(html).Should().BeTrue("热重建下缺目录必须由写入路径补建");
        File.ReadAllText(html).Should().Contain("B");
    }

    [Fact]
    public async Task 热重建_产物目录被外部删除_重建后恢复()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var pageDir = Path.Combine(_siteRoot, "public", "posts", "a");
        File.Exists(Path.Combine(pageDir, "index.html")).Should().BeTrue("前置条件：首轮构建已产出");
        Directory.Delete(pageDir, recursive: true);

        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        var restored = Path.Combine(pageDir, "index.html");
        File.Exists(restored).Should().BeTrue("产物目录被外部删除后，热重建必须重新创建目录并落盘");
        File.ReadAllText(restored).Should().Contain("A");
    }

    [Fact]
    public async Task 冷构建_CleanOutput_仍产出全部页面()
    {
        WriteSite();
        var builder = TestSiteFactory.CreateBuilder(_siteRoot);
        (await builder.BuildAsync(Options())).Success.Should().BeTrue();

        // CleanOutput 会删掉输出根：本次构建不得走热重建快速路径
        var cleanOptions = new BuildOptions
        {
            SourcePath = _siteRoot,
            OutputPath = Path.Combine(_siteRoot, "public"),
            CleanOutput = true
        };
        (await builder.BuildAsync(cleanOptions)).Success.Should().BeTrue();

        File.Exists(Path.Combine(_siteRoot, "public", "posts", "a", "index.html"))
            .Should().BeTrue("--clean 后页面必须完整重建");
    }
}
