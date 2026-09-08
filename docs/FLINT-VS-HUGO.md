# Flint vs Hugo：性能测试过程中实际遇到的问题所折射的差异

> 本文不是功能清单，而是四次性能对比测试（万页合成、官方基准 3978 页、
> MDN+k8s 14970 页、四批重构前后）中**实际触发的问题**所折射的引擎差异。
> 每条：现象 → 根因 → 两引擎各自的现状。

## 1. 大列表渲染：迭代上限

- **现象**：万页站点的 taxonomy 页与首页大列表直接渲染失败，
  "Exceeding number of iteration limit `1000` for loop statement"。
- **根因**：Scriban TemplateContext 默认 LoopLimit=1000；Hugo 的 Go template
  无迭代次数上限。
- **现状**：Flint 已修复（三处 context 统一 LoopLimit=1_000_000，706ae75）。
  修复本身由对比测试发现——标准数据集对比的价值实证。

## 2. 默认 permalink：根堆积 vs 目录结构

- **现象**：万页语料下 Flint 的 10000 个页面全部输出在站点根
  （/page-1/、/page-2/…），Hugo 输出在 /posts/page-1/ 分组目录。
- **根因**：Flint 旧默认 /:title/（按标题扁平），Hugo 默认"目录结构即 URL"
  （content/posts/x.md → /posts/x/）。
- **现状**：已裁决对齐 Hugo 目录结构（1dcd997，BREAKING）。附带修复
  /:title/ 的跨 section 同名冲突缺陷。

## 3. 配置-引擎断链：声明了但不消费

- **现象**：PermalinkConfig.Posts 默认值 /:year/:month/:title/ 从未被引擎
  消费（引擎只认 front matter type 字段）。
- **根因**：配置模型与引擎实现脱节——"先建完整语义、后接消费端"的顺序
  颠倒，同类断链曾涉及菜单/校验/环境覆盖/自定义分类（四批重构已全部
  接线或裁决）。
- **现状**：PermalinkConfig 默认值改空（目录结构语义），显式配置才展开。

## 4. 缺失模板：fail-fast vs 宽容降级

- **现象**：合并真实站点中声明自定义 layout（glossary、kubectl-all-subcommands）
  的页面，Flint 构建失败（RENDER001），Hugo 同场景输出占位/跳过继续。
- **根因**：Flint 对缺失模板 fail-fast（错误可见性好），Hugo 宽容降级
  （大规模构建"带病通过"）。
- **现状**：两种哲学各有取舍。Flint 在转换语料时剔除这类页面保证对比；
  产品层面保留 fail-fast（错误早暴露），可讨论增加 --lenient 选项。

## 5. 数据杂质容忍度：旧键与重复键

- **现象**：k8s 文档含已被 Hugo 0.145 移除的 `_build` front matter 键
  （新版构建直接报错）；部分真实页面 YAML 键重复（date 定义两次）。
- **根因**：Hugo 自身跨版本破坏（0.145 移除 _build）与 YAML 严格化；
  真实世界的历史内容存在大量此类残留。
- **现状**：两引擎新版本都会拒绝。对比语料转换时统一过滤——这也是
  "真实数据集对比"比合成数据集多出来的工作量。

## 6. 短代码生态：内置集与主题依赖

- **现象**：合并真实站点 4382 页中 400 页依赖原主题的 shortcode
  （bloglink 等），两引擎都无法解析。
- **根因**：Hugo 的 shortcode 生态沉淀在主题里（每个主题自带一批），
  脱离主题即失效；Flint 内置 10 个短码 + layouts/shortcodes/ 模板短码。
- **现状**：对比测试剔除处理。生态差距是真实的，属长期建设项。

## 7. 性能特征：规模规律与稳定性

- **现象**：三规模对比——3978 页 0.98x、10000 页合成 0.92x、
  14970 页真实 0.89x；Flint 三跑方差 ±23ms vs Hugo ±105ms。
- **根因**：Flint 并行管线（解析/渲染/写入全并行 + Server GC + 各级缓存）
  在大规模下扩展性更好；Hugo 单线程段（ assembling 等）在大规模下占比上升。
- **现状**：规模越大 Flint 优势越明显，且输出更稳定。

## 8. 工程行为差异（对比测试中的工具性观察）

- **编码**：Hugo Windows 控制台输出 GBK（自动化捕获需注意解码）；
  Flint 强制 UTF-8。
- **迭代上限、null 语义、null 字段三格式统一**等历史差异见
  docs/HUGO-GAP-TASKS.md 前序记录。

## 总结

四次对比测试暴露的 Flint 缺陷（LoopLimit、permalink 断链、页面对象
未映射）全部已修复并推送；暴露的 Hugo 特征（版本破坏、宽容降级、
控制台编码）记录在案。性能结论：合成 0.92-0.95x、官方数据集 0.98x、
MDN+k8s 0.89x——规模越大 Flint 越快、越稳。
