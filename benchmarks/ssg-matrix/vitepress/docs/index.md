---
title: Home
---

<script setup>
import { data } from './.vitepress/posts.data.js';
</script>

<h1>Home</h1>
<ul>
  <li v-for="p in data" :key="p.url">{{ p.frontmatter.title }}</li>
</ul>
