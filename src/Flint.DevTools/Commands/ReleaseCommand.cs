// release 子命令组：GitHub 发布页标准工具（scripts/release-github.py 的 C# 移植）
// CONVENTIONS §10.2 发布流第 4/5 步的机械执行：
//   - 标题 = 纯版本号（不带后缀）
//   - 变更日志必须来自 --notes 文件，禁止 commit 链接/git 日志直贴
//   - 凭据取自 git credential store，全程不回显不落盘（P0 #1）
//   网络：先直连、失败后退回本机代理 127.0.0.1:50001（与 Python 版双尝试语义一致）

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Flint.DevTools.Commands;

/// <summary>
/// release 工具的可预期失败（对应 Python sys.exit(message)：stderr 消息 + 退出码 1）
/// </summary>
internal sealed class ReleaseFailureException : Exception
{
    public ReleaseFailureException()
    {
    }

    public ReleaseFailureException(string message)
        : base(message)
    {
    }

    public ReleaseFailureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// release 子命令组
/// </summary>
internal static class ReleaseCommand
{
    internal const string Repo = "yuebo119/Flint";
    private const string Workflow = "release-assets.yml";
    private const string DefaultRef = "main"; // release 触发从默认分支读流水线（2026-09-25 裁决）

    internal static Command Build()
    {
        var cmd = new Command("release", "GitHub 发布页工具（替代 scripts/release-github.py）");
        cmd.Subcommands.Add(BuildVerify());
        cmd.Subcommands.Add(BuildCreate());
        cmd.Subcommands.Add(BuildDispatch());
        cmd.Subcommands.Add(BuildWait());
        cmd.Subcommands.Add(BuildUpdateNotes());
        return cmd;
    }

    // ---------------------------------------------------------------- verify

    private static Command BuildVerify()
    {
        var cmd = new Command("verify", "只读体检（无需凭据）：标题纯度/资产 label/远端 tags");

        cmd.SetAction(_ => GuardAsync(async () =>
        {
            using var client = GitHubClient.Create();
            var releases = await client.GetJsonArray($"{GitHubClient.ApiBase}/releases").ConfigureAwait(false);
            if (releases.NetError is not null || releases.HttpError is not null)
            {
                Fail($"releases 查询失败: {Describe(releases)}");
            }

            Console.WriteLine($"releases={releases.Items.GetArrayLength()}");
            var ok = true;
            foreach (var release in releases.Items.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                var name = release.GetPropertyOrNull("name")?.GetString();
                var draft = release.GetPropertyOrNull("draft")?.GetBoolean() ?? false;
                var assets = release.GetPropertyOrNull("assets");

                Console.WriteLine($"  tag={tag} name={Repr(name)} draft={draft} assets={(assets?.GetArrayLength() ?? 0)}");

                if (name != tag)
                {
                    ok = false;
                    Console.WriteLine("    ✗ 标题不是纯版本号");
                }

                if (assets is null)
                {
                    continue;
                }

                foreach (var asset in assets.Value.EnumerateArray())
                {
                    var assetName = asset.GetProperty("name").GetString() ?? "";
                    var size = asset.GetProperty("size").GetInt64();
                    var label = asset.GetPropertyOrNull("label");
                    var labelText = label?.ValueKind is JsonValueKind.String ? label.Value.GetString() : null;

                    Console.WriteLine($"    {assetName} | {size.ToString("N0", CultureInfo.InvariantCulture)} bytes | label={Repr(labelText)}");
                    if (!string.IsNullOrEmpty(labelText))
                    {
                        ok = false;
                        Console.WriteLine("    ✗ 资产带 label");
                    }
                }
            }

            var tags = ListRemoteTags();
            Console.WriteLine($"远端 tags: [{string.Join(", ", tags.Select(t => $"'{t}'"))}]");
            Console.WriteLine($"体检: {(ok ? "PASS" : "FAIL")}");
            return ok ? 0 : 1;
        }));

        return cmd;
    }

    // ---------------------------------------------------------------- create

    private static Command BuildCreate()
    {
        var tagOpt = new Option<string>("--tag") { Description = "标签（vX.Y.Z）", Required = true };
        var notesOpt = new Option<string>("--notes") { Description = "变更日志文件（总结式，禁止 git 日志）", Required = true };
        var expectOpt = new Option<int>("--expect") { Description = "预期资产数", DefaultValueFactory = _ => 4 };
        var timeoutOpt = new Option<int>("--timeout") { Description = "等待秒数", DefaultValueFactory = _ => 780 };

        var cmd = new Command("create", "建发布页 + 等待四平台资产");
        cmd.Options.Add(tagOpt);
        cmd.Options.Add(notesOpt);
        cmd.Options.Add(expectOpt);
        cmd.Options.Add(timeoutOpt);

        cmd.SetAction(parseResult => GuardAsync(async () =>
        {
            var tag = parseResult.GetValue(tagOpt)!;
            var notes = parseResult.GetValue(notesOpt)!;
            var expect = parseResult.GetValue(expectOpt);
            var timeout = parseResult.GetValue(timeoutOpt);

            if (!tag.StartsWith('v'))
            {
                Fail("错误：标签须为 vX.Y.Z 格式");
            }

            var body = LoadNotes(notes);
            var token = RequireToken();
            using var client = GitHubClient.Create();

            var release = await client.PostJsonAsync(
                $"{GitHubClient.ApiBase}/releases", token, new
                {
                    tag_name = tag,
                    name = tag,
                    body,
                    draft = false,
                    prerelease = false,
                }).ConfigureAwait(false);

            RequireReleaseId(release);
            Console.WriteLine($"发布页已创建: {release.Items.GetProperty("html_url").GetString()}");
            Console.WriteLine("published 事件将自动触发 release-assets 流水线");

            return await WaitAssetsAsync(client, token, tag, expect, timeout).ConfigureAwait(false);
        }));

        return cmd;
    }

    // ---------------------------------------------------------------- dispatch

    private static Command BuildDispatch()
    {
        var tagOpt = new Option<string>("--tag") { Description = "标签（vX.Y.Z）", Required = true };
        var notesOpt = new Option<string>("--notes") { Description = "变更日志文件（与建页路径同时使用时必填）" };
        var expectOpt = new Option<int>("--expect") { Description = "预期资产数", DefaultValueFactory = _ => 4 };
        var timeoutOpt = new Option<int>("--timeout") { Description = "等待秒数", DefaultValueFactory = _ => 780 };
        var waitOnlyOpt = new Option<bool>("--wait-only") { Description = "不建页，只等待既有页的资产" };

        var cmd = new Command("dispatch", "补建：派发 release-assets 流水线（默认随后建页并等待）");
        cmd.Options.Add(tagOpt);
        cmd.Options.Add(notesOpt);
        cmd.Options.Add(expectOpt);
        cmd.Options.Add(timeoutOpt);
        cmd.Options.Add(waitOnlyOpt);

        cmd.SetAction(parseResult => GuardAsync(async () =>
        {
            var tag = parseResult.GetValue(tagOpt)!;
            var notes = parseResult.GetValue(notesOpt);
            var expect = parseResult.GetValue(expectOpt);
            var timeout = parseResult.GetValue(timeoutOpt);
            var waitOnly = parseResult.GetValue(waitOnlyOpt);

            var token = RequireToken();
            using var client = GitHubClient.Create();

            var dispatch = await client.PostJsonAsync(
                $"{GitHubClient.ApiBase}/actions/workflows/{Workflow}/dispatches", token, new
                {
                    @ref = DefaultRef,
                    inputs = new { tag },
                }).ConfigureAwait(false);

            if (dispatch.Status != 204)
            {
                Fail($"派发失败: {Describe(dispatch)}");
            }

            Console.WriteLine($"已派发 {Workflow}（ref={DefaultRef}, tag={tag}）");
            await Task.Delay(10_000).ConfigureAwait(false);

            if (waitOnly)
            {
                return await WaitAssetsAsync(client, token, tag, expect, timeout).ConfigureAwait(false);
            }

            if (!tag.StartsWith('v'))
            {
                Fail("错误：标签须为 vX.Y.Z 格式");
            }

            var body = LoadNotes(notes);
            var release = await client.PostJsonAsync(
                $"{GitHubClient.ApiBase}/releases", token, new
                {
                    tag_name = tag,
                    name = tag,
                    body,
                    draft = false,
                    prerelease = false,
                }).ConfigureAwait(false);

            RequireReleaseId(release);
            Console.WriteLine($"发布页已创建: {release.Items.GetProperty("html_url").GetString()}");
            Console.WriteLine("published 事件将自动触发 release-assets 流水线");

            return await WaitAssetsAsync(client, token, tag, expect, timeout).ConfigureAwait(false);
        }));

        return cmd;
    }

    // ---------------------------------------------------------------- wait

    private static Command BuildWait()
    {
        var tagOpt = new Option<string>("--tag") { Description = "标签（vX.Y.Z）", Required = true };
        var expectOpt = new Option<int>("--expect") { Description = "预期资产数", DefaultValueFactory = _ => 4 };
        var timeoutOpt = new Option<int>("--timeout") { Description = "等待秒数", DefaultValueFactory = _ => 780 };

        var cmd = new Command("wait", "只等待既有发布页的资产");
        cmd.Options.Add(tagOpt);
        cmd.Options.Add(expectOpt);
        cmd.Options.Add(timeoutOpt);

        cmd.SetAction(parseResult => GuardAsync(async () =>
        {
            var tag = parseResult.GetValue(tagOpt)!;
            var expect = parseResult.GetValue(expectOpt);
            var timeout = parseResult.GetValue(timeoutOpt);

            var token = RequireToken();
            using var client = GitHubClient.Create();
            return await WaitAssetsAsync(client, token, tag, expect, timeout).ConfigureAwait(false);
        }));

        return cmd;
    }

    // ---------------------------------------------------------------- update-notes

    private static Command BuildUpdateNotes()
    {
        var tagOpt = new Option<string>("--tag") { Description = "标签（vX.Y.Z）", Required = true };
        var notesOpt = new Option<string>("--notes") { Description = "变更日志文件", Required = true };

        var cmd = new Command("update-notes", "更新既有发布页的说明正文（守卫同建页）");
        cmd.Options.Add(tagOpt);
        cmd.Options.Add(notesOpt);

        cmd.SetAction(parseResult => GuardAsync(async () =>
        {
            var tag = parseResult.GetValue(tagOpt)!;
            var notes = parseResult.GetValue(notesOpt)!;

            var body = LoadNotes(notes);
            var token = RequireToken();
            using var client = GitHubClient.Create();

            var releases = await client.GetJsonAsync($"{GitHubClient.ApiBase}/releases", token).ConfigureAwait(false);
            if (releases.NetError is not null || releases.HttpError is not null)
            {
                Fail($"releases 查询失败: {Describe(releases)}");
            }

            JsonElement? release = null;
            foreach (var r in releases.Items.EnumerateArray())
            {
                if (r.GetProperty("tag_name").GetString() == tag)
                {
                    release = r;
                    break;
                }
            }

            if (release is null)
            {
                Fail($"错误：未找到 {tag} 的发布页");
                return 1;
            }

            var id = release.Value.GetProperty("id").GetInt64();
            var patched = await client.PatchJsonAsync($"{GitHubClient.ApiBase}/releases/{id}", token, new { body }).ConfigureAwait(false);
            var patchedBody = patched.Items.ValueKind == JsonValueKind.Object
                && patched.Items.TryGetProperty("body", out var b)
                ? b.GetString()
                : null;

            if (patchedBody == body)
            {
                Console.WriteLine($"说明页已更新: {patched.Items.GetProperty("html_url").GetString()}");
                return 0;
            }

            Fail($"更新失败: {Describe(patched)}");
            return 1;
        }));

        return cmd;
    }

    // ---------------------------------------------------------------- 共享逻辑

    private static async Task<int> WaitAssetsAsync(
        GitHubClient client, string token, string tag, int expect, int timeoutSeconds)
    {
        var deadline = TimeSpan.FromSeconds(timeoutSeconds);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < deadline)
        {
            var releases = await client.GetJsonAsync($"{GitHubClient.ApiBase}/releases", token).ConfigureAwait(false);
            if (releases.NetError is not null || releases.HttpError is not null)
            {
                Fail($"releases 查询失败: {Describe(releases)}");
            }

            JsonElement? release = null;
            foreach (var r in releases.Items.EnumerateArray())
            {
                if (r.GetProperty("tag_name").GetString() == tag)
                {
                    release = r;
                    break;
                }
            }

            var assets = release is not null ? release.Value.GetPropertyOrNull("assets") : null;
            var count = assets?.GetArrayLength() ?? 0;

            if (release is not null && count >= expect)
            {
                Console.WriteLine($"资产齐备（{count}/{expect}）:");
                PrintAssets(assets!.Value);

                var bad = new List<string>();
                foreach (var a in assets.Value.EnumerateArray())
                {
                    var label = a.GetPropertyOrNull("label");
                    if (label?.ValueKind is JsonValueKind.String && !string.IsNullOrEmpty(label.Value.GetString()))
                    {
                        bad.Add(a.GetProperty("name").GetString() ?? "");
                    }
                }

                if (bad.Count > 0)
                {
                    Fail($"错误：以下资产带 label（应直显文件名）: {string.Join(", ", bad)}");
                }

                return 0;
            }

            Console.WriteLine($"  等待资产 {count}/{expect} ...");
            await Task.Delay(15_000).ConfigureAwait(false);
        }

        Console.WriteLine("超时：资产未达预期数");
        return 1;
    }

    private static void PrintAssets(JsonElement assets)
    {
        Console.WriteLine($"资产 {assets.GetArrayLength()} 项:");
        foreach (var a in assets.EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var size = a.GetProperty("size").GetInt64();
            var label = a.GetPropertyOrNull("label");
            var labelText = label?.ValueKind is JsonValueKind.String ? label.Value.GetString() : null;
            Console.WriteLine($"  {name} | {size.ToString("N0", CultureInfo.InvariantCulture)} bytes | label={Repr(labelText)}");
        }
    }

    private static string LoadNotes(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Fail("错误：--notes 必须指向存在的变更日志文件（总结式：主要特性/主要更新/主要更改）");
        }

        var body = File.ReadAllText(path!, Encoding.UTF8).Trim();
        if (body.Length == 0)
        {
            Fail("错误：变更日志文件为空");
        }

        if (body.Contains("/commit/", StringComparison.Ordinal)
            || body.Replace(" ", string.Empty).Contains("commit/", StringComparison.Ordinal))
        {
            Fail("错误：变更日志禁止 commit 链接/git 日志直贴——请写总结（主要特性/主要更新/主要更改）");
        }

        return body;
    }

    /// <summary>从 git credential store 取 github.com 凭据（不回显不落盘）</summary>
    private static string? GitPassword()
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("credential");
        psi.ArgumentList.Add("fill");

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        process.StandardInput.Write("protocol=https\nhost=github.com\n\n");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(30_000);

        foreach (var line in stdout.Split('\n'))
        {
            if (line.StartsWith("password=", StringComparison.Ordinal))
            {
                return line["password=".Length..];
            }
        }

        return null;
    }

    private static string RequireToken()
    {
        var token = GitPassword() ?? string.Empty;
        if (token.Length == 0)
        {
            Fail("错误：git 凭据库中无 github.com 凭据");
        }

        return token;
    }

    private static List<string> ListRemoteTags()
    {
        var psi = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("ls-remote");
        psi.ArgumentList.Add("--tags");
        psi.ArgumentList.Add("origin");

        using var process = Process.Start(psi);
        var stdout = process?.StandardOutput.ReadToEnd() ?? string.Empty;
        var tags = new List<string>();
        foreach (var line in stdout.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.Contains("^{}", StringComparison.Ordinal))
            {
                continue;
            }

            var idx = trimmed.LastIndexOf("refs/tags/", StringComparison.Ordinal);
            if (idx >= 0)
            {
                tags.Add(trimmed[(idx + "refs/tags/".Length)..]);
            }
        }

        return tags;
    }

    private static void RequireReleaseId(ApiResult release)
    {
        if (release.Items.ValueKind != JsonValueKind.Object || !release.Items.TryGetProperty("id", out _))
        {
            Fail($"建页失败: {Describe(release)}");
        }
    }

    private static int Guard(Func<int> body)
    {
        try
        {
            return body();
        }
        catch (ReleaseFailureException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<int> GuardAsync(Func<Task<int>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (ReleaseFailureException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void Fail(string message) => throw new ReleaseFailureException(message);

    private static string Describe(ApiResult result)
    {
        if (result.NetError is not null)
        {
            return $"网络错误: {result.NetError}";
        }

        if (result.HttpError is not null)
        {
            return $"HTTP {result.HttpError}";
        }

        return result.Items.ToString();
    }

    /// <summary>Python repr 风格显示：null → None，字符串加单引号</summary>
    private static string Repr(string? s) => s is null ? "None" : $"'{s}'";
}

/// <summary>
/// JsonElement 辅助：按名取属性，不存在返回 null
/// </summary>
internal static class JsonExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : null;
    }
}

/// <summary>
/// GitHub REST 调用结果（JSON 正文、状态码、网络/HTTP 错误三态）
/// </summary>
internal sealed record ApiResult
{
    public required JsonElement Items { get; init; }

    public int? Status { get; init; }

    public int? HttpError { get; init; }

    public string? NetError { get; init; }
}

/// <summary>
/// GitHub REST 客户端：先直连、失败后退回本机代理（与 Python 版双尝试语义一致）
/// </summary>
internal sealed class GitHubClient : IDisposable
{
    private const string ProxyAddress = "http://127.0.0.1:50001";

    private static readonly JsonDocumentOptions DocOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private GitHubClient(HttpClient http)
    {
        Http = http;
    }

    private HttpClient Http { get; }

    public static string ApiBase => $"https://api.github.com/repos/{ReleaseCommand.Repo}";

    public static GitHubClient Create() => new(new HttpClient { Timeout = TimeSpan.FromSeconds(300) });

    public void Dispose() => Http.Dispose();

    public async Task<ApiResult> GetJsonAsync(string url, string? token = null)
    {
        return await SendAsync(url, token, body: null).ConfigureAwait(false);
    }

    public Task<ApiResult> GetJsonArray(string url)
    {
        return GetJsonAsync(url);
    }

    public Task<ApiResult> PostJsonAsync(string url, string token, object body)
    {
        return SendAsync(url, token, body);
    }

    public Task<ApiResult> PatchJsonAsync(string url, string token, object body)
    {
        return SendAsync(url, token, body, HttpMethod.Patch);
    }

    private static async Task<ApiResult> SendAsync(string url, string? token, object? body, HttpMethod? method = null)
    {
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Accept", "application/vnd.github+json"),
            new("User-Agent", "flint-release-github"),
        };

        // 与 Python 版一致：先直连，再退回本机代理
        var attempts = new Func<HttpClientHandler>[]
        {
            () => new HttpClientHandler(),
            () => new HttpClientHandler { Proxy = new WebProxy(ProxyAddress) },
        };

        string? lastNetError = null;
        foreach (var makeHandler in attempts)
        {
            using var handler = makeHandler();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(300) };
            try
            {
                using var request = new HttpRequestMessage(method ?? (body is null ? HttpMethod.Get : HttpMethod.Post), url);
                foreach (var (key, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }

                if (token is not null)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                }

                // body 每次尝试新建（HttpContent 不可复用）
                if (body is not null)
                {
                    request.Content = JsonContent.Create(body);
                }

                using var response = await http.SendAsync(request).ConfigureAwait(false);
                var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // HTTP 错误也返回 JSON 错误体（与 Python 解析 e.read() 一致）
                    return new ApiResult
                    {
                        Items = ParseOrNull(raw),
                        Status = (int)response.StatusCode,
                        HttpError = (int)response.StatusCode,
                    };
                }

                return new ApiResult
                {
                    Items = raw.Length == 0 ? EmptyObject() : JsonDocument.Parse(raw, DocOptions).RootElement.Clone(),
                    Status = (int)response.StatusCode,
                };
            }
            catch (HttpRequestException ex)
            {
                lastNetError = $"{ex.GetType().Name}: {Truncate(ex.Message, 100)}";
            }
            catch (TaskCanceledException ex)
            {
                lastNetError = $"{ex.GetType().Name}: {Truncate(ex.Message, 100)}";
            }
        }

        return new ApiResult
        {
            Items = EmptyObject(),
            NetError = lastNetError ?? "unknown",
        };
    }

    private static JsonElement ParseOrNull(string raw)
    {
        return raw.Length == 0 ? EmptyObject() : JsonDocument.Parse(raw, DocOptions).RootElement.Clone();
    }

    private static JsonElement EmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
