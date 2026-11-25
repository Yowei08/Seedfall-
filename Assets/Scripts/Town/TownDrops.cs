using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v213：**BOSS 死亡 ⇒ 掉 1 颗主城种子 ⇒ 进主城仓库**（战斗侧到主城的唯一一条新链路）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、它在整条链路上的位置
    /// ══════════════════════════════════════════════════════════════════════
    ///   `Actors.cs`（既有）`Bus.Publish(new EnemyKilledEvent{ EnemyId, Pos, ... })`
    ///        ↓ **唯一新增的一处战斗侧接线**（`RunSession.OnEnemyKilled` 里两行）
    ///   `TownDrops.OnBossKilled(enemyId, pos)`
    ///        ├─ 判种类：`enemies.json` 里 `kind == Boss` 的才算 BOSS（数据驱动，不硬写 id）
    ///        ├─ 抽样：`town_drops.json` 的**品质权重表**（下一条说明为什么不喝 World.Rng）
    ///        ├─ 入账：`TownState.Ensure().AddSeed(索引, 1)` —— **主城仓库的既有入口**，没有第二个写法
    ///        └─ 演出：`BossRewardPopup.Push(...)` —— 战斗里那张"获得了什么"的卡片
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么不消耗 `World.Rng`（这一条关系到整个工程会不会一起变红）
    /// ══════════════════════════════════════════════════════════════════════
    /// `World.Rng` 是全局**唯一**逻辑随机源：刷怪位置、战利品、暴击都从同一条流里取。
    /// 掉落只要喝一口，之后**每一个既有随机读数都会整体漂一位** ⇒ 工程里一批把种子钉死的
    /// 自检、`RunSimulator.VerifyDeterminism`、以及探针的对照读数会一起变红 ——
    /// 而变红的原因不是"掉落算错了"，是"我悄悄改了全局序列"，那是最难查、也最不该付的代价 ✗
    ///
    /// ⇒ 抽样用 `BossSchedule.DerivedHash(击杀序号 × 7919 + 1337)`：
    ///   纯函数、只由"第几次击杀"决定 ⇒ **可重放**、**零全局副作用** ✓
    ///   （与 `BossSchedule` 自己决定"下次刷哪种 BOSS"用的是同一条纪律与同一个哈希器）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、失败模式
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 掉落表坏了 ⇒ `Log.Error` 一次，之后 `TryLoad()` 直接返回 false，**战斗照常进行**
    ///     （打 BOSS 的结果不该被一张概率表决定是不是能继续玩）
    ///   · 主城状态建不出来（主城表缺失）⇒ `Log.Error` + 放弃这次入账，**不抛到战斗帧里**
    ///   · 仓库这一种已经 99（`WarehouseCap`）⇒ `AddSeed` 返回 0 ⇒ 卡片上如实写"仓库已满，这次没装下"
    ///     （**不静默**：玩家看得见自己漏了一颗）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、★★ v254：为什么光"加进 `TownState`"还不够（用户报的 bug）★★
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户原话：「**战斗击败 boss 获取的种子没有实际带到主城仓库，退出游戏回到主城应该也获取成功**」。
    /// 根因不是"没加"，是**加了但没写盘，随后被读档覆盖**：
    ///   ① 击杀那一帧 `TownState.Ensure().AddSeed(...)` 只改**内存**里的静态 `TownState`
    ///      （`TownState.Warehouse`），**一个字节都没有进存档文件**；
    ///   ② 战斗里 `LoginBootstrap`（主城存档的**唯一写者**）已随 Login 场景销毁 ⇒ 战斗期间没有任何人写盘；
    ///   ③ 退出战斗 → `GameBootstrap.ExitToTownNow` 写 `LoginBootstrap.AutoEnterSlot` →
    ///      回 Login 场景后 `LoadSlotIndex` → `TownSaveIO.Restore` 用**进战斗前**那份存档
    ///      **逐字段覆盖**仓库（`TownSaveData.cs` 的 `SetProperty(state,"Warehouse",...)`）
    ///      ⇒ 战斗里加的那一颗被**原地抹掉** ✗
    /// ⇒ 修法（本类的 `TownDropLedger` 部分）：**击杀的那一刻就把这一颗写进存档**
    ///   （`TownDrops.OnBossKilled(enemyId, at, runKey)` 的第三个参数 = "这是真战斗击杀，要落盘"），
    ///   于是任何退出路径（正常退出 / 主动退 / 死亡 / Alt+F4）都不依赖"退出时补一笔"；
    ///   写盘失败时留在**待发队列**里，读档 / 下一次主城存档时补发，且靠存档里的
    ///   `bossSeedGrants`（幂等键）保证**同一击只发一次** ✓
    ///
    /// ⚠️ 掉落的**种类与数量一个字没改**（还是 `PickSeedIndex(BossKills)` 抽 1 颗），
    ///    本次只修"有没有到账"。
    /// ══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class TownDrops
    {
        public const string TablePath = "Data/town_drops";

        /// <summary>抽样哈希的两个盐（与 `BossSchedule` 用不同的偏移，避免与"刷哪种 BOSS"从同一段位出）。</summary>
        private const int HashMul = 7919;
        private const int HashAdd = 1337;

        public static bool Ready { get; private set; }
        public static string LastError { get; private set; }

        private static bool _loadFailed;

        /// <summary>表中每一项在主城种子表（`TownData.Seeds`）里的下标。</summary>
        private static int[] _seedIndex = Array.Empty<int>();

        /// <summary>权重前缀和（长度 = 条目数）。最后一项 = 总权重。</summary>
        private static int[] _prefix = Array.Empty<int>();

        /// <summary>表里没列到的种子（权重 0）只报一次。</summary>
        private static bool _warnedUnlisted;

        /// <summary>累计击败 BOSS 次数（也是抽样用的序号，**1 起**）。</summary>
        public static int BossKills { get; private set; }

        /// <summary>累计真正进了仓库的颗数。</summary>
        public static int DroppedTotal { get; private set; }

        /// <summary>最近一次抽到的种子下标（-1 = 还没掉过）。</summary>
        public static int LastSeedIndex { get; private set; } = -1;

        /// <summary>最近一次的抽样数（0 .. 总权重-1），报告里用它复核"落在哪一档"。</summary>
        public static int LastRoll { get; private set; }

        /// <summary>最近一次真正入账的颗数（0 = 仓库满）。</summary>
        public static int LastAdded { get; private set; }

        /// <summary>最近一次掉落的一句话（控制台 / 卡片 / 探针共用）。</summary>
        public static string LastLine { get; private set; } = "(还没打过 BOSS)";

        /// <summary>总权重（表内所有 weight 之和）。</summary>
        public static int TotalWeight => _prefix.Length > 0 ? _prefix[_prefix.Length - 1] : 0;

        // ══════════════════════════════════════════════════════════════════
        // 载入
        // ══════════════════════════════════════════════════════════════════

        public static void Load()
        {
            if (Ready || _loadFailed) return;
            try
            {
                TextAsset asset = Resources.Load<TextAsset>(TablePath);
                if (asset == null) throw Fail($"缺少配置表 Assets/Resources/{TablePath}.json");

                TownDropTable table;
                try { table = JsonUtility.FromJson<TownDropTable>(asset.text); }
                catch (Exception e) { throw Fail($"{TablePath}.json 解析失败：{e.Message}"); }
                if (table == null || table.bossSeeds == null || table.bossSeeds.Length == 0)
                    throw Fail("town_drops.json 的 bossSeeds 数组为空");

                // ★ 掉落表的 seedId 必须能在**主城种子表**里查到 ★
                //   这里先 `TownData.Load()`：两边都是"主城数据"，一边没载入就查不到索引，
                //   而"查不到就跳过"会让掉落变成静默失效 —— 那正是本类注释里最不想要的那类 bug
                TownData.Load();

                var idx = new int[table.bossSeeds.Length];
                var prefix = new int[table.bossSeeds.Length];
                int sum = 0;
                for (int i = 0; i < table.bossSeeds.Length; i++)
                {
                    BossSeedDrop d = table.bossSeeds[i];
                    if (d == null) throw Fail($"town_drops.json 第 {i} 条为 null");
                    if (string.IsNullOrWhiteSpace(d.seedId)) throw Fail($"town_drops.json 第 {i} 条缺少 seedId");
                    if (d.weight <= 0) throw Fail($"掉落项 {d.seedId} 的 weight 必须 > 0（现 {d.weight}）");
                    int si = TownData.IndexOf(d.seedId);
                    if (si < 0) throw Fail($"掉落项 {d.seedId} 在 town_seeds.json 里不存在");
                    for (int j = 0; j < i; j++)
                        if (idx[j] == si) throw Fail($"town_drops.json 里 {d.seedId} 重复出现");
                    sum += d.weight;
                    idx[i] = si;
                    prefix[i] = sum;
                }

                _seedIndex = idx;
                _prefix = prefix;
                Ready = true;
                LastError = null;
                Log.Info($"主城掉落表载入：{idx.Length} 档权重，总权重 {sum}");

                // 主城种子表里有没有"永远掉不出来"的种子？有就说清楚（不静默），但不抛：
                // 只列一部分种子是**允许**的（见 town_drops.json 的 _note），只是要让人知道
                if (!_warnedUnlisted && TownData.Seeds.Length > idx.Length)
                {
                    _warnedUnlisted = true;
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < TownData.Seeds.Length; i++)
                    {
                        bool listed = false;
                        for (int k = 0; k < idx.Length; k++) if (idx[k] == i) { listed = true; break; }
                        if (!listed) sb.Append(TownData.Seeds[i].id).Append(' ');
                    }
                    if (sb.Length > 0)
                        Log.Warn("主城掉落表没有列出的种子（权重 0，永远不会掉）：" + sb);
                }
            }
            catch (Exception e)
            {
                _loadFailed = true;
                Ready = false;
                LastError = e.Message;
                Log.Error("主城掉落表：" + e.Message);
                throw;
            }
        }

        /// <summary>不抛的载入（战斗帧路径用）。</summary>
        public static bool TryLoad()
        {
            if (Ready) return true;
            if (_loadFailed) return false;
            try { Load(); return Ready; }
            catch (Exception e)
            {
                LastError = e.Message;
                return false;
            }
        }

        private static InvalidOperationException Fail(string msg)
        {
            LastError = msg;
            return new InvalidOperationException(msg);
        }

        // ══════════════════════════════════════════════════════════════════
        // 抽样
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 第 <paramref name="killIndex"/> 次击败 BOSS 掉哪一种（**纯函数**，不读写任何全局状态）。
        /// 返回主城种子表里的下标；表没载入时返回 -1。
        /// </summary>
        public static int PickSeedIndex(int killIndex)
        {
            if (_prefix.Length == 0) return -1;
            int total = TotalWeight;
            if (total <= 0) return -1;

            unchecked
            {
                uint h = BossSchedule.DerivedHash(killIndex * HashMul + HashAdd);
                int roll = (int)(h % (uint)total);
                for (int i = 0; i < _prefix.Length; i++)
                    if (roll < _prefix[i]) return _seedIndex[i];
            }
            return _seedIndex[_seedIndex.Length - 1];   // 理论上到不了（roll < total）
        }

        // ══════════════════════════════════════════════════════════════════
        // BOSS 是不是 BOSS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 这个敌人 id 是不是 BOSS。**数据驱动**：扫 `enemies.json` 里 `kind == Boss` 的条目，
        /// 不把 "boss" / "moth" 硬写在调用点（那样将来加第三种 BOSS 会静默不掉落）。
        /// 表还没载入时退回"表里当前那两种"的硬编码兜底，保证"表没准备好也不漏掉"。
        /// </summary>
        public static bool IsBossId(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return false;
            EnemyDef[] all = TableDatabase.Enemies;
            for (int i = 0; i < all.Length; i++)
            {
                EnemyDef d = all[i];
                if (d == null || d.Id != enemyId) continue;
                return d.Kind == EnemyKind.Boss;
            }
            // 表里没有（或表还没载入）⇒ 兜底：`enemies.json` 里 kind=Boss 的这两个 id
            return enemyId == "boss" || enemyId == "moth";
        }

        /// <summary>BOSS 的显示名（`enemies.json` 的 name）；查不到就回退成 id。</summary>
        public static string BossNameOf(string enemyId)
        {
            EnemyDef[] all = TableDatabase.Enemies;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Id == enemyId && !string.IsNullOrEmpty(all[i].Name))
                    return all[i].Name;
            return enemyId;
        }

        // ══════════════════════════════════════════════════════════════════
        // 主链路：BOSS 死亡
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 一次击杀的**稳定身份**（`runKey` 由战斗侧给出：`{seed:x16}:w{波次}`）。
        /// 两个小时局里同一个 BOSS 死在同一处 ⇒ 视为同一次击杀（见 <see cref="TownDropLedger.KeyOf"/>）。
        /// </summary>
        public static string RunKeyOf(ulong runSeed, int waveIndex) => runSeed.ToString("x16") + ":w" + waveIndex;

        /// <summary>
        /// BOSS 死亡时调一次。返回抽到的种子下标（-1 = 没掉：不是 BOSS / 表坏了 / 这一击已经发过）。**永不抛**。
        ///
        /// ⚠️ 这个两参重载**不写盘**：它留给探针 / 自检（`ProbeV213`、`TownBootstrap`），
        ///    行为与 v213 逐字节一致（只加内存、只弹卡片）。**战斗里的真击杀走三参重载**。
        /// </summary>
        public static int OnBossKilled(string enemyId, Vector2 at) => OnBossKilled(enemyId, at, null);

        /// <summary>
        /// ★ v254：战斗里**真击杀**用的入口 —— 除了上面那些，还把这一颗**当场写进主城存档** ★
        /// `runKey` 为空 = 不落盘（等价于两参重载）；非空 = 这是真战斗击杀，必须落盘。
        /// 落盘的细节与失败模式全在 <see cref="TownDropLedger"/>。
        /// </summary>
        public static int OnBossKilled(string enemyId, Vector2 at, string runKey)
        {
            if (!IsBossId(enemyId)) return -1;
            if (!TryLoad()) return -1;

            // ★ 幂等：先认领"这一击"的身份；认领失败 = 同一击被重复上报 ⇒ 一颗都不许多发 ★
            //   （判据不是"计数涨了没有"，而是 runKey + 敌人 + 死亡坐标，见 `TownDropLedger.KeyOf`）
            //   ⚠️ 只对**战斗里的真击杀**（`runKey` 非空）生效：
            //      探针 / 自检入口（两参重载）的语义是"**调一次 = 打一次**"，
            //      `TownBootstrap.EconomyTour` 就是用同一坐标连打 N 次来造仓库的
            //      ⇒ 那条路上做去重会把"N 次击杀"压成 1 次，等于偷偷改了探针读数 ✗
            bool persist = !string.IsNullOrEmpty(runKey);
            string grantKey = TownDropLedger.KeyOf(runKey, enemyId, at);
            if (persist && !TownDropLedger.Begin(grantKey))
            {
                LastLine = $"跳过**重复上报**的击杀「{BossNameOf(enemyId)}」({enemyId} @ {at.x:0.0},{at.y:0.0})" +
                           $"：这一击已经发过（幂等键 {grantKey}）";
                Log.Warn("主城掉落：" + LastLine);
                return -1;
            }

            BossKills++;
            int idx = PickSeedIndex(BossKills);
            if (idx < 0)
            {
                if (persist) TownDropLedger.Abandon(grantKey);   // 抽样失败 = 这一击没成事 ⇒ 把认领退回
                return -1;
            }

            TownSeedDef seed = TownData.SeedAt(idx);
            int added = 0;
            long coins = 0;
            int have = 0;

            try
            {
                // ★ 先 `TownData.Load()` 再 `Ensure()` ★ 顺序反了会踩一个静默坑：
                //   `TownState.Initialize` 用 `TownData.Seeds.Length` 决定仓库数组长度，而
                //   `TownData.Seeds` 在 `Load()` 之前是**空数组** ⇒ 仓库会建成 1 格，
                //   于是 `AddSeed(3, 1)` 永远返回 0 —— 不报错、不掉落，只是"什么都没发生"✗
                TownData.Load();
                TownState state = TownState.Ensure();
                added = state.AddSeed(idx, 1);
                coins = state.Coins;
                have = idx < state.Warehouse.Length ? state.Warehouse[idx] : 0;
            }
            catch (Exception e)
            {
                Log.Error("主城掉落：入账失败（战斗继续，这次掉落丢了）：" + e.GetType().Name + "：" + e.Message);
            }

            LastSeedIndex = idx;
            LastRoll = RollOf(BossKills);
            LastAdded = added;
            if (added > 0) DroppedTotal += added;

            // ★★ v254：**当场落盘** —— 这是"击败 BOSS 就一定能带到主城"的那一步 ★★
            //   放在 `AddSeed` 之后、卡片之前：内存已经是 +1，这里把它写进当前存档位。
            //   写失败不抛、不回滚内存（内存那一颗仍然在，`TownDropLedger` 留成待发，读档/下次存档补发）✓
            TownDropLedger.Commit(grantKey, seed == null ? null : seed.id, added, persist);

            string seedName = seed == null ? "?" : (string.IsNullOrEmpty(seed.name) ? seed.id : seed.name);
            string grade = seed != null ? seed.grade : "?";
            string extra = added > 0
                ? $"主城仓库 +{added}（这种现在 {have} 颗，金币 {coins}）"
                : $"主城仓库已满（这种已 {TownState.WarehouseCap} 颗），这次没装下";

            LastLine = $"击败「{BossNameOf(enemyId)}」({enemyId} @ {at.x:0.0},{at.y:0.0}) ⇒ 掉落「{seedName}」"
                     + $"{grade} 档｜成熟 {seed?.GrowSeconds ?? 0f:0} 秒｜{seed?.GoldPerSecond ?? 0f:0.0} 金/秒"
                     + $"｜roll {LastRoll}/{TotalWeight}｜{extra}";
            Log.Info("★ " + LastLine);

            // 战斗里的"获得了什么"：卡片演出（编辑器非 play 环境下不建物体，只留读数）
            BossRewardPopup.Push(idx, added, BossNameOf(enemyId));
            return idx;
        }

        private static int RollOf(int killIndex)
        {
            int total = TotalWeight;
            if (total <= 0) return 0;
            unchecked { return (int)(BossSchedule.DerivedHash(killIndex * HashMul + HashAdd) % (uint)total); }
        }

        /// <summary>把状态清空（探针复位用；正常游戏里不调它）。</summary>
        public static void ResetRuntime()
        {
            BossKills = 0;
            DroppedTotal = 0;
            LastSeedIndex = -1;
            LastRoll = 0;
            LastAdded = 0;
            LastLine = "(已复位)";
        }

        // ══════════════════════════════════════════════════════════════════
        // 报告
        // ══════════════════════════════════════════════════════════════════

        /// <summary>权重表原文（报告/探针直接引用，与解析出来的 `_prefix` 同源）。</summary>
        public static string WeightTableText()
        {
            var sb = new System.Text.StringBuilder(512);
            if (!TryLoad())
            {
                sb.Append("掉落表没载入：").Append(LastError ?? "(未知)");
                return sb.ToString();
            }
            int total = TotalWeight;
            sb.Append("BOSS 掉落权重表（town_drops.json ⇒ 总权重 ").Append(total).Append("）\n");
            sb.Append("  品质  种子        权重   概率\n");
            for (int i = 0; i < _seedIndex.Length; i++)
            {
                TownSeedDef d = TownData.SeedAt(_seedIndex[i]);
                int w = i == 0 ? _prefix[0] : _prefix[i] - _prefix[i - 1];
                float p = total > 0 ? (float)w / total : 0f;
                sb.Append("  ").Append((d != null ? d.grade : "?").PadRight(5))
                  .Append((d != null ? d.name : "?").PadRight(10))
                  .Append(w.ToString().PadLeft(5)).Append("  ")
                  .Append((p * 100f).ToString("0.0")).Append("%\n");
            }
            // 品质合计（"低品质多、A 少"这条要能一眼读出来）
            var grades = new[] { "D", "C", "B", "A" };
            for (int g = 0; g < grades.Length; g++)
            {
                int w = 0, n = 0;
                for (int i = 0; i < _seedIndex.Length; i++)
                {
                    TownSeedDef d = TownData.SeedAt(_seedIndex[i]);
                    if (d == null || d.grade != grades[g]) continue;
                    w += i == 0 ? _prefix[0] : _prefix[i] - _prefix[i - 1];
                    n++;
                }
                sb.Append("  品质 ").Append(grades[g]).Append(" 合计：").Append(w).Append('/').Append(total)
                  .Append(" = ").Append(total > 0 ? (w * 100f / total).ToString("0.0") : "0").Append("%（")
                  .Append(n).Append(" 种）\n");
            }
            return sb.ToString();
        }

        /// <summary>一句话状态（探针 / HUD）。</summary>
        public static string Describe()
            => $"BOSS 击杀 {BossKills}｜掉落入账 {DroppedTotal} 颗｜最近：{LastLine}";
    }

    // ══════════════════════════════════════════════════════════════════════
    // BOSS 掉落的**落盘台账**（v254）
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ★★ v254：把"BOSS 掉的这一颗"写进**主城存档文件**的那一层 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么需要它（一句话）
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownState` 是**内存**里的静态对象；存档文件只有 `LoginBootstrap.SaveCurrent` 一个写者，
    /// 而它在战斗里**不在场**（进战斗是单场景加载，Login 场景连同 `LoginBootstrap` 一起销毁）。
    /// ⇒ 击杀那一帧 `AddSeed` 加的那一颗，**退出战斗回主城时会被 `TownSaveIO.Restore` 用
    ///   进战斗前那份存档覆盖掉** ⇒ 玩家看到"打了 BOSS 却没拿到种子"✗
    /// ⇒ 本类负责：**击杀当场**把这一颗并进当前存档位（而不是等退出），
    ///   于是正常退出 / 主动退回主城 / 死亡 / Alt+F4 四条路径都**不依赖退出时补一笔** ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、写哪一份、怎么写（**不猜位号、不用 `Capture`**）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 位号：`LoginBootstrap.ActiveSlot` —— 它是跨场景可读的**当前存档位**（`LoginBootstrap.cs:76`）。
    ///     位号未知（`-1`，例如直接从 `SeedfallArena` 场景启动）时**不猜**（猜一个号会覆盖玩家别的档，
    ///     那是比"不存档"坏得多的失败）；这一颗留在**待发队列**里，等有主城存档时补发 ✓
    ///   · 写法：`SaveStore.Read` 读出**整份** → 只改"这一种种子 +1 颗"和 `bossSeedGrants` 一个键
    ///     → `SaveStore.Write`（内部 `tmp` + `Move` 原子替换）。
    ///     为什么不用 `TownSaveIO.Capture`：它从**内存** `TownState` 重建整份存档，
    ///     会把 `items` / `skins` 这些不属于 `TownState` 的字段按"开局默认值"重写 ⇒ 那是另一个 bug ✗
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、幂等（同一次击杀只发一次）
    /// ══════════════════════════════════════════════════════════════════════
    /// 每一击有一个**幂等键**：`{runKey}#{敌人id}@{死亡坐标×100}`（见 <see cref="KeyOf"/>）。
    /// 键与那一颗种子**写在同一个文件、同一次原子写**里（`TownSaveData.bossSeedGrants`）⇒
    ///   · 磁盘上有键 ⇔ 磁盘上有那颗种子（不存在"记了账没给货"的中间态）
    ///   · 写盘失败 ⇒ 不进磁盘、留在待发队列（内存里那一颗仍然在）
    ///   · 同一击被重复上报 ⇒ <see cref="Begin"/> 返回 false ⇒ 一颗都不多发
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、失败与补发（全部留痕，不静默）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 写盘失败 / 位号未知 ⇒ `Log.Warn` + `LastError`，这一颗进**待发队列**；
    ///   · 回主城读档后（<see cref="AfterRestore"/>）：磁盘上没有的待发项被**重新加回内存**并再试一次写盘；
    ///   · 下一次主城存档（`TownSaveIO.Capture` → <see cref="CaptureTo"/>）：待发项的幂等键随存档一起落盘
    ///     （内存里那一颗本来就在捕获范围内）⇒ 只要主城存过一次档，这一颗一定在盘上 ✓
    /// </summary>
    public static class TownDropLedger
    {
        /// <summary>存档里最多留多少个幂等键（防止无限膨胀；只留最近的）。</summary>
        public const int MaxKeysInSave = 256;

        private const string Tag = "[掉落台账] ";

        /// <summary>台账里的一条：一次击杀要发的那一颗。</summary>
        private sealed class Grant
        {
            public string Key;        // 幂等键
            public string SeedId;     // 种子的 id（按 id 记，不按表内下标 —— 表一改，下标就串了）
            public int Count;         // 这一击真正加进内存仓库的颗数（0 = 仓库满，没装下）
            public bool OnDisk;       // 已经落盘（或压根不需要落盘）
            public bool InState;      // 内存 `TownState` 里已经有它
        }

        private static readonly Dictionary<string, Grant> _grants = new Dictionary<string, Grant>();

        /// <summary>本进程认过的击杀次数（探针读数）。</summary>
        public static int GrantedCount { get; private set; }

        /// <summary>真正写进存档文件的颗数（探针读数）。</summary>
        public static int WrittenCount { get; private set; }

        /// <summary>还没落盘的条数（> 0 表示"内存有、盘上还没有"）。</summary>
        public static int PendingCount
        {
            get
            {
                int n = 0;
                foreach (Grant g in _grants.Values) if (!g.OnDisk) n++;
                return n;
            }
        }

        /// <summary>最近一次落盘的一句话（探针 / 日志）。</summary>
        public static string LastPersist { get; private set; } = "(还没写过)";

        /// <summary>最近一次失败原因（成功时为 null）。</summary>
        public static string LastError { get; private set; }

        // ══════════════════════════════════════════════════════════════════
        // 幂等键
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 一次击杀的幂等键：`{runKey}#{enemyId}@{x*100},{y*100}`。
        /// `runKey` 由战斗侧给（`TownDrops.RunKeyOf(seed, 波次)`）⇒ **跨两局不会撞**；
        /// 同一次击杀被重复上报时三个分量都一样 ⇒ 撞上 ⇒ 判为同一击 ✓
        /// 坐标取到两位小数：同一个 BOSS 的死亡位置在重放里是**逐位相同**的（固定步长 + 同种子）。
        /// （同一局同一波里同种 BOSS 恰好死在同 0.01 位置 ⇒ 会被当成同一击，此时会 `Log.Warn` 出来，
        ///   不静默。）
        /// </summary>
        public static string KeyOf(string runKey, string enemyId, Vector2 at)
        {
            string run = string.IsNullOrEmpty(runKey) ? "norun" : runKey;
            int x = Mathf.RoundToInt(at.x * 100f);
            int y = Mathf.RoundToInt(at.y * 100f);
            return run + "#" + (enemyId ?? "?") + "@" + x.ToString() + "," + y.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // 生命周期：认领 → 入账 → 落盘
        // ══════════════════════════════════════════════════════════════════

        /// <summary>认领这一击。返回 false = 这一击**已经发过**（幂等拒绝，调用方必须就此打住）。</summary>
        public static bool Begin(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_grants.ContainsKey(key)) return false;
            _grants[key] = new Grant { Key = key, OnDisk = false, InState = false };
            GrantedCount++;
            return true;
        }

        /// <summary>退掉一次认领（抽样/建表失败 ⇒ 这一击什么都没成，可以重来）。</summary>
        public static void Abandon(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_grants.Remove(key) && GrantedCount > 0) GrantedCount--;
        }

        /// <summary>
        /// 入账完成（内存里已经 +<paramref name="added"/> 颗）。
        /// <paramref name="persist"/> = true 时**立刻**写进存档（战斗里真击杀走这条路）。
        /// </summary>
        public static void Commit(string key, string seedId, int added, bool persist)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_grants.TryGetValue(key, out Grant g))
            {
                g = new Grant { Key = key };
                _grants[key] = g;
            }
            g.SeedId = seedId ?? "";
            g.Count = added < 0 ? 0 : added;
            g.InState = true;

            if (!persist)
            {
                // 探针 / 自检入口：行为与 v213 一致（只加内存）。这一条不需要落盘 ⇒ 就地结账。
                g.OnDisk = true;
                LastPersist = "内存（非战斗入口，不写盘）";
                return;
            }
            Flush("击杀当场");
        }

        // ══════════════════════════════════════════════════════════════════
        // 落盘
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把**所有待发**的击杀写进当前存档位。返回 true = 现在没有待发项了。
        /// 幂等：磁盘上已有同一个键的，只结账、不重复加种子。**永不抛**。
        /// </summary>
        public static bool Flush(string reason)
        {
            try
            {
                var pending = new List<Grant>();
                foreach (Grant g in _grants.Values) if (!g.OnDisk) pending.Add(g);
                if (pending.Count == 0) return true;

                int slot = LoginBootstrap.ActiveSlot;
                if (slot < 0)
                {
                    LastError = "当前存档位未知（LoginBootstrap.ActiveSlot = -1）⇒ 这一颗留在待发队列";
                    LastPersist = reason + "：跳过落盘（" + LastError + "）";
                    Log.Warn(Tag + LastPersist);
                    return false;
                }
                if (!SaveStore.Read(slot, out TownSaveData data, out string readError) || data == null)
                {
                    LastError = "读不出存档位 " + (slot + 1) + "：" + (readError ?? "内容为空");
                    LastPersist = reason + "：跳过落盘（" + LastError + "）";
                    Log.Warn(Tag + LastPersist);
                    return false;
                }

                // 磁盘上的键 = **已经发过的**（权威真值源）
                var keys = new List<string>(data.bossSeedGrants ?? Array.Empty<string>());
                var toMark = new List<Grant>();
                var written = new List<Grant>();
                int added = 0;

                for (int i = 0; i < pending.Count; i++)
                {
                    Grant g = pending[i];
                    if (keys.Contains(g.Key)) { toMark.Add(g); continue; }   // 盘上已经记账 ⇒ 不再发第二次
                    if (g.Count <= 0) { toMark.Add(g); continue; }           // 没有颗数要写（仓库满 / 表坏）
                    int n = ApplyToSave(data, g.SeedId, g.Count);
                    if (n <= 0)
                    {
                        Log.Warn(Tag + $"存档里加不进「{g.SeedId}」（这种已满 {TownState.WarehouseCap} 颗或 id 不在表里）" +
                                       $" ⇒ 这一次击杀按「没装下」结账（{g.Key}）");
                        toMark.Add(g);
                        continue;
                    }
                    keys.Add(g.Key);
                    toMark.Add(g);
                    written.Add(g);
                    added += n;
                }

                if (written.Count == 0)
                {
                    for (int i = 0; i < toMark.Count; i++) toMark[i].OnDisk = true;
                    LastPersist = reason + "：没有新颗数需要写（" + pending.Count + " 条已结账）";
                    LastError = null;
                    return PendingCount == 0;
                }

                data.bossSeedGrants = TrimKeys(keys);
                if (!SaveStore.Write(slot, data, out string writeError))
                {
                    LastError = "写存档位 " + (slot + 1) + " 失败：" + writeError;
                    LastPersist = reason + "：落盘失败（" + LastError + "）⇒ 这一颗留在待发队列";
                    Log.Error(Tag + LastPersist);
                    return false;
                }

                // 写成功 ⇒ 这一批**全部结账**：`toMark` 里既有刚写进去的，
                // 也有"盘上早已有键"和"仓库满/没颗数可写"的 —— 它们都不再需要重试 ✓
                for (int i = 0; i < toMark.Count; i++) toMark[i].OnDisk = true;
                WrittenCount += added;
                LastError = null;
                LastPersist = $"{reason} ⇒ 存档位 {slot + 1} 已写入 +{added} 颗（{written.Count} 次击杀，幂等键进 bossSeedGrants）";
                Log.Info(Tag + LastPersist);
                return PendingCount == 0;
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + "：" + e.Message;
                LastPersist = reason + "：落盘抛异常（" + LastError + "）⇒ 这一颗留在待发队列";
                Log.Error(Tag + LastPersist);
                return false;
            }
        }

        /// <summary>
        /// 往**存档数据**里加 <paramref name="count"/> 颗 <paramref name="seedId"/>。
        /// 与 `TownSaveIO.Capture` 同一套口径：`plants`/`plantCounts`（id 为准）是真相源，
        /// `warehouse` 是位置镜像（长度对不上就不动它）。返回真正加进去的颗数。
        /// </summary>
        private static int ApplyToSave(TownSaveData data, string seedId, int count)
        {
            if (data == null || string.IsNullOrEmpty(seedId) || count <= 0) return 0;
            TownData.Load();                                   // id → 下标要用它
            if (TownData.IndexOf(seedId) < 0)
            {
                Log.Warn(Tag + "掉落表里的种子 " + seedId + " 不在当前 town_seeds.json 里 ⇒ 存档里加不进去");
                return 0;
            }

            var ids = new List<string>(data.plants ?? Array.Empty<string>());
            var counts = new List<int>(data.plantCounts ?? Array.Empty<int>());
            while (counts.Count < ids.Count) counts.Add(0);     // 两份数组长度必须一致（schema 约定）

            int at = ids.IndexOf(seedId);
            int have = at >= 0 ? counts[at] : 0;
            int room = TownState.WarehouseCap - have;
            if (room <= 0) return 0;
            int add = Mathf.Min(room, count);
            if (at >= 0) counts[at] = have + add;
            else { ids.Add(seedId); counts.Add(add); }

            data.plants = ids.ToArray();
            data.plantCounts = counts.ToArray();

            int idx = TownData.IndexOf(seedId);
            if (data.warehouse != null && idx >= 0 && idx < data.warehouse.Length)
                data.warehouse[idx] = Mathf.Min(TownState.WarehouseCap, data.warehouse[idx] + add);
            return add;
        }

        // ══════════════════════════════════════════════════════════════════
        // 与存档的两个挂钩（`TownSaveIO.Capture` / `TownSaveIO.Restore`）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 存档**写出前**：把"内存里已经发过、但盘上还没有"的幂等键补进这份存档。
        /// 只补键、不补颗数 —— 内存仓库是 `TownSaveIO.Capture` 的捕获源，那一颗本来就在里面 ✓
        /// （`InState == false` 的条目不写键：存档不能声称"发过一颗"而仓库里没有它）
        /// </summary>
        public static void CaptureTo(TownSaveData data)
        {
            if (data == null) return;
            try
            {
                var keys = new List<string>(data.bossSeedGrants ?? Array.Empty<string>());
                foreach (Grant g in _grants.Values)
                {
                    if (!g.InState) continue;
                    // 非战斗入口（探针/自检）认领的键不进存档：它们从来没有"需要跨局去重"的语义，
                    // 写进去只会让存档里堆一串 `norun#...` 的噪声 ✓
                    if (g.Key.StartsWith("norun#", StringComparison.Ordinal)) continue;
                    if (keys.Contains(g.Key)) continue;
                    keys.Add(g.Key);
                }
                data.bossSeedGrants = TrimKeys(keys);
            }
            catch (Exception e)
            {
                LastError = "写幂等键失败：" + e.GetType().Name + "：" + e.Message;
                Log.Error(Tag + LastError);
            }
        }

        /// <summary>
        /// 读档**之后**：以磁盘上的 `bossSeedGrants` 为准重建台账，并把"盘上没有的"（写盘失败过的）
        /// 那一颗**重新加回内存仓库**，然后再试一次落盘。**永不抛**。
        ///
        /// 顺序很重要：`TownSaveIO.Restore` 刚把仓库**整份覆盖**成磁盘内容 ⇒
        /// 内存里"加了但没来得及写盘"的那一颗此刻**已经不在内存里了**，所以这里必须补回。
        /// </summary>
        public static void AfterRestore(TownSaveData data)
        {
            try
            {
                var keep = new List<Grant>();
                foreach (Grant g in _grants.Values) if (!g.OnDisk) keep.Add(g);
                _grants.Clear();

                string[] disk = data != null && data.bossSeedGrants != null
                    ? data.bossSeedGrants : Array.Empty<string>();
                for (int i = 0; i < disk.Length; i++)
                {
                    string k = disk[i];
                    if (string.IsNullOrEmpty(k) || _grants.ContainsKey(k)) continue;
                    _grants[k] = new Grant { Key = k, OnDisk = true, InState = true };
                }

                TownState state = TownState.Current;
                int reapplied = 0;
                for (int i = 0; i < keep.Count; i++)
                {
                    Grant g = keep[i];
                    if (_grants.ContainsKey(g.Key)) continue;          // 盘上已经记账 ⇒ 仓库里也有了
                    g.OnDisk = false;
                    g.InState = true;
                    if (state != null && g.Count > 0)
                    {
                        int idx = TownData.IndexOf(g.SeedId);
                        if (idx >= 0)
                        {
                            int n = state.AddSeed(idx, g.Count);        // 内部会 Recompute
                            if (n > 0) reapplied += n;
                        }
                    }
                    _grants[g.Key] = g;
                }

                if (reapplied > 0)
                {
                    LastPersist = "读档后补发：内存仓库 +" + reapplied + " 颗（这几颗上次没来得及写盘）";
                    Log.Warn(Tag + LastPersist);
                }
                if (PendingCount > 0) Flush("读档后补发");
            }
            catch (Exception e)
            {
                LastError = "读档后补发失败：" + e.GetType().Name + "：" + e.Message;
                Log.Error(Tag + LastError);
            }
        }

        /// <summary>清空台账（新游戏 / 探针复位用）。</summary>
        public static void ResetAll()
        {
            _grants.Clear();
            GrantedCount = 0;
            WrittenCount = 0;
            LastError = null;
            LastPersist = "(已复位)";
        }

        private static string[] TrimKeys(List<string> keys)
        {
            if (keys.Count <= MaxKeysInSave) return keys.ToArray();
            return keys.GetRange(keys.Count - MaxKeysInSave, MaxKeysInSave).ToArray();
        }

        /// <summary>一句话状态（探针 / 日志）。</summary>
        public static string Describe()
            => $"击杀认领 {GrantedCount}｜已落盘 {WrittenCount} 颗｜待发 {PendingCount} 条" +
               $"｜存档位 {(LoginBootstrap.ActiveSlot < 0 ? "未知" : (LoginBootstrap.ActiveSlot + 1).ToString())}" +
               $"｜最近：{LastPersist}" + (string.IsNullOrEmpty(LastError) ? "" : "｜错误：" + LastError);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 表结构（字段名必须与 JSON 逐字一致 —— JsonUtility 按字段名匹配）
    // ══════════════════════════════════════════════════════════════════════

    [Serializable]
    public sealed class TownDropTable
    {
        public BossSeedDrop[] bossSeeds;
    }

    /// <summary>掉落表的一条：某种主城种子在一轮抽样里的相对权重。</summary>
    [Serializable]
    public sealed class BossSeedDrop
    {
        public string seedId = "";
        public int weight = 1;
    }
}
