# EdgeCases（边界条件测试数据）

本目录包含边界条件测试数据，用于测试系统在极端情况下的行为。

## 目录用途

边界条件测试数据用于：

- **测试空值处理**：空字符串、null、空集合
- **测试极端值**：超长字符串、超大文件、深层嵌套
- **测试特殊字符**：Unicode、控制字符、emoji、特殊路径字符
- **测试无效输入**：格式错误、类型错误、损坏数据

## 目录结构

```
EdgeCases/
├── Empty/                    # 空值测试数据
│   ├── EmptyConfig.toml      # 空配置文件
│   ├── EmptyContent.md       # 空内容文件
│   └── EmptyTemplate.html    # 空模板文件
├── Extreme/                  # 极端值测试数据
│   ├── LongString.txt        # 超长字符串
│   ├── DeepNesting/          # 深层嵌套目录
│   └── LargeFile.md          # 超大文件
├── SpecialChars/             # 特殊字符测试数据
│   ├── Unicode/              # Unicode 字符
│   ├── ControlChars/         # 控制字符
│   ├── Emoji/                # Emoji 字符
│   └── PathChars/            # 路径特殊字符
└── Invalid/                  # 无效输入测试数据
    ├── MalformedYaml.md      # 格式错误的 YAML
    ├── MalformedToml.toml    # 格式错误的 TOML
    ├── InvalidUtf8.bin       # 无效 UTF-8 编码
    └── TruncatedFile.md      # 截断的文件
```

## 边界条件类别

### 空值边界

- 空字符串 `""`
- null 值
- 空集合 `[]`
- 只有空白字符的字符串
- 只有 Front Matter 没有正文的内容

### 极端值边界

- 超长字符串（10MB+）
- 超大文件（100MB+）
- 深层嵌套目录（100+ 层）
- 大量文件（10000+ 文件）
- 超长文件名（255 字符）

### 特殊字符边界

- Unicode 字符（中文、日文、阿拉伯文）
- 控制字符（\x00-\x1F）
- Emoji 字符（😀🎉🚀）
- 路径特殊字符（空格、&、#、%）
- 换行符差异（\n、\r\n、\r）

### 无效输入边界

- 格式错误的 YAML/TOML/JSON
- 无效的 UTF-8 编码
- 截断的文件
- 循环引用
- 类型不匹配

## 使用示例

```csharp
[Theory]
[MemberData(nameof(EdgeCaseData.EmptyInputs))]
public async Task Build_WithEmptyInput_ShouldHandleGracefully(string input)
{
    // 测试空输入处理
    var result = await _fixture.BuildWithContentAsync(input);
    
    // 验证不会崩溃
    result.Should().NotBeNull();
}

[Theory]
[MemberData(nameof(EdgeCaseData.SpecialCharPaths))]
public async Task Build_WithSpecialCharPath_ShouldSucceed(string path)
{
    // 测试特殊字符路径
    await _fixture.AddContentAsync(path, "# Test");
    var result = await _fixture.BuildAsync();
    
    // 验证构建成功
    result.Success.Should().BeTrue();
}
```

## 相关需求

- **Requirements 10.2**: 提供预配置的测试数据集
- **Requirements 8.1-8.4**: 错误处理验证
