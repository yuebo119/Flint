// Flint 静态站点生成器
// API 面快照测试：运行时枚举真实注册的函数与命名空间，防止能力表漂移。
// 迁移工具引用 Flint.Core 同源枚举，此测试守住"文档声称的能力 = 实际注册的能力"。

using Flint.Core.Templates;
using Scriban.Runtime;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class ApiSurfaceSnapshotTests
{
    private static ScriptObject BuildSurface()
    {
        var obj = new ScriptObject();
        new BuiltinTemplateFunctions("https://example.com", null, null).RegisterFunctions(obj);
        return obj;
    }

    [Fact]
    public void 全局函数数不低于基线()
    {
        var root = BuildSurface();
        var globals = root.Keys.OfType<string>().Where(k => !k.Contains('.')).ToList();
        // 基线：阶段 0 前的 141 个 + 本阶段补充的 74 个（含别名）
        Assert.True(globals.Count >= 200, $"全局函数数 {globals.Count} 低于基线 200");
    }

    [Fact]
    public void 命名空间全部可枚举且非空()
    {
        var root = BuildSurface();
        var namespaces = root.Keys.OfType<string>()
            .Where(k => !k.Contains('.') && root[k] is ScriptObject)
            .Where(k => new[]
            {
                "strings", "collections", "compare", "cast", "crypto", "encoding", "hash",
                "transform", "urls", "path", "inflect", "safe", "math", "fmt", "reflect",
                "templates", "os", "lang", "debug", "hugo", "resources", "css", "js", "images"
            }.Contains(k))
            .ToList();

        Assert.True(namespaces.Count >= 20, $"命名空间数 {namespaces.Count} 低于 20");

        // 关键命名空间必须有成员（空命名空间说明别名映射全线失效）
        foreach (var ns in new[] { "strings", "collections", "compare", "math", "hugo" })
        {
            var count = ((ScriptObject?)root[ns])!.Count;
            Assert.True(count > 0, $"命名空间 {ns} 为空（别名映射失效）");
        }
    }

    [Fact]
    public void 迁移器依赖的关键函数存在()
    {
        // 4 主题矩阵实测用量 Top（迁移工具的映射目标必须存在）
        var root = BuildSurface();
        foreach (var fn in new[]
        {
            "where", "sort", "dict", "slice", "delimit", "merge", "default", "cond",
            "markdownify", "plainify", "truncate", "replace", "split", "abs_url", "rel_url",
            "humanize", "pluralize", "singularize", "safe_html", "jsonify"
        })
        {
            Assert.True(root.ContainsKey(fn), $"关键函数 {fn} 缺失（迁移映射会失败）");
        }
    }
}
