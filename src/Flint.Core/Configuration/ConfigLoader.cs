// Flint 静态站点生成器
// 配置加载器实现

using Flint.Core.Abstractions;

namespace Flint.Core.Configuration;

/// <summary>
/// 配置加载器实现
/// 支持自动检测配置文件格式
/// </summary>
public sealed class ConfigLoader : IConfigLoader
{
    /// <summary>
    /// 支持的配置文件名列表（按优先级排序）
    /// </summary>
    private static readonly string[] ConfigFileNames =
    [
        "Flint.toml",
        "Flint.yaml",
        "Flint.yml",
        "Flint.json",
        "config.toml",
        "config.yaml",
        "config.yml",
        "config.json",
        "hugo.toml",
        "hugo.yaml",
        "hugo.yml",
        "hugo.json"
    ];

    /// <inheritdoc />
    public async ValueTask<SiteConfig> LoadAsync(
        string configPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"配置文件不存在: {configPath}", configPath);
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var format = ConfigParser.DetectFormat(configPath);
        var config = ConfigParser.Parse(content, format);

        // 配置校验接线：非法配置 fail loud（对齐 Hugo 对无效配置的行为），
        // 校验器此前从未接入加载链——写完即死的 305 行休眠价值在此激活
        var validation = new ConfigValidator().Validate(config);
        if (!validation.IsValid)
        {
            var details = string.Join("\n",
                validation.Errors.Select(e => $"  - {e.PropertyPath}: {e.Message}"));
            throw new InvalidOperationException($"站点配置校验失败:\n{details}");
        }

        // 环境变量覆盖接线（USAGE.md 文档化的 FLINT_* 覆盖语义）
        return EnvironmentOverrides.ApplyOverrides(config);
    }

    /// <inheritdoc />
    public SiteConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"配置文件不存在: {configPath}", configPath);
        }

        var content = File.ReadAllText(configPath);
        var format = ConfigParser.DetectFormat(configPath);

        return ConfigParser.Parse(content, format);
    }

    /// <inheritdoc />
    public async ValueTask<SiteConfig> AutoLoadAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var configPath = FindConfigFile(directory);
        if (configPath is null)
        {
            throw new FileNotFoundException(
                $"在目录 {directory} 中未找到配置文件。支持的文件名: {string.Join(", ", ConfigFileNames)}");
        }

        return await LoadAsync(configPath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(
        SiteConfig config,
        string configPath,
        ConfigFormat format = ConfigFormat.Toml,
        CancellationToken cancellationToken = default)
    {
        var content = ConfigParser.Serialize(config, format);
        var directory = Path.GetDirectoryName(configPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(configPath, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在目录中查找配置文件
    /// </summary>
    /// <param name="directory">目录路径</param>
    /// <returns>配置文件路径，如果未找到则返回 null</returns>
    public static string? FindConfigFile(string directory)
    {
        foreach (var fileName in ConfigFileNames)
        {
            var path = Path.Combine(directory, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 检查目录是否包含配置文件
    /// </summary>
    /// <param name="directory">目录路径</param>
    /// <returns>是否包含配置文件</returns>
    public static bool HasConfigFile(string directory)
    {
        return FindConfigFile(directory) is not null;
    }

    /// <summary>
    /// 获取默认配置
    /// </summary>
    /// <param name="baseUrl">基础 URL</param>
    /// <param name="title">站点标题</param>
    /// <returns>默认站点配置</returns>
    public static SiteConfig GetDefaultConfig(string baseUrl = "http://localhost:1313/", string title = "My Site")
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        return new SiteConfig
        {
            BaseURL = baseUrl,
            Title = title,
            LanguageCode = "en",
            Theme = "",
            BuildDrafts = false,
            BuildFuture = false,
            Paginate = 10,
            PaginatePath = "page",
            EnableGitInfo = false,
            SummaryLength = 70,
            ContentDir = "content",
            LayoutDir = "layouts",
            StaticDir = "static",
            AssetDir = "assets",
            DataDir = "data",
            PublishDir = "public",
            ArchetypeDir = "archetypes"
        };
    }
}
