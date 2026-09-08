# 性能基准资料

**📊 综合性能报告：[REPORT.md](REPORT.md)**——三语料吞吐矩阵、进程内套件、
复杂度阶梯、公平性声明与可复现命令（2026-09-08）

## 目录

- `reports/`：四轮性能套件 HTML 报告快照（2026-02 基线 → 升级后优化前 →
  优化后 → 重构后），对应 docs/HUGO-GAP-TASKS.md 的对比记录
- `scripts/ssg-bench.py`：万页合成语料 + 双引擎测量（可复现）
- `scripts/corpus-convert.py`：真实语料转换器（MDN/k8s → 统一站点）
- `scripts/perf-gate.ps1` + `perf-baseline.json`：性能回归门禁

## 外部工具与语料（已统一保留在 benchmarks/ 下；corpus/ 与 tools/ 不入库）

| 资料 | 位置 | 再生方式 |
| ---- | ---- | -------- |
| Hugo v0.165.0 Extended | `tools/hugo.exe` | GitHub release `hugo_extended_0.165.0_windows-amd64.zip` 解压 |
| 万页合成语料 | `corpus/ssg-bench/` | `python scripts/ssg-bench.py --pages 10000` |
| MDN 源仓库与转换语料 | `corpus/mdn-content/`、`corpus/corpus-merged/` | 克隆 mdn/content（depth 1），再 `python scripts/corpus-convert.py` |
| k8s 源仓库与转换语料 | `corpus/k8s-website/`（含于 corpus-merged） | 同上 |

## 对比实测结论（2026-09-08）

| 口径 | 页数 | Flint/Hugo |
| ---- | ---- | ---------- |
| 合成（ssg-bench） | 10000 | 0.92-0.95x |
| MDN 真实内容 | 14621 | 0.72x |

规律：规模越大 Flint 并行管线优势越明显。详细记录见 docs/HUGO-GAP-TASKS.md。
（历史口径说明：bep 官方基准 3978 页与 MDN+k8s 合并语料 14970 页两轮对比
因产出页数不对称/未对称审计已移除，记录见 git 历史。）
