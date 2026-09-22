#!/usr/bin/env bash
# 统一演示站点生成：为全部适配主题建站（统一长文内容集），
# **按子路径合并到单一端口**（用户裁决 2026-09-23：21 站 21 端口难用）
#
# 用法:
#   bash scripts/demo-sites.sh                # 全部 21 主题：建站+迁移+构建+合并
#   bash scripts/demo-sites.sh narrow yinyang # 仅指定主题
#   SERVE=1 bash scripts/demo-sites.sh        # 合并后在 8400 单端口启动统一服务
#
# 产物:
#   demo-sites/<主题>/      各主题独立构建源 + public/（子路径 baseURL 产物）
#   demo-unified/<主题>/    单一端口服务树（画廊入口在根，见 build-gallery.sh）
#
# URL 形态：http://127.0.0.1:8400/            → 画廊（主题一览入口）
#           http://127.0.0.1:8400/fixit/     → fixit 演示站
#           http://127.0.0.1:8400/stack/     → stack 演示站
# 架构约束：SSG 的主题是站点级（一个站只能挂一个主题），21 主题无法进同一
# 渲染站；但**构建产物可按子路径归并**到一棵目录树、一个端口提供服务。

set -u

SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [ -d "$SELF_DIR/../tools/themes" ]; then
  REPO_ROOT="$(cd "$SELF_DIR/.." && pwd)"
elif [ -d "$SELF_DIR/../../tools/themes" ]; then
  REPO_ROOT="$(cd "$SELF_DIR/../.." && pwd)"
else
  echo "无法定位 tools/themes（脚本位置: $SELF_DIR）" >&2
  exit 1
fi
FLINT_SRC="$REPO_ROOT/Flint"
[ -d "$FLINT_SRC/src" ] || FLINT_SRC="$REPO_ROOT"
THEMES_DIR="$REPO_ROOT/tools/themes"
MIGRATOR="$FLINT_SRC/src/Flint.ThemeMigrator/bin/Debug/net10.0/Flint.ThemeMigrator.exe"
FLINT="$FLINT_SRC/src/Flint.Cli/bin/Release/net10.0/win-x64/Flint.exe"
FIXTURES="$SELF_DIR/fixtures/corpus"
WORK="$REPO_ROOT/demo-sites"
UNIFIED="$REPO_ROOT/demo-unified"
UNIFIED_PORT=8400
UNIFIED_BASE="http://127.0.0.1:$UNIFIED_PORT"

THEMES=(ananke bearblog blog-awesome blowfish clarity console even fixit github-style
        hugo-book hugo-coder hugo-paper loveit m10c monochrome narrow papermod
        stack techdoc xmin yinyang)
if [ $# -gt 0 ]; then
  THEMES=("$@")
fi

# ---- 统一站点配置：基础段 + 主题专属 params（与 theme-matrix20 同源）----
write_config() {
  local name="$1" site="$2"
  {
    echo "baseURL = \"$UNIFIED_BASE/$name/\""
    echo "languageCode = \"zh-cn\""
    echo "title = \"${name} 演示站\""
    echo "theme = \"$name\""
    echo "paginate = 5"
    echo "enableRobotsTXT = true"
    echo ""
    echo "[pagination]"
    echo "pagerSize = 5"
    echo ""
    echo "[params]"
    echo "description = \"Flint 演示站点（${name} 主题）\""
  } > "$site/hugo.toml"

  # 主题专属 params（含在 [params] 段内追加；语义与 theme-matrix20 一致）
  case "$name" in
    hugo-book)
      cat >> "$site/hugo.toml" <<'EOF'
BookSection = "posts"
BookTheme = "light"
BookDateFormat = "January 2, 2006"
BookComments = false
BookSearch = false
EOF
      ;;
    blog-awesome)
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "演示作者"
Author.avatar = "icons/android-chrome-192x192.png"
EOF
      ;;
    loveit|fixit)
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "演示作者"
Author.link = "http://127.0.0.1:PORT/"
home.profile.enable = true
EOF
      ;;
    even)
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "演示作者"
version = "4.x"
archivePaginate = 50
showArchiveCount = false
EOF
      ;;
    papermod)
      cat >> "$site/hugo.toml" <<'EOF'
author = "演示作者"
homeInfoParams.Title = "演示站点"
homeInfoParams.Content = "Flint 多主题演示"
EOF
      ;;
    stack)
      cat >> "$site/hugo.toml" <<'EOF'
mainSections = ["posts"]
sidebar.emoji = "cat"
sidebar.subtitle = "演示"
[[widgets.homepage]]
type = "search"
[[widgets.homepage]]
type = "archives"
[[widgets.page]]
type = "toc"
EOF
      ;;
    blowfish)
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "演示作者"
Author.email = "demo@example.com"
[params.homepage]
showRecent = true
showRecentItems = 5
EOF
      ;;
    github-style)
      cat >> "$site/hugo.toml" <<'EOF'
headerIcon = "/images/github-mark.png"
EOF
      ;;
    clarity)
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "演示作者"
Author.email = "demo@example.com"
EOF
      ;;
    ananke)
      cat >> "$site/hugo.toml" <<'EOF'
author = "演示作者"
ananke.show_recent_posts = true
EOF
      ;;
    congo|relearn|hextra|jane|mainroad|terminal|archie|hermit|xmin|bearblog|console|risotto|hugo-coder|hugo-paper|github-style|techdoc|m10c|monochrome|narrow|yinyang)
      cat >> "$site/hugo.toml" <<'EOF'
author = "演示作者"
EOF
      ;;
  esac

  # yinyang 需要 headTitle / mainSections
  if [ "$name" = "yinyang" ]; then
    cat >> "$site/hugo.toml" <<'EOF'
headTitle = "演示站点"
mainSections = ["posts"]
EOF
  fi

  # menus 收尾（TOML 表头后不能追加点号键）
  # **URL 带主题子路径**：归并单端口后菜单是站内绝对路径，不带 /<主题>/ 前缀
  # 会跳到统一树根（画廊）或 404（bearblog/stack 的菜单实测）
  {
    echo ""
    echo "[menus]"
    echo "[[menus.main]]"
    echo "name = \"首页\""
    echo "url = \"/$name/\""
    echo "weight = 1"
    echo "[[menus.main]]"
    echo "name = \"归档\""
    echo "url = \"/$name/posts/\""
    echo "weight = 2"
    echo "[[menus.main]]"
    echo "name = \"关于\""
    echo "url = \"/$name/about/\""
    echo "weight = 3"
  } >> "$site/hugo.toml"
  cp "$site/hugo.toml" "$site/Flint.toml"
}

# ---- 主循环 ----
printf "%-14s %-8s %-6s %-8s %s\n" "主题" "构建" "页数" "最小页" "URL"
printf -- "--------------------------------------------------------------------------\n"

fail=0
for name in "${THEMES[@]}"; do
  src="$THEMES_DIR/$name"
  if [ ! -d "$src/layouts" ]; then
    printf "%-14s 跳过（主题不存在）\n" "$name"
    continue
  fi
  site="$WORK/$name"
  rm -rf "$site"
  mkdir -p "$site"

  # 统一内容集：corpus 的 content/（posts/weekly/notes/docs/about）与
  # static/（占位插图、manifest、favicon）分别并入站点对应目录——21 主题共用同一套语料
  cp -r "$FIXTURES/content"/. "$site/content/"
  mkdir -p "$site/static"
  cp -r "$FIXTURES/static"/. "$site/static/"

  write_config "$name" "$site"

  # 迁移主题 → 站点 themes/
  "$MIGRATOR" "$src" "$site/themes/$name" > /dev/null 2>&1
  if [ ! -d "$site/themes/$name" ]; then
    printf "%-14s 迁移失败\n" "$name"
    fail=$((fail+1))
    continue
  fi

  # 构建
  # 单站超时 600s：fixit 在 100 篇语料 + 全分类/标签分页下需 ~500s
  # （其余主题 30-60s），300s 会把慢主题误判为失败
  build_out=$(cd "$site" && timeout 600 "$FLINT" build -s . -o public --clean 2>&1)
  bexit=$?
  pages=$(find "$site/public" -name "*.html" 2>/dev/null | wc -l)
  minsz=$(find "$site/public" -name "*.html" -exec wc -c {} + 2>/dev/null | sort -n | head -1 | awk '{print $1}')

  url="$UNIFIED_BASE/$name/"
  if [ $bexit -eq 0 ] && [ "$pages" -gt 0 ]; then
    printf "%-14s %-8s %-6s %-8s %s\n" "$name" "通过" "$pages" "${minsz:-NA}" "$url"
    # 合并进单一端口服务树：子路径 baseURL 生效后，构建产物整棵树在
    # public/<主题名>/ 下（public/stack/…），把**该目录内容**并入统一树对应
    # 主题目录——直接拷 public/ 会多套一层（demo-unified/stack/stack/…）
    rm -rf "$UNIFIED/$name"
    mkdir -p "$UNIFIED/$name"
    cp -r "$site/public/$name/." "$UNIFIED/$name/"
  else
    printf "%-14s %-8s %-6s %-8s\n" "$name" "失败($bexit)" "$pages" "${minsz:-NA}"
    echo "$build_out" | tail -3 | sed 's/^/    /'
    fail=$((fail+1))
  fi
done

echo "--------------------------------------------------------------------------"
if [ "${SERVE:-0}" = "1" ]; then
  echo "启动统一服务（$UNIFIED_PORT）…"
  (cd "$UNIFIED" && python -m http.server "$UNIFIED_PORT" --bind 127.0.0.1 > /dev/null 2>&1 &)
  echo "统一入口已启动: $UNIFIED_BASE/"
fi
exit $((fail > 0 ? 1 : 0))
