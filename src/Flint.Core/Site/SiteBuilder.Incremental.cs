// Flint 静态站点生成器
// SiteBuilder 增量聚合：增量构建主流程、全局 identity 判定、taxonomy 签名

using System.Collections.Concurrent;
using System.Diagnostics;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.IO;
using Flint.Core.Models;
using Flint.Core.Templates;
using PageTree = Flint.Core.Site.PageTrees.PageTree;
using PageTreeNode = Flint.Core.Site.PageTrees.PageTreeNode;
using PageTreeWalker = Flint.Core.Site.PageTrees.PageTreeWalker;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器（partial）：增量构建聚合
/// </summary>
public sealed partial class SiteBuilder
{
    /// <inheritdoc />
    public async ValueTask<BuildResult> IncrementalBuildAsync(
        BuildOptions options,
        IReadOnlyList<string> changedFiles,
        CancellationToken cancellationToken = default)
    {
        // 增量构建：只处理变化的文件
        var stopwatch = Stopwatch.StartNew();
        var errors = new ConcurrentBag<BuildError>();
        var warnings = new ConcurrentBag<BuildWarning>();

        // 构建作用域：同 BuildAsync 入口（同步前缀开新实例，并行构建执行流隔离）
        BuildRenderStateAccess.BeginBuildScope();
        OutputDirectoryEnsurer.Reset();
        _writtenPaths.Clear();

        // 构建边界：同 BuildAsync，增量同样必须看到模板的最新状态
        //（"改模板后立即增量"是测试锁定的高频真实场景）
        _templateRenderer.InvalidateMtimeCache();

        // 阶段计时诊断：同 BuildAsync（SiteBuilder.cs），FLINT_TRACE_PHASES=1
        // 时向 stderr 输出增量路径各阶段耗时；默认关闭，仅一个 bool 判断的开销
        var tracePhases = Environment.GetEnvironmentVariable("FLINT_TRACE_PHASES") == "1";
        var lastPhaseTs = 0L;
        void Phase(string name)
        {
            if (!tracePhases) return;
            var ts = stopwatch.ElapsedTicks;
            Console.Error.WriteLine($"[phase] {name,-24} {(ts - lastPhaseTs) * 1000.0 / Stopwatch.Frequency,9:F1}ms");
            lastPhaseTs = ts;
        }

        try
        {
            // 配置加载（优先用预加载配置，避免重复 AutoLoadAsync——
            // 2026-10-21 性能优化，同 BuildAsync）
            var config = options.PreloadedConfig ?? await _configLoader.AutoLoadAsync(
                options.SourcePath, cancellationToken);

            // 依赖图扩展：模板/partial 变化 → 使用它们的页面（此前依赖图无人喂，改 layout 页面不会更新）
            var affectedFiles = _dependencyGraph.GetAffectedFiles(changedFiles).ToList();

            // 全局 identity 防御：config/data/archetypes 变化影响面广（配置重载/
            // 全站数据/原型模板），增量装配的渲染集合只覆盖变化页+列表页，
            // 全量语义只能由全量构建保证——直接转全量，静默忽略会让修改对产物毫无影响
            var forcesFullAssemble = changedFiles.Any(f => IsGlobalIdentityPath(f, options.SourcePath));
            if (forcesFullAssemble)
            {
                return await BuildAsync(options, cancellationToken);
            }

            var contentChanges = affectedFiles
                .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // 模板变化（T4.2）：layouts 下的文件从"资源"中分流，走受影响页重渲染而非全量重建。
            // **.html 与 .liquid 双形态**（FLUID-MIGRATION-PLAN P1-3 修复 2026-10-21）：
            // 此前只认 .html——改 .liquid partial 落入 assetChanges，依赖它的页面
            // 不重渲（增量构建漏重渲，Fluid 依赖追踪白做）
            var layoutsRoot = Path.GetFullPath(Path.Combine(
                options.SourcePath,
                string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir)) + Path.DirectorySeparatorChar;
            var templateChanges = affectedFiles
                .Where(f => (f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                             f.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)) &&
                            f.StartsWith(layoutsRoot, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var assetChanges = affectedFiles
                .Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                            !templateChanges.Contains(f, StringComparer.OrdinalIgnoreCase))
                .ToList();
            Phase("1.配置+变更分类");

            var pagesBuilt = 0;
            var assetsProcessed = 0;

            // 处理内容/模板变化：树上精确替换/删除/插入，全量 siteContext 从树产出——
            // 不再重新扫描解析全部文件（此前为重建 siteContext 每次增量都付全量解析成本）
            if (contentChanges.Count > 0 || templateChanges.Count > 0)
            {
                var tree = _currentPageTree;
                string taxonomySignatureBefore;
                if (tree is null)
                {
                    // 兜底：无先前全量构建（如直接调用增量 API）——先全量装配。
                    // 装配后的树已含本次变更，"更新前旧签名"无从取起（前后同树恒等、
                    // 比较永假），以空哨兵强制 taxonomy 页重产
                    var allFiles = await ScanContentFilesAsync(options.SourcePath, config.ThemeNames, cancellationToken);
                    var allParsed = await ParseContentsAsync(allFiles, options, errors, cancellationToken);
                    tree = AssemblePageTree(FilterContents(allParsed, options), options.SourcePath, config);
                    _currentPageTree = tree;
                    // 树整体重装：旧缓存条目对应的节点内容已不可信
                    _taxonomyEntryCache.Clear();
                    taxonomySignatureBefore = string.Empty;
                }
                else
                {
                    taxonomySignatureBefore = ComputeTaxonomySignature(tree);
                }

                // 1. 变化文件逐个应用到树（新增/更新 → 原位替换；删除 → 摘除节点）
                // File.Exists 分桶与读取之间存在 TOCTOU 窗口（编辑器原子替换），
                // 读取时消失的文件按删除处理，不让单个文件放大为整个增量失败
                var deletedChanges = contentChanges.Where(f => !File.Exists(f)).ToList();
                var changedFilesList = new List<ContentFile>();
                foreach (var path in contentChanges.Where(File.Exists))
                {
                    try
                    {
                        changedFilesList.Add(new ContentFile
                        {
                            Path = path,
                            RawContent = File.ReadAllBytes(path),
                            ModifiedTime = File.GetLastWriteTimeUtc(path)
                        });
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                    {
                        deletedChanges.Add(path);
                    }
                }

                var parsedChanges = await ParseContentsAsync(
                    changedFilesList, options, errors, cancellationToken);
                var filteredChanges = FilterContents(parsedChanges, options);

                foreach (var parsed in filteredChanges)
                {
                    var key = ContentKeyFor(options.SourcePath, config, parsed.SourcePath);
                    var existing = tree.Get(key);
                    if (existing is not null)
                    {
                        existing.Content = parsed;
                    }
                    else
                    {
                        var (newKey, newBundleType) = PageTrees.PageTree.KeyForContentFile(
                            Path.GetRelativePath(
                                Path.Combine(options.SourcePath,
                                    string.IsNullOrEmpty(config.ContentDir) ? "content" : config.ContentDir),
                                parsed.SourcePath));
                        tree.Insert(new PageTreeNode
                        {
                            Key = key,
                            BundleType = newBundleType,
                            SourcePath = parsed.SourcePath,
                            Content = parsed
                        });
                        EnsureAncestors(tree, key);
                    }
                    // 节点内容已变：签名条目缓存失效，下次全站签名时按新内容重算
                    _taxonomyEntryCache.Remove(key);
                }

                foreach (var deleted in deletedChanges)
                {
                    // 陈旧输出按被删节点生成时的 permalink 定位（URL 扁平规则与树 key 不同构），
                    // 必须在摘除节点前取 permalink
                    var key = ContentKeyFor(options.SourcePath, config, deleted);
                    var oldNode = tree.Get(key);
                    string? staleOutput = null;
                    if (oldNode?.Content is not null)
                    {
                        var oldRelPermalink = PermalinkEngine.GeneratePermalink(oldNode.Content, config);
                        staleOutput = GetOutputPath(oldRelPermalink, options.OutputPath);
                    }
                    tree.Delete(key);
                    // 节点已摘除：签名条目缓存同步失效
                    _taxonomyEntryCache.Remove(key);

                    if (staleOutput is null)
                    {
                        continue;
                    }
                    if (File.Exists(staleOutput))
                    {
                        File.Delete(staleOutput);
                    }
                    // 清理因此变空的目录链（只删 index.html 会留下目录壳，部署产物含无效空目录）
                    PruneEmptyDirectories(staleOutput, options.OutputPath);
                }

                // 2. 全量上下文从树产出（零重解析）
                var allPageContexts = BuildPageContextsFromTree(tree, config);
                var taxonomySignatureAfter = ComputeTaxonomySignature(tree);
                // taxonomy identity：签名一致（纯正文编辑）时 term/taxonomy 页
                // 内容不变，仅模板变化时才需重渲染
                var taxonomyDataChanged = taxonomySignatureBefore != taxonomySignatureAfter;
                var taxonomyService = new TaxonomyService(config.BaseURL, TaxonomyService.FromConfigured(config.Taxonomies));
                var taxonomies = taxonomyService.BuildTaxonomies(allPageContexts);
                // data identity 已在 IsGlobalIdentityPath 归为全量装配——此处 data 变化
                // 必然伴随全量重建，重新加载保证 site.data 反映最新内容
                var siteData = await LoadSiteDataAsync(options, config.ThemeNames, cancellationToken);
                var translations = Translations.Load(
                    options.SourcePath, config.ThemeNames, config.LanguageCode);
                // 签名传入 BuildSiteContext：签名一致（纯正文编辑）时 lookup 投影
                // 整体复用上次构建结果（#12），省全词条遍历与投影重建
                var siteContext = BuildSiteContext(config, allPageContexts, taxonomies, siteData, translations, taxonomySignatureAfter);// 3. 渲染集合 = 变化页自身 + 全部 section/home 列表页
                //    （列表页聚合"最新内容"，任何内容变化都可能影响；数量 = section 数，远小于页数。
                //     Hugo 以运行时依赖追踪精确到页，此处为无追踪前提下的保守折中）
                var changedPathSet = new HashSet<string>(
                    affectedFiles, StringComparer.OrdinalIgnoreCase);
                var renderSet = new List<PageContext>();
                foreach (var context in allPageContexts)
                {
                    if (context.Kind is "section" or "home" ||
                        (context.SourcePath is not null &&
                         changedPathSet.Contains(context.SourcePath)))
                    {
                    renderSet.Add(context);
                }
                }

                // fast render mode（T4.4）：模板变化时受影响内容页可能很多（如改 single 模板影响全部页），
                // 只重渲染浏览器正在访问的 URL 对应页面；列表页数量小不裁剪。
                // 访问集为空（浏览器不在站点内）时不过滤，保持保守完整增量。
                // 豁免集合必须是真实内容变化文件（changedFiles 原始参数过滤 .md）——
                // contentChanges/changedPathSet 都派生自 affectedFiles（依赖图传播后，
                // 改模板时全部依赖页都被传播进来），用它豁免=废除 fast render
                if (templateChanges.Count > 0 &&
                    options.PreferredUrls is { } preferred && preferred.Count > 0)
                {
                    var contentChangedSet = new HashSet<string>(
                        changedFiles.Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)),
                        StringComparer.OrdinalIgnoreCase);
                    var fastRenderSet = renderSet
                        .Where(c => c.Kind is "section" or "home" ||
                                    MatchesPreferredUrl(c, preferred) ||
                                    // 内容变化页始终渲染：其 URL 浏览器可能从未访问过，
                                    // 不豁免会导致 HTML 旧而 taxonomy/sitemap 新的不一致
                                    (c.SourcePath is not null && contentChangedSet.Contains(c.SourcePath)))
                        .ToList();
                    if (fastRenderSet.Count > 0)
                    {
                        renderSet = fastRenderSet;
                    }
                }

                // identity 分离（layout identity vs content identity）：
                // 只有真实内容文件（.md）变化才影响页面集与聚合产物——
                // sitemap/feeds 的 URL 集与 lastmod 均来自内容，仅模板变化时
                // 重产是纯浪费。判定必须基于 changedFiles 原始参数而非
                // affectedFiles（后者经依赖图传播，改模板时全部依赖页都被卷入）
                var hasContentChanges = changedFiles.Any(
                    f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
                Phase("2.树更新+装配");

                var renderedPages = await RenderPagesAsync(
                    renderSet, siteContext, config, options, errors, cancellationToken);

                // home 页兜底重渲（FLUID-MIGRATION-PLAN P1-3 补缺 2026-10-21）：
                // 合成 home 节点的键含 '/' 被树 walk 跳过（不在 allPageContexts），
                // 全量构建经 8.5 阶段 GenerateHomePageAsync 兜底产出；增量路径
                // 此前只在模板变化时调它——纯内容变化（新增/编辑文章）时首页
                // 列表不更新，与同一增量里 sitemap/feed 按 hasContentChanges
                // 重产矛盾（sitemap 有新文章而首页列表仍旧）。
                // **内容变化也触发**（2026-10-21 全面审计修复）：首页列表聚合
                // "最新内容"，任何内容变化都可能影响首页外观。
                // 树版 home（有 _index.md 的站点）已在 renderSet 渲染过，此处互斥
                if ((templateChanges.Count > 0 || hasContentChanges) &&
                    !allPageContexts.Any(p => p.Kind == "home"))
                {
                    var homePage = await GenerateHomePageAsync(
                        siteContext, config, options, errors, cancellationToken);
                    if (homePage is not null)
                    {
                        renderedPages.Add(homePage);
                    }
                }

                // 分类/词条页聚合全部内容的 tags/categories——tags/categories 增删改
                // 使其陈旧必须重产；纯正文编辑（taxonomy 签名一致）跳过重产。
                // 模板变化时也重渲染（term/taxonomy 页外观由模板决定）
                var taxonomyPagesIncremental = templateChanges.Count > 0 || taxonomyDataChanged
                    ? await GenerateTaxonomyPagesAsync(
                        taxonomies, siteContext, config, options, errors, cancellationToken)
                        : [];
                Phase("3.渲染+分类页");

                await WriteOutputAsync(renderedPages, taxonomyPagesIncremental, [], options, cancellationToken);

                // sitemap/rss/atom 是全局聚合产物，内容变化后必须重生成
                // （此前增量路径完全跳过，删除内容后 sitemap 持久保留死链）；
                // 仅模板变化时页面集与 lastmod 均未变，重产是纯浪费——跳过
                if (hasContentChanges)
                {
                    await GenerateSitemapAndFeedsAsync(
                        allPageContexts, siteContext, config, options, cancellationToken);
                }
                Phase("4.写入+聚合产物");

                pagesBuilt = renderedPages.Count + taxonomyPagesIncremental.Count;
            }

            // 处理资源变化
            if (assetChanges.Count > 0)
            {
                foreach (var assetPath in assetChanges.Where(File.Exists))
                {
                    // FileInfo 构造即完成一次 stat（与全量路径 T4.6 同法）
                    var info = new FileInfo(assetPath);
                    var asset = new AssetFile
                    {
                        SourcePath = assetPath,
                        MediaType = GetMediaType(assetPath),
                        Content = await File.ReadAllBytesAsync(assetPath, cancellationToken),
                        ModifiedTime = info.LastWriteTimeUtc
                    };

                    var processed = await _assetPipeline.ProcessAsync(asset, cancellationToken);

                    // 写入处理后的资源
                    var outputPath = Path.Combine(options.OutputPath, processed.OutputPath.TrimStart('/'));
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    // 与全量路径一致的写入重试：dev server 下文件可能被占用，
                    // 单资产失败不应放大为整个增量失败
                    await WriteBytesWithRetryAsync(outputPath, processed.Content.ToArray(), cancellationToken);

                    assetsProcessed++;
                }
            }
            Phase("5.资产");
            Phase("总计");

            stopwatch.Stop();

            return new BuildResult
            {
                Success = errors.IsEmpty,
                PagesBuilt = pagesBuilt,
                AssetsProcessed = assetsProcessed,
                Duration = stopwatch.Elapsed,
                MemoryUsed = GC.GetTotalMemory(false),
                Errors = [.. errors],
                Warnings = [.. warnings],
                OutputPath = options.OutputPath
            };
        }
        catch (OperationCanceledException)
        {
            // 取消不是构建失败：重抛交还调用方（同 BUILD001 分支）
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            errors.Add(new BuildError
            {
                FilePath = options.SourcePath,
                Line = 0,
                Column = 0,
                Message = ex.Message,
                ErrorCode = "BUILD002"
            });

            return new BuildResult
            {
                Success = false,
                PagesBuilt = 0,
                AssetsProcessed = 0,
                Duration = stopwatch.Elapsed,
                MemoryUsed = GC.GetTotalMemory(false),
                Errors = [.. errors],
                Warnings = [.. warnings],
                OutputPath = options.OutputPath
            };
        }
    }

    /// <summary>
    /// 全局 identity 判定：config 文件（flint./config./hugo. 前缀）与
    /// data/、archetypes/ 目录的变化影响面广（配置重载/全站数据/原型模板），
    /// 增量路径收到这类路径时丢弃缓存树走全量装配
    /// </summary>
    internal static bool IsGlobalIdentityPath(string path, string sourcePath)
    {
        var normalized = Path.GetFullPath(path);
        var root = Path.GetFullPath(sourcePath);
        if (!normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalized[root.Length..].TrimStart(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var segments = relative.Split(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var fileName = segments[^1];
        if (segments.Length == 1 &&
            (fileName.StartsWith("flint.", StringComparison.OrdinalIgnoreCase)
             || fileName.StartsWith("config.", StringComparison.OrdinalIgnoreCase)
             || fileName.StartsWith("hugo.", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 与上方 config 前缀判定同口径：目录大小写不同的站点（Data/Archetypes）
        // 变更同样归入全局 identity
        // 主题兼容批次二：主题资产的站点级身份——themes/<name>/ 下的 shortcodes、
        // data、_markup（render hooks）、archetypes 变更影响面广（短码注册/数据/
        // 渲染钩子/内容模板），归入全局 identity 强制全量装配
        if (segments.Length >= 3 &&
            string.Equals(segments[0], "themes", StringComparison.OrdinalIgnoreCase))
        {
            var assetDirName = segments[2];
            return string.Equals(assetDirName, "shortcodes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assetDirName, "data", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assetDirName, "_markup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(assetDirName, "archetypes", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(segments[0], "data", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segments[0], "archetypes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// taxonomy 输入签名：全部内容页的（树 key + 标题 + Slug + 日期 + tags + categories + draft）
    /// 聚合哈希。签名一致 = term/taxonomy 页的数据输入未变（纯正文编辑），可跳过重产。
    /// 聚合方式：每节点条目哈希成 128-bit 后 XOR——顺序无关（去掉原实现的
    /// 全量 Sort 与 MB 级 Join 分配），节点级条目缓存复用未变化页的哈希
    /// </summary>
    private string ComputeTaxonomySignature(PageTree tree)
    {
        ulong lo = 0, hi = 0;
        tree.Walk(new PageTreeWalker
        {
            Tree = tree,
            Handle = (_, node) =>
            {
                var md = node.Content?.Metadata;
                if (md is not null)
                {
                    if (!_taxonomyEntryCache.TryGetValue(node.Key, out var entry))
                    {
                        var text = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"{node.Key}|{md.Title}|{md.Slug}|{md.Date:O}|{md.Draft}|{{{string.Join(",", md.Tags.OrderBy(t => t, StringComparer.Ordinal))}}}|{{{string.Join(",", md.Categories.OrderBy(c => c, StringComparer.Ordinal))}}}");
                        entry = HashTaxonomyEntry(text);
                        _taxonomyEntryCache[node.Key] = entry;
                    }
                    lo ^= entry.Lo;
                    hi ^= entry.Hi;
                }
                return false;
            }
        });
        // 128-bit XOR 聚合：同集合不同遍历序结果恒等（原实现靠 Sort 保证，此处天然成立）
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{lo:x16}{hi:x16}");
    }

    /// <summary>
    /// 节点条目的 128-bit FNV 哈希：两个独立种子的 64 位 FNV-1a 拼接，零依赖零分配。
    /// 碰撞概率 2^-128 量级；即便碰撞也仅表现为 taxonomy 页多产/漏产一次，
    /// 不损构建正确性（词条页始终由分页确证与依赖图兜底）
    /// </summary>
    private static (ulong Lo, ulong Hi) HashTaxonomyEntry(ReadOnlySpan<char> entry)
    {
        const ulong prime = 1099511628211UL;
        var lo = 14695981039346656037UL; // FNV-1a 64 标准偏置
        var hi = 0xC3B0578D2B6A9B4DUL;   // 第二通道独立种子
        foreach (var c in entry)
        {
            lo = (lo ^ c) * prime;
            hi = (hi ^ c) * prime;
        }
        // 第二通道末端雪崩，降低结构相近输入的碰撞
        hi ^= hi >> 33;
        hi *= 0xFF51AFD7ED558CCDUL;
        hi ^= hi >> 33;
        return (lo, hi);
    }

    /// <summary>
    /// 注册 页面→模板 依赖关系到依赖图（模板名含 partial 闭包，规范化为物理路径）
    /// </summary>
    /// <summary>
    /// 页面 permalink 是否命中最近访问 URL（去尾斜杠差异后精确比对；"/" 首页由 home 类型天然保留）
    /// </summary>
    private static bool MatchesPreferredUrl(PageContext page, IReadOnlySet<string> preferred)
    {
        var rel = page.RelPermalink.TrimEnd('/');
        if (rel.Length == 0)
        {
            return false; // "/" 首页：home 类型已保留在渲染集合
        }

        foreach (var url in preferred)
        {
            var normalized = url.TrimEnd('/');
            if (normalized.Length > 0 &&
                (rel.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                 page.Permalink.TrimEnd('/').Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从被删输出文件向上清理变空的目录链（只删 index.html 会留下目录壳，
    /// 部署产物因此含无效空目录）。安全边界：严格限制在输出根之下，输出根
    /// 本身永不删除；目录非空/被占用即停（父目录不可能为空），清理失败不
    /// 放大为构建失败
    /// </summary>
    private static void PruneEmptyDirectories(string staleOutputPath, string outputRootPath)
    {
        var outputRoot = Path.GetFullPath(outputRootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dir = Path.GetDirectoryName(Path.GetFullPath(staleOutputPath));
        while (!string.IsNullOrEmpty(dir))
        {
            var full = Path.GetFullPath(dir);
            if (full.Length <= outputRoot.Length ||
                !full.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                break; // 越界或已到输出根：输出根本身永不删除
            }
            bool deleted;
            try
            {
                Directory.Delete(full, recursive: false);
                deleted = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                deleted = false; // 非空或被占用：向上终止
            }
            if (!deleted)
            {
                break;
            }
            dir = Path.GetDirectoryName(full);
        }
    }
}
