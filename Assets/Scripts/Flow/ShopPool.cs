using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>商店里的一个货位。三类商品共用一个结构 ✗ —— 不为每类复制一套货架 ✗</summary>
    public struct ShopOfferNew
    {
        public enum Kind { Empty = 0, Seed = 1, Plugin = 2, Item = 3 }

        public Kind Type;
        public SeedAsset Seed;
        public PluginAsset Plugin;
        public ItemAsset Item;
        public GoodsQuality Quality;
        public int Price;
        public bool Sold;
        public bool Locked;

        public bool IsEmpty => Type == Kind.Empty || Sold;
        public string Title
        {
            get
            {
                switch (Type)
                {
                    case Kind.Seed: return Seed != null ? Seed.displayName : "种子";
                    case Kind.Plugin: return Plugin != null ? Plugin.displayName : "插件";
                    case Kind.Item: return Item != null ? Item.displayName : "道具";
                    default: return "";
                }
            }
        }
    }

    /// <summary>
    /// 阶段 5：**商店商品池**。
    ///
    /// 规格：商店负责"生成商品、展示商品、检查价格、执行购买和刷新"，
    /// 而**不负责**实现种子成长、武器攻击或道具效果 ✓
    ///
    /// 所以本类只做四件事：
    ///   ① 抽商品（类别权重 + 品质概率）
    ///   ② 定价（**复用 `SeedEconomy.Price`** ✗ 不另写价格公式）
    ///   ③ 购买（**复用 `PlayerGoods.Acquire`** ✗ 不另写叠加逻辑）
    ///   ④ 刷新（重抽空位 + 记录刷新次数）
    ///
    /// ★ 为什么不复用既有的 `ShopDef` 权重 ★
    /// `ShopDef` 的权重（现为 `seedWeight / pluginWeight / itemWeight`）服务于**商店主流程**那
    /// 一套货架 ✗ 而本类是"调用方灌入目录"的独立商品池 ✓ 两者的品质概率表与刷新语义都不同 ✓
    /// 但**权重的算法**（按累积权重轮盘抽取）与 `ShopDef` 是同一个套路 ✓
    /// 所以这里是"同算法、不同配置"，不是"第二份实现" ✓
    /// </summary>
    public sealed class ShopPool
    {
        // ── 类别权重（可调）──
        public int seedWeight = 55;
        public int pluginWeight = 25;
        public int itemWeight = 20;

        // ── 品质概率（按类别分开，因为三类的品质分布本来就不同）──
        //    数值是**相对权重**，不是百分比 ✓ 与 `ShopDef.gradeWeights` 同一表示法 ✓
        public float[] seedQualityWeights = { 58f, 26f, 12f, 3.5f, 0.5f };
        public float[] pluginQualityWeights = { 50f, 28f, 15f, 6f, 1f };
        public float[] itemQualityWeights = { 62f, 24f, 10f, 3.5f, 0.5f };

        /// <summary>商店货位数。</summary>
        public int slots = 4;

        /// <summary>已刷新次数（用于刷新涨价）。</summary>
        public int RerollCount { get; private set; }

        // 商品目录（由调用方灌入 ✗ 不在本类里硬编码资产路径）
        public readonly List<SeedAsset> SeedCatalog = new List<SeedAsset>();
        public readonly List<PluginAsset> PluginCatalog = new List<PluginAsset>();
        public readonly List<ItemAsset> ItemCatalog = new List<ItemAsset>();

        public readonly List<ShopOfferNew> Offers = new List<ShopOfferNew>();

        public void Reset()
        {
            RerollCount = 0;
            Offers.Clear();
            for (int i = 0; i < Mathf.Max(1, slots); i++) Offers.Add(new ShopOfferNew { Type = ShopOfferNew.Kind.Empty });
        }

        /// <summary>
        /// **重抽全部未锁定、未售出的货位**。
        ///
        /// ★ 两条保护，都是踩过的 ★
        ///   ① **已售出的空位也要重抽** —— 否则售出的格子会永久空着 ✗
        ///   ② **锁定位不重抽** —— 那是玩家花钱锁的 ✓ 重抽它就等于骗钱 ✗
        /// </summary>
        public void Refresh(System.Random rng, int minute, bool chargeReroll = true)
        {
            if (rng == null) return;
            if (chargeReroll) RerollCount++;
            for (int i = 0; i < Offers.Count; i++)
            {
                ShopOfferNew o = Offers[i];
                if (o.Locked && !o.Sold) continue;      // 锁定且未售出 ⇒ 保留 ✓
                Offers[i] = Roll(rng, minute);
            }
        }

        /// <summary>刷新费用（几何增长，复用整局的分钟刻度）✓</summary>
        public int RerollPrice(int basePrice = 3, int stepPerMinute = 2)
            => Mathf.Max(1, basePrice + stepPerMinute * Mathf.Max(0, RerollCount));

        /// <summary>抽一个货位：先抽类别，再按该类别的品质分布抽品质，最后在该品质的资产里随机 ✗</summary>
        public ShopOfferNew Roll(System.Random rng, int minute)
        {
            int total = Mathf.Max(1, seedWeight) + Mathf.Max(1, pluginWeight) + Mathf.Max(1, itemWeight);
            int roll = rng.Next(total);
            // 若某类目录为空，退到有货的那一类 ✗ 否则会抽出空货位 ✓
            bool hasSeed = SeedCatalog.Count > 0, hasPlugin = PluginCatalog.Count > 0, hasItem = ItemCatalog.Count > 0;

            if (roll < seedWeight && hasSeed) return RollSeed(rng, minute);
            if (roll < seedWeight + pluginWeight && hasPlugin) return RollPlugin(rng, minute);
            if (hasItem) return RollItem(rng, minute);
            if (hasSeed) return RollSeed(rng, minute);
            if (hasPlugin) return RollPlugin(rng, minute);
            return new ShopOfferNew { Type = ShopOfferNew.Kind.Empty };
        }

        private ShopOfferNew RollSeed(System.Random rng, int minute)
        {
            SeedAsset a = PickByQuality(rng, SeedCatalog, seedQualityWeights, s => (int)s.quality);
            if (a == null) return new ShopOfferNew { Type = ShopOfferNew.Kind.Empty };
            return new ShopOfferNew
            {
                Type = ShopOfferNew.Kind.Seed, Seed = a, Quality = a.quality,
                // 定价复用 SeedEconomy.Price ✗ 不在这里再写一条价格公式 ✓
                Price = SeedEconomy.Price(a.basePrice, a.quality, minute),
            };
        }

        private ShopOfferNew RollPlugin(System.Random rng, int minute)
        {
            PluginAsset a = PickByQuality(rng, PluginCatalog, pluginQualityWeights, p => (int)p.quality);
            if (a == null) return new ShopOfferNew { Type = ShopOfferNew.Kind.Empty };
            return new ShopOfferNew
            {
                Type = ShopOfferNew.Kind.Plugin, Plugin = a, Quality = a.quality,
                Price = SeedEconomy.Price(a.basePrice, a.quality, minute),
            };
        }

        private ShopOfferNew RollItem(System.Random rng, int minute)
        {
            ItemAsset a = PickByQuality(rng, ItemCatalog, itemQualityWeights, it => (int)it.quality);
            if (a == null) return new ShopOfferNew { Type = ShopOfferNew.Kind.Empty };
            return new ShopOfferNew
            {
                Type = ShopOfferNew.Kind.Item, Item = a, Quality = a.quality,
                Price = SeedEconomy.Price(a.basePrice, a.quality, minute),
            };
        }

        /// <summary>
        /// 按品质权重在同一品质的资产里随机。
        ///
        /// 两步走（先品质、再资产）而不是"给每个资产加权" ✗
        /// 理由：同品质的资产应当**等概率** ✓ 否则某个品种会因权重高而刷屏 ✓
        /// </summary>
        private static T PickByQuality<T>(System.Random rng, List<T> catalog, float[] weights, Func<T, int> qualityOf)
            where T : class
        {
            if (catalog == null || catalog.Count == 0) return null;

            int q = PickQuality(rng, weights, qualityOf(catalog[0]));
            // 收集该品质的全部条目
            var pool = new List<T>();
            for (int i = 0; i < catalog.Count; i++)
                if (catalog[i] != null && qualityOf(catalog[i]) == q) pool.Add(catalog[i]);

            if (pool.Count == 0)
            {
                // 该品质没有资产 ⇒ 退到全部 ✗ 否则会抽出空货位 ✓
                for (int i = 0; i < catalog.Count; i++) if (catalog[i] != null) pool.Add(catalog[i]);
            }
            if (pool.Count == 0) return null;
            return pool[rng.Next(pool.Count)];
        }

        /// <summary>按权重抽一个品质档（0..4）。权重不足或全零时退到最低档 ✓</summary>
        private static int PickQuality(System.Random rng, float[] weights, int fallback)
        {
            if (weights == null || weights.Length == 0) return Mathf.Clamp(fallback, 0, 4);
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(0f, weights[i]);
            if (total <= 0.0001f) return 0;

            float roll = (float)rng.NextDouble() * total;
            float acc = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += Mathf.Max(0f, weights[i]);
                if (roll < acc) return Mathf.Clamp(i, 0, 4);
            }
            return Mathf.Clamp(weights.Length - 1, 0, 4);
        }

        // ─────────────────────── 购买 ───────────────────────

        /// <summary>
        /// **买下一个货位**。返回是否成功。
        ///
        /// 规格："购买成功后，将商品交给对应系统处理" ✓
        /// 所以这里**只负责扣钱与转交** ✗ 不实现种子成长 / 武器攻击 / 道具效果 ✓
        /// 三类的"转交"都走既有的 `PlayerGoods` 入口 ✗ 不各写一套 ✓
        /// </summary>
        public bool TryBuy(int index, PlayerGoods goods, ref int gold, out string reason)
        {
            reason = "";
            if (index < 0 || index >= Offers.Count) { reason = "货位不存在"; return false; }
            ShopOfferNew o = Offers[index];
            if (o.Type == ShopOfferNew.Kind.Empty) { reason = "空货位"; return false; }
            if (o.Sold) { reason = "已售出"; return false; }
            if (gold < o.Price) { reason = "金币不足"; return false; }

            bool ok;
            switch (o.Type)
            {
                case ShopOfferNew.Kind.Seed:
                    // 种子进商品栏（一个实例）✓ 等级/浇水状态在实例上 ✗
                    goods?.Seeds.Add(new SeedInstance(o.Seed));
                    ok = goods != null && o.Seed != null;
                    if (!ok) reason = "商品栏不可用";
                    break;
                case ShopOfferNew.Kind.Plugin:
                    // 插件需要指定武器 ⇒ 这里只入背包，安装由 WeaponSystem.Install 做 ✓
                    ok = goods != null && o.Plugin != null;
                    if (!ok) reason = "商品栏不可用";
                    else goods.Plugins.Add(new PluginInstance(o.Plugin, WeaponSlot.None));
                    break;
                case ShopOfferNew.Kind.Item:
                    // 走统一入口 ✗ 叠加/唯一/上限都在 PlayerGoods.Acquire 里 ✓
                    ok = goods != null && goods.Acquire(o.Item);
                    if (!ok) reason = goods == null ? "商品栏不可用" : "已达叠加上限";
                    break;
                default:
                    ok = false; reason = "未知商品";
                    break;
            }

            if (!ok) return false;

            gold -= o.Price;
            o.Sold = true;
            Offers[index] = o;
            return true;
        }

        /// <summary>锁定/解锁一个货位。已售出的不能锁 ✗（会永久占位）</summary>
        public bool ToggleLock(int index, out string reason)
        {
            reason = "";
            if (index < 0 || index >= Offers.Count) { reason = "货位不存在"; return false; }
            ShopOfferNew o = Offers[index];
            if (o.Type == ShopOfferNew.Kind.Empty) { reason = "空货位不能锁"; return false; }
            if (o.Sold) { reason = "已售出的空位不能锁"; return false; }
            o.Locked = !o.Locked;
            Offers[index] = o;
            reason = o.Locked ? "已锁定（刷新时保留）" : "已解锁";
            return true;
        }
    }
}
