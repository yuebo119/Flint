# mini-hugo-theme — 端到端回归基准

一个覆盖 Hugo 常见用法的**最小主题**，用于验证迁移工具的完整管线
（四门禁：AST 完整性 → Scriban 预检 → 构建 → 产物 diff）。

## 覆盖的 Hugo 特性

| 特性 | 位置 |
|---|---|
| `baseof` + `block`/`define` 继承 | `layouts/_default/baseof.html` |
| 单页模板 + `define "title"/"main"` | `layouts/_default/single.html` |
| 列表页 + `.Pages.ByDate` 排序 | `layouts/_default/list.html` |
| partial 调用（普通/带参） | `_partials/head-extra.html`、`footer.html` |
| 分页（`.Paginator.Pagers`/`HasPrev`/`Prev.URL`） | `_partials/pagination.html` |
| 短码（`.Get`/`.Inner`/`markdownify`） | `_shortcodes/note.html` |
| 日期格式化（`.Date.Format "2006-01-02"`） | `single.html` |
| 站点菜单（`.Site.Menus.main`） | `baseof.html` |
| URL 函数（`relURL`） | `baseof.html` |
| 条件/`with`/`range` 嵌套 | 各模板 |
| static 资源（`static/css/style.css`） | — |

## 用途

```bash
# 迁移（含四门禁）
Flint.ThemeMigrator tests/fixtures/mini-hugo-theme <out> \
  --verify <站点目录> --hugo-output <hugo产物> --report <md>
```

## 与 Ananke 的分工

- **mini 主题**：证明**基本管线可用**（结构简单，无深度 Hugo 语义依赖）
- **Ananke**：压力测试（复杂主题，暴露引擎语义缺口）

mini 主题是回归基准——任何迁移器改动后应先确保它在 Flint 下能构建
且与 Hugo 产物结构一致。
