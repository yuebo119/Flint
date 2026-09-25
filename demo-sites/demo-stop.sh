#!/usr/bin/env bash
# 停掉演示站服务（单进程 demo-serve.py + 历史遗留的 per-port http.server）。
#
# 为什么需要：Windows 下 python 解释器的 CWD 位于 public/ 内会锁住目录，
# 重建时 rm -rf 报 "Device or resource busy"；多轮启动还会残留僵孤进程
# （实测 22 个 listener 对应 26 个 python 进程，多出的 4 个不服务任何端口）。
#
# 用法: bash demo-sites/demo-stop.sh

set -u

# 单进程服务：命令行含 demo-serve.py
powershell.exe -NoProfile -Command "
  Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" |
    Where-Object { \$_.CommandLine -like '*demo-serve.py*' } |
    ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }
" > /dev/null 2>&1

# 历史遗留：每主题一个 http.server（旧启动方式）
powershell.exe -NoProfile -Command "
  Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" |
    Where-Object { \$_.CommandLine -like '*http.server*' -and \$_.CommandLine -notlike '*demo-serve*' } |
    ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }
" > /dev/null 2>&1

# 兜底：按 84xx 监听端口反查 PID（前两条漏掉其他 python 时）
netstat -ano 2>/dev/null | grep -E ':84[0-9][0-9]' | grep -i listening | awk '{print $5}' | sort -u | while read -r pid; do
  case "$pid" in ''|*[!0-9]*) continue ;; esac
  taskkill //F //PID "$pid" > /dev/null 2>&1
done

echo "演示站服务已停止。"
