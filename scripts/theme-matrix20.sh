#!/usr/bin/env bash
# 20 主题横向兼容矩阵（第二轮：流行主题）
#
# 与第一轮 theme-matrix.sh 的差别（吸取第一轮两个盲区的教训）：
#   1. Hugo 侧检查**退出码 + 产出页数**——第一轮忽略退出码，使 loveit 的无效
#      基线伪装成"不对称"（配置 author 写成字符串导致 Hugo 构建失败却无人发现）
#   2. Flint 侧检查**产出页数 + 最小页尺寸**——第一轮只看退出码，
#      使 ananke 的空页（2-4 字节）被当成"构建通过"
#
# 用法: bash scripts/theme-matrix20.sh [主题名...]
# 输出: 每主题一行 TSV 到 stdout，明细到 matrix20/<主题>/report.txt

set -u

# 工作根探测：脚本固定在仓库 <repo>/scripts/ 下；主题语料读主题迁移项目
# （theme-migrator/），Hugo/Dart Sass 工具缓存位于工作区外层 tools/
SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SELF_DIR/.." && pwd)"
# fixit 等主题的 Hugo 基线需要 Dart Sass（to-css.html 写死 transpiler=dartsass）：
# 工作区 ../tools/dart-sass/（官方 windows-x64 发行包）存在则入 PATH
if [ -d "$REPO_ROOT/../tools/dart-sass" ]; then
  export PATH="$REPO_ROOT/../tools/dart-sass:$PATH"
fi
FLINT_SRC="$REPO_ROOT"
THEMES_DIR="$REPO_ROOT/theme-migrator/themes"
HUGO="$REPO_ROOT/../tools/hugo-bin/hugo.exe"
MIGRATOR="$FLINT_SRC/src/Flint.ThemeMigrator/bin/Debug/net10.0/Flint.ThemeMigrator.exe"
FLINT="$FLINT_SRC/src/Flint.Cli/bin/Release/net10.0/win-x64/Flint.exe"
WORK="$REPO_ROOT/matrix20"

THEMES=("$@")
if [ ${#THEMES[@]} -eq 0 ]; then
  # 与 theme-migrator/themes/ 目录的实际 21 个主题保持一致（不存在的主题名会静默跳过）
  THEMES=(ananke bearblog blog-awesome blowfish clarity console even fixit github-style
          hugo-book hugo-coder hugo-paper loveit m10c monochrome narrow papermod
          stack techdoc xmin yinyang)
fi

# ---- 通用内容集（覆盖首页/列表/单页/分类/标签/分页/代码/标题层级/独立页）----
make_content() {
  local site="$1"
  mkdir -p "$site/content/posts" "$site/content/docs/guide"
  cat > "$site/content/_index.md" <<'EOF'
---
title: Home
---
Welcome to the matrix site.
EOF
  cat > "$site/content/posts/_index.md" <<'EOF'
---
title: Posts
---
All posts.
EOF
  cat > "$site/content/posts/first.md" <<'EOF'
---
title: First Post
date: 2026-01-15
lastmod: 2026-02-01
tags: [intro, test]
categories: [general]
description: The first post
summary: Summary of the first post
---
First post body with some text.

## A heading

More content here.

```go
func main() { fmt.Println("hi") }
```

### Sub heading

Deeper content.
EOF
  cat > "$site/content/posts/second.md" <<'EOF'
---
title: Second Post
date: 2026-02-20
tags: [test]
categories: [general]
description: The second post
---
Second post body.
EOF
  cat > "$site/content/posts/third.md" <<'EOF'
---
title: Third Post
date: 2026-03-10
tags: [misc]
---
Third post body.
EOF
  cat > "$site/content/docs/_index.md" <<'EOF'
---
title: Docs
---
Documentation section.
EOF
  cat > "$site/content/docs/guide/getting-started.md" <<'EOF'
---
title: Getting Started
weight: 10
---
Guide body.
EOF
  cat > "$site/content/about.md" <<'EOF'
---
title: About
---
About page.
EOF
}

# ---- 最小配置（通用形态；主题特有的 params 由各主题分支补充）----
make_config() {
  local name="$1" site="$2"
  {
    echo "baseURL = \"https://example.com/\""
    echo "title = \"Matrix Site\""
    echo "theme = \"$name\""
    echo "paginate = 2"
    echo "enableRobotsTXT = true"
    echo ""
    echo "[pagination]"
    echo "pagerSize = 2"
    echo ""
    # TOML 铁律：表头之后的点号键挂进**该表**。站点参数必须用显式 [params] 段，
    # 且主题参数（add_theme_params）在此段内追加、menus 永远收尾（write_menus）
    echo "[params]"
    echo "description = \"Matrix test site\""
  } > "$site/hugo.toml"
  cp "$site/hugo.toml" "$site/Flint.toml"
}

# menus 必须写在**最后**：TOML 的表头之后，后续点号键全部挂进该表——
# 此前 params.* 追加在 [menus] 之后，全部被嵌进 menus.main 数组元素（对主题不可见）
write_menus() {
  local site="$1"
  {
    echo ""
    echo "[menus]"
    echo "[[menus.main]]"
    echo "name = \"Home\""
    echo "url = \"/\""
    echo "weight = 1"
    echo "[[menus.main]]"
    echo "name = \"Posts\""
    echo "url = \"/posts/\""
    echo "weight = 2"
  } >> "$site/hugo.toml"
  cp "$site/hugo.toml" "$site/Flint.toml"
}

# 主题特有 params（依据各主题 exampleSite 的最小必要字段；缺失时主题会渲染降级或报错）
add_theme_params() {
  local name="$1" site="$2"
  case "$name" in
    hugo-book)
      cat >> "$site/hugo.toml" <<'EOF'
BookSection = "docs"
BookTheme = "light"
BookDateFormat = "January 2, 2006"
BookComments = false
BookSearch = false
EOF
      ;;
    blog-awesome)
      # author 必须是映射（bio.html 取 author.name/avatar，meta 取 author.name）；
      # avatar 指向主题 assets 里的既有图标
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "Tester"
Author.avatar = "icons/android-chrome-192x192.png"
EOF
      ;;
    fixit)
      # fixit 要求 Author 是映射（同 loveit）；文章页的词数/阅读时长徽标由
      # .Param 门控（single.html 的 word_count/reading_time），开启后
      # FuzzyWordCount/ReadingTime 文案进对比面
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "Tester"
Author.link = "https://example.com/"
home.profile.enable = true
word_count = true
reading_time = true
EOF
      ;;
    loveit)
      # loveit 要求 Author 是映射（字符串会渲染失败——第一轮 loveit 血例）
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "Tester"
Author.link = "https://example.com/"
home.profile.enable = true
EOF
      ;;
    even)
      # even 的 baseof 校验 params.version == "4.x"（缺了直接 errorf 拒绝构建，
      # 主题 exampleSite 的 config.toml 自带此项）；Author 要求映射（head.html 取
      # .Site.Params.Author.name，字符串会报 "can't evaluate field name"）；
      # archivePaginate 供 .Paginate 第二参（缺失时 Hugo 报 "must be a positive integer"）
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "Tester"
version = "4.x"
archivePaginate = 50
showArchiveCount = false
EOF
      ;;
    papermod)
      cat >> "$site/hugo.toml" <<'EOF'
author = "Tester"
homeInfoParams.Title = "Matrix Site"
homeInfoParams.Content = "Matrix test"
EOF
      ;;
    stack)
      # widgets 必须是**带 type 的表数组**（sidebar/right.html 取 widget 的 .type，
      # 字符串元素报 "can't evaluate field type"）；形状对齐主题 demo
      cat >> "$site/hugo.toml" <<'EOF'
sidebar.emoji = "cat"
sidebar.subtitle = "Matrix"
[[widgets.homepage]]
type = "search"
[[widgets.homepage]]
type = "archives"
[[widgets.page]]
type = "toc"
EOF
      ;;
    ananke)
      cat >> "$site/hugo.toml" <<'EOF'
author = "Tester"
ananke.show_recent_posts = true
EOF
      ;;
    blowfish|clarity)
      # 两者的 author 相关取值按映射写（blowfish 的 Author.name 在字符串上取
      # .name 会直接报错；clarity 的 RSS 取 Author.email/name）
      cat >> "$site/hugo.toml" <<'EOF'
Author.name = "Tester"
Author.email = "tester@example.com"
EOF
      ;;
    congo|relearn|hextra|jane|mainroad|terminal|archie|hermit|xmin|bearblog|console|risotto|hugo-coder|hugo-paper)
      cat >> "$site/hugo.toml" <<'EOF'
author = "Tester"
EOF
      ;;
  esac
}

prepare_site() {
  local name="$1" site="$2" theme_src="$THEMES_DIR/$name"
  # junction 必须先 rmdir 卸载（rm -rf 会报 Device or resource busy）
  [ -d "$site/themes/$name" ] && cmd //c "rmdir $(cygpath -w "$site/themes/$name")" >/dev/null 2>&1
  rm -rf "$site"
  mkdir -p "$site/themes"
  make_content "$site"
  make_config "$name" "$site"
  add_theme_params "$name" "$site"
  write_menus "$site"
  # 主题用 junction 挂载（零拷贝；失败则回退复制）
  cmd //c "mklink /J $(cygpath -w "$site/themes/$name") $(cygpath -w "$theme_src")" >/dev/null 2>&1 ||
    cp -r "$theme_src" "$site/themes/$name"
}

count_html() { find "$1" -name "*.html" 2>/dev/null | wc -l; }
min_html_size() { find "$1" -name "*.html" -exec wc -c {} + 2>/dev/null | sort -n | head -1 | awk '{print $1}'; }

printf "%-14s %-6s %-6s %-11s %-6s %-6s %-9s %-7s\n" \
  "主题" "hugo" "页数" "flint构建" "页数" "最小页" "对称" "结构/文本"
printf -- "--------------------------------------------------------------------------------------\n"

for name in "${THEMES[@]}"; do
  src="$THEMES_DIR/$name"
  if [ ! -d "$src/layouts" ] && [ ! -d "$src/layout" ]; then
    printf "%-14s %s\n" "$name" "跳过（主题不存在）"
    continue
  fi

  site="$WORK/$name"
  prepare_site "$name" "$site"
  report="$WORK/$name-report.txt"
  : > "$report"

  # ---- Hugo 侧 ----
  (cd "$site" && timeout 300 "$HUGO" --quiet >"$report.hugo" 2>&1)
  hugo_exit=$?
  hugo_pages=$(count_html "$site/public")
  hugo_ok="失败"
  [ "$hugo_exit" -eq 0 ] && [ "$hugo_pages" -gt 0 ] && hugo_ok="通过"

  # ---- 迁移 + Flint 侧 ----
  mig_out=$(timeout 600 "$MIGRATOR" "$src" "$site/themes-migrated/$name" 2>&1)
  rate=$(echo "$mig_out" | grep -oE "rate=[0-9.]+" | cut -d= -f2)
  unsup=$(echo "$mig_out" | grep -oE "unsupported=[0-9]+" | cut -d= -f2)
  mig_ok="OK"
  if [ -z "$rate" ]; then mig_ok="迁移异常"; fi

  # 用迁移产物替换 Themes 目录下的主题（Flint 从 <site>/themes/<name> 读）
  if [ "$mig_ok" = "OK" ]; then
    rm -rf "$site/themes/$name"
    mkdir -p "$site/themes"
    cp -r "$site/themes-migrated/$name" "$site/themes/$name"
  fi

  build_out=$(cd "$site" && timeout 300 "$FLINT" build -s . -o public-flint --clean --missing-layout skip 2>&1)
  flint_exit=$?
  flint_pages=$(count_html "$site/public-flint")
  flint_min=$(min_html_size "$site/public-flint")
  flint_verdict="通过"
  [ "$flint_exit" -ne 0 ] && flint_verdict="构建失败"
  if [ "$flint_exit" -eq 0 ] && [ "${flint_min:-0}" -lt 200 ]; then flint_verdict="空页"; fi

  echo "=== $name ===" >> "$report"
  echo "hugo exit=$hugo_exit pages=$hugo_pages" >> "$report"
  echo "$build_out" | grep -oE "[^ \]*\.html\([0-9]+,[0-9]+\) : error : .{0,70}" | sort -u | head -12 >> "$report"
  echo "--- 无位置信息 ---" >> "$report"
  echo "$build_out" | grep -oE "error : .{0,80}" | sort -u | head -10 >> "$report"

  # ---- 门禁④（仅基线有效时）----
  sym="-"; sim="-"
  if [ "$hugo_ok" = "通过" ] && [ "$flint_pages" -gt 0 ]; then
    diff_out=$(timeout 600 "$MIGRATOR" "$src" "$site/themes-migrated/$name" \
      --verify "$site" --site-output "$site/public-flint" --hugo-output "$site/public" 2>&1)
    sym=$(echo "$diff_out" | grep -oE "symmetric=[01]" | cut -d= -f2)
    st=$(echo "$diff_out" | grep -oE "struct=[0-9.]+" | cut -d= -f2)
    tx=$(echo "$diff_out" | grep -oE "text=[0-9.]+" | cut -d= -f2)
    sim="${st}/${tx}"
  fi

  printf "%-14s %-6s %-6s %-11s %-6s %-6s %-9s %-7s\n" \
    "$name" "$hugo_ok" "${hugo_pages:-0}" "$flint_verdict" "${flint_pages:-0}" "${flint_min:-NA}" \
    "${sym}" "${sim}"
done

printf -- "--------------------------------------------------------------------------------------\n"
echo "（hugo/flint构建：通过=exit0 且产出非空；对称=1 表示门禁④通过）"
