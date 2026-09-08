# 多语料转换器：将真实世界 markdown 语料转换为 Flint/Hugo 可构建的统一站点
# 来源与规则：
#   mdn-content  files/en-us/**/index.md —— 逐行剔除 {{...}} 宏行，title 从首个 # 标题生成
#   k8s-website  content/en/**/*.md      —— 剔除含 {{< 短码的页面，保留原 front matter
#   progit       剔除（已迁 AsciiDoc，无 markdown 源）
# 用法: python scripts/corpus-convert.py --out <root>  （生成 <root>/{hugo,flint}/）

import argparse
import os
import re
import shutil

MACRO_LINE = re.compile(r"^\s*\{\{[^}]*\}\}\s*$")
INLINE_MACRO = re.compile(r"\{\{[^}]*\}\}")
FM_KEY = re.compile(r"^\s*([A-Za-z][\w-]*)\s*:")
SHORTCODE = re.compile(r"\{\{[<%]")


def strip_macros(text: str) -> str:
    """删除整行宏与行内宏，返回清理后的文本"""
    kept = []
    for line in text.split("\n"):
        if MACRO_LINE.match(line):
            continue
        kept.append(INLINE_MACRO.sub("", line))
    return "\n".join(kept)


def extract_title(text: str) -> str:
    for line in text.split("\n"):
        if line.startswith("# "):
            return line[2:].strip().strip('"')
    return "Untitled"


def convert_mdn(mdn_root: str, engine: str, site: str) -> int:
    src = os.path.join(mdn_root, "files", "en-us")
    count = 0
    for dirpath, dirs, files in os.walk(src):
        if "index.md" not in files:
            continue
        rel = os.path.relpath(dirpath, src)
        target_dir = os.path.join(site, "content", "mdn", rel.replace(os.sep, "/"))
        os.makedirs(target_dir, exist_ok=True)
        src_file = os.path.join(dirpath, "index.md")
        raw = open(src_file, encoding="utf-8", errors="ignore").read()
        cleaned = strip_macros(raw)
        # 无 front matter：生成 title（首个 # 标题）
        title = extract_title(cleaned)
        body = re.sub(r"^# .*$", "", cleaned, count=1, flags=re.M).strip()
        with open(os.path.join(target_dir, "index.md"), "w", encoding="utf-8") as f:
            f.write(f'---\ntitle: "{title}"\n---\n\n{body}\n')
        count += 1
    return count


def convert_k8s(k8s_root: str, engine: str, site: str) -> int:
    src = os.path.join(k8s_root, "content", "en", "docs")
    count = 0
    for dirpath, dirs, files in os.walk(src):
        rel = os.path.relpath(dirpath, src)
        target_dir = os.path.join(site, "content", "k8s", rel.replace(os.sep, "/"))
        os.makedirs(target_dir, exist_ok=True)
        for f in files:
            if not f.endswith(".md"):
                continue
            fp = os.path.join(dirpath, f)
            text = open(fp, encoding="utf-8", errors="ignore").read()
            if SHORTCODE.search(text):
                continue  # 短码密集页剔除
            if re.search(r"^_build\s*:", text, re.M):
                continue  # 含已移除的 _build 键（Hugo 0.145+ 拒绝）
            if re.search(r"^layout\s*:\s*\S+", text, re.M):
                continue  # 声明自定义 layout 的页面依赖原主题布局，双引擎均无
            shutil.copy2(fp, os.path.join(target_dir, f))
            count += 1
    return count


def write_layouts(site: str):
    os.makedirs(os.path.join(site, "layouts", "_default"), exist_ok=True)
    os.makedirs(os.path.join(site, "layouts"), exist_ok=True)
    if os.path.basename(site) == "hugo":
        with open(os.path.join(site, "hugo.toml"), "w", encoding="utf-8") as f:
            f.write('baseURL = "http://localhost:1313/"\ntitle = "Corpus"\ndisableKinds = ["taxonomy", "term", "RSS", "sitemap"]\n')
        with open(os.path.join(site, "layouts", "_default", "single.html"), "w", encoding="utf-8") as f:
            f.write("<article><h1>{{ .Title }}</h1>{{ .Content }}</article>")
        with open(os.path.join(site, "layouts", "index.html"), "w", encoding="utf-8") as f:
            f.write("<h1>Home</h1>")
        with open(os.path.join(site, "layouts", "_default", "list.html"), "w", encoding="utf-8") as f:
            f.write("<ul>{{ range .Pages }}<li>{{ .Title }}</li>{{ end }}</ul>")
    else:
        with open(os.path.join(site, "Flint.toml"), "w", encoding="utf-8") as f:
            f.write('baseURL = "http://localhost:1313/"\ntitle = "Corpus"\ndisableKinds = ["taxonomy", "term", "rss", "sitemap"]\n')
        with open(os.path.join(site, "layouts", "single.html"), "w", encoding="utf-8") as f:
            f.write("<article><h1>{{ page.title }}</h1>{{ page.content }}</article>")
        with open(os.path.join(site, "layouts", "index.html"), "w", encoding="utf-8") as f:
            f.write("<h1>Home</h1>")
        with open(os.path.join(site, "layouts", "list.html"), "w", encoding="utf-8") as f:
            f.write("<ul>{{ for p in pages }}<li>{{ p.title }}</li>{{ end }}</ul>")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mdn", default=os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "benchmarks", "corpus", "mdn-content"))
    ap.add_argument("--k8s", default=os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "benchmarks", "corpus", "k8s-website"))
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "benchmarks", "corpus", "corpus-merged"))
    args = ap.parse_args()
    shutil.rmtree(args.out, ignore_errors=True)
    for engine in ("hugo", "flint"):
        site = os.path.join(args.out, engine)
        n_mdn = convert_mdn(args.mdn, engine, site)
        n_k8s = convert_k8s(args.k8s, engine, site)
        write_layouts(site)
        print(f"{engine}: mdn={n_mdn} k8s={n_k8s}")


if __name__ == "__main__":
    main()
