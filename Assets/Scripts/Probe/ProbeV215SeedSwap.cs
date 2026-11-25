using System;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v215 只读探针：**战斗模式换种** ★★
    ///
    /// 用户原话："战斗模式下的植物存在此 bug，没有同步更新，更换了种子都没改变耕地"
    ///
    /// ── 这个探针要回答的三件事 ──
    ///   ① 换种走的是**真输入路径**吗：`FrameInput.ReplaceSeed = true` → `ConsumeFrameEdges`
    ///      → `TryReplaceSeedOnPlot` → `ReplaceSeedOnPlot`（`GameBootstrap` 里只有 Q 键那一行把它置真）✓
    ///   ② 田里真的变了吗：`FarmPlot.CropSeedId` / `CropDef` / **整列每一行**（一列 = 一株）✓
    ///   ③ 种子账对不对：A **+1** / B **−1** / 占用槽位**净变化 0** ✓
    ///   ④ 过几秒、并且**跨过一次 `SyncPlotsToSeeds`**（那一句就是"旧状态覆盖"的唯一通路）之后，
    ///      田里**仍然是 B** ✓
    ///   ⑤ 对照组：**不解绑**（列首仍挂着 `SeedId = B` 而袋里 B 槽 = 0）会怎样 ——
    ///      `SyncPlotsToSeeds` 把这一列**回收**掉，作物凭空消失 ✓
    ///      这就是 `ReplaceSeedOnPlot` 第 ⑤ 步（`DetachColumnSeed`）存在的理由。
    ///
    /// ★ 只读 ★：不写盘、不改场景、不碰存档；会话在方法内 `new` 出来、`Dispose()` 掉 ✓
    /// ★ 确定性 ★：固定种子 `215001` / `215002`；玩家每帧回满血（这一段只关心耕地，
    ///   但玩家一死阶段就切 Defeat ⇒ 后面的 `StartWave` 白跑）✓
    /// </summary>
    public sealed class ProbeV215SeedSwap : MonoBehaviour
    {
        /// <summary>场景里 `send` 调它。</summary>
        public string Run() => RunReport();

        public static string RunReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ v215 探针（只读）：战斗模式换种 ═══");

            if (TableDatabase.Tuning == null) TableDatabase.Load(true);
            if (TableDatabase.Tuning == null) { sb.AppendLine("（没有表：先 TableDatabase.Load）"); return sb.ToString(); }

            AppendMain(sb);
            AppendControl(sb);
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // 主场景：判据 ① ② ③
        // ══════════════════════════════════════════════════════════════════
        private static void AppendMain(StringBuilder sb)
        {
            SeedDef A = PickSeed(0), B = PickSeed(1);
            if (A == null || B == null) { sb.AppendLine("（种子表不足两种，跳过）"); return; }

            var s = new RunSession();
            try
            {
                s.Start(215001UL);
                s.StartWave(1);                 // ⇒ Phase = Combat（真开局那一句）
                World w = s.World;
                ResetFarm(s);
                ClearBag(s);
                if (s.Bag.SlotCount < 2) s.Bag.SetCapacity(2);

                // 夹具：A 已经长在第 0 列；B 在种子袋第 1 槽**但没种下去**
                s.Bag[0].Def = A; s.Bag[0].MutationId = null; s.Bag[0].Quality = A.RarityTier; s.Bag[0].Level = 1;
                s.Bag[1].Def = B; s.Bag[1].MutationId = null; s.Bag[1].Quality = B.RarityTier; s.Bag[1].Level = 1;
                s.Bag.Selected = 0;
                if (!s.AutoPlantSeed(A, 1)) { sb.AppendLine("（AutoPlantSeed 失败，跳过）"); return; }

                FarmGrid grid = w.Farm;
                FarmPlot col = grid.ColumnHead(0);
                int a0 = CountOf(s, A), b0 = CountOf(s, B), used0 = s.Bag.OccupiedCount;
                w.Player.Pos = col.World;        // 站在这一格上 ⇒ `NearestUnlocked` 必选它

                sb.AppendLine();
                sb.AppendLine("── 判据 0：换种前的夹具 ──");
                sb.AppendLine($"   阶段={s.Phase}｜种子袋 {used0}/{s.Bag.SlotCount} 槽｜耕地 {grid.PlotCount} 格 / {grid.Rows} 行 / {grid.Size} 列");
                sb.AppendLine($"   A = {A.Name}（{A.Id}）⇒ 第 {col.Column} 列（列首格 {col.Index}）");
                sb.AppendLine($"   B = {B.Name}（{B.Id}）⇒ 种子袋第 1 槽，**没有种下去**");
                sb.AppendLine($"   这一列：{RowIds(grid, col.Column)}");
                sb.AppendLine($"   列首读数：CropSeedId={col.CropSeedId}｜SeedId={col.SeedId}｜CropDef={NameOf(col.CropDef)}｜Level={col.Level}");
                sb.AppendLine($"   种子计数：A 槽 {a0}｜B 槽 {b0}｜占用 {used0}｜生长中的地块（按 SeedId 计）{s.GrowingPlotCount()}");

                // ── 判据 ①：按 Q（= 唯一入口那一行所置的脉冲）──
                sb.AppendLine();
                sb.AppendLine("── 判据 ①：按 Q（`FrameInput.ReplaceSeed` → `ConsumeFrameEdges`）──");
                var press = Idle();
                press.ReplaceSeed = true;
                s.ConsumeFrameEdges(ref press);
                int a1 = CountOf(s, A), b1 = CountOf(s, B), used1 = s.Bag.OccupiedCount;

                sb.AppendLine($"   提示文案：{s.Prompt}");
                sb.AppendLine($"   脉冲已清零（追帧不会重复换种）：ReplaceSeed={press.ReplaceSeed}");
                sb.AppendLine($"   这一列：{RowIds(grid, col.Column)}");
                sb.AppendLine($"   列首读数：CropSeedId={col.CropSeedId}｜SeedId={OrNull(col.SeedId)}｜CropDef={NameOf(col.CropDef)}｜Level={col.Level}｜Water={col.Water}/{col.WaterMax}｜生长 {col.GrowthRemaining:0.0}s/{col.GrowthTotal:0.0}s");
                sb.AppendLine($"   判据①-a 田里变成 B：{(col.CropSeedId == B.Id ? "PASS" : "FAIL")}（CropSeedId={col.CropSeedId}，期望 {B.Id}）");
                sb.AppendLine($"   判据①-a' 整列都是 B：{(WholeColumnIs(grid, col.Column, B.Id) ? "PASS" : "FAIL")}");
                sb.AppendLine($"   种子计数：A 槽 {a0} → {a1}（{(a1 - a0 == +1 ? "PASS +1" : "FAIL")}）｜B 槽 {b0} → {b1}（{(b1 - b0 == -1 ? "PASS −1" : "FAIL")}）｜占用 {used0} → {used1}");
                sb.AppendLine($"   判据③ 种子总数守恒：{(used1 == used0 ? "PASS" : "FAIL")}（总变化 {used1 - used0:+0;-0;0}）");

                // ── 判据 ②：时间 + 同步 ──
                sb.AppendLine();
                sb.AppendLine("── 判据 ②：换种后过几秒 / 跨过同步，田里仍是 B ──");
                Tick(s, 180);                                   // +3.0s
                sb.AppendLine($"   +3.0s（180 固定帧）后：CropSeedId={col.CropSeedId}｜{(col.CropSeedId == B.Id ? "PASS 仍是 B" : "FAIL 被改回去了")}｜生长进度 {col.GrowthProgress01 * 100f:0}%");
                s.SyncPlotsToSeeds(out int r1, out int p1);      // "旧状态覆盖"的唯一通路，直接调一次读计数
                sb.AppendLine($"   `SyncPlotsToSeeds` 后：released={r1} planted={p1}｜CropSeedId={col.CropSeedId}｜{(col.CropSeedId == B.Id ? "PASS 仍是 B" : "FAIL 被回收/覆盖")}");
                s.StartWave(2);                                 // 真正会触发同步的那一句（分钟边界走的同一条路）
                sb.AppendLine($"   StartWave(2) 后：阶段={s.Phase}｜CropSeedId={col.CropSeedId}｜{(col.CropSeedId == B.Id ? "PASS 仍是 B" : "FAIL 被回收/覆盖")}");
                Tick(s, 180);                                   // 再 +3.0s
                sb.AppendLine($"   再 +3.0s 后：CropSeedId={col.CropSeedId}｜{(col.CropSeedId == B.Id ? "PASS 仍是 B" : "FAIL 被改回去了")}｜生长进度 {col.GrowthProgress01 * 100f:0}%");

                int a2 = CountOf(s, A), b2 = CountOf(s, B), used2 = s.Bag.OccupiedCount;
                sb.AppendLine($"   一轮之后种子计数：A 槽 {a0} → {a2}｜B 槽 {b0} → {b2}｜占用 {used0} → {used2}（总变化 {used2 - used0:+0;-0;0}）");
                sb.AppendLine($"   全田作物：{AllCrops(grid)}");
            }
            finally { s.Dispose(); }
        }

        // ══════════════════════════════════════════════════════════════════
        // 对照组：**不解绑**会怎样（= 换种第 ⑤ 步存在的理由）
        // ══════════════════════════════════════════════════════════════════
        private static void AppendControl(StringBuilder sb)
        {
            SeedDef B = PickSeed(1);
            if (B == null) return;

            var s = new RunSession();
            try
            {
                s.Start(215002UL);
                s.StartWave(1);
                ResetFarm(s);
                ClearBag(s);                       // ⇒ 袋里任何一种种子的槽位都是 0

                FarmPlot col = s.World.Farm.ColumnHead(2);
                // 这就是"扣掉了新种子、却仍把 SeedId 挂在列首"的那个状态（v215 之前的写法）
                col.BeginPlant(s.T.farm, B, SeedSlot.WaterMaxFor(B, 1), B.WaterDrain, 1);

                sb.AppendLine();
                sb.AppendLine("── 对照组：**不解绑**（列首挂 SeedId = 扣掉的那颗种子）──");
                sb.AppendLine($"   这一列：{RowIds(s.World.Farm, col.Column)}｜SeedId={OrNull(col.SeedId)}｜袋里 {B.Name} 的槽位 = {CountOf(s, B)}");
                s.SyncPlotsToSeeds(out int rr, out int pp);
                sb.AppendLine($"   `SyncPlotsToSeeds` ⇒ released={rr} planted={pp}｜这一列：{RowIds(s.World.Farm, col.Column)}｜CropSeedId={OrNull(col.CropSeedId)}");
                sb.AppendLine($"   ⇒ {(rr > 0 ? "作物被回收（这就是 v215 第 ⑤ 步要解绑的原因）" : "（没有回收，与预期不符，需复查）")}");
            }
            finally { s.Dispose(); }
        }

        // ─────────────────────────────────────────── 夹具 / 读数
        private static void Tick(RunSession s, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                // 这一段只关心耕地；但玩家一死阶段就切 Defeat ⇒ 后面的路径白跑
                s.World.Player.Health.Heal(s.World.Player.Health.MaxHp);
                s.FixedTick(1f / 60f, Idle());
            }
        }

        private static FrameInput Idle() => new FrameInput
        {
            SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1,
            RewardPick = -1, RewardSelect = -1, SlotMerge = -1, ClickedSlot = -1
        };

        private static SeedDef PickSeed(int nth)
        {
            int seen = 0;
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                if (d.GrowTime <= 0f) continue;
                if (seen++ == nth) return d;
            }
            return null;
        }

        private static void ResetFarm(RunSession s)
        {
            FarmPlot[] slots = s.World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) slots[i].ResetToEmpty();
        }

        private static void ClearBag(RunSession s)
        {
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot sl = s.Bag[i];
                if (sl == null) continue;
                sl.Def = null;
                sl.MutationId = null;
                sl.Quality = 0;
                sl.Level = SeedSlot.StartingLevel;
            }
        }

        private static int CountOf(RunSession s, SeedDef def)
        {
            int n = 0;
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot sl = s.Bag[i];
                if (sl == null || sl.IsEmpty || sl.Def == null) continue;
                if (sl.Def.Id == def.Id) n++;
            }
            return n;
        }

        /// <summary>整列每一行（一列 = 一株 ⇒ 换种必须整列一起变）。</summary>
        private static string RowIds(FarmGrid g, int column)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < g.Rows; r++)
            {
                FarmPlot p = g.Slot(column, r);
                if (r > 0) sb.Append(" ｜ ");
                sb.Append('r').Append(r).Append('=');
                if (p == null) { sb.Append("(无地块)"); continue; }
                sb.Append(OrNull(p.CropSeedId)).Append('[').Append(p.State).Append(']');
            }
            return sb.ToString();
        }

        private static bool WholeColumnIs(FarmGrid g, int column, string seedId)
        {
            for (int r = 0; r < g.Rows; r++)
            {
                FarmPlot p = g.Slot(column, r);
                if (p == null || p.CropSeedId != seedId) return false;
            }
            return true;
        }

        private static string AllCrops(FarmGrid g)
        {
            var sb = new StringBuilder();
            int n = 0;
            for (int c = 0; c < g.Size; c++)
            {
                FarmPlot head = g.ColumnHead(c);
                if (head == null || string.IsNullOrEmpty(head.CropSeedId)) continue;
                if (n++ > 0) sb.Append(" ｜ ");
                sb.Append('列').Append(c).Append('=').Append(head.CropSeedId)
                  .Append('(').Append(head.State).Append(",SeedId=").Append(OrNull(head.SeedId)).Append(')');
            }
            return n == 0 ? "（空田）" : sb.ToString();
        }

        private static string NameOf(SeedDef d) => d == null ? "(null)" : d.Name;
        private static string OrNull(string v) => string.IsNullOrEmpty(v) ? "(null)" : v;
    }
}
