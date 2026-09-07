// Flint 静态站点生成器
// 站点构建器接口

using Flint.Core.Models;

namespace Flint.Core.Abstractions;

/// <summary>
/// 站点构建器接口
/// </summary>
public interface ISiteBuilder
{
    /// <summary>
    /// 构建站点
    /// </summary>
    /// <param name="options">构建选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>构建结果</returns>
    ValueTask<BuildResult> BuildAsync(
        BuildOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 增量构建
    /// </summary>
    /// <param name="options">构建选项</param>
    /// <param name="changedFiles">变化的文件列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>构建结果</returns>
    ValueTask<BuildResult> IncrementalBuildAsync(
        BuildOptions options,
        IReadOnlyList<string> changedFiles,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理输出目录
    /// </summary>
    /// <param name="outputPath">输出目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask CleanAsync(
        string outputPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 开发服务器接口
/// </summary>
public interface IDevServer
{
    /// <summary>
    /// 启动开发服务器
    /// </summary>
    /// <param name="options">服务器选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StartAsync(
        DevServerOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止开发服务器
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 服务器是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 服务器 URL
    /// </summary>
    string? ServerUrl { get; }
}

/// <summary>
/// 开发服务器选项
/// </summary>
public sealed class DevServerOptions
{
    /// <summary>
    /// 源目录路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 输出目录路径
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// 服务器端口
    /// </summary>
    public int Port { get; init; } = 1313;

    /// <summary>
    /// 是否启用热重载
    /// </summary>
    public bool LiveReload { get; init; } = true;

    /// <summary>
    /// 是否自动打开浏览器
    /// </summary>
    public bool OpenBrowser { get; init; } = true;

    /// <summary>
    /// 是否使用 HTTPS
    /// </summary>
    public bool UseHttps { get; init; }

    /// <summary>
    /// 绑定地址
    /// </summary>
    public string BindAddress { get; init; } = "localhost";

    /// <summary>
    /// 是否包含草稿
    /// </summary>
    public bool IncludeDrafts { get; init; } = true;

    /// <summary>
    /// 是否包含未来内容
    /// </summary>
    public bool IncludeFuture { get; init; } = true;

    /// <summary>
    /// 是否启用详细日志
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// fast render mode（T4.4，默认开启，对齐 Hugo）：记录浏览器最近访问的 URL（容量 20），
    /// 模板变化的增量构建只重渲染这些页面，改模板秒刷当前页而其余页不动
    /// </summary>
    public bool FastRenderMode { get; init; } = true;
}

/// <summary>
/// 文件监视器接口
/// </summary>
public interface IFileWatcher : IDisposable
{
    /// <summary>
    /// 监视文件变化
    /// </summary>
    /// <param name="path">监视路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件变化事件流</returns>
    IAsyncEnumerable<FileChangeEvent> WatchAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 开始监视
    /// </summary>
    /// <param name="path">监视路径</param>
    void Start(string path);

    /// <summary>
    /// 停止监视
    /// </summary>
    void Stop();
}

/// <summary>
/// 文件变化事件
/// </summary>
public readonly record struct FileChangeEvent
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// 变化类型
    /// </summary>
    public required FileChangeType ChangeType { get; init; }

    /// <summary>
    /// 事件时间戳
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// 旧路径（重命名时使用）
    /// </summary>
    public string? OldPath { get; init; }
}

/// <summary>
/// 文件变化类型
/// </summary>
public enum FileChangeType
{
    /// <summary>
    /// 文件创建
    /// </summary>
    Created,

    /// <summary>
    /// 文件修改
    /// </summary>
    Modified,

    /// <summary>
    /// 文件删除
    /// </summary>
    Deleted,

    /// <summary>
    /// 文件重命名
    /// </summary>
    Renamed
}

/// <summary>
/// 热重载通知器接口
/// </summary>
public interface ILiveReloadNotifier
{
    /// <summary>
    /// 通知浏览器刷新
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask NotifyReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 通知 CSS 更新（无需完全刷新）
    /// </summary>
    /// <param name="path">CSS 文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    ValueTask NotifyCssUpdateAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 连接的客户端数量
    /// </summary>
    int ConnectedClients { get; }
}

/// <summary>
/// 分页器
/// </summary>
/// <typeparam name="T">分页项类型</typeparam>
public sealed class Paginator<T>
{
    /// <summary>
    /// 当前页的项目
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// 当前页码（从 1 开始）
    /// </summary>
    public required int PageNumber { get; init; }

    /// <summary>
    /// 每页大小
    /// </summary>
    public required int PageSize { get; init; }

    /// <summary>
    /// 总页数
    /// </summary>
    public required int TotalPages { get; init; }

    /// <summary>
    /// 总项目数
    /// </summary>
    public required int TotalItems { get; init; }

    /// <summary>
    /// 是否有上一页
    /// </summary>
    public bool HasPrev => PageNumber > 1;

    /// <summary>
    /// 是否有下一页
    /// </summary>
    public bool HasNext => PageNumber < TotalPages;

    /// <summary>
    /// 上一页页码
    /// </summary>
    public int? PrevPageNumber => HasPrev ? PageNumber - 1 : null;

    /// <summary>
    /// 下一页页码
    /// </summary>
    public int? NextPageNumber => HasNext ? PageNumber + 1 : null;

    /// <summary>
    /// 是否为第一页
    /// </summary>
    public bool IsFirst => PageNumber == 1;

    /// <summary>
    /// 是否为最后一页
    /// </summary>
    public bool IsLast => PageNumber == TotalPages;

    /// <summary>
    /// 创建分页器
    /// </summary>
    /// <param name="allItems">所有项目</param>
    /// <param name="pageNumber">页码</param>
    /// <param name="pageSize">每页大小</param>
    /// <returns>分页器实例</returns>
    public static Paginator<T> Create(
        IReadOnlyList<T> allItems,
        int pageNumber,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(allItems);

        var totalItems = allItems.Count;
        var totalPages = totalItems > 0 ? (int)Math.Ceiling((double)totalItems / pageSize) : 1;
        pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

        var skip = (pageNumber - 1) * pageSize;
        var items = allItems.Skip(skip).Take(pageSize).ToList();

        return new Paginator<T>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = totalPages,
            TotalItems = totalItems
        };
    }
}
