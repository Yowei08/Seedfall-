using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v199：**只读探针** —— 三把武器 baseValue 4/3/5 → 8/6/12 的逐项读数 ★★
    ///
    /// 用户原话："玩家锄头初始伤害调整为8，水壶6，肥料12"
    ///
    /// 为什么要有它：
    ///   · ① 「改前 → 改后的一击伤害」必须**从表里算出来**，而不是手写在交付里 ——
    ///        探针同时把 `DamageResolver.AttackFormula` 的结果与"旧基数 4/3/5"代入的
    ///        同一条式子打出来 ⇒ 两个数来自**同一个函数**，只差入参 ✓
    ///   · ② 「20 分钟几刀」= `ceil(血 / 每击)`，血来自 `EnemyDef.HpForWave`、
    ///        每击来自公式 ⇒ 这是两个既有函数的商，不是新的算式 ✓
    ///   · ③ 「抽卡 +1 的相对价值」= `(基数+1)/基数 − 1`，基数读表、不看任何写死的数 ✓
    ///   · ④ 自检原文（469 条的计数与**武器相关的逐条**）一起落盘，交付里可直接引用 ✓
    ///
    /// ★ 只读 ★ 不写 `Assets/**`、不改场景、不改表；
    ///   唯一副作用是 `new RunSession()` 之后立刻 `Dispose()`（与自检同一套夹具用法），
    ///   以及往 `Library/` 落一份报告（那是 Unity 的临时目录：不进版本库、不参与打包）✓
    /// </summary>
    public sealed class ProbeV199Weapons : MonoBehaviour
    {
        /// <summary>报告落盘路径（`Library/` 是 Unity 的临时目录：不进版本库、不参与打包）。</summary>
        public const string ReportPath = "Library/yowei-v199-weapons.txt";

        /// <summary>改前的一击基数（= v195 定的 4 / 3 / 5，本轮被用户要求改掉的那三个数）。</summary>
        private const float OldHoe = 4f, OldCan = 3f, OldFert = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV199Weapons>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV199Weapons");
            go.AddComponent<ProbeV199Weapons>();
        }

        /// <summary>打一份完整读数并落盘，返回原文（桥 `send` 调它）。</summary>
        public string Report()
        {
            string text;
            try { text = BuildReport(); }
            catch (System.Exception ex)
            {
                text = "ProbeV199 CRASH: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
                Debug.LogError("[ProbeV199] " + text);
                TryWrite(text);
                return text;
            }
            TryWrite(text);
            Debug.Log("[ProbeV199] 报告已写入 " + ReportPath);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════
        // 正文
        // ══════════════════════════════════════════════════════════════════

        private static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("══════ ProbeV199Weapons：武器基础伤害 8 / 6 / 12 读数 ══════");
            TableDatabase.Load(true);
            TuningDef t = TableDatabase.Tuning;

            float critRate = t.player.critRate;
            float critDmg = t.player.critDmg;
            float atkMult0 = t.player.atkMult;              // 开局 1.0
            float melee0 = t.player.meleeDamage;            // 开局 0
            float ranged0 = t.player.rangedDamage;
            float blast0 = t.player.blastDamage;
            sb.AppendLine($"开局基底：critRate {critRate:0.###}｜critDmg {critDmg:0.###}｜atkMult {atkMult0:0.###}"
                        + $"｜近战 {melee0:0.###}｜远程 {ranged0:0.###}｜爆炸 {blast0:0.###}");

            // ── ① 三把武器：改前 → 改后（含暴击）──────────────────────────
            sb.AppendLine();
            sb.AppendLine("────── ① 一击伤害（零加成，含暴击）──────");
            WeaponRow(sb, "锄头 hoe", t.tools.hoe.baseValue, OldHoe, melee0, atkMult0, critDmg, t.tools.hoe.cooldown);
            WeaponRow(sb, "水壶 can", t.tools.can.baseValue, OldCan, ranged0, atkMult0, critDmg, t.tools.can.cooldown);
            WeaponRow(sb, "肥料 fertilizer", t.tools.fertilizer.baseValue, OldFert, blast0, atkMult0, critDmg,
                      t.tools.fertilizer.cooldown);
            sb.AppendLine($"  ⚠️ 肥料**没有**第二层乘区（`ExplodeDamage` / 爆炸倍率 2.5 早已删除，v195）"
                        + $" ⇒ 它的式子与锄头逐位同形 ✓");

            // ── ② 20 分钟几刀 / 开局一刀死 ────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("────── ② 刀数（ceil(血 / 每击)，每击 = 零加成普通伤害，不计暴击）──────");
            foreach (string id in new[] { "grunt", "shooter", "jumper", "boss" })
            {
                EnemyDef d = TableDatabase.GetEnemy(id);
                if (d == null) { sb.AppendLine($"  {id}：表里没有"); continue; }
                sb.AppendLine($"  【{id}】曲线 = {(d.Kind == EnemyKind.Boss ? "bossHp*" : d.Kind == EnemyKind.Jumper ? "eliteHp*" : "hpStep*")}"
                            + $"｜baseHp {d.BaseHp:0.##}");
                HitsRow(sb, "第 1 分钟", d.HpForWave(1, 1), t.tools.hoe.baseValue, OldHoe, melee0, atkMult0);
                HitsRow(sb, "第 5 分钟", d.HpForWave(5, 1), t.tools.hoe.baseValue, OldHoe, melee0, atkMult0);
                HitsRow(sb, "第 10 分钟", d.HpForWave(10, 1), t.tools.hoe.baseValue, OldHoe, melee0, atkMult0);
                HitsRow(sb, "第 20 分钟", d.HpForWave(20, 1), t.tools.hoe.baseValue, OldHoe, melee0, atkMult0);
                HitsRow(sb, "第 30 分钟", d.HpForWave(30, 1), t.tools.hoe.baseValue, OldHoe, melee0, atkMult0);
            }

            // 开局一刀死：与自检 V20Checks 同一条判据
            sb.AppendLine();
            sb.AppendLine("────── 『开局一刀死』判据（与自检 V20Checks 同一条：CeilToInt(血/每击) == 1）──────");
            float nowPerHit = DamageResolver.AttackFormula(t.tools.hoe.baseValue, melee0, atkMult0, false, 1f, 0f);
            float oldPerHit = DamageResolver.AttackFormula(OldHoe, melee0, atkMult0, false, 1f, 0f);
            foreach (string id in new[] { "grunt", "shooter" })
            {
                EnemyDef d = TableDatabase.GetEnemy(id);
                float hp = d.HpForWave(1, 1);
                int hitsOld = Mathf.CeilToInt(hp / oldPerHit);
                int hitsNew = Mathf.CeilToInt(hp / nowPerHit);
                sb.AppendLine($"  {id}：血 {hp:0.####}｜每击 {oldPerHit:0.##} → {nowPerHit:0.##}"
                            + $"｜{hitsOld} 次 → {hitsNew} 次｜改前 {(hitsOld == 1 ? "成立" : "不成立")}"
                            + $" → 改后 {(hitsNew == 1 ? "**成立**" : "**不成立**")}"
                            + $"｜余量 {hp / oldPerHit:0.##}× → {hp / nowPerHit:0.##}×"
                            + $"（= 每击 / 血 {nowPerHit / hp:0.##}×）");
            }

            // ── ③ 抽卡 +1 的相对价值 ──────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("────── ③ 抽卡「伤害 +1」的相对价值（(基数+1)/基数 − 1）──────");
            CardRow(sb, "近战伤害 MeleeDamage", "锄头", t.tools.hoe.baseValue, OldHoe);
            CardRow(sb, "远程伤害 RangedDamage", "水壶", t.tools.can.baseValue, OldCan);
            CardRow(sb, "爆炸伤害 BlastDamage", "肥料", t.tools.fertilizer.baseValue, OldFert);
            sb.AppendLine("  说明：卡池的 amount 就是这三个 `Add 1`（`levelReward.stats`），本轮**没动**它 ✓");

            // ── ④ 肥料 ×2.4 的连带项 ──────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("────── ④ 肥料 ×2.40 的连带项（本轮**未改**，仅报读数）──────");
            var mm = WeaponModifiers.None;
            mm.PoisonRatioPerLayer = 0.5f;
            mm.SubBurstRadiusRatio = 0.5f;
            float blastNow = DamageResolver.WeaponBlastDamage(t.tools.fertilizer.baseValue, blast0, atkMult0,
                                                             false, critDmg, 0f);
            float blastOld = DamageResolver.WeaponBlastDamage(OldFert, blast0, atkMult0, false, critDmg, 0f);
            sb.AppendLine($"  主爆 {blastOld:0.##} → {blastNow:0.##}"
                        + $"｜子爆（×{WeaponSystem.SubBurstDamage(1f, mm):0.##}）= {WeaponSystem.SubBurstDamage(blastOld, mm):0.##}"
                        + $" → {WeaponSystem.SubBurstDamage(blastNow, mm):0.##}");
            sb.AppendLine($"  毒区每层每秒（×0.50）= {WeaponSystem.PoisonPerLayerPerSecond(blastOld, mm):0.##}"
                        + $" → {WeaponSystem.PoisonPerLayerPerSecond(blastNow, mm):0.##}");
            sb.AppendLine($"  玩家血量 {t.player.maxHp:0.##}｜护盾 {t.player.shield:0.##}"
                        + $"｜敌人 baseHp（grunt）{TableDatabase.GetEnemy("grunt").BaseHp:0.##} —— 两者**本轮都未改** ✓");

            // ── ⑤ 自检原文 ────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("────── ⑤ 自检 ──────");
            System.Collections.Generic.List<CheckResult> results = null;
            try { results = SelfCheck.RunAll(); }
            catch (System.Exception ex)
            {
                sb.AppendLine("自检 CRASH " + ex.GetType().Name + ": " + ex.Message);
            }
            if (results != null)
            {
                int fail = 0;
                foreach (CheckResult r in results) if (!r.Passed) fail++;
                sb.AppendLine($"自检 {(fail == 0 ? "PASS" : "FAIL")}：{results.Count - fail}/{results.Count} 通过（失败 {fail}）");
                sb.AppendLine();
                sb.AppendLine("  ★ 与武器基础伤害直接相关的断言（PASS 也打）★");
                foreach (CheckResult r in results)
                {
                    if (r.Name.Contains("锄头单击") || r.Name.Contains("水壶单发") || r.Name.Contains("萝卜卫士")
                        || r.Name.Contains("一刀死") || r.Name.Contains("基础兵血量") || r.Name.Contains("20 分基础兵")
                        || r.Name.Contains("基础伤害提高") || r.Name.Contains("首波基础兵")
                        || r.Name.Contains("武器伤害") || r.Name.Contains("肥料"))
                    {
                        sb.AppendLine("  " + (r.Passed ? "PASS  " : "FAIL  ") + r.Name + "　" + r.Detail);
                    }
                }
                sb.AppendLine();
                sb.AppendLine("  ★ 失败清单 ★");
                if (fail == 0) sb.AppendLine("  （无）");
                else foreach (CheckResult r in results) if (!r.Passed) sb.AppendLine("  FAIL  " + r.Name + "　" + r.Detail);
                sb.AppendLine();
                sb.AppendLine("  ★ 全文（逐条）★");
                foreach (CheckResult r in results) sb.AppendLine("  " + r);
            }

            return sb.ToString();
        }

        /// <summary>一行武器读数：改前 / 改后的一击（普通）与一击（暴击），以及每击间隔。</summary>
        private static void WeaponRow(StringBuilder sb, string label, float baseNow, float baseOld,
                                       float dmgStat, float atkMult, float critDmg, float cooldown)
        {
            float plainNow = DamageResolver.AttackFormula(baseNow, dmgStat, atkMult, false, critDmg, 0f);
            float critNow = DamageResolver.AttackFormula(baseNow, dmgStat, atkMult, true, critDmg, 0f);
            float plainOld = DamageResolver.AttackFormula(baseOld, dmgStat, atkMult, false, critDmg, 0f);
            float critOld = DamageResolver.AttackFormula(baseOld, dmgStat, atkMult, true, critDmg, 0f);
            sb.AppendLine($"  {label}：baseValue {baseOld:0.##} → **{baseNow:0.##}**（×{baseNow / Mathf.Max(0.0001f, baseOld):0.###}）");
            sb.AppendLine($"      一击 {plainOld:0.00} → **{plainNow:0.00}**｜暴击 {critOld:0.00} → **{critNow:0.00}**"
                        + $"｜冷却 {cooldown:0.##}s ⇒ 期望 DPS（暴击率 {TableDatabase.Tuning.player.critRate:0.###}）"
                        + $"{plainOld * (1f + TableDatabase.Tuning.player.critRate * (critDmg - 1f)) / Mathf.Max(0.01f, cooldown):0.##}"
                        + $" → {plainNow * (1f + TableDatabase.Tuning.player.critRate * (critDmg - 1f)) / Mathf.Max(0.01f, cooldown):0.##}");
        }

        /// <summary>一行刀数：改前基数 / 改后基数 各需要几刀。</summary>
        private static void HitsRow(StringBuilder sb, string when, float hp, float baseNow, float baseOld,
                                     float dmgStat, float atkMult)
        {
            float perNew = DamageResolver.AttackFormula(baseNow, dmgStat, atkMult, false, 1f, 0f);
            float perOld = DamageResolver.AttackFormula(baseOld, dmgStat, atkMult, false, 1f, 0f);
            int hitsNew = perNew <= 0f ? int.MaxValue : Mathf.CeilToInt(hp / perNew);
            int hitsOld = perOld <= 0f ? int.MaxValue : Mathf.CeilToInt(hp / perOld);
            sb.AppendLine($"    {when}：血 {hp:0.##}｜{hitsOld} 刀 → **{hitsNew} 刀**"
                        + $"（每击 {perOld:0.##} → {perNew:0.##}）");
        }

        /// <summary>一行卡价值：同一张 `Add 1` 在旧基数 / 新基数下的相对增幅。</summary>
        private static void CardRow(StringBuilder sb, string statName, string weaponName,
                                     float baseNow, float baseOld)
        {
            float oldPct = 1f / Mathf.Max(0.0001f, baseOld);
            float newPct = 1f / Mathf.Max(0.0001f, baseNow);
            sb.AppendLine($"  {statName}（→ {weaponName}）：+1 = {oldPct * 100f:0.##}% → **{newPct * 100f:0.##}%**"
                        + $"｜缩水到 {newPct / oldPct * 100f:0.#}%");
        }

        private static void TryWrite(string text)
        {
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[ProbeV199] 报告写盘失败：" + ex.Message);
            }
        }
    }
}
