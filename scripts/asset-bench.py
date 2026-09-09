#!/usr/bin/env python3
# L4 语料基准：1000 页 × L3 主题 + 图片资产（ImageSharp 解码/重编码）+ Sass 编译
# 目的：补已知局限 #2（图片管线从未进入测试）——Flint 自身资源管线画像
# 用法：python scripts/asset-bench.py [--pages 1000] [--images 100] [--runs 3]
import argparse, os, shutil, statistics, struct, subprocess, sys, time, zlib

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FLINT = os.path.join(BASE, "benchmarks", "tools", "flint-aot", "Flint.exe")
ROOT = os.path.join(BASE, "benchmarks", "corpus", "asset-bench")

def make_png(path, w, h, seed):
    """手写噪声 PNG（zlib/struct，无依赖）：随机像素压不动，解码负载真实"""
    rng = seed
    raw = bytearray()
    for _ in range(h):
        raw.append(0)  # filter: none
        row = bytearray(w * 3)
        for x in range(w):
            rng = (1103515245 * rng + 12345) & 0x7FFFFFFF
            row[x*3] = rng & 0xFF
            row[x*3+1] = (rng >> 8) & 0xFF
            row[x*3+2] = (rng >> 16) & 0xFF
        raw.extend(row)
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    ihdr = struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", ihdr))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 1)))
        f.write(chunk(b"IEND", b""))

SCSS = """$primary: #2b6cb0;
$radius: 4px;
@mixin card($pad: 12px) {
  padding: $pad;
  border-radius: $radius;
  box-shadow: 0 1px 3px rgba(0,0,0,.12);
}
@each $name, $color in (a: #e53e3e, b: #38a169, c: #d69e2e, d: #805ad5, e: #319795) {
  .badge-#{$name} { color: $color; border: 1px solid darken($color, 10%); @include card(6px); }
}
nav.site {
  ul { display: flex; gap: 8px; li { a { color: $primary; &:hover { text-decoration: underline; } } } }
}
@for $i from 1 through 40 {
  .col-#{$i} { width: 100% / $i; @if $i % 5 == 0 { border-left: 1px solid #eee; } }
}
article { @include card; h1 { font-size: 1.6rem; color: $primary; } p code { background: #f7fafc; } }
"""

def gen_corpus(site, pages, images):
    content = os.path.join(site, "content", "posts")
    layouts = os.path.join(site, "layouts", "_default")
    partials = os.path.join(site, "layouts", "partials")
    imgdir = os.path.join(site, "assets", "images")
    styledir = os.path.join(site, "assets", "styles")
    for d in (content, layouts, partials, imgdir, styledir):
        os.makedirs(d, exist_ok=True)
    with open(os.path.join(site, "Flint.toml"), "w", encoding="utf-8") as f:
        f.write('baseURL = "http://localhost:1313/"\ntitle = "Asset Bench"\n')
    with open(os.path.join(layouts, "single.html"), "w", encoding="utf-8") as f:
        f.write('<nav>{{ include "nav" }}</nav>{{ include "sidebar" }}<article><h1>{{ page.title }}</h1>{{ page.content }}</article>{{ include "related" }}{{ partialcached "footer" }}')
    with open(os.path.join(partials, "sidebar.html"), "w", encoding="utf-8") as f:
        f.write('<aside>{{ for p in site.regular_pages }}<li><a href="{{ p.rel_permalink }}">{{ p.title }}</a></li>{{ end }}</aside>')
    with open(os.path.join(partials, "nav.html"), "w", encoding="utf-8") as f:
        f.write('<nav>{{ for p in site.regular_pages }}<a href="{{ p.rel_permalink }}">{{ p.title }}</a>{{ end }}</nav>')
    with open(os.path.join(partials, "related.html"), "w", encoding="utf-8") as f:
        f.write('<div>{{ for p in site.regular_pages }}{{ if p.type == "page" }}<span>{{ p.title }}</span>{{ end }}{{ end }}</div>')
    with open(os.path.join(partials, "footer.html"), "w", encoding="utf-8") as f:
        f.write("<footer>C</footer>")
    with open(os.path.join(site, "layouts", "index.html"), "w", encoding="utf-8") as f:
        f.write("<h1>home</h1>")
    for i in range(pages):
        with open(os.path.join(content, f"p{i:04d}.md"), "w", encoding="utf-8") as f:
            f.write(f'---\ntitle: "Post {i}"\ndate: 2026-01-01\ntags: ["t{i % 20}"]\n---\n\nBody {i} lorem ipsum.')
    print(f"生成 {images} 张 PNG（640x480 噪声）...", file=sys.stderr)
    for i in range(images):
        make_png(os.path.join(imgdir, f"img_{i:03d}.png"), 640, 480, seed=i + 7)
    with open(os.path.join(styledir, "main.scss"), "w", encoding="utf-8") as f:
        f.write(SCSS)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pages", type=int, default=1000)
    ap.add_argument("--images", type=int, default=100)
    ap.add_argument("--runs", type=int, default=3)
    args = ap.parse_args()
    site = os.path.join(ROOT, "site")
    pub = os.path.join(ROOT, "site-pub")
    if os.path.exists(site):
        shutil.rmtree(site)
    gen_corpus(site, args.pages, args.images)
    print(f"L4 语料就绪：{args.pages} 页 × L3 主题 + {args.images} 图 + Sass", file=sys.stderr)
    import psutil
    times, peaks = [], []
    for i in range(args.runs + 1):  # 首轮预热（含磁盘缓存建立）
        shutil.rmtree(pub, ignore_errors=True)
        t0 = time.perf_counter()
        env = {**os.environ, "FLINT_TRACE_PHASES": "1"}
        p = subprocess.Popen([FLINT, "build", "-s", site, "-o", pub],
                             stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, env=env)
        proc = psutil.Process(p.pid)
        peak = 0
        while p.poll() is None:
            try:
                peak = max(peak, proc.memory_info().rss)
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                pass
            time.sleep(0.02)
        el = (time.perf_counter() - t0) * 1000
        assert p.returncode == 0, f"构建失败 rc={p.returncode}"
        peaks.append(peak / 2**20)
        if i > 0:
            times.append(el)
        if i == 0:
            print("--- 预热轮阶段明细 ---", file=sys.stderr)
            print(p.stderr.read().decode("utf-8", "replace"), file=sys.stderr)
    print(f"\n=== L4 资源管线画像（{args.runs} 轮中位）===")
    print(f"构建时间中位: {statistics.median(times):.0f}ms")
    print(f"内存峰值中位: {statistics.median(peaks):.0f}MB")
    html = sum(len(files) for _, _, files in os.walk(pub))
    print(f"产出文件数: {html}")

if __name__ == "__main__":
    main()
