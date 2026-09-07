// Flint 静态站点生成器
// 配置解析器实现（AOT 兼容）

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flint.Core.Abstractions;
using Tomlyn;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Flint.Core.Configuration;

/// <summary>
/// 配置解析器 - 支持 TOML、YAML、JSON 三种格式
/// 提供解析和序列化功能，确保往返一致性
/// </summary>
#pragma warning disable IL2026, IL3050 // AOT 警告在此类中被抑制，因为配置解析需要动态类型处理
public static partial class ConfigParser
{
    // YAML 反序列化器（延迟初始化）
    private static IDeserializer? _yamlDeserializer;
    private static IDeserializer YamlDeserializer => _yamlDeserializer ??= new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // YAML 序列化器（延迟初始化）
    private static ISerializer? _yamlSerializer;
    private static ISerializer YamlSerializer => _yamlSerializer ??= new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

    /// <summary>
    /// 检测配置文件格式
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>配置格式</returns>
    /// <exception cref="ArgumentException">不支持的文件格式</exception>
    public static ConfigFormat DetectFormat(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".toml" => ConfigFormat.Toml,
            ".yaml" or ".yml" => ConfigFormat.Yaml,
            ".json" => ConfigFormat.Json,
            _ => throw new ArgumentException($"不支持的配置文件格式: {extension}", nameof(filePath))
        };
    }

    /// <summary>
    /// 根据格式解析配置内容
    /// </summary>
    /// <param name="content">配置内容</param>
    /// <param name="format">配置格式</param>
    /// <returns>站点配置</returns>
    public static SiteConfig Parse(string content, ConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(content);
        return format switch
        {
            ConfigFormat.Toml => ParseToml(content),
            ConfigFormat.Yaml => ParseYaml(content),
            ConfigFormat.Json => ParseJson(content),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "不支持的配置格式")
        };
    }

    /// <summary>
    /// 根据格式序列化配置
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <param name="format">配置格式</param>
    /// <returns>序列化后的字符串</returns>
    public static string Serialize(SiteConfig config, ConfigFormat format)
    {
        ArgumentNullException.ThrowIfNull(config);
        return format switch
        {
            ConfigFormat.Toml => SerializeToml(config),
            ConfigFormat.Yaml => SerializeYaml(config),
            ConfigFormat.Json => SerializeJson(config),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "不支持的配置格式")
        };
    }

    #region TOML 解析和序列化

    /// <summary>
    /// 解析 TOML 配置
    /// </summary>
    /// <param name="content">TOML 内容</param>
    /// <returns>站点配置</returns>
    public static SiteConfig ParseToml(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var model = Toml.ToModel(content);
        return ConvertDictToConfig(ConfigNormalizer.NormalizeToml(model));
    }

    /// <summary>
    /// 序列化为 TOML 格式
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <returns>TOML 字符串</returns>
    public static string SerializeToml(SiteConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return DictToToml(ConvertConfigToDict(config));
    }
    #endregion

    #region YAML 解析和序列化

    /// <summary>
    /// 解析 YAML 配置
    /// </summary>
    /// <param name="content">YAML 内容</param>
    /// <returns>站点配置</returns>
    public static SiteConfig ParseYaml(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            var dict = YamlDeserializer.Deserialize<Dictionary<string, object>>(content)
                ?? new Dictionary<string, object>();
            return ConvertDictToConfig(dict);
        }
        catch (Exception ex) when (ex is InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            // NativeAOT 发布形态：YamlDotNet 依赖运行时代码生成（内层反射异常
            // 可能被包装为 YamlException）——给可行动指引而非裸异常
            if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                throw new InvalidDataException(
                    "YAML 配置解析在 NativeAOT 发布形态不受支持（YamlDotNet 依赖运行时代码生成）。" +
                    "请改用 TOML 或 JSON 配置格式，或使用 Trim+R2R 发布形态", ex);
            }
            throw;
        }
    }

    /// <summary>
    /// 序列化为 YAML 格式
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <returns>YAML 字符串</returns>
    public static string SerializeYaml(SiteConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var dict = ConvertConfigToDict(config);
        return YamlSerializer.Serialize(dict);
    }

    #endregion

    #region JSON 解析和序列化

    /// <summary>
    /// 解析 JSON 配置
    /// </summary>
    /// <param name="content">JSON 内容</param>
    /// <returns>站点配置</returns>
    public static SiteConfig ParseJson(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var doc = JsonDocument.Parse(content, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        return ConvertDictToConfig(ConfigNormalizer.NormalizeJson(doc.RootElement));
    }

    /// <summary>
    /// 序列化为 JSON 格式（source-gen，无反射——对齐项目内 FeedJsonContext 等既有模式）
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <returns>JSON 字符串</returns>
    public static string SerializeJson(SiteConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var dict = ConvertConfigToDict(config);
        return JsonSerializer.Serialize(dict, ConfigJsonContext.Default.DictionaryStringObject);
    }

    #endregion

    #region 字典转换辅助方法（用于 YAML 解析）

    private static SiteConfig ConvertDictToConfig(Dictionary<string, object> dict)
    {
        return new SiteConfig
        {
            BaseURL = GetDictString(dict, "baseURL") ?? GetDictString(dict, "baseurl") ?? "http://localhost:1313/",
            Title = GetDictString(dict, "title") ?? "Untitled Site",
            LanguageCode = GetDictString(dict, "languageCode") ?? GetDictString(dict, "languagecode") ?? "en",
            Theme = GetDictString(dict, "theme") ?? "",
            BuildDrafts = GetDictBool(dict, "buildDrafts") ?? GetDictBool(dict, "builddrafts") ?? false,
            BuildFuture = GetDictBool(dict, "buildFuture") ?? GetDictBool(dict, "buildfuture") ?? false,
            BuildExpired = GetDictBool(dict, "buildExpired") ?? GetDictBool(dict, "buildexpired") ?? false,
            Paginate = GetDictInt(dict, "paginate") ?? 10,
            PaginatePath = GetDictString(dict, "paginatePath") ?? GetDictString(dict, "paginatepath") ?? "page",
            EnableGitInfo = GetDictBool(dict, "enableGitInfo") ?? GetDictBool(dict, "enablegitinfo") ?? false,
            TimeZone = GetDictString(dict, "timeZone") ?? GetDictString(dict, "timezone") ?? "",
            SummaryLength = GetDictInt(dict, "summaryLength") ?? GetDictInt(dict, "summarylength") ?? 70,
            Copyright = GetDictString(dict, "copyright"),
            DisablePathToLower = GetDictBool(dict, "disablePathToLower") ?? false,
            DisableKinds = ParseDictStringArray(dict, "disableKinds") ?? [],
            ContentDir = GetDictString(dict, "contentDir") ?? GetDictString(dict, "contentdir") ?? "content",
            LayoutDir = GetDictString(dict, "layoutDir") ?? GetDictString(dict, "layoutdir") ?? "layouts",
            StaticDir = GetDictString(dict, "staticDir") ?? GetDictString(dict, "staticdir") ?? "static",
            AssetDir = GetDictString(dict, "assetDir") ?? GetDictString(dict, "assetdir") ?? "assets",
            DataDir = GetDictString(dict, "dataDir") ?? GetDictString(dict, "datadir") ?? "data",
            PublishDir = GetDictString(dict, "publishDir") ?? GetDictString(dict, "publishdir") ?? "public",
            ArchetypeDir = GetDictString(dict, "archetypeDir") ?? GetDictString(dict, "archetypedir") ?? "archetypes",
            Permalinks = ParseDictPermalinks(dict),
            Taxonomies = ParseDictTaxonomies(dict),
            Menus = ParseDictMenus(dict),
            Params = ParseDictParams(dict),
            Markup = ParseDictMarkup(dict),
            Outputs = ParseDictOutputs(dict),
            Languages = ParseDictLanguages(dict),
            Module = ParseDictModule(dict),
            Security = ParseDictSecurity(dict),
            Caches = ParseDictCaches(dict),
            Author = ParseDictAuthor(dict)
        };
    }

    // 字典辅助方法
    private static string? GetDictString(Dictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) && value is string s ? s : null;
    }

    private static bool? GetDictBool(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static int? GetDictInt(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            int i => i,
            // 超范围静默 unchecked 截断会把 maxSize: 99999999999 变成负数——显式报错
            long l => l is >= int.MinValue and <= int.MaxValue
                ? (int)l
                : throw new InvalidDataException($"配置整数值超出 int 范围: {l}"),
            string s => int.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static Dictionary<string, object>? GetDictObject(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            Dictionary<string, object> d => d,
            Dictionary<object, object> d => d.ToDictionary(
                kvp => kvp.Key?.ToString() ?? "",
                kvp => kvp.Value ?? ""),
            _ => null
        };
    }

    private static List<object>? GetDictArray(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            List<object> l => l,
            IEnumerable<object> e => e.ToList(),
            _ => null
        };
    }

    private static Dictionary<string, object>? ConvertToStringDict(object? value)
    {
        return value switch
        {
            Dictionary<string, object> d => d,
            Dictionary<object, object> d => d.ToDictionary(
                kvp => kvp.Key?.ToString() ?? "",
                kvp => kvp.Value ?? ""),
            _ => null
        };
    }

    // 字典子配置解析方法
    private static PermalinkConfig ParseDictPermalinks(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "permalinks");
        if (obj is null)
            return new PermalinkConfig();

        return new PermalinkConfig
        {
            Posts = GetDictString(obj, "posts") ?? GetDictString(obj, "post") ?? "/:year/:month/:title/",
            Pages = GetDictString(obj, "pages") ?? GetDictString(obj, "page") ?? "/:title/",
            Categories = GetDictString(obj, "categories") ?? "/categories/:slug/",
            Tags = GetDictString(obj, "tags") ?? "/tags/:slug/"
        };
    }

    private static TaxonomyConfig ParseDictTaxonomies(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "taxonomies");
        if (obj is null)
            return new TaxonomyConfig();

        var taxonomies = new Dictionary<string, string>();
        foreach (var kvp in obj)
        {
            if (kvp.Value is string s)
            {
                taxonomies[kvp.Key] = s;
            }
        }

        return new TaxonomyConfig { Taxonomies = taxonomies };
    }

    private static MenuConfig ParseDictMenus(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "menu") ?? GetDictObject(dict, "menus");
        if (obj is null)
            return new MenuConfig();

        var menus = new Dictionary<string, IReadOnlyList<MenuItemConfig>>();
        foreach (var kvp in obj)
        {
            if (kvp.Value is List<object> list)
            {
                var items = new List<MenuItemConfig>();
                foreach (var item in list)
                {
                    var itemDict = ConvertToStringDict(item);
                    if (itemDict is not null)
                    {
                        items.Add(new MenuItemConfig
                        {
                            Name = GetDictString(itemDict, "name") ?? "",
                            URL = GetDictString(itemDict, "url"),
                            Weight = GetDictInt(itemDict, "weight") ?? 0,
                            Identifier = GetDictString(itemDict, "identifier"),
                            Parent = GetDictString(itemDict, "parent"),
                            Pre = GetDictString(itemDict, "pre"),
                            Post = GetDictString(itemDict, "post")
                        });
                    }
                }
                menus[kvp.Key] = items;
            }
        }

        return new MenuConfig { Menus = menus };
    }

    private static Dictionary<string, object> ParseDictParams(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "params");
        if (obj is null)
            return new Dictionary<string, object>();

        return NormalizeDictionary(obj);
    }

    private static Dictionary<string, object> NormalizeDictionary(Dictionary<string, object> dict)
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in dict)
        {
            // null ≈ 字段未设置：跳过该键，由取值层 ?? 默认值链统一回退（方案 A，三格式一致）
            var value = NormalizeValue(kvp.Value);
            if (value is not null)
            {
                result[kvp.Key] = value;
            }
        }
        return result;
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            Dictionary<string, object> d => NormalizeDictionary(d),
            Dictionary<object, object> d => NormalizeDictionary(d.ToDictionary(
                kvp => kvp.Key?.ToString() ?? "",
                kvp => kvp.Value)),
            List<object> l => l.Select(v => NormalizeValue(v)).Where(v => v is not null).ToList(),
            IEnumerable<object> e => e.Select(v => NormalizeValue(v)).Where(v => v is not null).ToList(),
            null => null, // null ≈ 未设置（此前归一化为空串，与 YAML 的 null 语义分叉）
            _ => value
        };
    }

    private static MarkupConfig ParseDictMarkup(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "markup");
        if (obj is null)
            return new MarkupConfig();

        return new MarkupConfig
        {
            TableOfContents = ParseDictTableOfContents(obj),
            Highlight = ParseDictHighlight(obj),
            Goldmark = ParseDictGoldmark(obj)
        };
    }

    private static TableOfContentsConfig ParseDictTableOfContents(Dictionary<string, object> markup)
    {
        var obj = GetDictObject(markup, "tableOfContents");
        if (obj is null)
            return new TableOfContentsConfig();

        return new TableOfContentsConfig
        {
            StartLevel = GetDictInt(obj, "startLevel") ?? 2,
            EndLevel = GetDictInt(obj, "endLevel") ?? 3,
            Ordered = GetDictBool(obj, "ordered") ?? false
        };
    }

    private static HighlightConfig ParseDictHighlight(Dictionary<string, object> markup)
    {
        var obj = GetDictObject(markup, "highlight");
        if (obj is null)
            return new HighlightConfig();

        return new HighlightConfig
        {
            Style = GetDictString(obj, "style") ?? "monokai",
            LineNos = GetDictBool(obj, "lineNos") ?? false,
            LineNumbersInTable = GetDictBool(obj, "lineNumbersInTable") ?? true,
            TabWidth = GetDictInt(obj, "tabWidth") ?? 4
        };
    }

    private static GoldmarkConfig ParseDictGoldmark(Dictionary<string, object> markup)
    {
        var obj = GetDictObject(markup, "goldmark");
        if (obj is null)
            return new GoldmarkConfig();

        var renderer = GetDictObject(obj, "renderer");
        var extensions = GetDictObject(obj, "extensions");

        return new GoldmarkConfig
        {
            Unsafe = renderer is not null && (GetDictBool(renderer, "unsafe") ?? false),
            Extensions = extensions is not null ? new GoldmarkExtensions
            {
                Table = GetDictBool(extensions, "table") ?? true,
                Strikethrough = GetDictBool(extensions, "strikethrough") ?? true,
                TaskList = GetDictBool(extensions, "taskList") ?? true,
                Footnote = GetDictBool(extensions, "footnote") ?? true,
                DefinitionList = GetDictBool(extensions, "definitionList") ?? true,
                Typographer = GetDictBool(extensions, "typographer") ?? true
            } : new GoldmarkExtensions()
        };
    }

    private static OutputConfig ParseDictOutputs(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "outputs");
        if (obj is null)
            return new OutputConfig();

        return new OutputConfig
        {
            Home = ParseDictStringArray(obj, "home") ?? ["HTML", "RSS"],
            Section = ParseDictStringArray(obj, "section") ?? ["HTML", "RSS"],
            Taxonomy = ParseDictStringArray(obj, "taxonomy") ?? ["HTML", "RSS"],
            Term = ParseDictStringArray(obj, "term") ?? ["HTML", "RSS"],
            Page = ParseDictStringArray(obj, "page") ?? ["HTML"]
        };
    }

    private static List<string>? ParseDictStringArray(Dictionary<string, object> dict, string key)
    {
        var arr = GetDictArray(dict, key);
        if (arr is null)
            return null;

        return arr.Select(v => v?.ToString() ?? "").ToList();
    }

    private static Dictionary<string, LanguageConfig> ParseDictLanguages(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "languages");
        if (obj is null)
            return new Dictionary<string, LanguageConfig>();

        var languages = new Dictionary<string, LanguageConfig>();
        foreach (var kvp in obj)
        {
            var langDict = ConvertToStringDict(kvp.Value);
            if (langDict is not null)
            {
                var paramsDict = GetDictObject(langDict, "params");
                languages[kvp.Key] = new LanguageConfig
                {
                    LanguageName = GetDictString(langDict, "languageName") ?? kvp.Key,
                    Weight = GetDictInt(langDict, "weight") ?? 0,
                    Title = GetDictString(langDict, "title"),
                    ContentDir = GetDictString(langDict, "contentDir"),
                    Params = paramsDict is not null
                        ? NormalizeDictionary(paramsDict)
                        : new Dictionary<string, object>()
                };
            }
        }

        return languages;
    }

    private static ModuleConfig ParseDictModule(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "module");
        if (obj is null)
            return new ModuleConfig();

        var imports = new List<ModuleImport>();
        var importsArr = GetDictArray(obj, "imports");
        if (importsArr is not null)
        {
            foreach (var item in importsArr)
            {
                var itemDict = ConvertToStringDict(item);
                if (itemDict is not null)
                {
                    var mounts = new List<ModuleMount>();
                    var mountsArr = GetDictArray(itemDict, "mounts");
                    if (mountsArr is not null)
                    {
                        foreach (var mount in mountsArr)
                        {
                            var mountDict = ConvertToStringDict(mount);
                            if (mountDict is not null)
                            {
                                mounts.Add(new ModuleMount
                                {
                                    Source = GetDictString(mountDict, "source") ?? "",
                                    Target = GetDictString(mountDict, "target") ?? ""
                                });
                            }
                        }
                    }

                    imports.Add(new ModuleImport
                    {
                        Path = GetDictString(itemDict, "path") ?? "",
                        Disabled = GetDictBool(itemDict, "disabled") ?? false,
                        Mounts = mounts
                    });
                }
            }
        }

        return new ModuleConfig { Imports = imports };
    }

    private static SecurityConfig ParseDictSecurity(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "security");
        if (obj is null)
            return new SecurityConfig();

        return new SecurityConfig
        {
            AllowedDomains = ParseDictStringArray(obj, "allowedDomains") ?? [],
            AllowedCommands = ParseDictStringArray(obj, "allowedCommands") ?? [],
            HttpTimeout = GetDictInt(obj, "httpTimeout") ?? 30
        };
    }

    private static CacheConfig ParseDictCaches(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "caches");
        if (obj is null)
            return new CacheConfig();

        return new CacheConfig
        {
            Enabled = GetDictBool(obj, "enabled") ?? true,
            Dir = GetDictString(obj, "dir"),
            MaxSize = GetDictInt(obj, "maxSize") ?? 100
        };
    }

    private static AuthorConfig? ParseDictAuthor(Dictionary<string, object> dict)
    {
        var obj = GetDictObject(dict, "author");
        if (obj is null)
            return null;

        var name = GetDictString(obj, "name");
        if (string.IsNullOrEmpty(name))
            return null;

        return new AuthorConfig
        {
            Name = name,
            Email = GetDictString(obj, "email"),
            URL = GetDictString(obj, "url")
        };
    }

    #endregion

    #region 配置转字典（用于序列化）

    /// <summary>
    /// 将配置转换为字典（用于 YAML/JSON 序列化）
    /// </summary>
    private static Dictionary<string, object> ConvertConfigToDict(SiteConfig config)
    {
        // 全量写回：所有字段无论是否等于默认值都序列化，
        // 否则"加载→保存"工作流会静默丢失用户配置（Hugo 同样全量写回）
        var dict = new Dictionary<string, object>
        {
            ["baseURL"] = config.BaseURL,
            ["title"] = config.Title,
            ["languageCode"] = config.LanguageCode,
            ["theme"] = config.Theme,
            ["buildDrafts"] = config.BuildDrafts,
            ["buildFuture"] = config.BuildFuture,
            ["buildExpired"] = config.BuildExpired,
            ["paginate"] = config.Paginate,
            ["paginatePath"] = config.PaginatePath,
            ["enableGitInfo"] = config.EnableGitInfo,
            ["timeZone"] = config.TimeZone,
            ["summaryLength"] = config.SummaryLength,
            ["disablePathToLower"] = config.DisablePathToLower,
            ["contentDir"] = config.ContentDir,
            ["layoutDir"] = config.LayoutDir,
            ["staticDir"] = config.StaticDir,
            ["assetDir"] = config.AssetDir,
            ["dataDir"] = config.DataDir,
            ["publishDir"] = config.PublishDir,
            ["archetypeDir"] = config.ArchetypeDir
        };

        if (!string.IsNullOrEmpty(config.Copyright))
            dict["copyright"] = config.Copyright;

        if (config.DisableKinds.Count > 0)
            dict["disableKinds"] = config.DisableKinds.ToList();

        if (config.Permalinks is { } permalinks)
        {
            var pl = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(permalinks.Posts))
                pl["posts"] = permalinks.Posts;
            if (!string.IsNullOrEmpty(permalinks.Pages))
                pl["pages"] = permalinks.Pages;
            if (!string.IsNullOrEmpty(permalinks.Categories))
                pl["categories"] = permalinks.Categories;
            if (!string.IsNullOrEmpty(permalinks.Tags))
                pl["tags"] = permalinks.Tags;
            if (pl.Count > 0)
                dict["permalinks"] = pl;
        }

        if (config.Taxonomies is { } taxonomies && taxonomies.Taxonomies.Count > 0)
            dict["taxonomies"] = taxonomies.Taxonomies.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

        if (config.Menus is { } menus && menus.Menus.Count > 0)
        {
            var menusDict = new Dictionary<string, object>();
            foreach (var (menuName, items) in menus.Menus)
            {
                var itemList = new List<object>();
                foreach (var item in items)
                {
                    var itemDict = new Dictionary<string, object>();
                    if (!string.IsNullOrEmpty(item.Name))
                        itemDict["name"] = item.Name;
                    if (!string.IsNullOrEmpty(item.URL))
                        itemDict["url"] = item.URL;
                    if (item.Weight != 0)
                        itemDict["weight"] = item.Weight;
                    // 与解析端七字段对称（ParseDictMenus 读 identifier/parent/pre/post）——
                    // 漏写会使菜单层级（parent）在加载→保存后塌平
                    if (!string.IsNullOrEmpty(item.Identifier))
                        itemDict["identifier"] = item.Identifier;
                    if (!string.IsNullOrEmpty(item.Parent))
                        itemDict["parent"] = item.Parent;
                    if (!string.IsNullOrEmpty(item.Pre))
                        itemDict["pre"] = item.Pre;
                    if (!string.IsNullOrEmpty(item.Post))
                        itemDict["post"] = item.Post;
                    if (itemDict.Count > 0)
                        itemList.Add(itemDict);
                }
                if (itemList.Count > 0)
                    menusDict[menuName] = itemList;
            }
            if (menusDict.Count > 0)
                dict["menus"] = menusDict;
        }

        if (config.Params.Count > 0)
            dict["params"] = new Dictionary<string, object>(config.Params);

        // markup 全量写回（对齐解析端 ParseDictMarkup 的三组子配置）——
        // 漏写会使"加载→保存"工作流静默丢弃用户 markup 配置
        dict["markup"] = new Dictionary<string, object>
        {
            ["tableOfContents"] = new Dictionary<string, object>
            {
                ["startLevel"] = config.Markup.TableOfContents.StartLevel,
                ["endLevel"] = config.Markup.TableOfContents.EndLevel,
                ["ordered"] = config.Markup.TableOfContents.Ordered
            },
            ["highlight"] = new Dictionary<string, object>
            {
                ["style"] = config.Markup.Highlight.Style,
                ["lineNos"] = config.Markup.Highlight.LineNos,
                ["lineNumbersInTable"] = config.Markup.Highlight.LineNumbersInTable,
                ["tabWidth"] = config.Markup.Highlight.TabWidth
            },
            ["goldmark"] = new Dictionary<string, object>
            {
                ["renderer"] = new Dictionary<string, object>
                {
                    ["unsafe"] = config.Markup.Goldmark.Unsafe
                },
                ["extensions"] = new Dictionary<string, object>
                {
                    ["table"] = config.Markup.Goldmark.Extensions.Table,
                    ["strikethrough"] = config.Markup.Goldmark.Extensions.Strikethrough,
                    ["taskList"] = config.Markup.Goldmark.Extensions.TaskList,
                    ["footnote"] = config.Markup.Goldmark.Extensions.Footnote,
                    ["definitionList"] = config.Markup.Goldmark.Extensions.DefinitionList,
                    ["typographer"] = config.Markup.Goldmark.Extensions.Typographer
                }
            }
        };

        if (config.Outputs is { } outputs)
        {
            var od = new Dictionary<string, object>();
            if (outputs.Home.Count > 0)
                od["home"] = outputs.Home.ToList();
            if (outputs.Page.Count > 0)
                od["page"] = outputs.Page.ToList();
            if (outputs.Section.Count > 0)
                od["section"] = outputs.Section.ToList();
            if (outputs.Taxonomy.Count > 0)
                od["taxonomy"] = outputs.Taxonomy.ToList();
            if (outputs.Term.Count > 0)
                od["term"] = outputs.Term.ToList();
            if (od.Count > 0)
                dict["outputs"] = od;
        }

        if (config.Module is { } module)
        {
            var md = new Dictionary<string, object>();
            if (module.Imports.Count > 0)
            {
                var imports = new List<object>();
                foreach (var import in module.Imports)
                {
                    var importDict = new Dictionary<string, object> { ["path"] = import.Path ?? "" };
                    if (import.Disabled)
                        importDict["disabled"] = true;
                    // mounts 与解析端对称（ParseDictModule 读 source/target）——漏写使
                    // 模块挂载配置在加载→保存后丢失
                    if (import.Mounts.Count > 0)
                    {
                        var mounts = new List<object>();
                        foreach (var mount in import.Mounts)
                        {
                            var mountDict = new Dictionary<string, object>
                            {
                                ["source"] = mount.Source ?? "",
                                ["target"] = mount.Target ?? ""
                            };
                            mounts.Add(mountDict);
                        }
                        importDict["mounts"] = mounts;
                    }
                    imports.Add(importDict);
                }
                md["imports"] = imports;
            }
            if (md.Count > 0)
                dict["module"] = md;
        }

        if (config.Security is { } security)
        {
            dict["security"] = new Dictionary<string, object>
            {
                ["httpTimeout"] = security.HttpTimeout,
                ["allowedDomains"] = security.AllowedDomains.ToList(),
                ["allowedCommands"] = security.AllowedCommands.ToList()
            };
        }

        if (config.Caches is { } caches)
        {
            dict["caches"] = new Dictionary<string, object>
            {
                ["enabled"] = caches.Enabled,
                ["dir"] = caches.Dir ?? "",
                ["maxSize"] = caches.MaxSize
            };
        }

        if (config.Languages.Count > 0)
            AddLanguagesToDict(dict, config.Languages);

        if (config.Author is not null)
            AddAuthorToDict(dict, config.Author);

        return dict;
    }

    private static void AddLanguagesToDict(Dictionary<string, object> dict, IReadOnlyDictionary<string, LanguageConfig> languages)
    {
        var languagesDict = new Dictionary<string, object>();
        foreach (var kvp in languages)
        {
            var langDict = new Dictionary<string, object>
            {
                ["languageName"] = kvp.Value.LanguageName,
                ["weight"] = kvp.Value.Weight
            };

            if (!string.IsNullOrEmpty(kvp.Value.Title))
                langDict["title"] = kvp.Value.Title;
            if (!string.IsNullOrEmpty(kvp.Value.ContentDir))
                langDict["contentDir"] = kvp.Value.ContentDir;
            if (kvp.Value.Params.Count > 0)
                langDict["params"] = new Dictionary<string, object>(kvp.Value.Params);

            languagesDict[kvp.Key] = langDict;
        }

        dict["languages"] = languagesDict;
    }

    private static void AddAuthorToDict(Dictionary<string, object> dict, AuthorConfig author)
    {
        var authorDict = new Dictionary<string, object>
        {
            ["name"] = author.Name
        };

        if (!string.IsNullOrEmpty(author.Email))
            authorDict["email"] = author.Email;
        if (!string.IsNullOrEmpty(author.URL))
            authorDict["url"] = author.URL;

        dict["author"] = authorDict;
    }

    /// <summary>
    /// 将字典树递归序列化为 TOML（顶层标量先行，子表随后；AOT 兼容，无反射）
    /// </summary>
    private static string DictToToml(Dictionary<string, object> dict)
    {
        var sb = new StringBuilder();
        AppendTomlTable(sb, dict, rootLevel: true);
        return sb.ToString();
    }

    private static void AppendTomlTable(StringBuilder sb, Dictionary<string, object> dict, bool rootLevel, string? prefix = null)
    {
        // 先标量后子表，保证子表键值归属正确的 TOML 语义
        foreach (var (key, value) in dict)
        {
            // 子表与含字典数组走表头语法，不当内联标量写
            if (value is Dictionary<string, object> || IsDictionaryArray(value))
            {
                continue;
            }
            AppendTomlKeyValue(sb, key, value);
        }

        foreach (var (key, value) in dict)
        {
            var childPrefix = prefix is null ? EscapeTomlKey(key) : $"{prefix}.{EscapeTomlKey(key)}";

            if (IsDictionaryArray(value) && value is IEnumerable<object> tableList)
            {
                // TOML 数组内含表必须用 [[array-of-tables]] 语法，内联 [...] 不可表达。
                // 异构数组（标量与表混排）无 TOML 表达形式——fail loud，
                // 静默丢弃非表元素会让"加载→保存"工作流丢用户数据
                foreach (var item in tableList)
                {
                    if (item is not Dictionary<string, object> itemDict)
                    {
                        throw new InvalidDataException(
                            $"配置项 {childPrefix} 为标量与表混排的数组，无法序列化为 TOML，请改用 JSON 或 YAML 格式");
                    }
                    sb.AppendLine();
                    sb.AppendLine($"[[{childPrefix}]]");
                    AppendTomlTable(sb, itemDict, rootLevel: false, childPrefix);
                }
                continue;
            }

            if (value is Dictionary<string, object> subDict)
            {
                // 表头必须携带完整父路径，否则嵌套结构会被解析成顶层表
                sb.AppendLine();
                sb.AppendLine($"[{childPrefix}]");
                AppendTomlTable(sb, subDict, rootLevel: false, childPrefix);
            }
        }
    }

    /// <summary>数组元素含字典时不可内联，须按 array-of-tables 输出</summary>
    private static bool IsDictionaryArray(object? value) =>
        value is IEnumerable<object> list && list.Any(x => x is Dictionary<string, object>);

    private static void AppendTomlKeyValue(StringBuilder sb, string key, object? value)
    {
        sb.Append(EscapeTomlKey(key)).Append(" = ").AppendLine(FormatTomlValue(value));
    }

    private static string FormatTomlValue(object? value) => value switch
    {
        null => "\"\"",
        bool b => b ? "true" : "false",
        int or long or double or float => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0",
        DateTimeOffset dto => dto.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        string s => $"\"{EscapeTomlString(s)}\"",
        // 非泛型枚举分支兜住 List<int> 等值类型元素集合（IEnumerable<object> 匹配不到值类型元素）
        System.Collections.IEnumerable list => "[" + string.Join(", ", list.Cast<object>().Select(FormatTomlValue)) + "]",
        _ => $"\"{EscapeTomlString(value.ToString() ?? "")}\""
    };

    private static string EscapeTomlKey(string key)
    {
        // 含非裸键字符（字母数字_- 之外）时用引号键
        return TomlBareKeyRegex().IsMatch(key) ? key : $"\"{EscapeTomlString(key)}\"";
    }

    /// <summary>
    /// TOML 基本字符串转义（反斜杠/引号/换行/控制字符）
    /// </summary>
    private static string EscapeTomlString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case < ' ' or '\u007f':
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex TomlBareKeyRegex();

    #endregion
}

/// <summary>
/// 配置 JSON 序列化上下文（source-gen，AOT/trim 友好）。
/// 注册 ConvertConfigToDict 产出的全部运行时形态（嵌套字典/列表/标量/日期），
/// 使 object 持有的值在源生成模式下可解析——替代项目中最后一个反射式 JSON 序列化点
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, object>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<object>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
[System.Text.Json.Serialization.JsonSerializable(typeof(bool))]
[System.Text.Json.Serialization.JsonSerializable(typeof(long))]
[System.Text.Json.Serialization.JsonSerializable(typeof(int))]
[System.Text.Json.Serialization.JsonSerializable(typeof(double))]
[System.Text.Json.Serialization.JsonSerializable(typeof(float))]
[System.Text.Json.Serialization.JsonSerializable(typeof(decimal))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DateTimeOffset))]
[System.Text.Json.Serialization.JsonSerializable(typeof(DateTime))]
internal sealed partial class ConfigJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
#pragma warning restore IL2026, IL3050
