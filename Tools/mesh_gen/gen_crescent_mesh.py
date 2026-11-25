#!/usr/bin/env python3
r"""gen_crescent_mesh.py —— v185「弯月剑气」网格的**离线同源副本**。
为什么要有这个文件
------------------
运行时构造在 `Assets/Scripts/Bootstrap/WorldView.cs` 的 `FxCrescentMesh()`。
那份代码是**唯一能被 Unity 编译**的地方（`Tools/` 在 `Assets/` 之外，
`Seedfall.Runtime.csproj` 用的是显式 `<Compile Include="Assets\...">`）。

但"能编译"不等于"能被核对"：`segs / 半张角 / 厚度 / 幂次` 写在 C# 里，
改一个数字没有任何东西会失败。所以这个脚本按**同一套参数**重建同样的拓扑，
落成 OBJ 预览 + 清单，并在 `--check` 下与 C# 侧钉住的常量**逐项比对**。

约定（与 `gen_fx_meshes.py` / `gen_v180_fx_meshes.py` 同一套）
-------------------------------------------------------------
· 全部 **flat shading**：每个三角形独占 3 个顶点 ⇒ `顶点数 = 三角数 × 3`
· 全部 **双面**：每个四边形正反各发两遍 ⇒ `三角数 = 四边形数 × 4`

弯月的形状（为什么不是"把刃形转 90°"）
--------------------------------------
`FxBladeMesh`（v180）的径向厚度**处处相同**（inner 0.62 / outer 1.0 常数）
⇒ 俯视是一条**等宽弧带、两端平头**，像"环的一截"，不是月亮。
弯月的定义性特征是**两端收尖、中段最厚**，所以这里内缘随角度外扩：

    c(θ)      = cos(|θ|/halfArc · π/2)
    taper(θ)  = 0                    if not (c(θ) > 0)     # ★ v187：NaN / 0 / 负数 ⇒ 刃尖
              = c(θ) ** 0.45         otherwise
    inner(θ)  = 1 - THICKNESS · taper(θ)
    hy(θ)     = max(HY_FLOOR, HALF_THICK_Y · taper(θ))

θ = 0 时 inner = 1 - 0.46 = 0.54（中段最厚），θ = ±55° 时 inner = 1、hy = HY_FLOOR（刃尖）。

★ v187 为什么必须有那道 `if not (c > 0)` ★ ─────────────────────────────────
`Mathf.Cos(1f * Mathf.PI * 0.5f)` 在 **float32** 下**不是 0**，而是 **-4.371139e-08**
（`Mathf.PI * 0.5f` = 1.5707964f，比真正的 π/2 略大 ⇒ 余弦落到负侧）。
而 `Mathf.Pow(负数, 0.45f)` = **NaN**（负底数 + 非整数指数）。
`u = 1` 正好是**两端刃尖** ⇒ 那两处顶点变 NaN ⇒ Unity 报
`Mesh 'FxCrescent': abnormal mesh bounds … (-nan(ind) …)` 并**每帧**刷
`Invalid worldAABB`（用户报的"剑气直接报错"）。

⚠️ 本脚本在**改前**抓不到这个 bug，原因值得记住：
Python 的 `math` 是 **double**，`math.cos(math.pi/2)` = +6.1e-17（正数！）⇒ 永远不 NaN；
再加上当时写的 `max(cos, 0.0)` 更把这层差异盖住了 ⇒ 脚本 `--check` 一直 PASS，
而真正的游戏在 float32 下崩。**离线副本不能只看 double** ——
所以 `--check` 现在会：(a) 用 `struct` 把 float32 语义算出来并断言 `cos(π/2) < 0`；
(b) 断言 `taper(±half_deg) == 0.0` 且**不是 NaN**；(c) 扫全部顶点有无 NaN/∞；
(d) **回到 `WorldView.cs` 里核对常量与那道 guard** —— 同源是**被检查**的，不是被声明的。

用法
----
    python Tools/mesh_gen/gen_crescent_mesh.py          # 生成 out/fx_crescent.obj + 清单
    python Tools/mesh_gen/gen_crescent_mesh.py --check   # 只核对常量，不写文件
"""

import argparse
import json
import math
import os
import re
import struct
import sys

# Windows 控制台默认是 GBK，`⇒` / `×` 这些符号会直接抛 UnicodeEncodeError 把脚本打断。
# 统一按 UTF-8 输出：终端可能显示成乱码，但**不会**因为一个符号让生成脚本失败。
try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

# ── 与 WorldView.cs 逐字对应的参数（改一边必须改另一边）──────────────────────
CRESCENT = {
    "segs": 24,             # WorldView.CrescentSegs        = 24
    "half_deg": 55.0,       # WorldView.CrescentHalfDeg     = 55f
    "thickness": 0.46,      # WorldView.CrescentThickness   = 0.46f
    "half_thick_y": 0.035,  # WorldView.CrescentHalfThickY  = 0.035f
    "taper_pow": 0.45,      # WorldView.CrescentTaper: Mathf.Pow(cos, 0.45f)
    "outer": 1.0,           # CrescentSection: const float outer = 1f
    "outer_thin": 0.30,     # CrescentSection: hy * 0.30f（外缘更薄 ⇒ 刃背）
    "hy_floor": 0.0008,     # CrescentSection: Mathf.Max(0.0008f, ...)
    "quads_per_seg": 4,     # 上面 / 下面 / 内缘 / 外缘
}


def f32(x):
    """把 double 舍入到 float32 —— C# 的 `Mathf.*` 全在 float32 上算，Python 默认 double。

    ★ 为什么整个脚本都要过这一层（v187 的教训）★
    改前本脚本只用 double：`math.cos(math.pi/2)` = **+6.1e-17**（正数），
    而 C# 的 `Mathf.Cos(1.5707964f)` = **-4.37e-08**（负数）✗
    符号都不一样 ⇒ 脚本永远算不出那个 NaN ⇒ `--check` 一直 PASS，游戏却每帧报错 ✓
    所以"同源"必须**连精度一起同源**，数字才对得上。
    """
    return struct.unpack("f", struct.pack("f", float(x)))[0]


# `Mathf.Deg2Rad` = (float)(Math.PI / 180) —— 与 WorldView 里用的同一个常量
DEG2RAD = f32(math.pi / 180.0)


def finite(x):
    """实数且有限。**故意把"不是实数"也算坏** —— 见下：

    Python 的 `负数 ** 0.45` 不抛异常，而是返回**复数**（实测 `(-4.37e-08) ** 0.45`
    = `7.6e-05+4.8e-04j`）⇒ 它既不是 NaN 也不是 inf，`math.isfinite` 会直接抛 TypeError ✗
    而 C# 的 `Math.Pow(负数, 0.45)` 返回 **NaN** ⇒ 两边"坏"的形式都不一样。
    所以判据必须比"isfinite"更宽一层：**不是有限实数**就算坏 ✓
    """
    try:
        return math.isfinite(x)
    except TypeError:
        return False


def cos_half_pi_f32():
    """C# 里 `Mathf.Cos(1f * Mathf.PI * 0.5f)` 的**实际值**（按 float32 语义算）。

    `Mathf.PI` 是 `(float)Math.PI` = 3.1415927f，乘 0.5f ⇒ 1.5707964f，
    而 `Mathf.Cos(x)` 是 `(float)Math.Cos((double)x)` ⇒ 对 1.5707964f 取余弦 = -4.371139e-08。
    """
    half = f32(f32(math.pi) * 0.5)
    return f32(math.cos(half)), half


def seg_angle(i):
    """采样角 `a_i = Deg2Rad × Lerp(-half, +half, i/segs)` —— 与 C# 同一串 float32 运算。

    ★ 端点必须**逐位**等于 `half_deg × Deg2Rad` ★：`taper` 里判 `u == 1` 靠的就是这个，
      只要两边少一步 float32 舍入，`u` 就会变成 0.99999994 或 1.0000001 ⇒
      刃尖不再是精确 0，`if not (c > 0)` 也就不会在端点命中 ✓
    """
    c = CRESCENT
    t = f32(i / float(c["segs"]))
    lerp = f32(-c["half_deg"] + f32(2.0 * c["half_deg"]) * t)
    return f32(DEG2RAD * lerp)


def taper(angle_rad):
    """与 C# `CrescentTaper` 同一个式子（**含 v187 的 NaN guard**），全程 float32。

    ★ 改前的写法是 `max(cos, 0.0) ** 0.45`（double）★
      Python 里 `max(nan, 0.0)` 会**返回 nan**（`0.0 > nan` 为假）⇒ 它与 C# 的
      `Mathf.Max(0f, NaN)` 一样**挡不住 NaN** ✗
      而 double 下 `cos(π/2) > 0`，连"会不会 NaN"这个问题都不会出现 ✓
    现在逐字照抄 C#：`if not (c > 0.0): return 0.0`
      —— 一个条件同时挡掉 **NaN / 0 / 负数**，与 `WorldView.cs` 那道 guard 一一对应 ✓
    """
    c = CRESCENT
    u = f32(abs(f32(angle_rad)) / f32(c["half_deg"] * DEG2RAD))
    if u > 1.0:
        u = 1.0
    v = f32(math.cos(f32(u * f32(math.pi * 0.5))))
    if not (v > 0.0):          # ← 与 C# `if (!(c > 0f)) return 0f;` 同一行（NaN 也走这里）
        return 0.0
    return f32(math.pow(v, f32(c["taper_pow"])))


def section(angle_rad):
    """一个角度上的 4 点截面：内上 / 外上 / 外下 / 内下（与 `CrescentSection` 同序、同精度）。"""
    c = CRESCENT
    t = taper(angle_rad)
    outer = f32(c["outer"])
    inner = f32(outer - f32(c["thickness"] * t))
    hy = max(f32(c["hy_floor"]), f32(c["half_thick_y"] * t))
    s, co = f32(math.sin(angle_rad)), f32(math.cos(angle_rad))
    return [
        (f32(s * inner), hy, f32(co * inner)),
        (f32(s * outer), f32(hy * c["outer_thin"]), f32(co * outer)),
        (f32(s * outer), -f32(hy * c["outer_thin"]), f32(co * outer)),
        (f32(s * inner), -hy, f32(co * inner)),
    ]


def quad(tris, a, b, c_, d):
    """一个四边形 ⇒ 2 个三角 × 正反两遍（与 C# `Quad` 同序）。"""
    tris.append((a, b, c_)); tris.append((a, c_, d))
    tris.append((a, c_, b)); tris.append((a, d, c_))


def crescent_tris():
    tris = []
    for i in range(CRESCENT["segs"]):
        # 与 C# `Mathf.Lerp(-half, +half, i/(float)segs)` 同一串（见 `seg_angle`）
        a0, a1 = seg_angle(i), seg_angle(i + 1)
        p0, p1 = section(a0), section(a1)
        quad(tris, p0[0], p1[0], p1[1], p0[1])   # 上面（内缘 → 外缘）
        quad(tris, p0[3], p1[3], p1[2], p0[2])   # 下面
        quad(tris, p0[0], p1[0], p1[3], p0[3])   # 内缘（凹面）
        quad(tris, p0[1], p1[1], p1[2], p0[2])   # 外缘（凸的刃背）
    return tris


def counts():
    c = CRESCENT
    quads = c["segs"] * c["quads_per_seg"]
    tris = quads * 4                 # 双面
    verts = tris * 3                 # flat shading：每面独享 3 个顶点
    return quads, tris, verts


def write_obj(tris):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, "fx_crescent.obj")
    with open(path, "w", encoding="utf-8") as f:
        f.write("# FxCrescent —— v185 弯月剑气（外径归一 1.0，局部 +Z = 飞行朝向）\n")
        f.write("# 顶点/三角数与 WorldView.FxCrescentMesh 逐字对应，见同目录 manifest\n")
        n = 0
        for tri in tris:
            for v in tri:
                n += 1
                f.write("v %.6f %.6f %.6f\n" % v)
        for t in range(len(tris)):
            a, b, c_ = 3 * t + 1, 3 * t + 2, 3 * t + 3
            f.write("f %d %d %d\n" % (a, b, c_))
    return path


CS_SOURCE = os.path.normpath(os.path.join(
    HERE, "..", "..", "Assets", "Scripts", "Bootstrap", "WorldView.cs"))


def cs_constants():
    """**回 C# 里核对**：把 `WorldView.cs` 里钉住的常量与那道 guard 读出来。

    返回 (dict 或 None, 说明)。读不到文件时返回 (None, 原因) —— 不静默 PASS。
    ⚠️ 只在本文件**对应的小函数体**里搜，不在整份 4000 行里搜：
        `hy * 0.30f` 这种式子别处也可能出现，搜全文会"对上别人家的常量"✗
    """
    try:
        with open(CS_SOURCE, "r", encoding="utf-8") as f:
            src = f.read()
    except OSError as err:
        return None, "读不到 %s（%s）" % (CS_SOURCE, err)

    def region(start_pat, end_pat):
        a = src.find(start_pat)
        if a < 0:
            return ""
        b = src.find(end_pat, a + len(start_pat))
        return src[a: b if b > 0 else a + 4000]

    body_taper = region("private static float CrescentTaper", "private static Mesh FxCrescentMesh")
    body_sect = region("private static Vector3[] CrescentSection", "private static void AddTri")

    def grab(body, pat):
        m = re.search(pat, body)
        return float(m.group(1)) if m else None

    got = {
        # 常量区（private const）在文件头部的弯月常量块里，全文唯一
        "segs":         grab(src, r"CrescentSegs\s*=\s*(\d+)"),
        "half_deg":     grab(src, r"CrescentHalfDeg\s*=\s*([0-9.]+)f"),
        "thickness":    grab(src, r"CrescentThickness\s*=\s*([0-9.]+)f"),
        "half_thick_y": grab(src, r"CrescentHalfThickY\s*=\s*([0-9.]+)f"),
        # 曲线与截面：只在各自函数体里搜
        "taper_pow":    grab(body_taper, r"Mathf\.Pow\(\s*c\s*,\s*([0-9.]+)f\s*\)"),
        "hy_floor":     grab(body_sect, r"Mathf\.Max\(\s*([0-9.]+)f\s*,\s*CrescentHalfThickY"),
        "outer_thin":   grab(body_sect, r"hy\s*\*\s*([0-9.]+)f"),
        "outer":        grab(body_sect, r"float\s+outer\s*=\s*([0-9.]+)f"),
    }
    # ★ v187 的那道闸：`if (!(c > 0f)) return 0f;`（字面比对，改一个字就红）
    got["nan_guard"] = 1.0 if "!(c > 0f)" in body_taper else 0.0
    return got, ""


def check():
    ok = True
    quads, tris, verts = counts()
    print("FxCrescent 参数：segs=%d 半张角=%.1f° 厚度=%.2f 半厚Y=%.3f 幂次=%.2f"
          % (CRESCENT["segs"], CRESCENT["half_deg"], CRESCENT["thickness"],
             CRESCENT["half_thick_y"], CRESCENT["taper_pow"]))
    print("  ⇒ 四边形 %d ／ 三角 %d ／ 顶点 %d（flat + 双面）" % (quads, tris, verts))

    # 形状自检：中段必须最厚、两端必须收成刃尖
    # ★ 用 `seg_angle(i)` 而不是 `math.radians(±55)`：端点必须与采样角**逐位相同**（见 seg_angle）★
    #   注意 `seg_angle(0)` 是 **-55°（左刃尖）**，中段是 `seg_angle(segs//2)`（= 0°）✓
    mid, horn = section(seg_angle(CRESCENT["segs"] // 2)), section(seg_angle(CRESCENT["segs"]))
    # 径向厚度 = 外缘点到内缘点的 XZ 距离（θ=0 时差异落在 Z 上，不能只看 X）
    mid_thick = math.hypot(mid[1][0] - mid[0][0], mid[1][2] - mid[0][2])
    horn_gap = math.hypot(horn[1][0] - horn[0][0], horn[1][2] - horn[0][2])
    print("  中段径向厚度（|外缘 - 内缘|）= %.4f" % mid_thick)
    print("  刃尖端「内缘与外缘的距离」= %.6f（应 ≈ 0）" % horn_gap)

    def ck(name, cond, detail=""):
        nonlocal ok
        print(("  [PASS] " if cond else "  [FAIL] ") + name + ("  " + detail if detail else ""))
        ok = ok and cond

    ck("中段厚度 > 0.4（不是一条细线）", mid_thick > 0.4, "%.4f" % mid_thick)
    ck("两端收成刃尖（间隙 < 1e-4）", horn_gap < 1e-4, "%.6f" % horn_gap)
    ck("三角数 = segs × 4 × 4（双面）", tris == CRESCENT["segs"] * 16, str(tris))
    ck("顶点数 = 三角数 × 3（flat shading）", verts == tris * 3, str(verts))

    # ══════════════════════════════════════════════════════════════════════
    # ★ v187：以下三组是**反向校验** —— 它们要能"抓住 C# 那个 NaN"才叫同源 ★
    # ══════════════════════════════════════════════════════════════════════

    # ① float32 语义：cos(π/2) 真的是负数 ⇒ 旧写法必然 NaN（这就是用户看到的那串红字）
    c32, half32 = cos_half_pi_f32()
    print("  float32 语义：π/2 = %.9f，cos(π/2) = %.6e（负数 ⇒ Pow(负数,0.45) = NaN）"
          % (half32, c32))
    ck("float32 下 cos(π/2) < 0（旧写法 `max(cos,0)**0.45` 必然 NaN）", c32 < 0.0, "%.3e" % c32)

    # ② 刃尖那一处：必须是**精确 0**，且不能是 NaN（NaN 会伪装成"很小"）
    horn_t = taper(seg_angle(CRESCENT["segs"]))
    mid_t = taper(seg_angle(CRESCENT["segs"] // 2))
    print("  taper(0°) = %.6f ／ taper(55°) = %.6e" % (mid_t, horn_t))
    ck("taper(±half_deg) == 0.0 且有限（v187 的 guard 守的就是这一处）",
       horn_t == 0.0 and finite(horn_t), "%.3e" % horn_t)
    ck("taper(0°) == 1.0（中段不收细）", abs(mid_t - 1.0) < 1e-6, "%.6f" % mid_t)
    ck("刃尖顶点 Y 落在 HY_FLOOR 上（inner/outer 都在 1.0 ⇒ 收成尖角）",
       abs(horn[0][0] - horn[1][0]) < 1e-9 and abs(horn[0][2] - horn[1][2]) < 1e-9,
       "Δ=(%.3e, %.3e)" % (horn[0][0] - horn[1][0], horn[0][2] - horn[1][2]))

    # ③ 全部顶点扫一遍 NaN/∞ —— Unity 的 "abnormal mesh bounds" 就是这一条挂掉
    bad = 0
    allv = 0
    for tri in crescent_tris():
        for v in tri:
            allv += 1
            if not (finite(v[0]) and finite(v[1]) and finite(v[2])):
                bad += 1
    ck("全部 %d 个顶点有限（NaN/∞ ⇒ Unity 刷 Invalid worldAABB）" % allv,
       bad == 0, "坏顶点 %d" % bad)

    # ④ **回 C# 核对**：常量 + guard。不一致 = 两边已经不同源了
    got, why = cs_constants()
    if got is None:
        ck("能与 WorldView.cs 核对常量", False, why)
    else:
        for key in ("segs", "half_deg", "thickness", "half_thick_y", "taper_pow",
                    "hy_floor", "outer_thin", "outer"):
            mine = float(CRESCENT[key])
            theirs = got[key]
            same = theirs is not None and abs(mine - theirs) < 1e-9
            ck("与 C# 同源：%s = %s" % (key, ("%.6g" % mine)),
               same, "C# → %s" % ("(没找到)" if theirs is None else "%.6g" % theirs))
        ck("C# 里那道 NaN guard 还在（`if (!(c > 0f)) return 0f;`）",
           got["nan_guard"] > 0.5, "")
    return ok, quads, tris, verts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只核对常量，不写文件")
    args = ap.parse_args()

    ok, quads, tris, verts = check()
    print("")
    if args.check:
        print("CHECK " + ("PASS" if ok else "FAIL"))
        return 0 if ok else 1

    path = write_obj(crescent_tris())
    manifest = {
        "name": "FxCrescent",
        "version": "v187",
        "unit": "外径归一 1.0（由 MeshBit.SclAxis 贴到判定框）",
        "params": CRESCENT,
        "counts": {"quads": quads, "tris": tris, "verts": verts},
        "csharp": "Assets/Scripts/Bootstrap/WorldView.cs :: FxCrescentMesh / CrescentSection / CrescentTaper",
        "cache_key": 5800,
        "obj": os.path.basename(path),
    }
    mpath = os.path.join(OUT, "fx_crescent.manifest.json")
    with open(mpath, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    print("已写出 %s" % path)
    print("已写出 %s" % mpath)
    print("CHECK " + ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
