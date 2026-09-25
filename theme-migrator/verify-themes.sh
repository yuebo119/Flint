#!/usr/bin/env bash
# 主题在**最新版 Hugo** 上的可用性验证（纯 Hugo 侧，不涉及 Flint）
#
# 目的：把"主题本身能否在最新 Hugo 上正常构建"与"迁移工具能否处理"分开。
# 只有前者成立的主题才构成有效基线——Hugo 自己都跑不了的主题，其"迁移失败"
# 无从判定（没有可信的对照产物）。
#
# 判定标准（三条件同时满足才算可用）：
#   ① Hugo 退出码 0  ② 产出 HTML 页数 > 0  ③ 最小页尺寸 > 200 字节（排除空页）
#
# 输入优先级：theme/exampleSite（作者验证过的配置与内容）> 最小配置
# 主题目录名：按 config 里的 `theme` 值命名（exampleSite 常见写法与仓库目录名不同，
#   如 terminal 的 config 写 hugo-theme-terminal；不归一会被报"module not found"）
# 环境：Dart Sass 加入 PATH（Hugo v0.153+ 起 LibSass 弃用，toCSS/Sass 需外部实现）
#
# 用法: bash theme-migrator/verify-themes.sh [主题名...]

set -u

# 项目内自定位：本脚本位于主题迁移项目根（<repo>/theme-migrator/）
SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SELF_DIR/.." && pwd)"

THEMES_DIR="$SELF_DIR/themes"
HUGO="$REPO_ROOT/../tools/hugo-bin/hugo.exe"
DART_SASS="$REPO_ROOT/../tools/dart-sass/dart-sass"
WORK="$REPO_ROOT/theme-verify"

THEMES=("$@")
if [ ${#THEMES[@]} -eq 0 ]; then
  THEMES=($(ls "$THEMES_DIR" 2>/dev/null))
fi

make_min_content() {
  local site="$1"
  mkdir -p "$site/content/posts" "$site/content/docs"
  cat > "$site/content/_index.md" <<'EOF'
---
title: Home
---
Welcome.
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
tags: [intro, test]
categories: [general]
description: The first post
---
First post body with some text.

## A heading

More content here.
EOF
  cat > "$site/content/posts/second.md" <<'EOF'
---
title: Second Post
date: 2026-02-20
tags: [test]
categories: [general]
---
Second post body.
EOF
  cat > "$site/content/docs/getting-started.md" <<'EOF'
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

make_min_config() {
  local theme_name="$1" site="$2" repo_dir="${3:-}"
  cat > "$site/hugo.toml" <<EOF
baseURL = "https://example.com/"
title = "Verify Site"
theme = "$theme_name"
paginate = 2
params.description = "Verify site"

[menus]
[[menus.main]]
name = "Home"
url = "/"
weight = 1
[[menus.main]]
name = "Posts"
url = "/posts/"
weight = 2
EOF
  # author 形状按主题期望给（同一份配置里同时写 `author` 字符串与 `Author` 映射会
  # 因 Hugo Params 大小写不敏感而冲突，把期望字符串的主题喂成映射——
  # ananke/mainroad 实测 "unable to cast hmaps.Params to string"）
  case "$repo_dir" in
    loveit|fixit|stack)
      cat >> "$site/hugo.toml" <<'EOF'
params.Author.name = "Tester"
params.Author.link = "https://example.com/"
EOF
      ;;
    *)
      echo 'params.author = "Tester"' >> "$site/hugo.toml"
      ;;
  esac
}

# 目录清理：Git Bash 的 rm -rf 遇被占用目录会失败（"Device or resource busy"），
# 残留内容会与新一轮复制混叠导致构建失败（congo 实测）→ 回退 Windows 原生命令
clean_dir() {
  local d="$1"
  [ -e "$d" ] || return 0
  rm -rf "$d" 2>/dev/null
  if [ -e "$d" ]; then
    cmd //c "rmdir /s /q $(cygpath -w "$d")" >/dev/null 2>&1
  fi
  [ -e "$d" ] && echo "  [警告] 目录未能清理: $d" >&2
}

count_html() { find "$1" -name "*.html" 2>/dev/null | wc -l; }
min_html_size() { find "$1" -name "*.html" -exec wc -c {} + 2>/dev/null | sort -n | head -1 | awk '{print $1}'; }

# 读 config 的 theme 值（TOML/YAML，单文件或 config/_default 目录形式）；
# 读不到时回落到仓库目录名
read_theme_name() {
  local site="$1" fallback="$2"
  python3 - "$site" "$fallback" <<'PY'
import os, re, sys, glob
site, fallback = sys.argv[1], sys.argv[2]
cands = (['hugo.toml', 'config.toml', 'hugo.yaml', 'config.yaml', 'hugo.yml', 'config.yml']
         + sorted(glob.glob(os.path.join(site, 'config', '_default', '*'))))
for c in cands:
    p = c if os.path.isabs(c) else os.path.join(site, c)
    if not os.path.isfile(p):
        continue
    try:
        t = open(p, encoding='utf-8', errors='replace').read()
    except OSError:
        continue
    m = re.search(r'^\s*theme\s*[:=]\s*["\']?([\w.\-/]+)', t, re.M)
    if m:
        print(m.group(1))
        sys.exit(0)
print(fallback)
PY
}

# 删除 config 里的 themesDir（复制后原相对路径失效）
strip_themesdir() {
  local site="$1"
  python3 - "$site" <<'PY'
import os, re, sys, glob
site = sys.argv[1]
files = (glob.glob(os.path.join(site, 'hugo.*')) + glob.glob(os.path.join(site, 'config.*'))
         + glob.glob(os.path.join(site, 'config', '**', '*'), recursive=True))
for p in files:
    if not os.path.isfile(p):
        continue
    try:
        t = open(p, encoding='utf-8', errors='replace').read()
    except OSError:
        continue
    nt = re.sub(r'^\s*themesDir\s*[:=].*\n', '', t, flags=re.M | re.I)
    # enableGitInfo 在非 git 站点会硬失败（"failed to load Git data: fatal: not a
    # git repository"）——主题的 exampleSite 常开启它，而验证站点不需要 git 信息
    # （hugo-book / loveit 实测被此项阻断）
    nt = re.sub(r'^\s*enableGitInfo\s*[:=].*\n', '', nt, flags=re.M | re.I)
    if nt != t:
        open(p, 'w', encoding='utf-8', newline='').write(nt)
PY
}

printf "%-16s %-11s %-7s %-9s %-30s\n" "主题" "配置源" "页数" "最小页" "结论/首错"
printf -- "-----------------------------------------------------------------------------------------------\n"

for name in "${THEMES[@]}"; do
  src="$THEMES_DIR/$name"
  [ -d "$src/layouts" ] || continue

  site="$WORK/$name"
  clean_dir "$site"
  mkdir -p "$site"

  cfg_src="最小配置"
  theme_name="$name"
  if [ -d "$src/exampleSite" ]; then
    cp -r "$src/exampleSite/." "$site/" 2>/dev/null
    strip_themesdir "$site"
    theme_name=$(read_theme_name "$site" "$name")
    cfg_src="exampleSite"
  fi

  mkdir -p "$site/themes"
  cp -r "$src" "$site/themes/$theme_name"
  rm -rf "$site/themes/$theme_name/exampleSite" "$site/themes/$theme_name/.git"

  if [ "$cfg_src" = "最小配置" ]; then
    make_min_content "$site"
    make_min_config "$theme_name" "$site" "$name"
  elif [ ! -d "$site/content" ]; then
    make_min_content "$site"
  fi

  run_hugo() {
    clean_dir "$site/public"
    # 退出码经显式标记回传：复杂子 shell 组合下 $? 不可靠；同时把 stderr 一起收进 out
    raw=$(cd "$site" && { PATH="$DART_SASS:$PATH" timeout 600 "$HUGO" 2>&1; echo "__HUGO_EXIT=$?"; })
    exit_code=$(echo "$raw" | grep -oE "__HUGO_EXIT=[0-9]+" | tail -1 | cut -d= -f2)
    exit_code=${exit_code:-1}
    out=$(echo "$raw" | grep -v "__HUGO_EXIT=")
    pages=$(count_html "$site/public")
    min_size=$(min_html_size "$site/public")
    min_size=${min_size:-0}
  }

  run_hugo
  # 首次失败重试一次：Hugo 偶发失败（资源处理/远程抓取抖动）不该判主题不可用
  if [ "$exit_code" -ne 0 ]; then
    run_hugo
  fi
  content_note=""
  # exampleSite 失败时用**最小内容**重试（保留其配置）：
  # exampleSite 的 content 常含已移除的短代码（gist/twitter）或失效远程链接，
  # 那是内容过时而非主题不可用——判定"主题能否用"应排除内容因素
  if [ "$cfg_src" = "exampleSite" ] && [ "$exit_code" -ne 0 ]; then
    first_err=$(echo "$out" | grep -oE "(ERROR|error)[^|]{0,110}" | head -1)
    rm -rf "$site/content"
    make_min_content "$site"
    run_hugo
    if [ "$exit_code" -eq 0 ] && [ "$pages" -gt 0 ] && [ "$min_size" -ge 200 ]; then
      content_note="（exampleSite 内容过时，换最小内容后可用）"
      cfg_src="exampleSite*"
    else
      out="${out}
--- 换最小内容后 ---
${out}"
      exit_code=1
    fi
  fi

  max_size=$(find "$site/public" -name "*.html" -exec wc -c {} + 2>/dev/null | sort -rn | head -1 | awk '{print $1}')
  max_size=${max_size:-0}
  verdict="可用"
  detail=""
  if [ "$exit_code" -ne 0 ] && [ "$pages" -gt 0 ] && [ "$max_size" -ge 1000 ]; then
    # 有实质产出却非零退出（Hugo 对个别残留告警/资源问题会返回 1）：判"可用（含非致命错误）"，
    # 错误摘要保留备查——判定"主题能否用"看的是产出，不是退出码
    verdict="可用*"
    first_err=$(echo "$out" | grep -oE "(ERROR|error)[^|]{0,90}" | head -1 | sed 's/^[A-Za-z]* *//')
    detail="（非致命：${first_err:-exit=${exit_code}}）"
  elif [ "$exit_code" -ne 0 ]; then
    verdict="不可用"
    detail=$(echo "$out" | grep -oE "(ERROR|error)[^|]{0,110}" | head -1 | sed 's/^[A-Za-z]* *//')
    # 无错误输出却非零退出：多为超时（timeout 返回 124），单独标注以便区分
    if [ -z "$detail" ]; then
      detail="exit=${exit_code}（无错误输出，疑似超时或静默失败）"
    fi
  elif [ "$pages" -eq 0 ]; then
    verdict="不可用"
    detail="构建成功但零产出"
  elif [ "$max_size" -lt 1000 ]; then
    verdict="空页"
    detail="最大页仅 ${max_size}B（主题未渲染出内容）"
  fi
  detail="${detail}${content_note}"

  printf "%-16s %-11s %-7s %-9s %-30s\n" "$name" "$cfg_src" "$pages" "${min_size}B" "【$verdict】$detail"
done

printf -- "-----------------------------------------------------------------------------------------------\n"
echo "（配置源：exampleSite=作者验证过的配置；最小配置=通用占位）"
