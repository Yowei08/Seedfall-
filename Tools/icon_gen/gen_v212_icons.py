#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Seedfall 守园者 —— ★ v212：**70 张 64×64 图标**（30 道具 / 8 植物 / 24 插件）★

为什么单独一个脚本
   既有的 `gen_item_png.py`（56 道具 + 6 机制植物，一个 id 一段 `i_xxx()`）与
   `gen_plugin_icons.py`（12 插件，深色圆角面板 + 单色符号）都**只认识自己那一批 id**。
   本轮新增 70 个 id ⇒ 新脚本，但**整套画法照抄它们**：
     · 道具 / 植物：`import gen_item_png as G`，用它的图元（rect/poly/disc/ring/ellipse/
       line/taper/bezier/drop）与三次后处理（fit_center → add_highlight → add_outline →
       add_slashes 或 add_corner_dots），所以**视觉语言完全一致**（同一个调色板、同样的
       2px 描边、同样的单条高光与角标）✓
     · 插件：照 `gen_plugin_icons.py` 的面板约定（`#1A1410` 圆角矩形、内缩 2px、radius 8）✓
   区别只有一处：**id 的登记表与配方在下面**，不动既有脚本一个字节 ✓

确定性
   没有随机数、没有时间戳、没有外部输入 ⇒ 同一份代码每次都出**逐字节相同**的 PNG ✓
   （`--check` 会把「现有的 PNG」与「重新画出来的 PNG」逐字节比，并顺带校验 64×64 / RGBA）

用法
   python Tools/icon_gen/gen_v212_icons.py            # 出图 + 出对照表 + 自校验
   python Tools/icon_gen/gen_v212_icons.py --check    # 只校验（不写 PNG）
   python Tools/icon_gen/gen_v212_icons.py --no-sheet # 只写 PNG
退出码：0 = 全绿；1 = 有缺图 / 尺寸不对 / 与重画结果不一致。
"""

import hashlib
import io
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import gen_item_png as G          # noqa: E402  道具/植物的图元与后处理（**同一个调色板**）

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
UI_DIR = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
OUT = os.path.join(HERE, "out")
ITEMS = os.path.join(ROOT, "Assets", "Resources", "Data", "items.json")

B1, B2, B3, HI, OUT_C = G.B1, G.B2, G.B3, G.HI, G.OUT_C
rect, poly, disc, ring, ellipse = G.rect, G.poly, G.disc, G.ring, G.ellipse
line, taper, bezier, drop = G.line, G.taper, G.bezier, G.drop

# ══════════════════════════════════════════════════════════════════════════
# 一、30 件新道具的图案（每个函数只画"主体"，描边/高光/角标由后处理统一加）
# ══════════════════════════════════════════════════════════════════════════
ITEM_ICONS = {}


def item(iid):
    def deco(fn):
        ITEM_ICONS[iid] = fn
        return fn
    return deco


@item("clay_trowel")
def _clay_trowel(cv, ac):
    taper(cv, 32, 34, 46, 50, 12, 8, B2)          # 短柄
    poly(cv, [(10, 12), (40, 8), (48, 26), (18, 34)], B1)   # 铲面
    rect(cv, 12, 14, 34, 18, ac)


@item("rain_gauge")
def _rain_gauge(cv, ac):
    rect(cv, 26, 8, 38, 54, B1)                   # 尺身
    for k in range(4):
        rect(cv, 28, 14 + k * 9, 44 - k * 2, 16 + k * 9, B3)   # 刻度
    rect(cv, 30, 22, 34, 26, ac)                  # 读数点


@item("powder_horn")
def _powder_horn(cv, ac):
    bezier(cv, (14, 48), (30, 6), (54, 26), 6, B1, 22)      # 牛角弯身
    bezier(cv, (14, 48), (30, 6), (54, 26), 2, B3, 22)
    disc(cv, 50, 30, 7, B2)                       # 口
    rect(cv, 20, 34, 26, 38, ac)


@item("straw_pad")
def _straw_pad(cv, ac):
    rect(cv, 10, 20, 54, 48, B1)                  # 垫面
    for k in range(4):
        line(cv, 10, 24 + k * 7, 54, 24 + k * 7, B3, 1)   # 编纹
    for k in range(4):
        line(cv, 14 + k * 11, 20, 14 + k * 11, 48, B2, 1)
    rect(cv, 12, 30, 52, 33, ac)


@item("bark_buckler")
def _bark_buckler(cv, ac):
    disc(cv, 32, 30, 20, B1)                      # 盾面
    disc(cv, 32, 30, 6, B3)                       # 盾心
    poly(cv, [(32, 10), (50, 22), (44, 46), (20, 46), (14, 22)], B2)  # 树皮拼片
    rect(cv, 26, 28, 38, 32, ac)


@item("plank_shoes")
def _plank_shoes(cv, ac):
    poly(cv, [(12, 38), (44, 38), (54, 46), (54, 52), (12, 52)], B1)  # 木底
    poly(cv, [(16, 20), (40, 15), (44, 38), (16, 38)], B2)            # 鞋面
    rect(cv, 14, 42, 52, 45, ac)


@item("wind_vane")
def _wind_vane(cv, ac):
    line(cv, 32, 10, 32, 52, B3, 3)               # 杆
    poly(cv, [(8, 18), (34, 12), (34, 24)], B1)   # 箭头
    poly(cv, [(34, 12), (34, 24), (52, 18)], B2)  # 尾羽
    disc(cv, 32, 26, 4, ac)
    rect(cv, 20, 52, 44, 56, B3)


@item("spark_bag")
def _spark_bag(cv, ac):
    poly(cv, [(18, 26), (46, 26), (52, 50), (12, 50)], B1)   # 袋身
    rect(cv, 24, 18, 40, 26, B2)                             # 袋口束带
    for k, (x, y) in enumerate([(16, 10), (32, 6), (48, 12)]):
        poly(cv, [(x, y), (x + 4, y + 8), (x - 4, y + 8)], ac)   # 三颗火星
    rect(cv, 22, 34, 42, 38, ac)


@item("yew_shaft")
def _yew_shaft(cv, ac):
    taper(cv, 16, 10, 48, 54, 13, 8, B1)          # 直柄
    rect(cv, 18, 20, 40, 24, B3)                  # 箍
    rect(cv, 22, 36, 44, 40, B3)
    poly(cv, [(8, 8), (22, 4), (24, 14), (10, 18)], B2)
    rect(cv, 20, 28, 26, 32, ac)


@item("brass_nozzle")
def _brass_nozzle(cv, ac):
    rect(cv, 22, 30, 44, 44, B1)                  # 管身
    poly(cv, [(44, 24), (56, 18), (56, 56), (44, 50)], B2)   # 喇叭口
    rect(cv, 12, 32, 22, 42, B3)                  # 尾接
    rect(cv, 26, 34, 40, 37, ac)


@item("cinder_scoop")
def _cinder_scoop(cv, ac):
    poly(cv, [(10, 26), (44, 24), (52, 48), (16, 50)], B1)   # 铲斗
    taper(cv, 44, 30, 56, 16, 8, 6, B2)                      # 柄
    for x, y in [(24, 34), (34, 40), (42, 32)]:
        disc(cv, x, y, 4, ac)                                # 灰里的火点
    rect(cv, 18, 44, 46, 47, B3)


@item("lucky_coal")
def _lucky_coal(cv, ac):
    poly(cv, [(16, 20), (44, 12), (54, 34), (40, 52), (18, 46)], B1)   # 煤块
    line(cv, 20, 30, 44, 24, ac, 2)                                    # 亮缝
    line(cv, 26, 42, 48, 34, ac, 1)
    rect(cv, 24, 34, 34, 38, B3)


@item("dodge_sash")
def _dodge_sash(cv, ac):
    bezier(cv, (10, 44), (28, 8), (54, 34), 5, B1, 24)      # 飘带
    bezier(cv, (10, 44), (28, 8), (54, 34), 2, B3, 24)
    for x, y in [(46, 14), (50, 20), (40, 10)]:
        line(cv, x, y, x + 6, y + 4, ac, 2)                  # 风线
    disc(cv, 12, 46, 5, B2)


@item("honey_heart")
def _honey_heart(cv, ac):
    poly(cv, [(32, 52), (10, 30), (14, 16), (26, 14), (32, 22),
              (38, 14), (50, 16), (54, 30)], B1)             # 心
    poly(cv, [(26, 44), (38, 44), (34, 56), (30, 56)], ac)   # 蜜滴
    disc(cv, 24, 26, 4, B3)


@item("thick_hide")
def _thick_hide(cv, ac):
    poly(cv, [(18, 12), (46, 12), (54, 26), (48, 52), (16, 52), (10, 26)], B1)  # 皮褂
    poly(cv, [(30, 12), (34, 12), (34, 22), (30, 22)], B3)                       # 领口
    line(cv, 32, 22, 32, 50, B3, 2)                                              # 门襟
    rect(cv, 14, 30, 50, 33, ac)


@item("brass_knuckle")
def _brass_knuckle(cv, ac):
    rect(cv, 10, 30, 54, 44, B1)                  # 指节横条
    for k in range(4):
        ring(cv, 16 + k * 11, 26, 7, 4, B2)       # 四个指环
    rect(cv, 12, 38, 52, 41, ac)


@item("beeswax_glove")
def _beeswax_glove(cv, ac):
    poly(cv, [(22, 10), (42, 10), (46, 34), (44, 50), (20, 50), (18, 34)], B1)  # 手背
    poly(cv, [(14, 34), (22, 34), (22, 52), (12, 52)], B2)                     # 拇指
    rect(cv, 18, 46, 46, 54, B3)                                               # 袖口
    rect(cv, 22, 24, 42, 27, ac)


@item("iron_tendon")
def _iron_tendon(cv, ac):
    for k in range(3):
        bezier(cv, (12, 20 + k * 10), (32, 8 + k * 10), (52, 20 + k * 10), 3, B1 if k % 2 == 0 else B2, 18)
    rect(cv, 8, 24, 14, 44, B3)                   # 束扣
    rect(cv, 50, 24, 56, 44, B3)
    rect(cv, 24, 28, 32, 32, ac)


@item("wide_pauldron")
def _wide_pauldron(cv, ac):
    poly(cv, [(8, 34), (20, 16), (44, 16), (56, 34), (48, 48), (16, 48)], B1)   # 甲片
    poly(cv, [(16, 30), (24, 20), (40, 20), (48, 30)], B2)                      # 上层
    rect(cv, 14, 38, 50, 41, ac)
    rect(cv, 22, 44, 42, 46, B3)


@item("peak_hood")
def _peak_hood(cv, ac):
    poly(cv, [(32, 8), (50, 34), (46, 52), (18, 52), (14, 34)], B1)             # 兜帽
    poly(cv, [(22, 34), (42, 34), (40, 50), (24, 50)], B3)                      # 帽里阴影
    rect(cv, 18, 30, 46, 34, ac)
    disc(cv, 32, 12, 3, B2)


@item("verdant_idol")
def _verdant_idol(cv, ac):
    rect(cv, 12, 48, 52, 56, B3)                  # 底座
    poly(cv, [(32, 10), (46, 30), (42, 48), (22, 48), (18, 30)], B1)            # 像身
    disc(cv, 32, 18, 7, B2)                       # 头
    rect(cv, 24, 34, 40, 38, ac)


@item("salted_cap")
def _salted_cap(cv, ac):
    poly(cv, [(16, 28), (32, 10), (48, 28), (48, 34), (16, 34)], B1)            # 帽冠
    rect(cv, 8, 34, 56, 42, B2)                   # 帽檐
    rect(cv, 12, 34, 52, 37, ac)                  # 盐霜环
    for x in (20, 30, 40, 48):
        disc(cv, x, 46, 2, B3)                    # 檐下的盐粒


@item("clover_badge")
def _clover_badge(cv, ac):
    disc(cv, 32, 32, 24, B1)                      # 徽章底
    ring(cv, 32, 32, 24, 20, B3)
    for k in range(3):
        a = -math.pi / 2 + k * 2 * math.pi / 3
        disc(cv, int(32 + 8 * math.cos(a)), int(32 + 8 * math.sin(a)), 7, ac)   # 三叶
    rect(cv, 31, 36, 33, 46, B2)                  # 叶柄


@item("amber_sickle")
def _amber_sickle(cv, ac):
    bezier(cv, (14, 12), (54, 14), (40, 48), 4, B1, 22)     # 镰刀弧
    taper(cv, 18, 20, 14, 48, 7, 6, B2)                     # 柄
    bezier(cv, (20, 14), (52, 16), (40, 44), 1, ac, 22)     # 刃口（品类色）
    disc(cv, 22, 24, 4, B3)


@item("tide_prism")
def _tide_prism(cv, ac):
    poly(cv, [(32, 8), (54, 48), (10, 48)], B1)   # 棱镜
    poly(cv, [(32, 8), (32, 48), (10, 48)], B2)   # 暗面
    line(cv, 32, 12, 32, 46, B3, 1)
    poly(cv, [(32, 20), (40, 40), (24, 40)], ac)  # 水光
    rect(cv, 14, 48, 50, 52, B3)


@item("soot_flask")
def _soot_flask(cv, ac):
    poly(cv, [(24, 24), (40, 24), (46, 50), (18, 50)], B1)   # 瓶身
    rect(cv, 27, 12, 37, 24, B2)                             # 瓶颈
    rect(cv, 25, 6, 39, 12, B3)                              # 塞
    bezier(cv, (32, 8), (40, -4), (50, 4), 2, ac, 16)        # 上升的烟
    rect(cv, 20, 34, 44, 38, ac)


@item("gale_cuff")
def _gale_cuff(cv, ac):
    ring(cv, 32, 34, 20, 12, B1)                  # 腕环
    ring(cv, 32, 34, 12, 8, B3)
    for k in range(3):
        line(cv, 10 + k * 4, 12 + k * 6, 26 + k * 4, 12 + k * 6, ac, 2)   # 风线
    rect(cv, 30, 52, 34, 56, B2)


@item("fortune_well")
def _fortune_well(cv, ac):
    rect(cv, 10, 34, 54, 56, B1)                  # 井口
    rect(cv, 14, 38, 50, 50, B3)                  # 井内
    rect(cv, 14, 16, 20, 34, B2)                  # 左柱
    rect(cv, 44, 16, 50, 34, B2)                  # 右柱
    rect(cv, 12, 10, 52, 16, B2)                  # 横梁
    line(cv, 32, 16, 32, 30, B3, 2)               # 绳
    rect(cv, 26, 26, 38, 34, ac)                  # 桶


@item("thunder_fist")
def _thunder_fist(cv, ac):
    poly(cv, [(16, 22), (46, 22), (52, 40), (44, 52), (18, 52), (10, 40)], B1)  # 拳套
    poly(cv, [(34, 8), (26, 26), (32, 26), (28, 40), (42, 20), (35, 20), (40, 8)], ac)  # 闪电
    rect(cv, 14, 44, 48, 47, B3)


@item("grudge_chain")
def _grudge_chain(cv, ac):
    ring(cv, 18, 20, 11, 6, B1)
    ring(cv, 32, 32, 11, 6, B2)
    ring(cv, 46, 44, 11, 6, B1)
    line(cv, 38, 38, 42, 42, ac, 2)               # 裂纹（怨气）
    line(cv, 24, 26, 27, 29, ac, 2)


# ══════════════════════════════════════════════════════════════════════════
# 二、8 株新植物的图案（金色系，和既有 22 张 `icon_plant_*` 同一套语言）
# ══════════════════════════════════════════════════════════════════════════
PLANT_ICONS = {}


def plant(iid):
    def deco(fn):
        PLANT_ICONS[iid] = fn
        return fn
    return deco


@plant("dusk_berry")
def _dusk_berry(cv, ac):
    line(cv, 32, 56, 32, 26, B2, 3)                            # 主枝
    for k, (x, y) in enumerate([(22, 20), (42, 24), (30, 14)]):
        line(cv, 32, 40 - k * 6, x, y + 6, B2, 2)              # 分枝
    for x, y in [(20, 18), (44, 22), (28, 12), (36, 30), (46, 38)]:
        disc(cv, x, y, 5, B1)                                  # 浆果
        disc(cv, x - 1, y - 2, 2, ac)


@plant("tide_gourd")
def _tide_gourd(cv, ac):
    disc(cv, 32, 42, 17, B1)                                   # 下球
    disc(cv, 32, 20, 11, B2)                                   # 上球
    rect(cv, 30, 6, 34, 12, B3)                                # 蒂
    ellipse(cv, 26, 44, 4, 6, ac)                              # 高光
    rect(cv, 18, 30, 46, 33, B3)                               # 束腰


@plant("hollow_vine")
def _hollow_vine(cv, ac):
    ring(cv, 32, 34, 22, 14, B1)                               # 拱起的空管
    ring(cv, 32, 34, 14, 12, B3)                               # 内壁
    rect(cv, 28, 34, 36, 56, B2)                               # 下延
    ellipse(cv, 20, 22, 5, 3, ac)


@plant("dune_cactus")
def _dune_cactus(cv, ac):
    rect(cv, 28, 26, 36, 56, B1)                               # 主干
    rect(cv, 18, 32, 28, 52, B2)                               # 左掌
    rect(cv, 36, 24, 46, 44, B2)                               # 右掌
    for k in range(6):
        x = 20 + (k % 3) * 10
        y = 20 + (k // 3) * 16
        line(cv, x, y, x + 3, y - 4, B3, 1)                    # 短刺
    rect(cv, 14, 52, 50, 56, ac)


@plant("harvest_bell")
def _harvest_bell(cv, ac):
    line(cv, 32, 56, 32, 12, B2, 3)                            # 主茎
    poly(cv, [(20, 20), (44, 20), (48, 40), (16, 40)], B1)     # 铃花
    ellipse(cv, 32, 40, 16, 3, B3)                             # 铃口
    disc(cv, 32, 46, 4, ac)                                    # 花蕊
    line(cv, 14, 12, 50, 12, B2, 2)


@plant("star_ladder")
def _star_ladder(cv, ac):
    line(cv, 32, 56, 32, 10, B2, 3)                            # 主轴
    for k, y in enumerate([16, 30, 44]):
        pts = []
        r = 14 - k * 2
        for j in range(10):
            a = -math.pi / 2 + j * math.pi / 5
            rr = r if j % 2 == 0 else r * 0.46
            pts.append((32 + rr * math.cos(a), y + rr * math.sin(a)))
        poly(cv, pts, B1 if k % 2 == 0 else ac)


@plant("elder_oak")
def _elder_oak(cv, ac):
    rect(cv, 24, 34, 40, 56, B3)                               # 粗矮树干
    disc(cv, 32, 22, 16, B1)                                   # 主冠
    disc(cv, 18, 28, 10, B2)                                   # 左冠
    disc(cv, 46, 28, 9, B2)                                    # 右冠
    ellipse(cv, 26, 18, 4, 3, ac)
    line(cv, 20, 52, 32, 48, B3, 2)                            # 露出的根


@plant("quick_reed")
def _quick_reed(cv, ac):
    for k, (x, top, tilt) in enumerate([(20, 10, -5), (28, 18, -2), (36, 14, 3), (44, 22, 6)]):
        poly(cv, [(x - 3, 54), (x + 3, 54), (x + tilt, top), (x + tilt - 2, top)], B1)
    rect(cv, 16, 52, 48, 56, B3)                               # 根部
    line(cv, 34, 20, 42, 20, ac, 2)


# ══════════════════════════════════════════════════════════════════════════
# 三、24 件插件的图案（深色圆角面板 + 单色符号，照 `gen_plugin_icons.py`）
# ══════════════════════════════════════════════════════════════════════════
PANEL_FILL = (26, 20, 16, 255)
PANEL_BOX = [2, 2, 61, 61]
PANEL_RADIUS = 8
SYM = (236, 224, 200, 255)          # 符号主色（暖白，沿用插件图标的单一色相）
SYM_HOT = (245, 128, 66, 255)       # 强调点（与"有代价"那一类道具同色）

PLUGIN_ICONS = {}


def plugin(pid):
    def deco(fn):
        PLUGIN_ICONS[pid] = fn
        return fn
    return deco


def _crescent(d, cx, cy, r, w=3):
    d.arc([cx - r, cy - r, cx + r, cy + r], 300, 60, fill=SYM, width=w)


@plugin("hoe_edgewave")
def _p_hoe_edgewave(d):
    _crescent(d, 30, 32, 20)
    d.line([38, 14, 52, 30], fill=SYM, width=3)
    d.ellipse([13, 14, 19, 20], fill=SYM_HOT)


@plugin("hoe_kindling")
def _p_hoe_kindling(d):
    d.polygon([(32, 10), (44, 30), (40, 48), (24, 48), (20, 30)], fill=SYM)
    d.polygon([(32, 26), (38, 36), (32, 46), (26, 36)], fill=PANEL_FILL)
    for x, y in [(14, 20), (50, 24), (46, 44)]:
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=SYM_HOT)


@plugin("hoe_splitcut")
def _p_hoe_splitcut(d):
    d.line([16, 46, 46, 16], fill=SYM, width=4)
    d.line([20, 50, 50, 20], fill=SYM, width=2)
    d.ellipse([40, 10, 46, 16], fill=SYM_HOT)


@plugin("hoe_leech_tip")
def _p_hoe_leech_tip(d):
    d.line([14, 48, 44, 18], fill=SYM, width=4)
    d.polygon([(44, 18), (52, 10), (48, 24)], fill=SYM)
    d.polygon([(30, 40), (36, 46), (30, 54), (24, 46)], fill=SYM_HOT)


@plugin("hoe_emberwave")
def _p_hoe_emberwave(d):
    _crescent(d, 28, 32, 18)
    for x, y in [(44, 16), (50, 26), (46, 44), (38, 52)]:
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=SYM_HOT)


@plugin("hoe_frostbite")
def _p_hoe_frostbite(d):
    for a in range(0, 360, 60):
        pass
    d.line([32, 12, 32, 52], fill=SYM, width=2)
    d.line([14, 22, 50, 42], fill=SYM, width=2)
    d.line([50, 22, 14, 42], fill=SYM, width=2)
    d.polygon([(46, 40), (52, 46), (46, 54), (40, 46)], fill=SYM_HOT)


@plugin("hoe_thrice")
def _p_hoe_thrice(d):
    for k, y in enumerate([18, 30, 42]):
        d.line([16, y + 6, 34, y - 6], fill=SYM, width=4)
    d.ellipse([44, 24, 52, 32], fill=SYM_HOT)


@plugin("hoe_bloodpact_blade")
def _p_hoe_bloodpact_blade(d):
    d.line([14, 46, 42, 18], fill=SYM, width=4)
    d.polygon([(42, 18), (52, 12), (46, 26)], fill=SYM)
    for x, y in [(30, 34), (24, 46)]:
        d.polygon([(x, y), (x + 5, y + 6), (x, y + 12), (x - 5, y + 6)], fill=SYM_HOT)


@plugin("can_sidejet")
def _p_can_sidejet(d):
    d.rectangle([26, 18, 38, 46], fill=SYM)
    for dy in (-6, 6):
        d.line([38, 32 + dy // 2, 52, 32 + dy], fill=SYM_HOT, width=3)


@plugin("can_frostjet")
def _p_can_frostjet(d):
    d.rectangle([20, 16, 32, 48], fill=SYM)
    d.line([34, 32, 52, 32], fill=SYM, width=3)
    d.line([44, 20, 44, 44], fill=SYM_HOT, width=2)
    d.line([36, 26, 52, 38], fill=SYM_HOT, width=2)


@plugin("can_widejet")
def _p_can_widejet(d):
    d.rectangle([16, 24, 30, 40], fill=SYM)
    d.rectangle([34, 16, 52, 48], fill=SYM_HOT)


@plugin("can_splash")
def _p_can_splash(d):
    d.rectangle([16, 26, 30, 38], fill=SYM)
    for a in (-24, 0, 24):
        x = 34 + int(14 * math.cos(math.radians(a)))
        y = 32 + int(14 * math.sin(math.radians(a)))
        d.line([34, 32, x, y], fill=SYM_HOT, width=3)


@plugin("can_trijet")
def _p_can_trijet(d):
    d.rectangle([14, 24, 26, 40], fill=SYM)
    for dy in (-10, 0, 10):
        d.line([26, 32, 52, 32 + dy], fill=SYM_HOT, width=3)


@plugin("can_tidecharge")
def _p_can_tidecharge(d):
    d.arc([14, 14, 50, 50], 40, 320, fill=SYM, width=3)
    d.polygon([(32, 22), (40, 34), (32, 44), (24, 34)], fill=SYM_HOT)


@plugin("can_gush")
def _p_can_gush(d):
    d.rectangle([12, 26, 22, 38], fill=SYM)
    for k in range(3):
        d.rectangle([26 + k * 4, 18 + k * 5, 52, 24 + k * 5], fill=SYM_HOT if k % 2 else SYM)


@plugin("can_glacier")
def _p_can_glacier(d):
    d.arc([12, 12, 52, 52], 0, 360, fill=SYM, width=3)
    d.line([32, 16, 32, 48], fill=SYM, width=2)
    d.line([16, 24, 48, 40], fill=SYM, width=2)
    d.line([48, 24, 16, 40], fill=SYM, width=2)
    d.ellipse([29, 29, 35, 35], fill=SYM_HOT)


@plugin("fert_split")
def _p_fert_split(d):
    d.ellipse([20, 20, 44, 44], fill=SYM)
    for x, y in [(12, 12), (52, 14), (44, 52)]:
        d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=SYM_HOT)


@plugin("fert_smoke")
def _p_fert_smoke(d):
    d.ellipse([20, 22, 44, 46], fill=SYM)
    for x, y, r in [(18, 18, 7), (34, 12, 8), (48, 20, 6), (46, 44, 6), (20, 46, 6)]:
        d.ellipse([x - r, y - r, x + r, y + r], fill=PANEL_FILL)
        d.arc([x - r, y - r, x + r, y + r], 0, 360, fill=SYM_HOT, width=2)


@plugin("fert_cheap")
def _p_fert_cheap(d):
    d.ellipse([18, 18, 46, 46], fill=SYM)
    d.line([12, 52, 52, 12], fill=SYM_HOT, width=3)


@plugin("fert_double")
def _p_fert_double(d):
    d.ellipse([14, 24, 36, 46], fill=SYM)
    d.ellipse([28, 14, 50, 36], fill=SYM_HOT)


@plugin("fert_shard")
def _p_fert_shard(d):
    d.ellipse([24, 24, 40, 40], fill=SYM)
    for k in range(5):
        a = -math.pi / 2 + k * 2 * math.pi / 5
        x0 = 32 + int(12 * math.cos(a))
        y0 = 32 + int(12 * math.sin(a))
        x1 = 32 + int(22 * math.cos(a))
        y1 = 32 + int(22 * math.sin(a))
        d.line([x0, y0, x1, y1], fill=SYM_HOT, width=3)


@plugin("fert_miasma")
def _p_fert_miasma(d):
    d.ellipse([16, 30, 48, 52], fill=SYM)
    for k in range(3):
        x = 22 + k * 10
        d.arc([x - 6, 8 + k * 3, x + 6, 28 + k * 3], 200, 340, fill=SYM_HOT, width=2)


@plugin("fert_cascade")
def _p_fert_cascade(d):
    for k, y in enumerate([14, 30, 46]):
        r = 9 - k
        d.ellipse([32 - r - 6, y - r, 32 + r - 6, y + r], fill=SYM if k % 2 == 0 else SYM_HOT)


@plugin("fert_plagueheart")
def _p_fert_plagueheart(d):
    d.polygon([(32, 50), (14, 32), (18, 18), (30, 18), (32, 26),
               (34, 18), (46, 18), (50, 32)], fill=SYM)
    d.ellipse([26, 28, 38, 40], fill=PANEL_FILL)
    d.arc([26, 28, 38, 40], 0, 360, fill=SYM_HOT, width=2)


# ══════════════════════════════════════════════════════════════════════════
# 四、出图
# ══════════════════════════════════════════════════════════════════════════
def rgba_items(items_table):
    """道具与植物：走 `gen_item_png` 的三段式后处理（与既有 62 张同一条路）。"""
    out = {}
    order = [it["id"] for it in items_table]
    for iid, fn in ITEM_ICONS.items():
        acc = G.ACC_COST if _cat2(items_table, iid) else G.ACC_GREEN
        cv = G.Cv()
        fn(cv, acc)
        G.fit_center(cv)
        G.add_highlight(cv, acc)
        G.add_outline(cv, acc)
        if iid in SLASH_IDS:
            G.add_slashes(cv, acc)
        else:
            G.add_corner_dots(cv, acc)
        out[("icon_item_", iid)] = G.to_rgba(cv)
    for pid, fn in PLANT_ICONS.items():
        cv = G.Cv()
        fn(cv, G.ACC_PLANT)
        G.fit_center(cv)
        G.add_highlight(cv, G.ACC_PLANT)
        G.add_outline(cv, G.ACC_PLANT)
        G.add_corner_dots(cv, G.ACC_PLANT)
        img = G.to_rgba(cv)
        out[("icon_plant_", pid)] = img
        out[("icon_item_", pid)] = img.copy()      # 种子卡走 icon_plant_*、道具卡走 icon_item_*
    assert set(order)  # 只用来证明表读得到（不参与出图）
    return out


# 有"动作性"的道具 → 左上 /// 三斜杠；其余 → 右下三颗角标（与既有表同一判据）
SLASH_IDS = {
    "clay_trowel", "yew_shaft", "brass_knuckle", "amber_sickle", "thunder_fist",
    "gale_cuff", "cinder_scoop", "gloom_blade", "brass_nozzle", "plank_shoes",
}


def _cat2(items_table, iid):
    for it in items_table:
        if it["id"] == iid:
            return int(it.get("category", 1)) == 2
    return False


def rgba_plugins():
    from PIL import Image, ImageDraw
    out = {}
    for pid, fn in PLUGIN_ICONS.items():
        img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        ImageDraw.Draw(img).rounded_rectangle(PANEL_BOX, radius=PANEL_RADIUS, fill=PANEL_FILL)
        fn(ImageDraw.Draw(img))
        out[("icon_plugin_", pid)] = img
    return out


def png_bytes(img):
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def sheet(images, path, cols=10):
    from PIL import Image, ImageDraw
    keys = sorted(images.keys())
    cell, pad = 64, 6
    rows = (len(keys) + cols - 1) // cols
    W = cols * (cell + pad) + pad
    H = rows * (cell + pad) + pad
    img = Image.new("RGBA", (W, H), (18, 16, 14, 255))
    d = ImageDraw.Draw(img)
    for i, k in enumerate(keys):
        cx = pad + (i % cols) * (cell + pad)
        cy = pad + (i // cols) * (cell + pad)
        img.alpha_composite(images[k].convert("RGBA"), (cx, cy))
    img.save(path)


def main(argv):
    check = "--check" in argv
    no_sheet = "--no-sheet" in argv
    if not (ITEM_ICONS or PLUGIN_ICONS):
        print(__doc__)
        return 2

    with io.open(ITEMS, encoding="utf-8-sig") as f:
        items_table = json.load(f)["items"]

    images = {}
    images.update(rgba_items(items_table))
    images.update(rgba_plugins())

    os.makedirs(UI_DIR, exist_ok=True)
    bad = []
    lines = []
    for (prefix, iid), img in sorted(images.items()):
        name = prefix + iid + ".png"
        path = os.path.join(UI_DIR, name)
        data = png_bytes(img)
        if check:
            if not os.path.isfile(path):
                bad.append(name + " 缺文件")
                continue
            with open(path, "rb") as f:
                old = f.read()
            if old != data:
                bad.append(name + " 与重画结果不一致（非确定性 / 被手改过）")
            continue
        with open(path, "wb") as f:
            f.write(data)
        lines.append("%-40s %dx%d %s" % (name, img.size[0], img.size[1],
                                         hashlib.sha256(data).hexdigest()[:16]))

    if not check and not no_sheet:
        os.makedirs(OUT, exist_ok=True)
        sheet(images, os.path.join(OUT, "v212_icons_sheet.png"))
        with io.open(os.path.join(OUT, "v212_icons_sheet.txt"), "w", encoding="utf-8") as f:
            f.write("v212 图标清单（%d 张，64x64 RGBA，sha256 前 16 位）\n" % len(images))
            f.write("\n".join(lines) + "\n")

    n_item = sum(1 for k in images if k[0] == "icon_item_")
    n_plant = sum(1 for k in images if k[0] == "icon_plant_")
    n_plug = sum(1 for k in images if k[0] == "icon_plugin_")
    print("v212 icons: item %d / plant %d / plugin %d = %d  (%s)"
          % (n_item, n_plant, n_plug, len(images), "check" if check else "written"))
    if check:
        if bad:
            for b in bad:
                print("  FAIL " + b.encode("ascii", "backslashreplace").decode("ascii"))
            return 1
        print("  OK all icons present and byte-identical")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
