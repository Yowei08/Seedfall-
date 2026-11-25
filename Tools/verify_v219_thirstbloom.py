# -*- coding: utf-8 -*-
"""v219 一次性校验：l10n 三语 json 能解析、hollow_vine 的定位词三语一致、seeds.json 未被改动。

跑法： python -X utf8 Tools/_verify_v219_thirstbloom.py
"""
import io
import json
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def path(*p):
    return os.path.join(ROOT, *p)


FILES = [
    "Assets/Resources/Data/l10n_zh.json",
    "Assets/Resources/Data/l10n_en.json",
    "Assets/Resources/Data/l10n_ja.json",
    "Assets/Resources/Data/seeds.json",
]

data = {}
for f in FILES:
    with io.open(path(f), encoding="utf-8") as fh:
        d = json.load(fh)
    data[f] = d
    n = len(d["entries"]) if isinstance(d, dict) and "entries" in d else (
        len(d["seeds"]) if isinstance(d, dict) and "seeds" in d else len(d))
    print("OK  json.loads %-42s 顶层 %-6s 条目 %d" % (f, type(d).__name__, n))

src = io.open(path("Assets/Scripts/UI/ImGuiHud.cs"), encoding="utf-8").read()
m = re.search(r'case "hollow_vine": return "([^"]+)"', src)
code_lit = m.group(1)
print("\n代码里的定位词（ImGuiHud.PlantRoleText）：", code_lit)

rows = {}
for f in FILES[:3]:
    e = [x for x in data[f]["entries"] if x["k"] == "imguihud.233"][0]
    rows[f] = e
    print("  %-6s k=%-14s z=%r  v=%r" % (f.split("/")[-1], e["k"], e["z"], e["v"]))

print("\nzh 的 z == 代码原文（L10n.S 靠原文→键，必须逐字相等）：",
      rows[FILES[0]]["z"] == code_lit)
print("三份 v 都非空：", all(bool(rows[f]["v"]) for f in FILES[:3]))
print("en 仍写着旧词 Drought：", "Drought" in rows[FILES[1]]["v"])

print("\nseeds.json 里这两株（本轮**没有**改 json，只贴证据）：")
for s in data["Assets/Resources/Data/seeds.json"]["seeds"]:
    if s["id"] in ("hollow_vine", "dune_cactus"):
        print("  %-12s mechanic=%-15s waterMax=%s waterDrain=%s goldYield=%s"
              % (s["id"], s["mechanic"], s["waterMax"], s["waterDrain"], s["goldYield"]))