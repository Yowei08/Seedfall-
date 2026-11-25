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
    /// | `ThirstBloom`     | 缺水+收获 | 该格水分**低于上限 50%** 时收获金币 ×1.60 | `World.HarvestPlot`（读该格 `Water01`）  |
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

        // ════════════════════════════════════════════════════════════════════
        // ★★ v197：**六种"机制植物"的机制**（用户本轮需求，品质 ≥ 史诗 / 传奇）★★
        //
        // 与本文件上半段那 9 条的分工相同：名字在这里登记、参数在这里、消费点在注释里。
        // ★ 但这 6 条与那 9 条有一处**本质区别**：
        //   那 9 条只作用于"成长 / 收获 / 缺水"三处（植物经济的内部循环）；
        //   这 6 条可以作用于**玩家**（临时增益、闪避、幸运）与**场景**（天气）——
        //   因为用户这一轮要的正是"机制植物"：它给的不是更多的金币，而是一条**规则** ✓
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 机制 1：**收割此植物 ⇒ 3 秒内攻击倍率 +3%（每级 +1%），最多 12 层，每次叠层刷新时长**。
        /// 消费点：`RunSession.HarvestPlot` 叠层 → `PlayerBuffs` 记账 →
        ///         `ProgMechanics.SyncRuntimeStats` 写成 `StatId.AtkMult` 的乘法修正 ✓
        /// </summary>
        public const string HarvestAtkMult = "HarvestAtkMult";
        /// <summary>机制 2：同上，作用于**攻击速度**（`StatId.AtkSpeed`）。</summary>
        public const string HarvestAtkSpeed = "HarvestAtkSpeed";
        /// <summary>
        /// 机制 3：**站在这株植物所在的那块田地上 ⇒ 闪避 +20%（每级 +5%）**。
        /// 消费点：`ProgMechanics.PlantPresenceDodge`（每帧读**玩家位置**落在哪几块植株地块里）
        ///         → `StatId.Dodge` 的加法修正 ✓
        ///
        /// ★ 判据**不是**"站着不动"（本轮用户明确纠正）★
        ///   用户原话："这种植物修改的效果说明和实际效果为**站在它的田地上**生效，而不是站着"
        ///   ⇒ 只看位置，不看速度：跑着穿过、边走边打都能拿到 ✓
        ///   （名字里那个 `Standing` 从此只指"站在地里"，不指"停下脚步"——
        ///     常量名不改是因为 `seeds.json` 的 `mechanic` 字段逐字引用它 ✓）
        /// </summary>
        public const string StandingDodge = "StandingDodge";
        /// <summary>
        /// 机制 4：**在场时每分钟整点 10%（每级 +5%）触发大晴天 30 秒 ⇒ 全场生长速度 ×1.10**。
        /// 消费点：`ProgMechanics.RollWeather`（掷骰）/ `World.Simulate`（`ProgMechanics.GrowMult`）✓
        /// </summary>
        public const string SunnyBloom = "SunnyBloom";
        /// <summary>
        /// 机制 5：**在场时每分钟整点 10%（每级 +5%）触发雨天 30 秒 ⇒ 补满所有耕地水分**。
        /// 消费点：`ProgMechanics.RollWeather` + `ProgMechanics.RefillAllPlots` ✓
        /// </summary>
        public const string RainCloud = "RainCloud";
        /// <summary>
        /// 机制 6：**自带 30% 幸运（每级 +5%）**。
        /// 消费点：`ProgMechanics.PlantPresenceLuck` → `StatId.Luck` 加法修正
        ///         → `World.RollHarvestLuck`（收割 ×2 的掷骰读的就是 Luck）✓
        /// </summary>
        public const string FarmLuck = "FarmLuck";

        // ════════════════════════════════════════════════════════════════════
        // ★★ v253：**表尾 12 株里的 6 条新机制**（用户本轮需求）★★
        //
        // 与本文件已有机制的纪律完全一致：名字登记在这里、参数在常量里、
        // **消费点写在各自的注释里**（下面每条都点名"谁读它"）。
        //
        // ★ 这 6 条与前面那 15 条的共同点：**没有一条用随机数** ✓
        //   判据因此可以被探针/自检**复算**：给定同一组输入（水分比例 / 时刻 / 生命比例 /
        //   收获间隔 / 耕地块数 / 击杀时刻），返回值逐位相同 ✓
        //
        // ★ 分工（"谁在哪一层被读"）★
        //   · `FullWaterGold`  —— 只看**这一格**的水分 ⇒ 进 `HarvestGoldMult(def, plot)`（纯函数）
        //   · `PlotCountGold` / `NightGold` —— 要看**整块农场 / 世界时刻** ⇒
        //     进 `FarmHarvestGoldMult(def, farm, worldTime)`（也是纯函数，只是参数更多）
        //   · `LowHpGrow`      —— 看**玩家生命比例** ⇒ 进 `LowHpGrowMult(def, hp01)`（纯函数）
        //   · `StreakHarvest`  —— 要记"这一格上次什么时候收的" ⇒ 有状态，
        //     **账本在 `ProgMechanics`**（`PlantStreakGoldMult`），这里只放算式
        //   · `KillRush`       —— 要记"上一次击杀是几秒前" ⇒ 有状态，
        //     计时在 `ProgMechanics`，效果走 `StatId.AtkMult`（见 `SyncRuntimeStats`）
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 新机制 1：**满水厚赏** —— 该格水分不低于上限的 80% 时，收获金币 ×1.25。
        /// 消费点：`HarvestGoldMult`（读 `plot.Water01`，与 `ThirstBloom` 同一处、方向相反）✓
        /// </summary>
        public const string FullWaterGold = "FullWaterGold";
        /// <summary>
        /// 新机制 2：**连收递增** —— 同一格**准时**连收（两次收获间隔 ≤ 成熟时间 ×1.5）
        /// 每层 +6%，上限 +36%；间隔超出即从 1 层重算。
        /// 消费点：`ProgMechanics.PlantStreakGoldMult`（账本）→ 本文件的 `StreakGoldMult(层数)`（算式）
        ///         → `World.HarvestPlot` 的金币乘区 ✓
        /// </summary>
        public const string StreakHarvest = "StreakHarvest";
        /// <summary>
        /// 新机制 3：**夜露** —— 夜间（太阳高度 `DayNightCycle.H01 ≤ 0.35`）收获金币 ×1.20。
        /// 消费点：`FarmHarvestGoldMult`（读 `DayNightCycle.H01(worldTime)`）✓
        /// ★ 用**昼夜循环那条纯函数**判夜，而不是自己写一个"第几分钟算夜里"——
        ///   两份时间口径必然分叉（玩家看到的暗/亮是 `DayNightCycle` 那张关键帧表）✓
        /// </summary>
        public const string NightGold = "NightGold";
        /// <summary>
        /// 新机制 4：**猎杀亢奋** —— 击杀任意敌人后 4 秒内攻击倍率 ×1.12。
        /// 消费点：`ProgMechanics.OnEnemyKilled`（计时）→ `ProgMechanics.SyncRuntimeStats`
        ///         写成 `StatId.AtkMult` 的**乘法**修正（来源 `prog:killrush`）✓
        /// </summary>
        public const string KillRush = "KillRush";
        /// <summary>
        /// 新机制 5：**田园成片** —— 场上每有一块**已解锁**耕地 ⇒ 收获金币 +2%，上限 +20%。
        /// 消费点：`FarmHarvestGoldMult`（数 `farm.Slots` 里 `Unlocked` 的个数）✓
        /// </summary>
        public const string PlotCountGold = "PlotCountGold";
        /// <summary>
        /// 新机制 6：**血勇** —— 玩家生命比例低于 40% 时，该株生长速度 ×1.30。
        /// 消费点：`LowHpGrowMult`（读 `HealthPool.HpPct`）→ `World.Simulate` 的成长乘区 ✓
        /// </summary>
        public const string LowHpGrow = "LowHpGrow";

        /// <summary>已登记的全部植物机制（自检用它验"表里的 mechanic 都有人解释"）。</summary>
        public static readonly string[] All =
        {
            Haste, NeighborBoost, WaterSave, ElderGrowth,
            ThirstTolerant, DewDrop, RichHarvest, ChainHarvest, ThirstBloom,
            HarvestAtkMult, HarvestAtkSpeed, StandingDodge, SunnyBloom, RainCloud, FarmLuck,
            FullWaterGold, StreakHarvest, NightGold, KillRush, PlotCountGold, LowHpGrow
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

        /// <summary>
        /// ★ v219：`ThirstBloom`（焦渴绽放）的**触发阈值 = 水分低于上限的 50%** ★
        ///
        /// 用户原话："有个缺水获得金币加成的植物，**不是缺水，而是水分低于 50**"。
        ///
        /// ── 为什么是**比例**，而不是"水分 &lt; 50"这个数 ✗ ──
        ///   `FarmPlot.Water` 是**整数蓄水量**，量程 **0..WaterMax**（不是 0..1，也不是恒定的 0..100）：
        ///   上限 = `SeedSlot.WaterMaxFor(品种, 等级)` = 100／140／180（1/2/3 级，每级 +40），
        ///   自检里还有 `BeginPlant(…, 1000, …)` 这种临时大水箱 ⇒ 写死 50 会变成
        ///   "1 级 50%、3 级只有 28%" 的假阈值 ✗
        ///   ⇒ 唯一正确的表达是 `Water / WaterMax`（`FarmPlot.Water01`）与 0.5 比 ✓
        ///
        /// ── 为什么参数放代码里、不放 `seeds.json` ──
        ///   与本文件 47~50 行的既有规矩一致（表里只写机制**名字**）：
        ///   若每株各填一个 `mechanicValue`，"填 0" 与 "没填" 会长得一样，
        ///   而前者会**静默地把机制变成空操作** ✗ ⇒ 参数只在常量里，改平衡改一处 ✓
        ///
        /// ── 边界 ──
        ///   严格小于：水分**正好** 50%（如 50/100、70/140、90/180）**不算**焦渴、不给加成 ✓
        ///   "低于 50%" 按用户原话取严格小于，与"≥ 一半就算有水"的直觉一致 ✓
        /// </summary>
        public const float ThirstGoldRatio = 0.5f;

        // ── v197 新机制的参数 ──

        /// <summary>机制 1 / 2：1 级时每层给多少百分点；每升 1 级再 +1 个百分点（用户原话）。</summary>
        public const float HarvestBuffBasePct = 3f;
        /// <summary>机制 1 / 2：每级递增的百分点。</summary>
        public const float HarvestBuffPerLevelPct = 1f;
        /// <summary>机制 1 / 2：一次增益持续几秒（"3 秒内…每次叠层刷新时长"）。</summary>
        public const float HarvestBuffSeconds = 3f;
        /// <summary>机制 1 / 2：层数上限（用户原话"最多 12 层"）。与 `PlayerBuffs.MaxStacksOf` 同值 ✓</summary>
        public const int HarvestBuffMaxStacks = 12;

        /// <summary>机制 3：1 级时站田闪避加成（0.20 = +20%）。数值本身本轮**未改**。</summary>
        public const float StandingDodgeBase = 0.20f;
        /// <summary>机制 3：每级递增（0.05 = +5%）。</summary>
        public const float StandingDodgePerLevel = 0.05f;

        /// <summary>
        /// 机制 4 / 5：1 级时每分钟触发的概率（0.25 = 25%）。
        ///
        /// ★ v252：**0.10 → 0.25** ★ 用户原话「设计的晴天、雨天效果种子异常不刷新，调整正常刷新」。
        ///   10% 的实机含义：一株 1 级天气植物**平均 10 分钟才出一次** 30 秒的天气 ——
        ///   而一局的前 10 分钟正好是玩家最需要看到"这株植物真的有用"的窗口 ⇒
        ///   玩家看到的是"种下去什么也没发生"，与"坏了"无法区分 ✗
        ///   25% ⇒ 平均 4 分钟一次、2 级 33%、3 级 41% ⇒ 一局里一定看得见 ✓
        ///   判据（探针/自检可复算）：`PlantMechanics.WeatherChance(1) == 0.25` ✓
        /// </summary>
        public const float WeatherChanceBase = 0.25f;
        /// <summary>机制 4 / 5：每级递增的概率（0.08 = +8 个百分点/级 ⇒ 1/2/3 级 = 25% / 33% / 41%）。</summary>
        public const float WeatherChancePerLevel = 0.08f;
        /// <summary>
        /// 机制 4 / 5：天气持续秒数（0.75 分钟 = 45 秒）。
        ///
        /// ★ v252：**30 → 45** ★ 与上面那条概率一起改的同一个需求（"调整正常刷新"）。
        ///   30 秒占一分钟的一半：天气刚亮起来、玩家还没走到田里就没了 ⇒ 读不出效果。
        ///   45 秒（0.75 分钟）⇒ 触发一次必然跨过玩家的"浇水 + 收割"一轮动作 ✓
        /// </summary>
        public const float WeatherSeconds = 45f;
        /// <summary>机制 4 大晴天：全场生长速度倍率（"所有植物成熟 +10%"）。</summary>
        public const float SunnyGrowMult = 1.10f;

        /// <summary>机制 6：1 级时的幸运加成（0.30 = +30%）。</summary>
        public const float FarmLuckBase = 0.30f;
        /// <summary>机制 6：每级递增（0.05 = +5%）。</summary>
        public const float FarmLuckPerLevel = 0.05f;

        // ── v253 六条新机制的参数（同样只在这里；表里只写机制名）──

        /// <summary>
        /// 新机制 1 `FullWaterGold`：触发阈值 —— 水分**不低于**上限的 80% 才给（`≥`，与焦渴的 `<` 互补）。
        /// 取 0.80 而不是 1.00：满水箱 `WaterMax` 是 100／140／180（按等级），
        /// 而一次浇水（`waterPerHit`）通常补不满 ⇒ 要求"一滴不少"会让它在 2/3 级永远不亮 ✓
        /// </summary>
        public const float FullWaterRatio = 0.80f;
        /// <summary>新机制 1：满水时的金币倍率。</summary>
        public const float FullWaterGoldMult = 1.25f;

        /// <summary>新机制 2 `StreakHarvest`：每层（每次准时连收）的百分点。</summary>
        public const float StreakStepPct = 6f;
        /// <summary>新机制 2：层数上限（6 层 ⇒ +36%）。</summary>
        public const int StreakMaxStacks = 6;
        /// <summary>
        /// 新机制 2：**"还算连续"的时间窗** = 该品种成熟时间 × 本系数。
        /// 1.5 倍 ⇒ 收完立刻等下熟、熟了就收，能一直续；而漏收半熟以上就断 ✓
        /// </summary>
        public const float StreakWindowMult = 1.5f;

        /// <summary>新机制 3 `NightGold`：判夜阈值 —— 太阳高度 ≤ 本值算夜间（0 = 子夜，1 = 正午）。</summary>
        public const float NightH01Max = 0.35f;
        /// <summary>新机制 3：夜间的金币倍率。</summary>
        public const float NightGoldMult = 1.20f;

        /// <summary>新机制 4 `KillRush`：击杀后增益持续秒数。</summary>
        public const float KillRushSeconds = 4f;
        /// <summary>新机制 4：增益期间的攻击倍率（`StatId.AtkMult` 的乘法修正）。</summary>
        public const float KillRushAtkMult = 1.12f;

        /// <summary>新机制 5 `PlotCountGold`：每块已解锁耕地的百分点。</summary>
        public const float PlotCountStepPct = 2f;
        /// <summary>新机制 5：上限百分点（20% ⇒ 10 块封顶）。</summary>
        public const float PlotCountCapPct = 20f;

        /// <summary>新机制 6 `LowHpGrow`：判"血勇"的生命比例上界（严格小于）。</summary>
        public const float LowHpRatio = 0.40f;
        /// <summary>新机制 6：低血时的生长倍率。</summary>
        public const float LowHpGrowMultValue = 1.30f;

        // ════════════════════════════════════════════════════════════════════
        // ★★ 机制补偿（`SEED-TIER-RATE` 的公平口径）★★
        //
        // 用户本轮明确要求："机制植物的**数值低于**同品质无机制植物" ✓
        // 而 `SEED-TIER-RATE` 判的是"同档内每秒总价值差 ≤ 2%" —— 两者**表面冲突**，
        // 实质是那条判据漏了一个变量：**机制本身也是有价值的** ✓
        //
        // ⇒ 补上"机制补偿率"：把每株的每秒价值乘以 `1 + 机制补偿`，再比同档差 ≤ 2%。
        //   判据的**形式没变**（还是"档内不比别人强"），只是把账算全了：
        //     无机制植物：值 × 1.00
        //     有机制植物：值 × 1.06 —— 即"它的数值应当比同档低 6% 左右"，
        //                 低得更多 = 这条机制不值那个差价（玩家不会种），
        //                 低得更少 = 这株在同档里**支配**别人（玩家只会种它）✓
        // ★ 为什么是 6% 而不是 20% ★
        //   6 条机制的强度差得很远（12 层 ×3% 的增益与 +30% 幸运不是一个量级），
        //   但**档位预算只差 2%** ⇒ 补偿率必须是"档内可比"的同一个数，
        //   不能一条机制一个补偿（那等于让每个机制自定价，判据就没了）✓
        //   6% 取在"看得出来（>2% 容差）、又不至于让机制植物在数值上明显吃亏"的那一档 ✓
        // ════════════════════════════════════════════════════════════════════
        public const float MechanicPayback = 0.06f;

        // ════════════════════════════════════════════════════════════════════
        // ★★ v200：**种子档位预算 + 成熟时间补偿 f(t)** —— 平衡的唯一算式 ★★
        //
        // 用户原话（v200）：
        //   "规定同品质的种子，金币和经验值获取量在相同时间内相同，若成熟时间长，
        //    则总和在此基础上略微增加（时间越长，增加越多），若有机制，则获取量总和减少，
        //    高品质的永远比低品质获取总和高"
        //   理由："成熟时间短的种子有更多次数可能被幸运影响，所以数值总和上低于成熟时间长的，
        //          而拥有机制的为了平衡与没有机制的种子强度，总和低"
        //
        // ── 算式（三条乘起来，一处生效）──
        //
        //     (金+验)/秒 = V档 × f(t) × (有机制 ? 1−6% : 1)          …… ①
        //     f(t)       = 1 + 0.10 × log2( t / t档基准 )             …… ②
        //
        //   ① 是**目标率**；把 ① 乘回 t 就是这一株一格的**整数和**
        //     `金 + 验 ≈ round( V档 × f(t) × t × (有机制 ? 0.94 : 1) )` —— 所以"相同时间内相同"
        //     不是近似的：**同一档里 t 相同的两株，整数和必须逐位相等** ✓
        //   ② `f(t)` 单调递增、且 `f(t档基准) = 1` ⇒ "越长越增"，且"档基准那一株不加成" ✓
        //     斜率 0.10/倍：**周期每翻一倍，每秒总和 +10%**（4 倍周期 = +20%）
        //     ⇒ 刻意取小值：用户要的是"**略微**增加"，而 f 的代价见下面"为什么分档取值" ✓
        //
        // ── 为什么经验按 1 : 1 与金币相加（而不是 v197 的 +2×经验）──
        //   用户这一轮的口径是"**金币和经验值获取量……总和**"⇒ 两样**等比**相加 ✓
        //   v197 那条加权（1 金 = 2 验）守的是"档内每秒**感知价值**相等"，
        //   而它必然让"金型"与"验型"的同档植物**总和不等**（金型 6+1=7 与验型 2+4=6 差 14%）
        //   ⇒ 与用户本轮的第一条规则直接冲突 ⇒ 口径换成 1:1 ✓
        //   ⚠️ 权重变了，所以"档内不再有支配关系"要多守两条：见下面 `SeedTierBudget` 的说明 ✓
        //
        // ── 为什么 f(t) 的基准是"**本档**最快的成熟时间"，而不是全局最小值 ──
        //   因为规则的优先级是 ④ 高于 ②：**高品质永远比低品质总和高** ✓
        //   而五档的周期区间并不单调（B 48s 比 A 42.5s 长、A 比 S 37s 长）——
        //   若用**全局** f(t)，B 档（周期更长）会被 f 抬到 A 档之上 ⇒ **直接违反 ④** ✗
        //   ⇒ f 只能是"档内相对"的：档基准是每档自己的最短周期，跨档由 V档 严格拉开 ✓
        //   ★ 由此得到的一条可验证结论：**"越长越增"在档内看，跨档看 V** ✓
        //
        // ── 整数刻度的物理下限（这一条必须认）──
        //   `金`/`验` 都是整数（`TableDatabase.Validate` 要求 > 0），所以 ① 只能落在整数格点上：
        //   某一档一株的整数和最小是 S ⇒ 该档能表达的最小台阶是 `1/S`；
        //     D 档和只有 2~9 ⇒ 台阶 50%~11%（`frost_ivy` 5 秒 2 单位 = 0.4000/秒）
        //     C 8~40 ⇒ 12.5%~2.5% ｜ B 32~47 ⇒ 3.1%~2.1%
        //     A 31~48 ⇒ 3.2%~2.1% ｜ S 30~49 ⇒ 3.3%~2.0%
        //   所以判据**不写"逐位相等"**，写两条物理上能做到的：
        //     · 同档**同 t** ⇒ 整数和**逐位相等**（这条是精确的，没有任何容差）✓
        //     · 同档**不同 t** ⇒ `|整数和 − 目标| ≤ 0.5`（= 四舍五入的**理论下界**，不是放宽）✓
        //   改前 v199 的容差是 `max(2%, 1/最小分子)`（D 档放到 33.33%）——
        //   本轮换成"半个整数单位的绝对值"，比那条**更严**（D 档 = 0.5/2 = 25%，但它随分子自然收紧）✓
        // ════════════════════════════════════════════════════════════════════

        /// <summary>`f(t)` 的斜率：**成熟时间每翻一倍，每秒总和 +10%**（用户"略微增加"）。</summary>
        public const float GrowTimeEscalation = 0.10f;

        /// <summary>
        /// 五档的**档基准 = 该档最快那一株的 (金+验)/秒**（下标 = `(int)SeedGrade`）。
        /// 也是 `f(t)` 的归一化点（`f(档基准周期) = 1`）✓
        ///
        /// 取值依据（逐档可复算）：
        ///   · 每档必须严格高于上一档的**最大值** `V低 × f_max(低)`，且机制植物要再打 6% 折
        ///     ⇒ `V高 > V低 × f_max / 0.94`（D 1.168 / C 1.200 / B 1.049 / A 1.056）
        ///   · 起点 D = 0.50 由**整数刻度**定死：D 最快那一株是 5 秒、一格的整数和最少是 2
        ///     ⇒ 该档最快株的率下限就是 `2/5 = 0.4000`，而档基准（f=1 处）只能取 0.50
        ///   · 其余四档在这个地板上按上面的不等式逐个抬 ⇒ 0.62 / 0.80 / 0.90 / 1.06
        /// </summary>
        public static readonly float[] SeedTierBudget = { 0.50f, 0.62f, 0.80f, 0.90f, 1.06f };

        /// <summary>
        /// 五档的**档基准周期**（秒，`f(t)` 的归一化点；下标 = `(int)SeedGrade`）——
        /// 即该档里最快那一株的成熟时间（D 5 / C 13.5 / B 40 / A 34 / S 30）。
        /// ⚠️ 它与 `seeds.json` 的 `growTime` 是**两份事实**：这里写的是"策划案要求的档基准"，
        ///    自检拿它跟表比 ⇒ 表被改乱时**会红**。这就是它存在的意义，不是重复 ✓
        /// </summary>
        public static readonly float[] SeedTierBaseGrow = { 5f, 13.5f, 40f, 34f, 30f };

        /// <summary>
        /// `f(t)`：成熟时间越长，每秒总和越高（单调递增，档基准处恒为 1）。
        /// `Mathf.Log(x, 2f)` = log2，与注释里的算式逐位一致 ✓
        /// </summary>
        public static float GrowTimeFactor(SeedGrade grade, float growTime)
        {
            float baseGrow = SeedTierBaseGrow[Mathf.Clamp((int)grade, 0, 4)];
            if (growTime <= baseGrow || baseGrow <= 0f) return 1f;
            return 1f + GrowTimeEscalation * Mathf.Log(growTime / baseGrow, 2f);
        }

        /// <summary>某档某周期的**目标每秒总和** `V档 × f(t)`（未含机制折扣）。</summary>
        public static float BudgetSumPerSecond(SeedGrade grade, float growTime)
            => SeedTierBudget[Mathf.Clamp((int)grade, 0, 4)] * GrowTimeFactor(grade, growTime);

        /// <summary>是不是机制植物（`mechanic` 非空且不是 `None`）。</summary>
        public static bool HasMechanic(SeedDef def)
            => def != null && !string.IsNullOrEmpty(def.Mechanic)
               && !string.Equals(def.Mechanic, None, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// **一格的整数和**（金 + 验）—— 用户规则里的"总和"，1 : 1 相加 ✓
        /// 它同时是落表那个整数的**唯一口径**（`goldYield + expYield`）。
        /// </summary>
        public static int SumPerCell(SeedDef def) => def == null ? 0 : def.GoldYield + def.ExpYield;

        /// <summary>
        /// **每秒总和**（用户第一条规则的度量）：`(金 + 验) / 成熟秒数` ✓
        ///
        /// ★ 与 `SeedEconomy.ValuePerSecond` 的分工（别混）★
        ///   那是 v179 的**加权**口径 `(金 + 2×验)/t`，只用于"档内感知价值"的历史对照；
        ///   本轮的公平判据一律读**本方法**（1:1，用户的字面规则）✓
        ///   它**不进任何结算公式**：结算永远是 `CellGold` / `CellExp` 两个原始整数 ✓
        /// </summary>
        public static float SumPerSecond(SeedDef def)
            => def == null || def.GrowTime <= 0f ? 0f : (def.GoldYield + def.ExpYield) / def.GrowTime;

        /// <summary>
        /// **这一株应该落的整数和**（含机制折扣，未取整）——
        /// `V档 × f(t) × t × (有机制 ? 1 − 6% : 1)`。
        /// 自检拿它跟表里的 `金+验` 比：`|差| ≤ 0.5`（四舍五入的理论下界）✓
        /// </summary>
        public static float ExpectedSum(SeedDef def)
        {
            if (def == null || def.GrowTime <= 0f) return 0f;
            float sum = BudgetSumPerSecond(def.Grade, def.GrowTime) * def.GrowTime;
            return HasMechanic(def) ? sum * (1f - MechanicPayback) : sum;
        }


        /// <summary>
        /// **同档公平判据的完整版**：每秒总和 × （1 + 机制补偿）。
        /// `SEED-TIER-RATE` 读它（`SelfCheck` 里不再自己算一遍）✓
        ///
        /// ★ v200：分子从 `金 + 2×验` 换成 `金 + 验` ★
        ///   用户本轮的规则是"金币和经验值获取量在相同时间内相同（**总和**）"⇒ 1 : 1 相加 ✓
        ///   而机制植物的 6% 折扣在这里**乘回来** ⇒ 同档同 t 的机制植物与非机制植物
        ///   在"公平值"上相等、在"原始总和"上严格更低 —— 正好同时满足
        ///   "机制 ⇒ 总和减少"与"档内不存在支配关系"两条 ✓
        /// </summary>
        public static float FairValuePerSecond(SeedDef def)
        {
            float v = SumPerSecond(def);
            return HasMechanic(def) ? v * (1f + MechanicPayback) : v;
        }

        /// <summary>机制 1 / 2：某等级的"每层百分点"（3% + 1%×(等级−1)）。</summary>
        public static float HarvestBuffPerStack(int level)
            => HarvestBuffBasePct + HarvestBuffPerLevelPct * Mathf.Max(0, level - 1);

        /// <summary>机制 3：某等级的站田闪避加成（0.20 + 0.05×(等级−1)）。</summary>
        public static float StandingDodgeValue(int level)
            => StandingDodgeBase + StandingDodgePerLevel * Mathf.Max(0, level - 1);

        /// <summary>机制 4 / 5：某等级每分钟的触发概率（0.10 + 0.05×(等级−1)，钳在 1）。</summary>
        public static float WeatherChance(int level)
            => Mathf.Min(1f, WeatherChanceBase + WeatherChancePerLevel * Mathf.Max(0, level - 1));

        /// <summary>机制 6：某等级的幸运加成（0.30 + 0.05×(等级−1)）。</summary>
        public static float FarmLuckValue(int level)
            => FarmLuckBase + FarmLuckPerLevel * Mathf.Max(0, level - 1);

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

        /// <summary>
        /// 该格收获时的**金币倍率**（`RichHarvest` 常驻、`ThirstBloom` 在**水分低于 50%** 时生效）。
        ///
        /// ★ v219：判定从"缺水（`Water &lt;= 0`，经 `plot.GrowthPaused`）"改成"**水分比例 &lt; 50%**" ★
        ///   用户原话："不是缺水，而是水分低于 50" ⇒ 旧判据 `plot.GrowthPaused`
        ///   （= `Thirsty`（`Water &lt;= 0`）‖ `NeedFullRefill`）只有**水见底**才给加成，
        ///   玩家必须把水箱彻底用干才拿得到 ×1.60，等于"惩罚到位才奖励" ✗
        ///   现在只要**低于一半**就生效：`plot.Water01 &lt; ThirstGoldRatio` ✓
        ///
        ///   为什么不再借用 `GrowthPaused`：那个属性回答的是"**还长不长**"（停下生长的闸门），
        ///   而这一条问的是"**够不够渴**"（收益档位）—— 两个问题从 v219 起答案不同，
        ///   继续共用一个属性会让"调生长规则把金币规则一起改掉" ✓
        ///   水量上限与等级的关系见 `FarmPlot.Water01` 的注释（100／140／180 ⇒ 必须用比例）✓
        /// </summary>
        public static float HarvestGoldMult(SeedDef def, FarmPlot plot)
        {
            if (def == null) return 1f;
            float m = 1f;
            if (Has(def, RichHarvest)) m *= RichHarvestMult;
            // ★ 水分低于一半反而有收益 ★ —— 用户标准里明确列出的那一种
            if (Has(def, ThirstBloom) && plot != null && plot.Water01 < ThirstGoldRatio) m *= ThirstBloomMult;
            // ★ v253 新机制 1：满水厚赏（与上面那条**方向相反**、阈值互补）★
            if (Has(def, FullWaterGold) && plot != null && plot.Water01 >= FullWaterRatio) m *= FullWaterGoldMult;
            return m;
        }

        /// <summary>
        /// ★ v253 新机制 2：**连收递增的算式**（纯函数）★
        /// `层数 1 ⇒ ×1.00`、`层数 2 ⇒ ×1.06` …… `层数 ≥ 6 ⇒ ×1.36`。
        /// 账本（"这一格现在是第几层"）在 `ProgMechanics.PlantStreakGoldMult` ✓
        /// </summary>
        public static float StreakGoldMult(int stacks)
            => 1f + StreakStepPct * Mathf.Clamp(stacks - 1, 0, StreakMaxStacks - 1) / 100f;

        /// <summary>
        /// ★ v253 新机制 3 的判据：**现在是不是夜间** ★
        /// 直接读昼夜循环本条纯函数（`DayNightCycle.H01`：0 = 子夜、1 = 正午），
        /// 不另写"第几分钟算夜里"—— 那是第二份时间口径（见 `NightGold` 的注释）✓
        /// </summary>
        public static bool IsNight(float worldTime) => DayNightCycle.H01(worldTime) <= NightH01Max;

        /// <summary>
        /// ★ v253 新机制 3 / 5：**需要"整块农场 / 世界时刻"才能算的金币倍率** ★
        ///
        /// · `NightGold`      —— 夜间 ×1.20（读 `worldTime` 经 `DayNightCycle.H01`）
        /// · `PlotCountGold` —— 每块已解锁耕地 +2%，上限 +20%
        ///
        /// ★ 为什么与 `HarvestGoldMult` 分开成两个函数，而不是加参数合成一个 ★
        ///   前者只要 `(def, plot)`：**一格自己的事**；
        ///   这两条要 `(farm, worldTime)`：**农场与世界的事**。
        ///   合成一个就得给 6 个既有调用点（HUD 预览 / 两个探针）都塞两个它用不上的参数 ✗
        ///   分开之后：既有调用点**一个字都没改**，而新参数只在**唯一的结算点**
        ///   （`World.HarvestPlot`）被喂进去 ✓
        ///
        /// ★ 参数为 null / 无机制 ⇒ 恒返回 1 ⇒ 对本轮以前的 39 株零影响 ✓
        /// </summary>
        public static float FarmHarvestGoldMult(SeedDef def, FarmGrid farm, float worldTime)
        {
            if (def == null) return 1f;
            float m = 1f;
            if (Has(def, NightGold) && IsNight(worldTime)) m *= NightGoldMult;
            if (Has(def, PlotCountGold) && farm != null)
            {
                FarmPlot[] slots = farm.Slots;
                int unlocked = 0;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null && slots[i].Unlocked) unlocked++;
                m *= 1f + Mathf.Min(PlotCountCapPct, PlotCountStepPct * unlocked) / 100f;
            }
            return m;
        }

        /// <summary>
        /// ★ v253 新机制 6：**血勇** —— 玩家生命比例严格低于 40% 时该株生长 ×1.30 ★
        /// `hp01` 传 `HealthPool.HpPct`（0..1）；`≤0`（没有玩家 / 已死）**不给**加成：
        /// 死亡后的那几帧不该再让作物加速 ✓
        /// </summary>
        public static float LowHpGrowMult(SeedDef def, float hp01)
            => Has(def, LowHpGrow) && hp01 > 0f && hp01 < LowHpRatio ? LowHpGrowMultValue : 1f;

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
            if (string.Equals(mechanic, ThirstBloom, System.StringComparison.OrdinalIgnoreCase)) return $"焦渴绽放：水分低于 {ThirstGoldRatio * 100f:0}% 时该格收获金币 ×{ThirstBloomMult:0.##}";
            // ── v197 的六条 ──
            if (string.Equals(mechanic, HarvestAtkMult, System.StringComparison.OrdinalIgnoreCase))
                return $"收割狂热：收割它 ⇒ {HarvestBuffSeconds:0} 秒内攻击倍率每层 +{HarvestBuffBasePct:0}%（每级 +{HarvestBuffPerLevelPct:0}%，最多 {HarvestBuffMaxStacks} 层，叠层刷新时长）";
            if (string.Equals(mechanic, HarvestAtkSpeed, System.StringComparison.OrdinalIgnoreCase))
                return $"收割疾风：收割它 ⇒ {HarvestBuffSeconds:0} 秒内攻击速度每层 +{HarvestBuffBasePct:0}%（每级 +{HarvestBuffPerLevelPct:0}%，最多 {HarvestBuffMaxStacks} 层，叠层刷新时长）";
            if (string.Equals(mechanic, StandingDodge, System.StringComparison.OrdinalIgnoreCase))
                return $"站田：站在它所在的田地上 ⇒ 闪避 +{StandingDodgeBase * 100f:0}%（每级 +{StandingDodgePerLevel * 100f:0}%，走动着也生效）";
            if (string.Equals(mechanic, SunnyBloom, System.StringComparison.OrdinalIgnoreCase))
                return $"日照：在场时每分钟整点 {WeatherChanceBase * 100f:0}%（每级 +{WeatherChancePerLevel * 100f:0}%）触发大晴天 {WeatherSeconds:0} 秒 ⇒ 全场生长速度 ×{SunnyGrowMult:0.##}";
            if (string.Equals(mechanic, RainCloud, System.StringComparison.OrdinalIgnoreCase))
                return $"降雨：在场时每分钟整点 {WeatherChanceBase * 100f:0}%（每级 +{WeatherChancePerLevel * 100f:0}%）触发雨天 {WeatherSeconds:0} 秒 ⇒ 补满所有耕地水分";
            if (string.Equals(mechanic, FarmLuck, System.StringComparison.OrdinalIgnoreCase))
                return $"祥瑞：在场时幸运 +{FarmLuckBase * 100f:0}%（每级 +{FarmLuckPerLevel * 100f:0}%）";
            // ── v253 的六条（短版：与上面同样"一句一条机制"，数值直接来自常量）──
            if (string.Equals(mechanic, FullWaterGold, System.StringComparison.OrdinalIgnoreCase))
                return $"满水厚赏：水分不低于 {FullWaterRatio * 100f:0}% 时该格收获金币 ×{FullWaterGoldMult:0.##}";
            if (string.Equals(mechanic, StreakHarvest, System.StringComparison.OrdinalIgnoreCase))
                return $"连收递增：同格准时连收每层 +{StreakStepPct:0}%（上限 +{StreakStepPct * (StreakMaxStacks - 1):0}%，超时重算）";
            if (string.Equals(mechanic, NightGold, System.StringComparison.OrdinalIgnoreCase))
                return $"夜露：夜间收获该格金币 ×{NightGoldMult:0.##}";
            if (string.Equals(mechanic, KillRush, System.StringComparison.OrdinalIgnoreCase))
                return $"猎杀亢奋：击杀敌人后 {KillRushSeconds:0} 秒内攻击倍率 ×{KillRushAtkMult:0.##}";
            if (string.Equals(mechanic, PlotCountGold, System.StringComparison.OrdinalIgnoreCase))
                return $"田园成片：每块已解锁耕地收获金币 +{PlotCountStepPct:0}%（上限 +{PlotCountCapPct:0}%）";
            if (string.Equals(mechanic, LowHpGrow, System.StringComparison.OrdinalIgnoreCase))
                return $"血勇：主人生命低于 {LowHpRatio * 100f:0}% 时该株生长 ×{LowHpGrowMultValue:0.##}";
            return "";
        }
    }
}
