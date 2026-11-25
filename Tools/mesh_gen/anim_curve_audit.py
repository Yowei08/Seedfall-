#!/usr/bin/env python3
"""anim_curve_audit.py —— 逐节点审计 Unity AnimationClip 里**到底哪些节点在动**。

用途（本次任务的取证工具）
--------------------------
"攻击动画卡脚"的第一手证据不是看代码猜，而是**读进 Unity 的那份片段**：
每一条曲线绑到哪个节点（`path`）、是什么通道（rotation / position / euler / scale）、
在整个片段里**实际变化了多少**（旋转取四元数夹角，位移取欧氏距离）。

于是"攻击时动的是哪几个节点"这个问题有确定答案，而且可以逐条给数：
  · 变化量 ≈ 0  ⇒ 这个节点**被片段钉死**（曲线存在但恒为常量）
  · 变化量 > 阈值 ⇒ 这个节点**真的在动**

读的是 Unity 自己序列化出来的 YAML（`Assets/Resources/Art/AnimMerged/*.asset`），
不是 Blender 侧的重算值 —— 后者只能说明"打算怎么动"，前者才是"Unity 采样时会写什么"。

用法
----
    python Tools/mesh_gen/anim_curve_audit.py                      # 审计 player_* 全套
    python Tools/mesh_gen/anim_curve_audit.py --glob player_attack # 只审计匹配的
    python Tools/mesh_gen/anim_curve_audit.py --md                 # 输出 markdown 表
    python Tools/mesh_gen/anim_curve_audit.py --group body         # 按上/下半身分组汇总

退出码：0 = 全部文件解析成功；2 = 有文件读不到或没有曲线。
"""

import argparse
import glob
import math
import os
import re
import sys

DEFAULT_CLIP_DIR = "Assets/Resources/Art/AnimMerged"

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(os.path.dirname(HERE))

# ── 骨架分组（与 gen_player_q.py 的 LOWER_BODY / UPPER_BODY 同源）────────────
LOWER = ["Bone_Hips", "Bone_Thigh_L", "Bone_Thigh_R",
         "Bone_LowerLeg_L", "Bone_LowerLeg_R", "Bone_Foot_L", "Bone_Foot_R"]
UPPER = ["Bone_Spine", "Bone_Chest", "Bone_Neck", "Bone_Head",
         "Bone_Shoulder_L", "Bone_Shoulder_R",
         "Bone_UpperArm_L", "Bone_UpperArm_R",
         "Bone_LowerArm_L", "Bone_LowerArm_R",
         "Bone_Hand_L", "Bone_Hand_R"]
# 手部链 = 允许在攻击时动的那几个（需求：只动手部 / 上臂 / 前臂 / 武器挂点）
HANDS = ["Bone_UpperArm_R", "Bone_LowerArm_R", "Bone_Hand_R"]

SECTION_RE = re.compile(r"^  m_(\w+)Curves:\s*$")
CURVE_RE = re.compile(r"^  - curve:\s*$")
PATH_RE = re.compile(r"^    path:\s*(.*)$")
TIME_RE = re.compile(r"^\s+time:\s*([-\d.eE+]+)\s*$")
VALUE4_RE = re.compile(
    r"^\s+value:\s*\{x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+),\s*z:\s*([-\d.eE+]+),\s*w:\s*([-\d.eE+]+)\}")
VALUE3_RE = re.compile(
    r"^\s+value:\s*\{x:\s*([-\d.eE+]+),\s*y:\s*([-\d.eE+]+),\s*z:\s*([-\d.eE+]+)\}")


def short(path):
    """绑定的 path 只留最后一级节点名（层级前缀对"哪个节点"没意义）。"""
    if not path:
        return "<root>"
    return path.replace("\\", "/").rstrip("/").split("/")[-1]


def group_of(node):
    if node in LOWER:
        return "下半身"
    if node in HANDS:
        return "手部"
    if node in UPPER:
        return "上半身"
    return "其他"


def quat_angle_deg(q0, q1):
    """两个四元数之间的夹角（度）。"""
    d = abs(sum(a * b for a, b in zip(q0, q1)))
    d = max(-1.0, min(1.0, d))
    return math.degrees(2.0 * math.acos(d))


def parse_clip(path):
    """→ list of dict(section, node, keys, pos_max_m, rot_max_deg)"""
    curves = []
    section = None
    node = None
    times = []
    values = []

    def flush():
        if section is None or node is None or not values:
            return
        rot_max = 0.0
        pos_max = 0.0
        if values and len(values[0]) == 4:
            for v in values:
                rot_max = max(rot_max, quat_angle_deg(values[0], v))
        else:
            for v in values:
                pos_max = max(pos_max, math.dist(values[0], v))
        curves.append({
            "section": section, "node": node, "keys": len(values),
            "rot_max_deg": rot_max, "pos_max_m": pos_max,
        })

    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            hit = SECTION_RE.match(line)
            if hit:
                flush()
                section, node, times, values = hit.group(1), None, [], []
                continue
            hit = PATH_RE.match(line)
            if hit:
                # ★ 顺序不能反（这里错一次，整张表会**整体错位一格**）★
                # Unity 的序列化把 `path:` 写在每个曲线块的**末尾**：
                #   - curve: {…m_Curve 的 values…}
                #     path: <这个块的节点>
                # 所以看到 `path:` 时，手上攒的 values **就是**这一块的 ⇒
                # 必须先 `node = 新 path` 再 flush ✗ 先 flush 会把这一块算到上一个节点上 ✓
                node = short(hit.group(1))
                flush()
                times, values = [], []
                continue
            if section is None:
                continue
            if CURVE_RE.match(line):
                # 防御：正常顺序下 values 这时已经是空的（上一个块的 path 行已经 flush 过）
                flush()
                node, times, values = None, [], []
                continue
            hit = TIME_RE.match(line)
            if hit:
                continue          # time 与 value 一一对应，value 自带顺序
            hit = VALUE4_RE.match(line)
            if hit:
                values.append(tuple(float(x) for x in hit.groups()))
                continue
            hit = VALUE3_RE.match(line)
            if hit:
                values.append(tuple(float(x) for x in hit.groups()))
    flush()
    return curves


def clip_length(path):
    """从 m_Length / 最后一个 time 读片段长度。"""
    length = 0.0
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        for line in handle:
            hit = TIME_RE.match(line)
            if hit:
                length = max(length, float(hit.group(1)))
    return length


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--clip-dir", default=os.path.join(PROJECT, DEFAULT_CLIP_DIR))
    ap.add_argument("--glob", default="player_*")
    ap.add_argument("--md", action="store_true", help="输出 markdown 表")
    ap.add_argument("--group", action="store_true", help="附上按上/下半身的汇总")
    ap.add_argument("--eps", type=float, default=0.05,
                    help="判定'在动'的旋转阈值（度），位移阈值 = eps/100 米")
    args = ap.parse_args()

    files = sorted(glob.glob(os.path.join(args.clip_dir, args.glob + ".asset")))
    if not files:
        print(f"没有匹配的片段：{args.clip_dir}/{args.glob}.asset", file=sys.stderr)
        return 2

    for path in files:
        name = os.path.splitext(os.path.basename(path))[0]
        curves = parse_clip(path)
        if not curves:
            print(f"{name}: 解析不出曲线", file=sys.stderr)
            return 2
        length = clip_length(path)
        print(f"\n=== {name}  长度 {length:.3f}s  曲线块 {len(curves)}")

        by_node = {}
        for c in curves:
            key = c["node"]
            row = by_node.setdefault(key, {"rot": 0.0, "pos": 0.0, "sections": set(), "keys": 0})
            row["rot"] = max(row["rot"], c["rot_max_deg"])
            row["pos"] = max(row["pos"], c["pos_max_m"])
            row["sections"].add(c["section"])
            row["keys"] = max(row["keys"], c["keys"])

        moving, pinned = [], []
        for node, row in by_node.items():
            (moving if (row["rot"] > args.eps or row["pos"] > args.eps / 100.0) else pinned).append((node, row))

        if args.md:
            print("| 节点 | 分组 | 通道 | 关键帧 | 旋转最大变化° | 位移最大变化 m | 状态 |")
            print("|---|---|---|---|---|---|---|")
        else:
            print(f"  {'节点':<20}{'分组':<8}{'通道':<12}{'帧':>4}{'旋转°':>9}{'位移m':>9}  状态")

        def emit(items, tag, order):
            for node, row in sorted(items, key=lambda kv: (order(kv[0]), kv[0])):
                chans = "+".join(sorted(s.replace("Curves", "") for s in row["sections"]))
                if args.md:
                    print(f"| {node} | {group_of(node)} | {chans} | {row['keys']} | "
                          f"{row['rot']:.2f} | {row['pos']:.4f} | **{tag}** |")
                else:
                    print(f"  {node:<20}{group_of(node):<8}{chans:<12}{row['keys']:>4}"
                          f"{row['rot']:>9.2f}{row['pos']:>9.4f}  {tag}")

        order = lambda n: ("下半身", "手部", "上半身", "其他").index(group_of(n))
        emit(moving, "动", order)
        emit(pinned, "钉死(常量)", order)

        if args.group:
            print("  --- 分组汇总 ---")
            for g in ("下半身", "手部", "上半身", "其他"):
                nodes = [n for n, r in by_node.items() if group_of(n) == g]
                if not nodes:
                    continue
                rot = max(by_node[n]["rot"] for n in nodes)
                pos = max(by_node[n]["pos"] for n in nodes)
                live = sum(1 for n in nodes if by_node[n]["rot"] > args.eps
                           or by_node[n]["pos"] > args.eps / 100.0)
                print(f"  {g:<6}节点 {len(nodes):>2}  在动 {live:>2}  "
                      f"最大旋转 {rot:6.2f}°  最大位移 {pos:.4f} m")
    return 0


if __name__ == "__main__":
    sys.exit(main())
