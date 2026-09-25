---
title: PWA 离线实践：Service Worker 的生命周期与缓存策略
date: 2025-04-15
tags: [PWA, Service Worker, 离线应用]
categories: [前端, PWA]
description: 一份 PWA 离线实战教程：Service Worker 生命周期、四种缓存策略的适用场景、版本更新与激活的坑，以及 manifest 与安装体验要点。
---

把网页做得"断网也能用"，听起来像原生 App 的特权，实际上 PWA（Progressive Web App）用两个标准件就做到了：Service Worker 负责拦截请求和管理缓存，Web App Manifest 负责安装后的外观和入口。这篇按落地顺序讲：注册、生命周期、缓存策略、更新、安装，最后是一份踩坑清单。

## PWA 是什么，不是什么

先说边界。PWA 不是一套框架，也不是离线优先的银弹。它的实际能力是：静态资源可缓存、弱网有兜底、可安装到桌面、可发推送（依赖浏览器支持）。它做不到：像原生 App 那样常驻后台跑任务（iOS 上尤其受限）、绕过浏览器存储配额（本地数据仍可能被清理）。想清楚这些，再决定哪些页面值得做离线。

## 注册 Service Worker

Service Worker 是一段注册在浏览器里的独立脚本，运行在自己的线程上，没有 DOM 访问权限，通过事件与页面通信。注册要放在页面加载之后，且只在 HTTPS 或 localhost 下生效：

```javascript
if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").then(
      (registration) => {
        console.log("注册成功，作用域：", registration.scope);
      },
      (error) => {
        console.error("注册失败：", error);
      },
    );
  });
}
```

作用域由脚本位置决定：`/sw.js` 能控制整个站点，`/sub/sw.js` 只能控制 `/sub/` 路径。想缩小控制范围就把脚本放深一层。

## 生命周期：三个事件与两次等待

### install 与 activate：缓存写入和旧缓存清理的时机

Service Worker 的生命周期是理解一切行为的地图，走一遍就再也不会被"为什么没更新"困扰：

1. **install**：首次注册或脚本内容变化时触发，适合做预缓存（把核心静态资源写进 CacheStorage）。事件里调用 `event.waitUntil()` 延长生命周期，等缓存写完。
2. **waiting**：新脚本安装完成，但旧脚本还在控制页面，新的只能等。这是"用户看不到更新"的经典原因。
3. **activate**：旧脚本下线、新脚本接管时触发，适合清理旧版本缓存。同样用 `event.waitUntil()` 等清理完成。

```javascript
const CACHE = "app-shell-v3";

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE).then((cache) =>
      cache.addAll(["/", "/app.css", "/app.js", "/offline.html"]),
    ),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(
        keys.filter((key) => key !== CACHE).map((key) => caches.delete(key)),
      ),
    ),
  );
});
```

注意缓存名带版本号。不换名字，用户第二次访问拿到的还是旧内容；只换名字不删旧的，缓存会无限膨胀。版本化命名加 activate 清理，是一对必须同时做的动作。

![Service Worker 的安装、等待与激活流程](/images/demo-6.svg)

## 四种缓存策略

请求策略在 `fetch` 事件里决定，选错的直接后果是要么内容不更新，要么弱网全白屏。按内容类型对号入座：

| 策略 | 行为 | 适用内容 |
| --- | --- | --- |
| 缓存优先 | 有缓存用缓存，没有走网络 | 带哈希的静态资源 |
| 网络优先 | 先请求网络，失败退回缓存 | 接口数据、HTML |
| 缓存并更新 | 先返回缓存，后台拉新替换 | 列表页、可接受旧数据的内容 |
| 仅网络 | 不走缓存，失败走兜底 | 支付、实时性要求高的请求 |

网络优先的骨架写法：

```javascript
self.addEventListener("fetch", (event) => {
  if (event.request.method !== "GET") return;

  event.respondWith(
    fetch(event.request)
      .then((response) => {
        const copy = response.clone();
        caches.open(CACHE).then((cache) => cache.put(event.request, copy));
        return response;
      })
      .catch(() => caches.match(event.request).then((hit) => hit || caches.match("/offline.html"))),
  );
});
```

`response.clone()` 不能省：响应体是流，读一次就耗尽，写缓存和返回页面各需要一份。这类细节是 Service Worker 新手期最常踩的坑。

## 更新：让新版本真正到达用户

默认流程下，新 Service Worker 装好后会一直等旧的控制释放，用户下次、甚至下下次访问才生效。要"刷新即更新"，标准做法分两步：新脚本 install 时调用 `self.skipWaiting()`，activate 后调用 `self.clients.claim()` 接管已打开的页面。

```javascript
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));
```

页面侧则要监听 `updatefound` 和 `controllerchange`，给用户一个"新版本已就绪，点击刷新"的提示。有团队做成静默更新加下次生效，体验更平滑，但关键修复（比如安全补丁）还是要显式提醒。经验：能自动更新自动更新，用户无感；必须刷新的场景给明确按钮，别偷偷 reload。

## manifest 与安装体验

Web App Manifest 是个 JSON 文件，描述应用的名称、图标、启动地址和显示模式：

```json
{
  "name": "示例应用",
  "short_name": "示例",
  "start_url": "/?source=pwa",
  "display": "standalone",
  "background_color": "#ffffff",
  "theme_color": "#1976d2",
  "icons": [
    { "src": "/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icon-512.png", "sizes": "512x512", "type": "image/png" }
  ]
}
```

`display: standalone` 让应用以独立窗口打开，没有浏览器地址栏，接近原生体验。iOS Safari 支持有限，安装体验不如 Android 完整，做 PWA 前建议先在目标机型上实测一遍。Service Worker 的完整 API 参考 <https://developer.mozilla.org/zh-CN/docs/Web/API/Service_Worker_API>，PWA 总览见 <https://developer.mozilla.org/zh-CN/docs/Web/Progressive_web_apps>。

## 边界与坑

- **存储不是永久承诺**：浏览器会在磁盘紧张时清理缓存，重要数据要能重新获取，或者引导用户安装后使用持久化存储 API。
- **HTTPS 是硬门槛**：Service Worker 能拦截请求，权限极大，浏览器只允许安全上下文注册。
- **iOS 的滞后**：推送、后台同步等能力在 iOS 上长期缺席或迟到，设计兜底方案时按"不支持"处理更保险。
- **调试用无痕模式**：Service Worker 缓存极顽固，调试时开无痕窗口，或在 DevTools 的 Application 面板里手动 Unregister 加 Clear storage。
- **预缓存别把整个站塞进去**：只缓存应用外壳（shell）和关键路由，其余走运行时缓存，否则首次 install 又慢又容易失败。

## 上手指引

- [ ] 确认站点全站 HTTPS，且核心页面在弱网下有可用的兜底 HTML
- [ ] 编写带版本号的 Service Worker，install 预缓存外壳，activate 清理旧缓存
- [ ] 按内容类型给路由分配四种策略之一，响应体写缓存前 clone
- [ ] 加上 skipWaiting 与 clients.claim，并提供"新版本可刷新"提示
- [ ] 补 manifest 与必需尺寸图标，在 Android 和 iOS 上分别实测安装与离线
- [ ] 用 DevTools 的 Offline 模式逐页面验证：断网后能看什么、不能看什么

## 预缓存要自动化，不要手写

install 里手写 `addAll` 清单，三天就会和实际构建产物脱节：改了文件名忘了改清单，用户拿到 404。正确做法是让构建工具生成预缓存清单：Vite 生态里用 `vite-plugin-pwa`，Webpack 里用 Workbox，它们在构建时扫描产物，生成带哈希的清单注入 Service Worker。清单即事实来源，源码里不再出现任何硬编码的资源路径。运行时缓存的路由规则则用声明式配置表达，比如"同源 GET 请求走缓存优先，带 token 的接口走网络优先"。

## 一次更新事故复盘

上线初期出过一次事故：紧急修复发布两小时后，后台数据显示仍有四成用户在用旧版页面。排查路径值得记住：先看 Service Worker 的安装率（埋点上报），发现新版本安装率正常；再看激活率，发现大量页面停留在 waiting 状态，原因是用户根本不关闭标签页，旧 Service Worker 一直控制着页面。修复方案是三件套：`skipWaiting` 让新版本立即接管，页面监听 `updatefound` 弹"刷新可用新版本"，超过 24 小时未更新的会话下次访问强制跳转刷新。事后我们给所有长生命周期页面（后台管理系统）加了轮询检查更新的逻辑，前台站点则依赖用户自然回访。

## 推送通知：能力与边界

PWA 的推送依赖 Push API，链路上有四个角色：浏览器推送服务、你的服务器、Service Worker、通知权限。国内环境的现实约束是：iOS 需要用户先把网页"添加到主屏幕"才支持推送，Android 各厂商 ROM 对后台进程的限制会影响推送到达率，FCM 在国内不可用。所以我们的结论是：推送做增强项，不做核心链路；把应用内消息中心作为兜底，推送只承担提醒。权限申请也要讲策略：在用户完成一次有价值的行为后再弹，冷启动就弹权限的拒绝率通常在七成以上。

## 给 PWA 本身做监控

Service Worker 是线上代码，一样要监控。至少采集四个指标：注册成功率、install 失败率（缓存写入失败常发生在存储配额满时）、缓存命中率（判断策略是否生效）、版本分布（有多少用户还停留在旧版本）。这四个指标接进前端监控体系后，"离线功能是不是坏了"第一次有了数据答案，而不是靠用户截图。

> Service Worker 的行为准则：安装要快、激活要稳、更新要可见。三条里任何一条没做到，用户就会以"我的修改没生效"的形式找到你。

## 小结

PWA 的离线能力没有魔法，本质是三件事的精确配合：生命周期管"什么时候生效"，缓存策略管"用什么数据"，更新机制管"用户何时拿到新版本"。把这三件事的边界理清，再按内容类型分配策略，一个弱网可用的站点就有了骨架。剩下的都是细节：版本号、clone、无痕调试、iOS 兼容。示例代码托管在 <https://example.com/pwa-offline-practice>。
