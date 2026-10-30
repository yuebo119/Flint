import { defineUserConfig } from 'vuepress';
import { defaultTheme } from '@vuepress/theme-default';
import { viteBundler } from '@vuepress/bundler-vite';

export default defineUserConfig({
  title: 'SSG Bench',
  bundler: viteBundler(),
  theme: defaultTheme(),
});
