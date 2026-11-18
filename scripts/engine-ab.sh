#!/usr/bin/env bash
# Fluid/Scriban 双引擎站点级 A/B（FLUID-MIGRATION-PLAN P2-6/P0-2）
#
# 方法（对齐 perf-budgets.md 口径纪律）：
# - 同内容：两个站点变体共享同一份 content/（脚本生成，N 篇文章）
# - 同机构建：站点骨架经 `Flint new site`（默认 fluid / --engine scriban）生成
# - 交错采集：A/B/A/B/A/B 六轮交替，抵消机器漂移；每轮 FLINT_TRACE_PHASES=1
#   记录阶段耗时（渲染段/解析段/扫描段/写盘段占比即 P0-2 基线）
# - 统计：各变体取三轮中位数；产物对称性（P2-7）对比两变体 public/ 文件集
#
# 用法：bash scripts/engine-ab.sh [文章数，默认 300] [轮数，默认 3] [CLI 路径，默认 Debug JIT]
# CLI 覆盖：第三参或环境变量 FLINT_CLI——AOT 口径重测时传 Release+AOT 发布产物
# （JIT 口径会稀释渲染段占比：非渲染段被 JIT 编译开销膨胀 10-15 倍）
# 输出：tmp/engine-ab/ 下的原始日志与汇总（tmp 不入库）

set -euo pipefail

POSTS="${1:-300}"
ROUNDS="${2:-3}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLI="${3:-${FLINT_CLI:-$ROOT/src/Flint.Cli/bin/Debug/net11.0/win-x64/Flint.exe}}"
WORK="$ROOT/tmp/engine-ab"

if [[ ! -x "$CLI" ]]; then
  echo "未找到 CLI: $CLI（先 dotnet build src/Flint.Cli）" >&2
  exit 1
fi

# 构建形态随报告声明（EPSL 口径纪律）：JIT 口径会把非渲染段膨胀 10-15 倍，
# 稀释渲染段占比、拉平两引擎差异，不声明形态的比值不可用于决策
CLI_BYTES="$(stat -c%s "$CLI" 2>/dev/null || echo 0)"
if [[ "$CLI_BYTES" -ge 30000000 ]]; then
  CLI_SHAPE="NativeAOT（单文件，${CLI_BYTES} 字节）"
else
  CLI_SHAPE="JIT/R2R（${CLI_BYTES} 字节，性能结论须标注此形态）"
fi
echo "== CLI: $CLI"
echo "== 构建形态: $CLI_SHAPE"

echo "== 准备工作目录 $WORK"
rm -rf "$WORK"
mkdir -p "$WORK"

# ---- 1. 生成双引擎站点（同 content） ----
echo "== 生成站点骨架（fluid 默认 / scriban 兼容）"
(cd "$WORK" && "$CLI" new site ab-fluid >/dev/null)
(cd "$WORK" && "$CLI" new site ab-scriban --engine scriban >/dev/null)

# 共享内容：N 篇文章（两变体逐字节相同——对称性审计的前提）
gen_posts() {
  local dir="$1"
  mkdir -p "$dir/content/posts"
  for i in $(seq 1 "$POSTS"); do
    cat > "$dir/content/posts/post-$i.md" <<EOF
+++
title = "文章 $i"
date = 2026-01-$(printf '%02d' $((i % 28 + 1)))T10:00:00+08:00
draft = false
tags = ["tag$((i % 7))"]
categories = ["cat$((i % 5))"]
+++

第 $i 篇文章的正文。包含**加粗**与 \`代码\`，以及足够长度的段落用于稳定的渲染计时。

## 小节甲

列表内容若干行，保证 Markdown 解析有真实工作量。

## 小节乙

结尾段落。
EOF
  done
  # section 索引：让 posts 列表页真实产出（合成 section 无 _index 不产页）
  cat > "$dir/content/posts/_index.md" <<EOF
+++
title = "文章"
date = 2026-01-01T10:00:00+08:00
draft = false
+++
EOF
}

gen_posts "$WORK/ab-fluid"
gen_posts "$WORK/ab-scriban"

# ---- 2. 交错采集 ----
echo "== 交错采集 ${ROUNDS} 轮（FLINT_TRACE_PHASES=1）"
run_round() {
  local variant="$1" round="$2"
  # 注意：warmup 文件名不得以 "-r" 开头——汇总 glob 是 ${variant}-r*.log，
  # "-rwarmup" 会被撞中计入统计（2026-10-19 实测坑）
  FLINT_TRACE_PHASES=1 "$CLI" build --source "$WORK/ab-$variant" \
    --output "$WORK/ab-$variant/public" \
    > "$WORK/${variant}-${round}.log" 2>&1
}

# 预热一轮（JIT/文件缓存），不计入统计
run_round fluid warmup
run_round scriban warmup

for r in $(seq 1 "$ROUNDS"); do
  run_round fluid "$r"
  run_round scriban "$r"
  echo "  轮次 $r 完成"
done

# ---- 3. 汇总（中位数，无 python 依赖；awk 用字段匹配，兼容 mawk） ----
# 日志编码：Windows 控制台为 GBK、Linux 为 UTF-8——先按 UTF-8 校验，
# 失败（含中文字节的 GBK 流必失败）再回退 GBK 转换。原实现 GBK 优先，
# 在 Linux 上会把合法 UTF-8 中文误按 GBK 解成乱码（iconv 不报错），
# 阶段名匹配全落空
read_logs() { # $1=glob 前缀 → UTF-8 文本流（只取编号轮次，排除 warmup）
  iconv -f UTF-8 -t UTF-8 "$1"-[0-9]*.log 2>/dev/null \
    || iconv -f GBK -t UTF-8 "$1"-[0-9]*.log 2>/dev/null
}

median() { # $1=glob 前缀；从"总计 xxxms"行取中位数
  read_logs "$1" | awk '$2 == "总计" { print $3 }' | sort -n | awk '
    { v[NR] = $1 }
    END {
      if (NR == 0) { print "n/a"; exit }
      printf "%.1f", (NR % 2 ? v[(NR + 1) / 2] : (v[NR / 2] + v[NR / 2 + 1]) / 2)
    }'
}

phase_median() { # $1=glob 前缀，$2=阶段名；从 [phase] 行取中位数
  read_logs "$1" | awk -v phase="$2" '$2 == phase { print $3 }' | sort -n | awk '
    { v[NR] = $1 }
    END {
      if (NR == 0) { print "n/a"; exit }
      printf "%.1f", (NR % 2 ? v[(NR + 1) / 2] : (v[NR / 2] + v[NR / 2 + 1]) / 2)
    }'
}

FLUID_TOTAL="$(median "$WORK/fluid")"
SCRIBAN_TOTAL="$(median "$WORK/scriban")"
echo ""
echo "== 总构建耗时中位数（${ROUNDS} 轮，$POSTS 篇文章）｜形态: $CLI_SHAPE"
echo "  Fluid  : $FLUID_TOTAL ms"
echo "  Scriban: $SCRIBAN_TOTAL ms"
awk -v f="$FLUID_TOTAL" -v s="$SCRIBAN_TOTAL" 'BEGIN {
  if (f != "n/a" && s != "n/a" && s + 0 > 0)
    printf "  比值   : Fluid/Scriban = %.3fx\n", f / s
}'

for variant in fluid scriban; do
  echo ""
  echo "== $variant 阶段耗时中位数（ms，P0-2 基线）"
  # 阶段名含圆括号（"2+3.扫描+解析(流式)"）必须引号包裹，否则 bash 当成
  # 子 shell 语法报错（2026-10-22 实测）
  for phase in 0.模板预编译 1.配置加载+短码 "2+3.扫描+解析(流式)" 4.页面上下文 6.taxonomy 7.站点上下文 8.渲染页面 9.分类页 10.资源 11.写入输出 11.5辅助输出 12.sitemap/feed 12.5模板错误汇聚 总计; do
    v="$(phase_median "$WORK/$variant" "$phase")"
    [[ "$v" == "n/a" ]] || printf "  %-24s %9s ms\n" "$phase" "$v"
  done
done

# ---- 4. 产物对称性审计（P2-7） ----
echo ""
echo "== 产物对称性审计（P2-7）"
(cd "$WORK/ab-fluid/public" && find . -type f | sort) > "$WORK/fluid-files.txt"
(cd "$WORK/ab-scriban/public" && find . -type f | sort) > "$WORK/scriban-files.txt"
if diff -q "$WORK/fluid-files.txt" "$WORK/scriban-files.txt" >/dev/null; then
  echo "  文件集一致: $(wc -l < "$WORK/fluid-files.txt") 个文件"
else
  echo "  文件集差异:"
  diff "$WORK/fluid-files.txt" "$WORK/scriban-files.txt" | head -20
fi

echo ""
echo "完成。原始日志: $WORK/{fluid,scriban}-r*.log；文件集: $WORK/{fluid,scriban}-files.txt"
