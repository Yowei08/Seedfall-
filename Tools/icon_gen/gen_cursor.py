#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Seedfall **鼠标光标**生成器（v203）：园丁小铲，三态 —— 默认 / 悬停 / 按下。

★ 为什么是"小园艺铲"★
  三把武器是**锄头 / 水壶 / 肥料**（见 `WeaponSystem` / `TableDatabase`），整局的动作是
  "刨地、浇水、施肥、收割"⇒ 光标就是玩家手里那把**刨地的小铲子** ✓
  它同时满足"贴园丁"与"能被认出来"两条：铲口是**尖的**（有明确指向）、木柄是**斜的**
  （与系统箭头的观测习惯一致：热点在左上、图形向右下展开）✓
  ⚠️ 为什么不是"小幼苗"（用户给的另一条路）：本工程的调色板是**三档灰 + 一个绿点缀**，
     它天生适合"金属 + 木头"，不适合"叶子"（叶子只有一种绿可画 ⇒ 会糊成一块色）。
     图标生成器里 62 张图也印证了这一点：**植物一律用灰白当主体、绿只做点缀** ✓

★ 三态（只有这三张图，热点**只有一份**）★
  cursor_default  刃口一线高光 + 箍圈 + 暗木柄 + 绿色握把环（默认）
  cursor_hover    高光加厚加长、箍圈与球头**整条发光**、刃外一颗**绿星芒** + 两粒火星
  cursor_press    与 default **逐像素相同**，整体下移 **2px**（= "铲子压下去"，热点不动）

  ★ 为什么按下只下移、不换造型 ★
    光标是要"能按"的：热点一变，点下去的那一刻指针就跳一格 ⇒ 比"没反馈"更糟 ✗
    所以三态**同一把铲子、同一个热点**，按下只把**像素**下沉 2px ✓
    这也是 `Cursor.SetCursor` 的硬约束：热点是**像素坐标**，只有一份 ✓

★ 比例是这张图的全部难点（四版才定下来，每版都是看完 4× 对照图才改的）★
    第 1 版 刃半宽 4.4 / 柄 3.3              → 一根**斜棍** ✗
    第 2 版 刃半宽 6.4、从尖到根平滑收放       → 一个**枣核 / 蜡笔头** ✗
    第 3 版 V 形尖 + 平行边 + 球头            → **马桶搋子**（头与柄一样长）✗
    第 4 版（本版）**刃:柄 ≈ 2:1**            ✓
      · 刃 u 0→28（尖端 7px 内张开、中间 19px 是**平行边**、尾端收根）
      · 箍 u 28→30.5（比刃根窄 ⇒ 有一道"脖子"）
      · 柄 u 30.5→38（更窄）+ 球头 u 39.3 r4
      · ⇒ 最宽处 14px（刃）／柄 5.2px／球头 8px ⇒ 轮廓是"宽头 → 细颈 → 小球"
    ★ 判据是"头部与柄的长度比"，不是"看起来像不像"：第 3 版那个 1:1 的比例，
      无论怎么描边、怎么打光都会读成"头 + 杆"的别的东西 ✓（这一条写在这里，免得下次又踩）

★ 统一规范（与 `gen_item_png.py` 的图标**同一套**，脚本尾部 `verify()` 逐条机器校验）★
  1. 画布 **48×48**、透明底。为什么不是 32：`CursorMode.Auto` 在高 DPI 下按系统缩放取图，
     32 会被 OS 拉大 ⇒ 硬边像素被**插值糊掉**；48 给 OS 留了余量 ✓
  2. 调色板**≤ 6 色**：描边 OUT #3A3A38 / 高光 HI #F2EFE6 / 主体 B1 #C9C6BE B2 #8E8B83
     B3 #5D5B56 / 点缀 ACC 绿 #8CE891 —— 五个颜色**直接 import 自 `gen_item_png`**
     （不复制常量：复制一份就有第二份真相，改一处忘一处是这类脚本最常见的坏法）
  3. 无渐变 / 无抗锯齿 / 硬边：alpha 只有 **0 或 255**
  4. 2px 暖灰描边 OUT（欧氏膨胀 2px ⇒ 角自然圆）
  5. 主体包围盒：下界 33px（≈69%）、上界 46px。为什么不是图标那条 "≥44/64 = 69%" 的 80%：
     光标**不能太大** —— 它盖在游戏画面上，太大就挡视野；而图标是躺在格子里的。
     69%~85% 是光标的合理区间（真实感来自"尖端贴着热点"，不是"占满画布"）✓
     ⚠️ 另有一条硬条件：**包围盒下缘 + 2px 不能超画布**（否则按下态会被裁掉）
  6. 确定性：无随机、无时间戳 ⇒ 同一份代码每次都出**逐字节相同**的 PNG

★ 输出 ★
  Assets/Resources/Art/UI/cursor_default.png
  Assets/Resources/Art/UI/cursor_hover.png
  Assets/Resources/Art/UI/cursor_press.png
  Tools/icon_gen/out/cursor_states.png    4× 放大对照图（带热点十字）
  Tools/icon_gen/out/cursor_states.txt    清单 + 热点推导

用法：
  python Tools/icon_gen/gen_cursor.py             # 出图 + 对照图 + 校验
  python Tools/icon_gen/gen_cursor.py --check     # 只校验（不出图）
  python Tools/icon_gen/gen_cursor.py --no-sheet  # 只写 PNG
"""

import io
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
UI_DIR = os.path.join(ROOT, "Assets", "Resources", "Art", "UI")
OUT = os.path.join(HERE, "out")

sys.path.insert(0, HERE)
# 调色板只从图标生成器取 —— 光标与图标必须是**同一套颜色**（用户要求"与图标同一套"）
from gen_item_png import OUT_C, HI, B1, B2, B3, ACC_GREEN  # noqa: E402

W = H = 48

# ── 工具轴：铲尖在左上，柄尾在右下（45°）──────────────────────────────────
# 热点就是**铲尖那个像素**（见 `verify()`：它等于整张图最靠前的不透明像素）
TIP_X, TIP_Y = 6.0, 6.0
COS45 = 0.7071067811865476

PRESS_DY = 2          # 按下：整张图下移 2px（热点不动）

# 刀刃沿轴的半宽曲线（u = 沿轴距离，单位像素）
# ★ 刃:柄 ≈ 2:1 是这个形状能被读成"铲"的唯一原因（见文件头"四版"那段）
BLADE_U = (0.0, 7.0, 12.0, 19.0, 25.0, 28.0)
BLADE_HW = (0.5, 5.4, 7.0, 7.0, 5.6, 3.4)

FERRULE_U0, FERRULE_U1, FERRULE_HW = 28.0, 30.5, 3.0
HANDLE_U0, HANDLE_U1, HANDLE_HW = 30.5, 38.0, 2.6
GRIP_U0, GRIP_U1 = 31.2, 34.4          # 绿色握把环（贴在箍后面）
KNOB_U, KNOB_R = 39.3, 4.0             # 柄尾球头


def uv(x, y):
    """像素中心 → 工具轴局部坐标 (u 沿轴, v 垂直轴; v>0 = 右上侧)。

    轴是 45° ⇒ 这是一次**正交变换**，u/v 都是等距的像素单位
    （所以 `in_knob` 里的圆在屏幕上还是圆；半宽也是真像素，不是斜着量的长度）✓
    """
    dx = x + 0.5 - TIP_X
    dy = y + 0.5 - TIP_Y
    return ((dx + dy) / (2.0 * COS45), (dx - dy) / (2.0 * COS45))


def blade_hw(u):
    """刀刃半宽：在 BLADE_U 上线性插值（轴是 45°，插值只是取宽度，不影响硬边）。"""
    if u < BLADE_U[0] or u > BLADE_U[-1]:
        return 0.0
    for i in range(len(BLADE_U) - 1):
        a, b = BLADE_U[i], BLADE_U[i + 1]
        if a <= u <= b:
            t = (u - a) / (b - a)
            return BLADE_HW[i] + (BLADE_HW[i + 1] - BLADE_HW[i]) * t
    return 0.0


def handle_hw(u):
    """柄半宽：平直的一段（尾巴交给球头 `KNOB_*`）。"""
    return HANDLE_HW if HANDLE_U0 <= u <= HANDLE_U1 else 0.0


def in_knob(u, v):
    du = u - KNOB_U
    return du * du + v * v <= KNOB_R * KNOB_R


def body_zone(x, y):
    """返回 ('blade'|'ferrule'|'handle'|None, u, v)，球头单独算（见 `in_knob`）。

    ⚠️ 半宽必须显式要求 `> 0`：柄在 u 超出 `HANDLE_U1` 之后半宽是 **0**，
       而 `abs(v) <= 0` 在 v 恰为 0 的那些像素上**成立** ⇒ 会沿着对角线画出一条 1px 的长线，
       再被 2px 描边膨胀成一整条粗带，把包围盒一路顶到画布角上（实测 bbox 44×44 顶到 x=47）。
       这不是"边缘毛刺"，是一条**贯穿整张图的伪影** —— 每个像素要么在形状里、要么不在，
       "宽度为 0 的截面"不包含任何像素 ✓
    """
    u, v = uv(x, y)
    if u < 0.0:
        return None, u, v
    if u <= BLADE_U[-1]:
        hw = blade_hw(u)
        return ("blade" if hw > 0.0 and abs(v) <= hw else None), u, v
    if u <= FERRULE_U1:
        return ("ferrule" if abs(v) <= FERRULE_HW else None), u, v
    hw = handle_hw(u)
    if hw <= 0.0:
        return None, u, v
    return ("handle" if abs(v) <= hw else None), u, v


# ════════════════════════════════════════════════════════════════════════════
#  绘图：整像素、解析判定（不靠多边形扫描线，形状永远是硬边）
# ════════════════════════════════════════════════════════════════════════════
class Cv(object):
    def __init__(self):
        self.d = {}

    def set(self, x, y, c):
        x, y = int(x), int(y)
        if 0 <= x < W and 0 <= y < H:
            self.d[(x, y)] = c

    def disc(self, cx, cy, r, c):
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                dx, dy = x + 0.5 - cx, y + 0.5 - cy
                if dx * dx + dy * dy <= r * r:
                    self.set(x, y, c)

    def star4(self, cx, cy, arm, c_core, c_arm):
        """四芒星（硬边）：中心 2×2 亮核 + 四个方向的 1px 臂。"""
        for (dx, dy) in ((0, 0), (1, 0), (0, 1), (1, 1)):
            self.set(cx + dx, cy + dy, c_core)
        for i in range(1, arm + 1):
            col = c_arm
            xs = (cx - i + 0.5, cx + 1 + i - 1, cx + 0.5, cx + 0.5)
            ys = (cy + 0.5, cy + 0.5, cy - i + 0.5, cy + 1 + i - 1)
            for k in range(4):
                self.set(xs[k], ys[k], col)


def draw_body(cv, hover):
    """刃 / 箍 / 柄 三段的**硬边**着色。

    明暗只有"一条贴边的带"这一种手法（与图标生成器同一个语言）：
      刃 上缘贴 1.2px → HI（受光）；下缘贴 2.2px → B2（凹面）；中间留大块**纯色** B1
      箍 整块 B3（暗金属）—— 它是"脖子"，必须比刃和柄都暗，才有台阶
      柄 整块 B3（暗木）+ 上缘 1px B2；球头 B3 + 上弧 B2
      ⇒ 亮刃 / 暗颈 / 暗柄：**对比**才是"一把工具"的读法 ✓
    """
    hi_th = 2.0 if hover else 1.2           # 刃口高光带厚度（悬停加厚）
    hi_u1 = 17.0 if hover else 12.0         # 高光带长度（悬停加长）
    for y in range(H):
        for x in range(W):
            zone, u, v = body_zone(x, y)
            if zone is None:
                continue
            if zone == "blade":
                hw = blade_hw(u)
                c = B1
                if v <= -(hw - 2.2):
                    c = B2                      # 刃的凹面（下缘）
                if 0.6 <= u <= hi_u1 and v >= hw - hi_th:
                    c = HI                      # 刃口受光（上缘，只在前段）
                cv.set(x, y, c)
            elif zone == "ferrule":
                cv.set(x, y, HI if (hover and v > FERRULE_HW - 1.6) else B3)
            else:                               # handle
                hw = handle_hw(u)
                cv.set(x, y, B2 if v >= hw - (1.6 if hover else 1.0) else B3)
    # 球头（柄尾）：比柄粗的一颗圆盘 —— 轮廓上有"细柄 → 粗头"，才像握把
    for y in range(H):
        for x in range(W):
            u, v = uv(x, y)
            if not in_knob(u, v):
                continue
            if v >= KNOB_R - (1.9 if hover else 1.3):
                cv.set(x, y, HI if hover else B2)   # 上缘受光弧
            else:
                cv.set(x, y, B3)
    # 绿色握把环：ACC（园丁的身份色；压在柄上，所以最后画）
    for y in range(H):
        for x in range(W):
            zone, u, v = body_zone(x, y)
            if zone == "handle" and GRIP_U0 <= u <= GRIP_U1:
                cv.set(x, y, ACC_GREEN)


def add_outline(cv):
    """统一 2px 暖灰描边（掩膜欧氏膨胀 2px 后填色）：与图标**同一个算法**。"""
    body = set(cv.d.keys())
    edge = set()
    r = 2
    for (x, y) in body:
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy > r * r + 0.25:
                    continue
                p = (x + dx, y + dy)
                if p in body:
                    continue
                if 0 <= p[0] < W and 0 <= p[1] < H:
                    edge.add(p)
    for p in edge:
        cv.d[p] = OUT_C


def add_glow(cv):
    """悬停的"发光"：刃外一颗绿星芒 + 两粒火星（**不描边** —— 它是光，不是实体）。

    位置是**量出来的**：刃的最宽处在 u=12~19、半宽 7.0 ⇒ 刃口上缘约在
    x = 6+0.707*(19+7) ≈ 24、y = 6+0.707*(19-7) ≈ 14.5，描边再往外 2px。
    星芒中心放在 (16,4)、臂长 3（最远到 y=7）⇒ 与描边留 ~3px 空隙，
    既不糊在刃上、也不飘到画布外 ✓
    """
    cx, cy = 16, 4
    cv.star4(cx, cy, 3, HI, ACC_GREEN)
    for (dx, dy) in ((6, 1), (7, 5)):
        cv.disc(cx + dx, cy + dy, 0.85, ACC_GREEN)


def to_rgba(cv):
    from PIL import Image
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px = img.load()
    for (x, y), c in cv.d.items():
        px[x, y] = c
    return img


def shifted(img, dy):
    """整张图下移 dy 像素（按下态）。透明像素也一起移，保证"只换图、不换热点"。"""
    from PIL import Image
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px, op = img.load(), out.load()
    for y in range(H):
        for x in range(W):
            sy = y - dy
            if 0 <= sy < H:
                op[x, y] = px[x, sy]
    return out


# ════════════════════════════════════════════════════════════════════════════
#  生成
# ════════════════════════════════════════════════════════════════════════════
def build_default():
    cv = Cv()
    draw_body(cv, hover=False)
    add_outline(cv)
    return to_rgba(cv)


def build_hover():
    cv = Cv()
    draw_body(cv, hover=True)
    add_outline(cv)
    add_glow(cv)
    return to_rgba(cv)


def build_press():
    return shifted(build_default(), PRESS_DY)


def tip_pixel(img):
    """铲尖 = 沿工具轴**最靠前**（u 最小）的那个不透明像素。

    工具轴正好 45° ⇒ u 最小等价于 `x + y` 最小。而 2px 描边把尖角磨成了**两个**
    同样靠前的像素（`(4,6)` 与 `(6,4)`，两者 x+y 都相等）—— 尖是"平"的。
    所以定下确定性的并列规则：**x 更小的那个**（左侧那个）✓
    这条规则写死在代码里、并由 `verify()` 反过来从图上核一遍，不靠"我记得是 (4,6)"。
    """
    px = img.load()
    best, key = None, None
    for y in range(H):
        for x in range(W):
            if px[x, y][3] == 0:
                continue
            k = (x + y, x)
            if key is None or k < key:
                best, key = (x, y), k
    return best


def tip_tie(img):
    """与铲尖并列最靠前的像素（报告里要写清"尖是两像素宽的"这件事）。"""
    px = img.load()
    m = None
    for y in range(H):
        for x in range(W):
            if px[x, y][3] == 0:
                continue
            if m is None or (x + y) < m:
                m = x + y
    out = []
    for y in range(H):
        for x in range(W):
            if px[x, y][3] != 0 and x + y == m:
                out.append((x, y))
    return out


def bbox_of(img):
    px = img.load()
    b = [W, H, -1, -1]
    for y in range(H):
        for x in range(W):
            if px[x, y][3] == 0:
                continue
            b[0] = min(b[0], x)
            b[1] = min(b[1], y)
            b[2] = max(b[2], x)
            b[3] = max(b[3], y)
    return tuple(b)


def colors_of(img):
    px = img.load()
    s = set()
    for y in range(H):
        for x in range(W):
            c = px[x, y]
            if c[3] == 0:
                continue
            s.add(c)
    return s


def count_color(img, color):
    px = img.load()
    n = 0
    for y in range(H):
        for x in range(W):
            if px[x, y] == color:
                n += 1
    return n


def diff_pixels(a, b):
    pa, pb = a.load(), b.load()
    n = 0
    for y in range(H):
        for x in range(W):
            if pa[x, y] != pb[x, y]:
                n += 1
    return n


# ════════════════════════════════════════════════════════════════════════════
#  校验（硬约束逐条机器判，不靠肉眼）
# ════════════════════════════════════════════════════════════════════════════
def verify(images, hotspot):
    problems = []
    if len(images) != 3:
        problems.append("生成 %d 张 ≠ 3" % len(images))
    for name, img in images.items():
        if img.size != (W, H):
            problems.append("%s：尺寸 %r ≠ %dx%d" % (name, img.size, W, H))
        px = img.load()
        alphas = set()
        for y in range(H):
            for x in range(W):
                alphas.add(px[x, y][3])
        if alphas - {0, 255}:
            problems.append("%s：alpha 出现中间值 %r（必须 0/255 硬边）"
                            % (name, sorted(alphas - {0, 255})[:4]))
        cols = colors_of(img)
        if len(cols) > 6:
            problems.append("%s：用了 %d 色 > 6：%r" % (name, len(cols), sorted(cols)))
        if (0, 0, 0, 255) in cols:
            problems.append("%s：出现纯黑（描边必须是暖灰 #3A3A38）" % name)
        b = bbox_of(img)
        bw, bh = b[2] - b[0] + 1, b[3] - b[1] + 1
        if bw < 33 or bh < 33:
            problems.append("%s：包围盒 %dx%d < 33（太小，看不见）" % (name, bw, bh))
        # 悬停态多一圈星芒，允许顶到画布边；默认/按下态必须给"下移 2px"留出余量
        cap = 48 if name == "hover" else 46
        if bw > cap or bh > cap:
            problems.append("%s：包围盒 %dx%d > %d" % (name, bw, bh, cap))
        if name != "hover" and b[3] + PRESS_DY > H - 1:
            problems.append("%s：包围盒下缘 y=%d，下移 %dpx 会**裁掉**底部描边"
                            % (name, b[3], PRESS_DY))
        # 热点必须落在**铲尖**那个像素上
        t = tip_pixel(img)
        if t is None:
            problems.append("%s：整张图是空的" % name)
        elif name != "press" and t != hotspot:
            problems.append("%s：最靠前不透明像素 %r ≠ 热点 %r" % (name, t, hotspot))
    # 按下态必须是默认态**整体下移 2px**，一个像素都不许有别
    a, p = images["default"], images["press"]
    pa, pp = a.load(), p.load()
    same = True
    for y in range(H):
        for x in range(W):
            sy = y - PRESS_DY
            want = pa[x, sy] if 0 <= sy < H else (0, 0, 0, 0)
            if pp[x, y] != want:
                same = False
    if not same:
        problems.append("press 不是 default 的下移 %dpx 副本（按下只许换图、不许改形）" % PRESS_DY)
    # 悬停必须**真的亮起来**：调色板集合相同不等于画面相同（绿握把两态都有），
    # 所以判据落在**像素量**上：亮部与绿点都必须变多，且整体与默认态不同。
    d = diff_pixels(images["hover"], images["default"])
    if d < 40:
        problems.append("hover 与 default 只差 %d 个像素（看不出亮起来）" % d)
    h_hi, d_hi = count_color(images["hover"], HI), count_color(images["default"], HI)
    if h_hi <= d_hi:
        problems.append("hover 的 HI 亮部 %d 不多于 default 的 %d（高光没加亮/加长）" % (h_hi, d_hi))
    h_acc, d_acc = count_color(images["hover"], ACC_GREEN), count_color(images["default"], ACC_GREEN)
    if h_acc <= d_acc:
        problems.append("hover 的 ACC 绿 %d 不多于 default 的 %d（星芒没画上）" % (h_acc, d_acc))
    if ACC_GREEN not in colors_of(images["default"]):
        problems.append("default 里没有 ACC 绿（绿色握把没画上）")
    return problems


# ════════════════════════════════════════════════════════════════════════════
#  对照图（4× 放大：报告里要"把光标贴图放大 4 倍"）
# ════════════════════════════════════════════════════════════════════════════
def render_sheet(images, hotspot, path):
    from PIL import Image, ImageDraw, ImageFont
    font_path = r"C:\Windows\Fonts\msyh.ttc"

    def font(px):
        try:
            return ImageFont.truetype(font_path, px)
        except Exception:
            return ImageFont.load_default()

    f_title, f_name, f_meta = font(22), font(19), font(14)
    scale = 4
    cell = W * scale
    pad, gap = 16, 18
    cols = 3
    lines = [
        "48×48 ｜ ≤6 色 ｜ 2px 暖灰描边 #3A3A38 ｜ 高光 #F2EFE6 ｜ alpha 只有 0/255（硬边像素）",
        "热点 (x=%d, y=%d) ＝ 铲尖 ｜ 青框 ＝ 热点那个像素 ｜ 品红十字 ＝ 点击点 ｜ 三态同一个热点"
        % hotspot,
        "按下 ＝ 与默认逐像素相同、整体下移 %dpx（只换图、不动热点 ⇒ 点下去指针不跳）" % PRESS_DY,
    ]
    Wc = pad * 2 + cols * cell + (cols - 1) * gap
    head = 34 + 22 * len(lines) + 12
    Hc = head + cell + 78
    img = Image.new("RGB", (Wc, Hc), (26, 20, 15))
    d = ImageDraw.Draw(img)
    d.text((pad, 12), "Seedfall v203 鼠标光标（园丁小铲）三态 —— 4× 放大、最近邻",
           font=f_title, fill=(240, 228, 200))
    for i, t in enumerate(lines):
        d.text((pad, 42 + i * 22), t, font=f_meta,
               fill=(150, 220, 255) if i == 2 else (190, 178, 152))

    order = [("default", "cursor_default.png", "默认：刃口一线高光 ＋ 暗木柄 ＋ 绿握把环"),
             ("hover", "cursor_hover.png", "悬停：刃口加厚加长 ＋ 箍/球头发光 ＋ 绿星芒"),
             ("press", "cursor_press.png", "按下：整体下沉 %dpx，热点不动" % PRESS_DY)]
    for idx, (key, fname, desc) in enumerate(order):
        cx = pad + idx * (cell + gap)
        cy = head
        d.rectangle([cx - 1, cy - 1, cx + cell, cy + cell], outline=(64, 48, 32))
        big = images[key].resize((cell, cell), Image.NEAREST)
        img.paste(big, (cx, cy), big)
        # 4× 格子线：一眼看出这是 48×48 的整数倍放大，不是矢量
        for k in range(1, W):
            d.line([cx + k * scale, cy, cx + k * scale, cy + cell], fill=(38, 30, 22))
            d.line([cx, cy + k * scale, cx + cell, cy + k * scale], fill=(38, 30, 22))
        # 热点：青框 + 品红十字
        hx = cx + hotspot[0] * scale
        hy = cy + hotspot[1] * scale
        d.rectangle([hx, hy, hx + scale - 1, hy + scale - 1], outline=(0, 255, 255))
        d.line([hx - 9, hy + 2, hx + scale + 8, hy + 2], fill=(255, 60, 160))
        d.line([hx + 2, hy - 9, hx + 2, hy + scale + 8], fill=(255, 60, 160))
        b = bbox_of(images[key])
        d.text((cx + 2, cy + cell + 8), fname, font=f_name, fill=(246, 240, 226))
        d.text((cx + 2, cy + cell + 32), desc, font=f_meta, fill=(196, 182, 158))
        d.text((cx + 2, cy + cell + 52),
               "包围盒 %d×%d ｜ %d 色" % (b[2] - b[0] + 1, b[3] - b[1] + 1,
                                        len(colors_of(images[key]))),
               font=f_meta, fill=(150, 220, 255))
    img.save(path)
    return path


def render_lifesize(images, path):
    """实机尺寸检查：48px（画布原尺寸）与 32px（Windows 100% DPI 下硬件光标的实际尺寸），
    各贴在三种底图（亮土 / 草地 / 夜林）上 —— "在游戏里看不看得出"才是光标唯一真正的验收。

    ⚠️ 32px 那一列必须用 **NEAREST** 缩：硬边像素画的验收标准是"整数倍/整数像素"，
       用 BILINEAR 缩出来的样子是"OS 恰好也可能这么做"的**另一种**结果，
       把它混进来当"我们的效果"是不诚实的 ⇒ 这里只放 NEAREST，并在标题里写明。
    """
    from PIL import Image, ImageDraw, ImageFont
    font_path = r"C:\Windows\Fonts\msyh.ttc"

    def font(px):
        try:
            return ImageFont.truetype(font_path, px)
        except Exception:
            return ImageFont.load_default()

    f_title, f_meta, f_name = font(20), font(13), font(13)
    bgs = [("亮土 C9A46A", (201, 164, 106)),
           ("草地 4E7A3E", (78, 122, 62)),
           ("夜林 232A33", (35, 42, 51))]
    sizes = [48, 32]
    states = ["default", "hover", "press"]
    pad, gap = 16, 8
    lab_w = 108                 # 左侧底图名那一列
    cellw = 48 + gap            # 每格按最大尺寸 48 算
    ncol = len(states) * len(sizes)
    Wc = pad * 2 + lab_w + ncol * cellw
    rh = 78
    Hc = 66 + pad + rh * len(bgs)
    img = Image.new("RGB", (Wc, Hc), (26, 20, 15))
    d = ImageDraw.Draw(img)
    d.text((pad, 10), "实机尺寸：48px（原尺寸）／32px（100% DPI 的实际大小）",
           font=f_title, fill=(240, 228, 200))
    d.text((pad, 34),
           "三行 = 三种底图，六列 = 三态 × 两尺寸；最近邻缩放",
           font=f_meta, fill=(190, 178, 152))
    # 列头
    for si, st in enumerate(states):
        for zi, s in enumerate(sizes):
            col = si * len(sizes) + zi
            d.text((pad + lab_w + col * cellw, 50), "%s %dpx" % (st, s), font=f_meta,
                   fill=(200, 190, 170))
    for bi, (bname, bc) in enumerate(bgs):
        y0 = 66 + pad // 2 + bi * rh
        d.text((pad, y0 + 22), bname, font=f_name, fill=(230, 220, 200))
        for si, st in enumerate(states):
            for zi, s in enumerate(sizes):
                col = si * len(sizes) + zi
                cx = pad + lab_w + col * cellw
                d.rectangle([cx - 3, y0, cx + s + 3, y0 + s + 3], fill=bc)
                small = images[st].resize((s, s), Image.NEAREST)
                img.paste(small, (cx, y0 + 1), small)
    img.save(path)
    return path


def main():
    args = sys.argv[1:]
    images = {
        "default": build_default(),
        "hover": build_hover(),
        "press": build_press(),
    }
    # 热点 = 铲尖像素（**从图里读出来**，不是手写一个数：手写的数会跟图分叉）
    hotspot = tip_pixel(images["default"])

    problems = verify(images, hotspot)
    print("生成 3 张｜热点 %r（铲尖 = 整张图最靠前的不透明像素；并列者 %r）"
          % (hotspot, tip_tie(images["default"])))
    for name in ("default", "hover", "press"):
        b = bbox_of(images[name])
        print("  %-8s bbox %d×%d (x %d..%d, y %d..%d)  %d 色"
              % (name, b[2] - b[0] + 1, b[3] - b[1] + 1, b[0], b[2], b[1], b[3],
                 len(colors_of(images[name]))))
    if problems:
        print("问题 %d 条：" % len(problems))
        for p in problems:
            print("  - " + p)
        if "--force" not in args:
            return 1
    else:
        print("校验 PASS：3/3 张 —— 48×48、每张 ≤6 色、alpha 只有 0/255（硬边）、"
              "包围盒 33~46px、无纯黑、热点=铲尖、按下=整体下移 %dpx" % PRESS_DY)
    if "--check" in args:
        return 0

    if not os.path.isdir(UI_DIR):
        print("找不到 UI 目录：%s" % UI_DIR)
        return 1
    for name in ("default", "hover", "press"):
        p = os.path.join(UI_DIR, "cursor_%s.png" % name)
        images[name].save(p)
        print("  写盘 %s" % p)
    if "--no-sheet" in args:
        return 0

    os.makedirs(OUT, exist_ok=True)
    sheet = render_sheet(images, hotspot, os.path.join(OUT, "cursor_states.png"))
    life = render_lifesize(images, os.path.join(OUT, "cursor_lifesize.png"))
    txt = os.path.join(OUT, "cursor_states.txt")
    with io.open(txt, "w", encoding="utf-8") as f:
        f.write("# Seedfall v203 光标清单（3 张 = 默认 / 悬停 / 按下）\n")
        f.write("# 规范：48×48 透明底 / ≤6 色 / 2px 暖灰描边 #3A3A38 / 高光 #F2EFE6 / 硬边(alpha 0|255)\n")
        f.write("# 热点：(%d, %d) —— 铲尖像素，原点左上；三态同一个热点\n" % hotspot)
        f.write("# 写入：Assets/Resources/Art/UI/cursor_<state>.png\n")
        for name in ("default", "hover", "press"):
            b = bbox_of(images[name])
            f.write("%-8s bbox %d×%d  %d 色  最靠前不透明像素 %r\n"
                    % (name, b[2] - b[0] + 1, b[3] - b[1] + 1,
                       len(colors_of(images[name])), tip_pixel(images[name])))
    print("对照图：%s" % sheet)
    print("实机尺寸图：%s" % life)
    print("清单：%s" % txt)
    return 0


if __name__ == "__main__":
    sys.exit(main())
