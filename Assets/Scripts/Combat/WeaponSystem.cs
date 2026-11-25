using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// **一把武器在"基础属性 + 已装插件"之后的实际攻击参数**。
    ///
    /// ★ 为什么是一个结构体，而不是"每个插件自己改行为" ★
    /// 规格要求"通过明确的接口或统一的效果处理机制扩展武器能力" ✗
    ///
    /// 如果每个插件都去 hook 攻击流程，会有三个后果：
    ///   ① 插件之间互相看不见，**叠加顺序**变成了隐式约定 ✗
    ///   ② 每加一个插件就要在攻击流程里插一段 if ✗
    ///   ③ "同一插件装两个"和"两个同类插件"的数值无法统一验证 ✗
    ///
    /// 本方案：插件**只贡献数值** ⇒ 聚合成这一个结构体 ⇒ 攻击流程只读它 ✓
    /// 于是叠加顺序、上限、互斥都在**一处**决定 ✓ 而且**可离线断言** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// ★ 阶段 3：**数值区整体消失** ★
    ///
    /// 上一版这个结构体上半截是"数值区"（`DamageFlat / DamagePercent / AttackSpeed /
    /// Range / CritRate / CritDamage / ExplodeRadius / Knockback / ProjectileCount`），
    /// 那 9 个字段喂的是 `AttackFormula` 的各个乘区 —— 也就是"给玩家属性加一个数"✗
    /// 用户的新标准是"**只改攻击方式，不改玩家属性**"⇒ 那 9 个字段连同
    /// `ApplyToAttack` / `FullHpMult` / `DamageMult` 一起删除 ✓
    ///
    /// 现在三条攻击路径算伤害时**一个插件都不读** ——
    /// 公式的入参全部来自 `TuningDef` 与 `StatBlock`（与装插件之前逐字相同）✓
    /// 插件只回答"**多打谁 / 多打几次 / 打完之后留下什么**"这三个问题 ✓
    /// ══════════════════════════════════════════════════════════════════════
    /// </summary>
    public struct WeaponModifiers
    {
        // ─────────────── 锄头（`allowedWeapons:[0]`）───────────────

        /// <summary>剑气道数（&gt;0 = 挥击同时劈出剑气）。射程见 `RunSession` 的 ×2。</summary>
        public int SwordWaveCount;
        /// <summary>每次命中附加的灼烧层数（= 该插件的层数；层数封顶在 `PluginDotTable`）。</summary>
        public int BurnLayersPerHit;
        /// <summary>**每层**灼烧每秒的伤害比例（0.50 ⇒ 每秒 = 近战伤害 × 本值）。</summary>
        public float BurnRatioPerLayer;
        /// <summary>额外挥砍次数（0 = 只挥 1 次；1 = 双击）。</summary>
        public int ExtraSwings;
        /// <summary>
        /// **多次挥舞插件的层数**（0 = 没装）。它决定"每刀伤害折减"的指数：
        /// 每拥有一层，锄头伤害 ×2/3 一次（累计相乘，见 `WeaponSystem.DoubleStrikeDamage`）。
        ///
        /// ★ 为什么不直接用 `ExtraSwings` ★
        /// 本轮 `hoe_doublestrike` 的 `amount` 恰好是 1（1 层 = 1 刀），两者数值相同 ✓
        /// 但那是**数据巧合**，不是机制：`ExtraSwings` 回答"多几刀"，
        /// 折减要的是"几层" ✗ 一旦有人把 amount 改成 2，用 `ExtraSwings` 当指数就会
        /// 把"2 刀"读成"折减 2 次"⇒ 刀数与折减脱钩，而这类错不会报错 ✓
        /// 所以聚合时单独记一份层数（见 `Aggregate` 的 `DoubleStrike` 分支）✓
        /// </summary>
        public int DoubleStrikeLayers;
        /// <summary>吸血比例（0.20 = 造成伤害的 20% 转生命）。</summary>
        public float LifestealPct;

        // ─────────────── 水壶（`allowedWeapons:[1]`）───────────────

        /// <summary>额外水柱道数（0 = 单道；1 = 双柱）。</summary>
        public int ExtraJets;
        /// <summary>每次命中附加的减速层数。</summary>
        public int SlowLayersPerHit;
        /// <summary>**每层**减速的移速降低比例（0.12 ⇒ −12%）。</summary>
        public float SlowPctPerLayer;
        /// <summary>**每层**减速每秒的伤害比例（0.50 ⇒ 每秒 = 远程伤害 × 本值）。</summary>
        public float SlowDamageRatio;
        /// <summary>水柱宽度 +N（0.50 = +50%）。★ 只影响宽，不影响长 ★</summary>
        public float BeamWidthPct;
        /// <summary>每秒回水量（5 = 每秒 5 点）。</summary>
        public float AutoChargePerSecond;
        /// <summary>
        /// **自充的层数**（0 = 没装）。★ 用户需求④：层数决定水球发数（2 颗/层）★
        ///
        /// ★ 为什么不复用 `AutoChargePerSecond / amount` 去反推层数 ★
        /// `amount` 是**数据侧的当前值**（v184 已从 5 改成 12）⇒ 用它做除数，
        /// 改一次表就把水球数改了 ✗ 而"层数 ⇒ 2 颗"是**机制**，与每秒回多少水无关 ✓
        /// 所以聚合时单独记一份层数（见 `Aggregate` 的 `AutoCharge` 分支）✓
        /// </summary>
        public int AutoChargeLayers;

        // ─────────────── 肥料（`allowedWeapons:[2]`）───────────────

        /// <summary>额外子爆个数。</summary>
        public int SubBurstCount;
        /// <summary>子爆半径 = 主爆 × 本比例（0.70）。</summary>
        public float SubBurstRadiusRatio;
        /// <summary>**每层**毒区每秒的伤害比例（0.50 ⇒ 每秒 = 爆炸伤害 × 本值）。</summary>
        public float PoisonRatioPerLayer;
        /// <summary>毒区层数（= 该插件的层数）。</summary>
        public int PoisonLayers;
        /// <summary>不消耗充能的概率（0.12 = 12%，封顶 100%）。</summary>
        public float FreeBurstChance;
        /// <summary>额外爆炸次数。</summary>
        public int ChainBurstCount;
        /// <summary>第二个爆点距主爆点的距离（米，2.5）。</summary>
        public float ChainBurstGap;

        // ════════════════════════════════════════════════════════════════════
        // ★★ v253：**六个全新机制的字段**（与上面 12 个不共用任何一格）★★
        // ════════════════════════════════════════════════════════════════════

        /// <summary>回响：延迟结算的比例（0.40 = 本次伤害的 40%）。</summary>
        public float EchoPct;
        /// <summary>回响：延迟秒数（0.35）。</summary>
        public float EchoDelay;
        /// <summary>弹射：跳到第二个敌人时保留的伤害比例（0.60）。</summary>
        public float RicochetPct;
        /// <summary>弹射：找第二个敌人的搜索半径（米，3.5）。</summary>
        public float RicochetRadius;
        /// <summary>催熟：水柱扫到已种未熟的地块时推进的成长秒数（3）。</summary>
        public float RipenSeconds;
        /// <summary>收束：每命中几次触发一次（3）。</summary>
        public int FocusEvery;
        /// <summary>收束：触发时追加的伤害比例（0.40）。</summary>
        public float FocusBonusPct;
        /// <summary>潮汐：命中把敌人推开的距离（米，1.2）。</summary>
        public float TidePush;

        // ── ★ v255：肥料线的三件新机制 ──
        /// <summary>伏雷：踩中时的伤害倍率（1.5 = 150%）。</summary>
        public float MineHitPct;
        /// <summary>伏雷：没人踩、到点自爆时的伤害倍率（0.8 = 80%）。</summary>
        public float MineMissPct;
        /// <summary>伏雷：上膛秒数（0.6，每层 −0.15）。</summary>
        public float MineArmTime;
        /// <summary>伏雷：感应半径（米，2.2，每层 +0.3）。</summary>
        public float MineSenseRadius;
        /// <summary>护壤：每次爆炸给玩家的护盾点数（2）。</summary>
        public float WardShield;
        /// <summary>护壤：护盾持续秒数（3）。</summary>
        public float WardSeconds;
        /// <summary>灼印：下一次伤害的放大比例（0.5 = +50%）。</summary>
        public float MarkBonusPct;
        /// <summary>灼印：印记持续秒数（3）。</summary>
        public float MarkSeconds;

        // ════════════════════════════════════════════════════════════════════
        // ★★ v290：**7 个全新机制的字段**（与上面 21 格不共用任何一格）★★
        //
        // 这一组的共同点：它们的**数值来自代码常量**（`PluginExtra2` 顶部那一段），
        // 不来自 `plugins.json` 的 `amount / amount2` —— 因为新 effect 的参数还没进表，
        // 而"数值只有一个真相"比"表里能改"更要紧（表里那两列只是**镜像**，见 `_note_v290`）✓
        // 层数照旧只乘在"它真的该叠的那一格"上（感电叠目标数、狂暴叠百分比、海浪叠宽度）。
        // ════════════════════════════════════════════════════════════════════

        /// <summary>感电：一次额外电击几个目标（3/层 ⇒ 3/6/9）。</summary>
        public int ShockTargets;
        /// <summary>感电：每个被电到的敌人吃"这一刀伤害"的该比例（0.10）。</summary>
        public float ShockPct;
        /// <summary>狂暴：**目标**生命低于该比例时生效（0.40）。</summary>
        public float FrenzyBelow;
        /// <summary>狂暴：生效时这一击的加成比例（0.10/层）。</summary>
        public float FrenzyPct;
        /// <summary>巨型：挥击判定改成"环绕玩家的整圈"。</summary>
        public bool GiantRing;
        /// <summary>巨型：环的内径 = 外径 × 本值（0.35）。</summary>
        public float GiantInnerRatio;
        /// <summary>臭水沟：每消耗这么多点水掉一次（20）。</summary>
        public int SewerEvery;
        /// <summary>臭水沟：一次掉几个（1/层 ⇒ 1/2）。</summary>
        public int SewerDrops;
        /// <summary>臭水沟：每个吃"水壶伤害"的该比例（0.60）。</summary>
        public float SewerPct;
        /// <summary>海浪：攻击时的移速加成比例（0.50）。</summary>
        public float WaveSpeedPct;
        /// <summary>海浪：加成持续秒数（1.0）。</summary>
        public float WaveSeconds;
        /// <summary>海浪：浪的半径（米；每层 +50% 的宽度）。</summary>
        public float WaveRadius;
        /// <summary>海浪：被浪扫到的敌人吃"水壶伤害"的该比例（0.30）。</summary>
        public float WaveDamagePct;
        /// <summary>黑洞：爆炸结算前把范围内的敌人拉到爆心。</summary>
        public bool BlackHole;


        /// <summary>用于自检/日志：这份修正是从几个插件聚合来的。</summary>
        public int SourceCount;

        /// <summary>这一份修正里有没有"命中之后"的机制（有的话攻击路径要调 `PluginPostHit`）。</summary>
        /// <remarks>★ v253：回响 / 弹射 / 收束 / 潮汐 / 催熟 全部走同一个"命中之后"钩子 ⇒ 都算进来 ✓</remarks>
        public bool HasPostHit => BurnLayersPerHit > 0 || SlowLayersPerHit > 0 || LifestealPct > 0f
                                  || EchoPct > 0f || RicochetPct > 0f || FocusEvery > 0
                                  || TidePush > 0f || RipenSeconds > 0f;

        /// <summary>这一份修正有没有改"攻击形式"（剑气 / 多刀 / 多柱 / 多炸 / 加宽 / 自充 / 毒区 / 免耗）。</summary>
        public bool HasFormChange => SwordWaveCount > 0 || ExtraSwings > 0 || ExtraJets > 0
                                     || BeamWidthPct > 0f || AutoChargePerSecond > 0f
                                     || SubBurstCount > 0 || PoisonLayers > 0
                                     || FreeBurstChance > 0f || ChainBurstCount > 0;

        /// <summary>一次挥击**总共挥几刀**（1 = 普通；双击 = 2）。</summary>
        public int SwingCount => 1 + Mathf.Max(0, ExtraSwings);
        /// <summary>一次喷水**总共几道**水柱（1 = 普通；多柱 = 2）。</summary>
        public int JetCount => 1 + Mathf.Max(0, ExtraJets);
        /// <summary>一次投掷**总共几次**爆炸（1 = 普通；连环 = 2）。</summary>
        public int BlastCount => 1 + Mathf.Max(0, ChainBurstCount);

        public static WeaponModifiers None => default;
    }

    /// <summary>
    /// **武器与插件的统一效果处理**。
    ///
    /// 规格（阶段 3）：建立武器攻击流程、插件安装流程与**统一的攻击效果处理** ✓
    ///
    /// 三个消费点（全部在 `RunSession`）：
    ///   · `SwingHoe`   → 锄头（近战判定框 + 剑气流 + 双击的第二刀 + 吸血回调）
    ///   · `FireBeam`   → 水壶激光（多柱 / 加粗 / 减速 / 自充在 `TickBeam` 里扣水）
    ///   · `ExplodeAt`  → 肥料爆炸（连环的第二爆点 / 子爆 / 毒区 / 免耗在 `UseTool` 里）
    /// 另有 `RunSession.PluginPostHit` 统一处理命中的副作用（灼烧 / 减速 / 吸血）✓
    /// </summary>
    public static class WeaponSystem
    {
        // ── 规格给死的两个比例（写在代码里，因为它们描述的是"形状"而不是"强度"）──

        /// <summary>
        /// 子爆落点：距主爆心 = 主爆半径 × 本值。
        ///
        /// ★ v189：0.9 → **1.75**（用户需求⑤："子爆炸与主爆炸间距增加"）★
        /// 旧值 0.9 让子爆**整个压在**主爆里面：
        ///   主爆半径 R、子爆半径 0.7R，圆心只隔 0.9R ⇒ 子爆的中心离主爆心还不到一个主爆半径，
        ///   两个圈几乎完全重叠 ⇒ 画面上读成"一个更大的爆"，而玩家报的正是"子爆看不出来" ✓
        /// 新值的判据是**相切**：两圆不重叠需要圆心距 ≥ R + 0.7R = **1.7R** ✓
        ///   取 1.75R（= 1.7R × 1.03 余量，与 `SwordWaveNoOverlapMargin` / `BeamNoOverlapMargin`
        ///   那两处"留一点余量"的写法同一个习惯）⇒ 子爆真的**落在主爆外面** ✓
        /// ★ 顺带把"子爆彼此也不重叠"一起保证了 ★
        ///   相邻两个子爆夹角 60° ⇒ 弦长 = 2·1.75R·sin30° = 1.75R > 2·0.7R = 1.4R ✓
        ///   （旧值 0.9R 时弦长 0.9R，6 个子爆挤成一圈互相咬住 ✗）
        /// </summary>
        public const float SubBurstRingRatio = 1.75f;
        /// <summary>子爆按**正六边形**布角 ⇒ 每 60° 一个（规格 0°/60°/…/300°）。</summary>
        public const float SubBurstAngleStep = 60f;
        /// <summary>毒区半径 = 主爆 × 本值（规格 0.8）。</summary>
        public const float PoisonRadiusRatio = 0.8f;

        /// <summary>
        /// **把某把武器上的全部插件聚合成一份修正**。
        ///
        /// ★ 这是唯一的聚合点 ★
        ///
        /// 聚合规则（三条，缺一不可）：
        ///   ① **互斥**：`exclusivePerWeapon` 的插件，同一武器只取**第一件** ✗
        ///      不做的话"同类技能型插件装 3 个"会让效果翻三倍 ✓
        ///   ② **叠加上限**：按 `maxStack` 截断 ✗ 不做的话 `Stacks` 会无限涨 ✓
        ///   ③ **同类累加**：两个"多一柱"是 **+2 道**，不是取 max ✗
        ///      取 max 的话"再装一个同款"会没有任何效果 —— 玩家会以为插件坏了 ✓
        ///
        /// 纯函数：不改任何状态 ⇒ 可离线断言 ✓（当前环境下最可靠的验证方式）
        ///
        /// ★ 品质不在这里乘 ★
        /// 武器品质由攻击路径的 `ToolMult(...)` 负责 ✗ 这里再乘一次就是**双重计费** ✓
        /// </summary>
        public static WeaponModifiers Aggregate(PlayerGoods goods, WeaponSlot slot)
        {
            WeaponModifiers m = WeaponModifiers.None;
            if (goods == null || slot == WeaponSlot.None) return m;

            List<PluginInstance> list = goods.Plugins;
            for (int i = 0; i < list.Count; i++)
            {
                PluginInstance pi = list[i];
                if (pi == null || pi.Asset == null || pi.Slot != slot) continue;

                // ① 互斥：同一武器只认**第一件** ✓
                // ★ 为什么是往回扫一遍，而不是 `new HashSet` ★
                // 这个函数每次攻击都调（激光是**每帧**调），HUD 的落点准星也每帧读它 ✗
                // 在里面 new 一个 HashSet 就是每帧一次堆分配 ⇒ 长期跑会一直给 GC 喂垃圾 ✓
                // 插件总数 ≤ 6×3 = 18，往回扫是 O(n²)=最多 324 次比较，代价可忽略 ✓
                if (pi.Asset.exclusivePerWeapon && SeenExclusiveBefore(list, i, slot, pi.Asset)) continue;

                // ② 叠加上限（至少 1 层，即使 maxStack 配错）
                int stacks = Mathf.Clamp(pi.Stacks, 1, Mathf.Max(1, pi.Asset.maxStack));
                float total = pi.Asset.amount * stacks;

                // ③ 分派 —— **只改攻击方式，不改玩家属性**
                switch (pi.Asset.effect)
                {
                    // ── 锄头 ──
                    case PluginEffectKind.SwordWave:
                        m.SwordWaveCount += Mathf.RoundToInt(total);       // amount = 条/层
                        break;
                    case PluginEffectKind.BurnOnHit:
                        m.BurnLayersPerHit += stacks;                      // 命中附"插件层数"层灼烧
                        m.BurnRatioPerLayer += pi.Asset.amount;            // amount = 每层每秒比例
                        break;
                    case PluginEffectKind.DoubleStrike:
                        m.ExtraSwings += Mathf.RoundToInt(total);          // amount = 额外刀数/层
                        m.DoubleStrikeLayers += stacks;                    // ★ ④ 层数 ⇒ 每刀 ×2/3 的次数
                        break;
                    case PluginEffectKind.Lifesteal:
                        m.LifestealPct += total;                           // amount = 0.20/层
                        break;

                    // ── 水壶 ──
                    case PluginEffectKind.MultiJet:
                        m.ExtraJets += Mathf.RoundToInt(total);            // amount = 道/层
                        break;
                    case PluginEffectKind.SlowOnHit:
                        m.SlowLayersPerHit += stacks;
                        m.SlowPctPerLayer += pi.Asset.amount;              // amount = 每层减速 0.12
                        m.SlowDamageRatio += pi.Asset.amount2;             // amount2 = 每层每秒比例 0.50
                        break;
                    case PluginEffectKind.ThickJet:
                        m.BeamWidthPct += total;                           // amount = 0.50/层
                        break;
                    case PluginEffectKind.AutoCharge:
                        m.AutoChargePerSecond += total;                    // amount = 10 点/层
                        m.AutoChargeLayers += stacks;                      // ★ ④ 层数 ⇒ 水球数（2 颗/层）
                        break;

                    // ── 肥料 ──
                    case PluginEffectKind.SubBurst:
                        m.SubBurstCount += Mathf.RoundToInt(total);        // amount = 子爆数/层
                        // ★ v189：半径比是**比例**，再叠一层不该变成 1.4 / 2.1 倍 ★
                        //   改前是 `+=` ⇒ 3 层时子爆半径 = 主爆 ×2.1（比主爆还大），
                        //   而同一个比例还被 `SubBurstDamage` 当伤害比用 ⇒ 子爆伤害 = 主爆 ×2.1 ✗
                        //   `plugins.json` 写的是"半径 = 主爆 ×0.70"，与层数无关 ⇒ 取 **max**（= 那个比例本身）✓
                        //   层数只加"几个子爆"（`SubBurstCount`），不加"每个多大" ✓
                        m.SubBurstRadiusRatio = Mathf.Max(m.SubBurstRadiusRatio, pi.Asset.amount2);
                        break;
                    case PluginEffectKind.PoisonZone:
                        m.PoisonLayers += stacks;
                        m.PoisonRatioPerLayer += pi.Asset.amount;          // amount = 每层每秒比例 0.50
                        break;
                    case PluginEffectKind.FreeBurst:
                        m.FreeBurstChance += total;                        // amount = 0.12/层
                        break;
                    case PluginEffectKind.ChainBurst:
                        m.ChainBurstCount += Mathf.RoundToInt(total);      // amount = 次数/层
                        m.ChainBurstGap += pi.Asset.amount2;               // amount2 = 2.5m
                        break;

                    // ── ★ v253：六个全新机制 ──
                    //   每个都只往**自己那一格**写，不与上面 12 个的字段混用（自检 `PlugField` 会逐件核对）
                    case PluginEffectKind.Echo:
                        m.EchoPct += total;                                // amount = 每次延迟结算的比例
                        m.EchoDelay = Mathf.Max(m.EchoDelay, pi.Asset.amount2);   // amount2 = 延迟秒数
                        break;
                    case PluginEffectKind.Ricochet:
                        m.RicochetPct += total;                            // amount = 跳射保留比例
                        m.RicochetRadius = Mathf.Max(m.RicochetRadius, pi.Asset.amount2);  // amount2 = 搜索半径
                        break;
                    case PluginEffectKind.Ripen:
                        m.RipenSeconds += total;                           // amount = 每次推进的成长秒数
                        break;
                    case PluginEffectKind.Focus:
                        m.FocusEvery = Mathf.Max(m.FocusEvery, Mathf.RoundToInt(pi.Asset.amount));  // amount = 几次一击
                        m.FocusBonusPct += pi.Asset.amount2;               // amount2 = 追加比例
                        break;
                    case PluginEffectKind.Tide:
                        m.TidePush += total;                               // amount = 推开距离（米）
                        break;

                    // ── ★ v255：肥料三件 ──
                    case PluginEffectKind.Mine:
                        // amount = 踩中倍率、amount2 = 超时倍率
                        // 层数只缩上膛时间、扩感应半径（**不叠倍率** —— 否则 3 层 450% 一击秒怪）
                        m.MineHitPct = Mathf.Max(m.MineHitPct, pi.Asset.amount);
                        m.MineMissPct = Mathf.Max(m.MineMissPct, pi.Asset.amount2);
                        m.MineArmTime = Mathf.Max(0.1f, 0.6f - 0.15f * (stacks - 1));
                        m.MineSenseRadius = 2.2f + 0.3f * (stacks - 1);
                        break;
                    case PluginEffectKind.Ward:
                        // amount = 护盾点数、amount2 = 持续秒数（**取 max 不累加**：maxStack = 1）
                        m.WardShield = Mathf.Max(m.WardShield, pi.Asset.amount);
                        m.WardSeconds = Mathf.Max(m.WardSeconds, pi.Asset.amount2);
                        break;
                    case PluginEffectKind.Mark:
                        m.MarkBonusPct += pi.Asset.amount;                 // amount = 每层放大比例
                        m.MarkSeconds = Mathf.Max(m.MarkSeconds, pi.Asset.amount2);
                        break;

                    // ── ★ v290：七个全新机制 ──
                    //   数值全部读 `PluginExtra2` 的常量（唯一真相）；这里只负责"乘层数"与"写格子"。
                    //   三条判据与上面 21 件一致：
                    //     ① 能叠层的**乘层数**（感电目标数 / 狂暴百分比 / 臭水沟掉落数 / 海浪宽度）
                    //     ② 描述"形状/阈值"的**取常量本身**（内径比 / 残血阈值 / 每 N 点水 / 收益系数）
                    //     ③ 唯一件（`stackRule: Unique`）的布尔标记直接置真 ✓
                    case PluginEffectKind.Shock:
                        m.ShockTargets = PluginExtra2.ShockTargetsPerStack * stacks;
                        m.ShockPct = PluginExtra2.ShockDamagePct;
                        break;
                    case PluginEffectKind.Frenzy:
                        m.FrenzyPct = PluginExtra2.FrenzyPctPerStack * stacks;
                        m.FrenzyBelow = PluginExtra2.FrenzyBelowFraction;
                        break;
                    case PluginEffectKind.Giant:
                        m.GiantRing = true;
                        m.GiantInnerRatio = PluginExtra2.GiantInnerRatio;
                        break;
                    case PluginEffectKind.Sewer:
                        m.SewerEvery = PluginExtra2.SewerWaterPerDrop;
                        m.SewerDrops = PluginExtra2.SewerDropsPerStack * stacks;
                        m.SewerPct = PluginExtra2.SewerDamagePct;
                        break;
                    case PluginEffectKind.Wave:
                        m.WaveSpeedPct = PluginExtra2.WaveSpeedPct;
                        m.WaveSeconds = PluginExtra2.WaveSeconds;
                        // ★ 宽度：基础 + 每层 50%（★v313 起 1 层 4.5 / 2 层 6.75 / 3 层 9.0 米）★
                        m.WaveRadius = PluginExtra2.WaveRadiusBase
                                       * (1f + PluginExtra2.WaveWidthPerStack * (stacks - 1));
                        m.WaveDamagePct = PluginExtra2.WaveDamagePct;
                        break;
                    case PluginEffectKind.BlackHole:
                        m.BlackHole = true;
                        break;

                    case PluginEffectKind.None:
                        continue;      // 无效行既不算层数也不占台账 ✗
                }
                m.SourceCount++;
            }
            return m;
        }

        /// <summary>
        /// 第 <paramref name="index"/> 件之前，是不是已经有一件**同一个资产、同一把武器**的插件了。
        /// 给互斥规则用（见 `Aggregate` 的 ①）。无分配 ✓
        /// </summary>
        private static bool SeenExclusiveBefore(List<PluginInstance> list, int index, WeaponSlot slot, PluginAsset asset)
        {
            for (int j = 0; j < index; j++)
            {
                PluginInstance pj = list[j];
                if (pj != null && pj.Asset == asset && pj.Slot == slot) return true;
            }
            return false;
        }

        // ────────────────────────────────────────────────────────────────────
        // 各档效果的**纯算式**：把"多打多少 / 每秒多少 / 落在哪"抽成可离线断言的函数 ✓
        // 攻击路径只负责"谁来挨这一下"，不负责决定数值 ✗
        // ────────────────────────────────────────────────────────────────────

        // ────────────────────────────────────────────────────────────────────
        // ★★ v279：三种持续伤害 —— **口径统一成"造成它的那把武器、这一次的实际伤害"** ★★
        //
        // 用户原话："燃烧、减速、中毒伤害都修改为继承自对应武器的伤害百分比，而不是继承属性"
        //
        // ── 改前（三个函数的**签名早就是对的**，错的是调用方传进来的东西）──
        //   `BurnPerLayerPerSecond(float meleeDamage, m)` / `SlowPerLayerPerSecond(float rangedDamage, m)`
        //   形参名写着 `meleeDamage` / `rangedDamage`，但 `RunSession.PluginPostHit` 传的是
        //   **属性**（`Stats.Get(StatId.MeleeDamage / RangedDamage)`）⇒ 名字骗了读者：
        //   读代码的人以为"按伤害"，实际上是"按属性"✗
        //   实机读数（`plugins.json` 那三件的描述也照这个写的）：开局近战属性 0 ⇒ 灼烧每秒 **0** ⇒
        //   `PluginDotTable.ApplyBurn` 在 `perLayerPerSecond <= 0` 处早退 ⇒ **连行都不建**（DoT 静默消失）✓
        //   而毒区走的是 `ExplodeAt` 的 `explodeDmg`（这一炮的实际伤害）⇒ 三种口径本来就不一致 ✗
        //
        // ── 改后 ──
        //   比例只有一处出处：`tuning.json` 的 `dots.burnPercent / slowPercent / poisonPercent`
        //   （`TuningDef.DotsBurnPercent` 等，`≤ 0` 回落 0.5）
        //   ⇒ 三个函数**签名不变**（4 个探针/自检调用点因此不用改），`m` 退化成**未使用形参**：
        //     与 `BeamWaterCostPerFire(int, in WeaponModifiers)` 的处置同一个先例（那里 `m` 也不再参与算式）✓
        //     为什么保留 `m`：删掉会让 `ProbeV199Weapons` / `SelfCheck` 的既有调用点编译不过，
        //     而"比例从哪来"这件事已经由**函数体**回答，靠签名回答反而会多出一个可漂移的数 ✓
        //   `m.BurnRatioPerLayer / SlowDamageRatio / PoisonRatioPerLayer` 仍由 `Aggregate` 从
        //   `plugins.json` 写入 —— 它们现在只作"插件自述比例"（自检 `PlugField` 的活体判定）✓
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 灼烧**每层**每秒的伤害 = **锄头这一刀的实际伤害** × `dots.burnPercent`（默认 0.50）。
        /// ★ v279：口径从"近战属性"改成"这一刀打出的伤害"（见上面那一节）★
        /// </summary>
        public static float BurnPerLayerPerSecond(float weaponHitDamage, in WeaponModifiers m)
            => Mathf.Max(0f, weaponHitDamage) * DotsPercent(DotsKindForTuning.Burn);

        /// <summary>
        /// 减速**每层**每秒的伤害 = **水壶这一束的实际伤害** × `dots.slowPercent`（默认 0.50）。
        /// ★ v279：口径从"远程属性"改成"这一束打出的伤害" ★
        /// </summary>
        public static float SlowPerLayerPerSecond(float weaponHitDamage, in WeaponModifiers m)
            => Mathf.Max(0f, weaponHitDamage) * DotsPercent(DotsKindForTuning.Slow);

        /// <summary>
        /// 毒区**每层**每秒的伤害 = **肥料这一炮的实际伤害** × `dots.poisonPercent`（默认 0.50）。
        /// ★ v279：调用方本来就传的是实际伤害（`RunSession.ExplodeAt` 的 `explodeDmg` / 子爆的 `subDmg`），
        /// 本轮只把**比例**从 `plugins.json` 的 `amount` 搬到 `tuning.dots` 一处 ★
        /// </summary>
        public static float PoisonPerLayerPerSecond(float blastHitDamage, in WeaponModifiers m)
            => Mathf.Max(0f, blastHitDamage) * DotsPercent(DotsKindForTuning.Poison);

        /// <summary>内部用：区分要读哪一档比例（只是为了让下面那一个取数口有三个入口）。</summary>
        private enum DotsKindForTuning { Burn, Slow, Poison }

        /// <summary>三个比例的唯一取数口；表未加载时回落到 `DotsDef` 的字段初始值 0.5。</summary>
        private static float DotsPercent(DotsKindForTuning which)
        {
            TuningDef t = TableDatabase.Tuning;
            if (t == null) return 0.5f;
            switch (which)
            {
                case DotsKindForTuning.Burn: return t.DotsBurnPercent;
                case DotsKindForTuning.Slow: return t.DotsSlowPercent;
                default: return t.DotsPoisonPercent;
            }
        }

        /// <summary>
        /// ★ v279：**单次**持续伤害结算的上限 = 目标 `MaxHp × dots.tickMaxHpFraction`（默认 25%）★
        ///
        /// 执法点唯一：`PluginDotTable.Hit`（每一次"满 1 秒"结算、**逐目标**夹紧）。
        /// 为什么不在施加时（`ApplyBurn / AddPoisonZone`）夹：
        ///   毒区是**按位置**的，踩进来的敌人血量各不相同 —— 施加时夹一次只能保护"当时想到的那只"✗
        /// `MaxHp ≤ 0`（还没 `Setup` 的对象）⇒ 返回 0 = **不夹**（由 `Hit` 判 `cap > 0`），
        ///   否则一个没初始化血量的目标会被夹成 0 伤害 ✓
        /// </summary>
        public static float DotTickCap(float targetMaxHp)
        {
            if (!(targetMaxHp > 0f)) return 0f;
            TuningDef t = TableDatabase.Tuning;
            float frac = t != null ? t.DotsTickMaxHpFraction : 0.25f;
            return targetMaxHp * frac;
        }

        /// <summary>吸血回到生命的量 = 伤害 × 比例（上限由 `HealthPool.Heal` 收到 `MaxHp`）。</summary>
        /// <remarks>
        /// ★ v282：本函数**仍然是"一次命中"的逐字算式**，一个字没改 ★
        /// 它回答"这一下（这一份伤害）该折多少血"，不回答"总共该回多少"。
        /// 群怪叠加的修复不在系数上，而在**结算批次**上：见 `LifestealSettle`。
        /// </remarks>
        public static float LifestealHeal(float damage, in WeaponModifiers m)
            => Mathf.Max(0f, damage) * Mathf.Max(0f, m.LifestealPct) * (m.LifestealPct > 0f ? 1f : 0f);

        // ════════════════════════════════════════════════════════════════════
        // ★★ v282：吸血的**批次结算 + 两条上限** ★★
        //
        // 用户反馈：「吸血类插件太强，在面对大量敌人时回血过多」。
        //
        // ── 病因（结构性，确认自代码）──
        //   改前四个攻击循环各自**逐敌**调 `RunSession.PluginPostHit` ⇒ 每敌各回一份，
        //   群怪时回血量 ∝ 敌人数，再叠加多段（双击 / 剑气 / 多柱）再乘一次 ✗
        //
        // ── 修法 ──
        //   ① 同一个"攻击事件"内的全部命中先**累加伤害**，事件结束时按**总伤害**结算**一次**
        //      ⇒ 一刀五怪 = 一份（不是五份）。
        //   ② 结算再被两条上限夹住：单次事件 ≤ `MaxHp × perSwing`，每秒桶 ≤ `MaxHp × perSecond`。
        //
        // 三个函数都是**纯函数**（不读时间、不写状态），所以上限口径可以单独复算，
        // 不需要跑起来才知道数对不上 ✓ 状态（累计值 / 秒桶）在 `RunSession` 里。
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 一次攻击事件的吸血上限 = 玩家 `MaxHp × tuning.lifesteal.perSwingMaxHpFraction`（默认 5%）。
        /// `MaxHp ≤ 0`（还没 `Setup`）或比例 `≤ 0` ⇒ 返回 `+∞`（= **不夹**，退化成改前行为）。
        /// </summary>
        public static float LifestealSwingCap(float ownerMaxHp)
        {
            if (!(ownerMaxHp > 0f)) return float.PositiveInfinity;
            TuningDef t = TableDatabase.Tuning;
            float frac = t != null ? t.LifestealPerSwingFraction : 0.05f;
            return frac > 0f ? ownerMaxHp * frac : float.PositiveInfinity;
        }

        /// <summary>
        /// 1 秒桶内的吸血上限 = 玩家 `MaxHp × tuning.lifesteal.perSecondMaxHpFraction`（默认 3%）。
        /// 退化条件与 `LifestealSwingCap` 相同（`+∞` = 不夹）。
        /// </summary>
        public static float LifestealSecondCap(float ownerMaxHp)
        {
            if (!(ownerMaxHp > 0f)) return float.PositiveInfinity;
            TuningDef t = TableDatabase.Tuning;
            float frac = t != null ? t.LifestealPerSecondFraction : 0.03f;
            return frac > 0f ? ownerMaxHp * frac : float.PositiveInfinity;
        }

        /// <summary>
        /// **一次攻击事件**的吸血结算：`sumDamage × pct × hpUnit`，再依次夹「单次上限」与「本秒余额」。
        ///
        /// 参数里没有"第几个敌人"—— 这正是修法的全部：批次里已经有**总伤害**，
        /// 所以结算次数与敌人数**无关** ✓
        /// </summary>
        /// <param name="totalDamage">本批次累计的伤害（敌人 HP 域，多个敌人的和）</param>
        /// <param name="pct">本批次的吸血比例（= `plugins.json` 的 `amount × 层数`）</param>
        /// <param name="hpUnit">敌我血池换算单位（`EnemyDef.PlayerHpUnit`）</param>
        /// <param name="swingCap">单次事件上限（`LifestealSwingCap`；`+∞` = 不夹）</param>
        /// <param name="secondLeft">本秒桶的剩余额度（`LifestealSecondCap - 已回`；`+∞` = 不夹）</param>
        /// <returns>实际要回的血量（玩家血池域）；`≤ 0` 表示这一批不回血</returns>
        public static float LifestealSettle(float totalDamage, float pct, float hpUnit,
                                            float swingCap, float secondLeft)
        {
            float raw = Mathf.Max(0f, totalDamage) * Mathf.Max(0f, pct) * Mathf.Max(0f, hpUnit);
            if (raw <= 0f) return 0f;
            // 两条上限都是"取更小的那一个" ⇒ 顺序无关，不会因为先夹哪条而漏血 ✓
            // `+∞` 与有限值比 `Mathf.Min` 取有限值 ⇒ "关闭上限"不需要额外分支 ✓
            float capped = Mathf.Min(raw, Mathf.Max(0f, secondLeft));
            capped = Mathf.Min(capped, swingCap);
            return Mathf.Max(0f, capped);
        }

        /// <summary>不消耗充能的概率，封顶 100%（叠到 9 层也还是 1.0）。</summary>
        public static float FreeBurstChanceOf(in WeaponModifiers m)
            => Mathf.Clamp01(m.FreeBurstChance);

        /// <summary>水柱**半宽**（判定用）= 基础半宽 ×(1+加粗)。★ 只改宽，长度由射程决定 ★</summary>
        public static float BeamHalfWidth(float baseHalfWidth, in WeaponModifiers m)
            => Mathf.Max(0.01f, baseHalfWidth) * (1f + Mathf.Max(0f, m.BeamWidthPct));

        /// <summary>
        /// ★★ v217：**多柱 = 同一份水打更多道** —— 一次喷射的总耗**不随道数变** ★★
        ///
        /// 这份语义的**唯一出处**（`BeamWaterCostPerFire` 按它决定乘不乘道数）✓
        ///
        /// 用户原话（本轮）："那就是多柱额外生成的攻击导致消耗量翻倍了，修复bug"。
        ///
        /// ── 三代口径（写清演进，免得下一轮又改回去）──
        ///   · v197 ✗ 每道 **+5**：1 道 20 ／ 2 道 **50** ／ 3 道 **75** ／ 4 道 **100**
        ///     （水箱上限就是 100 ⇒ 4 道喷一次扣光）
        ///   · v214 ✗ "不额外加价"但**每道仍各付一份**：1 道 20 ／ 2 道 **40** ／ 3 道 **60** ／ 4 道 **80**
        ///     ⇒ 玩家报"多柱额外生成的攻击导致消耗量翻倍了" —— 这正是 v217 要治的那条：
        ///     多柱的本意是"一次喷出好几道"，不是"把水费按道数翻倍"，后者等于**放大**而不是**加强** ✗
        ///   · v217 ✓ **一次喷射的总耗 = 水壶本体（20），几道都一样**：
        ///     1/2/3/4 道都是 **20** ⇒ 水箱 100 可以喷 **5 次**（改前 4 道只够 1.25 次）✓
        ///
        /// ⚠️ 代价（**必须**记下来）：多柱因此变成"零水费的多目标覆盖"，
        ///   它的强度上限完全由**扇形覆盖**给 —— `RunSession.FireBeam` 的夹角
        ///   `BeamFanStepDeg(halfWidth, range)` 与"同一目标只吃一次"（`_beamHitCd` 按敌人 id）
        ///   共同决定"多几道 = 多几个目标"，**同一敌人不会因为被几道罩住而多吃伤害** ✓
        ///   若日后要给它加代价，应该加在**覆盖**或**射程**上，别再回到"按道收水" ✗
        /// </summary>
        public const bool MultiJetSharesOneCharge = true;

        /// <summary>
        /// ★ v197 的"每道 + X 水" — **已彻底退役**（v214 归零，v217 连乘法一起没了）★
        ///
        /// 为什么这个常量还留着（不是"还在生效"）：
        ///   与 `ShopSystem.RerollMaxMult` 同一个处置 —— **退役值**。
        ///   `Assets/Scripts/Debug/ProbeV197.cs:143` 仍在**打印**它（"每道加 N"），
        ///   删掉会让那个探针编译不过（本单不允许改探针）⇒ 保留并在此声明已退役 ✓
        ///   **判据**：`BeamWaterCostPerFire` 一个字节都不读它 ✓ 现语义见 `MultiJetSharesOneCharge`。
        /// </summary>
        public const int MultiJetExtraWaterCost = 0;

        /// <summary>
        /// **一次喷射扣多少水** —— ★ v217：**就是单道单价，不乘道数** ★
        ///
        /// 口径只有这一处（`RunSession.TickBeam` 只负责把钱扣掉；HUD 的"耗水"行也读它）✓
        ///
        /// ★ 为什么签名里还留着 `m` ★
        ///   4 个调用点（`RunSession.TickBeam` / `ImGuiHud.DrawMenuWeaponCard` /
        ///   `DebugApi.JetWaterReport` ×3 / `ProbeV197`）里有三个文件**不在本轮可改范围** ⇒
        ///   改签名会让它们编译不过。参数保留、**不参与算式**；
        ///   将来若真要按道加价，只改这一个函数体即可（不要在各调用点各写一份 ✗）✓
        ///
        /// ★ 为什么不再兜"道数 ≥ 1" ★
        ///   不乘道数 ⇒ 道数根本不进算式 ⇒ 没有"免费喷水"的除零/为负风险 ✓
        ///   单价仍兜 `max(0, …)`：配置写成 0/负 时不会算出负水耗 ✓
        /// </summary>
        public static int BeamWaterCostPerFire(int baseCostPerJet, in WeaponModifiers m)
        {
            // ★ v217：**这里没有乘法** —— 多柱是"同一份水打更多道"（见 `MultiJetSharesOneCharge`）★
            return Mathf.Max(0, baseCostPerJet);
        }

        // ────────────────────────────────────────────────────────────────────
        // ④ **自充的新功能：耗水时发射水球**（用户需求原话）
        //
        //   "每次消耗水分时发射 2 颗水球攻击距离最近的敌人（可重复插件层数 ⇒ 增加水球数量），
        //    水球伤害为远程伤害的 50%，顺带制作水球特效"
        //
        // ★ 三条都抽成**纯算式**，因为它们全是"可离线断言的数"★
        //   （发数只看层数、伤害只看命中伤害 × 0.50 —— 都与"玩家在哪、敌人是谁"无关）
        // ────────────────────────────────────────────────────────────────────

        /// <summary>**每层**自充在每次耗水时发射的水球数（2 颗/层）。</summary>
        public const int WaterBallsPerLayer = 2;

        /// <summary>水球伤害占"水壶一次命中的远程伤害"的比例（0.50 = 50%）。</summary>
        public const float WaterBallDamageRatio = 0.50f;

        /// <summary>
        /// 一次耗水要发射几颗水球 = **自充层数 × 2**（没装 ⇒ 0）。
        /// ★ 层数封顶由 `maxStack` 负责（`can_autocharge` 是 3 ⇒ 最多 6 颗/次）★
        /// </summary>
        public static int WaterBallCount(in WeaponModifiers m)
            => Mathf.Max(0, m.AutoChargeLayers) * WaterBallsPerLayer;

        /// <summary>
        /// 水球伤害 = **水壶一次命中的远程伤害 × 0.50**。
        ///
        /// ★ "远程伤害"取的是**那一次命中的伤害值**，不是一个叫 `RangedDamage` 的裸属性 ★
        /// 依据是本工程既有的同一套惯例：`BurnPerLayerPerSecond(meleeDamage)`（灼烧 = 近战命中 × 0.50）、
        /// `SlowPerLayerPerSecond(rangedDamage)`（减速 = 远程命中 × 0.50）都是把
        /// **命中值**乘比例，而不是把属性乘比例 ✓
        /// 若只乘 `StatId.RangedDamage`（`tuning.player.rangedDamage` 开局是 **0**），
        /// 水球在开局就是 **0 伤害** ⇒ 玩家眼里"插件装了但打不死人"✗
        /// 换算关系：命中值 = (水壶基础值 `tools.can.baseValue`(3) + 远程伤害) × 伤害倍率
        /// ⇒ 水球 = 命中值 × 0.50 ⇒ **远程伤害每 +1，水球就 +0.50**（需求里的那条斜率）✓
        /// </summary>
        public static float WaterBallDamage(float rangedHitDamage)
            => Mathf.Max(0f, rangedHitDamage) * WaterBallDamageRatio;

        /// <summary>子爆半径 = 主爆半径 × 比例（`plugins.json` 的 `fert_subburst.amount2`，v192 起 = 0.50）。</summary>
        public static float SubBurstRadius(float mainRadius, in WeaponModifiers m)
            => Mathf.Max(0f, mainRadius) * Mathf.Max(0f, m.SubBurstRadiusRatio);

        /// <summary>
        /// ★★ v192：**子爆伤害 / 主爆伤害** 的比例（表值 `formula.subBurstDamageRatio`，= 0.50）★★
        ///
        /// 用户需求⑤原话："修改爆炸子爆炸的爆炸范围，缩小为正常爆炸的 1/2，**伤害基础爆炸伤害的 50%**"。
        ///
        /// ── 改前是什么（以及它为什么必须改）──
        /// v189 的写法是 `子爆伤害 = 主爆 × m.SubBurstRadiusRatio`（0.70）—— 因为那轮规格**只给了半径**，
        /// 于是拿半径比当伤害比用，理由写着"小一圈的爆配小一圈的伤害" ✓ 那是一句临时约定，
        /// 不是一条独立规格。本轮用户给了**两个数** ⇒ 两个比例必须分开，
        /// 否则"把半径调成 1/2"会**顺手把伤害也调成 1/2**，两件事永远绑在一起 ✗
        ///
        /// ── "基础爆炸伤害"取的是哪个数（查证过）──
        /// `DamageResolver.WeaponBlastDamage(weaponBase, damageStat, damageMult, crit, critDmg, min)`：
        /// 它唯一的"基础"入参是 `weaponBase`（= `tuning.tools.fertilizer.baseValue` = **5**，v195 刻度）。
        /// 若按字面取 `5 × 50% = 2.5`，子爆伤害就成了**常数 2.5**、完全不吃
        /// `BlastDamage / AtkMult / 暴击` —— 后期会退化成"可以忽略的挠痒"✗
        /// （v195：`ExplodeDamage` 那条乘区已随属性删除；这行注释同步去掉它，
        ///   否则读代码的人会去找一个**不存在的参数** ✗）
        /// 而"基础爆炸伤害"在玩家嘴里指的就是**这一口爆打出的伤害**（吃满乘区的那一个数）。
        /// ⇒ 这里取 `主爆伤害 × 0.5`，比例**恒为 0.50**（见 `ProbeV192Num` 的读数：
        ///   主爆 12.50 → 子爆 6.25，两者比 0.500）。
        /// ⚠️ 若裁决认为要"常数 2.5"，只需把 `explodeDmg` 换成 `fertTool.baseValue` 传进来 —— 一处改动 ✓
        ///
        /// `≤ 0` 回落 0.5（表里没这个键时 JsonUtility 给 0，直接乘会把子爆伤害变成 0）
        /// </summary>
        public static float SubBurstDamageRatio
        {
            get
            {
                TuningDef t = TableDatabase.Tuning;
                float r = t != null && t.formula != null ? t.formula.subBurstDamageRatio : 0f;
                return r > 0f ? r : 0.5f;
            }
        }

        /// <summary>子爆伤害 = 主爆伤害 × `SubBurstDamageRatio`（v192：0.50，与半径比**解耦**）。</summary>
        public static float SubBurstDamage(float mainDamage, in WeaponModifiers m)
            => Mathf.Max(0f, mainDamage) * SubBurstDamageRatio;

        /// <summary>毒区半径 = 主爆半径 × 0.8。</summary>
        public static float PoisonRadius(float mainRadius) => Mathf.Max(0f, mainRadius) * PoisonRadiusRatio;

        /// <summary>
        /// **剑气伤害 = 锄头一刀伤害 × 50%**（用户需求④："剑气只继承锄头伤害的 50%"）。
        ///
        /// ★ 改的是什么 ★
        /// 改前剑气吃的是**全额**那一刀的伤害（`DoHoeSwing` 直接把 `dmg` 传给 `FireSwordWaves`），
        /// ⇒ 一道剑气 = 一次完整的近战命中，几层剑气就是"同一刀再打几遍"✗
        /// 现在剑气是**这一刀的 50%**，与"多一道"的定位一致 ✓
        ///
        /// ★ 折减顺序（这一条决定读数）★
        /// 传进来的必须是**这一刀实际打出去的伤害**（已经吃过 `DoubleStrikeDamage`）⇒
        /// 双击 2 层时：每刀 0.444 × 锄头，剑气 = 0.444 × 0.5 = **0.222 × 锄头** ✓
        /// 若反过来先算剑气再折减，两份折减会各算一次，
        /// 乘法满足交换律 ⇒ 数值一样、**但"剑气的基线是哪把刀的伤害"这句话就不成立了** ✗
        /// </summary>
        public const float SwordWaveDamageRatio = 0.50f;

        /// <summary>剑气一道的伤害 = 锄头**这一刀**的伤害 × 0.50。</summary>
        public static float SwordWaveDamage(float hoeHitDamage)
            => Mathf.Max(0f, hoeHitDamage) * SwordWaveDamageRatio;

        /// <summary>
        /// 多次挥舞（`hoe_doublestrike`）：**每拥有一层，锄头伤害 ×2/3 一次**（累计相乘）✓
        ///
        /// 读数（用户给的形状，`layer` = 插件层数）：
        ///   0 层 ×1.000｜1 层 ×0.667｜2 层 ×0.444｜3 层 ×0.296
        /// ★ 为什么是"折减"而不是"加成" ★
        /// 这一档卖的是**刀数**（`SwingCount = 1 + 层数`）⇒
        /// 若每刀还是全额，一层就把"一次挥击的总伤害"抬到 2 倍 ✗
        /// 乘 2/3 之后总伤害是收敛的：
        ///   1 层 2 刀 ×0.667 = 1.333｜2 层 3 刀 ×0.444 = **1.333**｜3 层 4 刀 ×0.296 = 1.185
        /// ⇒ 它换来的是"分几刀结算"（每刀各判暴击、各叠一次灼烧/减速），
        ///   而不是"凭空多一倍伤害" ✓ —— 这正是用户要的"平衡调整" ✓
        /// </summary>
        public const float DoubleStrikeDamageRatioPerLayer = 2f / 3f;

        /// <summary>一次挥击里**每一刀**的伤害 = 锄头一刀的基础伤害 × (2/3)^层数。</summary>
        public static float DoubleStrikeDamage(float hoeHitDamage, int layers)
            => Mathf.Max(0f, hoeHitDamage)
             * Mathf.Pow(DoubleStrikeDamageRatioPerLayer, Mathf.Max(0, layers));

        /// <summary>
        /// **子爆的毒区半径** = 主毒区半径 × 子爆半径比（0.8 × 0.5 = **0.40 个主爆半径**，v192）。
        ///
        /// ★ 用户需求⑤："让子爆炸也召唤毒（有毒插件的情况下，范围小）"★
        /// 它跟的是**半径比**（`amount2`），不是 `SubBurstDamageRatio` —— 毒区是"圈"，不是"伤害" ✓
        /// ⇒ v192 半径比 0.70 → 0.50 之后，子毒区自动跟着缩成 0.8R × 0.5 = 0.4R（无需第二处改动）✓
        /// </summary>
        public static float SubPoisonRadius(float mainRadius, in WeaponModifiers m)
            => PoisonRadius(mainRadius) * Mathf.Max(0f, m.SubBurstRadiusRatio);

        /// <summary>
        /// 第 <paramref name="i"/> 个子爆的落点：**正六边形**布角、距主爆心 = 主爆半径 × `SubBurstRingRatio`。
        /// i = 0 在 0°、i = 1 在 60°、…、i = 5 在 300°（第 7 个开始绕回 0°）✓
        /// 抽成静态函数是为了让"多层真的落在不同位置"这条可以被自检直接断言 ✓
        /// </summary>
        public static Vector2 SubBurstCenter(Vector2 main, int i, float mainRadius)
        {
            if (i < 0) return main;
            float ang = (SubBurstAngleStep * i % 360f) * Mathf.Deg2Rad;
            return main + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (Mathf.Max(0f, mainRadius) * SubBurstRingRatio);
        }

        /// <summary>
        /// **装上某个插件**。返回是否成功。
        ///
        /// 检查顺序就是规格里"安装流程"的三步：
        ///   ① 武器类型适用 `Fits` ✓
        ///   ② 互斥检查（同武器已有同类互斥插件则拒绝）✓
        ///   ③ 叠加上限 ✓
        ///   ④ **每把武器 6 个插槽的总上限** ✓（唯一执法点在 `PlayerGoods.Acquire`，这里只把话说清楚）
        ///
        /// 复用 `PlayerGoods.InstallFromPending` —— **不另写一份购买/叠加逻辑** ✗
        /// （规格明确"不要为每种商品复制一套独立的购买逻辑" ✓）
        /// 它会先把背包里那件"待装"的挪过来，装不上就原样放回 ✓
        /// </summary>
        public static bool Install(PlayerGoods goods, PluginAsset plugin, WeaponSlot slot, out string reason)
        {
            reason = "";
            if (goods == null || plugin == null) { reason = "插件或商品栏为空"; return false; }
            if (slot == WeaponSlot.None) { reason = "没选武器"; return false; }
            if (!plugin.Fits(slot)) { reason = "该插件不适用于这把武器"; return false; }

            if (plugin.exclusivePerWeapon)
            {
                List<PluginInstance> list = goods.Plugins;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].Slot == slot && list[i].Asset == plugin)
                    { reason = "这把武器已经装过同类插件"; return false; }
            }

            // ④ 插槽上限：**在动手之前**就说清楚，而不是让 Acquire 静默返回 false ✗
            bool alreadyHere = goods.Find(plugin, slot) != null;
            if (!alreadyHere && goods.SlotsUsedOn(slot) >= PlayerGoods.MaxPluginsPerWeapon)
            {
                reason = $"这把武器的插件位已满（{goods.SlotsUsedOn(slot)}/{PlayerGoods.MaxPluginsPerWeapon}）"
                       + "——先卸下一件再装";
                return false;
            }

            if (!goods.InstallFromPending(plugin, slot))
            {
                PluginInstance have = goods.Find(plugin, slot);
                if (have != null && !have.CanStackMore)
                    reason = have.Asset.stackRule == StackRule.Unique ? "该插件唯一，不能重复安装" : "已达安装上限";
                else if (!alreadyHere && goods.SlotsUsedOn(slot) >= PlayerGoods.MaxPluginsPerWeapon)
                    reason = $"这把武器的插件位已满（{goods.SlotsUsedOn(slot)}/{PlayerGoods.MaxPluginsPerWeapon}）";
                else
                    reason = "安装失败";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 插件影响的是**攻击的哪一环**。给日志/自检/UI 共用一份说法 ✓
        /// 新增效果时**必须**在这里加一条 —— 自检有一条断言钉住"枚举值都有说法" ✓
        /// ★ 每个值都必须返回非空且不是"无"的字符串 ★：空串会让 UI 显示成"（未定义效果）"，
        /// 而那正是"已接线、已验证的效果被写成不存在"的那种 bug ✓
        /// </summary>
        public static string ChannelOf(PluginEffectKind k)
        {
            switch (k)
            {
                case PluginEffectKind.SwordWave: return "剑气（多劈一道）";
                case PluginEffectKind.BurnOnHit: return "灼烧（命中附层）";
                case PluginEffectKind.DoubleStrike: return "双击（多挥一刀）";
                case PluginEffectKind.Lifesteal: return "吸血（伤害转生命）";
                case PluginEffectKind.MultiJet: return "多柱（多一道水柱）";
                case PluginEffectKind.SlowOnHit: return "减速（命中附层）";
                case PluginEffectKind.ThickJet: return "加粗（只改宽度）";
                case PluginEffectKind.AutoCharge: return "自充（回水 + 耗水发水球）";
                case PluginEffectKind.SubBurst: return "子爆（主爆旁的副爆）";
                case PluginEffectKind.PoisonZone: return "毒素（爆点留毒区）";
                case PluginEffectKind.FreeBurst: return "免耗（概率不扣充能）";
                case PluginEffectKind.ChainBurst: return "连环（多炸一次）";
                // ★ v253：六个全新机制的一行说法（自检「每种效果都有说法」读这里）
                case PluginEffectKind.Echo: return "回响（同一下延迟再疼）";
                case PluginEffectKind.Ricochet: return "弹射（跳到第二个敌人）";
                case PluginEffectKind.Ripen: return "催熟（浇地推进成长）";
                case PluginEffectKind.Focus: return "收束（连击处决）";
                case PluginEffectKind.Tide: return "潮汐（命中推开）";
                // ★ v255：肥料三件
                case PluginEffectKind.Mine: return "伏雷（埋地待敌踩）";
                case PluginEffectKind.Ward: return "护壤（炸时给自己盾）";
                case PluginEffectKind.Mark: return "灼印（标记放大下一次伤害）";
                // ★ v290：七个全新机制的一行说法（自检「每种效果都有说法」读这里）
                case PluginEffectKind.Shock: return "感电（这一刀再电周围）";
                case PluginEffectKind.Frenzy: return "狂暴（对残血敌人加伤）";
                case PluginEffectKind.Giant: return "巨型（挥击变整圈）";
                case PluginEffectKind.Sewer: return "臭水沟（耗水掉香蕉皮）";
                case PluginEffectKind.Wave: return "海浪（脚下起浪 + 提速）";
                case PluginEffectKind.BlackHole: return "黑洞（爆炸前拉拢）";
                default: return "无";
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v237：**默认拥有的插件**（`OwnedPlugins` 初始列表）
        // ══════════════════════════════════════════════════════════════════
        //
        //  用户原话：「加 6~10 条『普通/一般』插件，默认拥有」。
        //  ⇒ 这一份就是"开局白送、不用买"的那 8 件（D/C 档，全部是 plugins.json 末尾那一批）。
        //
        //  ── 为什么"拥有"= 装进背包（`WeaponSlot.None`）而不是直接装上 ──
        //    ① 直接装会改掉**每一局**开局的三把武器读数 ⇒ `SelfCheck` 里那些
        //       "空手读数 / 单件插件读数"的断言会从根上变红（它们的夹具是 `new RunSession()`，
        //       和玩家走同一条构造路径，没法只给玩家装）✗
        //    ② `WeaponSystem.Aggregate` 只聚合 `Slot` 等于该武器的那几件 ⇒ 背包里的这几件
        //       **零副作用**（不改变任何一局的平衡），装上之后才生效 ✓
        //    ③ 玩家真要装上，走的是工程里**已有的**那条链：`RunSession.AutoInstallPending`
        //       → `WeaponSystem.Install`（买东西时自动装；`InstallPlugin` 也能指定武器）✓
        //
        //  ⚠️ 边界（写在这里，不藏）：本轮的"默认拥有"停在背包 + 自动装配链上，
        //     **没有**新增一个"点一下把背包插件装上"的 IMGUI 面板 —— 那属于 `UI/ImGuiHud.cs`（本轮禁改）。
        public static readonly string[] DefaultOwnedPlugins =
        {
            // ★ v254：清掉 30 件"基础效果拓展版"之后，开局白送的插件改为**新机制**的 C 档，
            //   保证一进战斗就有一套能用的插件（原来那 8 件全被删了）
            // ★ 本轮：「余温」（`hoe_warmth`）随用户要求删除 ⇒ 白送清单只剩「催熟」一件。
            //   ⚠️ 必须**同时**删掉这一项：表里已经没有这个 id，留着会让 `GrantDefaultOwned`
            //      每一局都打一条 "找不到（检查 id 是否拼错）" 的 Error ⇒ 悬空引用 ✓
            "can_ripen",    // 催熟     C · 浇水推进田地成长
        };

        /// <summary>
        /// 把 <see cref="DefaultOwnedPlugins"/> 里的每一件塞进 <paramref name="goods"/> 的背包
        /// （`Slot = None` = 已拥有、还没装上）。返回真的加进去几件。
        /// ★ 幂等：已经有的不重复加（同一局调两次不会变成两层）★
        /// ★ 表里查不到的 id 只报一次日志、跳过 —— 不抛、不静默（`TableDatabase.Plugins` 未载入时会走到）★
        /// </summary>
        public static int GrantDefaultOwned(PlayerGoods goods)
        {
            if (goods == null) return 0;
            PluginAsset[] all = TableDatabase.Plugins;
            if (all == null || all.Length == 0)
            {
                if (!_grantWarned)
                {
                    _grantWarned = true;
                    Log.Warn("[插件] 默认拥有：`TableDatabase.Plugins` 还没载入 ⇒ 本次没有发放"
                             + $"（{DefaultOwnedPlugins.Length} 件待发）");
                }
                return 0;
            }

            int added = 0;
            for (int i = 0; i < DefaultOwnedPlugins.Length; i++)
            {
                string id = DefaultOwnedPlugins[i];
                PluginAsset asset = null;
                for (int k = 0; k < all.Length; k++)
                    if (all[k] != null && string.Equals(all[k].id, id, System.StringComparison.Ordinal))
                    { asset = all[k]; break; }
                if (asset == null)
                {
                    Log.Error($"[插件] 默认拥有清单里的「{id}」在 plugins.json 里找不到（检查 id 是否拼错）");
                    continue;
                }
                if (goods.Find(asset, WeaponSlot.None) != null) continue;   // 幂等：背包里已经有它
                goods.Plugins.Add(new PluginInstance(asset, WeaponSlot.None));
                added++;
            }
            return added;
        }

        private static bool _grantWarned;

        /// <summary>
        /// 这一件是不是"默认拥有"那批（<see cref="DefaultOwnedPlugins"/>）。
        ///
        /// ★ v238：**它的唯一用途是把这批从"自动装配"队列里排除** ★
        /// 病因（用户报"战斗模式异常地获取了大量插件，我没有选择的前提"）：
        ///   发放走的是背包（`Slot == None`），而 `RunSession.AutoInstallPending`（买任何一件插件时都会调）
        ///   会把背包里**所有**待装件一次装完 ⇒ 那 8 件被自动装上 ⇒ 武器面板立刻 3/6 · 3/6 · 2/6 ✗
        ///   ⇒ 我上一轮说的"对平衡零影响"只在**当帧**成立，买一件插件之后就全装上了 ✓
        /// 现在：默认拥有 = **拥有但不替你装**（要装走 `RunSession.InstallPlugin`，那是玩家明确的一次选择）✓
        /// </summary>
        public static bool IsDefaultOwned(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < DefaultOwnedPlugins.Length; i++)
                if (string.Equals(DefaultOwnedPlugins[i], id, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>同上，按资产判断。</summary>
        public static bool IsDefaultOwned(PluginAsset a) => a != null && IsDefaultOwned(a.id);
    }
}
