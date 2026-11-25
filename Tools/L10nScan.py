# -*- coding: utf-8 -*-
"""Scan Assets/Scripts for player-visible Chinese string literals. Read-only."""
import json, os, re, sys
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCAN_ROOTS = [os.path.join(ROOT, 'Assets', 'Scripts'), os.path.join(ROOT, 'Assets', 'Editor')]
CJK = re.compile(r'[\u3400-\u9fff\uf900-\ufaff\u3000-\u303f\uff00-\uffef]')

# strip line/block comments but keep string literals intact
def strip_comments(src):
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i+1] == '/':
            j = src.find('\n', i)
            if j < 0: j = n
            out.append(' ' * (j - i)); i = j
        elif c == '/' and i + 1 < n and src[i+1] == '*':
            j = src.find('*/', i + 2)
            j = n if j < 0 else j + 2
            out.append(''.join('\n' if ch == '\n' else ' ' for ch in src[i:j])); i = j
        elif c == '"' or (c in '@$' and i + 1 < n and src[i+1] == '"'):
            start = i
            verbatim = False
            while src[i] in '@$':
                if src[i] == '@': verbatim = True
                i += 1
            i += 1  # opening quote
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

LIT = re.compile(r'(?P<pre>[@$]*)"(?P<body>(?:[^"\\\n]|\\.|"")*)"')

def literals(src):
    for m in LIT.finditer(src):
        body = m.group('body')
        line = src.count('\n', 0, m.start()) + 1
        yield line, body

def main():
    per_file = defaultdict(list)
    total = 0
    for base in SCAN_ROOTS:
        for dp, _, fns in os.walk(base):
            for fn in fns:
                if not fn.endswith('.cs'): continue
                p = os.path.join(dp, fn)
                raw = open(p, encoding='utf-8-sig', errors='replace').read()
                code = strip_comments(raw)
                hits = [(ln, b) for ln, b in literals(code) if CJK.search(b)]
                if hits:
                    rel = os.path.relpath(p, ROOT).replace('\\', '/')
                    per_file[rel] = hits
                    total += len(hits)
    print('== CS files with CJK string literals ==')
    print('files:', len(per_file), ' literal occurrences:', total)
    distinct = set()
    for f, hits in per_file.items():
        for _, b in hits: distinct.add(b)
    print('distinct literals:', len(distinct))
    print()
    for f, hits in sorted(per_file.items(), key=lambda kv: -len(kv[1])):
        print('%5d  %s' % (len(hits), f))
    json.dump({'perFile': {k: v for k, v in per_file.items()}, 'total': total,
               'distinct': sorted(distinct)},
              open(os.path.join(ROOT, 'Tools', 'l10n_scan_cs.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)

if __name__ == '__main__':
    main()
