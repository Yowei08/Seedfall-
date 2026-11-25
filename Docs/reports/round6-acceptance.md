# 第六轮验收清单（v179）

> 落档时间 2026-10-17 11:53 · 由协调者在等待三个并行任务时立好
> 用法：每个阶段交付时逐条对照，**每条都要有可复现的读数或图**，不接受"做好了"

---

## 阶段 0 · 骨架（✅ 已过）

| 判据 | 结果 |
|---|---|
| `seeds.json` 加 `goldYield` / `expYield` | ✅ `Defs.cs:129 public int goldYield, expYield;` |
| `SeedEconomy.cs` 改读新字段 | ✅ 6,791 → 9,349 B |
| `Actors.cs` 结算改用新字段 ＋ 改写旧注释 | ✅ +709B；`:880` 保留 v165 的理由并标注 v179 变化 |
| **骨架步不许有任何数值变化** | ✅ `SelfCheck` 425/425（条数与改前一致） |
| 6 条断言名字可 grep | ✅ `SelfCheck.cs` +16,387B；命中 15 处；`:3129` 注释写明"改名 = 上游 grep 不到 = 等于没做" |

---

## 阶段 1 · 植物四象限 —— ✅ **已验（2026-10-17 11:53，`seeds.json` 12,762 → 14,210 B）**

### ★ 判型规则（★ 这里踩过一次坑，写清楚：**重/快按周期判，不是按 `g` vs `e`**）

| 类型 | 判定 |
|---|---|
| **金** | `t ≈ 基准` 且 `g/t` 明显高于 `e×2/t` |
| **验** | `t ≈ 基准` 且 `g/t` 明显低于 `e×2/t` |
| **重** | `t ≥ 1.5 × 基准` |
| **快** | `t ≤ 0.7 × 基准` |

> ⚠️ **只看 `g` vs `e` 会把"重/快"误判成"金"** —— 因为重型/快型是 `g` 与 `e` 同时放大/缩小、**比值仍 `g>e`**。
> 我第一次验收就这么误判过，险些让实现者去修一个没坏的东西。

### 验收结果（★ 五条全绿）

| # | 判据 | 结果 |
|---|---|---|
| 1 | 档内 `(g + 2e) / t` 极差 ≤ 2% | ✅ **五档全部 0.00%** |
| 2 | 档间单调递增 | ✅ **0.500 → 1.296 → 1.708 → 2.200 → 2.797**（旧表是 0.500→0.216 递减） |
| 3 | 类型数 `min(4, 株数)` | ✅ **D 4/4 · C 4/4 · B 3/3 · A 3/3 · S 2/2** |
| 4 | 三元组 `(g,e,t)` 不重复 | ✅ **重复数 = 0** |
| 5 | `mechanic` 全部 `None` | ✅ **非 None = 0 条** |

### 周期确实分了三档

| 档 | 周期 |
|---|---|
| D | **16 / 32 / 8**（基准 / ×2 / ×0.5） |
| C | **27 / 54 / 13.5** |
| B | **48 / 96** |
| A | **85 / 170** |
| S | **148** |

### 经济量级校验（★ 与用户预算表对账）

| 档 | 平均 g/s | 预算 | 平均 e/s | 预算 |
|---|---|---|---|---|
| **D** | ★ **0.250** | **0.250** ✅ | ★ **0.125** | **0.125** ✅ |
| **C** | 0.324 | 0.330（差 1.8%） | 0.171 | 0.165（差 3.6%） |

> 结论：四象限**不是在预算之外加料，而是在预算之内换分配** —— 金型多拿 50%、验型就少拿 50%，**档的平均不动**，经济总量没有通胀。
> C 档那 1.8%/3.6% 的偏差来自快型的 `13.5s`（半基准的奇数倍），两条断言都不卡它，可以不改。

### 还差一条（★ 唯一没法由协调者代验的）

| # | 判据 | 状态 |
|---|---|---|
| 6 | **`SEED-ARCHETYPE` / `SEED-NO-MECH` 真的会红** | ⏳ 待实现者做"故意改坏 → 贴 FAIL 原文 → 改回" |

> 因为**现在全绿并不能证明它会红** —— 一条永远不会红的断言，与一个**没接进检查列表**的断言，在"全绿"这个读数上完全一样。

---

## 阶段 2 · 道具 24 件 —— ✅ **数据已验**（2026-10-17 11:54，`items.json` 7,338 → 10,691 B）

### 用 Python 的 `json` 验（★ 标准参照）

```
bytes       = 10691        has_BOM   = False
json_ok     = True         items     = 24
category    = {1: 6, 2: 18}            with_rule = 0
```

### `ITEM-COVERAGE` 的实质已满足

`eff_stats` 与 `cost_stats` **完全相同**，各 15 个；`only_eff` 与 `only_cost` **都为空**：

```
AtkMult, AtkRange, AtkSpeed, BlastDamage, CritDmg, CritRate, ExpMult, GoldMult,
HeartDropRate, Knockback, MaxHp, MeleeDamage, MoveSpeed, RangedDamage, Shield
```

⇒ **15 个属性每一个都在增益侧与减益侧各出现至少一次**。

### ⚠️ 两条经验写在这里

1. **`ConvertFrom-Json`（PowerShell 5.1）会误报这个文件语法错** —— 报「传入的对象无效，应为":"或"}" (302)」，而 Python 的 `json.loads` 说它合法。
   ⇒ **判据：一个工具报错，先换一个工具复验，再下结论。**
2. **属性名以表为准** —— 表里两侧都用 `CritDmg`（不是我原写的 `CritDamage`），表内自洽 ⇒ `ITEM-COVERAGE` 的清单也要用 `CritDmg`。

| # | 判据 | 结果 |
|---|---|---|
| 1 | 15 属性两侧各一次 | ✅ **eff/cost 集合完全相同，差集为空** |
| 2 | 0 条带 `rule` | ✅ **with_rule = 0** |
| 3 | `CritRate` 两侧数值可见 | ✅ `0.04 / 0.06 / 0.03`，且 `TableDatabase.IsFractionStat` 已落地 |
| 4 | JSON 合法、无 BOM | ✅ |
| 5 | **`ITEM-COVERAGE` 改前红 / 改后绿** | ⏳ 待实现者贴两段原文 |

## 阶段 2 · 道具 24 件（原始待验清单，保留备查）

| # | 判据 | 怎么验 |
|---|---|---|
| 1 | **15 个属性每个在 `effects` 与 `costs` 里各至少一次** | `ITEM-COVERAGE` |
| 2 | **0 条带 `rule`** | `ITEM-NO-RULE`；`Defs.cs` 的 `ItemRules` 保留不删 |
| 3 | `CritRate` 的两侧数值**真的可见** | 它在代码里是 0..1 小数，`Validate` 要求整数 ⇒ 只对它放开小数（0.06 = +6%）；**不能是 0、不能恒顶满** |
| 4 | `CritDamage` 是否同属率语义 | 若是，同一处理 ＋ 同一理由 |
| 5 | 同属性上增益**严格大于**减益 | `TableDatabase.Validate` 已有检查 |
| 6 | **`ITEM-COVERAGE` 改前红 / 改后绿** | **两段原文都要贴** |

---

## 阶段 3 · 插件 12 件 —— ⏸ **改前基线已量（2026-10-17 11:56）**

### 改前 `plugins.json` 的实测基线（Python 读，`30 件 / 17 种 effect`）

```
按武器分布  {0: 10, 1: 10, 2: 10}
品质命名    Common 6 / Uncommon 7 / Rare 7 / Epic 5 / Legendary 5   ← 英文名，要改成 D/C/B/A/S
用到玩家属性类的件数 = 19 / 30   ← ★ 修正：原先写 20 是我把 `SwordWave` 误算进属性类
  （11 个属性类相加 = 19；`SwordWave` 1 件是**攻击形式**、正是新 12 件里的第 1 件 ⇒ 19+1+10=30）
  DamageFlat 3 · AttackSpeed 3 · ProjectileCount 3 · CritRate 2 · FullHpBonus 2
  CritDamage 1 · Range 1 · AmmoOnKill 1 · ExplodeRadius 1 · Knockback 1
  DamagePercent 1 · SwordWave 1
```

⇒ **`PLUGIN-NO-STAT` 的「改前红」数字 = 20**；目标是把 **20 → 0**、**30 件 → 12 件**、品质 **英文名 → D/C/B/A/S**。

### 改后实测（2026-10-17 12:14:47，协调者用 Python 只读验，不跑 Rebuild）

```
plugins.json   5,983 B（改前 11,072）   count = 12
quality = {B:3, C:3, A:3, S:3}          ← 全部 D/C/B/A/S 命名；没有 D，因为规格的档位表里本就没给 D
effect  = 12 个新值各 1 次：
          SwordWave / BurnOnHit / DoubleStrike / Lifesteal
          MultiJet  / SlowOnHit / ThickJet     / AutoCharge
          SubBurst  / PoisonZone / FreeBurst   / ChainBurst
weapon  = {0:4, 1:4, 2:4}                ← 锄/壶/肥 各 4
ids     = missing [] / extra []          ← 12 个 id 与契约完全一致
```

⇒ **数据层判据全部通过**（件数／三组各 4／命名／12 个 effect 一一对应／id 一个不多一个不少）。
★ 数据层与代码层可以**分开验收**：`plugins.json` 的判据用 Python 就能验，编译绿不绿只影响 C# 那半边（枚举／消费点／持续效果表）。

### 持续效果表 `Assets/Scripts/Combat/PluginDots.cs` —— ✅ 已验（2026-10-17 12:15）

```
DotRow: Kind / EnemyId / Center / Radius / PerSecond / Remain / Stacks / Acc / PctPerStack
        → 正是要求的「谁 / 每秒多少 / 剩多久 / 叠了几层」
常量:   BurnSeconds = SlowSeconds = PoisonSeconds = 5f   SlowFloor = 0.60f
统计:   AppliedBurn / AppliedSlow / AppliedPoison / ExpiredRows / DeathClears / DamageDealt
结算:   OnEnemyDied(int enemyId)  ← 接敌人死亡；★ 毒区被排除（"毒区属于地面"）
```

**"时长不叠"做对了**：`r.Remain = Mathf.Max(r.Remain, BurnSeconds);   // ★ 时长不叠`
注释还写了反面：*"若写成 `Remain += BurnSeconds`，连点三次就能烧 15 秒 —— 那是另一条数值设计"*。

### 三种攻击特效 —— ✅ 代码层面已验（2026-10-17 12:18，协调者只读）

| 攻击 | 实现（`WorldView.cs`） |
|---|---|
| **锄头挥击** | `:2969` **`shardN = Clamp(9 + Rings − Sparks/2, 8, 12)`** ⇒ 正好 **8~12 块碎片**；`:2982` 用 `FxShardMesh(i % 6)` 六种程序化变体 |
| **水壶水柱** | `:1929` `NightLighting.IlluminateBeam(mid, len, …)`；`:1939` **`"WetDecal"`** 地面湿痕贴花 |
| **肥料爆炸** | `:2094` `BlastShell`（球壳）；`:2113-2124` `BlastShard`（碎片）；`:2148` **`BlastFlash`**（`VfxMatCached(white, white)` ⇒ 1 帧白闪）＋ `:2184` 点光 |

**碎片网格是程序化的**：`FxShardMesh(int variant)`（`:2579`，6 种变体）。

#### ★ 实现者发现了一个我没提的问题（值得记下来）

`NightLighting.Illuminate` 背后是**8 槽光池**，池里的灯**到期就平掉**（`_poolUntil` 一过就 `intensity = 0`），
**本身没有渐变**。所以"点光寿命 0.15s、强度随弧扫过衰减"**光靠它做不到**。

⇒ 它自己补了一层：`LightRamp` struct ＋ `TickLightRamps(dt)`（`:2843-2861`，逐帧衰减，`k ≥ 1` 移除），
在 `:1164` 每帧调用。

> **判据：光池只保证"亮与灭"，不保证"怎么亮起来的"。任何"渐亮/渐灭"的需求都要自己在池外补。**

#### 材质缓存纪律守住了（两条相反的规矩都写明了）

- `:2540` 「全部走 `VfxMatCached`（第⑤轮那个 ~60 份/秒 的材质泄漏）」
- `:2200` 「逐实例材质 **结束时必须 `Object.Destroy(Mat)`**」
- `:2764` 「全部共用 `VfxMatCached` 的那份缓存 ⇒ `TickFxMeshes` 里**绝不** `Destroy(Mat)`」

### `PLUGIN-NO-STAT` 的判据本身 —— ✅ 已验（2026-10-17 12:19，协调者只读）

**问题**：原黑名单是**枚举数组** `PluginEffectKind[] statKinds = { DamageFlat, … }`，而 `PluginEffectKind` 已删掉这些名字 ⇒ **必然 CS0117**；
更糟的是黑名单里**多了 `SwordWave`**，而新表里 `hoe_swordwave` 的 effect 就是 `SwordWave` ⇒ **那条断言结构上不可能绿**。

**改后的实测**：

```
11 个属性类名字全部以「字符串」形式存在：
  DamageFlat 2 · CritDamage 2 · DamagePercent 1 · AttackSpeed 1 · CritRate 1 · Range 1
  ProjectileCount 1 · FullHpBonus 1 · AmmoOnKill 1 · ExplodeRadius 1 · Knockback 1
SwordWave 不在其中（它只剩 4 处 case PluginEffectKind.SwordWave 的运行期逻辑）
rawEffect 用法 4 处  ← 不是只声明，是真的在用
```

**为什么必须改成字符串 + `rawEffect`**：
`DefParser.ParseEnum("DamageFlat", None)` 会**回落到 `None`** ⇒ `pl.effect.ToString()` 得到 `"None"` ⇒
若判据只比"解析后的枚举"，**变异测试（把一件的 effect 改成 `DamageFlat`）根本不会红**。
而比**表里的原始字面值**（`rawEffect`）才能报出 `hoe_swordwave = DamageFlat`，而不是 `= None`。

> **判据：校验"某个非法值有没有出现"时，要比**原始输入**，不是解析后的结果 —— 因为解析器会把非法值悄悄换成默认值。**

**`rawEffect` 的三层链（2026-10-17 12:19 实测，全部就位）**

| 层 | 位置 | 做了什么 |
|---|---|---|
| **存** | `Goods.cs:260` | `public string rawEffect = "";` |
| **填** | `TableDatabase.cs:189` | `p.rawEffect = raw.effect ?? "";` ← **从原始 JSON 取，不从枚举取** |
| **用** | `SelfCheck.cs:3298` | `string raw = pl.rawEffect ?? "";` ＋ 与 11 个名字比 ＋ 报告明细 |
| **复用** | `SelfCheck.cs:4048` | 另一处检查也用它（`a.id + "(" + a.rawEffect + ")"`） |

`SelfCheck.cs:3286` 的注释把理由写了进去：
> 「所以明细走 `rawEffect`（解析后的 `effect` 只剩 `None`，**等于告诉人"这里没写"**✗）」

> **一个"原始值"字段被三处需要 —— 这正是"解析器会容错"的必然结果。**

**实现层面的四个风险，逐条已消（2026-10-17 12:20 读码确认）**

| 风险 | 实现里怎么处理的 | 位置 |
|---|---|---|
| 解析回落到 `None` ⇒ 报错没定位 | 报 **`rawEffect`** 而不是 `effect` | `:3306` `pl.id + " = " + raw` |
| 空串被误判成"写了非法值" | **`raw.Length > 0`** 才算"解析不出来" | `:3302` `undefined = (effect == None && raw.Length > 0)` |
| 名字比较因空白/大小写不等 | **`StringComparison.Ordinal`** | `:3301` |
| 黑名单多了 `SwordWave` | 标题与数组都**只有 11 个** | `:3283-3291`, `:3309` |

> 而我自己猜的两条（空串、空白）**它本来就在处理** —— 真正有用的是"比原始值而不是解析结果"那条，因为它原本没有。
> **判据：假设的价值 = "对方没想到"的概率，不是"我想得到"的程度。**

### 12 条 `FxSpec` 签名 —— ✅ 代码层面已验（2026-10-17 12:17，协调者只读）

| 插件 | 通道增量（实际代码） |
|---|---|
| 剑气 | `Rings+1, Trail+0.55, Glow+0.35` |
| 灼烧 | `Core 橙红, Glow×1.4, Sparks+2` |
| 双击 | `Shake×1.3, Scale×1.1` |
| 吸血 | `Rim 血红, Trail+0.4` |
| 多柱 | `Rings+1, Sparks+1` |
| 减速 | `Core 冰蓝, Trail+0.32` |
| 加粗 | `Scale×1.25, Glow×1.15` |
| 自充 | `Rim 水绿, Glow×1.3` |
| 子爆 | `Rings+2, Scale×0.9` |
| 毒素 | `Core 毒绿, Rim 黄绿, Sparks+3` |
| 免耗 | `Rim 金, Glow×1.5` |
| 连环 | `Rings+1, Shake×1.5, Trail+0.45` |

**唯一一对通道名相同的**：自充 `Rim 水绿 / Glow×1.3` 与 免耗 `Rim 金 / Glow×1.5` —— **颜色与倍数都不同**，
而 `PluginFx.Diff()` 按值比（`ColorGap > 0.020`、`Glow 差 > 0.010`）⇒ **可分**。

**"两两不同"是机械可验的**：
- `Diff()`（`:280-287`）逐通道比 Core/Rim/Scale/Glow/Sparks/Rings/Trail/Shake；
- `Describe()`（`:296-303`）输出可读串；
- **`Hash()`（`:330-341`）把 8 个通道的量化值累进一个 `uint`** ⇒ 直接比哈希即可判重。

⇒ 代码层面已成立；剩运行期 `PluginFxAudit` 报 12/12（需编译绿）。

### 三条攻击路径的消费点 —— ✅ 已验（2026-10-17 12:15）

```
RunSession.cs:1567   Dots.Clear();                                        ← 新局重置
RunSession.cs:2686   Dots.Tick(dt, World, World.Player);                  ← 每帧推进
RunSession.cs:3650   Dots.ApplyBurn(e.Id, perLayer, m.BurnLayersPerHit);  ← 灼烧
RunSession.cs:3657   Dots.ApplySlow(e.Id, perLayer, m.SlowPctPerLayer, m.SlowLayersPerHit);  ← 减速
RunSession.cs:4351   Dots.AddPoisonZone(center, pr, perLayer, poisonLayers);                  ← 毒区
```

#### ★ 架构发现：`RunSession` **不按枚举 switch**，而是经过一层"翻译"

`PluginEffectKind.<新 12 个 kind>` 在 `RunSession.cs` 里 **命中 0**。它读的是 `WeaponModifiers` 的字段
（`m.BurnLayersPerHit` / `m.SlowPctPerLayer` / `m.SlowLayersPerHit` / `poisonLayers`）。

```
插件 id/effect  ──WeaponSystem 翻译──▶  WeaponModifiers 字段  ──消费点读字段──▶  三条攻击路径
                                                              └──▶  PluginDots（只认 每秒多少/多久/几层）
```

**为什么这比"消费点直接 switch 枚举"好**：
- 消费点**不必知道 12 个枚举**；
- 加第 13 件插件时**只要加字段**，三条攻击路径一行不用改；
- `PluginDots` 那层**完全不知道"插件"是什么**，只认数值 ⇒ 可单独测。

> 这也解释了 `PluginFx.cs` 从 21,307 → 17,875 B：旧版给 17 种 effect 各写一段，新版按 12 个**形态** ＋ 通道参数。
> 新出现的 `WeaponSystem.cs`（22,370 B）大致就是上面那个翻译层。

#### ⚠️ 我的规格自相矛盾一处（实现者用更合理的数解决了）

| 我给的 | 与它冲突的 | 实现者取 | 为什么对 |
|---|---|---|---|
| 减速 **最多 3 层** | 下限 **−60%**，每层 −12% | **`SlowMaxStacks = 5`** | `0.60 / 0.12 = 5` ⇒ **3 层只能到 −36%，永远到不了 −60%** |

> ⇒ **"3 层"与"下限 −60%"不能同时成立。** 实现者选了让两者都成立的那个数（5），这是正确的取舍；
> 下次给叠层数时，**要用「下限 ÷ 每层」反算**，不能凭手感给。

### 改前 / 改后对照表

| 指标 | 改前 | 目标 |
|---|---|---|
| 件数 | 30 | **12**（锄/壶/肥 各 4） |
| 改玩家属性的件数 | **20** | **0** |
| distinct effect | 17 | **12**（全为攻击方式类） |
| 品质命名 | `Common/Uncommon/Rare/Epic/Legendary` | **`D/C/B/A/S`** |
| `FxSpec` 签名 | — | **12 条两两不同** |

### 依赖（★ 为什么还没开工）

| 要改的 | 现在 |
|---|---|
| `Assets/Scripts/Data/Goods.cs`（`PluginEffectKind` 枚举） | ✅ 空着 |
| `Assets/Scripts/Flow/RunSession.cs`（三条攻击路径） | ✅ 空着 |
| `Assets/Scripts/Automation/SelfCheck.cs`（加 `PLUGIN-NO-STAT` / `PLUGIN-SLOT`） | ⏳ **被数据任务占用** |
| `Assets/Resources/Data/plugins.json` | ✅ 空着 |

> **枚举一改就会让 `RunSession` 编译不过** ⇒ 三处必须同一个人在同一次改动里接上，不能拆给两条任务。

## 阶段 3 · 插件 12 件 —— ✅ **已封闭（2026-10-17 12:21）**

| 判据 | 读数 | 谁验的 |
|---|---|---|
| 编译 0 error | ✅ **0 error**（写者报 10 警告＝基线；我复跑计数口径不同读到 20 行，非真差异） | ★ **协调者独立复跑** |
| `plugins.json` 12 件 / 12 effect / id 全中 | ✅ | ★ 协调者 Python 只读 |
| `stat_effect = 0`（无一件改玩家属性） | ✅ | ★ 协调者 Python 只读 |
| `maxStack` 全部 ≤ 3 | ✅ | ★ 协调者 Python 只读 |
| `SelfCheck` | ✅ **420/421 → 437/437** | ★ 实现者（运行期） |
| `PLUGIN-NO-STAT` | ✅ **19 件 → 0** | ★ 实现者 |
| 变异测试 | ✅ **430/437 时红：`hoe_swordwave = DamageFlat（属性类：本版已下线）`** → 改回 437/437 | ★ 实现者 |
| 12 件装前→装后 | ✅ **12/12 全部有数** | ★ 实现者 |
| `FxSpec` 12/12 两两不同 | ✅ `PluginFXAudit` 12/12 | ★ 实现者 |
| `HUD-AUDIT` | ✅ 通过 | ★ 实现者 |

**分母 `421 → 437`**：**＋12** = 新增 `DotTableChecks`（持续效果表单独一节，先单独验完才接插件线）；**＋4** = 纯算式那节 8 → 12 条（12 种新效果各自的算式）。其余条数不变。

**实现者报备的六处"规格没写、它定的"**（都在代码注释里留了理由，需用户复核）：
1. `None` 保留（解析兜底依赖它；"12 个取值"指 12 种真实效果）；
2. 每次命中附**「插件层数」层**（否则 `maxStack` 是死旋钮）；
3. **子爆伤害沿用半径比例 ×0.70**（否则 B 档凭空多 3 份全额伤害）；
4. 连环的"主爆点外 2.5m"沿「玩家 → 主爆点」方向外推（不是绕圆心分角）；
5. 每把 4 件 vs 6 槽 ⇒ "装满第 7 件被拒"用**合成探针**验（真数据填不满 6 槽）；
6. `FreeBurst` 读数取**叠满 3 层**（单层 12% 在 20 投里有 8% 概率一次不触发 ⇒ 不可复现）。

#### ⚠️ 值得记的一个坑：**读数窗口与生效窗口错开 = 假红**

`can_slow` 最初报"装前装后没变化"，真因**不是没接线**，而是 `SyncSlow` 只在 `Dots.Tick` 末尾跑
⇒ "打一下 → 立刻读移速"读到 **1.00**。修法：在 `PluginPostHit` 施加后**当场同步一次**。

> **判据：一个"没有变化"的读数，先怀疑"读得太早"，再怀疑"没接上"。**

## 阶段 3 · 插件 12 件（原始待验清单，保留备查）

---

## 阶段 4 · 界面 5 项 —— ⚠️ **4 / 5**（2026-10-17 12:24，协调者读图 `after-2-shop-slot-tip.png`）

| # | 判据 | 状态 | 证据 |
|---|---|---|---|
| ① | 战斗武器区**移到底部居中**，不与底部两条进度条重叠 | ❌ **未做** | 图证：武器区**仍在右上角**，截图底部为空 |
| ② | 商店每武器 **6 格**、空格**近黑**、有插件**按品质着色** | ✅ | 每把武器下一排黑槽；肥料那格**橙色发光**（传奇） |
| ③ | **悬停出详情**（名字/效果/层数） | ✅ | 「连环 传奇」＋「效果：连环（多炸一次）+1，二段 2.5」＋「可叠加层数：可叠加，上限 2」 |
| ④ | 种子框收紧（字号不变） | ✅ | 种子格 `220 → 196`；货架商品行 `86.83 → 79.97` |
| ⑤ | **商店不抖** | ✅ | 连拍 60 帧逐帧一致；两条真因已修：`_seedScrollPerCard = float[4]` 而种子袋 **6** 格、`rowH = rowAreaH / offerCount` 跟运行时 |

**① 做的时候注意**：武器区已被改成 **520×64**，原先那组 y 数（`[_virtualH-162, _virtualH-98]`）**不能沿用**，高度变了要重**新量**；`DrawPlotPrompt` 曾上移 72px，换位置后那个值**可能要重算**。

**已知偏离三处**：① 6 处颜色是 `Mix(基准,目标,t)` 插值而非直接赋色（`hoe_lifesteal` `#B0102E → #F0523A` 偏得最多，建议改纯赋色；其余 5 处仍在同一色相内）；② `hoe_swordwave` 多改了 `Core/Rim/Glow`；③ 1 帧白闪没单独出图（寿命 1/60s）。

**已知未验证四项**：① 整局真按键平衡没跑（12 件新数值强度未校准）；② 确定性重放复跑没做；③ 没跑 profiler（"60 份/秒泄漏消失"只有静态证据）；④ 湿痕不能 gradual dry-out（共享材质不许 alpha 淡出）。

## 阶段 4 · 界面 5 项

| # | 判据 | 怎么验 |
|---|---|---|
| 1 | 战斗武器区在**底部居中**，**不与「水壶储水 / 肥料」两条进度条重叠** | 量 y 范围 |
| 2 | 商店每武器 **6 格**，**空格纯黑**，有插件**按品质着色** | 图 ＋ 读取 `quality` 映射 |
| 3 | **悬停出详情**（名字 / 效果 / 层数） | 图 |
| 4 | 商品行与种子格**变窄**，**字号不变** | 前后图 ＋ 字号 diff |
| 5 | **商店不抖** | **改前 2~3 帧抖动证据 ＋ 修后稳定证据** |

---

## 阶段 5 · 美术

| # | 判据 | 怎么验 |
|---|---|---|
| 1 | **12 张 64×64 透明底图标**，命名 `icon_plugin_<id>.png` | 清单 ＋ `Tools/icon_gen/` 脚本 |
| 2 | **12 组插件特效**互通 `FxSpec` 8 通道且两两不同 | `PluginFxAudit` |
| 3 | 三种攻击特效**同一种颗粒语言**（棱面/鲜明色块/硬边） | 三特效同框图 |
| 4 | **不是 2D 广告牌**（实体多面体网格） | 代码＋图 |
| 5 | **有光影**（点光/闪光） | 图 |

---

## 阶段 5 · 音频 —— 🔄 **18 个音效已独立验过（零缺陷）／3 首 BGM 待出**

### 我（协调者）用 Python 独立复验的读数（不采信实现者的自检）

| 判据 | 要求 | 18 个 SFX 实测 |
|---|---|---|
| 采样率 | 44.1 kHz | ✅ **全部 44100** |
| 位深 | 16-bit | ✅ **全部 16** |
| 声道 | 立体声 | ✅ **全部 2** |
| 峰值 | **≤ −1 dBFS** | ✅ **全部精确 −1.00 dB** |
| 直流 | **`abs(mean) < 1e-4`** | ✅ **全部 5.7e-08 ~ 2.1e-07** |
| NaN / Inf | 0 | ✅ **全部 0** |
| 汇总 | — | ✅ **`flag_count = 0`** |

### 时长逐条对上规格

`sfx_ui_hover` **0.04s** · `sfx_ui_click` **0.06s** · `sfx_hoe_swing` **0.18s** · `sfx_hoe_hit` **0.12s** ·
`sfx_blast` **0.90s** · `sfx_defeat` **1.20s** · `sfx_water_low` **0.52s** · `sfx_coin` **0.22s**

### RMS 说明动态层次真的做了（★ 不是每条都压到同一响度）

```
ui_hover  -16.09 dB   ← 最轻（悬停就该几乎听不见）
ui_click  -15.22 dB
hoe_swing -14.39 dB
levelup   -13.73 dB
enemy_die -13.30 dB
hoe_hit   -13.31 dB
enemy_spawn -13.00
purchase  -12.69
shop_open -12.11
laser     -11.81
coin      -11.35
harvest   -11.15
plant     -11.00
interact  -10.88
defeat    -10.86
water     -10.71
water_low  -9.81
blast      -7.34   ← 最响（爆炸就该压住别的）
```

**动态范围 8.7 dB，顺序正确：悬停 < 点击 < 挥击 < 升级/命中 < 购买/开商店 < 激光 < 金币/收割 < 水警告 < 爆炸。**

### BGM 进度：3 / 3 ✅

**★ 三首的 BPM 都能从时长反推，且差不到 1 毫秒**

| 文件 | 实测时长 | 期望（拍数 × 60 / BPM） | 差 | BPM |
|---|---|---|---|---|
| `bgm_combat` | **58.182 s** | 128 拍 @132 = 58.1818 | **−0.0000 s** | ✅ 132 |
| `bgm_farm_day` | **91.429 s** | 128 拍 @84 = 91.4286 | **+0.0000 s** | ✅ 84 |
| `bgm_night` | **87.273 s** | 96 拍 @66 = 87.2727 | **−0.0000 s** | ✅ 66 |

> 只要 BPM 或小节数任一错，这个数就必然对不上 ⇒ **这是比"听起来像 84 拍"硬得多的判据。**

**★ 七条质量判据三首全过**

| 判据 | `farm_day` | `combat` | `night` |
|---|---|---|---|
| 声道 / 采样率 | 2 / 44100 | 2 / 44100 | 2 / 44100 |
| 峰值 | **−1.00 dB** | **−1.00 dB** | **−1.00 dB** |
| 直流 | 1.4e-09 | 5.8e-09 | 6.0e-09 |
| NaN | 0 | 0 | 0 |
| 接缝跳变 / 正常步长(p99.9) | 0.001236 / 0.086121 = **0.7%** | 0.003998 / 0.065002 = **6.2%** | 0.003540 / 0.245621 = **1.4%** |
| 判定 | **SEAM_OK** | **SEAM_OK** | **SEAM_OK** |
| RMS | −18.84 dB | −17.71 dB | −16.87 dB |

> **接缝判据：跳变要显著小于"文件内正常步长"，不是小于某个绝对值** —— 三首都在 6.2% 以内。
> 注：`bgm_farm_day` 的 RMS 曾被重写过一次（−14.47 → −18.84），新值更接近音乐床的合理响度。

#### `bgm_farm_day.wav` ✅（首轮验收留档）

| 判据 | 要求 | 实测 |
|---|---|---|
| 声道 / 采样率 / 位深 | 2 / 44100 / 16 | ✅ |
| 峰值 | ≤ −1 dBFS | ✅ **−1.00 dB** |
| RMS | ≈ −16 LUFS | ✅ **−14.47 dB** |
| 直流 | `< 1e-4` | ✅ **4.2e-10** |
| NaN | 0 | ✅ **0** |
| **时长＝整数小节** | 84 BPM × 4/4 × **32 小节** | ✅ **91.429 s** |

**★ 时长能反推 BPM（可复算的硬判据）**
```
128 拍 × (60 / 84) = 91.4286 s     实测 91.429 s     ⇒ BPM 精确为 84，差 < 1 ms
```
> 任何一个 BPM 或小节数不对，这个数就对不上 —— 比"听起来是 84 拍"可靠得多。

**★ 循环接缝（BGM 唯一容易做错的地方）**
```
取尾 50 ms 与首 50 ms：
  |tail_last − head_first| = 0.000870
  文件内相邻采样差的 99.9 分位   = 0.114548    ⇒ 接缝跳变只有最大正常步长的 0.76%
  seam_rms = 0.0378                            ⇒ 接缝处仍有音频，不是静音断层
  判定：OK
```
> **判据：接缝跳变要显著小于"文件内正常步长"，而不是"小于某个绝对值"。**
> 因为不同音量的曲子绝对值差异很大，而"相对正常步长"才是"听不出来"的真实含义。

### 还差

| # | 判据 | 状态 |
|---|---|---|
| 1 | 3 首 BGM：**整数小节**、**首尾无淡出**、44.1k/16bit/立体声 | ⏳ **1/3（`bgm_farm_day` 已过；`bgm_combat` / `bgm_night` 待出）** |
| 2 | BGM **无缝循环**（最后一拍与第一拍在音乐上连续、和弦闭合） | ⏳ |
| 3 | `Tools/audio_gen/report.txt` 含每文件五个字段 | ⏳ |
| 4 | 生成脚本能**一键重跑** ＋ `README.md` 写明调性/BPM/小节/DSP | ⏳ |

> **BGM 是最难的三个**：SFX 错了听得出，BGM 错了只在**循环接缝**那一瞬间听得出，而接缝只在长时间循环后才暴露 ⇒ 验收要**循环两遍**听/看波形，不能只看单个文件。

## 阶段 5 · 音频（原始待验清单，保留备查）

| # | 判据 | 怎么验 |
|---|---|---|
| 1 | 3 首 BGM：**整数小节**、**首尾无淡出**、44.1k/16bit/立体声 | 脚本自算 ＋ 时长/拍数 |
| 2 | 18 个 SFX：**峰值 ≤ −1 dBFS**、**首尾 2~5ms 淡入淡出**、**`abs(mean) < 1e-4`** | 脚本自算 |
| 3 | 全部 **无 NaN / Inf** | 脚本自算 |
| 4 | `Tools/audio_gen/report.txt` 含**时长 / 峰值 / RMS / 采样率 / 声道** | 读文件 |
| 5 | 脚本能**一键重跑** ＋ `README.md` 写明调性/BPM/小节/DSP 参数 | 读文件 |

---

## 全局门禁（每一阶段都要重跑）

1. `dotnet build Seedfall.Runtime.csproj -t:Rebuild` **0 error**（**一次只 Rebuild 一个工程**；连跑两个会出 `CS0006` 假错误）
2. `dotnet build Seedfall.Tests.csproj -t:Rebuild` **0 error**
3. `DebugApi.SelfCheck()` **全绿**，且**条数从 425 变为新值时必须交代是哪几条**
4. `DebugApi.HudAudit()` → 等一帧 → `HudAuditReport()` ⇒「HUD-AUDIT 通过」（**它不在 425 条里**）
5. 抓图：`playing && !paused && !compiling`、**Unity 提到前台**、**< 400KB 一律当黑图重拍**
6. **绝不用 PowerShell「读→改→写回」含中文的文件**（会把中文编成乱码并写 BOM ⇒ 表解析失败 ⇒ 游戏不启动）
7. 改 `Data/*.json` 后：**摸 mtime → `refresh` → 再 play**（只 refresh 不够，导入缓存会留旧版）
8. **桥上的 `pause` 是 toggle 不是设置**（`Assets/Editor/YoweiBridge.cs:2073` 是 `isPaused = !isPaused`）⇒ 发两次会互相抵消。要暂停发一次、要恢复再发一次，**更安全的做法是根本不发它**。
9. `/command` 的 body 必须是 `{"command":"play"}`，**不是** `{"action":"play"}`（后者返回 `{"ok":false,"error":"command not allowed"}`）。
10. **判断一个任务"在思考"还是"停滞"，看两个信号**：
    - **停写时长**：3 分钟是思考，25 分钟不是；
    - **有无新产物**：停写但**出了截图/报告/日志** = 在验证；停写且**后续零产物** = 停滞。
    ★ 实例：`Goods.cs` 15.6h / `RunSession.cs` 1.9h / `plugins.json` 15.5h 全旧、且 25 分钟无任何新产物 ⇒ 判停滞并重派；而三条都在 3 分钟内有写入时，即使文件停写也属于在验证。
11. **要证明某个 UI 在抖/在变，只测那个 UI，不要测整个屏幕。**
    - **错误实例**：`before-2-shop-a.png` vs `b.png` 整幅逐像素差 **17.722%**、bbox 是**整个画布**（`x[0..1919] y[0..1079]`）⇒ 那是**背景 3D 场景在动**（敌人/粒子/昼夜光/作物摇摆），**不是 UI 抖动**；而且这个结论一戳就破。
    - **正确做法**：① 只 diff **面板那块矩形**；② 更硬 —— 在 `DebugApi` 里打印两帧的 **`panel.x / y / width / height`**，**数值相等 = 没抖**。数值判据不受场景动画干扰。
    - **同类错误**（本会话我自己犯过）：用**整幅图的字节数**证明"布局没变"—— 而实际错位时字节数只差 **0.2%**。
    - **共同病根**：拿**整幅的量**去证**局部的性质**。
12. **写任务书时，每一个路径都要先 `glob` 一次** —— 尤其是"你可以改这个文件"那一类。
    - **实例**：我在美术任务里写了 `Assets/Scripts/WorldView.cs`（**不存在**），真实路径是 `Assets/Scripts/Bootstrap/WorldView.cs`。
    - **为什么只对"允许类"致命**：**禁止类**写错 ⇒ 它少改一个文件，无害；**允许类**写错 ⇒ 它**找不到就放弃**，整块功能不做。
    - 同一份任务书里"不要碰 `RunSession.cs`"是对的 —— 因为那条**核对过**（阶段 3 正在改它）。
13. **并行任务存在时，Rebuild 前必须先确认没有写者还在动**。
    - **判据**：`Get-ChildItem Assets/Scripts -Recurse -Filter *.cs | Sort LastWriteTime -Desc | Select -First 1`，取其**距今秒数 ≥ 180** 才 Rebuild。
    - **实例**：写者报 `0 error / 10 warnings`，而我并发跑 `-t:Rebuild` 读到 **178 个错** —— 那是两边**互相清 `obj`** 的假读数。
    - ⇒ 原门禁"一次只 Rebuild 一个工程"在并行时**不够**：它只管了"我自己的两次"，没管"别人正在写"。
    - ⇒ **读到的错误数若与写者报告差一个数量级，先怀疑自己的编译时机，不要怀疑代码。**
14. **数据表的字节层要单独查一道** —— 编译与断言都发现不了它。
    - **三条判据**：`BOM == False`、`json.loads` 成功、**`U+FFFD` 替换字符数 == 0**（乱码的硬证据）。
    - **实例（2026-10-17 12:18 实测，四张表全过）**：`seeds.json` 14,210 / `items.json` 10,691 / `plugins.json` 5,983 / `tuning.json` 8,592 字节，全部 `BOM=False`、`json=True`、`repl=0`。
    - **注意**：文件里出现少量 `latin1` 区字符（全角标点、`·`、`—`）**不是乱码** —— 判乱码看 `repl`，不看"有没有非 ASCII"。
    - **为什么必须独立查**：字节层坏掉时**编译是绿的、断言也是绿的**，只有运行时才表现为「配置表载入失败，游戏不会启动」（本会话发生过一次，根因是 PowerShell 读→改→写把它编成了 GBK 乱码并写了 BOM）。
15. **判断"停没停"，看**间隔的导数**，不是看间隔本身**。
    - **卡住**：最后一次写入之后**再也没有** —— 曲线是"**平后垂直**"。
    - **收尾**：间隔**越来越长** —— 曲线是"**逐渐变陡**"。
    - **实例**：某任务的写入间隔依次为 **20s → 39s → 60s → 75s**，单调变宽 ⇒ 判**收尾**（改得越来越少），按斜率每轮 +15~20s，约 2~3 分钟会越过 180s 的稳定阈值。
    - ⇒ 同样"很久没写"，一个是**结束**，一个是**还没放开**；只看"多久没写"会把两者判成同一件事。

---

## ★ 阶段 0 / 1 / 2 封闭记录（2026-10-17 12:00）

**最终读数**：`dotnet build 0 error` ｜ `自检 PASS：419/419` ｜ `HUD-AUDIT 通过` ｜ 条数 **425 → 419（＋9 −15，逐条列于下）**

### ★ 一处实现者主动报备的**设计改动**：`grade` 改了三处

| id | 原档 | 新档 | 原因 |
|---|---|---|---|
| `starvine` | B | **C** | C 档要有**快型** |
| `healing_clover` | A | **B** | B 档要有**重型** |
| `holy_tree_guard` | S | **A** | A 档要有**重型** |

`waterDrain` 随 grade 同步（D6 / C4 / B2 / A1 / S1）。

> ⚠️ **这条的副作用**：`SEED-ARCHETYPE` 判「重/快」用的**档基准 `t` 必须按档从数据取**，不能写死旧表（`D16/C27/B48/A85/S148`）。
> 因为 `starvine` 挪到 C 之后 `t=13.5`，**只有相对 C 的 27 才是"快"**；若仍拿旧 B 的 48 比，它照样"快" ⇒ 判据会歪。
> 我已独立复算验证：按档取基准后 **D 4/4、C 4/4、B 3/3、A 3/3、S 2/2，spread 全 0.00%，三元组无重复**。

### ★ 一处诚实的**限制**：整数约束下 70/30 不可达

`g` 与 `e` 必须是整数 ⇒ D 档整条等式 `g + 2e = 8` 只有 8 个单位 ⇒ 金型只能 `(6,1)=75%` 或 `(4,2)=50%`，**没有 70%**。
B 档是 70.7%、A 67%、S 69.9%。

> **判据是「等式相等 ＋ `SEED-TIER-RATE` ≤2%」，不是「百分比看起来像不像 70/30」。**
> 因为整数约束下精确比例不可达，而**等式相等**才是"同档无支配关系"的真正条件。

### ★ 分母账（425 → 419）

**＋9**：6 条命名护栏（`SEED-ID-STABLE` / `SEED-NO-MECH` / `SEED-ARCHETYPE` / `SEED-TIER-RATE` / `ITEM-COVERAGE` / `ITEM-NO-RULE`）＋ 3 条 v179 结构断言（同档售价唯一 / 两类别 6+18+0 / 6 件入门全单属性）。

**−15**（全部编码**旧设计**，删时在原位写了理由）：
4 条"档位↔机制"｜1 条"省水机制真表有人用"｜3 条 v176"同档全等"｜4 条道具旧结构｜3 条机制行为（三件道具已删）。

### ★ 三条超出"纯数据"的改动（已报备）

1. `TableDatabase.IsFractionStat`：只对 `CritRate` 放开整数（`Add 1`=+100% 顶满、`Percent` 从 0 乘=死道具，只有 `0.06` 能表达 +6%）。`CritDmg` **不在豁免内**（基础 2.0，Percent 乘天然可见）。`SelfCheck` 的两条整数约定断言**读同一份判定** ⇒ 不会两边打架。
2. `seeds.json` 的 `grade` 三处（见上表）。
3. `DebugApi.SeedCardsDemo()`：只为拍"四株同框"，走商店买种同一个 `Bag.AddSeed`，不动任何数值。

### ★ 未做到

- **"四株并排卡片"没有真的做成并排**：开局选种弹窗按布局只装 3 张（`initialChoices=3`，面板宽 940 只够 3×306），所以四株同框是**主界面植物面板 2×3**拍的。真做成 4 张并排需要改 UI 布局，不在本轮范围。

## ★ 六条护栏的「故意改坏」原文（2026-10-17，证明断言**真的会红**且**能定位**）

做法：把两张 JSON 临时改坏（**不动 `SelfCheck.cs`**）→ `refresh` → 跑 → 贴原文 → 用备份还原 → 再跑全绿（sha 与改坏前一致：seeds `0da93f9e…` / items `ba376be7…`）。

```
自检 FAIL：413/419 通过
FAIL SEED-ID-STABLE    清单里的 id 在表里找不到：chili_mage
FAIL SEED-NO-MECH      1 条仍带机制：radish_guard=Haste
FAIL SEED-ARCHETYPE    D 档只有 3/4 种（金 有／验 无／重 有／快 有）
FAIL SEED-TIER-RATE    D 档差 45.45%：radish_guard 0.6875 金/秒 vs pea_shooter 0.375
FAIL ITEM-COVERAGE     缺增益 0 条、缺减益 1 条 ⇒ CritRate 缺减益
FAIL ITEM-NO-RULE      1 件仍带 rule：wax_seal=harvest_dew
```

> **判据：一条好断言的失败消息必须说出「是谁、差多少」。** 只说"档内不一致"的断言无法定案。
> 六条全部满足 —— 它们同时给出了**违规对象**（`chili_mage` / `radish_guard` / `wax_seal` / `CritRate`）与**偏离量**（`45.45%` / `0.6875 vs 0.375` / `3/4 种`）。

### `ITEM-COVERAGE` 的「改前红」用的是**真数据**

```
旧 20 件表：  缺增益 10 条、缺减益 12 条
              （MaxHp / Shield / MoveSpeed / AtkRange / CritRate / CritDmg / Knockback /
                ExpMult / GoldMult / HeartDropRate 各缺增益；MeleeDamage 缺减益…）
旧 20 件表：  ITEM-NO-RULE ⇒ 4 件仍带 rule：greedy_sickle / dew_flask / haggle_charm / golden_sickle
改完 24 件：  419/419 全绿，ITEM-COVERAGE 的 detail = "15/15 属性双向覆盖"
```

### ★ 两条重要更正（我提过、被实现者纠正的）

1. **`CritDmg` vs `CritDamage`**：断言里写的是 **`StatId.CritDmg` 枚举成员**（拼错会**编译不过**），表里写 `"CritDmg"` ⇒ 同一个枚举。
   ★ 若改成字符串 `"CritDamage"`，`StatIds.TryParse` 会失败 ⇒ **那条效果被丢弃 ⇒ 断言反而变红**。⇒ **以枚举为准。**
2. **`SelfCheck` 的分母是 419（不是 425）** —— 加/改断言后分母变过。
   ★ 任何后续改动都要报告"从多少变成多少、哪几条变了"，否则"全绿"这个读数没有可比性。

## 已知的、不属于本轮范围的遗留

- **`商店详情1#0` 的 2 条 HUD 超宽**（739px / 可用 341px）：改前就有，修法是加宽栏或改短文案，**需用户点头**
- **`DrawPlantPanel` 一帧都不再被画**（选种弹窗与它共用 `Prepare` 条件）：按用户原话应是"那一刻不该出现"而不是"永远不出现"，**改一行条件即可**
- **像素字 ⑧ 的 4 个滚动区仍糊**：`BeginScrollView` 的裁剪框随矩阵缩放，彻底解决要把 4 处改成手写滚动
