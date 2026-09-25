# -*- coding: utf-8 -*-
"""统计语料库每篇文章的中文字符数（front matter 不计）

用法:
  python count_chars.py            # 全部 section
  python count_chars.py posts      # 指定 section
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CONTENT = os.path.join(HERE, "content")
CJK = re.compile(r"[\u4e00-\u9fff]")


def split_front_matter(text):
    if not text.startswith("---"):
        return text
    end = text.find("\n---", 3)
    if end < 0:
        return text
    return text[end + 4:]


def main():
    sections = sys.argv[1:] or [
        d for d in os.listdir(CONTENT)
        if os.path.isdir(os.path.join(CONTENT, d))
    ]
    total = 0
    bad = []
    for sec in sorted(sections):
        d = os.path.join(CONTENT, sec)
        if not os.path.isdir(d):
            continue
        files = sorted(f for f in os.listdir(d) if f.endswith(".md"))
        sec_total = 0
        for f in files:
            path = os.path.join(d, f)
            with open(path, encoding="utf-8") as fh:
                body = split_front_matter(fh.read())
            n = len(CJK.findall(body))
            sec_total += n
            total += 1
            if n < 2000 and f != "_index.md":
                bad.append((sec, f, n))
        print(f"[{sec}] {len(files)} 篇，中文字符合计 {sec_total}")
    print(f"---- 共 {total} 个 markdown 文件")
    if bad:
        print("!! 不足 2000 字:")
        for sec, f, n in bad:
            print(f"   {sec}/{f}: {n}")
        sys.exit(1)
    print("全部达标（>=2000 中文字符）")


if __name__ == "__main__":
    main()
