# -*- coding: utf-8 -*-
"""v247：按"这条字的值最终喂给谁"判上屏（替掉 v246 的"文件里有绘制出口就算"）。

判据（父任务口径）：
  · 喂给 `GUI.Label` / `GUILayout.Label` / `_status` / `SetPrompt(...)` / `TextMesh.text` / `TownHud.Text(...)`
    ⇒ **上屏** ✓ 收
  · 喂给 `sb.Append(...)`，**且** 那个 `sb` 后来交给 `Log.Info / Debug.Log` ⇒ **报表** ✗ 不收
  · 喂给 `sb.Append(...)`，**且** 那个 `sb` 后来赋给 `_status`（或别的屏幕字段）⇒ **上屏** ✓ 收（追一层）

用法：`python Tools/L10nScreen.py Login` → 只列 LoginBootstrap 那批。
"""
import json, os, re, sys
from collections import defaultdict
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MARK = '\u00a7'
DIG = re.compile(r'[0-9]+(?:[.,][0-9]+)?')
CJK = re.compile(r'[\u3400-\u9fff]')
LIT = re.compile(r'(?P<pre>[@$]*)"(?P<body>(?:[^"\\\n]|\\.|"")*)"')

# 屏幕出口（值最终落到这里就是上屏）
SCREEN = re.compile(
    r'GUI\.Label\(|GUILayout\.Label\(|GUI\.Button\(|GUILayout\.Button\('
    r'|_status\s*=|_actHint\s*=|SetPrompt\s*\(|\.text\s*=\s*[^;]*$'
    r'|TownHud\.(Text|TextWrap|Button|DrawHint)\s*\(|Hud\w*\.Text\s*\('
    r'|DrawHint\s*\(|Wrap\s*\(')
# 报表出口
REPORT = re.compile(r'Log\.Info|Log\.Warn|Log\.Error|Debug\.Log|Debug\.LogWarning|Debug\.LogError|Assert\.')
APPEND = re.compile(r'(\w+)\.Append\s*\(')
# ★ v249 **DEV 标签**：探针 / 诊断面板（`_probeLines`、`Probe*`、`Debug/`）虽然画在屏幕上，
#   但它是**开发者读数**（与 F1 调试面、`GfxProbe` 同类）⇒ **不进词表、不计欠账** ✓
DEV_FILE = re.compile(r'/Probe/|/Debug/|Probe[A-Z]\w*\.cs|GfxProbe\.cs|OutlineProbe\.cs'
                      r'|OutlineAudit\.cs|DeathDiagnostics\.cs|SelfCheck\.cs|DebugApi\.cs'
                      r'|RunBot\.cs|RunSimulator\.cs')
DEV_SINK = re.compile(r'_probeLines|\.Probe[A-Z]\w*\s*\(')
# 包在"探针/诊断方法"里的字符串也算 DEV（`RefreshProbe` / `Diagnose` / `Dump…`）
DEV_METHOD = re.compile(r'Probe|Diagnos|Report|Dump|SelfCheck|Assert', re.I)
METHOD_DEF = re.compile(r'(?:void|string|int|float|double|bool)\s+(\w+)\s*\(')


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
known = {e['z'] for e in zh['entries'] if e['z']}

want = (sys.argv[1] if len(sys.argv) > 1 else '').lower()
buckets = defaultdict(list)          # 'screen' / 'report' / 'unclear'

for base in ('Assets/Scripts', 'Assets/Editor'):
    for dp, _dn, fns in os.walk(os.path.join(ROOT, base)):
        for fn in fns:
            if not fn.endswith('.cs'):
                continue
            rel = os.path.relpath(os.path.join(dp, fn), ROOT).replace('\\', '/')
            if want and want not in rel.lower():
                continue
            raw = open(os.path.join(ROOT, rel), encoding='utf-8-sig', errors='replace').read()
            code = strip_comments(raw)
            for m in LIT.finditer(code):
                body = m.group('body')
                if not CJK.search(body) and '【E】' not in body:
                    continue
                z = shape(body)
                if z is None or z in known:
                    continue
                st = stmt_of(code, m.start())
                ln = code.count('\n', 0, m.start()) + 1

                # ★ DEV 优先判：探针/诊断面板的值 —— 画在屏幕上但**不是给玩家看的**
                if DEV_FILE.search(rel) or DEV_SINK.search(st):
                    buckets['dev'].append((rel, ln, body))
                    continue
                _mm = None
                for _m in METHOD_DEF.finditer(code, 0, m.start()):
                    _mm = _m.group(1)
                if _mm and DEV_METHOD.search(_mm):
                    buckets['dev'].append((rel, ln, body))
                    continue

                if SCREEN.search(st) and not REPORT.search(st):
                    buckets['screen'].append((rel, ln, body))
                    continue
                ap = APPEND.search(st)
                if ap:
                    var = ap.group(1)
                    # 追一层：这个 StringBuilder 后来交给谁？
                    tail = code[m.start():]
                    if re.search(re.escape(var) + r'\.ToString\(\)', tail):
                        # 找那句把 ToString() 赋给谁
                        for am in re.finditer(re.escape(var) + r'\.ToString\(\)[\s\S]{0,80}', tail):
                            frag = am.group(0)
                            # ★ 追到 `_probeLines = …` ⇒ 探针面板 ⇒ DEV（不计欠账）
                            if re.search(r'_probeLines', frag):
                                buckets['dev'].append((rel, ln, body))
                                break
                            if SCREEN.search(frag):
                                buckets['screen'].append((rel, ln, body))
                                break
                            if REPORT.search(frag):
                                buckets['report'].append((rel, ln, body))
                                break
                        else:
                            buckets['unclear'].append((rel, ln, body))
                        continue
                    if REPORT.search(st):
                        buckets['report'].append((rel, ln, body))
                        continue
                if REPORT.search(st):
                    buckets['report'].append((rel, ln, body))
                else:
                    buckets['unclear'].append((rel, ln, body))

print('=== %s ===' % (want or '全量'))
for k in ('screen', 'dev', 'report', 'unclear'):
    print('%-8s %4d 处' % (k, len(buckets[k])))
print('（`dev` = 探针/诊断读数：画在屏幕上但**不是给玩家看的** ⇒ 不进词表、不计欠账）')
print()
print('—— 上屏候选（按文件）——')
byf = defaultdict(int)
for rel, ln, body in buckets['screen']:
    byf[rel] += 1
for f, n in sorted(byf.items(), key=lambda kv: -kv[1])[:12]:
    print('  %4d  %s' % (n, f))
print()
print('—— 上屏候选前 25 条 ——')
for rel, ln, body in buckets['screen'][:25]:
    print('  %s:%d  %s' % (rel.split('/')[-1], ln, body.replace('\n', '\\n')[:56]))

json.dump({k: v for k, v in buckets.items()},
          open(os.path.join(ROOT, 'Tools', 'l10n_screen.json'), 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)
