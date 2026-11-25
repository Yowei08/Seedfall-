# -*- coding: utf-8 -*-
"""One-shot fix of the 22 marker-count mismatches found by Tools/L10nGen.py."""
import io, sys
sys.stdout.reconfigure(encoding='utf-8')
M = '\\u00a7'          # escaped form used inside Tools/l10n_en.py
P = 'Tools/l10n_en.py'
s = io.open(P, encoding='utf-8').read()

FIX = [
    ('20: "Drop ' + M + '",',            '20: "Drop #",'),
    ('111: "Plant Slot ' + M + '",',     '111: "Plant Slot #",'),
    ('140: "Sell 1",',                   '140: "Sell ' + M + '",'),
    ('194: "Till 2.0s",',                '194: "Till ' + M + 's",'),
    ('195: "Sow 1.0s",',                 '195: "Sow ' + M + 's",'),
    ('218: "Keep at least 1",',          '218: "Keep at least ' + M + '",'),
    ('262: "Fertilize (growth -50%)",',  '262: "Fertilize (growth -' + M + '%)",'),
    ('264: "Water (growth -30%)",',      '264: "Water (growth -' + M + '%)",'),
    ('282: "Saves \\u00b7 6 slots",',     '282: "Saves \\u00b7 ' + M + ' slots",'),
    ('303: "Click to collect (auto in 2.8s)",', '303: "Click to collect (auto in ' + M + 's)",'),
    ('461: "Luck  ' + M + '",',           '461: "Luck  ' + M + M + '",'),
    ('468: "Dodge  ' + M + '",',          '468: "Dodge  ' + M + M + '",'),
    ('477: "Crit Chance  ' + M + '",',    '477: "Crit Chance  ' + M + M + '",'),
    ('478: "Knockback  ' + M + '",',      '478: "Knockback  ' + M + M + '",'),
    ('480: "Attack Range  ' + M + '",',   '480: "Attack Range  ' + M + M + '",'),
    ('481: "Attack Speed  ' + M + '",',   '481: "Attack Speed  ' + M + M + '",'),
    ('483: "Move Speed  ' + M + '",',     '483: "Move Speed  ' + M + M + '",'),
    ('484: "Blast Damage  ' + M + '",',   '484: "Blast Damage  ' + M + M + '",'),
    ('485: "Melee Damage  ' + M + '",',   '485: "Melee Damage  ' + M + M + '",'),
    ('487: "Ranged Damage  ' + M + '",',  '487: "Ranged Damage  ' + M + M + '",'),
    ('490: "Blast Radius  ' + M + '",',   '490: "Blast Radius  ' + M + M + '",'),
]
for old, new in FIX:
    if s.count(old) != 1:
        print('MISS (%d): %s' % (s.count(old), old)); continue
    s = s.replace(old, new)
io.open(P, 'w', encoding='utf-8', newline='\n').write(s)

print('done')
