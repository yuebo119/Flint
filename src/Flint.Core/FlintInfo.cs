// Flint 静态站点生成器核心库
// 版本和元数据信息

namespace Flint.Core;

/// <summary>
/// Flint 版本和元数据信息
/// </summary>
public static class FlintInfo
{
    /// <summary>
    /// 产品名称
    /// </summary>
    public const string ProductName = "Flint";

    /// <summary>
    /// 产品描述
    /// </summary>
    public const string Description = "下一代高性能静态站点生成器";

    /// <summary>
    /// 当前版本
    /// </summary>
    public const string Version = "0.1.0";

    /// <summary>
    /// 目标运行时
    /// </summary>
    public const string Runtime = ".NET 10";

    /// <summary>
    /// 获取完整的版本信息字符串
    /// </summary>
    public static string VersionInfo =>
        $"{ProductName} v{Version} ({Runtime})";

    /// <summary>
    /// 获取详细的版本信息
    /// </summary>
    public static string DetailedVersionInfo =>
        $"""
        {ProductName} v{Version}
        运行时: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}
        操作系统: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}
        架构: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}
        """;
}
