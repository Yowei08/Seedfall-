# -*- coding: utf-8 -*-
"""Generate Assets/Resources/Data/l10n_*.json from the scanned key set + Tools/l10n_en.py.

  zh = full key set (source language)
  en = Tools/l10n_en.py
  ur/ja/ko/ru = the small SEM_* sample, everything else empty (=> runtime falls back)
"""
import json, os, re, sys, io
sys.stdout.reconfigure(encoding='utf-8')
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from l10n_en import EN                       # noqa: E402
from l10n_data import DATA_EN, DATA_JA      # noqa: E402
from l10n_missing_en import MISS_EN          # noqa: E402
from l10n_ja_batch1 import JA_BATCH1         # noqa: E402
from l10n_ja_batch2 import JA_BATCH2         # noqa: E402
from l10n_ja_batch3 import JA_BATCH3         # noqa: E402
from l10n_ja_batch4 import JA_BATCH4         # noqa: E402
from l10n_ja_batch5 import JA_BATCH5         # noqa: E402
from l10n_ja_batch6 import JA_BATCH6         # noqa: E402
from l10n_ja_batch7 import JA_BATCH7         # noqa: E402
from l10n_ja_batch8 import JA_BATCH8         # noqa: E402
from l10n_ja_batch9 import JA_BATCH9         # noqa: E402

MARK = '\u00a7'
ESCAPES = {'n': '\n', 't': '\t', 'r': '\r', '0': '\0', '\\': '\\', '"': '"', "'": "'"}


def unescape(s):
    out = []; i = 0; n = len(s)
    while i < n:
        c = s[i]
        if c == '\\' and i + 1 < n:
            d = s[i + 1]
            if d == 'u' and i + 5 < n + 1:
                try:
                    out.append(chr(int(s[i + 2:i + 6], 16))); i += 6; continue
                except ValueError:
                    pass
            out.append(ESCAPES.get(d, d)); i += 2; continue
        out.append(c); i += 1
    return ''.join(out)


def brace_norm(s):
    """replace every balanced {...} with one MARK; return None when unbalanced"""
    out = []; i = 0; n = len(s); hole = 0
    while i < n:
        c = s[i]
        if c == '{':
            j = s.find('}', i + 1)
            if j < 0:
                return None
            out.append(MARK); i = j + 1; hole += 1; continue
        out.append(c); i += 1
    return ''.join(out)


DIGITS = re.compile(r'[0-9]+(?:[.,][0-9]+)?')
def digit_norm(s):
    return DIGITS.sub(MARK, s)

# ── semantic keys, hand written (these are the ones the code calls by name) ──
SEM = {
    'ui.settings.title':          ('设置', 'Settings'),
    'ui.settings.language':       ('语言 / Language', 'Language / 语言'),
    'ui.settings.langShort':      ('语言', 'Lang'),
    'ui.settings.music':          ('音乐音量', 'Music Volume'),
    'ui.settings.sfx':            ('音效音量', 'SFX Volume'),
    'ui.settings.quality':        ('画质（实时改阴影 / 抗锯齿 / URP 渲染缩放）', 'Quality (shadows / AA / URP render scale)'),
    'ui.settings.fpscap':         ('帧率上限（关垂直同步）', 'Frame Rate Cap (vsync off)'),
    'ui.settings.resolution':     ('分辨率', 'Resolution'),
    'ui.settings.displaymode':    ('显示模式', 'Display Mode'),
    'ui.settings.windowed':       ('窗口', 'Windowed'),
    'ui.settings.fullscreen':     ('全屏', 'Fullscreen'),
    'ui.settings.bindings':       ('按键绑定 —— 主城 / 战斗 各一套', 'Key Bindings — town / combat'),
    'ui.settings.close':          ('关闭 (Esc)', 'Close (Esc)'),
    'ui.settings.backtitle':      ('返回开始界面', 'Back to Title'),
    'ui.login.title':             ('守园者 · 主城登录', 'Seedfall · Town Login'),
    'ui.login.continue':          ('继续游戏', 'Continue Game'),
    'ui.login.newgame':           ('新的游戏', 'New Game'),
    'ui.login.saves':             ('存档查看（6 位）', 'Saves (\u00a7 slots)'),
    'ui.login.quit':              ('退出游戏', 'Quit Game'),
    'hud.gold':                   ('金币', 'Gold'),
    'hud.goldOf':                 ('主城金币', 'Town Gold'),
    'hud.close':                  ('关闭 (E)', 'Close (E)'),
    'hud.buy':                    ('购买', 'Buy'),
    'hud.sell':                   ('出售', 'Sell'),
    'hud.unequip':                ('卸下', 'Unequip'),
    'hud.wear':                   ('穿上', 'Wear'),
    'hud.unlock':                 ('解锁', 'Unlock'),
    'hud.unlocked':               ('已解锁', 'Unlocked'),
    'hud.notEnoughGold':          ('金币不足', 'Not enough gold'),
    'combat.shop':                ('商店', 'Shop'),
    'combat.weapons':             ('武器（三把）', 'Weapons (3)'),
    'combat.stats':               ('角色属性', 'Character Stats'),
    'combat.death':               ('守园失败', 'Garden Lost'),
    'combat.restart':             ('重新开始', 'Restart'),
    'combat.backToTown':          ('返回主城', 'Back to Town'),
    'combat.continue':            ('继续游戏（Esc）', 'Resume Game (Esc)'),
    'combat.leaveCombat':         ('退出战斗（回到主城）', 'Leave Combat (back to town)'),
    # ── 飘字（`FloatingTextEvent` → `RunSession.OnFloatingText` 那个唯一漏斗）──
    #   这些字是**发布端拼出来的**（`"金币+" + 数值`），源码里没有整串字面量可扫，
    #   所以只能手写；`\u00a7` 就是"那里有个数字"的占位（与运行时归一规则同一口径）。
    'popup.gold':                 ('金币+\u00a7', 'Gold +\u00a7'),
    'popup.goldShort':            ('金+\u00a7', 'G +\u00a7'),
    'popup.exp':                  ('经验值+\u00a7', 'EXP +\u00a7'),
    'popup.ripe':                 ('成熟', 'Ripe'),
    'popup.thirst':               ('缺水', 'Dry'),
    # ── 设置面板里那批**不是渲染器文件里写的**字（v238 补）──
    #   键位名来自 `Login/LoginInput.cs` 的 `ActionDef.Label`、画质档名来自
    #   `Login/LoginSettings.TierName()` —— 都在 CJK 扫描的"12 个渲染器文件"之外，
    #   所以第一轮词表里没有它们 ⇒ 设置面板会留一列中文。这里按"原文当键"补齐 ✓
    'bind.moveUp':                ('向上移动', 'Move Up'),
    'bind.moveDown':              ('向下移动', 'Move Down'),
    'bind.moveLeft':              ('向左移动', 'Move Left'),
    'bind.moveRight':             ('向右移动', 'Move Right'),
    'bind.interactTown':          ('交互 / 种植', 'Interact / Plant'),
    'bind.attackTown':            ('攻击（主城无战斗 · 占位）', 'Attack (no combat in town)'),
    'bind.switchWeapon':          ('切换武器', 'Switch Weapon'),
    'bind.settings':              ('设置（Esc）', 'Settings (Esc)'),
    'bind.interactCombat':        ('交互 / 商店', 'Interact / Shop'),
    'bind.attack':                ('攻击', 'Attack'),
    'bind.menu':                  ('菜单 / 暂停（Esc）', 'Menu / Pause (Esc)'),
    'bind.reroll':                ('商店刷新 / 重抽（R）', 'Reroll Shop / Redraw (R)'),
    'bind.confirm':               ('跳过 / 继续 / 确认（空格）', 'Skip / Continue / Confirm (Space)'),
    'bind.restart':               ('重开一局（Shift + R）', 'Restart Run (Shift + R)'),
    'bind.keyhelp':               ('按键说明开关（Tab）', 'Toggle Controls (Tab)'),
    'ui.settings.tierLow':        ('低', 'Low'),
    'ui.settings.tierMid':        ('中', 'Medium'),
    'ui.settings.tierHigh':       ('高', 'High'),
    # ── 单位形状（v241）：这些字面上是**碎片**（` 金` / ` 颗` / ` 级`），
    #   真正常出现的是**拼好之后**的 `120 金`、`3 颗`、`5 级` ⇒ 用 `§` 占位写形状，
    #   一条就覆盖所有"数字 + 单位"的拼接结果 ✓（比逐条翻碎片可靠得多）
    'unit.gold':                  ('\u00a7 金', '\u00a7 gold'),
    'unit.seeds':                 ('\u00a7 颗', '\u00a7 seeds'),
    'unit.level':                 ('\u00a7 级', 'Lv \u00a7'),
    'unit.items':                 ('\u00a7 件', '\u00a7 items'),
    'unit.seconds':               ('\u00a7 秒', '\u00a7 s'),
    # ── 图鉴页签（v242）：`CodexTabButton` 画的是 `label + " " + n`（例如「植物 22」），
    #   单看「植物」在表里、拼上数字就不在 ⇒ 截图里就是"两个英文夹三个中文"。
    #   用 `§` 把"名字 + 数字"整条写成一个形状 ✓
    'codex.tabPlants':            ('植物 \u00a7', 'Plants \u00a7'),
    'codex.tabItems':             ('道具 \u00a7', 'Items \u00a7'),
    'codex.tabPlugins':           ('插件 \u00a7', 'Plugins \u00a7'),
    # ── 底部交互提示（v245）：`TownBootstrap.UpdateActTarget` 里的 `_actHint` ──
    #   这批字写在 `Town/TownBootstrap.cs`，**不在那 12 个渲染器文件里** ⇒ 从来没进过表，
    #   截图里"【E】走进传送门 · 回战斗里打 BOSS 掉种子"一直是中文就是这个原因 ✗
    #   `【E】` 前缀原样保留（它是按键提示的视觉符号，不是词）✓
    'hint.warehouse':             ('【E】打开种子仓库', '【E】Open Seed Warehouse'),
    'hint.warehouseEmpty':        ('【E】打开种子仓库（空着 —— 种子要靠打 BOSS 掉落）',
                                   '【E】Open Seed Warehouse (empty — seeds drop from BOSSes)'),
    'hint.portal':                ('【E】走进传送门 · 回战斗里打 BOSS 掉种子',
                                   '【E】Enter the portal · beat BOSSes in combat for seeds'),
    'hint.shop':                  ('【E】杂货商店 · 道具 / 插件 / 植物（不卖种子）',
                                   '【E】General Store · items / plugins / plants (no seeds)'),
    'hint.clothing':              ('【E】服装店 · 皮肤', '【E】Clothing Shop · skins'),
    'hint.home':                  ('【E】回家 · 查看图鉴（植物 / 道具 / 插件 / 成就）',
                                   '【E】Home · Codex (plants / items / plugins / achievements)'),
    'hint.plantHere':             ('【E】在这块空地种下种子', '【E】Plant a seed on this empty plot'),
    'hint.noSeed':                ('仓库里没有种子 —— 先回战斗里打 BOSS 掉种子（商店不卖）',
                                   'No seeds in storage — beat BOSSes in combat first (the shop sells none)'),
    # ── 设置面板底部状态行（v248）—— "前缀 + 运行期值"，形状表盖不住 ⇒ 在调用点显式接
    'status.rebound':             ('改键完成：', 'Rebind done: '),
    'status.audio':               ('音量已应用：', 'Volume applied: '),
    'status.quality':             ('画质已应用：', 'Quality applied: '),
    'status.fps':                 ('帧率已应用：', 'Frame rate applied: '),
    'status.resolution':          ('分辨率已应用：', 'Resolution applied: '),
    'status.bindReset':           ('按键已恢复默认：', 'Bindings reset to default: '),
    'status.saved':               ('已保存 → 存档位 {0}（{1}）', 'Saved → slot {0} ({1})'),
    # ── 带变量的三处（v250）：**先翻变量，再套格式** —— 整串去表里找是找不到的 ✗ ──
    'login.slotNo':               ('存档位 {0}', 'Slot {0}'),
    'hint.harvestNow':            ('【E】收获（现在：{0} · 已成熟，收走 + 种子回仓库）',
                                   '【E】Harvest (now: {0} · ripe — take it, the seed goes back to storage)'),
    'hint.replaceCrop':           ('【E】更换种植物（现在：{0} · {1}/{2} 秒）',
                                   '【E】Change crop (now: {0} · {1}/{2} s)'),
    # ── 战斗结算 / 死亡界面那三行（v251）：带变量 ⇒ 调用点 `T(key, args)` ──
    'result.line1':               ('坚持 {0}（第 {1} 分钟）　击杀 {2}', 'Survived {0} (minute {1})　kills {2}'),
    'result.line2':               ('金币 {0}　等级 {1}', 'Gold {0}　Level {1}'),
    'result.line3':               ('植物 {0}/{1} 株　耕地 {2} 格', 'Plants {0}/{1}　Plots {2}'),
}

# small hand sample for the four fallback languages (proves partial fill works)
SEM_JA = {
    'ui.settings.title': '設定', 'ui.settings.language': '言語 / Language',
    'ui.settings.music': '音楽音量', 'ui.settings.sfx': '効果音音量',
    'ui.settings.close': '閉じる (Esc)', 'ui.login.continue': 'ゲームを続ける',
    'ui.login.newgame': 'はじめから', 'ui.login.quit': 'ゲームを終了',
    'hud.gold': 'ゴールド', 'hud.close': '閉じる (E)', 'hud.buy': '購入',
    'hud.sell': '売却', 'hud.unlock': '解放', 'hud.unlocked': '解放済み',
    'hud.notEnoughGold': 'ゴールド不足', 'combat.shop': 'ショップ',
    'combat.death': '庭の終わり', 'combat.restart': 'リスタート', 'combat.backToTown': '町へ戻る',
    # ── v239：把 66 条具名键的 ja 补齐（界面可见度最高的那一批）──
    'ui.settings.langShort': '言語',
    'ui.settings.subtitle': '変更は即時反映され、settings.json に書き込まれます。',
    'ui.settings.quality': '画質（影 / AA / URP レンダースケールを即時変更）',
    'ui.settings.fpscap': 'フレームレート上限（垂直同期オフ）',
    'ui.settings.resolution': '解像度',
    'ui.settings.displaymode': '表示モード',
    'ui.settings.windowed': 'ウィンドウ',
    'ui.settings.fullscreen': 'フルスクリーン',
    'ui.settings.bindings': 'キー割り当て — 町 / 戦闘 各1セット（Input System）',
    'ui.settings.backtitle': 'タイトルへ戻る',
    'ui.login.title': 'シードフォール · 町のログイン',
    'ui.login.saves': 'セーブを見る（\u00a7 枠）',
    'hud.goldOf': '町のゴールド',
    'hud.unequip': '外す', 'hud.wear': '着ける',
    'combat.weapons': '武器（3本）', 'combat.stats': 'キャラクター属性',
    'combat.continue': 'ゲームを再開（Esc）', 'combat.leaveCombat': '戦闘を抜ける（町へ戻る）',
    'popup.gold': 'ゴールド+\u00a7', 'popup.goldShort': '金+\u00a7',
    'popup.exp': '経験値+\u00a7', 'popup.ripe': '成熟', 'popup.thirst': '水切れ',
    'bind.moveUp': '上へ移動', 'bind.moveDown': '下へ移動',
    'bind.moveLeft': '左へ移動', 'bind.moveRight': '右へ移動',
    'bind.interactTown': '調べる / 植える', 'bind.attackTown': '攻撃（町に戦闘なし・仮）',
    'bind.switchWeapon': '武器を持ち替え', 'bind.settings': '設定（Esc）',
    'bind.interactCombat': '調べる / 店', 'bind.attack': '攻撃',
    'bind.menu': 'メニュー / 一時停止（Esc）',
    'bind.reroll': '店の更新 / 引き直し（R）',
    'bind.confirm': 'スキップ / 続ける / 決定（スペース）',
    'bind.restart': 'やり直し（Shift + R）',
    'bind.keyhelp': '操作説明の表示切替（Tab）',
    'ui.settings.tierLow': '低', 'ui.settings.tierMid': '中', 'ui.settings.tierHigh': '高',
    'codex.tabPlants': '植物 \u00a7', 'codex.tabItems': '道具 \u00a7', 'codex.tabPlugins': 'プラグイン \u00a7',
    'hint.warehouse': '【E】種の倉庫を開く',
    'hint.warehouseEmpty': '【E】種の倉庫を開く（空 —— 種は BOSS 討伐でドロップ）',
    'hint.portal': '【E】門をくぐる · 戦闘に戻って BOSS を倒すと種が落ちる',
    'hint.shop': '【E】雑貨店 · 道具 / プラグイン / 植物（種は売らない）',
    'hint.clothing': '【E】衣装店 · スキン',
    'hint.home': '【E】家に帰る · 図鑑を見る（植物 / 道具 / プラグイン / 実績）',
    'hint.plantHere': '【E】この空きに種を植える',
    'hint.noSeed': '倉庫に種がありません —— 先に戦闘で BOSS を倒して種を集めてください（店では売りません）',
    'status.rebound': 'キー変更が完了：',
    'status.audio': '音量を適用しました：',
    'status.quality': '画質を適用しました：',
    'status.fps': 'フレームレートを適用しました：',
    'status.resolution': '解像度を適用しました：',
    'status.bindReset': 'キーを初期設定に戻しました：',
    'status.saved': '保存しました → スロット {0}（{1}）',
    'login.lockBar': '背景 = 稼働中の町（カメラ固定 · 入力オフ · キャラは展示のみ）・保存先 ',
    'login.slotNo': 'セーブ枠 {0}',
    'hint.harvestNow': '【E】収穫（現在：{0} · 成熟済み、収穫すると種が倉庫に戻る）',
    'hint.replaceCrop': '【E】作物を替える（現在：{0} · {1}/{2} 秒）',
    'result.line1': '生存 {0}（{1} 分）　撃破 {2}',
    'result.line2': 'ゴールド {0}　レベル {1}',
    'result.line3': '植物 {0}/{1} 株　耕地 {2} マス',
}
# ★ v242：`SEM_KO / SEM_RU / SEM_UR` 已删（那三门语言整门砍掉）。
#   要恢复时按 `SEM_JA` 的形状补一份即可 —— 键名与中文原文都在 `l10n_zh.json` 里 ✓

_cand = json.load(open(os.path.join(HERE, 'l10n_candidates.json'), encoding='utf-8'))
keymeta = {}
for _f, _v in _cand.items():
    for _ln, _t in _v:
        keymeta.setdefault(_t, []).append('%s:%d' % (os.path.basename(_f), _ln))
skel = json.load(open(os.path.join(HERE, 'l10n_keys.json'), encoding='utf-8'))

rows = []            # (key, zh_shape, en_value)
bad = []
seen_shape = {}

# semantic first so they win the shape index
#
# ★ v252：**两套占位符必须分清** ★
#   · `S(原文)` 形状表：占位符是 `§`，由 `L10n` 自己替换；
#   · `T(key, args)` 具名键：占位符是 `{0} {1}`，由 `string.Format` 替换。
#   ⇒ 具名键的模板**绝不能过 `digit_norm`**：那会把 `{0}` 的 0 变成 `{§}`，
#     `string.Format` 认不出 ⇒ 屏幕上原样印出 `{§}`（死亡界面就是这么翻车的）✗
FORMAT_KEY = re.compile(r'\{\d+\}')
format_keys = []
for k, (zh, en) in SEM.items():
    if FORMAT_KEY.search(zh):
        z = unescape(zh)                      # 原样保留 {0} {1} {2}
        format_keys.append(k)
        rows.append([k, z, en])
        continue
    z = digit_norm(unescape(zh))
    seen_shape[z] = k
    rows.append([k, z, en])
print('format keys (T + string.Format，模板用 {0}): %d → %s'
      % (len(format_keys), ', '.join(sorted(format_keys))))

for r in skel:
    raw = r['zh']
    b = brace_norm(raw)
    if b is None:
        bad.append(raw); continue
    z = digit_norm(b)
    if z in seen_shape:
        continue
    src = (keymeta.get(raw) or ['ui.cs:0'])[0].split(':')[0]
    stem = re.sub(r'[^a-z0-9]', '', os.path.splitext(src)[0].lower()) or 'ui'
    key = '%s.%03d' % (stem, r['i'])
    seen_shape[z] = key
    rows.append([key, z, EN.get(r['i'], '')])

# ── 数据侧（v239）：Resources/Data/*.json 里的 name / desc ────────────────
#   为什么必须有这一块：商店/图鉴里玩家最常看的就是**道具名/种子名/插件名**，
#   而它们从来没进过词表（第一轮的 CJK 扫描只取"12 个渲染器文件"里的字面量）✗
#   键名按父任务的口径：`name.<id>` / `desc.<id>`。
#   ⚠️ 它们仍然靠**形状索引**命中（`L10n.S` 在绘制漏斗里过表）—— 这样不用去改几十处读表点；
#      同时 `T("name.<id>")` 也照样能用，两条路都留着 ✓
DATA_CARDS = ['town_unlocks.json', 'town_seeds.json', 'town_skins.json', 'enemies.json',
              'items.json', 'plugins.json', 'seeds.json']
DATA_DIR = os.path.join(ROOT, 'Assets', 'Resources', 'Data')
CJK = re.compile(r'[\u3400-\u9fff]')
seen_keys = set(k for k, _z, _v in rows)


def collect_data():
    n = 0
    for fn in DATA_CARDS:
        p = os.path.join(DATA_DIR, fn)
        if not os.path.exists(p):
            continue
        try:
            obj = json.load(open(p, encoding='utf-8-sig'))
        except Exception as e:                       # town_shop.json 就是坏的（既有问题）
            print('  !! %-20s 不是合法 JSON：%s' % (fn, e))
            continue

        def walk(o):
            nonlocal n
            if isinstance(o, dict):
                i = o.get('id')
                if isinstance(i, str):
                    for field, pre in (('name', 'name.'), ('desc', 'desc.'), ('description', 'desc.')):
                        v = o.get(field)
                        if not isinstance(v, str) or not CJK.search(v):
                            continue
                        key = pre + i
                        if key in seen_keys:
                            continue
                        b = brace_norm(unescape(v))
                        if b is None:
                            continue
                        z = digit_norm(b)
                        if z in seen_shape:          # 与界面串同形 ⇒ 共用那一条，不重复建键
                            continue
                        seen_shape[z] = key
                        seen_keys.add(key)
                        rows.append([key, z, DATA_EN.get(key, '')])
                        n += 1
                for v in o.values():
                    walk(v)
            elif isinstance(o, list):
                for v in o:
                    walk(v)

        walk(obj)
    return n


print('data-card keys added: %d' % collect_data())

# ── v240：把"压根没进表"的 HUD 提示补进表（本轮只做 RunSession 那 95 条）──────
#   为什么先进表：这些是 `SetPrompt` 的提示语，走 `ImGuiHud.Txt → L10n.S()`，
#   但表里没有它们的形状 ⇒ 切任何语言都还是中文。进表之后至少能回退到 En ✓
MISSING_JSON = os.path.join(HERE, 'l10n_missing.json')
MISSING_PREFIX = ('runsession.', 'imguihud.', 'townhud.', 'townunlocks.',
                  'combatfeedback.', 'towndrops.')
missing_added = 0
if os.path.exists(MISSING_JSON):
    for it in json.load(open(MISSING_JSON, encoding='utf-8')):
        k, z = it['k'], it['z']
        if not k.startswith(MISSING_PREFIX):
            continue
        if z in seen_shape or k in seen_keys:
            continue
        seen_shape[z] = k
        seen_keys.add(k)
        rows.append([k, z, MISS_EN.get(k, '')])
        missing_added += 1
print('missing-literal keys added: %d' % missing_added)

# ── v243：`desc.*` 按**句子**拼英文 ──────────────────────────────────────
#   129 条 desc 只由 376 个不同的句子组成（最高频「触发：购买后一直生效。」出现 73 次）
#   ⇒ 逐句翻省 2/3 工作量，§ 也按句对齐。
#   ★ 任一句缺 ⇒ **整条留空**（回退中文）⇒ 不会出现"半条英文半条中文" ✓
from l10n_clauses_en import CLAUSE_EN        # noqa: E402
import hashlib                               # noqa: E402


def _h(s):
    return hashlib.md5(s.encode('utf-8')).hexdigest()[:8]


def _clauses(s):
    out = []
    cur = []
    for ch in s:
        cur.append(ch)
        if ch == '\u3002':
            out.append(''.join(cur)); cur = []
    if cur:
        out.append(''.join(cur))
    return out


_all_clauses = set()
for _row in rows:
    if _row[0].startswith('desc.'):
        _all_clauses.update(_clauses(_row[1]))

_clause_bad = []
for _c in sorted(_all_clauses):
    _e = CLAUSE_EN.get(_h(_c))
    if _e is not None and _c.count(MARK) != _e.count(MARK):
        _clause_bad.append((_h(_c), _c.count(MARK), _e.count(MARK), _c))

_desc_done = 0
_desc_left = 0
for _row in rows:
    if not _row[0].startswith('desc.') or _row[2]:
        continue
    _cl = _clauses(_row[1])
    if all(_h(c) in CLAUSE_EN for c in _cl):
        # 句与句之间补一个空格：中文靠「。」断句不需要空格，英文句号后必须有一个 ✓
        _row[2] = ' '.join(CLAUSE_EN[_h(c)].strip() for c in _cl)
        _desc_done += 1
    else:
        _desc_left += 1
print('clause EN: %d/%d 句已翻   desc 整条可拼出: %d / %d'
      % (len(CLAUSE_EN), len(_all_clauses), _desc_done, _desc_done + _desc_left))
if _clause_bad:
    print('  !! 句级 § 不匹配 %d 条：' % len(_clause_bad))
    for _x in _clause_bad[:10]:
        print('     %s zh=%d en=%d  %s' % (_x[0], _x[1], _x[2], _x[3][:44]))

# ── v244：同一张句子表的**日文列**（`CLAUSE_JA`，键与 en 完全同一份）──────
from l10n_clauses_ja import CLAUSE_JA        # noqa: E402

_ja_bad = []
for _c in sorted(_all_clauses):
    _e = CLAUSE_JA.get(_h(_c))
    if _e is not None and _c.count(MARK) != _e.count(MARK):
        _ja_bad.append((_h(_c), _c.count(MARK), _e.count(MARK), _c))

DESC_JA = {}
_ja_done = 0
_ja_left = 0
for _row in rows:
    if not _row[0].startswith('desc.'):
        continue
    _cl = _clauses(_row[1])
    if all(_h(c) in CLAUSE_JA for c in _cl):
        # 日文不像英文那样需要空格：句末的「。」本身就是分隔符，直接接起来 ✓
        DESC_JA[_row[0]] = ''.join(CLAUSE_JA[_h(c)].strip() for c in _cl)
        _ja_done += 1
    else:
        _ja_left += 1
print('clause JA: %d/%d 句已翻   desc 整条可拼出: %d / %d'
      % (len(CLAUSE_JA), len(_all_clauses), _ja_done, _ja_done + _ja_left))
if _ja_bad:
    print('  !! JA 句级 § 不匹配 %d 条：' % len(_ja_bad))
    for _x in _ja_bad[:10]:
        print('     %s zh=%d ja=%d  %s' % (_x[0], _x[1], _x[2], _x[3][:44]))

# verify marker counts
problems = []
for k, z, v in rows:
    if not v:
        continue
    if z.count(MARK) != v.count(MARK):
        problems.append((k, z, v, z.count(MARK), v.count(MARK)))

def table(lang, values, sample=None):
    entries = []
    for k, z, v in rows:
        if sample is not None:
            val = sample.get(k, '')
        elif isinstance(values, dict) and values:
            val = values.get(k, '')
        elif lang == 'zh':
            val = z                      # zh carries the shape itself (source of truth)
        else:
            val = v
        entries.append({'k': k, 'z': z if lang == 'zh' else '', 'v': val})
    return {'lang': lang, 'version': 1, 'count': len(entries), 'entries': entries}

en_map = {k: v for k, v in ((k, v) for k, z, v in rows)}
OUT = os.path.join(ROOT, 'Assets', 'Resources', 'Data')
files = {
    'zh': table('zh', {}),
    'en': table('en', None),
    'ja': table('ja', None, dict(list(SEM_JA.items()) + list(DATA_JA.items())
                                 + list(JA_BATCH1.items()) + list(JA_BATCH2.items())
                                 + list(JA_BATCH3.items()) + list(JA_BATCH4.items())
                                 + list(JA_BATCH5.items()) + list(JA_BATCH6.items()) + list(JA_BATCH7.items()) + list(JA_BATCH8.items()) + list(JA_BATCH9.items())
                                 + list(DESC_JA.items()))),
}
# ★ v242：`ur / ru / ko` 三份**已删**（各只有 19 条样例、其余全靠回退 ⇒
#   用户切过去看到的是英文，比"没这门语言"更糟 ✗）。要恢复就把它们加回这个 dict ✓
for code, data in files.items():
    p = os.path.join(OUT, 'l10n_%s.json' % code)
    with io.open(p, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
        f.write('\n')
    filled = sum(1 for e in data['entries'] if e['v'])
    print('%-4s -> %-52s keys=%d filled=%d missing=%d' %
          (code, os.path.relpath(p, ROOT), len(data['entries']), filled, len(data['entries']) - filled))

print()
print('keys total: %d   dropped (unbalanced braces): %d' % (len(rows), len(bad)))
