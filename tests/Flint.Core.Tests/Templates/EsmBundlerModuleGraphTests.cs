// EsmBundler 模块图能力测试：无扩展名 import 解析（stack 的 './menu'）、
// @params 虚拟模块（fixit/stack 的 js.Build params 选项）、默认/命名/命名空间
// 导入重写、TS 入口类型剥离。这些是 js.Build 对 .ts 主题入口全链路的地基

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class EsmBundlerModuleGraphTests : IDisposable
{
    private readonly string _dir;

    public EsmBundlerModuleGraphTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "flint-esm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "js"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* 忽略清理错误 */ }
    }

    private void Write(string rel, string content)
    {
        var path = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void 无扩展名import解析到ts依赖()
    {
        // stack 的 main.ts：import menu from './menu'（无扩展名，实际命中 menu.ts）
        Write("ts/menu.ts", "export default function menu() { return 1 }\n");
        Write("ts/main.ts", "import menu from './menu'\nmenu()\n");
        var entry = Path.Combine(_dir, "ts", "main.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(entry, File.ReadAllText(entry));

        Assert.NotNull(bundled);
        Assert.DoesNotContain("import ", bundled!);
        Assert.DoesNotContain("export ", bundled);
        // 默认导入重写为对注册表 .default 的引用；命名默认导出函数挂 .default
        Assert.Contains("var menu = __flint_mods[", bundled);
        Assert.Contains("].default;", bundled);
        Assert.Contains("__flint_exp.default = menu;", bundled);
    }

    [Fact]
    public void params选项注入params虚拟模块()
    {
        Write("js/head/index.ts",
            "import params from '@params'\nexport function init() { return params.defaultTheme }\n");
        var entry = Path.Combine(_dir, "js", "head", "index.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(
            entry, File.ReadAllText(entry), "{\"defaultTheme\":\"auto\"}");

        Assert.NotNull(bundled);
        // 虚拟模块：默认导出 + 顶层键命名导出（默认导入取 .default）
        Assert.Contains("var __flint_params = {\"defaultTheme\":\"auto\"};", bundled);
        Assert.Contains("__flint_exp.default = __flint_params;", bundled);
        Assert.Contains("const defaultTheme = __flint_params.defaultTheme;", bundled);
        Assert.Contains("var params = __flint_mods['@params'].default;", bundled);
    }

    [Fact]
    public void 命名空间导入取params命名导出()
    {
        Write("ts/main.ts",
            "import * as params from '@params'\nexport function copy() { return params.codeblock.copy }\n");
        var entry = Path.Combine(_dir, "ts", "main.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(
            entry, File.ReadAllText(entry), "{\"codeblock\":{\"copy\":\"Copy\"}}");

        Assert.NotNull(bundled);
        // 命名空间 import → 整个模块对象；命名导出来自虚拟模块的顶层键
        Assert.Contains("var params = __flint_mods['@params'];", bundled);
        Assert.Contains("const codeblock = __flint_params.codeblock;", bundled);
        Assert.Contains("__flint_exp.codeblock = codeblock;", bundled);
    }

    [Fact]
    public void 命名导入支持别名与export列表()
    {
        Write("js/a.ts", "export function one() {}\nexport function two() {}\n");
        Write("js/b.ts", "export { one as uno, two } from './a'\n");
        Write("js/entry.ts", "import { uno, two as dos } from './b'\nuno(); dos();\n");
        var entry = Path.Combine(_dir, "js", "entry.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(entry, File.ReadAllText(entry));

        Assert.NotNull(bundled);
        Assert.DoesNotContain("import ", bundled!);
        Assert.DoesNotContain("export ", bundled);
        Assert.Contains("var uno = __flint_mods[", bundled);
        Assert.Contains("].one;", bundled);
        Assert.Contains("var dos = __flint_mods[", bundled);
        Assert.Contains("].two;", bundled);
    }

    [Fact]
    public void type_only导入与export_type再导出被删除()
    {
        Write("js/tokens.ts", "export interface Api { x: number }\nexport const v = 1\n");
        Write("js/entry.ts",
            "import type { Api } from './tokens'\nexport type * from './tokens'\nimport { v } from './tokens'\nexport function use() { return v }\n");
        var entry = Path.Combine(_dir, "js", "entry.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(entry, File.ReadAllText(entry));

        Assert.NotNull(bundled);
        Assert.DoesNotContain("import ", bundled!);
        Assert.DoesNotContain("export ", bundled);
        Assert.DoesNotContain("interface", bundled);
        Assert.Contains("var v = __flint_mods[", bundled);
    }

    [Fact]
    public void 匿名默认导出函数挂载default()
    {
        // stack 的 menu.ts：export default function () { … }（匿名）
        Write("ts/menu.ts", "export default function () {\n  return 1\n}\n");
        Write("ts/main.ts", "import menu from './menu'\nmenu()\n");
        var entry = Path.Combine(_dir, "ts", "main.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(entry, File.ReadAllText(entry));

        Assert.NotNull(bundled);
        // 匿名默认导出必须是挂载形态（裸 function () {} 不是合法语句）
        Assert.Contains("__flint_exp.default = function ()", bundled);
        Assert.DoesNotContain("export ", bundled!);
    }

    [Fact]
    public void 星型再导出建依赖边()
    {
        // fixit 的 utils/index.ts 是 barrel：export * from './x'。再导出
        // 不建依赖边会让目标模块不入队、具名导出全丢
        // （createCopyText is not a function 实测）
        Write("utils/clipboard.ts", "export function createCopyText() { return 1 }\n");
        Write("utils/index.ts", "export * from './clipboard'\n");
        Write("main.ts", "import { createCopyText } from './utils'\ncreateCopyText()\n");
        var entry = Path.Combine(_dir, "main.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(entry, File.ReadAllText(entry));

        Assert.NotNull(bundled);
        // 目标模块已入队且其导出被星型复制到 barrel
        Assert.Contains("for (var __fk in __flint_mods[", bundled);
        Assert.Contains("__flint_exp.createCopyText = createCopyText;", bundled);
        // 入口的具名导入解析到 barrel 的导出
        Assert.Contains("var createCopyText = __flint_mods[", bundled);
    }

    [Fact]
    public void format_esm输出保留默认导出()
    {
        // stack 的 photoswipe：`js.Build (dict "format" "esm")` + 页面动态
        // `import gallery from '/ts/gallery.js'`——产物必须是带 default 导出的 ES 模块
        Write("ts/gallery.ts", "export default (container: HTMLElement) => {\n  return container\n}\n");
        var entry = Path.Combine(_dir, "ts", "gallery.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(
            entry, File.ReadAllText(entry), null, esmOutput: true);

        Assert.NotNull(bundled);
        Assert.Contains("export default __flint_mods[", bundled);
        Assert.Contains("].default;", bundled);
        // 经典输出（默认）不应有 export 语句
        var classic = BuiltinTemplateFunctions.EsmBundler.Bundle(entry, File.ReadAllText(entry));
        Assert.DoesNotContain("export ", classic!);
    }

    [Fact]
    public void format_esm输出保留命名导出()
    {
        Write("ts/lib.ts", "export function one() {}\nexport function two() {}\n");
        var entry = Path.Combine(_dir, "ts", "lib.ts");

        var bundled = BuiltinTemplateFunctions.EsmBundler.Bundle(
            entry, File.ReadAllText(entry), null, esmOutput: true);

        Assert.NotNull(bundled);
        Assert.Contains("const one = __flint_mods[", bundled);
        Assert.Contains("export { one };", bundled);
        Assert.Contains("export { two };", bundled);
    }

    [Fact]
    public void 裸模块名依赖打包失败返回null()
    {
        Write("js/entry.ts", "import vue from 'vue'\nconsole.log(vue)\n");
        var entry = Path.Combine(_dir, "js", "entry.ts");

        Assert.Null(BuiltinTemplateFunctions.EsmBundler.Bundle(entry, File.ReadAllText(entry)));
    }
}
