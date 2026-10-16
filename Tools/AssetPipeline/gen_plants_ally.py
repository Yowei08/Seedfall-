# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 植物帮手（成熟形态）生成器 v2 —— **风格化植物造型**
================================================================================

## 这一版改了什么（重做，不是微调）

上一版（`gen_plants_biped.py`）把 16 个帮手做成了**拟人化双足**：头 + 躯干 + 四肢，
品种差异靠配色和头饰区分。需求方否掉了这条路：**不要拟人化，植物本身就是主体**，
双腿用叶片代替，双手视情况而定，参考《植物大战僵尸：花园战争》——豌豆射手的身体是
豌豆荚、向日葵的头是花盘、仙人掌是柱状体，四肢是植物器官的延伸。

所以本文件**重写建模**（保留 `gen_plants.py` 的 `PLANTS` 参数表与配色作为单一数据源），
并**重做 16 套各自独立的动画**。

## 造型纪律（三条，都能被 `gen_plants_ally_verify.py` 量出来）

① **没有"头 + 躯干 + 四肢"这个结构。** 每个品种的"身体"就是它本身：萝卜是块根、
   豌豆射手是豆荚、蘑菇是伞盖、南瓜是带棱的瓜、石头是多面体堆、苔藓是绒球。
   代码里**不存在 head_geo / torso_geo**（grep 得到 0 处，这是可验证的）。
② **双腿 = 两片叶**（叶柄/藤/根须按品种换），能站能走就行。腿只有髋一个关节，
   读写出来的摆幅按品种分档（见 WALK，`--verify` 会逐帧读）。
③ **每个品种必须独特**：`--verify` 把 16 张剪影归一化后两两算 IoU，
   最大的一张都不许超过 0.74（越低越不像）。改配色不改变剪影，所以这条只认形状。

## 产出

    Assets/Resources/Art/Plants/<id>.fbx                        16 个成熟形态（覆盖）
    Assets/Resources/Art/Anim/<id>_{idle,walk,attack,hit}.fbx   每品种 4 条 = 64 条
    Docs/captures/plant_allies_v2_preview.png                   16 个带中文名的对照图

**绝不触碰**：`<id>_young.fbx`（16 个幼苗）与 `<id>_grown.fbx`（16 个成长中）。
`out_path()` 出口处有断言把关，命中即抛。

## 轴向 / 原点 / 尺寸（老约定，一条都没改）

· 高度在 **Z**、原点在**脚底中心**、`zmin = 0`
· `export_fbx(..., bake_yup=False)` —— **轴向只在导出器里转一次**，自己再烘一次 Y-up
  会让模型躺倒（这个坑踩过，写在这里防止再踩）
· 高度全部落在 **[1.60, 1.80]**：游戏侧 `WorldView.AllyPlantHeight = 1.70` 会按实测高度
  统一缩放，差太多"统一大小"就失效
· **面数 < 1500**：`Budget` 先记账，实测超了只降**装饰件**的细分（斑点/卵石/尖刺/孢子），
  主体块一动不动——把身体降档会把辨识度一起降掉，那还不如少两颗石子

## 动画：为什么"参数化但读起来不一样"

需求要"每个植物的移动和攻击动画单独制作，不要使用同一套"。做法是：
  · **每个品种写一份动画配方**（`WALK[pid]` / `ATK_BEAT[pid]` / `HIT[pid]`），
    不是"一个通用腿摆动函数套 16 次"
  · 配方里是：步态波形（sine/step/bounce/swell/lurch/march/roll）、腿摆角、
    左右相位、**每周期几步**、**脚是否离地**、躯干起伏/侧倾/扭转/挤压、是否整体滑移、
    攻击的分段节拍（名字 + 段数与幅度）
  · 于是"萝卜卫士是沉重的顿步、生命之泉是弹跳、寒霜藤是蜿蜒滑行、苔藓盾是滚动、
    铁根巨像是缓慢的碾步"这些读法全部有数据支撑，不是形容词
  · `--verify` 把每条曲线抽成 96 维签名两两比：**任意两个品种至少有一处对不上**；
    出现两条完全一样的曲线就等于没做到"单独制作"

**受击**：需求原话"受击维持现在版本膨胀加闪白就行"——膨胀 + 闪白是游戏侧代码做的
（`ModelView.HitPulseAmp` + `ArtImportSettings`），这里只产出 `<id>_hit.fbx`。
这一套**允许共用同一个节拍模板**，但幅值/后仰深度/回弹按品种体量分档（HIT 表）。

## 用法（$B = D:\\Steam\\steamapps\\common\\Blender\\blender.exe）

    & $B -b --factory-startup -P gen_plants_ally.py -- static [id...]
    & $B -b --factory-startup -P gen_plants_ally.py -- anim   [id...]
    & $B -b --factory-startup -P gen_plants_ally.py -- all    [id...]
    & $B -b --factory-startup -P gen_plants_ally.py -- report [id...]
    & $B -b --factory-startup -P gen_plants_ally.py -- recipe [id...]

    # 自检（回读 / 逐帧角度 / 独特性 / 对照图）另开一个脚本：
    python Tools/AssetPipeline/gen_plants_ally_verify.py verify
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy                        # noqa: E402
import mathutils                  # noqa: E402

import seedfall_kit as kit        # noqa: E402
import gen_plants as gp           # noqa: E402   ← PLANTS 表只有这一份，不复制

D = math.radians
TAU = math.pi * 2.0

# ---------------------------------------------------------------- 路径
HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
PLANT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Plants")
ANIM_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Anim")
PREVIEW_DIR = os.path.join(HERE, "preview")

PLANTS = gp.PLANTS                       # 16 个种子：id → kind/size/body/accent/...

PROTECTED_SUFFIXES = ("_young.fbx", "_grown.fbx")   # 一个字节都不许动

# ---------------------------------------------------------------- 尺寸
H_MIN, H_MAX = 1.62, 1.79                # 全部落在这条窄带里
S_MIN, S_MAX = 0.45, 1.10
H_OVERRIDE = {"moontree": 1.72}          # 层叠树冠会顶高，直接钉死

TRI_BUDGET = 1500                        # 硬上限（验收线）
TRI_TARGET = 1360                        # 记账软目标（给"账本 vs 实测"的误差留余量）

# ---------------------------------------------------------------- 调色板
# 只**新增**键，不改既有键（键名是 Unity 侧按 BaseColor 取色的契约）。
kit.PALETTE.update({
    "LeafDim":  "#4E8747",     # Leaf 暗一档：叶片背面 / 藤
    "BarkLite": "#8A6A45",     # Bark 亮一档：树干受光面
    "StoneDk":  "#7B7873",     # Stoner 暗一档：石块暗棱面
    "InkEye":   "#2A2419",     # 眼睛专用（比 Ink 再暗一档，小尺寸才读得出）
    "EmberDk":  "#8E3A24",     # Ember 暗一档：辣椒背光面 / 炭化面
    "Glow":     "#FFD9A0",     # 孢子 / 花芯的微光
    "MetalDk":  "#8A8C8E",     # 金属暗面（铁根巨像）
})


# ================================================================ 面数记账
# Blender 5.2 实测（`_probe_tris.py` 打出来的，不是猜的）：
#     UV 球   tri = 2 × segs × (rings − 1)     20/12 → 440、12/6 → 120、5/3 → 20
#     圆柱    tri = 4 × verts − 4               8 → 28、12 → 44
#     圆锥    tri = 4 × verts − 4               6 → 20、8 → 28
#     立方体  tri = 12
# 有了公式就能在建之前记账、超了就降档，而不是造完才发现爆了三倍。
Q = {
    "hero":  (14, 8),      # 196
    "hi":    (12, 7),      # 144
    "mid":   (10, 6),      # 100
    "low":   (8, 4),       # 48
    "tiny":  (6, 3),       # 24
    "micro": (5, 3),       # 20
}
QV = {"hi": 12, "mid": 10, "low": 8, "tiny": 6, "micro": 5}
TIER_RANK = ["micro", "tiny", "low", "mid", "hi", "hero"]


def tri_sphere(segs, rings):
    return 2 * segs * (rings - 1)


def tri_tube(v):
    return 4 * v - 4


# 名字里带这些片段的部件是"可以为了预算牺牲"的次级件。
# 分成两档：
#   DECOR  = 纯装饰，降细分不改变剪影（斑点、卵石、尖刺、孢子、铆钉…）
#   SECON  = 次级结构，降细分只影响圆润度、不影响轮廓形状（花瓣、绒包、树冠、藤圈…）
# 主体块（Root_Bulb / Pod_Mid / Cap / Gourd / Plate_* / Trunk…）**永远不降** ——
# 把它们降档等于把辨识度一起降掉，那还不如少两颗石子。
DECOR_HINTS = ("Spot", "Pebble", "Spike", "Spore", "Sac", "Gleam", "Spark", "Berry",
               "Halo", "Drop", "Rivet", "Ember", "Notch", "Rib", "Crystal", "Fringe",
               "Dome", "Petal", "CharPetal", "Star_", "Clover_", "Collar_", "Calyx_",
               "Coil", "Ring_", "Canopy", "Buttress", "Root_", "MoonRoot", "BigRoot",
               "SacStem", "Tendril", "Helix_", "Crown_", "IvyLeaf", "SideLeaf",
               "Rib_", "Shell_", "LegTip", "Buttress_", "Gleam_", "Spark_")


def _is_decor(name):
    return any(h in name for h in DECOR_HINTS)


def _tier_step(tier, steps):
    i = TIER_RANK.index(tier) if tier in TIER_RANK else 0
    return TIER_RANK[max(0, i - steps)]


def _est_sphere(tier):
    s, r = Q[tier]
    return tri_sphere(s, r)


# 降档表：部件名 → 降几档（由 build_parts 的预算收敛算出，图元函数读它）
DEMOTE = {}
# 全局细分系数：所有球/柱/锥的分辨率统一乘它（兜底手段，见 build_parts）
FINE = 1.0
SEG_FLOOR, RING_FLOOR = 5, 3


def _res(name, tier):
    """(部件名, 档位) → 实际细分（球）。EMOTE 降档 + FINE 全局收窄都要算进去。"""
    steps = DEMOTE.get(name, 0)
    t = _tier_step(tier, steps) if steps else tier
    segs, rings = Q[t]
    if FINE < 0.999:
        segs = max(SEG_FLOOR, int(round(segs * FINE)))
        rings = max(RING_FLOOR, int(round(rings * FINE)))
    return segs, rings, t


def _tube(name, tier):
    """(部件名, 档位) → 实际管状图元的分段数。"""
    steps = DEMOTE.get(name, 0)
    t = _tier_step(tier, steps) if steps else tier
    v = QV[t]
    if FINE < 0.999:
        v = max(SEG_FLOOR, int(round(v * FINE)))
    return v, t


def sp(parts, bud, name, loc, R, color, tier="mid", scale=(1, 1, 1),
       rot=(0, 0, 0), emission=0.0):
    """球：记账 + 造。次级件自动进入可降档名单。"""
    segs, rings, t = _res(name, tier)
    bud.add(name, tri_sphere(segs, rings), t, _is_decor(name))
    parts.append(kit.sphere(name, loc, R, color, scale=scale, segs=segs, rings=rings,
                            rot=rot, emission=emission))


def cy(parts, bud, name, loc, R, depth, color, tier="mid", rot=(0, 0, 0),
       scale=(1, 1, 1), smooth=True, emission=0.0):
    v, t = _tube(name, tier)
    bud.add(name, tri_tube(v), t, _is_decor(name))
    # depth = **全高占身高的比例**，直接交给 Blender（primitive_cylinder_add 收全高，
    # 见 _fix_depth2 的说明：这里曾经把它当"半高"乘过 2，结果是每根管子都长了 2 倍）
    parts.append(kit.cyl(name, loc, R, depth, color, verts=v, rot=rot,
                         scale=scale, smooth=smooth, emission=emission))


def cn(parts, bud, name, loc, r1, r2, depth, color, tier="tiny", rot=(0, 0, 0),
       scale=(1, 1, 1), smooth=True):
    v, t = _tube(name, tier)
    bud.add(name, tri_tube(v), t, _is_decor(name))
    parts.append(kit.cone(name, loc, r1, r2, depth, color, verts=v, rot=rot,
                          scale=scale, smooth=smooth))


def bx(parts, bud, name, loc, size, color, rot=(0, 0, 0), smooth=False):
    bud.add(name, 12, None, False)
    parts.append(kit.box(name, loc, size, color, rot=rot, smooth=smooth))


class Budget(object):
    """建模型时的面数账本：记账 → 超软目标就把装饰件降一档，直到进预算。"""

    def __init__(self, target=TRI_TARGET):
        self.target = target
        self.used = 0
        self.rows = []

    def add(self, name, tris, tier=None, demotable=False):
        self.used += tris
        self.rows.append(dict(name=name, tris=tris, tier=tier, demote=demotable))

    def demote_pass(self):
        """把所有可降档条目里最高的一档整体降一级。返回被动过的名字列表。"""
        cand = [r for r in self.rows
                if r["demote"] and r["tier"] in TIER_RANK and TIER_RANK.index(r["tier"]) > 0]
        if not cand:
            return []
        top = max(TIER_RANK.index(r["tier"]) for r in cand)
        touched = []
        for r in cand:
            if TIER_RANK.index(r["tier"]) != top:
                continue
            newt = TIER_RANK[top - 1]
            segs, rings = Q[newt]
            self.used += tri_sphere(segs, rings) - r["tris"]
            r["tris"], r["tier"] = tri_sphere(segs, rings), newt
            touched.append(r["name"])
        return touched


# ================================================================ 朝向辅助
# Blender：X = 右，−Y = 角色正面，Z = 上。
def aim_matrix(dirv, up=(0.0, 0.0, 1.0)):
    """
    把 +Z 转到 dirv 的正交基（列向量 = 局部 X/Y/Z 在世界里的方向）。

    叶子、藤、炮口全都要"沿某个方向长"——写死欧拉角的话一改方向就散架，
    所以统一用这个函数求旋转，物体的**局部 Z 就是生长轴**。
    """
    z = mathutils.Vector(dirv).normalized()
    u = mathutils.Vector(up).normalized()
    if abs(z.dot(u)) > 0.985:                   # 退化：换一个参考轴
        u = mathutils.Vector((0.0, 1.0, 0.0))
    x = u.cross(z)
    if x.length < 1e-6:
        x = mathutils.Vector((1.0, 0.0, 0.0))
    x.normalize()
    y = z.cross(x).normalized()
    return (x, y, z)


def rot_of(dirv, up=(0.0, 0.0, 1.0)):
    """dirv → 欧拉角（局部 Z 指向 dirv）。"""
    x_ax, y_ax, z_ax = aim_matrix(dirv, up)
    return mathutils.Matrix((x_ax, y_ax, z_ax)).transposed().to_euler()


def _at(base, dirv, t, up=(0.0, 0.0, 1.0)):
    """从 base 沿 dirv 走 t。"""
    _x, _y, z_ax = aim_matrix(dirv, up)
    v = mathutils.Vector(base) + z_ax * t
    return (v.x, v.y, v.z)


def blade(parts, bud, tag, base, dirv, length, width, color, tier="low", drop=0.0,
          up=(0.0, 0.0, 1.0), thick=1.0, name=None):
    """
    一片叶子 / 一段藤 / 一块腿肉 / 一根根须：沿 dirv 排两枚压扁椭球，
    根部窄、中段鼓、尖端收。

    局部轴：X = 宽（width 是**半宽**）、Y = 厚（width × thick）、Z = 长（length）。
    因为 scale 是**局部**的，所以先给一个球再压扁、最后整体旋到 dirv 上。

    `drop` 是沿局部 Y（叶子的法向）的弯垂量：叶尖往前垂 = 正，往后弓 = 负。
    两枚球 + 一点弯垂就能读出"叶子"，比一个圆柱像样得多，代价只有 2×48 面。

    `name` 显式给名字时不做后缀处理（预算降档表是按名字索引的）。
    """
    tag = name or tag
    _x_ax, y_ax, z_ax = aim_matrix(dirv, up)
    rot = rot_of(dirv, up)
    for i, (t, r, w, dp) in enumerate(((0.30, 0.52, 1.00, 0.0), (0.76, 0.44, 0.86, 1.0))):
        half_w = width * w
        c = mathutils.Vector(base) + z_ax * (length * t) + y_ax * (drop * dp)
        sp(parts, bud, "%s_%d" % (tag, i), (c.x, c.y, c.z), half_w, color, tier=tier,
           scale=(1.0, thick, length / max(1e-6, half_w) * r * 0.5), rot=rot)
    return parts


# ================================================================ 眼睛 / 脸
# 眼睛在"植物造型"里是可选的装饰：贴在本体最显眼的那个椭球面上，
# **不是**"头"上的五官——所以它跟着本体的半径走，本体一改自动跟着改。
def face_on(parts, bud, c, scale, dirv=(0.0, -1.0, 0.0), sep=0.46, dz=0.06,
            eye_r=0.085, wide=1.0, thin=0.60, eye="InkEye"):
    """
    把一对眼睛**贴到椭球表面**上（而不是手填坐标）。

    为什么算：眼睛手填数字，一改身体半径不是陷进肉里（渲染成没有五官的球）
    就是浮在空中（比穿模还难看）。这里按 S⁻¹·d 归一化求表面点。
    """
    s = (max(1e-6, scale[0]), max(1e-6, scale[1]), max(1e-6, scale[2]))
    d = mathutils.Vector(dirv).normalized()
    p = mathutils.Vector((d.x / s[0], d.y / s[1], d.z / s[2])).normalized()
    surf = mathutils.Vector((s[0] * p.x, s[1] * p.y, s[2] * p.z))
    side = mathutils.Vector((surf.y, -surf.x, 0.0))
    if side.length < 1e-6:
        side = mathutils.Vector((1.0, 0.0, 0.0))
    side.normalize()
    m = min(s)
    for sgn, nm in ((-1, "L"), (1, "R")):
        q = surf + side * (sep * m * sgn) + mathutils.Vector((0.0, 0.0, dz * m))
        sp(parts, bud, "Eye_%s" % nm, (c[0] + q.x, c[1] + q.y, c[2] + q.z),
           eye_r * m, eye, tier="tiny", scale=(wide, thin, wide * 1.10))


def face_rect(parts, bud, c, half, dirv=(0.0, -1.0, 0.0), sep=0.42, dz=0.02,
              eye_r=0.075, eye="InkEye", thin=0.70):
    """块面身体（石块 / 金属）用矩形锚点贴眼睛：表面就是 c + dirv 那一面。"""
    d = mathutils.Vector(dirv).normalized()
    base = mathutils.Vector(c) + mathutils.Vector((d.x * half[0], d.y * half[1],
                                                   d.z * half[2]))
    for sgn, nm in ((-1, "L"), (1, "R")):
        sp(parts, bud, "Eye_%s" % nm,
           (base.x + sep * half[0] * sgn, base.y - 0.004, base.z + dz * half[2]),
           eye_r, eye, tier="tiny", scale=(1.05, thin, 1.15))


#@GEOM_BEGIN
# ================================================================ 纵向比例
# ★ 这一版最重要的改动：**所有零件的位置都写成一个"高度百分比"** ★
#
# 上一版把零件位置写成"髋高 + 半径 × 系数"，而髋高和半径都跟着 size 走。
# 结果：巨像的自然高顶到 2.69m（要 1.79）、辣椒 2.18m、石拳 2.35m，靠最后的
# "整体等比缩到 1.7"硬凑——模型被压扁，读起来又矮又胖，跟参考的美术风格完全不像。
#
# 现在的做法：身体顶 = torso_top(r) × H，而 r 是**反解**出来的
#     r = (torso_top − hip_frac) / (s_bot + s_mid + s_top)
# 于是"自然高"从构造上就等于目标高，不需要任何缩放补偿。
# 各品种只改 s_bot / s_mid / s_top（身体三段的纵向配比）与 hip_frac（腿长占高比），
# 剪影差异就出在这两个量上。
def torso_geo(H, hip_frac, mass, s_bot, s_mid, s_top, wide=0.60, deep=0.58):
    """
    身体主块的几何：髋（腿枢轴）/ 三段球的球心与半径。

    返回 (hip, [(z, radius, scale), ...], r)，z 是**世界高度**（H 的比例）。
    `mass` 是整体胖瘦（相对基准 0.40H 的粗细）：>1 更圆胖，<1 更瘦长。
    """
    hip = hip_frac * H
    w = mass / 0.40
    span = s_bot + s_mid + s_top
    r = (TORSO_TOP - hip_frac) / span * H          # 反解：身体顶正好落在 TORSO_TOP·H
    blobs = [
        (r * s_bot, r, (wide * w * 0.98, deep * w * 0.96, s_bot * 0.98)),
        (r * (s_bot + s_mid), r * 1.02, (wide * w, deep * w, s_mid * 1.06)),
        (r * (s_bot + s_mid + s_top), r * 0.84, (wide * w * 0.86, deep * w * 0.84, s_top * 0.94)),
    ]
    return hip, blobs, r


TORSO_TOP = 0.96       # 身体顶（含树冠/伞盖这类上层结构）落在总高的 96%
LEG_OVER = 0.20        # 腿尖允许比脚底略低（叶子搭在地上是正常的）


def lay_species(H, mass, hip_frac, s_bot, s_mid, s_top, wide=0.60, deep=0.58):
    """torso_geo 的简写：只给每品种真正不同的那几个量。"""
    return torso_geo(H, hip_frac, mass, s_bot, s_mid, s_top, wide, deep)


# ================================================================ 腿（两片叶）
def legs(parts, cfg, g, H):
    """
    两片叶当腿。**只到"能站能走"为止**：髋一个关节，绕 X 前后摆。

    每品种改的是：叶腿的长宽、分叉角、外张角、根部弯垂方向、叶尖颜色。
    腿的枢轴在髋（世界 (0, ±leg_x, hip)），所以物体局部原点就是关节；
    腿长恒等于髋高 → 永远踩着地，不管身体怎么改。
    返回 joints 片段。
    """
    hip = cfg["hip"]
    bud = cfg["bud"]
    out = {}
    for sgn, tag in ((-1, "L"), (1, "R")):
        sfx = "_%s" % tag
        x = cfg["leg_x"] * sgn
        # 叶刃 1.02×hip、根部抬到 1.10×hip：叶顶插进身体 ~0.04m，
        # 叶尖只低 0.02×hip —— 抬平量小，身体不会被整体推上去
        blade(parts, bud, "LegBlade" + sfx,
              (x * 0.72, cfg["leg_mid_y"], hip * 1.10),
              (cfg["leg_splay"] * sgn, cfg["leg_fwd"], -1.0), hip * 1.02,
              cfg["leg_w"],
              cfg["leg_c1"], tier=cfg["leg_tier"], drop=cfg["leg_drop"],
              up=(0, 1, 0), thick=cfg["leg_thick"])
        blade(parts, bud, "LegTip" + sfx, (x, cfg["leg_tip_y"], hip * 0.22),
              (cfg["leg_splay"] * sgn * 0.5, cfg["leg_tip_fwd"], -1.0), hip * 0.42,
              cfg["leg_w"] * cfg["leg_tip_w"], cfg["leg_c2"], tier=cfg["leg_tier"],
              thick=cfg["leg_thick"])
        out["Leg" + sfx] = (x, 0.0, hip)
    return out


def base_leg_cfg(c):
    """腿参数的默认值；每个品种在 LEGS 表里覆盖自己那几项。"""
    return dict(
        leg_x=0.062, leg_w=0.070, leg_thick=0.48,
        leg_splay=0.20, leg_fwd=-0.06, leg_mid_y=0.0, leg_drop=0.020,
        leg_tip_y=0.045, leg_tip_fwd=-0.34, leg_tip_w=0.64,
        leg_c1=c["accent"], leg_c2=c["accent"], leg_tier="low",
    )


# ================================================================ 手臂（视情况）
def leaf_arm(parts, cfg, g, side, spec, H):
    """
    叶片手臂：从肩长出来的叶柄 + 叶端。**只在需要"朝前伸手"的品种上加**
    （豌豆射手 / 石拳守卫 / 辣椒法师 / 圣树守卫 / 铁根巨像）；
    苔藓盾、生命之泉、三叶草、寒霜藤、星藤、炭花、月桂、南瓜、萝卜、蘑菇
    **一个都没有**——不为对称硬加。

    spec 里的 shoulder / dir / len / w / tip_r 全部相对 H 写。
    """
    bud = cfg["bud"]
    sfx = "_%s" % ("R" if side > 0 else "L")
    base = (spec["shoulder"][0] * H * side, spec["shoulder"][1] * H, spec["shoulder"][2] * H)
    dirv = (spec["dir"][0] * side, spec["dir"][1], spec["dir"][2])
    ln, w = spec["len"] * H, spec["w"] * H
    blade(parts, bud, "ArmBlade" + sfx, base, dirv, ln, w,
          spec.get("color", cfg["accent"]), tier=spec.get("tier", "low"),
          drop=spec.get("drop", 0.02), thick=spec.get("thick", 0.55))
    tip = spec.get("tip", "none")
    if tip != "none":
        end = _at(base, dirv, ln * 1.02)
        x_ax, _y, z_ax = aim_matrix(dirv)
        tr = spec["tip_r"] * H
        if tip == "fist":                   # 石拳：叶柄末端一块石头（多面体感）
            for i, (dxx, _dzz, rr) in enumerate(((0.0, 0.0, 1.0), (0.5, 0.35, 0.68),
                                                 (-0.55, -0.30, 0.62))):
                p = mathutils.Vector(end) + z_ax * (tr * 0.26 * (i + 0.4)) \
                    + x_ax * (tr * dxx)
                sp(parts, bud, "StoneFist%s_%d" % (sfx, i), (p.x, p.y, p.z), tr * rr,
                   spec.get("tipcolor", cfg["body"]), tier="low",
                   scale=(1.15, 0.95, 0.92))
        elif tip == "hand":                 # 施法/抓握：三枚小叶片张开
            for i, ang in enumerate((-42.0, 0.0, 42.0)):
                d2 = (z_ax * math.cos(D(ang)) + x_ax * math.sin(D(ang))).normalized()
                b2 = _at(base, dirv, ln * 0.86)
                blade(parts, bud, "Finger%s_%d" % (sfx, i), b2, d2, tr * 2.6, tr * 0.60,
                      spec.get("tipcolor", cfg["body"]), tier="micro", thick=0.45)
    return ("Arm" + sfx, (base[0], base[1] - g["hip"], base[2] - g["hip"]))


# ================================================================ 通用零件
def stack(parts, bud, tag, specs, color, tier, H, zoff=0.0, wide=1.0, deep=1.0):
    """
    沿 Z 叠一串椭球（半径与 z 都按 **H 的比例**给，H 必须传进来）。

    树干、菌柄、藤柱、弹簧圈、辣椒身这几种"竖向主体"都是这个形状，
    写成一个循环比每个品种手抄五遍球体坐标强——改比例只改一处。
    specs = [(半径, z, 竖向拉伸, 横向系数), ...]
    """
    for i, sp_ in enumerate(specs):
        rr, zz, sz = sp_[0], sp_[1], sp_[2]
        wx = sp_[3] if len(sp_) > 3 else 1.0
        sp(parts, bud, "%s_%d" % (tag, i), (0.0, 0.0, zz * H + zoff), rr * H, color,
           tier=tier, scale=(wx * wide, deep, sz))


# ================================================================ 16 个品种的造型
# 每个 deco_* 只做一件事：把"这个品种是什么"用**植物器官**表达出来。
# 共同点是**没有头、没有躯干、没有四肢** —— 身体就是本体，腿就是两片叶。
# 所有位置都是 H 的比例；眼睛**探出体表 15%**（上一版贴在表面 = 渲染出来看不见）。

def deco_root(c, cfg, g, H):                       # 萝卜卫士
    """圆胖块根身体 + 粗叶腿（坦克感）。身体 = 一颗萝卜，根尖往下扎。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.44, 0.255, 0.575, 0.65, 0.52)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    for i, (z, rr, sc) in enumerate(blobs):
        sp(P, b, "Root_Bulb_%d" % i, (0, 0, z), rr, c["body"],
           tier="hero" if i == 1 else "hi", scale=sc)
    # 根尖：一根细锥往下扎（接地的是两片叶腿，不是它）
    cn(P, b, "Root_Tip", (0, 0, hip * 0.62), r * 0.30, 0.012, H * 0.075, c["body"],
       tier="low", rot=(D(180), 0, 0))
    # 叶冠：3 片宽叶（不是"头发"，是长在块根顶上的叶子）
    top = r * 1.88
    for i, (ang, ln) in enumerate(((-38.0, 0.16), (0.0, 0.21), (38.0, 0.16))):
        d = (math.sin(D(ang)), -0.24 + 0.06 * abs(ang) / 38.0, math.cos(D(ang)) * 0.88)
        blade(P, b, "Crown_%d" % i,
              (math.sin(D(ang)) * r * 0.34, 0.0, top + r * 0.10), d, H * ln, r * 0.36,
              c["accent"], tier="low", drop=0.04)
    # 胸前的两层对称叶（坦克的"肩甲"感，仍是叶）
    for sgn in (-1, 1):
        blade(P, b, "Shell_%s" % ("R" if sgn > 0 else "L"),
              (sgn * r * 0.56, -r * 0.34, top - r * 0.30),
              (sgn * 0.85, -0.46, 0.26), r * 0.68, r * 0.28, c["accent"], tier="low",
              drop=0.04)
    face_on(P, b, (0, 0, r * 1.72), (r, r * 0.95, r * 0.72), sep=0.50, dz=0.10,
            eye_r=0.098)


def deco_shooter(c, cfg, g, H):                    # 豌豆射手
    """豌豆荚身体 + 朝前的炮口。炮口是**本体的一部分**，不是手臂。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.30, 0.23, 0.38, 1.40, 0.36, wide=0.62)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 豆荚：竖着的长椭球（中段鼓、两端收）
    for i, (z, rr, sc) in enumerate(blobs):
        sp(P, b, "Pod_%d" % i, (0, -r * 0.10, z), rr, c["body"],
           tier="hero" if i == 1 else "mid",
           scale=(sc[0], sc[1] * (2.05 if i == 1 else 1.0), sc[2]))
    # 荚缝：accent 色的竖棱 —— 读得出"这是豆荚不是蛋"
    cy(P, b, "Pod_Seam", (0, r * 0.86, r * 1.28), r * 0.10, H * 0.42, c["accent"],
       tier="tiny", rot=(D(90), 0, 0), smooth=False)
    # 炮口：从荚里朝 −Y 伸出的粗筒（本体的一部分，不是手臂）
    mz = TORSO_TOP * H - r * 0.38
    cy(P, b, "Muzzle", (0, -r * 0.34, mz), r * 0.40, H * 0.135, c["body"], tier="mid",
       rot=(D(90), 0, 0))
    cn(P, b, "Muzzle_Lip", (0, -r * 1.24, mz), r * 0.48, r * 0.36, H * 0.022, c["accent"],
       tier="low", rot=(D(-90), 0, 0))
    sp(P, b, "Muzzle_Bore", (0, -r * 1.36, mz), r * 0.22, "Ink", tier="tiny",
       scale=(1.0, 0.45, 1.0))
    # 顶上的豌豆
    for i, (sgn, rr) in enumerate(((-1, 0.30), (1, 0.30), (0, 0.24))):
        sp(P, b, "Pea_%d" % i, (sgn * r * 0.34, r * 0.10, r * 2.02 + i * H * 0.006),
           r * rr, c["accent"], tier="micro", scale=(1.0, 1.0, 1.10))
    face_on(P, b, (0, -r * 0.10, r * 1.22), (r * 0.62, r * 0.62, r * 1.30),
            dirv=(0.0, -1.0, 0.34), sep=0.66, dz=0.22, eye_r=0.095)


def deco_fungus(c, cfg, g, H):                     # 毒蘑菇
    """伞盖做**整个上半身** + 斑点 + 孢子。没有头，伞盖就是它。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.34, 0.28, 0.52, 0.52, 0.50)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 菌柄：两段短粗（不是脖子，是菌柄）
    sp(P, b, "Stalk_Low", (0, 0, hip + r * 0.34), r * 0.44, "Bone", tier="mid",
       scale=(1.0, 0.96, 0.62))
    sp(P, b, "Stalk_Top", (0, 0, hip + r * 1.00), r * 0.34, "Bone", tier="low",
       scale=(1.0, 0.96, 1.05))
    # 伞盖：半球 + 一圈压扁的下缘（蘑菇伞不是球，是上面半球下面平）
    cap_z = TORSO_TOP * H - r * 0.92
    cap_r = r * 1.55
    sp(P, b, "Cap", (0, 0, cap_z), cap_r, c["body"], tier="hero", scale=(1.06, 1.02, 0.64))
    sp(P, b, "Cap_Rim", (0, 0, cap_z - cap_r * 0.34), cap_r * 0.98, c["body"], tier="mid",
       scale=(1.08, 1.04, 0.24))
    sp(P, b, "Cap_Under", (0, 0, cap_z - cap_r * 0.48), cap_r * 0.80, "Bone", tier="low",
       scale=(1.0, 1.0, 0.28))
    spots = int(c.get("spots", 5))
    for i in range(spots):
        a = (i / float(spots)) * 360.0 + 18.0
        rr = cap_r * (0.30 + 0.30 * ((i % 3) / 2.0))
        px, py = math.sin(D(a)) * rr, math.cos(D(a)) * rr
        pz = cap_z + cap_r * 0.56 * math.sqrt(max(0.02, 1.0 - (rr / cap_r) ** 2))
        sp(P, b, "Spot_%d" % i, (px, py, pz), cap_r * (0.19 - 0.03 * (i % 2)), "Bone",
           tier="low", scale=(1.0, 1.0, 0.46))
    for i in range(4):
        a = 40.0 + i * 88.0
        sp(P, b, "Spore_%d" % i,
           (math.sin(D(a)) * cap_r * 0.88, math.cos(D(a)) * cap_r * 0.88,
            cap_z - cap_r * 0.66), cap_r * 0.10, "Glow", tier="micro", emission=1.6)
    face_on(P, b, (0, 0, cap_z - cap_r * 0.06), (cap_r * 1.06, cap_r * 1.02, cap_r * 0.66),
            dirv=(0.0, -1.0, -0.26), sep=0.54, dz=-0.30, eye_r=0.082)


def deco_ivy(c, cfg, g, H):                        # 寒霜藤
    """藤蔓身体 + 冰晶 + 尖刺。它是**蜿蜒的一条藤**，不是站着的生物。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.26, 0.26, 0.30, 1.30, 0.40, wide=0.46)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 主藤：S 形五段（这一段决定了它的剪影跟别人都不一样）
    pts = ((0.00, 0.00, -0.62), (0.52, 0.16, -0.10), (-0.30, -0.10, 0.40),
           (0.44, 0.12, 1.00), (0.04, 0.00, 1.26), (-0.20, 0.06, 1.44))
    for i in range(len(pts) - 1):
        p0 = mathutils.Vector(pts[i]) * r
        p1 = mathutils.Vector(pts[i + 1]) * r
        mid = (p0 + p1) * 0.5
        cy(P, b, "Vine_%d" % i, (mid.x, mid.y, hip + mid.z), r * (0.26 - 0.015 * i),
           (p1 - p0).length * 0.61, c["accent"] if i % 2 else c["body"], tier="mid",
           rot=rot_of(p1 - p0))
    # 冰晶：从主藤上支出去的尖锥（霜的读法）
    for i in range(6):
        u = i / 5.0
        a = 30.0 + i * 62.0
        pz = hip + r * (0.40 + u * 1.05)
        px = math.sin(D(a)) * r * (0.48 - 0.10 * u)
        py = math.cos(D(a)) * r * (0.44 - 0.10 * u)
        d = (math.sin(D(a)) * 0.80, math.cos(D(a)) * 0.80, 0.34 + 0.30 * (i % 2))
        cn(P, b, "Crystal_%d" % i, (px, py, pz), r * 0.18, 0.004, H * 0.050, c["body"],
           tier="micro", rot=rot_of(d))
    # 垂叶：三片挂在藤上的叶
    for i, ang in enumerate((-120.0, 0.0, 120.0)):
        blade(P, b, "IvyLeaf_%d" % i,
              (math.sin(D(ang)) * r * 0.50, math.cos(D(ang)) * r * 0.44, hip + r * 1.46),
              (math.sin(D(ang)) * 0.80, math.cos(D(ang)) * 0.80, 0.16), r * 1.00,
              r * 0.30, c["body"], tier="low", drop=0.12)
    face_on(P, b, (0, 0, hip + r * 0.92), (r * 0.46, r * 0.44, r * 0.46), sep=0.70,
            dz=0.10, eye_r=0.084)


def deco_gourd(c, cfg, g, H):                      # 荆棘南瓜
    """带棱的瓜做躯干 + 藤蔓腿。棱是几何，不是贴图。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.46, 0.255, 0.60, 0.62, 0.52, wide=0.98, deep=0.94)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    zc = r * 1.14
    # 瓜：一个扁球（横向比身体别的品种宽得多 —— 这是它的剪影）
    sp(P, b, "Gourd", (0, 0, zc), r * 1.06, c["body"], tier="hero",
       scale=(1.14, 1.08, 0.92))
    ribs = 8
    for i in range(ribs):
        a = (i / float(ribs)) * 360.0
        px, py = math.sin(D(a)) * r * 1.10, math.cos(D(a)) * r * 1.10
        sp(P, b, "Rib_%d" % i, (px, py, zc), r * 0.20, c["accent"], tier="micro",
           scale=(0.40, 0.40, 4.0), rot=(0, 0, -D(a)))
    spikes = int(c.get("spikes", 10))
    for i in range(spikes):
        a = (i / float(spikes)) * 360.0 + 20.0
        el = D(-14.0 + 30.0 * (i % 2))
        px = math.sin(D(a)) * r * 1.16
        py = math.cos(D(a)) * r * 1.16
        pz = zc + math.sin(el) * r * 0.90
        d = (math.sin(D(a)) * math.cos(el), math.cos(D(a)) * math.cos(el), math.sin(el))
        # 尖刺要**够长**（0.045H → 0.085H）：归一化剪影时圆块的差别会被抹平，
        # 只有探出去的尖角才留在剪影上 —— 这是它跟苔藓盾分开的唯一杠杆
        cn(P, b, "Spike_%d" % i, (px, py, pz), r * 0.16, 0.004, H * 0.085, c["accent"],
           tier="micro", rot=rot_of(d))
    # 瓜蒂：卷曲的一段藤 + 一片大叶（头顶的凸起同样是剪影的一部分）
    cy(P, b, "Stem", (0, 0, zc + r * 1.06), r * 0.16, H * 0.055, "LeafDim", tier="micro")
    cy(P, b, "Tendril", (r * 0.26, 0, zc + r * 1.28), r * 0.075, H * 0.032, "LeafDim",
       tier="micro", rot=(0, D(52), 0))
    for i, ang in enumerate((-58.0, 58.0)):
        blade(P, b, "TopLeaf_%d" % i, (math.sin(D(ang)) * r * 0.30, 0.0, zc + r * 1.22),
              (math.sin(D(ang)) * 0.92, 0.0, 0.62), r * 0.94, r * 0.26, c["accent"],
              tier="low", thick=0.40)
    face_on(P, b, (0, 0, zc), (r * 1.06, r * 1.06, r * 0.92), sep=0.56, dz=0.16,
            eye_r=0.094)


def deco_rock(c, cfg, g, H):                       # 石拳守卫
    """多面体石块堆 + 拳面。**岩石堆叠感**，除腿之外只有两把石拳。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.34, 0.26, 0.50, 0.72, 0.56, wide=0.62, deep=0.60)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 三块错位的石头（对齐就成了一根柱子，错位才有"堆"的读法）
    bx(P, b, "Rock_Low", (-r * 0.14, 0.02, hip + r * 0.30),
       (r * 1.10, r * 0.94, r * 0.72), c["body"], rot=(0, D(8), 0))
    bx(P, b, "Rock_Mid", (r * 0.08, -0.03, hip + r * 0.96),
       (r * 1.34, r * 1.12, r * 0.82), c["body"], rot=(0, D(-9), 0))
    bx(P, b, "Rock_Top", (-r * 0.06, 0.01, hip + r * 1.62),
       (r * 0.88, r * 0.78, r * 0.50), c["body"], rot=(D(5), D(-7), 0))
    # 石座：髋高处一圈外扩的石头，把石堆与两片叶腿连起来
    # （没有它，渲染出来就是"一堆石头浮在两条腿上面"——bodymin 0.351 vs hip 0.440）
    for sgn in (-1, 1):
        bx(P, b, "BaseRock_%s" % ("R" if sgn > 0 else "L"),
           (sgn * r * 0.54, 0.04, hip * 0.70), (r * 0.72, r * 0.88, r * 0.80),
           c["body"], rot=(0, 0, D(-7 * sgn)))
    bx(P, b, "BaseRock_C", (0, -r * 0.10, hip * 0.56), (r * 0.64, r * 0.54, r * 0.64),
       "StoneDk")
    for i, (dxx, dyy, dzz, rr) in enumerate(((-0.52, -0.62, 0.98, 0.44),
                                             (0.46, -0.56, 0.52, 0.38),
                                             (0.10, -0.70, 1.48, 0.32))):
        sp(P, b, "Mossy_%d" % i, (dxx * r, dyy * r, hip + dzz * r), r * rr, c["accent"],
           tier="low", scale=(1.25, 0.42, 0.78))
    # 拳面：前面两块突出的石面（"石拳"的读法），不是手掌
    for sgn in (-1, 1):
        sfx = "_R" if sgn > 0 else "_L"
        bx(P, b, "Fist" + sfx, (sgn * r * 1.00, -r * 0.38, hip + r * 0.74),
           (r * 0.62, r * 0.72, r * 0.66), c["body"], rot=(0, 0, D(14 * sgn)))
        bx(P, b, "FistCap" + sfx, (sgn * r * 1.06, -r * 0.58, hip + r * 0.46),
           (r * 0.42, r * 0.34, r * 0.38), "StoneDk", rot=(D(10), 0, 0))
    face_rect(P, b, (r * 0.04, -r * 0.58, hip + r * 1.44), (r * 1.10, r * 0.44, r * 0.62),
              sep=0.42, dz=0.20, eye_r=0.082)


def deco_moss(c, cfg, g, H):                       # 苔藓盾
    """绒球身体（最该"没有腿之外的东西"的一个）。**没有手、没有头。**"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.50, 0.225, 0.66, 0.82, 0.56, wide=1.02, deep=1.00)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 绒球：主球贴着髋，几乎坐在地上（矮胖、没有脖子）
    zc = r * 1.02
    sp(P, b, "Moss_Core", (0, 0, zc), r * 1.16, c["body"], tier="hero",
       scale=(1.06, 1.04, 0.96))
    domes = int(c.get("domes", 3)) + 4
    for i in range(domes):
        a = (i / float(domes)) * 360.0 + 12.0
        el = D(-26.0 + 52.0 * (i % 3) / 2.0)
        px = math.sin(D(a)) * r * 0.94 * math.cos(el)
        py = math.cos(D(a)) * r * 0.94 * math.cos(el)
        pz = zc + math.sin(el) * r * 0.82
        sp(P, b, "Dome_%d" % i, (px, py, pz), r * 0.42, c["body"], tier="low",
           scale=(1.0, 1.0, 0.86))
    # 卵石：底下压着的几块（苔藓长在石头上的读法）
    for i in range(6):
        a = i * 60.0 + 26.0
        sp(P, b, "Pebble_%d" % i, (math.sin(D(a)) * r * 1.22, math.cos(D(a)) * r * 1.22,
                                   hip * 0.72), r * 0.20, c["accent"], tier="micro",
           scale=(1.2, 1.2, 0.62))
    # 绒芽：顶部几根细芽
    for i in range(4):
        a = i * 90.0 + 40.0
        blade(P, b, "Sprout_%d" % i,
              (math.sin(D(a)) * r * 0.38, math.cos(D(a)) * r * 0.38, zc + r * 0.98),
              (math.sin(D(a)) * 0.30, math.cos(D(a)) * 0.30, 1.0), r * 0.40, r * 0.11,
              c["accent"], tier="micro")
    # ★ 外缘一圈"绒瓣" + 顶部小绒芽 ★
    # 它的剪影跟荆棘南瓜的 IoU 一度到 0.817 —— 两个都是"宽圆块 + 两条腿"。
    # 归一化剪影会把圆块的胖瘦抹平，只有**探出轮廓外的凸起**才留得下来，
    # 所以这里往外加一圈错落的小瓣（不是加大身体）。
    lobes = 9
    for i in range(lobes):
        a = (i / float(lobes)) * 360.0 + 20.0
        rr = r * (1.06 + 0.10 * (i % 3))
        sp(P, b, "Lobe_%d" % i, (math.sin(D(a)) * rr, math.cos(D(a)) * rr,
                                 zc - r * 0.10 + r * 0.28 * ((i % 2) * 2 - 1)),
           r * 0.30, c["body"], tier="tiny", scale=(1.0, 1.0, 1.05))
    for i in range(5):
        a = i * 72.0 + 8.0
        blade(P, b, "TopSprig_%d" % i,
              (math.sin(D(a)) * r * 0.42, math.cos(D(a)) * r * 0.42, zc + r * 1.02),
              (math.sin(D(a)) * 0.62, math.cos(D(a)) * 0.62, 0.80), r * 0.72, r * 0.14,
              c["accent"], tier="tiny", thick=0.55)
    face_on(P, b, (0, 0, zc + r * 0.08), (r * 1.16, r * 1.12, r * 1.10), sep=0.44,
            dz=0.12, eye_r=0.090)


def deco_flower(c, cfg, g, H):                     # 花系骨架（三叶草/月桂/炭花另分派）
    """花瓣头 + 花茎身体。花瓣数按 PLANTS 的 petals。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.28, 0.30, 0.28, 0.80, 0.30, wide=0.34)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    stem_c = c.get("_stem", c["accent"])
    stack(P, b, "Stem", [(0.22, 0.30, 0.60), (0.19, 0.58, 0.60), (0.16, 0.84, 0.55)],
          stem_c, "low", H)
    head_z = TORSO_TOP * H - r * 0.56
    sp(P, b, "Disc", (0, 0, head_z), r * 1.00, c.get("_disc", c["accent"]), tier="mid",
       scale=(1.0, 0.92, 0.34))
    petals = int(c.get("petals", 8))
    for i in range(petals):
        a = (i / float(petals)) * 360.0
        d = (math.sin(D(a)) * 0.94, -0.16, math.cos(D(a)) * 0.94)
        base = (math.sin(D(a)) * r * 0.56, -r * 0.10, head_z + math.cos(D(a)) * r * 0.56)
        blade(P, b, "Petal_%d" % i, base, d, r * 1.05, r * 0.34, c["body"], tier="low",
              drop=0.03, up=(0, 1, 0), thick=0.34)
    sp(P, b, "Core", (0, -r * 0.24, head_z), r * 0.46, c.get("_core", "Gold"), tier="low",
       scale=(1.0, 0.62, 1.0))
    for sgn in (-1, 1):
        blade(P, b, "SideLeaf_%s" % ("R" if sgn > 0 else "L"),
              (sgn * r * 0.16, -r * 0.08, hip + r * 1.05),
              (sgn * 0.92, -0.30, 0.24), r * 0.82, r * 0.28, stem_c, tier="low",
              drop=0.05)
    face_on(P, b, (0, -r * 0.14, head_z), (r * 0.72, r * 0.70, r * 0.72), sep=0.54,
            dz=0.06, eye_r=0.078)


def deco_spring(c, cfg, g, H):                     # 生命之泉
    """弹簧圈身体（用弹性表达）。身体 = 一圈圈弹簧，腿 = 支撑的叶。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.26, 0.30, 0.32, 1.55, 0.32, wide=0.80)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    coils = 4
    z0 = hip * 0.82
    span = (TORSO_TOP * H - r * 0.90) - z0
    for i in range(coils):
        u = i / float(coils - 1)
        cz = z0 + span * u
        rr = r * (1.00 - 0.34 * u)
        cy(P, b, "Coil_%d" % i, (0, 0, cz), rr, H * 0.036, c["body"], tier="low",
           scale=(1.0, 0.62, 1.0))
        sp(P, b, "CoilEdge_%d" % i, (0, 0, cz), rr * 1.08, c["accent"], tier="micro",
           scale=(1.0, 0.62, 0.20))
    # 泉口：顶上一个小碗 + 一滴发光的水
    sp(P, b, "Bowl", (0, 0, z0 + span + r * 0.26), r * 0.54, c["accent"], tier="low",
       scale=(1.0, 0.92, 0.40))
    sp(P, b, "Water", (0, 0, z0 + span + r * 0.34), r * 0.44, c["body"], tier="low",
       scale=(1.0, 0.92, 0.34), emission=1.2)
    for i in range(3):
        a = i * 120.0
        sp(P, b, "Drop_%d" % i,
           (math.sin(D(a)) * r * 0.66, math.cos(D(a)) * r * 0.66, z0 + span + r * 0.62),
           r * 0.14, c["body"], tier="micro", scale=(0.8, 0.8, 1.3), emission=1.6)
    face_on(P, b, (0, 0, z0 + span * 0.30), (r * 1.00, r * 0.62, r * 0.42),
            dirv=(0.0, -1.0, 0.10), sep=0.50, dz=0.10, eye_r=0.076)


def deco_tree(c, cfg, g, H):                       # 圣树守卫
    """树冠 + 树干身体。树干是本体，树冠是它的上半身。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.34, 0.27, 0.50, 1.10, 0.40, wide=0.72, deep=0.72)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    trunk_top = hip + r * 1.60
    cfg["head"] = trunk_top + r * 0.10             # 树冠挂在这一圈上转
    # 树干：一段圆柱从髋上到 trunk_top（树干就是本体，不是"躯干"）
    cy(P, b, "TrunkBody", (0, 0, (hip * 0.62 + trunk_top) * 0.5), r * 0.46,
       H * 0.32, c["body"], tier="mid")
    for i, u in enumerate((0.28, 0.62)):
        cy(P, b, "Ring_%d" % i, (0, 0, hip * 0.92 + (trunk_top - hip * 0.92) * u),
           r * 0.48, H * 0.010, "BarkLite", tier="micro")
    # 板根：底部三块向外撑开的根（站得住的读法）
    for i, ang in enumerate((-58.0, 0.0, 58.0)):
        blade(P, b, "Buttress_%d" % i,
              (math.sin(D(ang)) * r * 0.26, math.cos(D(ang)) * r * 0.26, hip * 1.14),
              (math.sin(D(ang)) * 0.85, math.cos(D(ang)) * 0.85, -0.62), r * 1.10,
              r * 0.26, c["body"], tier="low", thick=0.85)
    # 树冠：三层叠球（层叠树冠是它的高度来源）—— 层偏移压在 TORSO_TOP 之内
    for i, (dz, rr, dxx, dyy) in enumerate(((0.00, 1.12, 0.00, 0.00),
                                            (0.28, 0.72, 0.34, -0.12),
                                            (0.52, 0.38, -0.28, 0.16))):
        sp(P, b, "Canopy_%d" % i, (dxx * r, dyy * r, trunk_top + dz * r), r * rr,
           c["accent"], tier="hero" if i == 0 else "mid", scale=(1.10, 1.04, 0.86))
    for i in range(5):
        a = i * 72.0 + 20.0
        sp(P, b, "Berry_%d" % i, (math.sin(D(a)) * r * 1.16, math.cos(D(a)) * r * 1.16,
                                  trunk_top - r * 0.10), r * 0.17, "Gold", tier="micro",
           emission=0.8)
    face_on(P, b, (0, 0, hip + r * 0.92), (r * 0.46, r * 0.46, r * 0.60), sep=0.62,
            dz=0.04, eye_r=0.070)


def deco_plague(c, cfg, g, H):                     # 疫病之母
    """
    大伞盖 + 垂落的孢子囊。比毒蘑菇更大更垂，下垂物是它的辨识度。

    ★ 剪影必须跟荆棘南瓜/苔藓盾分开 ★
    第一版是"又宽又高的大圆块 + 两条腿"，遮住颜色跟苔藓盾（IoU 0.795）、
    荆棘南瓜（0.757）分不出——那三个都是同一个形状。改法是把它拉成**伞**：
    菌柄又高又细（四节到 1.90r）、伞盖更宽更扁（半径 1.78r）。
    """
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.46, 0.22, 0.50, 0.60, 0.50)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 菌柄：四节、越往上越细，从髋一路顶到伞盖下面（细高是这把伞的关键）
    stack(P, b, "Stalk", [(0.30, 0.34, 0.62), (0.24, 0.86, 0.70), (0.19, 1.42, 0.66),
                          (0.15, 1.90, 0.62)],
          "Bone", "low", H)
    cap_z = TORSO_TOP * H - r * 1.30
    cap_r = r * 1.78
    sp(P, b, "Cap", (0, 0, cap_z), cap_r, c["body"], tier="hero", scale=(1.08, 1.04, 0.60))
    sp(P, b, "Cap_Skirt", (0, 0, cap_z - cap_r * 0.30), cap_r, c["body"], tier="mid",
       scale=(1.06, 1.02, 0.22))
    spots = int(c.get("spots", 8))
    for i in range(spots):
        a = (i / float(spots)) * 360.0 + 14.0
        rr = cap_r * (0.28 + 0.36 * ((i % 3) / 2.0))
        px, py = math.sin(D(a)) * rr, math.cos(D(a)) * rr
        pz = cap_z + cap_r * 0.50 * math.sqrt(max(0.02, 1.0 - (rr / cap_r) ** 2))
        sp(P, b, "Spot_%d" % i, (px, py, pz), cap_r * 0.14, c["accent"], tier="low",
           scale=(1.0, 1.0, 0.42))
    # 垂落的孢子囊：伞盖下缘挂下来的小袋（这是它跟毒蘑菇最大的区别）
    for i in range(6):
        a = i * 60.0 + 26.0
        px = math.sin(D(a)) * cap_r * 0.94
        py = math.cos(D(a)) * cap_r * 0.94
        pz = cap_z - cap_r * 0.62
        sp(P, b, "Sac_%d" % i, (px, py, pz), cap_r * 0.15, c["accent"], tier="tiny",
           scale=(1.0, 1.0, 1.5))
        cy(P, b, "SacStem_%d" % i, (px, py, pz + cap_r * 0.18), cap_r * 0.045,
           H * 0.026, "Glow", tier="micro", emission=0.9)
    face_on(P, b, (0, 0, hip + r * 1.10), (r * 0.42, r * 0.42, r * 0.80),
            dirv=(0.0, -1.0, -0.10), sep=0.66, dz=0.24, eye_r=0.072)


def deco_clover(c, cfg, g, H):                     # 治愈三叶草
    """
    三叶草头 + 柔和体型。三片心形叶是它的全部辨识度。

    上一版读成"几片散叶"：叶轴只有 0.52r、彼此离得远，茎又细又长。
    三叶草的可辨识性全在"**三片心形叶挤在一个叶柄顶上**"，所以这里改成
    矮壮的一株：叶柄从髋一路连到头，三片叶加大到 1.05r 并互相重叠，
    交汇处放一颗 accent 的小球当"花芯"，剪影才读得出三瓣花冠。
    """
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.34, 0.34, 0.34, 0.90, 0.30, wide=0.46)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    head_z = TORSO_TOP * H - r * 0.92
    # 叶柄：一根连续的茎，从髋长到头部（不是一小截浮在中间）
    cy(P, b, "Stem", (0, 0, (hip * 0.75 + head_z) * 0.5), r * 0.19,
       (head_z - hip * 0.75), c["accent"], tier="low")
    for i in range(3):
        a = i * 120.0 + 90.0
        d = (math.sin(D(a)) * 0.90, -0.18, math.cos(D(a)) * 0.90)
        base = (math.sin(D(a)) * r * 0.30, -r * 0.12, head_z + math.cos(D(a)) * r * 0.30)
        blade(P, b, "Clover_%d" % i, base, d, r * 1.05, r * 0.66, c["body"], tier="low",
              drop=0.02, thick=0.30)
        ev = _at(base, d, r * 0.88)
        sp(P, b, "Notch_%d" % i, ev, r * 0.26, c["accent"], tier="micro",
           scale=(1.0, 0.50, 0.80))
    sp(P, b, "Heart", (0, -r * 0.10, head_z - r * 0.10), r * 0.34, c["accent"],
       tier="low", scale=(1.0, 0.85, 0.75))
    for i in range(5):
        a = i * 72.0 + 36.0
        sp(P, b, "Halo_%d" % i, (math.sin(D(a)) * r * 0.80, math.cos(D(a)) * r * 0.80,
                                 head_z + r * 1.05), r * 0.15, "Gold", tier="micro",
           emission=1.4)
    face_on(P, b, (0, -r * 0.10, head_z - r * 0.10), (r * 0.34, r * 0.30, r * 0.26),
            sep=0.70, dz=0.20, eye_r=0.070)


def deco_colossus(c, cfg, g, H):                   # 铁根巨像
    """巨大根系 + 金属质感块面。**块面 + 根**，不是人形。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.36, 0.34, 0.60, 0.80, 0.55, wide=0.98, deep=0.88)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 底座：一块沉到髋下的宽板，把金属块面压在腿上
    # （原来最低的板在 hip+0.56r、bodymin 0.509 vs hip 0.609，整块浮着）
    bx(P, b, "Plate_Base", (0, 0, hip * 0.66), (r * 1.30, r * 1.10, r * 0.78),
       "MetalDk", rot=(0, D(4), 0))
    bx(P, b, "Plate_Low", (0, 0, hip + r * 0.56), (r * 1.42, r * 1.22, r * 0.86),
       c["body"], rot=(0, D(6), 0))
    bx(P, b, "Plate_Mid", (0, 0, hip + r * 1.62), (r * 1.66, r * 1.40, r * 0.94),
       c["body"], rot=(0, D(-5), 0))
    bx(P, b, "Plate_Top", (0, 0, hip + r * 2.52), (r * 1.18, r * 1.02, r * 0.60),
       "MetalDk", rot=(D(4), D(7), 0))
    for i, (dxx, dzz) in enumerate(((-0.52, 0.34), (0.52, 0.34), (-0.52, -0.30),
                                    (0.52, -0.30))):
        sp(P, b, "Rivet_%d" % i, (dxx * r, -r * 0.78, hip + r * (1.62 + dzz)), r * 0.16,
           "MetalDk", tier="micro", scale=(1.0, 0.50, 1.0))
    # 巨大根系：六条粗根从底部向外扎出（不接地，接地的是腿）
    for i in range(6):
        a = i * 60.0 + 30.0
        blade(P, b, "BigRoot_%d" % i,
              (math.sin(D(a)) * r * 0.62, math.cos(D(a)) * r * 0.62, hip + r * 0.56),
              (math.sin(D(a)) * 0.92, math.cos(D(a)) * 0.92, -0.62), r * 1.10, r * 0.24,
              c["accent"] if i % 2 else c["body"], tier="low", thick=0.90)
    for i, ang in enumerate((-38.0, 0.0, 38.0)):
        d = (math.sin(D(ang)) * 0.40, 0.0, 1.0)
        cn(P, b, "Crown_%d" % i, (math.sin(D(ang)) * r * 0.38, 0.0, hip + r * 2.34),
           r * 0.24, 0.004, H * 0.055, "MetalDk", tier="micro", rot=rot_of(d))
    face_rect(P, b, (0, -r * 0.74, hip + r * 1.62), (r * 1.60, r * 0.02, r * 0.84),
              sep=0.42, dz=0.18, eye_r=0.082)


def deco_moontree(c, cfg, g, H):                   # 月光树
    """层叠树冠（高度覆盖 1.72）。四层，比圣树守卫更多层、更冷色。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.28, 0.26, 0.45, 0.95, 0.35, wide=0.60, deep=0.60)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    trunk_top = hip + r * 1.30
    cfg["head"] = trunk_top + r * 0.10             # 四层树冠挂在这一圈上转
    # 树干从髋下 0.55 处起（不是 0.90）—— 起得太高，树身与腿之间会空一截
    cy(P, b, "TrunkBody", (0, 0, (hip * 0.30 + trunk_top) * 0.5), r * 0.38,
       (trunk_top - hip * 0.30), c["body"], tier="low")
    # 板根：树干下探到 0.30×hip 还不够，加四块外撑的根把树身压在腿上
    for i, ang in enumerate((-70.0, 0.0, 70.0, 160.0)):
        blade(P, b, "FlareRoot_%d" % i,
              (math.sin(D(ang)) * r * 0.28, math.cos(D(ang)) * r * 0.28, hip * 1.05),
              (math.sin(D(ang)) * 0.90, math.cos(D(ang)) * 0.90, -0.70), r * 0.86,
              r * 0.22, c["body"], tier="low", thick=0.85)
    for i, ang in enumerate((-64.0, 0.0, 64.0, 150.0)):
        blade(P, b, "MoonRoot_%d" % i,
              (math.sin(D(ang)) * r * 0.26, math.cos(D(ang)) * r * 0.26, hip * 1.10),
              (math.sin(D(ang)) * 0.90, math.cos(D(ang)) * 0.90, -0.70), r * 0.92,
              r * 0.22, c["body"], tier="low", thick=0.85)
    # 四层树冠：越上面越小、层间错位（"层叠"的读法，也是它跟圣树守卫的区别）
    # 层偏移压得比圣树守卫紧：四层 + 月亮的自然高要落在 TORSO_TOP 附近，
    # 否则整株会被"缩到 1.72"压扁（实测过 0.49 的比例，树冠被压成一张饼）。
    layers = ((0.00, 1.15, 0.00, 0.00, "hero"), (0.20, 0.86, 0.32, -0.14, "mid"),
              (0.38, 0.60, -0.28, 0.18, "mid"), (0.56, 0.30, 0.14, 0.10, "low"))
    for i, (dz, rr, dxx, dyy, tier) in enumerate(layers):
        sp(P, b, "Canopy_%d" % i, (dxx * r, dyy * r, trunk_top + dz * r), r * rr,
           c["accent"], tier=tier, scale=(1.12, 1.06, 0.80))
    sp(P, b, "Moon", (-r * 0.50, -r * 0.96, trunk_top + r * 0.92), r * 0.26, "Moon",
       tier="low", scale=(1.0, 0.55, 1.0), emission=1.8)
    for i in range(4):
        a = i * 90.0 + 30.0
        sp(P, b, "Spark_%d" % i, (math.sin(D(a)) * r * 1.00, math.cos(D(a)) * r * 1.00,
                                  trunk_top + r * (0.30 + 0.30 * i)), r * 0.13, "Moon",
           tier="micro", emission=1.6)
    face_on(P, b, (0, 0, hip + r * 0.86), (r * 0.36, r * 0.36, r * 0.60), sep=0.64,
            dz=0.04, eye_r=0.066)


def deco_char(c, cfg, g, H):                       # 炭花
    """花瓣头 + 焦黑躯干（会死亡爆炸）。焦黑 = 暗哑块 + 两道还在发光的裂口。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.30, 0.28, 0.30, 0.95, 0.32, wide=0.40)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 焦黑躯干：三段烧焦的短茎（暗、哑、略歪）
    for i, (dz, rr, dx) in enumerate(((0.34, 0.72, 0.00), (0.92, 0.58, 0.14),
                                      (1.44, 0.46, -0.10))):
        sp(P, b, "Char_%d" % i, (dx * r, 0, hip + dz * r * 0.86), r * rr,
           "Ink" if i % 2 == 0 else "EmberDk", tier="low", scale=(1.0, 0.94, 0.74),
           rot=(0, D(6 * (1 if i % 2 else -1)), 0))
    for i, (dxx, dzz) in enumerate(((-0.50, 0.92), (0.56, 0.42))):
        sp(P, b, "Ember_%d" % i, (dxx * r, -r * 0.62, hip + dzz * r), r * 0.30,
           "Ember", tier="micro", scale=(1.10, 0.42, 0.66), emission=1.4)
    head_z = TORSO_TOP * H - r * 1.02
    petals = int(c.get("petals", 8))
    for i in range(petals):
        a = (i / float(petals)) * 360.0
        d = (math.sin(D(a)) * 0.94, -0.18, math.cos(D(a)) * 0.94)
        base = (math.sin(D(a)) * r * 0.52, -r * 0.10, head_z + math.cos(D(a)) * r * 0.52)
        blade(P, b, "CharPetal_%d" % i, base, d, r * 0.78, r * 0.40, c["body"],
              tier="low", thick=0.34)
    sp(P, b, "Core", (0, -r * 0.26, head_z), r * 0.46, "EmberDk", tier="low",
       scale=(1.0, 0.58, 1.0), emission=0.5)
    face_on(P, b, (0, -r * 0.16, head_z), (r * 0.74, r * 0.70, r * 0.74), sep=0.56,
            dz=0.06, eye_r=0.078)


def deco_chili(c, cfg, g, H):                      # 辣椒法师
    """细长辣椒身体 + 环状花瓣领。身体就是一根辣椒，而且是弯的。"""
    b, P = cfg["bud"], cfg["parts"]
    # 辣椒要**细长一根**：身体段拉长（1.145→1.72）、半径收窄（mass 0.30→0.26）
    hip, blobs, r = lay_species(H, 0.44, 0.26, 0.22, 1.72, 0.22, wide=0.40)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    N = 4
    bot, top = hip + r * 0.30, TORSO_TOP * H - r * 0.85
    for i in range(N):
        u = i / float(N - 1)
        bend = math.sin(u * math.pi * 0.85) * r * 0.86
        rr = r * (0.40 + 0.52 * math.sin(min(1.0, u + 0.22) * math.pi * 0.90))
        sp(P, b, "Pepper_%d" % i, (bend, -bend * 0.22, bot + (top - bot) * u), rr,
           c["body"] if i < N - 1 else "EmberDk", tier="mid",
           scale=(1.0, 0.92, 1.24 - 0.10 * i))
    for i in range(5):
        a = i * 72.0 + 18.0
        blade(P, b, "Calyx_%d" % i,
              (math.sin(D(a)) * r * 0.26, math.cos(D(a)) * r * 0.26, top + r * 0.30),
              (math.sin(D(a)) * 0.72, math.cos(D(a)) * 0.72, 0.62), r * 0.50, r * 0.16,
              c["accent"], tier="micro")
    # 环状花瓣领：腰上的一圈（法师的辨识度）
    ring_z = hip + (top - hip) * 0.40
    petals = 9
    for i in range(petals):
        a = (i / float(petals)) * 360.0
        d = (math.sin(D(a)) * 0.95, -0.10, 0.30 + 0.16 * (i % 2))
        base = (math.sin(D(a)) * r * 0.50, math.cos(D(a)) * r * 0.50, ring_z)
        blade(P, b, "Collar_%d" % i, base, d, r * 0.90, r * 0.28, c["accent"], tier="low",
              thick=0.30)
    sp(P, b, "ChiliSpark", (0, 0, top + r * 0.62), r * 0.22, "Ember", tier="micro",
       emission=1.8)
    face_on(P, b, (0, 0, top - r * 0.85), (r * 0.74, r * 0.72, r * 0.74),
            dirv=(0.0, -1.0, 0.14), sep=0.52, dz=0.08, eye_r=0.074)


def deco_starvine(c, cfg, g, H):                   # 星藤
    """星形叶片 + 缠绕的藤。星的角就是它的剪影。"""
    b, P = cfg["bud"], cfg["parts"]
    hip, blobs, r = lay_species(H, 0.24, 0.24, 0.26, 1.40, 0.26, wide=0.36)
    cfg["hip"] = hip
    g["hip"] = hip
    g["r"] = r
    # 缠绕的藤：绕中轴一圈半的螺旋（用细杆拼，段数按高度定）
    segs = 12
    z0, z1 = hip + r * 0.30, TORSO_TOP * H - r * 0.80
    prev = None
    for i in range(segs):
        u = i / float(segs - 1)
        a = D(-40.0 + 430.0 * u)
        rr = r * (1.00 - 0.32 * u)
        p = mathutils.Vector((math.sin(a) * rr, math.cos(a) * rr * 0.72, z0 + (z1 - z0) * u))
        if prev is not None:
            mid = (prev + p) * 0.5
            cy(P, b, "Helix_%d" % i, (mid.x, mid.y, mid.z), r * 0.16,
               (p - prev).length * 0.64, c["accent"], tier="micro", rot=rot_of(p - prev))
        prev = p
    cy(P, b, "Axis", (0, 0, (z0 + z1) * 0.5), r * 0.18, (z1 - z0) * 0.5, c["body"],
       tier="low")
    star_z = TORSO_TOP * H - r * 1.20
    for i in range(5):
        a = i * 72.0 + 90.0
        d = (math.sin(D(a)) * 0.92, -0.18, math.cos(D(a)) * 0.92)
        base = (math.sin(D(a)) * r * 0.34, -r * 0.10, star_z + math.cos(D(a)) * r * 0.34)
        blade(P, b, "Star_%d" % i, base, d, r * 1.05, r * 0.42, c["body"], tier="low",
              thick=0.34)
    sp(P, b, "StarCore", (0, -r * 0.20, star_z), r * 0.46, "Moon", tier="low",
       scale=(1.0, 0.62, 1.0), emission=0.6)
    for i in range(4):
        a = i * 90.0 + 26.0
        sp(P, b, "Gleam_%d" % i, (math.sin(D(a)) * r * 0.94, math.cos(D(a)) * r * 0.94,
                                  hip + (z1 - hip) * (0.30 + 0.18 * i)), r * 0.12, "Moon",
           tier="micro", emission=1.5)
    face_on(P, b, (0, 0, hip + r * 1.40), (r * 0.30, r * 0.30, r * 0.30),
            dirv=(0.0, -1.0, 0.20), sep=0.84, dz=0.10, eye_r=0.068)


DECOS = {
    "root": deco_root, "shooter": deco_shooter, "fungus": deco_fungus,
    "ivy": deco_ivy, "gourd": deco_gourd, "rock": deco_rock, "moss": deco_moss,
    "flower": deco_flower, "spring": deco_spring, "tree": deco_tree,
}
# 同 kind 但造型完全不同的品种：按 **id** 覆盖分派（这才是"每株独立"）
DECOS_BY_ID = {
    "plague_mother": deco_plague,
    "healing_clover": deco_clover,
    "moontree": deco_moontree,
    "char_flower": deco_char,
    "chili_mage": deco_chili,
    "starvine": deco_starvine,
    "ironroot_colossus": deco_colossus,
}


# ---------------------------------------------------------------- 每品种参数
# 腿：长宽 / 分叉 / 外张 / 垂向 / 叶尖配色 —— "两片叶当腿"的差异化就在这里。
# 全部相对 H 写（leg_w / leg_drop / leg_tip_y 都是"占身高多少"）。
LEGS = {
    "radish_guard":   dict(leg_x=0.070, leg_w=0.092, leg_thick=0.50, leg_splay=0.20,
                           leg_drop=0.008, leg_tip_w=0.74),
    "pea_shooter":    dict(leg_x=0.058, leg_w=0.072, leg_thick=0.42, leg_splay=0.26,
                           leg_drop=0.028, leg_tip_fwd=-0.42),
    "toxic_mushroom": dict(leg_x=0.062, leg_w=0.070, leg_thick=0.44, leg_splay=0.14,
                           leg_drop=0.018, leg_c1="Bone", leg_c2="Bone"),
    "frost_ivy":      dict(leg_x=0.054, leg_w=0.058, leg_thick=0.66, leg_splay=0.30,
                           leg_drop=0.036, leg_c1="Icicle", leg_c2="LeafDark",
                           leg_tier="mid"),
    "thorn_pumpkin":  dict(leg_x=0.064, leg_w=0.076, leg_thick=0.48, leg_splay=0.18,
                           leg_drop=0.017, leg_c1="LeafDim", leg_c2="LeafDim"),
    "stone_fist":     dict(leg_x=0.082, leg_w=0.094, leg_thick=0.92, leg_splay=0.22,
                           leg_drop=0.004, leg_c1="Stoner", leg_c2="StoneDk"),
    "chili_mage":     dict(leg_x=0.052, leg_w=0.064, leg_thick=0.40, leg_splay=0.28,
                           leg_drop=0.031, leg_c1="LeafDim", leg_c2="EmberDk"),
    "starvine":       dict(leg_x=0.052, leg_w=0.060, leg_thick=0.60, leg_splay=0.32,
                           leg_drop=0.035, leg_c1="LeafDim", leg_c2="Moon",
                           leg_tier="mid"),
    "moontree":       dict(leg_x=0.065, leg_w=0.080, leg_thick=0.78, leg_splay=0.20,
                           leg_drop=0.012, leg_c1="Bark", leg_c2="BarkLite"),
    "char_flower":    dict(leg_x=0.059, leg_w=0.068, leg_thick=0.44, leg_splay=0.24,
                           leg_drop=0.024, leg_c1="Ink", leg_c2="EmberDk"),
    "plague_mother":  dict(leg_x=0.066, leg_w=0.074, leg_thick=0.46, leg_splay=0.16,
                           leg_drop=0.020, leg_c1="Bone", leg_c2="Bone"),
    "moss_shield":    dict(leg_x=0.057, leg_w=0.070, leg_thick=0.90, leg_splay=0.26,
                           leg_drop=0.011, leg_c1="Moss", leg_c2="Stoner",
                           leg_tier="micro"),
    "healing_clover": dict(leg_x=0.057, leg_w=0.074, leg_thick=0.42, leg_splay=0.26,
                           leg_drop=0.023),
    "life_spring":    dict(leg_x=0.063, leg_w=0.076, leg_thick=0.54, leg_splay=0.22,
                           leg_drop=0.016, leg_c1="Stoner", leg_c2="Water"),
    "holy_tree_guard": dict(leg_x=0.069, leg_w=0.088, leg_thick=0.86, leg_splay=0.18,
                            leg_drop=0.007, leg_c1="Bark", leg_c2="BarkLite"),
    "ironroot_colossus": dict(leg_x=0.077, leg_w=0.098, leg_thick=0.94, leg_splay=0.20,
                              leg_drop=0.002, leg_c1="Stoner", leg_c2="MetalDk"),
}

# 手臂：**视情况而定**。没有的品种就是没有
# （萝卜/蘑菇/寒霜藤/南瓜/星藤/月桂/炭花/三叶草/生命之泉/苔藓/疫病之母 一个都没有：
#   它们要么不攻击，要么攻击靠身体——不为对称硬加）
ARMS = {
    "pea_shooter": dict(shoulder=(0.20, 0.04, 0.72), dir=(0.44, -0.78, 0.42), len=0.28,
                        w=0.068, tier="low", tip="hand", tip_r=0.055, drop=0.02,
                        color="LeafDim", tipcolor="Leaf"),
    "stone_fist": dict(shoulder=(0.62, -0.08, 0.86), dir=(0.34, -0.74, 0.58), len=0.32,
                       w=0.078, tier="low", tip="fist", tip_r=0.104, drop=0.0,
                       color="Moss", tipcolor="Stoner", thick=0.72),
    "chili_mage": dict(shoulder=(0.16, -0.04, 0.78), dir=(0.52, -0.66, 0.54), len=0.34,
                       w=0.060, tier="low", tip="hand", tip_r=0.058, drop=0.03,
                       color="LeafDim", tipcolor="Ember", thick=0.44),
    "holy_tree_guard": dict(shoulder=(0.28, -0.03, 0.80), dir=(0.40, -0.72, 0.56),
                            len=0.36, w=0.082, tier="low", tip="fist", tip_r=0.090,
                            drop=0.03, color="Bark", tipcolor="BarkLite", thick=0.62),
    "ironroot_colossus": dict(shoulder=(0.42, -0.05, 0.88), dir=(0.32, -0.68, 0.66),
                              len=0.38, w=0.108, tier="low", tip="fist", tip_r=0.122,
                              drop=0.0, color="MetalDk", tipcolor="Stoner", thick=0.80),
}

# 头节点：只有"树冠类"需要单独起一个可以点头的组（层叠树冠晃起来是它的读法）
HEAD_FOR = {"moontree", "holy_tree_guard"}

# 身体形状：只给**真正不同的量** —— 髋高占身高比（=腿长）+ 三段纵向配比 + 整体胖瘦。
# 这里每一项都能从剪影上看出来：萝卜是矮胖块根、辣椒是细高一条、苔藓是坐地的绒球。
BODY = {
    #                                    hip   mass  bot   mid   top
    "radish_guard":   dict(hip=0.255, mass=0.44, s=(0.62, 0.70, 0.56)),
    "pea_shooter":    dict(hip=0.300, mass=0.23, s=(0.42, 1.55, 0.40)),
    "toxic_mushroom": dict(hip=0.340, mass=0.28, s=(0.52, 0.52, 0.50)),
    "frost_ivy":      dict(hip=0.260, mass=0.26, s=(0.30, 1.30, 0.40)),
    "thorn_pumpkin":  dict(hip=0.255, mass=0.46, s=(0.60, 0.62, 0.52)),
    "stone_fist":     dict(hip=0.340, mass=0.26, s=(0.55, 0.75, 0.55)),
    "chili_mage":     dict(hip=0.260, mass=0.30, s=(0.28, 1.30, 0.28)),
    "starvine":       dict(hip=0.240, mass=0.24, s=(0.26, 1.40, 0.26)),
    "moontree":       dict(hip=0.280, mass=0.26, s=(0.45, 0.95, 0.35)),
    "char_flower":    dict(hip=0.280, mass=0.30, s=(0.30, 0.95, 0.32)),
    "plague_mother":  dict(hip=0.420, mass=0.30, s=(0.60, 0.70, 0.55)),
    "moss_shield":    dict(hip=0.315, mass=0.50, s=(0.60, 0.72, 0.50)),
    "healing_clover": dict(hip=0.200, mass=0.32, s=(0.26, 0.50, 0.26)),
    "life_spring":    dict(hip=0.260, mass=0.30, s=(0.30, 1.40, 0.30)),
    "holy_tree_guard": dict(hip=0.340, mass=0.27, s=(0.50, 1.10, 0.40)),
    "ironroot_colossus": dict(hip=0.360, mass=0.34, s=(0.60, 0.80, 0.55)),
}

FLOWER_COLORS = {
    "moontree":       dict(_stem="Bark", _disc="Bark", _core="Glow"),
    "char_flower":    dict(_stem="Ink", _disc="EmberDk", _core="Ember"),
    "healing_clover": dict(_stem="LeafDim", _disc="LeafDim", _core="Gold"),
}


def height_of(c, pid=None):
    """总高：size 线性映射到 [1.62, 1.79]，个别品种钉死。"""
    if pid and pid in H_OVERRIDE:
        return H_OVERRIDE[pid]
    u = (c["size"] - S_MIN) / (S_MAX - S_MIN)
    u = max(0.0, min(1.0, u))
    return H_MIN + (H_MAX - H_MIN) * u


def group_of(name):
    """
    部件名 → 木偶分组。命名后缀就是分组，不做别的推断。

    分组只有 7 个（Body / Leg_L / Leg_R / Arm_L / Arm_R / Head / Shadow），
    **16 个品种共用同一套节点名** —— Unity 侧 `AnimationMerge` 的路径绑定与
    `ModelView` 的 State→Clip 映射只需要一套；"每个品种不同"体现在**几何与动画配方**上，
    而不是节点名上（节点名每品种一套的话，合并器就得写 16 个特例，那不叫独特叫散乱）。
    """
    if name.startswith(("LegBlade_L", "LegTip_L")):
        return "Leg_L"
    if name.startswith(("LegBlade_R", "LegTip_R")):
        return "Leg_R"
    if name.startswith(("ArmBlade_R", "Finger_R", "StoneFist_R")):
        return "Arm_R"
    if name.startswith(("ArmBlade_L", "Finger_L", "StoneFist_L")):
        return "Arm_L"
    if name.startswith("Canopy"):
        return "Head"
    if name.startswith("Ground_Shadow"):
        return "Shadow"
    return "Body"
#@GEOM_END


def height_of(c, pid=None):
    """总高：size 线性映射到 [1.62, 1.79]，个别品种钉死。"""
    if pid and pid in H_OVERRIDE:
        return H_OVERRIDE[pid]
    u = (c["size"] - S_MIN) / (S_MAX - S_MIN)
    u = max(0.0, min(1.0, u))
    return H_MIN + (H_MAX - H_MIN) * u


def group_of(name):
    """
    部件名 → 木偶分组。命名后缀就是分组，不做别的推断。

    分组只有 7 个（Body / Leg_L / Leg_R / Arm_L / Arm_R / Head / Shadow），
    **16 个品种共用同一套节点名** —— Unity 侧 `AnimationMerge` 的路径绑定与
    `ModelView` 的 State→Clip 映射只需要一套；"每个品种不同"体现在**几何与动画配方**上，
    而不是节点名上（节点名每品种一套的话，合并器就得写 16 个特例，那不叫独特叫散乱）。
    """
    if name.startswith(("LegBlade_L", "LegTip_L")):
        return "Leg_L"
    if name.startswith(("LegBlade_R", "LegTip_R")):
        return "Leg_R"
    if name.startswith(("ArmBlade_R", "Finger_R", "StoneFist_R")):
        return "Arm_R"
    if name.startswith(("ArmBlade_L", "Finger_L", "StoneFist_L")):
        return "Arm_L"
    if name.startswith("Canopy"):
        return "Head"
    if name.startswith("Ground_Shadow"):
        return "Shadow"
    return "Body"


# ================================================================ 造型组装
def soft_reset():
    """
    清空场景，**不用 bpy.ops**。

    为什么要自己写一个：`kit.reset()` 走 `bpy.ops.object.delete`，它对刚才造出来的
    部件对象做了**真正的销毁**，那些 Python 引用全变成悬空指针（dangling StructRNA）。
    本项目实测过一次：`kit.reset()` 之后再用旧的 parts 列表调 `kit.join()`，
    报 `ReferenceError: StructRNA of type Object has been removed`。

    改用 `bpy.data.objects.remove()` 直接删数据块，Python 端行为可预期；
    mesh / material / action 的清理照旧（动作必须清，否则多次构建会累积 take）。

    ★ 还必须清 kit 的材质缓存 ★
    `kit._MAT_CACHE` 存的是 Material 数据块的 Python 引用。我们刚把 material 数据块
    删了，缓存里就是悬空引用——下一个品种调 `kit.mat()` 拿到它、赋给新物体时抛
    `ReferenceError: StructRNA of type Material has been removed`
    （实测：radish_guard 建得出来，第二个品种必崩）。`kit.reset()` 里本来有这一句，
    自己写 reset 就得自己补上。
    """
    for o in list(bpy.data.objects):
        try:
            bpy.data.objects.remove(o, do_unlink=True)
        except Exception:                                   # noqa: BLE001
            pass
    for blk in (bpy.data.meshes, bpy.data.materials, bpy.data.actions):
        for item in list(blk):
            try:
                blk.remove(item)
            except Exception:                               # noqa: BLE001
                pass
    kit._MAT_CACHE = {}


def _grow(pid, demote=None, fine=1.0):
    """
    造出全部部件（不合并）+ 关节 + 配方。返回 (parts, cfg, joints, H)。

    位置全部相对 H 写（见 deco_* 与 BODY 表），所以**没有"竖向比例校正"这一步**：
    自然高从构造上就等于目标高。上一版靠"最后整体缩到 1.7"硬凑，
    结果 2.69m 的巨像被压成矮胖块 —— 那是把比例一起缩掉了，不是一个修法。
    """
    global DEMOTE, FINE
    c = dict(PLANTS[pid])
    c.update(FLOWER_COLORS.get(pid, {}))
    H = height_of(c, pid)
    g = dict(BODY[pid])

    cfg = dict(c)
    cfg["bud"] = Budget(TRI_TARGET)
    cfg.update(base_leg_cfg(c))
    cfg.update(LEGS.get(pid, {}))
    cfg["parts"] = []
    DEMOTE, FINE = (demote or {}), fine
    try:
        DECOS_BY_ID.get(pid, DECOS[c["kind"]])(c, cfg, g, H)
        # 造型函数把算出来的关键位置写回 g，这里搬到 cfg 上（腿/手臂/木偶都读 cfg）
        for k in ("hip", "r", "head"):
            if k in g:
                cfg[k] = g[k]
        joints = legs(cfg["parts"], cfg, g, H)
        if pid in ARMS:
            for side in (-1, 1):
                nm, at = leaf_arm(cfg["parts"], cfg, g, side, dict(ARMS[pid]), H)
                joints[nm] = at
        cy(cfg["parts"], cfg["bud"], "Ground_Shadow", (0, 0.006, 0.006), H * 0.145,
           0.012, "Ink", tier="low", scale=(0.62, 1.05, 1.0), smooth=False)
        joints["Shadow"] = (0.0, 0.0, 0.0)
    finally:
        DEMOTE, FINE = {}, 1.0
    return cfg["parts"], cfg, joints, H


def _geo_height(parts):
    """
    量部件并集的最高点，**排除接地阴影**。

    阴影是压扁的圆柱、本来就贴着地，它的包围盒 z 是 −0.006~0.018；
    更关键的是它跟"身体高度"没关系，算进去会让读数偏一点点。
    """
    top = 0.0
    for p in parts:
        if p.name.startswith("Ground_Shadow"):
            continue
        top = max(top, max((p.matrix_world @ mathutils.Vector(b)).z for b in p.bound_box))
    return top


# 全局细分系数的候选（从"原样"往下试，取第一个进预算的）
FINE_STEPS = (1.00, 0.88, 0.78, 0.70, 0.62, 0.55, 0.48)


# 高度区间（跟常量 H_MIN/H_MAX 的映射目标是同一件事，但这里是**落盘时的验收区间**）
H_OK_LO, H_OK_HI = 1.62, 1.79


def _residual_scale(h):
    """
    自然高 h → 需要的缩放系数。

    ★ 为什么不总是缩到目标值 ★
    每个品种的"目标高"由 size 映射得到（1.62~1.79），但造型的自然高会落在它附近而不是
    正好相等。上一版无条件 `k = H/h`，于是 1.751 的圣树被拉大 1.6%、1.596 的寒霜藤被
    拉大 1.5% —— 比例平白被改，而且"残差"这个诊断量永远是 1.000，等于把信息抹掉了。
    现在只在**真的越界**时收进区间内侧，其余时候一个顶点都不动。
    """
    if h <= 1e-6:
        return 1.0
    if h > H_OK_HI:
        return H_OK_HI / h
    if h < H_OK_LO:
        return H_OK_LO / h
    return 1.0


def build_parts(pid):
    """
    收敛三角面预算：先降次级件的档，不够就整体收窄细分（FINE）。
    主体块（块根/豆荚/伞盖/瓜/石块/树干/金属板）永远不降档：
    降它们等于把辨识度一起降掉，那还不如少两颗石子。

    返回 (parts, cfg, joints, H, scale)，`scale` 是"自然高 → 目标高"的残差
    （H-relative 布局下它应该非常接近 1；不为 1 说明某个品种的零件顶出了 TORSO_TOP）。
    """
    demote, fine = {}, 1.0
    parts = cfg = joints = None
    H = 0.0
    for _round in range(12):
        soft_reset()
        parts, cfg, joints, H = _grow(pid, demote=demote, fine=fine)
        if cfg["bud"].used > TRI_TARGET:
            touched = cfg["bud"].demote_pass()
            if touched:
                for nm in touched:
                    demote[nm] = demote.get(nm, 0) + 1
                continue
            i = FINE_STEPS.index(fine) + 1 if fine in FINE_STEPS else len(FINE_STEPS) - 1
            if i >= len(FINE_STEPS):
                break                       # 到底了，交给出口断言报错
            fine = FINE_STEPS[i]
            continue
        break
    top = _geo_height(parts)
    scale = _residual_scale(top)
    print("[SEEDFALL-ALLY]   %-18s 自然高 %.4f 目标 %.4f 残差 %.4f fine=%.2f 降档=%d"
          % (pid, top, H, scale, fine, len(demote)))
    return parts, cfg, joints, H, scale


def build(pid, preview=False, out_dir=None):
    """一整个成熟模型：造零件 → 合并 → 高度校正 → 原点归脚底 → 导出 FBX。"""
    parts, cfg, joints, H, scale = build_parts(pid)

    obj = kit.join(parts, "PL_" + pid)
    kit.apply_transforms(obj)

    # ① 高度校正：**只在越界时才改**（见 _residual_scale 的说明）
    k = _residual_scale(obj.dimensions.z)
    if abs(k - 1.0) > 1e-6:
        obj.scale = (k, k, k)
        kit.apply_transforms(obj)

    # ② 抬到踩地：zmin 归零
    # 注意这一步**改变了"髋高"的可读含义**：抬平之后坐在 z=0 上的是最低的那个零件
    # （可能是鼓出来的身体下缘，也可能是叶尖）。所以矮胖品种（苔藓盾的绒球下缘几乎贴地）
    # 抬平后腿会略显短——那是这个造型本来就该有的读法，不是 bug。
    z0 = min((obj.matrix_world @ mathutils.Vector(b)).z for b in obj.bound_box)
    obj.location.z -= z0
    kit.apply_transforms(obj)
    zmin = min((obj.matrix_world @ mathutils.Vector(b)).z for b in obj.bound_box)
    maxsag = 0.11 * H          # 允许身体下缘/叶尖比脚底低一点（叶子搭在地上是正常的）

    # ③ 原点 = 脚底中心
    kit.set_origin(obj, (0.0, 0.0, 0.0))

    dims = tuple(round(v, 4) for v in obj.dimensions)
    tris = kit.tri_count(obj)
    mats = [m.name for m in obj.data.materials if m]
    path = out_path(pid, out_dir)

    bad = [m for m in mats if m not in kit.PALETTE]
    assert not bad, "%s: 非调色板材质名 %s" % (pid, bad)
    assert abs(zmin) < 1e-4, "%s: 抬平之后 zmin=%.5f（应当正好是 0）" % (pid, zmin)
    assert z0 > -maxsag, "%s: 抬平前下沉 %.4f 超过 %.4f（腿尖离地太远，是造型问题）" \
        % (pid, z0, maxsag)
    assert 1.58 <= dims[2] <= 1.82, "%s: 高度越界 %.4f" % (pid, dims[2])
    assert tris < TRI_BUDGET, "%s: 三角面超预算 %d" % (pid, tris)

    kit.export_fbx(obj, path)          # ★ bake_yup=False：轴向只由导出器转一次 ★
    drop_meta(path)
    print("[SEEDFALL-ALLY] %-18s H=%.3f W=%.3f D=%.3f tris=%4d mats=%d -> %s"
          % (pid, dims[2], dims[0], dims[1], tris, len(mats), os.path.basename(path)))
    if preview:
        _preview(os.path.join(PREVIEW_DIR, "ally_%s_front.png" % pid), H)
    return dict(pid=pid, dims=dims, tris=tris, materials=mats, zmin=round(zmin, 5),
                sag=round(z0, 5), height=round(H, 3), path=path, kind=PLANTS[pid]["kind"])


def out_path(pid, out_dir=None):
    path = os.path.join(out_dir or PLANT_DIR, pid + ".fbx")
    for bad in PROTECTED_SUFFIXES:
        assert not path.endswith(bad), "拒绝写入受保护文件: " + path
    assert os.path.basename(path) == pid + ".fbx", "拒绝写入非成熟形态: " + path
    return path


def drop_meta(path):
    """删掉 Unity 的 .fbx.meta，强制重导（同名 FBX 内容变了但 meta 还在时不会重导）。"""
    meta = path + ".meta"
    if os.path.exists(meta):
        os.remove(meta)
        print("[SEEDFALL-ALLY]   已删除 %s" % os.path.basename(meta))


def _preview(path, H):
    kit.ensure_dir(path)
    try:
        return kit.render_preview(path, (0, 0, H * 0.5), H * 0.55, size=(420, 560),
                                  angle=22.0, elev=8.0, ambient=0.62)
    except Exception as e:                                  # noqa: BLE001
        print("[SEEDFALL-ALLY] ★预览失败 %s: %s★" % (path, e))
        return None


# ================================================================ 动画配方
# 移动配方：**16 份，一份一个品种**。改一个品种不会影响别的品种。
#
#   wave     step=顿步  swell=缓起缓落  bounce=弹跳  lurch=前滑后拽
#            march=机械抬起  sine=平滑  roll=滚动（腿收起，靠身体转）
#   leg_deg  腿前后摆的幅度（度）      phase  左右腿相差的周期比例（0.5 = 反相）
#   cyc      一个周期走几步            lift   抬起相位的离地量（米）
#   lean/bob/bob_cyc/roll/twist/squash/glid/head  躯干与整体位移
WALK = {
    # 沉重的顿步：方波（一步一顿）、脚抬得高、躯干左右砸
    "radish_guard": dict(wave="step", leg_deg=27.0, phase=0.5, cyc=1, lift=0.052,
                         lean=4.0, bob=0.030, bob_cyc=2, roll=4.2, twist=3.0,
                         squash=0.055, head=0.0, glid=0.0),
    # 轻快的小碎步：两周期、脚几乎不离地、躯干弹得高
    "pea_shooter": dict(wave="swell", leg_deg=22.0, phase=0.5, cyc=2, lift=0.028,
                        lean=6.5, bob=0.046, bob_cyc=4, roll=2.4, twist=4.4,
                        squash=0.040, head=0.0, glid=0.0),
    # 摇晃的摆步：左右大幅度侧倾
    "toxic_mushroom": dict(wave="swell", leg_deg=18.0, phase=0.5, cyc=1, lift=0.036,
                           lean=3.0, bob=0.034, bob_cyc=2, roll=7.4, twist=3.6,
                           squash=0.048, head=0.0, glid=0.0),
    # 蜿蜒滑行：几乎不摆腿（±9°），靠整体蛇行滑移
    "frost_ivy": dict(wave="sine", leg_deg=9.0, phase=0.25, cyc=1, lift=0.010,
                      lean=2.0, bob=0.020, bob_cyc=1, roll=6.0, twist=9.5,
                      squash=0.030, head=0.0, glid=0.030),
    # 前扑式重步：整体前冲，躯干大幅前倾
    "thorn_pumpkin": dict(wave="lurch", leg_deg=21.0, phase=0.5, cyc=1, lift=0.040,
                          lean=9.0, bob=0.038, bob_cyc=2, roll=5.2, twist=5.0,
                          squash=0.062, head=0.0, glid=0.024),
    # 碾步：方波化的机械抬起 + 躯干几乎不晃
    "stone_fist": dict(wave="march", leg_deg=24.0, phase=0.5, cyc=1, lift=0.046,
                       lean=4.5, bob=0.026, bob_cyc=2, roll=1.4, twist=1.2,
                       squash=0.030, head=0.0, glid=0.0),
    # 飘行：腿几乎不动、身体整体前飘 + 大幅扭转
    "chili_mage": dict(wave="sine", leg_deg=11.0, phase=0.5, cyc=1, lift=0.014,
                       lean=3.4, bob=0.024, bob_cyc=2, roll=3.0, twist=8.0,
                       squash=0.026, head=0.0, glid=0.034),
    # 螺旋轻跳：单周期双跳 + 绕轴扭转（星的读法）
    "starvine": dict(wave="bounce", leg_deg=19.0, phase=0.5, cyc=2, lift=0.062,
                     lean=1.4, bob=0.052, bob_cyc=4, roll=3.6, twist=13.0,
                     squash=0.070, head=0.0, glid=0.0),
    # 缓慢庄严的 3/4 拍：一个周期 3 次起伏，树冠跟着点
    "moontree": dict(wave="swell", leg_deg=15.0, phase=0.5, cyc=1, lift=0.030,
                     lean=1.6, bob=0.030, bob_cyc=3, roll=3.4, twist=5.0,
                     squash=0.032, head=5.4, glid=0.0),
    # 急促的碎步（急着打）：四步一周期、起伏小、扭转大
    "char_flower": dict(wave="step", leg_deg=23.0, phase=0.5, cyc=4, lift=0.032,
                        lean=7.0, bob=0.028, bob_cyc=8, roll=4.0, twist=7.5,
                        squash=0.036, head=0.0, glid=0.0),
    # 感染性的蹒跚：左右**不对称**（相位 0.62，不是标准反相）
    "plague_mother": dict(wave="lurch", leg_deg=16.0, phase=0.62, cyc=1, lift=0.026,
                          lean=5.0, bob=0.058, bob_cyc=3, roll=8.0, twist=4.0,
                          squash=0.052, head=0.0, glid=0.012),
    # **滚动**：腿收起、身体整圈滚（唯一用 roll 波形的品种）
    "moss_shield": dict(wave="roll", leg_deg=6.0, phase=0.5, cyc=1, lift=0.0,
                        lean=0.0, bob=0.0, bob_cyc=1, roll=360.0, twist=0.0,
                        squash=0.020, head=0.0, glid=0.0),
    # 柔和的双脚小跳
    "healing_clover": dict(wave="swell", leg_deg=17.0, phase=0.5, cyc=2, lift=0.044,
                           lean=1.0, bob=0.042, bob_cyc=4, roll=2.0, twist=3.2,
                           squash=0.058, head=0.0, glid=0.0),
    # 弹簧圈的弹性跳：起伏最大、挤压拉伸最明显
    "life_spring": dict(wave="bounce", leg_deg=14.0, phase=0.5, cyc=2, lift=0.058,
                        lean=0.0, bob=0.074, bob_cyc=2, roll=2.6, twist=2.0,
                        squash=0.115, head=0.0, glid=0.0),
    # 庄严的重步 + 树冠点头
    "holy_tree_guard": dict(wave="march", leg_deg=25.0, phase=0.5, cyc=1, lift=0.042,
                            lean=3.0, bob=0.030, bob_cyc=2, roll=2.2, twist=3.0,
                            squash=0.034, head=6.2, glid=0.0),
    # 缓慢的碾步（比石拳更慢更重）：腿摆幅最大、起伏最低频
    "ironroot_colossus": dict(wave="march", leg_deg=31.0, phase=0.5, cyc=1, lift=0.040,
                              lean=2.4, bob=0.022, bob_cyc=1, roll=1.0, twist=0.8,
                              squash=0.024, head=0.0, glid=0.0),
}


# 攻击配方：**16 份**。beat 是分段节拍 [(段末相位, 段名)]，段名不同 = 动作读起来不同。
# lean/bob/leg/head/arm 是 (蓄力值, 动作值)；spread 是横向张开；squash 是挤压；
# twist 是扭转；glid 是整体前冲。
ATK_BEAT = {
    # 前扑压顶：蓄力下沉 → 整身前砸 → 回收
    "radish_guard": dict(frames=22, beat=[(0.32, "coil"), (0.50, "slam"), (1.0, "settle")],
                         lean=(-6.0, 24.0), bob=(0.030, -0.040), leg=(7.0, 16.0),
                         twist=3.0, squash=0.14),
    # 炮口后坐：短的蓄力 → 猛烈后坐 → 缓慢回位
    "pea_shooter": dict(frames=18, beat=[(0.22, "coil"), (0.34, "recoil"), (1.0, "settle")],
                        lean=(3.0, -13.0), bob=(-0.016, 0.030), arm=(0.0, 15.0),
                        leg=(-4.0, 7.0), twist=8.0, glid=(0.0, 0.055), squash=0.08),
    # 孢子喷发：整身向上顶 + 伞盖剧烈抖动
    "toxic_mushroom": dict(frames=30, beat=[(0.52, "rise"), (0.68, "slam"),
                                             (0.82, "puff"), (1.0, "settle")],
                           lean=(-19.0, 34.0), bob=(-0.048, -0.088), leg=(-14.0, 26.0),
                           twist=5.0, squash=0.22, spread=14.0),
    # 冰爆：向外炸开（横向张到最大）+ 整体上顶
    "frost_ivy": dict(frames=22, beat=[(0.34, "open"), (0.44, "frost"),
                                       (1.0, "settle")],
                      lean=(12.0, -9.0), bob=(0.034, -0.020), leg=(-16.0, 4.0),
                      twist=3.0, squash=0.14, spread=42.0),
    # 前滚冲撞：整身向前翻一段
    "thorn_pumpkin": dict(frames=26, beat=[(0.26, "coil"), (0.62, "pitch"),
                                           (1.0, "settle")],
                          lean=(-14.0, 34.0), bob=(0.040, -0.030), leg=(10.0, 22.0),
                          twist=6.0, glid=(-0.030, 0.115), squash=0.12),
    # 右拳挥出：抬起 → 横扫 → 收拳
    "stone_fist": dict(frames=24, beat=[(0.36, "raise"), (0.56, "swing"), (1.0, "settle")],
                       lean=(-8.0, 16.0), bob=(0.024, -0.026), arm=(-88.0, 62.0),
                       leg=(6.0, 14.0), twist=12.0, squash=0.10),
    # 施法：双手划弧 + 整身转一圈
    "chili_mage": dict(frames=32, beat=[(0.44, "raise"), (0.66, "cast"), (1.0, "settle")],
                       lean=(-10.0, 12.0), bob=(0.022, 0.034), arm=(-70.0, 40.0),
                       leg=(-4.0, 8.0), twist=22.0, squash=0.08),
    # 星爆：整身旋转 + 向上弹
    "starvine": dict(frames=28, beat=[(0.34, "coil"), (0.40, "spin"), (1.0, "settle")],
                     lean=(-5.0, 8.0), bob=(-0.030, 0.066), leg=(-8.0, 12.0),
                     twist=120.0, squash=0.12, spread=18.0),
    # 树冠压顶：树冠抬起再砸下
    "moontree": dict(frames=30, beat=[(0.42, "raise"), (0.62, "slam"), (1.0, "settle")],
                     lean=(-7.0, 15.0), bob=(0.026, -0.034), leg=(5.0, 12.0),
                     head=(-18.0, 22.0), twist=2.0, squash=0.10),
    # 炭火爆开：整身膨胀蓄力 → 炸开
    "char_flower": dict(frames=24, beat=[(0.74, "shake"), (0.84, "blow"),
                                         (1.0, "settle")],
                        lean=(-26.0, 9.0), bob=(0.062, 0.026), leg=(19.0, -6.0),
                        twist=9.0, squash=0.20, spread=52.0),
    # 瘟疫云扩散：伞盖下压 → 向外摊开（横向最宽的一个）
    "plague_mother": dict(frames=34, beat=[(0.46, "coil"), (0.72, "burst"),
                                           (1.0, "settle")],
                          lean=(-5.0, 9.0), bob=(-0.024, 0.040), leg=(-4.0, 8.0),
                          twist=3.0, squash=0.10, spread=48.0),
    # 盾击：整身前顶（没有手臂，用身体撞）
    "moss_shield": dict(frames=20, beat=[(0.30, "coil"), (0.48, "ram"), (1.0, "settle")],
                        lean=(-6.0, 18.0), bob=(0.020, -0.018), leg=(8.0, 18.0),
                        twist=0.0, glid=(-0.020, 0.085), squash=0.14),
    # 治愈脉冲：整身膨起 + 光环扩大（最柔和的一个）
    "healing_clover": dict(frames=30, beat=[(0.50, "raise"), (0.68, "pulse"),
                                            (1.0, "settle")],
                           lean=(-4.0, -2.0), bob=(0.030, 0.056), leg=(-3.0, 5.0),
                           twist=4.0, squash=0.16, spread=20.0),
    # 弹跳治疗：蓄力下压 → 弹起（弹簧）
    "life_spring": dict(frames=26, beat=[(0.46, "coil"), (0.62, "pulse"),
                                         (1.0, "settle")],
                        lean=(0.0, 0.0), bob=(-0.055, 0.098), leg=(-6.0, 10.0),
                        twist=0.0, squash=0.26),
    # 上劈：双臂抡起 → 砸下
    "holy_tree_guard": dict(frames=28, beat=[(0.42, "raise"), (0.62, "slam"),
                                             (1.0, "settle")],
                            lean=(-11.0, 21.0), bob=(0.028, -0.032), arm=(-104.0, 74.0),
                            leg=(7.0, 16.0), head=(-14.0, 18.0), twist=5.0, squash=0.12),
    # 双拳砸地：抬得最高、砸得最深
    "ironroot_colossus": dict(frames=34, beat=[(0.48, "raise"), (0.66, "slam"),
                                               (1.0, "settle")],
                              lean=(-13.0, 26.0), bob=(0.034, -0.048), arm=(-118.0, 88.0),
                              leg=(10.0, 20.0), twist=4.0, squash=0.13),
}

# 受击：**这一套允许共用模板**（需求原话"受击维持现在版本膨胀加闪白就行"，
# 膨胀 + 闪白由游戏侧代码负责）。这里只按体量分档：越重后仰越小、回弹越慢。
HIT = {
    "radish_guard":      dict(frames=12, back=0.040, tilt=13.0, wob=5.0),
    "pea_shooter":       dict(frames=12, back=0.056, tilt=19.0, wob=8.0),
    "toxic_mushroom":    dict(frames=12, back=0.046, tilt=15.0, wob=9.0),
    "frost_ivy":         dict(frames=12, back=0.050, tilt=17.0, wob=11.0),
    "thorn_pumpkin":     dict(frames=12, back=0.036, tilt=12.0, wob=4.0),
    "stone_fist":        dict(frames=12, back=0.024, tilt=9.0, wob=2.0),
    "chili_mage":        dict(frames=12, back=0.060, tilt=22.0, wob=12.0),
    "starvine":          dict(frames=12, back=0.054, tilt=18.0, wob=13.0),
    "moontree":          dict(frames=12, back=0.030, tilt=11.0, wob=3.0),
    "char_flower":       dict(frames=12, back=0.048, tilt=16.0, wob=8.0),
    "plague_mother":     dict(frames=12, back=0.028, tilt=10.0, wob=5.0),
    "moss_shield":       dict(frames=12, back=0.034, tilt=12.0, wob=4.0),
    "healing_clover":    dict(frames=12, back=0.052, tilt=17.0, wob=7.0),
    "life_spring":       dict(frames=12, back=0.044, tilt=14.0, wob=10.0),
    "holy_tree_guard":   dict(frames=12, back=0.026, tilt=9.0, wob=2.0),
    "ironroot_colossus": dict(frames=12, back=0.018, tilt=7.0, wob=1.0),
}

ANIM_LOOP = {"idle": True, "walk": True, "attack": False, "hit": False}
IDLE_FRAMES = 48


# ---------------------------------------------------------------- 波形
def wave(kind, u):
    """
    步态波形。u ∈ [0,1) 是相位，返回值大致在 [−1, 1]。

    · sine    平滑正负（普通走）
    · step    方波化的正弦（顿步：一步一顿）
    · bounce  只取正半周期的软弹（脚下落时有一记冲击）
    · swell   升起慢、落下快（缓起缓落）
    · lurch   前段快后段拖（前滑后拽）
    · march   三角波带平台（机械抬起-放下）
    · roll    恒 0（滚动品种的腿收起，靠 body 转整圈）
    """
    if kind == "step":
        s = math.sin(TAU * u)
        return math.copysign(min(1.0, abs(s) * 1.65), s)
    if kind == "bounce":
        s = math.sin(TAU * u)
        return s * (0.55 + 0.45 * abs(s)) if s > 0 else s * 0.70
    if kind == "swell":
        s = math.sin(TAU * u)
        return math.copysign(abs(s) ** 0.72, s)
    if kind == "lurch":
        s = math.sin(TAU * u)
        return s ** 3 * 0.55 + s * 0.75
    if kind == "march":
        # ★ 必须**穿过零** ★
        # 上一版四段是 −0.35 → 1.00 → 0.325 → −0.35：最低只到 −0.35 且全程不正不负，
        # 于是相隔半个周期的两条腿**没有一个反向帧**，被判"左右腿没有反相"。
        # 现在改成零中心的梯形波：0 → +1（抬腿）→ 0 → −1（落腿），
        # 两条腿自然一正一负。
        p = (u % 1.0) * 4.0
        if p < 1.0:
            return p                       # 0 → 1
        if p < 2.0:
            return 2.0 - p                 # 1 → 0
        if p < 3.0:
            return -(p - 2.0)              # 0 → −1
        return -(4.0 - p)                  # −1 → 0
    if kind == "roll":
        return 0.0
    return math.sin(TAU * u)


def lift_wave(kind, u):
    """抬腿相位的权重 ∈ [0,1]：只在"往前摆"的半周期抬脚。"""
    if kind == "march":
        p = (u % 1.0) * 4.0
        return max(0.0, 1.0 - abs(p - 1.0)) if p < 2.0 else 0.0
    if kind == "roll":
        return 0.0
    if kind in ("bounce", "step"):
        return max(0.0, math.sin(TAU * u))
    return max(0.0, math.sin(TAU * u)) ** 1.4


# ================================================================ 木偶层级
def set_parent(child, parent):
    bpy.ops.object.select_all(action='DESELECT')
    child.select_set(True)
    parent.select_set(True)
    bpy.context.view_layer.objects.active = parent
    bpy.ops.object.parent_set(type='OBJECT', keep_transform=True)


def head_joint(pid, cfg, H):
    """
    树冠类的可动头节点位置（其它品种不建这个组）。

    位置由各自的 deco_* 在建的时候存进 cfg["head"]（它自己知道树干顶在哪），
    这里只做兜底 —— "头节点"对植物来说不是解剖学的头，而是**树冠/花盘这一整块**
    挂在哪儿转。没有树冠的品种就没有这个节点（Head 组的部件会被并进 Body）。
    """
    if "head" in cfg:
        return (0.0, 0.0, cfg["head"])
    return (0.0, 0.0, H * 0.60)


def build_puppet(pid):
    """
        AxisFix_<id>(−90°) → Root_<id> → Body_<id> → Arm_L / Arm_R / Head
                                      ├ Leg_L_<id>
                                      ├ Leg_R_<id>
                                      └ Shadow_<id>

    AxisFix 是**不参与动画**的轴修正节点：Root 每帧都打关键帧，把 −90° 放在 Root 上
    会被关键帧抹掉，动画版本就躺平了。
    """
    parts, cfg, joints, H, _scale = build_parts(pid)
    joints["Head"] = head_joint(pid, cfg, H)

    # 分组：先把 Head 的部件挑出来，再让"没头节点"的品种把树冠并进身体
    buckets = {}
    for p in parts:
        grp = group_of(p.name)
        if grp == "Head" and pid not in HEAD_FOR:
            grp = "Body"
        buckets.setdefault(grp, []).append(p)

    objs = {}
    for grp, members in sorted(buckets.items()):
        merged = kit.join(members, "%s_%s" % (grp, pid))
        kit.apply_transforms(merged)
        kit.set_origin(merged, joints.get(grp, (0.0, 0.0, 0.0)))
        objs[grp] = merged

    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    root = bpy.context.active_object
    root.name = "Root_%s" % pid

    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    axis_fix = bpy.context.active_object
    axis_fix.name = "AxisFix_%s" % pid
    axis_fix.rotation_euler = (D(-90.0), 0.0, 0.0)
    set_parent(root, axis_fix)

    for grp in ("Body", "Leg_L", "Leg_R", "Shadow"):
        if grp in objs:
            set_parent(objs[grp], root)
    for grp in ("Arm_L", "Arm_R", "Head"):
        if grp in objs and "Body" in objs:
            set_parent(objs[grp], objs["Body"])

    rest = {grp: tuple(o.location) for grp, o in objs.items()}
    rest["Root"] = tuple(root.location)
    return objs, root, rest, cfg, H


# ================================================================ 姿态
# 坐标系：X = 右，Y = 前后（−Y 是正面），Z = 上。
# 绕 X 旋转 = 前后摆（正 = 腿向后踢 / 身体前倾），绕 Z = 左右摆，绕 Y = 侧倾。
def pose(anim, t, objs, root, rest, cfg, pid):
    """
    相位 t → 各部件姿态。纯函数、无随机（同 t 必得同姿态）。

    ★ 复位用 rest，不能用 0 ★
    每个分组的原点是它的**关节**，location 的含义是"这块肉挂在哪儿"；
    归零 = 把整块肉搬到父节点原点（gen_anim 原版就是这样，实测躯干低了 0.17m）。
    """
    for name, o in objs.items():
        o.rotation_euler = (0.0, 0.0, 0.0)
        o.location = rest[name]
        o.scale = (1.0, 1.0, 1.0)
    root.rotation_euler = (0.0, 0.0, 0.0)
    root.location = rest["Root"]

    def place(o, name, dx=0.0, dy=0.0, dz=0.0):
        r = rest[name]
        o.location = (r[0] + dx, r[1] + dy, r[2] + dz)

    body = objs.get("Body")
    head = objs.get("Head")
    sh = objs.get("Shadow")
    legL, legR = objs.get("Leg_L"), objs.get("Leg_R")
    armL, armR = objs.get("Arm_L"), objs.get("Arm_R")
    c = PLANTS[pid]

    # ---------------------------------------------------------------- idle
    if anim == "idle":
        # 站立呼吸：节律由 size 派生（小株快、大株慢），不必再写 16 份
        rr = 1.0 + (1.10 - c["size"]) * 0.55
        br = math.sin(TAU * t * rr)
        br2 = math.sin(TAU * t * rr * 2.0)
        if body:
            place(body, "Body", dz=0.014 * br)
            body.rotation_euler = (D(1.1 + 1.6 * c["size"]) * br2, 0.0, D(1.4) * br)
            k = 1.0 + 0.018 * br
            body.scale = (1.0 / math.sqrt(k), 1.0 / math.sqrt(k), k)
        if head:
            head.rotation_euler = (D(3.4) * br, 0.0,
                                   D(2.6) * math.sin(TAU * t * rr * 0.5))
        for arm, sgn in ((armR, 1.0), (armL, -1.0)):
            if arm:
                arm.rotation_euler = (D(2.6 + 2.0 * c["size"]) * br, 0.0,
                                      D(1.8) * sgn * br)
        for leg in (legL, legR):
            if leg:
                leg.rotation_euler = (D(0.9) * br, 0.0, 0.0)
        if sh:
            k = 1.0 - 0.03 * br
            sh.scale = (k, k, 1.0)
        return

    # ---------------------------------------------------------------- walk
    if anim == "walk":
        m = WALK[pid]
        wv = m["wave"]
        cyc = max(1, int(m["cyc"]))
        for leg, key, ph in ((legR, "Leg_R", 0.0), (legL, "Leg_L", m["phase"])):
            if not leg:
                continue
            u = (t * cyc + ph) % 1.0
            ang = D(m["leg_deg"]) * wave(wv, u)
            lf = m["lift"] * lift_wave(wv, u)
            leg.rotation_euler = (ang, 0.0, D(2.0) * math.sin(TAU * u))
            r = rest[key]
            leg.location = (r[0], r[1], r[2] + lf)
        if body:
            if wv == "roll":
                # 滚动：整身绕 X 转**一整圈**。
                # ★ 写 -TAU·t 是个坑 ★ 循环动画的收尾帧用的是 t=0，于是末帧
                # 从 -2π 直接跳回 0 —— 数值上差了 360°（欧拉角不取模），
                # 验证器判"循环首尾差 360°"。这里改成映射到 [-π, π]，整圈之后
                # 首尾落在同一个等价角上。
                ang = (-TAU * t + math.pi) % TAU - math.pi
                body.rotation_euler = (ang, 0.0, 0.0)
                place(body, "Body", dz=0.006 * abs(math.sin(TAU * t)))
            else:
                place(body, "Body",
                      dy=-m["glid"] * math.sin(TAU * m["bob_cyc"] * t),
                      dz=m["bob"] * abs(math.sin(TAU * m["bob_cyc"] * t)))
                body.rotation_euler = (
                    D(m["lean"]) + D(2.0) * math.sin(TAU * m["bob_cyc"] * 2.0 * t),
                    D(m["roll"]) * math.sin(TAU * t * cyc),
                    D(m["twist"]) * math.sin(TAU * t * cyc),
                )
                sq = 1.0 + m["squash"] * math.sin(TAU * m["bob_cyc"] * t)
                body.scale = (1.0 / math.sqrt(sq), 1.0 / math.sqrt(sq), sq)
        if head:
            head.rotation_euler = (D(m["head"]) * math.sin(TAU * m["bob_cyc"] * t), 0.0,
                                   D(-m["twist"] * 0.5) * math.sin(TAU * t * cyc))
        for arm, sgn in ((armR, -1.0), (armL, 1.0)):
            if arm:
                arm.rotation_euler = (D(m["leg_deg"] * 0.42) * sgn
                                      * math.sin(TAU * t * cyc), 0.0,
                                      D(3.0) * sgn * abs(math.sin(TAU * t * cyc)))
        if sh:
            k = 1.0 - 0.14 * abs(math.sin(TAU * t * cyc))
            sh.scale = (k, k, 1.0)
        return

    # ---------------------------------------------------------------- attack
    if anim == "attack":
        a = ATK_BEAT[pid]
        seg, k = _seg_of(a["beat"], t)
        lean_a, lean_b = a.get("lean", (0.0, 0.0))
        bob_a, bob_b = a.get("bob", (0.0, 0.0))
        leg_a, leg_b = a.get("leg", (0.0, 0.0))
        head_a, head_b = a.get("head", (0.0, 0.0))
        arm_a, arm_b = a.get("arm", (0.0, 0.0))
        gl_a, gl_b = a.get("glid", (0.0, 0.0))
        spread = a.get("spread", 0.0)
        squash = a.get("squash", 0.10)
        tw = a.get("twist", 6.0)

        # ★ 三段的**起点与终点都必须是"静止姿态"（所有量 = 0）★
        # 上一版让第一段从 0 起、中段直接顶到 lean_a、尾段从 lean_b 收，
        # 于是攻击的**首帧**落在"已经蓄了一点力"的姿态上、末帧落在 lean_b·u 上，
        # 接 idle 时必然跳一下（实测末帧差 22~120°）。现在改成：
        #   段0  0 → lean_a（蓄力）
        #   段1  lean_a → lean_b（动作）
        #   段2  lean_b → 0（收招回静止）
        # 每段内部都用缓动，段间连续。
        if seg == 0:                       # 蓄力：0 → 蓄力值
            u = k
            lean, bob, lg = lean_a * u, bob_a * u, leg_a * u
            hd, arm, gl = head_a * u, arm_a * u, gl_a * u
            sp_ = spread * 0.22 * u
            sq = 1.0 - squash * 0.35 * u
            tws = tw * 0.15 * u
        elif seg == 1:                     # 动作：蓄力值 → 动作值
            u = 1.0 - (1.0 - k) ** 2.0
            lean = lean_a + (lean_b - lean_a) * u
            bob = bob_a + (bob_b - bob_a) * u
            lg = leg_a + (leg_b - leg_a) * u
            hd = head_a + (head_b - head_a) * u
            arm = arm_a + (arm_b - arm_a) * u
            gl = gl_a + (gl_b - gl_a) * u
            sp_ = spread * (0.22 + 0.78 * math.sin(math.pi * min(1.0, k * 1.15)))
            sq = 1.0 - squash * 0.35 * (1.0 - u) + squash * 0.70 * u
            tws = tw * (0.15 + 0.85 * u)
        else:                              # 回收：动作值 → 0（回静止）
            u = 1.0 - (1.0 - k) ** 2.0
            lean, bob, lg = lean_b * (1.0 - u), bob_b * (1.0 - u), leg_b * (1.0 - u)
            hd, arm, gl = head_b * (1.0 - u), arm_b * (1.0 - u), gl_b * (1.0 - u)
            sp_ = (spread * (0.22 + 0.78 * (1.0 - u))) if spread else 0.0
            sq = 1.0 + squash * 0.70 * (1.0 - u) - squash * 0.35 * u
            tws = tw * (1.0 - u)

        if body:
            place(body, "Body", dy=-gl, dz=bob)
            body.rotation_euler = (D(lean), D(sp_ * 0.30), D(tws))
            body.scale = (1.0 + sp_ * 0.004, 1.0 + sp_ * 0.004, max(0.5, sq))
        if head:
            head.rotation_euler = (D(hd), 0.0, D(tws * 0.30))
        if armR:
            armR.rotation_euler = (D(arm), 0.0, D(-tw * 0.40))
        if armL:
            armL.rotation_euler = (D(arm * 0.34), 0.0, D(tw * 0.20))
        if legR:
            legR.rotation_euler = (D(-lg), 0.0, 0.0)
        if legL:
            legL.rotation_euler = (D(lg * 1.25), 0.0, 0.0)
        if sh:
            kk = 1.0 + 0.16 * sp_ / max(1.0, spread if spread else 1.0)
            sh.scale = (kk, kk, 1.0)
        return

    # ---------------------------------------------------------------- hit
    if anim == "hit":
        h = HIT[pid]
        k = math.sin(math.pi * min(1.0, t / 0.78)) if t < 0.78 else 0.0
        osc = math.sin(TAU * 1.6 * t) * math.exp(-4.0 * t)
        if body:
            place(body, "Body", dy=h["back"] * k, dz=-0.010 * k)
            body.rotation_euler = (D(-h["tilt"]) * k, 0.0, D(h["wob"]) * osc)
            kk = 1.0 + 0.06 * k
            body.scale = (1.0 / math.sqrt(kk), 1.0 / math.sqrt(kk), kk)
        if head:
            head.rotation_euler = (D(-h["tilt"] * 1.25) * k, 0.0,
                                   D(h["wob"] * 1.4) * osc)
        for arm, sgn in ((armR, 1.0), (armL, -1.0)):
            if arm:
                arm.rotation_euler = (D(h["tilt"] * 0.45) * k, 0.0,
                                      D(h["wob"]) * sgn * osc)
        if legR:
            legR.rotation_euler = (D(h["tilt"] * 0.55) * k, 0.0, 0.0)
        if legL:
            legL.rotation_euler = (D(-h["tilt"] * 0.40) * k, 0.0, 0.0)
        if sh:
            kk = 1.0 - 0.10 * k
            sh.scale = (kk, kk, 1.0)


def _seg_of(beat, t):
    """分段节拍 → (段号, 段内进度 u)。beat = [(段末相位, 段名), ...]。"""
    lo = 0.0
    for i, (hi, _nm) in enumerate(beat):
        if t < hi or i == len(beat) - 1:
            span = max(1e-6, hi - lo)
            return i, min(1.0, max(0.0, (t - lo) / span))
        lo = hi
    return len(beat) - 1, 1.0


# ================================================================ 动画导出
def export_anim(pid, anim, frames, loop):
    """
    导出一个动画。**每次都重新搭一次木偶**。

    ★ 不要为了省事在动画之间复用同一个木偶 ★
    Blender 5 的动作按"槽位(slot)"绑到对象上，`animation_data.action` 换了新动作之后
    槽位还停在上一次绑定上。复用时的实测后果：每个对象身上同时留下两条 take，
    都被写进同一个 FBX，Unity 侧互相覆盖（`_walk` 里读到的是 idle 的曲线、
    腿的摆幅只剩 3.4° 而真值是 30°，**文件看起来完全正常**）。
    """
    scene = bpy.context.scene
    objs, root, rest, cfg, H = build_puppet(pid)

    # ★ 循环动画多打一帧收尾帧 ★
    # 相位 t=(f−1)/frames 均匀取 frames 个采样点，所以首尾两帧是**相邻相位、不是同相**：
    # 24 帧的 walk 末帧落在 −7.76°，回到首帧 0° 会跳一下。
    # 补一帧与首帧完全相同的收尾帧 → 25 帧 / 1 个完整周期，loopTime 才无缝。
    total = frames + 1 if loop else frames
    scene.frame_start = 1
    scene.frame_end = total

    shared = bpy.data.actions.new("%s_%s" % (pid, anim))
    for o in list(objs.values()) + [root]:
        o.animation_data_create()
        o.animation_data.action = shared

    for f in range(1, total + 1):
        t = (f - 1) / float(frames) if loop else (f - 1) / float(max(1, total - 1))
        if loop and f == total:
            t = 0.0
        # ★ 先 frame_set，再 pose，最后 keyframe_insert —— 顺序不能换 ★
        # frame_set 会触发依赖图求值，把刚摆好的姿态冲掉；gen_anim 正是栽在这里
        # （第 2 帧起每帧记的都是第 1 帧的值，曲线只剩 2 个键、全程恒定）。
        scene.frame_set(f)
        pose(anim, t, objs, root, rest, cfg, pid)
        for o in list(objs.values()) + [root]:
            o.keyframe_insert(data_path="location")
            o.keyframe_insert(data_path="rotation_euler")
            o.keyframe_insert(data_path="scale")

    path = os.path.join(ANIM_DIR, "%s_%s.fbx" % (pid, anim))
    kit.ensure_dir(path)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        object_types={'MESH', 'EMPTY'},
        mesh_smooth_type='FACE',
        path_mode='AUTO',
        embed_textures=False,
        bake_anim=True,
        bake_anim_use_all_bones=False,
        bake_anim_use_all_actions=True,      # False 时导出器一条曲线都不写
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0,
        axis_forward='-Z',                   # ★ 轴向只在这里处理一次 ★
        axis_up='Y',
        use_space_transform=True,
    )
    drop_meta(path)
    print("[SEEDFALL-ALLY] %-18s %-7s %2d 帧 loop=%-5s 节点=%d -> %s"
          % (pid, anim, total, loop, len(objs) + 1, os.path.basename(path)))
    return path


def anim_spec(pid):
    """这个品种的 4 条动画 → [(名字, 帧数, 是否循环)]。"""
    return [
        ("idle", IDLE_FRAMES, ANIM_LOOP["idle"]),
        ("walk", 24 + (4 if WALK[pid]["cyc"] >= 2 else 0), ANIM_LOOP["walk"]),
        ("attack", ATK_BEAT[pid]["frames"], ANIM_LOOP["attack"]),
        ("hit", HIT[pid]["frames"], ANIM_LOOP["hit"]),
    ]


def build_anims(pid, only=None):
    out = []
    for anim, frames, loop in anim_spec(pid):
        if only and anim not in only:
            continue
        out.append(export_anim(pid, anim, frames, loop))
    return out


# ================================================================ 入口
def report(ids=None):
    """只报数不落盘：三角面 / 身高 / 部件数 / 步态与攻击节拍。改完参数先看这张表。"""
    rows = []
    for pid in (ids or list(PLANTS)):
        proc, cfg, joints, H, _sc = build_parts(pid)
        obj = kit.join(proc, "PL_" + pid)
        kit.apply_transforms(obj)
        # 报数是"落盘后的实际尺寸"：越界时才缩放，跟 build() 一致
        k = _residual_scale(obj.dimensions.z)
        if abs(k - 1.0) > 1e-6:
            obj.scale = (k, k, k)
            kit.apply_transforms(obj)
        tris = kit.tri_count(obj)
        m, a = WALK[pid], ATK_BEAT[pid]
        rows.append((pid, PLANTS[pid]["kind"], round(obj.dimensions.z, 3), tris, len(proc)))
        print("  %-19s %-8s H=%.3f tris=%4d parts=%-3d 腿=%-7s %4.0f°x%d 攻击%2d帧=%s"
              % (pid, PLANTS[pid]["kind"], obj.dimensions.z, tris, len(proc), m["wave"],
                 m["leg_deg"], m["cyc"], a["frames"],
                 "/".join(nm for _u, nm in a["beat"])))
    print("  ---- 合计 %d 个，最大 %d 面，平均 %d 面（预算 %d）"
          % (len(rows), max(r[3] for r in rows), sum(r[3] for r in rows) // len(rows),
             TRI_BUDGET))
    return rows


def _main(argv):
    args = argv[argv.index("--") + 1:] if "--" in argv else []
    if not args:
        print(__doc__)
        print("16 个品种:", ", ".join(PLANTS))
        return 0
    cmd = args[0]
    ids = args[1:] or list(PLANTS)
    if cmd == "static":
        for pid in ids:
            build(pid)
    elif cmd == "anim":
        for pid in ids:
            build_anims(pid)
    elif cmd == "all":
        for pid in ids:
            build(pid)
        for pid in ids:
            build_anims(pid)
    elif cmd == "report":
        report(ids)
    elif cmd == "recipe":
        for pid in ids:
            print("  %-19s walk=%s" % (pid, WALK[pid]))
            print("  %-19s atk =%s" % ("", ATK_BEAT[pid]))
            print("  %-19s hit =%s" % ("", HIT[pid]))
    else:
        print("未知命令 %s" % cmd)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(_main(sys.argv))
