# Flint 性能回归门禁
# 跑性能套件并与基线对比，关键指标劣化超阈值即 FAIL（棘轮：基线只随确认的改进更新）
# 用法: powershell -File scripts/perf-gate.ps1 [-UpdateBaseline]

param(
    [switch]$UpdateBaseline,
    [string]$BaselinePath = "$PSScriptRoot\perf-baseline.json",
    [string]$OutputPath = "$PSScriptRoot\..\TestResults\perf-gate-run.html"
)

$ErrorActionPreference = "Stop"

Write-Host "🚦 Flint 性能门禁" -ForegroundColor Cyan
Write-Host "==================" -ForegroundColor Cyan

if (-not (Test-Path $BaselinePath)) {
    throw "基线文件不存在: $BaselinePath"
}
$baseline = Get-Content $BaselinePath -Raw -Encoding UTF8 | ConvertFrom-Json
$threshold = [double]$baseline.threshold_percent

# 跑性能套件
Write-Host "🧪 运行性能套件（约 10-15 分钟）..." -ForegroundColor Yellow
dotnet run --project "tests\Flint.PerformanceTests\Flint.PerformanceTests.csproj" -c Release -- $OutputPath
if ($LASTEXITCODE -ne 0) {
    Write-Host "⚠️ 套件自身有未达标项（阈值判定），继续做基线对比" -ForegroundColor Yellow
}

# 解析 HTML 报告：test-card 内的 metric-name/actual
$html = Get-Content $OutputPath -Raw -Encoding UTF8
$cards = $html -split 'class="test-card'
$results = @{}
foreach ($card in $cards) {
    $titleMatch = [regex]::Match($card, 'test-title[^>]*>([^<]+)<')
    if (-not $titleMatch.Success) { $titleMatch = [regex]::Match($card, '<h[23][^>]*>([^<]+)<') }
    if (-not $titleMatch.Success) { continue }
    $testName = $titleMatch.Groups[1].Value.Trim()
    $pairs = [regex]::Matches($card, 'metric-name">([^<]+)</div>(?s).*?metric-actual">([^<]+)<')
    foreach ($p in $pairs) {
        $results["$testName|$($p.Groups[1].Value.Trim())"] = [double]$p.Groups[2].Value.Trim()
    }
}

# 基线对比：构建时间类指标按"测试名|构建时间"键映射
$map = @{
    'site_build_100'       = @('小型站点构建 (100页)', '构建时间')
    'site_build_500'       = @('中型站点构建 (500页)', '构建时间')
    'site_build_1000'      = @('大型站点构建 (1000页)', '构建时间')
    'site_build_10000'     = @('超大型站点构建 (10000页)', '构建时间')
    'complex_theme_1000'   = @('复杂主题站点构建 (1000页)', '构建时间')
    'incremental_build'    = @('增量构建性能', '增量构建时间')
    'markdown_parse_1000'  = @('Markdown 解析性能', '解析时间')
    'template_render_1000' = @('模板渲染性能', '渲染时间')
    'concurrent_build'     = @('并发构建性能', '多线程时间')
}

$fail = 0
foreach ($key in $baseline.metrics.PSObject.Properties.Name) {
    if (-not $map.ContainsKey($key)) { continue }
    $entry = $baseline.metrics.$key
    $baseMs = [double]$entry.ms
    $lookup = $map[$key]
    $actualKey = "$($lookup[0])|$($lookup[1])"
    if (-not $results.ContainsKey($actualKey)) {
        Write-Host "  ⚠️ 未在报告中找到: $actualKey" -ForegroundColor Yellow
        continue
    }
    $actual = $results[$actualKey]
    $limit = $baseMs * (1 + $threshold / 100)
    $pct = if ($baseMs -gt 0) { [math]::Round(($actual - $baseMs) / $baseMs * 100, 1) } else { 0 }
    if ($actual -gt $limit) {
        Write-Host ("  [FAIL] {0}: {1:F1}ms > 基线 {2}ms x (1+{3}%) = {4:F1}ms (+{5}%)" -f $entry.label, $actual, $baseMs, $threshold, $limit, $pct) -ForegroundColor Red
        $fail++
    } else {
        Write-Host ("  [PASS] {0}: {1:F1}ms (基线 {2}ms, {3}%)" -f $entry.label, $actual, $baseMs, $pct) -ForegroundColor Green
    }
}

if ($UpdateBaseline) {
    Write-Host "基线更新请手工编辑 $BaselinePath（确认改进后才允许上调，棘轮语义）" -ForegroundColor Yellow
}

if ($fail -gt 0) {
    Write-Host "`n🚫 性能门禁 FAIL：$fail 项显著劣化（>$threshold%）" -ForegroundColor Red
    exit 1
}
Write-Host "`n✅ 性能门禁通过：无显著劣化" -ForegroundColor Green
