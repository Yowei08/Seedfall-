# -*- coding: utf-8 -*-
"""Build the l10n key set: distinct non-log CJK literals from renderer files + strict UI hits."""
import json, os, sys, re
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
cand = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_candidates.json'), encoding='utf-8'))
strict = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_scan_class.json'), encoding='utf-8'))['ui']

RENDERERS = [
    'Assets/Scripts/UI/ImGuiHud.cs',
    'Assets/Scripts/Town/TownHud.cs',
    'Assets/Scripts/Login/LoginHud.cs',
    'Assets/Scripts/UI/BossRewardPopup.cs',
    'Assets/Scripts/Login/LoginLoading.cs',
    'Assets/Scripts/Town/TownBuildings.cs',
    'Assets/Scripts/UI/GardenCursor.cs',
    'Assets/Scripts/Flow/CombatFeedback.cs',
    'Assets/Scripts/Town/TownSkins.cs',
    'Assets/Scripts/Town/TownCodex.cs',
    'Assets/Scripts/Town/TownUnlocks.cs',
    'Assets/Scripts/Town/TownDrops.cs',
]
keys = {}
for f in RENDERERS:
    for ln, b in cand.get(f, []):
        keys.setdefault(b, []).append('%s:%d' % (os.path.basename(f), ln))
for f, v in strict.items():
    for ln, b in v:
        keys.setdefault(b, []).append('%s:%d*' % (os.path.basename(f), ln))

print('key set size (distinct rendered CJK literals): %d' % len(keys))
by_src = {}
for zh, where in keys.items():
    for w in where:
        by_src.setdefault(w.split(':')[0], 0)
        by_src[w.split(':')[0]] += 1
print('by file:', json.dumps(by_src, ensure_ascii=False))
json.dump(sorted(keys.keys()), open(os.path.join(ROOT, 'Tools', 'l10n_keyset.json'), 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)
