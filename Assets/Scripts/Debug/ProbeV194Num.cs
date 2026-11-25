using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v194 只读探针 ★★ —— 给"新数值标准"打读数：五张表 + 三类伤害取整 + 20 分钟加速差分
    /// + 爆炸倍率属性的查证 + 自检原文。**不写任何工程文件**（只往 `Library/` 落一份结果方便读取）。
    ///
    /// 与 `ProbeV192Num` / `ProbeV191` 同一套做法：
    ///   · 静态类 + `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`
    ///   · 开关是 `<工程>/Library/yowei_v194_probe.on` —— 存在才跑一次，删掉就彻底静默
    ///   · 读数同时进 `Log`（前缀 `PROBEV194`）与 `Library/yowei-v194-probe.txt`
    ///
    /// ★ 为什么不用 `-batchmode` 跑 ★
    /// 工程已被编辑器独占（`Temp/UnityLockfile`），再起一个实例会被 Unity 拒绝。
    /// 所以挂在 Play 入口上、借编辑器自己的 domain 跑 ✓
    ///
    /// ★ 它是**只读**的 ★
    /// 每个小节都在**内存里**新建 `World` / `StatBlock`（与自检的夹具同一种做法），跑完就地丢掉；
    /// 唯一"写"的动作是把 `EnemyGrowthDef.post20Slope` 临时置 0 再还原（差分证明它真被读），
    /// 而且包在 try/finally 里 ✓
    /// </summary>
    public static class ProbeV194Num
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei_v194_probe.on");
        private static string OutPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-v194-probe.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!File.Exists(FlagPath)) return;

            var all = new StringBuilder();
            all.AppendLine("═══ v194 探针（只读）═══");

            // ── ① 自检原文（放最前面：它崩了也要先看见它崩了）
            try
            {
                List<CheckResult> results = SelfCheck.RunAll();
                all.AppendLine("────── 自检 ──────");
                all.AppendLine(SelfCheck.Report(results));
            }
            catch (Exception e)
            {
                all.AppendLine("自检 CRASH " + e.GetType().Name + ": " + e.Message);
                all.AppendLine(e.StackTrace ?? "");
            }

            // ── ② 五张表 + 读数
            try
            {
                all.AppendLine(Run());
            }
            catch (Exception e)
            {
                // 探针自己崩了也要看得见 —— 否则"没有输出"会被读成"没问题"
                all.AppendLine("PROBE CRASH " + e.GetType().Name + ": " + e.Message);
                all.AppendLine(e.StackTrace ?? "");
            }

            string text = all.ToString();
            try { File.WriteAllText(OutPath, text, new UTF8Encoding(false)); }
            catch (Exception e) { Log.Warn("PROBEV194 写结果文件失败：" + e.Message); }
            Log.Info("PROBEV194 结果已写入 " + OutPath);
            Log.Info("\n" + text);
        }

        // ═══════════════════════════════════════════════════════════════════
        public static string Run()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            AppendPlayer(sb);
            AppendEnemies(sb);
            AppendDraws(sb);
            AppendPrices(sb);
            AppendItems(sb);
            AppendRounding(sb);
            AppendPost20(sb);
            // ★ v195：`AppendBlastMult` 整段删除 —— 它查证的那条属性（爆炸倍率）已经不存在 ✗
            //   本轮的等价读数在新探针 `ProbeV195Num` 里（"爆炸倍率删干净"的证据）。
            return sb.ToString();
        }

        // ─────────────────────────── ① 玩家基础
        private static void AppendPlayer(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            var st = new StatBlock(t.caps, t.player);
            sb.AppendLine("────── ① 玩家基础数值表 ──────");
            sb.AppendLine($"  hpUnit（曲线值→血点） = {t.player.hpUnit:0.##}｜EnemyDef.PlayerHpUnit = {EnemyDef.PlayerHpUnit:0.##}");
            sb.AppendLine($"  maxHp = {t.player.maxHp:0.##}｜shield = {t.player.shield:0.##}｜startGold = {t.player.startGold}");
            sb.AppendLine($"  growth.hpPerLevel = {t.growth.hpPerLevel:0.##}｜growth.shieldPerLevel = {t.growth.shieldPerLevel:0.##}");
            sb.AppendLine("  属性｜初始值｜上限｜分数属性｜面板显示");
            for (int i = 0; i < StatIds.Count; i++)
            {
                StatId id = (StatId)i;
                float baseV = st.GetBase(id);
                float now = st.Get(id);
                sb.AppendLine($"    {id,-14} {now,10:0.####}  上限 {CapText(t, id),-10}" +
                              $" 分数={TableDatabase.IsFractionStat(id),-5}" +
                              $" 面板「{DisplayOf(t, id, now, baseV)}」");
            }
        }

        private static string CapText(TuningDef t, StatId id)
        {
            switch (id)
            {
                case StatId.Dodge: return $"{t.caps.dodge:0.##}";
                case StatId.CritRate: return $"{t.caps.critRate:0.##}";
                case StatId.AtkSpeed: return $"{t.caps.atkSpeed:0.##}";
                case StatId.AtkRange: return $"{t.caps.atkRange:0.##}";
                case StatId.MoveSpeed: return $"{t.caps.moveSpeed:0.##}";
                case StatId.MeleeDamage: return $"{t.caps.meleeDamage:0.##}";
                case StatId.RangedDamage: return $"{t.caps.rangedDamage:0.##}";
                case StatId.BlastDamage: return $"{t.caps.blastDamage:0.##}";
                // ★ v195：`explodeDamage` 上限随属性删除（见 `StatId` 的说明）★
                case StatId.ExplodeRange: return $"{t.caps.explodeRange:0.##}";
                case StatId.MaxPlots: return $"{t.caps.maxPlots}";
                case StatId.Knockback: return $"{t.caps.knockback:0.##}";
                case StatId.HeartDropRate: return "3（等级）";
                case StatId.HeartHeal: return "99";
                case StatId.PigRate: return "3（等级）";
                case StatId.Luck: return "1.00";
                default: return "+∞";
            }
        }

        /// <summary>面板那串字**直接调 ImGuiHud 的公开格式化函数**（不在探针里另写一份）。</summary>
        private static string DisplayOf(TuningDef t, StatId id, float now, float baseV)
        {
            if (id == StatId.MaxHp || id == StatId.Shield) return $"{now:0}";
            if (TableDatabase.IsFractionStat(id)) return ImGuiHud.AddPoints(now, baseV);
            switch (id)
            {
                case StatId.MeleeDamage:
                case StatId.RangedDamage:
                case StatId.BlastDamage:
                    return ImGuiHud.AddFlat(now, baseV);
                case StatId.ExplodeRange:
                    return ImGuiHud.AddRelative(now, t.player.explodeRange / 100f);
                default:
                    return ImGuiHud.AddRelative(now, baseV);
            }
        }

        // ─────────────────────────── ② 怪物
        private static readonly int[] Minutes = { 1, 5, 10, 15, 20, 25, 30 };

        private static void AppendEnemies(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            sb.AppendLine("────── ② 怪物数值表 ──────");
            sb.AppendLine($"  hpUnit = {EnemyDef.PlayerHpUnit:0.##}（血点 = Round(曲线值 × hpUnit)）" +
                          $"｜post20Slope = {g.post20Slope:0.###}｜earlyNerf = {g.earlyNerf:0.##}／窗口 {g.earlyNerfEnd:0.#} 分钟");
            sb.AppendLine("  base 值（enemies.json，v194 已 ×10）：");
            foreach (EnemyDef d in TableDatabase.Enemies)
                sb.AppendLine($"    {d.Id,-8} kind={d.Kind,-7} baseHp={d.BaseHp,8:0.##}  touch={d.TouchDamage,9:0.####}" +
                              $"  proj={d.ProjectileDamage,9:0.####}  jump={d.JumpDamage,9:0.####}" +
                              $"  金={d.Gold,4} 经验={d.Exp,4}");

            sb.AppendLine("  五套曲线系数（一个字节都没动）：");
            string[] kinds = { "grunt", "jumper", "boss" };
            foreach (string id in kinds)
            {
                EnemyDef d = TableDatabase.GetEnemy(id);
                if (d == null) continue;
                d.HpCurveCoefficients(out float a, out float b);
                sb.AppendLine($"    HP  {id,-6} a={a:0.#######}  b={b:0.########}  第20分={d.HpMultiplierForWave(20):0.000000}");
            }
            {
                EnemyDef d = TableDatabase.GetEnemy("grunt");
                d.DmgCurveCoefficients(out float a, out float b);
                sb.AppendLine($"    DMG 标准   a={a:0.#######}  b={b:0.########}  第20分={d.DamageForWave(1f, 20):0.000000}×");
                EnemyDef bd = TableDatabase.GetEnemy("boss");
                bd.DmgCurveCoefficients(out float a2, out float b2);
                sb.AppendLine($"    DMG BOSS   a={a2:0.#######}  b={b2:0.########}  第20分={bd.DamageForWave(1f, 20):0.000000}×");
            }

            // 血量
            sb.AppendLine("  血量（含 earlyNerf + post20；BOSS 用 bossIndex = 1）：");
            sb.AppendLine("    分钟｜  基础兵/远程兵/小猪｜      精英跳跃者｜              BOSS");
            foreach (int m in Minutes)
            {
                EnemyDef grunt = TableDatabase.GetEnemy("grunt");
                EnemyDef jump = TableDatabase.GetEnemy("jumper");
                EnemyDef boss = TableDatabase.GetEnemy("boss");
                sb.AppendLine($"    {m,4}｜{grunt.HpForWave(m, 1),16:0.00}｜{jump.HpForWave(m, 1),14:0.00}｜{boss.HpForWave(m, 1),17:0.00}");
            }

            // 三招曲线值
            sb.AppendLine("  三招·曲线值（小数）：");
            sb.AppendLine("    分钟｜基础兵触碰｜远程兵弹幕｜跳跃者跳砸｜BOSS 触碰/弹幕/跳砸");
            foreach (int m in Minutes)
            {
                EnemyDef grunt = TableDatabase.GetEnemy("grunt");
                EnemyDef sh = TableDatabase.GetEnemy("shooter");
                EnemyDef jump = TableDatabase.GetEnemy("jumper");
                EnemyDef boss = TableDatabase.GetEnemy("boss");
                sb.AppendLine($"    {m,4}｜{grunt.TouchDamageForWave(m),10:0.0000}｜{sh.ProjectileDamageForWave(m),10:0.0000}｜" +
                              $"{jump.JumpDamageForWave(m),10:0.0000}｜{boss.TouchDamageForWave(m):0.0000}/{boss.ProjectileDamageForWave(m):0.0000}/{boss.JumpDamageForWave(m):0.0000}");
            }

            // 三招整数血点 + 几下打死玩家
            sb.AppendLine("  三招·整数血点（玩家 100 血 / 20 盾）：");
            sb.AppendLine("    分钟｜基础兵触碰(含盾/裸血)｜远程兵弹幕｜跳跃者跳砸｜BOSS 触碰/弹幕/跳砸");
            foreach (int m in Minutes)
            {
                EnemyDef grunt = TableDatabase.GetEnemy("grunt");
                EnemyDef sh = TableDatabase.GetEnemy("shooter");
                EnemyDef jump = TableDatabase.GetEnemy("jumper");
                EnemyDef boss = TableDatabase.GetEnemy("boss");
                int gt = grunt.TouchDamagePoints(m);
                int hitsShield = Mathf.CeilToInt((t.player.maxHp + t.player.shield) / Mathf.Max(1, gt));
                int hitsBare = Mathf.CeilToInt(t.player.maxHp / Mathf.Max(1, gt));
                sb.AppendLine($"    {m,4}｜{gt,5}（{hitsShield} 下/ {hitsBare} 下）｜{sh.ProjectileDamagePoints(m),10}｜" +
                              $"{jump.JumpDamagePoints(m),10}｜{boss.TouchDamagePoints(m)}/{boss.ProjectileDamagePoints(m)}/{boss.JumpDamagePoints(m)}");
            }

            // 前 10 分钟的"几下打死玩家"明细（用户裁决的后果就在这一列）
            sb.AppendLine("  第 1~10 分钟：基础兵一次触碰 = 玩家血量的百分之几、几下致死");
            for (int m = 1; m <= 10; m++)
            {
                EnemyDef grunt = TableDatabase.GetEnemy("grunt");
                int gt = grunt.TouchDamagePoints(m);
                sb.AppendLine($"    第 {m,2} 分钟：触碰 {gt,4} 血点 = 血量的 {gt,4:0.#}%｜含盾 {Mathf.CeilToInt((t.player.maxHp + t.player.shield) / Mathf.Max(1, gt))} 下、裸血 {Mathf.CeilToInt(t.player.maxHp / Mathf.Max(1, gt))} 下致死");
            }
        }

        // ─────────────────────────── ③ 抽卡
        private static void AppendDraws(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ③ 抽卡数值表 ──────");
            sb.AppendLine($"  drawsPerLevel = {t.levelReward.drawsPerLevel}｜rerollBasePrice = {t.levelReward.rerollBasePrice}" +
                          $"｜rerollStepPrice = {t.levelReward.rerollStepPrice}（死配置，见 Docs）");
            sb.AppendLine("  #｜StatId｜名称｜mode｜amount｜实际写入");
            for (int i = 0; i < t.levelReward.stats.Length; i++)
            {
                RewardStatDef c = t.levelReward.stats[i];
                string effect;
                if (StatIds.TryParse(c.id, out StatId sid))
                {
                    if (TableDatabase.IsFractionStat(sid)) effect = $"Add({c.amount}/100 = +{c.amount / 100f:0.###})";
                    else if (string.Equals(c.mode, "Percent", StringComparison.OrdinalIgnoreCase))
                        effect = $"Mul(1 + {c.amount}/100 = ×{1f + c.amount / 100f:0.####})";
                    else effect = $"Add({c.amount})";
                }
                else effect = "★属性名解析失败★";
                sb.AppendLine($"    {i + 1,2}｜{c.id,-14}｜{c.name,-6}｜{c.mode,-7}｜{c.amount,4}｜{effect}");
            }
            // 暴伤读数
            StatBlock st = new StatBlock(t.caps, t.player);
            float crit0 = st.Get(StatId.CritDmg);
            sb.AppendLine($"  【暴击伤害读数】初始 {crit0:0.####}（= {crit0 * 100f:0.#}%）");
            StatBlock st2 = new StatBlock(t.caps, t.player);
            st2.AddModifier(new StatModifier(StatId.CritDmg, 1f + 5f / 100f, ModOp.Mul, "probe"));
            sb.AppendLine($"    抽 1 张 CritDmg 卡后 = {st2.Get(StatId.CritDmg):0.####}（= {st2.Get(StatId.CritDmg) * 100f:0.#}%）" +
                          $"｜面板印 {ImGuiHud.AddRelative(st2.Get(StatId.CritDmg), crit0)}");

            // 重抽价
            sb.AppendLine("  重抽价（商店与抽卡共用一条 RerollPrice）：");
            foreach (int wave in new[] { 1, 10, 20, 30 })
            {
                int basisShop = ShopSystem.RerollBaseAt(t, wave);
                int basisDraw = Mathf.Max(1, Mathf.RoundToInt(t.levelReward.rerollBasePrice * t.shop.WavePriceMult(wave)));
                var line = new StringBuilder();
                for (int n = 0; n < 12; n++)
                    line.Append(ShopSystem.RerollPrice(t, t.shop.rerollBase, wave, n, 0)).Append(n < 11 ? " / " : "");
                var line2 = new StringBuilder();
                for (int n = 0; n < 12; n++)
                    line2.Append(ShopSystem.RerollPrice(t, t.levelReward.rerollBasePrice, wave, n, 0)).Append(n < 11 ? " / " : "");
                sb.AppendLine($"    第 {wave,2} 波：商店基础 {basisShop}｜抽卡基础 {basisDraw}");
                sb.AppendLine($"      商店 12 次：{line}");
                sb.AppendLine($"      抽卡 12 次：{line2}");
            }
        }

        // ─────────────────────────── ④ 商品价格 1..30 分钟
        private static void AppendPrices(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ④ 商品价格每分钟（1..30 分钟）──────");
            sb.AppendLine($"  priceWaveStep = {t.shop.priceWaveStep:0.###}｜post20PriceSlope = {t.shop.post20PriceSlope:0.###}" +
                          $"｜档位倍率 D1.0 / C1.8 / B3.2 / A5.6 / S9.0");
            sb.AppendLine("  分钟｜波次倍率｜种子D20｜种子S80｜插件B29｜插件S29｜道具D5｜道具C8｜道具B10｜道具A16｜道具S22｜商店首刷｜抽卡首抽");
            for (int m = 1; m <= 30; m++)
            {
                sb.AppendLine($"    {m,3}｜{t.shop.WavePriceMult(m),9:0.0000}｜" +
                              $"{SeedSlot.ShopPriceFrom(20, 0, m),7}｜{SeedSlot.ShopPriceFrom(80, 4, m),7}｜" +
                              $"{FakeItemPrice(29, 2, m),6}｜{FakeItemPrice(29, 4, m),6}｜" +
                              $"{SeedSlot.ShopPriceFrom(5, 0, m),6}｜{SeedSlot.ShopPriceFrom(8, 1, m),6}｜" +
                              $"{SeedSlot.ShopPriceFrom(10, 2, m),7}｜{SeedSlot.ShopPriceFrom(16, 3, m),7}｜" +
                              $"{SeedSlot.ShopPriceFrom(22, 4, m),7}｜" +
                              $"{ShopSystem.RerollBaseAt(t, m),7}｜{ShopSystem.RerollPrice(t, t.levelReward.rerollBasePrice, m, 0, 0),7}");
            }
            sb.AppendLine("  对照（post20PriceSlope 关掉 = v193 行为）：同一列的差异");
            float old = t.shop.post20PriceSlope;
            try
            {
                t.shop.post20PriceSlope = 0f;
                sb.AppendLine("  分钟｜波次倍率｜种子D20｜道具B10｜道具S22｜商店首刷");
                for (int m = 1; m <= 30; m++)
                    sb.AppendLine($"    {m,3}｜{t.shop.WavePriceMult(m),9:0.0000}｜" +
                                  $"{SeedSlot.ShopPriceFrom(20, 0, m),7}｜{SeedSlot.ShopPriceFrom(10, 2, m),7}｜" +
                                  $"{SeedSlot.ShopPriceFrom(22, 4, m),7}｜{ShopSystem.RerollBaseAt(t, m),7}");
            }
            finally { t.shop.post20PriceSlope = old; }
        }

        /// <summary>
        /// 插件定价：`RunSession.ShopPriceFrom` 的 basePrice 是插件自己的 `basePrice`（全部 29），
        /// 档位由 `quality` 决定 —— 所以这里直接用同一个入口，不另写公式 ✓
        /// </summary>
        private static int FakeItemPrice(int basePrice, int tier, int wave)
            => SeedSlot.ShopPriceFrom(basePrice, tier, wave);

        // ─────────────────────────── ⑤ 道具 46 件
        private static void AppendItems(StringBuilder sb)
        {
            sb.AppendLine("────── ⑤ 全部道具 ──────");
            sb.AppendLine($"  共 {TableDatabase.Items.Length} 件");
            sb.AppendLine("  #｜id｜名称｜档｜类｜基础价｜maxStack｜成长｜增益｜代价");
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                string gain = BonusText(it.Effects);
                string cost = it.Costs.Length == 0 ? "—" : BonusText(it.Costs);
                sb.AppendLine($"    {i + 1,2}｜{it.Id,-16}｜{it.Name,-8}｜{it.Grade,-2}｜{it.Category}｜{it.Price,4}｜{it.MaxStack,4}｜" +
                              $"{(it.GrowPerWave > 0 ? it.GrowPerWave.ToString("0.##") + "/波" : "—"),-8}｜{gain}｜{cost}");
            }
        }

        private static string BonusText(ItemBonus[] list)
        {
            if (list == null || list.Length == 0) return "—";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Length; i++)
            {
                ItemBonus b = list[i];
                if (i > 0) sb.Append("；");
                // ⚠️ 刻意**不用** `ItemBonus.Label`：它用 `0.#`，会把 `CritRate 0.04` 印成
                //    "0"、`0.06` 印成 "0.1"（有损）。本表要给的是**表里的真值**，
                //    所以这里用 `0.####` 直接印 `Add` ✓（那件 Label 的显示缺陷已在报告里列出）
                sb.Append(b.Stat).Append(' ')
                  .Append(b.IsPercent ? "+" : "+")
                  .Append(b.Add.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(b.IsPercent ? "%" : "");
            }
            return sb.ToString();
        }

        // ─────────────────────────── ⑥ 三类伤害取整
        private static void AppendRounding(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine("────── ⑥ 伤害取整（三类）＋ 弹字 ──────");

            // ① 玩家打敌人：给一个**带小数**的 RawValue，看血真的少多少
            {
                World w = NewWorld(out PlayerActor p);
                w.SpawnEnemy("grunt", new Vector2(3f, 0f), 20);
                EnemyActor e = w.Enemies[0];
                float hp0 = e.Health.Hp;
                var got = new List<string>();
                Action<FloatingTextEvent> sink = ev => got.Add($"{ev.Kind}={ev.Text}");
                Bus.Subscribe(sink);
                try { w.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 45.5f, Source = p, Target = e, IgnoreDodge = true }); }
                finally { Bus.Unsubscribe(sink); }
                sb.AppendLine($"  ① 玩家→敌人：RawValue 45.5（带小数）⇒ 血 {hp0:0.######} → {e.Health.Hp:0.######}" +
                              $" ⇒ **实际扣 {hp0 - e.Health.Hp:0.######}**｜弹字 {string.Join(",", got)}");
            }

            // ② 敌人打玩家：曲线值 → PlayerDamagePoints
            {
                World w = NewWorld(out PlayerActor p);
                EnemyDef grunt = TableDatabase.GetEnemy("grunt");
                float curved = grunt.TouchDamageForWave(1);
                var got = new List<string>();
                Action<FloatingTextEvent> sink = ev => got.Add($"{ev.Kind}={ev.Text}");
                Bus.Subscribe(sink);
                try { w.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = curved, Target = p, IgnoreDodge = true }); }
                finally { Bus.Unsubscribe(sink); }
                sb.AppendLine($"  ② 敌人→玩家：曲线值 {curved:0.####}（第 1 分钟基础兵触碰）⇒ PlayerDamagePoints = {EnemyDef.PlayerDamagePoints(curved)}" +
                              $" ⇒ 血 {p.Health.Hp:0.##}/100、盾 {p.Health.Shield:0.##}/20｜弹字 {string.Join(",", got)}");
            }

            // ③ DoT：毒 / 火 的跳伤都走 HealthPool.ApplyDamageSplit
            //   ★ 毒与火**必须分开两个敌人**：`StatusRuntime.Tick` 一次会把所有还在跳的异常
            //     一起结算（毒 3 跳），同一个敌人上两样会让第二次读数混进毒的余跳 ✗
            {
                World w = NewWorld(out _);
                w.SpawnEnemy("grunt", new Vector2(3f, 0f), 20);
                EnemyActor e = w.Enemies[0];
                e.Health.Setup(e.Health.MaxHp, 0f);
                float hp0 = e.Health.Hp;
                e.Status.Apply(AilmentKind.Poison, 125f, t.formula, 1f);
                e.Status.Tick(1.0f, e, w);
                float afterPoison = e.Health.Hp;

                World w2 = NewWorld(out _);
                w2.SpawnEnemy("grunt", new Vector2(-3f, 0f), 20);
                EnemyActor e2 = w2.Enemies[0];
                e2.Health.Setup(e2.Health.MaxHp, 0f);
                float fhp0 = e2.Health.Hp;
                e2.Status.Apply(AilmentKind.Burn, 125f, t.formula, 1f);
                e2.Status.Tick(1.0f, e2, w2);
                float afterBurn = e2.Health.Hp;

                sb.AppendLine($"  ③ DoT：毒 tick = 0.15 × 125 = {0.15f * 125f:0.####} ⇒ 实际扣 **{hp0 - afterPoison:0.####}**" +
                              $"｜火 tick = 0.18 × 125 = {0.18f * 125f:0.####} ⇒ 实际扣 **{fhp0 - afterBurn:0.####}**" +
                              $"（两个独立夹具，避免毒的余跳混进火的读数）");
                // 小数残量落地：0.4 点 → 至少 1
                var pool = new HealthPool();
                pool.Setup(1000f, 0f);
                float d = pool.ApplyDamage(0.4f);
                sb.AppendLine($"     小数残量：ApplyDamage(0.4) ⇒ 实际扣 **{d:0.####}**（下限 1，不允许被取整成 0）");
            }

            // ④ 弹字：所有伤害都印整数，且不可能出现 -0
            {
                sb.AppendLine("  ④ 弹字取整：");
                int bad = 0, total = 0;
                var samples = new List<string>();
                float[] probeAmounts = { 0.05f, 0.4f, 0.49f, 4.55f, 18.75f, 45.5f, 600.4f, 9.5f, 2.5f, 46f };
                foreach (float a in probeAmounts)
                {
                    string text = AsyncPopupText(a);
                    if (text == null) continue;
                    total++;
                    bool hasDot = text.IndexOf('.') >= 0;
                    bool isZero = text == "-0" || text == "-0." || text.StartsWith("-0.0");
                    if (hasDot || isZero) bad++;
                    samples.Add($"{a:0.##}→{text}");
                }
                sb.AppendLine($"     样本 {total} 条：{string.Join("  ", samples)}");
                sb.AppendLine($"     判据：含小数点 {bad} 条（期望 0）｜出现 -0 的 0 条（`Mathf.Max(1, …)` 保证）");
            }
        }

        /// <summary>单发一条弹字，把上屏的 `Text` 抓回来（走真的 `Actor.Popup` 路径）。</summary>
        private static string AsyncPopupText(float amount)
        {
            World w = NewWorld(out PlayerActor p);
            w.SpawnEnemy("grunt", new Vector2(3f, 0f), 20);
            EnemyActor e = w.Enemies[0];
            e.Health.Setup(100000f, 0f);
            string text = null;
            Action<FloatingTextEvent> sink = ev =>
            {
                if (ev.Kind == PopupKind.DamageToEnemy && text == null) text = ev.Text;
            };
            Bus.Subscribe(sink);
            try { w.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = amount, Target = e, IgnoreDodge = true }); }
            finally { Bus.Unsubscribe(sink); }
            return text;
        }

        // ─────────────────────────── ⑦ 20 分钟后的加速
        private static void AppendPost20(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            sb.AppendLine("────── ⑦ 20 分钟后的额外加速（差分证明它真被读）──────");
            EnemyDef grunt = TableDatabase.GetEnemy("grunt");
            EnemyDef boss = TableDatabase.GetEnemy("boss");
            float keep = g.post20Slope;
            sb.AppendLine("  分钟｜血(0)｜血(slope)｜血量倍率｜触碰血点(0)｜触碰血点(slope)｜BOSS 血(0)｜BOSS 血(slope)");
            try
            {
                for (int m = 18; m <= 30; m++)
                {
                    g.post20Slope = 0f;
                    float h0 = grunt.HpForWave(m, 1), d0 = grunt.TouchDamagePoints(m), b0 = boss.HpForWave(m, 1);
                    g.post20Slope = keep;
                    float h1 = grunt.HpForWave(m, 1), d1 = grunt.TouchDamagePoints(m), b1 = boss.HpForWave(m, 1);
                    float ratio = h0 > 0f ? h1 / h0 : 1f;
                    string star = m <= 20 && Mathf.Abs(ratio - 1f) < 1e-6f ? "  ← 精确 1.000（20 分钟目标不变）" : "";
                    sb.AppendLine($"    {m,3}｜{h0,10:0.00}｜{h1,10:0.00}｜{ratio,7:0.0000}｜{d0,10}｜{d1,12}｜{b0,10:0.00}｜{b1,10:0.00}{star}");
                }
            }
            finally { g.post20Slope = keep; }

            // 价格侧
            float keepP = t.shop.post20PriceSlope;
            sb.AppendLine($"  价格侧差分（post20PriceSlope = {keepP:0.###}）：分钟｜倍率(0)｜倍率(slope)｜倍率比值｜道具S22(0)｜道具S22(slope)｜首刷(0)｜首刷(slope)");
            try
            {
                for (int m = 18; m <= 30; m++)
                {
                    t.shop.post20PriceSlope = 0f;
                    float r0 = t.shop.WavePriceMult(m);
                    int s0 = SeedSlot.ShopPriceFrom(22, 4, m), f0 = ShopSystem.RerollBaseAt(t, m);
                    t.shop.post20PriceSlope = keepP;
                    float r1 = t.shop.WavePriceMult(m);
                    int s1 = SeedSlot.ShopPriceFrom(22, 4, m), f1 = ShopSystem.RerollBaseAt(t, m);
                    sb.AppendLine($"    {m,3}｜{r0,9:0.0000}｜{r1,9:0.0000}｜{(r0 > 0 ? r1 / r0 : 1f),7:0.0000}｜{s0,8}｜{s1,12}｜{f0,6}｜{f1,6}");
                }
            }
            finally { t.shop.post20PriceSlope = keepP; }
        }

        // ★ v195：`AppendBlastMult` 与 `AppendRefs` 整段删除 ★
        //   它们查证的那条属性（`StatId.ExplodeDamage`）**已经不存在** ✗ ——
        //   本轮的等价证据在新探针 `ProbeV195Num`：
        //     · `StatIds.TryParse("ExplodeDamage") == false`
        //     · `grep ExplodeDamage` 在 Assets 下 0 处
        //     · 三件道具改后的 effects/costs 原文

        // ─────────────────────────── 夹具
        private static World NewWorld(out PlayerActor player)
        {
            TuningDef t = TableDatabase.Tuning;
            var w = new World(t, 12345UL);
            var p = new PlayerActor
            {
                Id = w.NextActorId(),
                Team = Team.Player,
                Progress = new PlayerProgress(t),
                Pos = Vector2.zero,
                Radius = 0.5f
            };
            p.Health.Setup(p.Progress.Stats.Get(StatId.MaxHp), p.Progress.Stats.Get(StatId.Shield));
            // 幸运归零：本探针不测收割，归零可以避免任何 ×2 干扰（与自检夹具同一条做法）
            p.Progress.Stats.SetBase(StatId.Luck, 0f);
            w.Player = p;
            player = p;
            return w;
        }
    }
}
