---
title: React 渲染机制深扒：从 JSX 到屏幕，中间发生了什么
date: 2024-04-10
tags: [React, 渲染机制, 前端框架]
categories: [前端, React]
description: 深扒 React 渲染链路：render 与 commit 两阶段、Fiber 可中断调和、自动批处理与并发特性，以及定位多余渲染的排查顺序与常见误解。
---

写 React 的人几乎都会经历同一个困惑：我明明只改了一个 state，为什么控制台里三个组件的 render 函数都跑了？要回答这类问题，得先把"渲染"这个词拆开。React 里的渲染不等于浏览器重绘，更不等于操作 DOM。它是一条分成两段的流水线：先在内存里算出"界面应该是什么样"，再把差异真正落到 DOM 上。本文按这条流水线走一遍。

## 渲染不是"更新 DOM"

JSX 只是语法糖。`<div className="a">hi</div>` 编译后是 `React.createElement("div", { className: "a" }, "hi")`，产出一个普通的 JavaScript 对象，也就是 React 元素（element）。元素描述界面，不可变，创建成本极低。真正干重活的是 Fiber 节点，它是 React 内部的工作单元，一棵通过链表串起来的树，每个 Fiber 保存了组件实例、状态、副作用标记，以及指向父、子、兄弟节点的引用[^1]。

![render 阶段可中断，commit 阶段不可中断](/images/demo-2.svg)

## 两个阶段：render 与 commit

一次更新从 `setState` 开始。React 从上到下遍历 Fiber 树，执行组件函数（类组件则是 render 方法），产出新的元素树，与旧树对比，找出需要变更的 Fiber，给它们打上标记（Placement、Update、Deletion）。这个阶段叫 render 阶段，它有两个关键性质：纯计算，不碰 DOM；可中断，优先级更高的更新可以插队。

计算完成后进入 commit 阶段，这一步不可中断，分三个子步骤：变更前快照旧状态、执行 DOM 增删改、变更后调度 `useLayoutEffect`。浏览器随后绘制屏幕，`useEffect` 则在绘制后异步执行。所以布局测量要放在 `useLayoutEffect` 里，否则会看到"先错位再跳正"的闪烁。

## Fiber 与可中断的调和

Fiber 之前，React 的栈调和（stack reconciler）是一次性深度优先遍历，一旦开始就不能停，大组件树更新会长时间占用主线程，掉帧明显。Fiber 把遍历拆成一个个工作单元，每做完一个就检查一次"有没有更紧急的事"，有就让出主线程，稍后从断点继续。React 18 的并发渲染正是建立在这套机制上。

调和时 React 用两条启发式规则：不同类型的元素直接推倒重建（`div` 变 `p`，子树全部卸载）；同类型元素只更新变化的属性，并递归比较子节点。列表子节点靠 `key` 判断谁是谁：key 稳定，React 就能把旧节点复用过来，只更新内容；用数组索引当 key，列表一重排，索引和内容的对应关系就错位，轻则丢状态，重则渲染错数据。

## 批处理与自动批处理

React 18 之前，在事件处理器里连续两次 `setState` 会合并成一次渲染，但在 `setTimeout`、Promise 回调里却是两次渲染。React 18 引入自动批处理（automatic batching），默认对所有更新合并，包括异步回调和原生事件处理器。想立刻刷新可以用 `flushSync`，但它是逃生舱，滥用会把并发特性拱手让出。

```javascript
function Counter() {
  const [count, setCount] = useState(0);
  const [flag, setFlag] = useState(false);

  function handleClick() {
    setCount((c) => c + 1);
    setFlag((f) => !f);
    // 两次更新合并为一次渲染，count 与 flag 同时生效
  }

  return <button onClick={handleClick}>{count}</button>;
}
```

## 🚀 并发特性：把紧急的事留给用户

并发渲染给了 React"有的更新可以等"的能力。`useTransition` 把更新标记为低优先级，输入框保持跟手，重列表稍慢半拍；`useDeferredValue` 反其道而行，先保留旧值渲染一版，新值算好了再替换。Suspense 让组件树在数据未就绪时展示 fallback，React 负责协调"显示加载态"与"尝试渲染"之间的切换。这三个 API 都不改变最终结果，只改变到达结果的顺序，这正是并发渲染的精髓。

## 关于"重新渲染"的三个误解

1. **render 函数执行不等于 DOM 更新**。React 会先做新旧对比，没变化就跳过 commit，你看到的 render 日志可能一次 DOM 操作都没有。
2. **状态没变也可能触发 render**。父组件渲染，子组件默认跟着渲染；用 `React.memo` 包裹，并保持 props 引用稳定（`useMemo`、`useCallback`），才能真正跳过。
3. **Context 变化会击穿 memo**。Context 穿透 memo，只要 context 值变了，消费组件全都渲染，拆细 context 比全局 memo 更有效。

```javascript
// 拆成两个 context：一个只放稳定的 dispatch，一个放会变的 value
const ThemeStateContext = createContext(null);
const ThemeDispatchContext = createContext(null);
```

严格模式（StrictMode）在开发环境会故意双调用组件函数，这是为了暴露不纯的副作用，不是 bug。

## 性能优化的正确顺序

排査"渲染太多"的问题，建议按这个顺序：先看有没有不必要的状态提升（状态下沉到真正用的组件）；再看列表 `key` 是否稳定；然后才是 `memo`、`useMemo`、`useCallback`；最后才考虑虚拟滚动或并发特性。跳过前两步直接上 memo，往往是把结构性问题盖住，代码反而更难维护。

| 症状 | 第一嫌疑 | 验证方法 |
| --- | --- | --- |
| 输入卡顿 | 每次输入触发重列表 | Profiler 看 commit 耗时 |
| 列表滚动掉帧 | 单帧渲染节点过多 | 减少渲染量或虚拟滚动 |
| 接口重复请求 | 严格模式双调用副作用 | 检查 effect 依赖与清理函数 |
| 状态更新"慢半拍" | 批处理合并了多次 setState | 确认是否依赖同步读取新值 |

## 用 Profiler 定位一次多余渲染

### 一次真实的排查过程

理论讲完，走一遍真实排查。场景：一个长列表页，输入框每敲一个字符，整个列表重渲染，输入延迟明显。打开 DevTools 的 Profiler 录制一次输入，看火焰图里哪个组件的渲染时长最长，以及它为什么渲染。常见答案有三种：props 里传了新建的对象字面量，引用每次都变；Context 的 value 没拆分，Provider 每次渲染都产出新对象；状态放得太高，本该下沉到子组件。修复分别对应 `useMemo` 包裹 value、拆细 Context、状态下沉。读图时先看彩色长条（真实提交的部分），灰色是没有 commit 的渲染，优先级靠后。

## useMemo 与 useCallback 的真实成本

这两个 API 常被当成"性能优化按钮"到处按。它们的成本是明确的：每次渲染都要做依赖比较和缓存查找，被缓存的函数还要多占一份闭包内存。收益只在两种情况下成立：缓存值传给 `memo` 化的子组件，或者缓存值本身就是昂贵计算（遍历大树、大数据处理）。依赖数组写错（漏依赖、引用不稳定的依赖）还会引入更难查的 bug。所以顺序应该是：先删掉不必要的渲染，再考虑缓存。

## 服务端渲染与流式渲染

渲染机制的最后一环在服务端。React 18 的流式 SSR 允许 HTML 分块发出，Suspense 边界内的慢数据可以后到，首字节时间不再被最慢的接口拖住。水合（hydration）依然是成本大头：客户端要把整棵树的事件绑定补齐，`hydrateRoot` 配合选择性水合（用户点哪里先水合哪一块）可以把这个成本摊薄。理解这条链路有助于回答一个常见问题：为什么 SSR 页面的可交互时间有时反而比客户端渲染差，因为服务端把渲染成本挪到了客户端的水合阶段，总量没变，只是转移了位置。

## 排查清单

按这个顺序排查"渲染太多"：

1. **状态下沉**：状态只放在真正消费它的组件里，能放叶子就不放根。
2. **props 稳定**：对象、函数、数组类型的 props 是否引用稳定，不稳定的用 `useMemo`、`useCallback` 包住。
3. **Context 拆分**：把稳定的 dispatch 和易变的 value 分成两个 context，消费方按需订阅。
4. **memo 兜底**：只给渲染昂贵且 props 确实稳定的组件加 `React.memo`，加之前先确认前三步都做过了。

顺序反了，就会出现"memo 加了一圈，输入还是卡"的挫败感。

> 经验法则：先证明渲染是多余的，再优化它。Profiler 的数据比直觉可靠，而直觉在性能问题上几乎总是错的。

## 小结

React 的渲染机制可以浓缩成三句话：render 阶段在内存里算差异，可中断；commit 阶段把差异落到 DOM，不可中断；批处理和并发特性都是在两者之间做排程。理解这条链路，"为什么这个组件又渲染了"就不再是玄学，而是可以顺着 Fiber 树一步步查证的问题。更多背景可参考 <https://zh.wikipedia.org/wiki/React>，示例代码托管在 <https://example.com/react-render-demo>。

[^1]: Fiber 是 React 16 引入的内部重构，React 团队在 2017 年的 React Conf 上首次公开介绍，2018 年随 React 16 正式发布。
