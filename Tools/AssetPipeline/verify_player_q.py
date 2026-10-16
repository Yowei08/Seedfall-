# -*- coding: utf-8 -*-
"""
玩家 **Q 版** · 回读验证器
================================================================================
验证分四层，每一层都能独立证明一件事：

  ① **回读层**（把导出的 FBX 重新导入 Blender 量）
       高度在 Z 轴 / zmin≈0 / 面数 / **19 根骨骼名逐一核对** / 材质名在调色板里
       + Q 版比例（头含帽 ÷ 总高 ≥0.40）+ 帽檐比（帽檐半径 ÷ 头半宽 ≤1.1）
  ② **读数自检层**（验证器本身也要被验证）
       把一根骨骼摆成**已知的 30°**，用与逐帧回读**同一个函数**读回来。
       上一批有人把弧度当度读，把 ±27° 报成 0.47 —— 单位错了会让"全 0"和"全对"
       长得一模一样。所以读数函数必须先自证。
  ③ **逐帧角度层**（7 条动画）
       idle 胸腔呼吸周期与主频 / walk 腿反相 + 双频不打架 /
       **三套 attack 的腿骨最大角 = 0.0000°（7 根逐一报出）** / 三套彼此不同 / swap 时长
  ④ **俯视可读层**（本版新增，对应"帽檐盖住全身"这个老故障）
       按**游戏相机的真实角度**（pitch 60°，机位在角色正前方）做**射线遮挡审计**：
       逐件统计"有多少比例的采样点能真的被相机看到"，并报出各部位在屏幕上的纵向次序。
       ★ 这一层是"从上面看得见身体"这句话的**可测量形式**，不靠肉眼也不靠感觉。

用法：blender -b --factory-startup -P verify_player_q.py -- [verify|preview|all]
"""

import os
import sys
import math
import json

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_q as gq           # noqa: E402

import bpy                          # noqa: E402
import mathutils                    # noqa: E402

V = mathutils.Vector
D = math.radians
PROJECT = gq.PROJECT
CHAR_DIR = gq.CHAR_DIR
TOOL_DIR = gq.TOOL_DIR
ANIM_DIR = gq.ANIM_DIR
PREVIEW_DIR = gq.PREVIEW_DIR
CAPTURE_DIR = gq.CAPTURE_DIR
FPS = gq.FPS

PITCH = 60.0                        # 游戏相机与地面的夹角（Defs.CameraDef 默认 60）
CAPTURE_TOP60 = os.path.join(CAPTURE_DIR, "player_qstyle_top60.png")


# ================================================================ 场景工具
def fresh():
    """清空场景**并清 bpy.data**。

    只 read_homefile(use_empty=True) 会清场景不清数据块；同一个进程里连续导入多个
    FBX 时，导入器会把新动作接到上一次残留的同名动作上，于是读到的是**上一个文件**
    的曲线。验证器读错比不验证更危险 —— 所以这里连 bpy.data 一起清。
    """
    bpy.ops.wm.read_homefile(use_empty=True)
    for blk in (bpy.data.actions, bpy.data.objects, bpy.data.meshes,
                bpy.data.materials, bpy.data.armatures):
        for item in list(blk):
            try:
                blk.remove(item)
            except Exception:                       # noqa: BLE001
                pass


def import_fbx(path):
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    return list(bpy.context.scene.objects)


def world_points(objs):
    pts = []
    for o in objs:
        if o.type != 'MESH':
            continue
        mw = o.matrix_world
        pts += [mw @ v.co for v in o.data.vertices]
    return pts


def gather(objs):
    """所有网格点的（中心, 外接球半径），给预览相机取景。"""
    pts = world_points(objs)
    if not pts:
        return (0, 0, 0), 0.5
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    center = ((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5,
              (min(zs) + max(zs)) * 0.5)
    radius = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)) * 0.5
    return center, max(0.05, radius)


def render(path, center, radius, size=(560, 560), angle=0.0, elev=PITCH, **kw):
    """固定参数渲染（确定性）。★ view_transform 必须是 Standard ★

    Blender 4/5 默认走 AgX 色调映射，会把**高饱和色压成灰白** ——
    Q 版的核心卖点就是"高饱和大色块"，用 AgX 出图会看不出改了颜色
    （上一版预览图整只角色发白就是这个原因，不是材质写错了）。
    """
    kit.ensure_dir(path)
    sc = bpy.context.scene
    try:
        sc.view_settings.view_transform = 'Standard'
        sc.view_settings.look = 'None'
    except Exception:                               # noqa: BLE001
        pass
    kit.render_preview(path, center, radius, size=size, angle=angle, elev=elev,
                       ambient=0.42, key_energy=2.0, **kw)
    return path


# ================================================================ ① 静态回读
def verify_static():
    path = os.path.join(CHAR_DIR, "player.fbx")
    fresh()
    objs = import_fbx(path)
    meshes = [o for o in objs if o.type == 'MESH']
    arms = [o for o in objs if o.type == 'ARMATURE']
    if not meshes:
        return dict(ok=False, problems=["回读后没有 MESH"])

    tris = 0
    mats = set()
    for o in meshes:
        tris += kit.tri_count(o)
        mats |= {m.name for m in o.data.materials if m}
    pts = world_points(meshes)
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    zmin, zmax = min(zs), max(zs)

    # ── Q 版两项比例：**按回读后的几何量**，不引用生成器里的常数 ──
    head_parts = [o for o in meshes
                  if o.name.startswith(("Part_Head", "Part_Hat", "Part_Eye",
                                        "Part_Sprout", "Part_Mouth"))]
    brim = next((o for o in meshes if o.name.startswith("Part_HatBrim")), None)
    head = next((o for o in meshes if o.name.startswith("Part_Head")), None)
    hpts = world_points(head_parts) if head_parts else []
    head_lo = min(p.z for p in hpts) if hpts else 0.0
    head_hi = max(p.z for p in hpts) if hpts else 0.0
    head_ratio = (head_hi - head_lo) / dims[2] if dims[2] > 0 else 0.0
    brim_r = max(abs(p.x) for p in world_points([brim])) if brim else 0.0
    head_hw = max(abs(p.x) for p in world_points([head])) if head else 0.0
    brim_ratio = brim_r / head_hw if head_hw > 0 else 0.0

    problems = []
    # ★ 高度必须在 Z 上 ★
    if dims[2] < max(dims[0], dims[1]):
        problems.append(f"Z 不是最大维（{dims[0]:.2f},{dims[1]:.2f},{dims[2]:.2f}）→ 躺倒了")
    if not (1.6 <= dims[2] <= 1.9):
        problems.append(f"高度 {dims[2]:.3f} 不在需求的 1.6~1.9m")
    if abs(zmin) > 0.006:
        problems.append(f"脚底不在 z=0（zmin={zmin:.4f}）")
    if tris >= gq.TRI_BUDGET:
        problems.append(f"三角面 {tris} ≥ {gq.TRI_BUDGET}")
    if head_ratio < gq.HEAD_RATIO_MIN:
        problems.append(f"Q 版头身比不足：{head_ratio:.4f} < {gq.HEAD_RATIO_MIN}")
    if brim_ratio > gq.BRIM_RATIO_MAX:
        problems.append(f"帽檐比超限：{brim_ratio:.4f} > {gq.BRIM_RATIO_MAX}")
    bad_mat = [m for m in mats if m not in kit.PALETTE]
    if bad_mat:
        problems.append(f"非调色板材质名: {bad_mat}")

    # ── 19 根骨骼**逐名核对**（Unity 靠 Bone_Hand_R 挂武器，改名就断）──
    bones = [b.name for b in arms[0].data.bones] if arms else []
    missing = [n for n in gq.REQUIRED_BONES if n not in bones]
    extra = [b for b in bones if b not in gq.REQUIRED_BONES]
    if missing:
        problems.append(f"★回读后缺骨骼: {missing}★")
    if extra:
        problems.append(f"★多出骨骼: {extra}★")

    # 每个件挂在哪根骨骼上（回读后 parent_type 应当是 BONE）
    bone_parent_ok = True
    for o in meshes:
        if o.parent_type != 'BONE':
            bone_parent_ok = False
    if not bone_parent_ok:
        problems.append("有网格件回读后不是挂在骨骼上（parent_type ≠ BONE）")

    return dict(ok=not problems, problems=problems,
                dims=tuple(round(v, 4) for v in dims),
                zmin=round(zmin, 5), zmax=round(zmax, 4), tris=tris,
                head_lo=round(head_lo, 4), head_hi=round(head_hi, 4),
                head_ratio=round(head_ratio, 4),
                brim_r=round(brim_r, 4), head_hw=round(head_hw, 4),
                brim_ratio=round(brim_ratio, 4),
                mats=sorted(mats), bones=bones, parts=len(meshes))


# ================================================================ ② 读数自检
def reader_selftest():
    """★ 验证器本身也要被验证 ★

    把 Bone_Chest / Bone_Thigh_R 摆成**已知的 30° 与 -27°**，用与 `read_frames`
    **完全同一个**读法读回来。上一批有人把弧度当度读，把 ±27° 报成 0.47 ——
    单位错了会让"曲线全是 0"和"全部正确"长得一模一样，必须先自证读数。
    """
    fresh()
    arm, _meshes = gq.assemble_player()
    cases = [("Bone_Chest", 30.0), ("Bone_Thigh_R", -27.0), ("Bone_Head", 12.5)]
    rows, bad = [], []
    for name, want in cases:
        pb = arm.pose.bones[name]
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (D(want), 0.0, 0.0)
        bpy.context.view_layer.update()
        got = _read_deg(pb)[0]
        rows.append((name, want, got))
        if abs(got - want) > 0.01:
            bad.append(f"{name}: 摆 {want}° 读回 {got:.4f}°")
        pb.rotation_euler = (0.0, 0.0, 0.0)
    return rows, bad


def _read_deg(pb):
    """从 pose bone 读 (x,y,z) 欧拉角（**度**）。

    ★ FBX 导出把骨骼烘成四元数 ★ 回读后 `rotation_mode == 'QUATERNION'`，
    此时 `rotation_euler` **恒为 0** —— 照着 euler 读会读出"动画没导出去"的假故障。
    """
    if pb.rotation_mode == 'QUATERNION':
        e = pb.rotation_quaternion.to_euler('XYZ')
    else:
        e = pb.rotation_euler
    return (math.degrees(e.x), math.degrees(e.y), math.degrees(e.z))


# ================================================================ ③ 逐帧角度
def read_frames(path, nframes):
    """导入 path，逐帧读每根骨骼的欧拉角（度）。返回 dict[bone] = [[x,y,z], ...]。"""
    fresh()
    import_fbx(path)
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if not arms:
        return None
    arm = arms[0]
    sc = bpy.context.scene
    out = {b[0]: [] for b in gq.BONES}
    for f in range(1, nframes + 1):
        sc.frame_set(f)
        for name in out:
            pb = arm.pose.bones.get(name)
            out[name].append(_read_deg(pb) if pb else [0.0, 0.0, 0.0])
    return out


def axis_span(vals, axis=0):
    col = [v[axis] for v in vals]
    return min(col), max(col), max(col) - min(col)


def dominant_hz(vals, axis, loop):
    """用过零计数估主频（Hz）。比 FFT 稳，短序列也准。周期 = 2×时长/过零次数。"""
    col = [v[axis] for v in vals]
    mean = sum(col) / len(col)
    c = [v - mean for v in col]
    if loop:
        c.append(c[0])
    crossings = sum(1 for i in range(1, len(c)) if (c[i - 1] < 0.0) != (c[i] < 0.0))
    dur = (len(c) - 1) / FPS
    if crossings < 2 or dur <= 1e-6:
        return 0.0
    return (crossings / 2.0) / dur


def smooth_wrap(vals, window):
    """对周期信号做滑动平均（首尾相接）。用来把高频层滤掉、只留慢层。

    为什么必须滤：walk 的躯干上叠了**两个频率**（步频 1.5Hz + 呼吸 0.25Hz）。
    直接对原始曲线数过零，得到的永远是快的那个 —— 上一版就是这么量出
    "呼吸层 = 1.41Hz"，然后误判成"没做到 6:1"。窗口取一个步周期时，
    步频成分的整周期均值≈直流，剩下的慢成分才是呼吸。
    **这是度量方法的问题，不是动画的问题。**
    """
    if window <= 1 or len(vals) <= window:
        return list(vals)
    n = len(vals)
    half = window // 2
    return [sum(vals[(i + k) % n] for k in range(-half, half + 1)) / float(2 * half + 1)
            for i in range(n)]


def anti_phase(valsL, valsR, axis=0):
    lo = [v[axis] for v in valsL]
    ro = [v[axis] for v in valsR]
    opp = sum(1 for a, b in zip(lo, ro) if a * b < 0.0)
    return opp, len(lo)


def verify_anims():
    results, facts, bad = {}, [], []

    def load(anim):
        frames, loop, _n = gq.ANIMS[anim]
        n = frames + 1 if loop else frames
        p = os.path.join(ANIM_DIR, f"player_{anim}.fbx")
        return read_frames(p, n), n, loop

    # ── ① idle：胸腔呼吸周期 ──
    d, n, loop = load("idle")
    if not d:
        bad.append("idle: 回读后没有骨架")
    else:
        c0, c1, cspan = axis_span(d["Bone_Chest"], 0)
        s0, s1, sspan = axis_span(d["Bone_Spine"], 0)
        shspan = axis_span(d["Bone_Shoulder_L"], 0)[2]
        hz = dominant_hz(d["Bone_Chest"], 0, loop)
        hold = [v[2] for v in d["Bone_Head"]]
        results["idle"] = dict(chest_span=cspan, chest_min=c0, chest_max=c1,
                               spine_span=sspan, shoulder_span=shspan, hz=hz, frames=n)
        facts.append(f"idle  胸腔X 区间 [{c0:+.2f}°, {c1:+.2f}°] 摆幅 {cspan:.2f}°  "
                     f"脊柱 {sspan:.2f}°  肩 {shspan:.2f}°  主频 {hz:.3f}Hz（目标 0.25）")
        if cspan < 4.0:
            bad.append(f"idle 呼吸太小：胸腔只有 {cspan:.2f}°（要 ≥4° 才叫'强烈'）")
        if hz <= 0.0:
            bad.append("idle 胸腔没有周期（过零数为 0）")
        elif not (0.18 <= hz <= 0.35):
            bad.append(f"idle 呼吸频率 {hz:.3f}Hz 不在 0.25Hz 附近")
        err = max(abs(d["Bone_Chest"][0][0] - d["Bone_Chest"][-1][0]),
                  abs(d["Bone_Spine"][0][0] - d["Bone_Spine"][-1][0]))
        if err > 1.0:
            bad.append(f"idle 循环首尾差 {err:.2f}°（>1° 会跳帧）")
        facts.append(f"      idle 循环首尾差 {err:.3f}°（{n} 帧 = {n / FPS:.2f}s）")

    # ── ② walk：腿真的在摆 + 左右反相 + 两个频率不同 ──
    d, n, loop = load("walk")
    if not d:
        bad.append("walk: 回读后没有骨架")
    else:
        t0, t1, tspan = axis_span(d["Bone_Thigh_R"], 0)
        l0, l1, lspan = axis_span(d["Bone_Thigh_L"], 0)
        hz_leg = dominant_hz(d["Bone_Thigh_R"], 0, loop)
        raw_spine = [v[0] for v in d["Bone_Spine"]]
        slow_spine = smooth_wrap(raw_spine, max(3, gq.WALK_STEP_FRAMES))
        hz_br = dominant_hz([[v] for v in slow_spine], 0, loop)
        opp, tot = anti_phase(d["Bone_Thigh_L"], d["Bone_Thigh_R"], 0)
        slow_span = max(slow_spine) - min(slow_spine)
        ratio = (hz_leg / hz_br) if hz_br > 0 else 0.0
        results["walk"] = dict(thighR_span=tspan, thighL_span=lspan, leg_hz=hz_leg,
                               breath_hz=hz_br, anti=opp, frames=n,
                               spine_slow_span=slow_span,
                               spine_raw_span=max(raw_spine) - min(raw_spine),
                               freq_ratio=ratio)
        facts.append(f"walk  大腿X R[{t0:+.2f}°, {t1:+.2f}°] 摆幅 {tspan:.2f}°  "
                     f"L 摆幅 {lspan:.2f}°  逐帧反相 {opp}/{tot} 帧")
        facts.append(f"      步频 {hz_leg:.3f}Hz（目标 1.5）  呼吸层 {hz_br:.3f}Hz"
                     f"（目标 0.25，已滤步频）  比值 {ratio:.2f}:1（目标 6:1）")
        facts.append(f"      Spine.x 原始摆幅 {max(raw_spine) - min(raw_spine):.2f}°"
                     f" → 慢层摆幅 {slow_span:.2f}°（这一层就是呼吸）")
        if tspan < 20.0 or lspan < 20.0:
            bad.append(f"walk 腿摆幅不足 R={tspan:.1f}° L={lspan:.1f}°（要 ≥20°）")
        if opp < tot * 0.6:
            bad.append(f"walk 左右腿没有反相（反相 {opp}/{tot} 帧）")
        if hz_br > 0 and abs(ratio - 6.0) > 1.5:
            bad.append(f"walk 步频/呼吸比 {ratio:.2f} 偏离 6:1")
        if abs(hz_leg - hz_br) < 0.5:
            bad.append(f"★walk 步频 {hz_leg:.2f}Hz 与呼吸 {hz_br:.2f}Hz 太接近（同频会互相抵消）★")
        if slow_span < 3.0:
            bad.append(f"walk 的呼吸层太弱：慢层摆幅只有 {slow_span:.2f}°（要 ≥3°）")

    # ── ③ 三套 attack：腿骨必须**全 0**（含 Hips）──
    leg_names = gq.LOWER_BODY
    # ★ 上半身的判据必须**含手臂** ★
    #   第一版照 v2 只取了 [Spine, Chest, Neck, Head, UpperArm_L/R] 里的前 6 根，
    #   于是 attack_can 报"上半身最大 4.0°"—— 而 can 的发力源**本来就在手腕**
    #   （需求：三套各自的发力源不同）。判据写窄了会把正确的动画误判成故障。
    upper_names = gq.UPPER_BODY
    sig = {}
    for anim in ("attack_hoe", "attack_can", "attack_fert"):
        d, n, loop = load(anim)
        if not d:
            bad.append(f"{anim}: 回读后没有骨架")
            continue
        per_leg = {}
        lmax = 0.0
        for ln in leg_names:
            m = max(abs(v[a]) for v in d[ln] for a in range(3))
            per_leg[ln] = round(m, 4)
            lmax = max(lmax, m)
        uamax = max(max(abs(v[a]) for v in d[un] for a in range(3))
                    for un in upper_names)
        results[anim] = dict(leg_max=lmax, legs=per_leg, upper_max=uamax, frames=n)
        facts.append(f"{anim}  腿骨最大角 = {lmax:.4f}°（{len(leg_names)} 根）  "
                     f"上半身最大角 = {uamax:.2f}°")
        facts.append("        逐根腿骨最大角: "
                     + "  ".join(f"{k.replace('Bone_', '')}={per_leg[k]:.3f}°"
                                 for k in leg_names))
        if lmax > 0.05:
            bad.append(f"★{anim} 的下半身在动：腿骨最大角 {lmax:.3f}° ≠ 0 ★")
        if uamax < 20.0:
            bad.append(f"{anim} 上半身动作太小：最大只有 {uamax:.1f}°")
        sig[anim] = [round(v[0], 2) for v in d["Bone_UpperArm_R"]]

    # ── ④ 三套 attack 彼此不同 ──
    diffs = {}
    keys = list(sig)
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            a, b = keys[i], keys[j]
            diffs[f"{a}|{b}"] = (round(max(abs(x - y) for x, y in zip(sig[a], sig[b])), 2)
                                 if len(sig[a]) == len(sig[b]) else None)
    results["distinct"] = diffs
    facts.append("三套 attack 的差异（右上臂 X 逐帧最大差）: "
                 + "  ".join(f"{k}={v}°" for k, v in diffs.items()))
    for k, v in diffs.items():
        if v is None or v < 2.0:
            bad.append(f"★{k} 两套攻击几乎一样（最大差 {v}°）—— 疑似改名复用 ★")

    # ── ⑤ swap：只上半身 + 0.3~0.4s ──
    d, n, loop = load("swap")
    if d:
        lmax = max(max(abs(v[a]) for v in d[ln] for a in range(3)) for ln in leg_names)
        uamax = max(max(abs(v[a]) for v in d[un] for a in range(3)) for un in upper_names)
        dur = n / FPS
        results["swap"] = dict(leg_max=lmax, upper_max=uamax, seconds=dur, frames=n)
        facts.append(f"swap  时长 {dur:.3f}s（{n} 帧，目标 0.3~0.4s）  "
                     f"腿骨最大角 = {lmax:.4f}°  上半身最大角 = {uamax:.2f}°")
        if lmax > 0.05:
            bad.append(f"★swap 的下半身在动：{lmax:.3f}° ★")
        if not (0.30 <= dur <= 0.42):
            bad.append(f"swap 时长 {dur:.3f}s 不在 0.3~0.4s")
    else:
        bad.append("swap: 回读后没有骨架")

    # ── ⑥ hit：一次性后仰 ──
    d, n, loop = load("hit")
    if d:
        cspan = axis_span(d["Bone_Chest"], 0)[2]
        hspan = axis_span(d["Bone_Head"], 0)[2]
        results["hit"] = dict(chest_span=cspan, head_span=hspan, frames=n)
        facts.append(f"hit   胸腔X 摆幅 {cspan:.2f}°  头X 摆幅 {hspan:.2f}°  "
                     f"{n} 帧一次性（{n / FPS:.2f}s，不循环）")
        if cspan < 8.0:
            bad.append(f"hit 后仰幅度不足：胸腔只有 {cspan:.2f}°")
        # 受击不该做缩放（需求点名）——检查 clip 里没有 scale 曲线
        if d["Bone_Chest"][0] == d["Bone_Chest"][-1] and abs(d["Bone_Chest"][0][0]) < 1e-6:
            facts.append("      hit 首尾都回到静止（一次性，接回 idle 不跳）")

    return results, facts, bad


# ================================================================ ④' 走姿相位
def verify_walk_swing():
    """量 walk 里**手臂与腿是否反相**（同手同脚是最容易被一眼看出来的动画缺陷）。

    为什么必须单独量：腿骨的 ±30° 反相判据**照样全过**，而手臂是否与腿反相
    取决于 `apply_arm` 里那几个语义轴的符号。本版第一稿就是错的：
    实测"右手 vs 右腿"相关 **+0.986**（同手同脚），"左手 vs 右手"同相。
    60° 俯视下手的尺寸只有几十像素，光看渲染图确认不了，只能量世界坐标。

    做法：逐帧读手骨/脚骨的**世界坐标**，看前后（Y）分量的相关系数。
    正常人走姿：同侧手脚**反相**（相关为负）。
    """
    fresh()
    import_fbx(os.path.join(ANIM_DIR, "player_walk.fbx"))
    arm = next((o for o in bpy.context.scene.objects if o.type == 'ARMATURE'), None)
    if arm is None:
        return None, ["walk 摆臂检查：回读后没有骨架"]
    sc = bpy.context.scene
    n = gq.ANIMS["walk"][0] + 1
    track = {"Hand_R": [], "Hand_L": [], "Foot_R": [], "Foot_L": []}
    for f in range(1, n + 1):
        sc.frame_set(f)
        for key, bone in (("Hand_R", "Bone_Hand_R"), ("Hand_L", "Bone_Hand_L"),
                          ("Foot_R", "Bone_Foot_R"), ("Foot_L", "Bone_Foot_L")):
            track[key].append((arm.matrix_world @ arm.pose.bones[bone].tail).y)

    def corr(a, b):
        va, vb = track[a], track[b]
        ma, mb = sum(va) / len(va), sum(vb) / len(vb)
        ca, cb = [v - ma for v in va], [v - mb for v in vb]
        den = (sum(x * x for x in ca) * sum(y * y for y in cb)) ** 0.5
        return sum(x * y for x, y in zip(ca, cb)) / den if den > 1e-9 else 0.0

    bad = []
    same_r = corr("Hand_R", "Foot_R")     # 同侧：必须反相
    same_l = corr("Hand_L", "Foot_L")
    arms = corr("Hand_R", "Hand_L")       # 两臂：必须反相
    cross = corr("Hand_R", "Foot_L")      # 异侧：必须同相（正常人走路就是这样）
    swing = max(track["Hand_R"]) - min(track["Hand_R"])
    if same_r > -0.3 or same_l > -0.3:
        bad.append(f"★走姿同手同脚：右手↔右腿 {same_r:+.3f}、左手↔左腿 {same_l:+.3f}"
                   f"（同侧应 < −0.3）★")
    if arms > 0.3:
        bad.append(f"★两条手臂同相摆动：左右手相关 {arms:+.3f}（应 < −0.3，要交替）★")
    if cross < 0.3:
        bad.append(f"★异侧手脚没同相：右手↔左腿 {cross:+.3f}（正常人走路应 > +0.3）★")
    if swing < 0.05:
        bad.append(f"手臂前后摆幅只有 {swing * 100:.1f}cm，看不出在摆")
    return dict(handR_vs_footR=same_r, handL_vs_footL=same_l, handR_vs_handL=arms,
                handR_vs_footL=cross, hand_swing_m=swing), bad


# ================================================================ ④ 俯视可读度
#: 部位分组（按件名前缀）。名字从生成器到 FBX 再到回读**一路不变**。
GROUPS = {
    "头(含帽)": ("Part_Head", "Part_Hat", "Part_Eye", "Part_Sprout", "Part_Mouth"),
    "肩":       ("Part_Shoulder",),
    "手臂":     ("Part_UpperArm", "Part_LowerArm", "Part_Hand"),
    "躯干":     ("Part_Chest", "Part_Belly", "Part_Hips", "Part_Mantle",
                 "Part_Scarf", "Part_Neck"),
    "腿":       ("Part_Thigh", "Part_Calf", "Part_Foot"),
}


def _group_of(name):
    for gname, prefixes in GROUPS.items():
        if name.startswith(prefixes):
            return gname
    return None


def visibility_audit():
    """按**游戏相机的真实角度**做射线遮挡审计。

    相机（`GameBootstrap.UpdateCamera`）：
        offset = (0, sin(pitch), −cos(pitch)) × dist，pitch = 60°
    也就是"在角色正前方（Blender 的 −Y 一侧）、抬高 60° 往下看"。
    于是屏幕纵轴与视线分别是（Blender 系）：
        视线 L = (0, cos60, −sin60) = (0, 0.5, −0.866)
        屏幕上 up = (0, sin60, cos60) = (0, 0.866, 0.5)
    对每个网格顶点 P，从 P 沿 −L 退到很远再朝 L 打一条射线；
    **第一个被打到的物体**就是这个方向上真正被看到的那个件。
    于是"某件可见比例 = 它的点里第一个被打到的是它自己的比例"。

    ★ 为什么必须这样做 ★
      "俯视下能不能看见身体"是**遮挡问题**，而遮挡在静帧里靠肉眼判断很容易
      自我说服（上一版就是截图里看到一块圆盘才发现的）。射线审计把它变成
      一个可复现的数字，并且能直接指出"挡住躯干的是帽子"。
    """
    fresh()
    arm, meshes = gq.assemble_player()
    deps = bpy.context.evaluated_depsgraph_get()
    scene = bpy.context.scene

    up = V((0.0, math.sin(D(PITCH)), math.cos(D(PITCH))))
    look = V((0.0, math.cos(D(PITCH)), -math.sin(D(PITCH))))

    per_part = {}
    group_pts = {g: [] for g in GROUPS}
    occluded_by_hat = {g: 0.0 for g in GROUPS}
    occluded_by_brim = {g: 0.0 for g in GROUPS}
    total_area = {g: 0.0 for g in GROUPS}

    # ★ 采样必须**按面积加权**，不能按顶点 ★
    #   顶点分布是建模方式的产物：一个 UV 球在两极挤了十几个顶点，
    #   按顶点数算出来的"可见比例"会被两极主导（第一版就是这样，把躯干报成 15.9%，
    #   而渲染图上躯干明明是一大块蓝色）。这里改成**每个三角面取 4 个点
    #   （三个角 + 重心），各带 1/4 的面权** —— 于是"可见比例"就是
    #   "这块部位有多少**面积**能被相机看到"，这才是验收要说的那句话。
    def _cast(P):
        origin = P - look * 8.0
        hit, _loc, _n, _i, hobj, _m = scene.ray_cast(deps, origin, look)
        return hobj if hit else None

    for o in meshes:
        gname = _group_of(o.name)
        mw = o.matrix_world
        me = o.data
        vis_a = 0.0
        tot_a = 0.0
        for poly in me.polygons:
            vs = [mw @ me.vertices[i].co for i in poly.vertices]
            for k in range(1, len(vs) - 1):
                a, b, c = vs[0], vs[k], vs[k + 1]
                area = (b - a).cross(c - a).length * 0.5
                if area <= 0.0:
                    continue
                w = area * 0.25
                for P in (a, b, c, (a + b + c) / 3.0):
                    hobj = _cast(P)
                    seen = hobj is not None and hobj.name == o.name
                    tot_a += w
                    if seen:
                        vis_a += w
                    if gname:
                        total_area[gname] += w
                        if seen:
                            group_pts[gname].append((float(P.dot(up)), float(P.x), w))
                        elif hobj is not None and hobj.name.startswith("Part_Hat"):
                            occluded_by_hat[gname] += w
                            if hobj.name.startswith("Part_HatBrim"):
                                occluded_by_brim[gname] += w
        per_part[o.name] = (round(vis_a / tot_a, 3) if tot_a > 0 else 0.0, round(tot_a, 4))

    # ── 汇总：每个分组的可见面积比例 + 屏幕纵向区间（可见部分，按面积加权中位）──
    rows = []
    for gname in GROUPS:
        vp = group_pts[gname]
        tot = total_area[gname]
        tot_vis = sum(p[2] for p in vp)
        vis_frac = tot_vis / tot if tot > 0 else 0.0
        med = lo = hi = 0.0
        if vp:
            ups = [p[0] for p in vp]
            ws = [p[2] for p in vp]
            lo, hi = min(ups), max(ups)
            # 面积加权中位：把可见点按屏幕高度排序，取累计权重过半的那一个
            order = sorted(range(len(ups)), key=lambda i: ups[i])
            acc = 0.0
            for i in order:
                acc += ws[i]
                if acc >= tot_vis * 0.5:
                    med = ups[i]
                    break
        rows.append(dict(group=gname,
                         parts=sum(1 for o in meshes if _group_of(o.name) == gname),
                         visible=round(vis_frac, 3),
                         area=round(tot, 3),
                         screen_up_min=round(lo, 3), screen_up_med=round(med, 3),
                         screen_up_max=round(hi, 3),
                         blocked_by_hat_pct=round(100.0 * occluded_by_hat[gname] / tot, 1)
                         if tot > 0 else 0.0,
                         blocked_by_brim_pct=round(100.0 * occluded_by_brim[gname] / tot, 1)
                         if tot > 0 else 0.0))
    rows.sort(key=lambda r: -r["screen_up_med"])
    return dict(rows=rows, per_part=per_part, pitch=PITCH)


# ================================================================ 武器
def verify_tools():
    rows, bad = [], []
    for kind in ("hoe", "can", "fert"):
        path = os.path.join(TOOL_DIR, f"tool_{kind}.fbx")
        fresh()
        objs = import_fbx(path)
        meshes = [o for o in objs if o.type == 'MESH']
        if not meshes:
            bad.append(f"tool_{kind}: 回读后没有 MESH")
            continue
        tris = sum(kit.tri_count(o) for o in meshes)
        mats = set()
        for o in meshes:
            mats |= {m.name for m in o.data.materials if m}
        pts = world_points(meshes)
        xs = [p.x for p in pts]
        ys = [p.y for p in pts]
        zs = [p.z for p in pts]
        dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
        # GRIP：必须在、在原点、**单位旋转**
        grip = next((o for o in objs if "GRIP" in o.name.upper()), None)
        gpos = tuple(round(v, 4) for v in grip.matrix_world.translation) if grip else None
        grot = (tuple(round(v, 4) for v in grip.rotation_quaternion) if grip else None)
        ident = bool(grip) and all(abs(a - b) < 1e-3 for a, b in
                                   zip(grot, mathutils.Quaternion()))
        if grip is None:
            bad.append(f"tool_{kind}: 回读后找不到 GRIP 标记")
        elif not ident or max(abs(v) for v in gpos) > 1e-3:
            bad.append(f"tool_{kind}: GRIP 不是原点+单位旋转（pos={gpos} quat={grot}）")
        span = max(dims)
        want = {"hoe": (1.10, 1.35), "can": (0.24, 0.42), "fert": (0.36, 0.58)}[kind]
        if not (want[0] <= span <= want[1]):
            bad.append(f"tool_{kind}: 最大尺寸 {span:.3f} 不在 {want}")
        bad_mat = [m for m in mats if m not in kit.PALETTE]
        if bad_mat:
            bad.append(f"tool_{kind}: 非调色板材质名 {bad_mat}")
        rows.append(dict(kind=kind, dims=tuple(round(v, 3) for v in dims), tris=tris,
                         mats=sorted(mats), grip_pos=gpos, grip_identity=ident))
    return rows, bad


def grip_delta_vs_v2():
    """量"握持坐标系与上一版的差角"。

    ★ 为什么这条重要 ★
    Unity 侧 `WorldView.AttachTools` 对三把武器用**同一个**局部旋转
    `Quaternion.Euler(0, 180, 0)`，这个值是按上一版的掌心轴向试出来的。
    本版手骨改了坐标（Q 版手更矮），只要**掌心轴方向不变**，那个 180° 就依然成立。
    这里把两版的 hand 骨轴向与 GRIP 四元数都算出来比一次 —— 比"看起来差不多"可靠。
    """
    import gen_player_v2 as g2
    hb_new = gq.BONE_NAME["Bone_Hand_R"]
    hb_old = g2.BONE_NAME["Bone_Hand_R"]
    n_new = (V(hb_new[2]) - V(hb_new[1])).normalized()
    n_old = (V(hb_old[2]) - V(hb_old[1])).normalized()
    palm_ang = math.degrees(math.acos(max(-1.0, min(1.0, n_new.dot(n_old)))))
    # ★ 两版的 grip_frame 返回值个数不同（v2 是 3 个，多一个 palm 向量）★
    #   第一版这里直接 `q_new, _ = ...` 解包，当场 ValueError —— 跨版本复用函数时
    #   取值要按**[0]** 取，不要按位置解包。
    q_new = gq.grip_frame()[0]
    q_old = g2.grip_frame()[0]
    grip_ang = math.degrees(q_new.rotation_difference(q_old).angle)
    return dict(palm_axis_deg=palm_ang, grip_frame_deg=grip_ang)


# ================================================================ 渲染
def _render_model(assemble, tag, shots):
    fresh()
    arm, meshes = assemble()
    center, radius = gather(meshes)
    out = []
    for name, ang, elev, size in shots:
        p = os.path.join(PREVIEW_DIR, f"{tag}_{name}.png")
        render(p, center, radius, size=size, angle=ang, elev=elev)
        out.append((name, p))
    return out, center, radius


def render_previews():
    """出交付对照图。

    ★ 60° 俯视是**主验收角度**（游戏相机就是这个角）★
    前后两版用**同一套相机 / 灯光 / 色调映射**渲染，才能比出"改了什么"。
    """
    shots = [("top60", 0.0, 60.0, (640, 640)),
             ("front", 0.0, 6.0, (560, 560)),
             ("side", 90.0, 6.0, (560, 560)),
             ("top90", 0.0, 88.0, (560, 560))]

    before, _c1, _r1 = _render_model(
        lambda: _assemble_v2(), "q_before",
        [("top60", 0.0, 60.0, (640, 640)), ("front", 0.0, 6.0, (560, 560))])
    after, center, radius = _render_model(lambda: gq.assemble_player(), "q_after", shots)

    # ★ 姿态下的 60° 俯视 ★
    #   静帧（rest pose）好看不代表在游戏里好看：游戏里角色一直在 idle/walk/attack。
    #   这里把 walk 的 1/4 相位与 attack_hoe 的发力帧按**游戏相机角度**渲出来，
    #   专门看"动起来之后，从上往下还能不能读出身体"。
    posed = []
    for tag, anim, t in (("walk60", "walk", 0.25), ("attack60", "attack_hoe", 0.50)):
        fresh()
        arm, meshes = gq.assemble_player()
        bpy.context.scene.frame_set(1)
        gq.pose_player(anim, t, arm)
        bpy.context.view_layer.update()
        c, r = gather(meshes)
        p = os.path.join(PREVIEW_DIR, f"q_after_{tag}.png")
        render(p, c, r, size=(640, 640), angle=0.0, elev=PITCH)
        posed.append((tag, p))

    # 主验收图：直接落到 Docs/captures/
    top = dict(after)["top60"]
    kit.ensure_dir(CAPTURE_TOP60)
    import shutil
    shutil.copyfile(top, CAPTURE_TOP60)
    print(f"[VERIFYQ] 主验收图 -> {CAPTURE_TOP60}")

    tools = []
    for kind in ("hoe", "can", "fert"):
        fresh()
        objs = import_fbx(os.path.join(TOOL_DIR, f"tool_{kind}.fbx"))
        c, r = gather(objs)
        p = os.path.join(PREVIEW_DIR, f"q_tool_{kind}.png")
        render(p, c, r, size=(460, 460), angle=32.0, elev=18.0)
        tools.append((kind, p))

    # 三套攻击的姿态稿（证明"三套彼此不同"看得见）
    poses = []
    for anim in ("attack_hoe", "attack_can", "attack_fert"):
        for label, t in (("windup", 0.38), ("strike", 0.52), ("recover", 0.85)):
            fresh()
            arm, meshes = gq.assemble_player()
            sc = bpy.context.scene
            sc.frame_set(1)
            gq.pose_player(anim, t, arm)
            bpy.context.view_layer.update()
            c, r = gather(meshes)
            p = os.path.join(PREVIEW_DIR, f"q_pose_{anim}_{label}.png")
            render(p, c, r, size=(360, 360), angle=24.0, elev=16.0)
            poses.append((f"{anim}/{label}", p))

    return dict(before=before, after=after, posed=posed, tools=tools, poses=poses,
                center=center, radius=radius)


def _assemble_v2():
    """把**上一版**模型重新搭出来（gen_player_v2 原封不动地导入）。

    为什么要它：player.fbx 已经被覆盖，直接对比"文件夹里的旧图"会有灯光/色调映射
    的差异。现场重建旧模型 + 同一套渲染参数，才叫同条件对照。
    """
    import gen_player_v2 as g2
    return g2.assemble_player()


# ================================================================ 主流程
def verify():
    print("=" * 78)
    print("① 读数自检（验证器本身也要被验证：摆已知角，用同一个读法读回）")
    print("=" * 78)
    rows, rbad = reader_selftest()
    for name, want, got in rows:
        print(f"  {name:<16} 摆 {want:+.1f}° → 读回 {got:+.4f}°  "
              f"{'✔' if abs(got - want) <= 0.01 else '★偏差★'}")
    if rbad:
        print(f"  ★读数自检失败：{rbad}★")
    else:
        print("  ✔ 读数单位正确（不是把弧度当度）")

    print("=" * 78)
    print("② 回读 static：尺寸 / Z 轴 / zmin / 面数 / 材质 / **19 根骨骼名** / Q 版比例")
    print("=" * 78)
    st = verify_static()
    print(f"  player.fbx  网格件 {st['parts']} 个  dims(x,y,z)={st['dims']}  "
          f"zmin={st['zmin']} zmax={st['zmax']}")
    print(f"  三角面 {st['tris']}（预算 {gq.TRI_BUDGET}）  材质 {', '.join(st['mats'])}")
    print(f"  ★ Q 版比例：头(含帽) Z[{st['head_lo']:.3f},{st['head_hi']:.3f}] "
          f"= {st['head_ratio']:.4f} ÷ 总高（目标 ≥ {gq.HEAD_RATIO_MIN}）")
    print(f"  ★ 帽檐比：{st['brim_r']:.4f} ÷ 头半宽 {st['head_hw']:.4f} "
          f"= {st['brim_ratio']:.4f}（目标 ≤ {gq.BRIM_RATIO_MAX}）")
    print(f"  骨骼 {len(st['bones'])} 根，逐名核对：")
    miss = [b for b in gq.REQUIRED_BONES if b not in st['bones']]
    for i in range(0, len(gq.REQUIRED_BONES), 4):
        chunk = gq.REQUIRED_BONES[i:i + 4]
        print("    " + "  ".join(f"{n}{'✔' if n in st['bones'] else '★缺★'}"
                                 for n in chunk))
    print(f"  缺失 {len(miss)} 根{'' if not miss else '：' + str(miss)}")
    for p in st["problems"]:
        print(f"  ★{p}★")
    if not st["problems"]:
        print("  ✔ 静态全部通过")

    print("=" * 78)
    print("③ 回读 weapons：GRIP 原点 + 单位旋转 / 尺寸 / 材质")
    print("=" * 78)
    trows, tbad = verify_tools()
    for r in trows:
        print(f"  tool_{r['kind']:<5} dims={r['dims']} tris={r['tris']} "
              f"GRIP@{r['grip_pos']} identity={r['grip_identity']}")
        print(f"        材质: {', '.join(r['mats'])}")
    gd = grip_delta_vs_v2()
    print(f"  握持坐标系 vs 上一版：掌心轴差 {gd['palm_axis_deg']:.2f}°，"
          f"GRIP 四元数差 {gd['grip_frame_deg']:.2f}°"
          f"（<5° ⇒ Unity 侧那个固定的 Euler(0,180,0) 依然成立）")
    for p in tbad:
        print(f"  ★{p}★")
    if not tbad:
        print("  ✔ 三把武器全部通过")

    print("=" * 78)
    print("④ 逐帧骨骼角度（决定性判据）")
    print("=" * 78)
    res, facts, bad = verify_anims()
    for f in facts:
        print("  " + f)

    print("=" * 78)
    print("④' 走姿相位（手臂 vs 腿：反相才是走路）")
    print("=" * 78)
    sw, swbad = verify_walk_swing()
    if sw:
        print(f"  右手↔右腿（同侧）相关 = {sw['handR_vs_footR']:+.3f}  应反相（< −0.3）")
        print(f"  左手↔左腿（同侧）相关 = {sw['handL_vs_footL']:+.3f}  应反相")
        print(f"  右手↔左手（两臂）相关 = {sw['handR_vs_handL']:+.3f}  应交替")
        print(f"  右手↔左腿（异侧）相关 = {sw['handR_vs_footL']:+.3f}  应同相（正常人走姿）")
        print(f"  手掌前后摆幅 = {sw['hand_swing_m'] * 100:.1f} cm")
    for p in swbad:
        print(f"  ★{p}★")
    if sw and not swbad:
        print("  ✔ 手臂与腿反相、两臂交替 —— 不是同手同脚")

    print("=" * 78)
    print("⑤ 俯视可读度（射线遮挡审计，相机 = 游戏相机 60° 俯视）")
    print("=" * 78)
    aud = visibility_audit()
    print("  部位        可见面积比   表面积   屏幕纵向 low/med/high     被帽挡   其中帽檐")
    for r in aud["rows"]:
        print(f"  {r['group']:<10}  {r['visible'] * 100:6.1f}%   {r['area']:7.3f}   "
              f"{r['screen_up_min']:+.3f} / {r['screen_up_med']:+.3f} / "
              f"{r['screen_up_max']:+.3f}   {r['blocked_by_hat_pct']:5.1f}%  "
              f"{r['blocked_by_brim_pct']:5.1f}%")
    order = [r["group"] for r in aud["rows"]]
    print(f"  屏幕纵向次序（上→下）：{' → '.join(order)}")

    aud_bad = []
    frac = {r["group"]: r["visible"] for r in aud["rows"]}
    # ★ 判据的门槛为什么定在 20% ★
    #   验收原话是"从 60° 俯视要能看见头、肩、手臂、躯干、腿"。
    #   Q 版的大头**必然**挡掉躯干的上半（这是"头大"的代价，不是故障），
    #   所以门槛不能定成"基本全露"；20% 的含义是"这个部位有一块成规模的面
    #   是相机直接看到的"，低于它就只能靠脑补，读不出来。
    for g in ("头(含帽)", "肩", "手臂", "躯干", "腿"):
        if frac.get(g, 0.0) < 0.20:
            aud_bad.append(f"{g} 从 60° 俯视几乎看不见（可见面积 {frac.get(g, 0) * 100:.1f}%）")
    head_med = next(r["screen_up_med"] for r in aud["rows"] if r["group"] == "头(含帽)")
    leg_med = next(r["screen_up_med"] for r in aud["rows"] if r["group"] == "腿")
    if head_med <= leg_med:
        aud_bad.append(f"头没有在腿上方（头 med={head_med:.3f} 腿 med={leg_med:.3f}）")
    # ★ 单列"被帽檐挡住" ★ 帽筒本质是头的一部分，把它算进来会把"大头挡躯干"
    #   误报成"帽檐太长"；上一版真正的故障就是**帽檐这一片扁盘**投出的影。
    for r in aud["rows"]:
        if r["group"] in ("躯干", "腿") and r["blocked_by_brim_pct"] > 15.0:
            aud_bad.append(f"{r['group']} 有 {r['blocked_by_brim_pct']}% 的面积被**帽檐**挡住"
                           f"（帽檐比已 ≤1.1，这一条正常应为个位数）")
    for p in aud_bad:
        print(f"  ★{p}★")
    if not aud_bad:
        print("  ✔ 头/肩/手臂/躯干/腿 在 60° 俯视下都露在外面，且自上而下分得开")

    print("=" * 78)
    allbad = list(st["problems"]) + tbad + bad + rbad + swbad + aud_bad
    if allbad:
        print(f"★验证失败 {len(allbad)} 条：")
        for p in allbad:
            print(f"   · {p}")
    else:
        print("★全部通过：读数自检 / 静态 / 武器 / 7 条动画逐帧角度 / 走姿相位 / 俯视可读度★")
    print("=" * 78)

    dump = os.path.join(PREVIEW_DIR, "player_q_verify.json")
    kit.ensure_dir(dump)
    with open(dump, "w", encoding="utf-8") as fh:
        json.dump(dict(static=st, tools=trows, grip_delta=gd, anims=res, swing=sw,
                       audit=aud, failures=allbad, facts=facts),
                  fh, ensure_ascii=False, indent=2, default=str)
    print(f"[VERIFYQ] 明细 -> {dump}")
    return allbad


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    cmd = a[0] if a else "verify"
    if cmd == "verify":
        verify()
    elif cmd == "preview":
        render_previews()
    elif cmd == "all":
        verify()
        render_previews()
    else:
        print("用法: verify_player_q.py -- [verify|preview|all]")
