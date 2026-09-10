// Flint 静态站点生成器
// 模块管理器实现（AOT 兼容）

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flint.Core.Abstractions;
using Flint.Core.Configuration;
using Tomlyn;
using Tomlyn.Model;

namespace Flint.Core.Modules;

/// <summary>
/// 模块管理器 - 管理主题和模块的安装、更新和依赖解析
/// 供应链安全：版本锁定于 Flint.lock（含 SHA256），下载有超时与大小上限，失败显式报错不静默降级
/// </summary>
public sealed partial class ModuleManager : IDisposable
{
    private const int DownloadTimeoutSeconds = 30;

    /// <summary>
    /// 下载包大小上限（100MB），防止超大响应耗尽内存
    /// </summary>
    private const long MaxDownloadBytes = 100L * 1024 * 1024;

    private readonly string _modulesPath;
    private readonly string _lockFilePath;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public ModuleManager(string sitePath)
    {
        _modulesPath = Path.Combine(sitePath, "themes");
        _lockFilePath = Path.Combine(sitePath, "Flint.lock");
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(DownloadTimeoutSeconds)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Flint/1.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async ValueTask<ModuleDescriptor> GetAsync(string repository, string? version = null, CancellationToken cancellationToken = default)
    {
        // 安装源分发（对齐 Hugo 生态现实：主题存量以 git repo 形态托管）：
        // ① git URL / *.git → git clone；② 本地目录 → 复制安装；③ owner/repo → GitHub Releases。
        // .git 后缀判定先于目录判定："<repo>/.git" 是真实目录但语义是 git 仓库
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ||
            repository.Contains("://"))
        {
            return await InstallFromGitAsync(repository, version, cancellationToken);
        }

        if (Directory.Exists(repository))
        {
            return await InstallFromLocalAsync(repository, cancellationToken);
        }

        // owner/repo 形态：@ref 后缀（tag/branch）走 git；纯 tag/版本走 Releases
        string? gitRef = null;
        var atSign = repository.IndexOf('@');
        if (atSign > 0)
        {
            gitRef = repository[(atSign + 1)..];
            repository = repository[..atSign];
            return await InstallFromGitAsync(
                $"https://github.com/{ParseRepository(repository)}.git", gitRef, cancellationToken);
        }

        var (owner, repo) = ParseRepository(repository);

        // lockfile 中已固定版本的模块优先于 latest（保证可复现安装）
        version ??= await GetLockVersionAsync(repo, cancellationToken)
                    ?? await GetLatestVersionAsync(owner, repo, cancellationToken);

        var modulePath = Path.Combine(_modulesPath, repo);
        var sha256 = await DownloadModuleAsync(owner, repo, version, modulePath, cancellationToken);
        var descriptor = await ReadModuleDescriptorAsync(modulePath, cancellationToken);
        await UpdateLockFileAsync(repo, descriptor, version, sha256, cancellationToken);
        return descriptor;
    }

    /// <summary>
    /// 本地路径安装：目录必须含 theme.toml，复制到 themes/&lt;name&gt;（已有则报错，
    /// 更新走 update 语义）；lockfile 记录 source=local
    /// </summary>
    private async ValueTask<ModuleDescriptor> InstallFromLocalAsync(string sourcePath, CancellationToken ct)
    {
        var themeToml = Path.Combine(sourcePath, "theme.toml");
        if (!File.Exists(themeToml))
        {
            throw new InvalidOperationException($"本地主题缺少 theme.toml: {sourcePath}");
        }

        var descriptor = await ReadModuleDescriptorAsync(sourcePath, ct);
        ValidateModuleName(descriptor.Name);
        var modulePath = Path.Combine(_modulesPath, descriptor.Name);
        if (Directory.Exists(modulePath))
        {
            throw new InvalidOperationException($"模块已存在: {descriptor.Name}（先 remove 再安装）");
        }

        Directory.CreateDirectory(_modulesPath);
        CopyDirectory(sourcePath, modulePath);
        var verified = await ReadModuleDescriptorAsync(modulePath, ct);
        await UpdateLockFileAsync(descriptor.Name, verified, "local", "local", ct);
        return verified;
    }

    /// <summary>
    /// git 安装：clone --depth 1 到临时目录，取 HEAD 短 SHA 写 lockfile；
    /// ref 为空时用默认分支。git 不可用/仓库无 theme.toml 时显式报错
    /// </summary>
    private async ValueTask<ModuleDescriptor> InstallFromGitAsync(string gitUrl, string? refName, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flint-mod-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var args = refName is null
                ? $"clone --depth 1 --quiet \"{gitUrl}\" \"{tempDir}\""
                : $"clone --depth 1 --quiet --branch \"{refName}\" \"{gitUrl}\" \"{tempDir}\"";
            var (code, output, error) = await RunGitAsync(args, ct);
            if (code != 0)
            {
                throw new InvalidOperationException($"git clone 失败（{gitUrl}@{refName ?? "默认分支"}）: {error}{output}");
            }

            var descriptor = await ReadModuleDescriptorAsync(tempDir, ct);
            ValidateModuleName(descriptor.Name);
            var modulePath = Path.Combine(_modulesPath, descriptor.Name);
            if (Directory.Exists(modulePath))
            {
                throw new InvalidOperationException($"模块已存在: {descriptor.Name}（先 remove 再安装）");
            }

            Directory.CreateDirectory(_modulesPath);
            CopyDirectory(tempDir, modulePath);
            // HEAD 短 SHA 是 lockfile 的最佳努力信息：部分环境（杀软实时扫描锁新写
            // pack 文件）会令紧随 clone 的读取被拒，此时不阻断安装，sha 记 "unknown"
            var headSha = "unknown";
            try
            {
                var (_, shaOut, _) = await RunGitAsync("-C \"" + tempDir + "\" rev-parse HEAD", ct);
                var trimmed = shaOut.Trim();
                if (trimmed.Length > 0)
                {
                    headSha = trimmed[..Math.Min(12, trimmed.Length)];
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            var verified = await ReadModuleDescriptorAsync(modulePath, ct);
            await UpdateLockFileAsync(descriptor.Name, verified, refName ?? headSha, headSha, ct);
            return verified;
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch (IOException) { }
        }
    }

    private static async Task<(int Code, string Output, string Error)> RunGitAsync(string arguments, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 git 进程（git 是否已安装？）");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, output, error);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            if (rel.StartsWith(".git", StringComparison.Ordinal))
            {
                continue; // 不搬运 git 元数据
            }
            Directory.CreateDirectory(Path.Combine(target, rel));
        }
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (rel.StartsWith(".git", StringComparison.Ordinal) ||
                rel.Split(Path.DirectorySeparatorChar).Any(p => p is ".git" or ".gitignore" && p == ".git"))
            {
                continue;
            }
            File.Copy(file, Path.Combine(target, rel), overwrite: true);
        }
    }

    public async ValueTask UpdateAsync(string moduleName, string? version = null, CancellationToken cancellationToken = default)
    {
        ValidateModuleName(moduleName);
        var modulePath = Path.Combine(_modulesPath, moduleName);
        if (!Directory.Exists(modulePath))
            throw new InvalidOperationException($"模块未安装: {moduleName}");
        var descriptor = await ReadModuleDescriptorAsync(modulePath, cancellationToken);
        var (owner, repo) = ParseRepository(descriptor.Repository);

        // 已安装版本以 lockfile 为准（theme.toml 里的是主题自述版本，不是安装版本）
        version ??= await GetLatestVersionAsync(owner, repo, cancellationToken);
        var installedVersion = await GetLockVersionAsync(moduleName, cancellationToken);
        if (version == installedVersion)
        {
            return;
        }

        var sha256 = await DownloadModuleAsync(owner, repo, version, modulePath, cancellationToken);
        var newDesc = new ModuleDescriptor { Name = descriptor.Name, Version = version, Repository = descriptor.Repository };
        await UpdateLockFileAsync(moduleName, newDesc, version, sha256, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ModuleDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var modules = new List<ModuleDescriptor>();
        if (!Directory.Exists(_modulesPath))
            return modules;
        foreach (var dir in Directory.GetDirectories(_modulesPath))
        {
            // 损坏的模块目录（缺 theme.toml）跳过但保留其他条目，不让整个列表失败
            try
            { modules.Add(await ReadModuleDescriptorAsync(dir, cancellationToken)); }
            catch (Exception ex) when (ex is IOException or Tomlyn.TomlException) { }
        }
        return modules;
    }

    public async ValueTask InitAsync(string path, CancellationToken cancellationToken = default)
    {
        var configPath = Path.Combine(path, "Flint.toml");
        if (!File.Exists(configPath))
            await File.WriteAllTextAsync(configPath, "# Flint 站点配置\n\n[module]\n", cancellationToken);
        var themesPath = Path.Combine(path, "themes");
        if (!Directory.Exists(themesPath))
            Directory.CreateDirectory(themesPath);
    }

    public async ValueTask RemoveAsync(string moduleName, CancellationToken cancellationToken = default)
    {
        // 模块名来自 CLI 参数：themes/.. 会把删除目标解析为站点根，递归删除不可逆
        ValidateModuleName(moduleName);
        var modulePath = Path.Combine(_modulesPath, moduleName);
        if (!Directory.Exists(modulePath))
            throw new InvalidOperationException($"模块未安装: {moduleName}");
        Directory.Delete(modulePath, recursive: true);
        await RemoveFromLockFileAsync(moduleName, cancellationToken);
    }

    /// <summary>模块名必须为安全文件名段（拒绝路径分隔符与 . / .. 父目录引用）</summary>
    private static void ValidateModuleName(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        if (!ModuleNameRegex().IsMatch(moduleName))
        {
            throw new ArgumentException($"无效的模块名: {moduleName}");
        }
    }

    private static (string owner, string repo) ParseRepository(string repository)
    {
        repository = repository.Replace("https://", "").Replace("http://", "").Replace("github.com/", "").TrimEnd('/');
        // 校验 owner/repo 字符集：防止 ?、#、../ 之类注入 URL 与路径
        if (!RepoPathRegex().IsMatch(repository))
            throw new ArgumentException($"无效的仓库格式（应为 owner/repo）: {repository}");
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(p => p is "." or ".."))
            throw new ArgumentException($"无效的仓库格式（应为 owner/repo）: {repository}");
        return (parts[0], parts[1]);
    }

    private async Task<string?> GetLockVersionAsync(string moduleName, CancellationToken ct)
    {
        if (!File.Exists(_lockFilePath))
            return null;
        var content = await File.ReadAllTextAsync(_lockFilePath, ct);
        if (TomlynCompat.TryParseTable(content) is not TomlTable lockData)
            return null;
        if (lockData.TryGetValue(moduleName, out var entry) && entry is TomlTable table &&
            table.TryGetValue("version", out var version))
        {
            return version?.ToString();
        }
        return null;
    }

    /// <summary>
    /// 获取最新 release 版本。失败显式抛出：静默回退 main 会把任意时刻的 HEAD 当成稳定版安装。
    /// </summary>
    private async Task<string> GetLatestVersionAsync(string owner, string repo, CancellationToken ct)
    {
        var response = await _httpClient.GetStringAsync(
            new Uri($"https://api.github.com/repos/{owner}/{repo}/releases/latest"), ct);
        using var json = JsonDocument.Parse(response);
        var tag = json.RootElement.GetProperty("tag_name").GetString();
        if (string.IsNullOrEmpty(tag))
        {
            throw new InvalidOperationException($"仓库 {owner}/{repo} 的最新 release 缺少 tag_name，请显式指定版本（--version）");
        }
        return tag;
    }

    /// <summary>
    /// 下载并解压模块到目标路径，返回包内容的 SHA256（写入 lockfile 供完整性校验）
    /// </summary>
    private async Task<string> DownloadModuleAsync(string owner, string repo, string version, string targetPath, CancellationToken ct)
    {
        // version 可能来自 CLI --version：未经校验直接拼进下载 URL 是注入点
        if (!VersionRegex().IsMatch(version))
        {
            throw new ArgumentException($"无效的版本号: {version}");
        }
        var zipData = await DownloadZipWithLimitAsync(
            new Uri($"https://github.com/{owner}/{repo}/archive/refs/tags/{version}.zip"), ct);
        await ExtractModuleAsync(zipData, targetPath, ct);
        return Convert.ToHexStringLower(SHA256.HashData(zipData));
    }

    /// <summary>
    /// 流式下载 zip 并强制大小上限（GetByteArrayAsync 无上限，超大响应会耗尽内存）
    /// </summary>
    private async Task<byte[]> DownloadZipWithLimitAsync(Uri uri, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(uri, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
        {
            throw new InvalidOperationException($"下载超过大小上限 ({MaxDownloadBytes / (1024 * 1024)}MB): {uri}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > MaxDownloadBytes)
            {
                throw new InvalidOperationException($"下载超过大小上限 ({MaxDownloadBytes / (1024 * 1024)}MB): {uri}");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// 原子解压：先解压到目标目录旁的临时目录，再整体换入；失败保留原安装
    /// </summary>
    private static async Task ExtractModuleAsync(byte[] zipData, string targetPath, CancellationToken ct)
    {
        var tempPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        var backupPath = targetPath + ".old-" + Guid.NewGuid().ToString("N");
        try
        {
            await System.IO.Compression.ZipFile.ExtractToDirectoryAsync(
                new MemoryStream(zipData), tempPath, ct);
            var extractedDir = Directory.GetDirectories(tempPath).FirstOrDefault() ?? tempPath;

            var hasOld = Directory.Exists(targetPath);
            if (hasOld)
                Directory.Move(targetPath, backupPath);
            try
            {
                Directory.Move(extractedDir, targetPath);
            }
            catch
            {
                // 换入失败时恢复旧安装，避免"旧已删新未装"的破坏现场
                if (hasOld && !Directory.Exists(targetPath))
                    Directory.Move(backupPath, targetPath);
                throw;
            }
            if (hasOld)
                Directory.Delete(backupPath, recursive: true);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    private static async Task<ModuleDescriptor> ReadModuleDescriptorAsync(string modulePath, CancellationToken ct)
    {
        var configPath = Path.Combine(modulePath, "theme.toml");
        if (!File.Exists(configPath))
            configPath = Path.Combine(modulePath, "module.toml");
        if (!File.Exists(configPath))
            return new ModuleDescriptor { Name = Path.GetFileName(modulePath), Version = "unknown", Repository = "", LocalPath = modulePath };

        var content = await File.ReadAllTextAsync(configPath, ct);
        // 使用 Tomlyn 低级 API（AOT 兼容）
        var config = TomlynCompat.TryParseTable(content);
        if (config is null)
            return new ModuleDescriptor { Name = Path.GetFileName(modulePath), Version = "unknown", Repository = "", LocalPath = modulePath };
        return new ModuleDescriptor
        {
            Name = GetTomlString(config, "name") ?? Path.GetFileName(modulePath),
            Version = GetTomlString(config, "version") ?? "1.0.0",
            Repository = GetTomlString(config, "repository") ?? "",
            // A4 元数据：主题自述信息（mod list 展示 + min_version 兼容提示）
            Description = GetTomlString(config, "description"),
            License = GetTomlString(config, "license"),
            MinVersion = GetTomlString(config, "min_version"),
            Tags = GetTomlStringList(config, "tags"),

            LocalPath = modulePath
        };
    }

    private static IReadOnlyList<string> GetTomlStringList(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlArray array)
        {
            return [];
        }
        return array.Select(v => v?.ToString() ?? "").Where(s => s.Length > 0).ToArray();
    }

    private async Task UpdateLockFileAsync(string lockKey, ModuleDescriptor descriptor, string installedVersion, string sha256, CancellationToken ct)
    {
        var lockData = new TomlTable();
        if (File.Exists(_lockFilePath))
        {
            var content = await File.ReadAllTextAsync(_lockFilePath, ct);
            if (TomlynCompat.TryParseTable(content) is TomlTable existing)
            {
                foreach (var kvp in existing)
                    lockData[kvp.Key] = kvp.Value;
            }
        }

        // 模块条目：安装版本 + 仓库 + 内容哈希（完整性校验与可复现安装的依据）。
        // 键必须用安装/更新时的模块名（repo 名）——读取（GetLockVersionAsync）与
        // 移除（RemoveFromLockFileAsync）都用该名；用第三方 theme.toml 的 name
        // 会在两者不一致时让版本锁定与移除双双失效。
        // 引号键包裹——裸键含空格/引号/点时会写出非法 TOML
        var moduleEntry = new TomlTable
        {
            ["version"] = installedVersion,
            ["repository"] = descriptor.Repository,
            ["sha256"] = sha256
        };
        lockData[lockKey] = moduleEntry;

        // 手动序列化为 TOML（AOT 兼容）
        await File.WriteAllTextAsync(_lockFilePath, SerializeTomlTable(lockData), ct);
    }

    private async Task RemoveFromLockFileAsync(string moduleName, CancellationToken ct)
    {
        if (!File.Exists(_lockFilePath))
            return;
        var content = await File.ReadAllTextAsync(_lockFilePath, ct);
        if (TomlynCompat.TryParseTable(content) is not TomlTable lockData)
            return;

        if (lockData.Remove(moduleName))
            await File.WriteAllTextAsync(_lockFilePath, SerializeTomlTable(lockData), ct);
    }

    // AOT 兼容的辅助方法
    private static string? GetTomlString(TomlTable table, string key)
    {
        return table.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static string SerializeTomlTable(TomlTable table)
    {
        var sb = new StringBuilder();
        foreach (var kvp in table)
        {
            if (kvp.Value is TomlTable subTable)
            {
                // 键统一用引号键：key 可能含空格/引号/点（第三方 theme.toml 的 name），
                // 裸写含点子键会产出非法 TOML
                sb.AppendLine($"[\"{EscapeTomlString(kvp.Key)}\"]");
                foreach (var subKvp in subTable)
                {
                    sb.AppendLine($"\"{EscapeTomlString(subKvp.Key)}\" = \"{EscapeTomlString(subKvp.Value?.ToString() ?? "")}\"");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine($"\"{EscapeTomlString(kvp.Key)}\" = \"{EscapeTomlString(kvp.Value?.ToString() ?? "")}\"");
            }
        }
        return sb.ToString();
    }

    private static string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
    private static partial Regex RepoPathRegex();

    // 模块名：安全文件名段，排除 . / ..（防 themes/.. 解析为站点根后递归删除）
    [GeneratedRegex("^(?!\\.+($|[/\\\\]))[A-Za-z0-9_.-]+$")]
    private static partial Regex ModuleNameRegex();

    // 版本号：tag/参数直接拼进下载 URL，仅允许安全字符
    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex VersionRegex();

    public void Dispose() { if (!_disposed) { _disposed = true; _httpClient.Dispose(); } }
}
