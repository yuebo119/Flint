import os, re, subprocess, collections

W = lambda p: subprocess.run(["cygpath", "-w", p], capture_output=True, text=True).stdout.strip()
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # 仓库根（scripts/ 上一级）
REF = re.compile(r'''(?:href|src|poster|data-src)\s*=\s*["']([^"'#?]+)["']|url\(([^)"']+)\)''')
THEMES = "ananke bearblog blog-awesome blowfish clarity console even fixit github-style hugo-book hugo-coder hugo-paper loveit m10c monochrome narrow papermod stack techdoc xmin yinyang".split()


def check(root, label):
    if not os.path.isdir(root):
        return None
    htmls = []
    for dp, _, fs in os.walk(root):
        for f in fs:
            if f.endswith(".html"):
                htmls.append(os.path.join(dp, f))
    missing = collections.Counter()
    total = 0
    for h in htmls:
        try:
            txt = open(h, encoding="utf-8", errors="replace").read()
        except Exception:
            continue
        for m in REF.finditer(txt):
            ref = (m.group(1) or m.group(2) or "").strip()
            if not ref or ref.startswith(("http://", "https://", "//", "data:", "mailto:", "javascript:", "{")):
                continue
            ref = ref.split("#")[0].split("?")[0]
            if not ref.startswith("/"):
                continue
            total += 1
            target = os.path.join(root, ref.lstrip("/").replace("/", os.sep))
            if os.path.isdir(target):
                target = os.path.join(target, "index.html")
            if not os.path.exists(target):
                missing[ref] += 1
    return total, missing


for t in THEMES:
    f = check(W(f"{ROOT}/matrix20/{t}/public-flint"), "flint")
    h = check(W(f"{ROOT}/matrix20/{t}/public"), "hugo")
    if f is None or h is None:
        print(f"{t:<14} 缺产物目录")
        continue
    ft, fm = f
    ht, hm = h
    print(f"{t:<14} Flint {len(fm):>3} 类缺失/{ft:>5} 引用    Hugo {len(hm):>3} 类缺失/{ht:>5} 引用")
    # Flint 独有的一律列出（这是"Flint 的问题"清单）；两侧共有的只列前 3
    own = [(r, n) for r, n in fm.most_common() if r not in hm]
    shared = [(r, n) for r, n in fm.most_common() if r in hm]
    for ref, n in own:
        print(f"      - {ref} ×{n}  ← Flint 独有")
    for ref, n in shared[:3]:
        print(f"      - {ref} ×{n} （Hugo 侧也有）")
