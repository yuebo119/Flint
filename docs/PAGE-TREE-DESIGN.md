# T2.0 页面树设计提案（待裁决）

> 状态：**设计稿，未实现**。本提案基于 Hugo 源码研究（`hugolib/doctree/`、`content_map*.go`、`common/paths/pathparser.go`）与 Flint 消费面分析。
> 裁决通过后进入 T2.1（装配）→ T2.2（查询+cascade）→ T2.3（增量精确替换）。
> 配套研究材料：Hugo doctree API 面细节见对话记录（agent 输出含全部文件行号佐证）。

---

## 1. 目标与范围

**要解决的问题**（对齐评审代差 1）：
- Flint 现状：`List<ParsedContent>` 平面表。section 查询 O(n) 全扫；缺失 section/home 由独立步骤事后合成；增量构建为重建 siteContext 重解析全部文件；cascade 无处安放。

**范围决策**：
- **单 language 维度**。Hugo 的 `[language, version, role]` 三维 Shifter/Shape/回退全部不做——树上 value 直接是节点对象，无 `contentNodesMap`。将来加多语言时把 value 换成 `Dictionary<LangVector, PageNode>` 并补按距离选优的 Shift，树结构与遍历代码不动（保留 Hugo 的分层精神）。
- **不做** `TypeContentData`（`_content.gotmpl` 编程式页面）、version/role。

## 2. 核心契约（C# 接口签名）

新文件 `src/Flint.Core/Site/PageTree/`：

```csharp
/// <summary>bundle 类型（对齐 Hugo pathparser.go:525-548 的判定）</summary>
public enum PageBundleType
{
    /// <summary>普通单页：posts/hello.md</summary>
    Single,
    /// <summary>leaf bundle：posts/gallery/index.md（资源归页面所有，key = 目录名）</summary>
    LeafBundle,
    /// <summary>branch 节点：posts/_index.md（section/列表页）</summary>
    Branch,
    /// <summary>合成节点（缺失 section/home 补齐，无文件来源）</summary>
    Synthesized
}

/// <summary>树节点。T2.1 落地时持有 ParsedContent；树自身不感知渲染模型。</summary>
public sealed class PageTreeNode
{
    public required string Key { get; init; }          // 规范化树 key（见 §3）
    public required PageBundleType BundleType { get; set; }
    public required string SourcePath { get; set; }    // 合成节点为空串
    public ParsedContent? Content { get; set; }        // branch 合成节点为 null
    public bool IsBranch => BundleType is PageBundleType.Branch
                         || BundleType is PageBundleType.Synthesized && Content is null;
}

/// <summary>页面树：以规范化逻辑路径为 key 的基数树。</summary>
public sealed class PageTree
{
    public int Count { get; }

    /// <summary>插入节点；key 冲突（leaf bundle 与同名单页）时抛 InvalidOperationException。</summary>
    public void Insert(string key, PageTreeNode node);

    public PageTreeNode? Get(string key);
    public bool Has(string key);

    /// <summary>删除单节点。</summary>
    public bool Delete(string key);

    /// <summary>删除前缀下全部节点（含该 section），返回删除数。T2.3 增量删除的基础。</summary>
    public int DeletePrefix(string prefix);

    /// <summary>最近祖先查找：从 key 向上（path.Dir）找第一个命中节点。用于 section 归属判定。</summary>
    public PageTreeNode? LongestPrefix(string key);

    /// <summary>前缀遍历。walker 支持整子树剪枝与遍历中延迟删除（对齐 Hugo Walker 语义）。</summary>
    public void Walk(PageTreeWalker walker);
}

/// <summary>声明式遍历配置（对齐 Hugo NodeShiftTreeWalker，去掉 shift/fallback 维度）。</summary>
public sealed class PageTreeWalker
{
    public required PageTree Tree { get; init; }
    public string? Prefix { get; init; }               // 带尾斜杠约定，见 §3
    public Func<PageTreeNode, bool>? IncludeFilter { get; init; }

    /// <summary>访问节点；返回 true = 跳过该节点的整棵子树（radix 级剪枝）。</summary>
    public Func<string, PageTreeNode, bool>? Handle { get; init; }

    /// <summary>遍历中标记延迟删除（遍历结束后批量执行，边走边删会破坏遍历序）。</summary>
    public void SkipPrefix(string prefix);
}
```

**自写 radix 树，不引依赖**。Go 版依赖 go-radix；C# 侧自写紧凑实现（插入/前缀遍历/子树剪枝约 250 行）。不用 `Dictionary<string, node>` + 前缀线性扫描替代——那会让 section 查询退化回 O(n)，违背本设计初衷。若裁决不愿自写，备选是引入 `Nito.Collections` 类库 [需评审]，不推荐。

## 3. key 规范（移植 Hugo cleanTreeKey）

```
规则：小写 + Unix 斜杠 + 剥扩展名 + 前导 '/' + 无尾 '/'；home = ""（空串）
/posts/hello.md        → /posts/hello
/posts/gallery/index.md（leaf）→ /posts/gallery
/posts/_index.md（branch）     → /posts
content/index.md（站点首页）    → ""（home）
```

- **home 为空串**是关键约定：home 天然是所有前缀遍历的根（`Prefix: "/"` 命中全部后代）。
- **剪枝必须带尾斜杠**：`Prefix: "/blog/"` 防止 `/blog` 误匹配 `/blog2`（Hugo getPagesInSection:343 的 `AddTrailingSlash`）。
- 树 key 冲突规则（对齐 Hugo:524-530 注释）：普通页 `posts.md` 与 section `posts/_index.md` 同 key `/posts` 时——**真实内容页胜出**，不合成覆盖；冲突即抛异常由构建报告。

## 4. 装配流程（T2.1）

```
ScanContentFilesAsync（现有）
  → 分类：文件名 == "index" → LeafBundle（key=父目录）；"_index" → Branch；其他 → Single
  → Insert 逐个入树（key 冲突 → BuildError PARSE002）
  → 补缺遍历（单次 Walk）：
      - 无 home（"" 未命中）→ Insert 合成 home（BundleType=Synthesized, kind=home）
      - 某目录路径 /a/b 是页面祖先但无节点 → Insert 合成 section（kind=section）
  → 输出：tree.Walk 全序收集 List<PageContext>（weight+date 排序，保持现有 BuildPageContexts 输出契约）
```

对外契约**零变化**：`SiteContext.Pages/RegularPages` 仍是 `List<PageContext>`；TaxonomyService/FeedGenerator/SitemapGenerator/LazyPageList 全部不动（T2.2 才让它们走树查询）。

## 5. 查询模式（T2.2）

```csharp
// section 直属页面（不含子 section）：前缀遍历 + branch 剪枝（Hugo 核心套路）
public IReadOnlyList<PageTreeNode> GetPagesInSection(string sectionKey, bool recursive)
{
    var result = new List<PageTreeNode>();
    tree.Walk(new PageTreeWalker
    {
        Tree = tree,
        Prefix = sectionKey + "/",                     // 尾斜杠防误匹配
        Handle = (key, node) =>
        {
            result.Add(node);
            if (node.IsBranch && !recursive)
            {
                walker.SkipPrefix(key + "/");          // radix 整子树跳过
            }
            return false;
        }
    });
    return result;
}
```

- TaxonomyService 改造：分类/标签归属保持现有字典分组（Hugo 也是独立 taxonomy 索引树 `/<复数>/<词条>/<页面key>`——**T2.2 只做 section 查询接树，taxonomy 树化列为可选**，因 Flint 词条数远小于页面数，字典分组 O(pages) 一次构建不构成瓶颈）[裁决点 E]。
- cascade：`Branch/Synthesized` 节点的 FrontMatter.Cascade 在装配补缺后自顶向下传播一次（Walk 顺序天然父先子后），合并进子节点 Content.Metadata.Params。

## 6. 增量精确替换（T2.3，依赖 T2.1/T2.2）

- 单文件变化：树上 `Get(key)` → 有则原位替换 Content（更新 BundleType），无则 Insert + 补缺遍历。
- 文件删除：`Delete(key)`；目录删除：`DeletePrefix("/section/")`。
- 受影响 section 重算：`LongestPrefix(changedKey)` 找到归属 section → 只重渲染该 section 的列表页 + 变化页自身。
- 现有 `IncrementalBuildAsync` 的"重解析全部文件"（SiteBuilder.cs 215-218）替换为上述路径。

## 7. 风险与权衡

| 风险 | 缓解 |
|---|---|
| 自写 radix 的正确性 | 边界：前缀碰撞/大小写/尾斜杠。配套单测（Go go-radix 行为为参照）+ 现有 BuildPipeline 全量回归 |
| key 冲突语义与 Hugo 不完全一致（Flint 无多输出/多语言） | 冲突即报错（fail-fast），文档声明 |
| 排序语义漂移（现有 List 顺序 = 文件扫描序 → 树序 = 字典序） | 输出前按 weight+date 排序（现有 BuildPageContexts 已有排序逻辑，保持一致） |
| T2.1 与 T2.2/T2.3 之间过渡态的性能回退 | T2.1 本身不提速也不减速（纯结构替换）；提速在 T2.2/T2.3 兑现 |

## 8. 待裁决问题（请逐项勾选）

- **[A] radix 实现方式**：自写紧凑 radix（推荐，~250 行无依赖） / 引入第三方库 / 暂用有序字典前缀扫描（性能目标打折）
- **[B] PageTreeNode 持有 ParsedContent**（推荐） / 引入新中间类型（解耦但多一层转换）
- **[C] 集成策略**：树封装在 SiteBuilder 内部、对外仍 List<PageContext>（推荐，零破坏） / 直接改造 SiteContext 对外契约（破坏性）
- **[D] 合成 section/home 是否参与渲染**：参与（有 kind/layout 可被模板引用，对齐 Hugo） / 仅作结构占位不渲染
- **[E] taxonomy 索引树化**：T2.2 顺带做 / 暂保持字典分组（推荐，词条量小非瓶颈）
- **[F] 批准 T2.1 动工**：是 / 否（先只做 radix+单测）
