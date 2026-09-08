// Flint 静态站点生成器
// 构建选项类型

namespace Flint.Core.Models;

/// <summary>
/// 构建选项
/// </summary>
public sealed class BuildOptions
{
    /// <summary>
    /// 源目录路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 缺失模板的处理哲学（对齐讨论：Flint fail-fast vs Hugo 宽容降级）：
    /// Error（默认）构建失败暴露问题；Skip 跳过缺模板页面继续（真实大站点
    /// 声明自定义 layout 依赖原主题时不阻塞全站）
    /// </summary>
    public MissingLayoutBehavior MissingLayout { get; init; } = MissingLayoutBehavior.Error;

    /// <summary>
    /// 输出目录路径
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// 是否压缩输出
    /// </summary>
    public bool Minify { get; init; }

    /// <summary>
    /// 是否包含草稿
    /// </summary>
    public bool IncludeDrafts { get; init; }

    /// <summary>
    /// 是否包含未来日期的内容
    /// </summary>
    public bool IncludeFuture { get; init; }

    /// <summary>
    /// 是否包含过期内容
    /// </summary>
    public bool IncludeExpired { get; init; }

    /// <summary>
    /// 环境名称（development、production 等）
    /// </summary>
    public string? Environment { get; init; }

    /// <summary>
    /// 并行度（默认为 CPU 核心数）
    /// </summary>
    public int Parallelism { get; init; } = System.Environment.ProcessorCount;

    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool EnableCache { get; init; } = true;

    /// <summary>
    /// 最近访问的站点 URL 集合（T4.4 fast render mode）：
    /// 模板变化的增量构建只重渲染这些 URL 对应的页面；
    /// null 或空集时不裁剪（保守完整增量）
    /// </summary>
    public IReadOnlySet<string>? PreferredUrls { get; init; }

    /// <summary>
    /// 缓存目录路径
    /// </summary>
    public string? CachePath { get; init; }

    /// <summary>
    /// 是否清理输出目录
    /// </summary>
    public bool CleanOutput { get; init; } = true;

    /// <summary>
    /// 是否生成 Source Map
    /// </summary>
    public bool GenerateSourceMaps { get; init; }

    /// <summary>
    /// 基础 URL（覆盖配置文件）
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// 是否启用详细日志
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// 是否启用调试模式
    /// </summary>
    public bool Debug { get; init; }
}

/// <summary>
/// 缺失模板行为
/// </summary>
public enum MissingLayoutBehavior
{
    /// <summary>构建失败（默认，fail-fast）</summary>
    Error,
    /// <summary>跳过该页继续构建（对齐 Hugo 大规模宽容语义）</summary>
    Skip
}
