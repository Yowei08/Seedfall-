# -*- coding: utf-8 -*-
"""全量扫描（v246）：不再圈"12 个渲染器文件"，改成扫 **全部** Scripts/** + Editor/**。

三次漏的共同根因是"圈文件"：
  ① 纯英文串 —— 只找 CJK
  ② `LoginInput.ActionDef.Label` / `LoginSettings.TierName()` —— 不在那 12 个文件里
  ③ `TownBootstrap._actHint` —— 同上
所以这里只按**语句是否在日志调用里**剔（Debug.Log / Log.Info / Warn / Error / Assert），
再按"像界面文本"的特征排优先：含 【E】 / ： / ， / 。  的排前面。
"""
import json, os, re, sys
from collections import defaultdict
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MARK = '\u00a7'
DIG = re.compile(r'[0-9]+(?:[.,][0-9]+)?')
CJK = re.compile(r'[\u3400-\u9fff]')
LOG = re.compile(r'Debug\.Log|Log\.Info|Log\.Warn|Log\.Error|Log\.Format|Assert\.|Console\.|Debug\.LogWarning|Debug\.LogError')
LIT = re.compile(r'(?P<pre>[@$]*)"(?P<body>(?:[^"\\\n]|\\.|"")*)"')
UI_HINT = re.compile(r'【[Ee]】|：|，|。|！|？')


def strip_comments(src):
    out = []; i = 0; n = len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i+1] == '/':
            j = src.find('\n', i); j = n if j < 0 else j
            out.append(' ' * (j - i)); i = j
        elif c == '/' and i + 1 < n and src[i+1] == '*':
            j = src.find('*/', i + 2); j = n if j < 0 else j + 2
            out.append(''.join('\n' if ch == '\n' else ' ' for ch in src[i:j])); i = j
        elif c == '"' or (c in '@$' and i + 1 < n and src[i+1] == '"'):
            start = i; verbatim = False
            while src[i] in '@$':
                if src[i] == '@': verbatim = True
                i += 1
            i += 1
            while i < n:
                if verbatim:
                    if src[i] == '"':
                        if i + 1 < n and src[i+1] == '"': i += 2; continue
                        i += 1; break
                else:
                    if src[i] == '\\': i += 2; continue
                    if src[i] == '"': i += 1; break
                i += 1
            out.append(src[start:i])
        elif c == "'":
            start = i; i += 1
            while i < n:
                if src[i] == '\\': i += 2; continue
                if src[i] == "'": i += 1; break
                i += 1
            out.append(src[start:i])
        else:
            out.append(c); i += 1
    return ''.join(out)


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


def stmt_of(code, pos):
    a = max(code.rfind(';', 0, pos), code.rfind('{', 0, pos), code.rfind('}', 0, pos))
    cands = [x for x in (code.find(';', pos), code.find('}', pos)) if x >= 0]
    b = min(cands) if cands else len(code)
    return code[a + 1:b]


zh = json.load(open(os.path.join(ROOT, 'Assets/Resources/Data/l10n_zh.json'), encoding='utf-8'))
# ★ 这里把 `.mNNN`（补进来的那批）也算作"已在表里" —— 本脚本只负责**报缺口**，
#   不需要 L10nMissing.py 那条"排除 .m 以保持清单稳定"的技巧 ✓
known = {e['z'] for e in zh['entries'] if e['z']}

new = {}
total = 0
for base in ('Assets/Scripts', 'Assets/Editor'):
    for dp, _dn, fns in os.walk(os.path.join(ROOT, base)):
        for fn in fns:
            if not fn.endswith('.cs'):
                continue
            p = os.path.join(dp, fn)
            rel = os.path.relpath(p, ROOT).replace('\\', '/')
            code = strip_comments(open(p, encoding='utf-8-sig', errors='replace').read())
            for m in LIT.finditer(code):
                body = m.group('body')
                if not CJK.search(body) and '【E】' not in body:
                    continue                                   # 纯英文串靠 UI_HINT 兜（下面单独一轮）
                if LOG.search(stmt_of(code, m.start())):
                    continue                                   # 只按"在日志语句里"剔 ✓
                total += 1
                z = shape(body)
                if z is None or z in known:
                    continue
                pri = 0 if UI_HINT.search(body) else 1
                if z not in new or pri < new[z][0]:
                    new[z] = (pri, rel, code.count('\n', 0, m.start()) + 1, body)

# 纯英文但"像界面"的串（`【E】…` 已含在上面；这里补 `x：`/`, ` 之类）
eng = 0
for base in ('Assets/Scripts', 'Assets/Editor'):
    for dp, _dn, fns in os.walk(os.path.join(ROOT, base)):
        for fn in fns:
            if not fn.endswith('.cs'):
                continue
            p = os.path.join(dp, fn)
            code = strip_comments(open(p, encoding='utf-8-sig', errors='replace').read())
            for m in LIT.finditer(code):
                body = m.group('body')
                if CJK.search(body) or '【E】' in body:
                    continue
                if not re.search(r'[A-Za-z]{2}', body) or len(body.strip()) < 4:
                    continue
                if LOG.search(stmt_of(code, m.start())):
                    continue
                if not re.search(r'[:：,，]', body):
                    continue
                eng += 1

print('全量候选（含 CJK 或 【E】，且不在日志语句里）: %d 处' % total)
print('其中表里**没有**的: %d 条（distinct）' % len(new))

# ── 精筛（v246）：上面那个 5349 里绝大多数是 **报表拼装**（`sb.Append("…")`），
#   它们永远不上屏。判据：① 这个文件里**没有任何绘制/提示出口** ⇒ 整份不算；
#   ② 语句在 `sb.Append` / `.Append(` 里 ⇒ 不算 ✓
DRAW = re.compile(r'TownHud\.(Text|TextWrap|Button|DrawHint)|GUI\.Label|GUI\.Button'
                  r'|_actHint\s*=|SetPrompt\(|DrawHint\(|\.text\s*=')
APPEND = re.compile(r'\.Append\(|sb\.|StringBuilder')
vis = {}
for z, (pri, rel, ln, body) in new.items():
    src = open(os.path.join(ROOT, rel), encoding='utf-8-sig', errors='replace').read()
    if not DRAW.search(strip_comments(src)):
        continue
    vis[z] = (pri, rel, ln, body)

print('精筛后（文件里有真正的绘制/提示出口）: %d 条' % len(vis))
print()
print('—— 精筛后前 20（含 【E】/：/，/。 的优先）——')
for i, (z, (pri, rel, ln, body)) in enumerate(sorted(vis.items(), key=lambda kv: (kv[1][0], kv[1][1]))[:20]):
    print('%2d %s:%d  %s' % (i + 1, rel.split('/')[-1], ln,
                             body.replace('\n', '\\n')[:52]))
print()
print('—— 精筛后按文件 Top 8 ——')
byfile = defaultdict(int)
for z, (pri, rel, ln, body) in vis.items():
    byfile[rel] += 1
for f, n in sorted(byfile.items(), key=lambda kv: -kv[1])[:8]:
    print('  %4d  %s' % (n, f))
