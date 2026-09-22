# 多主题演示站与主题画廊

一套测试语料、21 个适配主题、一个统一入口。用于主题选型对比与渲染验证。

## 快速开始

```bash
# 1. 全量重建 21 个主题演示站，并按子路径归并到单一服务树 demo-unified/
bash scripts/demo-sites.sh              # 全部主题
bash scripts/demo-sites.sh narrow       # 仅指定主题
SERVE=1 bash scripts/demo-sites.sh      # 构建后启动统一静态服务

# 2. 截 21 张主题预览图（Playwright，1280×800，需统一服务已启动）
#    输出到 demo-gallery/static/shots/<主题>.png

# 3. 构建画廊并合并进统一树根（画廊即 / 入口）
bash scripts/build-gallery.sh
```

统一入口：`http://127.0.0.1:8400/`（画廊）；各主题在子路径
`http://127.0.0.1:8400/<主题名>/`（如 `/fixit/`、`/stack/`）。

产物位置（均在 git 仓外，属生成物）：`../demo-sites/<主题>/`（各站独立构建源 +
`public/`）、`../demo-unified/`（单一端口服务树：根为画廊，`/<主题>/` 为各主题站点）。

## 主题画廊（案例站）

`scripts/gallery/` 是画廊站源码（Flint 自建，无主题依赖）：`layouts/index.html`
为单页卡片网格，21 个主题的预览图 + 名称 + 风格标签，点击卡片直达对应主题演示站的
子路径，顶部输入框按名称/风格/标签筛选。主题清单硬编码在模板里（`$themes` 数组），
卡片链接与 `demo-sites.sh` 的主题名一一对应：

| 主题 | 统一入口 | | 主题 | 统一入口 |
|---|---|---|---|---|
| ananke | /ananke/ | | m10c | /m10c/ |
| bearblog | /bearblog/ | | monochrome | /monochrome/ |
| blog-awesome | /blog-awesome/ | | narrow | /narrow/ |
| blowfish | /blowfish/ | | papermod | /papermod/ |
| clarity | /clarity/ | | stack | /stack/ |
| console | /console/ | | techdoc | /techdoc/ |
| even | /even/ | | xmin | /xmin/ |
| fixit | /fixit/ | | yinyang | /yinyang/ |
| github-style | /github-style/ | | **画廊** | **/** |
| hugo-book | /hugo-book/ | | | |
| hugo-coder | /hugo-coder/ | | | |
| hugo-paper | /hugo-paper/ | | | |
| loveit | /loveit/ | | | |

## 单端口统一架构

SSG 的主题是**站点级**能力（一个渲染站只能挂一个主题），21 个主题无法进同一渲染
站；但 Hugo/Flint 的 baseURL 支持**子路径**（GitHub Pages 项目站形态），因此：

1. `demo-sites.sh` 给每个主题站点写 `baseURL = "http://127.0.0.1:8400/<主题>/"`，
   构建产物的 relURL/RelPermalink/资源链接/feed/canonical 全部落在 `/<主题>/` 下；
2. 各站 `public/` 归并到 `demo-unified/<主题>/`（子路径 baseURL 的产物天然自包含，
   不发生重写）；
3. 画廊构建后合并进 `demo-unified/` 根，成为 `/` 入口；
4. 单一静态服务（`python -m http.server 8400 --directory demo-unified/`）同时提供
   画廊与 21 个主题站。

引擎侧关键实现：`TemplateResource.BasePathOf/OriginOf` 统一解析 baseURL 的子路径与
源站，RelUrl/AbsUrl、页面 permalink、`TemplateResource.Create`（toCSS/js.Build/
Concat/Copy 等）、`WithFingerprint`、sitemap/feed 输出、404/robots 均基于该前缀
推导，根 baseURL（无子路径）时行为与历史完全一致（1095 项测试覆盖）。

## 测试语料库（scripts/fixtures/corpus/）

100 篇文章，每篇正文不少于 2000 中文字符，21 个主题共用同一套内容：

```
corpus/
  content/
    _index.md          站点首页
    posts/_index.md    55 篇深度长文（前端/后端/数据库/云原生/AI/语言基础）
    weekly/_index.md   15 期技术周刊
    notes/_index.md    15 篇工具技巧笔记
    docs/_index.md     15 篇教程/指南/专题文档
    about/_index.md    关于页
  static/images/       12 张占位插图（demo-1..12.svg，gen_images.py 生成）
  count_chars.py       字数校验：python count_chars.py [section]
  gen_images.py        占位图生成器（仅生成新文件）
```

多样性设计：文章主题覆盖六大技术方向；文体覆盖教程/深扒/评测/笔记/周刊/案例/
对比/观点/清单/编年史/FAQ/术语表；Markdown 元素覆盖二三级标题、代码块（十种语言）、
表格、引用、任务列表、脚注、数学公式、折叠块、嵌套列表、分割线、图片、行内代码、
外链——用于验证各主题的渲染完整度。

`demo-sites.sh` 把 corpus 的 `content/` 与 `static/` 分别拷入各站点对应目录
（21 主题同源，非副本漂移）。

## 已知事项

- fixit 是 21 主题中构建最慢的（~5.5 分钟/631 页），`demo-sites.sh` 单站超时
  已放宽到 600s；慢的根因与修复见 `docs/PERFORMANCE-OPTIMIZATION.md` 阶段七
- `site.webmanifest` / favicon 已由 corpus 静态目录统一提供（`scripts/fixtures/
  corpus/static/`：根路径一套 + `images/` 一套，覆盖各主题的不同引用路径）；
  早期"主题引用不存在的文件"问题已修复
- 重建演示站前需先停掉统一服务（Windows 下服务进程 CWD 在 `demo-unified/`
  外则无锁问题；`--clean` 只清各站自己的 `public/`）：若 8400 已被占用，
  `netstat -ano | grep :8400` 找 PID 后 `taskkill /F /PID <pid>`
