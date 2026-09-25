---
title: "领域事件与系统集成：从进程内解耦到跨服务最终一致"
date: 2025-11-27
tags: ["领域事件", "系统集成", "最终一致性"]
categories: ["后端", "架构"]
description: "用订单系统完整走一遍领域事件的落地路径：事件建模、进程内事件、Outbox 跨服务投递、消费者幂等与对账，附 C# 代码和接入检查清单。"
---

订单系统上线半年后，`PlaceOrder` 方法从三十行涨到一百八十行：发确认邮件、加积分、通知仓库、同步搜索索引，每加一个下游就加一段 try-catch，因为"不能影响主流程"。下游一多，主流程的性能和稳定性全绑在了最慢的那个依赖上。我们用领域事件把这条耦合链拆开，主流程只发布事实，副作用各自订阅。这篇文章完整记录落地路径：事件怎么建模、进程内怎么分发、跨服务怎么可靠投递、消费端怎么保证幂等。先交代背景数字：改造前 `PlaceOrder` 方法 187 行，包含 4 段 try-catch，依赖 5 个外部服务，任何一段超时都会让下单失败。更要命的是测试，为了覆盖这些分支，单测要 mock 五个服务，新同学跑通一次测试要半天。改造后主流程 23 行，零外部依赖，副作用全部退到事件处理器里，各自独立测试、独立部署。

![领域事件从进程内到跨服务的两条路径](/images/demo-8.svg)

## 事件建模：命名即语义

领域事件是"已经发生的事实"，所以命名用过去时，不带祈使和猜测：`OrderPlaced`、`OrderPaid`，而不是 `SendEmail`、`NotifyWarehouse`。事件体只放消费方做决策必需的最小信息，不放整个聚合：一条 `OrderPaid` 带订单号、金额、会员号就够，带上全部订单明细会让事件膨胀，也会诱导消费方直接读事件里的陈旧数据。

每个事件统一带四个元数据字段，缺一个都会在排障时吃亏：

- `event_id`：全局唯一，消费端幂等和去重的主键
- `occurred_at`：事件发生时间，由发布方填写，不由中间件填写
- `trace_id`：链路追踪，串联发布、投递、消费全过程
- `version`：事件结构版本，结构变更时升级，老版本双写过渡

订单域最终收敛出七个事件，覆盖了全部十一个下游的决策点，下表是最核心的五个：

| 事件 | 触发时机 | 主要消费方 |
| --- | --- | --- |
| `OrderPlaced` | 订单创建完成 | 仓库预打包、风控 |
| `OrderPaid` | 支付成功 | 积分、确认邮件、搜索索引 |
| `OrderShipped` | 仓库发货出库 | 物流通知、会员进度 |
| `OrderCancelled` | 用户取消或超时取消 | 库存返还、积分扣回 |
| `OrderRefunded` | 退款到账 | 余额返还、财务对账 |

领域事件的概念和适用边界可以参考 https://martinfowler.com/bliki/DomainEvent.html ，我们落地时最重要的取舍是"事件表达事实，不表达意图"。

### 事件粒度的反模式

事件设计里最常见的反模式是"事件爆炸"：把一次业务操作拆成十几个细碎事件，`OrderItemAdded`、`OrderItemQuantityChanged`、`OrderTotalRecalculated` 全发出来，消费方要么被淹没，要么只能订阅最后一个。事件的粒度对应"消费方关心的决策点"：加积分关心的是支付完成，不关心中间改了几次数量。判断方法：列出每个下游真正需要做什么决策，一个决策对应一个事件，多出来的都是噪音。

## 进程内事件：同一事务，同一进程

先解决进程内的解耦。订单保存后发布 `OrderPaid`，处理器同步或异步执行。事件契约的代码定义见本节末尾：接口加一个 record，四个元数据字段在 record 的构造参数和属性里各就各位。关键规则：进程内事件与业务数据在同一个事务里生效，即事件先入"发件箱"表再提交事务，避免"订单存了但事件丢了"。

> 同步处理器管同生共死，异步处理器各自安好。分不清这两者的系统，主流程永远在被副作用绑架。

同步还是异步，判断标准是"处理器失败是否应该让主流程回滚"。更新订单的统计字段，失败必须回滚，用同步；发邮件，失败不该影响下单，用异步。异步处理器的线程模型也要注意：直接在请求线程里 fire-and-forget 是最省事也最危险的，进程一退出任务就丢。我们的做法是进程内队列加后台工作者，工作者从队列取事件执行，重启时队列内容落盘恢复。这个模式跑了一年，零丢失，代价是要多维护一个队列组件。

```csharp
// 事件契约：接口定义加具体事件
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
    string TraceId { get; }
}

public sealed record OrderPaid(
    Guid OrderId,
    decimal Amount,
    Guid MemberId,
    DateTimeOffset OccurredAt,
    string TraceId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
}

// 发布侧：业务数据与事件写入发件箱，同一个本地事务
public class OrderAppService
{
    public async Task PayAsync(Guid orderId, string paymentNo)
    {
        await using var tx = await _unitOfWork.BeginAsync();
        var order = await _orders.FindAsync(orderId);
        order.MarkPaid(paymentNo);
        _outbox.Add(new OutboxMessage(
            type: nameof(OrderPaid),
            payload: JsonSerializer.Serialize(new OrderPaid(
                order.Id, order.PaidAmount, order.MemberId,
                DateTimeOffset.UtcNow, _trace.Current()))));
        await _unitOfWork.CommitAsync(tx);
    }
}
```

## 跨服务事件：Outbox 加消息队列

跨进程的可靠性不能靠"发完 MQ 就信"，MQ 可能丢、可能重启、可能分区 Leader 切换。标准做法是事务性发件箱：一个后台中继读取发件箱表，投递到 MQ，成功后标记已发送。中继要处理三种情况：投递成功、投递失败重试、反复失败进死信并告警。这样"业务成功"与"事件最终送达"之间只差一个可观测、可补偿的间隙。

### 顺序性与高可用

中继有两个工程细节值得说。一是顺序性：同一订单的 `OrderPaid` 和 `OrderShipped` 如果乱序到达，下游状态机就乱了。解法是按聚合 ID 分片投递，同一订单的事件走同一个分区，中继按分片串行发送，分片之间并行。二是中继自身的高可用：多实例部署时要靠数据库行锁或乐观锁抢批，我们用的是 `SELECT ... FOR UPDATE SKIP LOCKED` 取一批未发送消息，天然分片，挂了实例其他实例接管，不需要额外协调组件。

```csharp
public class OutboxRelay : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var batch = await _db.Outbox
                .Where(m => m.SentAt == null && m.RetryCount < 5)
                .OrderBy(m => m.CreatedAt)
                .Take(100).ToListAsync(ct);

            foreach (var msg in batch)
            {
                try
                {
                    await _mq.PublishAsync(msg.Type, msg.Payload, ct);
                    msg.MarkSent();
                }
                catch
                {
                    msg.IncreaseRetry(TimeSpan.FromSeconds(1 << msg.RetryCount));
                }
            }
            await _db.SaveChangesAsync(ct);
            await Task.Delay(TimeSpan.FromMilliseconds(200), ct); // 200ms 轮询
        }
    }
}
```

## 消费端：幂等是底线

跨服务投递至少一次，重复是常态。消费者必须用 `event_id` 去重，标准做法是一张消费记录表，处理业务与写入记录在同一本地事务：

```csharp
public async Task HandleAsync(OrderPaid evt)
{
    await using var tx = await _db.BeginAsync();
    if (await _db.IsProcessedAsync(evt.EventId)) return;   // 已处理，直接确认
    await _points.AddAsync(evt.MemberId, PointsOf(evt.Amount));
    await _db.MarkProcessedAsync(evt.EventId);
    await _db.CommitAsync(tx);
}
```

去重表不能无限增长，按事件时间保留 7 到 30 天即可，覆盖所有重试窗口。消费失败的处置要分级：业务异常（数据不全）进死信等人工；临时异常（下游抖动）指数退避重试；毒消息（反序列化失败）直接告警，不要无限重试。

事件结构的演进是长期运营里逃不掉的问题。加字段是安全的，老消费者忽略新字段；删字段和改类型是破坏性的，必须升级 version 并双写过渡：发布方先同时写两个版本，等所有消费者升级到新版本，再停止写老版本。我们在一次"金额从分改成元"的变更里严格执行了这个流程，过渡期两个月，零故障。跳过过渡直接改的生产事故，我见过三个团队各一次，规律整齐得可怕。

## 接入检查清单

新系统接入事件总线前逐项确认：

- [ ] 事件命名是过去时，且不带下游动作
- [ ] 事件体是最小必要信息，消费方不依赖事件里的陈旧明细
- [ ] 发布方写入 Outbox 与业务数据同事务
- [ ] 中继有重试、退避、死信和积压告警
- [ ] 消费端用 event_id 幂等，去重与业务同事务
- [ ] 有对账任务：每日比对发布方与消费方的处理量，差异进工单
- [ ] 事件结构变更走版本升级，新老结构双写过渡一个迭代

清单之外还有一个运维视角的问题：事件流的健康度要能一眼看全。我们给事件总线做了三个看板：发件箱积压量（未发送消息数）、中继投递延迟（从事务提交到 MQ 确认的 P99）、消费滞后量（按 topic 和消费组）。三个指标任何一个连续五分钟恶化就告警。这套看板上线后，我们从"业务方发现数据不对才排查"变成了"滞后超标时就知道哪个消费组卡住"，故障发现时间从平均两小时降到五分钟。

## 最后的边界

领域事件不是万能胶。需要强一致的读后写（比如下单时必须立刻看到库存扣减），仍然用同步调用；只有"允许毫秒到秒级延迟"的副作用才值得事件化。另一个常见误区是把事件当数据同步管道：下游需要完整数据时应该调 API 或订阅 CDC，而不是把十兆的聚合塞进事件体。事件的边界是"通知事实"，不是"搬运状态"。

什么时候该退回同步调用，也值得一条明确边界。用户下单后立刻跳转到订单详情页，页面要显示最新状态，这种"读后写"场景用事件会有秒级延迟，用户会看到旧状态然后突然跳变，体验比同步调用差得多。我们的划分原则：写操作之后的立即读取，走同步调用或查询主库；其他所有下游的感知，走事件。这条线画清楚，团队就不再为"这个要不要发事件"反复争论。
