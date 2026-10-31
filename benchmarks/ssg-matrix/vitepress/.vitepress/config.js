import { defineConfig } from 'vitepress';

// VitePress 的 .vitepress 必须位于项目根（本目录），srcDir 指向 docs/
// 之前把 config 放在 docs/.vitepress 下，结果 config 未被读取、产物也落在根下
export default defineConfig({
  title: 'SSG Bench',
  description: 'bench',
  srcDir: 'docs',
  // 体积对照实验：把页面元数据合并成单个 chunk，并去掉每页的预加载链接
  metaChunk: true,
  shouldPreload: false,
});