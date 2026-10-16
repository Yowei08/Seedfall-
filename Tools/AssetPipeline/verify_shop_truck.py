# -*- coding: utf-8 -*-
"""
流动商店车 · 回读验证器
========================
把导出的 FBX **重新导入 Blender**，逐帧读曲线。这是唯一能证明
"动画真的写进去了""轮子真的在转"的手段 —— 曲线恒 0 和 ±30° 在静止模型上
长得一模一样，看预览图永远发现不了（gen_anim 的腿部恒 0° 就是这么漏掉的）。

四条决定性判据：
  ① 几何回读：Z 是高度轴、zmin≈0、尺寸在需求的三个区间内、面数 < 2500、
     材质名全部是调色板键
  ② 车头朝向：车头组（Hood / Grille / Bumper_Front）的 y 都在 -Y 侧，
     且**前轮在 -Y 侧** —— 这一条直接决定游戏里该配哪个 yaw
  ③ 轮子枢轴：每只轮子的网格包围盒中心 与 它的枢轴 的偏差 < 5mm
     （枢轴偏了轮子就会"绕整车画圆"而不是原地转，看图看不出来）
  ④ 逐帧轮子转角：drive 的 4 只轮子各自转满 1080°（3 圈），且斜率恒定
     —— 报出每帧角度和 deg/s，这是"轮子真的在转"的硬证据

★ 另外断言 Root 一动不动 ★
  游戏是**用 Transform 沿路点移动**整台车的。只要 clip 里带了 Root 的位移曲线，
  Unity 的旧版 Animation 组件就会把它当 root motion 应用到车的位置上，
  和代码的位移叠加 —— 表现就是"车自己往前跑"甚至飞出去。
  所以 Root 全程必须严格是 (0,0,0)，本文件把它当**失败项**来断言。

用法：blender -b --factory-startup -P verify_shop_truck.py -- [verify|preview|all]
"""

import os
import sys
import math

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_shop_truck as gt         # noqa: E402

import bpy                          # noqa: E402

PROJECT = gt.PROJECT
PROP_DIR = gt.PROP_DIR
ANIM_DIR = gt.ANIM_DIR
PREVIEW_DIR = gt.PREVIEW_DIR
CAPTURE = gt.CAPTURE
FPS = gt.FPS

STATIC_FBX = os.path.join(PROP_DIR, gt.ID + ".fbx")

#: 帧数 → 回读的帧数（loop 的片段多一帧收尾，和导出时一致）
READ_FRAMES = {
    "drive": gt.DRIVE_FRAMES + 1,
    "enter": gt.ENTER_FRAMES,
    "exit": gt.EXIT_FRAMES,
}

#: 车头组 —— 名字以这些开头的部件必须整块落在 -Y 侧
NOSE_PARTS = ("Hood_", "Grille_", "Bumper_Front_", "Headlight_")


# ================================================================ 场景工具
def fresh():
    """清空场景**并清 bpy.data**。

    只 read_homefile(use_empty=True) 清场景不清数据块；同一个进程里连续导入多个
    FBX 时，导入器会把新动作接到上一次残留的同名动作上，于是读到的是**上一个
    文件**的曲线。验证器读错比不验证更危险（gen_plants_biped / verify_player_v2
    都踩过同一个坑）。
    """
    bpy.ops.wm.read_homefile(use_empty=True)
    for blk in (bpy.data.actions, bpy.data.objects, bpy.data.meshes,
                bpy.data.materials, bpy.data.armatures):
        for item in list(blk):
            try:
                blk.remove(item)
            except Exception:                     # noqa: BLE001
                pass


def import_fbx(path):
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    return list(bpy.context.scene.objects)


def root_of(o):
    """上溯到最顶层父节点。"""
    p = o
    while p.parent is not None:
        p = p.parent
    return p


def find_objs(objs, prefix):
    return [o for o in objs if o.name.startswith(prefix)]


# ================================================================ ① 几何
def verify_static():
    if not os.path.exists(STATIC_FBX):
        return dict(ok=False, problems=[f"缺文件 {STATIC_FBX}"])
    fresh()
    objs = import_fbx(STATIC_FBX)
    meshes = [o for o in objs if o.type == 'MESH']
    if not meshes:
        return dict(ok=False, problems=["回读后没有 MESH"])

    pts, tris, mats = [], 0, set()
    per_part = []
    for o in meshes:
        mw = o.matrix_world
        local = [mw @ v.co for v in o.data.vertices]
        pts += local
        t = kit.tri_count(o)
        tris += t
        mats |= {m.name for m in o.data.materials if m}
        per_part.append((o.name, t, min(p.z for p in local), max(p.z for p in local),
                         min(p.y for p in local), max(p.y for p in local)))
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    zmin, zmax = min(zs), max(zs)

    problems = []

    # ---- Z 是高度轴 ----
    # ★ 判据不能写成 "长 ≥ 高 ≥ 宽" ★
    #   第一版这么写，实测 dims=(2.18, 4.06, 2.36) 被判"轴向不对" —— 其实完全正常：
    #   这台车**高(2.36) > 宽(2.18)**（遮阳棚顶到 2.36，比车宽还高）。
    #   真正要断言的只有两件：Z 是第二大的轴、Y 是最大的轴（车比它自己宽和高都长）。
    if not (dims[1] >= dims[2] and dims[1] >= dims[0]):
        problems.append(f"轴向不对：dims=({dims[0]:.2f},{dims[1]:.2f},{dims[2]:.2f})，"
                        f"期望 长(y) 是最大维")
    if dims[2] < 1.5:
        problems.append(f"高度只有 {dims[2]:.2f}，Z 可能不是高度轴")
    # ---- 三个尺寸区间 ----
    if not (gt.TARGET_LEN[0] <= dims[1] <= gt.TARGET_LEN[1]):
        problems.append(f"长 {dims[1]:.3f} 不在 {gt.TARGET_LEN}")
    if not (gt.TARGET_WID[0] <= dims[0] <= gt.TARGET_WID[1]):
        problems.append(f"宽 {dims[0]:.3f} 不在 {gt.TARGET_WID}")
    if not (gt.TARGET_HGT[0] <= dims[2] <= gt.TARGET_HGT[1]):
        problems.append(f"高 {dims[2]:.3f} 不在 {gt.TARGET_HGT}")
    # ---- 车底 z=0 ----
    if abs(zmin) > 0.006:
        problems.append(f"车底不在 z=0（zmin={zmin:.4f}）")
    # ---- 面数 ----
    if tris >= gt.TRI_BUDGET:
        problems.append(f"三角面 {tris} ≥ {gt.TRI_BUDGET}")
    # ---- 材质名 ----
    bad = sorted(m for m in mats if m not in kit.PALETTE)
    if bad:
        problems.append(f"非调色板材质名：{bad}")
    # ---- 六个水果色都在 ----
    missing_fruit = [k for k in ("Fruit_Red", "Fruit_Orange", "Fruit_Yellow",
                                 "Fruit_Green", "Fruit_Purple", "Fruit_Pink")
                     if k not in mats]
    if missing_fruit:
        problems.append(f"缺水果色：{missing_fruit}")

    # ---- ② 车头朝向 ----
    # ★ 两版都判错的教训留在这里 ★
    #   第一版用"部件名以 Hood_/Grille_ 开头"来核：导出前 kit.join() 已经把整车
    #   合成了几个分组网格，部件名在单文件里**根本不存在**，于是永远报"找不到车头组"。
    #   第二版改用"横向最宽处应在 +Y"：也不成立 —— 前挡泥板(±1.03)和货斗(±1.09)
    #   几乎一样宽，量出来两边都是 1.090，判不出方向。
    #   第三版改用**水果材质的质心**：水果只可能堆在货斗里，而货斗只可能在车尾。
    #   这条判据的物理含义最硬，且完全不依赖命名和宽度巧合。
    fruit_names = {"Fruit_Green", "Fruit_Orange", "Fruit_Pink", "Fruit_Purple",
                   "Fruit_Red", "Fruit_Yellow", "Fruit_Pumpkin", "Fruit_Stem"}
    fy_sum, fy_n = 0.0, 0
    for o in meshes:
        mw = o.matrix_world
        slots = [m.name for m in o.data.materials]
        keep = {i for i, n in enumerate(slots) if n in fruit_names}
        if not keep:
            continue
        for poly in o.data.polygons:
            if poly.material_index not in keep:
                continue
            for vi in poly.vertices:
                fy_sum += (mw @ o.data.vertices[vi].co).y
                fy_n += 1
    fruit_y = (fy_sum / fy_n) if fy_n else None

    # 货斗本体（TruckWood 组）的 y 跨度 —— 果堆必须落在它里面
    bed_y0, bed_y1, bed_n = None, None, 0
    for o in meshes:
        if not o.name.startswith("BedTrunk"):
            continue
        mw = o.matrix_world
        for v in o.data.vertices:
            y = (mw @ v.co).y
            bed_y0 = y if bed_y0 is None else min(bed_y0, y)
            bed_y1 = y if bed_y1 is None else max(bed_y1, y)
            bed_n += 1

    # 前轮必须在 -Y 侧（前后轮哪个在前，直接决定 yaw 怎么配）
    wf = [p for p in per_part if p[0].startswith("Wheel_F")]
    wr = [p for p in per_part if p[0].startswith("Wheel_R")]
    if not wf or not wr:
        problems.append(f"找不到前后轮：front={[p[0] for p in wf]} rear={[p[0] for p in wr]}")
    else:
        y_front = sum((p[4] + p[5]) * 0.5 for p in wf) / len(wf)
        y_rear = sum((p[4] + p[5]) * 0.5 for p in wr) / len(wr)
        if not (y_front < y_rear):
            problems.append(f"前轮(y={y_front:.2f}) 不在后轮(y={y_rear:.2f}) 的 -Y 侧 → 朝向反了")
    if fruit_y is None:
        problems.append("找不到水果面 → 无法用水果质心核朝向")
    elif bed_y0 is None:
        problems.append("找不到 BedTrunk 网格 → 无法核对果堆是否在货斗里")
    else:
        bed_mid = (bed_y0 + bed_y1) * 0.5
        # ★ 判据是"果堆质心 ≫ 车头"，不是"果堆质心 > 0" ★
        #   车头侧是 -Y；只写 `fruit_y > 0` 会在果堆整体偏前时误报（第三版就误报过一次，
        #   当时质心 = -0.221 但其实仍在货斗内）。真正要保证的是：
        #   果堆质心**落在货斗的 y 跨度里**，且明显比整车中心偏车尾。
        if not (bed_y0 - 0.05 <= fruit_y <= bed_y1 + 0.05):
            problems.append(f"水果质心 y={fruit_y:.2f} 跑出货斗范围 "
                            f"[{bed_y0:.2f},{bed_y1:.2f}] → 果堆位置错了")
        elif fruit_y <= bed_mid - 0.35:
            problems.append(f"水果质心 y={fruit_y:.2f} 明显偏车头侧"
                            f"（货斗中心 {bed_mid:.2f}）→ 果堆过于靠前")

    # ---- ③ 轮子枢轴 = 轮心 ----
    pivot_rows = []
    for w in [o for o in meshes if o.name.startswith("Wheel_")]:
        mw = w.matrix_world
        loc = [mw @ v.co for v in w.data.vertices]
        cx = (min(p.x for p in loc) + max(p.x for p in loc)) * 0.5
        cy = (min(p.y for p in loc) + max(p.y for p in loc)) * 0.5
        cz = (min(p.z for p in loc) + max(p.z for p in loc)) * 0.5
        # 枢轴 = 对象自身原点在世界里的位置
        px, py, pz = mw.translation
        d = math.sqrt((cx - px) ** 2 + (cy - py) ** 2 + (cz - pz) ** 2)
        pivot_rows.append((w.name, d, (px, py, pz)))
        if d > 0.005:
            problems.append(f"{w.name} 枢轴偏离轮心 {d * 1000:.1f}mm（>5mm）→ 转起来会画圆")

    print(f"[VERIFY] 几何 dims(x宽, y长, z高) = {tuple(round(v, 3) for v in dims)}")
    print(f"[VERIFY] zmin = {zmin:.4f}  zmax = {zmax:.4f}  三角面 = {tris}")
    print(f"[VERIFY] 材质 {len(mats)}: {sorted(mats)}")
    print(f"[VERIFY] 网格对象 {len(meshes)} 个")
    if fruit_y is None:
        print("[VERIFY] ★找不到水果面★")
    elif bed_y0 is None:
        print(f"[VERIFY] 水果材质质心 y={fruit_y:.3f}（找不到 BedTrunk，无法核对）")
    else:
        print(f"[VERIFY] 水果材质质心 y={fruit_y:.3f}  货斗 y 跨度 [{bed_y0:.3f},{bed_y1:.3f}]"
              f"  中心 {(bed_y0 + bed_y1) * 0.5:.3f}"
              f"  → {'落在货斗内 ✓（货斗在 +Y = 车尾，车头 = −Y）' if bed_y0 <= fruit_y <= bed_y1 else '跑出货斗 ✗'}")
    if wf and wr:
        print(f"[VERIFY] 前轮 y={y_front:.2f}  后轮 y={y_rear:.2f}  → 车头在 "
              f"{'−Y 侧 ✓' if y_front < y_rear else '+Y 侧 ✗'}")
    for nm, d, p in pivot_rows:
        print(f"[VERIFY] {nm}: 枢轴偏差 {d * 1000:.2f}mm  枢轴={tuple(round(v, 3) for v in p)}")
    for p in problems:
        print(f"[VERIFY] ★几何未过：{p}★")
    if not problems:
        print("[VERIFY] 几何全部通过")

    return dict(ok=not problems, problems=problems,
                dims=tuple(round(v, 4) for v in dims), zmin=zmin, zmax=zmax,
                tris=tris, mats=sorted(mats), meshes=len(meshes),
                pivots=[(n, round(d, 6)) for n, d, _p in pivot_rows])


# ================================================================ ② 动画
def read_anim(name):
    """导入一条动画，逐帧读全部关键对象的曲线。返回 dict。"""
    path = os.path.join(ANIM_DIR, f"{gt.ID}_{name}.fbx")
    if not os.path.exists(path):
        return None
    fresh()
    objs = import_fbx(path)
    sc = bpy.context.scene
    n = READ_FRAMES[name]

    wheels = sorted(find_objs(objs, "Wheel_"), key=lambda o: o.name)
    bobs = find_objs(objs, "Bob_")
    roots = find_objs(objs, "Root_")
    fruits = find_objs(objs, "Fruits_")
    if not wheels or not bobs or not roots:
        return dict(error=f"缺对象：wheels={len(wheels)} bobs={len(bobs)} roots={len(roots)}",
                    names=[o.name for o in objs])

    def rec(o):
        locs, euls = [], []
        for f in range(1, n + 1):
            sc.frame_set(f)
            locs.append(tuple(o.matrix_world.translation))
            # ★ 回读角度要先看 rotation_mode ★
            #   FBX 导入把矩阵烘进节点，rotation_mode 常是 QUATERNION，
            #   此时 rotation_euler 恒为 0 —— 照 euler 读会读出"动画没导出去"的假故障。
            if o.rotation_mode == 'QUATERNION':
                e = o.rotation_quaternion.to_euler('XYZ')
            else:
                e = o.rotation_euler
            euls.append(tuple(math.degrees(v) for v in e))
        return locs, euls

    out = dict(path=path, frames=n, wheels={}, )
    for w in wheels:
        locs, euls = rec(w)
        out["wheels"][w.name] = dict(locs=locs, euls=euls)
    out["bob_loc"], out["bob_eul"] = rec(bobs[0])
    out["root_loc"], out["root_eul"] = rec(roots[0])
    out["bob_name"] = bobs[0].name
    out["root_name"] = roots[0].name
    if fruits:
        out["fruit_loc"], _ = rec(fruits[0])
    else:
        out["fruit_loc"] = None
    out["all_names"] = sorted(o.name for o in objs)
    return out


def unwrap_deg(vals):
    """把一列角度按"最短路径"连续化，消掉 ±360 的跳变（Euler 回读常见）。

    ★ 为什么要 unwrap ★
    导出时我们写的是逐帧连续弧度量（0 → 1080°），但导入后经过矩阵/四元数一转，
    读出来可能被折回 [-180,180]。不 unwrap 就会看到"转了一格又跳回来"的假象，
    把一条单调直线误判成"没在转"。
    """
    if not vals:
        return []
    out = [vals[0]]
    for v in vals[1:]:
        prev = out[-1]
        while v - prev > 180.0:
            v -= 360.0
        while v - prev < -180.0:
            v += 360.0
        out.append(v)
    return out


def axis_span(vals, axis=0):
    col = [v[axis] for v in vals]
    return min(col), max(col)


def zero_cross_hz(vals, loop):
    """过零计数估主频（Hz）。比 FFT 稳，短序列也准。"""
    col = list(vals)
    mean = sum(col) / len(col)
    c = [v - mean for v in col]
    if loop:
        c.append(c[0])
    crossings = 0
    for i in range(1, len(c)):
        if (c[i - 1] < 0.0) != (c[i] < 0.0):
            crossings += 1
    dur = (len(c) - 1) / FPS
    if crossings < 2 or dur <= 1e-6:
        return 0.0
    return (crossings / 2.0) / dur


def verify_anim(name):
    """逐帧验证一条动画。返回 (ok, problems, facts)。"""
    data = read_anim(name)
    facts, problems = [], []
    if data is None:
        return False, [f"缺动画文件 {gt.ID}_{name}.fbx"], facts
    if "error" in data:
        return False, [f"{name}: {data['error']}  对象={data.get('names')}"], facts

    frames, loop, revs, _note = gt.ANIMS[name]
    dur = frames / FPS

    # ---------- ★ Root 必须一动不动 ★ ----------
    rmaxl = max(max(abs(v) for v in p) for p in data["root_loc"])
    rmaxr = max(max(abs(v) for v in e) for e in data["root_eul"])
    facts.append(f"Root「{data['root_name']}」全程最大位移 {rmaxl * 1000:.3f}mm、"
                 f"最大转角 {rmaxr:.4f}°（要求 <0.5mm / <0.05°）")
    if rmaxl > 0.0005 or rmaxr > 0.05:
        problems.append(f"{name}: Root 动了（位移 {rmaxl * 1000:.2f}mm / 转角 {rmaxr:.3f}°）"
                        f"→ 会和代码的路点位移叠加，车会飞出去")

    # ---------- 轮子真的在转 ----------
    for wname, wd in sorted(data["wheels"].items()):
        raw = [e[0] for e in wd["euls"]]
        ang = unwrap_deg(raw)
        span = max(ang) - min(ang)
        # 斜率（deg/s）：首尾差 ÷ 时长
        slope = (ang[-1] - ang[0]) / dur if dur else 0.0
        # 单调性：相邻帧增量的正负一致性（轮子只往一个方向转）
        deltas = [ang[i + 1] - ang[i] for i in range(len(ang) - 1)]
        forward = sum(1 for d in deltas if d > 0.01)
        bad_back = sum(1 for d in deltas if d < -0.01)
        exp_span = 360.0 * revs
        line = (f"{wname}: 总转角 {span:.1f}°（期望 {exp_span:.0f}°）"
                f"  {slope:.1f}°/s  正转帧 {forward}/{len(deltas)}  倒转帧 {bad_back}")
        if name == "drive":
            facts.append(line)
        if span < exp_span * 0.9:
            problems.append(f"{name}/{wname}: 只转了 {span:.1f}°，期望 ≈{exp_span:.0f}°")
        if bad_back > 0:
            problems.append(f"{name}/{wname}: 有 {bad_back} 帧角度倒退 → 不是单向转")
        # 轮心世界位置必须不动（枢轴正确才不动）
        wl = wd["locs"]
        wdev = max(max(abs(wl[i][k] - wl[0][k]) for k in range(3)) for i in range(len(wl)))
        if wdev > 0.002:
            problems.append(f"{name}/{wname}: 轮心漂了 {wdev * 1000:.1f}mm → 枢轴不在轮心")
        if name == "drive":
            facts.append(f"{wname}: 轮心最大漂移 {wdev * 1000:.3f}mm（枢轴正确性）")

    # ---------- 车身颠簸 ----------
    bl = data["bob_loc"]
    zs = [p[2] for p in bl]
    ys = [p[1] for p in bl]
    zspan = max(zs) - min(zs)
    yspan = max(ys) - min(ys)
    facts.append(f"车身 Bob：z 起伏 {zspan * 1000:.1f}mm（频率 {zero_cross_hz(zs, loop):.2f}Hz）"
                 f"  y 行程 {yspan:.3f}m")
    if name == "drive":
        if zspan < 0.010:
            problems.append(f"drive: 车身起伏只有 {zspan * 1000:.1f}mm，看不出颠簸")
        exp_hz = 1.0 / dur
        got_hz = zero_cross_hz(zs, loop)
        if abs(got_hz - exp_hz) > exp_hz * 0.4:
            problems.append(f"drive: 车身起伏频率 {got_hz:.2f}Hz ≠ 期望 {exp_hz:.2f}Hz")
        else:
            facts.append(f"drive: 车身 {got_hz:.2f}Hz vs 期望 {exp_hz:.2f}Hz ✓")
    else:
        # 入场/离场：Bob 必须真的走完 ~3.6m
        if yspan < 3.0:
            problems.append(f"{name}: 车身 y 行程只有 {yspan:.2f}m，入场/离场位移不够（期望 ≈3.6m）")
        else:
            facts.append(f"{name}: 位移 {yspan:.2f}m ✓（相对停车位，不含路径）")

    # ---------- 果堆轻晃 ----------
    if data["fruit_loc"]:
        fz = [p[2] for p in data["fruit_loc"]]
        fspan = max(fz) - min(fz)
        facts.append(f"果堆：z 晃动 {fspan * 1000:.1f}mm（频率 {zero_cross_hz(fz, loop):.2f}Hz）")
        if name == "drive" and fspan < 0.004:
            problems.append(f"drive: 果堆几乎不动（{fspan * 1000:.1f}mm）")

    # ---------- 频率互不相同（轮子 / 车身）----------
    if name == "drive":
        wang = unwrap_deg([e[0] for e in sorted(data["wheels"].values(),
                                                key=lambda d: d["euls"])[0]["euls"]])
        wheel_hz = (max(wang) - min(wang)) / 360.0 / dur
        body_hz = zero_cross_hz(zs, loop)
        facts.append(f"频率对照：轮子 {wheel_hz:.3f}转/s  车身 {body_hz:.3f}Hz  "
                     f"比值 {wheel_hz / body_hz if body_hz else 0:.2f}:1")
        if body_hz > 0 and abs(wheel_hz / body_hz - round(wheel_hz / body_hz)) > 0.15:
            problems.append(f"drive: 轮子与车身频率比 {wheel_hz / body_hz:.2f} 不是整数 → "
                            f"循环接回去会有相位跳变")

    for p in problems:
        print(f"[VERIFY] ★{name} 未过：{p}★")
    return (not problems), problems, facts


def verify():
    print("=" * 72)
    print("[VERIFY] ① 几何回读")
    print("=" * 72)
    geo = verify_static()

    print()
    print("=" * 72)
    print("[VERIFY] ② 动画逐帧回读")
    print("=" * 72)
    anims = {}
    for name in gt.ANIMS:
        ok, problems, facts = verify_anim(name)
        anims[name] = dict(ok=ok, problems=problems, facts=facts)
        print(f"\n--- {name} ---")
        for f in facts:
            print(f"       {f}")
        print(f"    结论: {'通过' if ok else '未过'}")

    all_ok = geo.get("ok", False) and all(a["ok"] for a in anims.values())
    print()
    print("=" * 72)
    print(f"[VERIFY] 总结：几何 {'通过' if geo.get('ok') else '未过'}；"
          + "；".join(f"{k} {'通过' if v['ok'] else '未过'}" for k, v in anims.items()))
    print(f"[VERIFY] {'全部通过' if all_ok else '★有未过项，见上面 ★'}")
    print("=" * 72)
    return all_ok


# ================================================================ ③ 渲染对照图
#: 视图名 → render_preview 的 (angle, elev)
#: ★ 角度约定（实测，别照字面猜）★
#:   render_preview 把相机放在 center + (sin a·cos e, -cos a·cos e, sin e)·d，
#:   即 a=0° 时相机在 **-Y 侧**。车头朝 -Y，所以：
#:     a =   0° → 相机在车头前方 → 正面
#:     a =  90° → 相机在 +X 侧   → 侧面
#:     a = 180° → 相机在车尾后方 → **背面（= 游戏相机所在的那一侧）**
#:     a = 200° → 车尾偏左 20°   → 3/4 后视
#:
#: ★ "60° 俯视"必须是 a=180°/elev=60°，不能用 200°/60° ★
#:   游戏相机的机位是（GameBootstrap.cs:325）
#:       offset = (0, sin pitch, -cos pitch)·dist   → 永远在**焦点 -Z 方向**
#:   而车头是 Unity +Z（WorldView.cs:999），所以游戏相机**始终在车尾那一侧**；
#:   pitch 的实参是 60°（TableDatabase.cs:186）。
#:   换算到 Blender：Unity -Z = Blender +Y = 车尾方向 → 正是 a=180°。
#:   第一版按字面挑了 200°，看着"差不多"，但它多出来的 20° 会让验证图
#:   与实机观感对不上 —— 验收视角必须和实机同源。
VIEWS = [
    ("top60", 180.0, 60.0, "60° 俯视（**实机相机同源**：车尾侧、pitch=60°）"),
    ("rear34", 205.0, 34.0, "3/4 后视（看得见货斗里整片水果）"),
    ("side", 90.0, 12.0, "侧面（看轮廓与货斗）"),
    ("front", 0.0, 12.0, "正面（看车头朝向）"),
]


def _neutral_view_transform():
    """把渲染的显示变换设成 Standard。

    ★ 为什么必须做 ★ Blender 4+/5 的默认 view transform 是 Filmic/AgX，
    它会把高饱和色往灰里压 —— 调色板里 #C8382F 的红苹果渲出来是**灰粉**，
    照着这张图会误判成"水果颜色不够跳"，然后去改本来正确的调色板。
    这条和第 ② 判据的错误是同一类：**量法本身把结果读歪了**。
    用 'Standard' 渲出来的才是材质基色的直读值。
    """
    try:
        vs = bpy.context.scene.view_settings
        for name in ('Standard', 'Raw'):
            try:
                vs.view_transform = name
                return name
            except Exception:                      # noqa: BLE001
                continue
    except Exception as e:                         # noqa: BLE001
        print(f"[VERIFY] 设 view_transform 失败：{e}")
    return None


def render_previews():
    """出 4 张视图（60° 车尾俯视 / 3-4 后视 / 侧 / 正）。返回文件清单。"""
    fresh()
    objs = import_fbx(STATIC_FBX)
    meshes = [o for o in objs if o.type == 'MESH']
    if not meshes:
        print("[VERIFY] ★没有网格，无法出图★")
        return []
    vt = _neutral_view_transform()
    print(f"[VERIFY] view_transform = {vt}（避免 AgX/Filmic 抽掉饱和度）")

    # 取景：用整体包围盒中心与外接球半径（和 gen_pig 一致的做法）
    pts = []
    for o in meshes:
        mw = o.matrix_world
        pts += [mw @ v.co for v in o.data.vertices]
    cx = (min(p.x for p in pts) + max(p.x for p in pts)) * 0.5
    cy = (min(p.y for p in pts) + max(p.y for p in pts)) * 0.5
    cz = (min(p.z for p in pts) + max(p.z for p in pts)) * 0.5
    radius = max(max(p.x for p in pts) - min(p.x for p in pts),
                 max(p.y for p in pts) - min(p.y for p in pts),
                 max(p.z for p in pts) - min(p.z for p in pts)) * 0.5
    center = (cx, cy, cz)
    print(f"[VERIFY] 取景 center={tuple(round(v, 3) for v in center)} radius={radius:.3f}")

    out = []
    for tag, ang, elev, _desc in VIEWS:
        p = os.path.join(PREVIEW_DIR, f"{gt.ID}_{tag}.png")
        kit.ensure_dir(p)
        try:
            # ★ ambient 0.40 + key 0.6 ★
            #   预览的默认值（ambient 0.55 + key 2.4 + fill 0.8 = 总能量 3.2）会把
            #   #B4553F 的晒褪砖红冲成"粉白"、把水果冲成粉彩。
            #   key=1.5 仍然过曝：受光面直接>1.0 被裁到白，颜色还是丢。
            #   key=0.6 + ambient=0.40 时受光面约 (1-0.4)*0.6+0.4 = 0.76，
            #   不裁白，渲出来的色相才和调色板对得上 —— 这一步是**校准量法**，
            #   不是改模型（第一版我照着过曝的图差点去改本来正确的调色板）。
            _neutral_view_transform()      # ★ 必须在 render 之前 ★
            kit.render_preview(p, center, radius, size=(640, 560),
                               angle=ang, elev=elev, dist_mul=2.5,
                               ambient=0.40, key_energy=0.6)
            out.append(p)
            print(f"[VERIFY] 出图 -> {p}")
        except Exception as e:                     # noqa: BLE001
            print(f"[VERIFY] ★渲染失败 {p}: {e}★")
    return out


# ================================================================ 入口
def _args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def main():
    a = _args() or ["all"]
    cmd = a[0]
    if cmd == "verify":
        verify()
    elif cmd == "preview":
        render_previews()
    else:
        verify()
        render_previews()


if __name__ == "__main__":
    main()
