# -*- coding: utf-8 -*-
"""列出①要补进表的 4 个文件的缺失字面量（带稳定序号）②en 为空的 206 条数据键。"""
import json, os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MARK = '\u00a7'
DIG = re.compile(r'[0-9]+(?:[.,][0-9]+)?')


def unescape(s):
    out = []; i = 0; n = len(s)
    E = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '\\': '\\', '"': '"', "'": "'"}
    while i < n:
        c = s[i]
        if c == '\\' and i + 1 < n:
            d = s[i + 1]
            if d == 'u' and i + 6 <= n:
                try:
                    out.append(chr(int(s[i + 2:i + 6], 16))); i += 6; continue
                except ValueError:
                    pass
            out.append(E.get(d, d)); i += 2; continue
        out.append(c); i += 1
    return ''.join(out)


def shape(s):
    s = unescape(s)
    out = []; i = 0; n = len(s)
    while i < n:
        c = s[i]
        if c == '{':
            j = s.find('}', i + 1)
            if j < 0:
                return None
            out.append(MARK); i = j + 1; continue
        out.append(c); i += 1
    return DIG.sub(MARK, ''.join(out))


zh = json.load(open(os.path.join(ROOT, 'Assets/Resources/Data/l10n_zh.json'), encoding='utf-8'))
en = json.load(open(os.path.join(ROOT, 'Assets/Resources/Data/l10n_en.json'), encoding='utf-8'))
# ★ 只把"骨架键"算作已进表：`.mNNN` 那批是**本脚本自己补进去的**，
#   若把它们也算进来，第二次运行就会认为"已经不缺了" ⇒ 生成器拿不到这批键 ⇒ 表会掉条目 ✗
shapes = {e['z'] for e in zh['entries'] if e['z'] and '.m' not in e['k']}
C = json.load(open(os.path.join(ROOT, 'Tools/l10n_candidates.json'), encoding='utf-8'))

FILES = ['Assets/Scripts/Flow/RunSession.cs', 'Assets/Scripts/UI/ImGuiHud.cs',
         'Assets/Scripts/Town/TownHud.cs', 'Assets/Scripts/Town/TownUnlocks.cs',
         'Assets/Scripts/Flow/CombatFeedback.cs', 'Assets/Scripts/Town/TownDrops.cs']
out = []
seen = set()
print('=== ① 缺失字面量 ===')
for f in FILES:
    b = os.path.basename(f)
    n = 0
    for ln, t in C.get(f, []):
        z = shape(t)
        if z is None or z in shapes or z in seen:
            continue
        seen.add(z)
        out.append({'k': '%s.m%03d' % (os.path.splitext(b)[0].lower(), len(out)), 'z': z, 'src': '%s:%d' % (b, ln)})
        n += 1
    print('%-22s %3d' % (b, n))
print('合计新增 %d 条' % len(out))
json.dump(out, open(os.path.join(ROOT, 'Tools/l10n_missing.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

print()
print('=== ② en 空值（数据侧）===')
enmap = {e['k']: e['v'] for e in en['entries']}
zhmap = {e['k']: e['z'] for e in zh['entries']}
empty = [k for k in zhmap if not enmap.get(k)]
print('en 空 %d 条' % len(empty))
for k in empty[:6]:
    print('  %-26s %s' % (k, zhmap[k][:50]))
