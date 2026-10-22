// Flint 静态站点生成器
// Serve 命令处理器 - 使用完整的 DevServer 实现

using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Server;
using Flint.Core.Site;
using Flint.Core.Templates;

namespace Flint.Cli;

/// <summary>
/// Serve 命令处理器
/// </summary>
internal static class ServeHandler
{
    /// <summary>
    /// 执行 serve 命令
    /// </summary>
    public static async Task<int> ExecuteAsync(
        int port,
        bool openBrowser,
        bool liveReload,
        bool drafts,
        string source,
        bool verbose,
        bool cache,
        string host = "localhost")
    {
        var sourcePath = Path.GetFullPath(source);
        var outputPath = Path.Combine(sourcePath, "public");

        // 检查是否是有效的 Flint 站点：单文件配置或 config/_default/ 目录式配置
        //（2026-10-21 一致性修复：此前只认单文件，目录式配置站点能构建但 serve
        // 报"不是有效的 Flint 站点"——blowfish/congo/clarity 等主题只用目录式）
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
            // AutoLoadAsync：目录式配置优先（与 BuildHandler 一致），
            // 避免 DevServer 首次构建重复加载
            var config = await configLoader.AutoLoadAsync(sourcePath);
            // 主题布局叠加（同 BuildHandler）：站点优先、主题回退，dev server 一致生效
            var themeLayoutDirs = config.Theme
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(t => Path.Combine(sourcePath, "themes", t, "layouts"))
                .ToArray();
            // 双引擎（FLUID-MIGRATION-PLAN，与 BuildHandler 同构）：Fluid 渲染
            // .liquid 与 Fluid 原生站点内置回退，Scriban 渲染 Hugo 主题 .html；
            // router 按配置 engine 策略（默认 auto 双轨）分派。
            // **Scriban 经工厂懒构造**（P2-4）：纯 Fluid 站点的 serve 不预构 Scriban
            var templateRenderer = new TemplateEngineRouter(
                new FluidTemplateRenderer(
                    Path.Combine(sourcePath, "layouts"),
                    config.BaseURL,
                    builtins: null,
                    resources: new FileSystemResourceProvider(config.BaseURL,
                    [
                        Path.Combine(sourcePath, "assets"),
                        .. config.Theme
                            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => Path.Combine(sourcePath, "themes", t, "assets"))
                    ]),
                    themeLayoutDirs),
                () => new ScribanTemplateRenderer(
                    Path.Combine(sourcePath, "layouts"),
                    config.BaseURL,
                    new TemplateEnvironmentInfo
                    {
                        Environment = "production",
                        IsMultilingual = config.Languages.Count > 0,
                        WorkingDir = sourcePath
                    },
                    // 模板资源提供者（Hugo Pipes）：与 BuildHandler 同构——否则
                    // `| minify` 等资源函数未注册，serve 的动态渲染全页失败
                    //（yinyang 的 head.html 实测）
                    new FileSystemResourceProvider(config.BaseURL,
                    [
                        Path.Combine(sourcePath, "assets"),
                        .. config.Theme
                            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => Path.Combine(sourcePath, "themes", t, "assets"))
                    ]),
                    themeLayoutDirs),
                TemplateEngineRouter.ParsePolicy(config));

            // render hooks（T5.1）：layouts/_markup/render-*.html 存在时定制链接/图片/标题渲染。
            // router 实现 IRenderHookRenderer（P5-2）：.liquid 钩子分发给 Fluid；
            // Scriban 兼容模式下只认 .html
            var renderHooks = templateRenderer.LoadRenderHooks(
                Path.Combine(sourcePath, "layouts"), themeLayoutDirs);
            var markdownParser = renderHooks is not null ? new MarkdownParser(renderHooks) : new MarkdownParser();
            // P5-3：disableShortcodes 时内置 C# 短代码也不注册（同 BuildHandler）
            var contentParser = new ContentParser(new FrontMatterParser(), markdownParser,
                new ShortcodeProcessor(
                    new ShortcodeRegistry(registerBuiltins: !config.DisableShortcodes))
                { EnableInlineShortcodes = config.EnableInlineShortcodes });
            ContentParserDateSetup.Apply(contentParser, config,
                Path.Combine(sourcePath, config.ContentDir));
            // CA2000 抑制：管线所有权转移至长驻 SiteBuilder，生命周期与 serve
            // 进程一致——此处不能 using（方法结束即释放会撞上后续热重载）
#pragma warning disable CA2000
            var assetPipeline = new AssetPipeline(new AssetPipelineOptions
            {
                SourceDirectory = sourcePath,
                OutputDirectory = outputPath
            },
            imageProcessor: new ImageProcessor(),
            sassCompiler: new SassCompiler(),
            jsBundler: new JavaScriptBundler());
#pragma warning restore CA2000

            // 创建站点构建器
            var siteBuilder = new SiteBuilder(
                contentParser,
                templateRenderer,
                assetPipeline,
                configLoader);

            // 创建开发服务器
            using var devServer = new DevServer(siteBuilder);

            var options = new DevServerOptions
            {
                SourcePath = sourcePath,
                OutputPath = outputPath,
                Port = port,
                BindAddress = host,
                LiveReload = liveReload,
                OpenBrowser = openBrowser,
                IncludeDrafts = drafts,
                IncludeFuture = true,
                Verbose = verbose,
                PreloadedConfig = config,
                // serve --cache：持久内容缓存目录（源侧，与 build --cache 同址）
                CachePath = cache ? Path.Combine(sourcePath, ".flint-cache") : null
            };

            // 设置取消处理
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"启动开发服务器...");
            Console.WriteLine($"  站点目录: {sourcePath}");
            Console.WriteLine($"  输出目录: {outputPath}");
            Console.WriteLine();

            // 启动服务器
            await devServer.StartAsync(options, cts.Token);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"开发服务器已启动: {devServer.ServerUrl}");
            Console.ResetColor();
            Console.WriteLine("按 Ctrl+C 停止服务器");
            Console.WriteLine();

            // 等待取消信号
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }

            Console.WriteLine("\n正在停止服务器...");
            await devServer.StopAsync();
            Console.WriteLine("服务器已停止");

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"服务器错误: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }
}
