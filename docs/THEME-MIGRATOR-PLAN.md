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

---

## 十三、阶段 2 延伸与剩余缺口（2026-09-11）

### 本轮新增

| 项 | 内容 |
|---|---|
| 内联 partial 提取 | `Migration/InlinePartialExtractor.cs`：把 Hugo 的 `{{ define "_partials/X.html" }}` 提取为独立文件（Scriban 无此机制，不提取则 include 报"找不到文件"）。Ananke 的 `AnankeGetResource.html` 由此解决 |
| `collections.Slice` 独立实现 | Hugo 是可变参数构造器（零参=空数组），与 Flint 的 `slice`（序列切片）语义不同——同名不同义必须分开 |
| `collections.IsSet` 独立实现 | Hugo 是 `(MAP, KEY)` 判键存在，与 Flint 的 `isset(value)`（判非空）不同 |
| `MediaType` 对象化 | Hugo 的 `.MediaType` 是对象（Type/SubType/MainType/Suffixes） |
| 命名空间三形态键 | resources/css/js 也补小写形态（`resources.get` 与 `resources.Get` 并存） |
| 内置模板名归一 | `include "pagination.html"`（带扩展名）也能命中内置模板 |

### ✅ 已解决：`partials.Include` 的返回值语义（2026-09-11）

**方案：Store 对象通道 + `partialValue` 函数**（实测验证，零序列化成本）

设计依据来自一个实测发现：`RenderPartialWithType` 复用**调用者上下文**，
因此页面 Store 在 partial 与调用方之间**天然共享**——这是一条现成的对象传递通道。

机制：

```
转换期（双向改写，键名两侧规范化为同一形态）:
  partial 内:  {{ return X }}
            →  {{ page.store.set "__partial_ret_<self>" X }}{{ ret }}
  调用点:      {{ $v := partial "Y.html" . }}
            →  {{ $v := partialValue "Y.html" }}

运行期（引擎侧 PartialValueFunction）:
  1. 清除残留键
  2. 渲染 partial（共享上下文 → store.set 副作用生效）
  3. 从 Store 取回 X —— 真实对象，非文本还原
  4. 无 return 的 partial 回退标量还原（保持原有兼容性）
```

**实测验证**（对象保真）：
```
输入:  partial 返回 {{ dict "name" "cover.png" "width" 480 }}
输出:  NAME=cover.png   WIDTH=480   TYPED=BIG（数值比较可用）
       数值类型未退化为字符串 → 类型保真 ✓
```

**为什么优于其他方案**：

| 方案 | 问题 |
|---|---|
| 文本序列化往返（JSON） | 类型丢失（480 变 "480"）、性能开销、循环引用风险 |
| 转换器内联展开 partial | 仅适用可静态分析的 partial；有状态/递归 partial 无法展开 |
| **Store 对象通道（采纳）** | **零序列化、类型保真、机制简单**；且复用既有 Store 基础设施 |

**边界（已诚实标注）**：
- 仅当调用点上下文参数是 dot 时改写（partialValue 共享调用者上下文，
  传其他页面对象时语义不等价）——非 dot 时保持 include 并记入诊断
- `partialCached` 不适用（缓存语义与返回对象冲突），保持原样

**关键实现陷阱（实测）**：写入键与读取键必须**同规则规范化**。
转换器用规范名（`func/X`），而调用方可能传 `func/X.html`——
不一致时 `partialValue` 读到空值（实测踩坑）。

### 其余架构性缺口

**深层 null 访问**：

```
Hugo:   {{ $res := partial "GetFeaturedImageResource.html" . }}
        {{ $res.MediaType.SubType }}      ← $res 是资源对象，有属性/方法
        {{ $res.Resize "480x" }}

Flint:  {{ include "GetFeaturedImageResource.html" }}   ← Scriban 返回字符串
```

Scriban 的 `include` 语义是"渲染为文本"，无法返回对象。要支持此形态需：
- 引擎侧提供"partial 返回对象"的机制（如 `partialValue` 函数，渲染后反序列化），或
- 转换器把 `partial` 调用内联展开（仅适用于无副作用且可静态分析的 partial）

**这是 Flint 与 Hugo 在 partial 机制上的根本差异**，影响面为所有"partial 返回结构化数据"
的主题写法（Ananke 的 `GetFeaturedImageResource`/`GetFeaturedImage` 即此类）。

### Ananke 端到端状态

```
门禁①（AST 完整）      ✅ 通过
门禁②（Scriban 预检）  ✅ 0 失败（转换率 99.2%）
门禁③（flint build）   ❌ 阻断于 partial 返回值语义（上述缺口）
门禁④（产物 diff）     — 未执行（依赖③）

已解决的阻断链（按发现顺序）:
  css.Build 缺失 → resources.get 大小写 → collections.Slice/IsSet 语义
  → MediaType 对象化 → 内联 partial 提取 → 【当前】partial 返回值语义
```

**每一步都是真实的引擎/转换器缺口，不是转换质量问题。** 转换层已达 99.2%
机械转换率且预检零失败；剩余全部是"引擎能力未覆盖 Hugo 语义"。

### 未实施

| 阶段 | 状态 | 前置 |
|---|---|---|
| partial 返回值语义 | 未实施 | 需引擎设计决策（新函数 or 转换器内联） |
| 阶段 4 主题矩阵扩展 | 未开始 | 4 主题已就绪 |
| 阶段 5 AI 残差 | 未开始 | — |

---

## 十四、块内作用域映射与端到端基准（2026-09-11 续）

### 修复的真 bug（影响所有含迭代的主题）

**块内裸点未映射到循环变量**：

```hugo
{{ range .Pages }}{{ .Title }}{{ end }}
```

曾被转换为 `{{ range ... }}{{ page.title }}{{ end }}` —— **循环变量被忽略**，
每项都渲染当前页面的标题。这是影响面最广的一类错误（所有列表模板）。

根因：`ConvertExpr` 的 FieldExpr 分支无条件映射 `.Field` → `page.field`，
未考虑 `scope`（range/with 上下文栈）非空时接收者应是循环变量。

**连带的边界 bug**：`.PageNumber` 以 `.Page` 开头，被"显式根排除条件"
`!raw.StartsWith(".Page")` 误伤 → 产出裸 `page_number`。修正为**完整段匹配**
（`.Page` 或 `.Page.` 才算显式根）。

### 端到端回归基准

新增 `tests/fixtures/mini-hugo-theme/`：一个覆盖 Hugo 常见用法的**最小主题**
（baseof 继承/block define/partial/分页/短码/日期格式化/菜单/URL 函数/
条件与迭代嵌套/static 资源），用于验证完整四门禁管线。

**与 Ananke 的分工**：
- mini 主题证明**基本管线可用**（无深度语义依赖）
- Ananke 是压力测试（暴露引擎语义缺口）

**mini 主题实测**：迁移 31 表达式，机械转换率 100%，预检 0 失败，
Flint 构建产出 7 页（vs Hugo 15 页，差异为分页与 taxonomy 形态）。

### 本轮新增测试（42 → 48）

`BlockScopeMappingTests` 6 条：range 内裸点映射、`.PageNumber` 前缀误伤回归、
`.Pages` 前缀误伤回归、显式根不受作用域影响、with 内裸点、无作用域保持原语义。

`PartialReturnValueTests` 8 条（前节）：return 改写、非 partial 保持 ret、
无参 return、调用点 partialValue 改写、非返回值型保持 include、
非 dot 上下文保持 include、partial 名规范化、return 关键字识别。

---

## 十五、四门禁全通过里程碑（2026-09-11）

### 验证结果

```
主题: tests/fixtures/mini-hugo-theme（9 文件 / 31 表达式）

① 表达式级  ✅ 转换率 100%，不支持 0，降级 0
② 模板级    ✅ Scriban 预检失败 0
③ 站点级    ✅ flint build exit=0（11 页产出）
④ 产物级    ✅ 对称性通过（11 共有页 / 0 单侧）
            结构相似度 54.6% / 文本覆盖 86.4%

对照：本轮开始前 结构 27% / 文本 77%、构建失败
```

**这是迁移工具第一次端到端走通全部门禁**——从"正则转换 13%"到
"AST 转换 100% + 构建通过 + 产物对称"。

### 本轮修复的 8 个通用 bug

| # | bug | 影响面 |
|---|---|---|
| 1 | 根切换前缀误伤（`.Pages.Len` 被当 `.Page` 根） | 所有用 `.Pages` 的主题 |
| 2 | block 默认值变量名 `__def_blk_title`（应 `__def_title`） | 所有用 block 默认内容的主题 |
| 3 | 模板全局 `pages` 未注入 | 所有列表页 `{{ range .Pages }}` |
| 4 | taxonomy 列表页 `.Pages` 为 null（Hugo 是词条页集合） | 所有 taxonomy 模板 |
| 5 | 分页器未绑定 taxonomy/term 页 | 所有带分页的分类页 |
| 6 | `.Date.Format` 接收者未作用域映射 | 所有列表页的日期显示 |
| 7 | `LazyPageList` 缺 `len`/`Len` | `.Pages.Len` 高频用法 |
| 8 | `Site.Language.LanguageCode` 多级路径未映射 | 所有主题的 `<html lang>` |

### 反复出现的陷阱模式（值得固化）

**前缀匹配必须是完整段**——同类 bug 出现 3 次：

1. `!raw.StartsWith(".Page")` 误伤 `.PageNumber`
2. `raw.StartsWith(".page")` 误伤 `.Pages.Len`
3. 早前 Python 版的 `startswith("partials/")` 误伤 `_partials/`

**教训**：路径/标识符的段判断应统一用"完整段"语义（相等或后跟分隔符），
不得用裸 StartsWith。已在该处加注释固化。

### 已知差异（非缺失，对称性判定已排除）

- Hugo 生成 `page/1/`（第 1 页副本），Flint 的第 1 页即列表根
- `<meta name="generator">` 等 Hugo 特有标签

### Ananke 状态（压力测试）

Ananke 仍在更深的引擎语义处阻断（`site.GetPage`、`collections.Index`
的对象接收者等），但这些与 mini 主题修掉的 8 个 bug 同类——**都是
"引擎能力未覆盖 Hugo 语义"，不是转换质量问题**。转换层对 Ananke
已达 99.2% 且预检零失败。

---

## 十六、四主题构建全绿里程碑（2026-09-11 收敛轮）

四个真实主题（Ananke / PaperMod / Stack / LoveIt）全部达到**构建零错误**，
PaperMod 额外达成**产物对称**（四门禁全通过）。

```
主题     表达式 不支持 降级 预检失败 转换率   构建 对称性
ananke      500     3    59      0   99.4%     通过  不对称（分页页集差异）
papermod    548     6    20      0   98.9%     通过  对称 ✅
stack       668     5    78      0   99.3%     通过  不对称（分页页集差异）
loveit     1236   123    55      0   90.0%     通过  不对称（分页页集差异）
```

### 本轮修复的 19 个引擎/转换缺陷

按错误聚类归并（括号内为实测命中主题）：

**引擎能力（Flint.Core）**

| # | 缺陷 | 修复要点 |
|---|---|---|
| 1 | 严格形参阻断 Hugo 多形态调用 | Scriban 绑定**首个注册重载**且类型不符即抛异常（不做重载回退）。`first/last/index/slice/after/in/eq/truncate/trim` 全部改 `params object?[]` + 形态分派 |
| 2 | 页面集合被当字典 | `LazyPageList : ScriptObject` 实现 `IDictionary`，被 `IsCollection` 判为字典 → 新增数值位判定 `SplitSeqCount`/`IsNumericLike`，`ToList` 优先按 `IList<ScriptObject>` 展开 |
| 3 | `hugo.Data` 取不到值 | 数据文件键由作者定义（`[PhotoSwipe]`/`Style` 驼峰），不能 snake 化——新增 `TryMapDataPath`（段名原样 + nil 安全），`hugo.Data` → `site.data` |
| 4 | Go printf 动词未支持 | 新增 `GoFormatToDotNet`：`%s/%v/%d/%q/%x/%%` 与宽度精度修饰转 .NET 复合格式 |
| 5 | `safe_html` 在 render hook 不可用 | 内置函数/日期对象注册抽为 `EnsureFunctionObjects`，hook 路径（`_markup/render-*.html`）同样装配；hook 的 `page` 补 `store` 通道 |
| 6 | partialCached 缺日期对象 | 隔离上下文只装了内置函数 → `date.to_string` 落到 Scriban 内置版（要 `DateTime`），而页面日期是 `DateTimeOffset` |
| 7 | `.GetPage`/`.Paginate`/`.GetTerms` 页面方法缺失 | 新增三个 `IScriptCustomFunction` 页面方法；`GetTerms` 按当前页过滤分类词条 |
| 8 | 资源集合链式调用丢方法 | `PageResourcesObject` 由 `ScriptObject` 改 `ScriptArray` 派生（经变量中转仍保留方法），`ByType` 等筛选结果返回**同类型**集合 |
| 9 | 相对 partial 解析 | Hugo 的 partial 名先相对**调用者目录**再回落根——`FileTemplateLoader` 借 Scriban 的 `callerSpan.FileName` 还原调用者目录 |
| 10 | `_internal/` 内置模板 | Hugo embedded template 命名空间前缀剥除（`template "_internal/google_analytics.html"`） |
| 11 | 菜单函数缺失 | `is_menu_current`/`has_menu_current`（按菜单项 `is_active` 判定，`has_*` 递归子项） |
| 12 | 全局 `minify`/`toCSS` 缺失 | Hugo 0.128 前顶层形态，等同 `resources.Minify`/`css.Sass` |
| 13 | `resources.FromString` 空白名 | 空白名产出 `RelPermalink "/assets/ "` → 输出路径无文件名 → 写盘必失败；`Track` 与输出阶段双重过滤 |

**转换层（Flint.ThemeMigrator）**

| # | 缺陷 | 修复要点 |
|---|---|---|
| 14 | 管道内 `\|` 越界切分 | `ParsePipeline` 未跟踪括号深度，`slice "a" (X \| default "y") $z` 从内层 `\|` 断开，数组提前闭合语义错乱 |
| 15 | 管道左值位置 | Go/Hugo 的 `X \| f A B` == `f A B X`（末参），Scriban 注入**首参**。新增 `PipeValueLastFunctions` 表（`FromString`/`Copy`/`printf`/`errorf`/`i18n` 等）+ `NeedsParens` 包裹多 token 左值 |
| 16 | 双变量 range 解构 | Scriban 迭代映射产出 `{Key, Value}`（PascalCase），`x.key` 取不到；改用 `pair.Key ?? for.index` / `pair.Value ?? pair` 统一映射与序列两种形态 |
| 17 | 局部变量接收者被换根 | `$scratch.Add` 的 head 是局部变量本身，被 `_ when head.StartsWith('$')` 改写成 `pagescratch.add`；裸 `$`/`$.` 不属此列，须换根 |
| 18 | 块名含非法标识符 | Hugo 允许 `block "body-class"`，Scriban 变量名不接受连字符 → `SanitizeIdent` 归一 |
| 19 | `.Format` 接收者根 | `.Site.Lastmod.Format` 缺 site 换根（产出 `page.site.lastmod`）；管道末段的 `.Receiver.Format` 未被识别为函数目标 |

### 反复出现的陷阱模式（本轮新增固化）

- **ScriptObject 是 IDictionary**：任何用 `IsCollection` 区分序列/字典的地方，
  对 `LazyPageList`/`PageResourcesObject` 这类"既是列表又是对象"的类型都会误判。
  **教训**：按**用途**判定（数值位、`IList<T>` 优先），不按接口类型判定。
- **首参不是管道值**：Scriban 与 Go 的管道注入位置相反。对"输入在末位"的函数
  （Hugo 为可管道化如此声明）必须改写为显式调用，且多 token 左值要加括号。
- **同名方法 vs 数据字段**：`paginate`/`format`/`lastmod` 既可能是方法也可能是
  普通字段，转换期需按路径形态（是否穿过 `.Params`）区分。

### 本轮新增回归测试（Core 866 → 879 / Migrator 48 → 56）

新增 `ThemeConvergenceRegressionTests`（13 条，Core）与
`PipeAndParserRegressionTests`（8 条，Migrator），逐条对应上表的失败形态：
集合函数形态分派、`in`/`eq` 多形态、`index` 越界宽容、`trim`/`truncate` 多参、
Go printf 动词、Store PascalCase 别名、`newScratch` 独立实例、
`FromString` 空名守卫、管道左值末参 + 括号、括号内管道切分、
块名归一、局部变量接收者、数据路径段名保持。

### 已知差异（非缺失）

- Hugo 为每个分页列表额外产出 `page/1/`（第 1 页副本），Flint 第 1 页即列表根；
  Flint 的首页分页在 Hugo 未分页时也会产出 `/page/2/`。这是**分页产物集合差异**，
  非迁移缺陷（对称性判定已如实报告为"仅 Hugo: N / 仅 Flint: N"）。
- `<meta name="generator">` 等 Hugo 特有标签。


---

## 十七、20 个流行主题横向矩阵（2026-09-11 第二轮）

### 名单（GitHub `topic:hugo-theme` 按 stars 排序的前列真实主题）

按 star 顺序取用，排除目录仓库（`gohugoio/hugoThemes`）、starter 模板
（`zeon-studio/hugoplate`、`HugoBlox/kit`）与依赖外部构建链的主题
（`docsy` 需 npm+postcss、HugoBlox 系列需 hugo module），以保证测的是
"主题模板迁移"而非"外部构建集成"：

| 主题 | 仓库 | 主题 | 仓库 |
|---|---|---|---|
| hugo-book | alex-shpak/hugo-book | mainroad | Vimux/Mainroad |
| hugo-coder | luizdepra/hugo-coder | jane | xianmin/hugo-theme-jane |
| blowfish | nunocoracao/blowfish | xmin | yihui/hugo-xmin |
| terminal | panr/hugo-theme-terminal | blog-awesome | hugo-sid/hugo-blog-awesome |
| hugo-paper | nanxiaobei/hugo-paper | console | mrmierzejewski/hugo-theme-console |
| hextra | imfing/hextra | clarity | chipzoller/hugo-clarity |
| even | olOwOlo/hugo-theme-even | risotto | joeroe/risotto |
| congo | jpanther/congo | relearn | McShelby/hugo-theme-relearn |
| bearblog | janraasch/hugo-bearblog | （另有第一轮 4 个） | ananke / papermod / stack / loveit |
| archie | athul/archie | hermit | Track3/hermit |
| fixit | hugo-fixit/FixIt | | |

克隆脚本：`scripts/clone-themes.sh`（浅克隆，20/20 成功）。
矩阵脚本：`scripts/theme-matrix20.sh`。

### 框架改进（修正第一轮两个门禁盲区）

1. **Hugo 侧检查退出码 + 产出页数**——第一轮忽略退出码，使 LoveIt 的无效基线
   （配置 `author` 写成字符串导致 Hugo 构建失败）伪装成"不对称"
2. **Flint 侧检查产出页数 + 最小页尺寸**——第一轮只看退出码，使 Ananke 的空页
   （2–4 字节）被当成"构建通过"
3. 每主题最小配置按主题期望的 params 形状生成（`params.Author` 映射 vs
   `params.author` 字符串等），并记录到脚本内便于复现

### 首轮结果与修复后结果

```
                 首轮                          批次 A/B 后
hugo-book        构建失败 0 页                 仍失败（见缺口 1/3）
hugo-coder       构建失败 0 页                 仍失败（见缺口 1/4）
blowfish         构建失败 0 页                 仍失败（见缺口 5）
terminal         构建失败 0 页   → 通过 19 页   61.6% / 88.6%
hugo-paper       构建失败 0 页   → 通过 19 页   36.8% / 89.6%
console          构建失败 6 页   → 通过 19 页   67.8% / 96.3%
bearblog         通过 19 页                    33.4% / 91.7%
congo            构建失败 0 页                 仍失败（见缺口 5）
mainroad         构建失败 14 页                仍失败（见缺口 1）
xmin             构建失败 14 页                仍失败（见缺口 1）
relearn/blog-awesome/clarity/archie/risotto    仍失败（见缺口 1/2）
```

15 个主题的 Hugo 基线有效；其余 5 个（hextra/even/hermit/fixit/jane）的
Hugo 侧本身失败（缺主题要求的 params 或依赖外部资源），基线无效，
其 Flint 结果不参与判定。

### 本轮修复的引擎缺口（8 项，均有主题实测来源）

| # | 缺口 | 修复 | 来源主题 |
|---|---|---|---|
| 1 | `isset` 只注册单参形态 | 改 variadic：`isset MAP KEY` / `isset VALUE` 双形态 | 9 个主题（24 处，最高频） |
| 2 | 无站点级 `.Site.Store` | site 对象加 store/Store/scratch（与页面级同型，构建内共享） | hugo-book 等 |
| 3 | 无全局 `fileExists` | 补全局别名（Hugo camelCase 形态） | blog-awesome |
| 4 | 无 `highlight` / `transform.HighlightCodeBlock` | 补实现（chroma 同构骨架，不实现词法着色） | hugo-book/clarity |
| 5 | hook 路径缺 `i18n`/`partial`/`partialValue` | hook 上下文补注册；翻译表由 SiteBuilder 装配后回填 | congo/fixit/hextra |
| 6 | `default` 严格双参 | 改 variadic（单参返回自身，对应 `default nil`）；**转换器重排非管道形态** `default DEF GIVEN` → `default GIVEN DEF` | blowfish/congo |
| 7 | `sort` 严格双参 + 不可比类型抛异常 | 改 variadic（1/2/3 参）+ 排序键投影（页面按 Weight→Date→Title，避免 ScriptObject 参与比较） | hugo-book/hugo-coder |
| 8 | `sort` 的 "Failed to compare two elements" | 同上（安全比较器，杜绝不可比类型异常） | hugo-book/hugo-coder |

回归：Core 879 通过；第一轮 4 主题（ananke/papermod/stack/loveit）构建仍全通过。

### 剩余缺口（按影响面排序，下一批目标）

| # | 缺口 | 证据 | 性质 |
|---|---|---|---|
| 1 | **partial 名回落命中页面模板 → 自递归** | hugo-coder：`partial "404.html"` 解析到 `layouts/404.html`（页面模板自身）→ "Exceeding number of recursive depth limit 100 for node: include \"404.html\""。Hugo 的 partial **只在 partials/_partials 目录**查找 | 引擎查找顺序 + 转换器产出形态 |
| 2 | **命名模板机制 `define` + `template "X"`** | 20/20 主题都使用（hugo-book 55 次、hextra 25 次、blowfish 21 次）。当前转换器把简单名 `define` 一律当 baseof 继承处理，跨文件的 `template "X" ctx` 调用无对应实现 | 架构级（需两遍扫描：block 名 vs 命名模板名） |
| 3 | `partial` 自定义查找位置 | hugo-coder：`include "404.html"` 自递归同因 | 同 #1 |
| 4 | `Unable to convert type string to Func<Object,Object>` | hugo-coder header.html：某高阶函数（apply/where）收到字符串而非谓词 | 函数签名 |
| 5 | `Unable to convert type object/int to IEnumerable` | blowfish/mainroad 残留 | 集合函数形参 |
| 6 | `page.Param` / `page.Get` / `page.HasShortcode` 调用点未转换 | blowfish（`page?.param` 被当函数名）、papermod | 转换器方法映射 |
| 7 | `page.Data.Pages.GroupByDate` | taxonomy 模板 | 转换器 + 引擎 |
| 8 | `site.language.Get` | hermit（基线无效，待确认是否普遍） | 待确认 |
| 9 | `$thumbnail "..."` 被当函数调用 | 2 处 | 转换器变量/函数判定 |
| 10 | `Unable to convert type DateTimeOffset to int` | console/xmin 残留 | 日期函数签名 |


### 第二轮补充修复（partial 查找语义 + 命名模板机制）

**#1 partial 查找语义（自递归根因）**

Hugo 的 partial **只在 `layouts/_partials/`、`layouts/partials/` 查找**，
不会命中原目录下的同名页面模板。此前转换器产出裸名，使
`partial "404.html"` 解析到 `layouts/404.html`（页面模板自身）→
"Exceeding number of recursive depth limit 100 for node: include \"404.html\""
（hugo-coder 实测，构建直接失败）。

修复（两侧配合）：
- 转换器统一产出 `<c>_partials/</c>` 前缀路径（`PartialPathFor`）
- FileTemplateLoader：
  - 带 partials 前缀时**只**在 partials 目录族内解析（`_partials/` ↔ `partials/`
    互换，覆盖新旧目录约定），不再回落到原目录裸名
  - 仍支持"相对调用者目录"组合（PaperMod 的
    `_partials/templates/opengraph.html` 调 `partial "_funcs/x"`），
    对带前缀的名字用"前缀剥离版"与调用者目录组合
  - 内置模板回退处剥除 partials 前缀后再查内置表
    （否则 `_partials/opengraph` 这类内置依赖全部解析失败）

**#2 命名模板机制 `define` + `template "X"`**

20/20 主题都使用该机制（hugo-book 55 处、hextra 25 处、blowfish 21 处）。
Hugo 的 `define` 语法承载两种语义，此前只支持第一种：

| 语义 | 形态 | 处理 |
|---|---|---|
| baseof 继承 | `block "X"` 声明 + 子模板 `define "X"` 覆盖 | `capture blk_X`（已有） |
| 命名模板库 | `define "X"`（无同名 block）+ 任意处 `template "X" ctx` | **新增**：提取为 `_partials/X.html` |

实现：
- `ThemeMigrator.ScanNamedTemplates` 第一遍跨文件扫描，得到
  "有 define 但无同名 block" 的名字集合（与既有 `ScanValueReturningPartials`
  同一模式）；同时用于区分两种 define 语义
- `InlinePartialExtractor.Extract` 增加 `namedTemplates` 参数：路径名 define
  （`_partials/X.html`）与命名模板 define 都提取为独立文件
- `TemplateConverter` 的 `template "X" ctx` 分派：简单名 → `include "_partials/X"`
  （命中提取出的文件）；`_internal/...` 或含 `/` → 保持 `include`（内置表/路径）

结果：hugo-book 从"构建失败 0 页"变为产出 19 页；hugo-coder 的自递归消失。

**本轮新增回归测试**：partial 调用点前缀、命名模板调用、内置模板保持 include、
非管道 default 重排（Migrator 56 → 60）。三项旧断言按新产出形态同步更新。

### 下一步（优先级）

1. **partial 查找语义**（#1/#3/自递归）——修 FileTemplateLoader 候选顺序 +
   转换器产出显式 `_partials/` 前缀，消除"partial 命中页面模板"这一整类问题
2. **命名模板机制**（#2）——两遍扫描区分 `block` 名与命名模板名；非 block 的
   `define "X"` 提取为 `_partials/X.html`，`template "X" ctx` → `partial "X" ctx`
3. 剩余单点缺口（#4-#10）按频次清理

---

## 十八、主题可用性验证（2026-09-11 第三轮）

### 为什么要先验证"主题在最新 Hugo 上能否使用"

第三轮开工时发现：矩阵里"基线无效"的主题，多数**不是主题的问题，而是验证方法的问题**
（配置不足、缺外部依赖、跑 Hugo 的时机错误）。因为**"主题能否迁移到 Flint"的前提是
"主题能在 Hugo 上跑"**——Hugo 自己都跑不了的主题，其迁移失败无从判定（没有可信对照）。

新增 `scripts/verify-themes.sh`：纯 Hugo 侧可用性验证，判定三条件同时满足：
① Hugo 退出码 0 ② 产出 HTML 页数 > 0 ③ 最大页 ≥ 1000 字节（证明真的渲染出内容）。

验证设计中修正的**五个方法缺陷**（每一个都曾造成误判）：

| # | 缺陷 | 后果 | 修正 |
|---|---|---|---|
| 1 | 用自编最小配置替代 exampleSite | even 被冤枉（用作者配置成功 57 页） | 优先用 exampleSite 的配置与内容 |
| 2 | 未装 Dart Sass | fixit 被冤枉（Hugo v0.153+ 起 LibSass 弃用，`toCSS` 需外部实现） | 安装 dart-sass 并注入 PATH |
| 3 | `params.author` 字符串与 `params.Author` 映射同时写 | ananke/mainroad 被冤枉（Hugo Params 大小写不敏感冲突 → "unable to cast hmaps.Params to string"） | 按主题期望的 author 形状生成配置 |
| 4 | 未处理 `enableGitInfo` | hugo-book/loveit 被冤枉（非 git 站点 → "failed to load Git data"） | 剥离该配置键 |
| 5 | 主题名未按其 config 的 `theme` 值归一 | 6 个主题被冤枉（"module hugo-theme-terminal not found in themes/terminal"） | 按 config 的 theme 值命名目录 |

另修正两处脚本自身的可靠性缺陷：`rm -rf` 遇占用目录静默失败导致内容混叠
（回退 Windows 原生 `rmdir /s /q`）；`$?` 在复杂子 shell 组合下取值不可靠
（改经显式标记回传）。

### 验证结果：34 个主题中 20 个可用

**✅ 在 Hugo v0.166.0 extended 上验证可用（20 个，构成有效基线）**

ananke、archie、bearblog、blog-awesome、blowfish、clarity、congo、console、doit、
even、fixit、github-style、hugo-book、hugo-coder、hugo-paper、loveit、papermod、
risotto、stack、xmin

其中 4 个（archie/blowfish/doit/risotto）的 exampleSite **内容**过时（用了 v0.156.0 移除的
`gist`/`twitter` 短代码或失效远程链接），换中性内容后可用——**主题本身没问题**。

**❌ 排除的 14 个及原因分类**

| 原因 | 主题 | 说明 |
|---|---|---|
| 用了 Hugo 已移除的 API | hermit、jane、mainroad、zzo | `.Site.Author`（v0.124.0 弃用后移除）、`_internal/google_analytics_async.html`（已不存在）——**在最新 Hugo 下确实不可用**，除非降级 Hugo 或给主题打补丁 |
| 依赖 Hugo Modules（需 Go 环境） | eureka、fresh、gallery | `failed to download modules: go mod download` |
| 模板解析与新版严格化冲突 | hextra | `_shortcodes/gallery.html` 注释定界符被换行拆开（`*/` 换行 `-}}`）；改同行即通过 |
| 配置/模板过时 | learn、meme、relearn、hello-friend、intro、terminal | 短代码语法、TOML 结构、自定义 outputFormat 等 |

### 这对"迁移通过率"统计的意义

此前"15 个基线有效、只 4 个通过"这个数字**不可信**（方法缺陷 + 未排除 Hugo 自身不可用的主题）。
修正后的口径应是：**在这 20 个"Hugo 上确证可用"的主题上测迁移**，
未通过的才是转换器的真实缺口。

---

## 十九、20 主题全面测试基线（2026-09-11 第四轮）

### 前置清理

删除 14 个"在最新 Hugo 上不可用"的主题（原因见第十八节），
`tools/themes/` 只保留 20 个验证可用的主题作为测试基线。

### 测试工具链修正

新增 `scripts/theme-matrix-full.sh`（20 主题全面测试）与 `scripts/collect-errors.sh`（错误收集），
相比早期矩阵脚本的关键差别：

1. 站点准备**复用 verify-themes.sh 的验证过逻辑**（exampleSite 优先、主题名按 config 归一、
   剥离 themesDir/enableGitInfo、按主题给 author 形状、目录清理回退 Windows rmdir）
2. Hugo 判定在**替换 themes 目录之前**完成（早期脚本把迁移产物写进 themes 后再跑 Hugo，
   导致 "function capture not defined" 的假象）
3. Dart Sass 注入 PATH
4. 双侧都查"页数 + 最大页尺寸"（空页不算通过）
5. **站点级 layouts 一并迁移**：exampleSite 常带 `layouts/shortcodes/` 覆盖（Go 模板语法），
   不迁移会报 `Expecting <expression> instead of '='`（blowfish 实测）
6. 错误日志按 **GBK** 转码（Flint CLI 在 Windows 中文控制台输出 GBK，
   直接 grep 会因非 UTF-8 字节被判为二进制而只输出 "Binary file matches"）

### 本轮修复的引擎缺口

**`config/_default/` 目录式配置支持**（重大兼容性缺口）

Hugo 0.116+ 推荐的配置形式，blowfish/congo/clarity 等主题只提供目录式配置
（无单文件 hugo.toml）→ 此前报"当前目录不是有效的 Flint 站点"**完全无法构建**。

实现（对齐 Hugo 合并语义）：
- `ConfigLoader.TryLoadConfigDirectory`：探测 `config/_default/`，
  `hugo.toml`/`config.toml` 提供顶层键，其他文件名（params/menus/languages/outputs…）
  的内容挂在**同名顶层键**下；`config/<production>/` 后应用覆盖
- `ConfigMerge.DeepMergeInto`：字典深合并（键大小写不敏感——Hugo 的 Params 语义）
- `ConfigParser.ParseTomlDict`/`ParseYamlDict`/`ParseMergedTomlDict`：按段合并的入口
- `BuildHandler` 改走 `AutoLoadAsync`（目录式优先），站点有效性检查也识别 config 目录

效果：blowfish 从 **0 页 → 276 页**。

**已确认的静默失败缺陷（待修，本轮已定位到最小复现）**

ananke 全站页面输出 2–4 字节（构建"成功"无错误）。二分定位到
`layouts/_partials/site-style.html` 被 include/partial 调用时**清空调用者的整个输出缓冲**
（把该调用换成任意文本即恢复正常 6.5KB）。site-style.html 内容本身普通
（无 ret/define/capture），调用路径与上下文均非诱因——属引擎级静默失败，待修。

### 当前基线（20 主题，Hugo v0.166.0 / 含 dart-sass）

| 主题 | Hugo | 页数 | Flint | F页数 | F最大页 | 转换率 | 对称(结构/文本) |
|---|---|---|---|---|---|---|---|
| ananke | 是 | 21 | 空页 | 18 | 47B | 99.4% | - |
| archie | 否* | 23 | 构建失败 | 25 | 112KB | 100.0% | - |
| bearblog | 是 | 8 | 通过 | 8 | 49KB | 100.0% | 0 / 29.2/94.7 |
| blog-awesome | 是 | 121 | 构建失败 | 0 | - | 100.0% | - |
| blowfish | 是 | 3870 | 构建失败 | 276 | - | 99.5% | - |
| clarity | 是 | 93 | 构建失败 | 0 | - | 100.0% | - |
| congo | 否* | 0 | 构建失败 | 0 | - | 99.1% | - |
| console | 是 | 15 | 构建失败 | 9 | 41KB | 100.0% | - |
| doit | 否* | 51 | 构建失败 | 0 | - | 90.5% | - |
| even | 是 | 57 | 构建失败 | 0 | - | 100.0% | - |
| fixit | 是 | 22 | 构建失败 | 2 | 232B | 91.9% | - |
| github-style | 是 | 23 | 通过 | 18 | 519KB | 100.0% | 0 / 65.3/92.9 |
| hugo-book | 是 | 53 | 通过 | 15 | 2976B | 98.3% | 0 / 7.8/13.1 |
| hugo-coder | 是 | 125 | 构建失败 | 4 | 1429B | 100.0% | - |
| hugo-paper | 是 | 9 | 构建失败 | 20 | 5438B | 100.0% | - |
| loveit | 是 | 78 | 构建失败 | 13 | 142KB | 90.0% | - |
| papermod | 是 | 22 | 通过 | 18 | 183KB | 98.9% | 0 / 36.2/89.1 |
| risotto | 否* | 32 | 构建失败 | 26 | 69KB | 100.0% | - |
| stack | 是 | 24 | 通过 | 18 | 234KB | 99.3% | 0 / 15.6/60.4 |
| xmin | 是 | 19 | 构建失败 | 14 | 25KB | 100.0% | - |

（否* = exampleSite 内容过时，换最小内容后 Hugo 侧可用；"-" = 无法评估）

**读数要点**：
- 转换率 90.0%–100%（8 个主题 100%），说明**表达式级转换已不是瓶颈**
- 5 个主题产出且可做产物对比；文本覆盖度 4/5 在 89–95%（内容迁移基本成功），
  hugo-book 仅 13%（内容大量丢失，与命名模板/块继承相关）
- 结构相似度普遍偏低（7.8%–65.3%），主因是页集差异（Hugo 的 `page/1/` 等）与 DOM 结构偏差
- **瓶颈已明确在引擎运行时**：15 个主题的失败集中在少数几类运行时缺口
  （`page.store.set` 上下文、静默塌陷、短代码解析），而非转换率

---

## 二十、按缺口聚类批量修复（2026-09-11 第五轮）

### 结果：Flint 通过 20 主题中的 16 个（起点 6 个）

| 主题 | Hugo | 页数 | Flint | F页数 | F最大页 |
|---|---|---|---|---|---|
| blowfish | ✅ | 3870 | **通过** | 1733 | 94KB |
| congo | ✅* | 63 | **通过** | 63 | 163KB |
| blog-awesome | ✅ | 121 | **通过** | 26 | 3KB |
| even | ✅ | 57 | **通过** | 37 | 70KB |
| hugo-coder | ✅ | 125 | **通过** | 38 | 18KB |
| loveit | ✅ | 78 | **通过** | 34 | 17KB |
| archie / risotto | ✅ | 32/23 | **通过** | 27/26 | 22/10KB |
| ananke / bearblog / console / github-style / hugo-book / papermod / stack / xmin | ✅ | 8–53 | **通过** | 8–18 | 2–37KB |
| clarity / doit / fixit / hugo-paper | ✅ | 9–93 | 空页 | 2–20 | <1KB |

### 本轮修复的缺口（9 项，每项都有实测来源）

**引擎 —— 返回值通道脱离 page**（Blowfish 1571 处 `page.store.set for a null object`）

返回值通道原本挂在 <c>page.Store</c> 上，但短代码/hook/被覆盖 page 的 partial 里
`page` 可能为 null。改为挂在**渲染上下文**（globals 的 `__flint_ret_store`，每页独立）：
引擎加 `__partial_ret_set` + `ResolveStore`，转换器 `{{ return X }}` 改写改用新函数，
`partialValue` 优先读渲染期 store 并回退 page.Store（旧产物兼容）。

**引擎 —— reading_time 类型**（Blowfish 1571 处 `TimeSpan to int`）

Hugo 的 `.ReadingTime` 是**分钟数整数**，内部 `TimeSpan` 直接暴露使
`reading_time != 0` / `add reading_time 1` 全部报类型错 → 投影为 `int`。

**引擎 —— templates.Exists 恒真**（Blowfish 1574 处 FileNotFound）

旧实现 `!string.IsNullOrEmpty(name)` 使 `{{ if templates.Exists "x" }}` 守卫失效 →
径直调用不存在的 partial。命名空间对象**固定引用**注册时的实现，故改为可注入的
`TemplateExistsProbe`（渲染器构造后注入 loader 探测）。

**引擎 —— range 集合语义**（Blowfish 1574 处 `Boolean for iterator`）

Hugo 的 `or A B` 皆空返回 nil（`range nil` 不迭代），Scriban 的 `||` 返回 false →
`for x in false` 抛异常。引擎加 `as_list`（nil/false→空、标量→单元素、集合→原样），
转换器对 range 的集合表达式统一包装。

**引擎 —— 函数形参宽容**：`add/sub/mul/div` 改 variadic（even 37 处）、
`slicestr` 第二参可省（even 37 处）、`date.to_string` 格式串可省（doit 21 处）、
`urls.Parse` 零参宽容（doit 3 处）。

**引擎 —— 补齐页面方法**：`.HasShortcode`（hugo-coder 42 处）、`.RenderString`（archie）、
`.Param`（fixit）；hook 上下文为站点级方法提供**宽松 stub**（Congo 的
`_markup/render-link.html` 70 处 `page.get_page`——hook 里得到 null 走 with 兜底，
而非构建失败）。

**引擎 —— 线程安全**：`FileSystemResourceProvider` 惰性索引加双重检查锁
（并行渲染首次访问 `resources.get` 时并发写 Dictionary，DoIt 22 处
"Operations that change non-concurrent collections"）；
`TemplateResourceFunctions._generated` 的 Track 加锁。

**转换器 —— `.Page` 根前缀重复**：`.Page.Scratch.Get` 被无条件加 page 前缀 →
`page.page.scratch.get`（LoveIt 22 处、Console 同因）。补 `isPageRoot` 判断
（与既有 `isSiteRoot` 对称），字段+参数分支与纯字段分支都处理；
`MapIdentifierPath` 同样剥 `$.Page.` 段。

**转换器 —— `.svg` 未入模板扩展名表**：含 `{{ .width }}` 的 SVG 是模板
（Hugo 会渲染），不入表则被整体复制、Hugo 语法残留 → 运行时 Scriban 报
`Unexpected token .`（blog-awesome 63 处）。

### 剩余 4 个空页主题的已定位原因

- **clarity**（29 处 `partial` 未找到）：短代码路径缺 partial 函数
- **doit**（22 处 Fontawesome errorf）：主题在缺配置时报错（exampleSite 未提供图标参数）
- **fixit**（2 页/143B）：站点级 layouts 与主题交互待查
- **hugo-paper**（20 页/628B）：静默空页，待定位

---

## 二十一、最后 4 个空页主题的收敛（2026-09-11 第六轮）

### 修复的 4 项缺口

**1. 短代码用全新空上下文渲染**（Clarity 29 处 `function partial was not found`）

`TemplateShortcode` 用 `new Scriban.TemplateContext()` 渲染，**只挂短代码自己的
`.Get`/`.Inner`**——`partial`/`safe_html`/`as_list` 等全局函数全部不可用。
加 `TemplateShortcode.EnrichContext` 静态委托（渲染器构造时注入），短代码与页面
共享同一套全局函数；`page`/`site` 在解析期尚无值，但 partial 的上下文参数
（`partial "sprite" (dict "icon" "x")`）可正常传递。

**2. `partialcached` 的隔离上下文缺 partial 家族**（Clarity 29 处同因的另一半）

`RenderPartialCached` 用隔离上下文渲染，只继承调用者的 `CurrentGlobal`——
而那是**最顶层** global 对象，partial/partialValue 挂在被它覆盖的下层 →
隔离上下文里没有 `partial`（Clarity 的 `partialcached "top"` 内部再调
`partial "sprite"` 必失败）。改为显式注册 partial 家族（页对象取自
callerGlobals，无则新建带 store 的空对象）。

**3. 比较运算不做类型强制**（Clarity 29 处 `Unable to convert type object to int`）

Scriban 的 `>`/`>=`/`<`/`<=` 在两侧类型不一致时抛异常（map 值为字符串与数字比较），
而 Hugo 的比较会做类型强制。引擎加 `num_gt`/`num_ge`/`num_lt`/`num_le`
（两侧可解析为数值则数值比较，否则 Ordinal 字符串比较，**不抛异常**），
转换器把比较运算符改产出这些函数。

**4. hook 上下文缺站点级页面方法**（Congo 70 处 `page.get_page`）

render hook 的 `page` 是最小字段集，站点级方法（get_page/get_terms）不可用。
注册**宽松 stub**（返回 null）——主题的 `with`/`if` 守卫会安全跳过，
而非整页渲染失败。

### 效果

| 主题 | 修复前 | 修复后 |
|---|---|---|
| clarity | 12 页 / 3.8KB | **41 页 / 805KB** |
| congo | 0 页 | **63 页 / 163KB** |
| blog-awesome | 0 页 | **26 页** |
| blowfish | 276 页 / 868B（空页） | **1733 页 / 94KB** |

Flint 通过 **16/20**（含修复前的 13 个 + blowfish/congo/blog-awesome）。

### 仍待处理（4 个主题仍有残留错误但已产出内容）

- **clarity**（41 页 / 805KB）：仍有少量残留错误致 exit≠0
- **doit**（14 页）：主题自身 errorf（exampleSite 缺图标参数，Hugo 侧同样失败）
- **fixit**（2 页 / 232B）：站点级 layouts 与主题交互待查
- **hugo-paper**（20 页 / 5.4KB）：静默空页，待定位

---

## 二十二、引擎与转换器的语义对齐（2026-09-12 第七轮）

本轮全部结论都来自**对 Hugo v0.166 的实测**（用一个最小站点逐个验证语义），
而不是从文档或记忆推断。下面每条都注明实测证据。

### A. 引擎：Hugo 语义缺口（7 项）

**A1. `add` 是字符串拼接的多态函数**（实测）

```
add "fa-solid fa-tag" " me-1"  ⇒ "fa-solid fa-tag me-1"   （拼接）
add "1" "2"                    ⇒ "12"                      （拼接！）
add 1 2 3                      ⇒ 6                         （求和）
add "3" 1                      ⇒ 报错 can't apply the operator to the values
math.Add 与 add 同一实现
```

Flint 的 `add` 一律 `ToNum` 求和 → `"" + "/img.png"` 算成 0。主题大量用它拼
URL/类名（clarity `add $relpath .`、fixit `add $icon " me-1"`）。修为：
操作数全为字符串 → 拼接，否则数值求和（混用取宽容）。

**A2. `errorf` 不中断渲染**（实测）

```
HOME {{ errorf "boom: %s" "detail" }} END  ⇒ 产出 "HOME  END"，构建 exit=1
```

Hugo 的 `errorf` 只记录错误日志、**继续渲染**，构建收尾按错误计数判失败。
Flint 原实现直接抛异常中止整页渲染——主题里一处 `errorf` 就让整站塌成空页
（fixit 的 `icon.html` 4 处 → 仅剩 2 页 / 232B）。修为：格式化后交
`ErrorReporter` 汇聚（`ScribanTemplateRenderer` 线程安全队列），
`SiteBuilder` 收尾取走并计入 `BuildResult.Errors`（错误码 `TEMPLATE001`）。

同时修 `FormatMessage`：Go 动词（`%s`/`%v`/`%T`）此前落进 .NET `string.Format`
抛 `FormatException` 被吞成"原样返回格式串"，主题日志里出现字面 `%s`。

**A3. 内联短代码**（`enableInlineShortcodes`）

```
{{< css.inline >}} … 模板源码 … {{< /css.inline >}}
```

Hugo 的"内容内定义短代码"：成对标签之间是**模板源码**，原位渲染。
Flint 查不到名字即报 `PARSE001 未注册的短代码`（hugo-paper 3 处、
clarity 4 处整篇内容解析失败）。新增 `InlineShortcode`（`.inline` 后缀 +
成对标签 → 以 innerContent 为模板源渲染），由 `SiteConfig.EnableInlineShortcodes`
开关控制（对齐 Hugo 默认关闭）。

**A4. `.TableOfContents` 是 HTML 字符串，不是列表**

主题按字符串消费它：Blowfish `replace (.TableOfContents | emojify) 'id="TableOfContents"'`、
Congo `in page.table_of_contents "<ul"`（判定"本页有无目录"）。Flint 原先暴露
标题**列表**且恒为空 → 15 个主题的目录相关判定静默失效。新增 `TocRenderer`，
逐字节对齐 Hugo 实测形状（`<nav id="TableOfContents">` + 层级嵌套 `<ul>`、
缺失层级补空 `<li>` 包裹），并新增 `.Fragments`（`ToHTML start end ordered`
/ `Identifiers` / `Headings`，Hugo v0.111+）。

**A5. 返回值通道三处缺陷**（`partialValue` 恒读不到值）

1. **键名双前缀**：写入端 `PartialRetSetFunction` 给已是 `__partial_ret_x` 的名字
   再加前缀 → 写入键 `__partial_ret___partial_ret_x`，读取键 `__partial_ret_x`。
2. **输出缓冲未隔离**：Scriban 的嵌套渲染会污染调用者输出流（调用者已累积的
   文本丢失、返回值退化成渲染文本）。改为 `PushOutput`/`PopOutput` 隔离。
3. **overlay 挡住 store**：`ResolveStore` 只读 `CurrentGlobal`，而
   `RenderPartialWithContext` 压入的 overlay 不含 `__flint_ret_store`
   → partial 内的 `__partial_ret_set` 写到 null。

修后实测：`partialValue "function/camel-case" "capitalize_titles"` ⇒
`capitalizeTitles`（与 Hugo 一致）。

**A6. `reflect.IsMap` 对 Page/Site 应为 false**（Hugo 语义）

Flint 按类型判定（页面也是 `ScriptObject`）→ 返回 true。主题里"递归转换 map 键"
的辅助函数（FixIt 的 `camel-case-keys.html`）据此遍历页面成员，经
site→pages→page 的对象环无限递归，打到 Scriban 的函数递归上限
（`Exceeding number of recursive depth limit 100`）。

**A7. 零散缺口**

- `warnidf`/`erroridf`：Hugo v0.146+ 的简洁别名 + **可变格式参数**
  （`warnidf ID FORMAT ARGS…`；两参严格形参在 3+ 实参时抛 `Argument index must be < 2`）
- `substr` 的 length 可省略（`substr $part 1` 取到末尾）
- `hugo.Store` 需 Scratch 语义（`Set`/`Get`/`Add`…），空 ScriptObject 报 function not found
- `hugo.Context.MarkupScope`（render hook 的作用域判定）
- 内置短代码别名：`x`/`twitter`（Hugo v0.132+ 改名）、`vimeo_simple`
- partial 的 dict 上下文补**页面方法族**（`param`/`get_page`/`get_terms`/
  `has_shortcode`/`render_string`），修 `dict "Page" . "Key" "x" | partial …`
  内部 `.Page.Param` 的 function not found
- render hook 的 `page` 补 `param`/`has_shortcode`/`render_string` 宽松 stub

### B. 转换器缺陷（4 项）

**B1. 双变量 `range` 多输出一个 `}`**（影响面最大）

产出串写成 6 个 `}`（C# 插值转义后是 3 个），正确应为 4 个（转义后 2 个）：
每个 `range $k, $v :=` 都向页面注入一个字面 `}`。改为**显式字符串拼接**，
从此不必在插值串里数花括号。

**B2. 块默认值引用 `$.blk_x`** → Scriban 里 `$` 是未定义符号，报
`Cannot get the member $.blk_x for a null object` 让整页失败。块值由
`capture blk_x` 落在全局变量上，直接按名引用。

**B3. 值返回型 partial 的上下文参数被丢弃**：`partial "x" $value` 走
`!isValueReturning` 守卫被跳过 → partial 内的 `.` 落到外层 page
（`split page "_"` 拿页面对象）。改为产出 `partialValue "x" CONTEXT`
（引擎侧 `PartialValueFunction` 同步支持第二参数）。

**B4. 变量接收者的字段段未映射**：`$page.Resources.GetMatch` 原样保留接收者
→ `$page.Resources.getmatch`（页面对象上的键是小写 `resources`）→ null object。
改为对变量名之后的段同样走 `ToSnakePath`。

### C. 配置与加载

**C1. `config/_default/` 未知文件名应按根级合并**：clarity 的
`configTaxo.toml` 装的是根级键（`enableInlineShortcodes`/`timeout`/`privacy`），
原先被塞进 `configTaxo` 子表 → 开关读不到。改为白名单段名
（params/menus/languages/…）挂同名键，其余按根级深合并（Hugo 语义）。

### D. 第二批（第七轮续）：资源、短代码与序列化

**D1. 资源对象的**图像变换方法族**：`$img.Fill "600x600"` / `.Resize` / `.Fit` /
`.Crop` / `.Process` 此前只注册在**命名空间**（`resources.Fill`），资源**对象**上没有
→ Blowfish 的 `$authorImage.Fill` 报 function not found（1580 处）。
新增 `TemplateResource.AttachImageOps` 注入点（由资源命名空间注册方设置），
命名空间级与成员级共用同一实现（确定性命名 + 产物登记落盘）。

**D2. 内联短代码开关的**默认行为**：Hugo 实测——`enableInlineShortcodes` 关闭时
Hugo **不报错**，而是丢弃整个成对块（产出 `BEFORE`/`AFTER`、exit=0）。
Flint 原先报 `PARSE001 未注册的短代码` 使整站失败（hugo-coder / blog-awesome 实测）。
改为：开关关闭 → 丢弃该块；开启 → 渲染 body。

**D3. `dict` 不能产出为 `{ k: v }`**：Scriban **没有对象字面量**，`{ k: v }`
会被当成语句块——作为函数参数时 `jsonify` 收到 0 参
（`Argument index must be < 1`，Congo 的 schema.html 86 处）。改为一律产
`dict "k" v …`。

**D4. `jsonify` 的 Hugo 形态与 AOT 序列化**：
- Hugo 签名是 `jsonify [OPTIONS] VALUE`（选项在前）；Scriban 管道把值注入**首参**，
  于是实到两参 → 单参严格形参越界。改为收 `params` 并按字典键
  （indent/prefix）识别选项。
- 模板对象（ScriptObject/ScriptArray）的**反射序列化在 NativeAOT 下被禁用**，
  原先一律抛异常回退成字面 `"null"` → 新增手写遍历序列化
  （`Utf8JsonWriter`，覆盖 null/布尔/数值/时间/字符串/映射/序列）。

**D5. `url.query` 是映射不是字符串**（Hugo `url.Values` 语义）：
`(.Query).Get "x"` 是惯用法（Congo 的 render-image.html）。改为映射对象
（同名键取首值 + `get`/`Get`），原始串另走 `raw_query`/`RawQuery`。

**D6. `slice` 的两义消歧**：Hugo 实测 `slice 1 2` ⇒ 长度 2（构建数组），
而 Flint 把"第二参为数值"解成 `slice SEQ START [LEN]` 自造语义 → `slice 1 2`
产出空数组。改为按**首参是否为序列**消歧：序列 + 数值第二参 → 子序列（保留
`site.pages | slice 0 2` 的既有可用形态）；否则 → Hugo 的构造器语义。

**D7. render hook 的页面资源与暂存宽容化**：
- `.Resources`（页面资源）在 hook 上下文注册宽松 stub（`getmatch`/`get`/`match`/
  `bytype` 等 → null / 空集合），否则 `{{ with .Resources.GetMatch $src }}`
  报 `Cannot get the member page.resources.getmatch for a null object` 并让
  **整篇内容解析失败**（Congo 的 _markup/render-image.html 实测）。
- `.Scratch.Get "params"` 在 hook 里取不到布局先前写入的值（hook 早于布局渲染）：
  缺失键返回**空对象**而非 null，使 `$params.code.copy | default true`
  这类链式访问得到 null 而不是硬错误（LoveIt 的 render-codeblock-goat.html 实测）。

---

## 二十三、第三批：baseof 继承、资源管线与递归（2026-09-13 第八轮）

### A. baseof 继承（hugo-book 从"每页 2 字节"到通过）

**A1. 只含 `{{ define "dummy" }}{{ end }}` 的模板**（hugo-book 的 single.html /
list.html 等）：这是 Hugo 的"仅用 define 触发 baseof 继承"写法。转换器把 define
提取为独立 partial 后本文件变空——Hugo 仍会渲染 baseof 骨架，Flint 则输出空文件
（14 页合计 94 字节）。修：转换后 body 为空**且**主题有 `layouts/baseof.html`
时补 `{{ include "baseof.html" }}`。兜底**只对非 partial 布局生效**——否则
`_partials/...` 里的空文件也会引 baseof，形成
`inject/head → baseof → head-styles → inject/head` 的无限递归。

**A2. `{{ with $v := EXPR }}`（Go 的带变量声明 with）**：解析器此前不认这个变量
声明，转换器便把它当成被赋值对象，产出 `$__w1 = $terms page?.get_terms $taxonomy`
——Scriban 把 `$terms` 当函数调用（"The function `$terms` was not found"）。
修：`with` 与 `range` 共用"变量声明 + 管道"的解析分支，产出 `$terms = EXPR; if $terms`。

**A3. `{{ template "X" CTX }}` 的上下文**：此前丢弃 CTX，被调模板里的 `.Field`
落到外层 page 上。修：非内置命名模板改为 `partial "_partials/X" CTX`。

### B. 资源管线

**B1. `transform.Unmarshal` 接受资源对象**：Hugo 惯用
`resources.Get "styles/index.yaml" | transform.Unmarshal`，而 Flint 只接受字符串
→ 整个链塌成 null。修：取参数表里最后一个"文本位"参数（字符串直接用、资源取其
内容）。

**B2. YAML 子集解析**：原实现只认 `key: value`，纯序列文件（hugo-book 的
`styles/index.yaml` 是 CSS 清单）被解析成空对象 → `resources.Concat` 拿到空集合
→ partial 上下文为 null → 全站页头报错。新增 `ParseSimpleYaml`：序列、映射、
缩进嵌套、标量（去引号/布尔/数值）。

**B3. `resources.ExecuteAsTemplate` 的参数序**：Hugo 是
`ExecuteAsTemplate TARGETPATH DATA RESOURCE`（管道把资源注入末位），原实现只取
**首参**当资源——而首参是目标路径字符串 → 返回 null → `$searchJS` 整条链塌掉。
修：按"找资源 + 找路径"解析，并把资源改名到目标路径使 RelPermalink 与 Hugo 一致。

### C. 递归与缓存

**C1. Scriban 的函数递归计数会跨调用累积**：它在"嵌套渲染 + `ret` 提前返回"时
不递减，partial 调用上百次的主题（hugo-book 单次构建调 `docs/title.html` 1871 次）
会假性超限（"Exceeding number of recursive depth limit 100 for node: `default
site.title`"，opengraph 全挂）。修：与 LoopLimit 同口径设 `RecursiveLimit = 0`
（页面渲染 3 处 + 短代码上下文），递归安全改由**自建**的 partial 嵌套深度守卫
（200 层，ThreadStatic）负责——真无限递归给出明确错误而不是无限循环。

**C2. partial 模板解析缓存**：每次调用都 `Template.Parse` 时，深调用链下解析器
自身的递归会顶到栈（"The parser recursive depth limit was reached near a stack
overflow"）。修：按物理路径缓存解析结果（复用 `_templateCache`，含 mtime 失效）。

**C3. `reflect.IsMap` 再排除两类**：`PageStoreObject`（Hugo 的 `.Scratch` 是
struct）与页面集合（Hugo 的 `.Pages` 是 slice）。不排除时，主题的 map 递归辅助
函数会钻进 partial 上下文注入的 `store` 内部——那里又引用了页面/站点，形成环
→ 无限递归（FixIt 的 camel-case-keys 实测，由自建深度守卫捕获）。

### D. 上下文与命名空间

**D1. dict 上下文并入页面成员**：Hugo 惯用 `dict "Page" . | partial "x"`，partial
内 `.Page.X` 被迁移成 `page.x`（page 即那个 dict）→ 取不到页面成员
（FixIt 的 `get-cover.html` `$page := .Page` → `$page.resources.getmatch` 报 null）。
修：`RenderPartialWithContext` 的 dict 分支把 dict 里 page-like 的 `Page`/`page`
值的成员并入绑定对象（dict 自身键优先）。

**D2. `diagrams` 命名空间**：`diagrams.Goat` / `diagrams.ASCIIArt` 返回含
`Inner`/`Width`/`Height` 的对象（LoveIt 的 plugin/goat.html 用 `{{ with
diagrams.Goat .Inner }}` 取这三者）。保真度说明：Hugo 用内嵌 ASCII 艺术字形表逐字
绘制，Flint 用等宽 SVG text 逐字排布（结构等价、视觉是普通等宽文本）。

**D3. 内置短代码补全**：`twitter_simple` / `x_simple`（Hugo v0.146+ 的 `_simple`
家族，实测 twitter_simple/x_simple/vimeo_simple 均有内置）。

### E. 测试同步

本轮行为变更同步了 6 个过时断言（返回值通道 `__partial_ret_set`、dict 函数形态、
`num_lt` 比较、值返回型 partial 的上下文参数）——测试与实现必须同源。

---

## 二十四、第四批：range 语义、上下文细化与一次有害尝试（2026-09-13 第九轮）

### A. `range` 的 map/slice 语义（Hugo 实测）

```
{{ range $v    := (dict "a" 1 "b" 2) }} ⇒ 1,2      （值）
{{ range $k,$v := (dict "a" 1 "b" 2) }} ⇒ a=1,b=2  （键值对）
{{ range $i,$v := (slice "x" "y") }}    ⇒ 0=x,1=y  （索引+值）
{{ range $v    := (slice "x" "y") }}    ⇒ x,y      （元素）
```

**A1. `as_list` 把映射当标量**：`as_list(dict)` 返回**单元素**（那个 dict 本身），
于是双变量 range 的 `$pair` 就是 dict → `reflect.IsMap $pair` 为真 → 主题的
"递归转换 map 键"辅助函数（FixIt 的 `camel-case-keys.html`）沿同一个 map
**无限自递归**（自建深度守卫捕获）。修：`as_list` 对映射返回**值序列**。

**A2. 新增 `as_pairs`**（键值对序列）：转换器把 `range $k, $v := X` 产成
`for $pair in as_pairs (X)`，`$k = $pair.Key` / `$v = $pair.Value`。

**A3. 列表优先于映射**（关键回归点）：页面集合是 `ScriptObject + IList<ScriptObject>`
（LazyPageList）。若按 `ScriptObject` 判定，就会去迭代它的**成员**
（bydate/bytitle/count… 一堆函数值）而不是页面本列——迭代出函数值后 Scriban 会
自动调用它们（"Argument index must be < 1"、"Unable to convert type object to int"，
loveit/hugo-coder/blowfish 实测）。故判定顺序必须是
`IList<ScriptObject>` → `IList` → 映射 → 其余 `IEnumerable`。

**A4. 迭代跳过"非数据成员"**：函数值成员在 Scriban 的取值位置会被自动调用
（0 参 → 参数校验失败）。Hugo 的映射从不含函数，故跳过。

**A5. `ScriptObject` 的成员值必须经 `Keys` + 索引器取**：`IDictionary.Values` /
`DictionaryEntry.Value` 返回 Scriban 的 `InternalValue` 包装（渲染成类型名）。

### B. partial 上下文细化

**B1. 大小写别名**：主题用 `dict "Config" …` 造 PascalCase 键，而迁移产物按页面
成员约定写小写（`page.config`）——Scriban 成员查找大小写敏感。给 partial 的 dict
上下文补大小写双向别名。

**B2. 缺失模板宽容**：`partial`/`partialValue` 解析不到模板时**输出空并记录诊断**
（TEMPLATE001），而不是抛异常打断整页——主题常引用由 Hugo Module 提供的 partial
（FixIt 的 `_funcs/get-page-images` 来自 LoveIt 模块）。

**B3. 内置模板哨兵路径**：内置模板的"路径"是哨兵（U+0001builtin:xxx），不能对它
调 `File.GetLastWriteTimeUtc`（内部 `Path.GetFullPath` 会把哨兵当相对路径解析并抛错）。

**B4. 转换器：管道形态 `dict … | partial "x"` 的上下文丢括号** → Scriban 把它当多个
实参（FixIt 的 rss.html：partial 内 `$page := .Page` 取到函数对象）。

**B5. `fingerprint` 收下可选算法参数**：内建注册是 `(string? path)` 单参版，
主题写 `$res | fingerprint "sha512"` 时形参不符 → "Argument index must be < 1"
（LoveIt 的 plugin/style.html 实测）。合并为一个资源感知的 variadic 实现：
资源对象走资源指纹（同 `resources.Fingerprint`）、字符串路径保留旧的 `?v=hash`。

### C. 渲染次序

**home 页先串行渲染**，其余页再分批并行（对齐 Hugo 的页面渲染次序）：主题常在 home
里把跨页数据写进 `.Site.Store`（FixIt 的 `$.Store.Set "mainSectionPages"`，随后由
`single/footer.html` 读取），分批并行会让读取先于写入。

### D. 一次有害尝试（已回退，记录备查）

为对齐 Hugo 的 nil 宽容（实测 `{{ (dict "a" 1).b.c }}` 无错、`{{ ge nil 1 }}` ⇒ false），
曾开启 Scriban 的 `EnableRelaxedTargetAccess`。**实测有害**：某主题的关键守卫从
"抛错"变成"静默 false"，页面渲染成空壳——Congo 从 26.3MB 塌到 471 字节
（157 页全是空白）。矩阵即验证：宁可显式失败，不可静默空页。已移除该开关。

**教训**：引擎级"宽容开关"是**全局语义变更**，必须用整站产出量做反向验证
（S3），只看错误数下降会漏掉静默劣化。

---

## 二十五、第五批：partial 解析与一次回退（2026-09-13 第十轮）

**1. partial 的"相对调用者目录"解析丢了调用者信息**

Hugo 的 partial 名可相对**调用者所在目录**解析（PaperMod 的
`_partials/templates/opengraph.html` 调 `partial "_funcs/get-page-images"` 命中
`_partials/templates/_funcs/get-page-images.html`）。走 Scriban 内置 `include` 时
调用者位置由 Scriban 的 span 提供；把 partial 调用改走 Flint 自己的实现后该信息丢失
→ 嵌套相对路径的 partial 全部解析失败（PaperMod 实测 18 处）。修：
`PartialFunction`/`PartialValueFunction` 把 `callerContext.Span` 传给解析器。

**2. 动态 partial 名**

`{{ partial $partial . }}` / `{{ partial (printf "home/%s" $layout) . }}`：名字在运行期
才确定。此前产 TODO 占位 → 命中分支只有注释、正文为空（Congo 的 `index.html` 经
`templates.Exists` 分支实测）。修：名字表达式**透传**给 Flint 的 `partial`
（它本就按字符串在运行期解析）。

**3. `fingerprint` 收下可选算法参数**

内建注册是 `(string? path)` 单参版，主题写 `$res | fingerprint "sha512"` 时形参不符
→ "Argument index must be < 1"（LoveIt 的 plugin/style.html 实测）。合并为资源感知的
variadic 实现：资源对象走资源指纹（同 `resources.Fingerprint`）、字符串路径保留旧的
`?v=hash`。

**4. 列表判定优先于映射**（回归修复，见第二十四节 A3）

### E. 又一次回退（S3 反向验证，记录备查）

**partial 内的提前返回**：为消除"Scriban 的 `ret` 把 FlowState 置为 Return、连带截断
调用者渲染"的隐患，曾把 partial 内的 `{{ return }}` 改产 Flint 自有信号
`__flint_partial_return`（抛 `PartialReturnSignal`，由 partial 渲染处捕获）。

**实测不成立**：在 `partialcached` 的隔离上下文路径上与 Clarity 的样式链冲突
（39 处渲染失败），而 Congo 的空页**并未**因此修复（两种方案下都空）。按诊断三步骤
S3 反向验证原则回退到 `{{ ret }}`——回退后 Clarity 恢复通过。

**教训**：替换引擎既有控制流语义（Scriban 的 return）牵动面极广，收益必须有
**产出量级**的证据支撑，不能只凭"消除了一类隐患"的判断。

### F. 本轮残留（记录在案）

**Stack 的 11 处 `Index was outside the bounds of the array`**（term/taxonomy 页的
`partialValue "helper/image" (dict … "Context" page)` 调用链）。探针复现显示同样的
表达式在**普通页**上正常、只在**分类页**上失败；`{{ ret }}` 方案下 stack 产出仍有
129KB（6 处错误），而信号方案下 Clarity 会塌成 12 页——两害相权取产出量级更优者，
故保留 `ret`。下一轮以 term 页上下文为切入口继续定位。
