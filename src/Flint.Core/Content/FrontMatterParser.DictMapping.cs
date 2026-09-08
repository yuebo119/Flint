// Flint 静态站点生成器
// Front Matter 解析器——字典映射部分：FrontMatter 与字典的双向转换
// （从 FrontMatterParser.cs 按 partial 拆出）

using Flint.Core.Abstractions;
using Flint.Core.Models;

namespace Flint.Core.Content;

/// <summary>
/// Front Matter 解析器——字典映射部分
/// （主文件：FrontMatterParser.cs，含类声明与 DetectFormat 公开方法）
/// </summary>
#pragma warning disable IL2026, IL3050 // AOT 警告在此类中被抑制
public sealed partial class FrontMatterParser
{
    /// <summary>
    /// 将字典转换为 FrontMatter
    /// </summary>
    private static FrontMatter ConvertDictToFrontMatter(
        Dictionary<string, object> dict,
        FrontMatterFormat format,
        TimeZoneInfo? siteTimeZone)
    {
        // Hugo 特殊日期源（date/lastmod = ":git"/":filemodtime"/":filename"）：
        // 解析期不产出时间，只记录源标记，由 FrontMatterExtensions.WithDateSources 兑现
        var (date, dateSource) = GetDictDateWithSource(dict, "date", siteTimeZone);
        var (lastMod, lastModSource) = GetDictDateWithSource(dict, "lastmod", siteTimeZone);
        lastMod ??= GetDictDateTime(dict, "lastMod", siteTimeZone)
            ?? GetDictDateTime(dict, "modified", siteTimeZone); // Hugo 别名（不带源标记）
        return new FrontMatter
        {
            Title = GetDictString(dict, "title") ?? "Untitled",
            Date = date,
            DateSource = dateSource,
            LastMod = lastMod,
            LastModSource = lastModSource,
            Draft = GetDictBool(dict, "draft") ?? false,
            ExpiryDate = GetDictDateTime(dict, "expiryDate", siteTimeZone) ?? GetDictDateTime(dict, "expirydate", siteTimeZone)
                ?? GetDictDateTime(dict, "unpublishdate", siteTimeZone), // Hugo 别名
            PublishDate = GetDictDateTime(dict, "publishDate", siteTimeZone) ?? GetDictDateTime(dict, "publishdate", siteTimeZone)
                ?? GetDictDateTime(dict, "pubdate", siteTimeZone) ?? GetDictDateTime(dict, "published", siteTimeZone), // Hugo 别名
            Tags = GetDictStringList(dict, "tags"),
            Categories = GetDictStringList(dict, "categories"),
            Layout = GetDictString(dict, "layout"),
            Slug = GetDictString(dict, "slug"),
            Aliases = GetDictStringList(dict, "aliases"),
            Description = GetDictString(dict, "description"),
            Summary = GetDictString(dict, "summary"),
            Weight = GetDictInt(dict, "weight") ?? 0,
            Author = GetDictString(dict, "author"),
            Authors = GetDictStringList(dict, "authors"),
            Keywords = GetDictStringList(dict, "keywords"),
            Type = GetDictString(dict, "type"),
            Outputs = GetDictStringList(dict, "outputs"),
            Menus = GetDictMenus(dict),
            Cascade = GetDictCascade(dict, format, siteTimeZone),
            Params = GetDictParams(dict),
            Format = format
        };
    }

    /// <summary>
    /// 解析页面级菜单配置（menu.main: {name, weight, parent, identifier}）
    /// 此前该配置被解析器静默丢弃（模型有属性但三路 Convert 均不填充）。
    /// 兼容 YamlDotNet 的 Dictionary&lt;object, object&gt; 嵌套产物——不兼容时
    /// YAML 格式的 menu 配置静默失效
    /// </summary>
    private static IReadOnlyDictionary<string, MenuEntry>? GetDictMenus(Dictionary<string, object> dict)
    {
        if (!dict.TryGetValue("menu", out var value))
        {
            return null;
        }
        var obj = AsStringKeyDict(value);
        if (obj is null)
        {
            return null;
        }

        var menus = new Dictionary<string, MenuEntry>();
        foreach (var (menuKey, entryObj) in obj)
        {
            var entry = AsStringKeyDict(entryObj);
            if (entry is null)
            {
                continue;
            }

            menus[menuKey] = new MenuEntry
            {
                Name = GetEntryString(entry, "name"),
                Weight = GetEntryInt(entry, "weight") ?? 0,
                Parent = GetEntryString(entry, "parent"),
                Identifier = GetEntryString(entry, "identifier")
            };
        }

        return menus.Count > 0 ? menus : null;
    }

    /// <summary>
    /// 嵌套字典统一为 string 键：YamlDotNet 对无类型 mapping 产出
    /// Dictionary&lt;object, object&gt;，与 TOML/JSON 路径的 string 键字典形态不一致
    /// </summary>
    private static Dictionary<string, object>? AsStringKeyDict(object? value)
    {
        return value switch
        {
            Dictionary<string, object> d => d,
            Dictionary<object, object> d => d.ToDictionary(
                kvp => kvp.Key?.ToString() ?? "",
                kvp => kvp.Value),
            _ => null
        };
    }

    /// <summary>
    /// 解析页面级级联配置（cascade: {&lt;下级默认键&gt;..., _target: {path, kind}}）
    /// </summary>
    private static CascadeConfig? GetDictCascade(
        Dictionary<string, object> dict,
        FrontMatterFormat format,
        TimeZoneInfo? siteTimeZone)
    {
        if (!dict.TryGetValue("cascade", out var value))
        {
            return null;
        }
        var obj = AsStringKeyDict(value);
        if (obj is null)
        {
            return null;
        }

        // _target 是控制键：从 Data 中剔除，避免泄漏进 cascade.Data.Params
        var targetDict = obj.TryGetValue("_target", out var t) ? AsStringKeyDict(t) : null;
        var dataObj = new Dictionary<string, object>(obj);
        dataObj.Remove("_target");

        return new CascadeConfig
        {
            Data = ConvertDictToFrontMatter(dataObj, format, siteTimeZone),
            Target = targetDict is not null ? GetEntryString(targetDict, "path") : null,
            Kind = targetDict is not null ? GetEntryString(targetDict, "kind") : null
        };
    }

    private static string? GetEntryString(Dictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) && value is string s && s.Length > 0 ? s : null;
    }

    private static int? GetEntryInt(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            int i => i,
            // 超范围 unchecked 截断会把超大权重/行号变成负数——显式范围检查
            long l => l is >= int.MinValue and <= int.MaxValue ? (int)l : null,
            double d => d is >= int.MinValue and <= int.MaxValue ? (int)d : null,
            _ => null
        };
    }


    /// <summary>
    /// 将 FrontMatter 转换为字典
    /// </summary>
    private static Dictionary<string, object> ConvertFrontMatterToDict(FrontMatter fm)
    {
        var dict = new Dictionary<string, object>
        {
            ["title"] = fm.Title
        };

        if (fm.Date.HasValue)
            dict["date"] = fm.Date.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
        else if (!string.IsNullOrEmpty(fm.DateSource))
            dict["date"] = fm.DateSource; // 特殊日期源（":git" 等）原样写出，往返不丢

        if (fm.LastMod.HasValue)
            dict["lastmod"] = fm.LastMod.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
        else if (!string.IsNullOrEmpty(fm.LastModSource))
            dict["lastmod"] = fm.LastModSource;

        if (fm.Draft)
            dict["draft"] = true;

        if (!string.IsNullOrEmpty(fm.Description))
            dict["description"] = fm.Description;

        if (!string.IsNullOrEmpty(fm.Summary))
            dict["summary"] = fm.Summary;

        if (fm.Tags.Count > 0)
            dict["tags"] = fm.Tags.ToList();

        if (fm.Categories.Count > 0)
            dict["categories"] = fm.Categories.ToList();

        if (!string.IsNullOrEmpty(fm.Layout))
            dict["layout"] = fm.Layout;

        if (!string.IsNullOrEmpty(fm.Slug))
            dict["slug"] = fm.Slug;

        if (!string.IsNullOrEmpty(fm.Author))
            dict["author"] = fm.Author;

        if (fm.Weight != 0)
            dict["weight"] = fm.Weight;

        if (fm.ExpiryDate.HasValue)
            dict["expiryDate"] = fm.ExpiryDate.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

        if (fm.PublishDate.HasValue)
            dict["publishDate"] = fm.PublishDate.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);

        if (fm.Aliases.Count > 0)
            dict["aliases"] = fm.Aliases.ToList();

        if (fm.Authors.Count > 0)
            dict["authors"] = fm.Authors.ToList();

        if (fm.Keywords.Count > 0)
            dict["keywords"] = fm.Keywords.ToList();

        if (fm.Outputs.Count > 0)
            dict["outputs"] = fm.Outputs.ToList();

        if (!string.IsNullOrEmpty(fm.Type))
            dict["type"] = fm.Type;

        // menu/cascade 键名与解析端约定一致（menu 单数、cascade._target 嵌套）
        if (fm.Menus is { } menus && menus.Count > 0)
        {
            var menusDict = new Dictionary<string, object>();
            foreach (var (menuKey, entry) in menus)
            {
                var entryDict = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(entry.Name))
                    entryDict["name"] = entry.Name;
                if (entry.Weight != 0)
                    entryDict["weight"] = entry.Weight;
                if (!string.IsNullOrEmpty(entry.Parent))
                    entryDict["parent"] = entry.Parent;
                if (!string.IsNullOrEmpty(entry.Identifier))
                    entryDict["identifier"] = entry.Identifier;
                menusDict[menuKey] = entryDict;
            }
            dict["menu"] = menusDict;
        }

        if (fm.Cascade is { } cascade)
        {
            var cascadeDict = new Dictionary<string, object>();
            if (cascade.Data is not null)
            {
                foreach (var kvp in ConvertFrontMatterToDict(cascade.Data))
                {
                    cascadeDict[kvp.Key] = kvp.Value;
                }
            }

            if (!string.IsNullOrEmpty(cascade.Target) || !string.IsNullOrEmpty(cascade.Kind))
            {
                var targetDict = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(cascade.Target))
                    targetDict["path"] = cascade.Target;
                if (!string.IsNullOrEmpty(cascade.Kind))
                    targetDict["kind"] = cascade.Kind;
                cascadeDict["_target"] = targetDict;
            }

            if (cascadeDict.Count > 0)
                dict["cascade"] = cascadeDict;
        }

        if (fm.Params.Count > 0)
        {
            foreach (var kvp in fm.Params)
            {
                dict[kvp.Key] = kvp.Value;
            }
        }

        return dict;
    }

    private static string? GetDictString(Dictionary<string, object> dict, string key)
    {
        return dict.TryGetValue(key, out var value) && value is string s ? s : null;
    }

    private static bool? GetDictBool(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static int? GetDictInt(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return null;
        return value switch
        {
            int i => i,
            // 超范围 unchecked 截断会静默产出负数（同 ConfigParser.GetDictInt 修复）
            long l => l is >= int.MinValue and <= int.MaxValue ? (int)l : null,
            // YAML "weight: 2.0" 解析为 double，缺分支会落 _ 静默变 0
            double d => d is >= int.MinValue and <= int.MaxValue ? (int)d : null,
            string s => int.TryParse(s, out var result) ? result : null,
            _ => null
        };
    }

    private static List<string> GetDictStringList(Dictionary<string, object> dict, string key)
    {
        if (!dict.TryGetValue(key, out var value))
            return [];
        return value switch
        {
            IEnumerable<string> list => list.ToList(),
            IEnumerable<object> list => list.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList(),
            _ => []
        };
    }

    private static Dictionary<string, object> GetDictParams(Dictionary<string, object> dict)
    {
        // 收集所有非标准字段作为 params
        var standardKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "title", "date", "lastmod", "draft", "expirydate", "publishdate",
            // 日期别名键：解析端 modified/unpublishdate/pubdate/published 均会生效，
            // 不剔除则同一值既进别名字段又泄漏进 params，写回后持久重复
            "modified", "unpublishdate", "pubdate", "published",
            "tags", "categories", "layout", "slug", "aliases", "description",
            "summary", "weight", "author", "authors", "keywords", "type",
            "outputs", "menu", "cascade"
        };

        var result = new Dictionary<string, object>();
        foreach (var kvp in dict)
        {
            if (!standardKeys.Contains(kvp.Key))
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }
}
#pragma warning restore IL2026, IL3050
