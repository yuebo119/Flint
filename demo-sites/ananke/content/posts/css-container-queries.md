---
title: 容器查询与现代 CSS 布局：让组件自己决定长什么样
date: 2024-01-15
tags: [CSS, 响应式布局, 前端工程]
categories: [前端, CSS]
description: 容器查询让组件按所在容器的宽度而非视口宽度自适应。本文从语法、容器单位到实战降级，完整梳理容器查询的用法与常见坑。
---

过去十年，写响应式布局几乎等于写媒体查询：屏幕够宽就三列，不够宽就一列，视口是唯一的锚点。可组件一旦被复用进不同宽度的容器，这套锚点就开始失灵。同一个商品卡片，放在 760px 的首页推荐位应该图文横排，塞进 280px 的侧边栏却必须竖排，媒体查询对此无能为力，因为视口宽度根本没变。容器查询（Container Queries）终结了这种尴尬：组件终于可以询问自己所在的容器，而不是整个屏幕。

## 容器查询解决了什么问题

先看一个具体场景。电商后台的商品卡片同时出现在两个位置：首页推荐位（容器宽约 760px）和"猜你喜欢"侧栏（容器宽约 280px）。用媒体查询，你只能判断视口，无法区分这两种处境，最后不得不写出 `card--horizontal` 和 `card--vertical` 两个变体类，由调用方手动切换。容器查询把这份责任交还给组件自身：卡片向父容器要宽度，自己决定排布。

![同一组件在宽容器中横排、在窄容器中竖排](/images/demo-1.svg)

需要说明的是，容器查询不是媒体查询的替代品。判断页面级结构（导航折叠、栅格列数）仍然应该用媒体查询；容器查询负责的是组件级自适应。两者是分工关系，不是新旧关系。CSS Grid 的 `auto-fit` 配合 `minmax` 能解决一部分"内容多就多列"的问题，但它要求容器本身就是网格容器，且无法按宽度切换完全不同的内部结构，容器查询补的正是这块。

## 基本语法：三步开启容器查询

用法可以拆成三步：声明容器、写查询、在查询里改样式。

### 第一步：建立查询上下文

给父容器设置 `container-type`。最常用的值是 `inline-size`，表示只在行内轴（水平方向）上建立查询上下文；`size` 会同时建立块向查询，但它引入 `contain: size` 语义，要求容器高度确定，实践中容易踩坑，一般不用。

第二步，可以给容器起个名字，查询时精确指定查哪个容器：

```css
.card-list {
  container-type: inline-size;
  container-name: cardlist;
}
```

第三步，子元素用 `@container` 书写条件样式：

```css
@container cardlist (min-width: 400px) {
  .card {
    display: grid;
    grid-template-columns: 120px 1fr;
    gap: 16px;
  }
}
```

注意 `@container` 里写的是子元素的样式，而不是容器自身的样式。这是新手最容易迷路的地方：查询条件描述的是容器，作用对象却是容器内部的后代。

## 容器查询单位：cqw 与它的家族

除了布尔式查询，CSS 还提供了一组以 `cq` 开头的长度单位：`cqw`、`cqh`、`cqi`、`cqb`、`cqmin`、`cqmax`，分别对应容器宽度、高度、内联尺寸、块尺寸，以及两者的较小值与较大值。1cqw 等于容器宽度的 1%。

```css
.card-title {
  font-size: clamp(1rem, 4cqi, 1.5rem);
}
```

这行代码的意思是：标题字号随容器宽度缩放，最小 16px，最大 24px。过去做这件事要靠一堆断点，或者用 JavaScript 监听 `resize` 再改样式变量，现在一行 CSS 就能完成，而且不占用主线程。

## 容器查询与媒体查询的对比

| 维度 | 媒体查询 | 容器查询 |
| --- | --- | --- |
| 查询目标 | 视口或设备特征 | 最近的查询容器 |
| 作用范围 | 整个页面 | 单个组件及其子树 |
| 复用性 | 依赖页面上下文 | 组件自带，随处可用 |
| 典型场景 | 导航折叠、栅格列数 | 卡片、侧栏、弹窗内部排版 |
| 兼容起点 | IE9 | Chrome 105、Safari 16、Firefox 110 |

兼容性方面，主流浏览器在 2022 年底到 2023 年初陆续支持，至今覆盖率超过九成，MDN 的兼容性数据见 <https://developer.mozilla.org/zh-CN/docs/Web/CSS/CSS_containment/Container_queries>。存量项目可以放心使用，只要按下面的方式降级。

## 实战：一个自适应的文章卡片

HTML 结构保持干净，布局全部交给 CSS 自己决定：

```html
<article class="post-card">
  <img class="post-card__cover" src="/cover.jpg" alt="文章封面" />
  <div class="post-card__body">
    <h3 class="post-card__title">容器查询入门</h3>
    <p class="post-card__excerpt">让组件按容器宽度自适应排版。</p>
    <span class="post-card__meta">2024-01-15</span>
  </div>
</article>
```

CSS 用两条查询覆盖三档形态：默认竖排，容器超过 320px 图文并排，超过 560px 时放大标题：

```css
.post-card {
  container-type: inline-size;
  display: grid;
  gap: 12px;
}

@container (min-width: 320px) {
  .post-card {
    grid-template-columns: 96px 1fr;
  }
}

@container (min-width: 560px) {
  .post-card__title {
    font-size: 1.5rem;
  }
}
```

同一个 `.post-card`，放进首页信息流和放进相关文章侧栏，排布完全不同，而 HTML 一行都没改。调用方也不需要知道组件内部有几种形态。

## 兼容与降级

对不支持的浏览器，最稳妥的做法是用 `@supports` 包一层：

```css
@supports (container-type: inline-size) {
  .card-list {
    container-type: inline-size;
  }
}
```

不支持容器查询的浏览器会退回 `display: block` 的基础样式，至少保证内容可读。移动端 App 内嵌的旧版 WebView 是重灾区，如果目标用户里有大量旧 WebView，先用 `@supports` 兜底，再逐步迁移。

## 常见坑

- **容器不能查询自身**：`@container` 里只能写后代样式，改容器自身属性会造成循环依赖，规范直接禁止。
- **`container-type: size` 需要高度**：行内查询用 `inline-size` 就够；`size` 会让容器的高度不再由内容决定，高度不设就会塌陷。
- **匹配的是最近的容器**：嵌套容器时，子元素匹配离它最近的查询容器，用 `container-name` 可以锁定特定容器。
- **`container-type` 会创建包含上下文**：它等价于给容器加上 `contain: layout style`，某些依赖溢出的效果（如未被裁剪的阴影）可能受影响，需要实测。

## 上手指引

- [ ] 找出项目里为了适配多个位置而存在的 `--horizontal`、`--compact` 变体类
- [ ] 给这些组件的父容器补上 `container-type: inline-size`
- [ ] 把变体类改写成 `@container` 规则，删除调用方的传参逻辑
- [ ] 用 `@supports` 包住新代码，验证旧浏览器下的基础样式
- [ ] 在 Chrome DevTools 的元素面板里检查容器标记，确认匹配结果符合预期

> 容器查询属于 CSS Containment Module Level 3，与 `content-visibility`、`contain` 同属一个规范家族。本文示例页面托管在 <https://example.com/container-query-demo>，可以直接改容器宽度观察效果。

## 容器样式查询与命名容器

容器查询不止查宽高。规范还支持样式查询（style queries）：当自定义属性等样式值满足条件时生效。

```css
.card-list {
  container-name: cardlist;
  --variant: normal;
}

@container cardlist style(--variant: featured) {
  .card {
    border-color: gold;
  }
}
```

这个能力让主题变体也能随容器走：父容器声明 `--variant: featured`，内部卡片自动切换到高亮样式，调用方不需要加任何类名。不过样式查询的浏览器支持比尺寸查询晚，落地前要确认目标环境。另外要记住命名容器的解析规则：查询条件里不写 `container-name` 时匹配最近的容器，写了名字就只匹配同名容器，嵌套容器场景下这是唯一可控的手段。

## 在组件库里落地

组件库是容器查询收益最大的地方。把每个组件的最外层容器设为查询容器，对外只暴露"放进去"的语义，调用方无需了解内部形态。我们在设计系统里落地时的约定是：组件的根元素自带 `container-type: inline-size` 和 `container-name: <组件名>`，文档里标注"支持的最小容器宽度"，视觉回归测试按容器宽度分档截图。这套约定让同一套卡片组件同时服务于后台表格内嵌预览、首页大卡和移动端全宽三种形态，相关的适配代码从六个变体类收敛成了零个。

## 什么时候不要用容器查询

容器查询不是万能药。三种场景用了反而更乱：组件只有一种形态，直接用媒体查询或干脆写死；容器宽度恒等于视口宽度，退化成就媒体查询；需要在多个断点之间共享复杂计算逻辑的布局，用 CSS Grid 的 `minmax` 配合容器单位更直接。判断标准很简单：组件是否真的会被放进两种以上不同宽度的容器？答案是否，就别引入这层间接。每多一层抽象，就多一份认知成本，容器查询也不例外。

## 小结

容器查询把响应式的粒度从视口缩小到组件，补上了组件化开发里缺失的一环。它没有推翻媒体查询，而是让两把尺子各归其位：页面结构看视口，组件内部看容器。配合 `cqi` 单位和 `clamp()`，大量原本需要 JavaScript 参与的适配逻辑可以压缩成纯 CSS，性能与可维护性双双受益。
