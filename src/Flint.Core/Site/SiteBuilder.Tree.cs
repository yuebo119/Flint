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
        CancellationToken cancellationToken)
    {
        var contentPath = Path.Combine(sourcePath, "content");
        if (!Directory.Exists(contentPath))
        {
            return [];
        }

        // 优化：先收集所有文件路径，然后并行读取
        var mdFiles = Directory.EnumerateFiles(contentPath, "*.md", SearchOption.AllDirectories)
            .ToArray();

        if (mdFiles.Length == 0)
        {
            return [];
        }

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
        var pages = new List<PageContext>(tree.Count);
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (_, node) =>
            {
                var nodeKind = KindOfNode(node);
                var (cascaded, dataChain) = MergeAncestorCascades(node.Key, nodeKind, cascades);
                pages.Add(NodeToPageContext(node, config, cascaded, dataChain));
                return false;
            }
        });

        // 保持既有输出契约：日期降序
        return pages
            .OrderByDescending(p => p.Date)
            .ToList();
    }

    /// <summary>节点 → Hugo kind 语义字符串（cascade _target 的 kind 过滤依据）</summary>
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
            return CreatePageContext(node.Content, config, cascadedParams, cascadeDataChain);
        }

        var kind = node.Key.Length == 0 ? "home" : "section";
        var relPermalink = node.Key.Length == 0 ? "/" : node.Key + "/";

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
            Permalink = config.BaseURL.TrimEnd('/') + relPermalink,
            RelPermalink = relPermalink,
            Date = DateTimeOffset.Now,
            Tags = [],
            Categories = [],
            WordCount = 0,
            ReadingTime = TimeSpan.Zero,
            Type = kind,
            Draft = draft,
            Params = cascadedParams
        };
    }

    private PageContext CreatePageContext(
        ParsedContent content,
        SiteConfig config,
        IReadOnlyDictionary<string, object>? cascadedParams = null,
        List<FrontMatter>? cascadeDataChain = null)
    {
        var permalink = PermalinkEngine.GeneratePermalink(content, config);
        var relPermalink = permalink.StartsWith(config.BaseURL)
            ? permalink[config.BaseURL.Length..]
            : permalink;

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

        return new PageContext
        {
            Title = title,
            Content = content.HtmlContent,
            Permalink = config.BaseURL.TrimEnd('/') + relPermalink,
            RelPermalink = relPermalink,
            Date = content.Metadata.Date ?? DateTimeOffset.Now,
            LastMod = content.Metadata.LastMod,
            Tags = content.Metadata.Tags,
            Categories = content.Metadata.Categories,
            WordCount = content.WordCount,
            ReadingTime = content.ReadingTime,
            Description = description,
            Summary = content.Summary,
            Type = content.Metadata.Type ?? "page",
            Layout = layout,
            Outputs = content.Metadata.Outputs,
            SourcePath = content.SourcePath,
            Draft = draft,
            Weight = weight,
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
            RawContent = content.RawMarkdown
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
