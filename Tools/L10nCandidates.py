# -*- coding: utf-8 -*-
"""Dump non-log CJK literals (candidate player-visible) grouped by file."""
import json, os, re
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCAN_ROOTS = [os.path.join(ROOT, 'Assets', 'Scripts'), os.path.join(ROOT, 'Assets', 'Editor')]
CJK = re.compile(r'[\u3400-\u9fff\uf900-\ufaff\u3000-\u303f\uff00-\uffef]')
DEV_FILE = re.compile(r'/Probe/|/Debug/|/Automation/|/Tests/|SelfCheck|DeathDiagnostics|RunBot|RunSimulator|GfxProbe|ProbeV|ProbePopups')

LOG_HINT = re.compile(r'Debug\.Log|LogError|LogWarning|NLog|\bTrace\b|Assert\.|Console\.|print\s*\(|SelfCheck|Report\s*\(')

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

LIT = re.compile(r'(?P<pre>[@$]*)"(?P<body>(?:[^"\\\n]|\\.|"")*)"')

def stmt_bounds(code, pos):
    a = max(code.rfind(';', 0, pos), code.rfind('{', 0, pos), code.rfind('}', 0, pos))
    cands = [x for x in (code.find(';', pos), code.find('}', pos)) if x >= 0]
    b = min(cands) if cands else len(code)
    return code[a+1:b]

def main():
    keep = defaultdict(list)
    for base in SCAN_ROOTS:
        for dp, _, fns in os.walk(base):
            for fn in fns:
                if not fn.endswith('.cs'): continue
                p = os.path.join(dp, fn)
                rel = os.path.relpath(p, ROOT).replace('\\', '/')
                if DEV_FILE.search(rel): continue
                src = strip_comments(open(p, encoding='utf-8-sig', errors='replace').read())
                for m in LIT.finditer(src):
                    body = m.group('body')
                    if not CJK.search(body): continue
                    if LOG_HINT.search(stmt_bounds(src, m.start())): continue
                    keep[rel].append((src.count('\n', 0, m.start()) + 1, body))
    tot = sum(len(v) for v in keep.values())
    dis = set(b for v in keep.values() for _, b in v)
    print('NON-DEV, NON-LOG CJK literals: files=%d occ=%d distinct=%d' % (len(keep), tot, len(dis)))
    for f, v in sorted(keep.items(), key=lambda kv: -len(kv[1])):
        print('%5d  %s' % (len(v), f))
    json.dump({k: v for k, v in keep.items()},
              open(os.path.join(ROOT, 'Tools', 'l10n_candidates.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)

if __name__ == '__main__':
    main()
