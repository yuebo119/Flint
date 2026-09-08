# 内存峰值采样：三语料 × 双引擎构建进程的峰值 RSS / USS（psutil 实测）
# 口径：每语料 2 轮取中位数；USS = 进程独占内存（不受系统换页影响）
# 用法: python scripts/memory-bench.py [--runs 2]

import argparse
import os
import shutil
import statistics
import subprocess
import time

import psutil

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HUGO = r"C:\Users\Andy\AppData\Local\Temp\hugo-bin\hugo.exe"
FLINT = os.path.join(BASE, "benchmarks", "tools", "flint-aot", "Flint.exe")
CB = os.path.join(BASE, "benchmarks", "corpus")

JOBS = [
    ("万页合成-Hugo",  HUGO,  ["-s", os.path.join(CB, "ssg-bench", "hugo"), "-d", os.path.join(CB, "ssg-bench", "hugo-pub"), "--quiet"], os.path.join(CB, "ssg-bench", "hugo-pub")),
    ("万页合成-Flint", FLINT, ["build", "-s", os.path.join(CB, "ssg-bench", "flint"), "-o", os.path.join(CB, "ssg-bench", "flint-pub")], os.path.join(CB, "ssg-bench", "flint-pub")),
    ("官方3978-Hugo",  HUGO,  ["-s", os.path.join(CB, "hbench-merged", "hugo"), "-d", os.path.join(CB, "hbench-merged", "hugo-pub"), "--quiet"], os.path.join(CB, "hbench-merged", "hugo-pub")),
    ("官方3978-Flint", FLINT, ["build", "-s", os.path.join(CB, "hbench-merged", "flint"), "-o", os.path.join(CB, "hbench-merged", "flint-pub")], os.path.join(CB, "hbench-merged", "flint-pub")),
    ("MDN14621-Hugo",  HUGO,  ["-s", os.path.join(CB, "corpus-merged", "hugo"), "-d", os.path.join(CB, "corpus-merged", "hugo-pub"), "--quiet"], os.path.join(CB, "corpus-merged", "hugo-pub")),
    ("MDN14621-Flint", FLINT, ["build", "-s", os.path.join(CB, "corpus-merged", "flint"), "-o", os.path.join(CB, "corpus-merged", "flint-pub")], os.path.join(CB, "corpus-merged", "flint-pub")),
]


def probe(exe, build_args, pub):
    shutil.rmtree(pub, ignore_errors=True)
    p = subprocess.Popen([exe] + build_args, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    proc = psutil.Process(p.pid)
    peak_rss = 0
    peak_uss = 0
    while p.poll() is None:
        try:
            m = proc.memory_full_info()
            peak_rss = max(peak_rss, m.rss)
            peak_uss = max(peak_uss, m.uss)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            break
        time.sleep(0.02)
    p.wait()
    return peak_rss / 1048576, peak_uss / 1048576


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=2)
    args = ap.parse_args()
    print(f"=== 内存峰值采样（{args.runs} 轮中位数，MB）===")
    for name, exe, build_args, pub in JOBS:
        rss_list = []
        uss_list = []
        for _ in range(args.runs):
            rss, uss = probe(exe, build_args, pub)
            rss_list.append(rss)
            uss_list.append(uss)
        print(f"{name}: rss中位={statistics.median(rss_list):.0f}MB uss中位={statistics.median(uss_list):.0f}MB")


if __name__ == "__main__":
    main()
