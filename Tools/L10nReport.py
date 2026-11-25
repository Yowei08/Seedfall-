# -*- coding: utf-8 -*-
"""Delivery numbers: the 7832 -> 2684 -> 493 funnel, with the reason for every cut."""
import json, os, sys, re
from collections import defaultdict
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_scan_cs.json'), encoding='utf-8'))
C = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_candidates.json'), encoding='utf-8'))
K = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_keys.json'), encoding='utf-8'))
D = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_dropped.json'), encoding='utf-8'))

print('STEP 1  all CJK string literals in Assets/{Scripts,Editor}/**/*.cs')
print('        occurrences = %d   files = %d   distinct = %d' % (A['total'], len(A['perFile']), len(A['distinct'])))

DEV = re.compile(r'/Probe/|/Debug/|/Automation/|/Tests/|SelfCheck|DeathDiagnostics|RunBot|RunSimulator|GfxProbe|ProbeV|ProbePopups')
dev_occ = sum(len(v) for f, v in A['perFile'].items() if DEV.search(f))
print('        minus dev-tool files (Probe/ Debug/ Automation/ Tests/ SelfCheck/ RunBot/ DeathDiagnostics)')
print('          -> %d occurrences dropped (%d files)' % (dev_occ, sum(1 for f in A['perFile'] if DEV.search(f))))
nondev_occ = A['total'] - dev_occ
nondev_dis = len(set(b for f, v in A['perFile'].items() if not DEV.search(f) for _, b in v))
print('        left = %d occurrences / %d distinct' % (nondev_occ, nondev_dis))

c_occ = sum(len(v) for v in C.values())
c_dis = len(set(b for v in C.values() for _, b in v))
print('STEP 2  minus statements that are log output (Debug.Log/LogError/LogWarning/Assert/Report/Console)')
print('          -> %d dropped   left = %d occurrences / %d distinct / %d files'
      % (nondev_occ - c_occ, c_occ, c_dis, len(C)))
print()
print('        top files of the %d player-visible occurrences:' % c_occ)
for f, v in sorted(C.items(), key=lambda kv: -len(kv[1]))[:14]:
    print('          %5d  %s' % (len(v), f))

print()
print('STEP 3  keep only the 12 renderer files -> key set')
KS = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_keyset.json'), encoding='utf-8'))
print('        renderer-file literals = %d distinct' % len(KS))
print('        minus %d pure-punctuation / space-edge / dev-tag fragments' % len(D))
print('        -> %d keys   + 37 hand-named keys (ui.* hud.* combat.*)' % len(K))
print('        -> table rows = 493 (2 entries dropped: braces unbalanced in the source literal)')

# ---- json data names ----
DATA = os.path.join(ROOT, 'Assets', 'Resources', 'Data')
CJK = re.compile(r'[\u3400-\u9fff]')
FIELDS = ('name', 'label', 'title', 'desc', 'description', 'text', 'hint', 'note')
tot, alld, per = 0, set(), {}
broken = []
for fn in sorted(os.listdir(DATA)):
    if not fn.endswith('.json') or fn.startswith('l10n_'): continue
    p = os.path.join(DATA, fn)
    try:
        obj = json.load(open(p, encoding='utf-8-sig'))
    except Exception as e:
        broken.append((fn, str(e))); continue
    found = []
    def walk(o):
        if isinstance(o, dict):
            for k, v in o.items():
                if isinstance(v, str) and k.lower() in FIELDS and CJK.search(v):
                    found.append(v)
                else:
                    walk(v)
        elif isinstance(o, list):
            for v in o: walk(v)
    walk(obj)
    per[fn] = len(found); tot += len(found); alld.update(found)
print()
print('STEP 4  Resources/Data 数据表里的 name/description（**不在本词表内**）')
print('        occurrences = %d   distinct = %d' % (tot, len(alld)))
for fn, n in sorted(per.items(), key=lambda kv: -kv[1]):
    if n: print('          %-18s %4d' % (fn, n))
for fn, e in broken:
    print('        !! %s 不能严格解析：%s' % (fn, e))

# ---- table coverage ----
print()
print('STEP 5  6 份词表')
for code in ('zh', 'en', 'ur', 'ja', 'ko', 'ru'):
    t = json.load(open(os.path.join(DATA, 'l10n_%s.json' % code), encoding='utf-8'))
    filled = sum(1 for e in t['entries'] if e['v'])
    print('        l10n_%-3s keys=%d filled=%d missing=%d' % (code, len(t['entries']), filled, len(t['entries']) - filled))
