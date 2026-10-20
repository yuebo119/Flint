// Flint 静态站点生成器
// Liquid 内容管线端到端测试（FLUID-MIGRATION-PLAN P5-4/P5-5）：
// 新站点的 .liquid 短代码与 .liquid render hook 经 Fluid 渲染，
// 不经 Scriban；Hugo 主题的 .html 短代码/hook 行为不变。

using Flint.IntegrationTests.Utilities;
using AwesomeAssertions;
using Xunit;

namespace Flint.IntegrationTests.Cli;

/// <summary>
/// Liquid 短代码与 render hook 的端到端验证
/// </summary>
[Trait("Category", "CLI")]
[Trait("Category", "EndToEnd")]
public sealed class CliLiquidContentPipelineTests : IAsyncLifetime
{
    private readonly CliTestRunner _cli;
    private readonly string _testDir;

    public CliLiquidContentPipelineTests()
    {
        _cli = new CliTestRunner();
        _testDir = Path.Combine(Path.GetTempPath(), "Flint-cli-pipeline", Guid.NewGuid().ToString("N")[..8]);
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_testDir);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _cli.Dispose();
        await Task.Delay(100);
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // 忽略清理错误
        }
    }

    /// <summary>.liquid 短代码与 .liquid render hook 在构建中生效</summary>
    [Fact]
    public async Task Liquid_短代码与_render_hook_生效()
    {
        var siteName = $"pipeline-{Guid.NewGuid():N}"[..13];
        var sitePath = Path.Combine(_testDir, siteName);

        var createResult = await _cli.NewSiteAsync(siteName, _testDir);
        createResult.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {createResult.ErrorOutput}");

        var layouts = Path.Combine(sitePath, "layouts");

        // .liquid 短代码：参数 + 位置参数 + inner
        var shortcodesDir = Path.Combine(layouts, "shortcodes");
        Directory.CreateDirectory(shortcodesDir);
        await File.WriteAllTextAsync(Path.Combine(shortcodesDir, "notice.liquid"), """
            <div class="notice notice-{{ params.type | default: 'info' }}">
            <strong>{{ params.title }}</strong>
            <p>{{ inner }}</p>
            </div>
            """);

        // .liquid render hook：链接定制
        var markupDir = Path.Combine(layouts, "_markup");
        Directory.CreateDirectory(markupDir);
        await File.WriteAllTextAsync(Path.Combine(markupDir, "render-link.liquid"), """
            <a href="{{ destination }}" class="liquid-hook"{% if title %} title="{{ title }}"{% endif %}>{{ text }}</a>
            """);

        // 内容：引用短代码 + 一个普通 Markdown 链接（触发 render hook）
        await File.WriteAllTextAsync(Path.Combine(sitePath, "content", "posts", "welcome.md"), """
            +++
            title = "短代码页面"
            date = 2026-03-01T10:00:00+08:00
            draft = false
            +++

            {{< notice type="warning" title="注意" >}}这是 Liquid 短代码渲染的提示。{{< /notice >}}

            参见 [Flint 官网](https://example.com "官网") 了解详情。
            """);

        var buildResult = await _cli.BuildAsync(workingDirectory: sitePath);

        buildResult.IsSuccess.Should().BeTrue($"构建应该成功，错误: {buildResult.ErrorOutput}\n{buildResult.StandardOutput}");

        var html = await File.ReadAllTextAsync(Path.Combine(sitePath, "public", "posts", "welcome", "index.html"));
        html.Should().Contain("notice-warning", "Liquid 短代码应经 Fluid 渲染（params.type）");
        html.Should().Contain("<strong>注意</strong>", "Liquid 短代码应经 Fluid 渲染（params.title）");
        html.Should().Contain("这是 Liquid 短代码渲染的提示", "Liquid 短代码 inner 应经 Fluid 渲染");
        html.Should().Contain("class=\"liquid-hook\"", "Liquid render hook 应经 Fluid 渲染");
        html.Should().Contain("href=\"https://example.com\"", "Liquid render hook 的 destination 应可用");
        html.Should().Contain("title=\"官网\"", "Liquid render hook 的 title 应可用");
    }

    /// <summary>engine: scriban 兼容模式下不认 .liquid 短代码（保持 Hugo 主题行为）</summary>
    [Fact]
    public async Task Scriban_模式_不认_liquid_短代码()    {
        var siteName = $"nosc-{Guid.NewGuid():N}"[..13];
        var sitePath = Path.Combine(_testDir, siteName);

        var createResult = await _cli.NewSiteAsync(siteName, _testDir);
        createResult.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {createResult.ErrorOutput}");

        var shortcodesDir = Path.Combine(sitePath, "layouts", "shortcodes");
        Directory.CreateDirectory(shortcodesDir);
        await File.WriteAllTextAsync(Path.Combine(shortcodesDir, "notice.liquid"), "<div>liquid</div>");

        // 配置切 scriban 模式。engine 须插在**任何段头之前**——追到 [params]
        // 段后 TOML 会把它归进 params 子表（2026-10-19 实测坑）
        var configPath = Path.Combine(sitePath, "Flint.toml");
        var config = await File.ReadAllTextAsync(configPath);
        var patched = config.Replace(
            "baseURL = \"http://localhost:1313/\"",
            "baseURL = \"http://localhost:1313/\"\nengine = \"scriban\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(configPath, patched);

        await File.WriteAllTextAsync(Path.Combine(sitePath, "content", "posts", "welcome.md"), """
            +++
            title = "兼容页"
            date = 2026-03-01T10:00:00+08:00
            draft = false
            +++

            {{< notice >}}x{{< /notice >}}
            """);

        var buildResult = await _cli.BuildAsync(workingDirectory: sitePath);

        // scriban 模式下 .liquid 短代码未注册 → 内容引用的短代码缺失，
        // 构建按短代码未找到失败（严格的 fail-fast；不是把 .liquid 当
        // Scriban 渲染出半成品内容——那会静默产出错误 HTML）
        buildResult.IsSuccess.Should().BeFalse("scriban 模式不注册 .liquid 短代码，缺失短代码应使构建失败");
        var output = buildResult.ErrorOutput + buildResult.StandardOutput;
        output.Should().Contain("notice", "失败原因应指向缺失的短代码");
        output.Should().NotContain("liquid-marker", "不应把 .liquid 短代码内容渲染出来");
    }

    /// <summary>disableShortcodes = true 时连内置 C# 短代码也不注册（P5-3）</summary>
    [Fact]
    public async Task DisableShortcodes_关闭全部短代码()
    {
        var siteName = $"nosc2-{Guid.NewGuid():N}"[..13];
        var sitePath = Path.Combine(_testDir, siteName);

        var createResult = await _cli.NewSiteAsync(siteName, _testDir);
        createResult.IsSuccess.Should().BeTrue($"创建站点应该成功，错误: {createResult.ErrorOutput}");

        var shortcodesDir = Path.Combine(sitePath, "layouts", "shortcodes");
        Directory.CreateDirectory(shortcodesDir);
        await File.WriteAllTextAsync(Path.Combine(shortcodesDir, "notice.liquid"), "<div>liquid</div>");

        // disableShortcodes 须插在**任何段头之前**（TOML 段归属，同 engine 键）
        var configPath = Path.Combine(sitePath, "Flint.toml");
        var config = await File.ReadAllTextAsync(configPath);
        var patched = config.Replace(
            "baseURL = \"http://localhost:1313/\"",
            "baseURL = \"http://localhost:1313/\"\ndisableShortcodes = true",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(configPath, patched);

        // 只引用内置 C# 短代码（figure）——证明 disableShortcodes 把引擎无关的
        // 内置短代码也关掉了（内容管线完全不经短代码）
        await File.WriteAllTextAsync(Path.Combine(sitePath, "content", "posts", "welcome.md"), """
            +++
            title = "关闭短代码页"
            date = 2026-03-01T10:00:00+08:00
            draft = false
            +++

            {{< figure src="/x.png" >}}
            """);

        var buildResult = await _cli.BuildAsync(workingDirectory: sitePath);

        // 内置短代码未注册 → 引用缺失，构建失败（fail-fast）
        buildResult.IsSuccess.Should().BeFalse("disableShortcodes 后引用内置短代码应使构建失败");
        var output = buildResult.ErrorOutput + buildResult.StandardOutput;
        output.Should().Contain("figure", "失败应指向未注册的内置短代码");
    }
}
