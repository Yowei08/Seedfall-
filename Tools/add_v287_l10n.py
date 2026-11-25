# -*- coding: utf-8 -*-
"""v287：把 12 件新插件的 name./desc. 词条追加到三份 l10n 表（zh / en / ja）。

只做**追加**：不动既有词条，按文件既有格式（1 空格缩进、k/z/v 三键）文本插入，
最后同步 `count`。可重复执行（已存在的 k 会被跳过）。
"""
import io
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Data")

# id, zh 名, zh 描述(z 用 § 代替层数数字), en 名, en 描述, ja 名, ja 描述
ITEMS = [
    ("hoe_momentum", "惯性", "惯性",
     "上一次出手之后玩家挪过位置，这一次挥击就打出更高的伤害。可叠 § 层。",
     "Momentum", "If you moved since your last swing, this swing hits harder. Stacks up to §.",
     "慣性", "前回の攻撃から移動していれば、この一振りはより強く当たる。最大 § 重ね。"),
    ("can_bulwark", "壁垒", "壁垒",
     "玩家每承受几次伤害就自动获得一层护盾。可叠 § 层，层数越多来得越勤。",
     "Bulwark", "Every few hits you take, you gain a shield layer. Stacks up to §; more layers means it comes more often.",
     "防壁", "数回ダメージを受けるたびにシールドを一枚得る。最大 § 重ね、重ねるほど頻繁に。"),
    ("fert_sunder", "破盾", "破盾",
     "炸到还带着护盾的敌人时，这一炮额外多打出伤害。可叠 § 层。",
     "Sunder", "Blasts that land on a shielded enemy deal extra damage. Stacks up to §.",
     "破盾", "シールドを持つ敵を爆発で捉えると、追加ダメージを与える。最大 § 重ね。"),
    ("hoe_frenzy", "狂暴", "狂暴",
     "自身生命掉到低位之后，锄头每一刀的伤害都变高。可叠 § 层。",
     "Frenzy", "Once your own health is low, every hoe cut hits harder. Stacks up to §.",
     "狂乱", "自分の体力が低いあいだ、鍬の一振りのダメージが上がる。最大 § 重ね。"),
    ("can_frostbite", "霜咬", "霜咬",
     "水柱打到已经发寒的敌人时，这一束的伤害变高。可叠 § 层。",
     "Frostbite", "A water jet striking an already chilled enemy deals more damage. Stacks up to §.",
     "霜噛み", "冷えている敵に水流が当たると、その束のダメージが上がる。最大 § 重ね。"),
    ("fert_gravity", "引力", "引力",
     "炸开的那一下把命中点周围的敌人朝爆心拽过来。可叠 § 层。",
     "Gravity", "The blast drags nearby enemies toward the blast point. Stacks up to §.",
     "引力", "爆発は周囲の敵を爆心へ引き寄せる。最大 § 重ね。"),
    ("hoe_overload", "过载", "过载",
     "连续砍同一个敌人到一定次数，那一刀追加一次爆发；中途换目标就重新数。可叠 § 层。",
     "Overload", "Hitting the same enemy repeatedly adds a burst on the hit that completes the count; switching targets restarts it. Stacks up to §.",
     "過載", "同じ敵を連続で斬り続けると、その一撃に爆発が上乗せされる。対象を変えると数え直し。最大 § 重ね。"),
    ("can_weakspot", "弱点", "弱点",
     "水柱打到已经残血的敌人时，这一束的伤害变高。可叠 § 层。",
     "Weakspot", "A water jet striking a low-health enemy deals more damage. Stacks up to §.",
     "弱点", "残り体力の少ない敵に水流が当たると、ダメージが上がる。最大 § 重ね。"),
    ("fert_detonate", "引爆", "引爆",
     "炸到正在灼烧或已经带印的敌人时，再加一次爆开伤害。可叠 § 层。",
     "Detonate", "Blasts that land on a burning or branded enemy add one more burst. Stacks up to §.",
     "起爆", "燃えている敵や印のついた敵を爆発で捉えると、もう一度爆ぜる。最大 § 重ね。"),
    ("hoe_aftershock", "余震", "余震",
     "砍中的那一下把伤害向命中点周围的敌人荡开。可叠 § 层。",
     "Aftershock", "Each landed cut ripples its damage out to enemies around the hit point. Stacks up to §.",
     "余震", "斬撃が当たった点の周囲の敵へダメージが波及する。最大 § 重ね。"),
    ("can_lance", "贯穿", "贯穿",
     "水柱打穿第一个敌人后继续打向它身后的敌人。可叠 § 层。",
     "Lance", "The water jet punches through the first enemy and keeps going into those behind it. Stacks up to §.",
     "貫通", "水流は最初の敵を貫き、その後ろの敵にも届く。最大 § 重ね。"),
    ("fert_siphon", "虹吸", "虹吸",
     "肥料每打出一次伤害，玩家身上就多一层护盾。可叠 § 层。",
     "Siphon", "Every time the fertiliser deals damage, the player gains a layer of shield. Stacks up to §.",
     "虹吸", "肥料がダメージを与えるたび、プレイヤーにシールドが一層付く。最大 § 重ね。"),
]


def esc(s):
    return s.replace("\\", "\\\\").replace('"', '\\"')


def build_entries(lang):
    out = []
    for (pid, zname_a, zname_b, zdesc, ename, edesc, jname, jdesc) in ITEMS:
        zname = zname_a            # 第 2、3 列是同一个中文名（一处占位、一处可读，保持对齐）
        assert zname_a == zname_b
        if lang == "zh":
            out.append((pid, "name." + pid, zname, zname))
            out.append((pid, "desc." + pid, zdesc, zdesc.replace("§", "2")))
        elif lang == "en":
            out.append((pid, "name." + pid, "", ename))
            out.append((pid, "desc." + pid, "", edesc.replace("§", "2")))
        else:
            out.append((pid, "name." + pid, "", jname))
            out.append((pid, "desc." + pid, "", jdesc.replace("§", "2")))
    return out


def patch(lang, path):
    with io.open(path, encoding="utf-8") as f:
        raw = f.read()
    data = json.loads(raw)
    have = set(e["k"] for e in data["entries"])
    lines = []
    added = 0
    for (pid, key, zval, vval) in build_entries(lang):
        if key in have:
            continue
        lines.append('  {\n   "k": "%s",\n   "z": "%s",\n   "v": "%s"\n  }'
                     % (esc(key), esc(zval), esc(vval)))
        have.add(key)
        added += 1
    if added == 0:
        print("%s: nothing to add" % lang)
        return 0
    block = ",\n".join(lines)
    # 在最后一个 entries 元素之后（即 " ]\n}" 之前）插入
    idx = raw.rstrip().rfind("]")
    head = raw[:idx].rstrip()
    assert head.endswith("}"), head[-40:]
    new = head + ",\n" + block + "\n ]\n}\n"
    try:
        data2 = json.loads(new)          # 先验：必须仍是合法 JSON
    except Exception as e:               # 调试用：把候选写出去，看是哪里断的
        dbg = os.path.join(os.path.dirname(os.path.abspath(__file__)), "_v287_preview_%s.txt" % lang)
        with io.open(dbg, "w", encoding="utf-8", newline="\n") as f:
            f.write(new)
        print("%s: JSON 失败 %s -> %s" % (lang, e, dbg))
        raise
    data2["count"] = len(data2["entries"])
    new = re.sub(r'"count":\s*\d+', '"count": %d' % data2["count"], new, count=1)
    json.loads(new)                   # 再验一次（count 改过之后）
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(new)
    print("%s: +%d entries, count -> %d" % (lang, added, data2["count"]))
    return added


if __name__ == "__main__":
    total = 0
    for lang in ("zh", "en", "ja"):
        total += patch(lang, os.path.join(BASE, "l10n_%s.json" % lang))
    print("added total", total)