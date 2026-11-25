using System;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城的状态层（纯 C#，不依赖任何 GameObject）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么状态是一个**静态**对象，而不是挂在 `TownBootstrap` 上的字段
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户要求「种子的成熟时间**在战斗中也会推进**」。战斗是一张独立场景
    /// （`SeedfallArena.unity`），进城 / 出城都是 `SceneManager.LoadScene` ——
    /// 场景一换，MonoBehaviour 上的字段就全没了 ✗
    /// ⇒ 田地、仓库、金币必须活在一个**不随场景销毁**的地方。这里选静态对象（不是 `DontDestroyOnLoad`）：
    ///    · 静态对象的生命周期是"一次运行"，域重载（退出 play）时自动清空 ⇒ 不会跨 play 会话脏数据 ✓
    ///    · 不需要一个"永远活着"的 GameObject 去背它 —— 那类对象正是 `DebugApi` 注释里记着的坑
    ///      （活过场景切换的实例让下一次会话读到旧计时）✓
    ///
    /// ⚠️ **本类的磁盘持久化不在这里**（读档/存档在 `Login/TownSaveData.cs`，
    ///    它按名字反射写 `Warehouse` / `PlotSeed` / `PlotElapsed` / `PlotMature` / `Coins` / `_coinFraction`
    ///    ⇒ **这几个名字与 `Snapshot()` 都不许改名**，改了就静默坏档）。
    ///    要接存档，入口就是本类的三个数组 + 两个标量，写出去/读回来各一个方法即可（见 `Snapshot`）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、产金 / 成熟 / 收获的规则（**全工程唯一实现**，HUD / 小地图 / 自动化入口都读这里）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① 有作物的地每秒结算 `goldPerSecond`（恒定，与是否成熟无关）。
    ///   ② `elapsed >= growSeconds` 的那一帧记为**成熟**。成熟**只管观感与"能不能收"**，
    ///      **不往仓库塞任何东西**。
    ///   ③ **收获**（<see cref="Harvest"/>）是**唯一**把种子送回仓库的路径：作物离开田位 ＋
    ///      那颗种子回仓库，**两件事在同一处一起做**。
    ///   ④ 金币用 double 累加、**满 1 才入账** —— 逐帧 `Mathf.Floor` 会把 0.4 金/秒抹成 0 ✗
    ///   ⑤ 换种 = 覆盖：旧的不退还、`elapsed` 归零。铲除 = 直接清空（同样不退还）。
    ///
    /// ★★ 为什么②③ 要这么定（这是本类最重要的一条不变量）★★
    /// 用户报的原话：「**存在耕地上的植物与实际拥有的种子不匹配的问题，没有同步更新**」。
    /// 上一版的 ② 是"成熟时**返还 1 颗同类种子**，作物留在地里继续产金"——
    /// 那等于**同一颗种子被数了两次**：种下时仓库 −1，长成时仓库 +1 而地里那株还在 ⇒
    /// 玩家看到的田里有 1 株、仓库里却又是满的，而且这块地从此永远停在"成熟"、
    /// 再也不会有任何状态变化（"没有同步更新"）✗ 实测读数见 `TownBootstrap.SeedLedger`。
    /// ⇒ 现在的口径（**一颗种子只能在"仓库"或"田里"其中一处**）：
    ///      种下：仓库 −1、田 +1　　收获：仓库 +1、田 −1
    ///   于是 `Σ仓库 + 已种` 在"种 / 收"上是不变量 —— 只有掉落(+)、卖(−)、
    ///   铲除或换种没成熟的作物(−) 会改变它 ⇒ **两个数组只剩一组一致的搬运口** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、数值口径
    /// ══════════════════════════════════════════════════════════════════════
    ///   `Coins` 是 `long`（整数金币，避免浮点在 HUD 上跳 1233.9999）；
    ///   `_coinFraction` 是"还没满 1 金"的零头，只在本类内部流转。
    ///   两者之和 = 真实累积，无损。
    /// </summary>
    public sealed class TownState
    {
        /// <summary>田地格数（6×6 = 36）。改这个数会同时改变场景里的地块数量与小地图网格。</summary>
        public const int PlotCount = 36;

        /// <summary>单种种子在仓库里的上限（超过就不再产出/购买）。</summary>
        public const int WarehouseCap = 99;

        /// <summary>一次 `Advance` 最多推进多少秒（防止战斗中长时间挂机后一次性结算爆表）。</summary>
        public const double MaxAdvanceSeconds = 86400.0;

        /// <summary>当前主城状态。**只在城建起来时 `Ensure`，之后跨场景复用**。</summary>
        public static TownState Current { get; private set; }

        /// <summary>取当前状态；没有就建一个（含初始仓库赠种）。</summary>
        public static TownState Ensure()
        {
            if (Current == null)
            {
                // ★ v213：**先载表，再定仓库长度** ★
                //   `Initialize` 用 `TownData.Seeds.Length` 决定 `Warehouse` 的数组长度，
                //   而 `TownData.Seeds` 在 `Load()` 之前是空数组 ⇒ 谁要是**比城建更早**碰到
                //   `Ensure()`（实测：皮肤商店在主城刚进 play、`TownBootstrap.BuildScene` 还没跑完时
                //   被探针点了一下），仓库就会被建成 **1 格**：不报错、不崩，只是从那一刻起
                //   "掉一颗青豆荚"永远返回 0、种地只有赤玉萝卜一种 —— 最难查的那类静默错 ✗
                //   把 Load 放在这里，这条路径就**结构上不可能**再走错 ✓
                //   （`TownBootstrap` 自己也会先 Load 一次，Ready 之后这里是空操作）
                TownData.Load();
                Current = new TownState();
                Current.Initialize();
            }
            return Current;
        }

        /// <summary>把状态丢掉，下次 `Ensure` 重新开始（给"重开一局"留的口子；P1 无 UI 入口）。</summary>
        public static void Discard() => Current = null;

        // ── 金币 ──
        public long Coins { get; private set; }
        private double _coinFraction;

        // ── 仓库 / 田地（长度 = 种子表条数 / PlotCount）──
        public int[] Warehouse { get; private set; } = Array.Empty<int>();
        public int[] PlotSeed { get; private set; } = new int[PlotCount];
        public double[] PlotElapsed { get; private set; } = new double[PlotCount];
        public bool[] PlotMature { get; private set; } = new bool[PlotCount];

        /// <summary>已种植的地块数（HUD 用）。</summary>
        public int PlantedCount { get; private set; }

        /// <summary>已成熟的地块数（HUD 用）。</summary>
        public int MatureCount { get; private set; }

        /// <summary>当前每秒金币总产出（= 所有已种地块的 goldPerSecond 之和）。</summary>
        public double IncomePerSecond { get; private set; }

        /// <summary>仓库里所有种子加起来有多少颗。</summary>
        public int WarehouseTotal { get; private set; }

        private TownState() { }

        // ══════════════════════════════════════════════════════════════════
        // 初始化
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 初始仓库赠种。
        ///
        /// ★★ v214：**全部为 0** ★★
        /// 用户原话：「**所有主城种子必须通过战斗 boss 掉落**」「**移除新开存档的种子供给**」。
        /// 上一版这里是 `{ 8, 8, 6, 4, 2, 1, 0, 0 }`（开局白送 29 颗），这一版一颗都不送 ⇒
        /// 新号进城时仓库是空的，田圃一块也种不了，必须先去战斗里打 BOSS（掉落链路见 `TownDrops`）✓
        ///
        /// ⚠️ **字段名必须叫 `StarterStock` 且保持 `private static`**：
        /// `Assets/Scripts/Login/TownSaveData.cs` 用反射按这个名字读它（"新的游戏"= 按开局库存重建仓库）。
        /// 改名或改成实例字段会让那一路静默拿不到值 ⇒ 新号拿到**旧的赠种**，而界面上没有任何报错 ✗
        /// </summary>
        private static readonly int[] StarterStock = { 0, 0, 0, 0, 0, 0, 0, 0 };

        private void Initialize()
        {
            int n = Mathf.Max(1, TownData.Seeds.Length);
            Warehouse = new int[n];
            for (int i = 0; i < n; i++)
            {
                int stock = i < StarterStock.Length ? StarterStock[i] : 0;
                Warehouse[i] = Mathf.Clamp(stock, 0, WarehouseCap);
            }
            for (int i = 0; i < PlotCount; i++)
            {
                PlotSeed[i] = -1;
                PlotElapsed[i] = 0.0;
                PlotMature[i] = false;
            }
            Coins = 0;
            _coinFraction = 0.0;
            Recompute();
        }

        // ══════════════════════════════════════════════════════════════════
        // 时间推进（★ 主城的金币与成熟都从这里走 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 推进 `seconds` 秒。返回**本次入账的整数金币**。
        /// `seconds <= 0` 或没有作物时立即返回 0（不做任何循环）。
        /// </summary>
        public long Advance(double seconds)
        {
            if (seconds <= 0.0) return 0L;
            if (seconds > MaxAdvanceSeconds) seconds = MaxAdvanceSeconds;

            double earned = 0.0;
            bool matured = false;

            for (int i = 0; i < PlotCount; i++)
            {
                int si = PlotSeed[i];
                if (si < 0) continue;
                TownSeedDef d = TownData.SeedAt(si);
                if (d == null) continue;

                // ★★ v239：**一次性结算 + 循环生长**（用户："10 分钟起步的成熟时间，到成熟后一次性获得金币，
                //   不每秒结算"）★★
                //   ① 删掉旧的"每秒 `GoldPerSecond * seconds`"那条路径（不是加开关，是删掉）
                //   ② 地块自己累计 `PlotElapsed`；**跨过 GrowSeconds 的那一帧**一次性给 `Yield`，
                //      然后把 `PlotElapsed` **减去一个周期**（不是清零）⇒ 余量继续往下走
                //   ③ 作物**不收**，原地进入下一个周期（循环生长）⇒ 每 `GrowSeconds` 秒再来一次
                //   ④ `Yield = GoldPerSecond × GrowSeconds × 1.05`
                //      ⇒ **每小时产出 = 改前 × 1.05（略高）**，因为 3600 秒内正好 3600/GrowSeconds 个周期
                double cycle = d.GrowSeconds > 0.05 ? d.GrowSeconds : 0.05;
                double payout = d.GoldPerSecond * cycle * 1.05;          // 单次结算量（比旧累计高 5%）

                PlotElapsed[i] += seconds;
                if (!PlotMature[i] && PlotElapsed[i] >= cycle)
                {
                    // 第一次成熟：翻标记（视觉/HUD 用），并且**这一帧就一次性结算** ✓
                    PlotMature[i] = true;
                    matured = true;
                }
                // 只要跨过了周期就结算（可能一帧跨多个周期：把 dt 拆成整周期 + 余量）
                while (PlotElapsed[i] >= cycle)
                {
                    PlotElapsed[i] -= cycle;
                    earned += payout;
                }
            }

            if (earned > 0.0)
            {
                _coinFraction += earned;
                long whole = (long)Math.Floor(_coinFraction);
                if (whole > 0)
                {
                    _coinFraction -= whole;
                    Coins += whole;
                    if (matured) Recompute();
                    return whole;
                }
            }
            if (matured) Recompute();
            return 0L;
        }

        // ══════════════════════════════════════════════════════════════════
        // 种植 / 换种 / 铲除
        // ══════════════════════════════════════════════════════════════════

        /// <summary>这块地能不能种（空地 && 仓库里这种种子 > 0）。</summary>
        public bool CanPlant(int plot, int seedIndex)
        {
            if (plot < 0 || plot >= PlotCount) return false;
            if (seedIndex < 0 || seedIndex >= Warehouse.Length) return false;
            if (PlotSeed[plot] >= 0) return false;
            return Warehouse[seedIndex] > 0;
        }

        /// <summary>
        /// 在 <paramref name="plot"/> 种下 <paramref name="seedIndex"/>。
        /// **空地种 = 扣 1 颗；已有作物 = 换种（覆盖，旧的不退）**。
        /// 返回 false 的含义：地块非法 / 种子索引非法 / 仓库没有这种种子 / 和现在种的是同一种。
        /// </summary>
        public bool Plant(int plot, int seedIndex)
        {
            if (plot < 0 || plot >= PlotCount) return false;
            if (seedIndex < 0 || seedIndex >= Warehouse.Length) return false;
            if (PlotSeed[plot] == seedIndex) return false;
            if (Warehouse[seedIndex] <= 0) return false;

            // ★ 去程：仓库 → 田位。**只走这一个原语**（回程是 `Harvest` 里的 `MoveFromPlot`）★
            MoveToPlot(plot, seedIndex);
            Recompute();
            return true;
        }

        /// <summary>
        /// ★ 收获一块地：**作物离开田位 ＋ 那颗种子回仓库，两件事在同一处一起做** ★
        ///
        /// 这是全类**唯一**把种子写回仓库的"田 → 仓库"路径（掉落/购买走 <see cref="AddSeed"/>，
        /// 但那是"外部来源"，与田位无关）。用户报的「耕地上的植物与实际拥有的种子不匹配，
        /// 没有同步更新」的根因就是上一版没有这条路：成熟时**直接**给仓库 +1 而地把作物留着 ⇒
        /// 田里 1 株 + 仓库满额 = 同一颗种子被数了两次，且这块地从此不再变化 ✗
        ///
        /// 返回 false 的含义：地块非法 / 这块地是空的 / 还没成熟（没长成不能收）。
        /// 种子返还受 <see cref="WarehouseCap"/> 钳制；**仓库满了照样腾地** —— 地不能被"仓库满"锁住。
        /// </summary>
        public bool Harvest(int plot)
        {
            if (plot < 0 || plot >= PlotCount) return false;
            if (PlotSeed[plot] < 0) return false;
            if (!PlotMature[plot]) return false;

            MoveFromPlot(plot);
            Recompute();
            return true;
        }

        /// <summary>仓库 → 田位（种下 / 换种）。**唯一**的"仓库减少 + 田位写入"处。</summary>
        private void MoveToPlot(int plot, int seedIndex)
        {
            Warehouse[seedIndex]--;
            PlotSeed[plot] = seedIndex;
            PlotElapsed[plot] = 0.0;
            PlotMature[plot] = false;
        }

        /// <summary>
        /// 田位 → 仓库（收获）。**唯一**的"田位清空 + 仓库增加"处。
        /// 与 <see cref="MoveToPlot"/> 成对：两个方向都只在这里改两个数组，别处一律不许。
        /// </summary>
        private void MoveFromPlot(int plot)
        {
            int si = PlotSeed[plot];
            PlotSeed[plot] = -1;
            PlotElapsed[plot] = 0.0;
            PlotMature[plot] = false;
            if (si >= 0) AddSeedInternal(si, 1);
        }

        /// <summary>铲掉这块地（不退还种子）。返回是否真的有东西被铲。</summary>
        public bool Clear(int plot)
        {
            if (plot < 0 || plot >= PlotCount) return false;
            if (PlotSeed[plot] < 0) return false;
            PlotSeed[plot] = -1;
            PlotElapsed[plot] = 0.0;
            PlotMature[plot] = false;
            Recompute();
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // 仓库 / 商业街
        // ══════════════════════════════════════════════════════════════════

        /// <summary>往仓库加种子（掉落、成熟返还、购买都走这里）。返回实际加进去的数量（受上限钳制）。</summary>
        public int AddSeed(int seedIndex, int amount)
        {
            if (seedIndex < 0 || seedIndex >= Warehouse.Length || amount <= 0) return 0;
            int added = AddSeedInternal(seedIndex, amount);
            if (added > 0) Recompute();
            return added;
        }

        private int AddSeedInternal(int seedIndex, int amount)
        {
            if (seedIndex < 0 || seedIndex >= Warehouse.Length || amount <= 0) return 0;
            int room = WarehouseCap - Warehouse[seedIndex];
            if (room <= 0) return 0;
            int added = Mathf.Min(room, amount);
            Warehouse[seedIndex] += added;
            return added;
        }

        /// <summary>仓库里有没有能种的东西（选种面板要用来判"空仓"）。</summary>
        public bool HasAnySeed()
        {
            for (int i = 0; i < Warehouse.Length; i++) if (Warehouse[i] > 0) return true;
            return false;
        }

        /// <summary>
        /// ★ v214：**把仓库里的种子换成金币**（商店的「收种子」分页）★
        ///
        /// 用户原话：「商店可以让玩家**把不要的主城种子换金币**」。
        /// 回收价不是这里算的 —— 它写在 `town_seeds.json` 的 `sellPrice` 字段（每颗多少钱），
        /// 本方法只负责"原子地做完一件事"：**扣种子 + 加金币 + 重算派生量**。
        /// 分成两步（先扣种子、再在某处加金币）会在第二步失败时留下"种子没了钱也没到"的状态 ✗
        ///
        /// 返回这次真正到账的金币数（0 = 仓库里没那么多该种子）。
        /// </summary>
        public long SellSeed(int seedIndex, int amount)
        {
            if (seedIndex < 0 || seedIndex >= Warehouse.Length || amount <= 0) return 0L;
            TownSeedDef d = TownData.SeedAt(seedIndex);
            if (d == null) return 0L;

            int have = Warehouse[seedIndex];
            int removed = Mathf.Min(have, amount);
            if (removed <= 0) return 0L;

            long gain = (long)removed * d.SellPrice;
            if (gain <= 0) return 0L;

            Warehouse[seedIndex] = have - removed;
            Coins += gain;
            Recompute();
            return gain;
        }

        /// <summary>
        /// ★ v214：**商店不再卖种子** ⇒ 上一版这个"花金币换一颗种子进仓库"的口子已经删掉 ★
        ///
        /// 为什么是**删**而不是留着一个没人调的公开方法：
        /// 用户要求「所有主城种子必须通过战斗 boss 掉落」，如果这里还留着一个能"花金币变出种子"的入口，
        /// 那么"种子的唯一来源是 BOSS"这句话在代码里就不成立了 —— 它只是没被 UI 调到而已。
        /// ⇒ 现在花金币只有一条路：<see cref="TrySpend"/>（扣钱），
        ///   货架上买到的东西由 `TownGoods` 负责落地（道具/插件/成品，都**不进仓库**）✓
        /// </summary>
        public bool TrySpend(long amount)
        {
            if (amount <= 0) return false;
            if (Coins < amount) return false;
            Coins -= amount;
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // 汇总
        // ══════════════════════════════════════════════════════════════════

        /// <summary>重算派生量（已种数 / 成熟数 / 每秒产出 / 仓库总数）。写入路径**必须**调它。</summary>
        public void Recompute()
        {
            int planted = 0, mature = 0, total = 0;
            double gps = 0.0;
            for (int i = 0; i < PlotCount; i++)
            {
                int si = PlotSeed[i];
                if (si < 0) continue;
                planted++;
                if (PlotMature[i]) mature++;
                TownSeedDef d = TownData.SeedAt(si);
                if (d != null) gps += d.GoldPerSecond;
            }
            for (int i = 0; i < Warehouse.Length; i++) total += Warehouse[i];
            PlantedCount = planted;
            MatureCount = mature;
            IncomePerSecond = gps;
            WarehouseTotal = total;
        }

        /// <summary>
        /// 状态快照（给存档 / 自动化断言用）。P1 **没有**存档，但把读取口先留好：
        /// `Snapshot` 出去的字符串直接能 spawn 到第二台机器上对照。
        /// </summary>
        public string Snapshot()
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append("coins=").Append(Coins)
              .Append(" planted=").Append(PlantedCount)
              .Append(" mature=").Append(MatureCount)
              .Append(" gps=").Append(IncomePerSecond.ToString("0.##"))
              .Append(" warehouse=[");
            for (int i = 0; i < Warehouse.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Warehouse[i]);
            }
            sb.Append("] plots=");
            for (int i = 0; i < PlotCount; i++)
            {
                // 每块地两位：负号=空，否则"种子序号"，方便一眼看出 36 块地的分布
                if (PlotSeed[i] < 0) sb.Append("..");
                else sb.Append(PlotSeed[i].ToString("00"));
                if ((i + 1) % 6 == 0 && i != PlotCount - 1) sb.Append('|');
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// ★★ 战斗 → 主城 的**唯一接口**（本轮"只留接口，不接线"）★★
    ///
    /// 用户原话：「（在游戏战斗中也会持续推近成熟时间）」。
    /// 本轮禁止改动战斗内容（`Combat/**` / `Flow/RunSession.cs` / `Actors/**` / `SeedfallArena.unity`），
    /// 所以这条链路的**接线**在战斗侧完成，主城这边把口子开好、写清怎么用：
    ///
    /// ```csharp
    /// // 战斗侧（未来，一行，放在 RunSession.FixedTick 里）：
    /// TownGrowth.AdvanceSeconds(dt);
    /// ```
    ///
    /// 为什么是"推固定秒数"而不是"推战斗波次/分钟"：主城的成熟时间以**秒**为单位，
    /// 而战斗的 `FixedTick(dt)` 天然就是秒 ⇒ 一个 dt 一个 dt 地推，切回主城时读到的成熟进度
    /// 与"一直在主城站着"**逐秒等价**，不需要额外的换算表 ✓
    ///
    /// 安全性：这个方法在 `TownState.Current == null`（没进过主城）时是**空操作**，
    /// 不建状态、不分配 ⇒ 战斗侧即使无条件每帧调它，也不会产生任何副作用 ✓
    /// </summary>
    public static class TownGrowth
    {
        /// <summary>
        /// 让主城的所有田地推进 <paramref name="seconds"/> 秒（金币与成熟一起前进）。
        /// **当前没有任何战斗代码调用它** —— 这是本轮的交付边界（见类注释）。
        /// </summary>
        public static void AdvanceSeconds(double seconds)
        {
            TownState state = TownState.Current;
            if (state == null) return;
            state.Advance(seconds);
        }
    }
}
