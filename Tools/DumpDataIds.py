# -*- coding: utf-8 -*-
"""Dump data-card id -> name for the translation pass."""
import json, os, sys, re
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = os.path.join(ROOT, 'Assets', 'Resources', 'Data')
CARDS = sys.argv[1:] or ['town_unlocks.json', 'town_seeds.json', 'town_skins.json', 'enemies.json']
CJK = re.compile(r'[\u3400-\u9fff]')
for fn in CARDS:
    p = os.path.join(D, fn)
    obj = json.load(open(p, encoding='utf-8-sig'))
    out = []

    def walk(o):
        if isinstance(o, dict):
            i = o.get('id')
            if isinstance(i, str):
                for f, pre in (('name', 'name.'), ('desc', 'desc.'), ('description', 'desc.')):
                    v = o.get(f)
                    if isinstance(v, str) and CJK.search(v):
                        out.append((pre + i, v))
            for v in o.values(): walk(v)
        elif isinstance(o, list):
            for v in o: walk(v)
    walk(obj)
    print('### ' + fn + '  (%d)' % len(out))
    for k, v in out:
        v = v.replace('\n', '\\n')
        print("%s\t%s" % (k, v if len(v) <= 60 else v[:58] + '…'))
