# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 把全部程序化模型汇总成一个可打开的 .blend 工程

■ 为什么需要这个脚本 ■
整条美术流水线是无头运行的（blender.exe -b --factory-startup -P gen_xxx.py）：
模型在内存里建好、导出 FBX、进程退出。**从头到尾没有保存过 .blend**。
于是"打开 Blender 看模型"这件事根本做不到——打开只会得到默认空场景。
（这不是资源没做，是产物形态的问题：FBX 在 Unity 那边好好的，只是没有 Blender 工程文件。）

本脚本把已生成的 FBX 全部导回来，按类别摆成几排、加上可读标签，
再存成一个 .blend。之后就可以直接双击打开、旋转查看、单独调某个模型。

■ 用法 ■
    blender.exe -b --factory-startup -P make_blend.py -- build    # 生成 .blend
    blender.exe -b -P make_blend.py -- verify                     # 打开 .blend 核对内容

产物：Docs/SeedfallModels.blend
"""

import os
import sys

import bpy
import mathutils

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.abspath(os.path.join(HERE, "..", ".."))
ART = os.path.join(PROJECT, "Assets", "Resources", "Art")
OUT_BLEND = os.path.join(PROJECT, "Docs", "SeedfallModels.blend")

# 类别 → (子目录, 横间距, 纵间距, 每排上限)
# 横向间距要留够：标签是按模型宽度写的，间距小于标签宽度就会糊成一片
CATEGORIES = [
    ("Characters", 2.4, 3.4, 6),
    ("Plants", 2.2, 3.0, 8),
    ("Environment", 3.4, 4.4, 9),
]


def wipe():
    """清空出厂场景：默认的 Cube / Camera / Light 不要，否则会和导入的模型混在一起。"""
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.objects, bpy.data.actions):
        for item in list(block):
            block.remove(item)


def label_at(text, x, y, z, size=0.34):
    """在模型上方放一个文字标签，打开就知道哪个是哪个。
    字号必须**明显小于横间距**，否则相邻标签会糊成一条（第一版就是这么翻车的）。"""
    bpy.ops.object.text_add(location=(x, y, z))
    ob = bpy.context.object
    ob.name = "LBL_" + text
    ob.data.body = text
    ob.data.size = size
    ob.data.align_x = 'CENTER'
    return ob


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def build():
    wipe()
    total = 0
    row_y = 0.0

    for cat, step_x, step_y, per_row in CATEGORIES:
        folder = os.path.join(ART, cat)
        if not os.path.isdir(folder):
            continue
        files = sorted(f for f in os.listdir(folder) if f.lower().endswith(".fbx"))
        # ★ 网格排布，不是一字排开 ★
        # 68 个模型排成一行会拉到 130m 长，俯瞰图里每个都只有几个像素。
        # 按类别给每排上限，排不下就换行——整体接近正方形，全景图才看得清。
        for i, fn in enumerate(files):
            path = os.path.join(folder, fn)
            name = os.path.splitext(fn)[0]
            try:
                objs = import_fbx(path)
            except Exception as e:
                print(f"[SEEDFALL-BLEND] 跳过 {fn}: {e}")
                continue
            col = i % per_row
            line = i // per_row
            x = col * step_x
            y = row_y - line * step_y
            for o in objs:
                if o.parent is None:
                    o.location.x += x
                    o.location.y += y
            label_at(name, x, y - 1.1, 1.5)
            total += 1
        lines = (len(files) + per_row - 1) // per_row
        print(f"[SEEDFALL-BLEND] {cat}: {len(files)} 个，{lines} 排，y 从 {row_y:.1f} 起")
        row_y -= (lines + 1.6) * step_y

    # 相机：先随便放，preview() 会按包围盒重新框一次
    bpy.ops.object.camera_add(location=(10, row_y * 0.5, 18))
    bpy.context.scene.camera = bpy.context.object

    bpy.ops.object.light_add(type='SUN', location=(10, -10, 25))
    bpy.context.object.data.energy = 3.5
    bpy.context.object.rotation_euler = (0.7, 0.2, 0.4)

    os.makedirs(os.path.dirname(OUT_BLEND), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
    print(f"[SEEDFALL-BLEND] 共导入 {total} 个模型 -> {OUT_BLEND}")


def verify():
    """重新打开 .blend，报出里面到底有什么——用来证明这个文件真的可用，不是空壳。"""
    if not os.path.exists(OUT_BLEND):
        print(f"[SEEDFALL-BLEND] 找不到 {OUT_BLEND}，先跑 build")
        return
    bpy.ops.wm.open_mainfile(filepath=OUT_BLEND)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    labels = [o for o in bpy.data.objects if o.type == 'FONT']
    acts = len(bpy.data.actions)
    tris = sum(len(o.data.polygons) for o in meshes)
    print(f"[SEEDFALL-BLEND] .blend 校验：网格 {len(meshes)} 个 / 标签 {len(labels)} 个 / "
          f"动作 {acts} 条 / 面数合计 {tris} / 材质 {len(bpy.data.materials)}")
    names = sorted(o.name for o in meshes)[:12]
    print("[SEEDFALL-BLEND] 前 12 个网格：" + ", ".join(names))


def preview():
    """从 .blend 渲一张俯瞰图，让人不用打开 Blender 也能看到里面有什么。"""
    if not os.path.exists(OUT_BLEND):
        print(f"[SEEDFALL-BLEND] 找不到 {OUT_BLEND}，先跑 build")
        return
    bpy.ops.wm.open_mainfile(filepath=OUT_BLEND)

    # 自动框住全部模型：算包围盒再反推机位，比手写坐标可靠
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if not meshes:
        print("[SEEDFALL-BLEND] 场景里没有网格")
        return
    xs, ys, zs = [], [], []
    for o in meshes:
        for corner in o.bound_box:
            w = o.matrix_world @ mathutils.Vector(corner)
            xs.append(w.x); ys.append(w.y); zs.append(w.z)
    cx = (min(xs) + max(xs)) * 0.5
    cy = (min(ys) + max(ys)) * 0.5
    cz = (min(zs) + max(zs)) * 0.5
    span = max(max(xs) - min(xs), max(ys) - min(ys), 1.0)

    cam = bpy.context.scene.camera
    target = mathutils.Vector((cx, cy, cz))
    # 俯角 55°：能看清顶面造型，又不至于把高度压没了
    cam.location = target + mathutils.Vector((span * 0.04, -span * 0.60, span * 0.80))
    # ★ 用 to_track_quat 算朝向，不要手写欧拉角 ★
    # 手写角度在第一版里把相机指向了地面外侧，68 个模型全挤在画面底边。
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = span * 1.10

    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in \
        [i.identifier for i in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x = 1920
    sc.render.resolution_y = 1080
    sc.render.filepath = os.path.join(PROJECT, "Docs", "captures", "blend_all_models.png")
    bpy.ops.render.render(write_still=True)
    print(f"[SEEDFALL-BLEND] 预览已输出 -> {sc.render.filepath}")


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    mode = args[0] if args else "build"
    if mode == "verify":
        verify()
    elif mode == "preview":
        preview()
    else:
        build()
