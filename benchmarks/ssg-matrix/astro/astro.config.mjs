import { defineConfig } from 'astro/config';

export default defineConfig({
  // 官方构建并发旋钮（默认 1）：万页纯 CPU 渲染，串行是主要瓶颈。
  build: { concurrency: 8 },
});