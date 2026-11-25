# -*- coding: utf-8 -*-
"""只做一件事：把 items.json 里 gamble_glove 的新 desc 同步进三份 l10n 表的 `desc.gamble_glove`。

为什么要同步：`L10n.Shape()` 把屏幕上的中文原文归一（数字 -> §）后在 `ShapeToKey` 里查键，
而 `ShapeToKey` 是由 **l10n_zh.json 的 `z` 值**建的（`L10n.BuildShapeIndex`）。
`z` 若还是旧文案，形状就查不到 => en/ja 那两条译文**永远不会被命中**（回退成中文）。
所以必须把 `z` 换成**新 desc 的归一形状**，en/ja 的 `v` 换成 § 个数一致的新译文。
"""
import io
import json
import re
import sys

ROOT = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Data"
ZH = ROOT + r"\l10n_zh.json"
EN = ROOT + r"\l10n_en.json"
JA = ROOT + r"\l10n_ja.json"


def load(path):
    with io.open(path, "r", encoding="utf-8") as f:
        return f.read()


def shape(text):
    """与 L10n.Shape / Tools/L10nGen.py 同一条归一规则：数字段 -> §。"""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c < "0" or c > "9":
            out.append(c)
            i += 1
            continue
        while i < n and "0" <= text[i] <= "9":
            i += 1
        while i + 1 < n and text[i] in ".,:" and "0" <= text[i + 1] <= "9":
            i += 2
            while i < n and "0" <= text[i] <= "9":
                i += 1
        out.append("\u00a7")
    return "".join(out)


def set_entry(path, key, zval, vval, touch_z):
    raw = load(path)
    obj = json.loads(raw)
    entry = [e for e in obj["entries"] if e["k"] == key]
    assert len(entry) == 1, (path, key, len(entry))
    pat = re.compile(r'("k": "' + re.escape(key) + r'",\s*\n\s*"z": )("(?:[^"\\]|\\.)*")(,\s*\n\s*"v": )("(?:[^"\\]|\\.)*")')
    m = pat.search(raw)
    assert m, (path, "pattern not found")
    new_z = json.dumps(zval if touch_z else entry[0]["z"], ensure_ascii=False)
    new_v = json.dumps(vval, ensure_ascii=False)
    raw2 = raw[:m.start()] + m.group(1) + new_z + m.group(3) + new_v + raw[m.end():]
    obj2 = json.loads(raw2)          # 校验：整份表仍然是合法 JSON
    got = [e for e in obj2["entries"] if e["k"] == key][0]
    assert got["z"] == (zval if touch_z else entry[0]["z"])
    assert got["v"] == vval
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(raw2)
    return got


items = json.loads(load(ROOT + r"\items.json"))
desc = [it for it in items["items"] if it["id"] == "gamble_glove"][0]["desc"]
z = shape(desc)
print("digits ->", z.count("\u00a7"))

EN_V = (
    "Trigger: always on after purchase. Effect: the wrist feels heavier and the step a little "
    "slower, but every swing lands harder. Numbers: crit chance +\u00a7 points "
    "(\u00a7% \u21d2 \u00a7%), crit damage +\u00a7% (\u00d7\u00a7 \u21d2 \u00d7\u00a7, i.e. \u00a7%); "
    "the cost is **movement speed \u2212\u00a7%** (\u00a7 m/s \u21d2 \u00a7 m/s) and "
    "**attack speed \u2212\u00a7%** (speed \u00a7 \u21d2 \u00a7, so hoe swings \u00d7\u00a7, i.e. "
    "\u00a7% slower). Limit: this is a **cost-for-gain** item \u2014 both halves always apply and you "
    "cannot keep only one; buying more stacks linearly (\u00a7 copies = crit chance \u00a7%, crit "
    "damage \u00d7\u00a7, movement and attack speed \u2212\u00a7% each), and the more you stack the "
    "slower you walk and swing, but the two crit halves stack with them; it does not touch attack "
    "range or the damage multiplier."
)
JA_V = (
    "\u767a\u52d5\uff1a\u8cfc\u5165\u5f8c\u306f\u5e38\u6642\u6709\u52b9\u3002\u52b9\u679c\uff1a"
    "\u624b\u9996\u304c\u5c11\u3057\u91cd\u304f\u306a\u308a\u8db3\u53d6\u308a\u3082\u5c11\u3057"
    "\u9045\u304f\u306a\u308b\u304c\u3001\u632f\u308c\u3070\u3088\u308a\u91cd\u3044\u4e00\u6483"
    "\u306b\u306a\u308b\u3002\u6570\u5024\uff1a\u30af\u30ea\u30c6\u30a3\u30ab\u30eb\u7387 "
    "+\u00a7 \u30dd\u30a4\u30f3\u30c8\uff08\u00a7% \u21d2 \u00a7%\uff09\u3001"
    "\u30af\u30ea\u30c6\u30a3\u30ab\u30eb\u30c0\u30e1\u30fc\u30b8 +\u00a7%\uff08\u00d7\u00a7 "
    "\u21d2 \u00d7\u00a7\u3001\u3064\u307e\u308a \u00a7%\uff09\u3002\u4ee3\u511f\u306f"
    "**\u79fb\u52d5\u901f\u5ea6 \u2212\u00a7%**\uff08\u00a7 m/s \u21d2 \u00a7 m/s\uff09\u3068"
    "**\u653b\u6483\u901f\u5ea6 \u2212\u00a7%**\uff08\u653b\u6483\u901f\u5ea6 \u00a7 \u21d2 "
    "\u00a7\u3001\u30af\u30ef\u306e\u9593\u9694\u306f \u00d7\u00a7\u3001\u3064\u307e\u308a "
    "\u00a7% \u9045\u3044\uff09\u3002\u5236\u9650\uff1a**\u4ee3\u511f\u3068\u5f15\u304d\u63db"
    "\u3048**\u306e\u4e00\u54c1\u3067\u3001\u4e21\u534a\u5206\u304c\u540c\u6642\u306b\u52b9"
    "\u304d\u3001\u7247\u65b9\u3060\u3051\u53d6\u308b\u3053\u3068\u306f\u3067\u304d\u306a\u3044"
    "\u3002\u8907\u6570\u8cb7\u3046\u3068\u7dda\u5f62\u306b\u91cd\u306a\u308b\uff08\u00a7 "
    "\u500b = \u30af\u30ea\u30c6\u30a3\u30ab\u30eb\u7387 \u00a7%\u3001\u30af\u30ea\u30c6\u30a3"
    "\u30ab\u30eb\u30c0\u30e1\u30fc\u30b8 \u00d7\u00a7\u3001\u79fb\u52d5\u901f\u5ea6\u3068"
    "\u653b\u6483\u901f\u5ea6\u306f\u5404 \u2212\u00a7%\uff09\u3002\u7a4d\u3080\u307b\u3069"
    "\u6b69\u304f\u306e\u3082\u632f\u308b\u306e\u3082\u9045\u304f\u306a\u308b\u304c\u3001"
    "\u30af\u30ea\u30c6\u30a3\u30ab\u30eb\u306e\u4e8c\u3064\u3082\u4e00\u7dd2\u306b\u91cd\u306a"
    "\u308b\u3002\u653b\u6483\u7bc4\u56f2\u3068\u30c0\u30e1\u30fc\u30b8\u500d\u7387\u306b\u306f"
    "\u89e6\u3089\u306a\u3044\u3002"
)

print("zh §", z.count("\u00a7"), "| en §", EN_V.count("\u00a7"), "| ja §", JA_V.count("\u00a7"))
assert EN_V.count("\u00a7") == z.count("\u00a7"), "en placeholders mismatch"
assert JA_V.count("\u00a7") == z.count("\u00a7"), "ja placeholders mismatch"

set_entry(ZH, "desc.gamble_glove", z, z, True)
set_entry(EN, "desc.gamble_glove", "", EN_V, False)
set_entry(JA, "desc.gamble_glove", "", JA_V, False)
print("OK: 三份 l10n 表的 desc.gamble_glove 已同步（z/v），整份表仍是合法 JSON")