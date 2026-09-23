#!/usr/bin/env bash
# 主题画廊案例站：把 scripts/gallery 源码落地为 demo-gallery/，构建并**合并进
# 统一服务树**（demo-unified/ 根 = 画廊入口，各主题在 /<主题>/ 子路径）
#
# 用法:
#   bash scripts/build-gallery.sh          # 仅构建并合并
#   SERVE=1 bash scripts/build-gallery.sh  # 合并后在 8400 端口启动统一服务
#
# 注意：static/shots/ 下的主题预览图由截图流程生成（先截图后构建），
# 本脚本只覆盖 hugo.toml/layouts/content，不触碰 static/。

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
FLINT="$FLINT_SRC/src/Flint.Cli/bin/Release/net10.0/win-x64/Flint.exe"
GALLERY_SRC="$SELF_DIR/gallery"
SITE="$REPO_ROOT/demo-gallery"
UNIFIED="$REPO_ROOT/demo-unified"
PORT=8400

[ -x "$FLINT" ] || { echo "未找到 Flint.exe: $FLINT" >&2; exit 1; }

# 只清理**可再生部分**（public/layouts/content/配置）；static/shots/ 下的
# 主题预览图由截图流程生成（先截图后构建），必须保留——整站 rm -rf 会把
# 21 张预览图一起删掉（实测踩坑）
rm -rf "$SITE/public" "$SITE/layouts" "$SITE/content" "$SITE/hugo.toml" "$SITE/Flint.toml"
mkdir -p "$SITE/static/shots"
cp "$GALLERY_SRC/hugo.toml" "$SITE/"
cp -r "$GALLERY_SRC/layouts" "$SITE/"
cp -r "$GALLERY_SRC/content" "$SITE/"

(cd "$SITE" && "$FLINT" build -s . -o public --clean 2>&1)
bexit=$?
pages=$(find "$SITE/public" -name "*.html" 2>/dev/null | wc -l)
shots=$(find "$SITE/static/shots" -name "*.png" 2>/dev/null | wc -l)
echo "画廊构建: exit=$bexit 页数=$pages 预览图=$shots"
if [ "$bexit" -ne 0 ] || [ "$pages" -eq 0 ]; then
  echo "画廊构建失败" >&2
  exit 1
fi

# 合并到统一服务树根部（index.html + shots/ + favicon 等），与各主题子路径共存
mkdir -p "$UNIFIED"
cp -r "$SITE/public/." "$UNIFIED/"
echo "画廊已合并进统一树: $UNIFIED/（入口 index.html，主题在 /<主题>/）"

# 根级字体兼容：个别主题（hugo-coder）的模板**硬编码** `/fonts/...` 绝对路径
# （原始 Hugo 主题写法，baseURL 带子路径时同样断——非 Flint 引擎问题）。
# 归并单端口后把各主题 static/fonts 汇总复制到统一树根，让硬编码路径仍可访问
mkdir -p "$UNIFIED/fonts"
font_themes=0
for theme_fonts in "$REPO_ROOT/tools/themes"/*/static/fonts; do
  [ -d "$theme_fonts" ] || continue
  cp -rn "$theme_fonts/." "$UNIFIED/fonts/" 2>/dev/null || true
  font_themes=$((font_themes + 1))
done
[ "$font_themes" -gt 0 ] && echo "根级字体兼容: $font_themes 个主题的 static/fonts → $UNIFIED/fonts/"

# 各主题 site.webmanifest 的图标路径改成**带主题子路径的绝对路径**：静态文件按
# 原样拷贝不过 relURL，相对路径会被 Chrome 按站点根解析（/loveit/site.webmanifest
# 里的 images/favicon.svg 实测请求 /images/favicon.svg → 404）
manifest_themes=0
for theme_dir in "$UNIFIED"/*/; do
  tname=$(basename "$theme_dir")
  [ -f "$theme_dir/site.webmanifest" ] || continue
  sed -e "s|\"src\": \"images/|\"src\": \"/$tname/images/|g" \
      "$theme_dir/site.webmanifest" > "$theme_dir/site.webmanifest.tmp" &&
    mv "$theme_dir/site.webmanifest.tmp" "$theme_dir/site.webmanifest"
  manifest_themes=$((manifest_themes + 1))
done
[ "$manifest_themes" -gt 0 ] && echo "manifest 图标路径已带子路径前缀: $manifest_themes 个主题"

if [ "${SERVE:-0}" = "1" ]; then
  (cd "$UNIFIED" && python -m http.server "$PORT" --bind 127.0.0.1 > /dev/null 2>&1 &)
  echo "统一服务已启动: http://127.0.0.1:$PORT/"
fi
exit 0
