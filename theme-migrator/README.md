# 主题迁移项目（theme-migrator/）

把 Hugo 主题转换为 Flint（Scriban）可用形态的独立工作区。产出的主题语料被
案例站项目（`../demo-sites/`）与兼容矩阵（`../scripts/theme-matrix20.sh`）只读消费。

## 项目结构

```
theme-migrator/
├── clone-themes.sh          # 上游 Hugo 主题抓取（GitHub topic:hugo-theme，浅克隆）
├── clone-candidates.sh      # 候选池补充抓取（awesome-hugo-themes / star 排序前列）
├── verify-themes.sh         # 候选主题 Hugo 侧三条件验证（exit=0 + 有页数 + 最小页 >200B）
├── THEME-MIGRATOR-PLAN.md   # 逐轮施工日志（历史记录，内部路径按当时布局记载）
├── themes/                  # [gitignore] 主题工作池（21 个矩阵主题 + 扩展）
└── candidates/              # [gitignore] 候选主题池（未入矩阵的备选，独立于 themes/）
```

## 在迁移链路中的位置

```
上游 GitHub 主题
   │  clone-themes.sh / clone-candidates.sh（抓取：基础池 / 候选补充）
   ▼
candidates/（候选池）── verify-themes.sh（三条件验证）+ 人工遴选 ──▶ themes/（工作池）
                                        │  引擎侧迁移器逐站转换（建站时）
                                        │  src/Flint.ThemeMigrator（C#，AOT 安全）
                                        ▼
                              demo-sites/<主题>/themes/<主题>/
```

- **一次性批量转换**（离线、深度迁移）：`../src/Flint.ThemeMigrator`（C#，AST 级，
  2026-09-26 起替代原 Python 版 gotmpl2scriban.py）——转换器知识库与
  能力映射见 `../docs/THEME-COMPAT-PLAN.md`
- **建站时逐站转换**（每次 `demo-sites.sh` 重建执行）：`src/Flint.ThemeMigrator`

## 缓存再生成

`themes/` 与 `candidates/` 均为可再生缓存，不入库（见仓库 `.gitignore`）：

1. `bash theme-migrator/clone-themes.sh` 重抓上游主题
2. 矩阵/建站流程按需消费；主题池的遴选标准与历史见
   `THEME-MIGRATOR-PLAN.md` 与 `../docs/HUGO-COMPAT-MATRIX.md`
