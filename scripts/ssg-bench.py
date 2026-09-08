# Flint vs Hugo 万页同数据集对比基准
# 方法：Hugo Discourse 19402 官方万页语料 + 双引擎同 content 同模板口径
# 用法: python scripts/ssg-bench.py [--pages 10000] [--flint <Flint.exe 路径>] [--hugo <hugo.exe 路径>]
# 口径: 外部高精度计时（含进程启动，两引擎同口径）、每次测量前删输出目录（冷构建）、预热 1 次 + 3 次测量取中位数
# 实测（2026-09-08, Win10 x64, .NET 10 / Hugo v0.165.0 Extended）: Flint 3361ms ≈ Hugo 3354ms (1.00x)

import argparse
import datetime
import os
import shutil
import statistics
import subprocess
import sys
import time

BODY = "\n\n".join([
    "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat.",
    "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.",
    "Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto beatae vitae dicta sunt explicabo.",
])


def generate(root: str, pages: int):
    """双引擎同 content 同模板：每页 title+date front matter + 三段正文"""
    for engine in ("hugo", "flint"):
        site = os.path.join(root, engine)
        os.makedirs(os.path.join(site, "content", "posts"), exist_ok=True)
        os.makedirs(os.path.join(site, "layouts", "_default"), exist_ok=True)
        os.makedirs(os.path.join(site, "layouts"), exist_ok=True)
        base = datetime.date(2026, 9, 8)
        for i in range(pages):
            d = base - datetime.timedelta(days=i)
            with open(os.path.join(site, "content", "posts", f"page-{i:05d}.md"), "w", encoding="utf-8") as f:
                f.write(f'---\ntitle: "Page {i}"\ndate: {d.isoformat()}T10:00:00+08:00\n---\n\n{BODY}\n')
        if engine == "hugo":
            with open(os.path.join(site, "hugo.toml"), "w", encoding="utf-8") as f:
                f.write('baseURL = "http://localhost:1313/"\ntitle = "SSG Bench"\n')
            with open(os.path.join(site, "layouts", "_default", "single.html"), "w", encoding="utf-8") as f:
                f.write("<article><h1>{{ .Title }}</h1>{{ .Content }}</article>")
            with open(os.path.join(site, "layouts", "index.html"), "w", encoding="utf-8") as f:
                f.write("<h1>Home</h1><ul>{{ range .Site.RegularPages }}<li>{{ .Title }}</li>{{ end }}</ul>")
        else:
            with open(os.path.join(site, "Flint.toml"), "w", encoding="utf-8") as f:
                f.write('baseURL = "http://localhost:1313/"\ntitle = "SSG Bench"\n')
            with open(os.path.join(site, "layouts", "single.html"), "w", encoding="utf-8") as f:
                f.write("<article><h1>{{ page.title }}</h1>{{ page.content }}</article>")
            with open(os.path.join(site, "layouts", "index.html"), "w", encoding="utf-8") as f:
                f.write("<h1>Home</h1><ul>{{ for p in site.regular_pages }}<li>{{ p.title }}</li>{{ end }}</ul>")


def bench(name: str, fn, pub: str, runs: int = 3) -> float:
    fn()  # 预热
    times = []
    for _ in range(runs):
        shutil.rmtree(pub, ignore_errors=True)
        t0 = time.perf_counter()
        fn()
        times.append((time.perf_counter() - t0) * 1000)
    med = statistics.median(times)
    runs_desc = ' '.join(f'r{i+1}={t:.0f}ms' for i, t in enumerate(times))
    print(f'{name}: {runs_desc} median={med:.0f}ms')
    return med


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pages", type=int, default=10000)
    ap.add_argument("--runs", type=int, default=3)
    ap.add_argument("--flint", default=r"src\Flint.Cli\bin\Debug\net10.0\win-x64\Flint.exe")
    ap.add_argument("--hugo", default=r"C:\Users\Andy\AppData\Local\Temp\hugo-bin\hugo.exe")
    ap.add_argument("--root", default=r"C:\Users\Andy\AppData\Local\Temp\ssg-bench")
    args = ap.parse_args()

    root = os.path.abspath(args.root)
    generate(root, args.pages)
    print(f"语料: {args.pages} 页 x 2 引擎")

    hugo_pub = os.path.join(root, "hugo-pub")
    flint_pub = os.path.join(root, "flint-pub")

    def run_hugo():
        r = subprocess.run([args.hugo, "-s", os.path.join(root, "hugo"), "-d", hugo_pub, "--quiet"],
                           capture_output=True)
        assert r.returncode == 0, r.stderr[-400:]

    def run_flint():
        r = subprocess.run([args.flint, "build", "-s", os.path.join(root, "flint"), "-o", flint_pub],
                           capture_output=True)
        assert r.returncode == 0, (r.stdout[-400:] if r.stdout else b"", r.stderr[-400:] if r.stderr else b"")

    hm = bench("Hugo", run_hugo, hugo_pub, args.runs)
    fm = bench("Flint", run_flint, flint_pub, args.runs)
    print(f"=== {args.pages} 页冷构建（{args.runs} 次中位数，外部计时含进程启动）===")
    print(f"Hugo : {hm:.0f}ms")
    print(f"Flint: {fm:.0f}ms")
    print(f"比值 Flint/Hugo = {fm / hm:.2f}x")


if __name__ == "__main__":
    sys.exit(main())
