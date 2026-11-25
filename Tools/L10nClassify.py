# -*- coding: utf-8 -*-
"""Classify CJK literals: player-visible (drawn) vs developer log/tool. Read-only."""
import json, os, re, sys
from collections import Counter, defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCAN_ROOTS = [os.path.join(ROOT, 'Assets', 'Scripts'), os.path.join(ROOT, 'Assets', 'Editor')]
CJK = re.compile(r'[\u3400-\u9fff\uf900-\ufaff\u3000-\u303f\uff00-\uffef]')

UI_HINT = re.compile(
    r'GUI\.|GUILayout|\bTxt\s*\(|\bText\s*\(|\bTextWrap\s*\(|\bBtn\s*\(|\bButton\s*\('
    r'|Label|\bPopText\b|\bPopup\b|\.text\s*=|SetText|TextMesh|\bSay\s*\(|\bToast\s*\('
    r'|\bHint\b|Bubble|PopText|\bDrawT\b|RichText|\bLM\s*\(|\bRow\s*\(')
LOG_HINT = re.compile(
    r'Debug\.Log|LogError|LogWarning|LogFormat|UnityEngine\.Debug|\bNLog\b|\bTrace\b'
    r'|Assert\.|SelfCheck|Report\s*\(|Diagnose|Console|print\s*\(')
FILE_SKIP = re.compile(
    r'/Probe/|/Debug/|/Automation/|/Tests/|RunBot\.cs|SelfCheck\.cs|DeathDiagnostics\.cs'
    r'|RunSimulator\.cs|/ProbeWeb|GfxProbe|OutlineProbe|ProbeV')

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
    b1 = code.find(';', pos); b2 = code.find('}', pos)
    cands = [x for x in (b1, b2) if x >= 0]
    b = min(cands) if cands else len(code)
    return code[a+1:b]

def main():
    ui = defaultdict(list); dev = defaultdict(list)
    ui_total = dev_total = 0
    for base in SCAN_ROOTS:
        for dp, _, fns in os.walk(base):
            for fn in fns:
                if not fn.endswith('.cs'): continue
                p = os.path.join(dp, fn)
                rel = os.path.relpath(p, ROOT).replace('\\', '/')
                src = strip_comments(open(p, encoding='utf-8-sig', errors='replace').read())
                skipped = bool(FILE_SKIP.search(rel))
                for m in LIT.finditer(src):
                    body = m.group('body')
                    if not CJK.search(body): continue
                    st = stmt_bounds(src, m.start())
                    line = src.count('\n', 0, m.start()) + 1
                    is_ui = bool(UI_HINT.search(st)) and not LOG_HINT.search(st)
                    if skipped:
                        is_ui = False
                    if is_ui:
                        ui[rel].append((line, body)); ui_total += 1
                    else:
                        dev[rel].append((line, body)); dev_total += 1
    du = set(b for v in ui.values() for _, b in v)
    dd = set(b for v in dev.values() for _, b in v)
    print('=== PLAYER-VISIBLE (drawn) ===')
    print('files: %d   occurrences: %d   distinct: %d' % (len(ui), ui_total, len(du)))
    for f, v in sorted(ui.items(), key=lambda kv: -len(kv[1])):
        print('%5d  %s' % (len(v), f))
    print()
    print('=== DEV / LOG / TOOL (excluded) ===')
    print('files: %d   occurrences: %d   distinct: %d' % (len(dev), dev_total, len(dd)))
    for f, v in sorted(dev.items(), key=lambda kv: -len(kv[1]))[:25]:
        print('%5d  %s' % (len(v), f))
    json.dump({'ui': {k: v for k, v in ui.items()}, 'uiTotal': ui_total,
               'uiDistinct': sorted(du), 'devTotal': dev_total, 'devDistinct': len(dd)},
              open(os.path.join(ROOT, 'Tools', 'l10n_scan_class.json'), 'w', encoding='utf-8'),
              ensure_ascii=False, indent=1)

if __name__ == '__main__':
    main()
