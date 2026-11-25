# -*- coding: utf-8 -*-
"""反证：把文档复制成临时副本，改掉其中几个数字，证明校验脚本会 FAIL（exit 1）。

只读 Docs/数值设计-v193.md，只写 Docs/_verify_negative_copy.md。不碰 Assets。
"""
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC = os.path.join(ROOT, "Docs", "数值设计-v193.md")
DST = os.path.join(ROOT, "Docs", "_verify_negative_copy.md")
TOOL = os.path.join(ROOT, "Tools", "docs", "verify_numbers.py")

with open(SRC, "r", encoding="utf-8") as f:
    text = f.read()

# 三处改动：一个主目标 + 两个"看起来很像"的值，都必须是脚本能抓到的
mutations = [
    ("| V02 | 20 分钟 基础兵 HP | 80.00 |", "| V02 | 20 分钟 基础兵 HP | 81.00 |"),
    ("| V09 | 道具总件数 | 46 |", "| V09 | 道具总件数 | 44 |"),
    ("| V17 | 爆炸收割比 | 0.33333334 |", "| V17 | 爆炸收割比 | 0.5 |"),
]
applied = 0
for old, new in mutations:
    if old in text:
        text = text.replace(old, new)
        applied += 1
    else:
        print("!! 没找到要改的行（文档格式变了）：", old)
        sys.exit(2)

with open(DST, "w", encoding="utf-8") as f:
    f.write(text)
print("已生成被改坏的副本（改了 %d 处）：%s" % (applied, DST))

r = subprocess.run([sys.executable, TOOL, "--doc", DST], capture_output=True, text=True,
                   encoding="utf-8")
print(r.stdout)
if r.stderr.strip():
    print("STDERR:", r.stderr)
print("被改坏的副本 -> exit code =", r.returncode)

# 再验一次原文档，确认它仍然是 PASS（证明失败确实来自那三处改动）
r2 = subprocess.run([sys.executable, TOOL], capture_output=True, text=True, encoding="utf-8")
print("原文档 -> exit code =", r2.returncode)

ok = (r.returncode == 1 and r2.returncode == 0)
print("反证结果：", "PASS（副本 FAIL、原文 PASS）" if ok else "不成立")
sys.exit(0 if ok else 3)
