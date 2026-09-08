// Flint 静态站点生成器
// SiteBuilder 渲染调度聚合：短代码注册、依赖注册、站点上下文、页面/首页/分类页渲染

using System.Collections.Concurrent;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content.Shortcodes;
using Flint.Core.Models;

namespace Flint.Core.Site;

/// <summary>
/// 站点构建器（partial）：渲染调度聚合
/// </summary>
public sealed partial class SiteBuilder
{
    /// <summary>
    /// 注册站点级短代码：扫描 layouts/shortcodes/*.html，每个文件注册为 TemplateShortcode
    /// （模板语法错误让构建失败——fail-fast 与未知短代码语义一致）
    /// </summary>
    private void RegisterSiteShortcodes(string sourcePath, SiteConfig config)
    {
        var shortcodesDir = Path.Combine(
            sourcePath,
            string.IsNullOrEmpty(config.LayoutDir) ? "layouts" : config.LayoutDir,
            "shortcodes");

        if (!Directory.Exists(shortcodesDir))
        {
            return;
        }

        var registry = _contentParser.ShortcodeProcessor.Registry;
        foreach (var file in Directory.EnumerateFiles(shortcodesDir, "*.html"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);
            try
            {
                registry.Register(new TemplateShortcode(name, content));
            }
            catch (FormatException ex)
            {
                throw new FormatException($"加载站点短代码失败: {file}", ex);
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
    /// 目录不存在时返回空字典
    /// </summary>
    private async Task<IReadOnlyDictionary<string, object>> LoadSiteDataAsync(
        BuildOptions options,
        CancellationToken cancellationToken)
    {
        var dataDir = Path.Combine(options.SourcePath,
            "data");
        return await _dataLoader.LoadAsync(dataDir, cancellationToken);
    }

    private SiteContext BuildSiteContext(
        SiteConfig config,
        List<PageContext> pages,
        TaxonomyCollection taxonomies,
        IReadOnlyDictionary<string, object>? siteData = null)
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

        return new SiteContext
        {
            Title = config.Title,
            BaseURL = config.BaseURL,
            Language = config.LanguageCode,
            Pages = pages,
            RegularPages = pages.Where(p => p.Type != "section").ToList(),
            Taxonomies = taxonomies,
            Menus = menuBuilder.Build(),
            Config = config,
            Data = siteData ?? new Dictionary<string, object>(),
            BuildDate = DateTimeOffset.Now,
            LastChange = lastChange,
            IsMultiLingual = false,
            Languages = [config.LanguageCode]
        };
    }

    private async Task<List<RenderedPage>> RenderPagesAsync(
        List<PageContext> pages,
        SiteContext siteContext,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<RenderedPage>();

        // 优化：批量处理以减少并发调度开销
        // 每批处理多个页面，共享模板查找开销
        const int batchSize = 20;
        var batches = pages.Chunk(batchSize).ToArray();

        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = cancellationToken
            },
            async (batch, ct) =>
            {
                foreach (var page in batch)
                {
                    try
                    {
                        // 模板选择对齐 Hugo kind 语义：home→index、section→list、普通页→single；
                        // front matter layout 声明优先，缺文件回退 single（与旧行为一致，
                        // 内置回退模板继续兜底 term/taxonomy）。此前一律 single：
                        // 首页 layouts/index.html 被 renderSet 的 home 页覆盖、section 永远不走 list
                        var baseTemplateName = page.Type switch
                        {
                            "home" => page.Layout ?? (_templateRenderer.TemplateExists("index") ? "index" : "single"),
                            "section" => page.Layout ?? (_templateRenderer.TemplateExists("list") ? "list" : "single"),
                            _ => page.Layout ?? "single"
                        };

                        // 输出格式：页面级 outputs 覆盖（对齐 Hugo），空则仅 html。
                        // html 恒渲染；非 html 格式（json 等）需存在输出格式变体模板
                        //（{kind/ayout 名}.{format}.html）才产出，无模板跳过
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
                                IsSingle = baseTemplateName == "single",
                                // 随 kind 分派同步置位：首页/列表页模板的 is_home/is_list
                                // 判断依赖此标志（taxonomy 路径已设 IsList，此处对齐）
                                IsHome = page.Type == "home",
                                IsList = page.Type == "section"
                            };

                            string html;
                            if (format.Name == "html")
                            {
                                html = await _templateRenderer.RenderAsync(baseTemplateName, context, ct);
                            }
                            else
                            {
                                // 输出格式变体模板：{基模板名}.{格式名}（如 single.json）
                                var variantTemplate = $"{baseTemplateName}.{format.Name}";
                                if (!_templateRenderer.TemplateExists(variantTemplate))
                                {
                                    continue;
                                }
                                html = await _templateRenderer.RenderAsync(variantTemplate, context, ct);
                            }

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
            });

        return [.. results];
    }

    private async Task<RenderedPage?> GenerateHomePageAsync(
        SiteContext siteContext,
        SiteConfig config,
        BuildOptions options,
        ConcurrentBag<BuildError> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            // 检查是否存在首页模板
            if (!_templateRenderer.TemplateExists("index"))
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
                Type = "home"
            };

            var context = new TemplateContext
            {
                Page = pageContext,
                Site = siteContext,
                IsHome = true
            };

            var html = await _templateRenderer.RenderAsync("index", context, cancellationToken);
            RegisterRenderedDependencies(context);
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

        foreach (var taxPage in taxonomyPages)
        {
            // 列表页（无 TermName）受 "taxonomy" 控制，term 页受 "term" 控制
            if (taxPage.TermName is null ? taxonomyDisabled : termDisabled)
            {
                continue;
            }
            try
            {
                // 从完整 URL 中提取相对路径
                var relPermalink = taxPage.Permalink ?? "/";
                var baseUrl = config.BaseURL.TrimEnd('/');
                if (relPermalink.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    relPermalink = relPermalink[baseUrl.Length..];
                }
                if (!relPermalink.StartsWith('/'))
                {
                    relPermalink = "/" + relPermalink;
                }

                // 创建分类页面的上下文
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
                    Type = "taxonomy",
                    // 模板 page.pages（term 页词条页面集合）与 page.terms（taxonomy
                    // 页词条列表）的数据源——默认主题模板依赖此二者，缺省时
                    // 词条/分类页渲染为空列表（端到端冒烟发现的回归）
                    Pages = taxPage.Pages,
                    Terms = taxPage.Terms
                };

                var templateName = taxPage.PageType == TaxonomyPageType.TaxonomyList
                    ? "taxonomy"
                    : "term";

                // 如果 term 模板不存在，回退到 taxonomy 或 list 模板
                if (templateName == "term" && !_templateRenderer.TemplateExists("term"))
                {
                    if (_templateRenderer.TemplateExists("taxonomy"))
                    {
                        templateName = "taxonomy";
                    }
                    else if (_templateRenderer.TemplateExists("list"))
                    {
                        templateName = "list";
                    }
                }
                // 如果 taxonomy 模板不存在，回退到 list 模板
                else if (templateName == "taxonomy" && !_templateRenderer.TemplateExists("taxonomy"))
                {
                    if (_templateRenderer.TemplateExists("list"))
                    {
                        templateName = "list";
                    }
                }

                var context = new TemplateContext
                {
                    Page = pageContext,
                    Site = siteContext,
                    IsList = true,
                    // term 页注入词条专属页面集合（Hugo .Pages 语义）：
                    // 此前 term 模板的 site.regular_pages 会列出全站页面而非该词条页面
                    Pages = taxPage.Pages
                };

                var html = await _templateRenderer.RenderAsync(templateName, context, cancellationToken);
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
