---
title: Wireshark 抓包入门笔记
date: 2025-09-14
tags: [Wireshark, 网络抓包, 排错]
categories: [笔记]
description: Wireshark 抓包过滤器与显示过滤器的入门笔记，附七个排错场景、命令行抓包方式、排错任务清单与五个踩坑记录。
---

# Wireshark 抓包入门笔记

## 一句话定位

Wireshark 是图形化的网络抓包与协议分析工具：把网卡上流过的字节还原成一条条协议记录，TCP 握手、HTTP 请求、TLS 协商都能逐帧看。维基百科对 [Wireshark](https://zh.wikipedia.org/wiki/Wireshark) 的定位是"网络协议分析器"，命令行对应物是 `tshark`，服务器上排错更顺手。它解决的是"信任问题"：浏览器、客户端、代理各说各话的时候，抓一份包出来，字节流不会说谎。入门门槛不在工具本身，而在知道该看哪一层、哪个字段，所以这篇笔记按"抓什么、怎么看、怎么排错"组织，不按菜单组织。

![Wireshark 抓包分析界面示意](/images/demo-7.svg)

## 安装与环境

桌面系统装图形版：`apt install wireshark`（Debian 会问是否允许非 root 用户抓包，选是，自动加入 `wireshark` 组），macOS 用 `brew install --cask wireshark`。服务器无桌面时装 `tshark`，或者只用 dumpcap 抓包、拿回本地分析。权限是第一个门槛：抓包需要访问网卡，Linux 上由 dumpcap 以受控方式提权[^1]。容器里抓包是另一类场景：给容器加 `--net=host` 或 `NET_ADMIN` 能力后，用 tshark 在容器内抓宿主网卡；Kubernetes 排错更常见的是临时起一个 debug 容器共享网络命名空间，抓完即销毁，不动业务容器。

```bash
# Debian / Ubuntu：图形版 + 命令行版一起装
sudo apt install -y wireshark tshark

# 把自己加入抓包组，重新登录后生效
sudo usermod -aG wireshark $USER

# 服务器无桌面：tshark 命令行抓包
tshark -i eth0 -f "port 80" -w /tmp/http.pcap
```

## 核心用法

### 抓包过滤器：BPF 语法

开始抓包前先设过滤器，只抓关心的流量，这是抓包不乱的关键。语法是"协议 + 方向 + 主机/端口"，用 `and`、`or`、`not` 组合：`host 10.0.0.5`、`port 443`、`tcp port 8080 and host 10.0.0.5`、`not arp`。抓包过滤器在抓包时生效，抓的时候没设，事后再强的显示过滤器也救不回来。BPF 表达式写错时 Wireshark 会红字提示，记不准语法就点"捕获选项"里的表达式管理器，里面按协议分类列好了常用模板；`tcpdump -ddd` 能把表达式编译成字节码，是验证语法是否被接受的最快方法。

```bash
# 抓某个主机 80 端口的双向流量
tcp port 80 and host 10.0.0.5

# 抓除 ARP 与 SSH 之外的所有流量
not arp and not port 22

# 命令行等价写法（-f 后面跟 BPF 表达式）
dumpcap -i any -f "tcp port 443" -w tls.pcapng
```

### 显示过滤器：抓完之后再筛

抓到包之后用显示过滤器精读，语法是"协议字段 + 比较符 + 值"：`http`、`http.request.method == "POST"`、`ip.addr == 10.0.0.5`、`tcp.analysis.retransmission`、`tls.handshake.type == 1`。字段记不全没关系，表达式栏有自动补全，敲 `tcp.` 就列出全部字段。显示过滤器只影响"看什么"，不删数据。过滤器表达式可以保存成书签（工具栏的书签图标），把排错场景固化成几个按钮，下次直接点，比从头敲快；书签还能导出，团队共享一套排查视图。

```bash
# 常用的几条显示过滤器
http.request                          # 只看 HTTP 请求
dns.qry.name contains "example"       # 查域名解析请求
tcp.analysis.retransmission           # 找重传，定位丢包
tcp.flags.reset == 1                  # 找被 RST 掉的连接
http.response.code >= 400             # 错误响应一览
```

### 三招读包

这三招覆盖九成日常：先看异常归类，再读具体对话，最后看全局分布，顺序本身就是排错路径。

1. **Follow Stream**：右键一条 TCP 流选 Follow → TCP Stream，客户端与服务端的完整对话按顺序重组展示，HTTP 接口调试看这个最直观。
2. **看统计**：Statistics → Conversations 看哪些 IP 在通话、Flow Graph 看时序，能快速判断"到底是没发出去还是对方没回"。
3. **专家信息**：Analyze → Expert Information，Wireshark 把重传、重复 ACK、乱序、零窗口自动归类，网络质量问题先看这里。

### 命令行抓包与自动分析

无桌面的服务器上，抓包靠 dumpcap 与 tshark，分析可以回到桌面。tshark 的 `-Y` 参数直接套用显示过滤器，`-T fields` 只输出指定字段，等于把 Wireshark 的过滤能力搬进管道；`-z` 系列统计（io、conv、http）不打开图形界面也能出报告。在 Cron 里定时抓一段做基线对比，是发现"网络什么时候变坏"的笨办法，但有效。

```bash
# 服务器上抓十分钟流量，按时间分卷
tshark -i eth0 -f "tcp port 80 or tcp port 443" -b duration:600 -w cap.pcapng

# 命令行直接统计：看谁在说话、说了多少
tshark -r cap.pcapng -q -z conv,tcp

# 只提取 HTTP 请求的字段，喂给后续统计
tshark -r cap.pcapng -Y http.request -T fields -e ip.src -e http.host
```

### 抓包伦理与合规

只抓该抓的：生产环境抓包前先拿授权，过滤器缩到最小范围。全量抓包会把 Cookie、Token、表单内容原样落盘，这些文件一旦泄露，比要排查的问题本身危险得多；权限和敏感数据要按同一级别对待，不能因为"只是抓个包"就放宽。

存储与清理：pcap 按敏感资产管理，排查完及时删除，不要留在共享目录或工单附件里；需要留证据时，用 Export Specified Packets 只导出关键帧，附上环境说明，比甩一个几个 G 的全文抓包专业得多，也安全得多。

脱敏再分享：把抓包文件发给同事前，抹掉敏感字段，或者改用文本摘要加关键字段截图；一张没脱敏的包足以让看到的人复用整套会话凭证，这种"帮忙"经常变成事故，合规检查时也说不清。

与监控配合：抓包是显微镜，不是监控屏。长期观测靠指标与日志系统，抓包只在"有具体假设要验证"时出场；两者结论互相印证的那一天，排错才从艺术变成工程，团队也才敢把结论写进复盘文档。

## 高频场景速查

| 场景 | 抓包/显示要点 | 判断依据 |
| --- | --- | --- |
| DNS 解析失败 | 过滤 `dns` | 看有无响应、返回码 |
| 连接建立慢 | 看三次握手时间差 | SYN 重传是丢包信号 |
| HTTPS 证书错误 | 过滤 `tls.alert` | Alert 记录指明原因 |
| 接口返回慢 | Follow Stream 看时序 | 区分服务端慢与网络慢 |
| 丢包卡顿 | `tcp.analysis.retransmission` | 重传密度与方向 |
| 端口不通 | 抓 SYN 看有无 RST | 被拒还是无人应答 |
| 定位中间人 | 看证书颁发者与 TTL | 证书链异常要警惕 |

## 网络排错任务清单

按这个顺序走，多数"网络玄学"能收敛到具体层。清单按"由外到内、由粗到细"排列：先在应用层确认现象，再用抓包落到协议层，顺序反了容易在噪音里打转：

- [ ] 先 `ping` 与 `telnet 主机 端口`，确认三层可达与端口开放
- [ ] 设好抓包过滤器再开抓，只留目标主机与端口，减少噪音
- [ ] 复现问题，立刻停止抓包，文件名带时间戳
- [ ] 先用显示过滤器缩小范围：`ip.addr` 定主机，`tcp.port` 定端口
- [ ] 打开 Expert Information，看重传、重复 ACK、RST 归类
- [ ] 对关键连接 Follow TCP Stream，读完整对话内容
- [ ] 用 Statistics → Conversations 判断流量走向是否异常
- [ ] 导出关键包（File → Export Specified Packets）存档，附环境说明

## 踩坑记录

> **坑一：网卡列表空白或点了没反应。** 权限问题。Linux 确认自己在 `wireshark` 组并重新登录；macOS 需要在"安全性与隐私"里允许 BPF 设备访问；Windows 以管理员身份运行。

> **坑二：抓了一堆包，找不到目标流量。** 抓包过滤器没设或者太宽。开抓前先想清楚"看哪两台机器、哪个端口、哪个协议"，把流量限制到几 MB，读起来才快。

> **坑三：本机回环抓不到。** 服务与本机客户端通信可能走 `lo` 接口而不是物理网卡，接口选 `any` 或 `Loopback`。WSL 与虚拟机的流量还要确认走的是虚拟交换机哪一侧。

> **坑四：HTTPS 全是乱码。** TLS 1.3 之后看不到明文是正常的，除非服务端把预主密钥导出（`SSLKEYLOGFILE`）并在 Wireshark 里配置。这也是设计使然，不是工具坏了。

> **坑五：抓包文件巨大打不开。** 长时间全流量抓包轻松几个 G。用环形缓冲：`dumpcap -b filesize:100000 -b files:10`，只保留最近 10 个 100MB 文件，覆盖旧的。

> **坑六：抓到包但看不到应用数据。** 先检查抓包位置：容器内抓只能看到容器自己的流量，跨主机通信要在网关或交换机镜像端口抓。另外 NAT 环境下容器 IP 是内网地址，映射回公网 IP 要靠 `Statistics → Endpoints` 结合环境信息人工对，别指望工具自动猜。

---

## 速查小结

- 抓包过滤器（BPF）在开抓前设，决定"抓什么"；显示过滤器事后用，决定"看什么"。
- 三招读包：Follow Stream 看对话、Statistics 看全局、Expert Information 看异常。
- 排错从下往上：ping 通不通、端口开不开、包到没到、对方回没回。
- 权限是第一门槛，抓不到包先查组、查管理员身份。
- 命令行抓包用 tshark，`-Y` 复用显示过滤器，`-T fields` 提字段。
- TLS 1.3 看不到明文是特性不是 bug，需要密钥日志文件才行。
- 大流量用环形缓冲抓包，别把磁盘写满才发现抓错了。

抓包是网络排错的最后仲裁者：日志会说谎，配置会骗人，字节流不会。最后一条经验：抓包前先想清楚"要证明什么"，目标明确的包比无脑全量抓有效一百倍；能复现问题的抓包，比任何口头描述都值一个下午的时间。

[^1]: Linux 上抓包不直接把 Wireshark 跑成 root，而是让 dumpcap 以最小权限访问网卡，用户加入 `wireshark` 组即可，兼顾安全与可用。
