// Flint 静态站点生成器命令行入口
// 使用 System.CommandLine 2.0 构建 CLI

using System.CommandLine;
using Flint.Core;

namespace Flint.Cli;

/// <summary>
/// Flint CLI 程序入口
/// </summary>
internal static class Program
{
    /// <summary>
    /// 程序入口点
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand(FlintInfo.Description);

        // 全局选项
        var verboseOption = new Option<bool>("--verbose", "-v") { Description = "输出详细日志信息" };
        var debugOption = new Option<bool>("--debug", "-d") { Description = "输出调试信息" };
        rootCommand.Options.Add(verboseOption);
        rootCommand.Options.Add(debugOption);

        // 添加所有命令
        AddVersionCommand(rootCommand);
        AddNewCommands(rootCommand);
        AddBuildCommand(rootCommand, verboseOption);
        AddServeCommand(rootCommand, verboseOption);
        AddDeployCommand(rootCommand, verboseOption);
        AddModCommands(rootCommand, verboseOption);

        var parseResult = rootCommand.Parse(args);
        return await parseResult.InvokeAsync().ConfigureAwait(false);
    }

    private static void AddVersionCommand(RootCommand rootCommand)
    {
        var cmd = new Command("version", "显示版本信息");
        var verboseOpt = new Option<bool>("--verbose", "-v") { Description = "显示详细版本信息" };
        cmd.Options.Add(verboseOpt);
        cmd.SetAction(ctx =>
        {
            var verbose = ctx.GetValue(verboseOpt);
            if (verbose)
            {
                Console.WriteLine(FlintInfo.DetailedVersionInfo);
            }
            else
            {
                Console.WriteLine(FlintInfo.VersionInfo);
            }
        });
        rootCommand.Subcommands.Add(cmd);
    }

    private static void AddNewCommands(RootCommand rootCommand)
    {
        var newCommand = new Command("new", "创建新的站点、主题或内容");

        // new site
        var newSiteCmd = new Command("site", "创建新的站点");
        var siteNameArg = new Argument<string>("name") { Description = "站点名称" };
        newSiteCmd.Arguments.Add(siteNameArg);
        newSiteCmd.SetAction(ctx =>
        {
            var name = ctx.GetValue(siteNameArg);
            return SiteCreator.CreateNewSite(name!);
        });
        newCommand.Subcommands.Add(newSiteCmd);

        // new theme
        var newThemeCmd = new Command("theme", "创建新的主题");
        var themeNameArg = new Argument<string>("name") { Description = "主题名称" };
        newThemeCmd.Arguments.Add(themeNameArg);
        newThemeCmd.SetAction(ctx =>
        {
            var name = ctx.GetValue(themeNameArg);
            return ThemeCreator.CreateNewTheme(name!);
        });
        newCommand.Subcommands.Add(newThemeCmd);

        // new content
        var newContentCmd = new Command("content", "创建新的内容文件");
        var contentPathArg = new Argument<string>("path") { Description = "内容文件路径" };
        var kindOption = new Option<string>("--kind", "-k") { Description = "内容类型（archetype）", DefaultValueFactory = _ => "default" };
        newContentCmd.Arguments.Add(contentPathArg);
        newContentCmd.Options.Add(kindOption);
        newContentCmd.SetAction(ctx =>
        {
            var path = ctx.GetValue(contentPathArg);
            var kind = ctx.GetValue(kindOption);
            return ContentCreator.CreateNewContent(path!, kind!);
        });
        newCommand.Subcommands.Add(newContentCmd);

        rootCommand.Subcommands.Add(newCommand);
    }

    private static void AddBuildCommand(RootCommand rootCommand, Option<bool> verboseOption)
    {
        var cmd = new Command("build", "构建静态站点");
        var minifyOpt = new Option<bool>("--minify", "-m") { Description = "压缩 HTML、CSS 和 JavaScript 输出" };
        var draftsOpt = new Option<bool>("--drafts", "-D") { Description = "包含草稿内容" };
        var futureOpt = new Option<bool>("--future", "-F") { Description = "包含未来日期的内容" };
        var outputOpt = new Option<string>("--output", "-o") { Description = "输出目录", DefaultValueFactory = _ => "public" };
        var sourceOpt = new Option<string>("--source", "-s") { Description = "源目录", DefaultValueFactory = _ => "." };
        var cleanOpt = new Option<bool>("--clean") { Description = "构建前清理输出目录" };

        cmd.Options.Add(minifyOpt);
        cmd.Options.Add(draftsOpt);
        cmd.Options.Add(futureOpt);
        cmd.Options.Add(outputOpt);
        cmd.Options.Add(sourceOpt);
        cmd.Options.Add(cleanOpt);

        cmd.SetAction(async (ctx, token) =>
        {
            var minify = ctx.GetValue(minifyOpt);
            var drafts = ctx.GetValue(draftsOpt);
            var future = ctx.GetValue(futureOpt);
            var output = ctx.GetValue(outputOpt);
            var source = ctx.GetValue(sourceOpt);
            var clean = ctx.GetValue(cleanOpt);
            var verbose = ctx.GetValue(verboseOption);
            return await BuildHandler.ExecuteAsync(minify, drafts, future, output!, source!, clean, verbose, token);
        });

        rootCommand.Subcommands.Add(cmd);
    }


    private static void AddServeCommand(RootCommand rootCommand, Option<bool> verboseOption)
    {
        var cmd = new Command("serve", "启动开发服务器");
        var portOpt = new Option<int>("--port", "-p") { Description = "服务器端口", DefaultValueFactory = _ => 1313 };
        var hostOpt = new Option<string>("--host") { Description = "绑定的主机地址（默认 localhost）", DefaultValueFactory = _ => "localhost" };
        var openOpt = new Option<bool>("--open") { Description = "自动打开浏览器（-o 保留给 build --output，避免跨命令语义漂移）", DefaultValueFactory = _ => true };
        var liveReloadOpt = new Option<bool>("--livereload", "-l") { Description = "启用热重载", DefaultValueFactory = _ => true };
        var draftsOpt = new Option<bool>("--drafts", "-D") { Description = "包含草稿内容", DefaultValueFactory = _ => true };
        var sourceOpt = new Option<string>("--source", "-s") { Description = "源目录", DefaultValueFactory = _ => "." };

        cmd.Options.Add(portOpt);
        cmd.Options.Add(hostOpt);
        cmd.Options.Add(openOpt);
        cmd.Options.Add(liveReloadOpt);
        cmd.Options.Add(draftsOpt);
        cmd.Options.Add(sourceOpt);

        cmd.SetAction(async (ctx, token) =>
        {
            var port = ctx.GetValue(portOpt);
            var host = ctx.GetValue(hostOpt);
            var open = ctx.GetValue(openOpt);
            var liveReload = ctx.GetValue(liveReloadOpt);
            var drafts = ctx.GetValue(draftsOpt);
            var source = ctx.GetValue(sourceOpt);
            var verbose = ctx.GetValue(verboseOption);
            return await ServeHandler.ExecuteAsync(port, open, liveReload, drafts, source!, verbose, host!);
        });

        rootCommand.Subcommands.Add(cmd);
    }

    private static void AddDeployCommand(RootCommand rootCommand, Option<bool> verboseOption)
    {
        var cmd = new Command("deploy", "部署站点到云端");
        var targetArg = new Argument<string>("target") { Description = "部署目标 (s3://bucket, gh-pages, netlify, vercel)" };
        var sourceOpt = new Option<string>("--source", "-s") { Description = "源目录", DefaultValueFactory = _ => "public" };
        var dryRunOpt = new Option<bool>("--dry-run") { Description = "模拟部署，不实际上传" };

        cmd.Arguments.Add(targetArg);
        cmd.Options.Add(sourceOpt);
        cmd.Options.Add(dryRunOpt);

        cmd.SetAction(async (ctx, token) =>
        {
            var target = ctx.GetValue(targetArg);
            var source = ctx.GetValue(sourceOpt);
            var dryRun = ctx.GetValue(dryRunOpt);
            var verbose = ctx.GetValue(verboseOption);
            return await DeployHandler.ExecuteAsync(target!, source!, dryRun, verbose);
        });

        rootCommand.Subcommands.Add(cmd);
    }

    private static void AddModCommands(RootCommand rootCommand, Option<bool> verboseOption)
    {
        var modCmd = new Command("mod", "模块管理");

        // mod init
        var initCmd = new Command("init", "初始化模块配置");
        initCmd.SetAction(async (ctx, token) =>
        {
            var verbose = ctx.GetValue(verboseOption);
            return await ModHandler.InitAsync(verbose);
        });
        modCmd.Subcommands.Add(initCmd);

        // mod get
        var getCmd = new Command("get", "获取并安装模块");
        var urlArg = new Argument<string>("url") { Description = "模块仓库 URL" };
        // 注意：不使用 -v 短选项，避免与全局 --verbose/-v 冲突
        var versionOpt = new Option<string?>("--version") { Description = "指定版本" };
        getCmd.Arguments.Add(urlArg);
        getCmd.Options.Add(versionOpt);
        getCmd.SetAction(async (ctx, token) =>
        {
            var url = ctx.GetValue(urlArg);
            var version = ctx.GetValue(versionOpt);
            var verbose = ctx.GetValue(verboseOption);
            return await ModHandler.GetAsync(url!, version, verbose, token);
        });
        modCmd.Subcommands.Add(getCmd);

        // mod update
        var updateCmd = new Command("update", "更新模块");
        var moduleArg = new Argument<string?>("module") { Description = "模块名称（留空更新所有）", DefaultValueFactory = _ => null };
        updateCmd.Arguments.Add(moduleArg);
        updateCmd.SetAction(async (ctx, token) =>
        {
            var module = ctx.GetValue(moduleArg);
            var verbose = ctx.GetValue(verboseOption);
            return await ModHandler.UpdateAsync(module, verbose, token);
        });
        modCmd.Subcommands.Add(updateCmd);

        // mod list
        var listCmd = new Command("list", "列出已安装的模块");
        listCmd.SetAction(async (ctx, token) =>
        {
            var verbose = ctx.GetValue(verboseOption);
            return await ModHandler.ListAsync(verbose);
        });
        modCmd.Subcommands.Add(listCmd);

        // mod remove
        var removeCmd = new Command("remove", "移除模块");
        var removeArg = new Argument<string>("module") { Description = "模块名称" };
        removeCmd.Arguments.Add(removeArg);
        removeCmd.SetAction(async (ctx, token) =>
        {
            var module = ctx.GetValue(removeArg);
            var verbose = ctx.GetValue(verboseOption);
            return await ModHandler.RemoveAsync(module!, verbose);
        });
        modCmd.Subcommands.Add(removeCmd);

        rootCommand.Subcommands.Add(modCmd);
    }
}
