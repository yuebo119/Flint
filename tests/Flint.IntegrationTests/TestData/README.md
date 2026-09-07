# TestData（测试数据）

本目录包含预定义的测试数据集，用于集成测试和端到端测试。

## 目录用途

测试数据目录用于存储：

- **站点模板**：预配置的测试站点结构
- **内容文件**：各种格式的 Markdown 内容
- **配置文件**：TOML、YAML、JSON 格式的配置
- **模板文件**：Scriban 模板文件
- **资源文件**：CSS、SCSS、JavaScript、图片等
- **边界条件数据**：用于测试边界情况的特殊数据

## 目录结构

```
TestData/
├── Sites/                    # 测试站点模板
│   ├── Minimal/              # 最小有效站点
│   │   ├── Flint.toml       # 基本配置
│   │   ├── content/          # 内容目录
│   │   │   └── index.md      # 首页内容
│   │   └── layouts/          # 布局目录
│   │       └── default.html  # 默认模板
│   │
│   ├── Complete/             # 包含所有功能的完整站点
│   │   ├── Flint.toml       # 完整配置
│   │   ├── content/          # 内容目录
│   │   │   ├── _index.md     # 首页
│   │   │   ├── about.md      # 关于页面
│   │   │   └── posts/        # 文章目录
│   │   │       ├── yaml-frontmatter.md    # YAML Front Matter
│   │   │       ├── toml-frontmatter.md    # TOML Front Matter
│   │   │       ├── json-frontmatter.md    # JSON Front Matter
│   │   │       ├── shortcodes-demo.md     # 短代码演示
│   │   │       ├── draft-post.md          # 草稿文章
│   │   │       └── future-post.md         # 未来文章
│   │   ├── layouts/          # 布局目录
│   │   │   ├── index.html    # 首页模板
│   │   │   ├── _default/     # 默认模板
│   │   │   │   ├── baseof.html
│   │   │   │   ├── single.html
│   │   │   │   ├── list.html
│   │   │   │   ├── taxonomy.html
│   │   │   │   └── term.html
│   │   │   └── partials/     # 部分模板
│   │   │       ├── head.html
│   │   │       ├── header.html
│   │   │       └── footer.html
│   │   └── assets/           # 资源目录
│   │       ├── scss/         # SCSS 文件
│   │       │   ├── main.scss
│   │       │   ├── _variables.scss
│   │       │   └── _mixins.scss
│   │       └── js/           # JavaScript 文件
│   │           └── main.js
│   │
│   └── Multilingual/         # 多语言站点
│       ├── Flint.toml       # 多语言配置
│       ├── content/          # 内容目录
│       │   ├── zh/           # 中文内容
│       │   │   ├── _index.md
│       │   │   ├── about.md
│       │   │   └── posts/
│       │   │       └── hello-world.md
│       │   ├── en/           # 英文内容
│       │   │   ├── _index.md
│       │   │   ├── about.md
│       │   │   └── posts/
│       │   │       └── hello-world.md
│       │   └── ja/           # 日文内容
│       │       ├── _index.md
│       │       ├── about.md
│       │       └── posts/
│       │           └── hello-world.md
│       └── layouts/          # 布局目录
│           ├── _default/
│           │   ├── baseof.html
│           │   ├── single.html
│           │   └── list.html
│           └── partials/
│               ├── head.html
│               ├── header.html
│               ├── footer.html
│               └── translations.html
│
└── EdgeCases/                # 边界条件测试数据
    ├── empty-content.md      # 空内容文件
    ├── empty-config.toml     # 空配置文件
    ├── unicode-content.md    # Unicode 特殊字符
    ├── emoji-content.md      # Emoji 表情
    ├── special-characters-filename-测试.md  # 特殊字符文件名
    └── deeply-nested-content.md  # 深层嵌套内容
```

## 测试站点模板

### Minimal（最小有效站点）

包含运行 Flint 所需的最少文件：
- `Flint.toml` - 基本配置（baseURL、title、languageCode）
- `content/index.md` - 首页内容
- `layouts/default.html` - 默认模板

**用途**：
- 测试基本构建流程
- 测试最小配置要求
- 快速冒烟测试

### Complete（完整站点）

包含所有 Flint 功能的测试站点：

**内容特性**：
- 多种 Front Matter 格式（YAML、TOML、JSON）
- 短代码使用示例
- 草稿和未来内容
- 分类和标签

**模板特性**：
- 完整的布局模板层次结构
- Partial 模板
- 分页支持
- 分类法模板

**资源特性**：
- SCSS 文件（包含变量、mixin、嵌套）
- JavaScript 文件
- @import 导入

**用途**：
- 测试完整构建流程
- 测试所有功能组合
- 性能测试基准

### Multilingual（多语言站点）

用于测试多语言功能：

**支持的语言**：
- 简体中文（zh）- 默认语言
- English（en）
- 日本語（ja）

**特性**：
- 独立的语言配置
- 翻译关联（translationKey）
- 语言切换器
- 本地化菜单

**用途**：
- 测试多语言内容管理
- 测试语言切换
- 测试翻译关联

## 边界条件测试数据

### empty-content.md
只有 Front Matter 没有正文的内容文件，用于测试空内容处理。

### empty-config.toml
空的配置文件（只有注释），用于测试配置加载器对空配置的处理。

### unicode-content.md
包含各种 Unicode 特殊字符的内容：
- 中日韩字符
- 数学符号、希腊字母
- 箭头、货币、音乐符号
- Emoji 表情
- 组合字符
- 零宽字符
- 双向文本（RTL）
- 特殊空白字符

### emoji-content.md
专门测试 Emoji 表情的内容：
- 标题中的 Emoji
- 表格中的 Emoji
- 代码块中的 Emoji
- 列表中的 Emoji
- 复杂 Emoji 序列（家庭、职业、旗帜）

### special-characters-filename-测试.md
文件名包含中文字符，用于测试特殊字符文件名的处理。

### deeply-nested-content.md
包含深层嵌套结构的内容：
- 10 层嵌套列表
- 5 层嵌套引用
- 复杂嵌套组合

## 使用方法

### 在测试中使用

```csharp
// 获取测试数据路径
var testDataPath = Path.Combine(
    AppContext.BaseDirectory,
    "TestData",
    "Sites",
    "Minimal");

// 创建测试站点夹具
var fixture = new TestSiteFixture();
await fixture.CreateFromTemplateAsync(testDataPath);
```

### 复制测试站点

```csharp
// 复制完整站点模板到临时目录
var sourcePath = Path.Combine(TestDataPath, "Sites", "Complete");
var targetPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}");
CopyDirectory(sourcePath, targetPath);
```

## 相关需求

- **Requirements 10.2**: 提供预配置的测试数据集

## 维护说明

1. 添加新的测试数据时，请更新此 README
2. 确保测试数据文件使用 UTF-8 编码
3. 测试数据应该是自包含的，不依赖外部资源
4. 边界条件数据应该覆盖各种极端情况
