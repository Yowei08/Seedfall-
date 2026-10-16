using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 耕地格状态机：Empty → Dug → Growing → （成熟解放 / 植物死亡）Empty。
    /// 生长计时与洒水/施肥可并行；铲除仅允许在 Growing。
    ///
    /// ★ v166：**一格 = 一株作物**，而"一列 = 一个植物（一份种子）"★
    /// 两个种子字段的分工见 <see cref="SeedId"/> 与 <see cref="CropSeedId"/> 的注释 ——
    /// 这不是冗余，是**为了让 RunSession 的"种子 : 地块 = 1:1"契约继续成立**。
    /// </summary>
    public sealed class FarmPlot
    {
        public int Index { get; }
        public int GridX { get; }
        public int GridY { get; }
        public Vector2 World { get; }

        /// <summary>所属网格（列语义需要它）。测试里直接 new 出来的地块为 null ⇒ 退化成单格列。</summary>
        public FarmGrid Owner { get; }

        /// <summary>本列是否"列首"（第 0 行）。只有列首登记种子。</summary>
        public bool IsColumnHead { get; }

        public PlotState State { get; private set; } = PlotState.Empty;

        /// <summary>
        /// ★ **驱动这一列的种子**（只有列首持有）★
        ///
        /// ⚠️ 不是"这一格上长的是什么" —— 那是 <see cref="CropSeedId"/>。
        ///
        /// 为什么要拆成两个字段（改动前请先读这段）：
        /// `RunSession.SyncPlotsToSeeds` 强制"袋里每种种子的数量 == 占着耕地、由该种子驱动的地块数量"，
        /// 多出来的地会被**当场释放**（`plot.RemoveGrowing()`）✗
        /// 而且它在**每回合开始时都会跑**（`StartWave` → 2311 行）✓
        ///
        /// 新玩法里一株植物要占满一列的每一行 ⇒ 同一种子会驱动 3~6 格 ✗
        /// 若每一格都写 `SeedId`，那个同步就会**每回合砍掉这株植物多余的 2~5 格** ✗
        ///
        /// 所以：`SeedId` 只留在列首 ⇒ 该同步看到的是"1 个种子 : 1 格地"，契约继续成立 ✓
        /// 而每格的作物身份走 `CropSeedId` ⇒ 收益与外观都不受影响 ✓
        /// </summary>
        public string SeedId { get; private set; }

        /// <summary>
        /// ★ **这一格上实际长的作物**（每一格都有，含列首）★
        /// 收割收益（`World.HarvestPlot`）与作物外观（`WorldView.EnsurePlotCropModels`）都读它。
        /// </summary>
        public string CropSeedId { get; private set; }

        /// <summary>作物定义（补齐新行时直接拿它，不必再查表；也避免 FarmGrid 依赖 TableDatabase）。</summary>
        public SeedDef CropDef { get; private set; }

        /// <summary>
        /// ★ v168：**这一格作物的合成等级**（1/2/3）★
        ///
        /// 种下时由 `RunSession` 从 `SeedSlot.Level` 写入 ✓ 同列每格相同 ✓
        ///
        /// ★ 为什么它必须落在**地块**上，而不是每次去查种子袋 ★
        /// 收益是在 `World.HarvestPlot` 结算的，而那里只有 `FarmPlot` ✗
        /// 如果按 `CropSeedId` 回查种子袋：① 同一品种可能有多个不同等级的槽位（查哪个？）
        /// ② 把种子卖掉之后地块还在（v46 的同步会释放它，但那个顺序不保证）
        /// ⇒ 每个地块**自带**等级，收割时不需要任何外部查询 ✓ 这也是"种下即定格"的语义：
        ///   2 级种子种下去，之后就算再合成到 3 级，**地里那一株仍是 2 级** ——
        ///   等级要变，走 `SetLevel`（`SyncPlotsToSeeds` 在每回合开始时同步一次）✓
        /// </summary>
        public int Level { get; private set; } = SeedSlot.StartingLevel;

        /// <summary>这一级的收获收益倍率（×1.00 / ×1.35 / ×1.70）。</summary>
        public float LevelYieldMult => SeedEconomy.YieldMult(SeedSlot.HarvestPerLevel, Level);

        public string MutationId { get; private set; }
        public bool Watered { get; private set; }
        public bool Fertilized { get; private set; }
        public float GrowthRemaining { get; private set; }
        public float GrowthTotal { get; private set; }
        /// <summary>基础生长时间（未受洒水/施肥影响），用于计算生长下限。</summary>
        public float GrowthBase { get; private set; }
        public float Progress { get; private set; }

        public FarmPlot(int index, int gx, int gy, Vector2 world, FarmGrid owner = null, bool columnHead = true)
        {
            Index = index; GridX = gx; GridY = gy; World = world;
            Owner = owner; IsColumnHead = columnHead;
        }

        public bool Unlocked => State != PlotState.Locked;
        // ★ v166 删除的死代码 `IsTree` / `OccupiedByPlant` ★
        // `IsTree`（`SeedId == "__tree__"`）只对 `BeginTree` 有意义，而那个方法全工程无调用者；
        // `OccupiedByPlant` 是**恒 false** 的常量，写在状态机上会误导读者以为"地块能长出参战植物" ✗
        // 两者都已随 v10 的"树"模型一起废弃 ✓
        public float GrowthProgress01 => GrowthTotal <= 0f ? 0f : Mathf.Clamp01(1f - GrowthRemaining / GrowthTotal);

        /// <summary>这一格所在的列号。</summary>
        public int Column => GridX;

        public void Unlock()
        {
            if (State == PlotState.Locked) State = PlotState.Empty;
        }

        /// <summary>
        /// 把这一格清回"未开垦"。
        /// ⚠️ 单格原语：**不影响同列其它格**（成列的拆除走 <see cref="RemoveGrowing"/> /
        /// <see cref="OnPlantDied"/>，它们会先请网格清整列）。
        /// 这样 `World.ResetFarmForNewWave` 逐格调用它时不会反复触整列拆除。
        /// </summary>
        public void ResetToEmpty()
        {
            State = PlotState.Empty;
            SeedId = null;
            CropSeedId = null;
            CropDef = null;
            MutationId = null;
            Watered = false;
            Fertilized = false;
            GrowthRemaining = 0f;
            GrowthTotal = 0f;
            GrowthBase = 0f;
            Progress = 0f;
            Water = 0;
            NeedFullRefill = false;
            ThirstyNotified = false;
            FertilizeHits = 0;
            Level = SeedSlot.StartingLevel;
        }

        /// <summary>当前状态下玩家应执行的动作（UI 提示与 Bot 都读它）。</summary>
        public PlotAction NextAction
        {
            get
            {
                switch (State)
                {
                    case PlotState.Empty: return PlotAction.Dig;
                    case PlotState.Dug: return PlotAction.Sow;
                    case PlotState.Seeded: return PlotAction.Water;
                    case PlotState.Growing: return Fertilized ? PlotAction.None : PlotAction.Fertilize;
                    default: return PlotAction.None;
                }
            }
        }

        public float RequiredTime(PlotAction action, FarmDef f)
        {
            switch (action)
            {
                case PlotAction.Dig: return Mathf.Max(0.05f, f.digTime);
                case PlotAction.Sow: return Mathf.Max(0.05f, f.sowTime);
                case PlotAction.Water: return Mathf.Max(0.05f, f.waterTime <= 0f ? 0.5f : f.waterTime);
                case PlotAction.Fertilize: return Mathf.Max(0.05f, f.fertilizeTime <= 0f ? 0.5f : f.fertilizeTime);
                default: return 0f;
            }
        }

        /// <summary>
        /// 推进玩家对本地块的动作。返回是否完成了一次动作转移。
        /// 攻速影响耕作速度（策划案 §5）。waterBonus/fertBonus 来自工具品质。
        /// </summary>
        public bool TickAction(PlotAction action, float dt, float atkSpeed, FarmDef f, SeedDef sowSeed,
            float waterBonus = 0f, float fertBonus = 0f)
        {
            if (action == PlotAction.None || action != NextAction) return false;
            float speed = Mathf.Max(0.05f, atkSpeed);
            Progress += dt * speed;
            float required = RequiredTime(action, f);
            if (Progress < required) return false;
            Progress = 0f;

            switch (action)
            {
                case PlotAction.Dig:
                    State = PlotState.Dug;
                    return true;
                case PlotAction.Sow:
                    // v23：种下即自动生长（蓄水由植物决定；无掘地/洒水步骤）
                    BeginPlant(f, sowSeed, 20, 1);
                    return true;
                case PlotAction.Water:
                    // v23：洒水 = 补水
                    AddWater(3);
                    return true;
                case PlotAction.Fertilize:
                    Fertilized = true;
                    ApplyGrowthFactor(f, Mathf.Clamp(f.fertilizeFactor - fertBonus, 0.15f, 1f));
                    return true;
            }
            return false;
        }

        // ★ v166 删除的死代码 `BeginTree` ★
        // 它是 v10"播种 = 种一棵树"的入口，全工程**无调用者**（v23 之后种的是 `BeginPlant`）✓
        // 删掉它的真正理由不是"多余"，而是**它会把状态写成非法** ✗：
        // 它把 `CropSeedId` 留成 null ⇒ 一旦有人复活它，收割时 `GetSeed(null)` 拿不到 def
        // ⇒ 轻则收益退化成保底 1 金币、重则（在 `GetSeed` 改为空 id 返回 null 之前）直接抛异常 ✓
        // 留一个"能把状态写成非法"的死方法，比留一段死代码更糟 ✓

        /// <summary>树成熟后的产出计时（每隔 treeProductInterval 秒产一次）。</summary>
        public float ProductTimer;
        /// <summary>v14：施肥完成（小游戏按满 10 次）。</summary>
        public void MarkFertilized() { Fertilized = true; }

        // ── v23：蓄水（自动生长需要水）────
        /// <summary>当前蓄水量（整数）。</summary>
        public int Water;
        /// <summary>蓄水上限（由植物决定，整数）。</summary>
        public int WaterMax = 100;
        /// <summary>每 5 秒消耗的蓄水（由植物决定，整数）。</summary>
        public int WaterDrain = 1;
        private float _drainAcc;

        /// <summary>v23：是否成熟待收获。</summary>
        public bool IsRipe => State == PlotState.Grown;
        /// <summary>v23：是否缺水（缺水则停止生长）。</summary>
        public bool Thirsty => Water <= 0;
        /// <summary>v24：耗尽过一次后，必须补满（≥上限）才能重新启动生长。</summary>
        public bool NeedFullRefill;
        /// <summary>v47：缺水提示是否已经弹过（补水后重置，避免每帧刷屏）。</summary>
        public bool ThirstyNotified;
        /// <summary>v24：当前是否暂停生长（没水，或耗尽后未补满）。</summary>
        public bool GrowthPaused => Thirsty || NeedFullRefill;


        /// <summary>
        /// v23：种下一株（无掘地/播种/洒水动作）。
        ///
        /// ★ v166：种在列首时，**同列其余行会被自动补齐**（"独占一列"）★
        /// 于是 `RunSession.AutoPlantSeed` 里"找第一块空地"的旧逻辑
        /// **天然就变成了"找第一条空列"** —— 因为一列只要开了头就没有空地了 ✓
        /// 所以这一步不需要改 RunSession ✓
        /// </summary>
        public void BeginPlant(FarmDef f, SeedDef def, int waterMax, int waterDrain,
            int level = SeedSlot.StartingLevel)
        {
            // ★ 只有列首登记"驱动种子"★ 详见 SeedId 的注释（1:1 契约）
            if (IsColumnHead) SeedId = def != null ? def.Id : SeedId;
            CropSeedId = def != null ? def.Id : CropSeedId;
            if (def != null) CropDef = def;
            MutationId = null;
            State = PlotState.Growing;
            Watered = true;
            Fertilized = false;
            Level = Mathf.Clamp(level, SeedSlot.StartingLevel, SeedSlot.MaxLevel);
            // ★ v168：成熟时间除以**等级速度倍率**（1 级 ×1.00 / 2 级 ×1.15 / 3 级 ×1.30）★
            // 在**种下时**折算进 `GrowthBase`，而不是每帧在 dt 上乘一次：
            //   · `Harvest()` 用 `GrowthBase` 重置计时 ⇒ 收获后自动保持同一等级速度 ✓
            //   · `ApplyGrowthFactor` 的生长下限也以 `GrowthBase` 为基准 ⇒ 施肥/浇水的减免仍然正确 ✓
            //   植物**机制**（Haste / NeighborBoost / ElderGrowth）走另一条路：每帧乘在 dt 上 ✓
            //   两条路互不干扰，各自只有一个实现 ✓
            float baseGrow = def != null && def.GrowTime > 0f ? def.GrowTime : 8f;
            float grow = baseGrow / SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, Level);
            GrowthBase = grow;
            GrowthTotal = grow;
            GrowthRemaining = grow;
            Progress = 0f;
            WaterMax = Mathf.Max(1, waterMax);
            Water = WaterMax;
            NeedFullRefill = false;
            WaterDrain = Mathf.Max(0, waterDrain);
            _drainAcc = 0f;
            FertilizeHits = 0;

            // 列首种下 ⇒ 把这一列的其它行补齐（非列首不会再触发，天然不会递归）
            if (IsColumnHead && Owner != null) Owner.FillColumnFrom(this);
        }

        /// <summary>
        /// ★ v168：**原地改等级**（合成之后，地里那一株跟着升级）★
        ///
        /// 只改与等级有关的三件事，**不动生长进度**（按比例保留）：
        ///   ① 成熟基准时间 ÷ 新等级的速度倍率
        ///   ② 蓄水上限取新等级的需求量（`farm.waterMaxDefault + 40×(级−1)`）
        ///   ③ 当前蓄水**按比例**缩放，保持"浇到几成"
        ///
        /// ★ 为什么不直接重新 `BeginPlant` ★
        /// 那会把生长进度清零、把蓄水灌满 ⇒ 玩家每合成一次就等于**白送一轮成熟** ✗
        ///
        /// ★ 为什么蓄水按比例而不是灌满/不动 ★
        /// 不动 ⇒ 容量涨了却还是旧水 ⇒ 凭空多出一截空槽，看起来像"升级后更容易缺水" ✗
        /// 灌满 ⇒ 凭空送水 ✗ 按比例是唯一"既不多给也不克扣"的选择 ✓
        /// </summary>
        public void SetLevel(int level)
        {
            int want = Mathf.Clamp(level, SeedSlot.StartingLevel, SeedSlot.MaxLevel);
            if (want == Level) return;

            float progress01 = GrowthProgress01;
            int oldWaterMax = Mathf.Max(1, WaterMax);
            int oldWater = Water;

            Level = want;

            float baseGrow = CropDef != null && CropDef.GrowTime > 0f ? CropDef.GrowTime : (GrowthBase > 0f ? GrowthBase : 8f);
            GrowthBase = baseGrow / SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, Level);
            GrowthTotal = GrowthBase;
            GrowthRemaining = GrowthTotal * (1f - progress01);

            WaterMax = Mathf.Max(1, SeedSlot.WaterMaxFor(CropDef, Level));
            Water = Mathf.Clamp(Mathf.RoundToInt(oldWater * (WaterMax / (float)oldWaterMax)), 0, WaterMax);
            if (Water > 0) ThirstyNotified = false;
        }

        /// <summary>
        /// v23：收获——地块立刻回到"生长中"并重新计时。
        /// ★ v166：注释原来写"植物解放参战"✗ —— 那条规则已在 v165 删除 ✓
        /// 收益不在这里发：由 `World.HarvestPlot` 按**格**发金币与经验（每格 price/5 与 price/10）✓
        /// </summary>
        public void Harvest(FarmDef f)
        {
            float grow = GrowthBase > 0f ? GrowthBase : 8f;
            State = PlotState.Growing;
            GrowthRemaining = grow;
            Progress = 0f;
            // v28：收获**不补水**——锄头只负责收割；新一轮生长必须由水壶补水才能启动
            if (Water >= WaterMax) NeedFullRefill = false;
            _drainAcc = 0f;
            Fertilized = false;
        }

        /// <summary>v23：浇水（水壶命中）。</summary>
        public int AddWater(int amount)
        {
            int before = Water;
            Water = Mathf.Clamp(Water + Mathf.Max(0, amount), 0, Mathf.Max(1, WaterMax));
            // v43：补到一半以上即可重启（每次命中固定 +50，避免"永远补不满"的死锁）
            if (NeedFullRefill && Water >= WaterMax * 0.5f) NeedFullRefill = false;
            // v47：一旦重新有水，缺水提示就可以再弹一次
            if (Water > 0) ThirstyNotified = false;
            return Water - before;
        }

        /// <summary>v14：施肥小游戏已按次数（离开地块会重置）。</summary>
        public int FertilizeHits;

        /// <summary>
        /// 洒水 -30% / 施肥 -50%：**只对本次施加的那个系数乘一次**，并按已走过的进度缩放剩余时间。
        /// 下限 = 基础生长时间 × farm.growthFloor（默认 ×0.35）。
        /// </summary>
        private void ApplyGrowthFactor(FarmDef f, float factor)
        {
            float total = GrowthTotal > 0f ? GrowthTotal : 1f;
            float progress01 = Mathf.Clamp01(1f - GrowthRemaining / total);
            float newTotal = total * factor;
            float floor = GrowthBase * (f.growthFloor > 0f ? f.growthFloor : 0.35f);
            if (newTotal < floor) newTotal = floor;
            GrowthTotal = newTotal;
            GrowthRemaining = GrowthTotal * (1f - progress01);
        }

        /// <summary>
        /// v15：耕作总进度（0..1，一条进度条用）。
        /// 掘地 → 播种 → 洒水 三阶段累计，**阶段切换不归零**。
        /// </summary>
        public float OverallProgress(FarmDef f)
        {
            float tDig = Mathf.Max(0.01f, f.digTime);
            float tSow = Mathf.Max(0.01f, f.sowTime);
            float tWater = Mathf.Max(0.01f, f.waterTime);
            float total = tDig + tSow + tWater;
            switch (State)
            {
                case PlotState.Empty: return Mathf.Clamp01(Progress / tDig) * (tDig / total);
                case PlotState.Dug: return (tDig + Mathf.Clamp01(Progress / tSow) * tSow) / total;
                case PlotState.Seeded: return (tDig + tSow + Mathf.Clamp01(Progress / tWater) * tWater) / total;
                default: return 1f;
            }
        }

        /// <summary>生长推进（与玩家操作无关，独立倒计时）。</summary>
        /// <summary>
        /// v23：生长推进（缺水则停止；成熟后进入"待收获"）。
        ///
        /// ★ v166：新增 `waterCostScale`（水耗系数，默认 1 ⇒ 既有调用者行为不变）★
        ///
        /// 为什么需要它：铁根巨像的机制叫 **WaterSave（省水）**，
        /// 而旧实现是 `TickGrowth2(dt * 2)` —— 让**生长和漏水一起 ×2** ✗
        /// ⇒ 实际效果是"成熟更快，但每秒水耗**翻倍**、长熟的总水量不变" ✓
        /// ⇒ **方向与名字相反**：一个叫"省水"的机制在**加重**水分压力 ✗
        ///
        /// 现在两者拆开：
        ///   · 生长推进 = `dt`（不受影响）✓
        ///   · 漏水时钟 = `dt × waterCostScale` ⇒ 传 0.5 就是**真的水耗减半** ✓
        ///
        /// 注意它只缩放**漏水时钟**，不动 `WaterDrain` 本身（每次仍扣满额，只是扣得慢一倍）✓
        /// 这样"每 5 秒扣一次"的既有节奏与 UI 提示都保持不变 ✓
        /// </summary>
        public bool TickGrowth2(float dt, float waterCostScale = 1f, float thirstyGrowMult = 0f)
        {
            if (State != PlotState.Growing) return false;
            // ★ v168：缺水不再一定是"完全停长"★
            //
            // `thirstyGrowMult` 由 `PlantMechanics.ThirstyGrowMult` 提供：
            //   0    = 绝大多数植物 —— 缺水/耗尽未补满 ⇒ 停长（v24 起的既有行为）✓
            //   0.4  = 耐旱型（治愈三叶草）⇒ 缺水时仍以 40% 速度生长 ✓
            //
            // ★ 为什么必须显式传参，而不是在 `FarmPlot` 里查机制 ★
            // `FarmPlot` 刻意**不依赖 `TableDatabase`**（见 `CropDef` 的注释），
            // 而机制解释器住在 `PlantMechanics` ⇒ 由唯一调用方 `World.Simulate` 传进来 ✓
            if (GrowthPaused)
            {
                if (thirstyGrowMult <= 0f) return false;
                dt *= thirstyGrowMult;
            }
            if (WaterDrain > 0)
            {
                _drainAcc += dt * Mathf.Max(0f, waterCostScale);
                while (_drainAcc >= 5f)
                {
                    _drainAcc -= 5f;
                    Water = Mathf.Max(0, Water - WaterDrain);
                    if (Water <= 0) NeedFullRefill = true;   // v24：耗尽后必须补满
                }
            }
            return TickGrowth(dt);                        // v166：生长推进**不受水耗系数影响** ✓
        }

        public bool TickGrowth(float dt)
        {
            if (State != PlotState.Growing) return false;
            GrowthRemaining -= dt;
            if (GrowthRemaining > 0f) return false;
            GrowthRemaining = 0f;
            State = PlotState.Grown;
            return true;
        }

        /// <summary>
        /// 成株被收回（用于双生变异等需要立刻再占一块地的情况）。
        /// ★ v166：植物是**整列**的存在 ⇒ 一格死了整列一起回到空地 ★
        /// </summary>
        public void OnPlantDied()
        {
            if (Owner != null) Owner.ClearColumn(this);
            ResetToEmpty();
        }

        /// <summary>铲除：整列一起铲（玩家视角里"一列 = 一株植物"）。</summary>
        public bool RemoveGrowing()
        {
            if (State != PlotState.Growing) return false;
            if (Owner != null) Owner.ClearColumn(this);
            ResetToEmpty();
            return true;
        }
    }

    /// <summary>
    /// 耕地网格（v166）：
    ///   · **形状固定**：`Size` 列 × `Rows` 行，玩家**不选位置** ✓
    ///   · 开局 `farm.initialRows` 行（默认 3）⇒ 6×3 = 18 格 ✓
    ///   · **每存活 `farm.rowGrowthSeconds`（300s = 5 分钟）加一行**，封顶 `farm.maxRows`（6）✓
    ///   · **一列 = 一个植物**：列首种下 ⇒ 同列其余行自动补齐（"相当于每个植物多种 1 个"）✓
    ///   · 索引恒为 `行 * Size + 列`，**加行只追加** ⇒ 已存在地块的世界坐标永不移动 ✓
    ///
    /// 行的方向：第 0 行在最下方，加行**向上**长。因为 `Size` 是偶数，
    /// 3 行的初始田在 6 行网格里无法正好居中，与其每加一行就把整片地重新居中
    /// （那会让已经种下的作物瞬移 ✗），不如固定从下往上长 ✓
    /// </summary>
    public sealed class FarmGrid
    {
        private readonly FarmDef _farm;
        private readonly List<FarmPlot> _plots = new List<FarmPlot>();
        private FarmPlot[] _slotsArray = Array.Empty<FarmPlot>();
        private readonly int[] _groupBuffer;

        /// <summary>列数（= `farm.gridSize`，默认 6）。</summary>
        public readonly int Size;
        public readonly float TileSize;

        private int _rows;
        private readonly int _initialRows;
        private readonly int _maxRows;
        private readonly float _rowGrowthSeconds;

        /// <summary>网格容量（6×6 = 36）。</summary>
        public int Capacity => Size * Size;
        /// <summary>列数（与 <see cref="Size"/> 同义，读起来更直白）。</summary>
        public int Columns => Size;
        /// <summary>已解锁的行数（开局 `initialRows`，随存活时间增长到 `maxRows`）。</summary>
        public int Rows => _rows;
        /// <summary>加一排所需的存活秒数（`farm.rowGrowthSeconds`）。</summary>
        public float RowGrowthSeconds => _rowGrowthSeconds;
        /// <summary>行数上限。</summary>
        public int MaxRows => _maxRows;

        public FarmGrid(TuningDef tuning)
        {
            _farm = tuning.farm;
            Size = Mathf.Max(2, tuning.farm.gridSize);
            TileSize = tuning.farm.tileSize;
            _maxRows = Mathf.Clamp(tuning.farm.maxRows > 0 ? tuning.farm.maxRows : Size, 1, Size);
            _initialRows = Mathf.Clamp(tuning.farm.initialRows > 0 ? tuning.farm.initialRows : 1, 1, _maxRows);
            _rowGrowthSeconds = tuning.farm.rowGrowthSeconds;
            _groupBuffer = new int[Mathf.Max(1, tuning.tools.workCapMax)];
            _slotsArray = Array.Empty<FarmPlot>();
            _rows = 0;
            SetRows(_initialRows);
        }

        /// <summary>已获得的地块（只包含真实存在的地块；数组在增地时重建，供逐帧遍历）。</summary>
        public FarmPlot[] Slots => _slotsArray;
        public int PlotCount => _plots.Count;
        public int MaxPlots => _plots.Count;
        public int UnlockedCount => _plots.Count;

        /// <summary>v23：找最近的地块（攻击命中判定用）。</summary>
        public FarmPlot NearestPlotTo(Vector2 from, float maxDist)
        {
            FarmPlot best = null;
            float bestD = maxDist * maxDist;
            for (int i = 0; i < Slots.Length; i++)
            {
                FarmPlot p = Slots[i];
                if (p == null || p.State == PlotState.Locked) continue;
                float d = (p.World - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }
        public FarmPlot Plot(int index) => index >= 0 && index < _plots.Count ? _plots[index] : null;

        /// <summary>按（列, 行）取地块；越界返回 null。</summary>
        public FarmPlot Slot(int column, int row)
            => column < 0 || column >= Size || row < 0 || row >= _rows ? null : _plots[row * Size + column];

        /// <summary>列首（第 0 行）的地块 —— 它是"这一列有没有植物"的唯一判据。</summary>
        public FarmPlot ColumnHead(int column) => Slot(column, 0);

        // ---------------------------------------------------------- 形状
        private Vector2 WorldOf(int gx, int gy)
        {
            // 整个 Size×Size 的格子带居中（与旧版一致）⇒ 加到满行时正好居中。
            float half = (Size - 1) * 0.5f;
            return new Vector2((gx - half) * TileSize, (gy - half) * TileSize);
        }

        /// <summary>
        /// 解锁到第 `rows` 行（**只增不减**）。
        /// 新增的行如果落在"已有植物"的列上，会**立刻把作物补齐**（需求④）。
        /// </summary>
        public void SetRows(int rows)
        {
            int want = Mathf.Clamp(rows, 1, _maxRows);
            if (want <= _rows) return;
            int from = _rows;
            for (int r = from; r < want; r++)
                for (int c = 0; c < Size; c++)
                    _plots.Add(new FarmPlot(_plots.Count, c, r, WorldOf(c, r), this, r == 0));
            _rows = want;
            _slotsArray = _plots.ToArray();
            FillRows(from);          // ④ "加一排，植物自动补齐"
        }

        /// <summary>
        /// 按**存活时间**同步行数：`行数 = initialRows + ⌊存活秒数 / rowGrowthSeconds⌋`。
        ///
        /// 用"绝对时间的纯函数"而不是"累加 dt"：
        ///   · 调用频率不影响结果（掉帧、多次调用都得到同一个值）✓
        ///   · 暂停时 `World.Time` 不增长 ⇒ 商店里挂机不算"存活"✓
        ///   · 可直接用 `World.Simulate(301f)` 一次验证 5 分钟，不必真等 5 分钟 ✓
        /// </summary>
        public void SyncSurvival(float survivedSeconds)
        {
            if (_rowGrowthSeconds <= 0f) return;
            int gained = Mathf.FloorToInt(Mathf.Max(0f, survivedSeconds) / _rowGrowthSeconds);
            SetRows(Mathf.Clamp(_initialRows + gained, _initialRows, _maxRows));
        }

        /// <summary>距离下一排还有多少秒；已满行返回 -1（给 HUD / 诊断用）。</summary>
        public float SecondsToNextRow(float survivedSeconds)
        {
            if (_rows >= _maxRows || _rowGrowthSeconds <= 0f) return -1f;
            return (_rows - _initialRows + 1) * _rowGrowthSeconds - Mathf.Max(0f, survivedSeconds);
        }

        /// <summary>
        /// 兼容壳（旧语义："至少要有 maxPlots 块地"，只增不减）。
        ///
        /// `RunSession`（1130/1306）与 `World.AddExp`（1654）仍按"每级 MaxPlots"调它 ✓
        /// 新模型里 `player.maxPlots` = **可种植株数 = 列数 6**，而开局就有 18 块 ⇒
        /// 这些调用**恒为 no-op** ✓ 旧模型"按等级 +1 块耕地"因此自然消失 ✓
        /// </summary>
        public void SetMaxPlots(int maxPlots)
        {
            if (maxPlots <= PlotCount) return;
            SetRows(Mathf.CeilToInt(maxPlots / (float)Size));
        }

        /// <summary>本列是否已经有植物（判据只看列首）。</summary>
        public bool IsColumnOccupied(int column)
        {
            FarmPlot head = ColumnHead(column);
            return head != null && head.State != PlotState.Empty && head.State != PlotState.Locked;
        }

        /// <summary>已有植物的列数 = 植物株数。</summary>
        public int PlantCount
        {
            get
            {
                int n = 0;
                for (int c = 0; c < Size; c++) if (IsColumnOccupied(c)) n++;
                return n;
            }
        }

        /// <summary>还没有植物的列数（= 还能再种几株）。</summary>
        public int FreeColumnCount => Size - PlantCount;

        /// <summary>第一条空列；没有空列返回 -1（"植物独占一列"的分配规则）。</summary>
        public int FirstFreeColumn()
        {
            for (int c = 0; c < Size; c++) if (!IsColumnOccupied(c)) return c;
            return -1;
        }

        // ---------------------------------------------------------- 整列补齐 / 整列拆除
        /// <summary>
        /// 列首种下后，把同列其余**还空着的**行补齐成同一株作物（需求④的即时版）。
        /// 非列首调用 <see cref="FarmPlot.BeginPlant"/> 不会再次触发它 ⇒ 不会递归。
        /// </summary>
        internal void FillColumnFrom(FarmPlot head)
        {
            if (head == null || !head.IsColumnHead) return;
            for (int r = 1; r < _rows; r++)
            {
                FarmPlot p = Slot(head.Column, r);
                if (p == null || p.State != PlotState.Empty) continue;
                // v168：等级与蓄水上限一并传下去 —— 整列 = 同一株，等级必须一致 ✓
                p.BeginPlant(_farm, head.CropDef, head.WaterMax, head.WaterDrain, head.Level);
            }
        }

        /// <summary>
        /// ★ v168：把整列的等级同步成 `level`（合成之后调用）★
        ///
        /// 一列 = 一株植物 ⇒ 等级必须整列一致 ✓ 只改列首会让"同一株"的两行收益不同 ✗
        /// 幂等：等级没变时 `FarmPlot.SetLevel` 直接返回 ⇒ 每回合调用是安全的 ✓
        /// </summary>
        public void SetColumnLevel(FarmPlot from, int level)
        {
            if (from == null) return;
            int col = from.Column;
            for (int i = 0; i < _plots.Count; i++)
            {
                FarmPlot p = _plots[i];
                if (p == null || p.Column != col) continue;
                if (p.State != PlotState.Growing && p.State != PlotState.Grown && p.State != PlotState.Seeded) continue;
                p.SetLevel(level);
            }
        }

        /// <summary>把整列清回未开垦（from 自己由调用方负责清）。</summary>
        internal void ClearColumn(FarmPlot from)
        {
            if (from == null) return;
            int col = from.Column;
            for (int i = 0; i < _plots.Count; i++)
            {
                FarmPlot p = _plots[i];
                if (p == from || p.Column != col) continue;
                p.ResetToEmpty();
            }
        }

        /// <summary>新增行落在已有植物的列上时，把这株植物种到新行里（需求④）。</summary>
        private void FillRows(int fromRow)
        {
            for (int c = 0; c < Size; c++)
            {
                FarmPlot head = ColumnHead(c);
                if (head == null || head.State == PlotState.Empty) continue;
                if (head.CropDef == null) continue;
                for (int r = Mathf.Max(1, fromRow); r < _rows; r++)
                {
                    FarmPlot p = Slot(c, r);
                    if (p == null || p.State != PlotState.Empty) continue;
                    // v168：新行长出来的作物**沿用列首的等级**（新玩法"加一排 = 每株多一份作物"）✓
                    p.BeginPlant(_farm, head.CropDef, head.WaterMax, head.WaterDrain, head.Level);
                }
            }
        }

        // ---------------------------------------------------------- 动作成组
        /// <summary>
        /// 一次动作作用的耕地组：目标地块 + **同一行**最近的地块（左优先），
        /// 最多 count 块（count = 1 + 工具/手套品质），只挑"当前动作适用"的地块。
        /// （跨列成组是有意的：工具品质越高越省手，列与列的归属不受影响。）
        /// </summary>
        public int BuildActionGroup(int plotIndex, int count, PlotAction action)
        {
            int used = 0;
            FarmPlot target = Plot(plotIndex);
            if (target == null) return 0;
            if (action == PlotAction.None) return 0;
            if (target.NextAction != action) return 0;

            int cap = Mathf.Min(_groupBuffer.Length, Mathf.Max(1, count));
            _groupBuffer[used++] = target.Index;

            // 横向拓展：先左后右，按列距由近到远
            for (int d = 1; d < Size && used < cap; d++)
            {
                TryAddToGroup(target.GridY, target.GridX - d, action, cap, ref used);
                if (used >= cap) break;
                TryAddToGroup(target.GridY, target.GridX + d, action, cap, ref used);
            }
            return used;
        }

        private void TryAddToGroup(int gy, int gx, PlotAction action, int cap, ref int used)
        {
            if (gx < 0 || gx >= Size || gy < 0 || gy >= _rows || used >= cap) return;
            FarmPlot p = _plots[gy * Size + gx];      // 行主序 ⇒ 直接下标，不再线性扫描
            if (p.NextAction != action) return;
            _groupBuffer[used++] = p.Index;
        }

        public int GroupMember(int i) => i >= 0 && i < _groupBuffer.Length ? _groupBuffer[i] : -1;

        /// <summary>返回范围内最近的已解锁地块，-1 表示不在范围内。</summary>
        public int NearestUnlocked(Vector2 p, float maxDist)
        {
            int best = -1;
            float bestD = maxDist * maxDist;
            for (int i = 0; i < _plots.Count; i++)
            {
                FarmPlot plot = _plots[i];
                float d = (plot.World - p).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = plot.Index; }
            }
            return best;
        }

        /// <summary>找一块空着的已解锁地块（双生变异用）。</summary>
        public FarmPlot FindFreePlot(Vector2 near)
        {
            FarmPlot best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _plots.Count; i++)
            {
                FarmPlot plot = _plots[i];
                if (plot.State != PlotState.Empty) continue;
                float d = (plot.World - near).sqrMagnitude;
                if (d < bestD) { bestD = d; best = plot; }
            }
            return best;
        }

        public int UsedPlotCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _plots.Count; i++)
                {
                    PlotState s = _plots[i].State;
                    if (s == PlotState.Dug || s == PlotState.Growing || s == PlotState.Grown) n++;
                }
                return n;
            }
        }

        public int GrowingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _plots.Count; i++) if (_plots[i].State == PlotState.Growing) n++;
                return n;
            }
        }
    }
}
