# -*- coding: utf-8 -*-
"""desc 句子表：按 md5 前 8 位做稳定键（避免手写中文键打错字）。"""
import json, os, sys, hashlib
from collections import Counter
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, 'Assets', 'Resources', 'Data')


def h(s):
    return hashlib.md5(s.encode('utf-8')).hexdigest()[:8]


def clauses(s):
    out = []
    cur = []
    for ch in s:
        cur.append(ch)
        if ch == '\u3002':
            out.append(''.join(cur)); cur = []
    if cur:
        out.append(''.join(cur))
    return out


zh = json.load(open(os.path.join(D, 'l10n_zh.json'), encoding='utf-8'))
en = json.load(open(os.path.join(D, 'l10n_en.json'), encoding='utf-8'))
enmap = {e['k']: e['v'] for e in en['entries']}
descs = [(e['k'], e['z']) for e in zh['entries'] if e['k'].startswith('desc.') and not enmap.get(e['k'])]

cnt = Counter()
for _k, v in descs:
    for c in clauses(v):
        cnt[c] += 1

print('desc 条数 %d   去重句子 %d' % (len(descs), len(cnt)))
print()
for c, n in cnt.most_common():
    print('%s\t%3d\t%s' % (h(c), n, c))
