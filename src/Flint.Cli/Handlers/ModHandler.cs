// Flint 静态站点生成器
// Mod 命令处理器

using Flint.Core.Configuration;
using Flint.Core.Modules;

namespace Flint.Cli;

/// <summary>
/// Mod 命令处理器
/// </summary>
internal static class ModHandler
{
    /// <summary>
    /// 初始化模块配置
    /// </summary>
    public static async Task<int> InitAsync(bool verbose)
    {
        var currentDir = Directory.GetCurrentDirectory();

        // 检查是否已经是 Flint 站点
        if (ConfigLoader.HasConfigFile(currentDir))
        {
            Console.WriteLine("当前目录已经是 Flint 站点");
        }

        try
        {
            using var moduleManager = new ModuleManager(currentDir);
            await moduleManager.InitAsync(currentDir);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("模块配置初始化完成");
            Console.ResetColor();

            if (verbose)
            {
                Console.WriteLine($"  配置文件: {Path.Combine(currentDir, "Flint.toml")}");
                Console.WriteLine($"  主题目录: {Path.Combine(currentDir, "themes")}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"初始化失败: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// 获取并安装模块
    /// </summary>
    public static async Task<int> GetAsync(string url, string? version, bool verbose, CancellationToken cancellationToken = default)
    {
        var currentDir = Directory.GetCurrentDirectory();

        // 检查是否是 Flint 站点
        if (!ConfigLoader.HasConfigFile(currentDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 当前目录不是有效的 Flint 站点");
            Console.WriteLine("请先运行 'Flint mod init' 或 'Flint new site <name>'");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"获取模块: {url}");
        if (!string.IsNullOrEmpty(version))
        {
            Console.WriteLine($"  版本: {version}");
        }

        try
        {
            using var moduleManager = new ModuleManager(currentDir);
            var descriptor = await moduleManager.GetAsync(url, version, cancellationToken);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n模块安装成功！");
            Console.ResetColor();
            Console.WriteLine($"  名称: {descriptor.Name}");
            Console.WriteLine($"  版本: {descriptor.Version}");
            Console.WriteLine($"  位置: themes/{descriptor.Name}");

            if (descriptor.Dependencies.Count > 0)
            {
                Console.WriteLine($"  依赖: {string.Join(", ", descriptor.Dependencies.Select(d => d.Name))}");
            }

            Console.WriteLine();
            Console.WriteLine("要使用此主题，请在 Flint.toml 中添加:");
            Console.WriteLine($"  theme = \"{descriptor.Name}\"");

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"获取模块失败: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// 更新模块
    /// </summary>
    public static async Task<int> UpdateAsync(string? moduleName, bool verbose, CancellationToken cancellationToken = default)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var partialFailed = false;

        if (!ConfigLoader.HasConfigFile(currentDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 当前目录不是有效的 Flint 站点");
            Console.ResetColor();
            return 1;
        }

        try
        {
            using var moduleManager = new ModuleManager(currentDir);

            if (string.IsNullOrEmpty(moduleName))
            {
                // 更新所有模块
                Console.WriteLine("更新所有模块...");
                var modules = await moduleManager.ListAsync(cancellationToken);

                if (modules.Count == 0)
                {
                    Console.WriteLine("没有已安装的模块");
                    return 0;
                }

                var updated = 0;
                var failed = 0;
                foreach (var module in modules)
                {
                    try
                    {
                        // 用目录名（repo 名）而非 theme.toml 的 name 更新——
                        // 模块目录查找与 lockfile 键都以目录名为准
                        var dirName = module.LocalPath is not null
                            ? Path.GetFileName(module.LocalPath)
                            : module.Name;
                        Console.WriteLine($"  更新 {dirName}...");
                        await moduleManager.UpdateAsync(dirName, null, cancellationToken);
                        updated++;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"    警告: {ex.Message}");
                        Console.ResetColor();
                        failed++;
                    }
                }

                Console.ForegroundColor = failed > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
                Console.WriteLine($"\n更新完成: {updated}/{modules.Count} 个模块");
                Console.ResetColor();
                partialFailed = failed > 0;
            }
            else
            {
                // 更新指定模块
                Console.WriteLine($"更新模块: {moduleName}");
                await moduleManager.UpdateAsync(moduleName, null, cancellationToken);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("更新成功！");
                Console.ResetColor();
            }

            // 批量更新部分失败时返回非零：CI 中不能让"3/5 更新成功"被成功退出码掩盖
            return partialFailed ? 1 : 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"更新失败: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// 列出已安装的模块
    /// </summary>
    public static async Task<int> ListAsync(bool verbose)
    {
        var currentDir = Directory.GetCurrentDirectory();

        if (!ConfigLoader.HasConfigFile(currentDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 当前目录不是有效的 Flint 站点");
            Console.ResetColor();
            return 1;
        }

        try
        {
            using var moduleManager = new ModuleManager(currentDir);
            var modules = await moduleManager.ListAsync();

            if (modules.Count == 0)
            {
                Console.WriteLine("没有已安装的模块");
                Console.WriteLine();
                Console.WriteLine("使用 'Flint mod get <url>' 安装模块");
                return 0;
            }

            Console.WriteLine($"已安装的模块 ({modules.Count}):\n");

            foreach (var module in modules)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  {module.Name}");
                Console.ResetColor();
                Console.WriteLine($" @ {module.Version}");

                if (verbose)
                {
                    if (!string.IsNullOrEmpty(module.Repository))
                    {
                        Console.WriteLine($"    仓库: {module.Repository}");
                    }
                    if (module.Dependencies.Count > 0)
                    {
                        Console.WriteLine($"    依赖: {string.Join(", ", module.Dependencies.Select(d => d.Name))}");
                    }
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"列出模块失败: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// 移除模块
    /// </summary>
    public static async Task<int> RemoveAsync(string moduleName, bool verbose)
    {
        var currentDir = Directory.GetCurrentDirectory();

        if (!ConfigLoader.HasConfigFile(currentDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("错误: 当前目录不是有效的 Flint 站点");
            Console.ResetColor();
            return 1;
        }

        Console.WriteLine($"移除模块: {moduleName}");

        try
        {
            using var moduleManager = new ModuleManager(currentDir);
            await moduleManager.RemoveAsync(moduleName);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("模块已移除");
            Console.ResetColor();

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"移除失败: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
            Console.ResetColor();
            return 1;
        }
    }
}
