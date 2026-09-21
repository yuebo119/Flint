# 多主题演示站与主题画廊

一套测试语料、21 个适配主题、一个画廊入口。用于主题选型对比与渲染验证。

## 快速开始

```bash
# 1. 全量重建 21 个主题演示站（每个主题独立站点，端口 8401-8421）
bash scripts/demo-sites.sh              # 全部主题
bash scripts/demo-sites.sh narrow       # 仅指定主题
SERVE=1 bash scripts/demo-sites.sh      # 构建后批量启动静态服务

# 2. 截 21 张主题预览图（Playwright，1280×800，需演示站服务已启动）
#    输出到 demo-gallery/static/shots/<主题>.png

# 3. 构建并启动画廊站（端口 8400）
SERVE=1 bash scripts/build-gallery.sh
```

产物位置（均在 git 仓外，属生成物）：`../demo-sites/<主题>/`、`../demo-gallery/`。

## 主题画廊（案例站）

`scripts/gallery/` 是画廊站源码（Flint 自建，无主题依赖）：`layouts/index.html`
为单页卡片网格，21 个主题的预览图 + 名称 + 端口 + 风格标签，点击卡片直达对应
主题演示站，顶部输入框按名称/风格/标签筛选。主题清单硬编码在模板里
（`$themes` 数组），端口与 `demo-sites.sh` 的 `THEME_PORT` 固定映射一致：

| 主题 | 端口 | | 主题 | 端口 |
|---|---|---|---|---|
| ananke | 8401 | | m10c | 8414 |
| bearblog | 8402 | | monochrome | 8415 |
| blog-awesome | 8403 | | narrow | 8416 |
| blowfish | 8404 | | papermod | 8417 |
| clarity | 8405 | | stack | 8418 |
| console | 8406 | | techdoc | 8419 |
| even | 8407 | | xmin | 8420 |
| fixit | 8408 | | yinyang | 8421 |
| github-style | 8409 | | **画廊** | **8400** |
| hugo-book | 8410 | | | |
| hugo-coder | 8411 | | | |
| hugo-paper | 8412 | | | |
| loveit | 8413 | | | |

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
- 各站 `site.webmanifest` 404 是主题自身引用不存在的文件（Hugo 下同样 404），
  非 Flint 问题
- 重建演示站前需先停掉对应端口的静态服务（Windows 下服务进程 CWD 在 public/
  内会锁目录）：`netstat -ano | grep :84xx` 找 PID 后 `taskkill /F /PID <pid>`
