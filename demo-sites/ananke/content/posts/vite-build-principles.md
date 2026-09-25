---
title: Vite 构建原理深扒：开发服务器为什么能秒开
date: 2024-07-05
tags: [Vite, 构建工具, 前端工程化]
categories: [前端, 工程化]
description: 深扒 Vite 的两套链路：开发时用原生 ESM 加依赖预构建实现按需编译，生产时交给 Rollup 打包，以及与 Webpack 的根本差异。
---

Webpack 时代的开发体验有个绕不开的循环：改一行代码，等打包，刷浏览器。项目越大，打包越慢，等十几秒是常态。Vite 把这个循环拆了：开发服务器不再"先打包再服务"，而是把浏览器当成模块加载器，源码按需编译、按需送达。本文把 Vite 的开发链路和生产链路各走一遍。

## 传统打包器的瓶颈在哪

Webpack 开发服务器的做法是"先打包，再服务"：从入口文件出发，把整张依赖图编译成一个或多个 bundle，放进内存，再响应浏览器请求。问题在于：启动时必须遍历整张图，首屏等待与项目规模成正比；热更新时即使只改一个文件，也要重新构建受影响的 chunk。对一个上千个模块的中后台项目，冷启动十几秒、热更新几秒，都是这套架构的必然结果。

## 开发链路：原生 ESM 加按需编译

Vite 的做法分三层。

### 第一层：启动时做一次依赖预构建

Vite 扫描 `node_modules` 里的依赖，用 esbuild 把它们打包成少量符合浏览器 ESM 规范的文件。这一步解决两个问题：CommonJS 与 UMD 包浏览器不认，预构建把它们转成 ESM；依赖内部成百上千个小文件会造成请求风暴，预构建把它们合并。

```bash
# 创建项目并启动开发服务器，预构建结果缓存在 node_modules/.vite
npm create vite@latest my-app -- --template vanilla-ts
cd my-app && npm install
npm run dev
```

第二层，浏览器请求源码时按需编译。浏览器加载入口 HTML，遇到 `import` 就发请求；Vite 的中间件拦截这些请求，用 esbuild 把 TypeScript、JSX 转成 JavaScript，把 CSS 里的 `@import` 和 `url()` 改写成合法的模块引用，即时返回。没被请求的模块，一个字节都不编译。

第三层，热更新走精确的模块图。某个模块变更，Vite 顺着 import 关系找到受影响的模块，通过 WebSocket 通知浏览器只重新请求这些模块。组件状态不丢，因为只替换了模块本身，没有整页刷新。

![开发服务器按需编译，生产构建整体打包](/images/demo-3.svg)

## import.meta.env 与条件编译

Vite 把环境变量注入到 `import.meta.env`，并提供模式（mode）概念：开发模式读 `.env.development`，生产构建读 `.env.production`。

```javascript
if (import.meta.env.DEV) {
  console.log("开发模式专属日志，生产构建中会被移除");
}

const base = import.meta.env.VITE_API_BASE;
```

注意只有 `VITE_` 前缀的变量会暴露到客户端，这是防止把密钥打进产物的设计。模式判断会在生产构建时被静态替换，if 分支里的死代码随后被 Rollup 移除。

## 生产链路：Rollup 出场

开发服务器那套按需编译，搬到生产环境并不合适：几百个模块请求的往返成本、没有 tree shaking、没有代码分割。所以 Vite 的生产构建交给 Rollup：从入口出发构建完整依赖图，做 tree shaking、代码分割、资源哈希和压缩，产出少量带长效缓存的静态文件。CSS、图片、JSON 都有对应插件处理，HTML 作为入口被自动注入产出的资源引用。

这也解释了一个常见疑问：为什么 Vite 开发时快得飞起，生产构建却和普通 Rollup 项目差不多。两者目标不同：开发追求单次响应快，生产追求产物体积小、缓存友好。压缩环节想再快一档，可以把 `build.minify` 设为 `esbuild`，用 Rust 写的压缩器替掉默认的 esbuild 压缩（Vite 早期默认 terser，后来换成了 esbuild）。

<details>
<summary>展开说说：为什么生产构建不全交给 esbuild？</summary>

esbuild 的打包速度快一个数量级，但生态是硬伤：代码分割的粒度控制、CSS 处理、插件 API 丰富度都不及 Rollup，tree shaking 的精细程度也有差距。Vite 团队的选择是：开发侧用 esbuild 做单文件转译，发挥速度优势；生产侧用 Rollup 做整体打包，发挥生态与产物质量优势。两条链路各用其长，是 Vite"快而不糙"的原因。
</details>

## Vite 与 Webpack 开发服务器对比

| 维度 | Webpack dev server | Vite dev server |
| --- | --- | --- |
| 服务方式 | 先打包进内存再响应 | 原生 ESM，按需编译响应 |
| 冷启动 | 随项目规模线性增长 | 与模块总数基本无关 |
| 热更新 | 重新构建受影响 chunk | 只失效变更模块 |
| 依赖处理 | 打包进 bundle | esbuild 预构建成 ESM |
| 生产构建 | Webpack | Rollup |

## 依赖预构建的坑

- **新增依赖要重启**：预构建有缓存元数据，装了新包后如果自动重构建没触发，删掉 `node_modules/.vite` 再启动。
- **CJS 依赖的命名导出**：预构建能把 CommonJS 转成 ESM，但某些包的导出是运行时计算的，转换后可能拿到 undefined，需要在 `optimizeDeps.include` 里单独指定。
- **Monorepo 里的软链接**：`pnpm` 项目里未被声明的间接依赖不会被预构建，表现为运行时报导入失败，把包显式加进 `optimizeDeps.include` 即可。
- **缓存要进 CI 忽略清单**：`.vite` 目录属于本地缓存，不要提交进仓库，否则成员之间会互相污染。

## 配置与插件：约定的力量

Vite 的配置集中在 `vite.config.ts`，核心概念不多：`resolve.alias` 配路径别名，`server.proxy` 配开发代理，`build.rollupOptions` 直接透传 Rollup 配置，`css.preprocessorOptions` 给预处理器传参。插件是一个带有 `name` 和若干钩子的对象，可以自己写：

```javascript
/** @type {import('vite').Plugin} */
function virtualConfig(options) {
  const virtualId = "virtual:app-config";
  return {
    name: "virtual-app-config",
    resolveId(id) {
      if (id === virtualId) return "\0" + virtualId;
    },
    load(id) {
      if (id === "\0" + virtualId) {
        return `export default ${JSON.stringify(options)}`;
      }
    },
  };
}

export default {
  plugins: [virtualConfig({ apiBase: "/api" })],
};
```

约定大于配置体现在默认值上：入口默认 `index.html`，输出默认 `dist`，环境变量默认读 `.env` 系列文件，不写配置也能跑。真正需要写配置的场景通常是三类：别名、代理、库模式。

## 库模式：为 npm 包构建

Vite 不只能构建应用，`build.lib` 模式用来打包库：指定入口和产物格式，外置 peerDependencies，产出不打包依赖的干净产物。

```javascript
export default {
  build: {
    lib: {
      entry: "src/index.ts",
      name: "MyLib",
      fileName: (format) => `my-lib.${format}.js`,
    },
    rollupOptions: {
      external: ["react", "react-dom"],
    },
  },
};
```

发布库时还要补两件事：`package.json` 里用 `exports` 字段区分 ESM 和 CJS 入口，以及为每个格式配上对应的类型声明。少了 `exports`，Node 和打包器会按老规则猜入口，双格式产物可能加载错。

## Monorepo 里的 Vite

Monorepo 场景下，应用需要消费工作区内的包。经验有两条：第一，工作区包的源码如果是 TypeScript，别让预构建去编译它，在 `optimizeDeps.exclude` 里排除，走源码别名；第二，多个应用共享配置时，把公共配置抽成 `createViteConfig` 工厂函数，各应用只写差异部分，避免配置文件复制粘贴后各自漂移。构建缓存方面，CI 里缓存 `node_modules/.vite` 对开发容器帮助明显，但生产构建不吃这个缓存，别指望它给构建提速。

## 升级与迁移成本

Vite 大版本之间的配置迁移成本不高，主要痛点在插件生态：底层向 Rust 工具链（Oxc、Rolldown）演进的方向意味着部分插件需要适配。升级策略建议跟随 minor 版本、谨慎对待 beta，生产项目锁定 lockfile，升级时先在 CI 跑全量构建和视觉回归。我们团队的做法是每次大版本升级预留半天，实际花的时间通常更少，但不预留就一定会拖。

## CSS 与静态资源的处理

Vite 对 CSS 的处理是内置的：`.css` 文件直接以模块形式引入，`@import` 会被内联；Less、Sass、Stylus 装对应预处理器即可用，不需要配 loader。开发时每个 CSS 文件以独立 `<style>` 标签注入，改样式即时热更新；生产时 CSS 被抽取成独立文件，经过压缩和加前缀处理。图片和字体走资源管线：小文件内联为 base64（默认 4KB 阈值，可调），大文件拷贝到输出目录并加上内容哈希。这套默认值覆盖了绝大多数项目的需求，需要定制的场景（比如调整内联阈值）再翻文档。

## 本地与线上的差异

开发服务器和生产构建共享同一套配置，但行为有差异，排查问题时要意识到自己在哪一侧。典型差异有三个：环境变量取值不同（DEV 与 PROD）；路径基准不同（开发用 `/`，部署在子路径时要配 `base`）；优化行为不同（生产才会压缩、拆包、哈希）。子路径部署是最高频的踩坑点：应用放在 CDN 的 `/app/` 目录下，忘记配 `base: "/app/"`，结果是白屏加一堆 404。改完配置先用 `vite preview` 本地验证，再上线。

> Vite 的分界线值得记住：开发侧优化"单次请求的响应时间"，生产侧优化"产物的体积与缓存"。用错了评判标准，就会得出"Vite 生产构建也很快"或者"Vite 开发也没多快"的错误结论。

## 小结

Vite 的核心思想只有一句话：开发时不做打包，把模块解析还给浏览器，把编译推到请求发生时。原生 ESM 是前提，esbuild 预构建是铺垫，Rollup 是生产的保证。理解这条分界线，就能明白为什么 Vite 冷启动与项目规模几乎无关，也能明白为什么它的生产构建速度仍然取决于 Rollup。ES 模块的规范细节可以参考 <https://developer.mozilla.org/zh-CN/docs/Web/JavaScript/Guide/Modules>，完整示例见 <https://example.com/vite-principles-demo>。
