# Flint 性能回归门禁
# 三轮跑性能套件取逐键中位数并与基线对比，关键指标劣化超阈值即 FAIL（棘轮：基线只随确认的改进更新）
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

# 跑性能套件：三轮取逐键中位数（压 ±19% 级单轮环境摆动；阈值与基线语义不变）
$runCount = 3
Write-Host "🧪 运行性能套件 ×$runCount（约 15-20 分钟，逐键三轮中位）..." -ForegroundColor Yellow

function Parse-GateReport([string]$path) {
    $html = Get-Content $path -Raw -Encoding UTF8
    $cards = $html -split 'class="test-card'
    $parsed = @{}
    foreach ($card in $cards) {
        $titleMatch = [regex]::Match($card, 'test-title[^>]*>([^<]+)<')
        if (-not $titleMatch.Success) { $titleMatch = [regex]::Match($card, '<h[23][^>]*>([^<]+)<') }
        if (-not $titleMatch.Success) { continue }
        $testName = $titleMatch.Groups[1].Value.Trim()
        $pairs = [regex]::Matches($card, 'metric-name">([^<]+)</div>(?s).*?metric-actual">([^<]+)<')
        foreach ($p in $pairs) {
            $parsed["$testName|$($p.Groups[1].Value.Trim())"] = [double]$p.Groups[2].Value.Trim()
        }
    }
    return $parsed
}

function Get-Median([double[]]$values) {
    $sorted = @($values | Sort-Object)
    $n = $sorted.Count
    if ($n % 2 -eq 1) { return [double]$sorted[[int](($n - 1) / 2)] }
    return ([double]$sorted[$n / 2 - 1] + [double]$sorted[$n / 2]) / 2
}

$runs = @{}
for ($i = 1; $i -le $runCount; $i++) {
    # 首轮写标准输出路径（供既有消费方），其余轮写并列文件
    $runPath = if ($i -eq 1) { $OutputPath } else { "$PSScriptRoot\..\TestResults\perf-gate-run.run$i.html" }
    Write-Host "  ── 轮 $i/$runCount ──" -ForegroundColor DarkYellow
    dotnet run --project "tests\Flint.PerformanceTests\Flint.PerformanceTests.csproj" -c Release -- $runPath
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ⚠️ 第 $i 轮套件自身有未达标项（阈值判定），继续做基线对比" -ForegroundColor Yellow
    }
    if (-not (Test-Path $runPath)) { throw "第 $i 轮报告未生成: $runPath" }
    $runs[$i] = Parse-GateReport $runPath
}

# 逐键三轮中位（某轮缺键时按现有轮取，不整键丢弃）
$results = @{}
$allKeys = @($runs.Values | ForEach-Object { $_.Keys } | Sort-Object -Unique)
foreach ($k in $allKeys) {
    $vals = @()
    for ($i = 1; $i -le $runCount; $i++) {
        if ($runs[$i].ContainsKey($k)) { $vals += [double]$runs[$i][$k] }
    }
    if ($vals.Count -eq 0) { continue }
    $results[$k] = Get-Median $vals
    if ($vals.Count -gt 1) {
        $min = ($vals | Measure-Object -Minimum).Minimum
        $max = ($vals | Measure-Object -Maximum).Maximum
        Write-Host ("    {0}: 中位 {1:F1}ms（三轮 {2:F1}~{3:F1}）" -f $k, $results[$k], $min, $max) -ForegroundColor DarkGray
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
