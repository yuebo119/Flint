#!/usr/bin/env bash
# 克隆候选主题（用于补足 20 个"在最新 Hugo 上可用"的主题）
#
# 候选来源：awesome-hugo-themes / GitHub topic:hugo-theme 按 star 排序的前列，
# 以及此前一轮因**验证方法缺陷**（exampleSite 未用、Dart Sass 未装、author 形状、
# enableGitInfo、主题名未归一——见 THEME-MIGRATOR-PLAN 第十八节）可能被误排除的主题。
# 判定仍由 theme-migrator/verify-themes.sh 用 Hugo 侧三条件（exit=0 + 有页数 + 最小页 >200B）做出。
#
# 用法: bash theme-migrator/clone-candidates.sh
set -u

# 项目内自定位：本脚本位于主题迁移项目根（<repo>/theme-migrator/）
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEST="$ROOT/themes"
mkdir -p "$DEST"

# 名称|仓库（默认分支由远端决定）
CANDIDATES=(
  # 此前可能被误排除的（验证方法已修）
  "terminal|panr/hugo-theme-terminal"
  "hextra|imfing/hextra"
  "relearn|McShelby/hugo-theme-relearn"
  "jane|xianmin/hugo-theme-jane"
  "mainroad|Vimux/Mainroad"
  "hermit|Track3/hermit"
  "eureka|wangchucheng/hugo-eureka"
  "meme|reuixiy/hugo-theme-meme"
  "intro|victoriadrake/hugo-theme-introduction"
  "learn|matcornic/hugo-theme-learn"
  "gallery|nicokaiser/hugo-theme-gallery"
  "fresh|StefMa/hugo-fresh"
  "zzo|zzossig/hugo-theme-zzo"
  "hello-friend|panr/hugo-theme-hello-friend"
  # 新候选（流行且近期有维护）
  "gokarna|526avijitgupta/gokarna"
  "yinyang|joway/hugo-theme-yinyang"
  "m10c|vaga/hugo-theme-m10c"
  "hugo-profile|gurusabarish/hugo-profile"
  "lynx|jpanther/lynx"
  "monochrome|kaiiiz/hugo-theme-monochrome"
  "hugo-tania|WingLim/hugo-tania"
  "techdoc|thingsym/hugo-theme-techdoc"
  "etch|LukasJoswiak/etch"
  "noteworthy|kimcc/hugo-theme-noteworthy"
  "adritian|zetxek/adritian-free-hugo-theme"
  "narrow|tom2almighty/hugo-narrow"
)

printf "%-16s %-48s %s\n" "名称" "仓库" "克隆"
printf -- "------------------------------------------------------------------------------------\n"

for entry in "${CANDIDATES[@]}"; do
  IFS='|' read -r name repo <<< "$entry"
  target="$DEST/$name"
  if [ -d "$target/layouts" ]; then
    printf "%-16s %-48s 已存在\n" "$name" "$repo"
    continue
  fi
  rm -rf "$target"
  if timeout 300 git clone --depth 1 --quiet "https://github.com/$repo.git" "$target" 2>/dev/null; then
    n=$(find "$target/layouts" -name "*.html" 2>/dev/null | wc -l)
    printf "%-16s %-48s OK（%s 布局）\n" "$name" "$repo" "$n"
  else
    printf "%-16s %-48s 失败\n" "$name" "$repo"
  fi
done
