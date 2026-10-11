# Flint 项目级 AI 规范

> 本文件被 ZCode 自动加载。与全局 `~/.zcode/AGENTS.md` 分工：全局管行为准则，本文件管 Flint 项目规范。
> 性能工程走 EPSL v1.1（方案：`C:\Users\Andy\ZCodeProject\extreme-performance-system.md`；模板：`C:\Users\Andy\ZCodeProject\perf-template\`）。

## 启动清单（每次会话开始确认）

```
1. git status——工作树清洁？
2. dotnet build Flint.slnx -c Release——0 警告 0 错误（TreatWarningsAsErrors + AnalysisLevel latest-all 已在 Directory.Build.props）？
3. perf-budgets.md——已读最新版？（预算 = perf-baseline.json 门禁阈值的契约层）
4. .ai/perf-ledger.md——勿重开清单与明确不做表已读？（提性能建议前必查，防重复提案）
```

## 性能工程（EPSL 接入 · 2026-10-11）

- **预算契约**：`perf-budgets.md`（SLO 数字草案待用户裁决；比值口径以 `scripts/perf-baseline.json` 门禁为单一执行真源，阈值 20%，只拦显著劣化不拦噪声）。
- **既有量具**（勿重复建设）：
  - 双引擎对照基准：`dotnet run --project src/Flint.DevTools -- bench ssg`（万页合成 + 真实语料，Flint vs Hugo 0.165，Hugo 即竞品臂）
  - 性能回归门禁：`src/Flint.DevTools` perf 子命令 + `scripts/perf-baseline.json`（含环境税归因纪律：劣化先做端点实验区分环境与代码）
  - 历史报告：`benchmarks/REPORT.md` / `SUMMARY.md`（2026-09-08 三轮全清口径）
- **L1 编码期门禁**：Directory.Build.props 已 latest-all + TWAE；性能规则段见 `.editorconfig`（新增 error 级规则走基线制：先 suggestion 存量清点，豁免登记后才升）。
- **性能深挖触发条件**（其余场合禁全量地毯扫描）：门禁红 / 量级越界 10× / 新增负载类型 / SLO 烧穿 / 阶段边界 / 项目完成。
- **AI 性能分析四红线**：抽查"核实/无一处"论断；负结论带检索口径；过 perf-ledger 与既有裁决比对；继承论断独立复验。
- **历史深挖档案**（三轮源码分析，2026-10-11，结论未实施）：串行管线三处 / ContainerChain O(全站页数) / GetPageFunction 全站线性查找——候选清单见 `.ai/perf-ledger.md` 排序表。

## 规范真源（按优先级）

| 优先级 | 文件 | 用途 |
| --- | --- | --- |
| 最高 | `.editorconfig` | 45 条 dotnet_diagnostic 降级登记（latest-all 全开后的豁免面） |
| 高 | `perf-budgets.md` | 性能预算契约（EPSL L0） |
| 高 | `scripts/perf-baseline.json` | 性能门禁阈值执行真源 |
| 参考 | `benchmarks/REPORT.md` | 既有实测与公平性声明 |

## 构建验证时机

| 时机 | 命令 |
| --- | --- |
| 最终提交前 | `dotnet build Flint.slnx -c Release --no-incremental` |
| 性能跑测前 | Release 全清重发布（AOT 形态，对齐 benchmarks 口径） |
