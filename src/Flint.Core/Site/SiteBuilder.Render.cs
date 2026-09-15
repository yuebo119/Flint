// Flint 静态站点生成器
// SiteBuilder 渲染调度聚合：短代码注册、依赖注册、站点上下文、页面/首页/分类页渲染

using System.Collections.Concurrent;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;
using Flint.Core.Templates;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器（partial）：渲染调度聚合
/// </summary>
public sealed partial class SiteBuilder
{
    /// <summary>
    /// 注册短代码：主题 layouts/shortcodes 先注册（低优先），站点后注册覆盖同名——
    /// 对齐 Hugo 主题语义（站点覆盖主题）。模板语法错误 fail-fast
    /// </summary>
    private void RegisterSiteShortcodes(string sourcePath, SiteConfig config)
    {
        var layoutDirName = string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir;

        // 主题列表前面的优先：反序注册，前面的主题后注册覆盖后面的
        foreach (var themeName in config.ThemeNames.Reverse())
        {
            var themeLayout = Path.Combine(sourcePath, "themes", themeName, layoutDirName);
            // shortcodes/ 与 _shortcodes/ 双形态（后者为 Hugo v0.146+ 新目录约定）
            RegisterShortcodesFrom(Path.Combine(themeLayout, "_shortcodes"), themeName);
            RegisterShortcodesFrom(Path.Combine(themeLayout, "shortcodes"), themeName);
        }

        var siteLayout = Path.Combine(sourcePath, layoutDirName);
        RegisterShortcodesFrom(Path.Combine(siteLayout, "_shortcodes"), "站点");
        RegisterShortcodesFrom(Path.Combine(siteLayout, "shortcodes"), "站点");
    }

    /// <summary>
    /// 由页面上下文构造模板查找查询（A 组）：kind 来自树节点类型，
    /// layout/type/section 参与候选级排序。layout 是"优先提示而非强制"——
    /// 候选链把 {type}/{layout}、{section}/{layout}、{layout} 置于默认名之前，
    /// 声明了但物理缺失时自然回退默认名（对齐 Hugo，且不再需要预先 TemplateExists 探测）。
    /// </summary>
    private static PageTemplateQuery BuildPageTemplateQuery(PageContext page) =>
        new()
        {
            Kind = string.IsNullOrEmpty(page.Kind) ? "page" : page.Kind,
            Layout = page.Layout,
            DeclaredType = page.DeclaredType,
            Section = page.Section,
            Taxonomy = page.TaxonomyName
        };

    private static bool IsHtmlFormat(string? formatName) =>
        string.IsNullOrEmpty(formatName) ||
        formatName.Equals("html", StringComparison.OrdinalIgnoreCase);

    private void RegisterShortcodesFrom(string shortcodesDir, string origin)
    {
        if (!Directory.Exists(shortcodesDir))
        {
            return;
        }

        var isSite = string.Equals(origin, "站点", StringComparison.Ordinal);
        var registry = _contentParser.ShortcodeProcessor.Registry;
        foreach (var file in Directory.EnumerateFiles(shortcodesDir, "*.html"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            try
            {
                registry.Register(new TemplateShortcode(name, content));
            }
            catch (FormatException ex) when (!isSite)
            {
                // 主题短码为第三方内容：单个短码语法错误不应阻断整个构建
                // （Hugo 对主题短码同样容忍；站点短码保持 fail-fast）
                Console.Error.WriteLine($"[警告] 跳过无法解析的主题短代码（{origin}）: {file} — {ex.Message}");
            }
            catch (FormatException ex)
            {
                throw new FormatException($"加载短代码失败（{origin}）: {file}", ex);
            }
        }
    }

    /// <summary>
    /// 渲染期依赖注册（T4.1）：用本次渲染实际 include 的模板物理路径
    /// 覆盖静态闭包注册（条件 include 只记实际走的分支，精确化反查）。
    /// data:site.* 键不是文件路径，不进依赖图（页面集合失效由树重算覆盖），
    /// 保留在 RenderedDependencies 供后续值缓存分层消费
    /// </summary>
    private void RegisterRenderedDependencies(TemplateContext context)
    {
        if (context.RenderedDependencies is not { } deps || string.IsNullOrEmpty(context.Page.SourcePath))
        {
            return;
        }

        var physicalPaths = deps
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToList();

        if (physicalPaths.Count > 0)
        {
            _dependencyGraph.RegisterDependency(context.Page.SourcePath, physicalPaths);
        }
    }

    private void RegisterTemplateDependencies(
        List<PageContext> pages,
        SiteConfig config,
        string sourcePath)
    {        var layoutsDir = Path.Combine(sourcePath, string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir);

        foreach (var page in pages)
        {
            if (string.IsNullOrEmpty(page.SourcePath))
            {
                continue;
            }

            var templateName = page.Layout ?? "single";
            var templateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                templateName
            };

            // 收集模板的 include/partial 闭包（递归）
            foreach (var dep in _templateRenderer.GetDependencies(templateName))
            {
                templateNames.Add(dep);
            }

            // 逻辑模板名 → layouts 目录下的物理路径（与 FileTemplateLoader 的搜索规则一致）
            var physicalPaths = templateNames
                .Select(name => Path.GetFullPath(Path.Combine(
                    layoutsDir,
                    name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ? name : name + ".html")))
                .ToList();

            _dependencyGraph.RegisterDependency(page.SourcePath, physicalPaths);
        }
    }

    /// <summary>
    /// 加载 data/ 目录数据（site.data 模板变量的来源）；
    /// 主题 data/ 先加载（低优先），站点后加载覆盖同名键；
    /// 目录不存在时返回空字典
    /// </summary>
    private async Task<IReadOnlyDictionary<string, object>> LoadSiteDataAsync(
        BuildOptions options,
        IReadOnlyList<string> themeNames,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var themeName in themeNames.Reverse())
        {
            var themeDataDir = Path.Combine(options.SourcePath, "themes", themeName, "data");
            if (Directory.Exists(themeDataDir))
            {
                foreach (var (key, value) in await _dataLoader.LoadAsync(themeDataDir, cancellationToken))
                {
                    merged[key] = value;
                }
            }
        }

        var dataDir = Path.Combine(options.SourcePath, "data");
        if (Directory.Exists(dataDir))
        {
            foreach (var (key, value) in await _dataLoader.LoadAsync(dataDir, cancellationToken))
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    private SiteContext BuildSiteContext(
        SiteConfig config,
        List<PageContext> pages,
        TaxonomyCollection taxonomies,
        IReadOnlyDictionary<string, object>? siteData = null,
        IReadOnlyDictionary<string, string>? translations = null)
    {
        // 当页面列表为空时，使用当前时间作为 LastChange
        var lastChange = pages.Count > 0
            ? pages.Max(p => p.LastMod ?? p.Date)
            : DateTimeOffset.Now;

        // 菜单链接线：config.Menus 静态定义 + 各页 front matter 声明共同汇入
        //（此前两条解析管线的产出在硬编码空 MenuCollection 处被丢弃）
        var menuBuilder = new MenuBuilder();
        foreach (var (menuName, items) in config.Menus.Menus)
        {
            foreach (var item in items)
            {
                menuBuilder.AddMenuItem(menuName, item);
            }
        }
        menuBuilder.AddFromPages(pages, p => p.MenuEntries);

        // render hook 内的 i18n 需要翻译表，但 hook 渲染发生在内容解析期
        //（早于本方法），故把翻译表快照回填给渲染器——同一构建内后续的 hook
        // 渲染（含增量重建）即可用上真实翻译（此前 hook 内 i18n 报函数未找到）
        if (_templateRenderer is ScribanTemplateRenderer scribanRenderer)
        {
            scribanRenderer.Translations = translations ?? new Dictionary<string, string>();
        }

        // 常规页 = Kind "page"（Hugo 语义：排除 home/section/taxonomy/term）。
        // 旧判据 `Type != "section"` 只挡住 section，首页（kind=home）混进集合——
        // `{{ range .Site.RegularPages }}` 会多出首页（实测：探测站 4 篇内容 +
        // 首页得 5 条，Hugo v0.166 同站点为 4 条）
        var regularPages = pages.Where(p => p.Kind == "page").ToList();

        return new SiteContext
        {
            Title = config.Title,
            BaseURL = config.BaseURL,
            Language = config.LanguageCode,
            Pages = pages,
            RegularPages = regularPages,
            Taxonomies = taxonomies,
            Menus = menuBuilder.Build(),
            Config = config,
            Data = siteData ?? new Dictionary<string, object>(),
            // 站点参数装配（曾缺失：模板 site.params.* 恒空——README 文档化的用法）
            Params = config.Params,
            BuildDate = DateTimeOffset.Now,
            LastChange = lastChange,
            IsMultiLingual = false,
            Languages = [config.LanguageCode],
            Translations = translations ?? new Dictionary<string, string>(),
            PaginatorPages = regularPages.Take(Math.Max(1, config.Paginate)).ToList(),
            PaginatorTotalPages = Math.Max(1, (int)Math.Ceiling(regularPages.Count / (double)Math.Max(1, config.Paginate))),
            PaginatorPageNumber = 1,
        };
    }

    private async Task<List<RenderedPage>> RenderPagesAsync(
        List<PageContext> pages,
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<RenderedPage>();

        // 分页标记按构建清空：跨构建残留会让"这轮没分页"的列表页被误判为分页
        if (_templateRenderer is ScribanTemplateRenderer)
        {
            ScribanTemplateRenderer.ResetPaginateTracking();
            // 登记全量页面：.GetPage 需要 section 页，而页面对象的构造快照可能只有常规页
            ScribanTemplateRenderer.SetCurrentSitePages(pages);
        }

        // C1 分页多页产出（Hugo 语义）：列表页（home/section）的**第 1 页**始终产出
        //（就是列表页自身的 URL）；`/page/1/` 跳转页与 `/page/2..N/` 只在**模板调用过
        //  `.Paginate`** 时产出。探测实证（Hugo v0.166）：home 模板不调用 .Paginate 的
        // 站点没有 /page/N/，调用过的列表页则额外产出 /page/1/（canonical 跳转页）。
        // 故先只放第 1 页，其余等渲染完、看标记再补。
        // paginate <= 0 显式关闭分页（非列表页不受影响）
        var renderTargets = new List<PageContext>(pages.Count);
        var paginatedLists = new List<(PageContext Page, IReadOnlyList<PageContext> Items, int TotalPages)>();
        foreach (var page in pages)
        {
            if ((page.Kind is "home" or "section") && config.Paginate > 0)
            {
                var items = page.Pages ?? [];
                var totalPages = Math.Max(1,
                    (int)Math.Ceiling(items.Count / (double)config.Paginate));
                var firstPager = PaginatorView.Create(
                    items, 1, config.Paginate,
                    page.RelPermalink, config.PaginatePath);
                renderTargets.Add(page.WithPaginator(firstPager, config.BaseURL));
                paginatedLists.Add((page, items, totalPages));
                continue;
            }
            renderTargets.Add(page);
        }

        // 优化：批量处理以减少并发调度开销
        // 渲染次序：home 页**先串行渲染**、其余页再分批并行（对齐 Hugo 的页面渲染次序）。
        // 主题常在 home 里把跨页数据写入 .Site.Store（FixIt 的
        // `$.Store.Set "mainSectionPages"` 之后由 single/footer.html 读取），
        // 分批并行会让读取先于写入 → "$pages.Prev for a null object"
        foreach (var homePage in renderTargets.Where(p => p.Kind == "home"))
        {
            await RenderBatchAsync([homePage], cancellationToken).ConfigureAwait(false);
        }

        // 每批处理多个页面，共享模板查找开销
        const int batchSize = 20;
        var batches = renderTargets.Where(p => p.Kind != "home").Chunk(batchSize).ToArray();

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (batch, ct) =>
            {
                await RenderBatchAsync(batch, ct).ConfigureAwait(false);
            });

        async ValueTask RenderBatchAsync(PageContext[] batch, CancellationToken ct)
        {
                foreach (var page in batch)
                {
                    try
                    {
                        // A 组：页面感知候选链（对齐 Hugo lookup order）。
                        // kind 来自树节点（home/section/page），layout/type/section 参与
                        // 候选级排序——此前只按单一名字全局加权，导致 section/type 目录
                        // 模板互相污染（Ananke 的 page/single.html 实测吞掉全站）
                        var baseQuery = BuildPageTemplateQuery(page);

                        // 输出格式：页面级 outputs 覆盖（对齐 Hugo），空则仅 html。
                        // 格式变体模板由候选链按 .{format} 后缀解析（含主题回退）
                        var formats = page.Outputs.Count > 0
                            ? page.Outputs
                            : (IReadOnlyList<string>)["html"];

                        foreach (var formatName in formats)
                        {
                            var format = OutputFormats.Resolve(formatName);
                            if (format is null)
                            {
                                continue;
                            }

                            var context = new TemplateContext
                            {
                                Page = page,
                                Site = siteContext,
                                Data = siteContext.Data,
                                IsSingle = baseQuery.Kind == "page",
                                // 随 kind 分派同步置位：首页/列表页模板的 is_home/is_list
                                // 判断依赖此标志（taxonomy 路径已设 IsList，此处对齐）
                                IsHome = baseQuery.Kind == "home",
                                IsList = baseQuery.Kind == "section",
                                // 模板全局 pages：Hugo 的列表页 .Pages 等价物。
                                // 此前未设置 → 全局 `pages` 回落空列表，使
                                // `{{ range .Pages }}`（转换为 pages）渲染空
                                // （mini fixture 实测）
                                Pages = page.Pages
                            };

                            // 非 html 格式：候选链已带格式后缀，未命中物理模板即跳过
                            //（格式互不替代，不回退 html 主形态——PageOutputFormats 契约）
                            if (!IsHtmlFormat(format.Name) &&
                                !_templateRenderer.PageTemplateExists(baseQuery with { OutputFormat = format.Name }))
                            {
                                continue;
                            }

                            var html = await _templateRenderer.RenderPageAsync(
                                baseQuery with { OutputFormat = format.Name },
                                context,
                                ct);

                            RegisterRenderedDependencies(context);
                            results.Add(new RenderedPage
                            {
                                OutputPath = GetOutputPathForFormat(page.RelPermalink, format, options.OutputPath),
                                Content = html
                            });
                        }
                    }
                    catch (Flint.Core.Templates.TemplateNotFoundException) when (options.MissingLayout == MissingLayoutBehavior.Skip)
                    {
                        // 宽容模式：缺模板页面跳过（对齐 Hugo 大规模"带病通过"语义），
                        // 错误不累积——页面声明了引擎无对应布局的 layout 时可选此路径
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        errors.Add(new BuildError
                        {
                            FilePath = page.RelPermalink,
                            Line = 0,
                            Column = 0,
                            Message = ex.Message,
                            ErrorCode = "RENDER001"
                        });
                    }
                }
        }

        // 模板确证分页的列表页：补 /page/1/ 跳转页与 /page/2..N/
        var extraTargets = new List<PageContext>();
        foreach (var (listPage, defaultItems, _) in paginatedLists)
        {
            if (!ScribanTemplateRenderer.WasPaginateInvoked(listPage.RelPermalink))
            {
                continue;
            }

            // 模板给 .Paginate 传了**显式集合/页大小**时以它为准（Hugo 语义）：
            // 分页页数与每页内容都按该集合与尺寸切片（papermod/m10c 的 home 实测；
            // loveit 的 home 传 `$posts.paginate`=6，按站点 2 算会多出 /page/2/）
            var registration = ScribanTemplateRenderer.GetPaginateCollection(listPage.RelPermalink);
            var items = registration?.Items ?? defaultItems;
            var pageSize = registration is { Size: > 0 } ? registration.Size : config.Paginate;
            var totalPages = Math.Max(1,
                (int)Math.Ceiling(items.Count / (double)Math.Max(1, pageSize)));

            var firstPagePath = $"{listPage.RelPermalink.TrimEnd('/')}/{config.PaginatePath}/1/";
            results.Add(new RenderedPage
            {
                OutputPath = GetOutputPathForFormat(firstPagePath, OutputFormats.Html, options.OutputPath),
                Content = BuildPaginationRedirectPage(
                    listPage.Permalink ?? listPage.RelPermalink)
            });

            for (var pageNumber = 2; pageNumber <= totalPages; pageNumber++)
            {
                var pager = PaginatorView.Create(
                    items, pageNumber, pageSize,
                    listPage.RelPermalink, config.PaginatePath);
                extraTargets.Add(listPage.WithPaginator(pager, config.BaseURL));
            }
        }

        if (extraTargets.Count > 0)
        {
            foreach (var batch in extraTargets.Chunk(batchSize))
            {
                await RenderBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            }
        }

        return [.. results];
    }

    /// <summary>
    /// 分页跳转页内容：Hugo 对 <c>/page/1/</c> 的产出形态（canonical + meta refresh
    /// 指向列表页本体），用于把分页 URL 归并到规范 URL
    /// </summary>
    private static string BuildPaginationRedirectPage(string canonicalUrl) =>
        "<!DOCTYPE html>\n<html>\n\t<head>\n" +
        $"\t\t<title>{canonicalUrl}</title>\n" +
        $"\t\t<link rel=\"canonical\" href=\"{canonicalUrl}\">\n" +
        "\t\t<meta charset=\"utf-8\">\n" +
        $"\t\t<meta http-equiv=\"refresh\" content=\"0; url={canonicalUrl}\">\n" +
        "\t</head>\n</html>\n";

    private async Task<RenderedPage?> GenerateHomePageAsync(
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            // A 组候选链：index → home → list（home.html 为 Hugo 0.146+ 标准名，
            // 此前硬编码 index 导致仅用 home.html 的主题首页渲染为空）
            var homeQuery = new PageTemplateQuery { Kind = "home" };

            // 无任何 home 布局（含 _default 兜底）时不做兜底页——树中 home 页
            // 已由 RenderPagesAsync 处理，此处仅补手建场景
            if (!_templateRenderer.PageTemplateExists(homeQuery))
            {
                return null;
            }

            // 创建首页上下文
            var pageContext = new PageContext
            {
                Title = config.Title,
                Content = "",
                Permalink = config.BaseURL,
                RelPermalink = "/",
                Date = DateTimeOffset.Now,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "home",
                Kind = "home"
            };

            var context = new TemplateContext
            {
                Page = pageContext,
                Site = siteContext,
                Data = siteContext.Data,
                IsHome = true
            };

            var html = await _templateRenderer.RenderPageAsync(homeQuery, context, cancellationToken);
            RegisterRenderedDependencies(context);
            // 模板命中却产出空内容（典型：裸 {{ define "main" }} 未配套 baseof）视为
            // 首页生成失败——静默写空 index.html 比报错更贵（Ananke 目录形态实证）
            if (string.IsNullOrWhiteSpace(html))
            {
                errors.Add(new BuildError
                {
                    FilePath = "/",
                    Line = 0,
                    Column = 0,
                    Message = "首页模板产出为空（检查 home/index 模板的 block 与 baseof 配套）",
                    ErrorCode = "HOME001"
                });
                return null;
            }

            return new RenderedPage
            {
                OutputPath = Path.Combine(options.OutputPath, "index.html"),
                Content = html
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(new BuildError
            {
                FilePath = "/",
                Line = 0,
                Column = 0,
                Message = $"首页生成失败: {ex.Message}",
                ErrorCode = "HOME001"
            });
            return null;
        }
    }

    /// <summary>
    /// 词条 → 页面对象（Hugo 的 taxonomy 列表页 .Pages 语义：列出各词条页）。
    /// 词条本身不是内容页，故用最小 PageContext 投影（title/permalink/date），
    /// 使主题的 .Pages.ByDate 排序与 .Title/.RelPermalink 访问可用
    /// </summary>
    private static IReadOnlyList<PageContext> BuildTermPages(
        IReadOnlyList<TaxonomyTerm>? terms,
        SiteConfig config)
    {
        if (terms is null || terms.Count == 0)
        {
            return [];
        }

        var list = new List<PageContext>(terms.Count);
        foreach (var term in terms)
        {
            var rel = term.Permalink ?? "/";
            if (rel.StartsWith(config.BaseURL, StringComparison.OrdinalIgnoreCase))
            {
                rel = rel[config.BaseURL.TrimEnd('/').Length..];
            }
            if (!rel.StartsWith('/'))
            {
                rel = "/" + rel;
            }

            // 词条页的"日期"取该词条下最新文章（用于 ByDate 排序，对齐 Hugo 的
            // 词条页 Date = 其页面集合的最近日期）
            var date = term.Pages.Count > 0
                ? term.Pages.Max(p => p.Date)
                : DateTimeOffset.Now;

            list.Add(new PageContext
            {
                Title = term.Name,
                Content = "",
                Permalink = term.Permalink ?? config.BaseURL,
                RelPermalink = rel,
                Date = date,
                Tags = [],
                Categories = [],
                WordCount = 0,
                ReadingTime = TimeSpan.Zero,
                Type = "term",
                Kind = "term",
                Pages = term.Pages
            });
        }

        return list;
    }

    private async Task<List<RenderedPage>> GenerateTaxonomyPagesAsync(
        TaxonomyCollection taxonomies,
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        var results = new List<RenderedPage>();

        // disableKinds 消费点：taxonomy/term 分类页可被配置禁用（对齐 Hugo kind 语义）。
        // 两者独立判定——此前 && 连接，只禁 taxonomy 或只禁 term 单独配置时不生效
        var disabled = config.DisableKinds;
        var taxonomyDisabled = disabled.Contains("taxonomy", StringComparer.OrdinalIgnoreCase);
        var termDisabled = disabled.Contains("term", StringComparer.OrdinalIgnoreCase);
        if (taxonomyDisabled && termDisabled)
        {
            return results;
        }

        var taxonomyService = new TaxonomyService(config.BaseURL);
        var paginationService = new PaginationService(config.PaginatePath, config.Paginate);
        var generator = new TaxonomyPageGenerator(taxonomyService, paginationService);

        var taxonomyPages = generator.GeneratePages(taxonomies, config.Paginate);

        // 分页产出（Hugo 语义）：先只渲染第 1 页；`/page/N/`(N≥2) 与 `/page/1/`
        // 跳转页待确认"该词条/分类页的模板确实分页"（访问过 .Paginate/.Paginator）
        // 再产出——Hugo 的分页页由模板调用驱动，不是站点级预生成
        var deferred = new List<TaxonomyPageInfo>();
        var firstPages = new List<TaxonomyPageInfo>();
        foreach (var taxPage in taxonomyPages)
        {
            // 列表页（无 TermName）受 "taxonomy" 控制，term 页受 "term" 控制
            if (taxPage.TermName is null ? taxonomyDisabled : termDisabled)
            {
                continue;
            }
            if (taxPage.PageNumber > 1)
            {
                deferred.Add(taxPage);
                continue;
            }
            await RenderTaxonomyPageAsync(taxPage).ConfigureAwait(false);
            firstPages.Add(taxPage);
        }

        // `/page/1/` 跳转页：模板确实分页的 term/分类列表页都要（与总页数无关）
        foreach (var firstPage in firstPages)
        {
            var baseRel = BaseRelPermalinkOf(firstPage);
            if (!ScribanTemplateRenderer.WasPaginateInvoked(baseRel))
            {
                continue;
            }
            results.Add(new RenderedPage
            {
                OutputPath = GetOutputPathForFormat(
                    $"{baseRel.TrimEnd('/')}/{config.PaginatePath}/1/", OutputFormats.Html, options.OutputPath),
                Content = BuildPaginationRedirectPage($"{config.BaseURL.TrimEnd('/')}{baseRel}")
            });
        }

        // `/page/N/`(N≥2)：同为模板分页确证后才产出
        foreach (var taxPage in deferred)
        {
            if (!ScribanTemplateRenderer.WasPaginateInvoked(BaseRelPermalinkOf(taxPage)))
            {
                continue;
            }
            await RenderTaxonomyPageAsync(taxPage).ConfigureAwait(false);
        }

        string BaseRelPermalinkOf(TaxonomyPageInfo taxPage)
        {
            var rel = RelPermalinkOf(taxPage);
            var suffix = $"/{config.PaginatePath}/{taxPage.PageNumber}/";
            return rel.EndsWith(suffix, StringComparison.Ordinal)
                ? rel[..^suffix.Length] + "/"
                : rel;
        }

        // 从完整 URL 中提取相对路径（taxonomy/term 页共用）
        string RelPermalinkOf(TaxonomyPageInfo taxPage)
        {
            var rel = taxPage.Permalink ?? "/";
            var baseUrl = config.BaseURL.TrimEnd('/');
            if (rel.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
            {
                rel = rel[baseUrl.Length..];
            }
            return rel.StartsWith('/') ? rel : "/" + rel;
        }

        async Task RenderTaxonomyPageAsync(TaxonomyPageInfo taxPage)
        {
            try
            {
                var relPermalink = RelPermalinkOf(taxPage);

                // 创建分类页面的上下文
                var isTaxonomyList = taxPage.PageType == TaxonomyPageType.TaxonomyList;
                var pageSize = Math.Max(1, config.Paginate);
                // taxonomy 列表页的集合 = **词条页集合**（Hugo 语义；分页切的就是它），
                // term 页则是该词条下的页面集合
                var taxonomyListItems = isTaxonomyList ? BuildTermPages(taxPage.Terms, config) : null;
                var pageItems = isTaxonomyList
                    ? taxonomyListItems!.Skip((taxPage.PageNumber - 1) * pageSize).Take(pageSize).ToList()
                    : taxPage.Pages ?? [];
                var pageContext = new PageContext
                {
                    Title = taxPage.TermName ?? taxPage.TaxonomyName,
                    Content = "",
                    Permalink = taxPage.Permalink ?? "/",
                    RelPermalink = relPermalink,
                    Date = DateTimeOffset.Now,
                    Tags = [],
                    Categories = [],
                    WordCount = 0,
                    ReadingTime = TimeSpan.Zero,
                    // Type 保留既有模板语义（"taxonomy"），Kind 承载查找链维度
                    Type = "taxonomy",
                    Kind = isTaxonomyList ? "taxonomy" : "term",
                    TaxonomyName = taxPage.TaxonomyName,
                    TaxonomySingular = taxPage.TaxonomySingular,
                    TaxonomyPlural = taxPage.TaxonomyPlural,
                    // 模板 page.pages 与 page.terms 的数据源。
                    // Hugo 语义：taxonomy 列表页的 .Pages 是**词条页集合**
                    // （主题用 `{{ range .Pages.ByDate }}` 列出全部标签）——
                    // 此前只给 term 页设 Pages，taxonomy 页为 null，使
                    // `page.pages.by_date` 报 null（mini fixture 实测）
                    Pages = pageItems,
                    Terms = taxPage.Terms,
                    // 分页器：Hugo 的 list 类页面（含 taxonomy/term）恒有 .Paginator，
                    // 主题直接访问 .TotalPages/.Pagers 而不加 with 保护——
                    // 缺省时模板报 "Cannot get the member $pag.TotalPages for a null object"
                    // （mini 主题 fixture 实证）。空集合也有一页（对齐 Hugo）。
                    // 分页对象与 .Pages 同源：taxonomy 列表页分页的是**词条页集合**
                    //（Hugo 实测：/tags/ 的 .Paginator 切词条页 → 产出 /tags/page/2/；
                    //  此前用 taxPage.Pages（taxonomy 页为 null）→ 恒 1 页，缺 /tags/page/2/）
                    Paginator = PaginatorView.Create(
                        taxonomyListItems ?? (taxPage.Pages ?? []),
                        taxPage.PageNumber,
                        pageSize,
                        relPermalink,
                        config.PaginatePath)
                };

                var context = new TemplateContext
                {
                    Page = pageContext,
                    Site = siteContext,
                    IsList = true,
                    // term 页注入词条专属页面集合（Hugo .Pages 语义）：
                    // 此前 term 模板的 site.regular_pages 会列出全站页面而非该词条页面
                    Pages = taxPage.Pages
                };

                // A 组候选链：{taxonomy}/terms → {taxonomy}/taxonomy → ... → list，
                // 未命中物理模板时渲染器回退内置模板（taxonomy/term/list）
                var query = BuildPageTemplateQuery(pageContext);
                var html = await _templateRenderer.RenderPageAsync(query, context, cancellationToken);
                RegisterRenderedDependencies(context);
                results.Add(new RenderedPage
                {
                    OutputPath = GetOutputPath(relPermalink, options.OutputPath),
                    Content = html
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new BuildError
                {
                    FilePath = taxPage.Permalink ?? "unknown",
                    Line = 0,
                    Column = 0,
                    Message = ex.Message,
                    ErrorCode = "TAXONOMY001"
                });
            }
        }

        return results;
    }
}
