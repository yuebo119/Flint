#!/usr/bin/env bash
# 统一演示站点生成：为全部适配主题建站（统一长文内容集）
#
# 用法（仓库根执行）:
#   bash demo-sites/demo-sites.sh                # 全部 21 主题：建站+迁移+构建+报告
#   bash demo-sites/demo-sites.sh narrow yinyang # 仅指定主题
#   SERVE=1 bash demo-sites/demo-sites.sh        # 构建后批量启动服务（端口 8401+）
#
# 产物: 本项目目录下 <主题>/   （站点源码 + public/ 构建产物）

set -u

# 项目内自定位：本脚本位于案例站项目根（<repo>/demo-sites/）
SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SELF_DIR/.." && pwd)"
FLINT_SRC="$REPO_ROOT"
THEMES_DIR="$REPO_ROOT/theme-migrator/themes"   # 读主题迁移项目的转换产出
MIGRATOR="$FLINT_SRC/src/Flint.ThemeMigrator/bin/Debug/net10.0/Flint.ThemeMigrator.exe"
FLINT="$FLINT_SRC/src/Flint.Cli/bin/Release/net10.0/win-x64/Flint.exe"
FIXTURES="$SELF_DIR/fixtures/corpus"
WORK="$SELF_DIR"
PORT_BASE=8401

THEMES=(ananke bearblog blog-awesome blowfish clarity console even fixit github-style
        hugo-book hugo-coder hugo-paper loveit m10c monochrome narrow papermod
        stack techdoc xmin yinyang)
if [ $# -gt 0 ]; then
  THEMES=("$@")
fi

# 端口按主题在总表中的**固定位置**分配：子集运行与全量运行的端口一致
declare -A THEME_PORT
_i=$PORT_BASE
for _t in ananke bearblog blog-awesome blowfish clarity console even fixit github-style hugo-book hugo-coder hugo-paper loveit m10c monochrome narrow papermod stack techdoc xmin yinyang; do
  THEME_PORT[$_t]=$_i
  _i=$((_i+1))
done

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
  port=${THEME_PORT[$name]}
  src="$THEMES_DIR/$name"
  if [ ! -d "$src/layouts" ]; then
    printf "%-14s 跳过（主题不存在）\n" "$name"
    continue
  fi
  site="$WORK/$name"
  rm -rf "$site"
  mkdir -p "$site"

  # 统一内容集：corpus 的 content/（posts/weekly/notes/docs/about）与
  # static/（占位插图）分别并入站点对应目录——21 主题共用同一套语料
  cp -r "$FIXTURES/content"/. "$site/content/"
  mkdir -p "$site/static"
  cp -r "$FIXTURES/static"/. "$site/static/"

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
  # 单站超时 600s：fixit 在 100 篇语料 + 全分类/标签分页下需 ~500s
  # （其余主题 30-60s），300s 会把慢主题误判为失败
  build_out=$(cd "$site" && timeout 600 "$FLINT" build -s . -o public --clean 2>&1)
  bexit=$?
  pages=$(find "$site/public" -name "*.html" 2>/dev/null | wc -l)
  minsz=$(find "$site/public" -name "*.html" -exec wc -c {} + 2>/dev/null | sort -n | head -1 | awk '{print $1}')

  port=${THEME_PORT[$name]}
  url="http://127.0.0.1:$port/"
  if [ $bexit -eq 0 ] && [ "$pages" -gt 0 ]; then
    printf "%-14s %-8s %-6s %-8s %s\n" "$name" "通过" "$pages" "${minsz:-NA}" "$url"
  else
    printf "%-14s %-8s %-6s %-8s\n" "$name" "失败($bexit)" "$pages" "${minsz:-NA}"
    echo "$build_out" | tail -3 | sed 's/^/    /'
    fail=$((fail+1))
  fi
done

echo "--------------------------------------------------------------------------"
if [ "${SERVE:-0}" = "1" ]; then
  echo "启动全部服务（单进程多端口）…"
  # **单进程服务 22 个端口**：21 主题 + 画廊各起一个 python 解释器实测 490MB
  # （~19MB/个）；合成单进程后内存大降，且浏览器并行请求不再排队。
  # 2026-09-25 起服务端为 .NET 10 实现（demo-sites/demo-serve/，TcpListener
  # 手写最小 HTTP，全面替代 python 版 demo-serve.py）。
  # 先停掉旧服务（Windows 下进程 CWD 在 public/ 内会锁目录，且重复启动
  # 会残留僵孤进程）
  "$SELF_DIR/demo-stop.sh" > /dev/null 2>&1
  # 映射直接走命令行参数（不用临时文件：后台进程可能还没读文件就被 rm，
  # 实测偶发 FileNotFoundError）
  serve_args=("8400=$SELF_DIR/gallery/public")
  for name in "${THEMES[@]}"; do
    [ -d "$WORK/$name/public" ] || continue
    serve_args+=("${THEME_PORT[$name]}=$WORK/$name/public")
  done
  # 直启已构建 apphost（实测：dotnet run 会多挂一个 160MB 的宿主父进程，
  # 直启 demo-serve.exe 单进程仅 ~22MB，与 python 版持平）
  dotnet build "$SELF_DIR/demo-serve" -c Release -v q --nologo > /dev/null 2>&1
  ("$SELF_DIR/demo-serve/bin/Release/net10.0/demo-serve.exe" "${serve_args[@]}" > /dev/null 2>&1 &)
  echo "全部服务已在单进程内启动（画廊 http://127.0.0.1:8400/）。"
fi
exit $((fail > 0 ? 1 : 0))
