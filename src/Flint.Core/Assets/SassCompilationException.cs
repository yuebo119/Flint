// Flint 静态站点生成器
// Sass 编译失败异常

namespace Flint.Core.Assets;

/// <summary>
/// Dart Sass 编译失败。携带源文件路径与 CLI stderr 明细。
/// </summary>
public sealed class SassCompilationException : Exception
{
    public SassCompilationException(
        string message,
        string? sourcePath = null,
        string? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        SourcePath = sourcePath;
        Details = details;
    }

    /// <summary>编译失败的源文件路径</summary>
    public string? SourcePath { get; }

    /// <summary>Dart Sass CLI 的 stderr 原文</summary>
    public string? Details { get; }
}
