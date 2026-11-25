# -*- coding: utf-8 -*-
import json, os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
skel = json.load(open(os.path.join(ROOT, 'Tools', 'l10n_keys.json'), encoding='utf-8'))
lo = int(sys.argv[1]); hi = int(sys.argv[2])
for r in skel[lo:hi]:
    print('%d|%s' % (r['i'], r['zh'].replace('\n', '\\n').replace('\t', '\\t')))
