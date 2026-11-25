"""v179 交叉核对：把 `WorldView.cs` 里的**真实表**读出来，重建一遍网格，
再与 `gen_fx_meshes.py` 产出的 OBJ 比顶点数 / 三角数。

它挡的是这样一类事故：
    有人改了 `WorldView.cs` 的八面体面表 / 拉伸表 / 二十面体表，
    却没有同步 `Tools/mesh_gen/`，或者反过来
    ⇒ 两边静默分叉，"预览图"从此不再是那个网格。

另外静态检查两条硬约束（都是规格明写的）：
    ① 三种攻击的代码路径里**不许出现 `new Material(`**
       —— 第⑤轮修掉的是 ~60 份/秒 的材质泄漏，必须全部走 `VfxMatCached`
    ② 三种特效必须是**实体网格**，不许有 billboard / Quad 精灵

重跑：
    C:\\Users\\18462\\.dsh\\dsh-runtimes\\dsh-primary-runtime\\dependencies\\python\\python.exe \\
        Tools/mesh_gen/verify_counts.py
退出码 0 = 全 PASS，1 = 有 FAIL（可直接进 CI）。
"""

import json
import math
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.dirname(os.path.dirname(HERE))
CS = os.path.join(PROJ, "Assets", "Scripts", "Bootstrap", "WorldView.cs")
OUT = os.path.join(HERE, "out")

results = []


def check(name, ok, detail=""):
    results.append((name, ok, detail))
    print("  %s  %-46s %s" % ("PASS" if ok else "FAIL", name, detail))
    return ok


# ─────────────────────────────────────────────────────────────
# 0) 读源码
# ─────────────────────────────────────────────────────────────
if not os.path.isfile(CS):
    print("找不到 %s" % CS)
    sys.exit(1)

with open(CS, "r", encoding="utf-8") as f:
    src = f.read()

if not os.path.isdir(OUT):
    print("找不到 %s —— 先跑 gen_fx_meshes.py" % OUT)
    sys.exit(1)

with open(os.path.join(OUT, "fx_mesh_manifest.json"), "r", encoding="utf-8") as f:
    manifest = json.load(f)


def ints(text):
    return [int(x) for x in re.findall(r"-?\d+", text)]


def floats3(text):
    return [(float(a), float(b), float(c)) for a, b, c in
            re.findall(r"new Vector3\(\s*(-?[\d.]+)f?\s*,\s*(-?[\d.]+)f?\s*,\s*(-?[\d.]+)f?\s*\)",
                       text)]


# ─────────────────────────────────────────────────────────────
# 1) 从 C# 抽出四族的定义
# ─────────────────────────────────────────────────────────────
m = re.search(r"int\[\]\s*f\s*=\s*\{([^}]*)\}", src, re.S)
cs_shard_faces = ints(m.group(1)) if m else []

m = re.search(r"Vector3\[\]\s*stretch\s*=\s*\{(.*?)\};", src, re.S)
cs_stretch = floats3(m.group(1)) if m else []

m = re.search(r"var\s+idx\s*=\s*new\s+List<int>\s*\{(.*?)\};", src, re.S)
cs_ico_idx = ints(m.group(1)) if m else []

m = re.search(r"FxFrustumMesh\([^,]+,\s*(\d+)\s*,\s*(-?[\d.]+)f\s*,\s*(-?[\d.]+)f\s*\)", src)
cs_fr_sides = int(m.group(1)) if m else -1
cs_fr_ra = float(m.group(2)) if m else -1.0
cs_fr_rb = float(m.group(3)) if m else -1.0

m = re.search(r"FxDiscMesh\(\s*\d+\s*,\s*(\d+)\s*\)", src)
cs_disc_segs = int(m.group(1)) if m else -1

m = re.search(r"FxIcoShellMesh\((\d+)\)", src)
cs_ico_subdiv = int(m.group(1)) if m else -1


# ─────────────────────────────────────────────────────────────
# 2) 用**从 C# 读出来的**表重建（不是用 Python 自己的表）
# ─────────────────────────────────────────────────────────────
def norm(v):
    m2 = math.sqrt(v[0] ** 2 + v[1] ** 2 + v[2] ** 2)
    return (0.0, 0.0, 0.0) if m2 == 0 else (v[0] / m2, v[1] / m2, v[2] / m2)


def lcg(seed):
    seed = (seed * 1664525 + 1013904223) & 0xFFFFFFFF
    return seed, ((seed >> 9) & 1023) / 1023.0 - 0.5


def rebuild_shard(variant):
    poles = [(1.0, 0, 0), (-1.0, 0, 0), (0, 1.0, 0), (0, -1.0, 0),
             (0, 0, 1.0), (0, 0, -1.0)]
    st = cs_stretch[variant % len(cs_stretch)]
    seed = (0x9E3779B9 * (variant + 1) + 12345) & 0xFFFFFFFF
    out = []
    for p in poles:
        seed, jx = lcg(seed)
        seed, jy = lcg(seed)
        seed, jz = lcg(seed)
        out.append(((p[0] + jx * 0.26) * st[0],
                    (p[1] + jy * 0.26) * st[1],
                    (p[2] + jz * 0.26) * st[2]))
    return len(cs_shard_faces) // 3, len(cs_shard_faces)   # 三角数 / 顶点数（flat）


def rebuild_ico(subdiv):
    v = [norm(p) for p in [
        (-1, (1 + 5 ** 0.5) / 2, 0), (1, (1 + 5 ** 0.5) / 2, 0),
        (-1, -(1 + 5 ** 0.5) / 2, 0), (1, -(1 + 5 ** 0.5) / 2, 0),
        (0, -1, (1 + 5 ** 0.5) / 2), (0, 1, (1 + 5 ** 0.5) / 2),
        (0, -1, -(1 + 5 ** 0.5) / 2), (0, 1, -(1 + 5 ** 0.5) / 2),
        ((1 + 5 ** 0.5) / 2, 0, -1), ((1 + 5 ** 0.5) / 2, 0, 1),
        (-(1 + 5 ** 0.5) / 2, 0, -1), (-(1 + 5 ** 0.5) / 2, 0, 1),
    ]]
    idx = list(cs_ico_idx)
    for _ in range(max(1, subdiv)):
        mids = {}

        def mid(i, j):
            k = (i, j) if i < j else (j, i)
            if k in mids:
                return mids[k]
            v.append(norm(((v[i][0] + v[j][0]) * 0.5,
                           (v[i][1] + v[j][1]) * 0.5,
                           (v[i][2] + v[j][2]) * 0.5)))
            mids[k] = len(v) - 1
            return mids[k]

        nxt = []
        for i in range(0, len(idx), 3):
            a, b, c = idx[i], idx[i + 1], idx[i + 2]
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            nxt += [a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca]
        idx = nxt
    return len(idx) // 3, len(idx)


def count_obj(path):
    """数 OBJ 里的 v 与 f 行。"""
    nv = nf = 0
    with open(path, "r", encoding="ascii") as f:
        for line in f:
            if line.startswith("v "):
                nv += 1
            elif line.startswith("f "):
                nf += 1
    return nv, nf


# ─────────────────────────────────────────────────────────────
# 3) 逐族比对
# ─────────────────────────────────────────────────────────────
print("=" * 78)
print("v179 网格交叉核对 —— WorldView.cs  <=>  Tools/mesh_gen/out/")
print("=" * 78)

# 3a 碎片
exp_v = manifest["expected"]["shard"]["verts"]
exp_t = manifest["expected"]["shard"]["tris"]
cs_t, cs_v = rebuild_shard(0)
obj_v, obj_t = count_obj(os.path.join(OUT, "shard0.obj"))
check("shard 面表：C# 8 面 / 24 索引",
      len(cs_shard_faces) == 24 and cs_t == 8, "C# 读到 %d 索引 → %d 三角" % (len(cs_shard_faces), cs_t))
check("shard 拉伸表：6 个变体",
      len(cs_stretch) == 6, "C# 读到 %d 组 stretch" % len(cs_stretch))
check("shard 顶点/三角数：C# 重建 == OBJ",
      (cs_v, cs_t) == (obj_v, obj_t), "C#(%d,%d) vs OBJ(%d,%d)" % (cs_v, cs_t, obj_v, obj_t))
check("shard 顶点/三角数 == 规格期望",
      (obj_v, obj_t) == (exp_v, exp_t), "期望(%d,%d) 实得(%d,%d)" % (exp_v, exp_t, obj_v, obj_t))
check("shard 6 个变体都能生成",
      len(manifest["families"]["shard"]["variants"]) == 6,
      "变体数 %d" % len(manifest["families"]["shard"]["variants"]))
outs = [v["outward"] for v in manifest["families"]["shard"]["variants"]]
check("shard 全部面朝外（绕序正确）", min(outs) >= 0.999,
      "最低朝外率 %.3f" % min(outs))

# 3b 棱台
ft = cs_fr_sides * 4
fv = cs_fr_sides * 12
obj_v, obj_t = count_obj(os.path.join(OUT, "frustum6.obj"))
check("frustum 参数：C# == Python 表",
      (cs_fr_sides, cs_fr_ra, cs_fr_rb) == (6, 0.72, 1.0),
      "C# 读到 sides=%d ra=%.2f rb=%.2f" % (cs_fr_sides, cs_fr_ra, cs_fr_rb))
check("frustum 顶点/三角数 == 规格期望",
      (obj_v, obj_t) == (fv, ft), "期望(%d,%d) 实得(%d,%d)" % (fv, ft, obj_v, obj_t))
check("frustum 全部面朝外", manifest["families"]["frustum"]["outward"] >= 0.999,
      "朝外率 %.3f" % manifest["families"]["frustum"]["outward"])

# 3c 球壳
it, iv = rebuild_ico(cs_ico_subdiv)
obj_v, obj_t = count_obj(os.path.join(OUT, "icoshell1.obj"))
check("icoshell 细分级数：C# 调用 == 1", cs_ico_subdiv == 1, "C# 读到 subdiv=%d" % cs_ico_subdiv)
check("icoshell 表：C# 60 索引（20 面）", len(cs_ico_idx) == 60,
      "C# 读到 %d 索引" % len(cs_ico_idx))
check("icoshell 顶点/三角数：C# 重建 == OBJ",
      (iv, it) == (obj_v, obj_t), "C#(%d,%d) vs OBJ(%d,%d)" % (iv, it, obj_v, obj_t))
check("icoshell 一级细分 == 80 三角（规格）", it == 80, "实得 %d 三角" % it)
check("icoshell 全部面朝外", manifest["families"]["icoshell"]["outward"] >= 0.999,
      "朝外率 %.3f" % manifest["families"]["icoshell"]["outward"])

# 3d 湿痕圆盘
obj_v, obj_t = count_obj(os.path.join(OUT, "disc18.obj"))
check("disc 段数：C# 调用 == 18", cs_disc_segs == 18, "C# 读到 segs=%d" % cs_disc_segs)
check("disc 顶点/三角数 == 期望",
      (obj_v, obj_t) == (cs_disc_segs * 3, cs_disc_segs),
      "期望(%d,%d) 实得(%d,%d)" % (cs_disc_segs * 3, cs_disc_segs, obj_v, obj_t))
check("disc 法线朝 +Y（贴地看得见）",
      manifest["families"]["disc"]["normal_up"] >= 0.999,
      "首面法线 y=%.3f" % manifest["families"]["disc"]["normal_up"])


# ─────────────────────────────────────────────────────────────
# 4) 静态约束：不许 new Material / 不许 billboard
# ─────────────────────────────────────────────────────────────
def func_range(sig):
    """返回函数体的 [起始行, 结束行]（按大括号配平）。"""
    i = src.find(sig)
    if i < 0:
        return None
    j = src.find("{", i)
    if j < 0:
        return None
    depth = 0
    k = j
    while k < len(src):
        if src[k] == "{":
            depth += 1
        elif src[k] == "}":
            depth -= 1
            if depth == 0:
                break
        k += 1
    return (src.count("\n", 0, i) + 1, src.count("\n", 0, k) + 1)


WATCH = [
    "private void OnMeleeSwing(",
    "private void OnBeam(",
    "private void OnFertBlast(",
    "private void TickFxMeshes(",
    "private void TickLightRamps(",
    "private void IlluminateRamp(",
    "private static Mesh FxShardMesh(",
    "private static Mesh FxFrustumMesh(",
    "private static Mesh FxIcoShellMesh(",
    "private static Mesh FxDiscMesh(",
]
lines = src.split("\n")
ranges = [(s, func_range(s)) for s in WATCH]

bad_mat = []
bad_quad = []
for sig, rng in ranges:
    if rng is None:
        bad_mat.append((sig, "函数没找到"))
        continue
    a, b = rng
    for ln in range(a, min(b, len(lines))):
        txt = lines[ln]
        if "new Material(" in txt:
            bad_mat.append((sig, "第 %d 行" % (ln + 1)))
        if re.search(r"PrimitiveType\.Quad|Sprite|Billboard", txt):
            bad_quad.append((sig, "第 %d 行" % (ln + 1)))

check("三种攻击的代码路径里**没有** new Material(", not bad_mat,
      "干净 ✓" if not bad_mat else str(bad_mat[:3]))
check("三种攻击里没有 Quad / Sprite / billboard", not bad_quad,
      "干净 ✓" if not bad_quad else str(bad_quad[:3]))

# `VfxMatCached` 的使用次数（≥ 三种特效各至少一处）
cached_uses = len(re.findall(r"VfxMatCached\(", src))
check("走 VfxMatCached 的调用点 ≥ 10 处", cached_uses >= 10, "实际 %d 处" % cached_uses)

ok_all = all(ok for _, ok, _ in results)
print("-" * 78)
print("%d/%d PASS" % (sum(1 for _, ok, _ in results if ok), len(results)))
print("结论：%s" % ("全部通过 ✓" if ok_all else "有 FAIL ✗"))
sys.exit(0 if ok_all else 1)
