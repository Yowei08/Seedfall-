r"""v179 三种攻击特效的**程序化网格** —— 离线生成与核对。

为什么运行时构造在 `Assets/Scripts/Bootstrap/WorldView.cs` 而不在这里：
    `Tools/` 在 `Assets/` **之外**。Unity 只编译 `Assets/` 与 `Packages/`，
    而 `Seedfall.Runtime.csproj` 用**显式** `<Compile Include="Assets\...">`
    ⇒ 放在 `Tools/` 里的 .cs **不会被任何一方编译**，`WorldView` 引用不到。

所以本文件不是"可选的预览工具"，它是那套拓扑的**离线权威副本**：
    · `gen_fx_meshes.py`  用与 C# **逐字相同**的表和算法在这里重建一遍
    · `verify_counts.py`  反过来从 `WorldView.cs` **读出**那些表，
                         再与这里产出的 OBJ 比顶点数/三角数/坐标
    ⇒ C# 改了、这里没跟上，`verify_counts.py` 就会 FAIL ✓

四族网格（全部 flat shading：每个三角形独占 3 个顶点 ⇒ 逐面法线 ⇒ 硬边）：

    shard{0..5}   八面体 + 各轴拉伸 + 确定性抖动      8 三角 /  24 顶点
    frustum{s}    六棱台（水柱的一段）                s×4 三角 / s×12 顶点
    icoshell1     二十面体一级细分球壳              80 三角 / 240 顶点
    disc18        贴地圆盘（湿痕）                  18 三角 /  54 顶点

重跑：
    C:\\Users\\18462\\.dsh\\dsh-runtimes\\dsh-primary-runtime\\dependencies\\python\\python.exe \\
        Tools/mesh_gen/gen_fx_meshes.py
"""

import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")

# ─────────────────────────────────────────────────────────────
# 与 WorldView.cs 逐字相同的表（verify_counts.py 会把它们校对回来）
# ─────────────────────────────────────────────────────────────

# `FxShardMesh`：八面体 6 极点（0=+x 1=-x 2=+y 3=-y 4=+z 5=-z）+ 8 面
SHARD_POLES = [
    (1.0, 0.0, 0.0), (-1.0, 0.0, 0.0),
    (0.0, 1.0, 0.0), (0.0, -1.0, 0.0),
    (0.0, 0.0, 1.0), (0.0, 0.0, -1.0),
]
SHARD_FACES = [
    0, 2, 4, 4, 2, 1, 1, 2, 5, 5, 2, 0,
    4, 3, 0, 1, 3, 4, 5, 3, 1, 0, 3, 5,
]
SHARD_STRETCH = [
    (1.00, 1.60, 0.50),
    (1.50, 0.60, 0.80),
    (0.70, 1.05, 1.60),
    (1.25, 0.95, 1.05),
    (0.55, 1.40, 0.90),
    (1.10, 1.15, 0.62),
]

# `FxIcoShellMesh`：二十面体 12 顶点 / 20 面
ICO_T = (1.0 + math.sqrt(5.0)) * 0.5
ICO_VERTS = [
    (-1, ICO_T, 0), (1, ICO_T, 0), (-1, -ICO_T, 0), (1, -ICO_T, 0),
    (0, -1, ICO_T), (0, 1, ICO_T), (0, -1, -ICO_T), (0, 1, -ICO_T),
    (ICO_T, 0, -1), (ICO_T, 0, 1), (-ICO_T, 0, -1), (-ICO_T, 0, 1),
]
ICO_IDX = [
    0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
    1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
    3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
    4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
]

# `FxFrustumMesh` 的入参（WorldView 里的调用：5300+segN, 6, 0.72, 1.0）
FRUSTUM_SIDES = 6
FRUSTUM_RA = 0.72
FRUSTUM_RB = 1.0

DISC_SEGS = 18


def lcg_jitter(seed):
    """与 C# `NextJitter` 逐位一致：seed 是 uint32，取 (seed>>9)&1023 归一化到 [-0.5,0.5]。"""
    seed = (seed * 1664525 + 1013904223) & 0xFFFFFFFF
    return seed, ((seed >> 9) & 1023) / 1023.0 - 0.5


def norm(v):
    m = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    if m == 0.0:
        return (0.0, 0.0, 0.0)
    return (v[0] / m, v[1] / m, v[2] / m)


def build_shard(variant):
    """八面体 + 拉伸 + 确定性抖动，再按面拆顶点（flat）。"""
    st = SHARD_STRETCH[variant % len(SHARD_STRETCH)]
    seed = (0x9E3779B9 * (variant + 1) + 12345) & 0xFFFFFFFF
    poles = []
    for p in SHARD_POLES:
        seed, jx = lcg_jitter(seed)
        seed, jy = lcg_jitter(seed)
        seed, jz = lcg_jitter(seed)
        poles.append((
            (p[0] + jx * 0.26) * st[0],
            (p[1] + jy * 0.26) * st[1],
            (p[2] + jz * 0.26) * st[2],
        ))
    tris = [(poles[SHARD_FACES[i]], poles[SHARD_FACES[i + 1]], poles[SHARD_FACES[i + 2]])
            for i in range(0, len(SHARD_FACES), 3)]
    return tris, poles


def build_frustum(sides, ra, rb):
    """棱台：侧面 2×sides 三角 + 两个端盖各 sides 三角。"""
    a, b = [], []
    for i in range(sides):
        t = (i / sides) * math.pi * 2.0
        cs, sn = math.cos(t), math.sin(t)
        a.append((cs * ra, sn * ra, -0.5))
        b.append((cs * rb, sn * rb, 0.5))
    ca, cb = (0.0, 0.0, -0.5), (0.0, 0.0, 0.5)
    tris = []
    for i in range(sides):
        j = (i + 1) % sides
        tris.append((a[i], b[j], b[i]))
        tris.append((a[i], a[j], b[j]))
        tris.append((ca, a[j], a[i]))
        tris.append((cb, b[i], b[j]))
    return tris, a + b


def build_icoshell(subdiv):
    """二十面体细分 `subdiv` 次并归一化到单位球。"""
    verts = [norm(v) for v in ICO_VERTS]
    idx = list(ICO_IDX)
    for _ in range(max(1, subdiv)):
        mids = {}

        def mid(i, j):
            key = (i, j) if i < j else (j, i)
            if key in mids:
                return mids[key]
            p = ((verts[i][0] + verts[j][0]) * 0.5,
                 (verts[i][1] + verts[j][1]) * 0.5,
                 (verts[i][2] + verts[j][2]) * 0.5)
            verts.append(norm(p))
            mids[key] = len(verts) - 1
            return mids[key]

        nxt = []
        for i in range(0, len(idx), 3):
            a, b, c = idx[i], idx[i + 1], idx[i + 2]
            ab, bc, ca = mid(a, b), mid(b, c), mid(c, a)
            nxt += [a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca]
        idx = nxt
    tris = [(verts[idx[i]], verts[idx[i + 1]], verts[idx[i + 2]])
            for i in range(0, len(idx), 3)]
    return tris, verts


def build_disc(segs):
    """贴地圆盘，法线朝 +Y。"""
    tris = []
    c = (0.0, 0.0, 0.0)
    for i in range(segs):
        t0 = (i / segs) * math.pi * 2.0
        t1 = ((i + 1) / segs) * math.pi * 2.0
        tris.append((c,
                     (math.cos(t1), 0.0, math.sin(t1)),
                     (math.cos(t0), 0.0, math.sin(t0))))
    return tris, []


def write_obj(path, name, tris):
    """把三角汤写成 OBJ（flat：每面 3 个独立顶点）。"""
    with open(path, "w", encoding="ascii") as f:
        f.write("# %s -- generated by Tools/mesh_gen/gen_fx_meshes.py\n" % name)
        n = 0
        for t in tris:
            for v in t:
                f.write("v %.6f %.6f %.6f\n" % v)
                n += 1
        for i in range(len(tris)):
            b = i * 3 + 1
            f.write("f %d %d %d\n" % (b, b + 1, b + 2))
    return n, len(tris)


def face_normals(tris):
    """逐面法线 —— flat shading 的证据：同一面的 3 个顶点法线完全一致。"""
    out = []
    for a, b, c in tris:
        u = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
        v = (c[0] - a[0], c[1] - a[1], c[2] - a[2])
        n = (u[1] * v[2] - u[2] * v[1],
             u[2] * v[0] - u[0] * v[2],
             u[0] * v[1] - u[1] * v[0])
        out.append(norm(n))
    return out


def outward_ratio(tris, verts_hint):
    """朝外法线的比例：多边形是否闭合并朝外（<1 说明有面绕反了）。"""
    if not tris:
        return 1.0
    cx = sum(p[0] for t in tris for p in t) / (len(tris) * 3)
    cy = sum(p[1] for t in tris for p in t) / (len(tris) * 3)
    cz = sum(p[2] for t in tris for p in t) / (len(tris) * 3)
    fn = face_normals(tris)
    good = 0
    for (a, b, c), n in zip(tris, fn):
        fc = ((a[0] + b[0] + c[0]) / 3 - cx,
              (a[1] + b[1] + c[1]) / 3 - cy,
              (a[2] + b[2] + c[2]) / 3 - cz)
        if fc[0] * n[0] + fc[1] * n[1] + fc[2] * n[2] > 0:
            good += 1
    return good / len(tris)


def main():
    os.makedirs(OUT, exist_ok=True)
    manifest = {"families": {}}

    # 1) 碎片 6 个变体
    shard_counts = []
    for variant in range(len(SHARD_STRETCH)):
        tris, _ = build_shard(variant)
        nv, nt = write_obj(os.path.join(OUT, "shard%d.obj" % variant),
                           "shard%d" % variant, tris)
        shard_counts.append({"variant": variant, "verts": nv, "tris": nt,
                             "outward": round(outward_ratio(tris, None), 4)})
    manifest["families"]["shard"] = {
        "count": len(SHARD_STRETCH), "variants": shard_counts,
        "verts": shard_counts[0]["verts"], "tris": shard_counts[0]["tris"],
    }

    # 2) 六棱台
    ftris, _ = build_frustum(FRUSTUM_SIDES, FRUSTUM_RA, FRUSTUM_RB)
    fnv, fnt = write_obj(os.path.join(OUT, "frustum6.obj"), "frustum6", ftris)
    manifest["families"]["frustum"] = {
        "sides": FRUSTUM_SIDES, "ra": FRUSTUM_RA, "rb": FRUSTUM_RB,
        "verts": fnv, "tris": fnt, "outward": round(outward_ratio(ftris, None), 4),
    }

    # 3) 球壳（一级细分）
    itris, _ = build_icoshell(1)
    inv, int_ = write_obj(os.path.join(OUT, "icoshell1.obj"), "icoshell1", itris)
    manifest["families"]["icoshell"] = {
        "subdiv": 1, "verts": inv, "tris": int_,
        "outward": round(outward_ratio(itris, None), 4),
    }

    # 4) 湿痕圆盘
    dtris, _ = build_disc(DISC_SEGS)
    dnv, dnt = write_obj(os.path.join(OUT, "disc18.obj"), "disc18", dtris)
    manifest["families"]["disc"] = {
        "segs": DISC_SEGS, "verts": dnv, "tris": dnt,
        "normal_up": round(face_normals(dtris)[0][1], 4),
    }

    # 5) 每族一条"期望值"，给 verify_counts.py 钉住
    manifest["expected"] = {
        "shard": {"verts": 24, "tris": 8},
        "frustum6": {"verts": FRUSTUM_SIDES * 12, "tris": FRUSTUM_SIDES * 4},
        "icoshell1": {"verts": 240, "tris": 80},
        "disc18": {"verts": DISC_SEGS * 3, "tris": DISC_SEGS},
    }

    mpath = os.path.join(OUT, "fx_mesh_manifest.json")
    with open(mpath, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)

    print("=" * 66)
    print("v179 程序化网格 —— 生成完毕")
    print("=" * 66)
    for name, fam in manifest["families"].items():
        if "variants" in fam:
            v0 = fam["variants"][0]
            print("  %-12s %d 变体  每变体 %d 三角 / %d 顶点  朝外 %.0f%%"
                  % (name, fam["count"], v0["tris"], v0["verts"], v0["outward"] * 100))
        elif name == "disc":
            print("  %-12s %d 三角 / %d 顶点  法线朝上 y=%.3f"
                  % (name, fam["tris"], fam["verts"], fam["normal_up"]))
        else:
            print("  %-12s %d 三角 / %d 顶点  朝外 %.0f%%"
                  % (name, fam["tris"], fam["verts"], fam["outward"] * 100))
    print("  清单 → %s" % os.path.relpath(mpath, HERE))
    print("  下一步 → verify_counts.py（与 WorldView.cs 逐项比对）")


if __name__ == "__main__":
    main()
