# 案例站项目（demo-sites/）

一套测试语料、21 个适配主题、一个画廊入口。用于主题选型对比与渲染验证。
独立项目目录：只读消费 `../theme-migrator/themes/` 的主题语料与仓库 `src/`
的引擎产物，自身构建产物（各站 `public/`、`themes/`、画廊 `public/`）不入库。

## 项目结构

```
demo-sites/
├── demo-sites.sh        # 21 主题建站（建站+迁移+构建+报告）
├── build-gallery.sh     # 画廊构建（源 → gallery/ 落地）
├── demo-serve.py        # 单进程多端口静态服务（8400 画廊 + 8401-8421 主题）
├── demo-stop.sh         # 停止全部服务
├── fixtures/corpus/     # 统一测试语料（100 篇长文 + 静态资源）
├── gallery-source/      # 画廊站源码（Flint 自建，无主题依赖）
├── gallery/             # 画廊站落地形态（static/shots/ 21 张预览图入库）
├── screenshots/         # 主题适配过程截图 31 张（验证留档；正式预览图在 gallery/static/shots/）
└── <主题>/ ×21           # 各主题站点源（public/、themes/ 为生成物，不入库）
```

## 快速开始

```bash
# 1. 全量重建 21 个主题演示站（每个主题独立站点，端口 8401-8421）
bash demo-sites/demo-sites.sh              # 全部主题
bash demo-sites/demo-sites.sh narrow       # 仅指定主题
SERVE=1 bash demo-sites/demo-sites.sh      # 构建后启动单进程多端口服务

# 2. 截 21 张主题预览图（Playwright，1280×800，需演示站服务已启动）
#    输出到 demo-sites/gallery/static/shots/<主题>.png

# 3. 构建画廊并合并进统一服务（画廊即 8400 端口根）
SERVE=1 bash demo-sites/build-gallery.sh

# 停止全部服务（单进程 demo-serve.py + 历史遗留 per-port http.server）
bash demo-sites/demo-stop.sh
```

**服务架构（2026-09-24 起）**：22 个端口由**一个** asyncio 进程服务
（`demo-sites/demo-serve.py`，端口→文档根映射走命令行参数）。此前每端口一个
`python -m http.server`，实测 22 个解释器进程占 490MB 工作集（空闲 CPU 为零
——内存全花在解释器自身）；合并后 **21MB**，且并发取文件不再排队。附带
`demo-sites/demo-stop.sh` 清理历史僵孤进程（实测出现过 22 listener 对应 26 个
python 进程）并解除 Windows 下 public/ 目录锁。

## 主题画廊（案例站）

`gallery-source/` 是画廊站源码（Flint 自建，无主题依赖）：`layouts/index.html`
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

## 测试语料库（fixtures/corpus/）

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

## 关联工具（仓库 scripts/）

- `scripts/theme-matrix20.sh`——20 主题横向兼容矩阵（结构相似度 TSV 输出）
- `scripts/run-performance-tests.*`——性能套件入口

## 已知事项

- fixit 是 21 主题中构建最慢的（~5.5 分钟/631 页），`demo-sites.sh` 单站超时
  已放宽到 600s；慢的根因与修复见 `docs/PERFORMANCE-OPTIMIZATION.md` 阶段七
- `site.webmanifest` / favicon 已由 corpus 静态目录统一提供（`fixtures/
  corpus/static/`：根路径一套 + `images/` 一套，覆盖各主题的不同引用路径）；
  早期"主题引用不存在的文件"问题已修复
- fixit/loveit 的 SCSS 经 `toCSS (dict "vars" …)` 传入变量字典，SCSS 侧用
  `@forward "hugo:vars"`（Hugo Pipes 的虚拟导入）取用。Flint 调外部 sass CLI
  （无自定义 importer、`hugo:` 含冒号不是合法 Windows 文件名），实现为**源码镜像
  文本替换**：把全部 .scss 的 hugo: 导入替换成生成的 `$k: v;` 声明，值按 Hugo 的
  isTypedCSSValue 规则格式化（hex/函数/单位原样，字体名等引号化）。Sass 可执行
  文件随 CLI 发布在 Flint.exe 旁的 `dart-sass.win-x64/`
- 重建演示站前需先停掉对应端口的静态服务（Windows 下服务进程 CWD 在 public/
  内会锁目录）：`netstat -ano | grep :84xx` 找 PID 后 `taskkill /F /PID <pid>`
- `gallery/static/shots/` 下 21 张预览图由截图流程生成、**随仓库入库**——
  `build-gallery.sh` 只覆盖 hugo.toml/layouts/content，绝不触碰 static/（实测踩坑）
