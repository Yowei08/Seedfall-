using System;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 家 → **图鉴** 的数据层：把"已经解锁的植物 / 道具 / 插件"整理成分页条目 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、用户要什么
    /// ══════════════════════════════════════════════════════════════════════
    /// 「左侧增加功能性建筑（家），互动后展示**图鉴**（**已经解锁的**植物、道具、插件，
    ///   **全部详细介绍**，**分页展示**，还有"**成就**"，先留空）」
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、为什么去读战斗那三张表（**只读**）
    /// ══════════════════════════════════════════════════════════════════════
    /// "全部详细介绍"这件事的信息量在**战斗侧的表**里：`Data/items.json` 的 `desc`、
    /// `Data/plugins.json` 的 `description`、`Data/seeds.json` 的数值列。
    /// 把这 152 条文本抄进主城自己的表 = 同一个东西两处维护，改一边另一边就过时 ✗
    /// ⇒ 主城**只读**它们（`Resources.Load<TextAsset>` + `JsonUtility`），一个字段都不写回 ✓
    ///   ⚠️ 惩罚与边界：那三张表归另一路，字段名可能变。所以——**任何一张读失败都不崩**：
    ///   该页退化成"只用 `TownUnlocks` 里有的名字 + 一句说明"，并把原因写进 `LastError` ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、"已经解锁"的判据
    /// ══════════════════════════════════════════════════════════════════════
    /// 只有一个：`TownUnlocks.IsUnlocked(id)`（老货默认 True、新货买了才 True）✓
    /// ⇒ 图鉴与商店共用同一份真相，不会出现"商店说解锁了、图鉴里没有"✓
    /// </summary>
    public static class TownCodex
    {
        /// <summary>图鉴的四页。</summary>
        public enum Page
        {
            Plants = 0,
            Items = 1,
            Plugins = 2,
            Achievements = 3,
        }

        /// <summary>每页显示几条（面板高度按这个数排的；改它要同时改 `TownHud.DrawCodex` 的行高）。</summary>
        public const int PageSize = 4;

        /// <summary>一条图鉴条目。</summary>
        public struct Entry
        {
            public string Id;
            public string Name;
            public string Detail;
        }

        public static Entry[] Plants { get; private set; } = Array.Empty<Entry>();
        public static Entry[] Items { get; private set; } = Array.Empty<Entry>();
        public static Entry[] Plugins { get; private set; } = Array.Empty<Entry>();

        /// <summary>
        /// ★ v237：**id → 一句话说明**（三张战斗表的原文），**不按解锁过滤**。
        /// 为什么要这一份：主城商店要在一行里说清"这条东西到底是什么"，而商店列的条目
        /// 恰恰**包含还没解锁的**（那批在图鉴里当然查不到）⇒ 图鉴那份 `Entry[]` 不够用 ✓
        /// 唯一的写法是在**建表的时候**顺手存一份全量（不过滤），图鉴自己那份仍然只收已解锁的 ✓
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> Details =
            new System.Collections.Generic.Dictionary<string, string>(192);

        /// <summary>取一条的说明（**任何 id 都能问**：没登记 / 表没读到 ⇒ 返回 null）。</summary>
        public static string DetailOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (!Ready) Load();      // 面板比 `TownBootstrap.Awake` 先画的极端情况：就地补读一次（`Load` 不递归回来）
            return Details.TryGetValue(id, out string d) ? d : null;
        }

        public static bool Ready { get; private set; }

        /// <summary>载入过程中的失败/降级原因（面板上会显示一行小字）。</summary>
        public static string LastError { get; private set; }

        public static int CountOf(Page page)
        {
            switch (page)
            {
                case Page.Plants: return Plants.Length;
                case Page.Items: return Items.Length;
                case Page.Plugins: return Plugins.Length;
                default: return 0;
            }
        }

        /// <summary>这一页有几页（至少 1 页，免得"第 1/0 页"）。</summary>
        public static int PageCount(Page page)
        {
            int n = CountOf(page);
            return Mathf.Max(1, (n + PageSize - 1) / PageSize);
        }

        /// <summary>取这一页的第 <paramref name="row"/> 条；越界返回 false。</summary>
        public static bool TryGet(Page page, int pageIndex, int row, out Entry entry)
        {
            entry = default;
            Entry[] src = page == Page.Plants ? Plants : page == Page.Items ? Items : page == Page.Plugins ? Plugins : null;
            if (src == null) return false;
            int i = pageIndex * PageSize + row;
            if (i < 0 || i >= src.Length) return false;
            entry = src[i];
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // 载入（只读三张表 + 用 IsUnlocked 过滤）
        // ══════════════════════════════════════════════════════════════════
        public static void Load()
        {
            if (Ready) return;
            LastError = null;
            var err = new System.Text.StringBuilder(160);

            Plants = BuildPlants(err);
            Items = BuildItems(err);
            Plugins = BuildPlugins(err);

            Ready = true;
            if (err.Length > 0) LastError = err.ToString();
            Log.Info($"[Town] 图鉴已载入：植物 {Plants.Length} / 道具 {Items.Length} / 插件 {Plugins.Length}" +
                     (LastError != null ? "（降级：" + LastError + "）" : ""));
        }

        /// <summary>清掉缓存（重开一局 / 自动化用例之间）。</summary>
        public static void Reset()
        {
            Ready = false;
            Plants = Array.Empty<Entry>();
            Items = Array.Empty<Entry>();
            Plugins = Array.Empty<Entry>();
            Details.Clear();
            LastError = null;
        }

        private static Entry[] BuildPlants(System.Text.StringBuilder err)
        {
            SeedTable t = TryLoad<SeedTable>("Data/seeds", err);
            if (t == null || t.seeds == null) return FromUnlocks(TownUnlockDef.KindPlant);

            var list = new System.Collections.Generic.List<Entry>(32);
            for (int i = 0; i < t.seeds.Length; i++)
            {
                SeedRow s = t.seeds[i];
                if (s == null || string.IsNullOrEmpty(s.id)) continue;
                string detail = $"品质 {s.grade}｜成熟 {s.growTime:0.#} 秒｜收成 金币 {s.goldYield} / 经验 {s.expYield}" +
                                $"｜种子售价 {s.price}" +
                                (string.IsNullOrEmpty(s.mechanic) || s.mechanic == "None" ? "" : "｜机制 " + s.mechanic);
                Details[s.id] = detail;                      // ★ v237：全量登记（含未解锁的）
                if (!TownUnlocks.IsUnlocked(s.id)) continue;
                list.Add(new Entry
                {
                    Id = s.id,
                    Name = string.IsNullOrEmpty(s.name) ? s.id : s.name,
                    Detail = detail,
                });
            }
            return list.ToArray();
        }

        private static Entry[] BuildItems(System.Text.StringBuilder err)
        {
            ItemTable t = TryLoad<ItemTable>("Data/items", err);
            if (t == null || t.items == null) return FromUnlocks(TownUnlockDef.KindItem);

            var list = new System.Collections.Generic.List<Entry>(96);
            for (int i = 0; i < t.items.Length; i++)
            {
                ItemRow s = t.items[i];
                if (s == null || string.IsNullOrEmpty(s.id)) continue;
                string detail = string.IsNullOrEmpty(s.desc)
                    ? $"品质 {s.grade}｜售价 {s.price}｜类别 {s.category}"
                    : s.desc;
                Details[s.id] = detail;                      // ★ v237：全量登记（含未解锁的）
                if (!TownUnlocks.IsUnlocked(s.id)) continue;
                list.Add(new Entry
                {
                    Id = s.id,
                    Name = string.IsNullOrEmpty(s.name) ? s.id : s.name,
                    Detail = detail,
                });
            }
            return list.ToArray();
        }

        private static Entry[] BuildPlugins(System.Text.StringBuilder err)
        {
            PluginTable t = TryLoad<PluginTable>("Data/plugins", err);
            if (t == null || t.plugins == null) return FromUnlocks(TownUnlockDef.KindPlugin);

            var list = new System.Collections.Generic.List<Entry>(40);
            for (int i = 0; i < t.plugins.Length; i++)
            {
                PluginRow s = t.plugins[i];
                if (s == null || string.IsNullOrEmpty(s.id)) continue;
                string detail = string.IsNullOrEmpty(s.description)
                    ? $"品质 {s.quality}｜基准价 {s.basePrice}｜可叠 {s.maxStack} 层"
                    : s.description;
                // ★ v237：插件表里新加的那批带 `nameEn`（英文名）⇒ 图鉴/商店的说明行把它摆在最前面，
                //   "英文名 + 中文效果"一行读完（旧的 36 条没有这个键 ⇒ 不会多出前缀）✓
                if (!string.IsNullOrEmpty(s.nameEn)) detail = "[" + s.nameEn + "] " + detail;
                Details[s.id] = detail;                      // ★ v237：全量登记（含未解锁的）
                if (!TownUnlocks.IsUnlocked(s.id)) continue;
                list.Add(new Entry
                {
                    Id = s.id,
                    Name = string.IsNullOrEmpty(s.displayName) ? s.id : s.displayName,
                    Detail = detail,
                });
            }
            return list.ToArray();
        }

        /// <summary>战斗表读不到时的退路：只用解锁表里那批（名字 + 一句说明），保证图鉴不会空白。</summary>
        private static Entry[] FromUnlocks(string kind)
        {
            var list = new System.Collections.Generic.List<Entry>(32);
            for (int i = 0; i < TownUnlocks.Count; i++)
            {
                TownUnlockDef d = TownUnlocks.At(i);
                if (d.kind != kind || !TownUnlocks.IsUnlocked(d.id)) continue;
                list.Add(new Entry
                {
                    Id = d.id,
                    Name = d.name,
                    Detail = $"（详细说明要读战斗表 Data/*.json，本次没读到）主城解锁价 {d.price} 金" +
                             (d.onSale ? "" : " · 暂未上架"),
                });
            }
            return list.ToArray();
        }

        private static T TryLoad<T>(string resourcePath, System.Text.StringBuilder err) where T : class
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                err.Append(resourcePath).Append(" 缺失；");
                return null;
            }
            try
            {
                T obj = JsonUtility.FromJson<T>(asset.text);
                if (obj == null) err.Append(resourcePath).Append(" 解析为空；");
                return obj;
            }
            catch (Exception e)
            {
                err.Append(resourcePath).Append(" 解析失败：").Append(e.Message).Append('；');
                return null;
            }
        }

        // ── 三张表的"只取我要的字段"的 DTO（字段名必须与 JSON 逐字一致）──

        [Serializable] public sealed class SeedTable { public SeedRow[] seeds; }
        [Serializable]
        public sealed class SeedRow
        {
            public string id = "";
            public string name = "";
            public string grade = "";
            public string mechanic = "";
            public float growTime = 0f;
            public int goldYield = 0;
            public int expYield = 0;
            public int price = 0;
        }

        [Serializable] public sealed class ItemTable { public ItemRow[] items; }
        [Serializable]
        public sealed class ItemRow
        {
            public string id = "";
            public string name = "";
            public string grade = "";
            public string desc = "";
            public int price = 0;
            public int category = 0;
        }

        [Serializable] public sealed class PluginTable { public PluginRow[] plugins; }
        [Serializable]
        public sealed class PluginRow
        {
            public string id = "";
            public string displayName = "";
            /// <summary>英文名（v237 起插件表的新条目才写；旧条目没有这个键 ⇒ 空串）。</summary>
            public string nameEn = "";
            public string quality = "";
            public string description = "";
            public int basePrice = 0;
            public int maxStack = 0;
        }
    }
}
