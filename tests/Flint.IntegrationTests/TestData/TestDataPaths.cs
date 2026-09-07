// Flint 静态站点生成器
// 测试数据路径辅助类

namespace Flint.IntegrationTests.TestData;

/// <summary>
/// 测试数据路径辅助类
/// 提供访问预定义测试数据的便捷方法
/// </summary>
public static class TestDataPaths
{
    /// <summary>
    /// 测试数据根目录
    /// </summary>
    public static string Root { get; } = Path.Combine(
        AppContext.BaseDirectory,
        "TestData");

    /// <summary>
    /// 站点模板目录
    /// </summary>
    public static string Sites { get; } = Path.Combine(Root, "Sites");

    /// <summary>
    /// 边界条件测试数据目录
    /// </summary>
    public static string EdgeCases { get; } = Path.Combine(Root, "EdgeCases");

    #region 站点模板路径

    /// <summary>
    /// 最小有效站点模板路径
    /// </summary>
    public static string MinimalSite { get; } = Path.Combine(Sites, "Minimal");

    /// <summary>
    /// 完整站点模板路径
    /// </summary>
    public static string CompleteSite { get; } = Path.Combine(Sites, "Complete");

    /// <summary>
    /// 多语言站点模板路径
    /// </summary>
    public static string MultilingualSite { get; } = Path.Combine(Sites, "Multilingual");

    #endregion

    #region 边界条件测试数据路径

    /// <summary>
    /// 空内容文件路径
    /// </summary>
    public static string EmptyContent { get; } = Path.Combine(EdgeCases, "empty-content.md");

    /// <summary>
    /// 空配置文件路径
    /// </summary>
    public static string EmptyConfig { get; } = Path.Combine(EdgeCases, "empty-config.toml");

    /// <summary>
    /// Unicode 特殊字符内容文件路径
    /// </summary>
    public static string UnicodeContent { get; } = Path.Combine(EdgeCases, "unicode-content.md");

    /// <summary>
    /// Emoji 内容文件路径
    /// </summary>
    public static string EmojiContent { get; } = Path.Combine(EdgeCases, "emoji-content.md");

    /// <summary>
    /// 特殊字符文件名内容路径
    /// </summary>
    public static string SpecialCharactersFilename { get; } = Path.Combine(EdgeCases, "special-characters-filename-测试.md");

    /// <summary>
    /// 深层嵌套内容文件路径
    /// </summary>
    public static string DeeplyNestedContent { get; } = Path.Combine(EdgeCases, "deeply-nested-content.md");

    #endregion

    #region 辅助方法

    /// <summary>
    /// 获取站点模板中的配置文件路径
    /// </summary>
    /// <param name="siteName">站点名称（Minimal、Complete、Multilingual）</param>
    /// <returns>配置文件路径</returns>
    public static string GetSiteConfig(string siteName)
    {
        return Path.Combine(Sites, siteName, "Flint.toml");
    }

    /// <summary>
    /// 获取站点模板中的内容目录路径
    /// </summary>
    /// <param name="siteName">站点名称</param>
    /// <returns>内容目录路径</returns>
    public static string GetSiteContentDir(string siteName)
    {
        return Path.Combine(Sites, siteName, "content");
    }

    /// <summary>
    /// 获取站点模板中的布局目录路径
    /// </summary>
    /// <param name="siteName">站点名称</param>
    /// <returns>布局目录路径</returns>
    public static string GetSiteLayoutDir(string siteName)
    {
        return Path.Combine(Sites, siteName, "layouts");
    }

    /// <summary>
    /// 获取站点模板中的资源目录路径
    /// </summary>
    /// <param name="siteName">站点名称</param>
    /// <returns>资源目录路径</returns>
    public static string GetSiteAssetDir(string siteName)
    {
        return Path.Combine(Sites, siteName, "assets");
    }

    /// <summary>
    /// 复制站点模板到目标目录
    /// </summary>
    /// <param name="siteName">站点名称</param>
    /// <param name="targetDir">目标目录</param>
    public static void CopySiteTemplate(string siteName, string targetDir)
    {
        var sourceDir = Path.Combine(Sites, siteName);
        CopyDirectory(sourceDir, targetDir);
    }

    /// <summary>
    /// 复制目录及其所有内容
    /// </summary>
    /// <param name="sourceDir">源目录</param>
    /// <param name="targetDir">目标目录</param>
    public static void CopyDirectory(string sourceDir, string targetDir)
    {
        // 创建目标目录
        Directory.CreateDirectory(targetDir);

        // 复制文件
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        // 递归复制子目录
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            var destDir = Path.Combine(targetDir, dirName);
            CopyDirectory(dir, destDir);
        }
    }

    /// <summary>
    /// 检查测试数据是否存在
    /// </summary>
    /// <returns>如果测试数据目录存在则返回 true</returns>
    public static bool Exists()
    {
        return Directory.Exists(Root);
    }

    /// <summary>
    /// 验证所有预期的测试数据文件是否存在
    /// </summary>
    /// <returns>验证结果，包含缺失文件列表</returns>
    public static (bool IsValid, IReadOnlyList<string> MissingFiles) Validate()
    {
        var missingFiles = new List<string>();

        // 检查站点模板
        var expectedSites = new[] { "Minimal", "Complete", "Multilingual" };
        foreach (var site in expectedSites)
        {
            var sitePath = Path.Combine(Sites, site);
            if (!Directory.Exists(sitePath))
            {
                missingFiles.Add($"Sites/{site}/");
            }
            else
            {
                var configPath = Path.Combine(sitePath, "Flint.toml");
                if (!File.Exists(configPath))
                {
                    missingFiles.Add($"Sites/{site}/Flint.toml");
                }
            }
        }

        // 检查边界条件测试数据
        var expectedEdgeCases = new[]
        {
            "empty-content.md",
            "empty-config.toml",
            "unicode-content.md",
            "emoji-content.md",
            "special-characters-filename-测试.md",
            "deeply-nested-content.md"
        };

        foreach (var file in expectedEdgeCases)
        {
            var filePath = Path.Combine(EdgeCases, file);
            if (!File.Exists(filePath))
            {
                missingFiles.Add($"EdgeCases/{file}");
            }
        }

        return (missingFiles.Count == 0, missingFiles);
    }

    #endregion
}
