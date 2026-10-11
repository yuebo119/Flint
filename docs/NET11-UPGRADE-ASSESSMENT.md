# .NET 11 升级评估报告（R7 T8.2）

> 2026-10-11，试点基线 dev @ T7 checkpoint。结论先行：**建议 .NET 11 GA（2026-11）后再升级，当前不升**。试点实证了主要阻力与收益，均为实测数据非推断。

## 试点实测（本机 net11.0.100-rc.1 SDK）

| 项 | 结果 |
|----|------|
| 代码可编译性 | ✓ 0 个语言级编译错误（C# 14 语言面无损） |
| 新增分析器错误 | **9 个**（IDE0018×2、IDE0120×2、IDE0090×2、CA2016×3；net10 全绿的同一代码） |
| 阻力性质 | 全部机械修复（内联声明/LINQ 简化/new 简化/CT 转发），无 API 破坏、无 obsoletion 阻断 |
| 依赖面 | 未验证（NuGet 包对 net11.0 的 TFM 兼容矩阵留试点下一步） |

## 收益面（按 R7 优化清单映射）

1. **R2R 目标升级 x86-64-v3**（Windows/Linux）：Flint 发布形态 = R2R 单文件（Flint.Cli.csproj 默认）与 AOT（`-p:PublishAot=true` 正式口径）。R2R v3 带 AVX2/BMI2 代码生成，升级即得，无需代码改动。
2. **Runtime Async V2**（`<UseRuntimeAsync>` 预览开关）：T3/T4 后渲染管线为数千 await/页的异步密集负载，理论收益方向正确；预览特性，GA 后复测再决定是否启用。
3. **零拷贝流适配器**（ReadOnlyMemoryStream/StringStream 等）：T7 已用 SendFileAsync/分段写覆盖主场景，边际。
4. **Process.RunAndCaptureText**：T5.1 的 sass CLI 回退若池化可简化进程交互（当前判定跳过池化，触发器未出现）。

## 代价面

1. **硬件基线**：x86/x64 JIT 基线升至 x86-64-v2（≈2013 年前 CPU 淘汰）、R2R 目标 v3；Windows Arm64 需 LSE。发布矩阵需确认目标用户硬件面（当前无 Linux 实测数据，R6 4.3 缺口）。
2. **9 处分析器清理**：半天内工作量，随升级实施一次清掉。
3. **NativeAOT 产物名**：Unix 下原生库输出加 `lib` 前缀——Flint 发布物是 exe 非原生库，影响面评估为零 [推断]。
4. **IsAotCompatible 库分析**：Flint.Core 在 net11 下需复验（试点只跑了 build，AOT 分析器集未复测）。

## 建议

- **时点**：GA 后（2026-11）单独立项升级，试点清单 = global.json/TFM 切换 → 9 处分析器清理 → 全量测试 → AOT 发布冒烟 → 双语料基准复测（T0 口径）→ R2R v3 收益量化。
- **此前可先行**：LangVersion 13→14 与升级解耦（C# 14 特性在 net10 可用），随任意批次顺手做。
- **触发器**：若 2026-11 前出现 net11 独占特性需求（Runtime Async 实测收益、union types 稳定），提前立项。
