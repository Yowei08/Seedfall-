# -*- coding: utf-8 -*-
"""
Seedfall 守园者 · UI 图标渲染
================================
把 3D 模型渲染成 UI 图标——种子卡上那格图案。

为什么要渲染而不是画：卡面上的图案必须和场上长出来的东西**长得一模一样**，
否则玩家在商店里认不出自己种的是什么。渲染保证了两者同源。

用正交投影 + 透明底：正交避免透视把近处放大，透明底才能贴到卡片上不带色块。

产出：Assets/Resources/Art/UI/icon_plant_<种子id>.png（128×128，RGBA）
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import seedfall_kit as kit       # noqa: E402
import gen_plants as gp          # noqa: E402

OUT_DIR = r"D:\Unity\project\Seedfall守园者\Assets\Resources\Art\UI"


def build(plant_id):
    c = dict(gp.PLANTS[plant_id])
    kit.reset()
    parts = gp.BUILDERS[c["kind"]](c)

    # 图标里不要底座：土丘与接地阴影是给场景用的（让模型"站"在地上），
    # 贴到卡片上只会变成一块占地方的褐色圆盘。
    parts = [p for p in parts if not p.name.startswith(("Soil", "Ground_Shadow"))]

    obj = kit.join(parts, "ICON_" + plant_id)
    kit.apply_transforms(obj)

    center, radius = kit.bounds(obj)
    path = os.path.join(OUT_DIR, "icon_plant_" + plant_id + ".png")
    # 透明底 + 正交正视 + 压低环境光：图标要的是"颜色清楚、轮廓完整、不带底色"
    kit.render_preview(path, center, radius, size=(128, 128),
                       angle=0.0, elev=8.0, ortho=True, transparent=True,
                       ambient=0.20, zoom=2.15, key_energy=3.2)
    print(f"[SEEDFALL-ICON] {plant_id} -> {path}")
    return path


def build_all():
    for pid in gp.PLANTS:
        build(pid)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args:
        print("用法: blender -b -P gen_icons.py -- all | <种子id...>")
    elif args[0] == "all":
        build_all()
    else:
        for pid in args:
            build(pid)
