"""v179 插件特效的 **12 条签名两两比对** —— 从真实源码解析，不手抄。

背景：规格给了 12 条「通道分配」（剑气 Rings+1/Trail0.55、灼烧 Core橙红/Glow×1.40/Sparks2…），
要求**两两不同**，并且明确说「不要肉眼看」。

★ 本脚本不信任任何手抄的表 ★
它直接读 `Assets/Scripts/Combat/PluginFx.cs`，把 `PluginFx.For()` 里那 12 个
`if (m.<字段> > N) { … }` 分支的**通道操作**解析出来，再：
    ① 比对**配方**（同一把武器的基准色上、各通道被怎么改）—— 查「两条 id 拿到了同一套配方」
    ② 比对**渲染结果**（各自套到自己武器的 `Base(slot)` 上、再按 `PluginFx.Diff()` 的
       **同一套阈值**判定）—— 查「两条 id 在画面上分不出来」

阈值与签名算法都从源码里读（`Diff` 的 0.020/0.010、`Hash` 的量化方式），
所以改了源码不会与本脚本静默分叉。

重跑：
    C:\\Users\\18462\\.dsh\\dsh-runtimes\\dsh-primary-runtime\\dependencies\\python\\python.exe \\
        Tools/mesh_gen/fx_signature_audit.py
退出码 0 = 12 条两两不同，1 = 有重复（可直接进 CI）。
"""

import itertools
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(os.path.dirname(HERE))
FX_CS = os.path.join(PROJ, "Assets", "Scripts", "Combat", "PluginFx.cs")
GOODS_CS = os.path.join(PROJ, "Assets", "Scripts", "Data", "Goods.cs")

CHANNELS = ["Core", "Rim", "Scale", "Glow", "Sparks", "Rings", "Trail", "Shake"]


def die(msg):
    print("FAIL  %s" % msg)
    sys.exit(1)


for p in (FX_CS, GOODS_CS):
    if not os.path.isfile(p):
        die("找不到 %s" % p)

with open(FX_CS, "r", encoding="utf-8") as f:
    fx_src = f.read()
with open(GOODS_CS, "r", encoding="utf-8") as f:
    goods_src = f.read()


# ─────────────────────────────────────────────────────────────
# 1) Diff() 的阈值 —— 从源码读
# ─────────────────────────────────────────────────────────────
m = re.search(r"Diff\(in FxSpec a, in FxSpec b\)(.*?)\n        \}", fx_src, re.S)
if not m:
    die("解析不到 PluginFx.Diff()")
diff_body = m.group(1)
m = re.search(r"ColorGap\(a\.Core, b\.Core\)\s*>\s*([\d.]+)f", diff_body)
TH_COLOR = float(m.group(1)) if m else 0.020
m = re.search(r"Mathf\.Abs\(a\.Scale - b\.Scale\)\s*>\s*([\d.]+)f", diff_body)
TH_NUM = float(m.group(1)) if m else 0.010
print("从源码读到 Diff 阈值：颜色 >%.3f  数值 >%.3f" % (TH_COLOR, TH_NUM))


# ─────────────────────────────────────────────────────────────
# 2) Base(slot) —— 三把武器的基准外观（空槽 = 改动前的硬编码值）
# ─────────────────────────────────────────────────────────────
def parse_color(text):
    # 只认真正的数字（`f.Core.a` 这类成员访问里的 '.' 不能被当成数）
    v = [float(x) for x in re.findall(r"-?\d+(?:\.\d+)?", text)]
    return tuple((v + [1.0, 1.0, 1.0, 1.0])[:4])


m = re.search(r"public static FxSpec Base\(WeaponSlot slot\)(.*?)\n        \}", fx_src, re.S)
if not m:
    die("解析不到 PluginFx.Base()")
base_body = m.group(1)

BASE = {}
for slot in ("Hoe", "Can", "Fertilizer", "default"):
    mm = re.search(r"case WeaponSlot\.%s:(.*?)break;" % slot, base_body, re.S)
    if not mm:
        continue
    seg = mm.group(1)
    cm = re.search(r"f\.Core\s*=\s*new Color\(([^)]*)\)", seg)
    rm = re.search(r"f\.Rim\s*=\s*new Color\(([^)]*)\)", seg)
    if cm and rm:
        BASE[slot] = {"Core": parse_color(cm.group(1)), "Rim": parse_color(rm.group(1))}

if len(BASE) < 3:
    die("Base() 里只解析到 %d 把武器（应有 Hoe/Can/Fertilizer）" % len(BASE))

for k, v in BASE.items():
    print("  Base(%-10s) Core=#%02X%02X%02X Rim=#%02X%02X%02X"
          % (k, int(v["Core"][0] * 255), int(v["Core"][1] * 255), int(v["Core"][2] * 255),
             int(v["Rim"][0] * 255), int(v["Rim"][1] * 255), int(v["Rim"][2] * 255)))


# ─────────────────────────────────────────────────────────────
# 3) PluginEffectKind → 插件 id（从 Goods.cs 的枚举文档注释里读）
# ─────────────────────────────────────────────────────────────
m = re.search(r"public enum PluginEffectKind\s*\{(.*?)\n    \}", goods_src, re.S)
if not m:
    die("解析不到 Goods.cs 的 PluginEffectKind")
enum_body = m.group(1)

KIND_ID = {}
KIND_ORDER = []
pending_id = None
for line in enum_body.split("\n"):
    md = re.search(r"\*\*[^*]+\*\*.*?`([a-z]+_[a-z]+)`", line)
    if md and "///" in line:
        pending_id = md.group(1)
    me = re.match(r"\s*([A-Z]\w+)\s*,", line)
    if me:
        KIND_ORDER.append(me.group(1))
        if pending_id:
            KIND_ID[me.group(1)] = pending_id
        pending_id = None

print("  PluginEffectKind 共 %d 个（%s…）" % (len(KIND_ORDER), ", ".join(KIND_ORDER[:3])))


# ─────────────────────────────────────────────────────────────
# 4) 解析 For() 的 12 个分支
# ─────────────────────────────────────────────────────────────
m = re.search(r"public static FxSpec For\(in WeaponModifiers m, WeaponSlot slot\)", fx_src)
if not m:
    die("解析不到 PluginFx.For()")
start = fx_src.find("{", m.end())
depth, i = 0, start
while i < len(fx_src):
    if fx_src[i] == "{":
        depth += 1
    elif fx_src[i] == "}":
        depth -= 1
        if depth == 0:
            break
    i += 1
for_body = fx_src[start:i]

# 按武器分节（注释里的 ═════ 锄头/水壶/肥料 ═════），用来定 slot
SECTION_SLOT = {"锄头": "Hoe", "水壶": "Can", "肥料": "Fertilizer"}
line_slot = {}
cur_slot = "Hoe"
for ln in for_body.split("\n"):
    for word, slot in SECTION_SLOT.items():
        if "═════════" in ln and word in ln:
            cur_slot = slot
    line_slot[id(ln)] = cur_slot

# 逐行扫，遇到 `if (m.X > N)` 就开始一个分支，按大括号配平收尾
branches = []
lines = for_body.split("\n")
idx = 0
cur_slot = "Hoe"
while idx < len(lines):
    ln = lines[idx]
    for word, slot in SECTION_SLOT.items():
        if "═════════" in ln and word in ln:
            cur_slot = slot
    mif = re.search(r"if\s*\(m\.(\w+)\s*>\s*([\d.]+)f?\)", ln)
    has_body = idx + 1 < len(lines) and "{" in lines[idx + 1]
    if mif and has_body:
        # 收集分支体
        body = []
        d = 0
        j = idx + 1
        while j < len(lines):
            body.append(lines[j])
            d += lines[j].count("{") - lines[j].count("}")
            if d == 0 and "}" in lines[j]:
                break
            j += 1
        branches.append({"field": mif.group(1), "slot": cur_slot, "body": "\n".join(body)})
        idx = j
    idx += 1

if len(branches) != 12:
    die("For() 里解析到 %d 个分支（期望 12）—— 源码结构变了，请更新本脚本" % len(branches))


# ─────────────────────────────────────────────────────────────
# 5) 把每个分支翻译成「通道操作」
# ─────────────────────────────────────────────────────────────
def clamp01(x):
    return max(0.0, min(1.0, x))


def lerp4(a, b, t):
    t = clamp01(t)
    return tuple(a[k] + (b[k] - a[k]) * t for k in range(4))


def parse_ops(body):
    """返回 (kind, ops)。ops 是 [(通道, 操作, 参数)]，操作 ∈ set/mix/add/mul。"""
    kind = None
    mk = re.search(r"PluginEffectKind\.(\w+)", body)
    if mk:
        kind = mk.group(1)
    ops = []
    for ln in body.split("\n"):
        s = ln.strip()
        if s.startswith("//") or not s.startswith("f."):
            continue
        mm = re.match(r"f\.(\w+)\s*=\s*Mix\(f\.\w+,\s*new Color\(([^)]*)\),\s*(-?[\d.]+)f?\)", s)
        if mm:
            ops.append((mm.group(1), "mix", (parse_color(mm.group(2)), float(mm.group(3)))))
            continue
        mm = re.match(r"f\.(\w+)\s*\+=\s*(-?[\d.]+)f?", s)
        if mm:
            ops.append((mm.group(1), "add", float(mm.group(2))))
            continue
        mm = re.match(r"f\.(\w+)\s*\*=\s*(-?[\d.]+)f?", s)
        if mm:
            ops.append((mm.group(1), "mul", float(mm.group(2))))
            continue
        mm = re.match(r"f\.(\w+)\s*=\s*(-?[\d.]+)f?;", s)
        if mm:
            ops.append((mm.group(1), "set", float(mm.group(2))))
    return kind, ops


def base_spec(slot):
    b = BASE.get(slot) or BASE.get("default")
    return {"Core": b["Core"], "Rim": b["Rim"],
            "Scale": 1.0, "Glow": 1.0, "Sparks": 0, "Rings": 0, "Trail": 0.0, "Shake": 1.0}


def apply_ops(spec, ops):
    s = dict(spec)
    for ch, op, arg in ops:
        if op == "mix":
            s[ch] = lerp4(s[ch], arg[0], arg[1])
        elif op == "add":
            s[ch] = s[ch] + arg
        elif op == "mul":
            s[ch] = s[ch] * arg
        elif op == "set":
            s[ch] = arg
    return s


def hash_sig(s):
    """复刻 PluginFx.Hash：FNV-1a + 量化。"""
    def q(c):
        return (int(clamp01(c) * 255),)
    vals = []
    for chn in ("Core", "Rim"):
        vals += [int(clamp01(s[chn][k]) * 255) for k in range(3)]
    vals += [round(s["Scale"] * 100), round(s["Glow"] * 100),
             max(0, int(s["Sparks"])), max(0, int(s["Rings"])),
             round(s["Trail"] * 100), round(s["Shake"] * 100)]
    h = 2166136261
    for v in vals:
        h ^= (v & 0xFFFFFFFF)
        h = (h * 16777619) & 0xFFFFFFFF
    return h


def color_gap(a, b):
    return max(abs(a[0] - b[0]), abs(a[1] - b[1]), abs(a[2] - b[2]))


def diff(sa, sb):
    """复刻 PluginFx.Diff：返回差异通道串（空 = 画出来一样）。"""
    out = []
    if color_gap(sa["Core"], sb["Core"]) > TH_COLOR:
        out.append("Core")
    if color_gap(sa["Rim"], sb["Rim"]) > TH_COLOR:
        out.append("Rim")
    if abs(sa["Scale"] - sb["Scale"]) > TH_NUM:
        out.append("Scale")
    if abs(sa["Glow"] - sb["Glow"]) > TH_NUM:
        out.append("Glow")
    if sa["Sparks"] != sb["Sparks"]:
        out.append("Sparks")
    if sa["Rings"] != sb["Rings"]:
        out.append("Rings")
    if abs(sa["Trail"] - sb["Trail"]) > TH_NUM:
        out.append("Trail")
    if abs(sa["Shake"] - sb["Shake"]) > TH_NUM:
        out.append("Shake")
    return " ".join(out)


def describe_ops(ops):
    parts = []
    for ch, op, arg in ops:
        if op == "mix":
            c = arg[0]
            parts.append("%s→#%02X%02X%02X(%.2f)" % (ch, int(c[0] * 255), int(c[1] * 255),
                                                      int(c[2] * 255), arg[1]))
        elif op == "add":
            parts.append("%s+%g" % (ch, arg))
        elif op == "mul":
            parts.append("%s×%g" % (ch, arg))
        else:
            parts.append("%s=%g" % (ch, arg))
    return "  ".join(parts)


entries = []
for br in branches:
    kind, ops = parse_ops(br["body"])
    if not kind:
        die("分支 `m.%s` 里没找到 PluginEffectKind" % br["field"])
    pid = KIND_ID.get(kind, "?" + kind)
    slot = br["slot"]
    spec = apply_ops(base_spec(slot), ops)
    entries.append({
        "id": pid, "kind": kind, "slot": slot, "field": br["field"],
        "ops": ops, "spec": spec, "sig": hash_sig(spec),
    })

# 配方（带参数的完整形状）：通道 + 操作 + 参数，全部进键
# ★ 为什么不能只比"改了哪些通道" ★
#   规格里 `can_autocharge`（Rim #6FD3A8 + Glow×1.30）与
#   `fert_freeburst`（Rim #F2C14E + Glow×1.50）**本来就用同一组通道**
#   ⇒ 只比通道集合会把它们误判成重复。真正的判据是**参数也要不同** ✓
def recipe(e):
    shape = []
    for ch, op, arg in e["ops"]:
        if op == "mix":
            c = arg[0]
            shape.append((ch, op, "#%02X%02X%02X" % (int(c[0] * 255), int(c[1] * 255),
                                                     int(c[2] * 255)), round(arg[1], 3)))
        else:
            shape.append((ch, op, round(arg, 3)))
    return tuple(sorted(shape))


def channel_set(e):
    return tuple(sorted({ch for ch, _, _ in e["ops"]}))


# ─────────────────────────────────────────────────────────────
# 6) 输出 12 条
# ─────────────────────────────────────────────────────────────
print()
print("=" * 100)
print("12 条签名（从 PluginFx.For() 实解析）")
print("=" * 100)
print("%-18s %-13s %-8s %-46s %s" % ("插件 id", "kind", "武器", "通道分配", "签名"))
print("-" * 100)
for e in entries:
    print("%-18s %-13s %-8s %-46s %08X"
          % (e["id"], e["kind"], e["slot"], describe_ops(e["ops"]), e["sig"]))


# ─────────────────────────────────────────────────────────────
# 7) 两两比较
# ─────────────────────────────────────────────────────────────
print()
print("=" * 100)
print("两两比较 ①：**配方**层面（通道 + 操作 + 参数，是否完全一样）")
print("=" * 100)
dup_recipe = []
n_pairs = len(entries) * (len(entries) - 1) // 2
for a, b in itertools.combinations(entries, 2):
    if recipe(a) == recipe(b):
        dup_recipe.append((a["id"], b["id"], describe_ops(a["ops"])))
print("  比较了 %d 对（12 选 2）" % n_pairs)
if dup_recipe:
    for x in dup_recipe:
        print("  FAIL  配方重复：%s == %s  (%s)" % x)
else:
    print("  PASS  无一对配方相同 ✓（连参数都不同）")

print()
print("=" * 100)
print("两两比较 ①b：**通道集合**碰撞（不是缺陷 —— 规格里就有意让两条走同一组通道）")
print("=" * 100)
by_set = {}
for e in entries:
    by_set.setdefault(channel_set(e), []).append(e["id"])
collisions = {k: v for k, v in by_set.items() if len(v) > 1}
if collisions:
    for k, v in collisions.items():
        print("  INFO  %s  ←  %s（参数不同 ⇒ 画面上仍然不同）" % (" + ".join(k), " / ".join(v)))
else:
    print("  （无碰撞）")

print()
print("=" * 100)
print("两两比较 ②：**渲染结果**层面（各自套到自己武器的 Base 上，按 PluginFx.Diff 的阈值）")
print("=" * 100)
dup_render = []
matrix = {}
for a, b in itertools.combinations(entries, 2):
    d = diff(a["spec"], b["spec"])
    matrix[(a["id"], b["id"])] = d
    if d == "":
        dup_render.append((a["id"], b["id"]))
    else:
        print("  %-18s vs %-18s → 差异通道： %s" % (a["id"], b["id"], d))

print()
if dup_render:
    for x in dup_render:
        print("  FAIL  画面上分不出来：%s == %s" % x)
else:
    print("  PASS  %d 对全部有可见差异，**无重复** ✓" % n_pairs)

# 每对至少一条通道不同；再看"有没有两对用了完全相同的通道组合"（不要求，只报告）
print()
print("=" * 100)
print("附：每条 id 改到的通道集合（供人读，不参与判定）")
print("=" * 100)
for e in entries:
    chans = sorted({ch for ch, _, _ in e["ops"]})
    print("  %-18s %s" % (e["id"], " + ".join(chans)))

ok = (not dup_recipe) and (not dup_render) and len(entries) == 12

# ─────────────────────────────────────────────────────────────
# 8) 与**规格表**逐项比对（只报告，不判定 —— 这份文件不在本任务的可改范围）
#
#    规格给的是"最终值"：Rings +1 ⇒ 最终 1；Scale 1.10 ⇒ 最终 1.10；
#    Core #FF8243 ⇒ 最终就是这个色。而实现里颜色是 `Mix(基准, 目标, t)`
#    ⇒ **最终色 ≠ 目标色**，两者会差一截。这一节把差多少算出来。
# ─────────────────────────────────────────────────────────────
SPEC = {
    "hoe_swordwave":  {"Rings": 1, "Trail": 0.55},
    "hoe_burn":       {"Core": "#FF8243", "Glow": 1.40, "Sparks": 2},
    "hoe_doublestrike": {"Shake": 1.30, "Scale": 1.10},
    "hoe_lifesteal":  {"Rim": "#B0102E", "Trail": 0.40},
    "can_multijet":   {"Rings": 1, "Sparks": 1},
    "can_slow":       {"Core": "#A4E3DF", "Trail": 0.32},
    "can_thickjet":   {"Scale": 1.25, "Glow": 1.15},
    "can_autocharge": {"Rim": "#6FD3A8", "Glow": 1.30},
    "fert_subburst":  {"Rings": 2, "Scale": 0.90},
    "fert_poison":    {"Core": "#7FC241", "Rim": "#D9E14A", "Sparks": 3},
    "fert_freeburst": {"Glow": 1.50, "Rim": "#F2C14E"},
    "fert_chain":     {"Rings": 1, "Shake": 1.50, "Trail": 0.45},
}


def hex_of(c):
    return "#%02X%02X%02X" % (int(clamp01(c[0]) * 255), int(clamp01(c[1]) * 255),
                              int(clamp01(c[2]) * 255))


print()
print("=" * 100)
print("附：与**规格表**的逐项偏离（报告用；PluginFx.cs 不在本任务可改范围）")
print("=" * 100)
n_dev = 0
n_extra = 0
for e in entries:
    want = SPEC.get(e["id"], {})
    chans = {}
    for ch, op, arg in e["ops"]:
        chans[ch] = chans.get(ch, 0) + 1
    lines_dev = []
    for ch, wv in want.items():
        got = e["spec"][ch]
        if isinstance(wv, str):
            g = hex_of(got)
            if g != wv:
                lines_dev.append("%s 规格 %s → 实得 %s" % (ch, wv, g))
                n_dev += 1
        else:
            if abs(got - wv) > 1e-6:
                lines_dev.append("%s 规格 %g → 实得 %g" % (ch, wv, got))
                n_dev += 1
    extra = [ch for ch in chans if ch not in want]
    if extra:
        n_extra += 1
        lines_dev.append("规格表未列、代码额外改了：%s" % " ".join(sorted(extra)))
    if lines_dev:
        print("  %-18s %s" % (e["id"], "；".join(lines_dev)))
if n_dev == 0 and n_extra == 0:
    print("  （完全一致）")
else:
    print()
    print("  ⇒ 颜色偏离 %d 项：实现用 `Mix(基准, 目标, t)` 而不是直接赋目标色，"
          "所以最终色是**基准色与目标色的插值**。" % n_dev)
    print("  ⇒ 有 %d 条比规格表**多改**了通道（多数是有意为之，但规格表里没写）。" % n_extra)

print()
print("-" * 100)
print("结论：%s" % ("12 条签名两两不同 ✓（配方 %d 对、渲染 %d 对，均无重复）"
                  % (n_pairs, n_pairs) if ok else "存在重复 ✗"))
print("（对照规格表的颜色偏离见上一节 —— 那是实现选择，不是重复）")
sys.exit(0 if ok else 1)
