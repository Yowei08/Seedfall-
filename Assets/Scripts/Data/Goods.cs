using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    // ═══════════════════════════════════════════════════════════════════════
    //  阶段 1：三类商品的【静态定义】与【运行时实例】
    //
    //  ★ 为什么用 ScriptableObject，而且为什么不迁移旧的 5 张表 ★
    //
    //  规格要求"优先使用 ScriptableObject 保存静态配置，
    //  避免将玩家运行时状态写入共享配置资产" ✓
    //
    //  审查发现：旧表（seeds/items/enemies/waves/tuning 五份 JSON）**已经做到了
    //  静态与运行时分离** ✓ 只是载体是 JSON 而不是 SO ✗
    //  把它们迁到 SO 会让 319 条自检断言大面积失效 ✗ 而它们工作正常 ✓
    //
    //  所以本文件只承载**新增的**三类商品 ✓ 旧表保持不动 ✓
    //  两者互不干扰：旧表走 TableDatabase（JSON），新商品走 SO 资产 ✓
    //
    //  ★ 静态 / 运行时 的界线（本文件的核心纪律）★
    //
    //     SeedAsset / PluginAsset / ItemAsset      ← 静态。只读。SO 资产。**绝不在运行时写入** ✗
    //     SeedInstance / PluginInstance / ItemInstance ← 运行时。普通 C# 类。每局新建 ✓
    //
    //  这条界线如果破了，症状是"重开一局后品质变了 / 等级没重置" ✗
    //  而且极难排查 —— 因为 SO 是**共享资产**，一局的修改会带到下一局 ✓
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 商品品质。**沿用工程既有的五档**（`Rarity.Names`：普通/一般/稀有/史诗/传奇）✗
    ///
    /// 为什么不新建一套：`Rarity.ColorOf` / `NameOf` / 商店的 `PriceMult` /
    /// 苗的 `GradeColor` 全都基于这个五档制 ✓
    /// 新建一套会出现"同一个品质在两个地方有两个名字" ✗ —— 本会话已经吃过一次
    /// （稀有度配色 bug：颜色一直对，错的是**第二份名字表**）✓
    /// </summary>
    public enum GoodsQuality { Common = 0, Uncommon = 1, Rare = 2, Epic = 3, Legendary = 4 }

    /// <summary>
    /// **插件适用的武器**。取值与 `RunSession.ToolHoe/Can/Fertilizer` 对齐 ✗
    ///
    /// 对齐的理由：武器索引在 `Actors.cs` 的 `WeaponHoe=0 / WeaponCan=1 / WeaponFertilizer=2`
    /// 已经定死了 ✓ 插件若要"安装到指定武器"，就必须用**同一套索引** ✗
    /// 另起一套枚举会造出第二份映射 ✓
    /// </summary>
    public enum WeaponSlot { None = -1, Hoe = 0, Can = 1, Fertilizer = 2 }

    /// <summary>
    /// 插件修改攻击行为的**方式**。用枚举分派，避免为每个插件写一个类 ✗
    ///
    /// ★ 每一档都【必须】被攻击路径真的消费 ★（本工程最贵的一课：看着活着、实际死的）
    /// 所以枚举按"它改的是攻击的哪一环"分组，并且每条都在
    /// `WeaponSystem.Aggregate` 里有分支、在 `RunSession` 的三条攻击路径里被读 ✓
    /// 自检里有一条"每个枚举值都被消费"的断言来钉住这条纪律。
    ///
    /// ★ 单位约定（踩过的坑，写在这里免得下次再踩）★
    /// `CritRate` / `CritDamage` / `Dodge` 这类"率"在这一局里是**0..1 的小数**，
    /// 不是百分数：`tuning.player.critRate = 0`、`caps.critRate = 1.0`，
    /// 而消费点是 `World.Rng.Chance(st.Get(StatId.CritRate))` ✓
    /// ⇒ 插件写 `amount: 8` 会得到 **800% 暴击率**（`Chance(8)` 恒真），必须写 `0.08` ✓
    /// 而 `AttackSpeed` / `Range` / `DamagePercent` / `ExplodeRadius` 是**倍率增量**（0.15 = +15%）✓
    /// </summary>
    public enum PluginEffectKind
    {
        None = 0,

        // ── ② 对武器的**数值**进行操作（9 种，改的是攻击公式的某一个乘区）──
        DamageFlat,        // 伤害 +N（加到 AttackFormula 的 baseValue）
        DamagePercent,     // 伤害 ×(1+N)
        AttackSpeed,       // 攻速 +N（0.15 = 挥击间隔 ×1/1.15）
        Range,             // 范围 +N%（近战判定框 / 激光射程 / 爆炸半径）
        CritRate,          // 暴击率 +N（**0..1 小数**，0.10 = +10%）
        CritDamage,        // 暴击伤害 +N（倍率增量，0.5 = 暴击倍率 +50%）
        ExplodeRadius,     // 爆炸范围 +N%（只对肥料那条路有判定）
        ProjectileCount,   // **弹道数 +N**（按武器解释：剑气条数 / 激光条数 / 爆炸点数）
        Knockback,         // 击退 +N（加到 DamageRequest.Knockback）

        // ── ① 对武器的**攻击形式**进行拓展（1 种）──
        /// <summary>剑气：近战挥击**同时**沿瞄准方向发一道剑气，打到近战判定框之外的敌人。
        /// `amount` = 剑气基础条数（≥1 即启用），实际条数 = round(amount) + ProjectileCount。</summary>
        SwordWave,

        // ── ③ 小机制（2 种）──
        /// <summary>命中霜冻：每次命中额外叠 `amount` 层寒冷（既有的减速/冻结体系）。</summary>
        ChillOnHit,
        /// <summary>击杀回弹药：击杀一个敌人回 `amount` 点弹药（水壶/肥料各自的弹药池）。</summary>
        AmmoOnKill,

        // ── ④ 大机制（3 种）──
        /// <summary>命中灼烧：命中即让目标进入既有的灼烧（`StatusRuntime.Burn`）。
        /// `amount` = 灼烧量占本次伤害的比例（0.35 = 本次伤害的 35%）。</summary>
        BurnOnHit,
        /// <summary>共振连锁：命中后向最近的 `amount` 个其它敌人溅射 `amount2` 比例的伤害
        /// （与星藤植物的 `Chain` 是两套，植物的写死在 Actors.cs，这里的走插件数据）。</summary>
        ChainOnHit,
        /// <summary>爆炸回声：肥料爆炸后**原地再结算一次**，`amount` = 第二段的伤害比例。</summary>
        BlastEcho,

        // ── ⑤ 特殊效果（2 种）──
        /// <summary>暴击炸裂：这一击暴击时，在命中点引发一次爆炸，`amount` = 溅射伤害比例。</summary>
        CritBlast,
        /// <summary>满血增伤：生命满值时这一击的伤害倍率 ×(1+`amount`)。</summary>
        FullHpBonus,
    }

    /// <summary>道具修改的**作用域**。全局规则与玩家属性分开 ✗（两者的叠加规则不同）</summary>
    public enum ItemScope
    {
        PlayerStat = 0,    // 改 StatBlock 里的某个属性
        GlobalRule = 1,    // 改全局规则（如"商店刷新更便宜""爱心掉落保底"）
    }

    /// <summary>叠加规则。规格要求"支持必要的叠加限制" ✓</summary>
    public enum StackRule
    {
        /// <summary>可叠加到 <c>maxStack</c> 层。</summary>
        Stackable = 0,
        /// <summary>唯一 —— 已有则不能再买（如"多一条命"这类）✗</summary>
        Unique = 1,
        /// <summary>可叠加但**乘算**（1.2 × 1.2），用于倍率类 ✗</summary>
        Multiplicative = 2,
    }

    // ─────────────────────────── 静态定义（SO）───────────────────────────

    /// <summary>
    /// **种子的静态定义**（SO 资产）。
    ///
    /// ★ 与既有 `SeedDef` 的关系 ★
    /// `SeedDef` 是旧表（`seeds.json`）里的定义 ✗ 仍在用 ✓
    /// 本类是**新版商品体系**的种子定义 ✓ 两者并存，由商店决定卖哪一套 ✓
    /// 这样阶段 2 可以逐个品种迁移，而不是"一次全换" ✗
    /// </summary>
    [CreateAssetMenu(fileName = "SeedAsset", menuName = "Seedfall/商品/种子")]
    public sealed class SeedAsset : ScriptableObject
    {
        [Header("身份")]
        public string id = "";
        public string displayName = "";
        public GoodsQuality quality = GoodsQuality.Common;

        [Header("成长条件（规格：不同种子具有不同成长条件）")]
        [Tooltip("成熟所需的水分总量")]
        public float waterToMature = 100f;
        [Tooltip("每次浇水提供的水分")]
        public float waterPerPour = 25f;
        [Tooltip("从种下到成熟的基础秒数（不含浇水加速）")]
        public float baseGrowSeconds = 60f;

        [Header("收割收益（规格：不同收割收益）")]
        public int goldHarvest = 10;
        public int expHarvest = 5;

        [Header("升级规则（规格：具有升级等级与升级规则）")]
        [Tooltip("可升到的最高等级。0 = 不可升级")]
        public int maxLevel = 3;
        [Tooltip("每级的水分需求增量")]
        public float waterPerLevel = 40f;
        [Tooltip("每级的收益倍率增量")]
        public float yieldPerLevel = 0.5f;

        [Header("特殊效果（规格：特殊效果）")]
        [TextArea] public string specialNote = "";

        [Header("价格")]
        public int basePrice = 10;

        /// <summary>品质倍率 —— 复用既有 `Rarity` 的档位语义 ✓ 不另建一份表 ✗</summary>
        public float QualityMultiplier
        {
            get
            {
                switch (quality)
                {
                    case GoodsQuality.Uncommon: return 1.25f;
                    case GoodsQuality.Rare: return 1.6f;
                    case GoodsQuality.Epic: return 2.1f;
                    case GoodsQuality.Legendary: return 2.8f;
                    default: return 1f;
                }
            }
        }

        /// <summary>第 <paramref name="level"/> 级的水分需求（级数从 1 起）。</summary>
        public float WaterAtLevel(int level)
            => Mathf.Max(1f, waterToMature + waterPerLevel * Mathf.Max(0, level - 1));

        /// <summary>第 <paramref name="level"/> 级的收割倍率。</summary>
        public float YieldAtLevel(int level)
            => 1f + yieldPerLevel * Mathf.Max(0, level - 1);

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(id)) id = name;
            maxLevel = Mathf.Max(0, maxLevel);
            waterToMature = Mathf.Max(1f, waterToMature);
            waterPerPour = Mathf.Max(1f, waterPerPour);
            baseGrowSeconds = Mathf.Max(1f, baseGrowSeconds);
            basePrice = Mathf.Max(1, basePrice);
        }
    }

    /// <summary>
    /// **插件的静态定义**（SO 资产）。规格：安装到指定武器、增加攻击效果或修改攻击行为 ✓
    /// </summary>
    [CreateAssetMenu(fileName = "PluginAsset", menuName = "Seedfall/商品/插件")]
    public sealed class PluginAsset : ScriptableObject
    {
        [Header("身份")]
        public string id = "";
        public string displayName = "";
        public GoodsQuality quality = GoodsQuality.Common;
        [TextArea] public string description = "";

        [Header("适用武器（规格：明确适用武器类型）")]
        [Tooltip("可安装的武器。留空数组 = 任意武器都可装")]
        public WeaponSlot[] allowedWeapons = Array.Empty<WeaponSlot>();

        [Header("效果（规格：效果参数）")]
        public PluginEffectKind effect = PluginEffectKind.DamageFlat;
        public float amount = 1f;
        /// <summary>
        /// ★ 第二效果参数（v171 新增）★
        ///
        /// 只有 `ChainOnHit` 用它：那一档要**同时**说两件事 ——
        /// "弹几个目标"（`amount`）与"每一跳打多少"（`amount2`）✗
        /// 一个 `amount` 塞不下两个独立的数，硬塞就得把比例写死在代码里，
        /// 而那正是"数据表说了不算"的老毛病 ✓
        ///
        /// 其余效果留 0 即可（它们只有一个数）✓
        /// </summary>
        public float amount2 = 0f;

        [Header("叠加规则（规格：叠加规则）")]
        public StackRule stackRule = StackRule.Stackable;
        [Tooltip("最多可安装几个（Stackable / Multiplicative 生效）")]
        public int maxStack = 3;
        [Tooltip("同一武器上是否只能装一个同类技能型的插件")]
        public bool exclusivePerWeapon = false;

        [Header("价格")]
        public int basePrice = 25;

        /// <summary>这个插件能不能装到该武器上。</summary>
        public bool Fits(WeaponSlot w)
            => allowedWeapons == null || allowedWeapons.Length == 0 || Array.IndexOf(allowedWeapons, w) >= 0;

        public float QualityMultiplier
        {
            get
            {
                switch (quality)
                {
                    case GoodsQuality.Uncommon: return 1.25f;
                    case GoodsQuality.Rare: return 1.6f;
                    case GoodsQuality.Epic: return 2.1f;
                    case GoodsQuality.Legendary: return 2.8f;
                    default: return 1f;
                }
            }
        }
    }

    /// <summary>
    /// **道具的静态定义**（SO 资产）。规格：修改玩家属性或全局游戏规则 ✓
    /// </summary>
    [CreateAssetMenu(fileName = "ItemAsset", menuName = "Seedfall/商品/道具")]
    public sealed class ItemAsset : ScriptableObject
    {
        [Header("身份")]
        public string id = "";
        public string displayName = "";
        public GoodsQuality quality = GoodsQuality.Common;
        [TextArea] public string description = "";

        [Header("作用域")]
        public ItemScope scope = ItemScope.PlayerStat;
        [Tooltip("scope = PlayerStat 时生效：改哪个属性")]
        public StatId stat = StatId.MeleeDamage;
        [Tooltip("scope = GlobalRule 时生效：规则键（由 ItemEffectSystem 解释）")]
        public string ruleKey = "";

        [Header("数值")]
        public float amount = 1f;
        [Tooltip("真 = 乘算（×(1+N)）；假 = 加算（+N）")]
        public bool multiplicative = false;

        [Header("叠加与触发（规格：叠加限制和触发条件）")]
        public StackRule stackRule = StackRule.Stackable;
        public int maxStack = 3;
        [Tooltip("留空 = 任何时候都能买；否则需满足条件（由 ItemEffectSystem 解释）")]
        public string requireKey = "";

        [Header("价格")]
        public int basePrice = 20;

        public float QualityMultiplier
        {
            get
            {
                switch (quality)
                {
                    case GoodsQuality.Uncommon: return 1.25f;
                    case GoodsQuality.Rare: return 1.6f;
                    case GoodsQuality.Epic: return 2.1f;
                    case GoodsQuality.Legendary: return 2.8f;
                    default: return 1f;
                }
            }
        }
    }

    // ─────────────────────────── 运行时实例 ───────────────────────────

    /// <summary>
    /// **玩家持有的一个种子实例**（运行时）。
    ///
    /// ★ 它只引用静态资产，**绝不修改它** ★
    /// 等级、已浇水、进度这些**每实例不同**的状态全在这里 ✓
    /// 这样同一个 `SeedAsset` 可以被 100 个实例共享而不互相污染 ✓
    /// </summary>
    public sealed class SeedInstance
    {
        public readonly SeedAsset Asset;
        public int Level = 1;
        public float Water;                 // 已浇的水
        public float GrowProgress;          // 0..1

        public SeedInstance(SeedAsset a) { Asset = a; }

        public float WaterNeeded => Asset != null ? Asset.WaterAtLevel(Level) : 1f;
        public bool IsMature => Water >= WaterNeeded;
        public int GoldYield => Asset != null ? Mathf.RoundToInt(Asset.goldHarvest * Asset.YieldAtLevel(Level)) : 0;
        public int ExpYield => Asset != null ? Mathf.RoundToInt(Asset.expHarvest * Asset.YieldAtLevel(Level)) : 0;

        /// <summary>浇一次水。返回实际加了多少（灌满了就少加）✗</summary>
        public float Pour()
        {
            if (Asset == null) return 0f;
            float before = Water;
            Water = Mathf.Min(WaterNeeded, Water + Asset.waterPerPour);
            return Water - before;
        }

        /// <summary>升级。返回是否成功（满级或未知资产失败）✗</summary>
        public bool TryLevelUp()
        {
            if (Asset == null || Level >= Asset.maxLevel) return false;
            Level++;
            return true;
        }
    }

    /// <summary>**玩家持有的一个道具**（运行时）。</summary>
    public sealed class ItemInstance
    {
        public readonly ItemAsset Asset;
        public int Stacks = 1;

        /// <summary>
        /// ★ 已经**写进属性**的层数 ★（防重复发放的关键）
        ///
        /// 规格明确要求"避免重复订阅事件、重复发放奖励" ✗
        /// 而道具效果是**持续生效**的（改的是属性，不是一次性奖励）✓
        /// 如果每次"刷新效果"都把全部层数加一遍，叠两层就会变成加三次、四次… ✗
        ///
        /// 所以应用时只加**增量**（`Stacks - AppliedStacks`）✓
        /// 这样"再买一个"和"首次应用"走同一条路，且**幂等** ✓
        /// </summary>
        public int AppliedStacks;

        public ItemInstance(ItemAsset a) { Asset = a; }

        /// <summary>还有几层没写进属性。</summary>
        public int PendingStacks => Mathf.Max(0, Stacks - AppliedStacks);

        public bool CanStackMore => Asset != null && Asset.stackRule != StackRule.Unique
                                    && Stacks < Mathf.Max(1, Asset.maxStack);
    }

    /// <summary>**某个武器上已安装的插件**（运行时）。</summary>
    public sealed class PluginInstance
    {
        public readonly PluginAsset Asset;
        public readonly WeaponSlot Slot;
        public int Stacks = 1;

        /// <summary>已经聚合进武器参数的层数（与 ItemInstance 同理，防重复累加）✓</summary>
        public int AppliedStacks;

        public PluginInstance(PluginAsset a, WeaponSlot slot) { Asset = a; Slot = slot; }

        public bool CanStackMore => Asset != null && Asset.stackRule != StackRule.Unique
                                    && Stacks < Mathf.Max(1, Asset.maxStack);
    }

    /// <summary>
    /// **玩家在本局拥有的全部商品**（运行时）。
    ///
    /// 它是一局的状态 ✗ 所以**每局新建** ✓ 不在 SO 上，也不静态 ✗
    /// 规格要求"区分静态商品定义与玩家运行时持有的数据" ✓ 这就是那条界线的运行时一侧 ✓
    /// </summary>
    public sealed class PlayerGoods
    {
        /// <summary>
        /// ★ **每把武器的插件插槽数 = 6**（用户新标准："每个武器可以装备 6 个插件"）★
        ///
        /// ★ 为什么上限写在这里，而不是写在 `WeaponSystem.Install` 里 ★
        /// 插槽数是**商品的持有规则**，不是"安装这个动作"的规则 ✗
        /// 写在 `Acquire` 这一层，任何往武器上加插件的路径（商店、调试、将来的 UI）
        /// 都会被同一条上限拦住 ✓
        /// 若写在 `Install` 里，绕过 `Install` 直接 `Acquire` 就能装到第 7 件 ——
        /// 本会话已经吃过一次"上限只写在一条路上"的亏 ✓
        /// </summary>
        public const int MaxPluginsPerWeapon = 6;

        public readonly List<SeedInstance> Seeds = new List<SeedInstance>();
        public readonly List<PluginInstance> Plugins = new List<PluginInstance>();
        public readonly List<ItemInstance> Items = new List<ItemInstance>();

        /// <summary>某把武器上已装的插件（按安装顺序）。</summary>
        public IEnumerable<PluginInstance> PluginsOn(WeaponSlot w)
        {
            for (int i = 0; i < Plugins.Count; i++)
                if (Plugins[i].Slot == w) yield return Plugins[i];
        }

        /// <summary>
        /// 某把武器上**已占用的插槽数** = 该武器上的插件实例条数。
        ///
        /// ⚠️ 注意它数的是**实例**（`PluginInstance`），不是 `Stacks`：
        /// "磨刀石 ×3"占 **1** 个插槽、叠 3 层 ✗
        /// 若按 Stacks 计，叠满一个插件就等于把 6 个位置全吃掉，与"6 个插件"的语义不符 ✓
        /// </summary>
        public int SlotsUsedOn(WeaponSlot w)
        {
            int n = 0;
            for (int i = 0; i < Plugins.Count; i++) if (Plugins[i].Slot == w) n++;
            return n;
        }

        /// <summary>这把武器还能再装几个插件。</summary>
        public int FreeSlotsOn(WeaponSlot w) => Mathf.Max(0, MaxPluginsPerWeapon - SlotsUsedOn(w));

        /// <summary>背包里"买了但还没决定装到哪把武器"的插件（`Slot == None`）。</summary>
        public int PendingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Plugins.Count; i++) if (Plugins[i].Slot == WeaponSlot.None) n++;
                return n;
            }
        }

        /// <summary>按资产查已有的实例（用于叠加而不是新增）✗</summary>
        public ItemInstance Find(ItemAsset a)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Asset == a) return Items[i];
            return null;
        }

        public PluginInstance Find(PluginAsset a, WeaponSlot w)
        {
            for (int i = 0; i < Plugins.Count; i++)
                if (Plugins[i].Asset == a && Plugins[i].Slot == w) return Plugins[i];
            return null;
        }

        /// <summary>
        /// 购买一个道具。返回是否成功。
        ///
        /// ★ 叠加规则在这里统一处理 ★ —— 规格要求"不要为每种商品复制一套独立的购买逻辑" ✓
        /// 唯一 / 可叠加 / 叠加上限 三种情形走同一个入口 ✓
        /// </summary>
        public bool Acquire(ItemAsset a)
        {
            if (a == null) return false;
            ItemInstance have = Find(a);
            if (have == null) { Items.Add(new ItemInstance(a)); return true; }
            if (!have.CanStackMore) return false;      // Unique 或已达上限 ✗
            have.Stacks++;
            return true;
        }

        /// <summary>
        /// 往一把武器上装插件。返回是否成功。
        ///
        /// v171：**加了每把武器 6 个插槽的总上限** ✗
        /// 顺序有意为之：先查"是不是已有同名可叠的"（叠层不占新插槽），
        /// 再查总上限 ⇒ "装满 6 件后仍然可以把其中一件叠到 2 层" ✓
        /// 反过来写的话，装满 6 件之后连叠层都会被拒 —— 那不是设计意图 ✓
        /// </summary>
        public bool Acquire(PluginAsset a, WeaponSlot w)
        {
            if (a == null || !a.Fits(w)) return false;
            PluginInstance have = Find(a, w);
            if (have != null)
            {
                if (!have.CanStackMore) return false;
                have.Stacks++;
                return true;
            }
            if (w != WeaponSlot.None && SlotsUsedOn(w) >= MaxPluginsPerWeapon) return false;   // 插槽满
            Plugins.Add(new PluginInstance(a, w));
            return true;
        }

        /// <summary>
        /// 把一个**已买到手、还放在背包里**（`Slot == None`）的插件装到指定武器上。
        ///
        /// 商店把买到的插件统一放进"待装"状态（`WeaponSlot.None`）✓
        /// 玩家之后决定装到哪把武器 ✗
        /// 所以安装这一步必须是"**移动**"而不是"再来一件"——
        /// 否则背包里那件会留下来，同一件插件在账面上出现两次 ✓
        /// </summary>
        public bool InstallFromPending(PluginAsset a, WeaponSlot w)
        {
            if (a == null || w == WeaponSlot.None) return false;
            PluginInstance pending = Find(a, WeaponSlot.None);
            if (pending != null)
            {
                // 先试试"装得上去吗"：装不上就别动背包（不许出现"背包少了、武器上也没有"）✗
                if (Find(a, w) == null && SlotsUsedOn(w) >= MaxPluginsPerWeapon) return false;
                if (Find(a, w) != null && !Find(a, w).CanStackMore) return false;
                Plugins.Remove(pending);
                if (Acquire(a, w)) return true;
                Plugins.Add(pending);       // 回滚：宁可没装，也不能凭空少一件 ✓
                return false;
            }
            return Acquire(a, w);
        }
    }
}
