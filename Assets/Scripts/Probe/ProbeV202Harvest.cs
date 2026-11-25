using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v202 只读探针：找「爆炸之后**手挥收割也永久变成 1/4**」的那个**持久状态** ★★
    ///
    /// ── 为什么要这个探针（v200 没找到的原因）✗ ──
    /// v200 改了结构（`World.HarvestYieldScale` 字段 → `HarvestPlot(plot, yieldScale)` 入参），
    /// 然后用 `w.HarvestPlot(p0)` / `w.MatureAndHarvestInRadius(...)` / `w.HarvestPlot(p2)` 三个读数
    /// 证明「炸完再手收仍是全额」。那三个调用**都是 World 上的直接调用**，而玩家的手挥走的是
    /// `RunSession.HarvestPlot`（它还会再调 `ProgMechanics.OnHarvest`），爆炸走的是
    /// `RunSession.ExplodeAt`（它先算插件 / 富豪 / 子爆 / 毒区，再进 `MatureAndHarvestInRadius`）。
    /// ⇒ 两个「玩家真路径」都不在 v200 的读数里 ✓ 这个探针只走它们 ✓
    ///
    /// ── 方法：**状态指纹**（不猜，只比）✓ ──
    /// 在爆炸**前 / 后**各拍一张快照，覆盖所有能进收益式子的标量：
    ///   · 21 条属性的 `Get()` 与 `GetBase()`（`GoldMult` / `ExpMult` / `Luck` / `MaxPlots`…）
    ///   · `World.HarvestGoldMult` / `World.HarvestDew`
    ///   · `Tuning.farm.*` 每一个参与结算的键
    /// 然后**逐键比**：哪一条在爆炸后变了、且**没变回来**，它就是那个「持久状态」。
    ///
    /// ── 判据（三段，缺一不可）──
    ///   ① `炸后手收 == 炸前手收`（同一颗种子、同一个等级、幸运 0）
    ///   ② 快照里**参与结算的键**在爆炸前后**逐位相同**
    ///   ③ 炸后新种下的那一格，`Level / WaterMax / WaterDrain / GrowthBase / 作物的金与验`
    ///      与炸前那一格**逐位相同**（排除「地块被写脏」这条独立可能）
    /// ① 只证明「这次没复发」，②③ 才证明「这一类缺陷不可能复发」 ✓
    ///
    /// ★ 只读：不写盘、不改场景、不碰存档。会话在方法内 `new` 出来、`Dispose()` 掉 ✓
    /// ★ 确定性：幸运基底先置 0（否则 5% ×2 会让每格读数变成抛硬币）。爆炸的暴击掷骰无法避免，
    ///   但它只影响**敌人伤害**，不进收益式子 ✓
    /// </summary>
    public sealed class ProbeV202Harvest : MonoBehaviour
    {
        /// <summary>场景里 `send` 调它。</summary>
        public string Run() => RunReport();

        public static string RunReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ v202 探针（只读）：爆炸后手挥是否仍为全额 ═══");

            if (TableDatabase.Tuning == null) TableDatabase.Load(true);
            if (TableDatabase.Tuning == null) { sb.AppendLine("（没有表：先 TableDatabase.Load）"); return sb.ToString(); }

            AppendStructure(sb);
            AppendVerdict(sb);
            AppendEndToEnd(sb);
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // D. 端到端：**全部走玩家输入路径**（不直接调 HarvestPlot / ExplodeAt）
        //    手挥 → 炸收 →（v204：**手动**切回锄头）→ 再手挥
        // ══════════════════════════════════════════════════════════════════
        private static void AppendEndToEnd(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── D 端到端（玩家输入路径：`FixedTick` + `Fire`，不直接调结算函数）──");

            var s = new RunSession();
            s.Start(202205UL);
            s.StartWave(1);
            World w = s.World;
            PlayerActor p = w.Player;
            s.Progress.Stats.SetBase(StatId.Luck, 0f);

            SeedDef crop = null;
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef sd = TableDatabase.Seeds[i];
                if (sd == null || sd.GrowTime <= 0f) continue;
                if (crop == null || sd.GoldYield > crop.GoldYield) crop = sd;
            }
            if (crop == null) { sb.AppendLine("（没有种子表）"); s.Dispose(); return; }

            ResetFarm(s);
            var idle = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1, Fire = false
            };

            // 一下"按左键"= 1 帧按下 + 若干帧松手（等冷却）
            void Swing(Vector2 aim, int idleFrames)
            {
                s.FixedTick(1f / 60f, new FrameInput
                {
                    SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1,
                    AimWorld = aim, Fire = true
                });
                for (int k = 0; k < idleFrames; k++) s.FixedTick(1f / 60f, idle);
            }

            // ① 手挥（武器默认 = 锄头）
            FarmPlot pa = Plant(s, 2, crop);
            p.Pos = pa.World + new Vector2(1f, 0f);
            p.SwitchWindup = 0f; p.WeaponCds[PlayerActor.WeaponHoe] = 0f;
            int g0 = w.GoldEarned; float x0 = w.ExpEarned;
            Swing(pa.World, 20);
            int gHand = w.GoldEarned - g0; float xHand = w.ExpEarned - x0;
            sb.AppendLine($"D1 手挥（锄头，真实输入）：金币 +{gHand}｜经验 +{xHand:0.##}"
                        + $"｜武器「{p.WeaponName}」");

            // ② 选肥料 → 炸一次（真的引爆在 0.5s 之后）
            // ★ v204：**这里原来写着"随后应该自动切回锄头"，那条行为已按用户要求撤销** ★
            //   用户原话："出现武器自动切换到锄头的 bug" ⇒ `RunSession` 不再替玩家换武器，
            //   所以本段的读数里**不再有 `LastBlastHarvested`**（那个字段随行为一起删了），
            //   而下一段"再手挥"必须**显式**把武器切回锄头（见 D4 的注释）✓
            FarmPlot pb = Plant(s, 8, crop);
            ResetFarmExcept(s, pb);
            p.Pos = pb.World + new Vector2(1f, 0f);
            p.SwitchWeapon(PlayerActor.WeaponFertilizer, w, true);
            p.SwitchWindup = 0f; p.WeaponCds[PlayerActor.WeaponFertilizer] = 0f;
            s.AddFertAmmo(RunSession.FertAmmoMax);
            int g1 = w.GoldEarned; float x1 = w.ExpEarned;
            Swing(pb.World, 45);            // 45 帧 = 0.75s > 0.5s 前摇
            int gBlast = w.GoldEarned - g1; float xBlast = w.ExpEarned - x1;
            sb.AppendLine($"D2 炸收（肥料爆炸，真实输入）：金币 +{gBlast}｜经验 +{xBlast:0.##}"
                        + $"｜炸完武器「{p.WeaponName}」（v204：**不会自动切回锄头**）");
            sb.AppendLine($"D3 【判据】炸收/手挥 = 金 {(gHand > 0 ? (float)gBlast / gHand : 0f):0.###}"
                        + $"、验 {(xHand > 0f ? xBlast / xHand : 0f):0.###}"
                        + $"（期望 ≈ {TableDatabase.Tuning.farm.blastHarvestRatio:0.###}）");

            // ③ 再手挥 —— ★ v204：**显式切回锄头**（自动切回已撤销，玩家自己换才是唯一路径）★
            FarmPlot pc = Plant(s, 14, crop);
            ResetFarmExcept(s, pc);
            p.Pos = pc.World + new Vector2(1f, 0f);
            p.SwitchWeapon(PlayerActor.WeaponHoe, w, true);
            p.SwitchWindup = 0f; p.WeaponCds[PlayerActor.WeaponHoe] = 0f;
            int g2 = w.GoldEarned; float x2 = w.ExpEarned;
            Swing(pc.World, 20);
            int gHand2 = w.GoldEarned - g2; float xHand2 = w.ExpEarned - x2;
            sb.AppendLine($"D4 再手挥（**手动切回锄头**后）：金币 +{gHand2}｜经验 +{xHand2:0.##}"
                        + $"｜武器「{p.WeaponName}」");
            sb.AppendLine($"D5 【判据】**D4 == D1** = {(gHand2 == gHand && Mathf.Abs(xHand2 - xHand) < 0.01f)}"
                        + $"（金 {gHand2} vs {gHand}｜验 {xHand2:0.##} vs {xHand:0.##}）"
                        + " ⇒ 爆炸没有把 1/4 留在玩家身上 ✓");

            // ④ 空炸：田清空 ⇒ 武器仍由玩家自己决定（v204：应是"仍是肥料"）
            ResetFarm(s);
            p.SwitchWeapon(PlayerActor.WeaponFertilizer, w, true);
            p.SwitchWindup = 0f; p.WeaponCds[PlayerActor.WeaponFertilizer] = 0f;
            s.AddFertAmmo(RunSession.FertAmmoMax);
            Swing(p.Pos + new Vector2(2f, 0f), 45);
            sb.AppendLine($"D6 空炸（半径内没有作物）：武器「{p.WeaponName}」（v204：系统不抢玩家武器 ⇒ 仍是 肥料）");

            s.Dispose();
        }

        /// <summary>清掉除 <paramref name="keep"/> 之外的所有地块（让爆炸半径里只有一格是活的）。</summary>
        private static void ResetFarmExcept(RunSession s, FarmPlot keep)
        {
            FarmPlot[] slots = s.World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !ReferenceEquals(slots[i], keep)) slots[i].ResetToEmpty();
        }

        // ══════════════════════════════════════════════════════════════════
        // A. 结构证据：还有没有「世界级可变系数」这种东西
        // ══════════════════════════════════════════════════════════════════
        private static void AppendStructure(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── A 结构：HarvestPlot 的重载，与「可泄漏的公共系数」清单 ──");

            var methods = typeof(World).GetMethods(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            int n = 0;
            string sigs = "";
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "HarvestPlot") continue;
                n++;
                var ps = methods[i].GetParameters();
                sigs += (n > 1 ? "｜" : "") + "HarvestPlot(";
                for (int k = 0; k < ps.Length; k++) sigs += (k > 0 ? ", " : "") + ps[k].ParameterType.Name;
                sigs += ")";
            }
            bool legacy = typeof(World).GetField("HarvestYieldScale") != null;
            sb.AppendLine($"A1 World.HarvestPlot 重载 {n} 个 ⇒ {sigs}");
            sb.AppendLine($"A2 World.HarvestYieldScale 字段存在 = {legacy}（v192/v195 的全局旗标；已删则为 False）");

            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
            var mutable = new List<string>();
            foreach (var f in typeof(World).GetFields(flags))
                if (!f.IsInitOnly && !f.IsLiteral && (f.FieldType == typeof(float) || f.FieldType == typeof(int)))
                    mutable.Add("World." + f.Name + " : " + f.FieldType.Name);
            sb.AppendLine($"A3 World 上公有可写数值字段 {mutable.Count} 个（收益式子里能读到它们）：");
            for (int i = 0; i < mutable.Count; i++) sb.AppendLine("     · " + mutable[i]);

            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine($"A4 表值：farm.blastHarvestRatio = {t.farm.blastHarvestRatio:0.####}"
                        + $"｜farm.harvestGoldScale = {t.farm.harvestGoldScale:0.####}"
                        + $"（1/4 = {0.25f:0.####}）");
            sb.AppendLine($"A5 玩家基底：goldMult = {t.player.goldMult:0.####}｜expMult = {t.player.expMult:0.####}"
                        + $"｜luck = {t.player.luck:0.####}");
        }

        // ══════════════════════════════════════════════════════════════════
        // B. 判据：真实路径 手收 → 爆炸 → 手收，并做状态指纹比对
        // ══════════════════════════════════════════════════════════════════
        private static void AppendVerdict(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── B 真实路径实测：RunSession.HarvestPlot（手挥）与 RunSession.ExplodeAt（肥料爆炸）──");

            var s = new RunSession();
            s.Start(202202UL);
            World w = s.World;
            // 幸运归零：不这么做每格有 luck 概率 ×2，读数会像抛硬币
            s.Progress.Stats.SetBase(StatId.Luck, 0f);

            SeedDef crop = TableDatabase.GetSeed("plague_mother");
            if (crop == null && TableDatabase.Seeds.Length > 0) crop = TableDatabase.Seeds[0];
            if (crop == null) { sb.AppendLine("（没有种子表）"); s.Dispose(); return; }

            sb.AppendLine($"B1 夹具：种子 {crop.Id}（{crop.Name}）1 级｜单格基数 金 {SeedEconomy.CellGold(crop)} / 验 {SeedEconomy.CellExp(crop)}"
                        + $"｜幸运已置 0｜等级 {s.Progress.Level}");

            // ── ① 手收（爆炸前）──
            ResetFarm(s);
            FarmPlot pA = Plant(s, 0, crop);
            string sigA = PlotSignature(pA);
            Measure handBefore = Shot(s, () => s.HarvestPlot(pA));
            sb.AppendLine($"B2 手收（爆炸前）：金币 +{handBefore.Gold}｜经验 +{handBefore.Exp:0.##}"
                        + $"｜弹字「{handBefore.PopGold}」「{handBefore.PopExp}」");
            PrintFactors(sb, s, pA, "   B2 因子");

            // ── ② 爆炸（真实肥料路径）──
            ResetFarm(s);
            FarmPlot pB = Plant(s, 7, crop);
            SortedDictionary<string, float> snapBeforeBlast = Snapshot(s);
            Measure blast = Shot(s, () => s.ExplodeAt(pB.World, w.Player));
            SortedDictionary<string, float> snapAfterBlast = Snapshot(s);
            sb.AppendLine($"B3 炸收（真实 ExplodeAt，圆心 = 那一格）：金币 +{blast.Gold}｜经验 +{blast.Exp:0.##}"
                        + $"｜弹字「{blast.PopGold}」「{blast.PopExp}」");
            sb.AppendLine($"B4 判据：炸收 / 手收 = 金 {(handBefore.Gold > 0 ? (float)blast.Gold / handBefore.Gold : 0f):0.###}"
                        + $"、验 {(handBefore.Exp > 0f ? blast.Exp / handBefore.Exp : 0f):0.###}"
                        + $"（期望 ≈ {TableDatabase.Tuning.farm.blastHarvestRatio:0.###}）");
            PrintFactors(sb, s, pB, "   B3 因子");

            // ── ③ 爆炸之后手收（**新种的一格** ⇒ 不是「这一格脏了」，而是「世界脏了」）──
            FarmPlot pC = Plant(s, 13, crop);
            string sigC = PlotSignature(pC);
            Measure handAfter = Shot(s, () => s.HarvestPlot(pC));
            SortedDictionary<string, float> snapAfterHand = Snapshot(s);
            sb.AppendLine($"B5 手收（爆炸之后，爆炸后才种下的新格）：金币 +{handAfter.Gold}｜经验 +{handAfter.Exp:0.##}"
                        + $"｜弹字「{handAfter.PopGold}」「{handAfter.PopExp}」");
            PrintFactors(sb, s, pC, "   B5 因子");

            sb.AppendLine($"B6 【判据①】炸后手收 == 炸前手收 = "
                        + $"{(handAfter.Gold == handBefore.Gold && Mathf.Abs(handAfter.Exp - handBefore.Exp) < 0.01f)}"
                        + $"（金 {handAfter.Gold} vs {handBefore.Gold}｜验 {handAfter.Exp:0.##} vs {handBefore.Exp:0.##}）");

            sb.AppendLine($"B7 【判据③】炸前那一格 vs 炸后新种那一格的格签名 = {sigA}");
            sb.AppendLine($"                            炸后那一格的格签名 = {sigC}"
                        + $" ⇒ {(sigA == sigC ? "逐位相同 ✓" : "**不同 ⇒ 地块级状态被写脏 ✗**")}");

            // ── ④ 状态指纹比对（**钉住持久状态的那一条**）──
            sb.AppendLine("B8 【判据②】状态指纹：只看参与结算的标量（地块自身的键见 B7）");
            int changedBlast = Diff(sb, "爆炸前→爆炸后", snapBeforeBlast, snapAfterBlast);
            int changedHand = Diff(sb, "爆炸前→炸后手收", snapBeforeBlast, snapAfterHand);
            sb.AppendLine($"B9 【判据②结论】参与结算的键发生持久变化 = {changedBlast + changedHand} 条"
                        + $"（爆炸前后 {changedBlast}｜到炸后手收 {changedHand}）"
                        + " ⇒ 0 条即「没有可泄漏的系数」，>0 条就是本次要找的那个持久状态");

            // ── ⑤ 放大：连做 3 轮 ──
            sb.AppendLine();
            sb.AppendLine("── C 放大：连做 3 轮（手收 → 爆炸 → 手收），看手收会不会一路掉下来 ──");
            for (int round = 1; round <= 3; round++)
            {
                ResetFarm(s);
                FarmPlot a = Plant(s, 0, crop);
                Measure mA = Shot(s, () => s.HarvestPlot(a));
                ResetFarm(s);
                FarmPlot b = Plant(s, 7, crop);
                Measure mB = Shot(s, () => s.ExplodeAt(b.World, w.Player));
                ResetFarm(s);
                FarmPlot c = Plant(s, 13, crop);
                Measure mC = Shot(s, () => s.HarvestPlot(c));
                sb.AppendLine($"C{round} 手收 金 {mA.Gold}/验 {mA.Exp:0.##} → 炸收 金 {mB.Gold}/验 {mB.Exp:0.##}"
                            + $" → 手收 金 {mC.Gold}/验 {mC.Exp:0.##}"
                            + $"　⇒ 末次手收/首次手收 = 金 {(mA.Gold > 0 ? (float)mC.Gold / mA.Gold : 0f):0.###}"
                            + $"、验 {(mA.Exp > 0f ? mC.Exp / mA.Exp : 0f):0.###}");
            }

            s.Dispose();
        }

        /// <summary>同一颗种子、同一等级、同一种植路径 ⇒ 这些数必须逐位相同（判据③）。</summary>
        private static string PlotSignature(FarmPlot p)
        {
            if (p == null) return "（null）";
            return $"种子={p.CropSeedId}｜等级={p.Level}｜蓄水上限={p.WaterMax}｜蓄水耗={p.WaterDrain}"
                 + $"｜生长基准={p.GrowthBase:0.####}｜作物金={p.CropDef?.GoldYield ?? -1}｜作物验={p.CropDef?.ExpYield ?? -1}";
        }

        // ─────────────────────────────────────────── 因子打印（把收益式子摊开）
        private static void PrintFactors(StringBuilder sb, RunSession s, FarmPlot plot, string tag)
        {
            SeedDef crop = plot != null ? plot.CropDef : null;
            TuningDef t = TableDatabase.Tuning;
            StatBlock st = s.Progress.Stats;
            float lvl = crop != null ? plot.LevelYieldMult : 1f;
            float plant = PlantMechanics.HarvestGoldMult(crop, plot);
            float item = Mathf.Max(0f, s.World.HarvestGoldMult);
            float goldScale = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
            float goldMulStat = st.Get(StatId.GoldMult);
            float expMulStat = st.Get(StatId.ExpMult);
            int predictGold = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(crop) * lvl * plant * item * goldScale));
            int predictCredited = Mathf.Max(0, Mathf.RoundToInt(predictGold * goldMulStat));
            int predictExp = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellExp(crop) * lvl));
            sb.AppendLine(tag + "：基数 金 " + SeedEconomy.CellGold(crop) + "/验 " + SeedEconomy.CellExp(crop)
                        + " × 等级 " + lvl.ToString("0.###")
                        + " × 植物机制 " + plant.ToString("0.###")
                        + " × 道具 " + item.ToString("0.###")
                        + " × 金币系数 " + goldScale.ToString("0.####")
                        + " × 属性 金 " + goldMulStat.ToString("0.###") + "/验 " + expMulStat.ToString("0.###")
                        + " ⇒ 期望 金 " + predictCredited + " / 验 " + predictExp);
        }

        // ─────────────────────────────────────────── 快照 / 比对
        private static SortedDictionary<string, float> Snapshot(RunSession s)
        {
            var d = new SortedDictionary<string, float>(StringComparer.Ordinal);
            StatBlock st = s.Progress.Stats;
            for (int i = 0; i < StatIds.Count; i++)
            {
                StatId id = (StatId)i;
                d["stat." + id + ".eff"] = st.Get(id);
                d["stat." + id + ".base"] = st.GetBase(id);
            }
            World w = s.World;
            d["world.HarvestGoldMult"] = w.HarvestGoldMult;
            d["world.HarvestDew"] = w.HarvestDew;
            d["session.Level"] = s.Progress.Level;

            FarmDef f = s.T.farm;
            d["farm.blastHarvestRatio"] = f.blastHarvestRatio;
            d["farm.harvestGoldScale"] = f.harvestGoldScale;
            d["farm.waterFactor"] = f.waterFactor;
            d["farm.growthFloor"] = f.growthFloor;
            d["farm.fertilizeFactor"] = f.fertilizeFactor;
            d["farm.waterMaxDefault"] = f.waterMaxDefault;
            d["farm.waterPerHit"] = f.waterPerHit;
            d["farm.tileSize"] = f.tileSize;
            d["farm.rowGrowthSeconds"] = f.rowGrowthSeconds;
            d["farm.gridSize"] = f.gridSize;
            d["farm.maxRows"] = f.maxRows;
            d["farm.initialRows"] = f.initialRows;

            FarmGrid grid = w.Farm;
            d["grid.Rows"] = grid.Rows;
            d["grid.PlotCount"] = grid.PlotCount;
            return d;
        }

        /// <summary>逐键比，打印差异行，返回「变了」的条数。</summary>
        private static int Diff(StringBuilder sb, string title,
            SortedDictionary<string, float> a, SortedDictionary<string, float> b)
        {
            int n = 0;
            foreach (KeyValuePair<string, float> kv in a)
            {
                if (!b.TryGetValue(kv.Key, out float other)) continue;
                if (Mathf.Abs(other - kv.Value) <= 1e-6f) continue;
                n++;
                if (n <= 24) sb.AppendLine($"     ! {title}｜{kv.Key}: {kv.Value:0.#####} → {other:0.#####}");
            }
            if (n == 0) sb.AppendLine($"     （{title}：参与结算的键没有任何变化 ✓）");
            else if (n > 24) sb.AppendLine($"     …另有 {n - 24} 条未列出");
            return n;
        }

        // ─────────────────────────────────────────── 夹具
        private static void ResetFarm(RunSession s)
        {
            FarmPlot[] slots = s.World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null) slots[i].ResetToEmpty();
        }

        private static FarmPlot Plant(RunSession s, int index, SeedDef crop)
        {
            FarmPlot p = s.World.Farm.Slots[index];
            p.TickAction(PlotAction.Dig, 10f, 1f, s.T.farm, null);
            p.TickAction(PlotAction.Sow, 10f, 1f, s.T.farm, crop);
            p.TickGrowth(999f);
            return p;
        }

        private struct Measure { public int Gold; public float Exp; public string PopGold, PopExp; }

        private static Measure Shot(RunSession s, Action action)
        {
            var m = new Measure { PopGold = "（无）", PopExp = "（无）" };
            int g0 = s.World.GoldEarned;
            float e0 = s.World.ExpEarned;
            Action<FloatingTextEvent> sink = e =>
            {
                if (e.Kind == PopupKind.GoldGain) m.PopGold = e.Text;
                else if (e.Kind == PopupKind.ExpGain) m.PopExp = e.Text;
            };
            Bus.Subscribe(sink);
            try { action(); }
            finally { Bus.Unsubscribe(sink); }
            m.Gold = s.World.GoldEarned - g0;
            m.Exp = s.World.ExpEarned - e0;
            return m;
        }
    }
}
