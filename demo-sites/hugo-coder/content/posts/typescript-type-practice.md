---
title: TypeScript 类型系统实战：从泛型到条件类型的正确用法
date: 2024-10-08
tags: [TypeScript, 类型系统, 前端工程]
categories: [前端, TypeScript]
description: 一份面向日常开发的 TypeScript 类型实战指南：泛型约束、条件类型与映射类型怎么组合使用，以及类型与运行时校验如何配合。
---

TypeScript 的类型系统经常被两个极端对待：要么 `any` 走天下，类型注解纯属摆设；要么沉迷类型体操，写出行数比实现还长的类型。这篇讲中间路线：用类型把接口契约钉死，把 bug 挡在编译期，同时不让类型成为维护负担。所有例子都可以直接跑，不需要黑魔法。

![类型检查把错误挡在编译期](/images/demo-4.svg)

## ## 类型是文档，也是测试

### 收窄的边界：unknown 不是万能挡箭牌

`unknown` 接收一切输入，但使用前必须收窄。收窄手段要成对出现：类型守卫（`typeof`、`in`、自定义 `is` 谓词）负责编译期，运行时校验（parse 函数）负责真实数据。只写守卫不做校验，等于把信任建立在"后端一定守约"上，而接口联调阶段最不缺的就是对方违约。

一个好类型的价值不在"写了类型"，而在"消灭了一类错误"。比如把函数的入参从 `object` 换成精确的结构类型，调用方少传一个字段，编辑器当场标红，这比运行到一半才发现 `undefined is not a function` 便宜得多。日常开发里最值得投资的三个习惯：

- 打开 `strict` 系列开关，尤其 `strictNullChecks`，它消灭的 null 错误比任何 lint 规则都多；
- 用 `unknown` 代替 `any` 接收外部数据，用之前先收窄；
- 公共函数的返回值必须显式标注，防止实现改动悄悄改变契约。

## 泛型：别急着写 any

遇到"类型不确定"的场景，第一反应应该是泛型，而不是 `any`。泛型是"类型层面的函数"：把类型当参数传进去，返回时保持关系不变。

```typescript
function first<T>(items: T[]): T | undefined {
  return items[0];
}

const name = first(["a", "b"]); // string | undefined
const port = first([80, 443]); // number | undefined
```

如果实现里只能处理有限几种类型，就用约束把边界画出来。约束写在 `extends` 后面，被约束的类型参数自动获得被约束类型的全部成员：

```typescript
interface HasId {
  id: string;
}

function byId<T extends HasId>(items: T[], id: string): T | undefined {
  return items.find((item) => item.id === id);
}
```

## 联合类型与收窄

联合类型是 TypeScript 表达"或"的方式，配合同名可辨识字段（discriminant）尤其好用：

```typescript
type Result =
  | { status: "loading" }
  | { status: "success"; data: string[] }
  | { status: "error"; message: string };

function render(result: Result): string {
  switch (result.status) {
    case "loading":
      return "加载中";
    case "success":
      return `共 ${result.data.length} 条`;
    case "error":
      return `出错：${result.message}`;
  }
}
```

注意 switch 覆盖了全部三个分支，编译器能检查穷尽性；将来给联合类型加第四个成员，忘了处理的地方会直接报错。这就是把类型当测试用的典型场景。

## 条件类型与 infer

条件类型的语法是 `T extends U ? X : Y`，可以理解为类型层面的三元表达式。配合 `infer` 关键字，还能在条件分支里"捕获"类型片段，实现诸如"取函数返回值类型"这类操作：

```typescript
type Unwrap<T> = T extends Promise<infer U> ? U : T;

type A = Unwrap<Promise<string>>; // string
type B = Unwrap<number>; // number

type ElementOf<T> = T extends Array<infer E> ? E : never;
type Item = ElementOf<string[]>; // string
```

内置工具类型大量使用这个技巧，`ReturnType`、`Parameters`、`Awaited` 本质上都是条件类型的封装。自己写条件类型的时机很明确：同一个类型在不同入参下结构不同，且这个差异需要被调用方感知。

## 映射类型：批量改造已有类型

映射类型用 `[K in keyof T]` 遍历一个类型的键，生成结构相同、值类型被改造过的新类型。最常见的用途是批量加可选、加只读、改键名：

```typescript
type PartialBy<T, K extends keyof T> = Partial<Pick<T, K>> & Omit<T, K>;

interface Article {
  id: string;
  title: string;
  summary: string;
}

type Draft = PartialBy<Article, "summary">;
// { id: string; title: string; summary?: string }
```

模板字面量类型（`` `${string}Id` ``）再进一步，可以表达"以 Id 结尾的键"这类模式，做事件名、字段名的批量约束时非常省事。

## 实战：一个类型安全的请求封装

把前面说的组合起来，写一个最小可用的请求封装。关键点是：响应类型由调用方指定，但运行时数据必须先校验再交付，类型不能当作运行时保证。

```typescript
type HttpMethod = "GET" | "POST" | "PUT" | "DELETE";

interface RequestOptions<T> {
  method?: HttpMethod;
  body?: unknown;
  parse: (raw: unknown) => T;
}

async function request<T>(url: string, options: RequestOptions<T>): Promise<T> {
  const response = await fetch(url, {
    method: options.method ?? "GET",
    body: options.body ? JSON.stringify(options.body) : undefined,
    headers: { "content-type": "application/json" },
  });
  if (!response.ok) {
    throw new Error(`请求失败：${response.status}`);
  }
  const raw: unknown = await response.json();
  return options.parse(raw); // parse 负责运行时校验，返回 T
}
```

调用方传入 `parse` 函数，编译期拿到精确类型，运行期拿到真实校验。类型断言（`as`）被限制在 parse 函数内部一个小角落里，不会污染业务代码。

## 常见反模式

- 用 `as` 绕过一切不匹配：断言是"我比编译器懂"的声明，用多了等于把 strict 关掉。
- 给所有返回值标注 `any`：等于放弃检查，还误导调用方。
- 类型嵌套三层以上还在硬撑：该拆接口了，或者用映射类型生成。
- 非空断言 `!` 满天飞：`a!.b!.c` 每一处都可能在未来某次重构后变成运行时炸弹。

## satisfies：要推断，也要约束

`as const` 之后最常用的新操作符是 `satisfies`。它回答一个具体问题：既要让 TypeScript 推断字面量类型，又要检查值符合某个接口。

```typescript
interface Route {
  path: string;
  method: "GET" | "POST";
}

const routes = [
  { path: "/users", method: "GET" },
  { path: "/users", method: "POST" },
] satisfies Route[];

// routes[0].method 的类型是 "GET" 而不是 string
```

对比一下：用 `as Route[]` 标注会丢掉字面量类型；不标注又少了约束检查。`satisfies` 两个都要，配置对象、路由表、主题变量这类场景特别好用。

## 给没有类型的库补类型

老库不带类型时，自己写声明文件比在业务代码里堆 `@ts-ignore` 干净得多。最小可用的做法是在项目里建一个 `types/` 目录，放对应模块的声明：

```typescript
// types/legacy-widget.d.ts
declare module "legacy-widget" {
  export interface WidgetOptions {
    container: HTMLElement;
    onSelect?: (id: string) => void;
  }
  export function create(options: WidgetOptions): { destroy(): void };
}
```

原则是"够用就好"：只声明项目实际用到的 API，不必穷尽整个库。用得多、质量要求高的场景，再考虑写完整声明并提给上游。

## 类型也要测试

类型是契约，契约可以用测试锁住。`tsd` 这类工具让你写下"这个表达式必须是这个类型"的断言，类型意外变化时测试失败：

```typescript
import { expectType } from "tsd";
import { byId } from "./byId";

expectType<{ id: string } | undefined>(byId([{ id: "a" }], "a"));
```

对公共库和团队内部的基础类型工具，这比人工 review 类型定义可靠。业务代码不必强求，但每次"重构类型后悄悄改变了调用方"的事故，都是类型测试的候选场景。

## 从 JavaScript 渐进迁移

存量项目不必一次性开启 strict。推荐顺序：先加 `allowJs` 让 TypeScript 编译 JavaScript，只给改动过的文件加 `.ts` 后缀；再逐个打开 strict 子项，`noImplicitAny` 优先，`strictNullChecks` 最后但必须开；同时把 `@ts-ignore` 换成 `@ts-expect-error`，后者在"预期的错误消失"时会报错，防止 ignore 注释烂掉。全程守住一条：新代码必须类型完整，老代码按接触面逐步收编。

展开成可执行的步骤：

1. 打开 `allowJs`，允许 TypeScript 与 JavaScript 混合编译
   - 只给改动过的文件加 `.ts` 后缀，不碰的文件维持原状
   - 想一次性全改的团队，按目录灰度推进，不要一个 PR 改全仓库
2. 逐个打开 strict 子项
   - `noImplicitAny` 优先，它拦的是最直接的错误
   - `strictNullChecks` 最后开，收益最大、报错也最多，留足工时
3. 清理抑制注释
   - 把 `@ts-ignore` 换成 `@ts-expect-error`，让过时的抑制自己报错
   - 每个抑制注释必须写明原因，写不出原因的就动手修

## 工具类型速查

| 类型 | 作用 | 典型用途 |
| --- | --- | --- |
| Partial<T> | 全部属性可选 | 补丁、草稿 |
| Pick<T, K> | 挑选属性 | 视图模型 |
| Omit<T, K> | 排除属性 | 裁剪配置 |
| Record<K, V> | 键值映射 | 字典 |
| ReturnType<F> | 取返回值类型 | 工厂产物 |
| Exclude<U, M> | 从联合类型排除 | 过滤字面量 |
| NonNullable<T> | 去掉 null 与 undefined | 收窄之后 |

这些内置工具覆盖了日常九成的类型改造需求，写自定义工具类型之前，先查一遍这张表。

## 泛型的两个实用技巧

第一个是默认类型参数。泛型可以带默认值，调用方不显式指定时自动生效，比没有默认值的版本更好用：

```typescript
interface ApiResponse<T = unknown> {
  code: number;
  data: T;
}

async function get<T = unknown>(url: string): Promise<ApiResponse<T>> {
  const response = await fetch(url);
  return response.json();
}

const user = await get<User>("/api/user"); // data 的类型是 User
const raw = await get("/api/raw"); // data 的类型是 unknown
```

第二个是函数重载与条件类型的取舍。同一个函数在不同入参下返回不同类型，重载写起来直观，但入参组合稍多就指数爆炸；泛型加条件类型更紧凑，可读性差一些。经验法则：两种形态用重载，三种以上用泛型加条件类型，超过五种就该拆成多个函数。

## tsconfig：值得打开的开关

类型系统的强度一半靠写法，一半靠配置。这几个开关值得默认打开：

- `noUncheckedIndexedAccess`：数组和对象索引访问返回 `T | undefined`，逼你在取值前处理空值，是修复"偶发 undefined"的利器。
- `exactOptionalPropertyTypes`：区分"属性不存在"和"属性值为 undefined"，接口描述更精确。
- `noImplicitOverride`：重写父类方法必须写 `override`，重构时不容易漏改。
- `verbatimModuleSyntax`：强制 import type 与 import 分开，避免类型导入被意外打包。

打开它们的成本是初期报错变多，收益是这些报错一次性还清旧账，之后新代码天然干净。

## 类型命名也是文档

最后说个软性但重要的点：类型名是给人和编译器共同阅读的文档。`type T = { a: string; b: number }` 和 `type User = { name: string; age: number }` 编译期等价，维护期天壤之别。可执行的约定：领域概念用具象名词（User、Order），工具类型用变换描述（PartialBy、DeepReadonly），泛型参数用有语义的命名（TItem 比裸 T 更能表达"这是条目类型"）。类型写得越像自然语言，后来人（包括三个月后的你）读接口的成本就越低。

> 类型的目的是消除一整类错误，不是让代码看起来高级。任何让阅读成本显著上升的类型写法，都要重新评估值不值得。

## 小结

类型系统的熟练度不体现在会写多复杂的类型体操，而体现在能用简单工具解决真实问题：泛型保持关系，联合类型表达或，条件类型与映射类型做批量改造，unknown 加收窄守住外部输入。类型写清楚的那天，重构和交接的成本都会 quietly 下降。更多背景可参考 <https://zh.wikipedia.org/wiki/TypeScript>，示例代码托管在 <https://example.com/typescript-type-practice>。
