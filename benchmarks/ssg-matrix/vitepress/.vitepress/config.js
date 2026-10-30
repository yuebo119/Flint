import { defineConfig } from 'vitepress';

// VitePress 的 .vitepress 必须位于项目根（本目录），srcDir 指向 docs/
// 之前把 config 放在 docs/.vitepress 下，结果 config 未被读取、产物也落在根下
export default defineConfig({
  title: 'SSG Bench',
  description: 'bench',
  srcDir: 'docs',
});