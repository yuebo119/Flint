// Flint 静态站点生成器
// Front Matter 元数据类型

using Flint.Core.Abstractions;

namespace Flint.Core.Models;

/// <summary>
/// Front Matter 元数据
/// 支持 YAML、TOML、JSON 格式
/// </summary>
public sealed class FrontMatter
{
    /// <summary>
    /// 文章标题
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 发布日期
    /// </summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>
    /// date 键的 Hugo 特殊日期源标记（":git"/":filemodtime"/":filename"），
    /// 由 <see cref="FrontMatterExtensions.WithDateSources"/> 在解析后兑现为实际时间
    /// </summary>
    public string? DateSource { get; init; }

    /// <summary>
    /// 最后修改日期
    /// </summary>
    public DateTimeOffset? LastMod { get; init; }

    /// <summary>
    /// lastmod 键的 Hugo 特殊日期源标记，语义同 <see cref="DateSource"/>
    /// </summary>
    public string? LastModSource { get; init; }

    /// <summary>
    /// 是否为草稿
    /// </summary>
    public bool Draft { get; init; }

    /// <summary>
    /// 过期日期（过期后不再发布）
    /// </summary>
    public DateTimeOffset? ExpiryDate { get; init; }

    /// <summary>
    /// 发布日期（定时发布）
    /// </summary>
    public DateTimeOffset? PublishDate { get; init; }

    /// <summary>
    /// 标签列表
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// 分类列表
    /// </summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>
    /// 布局模板名称
    /// </summary>
    public string? Layout { get; init; }

    /// <summary>
    /// URL 别名
    /// </summary>
    public string? Slug { get; init; }

    /// <summary>
    /// URL 别名列表（重定向）
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// 文章描述/摘要
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 文章摘要
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// 权重（用于排序）
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 作者
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// 作者列表
    /// </summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>
    /// 关键词（SEO）
    /// </summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>
    /// 内容类型
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// 菜单配置
    /// </summary>
    public IReadOnlyDictionary<string, MenuEntry>? Menus { get; init; }

    /// <summary>
    /// 输出格式
    /// </summary>
    public IReadOnlyList<string> Outputs { get; init; } = [];

    /// <summary>
    /// 级联配置
    /// </summary>
    public CascadeConfig? Cascade { get; init; }

    /// <summary>
    /// 自定义参数
    /// </summary>
    public IReadOnlyDictionary<string, object> Params { get; init; } =
        new Dictionary<string, object>();

    /// <summary>
    /// 原始格式类型
    /// </summary>
    public FrontMatterFormat Format { get; init; } = FrontMatterFormat.Yaml;
}

/// <summary>
/// FrontMatter 不可变拷贝辅助
/// </summary>
public static class FrontMatterExtensions
{
    /// <summary>
    /// 兑现 Hugo 特殊日期源（date/lastmod 的字符串值 ":git"/":filemodtime"）：
    /// ":git" 取文件在 git 中的最后一次提交时间；":filemodtime" 取文件修改时间。
    /// 解析层（<c>FrontMatterParser</c>）遇到特殊源字符串不产出时间，只记录
    /// <see cref="FrontMatter.DateSource"/>/<see cref="FrontMatter.LastModSource"/> 标记，此处兑现。
    /// git 不可用（非仓库/无 git）时该源静默缺省，页面显式配置优先。
    /// </summary>
    public static FrontMatter WithDateSources(
        this FrontMatter fm,
        string filePath,
        DateTimeOffset modifiedTimeUtc,
        IGitDateProvider? gitDates)
    {
        if (fm.DateSource is null && fm.LastModSource is null)
        {
            return fm;
        }

        return new FrontMatter
        {
            Title = fm.Title,
            Date = ApplySource(fm.DateSource, fm.Date, filePath, modifiedTimeUtc, gitDates),
            DateSource = fm.DateSource,
            LastMod = ApplySource(fm.LastModSource, fm.LastMod, filePath, modifiedTimeUtc, gitDates),
            LastModSource = fm.LastModSource,
            Draft = fm.Draft,
            ExpiryDate = fm.ExpiryDate,
            PublishDate = fm.PublishDate,
            Tags = fm.Tags,
            Categories = fm.Categories,
            Layout = fm.Layout,
            Slug = fm.Slug,
            Aliases = fm.Aliases,
            Description = fm.Description,
            Summary = fm.Summary,
            Weight = fm.Weight,
            Author = fm.Author,
            Authors = fm.Authors,
            Keywords = fm.Keywords,
            Type = fm.Type,
            Outputs = fm.Outputs,
            Menus = fm.Menus,
            Cascade = fm.Cascade,
            Params = fm.Params,
            Format = fm.Format
        };
    }

    private static DateTimeOffset? ApplySource(
        string? source,
        DateTimeOffset? fallback,
        string filePath,
        DateTimeOffset modifiedTimeUtc,
        IGitDateProvider? gitDates)
    {
        return source switch
        {
            ":git" => gitDates?.GetLastCommitTime(filePath) ?? fallback,
            ":filemodtime" => modifiedTimeUtc,
            _ => fallback
        };
    }

    /// <summary>
    /// Hugo 的 <c>:filename</c> 日期源：front matter 未设 date/slug 时，
    /// 从文件逻辑名前缀 <c>YYYY-MM-DD-</c> 解析发布日期与 slug。
    /// 不可变模型：返回补全后的新实例；无需补全时原样返回
    /// </summary>
    public static FrontMatter WithFilenameConvention(this FrontMatter fm, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        // 形如 2024-01-15 或 2024-01-15-my-slug
        var dash1 = baseName.IndexOf('-');
        if (dash1 < 0) return fm;
        var dash2 = baseName.IndexOf('-', dash1 + 1);
        if (dash2 < 0) return fm;
        var dash3 = baseName.IndexOf('-', dash2 + 1);
        var datePart = dash3 < 0 ? baseName : baseName[..dash3];
        var slugPart = dash3 < 0 ? null : baseName[(dash3 + 1)..];

        DateTimeOffset filenameDate = default;
        var needsDate = fm.Date is null &&
            DateTimeOffset.TryParseExact(
                datePart, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out filenameDate);
        var needsSlug = string.IsNullOrEmpty(fm.Slug) && slugPart is not null;

        if (!needsDate && !needsSlug)
        {
            return fm;
        }

        return new FrontMatter
        {
            Title = fm.Title,
            Date = needsDate ? filenameDate : fm.Date,
            DateSource = fm.DateSource,
            LastMod = fm.LastMod,
            LastModSource = fm.LastModSource,
            Draft = fm.Draft,
            ExpiryDate = fm.ExpiryDate,
            PublishDate = fm.PublishDate,
            Tags = fm.Tags,
            Categories = fm.Categories,
            Layout = fm.Layout,
            Slug = needsSlug ? slugPart : fm.Slug,
            Aliases = fm.Aliases,
            Description = fm.Description,
            Summary = fm.Summary,
            Weight = fm.Weight,
            Author = fm.Author,
            Authors = fm.Authors,
            Keywords = fm.Keywords,
            Type = fm.Type,
            Outputs = fm.Outputs,
            Menus = fm.Menus,
            Cascade = fm.Cascade,
            Params = fm.Params,
            Format = fm.Format
        };
    }
}

/// <summary>
/// Front Matter 格式类型
/// </summary>
public enum FrontMatterFormat
{
    /// <summary>
    /// YAML 格式（--- 分隔）
    /// </summary>
    Yaml,

    /// <summary>
    /// TOML 格式（+++ 分隔）
    /// </summary>
    Toml,

    /// <summary>
    /// JSON 格式（{ } 包围）
    /// </summary>
    Json
}

/// <summary>
/// 菜单项配置
/// </summary>
public sealed class MenuEntry
{
    /// <summary>
    /// 菜单项名称
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 菜单项权重
    /// </summary>
    public int Weight { get; init; }

    /// <summary>
    /// 父菜单项标识
    /// </summary>
    public string? Parent { get; init; }

    /// <summary>
    /// 菜单项标识
    /// </summary>
    public string? Identifier { get; init; }

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
/// 级联配置
/// </summary>
public sealed class CascadeConfig
{
    /// <summary>
    /// 级联的 Front Matter 数据
    /// </summary>
    public FrontMatter? Data { get; init; }

    /// <summary>
    /// 目标路径模式
    /// </summary>
    public string? Target { get; init; }

    /// <summary>
    /// 内容类型过滤
    /// </summary>
    public string? Kind { get; init; }
}
