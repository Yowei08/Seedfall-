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
            SetBase(StatId.ExplodeDamage, init.explodeDamage);
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
            }
            _dirty = false;
        }

        private float CapOf(StatId id)
        {
            switch (id)
            {
                case StatId.Dodge: return _caps.dodge;
                case StatId.CritRate: return _caps.critRate;
                case StatId.AtkSpeed: return _caps.atkSpeed;
                case StatId.AtkRange: return _caps.atkRange;
                case StatId.MoveSpeed: return _caps.moveSpeed;
                // v162：三种伤害类型各有自己的上限。
                // ★ 读不到就**不设上限**（返回 PositiveInfinity），不要返回 0 ★ ——
                //    返回 0 会把所有伤害钳成 0，而且不报错、不抛异常，只是"打不动怪"。
                //    （这与下面 HeartDropRate 那条是同一个教训：钳错了值比不钳更危险。）
                case StatId.MeleeDamage: return CapOrInfinity(_caps.meleeDamage);
                case StatId.RangedDamage: return CapOrInfinity(_caps.rangedDamage);
                case StatId.BlastDamage: return CapOrInfinity(_caps.blastDamage);
                case StatId.ExplodeDamage: return _caps.explodeDamage;
                case StatId.Knockback: return _caps.knockback;
                case StatId.ExplodeRange: return _caps.explodeRange;
                case StatId.MaxPlots: return _caps.maxPlots;
                // v110：爱心掉落率改等级制后，上限是**等级 3（MAX）**而不是 1。
                // 原来钳在 1f 会把"等级 3"直接压成"等级 1"——道具买了却看不到变化。
                case StatId.HeartDropRate: return 3f;
                case StatId.HeartHeal: return 99f;
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
                Stats.AddBase(StatId.MaxHp, _growth.hpPerLevel);
                Stats.AddBase(StatId.Shield, _growth.shieldPerLevel);
                Stats.AddBase(StatId.MaxHp, _growth.hpPerLevel);
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
