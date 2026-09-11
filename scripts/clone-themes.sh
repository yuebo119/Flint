#!/usr/bin/env bash
# 克隆用于兼容性测试的 Hugo 主题（GitHub topic:hugo-theme 按 star 排序的前列主题）
# 用法: bash scripts/clone-themes.sh
#
# 选择标准：真实主题（排除目录仓库/starter/kit）；覆盖极简/文档/功能全三类；
# 排除依赖外部构建链（hugo module + npm/postcss）的主题以隔离"迁移"这一变量。
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/tools/themes"
mkdir -p "$DEST"

# 名称|仓库|默认分支（已在本地的会跳过）
THEMES=(
  "hugo-book|alex-shpak/hugo-book|main"
  "hugo-coder|luizdepra/hugo-coder|main"
  "blowfish|nunocoracao/blowfish|main"
  "terminal|panr/hugo-theme-terminal|master"
  "hugo-paper|nanxiaobei/hugo-paper|main"
  "hextra|imfing/hextra|main"
  "even|olOwOlo/hugo-theme-even|master"
  "congo|jpanther/congo|dev"
  "bearblog|janraasch/hugo-bearblog|master"
  "archie|athul/archie|master"
  "hermit|Track3/hermit|master"
  "fixit|hugo-fixit/FixIt|main"
  "mainroad|Vimux/Mainroad|master"
  "jane|xianmin/hugo-theme-jane|master"
  "xmin|yihui/hugo-xmin|master"
  "blog-awesome|hugo-sid/hugo-blog-awesome|main"
  "console|mrmierzejewski/hugo-theme-console|master"
  "clarity|chipzoller/hugo-clarity|master"
  "risotto|joeroe/risotto|main"
  "relearn|McShelby/hugo-theme-relearn|main"
)

printf "%-16s %-42s %s\n" "名称" "仓库" "结果"
printf -- "-------------------------------------------------------------------------------\n"

for entry in "${THEMES[@]}"; do
  IFS='|' read -r name repo branch <<< "$entry"
  target="$DEST/$name"
  if [ -d "$target/layouts" ]; then
    printf "%-16s %-42s 已存在\n" "$name" "$repo"
    continue
  fi
  rm -rf "$target"
  if timeout 300 git clone --depth 1 --branch "$branch" --quiet \
      "https://github.com/$repo.git" "$target" 2>/dev/null; then
    n=$(find "$target/layouts" -name "*.html" 2>/dev/null | wc -l)
    printf "%-16s %-42s OK（%s 个布局文件）\n" "$name" "$repo" "$n"
  else
    printf "%-16s %-42s 失败\n" "$name" "$repo"
  fi
done
