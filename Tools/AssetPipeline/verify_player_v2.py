# -*- coding: utf-8 -*-
"""
玩家 v2 · 回读验证器
====================
把导出的 FBX **重新导入 Blender**，逐帧读骨骼角度。这是唯一能证明
"动画真的写进去了"的手段 —— 曲线恒 0 和 ±30° 在静止模型上长得一模一样，
看预览图永远发现不了（gen_anim 的腿部恒 0° 就是这么漏掉的）。

三条决定性判据：
  ① idle 的胸腔**真的有呼吸周期** —— 报出角度区间与频率（目标 0.25Hz）
  ② walk 的腿**真的在摆**且左右反相 —— 报出角度区间与频率（目标 1.5Hz）
  ③ 三套 attack 的**腿骨曲线全为 0** —— 报出腿骨最大角度（应为 0）
     （D 项的验收点：上半身动、下半身维持移动或站立）

用法：blender -b --factory-startup -P verify_player_v2.py -- [verify|preview|all]
"""

import os
import sys
import math
import json

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import seedfall_kit as kit          # noqa: E402
import gen_player_v2 as gp          # noqa: E402

import bpy                          # noqa: E402
import mathutils                    # noqa: E402

V = mathutils.Vector
D = math.radians
PROJECT = gp.PROJECT
CHAR_DIR = gp.CHAR_DIR
TOOL_DIR = gp.TOOL_DIR
ANIM_DIR = gp.ANIM_DIR
PREVIEW_DIR = gp.PREVIEW_DIR
CAPTURE = gp.CAPTURE
FPS = gp.FPS


# ================================================================ 场景工具
def fresh():
    """清空场景**并清 bpy.data**。

    第一版只用 read_homefile(use_empty=True)，它清场景不清数据块；同一个进程里
    连续导入多个 FBX 时，导入器会把新动作接到上一次残留的同名动作上，
    于是读到的是**上一个文件**的曲线。验证器读错比不验证更危险（gen_plants_biped
    也踩过同一个坑）。
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


# ================================================================ 静态回读
def verify_static():
    path = os.path.join(CHAR_DIR, "player.fbx")
    fresh()
    objs = import_fbx(path)
    meshes = [o for o in objs if o.type == 'MESH']
    arms = [o for o in objs if o.type == 'ARMATURE']
    if not meshes:
        return dict(ok=False, problems=["回读后没有 MESH"])

    pts, tris, mats = [], 0, set()
    for o in meshes:
        mw = o.matrix_world
        pts += [mw @ v.co for v in o.data.vertices]
        tris += kit.tri_count(o)
        mats |= {m.name for m in o.data.materials if m}
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs), min(zs))

    problems = []
    # ★ 高度必须在 Z 上 ★
    if dims[2] < max(dims[0], dims[1]):
        problems.append(f"Z 不是最大维（{dims[0]:.2f},{dims[1]:.2f},{dims[2]:.2f}）→ 躺倒了")
    if abs(dims[2] - gp.TARGET_H) > 0.08:
        problems.append(f"高度 {dims[2]:.3f} 偏离 1.8 超过 8cm")
    if abs(dims[3]) > 0.006:
        problems.append(f"脚底不在 z=0（zmin={dims[3]:.4f}）")
    if tris >= gp.TRI_BUDGET:
        problems.append(f"三角面 {tris} ≥ {gp.TRI_BUDGET}")
    bad_mat = [m for m in mats if m not in kit.PALETTE]
    if bad_mat:
        problems.append(f"非调色板材质名: {bad_mat}")

    bones = [b.name for b in arms[0].data.bones] if arms else []
    missing = [n for n, *_ in gp.BONES if n not in bones]
    if missing:
        problems.append(f"回读后缺骨骼: {missing}")
    extra = [b for b in bones if b not in [n for n, *_ in gp.BONES]]
    return dict(ok=not problems, problems=problems,
                dims=tuple(round(v, 3) for v in dims[:3]),
                zmin=round(dims[3], 5), zmax=round(max(zs), 4), tris=tris,
                mats=sorted(mats), bones=bones, extra_bones=extra,
                parts=len(meshes))


# ================================================================ 动画回读
def read_frames(path, nframes):
    """导入 path，逐帧读每根骨骼的欧拉角（度）。返回 dict[bone] = [[x,y,z], ...]。"""
    fresh()
    import_fbx(path)
    arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
    if not arms:
        return None
    arm = arms[0]
    sc = bpy.context.scene
    out = {}
    for b in gp.BONES:
        out[b[0]] = []
    for f in range(1, nframes + 1):
        sc.frame_set(f)
        for name in out:
            pb = arm.pose.bones.get(name)
            if pb is None:
                out[name].append([0.0, 0.0, 0.0])
                continue
            # ★ FBX 导出把骨骼烘成四元数 ★ 回读后 rotation_mode 是 QUATERNION，
            # 此时 rotation_euler 恒为 0 —— 照 euler 读会读出"动画没导出去"的假故障。
            e = (pb.rotation_quaternion.to_euler('XYZ')
                 if pb.rotation_mode == 'QUATERNION' else pb.rotation_euler)
            out[name].append([math.degrees(e.x), math.degrees(e.y), math.degrees(e.z)])
    return out


def axis_span(vals, axis=0):
    col = [v[axis] for v in vals]
    return min(col), max(col), max(col) - min(col)


def dominant_hz(vals, axis, nframes, loop):
    """用**过零计数**估主频（Hz）。比 FFT 稳，短序列也准。

    周期 T = 2 × 总时长 / 过零次数，频率 = 1/T。
    """
    col = [v[axis] for v in vals]
    mean = sum(col) / len(col)
    c = [v - mean for v in col]
    if loop:
        c.append(c[0])                     # 循环动画闭合，首尾相接
    crossings = 0
    for i in range(1, len(c)):
        if (c[i - 1] < 0.0) != (c[i] < 0.0):
            crossings += 1
    dur = (len(c) - 1) / FPS
    if crossings < 2 or dur <= 1e-6:
        return 0.0
    return (crossings / 2.0) / dur


def smooth_wrap(vals, window):
    """对**周期信号**做滑动平均（首尾相接）。用来把高频层滤掉、只留慢层。

    为什么要这一步：walk 的躯干上叠了**两个频率**（步频 1.5Hz + 呼吸 0.25Hz）。
    直接对原始曲线数过零，得到的永远是快的那个 —— 第一版就是这么量出
    "呼吸层 = 1.41Hz"，然后误判成"没做到 6:1"。
    窗口取一个步周期（= 步长），周期信号的整周期均值≈直流，于是步频成分被滤掉，
    剩下的慢成分才是呼吸。这是**度量方法**的问题，不是动画的问题。
    """
    if window <= 1 or len(vals) <= window:
        return list(vals)
    n = len(vals)
    half = window // 2
    out = []
    for i in range(n):
        acc = 0.0
        for k in range(-half, half + 1):
            acc += vals[(i + k) % n]
        out.append(acc / float(2 * half + 1))
    return out


def anti_phase(valsL, valsR, axis=0):
    lo = [v[axis] for v in valsL]
    ro = [v[axis] for v in valsR]
    opp = sum(1 for a, b in zip(lo, ro) if a * b < 0.0)
    return opp, len(lo)


def verify_anims():
    results = {}
    facts = []
    bad = []

    def load(anim):
        frames, loop, _n = gp.ANIMS[anim]
        n = frames + 1 if loop else frames
        p = os.path.join(ANIM_DIR, f"player_{anim}.fbx")
        r = read_frames(p, n)
        return r, n, loop

    # ── ① idle：胸腔呼吸周期 ──
    d, n, loop = load("idle")
    if not d:
        bad.append("idle: 回读后没有骨架")
    else:
        c0, c1, cspan = axis_span(d["Bone_Chest"], 0)
        s0, s1, sspan = axis_span(d["Bone_Spine"], 0)
        hz = dominant_hz(d["Bone_Chest"], 0, n, loop)
        results["idle"] = dict(chest_span=cspan, chest_min=c0, chest_max=c1,
                               spine_span=sspan, hz=hz, frames=n)
        facts.append(f"idle  胸腔X 区间 [{c0:.2f}°, {c1:.2f}°] 摆幅 {cspan:.2f}°  "
                     f"脊柱 {sspan:.2f}°  主频 {hz:.3f}Hz（目标 0.25）")
        if cspan < 4.0:
            bad.append(f"idle 呼吸太小：胸腔只有 {cspan:.2f}°（要 ≥4° 才叫'强烈'）")
        if hz <= 0.0:
            bad.append("idle 胸腔没有周期（过零数为 0）")
        elif not (0.18 <= hz <= 0.35):
            bad.append(f"idle 呼吸频率 {hz:.3f}Hz 不在 0.25Hz 附近")
        # 循环首尾
        err = max(abs(d["Bone_Chest"][0][0] - d["Bone_Chest"][-1][0]),
                  abs(d["Bone_Spine"][0][0] - d["Bone_Spine"][-1][0]))
        if err > 1.0:
            bad.append(f"idle 循环首尾差 {err:.2f}°（>1° 会跳帧）")
        facts.append(f"      idle 循环首尾差 {err:.3f}°")

    # ── ② walk：腿真的在摆 + 左右反相 + 两个频率不同 ──
    d, n, loop = load("walk")
    if not d:
        bad.append("walk: 回读后没有骨架")
    else:
        t0, t1, tspan = axis_span(d["Bone_Thigh_R"], 0)
        l0, l1, lspan = axis_span(d["Bone_Thigh_L"], 0)
        hz_leg = dominant_hz(d["Bone_Thigh_R"], 0, n, loop)
        # 呼吸层：取 Spine.x 并**滤掉步频成分**（窗口 = 一个步周期 = 16 帧）
        raw_spine = [v[0] for v in d["Bone_Spine"]]
        slow_spine = smooth_wrap(raw_spine, max(3, gp.WALK_STEP_FRAMES))
        hz_br = dominant_hz([[v] for v in slow_spine], 0, n, loop)
        opp, tot = anti_phase(d["Bone_Thigh_L"], d["Bone_Thigh_R"], 0)
        slow_span = max(slow_spine) - min(slow_spine)
        results["walk"] = dict(thighR_span=tspan, thighL_span=lspan, leg_hz=hz_leg,
                               breath_hz=hz_br, anti=opp, frames=n,
                               spine_slow_span=slow_span,
                               spine_raw_span=max(raw_spine) - min(raw_spine))
        facts.append(f"walk  大腿X R[{t0:.2f}°, {t1:.2f}°] 摆幅 {tspan:.2f}°  "
                     f"L 摆幅 {lspan:.2f}°  逐帧反相 {opp}/{tot}")
        facts.append(f"      步频 {hz_leg:.3f}Hz（目标 1.5）  "
                     f"呼吸层 {hz_br:.3f}Hz（目标 0.25，已滤步频）  比值 "
                     f"{hz_leg / hz_br if hz_br > 0 else 0:.2f}:1（目标 6:1）")
        facts.append(f"      Spine.x 原始摆幅 {max(raw_spine) - min(raw_spine):.2f}°"
                     f" → 慢层摆幅 {slow_span:.2f}°（这一层就是呼吸）")
        if tspan < 20.0 or lspan < 20.0:
            bad.append(f"walk 腿摆幅不足 R={tspan:.1f}° L={lspan:.1f}°（要 ≥20°）")
        if opp < tot * 0.6:
            bad.append(f"walk 左右腿没有反相（反相 {opp}/{tot} 帧）")
        if hz_br > 0 and abs(hz_leg / hz_br - 6.0) > 1.5:
            bad.append(f"walk 步频/呼吸比 {hz_leg / hz_br:.2f} 偏离 6:1")
        if slow_span < 3.0:
            bad.append(f"walk 的呼吸层太弱：慢层摆幅只有 {slow_span:.2f}°（要 ≥3°）")

    # ── ③ 三套 attack：腿骨必须**全 0**（D 项的验收点）──
    leg_names = ["Bone_Thigh_L", "Bone_Thigh_R", "Bone_LowerLeg_L",
                 "Bone_LowerLeg_R", "Bone_Foot_L", "Bone_Foot_R", "Bone_Hips"]
    upper_names = ["Bone_Spine", "Bone_Chest", "Bone_Neck", "Bone_Head",
                   "Bone_UpperArm_L", "Bone_UpperArm_R"]
    sig = {}
    for anim in ("attack_hoe", "attack_can", "attack_fert"):
        d, n, loop = load(anim)
        if not d:
            bad.append(f"{anim}: 回读后没有骨架")
            continue
        lmax = 0.0
        per_leg = {}
        for ln in leg_names:
            vals = d[ln]
            m = max(abs(v[a]) for v in vals for a in range(3))
            per_leg[ln] = round(m, 4)
            lmax = max(lmax, m)
        uamax = 0.0
        for un in upper_names:
            vals = d[un]
            m = max(abs(v[a]) for v in vals for a in range(3))
            uamax = max(uamax, m)
        results[anim] = dict(leg_max=lmax, legs=per_leg, upper_max=uamax, frames=n)
        facts.append(f"{anim}  腿骨最大角 = {lmax:.4f}°（{len(leg_names)} 根）  "
                     f"上半身最大角 = {uamax:.2f}°")
        facts.append(f"        逐根腿骨最大角: "
                     + "  ".join(f"{k.replace('Bone_', '')}={per_leg[k]:.3f}°"
                                 for k in leg_names))
        # ★ 判据 1：腿骨曲线必须全为 0 ★
        if lmax > 0.05:
            bad.append(f"★{anim} 的下半身在动：腿骨最大角 {lmax:.3f}° ≠ 0 ★")
        # ★ 判据 2：上半身必须真的在动 ★
        if uamax < 20.0:
            bad.append(f"{anim} 上半身动作太小：最大只有 {uamax:.1f}°")
        # 签名：用于证明三套彼此不同
        sig[anim] = [round(v[0], 2) for v in d["Bone_UpperArm_R"]]

    # ── ④ 三套 attack 彼此不同 ──
    diffs = {}
    keys = list(sig)
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            a, b = keys[i], keys[j]
            if len(sig[a]) != len(sig[b]):
                diffs[f"{a}|{b}"] = None
                continue
            diffs[f"{a}|{b}"] = round(max(abs(x - y) for x, y in zip(sig[a], sig[b])), 2)
    results["distinct"] = diffs
    facts.append("三套 attack 的差异（右上臂 X 逐帧最大差）: "
                 + "  ".join(f"{k}={v}°" for k, v in diffs.items()))
    for k, v in diffs.items():
        if v is None or v < 2.0:
            bad.append(f"★{k} 两套攻击几乎一样（最大差 {v}°）—— 疑似改名复用 ★")

    # ── ⑤ swap 也只作用上半身 ──
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

    # ── ⑥ hit 不循环、只给姿态（不动缩放）──
    d, n, loop = load("hit")
    if d:
        cspan = axis_span(d["Bone_Chest"], 0)[2]
        hspan = axis_span(d["Bone_Head"], 0)[2]
        results["hit"] = dict(chest_span=cspan, head_span=hspan, frames=n)
        facts.append(f"hit   胸腔X 摆幅 {cspan:.2f}°  头X 摆幅 {hspan:.2f}°  "
                     f"{n} 帧一次性（不循环）")

    return results, facts, bad


# ================================================================ 武器回读
def verify_tools():
    rows, bad = [], []
    for kind in ("hoe", "can", "fert"):
        path = os.path.join(TOOL_DIR, f"tool_{kind}.fbx")
        fresh()
        objs = import_fbx(path)
        meshes = [o for o in objs if o.type == 'MESH']
        gnames = [o.name for o in objs if o.type == 'EMPTY']
        if not meshes:
            bad.append(f"tool_{kind}: 回读后没有 MESH")
            continue
        pts, tris, mats = [], 0, set()
        for o in meshes:
            pts += [o.matrix_world @ v.co for v in o.data.vertices]
            tris += kit.tri_count(o)
            mats |= {m.name for m in o.data.materials if m}
        xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
        dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
        # GRIP 节点必须在，且位于原点
        grip_ok = False
        gpos = None
        for o in objs:
            if "GRIP" in o.name.upper():
                grip_ok = True
                gpos = tuple(round(v, 4) for v in o.matrix_world.translation)
        if not grip_ok:
            bad.append(f"tool_{kind}: 回读后找不到 GRIP 标记")
        rows.append(dict(kind=kind, dims=tuple(round(v, 3) for v in dims), tris=tris,
                         mats=sorted(mats), grip=grip_ok, grip_pos=gpos,
                         nodes=gnames))
        # 尺寸核对
        span = max(dims)
        want = {"hoe": (1.10, 1.30), "can": (0.24, 0.42), "fert": (0.36, 0.56)}[kind]
        if not (want[0] <= span <= want[1]):
            bad.append(f"tool_{kind}: 最大尺寸 {span:.3f} 不在 {want}")
    return rows, bad


# ================================================================ 预览渲染
def _gather(objs):
    pts = []
    for o in objs:
        if o.type != 'MESH':
            continue
        mw = o.matrix_world
        pts += [mw @ v.co for v in o.data.vertices]
    xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
    center = ((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5,
              (min(zs) + max(zs)) * 0.5)
    radius = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)) * 0.5
    return center, max(0.05, radius)


def render_previews():
    """出交付对照图。**60° 俯视是主验收角度**（游戏相机就是这个角）。"""
    out = []
    # ── 玩家：60° 俯视 + 侧视 + 正面 ──
    fresh()
    objs = import_fbx(os.path.join(CHAR_DIR, "player.fbx"))
    center, radius = _gather(objs)
    shots = []
    for tag, ang, elev in (("top60", 0.0, 60.0), ("side", 90.0, 6.0),
                           ("front", 0.0, 6.0), ("top90", 0.0, 88.0)):
        p = os.path.join(PREVIEW_DIR, f"player_v2_{tag}.png")
        kit.ensure_dir(p)
        try:
            kit.render_preview(p, center, radius, size=(520, 520), angle=ang, elev=elev)
            shots.append((tag, p))
        except Exception as e:                     # noqa: BLE001
            print(f"[VERIFY] 渲染失败 {tag}: {e}")
    out += shots

    # ── 旧模型同角度，做对照 ──
    old_path = os.path.join(CHAR_DIR, "player.fbx")
    old_shot = None

    # ── 三把武器 ──
    tool_shots = []
    for kind in ("hoe", "can", "fert"):
        fresh()
        objs = import_fbx(os.path.join(TOOL_DIR, f"tool_{kind}.fbx"))
        c, r = _gather(objs)
        p = os.path.join(PREVIEW_DIR, f"tool_{kind}.png")
        kit.ensure_dir(p)
        try:
            kit.render_preview(p, c, r, size=(460, 460), angle=32.0, elev=18.0)
            tool_shots.append((kind, p))
        except Exception as e:                     # noqa: BLE001
            print(f"[VERIFY] 渲染失败 {kind}: {e}")
    out += tool_shots

    # ── 合成交付图 Docs/captures/player_v2_preview.png ──
    _compose(shots, tool_shots, CAPTURE)
    return out


def _compose(player_shots, tool_shots, out_path):
    """把几张渲染拼成一张带标签的对照图（交付用）。"""
    try:
        from PIL import Image, ImageDraw
    except Exception as e:                          # noqa: BLE001
        print(f"[VERIFY] ★没有 PIL，跳过合成：{e}★")
        return None
    kit.ensure_dir(out_path)
    cell_w, cell_h = 520, 560
    cols = max(len(player_shots), len(tool_shots))
    cols = max(3, cols)
    rows = 2
    sheet = Image.new("RGB", (cols * cell_w, rows * cell_h), (246, 243, 233))
    dr = ImageDraw.Draw(sheet)

    def put(px, py, path, label):
        try:
            im = Image.open(path).convert("RGB")
        except Exception:                           # noqa: BLE001
            return
        im.thumbnail((cell_w - 16, cell_h - 60))
        sheet.paste(im, (px + (cell_w - im.width) // 2,
                         py + 12 + (cell_h - 60 - im.height) // 2))
        dr.text((px + 14, py + cell_h - 40), label, fill=(40, 34, 26))

    for i, (tag, p) in enumerate(player_shots):
        put(i * cell_w, 0, p, f"player v2 / {tag}")
    for i, (kind, p) in enumerate(tool_shots):
        put(i * cell_w, cell_h, p, f"tool_{kind}")
    sheet.save(out_path)
    print(f"[VERIFY] 交付对照图 -> {out_path}")
    return out_path


# ================================================================ 主流程
def verify():
    print("=" * 78)
    print("回读验证：静态模型（尺寸 / 轴向 / 材质 / 骨骼名）")
    print("=" * 78)
    st = verify_static()
    print(f"  player.fbx  网格件 {st.get('parts')} 个  "
          f"dims(x,y,z)={st['dims']}  zmin={st['zmin']} zmax={st['zmax']}")
    print(f"  三角面 {st['tris']}（预算 {gp.TRI_BUDGET}）")
    print(f"  材质 {', '.join(st['mats'])}")
    print(f"  骨骼 {len(st['bones'])} 根: {', '.join(st['bones'])}")
    if st.get("extra_bones"):
        print(f"  ★多出来的骨骼: {st['extra_bones']}★")
    for p in st["problems"]:
        print(f"  ★{p}★")
    if not st["problems"]:
        print("  ✔ 静态全部通过")

    print("=" * 78)
    print("回读验证：三把武器（GRIP 标记 / 尺寸 / 材质）")
    print("=" * 78)
    rows, tool_bad = verify_tools()
    for r in rows:
        print(f"  tool_{r['kind']:<5} dims={r['dims']} tris={r['tris']} "
              f"GRIP={'有' if r['grip'] else '★无★'} @{r['grip_pos']}")
        print(f"        材质: {', '.join(r['mats'])}")
    for p in tool_bad:
        print(f"  ★{p}★")
    if not tool_bad:
        print("  ✔ 三把武器全部通过")

    print("=" * 78)
    print("逐帧骨骼角度（决定性判据）")
    print("=" * 78)
    res, facts, bad = verify_anims()
    for f in facts:
        print("  " + f)

    print("=" * 78)
    allbad = list(st["problems"]) + tool_bad + bad
    if allbad:
        print(f"★验证失败 {len(allbad)} 条：")
        for p in allbad:
            print(f"   · {p}")
    else:
        print("★全部通过：静态/武器/7 条动画的逐帧骨骼角度都符合验收条件★")
    print("=" * 78)

    # 落一份 JSON，便于外部核对
    dump = os.path.join(PREVIEW_DIR, "player_v2_verify.json")
    kit.ensure_dir(dump)
    with open(dump, "w", encoding="utf-8") as fh:
        json.dump(dict(static=st, tools=rows, anims=res,
                       failures=allbad, facts=facts), fh,
                  ensure_ascii=False, indent=2, default=str)
    print(f"[VERIFY] 明细 -> {dump}")
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
        print("用法: ... verify_player_v2.py -- [verify|preview|all]")
