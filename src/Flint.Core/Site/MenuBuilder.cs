// Flint 静态站点生成器
// 菜单构建器

using Flint.Core.Abstractions;
using Flint.Core.Configuration;

namespace Flint.Core.Site;

/// <summary>
/// 菜单构建器
/// 构建多级嵌套菜单结构
/// </summary>
public sealed class MenuBuilder
{
    private readonly Dictionary<string, List<MenuItemConfig>> _menuConfigs = new();

    /// <summary>
    /// 添加菜单项配置
    /// </summary>
    public void AddMenuItem(string menuName, MenuItemConfig config)
    {
        if (!_menuConfigs.TryGetValue(menuName, out var items))
        {
            items = [];
            _menuConfigs[menuName] = items;
        }
        items.Add(config);
    }

    /// <summary>
    /// 从页面构建菜单
    /// </summary>
    public void AddFromPages(IEnumerable<PageContext> pages, Func<PageContext, IReadOnlyDictionary<string, MenuItemConfig>?> getMenuConfig)
    {
        foreach (var page in pages)
        {
            var menuConfigs = getMenuConfig(page);
            if (menuConfigs == null)
                continue;

            foreach (var (menuName, config) in menuConfigs)
            {
                var itemConfig = config with
                {
                    Name = config.Name ?? page.Title,
                    URL = config.URL ?? page.RelPermalink
                };
                AddMenuItem(menuName, itemConfig);
            }
        }
    }

    /// <summary>
    /// 构建菜单集合
    /// </summary>
    public MenuCollection Build(string? currentPath = null)
    {
        var menus = new Dictionary<string, IReadOnlyList<MenuItem>>();

        foreach (var (menuName, configs) in _menuConfigs)
        {
            var items = BuildMenuItems(configs, currentPath);
            menus[menuName] = items;
        }

        return new MenuCollection { Menus = menus };
    }

    private static List<MenuItem> BuildMenuItems(List<MenuItemConfig> configs, string? currentPath)
    {
        // 按权重排序
        var sorted = configs.OrderBy(c => c.Weight).ToList();

        // 构建层级结构
        var rootItems = new List<MenuItem>();
        var itemMap = new Dictionary<string, MenuItem>();

        // 第一遍：创建所有菜单项
        foreach (var config in sorted)
        {
            var item = new MenuItem
            {
                Name = config.Name ?? "",
                URL = config.URL ?? "",
                Weight = config.Weight,
                Identifier = config.Identifier,
                Parent = config.Parent,
                Pre = config.Pre,
                Post = config.Post,
                // URL 为 null/空时不得参与 StartsWith（空串前缀恒匹配使 IsActive 恒为 true）
                IsActive = currentPath != null && !string.IsNullOrEmpty(config.URL) &&
                          (config.URL == currentPath ||
                           (currentPath.StartsWith(config.URL, StringComparison.OrdinalIgnoreCase) && config.URL != "/"))
            };

            if (!string.IsNullOrEmpty(config.Identifier))
            {
                itemMap[config.Identifier] = item;
            }

            if (string.IsNullOrEmpty(config.Parent))
            {
                rootItems.Add(item);
            }
        }

        // 第二遍：按父项分组后自底向上装配。
        // 此前逐条 with 重建不回写 itemMap：同一父项的第二个子项会加到旧实例上
        // （前面的子项静默丢失），孙项因不在 rootItems 中被直接跳过
        var childrenByParent = sorted
            .Where(c => !string.IsNullOrEmpty(c.Parent))
            .GroupBy(c => c.Parent!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        // 配置错误（重复 Identifier 等）可使父子链成环——无防护的递归会
        // StackOverflow 且不可捕获，整个进程崩溃
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        MenuItem Resolve(MenuItem node)
        {
            if (node.Identifier is null ||
                !childrenByParent.TryGetValue(node.Identifier, out var kidConfigs))
            {
                return node;
            }

            if (!resolving.Add(node.Identifier))
            {
                // 已在解析栈中：环引用，保留现有子树不再下钻
                return node;
            }

            try
            {
                var children = new List<MenuItem>();
                foreach (var kidConfig in kidConfigs)
                {
                    if (kidConfig.Identifier is not null &&
                        itemMap.TryGetValue(kidConfig.Identifier, out var kid))
                    {
                        children.Add(Resolve(kid));
                    }
                }

                // MenuItem.Children 只读，with 重建并回写 itemMap（兄弟/父层引用拿到最新）
                var updated = node with { Children = children };
                itemMap[node.Identifier] = updated;
                return updated;
            }
            finally
            {
                resolving.Remove(node.Identifier);
            }
        }

        for (var i = 0; i < rootItems.Count; i++)
        {
            rootItems[i] = Resolve(rootItems[i]);
        }

        return rootItems;
    }
}

