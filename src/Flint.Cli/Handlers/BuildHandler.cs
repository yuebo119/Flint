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
        string? output,
        string source,
        bool clean,
        bool cache,
        bool verbose,
        string? missingLayout = null,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(source);
        // output 未显式指定时取站点配置的 publishDir（此前该配置解析、校验齐全
        // 却全链路无人消费，`-o` 之外没有生效路径）
        var outputExplicit = !string.IsNullOrWhiteSpace(output);
        var outputPath = outputExplicit ? Path.GetFullPath(output!) : string.Empty;

        // 清理守卫：--clean 会递归删除输出目录（无回收站），两种情形必须拒绝：
        //   1. 输出等于源、或是源的祖先（删输出即删源）
        //   2. 输出落在源的**内容目录**内（如 -o content）：删输出即删源内容。
        // 注意不能一概拒绝"输出在源目录内"——Hugo 默认布局的 public/ 就在站点根下。
        // GetFullPath 保留尾分隔符（如 "C:\site\"），直接拼分隔符会产生双分隔符
        // 使前缀匹配恒失效——先归一化再比较
        static bool IsDangerousClean(string outputPath, string sourcePath, SiteConfig config)
        {
            var sourceNorm = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var outputNorm = outputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(outputNorm, sourceNorm, StringComparison.OrdinalIgnoreCase)
                || sourceNorm.StartsWith(outputNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!outputNorm.StartsWith(sourceNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var relative = outputNorm[(sourceNorm.Length + 1)..];
            var firstSegment = relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrEmpty(firstSegment))
            {
                return false;
            }

            return SourceDirectoryNames(config).Contains(firstSegment, StringComparer.OrdinalIgnoreCase);
        }

        static string[] SourceDirectoryNames(SiteConfig config) =>
        [
            Name(config.ContentDir, "content"),
            Name(config.LayoutDir, "layouts"),
            Name(config.StaticDir, "static"),
            Name(config.AssetDir, "assets"),
            Name(config.DataDir, "data"),
            Name(config.ArchetypeDir, "archetypes"),
            "themes",
            "config",
            "i18n"
        ];

        static string Name(string configured, string fallback) =>
            string.IsNullOrEmpty(configured) ? fallback : configured;

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

        try
        {
            // 创建核心组件
            var configLoader = new ConfigLoader();
            // AutoLoadAsync：目录式配置优先（按段合并），否则读单文件；
            // 末段统一做主题默认参数合并
            var siteConfig = await configLoader.AutoLoadAsync(sourcePath, cancellationToken);

            if (!outputExplicit)
            {
                outputPath = Path.GetFullPath(string.IsNullOrEmpty(siteConfig.PublishDir)
                    ? "public"
                    : siteConfig.PublishDir);
            }

            if (clean && IsDangerousClean(outputPath, sourcePath, siteConfig))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"错误: 输出目录 ({outputPath}) 与源目录 ({sourcePath}) 的源内容目录重叠，--clean 将删除源内容，已拒绝");
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
            // 目录名由配置驱动（layoutDir/assetDir），空值取 Hugo 默认名；
            // 此前写死字面量，配置改了也无效（校验照常通过）
            var layoutDirName = string.IsNullOrEmpty(siteConfig.LayoutDir) ? "layouts" : siteConfig.LayoutDir;
            var assetDirName = string.IsNullOrEmpty(siteConfig.AssetDir) ? "assets" : siteConfig.AssetDir;

            // 主题布局叠加（对齐 Hugo 主题语义）：站点 layouts 优先，theme 配置的
            // 主题目录按序回退——mod 下载的主题其 layouts 由此生效
            var themeLayoutDirs = siteConfig.Theme
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Path.Combine(sourcePath, "themes", t, layoutDirName))
                .ToArray();

            // 模板资源提供者（Hugo Pipes）：站点 assets/ 优先，主题 assets/ 按序回退——
            // resources.Get/Match/ByType 由此读取；未提供时 resources.* 注册为空对象
            var assetRoots = new List<string> { Path.Combine(sourcePath, assetDirName) };
            assetRoots.AddRange(siteConfig.Theme
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Path.Combine(sourcePath, "themes", t, assetDirName)));
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
                    Path.Combine(sourcePath, layoutDirName),
                    siteConfig.BaseURL,
                    builtins: null,
                    resources: resourceProvider,
                    themeLayoutDirs),
                () => new ScribanTemplateRenderer(
                    Path.Combine(sourcePath, layoutDirName),
                    siteConfig.BaseURL,
                    environment,
                    resourceProvider,
                    themeLayoutDirs),
                TemplateEngineRouter.ParsePolicy(siteConfig));

            // render hooks（T5.1）：layouts/_markup/render-*.html 存在时定制链接/图片/标题渲染。
            // router 实现 IRenderHookRenderer（P5-2）：.liquid 钩子分发给 Fluid；
            // Scriban 兼容模式下只认 .html
            var renderHooks = templateRenderer.LoadRenderHooks(
                Path.Combine(sourcePath, layoutDirName), themeLayoutDirs);
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

            // 清理输出目录：由 BuildAsync 经 CleanOutput 选项执行（保留上方的
            // 危险路径守卫）。此前此处自行调用 CleanAsync，而 BuildOptions 的
            // CleanOutput 历史默认 true 又让 BuildAsync 再删一次（双删），
            // 且默认全删与 USAGE.md 的「--clean 按需」契约矛盾——
            // 2026-10-22 统一为单一清理点：CleanOutput 跟随 --clean 标志
            if (clean && verbose)
            {
                Console.WriteLine("清理输出目录...");
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
                // 清理语义唯一来源：--clean 传 true，默认 false（不清理，
                // 对齐 Hugo 与 USAGE.md:374 文档契约）
                CleanOutput = clean,
                // 持久内容缓存（--cache 显式开启）：缓存放源目录 .flint-cache/
                // （源侧而非输出侧：--clean 清输出不连带废缓存；Hugo 的
                // resources/_gen 同思路）。默认 null = 不启用，行为与之前一致
                CachePath = cache ? Path.Combine(sourcePath, ".flint-cache") : null,
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
