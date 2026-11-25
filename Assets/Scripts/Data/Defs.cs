using System;
using UnityEngine;

namespace Seedfall
{
    // ---------------------------------------------------------------- 枚举
    public enum SeedGrade { D = 0, C = 1, B = 2, A = 3, S = 4 }
    public enum AttackType { Physical, Magic, Ailment, Support }
    public enum AttackKind { Melee, Ranged }
    public enum SupportMode { None, Heal, Guard }
    public enum EnemyKind { Grunt, Shooter, Jumper, Boss, Wander }
    public enum Team { Player, Enemy }
    public enum PlotState { Locked, Empty, Dug, Seeded, Growing, Grown }
    public enum PlotAction { None, Dig, Sow, Water, Fertilize }
    /// <summary>
    /// 会话阶段。
    ///
    /// ★ 没有 Victory（本次删除）★
    /// 计时制是**无限模式** —— 唯一的结束条件是玩家死亡。原 `Victory` 只能由
    /// "守住全部 N 波"设置，而那条判定依赖已删除的 `TotalWaves`；`SetPhase(PhaseState.Victory,`
    /// 在工程里本来就是 **0 命中**，`RunSimulator.Victory` 因此恒为 false。
    /// 一个永远不会被置上的枚举成员只会让读代码的人以为"这游戏能通关"，所以删掉它，
    /// 而不是留一个恒 false 的分支。
    /// </summary>
    public enum PhaseState { Boot, Prepare, Combat, LevelReward, Shop, Defeat, Paused }
    public enum AilmentKind { None, Poison, Burn, Freeze }
    public enum DamageType { Physical, Magic, Ailment }

    public enum StatId
    {
        // v23 新属性表：删除 Regen/Leech/AtkPhys/AtkMagic/AtkAilment/AilmentSpeed
        // v45：拆成「攻击伤害（加法）」与「攻击倍率（百分比）」两条并列属性；
        //      删除与攻击倍率语义重复的 DmgMult（伤害倍数）；
        //      删除无任何读取方的 WaterCap（水壶已改为 WaterAmmo 弹药制）与 WorkCap（授予它的道具早已删除）。
        //
        // ────────────────────────────────────────────────────────────────────────
        // v162（属性体系重构 A1）：**删除通用的「攻击伤害 AtkDamage」，改为三种独立的伤害类型**。
        //   近战伤害 MeleeDamage  → 锄头
        //   远程伤害 RangedDamage → 水壶
        //   爆炸伤害 BlastDamage  → 肥料
        //   公式随之简化为 `(武器基础伤害 + 对应伤害属性) × 伤害倍数`（见 DamageResolver.AttackFormula）。
        //
        // ★ 约定：这三个属性是**整数值语义** ★
        //   `StatModifier.Value` 的**类型仍然是 float**（本次刻意不改类型，改它会牵动整套属性管线的
        //   加减乘与上限裁剪）。约束只加在**设计层**：往这三条属性上写东西时**只允许写整数** ——
        //   道具走 mode=Add 的整数、抽卡走 amount 的整数、玩家初始值取整数。
        //   这样面板上不会出现"近战伤害 +0.4 点"这种读不出来的数。
        //   这条与既有的「所有道具数值仍满足整数/整数%约定」是同一个约定，不是新规矩。
        //
        // ★ 为什么可以直接改编号（ExplodeDamage/ExplodeRange 被上移）★
        //   见下面 HeartDropRate 前的说明：属性不按编号持久化。
        // ────────────────────────────────────────────────────────────────────────
        MaxHp = 0, Shield = 1,
        MeleeDamage = 2, RangedDamage = 3, BlastDamage = 4,
        AtkMult = 5,
        // ★★ v195（用户原话："删除爆炸倍率这个参数删掉，肥料伤害=（基础伤害+爆炸伤害）*伤害倍率"）★★
        //   `ExplodeDamage`（爆炸倍率，乘数 2.5 = 250%）**整条属性删除** ✗
        //   ⇒ 肥料那条线不再有第二层乘区：三把武器同一个式子
        //     `(武器基础 + 对应伤害属性) × 伤害倍率 × 暴击` ✓
        //   ⇒ `ExplodeRange`（爆炸**范围**）保留 —— 它是半径倍率，与"伤害"无关 ✓
        //   编号：删中间成员会让后面全部前移（安全，理由见下面 HeartDropRate 前的说明）⇒
        //   v195 起连续编号 0..20，`StatIds.Count = 21` ✓
        ExplodeRange = 6,
        CritRate = 7, CritDmg = 8, AtkSpeed = 9, AtkRange = 10, MoveSpeed = 11,
        Dodge = 12, MaxPlots = 13, Knockback = 14,
        // v107：删除 Luck（需求）。
        // ★ 删中间成员会让后面所有编号前移 ★
        // 这里是安全的：属性**不按编号持久化**——种子/道具表里引用属性用的是**名字**
        // （如 "stat": "MeleeDamage"），StatIds.All 又是 Enum.GetValues 动态生成的，
        // 所以前移不会打乱任何已存数据。若将来有按编号存盘的地方，这里就要改成留空占位。
        HeartDropRate = 15, HeartHeal = 16, ExpMult = 17, GoldMult = 18,

        // ────────────────────────────────────────────────────────────────────────
        // ★ v191：重新加回 **幸运值**（用户需求①）+ 新增 **小猪刷新率**（需求③）★
        //
        // 编号从 20 开始（19 是 GoldMult，见上）—— 现有 20 个成员一个都不动，
        // 所以这两条是**纯追加**：`StatIds.Count` 20 → 22。
        //
        // ★ `Luck` 必须是**分数属性**（0..1 小数，0.05 = 5%）★
        //   用户原话："幸运值，百分比形式，初始值为 5%"；抽卡给"3% 一张卡"。
        //   ⇒ 语义与 `CritRate` 完全同形（同一个 0..1 单位、同一种 `Add 3` = +3% 的卡）
        //   ⇒ 已加进 `TableDatabase.IsFractionStat`（那里是**唯一**的判定，
        //      表校验 / 自检 / 抽卡 / 面板四处都读它，不各写一份）✓
        //
        // ★ 基底**必须**是 0.05 而不是 0（这一条是硬要求，见 `IsFractionStat` 的长注释）★
        //   `Percent` 是**乘法**：`0 × (1 + p)` 恒为 0 ⇒ 基底为 0 时任何倍率卡都变成死的 ✗
        //   而 `Add` 卡（`amount 3` ⇒ `/100`）两者都能用 ✓
        //   所以基底取 5%（`tuning.player.luck`），面板印"当前 − 基底" ⇒ 开局天然 `+0%` ✓
        //
        // ★ `PigRate` 是**等级属性**（0~3 级），不是概率 ✗ ★
        //   与 `HeartDropRate` 完全同构（同一套等级制：低/中/高/MAX，同一组数值 2/4/6/8%）
        //   ⇒ 存等级、查表换算成概率，理由写在 `TuningDef.PigRateByLevel` 上 ✓
        Luck = 19, PigRate = 20
    }

    public static class StatIds
    {
        /// <summary>
        /// 属性总数。**必须与 StatId 的成员个数逐一相符** ——
        /// StatBlock 用它开数组（`new float[StatIds.Count]`）并用它做裁剪循环，
        /// 少一个会让最后那几个属性**静默失去上限裁剪**（不报错、不抛异常，只是偶尔算出超界的值）。
        /// 自检里有 `StatIds.All.Length == StatIds.Count` 一条专门盯它。
        /// v162：18 → 20（删除 AtkDamage 1 个、新增三种伤害类型 3 个）。
        /// v191：20 → 22（新增 `Luck` 20 / `PigRate` 21）。
        /// v195：22 → **21**（删除 `ExplodeDamage`；编号重新连续，见 `StatId` 的说明）。
        /// </summary>
        public const int Count = 21;
        public static readonly StatId[] All = (StatId[])Enum.GetValues(typeof(StatId));

        public static bool TryParse(string name, out StatId id)
        {
            id = StatId.MaxHp;
            if (string.IsNullOrEmpty(name)) return false;
            foreach (StatId s in All)
            {
                if (string.Equals(s.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    id = s;
                    return true;
                }
            }
            return false;
        }
    }

    // ---------------------------------------------------------------- 原始 DTO（与 JSON 一一对应，枚举用字符串）
    [Serializable] public class SeedDefRaw
    {
        public string id, name, grade, attackType, attackKind, supportMode, special, desc;
        /// <summary>
        /// 机制标识 —— ★ 只允许 `PlantMechanics` 登记的那 9 个名字，或 `"None"` ★
        /// v172：旧注释在这里举例 `Taunt/Thorns/Chain/...` ✗ —— 那九条**已随"植物参战"删除** ✓
        ///   现在写旧名字会被 SelfCheck 判红（`PlantMechanics.IsKnown` 挡它）⇒ 表里写不出死机制 ✓
        /// </summary>
        public string mechanic;
        public float baseValue, inheritMult, growTime, baseHp, baseShield, attackInterval, range, moveSpeed;
        /// <summary>v23：蓄水（整数）——上限与每 5 秒消耗。</summary>
        public int waterMax, waterDrain;
        public int price;
        /// <summary>
        /// ★ v179：**每格收益的两个基数**（金币 / 经验），与 `price`（种子售价）彻底脱钩 ★
        ///
        /// ★ 为什么必须从 `price` 里拆出来（推翻 v165 那条"一个旋钮不打架"）★
        /// v165 用 `price/5` 与 `price/10` 推导收益，理由是"`price` 已是这株值多少的唯一依据，
        /// 再开两个字段会让买价与收益两个旋钮打架" ✓
        /// 但 v179 的需求是**同一档内金币与经验可以反向分配**：
        ///     高金低验（金型） / 低金高验（验型） / 都高但周期长（重型） / 都低但周期短（快型）
        /// 而 `price/5` 与 `price/10` 是**同一个数按两个比例缩放** ⇒ 金币与经验的比恒为 2:1，
        /// **同档内反向分配在数学上不可能** ✗（这不是"能不能调"的问题，是这个参数化表达不出来）
        /// ⇒ 拆成 `goldYield` / `expYield`，`price` 退回它本职的"种子售价"✓
        ///
        /// ★ 拆开之后靠什么防"两个旋钮打架"★
        /// 判据不是"两数相等"，而是**不能出现"更便宜还更强"** ✓
        /// 由 `SelfCheck` 的 `SEED-TIER-RATE` 守：同 `grade` 内任意两株
        /// `(goldYield + 2×expYield) / growTime` 相差 ≤ 2% ⇒ 同档内没有支配关系 ✓
        /// （经验按 1 金 = 2 经验折算的理由写在 `SEED-TIER-RATE` 那条断言的注释里）
        /// </summary>
        public int goldYield, expYield;
    }

    [Serializable] public class MutationDefRaw
    {
        public string id, name, desc;
        public float inheritMultBonus;
    }

    [Serializable] public class SeedTableRaw
    {
        public SeedDefRaw[] seeds = Array.Empty<SeedDefRaw>();
        public MutationDefRaw[] mutations = Array.Empty<MutationDefRaw>();
    }

    [Serializable] public class EnemyDefRaw
    {
        // v47：删除 hpPerWave / hpPerBoss / touchDamagePerWave 三个字段
        //      —— 前两个从未被读取，第三个是线性成长；现在生命与伤害统一走 tuning.enemies 的 2 的回合次方。
        public string id, name, kind, color;
        public float baseHp, moveSpeed, touchDamage, touchCooldown,
                     attackRange, safeRange, projectileDamage, projectileSpeed, attackInterval, radius,
                     jumpDamage, jumpRadius, jumpCooldown, jumpRange, jumpWindup;
        public int gold, exp;
    }

    [Serializable]
    public class EnemyGrowthDef
    {
        /// <summary>2 的回合次方的封顶值（64 → 第 7 波起不再指数增长，避免后期数值爆炸）。</summary>
        public float expCap = 64f;
        /// <summary>生命成长系数：生命 = 基础血 × (1 + hpGrowth × (expTerm − 1))。</summary>
        public float hpGrowth = 0.05f;
        /// <summary>伤害成长系数：伤害 = 基础伤害 × (1 + dmgGrowth × (expTerm − 1)) —— 生命与伤害同一套指数。</summary>
        public float dmgGrowth = 0.05f;
        /// <summary>BOSS 每多一档 bossIndex 的额外血量加成。</summary>
        public float bossHpGrowth = 0.10f;
        /// <summary>BOSS 的指数用立方根压平（否则第 10 波 BOSS 血量会离谱）。</summary>
        public bool bossFlattenExponent = true;

        // ── v115：新的成长曲线系数（需求："不要直线成长，需要曲线递增"）──
        //   数值倍率 = 1 + a × (波次−1) + b × (波次−1)²
        //
        // `a` 是全程的线性底子，`b` 让**后期加速**——两项分工明确、可以各自调。
        //
        // ★ 为什么换掉上面那套指数项 ★
        // 旧实现是 `成长项 = min(2^(波次−1), 64)`，而 2^6 = 64 ——
        // **成长在第 7 波就封顶了**，而一局有 20 波：后 13 波敌人数值完全相同。
        // 指数曲线看着"有成长"，实际是"很早就停"。
        //
        // 新曲线全程连续、无封顶，且每波增幅**本身在变大**（14% → 29%）。
        //
        // **HP 与伤害用不同系数是刻意的**：HP 涨得比伤害快。
        // 两者同步的话，后期敌人一击就能秒杀玩家——那是"不讲理"，不是"变难"。
        public float hpStepA = 0.12f, hpStepB = 0.006f;
        // ★★ v201：**`dmgStepA/B` 的锚点从「第 20 分钟」换成「第 30 分钟」** ★★
        //   （用户原话：『伤害修改，30分钟，基础敌人碰撞伤害达到20，弹幕15，跳匝25，
        //     boss碰撞25，弹幕20，跳30』）
        //   第 30 分钟倍率 = WaveGrowth(30) × post20(29)，`post20(29) = 1 + 0.15×(29−19) = 2.5` ⇒
        //   `a = (目标倍率/2.5 − 1)/71.05`（`b = 0.05a` 的形状锁不变）✓
        //   改前 0.0242915 / 0.001214575（第 20 分 1.900×、第 30 分 6.8152× ⇒ 5 / 3 / 6 点）；
        //   改后 **0.1467779 / 0.0073389**（第 30 分 **28.5714×** ⇒ 20 / 15 / 25 点）✓
        //   ⚠️ 三种普通伤害共用**这一条**曲线 ⇒ 第 30 分钟的比值只能由 base 决定。
        //   用户要 4 : 3 : 5，而旧 base 是 4 : 2.667 : 5.333 ⇒ 对不上 ⇒ 同轮改了
        //   shooter.projectileDamage 与 jumper.jumpDamage（碰撞 base 0.7 当锚，
        //   见 `enemies.json` 的 `_note_v201` 与 `tuning.json` 的 `_note_v201_dmg`）✓
        public float dmgStepA = 0.08f, dmgStepB = 0.003f;

        /// <summary>
        /// ★ v190：**精英家族（`EnemyKind.Jumper`）的独立 HP 曲线振幅** ★
        ///
        /// ── 为什么必须有第二条 HP 曲线 ──
        /// 用户给的两个 20 分钟目标是**互斥**的：
        ///   基础兵 80 血 / base 2.8 ⇒ 需要 28.571 倍
        ///   跳跃者 200 血 / base 16.8 ⇒ 需要 11.905 倍
        /// 而 `HpForWave` 只有一条曲线 ⇒ **同一个倍率不可能同时满足两个目标** ✗
        /// 两条出路：
        ///   A. 单曲线 28.571 倍 + 把跳跃者 base 压到 `200/28.571 = 7.0`
        ///      ⇒ 副作用：它第 1 分钟只有 7 血 ⇒ 玩家（每击 4）**2 刀就死**（现在 5 刀）
        ///      ⇒ 精英不该在**没人要求的时间点上**变弱 ✗
        ///   B. 保留 base 16.8，给精英**自己的振幅**（本条）
        ///      ⇒ 第 1 分钟仍是 16.8（5 刀，早期一字不动），第 20 分钟精确 200 ✓
        /// ⇒ 选 B ✓
        ///
        /// ── 语义 ──
        /// 公式与标准曲线**完全相同**（<see cref="EnemyDef.WaveGrowth"/>），只是换振幅：
        ///   `eliteHpStepA ≤ 0`  ⇒ **关闭**，精英回落标准 `hpStepA/hpStepB`（老数据/老读者行为不变）
        ///   `eliteHpStepA > 0`  ⇒ 精英用 `eliteHpStepA / max(0, eliteHpStepB)`
        ///                         （`eliteHpStepB` **允许为 0** ⇒ 精英走纯线性，这是合法形状）
        /// 当前值 0.294326 / 0.01471628 ⇒ 第 20 分钟 16.8 × 11.9048 = 200.00 ✓
        ///
        /// ★ 防"死配置"★ 这两个键**真的被读**：唯一消费点是 `EnemyDef.HpCurveCoefficients`，
        /// 而 `HpForWave` 与探针 `ProbeV190.ProbeCurve`（打印"同一只跳跃者走两条曲线各是多少"）
        /// 都走那一个函数 ⇒ 不存在第二份实现，也不存在"配了没人读" ✓
        /// </summary>
        public float eliteHpStepA, eliteHpStepB;

        /// <summary>
        /// ★ v190：**BOSS 专用的 HP 曲线振幅**（`Kind = Boss` 时启用，`bossHpStepA > 0` 为准）★
        ///
        /// ── 为什么 BOSS 必须单列一条 ──
        /// 标准曲线被解到 20 分 **28.571×**（基础兵 80 血这条需求逼出来的）⇒ BOSS 会跟着变成
        /// `300 × 28.571 = 8571` 血（改前 1634）✗ 而同一版伤害曲线下它还带 **15 点跳砸** ——
        /// 对 10 血玩家**一次即秒**。那不是"更难的 BOSS"，那是**不可玩** ✗
        ///
        /// ⇒ 目标（用户裁决）：20 分 BOSS 血 **3000~3500**（≈ 改前 1634 的 2 倍），
        ///   第 20 分钟三招 `碰撞 ≈4 / 子弹 ≈2.7 / 跳砸 ≈8.3`，**玩家 25 血能挨 2~3 下跳砸**。
        /// ⇒ 取值：`bossHpStepA = 0.2699055 / bossHpStepB = 0.01349528`（形状同样 b/a = 5%）
        ///   ⇒ `1 + 37.05a = 11.000` ⇒ 20 分 = `300 × 11.000 = 3300` ✓（2.02× 改前）
        ///
        /// ★ 为什么不用"BOSS 固定成旧值 0.12/0.006"（那也能得到 1634）★
        ///   那等于把两个浮点数写死在 `Defs.cs` 里 ⇒ ① 表不是唯一真相（v170 的结论）
        ///   ② 想动 BOSS 曲线必须改代码 ③ 而且 1634 低于用户自己写的 3000~3500 目标带。
        ///   现在这两个键**把一个特例变成了一次配置**：要回到旧值就填 0.12 / 0.006，
        ///   要更肉就填更大 —— **不需要碰代码** ✓
        /// </summary>
        public float bossHpStepA, bossHpStepB;

        /// <summary>
        /// ★ v190：**BOSS 专用的伤害曲线振幅**（`bossDmgStepA > 0` 为准）★
        ///
        /// 标准伤害曲线被解到 20 分 **8.571×**（6/4/8 三招目标逼出来的）⇒ BOSS 会得到
        /// `碰撞 7.2 / 子弹 4.8 / 跳砸 15` ✗ 而用户的目标带是 `4~5 / 2.5~3 / 8~10`。
        /// 两套倍率（8.571 vs 4.762）不可能共用一条曲线 ⇒ 增这两个键 ✓
        ///
        /// ★ BOSS 的**基础伤害一个字没改**（0.84 / 0.56 / 1.75，全保持 v180 的值）★
        /// 只换振幅 ⇒ 第 1 分钟与改前逐位相同（曲线起点 1.0），第 20 分钟落进目标带：
        ///   `A = 4/0.84 = 4.761905` ⇒ 碰撞 **4.00** / 子弹 **2.67** / 跳砸 **8.33** ✓
        ///   `bossDmgStepA = (4.761905 − 1)/37.05 = 0.10153589`，`bossDmgStepB = 0.05a ✓`
        ///
        /// ★★ v201：**当前值已不是 0.10153589** ★★ 用户把锚点从「第 20 分钟 4~5 / 2.5~3 / 8~10」
        /// 换成了「**第 30 分钟 25 / 20 / 30**」⇒ `A = 25/0.84 = 29.761905`（已含 `post20(29) = 2.5`）
        /// ⇒ `bossDmgStepA = (29.761905/2.5 − 1)/71.05 = **0.1534801**`、`b = 0.007674005` ✓
        /// BOSS 三招的比值 1.25 : 1 : 1.5 与旧 base 的 1.5 : 1 : 3.125 对不上（弹幕低 20%、
        /// 跳砸高 108%）⇒ 同轮把 boss.projectileDamage 0.56 → **0.672**、
        /// boss.jumpDamage 1.75 → **1.008**（碰撞 base 0.84 仍当锚，见 `enemies.json` 的 `_note_v201`）✓
        /// ★ 与标准曲线现在只差 4.2%（28.571 vs 29.762）—— 但仍**不合并**：
        ///   合并后第 30 分钟碰撞会落到 24.0 而不是 25 ✗（`tuning.json` 的 `_note_v201_boss_dmg`）
        /// </summary>
        public float bossDmgStepA, bossDmgStepB;

        /// <summary>
        /// ★ v191：**前段压低项**（用户要求"下调前期敌人数值"）★
        ///
        /// ── 为什么不能直接压 base、也不能压 a/b ──
        /// 上一轮刚把 20 分钟钉成硬目标（基础兵 80 血 / 碰撞 6 / 子弹 4 / 跳跃者 200 血 / 跳砸 8）✓
        /// 而 `WaveGrowth` 是一条**单调递增**的抛物线 ⇒ 压前期必然连带压 20 分钟：
        ///   · 压 base   ⇒ 20 分目标直接被打破（除非同步加大 a/b，那又是另一套系数）
        ///   · 压 a/b    ⇒ 整条曲线一起降，同上
        ///   · 改 t 的幂 ⇒ 影响所有敌人，而且 20 分目标一起被改掉
        /// ⇒ 唯一能"只压前段"的做法是给曲线**再乘一个只在前几分钟小于 1 的因子** ✓
        ///
        /// ── 形状：`1 − earlyNerf × (1 − t/earlyNerfEnd)²`，t 到窗口后**恒等于 1** ──
        /// ★ 为什么不用指数衰减 `(1 − early·e^(−t/k))` ★
        ///   指数项**永远到不了 0** ⇒ 第 20 分钟还留着残差
        ///   （early=0.35、k=6 ⇒ 0.35 × 4.2% = 1.5% ⇒ 基础兵 80 变成 78.8）✗ **20 分目标会被打破** ✗
        ///   而 `(1 − t/T0)²` 在 `t ≥ T0` 时**精确为 0** ⇒ 20 分钟与改前逐位相同 ✓
        ///   且它在 t=T0 处导数为 0（C¹ 连续）⇒ 窗口边界没有"折角" ✓
        ///
        /// ── 当前取值：压低 35%、窗口 8 分钟（t = 分钟−1 ≤ 7 ⇒ 到第 8 分钟仍有极小残差）──
        ///   第 1 分钟 基础兵血 2.8 → **1.82**（仍 ≤ 每击 4 ⇒「开局一刀死」继续成立 ✓）
        ///   第 2 分钟 4.88 → 3.65、第 3 分钟 7.38 → 5.93、第 5 分钟 12.80 → 11.68
        ///   第 10 分钟起一字节不变（29.99…），第 20 分钟仍是 **80.00** ✓
        ///
        /// ── 为什么窗口选 8 分钟 ──
        /// BOSS 由 `BossSchedule` 在**第 10 分钟**才第一次出场 ⇒ 窗口（t ≤ 7）覆盖不到它 ✓
        /// 而且 BOSS 在 `EnemyDef.EarlyNerfFactor` 里**显式排除**（见那里的注释）：用户原话
        /// "BOSS 暂时不调整" ⇒ 它的数值在任何波次都必须逐位不动，不能靠"窗口恰好没盖到"这种巧合 ✓
        ///
        /// ★ 这两个键**真的被读** ★ 唯一消费点是 `EnemyDef.EarlyNerfFactor(wave)`，
        ///   `HpForWave` 与 `DamageForWave` 都走它（不存在第二份实现）；
        ///   自检有五条断言钉住"第 1 分钟 = 1 − earlyNerf""窗口外恒为 1""20 分仍是 80.00"
        ///   "BOSS 不吃压低""压低只影响前期" ✓
        /// `earlyNerf ≤ 0` 或 `earlyNerfEnd ≤ 0` ⇒ 关闭（因子恒为 1）⇒ 老数据行为逐位不变 ✓
        /// </summary>
        public float earlyNerf, earlyNerfEnd;

        /// <summary>
        /// ★★ v194：**20 分钟后的额外加速斜率**（用户原话："检查20分钟后敌人数值是否还在成长，
        /// 20分钟后的成长规则修改，额外提高敌人成长倍率"）★★
        ///
        /// ── 先回答"20 分钟后还在不在成长"──
        /// **在长**。五条曲线的 `WaveGrowth` 是 `1 + a·t + b·t²`，而 `b > 0` ⇒ 单调递增、
        /// 且**每波增幅本身在变大**。实测倍率（`t = 分钟 − 1`）：
        ///     标准 HP  第 20 / 25 / 30 分钟 = **28.571 / 40.292 / 53.873**
        ///     标准伤害 第 20 / 25 / 30 分钟 = **8.571 / 11.790 / 15.520**
        ///     精英 HP  11.905 / 16.540 / 21.912 ｜BOSS HP 11.000 / 15.251 / 20.177
        /// 所以"不成长"这个怀疑**不成立** ✓ 成立的另一件事是：**二次曲线的尾巴不够陡** ——
        /// 20→30 分钟只涨 1.89 倍，而这十分钟里玩家靠升级 + 道具的成长通常不止 1.9 倍
        /// ⇒ 后期会**反向变简单** ✗ 这才是要修的东西 ✓
        ///
        /// ── 加的是什么 ──
        ///   `post20(t) = 1 + post20Slope × max(0, t − 19)`，`t = max(0, 分钟 − 1)`
        /// 一个**乘在整条曲线上**的因子（见 `EnemyDef.Post20Factor`，唯一实现）。
        ///   · 第 20 分钟及以前 ⇒ `max(0, 0) = 0` ⇒ **精确 1.0**（不带残差）
        ///     ⇒ 既有的 20 分钟硬目标（基础兵 800 / 触碰 60 / 子弹 40 / 跳砸 80 /
        ///        跳跃者 2000 / BOSS 33000）**一位都不变** ✓
        ///   · 第 21 分钟起：每分钟多 +`post20Slope`（取 0.15 ⇒ 第 25 分钟 ×1.75、第 30 分钟 ×2.50）
        ///
        /// ★ 为什么斜率是 0.15 ★ 它把 20→30 分钟的 1.89 倍抬成 **4.71 倍**（×2.50），
        ///   恰好压过玩家同期约 2~3 倍的强度增长 ⇒ 曲线由"后期变简单"变回"后期压力持续" ✓
        ///
        /// ★ 为什么**不再动** `(a, b)` ★ 那是**倍率形状**，×10 已经落在 `enemies.json` 的 base 里
        ///   （v194），再乘一遍就是双算 ⇒ 曲线系数本轮一个字节都没改 ✓
        ///
        /// ★ 它与 `earlyNerf` 不冲突 ★ 后者的窗口只到 t = 8（精确 1.0 之外全是 1），
        ///   而本项只在 t > 19 才离开 1.0 ⇒ 两者值域不相交 ✓
        ///
        /// `post20Slope ≤ 0` ⇒ 关闭（因子恒为 1）⇒ 回到 v193 行为，一个数回退 ✓
        /// </summary>
        public float post20Slope;
    }

    [Serializable] public class EnemyTableRaw { public EnemyDefRaw[] enemies = Array.Empty<EnemyDefRaw>(); }

    [Serializable] public class WaveBatchDefRaw { public string enemyId; public int count; public float startAt, interval; }
    [Serializable] public class WaveDefRaw
    {
        public int index;
        // v165：`duration` 已删除 ✗
        // 它是"每波 30~90 秒"这套旧节奏的唯一载体，与"第 N 分钟 = 第 N 个成长刻度"是两套真相。
        // 现在一回合恒为 60 秒，由 `Clock.SecondsPerMinute` 单点定义（见 WaveDirector.IsComplete）。
        // ★ JsonUtility 会静默忽略 JSON 里多余的字段 ★ 所以 waves.json 里的 duration 也一并删了，
        //   否则下一个人会以为"改这个数能调节奏"——而它完全不起作用。
        public bool isBoss;
        public string tip;
        public WaveBatchDefRaw[] batches = Array.Empty<WaveBatchDefRaw>();
    }
    [Serializable] public class WaveTableRaw { public WaveDefRaw[] waves = Array.Empty<WaveDefRaw>(); }

    /// <summary>
    /// **道具的一条数值效果**（增益或减益）。
    ///
    /// ★ v168：道具表从"单条 stat/add/mode"扩展成"效果列表"★
    ///
    /// 为什么必须扩展：新标准把道具分成三类 ——
    ///   ① 纯数值增加     —— 一条**或多条**增益（传奇的"全属性小幅度增加"就是多条）
    ///   ② 有牺牲的增加   —— 增益 **+ 减益**，两半都要在表里写清楚
    ///   ③ 机制拓展       —— 一条局部规则（`rule` / `ruleValue`）
    /// 旧表的 `stat + add + mode` 只能表达"一条增益"，装不下 ①②③ 里的任何一条新语义 ✗
    /// 而"把减益偷偷写成 add 的负数"会让"增益必须大于减益"这条设计约束**不可校验** ✗
    /// ⇒ 所以拆成两个显式列表：`effects`（增益）与 `costs`（减益）✓
    /// </summary>
    [Serializable]
    public class ItemBonusRaw
    {
        /// <summary>Add = 整数加法；Percent = 整数百分比（乘法）。</summary>
        public string mode = "Add";
        /// <summary>属性名（`StatId` 的名字，如 MeleeDamage / MaxHp）。</summary>
        public string stat;
        /// <summary>整数数值（约定：绝不出现 0.5 这种读不出来的数）。</summary>
        public float add;
    }

    /// <summary>
    /// **道具表的一行**（`Assets/Resources/Data/items.json`）。
    ///
    /// ★ 与旧格式的关系 ★
    /// 旧格式是 `{ "stat": "MeleeDamage", "mode": "Add", "add": 1 }` —— **已废弃** ✗
    /// 新格式把同一个意思写进 `effects: [ { mode, stat, add } ]` ✓
    ///
    /// ★ 为什么不做"两种格式都能读" ★
    /// 双格式 = 两份真相。只改一处时另一处会**静默失效**（JsonUtility 对多余键是忽略，不报错）
    /// —— 这正是本工程反复吃亏的那类 bug ✓ 所以只有一种格式，且 `TableDatabase.Validate`
    /// 在道具既没有 effects、也没有 rule 时**直接报错**，而不是让它变成一件"买了没反应"的道具 ✗
    /// </summary>
    [Serializable]
    public class ItemDefRaw
    {
        public string id, name, desc, grade;
        /// <summary>★ 1 = 纯数值增加｜2 = 有牺牲的增加｜3 = 机制拓展 ★（自检按它分类统计）</summary>
        public int category;
        public int price, maxStack;
        /// <summary>增益列表（类别 1 / 2 用；可以为空 —— 纯机制道具没有数值）。</summary>
        public ItemBonusRaw[] effects = Array.Empty<ItemBonusRaw>();
        /// <summary>减益列表（只有类别 2 有；**必须严格小于增益**，自检会验）。</summary>
        public ItemBonusRaw[] costs = Array.Empty<ItemBonusRaw>();
        /// <summary>
        /// **机制键**（类别 3 必填；类别 2 可用它承载"小机制"）。
        /// 空字符串 = 这件道具没有机制。
        /// ★ 能被识别的键**只有 `ItemRules` 里登记的那几条** ★ ——
        /// 表里写一个没人解释的键 = 一件"看着有机制、实际死的"道具 ✗
        /// 所以 `TableDatabase.Validate` 会拒绝未登记的键 ✓
        /// </summary>
        public string rule = "";
        /// <summary>机制的主参数（含义随 rule 而定，见 `ItemRules`）。</summary>
        public float ruleValue;
        /// <summary>
        /// **成长**：每过 1 个回合，本条道具的全部增益额外增加这么多（Add 类是点数，Percent 类是百分点）。
        /// 0 = 不成长 ✓
        /// </summary>
        public float growPerWave;

        /// <summary>
        /// ★★ v314：**这件道具不进任何随机池**（`true` = 只能由机制产出，商店 / 掉落 / 刷新都抽不到）★★
        ///
        /// ── 为什么是一个表字段，而不是在 `RunSession` 里写一个 id 白名单 ──
        ///   白名单 = "哪些 id 不许掉"这件事的**第二份真相**：加第二件这类道具时要在两处改，
        ///   而漏改的那一处**完全静默**（那件道具就该掉出来了）✗
        ///   写进表 ⇒ "它能不能掉"是这一行的属性，加行的人一定会看见这个键 ✓
        ///
        /// ── 消费点只有一个 ★★
        ///   `TableDatabase.RollableItems`（= 表里 `noDrop == false` 的那些）。
        ///   商店掷档（`RunSession.PickItemOfGrade`，解锁门生效与否两条路）与
        ///   `TableDatabase.RandomItem*` **全部**只读那个数组 ⇒ 没有第三条抽取路能绕开它 ✓
        ///
        /// ⚠️ 缺省 `false`（`JsonUtility` 对缺键就是 false）⇒ **老行一个字节不用改**，
        ///    95% 的道具行为逐位不变 ✓
        /// </summary>
        public bool noDrop;
    }

    /// <summary>
    /// ★ v314：**道具 id 的字面量** —— 只在"代码必须认识某一件具体道具"的地方引用。
    ///
    /// 为什么开这一格：`angel_descent` / `fallen_angel_feather` 不只是表里的两行，
    /// 它们**参与判定**（死亡拦截、合计上限、复活后的替换）。判定里写字符串字面量
    /// 一旦拼错，症状是"这件道具静默失效"，而编译器不会说话 ✗
    /// ⇒ 字面量只写在这里，判定侧引常量 ✓
    /// ⚠️ 表里的 `id` 仍然以 `items.json` 为准：这两个值必须与表行逐字相同（自检/探针会核）。
    /// </summary>
    public static class ItemIds
    {
        /// <summary>「天使降临」—— 持有者死亡时消耗 1 件、复活一次，然后变成下面的羽毛。</summary>
        public const string AngelDescent = "angel_descent";
        /// <summary>「陨落天使的羽毛」—— 复活产出的残渣（最大生命 −3），**不进任何随机池**。</summary>
        public const string FallenAngelFeather = "fallen_angel_feather";
    }
    [Serializable] public class ItemTableRaw { public ItemDefRaw[] items = Array.Empty<ItemDefRaw>(); }

    /// <summary>
    /// **插件商品**的原始表行（`Assets/Resources/Data/plugins.json`）。
    ///
    /// ★ 为什么插件走 JSON，而不是 `.asset` SO 资产 ★
    /// 规格要求"优先用 ScriptableObject 保存静态配置"，而插件**本来**就是按 SO 设计的
    /// （见 `Goods.cs` 的 `PluginAsset`）✗
    /// 但在本工程里，手写的 `.asset` 引用不到 `Goods.cs` 里的类型：
    /// `m_Script` 要写 MonoScript 的 fileID，而 `11500000` 只对应"**与 .cs 文件同名的那个类**"——
    /// `Goods.cs` 里有 `SeedAsset / PluginAsset / ItemAsset` 三个类型，**没有一个叫 `Goods`** ✗
    /// ⇒ 手写资产会静默变成"MonoScript 缺失"，`Resources.LoadAll<PluginAsset>` 恒为 0 ✓
    ///
    /// 所以这里改用**与 seeds/items/enemies/waves/tuning 同一套路**的第 6 张表 ✓
    /// 读出来之后仍然 `CreateInstance<PluginAsset>()` 成运行时实例 ⇒
    /// **`PluginAsset` 类型一个不改**，`ShopOffer` / `PlayerGoods` / `WeaponSystem` 全部不动 ✓
    /// 等哪天真要在编辑器里手工摆 SO 资产，只需把 `TableDatabase` 那一处换成
    /// `Resources.LoadAll<PluginAsset>("Plugins")` 即可 ✓
    /// </summary>
    [Serializable]
    public class PluginDefRaw
    {
        public string id;
        public string displayName;
        public string description;
        /// <summary>D / C / B / A / S</summary>
        public string quality;
        /// <summary>适用的武器：0 锄头 / 1 水壶 / 2 肥料。**空数组 = 任意武器** ✓</summary>
        public int[] allowedWeapons = Array.Empty<int>();
        /// <summary>SwordWave / BurnOnHit / DoubleStrike / Lifesteal / MultiJet / SlowOnHit / ThickJet / AutoCharge / SubBurst / PoisonZone / FreeBurst / ChainBurst</summary>
        public string effect;
        public float amount;
        /// <summary>第二效果参数：灼烧/减速的"每层每秒比例"、子爆的半径比、连环的落点距离。</summary>
        public float amount2;
        /// <summary>Stackable / Unique / Multiplicative</summary>
        public string stackRule;
        public int maxStack;
        public bool exclusivePerWeapon;
        public int basePrice;
    }
    [Serializable] public class PluginTableRaw { public PluginDefRaw[] plugins = Array.Empty<PluginDefRaw>(); }

    [Serializable] public class PlayerBaseDef
    {
        // v23：唯一攻击力 = atkDamage（整数）；新增爆炸伤害/爆炸范围；删除 regen/leech/三系攻击力/异常速度
        // v45：伤害由 (基准伤害 + atkDamage × 继承率) × atkMult 得到——atkMult 初始 1.0（100%）
        // v162（A1/A3）：**删除通用 atkDamage**，拆成三种独立的伤害类型（均为整数值语义，见 StatId 的说明）。
        //                公式同步简化为 (武器基础伤害 + 对应伤害属性) × 伤害倍数——继承率已被删除。
        public float maxHp, shield, meleeDamage, rangedDamage, blastDamage, atkMult, explodeRange, critRate, critDmg,
                     atkSpeed, atkRange, moveSpeed, dodge, knockback, expMult, goldMult;
        public int maxPlots;
        /// <summary>v48：开局发放的金币（第一回合开始前就能进商店买东西）。</summary>
        public int startGold;
        /// <summary>受击后的无敌时间（秒）。</summary>
        /// <summary>
        /// ★ v191：**幸运值基底**（0..1 小数，0.05 = 5%）★
        ///
        /// 它是"植物被收割时，概率让这一格的金币与经验 ×2"的触发概率（用户需求①）。
        /// ★ 它必须 > 0 ★ —— 若写成 0，任何 `Percent` 卡都乘不动（`0 × (1+p)` 恒 0），
        /// 而面板又恰好显示 `+0%`，于是"卡到底生效了吗"永远看不出来
        /// （详见 `TableDatabase.IsFractionStat` 的长注释）。
        ///
        /// 面板口径：`ImGuiHud.AddPoints(当前, 基底)` = **百分点差** ⇒ 开局天然 `+0%` ✓
        /// （这正是用户要的"属性界面显示增量 0%"，不需要任何特判）
        ///
        /// ⚠️ 旧注释写的"影响道具刷新"是 v23 时代的语义 ✗ ——
        ///   那套（`ShopDef.RollShifted` + `shop.luckRefreshBonus`）今天**全工程 0 个调用者**，
        ///   所以它不是这条属性的消费者；今天唯一的消费者是 `World.HarvestPlot` 的幸运判定 ✓
        /// </summary>
        public float luck;
        /// <summary>
        /// ★ v191：**小猪刷新率的初始等级**（0~3，含义见 `TuningDef.PigRateOf`）★
        /// 与 `heartDropRate` 同构：这里存的是**等级整数**，不是概率 ✓
        /// 0 级 = 低 = 2%（`TuningDef.PigRateByLevel[0]`）
        /// </summary>
        public float pigRate;
        /// <summary>爱心掉落率与每个爱心的回血量。</summary>
        public float heartDropRate, heartHeal;
        public float invulnTime;
        /// <summary>未受伤时每秒恢复的最大护盾比例。</summary>
        public float shieldRegenPct;
        /// <summary>护盾每秒恢复量的下限。</summary>
        public float shieldRegenMin;
        /// <summary>受击后延迟多久才开始回护盾（0 = 字面语义：不受伤就回）。</summary>
        public float shieldRegenDelay;

        /// <summary>
        /// ★★ v192：**玩家血量与敌人伤害共用的整数单位**（默认 10）★★
        ///
        /// ── 为什么需要它（用户需求②："敌人伤害处理，全部转换为整数"）──
        /// 改前敌人第 1 分钟的实际伤害全是小数：基础兵触碰 **0.455**、远程弹 **0.303**、
        /// 精英砸地 **0.607**（都是 `曲线值 × EarlyNerfFactor(1) = 0.65`）。
        /// 若直接把送进血池的数 `Round` 一下：`Round(0.455) = 0` ⇒ **敌人永远打不动玩家** ✗
        /// ⇒ 只能先换一个更细的"血点"刻度：**1 点旧血量 = `hpUnit` 点新血点** ✓
        /// 于是 `maxHp 10 → 100`、敌人伤害 `0.455 → 5`（`Round(0.455 × 10)`）——
        /// 两边同在**整数域**，比例与改前逐项相同 ✓
        ///
        /// ── 谁是消费者（唯一实现，别在别处再乘一次）──
        ///   · `EnemyDef.PlayerHpUnit` / `EnemyDef.PlayerDamagePoints` —— 曲线值 → 整数血点
        ///   · `DamageResolver.Resolve` —— 目标**是玩家**时把 `RawValue` 换成整数血点
        ///   · `RunSession` 的吸血 / `Combat` 的毒系回血 —— "按敌人伤害比例回血"两条，
        ///     伤害在敌人 HP 域、血在玩家域 ⇒ 必须乘同一个单位才不会退化成 1/10 ✓
        ///
        /// ★ 为什么 `≤ 0` 要回落 10 ★ 老存档/老配置没有这个键时 JsonUtility 给 0，
        /// 若直接乘 0 ⇒ 敌人伤害全变 0（打不动）✗ 回落值与表里写的默认值一致 ✓
        /// </summary>
        public float hpUnit = 10f;

        /// <summary>
        /// ★★ v192：**整点回血比例**（相对 `maxHp`，1.0 = 回满）★★ —— 用户需求⑦：        /// "每分钟整点会触发一次恢复玩家健康状态，疑似回合的机制遗留成为 bug，利用这个 bug 改为机制"。
        ///
        /// 改前它是 `ResetPlayerForNewWave` 里一句写死的 `Heal(MaxHp)`（+ `RefillShield`）——
        /// 那是**回合制**的遗物（一个回合开始时补满血量），而在计时制下 `StartWave` 每 60 秒
        /// 必然跑一次 ⇒ 它就退化成"每分钟整点回满血" ✓ 玩家要把它**转正**：
        /// 行为保留、数值进表、并发布 `MinuteHealEvent` 让表现层播全屏回血特效 ✓
        ///
        /// `1.0` = 与改前**逐位等价**（`Heal(maxHp)`）；`0` = 不回血（护盾仍补满）；
        /// `0.25` = 每分钟回四分之一。消费者唯一：`RunSession.ResetPlayerForNewWave` ✓
        /// </summary>
        public float minuteHealPercent = 1f;
    }

    [Serializable] public class GrowthDef
    {
        public float hpPerLevel, shieldPerLevel, expBase, expLinear, expExponent;
        public int levelsPerPlot;
    }

    [Serializable] public class CapsDef
    {
        public float dodge, critRate, atkSpeed, atkRange, moveSpeed, knockback;
        /// <summary>v195：`explodeDamage`（爆炸倍率的上限）随那条属性一起删除 ✗ —— 只剩范围倍率的上限。</summary>
        public float explodeRange;
        /// <summary>v162：三种伤害类型（整数值语义）各自的上限。原本只有一条通用的 atkDamage 上限。</summary>
        public float meleeDamage, rangedDamage, blastDamage;
        public int maxPlots;
    }

    [Serializable] public class FarmDef
    {
        public float arenaHalf, tileSize, digTime, sowTime, waterTime, fertilizeTime,
                     waterFactor, fertilizeFactor, growthFloor, interactRange, growingHpFactor, wallThickness;
        /// <summary>v166：**列数**（田地宽度）。初始田 = gridSize × initialRows。</summary>
        public int gridSize;
        /// <summary>v166：开局解锁的**行数**（默认 3 ⇒ 6×3 = 18 格）。</summary>
        public int initialRows;
        /// <summary>v166：行数上限（默认 6 ⇒ 满格 6×6 = 36 格）。</summary>
        public int maxRows;
        /// <summary>v166：**每存活多少秒加一排**（默认 300 = 5 分钟）。≤0 关闭成长。</summary>
        public float rowGrowthSeconds;
        /// <summary>v10：树的基础成熟时间（攻速会同步缩短）。</summary>
        /// <summary>v14：施肥小游戏需要连续按下的次数。</summary>
        /// <summary>v24：土地蓄水默认上限与每次水壶命中恢复量（整数）。</summary>
        public int waterMaxDefault, waterPerHit;
        public int fertilizeHits;
        public float treeGrowTime;
        /// <summary>树成熟后每隔多久再产出一次。</summary>
        public float treeProductInterval;
        /// <summary>
        /// ⚠️ v166：`initialPlots`（"开局几块耕地"）**已删除** ✗
        ///
        /// 它是旧的"耕地块数"模型的产物：2 块、按等级 +1 块、上限 25 ——
        /// 而新玩法的田地是**固定 6 列 × N 行**，玩家不选位置，成长靠**存活时间**而不是等级 ✓
        /// 换成 <see cref="initialRows"/> / <see cref="maxRows"/> / <see cref="rowGrowthSeconds"/> 三个字段。
        ///
        /// 为什么是"换字段"而不是"把 initialPlots 重新解释成行数"：
        /// 名字会撒谎（叫 Plots 却是 Rows），而且 `TableDatabase` 里有一条
        /// `if (initialPlots &lt;= 0) initialPlots = 6;` 的兜底 —— 一旦语义变成"行数"，
        /// 这条兜底就会在配置缺失时**静默地把 6 行全解锁**（等于开局满级田地）✗
        /// 换个名字，这个陷阱就不存在了 ✓
        /// </summary>
        /// <summary>耕作时是否锁住玩家移动（让"耗时 n 秒"成为真实代价）。</summary>
        public bool rootWhileActing;
        /// <summary>
        /// v165：**`resetOnWaveEnd` 已删除** ✗
        ///
        /// 它开着（tuning.json = true）时，每回合结束会把**全部植物清空、地块刷回未开垦** ——
        /// 而"植物是玩家的产出形式"，每 60 秒抹掉一次产出直接违背新玩法 ✓
        ///
        /// 之所以删掉字段而不是把值改成 false：留着它 = 留一个随时能把产出清空的开关，
        /// 而"要不要清空农场"在计时制下**已经不是可调参数**，是错的设计 ✗
        /// </summary>
        public bool freePlotOnGrown;

        /// <summary>
        /// ★★ v192：**爆炸收割的收益系数**（相对普通收割；1 = 全额）★★ 用户需求③：
        /// "爆炸对耕地的收割处理，让经验值和金币获取量为原来的 1/3"。
        ///
        /// 消费点唯一：`World.MatureAndHarvestInRadius`（肥料爆炸唯一的"催熟 + 收割半径内成熟格"入口）
        /// ★★ v200：**它现在是 `World.HarvestPlot(plot, yieldScale)` 的入参**，不再是世界字段 ★★
        /// 改前由 `MatureAndHarvestInRadius` 写进 `World.HarvestYieldScale`（并把"只有爆炸吃到 1/4"
        /// 寄托在 `finally` 一定恢复上）；那个字段已删除，比例只沿**这一次调用**传递 ⇒
        /// 手挥锄头 / 扫掠 / 剑气 / 水枪 / 连锁收获**在类型层面就拿不到它** ✓
        /// 再由 `World.HarvestPlot` 乘在**金币/经验基数**上 ✓
        /// 乘在基数上的三个好处：① 幸运 ×2 与等级倍率、道具 `harvest_gold_pct` 自动跟着缩
        /// ② 弹字印的是**到账值** ⇒ 弹字自动变（无需第二处改动）③ 单参重载恒传 1 ⇒ 普通收割一分不少 ✓
        ///
        /// `≤ 0` 或 `> 1` ⇒ 回落 1/4（表里没写这个键时 JsonUtility 给 0，若直接乘 0 会把爆炸收割变成 0 收益 ✗）
        ///
        /// ★★ v195（用户原话："修改爆炸收割效果，改为获取 1/4 金币和经验"）⇒ 1/3 改 **1/4** ★★
        /// 回落值同步改成 1/4：兜底值与表里写的值必须是同一个数，否则"表里漏键"会静默改平衡 ✓
        /// </summary>
        public float blastHarvestRatio = 1f / 4f;

        /// <summary>
        /// ★★ v195（用户原话："植物金币收获量调整" ⇒ 满配农场每分钟 ≈ 一个传奇的价 ≈ 2000 金）★★
        ///
        /// **收割金币的全局系数**（乘在每格金币基数上；经验不吃这一项）。
        ///
        /// ── 为什么需要它，而不是直接改 `seeds.json` 的 `goldYield` ──
        /// ① 本轮的可改文件清单里**没有 `seeds.json`**（用户在授权名单里只给了 tuning/enemies/items/plugins）
        /// ② 而且"满配收入"是**一条乘法**关系：36 格 × 等级 3(×1.70) × 最高品质 ÷ 成熟时间 ——
        ///    要把 3697/分 压到 2000/分，逐株改 16 个数与乘一个 0.54 在数值上等价，
        ///    但后者**可回退、可解释、只改一处** ✓
        /// ③ 它乘在**基数**上（与 `blastHarvestRatio` 同一个位置）⇒ 幸运 ×2 / 等级倍率 / 道具 /
        ///    `GoldMult` 全部自动跟着变，弹字印的仍是到账值 ✓
        ///
        /// 实算（见探针 `ProbeV195Num`）：36 格满级毒蕈(149 金 / 148 秒) ⇒ 61.6 金/秒 = **3697/分**；
        /// `×0.54` ⇒ **1996/分** ≈ 30 分钟一个传奇的价（`S 80 × 5.0 档位 × 5.0 波次 = 2000`）✓
        /// `≤ 0` ⇒ 回落 1（与"没有这个键"的行为一致，不允许把收割变成 0 收益 ✗）
        /// </summary>
        public float harvestGoldScale = 1f;
    }

    [Serializable] public class FormulaDef
    {
        public float plantHpBaseline, plantShieldBaseline, plantMoveBaseline, healInheritMult, guardInheritMult,
                     ailmentDelay, tickInterval, poisonTickFactor, poisonDuration, poisonLeechRatio,
                     burnTickFactor, burnDuration, burnSpreadRadius, burnSpreadTargets, burnSpreadRatio,
                     freezeSlow, freezeThresholdRatio, freezeThresholdMin, freezeDuration, freezeBreakFactor, minDamage;
        /// <summary>植物帮手弹道速度（米/秒）。</summary>
        public float plantProjectileSpeed;

        /// <summary>
        /// v120：**远程攻击的角度散布**（度，需求："对所有植物和玩家水壶的远程攻击制作细微偏差"）。
        ///
        /// 每次远程攻击在瞄准方向上叠加一个 `±aimJitterDeg` 的随机角度。
        ///
        /// 为什么需要它：远程攻击若**永远精确命中**，玩家看到的是"光束每次都从同一个像素点射出"，
        /// 手感偏机械；而且多株植物齐射时会叠成一条完美的直线，看起来像贴图错误。
        /// 一点散布让齐射有"手忙脚乱"的生命感。
        ///
        /// 幅度必须**小**：它的作用是"看起来自然"，不是"打不准"。
        /// 1.5° 在 10 米外约等于 26 厘米的偏移 —— 视觉可辨，但不会导致空枪。
        /// 设 0 可完全关闭（回归到精确瞄准）。
        /// </summary>
        public float aimJitterDeg;
        /// <summary>状态类（异常）子弹的命中直接伤害比例（相对该次异常值）。</summary>
        public float ailmentHitRatio;
        /// <summary>异常结算次数与间隔（毒/火各 4 次、每 1 秒一次）。</summary>
        public int ailmentTickCount;
        /// <summary>毒扩散：健康敌人贴身累计多少秒后被感染、扩散比例。</summary>
        public float poisonContactSeconds, poisonSpreadRatio;
        /// <summary>火：灼烧中的敌人被非异常命中时的爆炸半径与伤害比例。</summary>
        public float burnExplodeRadius, burnExplodeFactor;
        /// <summary>冰：每次非异常命中增加的寒冷比例、每层减速。</summary>
        public float chillPerHit, chillSlowPerStack;
        /// <summary>植物判定范围 = 攻击范围 × 该系数（范围内无敌人时向玩家靠拢）。</summary>
        public float plantDetectFactor;
        /// <summary>毒的独立生效延迟（秒）；≤0 时用 ailmentDelay。</summary>
        public float ailmentDelayPoison;
        /// <summary>毒接触传染：判定半径、传染比例、同一敌人的传染冷却。</summary>
        public float poisonContactRadius, poisonContactRatio, poisonContactCooldown;

        /// <summary>
        /// ★★ v192：**子爆伤害 / 主爆伤害** 的比例 —— 用户需求⑤："伤害基础爆炸伤害的 50%" ★★
        ///
        /// ── 为什么它是**独立的一行**，而不复用半径比 `SubBurstRadiusRatio` ──
        /// v189 时两者共用一个数（`SubBurstDamage = 主爆 × SubBurstRadiusRatio`），
        /// 因为那轮的规格只给了半径。本轮用户给了**两个数**（范围 1/2、伤害 50%），
        /// 而半径来自 `plugins.json` 的 `fert_subburst.amount2`、伤害来自这里 ⇒
        /// 以后单独调半径不会再**顺手把伤害也改了**（改前正是这个耦合）✓
        /// 消费者唯一：`WeaponSystem.SubBurstDamage` ✓
        /// `≤ 0` ⇒ 回落 0.5（表里没这个键时 JsonUtility 给 0，直接乘会把子爆伤害变成 0 ✗）
        /// </summary>
        public float subBurstDamageRatio = 0.5f;
    }

    /// <summary>
    /// ★★ v279：**三种持续伤害的百分比**（用户原话："燃烧、减速、中毒伤害都修改为
    /// 继承自对应武器的伤害百分比，而不是继承属性"）★★
    ///
    /// ── 它替代了什么 ──
    /// 改前：灼烧 / 减速的"每秒"是 `Stats.Get(MeleeDamage / RangedDamage) × 0.50`
    /// （**属性**，`RunSession.PluginPostHit`），毒区是"这一炮的伤害 × 0.50"（**伤害**）
    /// ⇒ 三种口径不一致；属性为 0 时 `ApplyBurn(…, 0f, …)` 连行都不建 ⇒ DoT 静默消失 ✗
    /// 改后：三者统一成"**造成它的那把武器、这一次的真实伤害** × 本段的百分比" ✓
    ///
    /// ── 为什么比例要放在 `tuning.json`，而不是留在 `plugins.json` 的 `amount` ──
    /// 那是**两处**会漂移的数：调插件售价表顺手改了 `amount` 就会静默改伤害 ✗
    /// 本段是唯一出处（`WeaponSystem.BurnPerLayerPerSecond` 等三个纯算式只读它）；
    /// `plugins.json` 里那三件的 `amount / amount2` 退化成"插件自述"（自检 `PlugField` 的活体判定仍读）✓
    ///
    /// ── `tickMaxHpFraction` 是什么 ──
    /// **单次**持续伤害结算的上限 = 目标 `MaxHp × 本值`（默认 0.25 = 25%），防"一次 DoT 直接秒杀"。
    /// 执法点在 `PluginDotTable.Hit`：**逐目标**夹紧 ⇒ 毒区对后走进来的敌人同样受保护
    /// （不是在施加时按施法者血量算一次 —— 那样对血少的敌人形同没有上限）✓
    ///
    /// ── 兜底 ──
    /// 字段初始值就是 0.5 / 0.25，而 `JsonUtility` 只覆盖 JSON 里出现过的键
    /// ⇒ 老表（没有 `dots` 段）跑起来与改前**逐位相同** ✓
    /// </summary>
    [Serializable]
    public class DotsDef
    {
        /// <summary>灼烧：**锄头这一刀的实际伤害** × 本值 = 每层每秒。</summary>
        public float burnPercent = 0.5f;
        /// <summary>减速：**水壶这一束的实际伤害** × 本值 = 每层每秒。</summary>
        public float slowPercent = 0.5f;
        /// <summary>毒区：**肥料这一炮的实际伤害** × 本值 = 每层每秒。</summary>
        public float poisonPercent = 0.5f;
        /// <summary>单次 DoT 结算上限 = 目标 `MaxHp` × 本值（0.25 = 25%）。</summary>
        public float tickMaxHpFraction = 0.25f;
    }

    /// <summary>
    /// ★★ v282：**吸血的两条上限**（用户反馈：「吸血类插件太强，在面对大量敌人时回血过多」）★★
    ///
    /// ── 为什么需要这一段（结构性病因）──
    /// 改前 `RunSession.PluginPostHit` 被**逐敌**调用：一刀打中 3 个敌人就结算 3 次，
    /// 剑气对每个敌人各结算一次，多柱对每道各结算一次 ⇒ 群怪时回血量随敌人数**线性增长** ✗
    /// 单纯调小 `plugins.json` 的 `hoe_lifesteal.amount` 压不住这条线性项（单挑会先被废掉）。
    ///
    /// ── 两条上限各自防什么 ──
    /// · `perSwingMaxHpFraction` —— 防「一刀十怪瞬间回满」：**一次攻击事件**的总回血上限。
    /// · `perSecondMaxHpFraction` —— 防「AOE 持续回血」：任意 1 秒桶内的总回血上限。
    ///
    /// 两者都写成**占玩家当前 `MaxHp` 的比例**（不是绝对点数）：血量成长后自动等比放大，
    /// 不会因为后期血池变大而让一条老上限变成摆设 ✓
    ///
    /// `≤ 0` ⇒ 该条上限**关闭**（与改前逐位相同；老表漏写这两个键时字段初始值就是下面的设计值，
    /// 但显式写 0 的人是在说"我要关掉它"，所以代码里读的是"填了就按填的、≤0 就不夹"）。
    /// </summary>
    public class LifestealDef
    {
        /// <summary>一次攻击事件的吸血上限 = 玩家 `MaxHp` × 本值（0.05 = 5%）。</summary>
        public float perSwingMaxHpFraction = 0.05f;
        /// <summary>1 秒桶内的吸血上限 = 玩家 `MaxHp` × 本值（0.03 = 3%）。</summary>
        public float perSecondMaxHpFraction = 0.03f;
    }

    [Serializable] public class ToolDef
    {
        public string damageType, kind, ailment;
        // v162（A3）：删除 inheritMult（继承率）。
        // 它唯一的作用是喂给旧公式的 `攻击伤害 × 继承率` 那一项；该参数已随公式一起删除，
        // 于是这三个数（锄 0.35 / 壶 0.30 / 肥 0.30）成了**没有任何读取方的死数据**。
        // 留着它比删掉更危险：下一个人读 tuning.json 会以为"改这个比例能调伤害"——
        // 而它现在完全不起作用。这与本项目删除 WaterCap/WorkCap 是同一条纪律。
        public float baseValue, cooldown, range, arc, projectileSpeed, radius;
        /// <summary>v47：水壶激光的半宽（米）。判定与表现共用它，避免"看起来打到却没伤害"。</summary>
        public float beamWidth;
        public int hits;
        /// <summary>
        /// v111：**基础爆炸范围**（整数米，需求："每个武器有自己的基础爆炸范围基础值"）。
        /// 实际半径 = 本值 × 爆炸范围属性。≤0 时按 3 处理（见 RunSession.BlastBaseRadius），
        /// 于是老数据不改也能跑，但新数据可以逐件武器调。
        /// </summary>
        public int baseExplodeRange;
    }

    [Serializable] public class ToolsDef
    {
        /// <summary>切换武器后的前摇：这段时间内无法攻击。</summary>
        public float switchWindup;
        /// <summary>同时操控的耕地上限（1 + 升级档位，最大 4）。★ 档位不是品质：不参与伤害（v186）</summary>
        public int workCapMax;
        /// <summary>水壶/肥料每升一档额外的生长减免。★ 与伤害无关（v186 删掉武器品质后仍在）</summary>
        public float waterBonusPerTier, fertBonusPerTier;

        /// <summary>
        /// ★★ v192：**自充的结算节拍（秒）** —— 用户需求①："水壶充能插件，修改为每 0.5 秒恢复一次" ★★
        ///
        /// 改前 `TickAutoCharge` 是**逐帧累加、攒够 1 点就发** ⇒ 10 点/秒 的那个插件
        /// 实际上每秒触发 10 次"发 1 点"（`GameAudio` 也因为这段每秒走 5~10 次而把冷却压到 0.32s）。
        /// 现在改成**只在节拍边界一次性结算**：每 0.5 秒发 `AutoChargePerSecond × 0.5 = 5` 点 ✓
        ///
        /// ★ 总量**不变**（10 点/秒）★ 用户只要求改**节奏**；把单次量也翻倍等于顺手把插件加强一倍，
        /// 那是没人要的平衡改动 ✗ 判据：2 秒内应恰好回 4 次、共 20 点（见 `ProbeV192Num`）✓
        ///
        /// `≤ 0` ⇒ 按 0.05 秒兜底（零周期会让 `while` 死循环 ⇒ 必须有一个正下限）✓
        /// </summary>
        public float canAutoChargeInterval = 0.5f;

        public ToolDef hoe = new ToolDef();
        public ToolDef can = new ToolDef();
        public ToolDef fertilizer = new ToolDef();
    }

    /// <summary>
    /// ★★ v252：`weight` 从 **int 改成 float** ★★
    ///
    /// ── 为什么必须改（这一个是本轮最值钱的发现）──
    ///   表里 S 档的权重写的是 **0.5**（`tuning.json` 的 `gradeWeights` / `itemGradeWeights`）。
    ///   而 `JsonUtility` 把 "0.5" 读进 **int 字段**时得到的是 **0** ⇒ S 档权重恒为 0
    ///   ⇒ **任何商品的 S 档都永远抽不到**：
    ///     · 种子：`sunflower_crown`（晴天）/ `rainlily`（雨天）等 6 株 S 档植物**一局都不可能刷出来**
    ///       —— 这正是用户报的「设计的晴天、雨天效果种子异常不刷新」的**根因**（v252 先加的
    ///       "天气种子保底"只是兜底，真正的病在这里）✓
    ///     · 插件：6 件 S 档插件 40000 次刷新 0 次出现（无头自检 `DumpPluginTiers` 实测：
    ///       实抽各档 D/C/B/A/S = 1905/1132/597/167/**0**）✓
    ///   A 档的 3.5 同样被截成 3（比值小，肉眼看不出来）✓
    ///   改成 float 之后：S 0.5 / A 3.5 都按原值生效，权重表**不再丢精度** ✓
    /// </summary>
    [Serializable] public class GradeWeightDef { public string grade; public float weight; }

    /// <summary>
    /// ★★ v277：**高品质（S/A/B）概率随时间膨胀的唯一算式** ★★
    ///
    /// 用户原话：「再次全面降低高品质刷新率」＋「给出明确的数值膨胀公式 ——
    /// 传说 5 分钟前 &lt; 1%、10 分钟前 &lt; 5%、任何时刻最高 10%」。
    ///
    /// ── 为什么必须动代码（上一轮穷举已经证明的结论）──
    ///   v276 用的是「表权重 × 写死的波次倾斜系数」那条路（旧 `TierWeightsRaw`：
    ///   `mult = 1 + bias·0.05·(波−1) − (1−bias)·0.04·(波−1)`）。它是**乘性**的 ⇒
    ///   S 档从第 5 波到第 10 波最多只能涨 `m_S(10)/m_S(5) = 2.29` 倍，
    ///   而「5 分钟 1% → 10 分钟 5%」需要 5 倍 ⇒ 在三条约束（5min≤1%、30min≤10%、单调）
    ///   下穷举 520 万组权重得到的 **S(10 分钟) 数学上界 = 2.55%** ⇒ 用旧机制到不了 5% ✓
    ///   本轮改成**加性目标**：直接算出 S(t)，低档吃剩余 ⇒ 三条约束**构造上成立**，不用拟合 ✓
    ///
    /// ── 公式（唯一的实现就是下面三个方法）──
    ///   t  = 已进行分钟（= `WaveIndex − 1`，见 `ShopSystem.MinutesAtWave`）
    ///   u  = clamp01(min(t, kneeMinutes) / kneeMinutes)      ← 前 10 分钟用幂曲线，10 分钟后用线性
    ///   S  = clamp(s0 + (s10 − s0)·u^gamma + postKneePerMinute·max(0, t − kneeMinutes)
    ///              , 0, sMax)                                ← 传说
    ///   A  = kA · S                                          ← 史诗
    ///   B  = kB · S                                          ← 稀有
    ///   D+C = 剩余 (1 − S − A − B)，在 D:C 之间按表里原比例分
    ///
    /// ── 参数为什么是这几个数（见 `tuning.json` 的 `_note_v277_raritycurve`）──
    ///   · `gamma = 3.5`：`s0 = 0.6%`、`s10 = 4.6%` 下，「t ≤ 5 分钟 S &lt; 1%」要求
    ///     `f(0.5) ≤ (1%−0.6%)/(4.6%−0.6%) = 0.10`；`0.5^1.6 = 0.33`（S(5)=1.92% 破线 ✗）、
    ///     `0.5^3.5 = 0.0884`（S(5)=0.9535% ✓）⇒ `gamma ≥ ln0.1/ln0.5 = 3.32`，取 3.5 留余量 ✓
    ///   · `postKneePerMinute = 0.27%/分钟`：让 S 恰在第 30 分钟到硬顶
    ///     （4.6% + 0.27%×20 = **10.0%**），此后恒 10% ✓
    ///   · `lowCap = 0.90`：高档合计的安全网（正常参数永不触发 —— S 封顶时合计恰 85%）✓
    ///
    /// ⚠️ 缺键/非法值的兜底在 `TableDatabase.Sanitize`（也在这里给了字段初值）：
    ///    表里漏写 `rarityCurve` 时，C# 初值 + Sanitize 会把它补成上面这套默认值，
    ///    不会出现「漏一个键 ⇒ 整个商店高档次全灭」✗
    /// </summary>
    [Serializable]
    public class RarityCurveDef
    {
        /// <summary>开局（t = 0）的传说概率。0.006 = 0.6%。</summary>
        public float s0 = 0.006f;
        /// <summary>拐点（t = kneeMinutes）的传说概率。0.046 = 4.6%（&lt; 5% 硬线）。</summary>
        public float s10 = 0.046f;
        /// <summary>传说概率的硬顶（用户的「任何时刻最高 10%」）。</summary>
        public float sMax = 0.10f;
        /// <summary>前 `kneeMinutes` 分钟的幂曲线指数（越大 ⇒ 越靠近拐点才放量）。</summary>
        public float gamma = 3.5f;
        /// <summary>拐点分钟数（幂曲线的终点 / 线性段的起点）。</summary>
        public float kneeMinutes = 10f;
        /// <summary>拐点之后每分钟再涨多少（绝对值，0.0027 = 0.27%/分钟）。</summary>
        public float postKneePerMinute = 0.0027f;
        /// <summary>史诗 = 传说 × 这个倍数。</summary>
        public float kA = 2.5f;
        /// <summary>稀有 = 传说 × 这个倍数。</summary>
        public float kB = 5f;
        /// <summary>高档（S+A+B）合计的安全上限，超出则等比缩回（正常参数永不触发）。</summary>
        public float lowCap = 0.90f;

        /// <summary>
        /// ★ v277：**传说概率 S(t)** —— 全工程唯一的那个算式（`ShopSystem.TierWeightsRaw` 读它）。
        /// `t` = 已进行分钟（负数按 0 处理）。
        /// </summary>
        public float SAt(float minutes)
        {
            float t = minutes > 0f ? minutes : 0f;
            float knee = kneeMinutes > 0.01f ? kneeMinutes : 10f;
            float u = t < knee ? t / knee : 1f;
            float ramp = u > 0f ? Mathf.Pow(u, Mathf.Max(0.1f, gamma)) : 0f;
            float s = s0 + (s10 - s0) * ramp;
            if (t > knee) s += Mathf.Max(0f, postKneePerMinute) * (t - knee);
            float cap = sMax > 0f ? sMax : 0.10f;
            return Mathf.Clamp(s, 0f, cap);
        }

        /// <summary>史诗概率 A(t) = kA · S(t)。</summary>
        public float AAt(float minutes) => Mathf.Max(0f, kA) * SAt(minutes);

        /// <summary>稀有概率 B(t) = kB · S(t)。</summary>
        public float BAt(float minutes) => Mathf.Max(0f, kB) * SAt(minutes);
    }

    /// <summary>v11 升级抽卡：一次升级抽 3 张卡，从主要属性里随机，相同则融合（数值相加）。</summary>
    [Serializable]
    public class RewardStatDef
    {
        public string id;
        public string name;
        public string mode;      // Add / Percent
        public int amount;       // 整数 or 整数%
    }

    [Serializable]
    public class RewardPatternDef { public string kind; public int weight; }

    [Serializable]
    public class LevelRewardDef
    {
        public int drawsPerLevel;
        /// <summary>v13：重抽消耗金币（无限次，每次价格递增）。</summary>
        public int rerollBasePrice, rerollStepPrice;
        public RewardPatternDef[] patterns = Array.Empty<RewardPatternDef>();
        public RewardStatDef[] stats = Array.Empty<RewardStatDef>();
    }

    /// <summary>工具升级项：锄头 / 水壶 / 肥料 / 播种手套（与种子同一套品质机制）。</summary>
    [Serializable]
    public class ToolUpgradeDef
    {
        public string id;
        public string name;
        public string effect;
    }

    [Serializable] public class ShopDef
    {
        // 三类商品：种子 / 插件 / 道具。
        // 第三类原为 `toolWeight`（工具升级），已由 `pluginWeight`（插件）取代 ——
        // 工具升级本身没有删除（`RunSession.UpgradeTool` / `ToolLevel` 仍在），只是**不在商店出售**。
        public int slots, rerollBase, rerollStep, seedWeight, pluginWeight, itemWeight, healWeight, healPrice;
        public float prepareTime, firstPrepareTime, healPercent, mutationChance;
        /// <summary>准备/商店阶段不限时（由玩家自己按键离开）。</summary>
        public bool infinitePrepare;
        /// <summary>
        /// ★★ v282：**品阶权重的唯一来源** —— 道具 / 插件 / 种子**三类共用这一张表** ★★
        ///
        /// 用户原话：「让道具、种子、插件刷新概率相同，只按照品质产出」。
        /// 改前本工程有**两张**品阶表（种子 `gradeWeights` + 道具/插件 `itemGradeWeights`）
        /// ⇒ 同一个"品质"在三类之间是两套概率，与「只按品质产出」直接矛盾 ✗
        /// v282 把种子的那张删掉（JSON 键与 C# 字段一起退役）⇒ 只剩本字段一处 ✓
        ///
        /// ⚠️ 本表**只定两件事**：① 档位集合与顺序；② D:C 之间的内部比例。
        ///    S/A/B 三档的概率**恒由**下面的 `rarityCurve` 按时间给出（`ShopSystem.TierWeightsRaw`）
        ///    ⇒ 三类**没有一个能绕开曲线**，也不存在"某一类的高档不吃曲线"✓
        /// </summary>
        public GradeWeightDef[] itemGradeWeights = Array.Empty<GradeWeightDef>();
        /// <summary>每高一级的价格倍率、每波的价格增长、幸运对刷新率的加成。</summary>
        public float itemGradePriceStep, priceWaveStep, luckRefreshBonus;

        /// <summary>
        /// ★★ v253：**前期高品质抑制**（用户原话：「降低前期高品质物品刷新率」）★★
        ///
        /// ── 改前的状态（可复算）──
        ///   `RunSession.RollGoodsTier` 只在**波次 > 1** 之后才把权重向高档倾斜，
        ///   第 1 波时所有档位的 mult 都是 1 ⇒ 直接吃表里的基础权重
        ///   `D 62 / C 24 / B 10 / A 3.5 / S 0.5` ⇒ **开局 A+S 就有 4%**，
        ///   而一局的前几分钟玩家连金币都不够，抽到也买不起，只会占掉货架 ✓
        ///
        /// ── 这一条怎么改 ──
        ///   给"高档"再加一个**只在前 `highTierRampWaves` 波生效**的抑制因子：
        ///     dampen(i) = 1 − (1 − t) × highBias(i) × (1 − highTierEarlyFloor)
        ///     t = clamp01((波次 − 1) / (rampWaves − 1))      ← 第 1 波 0、第 rampWaves 波 1
        ///     highBias(i) = 下标 / (档数 − 1)                 ← D 0 … S 1（越高端抑制越强）
        ///   第 1 波：D ×1、C ×0.79、B ×0.58、A/S ×**0.15** ⇒ 高档只剩原来的 15%，
        ///   到第 `highTierRampWaves` 波完全恢复成旧曲线（之后一秒不差）✓
        ///
        /// ⚠️ 只作用于"掷档位"这一处：种子的 `gradeWeights`（另一张表）**不动** ✓
        ///
        /// ★★ v277 起**退役** ★★
        ///   这套乘性抑制在 (5min ≤ 1%, 30min ≤ 10%, 单调) 三条约束下，S(10 分钟) 的数学上界
        ///   只有 **2.55%**（上一轮 520 万组穷举）⇒ 到不了用户要的 5% ⇒
        ///   权重算式改成 `RarityCurveDef` 那条**加性**时间曲线（见下面的 `rarityCurve`）。
        ///   这两个字段**保留**（`SelfCheck.cs:4248` 的消息串与只读探针仍在读），
        ///   但 `ShopSystem.TierWeightsRaw` 已**不再读它们** ✓
        /// </summary>
        public float highTierEarlyFloor = 0.15f;
        /// <summary>抑制衰减到 0 的波次（第 1 波最强、第 N 波起完全按旧曲线）。</summary>
        public int highTierRampWaves = 10;

        /// <summary>
        /// ★★ v277：**高品质概率随时间膨胀的显式曲线**（用户要的「数值膨胀公式」）★★
        ///
        /// 数据在 `tuning.json` 的 `shop.rarityCurve`；算式只有一处实现
        /// （<see cref="RarityCurveDef.SAt"/>）；读它的也只有一处
        /// （`ShopSystem.TierWeightsRaw` —— 道具 / 插件 / 种子三类商品共用）✓
        ///
        /// ★ 上面 `highTierEarlyFloor` / `highTierRampWaves` 两个字段**保留但已不参与算式** ★
        ///   它们只在 `SelfCheck.cs:4248` 的消息串与若干只读探针里被**读取打印**；
        ///   删掉会让那些文件编译不过，而它们不在本轮可改范围内 ⇒ 留成历史值 ✓
        /// </summary>
        public RarityCurveDef rarityCurve = new RarityCurveDef();

        /// <summary>v277：曲线访问器（缺键时返回字段初值那一套，永不返回 null）。</summary>
        public RarityCurveDef RarityCurve
        {
            get
            {
                if (rarityCurve == null) rarityCurve = new RarityCurveDef();
                return rarityCurve;
            }
        }

        /// <summary>
        /// ★★ v194：**20 分钟后的额外涨价斜率**（用户原话："额外提高…商品价格倍率以及刷新价格倍率
        /// （抽卡和商店）"）★★
        ///
        /// 加在 <see cref="WavePriceMult"/> 里 —— 因为**它就是这个工程唯一的价格曲线出口**：
        ///   · 商品价（种子 / 插件 / 道具）→ `SeedEconomy.Price`
        ///   · 商店刷新的**基础值** → `RunSession.RerollBaseAt` / `RerollPrice`
        ///   · 抽卡重抽价 → 同一个 `RerollPrice`（`levelReward.rerollBasePrice` 作 basePrice）
        /// 四处全走它 ⇒ 一处改动、四处同时生效 ⇒ 不可能出现"商品涨了而刷新没涨"这种分叉 ✓
        ///
        ///   `post20(t) = 1 + post20PriceSlope × max(0, t − 19)`，`t = max(0, 分钟 − 1)`
        ///
        /// · 第 20 分钟及以前 ⇒ **精确 1.0** ⇒ 既有的全部价格读数不变
        ///   （12 次重抽序列 3/6/12/…/6144、第 10 波首次刷新 9 金、D 档 15 / C 档 43 之类）✓
        /// · 第 25 分钟 ×1.5、第 30 分钟 ×2.0（取 0.10）
        ///
        /// ★ 为什么取 0.10 而不是敌人侧的 0.15 ★ 价格是**玩家要付的钱**，抬太陡会把后期商店
        ///   直接变成不可用；0.10 让 20→30 分钟的价格倍率从 3.30× 抬到 **6.62×**
        ///   （`1.13^9 × 2.0`），与敌人侧 2.5× 叠加后仍是"能买、但要挑" ✓
        ///
        /// `post20PriceSlope ≤ 0` ⇒ 关闭（因子恒为 1）⇒ 回到 v193 行为 ✓
        /// </summary>
        public float post20PriceSlope;

        /// <summary>★ v194：20 分钟后的价格因子（唯一实现，见 `post20PriceSlope` 的注释）。</summary>
        public float Post20Factor(int wave)
        {
            if (post20PriceSlope <= 0f) return 1f;
            float t = Mathf.Max(0f, wave - 1);
            return 1f + post20PriceSlope * Mathf.Max(0f, t - 19f);
        }

        /// <summary>
        /// ★★ v196：**价格锁定的分钟数 = 30**（用户原话："增加商品价格和刷新价格限制，
        /// 30 分钟后，商品价格和刷新价格锁定，敌人数值继续成长"）★★
        ///
        /// ── 锁在哪一层 ──
        /// 锁的是**波次因子**：`m > 30` 时把 `m` 当 `30` 用 ⇒ 商品价与刷新价的**底价**
        /// 冻结在第 30 分钟的读数（`W(30) = 5.000`、`W_r(30) = 10.16`、首刷 30 金）✓
        /// `post20` 项也一起冻结 —— 它乘在 `W` 里、入参同样是这个被夹过的 `w` ⇒ 不会漏 ✓
        ///
        /// ★ 唯一实现是下面的 <see cref="CappedPriceWave"/> ★
        /// `WavePriceMult` 与 `RerollWaveMult` **都先过它**，所以"锁在第 30 分钟"只有一处，
        /// 不存在"商品锁了、刷新没锁"这类分叉 ✗
        ///
        /// ★★ 而**次数惩罚不锁** ★★
        /// 同一分钟内刷第 n 次仍乘 `1.5^n`（`RunSession.RerollMult`，本文件不碰它）✓
        /// 若把 `1.5^n` 也一起锁死，"刷新越刷越贵"就退化成"无限刷新都是 30 金" ✗
        /// 第 30 分钟及以后：首刷 30 / 第 2 次 45 / 第 3 次 68 / 第 4 次 101（同一分钟内的次数差仍然存在）✓
        ///
        /// ★ 敌人数值**不看这个常量** ★ 敌人侧的 20 分钟加速是另一个函数
        /// （`EnemyDef.Post20Factor` / `EnemyGrowthDef.post20Slope`），**一个字节没动** ⇒
        /// 30 分钟后敌人继续成长（用户要的"敌人数值继续成长"）✓
        /// </summary>
        public const int PriceWaveCap = 30;

        /// <summary>
        /// ★★ v196：价格侧"波次 → 锁定后的波次"——**唯一的夹紧点** ★★
        ///
        ///   `m ≤ 30` ⇒ 原样返回（与改前**逐位相同**：旧式子里的 `Mathf.Max(0, wave−1)`
        ///              在 `wave ≥ 1` 时就是 `wave−1`，`wave = 0` 时两边都得到倍率 1.0）✓
        ///   `m > 30` ⇒ 一律返回 30 ⇒ 第 30/31/35/40…分钟的读数**逐位相同** ✓
        /// </summary>
        public static int CappedPriceWave(int wave)
            => Mathf.Min(PriceWaveCap, Mathf.Max(1, wave));

        /// <summary>
        /// ★★ v195：**刷新 / 重抽价的独立波次曲线**（用户原话："重抽几个重新计算，
        /// 前期个位数，后期 20 分钟初次重抽价格控制在以 10 为单位，30 分钟能达到控制在 100 内"）★★
        ///
        /// 　`rerollMult(w) = (1 + rerollWaveStep)^(w−1) × Post20Factor(w)`
        ///
        /// ── 为什么刷新价**不再**跟商品走同一条曲线 ──
        /// v189 让两者共用 `shop.priceWaveStep`（1.13/波），理由是"商品涨了刷新没涨会分叉" ✓
        /// 但 v195 的两条需求把两者**同时钉死**了：
        ///   · 商品：30 分钟 S 档 = **2000** ⇒ 全程商品倍率只能 **5.0×**
        ///   · 刷新：20 分钟**初次**刷新 = **十位**（≥10）⇒ 倍率要到 **3.5×**，而商品同期只有 2.2×
        /// ⇒ 一个倍率装不下两个目标（3.5 ≠ 2.2）⇒ 刷新价拿到自己的 knobs ✓
        ///
        /// 共用曲线的**目的仍然满足**：刷新价照样随分钟涨（`rerollWaveStep` > 0），
        /// 只是因为"它要压在 100 以内"而长得**慢一点**；两者的关系由探针逐分钟打表，（见 `ProbeV195Num`）✓
        ///
        /// 20 分钟后的加速**共用同一个 `post20PriceSlope`**（不是第二个斜率）：
        /// 那是"20 分钟后物价整体加速"这一条设计，刷新价属于物价 ⇒ 同一条规则、同一个旋钮 ✓
        ///
        /// ★ v196：`m > 30` **冻结**（见 `PriceWaveCap` / `CappedPriceWave`）★
        ///   第 31 分钟起 `w` 恒为 30 ⇒ `W_r` 恒为 `W_r(30) = 10.16`（首刷 30 金）✓
        ///   ⚠️ 而**次数惩罚不锁**：同一分钟内第 n 次刷新仍 `× 1.5^n`（30 / 45 / 68 / 101）✓
        /// </summary>
        public float RerollWaveMult(int wave)
        {
            // ★ v196：`m > 30` 冻结在第 30 分钟（唯一夹紧点见 `CappedPriceWave`）★
            //   `post20` 也吃同一个 `w` ⇒ 两项一起冻结 ✓
            //   ⚠️ 这里**只**锁波次因子：同一分钟内刷第 n 次的 `1.5^n` 在 `RunSession.RerollMult` 里，不碰 ✓
            int w = CappedPriceWave(wave);
            return Mathf.Pow(1f + Mathf.Max(0f, rerollWaveStep), Mathf.Max(0, w - 1)) * Post20Factor(w);
        }

        /// <summary>★ v195：刷新 / 重抽价的每波涨幅（默认 0.0682 ⇒ 第 20 波 3.50×、第 30 波 10.16×）。</summary>
        public float rerollWaveStep;

        /// <summary>
        /// v109：**波次价格曲线** —— 几何增长，替代原来的直线 `1 + step×(w-1)`。
        ///
        /// 为什么换成曲线：直线增长两头都不对。以 step=0.03 为例，
        /// 第 1 波与第 3 波的价格差不到 6%，玩家感觉不到"物价在涨"；
        /// 而到第 20 波只有 1.57×，追不上玩家同时期膨胀的金币收入，
        /// 后期商店等于白送。几何曲线**前缓后陡**，压力集中在后段。
        ///
        /// 数值对照（step=0.03）：
        ///     第 5 波   直线 1.12×   曲线 1.13×     ← 前期几乎不变
        ///     第 10 波  直线 1.27×   曲线 1.30×
        ///     第 20 波  直线 1.57×   曲线 1.75×     ← 差距集中在后期
        ///
        /// 想让它更陡只需调大 priceWaveStep —— 这个函数不用改。
        ///
        /// ★ v194：再乘一个 **20 分钟后的加速因子**（`Post20Factor`）★
        ///   几何曲线到第 20 分钟已经是 10.2×，但 20→30 分钟只再涨 3.3× —— 而玩家在
        ///   这十分钟里的金币收入是被农场行数（5 分钟一行）抬着走的 ⇒ 后期商店同样会变白送。
        ///   第 20 分钟及以前 `Post20Factor` **精确 1.0** ⇒ 本轮之前的所有价格读数逐位不变 ✓
        ///
        /// ★ v196：`m > 30` **冻结**（见 `PriceWaveCap` / `CappedPriceWave`）★
        ///   第 31 分钟起 `w` 恒为 30 ⇒ `W` 恒为 `W(30) = 5.000`、
        ///   实付价恒为 D 100 / C 270 / B 560 / A 1140 / S 2000（第 30/31/35/40 分钟**逐位相同**）✓
        /// </summary>
        public float WavePriceMult(int wave)
        {
            // ★ v196：`m > 30` 冻结在第 30 分钟（唯一夹紧点见 `CappedPriceWave`）★
            //   `post20` 项乘在本式里、入参也是夹过的 `w` ⇒ 一起冻结 ✓
            int w = CappedPriceWave(wave);
            return Mathf.Pow(1f + Mathf.Max(0f, priceWaveStep), Mathf.Max(0, w - 1)) * Post20Factor(w);
        }
    }

    [Serializable]
    public class SeedBagDef
    {
        public int slotCount, chargesPerSlot, initialChoices;
        /// <summary>无限种子：不再消耗充能（充能机制已移除）。</summary>
        public bool unlimitedSeeds;
    }

    /// <summary>
    /// 商品品质档（5 档，从低到高）。
    ///
    /// ★ 它**不再参与合成** ★
    /// 旧规则是"两个「同 id 同品质」的种子合体、品质 +1 并获得数值成长" ✗
    /// 现在的合成走**等级**轴：两个**同种同级**的种子合成一个高一级的（1→2→3，3 级封顶）✓
    /// 品质自 v44 起由种子定义固定（`def.RarityTier`），只决定配色与定价倍率 ✓
    /// 见 `SeedSlot.Level` 与 `SeedBag.CanMerge`
    /// </summary>
    [Serializable]
    public class QualityDef
    {
        /// <summary>v23：稀有度颜色名（白绿紫红金）。</summary>
        public string[] colorNames
        {
            get { return new[] { "白", "绿", "紫", "红", "金" }; }
        }

        public string[] names = Array.Empty<string>();
        /// <summary>每档的数值倍率（攻击与生命同步放大）。</summary>
        public float[] mult = Array.Empty<float>();
        /// <summary>商店刷出各档的权重。</summary>
        public int[] shopWeights = Array.Empty<int>();

        public int Max => names != null && names.Length > 0 ? names.Length - 1 : 0;

        public int Clamp(int level) => Mathf.Clamp(level, 0, Max);

        public string NameOf(int level)
        {
            if (names == null || names.Length == 0) return "白";
            return names[Clamp(level)];
        }

        public float MultOf(int level)
        {
            if (mult == null || mult.Length == 0) return 1f;
            return mult[Clamp(level)];
        }

        /// <summary>按权重随机一档（用世界随机流，保持确定性）。</summary>
        /// <summary>
        /// v37：按波次与幸运向高品质倾斜的权重抽取。
        /// 基础权重 白50/绿30/蓝15/紫4/金1；
        /// 倾斜 = 把低档权重按比例搬到高档（波次 + 幸运），
        /// **硬约束**：任何档位的权重不得超过它前一档（最终极限 = 全档均等）。
        /// </summary>
        public int RollShifted(Pcg32 rng, int wave, float luck)
        {
            int n = 5;
            // v41 初始爆率：普通 50% / 一般 30% / 稀有 19% / 史诗 1% / 传奇 0%
            float[] w = { 50f, 30f, 19f, 1f, 0f };
            if (luck < 0f) luck = 0f;
            // 概率迁移：随波次与幸运，把质量从低档**逐级**搬到高档
            //   迁移强度 = 波次贡献 + 幸运贡献（幸运放大迁移量）
            float wavePart = 0.015f * Mathf.Max(0, wave - 1);
            float luckPart = 0.04f * luck * (1f + 0.02f * Mathf.Max(0, wave - 1));
            float t = Mathf.Clamp(wavePart + luckPart, 0f, 0.85f);
            for (int i = 0; i < n - 1; i++)
            {
                // 每级只搬走一部分，逐级向上渗透（高品质不会凭空出现）
                float move = w[i] * t;
                w[i] -= move;
                w[i + 1] += move * 0.85f;   // 15% 在迁移中损失，避免高档被灌满
            }
            // 保证高档权重不低于 1（一旦解锁就能抽到）
            for (int i = 0; i < n; i++) if (w[i] < 1f) w[i] = 1f;
            // 单调约束：不得高于前一档（极限为均等）
            for (int i = 1; i < n; i++)
                if (w[i] > w[i - 1]) w[i] = w[i - 1];

            int total = 0;
            for (int i = 0; i < n; i++) total += Mathf.Max(1, Mathf.RoundToInt(w[i]));
            int roll = rng.RangeInt(0, Mathf.Max(1, total));
            for (int i = 0; i < n; i++)
            {
                roll -= Mathf.Max(1, Mathf.RoundToInt(w[i]));
                if (roll < 0) return Clamp(i);
            }
            return 0;
        }

        /// <summary>
        /// v37：按稀有度定价（低档便宜、高档显著更贵）。
        ///
        /// ★★ v195：档位倍率压缩为 1.0 / 1.8 / 2.8 / 3.8 / 5.0（原来是 1 / 1.8 / 3.2 / 5.6 / 9）★★
        ///
        /// 用户原话："希望即使到达 30 波，传奇商品价格还是以千为单位，普通商品价格以百为单位 …
        ///            商品价格 30 分钟最高理想达到 2000"
        ///
        /// ── 为什么必须压 S 档（9 → 5），这是**一道不等式**不是口味 ──
        /// 实付价 = 基础价 × 本倍率 × 波次倍率(W)。若 W(30) = 5（见 `WavePriceMult`）：
        ///   目标·传奇 2000 ⇒ 基础价 × 倍率 ≤ 400
        ///   目标·普通 ≥ 100 ⇒ 基础价(D) × 1.0 ≥ 20
        /// 而各档基础价现在**统一**为 D20 / C30 / B40 / A60 / S80（种子表本来就是这样，
        /// 道具表 v195 对齐到同一条阶梯 ⇒ 同档同价）⇒
        ///   S：80 × 5.0 × 5 = **2000** ✓（若沿用 9.0 ⇒ 80×9×5 = 3600 ✗ 破 2000 上限）
        ///   D：20 × 1.0 × 5 = **100** ✓（百位下沿）
        /// 中间三档取 1.8 / 2.8 / 3.8 ⇒ 30 分钟 270 / 560 / 1140，单调、全在 2000 以下 ✓
        /// ⇒ 全程 S:D 的实付价 = 20:1（**正好**是"2000 : 100"），不存在某一档被夹扁 ✓
        /// </summary>
        public float PriceMult(int tier)
        {
            switch (Mathf.Clamp(tier, 0, 4))
            {
                case 0: return 1f;
                case 1: return 1.8f;
                case 2: return 2.8f;
                case 3: return 3.8f;
                default: return 5f;
            }
        }

        public int Roll(Pcg32 rng)
        {
            if (shopWeights == null || shopWeights.Length == 0) return 0;
            int total = 0;
            for (int i = 0; i < shopWeights.Length; i++) total += Mathf.Max(0, shopWeights[i]);
            if (total <= 0) return 0;
            int roll = rng.RangeInt(0, total);
            for (int i = 0; i < shopWeights.Length; i++)
            {
                roll -= Mathf.Max(0, shopWeights[i]);
                if (roll < 0) return Clamp(i);
            }
            return 0;
        }
    }

    [Serializable] public class WavesCfg
    {
        // v165：`total`（= 20）已删除 ✗
        // 它唯一的作用是喂给 `TotalWaves`，而 `TotalWaves` 的消费方只剩结算画面的
        // "波次 N/20" 一行。计时制没有终点，"回合数上限"这个概念本身就不存在了。
        // ★ v191：`pigChance`（0.01）**已删除** ✗ ★
        //   用户需求③："小猪刷新率与爱心掉落率**相同的机制与数值**" ⇒ 概率不再是
        //   一个常量配置，而是 **属性等级 → 查表**（`StatId.PigRate` 0..3 ⇒ 2/4/6/8%）✓
        //   ★ 为什么删键而不是留着当"基准" ★ 留着的字面后果是"两处真相"：
        //     表里写 2%，代码里再乘一个 1% 的基准 ⇒ 谁也说不清最终是多少，
        //     而调平衡的人会去改那个**不生效**的键 ✓（本工程对这类"看着能调、实际不生效"
        //     的键有明确纪律：删掉，或者写明唯一的消费点）
        //   ⇒ 现在调小猪概率的唯一入口是 `TuningDef.PigRateByLevel`（那 4 个数就是设计值）✓
        //   ⚠️ 想临时把整档压低/抬高，改的是那 4 个数（等级表），不是复活这个键。
        /// <summary>v10 刷怪节奏：起始/最低刷新间隔、每批基础数量与其波次/进度系数、场上存活上限。</summary>
        /// <summary>v10：场上存活植物上限（树的产出超过它时，最老的植物枯萎）。</summary>
        public int maxLivePlants;
        // ★ v166 删除 `overflowOrbDelay` ★ —— "溢出植物化成治疗光球前的延迟" ✓
        // 光球整条链已删除（`SpawnHealOrb` 0 调用者 + 新玩法里植物不受伤、不需要治疗）
        // ⇒ 这个字段**没有读者**了 ✓ 留着它 = 让配置表看起来还有一条不存在的机制 ✗
        // ⚠️ `tuning.json` 的 `waves` 段里可能仍有这个键 —— 那是回合组的地界，我没动 ✓
        //    （JsonUtility 对多余键是**忽略**，所以不会报错 ✓）
        public float spawnIntervalStart, spawnIntervalMin;
        public int spawnBaseCount;
        public float spawnCountPerWave, spawnCountByProgress;
        /// <summary>v14：场上敌人硬上限（超出只预警不生成）与生成预警时长。</summary>
        public int maxAliveCap;
        public float spawnWarnSeconds;
        public int maxAliveBase;
        public float maxAlivePerWave;

        /// <summary>
        /// v116：**刷怪数量的连续成长系数**（需求："有多少波就成长多少次"）。
        ///
        ///   额外存活 = floor(maxAliveGrowth × √(波次−1))
        ///   额外刷新 = floor(spawnCountGrowth × √(波次−1))
        ///
        /// ★ 为什么数量用平方根、而 HP/伤害用「线性+二次」★
        /// 两者的形状刻意相反：
        ///   数值 —— 后期**加速**（玩家在变强，敌人必须跟上）
        ///   数量 —— 后期**减速**（数量加速会撞性能悬崖；而且"人海"比"变硬"更快摧毁手感）
        /// 但**都不封顶**：只要还有波次，就还在成长。
        ///
        /// 旧实现是 `Clamp(系数 × (min(2^(波次−1), 64) − 1), 0, 上限)` ——
        /// 第 7 波同时撞上指数封顶与数量上限，之后纹丝不动。
        /// </summary>
        public float maxAliveGrowth, spawnCountGrowth;

        /// <summary>
        /// v127：是否启用**存活数量上限**。
        ///
        /// 关掉之后同时放开三道闸门：整拍停刷、单拍截断、硬上限抑制。
        /// 注意它只约束"场上同时存在多少"，**不约束刷新速率** ——
        /// 刷新速率由 spawnCountGrowth 的 √ 曲线决定（永不停止）。
        ///
        /// ★ v136：实测后**改回 true**（曾一度置 false 以满足"解放数量上限"）★
        ///
        /// 实测数据（机器人自动战斗，`unity_log` 采样）：
        ///   无敌人      drawCalls 2143   fps 58      ← 基线
        ///   第 1 波     drawCalls 4024   fps 43      ← **仅第 1 波，drawCalls 几乎翻倍**
        ///
        /// 而数量曲线在第 20 分钟是"额外存活 42 + 每拍刷 15"，第 30 分钟是 52 + 19。
        /// 第 1 波（额外存活 ≈ 0）就已经让 drawCalls 翻倍，往后必然崩 ✗
        ///
        /// ★ 关键认识：**"数量无上限"和"成长不停止"是两件事** ★
        /// 需求要的是"成长不停"——那由 **√ 曲线** 保证（它永不封顶，已实现）✓
        /// 而"同一时刻场上多少"是**性能约束**，与成长无关 ✗
        /// 硬上限既不影响成长曲线，也不影响"总共出多少"，只防止帧率崩溃 ✓
        ///
        /// 如果将来要再关它，**请先跑一次第 20 分钟的帧率采样**，不要凭判断改 ✗
        /// （这条注释存在的意义就是防止"再解放一次"。）
        /// </summary>
        public bool aliveLimitEnabled = true;
        public float dropAttractRadius, pickupRadius, spawnRingRadius, spawnRingInner;
        /// <summary>普通波的硬上限倍率：超过 duration×该值仍未清场则强制结束，防止"只跑不打"软锁。</summary>
        public float hardCapFactor;
        /// <summary>开局免刷怪时间（秒）：这段时间只种地，不刷怪。</summary>
        public float graceSeconds;
        /// <summary>Boss 波时限（秒）：超时未能击败 Boss 视为失守，避免与自愈 Boss 无限僵持。</summary>
        public float bossTimeLimit;
    }

    [Serializable] public class CameraDef
    {
        /// <summary>镜头与地面的夹角（度）；镜头方向锁定，不随鼠标旋转。</summary>
        public float pitch;
        /// <summary>是否正交（false = 透视，避免"上帝视角"观感）。</summary>
        public bool orthographic;
        /// <summary>正交可视尺寸（越小越近）。</summary>
        public float orthoSize;
        /// <summary>透视视场角与机位距离。</summary>
        public float fov;
        public float distance;
        /// <summary>镜头架设高度（正交下只影响裁剪，不影响可视范围）。</summary>
        public float height;
        /// <summary>跟随插值速度。</summary>
        public float followLerp;
        /// <summary>是否把镜头裁在场地边界内（禁止展示板外）。</summary>
        public bool clampToArena;
    }

    [Serializable]
    public class UiDef
    {
        public float scale;
        /// <summary>按键说明面板默认是否显示（Tab 切换）。</summary>
        public bool keyHelpDefault;
        /// <summary>v48：浮动文字在对象周围的随机散布半径（米）。</summary>
        public float popupRadius = 0.6f;
        /// <summary>v48：浮动文字基础字号。</summary>
        public float popupFontSize = 26f;
    }

    [Serializable] public class MetaDef { public float fixedDt; public int maxCatchUpTicks; public long runSeed; }

    [Serializable]
    public class TuningDef
    {
        public PlayerBaseDef player = new PlayerBaseDef();
        public GrowthDef growth = new GrowthDef();
        public CapsDef caps = new CapsDef();
        public FarmDef farm = new FarmDef();
        public FormulaDef formula = new FormulaDef();
        /// <summary>★ v279：三种持续伤害的百分比与单跳上限（唯一出处，见 `DotsDef`）★</summary>
        public DotsDef dots = new DotsDef();
        /// <summary>★ v282：吸血的两条上限（唯一出处，见 `LifestealDef`）★</summary>
        public LifestealDef lifesteal = new LifestealDef();
        public ToolsDef tools = new ToolsDef();
        public ShopDef shop = new ShopDef();

        // ────────────────────────────────────────────────────────────
        // v110：**爱心掉落率等级表**（需求：改成等级制）
        //   低 / 中 / 高 / MAX = 10% / 15% / 20% / 25%
        //
        // 为什么用**等级**而不是直接存百分比：
        //   配套道具的语义是"当前等级直接转到下一级"。
        //   用等级存，道具就是 `add: 1` —— 天然就是升一级；
        //   `maxStack: 3` 也天然就是"升到 MAX 就不能再买"。
        //   若存百分比，道具得写成"+5%"，玩家叠到 25% 之后还能继续买，规则就守不住了。
        //
        // 注意 `Stats` 里这个属性的**上限是 3**（不是 1）——它现在是等级不是概率。
        // ------------------------------------------------------------
        /// v119：数值从 10/15/20/25% **大幅下调为 2/4/6/8%**（需求）。
        /// 原值在实机里爱心掉得太频繁，回血压力消失，玩家不再需要"要不要冒险去捡"这个决策。
        /// 等级制结构不变，只改这四个数 —— 这正是等级制的好处：调平衡不碰任何逻辑。
        public static readonly float[] HeartDropRateByLevel = { 0.02f, 0.04f, 0.06f, 0.08f };
        public static readonly string[] HeartDropLevelNames = { "低", "中", "高", "MAX" };
        public const int HeartDropMaxLevel = 3;   // = HeartDropRateByLevel.Length - 1

        /// <summary>等级 → 实际掉落率。越界一律钳到合法区间，不抛异常。</summary>
        public static float HeartDropRateOf(int level)
            => HeartDropRateByLevel[Mathf.Clamp(level, 0, HeartDropMaxLevel)];

        /// <summary>等级 → 显示名（低/中/高/MAX）。</summary>
        public static string HeartDropLevelNameOf(int level)
            => HeartDropLevelNames[Mathf.Clamp(level, 0, HeartDropMaxLevel)];

        // ────────────────────────────────────────────────────────────
        // ★ v191：**小猪刷新率等级表**（用户需求③）★
        //
        // 用户原话："新增次要属性「小猪刷新率」，与爱心掉落率相同的**机制与数值**，
        //           但影响小猪的刷新几率" ⇒ 所以这里**逐字照抄**上面那三条：
        //   同一套 4 级（0..3）、同一组数值（2% / 4% / 6% / 8%）、同一组名字（低/中/高/MAX）✓
        //
        // ★ 为什么用等级存、而不是直接存概率（与爱心同一条理由）★
        //   配套道具的语义是"当前等级直接转到下一级" ⇒ 道具就是 `add: 1`（天然升一级），
        //   `maxStack: 3` 也天然就是"升到 MAX 就不能再买" ✓
        //   若存百分比，道具得写 "+2%"，玩家叠到 8% 之后还能继续买，规则就守不住了 ✗
        //
        // ★ 于是"属性里存的是**等级整数**、不是概率"★ ——
        //   读者必须先过这一层查表（`RunSession.WaveDirector` 的小猪那一行），
        //   直接拿等级当概率用会得到"300% 刷新率"这种东西（爱心那条踩过同一个坑）。
        //   `StatBlock.CapOf(PigRate)` 把属性硬钳在 **3**（= MAX），
        //   与 `HeartDropRate` 钳 3 同构 ⇒ 等级永远落在表内 ✓
        // ------------------------------------------------------------
        public static readonly float[] PigRateByLevel = { 0.02f, 0.04f, 0.06f, 0.08f };
        public static readonly string[] PigLevelNames = { "低", "中", "高", "MAX" };
        public const int PigRateMaxLevel = 3;   // = PigRateByLevel.Length - 1

        /// <summary>小猪刷新率等级 → 实际刷新概率。越界一律钳到合法区间，不抛异常。</summary>
        public static float PigRateOf(int level)
            => PigRateByLevel[Mathf.Clamp(level, 0, PigRateMaxLevel)];

        /// <summary>小猪刷新率等级 → 显示名（低/中/高/MAX），与爱心同一套口径。</summary>
        public static string PigLevelNameOf(int level)
            => PigLevelNames[Mathf.Clamp(level, 0, PigRateMaxLevel)];

        public SeedBagDef seedBag = new SeedBagDef();
        public ToolUpgradeDef[] toolUpgrades = Array.Empty<ToolUpgradeDef>();
        public QualityDef quality = new QualityDef();
        public LevelRewardDef levelReward = new LevelRewardDef();
        public WavesCfg waves = new WavesCfg();
        /// <summary>v47：敌人成长（生命与伤害统一走 2 的回合次方）。</summary>
        public EnemyGrowthDef enemies = new EnemyGrowthDef();
        public CameraDef camera = new CameraDef();
        public UiDef ui = new UiDef();
        public MetaDef meta = new MetaDef();

        /// <summary>
        /// ★ v192：**自充节拍的安全读数**（`tools.canAutoChargeInterval`；`≤ 0` 回落 0.05 秒）★
        ///
        /// 为什么要包一层而不是直接用 `tools.canAutoChargeInterval`：
        /// 那个键一旦被填成 0（或老配置里根本没有它 ⇒ JsonUtility 给 0），
        /// `TickAutoCharge` 里的 `floor(timer / interval)` 就会**除零**（得到 ∞ 次/帧的拍数）✗
        /// 兜底写在这里、而不是写进消费点，是为了让"这个键的最小合法值"只有一处定义 ✓
        /// </summary>
        public float EffectiveCanAutoChargeInterval
            => tools != null && tools.canAutoChargeInterval > 0f ? tools.canAutoChargeInterval : 0.05f;

        /// <summary>
        /// ★ v192：**整点回血的比例**（`player.minuteHealPercent`，钳在 0..1）★
        /// ★ 为什么字段初始值就是 1.0 ★ JsonUtility 只覆盖**JSON 里出现过的键**，
        /// 没出现的键保留字段初始值 ⇒ 老配置/漏写这个键时仍然是"回满"（= 改前行为），
        /// 而表里显式写 `0` 才是"关闭回血" ✓ 两者因此可以区分
        /// （若靠 `≤ 0 回落 1` 兜底，就再也关不掉了 ✗）
        /// </summary>
        public float EffectiveMinuteHealPercent
            => player != null ? Mathf.Clamp01(player.minuteHealPercent) : 1f;

        // ────────────────────────────────────────────────────────────
        // ★★ v279：三种持续伤害的比例 —— **唯一读取面** ★★
        //
        // 为什么包一层属性，而不是让三个消费点各自 `t.dots.burnPercent > 0f ? … : 0.5f`：
        //   那样"0.5 这个兜底值"就有三份拷贝，其中一份迟早被改成别的数 ⇒
        //   三个 DoT 的兜底口径分叉，而这种分叉不抛异常、只在老表上表现成"某个 DoT 弱一档" ✗
        // `≤ 0` ⇒ 回落 0.5（与 v278 及更早的写死值逐位相同：表里漏写这个键时行为不变）✓
        // `tickMaxHpFraction` 额外钳到 (0, 1]：写 0 会让**所有** DoT 变成 0 伤害（静默失效），
        //   写 > 1 则是"最多打满一管血"，等于把上限关掉 —— 两者都不是设计者会要的值 ✓
        // ------------------------------------------------------------
        /// <summary>灼烧比例（每层每秒 = 锄头这一刀的实际伤害 × 本值）。</summary>
        public float DotsBurnPercent => DotsPercentOr(dots != null ? dots.burnPercent : 0f);
        /// <summary>减速比例（每层每秒 = 水壶这一束的实际伤害 × 本值）。</summary>
        public float DotsSlowPercent => DotsPercentOr(dots != null ? dots.slowPercent : 0f);
        /// <summary>毒区比例（每层每秒 = 肥料这一炮的实际伤害 × 本值）。</summary>
        public float DotsPoisonPercent => DotsPercentOr(dots != null ? dots.poisonPercent : 0f);
        /// <summary>单次 DoT 结算上限占目标 `MaxHp` 的比例（0.25 = 25%）。</summary>
        public float DotsTickMaxHpFraction
        {
            get
            {
                float v = dots != null ? dots.tickMaxHpFraction : 0f;
                return v > 0f ? Mathf.Clamp01(v) : 0.25f;
            }
        }

        private static float DotsPercentOr(float v) => v > 0f ? v : 0.5f;

        // ────────────────────────────────────────────────────────────
        // ★★ v282：吸血两条上限的比例 —— **唯一读取面** ★★
        //
        // 与上面三个 DoT 比例同一条纪律：兜底值只在这里出现一份，
        // 四个消费点（挥击 / 剑气 / 激光 / 爆炸）不各自写 `> 0f ? … : 0.05f`。
        // `≤ 0` 是**合法设计值**（= 关掉这条上限），所以这里**不**把它抬成 0.05：
        //   字段初始值已经是 0.05（老表漏写键 ⇒ 按设计值走），
        //   而表里显式写 0 的人要的是"不夹"，两者必须能区分 ✓
        // 负数 / NaN 一律当 0（= 关闭），避免 `MaxHp × 负比例` 变成负上限。
        // ------------------------------------------------------------
        /// <summary>一次攻击事件的吸血上限占玩家 `MaxHp` 的比例（0 = 关闭）。</summary>
        public float LifestealPerSwingFraction
            => FractionOr(lifesteal != null ? lifesteal.perSwingMaxHpFraction : 0f);

        /// <summary>1 秒桶内的吸血上限占玩家 `MaxHp` 的比例（0 = 关闭）。</summary>
        public float LifestealPerSecondFraction
            => FractionOr(lifesteal != null ? lifesteal.perSecondMaxHpFraction : 0f);

        private static float FractionOr(float v) => float.IsNaN(v) || v < 0f ? 0f : v;
    }

    // ---------------------------------------------------------------- 运行时定义（枚举已解析）
    /// <summary>
    /// v36：稀有度体系——旧版 D/C/B/A/S「等级」与「品质」文字已彻底废弃。
    ///
    /// ★ 五档 = 白 / 绿 / 蓝 / 紫 / 金 = 普通 / 一般 / 稀有 / 史诗 / 传奇 ★
    /// （v97 修正：这条注释原来写的是"普通/稀有/史诗/传奇/神话（白/绿/紫/红/金）"，
    ///   与下面两张数组**自相矛盾**——数组才是真的。注释骗过了一次：
    ///   UI 当时就是照着这句错话另写了一份名字表，于是"传奇"被画成了紫色。）
    /// </summary>
    public static class Rarity
    {
        public static readonly string[] Names = { "普通", "一般", "稀有", "史诗", "传奇" };
        public static readonly string[] Colors = { "白", "绿", "蓝", "紫", "金" };
        public static string NameOf(int tier) => Names[Mathf.Clamp(tier, 0, Names.Length - 1)];
        public static string ColorOf(int tier) => Colors[Mathf.Clamp(tier, 0, Colors.Length - 1)];
        public static int TierOf(SeedGrade g)
        {
            switch (g)
            {
                case SeedGrade.D: return 0;
                case SeedGrade.C: return 1;
                case SeedGrade.B: return 2;
                case SeedGrade.A: return 3;
                default: return 4;
            }
        }
    }

    public sealed class SeedDef
    {
        public string Id, Name, Desc, Special;
        public SeedGrade Grade;
        public AttackType AttackType;
        public AttackKind AttackKind;
        public SupportMode Support;
        public float BaseValue, InheritMult, GrowTime, BaseHp, BaseShield, AttackInterval, Range, MoveSpeed;

        /// <summary>
        /// v112：植物**自己的**初始暴击率与暴击伤害（需求 ⑬："每个植物基础暴击率和暴击伤害可不同，
        /// 植物拥有初始暴击率"）。
        ///
        /// 这与"玩家初始暴击率为 0%"**并不矛盾**，两者是两套基准：
        ///   玩家 —— 初始 0%，完全靠道具堆起来
        ///   植物 —— 各自带一个初始值，品种之间可以不同
        /// 所以植物打出来的暴击率是 `自己的基础 + 玩家属性的增量`，
        /// 而不是"玩家 0% 就永远不暴击"。
        ///
        /// BaseCritDmg 用**倍率**（2.0 = 两倍），与 tuning.player.critDmg 同一语义。
        /// </summary>
        public float BaseCritRate, BaseCritDmg;
        /// <summary>★ v179：**种子售价** —— 只进商店定价，**不再参与收益** ✗（见 GoldYield/ExpYield）</summary>
        public int Price;
        /// <summary>
        /// ★ v179：**每格金币基数 / 每格经验基数**（收益的唯一来源）★
        ///
        /// 结算：`max(1, round(GoldYield × 等级倍率 × 植物机制 × 道具机制))`
        /// 与 `max(1, round(ExpYield × 等级倍率))` —— 三级乘区在 `Actors.HarvestPlot`，
        /// 基数这两个数只在这里 ✓（口径由 `SeedEconomy.CellGold` / `CellExp` 单点给出 ✓）
        ///
        /// 为什么与 `Price` 脱钩：见 `SeedDefRaw.goldYield` 的说明（同档内金币/经验要能反向分配）。
        /// </summary>
        public int GoldYield, ExpYield;
        /// <summary>v23：独特机制 + 蓄水（整数）。</summary>
        public string Mechanic;
        /// <summary>
        /// v111：**基础爆炸范围**（整数米）。实际半径 = 本值 × 爆炸范围属性。
        /// ≤0 按 3 处理——这样 16 个植物的数据不必一次填完，
        /// 而"会炸的那几个"可以各自给出不同的爆炸体量。
        /// </summary>
        public int baseExplodeRange;
        public int WaterMax, WaterDrain;

        /// <summary>v36：稀有度档位（0 普通 … 4 传奇）。</summary>
        public int RarityTier => Rarity.TierOf(Grade);
        public string RarityName => Rarity.NameOf(RarityTier);
        public bool IsAttacker => AttackType != AttackType.Support;
        public string GradeName => Grade.ToString();
    }

    public sealed class MutationDef
    {
        public string Id, Name, Desc;
        public float InheritMultBonus;
    }

    public sealed class EnemyDef
    {
        public string Id, Name, ColorHex;
        public EnemyKind Kind;
        public Color Color = Color.white;
        public float BaseHp, MoveSpeed, TouchDamage, TouchCooldown,
                     AttackRange, SafeRange, ProjectileDamage, ProjectileSpeed, AttackInterval, Radius,
                     JumpDamage, JumpRadius, JumpCooldown, JumpRange, JumpWindup;
        public int Gold, Exp;

        /// <summary>
        /// v41/v47：随回合的成长统一为**2 的回合次方**（生命与伤害同一套指数）。
        /// expTerm = min(2^(wave-1), tuning.enemies.expCap)：到封顶波次后不再增长，避免后期数值爆炸。
        /// </summary>
        public static float ExpTerm(int wave)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            float cap = g != null && g.expCap > 0f ? g.expCap : 64f;
            return Mathf.Min(Mathf.Pow(2f, Mathf.Max(0, wave - 1)), cap);
        }

        /// <summary>
        /// v115：波次成长曲线（唯一实现）。HP 与伤害共用，只是系数不同。
        /// 系数在 `EnemyGrowthDef` 上；这里只做纯计算，方便自检直接验曲线。
        /// </summary>
        public static float WaveGrowth(int wave, float a, float b)
        {
            float t = Mathf.Max(0, wave - 1);
            return 1f + a * t + b * t * t;
        }

        /// <summary>
        /// ★ v190：该敌人是否走**精英 HP 曲线**（`eliteHpStepA > 0` 才启用）。
        /// 唯一的判据就在这里，`HpForWave` 与探针/自检都读它 ⇒ 不会出现第二份定义。
        /// </summary>
        public bool UsesEliteHpCurve =>
            Kind == EnemyKind.Jumper
            && TableDatabase.EnemyGrowth != null
            && TableDatabase.EnemyGrowth.eliteHpStepA > 0f;

        /// <summary>
        /// ★ v190：该敌人是否走 **BOSS 专用 HP 曲线**（`bossHpStepA > 0` 才启用）。
        /// ★ 为什么要单独一条 ★ BOSS 吃标准曲线的 28.571× ⇒ 第 20 分钟 **8571 血**
        /// （改前 1634）⇒ 那不是"更难的 BOSS"，那是**不可玩** ✗（同一版伤害曲线下它还带
        /// 15 点跳砸，对 10 血玩家一次即秒）。而"把 BOSS 固定成旧值"是**代码里的魔数** ✗ ⇒
        /// 配成一条可调的曲线（见 `EnemyGrowthDef.bossHpStepA` 的长注释）✓
        /// </summary>
        public bool UsesBossHpCurve =>
            Kind == EnemyKind.Boss
            && TableDatabase.EnemyGrowth != null
            && TableDatabase.EnemyGrowth.bossHpStepA > 0f;

        /// <summary>★ v190：该敌人是否走 **BOSS 专用伤害曲线**（`bossDmgStepA > 0` 才启用）。</summary>
        public bool UsesBossDmgCurve =>
            Kind == EnemyKind.Boss
            && TableDatabase.EnemyGrowth != null
            && TableDatabase.EnemyGrowth.bossDmgStepA > 0f;

        /// <summary>
        /// ★ v191：**前段压低因子** —— 唯一实现（`HpForWave` 与 `DamageForWave` 都只用这一个）。
        ///
        /// 公式（`t = 分钟 − 1`，与 <see cref="WaveGrowth"/> 同一刻度）：
        ///   `t ≥ earlyNerfEnd`  ⇒ **1.0（精确，不带残差）**
        ///   `t &lt; earlyNerfEnd`  ⇒ `1 − earlyNerf × (1 − t/earlyNerfEnd)²`
        /// 形状与取值的全部理由见 `EnemyGrowthDef.earlyNerf` 的长注释 ✓
        ///
        /// ★★ 为什么 BOSS 在这里被**显式排除** ★★
        /// 用户原话是"下调前期敌人数值 … BOSS 暂时不调整"⇒ BOSS 的数值**在任何波次**都必须
        /// 与改前逐位相同。而"BOSS 最早第 10 分钟才出场、窗口只到 8 分钟"只是**当前时刻表**的
        /// 巧合：`BossSchedule` 一旦被改早（例如调试跳分钟、或无头测试直接 `StartWave(1)`），
        /// BOSS 就会跟着吃 35% 的削弱 ✗ 那不是用户要的。所以这里不让它"恰好躲过"，
        /// 而是**结构性保证**：BOSS 的因子恒为 1 ✓
        /// 自检有一条断言钉它（`BOSS 不吃前段压低`，第 1 分钟血 = 300）✓
        /// </summary>
        public static float EarlyNerfFactor(int wave)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            if (g == null || g.earlyNerf <= 0f || g.earlyNerfEnd <= 0f) return 1f;
            float t = Mathf.Max(0f, wave - 1);
            if (t >= g.earlyNerfEnd) return 1f;             // 窗口外：**精确** 1 ⇒ 20 分目标逐位不变
            float u = 1f - t / g.earlyNerfEnd;              // 1 → 0
            return 1f - g.earlyNerf * u * u;
        }

        /// <summary>
        /// ★★ v194：**20 分钟后的额外加速因子** —— 唯一实现 ★★
        /// （`HpForWave` / `DamageForWave` / `HpMultiplierForWave` 三处都只调它，没有第二份式子）
        ///
        ///   `post20(t) = 1 + post20Slope × max(0, t − 19)`，`t = max(0, wave − 1)`
        ///
        /// · `t ≤ 19`（第 20 分钟及以前）⇒ **精确 1.0** —— 20 分钟那批硬目标是逐位不变的保证 ✓
        /// · `t = 24`（第 25 分钟）⇒ `1 + 5k`；`t = 29`（第 30 分钟）⇒ `1 + 10k`
        ///
        /// ★ 为什么 BOSS 也吃这一项 ★ 用户说的是"**所有**敌人"；而它的第 20 分钟同样精确 1.0
        ///   ⇒ 既有的 BOSS 断言（第 10/20 分钟那批）一个都不受影响 ✓
        ///   BOSS 唯一被**排除**的是 `EarlyNerfFactor`（那是另一条需求："BOSS 暂时不调整"），
        ///   两条的排除/纳入范围**不同**，不是笔误 ✓
        /// </summary>
        public static float Post20Factor(int wave)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            if (g == null || g.post20Slope <= 0f) return 1f;
            float t = Mathf.Max(0f, wave - 1);
            return 1f + g.post20Slope * Mathf.Max(0f, t - 19f);
        }

        /// <summary>该敌人实际使用的 HP 曲线系数 + 前段压低因子（诊断/探针读它）。</summary>
        public float HpMultiplierForWave(int wave)
        {
            HpCurveCoefficients(out float a, out float b);
            return WaveGrowth(wave, a, b)
                 * (Kind == EnemyKind.Boss ? 1f : EarlyNerfFactor(wave))
                 * Post20Factor(wave);
        }

        /// <summary>
        /// ★ v190：**该敌人实际使用的 HP 曲线系数**（`(a, b)`）—— 曲线系数的唯一出口。
        ///
        /// 三个读者都走这里：`HpForWave`（真正的消费点）、探针 `ProbeV190.ProbeCurve`、
        /// 自检的差分断言。所以"某条曲线被读漏"这类 bug 在这里一处就能发现，
        /// 而不会出现"表里配了、某一条路径还在用旧系数"的分叉 ✓
        ///
        /// 三条曲线同一个公式、同一个形状（`b/a = 5%` 的既有形状），只是振幅不同：
        ///   标准家族（pig / grunt / shooter）→ `hpStepA/B`          ⇒ 20 分 28.571×
        ///   精英家族（Jumper）              → `eliteHpStepA/B`      ⇒ 20 分 11.905×
        ///   BOSS                            → `bossHpStepA/B`       ⇒ 20 分 11.000×（= 3300 血）
        ///
        /// 回落规则（与 `hpStepA/B` 原有的写法同构，见 `EnemyGrowthDef.hpStepA` 的说明）：
        ///   `hpStepA ≤ 0`      ⇒ 0.12（老兜底值）
        ///   `hpStepB ≤ 0`      ⇒ 0.006（老兜底值）
        ///   家族曲线 > 0       ⇒ 换成家族那一对；家族 B **允许 0**（纯线性）✓
        /// ★ BOSS 先判、精英后判：两者互斥（Kind 不可能同时是 Boss 和 Jumper），顺序只影响可读性 ★
        /// </summary>
        public void HpCurveCoefficients(out float a, out float b)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            a = g != null && g.hpStepA > 0f ? g.hpStepA : 0.12f;
            b = g != null && g.hpStepB > 0f ? g.hpStepB : 0.006f;
            if (UsesBossHpCurve)
            {
                a = g.bossHpStepA;
                b = Mathf.Max(0f, g.bossHpStepB);
            }
            else if (UsesEliteHpCurve)
            {
                a = g.eliteHpStepA;
                b = Mathf.Max(0f, g.eliteHpStepB);
            }
        }

        /// <summary>
        /// ★ v190：**该敌人实际使用的伤害曲线系数**（`(a, b)`）—— 与 HP 侧同构的第二个出口。
        ///
        /// 为什么伤害也要分成"标准 / BOSS"两条：用户给 BOSS 的三招目标是
        /// `碰撞 4 / 子弹 2.5~3 / 跳砸 8~10`（第 20 分钟），而标准家族此刻要的是
        /// `6 / 4 / 8` —— 两套倍率不同（4.76× vs 8.57×）⇒ 一条曲线装不下 ✗
        /// ⇒ `bossDmgStepA/B`（4.76×）与 `dmgStepA/B`（8.57×）并列 ✓
        ///
        /// ★ BOSS 的**基础伤害一个字没改** ★（touchDamage 0.84 / projectileDamage 0.56 /
        /// jumpDamage 1.75 全部保持 v180 的值）—— 只换曲线的振幅 ⇒ 三个目标同时落进目标带，
        /// 且第 1 分钟与改前逐位相同（曲线起点恒为 1.0）✓
        /// </summary>
        public void DmgCurveCoefficients(out float a, out float b)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            a = g != null && g.dmgStepA > 0f ? g.dmgStepA : 0.08f;
            b = g != null && g.dmgStepB > 0f ? g.dmgStepB : 0.003f;
            if (UsesBossDmgCurve)
            {
                a = g.bossDmgStepA;
                b = Mathf.Max(0f, g.bossDmgStepB);
            }
        }

        /// <summary>生命 = 基础血 × 波次曲线；BOSS 额外吃 bossIndex 加成。精英（Jumper）与 BOSS 各走自己的振幅。</summary>
        /// <remarks>
        /// ★ v252：最后再乘**难度倍率** <see cref="RunDifficulty.HpMult"/> ★
        ///   （用户原话：「中：敌人基础血量加 20% … 高：加 50%」）
        ///   · 低档 = **1.0** ⇒ 与加这个功能之前逐位相同（既有自检/回放/基准全部跑在低档）✓
        ///   · 乘在**出口**而不是逐条曲线上：三条 HP 曲线（标准 / 精英 / BOSS）自动一起吃，
        ///     不会出现"某条曲线忘了乘难度"的分叉 ✓
        /// </remarks>
        public float HpForWave(int wave, int bossIndex)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            // ★ v190：系数改由 `HpCurveCoefficients` 单点给出（含精英 / BOSS 分支）★
            //   改前是就地读两行 `g.hpStepA/B` —— 那样加第二条曲线就会出现"两个地方各判一次" ✗
            HpCurveCoefficients(out float a, out float b);
            float bossGrowth = g != null ? g.bossHpGrowth : 0.10f;
            float mult = WaveGrowth(wave, a, b);
            // ★ v252：难度（低档恒 1.0）★
            float diff = RunDifficulty.HpMult;
            if (Kind == EnemyKind.Boss)
                return BaseHp * mult * (1f + bossGrowth * (Mathf.Max(1, bossIndex) - 1f)) * Post20Factor(wave) * diff;
            // ★ v191：非 BOSS 再乘前段压低因子（BOSS 会在上面提前返回 ⇒ 它的数值一个字节没动）★
            // ★ v194：两条分支都再乘 20 分钟后的加速因子（第 20 分钟及以前精确 1.0，
            //   所以上面那句"BOSS 一个字节没动"对 t ≤ 19 仍然成立 ✓）
            return BaseHp * mult * EarlyNerfFactor(wave) * Post20Factor(wave) * diff;
        }

        /// <summary>
        /// v115：伤害走波次曲线，但系数低于 HP（HP 与伤害刻意不同步，见 hpStep 的说明）。
        /// 接触伤害 / 弹幕伤害 / 跳砸伤害 / BOSS 冲撞全部走这里。
        /// ★ v190：系数由 `DmgCurveCoefficients` 单点给出（BOSS 走自己的振幅）★
        /// ★ v191：非 BOSS 再乘前段压低因子（与 HP 侧同一个函数、同一套窗口）★
        /// </summary>
        public float DamageForWave(float baseDamage, int wave)
        {
            DmgCurveCoefficients(out float a, out float b);
            float mult = WaveGrowth(wave, a, b);
            if (Kind != EnemyKind.Boss) mult *= EarlyNerfFactor(wave);
            // ★ v194：20 分钟后的额外加速（第 20 分钟及以前精确 1.0 ⇒ 既有读数不变）★
            // ★ v252：难度（三招都走这一个出口 ⇒ 中/高档连弹幕与跳砸一起加，低档恒 1.0）★
            return baseDamage * mult * Post20Factor(wave) * RunDifficulty.DmgMult;
        }

        public float TouchDamageForWave(int wave) => DamageForWave(TouchDamage, wave);
        public float ProjectileDamageForWave(int wave) => DamageForWave(ProjectileDamage, wave);
        public float JumpDamageForWave(int wave) => DamageForWave(JumpDamage, wave);

        // ════════════════════════════════════════════════════════════════════
        // ★★ v192：**敌人对玩家的伤害 → 整数血点**（用户需求②）★★
        //
        // ── 上面三个 `…ForWave` 为什么**不改** ——
        // 它们是"曲线读数"：探针（`ProbeV190.ProbeCurve` / `DeathDiagnostics`）与自检的
        // 差分断言都在读它们，而那条曲线的形状（第 20 分钟 6 / 4 / 8）是上一轮解出来的硬目标 ✓
        // 若在曲线层就取整，第 1 分钟的 0.455 会变成 0（打不动玩家）✗ 第 20 分钟的 6.00 也会丢掉精度 ✓
        // ⇒ **曲线保持小数，换算发生在"打进血池"的那一刻**（本文件的 `PlayerDamagePoints`
        //   与 `DamageResolver.Resolve`）✓
        //
        // ── 换算 ──
        //   ★ v194：`hpUnit` **保持 10**（玩家血池 100/20 本轮没动）★
        //   血点 = Round(曲线值 × hpUnit)
        //   改前（v193，表里是**旧刻度** 0.7 ⇒ 曲线值 0.455）：
        //       第 1 分钟基础兵 Round(0.455 × 10) = **5** 血点（玩家血 100 ⇒ 5%）✓
        //   改后（v194，表里是 **×10 后的 base** 7.0 ⇒ 曲线值 4.5499997）：
        //       第 1 分钟基础兵 Round(4.5499997 × 10) = **45** 血点 ⇒ 45% 血
        //   ★ 注意是 45 不是 46：`7.0f × 0.65f` 在 float 上落在 4.55 的**下方**，
        //     乘 10 得 45.499997，`RoundToInt` 于是落到 45 ✓（探针实测，别按十进制口算）
        //   ★★ 这个"双算"是**用户裁决过的**（原话："玩家生命值100，护盾20；敌人伤害也要10倍"）
        //      ⇒ 不许为了平衡去改 `hpUnit` 或压曲线；它是一条**设计决定**，不是 bug ✓
        //      三条已知后果：护盾 20 被一击打穿（退化成一次性吸收）、
        //      BOSS 跳砸 175 一下秒杀 100 血玩家、第 4 分钟起小兵触碰 103 点即一下致死 ✓
        //   第 1/5/10/15/20/25/30 分钟的六个整数血点见探针 `ProbeV194Num` ✓
        //   ★ `≤ 0` 的输入返回 **0**（不是 1）★ —— `pig.touchDamage = 0` 这种"本来就不打人"
        //   的敌人不许因为取整反而获得伤害 ✓
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 玩家血池与敌人伤害共用的**整数单位**（唯一读取点；`≤ 0` 回落 10）。
        /// 见 `PlayerBaseDef.hpUnit` 的说明。
        /// </summary>
        public static float PlayerHpUnit
        {
            get
            {
                TuningDef t = TableDatabase.Tuning;
                float u = t != null && t.player != null ? t.player.hpUnit : 0f;
                return u > 0f ? u : 10f;
            }
        }

        /// <summary>曲线伤害（小数）→ **打到玩家身上的整数血点**（0 伤害仍是 0）。</summary>
        public static int PlayerDamagePoints(float curved)
        {
            if (curved <= 0f) return 0;
            return Mathf.Max(1, Mathf.RoundToInt(curved * PlayerHpUnit));
        }

        /// <summary>该敌人三招对玩家的**整数伤害**（探针/自检/诊断读它，别自己再算一份）。</summary>
        public int TouchDamagePoints(int wave) => PlayerDamagePoints(TouchDamageForWave(wave));
        /// <inheritdoc cref="TouchDamagePoints"/>
        public int ProjectileDamagePoints(int wave) => PlayerDamagePoints(ProjectileDamageForWave(wave));
        /// <inheritdoc cref="TouchDamagePoints"/>
        public int JumpDamagePoints(int wave) => PlayerDamagePoints(JumpDamageForWave(wave));
    }

    public sealed class WaveBatchDef
    {
        public string EnemyId;
        public int Count;
        public float StartAt, Interval;
    }

    public sealed class WaveDef
    {
        public int Index;
        // v165：`Duration` 已删除（连同 WaveDefRaw.duration / waves.json 的 duration 一起）✗
        // 它同时驱动三件事，而这三件事现在都由**时钟**决定：
        //   ① 本回合何时结束      → WaveDirector.IsComplete 用 Clock.SecondsPerMinute
        //   ② 两条兜底清场的触发点 → RunSession.TickCombat 用 Clock.SecondsPerMinute
        //   ③ 顶部"剩余 Ns"倒计时  → 删除（顶部已有 mm:ss 计时器与 BOSS 倒计时）
        public bool IsBoss;
        public string Tip;
        public WaveBatchDef[] Batches = Array.Empty<WaveBatchDef>();
    }

    /// <summary>
    /// **运行时的一条道具数值效果**（已解析过枚举与模式）。
    ///
    /// `Scaled` 是**成长型道具**的唯一算法入口：`数值 = add + growPerWave × (波次 − 1)`，
    /// ★ v173：这里原写"（回合 − 1）"✗ —— 而"回合"这个词在本工程里【已经不存在了】✓
    /// 底层是 `WaveIndex` ✗ 而在计时制下它【每分钟 +1】✓ ⇒ 所以叫"波次"✓
    /// （★ 而字段名 `growPerWave` 本来就是 wave ✓ 日志里报的也是"派生波"✓ ——
    ///   所以"回合"是【外来词残留】✗ 不是另有一套刻度 ✓）
    /// Add 类直接返回点数、Percent 类返回 `1 + n/100` 的乘数 ✓
    /// 两者都放这里 ⇒ "成长"只有一个实现，而不是"应用时一处、显示时又一处" ✗
    /// </summary>
    public sealed class ItemBonus
    {
        public StatId Stat;
        public float Add;
        public bool IsPercent;

        /// <summary>把本效果换算成可直接写进 `StatBlock` 的值（含成长）。</summary>
        public float Scaled(float growPerWave, int wave)
        {
            float v = Add + growPerWave * Mathf.Max(0, wave - 1);
            return IsPercent ? 1f + v / 100f : v;
        }

        /// <summary>显示用（不含成长）：`+3` 或 `+15%`。</summary>
        public string Label => IsPercent ? $"+{Add:0.#}%" : $"+{Add:0.#}";
    }

    /// <summary>
    /// **道具的运行时定义**（v168 重构：三类别）。
    ///
    /// ★ 为什么 `Stat` / `Add` / `IsPercent` 是**只读属性**而不是字段 ★
    /// `ImGuiHud` 仍然按旧接口读这三个成员（本任务禁止改那个文件）✗
    /// 而新表的主增益住在 `Effects[0]` 里 ⇒ 如果这里再存一份字段，
    /// 就出现了**两份真相**：改了表、忘了同步，UI 会显示一个界面上的假数值 ✗
    /// （本工程已有过一次"UI 自己另写一份名字表 ⇒ 传奇被画成紫色"的教训 ✓）
    /// 所以它们**派生自 `Effects[0]`**，能编译、能显示，且不可能漂移 ✓
    /// </summary>
    public sealed class ItemDef
    {
        public string Id, Name, Desc;
        /// <summary>道具等级（v9）：D/C/B/A/S，影响刷新率与价格。也是品质档（0 普通 … 4 传奇）。</summary>
        public SeedGrade Grade;
        /// <summary>v36：稀有度档位（0 普通 … 4 传奇）。</summary>
        public int RarityTier => Rarity.TierOf(Grade);

        /// <summary>★ 1 = 纯数值增加｜2 = 有牺牲的增加｜3 = 机制拓展 ★</summary>
        public int Category;
        /// <summary>全部增益（顺序有意义：`Effects[0]` 是主增益）。</summary>
        public ItemBonus[] Effects = Array.Empty<ItemBonus>();
        /// <summary>全部减益（只有类别 2 非空）。</summary>
        public ItemBonus[] Costs = Array.Empty<ItemBonus>();
        /// <summary>机制键（空 = 无机制）。取值必须来自 `ItemRules`。</summary>
        public string Rule = "";
        public float RuleValue;
        /// <summary>每回合的增益成长量（0 = 不成长）。</summary>
        public float GrowPerWave;

        public int Price, MaxStack;

        /// <summary>
        /// ★ v314：**这件道具不进任何随机池**（表字段 `noDrop` 的运行时镜像）——
        /// 唯一消费点是 `TableDatabase.RollableItems`（见那个属性的注释）。
        /// </summary>
        public bool NoDrop;

        // ══════════════════════════════════════════════════════════════════
        // ★★ v198：**"无限"的哨兵** —— `maxStack` 是 `int`，表里没有"无限"这种类型 ★★
        //
        // 用户原话："道具的 4，5，6 上限修改为无限"。
        // 而 `items.json` 的 `maxStack` 是 `int` ⇒ 加不了 `Infinity`、也加不了 `null` ✓
        // 两种做法里选了**哨兵大数**（而不是"0 = 无限"或"负 = 无限"）：
        //   · `0` / `−1` 这种语义在**既有校验里已经有含义**（`Validate` 直接报
        //     "maxStack 必须 > 0"；`Mathf.Max(1, maxStack)` 也会把 0 兜成 1）✗
        //     写成 0 的话：表校验先炸，改校验又要动 4 处 `Mathf.Max(1, …)` 兜底 ⇒ 面太大 ✓
        //   · 哨兵大数**不需要改任何既有逻辑**：`TryBuy` 的 `n >= MaxStack` 天然永不成立
        //     ⇒ 语义立刻变成"买不完"，正是"无限" ✓
        //
        // ★ 判据只有这一处 ★ `IsUnlimitedStack` 是唯一实现；
        //   UI（`ImGuiHud.MaxStackText`）、自检、探针都读它 ——
        //   各写一份 `>= 9999` 就会出现"面板显示 ∞、商店却说上限 9999"的分歧 ✗
        //   （本工程已有过"UI 自己另写一份名字表 ⇒ 传奇被画成紫色"的教训）
        //
        // ⚠️ 它**不是运行时钳位**：谁真的能买到 9999 件，由金币（`Price` × 件数）兜着。
        //    哨兵只表达"上限不设"这个语义 ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>表示"持有上限无限"的哨兵值（唯一出处）。</summary>
        public const int UnlimitedStack = 9999;

        /// <summary>某个 `maxStack` 数值是否等于"无限"（**判据唯一实现**）。</summary>
        public static bool IsUnlimitedStack(int maxStack) => maxStack >= UnlimitedStack;

        /// <summary>这件道具的持有上限是否表示"无限"。</summary>
        public bool IsUnlimited => IsUnlimitedStack(MaxStack);

        // ── v168：`ImGuiHud` 读的三个旧成员（派生自主增益，不另存一份）──
        public StatId Stat => Effects.Length > 0 ? Effects[0].Stat : StatId.MaxHp;
        public float Add => Effects.Length > 0 ? Effects[0].Add : 0f;
        public bool IsPercent => Effects.Length > 0 && Effects[0].IsPercent;

        /// <summary>这件道具是不是"机制类"（有 rule 且登记过）。</summary>
        public bool HasRule => !string.IsNullOrEmpty(Rule) && ItemRules.IsKnown(Rule);
    }

    /// <summary>
    /// ★ **道具"机制拓展"的解释器 —— 唯一的一处** ★
    ///
    /// 新标准第三类要求"机制拓展（不要动全局玩法内容，只改局部规则）"✓
    /// 而"表里写了 rule，谁去读它"是本任务最容易做假的地方 ✗ ——
    /// 写 8 条只有数据、没人读的规则，看起来内容丰富，实际全是死的 ✓
    ///
    /// 所以这里**只登记真的接好线的那几条**，每条都注明它的**消费点**：
    ///
    /// | 规则键              | 含义（局部规则）                | 消费点（真接线处）                        |
    /// |---------------------|---------------------------------|-------------------------------------------|
    /// | `reroll_discount`   | 商店刷新费 −N 金币/层           | `ShopSystem.RerollCost`（HUD 也读它 ⇒ 显示一致）|
    /// | `harvest_gold_pct`  | 收获金币 +N%/层                 | `World.HarvestPlot` 的金币结算            |
    /// | `harvest_dew`       | 每次收获返还 N 点蓄水/层        | `World.HarvestPlot`（在 `plot.Harvest` 前）|
    ///
    /// ★ 加新规则的正确姿势 ★
    /// ① 在这里登记；② 在**上表写明的消费点**读 `World.Harvest*` / `Shop.RerollDiscount`
    /// 之类的**已算好的系数**（不要在消费点里再遍历一次道具表 ✗ 那是第二份实现）。
    /// ③ 登了却没接消费点 = 死机制 ✗ `SelfCheck` 的 `V168ItemChecks` 会对每条规则验一次实际行为。
    /// </summary>
    public static class ItemRules
    {
        public const string RerollDiscount = "reroll_discount";
        public const string HarvestGoldPct = "harvest_gold_pct";
        public const string HarvestDew = "harvest_dew";

        /// <summary>
        /// ★★ v314：**「天使降临」的复活规则**（用户原话：「获得时死亡会暂停时间 2 秒，天使羽毛飘落，
        /// 并完成一次复活，恢复所有血量，时间恢复正常，然后物品失效，依然占用位置，
        /// 替换为一个新的道具「陨落天使的羽毛」」）★★
        ///
        /// ── 为什么它有 rule，却没有 `SyncItemRules` 的消费点 ──
        ///   本工程的 14 条既有规则全是**系数**（写成 `World.Harvest*` / `Shop.RerollDiscount`
        ///   之类的字段，每回合重算一次）。这一条不是系数，是**事件**：
        ///   它只在"玩家血池见底"的那一刻被问一次 ⇒ 消费点是
        ///   `RunSession.TryAngelRevive()`（`TickCombat` 判负之前那一格）。
        ///   `ruleValue` = 停帧秒数（表里 2）。
        ///
        /// ⚠️ 登记在这里是**必须**的，不是形式主义：`TableDatabase.Validate` 会拒绝
        ///    "表里写了 rule、但 `ItemRules.All` 里没有"的行（那等于一件看着有机制、
        ///    实际没人读的死道具）✓ 所以"接线"与"登记"是同一件事的两半。
        /// </summary>
        public const string AngelRevive = "angel_revive";

        // ── v197：**10 件机制道具的规则键**（键名常量在 `Prog/ProgMechanics.cs` 的 `ProgRules`）──
        // 这里只做"登记"这件事：`Validate` 与自检都问 `IsKnown`，所以**登记表必须含它们** ✓
        // ★ 为什么键名不重复写在这里 ★ —— 两份字面量一定会漂移；
        //   本表引用 `ProgRules.*`，值的唯一来源留在 `ProgMechanics` ✓
        public static readonly string[] All =
        {
            RerollDiscount, HarvestGoldPct, HarvestDew,
            ProgRules.CarrotOrbit, ProgRules.RabbitPet, ProgRules.DodgeCapUp, ProgRules.WaterChargeUp,
            ProgRules.AtkSpeedRamp, ProgRules.DamageRamp, ProgRules.BlockShield, ProgRules.CritTrade,
            ProgRules.SharpWater, ProgRules.GoldDamage,
            // ★ v314：11 → 12 条（新增复活规则；`SelfCheck` 的 `ITEM-NO-RULE` 里
            //   "恰好 10 件带 rule"那个期望值随之变成 11，见交付报告 ⑨ —— 那条断言是**如实报警**的，
            //   本轮**没有**改它 ✓）
            AngelRevive
        };

        public static bool IsKnown(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i], key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>规则的中文说明（商店/详情页的文案来源，也是自检的报告口径）。</summary>
        public static string Describe(string key)
        {
            if (string.Equals(key, RerollDiscount, StringComparison.OrdinalIgnoreCase)) return "商店刷新费 −N 金币";
            if (string.Equals(key, HarvestGoldPct, StringComparison.OrdinalIgnoreCase)) return "收获金币 +N%";
            if (string.Equals(key, HarvestDew, StringComparison.OrdinalIgnoreCase)) return "收获返还 N 点蓄水";
            // ── v197 ──
            if (string.Equals(key, ProgRules.CarrotOrbit, StringComparison.OrdinalIgnoreCase)) return "胡萝卜环绕玩家，挡下敌人飞行物";
            if (string.Equals(key, ProgRules.RabbitPet, StringComparison.OrdinalIgnoreCase)) return "兔子宠物：追最近目标，伤害 = 玩家最大生命 10%";
            if (string.Equals(key, ProgRules.DodgeCapUp, StringComparison.OrdinalIgnoreCase)) return "戴着它时闪避上限变 70%";
            if (string.Equals(key, ProgRules.WaterChargeUp, StringComparison.OrdinalIgnoreCase)) return "水壶充能上限 +N";
            if (string.Equals(key, ProgRules.AtkSpeedRamp, StringComparison.OrdinalIgnoreCase)) return "每秒 +N% 攻击速度，受伤重置";
            if (string.Equals(key, ProgRules.DamageRamp, StringComparison.OrdinalIgnoreCase)) return "每秒 +N% 伤害倍率，受伤重置";
            if (string.Equals(key, ProgRules.BlockShield, StringComparison.OrdinalIgnoreCase)) return "获得盾牌：挡下一次伤害，之后 N 秒冷却";
            // ★ v212 改口径：这句话原来写的是「攻速上限改为 −200%」—— 那是 v197 的行为，
            //   v212 起 `ApplyCapsToTable` **不再改写上限**（那会让别的攻速增益全部失效），
            //   变成 `items.json` 里真实的一条 `Percent AtkSpeed −200`（真减速、不锁上限）⇒
            //   文案必须跟着改，否则 Describe() 一旦被接线到 UI/自检就是"代码在撒谎" ✓
            // ★ v3xx 再改口径（用户原话：「修改赌命手套效果，改为移动速度降低 9.9%，攻速降低 9.9%，
            //   暴击率上涨 9.9%，暴击伤害增加 99%」）★
            //   ⇒ 攻速 −200% 那一半已经不存在，新数值是"移速/攻速各 −9.9% + 暴击率 +9.9 个百分点
            //     + 暴击伤害 +99%"。**这一句必须与 `items.json` 里 gamble_glove 的 effects 一一对应**，
            //     否则 `ItemRules.Describe(CritTrade)` 会继续描述一件已经不存在的道具 ✗
            if (string.Equals(key, ProgRules.CritTrade, StringComparison.OrdinalIgnoreCase)) return "暴击率 +9.9 个百分点、暴击伤害 +99%，代价是移速与攻速各 −9.9%";
            if (string.Equals(key, ProgRules.SharpWater, StringComparison.OrdinalIgnoreCase)) return "水壶的远程攻击也能收割成熟作物";
            if (string.Equals(key, ProgRules.GoldDamage, StringComparison.OrdinalIgnoreCase)) return "每次攻击把 1% 金币取整转为额外伤害";
            // ★ v314：复活规则。`N` 由调用方换成表里的 `ruleValue`（停帧秒数）
            if (string.Equals(key, AngelRevive, StringComparison.OrdinalIgnoreCase)) return "死亡时停帧 N 秒、羽毛飘落后复活一次并回满生命，这件随即变成「陨落天使的羽毛」";
            return "";
        }
    }

    public static class DefParser
    {
        public static T ParseEnum<T>(string s, T fallback) where T : struct
        {
            if (!string.IsNullOrEmpty(s) && Enum.TryParse(s, true, out T v)) return v;
            return fallback;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : fallback;
        }
    }
}
