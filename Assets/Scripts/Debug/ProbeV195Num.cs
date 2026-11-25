using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v195 只读探针 ★★ —— 给用户这一轮要的**全部读数**打表：
    ///   ① 自检原文（放最前面：它崩了也要先看见它崩了）
    ///   ② 五张表（玩家 / 怪物 / 抽卡 / 商品价 1→30 分钟 / 道具）
    ///   ③ 敌人伤害"几下致死"（普通 / 弹幕 / 精英 / boss × 1 / 10 / 20 / 30 分钟）
    ///   ④ 重抽价 1 / 5 / 10 / 20 / 30 分钟
    ///   ⑤ 满配农场每分钟收入（36 格 · 全 3 级 · 最高品质）
    ///   ⑥ 负数属性的四条判据（伤害类钳 0 / 点数类钳 0 / 倍率类安全下限 / 真减益仍生效）
    ///   ⑦ "爆炸倍率删干净"的证据（TryParse / StatIds.Count / 三件道具改后原文）
    ///   ⑧ 爆炸收割 1/4 与商品价上限检查（≤2000 / 是否有人 ≥10000）
    ///
    /// 与 `ProbeV192Num` / `ProbeV194Num` 同一套做法：
    ///   · 静态类 + `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`
    ///   · 开关是 `<工程>/Library/yowei_v195_probe.on` —— 存在才跑一次，删掉就彻底静默
    ///   · 读数同时进 `Log`（前缀 `PROBEV195`）与 `Library/yowei-v195-probe.txt`
    ///
    /// ★ 它是**只读**的 ★ 不写任何工程文件；`StatBlock` 都在内存里新建、跑完就地丢掉 ✓
    /// </summary>
    public static class ProbeV195Num
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei_v195_probe.on");
        private static string OutPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-v195-probe.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!File.Exists(FlagPath)) return;

            var all = new StringBuilder();
            all.AppendLine("═══ v195 探针（只读）═══");

            try
            {
                List<CheckResult> results = SelfCheck.RunAll();
                int pass = 0;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                all.AppendLine("────── ① 自检 ──────");
                all.AppendLine($"自检 {(pass == results.Count ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过");
                all.AppendLine(SelfCheck.Report(results));
            }
            catch (Exception e)
            {
                all.AppendLine("自检 CRASH " + e.GetType().Name + ": " + e.Message);
                all.AppendLine(e.StackTrace ?? "");
            }

            try { all.AppendLine(Run()); }
            catch (Exception e)
            {
                all.AppendLine("PROBE CRASH " + e.GetType().Name + ": " + e.Message);
                all.AppendLine(e.StackTrace ?? "");
            }

            string text = all.ToString();
            try { File.WriteAllText(OutPath, text, new UTF8Encoding(false)); }
            catch (Exception e) { Log.Warn("PROBEV195 写结果文件失败：" + e.Message); }
            Log.Info("PROBEV195 结果已写入 " + OutPath);
            Log.Info("\n" + text);
        }

        public static string Run()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            AppendPlayerTable(sb);
            AppendEnemyTable(sb);
            AppendHitsToKill(sb);
            AppendDrawTable(sb);
            AppendPriceTable(sb);
            AppendRerollTable(sb);
            AppendItemTable(sb);
            AppendFarmIncome(sb);
            AppendNegativeStats(sb);
            AppendBlastRemoved(sb);
            AppendHarvestRatio(sb);
            return sb.ToString();
        }

        // ─────────────────────────── ① 玩家
        private static void AppendPlayerTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            var st = new StatBlock(t.caps, t.player);
            sb.AppendLine("────── ② 玩家数值表 ──────");
            sb.AppendLine($"  hpUnit = {t.player.hpUnit:0.##}（血点 = max(1, round(曲线值 × hpUnit))）");
            sb.AppendLine($"  最大生命 {st.Get(StatId.MaxHp):0.##}｜护盾 {st.Get(StatId.Shield):0.##}" +
                          $"｜爱心恢复 {st.Get(StatId.HeartHeal):0.##}｜起始金币 {t.player.startGold}");
            sb.AppendLine($"  升级每级 +{t.growth.hpPerLevel:0.##} 血（护盾每级 +{t.growth.shieldPerLevel:0.##}）");
            sb.AppendLine($"  武器基础：锄 {t.tools.hoe.baseValue:0.##}（近战）｜壶 {t.tools.can.baseValue:0.##}（远程）" +
                          $"｜肥 {t.tools.fertilizer.baseValue:0.##}（爆炸）");
            string[] names = { "锄头", "水壶", "肥料" };
            ToolDef[] tools = { t.tools.hoe, t.tools.can, t.tools.fertilizer };
            StatId[] stats = { StatId.MeleeDamage, StatId.RangedDamage, StatId.BlastDamage };
            for (int i = 0; i < 3; i++)
            {
                float dmg = DamageResolver.WeaponBlastDamage(tools[i].baseValue, st.Get(stats[i]),
                    st.Get(StatId.AtkMult), false, st.Get(StatId.CritDmg), 0f);
                sb.AppendLine($"    开局一击：{names[i]} = ({tools[i].baseValue:0.##} + {st.Get(stats[i]):0.##})" +
                              $" × 倍率 {st.Get(StatId.AtkMult):0.##} = **{dmg:0.##}**（暴击 {dmg * st.Get(StatId.CritDmg):0.##}）");
            }
            sb.AppendLine($"  爆炸范围倍率 = {st.Get(StatId.ExplodeRange):0.##}（基础半径 {RunSession.BlastBaseRadius(t.tools.fertilizer.baseExplodeRange):0.##} 米）");
        }

        // ─────────────────────────── ② 怪物
        private static void AppendEnemyTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ③ 怪物数值表（base → 第 20 / 30 分钟）──────");
            int[] waves = { 1, 20, 30 };
            for (int i = 0; i < TableDatabase.Enemies.Length; i++)
            {
                EnemyDef d = TableDatabase.Enemies[i];
                sb.Append("  ").Append(d.Id.PadRight(8)).Append(" 血 ").Append(d.BaseHp.ToString("0.##"));
                for (int w = 0; w < waves.Length; w++)
                    sb.Append("｜第").Append(waves[w]).Append("分 ").Append(d.HpForWave(waves[w], 1).ToString("0.#"));
                sb.AppendLine();
                sb.Append("           伤害 base 触碰 ").Append(d.TouchDamage.ToString("0.###"))
                  .Append(" / 弹幕 ").Append(d.ProjectileDamage.ToString("0.###"))
                  .Append(" / 跳砸 ").Append(d.JumpDamage.ToString("0.###")).AppendLine();
                for (int w = 0; w < waves.Length; w++)
                {
                    sb.Append("           第").Append(waves[w]).Append("分曲线值：触碰 ")
                      .Append(d.TouchDamageForWave(waves[w]).ToString("0.###"))
                      .Append("｜弹幕 ").Append(d.ProjectileDamageForWave(waves[w]).ToString("0.###"))
                      .Append("｜跳砸 ").Append(d.JumpDamageForWave(waves[w]).ToString("0.###"))
                      .AppendLine();
                }
            }
        }

        // ─────────────────────────── ③ 几下致死
        private static void AppendHitsToKill(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            int hp = Mathf.Max(1, Mathf.RoundToInt(t.player.maxHp));
            int shield = Mathf.Max(0, Mathf.RoundToInt(t.player.shield));
            int[] waves = { 1, 10, 20, 30 };
            sb.AppendLine("────── ③ 敌人伤害「几下致死」（玩家 " + hp + " 血 + " + shield + " 盾）──────");
            sb.AppendLine("  【口径】下数 = ceil(血 / 每下整数伤害)；每下伤害 = max(1, round(曲线值 × hpUnit))");
            for (int i = 0; i < TableDatabase.Enemies.Length; i++)
            {
                EnemyDef d = TableDatabase.Enemies[i];
                if (d.TouchDamage <= 0f && d.ProjectileDamage <= 0f && d.JumpDamage <= 0f) continue;
                sb.Append("  ").Append(d.Id.PadRight(8));
                for (int w = 0; w < waves.Length; w++)
                {
                    int a = d.TouchDamagePoints(waves[w]);
                    int b = d.ProjectileDamagePoints(waves[w]);
                    int c = d.JumpDamagePoints(waves[w]);
                    int hits = Mathf.CeilToInt(hp / (float)Mathf.Max(a, Mathf.Max(b, c)));
                    int hitsT = a > 0 ? Mathf.CeilToInt(hp / (float)a) : -1;
                    int hitsP = b > 0 ? Mathf.CeilToInt(hp / (float)b) : -1;
                    int hitsJ = c > 0 ? Mathf.CeilToInt(hp / (float)c) : -1;
                    sb.Append("｜第").Append(waves[w]).Append("分 触碰").Append(a.ToString().PadLeft(2))
                      .Append("→").Append((hitsT > 0 ? hitsT + "下" : "—").PadRight(4))
                      .Append(" 弹幕").Append(b.ToString().PadLeft(2))
                      .Append("→").Append((hitsP > 0 ? hitsP + "下" : "—").PadRight(4))
                      .Append(" 跳砸").Append(c.ToString().PadLeft(2))
                      .Append("→").Append((hitsJ > 0 ? hitsJ + "下" : "—").PadRight(4));
                }
                sb.AppendLine();
            }
            sb.AppendLine("  【目标】第 20 分钟：普通/弹幕 8 下、精英 5 下、boss 3 下（8 下那条见交付报告的冲突①）");
        }

        // ─────────────────────────── ④ 抽卡
        private static void AppendDrawTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ④ 抽卡数值表 ──────");
            for (int i = 0; i < t.levelReward.stats.Length; i++)
            {
                RewardStatDef r = t.levelReward.stats[i];
                sb.Append("  ").Append(r.name.PadRight(6)).Append(" ").Append(r.mode.PadRight(8))
                  .Append(" ").Append(r.amount).AppendLine();
            }
            sb.AppendLine($"  抽卡重抽基础价 = {t.levelReward.rerollBasePrice}（与商店共用 `RerollPrice`）" +
                          $"｜每级抽 {t.levelReward.drawsPerLevel} 张");
        }

        // ─────────────────────────── ⑤ 商品价
        private static void AppendPriceTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ⑤ 商品价 1→30 分钟（实付价 = 基础价 × 档位 × 波次）──────");
            sb.Append("   分钟｜波次倍率｜");
            string[] tiers = { "D种子", "C种子", "B种子", "A种子", "S种子", "D道具", "S道具", "插件(29)" };
            for (int i = 0; i < tiers.Length; i++) sb.Append(tiers[i].PadLeft(8)).Append('｜');
            sb.AppendLine();
            int maxAll = 0; string maxWhere = "";
            for (int m = 1; m <= 30; m++)
            {
                sb.Append("  ").Append(m.ToString().PadLeft(5)).Append("｜")
                  .Append(t.shop.WavePriceMult(m).ToString("0.000").PadLeft(8)).Append('｜');
                int[] vals =
                {
                    SeedSlot.ShopPriceFrom(20, 0, m), SeedSlot.ShopPriceFrom(30, 1, m),
                    SeedSlot.ShopPriceFrom(40, 2, m), SeedSlot.ShopPriceFrom(60, 3, m),
                    SeedSlot.ShopPriceFrom(80, 4, m), SeedSlot.ShopPriceFrom(20, 0, m),
                    SeedSlot.ShopPriceFrom(80, 4, m), SeedSlot.ShopPriceFrom(29, 2, m)
                };
                for (int i = 0; i < vals.Length; i++)
                {
                    sb.Append(vals[i].ToString().PadLeft(8)).Append('｜');
                    if (i < 5 && vals[i] > maxAll) { maxAll = vals[i]; maxWhere = tiers[i] + "@" + m + "分"; }
                }
                sb.AppendLine();
            }
            sb.AppendLine($"  【检查·30 分钟每一类】D {SeedSlot.ShopPriceFrom(20, 0, 30)}｜C {SeedSlot.ShopPriceFrom(30, 1, 30)}" +
                          $"｜B {SeedSlot.ShopPriceFrom(40, 2, 30)}｜A {SeedSlot.ShopPriceFrom(60, 3, 30)}" +
                          $"｜S {SeedSlot.ShopPriceFrom(80, 4, 30)}");
            sb.AppendLine($"  【检查·最高价】{maxAll}（{maxWhere}）⇒ ≤2000 ？{(maxAll <= 2000 ? "✓" : "✗")}");
            sb.AppendLine($"  【检查·有人 ≥10000 ？】{(maxAll >= 10000 ? "有 ✗" : "没有 ✓")}");
            sb.AppendLine($"  【检查·D 档 30 分钟是否百位】{SeedSlot.ShopPriceFrom(20, 0, 30)}" +
                          $"（{100 <= SeedSlot.ShopPriceFrom(20, 0, 30) && SeedSlot.ShopPriceFrom(20, 0, 30) < 1000}）");
        }

        // ─────────────────────────── ⑥ 重抽价
        private static void AppendRerollTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ⑥ 重抽价（商店与抽卡共用 RerollPrice；基础价 3）──────");
            sb.AppendLine($"  rerollWaveStep = {t.shop.rerollWaveStep:0.####}｜post20PriceSlope = {t.shop.post20PriceSlope:0.####}" +
                          $"｜RerollMult = 1.5^次数（v195）");
            int[] waves = { 1, 5, 10, 20, 30 };
            for (int w = 0; w < waves.Length; w++)
            {
                int m = waves[w];
                sb.Append("  第").Append(m.ToString().PadLeft(2)).Append("分 基础值 ")
                  .Append(ShopSystem.RerollBaseAt(t, m).ToString().PadLeft(4)).Append("｜倍数 ")
                  .Append(t.shop.RerollWaveMult(m).ToString("0.000")).Append("｜前 6 次：");
                for (int n = 0; n < 6; n++)
                    sb.Append(ShopSystem.RerollPrice(t, t.shop.rerollBase, m, n, 0).ToString().PadLeft(5));
                sb.AppendLine();
            }
            int r1 = ShopSystem.RerollPrice(t, t.shop.rerollBase, 1, 0, 0);
            int r20 = ShopSystem.RerollPrice(t, t.shop.rerollBase, 20, 0, 0);
            int r30 = ShopSystem.RerollPrice(t, t.shop.rerollBase, 30, 0, 0);
            sb.AppendLine($"  【检查·前期个位数】第 1 分钟首次重抽 = {r1} ⇒ {r1 < 10}");
            sb.AppendLine($"  【检查·20 分钟以 10 为单位】= {r20} ⇒ {r20 >= 10 && r20 < 100}");
            sb.AppendLine($"  【检查·30 分钟 ≤100】= {r30} ⇒ {r30 <= 100}");
        }

        // ─────────────────────────── ⑦ 道具
        private static void AppendItemTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ⑦ 道具数值表（实付价 @第 1 / 30 分钟）──────");
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                int tier = (int)it.Grade;
                sb.Append("  ").Append(it.Id.PadRight(16)).Append(it.Grade.ToString().PadRight(2))
                  .Append(" 基础 ").Append(it.Price.ToString().PadLeft(3))
                  .Append(" 实付 ").Append(SeedSlot.ShopPriceFrom(it.Price, tier, 1).ToString().PadLeft(5))
                  .Append(" → ").Append(SeedSlot.ShopPriceFrom(it.Price, tier, 30).ToString().PadLeft(6))
                  .Append(" ｜");
                for (int e = 0; e < it.Effects.Length; e++)
                    sb.Append('+').Append(it.Effects[e].Stat).Append(' ').Append(it.Effects[e].Label).Append("  ");
                for (int c = 0; c < it.Costs.Length; c++)
                    sb.Append('-').Append(it.Costs[c].Stat).Append(' ').Append(it.Costs[c].Label).Append("  ");
                sb.AppendLine();
            }
        }

        // ─────────────────────────── ⑧ 满配农场收入
        private static void AppendFarmIncome(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            int rows = t.farm.maxRows, cols = t.farm.gridSize;
            int cells = rows * cols;
            float scale = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
            sb.AppendLine("────── ⑧ 满配农场每分钟收入 ──────");
            sb.AppendLine($"  配置：{cols} 列 × {rows} 行 = **{cells} 格**全解锁、全部 3 级（等级倍率 ×{SeedEconomy.YieldMult(SeedSlot.HarvestPerLevel, 3):0.00}）");
            sb.AppendLine($"  金币系数 harvestGoldScale = {scale:0.####}");
            SeedDef best = null; float bestRate = -1f;
            float sumS = 0f; int nS = 0;
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                float rate = SeedEconomy.CellGold(d) * SeedEconomy.YieldMult(SeedSlot.HarvestPerLevel, 3) / d.GrowTime;
                sb.Append("  ").Append(d.Id.PadRight(18)).Append(d.Grade.ToString().PadRight(2))
                  .Append(" 每格金 ").Append(SeedEconomy.CellGold(d).ToString().PadLeft(3))
                  .Append(" 成熟 ").Append(d.GrowTime.ToString("0.#").PadLeft(6)).Append("s")
                  .Append(" ⇒ ").Append(rate.ToString("0.0000").PadLeft(8)).Append(" 金/秒·格")
                  .Append(" ⇒ 36 格 ").Append((rate * cells * 60f * scale).ToString("0").PadLeft(7)).Append(" 金/分")
                  .AppendLine();
                if (d.Grade == SeedGrade.S) { sumS += rate; nS++; }
                if (rate > bestRate) { bestRate = rate; best = d; }
            }
            float perMin = bestRate * cells * 60f * scale;
            float avgS = nS > 0 ? sumS / nS * cells * 60f * scale : 0f;
            int legendary30 = SeedSlot.ShopPriceFrom(80, 4, 30);
            sb.AppendLine($"  【最高配置】全种 {best.Id}（金产最高的品质档）⇒ **{perMin:0} 金/分**");
            sb.AppendLine($"  【S 档平均】⇒ {avgS:0} 金/分");
            sb.AppendLine($"  【对照】30 分钟传奇实付价 = {legendary30} ⇒ 每分钟收入 / 传奇价 = {perMin / Mathf.Max(1, legendary30):0.00}" +
                          $" ⇒ {(perMin >= legendary30 * 0.9f && perMin <= legendary30 * 1.2f ? "≈ 恰好买一个 ✓" : "偏离 ✗")}");
        }

        // ─────────────────────────── ⑨ 负数属性
        private static void AppendNegativeStats(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ⑨ 负数属性四条判据 ──────");

            // ① 三个伤害属性：−3 与 0 逐位等价
            sb.AppendLine("  【判据①·伤害类负值视作 0】三把武器各一组（−3 / 0 / +3）");
            ToolDef[] tools = { t.tools.hoe, t.tools.can, t.tools.fertilizer };
            StatId[] stats = { StatId.MeleeDamage, StatId.RangedDamage, StatId.BlastDamage };
            string[] names = { "锄头", "水壶", "肥料" };
            for (int i = 0; i < 3; i++)
            {
                var st = new StatBlock(t.caps, t.player);
                st.SetBase(stats[i], -3f); float vm = DamageResolver.WeaponDamage(tools[i].baseValue, st.Get(stats[i]), 1f, false, 2f, 0.1f);
                st.SetBase(stats[i], 0f); float v0 = DamageResolver.WeaponDamage(tools[i].baseValue, st.Get(stats[i]), 1f, false, 2f, 0.1f);
                st.SetBase(stats[i], 3f); float vp = DamageResolver.WeaponDamage(tools[i].baseValue, st.Get(stats[i]), 1f, false, 2f, 0.1f);
                sb.AppendLine($"    {names[i]}：−3 ⇒ {vm:0.####}｜0 ⇒ {v0:0.####}｜+3 ⇒ {vp:0.####}" +
                              $"｜(−3 == 0) = {Mathf.Abs(vm - v0) < 0.0001f}｜(-3 不是被 minDamage 兜成 0.1) = {vm > 0.1f}");
            }
            // 存储值保持负（面板要看得见减益）
            {
                var st = new StatBlock(t.caps, t.player);
                st.SetBase(StatId.MeleeDamage, -3f);
                sb.AppendLine($"    存储值（面板读的那个）：Get(MeleeDamage) = {st.Get(StatId.MeleeDamage):0.##}" +
                              $" ⇒ 负值被保留 ✓（下限 = {StatBlock.FloorOfStat(StatId.MeleeDamage)} = 负无穷）");
            }

            // ② 点数类：负值被钳到 0（MaxHp 钳到 1）
            sb.AppendLine("  【判据②·点数类负值钳到下限】");
            StatId[] pointStats =
            {
                StatId.MaxHp, StatId.Shield, StatId.HeartHeal, StatId.HeartDropRate,
                StatId.PigRate, StatId.CritRate, StatId.Luck, StatId.Knockback, StatId.Dodge, StatId.MaxPlots
            };
            for (int i = 0; i < pointStats.Length; i++)
            {
                StatId id = pointStats[i];
                var st = new StatBlock(t.caps, t.player);
                float b = st.Get(id);
                st.AddModifier(new StatModifier(id, -9999f, ModOp.Add, "probe_floor"));
                sb.AppendLine($"    {id,-14} 基底 {b,8:0.####} + (−9999) ⇒ **{st.Get(id):0.####}**" +
                              $"（下限 {StatBlock.FloorOfStat(id):0.####}）");
            }

            // ③ 倍率类：负值被钳到安全下限（> 0）
            sb.AppendLine("  【判据③·倍率类负值钳到安全下限（不除零 / 不出负伤害）】");
            StatId[] multStats =
            {
                StatId.AtkMult, StatId.AtkSpeed, StatId.AtkRange, StatId.MoveSpeed,
                StatId.ExpMult, StatId.GoldMult, StatId.CritDmg, StatId.ExplodeRange
            };
            for (int i = 0; i < multStats.Length; i++)
            {
                StatId id = multStats[i];
                var st = new StatBlock(t.caps, t.player);
                st.AddModifier(new StatModifier(id, -9999f, ModOp.Mul, "probe_floor"));
                float v = st.Get(id);
                sb.AppendLine($"    {id,-14} × (−9999) ⇒ **{v:0.####}**（下限 {StatBlock.FloorOfStat(id):0.####}）");
            }
            // 攻速下限的直接后果：攻击间隔仍然有限
            {
                var st = new StatBlock(t.caps, t.player);
                st.AddModifier(new StatModifier(StatId.AtkSpeed, -9999f, ModOp.Mul, "probe_floor"));
                float cd = t.tools.hoe.cooldown / Mathf.Max(0.05f, st.Get(StatId.AtkSpeed));
                sb.AppendLine($"    攻速触底后锄头间隔 = {t.tools.hoe.cooldown:0.#} / {st.Get(StatId.AtkSpeed):0.##} = {cd:0.##} 秒（有限 ✓）");
            }

            // ④ 真实减益仍生效
            sb.AppendLine("  【判据④·表里真实存在的减益仍然生效】");
            string[] ids = { "glass_cannon", "berserk_mask", "night_lamp", "frenzy_band", "thorn_whip" };
            for (int i = 0; i < ids.Length; i++)
            {
                ItemDef it = TableDatabase.GetItem(ids[i]);
                if (it == null) { sb.AppendLine($"    {ids[i]} 不在表里 ✗"); continue; }
                var st = new StatBlock(t.caps, t.player);
                float beforeV = 0f, afterV = 0f; string statName = "";
                for (int c = 0; c < it.Costs.Length; c++)
                {
                    ItemBonus cb = it.Costs[c];
                    beforeV = st.Get(cb.Stat);
                    if (cb.IsPercent) st.AddModifier(new StatModifier(cb.Stat, 1f - cb.Add / 100f, ModOp.Mul, ids[i]));
                    else st.AddModifier(new StatModifier(cb.Stat, -cb.Add, ModOp.Add, ids[i]));
                    afterV = st.Get(cb.Stat);
                    statName = cb.Stat.ToString();
                }
                sb.AppendLine($"    {it.Id,-14} 代价 {statName,-12} {beforeV:0.####} → **{afterV:0.####}**" +
                              $" ⇒ 生效 {Mathf.Abs(beforeV - afterV) > 0.0001f}");
            }
        }

        // ─────────────────────────── ⑩ 爆炸倍率删干净
        private static void AppendBlastRemoved(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ⑩ 「爆炸倍率」删除证据 ──────");
            bool parse = StatIds.TryParse("ExplodeDamage", out StatId sid);
            sb.AppendLine($"  StatIds.TryParse(\"ExplodeDamage\") = {parse}（期望 false）");
            sb.AppendLine($"  StatIds.Count = {StatIds.Count}｜枚举成员 = {StatIds.All.Length}｜两者相等 = {StatIds.Count == StatIds.All.Length}");
            sb.AppendLine($"  枚举全文：{string.Join(",", StatIds.All)}");
            sb.AppendLine($"  tuning.player.explodeDamage / caps.explodeDamage 已无此字段（编译期即证）");
            sb.AppendLine("  三件原本引用它的道具改后原文：");
            string[] three = { "blast_grain", "fertile_core", "big_bore" };
            for (int i = 0; i < three.Length; i++)
            {
                ItemDef it = TableDatabase.GetItem(three[i]);
                if (it == null) { sb.AppendLine($"    {three[i]} 不在表里 ✗"); continue; }
                sb.Append("    ").Append(it.Id).Append('（').Append(it.Name).Append("）effects：");
                for (int e = 0; e < it.Effects.Length; e++)
                    sb.Append(it.Effects[e].Stat).Append(' ').Append(it.Effects[e].Label).Append("；");
                sb.Append(" costs：");
                for (int c = 0; c < it.Costs.Length; c++)
                    sb.Append(it.Costs[c].Stat).Append(' ').Append(it.Costs[c].Label).Append("；");
                sb.Append(" desc ").Append(it.Desc.Length > 40 ? it.Desc.Substring(0, 40) + "…" : it.Desc).AppendLine();
            }
            // 肥料公式读数
            var st2 = new StatBlock(t.caps, t.player);
            float fert = DamageResolver.WeaponBlastDamage(t.tools.fertilizer.baseValue, st2.Get(StatId.BlastDamage),
                st2.Get(StatId.AtkMult), false, st2.Get(StatId.CritDmg), 0f);
            sb.AppendLine($"  肥料伤害（新式子） = ({t.tools.fertilizer.baseValue:0.##} + {st2.Get(StatId.BlastDamage):0.##})" +
                          $" × {st2.Get(StatId.AtkMult):0.##} = **{fert:0.##}** —— 与锄头/水壶同一条式子 ✓");
        }

        // ─────────────────────────── ⑪ 爆炸收割
        private static void AppendHarvestRatio(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ⑪ 爆炸收割收益（1/4）──────");
            float r = t.farm.blastHarvestRatio;
            float gScale = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
            sb.AppendLine($"  farm.blastHarvestRatio = {r:0.######}（= 1/4 ? {Mathf.Abs(r - 0.25f) < 1e-4f}）" +
                          $"｜harvestGoldScale = {gScale:0.####}（两边都要乘它，才是**同一个口径**的比较）");
            // 三档基数各取一株：1 级 D（小数字，取整主导）/ 3 级 A / 3 级 S（系数主导）
            sb.AppendLine("  【口径】手收 = max(1, round(goldYield × 等级倍率 × 金币系数))；" +
                          "炸收 = max(1, round(同一个基数 × 系数))");
            string[] ids = { "radish_guard", "plague_mother", "life_spring" };
            int[] lvls = { 1, 3, 3 };
            for (int i = 0; i < ids.Length; i++)
            {
                SeedDef d = TableDatabase.GetSeed(ids[i]);
                if (d == null) continue;
                float ym = SeedEconomy.YieldMult(SeedSlot.HarvestPerLevel, lvls[i]);
                int handG = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(d) * ym * gScale));
                int handE = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellExp(d) * ym));
                int blastG = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(d) * ym * gScale * r));
                int blastE = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellExp(d) * ym * r));
                sb.AppendLine($"    {d.Id}（{d.Grade}，{lvls[i]} 级）手收 金 {handG} / 验 {handE}" +
                              $"｜炸收 金 {blastG} / 验 {blastE}" +
                              $"（金币比值 {blastG / (float)handG:0.###}｜经验比值 {blastE / (float)handE:0.###}）");
            }
            sb.AppendLine("  ⚠️ 小数字刻度下**取整会主导比值**：D 档 1 级单格只有几位数金币，" +
                          "×0.25 后只剩 0.x ⇒ `max(1, …)` 把它兜成 1 ⇒ 比值看起来是 1/2~1/3 ✗");
            sb.AppendLine("     系数本身是**精确的 0.25**（上表 S 档大数字那一行 ≈0.25 就是证据）；" +
                          "这是本版「个位刻度」的必然代价，已如实上报 ✓");
            sb.AppendLine($"  （改前 = 0.33333334；本轮 1/3 → 1/4，用户原话『改为获取 1/4 金币和经验』）");
        }
    }
}
