# -*- coding: utf-8 -*-
import sys, json
sys.stdout.reconfigure(encoding='utf-8')
p = r'D:\Unity\project\Seedfall守园者\Assets\Resources\Data\town_shop.json'
raw = open(p, encoding='utf-8-sig').read()
line = raw.splitlines()[4]
print('line5 len', len(line))
bad = [(i, c, hex(ord(c))) for i, c in enumerate(line) if ord(c) < 0x20 or c == '"']
print('quote/control positions:', bad)
i = 140
print(repr(line[130:170]))
try:
    json.loads(raw); print('parses OK')
except Exception as e:
    print('fail:', e)
