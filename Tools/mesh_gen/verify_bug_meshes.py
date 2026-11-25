#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
verify_bug_meshes.py —— 把**运行时**与**离线副本**钉在一起。

★★ 一句话：C# 是真相，`gen_bug_meshes.py` 是它的副本，本脚本负责证明"副本没跑偏" ★★
几何对不上时**改离线副本、不改 `ProceduralBugs.cs`**（一个 `.cs` 都不许因为校验脚本而改）。

它比对五方：
    ① `Tools/mesh_gen/out_bugs/bug_mesh_manifest.json`  离线副本的产物（gen_bug_meshes.py 写）
    ② 本脚本用同一套公式**再重建一遍**                     （抓"清单被人改过"这种手滑）
    ③ `Tools/mesh_gen/out_bugs/<id>_<part>.obj`          落盘的 OBJ（抓"文件是旧的"）
    ④ `Docs/reports/bug-meshes.txt`                      **Unity 运行时**的真实输出
                                                         （BugShowcase.Report() 写，C# 真跑出来的数）
    ⑤ `Assets/Scripts/Model/ProceduralBugs.cs`           C# 源码里的**形状参数**

结论：
    · 零件集合 / 顶点数 / 三角数 —— 必须**逐位相同**
    · 包围盒 / 形状参数 —— 必须落在 2e-4 内（C# 是 float32，Python 是 float64）
    · 报告的 `meshes=` / `bounds=` **整行文本** —— 必须与离线副本逐字相同（4 位小数也对齐）
    · 每个零件的**有符号体积 > 0** ⇒ 绕序一致朝外（没有内翻的面）
    · 每种虫的**体节主网格三角数** == `BODY_TOPO` 独立算出的拓扑式
    · **BOSS 段与备份逐字相同**（`boss_frozen.txt` 是从备份原样拷贝的字节）
    · 生成脚本自身的静态约束（**不允许出现 `new Material(`**、必须有 crc32/xorshift 常数 ...）
    · 源码里的形状参数（`ShapeOf` / `HipHeight` / `HipOf` 的数值字面量）与清单里的**参数钉**一致
      ⇒ **C# 那边改一个参数，这里立刻点名 FAIL**

★ 为什么指纹（顶点字节级 crc32）不参与比对 ★
报告里的 `[MESH-FINGERPRINT]` 是 C# 用 **float32 逐步运算**的结果；离线副本是 float64 算完再转。
两边逐位必然差几个 ULP ⇒ 拿它当判据会把**正确**的实现判成 FAIL ✗（实测 5 个 id 全不同）
判据要用"零件集合 + 计数（整数，可以要求逐位相同）+ 4 位小数的包围盒文本（float32 的舍入远比 1e-4 小）"✓

用法：
    python Tools/mesh_gen/verify_bug_meshes.py               # 全量校验；退出码 0 = 全 PASS
    python Tools/mesh_gen/verify_bug_meshes.py --check       # 同上（别名，CI 两种写法都能用）
    python Tools/mesh_gen/verify_bug_meshes.py --report F    # 换一份运行时报告（演练 / 回归用）
    python Tools/mesh_gen/verify_bug_meshes.py --selftest    # 判别力自检：扰动一个参数必须被抓到
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import sys
from collections import Counter

import gen_bug_meshes as G

try:      # ★ 输出里有中文：在 ASCII/GBK 控制台上别让编码把 CI 弄红（只有显示受影响）
    sys.stdout.reconfigure(errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
MANIFEST = os.path.join(G.OUT_DIR, "bug_mesh_manifest.json")
RUNTIME_REPORT = os.path.join(PROJECT, "Docs", "reports", "bug-meshes.txt")
CS_SOURCE = os.path.join(PROJECT, "Assets", "Scripts", "Model", "ProceduralBugs.cs")
BOSS_FROZEN = os.path.join(HERE, "boss_frozen.txt")
TOL = 2e-4

results = []
FULL_FAILURES = []      # 运行时比对的**全部**差异（行内只显示前几条，末尾整份打印）


def check(name, ok, detail=""):
    results.append((name, bool(ok), detail))
    print("%-4s %-52s %s" % ("PASS" if ok else "FAIL", name, detail))
    return ok


def close(a, b, tol=TOL):
    return abs(a - b) <= tol


def section(title):
    print("\n── %s " % title + "─" * max(0, 74 - len(title)))


# ══════════════════════════════════════════════════════════════════════════
# ① 离线副本自洽：清单 vs 现算
# ══════════════════════════════════════════════════════════════════════════
def verify_manifest():
    if not os.path.exists(MANIFEST):
        return check("manifest exists", False, "run gen_bug_meshes.py first")
    with open(MANIFEST, "r", encoding="utf-8") as f:
        man = json.load(f)
    check("manifest version", man.get("version") == G.MANIFEST_VERSION, str(man.get("version")))
    check("manifest ids", man.get("type_ids") == G.TYPE_IDS, str(man.get("type_ids")))
    check("manifest boss unit", man.get("boss_unit") == G.BOSS_UNIT, str(man.get("boss_unit")))

    ok_parts = ok_counts = ok_shape = True
    for entry in man["types"]:
        ident = entry["id"]
        boss = ident == G.BOSS_ID
        s, parts = G.build_type(ident, boss)
        if entry["crc32"] != G.crc32(ident):
            ok_counts = False
            check("crc32 %s" % ident, False, "%s != %s" % (entry["crc32"], G.crc32(ident)))
        if entry.get("species") != s["Kind"]:
            ok_shape = False
            check("species %s" % ident, False, "%s != %s" % (entry.get("species"), s["Kind"]))
        for key, val in entry["shape"].items():
            if key == "Id":
                continue
            now = s[key]
            now = round(now, 9) if isinstance(now, float) else now
            if now != val:
                ok_shape = False
                check("shape %s.%s" % (ident, key), False, "manifest %s != rebuild %s" % (val, now))
        if set(entry["parts"].keys()) != set(parts.keys()):
            ok_parts = False
            check("parts %s" % ident, False,
                  "%s != %s" % (sorted(entry["parts"]), sorted(parts)))
            continue
        for pname, b in parts.items():
            info = entry["parts"][pname]
            want = G.counts_of(b)
            if (info["verts"], info["tris"]) != want:
                ok_counts = False
                check("%s/%s counts" % (ident, pname), False,
                      "manifest %dv/%dt vs rebuild %dv/%dt" %
                      (info["verts"], info["tris"], want[0], want[1]))
    check("manifest parts == rebuild", ok_parts, "%d types" % len(man["types"]))
    check("manifest counts == rebuild", ok_counts, "verts+tris, all parts")
    check("manifest shape/species == rebuild", ok_shape, "u0..u16 + w0..w7 派生量")
    return True


# ══════════════════════════════════════════════════════════════════════════
# ② 几何健康：绕序朝外、无退化三角形、体节拓扑、零件集合
# ══════════════════════════════════════════════════════════════════════════
def verify_geometry():
    ok_vol = ok_area = ok_topo = ok_sets = True
    vol_detail, topo_detail, set_detail = [], [], []
    for ident in G.TYPE_IDS:
        boss = ident == G.BOSS_ID
        s, parts = G.build_type(ident, boss)

        want_set = set(G.EXPECTED_PART_SETS[ident])
        if set(parts) != want_set:
            ok_sets = False
            set_detail.append("%s: %s != %s" % (ident, sorted(parts), sorted(want_set)))

        got = G.counts_of(parts["body"])[1]
        want = G.expected_body_tris(ident, boss, s["Sides"])
        topo_detail.append("%s=%d" % (ident, got))
        if got != want:
            ok_topo = False
            topo_detail[-1] += "(!=%d)" % want

        for pname, b in parts.items():
            vol = G.signed_volume(b)
            if vol <= 0.0:
                ok_vol = False
                vol_detail.append("%s/%s=%.6g" % (ident, pname, vol))
            if G.min_area(b) <= 1e-9:
                ok_area = False

    check("part set == EXPECTED_PART_SETS (per species)", ok_sets,
          "; ".join(set_detail) if set_detail else "4 种昆虫 + BOSS 各不相同")
    check("body tris == BODY_TOPO formula", ok_topo, ",".join(topo_detail))
    check("outward winding (signed volume > 0)", ok_vol,
          ",".join(vol_detail) if vol_detail else "all parts")
    check("no degenerate triangles", ok_area, "min area > 1e-9")

    # 体型：BOSS 约 2.5 倍
    _, boss_parts = G.build_type(G.BOSS_ID, True)
    bb = G.bounds_of(boss_parts["body"])
    boss_h = bb["max"][1] - bb["min"][1]
    ratios = []
    for ident in G.TYPE_IDS:
        if ident == G.BOSS_ID:
            continue
        _, parts = G.build_type(ident, False)
        bd = G.bounds_of(parts["body"])
        ratios.append(boss_h / (bd["max"][1] - bd["min"][1]))
    return check("boss ~2.5x normal bug (body height)",
                 all(2.0 <= r <= 3.2 for r in ratios),
                 ",".join("%.2f" % r for r in ratios))


# ══════════════════════════════════════════════════════════════════════════
# ③ OBJ 落盘一致性（抓"文件是旧的 / 忘了重跑 gen"）
# ══════════════════════════════════════════════════════════════════════════
def read_obj(path):
    verts = []
    faces = 0
    with open(path, "r", encoding="ascii") as f:
        for line in f:
            if line.startswith("v "):
                _, x, y, z = line.split()
                verts.append((float(x), float(y), float(z)))
            elif line.startswith("f "):
                faces += 1
    return verts, faces


def verify_obj():
    if not os.path.isdir(G.OUT_DIR):
        return check("out_bugs/ exists", False, "run gen_bug_meshes.py first")
    ok = True
    detail = []
    n_files = 0
    for ident in G.TYPE_IDS:
        boss = ident == G.BOSS_ID
        s, parts = G.build_type(ident, boss)
        for pname, b in parts.items():
            path = os.path.join(G.OUT_DIR, "%s_%s.obj" % (ident, pname))
            n_files += 1
            if not os.path.exists(path):
                ok = False
                detail.append("missing %s" % os.path.basename(path))
                continue
            verts, faces = read_obj(path)
            want_v, want_t = G.counts_of(b)
            if len(verts) != want_v or faces != want_t:
                ok = False
                detail.append("%s/%s %dv/%df != %dv/%dt" % (ident, pname, len(verts), faces, want_v, want_t))
                continue
            worst = 0.0
            for got, exp in zip(verts, b.v):
                for i in range(3):
                    worst = max(worst, abs(got[i] - exp[i]))
            if worst > 1e-6:            # OBJ 写的是 %.6f ⇒ 舍入误差 <= 5e-7
                ok = False
                detail.append("%s/%s verts off by %.2e" % (ident, pname, worst))
    return check("out_bugs/*.obj == rebuild (%d files)" % n_files, ok,
                 "; ".join(detail[:4]) if detail else "counts + 顶点逐点 <=1e-6")


# ══════════════════════════════════════════════════════════════════════════
# ④ 源码静态约束 + **形状参数钉**
# ══════════════════════════════════════════════════════════════════════════
def verify_source(man):
    if not os.path.exists(CS_SOURCE):
        return check("ProceduralBugs.cs exists", False, CS_SOURCE)
    with open(CS_SOURCE, "r", encoding="utf-8") as f:
        src = f.read()

    check("crc32 polynomial", "0xEDB88320u" in src, "0xEDB88320")
    check("xorshift shifts 13/17/5", ("x << 13" in src and "x >> 17" in src and "x << 5" in src), "")
    check("seed-zero fallback", "0x9E3779B9u" in src, "0x9E3779B9")
    check("boss unit 2.5", "BossUnit = 2.5f" in src, "")
    check("all five ids listed", all('"%s"' % i in src for i in G.TYPE_IDS), str(G.TYPE_IDS))
    check("flat shading path", "RecalculateNormals()" in src, "per-triangle normals")
    check("uses ToonLook material pool", "ToonLook.Get(" in src, "no per-instance material")
    bad = re.findall(r"new\s+Material\s*\(", src)
    check("NO `new Material(` in ProceduralBugs.cs", len(bad) == 0, "%d occurrences" % len(bad))
    check("state machine reuses ModelView.State", "ModelView.State" in src, "")
    check("mesh cache present", "_assets" in src and "Dictionary<string, Assets>" in src, "")
    check("object pool present", "_pool" in src and "Stack<BugView>" in src, "")
    check("legs = 6", "new Transform[6]" in src, "")
    check("leg freq in 4..8 Hz", "4f + 4f * u16" in src, "")
    check("物种派发存在 SpeciesOf", "SpeciesOf" in src, "4 种昆虫 + Giant")

    # ── ★ 参数钉：C# 源码里的数值字面量 vs 清单记录的（改一个参数 ⇒ 点名 FAIL）★ ──
    pins_now = G.source_param_pins()
    stored = (man or {}).get("source_params") or {}
    if pins_now is None or not stored:
        check("C# param pins (ShapeOf/HipHeight/HipOf)", False,
              "清单里没有参数钉 ⇒ 重跑 gen_bug_meshes.py")
        return True
    for name, _sig in G.PINNED_METHODS:
        now, was = pins_now.get(name), stored.get(name)
        if now is None or was is None:
            check("param pin %s" % name, False, "C# 里找不到这个方法")
            continue
        if now == was:
            check("param pin %s" % name, True, "%d 个数值字面量一致" % len(now))
            continue
        gone = sorted((Counter(was) - Counter(now)).elements())
        new = sorted((Counter(now) - Counter(was)).elements())
        check("param pin %s" % name, False,
              "C# 改了：少了 %s 多了 %s（改完请重跑 gen_bug_meshes.py）" %
              (gone[:6], new[:6]))
    return True


# ══════════════════════════════════════════════════════════════════════════
# ⑤ 运行时报告 vs 离线副本（真正的"两边钉在一起"）
# ══════════════════════════════════════════════════════════════════════════
TYPE_BLOCK = re.compile(
    r"\[TYPE\]\s+(\w+)\n(.*?)(?=\n\[TYPE\]|\n\[MESH-FINGERPRINT\]|\n\[LEG-PHASE\]|\Z)", re.S)


def parse_report(text):
    """把 BugShowcase.Report() 的文本解析成 {id: {...}}。"""
    out = {}
    for m in TYPE_BLOCK.finditer(text):
        ident, body = m.group(1), m.group(2)
        rec = {"counts": {}, "bounds": {}, "meshes_text": None, "bounds_text": None}
        for line in body.splitlines():
            line = line.strip()
            if not line:
                continue
            if line.startswith("meshes="):
                # meshes=pig/bug body=180v/60t accent=168v/56t ... kind=Ladybug
                rec["meshes_text"] = line[len("meshes="):]
                for token in line.split():
                    if "=" in token and "v/" in token:
                        k, v = token.split("=", 1)
                        vv, tt = v.split("v/")
                        rec["counts"][k] = (int(vv), int(tt.rstrip("t")))
                continue
            if line.startswith("bounds="):
                rec["bounds_text"] = line[len("bounds="):]
                for part, mn, mx in re.findall(
                        r"(\w+)=min\(([-\d.,]+)\)max\(([-\d.,]+)\)", line):
                    rec["bounds"][part] = ([float(x) for x in mn.split(",")],
                                           [float(x) for x in mx.split(",")])
                continue
            kv = {}
            for token in line.split():
                if "=" in token:
                    k, v = token.split("=", 1)
                    kv[k] = v
            if not kv:
                continue
            if "crc32" in kv:
                rec["crc32"] = int(kv["crc32"], 16)
            for key in G.REPORT_PARAM_KEYS:
                if key in kv:
                    rec[key] = float(kv[key])
            for key in ("boss", "unit", "sides", "carapace", "horn", "tailSpike"):
                if key in kv:
                    rec[key] = float(kv[key])
        out[ident] = rec
    return out


def compare_type(ident, boss, s, parts, rec):
    """这一个 id 的差异：{"shape": [...], "counts": [...], "bounds": [...], "text": [...]}。"""
    shape_bad, count_bad, bounds_bad, text_bad = [], [], [], []

    # ── 形状参数（报告里的数是 C# 真跑出来的）──
    if rec.get("crc32") != G.crc32(ident):
        shape_bad.append("crc32 cs=%s py=0x%08X" % (rec.get("crc32"), G.crc32(ident)))
    for rk, sk in G.REPORT_PARAM_KEYS.items():
        if rk not in rec:
            shape_bad.append("missing " + rk)
        elif not close(rec[rk], s[sk]):
            shape_bad.append("%s cs=%.6f py=%.6f" % (rk, rec[rk], s[sk]))
    if rec.get("sides") != s["Sides"]:
        shape_bad.append("sides cs=%s py=%s" % (rec.get("sides"), s["Sides"]))
    for rk, sk in G.REPORT_FLAG_KEYS.items():
        want = 1.0 if s[sk] else 0.0
        if not close(rec.get(rk, -1), want):
            shape_bad.append("%s cs=%s py=%s" % (rk, rec.get(rk), want))
    if not close(rec.get("unit", -1), s["Unit"]):
        shape_bad.append("unit cs=%s py=%s" % (rec.get("unit"), s["Unit"]))

    # ── 零件集合 + 顶点/三角数：逐位相同（**两边都比**，多一个少一个都算错）──
    want_parts = set(parts)
    got_parts = set(rec["counts"])
    if want_parts != got_parts:
        count_bad.append("part set cs=%s py=%s" % (sorted(got_parts), sorted(want_parts)))
    for pname in sorted(want_parts & got_parts):
        want = G.counts_of(parts[pname])
        got = rec["counts"][pname]
        if got != want:
            count_bad.append("%s cs=%dv/%dt py=%dv/%dt" % (pname, got[0], got[1], want[0], want[1]))

    # ── 包围盒 ──
    for pname in sorted(want_parts & got_parts):
        bd = G.bounds_of(parts[pname])
        mn, mx = bd["min"], bd["max"]
        got = rec["bounds"].get(pname)
        if got is None:
            bounds_bad.append("%s missing" % pname)
            continue
        for i in range(3):
            if not (close(got[0][i], mn[i]) and close(got[1][i], mx[i])):
                bounds_bad.append("%s[%d] cs=%s..%s py=%.4f..%.4f" %
                                  (pname, i, got[0][i], got[1][i], mn[i], mx[i]))
    if set(rec["bounds"]) != want_parts:
        bounds_bad.append("bounds part set cs=%s py=%s" %
                          (sorted(rec["bounds"]), sorted(want_parts)))

    # ── 报告整行文本（4 位小数也对齐 ⇒ 比数值容差更严）──
    want_m = G.report_meshes_line(ident, boss, s, parts)
    if rec["meshes_text"] != want_m:
        text_bad.append("meshes= %s" % _first_token_diff(rec["meshes_text"], want_m))
    want_b = G.report_bounds_line(ident, boss, parts)
    if rec["bounds_text"] != want_b:
        text_bad.append("bounds= %s" % _first_token_diff(rec["bounds_text"], want_b))

    return {"shape": shape_bad, "counts": count_bad, "bounds": bounds_bad, "text": text_bad}


def _first_token_diff(got, want):
    if got is None:
        return "报告里没有这一行"
    a, b = (got or "").split(), (want or "").split()
    for i in range(max(len(a), len(b))):
        ga = a[i] if i < len(a) else "<缺>"
        wb = b[i] if i < len(b) else "<多>"
        if ga != wb:
            return "token[%d] cs=%s py=%s" % (i, ga, wb)
    return "一致"


def all_diffs(rep, shape_fn=None):
    """对 5 个 id 各跑一遍比对；`shape_fn` 可换成被扰动的版本（自检用）。"""
    shape_fn = shape_fn or G.shape_of
    out = {}
    for ident in G.TYPE_IDS:
        boss = ident == G.BOSS_ID
        if ident not in rep:
            out[ident] = {"shape": ["报告里没有这个 id"], "counts": [], "bounds": [], "text": []}
            continue
        s = shape_fn(ident, boss)
        parts = G.build_parts(s)
        out[ident] = compare_type(ident, boss, s, parts, rep[ident])
    return out


def verify_runtime(rep, report_path):
    if not rep:
        return check("runtime report parses", False, "no [TYPE] blocks in %s" % report_path)
    check("runtime report has 5 types", len(rep) == 5, ",".join(sorted(rep)))
    diffs = all_diffs(rep)
    labels = (("shape", "shape params"), ("counts", "part set + counts"),
              ("bounds", "bounds"), ("text", "report line text"))
    ok = True
    for ident in G.TYPE_IDS:
        d = diffs[ident]
        for aspect, label in labels:
            items = d[aspect]
            if items:
                ok = False
                for it in items:
                    FULL_FAILURES.append("%s %s: %s" % (ident, label, it))
            check("%s %s" % (ident, label), not items,
                  "; ".join(items[:4]) if items else "== 离线副本")
    return check("runtime == offline copy (all fields)", ok, "tol %.0e" % TOL)


# ══════════════════════════════════════════════════════════════════════════
# ⑥ BOSS 段：与备份**逐字相同**（v191 重做 4 种昆虫时，BOSS 一个字节都不许动）
# ══════════════════════════════════════════════════════════════════════════
def boss_block_bytes(raw):
    """从报告字节里取出 `[TYPE] boss` 那 10 行（到空行为止）。"""
    m = re.search(rb"\[TYPE\] boss\n", raw)
    if not m:
        return None
    start = m.start()
    end = raw.find(b"\n\n", start)
    if end < 0:
        end = len(raw) - 1
    return raw[start:end + 1]


def verify_boss_frozen(raw, report_path):
    if not os.path.exists(BOSS_FROZEN):
        return check("boss_frozen.txt exists", False, BOSS_FROZEN)
    with open(BOSS_FROZEN, "rb") as f:
        frozen = f.read()
    now = boss_block_bytes(raw)
    if now is None:
        return check("report has [TYPE] boss", False, report_path)
    check("boss block == 备份逐字（sha256）", now == frozen,
          "sha256=%s" % hashlib.sha256(now).hexdigest()[:16])
    return True


# ══════════════════════════════════════════════════════════════════════════
# ⑦ 腿相位（运行时真的在摆腿）
# ══════════════════════════════════════════════════════════════════════════
def verify_leg_phase(text):
    """
    报告里的腿相位段：六条腿在多个时刻上必须
      ① 真的在动（相邻两次采样，六条腿的角度都变了）
      ② 摆幅够大（有一个时刻极差 >= 30° —— 说明不是"小幅抖动"）
      ③ 是**交替**的（一对腿里一条向前、一条向后 ⇒ 三角步态，不是六条一起摆）
    ★ 为什么不是"每个时刻极差都 >= 30" ★
      相位 0 正好是**过零点**：六条腿同时经过中位（这是步态本身的性质，不是缺陷）。
      在那里要求大极差会把**正确**的实现判成 FAIL ✗ —— 判据要跟着物理走。
    """
    rows = re.findall(r"t=([\d.]+)s\s+(L0=[-\d.]+(?: L\d=[-\d.]+){5})", text)
    if len(rows) < 2:
        return check("leg phase evidence", False, "no LEG-PHASE rows")
    ok = True
    detail = []
    samples = []
    for t, row in rows:
        vals = [float(v.split("=")[1]) for v in row.split()]
        samples.append(vals)
        detail.append("t=%s spread=%.1f" % (t, max(vals) - min(vals)))

    moved = sum(1 for a, b in zip(samples[0], samples[1]) if abs(a - b) > 1.0)
    if moved < 6:
        ok = False
        detail.append("%d/6 legs moved" % moved)
    else:
        detail.append("6/6 legs moved")

    big = [s for s in samples if max(s) - min(s) >= 30.0]
    if not big:
        ok = False
        detail.append("no sample reaches 30deg spread")
    else:
        detail.append("%d/%d samples spread>=30" % (len(big), len(samples)))

    pairs_ok = 0
    pairs_all = 0
    for s in big:
        for a, b in ((0, 1), (2, 3), (4, 5)):
            pairs_all += 1
            if (s[a] > 0.0) != (s[b] > 0.0):
                pairs_ok += 1
    detail.append("alternation %d/%d" % (pairs_ok, pairs_all))
    if pairs_all and pairs_ok * 2 < pairs_all:
        ok = False
    return check("legs actually swing (motion + amplitude + alternation)", ok, " | ".join(detail))


# ══════════════════════════════════════════════════════════════════════════
# ⑧ 判别力自检：**故意扰动离线副本**，比对必须抓到（不改任何文件）
# ══════════════════════════════════════════════════════════════════════════
def _mutated(key, factor=None, flip=False):
    def fn(ident, boss):
        s = G.shape_of(ident, boss)
        if flip:
            s[key] = 8 if s[key] == 6 else 6
        else:
            s[key] = s[key] * factor
        return s
    return fn


def selftest(rep):
    print("SELFTEST —— 故意改掉离线副本的一个参数，比对必须 FAIL")
    print("-" * 78)
    if not rep:
        print("FAIL  没有运行时报告，无法自检")
        return 1
    base = all_diffs(rep)
    if any(v for d in base.values() for v in d.values()):
        print("NOTE  本体校验已经不干净（先跑一次普通校验看 FAIL 清单）")

    cases = [
        # (标签, 扰动后的 shape 函数, 必须报错的方面, 必须被抓到的 id)
        ("ThoraxR * 1.02", _mutated("ThoraxR", 1.02), ("bounds", "text"), "grunt"),
        ("Sides 6<->8", _mutated("Sides", flip=True), ("counts", "text"), "boss"),
        ("DomeR * 1.02", _mutated("DomeR", 1.02), ("bounds", "text"), "pig"),
    ]
    ok = True
    for label, fn, aspects, must in cases:
        d = all_diffs(rep, fn)
        hit = sorted({ident for ident, dd in d.items()
                      for a in aspects if dd[a]})
        if hit and must in hit:
            print("PASS  扰动 %-16s 被抓到（%s）" % (label, ",".join(hit)))
        else:
            ok = False
            print("FAIL  扰动 %-16s 没被抓到（期望含 %s，实得 %s）⇒ 比对是空转的 ✗"
                  % (label, must, ",".join(hit) or "无"))
    print("-" * 78)
    print("SELFTEST %s" % ("PASS —— 判据有判别力" if ok else "FAIL —— 判据没有判别力"))
    return 0 if ok else 1


# ══════════════════════════════════════════════════════════════════════════
def main(argv):
    if "--selftest" in argv:
        path = _arg_value(argv, "--report") or RUNTIME_REPORT
        text = load_text(path)
        return selftest(parse_report(text) if text else {})

    report_path = _arg_value(argv, "--report") or RUNTIME_REPORT
    print("BUG-MESH VERIFY —— runtime vs offline authoritative copy (v191)")
    print("source : %s" % os.path.relpath(CS_SOURCE, PROJECT))
    print("report : %s" % (report_path if os.path.exists(report_path) else "(missing)"))
    print("-" * 78)

    man = None
    if os.path.exists(MANIFEST):
        with open(MANIFEST, "r", encoding="utf-8") as f:
            man = json.load(f)

    section("离线副本自洽")
    verify_manifest()
    section("几何健康")
    verify_geometry()
    section("OBJ 落盘")
    verify_obj()
    section("源码静态约束 + 参数钉")
    verify_source(man)

    text = load_text(report_path)
    raw = load_bytes(report_path)
    section("运行时报告")
    if not text:
        check("runtime report exists", False,
              "找 BugShowcase.Report() 跑一次（play mode）")
        rep = {}
    else:
        rep = parse_report(text)
    verify_runtime(rep, report_path)
    section("BOSS 段冻结")
    verify_boss_frozen(raw or b"", report_path)
    section("腿相位")
    verify_leg_phase(text or "")

    print("-" * 78)
    passed = sum(1 for _, ok, _ in results if ok)
    total = len(results)
    print("%d/%d PASS" % (passed, total))
    if passed != total:
        print("FAILURES:")
        for name, ok, detail in results:
            if not ok:
                print("  - %s  (%s)" % (name, detail))
        if FULL_FAILURES:
            print("\n运行时 vs 离线副本 的全部差异（%d 条）：" % len(FULL_FAILURES))
            for line in FULL_FAILURES:
                print("  · %s" % line)
        return 1
    print("ALL PASS —— 运行时、离线副本、OBJ、C# 参数四方一致")
    return 0


def _arg_value(argv, flag):
    if flag in argv:
        i = argv.index(flag)
        if i + 1 < len(argv):
            return argv[i + 1]
    return None


def load_text(path):
    if not path or not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def load_bytes(path):
    if not path or not os.path.exists(path):
        return None
    with open(path, "rb") as f:
        return f.read().replace(b"\r\n", b"\n")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
