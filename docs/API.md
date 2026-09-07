# Flint API 文档

## 目录

1. [概述](#概述)
2. [核心命名空间](#核心命名空间)
3. [配置 API](#配置-api)
4. [内容解析 API](#内容解析-api)
5. [模板渲染 API](#模板渲染-api)
6. [站点构建 API](#站点构建-api)
7. [模板函数](#模板函数)

---

## 概述

Flint 核心库 (`Flint.Core`) 提供了静态站点生成的所有核心功能。

### 项目结构

```
Flint.Core/
├── Abstractions/        # 接口定义
├── Configuration/       # 配置加载
├── Content/             # 内容解析
├── Models/              # 数据模型
├── Site/                # 站点构建
└── Templates/           # 模板渲染
```

### 主要组件

| 组件 | 说明 |
|------|------|
| `ConfigLoader` | 配置文件加载器 |
| `ContentParser` | 内容解析器 (Front Matter + Markdown) |
| `MarkdownParser` | Markdown 解析器 |
| `ScribanTemplateRenderer` | Scriban 模板渲染器 |
| `SiteBuilder` | 站点构建器 |

---

## 核心命名空间

```csharp
using Flint.Core;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Flint.Core.Content;
using Flint.Core.Models;
using Flint.Core.Site;
using Flint.Core.Templates;
```

---

## 配置 API

### ConfigLoader

加载站点配置文件。

```csharp
public class ConfigLoader
{
    /// <summary>
    /// 从文件加载配置
    /// </summary>
    /// <param name="path">配置文件路径</param>
    /// <returns>站点配置</returns>
    public Task<SiteConfig> LoadAsync(string path);
    
    /// <summary>
    /// 从目录自动检测并加载配置
    /// </summary>
    /// <param name="directory">站点目录</param>
    /// <returns>站点配置</returns>
    public Task<SiteConfig> LoadFromDirectoryAsync(string directory);
}
```

#### 使用示例

```csharp
var loader = new ConfigLoader();

// 从指定文件加载
var config = await loader.LoadAsync("Flint.toml");

// 从目录自动检测
var config = await loader.LoadFromDirectoryAsync("./my-site");
```

### SiteConfig

站点配置模型。

```csharp
public class SiteConfig
{
    public string BaseURL { get; set; }
    public string Title { get; set; }
    public string LanguageCode { get; set; }
    public string Theme { get; set; }
    public Dictionary<string, object> Params { get; set; }
    public Dictionary<string, List<MenuItem>> Menu { get; set; }
    public Dictionary<string, string> Taxonomies { get; set; }
}
```

---

## 内容解析 API

### ContentParser

解析 Markdown 内容文件（Front Matter + 正文）。

```csharp
public class ContentParser
{
    /// <summary>
    /// 解析内容文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>解析后的内容</returns>
    public Task<ContentFile> ParseAsync(string filePath);
    
    /// <summary>
    /// 解析内容字符串
    /// </summary>
    /// <param name="content">内容字符串</param>
    /// <param name="filePath">文件路径（用于元数据）</param>
    /// <returns>解析后的内容</returns>
    public Task<ContentFile> ParseStringAsync(string content, string filePath);
}
```

#### 使用示例

```csharp
var parser = new ContentParser();

// 解析文件
var content = await parser.ParseAsync("content/posts/hello.md");

Console.WriteLine(content.Title);       // 标题
Console.WriteLine(content.Date);        // 日期
Console.WriteLine(content.HtmlContent); // HTML 内容
Console.WriteLine(content.WordCount);   // 字数
Console.WriteLine(content.ReadingTime); // 阅读时间
```

### ContentFile

内容文件模型。

```csharp
public class ContentFile
{
    // 基本信息
    public string Title { get; set; }
    public DateTime Date { get; set; }
    public bool Draft { get; set; }
    
    // 分类
    public List<string> Tags { get; set; }
    public List<string> Categories { get; set; }
    
    // 内容
    public string RawContent { get; set; }
    public string HtmlContent { get; set; }
    public string Summary { get; set; }
    
    // 统计
    public int WordCount { get; set; }
    public int ReadingTime { get; set; }
    
    // 元数据
    public string FilePath { get; set; }
    public string Permalink { get; set; }
    public string Section { get; set; }
    
    // 自定义字段
    public Dictionary<string, object> Params { get; set; }
}
```

### MarkdownParser

Markdown 解析器。

```csharp
public class MarkdownParser
{
    /// <summary>
    /// 将 Markdown 转换为 HTML
    /// </summary>
    /// <param name="markdown">Markdown 内容</param>
    /// <returns>HTML 内容</returns>
    public string ToHtml(string markdown);
    
    /// <summary>
    /// 提取标题列表（用于生成目录）
    /// </summary>
    /// <param name="markdown">Markdown 内容</param>
    /// <returns>标题列表</returns>
    public List<Heading> ExtractHeadings(string markdown);
}
```

#### 使用示例

```csharp
var parser = new MarkdownParser();

// 转换为 HTML
var html = parser.ToHtml("# Hello\n\nWorld");

// 提取标题
var headings = parser.ExtractHeadings(markdown);
foreach (var h in headings)
{
    Console.WriteLine($"{h.Level}: {h.Text} (#{h.Id})");
}
```

---

## 模板渲染 API

### ScribanTemplateRenderer

Scriban 模板渲染器。

```csharp
public class ScribanTemplateRenderer : ITemplateRenderer
{
    /// <summary>
    /// 创建渲染器
    /// </summary>
    /// <param name="layoutsDir">模板目录</param>
    public ScribanTemplateRenderer(string layoutsDir);
    
    /// <summary>
    /// 渲染模板
    /// </summary>
    /// <param name="templateName">模板名称（不含扩展名）</param>
    /// <param name="context">渲染上下文</param>
    /// <returns>渲染后的 HTML</returns>
    public Task<string> RenderAsync(string templateName, RenderContext context);
    
    /// <summary>
    /// 渲染字符串模板
    /// </summary>
    /// <param name="template">模板字符串</param>
    /// <param name="context">渲染上下文</param>
    /// <returns>渲染后的 HTML</returns>
    public Task<string> RenderStringAsync(string template, RenderContext context);
    
    /// <summary>
    /// 预编译所有模板
    /// </summary>
    public Task PrecompileTemplatesAsync();
}
```

#### 使用示例

```csharp
var renderer = new ScribanTemplateRenderer("./layouts");

// 预编译模板（可选，提升性能）
await renderer.PrecompileTemplatesAsync();

// 创建上下文
var context = new RenderContext
{
    Page = contentFile,
    Site = siteContext
};

// 渲染模板
var html = await renderer.RenderAsync("_default/single", context);
```

### RenderContext

渲染上下文。

```csharp
public class RenderContext
{
    public ContentFile Page { get; set; }
    public SiteContext Site { get; set; }
    public Dictionary<string, object> Data { get; set; }
}
```

### SiteContext

站点上下文（模板中可访问）。

```csharp
public class SiteContext
{
    public string Title { get; set; }
    public string BaseURL { get; set; }
    public string LanguageCode { get; set; }
    public Dictionary<string, object> Params { get; set; }
    public Dictionary<string, List<MenuItem>> Menus { get; set; }
    public List<ContentFile> RegularPages { get; set; }
    public List<ContentFile> AllPages { get; set; }
}
```

---

## 站点构建 API

### SiteBuilder

站点构建器。

```csharp
public class SiteBuilder
{
    /// <summary>
    /// 创建构建器
    /// </summary>
    /// <param name="options">构建选项</param>
    public SiteBuilder(BuildOptions options);
    
    /// <summary>
    /// 构建站点
    /// </summary>
    /// <returns>构建结果</returns>
    public Task<BuildResult> BuildAsync();
    
    /// <summary>
    /// 增量构建
    /// </summary>
    /// <param name="changedFiles">变更的文件列表</param>
    /// <returns>构建结果</returns>
    public Task<BuildResult> IncrementalBuildAsync(IEnumerable<string> changedFiles);
}
```

#### 使用示例

```csharp
var options = new BuildOptions
{
    SourceDir = "./my-site",
    OutputDir = "./public",
    IncludeDrafts = false,
    Minify = true,
    Clean = true
};

var builder = new SiteBuilder(options);
var result = await builder.BuildAsync();

Console.WriteLine($"构建完成: {result.PageCount} 页");
Console.WriteLine($"耗时: {result.Duration.TotalMilliseconds}ms");
```

### BuildOptions

构建选项。

```csharp
public class BuildOptions
{
    public string SourceDir { get; set; } = ".";
    public string OutputDir { get; set; } = "public";
    public bool IncludeDrafts { get; set; } = false;
    public bool IncludeFuture { get; set; } = false;
    public bool Minify { get; set; } = false;
    public bool Clean { get; set; } = false;
    public bool Verbose { get; set; } = false;
    public bool EnableContentCache { get; set; } = false;
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;
}
```

### BuildResult

构建结果。

```csharp
public class BuildResult
{
    public bool Success { get; set; }
    public int PageCount { get; set; }
    public int StaticFileCount { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; }
    public List<string> Warnings { get; set; }
}
```

---

## 模板函数

Flint 提供丰富的内置模板函数。

### 字符串函数

| 函数 | 说明 | 示例 |
|------|------|------|
| `upper` | 转大写 | `{{ "hello" \| upper }}` → `HELLO` |
| `lower` | 转小写 | `{{ "HELLO" \| lower }}` → `hello` |
| `capitalize` | 首字母大写 | `{{ "hello" \| capitalize }}` → `Hello` |
| `truncate` | 截断 | `{{ text \| truncate 100 }}` |
| `replace` | 替换 | `{{ text \| replace "a" "b" }}` |
| `split` | 分割 | `{{ "a,b,c" \| split "," }}` |
| `slugify` | URL 友好化 | `{{ "Hello World" \| slugify }}` → `hello-world` |
| `md5` | MD5 哈希 | `{{ email \| md5 }}` |

### 日期函数

| 函数 | 说明 | 示例 |
|------|------|------|
| `date.to_string` | 格式化日期 | `{{ page.date \| date.to_string "%Y-%m-%d" }}` |
| `date.now` | 当前时间 | `{{ date.now }}` |
| `date.add_days` | 增加天数 | `{{ page.date \| date.add_days 7 }}` |

### 集合函数

| 函数 | 说明 | 示例 |
|------|------|------|
| `where` | 过滤 | `{{ pages \| where "draft" false }}` |
| `sort` | 排序 | `{{ pages \| sort "date" }}` |
| `reverse` | 反转 | `{{ pages \| reverse }}` |
| `first` | 取前 N 个 | `{{ pages \| first 5 }}` |
| `last` | 取后 N 个 | `{{ pages \| last 5 }}` |
| `slice` | 切片 | `{{ pages \| slice 0 10 }}` |
| `group_by` | 分组 | `{{ pages \| group_by "section" }}` |

### URL 函数

| 函数 | 说明 | 示例 |
|------|------|------|
| `abs_url` | 绝对 URL | `{{ "/about/" \| abs_url }}` |
| `rel_url` | 相对 URL | `{{ "/about/" \| rel_url }}` |
| `safe_url` | 安全 URL | `{{ url \| safe_url }}` |

### 数学函数

| 函数 | 说明 | 示例 |
|------|------|------|
| `math.ceil` | 向上取整 | `{{ 3.2 \| math.ceil }}` → `4` |
| `math.floor` | 向下取整 | `{{ 3.8 \| math.floor }}` → `3` |
| `math.round` | 四舍五入 | `{{ 3.5 \| math.round }}` → `4` |
| `math.abs` | 绝对值 | `{{ -5 \| math.abs }}` → `5` |

### 数组函数

| 函数 | 说明 | 示例 |
|------|------|------|
| `array.size` | 数组长度 | `{{ tags.size }}` |
| `array.first` | 第一个元素 | `{{ tags \| array.first }}` |
| `array.last` | 最后一个元素 | `{{ tags \| array.last }}` |
| `array.join` | 连接 | `{{ tags \| array.join ", " }}` |
| `array.contains` | 包含 | `{{ tags \| array.contains "go" }}` |

---

## 扩展开发

### 自定义模板函数

```csharp
// 注册自定义函数
renderer.RegisterFunction("my_func", (string input) => {
    return input.ToUpper() + "!";
});
```

### 自定义内容处理器

```csharp
public class MyContentProcessor : IContentProcessor
{
    public Task<ContentFile> ProcessAsync(ContentFile content)
    {
        // 自定义处理逻辑
        return Task.FromResult(content);
    }
}
```

---

*文档更新日期：2026-02-06*
