---
title: "DDD 战术模式实践：把一个订单模块拆干净"
date: 2024-05-10
tags: ["领域驱动设计", "架构", "重构"]
categories: ["后端", "架构"]
description: "用一次订单模块重构完整走一遍 DDD 战术模式：实体与值对象、聚合边界、领域服务、仓储与领域事件，附可对照的 C# 代码与重构前后数据。"
---

去年我接手过一个订单模块，一个 `OrderService` 类三千行，六十多个方法，改一条优惠规则要回归三天。测试倒是齐的，只是每个用例都在构造数据库快照，跑一遍二十分钟，没人敢跑。我们用六个星期做了一次以 DDD 战术模式为线索的重构，代码量少了四成，回归时间降到四分钟。这篇文章按实际操作顺序把整个过程走一遍，代码做了简化，但结构和判断标准是原样。开始之前先交代背景数据：重构前这个模块有 3120 行、63 个公开方法、11 个直接依赖的仓储，圈复杂度超过 20 的方法有 9 个。这些数字不是要批判谁，而是说明一件事：当模块长到这种规模，靠"小心一点"已经不管用，必须靠结构来约束。

![订单模块重构前的代码地图](/images/demo-2.svg)

## 第一步：区分实体与值对象

动手之前先回答一个问题：这个对象有没有身份。判断标准很直接：如果两个对象所有属性相同，它们能互相替换吗？能，就是值对象；不能，就是实体。订单有订单号，订单号不同就是两件事；地址没有身份，两个内容相同的地址没有任何区别，应该整体替换而不是逐字段更新。

```csharp
// 值对象：不可变，按属性值判等
public sealed record Address(string Province, string City, string Detail, string Receiver, string Phone);

public sealed record Money(decimal Amount, string Currency)
{
    public static readonly Money Zero = new(0m, "CNY");

    public static Money operator +(Money a, Money b)
    {
        if (a.Currency != b.Currency) throw new InvalidOperationException("币种不一致");
        return new Money(a.Amount + b.Amount, a.Currency);
    }
}

// 实体：有身份，生命周期内可变；Order 同时是聚合根
public class Order
{
    public Guid Id { get; private set; }
    public OrderStatus Status { get; private set; }
    private readonly List<OrderItem> _items = new();
    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    // 不变式一：明细金额之和等于订单总额，总额不落库，永远由明细算出
    public Money Total => _items.Aggregate(Money.Zero, (sum, i) => sum + i.Subtotal);

    // 不变式二：非草稿状态不能改明细，守卫集中在聚合内部
    public void AddItem(ProductSnapshot product, int quantity)
    {
        if (Status != OrderStatus.Draft)
            throw new InvalidOperationException("非草稿状态的订单不能修改明细");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        var existing = _items.FirstOrDefault(i => i.ProductId == product.Id);
        if (existing is null) _items.Add(new OrderItem(product, quantity));
        else existing.Increase(quantity);
    }
}
```

值对象不可变这条纪律看起来麻烦，实际回报很大：消除了"地址改了一半"的中间状态，也消除了大量 null 判断，因为属性要么整体给，要么不给。重构前订单表里躺着 province、city、detail、receiver、phone 五列，任何代码都能改其中一列，出现过"改了城市忘了改区县"的脏数据。收敛成 Address 值对象之后，更新地址变成整体替换，编译器替你保证完整性。这类把散落原始值收敛成概念的做法，书里的名字叫原始类型偏执，实践中记住一句话就够了：如果几个字段总是一起出现、一起变化，它们就属于同一个概念。

收敛按四步走，每一步都可以单独验证：

1. 列出实体上所有字段，逐个问"它有没有独立身份"
2. 把无身份且总是一起变化的字段合成一个值对象
3. 值对象属性全部只读，修改等于整体替换
4. 实现按值判等，测试里直接断言两个相等

---

## 第二步：用聚合划定一致性边界

重构前订单明细可以独立于订单被任意修改，导致出现过"删了明细但总额没更新"的事故。聚合就是把一组必须一起保持一致的对象圈起来，只留一个入口。订单是聚合根，明细只能通过订单修改，不变式有两条：明细金额之和等于订单总额；已支付的订单不能再改明细。上面代码里的 `AddItem` 就是把这两条不变式收进聚合的写法：入口只有一个，状态不对就抛异常，明细变了总额自动重算，调用方没有机会破坏一致性。

> 聚合不是对象图，是一致性边界。圈多大，取决于哪些东西必须一起对。

聚合要小，这是被说得最多也最容易被违反的一条。判断办法：把候选成员逐个拿掉，问"少了它，不变式还成立吗"。成立就拿掉。我们的订单聚合一开始把物流轨迹也圈了进去，按这个标准一问，轨迹有自己的生命周期和不变式，应该独立成聚合。

聚合之间靠 ID 关联，不靠对象引用。订单里存商品 ID 而不是商品对象，展示时按 ID 批量查询。这样做有两个好处：加载订单不再级联出整棵对象树，保存一个聚合不再牵动另一个聚合的状态。实践中判断边界是否合理有个土办法：打开仓储的加载日志，如果一次加载订单要出五条 SQL、关联三张表，多半是聚合画大了；如果保存订单时 Hibernate 级联更新了别的表，一定是聚合之间的关系建错了。

---

## 第三步：领域服务与应用服务各司其职

重构前"计算优惠价"这个逻辑散落在三个方法里，因为它同时涉及商品、优惠券、会员等级三个聚合，放哪个实体都不合适。这种跨聚合、又不适合塞进某个实体的规则，正是领域服务的位置。领域服务最常见的误用，是把它当成"放不下的方法都扔这里"的垃圾桶。判断标准只有一条：这个方法是否只依赖传入的参数和领域知识？如果需要查数据库才能算出结果，它要么是应用服务在干编排的活，要么说明规则依赖的聚合根本没加载全。我们的定价服务第一版就顺手查了会员等级表，后来把 Member 聚合作为参数传进来，领域服务重新变回无副作用的纯计算，单测从要 mock 三个仓储变成直接 new。记住：领域服务里出现 Repository，就是越界的信号。

```csharp
// 领域服务：只做业务规则计算，无副作用
public interface IPricingService
{
    Money Calculate(Order order, Member member, IReadOnlyList<Coupon> coupons);
}

// 仓储只面向聚合根：明细不能独立存取
public interface IOrderRepository
{
    Task<Order?> FindAsync(Guid id, CancellationToken ct = default);
    Task<Order> FindDraftAsync(Guid id, CancellationToken ct = default);
    void Add(Order order);
    Task SaveAsync(Order order, CancellationToken ct = default);
}

// 应用服务：编排事务、调用领域服务、发布事件
public class OrderAppService
{
    public async Task<Guid> PlaceOrderAsync(PlaceOrderCommand cmd)
    {
        var order = await _orderRepository.FindDraftAsync(cmd.OrderId);
        var price = _pricingService.Calculate(order, cmd.Member, cmd.Coupons);
        order.Place(price);
        await _orderRepository.SaveAsync(order);
        await _unitOfWork.CommitAsync();
        await _publisher.PublishAsync(new OrderPlaced(order.Id, price));
    }
}
```

---

## 第四步：仓储只面向聚合根

仓储接口是聚合的对外契约，设计仓储集合时只暴露聚合根，定义就是上一段代码里的 `IOrderRepository`。重构前代码里有 `IOrderItemRepository`，任何代码都能改明细，不变式形同虚设。收敛之后，明细只能随订单整体加载、整体保存，违规修改在编译期就被挡住了。

还有一条配套纪律：仓储不自己提交事务，事务边界由应用层的工作单元统一控制。重构前每个仓储方法自带 `SaveChanges`，出现过"订单保存成功、积分保存失败"的半截状态，对账对了一周。统一事务边界之后，这类问题在代码结构上就不再可能发生，测试里也能用内存实现的工作单元快速回滚。

## 第五步：用领域事件摘掉副作用

"下单成功"之后要发确认邮件、加积分、通知仓库预打包。这些逻辑原来全写在 `PlaceOrder` 方法里，每加一个副作用，主流程就厚一圈，测试要多 mock 一个服务。领域事件把副作用改成订阅：主流程只负责发布事实，谁关心谁自己来。

事件处理器的失败策略要分级，这是吃过亏才学会的。发确认邮件失败，记日志加重试即可，不影响主流程；加积分失败要进补偿队列；但如果某个处理器要修改另一个聚合的状态，就要停下来问一句：这真的该用事件，还是本该是一条显式的命令？我们踩过这个坑：积分扣减放在事件里做，邮件服务抛异常导致整个事件链重放，积分被重复扣了三次。后来每个事件处理器配独立的失败策略，互不影响，重放也安全。

```csharp
public sealed record OrderPlaced(Guid OrderId, Money PaidAmount, Guid MemberId);

public class SendConfirmationEmailHandler : IEventHandler<OrderPlaced>
{
    public async Task HandleAsync(OrderPlaced evt)
    {
        await _mailer.SendAsync(evt.MemberId, "订单确认", BuildBody(evt));
    }
}
```

---

## 重构效果对比

| 指标 | 重构前 | 重构后 |
| --- | --- | --- |
| 最大类行数 | 3120 | 480 |
| 单测执行时间 | 20 分钟 | 3 分 50 秒 |
| 改一条优惠规则的回归范围 | 全量 | 定价相关 3 个类 |
| 直接改订单明细的入口 | 6 处 | 0 处 |

## 三个容易踩的坑

### 坑一：聚合做得太大

把能加的都加进去，加载一个订单要查八张表，保存时并发冲突不断。聚合大小的判断办法前面说过：逐个拿掉候选成员，不变式还成立就拿掉。拿不准时往小了画，两个小聚合之间的协作成本，远小于一个大聚合的加载和冲突成本。

### 坑二：给纯 CRUD 模块硬套 DDD

如果业务规则只有"增删改查"，贫血模型加服务层就是最合适的方案，强行造聚合只会增加跳转层级。判断这块要不要 DDD，看的是不变式的数量：一条都没有，老老实实 CRUD；有三条以上互相牵制的规则，才值得动用聚合。

### 坑三：为了模式而模式

战术模式是工具，判断标准永远是：这次改动让不变式更清晰了吗？如果答案是否定的，再"标准"的写法也不该进代码库。

最后是一个更隐蔽的坑：轻视统一语言。重构后我们和业务方开会，说"聚合"对方不懂，但说"一单"和"一单里的行"立刻对齐。术语是给团队用的，不是给架构图用的，如果业务方在评审时说"这不是我想要的"，再标准的模型也没用。

领域驱动设计的价值不在于术语齐全，而在于它逼着你回答那个最容易被跳过的问题：这块业务里，什么东西必须始终保持一致。想清楚这个，代码结构自然就出来了。[^1]

[^1]: Eric Evans, "Domain-Driven Design: Tackling Complexity in the Heart of Software", https://zh.wikipedia.org/wiki/领域驱动设计
