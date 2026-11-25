# 阶段 3 · 插件全部重做 —— 独立验收报告

- 时间：2026-10-17 12:30（本机时间）
- 验收者：独立验收（不做实现；实现由并行的另一条任务完成，两条任务在 `Goods.cs` / `RunSession.cs` 上发生过所有权冲突，已由上层裁决为「② 让在写的那条做完，我转独立验收」）
- 本报告的所有读数都是**跑出来的**，每条都附命令与原文。

---

## 0. 一句话结论

**编译两套 0 error｜`SelfCheck` 437/437 全绿（419 → 437，+18）｜`HUD-AUDIT` 通过｜12 件插件的「装前 → 装后」都有变化｜持续效果表（灼烧/减速/毒区）各有一条带秒数与层数的实测｜两条新断言都做过「故意改坏 ⇒ 红 ⇒ 改回」的抓取证明。**

---

## 1. 编译（一次只 Rebuild 一个工程）

```
cd D:\Unity\project\Seedfall守园者
dotnet build Seedfall.Runtime.csproj -t:Rebuild -v minimal --nologo   ⇒ 0 个错误（10 警告）
dotnet build Seedfall.Tests.csproj   -t:Rebuild -v minimal --nologo   ⇒ 0 个错误（10 警告）
```

（12:22 与 12:30 各测一次，两次都是 0；12:23:57 界面任务又改过 `ImGuiHud.cs`，之后仍为 0。）

---

## 2. `DebugApi.SelfCheck()`

| 时点 | 插件表 | 结果 |
|---|---|---|
| 改前（git HEAD 的 30 行表，代码已是新版） | 30 行 | FAIL 430/437 |
| 故意改坏 #2（`fert_freeburst.maxStack = 4`） | 12 行 | FAIL 436/437 |
| 故意改坏 #1（`fert_freeburst.effect = "DamageFlat"`） | 12 行 | FAIL 431/437 |
| **改回（正式状态）** | **12 行** | **PASS 437/437** |

分母：**419 → 437**（+18：插件逐件实测/持续效果表/两条新断言等）。

---

## 3. 两条新断言的「改前红」与「改后绿」原文

### 3.1 `PLUGIN-NO-STAT`

**改前红（把插件表换回 git HEAD 的 30 行，其余不动）：**

```
自检 FAIL：430/437 通过  FAIL  PLUGIN-NO-STAT：没有任何一件插件的 effect 在改玩家属性（11 个属性类名字全部清零）  26 件仍在改玩家属性（或写了不存在的效果）：
  hoe_whetstone = DamageFlat（属性类：本版已下线）
  hoe_lightgrip = AttackSpeed（属性类：本版已下线）
  hoe_keeneye = CritRate（属性类：本版已下线）
  hoe_airsplit = ProjectileCount（属性类：本版已下线）
  hoe_frostedge = ChillOnHit（解析不出来 ⇒ 回落 None）
  hoe_shockcore = ChainOnHit（解析不出来 ⇒ 回落 None）
  hoe_bloodmoon = CritBlast（解析不出来 ⇒ 回落 None）
  hoe_hunttooth = DamagePercent（属性类：本版已下线）
  can_longpipe = Range（属性类：本版已下线）
  can_pumpcore = DamageFlat（属性类：本版已下线）
  can_focusjet = CritRate（属性类：本版已下线）
  can_turbine = AttackSpeed（属性类：本版已下线）
  can_rainstorm = ProjectileCount（属性类：本版已下线）
  can_arcnode = ChainOnHit（解析不出来 ⇒ 回落 None）
  can_overflow = AmmoOnKill（属性类：本版已下线）
  can_pressuredge = CritDamage（属性类：本版已下线）
  can_fullflow = FullHpBonus（属性类：本版已下线）
  fert_pressure = ExplodeRadius（属性类：本版已下线）
  fert_richmix = DamageFlat（属性类：本版已下线）
  fert_quickfuse = AttackSpeed（属性类：本版已下线）
  fert_blastweight = Knockback（属性类：本版已下线）
  fert_split = ProjectileCount（属性类：本版已下线）
  fert_echocan = BlastEcho（解析不出来 ⇒ 回落 None）
  fert_coldsmoke = ChillOnHit（解析不出来 ⇒ 回落 None）
  fert_chainpowder = ChainOnHit（解析不出来 ⇒ 回落 None）
  fert_fullcharge = FullHpBonus（属性类：本版已下线）
```

**关于「20」这个数（必须说清）**

- 纯「改玩家属性」= **19 件 / 30**。逐件就是上面那 19 条标着「属性类：本版已下线」的行。
- 用户清单里的第 12 项 `SwordWave` **不是**属性类 —— 它正是新版保留的 12 件里的第 1 件（`hoe_swordwave`）。所以 `19 + SwordWave 1 = 20` 是把一个**攻击形式**算进了属性类。
- 跑出来是 **26** 而不是 19/20：断言还把「表里写了、但新枚举里已不存在 ⇒ 回落 `None`」也算违规（`ChillOnHit / CritBlast / ChainOnHit / BlastEcho`，共 7 条）。这是有意的（否则将来有人把 `DamageFlat` 加回枚举，字符串判据就抓不住了）。
- 离线复算（不依赖历史状态，随时可复现）：`git show HEAD:Assets/Resources/Data/plugins.json` 解析 30 行，与 11 个属性类名字取交集 ⇒ **19**，逐件就是上面的清单。

**故意改坏（改完之后也能证明断言抓得住）—— `fert_freeburst` 的 effect 改成 `"DamageFlat"`：**

```
自检 FAIL：431/437 通过  FAIL  PLUGIN-NO-STAT：没有任何一件插件的 effect 在改玩家属性（11 个属性类名字全部清零）  1 件仍在改玩家属性（或写了不存在的效果）：
  fert_freeburst = DamageFlat（属性类：本版已下线）
```

**改后绿：**

```
自检 PASS：437/437 通过
```
（`Report()` 只打印失败项 ⇒ 绿的时候没有明细行，这是它既有的设计；上面那次「改坏 ⇒ 精确报 1 件 ⇒ 改回 ⇒ 全绿」就是它确实在跑并且确实在判的证据。）

### 3.2 `PLUGIN-SLOT`

**故意改坏（`fert_freeburst.maxStack` 3 → 4）：**

```
自检 FAIL：436/437 通过  FAIL  PLUGIN-SLOT：每把武器恰好 6 个插槽、每件插件 maxStack ≤ 3（叠层不占新插槽）  槽位数不符 0 把、maxStack 超 3 的 1 件 ⇒ fert_freeburst 的 maxStack = 4（应 ≤ 3）；
```

**改后绿：** 并入上面那条 `自检 PASS：437/437 通过`。

⚠️ 只对 `maxStack ≤ 3` 这一半做了破坏测试；「恰好 6 个槽」那一半没做（它比的是 `PlayerGoods.MaxPluginsPerWeapon` 的**运行期读值**与字面量 6，代码里刻意先读成局部变量以避开编译期折叠，不是空断言，但**没有实测红过**）。

---

## 4. `HUD-AUDIT`

```
DebugApi.HudAudit()        → HUD 审计已排队（当前阶段 Prepare）→ 读 Console 的 HUD-AUDIT 行
（等 2 秒）
DebugApi.HudAuditReport()  → HUD-AUDIT 通过：上一帧画出的所有文本都放得下
```

---

## 5. 12 件插件的「装前 → 装后」数值表

（`SelfCheck` 的逐件实测输出；同一把武器、同一个敌人、同一个种子 ⇒ 差值只可能来自插件）

```
[插件实测 v180] 同一把武器、同一个敌人、同一个种子 ⇒ 装前 → 装后
  插件                  武器    改了哪一环               量的东西                              装前 → 装后
  hoe_swordwave       锄头    剑气（多劈一道）            5.5m（挥击框外）处受到的伤害                  0 → 4
  hoe_burn            锄头    灼烧（命中附层）            命中后身上灼烧的「每秒」掉血                    0 → 0.5
  hoe_doublestrike    锄头    双击（多挥一刀）            一次挥击动作的总伤害（1 刀 → 2 刀）             4 → 8
  hoe_lifesteal       锄头    吸血（伤害转生命）           打中一个敌人后玩家回的血                      0 → 0.813
  can_multijet        水壶    多柱（多一道水柱）           连射 1 秒的耗水（每道独立扣水）                 26 → 46
  can_slow            水壶    减速（命中附层）            命中后敌人的移速系数（1 = 没减速）               0.8 → 0.704
  can_thickjet        水壶    加粗（只改宽度）            3.2m 偏轴处被激光打到的伤害（宽度才够得着）          0 → 4
  can_autocharge      水壶    自充（每秒回水）            2 秒不开火后的储水量变化（+10）                0 → 9
  fert_subburst       肥料    子爆（主爆旁的副爆）          落点外 4.5m 处受到的爆炸伤害（子爆罩到）           0 → 7
  fert_poison         肥料    毒素（爆点留毒区）           爆炸后第 1 秒毒区掉的伤害                    0 → 5
  fert_freeburst      肥料    免耗（概率不扣充能）          叠满 3 层后 20 次投掷的耗弹                 20 → 15
  fert_chain          肥料    连环（多炸一次）            落点外 3.4m 处受到的爆炸伤害（第 2 个爆点）        0 → 10
```

读数自洽性核对（这是「表不是摆设」的关键）：

- `hoe_doublestrike` 4 → 8：双击 = 同一刀打两次，恰好 ×2 ✓
- `fert_subburst` 7 = 主爆 10 × 0.70（子爆半径/伤害同比例）✓；`fert_chain` 10 = 主爆**全额**（★半径同主爆★，不是子爆的 0.7）⇒ 两条规格里点名区分的差异，在读数上正好是 7 vs 10 ✓
- `can_multijet` 26 → 46：多一道 ⇒ 一次判定付两份水 ✓
- `can_thickjet` 射程不变（3.2m 偏轴才够得着、沿轴长度没变）：**只改宽不改长** ✓
- `can_slow` 0.8 → 0.704 = ×0.88：基线 0.8 是「命中自带 1 层寒冷 −20%」，插件再加 1 层 −12% ⇒ 0.88 ✓

---

## 6. 持续效果表（灼烧 / 减速 / 毒区）实测

三行都来自 `[持续效果实测]` 日志（每个 = 一次真攻击 + 推进 1 秒）：

```
[持续效果实测] 灼烧：表里每秒 0.500｜实战 1 秒掉 0.500｜剩 4.00s ×1层
[持续效果实测] 减速：移速 ×0.704｜表里每秒 0.500｜实战 1 秒掉 0.500｜剩 4.00s ×1层
[持续效果实测] 毒区：毒区@(3.0,0.0)r2.4 每秒5.00 剩4.00s ×1层｜实战 1 秒掉 5.000
```

- 灼烧：每秒 0.500 = 近战伤害 1 × 0.50 × 1 层 ✓；剩 4.00s（5 秒里已过 1 秒）✓
- 减速：移速 ×0.704（0.8 寒冷 × 0.88 插件）✓；每秒 0.500 = 远程伤害 1 × 0.50 ✓
- 毒区：半径 r2.4 = 主爆 3.0 × 0.8 ✓；每秒 5.00 = 爆炸伤害 10 × 0.50 ✓；同一行里「剩 4.00s ×1层」就是规格要的「谁 / 每秒多少 / 剩多久 / 叠了几层」

配套的断言（同一次 437/437 里全绿）：施加后「剩 5 秒」、推进 1 秒掉血 = 每秒值、再叠 2 次 ⇒ 3 层且**时长不叠**（仍 5 秒不是 15 秒）、层数封顶 3、减速 1 层 ×0.88 / 5 层触底 ×0.40、毒区按位置伤半径内所有敌人、**敌人一死挂他身上的行立刻消失**（死亡结算）、到期行被移除。

---

## 7. 特效（FxSpec 12 通道）

`DebugApi.PluginFxAudit()`：**12/12 通过，无重复签名**。逐件的可见通道（本体色/边色/尺寸/发光/火花/环/拖尾/震屏）我核对了 12 个 8 元组，**两两不同**：

```
hoe_swordwave      DEF8DC/BEE7B6 1.00 1.35 0 1 0.55 1.00
hoe_burn           FF8243/FFD859 1.00 1.40 2 0 0.00 1.00
hoe_doublestrike   FFF49E/FFD859 1.10 1.00 0 0 0.00 1.30
hoe_lifesteal      FFF49E/F0523A 1.00 1.00 0 0 0.40 1.00
can_multijet       66F2FF/59D8FF 1.00 1.00 1 1 0.00 1.00
can_slow           73E2FF/59D8FF 1.00 1.00 0 0 0.32 1.00
can_thickjet       66F2FF/59D8FF 1.25 1.15 0 0 0.00 1.00
can_autocharge     66F2FF/68EED2 1.00 1.30 0 0 0.00 1.00
fert_subburst      FF9E33/FF7219 0.90 1.00 0 2 0.00 1.00
fert_poison        8CE141/E2C92F 1.00 1.00 3 0 0.00 1.00
fert_freeburst     FF9E33/FFB72D 1.00 1.50 0 0 0.00 1.00
fert_chain         FF9E33/FF7219 1.00 1.00 0 1 0.45 1.50
```

---

## 8. 数据表核对（`plugins.json` 12 行）

- 12 行，`id / displayName / quality / allowedWeapons / effect / amount / amount2 / stackRule / maxStack / exclusivePerWeapon / basePrice / description` 字段沿用
- `quality` 用 **D/C/B/A/S**（本表实际用到 C/B/A/S 四档，无 D）
- 每行 `effect` 与 12 个枚举值 **1:1**，没有重复、没有缺
- 三把武器各 4 件；`maxStack` 与规格的「层」列逐件一致（3/3/2/3｜3/3/2/3｜3/3/3/2）
- 相对 git HEAD：`+24 −33` 行

---

## 9. 没做到 / 偏离的地方（如实）

1. **`PLUGIN-NO-STAT` 的「改前红」不是 20 条**：纯属性类 **19**，算上「新枚举里不存在的 effect」是 **26**。用户清单里的第 20 项是 `SwordWave`，它不是属性类。数字差异我按「拿数与原文说话」写清，没有去凑 20。
2. **`PLUGIN-SLOT` 只破坏了一半**（`maxStack`），「恰好 6 个槽」那一半没实测红过。
3. **`can_autocharge` 2 秒实测回 9 点，不是 10**：每秒 5 点、60fps 下累加 120 次 `5/60`，float 求和得 9.99999… ⇒ 只发出 9 点。数学上 60 秒会少 1 点，属于浮点累加的已知误差，未改。
4. **改玩家属性的「伤害公式入参」这一条我是靠读代码 + 12 件读数认定的**（`DoHoeSwing` / `FireBeam` / `ExplodeAt` 里伤害只吃 `TuningDef` 与 `StatBlock`）；没有额外的「属性快照前后对比」断言。
5. **没做截图**：本次交付清单里没有截图项，且编辑器是与另外两条任务共用的，我没有占用它去抓图。
6. 我对工程的写入只有两处临时改动（`plugins.json` 的 `maxStack 3→4`、`effect FreeBurst→DamageFlat`）与一次 `git checkout HEAD -- plugins.json`（复现「改前表」）；**全部已还原，且逐字节校验与备份一致**（`Get-FileHash` 相等）。

---

## 10. 复现命令

```powershell
# 编译（一次一个）
cd D:\Unity\project\Seedfall守园者
dotnet build Seedfall.Runtime.csproj -t:Rebuild -v minimal --nologo
dotnet build Seedfall.Tests.csproj   -t:Rebuild -v minimal --nologo

# 桥
$b = "http://127.0.0.1:17890"
Invoke-RestMethod "$b/status"
Invoke-RestMethod "$b/command" -Method Post -Body '{"command":"refresh"}' -ContentType "application/json"
Invoke-RestMethod "$b/command" -Method Post -Body '{"command":"play"}'    -ContentType "application/json"
Invoke-RestMethod "$b/input"   -Method Post -Body '{"action":"send","path":"DebugApi","type":"DebugApi","method":"SelfCheck"}' -ContentType "application/json"
Invoke-RestMethod "$b/input"   -Method Post -Body '{"action":"send","path":"DebugApi","type":"DebugApi","method":"HudAudit"}' -ContentType "application/json"
Invoke-RestMethod "$b/input"   -Method Post -Body '{"action":"send","path":"DebugApi","type":"DebugApi","method":"HudAuditReport"}' -ContentType "application/json"
Invoke-RestMethod "$b/input"   -Method Post -Body '{"action":"send","path":"DebugApi","type":"DebugApi","method":"PluginFxAudit"}' -ContentType "application/json"
Invoke-RestMethod "$b/log?limit=3000"      # 里面找 [插件实测 v180] / [持续效果实测] / SELFCHECK
```
