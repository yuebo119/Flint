// Flint 静态站点生成器
// 测试站点夹具实现

using System.Collections.Concurrent;
using System.Text;
using Flint.Core.Abstractions;
using Flint.Core.Assets;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
using Flint.IntegrationTests.TestData;
using Xunit;

namespace Flint.IntegrationTests.Fixtures;

/// <summary>
/// 测试站点夹具
/// 提供创建和管理临时测试站点的功能
/// 实现 IAsyncLifetime 以支持异步初始化和清理
/// </summary>
/// <remarks>
/// 满足需求：
/// - Requirements 10.1: 提供创建临时测试站点的测试夹具
/// - Requirements 10.6: 测试完成后自动清理临时文件和目录
/// </remarks>
public sealed class TestSiteFixture : IAsyncLifetime, IDisposable
{
    #region 私有字段

    private readonly string _testId;
    private readonly ConcurrentDictionary<string, byte[]> _fileSnapshots;
    private bool _disposed;
    private string? _siteRoot;
    private string? _outputPath;
    private SiteConfig? _config;
    private ISiteBuilder? _siteBuilder;

    #endregion

    #region 公共属性

    /// <summary>
    /// 测试站点根目录
    /// </summary>
    public string SiteRoot => _siteRoot ?? throw new InvalidOperationException("站点尚未创建，请先调用 CreateSiteAsync");

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputPath => _outputPath ?? throw new InvalidOperationException("站点尚未创建，请先调用 CreateSiteAsync");

    /// <summary>
    /// 站点配置
    /// </summary>
    public SiteConfig Config => _config ?? throw new InvalidOperationException("站点尚未创建，请先调用 CreateSiteAsync");

    /// <summary>
    /// 测试唯一标识符
    /// </summary>
    public string TestId => _testId;

    /// <summary>
    /// 站点是否已创建
    /// </summary>
    public bool IsCreated => _siteRoot != null;

    #endregion

    #region 构造函数

    /// <summary>
    /// 创建测试站点夹具
    /// </summary>
    public TestSiteFixture()
    {
        _testId = Guid.NewGuid().ToString("N")[..8];
        _fileSnapshots = new ConcurrentDictionary<string, byte[]>();
    }

    #endregion

    #region IAsyncLifetime 实现

    /// <summary>
    /// 异步初始化
    /// </summary>
    public Task InitializeAsync()
    {
        // 初始化时不创建站点，等待显式调用 CreateSiteAsync
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步清理
    /// </summary>
    public async Task DisposeAsync()
    {
        await CleanupAsync();
    }

    #endregion

    #region 站点创建方法

    /// <summary>
    /// 创建测试站点
    /// </summary>
    /// <param name="template">站点模板名称（default、minimal、complete、multilingual）</param>
    /// <returns>测试站点路径</returns>
    public async ValueTask<string> CreateSiteAsync(string template = "default")
    {
        ThrowIfDisposed();

        // 创建临时目录
        _siteRoot = Path.Combine(
            Path.GetTempPath(),
            "Flint-tests",
            $"site_{_testId}_{template}");

        _outputPath = Path.Combine(_siteRoot, "public");

        // 确保目录存在
        Directory.CreateDirectory(_siteRoot);
        Directory.CreateDirectory(_outputPath);

        // 根据模板创建站点
        await CreateSiteFromTemplateAsync(template);

        // 加载配置
        await LoadConfigAsync();

        // 创建站点构建器
        CreateSiteBuilder();

        return _siteRoot;
    }

    /// <summary>
    /// 从预定义模板创建站点
    /// </summary>
    /// <param name="templatePath">模板目录路径</param>
    /// <returns>测试站点路径</returns>
    public async ValueTask<string> CreateFromTemplateAsync(string templatePath)
    {
        ThrowIfDisposed();

        if (!Directory.Exists(templatePath))
        {
            throw new DirectoryNotFoundException($"模板目录不存在: {templatePath}");
        }

        // 创建临时目录
        _siteRoot = Path.Combine(
            Path.GetTempPath(),
            "Flint-tests",
            $"site_{_testId}_custom");

        _outputPath = Path.Combine(_siteRoot, "public");

        // 复制模板目录
        TestDataPaths.CopyDirectory(templatePath, _siteRoot);

        // 加载配置
        await LoadConfigAsync();

        // 创建站点构建器
        CreateSiteBuilder();

        return _siteRoot;
    }

    #endregion

    #region 文件添加方法

    /// <summary>
    /// 添加内容文件
    /// </summary>
    /// <param name="relativePath">相对路径（相对于 content 目录）</param>
    /// <param name="content">文件内容</param>
    public async ValueTask AddContentAsync(string relativePath, string content)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var contentDir = Path.Combine(_siteRoot!, "content");
        var fullPath = Path.Combine(contentDir, relativePath);

        await WriteFileAsync(fullPath, content);
    }

    /// <summary>
    /// 添加模板文件
    /// </summary>
    /// <param name="relativePath">相对路径（相对于 layouts 目录）</param>
    /// <param name="content">模板内容</param>
    public async ValueTask AddTemplateAsync(string relativePath, string content)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var layoutsDir = Path.Combine(_siteRoot!, "layouts");
        var fullPath = Path.Combine(layoutsDir, relativePath);

        await WriteFileAsync(fullPath, content);

        // 重新创建站点构建器以加载新模板
        CreateSiteBuilder();
    }

    /// <summary>
    /// 添加资源文件
    /// </summary>
    /// <param name="relativePath">相对路径（相对于 assets 目录）</param>
    /// <param name="content">文件内容</param>
    public async ValueTask AddAssetAsync(string relativePath, byte[] content)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var assetsDir = Path.Combine(_siteRoot!, "assets");
        var fullPath = Path.Combine(assetsDir, relativePath);

        await WriteBinaryFileAsync(fullPath, content);
    }

    /// <summary>
    /// 添加静态文件
    /// </summary>
    /// <param name="relativePath">相对路径（相对于 static 目录）</param>
    /// <param name="content">文件内容</param>
    public async ValueTask AddStaticAsync(string relativePath, byte[] content)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var staticDir = Path.Combine(_siteRoot!, "static");
        var fullPath = Path.Combine(staticDir, relativePath);

        await WriteBinaryFileAsync(fullPath, content);
    }

    /// <summary>
    /// 添加配置文件
    /// </summary>
    /// <param name="content">配置内容</param>
    /// <param name="format">配置格式（toml、yaml、json）</param>
    public async ValueTask SetConfigAsync(string content, string format = "toml")
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var configFileName = format.ToLowerInvariant() switch
        {
            "toml" => "Flint.toml",
            "yaml" or "yml" => "Flint.yaml",
            "json" => "Flint.json",
            _ => throw new ArgumentException($"不支持的配置格式: {format}", nameof(format))
        };

        var fullPath = Path.Combine(_siteRoot!, configFileName);
        await WriteFileAsync(fullPath, content);

        // 尝试重新加载配置（如果配置无效，忽略错误，让 CLI 测试来验证）
        try
        {
            await LoadConfigAsync();
            // 重新创建站点构建器
            CreateSiteBuilder();
        }
        catch
        {
            // 配置无效时忽略错误，这允许测试无效配置的场景
        }
    }

    #endregion

    #region 构建方法

    /// <summary>
    /// 执行构建
    /// </summary>
    /// <param name="options">构建选项</param>
    /// <returns>构建结果</returns>
    public async ValueTask<BuildResult> BuildAsync(BuildOptions? options = null)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var buildOptions = options ?? new BuildOptions { SourcePath = _siteRoot!, OutputPath = _outputPath! };

        // 确保使用正确的路径
        if (buildOptions.SourcePath != _siteRoot || buildOptions.OutputPath != _outputPath)
        {
            buildOptions = new BuildOptions
            {
                SourcePath = _siteRoot!,
                OutputPath = _outputPath!,
                Minify = options?.Minify ?? false,
                IncludeDrafts = options?.IncludeDrafts ?? false,
                IncludeFuture = options?.IncludeFuture ?? false,
                IncludeExpired = options?.IncludeExpired ?? false,
                Environment = options?.Environment,
                Parallelism = options?.Parallelism ?? Environment.ProcessorCount,
                EnableCache = options?.EnableCache ?? true,
                CachePath = options?.CachePath,
                CleanOutput = options?.CleanOutput ?? true,
                GenerateSourceMaps = options?.GenerateSourceMaps ?? false,
                BaseUrl = options?.BaseUrl,
                Verbose = options?.Verbose ?? false,
                Debug = options?.Debug ?? false
            };
        }

        return await _siteBuilder!.BuildAsync(buildOptions);
    }

    /// <summary>
    /// 执行增量构建
    /// </summary>
    /// <param name="changedFiles">变化的文件列表</param>
    /// <param name="options">构建选项</param>
    /// <returns>构建结果</returns>
    public async ValueTask<BuildResult> IncrementalBuildAsync(
        IReadOnlyList<string> changedFiles,
        BuildOptions? options = null)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var buildOptions = options ?? new BuildOptions { SourcePath = _siteRoot!, OutputPath = _outputPath! };

        return await _siteBuilder!.IncrementalBuildAsync(buildOptions, changedFiles);
    }

    /// <summary>
    /// 清理输出目录
    /// </summary>
    public async ValueTask CleanOutputAsync()
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        await _siteBuilder!.CleanAsync(_outputPath!);
    }

    #endregion

    #region 文件系统快照和比较

    /// <summary>
    /// 创建文件系统快照
    /// </summary>
    /// <param name="snapshotName">快照名称</param>
    /// <returns>快照中的文件数量</returns>
    public async ValueTask<int> CreateSnapshotAsync(string snapshotName = "default")
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var files = Directory.EnumerateFiles(_outputPath!, "*", SearchOption.AllDirectories);
        var count = 0;

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(_outputPath!, file);
            var key = $"{snapshotName}:{relativePath}";
            var content = await File.ReadAllBytesAsync(file);
            _fileSnapshots[key] = content;
            count++;
        }

        return count;
    }

    /// <summary>
    /// 比较当前输出与快照
    /// </summary>
    /// <param name="snapshotName">快照名称</param>
    /// <returns>比较结果</returns>
    public async ValueTask<SnapshotComparisonResult> CompareWithSnapshotAsync(string snapshotName = "default")
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var added = new List<string>();
        var removed = new List<string>();
        var modified = new List<string>();
        var unchanged = new List<string>();

        // 获取快照中的文件
        var snapshotFiles = _fileSnapshots
            .Where(kv => kv.Key.StartsWith($"{snapshotName}:"))
            .ToDictionary(
                kv => kv.Key[($"{snapshotName}:".Length)..],
                kv => kv.Value);

        // 获取当前输出目录中的文件
        var currentFiles = new Dictionary<string, byte[]>();
        if (Directory.Exists(_outputPath))
        {
            foreach (var file in Directory.EnumerateFiles(_outputPath!, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(_outputPath!, file);
                currentFiles[relativePath] = await File.ReadAllBytesAsync(file);
            }
        }

        // 比较文件
        foreach (var (path, content) in currentFiles)
        {
            if (snapshotFiles.TryGetValue(path, out var snapshotContent))
            {
                if (content.SequenceEqual(snapshotContent))
                {
                    unchanged.Add(path);
                }
                else
                {
                    modified.Add(path);
                }
            }
            else
            {
                added.Add(path);
            }
        }

        // 查找已删除的文件
        foreach (var path in snapshotFiles.Keys)
        {
            if (!currentFiles.ContainsKey(path))
            {
                removed.Add(path);
            }
        }

        return new SnapshotComparisonResult
        {
            SnapshotName = snapshotName,
            AddedFiles = added,
            RemovedFiles = removed,
            ModifiedFiles = modified,
            UnchangedFiles = unchanged
        };
    }

    /// <summary>
    /// 获取输出文件内容
    /// </summary>
    /// <param name="relativePath">相对路径</param>
    /// <returns>文件内容</returns>
    public async ValueTask<string?> GetOutputFileAsync(string relativePath)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var fullPath = Path.Combine(_outputPath!, relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        return await File.ReadAllTextAsync(fullPath);
    }

    /// <summary>
    /// 获取输出文件二进制内容
    /// </summary>
    /// <param name="relativePath">相对路径</param>
    /// <returns>文件内容</returns>
    public async ValueTask<byte[]?> GetOutputFileBytesAsync(string relativePath)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var fullPath = Path.Combine(_outputPath!, relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(fullPath);
    }

    /// <summary>
    /// 检查输出文件是否存在
    /// </summary>
    /// <param name="relativePath">相对路径</param>
    /// <returns>是否存在</returns>
    public bool OutputFileExists(string relativePath)
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        var fullPath = Path.Combine(_outputPath!, relativePath);
        return File.Exists(fullPath);
    }

    /// <summary>
    /// 获取所有输出文件
    /// </summary>
    /// <returns>输出文件相对路径列表</returns>
    public IReadOnlyList<string> GetOutputFiles()
    {
        ThrowIfDisposed();
        EnsureSiteCreated();

        if (!Directory.Exists(_outputPath))
        {
            return [];
        }

        return Directory.EnumerateFiles(_outputPath!, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_outputPath!, f))
            .ToList();
    }

    #endregion

    #region IDisposable 实现

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CleanupAsync().GetAwaiter().GetResult();
        _disposed = true;
    }

    #endregion

    #region 私有方法

    private async Task CreateSiteFromTemplateAsync(string template)
    {
        var templateName = template.ToLowerInvariant() switch
        {
            "default" or "minimal" => "Minimal",
            "complete" or "full" => "Complete",
            "multilingual" or "i18n" => "Multilingual",
            _ => throw new ArgumentException($"未知的站点模板: {template}", nameof(template))
        };

        var templatePath = Path.Combine(TestDataPaths.Sites, templateName);

        if (Directory.Exists(templatePath))
        {
            // 从预定义模板复制
            TestDataPaths.CopyDirectory(templatePath, _siteRoot!);
        }
        else
        {
            // 创建最小站点结构
            await CreateMinimalSiteAsync();
        }
    }

    private async Task CreateMinimalSiteAsync()
    {
        // 创建目录结构
        Directory.CreateDirectory(Path.Combine(_siteRoot!, "content"));
        Directory.CreateDirectory(Path.Combine(_siteRoot!, "layouts"));
        Directory.CreateDirectory(Path.Combine(_siteRoot!, "assets"));
        Directory.CreateDirectory(Path.Combine(_siteRoot!, "static"));

        // 创建默认配置
        var configContent = """
            # Flint 测试站点配置
            baseURL = "http://localhost:1313/"
            title = "测试站点"
            languageCode = "zh-cn"

            [build]
              publishDir = "public"

            [taxonomies]
              category = "categories"
              tag = "tags"
            """;
        await WriteFileAsync(Path.Combine(_siteRoot!, "Flint.toml"), configContent);

        // 创建默认首页内容
        var indexContent = """
            +++
            title = "首页"
            date = 2024-01-01T00:00:00+08:00
            draft = false
            +++

            欢迎来到测试站点！
            """;
        await WriteFileAsync(Path.Combine(_siteRoot!, "content", "index.md"), indexContent);

        // 创建默认模板
        var defaultTemplate = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{ page.title }} | {{ site.title }}</title>
            </head>
            <body>
                <header>
                    <h1>{{ site.title }}</h1>
                </header>
                <main>
                    <article>
                        <h1>{{ page.title }}</h1>
                        <div class="content">
                            {{ page.content }}
                        </div>
                    </article>
                </main>
                <footer>
                    <p>&copy; {{ date.now | date.to_string "%Y" }} {{ site.title }}</p>
                </footer>
            </body>
            </html>
            """;
        await WriteFileAsync(Path.Combine(_siteRoot!, "layouts", "default.html"), defaultTemplate);

        // 创建 single 模板
        var singleTemplate = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{ page.title }} | {{ site.title }}</title>
            </head>
            <body>
                <header>
                    <h1>{{ site.title }}</h1>
                </header>
                <main>
                    <article>
                        <h1>{{ page.title }}</h1>
                        <div class="content">
                            {{ page.content }}
                        </div>
                    </article>
                </main>
            </body>
            </html>
            """;
        Directory.CreateDirectory(Path.Combine(_siteRoot!, "layouts", "_default"));
        await WriteFileAsync(Path.Combine(_siteRoot!, "layouts", "_default", "single.html"), singleTemplate);

        // 创建 taxonomy 模板（用于分类和标签页面）
        var taxonomyTemplate = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{ page.title }} | {{ site.title }}</title>
            </head>
            <body>
                <header>
                    <h1>{{ site.title }}</h1>
                </header>
                <main>
                    <h2>{{ page.title }}</h2>
                    <ul>
                    {{ for item in page.pages }}
                        <li><a href="{{ item.permalink }}">{{ item.title }}</a></li>
                    {{ end }}
                    </ul>
                </main>
            </body>
            </html>
            """;
        await WriteFileAsync(Path.Combine(_siteRoot!, "layouts", "_default", "taxonomy.html"), taxonomyTemplate);

        // 创建 list 模板（用于列表页面）
        var listTemplate = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{ page.title }} | {{ site.title }}</title>
            </head>
            <body>
                <header>
                    <h1>{{ site.title }}</h1>
                </header>
                <main>
                    <h2>{{ page.title }}</h2>
                    <ul>
                    {{ for item in page.pages }}
                        <li><a href="{{ item.permalink }}">{{ item.title }}</a></li>
                    {{ end }}
                    </ul>
                </main>
            </body>
            </html>
            """;
        await WriteFileAsync(Path.Combine(_siteRoot!, "layouts", "_default", "list.html"), listTemplate);

        // 创建 index 模板
        var indexTemplate = """
            <!DOCTYPE html>
            <html lang="{{ site.language }}">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{ site.title }}</title>
            </head>
            <body>
                <header>
                    <h1>{{ site.title }}</h1>
                </header>
                <main>
                    <h2>最新文章</h2>
                    <ul>
                    {{ for page in site.regular_pages }}
                        <li><a href="{{ page.permalink }}">{{ page.title }}</a></li>
                    {{ end }}
                    </ul>
                </main>
            </body>
            </html>
            """;
        await WriteFileAsync(Path.Combine(_siteRoot!, "layouts", "index.html"), indexTemplate);
    }

    private async Task LoadConfigAsync()
    {
        var configLoader = new ConfigLoader();

        // 尝试加载配置文件
        var configPath = FindConfigFile();
        if (configPath != null)
        {
            _config = await configLoader.LoadAsync(configPath);
        }
        else
        {
            // 使用默认配置
            _config = new SiteConfig
            {
                BaseURL = "http://localhost:1313/",
                Title = "测试站点",
                LanguageCode = "zh-cn"
            };
        }
    }

    private string? FindConfigFile()
    {
        var configNames = new[] { "Flint.toml", "Flint.yaml", "Flint.yml", "Flint.json" };

        foreach (var name in configNames)
        {
            var path = Path.Combine(_siteRoot!, name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private void CreateSiteBuilder()
    {
        var contentParser = new ContentParser();
        var templateRenderer = new ScribanTemplateRenderer(Path.Combine(_siteRoot!, "layouts"));
        var assetPipeline = new AssetPipeline();
        var configLoader = new ConfigLoader();

        _siteBuilder = new SiteBuilder(
            contentParser,
            templateRenderer,
            assetPipeline,
            configLoader);
    }

    private static async Task WriteFileAsync(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
    }

    private static async Task WriteBinaryFileAsync(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, content);
    }

    private async Task CleanupAsync()
    {
        if (_siteRoot != null && Directory.Exists(_siteRoot))
        {
            try
            {
                // 等待一小段时间确保文件句柄已释放
                await Task.Delay(100);

                // 递归删除目录
                Directory.Delete(_siteRoot, recursive: true);
            }
            catch (Exception)
            {
                // 忽略清理错误，可能是文件被占用
                // 在 CI 环境中，临时目录会被定期清理
            }
        }

        _fileSnapshots.Clear();
        _siteRoot = null;
        _outputPath = null;
        _config = null;
        _siteBuilder = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureSiteCreated()
    {
        if (_siteRoot == null)
        {
            throw new InvalidOperationException("站点尚未创建，请先调用 CreateSiteAsync");
        }
    }

    #endregion
}


/// <summary>
/// 快照比较结果
/// </summary>
public sealed class SnapshotComparisonResult
{
    /// <summary>
    /// 快照名称
    /// </summary>
    public required string SnapshotName { get; init; }

    /// <summary>
    /// 新增的文件
    /// </summary>
    public required IReadOnlyList<string> AddedFiles { get; init; }

    /// <summary>
    /// 删除的文件
    /// </summary>
    public required IReadOnlyList<string> RemovedFiles { get; init; }

    /// <summary>
    /// 修改的文件
    /// </summary>
    public required IReadOnlyList<string> ModifiedFiles { get; init; }

    /// <summary>
    /// 未变化的文件
    /// </summary>
    public required IReadOnlyList<string> UnchangedFiles { get; init; }

    /// <summary>
    /// 是否有变化
    /// </summary>
    public bool HasChanges => AddedFiles.Count > 0 || RemovedFiles.Count > 0 || ModifiedFiles.Count > 0;

    /// <summary>
    /// 变化的文件总数
    /// </summary>
    public int TotalChanges => AddedFiles.Count + RemovedFiles.Count + ModifiedFiles.Count;

    /// <summary>
    /// 所有文件总数
    /// </summary>
    public int TotalFiles => AddedFiles.Count + RemovedFiles.Count + ModifiedFiles.Count + UnchangedFiles.Count;
}
