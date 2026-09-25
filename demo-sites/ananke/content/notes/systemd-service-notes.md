---
title: systemd 服务管理笔记
date: 2025-06-25
tags: [systemd, Linux, 服务管理]
categories: [笔记]
description: systemd 单元文件编写与服务管理命令笔记，覆盖 Type 选择、重启策略、定时器与五个高频踩坑记录。
---

# systemd 服务管理笔记

## 一句话定位

systemd 是现代 Linux 的初始化系统与服务管理器：开机启动、进程守护、依赖排序、日志收集都归它管。运维一个自研服务，最终形态几乎都是一个 `.service` 单元文件加几条 `systemctl` 命令。维基百科对 [Systemd](https://zh.wikipedia.org/wiki/Systemd) 的介绍强调它是"系统和服务管理器"，这份笔记只抓最常用的子集：写单元、管生命周期、看日志。它的引入一度引发争议（"它做了太多事"），但现状是几乎所有主流发行版都以它为基础，会看单元文件、会读 journalctl，是 Linux 运维的基本功。从 SysV init 迁移过来的最大心智变化是：没有"启动脚本"了，一切皆单元，服务、定时器、挂载、套接字都是同一套语法描述的对象。

![systemd 服务生命周期示意](/images/demo-6.svg)

## 安装与环境

主流发行版（Debian/Ubuntu/CentOS/Fedora）默认自带 systemd，无需安装。用户级服务不需要 root：单元放 `~/.config/systemd/user/`，命令加 `--user`，适合跑个人定时任务与开发服务器。系统级单元放 `/etc/systemd/system/`，第三方软件包通常放 `/usr/lib/systemd/system/`，前者优先级更高，适合放自己的覆盖配置，升级也不会被冲掉。排查服务启动问题时的第一现场是 `systemctl status` 的输出：它会把主进程 PID、最近几行日志、退出码一起呈现，比单独翻日志高效。

确认环境用 `systemctl --version` 看版本、`systemctl get-default` 看默认目标；用户级服务全程无需 sudo，命令加 `--user` 即可，例如 `systemctl --user daemon-reload` 与 `systemctl --user status myapp`，个人定时任务与开发服务器都够用。

## 核心用法

### 单元文件三段结构

一个 `.service` 文件分三块：`[Unit]` 写元信息与依赖（Description、After、Wants）；`[Service]` 是主体，Type、ExecStart、Restart、User 都在这里；`[Install]` 定义"启用"时干什么，通常是 `WantedBy=multi-user.target`。Type 是最容易选错的一项：前台运行的服务用 `simple` 或更精确的 `notify`；自己 daemon 化的老程序用 `forking`；跑完就退的用 `oneshot`。写单元文件的三条纪律：ExecStart 用绝对路径（systemd 不继承你的 PATH）；程序以前台运行，自己 daemon 化是老程序的习惯，不是必须；每个可选依赖想清楚用 Wants 还是 Requires，前者失败不拖垮本服务，后者会。还有 `ExecStop` 与 `TimeoutStopSec`：优雅停止超时后 systemd 会发 SIGKILL，长连接服务（比如消息队列消费者）要把停止超时调大，给业务留出收尾时间。

```ini
[Unit]
Description=My App Service
After=network.target

[Service]
Type=simple
User=appuser
WorkingDirectory=/opt/myapp
ExecStart=/usr/bin/node /opt/myapp/server.js
Restart=on-failure
RestartSec=3
EnvironmentFile=/opt/myapp/.env

[Install]
WantedBy=multi-user.target
```

### 生命周期管理

启停命令都是 `systemctl`，改完单元文件必须 `daemon-reload` 让 systemd 重读磁盘，否则会拿着旧定义操作。日志用 `journalctl -u 服务名` 看，`-f` 实时跟踪，`--since "1 hour ago"` 限定时间。定时任务不用写 cron，`.timer` 单元配一个 oneshot service 就能实现，还自带日志与错过补偿。`systemctl edit` 是改第三方服务的安全姿势：它生成 override 片段，不动包管理的原始单元，升级不冲突、回滚也简单。

```ini
# /etc/systemd/system/backup.timer
[Unit]
Description=Daily backup at 02:30

[Timer]
OnCalendar=*-*-* 02:30:00
Persistent=true

[Install]
WantedBy=timers.target
```

```bash
# 生命周期四连
sudo systemctl daemon-reload
sudo systemctl enable --now myapp
sudo systemctl restart myapp
sudo systemctl status myapp

# 看日志：跟踪、限时、只看本次启动
sudo journalctl -u myapp -f
sudo journalctl -u myapp --since "30 min ago"
sudo journalctl -u myapp -b
```

### 沙箱与安全加固

systemd 的价值不止"把服务拉起来"，还在于用 cgroup 与命名空间给它画牢房。`ProtectSystem=strict` 让整个文件系统只读，`ReadWritePaths` 按需开放；`PrivateTmp=true` 给服务独立临时目录；`NoNewPrivileges=true` 禁止提权；`ProtectHome=true` 屏蔽家目录。这套组合能把服务被攻破后的爆炸半径压到最小，安全评审时是加分项，代价只是几行配置。

```ini
[Service]
Type=simple
ProtectSystem=strict
ReadWritePaths=/var/lib/myapp
PrivateTmp=true
NoNewPrivileges=true
ProtectHome=true
```

### 命令分层速查

按操作对象把常用命令分层，找的时候从外往里缩。命令记不住时先想"对象是谁"：操作服务、看状态，还是动单元定义，三层各取所需：

- **服务生命周期**
  - `start` / `stop` / `restart`：启停与重启
  - `reload`：不中断连接重载配置（程序需支持）
  - `enable` / `disable`：管开机自启，不动当前状态
  - `mask`：彻底禁止启动，连手动都挡
- **状态与诊断**
  - `status`：是否 active、最近几条日志、主进程 PID
  - `is-active` / `is-enabled`：脚本里判断用，返回码干净
  - `list-units --failed`：开机失败的服务一眼看全
- **单元与日志**
  - `daemon-reload`：改完单元文件的必经步骤
  - `cat 服务名`：看 systemd 实际生效的单元内容
  - `edit 服务名`：生成 override 片段，不动原始文件
  - `journalctl -u`：日志交互与 less 一致，<kbd>G</kbd> 跳到末尾、<kbd>/</kbd> 搜索、<kbd>q</kbd> 退出

### 单元文件的组织与复用

命名与放置：服务名全局唯一，自定义服务加项目前缀（`myapp-web.service`）防止和系统服务撞名；自己的单元放 `/etc/systemd/system/`，包管理的放 `/usr/lib/systemd/system/`，前者优先，升级不冲突。

模板单元是多实例的答案：`myapp@.service` 这样的定义配合 `%i`（实例名），`systemctl start myapp@8080` 就能起一套，端口、数据目录用 `%i` 传入；同机跑多个同构服务时，一份定义胜过复制十份，改一处全部生效。

override 片段是改第三方服务的正道：`systemctl edit nginx` 生成的 drop-in 只写差异，不动原始单元；排查"到底生效了哪些值"时，`systemctl cat nginx` 会把合并后的完整定义打印出来，比翻包管理目录直观，也不会在升级时被覆盖丢失。

依赖写错是开机失败的头号原因：`After=` 管顺序，`Requires`/`Wants` 管强弱，`BindsTo` 同生共死；写完新单元先跑 `systemd-analyze verify 文件路径`，能在上线前抓住大部分引用与语法错误，比开机时翻 journal 快得多。

最后是分工：裸机服务交给 systemd，容器里的服务别再套一层 systemd，PID 1 之争社区已有共识；一层只管一层的事，是这套体系给我们的最大启示，也是它学了半小时就能上手的原因。

监控也别忘：`systemctl list-timers` 看所有定时任务下次触发时间，`systemd-analyze blame` 列出拖慢启动的服务，`systemd-analyze critical-chain` 打印开机关键链；开机慢的机器，这三条命令按顺序过一遍，八成能定位到具体单元，剩下的才是硬件问题。

## 高频场景速查

十个场景按"常驻服务、定时任务、安全加固"分组，都是单元文件里真实用过的配置，抄走改路径就能用。

| 场景 | 配置要点 | 说明 |
| --- | --- | --- |
| Node/Python 常驻服务 | `Type=simple` + `Restart` | 前台进程交给 systemd 守护 |
| 老 daemon 程序 | `Type=forking` + `PIDFile` | 自己 fork 到后台的用这个 |
| 启动前准备 | `ExecStartPre` | 建目录、等挂载、查依赖 |
| 敏感配置 | `EnvironmentFile` | 别把密钥写进单元文件 |
| 开机自启 | `WantedBy=multi-user.target` | enable 之后才生效 |
| 每天定时 | `.timer` + `OnCalendar` | 比 cron 多日志和补偿 |
| 开机跑一次 | `oneshot` + `RemainAfterExit` | 初始化脚本的标准姿势 |
| 资源限制 | `MemoryMax` / `CPUQuota` | cgroup 层面的硬限制 |
| 故障重启 | `Restart=on-failure` + `RestartSec` | 防止疯狂重启拖垮机器 |
| 沙箱加固 | `ProtectSystem=strict` | 配合 `ReadWritePaths` 用 |

## 踩坑记录

> **坑一：Type 选错，服务"启动即成功、随即消失"。** 选 `simple` 但程序自己 daemon 化退出，systemd 认为主进程正常结束，状态显示 active (exited) 或直接 inactive。自己 fork 的老程序用 `forking`，现代程序一律前台运行。

> **坑二：改了单元文件没效果。** 忘记 `daemon-reload`。单元文件在磁盘上，systemd 内存里是旧副本，reload 只是重载服务，daemon-reload 才是重载定义，两个动作都要。

> **坑三：环境变量里的空格被截断。** `Environment="KEY=a b c"` 必须带引号，整条值原样传递；写成 `Environment=KEY=a b c` 只会取到 `a`。含特殊字符优先用 `EnvironmentFile`。

> **坑四：enable 了但开机没跑。** 看 `WantedBy` 目标是否存在、`After=` 的依赖是否满足；`systemctl list-dependencies 服务名` 能把依赖链拉出来看，常见是等待一个永远不会 ready 的网络目标。

> **坑五：日志把磁盘写满。** journald 默认可能不限制体积，`journalctl --vacuum-size=200M` 清理，`/etc/systemd/journald.conf` 里配 `SystemMaxUse` 一劳永逸。

> **坑六：服务里执行的命令找不到。** systemd 不读 `~/.bashrc` 与 `~/.profile`，环境是极简的。脚本里用到 node、python 这类命令必须写绝对路径，或显式 `Environment=PATH=/usr/local/bin:/usr/bin:/bin`；用 `systemd-analyze security 服务名` 还能给服务的暴露面打分，分数低不是错，但要知道差在哪。

> **坑七：`Restart=always` 让故障服务疯狂重启。** always 是不论退出码都重启，必须配合 `RestartSec` 与 `StartLimitBurst` 控制频率，否则崩溃循环会把 CPU 打满、日志写爆。生产服务推荐 `on-failure` 加 `RestartSec=5`，给依赖服务留出恢复时间。

---

## 速查小结

- 单元文件三段：`[Unit]` 依赖、`[Service]` 主体、`[Install]` 启用目标。
- Type 按程序行为选：前台 `simple`/`notify`，自守护 `forking`，一次性 `oneshot`。
- 改定义用 `daemon-reload`，改配置用 `reload`，别混。
- 日志 `journalctl -u` 是排错第一现场，`-f` 跟、`-b` 看本次启动。
- 密钥进 `EnvironmentFile`，不进单元文件；单元文件是要进版本库的。
- 定时任务优先 `.timer`，自带日志与错过执行补偿。
- 沙箱四件套：`ProtectSystem`、`PrivateTmp`、`NoNewPrivileges`、`ProtectHome`。

把服务交给 systemd，开机自启、崩溃重启、日志归档就都不再是应用自己的事了。把"这个服务怎么起的"从口头约定变成磁盘上的一个单元文件，交接和排错的成本都会降一个量级；这也是基础设施即代码在最微小一层的体现。下次遇到"服务又挂了"，先 `systemctl status` 再 `journalctl -u`，两分钟定位，比重启大法体面得多。
