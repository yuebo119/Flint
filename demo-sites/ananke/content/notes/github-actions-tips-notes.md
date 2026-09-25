---
title: GitHub Actions 技巧笔记
date: 2026-03-20
tags: [GitHub Actions, CI, 自动化]
categories: [笔记]
description: GitHub Actions 工作流编写的实用技巧笔记，覆盖触发矩阵、缓存并发、密钥安全与六个高频踩坑记录。
---

# GitHub Actions 技巧笔记

## 一句话定位

GitHub Actions 是 GitHub 内置的 CI/CD 服务：把 `.github/workflows/` 下的 YAML 文件变成每次 push、PR、定时自动执行的任务。写构建、跑测试、发版、部署都能在里面完成，runner、日志、制品存储都是现成的。维基百科对 [GitHub](https://zh.wikipedia.org/wiki/GitHub) 的介绍侧重代码托管平台本身，Actions 是它上面长出来的自动化层：仓库里的一切事件都能当触发器。它的另一个优势是生态：marketplace 上有上万个 action，checkout、setup-node 这类官方 action 几乎是所有 workflow 的第一行；但要警惕来路不明的第三方 action，固定版本号并审查它申请的权限，供应链风险和 npm 依赖是同一类问题。

![GitHub Actions 工作流执行示意](/images/demo-9.svg)

## 安装与环境

无需安装：在仓库建 `.github/workflows/ci.yml` 即自动生效[^1]。本地验证可以用 `act` 在容器里模拟运行，不必每次推送试错。workflow 文件顶部的 `name` 会显示在仓库 Actions 页；仓库页按 <kbd>t</kbd> 快速定位文件，按 <kbd>.</kbd> 可以直接在网页编辑器里改 workflow，小改动不必拉到本地。新仓库打开 Actions 页，GitHub 会按项目类型推荐 starter workflow（Node、Python、Docker 等），基于推荐模板改比从零写快，模板本身也是学习语法的最好材料。

本地验证可以用 `act`（需要 Docker）在容器里模拟运行，`act push` 就能触发一次；YAML 语法用任意 linter 检查，例如 `npx --yes yaml-lint .github/workflows/ci.yml`，提交前过一遍，拼写错误就出不了门。

## 核心用法

### 触发、任务与步骤

workflow 的骨架是三段：`on` 定义什么事件触发，`jobs` 定义并行任务，每个 job 里 `steps` 按顺序执行。`uses` 引用现成 action，`run` 执行 shell 命令，两者可以混排。`runs-on` 选 runner，常用 `ubuntu-latest`；步骤失败默认中断整个 job，`continue-on-error: true` 可放行非关键检查。触发条件还可以叠加：`push.tags` 配 `v*` 只让标签推送触发发版，`branches-ignore` 排除文档分支；`types` 细到 opened、synchronize、labeled 等事件，PR 门禁常用 `types: [opened, synchronize, reopened]`。`on.workflow_call` 还能带输入输出，被复用方像函数一样声明参数、调用方传值；把发布流程抽成可复用 workflow，多个仓库共用一套发布逻辑，改一次全部生效。

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: 20
          cache: npm
      - run: npm ci
      - run: npm test
```

### 矩阵、缓存与并发控制

`strategy.matrix` 一份配置跑多个组合：多 Node 版本、多操作系统，CI 覆盖率立刻翻倍。缓存用 `actions/cache` 或 setup-* 内置的 `cache` 参数，key 里带上锁文件哈希，依赖不变才命中。`concurrency` 防止同一分支的旧 run 还在跑、新 push 又排一队：同组直接取消旧的，省 runner 分钟数。矩阵不是越多越好：每个组合都是一个独立的计费单元，八组合就要跑八次。`include` 可以往特定组合里加额外变量（比如只在 Windows 上跑一个额外步骤），`exclude` 剪掉没意义的组合，把矩阵控制在"真正要覆盖"的维度上。

```yaml
jobs:
  test:
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest]
        node: [18, 20, 22]
    runs-on: ${{ matrix.os }}
    concurrency:
      group: ${{ github.workflow }}-${{ github.ref }}
      cancel-in-progress: true
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: ${{ matrix.node }}
      - run: npm ci && npm test
```

### 密钥、变量与制品

`secrets` 存敏感值，只在 workflow 里通过 `${{ secrets.NAME }}` 注入，日志里自动打码；非敏感配置用 `vars`。跨 job 传文件用 `actions/upload-artifact` 与 `download-artifact`。部署到生产前用 `environment` 配审批与保护规则。注意 fork 出来的 PR 默认拿不到 secrets，这是安全设计[^2]。secret 有大小限制，大证书别往里塞，改成 base64 后存 artifact 或从密钥管理服务拉取；`secrets.GITHUB_TOKEN` 是每次运行自动签发的临时令牌，不需要手工配置，权限在 workflow 顶部显式声明，用完即弃。

```yaml
  deploy:
    needs: test
    runs-on: ubuntu-latest
    environment: production
    steps:
      - uses: actions/checkout@v4
      - run: ./deploy.sh
        env:
          API_TOKEN: ${{ secrets.DEPLOY_TOKEN }}
      - uses: actions/upload-artifact@v4
        with:
          name: dist
          path: dist/
```

### monorepo 与路径过滤

单仓库多包的项目，最怕"改一行文档、全仓库 CI 跑一遍"。`on.push.paths` 与 `paths-ignore` 按路径过滤触发；job 级用 `dorny/paths-filter` 这类 action 检测改动目录，只构建受影响的包。配合 `needs` 把检测 job 放在最前，让后续 job 条件执行，分钟数立刻降下来。路径过滤的两个层级要分清：`on.push.paths` 决定整个 workflow 跑不跑，job 级 `if` 决定单个 job 跳不跳；文档改动只想跳过测试、保留 lint，就用后者。两个都用、各管一段，才是 monorepo 的正确打开方式。

```yaml
on:
  push:
    paths-ignore:
      - "**.md"
      - "docs/**"

jobs:
  changes:
    runs-on: ubuntu-latest
    outputs:
      api: ${{ steps.filter.outputs.api }}
    steps:
      - uses: actions/checkout@v4
      - uses: dorny/paths-filter@v3
        id: filter
        with:
          filters: |
            api:
              - packages/api/**

  test-api:
    needs: changes
    if: ${{ needs.changes.outputs.api == 'true' }}
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - run: cd packages/api && npm ci && npm test
```

### 成本与配额意识

先算账再扩容：公开仓库的 Actions 免费，私有仓库按分钟计费。矩阵每加一个组合，成本就翻一倍；八组合的矩阵跑一年不是小数目，加组合前先问"这个维度真的需要覆盖吗"，把矩阵控制在关键路径上，其余组合留给发版前的全量验证。

runner 也有价目表：ubuntu 最便宜也最快，Windows 贵且启动慢，只在必须验证 Windows 行为时用；macOS 更贵，跨平台测试留它一个组合兜底即可，全量矩阵上 macOS 是拿预算换心理安慰，真出问题再补也来得及。

缓存就是省钱：依赖缓存命中一次省几分钟，CI 时长直接对应账单；cache key 设计好（稳定前缀加锁文件哈希），命中率上去，账单就下来，这是少数"优化即省钱"的动作，值得专门花半小时调。

别忘了 workflow 文件的清理：废弃的 workflow 及时删或禁用，留在仓库里既是噪音也是隐患——没人维护的自动化比没有自动化更危险，定时任务某天突然发邮件给全组，往往就是半年前的遗留。

两个防失控开关：job 级 `timeout-minutes` 必设，挂起的步骤不会烧掉整晚；`concurrency` 取消同分支的旧 run，重复推送不再排队。再往上，自建 runner 等规模上来再考虑，算上机器与维护成本未必便宜；公开仓库上跑自建 runner 有安全风险，这点官方文档明确警告过。

最后一条关于组织：workflow 多了以后按用途拆分文件（ci、release、nightly），公共步骤抽成复用 workflow（`workflow_call`），仓库里就不会出现一个两千行的巨型 YAML；拆文件本身不增加运行成本，却让 review 回到可读的范围。

## 高频场景速查

十个场景覆盖从 PR 门禁到生产部署的全链路，按使用频率排列，配第三条 monorepo 技巧一起看效果更好。

| 场景 | 配置要点 | 说明 |
| --- | --- | --- |
| PR 检查 | `on: pull_request` | 合并前必跑的门禁 |
| 多版本矩阵 | `strategy.matrix` | `fail-fast: false` 看全结果 |
| 依赖缓存 | `cache: npm` 或 `actions/cache` | key 含锁文件哈希 |
| 定时任务 | `on: schedule` + cron | 注意是 UTC 时间 |
| 手动触发 | `workflow_dispatch` | 带输入参数更灵活 |
| 取消旧 run | `concurrency` | 同 ref 只留最新一次 |
| 跨 job 传文件 | artifact up/download | 大文件别用 output 字符串 |
| 生产部署审批 | `environment` + 保护规则 | 密钥按环境隔离 |
| 只跑改动路径 | `paths` / `paths-ignore` | monorepo 省时间的关键 |
| 复用工作流 | `workflow_call` | 多仓库共用同一套 CI |

## 踩坑记录

> **坑一：fork 的 PR 里 secrets 全是空。** 安全设计如此，防止有人靠 PR 偷密钥。需要三方 CI 结果就用单独服务，或让维护者手动触发带 secret 的 workflow。

> **坑二：`GITHUB_TOKEN` 权限不够，push 标签失败。** workflow 文件顶部加 `permissions: contents: write`，按最小原则只开需要的权限，别图省事写 `write-all`。

> **坑三：缓存永远命中不了。** key 每次都在变（把 run_id、时间戳写进 key 了），或者 restore-keys 顺序不对。key 用锁文件哈希，前面放稳定的前缀，命中率立刻上来。

> **坑四：schedule 定时任务不按点跑。** cron 是 UTC，且高峰时段可能延迟几分钟。写"UTC 02:00"前先换算，高峰任务加 `workflow_dispatch` 手动补跑通道。

> **坑五：checkout 之后 git 历史只有一条。** `actions/checkout` 默认 `fetch-depth: 1`，依赖版本号生成的 workflow（如 changesets）要显式 `fetch-depth: 0`。

> **坑六：表达式写错静默失败。** `${{ secrets.X }}` 拼进字符串时少了引号，或者把表达式写进 `if` 却忘了最外层的 `${{ }}`。改完先在 PR 里跑一次看日志，再合主分支。

> **坑七：workflow 改了却不触发。** 三个常见原因：文件放错分支（见脚注一）、YAML 缩进错导致整个文件解析失败（Actions 页会有黄点警告）、`on` 的事件名拼错。先用 `npx yaml-lint` 过语法，再去 Actions 页看有没有红色失败记录。

---

## 速查小结

- 骨架记三段：`on` 触发、`jobs` 并行、`steps` 顺序。
- 矩阵用 `fail-fast: false`，别让一个组合失败掩盖其他结果。
- 缓存 key 的稳定部分放前面，变化部分（锁文件哈希）放后面。
- `concurrency` 是省钱第一开关，同一 ref 只留最新 run。
- secrets 走注入、日志自动打码；fork PR 拿不到是特性不是 bug。
- 部署上生产用 `environment` 审批，密钥按环境隔离。
- monorepo 用 paths 过滤触发，改动检测 job 放最前面。

CI 写得越接近"每次提交都自动验证"，发布就越不需要勇气。把 CI 当成"每次提交都跑的回归测试"，上线的决定权就从"谁有空手动点一遍"回到代码本身，这才是 Actions 这类工具最大的价值。如果只能记一条：让每次 push 都自动跑一遍基础验证，其余技巧都是在这条之上的改良。

[^1]: workflow 文件必须放在仓库默认分支（通常是 main）的 `.github/workflows/` 目录才会被触发；在其他分支上新加的 workflow 要等合并后才生效。

[^2]: `environment` 除了审批，还能给密钥加保护规则：只有通过指定分支或标签的部署才能读取该环境的 secret，生产与测试环境的密钥因此彻底隔离。
