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
    /// 这与既有的 `DamageResolver.AttackFormula` 是同一条路子：
    /// 攻击只走一个公式，插件只是往它的入参里加东西 ✓
    ///
    /// ★ v171：字段按"它改攻击的哪一环"分组 ★
    /// 上半是**数值区**（进攻击公式的某个乘区），下半是**机制区**
    /// （不在公式里，而是命中的**副作用**：灼烧 / 寒冷 / 连锁 / 回声 / 暴击炸裂）✓
    /// 两者的消费点不同 —— 数值在"算伤害之前"，机制在"打到人之后" ✓
    /// </summary>
    public struct WeaponModifiers
    {
        // ─────────────── 数值区：进 AttackFormula 与各种半径/冷却 ───────────────
        public float DamageFlat;        // 加到基础伤害
        public float DamagePercent;     // 乘算，累加后一次性用（0.3 = +30%）
        public float AttackSpeedPct;    // 攻速 +N（0.15 = 间隔 ÷1.15）
        public float RangePct;          // 近战/激光/投掷射程 +N%
        public float CritRateFlat;      // 暴击率 +N（**0..1 小数**）
        public float CritDamageFlat;    // 暴击倍率 +N
        public float ExplodeRadiusPct;  // 爆炸半径 +N%
        public int ProjectileCount;     // 弹道数 +N（按武器：剑气 / 激光 / 爆炸点）
        public float KnockbackFlat;     // 击退 +N

        // ─────────────── 机制区：命中之后的副作用 ───────────────
        /// <summary>剑气条数（&gt;0 = 挥击同时放剑气）。实际条数 = 本值 + ProjectileCount。</summary>
        public int SwordWaveCount;
        /// <summary>命中额外叠加的寒冷层数（走既有 `StatusRuntime.Chill`）。</summary>
        public int ChillPerHit;
        /// <summary>击杀回复的弹药点数（按武器进各自的弹药池）。</summary>
        public float AmmoPerKill;
        /// <summary>命中灼烧量 = 本次伤害 × 本比例（走既有 `StatusRuntime.Burn`）。</summary>
        public float BurnPerHitPct;
        /// <summary>共振连锁的跳数（最近 N 个其它敌人）。</summary>
        public int ChainTargets;
        /// <summary>共振连锁每一跳的伤害比例。</summary>
        public float ChainRatio;
        /// <summary>爆炸回声：肥料爆炸后第二段的伤害比例。</summary>
        public float BlastEchoPct;
        /// <summary>暴击炸裂：暴击时溅射的伤害比例。</summary>
        public float CritBlastPct;
        /// <summary>满血增伤：满血时伤害倍率 ×(1+本值)。</summary>
        public float FullHpBonusPct;

        /// <summary>用于自检/日志：这份修正是从几个插件聚合来的。</summary>
        public int SourceCount;

        /// <summary>伤害总倍率（含插件）。`1 + Σpercent` ✓ 品质不在这里（见 `Aggregate` 的说明）</summary>
        public float DamageMult => 1f + DamagePercent;

        /// <summary>这一份修正里有没有"命中之后"的机制（有的话攻击路径要调 PostHit）。</summary>
        public bool HasPostHit => BurnPerHitPct > 0f || ChainTargets > 0
                                  || ChillPerHit > 0 || CritBlastPct > 0f || AmmoPerKill > 0f;

        /// <summary>这一份修正有没有改"攻击形式"（剑气 / 多弹道 / 爆炸点）。</summary>
        public bool HasFormChange => SwordWaveCount > 0 || ProjectileCount > 0;

        public static WeaponModifiers None => default;
    }

    /// <summary>
    /// **武器与插件的统一效果处理**。
    ///
    /// 规格（阶段 3）：建立武器攻击流程、插件安装流程与**统一的攻击效果处理** ✓
    ///
    /// ★ v171：从"聚合好了但没人读"改成**真的接进三条攻击路径** ★
    /// 在此之前 `Aggregate` / `Install` 零调用 —— 买到的插件只进背包，没有任何战斗效果 ✗
    /// 现在三个消费点：
    ///   · `RunSession.SwingHoe`     → 锄头（近战判定框 + 剑气流）
    ///   · `RunSession.FireBeam` / `RunSession.UseTool` → 水壶激光（**两条路都要接**）
    ///   · `RunSession.ExplodeAt`    → 肥料爆炸（含爆炸点 +N 与回声）
    /// 另有 `RunSession.PostHit` 统一处理命中的副作用（灼烧/寒冷/连锁/暴击炸裂/回弹药）✓
    /// </summary>
    public static class WeaponSystem
    {
        /// <summary>
        /// **把某把武器上的全部插件聚合成一份修正**。
        ///
        /// ★ 这是唯一的聚合点 ★
        ///
        /// 聚合规则（三条，缺一不可）：
        ///   ① **互斥**：`exclusivePerWeapon` 的插件，同一武器只取**第一件** ✗
        ///      不做的话"同类技能型插件装 3 个"会让效果翻三倍 ✓
        ///   ② **叠加上限**：按 `maxStack` 截断 ✗ 不做的话 `Stacks` 会无限涨 ✓
        ///   ③ **乘算类累加后一次性乘**：两个 +30% 是 `1+0.6`，**不是** `1.3×1.3` ✗
        ///      这条最容易做错，而两种算法的差距会随插件数**指数放大** ✓
        ///
        /// 纯函数：不改任何状态 ⇒ 可离线断言 ✓（当前环境下最可靠的验证方式）
        ///
        /// ★ v171：**删掉了 `weaponQuality` 参数**（原来是第三个参数）★
        /// 原因是一条**双重计费**：武器品质本来就已经由攻击路径乘过一次 ——
        /// 锄头 `hoe.baseValue * ToolMult(ToolHoe)`、肥料 `atkMult * ToolMult(WeaponFertilizer)`
        /// 都是 `T.quality.MultOf(ToolLevel[...])` ✗
        /// 这里再乘一次 `SeedEconomy.QualityMult(weaponQuality) - 1`，金锄头就是**三次**计费 ✓
        /// 而且那两个函数的参数一直没人传得对（零调用），所以从没暴露出来 ✓
        /// 现在品质的归属很清楚：**武器品质归攻击路径的 `ToolMult`，插件归这里** ✓
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
                float total2 = pi.Asset.amount2 * stacks;

                // ③ 分派
                switch (pi.Asset.effect)
                {
                    // ── 数值区 ──
                    case PluginEffectKind.DamageFlat: m.DamageFlat += total; break;
                    case PluginEffectKind.DamagePercent: m.DamagePercent += total; break;
                    case PluginEffectKind.AttackSpeed: m.AttackSpeedPct += total; break;
                    case PluginEffectKind.Range: m.RangePct += total; break;
                    case PluginEffectKind.CritRate: m.CritRateFlat += total; break;
                    case PluginEffectKind.CritDamage: m.CritDamageFlat += total; break;
                    case PluginEffectKind.ExplodeRadius: m.ExplodeRadiusPct += total; break;
                    case PluginEffectKind.ProjectileCount: m.ProjectileCount += Mathf.RoundToInt(total); break;
                    case PluginEffectKind.Knockback: m.KnockbackFlat += total; break;

                    // ── 攻击形式 ──
                    case PluginEffectKind.SwordWave: m.SwordWaveCount += Mathf.RoundToInt(total); break;

                    // ── 小机制 ──
                    case PluginEffectKind.ChillOnHit: m.ChillPerHit += Mathf.RoundToInt(total); break;
                    case PluginEffectKind.AmmoOnKill: m.AmmoPerKill += total; break;

                    // ── 大机制 ──
                    case PluginEffectKind.BurnOnHit: m.BurnPerHitPct += total; break;
                    case PluginEffectKind.ChainOnHit:
                        m.ChainTargets += Mathf.RoundToInt(total);
                        // 比例用**累加**：两个连锁插件 = 跳数相加、每跳比例相加 ✓
                        // 用 max 的话"再装一个同款"会没有任何效果 —— 玩家会以为插件坏了 ✓
                        m.ChainRatio += total2;
                        break;
                    case PluginEffectKind.BlastEcho: m.BlastEchoPct += total; break;

                    // ── 特殊效果 ──
                    case PluginEffectKind.CritBlast: m.CritBlastPct += total; break;
                    case PluginEffectKind.FullHpBonus: m.FullHpBonusPct += total; break;

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
        // 机制区的**纯算式**：把"命中之后打多少"抽成几个可离线断言的函数 ✓
        // 攻击路径只负责"谁来挨这一下"，不负责决定数值 ✗
        // ────────────────────────────────────────────────────────────────────

        /// <summary>满血增伤：满血时 ×(1+pct)，否则 ×1。**纯函数**，血量由调用方读。</summary>
        public static float FullHpMult(bool atFullHp, in WeaponModifiers m)
            => atFullHp && m.FullHpBonusPct > 0f ? 1f + m.FullHpBonusPct : 1f;

        /// <summary>命中灼烧的灼烧量 = 本次伤害 × 比例。</summary>
        public static float BurnAmountFor(float damage, in WeaponModifiers m)
            => m.BurnPerHitPct > 0f ? Mathf.Max(0f, damage) * m.BurnPerHitPct : 0f;

        /// <summary>共振连锁每一跳的伤害 = 本次伤害 × 每跳比例。</summary>
        public static float ChainStepDamage(float damage, in WeaponModifiers m)
            => m.ChainTargets > 0 ? Mathf.Max(0f, damage) * Mathf.Max(0f, m.ChainRatio) : 0f;

        /// <summary>暴击炸裂的溅射伤害 = 本次伤害 × 比例（只在暴击时非 0）。</summary>
        public static float CritBlastDamage(float damage, bool crit, in WeaponModifiers m)
            => crit && m.CritBlastPct > 0f ? Mathf.Max(0f, damage) * m.CritBlastPct : 0f;

        /// <summary>爆炸回声第二段的伤害 = 第一段 × 比例。</summary>
        public static float BlastEchoDamage(float firstBlastDamage, in WeaponModifiers m)
            => m.BlastEchoPct > 0f ? Mathf.Max(0f, firstBlastDamage) * m.BlastEchoPct : 0f;

        /// <summary>剑气/激光/爆炸点的**实际条数** = 基础 1 + 插件的 `ProjectileCount`。</summary>
        public static int VolleyCount(int baseCount, in WeaponModifiers m)
            => Mathf.Max(1, baseCount) + Mathf.Max(0, m.ProjectileCount);

        /// <summary>
        /// **装上某个插件**。返回是否成功。
        ///
        /// 检查顺序就是规格里"安装流程"的三步：
        ///   ① 武器类型适用 `Fits` ✓
        ///   ② 互斥检查（同武器已有同类互斥插件则拒绝）✓
        ///   ③ 叠加上限 ✓
        ///
        /// v171 补上第四步：④ **每把武器 6 个插槽的总上限** ✓
        /// —— 上限本身写在 `PlayerGoods.Acquire`（唯一执法点），这里只负责**把话说清楚** ✗
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
        /// 把插件修正**套用到一次攻击的入参上**。
        ///
        /// 它不自己算伤害 ✗ 只产出"喂给 `DamageResolver.AttackFormula` 的参数" ✓
        /// 所以伤害公式仍然**只有一处** ✓（本会话的纪律：两套真相必然分歧）
        ///
        /// 覆盖的攻击环节：**基础伤害 / 伤害属性 / 攻击倍率 / 暴击率 / 暴击伤害** ✓
        /// 另外几环（范围 / 攻速 / 击退 / 弹道数 / 各种机制）**不在这个签名里**——
        /// 它们不是公式的入参，由攻击路径自己从 `WeaponModifiers` 上读 ✓
        /// （例如 `p.WeaponCds[slot] = cooldown / (AttackSpeed × (1+m.AttackSpeedPct))`）
        ///
        /// v162（A3）：第三个参数原叫 `atkDamage`，随「攻击伤害」属性一起消失
        /// ——它引用的概念已不存在。改名为 `damageStat`（近战/远程/爆炸伤害之一）。
        /// </summary>
        public static void ApplyToAttack(in WeaponModifiers m,
                                         ref float baseValue, ref float damageStat, ref float atkMult,
                                         ref float critRate, ref float critDmg)
        {
            baseValue += m.DamageFlat;
            atkMult *= m.DamageMult;
            critRate += m.CritRateFlat;
            critDmg += m.CritDamageFlat;
        }

        /// <summary>
        /// 插件影响的是**攻击的哪一环**。给日志/自检/UI 共用一份说法 ✓
        /// 新增效果时**必须**在这里加一条 —— 自检有一条断言钉住"枚举值都有说法" ✓
        /// </summary>
        public static string ChannelOf(PluginEffectKind k)
        {
            switch (k)
            {
                case PluginEffectKind.DamageFlat:
                case PluginEffectKind.DamagePercent:
                    return "伤害";
                case PluginEffectKind.AttackSpeed:
                    return "攻速";
                case PluginEffectKind.Range:
                    return "射程";
                case PluginEffectKind.CritRate:
                case PluginEffectKind.CritDamage:
                    return "暴击";
                case PluginEffectKind.ExplodeRadius:
                    return "爆炸半径";
                case PluginEffectKind.ProjectileCount:
                    return "弹道数";
                case PluginEffectKind.Knockback:
                    return "击退";
                case PluginEffectKind.SwordWave:
                    return "攻击形式";
                case PluginEffectKind.ChillOnHit:
                    return "命中减速";
                case PluginEffectKind.AmmoOnKill:
                    return "击杀回弹药";
                case PluginEffectKind.BurnOnHit:
                    return "命中灼烧";
                case PluginEffectKind.ChainOnHit:
                    return "共振连锁";
                case PluginEffectKind.BlastEcho:
                    return "爆炸回声";
                case PluginEffectKind.CritBlast:
                    return "暴击炸裂";
                case PluginEffectKind.FullHpBonus:
                    return "满血增伤";
                default:
                    return "无";
            }
        }
    }
}
