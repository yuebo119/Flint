---
title: 前端监控体系搭建案例：从报错收集到性能告警
date: 2025-10-09
tags: [前端监控, 工程化, 案例分析]
categories: [前端, 工程化]
description: 一个电商前台的前端监控落地案例：错误采集的四类来源、性能指标怎么选、上报为何要批量加 sendBeacon，以及告警阈值怎么定才不扰民。
---

这套监控体系服务的是一个日活百万级的电商前台，前端单页应用，React 技术栈，部署在 CDN 之后。重构契机是一次大促：白屏投诉集中爆发，而团队手里只有后端接口错误率，说不清问题在前端还是后端。半年后回看，监控体系解决的不只是"看见错误"，而是把前端从"甩锅环节"变成了"有数据的一方"。这篇复盘完整过程。

## 目标与边界

立项时定下三条边界，事后证明是这套体系不跑偏的原因：

1. **只采集能触发行动的数据**。埋点很诱人，但每个埋点都是存储和带宽成本，采了没人看的字段一律不上。
2. **生产与体验优先，开发体验其次**。宁可少一个炫酷图表，也要保证上报本身不拖慢页面。
3. **隐私红线前置**。用户手机号、地址、搜索词一律不采集，上报前脱敏，法务先审再过。

## 错误监控：四类来源，各有坑

前端错误不是"try-catch 包一下"那么简单。最终采集的是四类来源：

- **JS 运行时错误**：`window.addEventListener("error")` 捕获，注意它拿不到脚本跨域时的详细堆栈，需要给 script 标签加 `crossorigin="anonymous"` 并在 CDN 配 CORS 头。
- **未处理的 Promise 拒绝**：`unhandledrejection` 事件，异步接口失败、懒加载 chunk 超时都在这里，是单页应用最高频的错误来源。
- **资源加载失败**：同样在 `error` 事件的捕获阶段监听 window，但目标限定为 `img`、`script`、`link` 标签，用来发现 CDN 故障和第三方脚本雪崩。
- **React 错误边界**：Error Boundary 捕获渲染期错误，附带组件栈，是定位"哪个页面哪个模块炸了"的关键。

骨架代码不到四十行：

```javascript
const errors = [];

window.addEventListener(
  "error",
  (event) => {
    if (event.target !== window) {
      errors.push({ type: "resource", url: event.target.src || event.target.href });
    } else {
      errors.push({ type: "runtime", message: event.message, stack: event.error?.stack });
    }
  },
  true,
);

window.addEventListener("unhandledrejection", (event) => {
  errors.push({ type: "promise", message: String(event.reason) });
});
```

### source map 是分水岭

`event.error?.stack` 是后续所有定位工作的基础，所以 source map 上传必须做进构建流程，否则线上堆栈是一堆压缩后的单字母变量，等于没采。

## 性能监控：指标选少，选准

性能指标见过很多团队一上来全采，结果告警没人看。我们的筛选标准是"这个数变了，用户能感觉到"：

| 指标 | 含义 | 采集方式 | 告警线 |
| --- | --- | --- | --- |
| LCP | 最大内容绘制 | PerformanceObserver | P75 超过 2.5 秒 |
| CLS | 累积布局偏移 | PerformanceObserver | P75 超过 0.1 |
| INP | 交互到下一次绘制 | PerformanceObserver | P75 超过 200 毫秒 |
| 接口错误率 | 前端视角的请求失败 | 包装 fetch/XHR | 超过 0.5% |
| 白屏率 | 根节点无内容 | 关键容器存在性检查 | 超过 0.1% |

采集统一走 PerformanceObserver，不监听 scroll、resize，避免自己成为性能问题：

```javascript
const observer = new PerformanceObserver((list) => {
  for (const entry of list.getEntries()) {
    if (entry.name === "largest-contentful-paint") {
      report("perf", { metric: "lcp", value: entry.startTime });
      observer.disconnect();
    }
  }
});
observer.observe({ type: "largest-contentful-paint", buffered: true });
```

白屏检查是土办法但极有效：页面加载完成后两秒，检查根容器是否有子节点、首屏关键图片是否加载完成，没有就上报白屏事件。大促那次事故，第一个报警信号就来自这里，比用户投诉早了三分钟。

![错误、性能、行为三类数据的采集与上报链路](/images/demo-8.svg)

## 行为链路：低成本还原现场

只采性能指标，很多问题仍然解释不了。补了三条低成本链路：页面访问（PV，带来源和路由）、关键点击（加购、结算按钮，记元素路径不记内容）、接口监控（包装 `fetch`，记录 URL、状态码、耗时，不记参数）。三条数据用同一个 traceId 串起来，就能回答"这个用户白屏前点了什么、哪个请求卡住了"。

## 上报设计：批量、可靠、别阻塞

上报设计的核心矛盾是实时性与页面性能。最终方案是三件套：

1. **内存队列加批量发送**：数据先进队列，攒够 10 条或 5 秒超时再发，把 N 个请求合成 1 个。
2. **`navigator.sendBeacon` 兜底**：页面卸载时（`visibilitychange` 变为 hidden）用 sendBeacon 发送，它不阻塞页面关闭，可靠性高于同步 XHR。
3. **失败重试加本地降级**：发送失败先进 localStorage，下次访问补发，防止关键错误随刷新丢失。

`sendBeacon` 的细节参考 <https://developer.mozilla.org/zh-CN/docs/Web/API/Navigator/sendBeacon>，PerformanceObserver 的完整 API 见 <https://developer.mozilla.org/zh-CN/docs/Web/API/Performance_API>。注意 sendBeacon 有 payload 大小限制（通常 64KB），批量队列要设上限，超限直接分批发。

<details>
<summary>采样策略：为什么全量采样反而更贵</summary>

性能数据全量上报，存储成本按月线性增长，而 99% 的数据只用来印证"一切正常"。最终策略是：错误全量（每个都可能是一次事故），性能指标按 10% 采样，但触发告警阈值的样本自动升级为全量保存。配合用户 ID 哈希分桶，保证同一个用户始终落在同一桶，避免统计偏差。
</details>

## 告警：宁可漏报，不要狼来了

告警规则定得保守，两条原则：阈值必须是统计值（P75、环比）而不是单次采样；告警必须能对应到一个动作。上线初期定了十二条规则，两周后砍到五条，全是没人处理的噪音。最终保留的五条：JS 错误率突增、白屏率超线、LCP P75 连续三天劣化、接口错误率超线、关键接口超时率上升。每条都绑定了值班人，没绑定人的规则不允许上线。

## 复盘：这套体系改变了什么

半年后的复盘会上有三个可见的变化。第一，故障定位时间从"小时级"降到"分钟级"，白屏、接口失败、资源雪崩都有对应数据，不再互相猜。第二，前端开始参与大促稳定性保障，LCP 和 INP 成了发版门禁的一部分。第三，也是最意外的，监控数据反向推动了体验优化：发现结算页 INP 偏高后做了输入防抖和渲染拆分，转化率提升了 0.8 个百分点。

教训同样明确：source map 上传要最早做，它是错误监控从"有数据"到"能定位"的分水岭；埋点上线前先想"谁会看这个数"，答不上来就砍掉；隐私审核一定要前置，补做一遍比重做一遍便宜。

## 看板与日常运营

监控的价值一半在采集，一半在运营。我们最终固化的看板只有三块：稳定性看板（错误率、白屏率、接口错误率，按版本对比）、性能看板（LCP、CLS、INP 的 P75 分位，按页面和版本下钻）、发布看板（每次发版后的指标环比，异常自动标红）。每天晨会过一遍发布看板，每周过一次性能周报。看板设计的教训是"少而准"：十五个图表的看板等于没有看板，没人能在晨会上读完，也没人知道该对哪张图负责。

## 成本账

最后算一笔账，供类似规模的团队参考：全量错误加一成采样性能，日均上报约两百万条，压缩加批量后日均流量不到 2GB；存储按 90 天保留，热数据 30 天；source map 单独存储，保留期与日志对齐。人力上，一期建设约两人月，之后每月约两人天维护，主要是加规则和跟告警。这个成本相对于一次大促事故的损失，是笔划算的买卖。但如果你的产品日活只有几千，建议直接用成熟 SaaS 方案，自建的成本摊不薄。

## 诚实的缺口

复盘也要说没做的部分：会话录制（session replay）因为隐私评审复杂一直没上，复杂交互 bug 的还原仍靠用户描述；真实用户监控覆盖了桌面和移动浏览器，但 App 内嵌 WebView 的数据单独隔离，还没打通；告警只做到了"指标超线"，基于日志聚类的异常检测（同类错误突然增多）还在试点。这些缺口写在文档里，比假装完美更有价值，下一个季度的计划也从这份清单里来。

> 监控体系的及格线不是"能看"，而是"能在三分钟内回答：哪里坏了、影响了谁、要不要回滚"。

## 小结

前端监控体系的重点不是采集多少数据，而是形成"采集、定位、告警、修复"的闭环。错误四类来源、性能五个指标、批量加 sendBeacon 的上报、保守的告警阈值，这套组合在我们的场景里跑得住，也值得按你的实际规模裁剪。示例代码托管在 <https://example.com/frontend-monitoring-case>。
