# -*- coding: utf-8 -*-
"""Build ordered key list (filter pure-punctuation fragments) and emit a fill-in skeleton."""
import json, os, sys, re
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_keyset.json'), encoding='utf-8'))

# a string is "content" if it holds a CJK ideograph / kana / hangul / fullwidth latin
CONTENT = re.compile(r'[\u3400-\u9fff\u3040-\u30ff\uac00-\ud7af\uff21-\uff3a\uff41-\uff5a\uff10-\uff19]')

def norm(s):
    """numbers -> #, so 'Gold 1234' and 'Gold 7' share one template"""
    out = re.sub(r'[0-9]+(?:[.,][0-9]+)?', '#', s)
    return out

def order_key(s):
    # stable: by length then unicode, gives readable grouping
    return (len(s), s)

content = [s for s in SRC if CONTENT.search(s)]
frag = [s for s in SRC if not CONTENT.search(s)]
print('content keys: %d   dropped fragments: %d' % (len(content), len(frag)))
print('fragments:', json.dumps(frag, ensure_ascii=False))

content.sort(key=order_key)
skel = [{'i': i, 'zh': s, 'en': ''} for i, s in enumerate(content)]
json.dump(skel, open(os.path.join(ROOT, 'Tools', 'l10n_keys.json'), 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)

# collision check on normalised form
tmpl = {}
for s in content:
    n = norm(s)
    tmpl.setdefault(n, []).append(s)
coll = {k: v for k, v in tmpl.items() if len(v) > 1}
print('template collisions: %d' % len(coll))
for k, v in list(coll.items())[:40]:
    print('  %s  <- %s' % (k, ' | '.join(v)))
