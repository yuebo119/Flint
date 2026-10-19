// Flint 静态站点生成器
// 测试集合定义

using Xunit;

namespace Flint.Core.Tests;

/// <summary>
/// xUnit 测试集合定义
/// 环境变量敏感测试串行化：进程级环境变量（Environment.SetEnvironmentVariable）
/// 是整个测试进程共享的全局状态，设置 Flint_* 变量的测试与经
/// ConfigLoader.LoadAsync（末尾 ApplyOverrides 读进程 env）消费配置的测试
/// 并行运行时构成竞态——曾致 ConfigLoaderTests 偶发断言失败（单跑必过）。
/// 同集合内测试串行执行，隔离这一共享状态
/// </summary>
[CollectionDefinition("EnvironmentSensitive")]
public class EnvironmentSensitiveTestGroup
{
}
