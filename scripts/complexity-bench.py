# 复杂度阶梯对照基准：三级复杂度主题（L1/L2/L3）× 双引擎（Hugo/Scriban）
# 目标：回答"主题复杂度增长时，两引擎的扩展性曲线谁更陡"
# 等价性：同逻辑双语法实现，产物 HTML 结构一致
# 用法: python scripts/complexity-bench.py [--pages 1000] [--runs 3]

import argparse
import os
import shutil
import statistics
import subprocess
import time

HUGO = r"C:\Users\Andy\AppData\Local\Temp\hugo-bin\hugo.exe"
FLINT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                     "benchmarks", "tools", "flint-aot", "Flint.exe")
ROOT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "benchmarks", "corpus", "complexity")

BODY = "\n\n".join([
    "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.",
    "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.",
    "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur.",
])

def gen_corpus(root, pages):
    """固定语料：1000 页（语料只生成一次，双引擎共享 content 副本）"""
    base = __import__("datetime").date(2026, 9, 8)
    for engine in ("hugo", "flint"):
        cdir = os.path.join(root, engine, "content", "posts")
        shutil.rmtree(cdir, ignore_errors=True)
        os.makedirs(cdir, exist_ok=True)
        for i in range(pages):
            d = base - __import__("datetime").timedelta(days=i % 900)
            with open(os.path.join(cdir, f"page-{i:05d}.md"), "w", encoding="utf-8") as f:
                f.write(f'---\ntitle: "Page {i}"\ndate: {d.isoformat()}T10:00:00+08:00\ntags: ["t{i % 20}"]\n---\n\n{BODY}\n')

# ---------------- L1 基础 ----------------
def theme_l1(site, engine):
    ld = os.path.join(site, "layouts", "_default")
    os.makedirs(ld, exist_ok=True)
    if engine == "hugo":
        with open(os.path.join(site, "hugo.toml"), "w", encoding="utf-8") as f:
            f.write('baseURL = "http://x/"\ntitle = "C"\n')
        with open(os.path.join(ld, "single.html"), "w", encoding="utf-8") as f:
            f.write('<article><h1>{{ .Title }}</h1>{{ .Content }}</article>')
        with open(os.path.join(ld, "list.html"), "w", encoding="utf-8") as f:
            f.write('<h1>{{ .Title }}</h1>')
        with open(os.path.join(site, "layouts", "index.html"), "w", encoding="utf-8") as f:
            f.write('<h1>{{ .Site.Title }}</h1>')
    else:
        with open(os.path.join(site, "Flint.toml"), "w", encoding="utf-8") as f:
            f.write('baseURL = "http://x/"\ntitle = "C"\n')
        with open(os.path.join(ld, "single.html"), "w", encoding="utf-8") as f:
            f.write('<article><h1>{{ page.title }}</h1>{{ page.content }}</article>')
        with open(os.path.join(ld, "list.html"), "w", encoding="utf-8") as f:
            f.write('<h1>{{ page.title }}</h1>')
        with open(os.path.join(site, "layouts", "index.html"), "w", encoding="utf-8") as f:
            f.write('<h1>{{ site.title }}</h1>')

# ---------------- L2 中等：侧边栏全站循环 + partial ----------------
SIDEBAR_HUGO = '''<aside>{{ range .Site.RegularPages }}<li><a href="{{ .RelPermalink }}">{{ .Title }}</a></li>{{ end }}</aside>'''
SIDEBAR_SCRIBAN = '''<aside>{{ for p in site.regular_pages }}<li><a href="{{ p.rel_permalink }}">{{ p.title }}</a></li>{{ end }}</aside>'''

def theme_l2(site, engine):
    theme_l1(site, engine)
    ld = os.path.join(site, "layouts", "_default")
    sc = os.path.join(site, "layouts", "partials")
    os.makedirs(sc, exist_ok=True)
    sb = SIDEBAR_HUGO if engine == "hugo" else SIDEBAR_SCRIBAN
    with open(os.path.join(sc, "sidebar.html"), "w", encoding="utf-8") as f:
        f.write(sb)
    if engine == "hugo":
        with open(os.path.join(ld, "single.html"), "w", encoding="utf-8") as f:
            f.write('<nav>{{ partial "sidebar.html" . }}</nav><article><h1>{{ .Title }}</h1>{{ .Content }}</article>')
    else:
        with open(os.path.join(ld, "single.html"), "w", encoding="utf-8") as f:
            f.write('<nav>{{ include "sidebar" }}</nav><article><h1>{{ page.title }}</h1>{{ page.content }}</article>')

# ---------------- L3 重度：双 O(N) 循环 + 嵌套 partial + partialCached 对照 ----------------
NAV_HUGO = '''<nav>{{ range .Site.RegularPages }}<a href="{{ .RelPermalink }}">{{ .Title }}</a>{{ end }}</nav>'''
NAV_SCRIBAN = '''<nav>{{ for p in site.regular_pages }}<a href="{{ p.rel_permalink }}">{{ p.title }}</a>{{ end }}</nav>'''
RELATED_HUGO = '''<div>{{ range .Site.RegularPages }}{{ if eq .Section "posts" }}<span>{{ .Title }}</span>{{ end }}{{ end }}</div>'''
RELATED_SCRIBAN = '''<div>{{ for p in site.regular_pages }}{{ if p.type == "page" }}<span>{{ p.title }}</span>{{ end }}{{ end }}</div>'''
FOOTER = '<footer>C</footer>'

def theme_l3(site, engine):
    theme_l2(site, engine)
    ld = os.path.join(site, "layouts", "_default")
    sc = os.path.join(site, "layouts", "partials")
    nav = NAV_HUGO if engine == "hugo" else NAV_SCRIBAN
    rel = RELATED_HUGO if engine == "hugo" else RELATED_SCRIBAN
    with open(os.path.join(sc, "nav.html"), "w", encoding="utf-8") as f:
        f.write(nav)
    with open(os.path.join(sc, "related.html"), "w", encoding="utf-8") as f:
        f.write(rel)
    with open(os.path.join(sc, "footer.html"), "w", encoding="utf-8") as f:
        f.write(FOOTER)
    if engine == "hugo":
        with open(os.path.join(ld, "single.html"), "w", encoding="utf-8") as f:
            f.write('<nav>{{ partial "nav.html" . }}</nav>{{ partial "sidebar.html" . }}<article><h1>{{ .Title }}</h1>{{ .Content }}</article>{{ partial "related.html" . }}{{ partialCached "footer.html" . }}')
    else:
        with open(os.path.join(ld, "single.html"), "w", encoding="utf-8") as f:
            f.write('<nav>{{ include "nav" }}</nav>{{ include "sidebar" }}<article><h1>{{ page.title }}</h1>{{ page.content }}</article>{{ include "related" }}{{ partialcached "footer" }}')

THEMES = {"L1": theme_l1, "L2": theme_l2, "L3": theme_l3}

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pages", type=int, default=1000)
    ap.add_argument("--runs", type=int, default=3)
    args = ap.parse_args()

    gen_corpus(ROOT, args.pages)

    jobs = []
    for level, theme_fn in THEMES.items():
        for engine in ("hugo", "flint"):
            site = os.path.join(ROOT, f"{engine}-{level}")
            shutil.rmtree(site, ignore_errors=True)
            # 复制共享语料
            shutil.copytree(os.path.join(ROOT, "hugo", "content"), os.path.join(site, "content"))
            theme_fn(site, engine)
            pub = os.path.join(site, "pub")
            jobs.append((f"{level}-{engine}", engine, site, pub))

    print(f"\n=== 复杂度阶梯对照（{args.pages} 页 × {args.runs} 次中位数）===")
    matrix = {}
    for name, engine, site, pub in jobs:
        if engine == "hugo":
            cmd = [HUGO, "-s", site, "-d", pub, "--quiet"]
        else:
            cmd = [FLINT, "build", "-s", site, "-o", pub]
        times = []
        for i in range(args.runs + 1):  # 首轮预热
            shutil.rmtree(pub, ignore_errors=True)
            t0 = time.perf_counter()
            r = subprocess.run(cmd, capture_output=True)
            el = (time.perf_counter() - t0) * 1000
            assert r.returncode == 0, (name, r.stderr[-300:] if r.stderr else r.stdout[-300:])
            if i > 0:
                times.append(el)
        med = statistics.median(times)
        matrix[name] = med
        print(f"{name}: " + " ".join(f"{t:.0f}" for t in times) + f" | median={med:.0f}ms")

    print("\n=== 复杂度-性能曲线（中位数 ms）===")
    print(f"{'层级':<6}{'Hugo':>10}{'Flint':>10}{'比值':>8}")
    for lv in ("L1", "L2", "L3"):
        h = matrix[f"{lv}-hugo"]; f_ = matrix[f"{lv}-flint"]
        print(f"{lv:<6}{h:>10.0f}{f_:>10.0f}{f_/h:>8.2f}")

if __name__ == "__main__":
    main()
