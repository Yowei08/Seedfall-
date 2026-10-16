# -*- coding: utf-8 -*-
"""
只取一个字段：几何对象的 `Vertices` 数组的**坐标量级**。

## 实测结论（本工程 48 个植物 FBX 全部一致，含成熟体）

    文件名                     nVerts   坐标范围            模型米制高度
    radish_guard_young.fbx     260      [-0.15, 0.37]       0.373
    radish_guard_grown.fbx     286      [-0.33, 0.98]       0.981
    radish_guard.fbx           684      [-0.77, 1.74]       1.742

也就是：**顶点坐标约等于米制尺寸（1 单位 ≈ 1 米）**，尽管 FBX 头里
`UnitScaleFactor = 1.0`（声明"1 单位 = 1 厘米"）。两者不矛盾 —— 关键在下面这条链：

    Unity 侧 `ArtImportSettings.cs`：Plants 目录 `globalScale = 0.01f`
      → 顶点值再乘 0.01
    但导入后的根节点 `localScale.z = 100`（FBX 单位声明带进来的）
      → 世界尺寸 = 顶点值 × 0.01 × 100 = **顶点值（米）**

所以"顶点 ≈ 米"这个约定必须保持：一个新模型若按"厘米"去写顶点（0.373 m 写成 37.3），
乘完那两步会变成 37.3 米 —— 就是本项目注释里记的"18 公里高的植物"。

本脚本用来在写盘后复核这一条：坐标范围的上界应当 ≈ 该模型的米制高度。

用法: python fbx_vertex_scale.py <file.fbx> [...]
      python fbx_vertex_scale.py --dir <目录>
"""

import os
import struct
import sys
import zlib

ARRAY_CODE = {0x64: "d", 0x66: "f", 0x69: "i", 0x6C: "l", 0x62: "b"}


def scan_vertices(path):
    """
    返回 dict(nVerts, lo, hi, extent) 或 None。

    ★ 实测字节（radish_guard_young.fbx @11454）★
        00 00 00 8C 07 00 00 | 08 | 56 65 72 74 69 63 65 73 | 64 | 0C 03 00 00 | 01 00 00 00 | 7F 07 00 00 | <zlib>
                             len  "V  e  r  t  i  c  e  s"  'd'  alen=780      enc=1         clen=1919
    要点：
      · 属性名是「**1 字节**长度 + 内容」（不是 4 字节，也不是 NUL 结尾）
        —— 早先按 4 字节长度校验，一个都匹配不上，于是全部报"Vertices 未找到"。
      · 名字后紧跟类型码 'd'（double 数组），再是 12 字节数组头。
      · 坐标是 double 且 zlib 压缩（enc=1）。
    """
    data = open(path, "rb").read()
    name = b"Vertices"
    idx = data.find(name)
    while idx >= 0:
        if idx < 1 or data[idx - 1] != len(name):
            idx = data.find(name, idx + 1)
            continue
        o = idx + len(name)
        code_byte = data[o] if o < len(data) else 0
        code = ARRAY_CODE.get(code_byte)
        if code is None or code == "b":
            idx = data.find(name, idx + 1)
            continue
        base = o + 1
        if base + 12 > len(data):
            break
        alen, enc, clen = struct.unpack("<III", data[base:base + 12])
        comp = data[base + 12:base + 12 + clen]
        if not (0 < alen <= 400000) or clen <= 0 or len(comp) < clen:
            idx = data.find(name, idx + 1)
            continue
        try:
            raw = zlib.decompress(comp) if enc == 1 else comp
        except Exception:                                  # noqa: BLE001
            idx = data.find(name, idx + 1)
            continue
        item = struct.calcsize("<" + code)
        n = min(alen, len(raw) // item)
        if n < 3:
            idx = data.find(name, idx + 1)
            continue
        vals = struct.unpack("<%d%s" % (n, code), raw[:n * item])
        return dict(nVerts=n // 3, lo=min(vals), hi=max(vals),
                    extent=max(vals) - min(vals))
    return None


def summarize(path):
    base = os.path.basename(path)
    try:
        v = scan_vertices(path)
    except Exception as exc:                               # noqa: BLE001
        return "%-32s SCAN-FAIL %s" % (base, exc)
    if v is None:
        return "%-32s Vertices 未找到" % base
    # 判读：本工程约定是"顶点值 ≈ 米制高度"，所以 extent 应落在 0.1~3 之间
    e = v["extent"]
    if 0.05 <= e <= 3.5:
        unit = "OK（顶点≈米制，与既有 48 个文件一致）"
    elif e > 3.5:
        unit = "异常！疑似按厘米写顶点（会放大 100 倍）"
    else:
        unit = "异常！量级过小（会被判成 1/100 存储）"
    return ("%-32s nVerts=%-6d coord=[%.3f, %.3f] extent=%.3f  -> %s"
            % (base, v["nVerts"], v["lo"], v["hi"], e, unit))


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
