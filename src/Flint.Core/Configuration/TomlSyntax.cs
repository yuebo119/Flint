// Flint 静态站点生成器
// TOML 键语法共享规则（ConfigParser / FrontMatterParser 共用的裸键判定与转义）

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Flint.Core.Configuration;

/// <summary>
/// TOML 键语法共享规则：裸键白名单（字母/数字/<c>_</c>/<c>-</c>）外的键写成引号键，
/// 引号内按 TOML 基本字符串转义
/// </summary>
internal static partial class TomlSyntax
{
    /// <summary>TOML 裸键白名单外的键加引号（引号内按基本字符串转义）</summary>
    internal static string EscapeBareKey(string key)
    {
        // 含非裸键字符（字母数字_- 之外）时用引号键
        return BareKeyRegex().IsMatch(key) ? key : $"\"{EscapeString(key)}\"";
    }

    /// <summary>
    /// TOML 基本字符串转义（反斜杠/引号/换行/控制字符）
    /// </summary>
    internal static string EscapeString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case < ' ' or '\u007f':
                    sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex BareKeyRegex();
}
