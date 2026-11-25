# -*- coding: utf-8 -*-
import json, sys
sys.stdout.reconfigure(encoding='utf-8')
d = json.load(open('Tools/l10n_scan_class.json', encoding='utf-8'))
for f, v in d['ui'].items():
    print('--- ' + f)
    for ln, b in v:
        print('  %5d %s' % (ln, b))
