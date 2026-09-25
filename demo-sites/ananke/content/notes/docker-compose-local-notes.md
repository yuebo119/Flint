---
title: Docker Compose 本地环境笔记
date: 2024-12-10
tags: [Docker, docker-compose, 本地开发]
categories: [笔记]
description: Docker Compose 编排本地多容器环境的要点笔记，覆盖服务定义、健康检查、变量插值与五个高频踩坑记录。
---

# Docker Compose 本地环境笔记

## 一句话定位

Docker Compose 用一个 YAML 文件描述多容器应用，一条命令把整套本地环境拉起来。它的价值不在"容器编排"这种大词，而在把"装数据库、装缓存、装消息队列"这些重复劳动变成可提交的声明：新人 clone 仓库后一条 `docker compose up` 就能跑起来。维基百科对 [Docker](https://zh.wikipedia.org/wiki/Docker) 的介绍侧重容器化本身，Compose 是在此之上解决"多容器如何协同"的那一层。它同时是文档：新人不用问"环境怎么搭"，读一个 YAML 文件就懂；这也是把 compose 文件当代码 review 的原因，改错一个端口映射，影响的是全组的开发环境。

![Docker Compose 多容器协同示意](/images/demo-4.svg)

## 安装与环境

Compose 现在是 Docker 的插件，命令是 `docker compose`（v2），不再是独立的 `docker-compose`（v1，已停止维护）[^1]。装 Docker Desktop 自带；Linux 服务器装完 Docker Engine 后通常还需要单独装插件包。验证 `docker compose version`，输出带 v2 字样即可，之后的命令都按新写法来。

Ubuntu 上装完 Docker Engine 后补一个插件包：`sudo apt install docker-compose-plugin`；验证用 `docker compose version`，输出带 v2 字样即可，日常起停就是 `docker compose up -d` 与 `docker compose down` 两条命令，不需要额外装别的东西。

## 核心用法

### 服务定义：镜像、端口与卷

一个 compose.yaml 里每个顶层键是一个服务，最小配置只需要 `image`。三件事最重要：`ports` 做主机到容器的端口映射，格式是"主机:容器"；`volumes` 挂载数据，命名卷存数据库文件，绑定挂载放源码；`environment` 与 `env_file` 注入环境变量。数据库容器必须配命名卷，否则删了容器数据也没了。绑定挂载（`./src:/app/src`）与命名卷的分工要记牢：前者同步源码，后者存数据；把源码绑进数据库容器是经典误用。卷还有本地目录映射（`type: bind`）等选项，本地开发用不到，跨主机共享才需要上 NFS 这类网络存储。

```yaml
services:
  db:
    image: postgres:16
    ports:
      - "5432:5432"
    environment:
      POSTGRES_PASSWORD: devpass
      POSTGRES_DB: appdb
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      retries: 12

  cache:
    image: redis:7-alpine
    ports:
      - "6379:6379"

volumes:
  pgdata:
```

### 依赖顺序与健康检查

`depends_on` 只保证启动顺序，不保证"就绪"：容器进程起来了，数据库可能还在初始化。正确做法是给被依赖服务加 `healthcheck`，然后 `depends_on: condition: service_healthy`，应用容器等数据库真正可用再启动。这个组合能消灭一半的"启动时报连不上库"问题。健康检查的命令要选"轻且准"的：数据库用官方自带的就绪命令，HTTP 服务用 `/healthz` 探针，别用 `curl` 大动干戈，interval 太密反而拖累启动。被依赖服务重启后，`restart: true` 还能让应用跟着重建连接，处理连锁反应。

```yaml
services:
  api:
    build: .
    ports:
      - "8080:8080"
    depends_on:
      db:
        condition: service_healthy
    env_file:
      - .env
```

### 变量插值与多环境

compose.yaml 支持 `${VAR}` 插值，默认值写法 `${VAR:-默认值}`，本地开发把端口、密码都参数化，同一个文件就能服务多个开发者。`profiles` 用来把"调试工具"这类可选服务隔离：不带参数 `up` 时不启动，`--profile debug` 才拉起来，避免默认环境塞满用不到的东西。注意插值发生在 compose 层，应用容器内读到的才是最终值；调试插值问题用 `docker compose config` 打印渲染后的完整配置，比逐行猜快。

```yaml
services:
  adminer:
    image: adminer
    ports:
      - "${ADMINER_PORT:-8081}:8080"
    profiles: ["debug"]
```

### 日志、exec 与日常调试

起服务只是开始，日常更多时间花在看日志和进容器。`logs -f 服务名` 跟踪日志，`--tail 200` 只看最近两百行；`exec 服务名 sh` 进容器排查，比重启容器加日志高效得多；`ps`、`top` 看容器状态与资源占用。日志太多时给服务加 `logging` 选项限制单容器日志量，别让 debug 日志把磁盘吃光。资源占用异常时 `docker stats` 看实时 CPU 与内存，定位是哪个容器在飙；`restart 服务名` 比重建快，但配置变更必须 `up -d` 才生效，两者的区别在排障时经常被混。

```bash
# 日常三连：看日志、进容器、看状态
docker compose logs -f --tail 200 api
docker compose exec db psql -U postgres -d appdb
docker compose ps
```

### 网络与互通

同一 compose 项目里的服务默认在同一个网络，用服务名直接互连（`db:5432`），不需要知道容器 IP，容器重建 IP 变了也不影响。要模拟"服务暂时不可用"练容错，用 `docker compose pause db` 冻结进程、`unpause` 恢复，比 stop/start 更贴近网络分区场景。跨项目通信（比如另一个 compose 里的服务）需要把网络设为 external，两边 join 同一个网络，再用服务名访问；本机调试时把端口暴露到 `127.0.0.1` 就够了，别图方便映射到 `0.0.0.0`。

### 与生产环境的边界

先说边界：compose 是单机多容器工具，不是编排器。跨主机调度、故障自愈、滚动更新、服务网格这些是 Kubernetes 的领域，用 compose 硬扛只会得到一套谁都不敢动的"生产环境"；本地开发做到一键起停，它的使命就完成了。

再说数据：compose 文件里出现的密码是开发示例，不是配置管理。生产连接串走密钥管理或环境注入，仓库里只留变量名；本地卷里的库数据也别指望能带上生产，那是完全不同的两套存储，演练迁移走的是导出导入，不是拷贝卷。

然后是漂移：本地能跑不等于 CI 能跑。把 `docker compose config` 校验和一次完整 `up` 放进 CI，环境差异在合并前暴露，比在别人机器上复现 bug 便宜得多；这也是把 compose 文件当代码 review 的原因。

最后是升级：镜像 tag 别用 latest，锁小版本（比如 `postgres:16.2`），升级时先跑数据迁移、再重建容器，本地演练过再上生产。compose 的 `pull` 与 `up -d` 顺序也有讲究：先拉新镜像再重建，才能把停机时间压到最短。

## 高频场景速查

| 场景 | 配置要点 | 说明 |
| --- | --- | --- |
| PostgreSQL 本地库 | 命名卷 + healthcheck | 卷不删，数据就在 |
| Redis 缓存 | 官方 alpine 镜像 | 无需配置，开箱即用 |
| 应用热重载 | 绑定挂载源码目录 | 挂配置时排除 node_modules |
| 数据库管理界面 | adminer + profiles | 默认不启动，按需拉起 |
| 邮件测试 | mailpit 或 mailhog | 本地不发真实邮件 |
| 固定宿主端口 | `"5432:5432"` | 端口冲突时改左边主机口 |
| 日志限容 | `logging` 驱动选项 | 防日志吃满磁盘 |
| 资源限制 | `deploy.resources.limits` | 限制内存防 OOM |
| 多环境切换 | `.env` + 插值 | 配合 profiles 更干净 |
| 重建单个服务 | `up -d --build api` | 不用全量重启 |

## 提交前检查清单

把一个 compose 配置提交进仓库前，过一遍这几项。这份清单来自真实 review 中反复出现的问题，每条都对应一次"我本地好好的"事故；提交前花两分钟过一遍，比在群里互相排查一小时便宜：

- [ ] 数据库类服务都挂了命名卷，删容器不丢数据
- [ ] 被依赖的服务有 healthcheck，`depends_on` 用的是 `service_healthy`
- [ ] 敏感值来自 `env_file` 或环境变量，没有硬编码密码
- [ ] 端口映射左边是变量或明确端口，不与他人冲突
- [ ] 调试类服务收进 `profiles`，默认 `up` 不带出来
- [ ] `docker compose config` 能通过，无语法与插值错误
- [ ] 在干净机器上 `up` 验证过一次（CI 里跑更好）

## 踩坑记录

> **坑一：数据库数据突然没了。** 八成是把匿名卷当成了持久化，或者 `down` 时加了 `-v`。命名卷 + 不加 `-v` 才是安全组合；`docker compose down -v` 是显式删数据的操作，别写进日常脚本。

> **坑二：应用启动时报"连不上数据库"，重启一下又好了。** `depends_on` 默认只等容器启动，不等数据库就绪。补上 healthcheck 与 `service_healthy`，从根上消灭这类偶发。

> **坑三：挂载源码后 node_modules 被覆盖。** 绑定挂载主机目录会盖掉容器内路径，把 node_modules 卷单独挂回去：`- node_modules:/app/node_modules`，这是经典组合。

> **坑四：`docker compose ps` 显示端口映射了，外部还是连不上。** WSL2 与虚拟机场景下，服务可能只听在容器内的 127.0.0.1。检查应用监听地址是 `0.0.0.0`，以及防火墙与 WSL 端口转发。

> **坑五：改了很多遍镜像层，越 build 越慢。** Dockerfile 里把不常变的依赖安装放前面、常变的源码拷贝放后面，缓存命中率立刻回升。compose 的 build 一样吃这套规则。

> **坑六：`up` 之后数据库数据"消失"了。** 改了顶层 `volumes` 的定义（比如把 `pgdata` 改名），旧命名卷就丢了引用，数据看着像消失，其实卷还在，只是没挂上去。`docker volume ls` 能找回来，改名卷前先 `docker volume inspect` 确认。

> **坑七：`up -d` 之后端口时通时不通。** 常见于端口左边写成了变量但 `.env` 没加载，或者和本机已占用端口静默冲突。`docker compose config` 看渲染后的端口映射，`lsof -i :端口`（Windows 用 `netstat -ano`）确认本机占用，冲突就换主机侧端口。

---

## 速查小结

- 命令是 `docker compose`（v2），别再用 `docker-compose`。
- 三件套记牢：ports 映射、volumes 持久化、healthcheck 就绪判断。
- `depends_on` 管顺序，`service_healthy` 管就绪，两个一起用。
- 变量插值让一份配置服务所有环境，profiles 隔离可选服务。
- 绑定挂载同步源码，命名卷存数据，别混用。
- 提交前跑 `docker compose config` 验证，比上线后排查便宜得多。

本地环境声明化之后，"在我机器上能跑"这句话就到此为止了。compose 文件进版本库的那天，团队就再也不用传"环境配置文档"了，文件本身就是最新版文档。最后一条经验：compose 管本地，Kubernetes 管生产，两边语法不通但思想相通，把本地这套依赖声明练熟，上 K8s 时只是换了种写法。

[^1]: Compose v1 是 Python 写的独立命令 `docker-compose`，v2 起改为 Go 实现并作为 Docker 插件，命令变成 `docker compose`。v1 已停止维护，新项目一律用 v2 语法与命令。
