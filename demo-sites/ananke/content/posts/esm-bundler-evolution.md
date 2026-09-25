---
title: ES 模块与打包器编年史：从 script 标签到 Rust 编译器
date: 2026-04-20
tags: [ESM, 打包器, 前端历史]
categories: [前端, 工程化]
description: 按年代梳理前端模块化三十年：AMD 与 CommonJS 之争、标准模块进入浏览器，以及打包器从 Webpack 到 Rust 系工具的演进主线。
---

前端工程的每一次大变局，背后几乎都是"模块怎么组织"这一个问题。从 2009 年 CommonJS 定下服务器端模块规范，到 2015 年 ES 模块写进语言标准，再到 2020 年后 Vite 和 Rust 系工具把构建速度抬升一个数量级，主线始终清晰：模块的"书写方式"在向语言标准收敛，模块的"编译方式"在向原生和本地代码收敛。这篇按年代讲这条主线。

## 2009 年之前：全局作用域时代

最早的 JavaScript 没有模块概念，一个页面里所有 script 共享一个全局作用域。代码组织靠两个土办法：命名空间对象（把所有东西挂在一个全局对象下）和立即执行函数表达式（IIFE，用闭包藏私有成员）。jQuery 时代的插件生态建立在这套约定上：一个全局 `$`，无数插件往上挂方法。

它的天花板很明显：依赖关系只能靠 HTML 里 script 标签的顺序隐式表达，加载几百个文件时顺序地狱、全局污染、无法按需加载，全都无解。对模块化的需求，从第一个超过千行的 JS 项目出现起就存在了。

## 2009 到 2012：CommonJS、AMD 与 Browserify

Node.js 在 2009 年出现，随它一起定下的是 CommonJS 规范：`require` 引入依赖，`module.exports` 导出，同步加载，适合服务器（文件在本地，读盘快）。这是第一次，JavaScript 有了事实上的模块标准。

### AMD 与 CommonJS 的分歧

浏览器等不了同步加载，AMD 规范（RequireJS 为代表）应运而生：定义模块时显式声明依赖数组，加载完成后执行工厂函数，天然异步。同一时期还有国内兴起的 CMD 规范（Sea.js），依赖就近声明、延迟执行，与 AMD 的"依赖前置"是路线之争。

Browserify 在 2011 年前后出现，提出一句改变格局的口号：让 Node 的模块跑在浏览器里。它把所有依赖打包成一个 bundle，用 `require` 的浏览器端填充实现打通两端。从此"写代码用 CommonJS，发布前打成一个包"成为标准工作流，CommonJS 也事实上赢下了书写格式之争。

## 2012 到 2015：Webpack 与代码分割

Webpack 在 2012 年出现，起初只是另一个打包器，真正的分水岭是它把"一切皆模块"推到了极致：JavaScript、CSS、图片、字体，全部走同一套依赖图处理。配合 code splitting（代码分割）和 loader 机制，"按路由拆分 bundle、按需加载"第一次变得可行。同期 Gulp 和 Grunt 负责任务流（压缩、合并、lint），Webpack 负责模块图，前端构建工具链正式分层。

ES2015（2015 年 6 月定稿）把模块写进了语言标准：`import`、`export`、动态 `import()`。浏览器和 Node 都开始计划支持，模块化的"书写方式"终于不再靠社区规范猜。规范细节见 <https://developer.mozilla.org/zh-CN/docs/Web/JavaScript/Guide/Modules>。

## 2015 到 2018：Rollup、tree shaking 与原生 ESM

标准有了，配套工具跟上。Rollup 在 2015 年前后出现，主打"只打包用到的代码"：ES 模块的静态结构让 tree shaking（死代码消除）成为可能，产物体积比 Webpack 小一大截，库作者纷纷转向。前端生态开始分化：应用用 Webpack（功能全），库用 Rollup（产物干净）。

浏览器这边，原生 ESM 在 2017 到 2018 年落地：Chrome 61、Safari 10.1、Firefox 60、Edge 16 先后支持 `<script type="module">`。配合 import maps，浏览器第一次有能力不打包直接解析模块图，这为四年后 Vite 的爆发埋下了全部伏笔。

---

## 2019 到 2021：Parcel、esbuild 与 Vite

零配置打包器 Parcel（2017 年）先试探了"开箱即用"路线，而真正的提速来自两个项目。esbuild（2019 年，Go 编写）把打包速度提高了整整一个数量级，证明了打包器的瓶颈从来不是算法而是实现语言。Snowpack（2019 年）则把"开发时不打包、浏览器原生 ESM 按需加载"的思路产品化。

Vite（2020 年）站在两条线的交汇点：开发服务器用原生 ESM 加 esbuild 预构建，做到冷启动与项目规模无关；生产构建交给 Rollup，保证产物质量。到 2021 年 Vite 2 发布时，它已经不只是 Vue 的专属工具，而成为前端构建的新默认选项。同一时期，Webpack 5（2020 年）推出 Module Federation，把"运行时共享远程模块"做进打包器，微前端架构因此多了一条构建期路线。

## 2022 至今：Rust 系工具与编译器化

### Rust 系工具为什么集体登场

构建工具进入 Rust 时代。Rspack（字节跳动，2023 年正式发布 1.0）用 Rust 重写 Webpack 的核心，保持 API 兼容换取十倍级提速；Turbopack（Vercel）同样押注 Rust 加增量架构；Vite 团队自己也启动了 Rolldown 项目，用 Rust 重写 Rollup，目标是兼容既有插件生态。

另一个趋势是"打包器变编译器"。Rome 项目曾试图把格式化、lint、编译、打包收进一个工具链，虽然后来开发受阻，但从它分叉出的 Biome 延续了这个方向：一个二进制文件、零配置、全流程覆盖。工具链从"组装"走向"一体"，从 JS 运行时走向本地二进制，是这条时间线给出的明确指向。

![模块化书写格式与构建工具的演进时间线](/images/demo-10.svg)

## 一条时间线看清三十年

| 年代 | 模块书写 | 代表工具 | 关键变化 |
| --- | --- | --- | --- |
| 2009 前 | 全局加 IIFE | 无 | 顺序地狱 |
| 2009-2012 | CommonJS、AMD | Node、Browserify、RequireJS | 模块概念落地 |
| 2012-2015 | CommonJS 事实标准 | Webpack、Gulp | 依赖图与代码分割 |
| 2015-2018 | ES2015 模块 | Rollup、Webpack | 语言标准加 tree shaking |
| 2019-2021 | ESM 全面普及 | Vite、esbuild、Snowpack | 原生 ESM 与提速 |
| 2022 至今 | ESM 加 TypeScript | Rspack、Turbopack、Rolldown、Biome | Rust 实现与工具一体化 |

## 历史给的三个判断

1. **书写格式的收敛已经完成**。ESM 赢了，剩下的分歧只在编译目标（ES 版本、产物格式）。新项目没有理由再从 CommonJS 或 AMD 开始。
2. **打包器的竞争转向实现语言**。当依赖图算法趋同，速度差异主要来自语言与并行度，这是 Rust 系工具集体登场的根本原因。
3. **"打包"本身在被重新审视**。原生 ESM 让浏览器自己能做模块解析，打包的存在理由从"浏览器做不到"变成"请求数量与缓存策略的权衡"。未来更可能是编译器而非打包器：按部署环境直接产出最优形态，而不是一刀切地合并。

ECMAScript 各版本的变化见 <https://zh.wikipedia.org/wiki/ECMAScript>，JavaScript 语言背景见 <https://zh.wikipedia.org/wiki/JavaScript>，完整示例见 <https://example.com/esm-bundler-evolution>。

## import maps：浏览器里的"别名表"

import maps 是原生 ESM 生态里被低估的一块。它让 HTML 里直接声明裸模块名的解析规则，浏览器自己完成"react"到实际 CDN 地址的映射，不打包也能写 `import React from "react"`：

```html
<script type="importmap">
{
  "imports": {
    "react": "https://esm.sh/react@18.3.1",
    "react-dom/client": "https://esm.sh/react-dom@18.3.1/client"
  }
}
</script>
```

它的意义在于给"无打包"方案补上了最后一块：模块说明符解析。CDN 直出加 import maps 在文档站、演示页、教学场景已经够用；但生产环境仍有请求数量和缓存策略的顾虑，所以更现实的形态是混合：框架代码打包，变化少、缓存久；业务代码走原生 ESM，改完即生效。

## TypeScript 之后：编译层成为标配

还有一条暗线值得点出：TypeScript 的普及让"编译"从可选项变成必选项，打包器的职责因此从"合并文件"扩展为"完整的编译管线"：TS 转译、类型剥离、语法降级、polyfill 注入、tree shaking，全部打包在一条流水线里。Rust 系工具的优势正在于把整条流水线用同一门语言重写，共享 AST 和并行度。这也是为什么新工具的宣传重点从"打包多快"变成了"端到端多快"。

## 给今天选型的三条建议

第一，看维护频率而不是 star 数。构建工具是长周期依赖，选有活跃维护、明确升级路径的，Vite 和它的 Rust 继任者们目前都满足。第二，先确认产物的消费者。给浏览器用，优化首屏和缓存；给 Node 用，注意 dual package 和 exports 字段；给库用，保留 tree shaking 空间。第三，把构建时间当指标管理。冷启超过十秒、热更新超过一秒，就该投入精力优化，工程效率是复利，早期偷的懒后期要连本带利还。

> 模块化的历史告诉我们：能被语言标准接管的约定，最终都会进语言；能被本地代码重写的工具链，最终都会换成 Rust 或它的后继者。

## 小结

模块化的三十年，是一部"约定让位于标准、标准让位于原生能力"的历史。CommonJS 解决了有没有，ESM 解决了统不统一，原生 ESM 和 Rust 编译器解决了快不快。理解这条主线，再看今天的工具选型就不再是追新：选型标准应该是模块图谁处理、产物给谁用、瓶颈在哪一段。历史不会重复，但模块化确实一直在押韵。
