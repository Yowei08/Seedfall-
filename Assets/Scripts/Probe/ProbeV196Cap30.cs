using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v196-c：**30 分钟价格锁定**的只读探针（唯一入口 `Run` / `BatchRun`）★★
    ///
    /// 用户原话："增加商品价格和刷新价格限制，30 分钟后，商品价格和刷新价格锁定，敌人数值继续成长"
    ///
    /// ── 这一轮只锁**波次因子** ──
    ///   · 商品价：`ShopDef.WavePriceMult(w)` 内部 `w = CappedPriceWave(w)`（`m > 30` 当 30 用）✓
    ///   · 刷新价：`ShopDef.RerollWaveMult(w)` 过同一个夹紧点 ✓
    ///   · 次数惩罚 `1.5^n`（`ShopSystem.RerollMult`）**不动** ⇒ 同一分钟内刷得更贵 ✓
    ///   · 敌人 `EnemyGrowthDef.post20Slope` **一个字节不动** ⇒ 30 分钟后继续涨 ✓
    ///
    /// 读数分四组（对应交付的 ①②③④）：
    ///   ② 商品价（D/C/B/A/S，走**真实的** `ShopSystem.PriceOf` + 真 `SeedDef`）× 刷新价（首刷 + 2/3/4 次，
    ///      走 HUD 也在用的 `ShopSystem.RerollCost(t)`）
    ///   ③ `1.5^n` 仍生效：同一分钟内四次刷新价**严格递增**、相邻比值 ≈1.5
    ///   ④ 敌人 `m = 30 / 35 / 40`：血与三招（触碰 / 弹幕 / 跳砸）逐分钟仍在长
    ///   以及⑧一批硬检查：第 30/31/35/40 分钟**逐位相同**（`float` 位模式比对，不是"看着差不多"）
    ///
    /// ★ 它是**只读**的 ★ 只做 `TableDatabase.Load(true)`（幂等）、读表、算数；
    ///   不写 `Assets/**`、不改场景、不改存档、不进 Play 也能跑（`-executeMethod` 批处理）✓
    ///   结果写 `Library/yowei-v196-cap30.txt`（`Library/` 已被 `.gitignore` 忽略）✓
    ///
    /// 跑法：
    ///   Unity.exe -batchmode -quit -projectPath &lt;工程&gt; -executeMethod Seedfall.ProbeV196Cap30.BatchRun
    /// </summary>
    public static class ProbeV196Cap30
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        public static string OutPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-v196-cap30.txt");

        /// <summary>本轮的六列分钟点：最后四列（30/31/35/40）必须逐位相同。</summary>
        private static readonly int[] Minutes = { 28, 29, 30, 31, 35, 40 };
        private static readonly string[] TierNames = { "D", "C", "B", "A", "S" };

        // ══════════════════════════════════════════════════════════════
        // 批处理入口（自检 + 探针，退出码：自检全绿 = 0，有红 = 1）
        // ══════════════════════════════════════════════════════════════
        public static void BatchRun()
        {
            int exit = 0;
            try
            {
                TableDatabase.Load(true);
                var all = new StringBuilder();
                all.AppendLine("═══ v196-c 批处理（只读：30 分钟价格锁定）═══");

                List<CheckResult> results = SelfCheck.RunAll();
                int pass = 0;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                all.AppendLine($"自检 {(pass == results.Count ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过");
                var fails = new StringBuilder();
                for (int i = 0; i < results.Count; i++)
                    if (!results[i].Passed) fails.AppendLine("  ✗ " + results[i]);
                if (fails.Length > 0) all.Append(fails);
                if (pass != results.Count) exit = 1;

                all.AppendLine(Run());

                string dir = Path.Combine(ProjRoot, "Library");
                Directory.CreateDirectory(dir);
                File.WriteAllText(OutPath, all.ToString(), new UTF8Encoding(false));
                Debug.Log(all.ToString());
                Debug.Log("V196CAP30 结果已写入 " + OutPath);
            }
            catch (Exception e)
            {
                Debug.LogError("V196CAP30 CRASH " + e.GetType().Name + ": " + e.Message + "\n" + (e.StackTrace ?? ""));
                exit = 2;
            }
            EditorExit(exit);
        }

        /// <summary>批处理里把退出码交出去；Play 模式（若有）不会有这一步。</summary>
        private static void EditorExit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            if (code != 0) Application.Quit(code);
#endif
        }

        // ══════════════════════════════════════════════════════════════
        // 探针主体
        // ══════════════════════════════════════════════════════════════
        public static string Run()
        {
            if (TableDatabase.Tuning == null) TableDatabase.Load(true);
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("════════════════ v196-c 探针：30 分钟价格锁定 ════════════════");

            AppendParams(sb);
            AppendPriceTable(sb);
            AppendRerollTable(sb);
            AppendCounterEvidence(sb);
            AppendEnemyTable(sb);
            AppendChecks(sb);

            return sb.ToString();
        }

        // ───────────────────────── ⓪ 参数
        private static void AppendParams(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            sb.AppendLine("────── ⓪ 参数（改前 / 改后都是这几个数，只多了一个夹紧点）──────");
            sb.AppendLine($"  商品：priceWaveStep = {t.shop.priceWaveStep:0.#####}｜post20PriceSlope = {t.shop.post20PriceSlope:0.#####}");
            sb.AppendLine($"  刷新：rerollWaveStep = {t.shop.rerollWaveStep:0.#####}｜rerollBase = {t.shop.rerollBase}｜RerollMult = 1.5^次数（不封顶）");
            sb.AppendLine($"  ★ 本轮新增：ShopDef.PriceWaveCap = {ShopDef.PriceWaveCap}（`m > {ShopDef.PriceWaveCap}` ⇒ 按 {ShopDef.PriceWaveCap} 算）");
            sb.AppendLine($"  ★ 敌人（**没动**）：post20Slope = {g.post20Slope:0.###} ⇒ 第 30 分 post20 = {EnemyDef.Post20Factor(30):0.###}×、" +
                          $"第 35 分 {EnemyDef.Post20Factor(35):0.###}×、第 40 分 {EnemyDef.Post20Factor(40):0.###}×");
        }

        // ───────────────────────── ① 商品价
        private static void AppendPriceTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ① 商品价（真实定价路径 `ShopSystem.PriceOf(offer, 分钟)`，真 `SeedDef`）──────");
            sb.AppendLine("  实付价 = 基础价(档) × 档位倍率 × W(分钟)；W 由 `ShopDef.WavePriceMult` 给出（内部夹紧到 30）");
            sb.Append("  档位基础价（表内实测）：");
            for (int q = 0; q < 5; q++) sb.Append($"{TierNames[q]} {BaseOf(q),4}｜");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("  分钟｜     W     ｜   D   ｜   C   ｜   B   ｜   A   ｜   S   ｜ W 位模式");
            for (int i = 0; i < Minutes.Length; i++)
            {
                int m = Minutes[i];
                float w = t.shop.WavePriceMult(m);
                sb.Append("  ").Append(m.ToString().PadLeft(4)).Append("｜")
                  .Append(w.ToString("0.00000").PadLeft(9)).Append("｜");
                for (int q = 0; q < 5; q++) sb.Append(PriceAt(q, m).ToString().PadLeft(6)).Append("｜");
                sb.Append(" 0x").Append(BitsOf(w).ToString("X8")).AppendLine();
            }
            sb.AppendLine($"  说明：W(30) = {t.shop.WavePriceMult(30):0.00000}（v195 解出来的 5.000 保持不变）；" +
                          $"第 31 分起 `W` 与第 30 分**同一位模式** ⇒ 价格逐位不变");
        }

        /// <summary>该档位的**表内真实基础价**：取表里第一颗该档种子（与 v195 的 D20/C30/B40/A60/S80 一致）。</summary>
        private static int BaseOf(int tier)
        {
            SeedDef d = FirstSeedOfTier(tier);
            return d != null ? d.Price : 20 + tier * 10;
        }

        private static SeedDef FirstSeedOfTier(int tier)
        {
            SeedDef[] arr = TableDatabase.Seeds;
            if (arr == null) return null;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].RarityTier == tier) return arr[i];
            return null;
        }

        /// <summary>走**真实**定价函数：`ShopSystem.PriceOf`（与货架掷价、锁定项重定价同一个入口）。</summary>
        private static int PriceAt(int tier, int minute)
        {
            SeedDef d = FirstSeedOfTier(tier);
            if (d == null) return SeedSlot.ShopPriceFrom(20 + tier * 10, tier, minute);
            var o = new ShopOffer { Kind = OfferKind.Seed, Seed = d, Quality = tier, Level = SeedSlot.StartingLevel };
            return ShopSystem.PriceOf(o, minute);
        }

        // ───────────────────────── ② 刷新价
        private static void AppendRerollTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ② 刷新价（HUD 也在读的 `ShopSystem.RerollCost(t)`；商店 wave = 分钟）──────");
            sb.AppendLine("  刷新价 = round( round(rerollBase × W_r(分钟)) × 1.5^次数 )；W_r 由 `ShopDef.RerollWaveMult`（内部同样夹紧到 30）");
            sb.AppendLine();
            sb.AppendLine("  分钟｜ W_r    ｜底价(首刷)｜ 第2次 ｜ 第3次 ｜ 第4次 ｜ 抽卡首刷/第2次");
            for (int i = 0; i < Minutes.Length; i++)
            {
                int m = Minutes[i];
                var shop = new ShopSystem { Wave = m };
                int basis = ShopSystem.RerollBaseAt(t, m);
                sb.Append("  ").Append(m.ToString().PadLeft(4)).Append("｜")
                  .Append(t.shop.RerollWaveMult(m).ToString("0.0000").PadLeft(8)).Append("｜")
                  .Append(basis.ToString().PadLeft(10)).Append("｜");
                for (int n = 1; n <= 3; n++)
                {
                    shop.RerollCount = n;
                    sb.Append(shop.RerollCost(t).ToString().PadLeft(7)).Append("｜");
                }
                int draw0 = ShopSystem.RerollPrice(t, t.levelReward.rerollBasePrice, m, 0, 0);
                int draw1 = ShopSystem.RerollPrice(t, t.levelReward.rerollBasePrice, m, 1, 0);
                sb.Append(" ").Append(draw0).Append('/').Append(draw1).AppendLine();
            }
            sb.AppendLine($"  注：第 30 分钟底价 = {ShopSystem.RerollBaseAt(t, 30)}（v195 的目标\"30 分钟初刷 30 金\"保持不变；" +
                          $"底价是 `RoundToInt(3 × W_r(30))` = RoundToInt(3 × {t.shop.RerollWaveMult(30):0.0000}) = {ShopSystem.RerollBaseAt(t, 30)}）");
        }

        // ───────────────────────── ③ 1.5^n 仍生效
        private static void AppendCounterEvidence(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ③ 次数惩罚 `1.5^n` 仍生效（**锁的是波次因子，不是次数**）──────");
            sb.AppendLine("  倍数表：RerollMult(0..4) = " +
                          $"{ShopSystem.RerollMult(0):0.###} / {ShopSystem.RerollMult(1):0.###} / " +
                          $"{ShopSystem.RerollMult(2):0.###} / {ShopSystem.RerollMult(3):0.###} / {ShopSystem.RerollMult(4):0.####}");
            int[] waves = { 30, 31, 35, 40 };
            for (int i = 0; i < waves.Length; i++)
            {
                int m = waves[i];
                var shop = new ShopSystem { Wave = m };
                sb.Append($"  第 {m,2} 分钟：");
                int prev = 0;
                for (int n = 0; n < 4; n++)
                {
                    shop.RerollCount = n;
                    int p = shop.RerollCost(t);
                    sb.Append(p.ToString().PadLeft(5));
                    if (n > 0) sb.Append($"（×{p / (float)prev:0.00}）");
                    prev = p;
                }
                sb.AppendLine();
            }
            sb.AppendLine("  ⇒ 同一分钟内第 1/2/3/4 次价**不同**且严格递增 ⇒ 次数惩罚没有被锁死 ✓");
            sb.AppendLine("  （若把 `1.5^n` 一起锁死，四次读数会全是 30 —— 那会毁掉\"刷新越刷越贵\" ✗）");
        }

        // ───────────────────────── ④ 敌人
        private static void AppendEnemyTable(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("────── ④ 敌人数值 m = 30 / 35 / 40（`post20Slope` 一个字节没动 ⇒ 继续长）──────");
            sb.AppendLine("  血量 = baseHp × WaveGrowth × 前段压低(非 BOSS) × post20；三招 = base × 伤害曲线 × post20");
            string[] ids = { "grunt", "shooter", "jumper", "boss" };
            int[] mins = { 30, 35, 40 };
            for (int i = 0; i < ids.Length; i++)
            {
                EnemyDef d = TableDatabase.GetEnemy(ids[i]);
                if (d == null) { sb.AppendLine($"  ◆ {ids[i]}：表里没有"); continue; }
                sb.AppendLine();
                sb.AppendLine($"  ◆ {d.Id}（{d.Name}／{d.Kind}）baseHp {d.BaseHp:0.##}｜触碰 {d.TouchDamage:0.####}" +
                              $"｜弹幕 {d.ProjectileDamage:0.####}｜跳砸 {d.JumpDamage:0.####}");
                sb.AppendLine("    分钟｜ post20 ｜ 血量倍率 ｜     血量     ｜ 触碰 ｜ 弹幕 ｜ 跳砸 ｜ 整数血点(触/弹/跳)");
                for (int k = 0; k < mins.Length; k++)
                {
                    int m = mins[k];
                    sb.Append("    ").Append(m.ToString().PadLeft(4)).Append("｜")
                      .Append(EnemyDef.Post20Factor(m).ToString("0.0000").PadLeft(8)).Append("｜")
                      .Append(d.HpMultiplierForWave(m).ToString("0.0000").PadLeft(10)).Append("｜")
                      .Append(d.HpForWave(m, 1).ToString("0.00").PadLeft(12)).Append("｜")
                      .Append(d.TouchDamageForWave(m).ToString("0.0000").PadLeft(6)).Append("｜")
                      .Append(d.ProjectileDamageForWave(m).ToString("0.0000").PadLeft(6)).Append("｜")
                      .Append(d.JumpDamageForWave(m).ToString("0.0000").PadLeft(6)).Append("｜")
                      .Append($" {d.TouchDamagePoints(m)}/{d.ProjectileDamagePoints(m)}/{d.JumpDamagePoints(m)}")
                      .AppendLine();
                }
            }
        }

        // ───────────────────────── ⑧ 硬检查
        private static void AppendChecks(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ⑧ 硬检查 ──────");
            int ok = 0, n = 0;

            // ① 商品价：30/31/35/40 逐位相同（W 的 float 位模式 + 五档整数价）
            bool wSame = true, priceSame = true;
            float w30 = t.shop.WavePriceMult(30);
            for (int m = 31; m <= 40; m++)
            {
                if (!SameBits(w30, t.shop.WavePriceMult(m))) wSame = false;
                for (int q = 0; q < 5; q++) if (PriceAt(q, m) != PriceAt(q, 30)) priceSame = false;
            }
            n++; if (wSame) ok++;
            sb.AppendLine($"  {Mark(wSame)} ① 商品：第 30/31/…/40 分钟的 `W` **位模式完全相同**（0x{BitsOf(w30):X8}）");
            n++; if (priceSame) ok++;
            sb.AppendLine($"  {Mark(priceSame)} ② 商品：第 30/31/…/40 分钟的 D/C/B/A/S 实付价完全相同" +
                          $"（{PriceAt(0, 30)}/{PriceAt(1, 30)}/{PriceAt(2, 30)}/{PriceAt(3, 30)}/{PriceAt(4, 30)}）");

            // ③ 刷新价：30/31/35/40 逐位相同（底价 + 4 次）
            bool rSame = true;
            var s30 = new ShopSystem { Wave = 30 };
            int[] refPrices = new int[5];
            for (int k = 0; k < 4; k++) { s30.RerollCount = k; refPrices[k] = s30.RerollCost(t); }
            refPrices[4] = ShopSystem.RerollBaseAt(t, 30);
            for (int m = 31; m <= 40; m++)
            {
                var sm = new ShopSystem { Wave = m };
                for (int k = 0; k < 4; k++) { sm.RerollCount = k; if (sm.RerollCost(t) != refPrices[k]) rSame = false; }
                if (ShopSystem.RerollBaseAt(t, m) != refPrices[4]) rSame = false;
                if (!SameBits(t.shop.RerollWaveMult(m), t.shop.RerollWaveMult(30))) rSame = false;
            }
            n++; if (rSame) ok++;
            sb.AppendLine($"  {Mark(rSame)} ③ 刷新：第 30/31/…/40 分钟的底价与首刷/2/3/4 次价完全相同" +
                          $"（底价 {refPrices[4]}｜{refPrices[0]}/{refPrices[1]}/{refPrices[2]}/{refPrices[3]}）");

            // ④ 锁前（m ≤ 30）逐位不变：W(1) 必须精确 1.0，且 1..30 与旧式子逐位同值
            float w1 = t.shop.WavePriceMult(1);
            bool preIntact = w1 == 1f;
            for (int m = 1; m <= 30; m++)
            {
                // 旧式子（v196 之前）：Exponent = Max(0, m−1)，post20 入参 m
                float oldW = Mathf.Pow(1f + Mathf.Max(0f, t.shop.priceWaveStep), Mathf.Max(0, m - 1)) * t.shop.Post20Factor(m);
                if (!SameBits(oldW, t.shop.WavePriceMult(m))) preIntact = false;
                float oldR = Mathf.Pow(1f + Mathf.Max(0f, t.shop.rerollWaveStep), Mathf.Max(0, m - 1)) * t.shop.Post20Factor(m);
                if (!SameBits(oldR, t.shop.RerollWaveMult(m))) preIntact = false;
            }
            n++; if (preIntact) ok++;
            sb.AppendLine($"  {Mark(preIntact)} ④ 锁之前（m = 1..30）与 v195 旧式子**逐位相同**（W(1) = {w1:0.00000}，锁定没有波及前 30 分钟）");

            // ⑤ 次数惩罚仍生效：同一分钟内 4 次价严格递增
            bool rising = true;
            var sx = new ShopSystem { Wave = 40 };
            int prev = 0;
            for (int k = 0; k < 4; k++)
            {
                sx.RerollCount = k;
                int p = sx.RerollCost(t);
                if (k > 0 && p <= prev) rising = false;
                prev = p;
            }
            n++; if (rising) ok++;
            sb.AppendLine($"  {Mark(rising)} ⑤ 第 40 分钟同一分钟内刷 4 次：价**严格递增**（1.5^n 没被锁死）");

            // ⑥ 敌人仍在长：40 分 > 35 分 > 30 分（血 + **这个敌真有的**招）
            //   ⚠️ 只比 base > 0 的招：基础兵没有弹幕（base 0）、远程兵没有跳砸（base 0）⇒
            //   拿"0 > 0"去判会把一只本来正常成长的敌人误判成没长（这是探针自己的错，不是被测代码的错）
            bool growing = true;
            var ids = new[] { "grunt", "shooter", "jumper", "boss" };
            var arms = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                EnemyDef d = TableDatabase.GetEnemy(ids[i]);
                if (d == null) continue;
                if (!(d.HpForWave(40, 1) > d.HpForWave(35, 1) && d.HpForWave(35, 1) > d.HpForWave(30, 1))) growing = false;
                int checkedArms = 0;
                if (d.TouchDamage > 0f)
                {
                    checkedArms++;
                    if (!(d.TouchDamageForWave(40) > d.TouchDamageForWave(35)
                       && d.TouchDamageForWave(35) > d.TouchDamageForWave(30))) growing = false;
                }
                if (d.ProjectileDamage > 0f)
                {
                    checkedArms++;
                    if (!(d.ProjectileDamageForWave(40) > d.ProjectileDamageForWave(35)
                       && d.ProjectileDamageForWave(35) > d.ProjectileDamageForWave(30))) growing = false;
                }
                if (d.JumpDamage > 0f)
                {
                    checkedArms++;
                    if (!(d.JumpDamageForWave(40) > d.JumpDamageForWave(35)
                       && d.JumpDamageForWave(35) > d.JumpDamageForWave(30))) growing = false;
                }
                arms.Append($"{d.Id}:血×{d.HpForWave(40, 1) / d.HpForWave(30, 1):0.00}/招 {checkedArms} 项 ");
            }
            n++; if (growing) ok++;
            sb.AppendLine($"  {Mark(growing)} ⑥ 敌人第 30/35/40 分钟**仍在长**（血严格递增 + 各敌**有 base 的招**全部严格递增）：{arms}");

            // ⑦ v195 的两个硬目标没被本轮破坏
            int d30 = PriceAt(0, 30), s30p = PriceAt(4, 30);
            bool targets = Mathf.Abs(t.shop.WavePriceMult(30) - 5f) < 0.001f && s30p <= 2000 && d30 >= 100 && d30 < 1000;
            n++; if (targets) ok++;
            sb.AppendLine($"  {Mark(targets)} ⑦ v195 的目标仍在：W(30) = {t.shop.WavePriceMult(30):0.00000}（≈5.000）、" +
                          $"S@30 = {s30p}（≤2000）、D@30 = {d30}（百位）");

            // ⑧ 刷新价 ≤ 100 的目标仍在（第 3 次 68）
            var s3 = new ShopSystem { Wave = 30, RerollCount = 2 };
            int third = s3.RerollCost(t);
            n++; if (third <= 100) ok++;
            sb.AppendLine($"  {Mark(third <= 100)} ⑧ 刷新第 3 次 @30 分钟 = {third}（≤100 的 v195 目标仍在）");

            sb.AppendLine($"  ── 硬检查合计：{ok}/{n} 通过 ──");
        }

        // ══════════════════════════════════════════════════════════════
        // 工具
        // ══════════════════════════════════════════════════════════════
        private static string Mark(bool pass) => pass ? "✓" : "✗";

        /// <summary>float 的**位模式**（`BitConverter.GetBytes` —— 不依赖 `SingleToInt32Bits` 的可用性）。</summary>
        private static int BitsOf(float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            return b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
        }

        private static bool SameBits(float a, float b) => BitsOf(a) == BitsOf(b);
    }
}
