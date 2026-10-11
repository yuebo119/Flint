# LazyPageObject 委托化设计裁决件（R7 T6.1）

> 2026-10-11 产出，**待用户裁决**（历史约束：R6 批次 3.3"轻量投影"2026-09-09 裁决搁置；重开新证据 = T1/T3/T4 实测渲染段 1027→884ms、固定开销定量 ~150 次 SetValue/页 + 双命名键翻倍、L3 内存 2.26x 稳定复现）。
> 本文是裁决输入，不是实施授权。批准前 T6.2/T6.3 不动工。

## 一、现状问题（定量）

每页构造 `LazyPageObject`（`Objects.cs:282-544`）：

- 约 **150 次 `SetValue` 字典插入**（snake 与 Pascal 双命名各一份）
- 约 **15 个包装对象分配**（函数对象 ×12、ScriptObject 嵌套 ×3）
- `GetPageFunction`/`PagePaginateFunction` 等每页重复 new（T1.2 已消重复，但每页仍各建一份）

万页站点 = 150 万次字典写 + 15 万个对象。T3.4 已把**构建级**函数共享化（i18n/template_exists/partialCached），剩余成本全部在页面级。

## 二、目标形态

```csharp
// 访问器表：构建期静态构建一次（Pattern 无页面态），全部页面共享
private static readonly FrozenDictionary<string, Func<PageContext, LazyPageObject, object?>> MemberAccessors =
    BuildAccessors();   // 约 80 个键（归一化后），原 150 键的一半

// LazyPageObject 不再在构造期 SetValue，TryGetValue 走表
public override bool TryGetValue(ScriptTemplateContext? ctx, SourceSpan span, string member, out object? value)
{
    if (MemberAccessors.TryGetValue(member, out var accessor))   // OrdinalIgnoreCase 键
    {
        value = accessor(_page, this);
        return true;
    }
    return base.TryGetValue(ctx, span, member, out value);
}
```

**双命名消失的机制**：键以 snake_case 归一注册，查找走 `FrozenDictionary` 的 OrdinalIgnoreCase 模式覆盖 Pascal 形态；`Param`/`param` 这类同值异形键合并为一条。Scriban 成员查找本身大小写不敏感，行为等价（`Objects.cs` 现有注释自证该性质）。

## 三、成员三栏分类（80 键归一化后）

### A. 保留急切（构造期物化，约 12 键）

`title / content / permalink / rel_permalink / date / lastmod / kind / is_home / is_page / is_section / section / type`

理由：渲染高频（几乎每页每模板访问）+ 取值零成本（PageContext 字段直读）。转委托反而每次访问多一层委托调用。

### B. 转委托·廉价标量（约 40 键）

`word_count / reading_time / description / summary / draft / weight / layout / plain / raw_content / path / level / bundle_type / keywords / publish_date / expiry_date / aliases / link_title / truncated / fuzzy_word_count / output_format / markup / len / ...`

理由：派生自 PageContext 字段（一行投影），低频访问；委托化后未访问页面零成本。

### C. 转委托·昂贵对象（约 20 键）

`pages / sections / terms / ancestors / parent / current_section / first_section / resources / data / params / file / language / store / output_formats / alternative_output_formats / plain_words / fragments / ...`

理由：构造涉及集合包装/树查找（plain_words 对长文是全词切分！现构造期无条件执行）。惰性化后未访问零成本，访问后缓存实例字段。

### D. 转委托·函数对象（约 8 键）

`get_page / paginate / get_terms / is_ancestor / is_descendant / has_shortcode / render_string / param`

理由：每个是 IScriptCustomFunction 包装，委托化为"首次访问 new 一次 + 实例字段缓存"。

## 四、接口与迁移

1. **访问器构建**：`BuildAccessors()` 静态方法按三栏表注册；每条 `static (ctx, self) => ...`。
2. **实例缓存位**：C 组成员的惰性缓存沿用现有 `_lazyGate` 双检锁模式（`_ancestorsValue` 等已存在，扩展为 `object?[] _cacheSlots` 按 accessor 索引）。
3. **兼容性护栏**：
   - TryGetValue 特殊路径保留（paginator 的访问打标、ancestors 的站点页集回退——现有逻辑原样搬进对应 accessor）。
   - `IFlintNonDataObject` 语义不变。
   - partial dict 合并路径（`MergePageMembers`/`AddPageMethodFamily`）读成员的入口走同一 TryGetValue，零改动。
4. **灰度**：两步——先 D 组函数对象（最低风险），后 C 组昂贵对象；A/B 组最后一批整体切。
5. **金丝雀**：21 主题矩阵产物**字节级对称**（时间戳类字段除外）+ FM 等价性 25 项 + 全量测试。

## 五、预期收益与风险

| 项 | 预期 | 置信度 |
|----|------|--------|
| 每页构造分配 | ~150 次 SetValue + 15 对象 → 0（未访问成员） | [事实] 机制确定 |
| 渲染段固定开销 | T0.2 差额 ~295ms 中的构造占比待 profile 定位后估 | [推断] |
| L3 内存 2.26x | plain_words/fragments 等大对象不再无条件物化 | [推断] |
| 风险 | 成员访问路径回归面大（21 主题字节级金丝雀兜底） | 缓解已设计 |

## 六、裁决请求

- [x] **批准实施**（用户 2026-10-12 裁决："t6 批准实施"）
- [ ] 维持搁置（登记理由，本文件归档）
- [ ] 部分批准（仅 D 组函数对象委托化，C 组缓行）

**实施终态（2026-10-12，偏离设计件的两处，均已验证）**：
1. **A/B 组标量保留急切**（设计件计划全量 FrozenDictionary 委托化）：实施时确认实例字典键枚举被 partial 合并路径（MergePageMembers 的 `pageObj.Keys`）与 reflect 类操作依赖，标量键转访问器表会让枚举看到空对象——混合形态（标量急切 + C/D 组惰性）等价消掉每页 ~15 个对象分配与 plain_words 全文切分（内存最大项），枚举行为零变化。
2. **分发热路径教训**：TryGetValue 的分发顺序是硬性能约束——急切字典查找必须最前（O(1) 哈希），惰性 switch 其次，特殊成员 is 链最后。首版反序（is 链在前）实测渲染段 884→5837ms，修正后与基线同机对照持平。
3. 验收金丝雀全绿：矩阵 20/21 对称、loveit 产物 diff 仅时间戳字段、单测 1128 全绿。
