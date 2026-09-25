---
title: nginx 配置要点笔记
date: 2025-03-18
tags: [nginx, 反向代理, 运维]
categories: [笔记]
description: nginx 静态服务、反向代理与 location 匹配的配置要点，附高频场景速查表、匹配规则详解与五条踩坑记录。
---

# nginx 配置要点笔记

## 一句话定位

nginx 是高性能的 HTTP 与反向代理服务器，一台机器上最常见的三种身份：静态文件服务器、反向代理、负载均衡入口。配置写在 `nginx.conf` 与 `conf.d/`、`sites-enabled/` 的片段里，语法是"指令 + 参数 + 分号结尾"，块结构用大括号。官方文档见 [nginx.org](http://nginx.org/en/docs/)，维基百科对 [Nginx](https://zh.wikipedia.org/wiki/Nginx) 的定位是"轻量级 Web 服务器、反向代理及电子邮件代理服务器"，入门阶段抓住"location 匹配"与"proxy_pass"两条主线就够。它还是证书终止、WebSocket 代理、限流熔断的落点，一个配置片段就能改掉线上流量的走向；也正因为它离流量最近，每次改配置都要按"校验、灰度、观察"的节奏来，忌一把梭。

![nginx 请求处理流程示意](/images/demo-5.svg)

## 安装与环境

Debian/Ubuntu 用 `apt install nginx`，装完自动注册系统服务。要理解目录约定：主配置 `/etc/nginx/nginx.conf`，站点片段 `/etc/nginx/sites-enabled/`（软链接到 `sites-available`），公共片段 `/etc/nginx/conf.d/`。每次改完配置必须 `nginx -t` 校验语法，通过再 `systemctl reload nginx`，reload 是平滑重载，不断现有连接。源码编译能开启更多模块（比如 QUIC 支持），但维护成本高，生产建议用发行版或官方仓库的稳定包；要用第三方模块（如 lua）时再考虑编译，并把这层决策写进运维文档。

安装是 `sudo apt install nginx`，再用 `systemctl enable --now nginx` 设为开机自启；此后改配置的标准两步是 `sudo nginx -t` 校验语法、`sudo systemctl reload nginx` 平滑重载，排错时用 `sudo tail -f /var/log/nginx/error.log` 跟着错误日志走。

## 核心用法

### 静态站点与 try_files

`root` 指定文件根目录，`location /` 兜底。单页应用（SPA）的 history 路由要靠 `try_files $uri $uri/ /index.html`：先找真实文件，再找目录，最后回落到入口，否则刷新子路由会 404。`alias` 与 `root` 的区别是路径拼接方式，`alias` 用在"把一段 URL 映射到无关目录"的场景，两者混用是最常见的配置错误来源。`index` 指令决定目录默认文件；`autoindex` 默认关闭，别在生产意外打开目录列表。多站点并存的机器用 `server_name` 区分，默认 server（第一个）会接住所有未匹配的 Host，常被用来钓恶意流量，可以给它配一个直接返回 444 的空站点。

```nginx
server {
    listen 80;
    server_name example.com;
    root /var/www/site;
    index index.html;

    location / {
        try_files $uri $uri/ /index.html;
    }

    # 把 /media/ 映射到另一个目录，用 alias
    location /media/ {
        alias /data/assets/;
    }
}
```

### 反向代理

`proxy_pass` 把请求转给后端应用，三个配套指令几乎必带：`proxy_set_header Host $host` 保留原始域名，`X-Real-IP` 与 `X-Forwarded-For` 传递真实客户端 IP，否则应用里看到的全是代理地址。`proxy_pass` 末尾带不带斜杠语义完全不同：带斜杠会把 location 前缀从 URL 里替换掉，不带则原样透传，这是最容易踩的坑。长连接场景（WebSocket、SSE）要把 `proxy_read_timeout` 调大，`proxy_buffering off` 关掉缓冲才能实时推送；`proxy_next_upstream` 决定后端故障时的切换行为，API 场景建议只对连接错误切换，避免重复提交非幂等请求。

```nginx
location /api/ {
    proxy_pass http://127.0.0.1:8080/;   # 注意尾斜杠
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

![反向代理与静态服务分工示意](/images/demo-12.svg)

### 压缩、缓存与静态缓存头

`gzip on` 压缩文本响应，`gzip_types` 别忘了把 `application/json`、`image/svg+xml` 加进去，默认只压 HTML。静态资源配超长 `expires`，文件名带 hash 的前端产物配 `Cache-Control: immutable`；HTML 入口则要 `no-cache`，保证发版后立刻拿到新页面。这一组是前端部署的标配。gzip 压缩等级 1 到 9，CPU 与体积的权衡点通常在 4 到 6；`gzip_min_length` 避免压缩过小的响应，几百字节的文件压了反而更大。另外 `gzip_vary on` 让缓存按编码区分，CDN 场景少了它会把压缩版返回给不支持的客户端；`gzip_proxied any` 决定是否压缩经过代理的响应，默认值经常需要显式打开。

```nginx
server {
    gzip on;
    gzip_types text/css application/json application/javascript image/svg+xml;

    location ~* \.(?:css|js|svg|woff2)$ {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }

    location = /index.html {
        add_header Cache-Control "no-cache";
    }
}
```

### 限流与访问控制

对外暴露的接口要做两道闸：连接级限流防单点打满，请求级限流防接口被刷。`limit_conn_zone` 按客户端 IP 限并发连接，`limit_req_zone` 限请求速率，超限默认返回 503，可以用 `limit_req_status` 改成 429 更规范。配合 `deny` / `allow` 做 IP 黑白名单，内网管理路径只放行办公网段，这一套是安全加固的最低配。

```nginx
# 在 http 块定义区域，在 server/location 里引用
limit_req_zone $binary_remote_addr zone=api:10m rate=10r/s;
limit_conn_zone $binary_remote_addr zone=perip:10m;

server {
    location /api/ {
        limit_req zone=api burst=20 nodelay;
        limit_conn perip 10;
        limit_req_status 429;
    }

    location /admin/ {
        allow 10.0.0.0/8;
        deny all;
    }
}
```

location 的匹配规则是配置正确性的地基，建议展开下面这段逐条对照自己的配置检查一遍。

<details>
<summary>展开：location 匹配规则详解</summary>

nginx 的 location 有四类写法，匹配优先级从高到低：

1. **精确匹配** `location = /path`：完全相等才命中，优先级最高。
2. **正则匹配** `location ~ \.php$`（区分大小写）或 `~*`（不敏感）：按配置里出现顺序，第一条命中的生效。
3. **`^~` 前缀** `location ^~ /static/`：前缀匹配命中后不再检查正则，用于"这段路径我包了"。
4. **最长前缀匹配** `location /api/`：普通字符串前缀，记下最长的那条备用。

实际流程是：先找所有前缀匹配，记住最长的那条；如果最长前缀用了 `^~`，直接用它；否则按顺序试正则，正则命中用正则，全不中才用最长前缀。`= /healthz` 这类精确匹配常用于"只给健康检查用"的内部路径。

</details>

### 配置的组织与版本管理

配置怎么放：公共片段进 `conf.d/`，站点配置在 `sites-available/` 编辑、软链接到 `sites-enabled/` 启用，一台机器多站点的标准布局；每个 location 上方写一行注释说明用途，三个月后回来看配置，注释比记忆可靠。

重复出现三次就抽象：upstream 地址、日志格式、通用响应头，提成片段文件用 `include` 引入，改一处生效全局；`set` 变量与 `map` 能把"根据域名换行为"这类逻辑收拢到一处，配置行数骤减，出错面也跟着小。

变更必须走流程：改配置提 MR、CI 里跑 `nginx -t`、先在一台机器 reload 观察、再全量；回滚就是 revert 加 reload，比登服务器 vi 编辑可靠得多。配置即代码在 nginx 上的收益最直接，因为它的每次失误都是全站 502。

日志是排错的另一半：`access_log` 格式里加上响应时间与 upstream 状态，谁在超时、哪个后端在报错，日志里一目了然；接 ELK 或 Loki 之后，"最近变慢了"这种模糊反馈才能变成可查的数据。

还有一类容易忘的是超时：`proxy_connect_timeout`、`proxy_read_timeout`、`send_timeout` 三个值按业务设，默认值对慢接口经常不够用；前端到 nginx、nginx 到后端两段超时要成比例，内段大于外段，让用户先看到网关超时，比两边同时挂更可控。

## 高频场景速查

下面十二个场景覆盖静态站与 API 代理的日常配置，每条都给了关键指令与注意事项，配置前先来表里对号入座。

| 场景 | 关键指令 | 说明 |
| --- | --- | --- |
| 静态站点 | `root` + `try_files` | SPA 必须回落 index.html |
| 反向代理 API | `proxy_pass` 三件套 | Host、Real-IP、Forwarded-For |
| WebSocket 代理 | `Upgrade` + `Connection` 头 | 不加这两头 101 升级失败 |
| HTTPS 终止 | `listen 443 ssl` + 证书 | 证书链要完整，含中间证书 |
| HTTP 跳 HTTPS | `return 301 https://$host$1` | 放在 80 的 server 块里 |
| Gzip 压缩 | `gzip_types` | 别漏 JSON 与 SVG |
| 静态缓存 | `expires` + `add_header` | 带 hash 的资源用 immutable |
| 限流 | `limit_req_zone` + `limit_req` | 防刷与防爬的第一道闸 |
| 大文件上传 | `client_max_body_size` | 默认 1M，超了直接 413 |
| 隐藏版本号 | `server_tokens off` | 安全加固顺手做 |
| 目录列表 | `autoindex` | 默认关，别忘 |
| 路径映射 | `alias` vs `root` | alias 替换前缀，root 拼全路径 |

## 踩坑记录

> **坑一：代理之后应用拿到的路径少了 /api。** `proxy_pass` 带尾斜杠会替换掉 location 前缀：`location /api/` 配 `proxy_pass http://x/`，后端收到的是 `/` 开头。不想被替换就去掉尾斜杠，两边语义要对齐。

> **坑二：刷新 SPA 子路由 404。** 缺 `try_files ... /index.html`。静态文件能找到，前端路由的虚拟路径找不到，必须回落到入口交给前端路由器。

> **坑三：改了配置不生效。** 两个常见原因：没跑 `nginx -t` 导致 reload 失败、旧配置还在跑；或者改的文件没被 include（放在 `sites-available` 却没建软链接到 `sites-enabled`）。

> **坑四：WebSocket upgrade 报 400。** 缺 `proxy_set_header Upgrade $http_upgrade;` 与 `proxy_set_header Connection "upgrade";`，还要注意 HTTP/1.1 与合理超时，长连接被默认 60 秒切断。

> **坑五：上传大文件 413。** 默认 `client_max_body_size 1m`，按需调大；反代后面再套一层（如应用框架的文件大小限制）时，两层都要改。

> **坑六：升级 HTTP/2 后页面变慢。** 多半是 gzip 与 HTTP/2 的多路复用叠加，或者 TLS 会话复用没开。`listen 443 ssl http2;` 之外，确认 `ssl_session_cache shared:SSL:10m` 与 OCSP stapling 打开，用 `curl -w "%{http_version}"` 验证协商到的协议版本。

---

## 速查小结

- 改配置的标准循环：改 → `nginx -t` → `reload`，一步都不能省。
- location 四类写法优先级：`=` > 正则（按顺序）> `^~` > 最长前缀。
- `proxy_pass` 尾斜杠决定 URL 是否被替换，下笔前想清楚。
- SPA 三件套：`try_files`、`no-cache` 的 HTML、`immutable` 的带 hash 资源。
- 安全默认项：`server_tokens off`、限制 `client_max_body_size`、限流。
- 对外接口两道闸：`limit_conn` 限并发，`limit_req` 限速率。
- 排查顺序：`error.log` → `nginx -t` → 后端是否活着 → 浏览器 Network。

nginx 的配置语法半小时能学会，真正花时间的是把每个指令的副作用记住。配置管理上，把站点片段纳入版本库、改配置走 MR，比直接登服务器 vi 编辑可靠得多；`nginx -t` 进部署脚本，语法错误就出不了门。最后送一个排错口诀：先看日志、再验语法、后查后端，三步走完还不定位，问题多半在中间网络而不在配置本身。
