using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 阶段 2：**种子经济学的纯计算**。
    ///
    /// ★ 为什么单独成类，而不是散在 SeedAsset 里 ★
    /// 规格要求"不同种子具有不同成长条件、收割收益与特殊效果" ✗
    /// 而"不同"意味着**同一套公式 + 不同参数** ✓ 不是每个种子一段代码 ✓
    /// 所以公式集中在这里，参数留在 `SeedAsset` 上 ✓
    ///
    /// ★ 为什么全是静态纯函数 ★
    /// 纯函数**可离线验、可加断言、不会隐式改共享状态** ✓
    /// 而当前环境里"实机验证"很不稳定（编辑器窗口失焦就停）✗
    /// 纯函数是本环境下唯一能确定性验证的东西 ✓
    ///
    /// ★ 与既有系统的关系 ★
    /// 本类**不碰** `FarmGrid` 的种植/浇水/收割流程 ✓（规格明确要保留那套）
    /// 它只提供：**浇多少水算满、浇满了给多少金币与经验、卖多少钱** ✓
    /// 既有流程照常跑，只是数值改从本类取 ✓
    /// </summary>
    public static class SeedEconomy
    {
        // ───────────────────────── 品质 ─────────────────────────

        /// <summary>
        /// 品质倍率。**唯一实现** ✗
        ///
        /// 三处（种子/插件/道具）的 `QualityMultiplier` 都转发到这里 ✓
        /// 我最初在三个 SO 里各写了一份 switch ✗ 那是"三处定价公式各写一份"的同款错误 ✓
        /// 本会话已经吃过两次（定价、时间源）✗ 所以这里是收敛点 ✓
        /// </summary>
        public static float QualityMult(GoodsQuality q)
        {
            switch (q)
            {
                case GoodsQuality.Uncommon: return 1.25f;
                case GoodsQuality.Rare: return 1.60f;
                case GoodsQuality.Epic: return 2.10f;
                case GoodsQuality.Legendary: return 2.80f;
                default: return 1f;
            }
        }

        /// <summary>品质的中文名 —— **转发既有 `Rarity`**，不新建第二份表 ✗</summary>
        public static string QualityName(GoodsQuality q)
            => Rarity.NameOf(Mathf.Clamp((int)q, 0, Rarity.Names.Length - 1));

        // ───────────────────────── 成长 ─────────────────────────

        /// <summary>第 <paramref name="level"/> 级成熟所需的水分（级数从 1 起）。</summary>
        public static float WaterNeeded(float baseWater, float waterPerLevel, int level)
            => Mathf.Max(1f, baseWater + waterPerLevel * Mathf.Max(0, level - 1));

        /// <summary>成熟所需的**基础**秒数（不含浇水加速）✗</summary>
        public static float GrowSeconds(float baseSeconds, GoodsQuality q)
            => Mathf.Max(1f, baseSeconds) * Mathf.Lerp(1.15f, 0.80f, (int)q / 4f);

        // ───────────────────────── 收益 ─────────────────────────

        /// <summary>第 <paramref name="level"/> 级的收益倍率。</summary>
        public static float YieldMult(float yieldPerLevel, int level)
            => 1f + yieldPerLevel * Mathf.Max(0, level - 1);

        /// <summary>
        /// ★ v168：第 <paramref name="level"/> 级的**生长速度**倍率 ★
        ///
        /// 合成等级的第一条新影响：等级越高、成熟越快 ✓
        ///
        /// 为什么它必须存在：等级的另两条影响（蓄水需求 100→140→180、收益 ×1.35/×1.70）
        /// 是**一好一坏**的 —— 蓄水需求变大其实是**惩罚**（要浇更多次）✗
        /// 只补收益倍率的话，2/3 级在"成长"这一侧**纯亏**，玩家合成完会先看到自己的田更难伺候 ✓
        /// 加上速度倍率之后，"成长"与"收益"两侧都随等级变好，符合"等级只做数值提升"✓
        ///
        /// 它同时是**唯一实现**：`FarmPlot.BeginPlant` / `SetLevel` 都转发到这里，
        /// 不各写一份 `1 + k×(n−1)` ✗（本会话已因"公式三处各写一份"吃过亏）
        /// </summary>
        public static float LevelSpeedMult(float speedPerLevel, int level)
            => 1f + Mathf.Max(0f, speedPerLevel) * Mathf.Max(0, level - 1);

        /// <summary>
        /// **收割收益** = 基础值 × 品质倍率 × 等级倍率 ✓
        ///
        /// 乘法顺序不影响结果（都是乘）✗ 但**列出来**是为了让"改哪里会影响什么"一眼可见 ✓
        /// 而 `Mathf.RoundToInt` 放在最后 —— 中间四舍五入会累积误差 ✗
        /// </summary>
        public static int Harvest(int baseAmount, GoodsQuality q, float yieldPerLevel, int level)
            => Mathf.Max(0, Mathf.RoundToInt(baseAmount * QualityMult(q) * YieldMult(yieldPerLevel, level)));

        // ───────────────────────── 价格 ─────────────────────────

        /// <summary>
        /// **种子售价** = 基础价 × 品质倍率 × 波次曲线 ✓
        ///
        /// 波次曲线**复用既有的几何增长**（`ShopDef.WavePriceMult` = `(1+step)^(wave-1)`）✗
        /// 不另写一条 —— 否则新商品和旧商品的涨价规律会不一致 ✓
        /// （本会话已吃过"三处定价各写一份、只有一处带波次项"的亏 ✓）
        /// </summary>
        public static int Price(int basePrice, GoodsQuality q, int minute)
        {
            TuningDef t = TableDatabase.Tuning;
            float waveMult = t != null && t.shop != null ? t.shop.WavePriceMult(minute) : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(basePrice * QualityMult(q) * waveMult));
        }

        // ───────────────────────── 升级 ─────────────────────────

        /// <summary>升级到下一级需要付的金币。返回 -1 = 已满级/不可升级 ✗</summary>
        public static int LevelUpCost(int basePrice, int level, int maxLevel)
        {
            if (level >= maxLevel) return -1;
            // 每级递增 60%，四舍五入 —— 用整数以避免累积浮点误差 ✗
            return Mathf.Max(1, Mathf.RoundToInt(basePrice * 0.6f * level));
        }

        /// <summary>能不能升到下一级。</summary>
        public static bool CanLevelUp(int level, int maxLevel) => level < maxLevel && maxLevel > 0;
    }
}
