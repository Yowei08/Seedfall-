# -*- coding: utf-8 -*-
"""把 129 条 desc 拆成"句"（按 。），统计去重后的句子 —— 供逐句翻译。"""
import json, os, sys, re
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, 'Assets', 'Resources', 'Data')

zh = json.load(open(os.path.join(D, 'l10n_zh.json'), encoding='utf-8'))
en = json.load(open(os.path.join(D, 'l10n_en.json'), encoding='utf-8'))
enmap = {e['k']: e['v'] for e in en['entries']}

descs = [(e['k'], e['z']) for e in zh['entries'] if e['k'].startswith('desc.') and not enmap.get(e['k'])]
print('待拆 desc: %d' % len(descs))

def clauses(s):
    out = []
    cur = []
    for ch in s:
        cur.append(ch)
        if ch == '\u3002':          # 。
            out.append(''.join(cur)); cur = []
    if cur:
        out.append(''.join(cur))
    return out

from collections import Counter
cnt = Counter()
for k, v in descs:
    for c in clauses(v):
        cnt[c] += 1

print('去重句子: %d' % len(cnt))
print()
for c, n in cnt.most_common():
    print('%3d\t%s' % (n, c))
