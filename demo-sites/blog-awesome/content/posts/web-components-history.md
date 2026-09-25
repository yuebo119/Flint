---
title: Web Components 编年史：从 2011 到标准落地
date: 2025-07-12
tags: [Web Components, 标准演进, 前端历史]
categories: [前端, Web 标准]
description: 按年代梳理 Web Components 的十五年：从概念提出、规范更迭到标准落地，以及它与前端框架从竞争到分工的完整脉络。
---

Web Components 是前端史上少有的"提出早、落地晚、争议大"的标准。2011 年就有人把它描绘成组件化的未来，此后它经历了 HTML Imports 被移除、规范一分为二、浏览器实现冰火两重天，最终在 2017 年之后悄悄成为设计系统的通用底座。这篇按年代讲这段历史，也讲清楚一件事：为什么它今天的位置既不是"取代框架"，也不是"无人使用"。

## 2011：概念的提出

Web Components 这个词最早出现在 Alex Russell 2011 年在阿姆斯特丹 Fronteers 大会上的演讲。他描述的图景很诱人：浏览器原生提供可复用、可封装、可组合的组件，不需要任何框架。当时的现实是 jQuery 插件满天飞，"组件"意味着约定俗成的 class 命名和一堆全局副作用。这个愿景足够动人，也足够遥远。

早期的 Web Components 是一整套提案，包含四块：Custom Elements（自定义元素）、Shadow DOM（影子 DOM，封装样式与结构）、HTML Templates（`<template>` 模板）和 HTML Imports（用 `<link rel="import">` 引入组件）。前两块延续至今，第四块已经埋进历史。

![Web Components 的四块拼图与各自的浏览器落地时间](/images/demo-7.svg)

## 2013 到 2015：Polymer 与 v0 规范

谷歌是最积极的推动者。2013 年 Polymer 项目预览发布，目标是把 Web Components 的能力包装成好用的库；2015 年 Polymer 1.0 正式发布。与此同时，浏览器开始实现第一版规范：Chrome 36（2014 年）支持 Custom Elements v0，`document.registerElement` 是当时的入口；Shadow DOM v0 随 Chrome 25 进入，带着 `<content>` 插入点这套后来被废弃的设计；`<template>` 元素也在这一时期落地，成为所有组件方案共用的"惰性文档片段"。

这一段是繁荣与混乱并存。繁荣在于生态真的转动起来了：Mozilla 的 X-Tag、Skate 等库纷纷出现，组件化第一次有了浏览器级别的答案。混乱在于 v0 规范带着明显的探索痕迹，`registerElement` 的返回值语义、Shadow DOM 的插入点语法都在试点，社区担心"学完就过时"。

## 2016 到 2018：v1 规范与真正的标准化

### 规范的推倒重来

转折点是规范重写。Custom Elements v1 用 `customElements.define()` 取代 `registerElement`，生命周期回调（`connectedCallback`、`disconnectedCallback` 等）固定下来；Shadow DOM v1 用 `attachShadow()` 和 `<slot>` 取代 v0 的设计；HTML Imports 则在 2019 年被 Chrome 正式移除，让位给 ES Modules。

浏览器落地的时间表大致是：Chrome 54（2016 年 10 月）率先支持 Custom Elements v1 与 Shadow DOM v1，Safari 10.1 和 Firefox 63（2018 年）跟进。加上 2017 年起各浏览器陆续支持原生 ES Modules（Chrome 61、Safari 10.1、Firefox 60），组件化所需的三块基石齐了[^1]。2018 年前后，社区开始称其为"Web Components 元年"。

| 能力 | Chrome | Safari | Firefox |
| --- | --- | --- | --- |
| Custom Elements v1 | 54 | 10.1 | 63 |
| Shadow DOM v1 | 53 | 10.1 | 63 |
| 原生 ES Modules | 61 | 10.1 | 60 |
| HTML Imports | 已移除（73） | 从未支持 | 从未支持 |

这张表也解释了 HTML Imports 的结局：它从诞生起就只有 Chrome 一家支持，标准化讨论最终判定 ES Modules 才是加载组件的正道，Imports 在 2019 年被移除。一次"被标准放弃"的方案，留下的教训是：浏览器厂商分歧巨大的特性，不要写进生产代码。

写法也彻底现代化。一个最小的自定义元素长这样：

```javascript
class MyCard extends HTMLElement {
  connectedCallback() {
    this.attachShadow({ mode: "open" }).innerHTML = `
      <style>
        :host { display: block; border: 1px solid #ddd; }
        h2 { margin: 0; font-size: 1.1rem; }
      </style>
      <h2><slot name="title">默认标题</slot></h2>
      <slot></slot>
    `;
  }
}

customElements.define("my-card", MyCard);
```

配合 `<template>` 声明结构，就是完整的组件开发体验：

```html
<template id="card-tpl">
  <style>
    .body { padding: 12px; }
  </style>
  <div class="body"><slot></slot></div>
</template>
```

Shadow DOM 解决了样式封存：组件内部样式不会外泄，外部样式也不会轻易渗入。CSS 有了 `::part()` 伪元素后，组件作者还能显式开放内部样式钩子，封装与可定制不再二选一。

## 2019 到 2021：Lit 与框架互操作

原生 API 好用但啰嗦，模板拼接、属性观测都要自己写。谷歌团队把 Polymer 积累的 lit-html 模板库独立出来，与 lit-element 合并为 Lit（2021 年发布 Lit 2）。Lit 的思路很清晰：只做"响应式加模板"这一层，不碰路由、状态管理，体积小到可以直接进任何页面。Stencil（Ionic 出品）则走了另一条路：写时编译成标准 Web Components，顺带产出框架绑定。

框架这边，互操作问题被摆上台面。Angular 推出 Angular Elements，把组件打包成自定义元素；React 对自定义元素的支持长期不完整，传属性还是传特性、事件命名怎么映射都要小心，直到 React 19 才明显改善。这段历史留下一个经验：Web Components 的价值恰恰在框架之外，跨框架共享的设计系统是它最稳的用例。

![Shadow DOM 封装与插槽分发示意](/images/demo-11.svg)

## 2022 至今：服务端渲染与新能力

Web Components 此前的短板在 SSR：Shadow DOM 是客户端行为，服务端吐不出带影子树的 HTML，SEO 和首屏都吃亏。声明式 Shadow DOM（Declarative Shadow DOM）补上了这一环，服务端可以直接在 HTML 里写 `<template shadowrootmode="open">`，浏览器解析时自动挂载影子根，水合后客户端代码无缝接管。与之配套的 Constructable Stylesheets 让样式表可以复用、共享，解决了多实例重复注入样式的浪费。

Form-associated custom elements（借助 `ElementInternals`）让自定义元素可以像原生表单控件一样参与表单校验和提交，这一度是 Web Components 最难补齐的拼图。再加上 `::part()`、CSS 自定义属性穿透，组件封装与主题定制的矛盾基本有解。

## 为什么框架赢了第一回合

2013 到 2018 年，Angular、React、Vue 拿下了绝大部分新项目，Web Components 却被主流业务边缘化。原因不在标准本身，而在生态位：框架提供的是全家桶（状态、路由、工具链、最佳实践），Web Components 提供的只是组件模型。业务团队要的是"今天就能开工"，不是"先自己组装一遍基础设施"。加上 v0 到 v1 的断裂、Safari 和 Firefox 长期的实现滞后，以及框架对自定义元素的兼容瑕疵，多重因素把 Web Components 挤到了"值得尊敬但不必采用"的位置。

React 与自定义元素的相处史是这段尴尬的缩影：React 把 props 映射为特性，而自定义元素的 API 常常是属性优先，数组和对象类型的 props 直接传不过去；事件要用 `addEventListener` 而不是 React 的合成事件系统。社区一度要靠包装库弥合，直到 React 19 才官方改善。这个教训后来被吸收进 Web Components 生态的设计：好的封装库（如 Lit）不试图伪装成框架，而是老实提供属性观察、模板和响应式。

## 今天谁在用

Web Components 的真实用户画像很稳定，主要是三类：跨框架分发的设计系统，一套组件同时供 React、Vue、甚至无框架页面使用；需要嵌入第三方页面的产品，客服 widget、广告组件、低代码平台的渲染器，宿主环境不可控；以及框架无关的微前端共享层。这些场景的共同点是"作者不知道消费者用什么框架"，恰好是 Web Components 的主场。反过来说，一个全公司统一技术栈的内部系统引入 Web Components，收益就相当有限。

## 生态与学习曲线

现在的开发体验已经接近"够用"：Lit 把模板和响应式补齐，声明式 Shadow DOM 打通 SSR，`::part()` 和 CSS 自定义属性解决主题定制，form-associated 元素补齐表单场景。剩下的短板集中在周边：组件库数量远不及框架生态，DevTools 对 shadow 树的展示仍有改进空间，TypeScript 对自定义元素的类型增强需要手写声明或者用社区工具生成。

学习曲线方面，一个 React 开发者上手 Lit 通常只要一两天，真正的门槛不在 API 而在心智切换：没有虚拟 DOM，没有状态管理，没有生命周期调度，一切更新都基于属性观察和模板重渲染。习惯了"状态驱动界面"的人，需要重新建立"属性即接口"的直觉。这个切换成本，也是团队引入前要诚实评估的部分。

## 与框架组件的分工

同样是"一个计数器组件"，框架实现和 Web Components 实现的差异不在代码量，而在边界：框架组件依赖运行时，Web Components 依赖平台。前者的能力上限由框架决定，后者的能力下限由浏览器保证。选型时问自己一个问题：这个组件需要离开我的框架生存吗？答案是需要，用 Web Components；答案是不需要，用框架组件更顺手。十五年历史最后沉淀下来的，就是这个朴素的分工。

> 一段标准的价值，往往要等生态追上来才显现。Web Components 等了六年，才等到原生 ESM 和声明式 Shadow DOM 把最后两块短板补上。

## 今天的定位：既不神话，也不看衰

回头看，Web Components 的十五年给出的答案是：

- **它是标准件，不是框架**：路由、状态、构建都不管，这些本来也不该它管。
- **它是跨框架的通用语言**：设计系统、嵌入式 widget、CMS 插件，凡是"不知道宿主用什么框架"的场景，它是默认选项。
- **它不再孤单**：Lit 补上开发体验，声明式 Shadow DOM 补上 SSR，主流框架对它的支持都在改善。

它没有杀死框架，正如 HTTP 没有杀死浏览器。把 Web Components 当作平台能力来用，在正确的场景（跨框架共享、框架无关的嵌入）发力，这份十五年的标准演进才算落到实地。规范入口见 <https://developer.mozilla.org/zh-CN/docs/Web/API/Web_components>，`<template>` 的用法见 <https://developer.mozilla.org/zh-CN/docs/Web/HTML/Element/template>，示例代码托管在 <https://example.com/web-components-history>。

[^1]: 原生 ES Modules 的落地时间各浏览器略有差异，Chrome 61（2017 年）与 Safari 10.1（2017 年）最早，Firefox 60（2018 年）稍晚，Edge 16（2017 年）切换 Chromium 内核后一并获得支持。
