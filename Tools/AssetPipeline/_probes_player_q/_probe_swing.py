# -*- coding: utf-8 -*-
"""
探针：walk 的**手臂与腿是否反相**（同手同脚是最容易被一眼看出来的动画缺陷）
================================================================================
为什么要单独量：腿骨的判据（±30° 反相）在 `verify_player_q.py` 里已经量过了，
但**手臂**用的是 `apply_arm` 里的语义轴（抬起/前摆/外展），那几个轴的符号一旦写反，
腿的判据照样全过，走路却会变成"同手同脚"。而这一点只靠渲染图很难确认
（60° 俯视下手的大小只有几十像素）。

做法：逐帧读**手骨与脚骨的世界坐标**，看它们的"前后"分量相关性。
     · 手臂与腿反相 ⇒ 相关为负
     · 同手同脚     ⇒ 相关为正
人的正常走姿是**反相**（左手朝前时右腿朝前）。

用法：blender -b --factory-startup -P _probe_swing.py
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
AP = os.path.dirname(HERE)
sys.path.insert(0, AP)
import gen_player_q as gq          # noqa: E402

import bpy                          # noqa: E402

ANIM = os.path.join(gq.ANIM_DIR, "player_walk.fbx")


def main():
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=ANIM, automatic_bone_orientation=False,
                             ignore_leaf_bones=False)
    arm = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE'][0]
    sc = bpy.context.scene
    n = gq.ANIMS["walk"][0] + 1

    samples = {"Hand_R": [], "Hand_L": [], "Foot_R": [], "Foot_L": []}
    for f in range(1, n + 1):
        sc.frame_set(f)
        for key, bone in (("Hand_R", "Bone_Hand_R"), ("Hand_L", "Bone_Hand_L"),
                          ("Foot_R", "Bone_Foot_R"), ("Foot_L", "Bone_Foot_L")):
            pb = arm.pose.bones[bone]
            samples[key].append((arm.matrix_world @ pb.tail).copy())

    def corr(a, b, axis=1):
        """只在 Y（前后）分量上做相关 —— 走路的前后摆就在这个轴上。"""
        va = [p[axis] for p in a]
        vb = [p[axis] for p in b]
        ma, mb = sum(va) / len(va), sum(vb) / len(vb)
        ca = [v - ma for v in va]
        cb = [v - mb for v in vb]
        num = sum(x * y for x, y in zip(ca, cb))
        den = (sum(x * x for x in ca) * sum(y * y for y in cb)) ** 0.5
        return num / den if den > 1e-9 else 0.0

    print("=" * 70)
    print("walk · 手臂 / 腿 前后摆的相位关系")
    print("=" * 70)
    print("  判据：**同侧** 手↔脚 必须反相；**异侧** 手↔脚 必须同相；两腿必须反相。")
    print("  ⚠ 第一版这里把"右手 vs 左腿 = +0.994"也标成"同手同脚" —— 那是**错的**：")
    print("    正常人走路就是"右手与左腿一起向前"。指标写错会把正确结果读成故障。")
    #: (标签, 样本A, 样本B, 期望符号)
    rows = [("右手 vs 右腿（同侧）", "Hand_R", "Foot_R", -1),
            ("左手 vs 左腿（同侧）", "Hand_L", "Foot_L", -1),
            ("右手 vs 左腿（异侧）", "Hand_R", "Foot_L", +1),
            ("左手 vs 右腿（异侧）", "Hand_L", "Foot_R", +1),
            ("右手 vs 左手（两臂）", "Hand_R", "Hand_L", -1),
            ("右腿 vs 左腿（两腿）", "Foot_R", "Foot_L", -1)]
    fails = 0
    for lab, a, b, want in rows:
        c = corr(samples[a], samples[b])
        ok = (c < -0.3) if want < 0 else (c > 0.3)
        if not ok:
            fails += 1
        print(f"  {lab:<22} 相关 = {c:+.3f}  {'✔' if ok else '★不符★'}"
              f"（期望 {'反相' if want < 0 else '同相'}）")
    for lab, a in (("右手", "Hand_R"), ("右腿", "Foot_R")):
        ys = [p[1] for p in samples[a]]
        print(f"  {lab} Y 摆幅 = {max(ys) - min(ys):.4f} m  区间 "
              f"[{min(ys):+.4f}, {max(ys):+.4f}]")
    print(f"  结论：{'全部符合走姿 ✔' if fails == 0 else f'★{fails} 条不符★'}")


if __name__ == "__main__":
    main()
