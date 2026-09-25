#!/usr/bin/env python3
# Flint 静态站点生成器
# 单进程多端口静态服务——demo-sites.sh 的服务端（替代"每主题一个
# python -m http.server"）。
#
# 动机：21 主题 + 画廊 = 22 个独立解释器进程，实测 490MB 工作集（每个
# python http.server ~19MB）。空闲 CPU 为零、内存全花在解释器自身上。
# 合成一个 asyncio 进程后总占用 ~15MB，且并发取文件比阻塞式
# http.server 更好（浏览器并行请求不再排队）。
#
# 用法（仓库根执行）:
#   python demo-sites/demo-serve.py 8400=demo-sites/gallery/public 8401=demo-sites/ananke/public ...
#   python demo-sites/demo-serve.py --list demo-serve.map   # 或用 --list 指定映射文件
#
# 映射行格式: <端口>=<文档根目录>

import asyncio
import os
import sys
import urllib.parse

MIME = {
    ".html": "text/html; charset=utf-8",
    ".htm": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".mjs": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".xml": "application/xml; charset=utf-8",
    ".txt": "text/plain; charset=utf-8",
    ".svg": "image/svg+xml",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif": "image/gif",
    ".webp": "image/webp",
    ".avif": "image/avif",
    ".ico": "image/x-icon",
    ".woff": "font/woff",
    ".woff2": "font/woff2",
    ".ttf": "font/ttf",
    ".otf": "font/otf",
    ".pdf": "application/pdf",
    ".mp4": "video/mp4",
    ".webm": "video/webm",
    ".mp3": "audio/mpeg",
    ".wasm": "application/wasm",
    ".map": "application/json",
}


def parse_mappings(argv):
    """解析 端口=目录 参数（或 --list 文件）；相对目录按多个基准依次探测"""
    script_dir = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.dirname(script_dir)          # Flint/ 仓库根
    # 演示站点目录在工作区根（Flint/ 的上级），映射文件常按该基准写相对路径；
    # 依次探测 CWD → 仓库根 → 仓库根上级，取第一个存在的
    bases = [os.getcwd(), repo_root, os.path.dirname(repo_root)]
    entries = []
    i = 0
    while i < len(argv):
        arg = argv[i]
        if arg == "--list" and i + 1 < len(argv):
            with open(argv[i + 1], encoding="utf-8") as fh:
                entries.extend(line.strip() for line in fh if line.strip() and not line.startswith("#"))
            i += 2
            continue
        entries.append(arg)
        i += 1
    mappings = {}
    for entry in entries:
        if "=" not in entry:
            raise SystemExit(f"映射格式错误（应为 端口=目录）: {entry}")
        port_s, root = entry.split("=", 1)
        port = int(port_s.strip())
        root = root.strip()
        if not os.path.isabs(root):
            # 依次探测多个基准，取第一个存在的；都不存在则报错
            root = next(
                (os.path.normpath(os.path.join(b, root)) for b in bases
                 if os.path.isdir(os.path.join(b, root))),
                os.path.abspath(root),
            )
        if not os.path.isabs(root):
            # 相对路径以仓库根（scripts/ 的上级）为基准，也可相对 CWD 兜底
            candidate = os.path.normpath(os.path.join(repo_root, root))
            root = candidate if os.path.isdir(candidate) else os.path.abspath(root)
        if not os.path.isdir(root):
            raise SystemExit(f"文档根不存在: {root}（端口 {port}）")
        mappings[port] = root
    if not mappings:
        raise SystemExit("未提供任何 端口=目录 映射")
    return mappings


def safe_join(root, url_path):
    """URL 路径 → 磁盘路径；越界（..）与不存在返回 None"""
    path = urllib.parse.unquote(url_path.split("?", 1)[0].split("#", 1)[0])
    if path.endswith("/"):
        path += "index.html"
    full = os.path.normpath(os.path.join(root, path.lstrip("/")))
    root_abs = os.path.abspath(root)
    if not (full == root_abs or full.startswith(root_abs + os.sep)):
        return None
    return full


def send_response(writer, status, reason, body, mime, head_only=False):
    writer.write(
        f"HTTP/1.1 {status} {reason}\r\n"
        f"Content-Type: {mime}\r\n"
        f"Content-Length: {len(body)}\r\n"
        f"Cache-Control: no-store\r\n"
        f"Connection: close\r\n\r\n".encode("latin-1")
    )
    if not head_only:
        writer.write(body)


async def handle(reader, writer, root):
    try:
        request_line = await asyncio.wait_for(reader.readline(), timeout=10)
        if not request_line:
            writer.close()
            return
        parts = request_line.decode("latin-1").split()
        if len(parts) < 2:
            send_response(writer, 400, "Bad Request", b"bad request", "text/plain; charset=utf-8")
            return
        method, target = parts[0], parts[1]
        # 丢弃剩余请求头（保持连接简单）
        while True:
            line = await reader.readline()
            if not line or line in (b"\r\n", b"\n"):
                break

        full = safe_join(root, target)
        if full is None or not os.path.isfile(full):
            # 404 页存在则返回其内容（主题自带 404.html）
            not_found = os.path.join(root, "404.html")
            if os.path.isfile(not_found):
                try:
                    with open(not_found, "rb") as fh:
                        body = fh.read()
                    send_response(writer, 404, "Not Found", body, "text/html; charset=utf-8", method == "HEAD")
                    return
                except OSError:
                    pass
            send_response(writer, 404, "Not Found", b"404 not found", "text/plain; charset=utf-8", method == "HEAD")
            return

        ext = os.path.splitext(full)[1].lower()
        mime = MIME.get(ext, "application/octet-stream")
        try:
            with open(full, "rb") as fh:
                body = fh.read()
        except OSError:
            send_response(writer, 404, "Not Found", b"404 not found", "text/plain; charset=utf-8", method == "HEAD")
            return
        send_response(writer, 200, "OK", body, mime, method == "HEAD")
    except (asyncio.TimeoutError, ConnectionError, OSError):
        pass
    finally:
        try:
            writer.close()
        except OSError:
            pass


async def main():
    mappings = parse_mappings(sys.argv[1:])
    servers = []
    for port in sorted(mappings):
        root = mappings[port]
        # 中文路径需回环地址字面量 + 足够 backlog（浏览器一次开数十条并行连接）
        server = await asyncio.start_server(
            lambda r, w, root=root: handle(r, w, root),
            host="127.0.0.1",
            port=port,
            backlog=256,
            reuse_address=True,
        )
        servers.append(server)
        print(f"  http://127.0.0.1:{port}/  ->  {root}", flush=True)
    print(f"共 {len(servers)} 个端口，单进程服务中（Ctrl+C 停止）", flush=True)
    async with servers[0]:
        await asyncio.gather(*(s.serve_forever() for s in servers))


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
