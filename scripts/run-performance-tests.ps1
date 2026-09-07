# Flint 性能测试运行脚本
# 运行性能测试并生成 HTML 报告

param(
    [string]$OutputPath = ".\TestResults\performance-report.html",
    [switch]$OpenReport
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Flint 性能测试" -ForegroundColor Cyan
Write-Host "==================" -ForegroundColor Cyan
Write-Host ""

# 确保在正确的目录
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$FlintDir = Split-Path -Parent $scriptDir

Push-Location $FlintDir

try {
    # 构建项目
    Write-Host "📦 构建项目..." -ForegroundColor Yellow
    dotnet build -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        throw "构建失败"
    }
    Write-Host "   ✓ 构建完成" -ForegroundColor Green
    Write-Host ""

    # 创建输出目录
    $outputDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    # 运行性能测试
    Write-Host "🧪 运行性能测试..." -ForegroundColor Yellow
    Write-Host ""
    
    $testProject = "tests\Flint.PerformanceTests\Flint.PerformanceTests.csproj"
    
    # 使用 dotnet run 运行性能测试
    dotnet run --project $testProject -c Release -- $OutputPath
    
    $exitCode = $LASTEXITCODE

    Write-Host ""
    if ($exitCode -eq 0) {
        Write-Host "✅ 所有性能测试通过!" -ForegroundColor Green
    } else {
        Write-Host "⚠️ 部分性能测试未达到基准" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "📄 报告位置: $OutputPath" -ForegroundColor Cyan

    # 打开报告
    if ($OpenReport -and (Test-Path $OutputPath)) {
        Write-Host "🌐 正在打开报告..." -ForegroundColor Cyan
        Start-Process $OutputPath
    }

    exit $exitCode
}
finally {
    Pop-Location
}
