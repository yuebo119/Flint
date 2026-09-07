# Utilities（工具类）

本目录包含测试工具类，提供测试过程中常用的辅助功能。

## 目录用途

工具类用于封装测试中重复使用的功能，包括：

- **CLI 测试运行器**：执行和验证 CLI 命令
- **开发服务器测试客户端**：与开发服务器交互
- **断言辅助类**：提供自定义断言方法
- **文件系统辅助类**：文件操作和比较

## 主要组件

### CliTestRunner

CLI 测试运行器，提供以下功能：

- 执行 CLI 命令并捕获输出
- 支持超时处理
- 支持环境变量注入
- 支持工作目录设置

### DevServerTestClient

开发服务器测试客户端，提供以下功能：

- 发送 HTTP 请求
- 连接 WebSocket 热重载端点
- 等待热重载通知

### TestErrorAssertions

测试错误断言辅助类，提供以下功能：

- 断言构建结果包含指定错误代码
- 断言构建结果包含指定警告
- 断言 CLI 结果返回指定退出代码

### FileSystemHelper

文件系统辅助类，提供以下功能：

- 创建临时目录
- 复制目录结构
- 比较文件内容
- 计算文件哈希

## 使用示例

```csharp
// 使用 CliTestRunner
var runner = new CliTestRunner();
var result = await runner.RunAsync(["build", "--minify"], workingDirectory: sitePath);

// 验证结果
TestErrorAssertions.AssertExitCode(result, 0);
result.StandardOutput.Should().Contain("Build completed");

// 使用 DevServerTestClient
using var client = new DevServerTestClient(serverUrl);
var response = await client.GetAsync("/index.html");
response.StatusCode.Should().Be(HttpStatusCode.OK);
```

## 相关需求

- **Requirements 1.1, 1.6, 1.10**: CLI 命令执行和验证
- **Requirements 3.1, 3.6**: 开发服务器交互
- **Requirements 8.1-8.4**: 错误处理验证
