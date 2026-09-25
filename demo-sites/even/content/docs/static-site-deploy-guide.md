---
title: "静态站点部署指南"
date: 2025-04-18
tags: ["静态站点", "部署", "Nginx"]
categories: ["文档"]
description: "静态站点部署完整指南：产物检查、托管方案对比、Nginx 配置、HTTPS、缓存策略与回滚方法，一条链路讲清上线全过程。"
---

静态站点生成器跑完最后一条命令，产出一个目录，接下来就是把它送上互联网。这一步看似只是"上传文件"，真正决定上线质量的却是另外几件事：构建产物是否完整、URL 是否与部署路径匹配、HTTPS 是否强制、缓存头是否让改动能立刻生效、出问题时能否在分钟级回滚。这篇指南按"检查产物、选择方案、配置服务、上线回滚"的顺序走一遍完整流程，三种主流方案都给出可用的配置。

![静态站点从构建到上线的流程示意](/images/demo-6.svg)

## 部署前检查

无论选哪种托管方式，上传之前先过这四项：

- **构建命令在干净环境跑通过**：本地 `node_modules` 或缓存可能掩盖依赖问题，用 CI 或全新环境构建一次最稳。
- **`baseURL` 与实际访问地址一致**：地址含子路径时结尾斜杠不能省，否则站内链接和资源路径全部指错。
- **产物目录内容完整**：确认 HTML、CSS、JS、图片、字体、`404.html`、`robots.txt`、`sitemap.xml` 都在。
- **本地起一个静态服务验证**：`npx serve public` 之类的工具起服务后点几个页面，比直接上传更容易发现问题。
- **检查大文件体积**：单张图片超过五百 KB 就考虑压缩或转 WebP，首屏资源总体积控制在两百 KB 以内，静态站点的速度优势才留得住。

### 构建产物里有什么

上传前先确认目录内容完整，一个典型的静态站点产物包含：

- `index.html` 与各级栏目页：站点入口与列表页，缺一个就说明生成阶段有问题。
- `posts/`、`tags/` 等目录：文章页与归档页，URL 结构与站内链接对应。
- `css/`、`js/`、`images/`：样式、脚本与图片，文件名带内容指纹的适合长缓存。
- `404.html`、`robots.txt`、`sitemap.xml`：错误页、爬虫协议与站点地图，托管平台需要这三个文件正常工作。

发现缺文件时不要手动补，回到构建环节排查：主题配置、草稿过滤、`baseURL` 是三个最常见的根因。

## 托管方案对比

| 方案 | 适合场景 | 成本 | 自动化程度 | 主要限制 |
|------|----------|------|------------|----------|
| GitHub Pages | 开源项目、个人站点 | 免费 | 高（Actions 推送即部署） | 无服务端逻辑，国内访问不稳定 |
| Netlify / Vercel | 需要预览环境的产品站 | 免费额度充足 | 很高 | 免费版有带宽与构建时长限制 |
| 对象存储 + CDN | 已有云账号的团队 | 按量计费，通常很低 | 中 | 需要自己配缓存与刷新 |
| 自有服务器 + Nginx | 需要完全掌控的团队 | 服务器费用 | 低，需脚本化 | 安全、备份、升级都要自己管 |

选择建议很简单：个人与开源项目用 GitHub Pages；需要每个 PR 一个预览地址的用 Netlify 或 Vercel；已经在某家云上、有运维能力的用对象存储加 CDN；有合规要求或需要和其他服务同机部署的，用自有服务器。

---

## 方案一：GitHub Pages 自动部署

把构建工作交给 GitHub Actions，推送即上线。在仓库新建 `.github/workflows/deploy.yml`：

```yaml
name: deploy
on:
  push:
    branches: [main]
permissions:
  contents: read
  pages: write
  id-token: write
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: 构建站点
        run: |
          hugo --minify
      - name: 上传产物
        uses: actions/upload-pages-artifact@v3
        with:
          path: ./public
  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

同时在仓库设置里把 Pages 的构建来源选为 GitHub Actions。之后每次推送到 `main` 分支都会自动构建并发布，Actions 日志里能看到每一步耗时，构建失败时站点保持上一个版本不变，这本身就是一层保护。

自定义域名的配置在仓库根目录放一个 `CNAME` 文件写入域名即可，Pages 会自动为其签发证书。免费方案的限制是单仓库一个站点、仓库体积有上限、国内访问速度不稳定；把图片等大文件放对象存储或 CDN，站点只留 HTML 和代码，是常见的规避手段。

## 方案二：Nginx 自有服务器

自有服务器的核心是 Nginx 配置。上传产物到站点根目录后，写一份这样的配置：

```nginx
server {
    listen 80;
    server_name example.com;
    root /var/www/site;
    index index.html;

    # 强制 HTTPS，证书配置好之后启用
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name example.com;
    root /var/www/site;
    index index.html;

    ssl_certificate     /etc/letsencrypt/live/example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/example.com/privkey.pem;

    add_header Strict-Transport-Security "max-age=31536000" always;

    # HTML 不缓存，保证改动能立刻看到
    location ~* \.html$ {
        add_header Cache-Control "no-cache, must-revalidate";
    }

    # 带指纹的静态资源长缓存
    location ~* \.(css|js|woff2)$ {
        add_header Cache-Control "public, max-age=31536000, immutable";
    }

    # 图片按天缓存
    location ~* \.(png|jpg|svg|webp)$ {
        add_header Cache-Control "public, max-age=86400";
    }

    location / {
        try_files $uri $uri/ =404;
    }
}
```

用 `nginx -t` 验证语法后 `systemctl reload nginx` 热加载，不断开现有连接。Let's Encrypt 证书用 `certbot --nginx -d example.com` 一条命令即可申请并自动续期，续期定时任务安装时默认配置好。

## 方案三：对象存储 + CDN

云厂商的对象存储都能直接托管静态站点，把 `public/` 同步上去，再开 CDN 加速：

```bash
# 以阿里云 OSS 为例，同步并设置内容类型
ossutil cp -r public/ oss://my-bucket/ --update

# 腾讯云 COS 的等价命令
coscli sync -r public/ cos://my-bucket/

# 刷新 CDN 缓存，让 HTML 变更立即生效
cdn-cli refresh --urls https://example.com/index.html
```

要点：静态资源设置长缓存，HTML 设置短缓存或不缓存；发布后主动刷新一次 CDN 上受影响的 HTML；给 Bucket 设置正确的默认首页与 404 页面，否则访问不存在的路径会返回存储的错误 XML。

## HTTPS 与域名

三条记录配好，站点就完整暴露在互联网上了：

| 记录类型 | 主机记录 | 值 | 作用 |
|----------|----------|-----|------|
| A | `@` | 服务器 IP | 根域名指向服务器 |
| CNAME | `www` | `example.com` | 主域名的别名 |
| CAA | `@` | `letsencrypt.org` | 限定只有指定机构可签发证书 |

证书只覆盖解析到的主机名，`www` 和根域名都要配齐，漏一个就会出现"一半页面显示不安全"的怪现象。DNS 修改最长需要二十四小时全球生效，可以先用 `dig example.com` 确认解析是否已经到位。

## 缓存策略

静态站点的缓存分两级设计，目标正好相反：入口文件要短缓存保证更新可见，静态资源要长缓存保证加载速度。

| 资源类型 | 建议缓存头 | 理由 |
|----------|------------|------|
| HTML 页面 | `no-cache` 或 `max-age=60` | 内容更新要分钟级可见 |
| 带指纹的 CSS/JS | `max-age=31536000, immutable` | 内容变则文件名变，可永久缓存 |
| 图片与字体 | `max-age=86400` 起 | 更新频率低，可按天缓存 |
| `sitemap.xml` | `max-age=3600` | 小时级更新即可 |

两个容易忘的细节：一是发布后主动刷新 CDN 上受影响的 HTML，否则边缘节点还揣着旧页面；二是 `immutable` 只对带内容指纹的文件使用，普通命名的文件加了这条指令，用户将再也拿不到更新。

## 发布与回滚

静态站点最大的优势是回滚便宜。关键习惯只有一个：**每次发布保留一份带版本号的产物**。

![版本归档与回滚切换示意](/images/demo-12.svg)

> 缓存配置的第一原则：HTML 要"新"，静态资源要"稳"。前者决定用户多快看到你的更新，后者决定页面加载有多快，两者混用必然二选一地牺牲一头。

### 发布

用带时间戳的目录归档每次构建产物，软链接指向当前版本：`cp -r public /var/www/releases/$(date +%Y%m%d-%H%M)`，然后 `ln -sfn /var/www/releases/<版本号> /var/www/current`，Nginx 的 root 始终指向 `current`。发布即切换软链接，过程原子，不会出现半个目录覆盖一半的中间态。

### 回滚

回滚就是切换回旧版本：`ln -sfn /var/www/releases/20250418-0930 /var/www/current`，秒级生效。配合上面的缓存策略，HTML 立即生效，带指纹的 CSS 和 JS 因为文件名不变也不会串版本。把这个流程写成脚本，任何人值班时都能操作，不依赖某个人记得步骤。

## 上线检查清单

- [ ] 构建在干净环境通过，产物目录完整
- [ ] `baseURL` 与实际域名一致，子路径斜杠正确
- [ ] DNS 解析生效，HTTP 强制跳转 HTTPS
- [ ] 证书覆盖全部主机名，自动续期已配置
- [ ] HTML 与静态资源缓存策略已区分
- [ ] 404 页面与 `robots.txt`、`sitemap.xml` 可访问
- [ ] 发布脚本可重复执行，回滚步骤演练过一次
- [ ] 移动端与桌面端各抽查三个页面

## 小结

静态站点部署的难点从来不在"上传"，而在缓存、证书、回滚这三件容易被跳过的小事。方案选型看团队现状，个人项目 GitHub Pages 起步，商业站点优先托管平台换预览能力，有运维积淀再上自有服务器。上线之后建议再加一层轻量监控：用免费的 uptime 服务每小时探测一次首页和文章页，状态异常时邮件或 IM 通知，比用户先知道故障。无论哪种方案，把发布和回滚脚本化，上线就从一件紧张的事变成一次日常操作。Hugo 官方部署文档见 <https://gohugo.io/hosting-and-deployment/>。
