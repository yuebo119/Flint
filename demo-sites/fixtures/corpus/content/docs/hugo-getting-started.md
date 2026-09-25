---
title: "Hugo 上手指南：从安装到发布第一个站点"
date: 2024-01-15
tags: ["Hugo", "静态站点", "快速入门"]
categories: ["文档"]
description: "从零开始的 Hugo 上手指南：覆盖安装、目录结构、front matter、主题安装与构建部署，一篇文章带你发布第一个静态站点。"
---

Hugo 是用 Go 语言编写的静态站点生成器，也是同类工具中构建速度最快的一批。一个几百页的站点，从源码到可部署的 HTML 往往只需要几十毫秒。对个人博客、项目文档站和团队官网来说，Hugo 意味着没有数据库、没有运行时依赖，交付物就是一堆可以扔到任意 Web 服务器上的静态文件。这篇指南面向完全没有接触过静态站点生成器的读者，完整走一遍"安装、建站、写内容、构建、部署"的路径，中途不跳过任何一步。

静态站点生成器的工作方式可以这样理解：你在本地写好 Markdown，运行一条构建命令，程序把 Markdown 连同模板、配置文件一起编译成 HTML、CSS 和 JS，你把这个产物目录上传到服务器，访客看到的就是纯静态页面。整个过程不涉及数据库查询，也不依赖服务器端语言环境，因此托管成本极低，被流量突袭时的表现也相当稳定。

## Hugo 适合谁，不适合谁

先明确边界，再决定要不要投入时间。

- 适合：个人博客、知识库、开源项目文档、活动落地页、内容更新不频繁的小型官网。
- 不适合：需要用户登录、实时评论、后台在线编辑、按登录用户展示不同内容的站点。这类需求要么接入第三方服务，要么直接选择动态框架。

如果需求落在"适合"一栏，Hugo 很可能是最省心的选择：一个二进制文件、一个 `hugo.toml`、一个 `content/` 目录，就是全部家当。反过来，如果站点一半的页面依赖用户交互数据，硬套静态生成只会把复杂度藏进前端 JavaScript 里，后期维护反而更累。折中方案是"静态主体加动态补丁"：文章和文档用 Hugo 生成，评论用第三方嵌入，搜索用托管服务，各取所长。

## 第一步：安装 Hugo

官方提供各平台的安装方式，推荐用包管理器安装，后续升级最省事：

| 平台 | 安装命令 | 说明 |
|------|----------|------|
| Windows | `scoop install hugo-extended` | 选 extended 版本以支持 SCSS |
| macOS | `brew install hugo` | Homebrew 源默认为完整版 |
| Linux | `snap install hugo` | 也可用发行版仓库中的包 |
| 任意平台 | GitHub Releases 下载 | 解压后把可执行文件加入 PATH |

```bash
# macOS 示例
brew install hugo

# Windows (Scoop) 示例
scoop install hugo-extended

# 创建站点骨架并进入目录
hugo new site my-blog
cd my-blog

# 以子模块方式引入主题，便于跟随上游更新
git init
git submodule add https://github.com/theNewDynamic/gohugo-theme-ananke themes/ananke

# 验证安装，应输出包含版本号的命令行结果
hugo version
```

安装完成后执行 `hugo version`，能看到版本号即说明可用。注意版本至少要 0.112 以上：更早的版本默认配置文件还是 `config.toml`，与本文示例不符。另外要区分普通版与 extended 版，后者内置 Sass/SCSS 编译器，使用依赖 SCSS 的主题时必须装 extended 版，否则构建阶段会报找不到依赖的错误。

## 第二步：认识目录结构

生成的站点骨架包含若干约定目录，先弄清各自职责再动手：

![Hugo 站点目录结构示意](/images/demo-1.svg)

- `archetypes/`：内容模板。之后用 `hugo new` 创建文章时，会自动套用这里定义的 front matter，省去每次手写元数据的重复劳动。
- `content/`：站点的全部内容。所有 Markdown 文件放在这个目录下，目录层级即 URL 层级，`content/posts/a.md` 对应访问路径 `/posts/a/`。
- `static/`：静态资源目录。这里的文件会被原样拷贝进构建产物，图片、字体、`robots.txt`、验证用的 html 文件都放这里。
- `layouts/`：模板目录。使用第三方主题时通常不需要改动，只有深度定制时才写自己的模板。
- `themes/`：主题目录。一个站点同一时刻只启用一个主题，多主题共存时通过配置项切换。
- `hugo.toml`：站点配置文件。站名、域名、语言、菜单、自定义参数都在这里，相当于全站的中央控制台。

> 建议：站点根目录创建后立即执行 `git init`。内容、配置、主题引用全部进版本库，之后换机器、回滚、团队协作都靠它兜底。

## 第三步：配置站点

编辑 `hugo.toml`，写入基础信息并启用主题：

```toml
baseURL = 'https://example.com/'
languageCode = 'zh-cn'
title = '我的博客'
theme = 'ananke'

[params]
  author = '张三'
  description = '记录技术与生活'

[menu]
  [[menu.main]]
    name = '首页'
    url = '/'
    weight = 1
  [[menu.main]]
    name = '文章'
    url = '/posts/'
    weight = 2
```

`baseURL` 是最容易踩坑的配置项：部署在域名根目录时必须带结尾斜杠；部署在子路径（例如 `https://example.com/blog/`）也要如实填写，否则生成的文章链接会指向错误的位置。`params` 下的自定义字段可以在模板里读取，主题的 README 通常会列出所有可用参数。主题可以在 Hugo 官方主题站 <https://gohugo.io/themes/> 挑选，建议优先看最近半年有更新的主题，并实际跑一遍再决定。

## 第四步：写第一篇文章

在终端执行 `hugo new posts/hello-hugo.md`，命令会根据 `archetypes/default.md` 生成一个带 front matter 的文件。front matter 是文章头部的元数据块，用 `---` 包裹，常用字段如下：

| 字段 | 作用 | 示例值 |
|------|------|--------|
| `title` | 文章标题 | `第一篇文章` |
| `date` | 发布日期 | `2024-01-15` |
| `draft` | 是否为草稿 | `false` |
| `tags` | 标签列表 | `["Hugo", "笔记"]` |
| `categories` | 所属分类 | `["博客"]` |

写完后把 `draft` 改成 `false`，文章才会进入正式构建。开发阶段可以保留 `true`，配合 `hugo server -D` 本地预览。archetype 模板本身也可以自定义，把常用标签、作者、封面字段预先写好，新建文章时直接补内容即可，团队协作时这一步能省下大量重复录入。

## 第五步：理解内容组织方式

URL 与目录的对应关系理清楚，后面建栏目才不会乱。

### 目录与 URL 的对应

- `content/posts/a.md` 生成单页，URL 为 `/posts/a/`；`content/posts/my-post/index.md` 与同目录图片构成 Page Bundle，文中用相对路径引用图片，栏目迁移时不会出现图片失效。
- `content/posts/_index.md` 是列表页，URL 为 `/posts/`，可以在这里写栏目介绍、置顶链接，控制列表页的标题与摘要。
- `content/about.md` 生成独立单页，URL 为 `/about/`，适合放关于、简历、联系方式这类不随时间归档的页面。

### 分类与标签

分类与标签是两套并行的体系：分类粗，用来划分栏目；标签细，用来串联主题。两者都在 front matter 里声明，Hugo 会自动生成对应的归档页。建议分类控制在五个以内，标签随意但保持命名一致，否则归档页会碎片化，读者很难通过标签找到相关文章。

> 一个实用的判断方法：如果两个词无法同时出现在一篇文章里，它们就不该是同一层级的术语；标签名尽量用名词，动词和形容词留给标题。

## 第六步：本地预览与构建

```bash
# 启动开发服务器，-D 表示包含草稿，默认监听 localhost:1313
hugo server -D

# 默认端口被占用时换端口，并指定构建环境
hugo server -p 1314 --environment production

# 正式构建，产物输出到 public/
hugo --minify
```

开发服务器默认监听 `http://localhost:1313/`，保存文件后浏览器自动刷新，写文章的反馈回路几乎是实时的。满意之后执行 `hugo`，`public/` 目录就是完整的静态站点，`--minify` 会压缩 HTML 体积，生产环境建议加上。

### 多环境配置

同一个站点常有开发与生产两套参数，用环境配置区分：把生产专属配置放进 `config/production/hugo.toml`，执行 `hugo --environment production` 时自动叠加。开发环境关掉统计脚本、打开调试信息，生产环境反之，一份代码两种行为，不用手动改配置再改回来。

## 第七步：部署上线

最常见的免费方案是 GitHub Pages：在仓库设置中把发布源指向承载 `public/` 内容的分支，推送后自动生效。更省心的做法是接入 Netlify 或 Vercel，绑定仓库后每次推送自动构建，还能获得预览环境。自有服务器则把 `public/` 上传到 Nginx 的站点根目录即可。涉及域名解析、HTTPS、缓存头和回滚策略的完整流程，可以参考本专栏的《静态站点部署指南》一文。

## 常见问题

- **`hugo server` 提示端口被占用**：加 `-p 1314` 换端口，或先结束占用 1313 的进程。
- **页面能打开但没有样式**：检查 `hugo.toml` 里的 `theme` 名称是否与 `themes/` 下的目录名完全一致，以及是否装了 extended 版本。
- **标题和正文中文乱码**：确认文件以 UTF-8 无 BOM 保存，并检查编辑器编码设置。
- **部署后文章链接全部 404**：几乎都是 `baseURL` 配置错误，尤其是部署在子路径时漏写了结尾斜杠。
- **`hugo new` 生成的文件 front matter 是空的**：说明 `archetypes/default.md` 被改坏或被删除，从主题目录复制一份回来即可。
- **构建产物里没有图片**：图片是否放在了 `static/` 或文章所在的 Page Bundle 目录中，放在仓库根目录的其他位置不会被拷贝。

## 上手检查清单

按顺序过一遍，全部打勾说明第一个站点已经跑通：

- [ ] `hugo version` 正常输出版本号
- [ ] `hugo new site` 生成的目录结构完整
- [ ] 主题已加入 `themes/` 并在 `hugo.toml` 中启用
- [ ] 第一篇文章 `draft = false` 且能在列表页看到
- [ ] `hugo` 构建成功，`public/index.html` 存在
- [ ] 推送后线上域名能打开，样式与本地一致

## 小结

Hugo 的学习曲线集中在前半天：理解目录约定、front matter 和主题机制。跨过这一步，日常写作就只剩"新建 Markdown、写内容、推送"三个动作。下一步建议挑一个结构简单的主题，读一遍它的模板代码，这比通读文档更快建立整体认知；等默认主题玩熟了，再回来研究 archetype 与多环境配置，把站点调成自己顺手的样子。
