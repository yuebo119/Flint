// Flint 静态站点生成器
// 开发服务器选项

namespace Flint.Core.Server;

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
