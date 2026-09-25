import os, re, subprocess, collections
from collections import Counter

W = lambda p: subprocess.run(["cygpath", "-w", p], capture_output=True, text=True).stdout.strip()
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 仓库根（scripts/ 上一级）
THEMES = "ananke bearblog blog-awesome blowfish clarity console even fixit github-style hugo-book hugo-coder hugo-paper loveit m10c monochrome narrow papermod stack techdoc xmin yinyang".split()


def norm(h):
    h = re.sub(r"<script[^>]*>.*?</script>", "", h, flags=re.S | re.I)
    h = re.sub(r"<style[^>]*>.*?</style>", "", h, flags=re.S | re.I)
    h = re.sub(r"[0-9a-f]{8,64}", "HASH", h)
    return re.sub(r"\s+", " ", h).strip()


TAG = re.compile(r"<([a-zA-Z][\w-]*)\b[^>]*>")
CLS = re.compile(r'\bclass\s*=\s*"([^"]*)"', re.I)
META = re.compile(r'\b(?:property|name)\s*=\s*"([^"]*)"', re.I)


def sigs(h):
    c = Counter()
    for m in TAG.finditer(h):
        tag = m.group(1).lower()
        if tag == "meta":
            k = META.search(m.group(0))
            if not k:
                continue
            c["meta[" + k.group(1).lower() + "]"] += 1
            continue
        if tag in ("br", "link", "input", "img", "hr", "source", "track"):
            continue
        cm = CLS.search(m.group(0))
        cls = ".".join(sorted(set(cm.group(1).lower().split()))) if cm else ""
        c[tag + ("." + cls if cls else "")] += 1
    return c


def pages(root):
    d = {}
    for dp, _, fs in os.walk(root):
        for f in fs:
            if f.endswith(".html"):
                p = os.path.join(dp, f)
                d[os.path.relpath(p, root).replace("\\", "/")] = p
    return d


miss_total = collections.Counter()
miss_pages = collections.Counter()
extra_total = collections.Counter()
extra_pages = collections.Counter()
for t in THEMES:
    H = pages(W(f"{ROOT}/matrix20/{t}/public"))
    F = pages(W(f"{ROOT}/matrix20/{t}/public-flint"))
    for rel in sorted(set(H) & set(F)):
        h = sigs(norm(open(H[rel], encoding="utf-8", errors="replace").read()))
        f = sigs(norm(open(F[rel], encoding="utf-8", errors="replace").read()))
        for k, n in (h - f).items():
            miss_total[k] += n
            miss_pages[k] += 1
        for k, n in (f - h).items():
            extra_total[k] += n
            extra_pages[k] += 1

print("=== 缺元素 Top 25（Hugo 有、Flint 缺；按涉及页面数）===")
for k, n in miss_pages.most_common(25):
    print(f"{n:4d} 页 / {miss_total[k]:5d} 处   {k[:100]}")
print()
print("=== 多元素 Top 15（Flint 多出）===")
for k, n in extra_pages.most_common(15):
    print(f"{n:4d} 页 / {extra_total[k]:5d} 处   {k[:100]}")
