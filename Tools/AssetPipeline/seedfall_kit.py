# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · 程序化建模库（Blender 5.x）
================================================
设计原则（对应 yowei-asset-pipeline 硬规则）：
  · 确定性：不使用随机数；同参数必得同模型
  · 可重建：产物全部可由「本脚本 + 参数表 + Blender 版本」重建
  · 不覆盖源：只写 Assets/Art/**，源参考图放 Tools/AssetPipeline/ref/
  · 契约对齐：材质名 = 调色板键名，Unity 侧按名字替换成卡通着色器

用法（在 Blender 进程内）：
    import sys; sys.path.insert(0, r"...\\Tools\\AssetPipeline")
    import seedfall_kit as kit
    kit.reset(); kit.box("a", (0,0,0), (1,1,1), "#FFFFFF")
"""

import os
import math
import bpy

# ---------------------------------------------------------------- 调色板
# 取自参考美术：宫崎骏/吉卜力风的暖色低饱和 + 明确的明暗两级
PALETTE = {
    # 玩家 · 种落小灵
    "Skin":      "#F7F1DF",
    "Cloth":     "#EFE6CE",
    "Hat":       "#E8C87A",
    "HatDark":   "#D9B968",
    "Scarf":     "#7CA85B",
    "Bag":       "#A97B4F",
    "Wood":      "#8C6239",
    "Metal":     "#C9CBCB",
    "Sprout":    "#8FBF5A",
    "Ink":       "#3A3226",
    # 小恶魔（基础近战）
    "ImpRed":    "#C0392B",
    "ImpDark":   "#8E2A22",
    "ImpHorn":   "#D9BE93",
    "Bone":      "#EFE6D2",
    # 小恶魔弓手（远程）
    "ImpPurple": "#6B4E8E",
    "ImpRobe":   "#5A4079",
    "Fire":      "#F2A03D",
    # 大恶魔（精英）
    "BruteSkin": "#8E3B34",
    "BruteRock": "#6E6A66",
    "BruteGlow": "#E8712C",
    # 植物 / 环境
    "Leaf":      "#5FA24A",
    "LeafDark":  "#3F7A33",
    "Petal":     "#E86A8A",
    "PetalB":    "#7FA8E0",
    "Fungus":    "#C25B5B",
    "Root":      "#7A5A3A",
    "Soil":      "#6B4A2F",
    "Stone":     "#8A8880",
    "Water":     "#66A8D8",
    "Gold":      "#E8C24A",
    "Purple":    "#9B6BC4",
}


# ---------------------------------------------------------------- 场景
def reset():
    """清空场景（保留世界设置），并丢弃材质与**动作**缓存。

    容易漏的一点：只删对象不删 bpy.data.actions，动画动作会在多次构建间累积。
    症状是导出的 FBX 里带着上一批动作（ArmAction / ArmAction.001 / .002 …），
    Unity 侧于是每个部件都生成一堆片段、时长还各不相同。
    """
    global _MAT_CACHE
    _MAT_CACHE = {}
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials, bpy.data.objects, bpy.data.actions):
        for item in list(block):
            if item.users == 0:
                block.remove(item)
    # 动作即使 users==0 也可能被 NLA 引用，这里强制清干净
    for act in list(bpy.data.actions):
        try:
            bpy.data.actions.remove(act)
        except Exception:
            pass


# ---------------------------------------------------------------- 材质
_MAT_CACHE = {}


def mat(color_hex, emission=0.0, rough=0.62):
    """
    按调色板键名或 #RRGGBB 取材质（带缓存，同名同色只建一次）。
    材质名就是调色板键名——Unity 侧按名字换成卡通着色器，这是两侧的契约。
    """
    key = color_hex if color_hex.startswith("#") else PALETTE.get(color_hex, "#CCCCCC")
    name = color_hex if not color_hex.startswith("#") else "C_" + color_hex.lstrip("#")
    cache_key = (name, round(emission, 3), round(rough, 3))
    if cache_key in _MAT_CACHE:
        return _MAT_CACHE[cache_key]

    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    rgb = _hex_to_rgb(key)
    if bsdf is not None:
        _set(bsdf, "Base Color", (*rgb, 1.0))
        _set(bsdf, "Roughness", rough)
        _set(bsdf, "Metallic", 0.0)
        _set(bsdf, "Specular IOR Level", 0.25)   # 4.x+ 名字；旧版没有就跳过
        _set(bsdf, "Specular", 0.25)
        if emission > 0.0:
            _set(bsdf, "Emission Color", (*rgb, 1.0))
            _set(bsdf, "Emission", (*rgb, 1.0))
            _set(bsdf, "Emission Strength", emission)
    # 视口底色，方便 Blender 里预览
    m.diffuse_color = (*rgb, 1.0)
    _MAT_CACHE[cache_key] = m
    return m


def _set(node, input_name, value):
    """按名字设置节点输入；名字不存在就静默跳过（跨 Blender 版本兼容）。"""
    sock = node.inputs.get(input_name)
    if sock is None:
        return False
    try:
        sock.default_value = value
        return True
    except Exception:
        return False


def _hex_to_rgb(h):
    h = (h or "#CCCCCC").lstrip("#")
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    return (int(h[0:2], 16) / 255.0, int(h[2:4], 16) / 255.0, int(h[4:6], 16) / 255.0)


def _apply(obj, color, emission, rough):
    obj.data.materials.clear()
    obj.data.materials.append(mat(color, emission, rough))
    return obj


# ---------------------------------------------------------------- 图元
def box(name, loc, size, color, rot=(0, 0, 0), smooth=False, emission=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = size
    o.rotation_euler = rot
    _apply(o, color, emission, 0.62)
    if smooth:
        bpy.ops.object.shade_smooth()
    return o


def sphere(name, loc, radius, color, scale=(1, 1, 1), segs=20, rings=12, rot=(0, 0, 0), emission=0.0):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=radius, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.scale = scale
    o.rotation_euler = rot
    _apply(o, color, emission, 0.62)
    bpy.ops.object.shade_smooth()
    return o


def cyl(name, loc, radius, depth, color, verts=16, rot=(0, 0, 0), scale=(1, 1, 1), smooth=True, emission=0.0):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=depth, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.rotation_euler = rot
    o.scale = scale
    _apply(o, color, emission, 0.62)
    if smooth:
        bpy.ops.object.shade_smooth()
    return o


def cone(name, loc, r1, r2, depth, color, verts=16, rot=(0, 0, 0), scale=(1, 1, 1), smooth=True, emission=0.0):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc)
    o = bpy.context.active_object
    o.name = name
    o.rotation_euler = rot
    o.scale = scale
    _apply(o, color, emission, 0.62)
    if smooth:
        bpy.ops.object.shade_smooth()
    return o


def mirror_x(objs, tag="L"):
    """把一串部件沿 X 镜像复制（左右对称的四肢）。返回镜像出来的新对象列表。"""
    out = []
    for src in objs:
        dup = src.copy()
        dup.data = src.data.copy()
        dup.name = src.name.replace("_R", "_" + tag) if "_R" in src.name else src.name + "_" + tag
        bpy.context.collection.objects.link(dup)
        dup.location = (-src.location.x, src.location.y, src.location.z)
        dup.rotation_euler = (src.rotation_euler.x, -src.rotation_euler.y, -src.rotation_euler.z)
        dup.scale = src.scale
        out.append(dup)
    return out


# ---------------------------------------------------------------- 组装与导出
def join(objs, name):
    """合并成一整个网格（保持多材质槽）。返回合并后的对象。"""
    objs = [o for o in objs if o is not None]
    if not objs:
        return None
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    merged = bpy.context.active_object
    merged.name = name
    return merged


def apply_transforms(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)


def set_origin(obj, at=(0.0, 0.0, 0.0)):
    """
    把物体原点移到世界坐标的指定点（默认脚底中心），顶点坐标相应重算。

    为什么必须做：join() 会以"第一个部件"作为合并目标，于是模型原点是那条腿的位置，
    不是脚底中心。原点和几何中心错位时，Unity 里任何旋转/缩放都会让模型跑偏——
    这一版就是因此让 180° 朝向修正把模型甩出了画面。
    """
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.context.scene.cursor.location = at
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.context.scene.cursor.location = (0.0, 0.0, 0.0)
    return obj


def ensure_dir(path):
    d = os.path.dirname(path)
    if d and not os.path.isdir(d):
        os.makedirs(d, exist_ok=True)


def export_fbx(obj, path, bake_yup=False):
    """
    导出单个对象为 FBX（Unity 原生支持，不依赖 glTFast）。

    ★ 轴向：交给导出器转，自己不要再烘一次 ★
    这里踩过一个绕了很久的坑：
      · 起初用 axis_up='Y'（导出器把几何转成 Y-up）——model 正常；
      · 后来为了"保险"，又在导出前自己把几何烘了一遍 -90°，
        于是**转了两遍**：FBX 里同时带着"已烘焙的 Y-up 网格"和"导出器的轴向转换节点"。
      · 症状很隐蔽：角色没事（游戏代码显式覆盖了它们的 rotation，把多余旋转抹掉了），
        但耕地/栅栏没有覆盖 rotation，于是躺倒 —— 表现就是"田地和模型缺失"。
    结论：轴向只在一处处理。bake_yup 保留为可选（默认 False），正常路径不要用它。
    """
    ensure_dir(path)
    if bake_yup:
        obj.rotation_euler = (math.radians(-90.0), 0.0, 0.0)
        apply_transforms(obj)

    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        object_types={'MESH'},
        mesh_smooth_type='FACE',
        path_mode='AUTO',
        embed_textures=False,
        bake_anim=False,
        axis_forward='-Z',      # Blender -Y（角色正面）→ Unity -Z
        axis_up='Y',            # Blender Z-up → Unity Y-up（导出器负责转换）
        use_space_transform=True,
    )
    return path


def tri_count(obj):
    """统计三角面数（预算用）。"""
    mesh = obj.data
    n = 0
    for p in mesh.polygons:
        n += max(1, len(p.vertices) - 2)
    return n


def bounds(obj):
    """返回对象世界坐标包围盒的（中心, 外接球半径）——用来给预览相机取景。"""
    import mathutils
    pts = [obj.matrix_world @ mathutils.Vector(c) for c in obj.bound_box]
    xs = [p.x for p in pts]
    ys = [p.y for p in pts]
    zs = [p.z for p in pts]
    center = ((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5, (min(zs) + max(zs)) * 0.5)
    radius = max(max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)) * 0.5
    return center, max(0.05, radius)


def render_preview(path, center=(0, 0, 0.5), radius=1.0, size=(720, 720),
                   angle=35.0, elev=14.0, dist_mul=2.35, ortho=False, transparent=False,
                   ambient=0.55, zoom=2.6, key_energy=2.4):
    """
    出预览图：相机**始终对准目标包围盒中心**（用 to_track_quat，不再手算欧拉角——
    上一版手算角度导致角色跑到画外）。固定灯光与配色，确定性。

    transparent=True 时输出透明底 PNG——UI 图标要的就是这个（贴到卡片上不能带底色方块）。
    """
    import mathutils
    ensure_dir(path)
    scene = bpy.context.scene
    engines = _engines()
    scene.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = bool(transparent)
    if transparent:
        # 透明底 + PNG RGBA，否则合成时会带上黑/灰底
        scene.render.image_settings.file_format = 'PNG'
        scene.render.image_settings.color_mode = 'RGBA'

    scene.world = scene.world or bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    bg = scene.world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.62, 0.66, 0.63, 1.0)   # 中灰底：暖色模型才读得出来
        if len(bg.inputs) > 1:
            # 环境光：预览用 0.55 看清结构；UI 图标要压低到 0.2 左右，否则颜色被冲淡
            bg.inputs[1].default_value = ambient

    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        bpy.context.collection.objects.link(cam)
    cam.data.type = 'ORTHO' if ortho else 'PERSP'
    # 取景：用**包围球半径**除以 tan(半视角)，保证整个模型一定进画面
    # （上一版拿"最大边长的一半"当距离，恶魔类的角/翼超出包围盒就直接被裁掉）
    half_fov = cam.data.angle * 0.5 if not ortho else 0.0
    if ortho:
        cam.data.ortho_scale = radius * zoom
        d = radius * 6.0
    else:
        d = radius / max(0.05, math.tan(half_fov)) * 1.18
    # 方位角/仰角：上面改过取景公式后这两个变量一度被漏掉，
    # 导致后续渲染全部 NameError —— 表现是"预览图不再产出"，很容易被忽略。
    a, e = math.radians(angle), math.radians(elev)
    target = mathutils.Vector(center)
    cam.location = target + mathutils.Vector((math.sin(a) * math.cos(e) * d,
                                              -math.cos(a) * math.cos(e) * d,
                                              math.sin(e) * d))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam

    key = bpy.data.objects.get("PreviewKey")
    if key is None:
        ld = bpy.data.lights.new("PreviewKey", type='SUN')
        key = bpy.data.objects.new("PreviewKey", ld)
        bpy.context.collection.objects.link(key)
    key.data.energy = key_energy
    key.rotation_euler = (math.radians(48), 0, math.radians(35))

    fill = bpy.data.objects.get("PreviewFill")
    if fill is None:
        fd = bpy.data.lights.new("PreviewFill", type='SUN')
        fill = bpy.data.objects.new("PreviewFill", fd)
        bpy.context.collection.objects.link(fill)
    fill.data.energy = 0.8
    fill.rotation_euler = (math.radians(70), 0, math.radians(-120))

    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def render_views(base_path, center, radius, views=(0.0, 90.0, 180.0), size=(480, 480), **kw):
    """按角度列表出一组视图（正面 / 侧面 / 背面），文件名 base_<angle>.png。"""
    out = []
    for ang in views:
        p = base_path.replace(".png", f"_{int(ang):03d}.png")
        out.append(render_preview(p, center, radius, size=size, angle=ang, **kw))
    return out


def _engines():
    try:
        return bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items.keys()
    except Exception:
        return []
