// Flint 静态站点生成器
// Build 命令处理器

using System.Diagnostics;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;

namespace Flint.Cli;

/// <summary>
/// Build 命令处理器
/// </summary>
internal static class BuildHandler
{
    /// <summary>
    /// 执行构建命令
    /// </summary>
    public static async Task<int> ExecuteAsync(
        bool minify,
        bool drafts,
        bool future,
        string output,
        string source,
        bool clean,
        bool verbose,
        string? missingLayout = null,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(source);
        var outputPath = Path.GetFullPath(output);

        // 清理守卫：--clean 会递归删除输出目录（无回收站），
        // 输出目录等于源目录或包含源目录时必须拒绝，否则源内容不可逆丢失。
        // GetFullPath 保留尾分隔符（如 "C:\site\"），直接拼接分隔符会产生
        // 双分隔符使前缀匹配恒失效（守卫被绕过、源目录被删）——先归一化再比较
        var sourceNorm = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var outputNorm = outputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (clean && (string.Equals(outputNorm, sourceNorm, StringComparison.OrdinalIgnoreCase)
            || sourceNorm.StartsWith(outputNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"错误: 输出目录 ({outputPath}) 等于或包含源目录 ({sourcePath})，--clean 将删除源内容，已拒绝");
            Console.ResetColor();
            return 1;
        }

        // 检查是否是有效的 Flint 站点：单文件配置或 config/_default/ 目录式配置
        //（Hugo 0.116+ 推荐形式；blowfish/congo/clarity 等主题只用目录式）
        var configPath = ConfigLoader.FindConfigFile(sourcePath);
        var hasConfigDir = Directory.Exists(Path.Combine(sourcePath, "config", "_default"));
        if (configPath == null && !hasConfigDir)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 当前目录不是有效的 Flint 站点");
            Console.WriteLine("请在站点根目录下运行此命令，或使用 'Flint new site <name>' 创建新站点");
            Console.ResetColor();
            return 1;
        }

        if (verbose)
        {
            Console.WriteLine($"源目录: {sourcePath}");
            Console.WriteLine($"输出目录: {outputPath}");
            Console.WriteLine($"配置文件: {configPath}");
            Console.WriteLine($"压缩: {minify}");
            Console.WriteLine($"包含草稿: {drafts}");
            Console.WriteLine($"包含未来内容: {future}");
            Console.WriteLine();
        }

        try
        {
            // 创建核心组件
            var configLoader = new ConfigLoader();
            // AutoLoadAsync：目录式配置优先（按段合并），否则读单文件；
            // 末段统一做主题默认参数合并
            var siteConfig = await configLoader.AutoLoadAsync(sourcePath, cancellationToken);
            // 主题布局叠加（对齐 Hugo 主题语义）：站点 layouts 优先，theme 配置的
            // 主题目录按序回退——mod 下载的主题其 layouts 由此生效
            var themeLayoutDirs = siteConfig.Theme
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Path.Combine(sourcePath, "themes", t, "layouts"))
                .ToArray();

            // 模板资源提供者（Hugo Pipes）：站点 assets/ 优先，主题 assets/ 按序回退——
            // resources.Get/Match/ByType 由此读取；未提供时 resources.* 注册为空对象
            var assetRoots = new List<string> { Path.Combine(sourcePath, "assets") };
            assetRoots.AddRange(siteConfig.Theme
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Path.Combine(sourcePath, "themes", t, "assets")));
            var resourceProvider = new FileSystemResourceProvider(siteConfig.BaseURL, [.. assetRoots]);
            var environment = new TemplateEnvironmentInfo
            {
                Environment = "production",
                IsMultilingual = siteConfig.Languages.Count > 0,
                WorkingDir = sourcePath
            };
            // 双引擎（FLUID-MIGRATION-PLAN）：Fluid 渲染 .liquid 与 Fluid 原生
            // 站点内置回退，Scriban 渲染 Hugo 主题 .html 与兼容内置；router 按
            // 配置 engine 策略（默认 auto 双轨）分派，SiteBuilder 只依赖接口。
            // **Scriban 经工厂懒构造**（P2-4）：纯 Fluid 站点（新站点默认形态）
            // 的构建全程不构造 Scriban 渲染器，Scriban 程序集不被加载
            var templateRenderer = new TemplateEngineRouter(
                new FluidTemplateRenderer(
                    Path.Combine(sourcePath, "layouts"),
                    siteConfig.BaseURL,
                    builtins: null,
                    resources: resourceProvider,
                    themeLayoutDirs),
                () => new ScribanTemplateRenderer(
                    Path.Combine(sourcePath, "layouts"),
                    siteConfig.BaseURL,
                    environment,
                    resourceProvider,
                    themeLayoutDirs),
                TemplateEngineRouter.ParsePolicy(siteConfig));

            // render hooks（T5.1）：layouts/_markup/render-*.html 存在时定制链接/图片/标题渲染。
            // router 实现 IRenderHookRenderer（P5-2）：.liquid 钩子分发给 Fluid；
            // Scriban 兼容模式下只认 .html
            var renderHooks = templateRenderer.LoadRenderHooks(
                Path.Combine(sourcePath, "layouts"), themeLayoutDirs);
            var markdownParser = renderHooks is not null ? new MarkdownParser(renderHooks) : new MarkdownParser();
            // P5-3：disableShortcodes 时内置 C# 短代码（figure/highlight/ref 等）
            // 也不注册——经 ShortcodeRegistry 的 registerBuiltins 开关，
            // 站点/主题模板短代码由 SiteBuilder 的 RegisterSiteShortcodes 同配置跳过
            var contentParser = new ContentParser(new FrontMatterParser(), markdownParser,
                new ShortcodeProcessor(
                    new ShortcodeRegistry(registerBuiltins: !siteConfig.DisableShortcodes))
                { EnableInlineShortcodes = siteConfig.EnableInlineShortcodes });
            ContentParserDateSetup.Apply(contentParser, siteConfig,
                Path.Combine(sourcePath, siteConfig.ContentDir));
            // CA2000 抑制：管线与三处理器生命周期与 CLI 进程一致（构建结束进程
            // 退出即释放），显式 Dispose 无实际收益
#pragma warning disable CA2000
            var assetPipeline = new AssetPipeline(new AssetPipelineOptions
            {
                MinifyCss = minify,
                SourceDirectory = sourcePath,
                OutputDirectory = outputPath
            },
            imageProcessor: new ImageProcessor(),
            sassCompiler: new SassCompiler(),
            jsBundler: new JavaScriptBundler());
#pragma warning restore CA2000

            // 创建站点构建器（T4.3：一次性构建关闭渲染期依赖登记，省每页 stat；
            // serve/watch 路径经 DevServer 用默认值保持精确增量反查）
            var siteBuilder = new SiteBuilder(
                contentParser,
                templateRenderer,
                assetPipeline,
                configLoader,
                new SiteBuilderOptions { EnableDependencyTracking = false });

            // 清理输出目录
            if (clean && Directory.Exists(outputPath))
            {
                if (verbose)
                    Console.WriteLine("清理输出目录...");
                await siteBuilder.CleanAsync(outputPath, cancellationToken);
            }

            // 执行构建
            Console.WriteLine("开始构建站点...");
            var stopwatch = Stopwatch.StartNew();

            var buildOptions = new BuildOptions
            {
                SourcePath = sourcePath,
                OutputPath = outputPath,
                Minify = minify,
                IncludeDrafts = drafts,
                IncludeFuture = future,
                Parallelism = Environment.ProcessorCount,
                MissingLayout = string.Equals(missingLayout, "skip", StringComparison.OrdinalIgnoreCase)
                    ? Flint.Core.Models.MissingLayoutBehavior.Skip
                    : Flint.Core.Models.MissingLayoutBehavior.Error,
                // 传入已加载的配置，避免 SiteBuilder 重复 AutoLoadAsync
                // （2026-10-21 性能优化：消除配置双加载，149ms → ~75ms）
                PreloadedConfig = siteConfig
            };

            var result = await siteBuilder.BuildAsync(buildOptions, cancellationToken);
            stopwatch.Stop();

            // 输出结果
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine();
                Console.WriteLine($"构建成功！");
                Console.ResetColor();
                Console.WriteLine($"  页面: {result.PagesBuilt}");
                Console.WriteLine($"  资源: {result.AssetsProcessed}");
                Console.WriteLine($"  耗时: {result.Duration.TotalMilliseconds:F0}ms");
                Console.WriteLine($"  内存: {result.MemoryUsed / 1024 / 1024:F1}MB");
                Console.WriteLine($"  输出: {outputPath}");

                if (result.Warnings.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n警告 ({result.Warnings.Count}):");
                    foreach (var warning in result.Warnings.Take(10))
                    {
                        Console.WriteLine($"  - {warning.Message}");
                    }
                    if (result.Warnings.Count > 10)
                    {
                        Console.WriteLine($"  ... 还有 {result.Warnings.Count - 10} 个警告");
                    }
                    Console.ResetColor();
                }

                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine($"构建失败！");
                Console.WriteLine($"\n错误 ({result.Errors.Count}):");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"  [{error.ErrorCode}] {error.FilePath}:{error.Line}:{error.Column}");
                    Console.WriteLine($"    {error.Message}");
                }
                Console.ResetColor();
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"构建异常: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }
}
