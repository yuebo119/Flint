---
title: "Docker 开发环境搭建"
date: 2026-02-14
tags: ["Docker", "开发环境", "容器"]
categories: ["文档"]
description: "用 Docker 搭建可复现开发环境：基础镜像选择、Dockerfile 多阶段编写、compose 编排多服务、卷挂载热更新与常见问题排查，一条命令拉起全套环境。"
---

"在我机器上是好的"这句话的代价，每个团队都付过。新人入职装环境花两天，升级依赖把数据库配置搞坏，macOS 上跑得好好的脚本在同事的 Windows 上全线报错。容器化开发环境把"环境"本身变成一份随代码提交的配置：任何人拿到仓库，一条命令拉起一模一样的环境。这篇教程走完搭建全过程：选镜像、写 Dockerfile、用 compose 编排服务、处理热更新，最后附常见问题的排查方法。

🐳 先说结论：容器化开发环境的投入是一次性的，收益是每个人每天少踩的环境坑。

![开发环境容器化前后的依赖关系对比](/images/demo-9.svg)

## 环境要解决什么问题

在动手之前先明确目标，避免为了用容器而用容器：

- **可复现**：新同事 clone 仓库后一条命令进入可开发状态，不读长篇安装文档。
- **可隔离**：不同项目依赖不同版本的运行时、数据库，互不干扰，卸载项目即清理。
- **与生产一致**：本地跑的运行时、依赖版本和生产是同一份镜像定义，减少"环境差异"类故障。
- **可丢弃**：环境搞坏了就删掉重建，修复成本从半天降到一分钟。

不凑巧的是，把 Windows 上的老项目、需要硬件直通的程序、强依赖 GUI 的工具塞进容器，通常是在给自己找麻烦。先确认项目适合，再动手。

## 安装 Docker

桌面端安装 Docker Desktop，服务器装 Docker Engine，官方文档见 <https://docs.docker.com/get-started/>。安装后做三步验证：

```bash
docker version          # 客户端与服务端版本都正常输出
docker run --rm hello-world   # 能拉取镜像并运行即网络与权限正常
docker compose version   # 确认 compose 插件可用（V2 是独立插件）
```

国内网络拉取镜像慢的话，在 Docker Desktop 设置里配置镜像加速器；公司内网有私有仓库时，提前 `docker login` 配好凭证，避免每次构建现登。

## 选择基础镜像

基础镜像决定体积、安全和维护成本，按项目需求选：

- **官方运行时镜像**：如 `mcr.microsoft.com/dotnet/sdk`、`node:20`、`python:3.12`，自带工具链，适合开发环境直接用。
  - 后缀 `-slim`、`-alpine` 体积更小，但 alpine 用 musl libc，少数预编译二进制不兼容，遇到奇怪段错误时先换回 `-slim` 验证。
- **发行版镜像**：如 `ubuntu:24.04`、`debian:bookworm-slim`，需要自己装工具链，可控性最强。
- **distroless**：只含运行时不含 shell，适合生产镜像，不适合当开发环境。

原则：开发镜像可以"胖"（带编译器、调试工具），生产镜像要"瘦"，两者用同一个 Dockerfile 的不同阶段产出，这就是下面要讲的多阶段构建。

> 判断镜像选得对不对，看两个数字就够：开发镜像拉取时间是否还在分钟级，生产镜像体积是否比运行时必需部分大出数倍。超了，就该回头检查基础镜像和构建阶段。

## 写 Dockerfile

以一个 .NET 项目为例，开发与生产共用一个 Dockerfile，靠构建阶段区分。

### 多阶段构建示例

```dockerfile
# 语法指令，启用 BuildKit 的缓存挂载特性
# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/App -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "App.dll"]
```

### 三条关键实践

- **分层顺序**：把不常变的依赖还原放在拷贝全部代码之前，命中构建缓存，日常改代码只需重跑最后几层。
- **多阶段构建**：最终镜像只带运行时不带 SDK，通常能瘦掉一个数量级，拉取和启动都更快。
- **固定标签**：别用 `latest`，明确到小版本，避免某天拉到的镜像悄悄变了样。

## 用 compose 编排多服务

真实项目很少只有一个容器。应用加数据库加缓存，用 compose 一份文件描述全部：

```yaml
services:
  app:
    build:
      context: .
      target: build        # 开发阶段用带 SDK 的阶段
    ports:
      - "8080:8080"
    volumes:
      - .:/src             # 挂载源码，实现热更新
      - nuget-cache:/root/.nuget/packages   # 命名卷缓存依赖，避免重复下载
    environment:
      - ConnectionStrings__Default=Host=db;Database=app;Username=app;Password=${DB_PASSWORD}
    depends_on:
      db:
        condition: service_healthy

  db:
    image: postgres:16-alpine
    environment:
      - POSTGRES_PASSWORD=${DB_PASSWORD}
      - POSTGRES_DB=app
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      retries: 10

volumes:
  nuget-cache:
  pgdata:
```

几个容易踩的点：数据库密码通过环境变量注入，不要写死在文件里提交进仓库；`depends_on` 加健康检查条件，否则应用启动时数据库可能还没就绪；数据目录用命名卷，容器删了数据还在。日常操作就三条命令：`docker compose up -d` 启动、`docker compose logs -f app` 看日志、`docker compose down` 停止并清理。

## 热更新与调试

开发体验的关键是改完代码立刻见效，不用重建镜像。以 .NET 为例，容器内跑 `dotnet watch`，宿主机的文件变更通过绑定挂载传进容器。需要进容器排查时执行 `docker compose exec app bash`；只有依赖发生变化后才执行 `docker compose up -d --build` 重建，日常改代码不重建。

## 网络与数据卷基础

compose 把容器的网络和存储都自动化了，但出问题时懂底层原理能省半天时间：

- **网络**：compose 会为项目建一个独立网络，容器之间用服务名互访，`db:5432` 能通而 `localhost:5432` 不通，因为 localhost 在容器里指向自己。
- **端口映射**：`ports` 写成 `宿主端口:容器端口`，左侧是外部访问用的，右侧是容器内监听的，两边搞反是最常见的配置错误。
- **绑定挂载**：把宿主目录直接映射进容器，改代码即时生效，适合源码目录。
- **命名卷**：由 Docker 管理的存储，适合包缓存和数据库数据，容器删了数据还在，性能和跨平台兼容性都比绑定挂载好。
- **匿名卷**：临时数据，容器删除即回收，适合构建过程的中间产物。

一个实用技巧：怀疑数据卷有问题时，用 `docker run --rm -v 卷名:/data alpine ls /data` 起一个临时容器查看卷内容，比重启服务试错快得多。

## 常见问题排查

- **容器启动就退出**：`docker compose logs` 看最后几行，八成是启动命令或端口写错。
- **端口已被占用**：`docker ps` 找到占用端口的容器，改左侧宿主端口映射即可。
- **改了代码没生效**：确认代码目录真的挂载进去了，`docker compose exec app ls /src` 看一眼。
- **磁盘爆满**：`docker system df` 看占用，`docker system prune` 清理悬空镜像和停止的容器，数据卷删前先确认。
- **镜像拉取超时**：换加速器或提前 `docker pull` 到本地。

## 多环境与日常维护

项目通常要跑在开发、测试、生产三种环境，用 compose 的override 机制区分，避免三份配置各自漂移：

- 基础配置放 `compose.yaml`，开发专属放 `compose.override.yaml`，挂载、调试端口、详细日志都写在这里，提交进仓库。
- 生产配置放 `compose.prod.yaml`，去掉挂载、加上资源限制，构建时用 `docker compose -f compose.yaml -f compose.prod.yaml up -d` 合并生效。
- 环境差异全部通过环境变量注入，配置文件里只保留默认值，密钥绝不进仓库。

日常维护三条命令就够：`docker system df` 每周看一次磁盘；`docker image prune` 清理悬空镜像；`docker compose down -v` 只在对项目彻底放手时用，`-v` 会连数据卷一起删，执行前想清楚。

镜像优化还有一个常被忽略的点：合并 RUN 指令。每条 `RUN` 产生一层，层数越多镜像越大、拉取越慢，但过度合并又会破坏缓存。折中做法是按"变更频率"分组：系统更新一组、依赖安装一组、代码拷贝一组，用 `&&` 串联同组命令并及时清理同层产生的缓存文件，比如 `apt-get install` 后接 `rm -rf /var/lib/apt/lists/*`。

## 上手检查清单

- [ ] `docker version` 与 `docker compose version` 正常
- [ ] Dockerfile 使用多阶段构建，固定基础镜像标签
- [ ] compose 文件描述全部依赖服务，含健康检查
- [ ] 数据库密码等敏感配置走环境变量
- [ ] 源码挂载生效，改代码无需重建镜像
- [ ] 包缓存目录用卷隔离，不拖慢文件监听
- [ ] `docker compose down` 后重新 `up` 数据不丢

## 小结

容器化开发环境的收益不在技术先进，而在把"环境问题"从日常噪音里彻底拿掉。从一个小项目开始实践：先写 Dockerfile，再用 compose 加上数据库，最后处理热更新。等这套配置在团队里跑顺，新人入职的第一天就能提交代码，这才是它真正的价值所在。

最后提醒一条边界：容器解决的是"环境一致性"，不是"所有问题"。涉及硬件直通、特殊内核模块、强 GPU 加速的场景，容器方案要么不可用要么代价高昂，提前评估比强行迁移理智。环境配置和 Dockerfile 一样，也应该进版本库并参与评审，它本身就是项目基础设施的一部分。
