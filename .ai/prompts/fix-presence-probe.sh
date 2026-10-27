#!/usr/bin/env bash
# 已知缺陷修复存在性探针：判定当前工作树的起点是否已含本项目历次审计的修复。
#
# 用途：任何审计或性能轮开始前跑一次。出现 ABSENT 说明起点与预期不符（如 dev 未同步），
# 先报告再决定是否继续，避免在已修项上重复审计，或在未修项上做性能优化。
#
# 用法：bash .ai/prompts/fix-presence-probe.sh [仓库根]
# 输出：每条一行 PRESENT / ABSENT + 探针说明
# 退出码：恒为 0（信息收集，不是门禁）

set -u
ROOT="${1:-$(pwd)}"
cd "$ROOT" || { echo "无法进入 $ROOT"; exit 0; }

pass=0; fail=0
check() {
  local name="$1" file="$2" pattern="$3"
  if [ ! -f "$file" ]; then
    echo "ABSENT  [$name] 缺文件 $file"
    fail=$((fail+1)); return
  fi
  if grep -qE "$pattern" "$file" 2>/dev/null; then
    echo "PRESENT [$name] $file"
    pass=$((pass+1))
  else
    echo "ABSENT  [$name] $file 未命中 /$pattern/"
    fail=$((fail+1))
  fi
}

echo "== 仓库：$(git rev-parse --short HEAD 2>/dev/null || echo '非 git')"
echo "== 分支：$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"
echo

# ---- P0：产物正确性 ----
check "P0-1 XML 产物声明编码" src/Flint.Core/Site/XmlOutput.cs "Utf8Preamble"
check "P0-1 sitemap 走 XmlOutput" src/Flint.Core/Site/SitemapGenerator.cs "XmlOutput\.WithPreamble"
check "P0-2 front matter 大小写归一" src/Flint.Core/Content/FrontMatterParser.DictMapping.cs "WithCaseInsensitiveKeys"
check "P0-3 关闭 InvariantGlobalization" Directory.Build.props "InvariantGlobalization>false"
check "P0-3 CLI 钉不变文化" src/Flint.Cli/Program.cs "DefaultThreadCurrentCulture = CultureInfo\.InvariantCulture"
check "P0-4 feed 时间戳取页面日期" src/Flint.Core/Site/FeedGenerator.cs "LatestFeedDate"
check "P0-4 合成列表页零值日期" src/Flint.Core/Site/SiteBuilder.Tree.cs "Date = DateTimeOffset\.MinValue"
check "P0-4 permalink 无日期零值" src/Flint.Core/Site/PermalinkEngine.cs "DateTimeOffset\.MinValue"

# ---- P1：配置与安全 ----
check "P1-1 覆盖后校验" src/Flint.Core/Configuration/ConfigLoader.cs "private static SiteConfig Finalize"
check "P1-1 目录式配置走 Finalize" src/Flint.Core/Configuration/ConfigLoader.cs "Finalize\(ThemeParamsMerger\.Merge"
check "P1-2 theme 路径校验" src/Flint.Core/Configuration/ConfigValidator.cs "config\.ThemeNames"
check "P1-3 Fluid include 根内校验" src/Flint.Core/Templates/FluidTemplateRenderer.cs "IsUnderRoot"
check "P1-3 Fluid 递归上限" src/Flint.Core/Templates/FluidTemplateRenderer.cs "MaxRecursion = 500"
check "P1-4 truncate 非正长度守卫" src/Flint.Core/Templates/BuiltinTemplateFunctions.cs "if \(length <= 0\)"
check "P1-5 aliases 写盘去重" src/Flint.Core/Site/SiteBuilder.Output.cs "aliasTargets"
check "P1-6 --verbose 递归" src/Flint.Cli/Program.cs "Recursive = true"
check "P1-6 publishDir 生效" src/Flint.Cli/Handlers/BuildHandler.cs "siteConfig\.PublishDir"
check "P1-7 clean 守卫含源内容目录" src/Flint.Cli/Handlers/BuildHandler.cs "IsDangerousClean"
check "P1-7 gh-pages 分支存在即中止" src/Flint.Cli/Handlers/DeployHandler.cs "ls-remote --exit-code"
check "P1-7 ThemeMigrator 目标守卫" src/Flint.ThemeMigrator/Migration/ThemeMigrator.cs "拒绝迁移"
check "P1-8 测试按配置选 CLI 产物" tests/Flint.IntegrationTests/Utilities/CliTestRunner.cs "ownConfig"
check "P1-9 CI 覆盖工具链测试" .github/workflows/ci.yml "Flint.ThemeMigrator.Tests"
check "P1-10 git 参数逐项传递" src/Flint.Core/Modules/ModuleManager.cs "ArgumentList"
check "P1-10 lockfile 哈希比对" src/Flint.Core/Modules/ModuleManager.cs "VerifyAgainstLockAsync"
check "P1-10 解压体积/条目上限" src/Flint.Core/Modules/ModuleManager.cs "MaxExtractedEntries"
check "P1-11 模板路径缓存失效" src/Flint.Core/Templates/FileTemplateLoader.cs "ClearPathCache"

# ---- P2 ----
check "P2 内容软诊断（未闭合/非 UTF-8）" src/Flint.Core/Site/SiteBuilder.Tree.cs "LooksLikeUnterminatedFrontMatter"
check "P2 WS Origin 校验" src/Flint.Core/Server/DevServer.cs "IsSameOrigin"
check "P2 钩子变更按全量" src/Flint.Core/Server/DevServer.cs "_markup"
check "P2 partialCached 按构建清空" src/Flint.Core/Templates/ScribanTemplateRenderer.cs "_partialResultCache\.Clear\(\);\s*$"
check "P2 产物队列按构建清空" src/Flint.Core/Templates/TemplateResourceFunctions.cs "ClearGeneratedResources"
check "P2 taxonomy feed 上下文按构建清空" src/Flint.Core/Site/SiteBuilder.cs "TaxonomyFeedContexts\.Clear"
check "P2 缓存 blob 越界校验" src/Flint.Core/Content/ContentCacheStore.cs "IsInBuffer"
check "P2 模块身份反查" src/Flint.Core/Modules/ModuleManager.cs "ResolveInstalledNameAsync"
check "P2 临时目录尽力清理" src/Flint.Core/Modules/ModuleManager.cs "TryDeleteDirectory"
check "P2 archetypeDir 生效" src/Flint.Cli/Handlers/ContentCreator.cs "TryLoadSiteConfigAsync"
check "P2 渲染钩子按 mtime 重读" src/Flint.Core/Content/RenderHooks.cs "HookTemplateFile"

echo
echo "== 汇总：PRESENT=$pass ABSENT=$fail"
echo "== 判读：ABSENT 非 0 说明起点未含对应修复，先报告，不要直接开始审计或性能优化。"
echo "==       PRESENT 的条目只做回归验证，不重读代码。"
