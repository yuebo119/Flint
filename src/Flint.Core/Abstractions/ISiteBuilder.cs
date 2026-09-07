// Flint 静态站点生成器
// 站点构建器接口

using Flint.Core.Models;
using Flint.Core.Server;

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
