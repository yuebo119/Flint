#!/usr/bin/env python3
# Go template → Scriban 转换器（Hugo 主题迁移工具）
# 机械形态自动覆盖（变量/控制流/partial/range/with/define），不可静态确定的输出
# TODO-HUGO 标记（绝不静默错译），跑完输出手工项清单
#
# 用法: python scripts/gotmpl2scriban.py <hugo-theme-dir> <flint-theme-dir>
import os, re, sys, shutil, json

TODO_LIST = []
# define 块闭合后待发出的调用（convert_template 逐表达式排空）：
# `{{ func main() }}...{{ end }}{{ main() }}` 需要 end 与调用分属两个 {{ }}
PENDING_CALLS = []
# 本文件 define 捕获的 block 名：convert_template 结束时统一生成
# `{{ include "baseof.html" blk_X: blk_X }}` 把子模板块传给 baseof。
# 这是 Scriban 无 block/extends 语句时的继承等价物（capture + 命名参数 include）
DEFINED_BLOCKS = []

# ctx_stack 中「不参与裸点映射」的帧类型：define 是函数标记，__block__ 是 if 的占位帧，
# __blockdef__ 是 baseof 的 block 声明（帧值是 block 名，不是上下文对象——若参与
# 裸点映射会把 `{{ .Params.Title }}` 误映射为 `title.params.title`）
NON_CTX_FRAMES = ("__define__", "__block__", "__blockdef__", "__skipblock__")

def dot_ctx(ctx_stack):
    """块内裸点（`.`）应映射到的变量：最近的 with/range 帧，否则 page"""
    return next((v for k, v in reversed(ctx_stack) if k not in NON_CTX_FRAMES), "page")

VAR_MAP = [
    # ========== 1) 站点全局（小写 site.* 与大写 .Site.* 并存，多段路径先行）==========
    (r"\bsite\.Params\.(\w+)", r"site.params.\1"),
    (r"\bsite\.Params\b", "site.params"),
    (r"\bsite\.Menus\.(\w+)", r"site.menus.\1"),
    (r"\bsite\.RegularPages\b", "site.regular_pages"),
    (r"\bsite\.Pages\b", "site.pages"),
    (r"\bsite\.Title\b", "site.title"),
    (r"\bsite\.Copyright\b", "site.copyright"),
    (r"\bsite\.Language(?:\.(?:Direction|Locale))?\b", "site.language"),
    (r"\bsite\.Data\b", "site.data"),
    (r"\bsite\.Config\b", "site.config"),
    (r"\bsite\.Home\b", "site"),
    # .Site.Home.* 无独立 Flint 键：首页 URL 用 site.base_url 近似
    (r"\.Site\.Home\.(?:RelPermalink|Permalink)", "site.base_url"),
    (r"\.Site\.Home\b", "site"),
    (r"\.Site\.Params\.(\w+)", r"site.params.\1"),
    (r"\.Site\.Menus\.(\w+)", r"site.menus.\1"),
    (r"\.Site\.Title", "site.title"),
    (r"\.Site\.LanguageCode", "site.language_code"),
    (r"\.Site\.BaseURL", "site.base_url"),
    (r"\.Site\.Language", "site.language"),
    (r"\.Site\.Copyright", "site.copyright"),
    (r"\.Site\.RegularPages", "site.regular_pages"),
    (r"\.Site\.Pages", "site.pages"),
    (r"\.Site\.Data\.([\w.]+)", r"site.data.\1"),
    (r"\.Site\b", "site"),

    # ========== 1.5) 分页器（须先于通用 `.Pages`/`.HasPrev` 等字段规则）==========
    # 映射到全局 paginator（Flint 渲染器逐页绑定：pager 渲染时 = 该页分页器，
    # 非分页渲染回落站点级旧值）——Hugo 的 .Paginator 即"当前列表页的分页器"，
    # 故 .Paginator.Pages 在 /page/2/ 上必须取第 2 页切片，不能用站点级 site.paginator
    # （后者恒为首页切片，会让分页多页内容重复）
    (r"\.Paginator\.Pages", "paginator.pages"),
    (r"\.Paginator\.TotalPages", "paginator.total_pages"),
    (r"\.Paginator\.PageNumber", "paginator.page_number"),
    (r"\.Paginator\.HasPrev", "paginator.has_prev"),
    (r"\.Paginator\.HasNext", "paginator.has_next"),

    # ========== 2) 接收者无关字段（$var.Field / 页面对象 / 裸点 均适用，驼峰→蛇形）==========
    # Hugo 短代码上下文里的 .Page（当前页面）→ 直接落到页面上下文
    (r"\.Page(?=\.)", ""),
    # 修复原 `.RelPermalink`→`page.rel_permalink` 对 `$var.RelPermalink` 的黏连 bug
    (r"\.RelPermalink\b", ".rel_permalink"),
    (r"\.Permalink\b", ".permalink"),
    (r"\.LinkTitle\b", ".title"),
    (r"\.WordCount\b", ".word_count"),
    (r"\.ReadingTime\b", ".reading_time"),
    (r"\.PublishDate\b", ".date"),
    (r"\.Lastmod\b", ".lastmod"),
    (r"\.Summary\b", ".summary"),
    (r"\.Content\b", ".content"),
    (r"\.Description\b", ".description"),
    (r"\.Categories\b", ".categories"),
    (r"\.Tags\b", ".tags"),
    (r"\.Title\b", ".title"),
    (r"\.Date\b", ".date"),
    (r"\.Section\b", ".section"),
    (r"\.Kind\b", ".type"),
    (r"\.Pages\b", ".pages"),
    (r"\.Params\b", ".params"),

    # ========== 3) 仅裸点（页面/站点上下文）适用的映射 ==========
    (r"\.RegularPages\b", "site.regular_pages"),
    (r"\.Data\.Pages", "page.pages"),
    (r"\.Data\.Terms", "page.terms"),
    (r"\.Data\.Singular", "page.title"),
    (r"\.Data\.Plural", "page.title"),
    (r"\.CurrentSection\.Title", "page.section"),
    (r"\.CurrentSection", "page.section"),
    (r"\.Parent\.Title", "page.section"),
    (r"\.Kind", "page.type"),
    (r"\.IsPage", "true"),
    (r"\.IsHome", "is_home"),
    (r"\.IsSection", "is_list"),
]

GO_DATE_TOKENS = [
    ("2006-01-02T15:04:05Z07:00", "yyyy-MM-ddTHH:mm:sszzz"),
    ("2006-01-02 15:04:05", "yyyy-MM-dd HH:mm:ss"),
    ("2006-01-02", "yyyy-MM-dd"),
    ("January 2, 2006", "MMMM d, yyyy"),
    ("Jan 2, 2006", "MMM d, yyyy"),
    ("January 2006", "MMMM yyyy"),
    ("2 January 2006", "d MMMM yyyy"),
    ("Mon Jan 2 15:04:05 2006", "ddd MMM d HH:mm:ss yyyy"),
    ("January", "MMMM"), ("Jan", "MMM"), ("Monday", "dddd"), ("Mon", "ddd"),
    ("15:04:05", "HH:mm:ss"), ("15:04", "HH:mm"),
    ("2006", "yyyy"), ("01", "MM"), ("02", "dd"),
    ("15", "HH"), ("04", "mm"), ("05", "ss"),
]

HUGO_NS_RE = re.compile(
    r"\b(compare|collections|transform|urls|hugo|resources|lang|fmt|inflect|crypto|data|reflect|strings|math|templates|os)\."
    r"|\.(Scratch|File|Resources|GetPage|Next|Prev|Related|OutputFormats)\b"
    r"|\b(warnf|printf|after\s|first\s|last\s|where\s|shuffle|uniq|dict\s|slice\s|\.Get\b|delimit\b)"
    # .Site.Config.Services.*：Flint 无 services 配置树，整块降级
    r"|\.Config\.Services\b")

# 可嵌入表达式的 Hugo 调用 → Flint/Scriban 等价（在裸点改写之后套用，
# 因此 partial 的 `.`/`page` 上下文参数此时已归一为 page）。
FUNC_SUBS = [
    # partial 调用（嵌在 if/属性等位置）：include "<name>"，上下文参数由当前渲染上下文提供
    (r'partials\.IncludeCached\s+"([\w/.-]+)"(?:\s+page)*', r'include "\1"'),
    (r'partials\.Include\s+"([\w/.-]+)"(?:\s+page)*', r'include "\1"'),
    # safe.* 是 Hugo 的方法式调用，Scriban 无 method 调用，改为过滤器函数
    (r"\bsafe\.HTMLAttr\b", "safe_html_attr"),
    (r"\bsafe\.HTML\b", "safe_html"),
    (r"\bsafe\.CSS\b", "safe_css"),
    (r"\bsafe\.JS\b", "safe_js"),
    (r"\bsafe\.URL\b", "safe_url"),
]

def convert_go_date_layout(layout):
    out = layout
    for go, net in GO_DATE_TOKENS:
        out = out.replace(go, net)
    return out

def convert_expr(expr, ctx_stack, file):
    expr = re.sub(r"^\-+\s*", "", expr).strip()
    expr = re.sub(r"\s*\-+$", "", expr).strip()

    # Hugo 注释必须最先拦截（含单行/多行）：多行注释内容可能含 `}}`
    # （如示例写法），若先走多行 TODO 分支会留下未消化的原始 `{{ < ... > }}` 文本
    if re.match(r"^/\*.*\*/$", expr, re.S):
        return ""

    # 多行表达式（Hugo 的括号换行风格）与 return 语句：无 Scriban 等价，
    # 一律 TODO——必须在任何函数处理器之前拦截（and/or 的空白切分会把多行拆坏）
    if "\n" in expr or re.match(r"^return\b", expr):
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr.replace(chr(10), ' ')[:60]}##"

    if expr == "end":
        if ctx_stack:
            top = ctx_stack.pop()
            if top[0] == "__define__":
                # 子模板块闭合：内容已由 capture 收进 blk_X，调用在文件末尾
                # 统一以 include "baseof.html" 发出（不再自调用 func）
                return "end"
            if top[0] == "__blockdef__":
                # baseof 的 block 声明闭合：用它捕获的默认内容兜底，
                # 子模板通过 include 传入 blk_X 时优先用子模板内容
                n = top[1]
                return ("end }}{{ if $.blk_" + n + " }}{{ $.blk_" + n
                        + " }}{{ else }}{{ __def_" + n + " }}{{ end")
            return "end"
        return "end"

    if expr == "else":
        # Hugo 的 with/range 在 else 分支恢复外层点上下文：把当前 with/range 帧
        # 换成不参与裸点映射的占位帧（保持 end 配对，同时让裸点回落到外层上下文）
        if ctx_stack and ctx_stack[-1][0] == "__with__":
            ctx_stack[-1] = ("__block__", None)
        return "else"
    if expr.startswith("else if"):
        if ctx_stack and ctx_stack[-1][0] == "__with__":
            ctx_stack[-1] = ("__block__", None)
        inner = re.sub(r"^else\s+if\s*", "", expr)
        converted = convert_expr(inner, ctx_stack, file)
        # 条件不可转换时降级为裸 else（保留块结构，条件语义裁剪）
        return "else" if "TODO-HUGO" in converted else "else if " + converted

    # if 也要压帧：否则块内首个 end 会误弹掉 with/range/define 帧
    # （导致裸点映射错位、define 提前闭合）。if 帧不参与裸点映射。
    if re.match(r"^if\b", expr):
        inner = re.sub(r"^if\s*", "", expr).strip()
        ctx_stack.append(("__block__", None))
        if not inner:
            return "if false"
        converted = convert_expr(inner, ctx_stack, file)
        if "TODO-HUGO" in converted:
            TODO_LIST.append((file, expr))
            return f"##TODO-HUGO: {expr}##"  # convert_template 降级为 if false，帧已压
        return "if " + converted

    if re.match(r"^/\*.*\*/$", expr, re.S):
        return ""

    m = re.match(r'^define\s+"([\w./-]+)"$', expr)
    if m:
        # 子模板块 → capture（Hugo define 的等价物）；Scriban 无 extends/block，
        # 文件末尾以 include "baseof.html" 命名参数把捕获内容传给 baseof。
        # 仅简单标识符名是 baseof 块；路径名（"_partials/x.html"）是 Hugo 内联
        # partial 定义，捕获后无法作为 partial 取用 → 结构保持式降级（if false）
        name = m.group(1)
        if re.match(r"^[\w-]+$", name):
            ctx_stack.append(("__define__", name))
            DEFINED_BLOCKS.append(name)
            return "capture blk_" + name
        ctx_stack.append(("__skipblock__", None))
        TODO_LIST.append((file, expr))
        # 只产出 TODO 标记：块开头的 TODO 由 convert_template 统一降级为
        # `... }}{{ if false` 保持 if/end 配对，此处再拼一次会产生双份 `}}`
        return f"##TODO-HUGO(内联partial定义): define \"{name}\"##"

    m = re.match(r'^block\s+"([\w-]+)"(?:\s+.+)?$', expr)
    if m:
        # baseof 的 block 声明（带默认内容）→ capture 默认内容 + 结束处条件输出。
        # 此前该形态未被识别而原样保留，产出字面 `{{ block "x" . }}` 文本，
        # 使子模板与 baseof 完全脱节（Ananke 迁移产物无 <!DOCTYPE html> 的根因）
        name = m.group(1)
        ctx_stack.append(("__blockdef__", name))
        return "capture __def_" + name

    m = re.match(r'^partials\.Include(Cached)?\s+"([\w/.-]+)"(?:\s+(.+))?$', expr, re.S)
    if m:
        cached, raw_name, arg = m.group(1), m.group(2), (m.group(3) or "").strip()
        name = re.sub(r"\.html$", "", raw_name)
        if cached:
            # IncludeCached "X" <cache scope> [ctx]：缓存域参数无 Flint 等价，丢弃
            arg = re.sub(r"^(?:\.|page)\s+", "", arg).strip()
        base = f'include "{name}"'
        if arg and arg not in (".", "page"):
            return f"##TODO-HUGO(partial-arg): include {name} {arg}##"
        return base

    m = re.match(r'^partial\s+"([\w/.-]+)"(?:\s+(.+))?$', expr, re.S)
    if m:
        name = re.sub(r"\.html$", "", m.group(1))
        arg = (m.group(2) or "").strip()
        base = f'include "{name}"'
        if arg and arg not in (".", "page"):
            return f"##TODO-HUGO(partial-arg): include {name} {arg}##"
        return base

    # .Render 是页面方法（Hugo 语义：dot/接收者决定渲染上下文，list 模板里
    # `{{ range .Paginator.Pages }}{{ .Render "summary" }}{{ end }}` 的 dot 是
    # 当前文章）。必须把当前上下文变量作为接收者传入——只发 render "x" 会让
    # 每个条目都用外层列表页渲染 summary（Ananke 实测：三张卡片全是 section 自身）
    m = re.match(r'^\.Render\s+"([^"]+)"$', expr)
    if m:
        return 'render "' + m.group(1) + '" ' + dot_ctx(ctx_stack)
    m = re.match(r'^\.Render\s+(\$\w+)$', expr)
    if m:
        return "render " + m.group(1) + " " + dot_ctx(ctx_stack)

    m = re.match(r'^\$\.Param\s+"([\w.-]+)"$', expr)
    if m:
        # 走 paramLookup：Hugo 参数名可含点名且缺失时返回 nil，
        # 直接写 page.params.a.b 会在中间层为 null 时报 member-of-null
        return f'paramLookup page "{m.group(1)}"'

    m = re.match(r'^lang\.Translate\s+"([\w.-]+)"', expr)
    if m:
        return f'i18n "{m.group(1)}"'

    m = re.match(r'^T\s+"([\w.-]+)"$', expr)
    if m:
        return f'i18n "{m.group(1)}"'

    m = re.match(r'^dateFormat\s+"([^"]+)"\s+(.+)$', expr, re.S)
    if m:
        date_expr = convert_expr(m.group(2), ctx_stack, file)
        net_fmt = convert_go_date_layout(m.group(1))
        return f'date.to_string {date_expr} "{net_fmt}"'

    m = re.match(r"^(.+?)\.Format\s+\"([^\"]+)\"$", expr, re.S)
    if m:
        date_expr = convert_expr(m.group(1), ctx_stack, file)
        net_fmt = convert_go_date_layout(m.group(2))
        return f"date.to_string {date_expr} \"{net_fmt}\""

    # and/or/not → Scriban 逻辑运算符（Hugo 前缀函数形态）
    m = re.match(r"^and\s+(.+)$", expr, re.S)
    if m:
        parts = [convert_expr(p, ctx_stack, file) for p in m.group(1).split()]
        return " && ".join(parts)
    m = re.match(r"^or\s+(.+)$", expr, re.S)
    if m:
        parts = [convert_expr(p, ctx_stack, file) for p in m.group(1).split()]
        return " || ".join(parts)
    m = re.match(r"^not\s+(.+)$", expr, re.S)
    if m:
        return "!(" + convert_expr(m.group(1), ctx_stack, file) + ")"

    for fn, op in (("eq", "=="), ("ne", "!="), ("gt", ">"), ("ge", ">="), ("lt", "<"), ("le", "<=")):
        m = re.match(rf"^{fn}\s+(.+?)\s+(.+)$", expr, re.S)
        if m:
            a = convert_expr(m.group(1), ctx_stack, file)
            b = convert_expr(m.group(2), ctx_stack, file)
            return f"{a} {op} {b}"

    m = re.match(r"^delimit\s+(.+?)\s+(.+)$", expr, re.S)
    if m:
        seq = convert_expr(m.group(1), ctx_stack, file)
        return f"array.join {seq} {m.group(2)}"

    m = re.match(r"^truncate\s+(\d+)\s+(.+)$", expr, re.S)
    if m:
        return f"string.truncate {convert_expr(m.group(2), ctx_stack, file)} {m.group(1)}"

    # 赋值先行：右值需递归转换（.Related/dict 等 Hugo 形态在递归内拦截），
    # 必须在变量映射与 text!=expr 提前返回之前处理，否则 := 会被跳过
    m = re.match(r"^\$(\w+)\s*:=\s*(.+)$", expr, re.S)
    if m:
        var, rhs = m.group(1), m.group(2)
        converted_rhs = convert_expr(rhs, ctx_stack, file)
        if "TODO-HUGO" in converted_rhs:
            TODO_LIST.append((file, expr))
            converted_rhs = '""'
        return f"${var} = {converted_rhs}"
    m = re.match(r"^\$(\w+)\s*=\s*(.+)$", expr, re.S)
    if m:
        var, rhs = m.group(1), m.group(2)
        converted_rhs = convert_expr(rhs, ctx_stack, file)
        if "TODO-HUGO" in converted_rhs:
            TODO_LIST.append((file, expr))
            converted_rhs = '""'
        return f"${var} = {converted_rhs}"

    # with / range 是块关键字：必须在 HUGO_NS 拦截之前处理。否则带 Hugo 函数的参数
    # （`range first 1 X`、`with .File`、`with .OutputFormats.Get "RSS"`）会被 HUGO_NS
    # 提前 TODO，再被 convert_template 降级成 `if false`，整块内容被隐藏。
    # 块内裸点的映射仍走 ctx_stack（__with__ / range 变量）。

    # with：Scriban 既不支持 `with $x = expr`（目标形态非法），也不支持 with/else。
    # 统一降为同一 {{ }} 内的「先赋值再 if」——else/end 在该形态下能正确配对。
    m = re.match(r"^with\s+(.+)$", expr, re.S)
    if m:
        var = f"$__w{len(ctx_stack)}"
        converted_arg = convert_expr(m.group(1), ctx_stack, file)
        if "TODO-HUGO" in converted_arg:
            TODO_LIST.append((file, expr))
            # 参数不可转换：降级为空串（falsy），块内容隐藏但 for/end 结构保持可解析。
            # 不用 page 作真值——部分模板在 with 块内对裸点做 safe.HTML 等类型敏感处理，
            # 传入页面对象会触发运行期类型错误。
            converted_arg = '""'
        ctx_stack.append(("__with__", var))
        return f"{var} = {converted_arg}; if {var}"

    # range：栈式上下文（集合表达式经 convert_expr 统一映射，长模式优先）
    if expr.startswith("range"):
        coll = re.sub(r"^range\s+", "", expr)
        # 双变量形态 `range $k, $v := obj`（Hugo map 迭代）：
        # Scriban 不支持 `for k, v in`（实测 PARSE-ERR），且迭代 ScriptObject 产出
        # {key, value} 对象（实测：x[0] 取不到，须用 x.key / x.value）。
        # 故转为单变量迭代 + 块首从 pair 解构出 k/v
        m2 = re.match(r"^\$(\w+)\s*,\s*\$(\w+)\s*:=\s*(.+)$", coll, re.S)
        if m2:
            kvar, vvar, src = "$" + m2.group(1), "$" + m2.group(2), m2.group(3)
            src = convert_expr(src, ctx_stack, file)
            if "TODO-HUGO" in src:
                TODO_LIST.append((file, expr))
                src = "{}"
            loopvar = f"_pair{len(ctx_stack)}"
            ctx_stack.append((vvar, vvar))
            # 注意 f-string 转义：要产出字面 `}}` 须写 `}}}}`，产出 `{{` 须写 `{{{{`
            return (f"for {loopvar} in {src} }}}}}}{{{{ {kvar} = {loopvar}.key; "
                    f"{vvar} = {loopvar}.value")
        m3 = re.match(r"^\$(\w+)\s*:=\s*(.+)$", coll, re.S)
        var_name = None
        if m3:
            var_name, coll = "$" + m3.group(1), m3.group(2)
        coll = convert_expr(coll, ctx_stack, file)
        if "TODO-HUGO" in coll:
            TODO_LIST.append((file, expr))
            coll = "[]"  # 集合不可转换：空序列降级，保住 for/end 配对（不产 TODO 文本进代码位）
        var_name = var_name or f"_it{len(ctx_stack)}"
        # 栈内第二元是「裸点替换值」：range 时裸点应指向循环变量本身，
        # 而非集合（集合仅在 for 头使用）
        ctx_stack.append((var_name, var_name))
        return f"for {var_name} in {coll}"

    # 前导裸点的 Hugo 方法调用（`.GetTerms "tags"` / `.Translations` / `.GetMatch ...`）：
    # Scriban 无对应方法，整体降级为 TODO。必须在「前导裸点字段改写」之前拦截——
    # 否则会留下 `page.GetTerms "tags"` 这类非法调用。
    if re.match(r"^\.[A-Za-z_][\w.]*\s+\S", expr) or re.match(
            r"^\.[A-Za-z_]*\.?(GetTerms|Translations|GetMatch|ByType|GetPage|OutputFormats"
            r"|Related|Resources|Store|Scratch|File)\b", expr):
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr}##"

    # Hugo 特有命名空间/函数：先于变量映射与提前返回判定（否则 .RegularPages.Related
    # 这类"部分可映射部分不可"的表达式会被变量映射的 text!=expr 提前放行）
    if HUGO_NS_RE.search(expr):
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr}##"

    text = expr
    for pat, rep in VAR_MAP:
        text = re.sub(pat, rep, text)

    ctx_var = dot_ctx(ctx_stack)
    if text == ".":
        # 跳过 __define__ 标记（define 块内裸点语义 = 页面上下文，非方法名）
        return ctx_var
    # 隔离裸点（`{{ . }}` → 上下文变量）与前导裸点字段（`.URL` → `<ctx>.URL`）：
    # Scriban 不支持前导 `.`；块内裸点指向当前 range/with 变量，顶层回落 page。
    if re.search(r"(?<![\w.$])\.(?![\w])", text):
        text = re.sub(r"(?<![\w.$])\.(?![\w])", ctx_var, text)
    if re.match(r"^\.[A-Za-z_]", text):
        text = re.sub(r"(?<![\w.$])\.(?=[A-Za-z_])", ctx_var + ".", text)

    # 可嵌入的 Hugo 调用（partial/safe.*）在裸点归一后统一替换
    for pat, rep in FUNC_SUBS:
        text = re.sub(pat, rep, text)

    if text != expr:
        return text

    if re.match(r"^\$\w+([\w.]*)?$", expr):
        return expr
    if expr.startswith("if $") or expr.startswith("with $"):
        return expr
    if re.match(r'^"[^"]*"$', expr) or re.match(r"^\d+$", expr) or expr in ("true", "false", "page", "site"):
        return expr

    m = re.match(r'^template\s+"([\w.-]+)"', expr)
    if m:
        # {{ template "x" . }} 是调用（不是声明）→ include；此前转成 `block "x"`
        # 在 Scriban 中无对应语义（block 非函数），属误译
        return 'include "' + m.group(1) + '"'

    if "\n" in expr:
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr}##"

    if re.match(r"^[a-zA-Z_][\w.]*\s+\S", expr) or re.match(r"^[a-zA-Z_][\w.]*\(", expr):
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr}##"

    return text

def convert_template(text, file):
    ctx_stack = []
    PENDING_CALLS.clear()  # 每文件独立：避免上一个文件未闭合 define 的残留调用泄漏
    DEFINED_BLOCKS.clear()
    out = []
    pos = 0
    # 注释 token 优先整段匹配（含跨行与两侧 trim 标记），避免注释内的 `}}`
    # （Hugo 短代码示例写法）把 token 提前截断、残留原始 `{{ ... }}` 文本
    for m in re.finditer(r"\{\{-?\s*/\*.*?\*/\s*-?\}\}|\{\{.*?\}\}", text, re.S):
        out.append(text[pos:m.start()])
        inner = m.group(0)[2:-2]
        converted = convert_expr(inner, ctx_stack, file)
        # define 块闭合排空的函数调用：end 与调用必须是两个独立 {{ }}，
        # 合在一条里会多出一个 `}}`（渲染成字面量）
        calls = list(PENDING_CALLS)
        PENDING_CALLS.clear()
        # 块关键字位置的 TODO：注释会破坏 if/with 与 end 的配对（end 悬空），
        # 降级为 "if false" 保持块结构可解析。同时保留 TODO-HUGO 标记 Tag，
        # 便于追溯原始 Hugo 语义（降级不等于静默错译）。
        if "TODO-HUGO" in converted:
            stripped = converted.strip()
            if stripped.startswith("##TODO-HUGO") and re.match(
                    r"^\s*(if|with|else\s+if|range|define|block)\b",
                    inner.strip().lstrip("-").strip()):
                converted = stripped + " }}{{ if false"
        if converted.strip():
            out.append("{{ " + converted + " }}")  # 空表达式（如 Go 注释）不产出 {{ }}
        for name in calls:
            out.append("{{ " + name + "() }}")
        pos = m.end()
    out.append(text[pos:])
    # 子模板块传给 baseof：Scriban 无 extends/block 语句，用 include 命名参数
    # 传递捕获的块。仅当本文件有 define 时生成（baseof 自身只有 block 声明）
    if DEFINED_BLOCKS:
        # Scriban 命名参数以空白分隔（无逗号）
        pairs = " ".join("blk_" + n + ": blk_" + n for n in DEFINED_BLOCKS)
        out.append('\n{{ include "baseof.html" ' + pairs + ' }}\n')
    return "".join(out)

def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    src, dst = sys.argv[1], sys.argv[2]
    if os.path.exists(dst):
        shutil.rmtree(dst, ignore_errors=True)
    os.makedirs(dst)

    stats = {"files": 0, "converted": 0, "todo_exprs": 0}
    todo_files = {}

    for root, dirs, files in os.walk(src):
        dirs[:] = [d for d in dirs if d != ".git"]
        rel_root = os.path.relpath(root, src)
        rel_root = re.sub(r"(^|/)_partials", r"\1partials", rel_root)
        for f in files:
            src_file = os.path.join(root, f)
            dst_file = os.path.join(dst, rel_root, f)
            os.makedirs(os.path.dirname(dst_file), exist_ok=True)
            stats["files"] += 1
            # robots.txt 也是模板（Hugo 将其作为模板渲染），必须参与转换而非原样复制
            if f.endswith((".html", ".xml", ".json", ".txt")):
                text = open(src_file, encoding="utf-8", errors="replace").read()
                converted = convert_template(text, rel_root)
                with open(dst_file, "w", encoding="utf-8", newline="") as fh:
                    fh.write(converted)
                n_todo = converted.count("TODO-HUGO")
                if n_todo:
                    todo_files[os.path.join(rel_root, f)] = n_todo
                    stats["todo_exprs"] += n_todo
                stats["converted"] += 1
            else:
                shutil.copy2(src_file, dst_file)

    print(json.dumps(stats, ensure_ascii=False))
    if todo_files:
        print("--- 含 TODO 的文件（Top 15）---")
        for f, n in sorted(todo_files.items(), key=lambda kv: -kv[1])[:15]:
            print(f"  {n:3d}  {f}")

if __name__ == "__main__":
    main()
