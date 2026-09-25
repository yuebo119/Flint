---
title: 分页设计专题：LIMIT/OFFSET 到 keyset 的深水区
date: 2026-08-12
tags: [数据库, 性能, 设计]
categories: [文档]
description: 对比 offset 分页、游标分页与搜索场景的 search_after 方案，用执行计划说明深分页为何变慢，并给出接口兼容的渐进迁移路径。
---

![offset 分页与游标分页的扫描范围对比](/images/demo-6.svg)

## 一、问题定义

分页接口只有两个动作：取一页数据，告诉调用方还有没有下一页。真正的麻烦出现在
两个地方。第一是**深分页性能**：`LIMIT 20 OFFSET 100000` 并不是"跳过前十万行"
这么便宜，数据库要先定位、扫描、再丢弃十万行，深度越大越慢。第二是**翻页漂移**：
用户看第一页的瞬间有新数据插入或旧数据删除，等他翻到第二页时，整个结果集已经
位移，于是出现重复行或漏行。

管理后台场景对这两个问题都不敏感：数据量小、翻页浅、使用者容忍刷新。
面向 C 端的信息流、订单列表则相反：深度动辄上千页，一边翻一边有新订单进来，
offset 分页从一开始就不该上场。设计分页方案前，先回答三个问题：
结果集排序是否稳定唯一、最大深度约多少、翻页期间数据变动频率如何。

## 二、方案枚举

| 方案 | 请求参数 | 深页性能 | 翻页漂移 | 随机跳页 |
| --- | --- | --- | --- | --- |
| LIMIT/OFFSET | `page=5000` | 随深度线性退化 | 有 | 支持 |
| 延迟关联 offset | `page=5000` | 退化减半 | 有 | 支持 |
| keyset 游标 | `cursor=xxx` | 恒定，走索引 | 无 | 不支持 |
| 先查总数再分页 | 同上 + 总数 | 多一次全量计数 | 有 | 支持 |
| 搜索 search_after | `after=[排序值]` | 恒定 | 无 | 不支持 |

---

## 三、深入分析

### 3.1 offset 为什么慢：扫描代价的真相

offset 分页的 SQL 看起来人畜无害：

```sql
-- offset 方案：数据库要扫描前 100020 行，丢弃前 100000 行
SELECT id, title, created_at
FROM articles
ORDER BY id DESC
LIMIT 20 OFFSET 100000;

-- keyset 方案：从上次的位置继续，直接走主键索引定位
SELECT id, title, created_at
FROM articles
WHERE id < 100000
ORDER BY id DESC
LIMIT 20;
```

两条语句的结果集完全相同，执行成本天差地别。offset 版本即使有 `id` 上的索引，
引擎也要遍历索引树找到偏移起点，聚簇索引下还要把十万行的行数据读出来再丢掉；
keyset 版本利用排序键的有序性，一条 `WHERE id < ?` 直接定位到游标位置，
读取行数与页大小成正比，与深度无关。

在一张千万行、InnoDB、SSD 的订单表上，两种方案的典型耗时量级如下
（数据仅作横向对比参考，实际以本库执行计划为准）：

| 翻页深度 | offset 方案 | keyset 方案 |
| --- | --- | --- |
| 第 10 页 | 5 毫秒 | 3 毫秒 |
| 第 1000 页 | 80 毫秒 | 4 毫秒 |
| 第 10000 页 | 900 毫秒 | 4 毫秒 |
| 第 100000 页 | 10 秒以上 | 4 毫秒 |

offset 方案还有一个常被忽视的暗坑：`ORDER BY` 的字段不唯一时，同一行可能出现在
两页里。按 `created_at` 排序遇到同秒插入的多条记录，跨页边界时它们的相对顺序
不确定，翻页就会重复或漏行。keyset 方案天然规避漂移，因为游标固定在某一行的
排序键上，之后的数据变动只会让"新行出现在游标之后"，不会让已读过的行位移。

### 3.2 游标的工程封装

游标不该把内部排序键直接暴露给客户端，否则调用方可以伪造、遍历、猜测数据量。
常规做法是编码成不透明字符串：

```python
import base64
import json

def encode_cursor(sort_key: int) -> str:
    """把排序键编码成不透明游标，客户端不应解析其内容"""
    raw = json.dumps({"id": sort_key}, separators=(",", ":")).encode()
    return base64.urlsafe_b64encode(raw).decode().rstrip("=")

def decode_cursor(cursor: str) -> int:
    padded = cursor + "=" * (-len(cursor) % 4)  # 补齐 base64 填充
    raw = base64.urlsafe_b64decode(padded.encode())
    return json.loads(raw)["id"]

def list_page(cursor: str | None, size: int = 20):
    if cursor is None:
        # 第一页：直接取最新一屏
        rows = db.query(
            "SELECT id, title FROM articles ORDER BY id DESC LIMIT ?", size + 1)
    else:
        last_id = decode_cursor(cursor)
        rows = db.query(
            "SELECT id, title FROM articles WHERE id < ? ORDER BY id DESC LIMIT ?",
            last_id, size + 1)
    # 多查一行判断有没有下一页，省去一次 COUNT(*)
    has_next = len(rows) > size
    next_cursor = encode_cursor(rows[-2].id) if has_next else None
    return rows[:size], next_cursor
```

三个设计要点：多查一行判断 `has_next`，避免深分页场景下昂贵的 `COUNT(*)`；
游标里只放排序键，不放过滤条件，条件变化时客户端应重新从头翻；
客户端拿到的 `next_cursor` 是一次性的，超过合理时间（如 24 小时）后失效，
失效时返回明确错误码让调用方回到第一页，而不是默默返回错误数据。

### 3.3 迁移：别让接口一次换血

存量 offset 接口切换到 keyset，最大的阻力不是数据库而是调用方。
一个低风险路径是新旧并存：

<details>
<summary>渐进迁移的三步走</summary>

第一步，在现有 offset 接口上增加可选的 `cursor` 参数，两者都传时以 cursor 为准，
老客户端无感。第二步，控制台类需要跳页的页面继续用 offset，但只允许浅翻
（比如限制最大 100 页），并在响应里附带 `next_cursor` 引导新调用方迁移。
第三步，监控 offset 请求的深度分布，确认深分页流量清零后，再把 offset 参数
标记废弃，进入下线倒计时。整个过程两个参数并存一个版本周期，
比一次性替换的回归风险小一个数量级。

</details>

在 psql 里核对执行计划时，如果输出被分页器截断，按 <kbd>q</kbd> 退出手动分页，
加 `\pset pager off` 可以彻底关掉它。

### 3.4 搜索场景与深页治理

通用搜索的分页是另一套问题：结果按相关度排序，同分值的大量文档之间没有稳定
顺序；深分页在分布式引擎上代价更高，每个分片都要取前 N 条再归并。
Elasticsearch 的答案是 `search_after` 加 PIT（point in time）：
前者用上一页最后一条的排序值定位下一页，避开 from/size 的深页开销；
后者把查询绑定到固定快照，翻页期间的索引写入不会让结果集漂移。
要点有三：排序必须带 tiebreaker 字段保证同分值顺序稳定；PIT 有生命周期，
超时后重新打开而不是继续翻；全量遍历用 PIT 分批，scroll API 已废弃。
自研搜索分页同样遵循"排序键唯一、快照可见、游标不透明"三条，
它们与数据库 keyset 分页是同一思想在不同引擎上的投影。

必须保留随机跳页的场景里，offset 方案还有一个续命手段：延迟关联。
先按覆盖索引查出当页的主键，再回表取完整行，避免丢弃十万行的完整数据，
深页耗时能砍掉一半上下。但它没有改变扫描量与深度成正比的本质，
是过渡方案而非终点。

观测指标也要单独建：分页深度分布、游标失效率、深分页请求占比。
深度分布决定要不要治理，失效率反映游标有效期是否合理，
深分页占比是迁移进度的直接度量。三个数接进仪表盘，
offset 方案的退出就从"感觉没人用了"变成可验证的事实。
而对按年翻的审计日志这类超深场景，更该问的问题或许是：
用户真的需要随机跳页吗？把交互从"翻到任意一页"改成"先筛选再浏览"，
offset 的深水区问题会消失一大半。分页设计的第一性原理是理解用户怎么用数据，
而不是先把接口写出来再优化。

分页与排序还牵动一个常被忽视的依赖：总数。前端习惯显示"共 12345 条"，
这个数字在深分页与大数据量表上是一次全量计数，代价可能超过取数本身。
对策前文提过多查一行判断有没有下一页，另外可以把总数改为估算
（如数据库的估算行数），或只在浅页显示精确值、深页显示"更多"。
交互上一次小的让步，换来的是一次全表扫描的消失。

> 执行计划是分页优化唯一的裁判：同一个查询，看一眼是顺序扫描还是索引范围扫描，
> 比争论半小时"哪个方案更快"都管用。

## 四、选型建议

- [ ] 列出所有分页接口，标注排序键是否唯一、最大翻页深度、是否需要跳页
- [ ] 排序键不唯一的先补唯一 tiebreaker（如 `ORDER BY created_at DESC, id DESC`），
      这是所有方案的地基
- [ ] C 端信息流、订单列表迁移 keyset，游标编码为不透明字符串
- [ ] 管理后台保留 offset，但限制最大深度并在超深时提示使用筛选条件收敛
- [ ] 去掉深分页场景的 `COUNT(*)` 总数，用"多查一行"判断下一页
- [ ] 接口文档写清游标有效期与失效行为，客户端要有失效重头翻的兜底逻辑

收束成一句话：**浅分页用 offset，深分页用游标，跳页是功能不是默认**。
分页方案的选择依据是数据规模与访问模式，不是接口的书写习惯。

## 五、参考文献

- [分页 - 维基百科](https://zh.wikipedia.org/wiki/分页)
- [游标分页设计笔记](https://example.com/pagination/keyset-pattern)
- [数据库执行计划入门](https://example.com/database/explain-basics)
