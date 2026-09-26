// Flint 静态站点生成器
// SiteBuilder 树装配聚合：内容扫描/解析/过滤、页面树装配、cascade 合并、PageContext 构建

using System.Collections.Concurrent;
using System.Threading.Channels;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Models;
using PageBundleType = Flint.Core.Site.PageTrees.PageBundleType;
using PageTree = Flint.Core.Site.PageTrees.PageTree;
using PageTreeNode = Flint.Core.Site.PageTrees.PageTreeNode;
using PageTreeWalker = Flint.Core.Site.PageTrees.PageTreeWalker;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器（partial）：树装配 + cascade + PageContext 构建聚合
/// </summary>
public sealed partial class SiteBuilder
{
    private async Task<List<ContentFile>> ScanContentFilesAsync(
        string sourcePath,
        IReadOnlyList<string>? themeNames,
        CancellationToken cancellationToken)
    {
        var contentPath = Path.Combine(sourcePath, "content");

        // 内容收集（A2 主题兼容）：站点 content 优先，主题 content 按序补缺——
        // 键 = content/ 下的相对路径，站点已有的路径不被主题覆盖（对齐 Hugo 主题语义）
        var filesByRelative = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectContentFiles(contentPath, filesByRelative);
        if (themeNames is not null)
        {
            foreach (var themeName in themeNames)
            {
                CollectContentFiles(
                    Path.Combine(sourcePath, "themes", themeName, "content"), filesByRelative);
            }
        }

        if (filesByRelative.Count == 0)
        {
            return [];
        }

        var mdFiles = filesByRelative.Values.ToArray();

        // 预分配数组，避免 List 扩容
        var files = new ContentFile[mdFiles.Length];

        // 并行读取文件内容
        await Parallel.ForEachAsync(
            Enumerable.Range(0, mdFiles.Length),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            },
            async (i, ct) =>
            {
                var filePath = mdFiles[i];
                var content = await File.ReadAllBytesAsync(filePath, ct);
                files[i] = new ContentFile
                {
                    Path = filePath,
                    RawContent = content,
                    ModifiedTime = File.GetLastWriteTimeUtc(filePath)
                };
            });

        return [.. files];
    }

    /// <summary>
    /// 收集 content 目录下的 Markdown 到「相对路径 → 物理路径」字典（A2）；
    /// TryAdd 先到先得形成站点覆盖主题的优先级链
    /// </summary>
    private static void CollectContentFiles(
        string contentDir, Dictionary<string, string> filesByRelative)
    {
        if (!Directory.Exists(contentDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(contentDir, "*.md", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(contentDir, file).Replace('\\', '/');
            filesByRelative.TryAdd(rel, file);
        }
    }

    private async Task<List<ParsedContent>> ParseContentsAsync(
        List<ContentFile> contentFiles,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        // 优化：预分配结果集合容量
        var results = new ConcurrentBag<ParsedContent>();

        // P3 优化：只在启用缓存时使用内容哈希缓存
        var filesToParse = new List<ContentFile>();
        if (_contentCache != null)
        {
            foreach (var file in contentFiles)
            {
                if (_contentCache.TryGetCached(file, out var cached) && cached != null)
                {
                    results.Add(cached);
                }
                else
                {
                    filesToParse.Add(file);
                }
            }
        }
        else
        {
            filesToParse = contentFiles;
        }

        // 如果所有文件都命中缓存，直接返回
        if (filesToParse.Count == 0)
        {
            return [.. results];
        }

        // 优化：对于小批量直接并行处理，避免 Channel 开销
        if (filesToParse.Count <= options.Parallelism * 4)
        {
            await Parallel.ForEachAsync(
                filesToParse,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.Parallelism,
                    CancellationToken = cancellationToken
                },
                async (file, ct) =>
                {
                    try
                    {
                        var parsed = await _contentParser.ParseAsync(file, ct);
                        results.Add(parsed);
                        // 更新缓存（仅在启用时）
                        _contentCache?.Set(file, parsed);
                    }
                    // 取消必须逃逸：否则 ForEachAsync 感知不到取消，
                    // 会把剩余文件逐个记成假解析错误
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(new BuildError
                        {
                            FilePath = file.Path,
                            Line = 0,
                            Column = 0,
                            Message = ex.Message,
                            ErrorCode = "PARSE001"
                        });
                    }
                });

            return [.. results];
        }

        // 大批量使用 Channel 进行流式处理
        var channel = Channel.CreateBounded<ContentFile>(
            new BoundedChannelOptions(options.Parallelism * 2)
            {
                FullMode = BoundedChannelFullMode.Wait
            });

        var channelResults = new ConcurrentBag<ParsedContent>();

        // 生产者：将文件放入 Channel（只放入需要解析的文件）
        var producer = Task.Run(async () =>
        {
            foreach (var file in filesToParse)
            {
                await channel.Writer.WriteAsync(file, cancellationToken);
            }
            channel.Writer.Complete();
        }, cancellationToken);

        // 消费者：并行解析文件
        var consumers = Enumerable.Range(0, options.Parallelism)
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var file in channel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        var parsed = await _contentParser.ParseAsync(file, cancellationToken);
                        channelResults.Add(parsed);
                        // 更新缓存（仅在启用时）
                        _contentCache?.Set(file, parsed);
                    }
                    // 取消必须逃逸（同小批量分支）
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(new BuildError
                        {
                            FilePath = file.Path,
                            Line = 0,
                            Column = 0,
                            Message = ex.Message,
                            ErrorCode = "PARSE001"
                        });
                    }
                }
            }, cancellationToken))
            .ToArray();

        await producer;
        await Task.WhenAll(consumers);

        // 合并缓存命中的结果和新解析的结果
        foreach (var item in channelResults)
        {
            results.Add(item);
        }

        return [.. results];
    }

    private List<ParsedContent> FilterContents(
        List<ParsedContent> contents,
        BuildOptions options)
    {
        var now = DateTimeOffset.Now;

        var filtered = contents.Where(c =>
        {
            // 过滤草稿
            if (c.Metadata.Draft && !options.IncludeDrafts)
            {
                return false;
            }

            // 过滤未来内容
            if (!options.IncludeFuture)
            {
                var publishDate = c.Metadata.PublishDate ?? c.Metadata.Date;
                if (publishDate.HasValue && publishDate.Value > now)
                {
                    return false;
                }
            }

            // 过滤过期内容（考虑 IncludeExpired 选项）
            if (!options.IncludeExpired && c.Metadata.ExpiryDate.HasValue && c.Metadata.ExpiryDate.Value < now)
            {
                return false;
            }

            return true;
        }).ToList();

        return filtered;
    }

    private List<PageContext> BuildPageContexts(
        List<ParsedContent> contents,
        SiteConfig config,
        string sourcePath)
    {
        // 装配页面树（bundle 语义 + 缺失 section/home 合成补齐），再从树遍历产出页面上下文。
        // 树保留到构建周期：增量构建据此做 section 级精确替换，无需重新解析全部文件
        var tree = AssemblePageTree(contents, sourcePath, config);
        _currentPageTree = tree;
        // 全量重装：签名条目缓存对应的旧节点内容已全部作废
        _taxonomyEntryCache.Clear();

        return BuildPageContextsFromTree(tree, config);
    }

    /// <summary>从（最新状态的）页面树遍历产出全部页面上下文：cascade 合并 + 日期降序契约</summary>
    private List<PageContext> BuildPageContextsFromTree(PageTree tree, SiteConfig config)
    {
        // cascade 索引：携带 cascade 的 branch/synthesized 节点 → 完整级联配置
        //（Data 字段 + _target 的 path/kind 过滤）
        var cascades = CollectCascades(tree);
        var entries = new List<(string Key, PageContext Page)>(tree.Count);
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (key, node) =>
            {
                // Hugo 语义：**嵌套**目录缺 _index.md 时不生成 section 页。探测实证
                // （v0.166）：`content/a/x.md`（顶层 a 无 _index.md）→ 产出 /a/ ✔；
                // `content/b/sub/y.md`（sub 无 _index.md，且 b 是 section）→ **不产出**
                // /b/sub/ ✔；`content/b/sub2/_index.md` → 产出 /b/sub2/ ✔。
                // 判据：合成节点（无 _index.md 补齐）且 key 含 '/'（嵌套）。
                // 节点仍留在树里供 .Parent/.CurrentSection/级联查找使用，只是不产页
                if (node.BundleType == PageBundleType.Synthesized &&
                    key.Contains('/', StringComparison.Ordinal))
                {
                    return false;
                }

                var nodeKind = KindOfNode(node);
                var (cascaded, dataChain) = MergeAncestorCascades(key, nodeKind, cascades);
                entries.Add((key, NodeToPageContext(node, config, cascaded, dataChain)));
                return false;
            }
        });

        // 保持既有输出契约：日期降序
        var ordered = entries
            .OrderByDescending(e => e.Page.Date)
            .ToList();

        // C1 分页输入：列表页装配 page.Pages——home 为全部常规页，
        // section 为其下全部常规页（descendant，等价 Hugo .RegularPages；
        // 直接子级为主的站点与 Hugo .Pages 结果一致）
        var regularByKey = ordered
            .Where(e => e.Page.Kind is not ("home" or "section"))
            .ToList();
        var regular = regularByKey.Select(e => e.Page).ToList();

        var result = new List<PageContext>(ordered.Count);
        // section 页集合（Hugo 的 .Sections）：与 .Pages 同阶段装配，随页面对象流动。
        // 主题按层级遍历站点结构（techdoc 的 open-menu 用 site.home.sections.by_weight）
        // `.Sections` 只暴露**会真正产出页面**的 section：嵌套且无 _index.md 的合成节点
        // 不产页（见 Handle 的门禁），若仍出现在 `.Sections`/`.Pages` 里，主题的导航遍历会
        // 生成指向不存在目录的链接（narrow 的 /docs/guide/ 实测——content/docs/guide/ 无
        // _index.md，Hugo 也不产 /docs/guide/）。BundleType 判定与产页门禁同源
        var allSections = ordered
            .Where(e => e.Page.Kind == "section" && !IsNonProducingSynthesized(e.Key, e.Page))
            .ToList();

        // **两阶段装配：先深后浅**。单遍装配时父节点拿到的子 section 是**尚未装配
        // Pages 的旧对象**（实测 `site.home.sections[*].pages` 恒为空、而 `site.pages`
        // 里同名 section 有 3 条）——主题按 `.Sections`/`.Pages` 递归遍历站点结构
        //（techdoc 的 pagination 走 prev/next 导航树）就会走出错误结构，甚至触发
        // 递归深度上限（"partial 嵌套深度超过 200"）。故先按**层级降序**装配，
        // 让每个 section 装配时能从表里取到已装配好的子 section；再按原顺序产出时
        // 从表里取（顺序语义不变：`ordered` 仍是日期降序）
        var assembled = new Dictionary<string, PageContext>(StringComparer.Ordinal);
        foreach (var (key, page) in ordered.OrderByDescending(e => DepthOfKey(e.Key)))
        {
            if (page.Kind == "home")
            {
                // Hugo 语义：home 的 .Pages 是**顶层子页 + 顶层 section**（不含深层页面）。
                // 实测（Hugo v0.166）：content/docs/guide/getting-started.md 不进 home.Pages，
                // 故 home 的分页页数也据此（此前用全部常规页 → 多出 /page/2/、/page/3/）
                // **顶层判定要先去前导斜杠**：树节点的 key 形态是 `/posts`、`/about`
                //（section 分支的 `key + "/"` 前缀匹配正是基于这个形态）。
                // 此前写 `!e.Key.Contains('/')` → 带前导斜杠的一级节点全被排除，
                // 首页的 `.Pages` / `.Sections` **恒为空**（实测 m10c/papermod 等站点
                // `page.pages | len` = 0、Hugo 为 3）——凡首页用 `.Pages` 列文章的主题
                // 都会缺整个列表区。修法：`TrimStart('/')` 后再判层级
                var homeChildren = ordered
                    .Where(e => e.Page.Kind is "page" or "section" && IsTopLevelKey(e.Key) &&
                                !IsNonProducingSynthesized(e.Key, e.Page))
                    .Select(e => assembled.TryGetValue(e.Key, out var assembledChild) ? assembledChild : e.Page)
                    .ToList();
                // home 的直属 section 即一级 section 页（路径无 '/'）
                var homeSections = allSections
                    .Where(e => IsTopLevelKey(e.Key))
                    .Select(e => assembled.TryGetValue(e.Key, out var s) ? s : e.Page)
                    .ToList();
                assembled[key] = WithDerivedListDates(
                    page.WithPages(homeChildren).WithSections(homeSections), homeChildren, homeSections);
            }
            else if (page.Kind == "section")
            {
                var prefix = key + "/";
                var sectionPages = regularByKey
                    .Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(e => e.Page)
                    .ToList();
                // 直属子 section：前缀匹配且层数恰好 +1（深层 section 不属于本页）
                var childSections = allSections
                    .Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                                e.Key.Count(c => c == '/') == key.Count(c => c == '/') + 1)
                    .Select(e => assembled.TryGetValue(e.Key, out var s) ? s : e.Page)
                    .ToList();
                var assembledSection = page.WithPages(sectionPages).WithSections(childSections);
                assembled[key] = WithDerivedListDates(assembledSection, sectionPages, childSections);
            }
            else
            {
                assembled[key] = page;
            }
        }

        // 按原顺序产出（`ordered` 的日期降序语义不变）
        foreach (var (key, page) in ordered)
        {
            result.Add(assembled.TryGetValue(key, out var assembledPage) ? assembledPage : page);
        }

        return result;
    }

    /// <summary>节点 → Hugo kind 语义字符串（cascade _target 的 kind 过滤依据）</summary>
    /// <summary>
    /// leaf bundle 资源装配（B5）：index.md 同目录下的非 Markdown 文件归页面所有
    /// （Hugo Page Resources 语义）。返回 {name, path, rel_permalink, media_type, size} 字典列表；
    /// 资源同时经 assets 管线发布，模板引用其 rel_permalink 即可
    /// </summary>
    private static IReadOnlyList<object> LoadBundleResources(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)
            || !Path.GetFileName(sourcePath).Equals("index.md", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var bundleDir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrEmpty(bundleDir) || !Directory.Exists(bundleDir))
        {
            return [];
        }

        // bundle 目录的站点相对路径（用于推导资源 URL）
        var normalizedDir = bundleDir.Replace('\\', '/');
        var contentIdx = normalizedDir.LastIndexOf("/content/", StringComparison.OrdinalIgnoreCase);
        var dirRel = contentIdx >= 0
            ? normalizedDir[(contentIdx + "/content/".Length)..]
            : "";

        var resources = new List<object>();
        foreach (var file in Directory.EnumerateFiles(bundleDir, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue; // Markdown 是页面本体，不是资源
            }
            var info = new FileInfo(file);
            resources.Add(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["name"] = name,
                ["title"] = Path.GetFileNameWithoutExtension(name),
                ["path"] = dirRel.Length > 0 ? $"/{dirRel}/{name}" : $"/{name}",
                ["rel_permalink"] = dirRel.Length > 0 ? $"/{dirRel}/{name}" : $"/{name}",
                ["media_type"] = GetMediaTypeByName(name),
                ["size"] = info.Length,
                ["resource_type"] = "page"
            });
        }
        return resources;
    }

    private static string GetMediaTypeByName(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }

    private static string KindOfNode(PageTreeNode node)
    {
        if (node.Key.Length == 0)
        {
            return "home";
        }
        return node.IsBranch ? "section" : "page";
    }

    /// <summary>
    /// cascade _target 的 path 匹配（简化 Hugo glob 语义，边界已声明）：
    /// null/空 或 "**" = 匹配全部；"/**" 结尾 = 路径前缀；含 "*" 的单段通配 =
    /// 段数一致且非通配段前缀匹配；否则 = 精确或子路径（/posts 匹配 /posts/x）
    /// </summary>
    internal static bool CascadeTargetMatchesPath(string? target, string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(target) || target == "**")
        {
            return true;
        }

        var normalized = target.Trim().TrimEnd('/').ToLowerInvariant();
        if (normalized.Length == 0 || normalized == "**")
        {
            return true;
        }

        if (normalized.EndsWith("/**", StringComparison.Ordinal))
        {
            var prefix = normalized[..^3];
            return nodeKey.StartsWith(prefix, StringComparison.Ordinal);
        }

        if (normalized.Contains('*', StringComparison.Ordinal))
        {
            // 段级通配：段数一致且逐段匹配（* = 任意单段内容）
            var targetSegs = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var nodeSegs = nodeKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (targetSegs.Length != nodeSegs.Length)
            {
                return false;
            }
            for (var i = 0; i < targetSegs.Length; i++)
            {
                if (targetSegs[i] != "*" &&
                    !nodeSegs[i].Equals(targetSegs[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        return nodeKey == normalized || nodeKey.StartsWith(normalized + "/", StringComparison.Ordinal);
    }

    /// <summary>
    /// 收集树中携带 cascade 的节点（key → 完整级联配置，含 Data 字段与 _target）
    /// </summary>
    private static Dictionary<string, CascadeConfig> CollectCascades(PageTree tree)
    {
        var result = new Dictionary<string, CascadeConfig>(StringComparer.Ordinal);
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (_, node) =>
            {
                var cascade = node.Content?.Metadata.Cascade;
                if (cascade is not null && (cascade.Data is not null || !string.IsNullOrEmpty(cascade.Target) || !string.IsNullOrEmpty(cascade.Kind)))
                {
                    result[node.Key] = cascade;
                }
                return false;
            }
        });
        return result;
    }

    /// <summary>
    /// 合并 key 全部祖先的 cascade：home → 逐级到直接父级（近者覆盖远者）。
    /// 每条 cascade 先过 _target 的 path/kind 过滤再合入。
    /// 返回：①合并后的 Params（页面显式值由调用方最后覆盖）②近→远的 Data 链
    ///（供非 Params 字段的先到先得兜底）
    /// </summary>
    private static (IReadOnlyDictionary<string, object> Params, List<FrontMatter> DataChain)
        MergeAncestorCascades(
            string key,
            string nodeKind,
            Dictionary<string, CascadeConfig> cascades)
    {
        var mergedParams = new Dictionary<string, object>(StringComparer.Ordinal);
        var dataChain = new List<FrontMatter>();
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var segment in segments) // 逐级祖先："" → /a → /a/b（不含 key 自身）
        {
            // 祖先自身 key 对应的 cascade 不作用于自己（Hugo：cascade 面向后代）
            if (cascades.TryGetValue(current, out var layer) &&
                CascadeTargetMatchesPath(layer.Target, key) &&
                KindMatches(layer.Kind, nodeKind))
            {
                if (layer.Data is not null)
                {
                    foreach (var (k, v) in layer.Data.Params)
                    {
                        mergedParams[k] = v; // 近层覆盖远层
                    }
                    dataChain.Add(layer.Data); // 近→远，供字段先到先得
                }
            }
            current = current.Length == 0 ? "/" + segment : current + "/" + segment;
        }

        return (mergedParams, dataChain);
    }

    private static bool KindMatches(string? cascadeKind, string nodeKind)
    {
        return string.IsNullOrWhiteSpace(cascadeKind) ||
               cascadeKind.Equals(nodeKind, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>内容文件绝对路径 → 树 key（与装配时的推导规则一致）</summary>
    private static string ContentKeyFor(string sourcePath, SiteConfig config, string absoluteFilePath)    {
        var contentDir = Path.Combine(sourcePath,
            string.IsNullOrEmpty(config.ContentDir) ? "content" : config.ContentDir);
        var relative = Path.GetRelativePath(contentDir, absoluteFilePath);
        return PageTrees.PageTree.KeyForContentFile(relative).Key;
    }

    /// <summary>
    /// 将解析结果装配为页面树：key 由内容文件相对路径推导（bundle 语义），
    /// 缺失的 section/home 以合成节点补齐（无文件来源，模板可见）
    /// </summary>
    private static PageTree AssemblePageTree(
        List<ParsedContent> contents,
        string sourcePath,
        SiteConfig config)
    {
        var tree = new PageTree();
        var contentDir = Path.Combine(sourcePath,
            string.IsNullOrEmpty(config.ContentDir) ? "content" : config.ContentDir);

        foreach (var content in contents)
        {
            var relative = Path.GetRelativePath(contentDir, content.SourcePath);
            var (key, bundleType) = PageTrees.PageTree.KeyForContentFile(relative);
            tree.Insert(new PageTreeNode
            {
                Key = key,
                BundleType = bundleType,
                SourcePath = content.SourcePath,
                Content = content
            });
        }

        // 补缺：确保每个节点的祖先链与 home 存在（合成 section/home）
        var keys = new List<string>(tree.Count);
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (key, _) =>
            {
                keys.Add(key);
                return false;
            }
        });

        foreach (var key in keys)
        {
            EnsureAncestors(tree, key);
        }

        if (!tree.Has(""))
        {
            tree.Insert(new PageTreeNode
            {
                Key = "",
                BundleType = PageBundleType.Synthesized,
                SourcePath = "",
                Title = config.Title
            });
        }


        return tree;
    }

    /// <summary>确保 key 的全部祖先（不含自身）在树中存在，缺失则插入合成 section</summary>
    private static void EnsureAncestors(PageTree tree, string key)
    {
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        for (var i = 0; i < segments.Length - 1; i++)
        {
            current = current.Length == 0 ? "/" + segments[i] : current + "/" + segments[i];
            if (tree.Has(current))
            {
                continue;
            }

            tree.Insert(new PageTreeNode
            {
                Key = current,
                BundleType = PageBundleType.Synthesized,
                SourcePath = "",
                Title = segments[i]
            });
        }
    }

    /// <summary>树节点 → 页面上下文：真实节点走 CreatePageContext，合成节点手建（Content 为空）</summary>
    private PageContext NodeToPageContext(
        PageTreeNode node,
        SiteConfig config,
        IReadOnlyDictionary<string, object> cascadedParams,
        List<FrontMatter>? cascadeDataChain = null)
    {
        if (node.Content is not null)
        {
            return CreatePageContext(node.Content, config, cascadedParams, cascadeDataChain, KindOfNode(node));
        }

        var kind = node.Key.Length == 0 ? "home" : "section";
        // **baseURL 子路径前缀**（Hugo 语义：relURL/RelPermalink 含 baseURL 路径）——
        // 多主题站按子路径归并单一端口时（/fixit/、/stack/…），链接必须落在各自
        // 主题目录下。根 baseURL（历史默认）前缀为空串，输出不变
        var relPermalink = Templates.TemplateResource.BasePathOf(config.BaseURL) +
            (node.Key.Length == 0 ? "/" : node.Key + "/");

        // 合成节点的 cascade 字段兜底（近→远先到先得；合成节点无自身 front matter）
        string? title = node.Title ?? node.Key;
        bool draft = false;
        if (cascadeDataChain is { Count: > 0 })
        {
            foreach (var data in cascadeDataChain)
            {
                if (title == node.Title && !string.IsNullOrEmpty(data.Title) && data.Title != "Untitled")
                {
                    title = data.Title;
                }
                if (!draft && data.Draft)
                {
                    draft = true;
                }
            }
        }

        return new PageContext
        {
            Title = title,
            Content = "",
            Permalink = Templates.TemplateResource.OriginOf(config.BaseURL) + relPermalink,
            RelPermalink = relPermalink,
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 0,
            ReadingTime = TimeSpan.Zero,
            Type = kind,
            // 合成列表页（section/home）：kind 即节点类型，无显式 type 声明
            Kind = kind,
            Language = config.LanguageCode,
            Draft = draft,
            // node.Key 是**根相对**路径（/posts），不含 baseURL 子路径——直接用带
            // 前缀的 relPermalink 会把子路径名（主题名）当成段名：.Section/.Type
            // 全错，`where … "Type" "posts"` 过滤落空（loveit 首页空的根因）
            Section = SectionOfKind(node.Key),
            Params = cascadedParams
        };
    }

    /// <summary>
    /// 是否"**不产页**的合成节点"：嵌套目录缺 _index.md 时补齐的节点不产出页面
    /// （判据与 Handle 的产页门禁一致：合成节点 + key 含 '/'；合成节点的
    /// <c>SourcePath</c> 为空串，用它区分于真实内容页）。
    /// `.Sections`/`.Pages` 里若带上它，主题的导航遍历就会生成指向不存在目录的链接
    /// （narrow 的 <c>/docs/guide/</c> 实测——content/docs/guide/ 无 _index.md，Hugo 也不产该页）
    /// </summary>
    /// <summary>
    /// 列表页（home/section）的派生日期（Hugo v0.166 探针）：未显式设置时——
    /// <c>.Date</c> = 后代页面中**最大**的 date、<c>.Lastmod</c> = 后代中最大的 lastmod
    /// （子页自身未设 lastmod 时用其 date），没有后代则保持零值。
    /// 主题在列表页页脚显示 "Last updated on …"（techdoc）、排序与 sitemap lastmod 都依赖它；
    /// 此前列表页这两个字段恒空 → 渲染成 "Last updated on "（实测）
    /// </summary>
    private static PageContext WithDerivedListDates(
        PageContext listPage,
        IReadOnlyList<PageContext> pages,
        IReadOnlyList<PageContext> sections)
    {
        var descendants = pages.Concat(sections).ToList();
        if (descendants.Count == 0)
        {
            return listPage;
        }


        var date = listPage.Date != DateTimeOffset.MinValue
            ? listPage.Date
            : descendants.Where(d => d.Date != DateTimeOffset.MinValue)
                .Select(d => d.Date)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();
        var lastMod = listPage.LastMod
            ?? descendants.Select(d => d.LastMod ?? d.Date)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max();
        return listPage.WithDates(date, lastMod);
    }

    /// <summary>
    /// 树节点 key 的**层级**（按路径段数）：`"/"`（home）→ 0、`"/posts"` → 1、
    /// `"/posts/third"` → 2。**不能用斜杠计数**——home 的 key 是 `"/"`（1 个斜杠），
    /// 会与一级节点并列，导致 home 排在 `/docs`、`/posts` 之前被装配，派生日期聚合
    /// 读到尚未派生的子 section（techdoc 的 home 日期恒为零，实测）
    /// </summary>
    private static int DepthOfKey(string key) =>
        key.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsNonProducingSynthesized(string key, PageContext page) =>
        string.IsNullOrEmpty(page.SourcePath) && key.Contains('/', StringComparison.Ordinal);

    /// <summary>
    /// 是否**一级**树节点：key 形态为 <c>/posts</c>（带前导斜杠），
    /// 一级判定即"去掉前导斜杠后不再含 '/'"
    /// </summary>
    private static bool IsTopLevelKey(string key) =>
        !key.TrimStart('/').Contains('/', StringComparison.Ordinal);

    /// <summary>页面相对 URL → Hugo <c>.Section</c>（路径首段；home 为 "/" 时为空串）</summary>
    private static string SectionOfKind(string relPermalink)
    {
        var segments = relPermalink.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 ? segments[0] : "";
    }

    private PageContext CreatePageContext(
        ParsedContent content,
        SiteConfig config,
        IReadOnlyDictionary<string, object>? cascadedParams = null,
        List<FrontMatter>? cascadeDataChain = null,
        string? nodeKind = null)
    {
        var permalink = PermalinkEngine.GeneratePermalink(content, config);
        // permalink 恒为根相对路径；relPermalink 需拼上 baseURL 子路径前缀
        // （Hugo 语义——多主题站按子路径归并单一端口的前提）。此前此处按
        // StartsWith(config.BaseURL) 剥离整段 baseURL，而 GeneratePermalink 从不
        // 返回绝对 URL，子路径被静默丢弃
        var relPermalink = Templates.TemplateResource.BasePathOf(config.BaseURL) + permalink;

        // cascade 合并：祖先级联值先入，页面自身显式 Params 覆盖（对齐 Hugo 优先级）
        var ownParams = content.Metadata.Params;
        var effectiveParams = cascadedParams is { Count: > 0 }
            ? MergeParams(cascadedParams, ownParams)
            : ownParams;

        // cascade 字段兜底（近→远先到先得）：页面 front matter 为默认值时
        // 才取 cascade 值——页面显式值恒优先（对齐 Hugo 语义）
        var title = content.Metadata.Title;
        var description = content.Metadata.Description;
        var layout = content.Metadata.Layout;
        var weight = content.Metadata.Weight;
        var draft = content.Metadata.Draft;
        if (cascadeDataChain is { Count: > 0 })
        {
            foreach (var data in cascadeDataChain)
            {
                if (title == "Untitled" && !string.IsNullOrEmpty(data.Title))
                    title = data.Title;
                if (description is null && !string.IsNullOrEmpty(data.Description))
                    description = data.Description;
                if (layout is null && !string.IsNullOrEmpty(data.Layout))
                    layout = data.Layout;
                if (weight == 0 && data.Weight != 0)
                    weight = data.Weight;
                if (!draft && data.Draft)
                    draft = true;
            }
        }

        // permalink 是**根相对**形式（GeneratePermalink 的产出），relPermalink 才带
        // baseURL 子路径前缀——段名/Type 从根相对形式推导，否则子路径名（主题名）
        // 会被当成段名：.Section/.Type 全错，主题的 `where … "Type" "posts"` 过滤
        // 落空（loveit 首页只渲染 _index 正文、无文章列表的根因）
        var sectionName = SectionOfKind(permalink);

        return new PageContext
        {
            Title = title,
            Content = content.HtmlContent,
            Permalink = Templates.TemplateResource.OriginOf(config.BaseURL) + relPermalink,
            RelPermalink = relPermalink,
            // **无日期页用零值时间**（Hugo 语义）：Hugo 对无 front matter date 的页面给
            // 零值时间 0001-01-01T00:00:00Z，主题据此隐藏日期（`.Date.IsZero` 守卫）或
            // 直接渲染 `01 Jan, 0001`（bearblog 实测两侧输出）。此前回落 DateTimeOffset.Now
            // → 每个无日期页都显示"构建当天"，与 Hugo 差一整段（页脚/卡片/opengraph 都受影响）
            Date = content.Metadata.Date ?? DateTimeOffset.MinValue,
            // .Lastmod 缺省 = .Date（Hugo 语义：v0.166 探针——无 lastmod 的子页在列表页
            // 的 lastmod 聚合里贡献自己的 date）
            LastMod = content.Metadata.LastMod ?? content.Metadata.Date,
            Tags = content.Metadata.Tags,
            Categories = content.Metadata.Categories,
            Aliases = content.Metadata.Aliases,
            WordCount = content.WordCount,
            ReadingTime = content.ReadingTime,
            Description = description,
            Summary = content.Summary,
            ExplicitSummary = content.Metadata.Summary,
            // kind 由树节点 bundle 类型推导（前置：front matter 显式 type 覆盖）——
            // _index.md 归一为 branch 节点后必须产出 section 语义（list 模板、IsList），
            // 此前用 "page" 兜底致真实 section 页走 single 模板
            //
            // **.Type = section 名**（Hugo v0.166 实测，与 .Section 同值）：
            //   /                → page（无 section）
            //   /posts/          → posts（段页）
            //   /tags/、/tags/x/ → tags（taxonomy / term）
            //   /posts/a/        → posts（普通页取**顶层**段名）
            //   /docs/guide/g/   → docs（嵌套取顶层，不是 "guide"）
            //   /about/          → page（根级页无段名）
            //   front matter type: custom → custom（显式声明覆盖）
            // 主题按 `.Type` 过滤（`where … "Type" "in" site.Params.mainSections`）依赖它；
            // 此前用 kind 名（home/section/taxonomy），上述过滤全部落空
            Type = content.Metadata.Type ?? (sectionName.Length > 0 ? sectionName : "page"),
            // 模板查找链维度（A 组）：kind 来自树节点类型（用户 type 不覆盖它），
            // DeclaredType 只记 front matter 显式声明——两者分离后候选链才能既让
            // Ananke 的 type:page 命中 layouts/page/single.html，又不让普通页误命中
            Kind = nodeKind ?? "page",
            DeclaredType = content.Metadata.Type,
            Language = config.LanguageCode,
            Layout = layout,
            Outputs = content.Metadata.Outputs,
            SourcePath = content.SourcePath,
            Resources = LoadBundleResources(content.SourcePath),
            Draft = draft,
            Weight = weight,
            Section = SectionOfKind(relPermalink),
            MenuEntries = content.Metadata.Menus is { Count: > 0 } menus
                ? menus.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new MenuItemConfig
                    {
                        Name = kvp.Value.Name ?? title,
                        Weight = kvp.Value.Weight,
                        Parent = kvp.Value.Parent,
                        Identifier = kvp.Value.Identifier,
                        Pre = kvp.Value.Pre,
                        Post = kvp.Value.Post
                    })
                : null,
            Params = effectiveParams,
            Plain = content.PlainText,
            RawContent = content.RawMarkdown,
            // 目录 HTML（Hugo .TableOfContents 语义）+ 标题列表（.Fragments 数据源）：
            // 层级区间取 markup.tableOfContents 配置（默认 2..3），主题按字符串消费
            TableOfContents = Content.TocRenderer.Render(
                content.Headings,
                config.Markup.TableOfContents.StartLevel,
                config.Markup.TableOfContents.EndLevel,
                config.Markup.TableOfContents.Ordered),
            Headings = content.Headings
        };
    }

    /// <summary>合并参数字典：base 先入，override 覆盖同名键</summary>
    private static IReadOnlyDictionary<string, object> MergeParams(
        IReadOnlyDictionary<string, object> baseParams,
        IReadOnlyDictionary<string, object> overrideParams)
    {
        var merged = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (k, v) in baseParams)
        {
            merged[k] = v;
        }
        foreach (var (k, v) in overrideParams)
        {
            merged[k] = v;
        }
        return merged;
    }
}
