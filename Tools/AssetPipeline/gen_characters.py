# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 角色生成器
================================
参考美术：宫崎骏/吉卜力风（暖色低饱和、圆润轮廓、大头小身）。
尺寸按玩法半径反推（模型高 ≈ 碰撞半径 × 2.2），不是参考图上标的真实身高——
参考图里的大恶魔标 3.5m，放进 40×40 的场地会压掉半个屏。

产出：
  Assets/Art/Characters/Model/<id>.fbx     给 Unity 导入
  Assets/Art/Characters/Preview/<id>.png   给美术比对
"""

import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit   # noqa: E402


# ---------------------------------------------------------------- 参数表
# 想调体型只改这里；所有数值单位是米，脚底 = z0
CHARACTERS = {
    # 玩家 · 种落小灵（半径 0.5 → 高约 0.95）
    "player": dict(
        height=0.95, legs=0.17, torso=0.26, head=0.20, limb_r=0.035,
        leg_sep=0.055, foot_fwd=0.012, arm_len=0.17, arm_r=0.032,
        body="Cloth", legs_color="Cloth", foot_color="Bag", head_color="Skin",
        eye="Ink", eye_scale=(1.0, 0.55, 1.25), eye_sep=0.072, eye_z=0.028,
        hat=dict(kind="straw", color="Hat", dark="HatDark", radius=0.30, h=0.13, brim=0.34),
        scarf="Scarf", bag="Bag", sprout="Sprout", weapon="hoe",
    ),
    # 小恶魔（近战基础兵，半径 0.45 → 高约 0.72）
    "imp": dict(
        height=0.72, legs=0.15, torso=0.20, head=0.165, limb_r=0.032,
        leg_sep=0.05, foot_fwd=0.01, arm_len=0.15, arm_r=0.030,
        body="ImpRed", legs_color="ImpDark", foot_color="ImpDark", head_color="ImpRed",
        eye="Fire", eye_scale=(1.0, 0.5, 0.8), eye_sep=0.058, eye_z=0.022,
        hat=None, scarf=None, bag=None, sprout=None, weapon="club",
        horns=dict(color="ImpHorn", r=0.022, h=0.13, tilt=28.0, sep=0.085),
        wings="ImpDark", tail="ImpDark", belt="Bone",
    ),
    # 小恶魔弓手（远程兵，半径 0.42 → 高约 0.70）
    "imp_archer": dict(
        height=0.70, legs=0.14, torso=0.20, head=0.16, limb_r=0.030,
        leg_sep=0.048, foot_fwd=0.01, arm_len=0.15, arm_r=0.028,
        body="ImpPurple", legs_color="ImpRobe", foot_color="ImpRobe", head_color="ImpRed",
        eye="Fire", eye_scale=(1.0, 0.5, 0.8), eye_sep=0.056, eye_z=0.020,
        hat=dict(kind="hood", color="ImpRobe", dark="ImpPurple", radius=0.20, h=0.20, brim=0.24),
        scarf=None, bag="Bag", sprout=None, weapon="staff",
        horns=dict(color="ImpHorn", r=0.018, h=0.10, tilt=24.0, sep=0.075),
        wings="ImpRobe", tail="ImpRobe", belt=None,
    ),
    # 大恶魔（精英怪，半径 0.70 → 高约 1.55）
    "brute": dict(
        height=1.55, legs=0.40, torso=0.56, head=0.24, limb_r=0.085,
        leg_sep=0.13, foot_fwd=0.03, arm_len=0.34, arm_r=0.075,
        body="BruteSkin", legs_color="BruteSkin", foot_color="BruteRock", head_color="BruteSkin",
        eye="Fire", eye_scale=(1.0, 0.5, 0.7), eye_sep=0.085, eye_z=0.03,
        hat=None, scarf=None, bag=None, sprout=None, weapon="bigclub",
        horns=dict(color="BruteRock", r=0.055, h=0.30, tilt=34.0, sep=0.14),
        wings=None, tail=None, belt=None, armor="BruteRock", glow="BruteGlow",
    ),
}


# ---------------------------------------------------------------- 通用骨架
def build_chibi(cfg, puppet=False):
    """
    所有角色共用的 chibi 骨架：腿 → 躯干 → 头 → 眼睛 → 手臂，
    再按参数挂帽子/角/翅膀/尾巴/背包/武器。

    puppet=False：把全部部件合并成一个静态网格（静态展示/道具用）。
    puppet=True ：返回 (parts, joints)，由 gen_anim 按关节搭可动木偶。
    """
    kit.reset()
    parts = []
    legs, torso, headR = cfg["legs"], cfg["torso"], cfg["head"]
    hipZ = legs
    chestZ = legs + torso
    headZ = chestZ + headR * 0.88

    # ── 腿与脚 ──
    for side in (-1, 1):
        parts.append(kit.cyl(f"Leg_{'R' if side > 0 else 'L'}", (side * cfg["leg_sep"], 0, legs * 0.5),
                             cfg["limb_r"], legs, cfg["legs_color"], verts=12))
        parts.append(kit.sphere(f"Foot_{'R' if side > 0 else 'L'}",
                                (side * cfg["leg_sep"], cfg["foot_fwd"], cfg["limb_r"] * 0.9),
                                cfg["limb_r"] * 1.25, cfg["foot_color"], scale=(1.1, 1.5, 0.62), segs=12, rings=8))

    # ── 躯干（蛋形）──
    torsoR = torso * 0.52
    parts.append(kit.sphere("Torso", (0, 0, (hipZ + chestZ) * 0.5), torsoR, cfg["body"],
                            scale=(1.0, 0.86, torso / (torsoR * 2.0) * 1.0), segs=20, rings=12))
    # 髋部收口
    parts.append(kit.sphere("Hip", (0, 0, hipZ + torso * 0.10), torsoR * 0.78, cfg["body"],
                            scale=(1.0, 0.9, 0.7), segs=14, rings=8))

    # ── 头 ──
    parts.append(kit.sphere("Head", (0, 0, headZ), headR, cfg["head_color"],
                            scale=(1.0, 0.94, 0.98), segs=24, rings=16))
    # 眼（两个深色椭球）——必须**露在头表面之外**：
    # 上一版放在 -headR*0.80、眼半径又小，眼珠整个陷进球里，
    # 所以大恶魔/小恶魔渲染出来是一张没有五官的球脸。现在推到 -headR*1.02。
    ex = cfg["eye_scale"]
    eyeR = headR * 0.21
    for side in (-1, 1):
        parts.append(kit.sphere(f"Eye_{'R' if side > 0 else 'L'}",
                                (side * cfg["eye_sep"], -headR * 1.02, headZ + cfg["eye_z"]),
                                eyeR, cfg["eye"], scale=ex, segs=12, rings=8))

    # ── 手臂 ──
    armZ = chestZ - cfg["arm_len"] * 0.15
    for side in (-1, 1):
        parts.append(kit.cyl(f"Arm_{'R' if side > 0 else 'L'}",
                             (side * (torsoR * 0.95 + cfg["arm_r"] * 0.6), 0, armZ),
                             cfg["arm_r"], cfg["arm_len"], cfg["body"], verts=12,
                             rot=(0, math.radians(12 * side), 0)))

    # ── 可选配件 ──
    parts += _hat(cfg, headZ, headR)
    parts += _horns(cfg, headZ, headR)
    parts += _scarf(cfg, chestZ, torsoR)
    parts += _bag(cfg, hipZ, torsoR)
    parts += _sprout(cfg, headZ, headR)
    parts += _wings(cfg, chestZ)
    parts += _tail(cfg, hipZ)
    parts += _belt(cfg, hipZ, torsoR)
    parts += _armor(cfg, hipZ, torso, torsoR)
    parts += _weapon(cfg, chestZ, torsoR)

    # 接地阴影：一块压扁的深色圆盘，**并进同一个网格**一起导出。
    # 参考美术里角色脚下都有一层软阴影，没有它模型会像"贴"在地面上。
    shadow = kit.cyl("Ground_Shadow", (0, 0, 0.006), cfg["leg_sep"] * 2.4, 0.011, "Ink",
                     verts=20, smooth=False)
    shadow.scale = (1.0, 1.0, 0.30)
    parts.append(shadow)

    if puppet:
        # 木偶模式：不合并，交给 gen_anim 按"身体分组 + 关节枢轴"搭层级。
        # 关节位置必须在这里给出——四肢要绕**肩/髋**旋转，而不是绕世界原点，
        # 否则动画会出现"整条胳膊绕场地中心画圈"这种诡异效果。
        joints = {
            "Root": (0.0, 0.0, 0.0),
            "Body": (0.0, 0.0, hipZ),
            "Head": (0.0, 0.0, chestZ),
            "Arm_R": (torsoR * 0.95, 0.0, armZ),
            "Arm_L": (-torsoR * 0.95, 0.0, armZ),
            "Leg_R": (cfg["leg_sep"], 0.0, hipZ),
            "Leg_L": (-cfg["leg_sep"], 0.0, hipZ),
        }
        return parts, joints

    obj = kit.join(parts, cfg.get("name", "Character"))
    kit.apply_transforms(obj)
    return obj


def _hat(cfg, headZ, headR):
    h = cfg.get("hat")
    if not h:
        return []
    top = headZ + headR * 0.72
    if h["kind"] == "straw":
        return [
            kit.cone("Hat_Crown", (0, 0, top + h["h"] * 0.42), h["radius"] * 0.62, h["radius"] * 0.06,
                     h["h"], h["color"], verts=20),
            kit.cyl("Hat_Brim", (0, 0, top + 0.006), h["brim"], 0.012, h["dark"], verts=24, smooth=False),
        ]
    # 兜帽：一个稍大的球壳 + 前檐
    return [
        kit.sphere("Hood", (0, 0, headZ + headR * 0.16), headR * 1.16, h["color"],
                   scale=(1.0, 1.02, 1.02), segs=20, rings=12),
        kit.cone("Hood_Peak", (0, -headR * 0.55, headZ + headR * 0.95), h["brim"] * 0.5, 0.005,
                 h["h"], h["dark"], verts=12, rot=(math.radians(-58), 0, 0)),
    ]


def _horns(cfg, headZ, headR):
    h = cfg.get("horns")
    if not h:
        return []
    out = []
    for side in (-1, 1):
        out.append(kit.cone(f"Horn_{'R' if side > 0 else 'L'}",
                            (side * h["sep"], 0, headZ + headR * 0.72),
                            h["r"], h["r"] * 0.12, h["h"], h["color"], verts=10,
                            rot=(0, math.radians(h["tilt"] * side), 0)))
    return out


def _scarf(cfg, chestZ, torsoR):
    c = cfg.get("scarf")
    if not c:
        return []
    return [kit.cyl("Scarf", (0, 0, chestZ - 0.02), torsoR * 1.02, 0.045, c, verts=20)]


def _bag(cfg, hipZ, torsoR):
    c = cfg.get("bag")
    if not c:
        return []
    side = 1
    return [
        kit.box("Satchel", (side * (torsoR + 0.045), 0.01, hipZ + 0.06), (0.085, 0.06, 0.09), c),
        kit.box("Satchel_Lid", (side * (torsoR + 0.045), 0.01, hipZ + 0.108), (0.09, 0.065, 0.02), "Wood"),
    ]


def _sprout(cfg, headZ, headR):
    c = cfg.get("sprout")
    if not c:
        return []
    # 呆毛：茎要短、叶要大且向两侧摊开。
    # 上一版茎 0.075 + 叶半径 0.028×0.55，渲染出来像一根天线顶个小圆片。
    top = headZ + headR * 1.42
    return [
        kit.cyl("Sprout_Stem", (0, 0, top), 0.007, 0.05, "LeafDark", verts=8),
        kit.sphere("Sprout_LeafA", (0.055, 0, top + 0.055), 0.055, c,
                   scale=(1.35, 0.45, 0.30), rot=(0, 0, math.radians(-14)), segs=12, rings=8),
        kit.sphere("Sprout_LeafB", (-0.055, 0, top + 0.055), 0.055, c,
                   scale=(1.35, 0.45, 0.30), rot=(0, 0, math.radians(14)), segs=12, rings=8),
    ]


def _wings(cfg, chestZ):
    c = cfg.get("wings")
    if not c:
        return []
    out = []
    for side in (-1, 1):
        out.append(kit.sphere(f"Wing_{'R' if side > 0 else 'L'}",
                              (side * 0.10, 0.055, chestZ + 0.02), 0.11, c,
                              scale=(0.9, 0.16, 0.62), rot=(0, 0, math.radians(18 * side)),
                              segs=12, rings=8))
    return out


def _tail(cfg, hipZ):
    c = cfg.get("tail")
    if not c:
        return []
    return [
        kit.cyl("Tail", (0, 0.11, hipZ + 0.02), 0.012, 0.20, c, verts=8,
                rot=(math.radians(-62), 0, 0)),
        kit.cone("Tail_Tip", (0, 0.20, hipZ + 0.09), 0.035, 0.004, 0.06, c, verts=8,
                 rot=(math.radians(-62), 0, 0)),
    ]


def _belt(cfg, hipZ, torsoR):
    c = cfg.get("belt")
    if not c:
        return []
    return [kit.sphere("Belt_Skull", (0, -torsoR * 0.92, hipZ + torsoR * 0.12), 0.032, c,
                       scale=(1.0, 0.7, 1.0), segs=12, rings=8)]


def _armor(cfg, hipZ, torso, torsoR):
    """精英怪的岩石护甲：肩甲 + 胸口甲片。"""
    c = cfg.get("armor")
    if not c:
        return []
    glow = cfg.get("glow")
    out = [
        kit.sphere("Pauldron_R", (torsoR * 1.02, 0, hipZ + torso * 0.78), 0.13, c,
                   scale=(1.15, 0.95, 0.72), segs=14, rings=8),
        kit.sphere("Pauldron_L", (-torsoR * 1.02, 0, hipZ + torso * 0.78), 0.13, c,
                   scale=(1.15, 0.95, 0.72), segs=14, rings=8),
        kit.box("Chest_Plate", (0, -torsoR * 0.92, hipZ + torso * 0.60), (0.24, 0.09, 0.18), c),
    ]
    if glow:
        out.append(kit.sphere("Core_Glow", (0, -torsoR * 0.98, hipZ + torso * 0.60), 0.045, glow,
                              emission=3.0, segs=12, rings=8))
    return out


def _weapon(cfg, chestZ, torsoR):
    """手里的主工具（锄头 / 木棒 / 法杖）。"""
    w = cfg.get("weapon")
    if not w:
        return []
    hx = torsoR + 0.13
    if w == "hoe":
        return [
            kit.cyl("Hoe_Shaft", (hx, 0.02, chestZ + 0.05), 0.014, 0.42, "Wood", verts=10,
                    rot=(0, math.radians(-12), 0)),
            kit.box("Hoe_Head", (hx + 0.055, 0.02, chestZ + 0.24), (0.11, 0.028, 0.05), "Metal",
                    rot=(0, math.radians(-12), 0)),
        ]
    if w == "club":
        return [
            kit.cyl("Club_Shaft", (hx, 0.02, chestZ), 0.016, 0.24, "Wood", verts=10),
            kit.sphere("Club_Head", (hx, 0.02, chestZ + 0.15), 0.05, "Wood", scale=(1.0, 1.0, 1.25), segs=12, rings=8),
        ]
    if w == "staff":
        return [
            kit.cyl("Staff_Shaft", (hx, 0.02, chestZ - 0.02), 0.013, 0.44, "Wood", verts=10),
            kit.sphere("Staff_Orb", (hx, 0.02, chestZ + 0.22), 0.045, "Fire", emission=4.0, segs=14, rings=10),
        ]
    # bigclub
    return [
        kit.cyl("BigClub_Shaft", (hx + 0.06, 0.02, chestZ - 0.10), 0.035, 0.42, "Wood", verts=12,
                rot=(0, math.radians(-16), 0)),
        kit.sphere("BigClub_Head", (hx + 0.12, 0.02, chestZ + 0.16), 0.14, "BruteRock",
                   scale=(1.0, 0.95, 1.15), segs=14, rings=10),
    ]


# ---------------------------------------------------------------- 入口
# 模型进 Resources（运行时 Resources.Load 直接可用，与项目既有的 Resources/Data 约定一致）；
# 预览图放在 Assets 之外，避免被 Unity 当贴图导入。
MODEL_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\Characters"
PREVIEW_DIR = r"D:\Unity\project\Seedfall守园者\Tools\AssetPipeline\preview"


def build(char_id, preview=True):
    cfg = dict(CHARACTERS[char_id])
    cfg["name"] = "CH_" + char_id
    obj = build_chibi(cfg)
    # 朝向自检：Blender 是 Z-up，所以"最高的那一维必须是 Z"，否则模型在引擎里会躺平。
    # 这条判定是为了把"躺平"归因到 Blender 侧还是 Unity 导入侧——实测两边都要看。
    dims = tuple(round(v, 3) for v in obj.dimensions)
    rot = tuple(round(math.degrees(v), 1) for v in obj.rotation_euler)
    print(f"[SEEDFALL-DIMS] {char_id} dims(x,y,z)={dims} rot(deg)={rot} "
          + ("直立 OK" if dims[2] >= max(dims[0], dims[1]) else "★Z 不是最高轴：模型本身就躺平★"))
    fbx = os.path.join(MODEL_DIR, char_id + ".fbx")
    kit.export_fbx(obj, fbx)
    tris = kit.tri_count(obj)
    out = {"id": char_id, "fbx": fbx, "tris": tris, "materials": len(obj.data.materials)}
    if preview:
        try:
            center, radius = kit.bounds(obj)
            # 正面 / 侧面 / 背面 三视图（对照参考美术的三视图排版）
            kit.render_views(os.path.join(PREVIEW_DIR, char_id + ".png"), center, radius,
                             views=(0.0, 90.0, 180.0), size=(480, 480), elev=12.0)
            out["preview"] = os.path.join(PREVIEW_DIR, char_id + "_000.png")
        except Exception as e:
            out["preview_error"] = str(e)
    print(f"[SEEDFALL] {char_id}: tris={tris} mats={len(obj.data.materials)} -> {fbx}")
    return out


def build_all(preview=True):
    results = []
    for cid in CHARACTERS:
        results.append(build(cid, preview))
    return results


if __name__ == "__main__":
    ids = [a for a in sys.argv[sys.argv.index("--") + 1:]] if "--" in sys.argv else []
    if not ids:
        print("用法: blender -b -P gen_characters.py -- <id...>   可选 id:", ", ".join(CHARACTERS))
    else:
        for cid in ids:
            build(cid)
