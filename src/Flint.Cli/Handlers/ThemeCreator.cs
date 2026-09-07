// Flint 静态站点生成器
// 主题创建器

namespace Flint.Cli;

/// <summary>
/// 主题创建器
/// </summary>
internal static class ThemeCreator
{
    /// <summary>
    /// 创建新主题
    /// </summary>
    /// <returns>0 表示成功，1 表示失败</returns>
    public static int CreateNewTheme(string name)
    {
        // 验证主题名称
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine("错误: 主题名称不能为空");
            Console.ResetColor();
            return 1;
        }

        // 名称是路径段：拒绝分隔符与父目录引用，防在 cwd 之外创建/覆盖目录
        if (name.Contains('/') || name.Contains('\\') || name is "." or "..")
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"错误: 主题名称不能包含路径分隔符或父目录引用: {name}");
            Console.ResetColor();
            return 1;
        }

        var currentDir = Directory.GetCurrentDirectory();
        var themesDir = Path.Combine(currentDir, "themes");
        var themePath = Path.Combine(themesDir, name);

        // 检查是否在 Flint 站点目录中
        if (!Directory.Exists(themesDir))
        {
            // 如果不在站点目录中，在当前目录创建主题
            themePath = Path.Combine(currentDir, name);
        }

        if (Directory.Exists(themePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"错误: 主题目录 '{name}' 已存在");
            Console.ResetColor();
            return 1;
        }

        try
        {
            Console.WriteLine($"创建主题: {name}");

            // 创建主题目录结构
            var directories = new[]
            {
                "",
                "archetypes",
                "assets",
                "assets/css",
                "assets/js",
                "i18n",
                "layouts",
                "layouts/_default",
                "layouts/partials",
                "layouts/shortcodes",
                "static",
                "static/images"
            };

            foreach (var dir in directories)
            {
                var dirPath = Path.Combine(themePath, dir);
                Directory.CreateDirectory(dirPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Console.WriteLine($"  创建目录: {dir}/");
                }
            }

            // 创建主题配置文件
            CreateThemeConfig(themePath, name);

            // 创建基础布局
            CreateThemeLayouts(themePath, name);

            // 创建样式文件
            CreateThemeStyles(themePath);

            // 创建 README
            CreateThemeReadme(themePath, name);

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"主题 '{name}' 创建成功！");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("要使用此主题，请在 Flint.toml 中添加:");
            Console.WriteLine($"  theme = \"{name}\"");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"创建主题失败: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void CreateThemeConfig(string themePath, string name)
    {
        var configContent = $"""
            # 主题配置文件

            name = "{name}"
            version = "1.0.0"
            description = "A Flint theme"
            license = "MIT"
            homepage = ""

            [author]
              name = ""
              homepage = ""

            # 主题参数
            [params]
              # 在这里定义主题特定的参数
              colorScheme = "light"
              showToc = true
              showReadingTime = true

            # 主题支持的功能
            [features]
              darkMode = true
              search = false
              comments = false
            """;

        File.WriteAllText(Path.Combine(themePath, "theme.toml"), configContent);
        Console.WriteLine("  创建配置: theme.toml");
    }

    private static void CreateThemeLayouts(string themePath, string name)
    {
        // baseof.html - 不使用字符串插值，因为模板语法使用 {{ }}
        var baseofContent = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{ if page.title }}{{ page.title }} | {{ end }}{{ site.title }}</title>
                <meta name="description" content="{{ page.description | default site.params.description }}">
                {{ include "partials/head.html" }}
            </head>
            <body>
                {{ include "partials/header.html" }}
                <main class="main">
                    {{ content }}
                </main>
                {{ include "partials/footer.html" }}
            </body>
            </html>
            """;

        File.WriteAllText(Path.Combine(themePath, "layouts", "_default", "baseof.html"), baseofContent);
        Console.WriteLine("  创建布局: layouts/_default/baseof.html");

        // single.html
        var singleContent = """
            {{ extends "baseof.html" }}
            {{ block content }}
            <article class="post">
                <header class="post-header">
                    <h1 class="post-title">{{ page.title }}</h1>
                    <div class="post-meta">
                        {{ if page.date }}
                        <time datetime="{{ page.date | date.to_string "%Y-%m-%d" }}">
                            {{ page.date | date.to_string "%Y年%m月%d日" }}
                        </time>
                        {{ end }}
                        {{ if page.reading_time }}
                        <span class="reading-time">{{ page.reading_time.total_minutes | math.round }} 分钟阅读</span>
                        {{ end }}
                    </div>
                </header>
                <div class="post-content">
                    {{ page.content }}
                </div>
                {{ if page.tags && page.tags.size > 0 }}
                <footer class="post-footer">
                    <div class="post-tags">
                        {{ for tag in page.tags }}
                        <a href="/tags/{{ tag | string.downcase }}/" class="tag">{{ tag }}</a>
                        {{ end }}
                    </div>
                </footer>
                {{ end }}
            </article>
            {{ end }}
            """;

        File.WriteAllText(Path.Combine(themePath, "layouts", "_default", "single.html"), singleContent);
        Console.WriteLine("  创建布局: layouts/_default/single.html");

        // list.html
        var listContent = """
            {{ extends "baseof.html" }}
            {{ block content }}
            <div class="list-page">
                <h1 class="list-title">{{ page.title | default "文章" }}</h1>
                <ul class="post-list">
                    {{ for post in site.regular_pages }}
                    <li class="post-item">
                        <article>
                            <h2 class="post-item-title">
                                <a href="{{ post.permalink }}">{{ post.title }}</a>
                            </h2>
                            <div class="post-item-meta">
                                {{ if post.date }}
                                <time>{{ post.date | date.to_string "%Y-%m-%d" }}</time>
                                {{ end }}
                            </div>
                            {{ if post.summary }}
                            <p class="post-item-summary">{{ post.summary }}</p>
                            {{ end }}
                        </article>
                    </li>
                    {{ end }}
                </ul>
            </div>
            {{ end }}
            """;

        File.WriteAllText(Path.Combine(themePath, "layouts", "_default", "list.html"), listContent);
        Console.WriteLine("  创建布局: layouts/_default/list.html");

        // partials/head.html
        var headContent = """
            <link rel="stylesheet" href="/css/style.css">
            {{ if site.params.customCss }}
            <link rel="stylesheet" href="{{ site.params.customCss }}">
            {{ end }}
            """;

        File.WriteAllText(Path.Combine(themePath, "layouts", "partials", "head.html"), headContent);
        Console.WriteLine("  创建部件: layouts/partials/head.html");

        // partials/header.html
        var headerContent = """
            <header class="site-header">
                <div class="container">
                    <a href="/" class="site-title">{{ site.title }}</a>
                    <nav class="site-nav">
                        {{ for item in site.menus.main }}
                        <a href="{{ item.url }}" class="nav-link{{ if item.is_active }} active{{ end }}">
                            {{ item.name }}
                        </a>
                        {{ end }}
                    </nav>
                </div>
            </header>
            """;

        File.WriteAllText(Path.Combine(themePath, "layouts", "partials", "header.html"), headerContent);
        Console.WriteLine("  创建部件: layouts/partials/header.html");

        // partials/footer.html
        var footerContent = """
            <footer class="site-footer">
                <div class="container">
                    <p>&copy; {{ date.now | date.to_string "%Y" }} {{ site.title }}. 
                       Powered by <a href="https://github.com/yuebo119/Flint">Flint</a>.</p>
                </div>
            </footer>
            """;

        File.WriteAllText(Path.Combine(themePath, "layouts", "partials", "footer.html"), footerContent);
        Console.WriteLine("  创建部件: layouts/partials/footer.html");
    }

    private static void CreateThemeStyles(string themePath)
    {
        var styleContent = """
            /* 主题样式 */

            :root {
                --color-bg: #ffffff;
                --color-text: #333333;
                --color-primary: #0066cc;
                --color-secondary: #666666;
                --color-border: #eeeeee;
                --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                --font-mono: "SF Mono", Monaco, "Cascadia Code", monospace;
                --max-width: 800px;
            }

            * {
                box-sizing: border-box;
                margin: 0;
                padding: 0;
            }

            body {
                font-family: var(--font-sans);
                line-height: 1.6;
                color: var(--color-text);
                background: var(--color-bg);
            }

            .container {
                max-width: var(--max-width);
                margin: 0 auto;
                padding: 0 20px;
            }

            /* Header */
            .site-header {
                border-bottom: 1px solid var(--color-border);
                padding: 20px 0;
            }

            .site-header .container {
                display: flex;
                justify-content: space-between;
                align-items: center;
            }

            .site-title {
                font-size: 1.5rem;
                font-weight: bold;
                color: var(--color-text);
                text-decoration: none;
            }

            .site-nav .nav-link {
                margin-left: 20px;
                color: var(--color-secondary);
                text-decoration: none;
            }

            .site-nav .nav-link:hover,
            .site-nav .nav-link.active {
                color: var(--color-primary);
            }

            /* Main */
            .main {
                min-height: calc(100vh - 200px);
                padding: 40px 20px;
                max-width: var(--max-width);
                margin: 0 auto;
            }

            /* Post */
            .post-header {
                margin-bottom: 30px;
            }

            .post-title {
                font-size: 2rem;
                margin-bottom: 10px;
            }

            .post-meta {
                color: var(--color-secondary);
                font-size: 0.9rem;
            }

            .post-meta > * + *::before {
                content: " · ";
            }

            .post-content {
                line-height: 1.8;
            }

            .post-content h2 {
                margin-top: 40px;
                margin-bottom: 20px;
            }

            .post-content p {
                margin-bottom: 20px;
            }

            .post-content code {
                font-family: var(--font-mono);
                background: #f5f5f5;
                padding: 2px 6px;
                border-radius: 3px;
                font-size: 0.9em;
            }

            .post-content pre {
                background: #f5f5f5;
                padding: 20px;
                border-radius: 5px;
                overflow-x: auto;
                margin-bottom: 20px;
            }

            .post-content pre code {
                background: none;
                padding: 0;
            }

            .post-footer {
                margin-top: 40px;
                padding-top: 20px;
                border-top: 1px solid var(--color-border);
            }

            .post-tags .tag {
                display: inline-block;
                background: #f0f0f0;
                padding: 4px 12px;
                border-radius: 20px;
                margin-right: 8px;
                font-size: 0.85rem;
                color: var(--color-secondary);
                text-decoration: none;
            }

            .post-tags .tag:hover {
                background: var(--color-primary);
                color: white;
            }

            /* Post List */
            .post-list {
                list-style: none;
            }

            .post-item {
                padding: 20px 0;
                border-bottom: 1px solid var(--color-border);
            }

            .post-item-title {
                font-size: 1.3rem;
                margin-bottom: 5px;
            }

            .post-item-title a {
                color: var(--color-text);
                text-decoration: none;
            }

            .post-item-title a:hover {
                color: var(--color-primary);
            }

            .post-item-meta {
                color: var(--color-secondary);
                font-size: 0.85rem;
                margin-bottom: 10px;
            }

            .post-item-summary {
                color: var(--color-secondary);
            }

            /* Footer */
            .site-footer {
                border-top: 1px solid var(--color-border);
                padding: 20px 0;
                text-align: center;
                color: var(--color-secondary);
                font-size: 0.9rem;
            }

            .site-footer a {
                color: var(--color-primary);
                text-decoration: none;
            }
            """;

        File.WriteAllText(Path.Combine(themePath, "assets", "css", "style.css"), styleContent);
        Console.WriteLine("  创建样式: assets/css/style.css");

        // 同时复制到 static 目录
        var staticCssDir = Path.Combine(themePath, "static", "css");
        Directory.CreateDirectory(staticCssDir);
        File.WriteAllText(Path.Combine(staticCssDir, "style.css"), styleContent);
    }

    private static void CreateThemeReadme(string themePath, string name)
    {
        var readmeContent = $"""
            # {name}

            A theme for [Flint](https://github.com/yuebo119/Flint) static site generator.

            ## Installation

            ```bash
            cd your-site
            Flint mod get https://github.com/your-username/{name}
            ```

            Or manually clone to `themes/{name}`:

            ```bash
            git clone https://github.com/your-username/{name} themes/{name}
            ```

            ## Configuration

            Add to your `Flint.toml`:

            ```toml
            theme = "{name}"
            ```

            ## Features

            - Responsive design
            - Clean typography
            - Tag support
            - Reading time estimation

            ## Customization

            Override any template by creating the same file in your site's `layouts/` directory.

            ## License

            MIT License
            """;

        File.WriteAllText(Path.Combine(themePath, "README.md"), readmeContent);
        Console.WriteLine("  创建文档: README.md");
    }
}
