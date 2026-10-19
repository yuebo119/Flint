// Flint 静态站点生成器核心库
// TOML 解析适配层：底层 Tommy，对外保持 TomlTable/TomlArray/TomlTableArray
// 与 TryParseTable/ParseTable 两形态错误语义（null / 异常）。

using System.IO;
using Tommy;

namespace Flint.Core.Configuration;

/// <summary>
/// Tommy 适配层。类名保留 TomlynCompat 以缩小调用点改动。
/// </summary>
internal static class TomlynCompat
{
    /// <summary>解析文本为 TomlTable，语法错误返回 null。</summary>
    public static TomlTable? TryParseTable(string content)
    {
        try
        {
            EnsureBalanced(content);
            using var reader = new StringReader(content);
            return Convert(TOML.Parse(reader));
        }
        catch (Exception ex) when (ex is TomlException or TomlSyntaxException or TomlFormatException)
        {
            return null;
        }
    }

    /// <summary>解析文本为 TomlTable，语法错误抛 TomlException。</summary>
    public static TomlTable ParseTable(string content)
    {
        try
        {
            EnsureBalanced(content);
            using var reader = new StringReader(content);
            return Convert(TOML.Parse(reader))
                ?? throw new TomlException("TOML 解析结果为空");
        }
        catch (Exception ex) when (ex is TomlSyntaxException or TomlFormatException)
        {
            throw new TomlException(ex.Message, ex);
        }
    }

    /// <summary>
    /// Tommy 对未闭合流式集合（如 <c>[1, 2, 3</c>）宽容接受，补一层括号/字符串平衡校验，
    /// 保持“非法 TOML 必须抛错”的原有语义。只校验括号与字符串闭合，不做完整 TOML 语法验证。
    /// </summary>
    private static void EnsureBalanced(string content)
    {
        var depth = 0;
        var i = 0;

        while (i < content.Length)
        {
            var c = content[i];

            if (c == '#')
            {
                while (i < content.Length && content[i] is not '\n' and not '\r')
                {
                    i++;
                }
                continue;
            }

            if (c == '"')
            {
                if (i + 2 < content.Length && content[i + 1] == '"' && content[i + 2] == '"')
                {
                    i += 3;
                    while (i < content.Length && !(content[i] == '"' && i + 2 < content.Length &&
                                                    content[i + 1] == '"' && content[i + 2] == '"'))
                    {
                        i++;
                    }
                    if (i >= content.Length)
                    {
                        throw new TomlException("TOML 多行基本字符串未闭合");
                    }
                    i += 3;
                    continue;
                }

                i++;
                while (i < content.Length && content[i] != '"')
                {
                    if (content[i] == '\\')
                    {
                        i++;
                    }
                    i++;
                }
                if (i >= content.Length)
                {
                    throw new TomlException("TOML 基本字符串未闭合");
                }
                i++;
                continue;
            }

            if (c == '\'')
            {
                if (i + 2 < content.Length && content[i + 1] == '\'' && content[i + 2] == '\'')
                {
                    i += 3;
                    while (i < content.Length && !(content[i] == '\'' && i + 2 < content.Length &&
                                                    content[i + 1] == '\'' && content[i + 2] == '\''))
                    {
                        i++;
                    }
                    if (i >= content.Length)
                    {
                        throw new TomlException("TOML 多行字面字符串未闭合");
                    }
                    i += 3;
                    continue;
                }

                i++;
                while (i < content.Length && content[i] != '\'')
                {
                    i++;
                }
                if (i >= content.Length)
                {
                    throw new TomlException("TOML 字面字符串未闭合");
                }
                i++;
                continue;
            }

            if (c is '[' or '{')
            {
                depth++;
            }
            else if (c is ']' or '}')
            {
                depth--;
                if (depth < 0)
                {
                    throw new TomlException("TOML 流式集合括号过早闭合");
                }
            }

            i++;
        }

        if (depth != 0)
        {
            throw new TomlException("TOML 流式集合括号未闭合");
        }
    }

    private static TomlTable Convert(Tommy.TomlTable table)
    {
        var result = new TomlTable();
        foreach (var (key, node) in table.RawTable)
        {
            result[key] = ConvertNode(node);
        }
        return result;
    }

    private static object? ConvertNode(Tommy.TomlNode node)
    {
        if (node.IsTable)
        {
            return Convert(node.AsTable);
        }

        if (node.IsArray)
        {
            var array = node.AsArray;
            if (array.IsTableArray)
            {
                var tableArray = new TomlTableArray();
                foreach (var child in array.RawArray)
                {
                    tableArray.Add(Convert(child.AsTable));
                }
                return tableArray;
            }

            var list = new TomlArray();
            foreach (var child in array.RawArray)
            {
                list.Add(ConvertNode(child));
            }
            return list;
        }

        if (node.IsString)
        {
            return node.AsString.Value;
        }

        if (node.IsInteger)
        {
            return node.AsInteger.Value;
        }

        if (node.IsFloat)
        {
            return node.AsFloat.Value;
        }

        if (node.IsBoolean)
        {
            return node.AsBoolean.Value;
        }

        if (node.IsDateTimeOffset)
        {
            return node.AsDateTimeOffset.Value;
        }

        if (node.IsDateTimeLocal)
        {
            return new DateTimeOffset(node.AsDateTimeLocal.Value);
        }

        return node.HasValue ? node.ToString() : null;
    }
}
