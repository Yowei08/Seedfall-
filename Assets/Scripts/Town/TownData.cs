using System;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城（Town）的数据层 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么主城自己开一张种子表，而不是复用 `Data/seeds.json` / `Flow/ShopPool.cs`
    /// ══════════════════════════════════════════════════════════════════════
    /// ① **语义不同**：战斗侧的 `seeds.json` 是「会参战的植物」（含水耗、机制、收益按格结算），
    ///    主城的种子只是「一块地上的提款机 + 一个成熟计时器」—— 两套字段没有一个是共用的
    ///    （主城只有成熟秒 / 金币每秒 / 等级 / 回收价几个数 + 一个展示色）。
    /// ② **隔离**：另一路正在逐条校正 `seeds.json` 的描述文本，主城若引用它，
    ///    任何一次校对都可能把主城拖红（甚至只是改一句 `_note`）。
    /// ③ **不越界**：主城**不许**改战斗内容 ⇒ 连"读战斗表"这条耦合都掐掉，
    ///    主城才能在战斗侧继续演进时保持零风险 ✓
    ///
    /// ⇒ 结论：只有一张 `Resources/Data/town_seeds.json`，id 一律 `town_` 前缀，
    ///   **与战斗表无一处共享** ✓
    ///   （v214 起商店不再有"商品表"：商店卖的是**解锁**，那张表是 `Data/town_unlocks.json`
    ///     且由 `TownUnlocks` 自己载入 —— 与种子表彻底分家 ✓）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、失败模式（**不静默**）
    /// ══════════════════════════════════════════════════════════════════════
    /// 表缺失 / 解析失败 / id 重复 / 数值非正 —— 全部 `Log.Error` 并抛出。
    /// 理由：主城的全部玩法都由这张表驱动（田里种什么、每秒给多少钱、回收价多少），
    /// 表坏了就没有"降级运行"这回事；抛出来比悄悄跑一个空城好查一万倍 ✓
    /// 唯一**不抛**的是「种子数量不是 8」：数量由表决定，代码里没有写死 8 的地方 ✓
    /// </summary>
    public static class TownData
    {
        public const string SeedTablePath = "Data/town_seeds";

        /// <summary>主城种子（顺序 = 仓库槽位顺序 = HUD 行顺序 = 小地图色号顺序）。</summary>
        public static TownSeedDef[] Seeds { get; private set; } = Array.Empty<TownSeedDef>();

        public static bool Ready { get; private set; }

        /// <summary>加载失败时的原文（HUD 直接显示它，而不是显示一张空城）。</summary>
        public static string LastError { get; private set; }

        public static void Load()
        {
            if (Ready) return;

            TownSeedTable seedTable = LoadJson<TownSeedTable>(SeedTablePath);

            if (seedTable.seeds == null || seedTable.seeds.Length == 0)
                throw Fail("town_seeds.json 的 seeds 数组为空");

            var seeds = new TownSeedDef[seedTable.seeds.Length];
            for (int i = 0; i < seedTable.seeds.Length; i++)
            {
                TownSeedDef s = seedTable.seeds[i];
                if (s == null) throw Fail($"town_seeds.json 第 {i} 条为 null");
                if (string.IsNullOrWhiteSpace(s.id)) throw Fail($"town_seeds.json 第 {i} 条缺少 id");
                for (int j = 0; j < i; j++)
                    if (seeds[j].id == s.id) throw Fail($"town_seeds.json 的 id 重复：{s.id}");
                if (s.growSeconds <= 0f) throw Fail($"种子 {s.id} 的 growSeconds 必须 > 0（现 {s.growSeconds}）");
                if (s.goldPerSecond <= 0f) throw Fail($"种子 {s.id} 的 goldPerSecond 必须 > 0（现 {s.goldPerSecond}）");
                if (s.level < 1 || s.level > 3) throw Fail($"种子 {s.id} 的 level 必须在 1~3（现 {s.level}）");
                if (s.sellPrice <= 0) throw Fail($"种子 {s.id} 的 sellPrice（回收价）必须 > 0（现 {s.sellPrice}）");
                if (string.IsNullOrWhiteSpace(s.name)) s.name = s.id;
                if (string.IsNullOrWhiteSpace(s.grade)) s.grade = "D";
                if (s.size <= 0f) s.size = 0.5f;
                seeds[i] = s;
            }

            Seeds = seeds;
            Ready = true;
            LastError = null;
        }

        /// <summary>种子 id → 索引；找不到返回 -1（**不抛** —— 调用方自己决定是报错还是忽略）。</summary>
        public static int IndexOf(string seedId)
        {
            if (string.IsNullOrEmpty(seedId)) return -1;
            for (int i = 0; i < Seeds.Length; i++)
                if (Seeds[i].id == seedId) return i;
            return -1;
        }

        public static TownSeedDef SeedAt(int index)
            => index >= 0 && index < Seeds.Length ? Seeds[index] : null;

        /// <summary>8 种颜色的解析缓存 —— 同一个 id 只解析一次，别在 HUD 的逐帧路径里反复 `ColorUtility`。</summary>
        private static Color[] _tints;

        public static Color TintAt(int index)
        {
            if (_tints == null || _tints.Length != Seeds.Length)
            {
                _tints = new Color[Seeds.Length];
                for (int i = 0; i < Seeds.Length; i++) _tints[i] = Seeds[i].Tint;
            }
            if (index < 0 || index >= _tints.Length) return new Color(0.55f, 0.55f, 0.55f);
            return _tints[index];
        }

        private static T LoadJson<T>(string resourcePath) where T : class
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
                throw Fail($"缺少配置表 Assets/Resources/{resourcePath}.json");
            try
            {
                T obj = JsonUtility.FromJson<T>(asset.text);
                if (obj == null) throw new InvalidOperationException("解析结果为空");
                return obj;
            }
            catch (Exception e)
            {
                throw Fail($"{resourcePath}.json 解析失败：{e.Message}");
            }
        }

        private static InvalidOperationException Fail(string msg)
        {
            LastError = msg;
            Log.Error("主城配置表：" + msg);
            return new InvalidOperationException(msg);
        }

        // ══════════════════════════════════════════════════════════════════
        // 表结构（字段名必须与 JSON 逐字一致 —— JsonUtility 按字段名匹配）
        // ══════════════════════════════════════════════════════════════════

        [Serializable]
        public sealed class TownSeedTable
        {
            public TownSeedDef[] seeds;
        }
    }

    /// <summary>
    /// 一种主城种子。三个玩法数值：<see cref="growSeconds"/> / <see cref="goldPerSecond"/> / <see cref="level"/>。
    /// 其余字段是展示与表现（name / grade / color / size），**均不参与算式**。
    /// </summary>
    [Serializable]
    public sealed class TownSeedDef
    {
        public string id = "";
        public string name = "";
        public string grade = "D";
        public int level = 1;
        public float growSeconds = 20f;
        public float goldPerSecond = 1f;

        /// <summary>
        /// ★ v214：**回收价**（每颗多少主城金币）—— 商店的「收种子」分页用它 ★
        /// 用户原话：「商店可以让玩家把不要的主城种子换金币」。
        /// 这个数**只在一个地方用**（`TownState.SellSeed`），不参与产金、不参与成熟。
        /// 口径：取"如果它还卖，卖价"的 30~50%（见 `town_seeds.json` 的 `_note_回收价`）✓
        /// </summary>
        public int sellPrice = 1;

        public string color = "#FFFFFF";
        public float size = 0.5f;

        /// <summary>成熟时间（秒）。表里必须 > 0，这里再夹一层兜底防止运行期被人改成 0 后除零。</summary>
        public float GrowSeconds => Mathf.Max(0.5f, growSeconds);

        public float GoldPerSecond => Mathf.Max(0f, goldPerSecond);

        /// <summary>回收价（≥ 1，防止表里写 0 变成"白送"）。</summary>
        public int SellPrice => Mathf.Max(1, sellPrice);

        /// <summary>成熟进度 0~1（给 HUD 的进度条与小地图用；不做任何结算）。</summary>
        public float Progress(double elapsed) => Mathf.Clamp01((float)(elapsed / GrowSeconds));

        public bool IsMature(double elapsed) => elapsed >= GrowSeconds;

        /// <summary>`color` 字段（#RRGGBB）→ Color。解析失败退回中性灰（**不抛** —— 颜色只影响观感）。</summary>
        public Color Tint
        {
            get
            {
                if (ColorUtility.TryParseHtmlString(color, out Color c)) return c;
                return new Color(0.55f, 0.55f, 0.55f);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // v214：商店的商品**不再由本文件管**
    // ══════════════════════════════════════════════════════════════════════
    // 上一版这里还有一个 `TownShopItem`（卖种子 / 卖道具插件成品）。用户这一轮把商店的
    // **真实职责**讲清了：「商店卖的是道具、插件、植物，**买了之后才会在战斗刷新这些**」＋
    // 「商店可以让玩家把不要的主城种子换金币」。
    // ⇒ 商店现在是「解锁表 + 种子回收」，两者的数据形状都与种子表无关，各自独立：
    //   · 解锁表 → `Data/town_unlocks.json` + `TownUnlocks`（含对战斗侧的只读口 `IsUnlocked`）
    //   · 回收价 → 就在本文件的 `TownSeedDef.sellPrice`（一颗种子一个价，放这里最自然）
    // 本类于是回到它最初的样子：**只负责主城种子这一张表** ✓
}
