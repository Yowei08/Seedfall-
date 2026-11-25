using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v192 只读探针 ★★ —— 给用户七项需求打**读数**，不写任何工程文件。
    ///
    /// 与 `ProbeV191` 同一套做法（那个文件就在隔壁）：
    ///   · 静态类 + `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`
    ///   · 开关是 `<工程>/Library/yowei_v192_probe.on` —— 存在才跑一次，删掉就彻底静默
    ///   · 全部读数进 `Log.Info`，前缀 `PROBEV192`，便于从 `/log` 里捞
    ///
    /// ★ 为什么不用 `-batchmode` 跑 ★
    /// 工程已被编辑器独占（`Temp/UnityLockfile`），再起一个实例会被 Unity 拒绝 ✗
    /// 所以挂在 Play 入口上、借编辑器自己的 domain 跑 ✓
    ///
    /// ★ 它是**只读**的 ★
    /// 每个小节都在**内存里新建一个 `RunSession`**（与自检的夹具同一种做法），
    /// 跑完就地丢掉；不写文件、不改表、不动存档 ✓
    /// </summary>
    public static class ProbeV192Num
    {
        private static string FlagPath
            => Path.Combine(Path.Combine(Application.dataPath, ".."), "Library", "yowei_v192_probe.on");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!File.Exists(FlagPath)) return;
            Log.Info("PROBEV192 BEGIN");
            try
            {
                Log.Info("\n" + Run());
            }
            catch (Exception e)
            {
                // 探针自己崩了也要看得见 —— 否则"没有输出"会被读成"没问题"
                Log.Error("PROBEV192 CRASH " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
            Log.Info("PROBEV192 END");
        }

        public static string Run()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.AppendLine("═══ v192 探针（只读）═══");
            AppendAutoCharge(sb);
            AppendEnemyDamage(sb);
            AppendBlastHarvest(sb);
            AppendReroll(sb);
            AppendSubBurst(sb);
            AppendDrawCards(sb);
            AppendMinuteHeal(sb);
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════
        // ① 水壶自充：每 0.5 秒一次
        // ═══════════════════════════════════════════════════════════════════
        private static void AppendAutoCharge(StringBuilder sb)
        {
            sb.AppendLine("── ① 水壶自充（can_autocharge）──");
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine($"  节拍 tools.canAutoChargeInterval = {t.EffectiveCanAutoChargeInterval:0.###} 秒" +
                          $"｜表值 {t.tools.canAutoChargeInterval:0.###}");

            RunSession s = NewSession(9UL, 1);
            PluginAsset ac = FindPlugin("can_autocharge");
            if (ac == null) { sb.AppendLine("  ★表里没有 can_autocharge ✘★"); return; }
            s.Goods.Plugins.Add(new PluginInstance(ac, WeaponSlot.Can));
            float rate = s.ModsFor(WeaponSlot.Can).AutoChargePerSecond;
            sb.AppendLine($"  插件 amount = {ac.amount:0.##}｜聚合 AutoChargePerSecond = {rate:0.##}/秒" +
                          $"｜层数 = {s.ModsFor(WeaponSlot.Can).AutoChargeLayers}");

            int water0 = s.WaterAmmo;
            sb.AppendLine($"  起手 WaterAmmo = {water0}（StartWave 的 +50 之后，上限 {RunSession.WaterAmmoMax}）");

            // 按固定步长跑 4.2 秒，记录每次水量跳变的时刻与增量
            var beatTimes = new List<float>();
            var beatGains = new List<int>();
            FrameInput input = default;
            float acc = 0f;
            int last = s.WaterAmmo;
            int ticks = 252;                     // 252 × 1/60 = 4.20 秒
            for (int i = 0; i < ticks; i++)
            {
                s.FixedTick(1f / 60f, in input);
                acc += 1f / 60f;
                if (s.WaterAmmo != last)
                {
                    beatTimes.Add(acc);
                    beatGains.Add(s.WaterAmmo - last);
                    last = s.WaterAmmo;
                }
            }
            int total = s.WaterAmmo - water0;
            sb.AppendLine($"  4.2 秒内跳变 {beatTimes.Count} 次｜合计 +{total} 点｜末值 {s.WaterAmmo}");
            var tc = new StringBuilder("  跳变时刻/增量：");
            for (int i = 0; i < beatTimes.Count; i++) tc.Append($"{beatTimes[i]:0.00}s:+{beatGains[i]} ");
            sb.AppendLine(tc.ToString());

            int within2 = 0;
            for (int i = 0; i < beatTimes.Count; i++) if (beatTimes[i] <= 2.0001f) within2++;
            sb.AppendLine($"  【判据】2 秒内恢复次数 = {within2}（期望 4）" +
                          $"｜2 秒内的总量 = {Sum(beatGains, beatTimes, 2.0001f)} 点（期望 20 = 10/秒 × 2 秒，**总量不变**）");
            sb.AppendLine($"  【判据】单次量 = {(beatGains.Count > 0 ? beatGains[0] : 0)} 点（期望 5 = 10 × 0.5）");
        }

        private static int Sum(List<int> gains, List<float> times, float until)
        {
            int n = 0;
            for (int i = 0; i < gains.Count; i++) if (times[i] <= until) n += gains[i];
            return n;
        }

        // ═══════════════════════════════════════════════════════════════════
        // ② 敌人伤害整数化（玩家血池 ×hpUnit）
        // ═══════════════════════════════════════════════════════════════════
        private static void AppendEnemyDamage(StringBuilder sb)
        {
            sb.AppendLine("── ② 敌人伤害整数化（hpUnit / 玩家血池 / 六个值）──");
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine($"  player.hpUnit = {t.player.hpUnit:0.##}" +
                          $"｜maxHp = {t.player.maxHp:0.##}｜shield = {t.player.shield:0.##}" +
                          $"｜heartHeal = {t.player.heartHeal:0.##}｜hpPerLevel = {t.growth.hpPerLevel:0.##}");
            sb.AppendLine($"  EnemyDef.PlayerHpUnit = {EnemyDef.PlayerHpUnit:0.##}");

            RunSession s = NewSession(11UL, 1);
            sb.AppendLine($"  实测玩家血池：MaxHp = {s.World.Player.Health.MaxHp:0.##}" +
                          $"／Shield = {s.World.Player.Health.MaxShield:0.##}" +
                          $"（HUD 读数应为 100/100 与 20/20）");

            sb.AppendLine("  敌人 / 招式            曲线值(小数)    整数血点   占玩家血池");
            foreach (string id in new[] { "grunt", "shooter", "jumper", "boss" })
            {
                EnemyDef d = TableDatabase.GetEnemy(id);
                if (d == null) continue;
                AppendDamageRow(sb, d, "触碰", 1, d.TouchDamageForWave(1), d.TouchDamagePoints(1));
                if (d.ProjectileDamage > 0f) AppendDamageRow(sb, d, "子弹", 1, d.ProjectileDamageForWave(1), d.ProjectileDamagePoints(1));
                if (d.JumpDamage > 0f) AppendDamageRow(sb, d, "砸地", 1, d.JumpDamageForWave(1), d.JumpDamagePoints(1));
            }
            sb.AppendLine("  【判据】六个值（第 1 分钟，含 ×0.65 前段压低）：" +
                          $"基础兵触碰 {TableDatabase.GetEnemy("grunt").TouchDamagePoints(1)}｜" +
                          $"远程触碰 {TableDatabase.GetEnemy("shooter").TouchDamagePoints(1)}｜" +
                          $"远程弹 {TableDatabase.GetEnemy("shooter").ProjectileDamagePoints(1)}｜" +
                          $"精英触碰 {TableDatabase.GetEnemy("jumper").TouchDamagePoints(1)}｜" +
                          $"精英砸地 {TableDatabase.GetEnemy("jumper").JumpDamagePoints(1)}｜" +
                          $"BOSS 触碰 {TableDatabase.GetEnemy("boss").TouchDamagePoints(1)}");

            // 第 20 分钟的读数（曲线终点：证明整数化没有破坏"曲线还在涨"）
            sb.AppendLine("  第 20 分钟（曲线终点）：" +
                          $"基础兵触碰 {TableDatabase.GetEnemy("grunt").TouchDamagePoints(20)}｜" +
                          $"远程弹 {TableDatabase.GetEnemy("shooter").ProjectileDamagePoints(20)}｜" +
                          $"精英砸地 {TableDatabase.GetEnemy("jumper").JumpDamagePoints(20)}｜" +
                          $"BOSS 触碰 {TableDatabase.GetEnemy("boss").TouchDamagePoints(20)}");

            // 真打一下：证明落到血池上的数确实是整数
            RunSession h = NewSession(12UL, 1);
            PlayerActor p = h.World.Player;
            p.InvulnRemaining = 0f;
            float sh0 = p.Health.Shield, hp0 = p.Health.Hp;
            h.World.DealDamage(new DamageRequest
            {
                Type = DamageType.Physical,
                RawValue = TableDatabase.GetEnemy("grunt").TouchDamageForWave(1),   // 0.455
                Target = p,
                IgnoreDodge = true
            });
            float got = (sh0 - p.Health.Shield) + (hp0 - p.Health.Hp);
            sb.AppendLine($"  实打一发：`RawValue = 0.455`（曲线值）⇒ 真的扣掉 **{got:0.###}** 点" +
                          $"｜盾 {sh0:0.#}→{p.Health.Shield:0.#}｜血 {hp0:0.#}→{p.Health.Hp:0.#}" +
                          $" ⇒ {(Mathf.Abs(got - Mathf.Round(got)) < 1e-4f ? "整数 ✓" : "★不是整数 ✘★")}");

            p.InvulnRemaining = 0f;
            sh0 = p.Health.Shield; hp0 = p.Health.Hp;
            h.World.DealDamage(new DamageRequest
            {
                Type = DamageType.Physical,
                RawValue = -1f, Target = p, IgnoreDodge = true      // 负数：不该回血
            });
            sb.AppendLine($"  负伤害守卫：−1 ⇒ 扣掉 {(sh0 - p.Health.Shield) + (hp0 - p.Health.Hp):0.###}（期望 0）");
        }

        private static void AppendDamageRow(StringBuilder sb, EnemyDef d, string move, int wave, float raw, int pts)
        {
            float pct = pts / Mathf.Max(1f, TableDatabase.Tuning.player.maxHp) * 100f;
            sb.AppendLine($"  {d.Id,-6}{move,-6}      {raw,10:0.0000}      {pts,7}      {pct,6:0.0}%");
        }

        // ═══════════════════════════════════════════════════════════════════
        // ③ 爆炸收割 = 普通收割的 1/3
        // ═══════════════════════════════════════════════════════════════════
        private static void AppendBlastHarvest(StringBuilder sb)
        {
            sb.AppendLine("── ③ 爆炸对耕地的收割（金币/经验 = 普通收割的 1/3）──");
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine($"  farm.blastHarvestRatio = {t.farm.blastHarvestRatio:0.####}（1/3 = {1f / 3f:0.####}）");

            string[] ids = { "life_spring", "radish_guard" };
            foreach (string id in ids)
            {
                SeedDef def = TableDatabase.GetSeed(id);
                if (def == null) { sb.AppendLine($"  ★表里没有 {id} ✘★"); continue; }
                HarvestResult a = HarvestOnce(id, false);
                HarvestResult b = HarvestOnce(id, true);
                sb.AppendLine($"  {id}：种子基数 goldYield={SeedEconomy.CellGold(def)} expYield={SeedEconomy.CellExp(def)}" +
                              $"｜普通收割命中 {a.Plots} 格、爆炸收割命中 {b.Plots} 格（必须各 1 格，否则读数不可比）");
                sb.AppendLine($"    普通收割（手挥锄头）  金币 +{a.Gold}  经验 +{a.Exp:0.##}   弹字「{a.PopupGold}」「{a.PopupExp}」");
                sb.AppendLine($"    爆炸收割（炸熟的那格）金币 +{b.Gold}  经验 +{b.Exp:0.##}   弹字「{b.PopupGold}」「{b.PopupExp}」");
                sb.AppendLine($"    【判据】金币比 = {(a.Gold == 0 ? 0f : (float)b.Gold / a.Gold):0.###}（期望 ≈ 0.333）" +
                              $"｜经验比 = {(a.Exp <= 0f ? 0f : b.Exp / a.Exp):0.###}（期望 ≈ 0.333）" +
                              $"｜误差 金币 {Mathf.Abs(b.Gold - a.Gold / 3f):0.##} 经验 {Mathf.Abs(b.Exp - a.Exp / 3f):0.##}（期望 < 1）");
            }
            sb.AppendLine("  ⚠️ 基数很小的种子会被 `Mathf.Max(1, …)` 保底顶住：`expYield 1 → Round(0.33) = 0 → 1`" +
                          " ⇒ 那种种子的爆炸收割仍是 1（比率 1.0，而不是 0.333）。" +
                          "这是既有保底口径（普通收割同样是 1），不是本规则失效 —— 读数见上面两行 goldYield/expYield ✓");
        }

        private struct HarvestResult { public int Gold; public float Exp; public int Plots; public string PopupGold, PopupExp; }

        /// <summary>
        /// 在**同一个种子**上各收一次：`viaBlast = false` 走手挥锄头那条（`World.HarvestPlot`），
        /// `true` 走肥料爆炸那条（`World.MatureAndHarvestInRadius`）。
        /// 幸运值归零（否则 5% 概率 ×2 会让这条读数变成抛硬币 —— 自检的夹具也是这么做的）。
        ///
        /// ★ 爆炸那一侧只给 **0.1 米** 半径 ★ 该函数的命中判据是
        /// `Distance(plot.World, center) > radius + 1.0` ⇒ 0.1 + 1.0 = 1.1 米；
        /// 地块间距 = `farm.tileSize` = 2.5 米 ⇒ **只有被测那一格**进圈 ✓
        /// （第一版打了 3 米，结果把邻格一起收了 ⇒ 金币 +4 而弹字 +2，读数不可比 ✗）
        /// </summary>
        private static HarvestResult HarvestOnce(string seedId, bool viaBlast)
        {
            var res = new HarvestResult();
            RunSession s = NewSession(4242UL, 1);
            s.Progress.Stats.SetBase(StatId.Luck, 0f);        // 归零：只测"收益系数"这一条乘区
            SeedDef def = TableDatabase.GetSeed(seedId);
            FarmPlot p = s.World.Farm.Slots[0];
            p.TickAction(PlotAction.Dig, 10f, 1f, s.T.farm, null);
            p.TickAction(PlotAction.Sow, 10f, 1f, s.T.farm, def);
            p.TickGrowth(999f);

            int g0 = s.World.GoldEarned;
            float e0 = s.World.ExpEarned;
            var goldPopups = new List<string>();
            var expPopups = new List<string>();
            var harvestedPlots = new List<Vector2>();
            Action<FloatingTextEvent> sink = e =>
            {
                if (e.Kind == PopupKind.GoldGain) { goldPopups.Add(e.Text); harvestedPlots.Add(e.Pos); }
                else if (e.Kind == PopupKind.ExpGain) expPopups.Add(e.Text);
            };
            Bus.Subscribe(sink);
            try
            {
                if (viaBlast) s.World.MatureAndHarvestInRadius(p.World, 0.1f);
                else s.World.HarvestPlot(p);
            }
            finally { Bus.Unsubscribe(sink); }

            res.Gold = s.World.GoldEarned - g0;
            res.Exp = s.World.ExpEarned - e0;
            res.Plots = goldPopups.Count;
            res.PopupGold = goldPopups.Count > 0 ? goldPopups[goldPopups.Count - 1] : "（无）";
            res.PopupExp = expPopups.Count > 0 ? expPopups[expPopups.Count - 1] : "（无）";
            return res;
        }

        // ═══════════════════════════════════════════════════════════════════
        // ④ 商店 / 抽卡刷新价：无限增长
        // ═══════════════════════════════════════════════════════════════════
        private static void AppendReroll(StringBuilder sb)
        {
            sb.AppendLine("── ④ 刷新价（商店 & 抽卡，同一个式子）──");
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine($"  shop.rerollBase = {t.shop.rerollBase}｜levelReward.rerollBasePrice = {t.levelReward.rerollBasePrice}" +
                          $"｜priceWaveStep = {t.shop.priceWaveStep}");
            sb.AppendLine($"  倍数 = 2^次数（**不封顶**）⇒ RerollMult(0..12) = " +
                          $"{ShopSystem.RerollMult(0)}, {ShopSystem.RerollMult(1)}, {ShopSystem.RerollMult(2)}, " +
                          $"{ShopSystem.RerollMult(3)}, {ShopSystem.RerollMult(4)}, … {ShopSystem.RerollMult(12)}");

            sb.Append("  【商店·波次 1】基础值 = ").Append(ShopSystem.RerollBaseAt(t, 1)).Append("｜1..12 次 = ");
            AppendPriceSeries(sb, t, t.shop.rerollBase, 1, 12);
            sb.Append("  【商店·波次 10】基础值 = ").Append(ShopSystem.RerollBaseAt(t, 10)).Append("｜1..12 次 = ");
            AppendPriceSeries(sb, t, t.shop.rerollBase, 10, 12);
            sb.Append("  【抽卡·波次 1】基础值 = ").Append(ShopSystem.RerollBaseAt(t, 1)).Append("｜1..12 次 = ");
            AppendPriceSeries(sb, t, t.levelReward.rerollBasePrice, 1, 12);
            sb.Append("  【抽卡·波次 10】基础值 = ").Append(ShopSystem.RerollBaseAt(t, 10)).Append("｜1..12 次 = ");
            AppendPriceSeries(sb, t, t.levelReward.rerollBasePrice, 10, 12);

            // 严格 2 倍递增 + 无双顶（判据）
            bool mono = true; bool doubled = true; string why = "";
            int prev = -1;
            for (int n = 0; n < 12; n++)
            {
                int price = ShopSystem.RerollPrice(t, t.shop.rerollBase, 1, n, 0);
                if (prev > 0 && price <= prev) { mono = false; why += $"[{n}次 {price} ≤ {prev}]"; }
                if (prev > 0 && Mathf.Abs(price - prev * 2f) > 0.5f) { doubled = false; why += $"[{n}次 {price} ≠ 2×{prev}]"; }
                prev = price;
            }
            sb.AppendLine($"  【判据】1..12 次：单调递增 = {mono}｜每次严格 ×2（无双顶）= {doubled} {why}");
            sb.AppendLine($"  【判据】v180~v191 的封顶常量 RerollMaxMult = {ShopSystem.RerollMaxMult}（已退役，不再参与定价）" +
                          $"｜第 4 次 = {ShopSystem.RerollPrice(t, t.shop.rerollBase, 1, 3, 0)}（改前会被压在 24）");

            // 溢出边界：证明"无限增长"到 int 边界时不会回绕成负数
            int big = ShopSystem.RerollPrice(t, t.shop.rerollBase, 1, 40, 0);
            sb.AppendLine($"  第 41 次（2^40）＝ {big}（int.MaxValue = {int.MaxValue}；撞顶返回上限而不是回绕成负数）");

            RunSession s = NewSession(13UL, 1);
            sb.AppendLine($"  实测 RunSession：商店 RerollCost(波次 {s.Shop.Wave}) = {s.Shop.RerollCost(s.T)}" +
                          $"｜抽卡 RewardRerollCost = {s.RewardRerollCost}");
            s.Shop.RerollCount = 3; s.RewardRerollCount = 3;
            sb.AppendLine($"  第 4 次：商店 = {s.Shop.RerollCost(s.T)}｜抽卡 = {s.RewardRerollCost}（改前两者都会被压到基础 ×8）");
        }

        private static void AppendPriceSeries(StringBuilder sb, TuningDef t, int basePrice, int wave, int count)
        {
            for (int n = 0; n < count; n++)
            {
                if (n > 0) sb.Append(" / ");
                sb.Append(ShopSystem.RerollPrice(t, basePrice, wave, n, 0));
            }
            sb.AppendLine();
        }

        // ═══════════════════════════════════════════════════════════════════
        // ⑤ 子爆：半径 = 主爆 1/2、伤害 = 基础爆炸伤害 50%
        // ═══════════════════════════════════════════════════════════════════
        private static void AppendSubBurst(StringBuilder sb)
        {
            sb.AppendLine("── ⑤ 子爆（fert_subburst）半径 / 伤害 ──");
            TuningDef t = TableDatabase.Tuning;
            PluginAsset sb2 = FindPlugin("fert_subburst");
            if (sb2 == null) { sb.AppendLine("  ★表里没有 fert_subburst ✘★"); return; }
            sb.AppendLine($"  表值 fertile: amount(个数/层) = {sb2.amount:0.##}｜amount2(半径比) = {sb2.amount2:0.###}" +
                          $"｜formula.subBurstDamageRatio = {WeaponSystem.SubBurstDamageRatio:0.###}");

            var goods = new PlayerGoods();
            goods.Plugins.Add(new PluginInstance(sb2, WeaponSlot.Fertilizer));
            WeaponModifiers m = WeaponSystem.Aggregate(goods, WeaponSlot.Fertilizer);
            sb.AppendLine($"  聚合：SubBurstCount = {m.SubBurstCount}｜SubBurstRadiusRatio = {m.SubBurstRadiusRatio:0.###}" +
                          $"｜SubBurstRingRatio(落点/主爆半径) = {WeaponSystem.SubBurstRingRatio:0.##}");

            // 与 `RunSession.ExplodeAt` **同一个式子**算主爆（不另写一份）
            RunSession s = NewSession(14UL, 1);
            StatBlock st = s.Progress.Stats;
            ToolDef fert = t.tools.fertilizer;
            float mainRadius = RunSession.BlastBaseRadius(fert.baseExplodeRange) * st.Get(StatId.ExplodeRange);
            float mainDmg = DamageResolver.WeaponBlastDamage(fert.baseValue, st.Get(StatId.BlastDamage),
                                                             st.Get(StatId.AtkMult), false, st.Get(StatId.CritDmg),
                                                             0f);
            float subRadius = WeaponSystem.SubBurstRadius(mainRadius, m);
            float subDmg = WeaponSystem.SubBurstDamage(mainDmg, m);
            float ratioR = mainRadius > 0f ? subRadius / mainRadius : 0f;
            float ratioD = mainDmg > 0f ? subDmg / mainDmg : 0f;
            sb.AppendLine($"  基础爆炸伤害的入参：tools.fertilizer.baseValue = {fert.baseValue:0.##}" +
                          $"｜Stats.BlastDamage = {st.Get(StatId.BlastDamage):0.##}" +
                          "｜Stats.ExplodeDamage(爆炸倍率) = 已删除（v195）");
            sb.AppendLine($"  主爆：半径 {mainRadius:0.###} 米｜伤害 {mainDmg:0.###}");
            sb.AppendLine($"  子爆：半径 {subRadius:0.###} 米｜伤害 {subDmg:0.###}");
            sb.AppendLine($"  【判据】子爆半径 / 主爆半径 = {ratioR:0.###}（期望 0.500）");
            sb.AppendLine($"  【判据】子爆伤害 / 主爆伤害 = {ratioD:0.###}（期望 0.500）");
            sb.AppendLine($"  【判据】子爆伤害 / 肥料基础值 {fert.baseValue:0.##} = {subDmg / Mathf.Max(0.01f, fert.baseValue):0.###}" +
                          $"（★ 若按『基础值 ×50%』的字面读法应为 0.5 ⇒ 即 2.5 常数；见 WeaponSystem.SubBurstDamageRatio 的说明）");
            sb.AppendLine($"  子毒区半径 = {WeaponSystem.SubPoisonRadius(mainRadius, m):0.###} 米" +
                          $"（= 主毒区 {WeaponSystem.PoisonRadius(mainRadius):0.###} × 半径比 {m.SubBurstRadiusRatio:0.##}）" +
                          $"｜落点距主爆心 = {WeaponSystem.SubBurstRingRatio * mainRadius:0.###} 米");
        }

        // ═══════════════════════════════════════════════════════════════════
        // ⑥ 抽卡：爆炸伤害 Add 2 → 1
        // ═══════════════════════════════════════════════════════════════════
        private static void AppendDrawCards(StringBuilder sb)
        {
            sb.AppendLine("── ⑥ 抽卡属性池（爆炸伤害修改值）──");
            TuningDef t = TableDatabase.Tuning;
            if (t.levelReward?.stats == null) { sb.AppendLine("  ★没有 stats ✘★"); return; }
            sb.AppendLine($"  池子共 {t.levelReward.stats.Length} 张：");
            for (int i = 0; i < t.levelReward.stats.Length; i++)
            {
                RewardStatDef r = t.levelReward.stats[i];
                sb.AppendLine($"    {r.id,-14}{r.name,-8}{r.mode,-8}{r.amount,4}" +
                              (r.id == "BlastDamage" ? "   ← ★ 本次改动（2 → 1）" : ""));
            }
            for (int i = 0; i < t.levelReward.stats.Length; i++)
            {
                RewardStatDef r = t.levelReward.stats[i];
                if (r.id == "BlastDamage")
                    sb.AppendLine($"  【判据】BlastDamage = {r.amount}（期望 1）｜mode = {r.mode}（期望 Add）");
                if (r.id == "ExplodeRange")
                    sb.AppendLine($"  【判据】ExplodeRange 未被本次改动波及 = {r.mode} {r.amount}（期望 Percent 5）");
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // ⑦ 每分钟整点回血（正式机制 + 事件）
        // ═══════════════════════════════════════════════════════════════════
        private static void AppendMinuteHeal(StringBuilder sb)
        {
            sb.AppendLine("── ⑦ 每分钟整点回血（机制侧读数）──");
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine($"  player.minuteHealPercent = {t.player.minuteHealPercent:0.###}" +
                          $"｜EffectiveMinuteHealPercent = {t.EffectiveMinuteHealPercent:0.###}" +
                          $"｜maxHp = {t.player.maxHp:0.##} ⇒ 每次回 {t.EffectiveMinuteHealPercent * t.player.maxHp:0.##} 生命" +
                          $"（默认 1.0 = 回满，与改前 `Heal(MaxHp)` 逐位等价）");

            // 订阅事件，看它到底发了没有、带了什么数
            var seen = new List<MinuteHealEvent>();
            Action<MinuteHealEvent> sink = e => seen.Add(e);
            Bus.Subscribe(sink);
            try
            {
                RunSession s = new RunSession();
                s.Start(777UL);
                s.StartWave(1);                          // ← 这一次也会发事件（t=0 的第一分钟），先记下条数
                int afterFirstWave = seen.Count;
                PlayerActor p = s.World.Player;
                // 先掉一部分血与盾（否则"回血"看不出量）
                // ★ 注意量纲：`DamageRequest.RawValue` 对**玩家**是"敌人曲线值"（旧域），
                //   会被 `EnemyDef.PlayerDamagePoints` 乘 hpUnit 取整 ⇒ 传 3 就是 **30 血点** ✓
                //   （第一版传了 30 ⇒ 300 血点 ⇒ 玩家当场死，读数变成 0/100 ✗）
                p.InvulnRemaining = 0f;
                s.World.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 3f, Target = p, IgnoreDodge = true });
                p.Health.Shield = 0f;
                float hp0 = p.Health.Hp, sh0 = p.Health.Shield;
                int before = seen.Count;
                s.StartWave(2);                       // ← 整点：`Clock.WaveIndex > WaveIndex` 每分钟为真一次
                sb.AppendLine($"  跨分前：血 {hp0:0.##}/{p.Health.MaxHp:0.##}｜盾 {sh0:0.##}");
                sb.AppendLine($"  跨分后：血 {p.Health.Hp:0.##}/{p.Health.MaxHp:0.##}｜盾 {p.Health.Shield:0.##}/{p.Health.MaxShield:0.##}" +
                              $"｜实回 血 +{p.Health.Hp - hp0:0.##}、盾 +{p.Health.Shield - sh0:0.##}");
                sb.AppendLine($"  （开局第 1 波也发了一次：{afterFirstWave} 次；跨分这一次新增 {seen.Count - before} 次）");
            }
            finally { Bus.Unsubscribe(sink); }

            sb.AppendLine($"  MinuteHealEvent 共收到 {seen.Count} 次：第 1 波开局 1 次 + 本次跨分 1 次（期望 2，每次整点必然一次）");
            for (int i = 0; i < seen.Count; i++)
            {
                MinuteHealEvent e = seen[i];
                sb.AppendLine($"    第 {e.Wave} 分钟：HealHp +{e.HealHp:0.##}｜HealShield +{e.HealShield:0.##}" +
                              $"｜Hp {e.Hp:0.##}/{e.MaxHp:0.##}｜Pos ({e.Pos.x:0.##},{e.Pos.y:0.##})");
            }
            sb.AppendLine("  【第三路该订阅什么】`Bus.Subscribe<MinuteHealEvent>(OnMinuteHeal)` —— " +
                          "`MinuteHealEvent { int Wave; float HealHp, HealShield, Hp, MaxHp; Vector2 Pos; }`" +
                          "（定义在 Assets/Scripts/Combat/Combat.cs）；发布点：`RunSession.ResetPlayerForNewWave`" +
                          "（每分钟一次）。逻辑层不读它 ⇒ 不订阅也能正常玩 ✓");
        }

        // ═══════════════════════════════════════════════════════════════════
        private static RunSession NewSession(ulong seed, int wave)
        {
            var s = new RunSession();
            s.Start(seed);
            s.StartWave(wave);
            return s;
        }

        private static PluginAsset FindPlugin(string id)
        {
            PluginAsset[] all = TableDatabase.Plugins;
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }
    }
}
