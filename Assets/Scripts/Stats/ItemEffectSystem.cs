using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 阶段 4：**道具效果 → 玩家属性的应用点**。
    ///
    /// ★ 为什么必须有"应用点"这个东西 ★
    /// 阶段 1 定义了道具 ✓ 阶段 3 算出了武器修正 ✓ **但两者都没有生效** ✗
    /// 因为"算出来"和"写进去"是两件事 ✓
    ///
    /// 本会话已经反复吃过这个亏：公式写好了不接线，最后累积成"三套算好了但没人用" ✗
    /// 所以本类刻意做得很小 —— 它只干一件事：**把道具的层数写进 StatBlock** ✓
    ///
    /// ★ 幂等性：用"先删后加"而不是"累加增量" ★
    /// 规格明确要求"避免重复订阅事件、重复发放奖励" ✗
    ///
    /// 累加增量的问题：一旦某次调用被跳过（暂停/异常/时序），
    /// 后面就永远少加了，而且**没有任何报错** ✗
    /// 而"先按来源删掉自己的全部修正，再按当前层数重新加" —— **天然幂等** ✓
    /// 调 1 次和调 100 次结果一样 ✓ 这是本工程既有的模式
    /// （`StatBlock.RemoveModifiersFrom(source)` 就是为此存在的）✓
    /// </summary>
    public static class ItemEffectSystem
    {
        /// <summary>道具修正的来源标记前缀。用前缀是为了能"只删道具的，不动别人的" ✓</summary>
        public const string SourcePrefix = "item:";

        /// <summary>
        /// **把全部道具效果写进属性**。幂等 ✓
        ///
        /// 返回实际写入了几个道具（0 = 没有变化，可用于避免无谓的重算）✓
        /// </summary>
        public static int Apply(PlayerGoods goods, StatBlock stats)
        {
            if (stats == null) return 0;

            // ① 先撤掉**本系统写过**的修正 ✓
            //
            // ★ 为什么逐个精确删除，而不是"按前缀删除" ★
            // `StatBlock` 只提供 `RemoveModifiersFrom(string source)` —— **精确匹配** ✗
            // 没有前缀删除 ✓ 而我不打算为它加一个 —— 那要改既有的 `Stats.cs` ✓
            // 而"新阶段不改既有文件"是本轮重构的纪律 ✓
            //
            // 逐个删是**精确且足够**的：要重新加的正是这些条目 ✓
            // ⚠️ 已知限制：如果将来**卖出/消耗**了某个道具，它的修正会残留 ✗
            //    那时需要"记住曾经写过哪些来源" —— 我在 Goods 的实例上留了 `AppliedStacks`
            //    可以作为那个记忆点 ✓ 但现阶段道具不会被移除，所以不做 ✗
            int removed = 0;
            var items = goods != null ? goods.Items : null;
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                {
                    ItemInstance it = items[i];
                    if (it == null || it.Asset == null || string.IsNullOrEmpty(it.Asset.id)) continue;
                    removed += stats.RemoveModifiersFrom(SourcePrefix + it.Asset.id);
                }

            if (goods == null) return removed;

            // ② 再按当前层数重新写一遍 ✓
            int written = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance it = items[i];
                if (it == null || it.Asset == null) continue;
                if (it.Asset.scope != ItemScope.PlayerStat) continue;   // 全局规则不走属性 ✗

                int stacks = Mathf.Max(1, it.Stacks);
                float total = it.Asset.amount * stacks;

                // 乘算类：把 N 层合并成**一次**乘算（1 + 0.3×2 = 1.6）✗
                // 不合并的话 1.3 × 1.3 = 1.69，差距随层数指数放大（阶段 3 同样的问题）✓
                ModOp op = it.Asset.multiplicative ? ModOp.Mul : ModOp.Add;
                if (it.Asset.multiplicative) total = 1f + it.Asset.amount * stacks;

                stats.AddModifier(new StatModifier(
                    it.Asset.stat, total, op, SourcePrefix + it.Asset.id));

                it.AppliedStacks = stacks;   // 记账（仅供调试/UI 显示）✓
                written++;
            }
            return written;
        }

        /// <summary>
        /// **全局规则类道具**的查表。
        ///
        /// 规格：道具可"修改玩家属性或**全局游戏规则**" ✓
        /// 全局规则不能塞进 `StatBlock`（那是数值属性）✗ 所以走"按 key 查值"的接口 ✓
        ///
        /// 调用方例：`ItemEffectSystem.Rule(goods, "reroll_discount", 1f)` ✓
        /// 这样规则的效果由**使用方**解释，而"有几层、乘加怎么算"由本类统一负责 ✓
        /// —— 与阶段 3 的 `WeaponModifiers` 是同一条路子 ✓
        /// </summary>
        public static float Rule(PlayerGoods goods, string key, float defaultValue = 0f)
        {
            if (goods == null || string.IsNullOrEmpty(key)) return defaultValue;
            float add = 0f;
            float mul = 1f;
            bool any = false;
            var items = goods.Items;
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance it = items[i];
                if (it == null || it.Asset == null) continue;
                if (it.Asset.scope != ItemScope.GlobalRule) continue;
                if (it.Asset.ruleKey != key) continue;
                int stacks = Mathf.Max(1, it.Stacks);
                if (it.Asset.multiplicative) mul *= (1f + it.Asset.amount * stacks);
                else add += it.Asset.amount * stacks;
                any = true;
            }
            return any ? add + mul : defaultValue;
        }

        /// <summary>某个全局规则道具现在能不能买（触发条件）✓</summary>
        public static bool MeetsRequirement(ItemAsset a, PlayerGoods goods, float contextValue)
        {
            if (a == null) return false;
            if (string.IsNullOrEmpty(a.requireKey)) return true;   // 无条件 ⇒ 可买 ✓
            float have = Rule(goods, a.requireKey, 0f);
            return have >= contextValue;
        }
    }
}
