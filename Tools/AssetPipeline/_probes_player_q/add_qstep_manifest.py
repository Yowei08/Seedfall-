# -*- coding: utf-8 -*-
"""
把 Q 版玩家这一步写进流水线 manifest（新增一条 step，不动其它条目）。
================================================================================
为什么要有这一步：`manifest.json` 是"产物 = 脚本 + 参数 + 工具版本"的账本。
新加了一组产物却不记账，下次别人（或三个月后的自己）就不知道
`Art/Characters/player.fbx` 到底是哪个脚本、哪条命令生成的。

写法：读 manifest → 按 id 去重地 append → 原编码（UTF-8 无 BOM）写回，缩进 2。

用法：
    python add_qstep_manifest.py
"""

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
AP = os.path.dirname(HERE)
MANIFEST = os.path.join(AP, "manifest.json")

STEP = {
    "id": "player-qstyle",
    "script": "Tools/AssetPipeline/gen_player_q.py",
    "library": "Tools/AssetPipeline/seedfall_kit.py",
    "verifier": "Tools/AssetPipeline/verify_player_q.py（回读 + 射线遮挡审计 + 出图）",
    "command": "blender.exe -b --factory-startup -P gen_player_q.py -- all",
    "why": ("需求：'玩家模型也重做，包括玩家动画资源，使用原本的 Q 版风格'。"
            "Q 版 = 头大身小（头含帽 ≥ 总高 40%）、四肢短粗圆头收尾、大圆弧无尖角、高饱和大色块。"),
    "approach": [
        "真骨架 + 骨头父子绑定（不是蒙皮）：19 根命名骨骼与旧版**逐字相同**，"
        "Unity 侧 WorldView.AttachTools 靠名字找 Bone_Hand_R 挂武器，改名会断。",
        "Q 版比例靠**椭球参数**做：头 rz=0.36 撑起 40% 的高度，rx/ry 压小以少挡身体；"
        "帽檐半径 0.345 = 头半宽 0.3312 × 1.0417（需求上限 1.1）。",
        "头部把帽子做成'压扁圆顶 + 后仰 15°'而不是整颗球扣在头上：水平圆盘会在 60° 俯视下"
        "投出 0.87×半径 宽的死区，水平的帽檐会把整张脸盖住（实测眼睛可见 0/34）。",
        "attack/swap 不写 Hips 的 location 曲线（角度照写、恒 0），"
        "这样游戏侧两段采样（move 先、attack 后）不会把跑动的髋部起伏压回静止。",
    ],
    "outputs": [
        "Assets/Resources/Art/Characters/player.fbx",
        "Assets/Resources/Art/Tools/tool_hoe.fbx",
        "Assets/Resources/Art/Tools/tool_can.fbx",
        "Assets/Resources/Art/Tools/tool_fert.fbx",
        "Assets/Resources/Art/Anim/player_idle.fbx",
        "Assets/Resources/Art/Anim/player_walk.fbx",
        "Assets/Resources/Art/Anim/player_hit.fbx",
        "Assets/Resources/Art/Anim/player_swap.fbx",
        "Assets/Resources/Art/Anim/player_attack_hoe.fbx",
        "Assets/Resources/Art/Anim/player_attack_can.fbx",
        "Assets/Resources/Art/Anim/player_attack_fert.fbx",
    ],
    "budget": {
        "playerTris": 1868,
        "playerHeightM": 1.8385,
        "headRatio": 0.4126,
        "brimRatio": 1.0417,
        "toolTris": {"hoe": 160, "can": 992, "fert": 492},
    },
    "animations": [
        "idle 96帧 循环 4.00s 呼吸 0.25Hz（胸腔 X 摆幅 12.40°）",
        "walk 96帧 循环 4.00s 步频 1.4845Hz + 呼吸 0.2474Hz（6:1，腿摆 ±30°）",
        "hit 24帧 一次性 1.00s 后仰（胸腔 13.95°）",
        "swap 9帧 0.375s 只上半身（腿骨 0.0000°）",
        "attack_hoe/can/fert 各 36帧 1.50s 只上半身（腿骨 0.0000°，三套互异）",
    ],
    "verification": {
        "reader_selftest": "把骨骼摆成已知 30.0°/-27.0°/12.5°，用同一个读数函数读回，误差 < 0.01°",
        "static": "回读 FBX：高度 1.8385 在 Z 轴、zmin=0、1868 面、19 根骨骼逐名核对、材质名全在调色板内",
        "anim_frames": "逐帧读回：三套 attack 的 7 根腿骨最大角 = 0.0000°；三套右上臂逐帧最大差 37.7~75.2°",
        "visibility_audit": "按游戏相机（pitch 60°，正前方）逐面射线遮挡审计："
                            "可见面积 头 33.1% / 肩 59.1% / 手臂 25.8% / 躯干 20.5% / 腿 31.5%，"
                            "屏幕纵向次序 头 → 肩 → 手臂 → 躯干 → 腿",
        "captures": ["Docs/captures/player_qstyle_top60.png（主验收：60° 俯视）",
                     "Docs/captures/player_qstyle_preview.png（旧版/新版同角度对照）",
                     "Docs/captures/player_qstyle_attack_poses.png（三套攻击姿态稿）"],
    },
    "pitfall": [
        "GRIP 的基必须与上一版**逐字相同**：朝向会被烘进网格顶点，'等价但差 180°'的基会让"
        "工具在握把轴上翻半圈（锄刃朝后），而 Unity 侧那个固定的 Euler(0,180,0) 是按旧基试出来的。",
        "跨版本复用函数时不要按位置解包：gen_player_v2.grip_frame() 返回 3 个值、本版 2 个，"
        "`q, _ = ...` 当场 ValueError。",
        "验证指标要按**面积**加权而不是按顶点：UV 球两极顶点密集，按顶点算会把躯干报成 15.9%，"
        "而渲染图上躯干明明是一大块。",
        "`view_transform` 必须是 Standard：Blender 4/5 默认 AgX 会把高饱和色压成灰白，"
        "而'高饱和大色块'正是 Q 版的卖点。",
    ],
}


def main():
    with open(MANIFEST, "r", encoding="utf-8") as fh:
        data = json.load(fh)
    steps = data.setdefault("steps", [])
    before = len(steps)
    steps[:] = [s for s in steps if s.get("id") != STEP["id"]]
    steps.append(STEP)
    data["pending"] = [p for p in data.get("pending", [])
                       if "动画" not in p or "待机" not in p]
    with open(MANIFEST, "w", encoding="utf-8") as fh:
        json.dump(data, fh, ensure_ascii=False, indent=2)
        fh.write("\n")
    print(f"[MANIFEST] steps {before} → {len(steps)}，已写入 {STEP['id']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
