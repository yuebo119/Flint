---
title: 分布式 ID 方案专题：从 UUID 到雪花算法的位经济学
date: 2024-11-20
tags: [分布式, 算法, 架构]
categories: [文档]
description: 对比 UUID、数据库号段、Redis 自增与雪花算法等主流分布式 ID 生成方案，分析位分配、时钟回拨与趋势递增之间的关键取舍与落地边界。
---

![分布式 ID 生成方案的位分配示意](/images/demo-3.svg)

## 一、问题定义

单库时代用自增主键，分库分表或微服务之后自增 id 立刻失效：多张表各自计数会产生
重复 id，靠设置不同步长又很快耗尽且难以扩容。分布式 ID 要同时满足四个要求：

- **唯一性**：全局任意机器、任意时刻生成的 id 不重复，这是底线。
- **趋势递增**：id 大致随时间递增，让数据库索引页顺序追加而不是随机插入。
- **高可用**：生成服务不能成为单点，发号失败不能拖垮业务写入。
- **高性能**：单机每秒发号量要覆盖业务峰值，通常是万级到十万级。

这四个要求彼此牵制：uuid 唯一且去中心化但完全无序；数据库自增有序但受单库限制；
雪花算法兼顾有序与性能，却引入时钟依赖。选型的本质就是在这条张力带上找位置。

> 经验法则：先问 id 会不会暴露给外部（如出现在 URL 里），决定用自增还是随机；
> 再问下游是否按范围扫描，决定要严格递增还是趋势递增。两个问题问完，
> 六个候选方案通常只剩一两个。

## 二、方案枚举

| 方案 | 唯一性 | 有序性 | 单机吞吐 | 主要风险 |
| --- | --- | --- | --- | --- |
| UUID v4 | 强 | 无，随机 | 百万级 | 索引页分裂，主键体积大 |
| UUID v7 / ULID | 强 | 时间前缀有序 | 百万级 | 仍比整数长，生态支持度参差 |
| 数据库自增 | 强 | 严格连续 | 受单库写入限制 | 单点，扩容要改步长 |
| 号段模式 | 强 | 段内严格递增 | 十万级 | 号段用尽时有的一次性取段延迟 |
| Redis INCR | 强 | 严格递增 | 十万级 | Redis 故障即停发，持久化丢号 |
| 雪花算法 | 强 | 趋势递增 | 百万级 | 时钟回拨导致重复或停发 |

---

## 三、深入分析

### 3.1 雪花算法的位经济学

雪花算法把 64 位有符号长整型切成四段[^1]：1 位符号位恒为零，41 位毫秒时间戳，
10 位机器标识，12 位同毫秒序列号。41 位毫秒大约覆盖 69.9 年，10 位机器支持 1024 个节点，
每节点每毫秒 4096 个 id，理论单机峰值约 409.6 万。这是一笔精打细算的账：
每一段多给一位，另一段就要少一秒寿命或少一倍节点。

一个自洽的 Java 实现骨架如下：

```java
public final class SnowflakeIdGenerator {
    // 自定义纪元，取 2024-11-20 00:00:00 UTC，可再服役约 69.9 年
    private static final long EPOCH = 1732060800000L;
    private static final int MACHINE_BITS = 10;
    private static final int SEQUENCE_BITS = 12;

    private final long machineId;
    private long lastTimestamp = -1L;
    private long sequence = 0L;

    public SnowflakeIdGenerator(long machineId) {
        if (machineId < 0 || machineId >= (1L << MACHINE_BITS)) {
            throw new IllegalArgumentException("machineId 必须在 [0, 1024) 之间");
        }
        this.machineId = machineId;
    }

    public synchronized long nextId() {
        long now = System.currentTimeMillis();
        if (now < lastTimestamp) {
            // 时钟回拨：拒绝发号比发出重复 id 更安全
            throw new IllegalStateException(
                "时钟回拨 " + (lastTimestamp - now) + " 毫秒，拒绝发号");
        }
        if (now == lastTimestamp) {
            sequence = (sequence + 1) & ((1L << SEQUENCE_BITS) - 1);
            if (sequence == 0) {
                // 本毫秒序列号耗尽，自旋等待下一毫秒
                while (now <= lastTimestamp) {
                    now = System.currentTimeMillis();
                }
            }
        } else {
            sequence = 0L;
        }
        lastTimestamp = now;
        return ((now - EPOCH) << (MACHINE_BITS + SEQUENCE_BITS))
                | (machineId << SEQUENCE_BITS)
                | sequence;
    }
}
```

时钟回拨是雪花算法最现实的敌人。NTP 校准、虚拟机迁移、宿主机负载波动都可能让
`currentTimeMillis` 倒退。常见对策有三种：

- **等待回拨结束**：小步回拨（几十毫秒）时自旋等待，超过阈值再报警。
- **借用未来时间戳**：记录已用到的最大时间戳，回拨期间继续在这个水位上发号，
   副作用是 id 比真实时钟快一点，可接受。
- **直接失败**：发号失败让上游重试或降级到备用号段，换取绝不重复的硬保证。

机器标识的分配同样有讲究。写死在配置里，扩缩容时容易冲突；用 IP 后两段或容器序号
拼装，重启后可能漂移；成熟做法是接入注册中心或配置中心，启动时领取、心跳续约。

### 3.2 号段模式：用数据库换性能

号段模式把"每次发号都访问数据库"改成"每次领一段号在本地发"。用 Go 写一个最小骨架：

```go
// SegmentAllocator 每次从数据库领一段号，本地原子自增，耗尽再领
type SegmentAllocator struct {
    bizTag string
    step   int64
    mu     sync.Mutex
    next   int64 // 下一个可发号
    max    int64 // 当前段上界
    db     *sql.DB
}

func (a *SegmentAllocator) Next() (int64, error) {
    a.mu.Lock()
    defer a.mu.Unlock()
    if a.next >= a.max {
        if err := a.fetchSegment(); err != nil {
            return 0, err
        }
    }
    id := a.next
    a.next++
    return id, nil
}

// fetchSegment 在事务里推进水位并取回新段，保证多节点不重叠
func (a *SegmentAllocator) fetchSegment() error {
    tx, err := a.db.Begin()
    if err != nil {
        return err
    }
    defer tx.Rollback()
    var newMax int64
    err = tx.QueryRow(
        "UPDATE id_segment SET max_id = max_id + ? WHERE biz_tag = ? RETURNING max_id",
        a.step, a.bizTag).Scan(&newMax)
    if err != nil {
        return err
    }
    a.max = newMax
    a.next = newMax - a.step
    return tx.Commit()
}
```

号段模式的号严格递增且连续，对下游按 id 排序、翻页都友好；代价是段内会跳号
（节点重启时未用完的段被丢弃），且取段瞬间有一次数据库往返，通常可以靠预取
下一段把延迟隐藏掉。

### 3.3 无序 id 的隐性代价

为什么趋势递增值得单独投入？因为主流数据库的聚簇索引按主键组织存储[^2]。
随机主键（如 UUID v4）的插入会不断把新行写到索引页中部的随机位置，引发页分裂、
缓冲池命中率下降、写入放大。一张千万级的用户表换成 UUID v4 主键后写入吞吐下降
三到五倍并不罕见，而 UUID v7 或 ULID 因为时间前缀有序，能把大部分插入拉回顺序追加。
id 长度是另一笔账：64 位整数占 8 字节，UUID 字符串占 36 字节，二级索引里每个
主键引用都要复制一份，差距会随索引数量放大。

### 3.4 发号器的可观测与运维

发号器是基础设施，故障模式是"不报错、只是悄悄重复或停发"，所以观测必须前置。
三个必配指标：发号速率与段位水位（号段模式下距下次取段的预估时间，
取段延迟突增说明数据库在抖）；时钟回拨次数（任何大于零的回拨都要记录，
自旋等待超时长告警，借未来时间戳的方案还要监控虚拟时钟领先真实时钟的差值）；
机器标识冲突（同一 machineId 被两个节点持有是隐形事故，启动时向注册中心
校验唯一性，运行期心跳续约，续约失败主动退出）。

排障工具也要收口：一个把 id 反解出时间戳、机器号、序列号的函数，
加一张按 id 区间反查发号节点的路由表。线上遇到"某批 id 重复"的投诉，
先反解 id 拿到机器号与时间窗，再定位到具体节点，
比全链路捞日志快一个数量级。

规模再大一步，单一纪元会撞上两个天花板：41 位时间戳耗尽、机器位不够。
应对分别是启用新纪元（解析端同时兼容两个纪元）与重新划段（缩小序列位）。
这些是协议级变更，影响所有下游，位分配时就要预留演进路径，
而不是等撞线再动。容量规划按业务峰值两倍预估单节点发号量，
得出节点数再乘二留灰度与故障余量，和 machineId 上限比对，提前一年报扩容。
发号量的增长曲线通常是业务曲线的领先指标，接进容量仪表盘，
比等延迟上涨再反应从容得多。发号器上线前还要过一次压测：
按预估峰值的两倍打满，观察同毫秒序列号耗尽时的自旋表现，
以及号段模式下取段延迟的毛刺。压测数据是容量规划的输入，
也是后续扩容谈判的依据，没有它，扩容时点的争论永远停在感觉层面。

## 四、选型建议

按场景对号入座，优先级从高到低：

1. **内部订单、流水、消息主键**
   1. 默认选雪花算法或其变体，注意时钟回拨预案
   2. 机器 id 走配置中心分配，不要硬编码
   3. 暴露一个把 id 解析回时间与机器的工具函数，排障时能直接读出生成时刻
2. **对外开放的 id（URL、文件名）**
   1. 用 UUID v7 或 ULID，避免自增 id 被遍历爬取
   2. 需要更短可读性时再考虑编码转换，不要压缩成可猜的序号
3. **规模很小、团队没有运维余量**
   1. 号段模式最省心，一张表一个业务Tag，重启丢一段号完全可以接受
   2. 单库还没成为瓶颈前，不必早上 Redis 或自研发号器

无论选哪种，把生成器包成基础库统一收口，禁止业务代码自己拼时间戳加随机数，
这是唯一不能妥协的纪律。

## 五、参考文献

- [Snowflake - 维基百科](https://zh.wikipedia.org/wiki/雪花算法)
- [UUID - 维基百科](https://zh.wikipedia.org/wiki/通用唯一识别码)
- [号段模式发号器设计笔记](https://example.com/distributed-id/segment-allocator)

[^1]: Snowflake 方案源于 Twitter 2010 年前后的工程实践，本文位分配为其经典形式，
      各公司实现（如百度 UidGenerator、美团 Leaf）在机器 id 分配与秒级时间戳上各有变体。
[^2]: 这里指 InnoDB 这类聚簇索引引擎：数据行按主键顺序存储在 B+ 树叶子节点中，
      PostgreSQL 的堆表加二级索引结构不同，但随机主键仍会放大索引维护成本。
