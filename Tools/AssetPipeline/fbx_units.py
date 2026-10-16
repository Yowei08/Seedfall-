# -*- coding: utf-8 -*-
"""
只取一个字段：FBX GlobalSettings 里的 `UnitScaleFactor`（以及 UpAxis）。

用途：判定「这个 FBX 里的网格坐标是米制还是厘米制」。
这一步是**载荷性**的，因为：

    Assets/Scripts/Art/ArtImportSettings.cs
        mi.globalScale = Plants 目录 ? 0.01f : 1f     // 把 FBX 单位带来的那 100 倍压回去
    Assets/Scripts/Art/ModelView.cs :: Spawn
        meshZ = MeshRawHeight / |BaseScale.z|;  if (meshZ >= 0.05) → 把根缩放压回 1

两条合起来 ⇒ 植物网格必须是「厘米数量级坐标 + 根 scale=100」这一种约定。
若某模型是米制网格（坐标≈1.7），那条 0.01 会再缩 100 倍，
而上限判据又会把根上的 100 抹掉 → 净 100 倍误差。
（项目注释里记着两次真实事故：136m 的豌豆射手、18km 的植物。）

判读：USF = 1.0 → 1 单位 = 1 厘米；USF = 100.0 → 1 单位 = 1 米。

实现说明（踩过的两个坑，别改回去）
    ① 属性名不是 NUL 结尾，是「4 字节小端长度 + 内容」。
    ② 名字之后紧跟**一个类型字节**（'S'），再是属性描述 `S<len>"double"`，
       最后才是 `'D' + 8 字节 double` 的值。
       所以要在描述尾巴里找 'D'/'F'/'I'，不能假定值紧跟在名字后面。

用法: python fbx_units.py <file.fbx> [...]
      python fbx_units.py --dir <目录>
"""

import os
import struct
import sys

LEAF_READERS = {
    b"UnitScaleFactor": ("D", "F"),
    b"UpAxis": ("I",),
}

_TYPE_BYTES = {0x44: "D", 0x46: "F", 0x49: "I"}


def scan(path):
    data = open(path, "rb").read()
    out = {}
    for name, types in LEAF_READERS.items():
        idx = data.find(name)
        while idx >= 0:
            plen = struct.unpack("<I", data[idx - 4:idx])[0] if idx >= 4 else -1
            if plen == len(name):
                tail = data[idx + len(name): idx + len(name) + 64]
                for k, ch in enumerate(tail):
                    tpc = _TYPE_BYTES.get(ch)
                    if tpc is None or tpc not in types:
                        continue
                    base = idx + len(name) + k + 1
                    need = 8 if tpc == "D" else 4
                    raw = data[base:base + need]
                    if len(raw) < need:
                        continue
                    val = struct.unpack("<d" if tpc == "D" else
                                        ("<f" if tpc == "F" else "<I"), raw)[0]
                    out.setdefault(name.decode(), []).append((tpc, round(float(val), 6)))
                    break
            idx = data.find(name, idx + 1)
    return out


def summarize(path):
    base = os.path.basename(path)
    try:
        vals = scan(path)
    except Exception as exc:                       # noqa: BLE001
        return "%-34s SCAN-FAIL %s" % (base, exc)
    usf_t = (vals.get("UnitScaleFactor") or [("?", None)])[0]
    usf = usf_t[1]
    up_t = (vals.get("UpAxis") or [("?", None)])[0]
    if usf is None:
        declared = "?"
    elif abs(usf - 1.0) < 1e-6:
        declared = "1unit=1cm"
    elif abs(usf - 100.0) < 1e-3:
        declared = "1unit=1m "
    elif abs(usf - 10.0) < 1e-3:
        declared = "mm       "
    else:
        declared = "%.3f" % usf
    return "%-34s UnitScaleFactor(%s)=%-8s %-10s UpAxis=%s" % (
        base, usf_t[0], usf, declared, up_t[1])


if __name__ == "__main__":
    argv = sys.argv[1:]
    if not argv:
        print(__doc__)
        sys.exit(0)
    if argv[0] == "--dir":
        for f in sorted(os.listdir(argv[1])):
            if f.lower().endswith(".fbx"):
                print(summarize(os.path.join(argv[1], f)))
    else:
        for f in argv:
            print(summarize(f))
