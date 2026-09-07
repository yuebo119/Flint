// Flint 静态站点生成器
// 短代码未注册异常

namespace Flint.Core.Content.Shortcodes;

/// <summary>
/// 短代码未注册异常
/// 对齐 Hugo 语义："template for shortcode %q not found" 是硬错误——
/// 静默输出 HTML 注释会让缺 shortcode 的页面带着可见残缺上线
/// </summary>
public sealed class ShortcodeNotFoundException : Exception
{
    /// <summary>
    /// 未注册的短代码名称
    /// </summary>
    public string ShortcodeName { get; }

    /// <summary>
    /// 短代码在源文本中的起始位置
    /// </summary>
    public int Position { get; }

    public ShortcodeNotFoundException(string shortcodeName, int position = -1)
        : base(position >= 0
            ? $"未注册的短代码: {shortcodeName}（源文本位置 {position}）"
            : $"未注册的短代码: {shortcodeName}")
    {
        ShortcodeName = shortcodeName;
        Position = position;
    }
}
