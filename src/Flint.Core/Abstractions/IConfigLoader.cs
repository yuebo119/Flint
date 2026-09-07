// Flint 静态站点生成器
// 配置加载器接口

namespace Flint.Core.Abstractions;

/// <summary>
/// 配置加载器接口
/// </summary>
public interface IConfigLoader
{
    /// <summary>
    /// 加载站点配置
    /// </summary>
    /// <param name="configPath">配置文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>站点配置</returns>
    ValueTask<SiteConfig> LoadAsync(
        string configPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同步加载配置
    /// </summary>
    /// <param name="configPath">配置文件路径</param>
    /// <returns>站点配置</returns>
    SiteConfig Load(string configPath);

    /// <summary>
    /// 自动检测并加载配置文件
    /// </summary>
    /// <param name="directory">站点目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>站点配置</returns>
    ValueTask<SiteConfig> AutoLoadAsync(
        string directory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <param name="configPath">配置文件路径</param>
    /// <param name="format">配置格式</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask SaveAsync(
        SiteConfig config,
        string configPath,
        ConfigFormat format = ConfigFormat.Toml,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 配置验证器接口
/// </summary>
public interface IConfigValidator
{
    /// <summary>
    /// 验证配置
    /// </summary>
    /// <param name="config">站点配置</param>
    /// <returns>验证结果</returns>
    ValidationResult Validate(SiteConfig config);
}

/// <summary>
/// 验证结果
/// </summary>
public readonly record struct ValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// 验证错误列表
    /// </summary>
    public required IReadOnlyList<ValidationError> Errors { get; init; }

    /// <summary>
    /// 创建成功的验证结果
    /// </summary>
    public static ValidationResult Success => new()
    {
        IsValid = true,
        Errors = []
    };

    /// <summary>
    /// 创建失败的验证结果
    /// </summary>
    public static ValidationResult Failure(params ValidationError[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}

/// <summary>
/// 验证错误
/// </summary>
public readonly record struct ValidationError
{
    /// <summary>
    /// 属性路径
    /// </summary>
    public required string PropertyPath { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 错误代码
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 期望值
    /// </summary>
    public string? ExpectedValue { get; init; }

    /// <summary>
    /// 实际值
    /// </summary>
    public string? ActualValue { get; init; }
}

/// <summary>
/// 配置格式
/// </summary>
public enum ConfigFormat
{
    /// <summary>
    /// TOML 格式
    /// </summary>
    Toml,

    /// <summary>
    /// YAML 格式
    /// </summary>
    Yaml,

    /// <summary>
    /// JSON 格式
    /// </summary>
    Json
}

/// <summary>
/// 站点配置
/// </summary>
public sealed class SiteConfig
{
    /// <summary>
    /// 基础 URL
    /// </summary>
    public required string BaseURL { get; init; }

    /// <summary>
    /// 站点标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 语言代码
    /// </summary>
    public string LanguageCode { get; init; } = "en";

    /// <summary>
    /// 主题名称
    /// </summary>
    public string Theme { get; init; } = "";

    /// <summary>
    /// 是否构建草稿
    /// </summary>
    public bool BuildDrafts { get; init; }

    /// <summary>
    /// 是否构建未来内容
    /// </summary>
    public bool BuildFuture { get; init; }

    /// <summary>
    /// 是否构建过期内容
    /// </summary>
    public bool BuildExpired { get; init; }

    /// <summary>
    /// 每页文章数
    /// </summary>
    public int Paginate { get; init; } = 10;

    /// <summary>
    /// 分页路径
    /// </summary>
    public string PaginatePath { get; init; } = "page";

    /// <summary>
    /// 永久链接配置
    /// </summary>
    public PermalinkConfig Permalinks { get; init; } = new();

    /// <summary>
    /// 分类配置
    /// </summary>
    public TaxonomyConfig Taxonomies { get; init; } = new();

    /// <summary>
    /// 菜单配置
    /// </summary>
    public MenuConfig Menus { get; init; } = new();

    /// <summary>
    /// 站点参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// Markdown 渲染配置
    /// </summary>
    public MarkupConfig Markup { get; init; } = new();

    /// <summary>
    /// 输出格式配置
    /// </summary>
    public OutputConfig Outputs { get; init; } = new();

    /// <summary>
    /// 多语言配置
    /// </summary>
    public IReadOnlyDictionary<string, LanguageConfig> Languages { get; init; } =
        new Dictionary<string, LanguageConfig>();

    /// <summary>
    /// 模块配置
    /// </summary>
    public ModuleConfig Module { get; init; } = new();

    /// <summary>
    /// 安全配置
    /// </summary>
    public SecurityConfig Security { get; init; } = new();

    /// <summary>
    /// 缓存配置
    /// </summary>
    public CacheConfig Caches { get; init; } = new();

    /// <summary>
    /// 是否启用 Git 信息（Hugo <c>enableGitInfo</c>）：启用后页面可用 <c>:git</c> 特殊日期源
    /// </summary>
    public bool EnableGitInfo { get; init; }

    /// <summary>
    /// 站点时区（IANA 名称，Hugo <c>timeZone</c>）：
    /// 无偏移的日期值按此时区解释；空串时沿用本机时区
    /// </summary>
    public string TimeZone { get; init; } = "";

    /// <summary>
    /// 摘要长度
    /// </summary>
    public int SummaryLength { get; init; } = 70;

    /// <summary>
    /// 版权信息
    /// </summary>
    public string? Copyright { get; init; }

    /// <summary>
    /// 作者信息
    /// </summary>
    public AuthorConfig? Author { get; init; }

    /// <summary>
    /// 是否禁用路径小写转换
    /// </summary>
    public bool DisablePathToLower { get; init; }

    /// <summary>
    /// 禁用的 Kind/输出类型列表（对齐 Hugo 语义，如 ["RSS", "sitemap"]；空 = 全部启用）
    /// </summary>
    public IReadOnlyList<string> DisableKinds { get; init; } = [];

    /// <summary>
    /// 内容目录
    /// </summary>
    public string ContentDir { get; init; } = "content";

    /// <summary>
    /// 布局目录
    /// </summary>
    public string LayoutDir { get; init; } = "layouts";

    /// <summary>
    /// 静态文件目录
    /// </summary>
    public string StaticDir { get; init; } = "static";

    /// <summary>
    /// 资源目录
    /// </summary>
    public string AssetDir { get; init; } = "assets";

    /// <summary>
    /// 数据目录
    /// </summary>
    public string DataDir { get; init; } = "data";

    /// <summary>
    /// 发布目录
    /// </summary>
    public string PublishDir { get; init; } = "public";

    /// <summary>
    /// 原型目录
    /// </summary>
    public string ArchetypeDir { get; init; } = "archetypes";
}

/// <summary>
/// 永久链接配置
/// </summary>
public sealed class PermalinkConfig
{
    /// <summary>
    /// 文章永久链接模式
    /// </summary>
    public string Posts { get; init; } = "/:year/:month/:title/";

    /// <summary>
    /// 页面永久链接模式
    /// </summary>
    public string Pages { get; init; } = "/:title/";

    /// <summary>
    /// 分类永久链接模式
    /// </summary>
    public string Categories { get; init; } = "/categories/:slug/";

    /// <summary>
    /// 标签永久链接模式
    /// </summary>
    public string Tags { get; init; } = "/tags/:slug/";
}

/// <summary>
/// 分类配置
/// </summary>
public sealed class TaxonomyConfig
{
    /// <summary>
    /// 分类名称映射
    /// </summary>
    public IReadOnlyDictionary<string, string> Taxonomies { get; init; } =
        new Dictionary<string, string>
        {
            ["category"] = "categories",
            ["tag"] = "tags"
        };
}

/// <summary>
/// 菜单配置
/// </summary>
public sealed class MenuConfig
{
    /// <summary>
    /// 菜单定义
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<MenuItemConfig>> Menus { get; init; } =
        new Dictionary<string, IReadOnlyList<MenuItemConfig>>();
}

/// <summary>
/// 菜单项配置
/// </summary>
public sealed record MenuItemConfig
{
    /// <summary>
    /// 菜单项名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 菜单项 URL
    /// </summary>
    public string? URL { get; init; }

    /// <summary>
    /// 菜单项权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 菜单项标识
    /// </summary>
    public string? Identifier { get; init; }

    /// <summary>
    /// 父菜单项标识
    /// </summary>
    public string? Parent { get; init; }

    /// <summary>
    /// 前置内容
    /// </summary>
    public string? Pre { get; init; }

    /// <summary>
    /// 后置内容
    /// </summary>
    public string? Post { get; init; }
}

/// <summary>
/// Markdown 渲染配置
/// </summary>
public sealed class MarkupConfig
{
    /// <summary>
    /// 目录配置
    /// </summary>
    public TableOfContentsConfig TableOfContents { get; init; } = new();

    /// <summary>
    /// 代码高亮配置
    /// </summary>
    public HighlightConfig Highlight { get; init; } = new();

    /// <summary>
    /// Goldmark 配置
    /// </summary>
    public GoldmarkConfig Goldmark { get; init; } = new();
}

/// <summary>
/// 目录配置
/// </summary>
public sealed class TableOfContentsConfig
{
    /// <summary>
    /// 起始标题级别
    /// </summary>
    public int StartLevel { get; init; } = 2;

    /// <summary>
    /// 结束标题级别
    /// </summary>
    public int EndLevel { get; init; } = 3;

    /// <summary>
    /// 是否有序列表
    /// </summary>
    public bool Ordered { get; init; }
}

/// <summary>
/// 代码高亮配置
/// </summary>
public sealed class HighlightConfig
{
    /// <summary>
    /// 高亮样式
    /// </summary>
    public string Style { get; init; } = "monokai";

    /// <summary>
    /// 是否显示行号
    /// </summary>
    public bool LineNos { get; init; }

    /// <summary>
    /// 行号是否在表格中
    /// </summary>
    public bool LineNumbersInTable { get; init; } = true;

    /// <summary>
    /// Tab 宽度
    /// </summary>
    public int TabWidth { get; init; } = 4;
}

/// <summary>
/// Goldmark 配置
/// </summary>
public sealed class GoldmarkConfig
{
    /// <summary>
    /// 是否启用不安全 HTML
    /// </summary>
    public bool Unsafe { get; init; }

    /// <summary>
    /// 扩展配置
    /// </summary>
    public GoldmarkExtensions Extensions { get; init; } = new();
}

/// <summary>
/// Goldmark 扩展配置
/// </summary>
public sealed class GoldmarkExtensions
{
    /// <summary>
    /// 是否启用表格
    /// </summary>
    public bool Table { get; init; } = true;

    /// <summary>
    /// 是否启用删除线
    /// </summary>
    public bool Strikethrough { get; init; } = true;

    /// <summary>
    /// 是否启用任务列表
    /// </summary>
    public bool TaskList { get; init; } = true;

    /// <summary>
    /// 是否启用脚注
    /// </summary>
    public bool Footnote { get; init; } = true;

    /// <summary>
    /// 是否启用定义列表
    /// </summary>
    public bool DefinitionList { get; init; } = true;

    /// <summary>
    /// 是否启用排版替换
    /// </summary>
    public bool Typographer { get; init; } = true;
}

/// <summary>
/// 输出配置
/// </summary>
public sealed class OutputConfig
{
    /// <summary>
    /// 首页输出格式
    /// </summary>
    public IReadOnlyList<string> Home { get; init; } = ["HTML", "RSS"];

    /// <summary>
    /// 章节输出格式
    /// </summary>
    public IReadOnlyList<string> Section { get; init; } = ["HTML", "RSS"];

    /// <summary>
    /// 分类输出格式
    /// </summary>
    public IReadOnlyList<string> Taxonomy { get; init; } = ["HTML", "RSS"];

    /// <summary>
    /// 术语输出格式
    /// </summary>
    public IReadOnlyList<string> Term { get; init; } = ["HTML", "RSS"];

    /// <summary>
    /// 页面输出格式
    /// </summary>
    public IReadOnlyList<string> Page { get; init; } = ["HTML"];
}

/// <summary>
/// 语言配置
/// </summary>
public sealed class LanguageConfig
{
    /// <summary>
    /// 语言名称
    /// </summary>
    public required string LanguageName { get; init; }

    /// <summary>
    /// 语言权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 站点标题
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// 内容目录
    /// </summary>
    public string? ContentDir { get; init; }

    /// <summary>
    /// 语言参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();
}

/// <summary>
/// 模块配置
/// </summary>
public sealed class ModuleConfig
{
    /// <summary>
    /// 模块导入
    /// </summary>
    public IReadOnlyList<ModuleImport> Imports { get; init; } = [];
}

/// <summary>
/// 模块导入
/// </summary>
public sealed class ModuleImport
{
    /// <summary>
    /// 模块路径
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 是否禁用
    /// </summary>
    public bool Disabled { get; init; }

    /// <summary>
    /// 挂载点
    /// </summary>
    public IReadOnlyList<ModuleMount> Mounts { get; init; } = [];
}

/// <summary>
/// 模块挂载点
/// </summary>
public sealed class ModuleMount
{
    /// <summary>
    /// 源路径
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// 目标路径
    /// </summary>
    public required string Target { get; init; }
}

/// <summary>
/// 安全配置
/// </summary>
public sealed class SecurityConfig
{
    /// <summary>
    /// 允许的 HTTP 域名
    /// </summary>
    public IReadOnlyList<string> AllowedDomains { get; init; } = [];

    /// <summary>
    /// 允许的命令
    /// </summary>
    public IReadOnlyList<string> AllowedCommands { get; init; } = [];

    /// <summary>
    /// HTTP 请求超时（秒）
    /// </summary>
    public int HttpTimeout { get; init; } = 30;
}

/// <summary>
/// 缓存配置
/// </summary>
public sealed class CacheConfig
{
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 缓存目录
    /// </summary>
    public string? Dir { get; init; }

    /// <summary>
    /// 最大缓存大小（MB）
    /// </summary>
    public int MaxSize { get; init; } = 100;
}

/// <summary>
/// 作者配置
/// </summary>
public sealed class AuthorConfig
{
    /// <summary>
    /// 作者名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 作者邮箱
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// 作者网站
    /// </summary>
    public string? URL { get; init; }
}
