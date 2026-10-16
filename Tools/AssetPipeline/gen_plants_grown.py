# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 「成长中」阶段模型生成器（v99）
================================================

## 为什么需要这个脚本

一株植物有 4 个视觉阶段，其中两个曾经共用同一个模型文件：

    幼苗      <id>_young.fbx      生长 0~55%
    成长中    ——（缺失）——         生长 55%~100%     ← 本脚本补的就是这一格
    成体      <id>.fbx            长熟待收割
    植物帮手   同 <id>.fbx         收割后跑动参战

`<id>.fbx` 后被改为**拟人化双足**版本，于是"成长中"这段没有旧版造型可用，
画面上表现为「一颗小芽直接变成一个会走路的小人」——中间缺一帧体量过渡。

旧版（非拟人化）成熟造型是可以重建的：建模代码仍在 `gen_plants.py` 里
（`PLANTS` 参数表 + `BUILDERS` 按 kind 分派）。本脚本**import 它**，
不复制粘贴——造型只有一个数据源，以后调 `PLANTS` 两边一起变。

## 产出

    Assets/Resources/Art/Plants/<id>_grown.fbx  × 16

## 三条硬约定

① **绝不覆盖** `<id>.fbx` 与 `<id>_young.fbx`（线上在用）。
   脚本出口处有断言把关：输出文件名必须以 `_grown.fbx` 结尾，且不得命中受保护清单。
   也没有改 `gen_plants.py` 的输出路径——它的 build() 依旧只写 `<id>.fbx` / `<id>_young.fbx`。

② **尺寸**：高度落在 [1.0, 1.3] m，按 `PLANTS` 的 size 线性映射——小株靠 1.0、S 级靠 1.3，
   于是「幼苗(0.29~0.72) → 成长中(1.0~1.3) → 成体(1.62~1.79)」在体量上**逐株单调递增**，
   同时保留稀有度读出（D 级比 S 级矮）。

③ **原点与轴向**：高度在 Z、原点在**脚底中心**、zmin = 0。轴向只在一处处理——
   `export_fbx(..., bake_yup=False)`，不再自己烘一次 Y-up（那会让模型躺倒，这个坑踩过）。
   旧版土壤土丘的球心低于地面（zmin ≈ -0.02），这里把它整体抬起来，正好踩在 z=0 上。

## 面数预算的处理（需要留意的一处取舍）

`gen_plants.py` 的旧版造型当初没按 1500 面预算做，实测 16 个里有 11 个超标：

    moontree 2728 / char_flower 2392 / moss_shield 2120 / healing_clover 2120 /
    holy_tree_guard 2096 / plague_mother 1980 / starvine 1888 / life_spring 1780 /
    radish_guard 1720 / toxic_mushroom 1680 / thorn_pumpkin 1520

本脚本的做法是**够用就好**，不动造型、只降细分：
  · 造型完全来自 `gp.BUILDERS`（单一数据源，一行没改）
  · 只把 `seedfall_kit` 的图元分辨率按一个系数收窄（球 segs/rings、柱/锥 verts）
  · 系数**逐株自适应**：从 1.0 往下试，取第一个落进预算的——本来就不超标的 5 个
    （frost_ivy 700 / stone_fist 1088 / ironroot_colossus 1148 / pea_shooter 1388 /
    chili_mage 1432）保持**原始分辨率一点不动**
  · 每档有下限（segs≥8 / rings≥6 / verts≥8）且**不会把已经更低的图元抬高**，
    免得 6 棱的尖刺反被"降级"成 8 棱
  · 想拿旧版原样分辨率：`--budget none`，一条命令的事

细分密度是渲染成本，不是造型。轮廓/配色/材质键名/部件布局全部不变。

## 用法

    blender.exe -b --factory-startup -P gen_plants_grown.py -- all
    blender.exe -b --factory-startup -P gen_plants_grown.py -- radish_guard moontree
    blender.exe -b --factory-startup -P gen_plants_grown.py -- all --budget none
    blender.exe -b --factory-startup -P gen_plants_grown.py -- all --report out.json
"""

import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy                     # noqa: E402
import seedfall_kit as kit     # noqa: E402
import gen_plants as gp        # noqa: E402

GROWN_SUFFIX = "_grown"
PLANT_DIR = gp.PLANT_DIR           # 复用 gen_plants 的目录常量，不另立一份
TRI_BUDGET = 1500                  # 每个模型的上限
HEIGHT_LO, HEIGHT_HI = 1.00, 1.30  # 成长中阶段的体量区间（米）

# 降细分候选系数：从"原样"往下试，取第一个进预算的
LOD_STEPS = (1.00, 0.90, 0.85, 0.80, 0.75, 0.70, 0.65, 0.60, 0.55, 0.50)
LOD_FLOOR = {"segs": 8, "rings": 6, "verts": 8}

# 受保护文件：这两个是线上在用的，一个字节都不许动
PROTECTED_SUFFIXES = ("_young.fbx",)


# ---------------------------------------------------------------- 尺寸映射
def target_height(size):
    """
    把 PLANTS 的 size 线性映射到 [HEIGHT_LO, HEIGHT_HI]。
    D 级最矮、S 级最高——稀有度在体量上仍然读得出来，同时保证逐株都高过幼苗。
    """
    sizes = [p["size"] for p in gp.PLANTS.values()]
    lo, hi = min(sizes), max(sizes)
    t = 0.0 if hi <= lo else (size - lo) / (hi - lo)
    return HEIGHT_LO + t * (HEIGHT_HI - HEIGHT_LO)


# ---------------------------------------------------------------- 降细分（只动图元分辨率）
_ORIG = {}


def _lod_count(n, f, floor):
    """收窄一个分辨率参数：向下取整、不低于 floor、**永不超过原值**。"""
    return min(int(n), max(int(floor), int(round(n * f))))


class LodPatch(object):
    """
    临时替换 seedfall_kit 的图元函数，按系数收窄分辨率。
    造型代码（gen_plants.BUILDERS）一行不用改——它调的还是 kit.sphere/cyl/cone。
    """

    def __init__(self, factor):
        self.f = float(factor)

    def __enter__(self):
        if self.f >= 0.999:
            return self
        if not _ORIG:
            _ORIG["sphere"] = kit.sphere
            _ORIG["cyl"] = kit.cyl
            _ORIG["cone"] = kit.cone
            _ORIG["box"] = kit.box
        f = self.f

        def sphere(name, loc, radius, color, scale=(1, 1, 1), segs=20, rings=12,
                   rot=(0, 0, 0), emission=0.0):
            return _ORIG["sphere"](name, loc, radius, color, scale=scale,
                                   segs=_lod_count(segs, f, LOD_FLOOR["segs"]),
                                   rings=_lod_count(rings, f, LOD_FLOOR["rings"]),
                                   rot=rot, emission=emission)

        def cyl(name, loc, radius, depth, color, verts=16, rot=(0, 0, 0), scale=(1, 1, 1),
                smooth=True, emission=0.0):
            return _ORIG["cyl"](name, loc, radius, depth, color,
                                verts=_lod_count(verts, f, LOD_FLOOR["verts"]),
                                rot=rot, scale=scale, smooth=smooth, emission=emission)

        def cone(name, loc, r1, r2, depth, color, verts=16, rot=(0, 0, 0), scale=(1, 1, 1),
                 smooth=True, emission=0.0):
            return _ORIG["cone"](name, loc, r1, r2, depth, color,
                                 verts=_lod_count(verts, f, LOD_FLOOR["verts"]),
                                 rot=rot, scale=scale, smooth=smooth, emission=emission)

        kit.sphere, kit.cyl, kit.cone = sphere, cyl, cone
        return self

    def __exit__(self, *exc):
        if _ORIG:
            kit.sphere = _ORIG["sphere"]
            kit.cyl = _ORIG["cyl"]
            kit.cone = _ORIG["cone"]
            kit.box = _ORIG["box"]
        return False


# ---------------------------------------------------------------- 几何小工具
def _select_only(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def _world_zmin(obj):
    import mathutils
    pts = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    return min(p.z for p in pts)


def _apply_all(obj):
    _select_only(obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


# ---------------------------------------------------------------- 单个植物
def out_path(plant_id):
    path = os.path.join(PLANT_DIR, plant_id + GROWN_SUFFIX + ".fbx")
    # 出口把关：只写 _grown，且不得命中受保护文件
    assert path.endswith(GROWN_SUFFIX + ".fbx"), path
    for bad in PROTECTED_SUFFIXES:
        assert not path.endswith(bad), "拒绝写入受保护文件: " + path
    assert os.path.basename(path) != plant_id + ".fbx", "拒绝覆盖成体: " + path
    return path


def build_parts(plant_id, lod):
    """用 gen_plants 的 BUILDERS 造零件（造型单一数据源），只按 lod 收窄细分。"""
    params = dict(gp.PLANTS[plant_id])
    kit.reset()
    with LodPatch(lod):
        parts = gp.BUILDERS[params["kind"]](params)
        obj = kit.join(parts, "PL_" + plant_id + GROWN_SUFFIX)
    return params, obj


def build_grown(plant_id, budget=TRI_BUDGET):
    """
    造一个「成长中」模型并导出。返回一行摘要（也给 --report 用）。
    形状取自旧版成熟造型；等比缩放到目标高度；原点落在脚底中心、zmin = 0。
    """
    params = dict(gp.PLANTS[plant_id])
    target = target_height(params["size"])
    native_tris = None
    lod_used = None
    obj = None

    for lod in LOD_STEPS:
        _params, obj = build_parts(plant_id, lod)
        kit.apply_transforms(obj)          # 只烘旋转+缩放，不动位置
        tris = kit.tri_count(obj)
        if native_tris is None:
            native_tris = tris             # lod=1.0 那一档就是旧版原样
        if budget is None or tris < budget:
            lod_used = lod
            break
        # 这一档还是超标：扔掉重来（下一轮 kit.reset() 会清场景）
    else:
        raise RuntimeError("%s: 连 %.2f 档都塞不进 %d 面预算（最后一档 %d 面）"
                           % (plant_id, LOD_STEPS[-1], budget, tris))

    # ① 等比缩放到目标高度（对自身尺寸取比例，所以最终高度恒等于 target）
    hz = obj.dimensions.z
    k = target / hz if hz > 1e-6 else 1.0
    obj.scale = (k, k, k)
    kit.apply_transforms(obj)

    # ② 抬到踩地：旧版 _mound 的土丘球心低于地面（zmin 略负），抬起来正好 zmin = 0
    lift = -_world_zmin(obj)
    obj.location.z += lift
    _apply_all(obj)

    # ③ 原点 = 脚底中心（世界原点）
    kit.set_origin(obj, (0.0, 0.0, 0.0))

    # ④ 导出前自检：尺寸/落点/原点都对不上就别写盘
    dims = tuple(round(v, 4) for v in obj.dimensions)
    zmin = round(_world_zmin(obj), 5)
    loc = tuple(round(v, 5) for v in obj.location)
    mats = [m.name for m in obj.data.materials if m]
    tris = kit.tri_count(obj)
    assert abs(dims[2] - target) < 0.005, "高度没对上: %s vs %s" % (dims[2], target)
    assert abs(zmin) < 1e-4, "zmin 不为 0: %s" % zmin
    assert abs(loc[0]) < 1e-5 and abs(loc[1]) < 1e-5 and abs(loc[2]) < 1e-5, "原点不在脚底: %s" % (loc,)
    if budget is not None:
        assert tris < budget, "超出面数预算: %d" % tris

    path = out_path(plant_id)
    kit.export_fbx(obj, path)          # bake_yup=False：轴向只由导出器转一次
    row = {
        "id": plant_id,
        "kind": params["kind"],
        "size": params["size"],
        "targetHeight": round(target, 4),
        "height": dims[2],
        "dimX": dims[0],
        "dimY": dims[1],
        "zmin": zmin,
        "origin": list(loc),
        "tris": tris,
        "nativeTris": native_tris,
        "lod": round(lod_used, 3),
        "materials": mats,
        "path": path,
    }
    print("[SEEDFALL-PLANT-GROWN] %-20s h=%.3fm tris=%4d (native %4d, lod %.2f) zmin=%.4f mats=%s -> %s"
          % (plant_id, dims[2], tris, native_tris, lod_used, zmin, ",".join(mats), path))
    return row


def build_all(ids=None, budget=TRI_BUDGET, report=None):
    ids = list(ids) if ids else list(gp.PLANTS.keys())
    rows = []
    for pid in ids:
        rows.append(build_grown(pid, budget=budget))
    over = [r["id"] for r in rows if budget is not None and r["tris"] >= budget]
    print("[SEEDFALL-PLANT-GROWN] 共 %d 个，面数 %d~%d，高度 %.3f~%.3f，超预算: %s"
          % (len(rows), min(r["tris"] for r in rows), max(r["tris"] for r in rows),
             min(r["height"] for r in rows), max(r["height"] for r in rows), over or "无"))
    if report:
        kit.ensure_dir(report)
        with open(report, "w", encoding="utf-8") as fh:
            json.dump({
                "command": "blender.exe -b --factory-startup -P gen_plants_grown.py -- all"
                           + ("" if budget is not None else " --budget none"),
                "trisBudget": budget,
                "heightRange": [HEIGHT_LO, HEIGHT_HI],
                "source": "gen_plants.py 的 PLANTS 参数表 + BUILDERS（import，非复制）",
                "models": rows,
            }, fh, ensure_ascii=False, indent=1)
        print("[SEEDFALL-PLANT-GROWN] report -> " + report)
    return rows


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args or args[0] in ("-h", "--help"):
        print("用法: blender -b --factory-startup -P gen_plants_grown.py -- all | <植物id...>")
        print("     可选: --budget <面数|none>   --report <out.json>")
        print("  植物:", ", ".join(gp.PLANTS))
    else:
        budget = TRI_BUDGET
        report = None
        if "--budget" in args:
            i = args.index("--budget")
            raw = args[i + 1]
            args = args[:i] + args[i + 2:]
            budget = None if raw.lower() in ("none", "off", "0") else int(raw)
        if "--report" in args:
            i = args.index("--report")
            report = args[i + 1]
            args = args[:i] + args[i + 2:]
        ids = None if args and args[0] == "all" else args
        build_all(ids=ids, budget=budget, report=report)
