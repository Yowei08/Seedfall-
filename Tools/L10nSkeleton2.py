# -*- coding: utf-8 -*-
"""Final key set: prune concatenation fragments + dev-log tags, then emit skeleton."""
import json, os, sys, re
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_keyset.json'), encoding='utf-8'))
CONTENT = re.compile(r'[\u3400-\u9fff\u3040-\u30ff\uac00-\ud7af\uff21-\uff3a\uff41-\uff5a\uff10-\uff19]')
DEV_TAG = re.compile(r'^\[[A-Za-z\u4e00-\u9fff ]+\]')

def fragment(s):
    if not s: return True
    if s[0].isspace() or s[-1].isspace(): return True      # concatenation piece
    if DEV_TAG.match(s): return True                       # dev log tag
    if not CONTENT.search(s): return True                  # pure punctuation
    return False

keep = sorted([s for s in SRC if not fragment(s)], key=lambda s: (len(s), s))
drop = [s for s in SRC if fragment(s)]
print('keep %d / drop %d' % (len(keep), len(drop)))
json.dump([{'i': i, 'zh': s, 'en': ''} for i, s in enumerate(keep)],
          open(os.path.join(ROOT, 'Tools', 'l10n_keys.json'), 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)
json.dump(drop, open(os.path.join(ROOT, 'Tools', 'l10n_dropped.json'), 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)
print('dropped sample:', json.dumps(drop[:25], ensure_ascii=False))
