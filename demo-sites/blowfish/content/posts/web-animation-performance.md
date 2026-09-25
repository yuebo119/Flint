---
title: Web 动画性能深扒：让浏览器只用合成器
date: 2025-01-06
tags: [动画, 性能优化, CSS]
categories: [前端, 性能]
description: 深扒浏览器渲染管线与动画卡顿：为什么只动位移与透明度属性，如何用工具定位掉帧，以及动画库与手势跟手的实战经验。
---

动画卡顿是前端最容易被感知、也最难甩锅的性能问题。用户说不清哪里不对，只会说"有点卡"。要解决它，得先接受一个事实：浏览器每帧的预算是 16.6 毫秒（60Hz），高刷屏幕是 8.3 毫秒，超预算就要丢帧。动画优化的全部工作，就是让每帧的工作量稳定待在这个预算内。

## 一帧里浏览器做了什么

浏览器把一帧拆成几个阶段：样式计算、布局、绘制、合成。前三个阶段跑在主线程，合成阶段可以由独立的合成线程完成，这正是性能优化的分水岭。

### 四阶段与两条路线

- **样式计算**：匹配选择器，算出每个元素最终的 computed style。选择器写得太野（深层后代选择器、通配符）会在这里烧时间。
- **布局（layout）**：计算每个元素的几何位置和尺寸。任何影响布局的属性变化（width、margin、font-size）都会触发这一阶段，而且影响范围可能扩散到整个文档。
- **绘制（paint）**：把元素的可见部分画成一个个绘制指令，生成绘制记录。
- **合成（composite）**：把多个绘制层按规则合并成最终画面。这一步可以交给 GPU，不占主线程。

关键结论：改 `width` 会让浏览器走完前三步；改 `transform` 和 `opacity` 则可能直接跳过前三步，只在合成阶段处理。后者就是"合成器动画"，也是流畅动画的第一公民。

![样式、布局、绘制、合成四级管线与动画属性的分流](/images/demo-5.svg)

## 只动合成器友好的属性

哪些属性只触发合成？经验法则是：不动盒子几何、不改变像素内容的属性都安全。最常用的两个是 `transform`（位移、缩放、旋转）和 `opacity`。要做展开动画，不要动画 `height`，改用 `transform: scaleY()`；要做跟随滚动，不要动画 `top`，改用 `translateY()`。

```css
/* 反例：动画 height 触发布局，每帧重排整个卡片 */
.card { transition: height 0.3s ease; }
.card.open { height: 320px; }

/* 正例：动画 transform，只走合成 */
.card { transition: transform 0.3s ease; transform-origin: top; }
.card.open { transform: scaleY(1); }
```

`will-change` 可以提前告诉浏览器"这个元素要变了，给它单独一层"，把元素提升为合成层。但它是提示不是命令，滥用会让层数量爆炸，显存和合成开销反而上升。经验值：只在动画即将开始的元素上加，动画结束移除，或者控制在十几个层以内。

| 动画属性 | 触发布局 | 触发绘制 | 走合成 | 建议 |
| --- | --- | --- | --- | --- |
| width / height | 是 | 是 | 否 | 换 transform |
| margin / padding | 是 | 是 | 否 | 换 transform |
| top / left | 是 | 是 | 否 | 换 translate |
| transform | 否 | 否 | 是 | 首选 |
| opacity | 否 | 否 | 是 | 首选 |
| color / background | 否 | 是 | 否 | 可用，注意面积 |
| filter / box-shadow | 否 | 是 | 否 | 控制使用范围 |

MDN 对 `will-change` 的说明值得一读：<https://developer.mozilla.org/zh-CN/docs/Web/CSS/will-change>，核心就一句，提前声明，用完收回。

## 布局抖动：读写交替的隐形杀手

比"动画了错误属性"更隐蔽的是布局抖动（layout thrashing）：在循环里交替读布局属性（`offsetTop`、`getBoundingClientRect`）和写样式，每次读取都迫使浏览器同步执行布局，写出后又让之前的布局作废。几十次循环就能吃掉整帧预算。

```javascript
// 反例：读一次排一次，n 个元素就是 n 次布局
items.forEach((item) => {
  const top = item.offsetTop; // 读：强制同步布局
  item.style.transform = `translateY(${top}px)`; // 写：布局作废
});

// 正例：先批量读，再批量写
const tops = items.map((item) => item.offsetTop);
items.forEach((item, index) => {
  item.style.transform = `translateY(${tops[index]}px)`;
});
```

这个模式在滚动监听里最致命。监听 `scroll` 事件做视差效果时，务必把读取和写入分成两趟，或者干脆用 `requestAnimationFrame` 节流。

## 🚀 用对工具：先量再改

猜卡顿原因几乎必错，先用工具看数据。Chrome DevTools 的 Performance 面板录制一段动画，重点看三个地方：

1. **火焰图里有没有紫色的 Recalculate Style 和绿色的 Layout 长条**：有就说明动画了非合成属性。
2. **帧与帧之间有没有超过 16ms 的间隙**：长任务会把帧拉长，丢帧就发生在这里。
3. **Rendering 面板里的 Paint flashing 和 Layer borders**：前者高亮重绘区域，后者显示合成层数量和边界，层数量异常时一眼能看出来。

再配合 `requestAnimationFrame` 自己埋点，记录每帧时间戳的差值，就能拿到稳定的帧率数据，而不是凭手感说"好像不卡了"。

```javascript
let last = performance.now();
function tick(now) {
  const delta = now - last;
  if (delta > 20) {
    console.warn(`掉帧：本帧 ${delta.toFixed(1)}ms`);
  }
  last = now;
  requestAnimationFrame(tick);
}
requestAnimationFrame(tick);
```

`requestAnimationFrame` 把回调对齐到浏览器绘制前执行，既是动画循环的正确节拍器，也避免了在 `setInterval` 里无脑跑导致的掉帧和无效计算。API 细节见 <https://developer.mozilla.org/zh-CN/docs/Web/API/Window/requestAnimationFrame>。

---

## FLIP：让布局动画也能跑在合成器上

有些动画本质上必须改变布局，比如列表项从一个位置跳到另一个位置。FLIP 技术（First、Last、Invert、Play）把"改变布局"伪装成"合成动画"：先记录元素初始位置（First），立刻把它移到目标位置并记录（Last），然后用 transform 把它倒推回初始视觉位置（Invert），最后清除 transform 并加上 transition，让浏览器以动画方式走到终点（Play）。用户看到的是丝滑位移动画，浏览器实际只合成了 transform。

```css
.flip-item {
  transition: transform 0.25s ease;
  will-change: transform;
}
```

配合 Web Animations API，FLIP 可以写得很紧凑，`element.animate()` 直接接收关键帧和时间参数，比手动管理 class 更精确，还能拿到动画完成Promise 做串联。

## 别忘了可访问性

相当一部分用户对动画敏感，前庭功能紊乱者会被大幅度位移动画引发生理不适。CSS 提供了 `prefers-reduced-motion` 媒体查询，检测到用户系统开启"减弱动态效果"时，应该把动画降到最小：

```css
@media (prefers-reduced-motion: reduce) {
  .flip-item {
    transition: none;
  }
  .hero-banner {
    animation: none;
  }
}
```

这不只是合规要求，也是体验底线：动效是调味料，不是主菜。

## 合成层不是免费的午餐

把元素提成合成层能绕开主线程，但层本身有成本：每个层是一块纹理，占用显存；层太多时合成线程合并图层的开销也会上升，手机上尤其明显，几百兆的纹理上传能直接把低端机拖垮。层爆炸（layer explosion）的典型症状是动画本身流畅，但页面整体卡顿、内存持续上涨。调试方法：开 DevTools 的 Layer borders 看层数量，异常的层（比如给上百个列表项都加了 `will-change`）就是嫌疑。经验阈值：全屏页面控制在几十层以内，列表项只给可见区域附近的元素加层。

## 滚动驱动的动画

跟手滚动是移动端体验的分水岭。`position: sticky` 之外，CSS 提供了 scroll-driven animations（滚动驱动动画），用 `animation-timeline: scroll()` 或 `view()` 把动画进度绑定到滚动位置，完全不需要 JS 监听 scroll，主线程零成本：

```css
.progress-bar {
  animation: grow linear;
  animation-timeline: scroll(root);
}

@keyframes grow {
  from { transform: scaleX(0); }
  to { transform: scaleX(1); }
}
```

不支持的浏览器会忽略动画相关声明，元素保持初始状态，降级成本几乎为零。这类"原生 API 逐步增强"的思路，比写一套 scroll 监听加 rAF 回退更值得投入。

## 帧率测量：别信手感

"感觉流畅"不是数据。可信的帧率测量要满足三个条件：在目标设备上测，开发机的性能会骗人；测稳态而不是启动几帧，去掉前两秒；测量脚本本身要轻，只记时间戳，不做 DOM 操作。前面给的 rAF 差值脚本满足前两条，配合真机远程调试或预发布环境的性能采集，才能得到可对比的数据。优化的验收标准也应该量化：优化前 P95 帧间隔 28 毫秒，优化后 17 毫秒，而不是"好像顺了"。

## 三个高频反模式

1. **用 JS 逐个改样式做入场动画**：几十个元素循环加 class 触发重排，改成 CSS animation 或 Web Animations API 批量驱动，帧时间能差三倍。
2. **阴影和模糊的滥用**：`box-shadow`、`filter: blur()` 大面积使用会显著增加绘制成本，动画中尤其明显，能用合成属性模拟就用模拟，比如用一张预模糊的图片代替实时模糊。
3. **忽略输入响应**：动画期间点击按钮没反应，多半是主线程被长任务占满，INP 指标监控能提前发现这类问题，而不是等用户投诉。

## 把帧率纳入监控

帧率不只应该在本地测，还应该进线上监控。做法和性能指标一样：用 rAF 记录帧间隔，聚合后按页面、设备档位上报 P95。设备档位可以用硬件并发数、设备内存等粗粒度信号分桶，低端机是卡顿的重灾区，也是最容易被桌面测试漏掉的群体。我们上线这套统计后的第一个发现是：中端安卓机的 P95 帧间隔是桌面 Chrome 的两倍，而团队此前的优化验收全在桌面浏览器上做。这个数据直接改变了验收流程：性能类改动必须在低端机档位复测才算完成。

## 小结前的一句提醒

动画优化的最后一条经验：不要为不存在的卡顿优化。先用数据确认问题存在、确认量级，再动手。过早优化的常见结局是为了省 0.1 毫秒引入三倍的代码复杂度，而用户根本没感知到那 0.1 毫秒。量具先行，动手在后，这是整篇文章的缩影。

> 动画优化的所有技巧，归根到底是一句话：让浏览器每帧少做事，或者让它把事交给更空闲的线程。

## 小结

Web 动画优化的知识可以压缩成五条：一帧 16.6ms；只动 transform 和 opacity；读写分离，避免布局抖动；先用 DevTools 量再改；尊重 `prefers-reduced-motion`。做到这五条，绝大多数"感觉卡"的动画都能救回来。示例页面托管在 <https://example.com/web-animation-performance>，可以边改边看帧率。
