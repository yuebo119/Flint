#!/usr/bin/env bash
# 主题画廊案例站：把 gallery-source/ 源码落地为 gallery/，构建并按需启动服务
#
# 用法（仓库根执行）:
#   bash demo-sites/build-gallery.sh          # 仅构建
#   SERVE=1 bash demo-sites/build-gallery.sh  # 构建后在 8400 端口启动静态服务
#
# 注意：gallery/static/shots/ 下的主题预览图由截图流程生成（先截图后构建），
# 本脚本只覆盖 hugo.toml/layouts/content，不触碰 static/。

set -u

# 项目内自定位：本脚本位于案例站项目根（<repo>/demo-sites/）
SELF_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SELF_DIR/.." && pwd)"
FLINT_SRC="$REPO_ROOT"
FLINT="$FLINT_SRC/src/Flint.Cli/bin/Release/net10.0/win-x64/Flint.exe"
GALLERY_SRC="$SELF_DIR/gallery-source"
SITE="$SELF_DIR/gallery"
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

if [ "${SERVE:-0}" = "1" ]; then
  (cd "$SITE/public" && python -m http.server "$PORT" --bind 127.0.0.1 > /dev/null 2>&1 &)
  echo "画廊已启动: http://127.0.0.1:$PORT/"
fi
exit 0
