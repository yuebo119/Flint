---
title: curl 进阶用法笔记
date: 2024-06-08
tags: [curl, HTTP, 命令行]
categories: [笔记]
description: curl 构造请求、调试接口、管理会话与下载文件的进阶参数笔记，附排错任务清单、完整调试会话示例与四个真实踩坑记录。
---

# curl 进阶用法笔记

## 一句话定位

curl 是命令行的 HTTP 客户端，支持二十多种协议，日常九成的使用场景是"发一个请求，看看回来什么"。它和浏览器不同：不带渲染、不执行 JS，但胜在参数透明，每一个头、每一个字节都可控。调试后端接口、写 CI 健康检查、模拟表单上传，curl 都是第一工具。维基百科对 [cURL](https://zh.wikipedia.org/wiki/cURL) 的介绍里强调它"用于使用各种网络协议传输数据"，记住这一点：curl 不只是 HTTP 工具，只是我们最常用它发 HTTP 请求。

![curl 请求响应流程示意](/images/demo-2.svg)

## 安装与环境

Linux 与 macOS 基本都预装；没有的话 `apt install curl` 或 `brew install curl`。Windows 10 1803 之后自带 curl，在 PowerShell 与 Git Bash 里都能直接调用。装完用 `curl --version` 确认支持的协议列表里有 https，以及 TLS 后端是 OpenSSL 还是 Schannel，这会影响后面证书相关的行为。企业内网常遇到根证书不被信任的情况，除了 `-k` 跳过校验，更稳妥的做法是把内部 CA 证书交给 curl：`--cacert corp-ca.crt`，或者写进 `CURL_CA_BUNDLE` 环境变量，一劳永逸。另外 `~/.curlrc` 配置文件可以写默认参数，比如默认超时 `--max-time 30`、默认跟随重定向 `-L`，省得每次敲；但 CI 镜像里别依赖它，显式参数才可复现。

安装是一条命令的事：Debian/Ubuntu 用 `sudo apt install curl`，macOS 用 `brew install curl`，Windows 用 `winget install cURL.cURL`，多数系统其实已经预装；装完 `curl --version` 确认协议列表里有 https 即可。

## 核心用法

### 构造请求：方法、头与体

默认 GET；`-X` 换方法；`-H` 加头，可以重复多次；`-d` 发表单或 JSON 体，`--data-urlencode` 处理中文与特殊字符；`-F` 发 multipart 表单，文件上传用它。注意 `-d` 会自动把方法改成 POST，除非显式指定 `-X GET`，这一点在只想发个带 body 的 GET 时容易忘。请求体的内容类型要与 `-H "Content-Type: ..."` 对齐，服务端解析失败时九成是这里对不上；多个 `-H` 会全部发送，同名头以最后一次为准。

```bash
# 只取响应头
curl -I https://example.com

# POST JSON，并带上自定义头
curl -X POST https://example.com/api/login \
  -H "Content-Type: application/json" \
  -d '{"user":"admin","password":"***"}'

# 上传文件（multipart 表单）
curl -F "file=@report.pdf" -F "desc=月度报告" https://example.com/upload
```

### 会话与重定向

`-b` 发 Cookie，`-c` 把响应里的 Set-Cookie 存进文件，两步配合就是"登录后保持会话"。`-L` 跟随 3xx 重定向，配合 `-J -O` 可以让下载文件用服务器给的名字。访问自签证书的内网地址用 `-k` 跳过校验，仅限内网调试，生产环境不要图省事。会话过期是另一类高频问题：`-b` 带的 Cookie 失效后服务端返回 302 到登录页，脚本里判断 `%{http_code}` 是 302 就该重新登录，别把登录页当成正常响应处理。

```bash
# 登录并把 Cookie 存下来，再带 Cookie 访问受保护页面
curl -c cookies.txt -d "user=admin&pass=***" https://example.com/login
curl -b cookies.txt https://example.com/dashboard

# 跟随重定向，保存为服务器建议的文件名
curl -L -J -O https://example.com/files/report.pdf
```

### 下载与调试

`-o` 指定输出文件名，`-O` 用远端文件名；`-C -` 断点续传；`--limit-rate` 限速；`-v` 打印完整的请求响应过程，包括 TLS 握手细节；`-w` 在结束后输出自定义格式，是写监控脚本的利器。想安静又不错过错误，用 `-sS`：静默但保留错误信息，这是脚本里的默认姿势。下载大文件时 `--max-filesize` 是保险丝，超过就中止，防止把磁盘写满；镜像站常用 `-A` 伪装 User-Agent，部分站点会拦截 curl 默认的 UA。

```bash
# 断点续传大文件
curl -C - -o ubuntu.iso https://example.com/ubuntu.iso

# 输出状态码与总耗时，适合健康检查
curl -sS -o /dev/null -w "code=%{http_code} time=%{time_total}s\n" https://example.com

# 完整调试：看请求头、响应头与 TLS 握手
curl -v https://example.com/api/health
```

### 把 curl 写进脚本

单次调试和脚本调用是两种用法。脚本里三件事必须做：超时（`--max-time` 防挂死）、失败检测（`--fail` 让 4xx/5xx 返回非零退出码）、重试（`--retry` 配 `--retry-connrefused`）。再加上 `-w` 输出结构化结果，一条 curl 就是一个小型健康检查探针，比引一个 HTTP 客户端库轻得多。`--fail` 与退出码的配合还有个细节：连接失败（DNS、拒连）返回 6，超时返回 28，HTTP 错误返回 22。脚本里按退出码分支处理，能把"网络问题"和"服务问题"分开告警，比一个笼统的失败信息有用得多。

```bash
# 脚本里的标准姿势：超时 + 失败退出 + 重试
code=$(curl -sS --fail --max-time 10 --retry 3 \
  -o /tmp/resp.json \
  -w "%{http_code}" \
  https://example.com/api/health) || {
  echo "health check failed: $code" >&2
  exit 1
}
```

### 常见误区澄清

误区一：把 curl 当浏览器用。curl 不执行 JS、不渲染页面，请求一个单页应用只能看到空壳 HTML；判断"接口有没有问题"用它，"页面显示对不对"必须开浏览器。两者结论经常不一致，不是谁错了，是看的根本不是一回事。

误区二：`-X` 加到哪哪对。多数情况默认行为已经正确：`-d` 自动 POST、`-I` 自动 HEAD。乱加 `-X GET` 配 `-d` 会发出带 body 的 GET，服务端多半不认；方法不对时先想清楚语义，再决定加不加 `-X`。

误区三：重定向无害。`-L` 跟随 3xx 时，POST 在 301/302 下会变成 GET（只有 307/308 保持方法），上传请求跟随重定向等于把文件发到另一个地址；自动跟随前想清楚终点是谁。

误区四：默认超时存在。curl 默认无限等待，脚本里忘加 `--max-time`，一个挂起的连接能拖垮整条流水线；把超时、重试、失败退出当成脚本三件套，比事后救火便宜。

## 高频场景速查

| 场景 | 关键参数 | 说明 |
| --- | --- | --- |
| 看响应头 | `-I` | 等价于发 HEAD 请求 |
| POST JSON | `-X POST -H -d` | `-d` 内容记得用单引号包住 |
| 文件上传 | `-F "file=@路径"` | multipart 表单专用 |
| 保持会话 | `-c` 存、`-b` 带 | 两步配合完成登录态 |
| 跟随重定向 | `-L` | 不加重定向到哪停在哪 |
| 下载文件 | `-O` / `-o` | 长文件名时用 `-J -O` 更稳 |
| 断点续传 | `-C -` | 配合 `-o` 指定本地文件 |
| 跳过证书校验 | `-k` | 仅限内网自签证书场景 |
| 限速下载 | `--limit-rate 1M` | 别把测试机带宽占满 |
| 失败重试 | `--retry 3 --retry-delay 2` | 网络抖动时的保险 |
| 压测探测 | `-w` 自定义格式 | 输出状态码与各阶段耗时 |
| 走代理 | `-x http://127.0.0.1:8080` | 抓包调试时最常用 |

### 常见响应头速查

调试接口时这几类响应头信息量最大，看到异常值往往比状态码更早定位问题：

| 响应头 | 看什么 | 异常意味着 |
| --- | --- | --- |
| Content-Type | 是否与预期一致 | 变成 text/html，多半是代理返回了错误页 |
| Cache-Control | 缓存策略 | 线上改了没生效，先看是不是被缓存 |
| Set-Cookie | 会话与过期 | 登录后没有它，说明认证链断了 |
| Location | 重定向目标 | 302 循环常因这里指回了登录页 |
| Retry-After | 限流等待秒数 | 出现它说明触发了服务端限流 |
| Content-Length | 与实到字节对比 | 不一致就是被截断，查代理与防火墙 |

## 接口排错任务清单

拿到一个"接口不通"的反馈，按顺序过一遍，多数问题在前四步就能定位。这个清单按"从现象到根因"排序，每一步的命令都可以直接复制运行，输出对比比口头描述高效得多：

- [ ] 用 `-v` 看请求是否真的发出去了，DNS 是否解析成功
- [ ] 看响应状态码：404 是路径错，401 是认证问题，502 说明代理层挂了
- [ ] 对比请求头：`User-Agent`、`Content-Type`、`Authorization` 是否和服务端要求一致
- [ ] 用 `-d` 与 `-F` 分别试，确认服务端要的是 JSON 还是表单
- [ ] 换 `-k` 试一次，排除自签证书导致的握手失败
- [ ] 用 `--resolve` 或 `-x` 指定代理，绕开本地 hosts 与代理干扰
- [ ] 仍然失败，把 `-v` 的完整输出（去掉敏感头）提给服务端同学

## 踩坑记录

> **坑一：`-d` 里的中文变成乱码。** `-d` 原样发送，不编码。带中文或 `&`、`=` 的内容要用 `--data-urlencode "key=值"`，curl 会负责百分号编码。

> **坑二：`-L` 之后 `-o` 的文件名不对。** 重定向后的最终地址才有真实文件名，加 `-J` 让 curl 从 `Content-Disposition` 里取名字，`-O` 才名副其实。

> **坑三：Windows 下 JSON 里的双引号全丢。** CMD 与 Git Bash 对引号的处理不同，JSON 体一律用单引号包住，或者把内容写进文件用 `-d @body.json`，最稳。

> **坑四：`-w` 输出挤在一行。** `-w` 的格式串不会自动换行，结尾显式写 `\n`。写监控脚本时建议同时输出 `%{http_code}` 和 `%{time_total}`，只判断状态码会漏掉慢请求。

> **坑五：`-I` 看到的头和浏览器不一样。** HEAD 请求由服务端自己决定实现，有些服务器对 HEAD 直接返回 GET 的头，有些则返回空。判断线上真实响应，用 `-sD - -o /dev/null URL` 发 GET 只打印响应头，最接近浏览器行为。

<details>
<summary>展开：一个完整的登录态调试会话</summary>

1. 带调试登录，保存 Cookie 与响应体：`curl -v -c jar.txt -o login.json -X POST https://example.com/api/login -H "Content-Type: application/json" -d '{"user":"admin","password":"***"}'`
2. 带 Cookie 访问需要登录的接口，只看响应体：`curl -sS -b jar.txt https://example.com/api/orders?page=1 | head -c 500`
3. 检查会话是否过期，401 就该重新登录换新 jar：`curl -sS -b jar.txt -o /dev/null -w "%{http_code}\n" https://example.com/api/me`

三条命令按顺序执行，第二步返回 302 到登录页，说明 jar 过期，回到第一步重来；这套流程写进脚本，就是带自恢复的接口探针。

</details>

---

## 速查小结

- 默认 GET，`-d` 会隐式改 POST，别被隐式的方法变化坑到。
- `-v` 是调试第一步，`-sS` 是脚本里的默认姿势。
- 会话 = `-c` 存 Cookie + `-b` 带 Cookie，两步缺一不可。
- `-L -J -O` 是下载三件套，配合 `-C -` 支持断点续传。
- 写监控只判断状态码不够，加上 `%{time_total}` 才能发现慢请求。
- 退出码分支：6 连接失败、28 超时、22 HTTP 错误，告警要分开。

把 `-v` 和 `-w` 用熟，curl 就从一个下载工具变成了接口调试台。再进一步，把常用的几条命令写成脚本或 Makefile 目标，"查一下接口"这个动作就从翻聊天记录找命令，变成敲一个目标名。
