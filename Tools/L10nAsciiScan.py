# -*- coding: utf-8 -*-
"""Find player-visible PURE-ASCII string literals (the CJK-only scan never saw these)."""
import json, os, re, sys
from collections import defaultdict
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FILES = [
    'Assets/Scripts/UI/ImGuiHud.cs', 'Assets/Scripts/Town/TownHud.cs',
    'Assets/Scripts/Login/LoginHud.cs', 'Assets/Scripts/UI/BossRewardPopup.cs',
    'Assets/Scripts/Login/LoginLoading.cs', 'Assets/Scripts/UI/GardenCursor.cs',
    'Assets/Scripts/Flow/CombatFeedback.cs',
]
CJK = re.compile(r'[\u3400-\u9fff\u3040-\u30ff\uac00-\ud7af]')
DRAW = re.compile(r'\b(Text|TextWrap|Wrap|Button|Btn|Choice|Slider|Txt|Label)\s*\(')
LIT = re.compile(r'"((?:[^"\\\n]|\\.)*)"')
LOG = re.compile(r'Debug\.Log|Log\.Info|Log\.Warn|Log\.Error|Assert\.')

seen = defaultdict(set)
for rel in FILES:
    p = os.path.join(ROOT, rel.replace('/', os.sep))
    src = open(p, encoding='utf-8-sig', errors='replace').read()
    for ln, line in enumerate(src.splitlines(), 1):
        if not DRAW.search(line): continue
        if LOG.search(line): continue
        if line.strip().startswith('//'): continue
        for m in LIT.finditer(line):
            s = m.group(1)
            if CJK.search(s): continue
            if len(s.strip()) < 3: continue
            if not re.search(r'[A-Za-z]{2}', s): continue
            seen[s].add('%s:%d' % (os.path.basename(rel), ln))
print('pure-ASCII literals in draw calls: %d distinct' % len(seen))
for s, w in sorted(seen.items()):
    print('  %-52s %s' % (repr(s), ', '.join(sorted(w))[:70]))
