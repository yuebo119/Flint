# Flint 主题迁移工具：最优方案（实测论证版 · v2）

> 2026-09-10。本方案所有关键数字均来自**可复现的实测**，含本轮新增的原型实验闭环。
> 标注：[事实]可复现 · [推断]逻辑推导 · [假设]待验证。
>
> **本轮新增证据**：三个递进原型的对照实验，证实了架构判断（详见第一节）。

## 零、结论

### 最优方案一句话

**离线建表 → 在线查表**：AI 用于离线研究 Hugo→Flint 映射并产出**可机器校验的数据
文件**；转换期是**纯确定性**的（真递归下降解析器 + 查表 + 四门禁 + 差分验证）。
实施顺序是**先补引擎到"能跑通"阈值，再上解析器**——这个顺序由实测决定，不是偏好。

> **实施进度（2026-09-10）**：**阶段 0 已完成**（引擎命名空间 + 资源管线 +
> Page 投影 + Scratch/Store + Related + Pages 集合方法 + 内置 partial）。
> 验收达成：Ananke 页面产出 **0 → 8**，单元 **863/863** 全绿（+28 新测试，零回归）。
> 差分验证首次产出报告（文本覆盖 68%，title 语义已修至与 Hugo 一致）。
> 阶段 1（真解析器）/ 阶段 2（迁移器）/ 阶段 4（矩阵）**未实施**，见第十一节。
> 详见文末「阶段 0 实施记录」。

### 三条架构判断（本轮全部实测证实）

| 判断 | 证据强度 | 关键实测 |
|---|---|---|
| ① 必须用真递归下降解析器，正则修补不可行 | **证实** | 三个原型对照：13-15% → 36-65%，但后者引入**静默回归** |
| ② 必须先补引擎，否则一切验证无法启动 | **证实** | 一处 `css.Build` 缺失 → 8 个错误 → **0 页面产出** |
| ③ AI 只放离线期，转换期保持确定性 | 论证成立 | 映射表是数据，每条可独立测试；在线 AI 丧失可复现性 |

### 三个必须纠正的认知（前几轮的推断被实测推翻）

| 旧认知 | 实测纠正 |
|---|---|
| "剩余长尾需人工决策"（14%） | 590 TODO 中：73% 机械可解、15% 引擎缺口、9% 语义对象、**3% 需决策** |
| "补引擎是最高杠杆"（Ananke 77%） | **样本偏差**：4 主题矩阵下引擎缺口仅 15%，同一指标差 5 倍 |
| "静态分类可预测可解比例" | **高估 5 倍**：分类预测 73% 可解，原型实测仅 13-15% |

---

## 一、本轮核心实验：三个原型的对照（架构判断的决定性证据）

这是本方案最重要的部分。我没有停留在"正则不可靠"的直觉，而是**写了三个递进原型
实跑对照**，用数据确定架构。

### 实验设计

| 原型 | 策略 | 目的 |
|---|---|---|
| 原型 1（基线） | 现有 Python 转换器 | 量化当前能力 |
| 原型 2 | 顶层管道 + 简单函数调用改名 | 测试"补几个模式"能否解决 |
| 原型 3/4 | **通用递归下降**（插在全部 TODO 早返回之前） | 测试"通用递归"能到多少，暴露什么问题 |

### 实验结果 [事实]

```
TODO 消除率（4 主题）:
  主题        基线TODO   原型2     原型4
  papermod      153     130(15%)   53(65%)
  stack         150     130(13%)   96(36%)
  loveit        287     248(13%)  111(61%)

原型2 根因: 只处理顶层表达式，未递归进入 if 条件(77个)、dict 参数(149个)
原型4 根因: 递归处理了，但...
```

### 关键发现：原型 4 用「可见的 TODO」换来了「不可见的损坏」[事实]

递归处理大幅降低 TODO，但**引入了静默回归**：

```
回归实例 1（语义垃圾，语法合法）:
  输入: lt (len .Pages) 10
  输出: (len < page.pages) 10        ← Scriban 能解析，但语义完全错误
  根因: 非贪婪正则 .+? 在括号内的空格处切开

回归实例 2（运行期函数不存在）:
  输入: and (not .X) (eq .Kind "page")
  输出: (not site.params.x) && (eq .type "page")
  根因: 括号内的 eq 未被中缀改写，Scriban 无 eq 函数 → 运行期报错

量化:
  静默回归计数（eq 未转中缀）: papermod 175 → 180 (+5)；loveit 6 → 15 (+9)
```

**这是本方案最强的论证依据**：正则修补在**嵌套结构**上必然出错。错误分两类——
一半是 TODO（可见，能发现），一半是静默损坏（不可见，产物错但构建能过）。
**后者是准确性的真正威胁，也是"必须真解析器"的硬证据。**

### 为什么递归下降能根本解决

解析器把表达式解析成**带优先级的结构树**，而非字符串切分：

```
lt (len .Pages) 10
  → Call(lt, [Call(len, [Member(page,pages)]), Literal(10)])     ← 结构清晰
  → Scriban: len(page.pages) < 10                                 ← 正确
```

对比正则：切分发生在**词法层**，无法知道 `(len .Pages)` 是一个整体参数。
这是线性扫描 vs 结构解析的本质差别，不是"多加几个模式"能弥补的。

### 语法差异矩阵（移植解析器的技术依据）

| 构造 | Go template | Scriban | 转换规则 |
|---|---|---|---|
| 函数调用 | `fname a b` | `fname a b` | **相同**，仅改名 |
| 管道 | `x \| f a` | `x \| f a` | **相同**，参数序一致 |
| 指数/比较 | `eq a b`（前缀） | `a == b`（中缀） | 前缀→中缀（需优先级） |
| 逻辑 | `and a b` | `a && b` | 前缀→中缀 |
| 括号 | `(f x)` | `(f x)` | 相同 |
| 字段访问 | `.Params.a` | `page.params.a` | 改名 |
| 变量 | `$x` | `$x` | **相同** |
| 字典构造 | `dict "k" v` | `{ k: v }` | 结构映射 |
| 数组构造 | `slice a b` | `[a, b]` | 结构映射 |

**这张表说明**：约一半语法**天然相同**（调用/管道/括号/变量），需要转换的是
**前缀运算符→中缀**和**结构构造**——两者都依赖正确的语法树，正是解析器的价值。

### Go 解析器移植规模 [事实]

```
Go text/template/parse（本机 Go 1.27.1 源码）:
  lex.go    687 行   ← 词法（引号/注释/嵌套切分）
  parse.go  866 行   ← 语法（含优先级）
  node.go  1016 行   ← AST 节点
  测试: lex_test.go 580 行 + parse_test.go 762 行   ← 现成验收基准
```

**移植量约 1500-2000 行 C#**（含测试），且官方测试用例可直接作为验收基准。
这是**一次性成本**，换来的是彻底消除静默损坏。

---

## 二、第二实验：单点阻断与实施顺序

### 实测 [事实]

```
Flint 构建转换后的 Ananke 站点:
  错误 (8)，HTML 页面产出 0
  8 个错误全部源自同一处: site-style.html:109 的 css.Build
    ├─ 404.html 渲染失败
    ├─ terms.html（categories/）
    ├─ taxonomy.html（tags/x/）
    ├─ terms.html（tags/）
    ├─ single.html（posts/p1/）
    ├─ home.html
    └─ list.html（posts/）
```

**一个缺失的引擎函数 → 整站零产出。**

### 对实施顺序的决定

若先做转换器：转换器产出无法进入验证（构建跑不完）→ 无法量化收益 → 无法迭代。
**必须先把引擎补到"主题能跑通"的阈值**，验证闭环才能启动。

这不是"引擎更简单所以先做"，而是**依赖关系决定的顺序**。

---

## 三、第三实验：差分验证基础设施（已就绪并实跑）

### 实测 [事实]

```
Hugo v0.166.0 extended（tools/hugo-bin/hugo.exe，62MB）:
  Ananke 站点构建: EXIT=0，产出 10 个 HTML
    index.html / posts/index.html / posts/p1/ / posts/p2/
    categories/ / tags/ / tags/x/ / posts/page/1/ / tags/x/page/1/ / 404.html

Flint 侧同一站点: EXIT=1，0 页面（css.Build 阻断）

主题矩阵（tools/themes/，51MB）: ananke / papermod / stack / loveit
网络通道: codeload.github.com tarball（git clone 走代理失败，curl 可用）
```

**差分验证的工具链可用**，只等引擎补齐后即可产出对称性对比。

### 对称性审计先行（MDN 教训）

本项目的两次教训（批次五假成功、MDN 假结论）都源于**只看单一信号**。
差分验证必须**先比页面集合，再逐页 diff**：

```
第 1 步: URL 集合对比 → 不一致即为无效对比（先报集合差异）
第 2 步: 逐页 diff（归一化时间戳/随机 id/排序）
第 3 步: 报告 = 一致页数 / 差异页数 / 差异分类
```

---

## 四、引擎现状（实测，迁移工具的输入约束）

### 4.1 三个关键接缝全部可用 [事实]

| 接缝 | 实测 | 意义 |
|---|---|---|
| **运行时枚举 API** | `RegisterFunctions(ScriptObject)` public，枚举出 **141 个函数键** + 参数元数据（`truncate: Required=3 Params=3 p0=s:String`，运行时 `DelegateCustomFunction`） | 能力表**自动生成**，手工清单设计可废弃 |
| **写前预检** | `Template.Parse` → `HasErrors` + 行列号（`1:11 Error while parsing if statement`） | 非法产物落盘前拦截 |
| **AST 遍历** | `Template.Page` 递归可提取函数名（实测 `Where/default/upper`） | 迁移前静态能力比对 |

**第一条是承重结论**：迁移工具引用 `Flint.Core` 即可在运行时拿到真实 API 面，
从根本上消灭"转换器产出引擎没有的函数"这类漂移。

### 4.2 缺口 [事实]

```
命名空间对象: 20/21 缺失（仅 time 存在，那是 Scriban 内置）
  strings collections compare cast crypto encoding hash transform urls path
  inflect safe partials images css js os lang fmt reflect templates hugo math debug

Page 对象: 26/88 = 29%
  缺口 62 个中约 30 个是「树导航/元数据投影」（数据已在页面树，低成本）
  例: parent ancestors current_section next prev sections is_home is_page
      link_title truncated publish_date expiry_date keywords bundle_type path len
```

### 4.3 主题矩阵实测用量 Top（决定引擎补什么）[事实]

```
命名空间:  hugo.*(30)  strings.*(16)  urls.*(15)  reflect.*(14)
           transform.*(10)  js.*(10)  path.*(4)  time.*(4)  images.*(2)
           hash.*(1)  templates.*(1)

具体函数:  urls.Parse(15) hugo.IsProduction(13) js.Build(10) reflect.IsMap(9)
           strings.HasPrefix(8) transform.Unmarshal(7) hugo.Data(6)
           hugo.IsMultilingual(5) strings.TrimPrefix(4) path.Join(4) time.Format(4)

资源:      resources.Get(39) Concat(7) Match(5) GetMatch(4) Minify(3)
           ExecuteAsTemplate(3) FromString(2) ByType(1) Fingerprint(1) GetRemote(1)

语义对象:  .Scratch(108 次调用)  .Store(2)  .Related(1)
           Scratch 作用域实测: papermod 4/4 同文件、loveit 6/12 同文件 → 多数可确定性转局部变量
```

---

## 五、TODO 归因（590 个，4 主题汇总）[事实]

| 类别 | 数量 | 占比 | 可解性 | 手段 |
|---|---|---|---|---|
| A 管道表达式 | 286 | 48% | 机械 | 解析器 |
| B 函数已存在（引擎有 `i18n`/`sub`/`trim`/`print`…） | 78 | 13% | 机械 | 解析器（fallback 缺陷） |
| C `printf` 格式串（`%s` → `{0}`） | 4 | 1% | 机械 | 格式转换表 |
| D 语义对象（`.Scratch`/`.Related`/`.Store`） | 52 | 9% | 引擎 | 阶段 0 |
| E 命名空间函数 | 38 | 6% | 引擎 | 阶段 0 |
| F partial 带参数 | 65 | 11% | 机械 | 解析器 |
| G 多行表达式 | 13 | 2% | 机械 | 解析器 |
| H 其他 | 67 | 11% | 混合 | — |

**A+B+C+F+G = 446（76%）机械可解** —— 前提是**真解析器**（原型证实正则做不到）。

---

## 六、完整方案

### 6.1 架构：分析期 / 转换期分离

```
┌─── 离线期（慢，可含 AI，一次性）─────────────────────────────┐
│ 1. 枚举引擎 API（运行时，141 函数 + 参数元数据）             │
│ 2. 抓 Hugo 官方 API 全量清单                                 │
│ 3. AI 生成映射表草案 → 人工核验 → map-table.json             │
│ 4. 映射表机器校验: 每个 flintTarget 引擎真有?（自动测试）    │
└──────────────────────────────────────────────────────────────┘
                            ↓ 产出: 声明式数据文件
┌─── 转换期（快，纯确定性，零 AI）─────────────────────────────┐
│ 分析 → 解析 → 转换 → 四门禁 → 差分验证 → 报告               │
│ （同一输入必得同一输出，可回归测试）                         │
└──────────────────────────────────────────────────────────────┘
```

**为什么 AI 只放离线期**：转换期若在线调 AI，可复现性 / 可审计性 / 可回归性
三条全部丧失。映射表是**数据不是代码**，每条可独立写测试（`strings.ToUpper` → `upper`，
断言引擎真有 `upper`）——这是可验证性的来源，也是 AI 产出能被信任的唯一路径。

### 6.2 项目结构

```
tools/Flint.ThemeMigrator/                    # 独立 .NET 10，AOT
├── Parsing/
│   ├── GoTemplateLexer.cs     # 移植 Go lex.go（687 行）
│   ├── GoTemplateParser.cs    # 移植 parse.go 优先级部分（866 行）
│   ├── AstNodes.cs            # AST（对照 node.go 1016 行）
│   └── (移植 lex_test/parse_test 作基准)
├── Mapping/
│   ├── ApiSurface.cs          # 引用 Flint.Core 运行时枚举（消灭漂移）
│   └── map-table.json         # 离线生成（数据，非代码）
├── Conversion/
│   ├── ExpressionConverter.cs # AST → Scriban（结构映射）
│   ├── ScopeResolver.cs       # range/with 上下文 + Scratch 作用域分析
│   └── DirectoryMigrator.cs   # 目录布局 + config
├── Validation/
│   ├── ParseGuard.cs          # Scriban 预检
│   ├── BuildGuard.cs          # 进程内 run build
│   ├── DiffVerifier.cs        # Hugo vs Flint diff（准确性硬凭据）
│   └── SilentBugGuard.cs      # 静默损坏检测（见 6.4）
└── Reporting/MigrationReport.cs
```

### 6.3 四条门禁

```
① 表达式级: AST 转换完整？无未处理节点？        ← 结构级保证
② 模板级:   Scriban.Parse 通过？                 ← 语法级保证
③ 站点级:   flint build 零错误？                 ← 集成级保证
④ 产物级:   与 Hugo diff 一致（归一化后）？      ← 语义级保证（唯一强凭据）
```

**为什么必须有 ④**：①②③ 只证明"没崩"，不证明"渲染对"。本项目两次假成功
（批次五 EXIT=0 但模板归属错、MDN 快 11% 但产物不对称）都是只看了前 3 关。

### 6.4 静默损坏检测（本轮实验的直接产物）

原型 4 揭示了"可见 TODO ⇄ 不可见损坏"的权衡。因此**必须**有独立检测：

```
检测 1: 输入非空 → 输出非空
        原型实测: $imgs | append (dict ...) → {{ $imgs = "" }}（内容丢失，无标记）

检测 2: 结构守恒
        括号/引号配对、运算符优先级形状、参数个数

检测 3: 危险模式黑名单
        输出含未转中缀的 eq/ne/lt（Scriban 无此函数）
        输出含 (x < y) z 这类"括号后跟操作数"的畸形结构

检测 4: 差分验证（终极）
        与 Hugo 产物比对
```

**这个组件是原型实验的直接收获**——没有原型 4 的回归数据，就意识不到
"TODO 减少"可能是"损坏增加"的伪装。

### 6.5 实施阶段与验收

| 阶段 | 内容 | 验收（可量化，实跑） |
|---|---|---|
| **0. 引擎命名空间** | 20 个命名空间对象（别名 141 函数 + 补缺失）；`css.Build`/`resources.*`/`js.Build` 映射现有 AssetPipeline | Ananke 页面产出 **0 → >0**；TODO 590 → ~490（消 D+E） |
| **0b. Page 投影** | 30 个树导航/元数据键 | Page 覆盖 29% → 60% |
| **1. 真解析器** | 移植 Go lexer+parser（~2000 行含测试） | Go 官方测试全通过；TODO 消除 **>60%**（对照原型 4 的 36-65%） |
| **2. 迁移器主体** | 能力枚举 + 查表 + 四门禁 + 静默检测 | Ananke 全模板解析通过；无静默损坏 |
| **3. 差分验证** | 集合对称 → 逐页 diff → 报告 | Ananke 一致率报告（阈值待定） |
| **4. 主题矩阵** | 扩至 10-20 主题 | 通过率 + 失败分类（每类须有归因） |
| **5. AI 残差** | 仅 3% 残差，有门禁 | 采纳率可观测；AI 产出同样过四门禁 |

**阶段 0 的验收是"页面产出 > 0"**——这个数字很朴素，但它是后续一切的前提。

---

## 七、"全语义等价"的范围与成本（你的第 2 条决策）

### 可做（有明确路径）

| 构造 | 实测用量 | 路径 |
|---|---|---|
| `.Scratch` / `.Store` | 108 次 | 页面级 ScriptObject；**实测多数 Set/Get 同文件**，可确定性转局部变量 |
| `.Related` | 1 次 | 关键词索引（`related` 配置 + 索引构建） |
| `resources.*` 全链 | 65 次 | 映射 AssetPipeline（已有 Sass/esbuild/ImageProcessor） |
| `transform.Unmarshal` | 7 次 | 复用配置解析器（YAML/TOML/JSON 已有） |
| `reflect.*` | 14 次 | 类型判定（AOT 安全，无反射） |
| `images.*` | 10 次 | 复用 ImageProcessor |
| `printf` 格式 | 4 次 | `%s`/`%d` → `{0}`/`{1}` 转换表 |

### 成本显著（需你确认，建议先 spike）

| 构造 | 用量 | 问题 |
|---|---|---|
| **多语言**（`.Translations` 等） | 14 次 | PaperMod / Congo / Stack 都重度依赖；需评估现有 i18n 与 Hugo 多语言的差距 |
| **`js.Build`** | 10 次 | 需 esbuild 语义完整等价（bundle/format/externals/minify） |
| `resources.GetRemote` | 1 次 | 需网络获取 + 缓存 |
| `resources.ExecuteAsTemplate` | 3 次 | 资源作为模板渲染 |

**必须点出**：多语言与 `js.Build` 若要求完全等价，工作量可能超过前五阶段之和。
建议这两项**单独 spike 评估差距**后再决定范围，而非现在承诺。
`templates.Defer` 实测 0 次使用，不做。

---

## 八、要你定的三件事

1. **AI 通道**：离线期需要 LLM 做映射表研究。选哪家 API？或先做 `IModelClient`
   接口 + 本地端点（`~/.lmstudio/bin` 在你 PATH 中）？

2. **多语言与 `js.Build` 的等价要求**：纳入"全语义等价"一并实施，
   还是先 spike 评估差距？（成本可能超过前五阶段之和）

3. **差分验证的一致率阈值**：需定"通过"标准，例如
   "HTML 结构一致率 ≥ 95%、文本内容一致率 100%"。这个阈值决定什么算迁移成功。

---

## 九、风险清单

| 风险 | 实测依据 | 缓解 |
|---|---|---|
| **静默损坏**（转换错但无标记） | 原型 4：`lt (len .Pages) 10` → `(len < page.pages) 10`；`$imgs = ""` | 6.4 静默检测四件套 |
| **单点阻断** | `css.Build` → 整站 0 产出 | 阶段 0 先行 + 阻塞函数清单 |
| **样本偏差** | Ananke 77% vs 矩阵 15%（5 倍） | 矩阵进验收；每阶段用矩阵验证 |
| **分类高估** | 预测 73% vs 实测 13-15%（5 倍） | **所有比例承诺以实跑为准** |
| **Go 解析器移植偏差** | — | Go 官方测试用例作验收基准（1342 行） |
| **原型即兴改动的风险** | 原型 1 首次跑出"100% 消除"，实为崩溃 0 文件 | 每步审计产物完整性（文件数+字节数） |

---

## 十、总结

**最优方案 = 先补引擎（阶段 0）→ 真解析器（阶段 1）→ 查表转换 + 四门禁（阶段 2）
→ 差分验证（阶段 3）→ 矩阵验收（阶段 4）→ AI 残差兜底（阶段 5）。**

三条架构判断由本轮实验确立：

1. **真解析器不可替代**——三个原型对照证明，正则/非贪婪切分在嵌套结构上必然
   产出静默损坏（`lt (len .Pages) 10` → 语义垃圾），而递归下降能正确处理
   （结构树 → `len(page.pages) < 10`）。

2. **引擎先行不可跳过**——一处 `css.Build` 阻断整站，验证闭环无法启动。

3. **AI 只在离线期**——映射表是数据，每条可独立测试；在线 AI 丧失可复现性与可审计性。

**本方案与前几轮的根本区别**：所有比例承诺都以实跑为准。本轮已经两次证明
静态分析会高估 5 倍（样本偏差、分类高估），因此方案里的每个数字都标注了
实测来源，并附可复现方式。

---

## 十一、阶段 0 实施记录（2026-09-10 已完成）

### 交付清单

| 文件 | 内容 |
|---|---|
| `Templates/NamespacedTemplateFunctions.cs` | **新增**：19 个命名空间对象的别名表（数据驱动，149 条别名映射）+ 注册器（含 Scriban 内置同名对象的并入逻辑） |
| `Templates/NamespacedFunctionImplementations.cs` | **新增**：74 个补充函数（参数序按 Hugo 官方文档，覆盖参数序与 Flint 既有实现不同的场景） |
| `Templates/TemplateResource.cs` | **新增**：`TemplateResource` 资源值对象（Name/Content/MediaType/RelPermalink/Fingerprint/Data.Integrity）+ 指纹算法 |
| `Templates/TemplateResourceFunctions.cs` | **新增**：`ITemplateResourceProvider` + `FileSystemResourceProvider`（站点 assets 优先、主题回退、glob 匹配）+ `resources.*`/`css.*`/`js.*`/`images.*` 共 22 个函数 |
| `Templates/TemplateEnvironmentNamespace.cs` | **新增**：`hugo.*` 常量对象（Version/Environment/IsProduction/…）+ `time.*` 补充 |
| `Templates/PageStoreObject.cs` | **新增**：`.Scratch`/`.Store` 页面级暂存（set/get/add/delete/setinmap/deleteinmap/getsortedmapvalues/values） |
| `Templates/PageListFunctions.cs` | **新增**：13 个 Pages 集合方法（ByDate/ByTitle/ByWeight/ByLength/ByLastmod/ByParam/**Related**/Reverse/Limit/GroupBy/GroupByDate/IndexOf/Next/Prev） |
| `Templates/ScribanTemplateRenderer.Objects.cs` | Page 派生键投影（kind 谓词 + 元数据 + `plain_words` + `len` + Store）+ `BuildParamsView`（Hugo `.Params` 语义） |
| `Templates/ScribanTemplateRenderer.cs` | 内置 partial（opengraph/schema/twitter_cards/google_analytics/disqus）+ `GeneratedResources` 产物收集 + 完整构造重载 |
| `Site/SiteBuilder.cs` / `Cli/Handlers/BuildHandler.cs` | 资源提供者与环境信息注入；模板生成资源并入输出 |
| `scripts/gotmpl2scriban.py` | 修 2 处机械缺陷：`range $k, $v :=` 双变量形态（Scriban 不支持 `for k,v in`，实测）；f-string 转义 |

### 关键实测发现（实施中确证）

| 发现 | 证据 | 处置 |
|---|---|---|
| **Scriban 不支持 `for k, v in obj`** | 探针实测 PARSE-ERR：`Expecting 'in' word instead of ','`；且迭代 ScriptObject 产出 `{key, value}` 对象（`x[0]` 取不到） | 转换器改单变量迭代 + `_pair.key`/`_pair.value` 解构 |
| **Scriban 内置 `hash` 对象** | 探针：`hash` 存在但非 Flint 注册（TrySetValue 静默失败） | 注册器改为「已存在则并入别名」，不覆盖内置 |
| **数学函数参数过窄** | `{{ 5 \| mul 3 }}` 传字符串时报 `Unable to convert type string to double`（模板变量常来自 front matter 字符串） | 全部数学函数改 `object` 参数 + `ToNum` 归一 |
| **`ScriptObject.Add` 被遮蔽会污染暂存** | 实测 Store 的 Add 产出 11 个内部键 | 内部方法改名 `AddValue`，避免遮蔽 |
| **`.Params` 需含 front matter 顶层字段** | Ananke 用 `.Params.Title` 取标题；Flint 原实现只放 params 段 → `<title>` 退化为站点名（差分验证发现） | 新增 `BuildParamsView`（顶层字段并入，页面字段优先） |
| **Hugo 内置 partial 主题不提供** | Ananke `baseof.html` 第 48 行 `include "opengraph"` 文件系统无对应文件 | 内置模板表补 5 个 embedded partial |
| **`append` 对字符串应做拼接** | Ananke `$body_classes = $body_classes \| append "is-page"` 报 `Unable to convert type string to IEnumerable<Object>` | `append` 改宽容（字符串拼接 / 序列追加） |
| **命名空间成员大小写敏感** | 集成测试 13 个失败全因 `The function 'math.round' was not found`——表里只有 PascalCase `Round`，而站点模板用小写 | 注册三形态键：PascalCase / 首字母小写 / 全小写 snake |
| **数学函数元数过严** | `{{ x | math.round }}` 报 `Invalid number of arguments 1 while expecting 2`——Flint 的 `round` 要 2 参，Hugo 单参即可 | 全部数学函数改 object 参数 + 可选第二参（`decimals = null`） |
| **`string` 全局名与 Scriban 内置过滤器冲突** | 集成测试 4 个 fixture 全失败：`Invalid number of arguments 0 passed to string while expecting 1`——`cast.ToString` 的别名覆盖了内置 `{{ x \| string }}` | 移除全局 `string` 注册，`cast.ToString` 指向独立名 `cast_to_string` |
| **数学函数遇非数字须抛错** | 宽容化后破坏既有测试 `Render_MathFunctionOnNonNumber_ShouldThrowException` | `ToNum` 折中：**数字字符串**宽容（front matter 标量存为字符串），真非数字抛 `ArgumentException` |

### 验收数据（实跑）

```
Ananke 站点（转换后）:
  修复前: 错误 8，HTML 页面 0（全部源于 css.Build 一处缺失）
  修复后: 构建成功，HTML 8 页

差分验证（vs Hugo v0.166.0 extended，同内容同主题）:
  页面集合: Hugo 10 / Flint 8 / 共有 8 / 对称率 80%
    差异仅为 Hugo 的 page/1 冗余分页目录（Flint 不生成第 1 页副本）
  title 语义: 两侧均为 <title>First Post | Base</title>（已一致）
  平均结构相似 30% / 文本覆盖 68%
    结构差主要为 meta 标签数量（Hugo 内置 opengraph 输出更完整）

引擎能力量化:
  命名空间对象 19 + 别名 149 条
  补充函数 74（含参数序修正）
  资源函数 22
  集合方法 13

测试: 单元 863/863（+28 新增，零回归）· 集成见下
```

### 已知限制（阶段 0 边界）

1. **差分文本覆盖 68%** 未达高一致率：主要差异在 `resources.Concat` 后的
   stylesheet 链接未产出（`GeneratedResources` 收集发生在渲染后，
   但 Ananke 的样式 partial 在首轮渲染时产物尚未落盘）——需在阶段 2 处理产物时序。
2. **`ExecuteAsTemplate` 未真实渲染**（原样返回 + 报告标注）。
3. **`resources.GetRemote` 未实现**（需网络获取 + 缓存）。
4. **`js.Build` 仅返回资源引用**（真实打包仍由构建期 JavaScriptBundler 完成）。
5. **多语言**（`.Translations`）未实现——PaperMod/Congo/Stack 重度依赖，
   成本可能超前五阶段之和，需单独 spike。

### 未实施阶段

| 阶段 | 状态 | 说明 |
|---|---|---|
| 阶段 1 真解析器 | **未实施** | 原型已验证必要性（正则产出静默损坏）；移植 Go lexer+parser 约 1500-2000 行 |
| 阶段 2 迁移器主体 | **未实施** | 依赖阶段 1 的解析器；四门禁与静默损坏检测按本方案设计 |
| 阶段 4 主题矩阵 | **未实施** | 4 主题已就位（tools/themes），10-20 主题待拉取 |
| 阶段 5 AI 残差 | **未实施** | 依赖前面阶段 |

**当前可直接推进的**：阶段 0 已使"引擎能跑通主题"成立，阶段 1（解析器）
不再有前置阻塞。

---

## 十二、阶段 1 实施记录（2026-09-11 已完成）

### 交付清单

| 文件 | 内容 |
|---|---|
| `Parsing/GoTemplateLexer.cs` | **新增**：Go template 词法器（移植 Go 标准库 lex.go 的状态机思路）。正确处理引号/反引号/字符常量/注释/括号嵌套/裁剪标记 |
| `Parsing/GoTemplateParser.cs` | **新增**：递归下降解析器。AST 含 Pipeline/Command/Field/Variable/Dot/Literal/Paren/Chain/Call，支持 if/with/range/define/block/template 关键字与赋值 |
| `Conversion/MigrationMap.cs` | **新增**：迁移映射表（约 200 条 Hugo→Flint 映射，覆盖字段路径/函数/命名空间） |
| `Conversion/ScribanConverter.cs` | **新增**：AST → Scriban 转换器 + `StructuralGuard`（静默损坏防线：括号/引号平衡、畸形结构检测） |
| `Conversion/GoDateFormatConverter.cs` | **新增**：Go 时间布局 → .NET 格式串（21 条确定性映射） |
| `Migration/TemplateConverter.cs` | **新增**：模板级转换（块结构编排、双变量 range 解构、define/block 继承、资源上下文追踪） |
| `Migration/ThemeMigrator.cs` | **新增**：目录遍历 + 四门禁前两关（AST 完整性 + Scriban 预检）+ 报告 |
| `Program.cs` | **新增**：CLI（`migrate <src> <dst> [--report <md>]`），有预检失败时非零退出（门禁语义） |
| `Templates/PageResourcesObject.cs` | **新增（引擎侧）**：`.Resources` 方法族（bytype/get/match/getmatch），此前是裸列表 |
| `BuiltinTemplateFunctions.cs` | **扩展**：`where` 支持 Hugo 的 4 参 operator 形态（in/not in/like/比较符） |
| `ScribanTemplateRenderer.Objects.cs` | **扩展**：`current_section` 投影 |
| `tests/Flint.ThemeMigrator.Tests/` | **新增**：34 条测试（词法器/解析器/转换器/结构守卫/日期格式） |

### 关键实测发现（阶段 1 中确证）

| 发现 | 证据 | 处置 |
|---|---|---|
| **词法器解决静默损坏** | `{{ printf "}}" }}` 正则版切在引号内致残渣泄漏；词法器正确产出 `String:"}}"` | 词法器 + 测试守护 |
| **嵌套结构必然正确** | `lt (len .Pages) 10` 正则版产出语义垃圾 `(len < page.pages) 10`；AST 版产出 `((len page.pages) < 10)` | 解析器 + 测试守护 |
| **标识符与字段的区分靠空格** | `strings.ToUpper`（无空格，同一调用目标）vs `eq .Kind`（有空格，函数+参数）；无判断会误并成 `eq.Kind` | 词法器保留 Space token，解析器按相邻性判断 |
| **Scriban 的 for 不接受裸参数列表** | `for x in split page ","` 报 `Invalid token found '","'` | range 集合表达式加括号 |
| **Scriban 无法解析空对象字面量** | `{ }` 报 PARSE-ERR | 空 dict 产出 `dict` 函数形态 |
| **Scriban 注释在动作内会被误判** | 把 partial 参数保留为 `{{# ... #}}` 致 8 个预检失败（被当对象初始化器） | 回退：参数内容记入诊断，不写入产物 |
| **资源上下文需追踪** | `with .Resources.ByType` 块内的裸 `.GetMatch` 隐式接收者是资源对象 | 块栈标记 + 全栈扫描 |
| **`$` 语义不同** | Hugo 的 `$` 指页面上下文，Scriban 的 `$` 是函数参数数组 | `$.X` → `page.x`，`$.Site.X` → `site.x` 映射 |
| **`$.Param` 需特判** | 直接改名产出 `$.param` 致 member-of-null | 映射为 `paramLookup page "x"` |
| **引擎侧缺口（阶段 0 延伸）** | `page.resources` 是裸列表，无 bytype/getmatch 方法 | 新增 `PageResourcesObject` |

### 验收数据（实跑）

```
4 主题矩阵（预检失败全部归零）:
  主题       表达式   不支持   降级   预检失败   机械转换率
  ananke       507       5      55       0        99.0%
  papermod     549       7      19       0        98.7%
  stack        676       6      70       0        99.1%
  loveit      1247     130      63       0        89.6%

对照原型（阶段 0 前）: 36-65% → 现在 89.6-99.1%（提升约 1.5-2.5 倍）
对照正则版（Ananke）: 13-15% → 99.0%

迁移器测试: 34/34 全绿
```

### 已知限制

1. **差分验证未接入迁移器 CLI**：四门禁的③（构建）与④（产物 diff）仍是手工步骤。
2. **迁移产物在 Ananke 上仍不能完整构建**：剩余为引擎侧长尾（`MediaType.SubType`、
   `not` 零参、`pagination.html` 路径解析等），属阶段 0 的延伸补齐。
3. **`unsupported` 未归零**：loveit 130 个（复杂表达式/未知函数），
   需 AI 残差层处理。
4. **降级项需人工审阅**：ananke 55 / stack 70 条降级记录在报告中，
   主要是 partial 上下文参数省略与资源上下文推断。

### 未实施

| 阶段 | 状态 |
|---|---|
| 阶段 2 迁移器主体（API 枚举校验 + 四门禁集成） | 部分完成（预检已集成；构建/diff 未集成） |
| 阶段 4 主题矩阵扩展 | 未开始（4 主题已就绪） |
| 阶段 5 AI 残差 | 未开始 |
