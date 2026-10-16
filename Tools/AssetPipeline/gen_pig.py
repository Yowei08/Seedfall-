# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 小猪生成器
================================
小猪是**中立单位**（不是敌人）：被玩家撞到掉 1 份肥料，所以造型的第一诉求是
"一眼看出是个能撞的东西"，而不是"又一只怪"。三条约束决定了它长什么样：

① 体量跟玩法走：enemies.json 里 `pig.radius = 0.5`，模型按 **体长 0.90m / 高 0.55m**
   建模（四足、矮圆、约等于碰撞体的外接盒）——尺寸参数全在下面的 P 表里，单位是米。
② 轮廓读得出朝向：大头 + 短宽吻 + 立耳 + 卷尾，俯视角下要能立刻判断它朝哪边走
   （它一直在场地上游走，玩家要靠轮廓预判它的切线）。
③ 风格跟既有 4 角色 / 16 植物一致：低饱和暖色、深色墨点眼、接地阴影盘；
   材质名 = 调色板键名（Unity 侧 ToonLook 按名字换成卡通着色器，这是两侧的契约）。

产出：
    Assets/Resources/Art/Characters/pig.fbx                 静止模型（单网格，与既有角色一致）
    Assets/Resources/Art/Anim/pig_{idle,walk,hit}.fbx       木偶层级动画（目录与命名照 gen_anim.py）
    Docs/captures/pig_preview.png                           预览图（交付前自己看一眼）

用法：
    blender -b --factory-startup -P gen_pig.py -- all        # 静态 + 动画 + 预览
    blender -b --factory-startup -P gen_pig.py -- static     # 只出静止模型
    blender -b --factory-startup -P gen_pig.py -- anim idle walk hit
    blender -b --factory-startup -P gen_pig.py -- pose       # 只渲姿态自检图（改动画后快速看效果）
"""

import os
import sys
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit        # noqa: E402

import bpy                        # noqa: E402
import mathutils                  # noqa: E402

# ---------------------------------------------------------------- 调色板
# 只**新增**键，不动既有键——否则会改到已生成的 4 角色 / 16 植物 / 18 环境模块。
# "Pig" 直接取 enemies.json 里 pig.color = #F5A9B8：美术与数据两侧同一个粉，
# 将来改数据表颜色时这张表就是唯一对照物。
kit.PALETTE.update({
    "Pig":      "#F5A9B8",   # 体色（= enemies.json 的 pig.color）
    "PigDark":  "#DE8AA0",   # 耳朵 / 背斑
    "PigBelly": "#FBD3DC",   # 肚皮（亮一度），把"圆肚子"读出来
    "PigSnout": "#E58FA6",   # 吻部
    "PigHoof":  "#7A5A4A",   # 蹄
})

# ---------------------------------------------------------------- 路径
PROJECT     = r"D:\Unity\project\Seedfall守园者"
MODEL_DIR   = os.path.join(PROJECT, "Assets", "Resources", "Art", "Characters")
ANIM_DIR    = os.path.join(PROJECT, "Assets", "Resources", "Art", "Anim")       # 照 gen_anim.ANIM_DIR
PREVIEW_DIR = os.path.join(PROJECT, "Tools", "AssetPipeline", "preview")        # 照 gen_characters.PREVIEW_DIR
CAPTURE     = os.path.join(PROJECT, "Docs", "captures", "pig_preview.png")      # 交付用的那张

ID = "pig"
D = math.radians

# ---------------------------------------------------------------- 尺寸参数（米）
# 脚底 = z 0，正面 = -Y（Blender 的角色正面约定，见 gen_anim 的姿态函数）
# 目标：体长 0.90（吻尖→尾卷外缘）/ 高 0.55（蹄底→耳尖）/ 宽约 0.36
P = dict(
    # 腿与蹄（四足：矮、粗、短 —— 腿只占身高不到 1/3 才读得出"猪"，
    # 第一版腿长 0.17 渲染出来像张桌子，这里压到 0.15 并加粗到 r=0.052）
    leg_h=0.150, leg_r=0.052, leg_x=0.100,
    leg_fy=-0.200, leg_by=0.220,
    hoof_h=0.040, hoof_r=0.060,
    # 躯干（桶身：横向拉长的蛋；臀/肩的球按**拉伸后的半长**定位，改 y 缩放不会脱节）
    body_c=(0.0, 0.010, 0.305), body_r=0.175, body_scale=(1.00, 1.78, 1.04),
    # 头（大——chibi 读法：头大 = 可爱 = 中立单位）
    head_c=(0.0, -0.315, 0.355), head_r=0.145, head_scale=(1.00, 0.96, 0.95),
    # 吻（**短而宽**：第一版 9cm 的长筒渲染出来像吸尘器管，压到 5.8cm 并把鼻盘放大）
    snout_r=0.070, snout_len=0.058, nose_r=0.076, nose_h=0.018,
    # 眼（球心直接放在头表面上 → 一半露在外面；陷进去就是一张没五官的脸）
    eye_dir=(0.44, -0.80, 0.47), eye_r=0.026, eye_scale=(1.15, 0.60, 1.05),
    # 耳（压扁的三角片。**旋转只绕 X/Y，不绕 Z**：这样"压扁轴"始终是前后向，
    # 片子才立得住；第一版用 to_track_quat 对准外上方，结果压扁轴歪成竖向，
    # 耳朵变成两根横插的尖角——渲染出来像恶魔角）
    ear_dir=(0.30, -0.10, 0.95), ear_r=0.080, ear_h=0.105, ear_flat=0.40,
    ear_tilt_out=20.0, ear_tilt_fwd=12.0,
    # 尾（卷尾：Y-Z 平面里扫一段螺旋。段数要够多、球要够大，相邻球重叠 60% 以上，
    # 否则渲染出来是一串糖葫芦）
    tail_y=0.342, tail_z=0.350, tail_r=0.062, tail_sweep=290.0, tail_segs=12,
    # 接地阴影（椭圆盘，比躯干略大一圈）
    shadow_r=0.320, shadow_scale=(0.60, 1.05, 0.30), shadow_h=0.011,
)

# 动画表：名字 → (帧数, 是否循环)。帧数照 gen_anim.ANIMS —— idle48 / walk24 / hit12
ANIMS = {
    "idle": (48, True),
    "walk": (24, True),
    "hit":  (12, False),
}


# ---------------------------------------------------------------- 小工具
def _surface(c, r, scale, dirv, out=1.0):
    """
    椭球表面取点：方向 →（表面点 × out）。

    为什么要算而不是手填坐标：眼、耳、吻都要**贴在头表面上**，
    手填的数字一改头的半径就全失灵——要么陷进头里（渲染成没五官的球），
    要么浮在空中（比穿模还难看）。这里按 S⁻¹·d 归一化求表面点，
    头的任何参数改动都会自动跟着走。
    """
    s = (r * scale[0], r * scale[1], r * scale[2])
    d = mathutils.Vector(dirv).normalized()
    p = mathutils.Vector((d.x / s[0], d.y / s[1], d.z / s[2])).normalized()
    return (c[0] + s[0] * p.x * out, c[1] + s[1] * p.y * out, c[2] + s[2] * p.z * out)


def _drop_meta(path):
    """
    删掉 Unity 生成的 .fbx.meta。

    ★ 这是实测出来的坑 ★ 同名 FBX 内容变了但 .meta 还在时，Unity 不会重新导入
    （mtime 变了也可能被判为"没变化"），于是在引擎里看到的还是旧模型。
    导出后顺手删掉，让 Unity 把它当新资源重导一次。
    """
    meta = path + ".meta"
    if os.path.exists(meta):
        os.remove(meta)
        print(f"[SEEDFALL-PIG] 已删除 {os.path.basename(meta)}（强制 Unity 重新导入）")


# ---------------------------------------------------------------- 造型
def build_parts():
    """造出全部部件（不合并）并给出关节位置。返回 (parts, joints)。"""
    parts = []
    bx, by, bz = P["body_c"]
    hc, hr, hs = P["head_c"], P["head_r"], P["head_scale"]
    half_x = P["body_r"] * P["body_scale"][0]
    half_y = P["body_r"] * P["body_scale"][1]
    half_z = P["body_r"] * P["body_scale"][2]

    # ── 躯干：桶身 ──
    # 三段拼出"猪"的侧面剪影：中间大蛋 + 后端收圆的臀 + 前端稍宽的肩。
    # 只用一个球会得到一个球，读不出"四足的侧面"。
    parts.append(kit.sphere("Body_Torso", (bx, by, bz), P["body_r"], "Pig",
                            scale=P["body_scale"], segs=20, rings=13))
    parts.append(kit.sphere("Body_Rump", (bx, by + half_y * 0.82, bz - 0.005),
                            P["body_r"] * 0.66, "Pig", scale=(1.00, 0.95, 1.02),
                            segs=14, rings=9))
    parts.append(kit.sphere("Body_Chest", (bx, by - half_y * 0.78, bz - 0.012),
                            P["body_r"] * 0.76, "Pig", scale=(1.02, 0.95, 1.00),
                            segs=14, rings=9))
    # 肚皮：亮一度的浅粉，**要露在躯干下缘之外 1cm 才看得见**（藏进去等于没做），
    # 侧视/正视读到"圆肚子"，俯视角看到的是背、不受影响。
    parts.append(kit.sphere("Body_Belly", (bx, by + 0.012, bz - P["body_r"] * 0.70),
                            P["body_r"] * 0.82, "PigBelly", scale=(0.90, 1.22, 0.50),
                            segs=14, rings=9))
    # 背斑：试过两块压扁的深粉球，渲染出来是"背上贴了两个盘子"（俯视图里最明显），
    # 已删除——纯色粉猪反而更干净，也和既有 4 角色的"纯色 + 少量配件"读法一致。

    # ── 四条腿 + 蹄 ──
    # 腿高 0.15、躯干底在 0.123 —— 腿顶埋进躯干 2.7cm，走路上下起伏时不会露出"断腿"。
    for tag in ("FL", "FR", "BL", "BR"):
        sy = P["leg_fy"] if tag[0] == "F" else P["leg_by"]
        sx = -P["leg_x"] if tag[1] == "L" else P["leg_x"]
        parts.append(kit.cyl(f"Leg_{tag}", (sx, sy, P["leg_h"] * 0.5), P["leg_r"], P["leg_h"],
                             "Pig", verts=10))
        parts.append(kit.cyl(f"Hoof_{tag}", (sx, sy, P["hoof_h"] * 0.5), P["hoof_r"], P["hoof_h"],
                             "PigHoof", verts=10, smooth=False))

    # ── 头 ──
    parts.append(kit.sphere("Head_Skull", hc, hr, "Pig", scale=hs, segs=20, rings=13))

    # 吻：朝 -Y 的短筒，后端埋进头里、前端探出 5cm
    snout_anchor = _surface(hc, hr, hs, (0.0, -1.0, -0.42))
    snout_y = snout_anchor[1] - P["snout_len"] * 0.28
    snout_z = snout_anchor[2]
    parts.append(kit.cyl("Snout", (0.0, snout_y, snout_z), P["snout_r"], P["snout_len"],
                         "PigSnout", verts=14, rot=(D(90), 0, 0)))
    nose_y = snout_y - P["snout_len"] * 0.5
    parts.append(kit.cyl("Nose_Plate", (0.0, nose_y, snout_z), P["nose_r"], P["nose_h"],
                         "PigSnout", verts=14, rot=(D(90), 0, 0), smooth=False))
    # 鼻孔：两粒墨色小柱，**探出鼻盘 4mm** 才看得见（平齐等于没有）
    for side in (-1, 1):
        parts.append(kit.cyl(f"Nostril_{'R' if side > 0 else 'L'}",
                             (side * P["nose_r"] * 0.42, nose_y - P["nose_h"] * 0.5 - 0.002, snout_z),
                             0.011, 0.014, "Ink", verts=8, rot=(D(90), 0, 0), smooth=False))

    # 眼：球心直接放在头表面上，于是有一半露在外面
    for side in (-1, 1):
        d = (P["eye_dir"][0] * side, P["eye_dir"][1], P["eye_dir"][2])
        parts.append(kit.sphere(f"Eye_{'R' if side > 0 else 'L'}", _surface(hc, hr, hs, d),
                                P["eye_r"], "Ink", scale=P["eye_scale"], segs=10, rings=6))

    # 耳：压扁的三角片（4 边锥，Y 向压到 0.40），向外上方立起，基部埋进头里
    for side in (-1, 1):
        d = (P["ear_dir"][0] * side, P["ear_dir"][1], P["ear_dir"][2])
        base = _surface(hc, hr, hs, d, out=0.92)
        cen = (base[0], base[1], base[2] + P["ear_h"] * 0.30)
        parts.append(kit.cone(f"Ear_{'R' if side > 0 else 'L'}", cen,
                              P["ear_r"], P["ear_r"] * 0.08, P["ear_h"], "PigDark", verts=4,
                              rot=(D(P["ear_tilt_fwd"]), D(P["ear_tilt_out"] * side), 0.0),
                              scale=(1.0, P["ear_flat"], 1.0), smooth=False))

    # ── 卷尾 ──
    # 在 Y-Z 平面里扫 290° 的螺旋：起点(ang=-90°)正好落在尾根，向外→向上→向前收卷。
    # 半径随进度收细，末端收成小圆头——直尾巴读起来像"另一条腿"。
    cy, cz = P["tail_y"], P["tail_z"] + P["tail_r"]
    for i in range(P["tail_segs"]):
        u = i / float(P["tail_segs"] - 1)
        ang = D(-90.0 + P["tail_sweep"] * u)
        rad = P["tail_r"] * (1.0 - 0.62 * u)
        parts.append(kit.sphere(f"Tail_{i}",
                                (0.0, cy + rad * math.cos(ang), cz + rad * math.sin(ang)),
                                0.027 - 0.015 * u, "Pig", segs=6, rings=4))

    # ── 接地阴影 ──
    # 参考美术里每个单位脚下都有一层软阴影；没有它模型会像"贴"在地面上。
    # 并进同一个网格一起导出（与 gen_characters 一致）。
    parts.append(kit.cyl("Ground_Shadow", (0.0, 0.010, 0.006), P["shadow_r"], P["shadow_h"],
                         "Ink", verts=20, scale=P["shadow_scale"], smooth=False))

    # ── 关节（木偶层级用；静态模型不用）──
    joints = {
        "Root":   (0.0, 0.0, 0.0),
        "Body":   (0.0, by, P["leg_h"]),                                  # 枢轴在髋线：躯干绕它俯仰/起伏
        "Head":   (0.0, hc[1] + hr * hs[1] * 0.70, hc[2] - hr * hs[2] * 0.18),   # 颈根
        "Tail":   (0.0, P["tail_y"], P["tail_z"]),
        "Shadow": (0.0, 0.0, 0.0),
    }
    for tag in ("FL", "FR", "BL", "BR"):
        sy = P["leg_fy"] if tag[0] == "F" else P["leg_by"]
        sx = -P["leg_x"] if tag[1] == "L" else P["leg_x"]
        joints["Leg_" + tag] = (sx, sy, P["leg_h"])                       # 枢轴在髋：腿绕髋摆
    return parts, joints


def group_of(name):
    """部件名 → 身体分组（与 build_parts 的命名一一对应）。"""
    for tag in ("FL", "FR", "BL", "BR"):
        if name.endswith("_" + tag):
            return "Leg_" + tag
    if name.startswith(("Head", "Eye_", "Ear_", "Snout", "Nose", "Nostril_")):
        return "Head"
    if name.startswith("Tail_"):
        return "Tail"
    if name.startswith("Ground_Shadow"):
        return "Shadow"
    return "Body"


# ---------------------------------------------------------------- 静止模型
def build_static(preview=True):
    kit.reset()
    parts, joints = build_parts()
    obj = kit.join(parts, "CH_" + ID)
    kit.apply_transforms(obj)
    # 原点放**脚底中心**：游戏把单位摆在 pos(y=0) 上，原点错了就会浮空或陷地
    kit.set_origin(obj, (0.0, 0.0, 0.0))

    dims = tuple(round(v, 3) for v in obj.dimensions)
    zmin = round(min((obj.matrix_world @ mathutils.Vector(c)).z for c in obj.bound_box), 4)
    print(f"[SEEDFALL-PIG] dims(x=宽,y=长,z=高)={dims} 目标=(0.38,0.90,0.55)")
    if abs(dims[1] - 0.90) > 0.06 or abs(dims[2] - 0.55) > 0.05:
        print(f"[SEEDFALL-PIG] ★尺寸偏离目标：长 {dims[1]} vs 0.90 / 高 {dims[2]} vs 0.55"
              f"（调 P 表后重跑）★")
    # 四足不能照抄 gen_characters 的"最高维必须是 Z"判定（猪最长的一维是体长，不是身高）。
    # 这里检查的才是真正会出事的量：脚底有没有落在 z=0 —— 游戏把单位摆在 y=0 上，
    # 原点一偏就整只浮空或陷进地里。
    if abs(zmin) > 0.005:
        print(f"[SEEDFALL-PIG] ★脚底不在 z=0（实测 zmin={zmin}）★")

    fbx = os.path.join(MODEL_DIR, ID + ".fbx")
    kit.export_fbx(obj, fbx)          # ★ 不要自己烘 Y-up：轴向只在导出器里处理一次 ★
    _drop_meta(fbx)

    out = {"fbx": fbx, "tris": kit.tri_count(obj), "materials": len(obj.data.materials),
           "dims": dims, "zmin": zmin}
    print(f"[SEEDFALL-PIG] {ID}: tris={out['tris']} mats={out['materials']} -> {fbx}")

    if preview:
        center, radius = kit.bounds(obj)
        # 交付图：3/4 视角（跟 gen_characters 的取景参数一致）
        _preview(CAPTURE, center, radius, size=(760, 620), angle=35.0, elev=14.0)
        # 自检图：正/侧/背/顶四视图，专门用来看比例与穿模（不进交付清单，放在流水线 preview 目录）
        for ang, tag in ((0.0, "front"), (90.0, "side"), (180.0, "back")):
            _preview(os.path.join(PREVIEW_DIR, f"pig_{tag}.png"), center, radius,
                     size=(520, 520), angle=ang, elev=8.0)
        _preview(os.path.join(PREVIEW_DIR, "pig_top.png"), center, radius,
                 size=(520, 520), angle=0.0, elev=84.0)
        out["preview"] = CAPTURE
    return out


def _preview(path, center, radius, **kw):
    """包一层：预览失败不该把建模结果一起带走（模型已经落盘了）。"""
    kit.ensure_dir(path)
    try:
        return kit.render_preview(path, center, radius, **kw)
    except Exception as e:                       # noqa: BLE001
        print(f"[SEEDFALL-PIG] ★预览渲染失败 {path}: {e}★")
        return None


# ---------------------------------------------------------------- 木偶层级
def set_parent(child, parent):
    """带保持世界变换的父子绑定（直接赋 .parent 会让子物体跳位）。"""
    bpy.ops.object.select_all(action='DESELECT')
    child.select_set(True)
    parent.select_set(True)
    bpy.context.view_layer.objects.active = parent
    bpy.ops.object.parent_set(type='OBJECT', keep_transform=True)


def build_puppet():
    """
    层级照 gen_anim.build_puppet：

        AxisFix(-90°) → Root → Body → Head / Tail
                              ├ Leg_FL / Leg_FR / Leg_BL / Leg_BR
                              └ Shadow

    AxisFix 是**不参与动画**的轴修正节点；Root 每帧都被打关键帧，
    把 -90° 修正放在 Root 上会被关键帧覆盖掉，动画版本就躺平了。
    """
    kit.reset()
    parts, joints = build_parts()

    buckets = {}
    for p in parts:
        buckets.setdefault(group_of(p.name), []).append(p)

    objs = {}
    for group, members in sorted(buckets.items()):
        merged = kit.join(members, f"{group}_{ID}")
        kit.apply_transforms(merged)
        kit.set_origin(merged, joints.get(group, (0.0, 0.0, 0.0)))   # ★ 枢轴 = 关节
        objs[group] = merged

    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    root = bpy.context.active_object
    root.name = f"Root_{ID}"

    bpy.ops.object.empty_add(type='PLAIN_AXES', location=(0, 0, 0))
    axis_fix = bpy.context.active_object
    axis_fix.name = f"AxisFix_{ID}"
    axis_fix.rotation_euler = (D(-90.0), 0.0, 0.0)
    set_parent(root, axis_fix)

    for g in list(objs):
        if g == "Head" or g == "Tail":
            continue
        set_parent(objs[g], root)
    for g in ("Head", "Tail"):
        if g in objs and "Body" in objs:
            set_parent(objs[g], objs["Body"])

    # 记下每个分组的**静止位置**（= 它的关节），姿态函数据此复位。
    # 见 pose() 顶部关于"归零 vs 复位到 rest"的说明。
    rest = {g: tuple(o.location) for g, o in objs.items()}
    rest["Root"] = tuple(root.location)
    return objs, root, rest


# ---------------------------------------------------------------- 姿态函数
# 全部在 **Blender 坐标系** 里写：X = 右，Y = 前后（-Y 是正面），Z = 上
# 绕 X 旋转 = 前后俯仰（**正 = 头朝下/屁股抬起**，负 = 后仰）；绕 Z = 左右摆；绕 Y = 侧倾
def pose(anim, t, objs, root, rest):
    """相位 t∈[0,1) → 各部件姿态。纯函数，无随机（同 t 必得同姿态）。

    ★ 复位用 rest，不能用 0 ★
    每个分组的原点都在它的**关节**上（build_puppet 里的 set_origin 干的），
    所以 object.location 的含义是"这块肉挂在哪儿"。把它归零 = 把整块肉搬到
    父节点的原点上去。gen_anim 原版正是 `o.location = (0,0,0)`，实测后果是
    导出的 FBX 里所有部件朝关节塌一级：
        · player_walk.fbx 重新导入后躯干比正确位置低 0.17m（= 髋高）
        · 本文件第一版的小猪同样如此（躯干低 0.15m = 腿高）
    所以这里改成"先复位到 rest，再叠加本次姿态的位移"。
    """
    tau = 2.0 * math.pi
    for name, o in objs.items():
        o.rotation_euler = (0.0, 0.0, 0.0)
        o.location = rest[name]
    root.rotation_euler = (0.0, 0.0, 0.0)
    root.location = rest["Root"]

    def place(o, name, dx=0.0, dy=0.0, dz=0.0):
        """在静止位置上叠加位移（而不是把位置整个写掉）。"""
        r = rest[name]
        o.location = (r[0] + dx, r[1] + dy, r[2] + dz)

    body = objs.get("Body")
    head = objs.get("Head")
    tail = objs.get("Tail")
    legs = {k: objs.get("Leg_" + k) for k in ("FL", "FR", "BL", "BR")}

    if anim == "idle":
        # 站着喘气：躯干上下 1cm、头随呼吸点一下并左右看、卷尾慢摇
        br = math.sin(tau * t)
        if body:
            place(body, "Body", dz=0.010 * br)
            body.rotation_euler = (D(1.2) * math.sin(tau * t * 2.0), 0.0, 0.0)
        if head:
            head.rotation_euler = (D(3.5) * br, 0.0, D(4.5) * math.sin(tau * t * 0.5))
        if tail:
            tail.rotation_euler = (0.0, 0.0, D(16.0) * math.sin(tau * t * 2.5))

    elif anim == "walk":
        # 小跑（trot）：**对角腿同相** —— FL+BR 一组、FR+BL 另一组，两组差半周期。
        # 四足不能用"左右反相"（那是人走路的读法），对角步态才是四足小跑的剪影。
        swing = math.sin(tau * t)
        for k, ph in (("FL", 0.0), ("BR", 0.0), ("FR", 0.5), ("BL", 0.5)):
            lg = legs.get(k)
            if lg:
                lg.rotation_euler = (D(30.0) * math.sin(tau * (t + ph)), 0.0, 0.0)
        if body:
            place(body, "Body", dz=0.018 * abs(math.sin(tau * t)))            # 每步一次起落
            body.rotation_euler = (D(2.5) * math.sin(tau * t * 2.0),          # 前后颠
                                   D(3.0) * swing,                            # 左右侧倾
                                   D(3.5) * swing)                            # 轻微扭身
        if head:
            head.rotation_euler = (D(-3.0) * math.sin(tau * t * 2.0), 0.0, D(-3.0) * swing)
        if tail:
            tail.rotation_euler = (D(6.0) * swing, 0.0, D(22.0) * math.sin(tau * t * 2.0))

    elif anim == "hit":
        # 被撞一下：整体后坐半步 + 前躯抬起 + 前腿撑地刹车，随后回弹（12 帧一次性动作）
        k = math.sin(math.pi * min(1.0, t / 0.7)) if t < 0.7 else 0.0
        if root:
            place(root, "Root", dy=0.045 * k)      # +Y = 屁股方向 = 被推着倒退
        if body:
            body.rotation_euler = (D(-15.0) * k, 0.0, 0.0)
            place(body, "Body", dz=-0.012 * k)
        if head:
            head.rotation_euler = (D(-26.0) * k, 0.0, D(7.0) * k)
        for k2, deg in (("FL", -14.0), ("FR", -14.0), ("BL", 16.0), ("BR", 16.0)):
            lg = legs.get(k2)
            if lg:
                lg.rotation_euler = (D(deg) * k, 0.0, 0.0)
        if tail:
            tail.rotation_euler = (0.0, 0.0, D(34.0) * k)


# ---------------------------------------------------------------- 动画导出
def export_anim(anim, frames, loop, objs=None, root=None, rest=None):
    scene = bpy.context.scene
    if objs is None:
        objs, root, rest = build_puppet()
    scene.frame_start = 1
    scene.frame_end = frames

    # ★ 让全部部件共用**同一条动作** ★
    # 每个对象各自一条 action 时，导出器会为每条 action 写一个 take，
    # Unity 侧于是得到 7~9 条片段（每条只含一个部件）而不是 1 条完整动画。
    shared = bpy.data.actions.new(f"{ID}_{anim}")
    for o in list(objs.values()) + [root]:
        o.animation_data_create()
        o.animation_data.action = shared

    for f in range(1, frames + 1):
        t = (f - 1) / float(frames) if loop else (f - 1) / float(max(1, frames - 1))
        # ★ 先 frame_set，再摆姿态，最后打关键帧 —— 顺序不能换 ★
        # frame_set 会触发依赖图求值，把对象变换**覆盖成本帧的动画值**。
        # 若按 gen_anim 的写法（pose → frame_set → insert），从第 2 帧起
        # 姿态刚摆好就被求值结果冲掉，于是每一帧记下的都是第 1 帧的值：
        # 导出的曲线只剩 2 个键、而且全程恒定（本文件第一版就是这样，
        # 重新导入后逐帧读出来腿部角度恒为 0°，肉眼完全看不出区别）。
        scene.frame_set(f)
        pose(anim, t, objs, root, rest)
        for o in list(objs.values()) + [root]:
            o.keyframe_insert(data_path="location")
            o.keyframe_insert(data_path="rotation_euler")

    path = os.path.join(ANIM_DIR, f"{ID}_{anim}.fbx")
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
        # 实测：本项为 False 时导出器**一条曲线都不写**；必须 True。
        # "共用一条 action" 保证每个 FBX 里只有一条 take。
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=1.0,     # 0 = 不简化，会产出巨大文件
        axis_forward='-Z',                 # 轴向只在这里处理一次
        axis_up='Y',
        use_space_transform=True,
    )
    _drop_meta(path)
    print(f"[SEEDFALL-PIG] {ID}/{anim}: {frames} 帧 loop={loop} -> {path}")
    return path


def build_anims(only=None):
    out = []
    for anim, (frames, loop) in ANIMS.items():
        if only and anim not in only:
            continue
        out.append(export_anim(anim, frames, loop))
    return out


def render_pose_check():
    """
    姿态自检图：把木偶摆到 walk 的两个相位各渲一张，确认"腿真的摆起来了、而且是反相的"。
    静态预览图看不出这个——腿没动和腿动了在静止模型上长得一模一样。

    木偶在 Blender 世界空间里**是直立的**：AxisFix 的 -90° 由 Root 的
    matrix_parent_inverse(+90°，set_parent keep_transform 写进去的) 抵消掉了。
    第一版以为它是躺着的、去"修"了一下，反而把它转歪了 90°——不要再动这两处。
    """
    out = []
    for t, tag in ((0.25, "a"), (0.75, "b")):
        objs, root, rest = build_puppet()
        pose("walk", t, objs, root, rest)
        bpy.context.view_layer.update()
        pts = []
        for o in bpy.data.objects:
            if o.type == 'MESH':
                pts += [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
        xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
        center = ((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5, (min(zs) + max(zs)) * 0.5)
        radius = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)) * 0.5
        out.append(_preview(os.path.join(PREVIEW_DIR, f"pig_pose_walk_{tag}.png"), center, radius,
                            size=(560, 460), angle=92.0, elev=8.0))
    return out


# ---------------------------------------------------------------- 入口
if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("用法: blender -b --factory-startup -P gen_pig.py -- all|static|anim|pose [动画名...]")
        print("  动画:", ", ".join(ANIMS))
    elif args[0] == "all":
        build_static(preview=True)
        build_anims()
        render_pose_check()
    elif args[0] == "static":
        build_static(preview=True)
    elif args[0] == "anim":
        build_anims(args[1:] or None)
    elif args[0] == "pose":
        render_pose_check()
    else:
        print(f"未知命令 {args[0]}；用 all|static|anim|pose")
