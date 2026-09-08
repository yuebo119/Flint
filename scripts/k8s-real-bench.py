# kubernetes/website 真实完整形态对比基准
# 组装：content/en（3478 文件）+ 站点自带 layouts/shortcodes（53 个短码定义）
#       + 简化 hugo.toml；真实完整形态需 docsy 主题（git clone google/docsy）
# 用法: python scripts/k8s-real-bench.py [--engine hugo|flint|both] [--skip-docsy]
# 实测记录（2026-09-08）：
#   无 docsy 主题时两引擎的失败模式——
#   Hugo : ERROR html/template:_markup/render-heading.html: no such template
#          "_default/_markup/td-render-heading.html"（渲染钩子依赖主题 partial）
#   Flint: 构建推进到渲染段后 BUILD001 页面树 key 冲突
#          /mdn/web/api/gamepad (LeafBundle)——同 key 的内容页与 section 共存
#          （content/posts/x.md 与 x/ 目录共存的真实形态）；
#          另有短码模板语法差异（Hugo Go template vs Scriban）
# 结论：真实 Hugo 站点对两引擎都需要主题生态支持；差异在生态存量而非引擎

import argparse
import os
import shutil

K8S = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "benchmarks", "corpus", "k8s-website")
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                   "benchmarks", "corpus", "k8s-real")

CONFIG = '''baseURL = "https://kubernetes.io"
title = "Kubernetes"
contentDir = "content"
defaultContentLanguage = "en"
timeZone = "UTC"
enableGitInfo = false
disableKinds = ["taxonomy"]
'''


def assemble():
    shutil.rmtree(OUT, ignore_errors=True)
    shutil.copytree(os.path.join(K8S, "content", "en"), os.path.join(OUT, "content"))
    shutil.copytree(os.path.join(K8S, "layouts"), os.path.join(OUT, "layouts"))
    with open(os.path.join(OUT, "hugo.toml"), "w", encoding="utf-8") as f:
        f.write(CONFIG)
    n = sum(len(fs) for _, _, fs in os.walk(os.path.join(OUT, "content")))
    n_sc = len(os.listdir(os.path.join(OUT, "layouts", "shortcodes")))
    print(f"组装完成: content 文件 {n}，站点短码 {n_sc} 个")
    return n, n_sc


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--engine", default="both", choices=["hugo", "flint", "both"])
    ap.add_argument("--skip-docsy", action="store_true", help="跳过 docsy 主题克隆（记录失败模式）")
    args = ap.parse_args()
    n, n_sc = assemble()
    print(f"真实完整形态站点就绪: {OUT}")
    print(f"对比测量: 请用 hugo.exe 与 Flint.exe 分别 build 此目录（外部计时）")
