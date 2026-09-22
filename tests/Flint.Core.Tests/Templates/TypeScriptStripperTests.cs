// TypeScriptStripper 单元测试：覆盖 fixit/stack 两主题实际用到的 TS 子集。
// 每个用例对应主题资产里真实出现的语法形态（剥离器漏一种，浏览器就报
// SyntaxError——js.Build 的 .ts 入口全链路依赖它）

using Flint.Core.Templates;
using Xunit;

namespace Flint.Core.Tests.Templates;

public sealed class TypeScriptStripperTests
{
    private static string Strip(string ts) => BuiltinTemplateFunctions.TypeScriptStripper.Strip(ts);

    [Fact]
    public void 非空断言剥离()
    {
        var r = Strip("function f(b) { downloadAsFile(b.textContent!, 1) }");
        Assert.DoesNotContain("!", r);
        Assert.Contains("downloadAsFile(b.textContent, 1)", r);
    }

    [Fact]
    public void 非空断言在模板字面量之后仍生效()
    {
        // 回归：${...} 配平 depth 错曾把收尾反引号当嵌套模板，吞掉后续代码
        var r = Strip("function f(a, b) { const t1 = `code.${a}`\ndownloadAsFile(b.textContent!, 1) }");
        Assert.DoesNotContain("!", r);
        Assert.Contains("`code.${a}`", r);
    }

    [Fact]
    public void as转换与联合类型剥离()
    {
        var r = Strip("const d = document.getElementById('x') as HTMLDialogElement\nconst n = d as HTMLElement | null");
        Assert.DoesNotContain(" as ", r);
        Assert.DoesNotContain("HTMLDialogElement", r);
    }

    [Fact]
    public void 链式as与泛型实参()
    {
        var r = Strip("const e = event as CustomEvent<{ a: number } | null>\nconst c = new Map<string, number>()");
        Assert.DoesNotContain("CustomEvent", r);
        Assert.Contains("new Map()", r);
    }

    [Fact]
    public void interface与type别名整块删除()
    {
        var ts = "export interface CoreService {\n  readonly config: FixItConfig\n  isDark: boolean\n}\n" +
                 "type Mode = 'light' | 'dark'\nconst x = 1";
        var r = Strip(ts);
        Assert.DoesNotContain("interface", r);
        Assert.DoesNotContain("FixItConfig", r);
        Assert.DoesNotContain("'light'", r);
        Assert.Contains("const x = 1", r);
    }

    [Fact]
    public void export_type再导出整语句删除()
    {
        var ts = "export type * from './config'\nexport type { A } from './global'\nconst y = 2";
        var r = Strip(ts);
        Assert.DoesNotContain("from", r);
        Assert.Contains("const y = 2", r);
    }

    [Fact]
    public void declare全局声明删除()
    {
        var ts = "declare global {\n  interface Window {\n    Stack: any\n  }\n}\nconst z = 3";
        var r = Strip(ts);
        Assert.DoesNotContain("declare", r);
        Assert.DoesNotContain("Window", r);
        Assert.Contains("const z = 3", r);
    }

    [Fact]
    public void 类成员修饰符与类型注解剥离()
    {
        var ts = "class A {\n  private key = 'k'\n  private mode: Mode;\n  private save() {}\n" +
                 "  bind(el: HTMLElement): void {}\n  #abort: AbortController | undefined\n}";
        var r = Strip(ts);
        Assert.DoesNotContain("private", r);
        Assert.DoesNotContain("Mode", r);
        Assert.DoesNotContain("HTMLElement", r);
        Assert.Contains("#abort", r);
        Assert.Contains("bind(el)", r);
    }

    [Fact]
    public void implements子句剥离保留类体()
    {
        var r = Strip("export class PublicAPI implements FixItPublicAPI {\n  run() {}\n}");
        Assert.DoesNotContain("implements", r);
        Assert.DoesNotContain("FixItPublicAPI", r);
        Assert.Contains("class PublicAPI", r);
        Assert.Contains("run() {}", r);
    }

    [Fact]
    public void 箭头函数参数与返回类型()
    {
        var r = Strip("const f = (consent?: State | null): boolean => { return true }");
        Assert.Contains("const f = (consent) =>", r);
        Assert.DoesNotContain("State", r);
    }

    [Fact]
    public void 对象字面量与三元不被误剥()
    {
        var r = Strip("const o = { a: 1, b: 'x' }\nconst t = cond ? f(a) : g(b)\nconst u = { k: v ? 1 : 2 }");
        Assert.Contains("{ a: 1, b: 'x' }", r);
        Assert.Contains("cond ? f(a) : g(b)", r);
        Assert.Contains("{ k: v ? 1 : 2 }", r);
    }

    [Fact]
    public void 小于号比较不被当泛型()
    {
        var r = Strip("if (i < len) { return }\nconst ok = a < b > (c)");
        Assert.Contains("i < len", r);
    }

    [Fact]
    public void 对象字面量实参内的三元不被误剥()
    {
        // 回归：反向扫描曾在对象字面量的 } 处误停，漏看 ? 把三元 : 当返回类型剥掉
        var r = Strip("f(a ? new X(1, { detail }) : new Y(2))");
        Assert.Contains("? new X(1, { detail }) : new Y(2)", r);
    }

    [Fact]
    public void 代码块内变量注解且上一行有三元()
    {
        // 回归：反向扫描曾跨越无分号语句的换行边界，把上一行三元的 ?
        // 泄漏到本行 : 判定，块内变量注解漏剥（fixit code.ts 实测）
        var ts = "f() {\n  a.forEach((b) => {\n    const x = y !== -1 ? y : z\n    const g: H[] = []\n  })\n}";
        var r = Strip(ts);
        Assert.Contains("const x = y !== -1 ? y : z", r);
        Assert.Contains("const g = []", r);
        Assert.DoesNotContain("H[]", r);
    }

    [Fact]
    public void 条件类型参数与后续返回类型()
    {
        // fixit event-bus.ts 的实际形态
        var ts = "emit<K extends keyof M>(\n    event: K,\n    ...args: M[K] extends void ? [] : [M[K]]\n  ): void {\n    const detail = args[0]\n  }";
        var r = Strip(ts);
        Assert.Contains("emit(", r);
        Assert.Contains("...args", r);
        Assert.DoesNotContain("extends", r);
        Assert.DoesNotContain(": void", r);
        Assert.Contains("const detail = args[0]", r);
    }

    [Fact]
    public void 返回类型后的函数体内变量注解()
    {
        // 回归：剥掉返回类型后 prevMeaningful 变 ":"，函数体 { 曾被误判为
        // 对象字面量，块内变量注解全部漏剥（fixit algolia.ts 实测）
        var ts = "export function f(cfg: C): E {\n  let idx: {\n    search: (q: string) => Promise<{ hits: any[] }>\n  } | null = null\n}";
        var r = Strip(ts);
        Assert.Contains("let idx = null", r);
        Assert.DoesNotContain("hits", r);
    }

    [Fact]
    public void switch_case与default标签不被剥()
    {
        // 回归：`default:` 的冒号曾被当变量类型注解剥掉（fixit search/index.ts 实测）
        var ts = "switch (type) {\n  case 'algolia':\n    return a()\n  default:\n    console.warn('x')\n}";
        var r = Strip(ts);
        Assert.Contains("case 'algolia':", r);
        Assert.Contains("default:", r);
        Assert.DoesNotContain("default ", r.Replace("default:", ""));
    }

    [Fact]
    public void 三元分支中的非空断言()
    {
        // 回归：`a ? b.dark! : b.light` 的 ! 后随三元冒号，未被删除
        var r = Strip("$meta.content = isDark ? $meta.dataset.dark! : $meta.dataset.light");
        Assert.Contains("isDark ? $meta.dataset.dark : $meta.dataset.light", r);
        Assert.DoesNotContain("!", r);
    }

    [Fact]
    public void export函数体内的as转换()
    {
        // 回归：`export function f() {` 的命名子句期待曾泄漏到函数体 {，
        // 抑制函数体内所有 as 转换（stack pagination.ts 实测）
        var ts = "export function setupPaginationJump() {\n" +
                 "  const triggers = document.querySelectorAll<HTMLButtonElement>('.x');\n" +
                 "  const dialog = document.getElementById('d') as HTMLDialogElement;\n}";
        var r = Strip(ts);
        Assert.DoesNotContain(" as ", r);
        Assert.DoesNotContain("HTMLDialogElement", r);
        Assert.Contains("querySelectorAll('.x')", r);
    }

    [Fact]
    public void 三元分支中的对象字面量键不被剥()
    {
        // 回归：`cond ? { v: x, i: y } : z` 的 { 曾被当代码块，对象键
        // v: / i: 被当类型注解剥掉（fuse.mjs deepGet 实测语法错误）
        var r = Strip("list.push(arrayIndex !== void 0 ? {\n  v: toString(value),\n  i: arrayIndex\n} : toString(value));");
        Assert.Contains("v: toString(value)", r);
        Assert.Contains("i: arrayIndex", r);
    }

    [Fact]
    public void 可选参数与函数类型()
    {
        // 回归：`callback?: () => void` 的 () 后跟 => 是函数类型，曾停吃
        // 留下 `=> void`（fixit animation.ts 实测）
        var ts = "export function animateCSS(element: Element, animation: string | string[], reserved?: boolean, callback?: () => void) {\n  return element\n}";
        var r = Strip(ts);
        Assert.Contains("animateCSS(element, animation, reserved, callback)", r);
        Assert.DoesNotContain("void", r);
    }

    [Fact]
    public void 正则字面量不干扰括号配平与类型剥离()
    {
        // 回归：正则里的引号曾被当字符串起点，MatchBracket 打到文件尾、
        // export 语句解析失败（fixit file.ts 的 /[\\/:*?"<>|\r\n]+/g 实测）
        var ts = "export function downloadAsFile(content: string, filename: string) {\n" +
                 "  link.download = filename.replace(/[\\\\/:*?\"<>|\\r\\n]+/g, '-')\n" +
                 "  const x: number = 1\n}";
        var r = Strip(ts);
        Assert.Contains("downloadAsFile(content, filename)", r);
        Assert.Contains("/[\\\\/:*?\"<>|\\r\\n]+/g", r); // 正则原样保留
        Assert.Contains("const x = 1", r);
    }

    [Fact]
    public void 满足stack主题main形态()
    {
        // stack 的 assets/ts/main.ts 实际片段
        var ts = "import menu from './menu';\nlet Stack = {\n  init: () => {\n" +
                 "    const articleContent = document.querySelector('.article-content') as HTMLElement;\n" +
                 "    if (articleContent) { setupSmoothAnchors(); }\n  }\n}";
        var r = Strip(ts);
        Assert.DoesNotContain(" as ", r);
        Assert.Contains("document.querySelector('.article-content')", r);
        Assert.Contains("let Stack = {", r);
    }
}
