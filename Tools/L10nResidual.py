# -*- coding: utf-8 -*-
"""残留清单：① 数据键拆分 ② 可见串里还没进表的（切到 En 依然中文的那批）。"""
import json, os, re, sys
from collections import defaultdict
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, 'Assets', 'Resources', 'Data')

zh = json.load(open(os.path.join(D, 'l10n_zh.json'), encoding='utf-8'))
en = json.load(open(os.path.join(D, 'l10n_en.json'), encoding='utf-8'))
ja = json.load(open(os.path.join(D, 'l10n_ja.json'), encoding='utf-8'))
shapes = {e['z'] for e in zh['entries'] if e['z']}
keys = [e['k'] for e in zh['entries']]

MARK = '\u00a7'
DIG = re.compile(r'[0-9]+(?:[.,][0-9]+)?')

def norm(s):
    """与 Tools/L10nGen.py 完全同一口径：先 {...} → §，再数字 → §"""
    out = []
    i = 0
    n = len(s)
    while i < n:
        c = s[i]
        if c == '{':
            j = s.find('}', i + 1)
            if j < 0:
                return None
            out.append(MARK); i = j + 1; continue
        out.append(c); i += 1
    return DIG.sub(MARK, ''.join(out))

namek = [k for k in keys if k.startswith('name.')]
desck = [k for k in keys if k.startswith('desc.')]
uik = [k for k in keys if not k.startswith(('name.', 'desc.'))]
enmap = {e['k']: e['v'] for e in en['entries']}
jamap = {e['k']: e['v'] for e in ja['entries']}
print('keys=%d  ui=%d  name=%d  desc=%d' % (len(keys), len(uik), len(namek), len(desck)))
print('en filled: ui=%d name=%d desc=%d' % (
    sum(1 for k in uik if enmap.get(k)), sum(1 for k in namek if enmap.get(k)),
    sum(1 for k in desck if enmap.get(k))))
print('ja filled: ui=%d name=%d desc=%d' % (
    sum(1 for k in uik if jamap.get(k)), sum(1 for k in namek if jamap.get(k)),
    sum(1 for k in desck if jamap.get(k))))
print('en 仍缺（切 En 后仍是中文）: %d 条' % sum(1 for k in keys if not enmap.get(k)))

# ② 可见串里没进表的
C = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_candidates.json'), encoding='utf-8'))
miss = defaultdict(list)
for f, v in C.items():
    for ln, t in v:
        z = norm(t)
        if z is None or z not in shapes:
            miss[f].append((ln, t))
tot = sum(len(v) for v in miss.values())
print()
print('未进表的可见串（非 dev 文件、非 log 语句）: %d 处' % tot)
for f, v in sorted(miss.items(), key=lambda kv: -len(kv[1]))[:12]:
    print('  %5d  %s' % (len(v), f))
print()
print('示例（前 14 条）:')
n = 0
for f, v in sorted(miss.items(), key=lambda kv: -len(kv[1])):
    for ln, t in v[:3]:
        print('  %s:%d  %s' % (os.path.basename(f), ln, t[:60]))
        n += 1
        if n >= 14:
            break
    if n >= 14:
        break
