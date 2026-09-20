#!/usr/bin/env bash
# 统一演示站点生成：为全部适配主题建站（统一长文内容集）
#
# 用法:
#   bash scripts/demo-sites.sh                # 全部 21 主题：建站+迁移+构建+报告
#   bash scripts/demo-sites.sh narrow yinyang # 仅指定主题
#   SERVE=1 bash scripts/demo-sites.sh        # 构建后批量启动服务（端口 8401+）
#
# 产物: demo-sites/<主题>/   （站点源码 + public/ 构建产物）

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
FIXTURES="$SELF_DIR/fixtures/longform"
WORK="$REPO_ROOT/demo-sites"
PORT_BASE=8401

THEMES=(ananke bearblog blog-awesome blowfish clarity console even fixit github-style
        hugo-book hugo-coder hugo-paper loveit m10c monochrome narrow papermod
        stack techdoc xmin yinyang)
if [ $# -gt 0 ]; then
  THEMES=("$@")
fi

# ---- 统一站点配置：基础段 + 主题专属 params（与 theme-matrix20 同源）----
write_config() {
  local name="$1" site="$2" port="$3"
  {
    echo "baseURL = \"http://127.0.0.1:$port/\""
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
    blowfish|clarity)
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
  {
    echo ""
    echo "[menus]"
    echo "[[menus.main]]"
    echo "name = \"首页\""
    echo "url = \"/\""
    echo "weight = 1"
    echo "[[menus.main]]"
    echo "name = \"归档\""
    echo "url = \"/posts/\""
    echo "weight = 2"
    echo "[[menus.main]]"
    echo "name = \"关于\""
    echo "url = \"/about/\""
    echo "weight = 3"
  } >> "$site/hugo.toml"
  cp "$site/hugo.toml" "$site/Flint.toml"
}

# ---- 主循环 ----
printf "%-14s %-8s %-6s %-8s %s\n" "主题" "构建" "页数" "最小页" "URL"
printf -- "--------------------------------------------------------------------------\n"

port=$PORT_BASE
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

  # 统一内容集：fixtures 根内容（posts/、about/、_index.md）整体并入 content/
  cp -r "$FIXTURES"/. "$site/content/"

  write_config "$name" "$site" "$port"

  # 迁移主题 → 站点 themes/
  "$MIGRATOR" "$src" "$site/themes/$name" > /dev/null 2>&1
  if [ ! -d "$site/themes/$name" ]; then
    printf "%-14s 迁移失败\n" "$name"
    fail=$((fail+1))
    port=$((port+1))
    continue
  fi

  # 构建
  build_out=$(cd "$site" && timeout 300 "$FLINT" build -s . -o public --clean 2>&1)
  bexit=$?
  pages=$(find "$site/public" -name "*.html" 2>/dev/null | wc -l)
  minsz=$(find "$site/public" -name "*.html" -exec wc -c {} + 2>/dev/null | sort -n | head -1 | awk '{print $1}')

  url="http://127.0.0.1:$port/"
  if [ $bexit -eq 0 ] && [ "$pages" -gt 0 ]; then
    printf "%-14s %-8s %-6s %-8s %s\n" "$name" "通过" "$pages" "${minsz:-NA}" "$url"
  else
    printf "%-14s %-8s %-6s %-8s\n" "$name" "失败($bexit)" "$pages" "${minsz:-NA}"
    echo "$build_out" | tail -3 | sed 's/^/    /'
    fail=$((fail+1))
  fi
  port=$((port+1))
done

echo "--------------------------------------------------------------------------"
if [ "${SERVE:-0}" = "1" ]; then
  echo "启动全部服务…"
  port=$PORT_BASE
  for name in "${THEMES[@]}"; do
    site="$WORK/$name"
    [ -d "$site/public" ] || continue
    (cd "$site/public" && python -m http.server "$port" --bind 127.0.0.1 > /dev/null 2>&1 &)
    port=$((port+1))
  done
  echo "全部服务已后台启动。"
fi
exit $((fail > 0 ? 1 : 0))
