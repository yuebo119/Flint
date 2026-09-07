// Flint 静态站点生成器
// 模板相关异常类型

namespace Flint.Core.Templates;

/// <summary>
/// 模板解析异常
/// </summary>
public sealed class TemplateParseException : Exception
{
    /// <summary>
    /// 模板名称
    /// </summary>
    public string TemplateName { get; }

    /// <summary>
    /// 解析错误列表
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    /// 创建模板解析异常
    /// </summary>
    public TemplateParseException(string templateName, IReadOnlyList<string> errors)
        : base($"模板 '{templateName}' 解析失败: {string.Join("; ", errors)}")
    {
        TemplateName = templateName;
        Errors = errors;
    }
}

/// <summary>
/// 模板未找到异常
/// </summary>
public sealed class TemplateNotFoundException : Exception
{
    /// <summary>
    /// 模板名称
    /// </summary>
    public string TemplateName { get; }

    /// <summary>
    /// 搜索路径列表
    /// </summary>
    public IReadOnlyList<string> SearchPaths { get; }

    /// <summary>
    /// 创建模板未找到异常
    /// </summary>
    public TemplateNotFoundException(string templateName, IReadOnlyList<string> searchPaths)
        : base($"模板 '{templateName}' 未找到。已搜索路径: {string.Join(", ", searchPaths)}")
    {
        TemplateName = templateName;
        SearchPaths = searchPaths;
    }
}

/// <summary>
/// 模板渲染异常
/// </summary>
public sealed class TemplateRenderException : Exception
{
    /// <summary>
    /// 模板名称
    /// </summary>
    public string TemplateName { get; }

    /// <summary>
    /// 错误行号
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// 错误列号
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// 创建模板渲染异常
    /// </summary>
    public TemplateRenderException(
        string templateName,
        string message,
        int line = 0,
        int column = 0,
        Exception? innerException = null)
        : base($"模板 '{templateName}' 渲染失败 (行 {line}, 列 {column}): {message}", innerException)
    {
        TemplateName = templateName;
        Line = line;
        Column = column;
    }
}
