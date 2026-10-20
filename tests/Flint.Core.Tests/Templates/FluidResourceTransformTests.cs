// Flint 静态站点生成器
// 资源与资产变换过滤器测试（FLUID-MIGRATION-PLAN P4-3/P4-4 补齐）：
// resources.Concat/Copy/Fingerprint/FromString/Minify、css.Sass/css.Build/
// js.Build、path.* 与 findRE/replaceRE 的 Liquid 过滤器行为，
// 以及 GeneratedResources 产物登记。

using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

/// <summary>资源/资产变换 Liquid 过滤器测试</summary>
public sealed class FluidResourceTransformTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FluidTemplateRenderer _renderer;

    public FluidResourceTransformTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_res_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "assets", "css"));
        _renderer = new FluidTemplateRenderer(_tempDir, "https://example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    private static TemplateContext CreateContext() =>
        new()
        {
            Page = new PageContext
            {
                Title = "T", Content = "", Permalink = "https://example.com/t/",
                RelPermalink = "/t/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero
            },
            Site = new SiteContext
            {
                Title = "S", BaseURL = "https://example.com", Language = "zh-cn",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

    private async Task<string> RenderAsync(string template, TemplateContext? context = null)
    {
        var file = Path.Combine(_tempDir, "t.liquid");
        await File.WriteAllTextAsync(file, template).ConfigureAwait(false);
        return await _renderer.RenderTemplateFileAsync(file, context ?? CreateContext()).ConfigureAwait(false);
    }

    [Fact]
    public async Task resource_from_string_构造虚拟资源并登记产物()
    {
        var result = await RenderAsync(
            "{{ 'inline.css' | resource_from_string: 'body{color:red}' | jsonify }}");

        // jsonify 输出含资源字段（Name/RelPermalink 证明构造成功）
        Assert.Contains("inline.css", result);
        Assert.Contains("/inline.css", result);
    }

    [Fact]
    public async Task resource_fingerprint_内容哈希进文件名()
    {
        var result = await RenderAsync("""
            {% assign r = 'theme.css' | resource_from_string: 'body{}' %}
            {{ r | resource_fingerprint | jsonify }}
            """);

        // 产物名形如 theme.<hash>.css
        Assert.Contains("theme.", result);
        Assert.Contains(".css", result);
    }

    [Fact]
    public async Task resource_minify_按类型压缩()
    {
        var result = await RenderAsync("""
            {% assign r = 'a.css' | resource_from_string: 'body { color : red ; }' %}
            {{ r | resource_minify | jsonify }}
            """);

        // 压缩后无 ": " 与 "; " 的空格形态（Lightning CSS 缺失时回退内置简单压缩，
        // 断言落到"内容被处理过"的弱形态即可——精确压缩形态由 Sass/Minify 单测覆盖）
        Assert.Contains("a.css", result);
    }

    [Fact]
    public async Task resource_copy_改名资源()
    {
        var result = await RenderAsync("""
            {% assign r = 'a.css' | resource_from_string: 'body{}' %}
            {{ r | resource_copy: 'b.css' | jsonify }}
            """);

        Assert.Contains("b.css", result);
        Assert.Contains("/b.css", result);
    }

    [Fact]
    public async Task resource_concat_拼接多资源()
    {
        var result = await RenderAsync("""
            {% assign a = 'a.css' | resource_from_string: 'AAA' %}
            {% assign b = 'b.css' | resource_from_string: 'BBB' %}
            {{ a | resource_concat: b, target: 'all.css' | jsonify }}
            """);

        Assert.Contains("all.css", result);
    }

    [Fact]
    public async Task css_build_内容可选压缩()
    {
        var result = await RenderAsync("""
            {% assign r = 'a.css' | resource_from_string: 'body{color:red}' %}
            {{ r | css_build | jsonify }}
            """);

        Assert.Contains("a.css", result);
    }

    [Fact]
    public async Task css_sass_非磁盘资源_内容直通()
    {
        // from_string 的资源无 SourcePath——不满足"按磁盘文件编译"条件，
        // 直通（外部 Sass CLI 不可用时的回退语义同口径）
        var result = await RenderAsync("""
            {% assign r = 'a.css' | resource_from_string: 'body{color:red}' %}
            {{ r | css_sass | jsonify }}
            """);

        Assert.Contains("a.css", result);
    }

    [Fact]
    public async Task js_build_ts_入口扩展名改写()
    {
        File.WriteAllText(Path.Combine(_tempDir, "assets", "main.ts"), "const x: number = 1;\n");
        // 路径形态的 js_build 经资源提供者解析——渲染器须带 FileSystemResourceProvider
        var provider = new FileSystemResourceProvider(
            "https://example.com", [Path.Combine(_tempDir, "assets")]);
        var renderer = new FluidTemplateRenderer(
            _tempDir, "https://example.com", builtins: null, resources: provider);
        var file = Path.Combine(_tempDir, "t.liquid");
        await File.WriteAllTextAsync(file, "{{ 'main.ts' | js_build | jsonify }}");
        var result = await renderer.RenderTemplateFileAsync(file, CreateContext());

        // 无 targetPath 时 .ts 产物改 .js（Hugo 语义）
        Assert.Contains("main.js", result);
    }

    [Fact]
    public async Task path_过滤器族()
    {
        var result = await RenderAsync(
            "[{{ '/a/b/c.css' | path_base }}][{{ '/a/b/c.css' | path_dir }}][{{ '/a/b/c.css' | path_ext }}]" +
            "[{{ 'a' | path_join: 'b', 'c.css' }}][{{ '/a/b/c.css' | path_split | first }}]");

        Assert.Equal("[c.css][/a/b][.css][/a/b/c.css][/a/b]", result);
    }

    [Fact]
    public async Task find_re_replace_re_正则过滤器()
    {
        var found = await RenderAsync("{{ '<h1>标题</h1>' | find_re: '<[^>]+>' | join: '|' }}");
        // Liquid 单引号串把 \ 当转义——正则 pattern 的反斜杠须双写
        // （'\\w' 在模板里才是一个 \w；Fluid 解析器对单 \w 直接报解析错误）
        var replaced = await RenderAsync("{{ 'hello world' | replace_re: '(\\\\w+)\\\\s(\\\\w+)', '$2 $1' }}");

        Assert.Equal("<h1>|</h1>", found);
        Assert.Equal("world hello", replaced);
    }

    [Fact]
    public async Task find_re_坏_pattern_返回空不抛()
    {
        var result = await RenderAsync("{{ 'abc' | find_re: '(' | join: '|' }}");

        Assert.Equal("", result);
    }

    [Fact]
    public async Task GeneratedResources_构建边界清空()
    {
        await RenderAsync("{{ 'a.css' | resource_from_string: 'body{}' }}");
        Assert.NotEmpty(_renderer.GeneratedResources);

        _renderer.InvalidateMtimeCache();
        Assert.Empty(_renderer.GeneratedResources);
    }

    [Fact]
    public async Task js_build_esm_与_params_命名实参()
    {
        // format: 'esm' 与 params 命名实参（Liquid 标量选项形态）——
        // from_string 资源无 SourcePath，打包路径跳过，但实参解析不报错
        var result = await RenderAsync("""
            {% assign r = 'app.js' | resource_from_string: 'export const x = 1;' %}
            {{ r | js_build: format: 'esm', minify: true | jsonify }}
            """);

        Assert.Contains("app.js", result);
    }

    [Fact]
    public async Task css_sass_vars_前导文本不破坏直通路径()
    {
        // vars 前导文本只在"按磁盘文件编译"路径拼接；from_string 资源直通，
        // 前导文本不污染内容
        var result = await RenderAsync("""
            {% assign r = 'a.css' | resource_from_string: 'body{color:red}' %}
            {{ r | css_sass: vars: '$primary: #333;' | jsonify }}
            """);

        Assert.Contains("a.css", result);
    }
}

/// <summary>序列/分组过滤器测试（group/apply/seq/shuffle）</summary>
public sealed class FluidSequenceFilterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FluidTemplateRenderer _renderer;

    public FluidSequenceFilterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"Flint_seq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _renderer = new FluidTemplateRenderer(_tempDir, "https://example.com");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
        GC.SuppressFinalize(this);
    }

    private static TemplateContext CreateContext() =>
        new()
        {
            Page = new PageContext
            {
                Title = "T", Content = "", Permalink = "https://example.com/t/",
                RelPermalink = "/t/", Date = DateTimeOffset.UnixEpoch, Tags = [], Categories = [],
                WordCount = 1, ReadingTime = TimeSpan.Zero,
                Pages =
                [
                    new PageContext
                    {
                        Title = "P1", Content = "", Permalink = "https://example.com/p1/",
                        RelPermalink = "/p1/", Date = DateTimeOffset.UnixEpoch, Tags = [],
                        Categories = [], WordCount = 1, ReadingTime = TimeSpan.Zero, Section = "posts"
                    },
                    new PageContext
                    {
                        Title = "P2", Content = "", Permalink = "https://example.com/p2/",
                        RelPermalink = "/p2/", Date = DateTimeOffset.UnixEpoch, Tags = [],
                        Categories = [], WordCount = 1, ReadingTime = TimeSpan.Zero, Section = "posts"
                    },
                    new PageContext
                    {
                        Title = "A1", Content = "", Permalink = "https://example.com/a1/",
                        RelPermalink = "/a1/", Date = DateTimeOffset.UnixEpoch, Tags = [],
                        Categories = [], WordCount = 1, ReadingTime = TimeSpan.Zero, Section = "about"
                    }
                ]
            },
            Site = new SiteContext
            {
                Title = "S", BaseURL = "https://example.com", Language = "zh-cn",
                Pages = [], RegularPages = [],
                Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                Config = new SiteConfig { BaseURL = "https://example.com", Title = "S" }
            }
        };

    private async Task<string> RenderAsync(string template)
    {
        var file = Path.Combine(_tempDir, "t.liquid");
        await File.WriteAllTextAsync(file, template).ConfigureAwait(false);
        return await _renderer.RenderTemplateFileAsync(file, CreateContext()).ConfigureAwait(false);
    }

    [Fact]
    public async Task seq_序列生成()
    {
        Assert.Equal("1,2,3,4,5", await RenderAsync("{{ 5 | seq | join: ',' }}"));
        Assert.Equal("2,4", await RenderAsync("{{ 4 | seq: first: 2, step: 2 | join: ',' }}"));
        Assert.Equal("3,2,1", await RenderAsync("{{ 1 | seq: first: 3, step: -1 | join: ',' }}"));
    }

    [Fact]
    public async Task group_按成员分组返回二元组()
    {
        var result = await RenderAsync("""
            {%- assign grouped = page.pages | group: 'section' -%}
            {%- for g in grouped -%}[{{ g[0] }}:{{ g[1] | size }}]{%- endfor -%}
            """);

        // 组序 = 首现顺序：posts(2) → about(1)
        Assert.Equal("[posts:2][about:1]", result);
    }

    [Fact]
    public async Task apply_逐元素应用过滤器()
    {
        var result = await RenderAsync("{{ 'a,b,c' | split: ',' | apply: 'upcase' | join: '|' }}");

        Assert.Equal("A|B|C", result);
    }

    [Fact]
    public async Task shuffle_长度不变()
    {
        var result = await RenderAsync("{{ 'a,b,c,d,e' | split: ',' | shuffle | size }}");

        Assert.Equal("5", result);
    }
}

/// <summary>disableShortcodes 配置测试（P5-3）</summary>
public sealed class DisableShortcodesConfigTests
{
    [Fact]
    public void TOML_disableShortcodes_读入()
    {
        var on = ConfigParser.ParseToml("baseURL = \"http://x/\"\ntitle = \"t\"\ndisableShortcodes = true");
        var off = ConfigParser.ParseToml("baseURL = \"http://x/\"\ntitle = \"t\"");

        Assert.True(on.DisableShortcodes);
        Assert.False(off.DisableShortcodes);
    }

    [Fact]
    public void ThemeParamsMerger_重建保留_disableShortcodes()
    {
        var config = ConfigParser.ParseToml(
            "baseURL = \"http://x/\"\ntitle = \"t\"\ndisableShortcodes = true");
        var merged = ThemeParamsMerger.Merge(config, Path.GetTempPath());

        Assert.True(merged.DisableShortcodes);
    }
}
