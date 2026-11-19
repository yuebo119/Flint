// Flint 静态站点生成器
// 2026-11-20 R1 审计修复锁定（HYG-D01/C1）：os 系文件函数的站点根护栏。
// 修复前 read_file/read_dir/file_stat/file_exists/fileExists 可读宿主任意
// 路径（第三方主题可把 ~/.ssh/密钥文件内容写进产物）；修复后对齐 Hugo
// os.ReadFile 的 workingDir 安全策略——逃逸路径一律按"不存在"处理。
// WorkingDir 未配置（独立使用/测试）保持宽松，既有行为不破坏。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class OsFunctionsRootGuardTests : IDisposable
{
    private readonly string _siteDir;
    private readonly string _outsideDir;
    private readonly ScribanTemplateRenderer _renderer;
    private readonly ScribanTemplateRenderer _looseRenderer;

    public OsFunctionsRootGuardTests()
    {
        _siteDir = Path.Combine(Path.GetTempPath(), $"Flint_osguard_{Guid.NewGuid():N}");
        _outsideDir = Path.Combine(Path.GetTempPath(), $"Flint_osguard_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_siteDir, "layouts"));
        Directory.CreateDirectory(_outsideDir);
        // 根内靶子与根外靶子（绝对路径统一用正斜杠，规避 Scriban 字符串转义歧义）
        File.WriteAllText(Path.Combine(_siteDir, "content.txt"), "INSIDE");
        File.WriteAllText(Path.Combine(_outsideDir, "secret.txt"), "SECRET-OUTSIDE");

        _renderer = new ScribanTemplateRenderer(
            Path.Combine(_siteDir, "layouts"),
            "https://example.com",
            new TemplateEnvironmentInfo { WorkingDir = _siteDir },
            resources: null);
        // 宽松回归：WorkingDir 未配置 = 独立使用场景，行为不变
        _looseRenderer = new ScribanTemplateRenderer(
            Path.Combine(_siteDir, "layouts"),
            "https://example.com",
            new TemplateEnvironmentInfo(),
            resources: null);
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _siteDir, _outsideDir })
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }
        GC.SuppressFinalize(this);
    }

    private string OutsidePath => _outsideDir.Replace('\\', '/') + "/secret.txt";

    private string OutsideRelative => "../" + Path.GetFileName(_outsideDir) + "/secret.txt";

    /// <summary>渲染单个函数调用（路径参数由调用方拼好引号）</summary>
    private Task<string> RenderCallAsync(ScribanTemplateRenderer renderer, string call) =>
        RenderAsync(renderer, "{{ " + call + " }}");

    private static string Q(string path) => "\"" + path + "\"";

    private int _tplSeq;

    /// <summary>渲染一段 Scriban 模板串（每次唯一模板名——加载器的 50ms mtime
    /// TTL 缓存会让同名的连续改写在全量跑时读到旧内容，属测试自造竞态）</summary>
    private async Task<string> RenderAsync(ScribanTemplateRenderer renderer, string snippet)
    {
        var name = $"t{++_tplSeq}";
        File.WriteAllText(Path.Combine(_siteDir, "layouts", name + ".html"), snippet);
        var result = await renderer.RenderAsync(name, CreateContext()).ConfigureAwait(false);
        return result.Trim();
    }

    private static TemplateContext CreateContext() => new()
    {
        Page = new PageContext
        {
            Title = "探测页",
            Content = "<p>x</p>",
            Permalink = "https://example.com/p/",
            RelPermalink = "/p/",
            Tags = [],
            Categories = [],
            Date = DateTimeOffset.UtcNow,
            ReadingTime = TimeSpan.FromMinutes(1),
            WordCount = 10,
        },
        Site = new SiteContext
        {
            Title = "探测站",
            BaseURL = "https://example.com",
            Language = "zh-cn",
            Pages = [],
            RegularPages = [],
            Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
            Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
            Data = new Dictionary<string, object>(),
            Params = new Dictionary<string, object>(),
            Translations = new Dictionary<string, string>(),
            PaginatorPages = [],
            Config = new SiteConfig { BaseURL = "https://example.com", Title = "探测站" },
        },
    };

    [Fact]
    public async Task read_file_根内相对路径_正常读取()
    {
        Assert.Equal("INSIDE", await RenderCallAsync(_renderer, "read_file " + Q("content.txt")));
    }

    [Fact]
    public async Task read_file_根外绝对路径_按不存在处理()
    {
        // 修复前（红）：返回 SECRET-OUTSIDE——第三方主题可读宿主任意文件并写进产物
        Assert.Equal("", await RenderCallAsync(_renderer, "read_file " + Q(OutsidePath)));
    }

    [Fact]
    public async Task read_file_路径穿越_按不存在处理()
    {
        Assert.Equal("", await RenderCallAsync(_renderer, "read_file " + Q(OutsideRelative)));
    }

    [Fact]
    public async Task fileExists_根外false_根内true()
    {
        var outside = await RenderCallAsync(_renderer, "fileExists " + Q(OutsidePath));
        var inside = await RenderCallAsync(_renderer, "fileExists " + Q("content.txt"));
        Assert.Equal("false", outside);
        Assert.Equal("true", inside);
    }

    [Fact]
    public async Task read_dir_根外_空数组()
    {
        var outsideDirOnly = _outsideDir.Replace('\\', '/');
        Assert.Equal("[]", await RenderCallAsync(_renderer, "read_dir " + Q(outsideDirOnly)));
    }

    [Fact]
    public async Task file_stat_根外_exists_false()
    {
        Assert.Equal(
            "false",
            await RenderAsync(_renderer, "{{ (file_stat " + Q(OutsidePath) + ").exists }}"));
    }

    [Fact]
    public async Task WorkingDir_未配置_保持宽松()
    {
        // 独立使用场景（无 WorkingDir）不收紧——既有行为回归锁定
        Assert.Equal(
            "SECRET-OUTSIDE",
            await RenderCallAsync(_looseRenderer, "read_file " + Q(OutsidePath)));
    }
}
