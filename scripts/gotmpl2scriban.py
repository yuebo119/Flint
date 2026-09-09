#!/usr/bin/env python3
# Go template → Scriban 转换器（Hugo 主题迁移工具）
# 机械形态自动覆盖（变量/控制流/partial/range/with/define），不可静态确定的输出
# TODO-HUGO 标记（绝不静默错译），跑完输出手工项清单
#
# 用法: python scripts/gotmpl2scriban.py <hugo-theme-dir> <flint-theme-dir>
import os, re, sys, shutil, json

TODO_LIST = []

VAR_MAP = [
    (r"\.Site\.Params\.(\w+)", r"site.params.\1"),
    (r"\.Site\.Menus\.(\w+)", r"site.menus.\1"),
    (r"\.Site\.Title", "site.title"),
    (r"\.Site\.LanguageCode", "site.language_code"),
    (r"\.Site\.BaseURL", "site.base_url"),
    (r"\.Site\.Language", "site.language"),
    (r"\.Site\.Copyright", "site.copyright"),
    (r"\.Site\.RegularPages", "site.regular_pages"),
    (r"\.RegularPages", "site.regular_pages"),
    (r"\.Site\.Pages", "site.pages"),
    (r"\.Pages", "site.pages"),
    (r"\.Site\.Data\.([\w.]+)", r"site.data.\1"),
    (r"\.Site\b", "site"),
    (r"\.Params\.(\w+)", r"page.params.\1"),
    (r"\.RelPermalink", "page.rel_permalink"),
    (r"\.Permalink", "page.permalink"),
    (r"\.Content", "page.content"),
    (r"\.Summary", "page.summary"),
    (r"\.Description", "page.description"),
    (r"\.Title", "page.title"),
    (r"\.WordCount", "page.word_count"),
    (r"\.ReadingTime", "page.reading_time"),
    (r"\.Section", "page.section"),
    (r"\.PublishDate", "page.date"),
    (r"\.Lastmod", "page.lastmod"),
    (r"\.Date", "page.date"),
    (r"\.Tags", "page.tags"),
    (r"\.Categories", "page.categories"),
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
    r"\b(compare|collections|transform|urls|hugo|resources|lang|fmt|inflect|crypto|data)\."
    r"|\.(Scratch|File|Resources|Paginator|GetPage|Next|Prev|Render)\b"
    r"|\b(template|warnf|printf|default|after|first|last|where|shuffle|uniq|Get)\b")

def convert_go_date_layout(layout):
    out = layout
    for go, net in GO_DATE_TOKENS:
        out = out.replace(go, net)
    return out

def convert_expr(expr, ctx_stack, file):
    expr = re.sub(r"^\-+\s*", "", expr).strip()
    expr = re.sub(r"\s*\-+$", "", expr).strip()

    if expr == "end":
        if ctx_stack:
            top = ctx_stack.pop()
            if top[0] == "__define__":
                # define 块闭合后：立即调用该 func（base 中 block 原位渲染）
                return "end }}{{ " + top[1] + "() }}"
            return "end"
        return "end"

    if expr == "else":
        return "else"
    if expr.startswith("else if"):
        inner = re.sub(r"^else\s+if\s*", "", expr)
        return "else if " + convert_expr(inner, ctx_stack, file)

    if re.match(r"^/\*.*\*/$", expr, re.S):
        return ""

    m = re.match(r'^define\s+"([\w.-]+)"$', expr)
    if m:
        # Scriban 继承 = extends 后重定义同名 func（无 block 语句）
        ctx_stack.append(("__define__", m.group(1)))
        return f"func {m.group(1)}()"

    m = re.match(r'^partials\.Include\s+"([\w/.-]+)"(?:\s+(.+))?$', expr, re.S)
    if m:
        name = re.sub(r"\.html$", "", m.group(1))
        arg = (m.group(2) or "").strip()
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

    m = re.match(r'^\$\.Param\s+"([\w-]+)"$', expr)
    if m:
        return f"page.params.{m.group(1).replace('-', '_')}"

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

    text = expr
    for pat, rep in VAR_MAP:
        text = re.sub(pat, rep, text)

    # range：栈式上下文（先于变量映射，避免 .Pages 等集合映射干扰）
    if expr.startswith("range"):
        coll = re.sub(r"^range\s+", "", expr)
        m3 = re.match(r"^\$(\w+)\s*:=\s*(.+)$", coll, re.S)
        var_name = None
        if m3:
            var_name, coll = "$" + m3.group(1), m3.group(2)
        coll = re.sub(r"^\.\s*", "", coll)  # range 的集合无需 page 前缀
        if coll in (".Pages", "Pages"):
            coll = "site.pages"
        coll = convert_expr(coll, ctx_stack, file)
        var_name = var_name or f"_it{len(ctx_stack)}"
        ctx_stack.append((var_name, coll))
        return f"for {var_name} in {coll}"

    if text == ".":
        return ctx_stack[-1][0] if ctx_stack else "page"
    if re.search(r"(?<![\w.$])\.(?![\w])", text):
        repl = next((v for k, v in reversed(ctx_stack) if k != "__define__"), "page")
        text = re.sub(r"(?<![\w.$])\.(?![\w])", repl, text)

    if text != expr:
        return text

    # 赋值与 $ 变量：Scriban 同形态保留（:= 转 =）
    if re.match(r"^\$\w+\s*:=\s*.+$", expr, re.S):
        return expr.replace(":=", "=", 1)
    if re.match(r"^\$\w+\s*=\s*.+$", expr, re.S):
        return expr
    if re.match(r"^\$\w+([\w.]*)?$", expr):
        return expr
    if expr.startswith("if $") or expr.startswith("with $"):
        return expr
    if re.match(r'^"[^"]*"$', expr) or re.match(r"^\d+$", expr) or expr in ("true", "false", "page", "site"):
        return expr

    m = re.match(r'^template\s+"([\w.-]+)"', expr)
    if m:
        return f'block "{m.group(1)}"'

    if HUGO_NS_RE.search(expr):
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr}##"

    if "\n" in expr:
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr}##"

    if re.match(r"^[a-zA-Z_][\w.]*\s+\S", expr) or re.match(r"^[a-zA-Z_][\w.]*\(", expr):
        TODO_LIST.append((file, expr))
        return f"##TODO-HUGO: {expr}##"

    return text

def convert_template(text, file):
    ctx_stack = []
    out = []
    pos = 0
    for m in re.finditer(r"\{\{.*?\}\}", text, re.S):
        out.append(text[pos:m.start()])
        inner = m.group(0)[2:-2]
        converted = convert_expr(inner, ctx_stack, file)
        if converted.strip():
            out.append("{{ " + converted + " }}")  # 空表达式（如 Go 注释）不产出 {{ }}
        pos = m.end()
    out.append(text[pos:])
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
            if f.endswith((".html", ".xml", ".json")):
                text = open(src_file, encoding="utf-8", errors="replace").read()
                converted = convert_template(text, rel_root)
                # Hugo 子模板隐式 extends baseof；Scriban 需显式声明
                baseof = os.path.join(src, "layouts", "baseof.html")
                has_block = "block " in converted
                if has_block and "{{ extends" not in converted and os.path.exists(baseof):
                    converted = '{{ extends "baseof.html" }}\n' + converted
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
