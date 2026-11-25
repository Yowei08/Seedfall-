#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_bug_meshes.py —— v191 虫怪网格的**离线权威副本**。

它和 `Assets/Scripts/Model/ProceduralBugs.cs` 是**同一套拓扑、同一组常数、同一个公式**。
C# 那边是运行时（唯一能被 Unity / csproj 编译的位置），这边是给人看、给 CI 比的离线副本：

    gen_bug_meshes.py  ..... 按 C# 的权威表重建全部零件 → 出 OBJ + 清单（本文件）
    verify_bug_meshes.py .. 逐项比对"运行时报告 / 本脚本重建 / OBJ / C# 源码参数"，全 PASS 才退出码 0

★ **C# 是真相，本文件是它的副本** ★
几何对不上时**改这里、不改 `ProceduralBugs.cs`**：C# 一个 `.cs` 字节都不许因为校验脚本而改动。

★★ v191 的物种构成（5 个 id → 4 种昆虫 ＋ BOSS 原样）★★
    · `grunt`   锹形虫 Stag    ：大颚钳×2 + 鞘翅×2 + 棒状触角 + 6 条三段腿 + 前胸背板
    · `shooter` 蛾     Moth    ：4 片翅（前翅×2 / 后翅×2）+ 羽状触角 + 卷曲口器 + 无鞘翅无大颚
    · `jumper`  蚱蜢   Hopper  ：2 条巨大后腿 + 4 条步足 + 复眼×2 + 覆翅×2 + 短触角
    · `pig`     瓢虫   Ladybug ：半球壳 + 4 枚壳面斑点 + 极短腿 + 小触角
    · `boss`    巨型甲虫 Giant ：**v181 原样**（三段体节 + 背甲 + 头角 + 尾刺 + 鞘翅×2 + 大颚×2）

★ 为什么运行时构造不在这个目录 ★
`Tools/` 在 `Assets/` 之外，Unity 只编译 `Assets/` 与 `Packages/`，而 `Seedfall.Runtime.csproj`
用的是**显式** `<Compile Include="Assets\\...">` ⇒ 放这里的 .py 只负责"同一套算法的第二实现" ✓

用法：
    python Tools/mesh_gen/gen_bug_meshes.py          # 出 OBJ + out_bugs/bug_mesh_manifest.json
    python Tools/mesh_gen/gen_bug_meshes.py --check  # 只核对 + 打印，不写任何文件
"""

from __future__ import annotations

import json
import math
import os
import re
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_DIR = os.path.join(HERE, "out_bugs")
CS_SOURCE = os.path.join(PROJECT, "Assets", "Scripts", "Model", "ProceduralBugs.cs")

try:      # ★ 输出里有中文路径：在 ASCII/GBK 控制台上别让编码把 CI 弄红
    sys.stdout.reconfigure(errors="replace")
except Exception:
    pass

MANIFEST_VERSION = "v196-bug-meshes"

# ══════════════════════════════════════════════════════════════════════════
# 权威表 1：怪物 id（顺序 = Assets/Resources/Data/enemies.json）
# ══════════════════════════════════════════════════════════════════════════
TYPE_IDS = ["pig", "grunt", "shooter", "jumper", "boss"]
BOSS_ID = "boss"
BOSS_UNIT = 2.5          # 与 C# 的 `ProceduralBugs.BossUnit` 一致
# ★ v196：基础兵 `grunt`（锹形虫）的**体型系数** —— 与 C# 的 `ProceduralBugs.StagScale` 一致 ★
#   用户原话「制作一个更小的虫类（体型更小）」⇒ 整只等比缩小 28%（比例一个不变）
#   ★ 唯一真相在 C# `ShapeOf`：这个常数只是它的离线镜像 ⇒ 两边不一致时 verify 会点名 FAIL ★
STAG_SCALE = 0.72

# ══════════════════════════════════════════════════════════════════════════
# 权威表 1b：id → 物种（= C# 的 `SpeciesOf`，BOSS 优先判）
# ══════════════════════════════════════════════════════════════════════════
SPECIES_OF_ID = {
    "pig": "Ladybug",
    "grunt": "Stag",
    "shooter": "Moth",
    "jumper": "Hopper",
}
SPECIES_SEED_SALT = 0x91E3BEEF   # 第二条随机流的盐（C# `Crc32(key) ^ 0x91E3BEEFu`）


def species_of(ident: str, boss: bool) -> str:
    """id → 物种名。**BOSS 优先**，其余由 id 决定（纯函数）。"""
    if boss:
        return "Giant"
    return SPECIES_OF_ID.get(ident, "Stag")


# ══════════════════════════════════════════════════════════════════════════
# 权威表 2：确定性（crc32 + xorshift32）—— 与 C# 逐位一致
# ══════════════════════════════════════════════════════════════════════════
def crc32(text: str) -> int:
    """标准 CRC-32（IEEE 802.3），按 UTF-8 字节。与 C# 的位运算实现等价。"""
    return zlib.crc32(text.encode("utf-8")) & 0xFFFFFFFF


class Rng32:
    """xorshift32 —— 与 C# 的 `ProceduralBugs.Rng32` 同构。"""

    def __init__(self, seed: int):
        self.s = 0x9E3779B9 if seed == 0 else (seed & 0xFFFFFFFF)

    def next(self) -> int:
        x = self.s
        x ^= (x << 13) & 0xFFFFFFFF
        x ^= x >> 17
        x ^= (x << 5) & 0xFFFFFFFF
        x &= 0xFFFFFFFF
        self.s = x
        return x

    def next01(self) -> float:
        return (self.next() >> 8) / 16777216.0


# ══════════════════════════════════════════════════════════════════════════
# 权威表 3：形状参数（按 id 派生）—— 与 C# `ShapeOf` 逐字一致
#
# 键名 = C# `Shape` 的字段名（`HeadLen` …），报告里的小写键（`headLen`）由
# `REPORT_PARAM_KEYS` 映射过来 ⇒ 一眼能看出"报告里的数 = 哪个字段"。
# ══════════════════════════════════════════════════════════════════════════
def shape_of(ident: str, boss: bool) -> dict:
    kind = species_of(ident, boss)
    # ★ v196：**几何乘数** —— 与 C# `ShapeOf` 的那一行同构：
    #   `unit = boss ? BossUnit : (kind == Stag ? StagScale : 1f)`
    #   ⇒ BOSS 仍是 2.5（逐位不变）、其余三种仍是 1、只有 grunt 是 0.72 ✓
    unit = BOSS_UNIT if boss else (STAG_SCALE if kind == "Stag" else 1.0)
    # 第一条流：u0..u16 —— **BOSS 的每一个数都由它决定**，取值顺序不可动
    r = Rng32(crc32(ident))
    u = [r.next01() for _ in range(17)]
    # 第二条流：物种专属比例 w0..w7（另起一条 ⇒ 不动 u0..u16 ⇒ BOSS 逐位不变）
    r2 = Rng32(crc32(ident) ^ SPECIES_SEED_SALT)
    w = [r2.next01() for _ in range(8)]
    return {
        "Id": ident,
        "Boss": boss,
        "Kind": kind,
        "Unit": unit,
        # ── 体节长度比 ──
        "HeadLen": 0.130 * unit * (0.85 + 0.45 * u[0]),
        "ThoraxLen": 0.250 * unit * (0.85 + 0.45 * u[1]),
        "AbdomenLen": 0.320 * unit * (0.75 + 0.95 * u[2]),
        # ── 粗细 / 腹部胖瘦 ──
        "HeadR": 0.095 * unit * (0.85 + 0.45 * u[3]),
        "ThoraxR": 0.128 * unit * (0.85 + 0.45 * u[4]),
        "AbdomenR": 0.140 * unit * (0.80 + 0.50 * u[5]) * (0.75 + 0.55 * u[6]),
        # ── 腿长 / 腿粗 / 触角 ──
        "LegLen": 0.215 * unit * (0.80 + 0.60 * u[7]),
        "LegThick": 0.030 * unit * (0.80 + 0.60 * u[8]),
        "AntennaLen": 0.180 * unit * (0.70 + 0.90 * u[9]),
        # ── 体轴高度 ──
        "BodyY": 0.320 * unit * (0.85 + 0.35 * u[10]),
        # ── 每段面数：6 或 8 ──
        "Sides": 8 if u[11] > 0.55 else 6,
        # ── 有无甲壳 / 角 / 尾刺（★ BOSS 强制有背甲与头角 ★）──
        "Carapace": bool(boss) or u[12] < 0.50,
        "Horn": bool(boss) or u[13] < 0.40,
        "TailSpike": u[14] < 0.45,
        "Hue": u[15],
        "LegFreq": 4.0 + 4.0 * u[16],
        # ── ★ v191：物种专属比例（只对非 BOSS 有意义；BOSS 不读它们）──
        "WingLen": 0.46 * unit * (0.90 + 0.20 * w[0]),
        "WingSpan": 0.30 * unit * (0.85 + 0.30 * w[1]),
        "HindLen": 0.40 * unit * (0.88 + 0.24 * w[2]),
        "HindThick": 0.052 * unit * (0.85 + 0.30 * w[3]),
        "DomeR": 0.285 * unit * (0.90 + 0.20 * w[4]),
        "DomeH": 0.235 * unit * (0.90 + 0.20 * w[5]),
        "PincerLen": 0.215 * unit * (0.85 + 0.30 * w[6]),
        "EyeR": 0.050 * unit * (0.85 + 0.30 * w[7]),
    }


# 报告里的参数名 → `Shape` 字段名（`BugShowcase.Report` 打印的是小写小驼峰）
REPORT_PARAM_KEYS = {
    "headLen": "HeadLen", "thoraxLen": "ThoraxLen", "abdomenLen": "AbdomenLen",
    "headR": "HeadR", "thoraxR": "ThoraxR", "abdomenR": "AbdomenR",
    "legLen": "LegLen", "legThick": "LegThick", "antennaLen": "AntennaLen",
    "bodyY": "BodyY", "hue": "Hue", "legFreq": "LegFreq",
}
REPORT_FLAG_KEYS = {"carapace": "Carapace", "horn": "Horn",
                    "tailSpike": "TailSpike", "boss": "Boss"}


# ══════════════════════════════════════════════════════════════════════════
# 向量小工具（float64；C# 是 float32 ⇒ verify 用 2e-4 的容差比）
# ══════════════════════════════════════════════════════════════════════════
def vadd(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def vsub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def vmul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def vdot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def vcross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def vnorm(a):
    n = math.sqrt(vdot(a, a))
    return (0.0, 0.0, 0.0) if n < 1e-12 else (a[0] / n, a[1] / n, a[2] / n)


UP = (0.0, 1.0, 0.0)
RIGHT = (1.0, 0.0, 0.0)
FORWARD = (0.0, 0.0, 1.0)
DOWN = (0.0, -1.0, 0.0)


class Builder:
    """
    三角汤 + 逐面外法线。与 C# 的 `ProceduralBugs.Builder` 同构。

    三个构造原语，与 C# 一一对应：
        `tube`  —— 任意轴、椭圆截面、可选端盖（**BOSS 全部零件只用它**）
        `blade` —— 同上，但横截面的 `u` 轴由调用方给（翅 / 鳃叶要明确的"弦向"）
        `dome`  —— 半球顶（瓢虫的壳 / 壳面斑点 / 蚱蜢的复眼）
    """

    def __init__(self):
        self.v = []
        self.t = []

    def tri_out(self, a, b, c, outward):
        n = vcross(vsub(b, a), vsub(c, a))
        if vdot(n, outward) < 0.0:
            b, c = c, b
        i = len(self.v)
        self.v.extend([a, b, c])
        self.t.extend([i, i + 1, i + 2])

    def tube(self, c0, c1, ru0, rv0, ru1, rv1, sides, twist, cap0, cap1):
        axis = vsub(c1, c0)
        if vdot(axis, axis) < 1e-12:
            return
        axis = vnorm(axis)
        reference = UP if abs(axis[1]) < 0.9 else RIGHT
        u = vnorm(vcross(reference, axis))
        v = vnorm(vcross(axis, u))

        ra = []
        rb = []
        for i in range(sides):
            t = twist + (i / sides) * math.pi * 2.0
            cs, sn = math.cos(t), math.sin(t)
            ra.append(vadd(c0, vadd(vmul(u, cs * ru0), vmul(v, sn * rv0))))
            rb.append(vadd(c1, vadd(vmul(u, cs * ru1), vmul(v, sn * rv1))))

        for i in range(sides):
            j = (i + 1) % sides
            tm = twist + ((i + 0.5) / sides) * math.pi * 2.0
            cm, sm = math.cos(tm), math.sin(tm)
            outward = vadd(vmul(u, cm * (ru0 + ru1) * 0.5), vmul(v, sm * (rv0 + rv1) * 0.5))
            if vdot(outward, outward) < 1e-12:
                outward = vsub(ra[i], c0)
            self.tri_out(ra[i], ra[j], rb[j], outward)
            self.tri_out(ra[i], rb[j], rb[i], outward)

        if cap0:
            for i in range(sides):
                self.tri_out(c0, ra[(i + 1) % sides], ra[i], vmul(axis, -1.0))
        if cap1:
            for i in range(sides):
                self.tri_out(c1, rb[i], rb[(i + 1) % sides], axis)

    def blade(self, c0, c1, u_axis, ru0, rv0, ru1, rv1, sides, cap0, cap1):
        axis = vsub(c1, c0)
        if vdot(axis, axis) < 1e-12:
            return
        axis = vnorm(axis)
        # u = Vector3.ProjectOnPlane(uAxis, axis)  ⇒ uAxis - axis * dot(uAxis, axis)
        k = vdot(u_axis, axis)
        u = vsub(u_axis, vmul(axis, k))
        if vdot(u, u) < 1e-10:
            reference = UP if abs(axis[1]) < 0.9 else RIGHT
            u = vcross(reference, axis)
        u = vnorm(u)
        v = vnorm(vcross(axis, u))

        ra = []
        rb = []
        for i in range(sides):
            t = (i / sides) * math.pi * 2.0
            cs, sn = math.cos(t), math.sin(t)
            ra.append(vadd(c0, vadd(vmul(u, cs * ru0), vmul(v, sn * rv0))))
            rb.append(vadd(c1, vadd(vmul(u, cs * ru1), vmul(v, sn * rv1))))

        for i in range(sides):
            j = (i + 1) % sides
            tm = ((i + 0.5) / sides) * math.pi * 2.0
            cm, sm = math.cos(tm), math.sin(tm)
            outward = vadd(vmul(u, cm * (ru0 + ru1) * 0.5), vmul(v, sm * (rv0 + rv1) * 0.5))
            if vdot(outward, outward) < 1e-12:
                outward = vsub(ra[i], c0)
            self.tri_out(ra[i], ra[j], rb[j], outward)
            self.tri_out(ra[i], rb[j], rb[i], outward)

        if cap0:
            for i in range(sides):
                self.tri_out(c0, ra[(i + 1) % sides], ra[i], vmul(axis, -1.0))
        if cap1:
            for i in range(sides):
                self.tri_out(c1, rb[i], rb[(i + 1) % sides], axis)

    def dome(self, center, rx, rz, height, rings, sides, cap_bottom):
        if rings < 1 or sides < 3:
            return
        r = max(1, rings)
        sd = max(3, sides)
        apex = (center[0], center[1] + height, center[2])
        prev = None
        first = None
        for ring in range(r + 1):
            phi = (ring / r) * math.pi * 0.5
            c = math.cos(phi)
            sy = math.sin(phi) * height
            cur = []
            for i in range(sd):
                th = (i / sd) * math.pi * 2.0
                cur.append((center[0] + c * math.cos(th) * rx,
                            center[1] + sy,
                            center[2] + c * math.sin(th) * rz))
            if first is None:
                first = cur
            if prev is not None:
                for i in range(sd):
                    j = (i + 1) % sd
                    thm = ((i + 0.5) / sd) * math.pi * 2.0
                    outward = (math.cos(thm), 0.30, math.sin(thm))
                    if ring == r:
                        # 顶环退化成一个点 ⇒ 用三角扇收口，不产生零面积三角形
                        self.tri_out(prev[i], prev[j], apex, outward)
                    else:
                        self.tri_out(prev[i], prev[j], cur[j], outward)
                        self.tri_out(prev[i], cur[j], cur[i], outward)
            prev = cur
        if cap_bottom and first is not None:
            for i in range(sd):
                self.tri_out(center, first[(i + 1) % sd], first[i], DOWN)


# ══════════════════════════════════════════════════════════════════════════
# 权威表 4：各零件几何 —— 与 C# 的 `*Mesh` / `*Body` / `*Wing` … 逐行对应
#
# 模型空间约定：**+Y 向上、+Z 向前、原点在脚底平面中心**。
# 三段体节沿 Z 排布：头在 +Z 端，腹在 -Z 端。
# ══════════════════════════════════════════════════════════════════════════

# ── 旧几何（BOSS 专用，v181 原样；一个字节都没动）────────────────────────
def body_mesh(s):
    b = Builder()
    n = s["Sides"]
    y = s["BodyY"]
    flat = 0.86
    tf = s["ThoraxLen"] * 0.5
    tb = -s["ThoraxLen"] * 0.5

    b.tube((0.0, y, tb), (0.0, y, tf),
           s["ThoraxR"] * 0.95, s["ThoraxR"] * 0.95 * flat,
           s["ThoraxR"] * 0.90, s["ThoraxR"] * 0.90 * flat, n, 0.0, False, False)
    b.tube((0.0, y, tf), (0.0, y * 1.02, tf + s["HeadLen"]),
           s["ThoraxR"] * 0.88, s["ThoraxR"] * 0.88 * flat,
           s["HeadR"], s["HeadR"] * flat, n, 0.26, False, True)

    waist = tb - s["AbdomenLen"] * 0.55
    tail = tb - s["AbdomenLen"]
    b.tube((0.0, y, tb), (0.0, y * 0.99, waist),
           s["ThoraxR"] * 0.92, s["ThoraxR"] * 0.92 * flat,
           s["AbdomenR"] * 1.06, s["AbdomenR"] * 1.06 * flat, n, 0.13, False, False)
    b.tube((0.0, y * 0.99, waist), (0.0, y * 0.95, tail),
           s["AbdomenR"] * 1.06, s["AbdomenR"] * 1.06 * flat,
           s["AbdomenR"] * 0.26, s["AbdomenR"] * 0.26 * flat, n, 0.13, False, True)
    return b


def accent_mesh(s):
    b = Builder()
    y = s["BodyY"]
    tf = s["ThoraxLen"] * 0.5
    tb = -s["ThoraxLen"] * 0.5
    tail = tb - s["AbdomenLen"]
    if s["Carapace"]:
        b.tube((0.0, y + s["ThoraxR"] * 0.60, tb * 0.92),
               (0.0, y + s["ThoraxR"] * 0.50, tf * 0.92),
               s["ThoraxR"] * 1.02, s["ThoraxR"] * 0.28,
               s["ThoraxR"] * 1.02, s["ThoraxR"] * 0.28, s["Sides"], 0.52, True, True)
    if s["Horn"]:
        z0 = tf + s["HeadLen"] * 0.55
        b.tube((0.0, y + s["HeadR"] * 0.45, z0),
               (0.0, y + s["HeadR"] * 2.10, z0 + s["HeadLen"] * 0.62),
               s["HeadR"] * 0.34, s["HeadR"] * 0.34,
               s["HeadR"] * 0.03, s["HeadR"] * 0.03, 4, 0.4, True, True)
    if s["TailSpike"]:
        b.tube((0.0, y * 0.95, tail),
               (0.0, y * 0.62, tail - s["AbdomenLen"] * 0.42),
               s["AbdomenR"] * 0.28, s["AbdomenR"] * 0.28, 0.004, 0.004, 4, 0.2, True, True)
    return b if len(b.v) else None


def leg_mesh(s):
    b = Builder()
    reach = s["LegLen"]
    hip_y = s["BodyY"] * 0.55
    t = s["LegThick"]
    knee = (reach * 0.52, reach * 0.16, 0.0)
    foot = (reach * 1.00, -hip_y, 0.0)
    b.tube((0.0, 0.0, 0.0), knee, t, t * 0.85, t * 0.72, t * 0.62, 4, 0.4, True, True)
    b.tube(knee, foot, t * 0.72, t * 0.62, t * 0.30, t * 0.26, 4, 0.4, True, True)
    return b


def antenna_mesh(s):
    b = Builder()
    a = s["AntennaLen"]
    t = max(0.006, s["LegThick"] * 0.34)
    elbow = (0.0, a * 0.62, a * 0.22)
    tip = (0.0, a * 1.00, a * 0.86)
    b.tube((0.0, 0.0, 0.0), elbow, t, t, t * 0.72, t * 0.72, 3, 0.5, True, True)
    b.tube(elbow, tip, t * 0.72, t * 0.72, t * 0.26, t * 0.26, 3, 0.5, True, True)
    return b


def elytra_mesh(s, right):
    b = Builder()
    sign = 1.0 if right else -1.0
    half = s["ThoraxR"] * 0.40
    ln = s["ThoraxLen"] + s["AbdomenLen"] * 0.92
    b.tube((sign * (half + s["ThoraxR"] * 0.80), 0.0, s["ThoraxLen"] * 0.42),
           (sign * (half + s["ThoraxR"] * 0.68), -s["Unit"] * 0.012, s["ThoraxLen"] * 0.42 - ln),
           s["ThoraxR"] * 0.80, s["ThoraxR"] * 0.17,
           s["ThoraxR"] * 0.54, s["ThoraxR"] * 0.12, 6, 0.5, True, True)
    return b


def mandible_mesh(s, right):
    b = Builder()
    sign = 1.0 if right else -1.0
    a = s["HeadR"] * 1.35
    knee = (sign * a * 0.42, -a * 0.16, a * 0.72)
    tip = (sign * a * 0.06, -a * 0.30, a * 1.30)
    t = s["HeadR"] * 0.26
    b.tube((0.0, 0.0, 0.0), knee, t, t * 0.9, t * 0.80, t * 0.72, 4, 0.3, True, True)
    b.tube(knee, tip, t * 0.80, t * 0.72, t * 0.20, t * 0.18, 4, 0.3, True, True)
    return b


# ── ★ v191：腿根高度（脚要落在 y=0 ⇒ 腿网格里脚在 -hipY 处）────────────
def hip_height(s):
    """与 C# `HipHeight` 一致。每个物种的"身子离地"不同。"""
    kind = s["Kind"]
    if kind == "Stag":
        return s["BodyY"] * 0.55
    if kind == "Moth":
        return s["BodyY"] * 0.48
    if kind == "Hopper":
        return s["BodyY"] * 0.52
    if kind == "Ladybug":
        return s["DomeR"] * 0.42
    return s["BodyY"] * 0.55      # Giant 走 `LegMesh`，这里只是兜底


# ── ① 锹形虫（grunt）────────────────────────────────────────────────────
def stag_body(s):
    b = Builder()
    n = 8
    y = s["BodyY"]
    flat = 0.84
    tf = s["ThoraxLen"] * 0.62
    tb = -s["ThoraxLen"] * 0.38
    head_y = y + s["Unit"] * 0.020
    head_tip = tf + s["HeadLen"] * 1.30
    tail = tb - s["AbdomenLen"] * 1.02
    waist = tb - s["AbdomenLen"] * 0.50

    b.tube((0.0, y, tb), (0.0, y + s["Unit"] * 0.006, tf),
           s["ThoraxR"] * 0.98, s["ThoraxR"] * 0.98 * flat,
           s["ThoraxR"] * 0.92, s["ThoraxR"] * 0.92 * flat, n, 0.0, False, False)
    b.tube((0.0, y + s["Unit"] * 0.006, tf), (0.0, head_y, head_tip),
           s["ThoraxR"] * 0.90, s["ThoraxR"] * 0.90 * flat,
           s["HeadR"] * 1.18, s["HeadR"] * 1.18 * 0.66, n, 0.22, False, True)
    b.tube((0.0, y, tb), (0.0, y * 0.99, waist),
           s["ThoraxR"] * 0.94, s["ThoraxR"] * 0.94 * flat,
           s["AbdomenR"] * 1.04, s["AbdomenR"] * 1.04 * flat, n, 0.11, False, False)
    b.tube((0.0, y * 0.99, waist), (0.0, y * 0.94, tail),
           s["AbdomenR"] * 1.04, s["AbdomenR"] * 1.04 * flat,
           s["AbdomenR"] * 0.22, s["AbdomenR"] * 0.22 * flat, n, 0.11, False, True)
    return b


def stag_accent(s):
    b = Builder()
    y = s["BodyY"]
    tf = s["ThoraxLen"] * 0.62
    tb = -s["ThoraxLen"] * 0.38
    # 前胸背板（比胸节**宽** ⇒ 侧缘露出来 ⇒ 轮廓更硬）
    b.tube((0.0, y + s["ThoraxR"] * 0.52, tb * 0.86),
           (0.0, y + s["ThoraxR"] * 0.40, tf * 0.92),
           s["ThoraxR"] * 1.16, s["ThoraxR"] * 0.34,
           s["ThoraxR"] * 1.10, s["ThoraxR"] * 0.30, 8, 0.40, True, True)
    # 颈鞘：胸与头之间一圈窄环
    b.tube((0.0, y + s["Unit"] * 0.012, tf * 0.98),
           (0.0, y + s["Unit"] * 0.014, tf * 0.98 + s["Unit"] * 0.030),
           s["ThoraxR"] * 0.72, s["ThoraxR"] * 0.62,
           s["ThoraxR"] * 0.66, s["ThoraxR"] * 0.58, 8, 0.2, True, True)
    return b


def stag_pincer(s, right):
    b = Builder()
    sign = 1.0 if right else -1.0
    ln = s["PincerLen"]
    t = max(0.010, s["HeadR"] * 0.24)
    knee = (sign * ln * 0.30, -ln * 0.06, ln * 0.52)
    tip = (sign * ln * 0.05, -ln * 0.12, ln * 1.00)
    b.tube((0.0, 0.0, 0.0), knee, t, t, t * 0.80, t * 0.80, 5, 0.3, True, True)
    b.tube(knee, tip, t * 0.80, t * 0.80, t * 0.18, t * 0.18, 5, 0.3, True, True)
    # 内齿：朝身体中线扎出去的一小截
    b.tube((sign * ln * 0.20, -ln * 0.07, ln * 0.62),
           (sign * ln * 0.02, -ln * 0.10, ln * 0.68),
           t * 0.55, t * 0.55, 0.003, 0.003, 4, 0.4, True, True)
    return b


def stag_elytra(s, right):
    b = Builder()
    sign = 1.0 if right else -1.0
    z_back = -s["ThoraxLen"] * 0.30
    z_tip = z_back - s["AbdomenLen"] * 1.36
    r = s["AbdomenR"]
    b.tube((sign * s["ThoraxR"] * 0.52, s["BodyY"] + s["ThoraxR"] * 0.46, z_back),
           (sign * s["ThoraxR"] * 0.30, s["BodyY"] + r * 0.60, z_tip),
           r * 0.62, r * 0.40, r * 0.30, r * 0.20, 6, 0.5, True, True)
    return b


def stag_antenna(s):
    b = Builder()
    a = s["AntennaLen"] * 1.10
    t = max(0.006, s["LegThick"] * 0.32)
    elbow = (0.0, a * 0.34, a * 0.42)
    stem = (0.0, a * 0.46, a * 0.84)
    b.tube((0.0, 0.0, 0.0), elbow, t, t, t * 0.85, t * 0.85, 4, 0.4, True, True)
    b.tube(elbow, stem, t * 0.85, t * 0.85, t * 0.62, t * 0.62, 4, 0.4, True, True)
    # 三片鳃叶：从棒的末端张开（扇面在 YZ 平面里）
    for i in range(3):
        fan = (i - 1) * 0.55
        direction = (0.0, math.cos(fan) * a * 0.34, math.sin(fan) * a * 0.30)
        tip = vadd(vadd(stem, direction), (0.0, a * 0.06, a * 0.10))
        b.blade(stem, tip, FORWARD,
                a * 0.13, t * 0.30, a * 0.07, t * 0.16, 4, True, True)
    return b


def stag_leg(s):
    b = Builder()
    reach = s["LegLen"] * 1.05
    hip_y = hip_height(s)
    t = s["LegThick"] * 1.15
    knee = (reach * 0.48, reach * 0.14, 0.0)
    ankle = (reach * 0.86, -hip_y * 0.55, 0.0)
    foot = (reach * 1.00, -hip_y, 0.0)
    b.tube((0.0, 0.0, 0.0), knee, t, t * 0.9, t * 0.78, t * 0.72, 5, 0.4, True, True)
    b.tube(knee, ankle, t * 0.78, t * 0.72, t * 0.52, t * 0.48, 5, 0.2, True, True)
    b.tube(ankle, foot, t * 0.52, t * 0.48, t * 0.20, t * 0.16, 4, 0.2, True, True)
    return b


# ── ② 蛾（shooter）──────────────────────────────────────────────────────
def moth_body(s):
    b = Builder()
    n = 8
    y = s["BodyY"]
    flat = 0.92
    tf = s["ThoraxLen"] * 0.34
    tb = -s["ThoraxLen"] * 0.34
    head_tip = tf + s["HeadLen"] * 0.78
    abdomen_end = tb - s["AbdomenLen"] * 0.96
    bulge = tb - s["AbdomenLen"] * 0.38
    r_a = s["AbdomenR"] * 1.42

    b.tube((0.0, y, tb), (0.0, y + s["Unit"] * 0.008, tf),
           s["ThoraxR"] * 1.02, s["ThoraxR"] * 1.02 * flat,
           s["ThoraxR"] * 0.94, s["ThoraxR"] * 0.94 * flat, n, 0.0, False, False)
    b.tube((0.0, y + s["Unit"] * 0.008, tf), (0.0, y * 1.02, head_tip),
           s["ThoraxR"] * 0.74, s["ThoraxR"] * 0.74 * flat,
           s["HeadR"] * 0.92, s["HeadR"] * 0.92 * flat, n, 0.24, False, True)
    b.tube((0.0, y, tb), (0.0, y * 0.96, bulge),
           s["ThoraxR"] * 0.86, s["ThoraxR"] * 0.86 * flat,
           r_a, r_a * flat, n, 0.13, False, False)
    b.tube((0.0, y * 0.96, bulge), (0.0, y * 0.80, abdomen_end),
           r_a, r_a * flat, r_a * 0.16, r_a * 0.16 * flat, n, 0.13, False, True)

    # 口器：从头的腹面往前下卷成一个小钩
    pr = max(0.006, s["HeadR"] * 0.10)
    p0 = (0.0, y * 1.00 - s["HeadR"] * 0.30, head_tip * 0.96)
    p1 = (0.0, y * 1.00 - s["HeadR"] * 1.05, head_tip + s["HeadLen"] * 0.34)
    p2 = (0.0, y * 1.00 - s["HeadR"] * 1.60, head_tip + s["HeadLen"] * 0.06)
    b.tube(p0, p1, pr, pr, pr * 0.8, pr * 0.8, 4, 0.3, True, True)
    b.tube(p1, p2, pr * 0.8, pr * 0.8, pr * 0.45, pr * 0.45, 4, 0.3, True, True)
    return b


def moth_wing(s, right, fore):
    b = Builder()
    sign = 1.0 if right else -1.0
    y = s["BodyY"]
    if fore:
        root = (sign * s["ThoraxR"] * 0.40, y + s["ThoraxR"] * 0.66, s["ThoraxLen"] * 0.18)
        tip = (sign * (s["WingLen"] + s["ThoraxR"] * 0.40), y + s["ThoraxR"] * 1.10,
               s["ThoraxLen"] * 0.18 - s["WingLen"] * 0.74)
        chord = s["WingSpan"]
        thick = s["Unit"] * 0.013
    else:
        root = (sign * s["ThoraxR"] * 0.34, y + s["ThoraxR"] * 0.20, -s["ThoraxLen"] * 0.16)
        tip = (sign * (s["WingLen"] * 0.70 + s["ThoraxR"] * 0.30), y + s["ThoraxR"] * 0.44,
               -s["ThoraxLen"] * 0.16 - s["WingLen"] * 0.86)
        chord = s["WingSpan"] * 0.62
        thick = s["Unit"] * 0.010
    # 弦向 = 世界 +Z（前），厚度沿翅面法线 ⇒ 一片"薄板"，不是一根"细杆"
    b.blade(root, tip, FORWARD,
            chord * 0.50, thick * 0.50, chord * 0.13, thick * 0.30, 6, True, True)
    return b


def moth_antenna(s):
    b = Builder()
    a = s["AntennaLen"] * 1.45
    t = max(0.005, s["LegThick"] * 0.26)
    p1 = (0.0, a * 0.52, a * 0.20)
    p2 = (0.0, a * 0.74, -a * 0.10)
    p3 = (0.0, a * 0.70, -a * 0.46)
    b.tube((0.0, 0.0, 0.0), p1, t, t, t * 0.85, t * 0.85, 4, 0.4, True, True)
    b.tube(p1, p2, t * 0.85, t * 0.85, t * 0.66, t * 0.66, 4, 0.4, True, True)
    b.tube(p2, p3, t * 0.66, t * 0.66, t * 0.24, t * 0.24, 3, 0.4, True, True)
    return b


def moth_leg(s):
    b = Builder()
    reach = s["LegLen"] * 0.62
    hip_y = hip_height(s)
    t = s["LegThick"] * 0.55
    knee = (reach * 0.50, reach * 0.18, 0.0)
    foot = (reach, -hip_y, 0.0)
    b.tube((0.0, 0.0, 0.0), knee, t, t, t * 0.70, t * 0.70, 4, 0.4, True, True)
    b.tube(knee, foot, t * 0.70, t * 0.70, t * 0.22, t * 0.22, 4, 0.4, True, True)
    return b


# ── ③ 蚱蜢（jumper）────────────────────────────────────────────────────
def hopper_body(s):
    b = Builder()
    n = 8
    y = s["BodyY"]
    tf = s["ThoraxLen"] * 0.40
    tb = -s["ThoraxLen"] * 0.26
    head_tip = tf + s["HeadLen"] * 1.05
    tail = tb - s["AbdomenLen"] * 1.00

    # 头：**高**（rv > ru ⇒ 竖起来的马脸），正面封盖
    b.tube((0.0, y, tf), (0.0, y + s["HeadR"] * 0.18, head_tip),
           s["HeadR"] * 1.10, s["HeadR"] * 1.42,
           s["HeadR"] * 0.80, s["HeadR"] * 1.10, n, 0.20, False, True)
    # 胸：矮而宽
    b.tube((0.0, y, tb), (0.0, y, tf),
           s["ThoraxR"] * 1.02, s["ThoraxR"] * 0.86,
           s["ThoraxR"] * 0.92, s["ThoraxR"] * 0.80, n, 0.0, False, False)
    # 腹：细长、末端上翘一点
    b.tube((0.0, y, tb), (0.0, y * 0.96, tail),
           s["ThoraxR"] * 0.90, s["ThoraxR"] * 0.78,
           s["AbdomenR"] * 0.30, s["AbdomenR"] * 0.26, n, 0.09, False, True)
    return b


def hopper_eye(s, right):
    b = Builder()
    r = s["EyeR"]
    b.dome((0.0, 0.0, 0.0), r, r, r * 0.92, 3, 8, True)
    return b


def hopper_tegmen(s, right):
    b = Builder()
    sign = 1.0 if right else -1.0
    y = s["BodyY"]
    root = (sign * s["ThoraxR"] * 0.22, y + s["ThoraxR"] * 0.62, s["ThoraxLen"] * 0.30)
    tip = (sign * s["ThoraxR"] * 0.34, y + s["ThoraxR"] * 0.34, -s["AbdomenLen"] * 1.05)
    b.blade(root, tip, FORWARD, s["ThoraxR"] * 0.52, s["Unit"] * 0.007,
            s["ThoraxR"] * 0.18, s["Unit"] * 0.004, 4, True, True)
    return b


def hopper_hind_leg(s, right):
    b = Builder()
    hip_y = hip_height(s)
    ln = s["HindLen"]
    t = s["HindThick"]
    knee = (ln * 0.46, ln * 0.62, -ln * 0.34)
    ankle = (ln * 0.66, -hip_y * 0.30, -ln * 0.10)
    foot = (ln * 0.74, -hip_y, ln * 0.22)
    # 股节：**鼓起来**（中段比两端粗得多 ⇒ 蚱蜢的大腿）
    b.tube((0.0, 0.0, 0.0), knee, t * 1.00, t * 0.92, t * 0.72, t * 0.66, 6, 0.4, True, True)
    mid = vmul(knee, 0.42)                                    # Vector3.Lerp(zero, knee, 0.42f)
    mid2 = vadd(mid, vmul(vsub(knee, mid), 0.55))             # Lerp(mid, knee, 0.55f)
    b.tube(mid, mid2, t * 1.42, t * 1.30, t * 1.05, t * 0.95, 6, 0.4, True, True)
    # 胫节 + 脚
    b.tube(knee, ankle, t * 0.52, t * 0.48, t * 0.34, t * 0.30, 5, 0.2, True, True)
    b.tube(ankle, foot, t * 0.34, t * 0.30, t * 0.16, t * 0.14, 4, 0.2, True, True)
    return b


def hopper_leg(s):
    b = Builder()
    reach = s["LegLen"] * 0.72
    hip_y = hip_height(s)
    t = s["LegThick"] * 0.62
    knee = (reach * 0.52, reach * 0.10, 0.0)
    foot = (reach, -hip_y, 0.0)
    b.tube((0.0, 0.0, 0.0), knee, t, t * 0.9, t * 0.68, t * 0.64, 4, 0.4, True, True)
    b.tube(knee, foot, t * 0.68, t * 0.64, t * 0.20, t * 0.18, 4, 0.4, True, True)
    return b


def hopper_antenna(s):
    b = Builder()
    a = s["AntennaLen"] * 0.60
    t = max(0.005, s["LegThick"] * 0.22)
    b.tube((0.0, 0.0, 0.0), (0.0, a * 0.30, a * 0.96),
           t, t, t * 0.55, t * 0.55, 4, 0.4, True, True)
    return b


# ── ④ 瓢虫（pig）───────────────────────────────────────────────────────
def ladybug_shell(s):
    b = Builder()
    rim = s["DomeR"] * 0.30                        # 壳缘离地
    r = s["DomeR"]
    # 壳：经 12 段 × 纬 4 层 ⇒ 圆而矮的半球
    b.dome((0.0, rim, 0.0), r, r * 1.06, s["DomeH"], 4, 12, True)
    # 头：从壳前沿下方伸出一个小锥
    hx = s["HeadR"] * 0.86
    b.tube((0.0, rim + s["DomeH"] * 0.16, r * 0.86),
           (0.0, rim * 0.72, r * 0.86 + s["HeadLen"] * 0.92),
           hx, hx * 0.80, hx * 0.72, hx * 0.58, 8, 0.25, False, True)
    return b


def ladybug_accent(s):
    b = Builder()
    rim = s["DomeR"] * 0.30
    r = s["DomeR"]
    # 头冠：盖在头上面的一片深色小壳
    b.tube((0.0, rim + s["DomeH"] * 0.22, r * 0.86),
           (0.0, rim * 0.80, r * 0.90 + s["HeadLen"] * 0.70),
           s["HeadR"] * 0.92, s["HeadR"] * 0.72,
           s["HeadR"] * 0.62, s["HeadR"] * 0.46, 6, 0.3, True, True)
    # 斑点：按椭球方程反解壳面高度，再压低 3% ⇒ 底边藏进壳内、只露上半个
    rx = r
    rz = r * 1.06
    spot_r = r * 0.19
    for i in range(2):
        for k in range(2):
            sx = 1.0 if k == 0 else -1.0
            px = sx * r * 0.40
            pz = (1.0 if i == 0 else -1.0) * r * 0.40
            h2 = 1.0 - (px / rx) * (px / rx) - (pz / rz) * (pz / rz)
            if h2 <= 0.05:
                continue
            py = rim + s["DomeH"] * math.sqrt(h2) - s["DomeH"] * 0.03
            b.dome((px, py, pz), spot_r, spot_r, spot_r * 0.75, 2, 6, True)
    return b


def ladybug_leg(s):
    b = Builder()
    hip_y = hip_height(s)
    reach = max(s["LegLen"] * 0.50, hip_y * 0.90)
    t = s["LegThick"] * 0.52
    knee = (reach * 0.58, hip_y * 0.18, 0.0)
    foot = (reach, -hip_y, 0.0)
    b.tube((0.0, 0.0, 0.0), knee, t, t, t * 0.66, t * 0.66, 4, 0.4, True, True)
    b.tube(knee, foot, t * 0.66, t * 0.66, t * 0.20, t * 0.20, 4, 0.4, True, True)
    return b


def ladybug_antenna(s):
    b = Builder()
    a = s["AntennaLen"] * 0.34
    t = max(0.005, s["LegThick"] * 0.20)
    b.tube((0.0, 0.0, 0.0), (0.0, a * 0.36, a * 0.92),
           t, t, t * 0.50, t * 0.50, 3, 0.4, True, True)
    return b


# ══════════════════════════════════════════════════════════════════════════
# 权威表 5：零件集合（按物种）—— 报告 `meshes=` / `bounds=` 的**键集**
#   名字与 C# `MeshStats` / `MeshBounds` 打印的完全一致 ⇒ 报告能逐 token 对上
# ══════════════════════════════════════════════════════════════════════════
def _boss_parts(s):
    return {
        "body": body_mesh(s),
        "accent": accent_mesh(s),
        "leg": leg_mesh(s),
        "antenna": antenna_mesh(s),
        "elytra_r": elytra_mesh(s, True),
        "elytra_l": elytra_mesh(s, False),
        "mandible_r": mandible_mesh(s, True),
        "mandible_l": mandible_mesh(s, False),
    }


def _stag_parts(s):
    return {
        "body": stag_body(s),
        "accent": stag_accent(s),
        "leg": stag_leg(s),
        "antenna": stag_antenna(s),
        "wing_f_r": stag_elytra(s, True),
        "wing_f_l": stag_elytra(s, False),
        "pincer_r": stag_pincer(s, True),
        "pincer_l": stag_pincer(s, False),
    }


def _moth_parts(s):
    return {
        "body": moth_body(s),
        "leg": moth_leg(s),
        "antenna": moth_antenna(s),
        "wing_f_r": moth_wing(s, True, True),
        "wing_f_l": moth_wing(s, False, True),
        "wing_h_r": moth_wing(s, True, False),
        "wing_h_l": moth_wing(s, False, False),
    }


def _hopper_parts(s):
    return {
        "body": hopper_body(s),
        "leg": hopper_leg(s),
        "antenna": hopper_antenna(s),
        "wing_f_r": hopper_tegmen(s, True),
        "wing_f_l": hopper_tegmen(s, False),
        "hindleg_r": hopper_hind_leg(s, True),
        "hindleg_l": hopper_hind_leg(s, False),
        "eye_r": hopper_eye(s, True),
        "eye_l": hopper_eye(s, False),
    }


def _ladybug_parts(s):
    return {
        "body": ladybug_shell(s),
        "accent": ladybug_accent(s),
        "leg": ladybug_leg(s),
        "antenna": ladybug_antenna(s),
    }


PART_BUILDERS = {
    "Giant": _boss_parts,
    "Stag": _stag_parts,
    "Moth": _moth_parts,
    "Hopper": _hopper_parts,
    "Ladybug": _ladybug_parts,
}

# 每个物种**应当**有哪些零件（独立于上面那批构造函数的第二份声明 ⇒ 真能抓"少建/多建"）
EXPECTED_PART_SETS = {
    "pig": ["body", "accent", "leg", "antenna"],
    "grunt": ["body", "accent", "leg", "antenna",
              "wing_f_r", "wing_f_l", "pincer_r", "pincer_l"],
    "shooter": ["body", "leg", "antenna",
                "wing_f_r", "wing_f_l", "wing_h_r", "wing_h_l"],
    "jumper": ["body", "leg", "antenna", "wing_f_r", "wing_f_l",
               "hindleg_r", "hindleg_l", "eye_r", "eye_l"],
    "boss": ["body", "accent", "leg", "antenna",
             "elytra_r", "elytra_l", "mandible_r", "mandible_l"],
}

# 体节主网格的**拓扑构成**（独立写的第二份 ⇒ 抓"少建了一段 / 面数改了 / 换了几何"）
#   dome: (rings, sides, cap_bottom)   tube: (sides, cap0, cap1)
BODY_TOPO = {
    "pig": {"dome": [(4, 12, True)], "tube": [(8, False, True)]},
    "grunt": {"dome": [],
              "tube": [(8, False, False), (8, False, True),
                       (8, False, False), (8, False, True)]},
    "shooter": {"dome": [],
                "tube": [(8, False, False), (8, False, True),
                         (8, False, False), (8, False, True),
                         (4, True, True), (4, True, True)]},   # 后两条 = 口器
    "jumper": {"dome": [],
               "tube": [(8, False, True), (8, False, False), (8, False, True)]},
    # boss 走旧几何：4 段 tube，面数随 `Sides`（6 或 8）⇒ 2S + 3S + 2S + 3S = 10S
}


def tube_tris(sides, cap0, cap1):
    """一段管：每段侧面 2 个三角 + 每个端盖 `sides` 个。"""
    return 2 * sides + (sides if cap0 else 0) + (sides if cap1 else 0)


def dome_tris(rings, sides, cap_bottom):
    """半球顶：中间各环 2×sides + 顶环三角扇 sides + 可选底盘 sides。"""
    total = 2 * sides * (rings - 1) + sides
    return total + (sides if cap_bottom else 0)


def expected_body_tris(ident, boss, sides):
    """体节主网格**应当**有多少三角 —— 由 `BODY_TOPO` 独立算出，不看建好的网格。"""
    if boss:
        return sides * 10
    topo = BODY_TOPO[ident]
    total = sum(dome_tris(r, sd, cap) for r, sd, cap in topo["dome"])
    total += sum(tube_tris(sd, c0, c1) for sd, c0, c1 in topo["tube"])
    return total


# ══════════════════════════════════════════════════════════════════════════
# 统计 / 报告行复现
# ══════════════════════════════════════════════════════════════════════════
def bounds_of(b):
    xs = [p[0] for p in b.v]
    ys = [p[1] for p in b.v]
    zs = [p[2] for p in b.v]
    return {"min": [min(xs), min(ys), min(zs)], "max": [max(xs), max(ys), max(zs)]}


def signed_volume(b):
    """有符号体积（散度定理）。> 0 ⇒ 绕序一致朝外。闭壳才有意义。"""
    total = 0.0
    for i in range(0, len(b.t), 3):
        a = b.v[b.t[i]]
        c = b.v[b.t[i + 1]]
        d = b.v[b.t[i + 2]]
        total += vdot(a, vcross(c, d)) / 6.0
    return total


def min_area(b):
    best = float("inf")
    for i in range(0, len(b.t), 3):
        a = b.v[b.t[i]]
        c = b.v[b.t[i + 1]]
        d = b.v[b.t[i + 2]]
        n = vcross(vsub(c, a), vsub(d, a))
        best = min(best, math.sqrt(vdot(n, n)) * 0.5)
    return best


def counts_of(b):
    return (len(b.v), len(b.t) // 3)


def report_meshes_line(ident, boss, s, parts):
    """
    复现 `BugShowcase.Report()` 里的 `meshes=…` 正文（= `ProceduralBugs.MeshStats`）。
    顺序：body / accent / leg / antenna →（BOSS）鞘翅·大颚 →（其余）物种零件 + kind
    """
    def cnt(part):
        b = parts.get(part)
        if b is None:
            return "-"
        v, t = counts_of(b)
        return "%dv/%dt" % (v, t)

    out = [ident + ("/boss" if boss else "/bug")]
    out.append("body=" + cnt("body"))
    out.append("accent=" + cnt("accent"))
    out.append("leg=" + cnt("leg"))
    out.append("antenna=" + cnt("antenna"))
    if boss:
        for p in ("elytra_r", "elytra_l", "mandible_r", "mandible_l"):
            out.append(p + "=" + cnt(p))
    else:
        for p in ("wing_f_r", "wing_f_l", "wing_h_r", "wing_h_l",
                  "pincer_r", "pincer_l", "hindleg_r", "hindleg_l",
                  "eye_r", "eye_l"):
            if parts.get(p) is not None:
                out.append(p + "=" + cnt(p))
        out.append("kind=" + s["Kind"])
    return " ".join(out)


def report_bounds_line(ident, boss, parts):
    """复现 `BugShowcase.Report()` 里的 `bounds=…` 正文（= `ProceduralBugs.MeshBounds`，4 位小数）。"""
    out = [ident]

    def add(part):
        b = parts.get(part)
        if b is None:
            return
        bd = bounds_of(b)
        out.append("%s=min(%s)max(%s)" % (
            part,
            ",".join("%.4f" % x for x in bd["min"]),
            ",".join("%.4f" % x for x in bd["max"])))

    for p in ("body", "leg", "antenna", "accent"):
        add(p)
    order = (("elytra_r", "elytra_l", "mandible_r", "mandible_l") if boss
             else ("wing_f_r", "wing_f_l", "wing_h_r", "wing_h_l",
                   "pincer_r", "pincer_l", "hindleg_r", "hindleg_l",
                   "eye_r", "eye_l"))
    for p in order:
        add(p)
    return " ".join(out)


# ══════════════════════════════════════════════════════════════════════════
# C# 源码里的**形状参数**：把 `ShapeOf` / `HipHeight` / `HipOf` 的数值字面量
# 折成一份多重集合存进清单 ⇒ C# 那边改一个参数，verify 立刻能点名 FAIL。
# ══════════════════════════════════════════════════════════════════════════
PINNED_METHODS = [
    ("ShapeOf", "public static Shape ShapeOf(string id, bool boss)"),
    ("HipHeight", "public static float HipHeight(Shape s)"),
    ("HipOf", "public static Vector3 HipOf(Shape s, int index)"),
]

_NUM_RE = re.compile(
    r"(?<![\w.])"
    r"(0[xX][0-9A-Fa-f]+[uU]?"
    r"|\d+\.\d*(?:[eE][-+]?\d+)?[fFdD]?"
    r"|\.\d+(?:[eE][-+]?\d+)?[fFdD]?"
    r"|\d+(?:[eE][-+]?\d+)?[fFdD]?)"
    r"(?!\w)")


def strip_comments(src: str) -> str:
    """去掉 `//` 与 `/* */` 注释（本仓库的 C# 注释里常写数字，不去掉会污染字面量表）。"""
    src = re.sub(r"/\*.*?\*/", "", src, flags=re.S)
    return re.sub(r"//[^\n]*", "", src)


def method_body(src: str, signature: str):
    """按签名取方法体（花括号配对）。**先剥注释**再配对，避免注释里的括号捣乱。"""
    clean = strip_comments(src)
    i = clean.find(signature)
    if i < 0:
        return None
    j = clean.find("{", i + len(signature))
    if j < 0:
        return None
    depth = 0
    for k in range(j, len(clean)):
        ch = clean[k]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return clean[j + 1:k]
    return None


def numeric_literals(text: str):
    """方法体里的数值字面量 → 归一化后的多重集合（排序列表）。"""
    text = re.sub(r'"[^"\n]*"', '""', text)          # 去字符串（`"BUG_body_"` 之类）
    out = []
    for tok in _NUM_RE.findall(text):
        if tok[:2].lower() == "0x":
            out.append(tok.lower().rstrip("u"))      # 十六进制原样（小写、去 u）
        else:
            out.append(repr(float(tok.rstrip("fFdD"))))
    return sorted(out)


def source_param_pins(path=CS_SOURCE):
    """{方法名: [数值字面量…]}；取不到源码时返回 None。"""
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as f:
        src = f.read()
    pins = {}
    for name, signature in PINNED_METHODS:
        body = method_body(src, signature)
        pins[name] = None if body is None else numeric_literals(body)
    return pins


# ══════════════════════════════════════════════════════════════════════════
# 建整套零件
# ══════════════════════════════════════════════════════════════════════════
def build_parts(s):
    """按 `s["Kind"]` 建这个物种的**全部零件**（不经过 `shape_of` ⇒ 便于扰动测试）。"""
    return PART_BUILDERS[s["Kind"]](s)


def build_type(ident: str, boss: bool):
    s = shape_of(ident, boss)
    return s, build_parts(s)


def write_obj(path, b, name):
    with open(path, "w", encoding="ascii", newline="\n") as f:
        f.write("# %s -- v191 bug mesh (offline authoritative copy of ProceduralBugs.cs)\n" % name)
        f.write("# flat shading: every triangle owns its 3 vertices\n")
        f.write("o %s\n" % name)
        for p in b.v:
            f.write("v %.6f %.6f %.6f\n" % p)
        for i in range(0, len(b.t), 3):
            f.write("f %d %d %d\n" % (b.t[i] + 1, b.t[i + 1] + 1, b.t[i + 2] + 1))


def main(argv):
    check_only = "--check" in argv
    os.makedirs(OUT_DIR, exist_ok=True)
    manifest = {
        "version": MANIFEST_VERSION,
        "source": "Assets/Scripts/Model/ProceduralBugs.cs",
        "crc_poly": "0xEDB88320",
        "xorshift_seed_zero_fallback": "0x9E3779B9",
        "species_seed_salt": "0x91E3BEEF",
        "boss_unit": BOSS_UNIT,
        "type_ids": TYPE_IDS,
        "expected_parts": EXPECTED_PART_SETS,
        "types": [],
    }

    print("BUG-MESH GEN -- offline authoritative copy (same topology as ProceduralBugs.cs)")
    print("%-9s %-5s %-10s %-10s %s" % ("id", "boss", "crc32", "species", "parts"))
    wrote = []
    for ident in TYPE_IDS:
        boss = ident == BOSS_ID
        s, parts = build_type(ident, boss)
        entry = {
            "id": ident,
            "boss": boss,
            "species": s["Kind"],
            "crc32": crc32(ident),
            "shape": {k: (round(v, 9) if isinstance(v, float) else v) for k, v in s.items()},
            "parts": {},
        }
        summary = []
        for pname, b in parts.items():
            v, t = counts_of(b)
            entry["parts"][pname] = {
                "verts": v,
                "tris": t,
                "bounds": bounds_of(b),
                "signed_volume": round(signed_volume(b), 9),
                "min_tri_area": round(min_area(b), 12),
            }
            if not check_only:
                path = os.path.join(OUT_DIR, "%s_%s.obj" % (ident, pname))
                write_obj(path, b, "BUG_%s_%s" % (ident, pname))
                wrote.append(os.path.basename(path))
            summary.append("%s:%dv/%dt" % (pname, v, t))
        manifest["types"].append(entry)
        print("%-9s %-5s 0x%08X %-10s %s" % (ident, "Y" if boss else "n",
                                             entry["crc32"], s["Kind"], " ".join(summary)))

    pins = source_param_pins()
    if pins is None:
        print("\nWARN: %s 读不到 ⇒ 清单里不含参数钉" % CS_SOURCE)
    else:
        missing = [n for n, v in pins.items() if v is None]
        if missing:
            print("\nWARN: 这些方法在 C# 里找不到，参数钉为 null: %s" % ", ".join(missing))
        manifest["source_params"] = pins

    if not check_only:
        if wrote:
            # 清掉旧的 .obj（零件改名 / 物种换构成时会留下孤儿文件）
            keep = set(wrote)
            stale = sorted(n for n in os.listdir(OUT_DIR)
                           if n.endswith(".obj") and n not in keep)
            for n in stale:
                os.remove(os.path.join(OUT_DIR, n))
            if stale:
                print("\nremoved %d stale .obj: %s" % (len(stale), " ".join(stale)))
        path = os.path.join(OUT_DIR, "bug_mesh_manifest.json")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            json.dump(manifest, f, indent=2, ensure_ascii=False)
            f.write("\n")
        print("\nwrote: %s" % path)
        print("wrote: %s\\<id>_<part>.obj (%d files)" % (OUT_DIR, len(wrote)))
    print("PASS (generator itself raised nothing)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
