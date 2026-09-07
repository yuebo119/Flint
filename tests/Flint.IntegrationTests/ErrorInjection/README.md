# ErrorInjection（错误注入测试）

本目录包含错误注入测试相关的代码和数据，用于主动验证系统的错误处理逻辑。

## 目录用途

错误注入测试用于：

- **验证错误处理**：确保系统正确处理各种错误情况
- **测试错误恢复**：验证系统在错误后能正确恢复
- **测试错误报告**：验证错误信息的完整性和准确性
- **提高系统健壮性**：发现潜在的错误处理漏洞

## 主要组件

### ErrorInjector

错误注入工具类，提供以下功能：

- **文件系统错误注入**
  - 权限错误（只读文件、无访问权限）
  - 磁盘满错误
  - 文件锁定错误
  - 路径不存在错误

- **网络错误注入**
  - 连接超时
  - 连接失败
  - DNS 解析失败
  - 网络中断

- **数据损坏注入**
  - 无效 UTF-8 编码
  - 截断文件
  - 格式错误的配置
  - 损坏的二进制文件

### ErrorScenarios

预定义的错误场景，包含：

- 单个文件错误场景
- 多个文件错误场景
- 级联错误场景
- 并发错误场景

## 目录结构

```
ErrorInjection/
├── FileSystem/               # 文件系统错误注入
│   ├── PermissionErrors/     # 权限错误
│   ├── DiskErrors/           # 磁盘错误
│   └── LockErrors/           # 文件锁定错误
├── Network/                  # 网络错误注入
│   ├── TimeoutErrors/        # 超时错误
│   └── ConnectionErrors/     # 连接错误
├── Data/                     # 数据损坏注入
│   ├── EncodingErrors/       # 编码错误
│   ├── FormatErrors/         # 格式错误
│   └── TruncationErrors/     # 截断错误
└── Scenarios/                # 错误场景
    ├── SingleError/          # 单个错误场景
    ├── MultipleErrors/       # 多个错误场景
    └── CascadingErrors/      # 级联错误场景
```

## 使用示例

```csharp
// 注入文件系统错误
[Fact]
public async Task Build_WithReadOnlyFile_ShouldReportError()
{
    // 注入只读文件错误
    using var injection = ErrorInjector.InjectReadOnlyFile(
        _fixture.SiteRoot, "content/test.md");
    
    // 执行构建
    var result = await _fixture.BuildAsync();
    
    // 验证错误报告
    result.Success.Should().BeFalse();
    result.Errors.Should().Contain(e => 
        e.ErrorCode == "FILE_ACCESS_DENIED");
}

// 注入网络错误
[Fact]
public async Task ModuleInstall_WithNetworkTimeout_ShouldReportError()
{
    // 注入网络超时错误
    using var injection = ErrorInjector.InjectNetworkTimeout();
    
    // 执行模块安装
    var result = await _moduleManager.InstallAsync("test-theme");
    
    // 验证错误报告
    result.Success.Should().BeFalse();
    result.Error.Should().Contain("timeout");
}

// 注入数据损坏
[Fact]
public async Task Parse_WithInvalidUtf8_ShouldReportError()
{
    // 注入无效 UTF-8 数据
    await ErrorInjector.InjectInvalidUtf8Async(
        _fixture.SiteRoot, "content/test.md");
    
    // 执行解析
    var result = await _parser.ParseAsync("content/test.md");
    
    // 验证错误报告
    result.Success.Should().BeFalse();
    result.Error.ErrorCode.Should().Be("INVALID_ENCODING");
}
```

## 错误注入原则

1. **隔离性**：错误注入不应影响其他测试
2. **可恢复**：注入的错误应能被清理
3. **可重现**：错误场景应能被重现
4. **真实性**：模拟真实的错误情况

## 相关需求

- **Requirements 8.1**: 内容文件错误处理
- **Requirements 8.2**: 模板文件错误处理
- **Requirements 8.3**: 配置文件错误处理
- **Requirements 8.4**: 资源处理错误处理
- **Requirements 8.5**: 多错误收集和报告
- **Requirements 8.10**: 异步错误传播
