---
title: 深扒 Kubernetes 调度：一个 Pod 怎样被安排到合适的节点
date: 2024-07-08
tags:
  - Kubernetes
  - 调度
  - 云原生
categories:
  - DevOps
description: 调度器是 Kubernetes 的心脏。本文从一次调度的完整旅程讲起，拆解过滤、打分与绑定三个阶段，并说明亲和性、污点与 QoS 在调度中扮演的角色。
---

![调度器工作流程示意](/images/demo-2.svg)

在 etcd 里创建一条 Pod 记录，几毫秒后它就变成了某个节点上运行的容器。中间发生了什么？多数人知道"有个叫 kube-scheduler 的组件在负责"，但一旦 Pod 卡在 Pending，或者负载分布明显不均，没有对调度过程的了解就只能靠猜。这篇文章把调度器拆开讲清楚。相关背景可以参考[维基百科的 Kubernetes 条目](https://zh.wikipedia.org/wiki/Kubernetes)。

## 调度器的位置：输入与输出

kube-scheduler 是控制平面的一个独立组件[^1]，它的工作可以用一句话概括：**为每个尚未绑定节点的 Pod，从集群所有节点里挑出一个最合适的，然后写一条绑定关系**。

- 输入：未调度的 Pod（spec.nodeName 为空）、集群节点列表、节点的资源余量与标签。
- 输出：一条 Binding 对象，Pod 的 spec.nodeName 被填上，随后对应节点上的 kubelet 才会拉镜像、起容器。

注意调度器**不负责把容器跑起来**。它只做决策，执行归 kubelet。理解这个边界很重要：调度失败表现为 Pending，而镜像拉取失败、探针失败都发生在调度完成之后，两者的排查路径完全不同。

## 一次调度的完整旅程

调度一个 Pod 大致经过三个阶段：过滤、打分、绑定。每个阶段都由一组插件完成，插件通过调度框架注册。

### 过滤：先排除不能跑的节点

过滤阶段回答"这个节点能不能跑这个 Pod"，输出一个节点候选集。常见的过滤规则包括：

- **资源余量**：节点的 allocatable 减去已分配请求，是否还装得下 Pod 的 requests；
- **节点选择器与亲和性**：nodeSelector 精确匹配、nodeAffinity 的 required 规则必须全部满足；
- **污点与容忍**：节点带有 NoSchedule 污点时，只有容忍该污点的 Pod 才能上去；
- **其他**：端口冲突、卷的拓扑限制、Pod 拓扑分布约束等。

过滤是"一票否决"：任何一条硬性规则不满足，节点直接出局。如果所有节点都被过滤掉，Pod 保持 Pending，事件里会留下 FailedScheduling 记录，通常会写明是哪条规则淘汰了最后一个候选。

过滤阶段还有一个隐藏杀手叫**资源碎片**：每个节点余量都够，但没有一个节点装得下整个 Pod。十个节点各剩 600 MB，一个要 1 GB 的 Pod 就调度不上去，而集群总余量高达 6 GB。这种"看着有空、实际放不下"的局面，靠调度器无解，需要从源头治理：给节点按规格分组（通用型、计算型、内存型），让 Pod 的 requests 对齐节点规格，而不是让所有 Pod 抢同一批机器。调度器只是执行者，资源规划的责任在平台团队。

### 打分：在候选集里挑最优

通过过滤的节点进入打分阶段，每个插件给 0 到 100 的分，加权求和后得分最高的节点胜出。常见的打分维度：

| 插件 | 打分依据 | 效果 |
| --- | --- | --- |
| NodeResourcesFit | 节点资源利用率 | 倾向把 Pod 放到更空的节点上，均衡负载 |
| ImageLocality | 节点是否已有所需镜像 | 命中已有镜像的节点加分，省一次拉取 |
| InterPodAffinity | Pod 间亲和/反亲和 | 满足亲和倾向的加分 |
| PodTopologySpread | 拓扑域内分布均匀度 | 惩罚已经拥挤的拓扑域 |
| TaintToleration | 容忍污点的程度 | 越"愿意"用带污点节点的 Pod 越加分 |
| NodeResourcesBalancedAllocation | CPU 与内存占比的均衡 | 避免单资源耗尽的节点 |

打分是"择优录取"：多个候选都满足硬性条件时，用加权得分挑最合适的。理解了这一点，很多看似玄学的调度结果就有了 explanation：Pod 落到某台机器，不一定是那里最空，可能只是那台机器上已经有这个镜像，ImageLocality 插件加了分。想验证这类猜测，可以打开调度器的 verbose 日志，逐插件看打分明细，比反复调整配置盲猜高效得多。

> 打分插件的存在意味着：Pod 最终落在哪台机器，是一套可解释的加权计算的结果，不是随机抽签。看不懂结果时，去看日志里的分数，别去改配置碰运气。

打分阶段还能做**抢占**：当一个高优先级 Pod 找不到节点时，调度器会挑选牺牲者，驱逐低优先级 Pod，腾出资源。抢占是最后手段，会真实驱逐正在运行的容器，生产环境需要配合 PodDisruptionBudget 评估影响。

### 绑定：写下决定

最高分节点确定后，调度器向 API Server 发送 Binding 对象。绑定是异步的，且可能失败（例如节点刚好失联），失败时 Pod 回到未调度队列重试。整个过滤加打分的过程通常在一两百毫秒内完成，集群规模越大，单次调度越慢，这也是为什么万节点集群需要调度器性能调优。

## 亲和性、污点与 QoS：约束的三种写法

日常最常用的三种约束，写法与语义各有侧重：

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: web-with-constraints
spec:
  nodeSelector:
    disktype: ssd
  affinity:
    nodeAffinity:
      requiredDuringSchedulingIgnoredDuringExecution:
        nodeSelectorTerms:
          - matchExpressions:
              - key: kubernetes.io/arch
                operator: In
                values: ["amd64"]
      preferredDuringSchedulingIgnoredDuringExecution:
        - weight: 80
          preference:
            matchExpressions:
              - key: zone
                operator: In
                values: ["zone-a"]
    podAntiAffinity:
      requiredDuringSchedulingIgnoredDuringExecution:
        - labelSelector:
            matchExpressions:
              - key: app
                operator: In
                values: ["web"]
          topologyKey: kubernetes.io/hostname
  tolerations:
    - key: "dedicated"
      operator: "Equal"
      value: "gpu"
      effect: "NoSchedule"
  containers:
    - name: web
      image: registry.example.com/web:1.4.2
      resources:
        requests:
          cpu: "500m"
          memory: "512Mi"
        limits:
          cpu: "1"
          memory: "1Gi"
```

三个要点：

1. `requiredDuringScheduling...` 是硬约束，`preferred...` 是软偏好，满足不了软偏好只会减分，不会阻止调度。
2. `IgnoredDuringExecution` 的含义是：调度完成后节点标签变了，Pod 不会被重新调度。想要"运行中也要满足"，需要 `RequiredDuringSchedulingRequiredDuringExecution`，但它影响驱逐逻辑，慎用。
3. 反亲和 + topologyKey 是保证副本跨节点分散的标准写法，topologyKey 用 `kubernetes.io/hostname` 就是强制每个节点最多一个副本。

## 实战排查：Pod 一直 Pending

遇到 Pending，按固定顺序查，比逐个猜快得多：

```bash
# 1. 看事件，绝大多数 Pending 的原因直接写在里面
kubectl describe pod <pod-name> | tail -20

# 2. 看节点余量，requests 总和是否已经超过 allocatable
kubectl describe nodes | grep -A 10 "Allocated resources"

# 3. 看污点与容忍是否匹配
kubectl get nodes -o json | jq '.items[].spec.taints'
```

最常见的三类原因：资源不足（扩容或调低 requests）、污点未容忍（给 Pod 加 toleration）、亲和条件写死了一个不存在的标签（修正标签或规则）。第四类不那么直观：**节点压力导致 kubelet 上报的状态过期**，调度器拿到的是几分钟前的快照，表现为"明明有余量却调度不上"，重启 kubelet 或检查节点心跳通常能恢复。

## 资源请求、QoS 与驱逐

调度器只看 requests 做决策，而 requests 和 limits 的配比还决定了 Pod 的服务质量等级，这个等级在节点资源紧张时决定谁先被驱逐：

| QoS 等级 | 判定条件 | 被驱逐优先级 |
| --- | --- | --- |
| Guaranteed | 每个容器的 requests 等于 limits | 最低，最后被驱逐 |
| Burstable | 至少一个容器配了 requests，但不全相等 | 中等，按超售程度排序 |
| BestEffort | 完全没配 requests 和 limits | 最高，最先被驱逐 |

推论很直接：**核心服务的 CPU 和内存都应该把 requests 和 limits 配成相等**，拿到 Guaranteed 等级，节点吃紧时最后一个才轮到它。反过来，把 limits 配得远高于 requests 看似"留足余量"，实际上会让 Pod 掉到 Burstable，还增加被 OOMKill 的概率，因为 limit 才是内存超用的硬顶。

```bash
# 查看节点上各 Pod 的 QoS 等级与实际占用
kubectl get pods -A -o json | jq -r '
  .items[] |
  "\(.metadata.namespace)/\(.metadata.name)  qos=\(.status.qosClass)"'
```

## 多个调度器与自定义扩展

一个集群里可以跑多个调度器，各管一类负载。做法是在 Pod 上指定 `schedulerName`，为关键业务单独部署一个调度器实例，与默认调度器隔离：默认调度器被海量 Pod 拖慢时，关键业务不受影响。再往前一步，可以利用调度框架的扩展点写自己的插件，比如按 GPU 拓扑亲和调度、按网络时延打分。扩展的代价是维护成本：插件要跟着 Kubernetes 版本升级重新验证，小团队一般不划算，大团队和云厂商才有必要。

---

## 小结

调度器做的事可以压缩成三句话：**过滤决定候选集，打分决定谁最优，绑定只是记录结果**。过滤规则排查 Pending，打分插件解释"为什么跑在那台机器上"，而抢占和拓扑分布决定了集群的韧性。读懂了这套机制，遇到负载不均、Pod 一直 pending、扩容不生效的问题，就有了固定的排查路径，而不是改配置碰运气。

最后留一个进阶入口：调度器的所有默认插件都可以在启动参数里调节权重甚至关闭，调度框架的扩展点文档是官方资料里信息密度最高的一篇。想真正把调度玩明白，值得在测试集群里亲手改一次打分权重，再观察 Pod 的分布变化。一次动手，胜过读十篇原理文章。

顺带回答一个高频疑问：**改了节点标签，已经在跑的 Pod 会被重新调度吗**。默认不会，这正是 `IgnoredDuringExecution` 的含义，调度决策发生在 Pod 创建那一刻，之后节点侧的变化只影响新 Pod。想让存量 Pod 也满足新约束，需要驱逐重建，可以用 Descheduler 这类工具定期扫描并驱逐"放错位置"的 Pod，让它重新走一遍调度流程。

[^1]: 控制平面的标准组件之一，历史上曾以 kube-scheduler 独立进程运行，如今也可以按调度框架扩展为自定义调度器，与默认调度器并存。
