# -*- coding: utf-8 -*-
"""Count player-visible name/description strings inside Resources/Data/*.json."""
import json, os, sys, re
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'Assets', 'Resources', 'Data')
CJK = re.compile(r'[\u3400-\u9fff]')
FIELDS = ('name', 'label', 'title', 'desc', 'description', 'text', 'hint', 'note')
total = 0
per = {}
alld = set()
for fn in sorted(os.listdir(DATA)):
    if not fn.endswith('.json'): continue
    p = os.path.join(DATA, fn)
    try:
        obj = json.load(open(p, encoding='utf-8-sig'))
    except Exception as e:
        print('SKIP %-20s %s' % (fn, e)); continue
    found = []
    def walk(o):
        if isinstance(o, dict):
            for k, v in o.items():
                if isinstance(v, str) and k.lower() in FIELDS and CJK.search(v):
                    found.append((k, v))
                else:
                    walk(v)
        elif isinstance(o, list):
            for v in o: walk(v)
    walk(obj)
    per[fn] = len(found); total += len(found)
    for _, v in found: alld.add(v)
    print('%-20s %4d' % (fn, len(found)))
print('---- total name/desc strings: %d   distinct: %d' % (total, len(alld)))
