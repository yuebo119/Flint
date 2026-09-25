# GitHub 发布页标准工具（CONVENTIONS §10.2 发布流第 3 步）
#
# 用法：
#   python scripts/release-github.py --tag v0.2.0 --notes notes.md   # 建页 + 等待四平台资产
#   python scripts/release-github.py --tag v0.1.0 --dispatch         # 补建：派发流水线
#   python scripts/release-github.py --tag v0.1.0 --wait-only        # 只等待（页已存在）
#   python scripts/release-github.py --verify                        # 只读体检（无需凭据）
#
# 规则（§10.2 硬规则的机械执行）：
#   - 标题 = 纯版本号（不带后缀）
#   - 变更日志必须来自 --notes 文件，且禁止 commit 链接/git 日志直贴（自动拒绝）
#   - 凭据取自 git credential store，全程不回显不落盘
import argparse
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")
REPO = "yuebo119/Flint"
API = f"https://api.github.com/repos/{REPO}"
WF = "release-assets.yml"
DEFAULT_REF = "main"         # release 触发从默认分支读流水线（2026-09-25 裁决：默认分支=main，
                              # 发布流先 dev→main 合并再建页；--dispatch 补建也走 main）
PROXIES = [None, {"https": "http://127.0.0.1:50001", "http": "http://127.0.0.1:50001"}]


def api(url, token=None, data=None, content_type=None, method=None, timeout=300):
    headers = {"Accept": "application/vnd.github+json",
               "User-Agent": "flint-release-github"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    if isinstance(data, dict):
        data = json.dumps(data).encode()
        headers["Content-Type"] = "application/json"
    elif content_type:
        headers["Content-Type"] = content_type
    last = None
    for px in PROXIES:
        handlers = [urllib.request.ProxyHandler(px)] if px else []
        opener = urllib.request.build_opener(*handlers)
        req = urllib.request.Request(url, data=data, headers=headers,
                                     method=method or ("POST" if data else "GET"))
        try:
            with opener.open(req, timeout=timeout) as resp:
                raw = resp.read()
                return json.loads(raw) if raw else {"_status": resp.status}
        except urllib.error.HTTPError as e:
            raw = e.read()
            try:
                return json.loads(raw) if raw else {"_http_error": e.code}
            except Exception:  # noqa: BLE001
                return {"_http_error": e.code}
        except Exception as e:  # noqa: BLE001
            last = e
            continue
    return {"_net_error": f"{type(last).__name__}: {str(last)[:100]}" if last else "unknown"}


def git_password():
    p = subprocess.run(["git", "credential", "fill"],
                       input="protocol=https\nhost=github.com\n\n",
                       capture_output=True, text=True, timeout=30)
    for line in p.stdout.splitlines():
        if line.startswith("password="):
            return line.split("=", 1)[1]
    return None


def print_assets(rel):
    assets = rel.get("assets", [])
    print(f"资产 {len(assets)} 项:")
    for a in assets:
        print(f"  {a['name']} | {a['size']:,} bytes | label={a['label']!r}")


def load_notes(path):
    if not path or not os.path.exists(path):
        sys.exit("错误：--notes 必须指向存在的变更日志文件（总结式：主要特性/主要更新/主要更改）")
    body = open(path, encoding="utf-8").read().strip()
    if not body:
        sys.exit("错误：变更日志文件为空")
    if "/commit/" in body or "commit/" in body.replace(" ", ""):
        sys.exit("错误：变更日志禁止 commit 链接/git 日志直贴——请写总结（主要特性/主要更新/主要更改）")
    return body


def wait_assets(token, tag, expect, timeout_s):
    deadline = time.time() + timeout_s
    while time.time() < deadline:
        rels = api(f"{API}/releases", token)
        rel = next((r for r in rels if r["tag_name"] == tag), None)
        if rel and len(rel.get("assets", [])) >= expect:
            print(f"资产齐备（{len(rel['assets'])}/{expect}）:")
            print_assets(rel)
            bad = [a["name"] for a in rel["assets"] if a.get("label")]
            if bad:
                sys.exit(f"错误：以下资产带 label（应直显文件名）: {bad}")
            return 0
        n = len(rel["assets"]) if rel else 0
        print(f"  等待资产 {n}/{expect} ...")
        time.sleep(15)
    print("超时：资产未达预期数")
    return 1


def main():
    ap = argparse.ArgumentParser(description="Flint GitHub 发布页标准工具")
    ap.add_argument("--tag", help="标签（如 v0.2.0）")
    ap.add_argument("--notes", help="变更日志文件（总结式，禁止 git 日志）")
    ap.add_argument("--expect", type=int, default=4, help="预期资产数（默认 4 = 四平台）")
    ap.add_argument("--timeout", type=int, default=780, help="等待秒数（默认 780）")
    ap.add_argument("--dispatch", action="store_true", help="派发 release-assets 流水线（补建场景）")
    ap.add_argument("--wait-only", action="store_true", help="不建页，只等待既有页的资产")
    ap.add_argument("--update-notes", action="store_true",
                    help="更新既有发布页的说明正文（需 --tag 与 --notes，守卫同建页）")
    ap.add_argument("--verify", action="store_true", help="只读体检（无需凭据）")
    args = ap.parse_args()

    if args.verify:
        rels = api(f"{API}/releases")
        print(f"releases={len(rels)}")
        ok = True
        for r in rels:
            print(f"  tag={r['tag_name']} name={r['name']!r} draft={r['draft']} assets={len(r['assets'])}")
            if r["name"] != r["tag_name"]:
                ok = False
                print("    ✗ 标题不是纯版本号")
            for a in r["assets"]:
                print(f"    {a['name']} | {a['size']:,} bytes | label={a['label']!r}")
                if a.get("label"):
                    ok = False
                    print("    ✗ 资产带 label")
        tags = subprocess.run(["git", "ls-remote", "--tags", "origin"],
                              capture_output=True, text=True, timeout=60).stdout
        print("远端 tags:", [l.split("refs/tags/")[-1] for l in tags.splitlines()
                             if l and "^{}" not in l])
        print("体检:", "PASS" if ok else "FAIL")
        return 0 if ok else 1

    if not args.tag:
        sys.exit("错误：需要 --tag")
    token = git_password()
    if not token:
        sys.exit("错误：git 凭据库中无 github.com 凭据")

    if args.update_notes:
        if not args.tag:
            sys.exit("错误：--update-notes 需要 --tag")
        body = load_notes(args.notes)
        rels = api(f"{API}/releases", token)
        rel = next((r for r in rels if r["tag_name"] == args.tag), None)
        if not rel:
            sys.exit(f"错误：未找到 {args.tag} 的发布页")
        out = api(f"{API}/releases/{rel['id']}", token,
                  data={"body": body}, method="PATCH")
        if out.get("body") == body:
            print(f"说明页已更新: {out['html_url']}")
            return 0
        sys.exit(f"更新失败: {str(out)[:200]}")

    if args.dispatch:
        out = api(f"{API}/actions/workflows/{WF}/dispatches", token,
                  data={"ref": DEFAULT_REF, "inputs": {"tag": args.tag}})
        if out.get("_status") != 204:
            sys.exit(f"派发失败: {out}")
        print(f"已派发 {WF}（ref={DEFAULT_REF}, tag={args.tag}）")
        time.sleep(10)

    if not args.wait_only:
        if not args.tag.startswith("v"):
            sys.exit("错误：标签须为 vX.Y.Z 格式")
        body = load_notes(args.notes)
        rel = api(f"{API}/releases", token, data={
            "tag_name": args.tag, "name": args.tag, "body": body,
            "draft": False, "prerelease": False,
        })
        if "id" not in rel:
            sys.exit(f"建页失败: {rel}")
        print(f"发布页已创建: {rel['html_url']}")
        print("published 事件将自动触发 release-assets 流水线")

    return wait_assets(token, args.tag, args.expect, args.timeout)


if __name__ == "__main__":
    sys.exit(main())
