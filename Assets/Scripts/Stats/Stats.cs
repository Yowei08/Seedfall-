using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    public enum ModOp { Add, Mul }

    public struct StatModifier
    {
        public StatId Stat;
        public float Value;
        public ModOp Op;
        public string Source;

        public StatModifier(StatId stat, float value, ModOp op, string source)
        {
            Stat = stat; Value = value; Op = op; Source = source;
        }
    }

    /// <summary>
    /// 属性块：基础值 → 加法修正 → 乘法修正 → 上限裁剪。
    /// 裁剪最后执行，避免上限被乘法绕过（策划案 §1）。
    /// </summary>
    public sealed class StatBlock
    {
        private readonly float[] _base = new float[StatIds.Count];
        private readonly List<StatModifier> _mods = new List<StatModifier>();
        private readonly CapsDef _caps;
        private readonly float[] _cache = new float[StatIds.Count];
        private bool _dirty = true;

        public StatBlock(CapsDef caps, PlayerBaseDef init)
        {
            _caps = caps ?? new CapsDef();
            if (init == null) return;
            SetBase(StatId.MaxHp, init.maxHp);
            SetBase(StatId.Shield, init.shield);
            SetBase(StatId.MeleeDamage, init.meleeDamage);
            SetBase(StatId.RangedDamage, init.rangedDamage);
            SetBase(StatId.BlastDamage, init.blastDamage);
            SetBase(StatId.AtkMult, init.atkMult);
            SetBase(StatId.ExplodeRange, init.explodeRange / 100f);
            SetBase(StatId.CritRate, init.critRate);
            SetBase(StatId.CritDmg, init.critDmg);
            SetBase(StatId.AtkSpeed, init.atkSpeed);
            SetBase(StatId.AtkRange, init.atkRange);
            SetBase(StatId.MoveSpeed, init.moveSpeed);
            SetBase(StatId.Dodge, init.dodge);
            SetBase(StatId.MaxPlots, init.maxPlots);
            SetBase(StatId.Knockback, init.knockback);
            SetBase(StatId.ExpMult, init.expMult);
            SetBase(StatId.GoldMult, init.goldMult);
            SetBase(StatId.HeartDropRate, init.heartDropRate);
            SetBase(StatId.HeartHeal, init.heartHeal);
            // ★ v191：两条新属性必须**在这里**接上基底，否则它们的基底恒为 0 ★
            //
            // `_base` 是 `new float[StatIds.Count]` 的零初始化数组 ⇒ 漏接一行的后果不是编译错误，
            // 而是"属性在枚举里、在面板上有名字、在卡池里能抽到，但基底永远是 0" ✗
            // 对 `PigRate` 是"等级恒 0"（看不出问题），对 `Luck` 是**每张 Percent 卡都乘不动** ✗
            // （`0 × (1+p) = 0`，而面板又显示 `+0%` ⇒ 卡到底生效没有，玩家与自检都看不出来）
            // 所以这两行不是"顺手补齐"，是需求①②能成立的前提 ✓
            SetBase(StatId.Luck, init.luck);
            SetBase(StatId.PigRate, init.pigRate);
        }

        public void SetBase(StatId id, float value)
        {
            _base[(int)id] = value;
            _dirty = true;
        }

        public void AddBase(StatId id, float delta)
        {
            _base[(int)id] += delta;
            _dirty = true;
        }

        public float GetBase(StatId id) => _base[(int)id];

        public void AddModifier(in StatModifier m)
        {
            _mods.Add(m);
            _dirty = true;
        }

        public int RemoveModifiersFrom(string source)
        {
            int removed = _mods.RemoveAll(m => m.Source == source);
            if (removed > 0) _dirty = true;
            return removed;
        }

        public float Get(StatId id)
        {
            if (_dirty) Evaluate();
            return _cache[(int)id];
        }

        public void Evaluate()
        {
            Array.Copy(_base, _cache, StatIds.Count);
            for (int i = 0; i < _mods.Count; i++)
            {
                StatModifier m = _mods[i];
                if (m.Op == ModOp.Add) _cache[(int)m.Stat] += m.Value;
            }
            for (int i = 0; i < _mods.Count; i++)
            {
                StatModifier m = _mods[i];
                if (m.Op == ModOp.Mul) _cache[(int)m.Stat] *= m.Value;
            }
            for (int i = 0; i < StatIds.Count; i++)
            {
                float cap = CapOf((StatId)i);
                if (!float.IsPositiveInfinity(cap) && _cache[i] > cap) _cache[i] = cap;
                // ★★ v195（用户原话："检查其它为负数做减益处理"）★★
                //   上限之后**再**钳下限：顺序必须是"先封顶后保底"，
                //   否则某条被顶到 9990 的属性又被一个负下限抬回去，两个旋钮互相打架 ✓
                float floor = FloorOf((StatId)i);
                if (_cache[i] < floor) _cache[i] = floor;
            }
            _dirty = false;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v195：每条属性的负值下限 —— **这一套在 v197 被用户作废了** ★★
        //
        // v195 的口径是"三类分档"：点数类下限 0/1、**倍率类下限 0.1**、伤害类无下限。
        // 用户 v197 的原话直接否掉了中间那一档：
        //   "…其它的数值可以为负数，并造成负面效果，−200% 攻速降低比如降低攻速、
        //    −200 暴击伤害降低暴击伤害"
        // ⇒ 倍率类统一 0.1 的下限会把"−200% 攻速"抬成 0.1，**减益被下限吃掉** ✗（v195 的实质 bug）
        // ⇒ 现行口径只有**四组例外**（概率三条 / MaxHp / Shield / 伤害三条的"存值不锁"），
        //    其余一律 `−∞`；每个消费点各自兜底 —— 详见下面 `FloorOf` 的注释 ✓
        //
        // 本注释块保留是因为它解释了**为什么会有这套下限**：v195 当年加它，
        // 治的是"血条画到框外 / 等级查表越界 / 攻击间隔变负"这三类坏法。
        // 那三类坏法**一条都没消失** —— 它们现在由"消费点兜底"接手（`Clamp` / `Max(0.05f, …)` /
        // `HeartDropRateOf` 的查表钳），而不是由属性层统一抬到 0.1 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 负值下限（唯一实现）。
        ///
        /// ★★ v197（用户原话："闪避修改就是全局…除了近战伤害、远程伤害、爆炸伤害为负数时会锁0，
        ///    以及暴击率、幸运值、闪避率锁0%，最大生命值锁1，护盾锁0，其它的数值可以为负数，
        ///    并造成负面效果，-200%攻速降低比如降低攻速、-200暴击伤害降低暴击伤害"）★★
        ///
        /// ⇒ **三档划分（v195 那套）作废，只剩下四组例外**：
        ///   ① 伤害三条（近战/远程/爆炸）：**存值可为负**，`DamageResolver` 代入公式时钳 0 ✓
        ///   ② 概率三条（暴击率 / 幸运值 / 闪避率）：钳 0 ✓
        ///   ③ 最大生命：钳 1（0 血 = 立刻死亡 + 血条除零）✓
        ///   ④ 护盾：钳 0 ✓
        ///   **其余全部无下限** —— 负值必须**真的造成负面效果** ✓
        ///   （攻速 −200% 就是真的变慢；暴击伤害 −200% 就是暴击倒扣；移速为负就是反向…）
        ///
        /// ⚠️ 消费点自己兜底（这是"放开"的代价，逐条写在消费点）：
        ///   · 攻速：`cooldown / atkSpeed` 的三处**早就有** `Mathf.Max(0.05f, …)` ⇒
        ///     负攻速被兜成 0.05 ⇒ 攻击间隔 ×20 ⇒ **真的变慢**，且不会出现负冷却 ✓
        ///   · `MaxPlots`：`PlayerProgress.MaxPlots` 自己 `Clamp(1, caps)` ✓
        ///   · 等级类（HeartDropRate / PigRate）：查表处自己钳（`TuningDef.PigRateByLevel` 已钳）✓
        /// </summary>
        private static float FloorOf(StatId id)
        {
            switch (id)
            {
                // ② 概率三条：锁 0%
                case StatId.CritRate: return 0f;
                case StatId.Luck: return 0f;
                case StatId.Dodge: return 0f;

                // ③ 最大生命：锁 1（不是 0 —— 0 血会让 `Hp/MaxHp` 变成 NaN）
                case StatId.MaxHp: return 1f;

                // ④ 护盾：锁 0
                case StatId.Shield: return 0f;

                // ① 伤害三条：**不设下限**（存值保持负，计算时由 `NonNegativeDamageStat` 钳 0）
                case StatId.MeleeDamage:
                case StatId.RangedDamage:
                case StatId.BlastDamage:
                    return float.NegativeInfinity;

                // ★ 其余全部无下限（v197 放开的那些：攻速 / 暴击伤害 / 伤害倍率 / 移速 /
                //   爆炸范围 / 经验 / 金币 / 击退 / 回血量 / 等级类 / 列数 …）⇒ 负值照原样保留 ✓
                default: return float.NegativeInfinity;
            }
        }

        /// <summary>v195：查询某条属性的负值下限（探针/自检/报告读它，别自己再算一份）。</summary>
        public static float FloorOfStat(StatId id) => FloorOf(id);

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v197：**攻速上限**可以被一件道具改写（"攻速上限改为 −200%"）★★
        //
        // ★ 用户纠正后的口径（这一段是照原话重写的）★
        //   "攻速上限修改为 **−200%**，**不是下限**" —— 也就是说允许的**最高**攻速被压到 −200%
        //   ⇒ 乘子是负的 ⇒ **真的变慢** ✓
        //   而"闪避上限 70%"是**全局**的（另一路把 `caps.dodge` 定成 0.6，
        //   拥有那件道具时写 0.7、摘掉回落）⇒ 它**不走这里**，由
        //   `ProgMechanics.ApplyDodgeCapToTable` 直接写 `Tuning.caps.dodge` ✓
        //
        // ★ 为什么上限覆盖做成 `static` ★
        //   写侧在 `ProgMechanics.ItemPanel.Of`、读侧在 `CapOf`（本文件）；
        //   传实例会把 `StatBlock` 的构造签名与全部夹具一起改掉 ✗
        //   ⚠️ 由 `ProgMechanics.Reset()` 在每局开始时归零（**唯一**复位点）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>攻速上限是否被道具改写（false = 用 `caps.atkSpeed`）。</summary>
        public static bool AtkSpeedCapFixedActive;

        /// <summary>改写后的攻速上限（倍率单位；−2 = −200%）。</summary>
        public static float AtkSpeedCapFixed;

        /// <summary>清掉攻速上限改写（每局开始 / 夹具复位）。</summary>
        public static void ResetRuntimeOverrides()
        {
            AtkSpeedCapFixedActive = false;
            AtkSpeedCapFixed = 0f;
        }

        private float CapOf(StatId id)
        {
            switch (id)
            {
                case StatId.Dodge: return _caps.dodge;
                // ★ v197：攻速上限可被道具压到负值（"−200%"）。取 `min(改写值, 表值)` ——
                //   这件道具的语义是"把上限压小"，它**不该**顺手把上限抬高 ✓
                case StatId.AtkSpeed:
                    return AtkSpeedCapFixedActive
                        ? Mathf.Min(AtkSpeedCapFixed, _caps.atkSpeed)
                        : _caps.atkSpeed;
                case StatId.CritRate: return _caps.critRate;
                case StatId.AtkRange: return _caps.atkRange;
                case StatId.MoveSpeed: return _caps.moveSpeed;
                // v162：三种伤害类型各有自己的上限。
                // ★ 读不到就**不设上限**（返回 PositiveInfinity），不要返回 0 ★ ——
                //    返回 0 会把所有伤害钳成 0，而且不报错、不抛异常，只是"打不动怪"。
                //    （这与下面 HeartDropRate 那条是同一个教训：钳错了值比不钳更危险。）
                case StatId.MeleeDamage: return CapOrInfinity(_caps.meleeDamage);
                case StatId.RangedDamage: return CapOrInfinity(_caps.rangedDamage);
                case StatId.BlastDamage: return CapOrInfinity(_caps.blastDamage);
                case StatId.Knockback: return _caps.knockback;
                case StatId.ExplodeRange: return _caps.explodeRange;
                case StatId.MaxPlots: return _caps.maxPlots;
                // v110：爱心掉落率改等级制后，上限是**等级 3（MAX）**而不是 1。
                // 原来钳在 1f 会把"等级 3"直接压成"等级 1"——道具买了却看不到变化。
                case StatId.HeartDropRate: return 3f;
                // ★ v210（用户原话：『…爱心恢复数全部修改无上限』）：99f → 9999f ★
                //   · 它是**恢复量**（每个爱心回几点血），不是等级也不是概率 ⇒ 放开不会破坏任何枚举映射 ✓
                //   · 与上面 `HeartDropRate`（**等级** 0..3）、下面 `PigRate`（**等级** 0..3）**性质不同** ——
                //     那两条必须继续钳，钳错了值比不钳更危险（会落到 `TuningDef.*ByLevel` 之外）✓
                //   · 用 9999 作哨兵，与 `tuning.json` 的 `caps._note_v210_uncap` 同源 ✓
                case StatId.HeartHeal: return 9999f;
                // ★ v191：两条新属性的上限 ★
                //
                // `PigRate` **必须**钳在 3 —— 它是**等级**（0..3），
                //   与上面 `HeartDropRate` 是同一条理由：钳错了值比不钳更危险。
                //   钳在 3 ⇒ 等级永远落在 `TuningDef.PigRateByLevel`（4 条）之内 ✓
                //   不钳 ⇒ 道具叠到第 4 张时等级变成 4 ⇒ 查表越界（`PigRateOf` 虽会钳，
                //   但"道具买到上限还继续加"这件事本身与"达到上限不再刷新"的要求冲突）
                //
                // `Luck` 钳在 100% —— 它是概率（0..1 小数），>100% 没有语义（恒触发）。
                //   ⚠️ 这里**不用** `CapOrInfinity`：那条是"表里没填就当没上限"，
                //   而幸运的上限是**设计值**（100%），不是配置项 ⇒ 写死更诚实 ✓
                case StatId.PigRate: return 3f;
                case StatId.Luck: return 1f;
                default: return float.PositiveInfinity;
            }
        }

        /// <summary>
        /// 表里没填（≤0）就当作**没有上限**。
        /// 用于"漏填一个数会静默毁掉玩法"的属性——伤害类全是这种。
        /// </summary>
        private static float CapOrInfinity(float configured)
            => configured > 0f ? configured : float.PositiveInfinity;
    }

    /// <summary>等级 / 经验 / 属性一体。升级奖励按策划案 §4 执行。</summary>
    public sealed class PlayerProgress
    {
        private readonly GrowthDef _growth;

        public StatBlock Stats { get; }
        public int Level { get; private set; } = 1;
        public float Exp { get; private set; }
        public int LevelsGainedTotal { get; private set; }

        private readonly CapsDef _caps;

        public PlayerProgress(TuningDef tuning)
        {
            _growth = tuning.growth;
            _caps = tuning.caps;
            Stats = new StatBlock(tuning.caps, tuning.player);
        }

        public float ExpToNext => ExpToNextFor(Level, _growth);

        public static float ExpToNextFor(int level, GrowthDef g)
        {
            return Mathf.Round(g.expBase + g.expLinear * Mathf.Pow(level - 1, g.expExponent));
        }

        /// <summary>返回是否升级（并输出本次升了几级）。经验会连续结算多级。</summary>
        public bool AddExp(float amount, out int levelsGained)
        {
            levelsGained = 0;
            if (amount <= 0f) return false;
            Exp += amount;
            while (Exp >= ExpToNext)
            {
                Exp -= ExpToNext;
                Level++;
                levelsGained++;
                LevelsGainedTotal++;
                // ★ v184：**这里原本把 `MaxHp` 加了两次** ✗（用户需求④："升级生命值加 1"）
                //
                // 旧代码（三行）：
                //     Stats.AddBase(StatId.MaxHp, _growth.hpPerLevel);
                //     Stats.AddBase(StatId.Shield, _growth.shieldPerLevel);
                //     Stats.AddBase(StatId.MaxHp, _growth.hpPerLevel);      ← 重复行
                // 于是 `growth.hpPerLevel = 1` 实际给的是 **+2 血/级**，
                // 而自检 `升级最大生命 +1` 只验了**配置值**（`t.growth.hpPerLevel == 1`）⇒ 一直是绿的 ✓
                // 这就是"配置读对了、行为却是两倍"的典型：断言守在配置上，没守在行为上 ✗
                // 删除重复行后：+1 血/级（由 `hpPerLevel` 决定）✓
                Stats.AddBase(StatId.MaxHp, _growth.hpPerLevel);
                // ★ v184：护盾**不再随等级增长**（用户需求④："但护盾不加"）★
                // 仍然读配置（`growth.shieldPerLevel`），只是 `tuning.json` 里把它置 0 ——
                // 保留这一行而不是删掉，是为了让"要不要给护盾成长"继续是一个**改表就能回退**的决定 ✓
                // （`shieldRegenMin = 1` 那条下限不会被这条影响：它管的是"每秒回多少"，不是"上限多高"）
                Stats.AddBase(StatId.Shield, _growth.shieldPerLevel);
                // ★ v166：**删除"按等级 +1 块耕地"** ✗
                //
                // 旧模型：`growth.levelsPerPlot`（曾为 1）级就给一块新耕地，上限 caps.maxPlots ✓
                // 新玩法下田地是**固定形状**（6 列 × N 行），行数由【存活时间】决定，
                // 与等级无关 ⇒ "升级多一块地"这件事在模型里**不存在了** ✓
                //
                // 实现方式：`growth.levelsPerPlot` 置 0 ⇒ 这个分支永不进入 ✓
                // 保留分支（而不是删代码）是为了让"回退到旧模型"仍然只需改一个配置值。
                if (_growth.levelsPerPlot > 0 && Level % _growth.levelsPerPlot == 0)
                    Stats.AddBase(StatId.MaxPlots, 1f);
            }
            return levelsGained > 0;
        }

        /// <summary>
        /// ★ v166：这个值现在的语义是**可种植株数（= 田地的列数）**，不再是"耕地块数"★
        ///
        /// 旧语义：初始 `farm.initialPlots` 块，每 `growth.levelsPerPlot` 级 +1，上限 `caps.maxPlots` ✗
        /// 新语义：田地固定 6 列 × N 行；**一列 = 一株植物**，所以"能种几株"就等于列数 ✓
        /// 它仍然驱动 `RunSession` 的种子袋容量（`Bag.SlotCount = MaxPlots`，一个槽位一株）✓
        /// 行数成长**不经过这里**，而是 `FarmGrid.SyncSurvival`（存活 5 分钟一行）✓
        /// </summary>
        public int MaxPlots => Mathf.Clamp(Mathf.RoundToInt(Stats.Get(StatId.MaxPlots)), 1, Mathf.Max(1, _caps.maxPlots));
    }
}
