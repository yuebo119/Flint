# -*- coding: utf-8 -*-
"""生成语料库占位插图（12 张 SVG，仅生成新文件）"""
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "static", "images")
os.makedirs(OUT, exist_ok=True)

# (主色, 辅色, 标签, 图形类型)
SPECS = [
    ("#2563eb", "#93c5fd", "架构示意图", "boxes"),
    ("#059669", "#6ee7b7", "流程图", "flow"),
    ("#d97706", "#fcd34d", "数据表", "table"),
    ("#dc2626", "#fca5a5", "告警面板", "gauge"),
    ("#7c3aed", "#c4b5fd", "拓扑图", "net"),
    ("#0891b2", "#a5f3fc", "时间线", "time"),
    ("#db2777", "#f9a8d4", "增长曲线", "chart"),
    ("#65a30d", "#d9f99d", "对比柱状", "bars"),
    ("#475569", "#cbd5e1", "代码片段", "code"),
    ("#e11d48", "#fda4af", "状态灯", "dots"),
    ("#0d9488", "#99f6e4", "分层结构", "layers"),
    ("#4f46e5", "#a5b4fc", "概念模型", "circle"),
]

HEAD = ('<svg xmlns="http://www.w3.org/2000/svg" width="800" height="400" '
        'viewBox="0 0 800 400" font-family="sans-serif">')
FOOT = "</svg>"


def shapes(kind, main, sub):
    if kind == "boxes":
        return (f'<rect x="80" y="90" width="180" height="80" rx="8" fill="{sub}"/>'
                f'<rect x="310" y="90" width="180" height="80" rx="8" fill="{sub}"/>'
                f'<rect x="540" y="90" width="180" height="80" rx="8" fill="{sub}"/>'
                f'<rect x="195" y="230" width="180" height="80" rx="8" fill="{main}"/>'
                f'<rect x="425" y="230" width="180" height="80" rx="8" fill="{main}"/>'
                f'<path d="M170 170 L285 230 M400 170 L400 230 M630 170 L515 230" '
                f'stroke="{main}" stroke-width="3" fill="none"/>')
    if kind == "flow":
        out = []
        for i in range(4):
            x = 90 + i * 180
            out.append(f'<rect x="{x}" y="150" width="120" height="70" rx="35" fill="{sub}"/>')
            if i < 3:
                out.append(f'<path d="M{x + 120} 185 L{x + 180} 185" stroke="{main}" '
                           f'stroke-width="3" marker-end="url(#a)"/>')
        return "".join(out) + (f'<defs><marker id="a" markerWidth="10" markerHeight="10" '
                               f'refX="8" refY="3" orient="auto"><path d="M0 0 L8 3 L0 6 z" '
                               f'fill="{main}"/></marker></defs>')
    if kind == "table":
        rows = []
        for r in range(4):
            y = 110 + r * 55
            rows.append(f'<rect x="150" y="{y}" width="500" height="45" rx="4" '
                        f'fill="{sub if r % 2 else main}" opacity="{0.9 if r % 2 else 0.75}"/>')
            for c in range(3):
                rows.append(f'<rect x="{170 + c * 160}" y="{y + 12}" width="120" height="20" '
                            f'rx="3" fill="#ffffff" opacity="0.85"/>')
        return "".join(rows)
    if kind == "gauge":
        return (f'<path d="M 200 280 A 200 200 0 0 1 600 280" fill="none" stroke="{sub}" '
                f'stroke-width="34" stroke-linecap="round"/>'
                f'<path d="M 200 280 A 200 200 0 0 1 470 130" fill="none" stroke="{main}" '
                f'stroke-width="34" stroke-linecap="round"/>'
                f'<circle cx="400" cy="280" r="14" fill="{main}"/>')
    if kind == "net":
        pts = [(400, 90), (200, 200), (600, 200), (280, 320), (520, 320)]
        edges = [(0, 1), (0, 2), (1, 3), (2, 4), (1, 2), (3, 4)]
        out = [f'<path d="M{pts[a][0]} {pts[a][1]} L{pts[b][0]} {pts[b][1]}" stroke="{sub}" '
               f'stroke-width="3"/>' for a, b in edges]
        out += [f'<circle cx="{x}" cy="{y}" r="26" fill="{main}"/>' for x, y in pts]
        return "".join(out)
    if kind == "time":
        out = [f'<path d="M 100 200 L 700 200" stroke="{main}" stroke-width="4"/>']
        for i in range(5):
            x = 130 + i * 135
            out.append(f'<circle cx="{x}" cy="200" r="16" fill="{sub}" stroke="{main}" '
                       f'stroke-width="4"/>')
        return "".join(out)
    if kind == "chart":
        return (f'<path d="M 100 330 C 250 320 350 260 450 190 S 620 90 700 70" fill="none" '
                f'stroke="{main}" stroke-width="5"/>'
                f'<path d="M 100 330 L 700 330 M 100 330 L 100 60" stroke="{sub}" '
                f'stroke-width="3"/>')
    if kind == "bars":
        out = [f'<path d="M 100 330 L 700 330" stroke="{main}" stroke-width="3"/>']
        for i, h in enumerate([60, 130, 200, 150, 240, 110]):
            x = 130 + i * 95
            out.append(f'<rect x="{x}" y="{330 - h}" width="56" height="{h}" rx="4" '
                       f'fill="{sub if i % 2 else main}"/>')
        return "".join(out)
    if kind == "code":
        out = []
        for i, w in enumerate([280, 340, 220, 380, 260]):
            out.append(f'<rect x="160" y="{90 + i * 45}" width="{w}" height="18" rx="4" '
                       f'fill="{sub if i % 2 else main}" opacity="0.8"/>')
        return "".join(out)
    if kind == "dots":
        out = []
        for i, (cx, on) in enumerate([(200, 1), (320, 0), (440, 1), (560, 0), (660, 1)]):
            out.append(f'<circle cx="{cx}" cy="200" r="34" '
                       f'fill="{main if on else sub}"/>')
        return "".join(out)
    if kind == "layers":
        out = []
        for i in range(4):
            y = 90 + i * 60
            out.append(f'<rect x="{180 + i * 30}" y="{y}" width="{440 - i * 60}" height="46" '
                       f'rx="8" fill="{main if i == 0 else sub}" opacity="{1 - i * 0.15}"/>')
        return "".join(out)
    # circle
    return (f'<circle cx="400" cy="200" r="120" fill="none" stroke="{main}" stroke-width="6"/>'
            f'<circle cx="400" cy="200" r="70" fill="{sub}"/>'
            f'<circle cx="400" cy="200" r="24" fill="{main}"/>')


for idx, (main, sub, label, kind) in enumerate(SPECS, start=1):
    svg = (HEAD + f'<rect width="800" height="400" fill="#f8fafc"/>' +
           shapes(kind, main, sub) +
           f'<text x="400" y="370" text-anchor="middle" font-size="26" fill="#334155" '
           f'letter-spacing="4">{label}</text>' + FOOT)
    with open(os.path.join(OUT, f"demo-{idx}.svg"), "w", encoding="utf-8") as f:
        f.write(svg)
    print(f"demo-{idx}.svg  {label}")
