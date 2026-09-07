@echo off
REM Flint 性能测试运行脚本 (Windows CMD)
REM 运行性能测试并生成 HTML 报告

setlocal enabledelayedexpansion

echo.
echo ========================================
echo   Flint 性能测试
echo ========================================
echo.

REM 获取脚本目录
set "SCRIPT_DIR=%~dp0"
set "Flint_DIR=%SCRIPT_DIR%.."

REM 切换到 Flint 目录
pushd "%Flint_DIR%"

REM 设置输出路径
if "%1"=="" (
    set "OUTPUT_PATH=TestResults\performance-report.html"
) else (
    set "OUTPUT_PATH=%1"
)

REM 构建项目
echo [1/3] 构建项目...
dotnet build -c Release --nologo -v q
if errorlevel 1 (
    echo 构建失败!
    popd
    exit /b 1
)
echo       构建完成
echo.

REM 创建输出目录
for %%i in ("%OUTPUT_PATH%") do set "OUTPUT_DIR=%%~dpi"
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

REM 运行性能测试
echo [2/3] 运行性能测试...
echo.

dotnet run --project tests\Flint.PerformanceTests\Flint.PerformanceTests.csproj -c Release -- "%OUTPUT_PATH%"
set "EXIT_CODE=%errorlevel%"

echo.
if %EXIT_CODE%==0 (
    echo [SUCCESS] 所有性能测试通过!
) else (
    echo [WARNING] 部分性能测试未达到基准
)

echo.
echo [3/3] 报告已生成: %OUTPUT_PATH%

REM 询问是否打开报告
if "%2"=="--open" (
    echo 正在打开报告...
    start "" "%OUTPUT_PATH%"
)

popd
exit /b %EXIT_CODE%
