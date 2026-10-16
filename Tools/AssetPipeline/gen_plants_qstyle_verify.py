# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · Q 版植物三阶段自检器
================================================================================

这不是生成器的一部分 —— 它**从磁盘上的 .fbx 重新读回来**，独立量一遍，
再渲染剪影做客观比对。生成器自己的 assert 只能证明"我以为我造对了"。

## 量什么（对应需求的 6 条自检）

1. **回读验证**：高度在 Z 轴、zmin≈0、三角面数、高度是否落在阶段区间
2. **Q 版比例**：主体高度 ÷ 总高度（≥0.45），按"最大单件"与"主体+冠层"两种口径都报
3. **配色饱和度**：逐材质算 HSV 的 S，报平均值与最低值，<0.35 的点名
4. **剪影可区分**：归一化后两两 IoU，报最高的一对
5. **三阶段成长关系**：**按米制等比**渲染 16 组「幼苗/成长中/成熟」并排图
   （同一相机、同一像素密度 ⇒ 图中高度之比 = 真实高度之比，
     所以"长大"是**看**出来的，不是靠缩放摆出来的假象）
6. **对照图** → `Docs/captures/plants_qstyle_preview.png`，带中文名

## 为什么单独一个脚本（而不是塞进生成器）

生成器的 assert 与生成器共享同一套假设：假设错了，两边一起错。
本脚本改用**另一条路径**读数据（Blender 的 FBX 导入器 + `bound_box` 顶点变换），
所以它能抓到"生成器记账撒谎"这类问题 —— 事实上它就抓到过一次
（立方体没烘缩放，主体占比被算成 2.425）。

## 用法

    & $B -b --factory-startup -P gen_plants_qstyle_verify.py -- all
    & $B -b --factory-startup -P gen_plants_qstyle_verify.py -- all --no-render
    & $B -b --factory-startup -P gen_plants_qstyle_verify.py -- all --report Docs/plants_qstyle_verify.json
"""

import colorsys
import json
import math
import os
import sys
import warnings

# Blender 5.2 起 `Material.use_nodes` 会打 DeprecationWarning，
# 而 -b 模式下 warning 会把进程退出码变成 1 —— CI 里会误判成"自检失败"。
# 这是纯噪音（5.2 仍然是唯一正确写法），显式压掉，别让退出码撒谎。
warnings.filterwarnings("ignore", category=DeprecationWarning)

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy                # noqa: E402
import mathutils          # noqa: E402

import gen_plants_qstyle as q   # noqa: E402  ← 只读它的 SPECIES / STAGE 表（单一数据源）

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
PLANT_DIR = os.path.join(PROJECT, "Assets", "Resources", "Art", "Plants")
CAPTURE_DIR = os.path.join(PROJECT, "Docs", "captures")
RAW_DIR = os.path.join(HERE, "qstyle_renders")

SUFFIX = {"young": "_young", "grown": "_grown", "mature": ""}
STAGE_ORDER = ("young", "grown", "mature")
STAGE_CN = {"young": "幼苗", "grown": "成长中", "mature": "成熟"}

# 阶段区间（与生成器同一张表，但**独立抄一份**：抄错了会被区间断言抓到）
# 成熟体的区间取**工程既有**的 [1.62, 1.80]（来自 gen_plants_ally.py 的验收线），
# 不是本批 Q 版的 [1.68, 1.78] —— 因为成熟体这一批**没有重做**，它仍是旧文件，
# 本来就落在 1.6645~1.7900。若这里照抄 Q 版区间，会把"没重做"误报成"不合规"。
RANGES = {
    "young":  (0.35, 0.50, 500),
    "grown":  (0.95, 1.15, 900),
    "mature": (1.62, 1.80, 1500),
}
BODY_MIN = 0.45
SAT_MIN = 0.35

# 一律用"行/列"顺序（集合表示，渲染时归一化尺寸）
SIL = 512          # 剪影栅格边长
GRID_CELL = 260    # 并排图单元格宽
GRID_ROW_H = 300   # 并排图单元格高


# ================================================================ 读回一个 fbx
def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.objects,
                 bpy.data.images, bpy.data.cameras, bpy.data.lights):
        for it in list(coll):
            if it.users == 0:
                coll.remove(it)


def read_model(path):
    """把一个 fbx 读回来量尺寸/面数/材质。"""
    clear()
    bpy.ops.import_scene.fbx(filepath=path)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not objs:
        return None
    # 多件时取面数最多的那件做主件，但尺寸用**全体**的并集（导入的 FBX 只有一件）
    main = max(objs, key=lambda o: len(o.data.polygons))
    zs, xs, ys = [], [], []
    for o in objs:
        mw = o.matrix_world
        for v in o.data.vertices:
            p = mw @ v.co
            xs.append(p.x)
            ys.append(p.y)
            zs.append(p.z)
    tris = sum(max(1, len(p.vertices) - 2) for o in objs for p in o.data.polygons)
    mats = []
    for o in objs:
        for m in o.data.materials:
            if m is None or m.name in [x["name"] for x in mats]:
                continue
            s = None
            if m.use_nodes and m.node_tree:
                for n in m.node_tree.nodes:
                    if n.type == 'BSDF_PRINCIPLED':
                        c = n.inputs["Base Color"].default_value
                        s = colorsys.rgb_to_hsv(c[0], c[1], c[2])[1]
                        hexs = "#%02X%02X%02X" % (round(c[0] * 255), round(c[1] * 255),
                                                  round(c[2] * 255))
                        mats.append(dict(name=m.name, hex=hexs, s=round(s, 4)))
                        break
    real = [m["s"] for m in mats if m["s"] is not None]
    return dict(
        obj=main, objs=objs, tris=tris,
        dims=[round(max(xs) - min(xs), 4), round(max(ys) - min(ys), 4),
              round(max(zs) - min(zs), 4)],
        zmin=round(min(zs), 5), zmax=round(max(zs), 5),
        mats=mats,
        meanSat=round(sum(real) / len(real), 4) if real else 0.0,
        minSat=round(min(real), 4) if real else 0.0,
        minSatKey=(min(mats, key=lambda m: m["s"])["name"] if mats else "-"),
    )


# ================================================================ 渲染剪影
def pick_engine():
    """
    选渲染引擎。★ 不要硬写 'BLENDER_EEVEE_NEXT' ★
    实测：Blender 5.2.2 的枚举里根本没有这个名字，只有
        ('BLENDER_EEVEE', 'BLENDER_WORKBENCH', 'CYCLES')
    硬写会抛 "enum ... not found"，而这条异常发生在渲染阶段、
    前面的量测已经全部打印完毕 —— 很容易被当成"渲染只是慢"而漏掉。
    所以这里查枚举再挑，并且按"能出图"的顺序回退。
    """
    try:
        avail = [e.identifier for e in
                 bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    except Exception:                                     # noqa: BLE001
        avail = []
    for name in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "BLENDER_WORKBENCH", "CYCLES"):
        if name in avail:
            return name
    return avail[0] if avail else 'BLENDER_WORKBENCH'


def setup_render(px, py, ortho_scale, film_transparent=False):
    sc = bpy.context.scene
    sc.render.engine = pick_engine()
    sc.render.resolution_x, sc.render.resolution_y = px, py
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.image_settings.color_mode = 'RGBA' if film_transparent else 'RGB'
    sc.render.film_transparent = bool(film_transparent)
    sc.view_settings.view_transform = 'Standard'
    sc.render.fps = 24

    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (1.0, 1.0, 1.0, 1.0) if not film_transparent \
            else (1.0, 1.0, 1.0, 1.0)
        if len(bg.inputs) > 1:
            bg.inputs[1].default_value = 1.0

    cam = bpy.data.objects.get("VCam")
    if cam is None:
        cam = bpy.data.objects.new("VCam", bpy.data.cameras.new("VCam"))
        bpy.context.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ortho_scale
    cam.rotation_euler = (math.radians(90.0), 0.0, 0.0)     # 沿 -Y 看，正面
    sc.camera = cam
    return cam


def make_silhouette_mat():
    m = bpy.data.materials.get("V_SIL")
    if m is None:
        m = bpy.data.materials.new("V_SIL")
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (0.0, 0.0, 0.0, 1.0)
    em.inputs[1].default_value = 1.0
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m


def render_silhouette(path, out_png):
    """
    渲染剪影：相机固定在原点上方 0.5*H 处、正交高度 H、沿 -Y 看，
    所以**像素密度 = H / 图高** 对每个模型都相同 ⇒ 图里的大小就是真实大小。
    """
    r = read_model(path)
    if r is None:
        return None
    h = max(0.05, r["dims"][2])
    ymax = max(0.08, r["dims"][1])
    cam = setup_render(SIL, SIL, h * 1.08)
    cam.location = (0.0, -max(3.0, ymax * 4.0), h * 0.5)
    sil = make_silhouette_mat()
    for o in r["objs"]:
        o.data.materials.clear()
        o.data.materials.append(sil)
    bpy.context.scene.render.filepath = out_png
    bpy.ops.render.render(write_still=True)
    return r


# ================================================================ 剪影分析
# ★ 这里用 numpy，**不用 PIL** ★
# Blender 自带的 Python 里没有 Pillow（实测 ModuleNotFoundError: No module named 'PIL'），
# 但有 numpy。硬 import PIL 的结果是"48 张全渲染完、最后拼图那一步崩掉"。
# 所以掩码计算与 IoU 留在 Blender 侧（numpy），拼图交给普通 Python（compose_qstyle_sheets.py）。
def _load_mask(png_path):
    """读 PNG → 布尔掩码（非白像素算模型）。Blender 的 bpy.data.images 能直接读像素。"""
    import numpy as np
    img = bpy.data.images.load(png_path, check_existing=False)
    w, h = img.size
    buf = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(buf)
    rgba = buf.reshape(h, w, 4)
    alpha = rgba[:, :, 3]
    lum = rgba[:, :, 0] * 0.2126 + rgba[:, :, 1] * 0.7152 + rgba[:, :, 2] * 0.0722
    mask = (alpha > 0.5) & (lum < 0.92)
    bpy.data.images.remove(img)
    return mask


def fit_mask_box(mask, size=SIL):
    """
    把掩码按**自身包围盒**归一化到 size×size（保持长宽比居中）。
    这样 IoU 只比"形状"，不比"大小" —— 否则成熟体永远和成熟体最像、幼苗和幼苗最像，
    量出来的是体量而不是剪影。
    """
    import numpy as np
    ys, xs = np.nonzero(mask)
    if len(ys) == 0:
        return np.zeros((size, size), dtype=bool)
    sub = mask[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    h, w = sub.shape
    k = min(size / float(w), size / float(h))
    nw, nh = max(1, int(round(w * k))), max(1, int(round(h * k)))
    # 最近邻重采样（纯 numpy，避免依赖 PIL）
    yi = (np.arange(nh) * (h / float(nh))).astype(int).clip(0, h - 1)
    xi = (np.arange(nw) * (w / float(nw))).astype(int).clip(0, w - 1)
    small = sub[yi][:, xi]
    canvas = np.zeros((size, size), dtype=bool)
    oy, ox = (size - nh) // 2, (size - nw) // 2
    canvas[oy:oy + nh, ox:ox + nw] = small
    return canvas


def iou_matrix(norm_masks):
    import numpy as np
    keys = sorted(norm_masks)
    flat = np.stack([norm_masks[k].reshape(-1) for k in keys])
    inter = np.logical_and(flat[:, None, :], flat[None, :, :]).sum(axis=2).astype(float)
    union = np.logical_or(flat[:, None, :], flat[None, :, :]).sum(axis=2).astype(float)
    with np.errstate(invalid="ignore", divide="ignore"):
        iou = np.where(union > 0, inter / union, 0.0)
    return keys, iou


# ------------------------------------------------ 解析投影（毫秒级，先于渲染）
def project_mask(obj, res=128):
    """
    把网格顶点投到**正面平面（X-Z）**并栅格化成覆盖掩码 —— 不渲染。

    为什么要有这条：渲染那条路一次要一分钟，改一次设计就得等一轮。
    这条只读顶点（与渲染无关、确定性），几毫秒就能发现"两个品种塌成同一个球"
    这类问题，再去花渲染预算确认。两条路算的是同一件事，互相校验。

    口径说明：它**不是**精确剪影（顶点覆盖偏小），所以只用它做排序与预警，
    最终判据仍以渲染剪影的 IoU 为准 —— 报告里两者分开列。
    """
    import numpy as np
    mw = obj.matrix_world
    co = np.array([(mw @ v.co)[:] for v in obj.data.vertices])
    if len(co) < 3:
        return np.zeros((res, res), dtype=bool)
    x, z = co[:, 0], co[:, 2]
    w, h = x.max() - x.min(), z.max() - z.min()
    if w < 1e-6 or h < 1e-6:
        return np.zeros((res, res), dtype=bool)
    k = min(res / w, res / h)
    nw, nh = max(1, int(round(w * k))), max(1, int(round(h * k)))
    xi = ((x - x.min()) * k).astype(int).clip(0, nw - 1)
    zi = ((z - z.min()) * k).astype(int).clip(0, nh - 1)
    small = np.zeros((nh, nw), dtype=bool)
    small[zi, xi] = True
    canvas = np.zeros((res, res), dtype=bool)
    oy, ox = (res - nh) // 2, (res - nw) // 2
    canvas[oy:oy + nh, ox:ox + nw] = small
    return canvas


def project_aspect(obj):
    """正面投影的宽高比（宽/高）。品种差异最直观的一个数，报告里逐个列。"""
    mw = obj.matrix_world
    xs = [(mw @ v.co).x for v in obj.data.vertices]
    zs = [(mw @ v.co).z for v in obj.data.vertices]
    w = max(xs) - min(xs)
    h = max(zs) - min(zs)
    return round(w / h, 3) if h > 1e-6 else 0.0


# ================================================================ 主流程
def main(ids, do_render=True, report=None):
    os.makedirs(RAW_DIR, exist_ok=True)
    os.makedirs(CAPTURE_DIR, exist_ok=True)

    # ---- ① 回读全部 16×3 ----
    rows = []
    missing = []
    for pid in ids:
        cn, form = q.SPECIES[pid][0], q.SPECIES[pid][1]
        for st in STAGE_ORDER:
            p = os.path.join(PLANT_DIR, pid + SUFFIX[st] + ".fbx")
            if not os.path.isfile(p):
                missing.append(os.path.basename(p))
                continue
            r = read_model(p)
            if r is None:
                missing.append(os.path.basename(p) + " (empty)")
                continue
            lo, hi, budget = RANGES[st]
            rows.append(dict(
                id=pid, stage=st, nameCN=cn, form=form,
                file=os.path.basename(p),
                height=r["dims"][2], dimX=r["dims"][0], dimY=r["dims"][1],
                aspect=project_aspect(r["obj"]),
                projMask=project_mask(r["obj"]),
                zmin=r["zmin"], zmax=r["zmax"], tris=r["tris"],
                budget=budget, hRange=[lo, hi],
                hOK=(lo - 0.005 <= r["dims"][2] <= hi + 0.005),
                triOK=r["tris"] < budget,
                zminOK=abs(r["zmin"]) < 1e-3,
                mats=[m["name"] for m in r["mats"]],
                matSats={m["name"]: m["s"] for m in r["mats"]},
                meanSat=r["meanSat"], minSat=r["minSat"], minSatKey=r["minSatKey"],
            ))
            print("[QVERIFY] %-30s %-6s h=%.4f zmin=%+.4f tris=%4d/%4d meanSat=%.3f "
                  "minSat=%.3f(%s) %s%s%s"
                  % (os.path.basename(p), st, r["dims"][2], r["zmin"], r["tris"], budget,
                     r["meanSat"], r["minSat"], r["minSatKey"],
                     "H-OK" if (lo - 0.005 <= r["dims"][2] <= hi + 0.005) else "H-BAD",
                     " TRI-OK" if r["tris"] < budget else " TRI-BAD",
                     " Z-OK" if abs(r["zmin"]) < 1e-3 else " Z-BAD"))

    print("[QVERIFY] 回读 %d 个模型，缺失/空 %d 个" % (len(rows), len(missing)))
    for st in STAGE_ORDER:
        sub = [r for r in rows if r["stage"] == st]
        if not sub:
            continue
        bad = [r["file"] for r in sub if not (r["hOK"] and r["triOK"] and r["zminOK"])]
        print("[QVERIFY]   %-6s 高度[%.4f, %.4f] 区间%s  面数[%d, %d]  不合规: %s"
              % (st, min(r["height"] for r in sub), max(r["height"] for r in sub),
                 RANGES[st][:2], min(r["tris"] for r in sub), max(r["tris"] for r in sub),
                 ", ".join(bad) or "无"))
    lowsat = [r for r in rows if r["minSat"] < SAT_MIN]
    print("[QVERIFY]   平均饱和度范围 [%.3f, %.3f]；最低材质 <%.2f 的: %s"
          % (min(r["meanSat"] for r in rows), max(r["meanSat"] for r in rows), SAT_MIN,
             ", ".join("%s/%s(%s=%.3f)" % (r["id"], r["stage"], r["minSatKey"], r["minSat"])
                       for r in lowsat) or "无"))

    # ---- 三阶段成长关系（数值口径）----
    growth = []
    for pid in ids:
        trip = {r["stage"]: r for r in rows if r["id"] == pid}
        if len(trip) != 3:
            continue
        young, grown, mature = trip["young"], trip["grown"], trip["mature"]
        mono = young["height"] < grown["height"] < mature["height"]
        growth.append(dict(
            id=pid, nameCN=young["nameCN"],
            youngHeight=young["height"], grownHeight=grown["height"],
            matureHeight=mature["height"],
            ratioYG=round(grown["height"] / young["height"], 3),
            ratioGM=round(mature["height"] / grown["height"], 3),
            monotonic=mono,
        ))
    bad_growth = [g["id"] for g in growth if not g["monotonic"]]
    print("[QVERIFY] 成长关系: %d/%d 个品种三阶段高度单调递增；不单调: %s"
          % (len(growth) - len(bad_growth), len(growth), ", ".join(bad_growth) or "无"))
    print("[QVERIFY]   成长中/幼苗 = %.2f~%.2f 倍；成熟/成长中 = %.2f~%.2f 倍"
          % (min(g["ratioYG"] for g in growth), max(g["ratioYG"] for g in growth),
             min(g["ratioGM"] for g in growth), max(g["ratioGM"] for g in growth)))

    iou_top = None

    # ---- 解析投影 IoU（毫秒级，不依赖渲染；先给出预警）----
    pm = {"%s/%s" % (r["id"], r["stage"]): r["projMask"] for r in rows}
    pkeys, piou = iou_matrix(pm)
    ppairs = sorted(((float(piou[i, j]), pkeys[i], pkeys[j])
                     for i in range(len(pkeys)) for j in range(i + 1, len(pkeys))),
                    reverse=True)
    psame = [x for x in ppairs if x[1].split("/")[1] == x[2].split("/")[1]
             and x[1].split("/")[0] != x[2].split("/")[0]]
    psameid = [x for x in ppairs if x[1].split("/")[0] == x[2].split("/")[0]
               and x[1].split("/")[1] != x[2].split("/")[1]]
    print("[QVERIFY] 解析投影（顶点栅格，非渲染；口径偏小而保守）:")
    print("[QVERIFY]   同阶段不同品种最高 5 对: " +
          ("; ".join("%.3f %s|%s" % (v, a, b) for v, a, b in psame[:5]) or "无"))
    print("[QVERIFY]   跨阶段同品种最高 3 对: " +
          ("; ".join("%.3f %s|%s" % (v, a, b) for v, a, b in psameid[:3]) or "无"))
    asp = {}
    for r in rows:
        asp.setdefault(r["stage"], []).append((r["aspect"], r["id"]))
    for st in STAGE_ORDER:
        if st in asp:
            v = sorted(asp[st])
            print("[QVERIFY]   %-6s 宽高比 [%.2f, %.2f]（最窄 %s / 最宽 %s）"
                  % (st, v[0][0], v[-1][0], v[0][1], v[-1][1]))

    if not do_render:
        summary = dict(models=[{k: v for k, v in r.items() if k != "projMask"}
                              for r in rows], growth=growth, missing=missing,
                       projIoU=dict(sameStageTop=psame[:8], sameIdTop=psameid[:8]))
        return summary

    # ---- ② 渲染剪影 ----
    pngs = {}
    for pid in ids:
        for st in STAGE_ORDER:
            p = os.path.join(PLANT_DIR, pid + SUFFIX[st] + ".fbx")
            if not os.path.isfile(p):
                continue
            out = os.path.join(RAW_DIR, "%s_%s.png" % (pid, st))
            render_silhouette(p, out)
            pngs["%s/%s" % (pid, st)] = out
    print("[QVERIFY] 剪影渲染完成: %d 张 -> %s" % (len(pngs), RAW_DIR))

    # ---- ③ 剪影 IoU（归一化后两两）----
    masks = {k: _load_mask(p) for k, p in pngs.items()}
    norm = {k: fit_mask_box(m) for k, m in masks.items()}
    keys, iou = iou_matrix(norm)
    pairs = []
    for i in range(len(keys)):
        for j in range(i + 1, len(keys)):
            pairs.append((float(iou[i, j]), keys[i], keys[j]))
    pairs.sort(reverse=True)
    # 同一品种跨阶段的"长得像"是**期望**（同一个体），不算撞脸；
    # 所以判据分两类：同阶段不同品种（必须不像）与跨阶段同品种（应该像）。
    same_stage = [p for p in pairs if p[1].split("/")[1] == p[2].split("/")[1]]
    cross_stage = [p for p in pairs if p[1].split("/")[1] != p[2].split("/")[1]]
    same_id = [p for p in cross_stage if p[1].split("/")[0] == p[2].split("/")[0]]
    print("[QVERIFY] 剪影 IoU（按自身包围盒归一化后两两，共 %d 对）" % len(pairs))
    print("[QVERIFY]   ① 同阶段内、不同品种的最高 5 对（撞脸指标，越低越好，判据 <0.74）:")
    for v, a, b in same_stage[:5]:
        if a.split("/")[0] == b.split("/")[0]:
            continue
        print("[QVERIFY]      %.3f  %s  vs  %s" % (v, a, b))
    print("[QVERIFY]   ② 跨阶段、同一品种的最高 5 对（成长连续性，越高越说明是同一个体）:")
    for v, a, b in same_id[:5]:
        print("[QVERIFY]      %.3f  %s  vs  %s" % (v, a, b))
    cross_diff_id = [p for p in cross_stage if p[1].split("/")[0] != p[2].split("/")[0]]
    print("[QVERIFY]   ③ 跨阶段、不同品种的最高 3 对:")
    for v, a, b in cross_diff_id[:3]:
        print("[QVERIFY]      %.3f  %s  vs  %s" % (v, a, b))
    iou_top = dict(sameStageTop=same_stage[:8], sameIdCrossStageTop=same_id[:8],
                   crossStageMax=same_id[0][0] if same_id else 0.0,
                   sameStageMaxNextBest=same_stage[1][0] if len(same_stage) > 1 else 0.0)

    # ---- ④ 并排对照图（渲染已就绪，交给外层 Python 拼）----
    plan = dict(
        ids=ids,
        names={r["id"]: r["nameCN"] for r in rows},
        forms={r["id"]: r["form"] for r in rows},
        heights={"%s/%s" % (r["id"], r["stage"]): r["height"] for r in rows},
        pngs=pngs,
        outDir=CAPTURE_DIR,
        labelWidth=200, cellWidth=260, rowHeight=300, perSheet=4,
    )
    plan_path = os.path.join(RAW_DIR, "compose_plan.json")
    with open(plan_path, "w", encoding="utf-8") as fh:
        json.dump(plan, fh, ensure_ascii=False, indent=1)
    print("[QVERIFY] 拼图计划 -> %s" % plan_path)
    print("[QVERIFY] 下一步（Blender 里没有 Pillow，必须用 Harness 的 Python）:")
    print("[QVERIFY]   python Tools/AssetPipeline/compose_qstyle_sheets.py \"%s\"" % plan_path)

    summary = dict(models=[{k: v for k, v in r.items() if k != "projMask"}
                           for r in rows],
                   growth=growth, missing=missing, iou=iou_top,
                   projIoU=dict(sameStageTop=psame[:8], sameIdTop=psameid[:8]),
                   composeePlan=plan_path)
    if report:
        os.makedirs(os.path.dirname(report), exist_ok=True)
        with open(report, "w", encoding="utf-8") as fh:
            json.dump(summary, fh, ensure_ascii=False, indent=1)
        print("[QVERIFY] report -> " + report)
    return summary


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args or args[0] in ("-h", "--help"):
        print("用法: blender -b --factory-startup -P gen_plants_qstyle_verify.py -- "
              "all | <id...> [--no-render] [--report out.json]")
        print("  品种:", ", ".join(q.SPECIES))
        sys.exit(0)
    report = None
    if "--report" in args:
        i = args.index("--report")
        report = args[i + 1]
        args = args[:i] + args[i + 2:]
    do_render = "--no-render" not in args
    args = [a for a in args if a != "--no-render"]
    ids = list(q.SPECIES.keys()) if (args and args[0] == "all") else args
    main(ids, do_render=do_render, report=report)
