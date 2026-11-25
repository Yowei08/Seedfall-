# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · v202 「8 株新植物」同框对照图渲染器
================================================================================

一张图回答三件事：

  ① 8 株都做出来了吗？      → 一排 8 列，每列一根成熟体 ✓
  ② 能看出谁是谁吗？        → 剪影各不相同（浆果簇 / 扁瓜 / 藤环 / 掌片 /
                               铃身 / 梯子 / 树干冠层 / 苇秆）✓
  ③ 和既有 22 株比例一致吗？ → 右边 3 株是**既有品种**，与本批 8 株
                               同一个缩放系数、同一条地面、同一个正交机位；
                               图里还画了 1.0m / 1.7m 两条横线当尺子 ✓

## 为什么渲染 FBX，而不是重新生成几何

要证明的是**交付物本身**（`Assets/Resources/Art/Plants/*.fbx`）对不对，不是"生成器还能跑"。
所以 `import_scene.fbx` 把落盘的文件读回来再渲染 —— 导出时轴向/单位/原点错了，这张图立刻露馅
（躺倒 / 巨大 / 陷进地里）。

## 单位一致性是**量出来**的，不是假设的

  1. 用 `out/plant_meshes_8.json` 里的设计高度 `height`（米）当基准；
  2. 每导入一株量一次世界包围盒高度，算 `c_i = 实测 / 设计`；
  3. **断言所有 c_i 相等**（相对离散 < 1e-3）—— 不等就是"一株大一株小"的直接成因 ✗
  4. 用**同一个** `1/c` 缩放全部模型（含既有品种）⇒ 相对大小原样保留 ✓

## 轴向也是量出来的

`bake_yup=False` 导的是 Z-up。导入后最长边若不在 Z 上，说明被转成 Y-up，本脚本会打印出来。

## 图元两点（`ball` 的真实语义）

`qs.ball()` 的 `size` 是**全边长**而不是半边长：`primitive_cube_add(size=1.0, scale=size)`
的顶点落在 ±size/2。这条只影响生成端，渲染端不依赖它。

## 用法

    B="D:\\Steam\\steamapps\\common\\Blender\\blender.exe"
    & $B -b --factory-startup -P Tools/icon_gen/render_plant_lineup_8.py
    & $B -b --factory-startup -P Tools/icon_gen/render_plant_lineup_8.py -- --width 2400 --height 900
    & $B -b --factory-startup -P Tools/icon_gen/render_plant_lineup_8.py -- --only lineup

产物（都在 `Tools/icon_gen/out/`）：
    plant_lineup_8.png   8 株成熟体 + 3 株既有当尺子（主图）
    plant_stages_8.png   8 株 × 三阶段（幼苗 / 成长中 / 成熟）生长阶梯
"""

import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
PLANT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Plants")
REPORT = os.path.join(HERE, "out", "plant_meshes_8.json")
OUT_DIR = os.path.join(HERE, "out")

import bpy                                            # noqa: E402
from mathutils import Vector                          # noqa: E402

NEW_IDS = ["dusk_berry", "tide_gourd", "hollow_vine", "dune_cactus",
           "harvest_bell", "star_ladder", "elder_oak", "quick_reed"]
# 既有品种：只当"尺子"用，证明 8 株没有整体放大/缩小
REF_IDS = ["pea_shooter", "radish_guard", "moontree"]
# 既有 22 株成熟体的高度窄带（`gen_plants_qstyle.MATURE_H_LOCK`）
REF_BAND = (1.68, 1.78)
SUFFIX = {"young": "_young", "grown": "_grown", "mature": ""}
STAGE_CN = {"young": "young", "grown": "grown", "mature": "mature"}


# ════════════════════════════════════════════════════════════ 场景工具
def wipe():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before and o.type == "MESH"]


def world_bbox(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        m = o.matrix_world
        for corner in o.bound_box:
            w = m @ Vector(corner)
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    return lo, hi


def measure(objs):
    lo, hi = world_bbox(objs)
    return lo, hi, (hi - lo)


def flat_material(name, rgb, strength=1.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    em.inputs[1].default_value = strength
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs[0], out.inputs[0])
    return mat


def setup_world():
    world = bpy.data.worlds.new("W")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.085, 0.095, 0.105, 1.0)
    bg.inputs[1].default_value = 1.15


def add_lights():
    sun = bpy.data.lights.new("Sun", type="SUN")
    sun.energy = 3.4
    sun.angle = math.radians(14)
    o = bpy.data.objects.new("Sun", sun)
    bpy.context.collection.objects.link(o)
    o.rotation_euler = (math.radians(62), math.radians(4), math.radians(28))

    fill = bpy.data.lights.new("Fill", type="AREA")
    fill.energy = 420.0
    fill.size = 30.0
    f = bpy.data.objects.new("Fill", fill)
    bpy.context.collection.objects.link(f)
    f.location = (0.0, -8.0, 3.0)
    f.rotation_euler = (math.radians(90), 0, 0)


def add_text(body, loc, size=0.115, color=(0.92, 0.92, 0.88)):
    curve = bpy.data.curves.new(body, type="FONT")
    curve.body = body
    curve.size = size
    curve.align_x = "CENTER"
    o = bpy.data.objects.new("TXT_" + body, curve)
    bpy.context.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (math.radians(90), 0, 0)     # 正对 -Y（相机方向）
    o.data.materials.append(flat_material("T_" + body, color))
    return o


def add_bar(x0, x1, z, thickness, color, strength=1.0):
    """一条细横线（被当尺子用）。"""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=((x0 + x1) * 0.5, 0.0, z))
    b = bpy.context.active_object
    b.name = "Bar_%.2f" % z
    b.scale = (abs(x1 - x0), 0.012, thickness)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    b.data.materials.append(flat_material("BarMat_%.2f" % z, color, strength))
    return b


def pick_engine():
    avail = {e.identifier for e in
             bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        if name in avail:
            return name
    return "CYCLES"


def load_targets():
    with open(REPORT, encoding="utf-8") as fh:
        rep = json.load(fh)
    return {(r["id"], r["stage"]): r["height"] for r in rep["models"]}


# ════════════════════════════════════════════════════════════ 摆放 + 出图
def place(objs, x, k):
    """缩放 k、落地（zmin=0）、水平居中到 x。返回落地后的实测高度。"""
    parent = bpy.data.objects.new("holder", None)
    bpy.context.collection.objects.link(parent)
    for o in objs:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    parent.scale = (k, k, k)
    bpy.context.view_layer.update()
    lo, hi, _ = measure(objs)
    parent.location = (x - (lo.x + hi.x) * 0.5, -(lo.y + hi.y) * 0.5, -lo.z)
    bpy.context.view_layer.update()
    _, _, dims = measure(objs)
    return dims.z


def render(scene, out_path, width, height, bbox_lo, bbox_hi, margin=1.08):
    """正交正面机位，按内容包围盒取景（不浪费画布）。"""
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    center = (bbox_lo + bbox_hi) * 0.5
    span_x = max(1e-6, bbox_hi.x - bbox_lo.x)
    span_z = max(1e-6, bbox_hi.z - bbox_lo.z)
    aspect = width / float(height)
    cam_data.ortho_scale = max(span_x, span_z * aspect) * margin
    cam = bpy.data.objects.new("Cam", cam_data)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (center.x, -12.0, center.z)
    cam.rotation_euler = (math.radians(90), 0, 0)

    scene.render.engine = pick_engine()
    if scene.render.engine == "CYCLES":
        scene.cycles.samples = 48
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = out_path
    print("[LINEUP] 引擎=%s 分辨率=%dx%d 正交宽=%.2fm 取景中心=(%.2f, %.2f)"
          % (scene.render.engine, width, height, cam_data.ortho_scale,
             center.x, center.z))
    bpy.ops.render.render(write_still=True)
    print("[LINEUP] 写出 %s（%d 字节）" % (out_path, os.path.getsize(out_path)))


def main(argv):
    width, height = 2400, 900
    if "--width" in argv:
        width = int(argv[argv.index("--width") + 1])
    if "--height" in argv:
        height = int(argv[argv.index("--height") + 1])
    only = None
    if "--only" in argv:
        only = argv[argv.index("--only") + 1]

    targets = load_targets()

    # ── 1) 量一遍：实测高度 / 设计高度 必须是同一个常数 ──
    wipe()
    print("[LINEUP] 单位一致性（实测包围盒高度 ÷ 设计高度）")
    factors, axes = {}, set()
    for stage in ("young", "grown", "mature"):
        for pid in NEW_IDS:
            path = os.path.join(PLANT_DIR, pid + SUFFIX[stage] + ".fbx")
            if not os.path.exists(path):
                raise SystemExit("[LINEUP] 缺模型：%s" % path)
            objs = import_fbx(path)
            if not objs:
                raise SystemExit("[LINEUP] %s 导入后没有网格" % path)
            _, _, dims = measure(objs)
            axes.add("XYZ"[max(range(3), key=lambda i: dims[i])])
            f = dims.z / targets[(pid, stage)]
            factors[(pid, stage)] = f
            for o in objs:
                bpy.data.objects.remove(o, do_unlink=True)
    fs = list(factors.values())
    cmin, cmax = min(fs), max(fs)
    spread = (cmax - cmin) / cmin
    print("  系数区间 [%.5f, %.5f]｜相对离散 %.6f%%｜样本 %d"
          % (cmin, cmax, spread * 100, len(fs)))
    print("  最长轴集合 = %s%s" % (sorted(axes),
                                "（高度在 Z，Z-up 正确 ✓）" if axes == {"Z"} else " ✗"))
    if spread > 1e-3:
        bad = sorted(k for k, v in factors.items() if abs(v - cmin) / cmin > 1e-3)
        raise SystemExit("[LINEUP] 系数不一致 ✗ %s —— 这就是一株大一株小的成因" % bad)
    c = (cmin + cmax) * 0.5
    k = 1.0 / c
    print("  统一缩放 k = 1/%.5f = %.6f（全部模型共用，含既有品种）" % (c, k))

    # ── 2) 主图：8 株成熟体 + 3 株既有 ──
    figures = []
    if only in (None, "lineup"):
        wipe()
        setup_world()
        add_lights()
        col_w = 1.30
        cols = [(p, "mature", False) for p in NEW_IDS] + \
               [(p, "mature", True) for p in REF_IDS]
        n = len(cols)
        x0 = -(n - 1) * 0.5 * col_w
        heights = {}
        all_lo = Vector((1e9, 1e9, 1e9))
        all_hi = Vector((-1e9, -1e9, -1e9))
        for i, (pid, stage, is_ref) in enumerate(cols):
            path = os.path.join(PLANT_DIR, pid + SUFFIX[stage] + ".fbx")
            if not os.path.exists(path):
                print("[LINEUP] 参考株缺失，跳过：%s" % path)
                continue
            objs = import_fbx(path)
            x = x0 + i * col_w
            h = place(objs, x, k)
            heights[pid] = h
            lo, hi = world_bbox(objs)
            for ax in range(3):
                all_lo[ax] = min(all_lo[ax], lo[ax])
                all_hi[ax] = max(all_hi[ax], hi[ax])
            label = ("ref:" + pid) if is_ref else pid
            add_text(label, (x, 0.85, -0.16), size=0.115)
        # 尺子：1.0m / 1.7m
        span_l, span_r = x0 - col_w * 0.55, x0 + (n - 0.45) * col_w
        for z, col in ((1.0, (0.35, 0.55, 0.75)), (REF_BAND[0], (0.75, 0.45, 0.35))):
            add_bar(span_l, span_r, z, 0.010, col, 1.6)
            add_text("%.2fm" % z, (span_l - 0.30, 0.85, z), size=0.085, color=col)
        all_lo.z = min(all_lo.z, -0.24)
        all_hi.z = max(all_hi.z, REF_BAND[1] + 0.06)
        all_lo.x = span_l - 0.55
        all_hi.x = span_r
        out = os.path.join(OUT_DIR, "plant_lineup_8.png")
        os.makedirs(OUT_DIR, exist_ok=True)
        render(bpy.context.scene, out, width, height, all_lo, all_hi)
        figures.append(("lineup", heights, out))

    # ── 3) 生长阶梯：8 株 × 三阶段（同一列里按 X 错开，读得出"长大"）──
    if only in (None, "stages"):
        wipe()
        setup_world()
        add_lights()
        group_w = 1.85
        stage_dx = 0.40
        x0 = -(len(NEW_IDS) - 1) * 0.5 * group_w
        all_lo = Vector((1e9, 1e9, 1e9))
        all_hi = Vector((-1e9, -1e9, -1e9))
        hs = {}
        for i, pid in enumerate(NEW_IDS):
            for si, stage in enumerate(("young", "grown", "mature")):
                path = os.path.join(PLANT_DIR, pid + SUFFIX[stage] + ".fbx")
                objs = import_fbx(path)
                x = x0 + i * group_w + (si - 1) * stage_dx
                h = place(objs, x, k)
                hs[(pid, stage)] = h
                lo, hi = world_bbox(objs)
                for ax in range(3):
                    all_lo[ax] = min(all_lo[ax], lo[ax])
                    all_hi[ax] = max(all_hi[ax], hi[ax])
            add_text(pid, (x0 + i * group_w, 0.85, -0.16), size=0.105)
        all_lo.z = min(all_lo.z, -0.24)
        out = os.path.join(OUT_DIR, "plant_stages_8.png")
        render(bpy.context.scene, out, width, 1100, all_lo, all_hi)
        figures.append(("stages", hs, out))

    # ── 4) 读数：证明"没有一株大一株小" ──
    for tag, hs, out in figures:
        print("[LINEUP] %s 实测高度：" % tag)
        if tag == "lineup":
            new_m = [hs[p] for p in NEW_IDS if p in hs]
            refs = [hs[p] for p in REF_IDS if p in hs]
            print("  新 8 株成熟体  min=%.3f max=%.3f 极差=%.3f"
                  % (min(new_m), max(new_m), max(new_m) - min(new_m)))
            print("  既有参考株    min=%.3f max=%.3f"
                  % (min(refs), max(refs)))
            print("  既有成熟窄带  [%.2f, %.2f]" % REF_BAND)
            bad = [p for p in NEW_IDS if p in hs and
                   not (REF_BAND[0] - 0.06 <= hs[p] <= REF_BAND[1] + 0.06)]
            print("  8 株是否都落在既有窄带内：%s" % ("是 ✓" if not bad else "否 ✗ %s" % bad))
        else:
            for stage in ("young", "grown", "mature"):
                v = [hs[(p, stage)] for p in NEW_IDS]
                print("  %-6s min=%.3f max=%.3f 极差=%.3f"
                      % (stage, min(v), max(v), max(v) - min(v)))
    return 0


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    sys.exit(main(args))
