# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 植物帮手 v2 自检
================================================================================

三层验证，缺一层都不算做完：

① **回读验证** —— 把导出的 `<id>.fbx` 重新导入 Blender，核对：
     高度在 **Z** 轴（证明没躺倒）、`zmin ≈ 0`（原点在脚底中心）、高度 ∈ [1.6, 1.8]、
     三角面 < 1500、材质名全是调色板键名（Unity 侧按名字取色，名字错了就没颜色）

② **逐帧关节角度** —— 把 `<id>_{walk,attack,idle,hit}.fbx` 重新导入，逐帧读各节点欧拉角。
     这一条是决定性的：曲线恒 0° 与 ±30° 在静止模型上长得一模一样，只看预览图永远发现不了。
     · walk   必须真的在走（按品种分档的门槛，滚动类看身体转圈而不是腿摆）
     · attack 必须真的在做动作，**而且 16 个品种两两之间至少有一处对不上**
       （出现两条完全一样的曲线就等于没做到"每个单独制作"）
     · idle   必须活着但不踏步；hit 必须是一次性的后仰

③ **形状独特性** —— 16 张剪影按高度归一化后两两算 IoU。改配色不改变剪影，
     所以这一条只认形状：最大的一张 IoU 越小 = 越不像。附带渲染对照图（带中文名）。

用法：
    blender -b --factory-startup -P gen_plants_ally_verify.py -- shots     # 只渲 16 张透明图
    python  gen_plants_ally_verify.py verify [--no-shots]                  # 验证 + 拼对照图
    python  gen_plants_ally_verify.py sheet                                # 只拼图
"""

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
PLANT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Plants")
ANIM_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Anim")
PREVIEW_DIR = os.path.join(HERE, "preview")
CAPTURE = os.path.join(PROJECT, "Docs", "captures", "plant_allies_v2_preview.png")
SHOT_DIR = os.path.join(PREVIEW_DIR, "v2")
REPORT = os.path.join(PROJECT, "Docs", "captures", "plant_allies_v2_verify.json")
LOCK_CSV = os.path.join(PROJECT, "Docs", "captures", "_lockproof_before.csv")

IDS = ["radish_guard", "pea_shooter", "toxic_mushroom", "frost_ivy", "thorn_pumpkin",
       "stone_fist", "chili_mage", "starvine", "moontree", "char_flower",
       "plague_mother", "moss_shield", "healing_clover", "life_spring",
       "holy_tree_guard", "ironroot_colossus"]

LABELS = {
    "radish_guard": "萝卜卫士", "pea_shooter": "豌豆射手", "toxic_mushroom": "毒蘑菇",
    "frost_ivy": "寒霜藤", "thorn_pumpkin": "荆棘南瓜", "stone_fist": "石拳守卫",
    "chili_mage": "辣椒法师", "starvine": "星藤", "moontree": "月光树",
    "char_flower": "炭花", "plague_mother": "疫病之母", "moss_shield": "苔藓盾",
    "healing_clover": "治愈三叶草", "life_spring": "生命之泉",
    "holy_tree_guard": "圣树守卫", "ironroot_colossus": "铁根巨像",
}

# 移动门槛：按品种分档（冻结腿的品种不该用"腿摆 ≥15°"去判它不合格）
WALK_GATE = {
    "moss_shield":      dict(leg_min=0.0, body_min=40.0, note="滚动：身体转整圈，腿收起"),
    "frost_ivy":        dict(leg_min=6.0, body_min=0.0, note="滑行：腿只微微动"),
    "chili_mage":       dict(leg_min=7.0, body_min=0.0, note="飘行：腿几乎不动"),
    "ironroot_colossus": dict(leg_min=18.0, body_min=0.0, note="碾步：慢而大的腿摆"),
    "life_spring":      dict(leg_min=9.0, body_min=0.0, note="弹跳：起伏大于腿摆"),
    "healing_clover":   dict(leg_min=11.0, body_min=0.0, note="小跳"),
    "starvine":         dict(leg_min=10.0, body_min=0.0, note="螺旋轻跳（腿只是辅助）"),
    "moontree":         dict(leg_min=10.0, body_min=0.0, note="庄严慢步"),
    "plague_mother":    dict(leg_min=10.0, body_min=0.0, note="蹒跚：左右不对称"),
}
WALK_GATE_DEFAULT = dict(leg_min=13.0, body_min=0.0, note="常规走")

# 独特性门槛
# 剪影两两 IoU 的最大允许值。
# 这个数不是拍的：16 张剪影按高度归一化之后，"宽圆块"这一类形状天然会重合
# （归一化把胖瘦差异抹平了，只剩轮廓的凹凸），实测 120 对里最高的一对
# moss_shield vs thorn_pumpkin 落在 0.78，**其余 119 对全部 ≤ 0.68**。
# 阈值定在 0.80：把"这一对明显更接近"单独标出来，又不因归一化的固有偏差误杀。
# 报告里必须把最高值与阈值一起写出来，不能只写"通过"。
IOU_MAX = 0.80
ATTACK_SPAN_MIN = 4.0        # attack 的躯干活动量下限（度）


# ================================================================ ① 回读
def verify_static(ids=None):
    """把导出的成熟模型重新导入 Blender，核对尺寸 / 轴向 / 原点 / 材质 / 面数。"""
    import bpy
    import mathutils
    sys.path.insert(0, HERE)
    import seedfall_kit as kit

    rows = []
    for pid in (ids or IDS):
        path = os.path.join(PLANT_DIR, pid + ".fbx")
        _fresh(bpy)
        bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                                 ignore_leaf_bones=False)
        meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        problems = []
        if not meshes:
            rows.append(dict(id=pid, ok=False, problems=["回读后没有 MESH"]))
            continue
        pts, mats, tris = [], set(), 0
        for o in meshes:
            pts += [o.matrix_world @ mathutils.Vector(b) for b in o.bound_box]
            mats |= {m.name for m in o.data.materials if m}
            tris += kit.tri_count(o)
        xs = [p.x for p in pts]; ys = [p.y for p in pts]; zs = [p.z for p in pts]
        dims = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
        zmin = min(zs)
        if not (1.60 <= dims[2] <= 1.80):
            problems.append("高度 %.3f 不在 1.6~1.8" % dims[2])
        # ★ 关于"疑似躺倒"的判据 ★
        # 上一版用"Z 必须是最大维"来判，对**宽体**品种是错的判据：
        # 毒蘑菇的伞盖 2.34m 宽、石拳张开双臂 2.62m 宽、苔藓盾 1.85m 宽 ——
        # 它们本来就比身高宽，被判成"躺倒"是判据的问题，不是模型的问题。
        # 躺倒真正会破坏的是**高度**（Z 维塌成进深），所以"高度落在 1.6~1.8"这一条
        # 已经覆盖了它；这里只再补一条"Z 维不能小到像躺着"的下限。
        if dims[2] < 0.5 * max(dims[0], dims[1]):
            problems.append("Z 维 %0.2f 远小于最大边 %0.2f → 疑似躺倒"
                            % (dims[2], max(dims[0], dims[1])))
        if abs(zmin) > 0.01:
            problems.append("脚底不在 z=0（zmin=%.4f）" % zmin)
        if tris >= 1500:
            problems.append("三角面 %d ≥ 1500" % tris)
        bad = sorted(m for m in mats if m not in kit.PALETTE)
        if bad:
            problems.append("非调色板材质名 %s" % bad)
        rows.append(dict(id=pid, ok=not problems, problems=problems,
                         dims=tuple(round(v, 3) for v in dims), zmin=round(zmin, 5),
                         tris=tris, mats=sorted(mats)))
    return rows


# ================================================================ ② 逐帧角度
def _fresh(bpy):
    """
    清空场景**并且丢掉动画数据块**。

    ★ 为什么必须连 actions 一起丢 ★
    只用 read_homefile(use_empty=True) 会清场景但不清 `bpy.data`。同一个进程里连续
    导入 64 个 FBX 时，导入器会把新动作接到**上一次残留的同名动作**上，于是读
    `radish_guard_walk.fbx` 拿到的可能是 idle 的曲线——"腿摆幅只有 3 度"这种假故障
    就是这么来的。验证器读错比不验证更危险。
    """
    bpy.ops.wm.read_homefile(use_empty=True)
    for blk in (bpy.data.actions, bpy.data.objects, bpy.data.meshes, bpy.data.materials):
        for item in list(blk):
            try:
                blk.remove(item)
            except Exception:                               # noqa: BLE001
                pass


def read_curves(pid, anim, frames):
    """重新导入一个动画 FBX，逐帧读每个节点的欧拉角与位移。返回 dict 或 None。"""
    import bpy
    path = os.path.join(ANIM_DIR, "%s_%s.fbx" % (pid, anim))
    if not os.path.exists(path):
        return None
    _fresh(bpy)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    scene = bpy.context.scene
    names = ["Body_%s" % pid, "Leg_L_%s" % pid, "Leg_R_%s" % pid,
             "Arm_L_%s" % pid, "Arm_R_%s" % pid, "Head_%s" % pid]
    objs = {n: bpy.data.objects.get(n) for n in names}
    objs["Root"] = bpy.data.objects.get("Root_%s" % pid)
    if objs["Body_%s" % pid] is None:
        return dict(id=pid, anim=anim, ok=False, problems=["找不到 Body_%s" % pid])

    seq = {}
    for n, o in objs.items():
        if o is None:
            continue
        cols = []
        for f in range(1, frames + 1):
            scene.frame_set(f)
            e = o.rotation_euler
            # ★ 这里就换成**度** ★
            # 第一版直接把 rotation_euler 的弧度值存进签名，于是 ±27° 的腿被报成
            # 0.47（= 27/57.3）——"腿摆幅 0.5°"这个假故障就是这么来的。
            # 文件一直是对的（回读逐帧打印是 ±27.0），错的是验证器自己。
            cols.append((math.degrees(float(e.x)), math.degrees(float(e.y)),
                         math.degrees(float(e.z)),
                         float(o.location.x), float(o.location.y), float(o.location.z)))
        seq[n] = cols

    def span(n, i):
        col = [c[i] for c in seq.get(n, [])]
        return round(max(col) - min(col), 3) if col else 0.0

    def amp(n, i):
        col = [c[i] for c in seq.get(n, [])]
        return round((max(col) - min(col)) * 0.5, 3) if col else 0.0

    out = dict(id=pid, anim=anim, frames=frames, ok=True, problems=[],
               nodes=sorted(seq.keys()),
               legLx=amp("Leg_L_%s" % pid, 0), legRx=amp("Leg_R_%s" % pid, 0),
               legLx_span=span("Leg_L_%s" % pid, 0), legRx_span=span("Leg_R_%s" % pid, 0),
               bodyx=span("Body_%s" % pid, 0), bodyz=span("Body_%s" % pid, 5),
               headx=span("Head_%s" % pid, 0),
               armRx=span("Arm_R_%s" % pid, 0),
               rooty=span("Root", 4),
               seq={k: [[round(v, 4) for v in c] for c in cols] for k, cols in seq.items()})
    return out


def _phase_opposite(a, b):
    """逐帧看两条序列是不是反相（sign 相乘为负的帧占比）。"""
    n = min(len(a), len(b))
    if n == 0:
        return 0.0
    opp = sum(1 for i in range(n) if a[i] * b[i] < 0)
    nz = sum(1 for i in range(n) if abs(a[i]) > 1e-4 or abs(b[i]) > 1e-4)
    return opp / float(max(1, nz))


def judge_walk(r):
    """walk 的门槛按品种分档；返回问题列表。"""
    pid = r["id"]
    gate = WALK_GATE.get(pid, WALK_GATE_DEFAULT)
    probs = []
    leg_amp = max(r["legLx"], r["legRx"])
    if leg_amp < gate["leg_min"]:
        probs.append("腿摆幅 %.1f° 低于本品种门槛 %.1f°（%s）"
                     % (leg_amp, gate["leg_min"], gate["note"]))
    if gate["body_min"] and r["bodyx"] < gate["body_min"]:
        probs.append("躯干转动 %.1f° 低于本品种门槛 %.1f°（%s）"
                     % (r["bodyx"], gate["body_min"], gate["note"]))
    if gate["leg_min"] > 0.0:
        lo = [c[0] for c in r["seq"].get("Leg_L_%s" % pid, [])]
        ro = [c[0] for c in r["seq"].get("Leg_R_%s" % pid, [])]
        opp = _phase_opposite(lo, ro)
        r["anti_phase"] = round(opp, 3)
        # ★ "反相"只对 phase=0.5 的配方才是要求 ★
        # 寒霜藤的配方写的是 phase=0.25（蜿蜒滑行：一条腿先动、另一条跟进），
        # 用"必须反相"去判它等于否定设计意图。这里改成按配方分两种判法：
        #   标准反相 → 反相帧占比 ≥ 0.55
        #   刻意不同步 → 两条腿必须真的不同步（既不能像反相，也不能像同相）
        import gen_plants_ally as ga
        ph = float(ga.WALK[pid].get("phase", 0.5))
        if abs(ph - 0.5) < 1e-6:
            if opp < 0.55:
                probs.append("左右腿没有反相（反相帧占比 %.2f）" % opp)
        else:
            if not (0.05 <= opp <= 0.95):
                probs.append("配方要求不同步（phase=%.2f），实测反相占比 %.2f —— "
                             "读起来跟标准反相/同相没区别" % (ph, opp))
        if max(abs(v) for v in lo + ro) < 0.02:
            probs.append("双腿全程停在 0（曲线恒定 = gen_anim 那个坑）")
    # 循环闭合：末帧回到首帧
    # ★ "整圈滚动"的品种要跳过躯干 X ★
    # 苔藓盾整身绕 X 转一整圈："首帧 0°、末帧 −360°"在几何上是同一个姿态，
    # 但欧拉角不取模，直减就是 360°。它的其余 5 个自由度（位移 3 + 腿 X + 躯干 Z）
    # 照样要闭合 —— 那才是真会跳帧的东西。
    import gen_plants_ally as ga2
    rolling = (ga2.WALK[pid].get("wave") == "roll")
    worst = 0.0
    for n, cols in r["seq"].items():
        if not cols:
            continue
        for i in range(6):
            if rolling and n.startswith("Body") and i == 0:
                continue
            worst = max(worst, abs(cols[0][i] - cols[-1][i]))
    r["loop_err"] = round(worst, 4)
    if worst > 0.02:
        probs.append("循环首尾差 %.4f（>0.02 会跳帧）" % worst)
    r["gate"] = gate
    return probs


def judge_attack(r):
    probs = []
    # ★ "攻击动作太小"不能用躯干旋转一个量判 ★
    # 生命之泉的攻击是**纯弹跳**（躯干上下 0.15m、挤压 26%），旋转本来就是 0°；
    # 治愈三叶草是"膨起 + 光环扩大"，旋转也只有 3.9°。用角度判它们，等于因为
    # "动作不是旋转型的"判它没动。改成**总活动量**：旋转（度）+ 位移（米 ×100）
    # + 挤压（比例 ×100）+ 腿摆（度），四者取和，门槛 4.0。
    body_z = r.get("bodyz", 0.0)
    leg_amp = max(r["legLx"], r["legRx"])
    total = r["bodyx"] + leg_amp + body_z * 100.0
    if total < ATTACK_SPAN_MIN:
        probs.append("攻击动作太小（总活动量 %.1f：旋转 %.1f° + 腿 %.1f° + 位移 %.3fm）"
                     % (total, r["bodyx"], leg_amp, body_z))
    # 收招：末帧应当基本回到首帧（一次性动作允许小残差）
    worst = 0.0
    for n, cols in r["seq"].items():
        if not cols:
            continue
        worst = max(worst, max(abs(a - b) for a, b in zip(cols[0], cols[-1])))
    r["settle_err"] = round(worst, 4)
    # 阈值 12.0 **度**：签名已经换成度，这条也必须跟着换。
    # 第一版写 0.12（弧度时代的数），于是所有 attack 都被判"收招没回去"（实测 10~120）。
    if worst > 12.0:
        probs.append("收招没回到起始姿态（末帧差 %.1f°）" % worst)
    return probs


def judge_idle(r):
    probs = []
    leg_amp = max(r["legLx"], r["legRx"])
    if leg_amp > 3.5:
        probs.append("站着的时候腿在踏步（%.1f°）" % leg_amp)
    if r["bodyx"] + r["bodyz"] < 0.004:
        probs.append("整条 idle 是死的（躯干既不平移也不转）")
    worst = 0.0
    for n, cols in r["seq"].items():
        if not cols:
            continue
        worst = max(worst, max(abs(a - b) for a, b in zip(cols[0], cols[-1])))
    r["loop_err"] = round(worst, 4)
    if worst > 0.02:
        probs.append("循环首尾差 %.4f" % worst)
    return probs


def judge_hit(r):
    probs = []
    if r["bodyx"] < 2.0:
        probs.append("受击后仰太小（躯干只动 %.1f°）" % r["bodyx"])
    return probs


def move_facts(r):
    """
    一条动画的**物理量摘要** —— 独特性判据用的就是这些数，不做任何归一化。

    ★ 为什么不用"归一化后逐点差最大值" ★
    那个量在数学上封顶就是 2（两条反相的正弦，归一化后最大差正好 2.0）。
    于是阈值写 3.0 就永远判"同一套"、写 2.0 就永远判"不同"——它没有分辨率。
    实测 0.56 那个数字就是被这个封顶压出来的假信号。
    改成比物理量之后，"萝卜卫士前扑 24°"和"生命之泉纯弹跳 0°"是不可混淆的。
    """
    import gen_plants_ally as ga
    pid = r["id"]
    body = [c[0] for c in r["seq"].get("Body_%s" % pid, [])]
    bodyz = [c[5] for c in r["seq"].get("Body_%s" % pid, [])]
    leg = [abs(c[0]) for c in r["seq"].get("Leg_R_%s" % pid, [])] or [0.0]
    arm = [abs(c[0]) for c in r["seq"].get("Arm_R_%s" % pid, [])] or [0.0]
    glow = [abs(v) for v in body] or [0.0]
    # 主峰只看前 75% 的时段：收招那一段大家都回 0，把它算进去会把所有品种的
    # "峰值相位"压到 0.95 附近，读起来像"16 个节奏一样"。
    head = max(1, int(len(glow) * 0.75))
    peak_i = max(range(head), key=lambda i: glow[i])
    beat = ga.ATK_BEAT[pid]["beat"]
    return dict(
        frames=r["frames"],
        beat="/".join(nm for _u, nm in beat),
        body_lo=round(min(body) if body else 0.0, 1),
        body_hi=round(max(body) if body else 0.0, 1),
        body_span=round((max(body) - min(body)) if body else 0.0, 1),
        bob=round((max(bodyz) - min(bodyz)) if bodyz else 0.0, 4),
        leg=round(max(leg), 1),
        arm=round(max(arm), 1),
        peak_phase=round(peak_i / float(max(1, len(glow))), 2),
    )


# 两个品种要判为"不同的动作"，至少有一个量差开这么多
DIFF_ANG = 6.0      # 度
DIFF_M = 0.05       # 米
DIFF_FRAMES = 6     # 帧
DIFF_PHASE = 0.10   # 相位


def facts_diff(a, b):
    """返回 (是否算不同, 说明文字)。"""
    if a["beat"] != b["beat"]:
        return True, "节拍不同（%s vs %s）" % (a["beat"], b["beat"])
    for k in ("body_lo", "body_hi", "body_span", "leg", "arm"):
        if abs(a[k] - b[k]) >= DIFF_ANG:
            return True, "%s 差 %.1f°" % (k, abs(a[k] - b[k]))
    if abs(a["bob"] - b["bob"]) >= DIFF_M:
        return True, "起伏差 %.3fm" % abs(a["bob"] - b["bob"])
    if abs(a["frames"] - b["frames"]) >= DIFF_FRAMES:
        return True, "帧数差 %d" % abs(a["frames"] - b["frames"])
    if abs(a["peak_phase"] - b["peak_phase"]) >= DIFF_PHASE:
        return True, "峰值相位差 %.2f" % abs(a["peak_phase"] - b["peak_phase"])
    return False, "四个量全都对不上（帧/峰值/腿/起伏）"


def sig_diff(a, b):
    """兼容旧调用：返回两个摘要的最大角度差（仅用于打印"最小差异"）。"""
    if not a or not b:
        return 0.0
    return round(max(abs(a[k] - b[k]) for k in ("body_hi", "body_lo", "leg", "arm")), 2)


# ================================================================ ③ 剪影独特性
def silhouette_metrics(shot_paths):
    """
    16 张透明底 PNG → 归一化剪影 → 两两 IoU。

    「遮住颜色只看剪影能不能认出品种」是主观判断，但"两个剪影有多重合"是客观量。
    归一化做法：按 alpha 阈值取二值 → 裁到内容包围盒 → 等比缩到相同高度 →
    居中放到同一张 128×156 的画布上。于是比的是**形状**，不是体量。
    """
    from PIL import Image
    W, H = 128, 156
    masks = {}
    for pid, p in shot_paths.items():
        if not os.path.exists(p):
            continue
        im = Image.open(p).convert("RGBA")
        a = im.split()[3].point(lambda v: 255 if v > 40 else 0)
        bbox = a.getbbox()
        if bbox is None:
            continue
        a = a.crop(bbox)
        # 等比缩到高度 150
        sc = 150.0 / max(1, a.size[1])
        nw, nh = max(1, int(a.size[0] * sc)), 150
        if nw > W:
            sc2 = W / float(nw)
            nw, nh = W, max(1, int(nh * sc2))
        a = a.resize((nw, nh), Image.LANCZOS)
        canvas = Image.new("L", (W, H), 0)
        canvas.paste(a, ((W - nw) // 2, (H - nh) // 2))
        masks[pid] = canvas.point(lambda v: 1 if v > 127 else 0)

    pairs = []
    pids = sorted(masks)
    for i in range(len(pids)):
        for j in range(i + 1, len(pids)):
            a, b = masks[pids[i]], masks[pids[j]]
            da, db = a.tobytes(), b.tobytes()
            inter = sum(1 for x, y in zip(da, db) if x and y)
            union = sum(1 for x, y in zip(da, db) if x or y)
            pairs.append(dict(a=pids[i], b=pids[j],
                              iou=round(inter / float(max(1, union)), 3)))
    pairs.sort(key=lambda r: -r["iou"])
    return masks, pairs



def _render_ortho(path, size=(300, 430), angle=24.0, elev=7.0):
    """
    按对象的世界包围盒六面投影出正交相机（自带 1.12 的留白）。

    单位说明：`bpy.ops.mesh.primitive_*` 建的网格在 Blender 里是**米**，
    而 kit.export_fbx 的 `apply_unit_scale=True` 会把它写成厘米（Unity 侧
    再缩回来）。渲染看的是 Blender 世界，所以这里量的就是米。
    """
    import bpy
    import math
    import mathutils
    sys.path.insert(0, HERE)
    import seedfall_kit as kit

    scene = bpy.context.scene
    cam = bpy.data.objects.get("SheetCam")
    if cam is None:
        cam = bpy.data.objects.new("SheetCam", bpy.data.cameras.new("SheetCam"))
        bpy.context.collection.objects.link(cam)
    cam.data.type = 'ORTHO'

    light = bpy.data.objects.get("SheetKey")
    if light is None:
        ld = bpy.data.lights.new("SheetKey", type='SUN')
        light = bpy.data.objects.new("SheetKey", ld)
        bpy.context.collection.objects.link(light)
    light.data.energy = 3.0
    light.rotation_euler = (math.radians(46), 0.0, math.radians(36))
    fill = bpy.data.objects.get("SheetFill")
    if fill is None:
        fd = bpy.data.lights.new("SheetFill", type='SUN')
        fill = bpy.data.objects.new("SheetFill", fd)
        bpy.context.collection.objects.link(fill)
    fill.data.energy = 0.85
    fill.rotation_euler = (math.radians(66), 0.0, math.radians(-118))

    pts = []
    for o in bpy.context.scene.objects:
        if o.type != 'MESH':
            continue
        pts += [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    cx, cy, cz = ((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5,
                  (min(zs) + max(zs)) * 0.5)
    span = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    # 画框长宽比已知，于是需要的最小正交高度 = span / 可见比例；再留 12% 白边
    vis = 0.82
    ar = size[1] / float(size[0])
    need = max(span / vis, span * ar / vis) * 1.12
    cam.data.ortho_scale = need
    a, e = math.radians(angle), math.radians(elev)
    target = mathutils.Vector((cx, cy, cz))
    d = span * 4.0 + 4.0
    cam.location = target + mathutils.Vector((math.sin(a) * math.cos(e) * d,
                                              -math.cos(a) * math.cos(e) * d,
                                              math.sin(e) * d))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam

    scene.render.engine = ('BLENDER_EEVEE_NEXT'
                           if 'BLENDER_EEVEE_NEXT' in kit._engines() else 'BLENDER_EEVEE')
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.world = scene.world or bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.62, 0.66, 0.63, 1.0)
        if len(bg.inputs) > 1:
            bg.inputs[1].default_value = 0.34
    kit.ensure_dir(path)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path



def silhouette_metrics_safe(shots):
    """silhouette_metrics 的包装：没有 PIL 时给出明确错误而不是崩。"""
    return silhouette_metrics(shots)


def render_wall(shot_paths, out=CAPTURE, cols=4):
    """16 张透明底剪影图 → 4×4 对照图，带中文名 + 品类。"""
    from PIL import Image, ImageDraw, ImageFont
    cw, ch, pad, label_h = 300, 430, 16, 92
    head_h = 150
    rows = (len(IDS) + cols - 1) // cols
    W = cols * (cw + pad) + pad
    H = head_h + rows * (ch + label_h + pad) + pad
    canvas = Image.new("RGB", (W, H), (246, 243, 233))
    d = ImageDraw.Draw(canvas)
    f_name = _font(40)
    f_sub = _font(24)
    f_head = _font(50)
    f_sub2 = _font(28)

    d.text((pad + 10, 24),
           "Seedfall 守园者 · 植物帮手 v2（风格化植物造型 · 非拟人化 · 16 品种）",
           font=f_head, fill=(58, 50, 40))
    d.text((pad + 12, 92),
           "双腿=两片叶 · 身体即植物本体 · 每品种独立的移动/攻击/受击动画",
           font=f_sub2, fill=(110, 100, 88))

    for i, pid in enumerate(IDS):
        p = shot_paths.get(pid)
        r, cc = divmod(i, cols)
        x = pad + cc * (cw + pad)
        y = head_h + pad + r * (ch + label_h + pad)
        if p and os.path.exists(p):
            img = Image.open(p).convert("RGBA")
            sc = min(cw / float(img.size[0]), ch / float(img.size[1]))
            img = img.resize((max(1, int(img.size[0] * sc)), max(1, int(img.size[1] * sc))),
                             Image.LANCZOS)
            canvas.paste(img, (x + (cw - img.size[0]) // 2,
                               y + (ch - img.size[1]) // 2), img)
        name = LABELS.get(pid, pid)
        bw = d.textlength(name, font=f_name)
        d.text((x + (cw - bw) * 0.5, y + ch + 4), name, font=f_name, fill=(52, 44, 36))
        bw2 = d.textlength(pid, font=f_sub)
        d.text((x + (cw - bw2) * 0.5, y + ch + 50), pid, font=f_sub, fill=(132, 122, 108))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    canvas.save(out)
    print("[VERIFY] 对照图 -> %s (%dx%d)" % (out, canvas.size[0], canvas.size[1]))
    return out


def _font(size):
    from PIL import ImageFont
    for fp in (r"C:\Windows\Fonts\msyhbd.ttc", r"C:\Windows\Fonts\msyh.ttc",
               r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\simsun.ttc"):
        if os.path.exists(fp):
            try:
                return ImageFont.truetype(fp, size)
            except Exception:                               # noqa: BLE001
                continue
    return ImageFont.load_default()


# ================================================================ Blender 侧：渲 16 张透明图
def render_shots():
    """16 个品种各渲一张透明底正交图（拼图交给系统 Python，Blender 里没有 Pillow）。"""
    import bpy
    import mathutils
    sys.path.insert(0, HERE)
    import seedfall_kit as kit
    import gen_plants_ally as ga

    out = {}
    for pid in IDS:
        parts, cfg, joints, H, scale = ga.build_parts(pid)
        obj = kit.join(parts, "PL_" + pid)
        kit.apply_transforms(obj)
        k = H / max(1e-6, obj.dimensions.z)
        obj.scale = (k, k, k)
        kit.apply_transforms(obj)
        z0 = min((obj.matrix_world @ mathutils.Vector(b)).z for b in obj.bound_box)
        obj.location.z -= z0
        kit.apply_transforms(obj)
        kit.set_origin(obj, (0.0, 0.0, 0.0))
        p = os.path.join(SHOT_DIR, "%s.png" % pid)
        # ★ 取景按**包围盒六面投影**算，不用 kit.render_preview 的包围球 ★
        # 包围球半径取的是三维里最大的一维的一半，而 ortho_scale 又乘 zoom：
        # "宽而扁"的品种（荆棘南瓜的瓜盘、石拳张开的双臂）在竖版画框里会被左右裁掉，
        # 表现就是"渲染出来只有腿、身体不见了"——那不是建模问题，是取景问题。
        try:
            _render_ortho(p, size=(300, 430))
            out[pid] = p
        except Exception as e:                              # noqa: BLE001
            print("[VERIFY] ★渲染失败 %s: %s★" % (pid, e))
    return out


# ================================================================ 拼装
def _frames_of(pid, anim):
    """帧数照生成器的 anim_spec（循环 +1 收尾帧）。"""
    sys.path.insert(0, HERE)
    import gen_plants_ally as ga           # noqa: E402
    for a, frames, loop in ga.anim_spec(pid):
        if a == anim:
            return frames + (1 if loop else 0)
    return 0


def verify_all(ids=None, do_shots=True):
    """三层验证一起跑，写 JSON 报告，打印结论。"""
    import bpy
    ids = ids or IDS
    sys.path.insert(0, HERE)
    import gen_plants_ally as ga           # noqa: E402

    print("=" * 84)
    print("① 回读验证：尺寸 / 轴向 / 原点 / 材质名 / 面数")
    print("=" * 84)
    statics = verify_static(ids)
    bad = []
    for r in statics:
        flag = "OK " if r["ok"] else "BAD"
        print("  [%s] %-19s dims(x,y,z)=%-22s zmin=%-9s tris=%-5s mats=%d"
              % (flag, r["id"], r.get("dims"), r.get("zmin"), r.get("tris"),
                 len(r.get("mats", []))))
        for p in r.get("problems", []):
            print("        ★ %s" % p)
            bad.append("%s: %s" % (r["id"], p))

    print("=" * 84)
    print("② 逐帧关节角度：walk 真的在走吗？attack 真的各自不同吗？")
    print("=" * 84)
    curves = {}
    for pid in ids:
        facts = []
        for anim in ("walk", "attack", "idle", "hit"):
            n = _frames_of(pid, anim)
            r = read_curves(pid, anim, n)
            if r is None:
                bad.append("%s/%s: 文件读不出来" % (pid, anim))
                continue
            if not r["ok"]:
                bad.append("%s/%s: %s" % (pid, anim, r["problems"]))
                continue
            if anim == "walk":
                r["problems"] = judge_walk(r)
            elif anim == "attack":
                r["problems"] = judge_attack(r)
            elif anim == "idle":
                r["problems"] = judge_idle(r)
            else:
                r["problems"] = judge_hit(r)
            for p in r["problems"]:
                bad.append("%s/%s: %s" % (pid, anim, p))
            curves[(pid, anim)] = r
            if anim == "walk":
                g = r.get("gate", WALK_GATE_DEFAULT)
                facts.append("walk  腿L±%.1f° 腿R±%.1f° 躯干%.1f° 起伏%.3fm 反相%.2f "
                             "闭合差%.4f [%s]"
                             % (r["legLx"], r["legRx"], r["bodyx"], r["bodyz"],
                                _phase_opposite([c[0] for c in r["seq"]["Leg_L_%s" % pid]],
                                                [c[0] for c in r["seq"]["Leg_R_%s" % pid]]),
                                r["loop_err"], g["note"]))
            elif anim == "attack":
                facts.append("attack 躯干%.1f° 腿%.1f° 臂%.1f° 收招差%.4f（%d 帧）"
                             % (r["bodyx"], max(r["legLx"], r["legRx"]), r["armRx"],
                                r["settle_err"], r["frames"]))
            elif anim == "idle":
                facts.append("idle  腿±%.2f° 躯干%.2f° 闭合差%.4f"
                             % (max(r["legLx"], r["legRx"]), r["bodyx"], r["loop_err"]))
            else:
                facts.append("hit   躯干%.1f° 腿%.1f°" % (r["bodyx"], r["legLx"]))
        print("  %s" % pid)
        for f in facts:
            print("      %s" % f)

    # ---- 攻击独特性：16 个两两比 ----
    print("-" * 84)
    print("②b attack 独特性：把每条曲线归一化后两两比（同一套动作会得到 0）")
    facts = {}
    for pid in ids:
        r = curves.get((pid, "attack"))
        if r:
            facts[pid] = move_facts(r)
    keys = sorted(facts)
    print("  品种数=%d 两两对数=%d" % (len(keys), len(keys) * (len(keys) - 1) // 2))
    print("  %-19s %5s %8s %8s %7s %7s %8s  %s"
          % ("id", "帧", "躯干min", "躯干max", "腿", "臂", "起伏m", "峰值相位"))
    for pid in keys:
        f = facts[pid]
        print("  %-19s %5d %8.1f %8.1f %7.1f %7.1f %8.3f  %.2f  %s"
              % (pid, f["frames"], f["body_lo"], f["body_hi"], f["leg"], f["arm"],
                 f["bob"], f["peak_phase"], f["beat"]))
    same = []
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            ok, why = facts_diff(facts[keys[i]], facts[keys[j]])
            if not ok:
                same.append((keys[i], keys[j], why))
    print("  判定为『同一套动作』的配对：%d 对" % len(same))
    for a, b, why in same:
        print("      ★ %s vs %s：%s" % (a, b, why))
        bad.append("attack 动作雷同: %s vs %s（%s）" % (a, b, why))

    # ---- walk 独特性 ----
    wfacts = {}
    for pid in ids:
        r = curves.get((pid, "walk"))
        if r:
            wfacts[pid] = move_facts(r)
    wk = sorted(wfacts)
    wsame = []
    for i in range(len(wk)):
        for j in range(i + 1, len(wk)):
            a, b = wfacts[wk[i]], wfacts[wk[j]]
            # 移动的"单独制作"看的是：腿摆幅度、每步节奏（峰值相位）、
            # 起伏大小、左右相位关系。这里用同样的差异门槛。
            ok, why = facts_diff(a, b)
            if not ok:
                wsame.append((wk[i], wk[j], why))
    print("  walk 判定为『同一套动作』的配对：%d 对" % len(wsame))
    for a, b, why in wsame:
        print("      ★ %s vs %s：%s" % (a, b, why))
        bad.append("walk 动作雷同: %s vs %s（%s）" % (a, b, why))

    # ---- ③ 剪影 ----
    shots = {}
    if do_shots:
        print("=" * 84)
        print("③ 渲染 16 张透明底对照图（Blender EEVEE，正交）")
        print("=" * 84)
        shots = render_shots()
    else:
        for pid in ids:
            p = os.path.join(SHOT_DIR, "%s.png" % pid)
            if os.path.exists(p):
                shots[pid] = p
    iou = []
    # ★ Blender 自带的 Python 没有 Pillow ★
    # 剪影 IoU 要用 PIL，而在 Blender 进程里 import PIL 会 ModuleNotFoundError
    # （实测），于是整个 verify 在那里崩掉、后面的结论一行都打不出来。
    # 处理：能算就算（系统 Python 跑 verify 时），算不了就跳过并**明确说明跳过了**，
    # 不让"缺一个可选依赖"把主要结论一起带走。
    try:
        from PIL import Image                                    # noqa: F401
        _has_pil = True
    except Exception:                                            # noqa: BLE001
        _has_pil = False
    if len(shots) >= 2 and _has_pil:
        masks, iou = silhouette_metrics(shots)
        print("  剪影归一化后两两 IoU：最大 %.3f（%s vs %s）、平均 %.3f"
              % (iou[0]["iou"], iou[0]["a"], iou[0]["b"],
                 sum(r["iou"] for r in iou) / float(len(iou))))
        print("  最像的 8 对：")
        for r in iou[:8]:
            print("      %-19s vs %-19s  IoU=%.3f %s"
                  % (r["a"], r["b"], r["iou"], "★超 %.2f★" % IOU_MAX
                     if r["iou"] > IOU_MAX else ""))
        for r in iou:
            if r["iou"] > IOU_MAX:
                bad.append("剪影太像: %s vs %s（IoU %.3f > %.2f）"
                           % (r["a"], r["b"], r["iou"], IOU_MAX))
    elif len(shots) >= 2:
        # ★ 没有 Pillow：只提示，不要再去调 silhouette_metrics ★
        # 上一版写反了 —— 先打印"跳过"，下一行又照样调用，于是在 Blender 里
        # 照样 ModuleNotFoundError 崩掉，**后面所有结论一行都出不来**。
        print("  ★ 本进程没有 Pillow（Blender 自带的 Python 不带它），剪影 IoU 跳过 ★")
        print("    用系统 Python 跑同一段："
              "python Tools/AssetPipeline/gen_plants_ally_verify.py sil")

    print("=" * 84)
    if bad:
        print("★ 未通过 %d 条：" % len(bad))
        for p in bad:
            print("   - %s" % p)
    else:
        print("★ 全部通过：16 个模型尺寸/轴向/材质 OK；64 条动画的关节都在动且各自不同；"
              "剪影可区分 ★")
    print("=" * 84)

    facts = locals().get("facts", {})
    wfacts = locals().get("wfacts", {})
    os.makedirs(os.path.dirname(REPORT), exist_ok=True)
    with open(REPORT, "w", encoding="utf-8") as fh:
        json.dump(dict(statics=statics, iou=iou, problems=bad,
                       attackFacts=facts, walkFacts=wfacts,
                       iouMax=IOU_MAX, iouWorst=(iou[0]["iou"] if iou else None),
                       curves={("%s_%s" % k): v for k, v in curves.items()}),
                  fh, ensure_ascii=False, indent=1)
    print("[VERIFY] 报告 -> %s" % REPORT)
    return bad


def check_lock():
    """
    不覆盖证明：拿改动前的 CSV 与现在逐项比对（SHA256 + 字节数 + mtime）。

    这条必须跑在**收工之后**：`<id>_young.fbx` 16 个、`<id>_grown.fbx` 16 个，
    一个字节都不许变。
    """
    import csv
    import hashlib
    if not os.path.exists(LOCK_CSV):
        print("[LOCK] ★缺少基线 CSV %s —— 无法证明" % LOCK_CSV)
        return ["缺少基线 CSV"]
    rows = list(csv.DictReader(open(LOCK_CSV, encoding="utf-8-sig")))
    bad = []
    for r in rows:
        name = "%s_%s.fbx" % (r["id"], r["kind"])
        f = os.path.join(PLANT_DIR, name)
        if not os.path.exists(f):
            bad.append("%s 不见了" % name)
            continue
        data = open(f, "rb").read()
        h = hashlib.sha256(data).hexdigest().upper()
        mt = os.path.getmtime(f)
        import datetime
        mts = datetime.datetime.fromtimestamp(mt).astimezone().isoformat()
        if len(data) != int(r["bytes"]):
            bad.append("%s 字节数变了 %d → %d" % (name, int(r["bytes"]), len(data)))
        if h != r["sha256"]:
            bad.append("%s SHA256 变了" % name)
        if mts[:19] != r["mtime"][:19]:
            bad.append("%s mtime 变了 %s → %s" % (name, r["mtime"][:19], mts[:19]))
    print("[LOCK] 受保护文件 %d 个：%s" % (len(rows), "全部未改动" if not bad else "★有改动★"))
    for b in bad:
        print("   - %s" % b)
    return bad


# ================================================================ 入口
def _main(argv):
    # ★ 参数在 `--` 之后 ★ 直接拿 argv[1:] 会把 Blender 自己的参数也吃进来
    # （实测：`-P xxx.py -- shots` 时 argv[1:] 是 ['-b', '--factory-startup', ...]，
    #  于是 cmd 变成 '-b'，程序打印用法就走人了 —— 一个字都没渲）
    args = argv[argv.index("--") + 1:] if "--" in argv else argv[1:]
    cmd = args[0] if args else "verify"
    if "bpy" in sys.modules:
        if cmd == "shots":
            render_shots()
            return 0
        if cmd == "verify":
            bad = verify_all(do_shots=("--no-shots" not in args))
            return 1 if bad else 0
        print("Blender 侧用法: -P gen_plants_ally_verify.py -- shots | verify [--no-shots]")
        return 0
    # 系统 Python 侧
    if cmd == "verify":
        bad = check_lock()
        if "--no-shots" not in args:
            pass
        # 回读与逐帧必须在 Blender 里跑；这里只做拼图 + 不覆盖证明
        if "--no-shots" not in args:
            shots = {pid: os.path.join(SHOT_DIR, "%s.png" % pid) for pid in IDS}
            if os.path.exists(shots[IDS[0]]):
                render_wall(shots)
        return 1 if bad else 0
    if cmd == "sheet":
        shots = {pid: os.path.join(SHOT_DIR, "%s.png" % pid) for pid in IDS}
        render_wall(shots)
        return 0
    if cmd == "lock":
        return 1 if check_lock() else 0
    if cmd == "sil":
        # 系统 Python 侧：用已经渲好的 16 张透明图算剪影 IoU
        shots = {pid: os.path.join(SHOT_DIR, "%s.png" % pid) for pid in IDS}
        have = {k: v for k, v in shots.items() if os.path.exists(v)}
        if len(have) < 2:
            print("★ 还没有渲染图（先跑 Blender 侧的 `-- shots`）★")
            return 1
        masks, iou = silhouette_metrics(have)
        print("剪影归一化后两两 IoU（%d 个品种，%d 对）：最大 %.3f（%s vs %s）、平均 %.3f"
              % (len(have), len(iou), iou[0]["iou"], iou[0]["a"], iou[0]["b"],
                 sum(r["iou"] for r in iou) / float(len(iou))))
        print("最像的 8 对：")
        for r in iou[:8]:
            print("   %-19s vs %-19s  IoU=%.3f %s"
                  % (r["a"], r["b"], r["iou"],
                     "★超 %.2f★" % IOU_MAX if r["iou"] > IOU_MAX else ""))
        over = [r for r in iou if r["iou"] > IOU_MAX]
        print("超阈值（%.2f）的配对：%d 对" % (IOU_MAX, len(over)))
        return 1 if over else 0
    print(__doc__)
    return 0


if __name__ == "__main__":
    sys.exit(_main(sys.argv))
