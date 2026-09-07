# Generators（FsCheck 生成器）

本目录包含 FsCheck 属性测试的自定义生成器，用于生成随机测试数据。

## 目录用途

FsCheck 生成器用于：

- **生成随机测试数据**：自动生成大量测试用例
- **覆盖边界条件**：生成边界值和特殊情况
- **发现隐藏 bug**：通过随机测试发现边界条件 bug
- **最小化失败用例**：通过收缩器找到最小失败用例

## 主要组件

### FlintArbitraries

主要的自定义生成器类，包含：

- `SiteConfig` 生成器 - 生成随机站点配置
- `FrontMatter` 生成器 - 生成随机 Front Matter
- `ContentFile` 生成器 - 生成随机内容文件
- `BuildOptions` 生成器 - 生成随机构建选项
- `AssetFile` 生成器 - 生成随机资源文件

### EdgeCaseArbitraries

边界条件生成器，包含：

- 空值生成器（空字符串、null、空集合）
- 极端值生成器（超长字符串、超大数字）
- 特殊字符生成器（Unicode、控制字符、emoji）
- 路径边界生成器（深层嵌套、特殊字符路径）

### Shrinkers

收缩器类，用于最小化失败用例：

- `SiteConfigShrinker` - 收缩站点配置
- `ContentFileShrinker` - 收缩内容文件

## 使用示例

```csharp
// 注册自定义生成器
[Property(Arbitrary = new[] { typeof(FlintArbitraries) })]
public Property ConfigRoundTrip(SiteConfig config)
{
    // 保存配置
    var saved = ConfigSerializer.Serialize(config);
    
    // 重新加载
    var loaded = ConfigSerializer.Deserialize(saved);
    
    // 验证往返一致性
    return (config == loaded).ToProperty();
}

// 使用边界条件生成器
[Property(Arbitrary = new[] { typeof(EdgeCaseArbitraries) })]
public Property HandleEmptyInput(string? input)
{
    // 测试空输入处理
    var result = Parser.Parse(input ?? "");
    return result.IsValid.ToProperty();
}
```

## 生成器设计原则

1. **智能约束**：生成器应约束到有效的输入空间
2. **分布合理**：生成的数据应覆盖各种情况
3. **可收缩**：失败用例应能被最小化
4. **可重现**：使用种子确保测试可重现

## 相关需求

- **Requirements 10.2**: 提供预配置的测试数据集
- 支持属性测试的核心基础设施
