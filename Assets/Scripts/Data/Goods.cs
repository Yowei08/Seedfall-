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
    /// 商品品质。★ 阶段 3 改名为 **D / C / B / A / S**（不再用 Common…Legendary）★
    ///
    /// ★ 为什么改成字母档 ★
    /// 旧名是一套"英文稀有度形容词"（普通/一般/稀有/史诗/传奇），而**插件表里根本没有"普通"**
    /// —— 12 件全部落在 C 及以上 ⇒ 一整套五档名里只有四档真的被用到 ✗
    /// 换成字母档之后，档位本身就是"从低到高"的顺序，读表时不用再背一张对照表 ✓
    ///
    /// ★ 索引必须保持 0..4 且顺序不变 ★
    /// `ShopPool.seedQualityWeights / pluginQualityWeights / itemQualityWeights` 是**按索引**
    /// 取权重的数组（`(int)quality`）✗ 换名但换序会让"哪一档更常见"悄悄反过来 ✓
    /// 所以这里只改名与注释，**取值一个不动**：D=0 / C=1 / B=2 / A=3 / S=4 ✓
    /// </summary>
    public enum GoodsQuality { D = 0, C = 1, B = 2, A = 3, S = 4 }

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
    /// 自检里有一条"每个枚举值都被消费"的断言来钉住这条纪律。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// ★ 阶段 3：**只改攻击方式，不改玩家属性** ★
    ///
    /// 上一版的 17 种里有 12 种是"给玩家属性加一个数"（伤害 +N / 攻速 +N% / 暴击 +N% …）✗
    /// 它们买到的是**同一个体验的更大版本** —— 商店里没有真正的选择，永远是"哪件数字高" ✓
    /// 这一版把这 12 种**全部删掉**（`PLUGIN-NO-STAT` 这条断言就是在数它们还剩几件）✓
    ///
    /// 留下的是 12 种"**改变攻击的形态**"：多一道、多一刀、多一炸、附一层、回一点水 ✓
    /// 属性区因此从 `WeaponModifiers` 上整体消失 —— 攻击公式的入参不再有任何一项来自插件 ✓
    /// ══════════════════════════════════════════════════════════════════════
    ///
    /// ★ 单位约定（踩过的坑，写在这里免得下次再踩）★
    /// `Lifesteal` / `FreeBurst` / `ThickJet` / `SlowOnHit.pct` 这类"比例"是 **0..1 小数**：
    /// `0.20` = 20%、`0.12` = 12%。写成 `20` 会得到 2000% ✓
    /// 而 `AutoCharge`（每秒回 5 点水）是**绝对值**，写 `0.05` 会变成"每秒回 0.05 点"✓
    /// </summary>
    public enum PluginEffectKind
    {
        /// <summary>无效果（表里写错 / 还没配）。**不算一种效果**，自检把它当"这行是死的"。</summary>
        None = 0,

        // ───────────── 锄头 `allowedWeapons:[0]` ─────────────

        /// <summary>**剑气**（`hoe_swordwave`，B 档）：挥击的同时沿瞄准方向多劈 `amount` 道/层剑气，
        /// 射程 = 近战范围 ×2。打的是**挥击框之外**的敌人（框内的已被挥击打过，不重复打）。</summary>
        SwordWave,

        /// <summary>**灼烧**（`hoe_burn`，C 档）：命中附 `amount2`(=1) 层灼烧；
        /// 每层每 1 秒掉 `近战伤害 × amount`(0.50)，持续 5 秒，**时长不叠**、每秒伤害按层乘。
        /// 结算在 `PluginDotTable`（持续效果表），不在这里。</summary>
        BurnOnHit,

        /// <summary>**双击**（`hoe_doublestrike`，A 档）：一次攻击挥砍 `1 + amount×层` 次，
        /// 每一刀**各自独立结算**（各判暴击），第 k 刀延迟 `0.12s × k`。</summary>
        DoubleStrike,

        /// <summary>**吸血**（`hoe_lifesteal`，S 档）：造成的伤害 × `amount`(0.20)/层 转成生命，
        /// 上限 `MaxHp`。★ **对持续伤害不生效** ★（DoT 不经过 `PluginPostHit`）。</summary>
        Lifesteal,

        // ───────────── 水壶 `allowedWeapons:[1]` ─────────────

        /// <summary>**多柱**（`can_multijet`，B 档）：水柱 +`amount`(1) 道/层，每道朝向差
        /// `amount2`(8)°，**每道独立扣水**（一次判定 N 道就付 N 份）。</summary>
        MultiJet,

        /// <summary>**减速**（`can_slow`，C 档）：命中附 `amount2`(=1) 层减速；
        /// 移速 −`amount`(0.12)/层（下限 −60%）；被减速的敌人每秒受 `远程伤害 × 0.50`，持续 5 秒。
        /// 结算同样在 `PluginDotTable`。</summary>
        SlowOnHit,

        /// <summary>**加粗**（`can_thickjet`，A 档）：水柱**宽度** +`amount`(0.50)/层。
        /// ★ **只改宽不改长** ★ —— 射程由 `tools.can.range` 决定，本档不许碰它。</summary>
        ThickJet,

        /// <summary>**自充**（`can_autocharge`，S 档）：每秒回 `amount`(5) 点水量/层，
        /// 上限 `WaterAmmoMax`。它回的是**弹药**，不是玩家属性 ✓</summary>
        AutoCharge,

        // ───────────── 肥料 `allowedWeapons:[2]` ─────────────

        /// <summary>**子爆**（`fert_subburst`，B 档）：主爆旁额外 `amount`(1) 个子爆/层，
        /// 子爆半径 = 主爆 × `amount2`(0.70)，落点按**正六边形**角分布（0°/60°/…/300°）、
        /// 距主爆心 = 主爆半径 × 0.9。</summary>
        SubBurst,

        /// <summary>**毒素**（`fert_poison`，C 档）：爆点留 5 秒毒区/层，毒区半径 = 主爆 ×0.8，
        /// 每层每秒 `爆炸伤害 × amount`(0.50)。毒区**按位置**，后走进来的敌人照样掉血 ✓</summary>
        PoisonZone,

        /// <summary>**免耗**（`fert_freeburst`，A 档）：`amount`(0.12)/层 概率**不消耗**肥料充能，
        /// 上限 100%（叠到 9 层就是必不耗）。</summary>
        FreeBurst,

        /// <summary>**连环**（`fert_chain`，S 档）：爆炸次数 +`amount`(1)/层，第二爆点落在
        /// 主爆点外 `amount2`(2.5)m，★ **半径同主爆** ★（不是子爆的 0.7×）。</summary>
        ChainBurst,

        // ════════════════════════════════════════════════════════════════════
        // ★★ v253：**六个全新机制**（用户原话：「我设计过剑气、毒、子爆、加粗等内容，
        //    新加的插件不要在我的方向做拓展或者做数值强化，设计全新的机制和特效」）★★
        //
        // ── 与前面 12 个的根本区别 ──
        //   前 12 个回答的都是"**这一下多打谁 / 多打几次 / 打完留什么**"（形态与持续伤害）；
        //   下面 6 个各自引入一个**新的作用维度**：
        //     时间（回响）/ 目标跳转（弹射）/ 田地（催熟）/
        //     连续命中的计数（收束）/ 位移（潮汐）
        //   ★ 这里的"击杀转化（余温）"一档已随 `hoe_warmth` 一起删除（用户要求删掉余温插件）★
        //   ⇒ 它们**不是**任何既有档位的 amount 加减，也不是同一效果换了数值 ✓
        // ════════════════════════════════════════════════════════════════════

        /// <summary>**回响**（锄头）：命中 `amount2`(0.35) 秒后，在**同一个敌人身上**再结算一次
        /// `本次伤害 × amount`(0.40) 的伤害。★ 新维度 = **时间**：这一击的账延后结 ✓
        /// 与"双击/剑气"的区别：那两者是**当下**多打；回响是**同一刀**晚一点再疼一次 ✓</summary>
        Echo,

        /// <summary>**弹射**（水壶）：水柱命中后，跳到 `amount2`(3.5)m 内的**另一个**敌人，
        /// 造成 `本次伤害 × amount`(0.60)。★ 新维度 = **目标跳转**（不是多一道柱、不是加宽）✓</summary>
        Ricochet,

        /// <summary>**催熟**（水壶）：水柱扫过"已种下但没成熟"的地块时，立刻推进
        /// `amount`(3) 秒成长。★ 新维度 = **作用于田地**（把"浇水"变成"催熟"）✓</summary>
        Ripen,

        /// <summary>**收束**（锄头）：对**同一个敌人**每命中 `amount`(3) 次，第 `amount` 次追加
        /// `本次伤害 × amount2`(0.40) 的"处决"伤害，并把计数清零；`FocusWindow` 秒内没再命中就重置。
        /// ★ 新维度 = **命中计数**（前 12 个都不记"这是第几下"）✓</summary>
        Focus,

        /// <summary>**潮汐**（水壶）：水柱命中时把敌人**推开** `amount`(1.2)m（沿"玩家→敌人"方向）。
        /// ★ 新维度 = **位移**：减速改的是"走多快"，潮汐改的是"站在哪" ✓</summary>
        Tide,

        // ════════════════════════════════════════════════════════════════════
        // ★★ v255：肥料线的三件新机制（用户定稿）★★
        // ════════════════════════════════════════════════════════════════════

        /// <summary>**伏雷**（肥料）：落地不立刻炸，先埋进土里上膛；**敌人踩到**按 `amount`(1.5) 倍引爆，
        /// 没人踩则上膛 `MineFuse`(6) 秒后自己炸、按 `amount2`(0.8) 倍。
        /// ★ 新维度 = **引爆时机交给敌人**（既有爆炸全是"投出即爆"）✓</summary>
        Mine,

        /// <summary>**护壤**（肥料）：爆炸时玩家**自己在半径内**才吃到 —— 获得 `amount`(2) 点额外护盾，
        /// 持续 `amount2`(3) 秒。★ 新维度 = **玩家侧防御**（前 18 件全是对敌效果）✓</summary>
        Ward,

        /// <summary>**灼印**（肥料）：被炸到的敌人挂一个像素「X」标记，**下一次任何来源的伤害** ×(1+`amount`)
        /// 并消耗标记，标记最多挂 `amount2`(3) 秒。★ 新维度 = **跨伤害源**（前 18 件都只管自己那条线）✓</summary>
        Mark,

        // ════════════════════════════════════════════════════════════════════
        // ★★ v290：**5 个全新机制**（锄头 3 / 水壶 2 / 肥料 1）★★
        //
        // ── 与前面 20 个的区别（每一条都换了一个"作用对象"，不是数值加减）──
        //   感电：把**这一刀**传播到它周围的敌人（20 件里没有一件看"溅射数量"的）
        //   狂暴：判据是**目标的血量**（上一轮回收的「弱点」也是这个维度 —— 本轮按用户新规格
        //         把它定为"残血目标多吃伤害"，且**只有它**，不再有第二个同类）
        //   巨型：改的是**挥击判定的形状**（半圆 → 整圈），不是范围数值
        //   臭水沟：把**弹药消耗**变成掉落物（前 20 件没有一件读"耗了多少水"）
        //   海浪：作用在**玩家的移动**上（唯一一件给玩家位移增益的）
        //   黑洞：把**爆炸的落点变成有引力的场**（唯一一件在爆炸之前改站位）
        //   ★ 「锋利」（水壶也能收割）已随 `can_sharp` 一起删除（用户要求删掉锋利插件）★
        //
        // ── 执行点（与上一轮同一条纪律：算式只有一处）──
        //   闸门组（感电 / 狂暴）在 `World.DealDamage`（`Actors.cs`，与灼印同位置）；
        //   几何组（巨型）在 `RunSession.MeleePointHit`；
        //   动作组（臭水沟 / 海浪 / 黑洞）在各自的那一次出手上。
        //   **算式全部在 `Combat/PluginExtra2.cs`**，`Aggregate` 只把数搬进格子 ✓
        // ════════════════════════════════════════════════════════════════════

        /// <summary>**感电**（锄头）：这一刀再电击主目标 `ShockRadius` 米内最近的若干敌人，
        /// 每个吃 `这一刀伤害 × 10%`。层数只加**目标数**（3 / 6 / 9 人）。</summary>
        Shock,

        /// <summary>**狂暴**（锄头）：**目标**生命低于 40% 时，这一击的伤害 +10%/层（2 层 20%）。
        /// ★ 方向：敌人残血你打它更疼，不是你自己残血 ★</summary>
        Frenzy,

        /// <summary>**巨型**（锄头，唯一件）：挥击判定从"面前 180° 半圆"改成**环绕玩家的整圈**
        /// （内径 = 外径 × 0.35，外径沿用既有的 `MeleeRingOuter`）。</summary>
        Giant,

        /// <summary>**臭水沟**（水壶）：水柱变成**绿色浑浊**；每累计消耗 20 点水，在玩家周围掉一个
        /// 巨大香蕉皮（吃 `水壶伤害 × 60%`）。1/2 层 ⇒ 一次掉 1/2 个。</summary>
        Sewer,

        /// <summary>**海浪**（水壶）：喷水时脚底起一道向外扩散的浪，扫到的敌人吃 `水壶伤害 × 30%`，
        /// 玩家 **+50% 移速 1 秒**；每层再加宽 50%（1/2/3 层）。</summary>
        Wave,

        /// <summary>**黑洞**（肥料，唯一件）：每次爆炸结算**之前**，先把爆炸范围内的敌人朝爆心拉拢；
        /// 爆炸前 0.25 秒在爆心画一圈**向内收缩**的暗环（吸引阶段）。</summary>
        BlackHole,
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
        public GoodsQuality quality = GoodsQuality.D;

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
                    case GoodsQuality.C: return 1.25f;
                    case GoodsQuality.B: return 1.6f;
                    case GoodsQuality.A: return 2.1f;
                    case GoodsQuality.S: return 2.8f;
                    default: return 1f;          // D 档 = 基准价
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
        public GoodsQuality quality = GoodsQuality.D;
        [TextArea] public string description = "";

        [Header("适用武器（规格：明确适用武器类型）")]
        [Tooltip("可安装的武器。留空数组 = 任意武器都可装")]
        public WeaponSlot[] allowedWeapons = Array.Empty<WeaponSlot>();

        [Header("效果（规格：效果参数）")]
        public PluginEffectKind effect = PluginEffectKind.None;
        /// <summary>
        /// 表里 `effect` 那一格的**字面值**（由 `TableDatabase` 填，运行时不改）✓
        /// 用途只有一个：**报告与自检要点名"表里写的到底是什么"** ✗
        /// `effect` 解析失败会回落 `None` ⇒ 只留枚举的话，诊断信息只剩"没写效果" ✓
        /// </summary>
        public string rawEffect = "";
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
                    case GoodsQuality.C: return 1.25f;
                    case GoodsQuality.B: return 1.6f;
                    case GoodsQuality.A: return 2.1f;
                    case GoodsQuality.S: return 2.8f;
                    default: return 1f;          // D 档 = 基准价
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
        public GoodsQuality quality = GoodsQuality.D;
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
                    case GoodsQuality.C: return 1.25f;
                    case GoodsQuality.B: return 1.6f;
                    case GoodsQuality.A: return 2.1f;
                    case GoodsQuality.S: return 2.8f;
                    default: return 1f;          // D 档 = 基准价
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
