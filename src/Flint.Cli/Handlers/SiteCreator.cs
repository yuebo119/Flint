// Flint 静态站点生成器
// 站点创建器

namespace Flint.Cli;

/// <summary>
/// 站点创建器
/// </summary>
internal static class SiteCreator
{
    /// <summary>引擎选项：fluid（默认，FLUID-MIGRATION-PLAN P2-3 起新站点默认 Liquid）/ scriban（兼容模式）</summary>
    internal const string EngineFluid = "fluid";
    internal const string EngineScriban = "scriban";

    /// <summary>
    /// 创建新站点目录结构和默认文件
    /// </summary>
    /// <param name="name">站点名称</param>
    /// <param name="engine">模板引擎：fluid（默认，生成 .liquid 脚手架）/ scriban（生成 .html 脚手架）</param>
    /// <returns>0 表示成功，1 表示失败</returns>
    public static int CreateNewSite(string name, string engine = EngineFluid)
    {
        // 验证站点名称
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("错误: 站点名称不能为空");
            Console.ResetColor();
            return 1;
        }

        // 名称是路径段：拒绝分隔符与父目录引用，防在 cwd 之外创建目录
        if (name.Contains('/') || name.Contains('\\') || name is "." or "..")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"错误: 站点名称不能包含路径分隔符或父目录引用: {name}");
            Console.ResetColor();
            return 1;
        }

        // 引擎参数归一：无法识别按 fluid（新站点默认主力引擎）
        var useFluid = !string.Equals(engine, EngineScriban, StringComparison.OrdinalIgnoreCase);

        var sitePath = Path.Combine(Directory.GetCurrentDirectory(), name);

        if (Directory.Exists(sitePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"错误: 目录 '{name}' 已存在");
            Console.ResetColor();
            return 1;
        }

        try
        {
            // 创建主目录
            Directory.CreateDirectory(sitePath);
            Console.WriteLine($"创建站点目录: {sitePath}");

            // 创建子目录结构
            var directories = new[]
            {
                "archetypes",   // 内容模板
                "assets",       // 资源文件（SCSS、JS 等）
                "content",      // 内容文件
                "data",         // 数据文件
                "i18n",         // 国际化文件
                "layouts",      // 布局模板
                "layouts/_default",
                "layouts/partials",
                "static",       // 静态文件
                "themes"        // 主题目录
            };

            foreach (var dir in directories)
            {
                var dirPath = Path.Combine(sitePath, dir);
                Directory.CreateDirectory(dirPath);
                Console.WriteLine($"  创建目录: {dir}/");
            }

            // 创建默认配置文件（engine 选择持久化：--engine scriban 时写入
            // engine = "scriban"，否则不写——默认 auto 不出现在配置里）
            CreateConfigFile(sitePath, name, useFluid ? null : "scriban");

            // 创建默认 archetype
            CreateDefaultArchetype(sitePath);

            // 创建默认布局模板
            // 创建默认布局模板（FLUID-MIGRATION-PLAN P2-3：默认 Liquid 脚手架，
            // --engine scriban 生成旧 .html 形态）
            CreateDefaultLayouts(sitePath, useFluid);

            // 创建示例内容
            CreateWelcomeContent(sitePath);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"站点 '{name}' 创建成功！");
            Console.ResetColor();
            Console.WriteLine($"  模板引擎: {(useFluid ? "Fluid (Liquid)" : "Scriban")}");
            Console.WriteLine();
            Console.WriteLine("下一步:");
            Console.WriteLine($"  cd {name}");
            Console.WriteLine("  Flint serve");
            return 0;
        }
        catch (Exception ex)
        {
            // 半成品回滚：创建中途失败时删除已建的站点目录，
            // 否则重跑会被"目录已存在"挡死，用户须手动清理
            try
            {
                if (Directory.Exists(sitePath))
                {
                    Directory.Delete(sitePath, recursive: true);
                    Console.Error.WriteLine($"已回滚未完成的站点目录: {sitePath}");
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Console.Error.WriteLine($"警告: 半成品目录回滚失败，请手动删除 {sitePath}（{cleanupEx.Message}）");
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"创建站点失败: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void CreateConfigFile(string sitePath, string name, string? engine = null)
    {
        // engine 非默认时写入配置（auto 不写——保持配置简洁）
        var engineLine = engine is not null ? $"\n# 模板引擎策略（auto / fluid / scriban）\nengine = \"{engine}\"\n" : "";
        var configContent = $"""
            # Flint 站点配置文件
            # 详细配置说明请参考文档

            # 站点基本信息
            baseURL = "http://localhost:1313/"
            languageCode = "zh-cn"
            title = "{name}"{engineLine}

            # 构建选项
            [build]
              publishDir = "public"

            # 分页设置
            paginate = 10
            paginatePath = "page"

            # 分类法
            [taxonomies]
              category = "categories"
              tag = "tags"

            # 永久链接：未配置时按 content 目录结构生成（/posts/:filename/，
            # 对齐 Hugo 默认）。如需日期式 URL 可显式配置，如：
            # [permalinks]
            #   posts = "/:year/:month/:title/"

            # 菜单配置
            [menus]
              [[menus.main]]
                name = "首页"
                url = "/"
                weight = 1
              [[menus.main]]
                name = "文章"
                url = "/posts/"
                weight = 2
              [[menus.main]]
                name = "关于"
                url = "/about/"
                weight = 3

            # 参数配置
            [params]
              description = "使用 Flint 构建的站点"
              author = ""
            """;

        var configPath = Path.Combine(sitePath, "Flint.toml");
        File.WriteAllText(configPath, configContent);
        Console.WriteLine("  创建配置: Flint.toml");
    }

    private static void CreateDefaultArchetype(string sitePath)
    {
        var archetypeContent = """
            +++
            title = "{{ replace .Name "-" " " | title }}"
            date = {{ .Date }}
            draft = true
            tags = []
            categories = []
            +++

            在这里写入内容...
            """;

        var archetypePath = Path.Combine(sitePath, "archetypes", "default.md");
        File.WriteAllText(archetypePath, archetypeContent);
        Console.WriteLine("  创建模板: archetypes/default.md");
    }

    private static void CreateDefaultLayouts(string sitePath, bool useFluid)
    {
        if (useFluid)
        {
            CreateLiquidLayouts(sitePath);
            return;
        }
        CreateScribanLayouts(sitePath);
    }

    /// <summary>
    /// Liquid 版脚手架（FLUID-MIGRATION-PLAN P2-3）：新站点默认形态。
    /// 与 Scriban 版同构（同目录同职责），语法差异仅限：
    /// include 无扩展名、循环/条件用 Liquid 标签、下划线标签 trim 空白
    /// </summary>
    private static void CreateLiquidLayouts(string sitePath)
    {
        // head.liquid - 头部 partial
        var headContent = """
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{% if page.title %}{{ page.title }} | {% endif %}{{ site.title }}</title>
            <meta name="description" content="{% if page.description %}{{ page.description }}{% else %}{{ site.params.description }}{% endif %}">
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 800px; margin: 0 auto; padding: 20px; }
                header { border-bottom: 1px solid #eee; padding-bottom: 20px; margin-bottom: 30px; }
                header h1 { font-size: 1.5rem; }
                nav { margin-top: 10px; }
                nav a { margin-right: 15px; color: #0066cc; text-decoration: none; }
                nav a:hover { text-decoration: underline; }
                main { min-height: 60vh; }
                article { margin-bottom: 40px; }
                article h1 { font-size: 2rem; margin-bottom: 10px; }
                article .meta { color: #666; font-size: 0.9rem; margin-bottom: 20px; }
                article .content { line-height: 1.8; }
                article .content h2 { margin-top: 30px; margin-bottom: 15px; }
                article .content p { margin-bottom: 15px; }
                article .content code { background: #f5f5f5; padding: 2px 6px; border-radius: 3px; }
                article .content pre { background: #f5f5f5; padding: 15px; border-radius: 5px; overflow-x: auto; margin-bottom: 15px; }
                footer { border-top: 1px solid #eee; padding-top: 20px; margin-top: 40px; color: #666; font-size: 0.9rem; }
                .post-list { list-style: none; }
                .post-list li { margin-bottom: 20px; padding-bottom: 20px; border-bottom: 1px solid #eee; }
                .post-list h2 { font-size: 1.3rem; margin-bottom: 5px; }
                .post-list h2 a { color: #333; text-decoration: none; }
                .post-list h2 a:hover { color: #0066cc; }
                .post-list .meta { color: #666; font-size: 0.85rem; }
            </style>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "partials", "head.liquid"), headContent);
        Console.WriteLine("  创建布局: layouts/partials/head.liquid");

        // header.liquid - 页头 partial
        var headerContent = """
            <header>
                <h1><a href="/" style="color: inherit; text-decoration: none;">{{ site.title }}</a></h1>
                <nav>
                    {% for item in site.menus.main %}
                    <a href="{{ item.url }}">{{ item.name }}</a>
                    {% endfor %}
                </nav>
            </header>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "partials", "header.liquid"), headerContent);
        Console.WriteLine("  创建布局: layouts/partials/header.liquid");

        // footer.liquid - 页脚 partial（年份经 date filter 的 %Y 取构建年）
        var footerContent = """
            <footer>
                <p>&copy; {{ site.build_date | date: '%Y' }} {{ site.title }}. Powered by <a href="https://github.com/yuebo119/Flint">Flint</a>.</p>
            </footer>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "partials", "footer.liquid"), footerContent);
        Console.WriteLine("  创建布局: layouts/partials/footer.liquid");

        // baseof.liquid - 基础布局（不再使用，但保留作为参考）
        var baseofContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {% include 'partials/head' %}
            </head>
            <body>
            {% include 'partials/header' %}
            <main>
            {{ page.content }}
            </main>
            {% include 'partials/footer' %}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "baseof.liquid"), baseofContent);
        Console.WriteLine("  创建布局: layouts/_default/baseof.liquid");

        // single.liquid - 单页布局（完整独立模板）
        var singleContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {% include 'partials/head' %}
            </head>
            <body>
            {% include 'partials/header' %}
            <main>
            <article>
                <h1>{{ page.title }}</h1>
                <div class="meta">
                    {% if page.date %}
                    <time>{{ page.date }}</time>
                    {% endif %}
                    {% if page.reading_time.total_minutes %}
                    · {{ page.reading_time.total_minutes | round }} 分钟阅读
                    {% endif %}
                </div>
                <div class="content">
                    {{ page.content }}
                </div>
                {% if page.tags.size > 0 %}
                <div class="tags" style="margin-top: 30px;">
                    标签:
                    {% for tag in page.tags %}
                    <a href="/tags/{{ tag | downcase }}/">{{ tag }}</a>{% unless forloop.last %}, {% endunless %}
                    {% endfor %}
                </div>
                {% endif %}
            </article>
            </main>
            {% include 'partials/footer' %}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "single.liquid"), singleContent);
        Console.WriteLine("  创建布局: layouts/_default/single.liquid");

        // list.liquid - 列表布局
        var listContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {% include 'partials/head' %}
            </head>
            <body>
            {% include 'partials/header' %}
            <main>
            <h1>{{ page.title | default: "文章列表" }}</h1>
            <ul class="post-list">
                {% for post in site.regular_pages %}
                <li>
                    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
                    <div class="meta">
                        {% if post.date %}
                        <time>{{ post.date }}</time>
                        {% endif %}
                    </div>
                    {% if post.summary %}
                    <p>{{ post.summary }}</p>
                    {% endif %}
                </li>
                {% endfor %}
            </ul>
            </main>
            {% include 'partials/footer' %}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "list.liquid"), listContent);
        Console.WriteLine("  创建布局: layouts/_default/list.liquid");

        // taxonomy.liquid - 分类法列表布局
        var taxonomyContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {% include 'partials/head' %}
            </head>
            <body>
            {% include 'partials/header' %}
            <main>
            <h1>{{ page.title | default: "分类" }}</h1>
            <ul class="post-list">
                {% for term in page.terms %}
                <li>
                    <h2><a href="{{ term.permalink }}">{{ term.name }}</a> ({{ term.count }})</h2>
                </li>
                {% endfor %}
            </ul>
            </main>
            {% include 'partials/footer' %}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "taxonomy.liquid"), taxonomyContent);
        Console.WriteLine("  创建布局: layouts/_default/taxonomy.liquid");

        // term.liquid - 分类法术语页面布局
        var termContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {% include 'partials/head' %}
            </head>
            <body>
            {% include 'partials/header' %}
            <main>
            <h1>{{ page.title }}</h1>
            <ul class="post-list">
                {% for post in page.pages %}
                <li>
                    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
                    <div class="meta">
                        {% if post.date %}
                        <time>{{ post.date }}</time>
                        {% endif %}
                    </div>
                </li>
                {% endfor %}
            </ul>
            </main>
            {% include 'partials/footer' %}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "term.liquid"), termContent);
        Console.WriteLine("  创建布局: layouts/_default/term.liquid");

        // index.liquid - 首页布局（最新 5 篇经 slice filter 截取）
        var indexContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {% include 'partials/head' %}
            </head>
            <body>
            {% include 'partials/header' %}
            <main>
            <h1>欢迎来到 {{ site.title }}</h1>
            <p>{{ site.params.description }}</p>

            <h2 style="margin-top: 40px;">最新文章</h2>
            <ul class="post-list">
                {% assign recent = site.regular_pages | slice: 0, 5 %}
                {% for post in recent %}
                <li>
                    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
                    <div class="meta">
                        {% if post.date %}
                        <time>{{ post.date }}</time>
                        {% endif %}
                    </div>
                    {% if post.summary %}
                    <p>{{ post.summary }}</p>
                    {% endif %}
                </li>
                {% endfor %}
            </ul>
            </main>
            {% include 'partials/footer' %}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "index.liquid"), indexContent);
        Console.WriteLine("  创建布局: layouts/index.liquid");
    }

    private static void CreateScribanLayouts(string sitePath)
    {
        // head.html - 头部 partial
        var headContent = """
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>{{ if page.title }}{{ page.title }} | {{ end }}{{ site.title }}</title>
            <meta name="description" content="{{ page.description ?? site.params.description }}">
            <style>
                * { box-sizing: border-box; margin: 0; padding: 0; }
                body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; line-height: 1.6; color: #333; max-width: 800px; margin: 0 auto; padding: 20px; }
                header { border-bottom: 1px solid #eee; padding-bottom: 20px; margin-bottom: 30px; }
                header h1 { font-size: 1.5rem; }
                nav { margin-top: 10px; }
                nav a { margin-right: 15px; color: #0066cc; text-decoration: none; }
                nav a:hover { text-decoration: underline; }
                main { min-height: 60vh; }
                article { margin-bottom: 40px; }
                article h1 { font-size: 2rem; margin-bottom: 10px; }
                article .meta { color: #666; font-size: 0.9rem; margin-bottom: 20px; }
                article .content { line-height: 1.8; }
                article .content h2 { margin-top: 30px; margin-bottom: 15px; }
                article .content p { margin-bottom: 15px; }
                article .content code { background: #f5f5f5; padding: 2px 6px; border-radius: 3px; }
                article .content pre { background: #f5f5f5; padding: 15px; border-radius: 5px; overflow-x: auto; margin-bottom: 15px; }
                footer { border-top: 1px solid #eee; padding-top: 20px; margin-top: 40px; color: #666; font-size: 0.9rem; }
                .post-list { list-style: none; }
                .post-list li { margin-bottom: 20px; padding-bottom: 20px; border-bottom: 1px solid #eee; }
                .post-list h2 { font-size: 1.3rem; margin-bottom: 5px; }
                .post-list h2 a { color: #333; text-decoration: none; }
                .post-list h2 a:hover { color: #0066cc; }
                .post-list .meta { color: #666; font-size: 0.85rem; }
            </style>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "partials", "head.html"), headContent);
        Console.WriteLine("  创建布局: layouts/partials/head.html");

        // header.html - 页头 partial
        var headerContent = """
            <header>
                <h1><a href="/" style="color: inherit; text-decoration: none;">{{ site.title }}</a></h1>
                <nav>
                    {{ for item in site.menus.main }}
                    <a href="{{ item.url }}">{{ item.name }}</a>
                    {{ end }}
                </nav>
            </header>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "partials", "header.html"), headerContent);
        Console.WriteLine("  创建布局: layouts/partials/header.html");

        // footer.html - 页脚 partial
        var footerContent = """
            <footer>
                <p>&copy; {{ date.now | date.to_string "%Y" }} {{ site.title }}. Powered by <a href="https://github.com/yuebo119/Flint">Flint</a>.</p>
            </footer>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "partials", "footer.html"), footerContent);
        Console.WriteLine("  创建布局: layouts/partials/footer.html");

        // baseof.html - 基础布局（不再使用，但保留作为参考）
        var baseofContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {{ include "partials/head.html" }}
            </head>
            <body>
            {{ include "partials/header.html" }}
            <main>
            {{ body }}
            </main>
            {{ include "partials/footer.html" }}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "baseof.html"), baseofContent);
        Console.WriteLine("  创建布局: layouts/_default/baseof.html");

        // single.html - 单页布局（完整独立模板）
        var singleContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {{ include "partials/head.html" }}
            </head>
            <body>
            {{ include "partials/header.html" }}
            <main>
            <article>
                <h1>{{ page.title }}</h1>
                <div class="meta">
                    {{ if page.date }}
                    <time>{{ page.date }}</time>
                    {{ end }}
                    {{ if page.reading_time }}
                    · {{ page.reading_time.total_minutes | math.round }} 分钟阅读
                    {{ end }}
                </div>
                <div class="content">
                    {{ page.content }}
                </div>
                {{ if page.tags && page.tags.size > 0 }}
                <div class="tags" style="margin-top: 30px;">
                    标签: 
                    {{ for tag in page.tags }}
                    <a href="/tags/{{ tag | string.downcase }}/">{{ tag }}</a>{{ if !for.last }}, {{ end }}
                    {{ end }}
                </div>
                {{ end }}
            </article>
            </main>
            {{ include "partials/footer.html" }}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "single.html"), singleContent);
        Console.WriteLine("  创建布局: layouts/_default/single.html");

        // list.html - 列表布局
        var listContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {{ include "partials/head.html" }}
            </head>
            <body>
            {{ include "partials/header.html" }}
            <main>
            <h1>{{ page.title ?? "文章列表" }}</h1>
            <ul class="post-list">
                {{ for post in site.regular_pages }}
                <li>
                    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
                    <div class="meta">
                        {{ if post.date }}
                        <time>{{ post.date }}</time>
                        {{ end }}
                    </div>
                    {{ if post.summary }}
                    <p>{{ post.summary }}</p>
                    {{ end }}
                </li>
                {{ end }}
            </ul>
            </main>
            {{ include "partials/footer.html" }}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "list.html"), listContent);
        Console.WriteLine("  创建布局: layouts/_default/list.html");

        // taxonomy.html - 分类法列表布局
        var taxonomyContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {{ include "partials/head.html" }}
            </head>
            <body>
            {{ include "partials/header.html" }}
            <main>
            <h1>{{ page.title ?? "分类" }}</h1>
            <ul class="post-list">
                {{ for term in page.terms }}
                <li>
                    <h2><a href="{{ term.permalink }}">{{ term.name }}</a> ({{ term.count }})</h2>
                </li>
                {{ end }}
            </ul>
            </main>
            {{ include "partials/footer.html" }}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "taxonomy.html"), taxonomyContent);
        Console.WriteLine("  创建布局: layouts/_default/taxonomy.html");

        // term.html - 分类法术语页面布局
        var termContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {{ include "partials/head.html" }}
            </head>
            <body>
            {{ include "partials/header.html" }}
            <main>
            <h1>{{ page.title }}</h1>
            <ul class="post-list">
                {{ for post in page.pages }}
                <li>
                    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
                    <div class="meta">
                        {{ if post.date }}
                        <time>{{ post.date }}</time>
                        {{ end }}
                    </div>
                </li>
                {{ end }}
            </ul>
            </main>
            {{ include "partials/footer.html" }}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "_default", "term.html"), termContent);
        Console.WriteLine("  创建布局: layouts/_default/term.html");

        // index.html - 首页布局
        var indexContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
            {{ include "partials/head.html" }}
            </head>
            <body>
            {{ include "partials/header.html" }}
            <main>
            <h1>欢迎来到 {{ site.title }}</h1>
            <p>{{ site.params.description }}</p>
            
            <h2 style="margin-top: 40px;">最新文章</h2>
            <ul class="post-list">
                {{ for post in site.regular_pages | array.limit 5 }}
                <li>
                    <h2><a href="{{ post.permalink }}">{{ post.title }}</a></h2>
                    <div class="meta">
                        {{ if post.date }}
                        <time>{{ post.date }}</time>
                        {{ end }}
                    </div>
                    {{ if post.summary }}
                    <p>{{ post.summary }}</p>
                    {{ end }}
                </li>
                {{ end }}
            </ul>
            </main>
            {{ include "partials/footer.html" }}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(sitePath, "layouts", "index.html"), indexContent);
        Console.WriteLine("  创建布局: layouts/index.html");
    }

    private static void CreateWelcomeContent(string sitePath)
    {
        var welcomeContent = $"""
            +++
            title = "欢迎使用 Flint"
            date = {DateTime.Now:yyyy-MM-ddTHH:mm:sszzz}
            draft = false
            tags = ["Flint", "静态站点"]
            categories = ["入门"]
            +++

            欢迎使用 Flint 静态站点生成器！

            ## 快速开始

            1. 编辑 `Flint.toml` 配置文件
            2. 在 `content/` 目录下创建内容
            3. 运行 `Flint serve` 预览站点
            4. 运行 `Flint build` 构建站点

            ## 创建新内容

            使用以下命令创建新文章：

            ```bash
            Flint new content posts/my-first-post.md
            ```

            ## 了解更多

            - 查看 `layouts/` 目录了解模板系统
            - 查看 `assets/` 目录管理样式和脚本
            - 使用 `Flint mod get` 安装主题

            祝你使用愉快！
            """;

        var postsDir = Path.Combine(sitePath, "content", "posts");
        Directory.CreateDirectory(postsDir);
        File.WriteAllText(Path.Combine(postsDir, "welcome.md"), welcomeContent);
        Console.WriteLine("  创建内容: content/posts/welcome.md");

        // 创建关于页面
        var aboutContent = $"""
            +++
            title = "关于"
            date = {DateTime.Now:yyyy-MM-ddTHH:mm:sszzz}
            draft = false
            +++

            这是关于页面。

            ## 关于本站

            本站使用 [Flint](https://github.com/yuebo119/Flint) 静态站点生成器构建。

            Flint 是一个使用 .NET 10 开发的高性能静态站点生成器，具有以下特点：

            - 🚀 极速构建 - 比 Hugo 快 3-5 倍
            - 🔒 类型安全 - 编译时错误检查
            - 📦 Native AOT - 独立可执行文件
            - 🎨 灵活模板 - Scriban 模板引擎
            """;

        File.WriteAllText(Path.Combine(sitePath, "content", "about.md"), aboutContent);
        Console.WriteLine("  创建内容: content/about.md");
    }
}
