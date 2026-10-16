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
        // v162：爆炸倍率 / 爆炸范围**上移进主要属性区**（A2 分组调整）。
        //       编号因此从 4/5 变成 6/7 —— 安全，理由同下。
        ExplodeDamage = 6, ExplodeRange = 7,
        CritRate = 8, CritDmg = 9, AtkSpeed = 10, AtkRange = 11, MoveSpeed = 12,
        Dodge = 13, MaxPlots = 14, Knockback = 15,
        // v107：删除 Luck（需求）。
        // ★ 删中间成员会让后面所有编号前移 ★
        // 这里是安全的：属性**不按编号持久化**——种子/道具表里引用属性用的是**名字**
        // （如 "stat": "MeleeDamage"），StatIds.All 又是 Enum.GetValues 动态生成的，
        // 所以前移不会打乱任何已存数据。若将来有按编号存盘的地方，这里就要改成留空占位。
        HeartDropRate = 16, HeartHeal = 17, ExpMult = 18, GoldMult = 19
    }

    public static class StatIds
    {
        /// <summary>
        /// 属性总数。**必须与 StatId 的成员个数逐一相符** ——
        /// StatBlock 用它开数组（`new float[StatIds.Count]`）并用它做裁剪循环，
        /// 少一个会让最后那几个属性**静默失去上限裁剪**（不报错、不抛异常，只是偶尔算出超界的值）。
        /// 自检里有 `StatIds.All.Length == StatIds.Count` 一条专门盯它。
        /// v162：18 → 20（删除 AtkDamage 1 个、新增三种伤害类型 3 个）。
        /// </summary>
        public const int Count = 20;
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
        public float dmgStepA = 0.08f, dmgStepB = 0.003f;
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
        /// <summary>Common / Uncommon / Rare / Epic / Legendary</summary>
        public string quality;
        /// <summary>适用的武器：0 锄头 / 1 水壶 / 2 肥料。**空数组 = 任意武器** ✓</summary>
        public int[] allowedWeapons = Array.Empty<int>();
        /// <summary>DamageFlat / DamagePercent / AttackSpeed / Range / CritRate / CritDamage / ExplodeRadius / ProjectileCount / Knockback / SwordWave / ChillOnHit / AmmoOnKill / BurnOnHit / ChainOnHit / BlastEcho / CritBlast / FullHpBonus</summary>
        public string effect;
        public float amount;
        /// <summary>v171：第二效果参数。目前只有 ChainOnHit 用（跳数用 amount、每跳比例用 amount2）。</summary>
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
        public float maxHp, shield, meleeDamage, rangedDamage, blastDamage, atkMult, explodeDamage, explodeRange, critRate, critDmg,
                     atkSpeed, atkRange, moveSpeed, dodge, knockback, expMult, goldMult;
        public int maxPlots;
        /// <summary>v48：开局发放的金币（第一回合开始前就能进商店买东西）。</summary>
        public int startGold;
        /// <summary>受击后的无敌时间（秒）。</summary>
        /// <summary>幸运：影响道具刷新（默认 0）。</summary>
        public float luck;
        /// <summary>爱心掉落率与每个爱心的回血量。</summary>
        public float heartDropRate, heartHeal;
        public float invulnTime;
        /// <summary>未受伤时每秒恢复的最大护盾比例。</summary>
        public float shieldRegenPct;
        /// <summary>护盾每秒恢复量的下限。</summary>
        public float shieldRegenMin;
        /// <summary>受击后延迟多久才开始回护盾（0 = 字面语义：不受伤就回）。</summary>
        public float shieldRegenDelay;
    }

    [Serializable] public class GrowthDef
    {
        public float hpPerLevel, shieldPerLevel, expBase, expLinear, expExponent;
        public int levelsPerPlot;
    }

    [Serializable] public class CapsDef
    {
        public float dodge, critRate, atkSpeed, atkRange, moveSpeed, knockback;
        public float explodeDamage, explodeRange;
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
        /// <summary>同时操控的耕地上限（1 + 品质，最大 4）。</summary>
        public int workCapMax;
        /// <summary>水壶/肥料每档品质额外的生长减免。</summary>
        public float waterBonusPerTier, fertBonusPerTier;
        public ToolDef hoe = new ToolDef();
        public ToolDef can = new ToolDef();
        public ToolDef fertilizer = new ToolDef();
    }

    [Serializable] public class GradeWeightDef { public string grade; public int weight; }

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
        public GradeWeightDef[] gradeWeights = Array.Empty<GradeWeightDef>();
        /// <summary>v9：道具等级权重（随波次与"幸运"倾斜到高等级）。</summary>
        public GradeWeightDef[] itemGradeWeights = Array.Empty<GradeWeightDef>();
        /// <summary>每高一级的价格倍率、每波的价格增长、幸运对刷新率的加成。</summary>
        public float itemGradePriceStep, priceWaveStep, luckRefreshBonus;

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
        /// </summary>
        public float WavePriceMult(int wave)
            => Mathf.Pow(1f + Mathf.Max(0f, priceWaveStep), Mathf.Max(0, wave - 1));
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

        /// <summary>v37：按稀有度定价（低档便宜、高档显著更贵）。</summary>
        public float PriceMult(int tier)
        {
            switch (Mathf.Clamp(tier, 0, 4))
            {
                case 0: return 1f;
                case 1: return 1.8f;
                case 2: return 3.2f;
                case 3: return 5.6f;
                default: return 9f;
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
        /// <summary>v47：每次刷怪拍出现小猪的概率（0.05 = 5%）。BOSS 波不刷怪，天然不会出小猪。</summary>
        public float pigChance = 0.05f;
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
        public int Price;
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

        /// <summary>生命 = 基础血 × 波次曲线；BOSS 额外吃 bossIndex 加成。</summary>
        public float HpForWave(int wave, int bossIndex)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            float a = g != null && g.hpStepA > 0f ? g.hpStepA : 0.12f;
            float b = g != null && g.hpStepB > 0f ? g.hpStepB : 0.006f;
            float bossGrowth = g != null ? g.bossHpGrowth : 0.10f;
            float mult = WaveGrowth(wave, a, b);
            if (Kind == EnemyKind.Boss)
                return BaseHp * mult * (1f + bossGrowth * (Mathf.Max(1, bossIndex) - 1f));
            return BaseHp * mult;
        }

        /// <summary>
        /// v115：伤害走**同一条曲线**，但系数更低（HP 与伤害刻意不同步，见 hpStep 的说明）。
        /// 接触伤害 / 弹幕伤害 / 跳砸伤害 / BOSS 冲撞全部走这里。
        /// </summary>
        public float DamageForWave(float baseDamage, int wave)
        {
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            float a = g != null && g.dmgStepA > 0f ? g.dmgStepA : 0.08f;
            float b = g != null && g.dmgStepB > 0f ? g.dmgStepB : 0.003f;
            return baseDamage * WaveGrowth(wave, a, b);
        }

        public float TouchDamageForWave(int wave) => DamageForWave(TouchDamage, wave);
        public float ProjectileDamageForWave(int wave) => DamageForWave(ProjectileDamage, wave);
        public float JumpDamageForWave(int wave) => DamageForWave(JumpDamage, wave);
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

        /// <summary>已登记的全部规则键（自检用它验"表里的 rule 都有人解释"）。</summary>
        public static readonly string[] All = { RerollDiscount, HarvestGoldPct, HarvestDew };

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
