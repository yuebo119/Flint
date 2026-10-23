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
        return Finalize(await ParseFileAsync(configPath, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public SiteConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"配置文件不存在: {configPath}", configPath);
        }

        var content = File.ReadAllText(configPath);
        return Finalize(ConfigParser.Parse(content, ConfigParser.DetectFormat(configPath)));
    }

    /// <summary>
    /// 加载并解析单个配置文件（不含覆盖与校验，供 AutoLoadAsync 在主题参数合并后只做一次终态化）
    /// </summary>
    private static async ValueTask<SiteConfig> ParseFileAsync(string configPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"配置文件不存在: {configPath}", configPath);
        }

        var content = await File.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
        var format = ConfigParser.DetectFormat(configPath);
        return ConfigParser.Parse(content, format);
    }

    /// <summary>
    /// 终态化：先应用环境变量覆盖，再校验。顺序不可交换——此前先校验后覆盖，
    /// 校验对象不是最终配置，`Flint_CONTENTDIR=../../x` 之类覆盖可绕过全部校验。
    /// 目录式配置（config/_default/）同样走这里，不再跳过校验与覆盖。
    /// </summary>
    private static SiteConfig Finalize(SiteConfig config)
    {
        var effective = EnvironmentOverrides.ApplyOverrides(config);

        var validation = new ConfigValidator().Validate(effective);
        if (!validation.IsValid)
        {
            var details = string.Join("\n",
                validation.Errors.Select(e => $"  - {e.PropertyPath}: {e.Message}"));
            throw new InvalidOperationException($"站点配置校验失败:\n{details}");
        }

        return effective;
    }

    /// <inheritdoc />
    public async ValueTask<SiteConfig> AutoLoadAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        // 目录式配置优先（Hugo 0.116+）：config/_default/ 存在时按段合并
        var dirConfig = TryLoadConfigDirectory(directory);
        if (dirConfig is not null)
        {
            // 合并后终态化：目录式配置此前直接返回，校验与环境变量覆盖双双失效
            return Finalize(ThemeParamsMerger.Merge(dirConfig, directory));
        }

        var configPath = FindConfigFile(directory);
        if (configPath is null)
        {
            throw new FileNotFoundException(
                $"在目录 {directory} 中未找到配置文件。支持的文件名: {string.Join(", ", ConfigFileNames)}");
        }

        var config = await ParseFileAsync(configPath, cancellationToken).ConfigureAwait(false);

        // 主题默认参数合并（主题系统 P1-2）：主题 theme.toml 的 [params] 作为默认值，
        // 站点配置深覆盖——AutoLoadAsync 是生产装配唯一入口，stub 测试语义不受影响。
        // 合并是最后一步重建，故覆盖与校验放在合并之后，避免被重建丢弃
        return Finalize(ThemeParamsMerger.Merge(config, directory));
    }

    /// <summary>
    /// 加载 **<c>config/_default/</c> 目录式配置**（Hugo 0.116+ 推荐形式）。
    /// 合并规则对齐 Hugo：
    ///   · <c>hugo.toml</c>/<c>config.toml</c> 提供顶层键
    ///   · 其他文件名（params/menus/languages/outputs/module…）的内容挂在**同名顶层键**下
    ///   · <c>config/&lt;environment&gt;/</c> 的同名文件后应用（覆盖 _default）
    /// 目录不存在时返回 null（交回单文件路径）
    /// 实测来源：blowfish/congo/clarity 等主题只用目录式配置，此前报
    /// "当前目录不是有效的 Flint 站点" 而完全无法构建
    /// </summary>
    /// <summary>
    /// config/_default/ 里"文件名即配置段名"的已知段：内容挂到同名顶层键下。
    /// 其余文件名按根级合并（Hugo 语义）
    /// </summary>
    private static readonly HashSet<string> KnownSectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "params", "menus", "languages", "outputs", "outputFormats", "module", "markup",
        "security", "taxonomies", "imaging", "frontmatter", "server", "build", "caches",
        "permalinks", "mediaTypes", "minify", "privacy", "services", "related",
        "sitemap", "pagination", "author", "deployment", "navigation", "httpcache"
    };

    public static SiteConfig? TryLoadConfigDirectory(string directory)
    {
        var baseDir = Path.Combine(directory, "config", "_default");
        if (!Directory.Exists(baseDir))
        {
            return null;
        }

        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        void MergeFile(string path, bool asTopLevel)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".toml" or ".yaml" or ".yml" or ".json"))
            {
                return;
            }

            Dictionary<string, object> table;
            try
            {
                var text = File.ReadAllText(path);
                table = ext == ".toml"
                    ? ConfigParser.ParseTomlDict(text)
                    : ConfigParser.ParseYamlDict(text);
            }
            catch (IOException)
            {
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // 语法错误不能静默跳过（此前 YAML 段解析失败被吞成空字典，
                // 站点在缺段的情况下照样"构建成功"）。报出文件路径与实际错误
                throw new InvalidDataException(
                    $"配置段解析失败: {path}: {ex.Message}", ex);
            }

            var key = Path.GetFileNameWithoutExtension(path);
            // 已知段名（params.toml → params 段等）与 hugo/config 之外的**任意文件名**
            // 一律按根级合并——对齐 Hugo 语义：目录式配置里文件名只是组织手段，
            // 内容统一并入根映射。clarity 的 configTaxo.toml 装的正是根级键
            //（enableInlineShortcodes / timeout / privacy），此前被塞进 "configTaxo"
            // 子表 → enableInlineShortcodes 读不到 → 内容里的内联短代码报"未注册"
            if (asTopLevel || !KnownSectionNames.Contains(key))
            {
                ConfigMerge.DeepMergeInto(merged, table);
            }
            else
            {
                // 文件名即顶层键（params.toml → params 段）
                if (merged.TryGetValue(key, out var existing) &&
                    existing is Dictionary<string, object> existingDict)
                {
                    ConfigMerge.DeepMergeInto(existingDict, table);
                }
                else
                {
                    merged[key] = table;
                }
            }
        }

        // _default：先顶层文件，再按段文件（顺序稳定，便于复现）
        foreach (var f in Directory.EnumerateFiles(baseDir, "*.*").OrderBy(
                     x => Path.GetFileNameWithoutExtension(x) is "hugo" or "config" ? 0 : 1)
                     .ThenBy(x => x, StringComparer.Ordinal))
        {
            MergeFile(f, asTopLevel: false);
        }

        // 环境覆盖：production（Flint 的构建环境语义）
        var envDir = Path.Combine(directory, "config", "production");
        if (Directory.Exists(envDir))
        {
            foreach (var f in Directory.EnumerateFiles(envDir, "*.*").OrderBy(x => x, StringComparer.Ordinal))
            {
                MergeFile(f, asTopLevel: false);
            }
        }

        return merged.Count == 0 ? null : ConfigParser.ParseMergedTomlDict(merged);
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
    /// 读取站点配置的 theme 名（主题 archetype/资源回退判定用）。
    /// 已知边界：TOML 兼容层只解析 TOML 形态配置，YAML/JSON 配置的站点返回 null
    /// </summary>
    public static string? TryGetThemeName(string siteDir)
    {
        try
        {
            var configPath = FindConfigFile(siteDir);
            if (configPath is null || !configPath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            var table = TomlynCompat.TryParseTable(File.ReadAllText(configPath));
            if (table is null || !table.TryGetValue("theme", out var value))
            {
                return null;
            }
            var theme = value?.ToString();
            return string.IsNullOrWhiteSpace(theme) ? null : theme;
        }
        catch (Exception ex) when (ex is IOException or TomlException)
        {
            return null;
        }
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
