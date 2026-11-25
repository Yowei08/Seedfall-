using NUnit.Framework;
using UnityEngine;

namespace Seedfall.Tests
{
    /// <summary>
    /// 公式回归：策划案 §3/§4/§5 的每一条都必须能用固定输入手算出固定输出（硬规则：数值可复算）。
    /// </summary>
    public class FormulaTests
    {
        [OneTimeSetUp]
        public void LoadTables()
        {
            TableDatabase.Load();
        }

        [Test]
        public void AttackFormula_MatchesHandComputed()
        {
            // v162：z = (基准伤害 + 伤害属性)·伤害倍率
            // x=22(C/B 级基础值), 伤害属性=46, 伤害倍率=2.0 → (22+46)*2 = 136
            Assert.AreEqual(136f, DamageResolver.AttackFormula(22f, 46f, 2f, false, 1.5f), 1e-3f);
            // 暴击 ×1.5 → 204
            Assert.AreEqual(204f, DamageResolver.AttackFormula(22f, 46f, 2f, true, 1.5f), 1e-3f);
            // 下限保护：minDamage
            Assert.AreEqual(1f, DamageResolver.AttackFormula(0f, 0f, 0f, false, 1.5f), 1e-6f);

            // v45：伤害属性（加法）与伤害倍率（乘法）是两条独立属性，各自生效、相乘叠加
            Assert.AreEqual(14f, DamageResolver.AttackFormula(10f, 4f, 1f, false, 1.5f, 0.1f), 1e-3f, "只加伤害属性");
            Assert.AreEqual(15f, DamageResolver.AttackFormula(10f, 0f, 1.5f, false, 1.5f, 0.1f), 1e-3f, "只加伤害倍率");
            Assert.AreEqual(21f, DamageResolver.AttackFormula(10f, 4f, 1.5f, false, 1.5f, 0.1f), 1e-3f, "两条相乘");

            // v162：**继承率已被删除** —— 公式对"这份属性来自近战还是远程"一视同仁，
            // 只把传进来的数值直接相加。下面这组是旧公式的表达不了的新恒等式：
            // `(基础 + 属性) × 倍率`，没有任何比例乘区留在中间。
            Assert.AreEqual(50f, DamageResolver.AttackFormula(30f, 20f, 1f, false, 2f), 1e-3f, "基准 30 + 属性 20 = 50");
            Assert.AreEqual(60f, DamageResolver.AttackFormula(30f, 20f, 1.2f, false, 2f), 1e-3f, "再乘 120% = 60");

            // ★ v195：`ExplodeFormula`（爆炸倍率）整条删除 ⇒ 肥料与锄头/水壶**同一个式子** ★
            //   断言**没有删**，改的是它们钉的那件事：从"爆炸 = 普通 × 倍率"变成
            //   "肥料 = (基础 + 爆炸伤害) × 倍率，没有第二层乘区" ✓
            Assert.AreEqual(19f, DamageResolver.WeaponBlastDamage(19f, 0f, 1f, false, 2f, 0.1f), 1e-3f, "倍率已删除：基础原样");
            Assert.AreEqual(22f, DamageResolver.WeaponBlastDamage(19f, 3f, 1f, false, 2f, 0.1f), 1e-3f, "吃爆炸伤害：19 + 3");
            Assert.AreEqual(33f, DamageResolver.WeaponBlastDamage(19f, 3f, 1.5f, false, 2f, 0.1f), 1e-3f, "再乘伤害倍率 150%");
        }

        [Test]
        public void HealGuardLeech_MatchHandComputed()
        {
            // 恢复 q=(x+y·m)·n ；x=8, regen=10, m=0.8, n=1.0 → 16
            Assert.AreEqual(16f, DamageResolver.HealFormula(8f, 10f, 0.8f, 1f), 1e-3f);
            // 护卫 q=(x+maxHp·m)·n ；x=30, maxHp=100, m=0.35, n=1.0 → 65
            Assert.AreEqual(65f, DamageResolver.GuardFormula(30f, 100f, 0.35f, 1f), 1e-3f);
            // 汲取 w=z·(0.1+leech) ；z=100, leech=0.2 → 30
            Assert.AreEqual(30f, DamageResolver.LeechFormula(100f, 0.2f, 0.1f), 1e-3f);
        }

        [Test]
        public void AilmentThreshold_UsesMaxOfRatioAndFloor()
        {
            FormulaDef f = TableDatabase.Tuning.formula;
            // 阈值 = max(freezeThresholdMin, maxHp × freezeThresholdRatio)
            // maxHp=200 → 200×0.5 = 100 > 4 → 取 100
            Assert.AreEqual(200f * f.freezeThresholdRatio, DamageResolver.AilmentThreshold(200f, f), 1e-3f);
            // maxHp=40 → 40×0.5 = 20 > 4 → 取 20
            Assert.AreEqual(20f, DamageResolver.AilmentThreshold(40f, f), 1e-3f);
            // maxHp=4 → 2 < 下限 → 取 f.freezeThresholdMin
            Assert.AreEqual(f.freezeThresholdMin, DamageResolver.AilmentThreshold(4f, f), 1e-3f);
            Assert.Greater(f.freezeThresholdMin, 0f);
        }

        [Test]
        public void ExpCurve_FollowsConfiguredFormula()
        {
            GrowthDef g = TableDatabase.Tuning.growth;
            // round(expBase + expLinear·(level-1)^expExponent)；v48：expBase 24→20、expLinear 12→11
            // ★ v209（用户原话"降低前期升级需要经验值"）：expBase 20→12，而 expLinear 11 与 expExponent 1.35
            //   未动 ⇒ 只降"每级固定底"（L1 -40.0%、L10 -3.4%）。三条期望值随之更新，**一条没删** ✓
            Assert.AreEqual(12f, PlayerProgress.ExpToNextFor(1, g));   // 12 + 11×0^1.35
            Assert.AreEqual(23f, PlayerProgress.ExpToNextFor(2, g));   // 12 + 11×1
            Assert.AreEqual(40f, PlayerProgress.ExpToNextFor(3, g));   // 12 + 11×2^1.35 ≈ 40.04 → 40
            Assert.Greater(PlayerProgress.ExpToNextFor(10, g), PlayerProgress.ExpToNextFor(5, g));
        }

        [Test]
        public void StatBlock_AppliesAddThenMulThenCaps()
        {
            var block = new StatBlock(TableDatabase.Tuning.caps, TableDatabase.Tuning.player);
            block.AddModifier(new StatModifier(StatId.Dodge, 0.5f, ModOp.Add, "t"));
            block.AddModifier(new StatModifier(StatId.Dodge, 10f, ModOp.Mul, "t"));
            Assert.AreEqual(TableDatabase.Tuning.caps.dodge, block.Get(StatId.Dodge), 1e-4f, "闪避必须被硬上限 0.60 裁掉");

            block.AddModifier(new StatModifier(StatId.AtkSpeed, 5f, ModOp.Add, "t"));
            Assert.AreEqual(TableDatabase.Tuning.caps.atkSpeed, block.Get(StatId.AtkSpeed), 1e-4f);

            // v162：三种伤害类型各是一条独立的加法属性（以前只有一条 AtkDamage）
            block.AddModifier(new StatModifier(StatId.MeleeDamage, 5f, ModOp.Add, "t"));
            Assert.AreEqual(6f, block.Get(StatId.MeleeDamage), 1e-4f, "近战伤害基础 1 + 5 = 6");
            Assert.AreEqual(1f, block.Get(StatId.RangedDamage), 1e-4f, "加近战不得影响远程");
            Assert.AreEqual(1f, block.Get(StatId.BlastDamage), 1e-4f, "加近战不得影响爆炸");

            // v45：伤害倍率是与伤害属性并列的第二条属性
            var fresh = new StatBlock(TableDatabase.Tuning.caps, TableDatabase.Tuning.player);
            Assert.AreEqual(1f, fresh.Get(StatId.AtkMult), 1e-4f, "伤害倍率基础 100%");
            fresh.AddModifier(new StatModifier(StatId.AtkMult, 1.5f, ModOp.Mul, "t"));
            Assert.AreEqual(1.5f, fresh.Get(StatId.AtkMult), 1e-4f);
            Assert.AreEqual(1f, fresh.Get(StatId.MeleeDamage), 1e-4f, "伤害倍率不得影响近战伤害");
            Assert.AreEqual(1f, fresh.Get(StatId.RangedDamage), 1e-4f, "伤害倍率不得影响远程伤害");
            Assert.AreEqual(1f, fresh.Get(StatId.BlastDamage), 1e-4f, "伤害倍率不得影响爆炸伤害");
        }

        /// <summary>
        /// v23 耕作流程：掘地 → 播种（种下即生长）→ 施肥（生长 -50%）→ 浇水（只补蓄水）。
        /// 注意 v22 的"洒水加速生长"已删除——farm.waterFactor 因此不再参与生长计算，
        /// 生长中地块的唯一合法动作是施肥（这是 v23 的实际规则，旧断言写的是 v22 流程）。
        /// </summary>
        [Test]
        public void Farming_DigSowFertilizeAndWater()
        {
            TuningDef t = TableDatabase.Tuning;
            var grid = new FarmGrid(t);
            FarmPlot plot = grid.Slots[0];
            FarmDef farm = t.farm;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");

            Assert.AreEqual(PlotState.Empty, plot.State);
            Assert.AreEqual(PlotAction.Dig, plot.NextAction);
            Assert.IsTrue(plot.TickAction(PlotAction.Dig, farm.digTime + 0.05f, 1f, farm, null));
            Assert.AreEqual(PlotState.Dug, plot.State);

            Assert.AreEqual(PlotAction.Sow, plot.NextAction);
            Assert.IsTrue(plot.TickAction(PlotAction.Sow, farm.sowTime + 0.05f, 1f, farm, radish));
            Assert.AreEqual(PlotState.Growing, plot.State);
            Assert.AreEqual(radish.GrowTime, plot.GrowthRemaining, 1e-3f);

            // 生长中：唯一合法动作是施肥；洒水动作在 v23 已不存在
            Assert.AreEqual(PlotAction.Fertilize, plot.NextAction);
            Assert.IsFalse(plot.TickAction(PlotAction.Water, 1f, 1f, farm, null), "生长中不再有洒水动作");
            Assert.IsTrue(plot.TickAction(PlotAction.Fertilize, farm.fertilizeTime + 0.05f, 1f, farm, null));
            Assert.IsTrue(plot.Fertilized);
            Assert.AreEqual(radish.GrowTime * farm.fertilizeFactor, plot.GrowthRemaining, 1e-3f);
            Assert.AreEqual(PlotAction.None, plot.NextAction, "施肥后地块无待办动作");

            // 浇水 = 水壶命中后补蓄水，不再直接改生长时间
            plot.Water = 0;
            Assert.AreEqual(3, plot.AddWater(3));
            Assert.AreEqual(3, plot.Water);

            // 生长推进到 0 后进入成株
            plot.Water = plot.WaterMax;
            Assert.IsFalse(plot.TickGrowth(plot.GrowthRemaining - 0.01f));
            Assert.IsTrue(plot.TickGrowth(0.05f));
            Assert.AreEqual(PlotState.Grown, plot.State);
        }

        [Test]
        public void Farming_AttackSpeedShortensDigAndSow()
        {
            TuningDef t = TableDatabase.Tuning;
            var grid = new FarmGrid(t);
            FarmPlot a = grid.Slots[0];
            FarmPlot b = grid.Slots.Length > 1 ? grid.Slots[1] : null;
            // 攻速 2.0 时：2.0s × 2 = 4.0 < digTime(5) 未完成；再 0.6s × 2 = 1.2 → 5.2 ≥ 5 完成
            Assert.IsFalse(a.TickAction(PlotAction.Dig, 2.0f, 2f, t.farm, null));
            Assert.IsTrue(a.TickAction(PlotAction.Dig, 0.6f, 2f, t.farm, null));
            Assert.AreEqual(PlotState.Dug, a.State);
            if (b != null) Assert.AreEqual(PlotState.Empty, b.State);
        }

        /// <summary>v23：动作必须与地块当前状态匹配（NextAction），否则一律忽略。</summary>
        [Test]
        public void Farming_WrongActionIsIgnored()
        {
            TuningDef t = TableDatabase.Tuning;
            var grid = new FarmGrid(t);
            FarmPlot plot = grid.Slots[0];
            SeedDef radish = TableDatabase.GetSeed("radish_guard");

            // 空地只有"掘地"是合法动作
            Assert.AreEqual(PlotAction.Dig, plot.NextAction);
            Assert.IsFalse(plot.TickAction(PlotAction.Sow, 10f, 1f, t.farm, radish));
            Assert.IsFalse(plot.TickAction(PlotAction.Water, 10f, 1f, t.farm, null));
            Assert.IsFalse(plot.TickAction(PlotAction.Fertilize, 10f, 1f, t.farm, null));
            Assert.AreEqual(PlotState.Empty, plot.State);

            // 掘地后：只剩播种
            plot.TickAction(PlotAction.Dig, 10f, 1f, t.farm, null);
            Assert.AreEqual(PlotState.Dug, plot.State);
            plot.TickAction(PlotAction.Sow, 10f, 1f, t.farm, radish);
            Assert.AreEqual(PlotState.Growing, plot.State);

            // 生长中：不能重复播种、不能回头掘地
            Assert.IsFalse(plot.TickAction(PlotAction.Sow, 10f, 1f, t.farm, radish), "已在生长，不能重复播种");
            Assert.IsFalse(plot.TickAction(PlotAction.Dig, 10f, 1f, t.farm, null), "已在生长，不能回头掘地");
            Assert.AreEqual(PlotState.Growing, plot.State);
        }

        /// <summary>
        /// v166：田地是**固定形状**（列 × 行），玩家不选位置。
        /// 取代旧的 `PlotUnlock_StartsAtCenterAndExpandsAdjacent` —— 那条测的是
        /// "从中心 3×3 起、按相邻关系逐块开垦"，而这套模型连同 `farm.initialPlots` 已被删除。
        /// </summary>
        [Test]
        public void FarmShape_IsFixedGrid_GrowsByRowsOnly()
        {
            TuningDef t = TableDatabase.Tuning;
            var grid = new FarmGrid(t);

            // ① 开局形状 = gridSize 列 × initialRows 行 ⇒ 6×3 = 18
            Assert.AreEqual(t.farm.gridSize, grid.Columns, "列数 = farm.gridSize");
            Assert.AreEqual(t.farm.initialRows, grid.Rows, "开局行数 = farm.initialRows");
            Assert.AreEqual(18, grid.UnlockedCount, "开局耕地 = 6×3 = 18 格");
            Assert.AreEqual(36, grid.Capacity, "满格容量 = 6×6 = 36");

            // ② 索引恒为 行*列数+列（加行只追加 ⇒ 已种作物的世界坐标永不移动）
            Assert.AreEqual(0, grid.Slot(0, 0).Index);
            Assert.AreEqual(5, grid.Slot(5, 0).Index);
            Assert.AreEqual(6, grid.Slot(0, 1).Index);

            // ③ 每一行都是完整的 6 格（矩形田，不是"相邻扩展出的簇"）
            for (int r = 0; r < grid.Rows; r++)
                for (int c = 0; c < grid.Columns; c++)
                {
                    FarmPlot p = grid.Slot(c, r);
                    Assert.IsNotNull(p, $"({c},{r}) 应存在");
                    Assert.AreNotEqual(PlotState.Locked, p.State, $"({c},{r}) 应已解锁");
                    Assert.AreEqual(c, p.GridX, $"({c},{r}) 列号");
                    Assert.AreEqual(r, p.GridY, $"({c},{r}) 行号");
                }

            // ④ 存活时间加排：每 rowGrowthSeconds（300s）一排，封顶 maxRows
            grid.SyncSurvival(t.farm.rowGrowthSeconds - 1f);
            Assert.AreEqual(t.farm.initialRows, grid.Rows, "不到 5 分钟不加排");
            grid.SyncSurvival(t.farm.rowGrowthSeconds);
            Assert.AreEqual(t.farm.initialRows + 1, grid.Rows, "存活 5 分钟加一排");
            grid.SyncSurvival(1000000f);
            Assert.AreEqual(t.farm.maxRows, grid.Rows, "行数封顶 farm.maxRows");
            Assert.AreEqual(36, grid.UnlockedCount, "满格 6×6 = 36 格");

            // ⑤ 只增不减
            grid.SetRows(1);
            Assert.AreEqual(t.farm.maxRows, grid.Rows, "加排不可回退");
        }

        /// <summary>
        /// v166 需求③④：**植物独占一列** —— 列首种下 ⇒ 整列补齐；上限 = 列数（6）。
        /// </summary>
        [Test]
        public void PlantOccupiesWholeColumn_AndRowGrowthRefillsIt()
        {
            TuningDef t = TableDatabase.Tuning;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            var grid = new FarmGrid(t);

            grid.Slot(0, 0).BeginPlant(t.farm, radish, t.farm.waterMaxDefault, radish.WaterDrain);
            for (int r = 0; r < grid.Rows; r++)
                Assert.AreNotEqual(PlotState.Empty, grid.Slot(0, r).State, $"列 0 第 {r} 行应已自动补齐");
            Assert.AreEqual(PlotState.Empty, grid.Slot(1, 0).State, "其它列不受影响");
            Assert.AreEqual(1, grid.PlantCount, "植物株数 = 1");
            Assert.AreEqual(1, grid.FirstFreeColumn(), "第一条空列 = 1");

            // 只有列首登记驱动种子（这是 RunSession "1 种子 : 1 地块" 契约的前提）
            Assert.AreEqual(radish.Id, grid.Slot(0, 0).SeedId, "列首持有 SeedId");
            Assert.IsNull(grid.Slot(0, 1).SeedId, "非列首不得持有 SeedId");
            Assert.AreEqual(radish.Id, grid.Slot(0, 1).CropSeedId, "但每格都有作物身份");

            // 补满 6 列 ⇒ 第 7 株没有位置
            for (int c = 1; c < grid.Columns; c++)
                grid.Slot(c, 0).BeginPlant(t.farm, radish, t.farm.waterMaxDefault, radish.WaterDrain);
            Assert.AreEqual(-1, grid.FirstFreeColumn(), "6 列种满后没有空列");
            Assert.AreEqual(grid.Columns, grid.PlantCount, "株数上限 = 列数");

            // 加一排 ⇒ 每株各补 1 个
            grid.SyncSurvival(t.farm.rowGrowthSeconds);
            Assert.AreEqual(4, grid.Rows);
            for (int c = 0; c < grid.Columns; c++)
                Assert.AreNotEqual(PlotState.Empty, grid.Slot(c, 3).State, $"列 {c} 的新排应自动补齐");
            Assert.AreEqual(radish.Id, grid.Slot(0, 3).CropSeedId, "新排是同品种");

            // 整列拆除：铲掉一格的植物 ⇒ 同列不留孤儿作物
            grid.Slot(0, 0).RemoveGrowing();
            Assert.AreEqual(PlotState.Empty, grid.Slot(0, 3).State, "同列其它格一并清空");
            Assert.AreEqual(grid.Columns - 1, grid.PlantCount);
        }

        [Test]
        public void Pcg32_IsDeterministicAndSeedSensitive()
        {
            var a = new Pcg32(1234UL, 1);
            var b = new Pcg32(1234UL, 1);
            var c = new Pcg32(1235UL, 1);
            bool allSame = true, allDifferent = true;
            for (int i = 0; i < 64; i++)
            {
                uint x = a.NextUInt();
                uint y = b.NextUInt();
                uint z = c.NextUInt();
                if (x != y) allSame = false;
                if (x == z) allDifferent = false;
            }
            Assert.IsTrue(allSame, "同种子必须产出同序列");
            Assert.IsTrue(allDifferent, "不同种子必须产出不同序列");

            var fork = new Pcg32(1234UL, 1);
            Pcg32 derived = fork.Fork(99);
            uint before = fork.NextUInt();
            derived.NextUInt();
            Assert.AreEqual(before, new Pcg32(1234UL, 1).NextUInt(), "派生流不得污染主逻辑流");
        }

        [Test]
        public void PlayerLevelUp_IncreasesHpButNotPlots()
        {
            var progress = new PlayerProgress(TableDatabase.Tuning);
            int plotsBefore = progress.MaxPlots;
            bool leveled = progress.AddExp(1000f, out int levels);
            Assert.IsTrue(leveled);
            Assert.Greater(levels, 0);
            Assert.Greater(progress.Stats.Get(StatId.MaxHp), TableDatabase.Tuning.player.maxHp);
            // v166：田地成长只由**存活时间**驱动（FarmGrid.SyncSurvival），升级不再给耕地
            Assert.AreEqual(plotsBefore, progress.MaxPlots, "升级不得改变可种植株数");
        }
    }

    public class TableTests
    {
        [OneTimeSetUp]
        public void LoadTables()
        {
            TableDatabase.Load();
        }

        /// <summary>
        /// 与运行时 SelfCheck 共用同一套期望值：单测与无人值守自检必须给出相同结论。
        /// </summary>
        [Test]
        public void SelfCheck_AllGreen()
        {
            var results = SelfCheck.RunAll();
            foreach (CheckResult r in results)
                Assert.IsTrue(r.Passed, r.ToString());
        }

        [Test]
        public void Tables_LoadAndValidate()
        {
            Assert.AreEqual(16, TableDatabase.Seeds.Length, "策划案定稿 16 个种子");
            Assert.AreEqual(0, TableDatabase.Mutations.Length, "变异词条系统已删除，表内必须为空");
            Assert.AreEqual(5, TableDatabase.Enemies.Length, "五类敌人（含小猪）");
            // ★ v165：不再是"必须正好 20 条" ★
            // 那条断言把"表只有 20 条"钉成了正确状态，而"第 21 分钟越界取到 null"
            // 正是最严重的那个死锁（不再刷怪 + Shop↔Combat 乒乓）✗
            // 改成验**参数表的本分**：至少 1 条 ✓（"越界不返回 null" 见下面的单测）
            Assert.GreaterOrEqual(TableDatabase.Waves.Length, 1,
                "waves.json 是【参数表】而不是关卡数：计时制是无限模式，条目数不设上限也不设下限");
            Assert.AreEqual(TuningCount, TableDatabase.Items.Length);
            Assert.AreEqual(0, TableDatabase.LastError.Length);
        }

        // v45：删除无效道具「水囊」后为 15 条
        private const int TuningCount = 15;

        [Test]
        public void EveryGrade_HasSeeds()
        {
            for (int g = 0; g <= 3; g++)
                Assert.GreaterOrEqual(TableDatabase.SeedsOfGrade((SeedGrade)g).Length, 3,
                    $"{(SeedGrade)g} 级至少 3 个种子");
        }

        [Test]
        public void Waves_ReferenceExistingEnemiesAndBossTwice()
        {
            int bossWaves = 0;
            foreach (WaveDef w in TableDatabase.Waves)
            {
                if (w.IsBoss) bossWaves++;
                foreach (WaveBatchDef b in w.Batches)
                    Assert.DoesNotThrow(() => TableDatabase.GetEnemy(b.EnemyId));
            }
            // v165：文案更正 —— 那两条 isBoss 在**表里**是第 10 / 20 条
            // （旧注释写的"第 5、10 波"从来就对不上表）。
            // 注意：它们现在**只表示"这个条目标了 BOSS"**，不再驱动刷怪 ——
            // BOSS 的唯一入口是 BossSchedule（10/18/24/28/30/32… 分钟）✓
            Assert.AreEqual(2, bossWaves, "表内标记 BOSS 的条目 2 条（第 10 / 20 条）");
        }

        /// <summary>
        /// v165：**越界必须返回最后一条，而不是 null**。
        ///
        /// 这是本次修掉的那个 P0 的直接回归防线：
        /// `GetWave` 返回 null ⇒ `WaveDirector.Tick` 首行 return ⇒ 第 21 分钟起整局不再刷怪，
        /// 且 `IsComplete` 当帧成立 ⇒ Shop↔Combat 每帧乒乓 ✗
        /// 计时制的成长曲线本身是公式化的，表只是"前 N 分钟的参数"，
        /// 所以越界复用最后一条在数值上是【连续的】✓
        /// </summary>
        [Test]
        public void GetWave_BeyondTable_ReusesLastInsteadOfNull()
        {
            Assert.GreaterOrEqual(TableDatabase.Waves.Length, 1, "参数表不能为空");
            WaveDef last = TableDatabase.Waves[TableDatabase.Waves.Length - 1];
            int[] probes = { last.Index + 1, last.Index + 30, last.Index + 180, last.Index + 1980 };
            foreach (int probe in probes)
            {
                WaveDef got = TableDatabase.GetWave(probe);
                Assert.IsNotNull(got, $"第 {probe} 分钟越界后不得返回 null（否则整局停止刷怪）");
                Assert.AreEqual(last.Index, got.Index, $"第 {probe} 分钟应复用 index {last.Index} 的参数");
            }
        }

        [Test]
        public void BossStats_ScaleWithBossIndex()
        {
            EnemyDef boss = TableDatabase.GetEnemy("boss");
            // v47：血量 = baseHp × (1 + hpGrowth×(expTerm^(1/3) − 1)) × (1 + bossHpGrowth×(index − 1))
            //      expTerm(10) = 64 → 立方根 4 → 360 × 1.15 = 414；第 20 波再 × 1.10 = 455.4
            Assert.AreEqual(414f, boss.HpForWave(10, 1), 1e-2f);
            Assert.AreEqual(455.4f, boss.HpForWave(20, 2), 1e-2f);
            Assert.IsTrue(boss.BaseHp >= 300f, "v47：BOSS 基础血量已提高");
        }
    }
}
