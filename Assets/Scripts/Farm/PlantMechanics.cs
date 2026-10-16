using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ **植物机制的解释器 —— 唯一的一处** ★
    ///
    /// 新标准对植物的要求是：
    ///   低品质（普通/一般）—— 纯数值差异，**没有机制**
    ///   中品质（稀有/史诗）—— 小机制 + 微高数值
    ///   高品质（传奇）      —— 大机制 + 高数值
    /// ★ 而所有机制只能作用在三个位置上 ★
    ///   · **成长阶段**（更快/更慢、蓄水变化、相邻影响）
    ///   · **收获阶段**（收益变化、额外返还、连带收获）
    ///   · **缺水时**（缺水惩罚减轻、缺水反而有收益）
    ///   ✗ 不允许作用于战斗 —— 植物在新玩法里**不参战**（v165 起），
    ///     所以 `Taunt/Thorns/Chain/HealOnKill` 那些旧机制**已被删除**（v172）✓
    ///     ★ 这条记录**保留不删** ★ —— 它是"旧机制去哪了"的唯一交代 ✓
    ///     v172 删除的九条：`Taunt` / `Thorns` / `Chain` / `HealOnKill` / `DeathExplode` /
    ///                      `HealAura` / `Magnet` / `SlowField` / `NightBoost`
    ///     ⇒ 判定口（`PlantAlly.HasMechanic`）与服务点（`World.DealDamage` 等）**都删了** ✓
    ///     ⇒ 现在工程里**只剩这一个机制解释器**：机制名必须出现在下面 `All` 里，否则 SelfCheck 判红 ✓
    ///
    /// ────────────────────────────────────────────────────────────────────────
    /// ★ 为什么必须单独成类 ★
    ///
    /// 做这个任务最容易犯的错，是把 16 个机制名写进 `seeds.json`，
    /// 然后只有一两个被读 —— 剩下的一整片数据"看着活着、实际死的" ✗
    /// 本工程已经吃过一次同款亏：铁根巨像的 `WaterSave` 曾经被实现成
    /// `TickGrowth2(dt * 2)` ⇒ 一个叫"省水"的机制**在加重水耗** ✗
    ///
    /// ⇒ 所以：**机制名 → 效果 → 唯一的消费点**三者集中在下面这张表里，
    ///   每个机制都注明它在哪里被读 ✓ 没有一个机制是"只在表里存在"的 ✓
    ///
    /// | 机制              | 作用阶段   | 效果                                   | 消费点（真接线处）                        |
    /// |-------------------|-----------|----------------------------------------|------------------------------------------|
    /// | `Haste`           | 成长      | 该列成熟速度 ×1.25                     | `World.Simulate` → `TickGrowth2` 的 dt    |
    /// | `NeighborBoost`   | 成长      | 左右相邻两列成熟速度 ×1.12（不相乘）   | 同上（读 `Farm` 的相邻列首）              |
    /// | `WaterSave`       | 成长/缺水 | 该列漏水时钟 ×0.5                      | `World.Simulate` → `TickGrowth2` 的水耗系数 |
    /// | `ElderGrowth`     | 成长      | 存活每满 60s，该列生长速度 +4%（≤+60%）| 同上（读 `World.Time`）                   |
    /// | `ThirstTolerant`  | 缺水      | 缺水时**仍以 40% 速度生长**（不停长）  | `FarmPlot.TickGrowth2` 的 thirstGrowMult  |
    /// | `DewDrop`         | 收获      | 每次收获**返还 12 点蓄水**             | `World.HarvestPlot`（在 `plot.Harvest` 前）|
    /// | `RichHarvest`     | 收获      | 该格收获金币 ×1.30                     | `World.HarvestPlot` 的金币结算            |
    /// | `ChainHarvest`    | 收获      | 收割一格 ⇒ **同列其它已成熟格一并收获**| `World.HarvestPlot` 的连锁分支            |
    /// | `ThirstBloom`     | 缺水+收获 | 该格**缺水时**收获金币 ×1.60           | `World.HarvestPlot`（读该格缺水状态）     |
    ///
    /// ★ 参数写在这里，而不是写进 `seeds.json` ★
    /// 表里只写机制**名字** ✓ 理由：如果每个机制各要一个 `mechanicValue`，
    /// 那么"表里填了 0"与"根本没填"就会长得一样，而前者会**静默地把机制变成空操作** ✗
    /// 参数在这里 ⇒ 不存在"填错/漏填"这种状态 ✓ 要调平衡就改这里的常量，一处生效 ✓
    /// </summary>
    public static class PlantMechanics
    {
        // ── 机制标识（与 seeds.json 的 "mechanic" 字段**逐字**对应）──
        public const string None = "None";
        public const string Haste = "Haste";
        public const string NeighborBoost = "NeighborBoost";
        public const string WaterSave = "WaterSave";
        public const string ElderGrowth = "ElderGrowth";
        public const string ThirstTolerant = "ThirstTolerant";
        public const string DewDrop = "DewDrop";
        public const string RichHarvest = "RichHarvest";
        public const string ChainHarvest = "ChainHarvest";
        public const string ThirstBloom = "ThirstBloom";

        /// <summary>已登记的全部植物机制（自检用它验"表里的 mechanic 都有人解释"）。</summary>
        public static readonly string[] All =
        {
            Haste, NeighborBoost, WaterSave, ElderGrowth,
            ThirstTolerant, DewDrop, RichHarvest, ChainHarvest, ThirstBloom
        };

        // ── 参数（唯一来源；改这里就是调平衡）──
        public const float HasteMult = 1.25f;
        public const float NeighborMult = 1.12f;
        public const float WaterSaveScale = 0.5f;
        public const float ElderStepSeconds = 60f;
        public const float ElderStepBonus = 0.04f;
        public const float ElderBonusCap = 0.60f;
        public const float ThirstGrowMult = 0.40f;
        public const int DewDropWater = 12;
        public const float RichHarvestMult = 1.30f;
        public const float ThirstBloomMult = 1.60f;

        /// <summary>某个植物定义是不是带这条机制（大小写不敏感，空值安全）。</summary>
        public static bool Has(SeedDef def, string mechanic)
            => def != null && !string.IsNullOrEmpty(def.Mechanic)
               && string.Equals(def.Mechanic, mechanic, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 这个名字**有没有解释器**（`None` 也算已知 —— 它的解释是"没有机制"）✓
        ///
        /// 用途：`TableDatabase` / `SelfCheck` 用它挡掉"表里写了一个没人读的机制名" ✗
        /// 那种错的症状是"这株植物看起来有机制、实际什么都没发生"，
        /// 而且**不报错、不崩溃** —— 正是本工程反复吃亏的那一类 ✓
        /// </summary>
        public static bool IsKnown(string mechanic)
        {
            if (string.IsNullOrEmpty(mechanic)) return false;
            if (string.Equals(mechanic, None, System.StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i], mechanic, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ─────────────────────────────── 成长阶段

        /// <summary>
        /// **本株自身的生长速度倍率**（不含相邻影响）。
        /// `survivedSeconds` 传 `World.Time`，只有 `ElderGrowth` 用它。
        /// </summary>
        public static float GrowSpeedMult(SeedDef def, float survivedSeconds)
        {
            if (def == null) return 1f;
            float m = 1f;
            if (Has(def, Haste)) m *= HasteMult;
            if (Has(def, ElderGrowth))
            {
                float steps = Mathf.Floor(Mathf.Max(0f, survivedSeconds) / ElderStepSeconds);
                m *= 1f + Mathf.Min(ElderBonusCap, steps * ElderStepBonus);
            }
            return m;
        }

        /// <summary>
        /// **相邻影响**：左右两列里只要有 `NeighborBoost` 的植物，本列跟着变快。
        /// 左右**同时**有也**只算一次**（不相乘）—— 否则把两株月光树夹着一株普通植物
        /// 就能叠出 1.25 倍，那是"相邻"变成了"堆叠"，不是设计意图 ✓
        /// </summary>
        public static float NeighborGrowSpeedMult(FarmGrid farm, FarmPlot plot)
        {
            if (farm == null || plot == null) return 1f;
            int c = plot.Column;
            if (Has(farm.ColumnHead(c - 1)?.CropDef, NeighborBoost)) return NeighborMult;
            if (Has(farm.ColumnHead(c + 1)?.CropDef, NeighborBoost)) return NeighborMult;
            return 1f;
        }

        /// <summary>**漏水时钟**的缩放：`WaterSave` 真的省水（0.5 = 每 5 秒扣的那次慢一倍）。</summary>
        public static float WaterCostScale(SeedDef def)
            => Has(def, WaterSave) ? WaterSaveScale : 1f;

        // ─────────────────────────────── 缺水

        /// <summary>
        /// 缺水（或耗尽未补满）时的**生长速度**。
        /// 返回 0 = 完全停长（默认行为，绝大多数植物如此）✓
        /// 返回 0.4 = 仍以 40% 速度生长（`ThirstTolerant`）✓
        /// </summary>
        public static float ThirstyGrowMult(SeedDef def)
            => Has(def, ThirstTolerant) ? ThirstGrowMult : 0f;

        // ─────────────────────────────── 收获

        /// <summary>该格收获时的**金币倍率**（`RichHarvest` 常驻、`ThirstBloom` 只在缺水时生效）。</summary>
        public static float HarvestGoldMult(SeedDef def, FarmPlot plot)
        {
            if (def == null) return 1f;
            float m = 1f;
            if (Has(def, RichHarvest)) m *= RichHarvestMult;
            // ★ 缺水反而有收益 ★ —— 用户标准里明确列出的那一种
            if (Has(def, ThirstBloom) && plot != null && plot.GrowthPaused) m *= ThirstBloomMult;
            return m;
        }

        /// <summary>收获时额外返还的蓄水（`DewDrop`）；0 = 不返还。</summary>
        public static int HarvestDew(SeedDef def) => Has(def, DewDrop) ? DewDropWater : 0;

        /// <summary>收割一格时是否**连带**收获同列其它已成熟的格（`ChainHarvest`）。</summary>
        public static bool ChainsHarvest(SeedDef def) => Has(def, ChainHarvest);

        // ─────────────────────────────── 展示

        /// <summary>机制的中文说明（商店卡片 / 详情 / 自检报告共用一份文案）。</summary>
        public static string Describe(string mechanic)
        {
            if (string.IsNullOrEmpty(mechanic)) return "";
            if (string.Equals(mechanic, None, System.StringComparison.OrdinalIgnoreCase)) return "（无机制：纯数值差异）";
            if (string.Equals(mechanic, Haste, System.StringComparison.OrdinalIgnoreCase)) return $"成长加速：成熟速度 ×{HasteMult:0.##}";
            if (string.Equals(mechanic, NeighborBoost, System.StringComparison.OrdinalIgnoreCase)) return $"相邻增益：左右两列成熟速度 ×{NeighborMult:0.##}";
            if (string.Equals(mechanic, WaterSave, System.StringComparison.OrdinalIgnoreCase)) return $"省水：该列漏水减半（×{WaterSaveScale:0.##}）";
            if (string.Equals(mechanic, ElderGrowth, System.StringComparison.OrdinalIgnoreCase)) return $"古木生长：存活每 {ElderStepSeconds:0} 秒该列生长 +{ElderStepBonus * 100f:0}%（上限 +{ElderBonusCap * 100f:0}%）";
            if (string.Equals(mechanic, ThirstTolerant, System.StringComparison.OrdinalIgnoreCase)) return $"耐旱：缺水时仍以 {ThirstGrowMult * 100f:0}% 速度生长";
            if (string.Equals(mechanic, DewDrop, System.StringComparison.OrdinalIgnoreCase)) return $"凝露：每次收获返还 {DewDropWater} 点蓄水";
            if (string.Equals(mechanic, RichHarvest, System.StringComparison.OrdinalIgnoreCase)) return $"丰收：该格收获金币 ×{RichHarvestMult:0.##}";
            if (string.Equals(mechanic, ChainHarvest, System.StringComparison.OrdinalIgnoreCase)) return "连锁收获：收割一格 ⇒ 同列其它已成熟格一并收获";
            if (string.Equals(mechanic, ThirstBloom, System.StringComparison.OrdinalIgnoreCase)) return $"焦渴绽放：缺水时该格收获金币 ×{ThirstBloomMult:0.##}";
            return "";
        }
    }
}
