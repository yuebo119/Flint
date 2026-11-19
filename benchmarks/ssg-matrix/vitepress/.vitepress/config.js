import { defineConfig } from 'vitepress';

// VitePress 的 .vitepress 必须位于项目根（本目录），srcDir 指向 docs/
// 之前把 config 放在 docs/.vitepress 下，结果 config 未被读取、产物也落在根下
export default defineConfig({
  title: 'SSG Bench',
  description: 'bench',
  srcDir: 'docs',
  // 体积治理（生产合理）：把站点级路由哈希表合并成单个 chunk，并去掉每页预加载链接。
  // minify 保持默认开启（生产压缩），2026-11-19 起「minify off 换速度」的调优已移除。
  metaChunk: true,
  shouldPreload: false,
});