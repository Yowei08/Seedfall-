# -*- coding: utf-8 -*-
"""Compare per-file: literals flagged strict-UI vs all non-log literals in renderer files."""
import json, sys, os, re
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
strict = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_scan_class.json'), encoding='utf-8'))['ui']
cand = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_candidates.json'), encoding='utf-8'))
FILES = sys.argv[1:] or ['Assets/Scripts/UI/ImGuiHud.cs']
for f in FILES:
    s = set(b for _, b in strict.get(f, []))
    print('=== ' + f + '   non-log=%d strict=%d' % (len(cand.get(f, [])), len(strict.get(f, []))))
    for ln, b in cand.get(f, []):
        if b not in s:
            print('  %5d | %s' % (ln, b))
