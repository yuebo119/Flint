// Flint 开发运维工具
// engine-probe：Fluid 渲染路径的**运行期装配探针**（FLUID-MIGRATION-PLAN P6-3）。
// 与 EngineIsolationTests 的静态 IL 扫描互补：本探针在独立进程里**只**经
// FluidTemplateRenderer 渲染 .liquid 模板，渲染后检查 Scriban 程序集是否被
// 加载——验证"Fluid 热路径运行期不拖入 Scriban"（.NET 程序集按需加载，
// 未触及的类型不会拉入程序集）。
//
// 用法：dotnet run --project src/Flint.DevTools -- engine-probe [--source <站点目录>]

using System.CommandLine;
using System.Reflection;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Templates;

namespace Flint.DevTools.Commands;

/// <summary>
/// engine-probe 子命令组
/// </summary>
internal static class EngineProbeCommand
{
    public static Command Build()
    {
        var cmd = new Command("engine-probe",
            "Fluid 渲染路径运行期装配探针：渲染 .liquid 模板并验证 Scriban 程序集未加载");

        var sourceOpt = new Option<string?>("--source", "-s")
        {
            Description = "站点目录（缺省在临时目录生成最小 .liquid 站点）"
        };

        cmd.Options.Add(sourceOpt);
        cmd.SetAction(async (ctx, token) =>
        {
            var source = ctx.GetValue(sourceOpt);
            return await ExecuteAsync(source, token);
        });

        return cmd;
    }

    private static async Task<int> ExecuteAsync(string? source, CancellationToken cancellationToken)
    {
        // 1. 准备最小 .liquid 站点（外部站点或临时生成）
        var tempDir = false;
        var sitePath = source;
        if (string.IsNullOrWhiteSpace(sitePath))
        {
            sitePath = Path.Combine(Path.GetTempPath(), $"Flint-engine-probe-{Guid.NewGuid():N}");
            tempDir = true;
            CreateMinimalSite(sitePath);
        }

        try
        {
            // 2. 只经 FluidTemplateRenderer 渲染（不碰 TemplateEngineRouter /
            //    ScribanTemplateRenderer——本探针的证明力全系于此）
            var renderer = new FluidTemplateRenderer(Path.Combine(sitePath, "layouts"), "https://example.com");
            var context = new TemplateContext
            {
                Page = new PageContext
                {
                    Title = "探针页面", Content = "<p>内容</p>",
                    Permalink = "https://example.com/probe/", RelPermalink = "/probe/",
                    Date = DateTimeOffset.UnixEpoch, Tags = ["t"], Categories = [],
                    WordCount = 7, ReadingTime = TimeSpan.FromMinutes(1),
                    Params = new Dictionary<string, object> { ["description"] = "探针" }
                },
                Site = new SiteContext
                {
                    Title = "探针站点", BaseURL = "https://example.com", Language = "zh-cn",
                    Pages = [], RegularPages = [],
                    Taxonomies = new TaxonomyCollection { Taxonomies = new Dictionary<string, IReadOnlyList<TaxonomyTerm>>() },
                    Menus = new MenuCollection { Menus = new Dictionary<string, IReadOnlyList<MenuItem>>() },
                    Translations = new Dictionary<string, string> { ["hello"] = "你好" },
                    Config = new SiteConfig { BaseURL = "https://example.com", Title = "探针站点" }
                }
            };

            var output = await renderer.RenderAsync("index", context, cancellationToken);

            // 3. 装配检查：Scriban 程序集是否在进程已加载集
            var scribanLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name?.Equals("Scriban", StringComparison.OrdinalIgnoreCase) == true);

            // 4. 渲染正确性快检（成员解析在 JIT 下必须工作；AOT 下的裁剪问题
            //    由 DynamicDependency 保留清单兜住——见 FluidTemplateRenderer）
            var expected = new[]
            {
                "探针站点",     // site.title
                "探针页面",     // page.title
                "<p>内容</p>",  // page.content
                "7",            // page.word_count
                "1",            // page.reading_time.total_minutes
                "探针"          // page.params.description
            };
            var missing = expected.Where(e => !output.Contains(e, StringComparison.Ordinal)).ToList();

            Console.WriteLine("== Fluid 渲染路径运行期装配探针 ==");
            Console.WriteLine($"  渲染输出长度: {output.Length} 字符");
            Console.WriteLine($"  成员解析缺失: {(missing.Count == 0 ? "无" : string.Join(", ", missing))}");
            Console.WriteLine($"  Scriban 程序集已加载: {(scribanLoaded ? "是（违反隔离）" : "否")}");

            var pass = !scribanLoaded && missing.Count == 0;
            Console.WriteLine(pass ? "探针通过（PASS）" : "探针失败（FAIL）");
            return pass ? 0 : 1;
        }
        finally
        {
            if (tempDir && sitePath is not null && Directory.Exists(sitePath))
            {
                try
                {
                    Directory.Delete(sitePath, recursive: true);
                }
                catch
                {
                    // 清理失败不掩蔽探针结论
                }
            }
        }
    }

    /// <summary>生成最小 .liquid 站点（index + partial include + 成员访问全覆盖）</summary>
    private static void CreateMinimalSite(string sitePath)
    {
        var layouts = Path.Combine(sitePath, "layouts");
        Directory.CreateDirectory(Path.Combine(layouts, "partials"));
        File.WriteAllText(Path.Combine(layouts, "partials", "head.liquid"),
            "<title>{{ site.title }}</title>");
        File.WriteAllText(Path.Combine(layouts, "index.liquid"),
            "{% include 'partials/head' %}<h1>{{ page.title }}</h1>{{ page.content }}" +
            "<span>{{ page.word_count }}</span><span>{{ page.reading_time.total_minutes }}</span>" +
            "<span>{{ page.params.description }}</span>");
    }
}
