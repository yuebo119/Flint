# Fixtures（测试夹具）

本目录包含测试夹具类，用于为测试提供预配置的环境和测试数据。

## 目录用途

测试夹具是 xUnit 测试框架中的重要概念，用于：

- **创建临时测试站点**：提供隔离的测试环境
- **管理测试生命周期**：自动创建和清理测试资源
- **共享测试上下文**：在多个测试之间共享昂贵的设置操作

## 主要组件

### TestSiteFixture

测试站点夹具，提供以下功能：

- 创建临时测试站点目录
- 从预定义模板创建站点（minimal、complete、multilingual）
- 添加内容文件、模板文件、资源文件
- 执行构建操作
- 文件系统快照和比较
- 自动清理临时文件

#### 属性

| 属性 | 类型 | 描述 |
|------|------|------|
| `SiteRoot` | `string` | 测试站点根目录 |
| `OutputPath` | `string` | 输出目录路径 |
| `Config` | `SiteConfig` | 站点配置对象 |
| `TestId` | `string` | 测试唯一标识符 |
| `IsCreated` | `bool` | 站点是否已创建 |

#### 方法

| 方法 | 描述 |
|------|------|
| `CreateSiteAsync(template)` | 从模板创建测试站点 |
| `CreateFromTemplateAsync(path)` | 从指定路径复制站点模板 |
| `AddContentAsync(path, content)` | 添加内容文件 |
| `AddTemplateAsync(path, content)` | 添加模板文件 |
| `AddAssetAsync(path, content)` | 添加资源文件 |
| `AddStaticAsync(path, content)` | 添加静态文件 |
| `SetConfigAsync(content, format)` | 设置配置文件 |
| `BuildAsync(options)` | 执行构建 |
| `IncrementalBuildAsync(files, options)` | 执行增量构建 |
| `CleanOutputAsync()` | 清理输出目录 |
| `CreateSnapshotAsync(name)` | 创建文件系统快照 |
| `CompareWithSnapshotAsync(name)` | 比较当前输出与快照 |
| `GetOutputFileAsync(path)` | 获取输出文件内容 |
| `OutputFileExists(path)` | 检查输出文件是否存在 |
| `GetOutputFiles()` | 获取所有输出文件列表 |

## 使用示例

### 基本使用

```csharp
public class BuildTests : IClassFixture<TestSiteFixture>
{
    private readonly TestSiteFixture _fixture;

    public BuildTests(TestSiteFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Build_WithValidSite_ShouldSucceed()
    {
        // 创建测试站点
        await _fixture.CreateSiteAsync("default");
        
        // 执行构建
        var result = await _fixture.BuildAsync();
        
        // 验证结果
        result.Success.Should().BeTrue();
    }
}
```

### 添加自定义内容

```csharp
[Fact]
public async Task Build_WithCustomContent_ShouldGenerateCorrectOutput()
{
    await _fixture.CreateSiteAsync();
    
    // 添加自定义内容
    await _fixture.AddContentAsync("posts/my-post.md", """
        +++
        title = "我的文章"
        date = 2024-01-15T10:00:00+08:00
        +++
        
        这是文章内容。
        """);
    
    // 执行构建
    var result = await _fixture.BuildAsync();
    
    // 验证输出
    _fixture.OutputFileExists("posts/my-post/index.html").Should().BeTrue();
}
```

### 使用快照比较

```csharp
[Fact]
public async Task IncrementalBuild_ShouldOnlyUpdateChangedFiles()
{
    await _fixture.CreateSiteAsync();
    await _fixture.BuildAsync();
    
    // 创建基线快照
    await _fixture.CreateSnapshotAsync("baseline");
    
    // 修改内容
    await _fixture.AddContentAsync("posts/new-post.md", "...");
    
    // 增量构建
    await _fixture.IncrementalBuildAsync(["content/posts/new-post.md"]);
    
    // 比较变化
    var comparison = await _fixture.CompareWithSnapshotAsync("baseline");
    comparison.AddedFiles.Should().Contain("posts/new-post/index.html");
}
```

### 使用 IAsyncLifetime

```csharp
public class MyTests : IAsyncLifetime
{
    private TestSiteFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new TestSiteFixture();
        await _fixture.CreateSiteAsync("complete");
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task Test_Something()
    {
        // 使用已初始化的 fixture
        var result = await _fixture.BuildAsync();
        result.Success.Should().BeTrue();
    }
}
```

## 站点模板

### default / minimal

最小有效站点，包含：
- 基本配置文件
- 首页内容
- 默认模板

### complete / full

完整功能站点，包含：
- 多种 Front Matter 格式
- 短代码示例
- 草稿和未来内容
- SCSS 和 JavaScript 资源
- 完整的模板层次结构

### multilingual / i18n

多语言站点，包含：
- 中文、英文、日文内容
- 语言配置
- 翻译关联

## 相关需求

- **Requirements 10.1**: 提供创建临时测试站点的测试夹具
- **Requirements 10.6**: 测试完成后自动清理临时文件和目录
- **Requirements 10.9**: 确保测试之间的隔离性

## 注意事项

1. 每个测试应该创建独立的 TestSiteFixture 实例，确保测试隔离
2. 使用 `IAsyncLifetime` 或 `IClassFixture<T>` 管理夹具生命周期
3. 临时目录会在测试完成后自动清理
4. 如果需要保留测试输出进行调试，可以在 `DisposeAsync` 之前检查文件
