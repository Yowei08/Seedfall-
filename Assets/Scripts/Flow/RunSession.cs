using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>一帧的输入意图。人类输入与 Bot 都填这个结构，保证"Bot 跑局 = 回放"。</summary>
    public struct FrameInput
    {
        public Vector2 Move;
        public Vector2 AimWorld;
        public bool Interact;      // 按住：对最近地块执行当前动作

        /// <summary>
        /// v160：**E 键** —— 与商店车对话。
        ///
        /// ★ 为什么不能复用 `Interact` ★
        /// `Interact` 的语义是"对最近地块执行当前动作" ✗ 实际绑的是**鼠标左键**
        /// （HUD 文案："攻击 / 与地块互动（鼠标左键）"）✓
        /// 若把 E 也接到它上面，按 E 会**同时**触发锄地/浇水 ✗
        ///
        /// 我上一轮就是假设了"E = Interact"而没有查绑定 ✗ 结果逻辑写好了却永远不触发 ✓
        /// 这次的教训：**接线前先确认输入源，而不是假设键位** ✓
        /// </summary>
        public bool ShopOpen;
        public bool RemovePlant;   // 铲除生长中的植物
        public bool Fire;          // 鼠标左键：朝鼠标位置方向主动攻击
        public bool Fertilize;     // v14：空格——在成长中的地块上施肥
        public bool SlotClicked;   // 本帧是否有种子袋点击
        public bool RewardSelectSet; // v13：本帧是否点了"选中"（避免 0 被当成有效选择）
        public int RewardSelect;   // v13：抽卡"选中"某张卡
        public int RewardPick;     // v11：抽卡"确认"选择（-1 无）
        public bool RewardReroll;  // v13：抽卡重抽（消耗金币）
        public bool RewardPickConfirmed; // v13：抽卡"确认"（避免 int 默认值 0 被当成有效选择）
        public int ToggleOfferLock;      // v17：要切换锁定的商品序号
        public bool OfferLockToggled;    // v17：本帧是否真的点了"锁定"（否则 0 会被当成合法值，每帧翻转！）
        public bool SlotMergeClicked; // v9：本帧是否点了某个种子的"合成"
        public int SlotMerge;      // 被点"合成"的槽位（仅在 SlotMergeClicked 为真时有效）
        public int ClickedSlot;    // 被点击的槽位（仅在 SlotClicked 为真时有效）
        public int WheelScroll;    // 鼠标滚轮增量：+1 下一把武器 / -1 上一把 / 0 不变
        public bool LockOn;        // 已废弃（v10 起自动锁定，不再需要按键）
        public int SelectSeed;     // 由 UI 写入的选中槽位，-1 表示无
        /// <summary>v48：本帧是否按了 1/2/3 切武器（不用默认值 0，否则会被当成"每帧切锄头"）。</summary>
        public bool WeaponSelectSet;
        /// <summary>v48：要切换到的武器（0 锄头 / 1 水壶 / 2 肥料）。</summary>
        public int SelectWeapon;
        public int SellSlot;       // 由 UI 写入的出售槽位，-1 表示无
        public bool Skip;          // 空格：提前结束准备/商店
        public int BuyOffer;       // 商店格 0..3，-1 表示无
        public bool Reroll;
        public int InitialChoice;  // 开局三选一 0..2，-1 表示无
        /// <summary>
        /// ★★ v215：本帧是否按了 **Q = 换种** ★★
        ///
        /// 语义：把"最近的那块耕地"上的作物换成种子袋里**下一个与它不同的种子**
        /// （唯一实现 `RunSession.ReplaceSeedOnPlot`）。
        ///
        /// 为什么是"一次性脉冲"而不是电平：换种是一次性动作（像商店刷新 R），
        /// 电平会让"按住 Q"每帧换一次 ⇒ 种子袋被反复折腾 ✓
        /// ⇒ 与 `Reroll` / `OfferLockToggled` 同样在 `ConsumeFrameEdges` **帧级**消费并清零 ✓
        /// </summary>
        public bool ReplaceSeed;
    }

    public struct MeleeSwingEvent
    {
        public Vector2 Pos;
        public Vector2 Dir;
        /// <summary>
        /// ★ v205：这是**视觉**半径（米），不是判定半径 ★
        /// `MeleeFxRadius(range)` = `判定半径 × 0.95`（★ v273：3.42 → **3.729m**；v273 前 3.8 → 4.09m）。
        /// 判定那边是 `MeleeJudgeRadius(range, e.Radius)`，**永远比这个大** ✓
        /// ⇒ 看到的那条弧一定落在打得到的范围**里面**（用户需求："判定框覆盖特效，且实际大于特效一点"）
        /// </summary>
        public float Range;
        /// <summary>★ v205：**视觉**张角（度）= `判定张角 − 4°`（今天 100 → **96°**）。</summary>
        public float ArcDeg;
        /// <summary>
        /// v176：挥击弧的**颜色/尺寸/拖尾/火花/震屏/照明** —— 由锄头这一路的插件修正算出来
        /// （`PluginFx.For(ModsFor(Hoe), Hoe)`）。没有这个字段时，弧的外观写死在
        /// `WorldView.OnMeleeSwing` 里，装十件插件也画得一模一样 ✗
        /// </summary>
        public FxSpec Fx;

        /// <summary>
        /// ★ v304：**巨型**（`hoe_giant`）—— `true` 时这一刀不是"面前半圆"，而是**环绕玩家的整圈**。
        ///
        /// ── 为什么必须加这个字段（用户报的"巨型没有生效，特效也没有变成环形"）──
        ///   判定侧是通的（`MeleeHitScan` → `MeleePointHit(..., pm.GiantRing)`，
        ///   见 `RunSession.MeleeHitScan` 与 `PluginExtra2.GiantInnerRatio`），
        ///   但**形状从来没有变成一条通道交给表现层**：
        ///     · 本结构体只有 `Pos / Dir / Range / ArcDeg / Fx` 五个字段，全是"半圆"的几何
        ///     · `FxSpec` 里巨型只贡献 `Scale×1.35 / Rings+1`（都是"更大"，不是"整圈"）
        ///   ⇒ `WorldView.OnMeleeSwing` 与 `AttackVfx.SpawnSwing` **没有任何办法知道要画整圈**，
        ///     永远画一条 ±48° 的弧 ⇒ 玩家眼里"装了巨型 = 什么都没发生" ✗
        ///   形状是几何，就该和 `Range` / `ArcDeg` 放在同一层（本结构体），
        ///   而不是塞进配色规格 `FxSpec`（那是"长什么样"，不是"打在哪"）✓
        ///
        /// ⚠️ **默认 `false` = 逐位等于改动前**：所有既有发布点（`DoHoeSwing` 的普通挥击、
        ///   `DebugSwing`）都不赋这个字段 ⇒ 它们画出来与改前**一个像素都不差** ✓
        /// </summary>
        public bool FullRing;
    }

    public static class Telemetry
    {
        private const int Cap = 2000;
        public static readonly List<string> Buffer = new List<string>();

        public static void Emit(string evt, params (string key, object value)[] fields)
        {
            var sb = new StringBuilder();
            sb.Append('{').Append("\"evt\":\"").Append(evt).Append('"');
            for (int i = 0; i < fields.Length; i++)
            {
                sb.Append(",\"").Append(fields[i].key).Append("\":");
                object v = fields[i].value;
                if (v is float f) sb.Append(f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                else if (v is string s) sb.Append('"').Append(s).Append('"');
                else if (v is bool b) sb.Append(b ? "true" : "false");
                else sb.Append(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append('}');
            if (Buffer.Count >= Cap) Buffer.RemoveAt(0);
            Buffer.Add(sb.ToString());
        }

        public static void Clear() => Buffer.Clear();

        public static string Dump() => string.Join("\n", Buffer);
    }

    public sealed class SeedSlot
    {
        public SeedDef Def;
        public string MutationId;
        /// <summary>品质档位：0 白 / 1 绿 / 2 蓝 / 3 紫 / 4 金。**由种子定义决定**（`RollSeed` 写的是 `def.RarityTier`），合成不再改动它。</summary>
        public int Quality;

        /// <summary>
        /// **等级**（<see cref="StartingLevel"/> 起，<see cref="MaxLevel"/> 封顶）。
        ///
        /// ★ 它是这套种子的**唯一成长轴** ★
        /// 涨级**有两条来源**：
        ///   ① 商店的"合成"——两个**同种同级**的种子并成一个高一级的（主力路径 ✓）
        ///   ② ★ v185 ★ 商店**直接卖** 2 级 / 3 级种子（`ShopOffer.Level`）——
        ///      买进来就是那个等级（`SeedBag.AddSeed(..., level)`）✓
        /// 品质与等级是**两个正交的维度** ✗ ：品质是"这是哪一品级的种子"（买定即固定，决定配色与定价），
        /// 等级是"这一个种子养到第几级"（合成或直接买）✓ 所以两者并存，不是二选一 ✓
        /// </summary>
        public int Level = StartingLevel;

        public bool IsEmpty => Def == null;

        /// <summary>合成链的终点：1 → 2 → 3，3 级不能再合。</summary>
        public const int MaxLevel = 3;
        /// <summary>等级的起点：开局拿到的种子、以及**1 级货位**上买到的种子都是 1 级。
        /// ★ v185：2 级 / 3 级货位上买到的**不是** 1 级 —— 它们是 `ShopOffer.Level` ✓</summary>
        public const int StartingLevel = 1;
        /// <summary>每升一级多要的水分（喂给 <see cref="SeedEconomy.WaterNeeded"/>）。</summary>
        public const float WaterPerLevel = 40f;
        /// <summary>每升一级的**卖出返还**倍率增量（喂给 <see cref="SeedEconomy.YieldMult"/>）。</summary>
        public const float YieldPerLevel = 0.5f;

        /// <summary>
        /// ★ v168：每升一级的**收获收益**倍率增量 ★ —— 1 级 ×1.00 / 2 级 ×1.35 / 3 级 ×1.70
        ///
        /// ★ 为什么不复用 `YieldPerLevel`（0.5）★
        /// 那个数服务的是**卖出返还**（×1.00/×1.50/×2.00），是一次性、可由玩家主动变现的钱 ✓
        /// 而收获收益是**每次成熟都结算一次**的复利 ⇒ 同一个系数会让高等级种子过于强势 ✗
        /// 两者是两条经济曲线，共用一个常量就等于"调其中一条会顺手改坏另一条" ✓
        /// </summary>
        public const float HarvestPerLevel = 0.35f;

        /// <summary>
        /// ★ v168：每升一级的**生长速度**倍率增量 ★ —— 1 级 ×1.00 / 2 级 ×1.15 / 3 级 ×1.30
        ///
        /// 它存在的理由：等级的另一条影响是"蓄水需求 100→140→180"，而那是**惩罚**（要浇更多次）✗
        /// 只有收益变好、成长变难的话，合成完的第一感受是"我的田更难伺候了" ✓
        /// 给"成长"这一侧一条正向的数值，等级才是纯粹的提升 ✓
        /// </summary>
        public const float GrowSpeedPerLevel = 0.15f;

        /// <summary>
        /// 等级带来的收益倍率：1 级 = 1.00 / 2 级 = 1.50 / 3 级 = 2.00。**卖出返还**用这条。
        /// **复用 `SeedEconomy.YieldMult`** ✗ 不在槽位上再写一遍 `1 + k×(n-1)` ✓
        /// </summary>
        public float LevelYieldMult => SeedEconomy.YieldMult(YieldPerLevel, Level);

        /// <summary>等级带来的**收获收益**倍率：1 级 = 1.00 / 2 级 = 1.35 / 3 级 = 1.70。</summary>
        public float LevelHarvestMult => SeedEconomy.YieldMult(HarvestPerLevel, Level);

        /// <summary>等级带来的**生长速度**倍率：1 级 = 1.00 / 2 级 = 1.15 / 3 级 = 1.30。</summary>
        public float LevelGrowSpeedMult => SeedEconomy.LevelSpeedMult(GrowSpeedPerLevel, Level);

        /// <summary>
        /// 第 <paramref name="level"/> 级的种子要多少水。**复用 `SeedEconomy.WaterNeeded`** ✗ 不另写公式 ✓
        /// 1 级 = `farm.waterMaxDefault` / 2 级 = +40 / 3 级 = +80。
        /// </summary>
        public static int WaterNeededFor(int level)
        {
            TuningDef t = TableDatabase.Tuning;
            float baseWater = t != null && t.farm != null ? t.farm.waterMaxDefault : 100f;
            return Mathf.RoundToInt(SeedEconomy.WaterNeeded(baseWater, WaterPerLevel, level));
        }

        /// <summary>
        /// ★ v168：**某个品种、某一级**的实际蓄水上限（种下去的时候写进地块的那个数）★
        ///
        ///   上限 = [100 + 40×(等级−1)] + (品种的 waterMax − 100)
        ///
        /// ★ 两条轴分开，各自可读 ★
        ///   · **等级轴** —— 恒定 +40/级（100 → 140 → 180）✓ 用户标准里点名要的就是这三个数 ✓
        ///   · **品种轴** —— 种子自己的 `waterMax` 相对基准 100 的偏差（80 = 小水箱，140 = 大水箱）✓
        ///
        /// ★ 为什么是"相加"而不是"相乘"或"取最大值" ★
        /// 相乘会让"2 级"的水量随品种漂移（小水箱 +40×0.8=32、大水箱 +56），
        /// 于是"每级 +40"这句写在面板上的话就变成了假话 ✗
        /// 相加保住 +40 这个数在所有品种上都成立 ⇒ 玩家与策划看到的是同一个规律 ✓
        ///
        /// ⚠️ 下限 20：`Mathf.Max(1, waterMax)` 只能防 0，防不住"品种偏移把水箱压成个位数" ✓
        /// </summary>
        public static int WaterMaxFor(SeedDef def, int level)
        {
            TuningDef t = TableDatabase.Tuning;
            float baseWater = t != null && t.farm != null ? t.farm.waterMaxDefault : 100f;
            float levelWater = SeedEconomy.WaterNeeded(baseWater, WaterPerLevel, level);
            float seedOffset = def != null ? def.WaterMax - baseWater : 0f;
            return Mathf.Max(20, Mathf.RoundToInt(levelWater + seedOffset));
        }

        /// <summary>这一级的种子要多少水（实例版）。</summary>
        public int WaterNeeded => WaterNeededFor(Level);

        /// <summary>出售返还比例：**当前波次下该商品实际价格的 40%**（需求）。</summary>
        public const float SellRefundRatio = 0.40f;

        /// <summary>
        /// v118：定价的**唯一实现**——所有商品都走这里。
        ///
        ///   价格 = 基础价 × 稀有度倍率 × 波次曲线
        ///
        /// ★ 为什么必须是唯一实现 ★
        /// 种子与工具原本各自写了一份、**漏掉了波次曲线**，只有道具那一份是完整的。
        /// 结果就是玩家报的"商店内植物道具的价格没有随回合增加"——
        /// 同一家商店里，道具在涨价而种子和武器不涨，玩家一眼就看出来不对。
        /// 三处公式必然漂移，所以收敛成一个入口。
        /// </summary>
        public static int ShopPriceFrom(int basePrice, int quality, int wave)
        {
            TuningDef t = TableDatabase.Tuning;
            return Mathf.Max(1, Mathf.RoundToInt(
                basePrice * t.quality.PriceMult(quality) * t.shop.WavePriceMult(wave)));
        }

        /// <summary>种子定义 → 当前波次的售价。</summary>
        public static int ShopPriceAt(SeedDef def, int quality, int wave)
            => def == null ? 0 : SeedSlot.ShopPriceFrom(def.Price, quality, wave);

        /// <summary>
        /// 按**当前波次**算返还（= 实际价格 × 40% × 等级倍率）。
        /// 等级在这里变成真金白银 ✗ —— 否则"等级"只是一个数字，玩家没有任何理由去合成 ✓
        /// </summary>
        public int SellValueFor(int wave)
            => Def == null ? 0
             : Mathf.FloorToInt(ShopPriceAt(Def, Quality, wave) * LevelYieldMult * SellRefundRatio);

        public string QualityName => TableDatabase.Tuning.quality.NameOf(Quality);

        public string Label
        {
            get
            {
                if (Def == null) return "空";
                // v44：品质由种子固定（不再有品质后缀）
                // 等级单独成行显示（卡片 / 详情），不混进名字里 ——
                // 名字一长，"豌豆射手·变异 Lv2" 会在窄卡里被裁掉后半截。
                return Def.Name;
            }
        }
    }

    /// <summary>种子袋：6 槽，每槽一种种子。v4 起**移除充能机制、种子无限**。</summary>
    public sealed class SeedBag
    {
        private SeedSlot[] _slots;

        public SeedBag(int slotCount, bool unlimited = true)
        {
            _slots = new SeedSlot[slotCount];
            for (int i = 0; i < slotCount; i++) _slots[i] = new SeedSlot();
            Unlimited = unlimited;
        }

        public int SlotCount => _slots.Length;
        /// <summary>无限种子：种植不消耗任何东西。</summary>
        public bool Unlimited { get; }
        public int Selected { get; set; }
        public SeedSlot this[int i] => _slots[i];

        /// <summary>
        /// "没有当前种子"时返回的占位槽（`Selected < 0`）。**只读占位，永远空**。
        /// 为什么不能再用 `Mathf.Clamp(Selected, 0, len-1)`：`-1` 会被夹到 **第 0 格**，
        /// 而第 0 格完全可能是另一颗**玩家没选的**种子 ⇒ "当前种子"悄悄变成别人的 ✓
        /// </summary>
        private static readonly SeedSlot NoSelection = new SeedSlot();

        /// <summary>
        /// 选中格。`Selected` 越界或为 -1（= 卖空/没有种子）⇒ 返回恒空的占位槽，
        /// 于是 `SelectedSlot.IsEmpty` 为真 ⇒ 播种那条链（`RunSession` 的 `sowSeed` 三行）
        /// 自然读到 `null`，不会再"拿上一颗种子去种"或"拿空槽里的残留定义去种" ✓
        /// </summary>
        public SeedSlot SelectedSlot
            => Selected >= 0 && Selected < _slots.Length ? _slots[Selected] : NoSelection;

        public SeedSlot Find(string seedId, string mutationId, int quality = -1)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].IsEmpty) continue;
                if (_slots[i].Def.Id != seedId) continue;
                if (_slots[i].MutationId != mutationId) continue;
                if (quality >= 0 && _slots[i].Quality != quality) continue;
                return _slots[i];
            }
            return null;
        }

        /// <summary>
        /// 两个槽能否**合成**：同一种种子（含变异）、**同一等级**、且未到 3 级。
        ///
        /// ★ 为什么不再要求"同品质" ★
        /// 品质自 v44 起由种子定义固定（`RollSeed` 写的就是 `def.RarityTier`）✗
        /// ⇒ "同种"已经蕴含"同品质"，再写一条就是**同一条规则的第二个写法** ✓
        /// 而合成改走等级轴之后，把品质写进门槛还会让"同种同级"这条规则变成假的 ✓
        /// </summary>
        public bool CanMerge(int a, int b, out string reason)
        {
            reason = null;
            if (a == b || a < 0 || b < 0 || a >= _slots.Length || b >= _slots.Length)
            {
                reason = "请选择两个不同的种子槽";
                return false;
            }
            SeedSlot from = _slots[a], to = _slots[b];
            if (from.IsEmpty || to.IsEmpty) { reason = "空槽不能合成"; return false; }
            if (from.Def.Id != to.Def.Id || from.MutationId != to.MutationId)
            {
                reason = "只有同一种种子才能合成";
                return false;
            }
            if (from.Level != to.Level)
            {
                reason = $"两个种子必须是同一等级（现在是 {from.Level} 级 + {to.Level} 级）";
                return false;
            }
            if (from.Level >= SeedSlot.MaxLevel)
            {
                reason = $"已经是最高等级（{SeedSlot.MaxLevel} 级），无法继续合成";
                return false;
            }
            return true;
        }

        /// <summary>合成：b 的**等级 +1**，a 被消耗。品质不变（它由种子定义决定）✗</summary>
        public bool Merge(int a, int b, out string reason)
        {
            if (!CanMerge(a, b, out reason)) return false;
            _slots[b].Level++;
            ClearSlot(a);
            return true;
        }

        /// <summary>
        /// 清空一个槽位。**品质与等级一并复位** ✗
        /// 只清 `Def` 的话，残留的等级会被下一个住进来的种子继承
        /// —— 而 `AddSeed` 只写品质不写等级，症状就是"新买的 1 级种子一进袋就是 2 级" ✓
        /// </summary>
        private void ClearSlot(int i)
        {
            _slots[i].Def = null;
            _slots[i].MutationId = null;
            _slots[i].Quality = 0;
            _slots[i].Level = SeedSlot.StartingLevel;
        }

        /// <summary>
        /// **自动合成**：反复找出"同种同级"的一对合成，直到再也合不动
        /// （4 个 1 级 → 2 个 2 级 → 1 个 3 级）。
        ///
        /// ★ v198：**商店那个"一键合成"按钮已按用户要求删除** ★
        ///   旧注释写"商店的『一键合成』按钮与『袋满时的购买』都走这里"——
        ///   **后半句是错的**（grep 全工程：购买路径**没有**任何一处调它）⇒ 已订正 ✓
        ///   删掉按钮之后，本方法的**玩法侧调用者为 0**；它仍然留下，理由是
        ///   "合成规则本身没被删"——自检有三条断言直接调它验规则
        ///   （`V198 前的合成三条`：能合、不同种不合、合到封顶后不再有可合成的一对），
        ///   删方法就要删断言，而本轮硬约束是"改期望值不删断言" ✓
        ///   若日后要彻底删掉批量合成，连同那三条断言一起删，别只删一半 ✗
        ///
        /// ★ 它现在真的会合并 ★
        /// v44 起这个方法被改成了 `return 0` + "合成已删除"的注释，
        /// 而真逻辑被改名成 `AutoMergeAllLegacy` 并**留在同一个类里没人调用** ✗
        /// 一个"名字叫自动合成、实际什么都不做"的方法比不删更糟：
        /// 调用方（含自检）会以为自己验证过合成 ✓
        /// </summary>
        public int AutoMergeAll(out string firstReason)
        {
            firstReason = null;
            int merged = 0;
            bool again = true;
            while (again)
            {
                again = false;
                for (int i = 0; i < _slots.Length && !again; i++)
                {
                    if (_slots[i].IsEmpty) continue;
                    for (int j = i + 1; j < _slots.Length; j++)
                    {
                        string reason;
                        if (!CanMerge(i, j, out reason)) { if (firstReason == null) firstReason = reason; continue; }
                        Merge(i, j, out _);
                        merged++;
                        again = true;
                        break;
                    }
                }
            }
            return merged;
        }

        /// <summary>指定槽位是否存在"另一个可合成的种子"（同品种、同等级、未到 3 级）。</summary>
        public bool CanMergeWithOther(int slot, out int other)
        {
            other = -1;
            if (slot < 0 || slot >= _slots.Length || _slots[slot].IsEmpty) return false;
            for (int j = 0; j < _slots.Length; j++)
            {
                if (j == slot) continue;
                if (CanMerge(slot, j, out _)) { other = j; return true; }
            }
            return false;
        }

        /// <summary>合成指定槽位——找到另一个同种同级的种子，把它合到本槽（**等级 +1**）。</summary>
        public bool MergeSlot(int slot, out string reason)
        {
            if (slot < 0 || slot >= _slots.Length || _slots[slot].IsEmpty)
            {
                reason = "空槽位";
                return false;
            }
            int other;
            if (!CanMergeWithOther(slot, out other))
            {
                reason = _slots[slot].Level >= SeedSlot.MaxLevel
                    ? $"已经是最高等级（{SeedSlot.MaxLevel} 级），无法继续合成"
                    : "种子袋里没有另一个同样的种子（需同品种、同等级）";
                return false;
            }
            _slots[slot].Level++;
            ClearSlot(other);
            reason = null;
            return true;
        }

        /// <summary>袋里是否有一对可合成的种子（商店按钮用它显示可用状态）。</summary>
        public bool HasMergeablePair()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].IsEmpty) continue;
                for (int j = i + 1; j < _slots.Length; j++)
                {
                    if (CanMerge(i, j, out _)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// **满袋时买入"与袋中相同"的种子 → 直接合成**（该槽等级 +1）。
        /// 返回 true 表示这次购买已被合成消化。
        ///
        /// ★ 为什么留它 ★
        /// 没有这一条，种子上限与合成会形成死锁：袋满 ⇒ 买不进用于合成的材料 ⇒ 永远合不了 ✓
        /// ★ v185：只找**与这张商品同等级**的同种槽位（`level` 由调用方传商品自己的等级）★
        /// 改前"买进来的种子恒为 1 级"⇒ 这里恒按 1 级找 ✓
        /// 现在货架会卖 2 级 / 3 级种子 ⇒ 必须按**商品等级**找，否则"买 2 级"会被并进 1 级槽，
        /// 玩家的钱花了、袋里却只多了一个 2 级（而不是预期的 3 级）✗
        /// ⚠️ 判据"不找任意等级"仍然成立：找任意会把材料并进更高的槽 ⇒ 3 级封顶后再买就是白扔钱 ✓
        /// </summary>
        public bool TryMergeWithExisting(SeedDef def, string mutationId, int level, out SeedSlot merged)
        {
            merged = null;
            if (def == null) return false;
            if (level >= SeedSlot.MaxLevel) return false;      // 3 级封顶：不能再并进去 ✗
            for (int i = 0; i < _slots.Length; i++)
            {
                SeedSlot s = _slots[i];
                if (s.IsEmpty) continue;
                if (s.Def.Id != def.Id || s.MutationId != mutationId) continue;
                if (s.Level != level) continue;
                s.Level++;
                merged = s;
                return true;
            }
            return false;
        }

        /// <summary>v23：种子位容量 = 田地数（1:1）。只增不减，超出部分丢弃。</summary>
        public void SetCapacity(int count)
        {
            count = Mathf.Clamp(count, 1, 25);
            if (count == _slots.Length) return;
            var next = new SeedSlot[count];
            for (int i = 0; i < count; i++)
            {
                // v30：新格子必须实例化（否则扩容后访问会空引用崩溃）
                next[i] = (i < _slots.Length && _slots[i] != null) ? _slots[i] : new SeedSlot();
            }
            _slots = next;
        }

        public int FirstEmpty()
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i].IsEmpty) return i;
            return -1;
        }

        /// <summary>第一个**非空**槽下标；一颗种子都没有 ⇒ -1。</summary>
        public int FirstOccupied()
        {
            for (int i = 0; i < _slots.Length; i++) if (!_slots[i].IsEmpty) return i;
            return -1;
        }

        /// <summary>
        /// 把 <see cref="Selected"/> 拉回"一个真的住着种子的格子"。**幂等**。
        ///
        /// 当前选中格非空 ⇒ 原样返回（不动玩家点过的选择）；
        /// 当前选中格是空的（典型：刚把它卖了）⇒ 改到 `FirstOccupied()`；
        /// 一颗种子都没有 ⇒ **-1**（`SelectedSlot` 于是返回恒空占位槽）。
        ///
        /// ★ 为什么必须由**变动的那个方法**自己调 ★
        ///   `Selected` 是裸字段，全工程有 6 处写它；若靠 UI 每帧"顺手修正"，
        ///   逻辑侧（播种 / 自检 / 回放）读到的仍是过期指针 ⇒ 同一个 bug 换一层皮回来 ✗
        ///   这里的判据只有一条、就在改数据的当口，所以**不可能**出现"UI 改了、逻辑没改" ✓
        /// </summary>
        public int NormalizeSelection()
        {
            if (Selected >= 0 && Selected < _slots.Length && !_slots[Selected].IsEmpty) return Selected;
            Selected = FirstOccupied();
            return Selected;
        }

        public int IndexOf(SeedSlot slot)
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i] == slot) return i;
            return -1;
        }

        /// <summary>
        /// 放入一个种子。v5 起**允许同名重复**：两个相同种子是**合成**的材料，
        /// 所以每次都占用一个新槽位（种子本身无限使用，不消耗）。
        ///
        /// ★ v185：等级不再是常量 ★
        ///   商店货架现在会卖 2 级 / 3 级种子（`ShopOffer.Level`）⇒ 买进来的种子
        ///   必须带着**这一格商品的等级**进袋 ✓
        ///   省略 `level` 的调用方仍得 1 级（= `SeedSlot.StartingLevel`）⇒
        ///   既有调用（自检夹具、开局选种、赠送种子）行为**逐位不变** ✓
        /// </summary>
        public bool AddSeed(SeedDef def, string mutationId, int quality, out string reason, int level = SeedSlot.StartingLevel)
            => AddSeed(def, mutationId, quality, out reason, level, out _);

        /// <summary>
        /// 同上，并**回报落进了第几个槽**（`slotIndex`，失败时 -1）。
        ///
        /// 多这一个 `out` 是为了让"买到的种子立刻成为当前选中项"能写成**一次**调用 ——
        /// 调用方若自己再调 `FirstEmpty()` 猜一遍，就出现了"槽位的第二个真相"，
        /// 而它在下一次 `AddSeed` 之后就可能和实际落点不同 ✗
        /// </summary>
        public bool AddSeed(SeedDef def, string mutationId, int quality, out string reason, int level, out int slotIndex)
        {
            reason = null;
            slotIndex = -1;
            int idx = FirstEmpty();
            if (idx < 0)
            {
                reason = "种子袋已满，先合成或出售";
                return false;
            }
            _slots[idx].Def = def;
            _slots[idx].MutationId = mutationId;
            _slots[idx].Quality = TableDatabase.Tuning.quality.Clamp(quality);
            _slots[idx].Level = Mathf.Clamp(level, SeedSlot.StartingLevel, SeedSlot.MaxLevel);
            slotIndex = idx;
            return true;
        }

        /// <summary>取用一种种子：种子无限，只要求槽里有这种东西。</summary>
        public bool TryConsume(int index, out SeedSlot slot)
        {
            slot = null;
            if (index < 0 || index >= _slots.Length) return false;
            SeedSlot s = _slots[index];
            if (s.IsEmpty) return false;
            slot = s;
            return true;
        }

        public bool Sell(int index, int wave, out int refund)
        {
            refund = 0;
            if (index < 0 || index >= _slots.Length) return false;
            SeedSlot s = _slots[index];
            if (s.IsEmpty) return false;
            // v109：返还额**依赖当前波次**（= 该商品此刻实际价格的 40%），
            // 所以波次必须由调用方传进来——种子袋自己不知道现在打到第几波。
            // 新：返还额还乘**等级倍率** ⇒ 合成出来的高等级种子卖得更贵 ✓
            refund = s.SellValueFor(wave);
            ClearSlot(index);
            // ★ 根系 B：**卖掉的正是"当前选中"那一格 ⇒ 指针必须立刻搬家** ★
            //   病因（用户原话："卖掉背包里已有的植物，再买入新的植物，没有替换"）：
            //     `ClearSlot` 把槽位清空，而 `Selected` 一个字节没动 ⇒ `SelectedSlot`
            //     返回一个 `Def == null` 的空槽 ⇒ ① HUD 的"当前种子"仍指着一格空气
            //     ② 播种那条链读到的 `sowSeed` 是 null ⇒ 按了 E 什么都不发生。
            //     玩家读到的就是"卖了/买了都没换" ✓
            //   修法：清空之后统一走 `NormalizeSelection()`（非空 ⇒ 不动；空 ⇒ 第一个非空槽或 -1）✓
            NormalizeSelection();
            return true;
        }

        public int OccupiedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++) if (!_slots[i].IsEmpty) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// 按 waves.json 的参数刷怪，并判定本回合是否走完。
    ///
    /// v165：**它不再是"波次"驱动器** —— 表里的 `index` 现在是"第几分钟"的别名，
    /// 本回合的结束点由 <see cref="Clock.SecondsPerMinute"/> 单点决定，
    /// BOSS 由 <see cref="BossSchedule"/> 单独刷新（本类不再刷 BOSS）。
    /// </summary>
    public sealed class WaveDirector
    {
        private int[] _spawned = Array.Empty<int>();
        private float _spawnTimer;

        public WaveDef Wave { get; private set; }
        public float Elapsed { get; private set; }
        /// <summary>开局免刷怪的种植时间。</summary>
        public float GraceSeconds { get; private set; }
        public float GraceRemaining { get; private set; }
        /// <summary>扣除种植时间后的战斗计时：分钟时钟与批次时间都以它为基准。</summary>
        public float CombatElapsed => Mathf.Max(0f, Elapsed - GraceSeconds);
        public bool InGrace => GraceRemaining > 0f;
        public float GraceLeft => Mathf.Max(0f, GraceRemaining);

        // v165：删除 `Duration` 与 `Remaining` 两个属性 ✗
        // 它们都是 `Wave.Duration`（30~90 秒）的派生视图：
        //   `Remaining` 的唯一读点是顶部"剩余 Ns"（已删）
        //   `Duration` 的读点是 IsComplete 与两条兜底清场（已改成 60 秒时钟）
        // 留着它们 = 留一条"回合计时是 30~90 秒"的假线索 ✗

        /// <summary>
        /// 本回合是否落在 waves.json 标记的 BOSS 条目上。
        ///
        /// ★ 保留 ★ 它仍有活读点：RunSession 的两条兜底清场（BOSS 分钟不清场）、
        /// 顶部 UI、自检。
        /// 但 **它不再驱动刷怪** —— 表里的 isBoss 曾经让本类自己刷一只 BOSS，
        /// 那条路径已删除（BOSS 的唯一入口是 <see cref="BossSchedule"/>）✓
        /// </summary>
        public bool IsBossWave => Wave != null && Wave.IsBoss;

        public void StartWave(int index)
        {
            Wave = TableDatabase.GetWave(index);
            Elapsed = 0f;
            _spawned = Wave == null ? Array.Empty<int>() : new int[Wave.Batches.Length];
            GraceSeconds = Mathf.Max(0f, TableDatabase.Tuning.waves.graceSeconds);
            GraceRemaining = GraceSeconds;
            _spawnTimer = Mathf.Max(1f, TableDatabase.Tuning.waves.spawnIntervalStart);
            _pending.Clear();
        }

        public void Tick(float dt, World w)
        {
            if (Wave == null) return;
            // ★★ v300：训练场 ⇒ 不刷任何波次敌人，也不发预警 ★★
            //   为什么放在 `Elapsed += dt` **之前**：训练场没有"回合"这个概念，
            //   本分钟的进度（`CombatElapsed`）与刷怪计时器都不该被推着走。
            //   关掉开关时（`TrainingMode.NoWaves == false`）这一行**不成立**，
            //   下一个语句仍然是原来的 `Elapsed += dt;` ⇒ 战斗行为逐字节相同 ✓
            if (TrainingMode.NoWaves) return;
            Elapsed += dt;

            // 开局种植时间：只种地，不刷怪（跨过结束点的那一帧就允许开始刷怪）
            if (GraceRemaining > 0f)
            {
                GraceRemaining -= dt;
                if (GraceRemaining > 0f) return;
            }

            // v10 刷怪：每 5 秒补一批，随**本分钟进度**逐步缩短到最低间隔；
            // 每批数量由分钟数与进度决定，并用"场上存活上限"控制难度。
            WavesCfg cfg = w.Tuning.waves;
            // v165：进度分母从 `Wave.Duration`（30~90 秒）改成【一分钟】——
            //       "进度"现在的语义是"本分钟走完了多少"，与 IsComplete 用同一条时钟 ✓
            float progress = Mathf.Clamp01(CombatElapsed / Clock.SecondsPerMinute);
            float interval = Mathf.Lerp(
                Mathf.Max(1f, cfg.spawnIntervalStart),
                Mathf.Max(0.5f, cfg.spawnIntervalMin),
                progress);

            // ★ v165：删掉"BOSS 分钟只出 BOSS"整段 ★
            //
            // 旧行为：waves.json 里 isBoss=true 的两条（index 10 / 20）会让**这里再刷一只 BOSS**，
            //        并当分钟 `return` ⇒ 整分钟不刷小怪 ✗
            // 而 BOSS 现在的唯一入口是 `BossSchedule`：10/18/24/28/30/32… 分钟 ✓
            // 两条刷怪路径 = 两套真相，实机后果是：
            //   第 9 分钟（表 index 10）刷一只、第 10 分钟（时刻表）又刷一只，**同时在场** ✗
            // 而 BossSchedule.DeferAfter 的注释里写得很明确："并存会叠出多个 Boss，
            // 而 Boss 有 90 秒限时与冲撞机制，两个同时在场基本等于无法处理" ✗
            // 所以这里整段删除 —— 收敛到单一入口 ✓
            // `IsBossWave` 属性保留（HUD / 兜底清场 / 自检三处仍在读它）✓

            // 预警队列：倒计时结束后真正生成（被抑制的只闪不生成）
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                _pending[i].Timer -= dt;
                if (_pending[i].Timer > 0f) continue;
                if (!_pending[i].Suppressed)
                {
                    if (_pending[i].Pig) SpawnOne(new WaveBatchDef { EnemyId = "pig", Count = 1, StartAt = 0f, Interval = 1f }, w, _pending[i].Pos);
                    else SpawnOne(PickBatch(w), w, _pending[i].Pos);
                }
                _pending.RemoveAt(i);
            }

            _spawnTimer -= dt;
            if (_spawnTimer > 0f) return;
            _spawnTimer = interval;

            // v116：**刷怪数量也改成连续成长**（需求："有多少波就成长多少次"）。
            //
            // 旧写法是 `Clamp(系数 × (min(2^(w-1), 64) − 1), 0, 上限)` ——
            // 第 7 波就同时撞上指数封顶与数量上限，之后**纹丝不动**。
            // 上一轮我只改了 HP/伤害（那部分已无封顶），数量这一半确实是停的。
            //
            // 新曲线用**平方根**，与 HP/伤害的「线性+二次」刻意相反：
            //   数值 —— 后期加速（玩家变强，敌人要跟上）
            //   数量 —— 后期减速（数量加速会撞性能悬崖，而且"人海"比"变硬"更快摧毁手感）
            // 两者都**永不停止**，只是形状不同。
            float t = Mathf.Max(0f, w.WaveIndex - 1f);
            float grow = Mathf.Sqrt(t);

            // v123：检测到进入新回合就复位小猪保底（自包含，不依赖外部调用）。
            if (_pigWaveMark != w.WaveIndex) { _pigWaveMark = w.WaveIndex; _pigSpawnedThisWave = false; }
            // v127：**解放敌人数量上限**（需求"不做限制"）。
            //
            // 原来的上限是**三道闸门**，任何一道都会拦住增长：
            //   ① `if (w.Enemies.Count >= maxAlive) return;`         —— 场上太多就整拍不刷
            //   ② `count = Min(count, maxAlive - Enemies.Count)`     —— 单拍刷量被剩余空间截断
            //   ③ `hardCap` 抑制（下面的 wouldExceed 分支）           —— 超硬上限只预警不生成
            // 只关①或②都还会被另外两道拦住，所以要一起放开。
            //
            // ★ 用**配置开关**而不是删代码 ★
            // 数量无界意味着"敌人只增不减地累积"——这在性能上是**最危险的一类改动**：
            // 它不会立刻崩，而是在某一波突然掉到个位数帧率。
            // 所以做成 `aliveLimitEnabled`，出事时改一个 false→true 就能回到原来的行为，
            // 不用回滚代码。默认仍为 true（保持既有平衡），需求要求时才关。
            int maxAlive = int.MaxValue;
            int hardCap = int.MaxValue;
            if (cfg.aliveLimitEnabled)
            {
                maxAlive = Mathf.Max(4, cfg.maxAliveBase + Mathf.RoundToInt(cfg.maxAlivePerWave * w.WaveIndex)
                                        + Mathf.RoundToInt(cfg.maxAliveGrowth * grow));
                hardCap = Mathf.Max(maxAlive, cfg.maxAliveCap);
                if (w.Enemies.Count >= maxAlive) return;   // 场上够多了，等下一拍
            }

            int count = Mathf.Max(1, cfg.spawnBaseCount
                                     + Mathf.FloorToInt(cfg.spawnCountPerWave * w.WaveIndex)
                                     + Mathf.FloorToInt(cfg.spawnCountByProgress * progress)
                                     + Mathf.FloorToInt(cfg.spawnCountGrowth * grow));
            if (cfg.aliveLimitEnabled) count = Mathf.Min(count, maxAlive - w.Enemies.Count);
            for (int i = 0; i < count; i++)
            {
                // v123：**每回合保底一只小猪**（需求）。
                //
                // 概率从 5% 降到 1% 之后，一局 20 波"一次都不出猪"的概率约 0.99^20 ≈ 82% ——
                // 而小猪是**肥料的唯一来源**。没有它，"肥料 → 催熟 / 收割成熟地"整条循环直接断掉，
                // 玩家会发现"肥料一直是 0/10"而不知道原因。
                //
                // 所以保底不是锦上添花，是**保证核心循环可玩**。
                // 做法：本波还没出过猪时，把**本次生成**强制成猪 —— 于是每波必有一只，
                // 且出现在波次开头（玩家一眼能看到、能提前决定要不要去拿）。
                // ★ v191（用户需求③）：**小猪概率改由属性「小猪刷新率」决定** ★
                //
                // 改前：`w.Rng.Chance(Mathf.Clamp01(cfg.pigChance))` —— 一个写死的常量（1%）✗
                // 改后：属性存**等级**（0..3，`StatId.PigRate`）⇒ 查 `TuningDef.PigRateByLevel`
                //       得实际概率（2% / 4% / 6% / 8%）✓
                //
                // ★ 为什么用等级、为什么照抄爱心那一套 ★
                //   需求原话是"与爱心掉落率**相同的机制与数值**" ⇒ 同一套 4 级、同一组数值、
                //   同一组名字（低/中/高/MAX）✓ 而"等级"这种存法的好处是道具 = `add: 1`
                //   （天然升一级）、`maxStack: 3`（天然"到 MAX 不能再买"）✓
                //
                // ★ 为什么**不能**直接 `Chance(Stats.Get(PigRate))` ★
                //   那样会拿等级当概率用：等级 3 ⇒ 300% ⇒ 必出 ✗（爱心那条踩过同一个坑，
                //   见 `Actors.Cleanup` 里 `heartLv` 的注释）⇒ 必须过一层**查表** ✓
                //
                // ★ `w.Rng` 的取数**没有变** ★
                //   `Chance(p)` 无论 p 是多少都只 `NextUInt()` 一次 ⇒ 概率变了，
                //   但**随机流的位置不变** ⇒ 同种子下"其它随机事件"不受影响 ✓
                //   （变的只有"这次掷出的是不是猪"这个结果本身 —— 那是需求的**目的**✓）
                bool forcePig = !_pigSpawnedThisWave;
                int pigLv = w.Player != null ? Mathf.RoundToInt(w.Player.Progress.Stats.Get(StatId.PigRate)) : 0;
                float pigChance = TuningDef.PigRateOf(pigLv);
                if (forcePig || w.Rng.Chance(Mathf.Clamp01(pigChance)))
                {
                    Vector2 pigPos = PickSpawnPos(w);
                    _pending.Add(new PendingSpawn { Pos = pigPos, Timer = Mathf.Max(0.1f, cfg.spawnWarnSeconds), Pig = true });
                    Bus.Publish(new SpawnWarningEvent { Pos = pigPos, Seconds = cfg.spawnWarnSeconds });
                    _pigSpawnedThisWave = true;
                    continue;
                }
                Vector2 pos = PickSpawnPos(w);
                bool wouldExceed = w.Enemies.Count + _pending.Count >= hardCap;
                // v14：所有生成都先出 2 秒预警；超过硬上限时只预警、不真正生成
                _pending.Add(new PendingSpawn { Pos = pos, Timer = Mathf.Max(0.1f, cfg.spawnWarnSeconds), Suppressed = wouldExceed });
                Bus.Publish(new SpawnWarningEvent { Pos = pos, Seconds = cfg.spawnWarnSeconds });
            }
        }

        /// <summary>
        /// 按波次挑本拍刷什么怪：**以基础近战怪为主**。
        /// v48：远程与精英的占比大幅下调（远程 32%→12%，精英 18%→5%），精英推迟到第 8 波才出现，
        /// 基础近战怪占比约 84%。
        /// 公开是为了让自检能直接统计刷怪构成，不必跑一整局。
        /// </summary>
        public static WaveBatchDef PickBatch(World w)
        {
            int wave = w.WaveIndex;
            string id = "grunt";
            if (wave >= 8 && w.Rng.Chance(0.05f)) id = "jumper";
            else if (wave >= 3 && w.Rng.Chance(0.12f)) id = "shooter";
            return new WaveBatchDef { EnemyId = id, Count = 1, StartAt = 0f, Interval = 1f };
        }

        // v165：`SpawnBoss` 已删除 —— 它是"表里的 isBoss 分钟刷 BOSS"那条路径的唯一调用者，
        // 而那条路径整段删除（BOSS 的唯一入口是 BossSchedule）。刷怪的公开入口
        // `SpawnOne(WaveBatchDef, World)` 保持不变，RunSession 的 Boss 刷新仍在用它 ✓

        /// <summary>v14：预警中的待生成敌人。</summary>
        private sealed class PendingSpawn { public Vector2 Pos; public float Timer; public bool Suppressed; public bool Pig; }
        private readonly List<PendingSpawn> _pending = new List<PendingSpawn>();

        /// <summary>
        /// v123：本回合是否已经出过小猪（用于"每回合保底一只"）。
        /// 回合开始时由 `ClearTransientCombatState` 复位。
        /// ★ 声明必须和 `_pending` 在同一个类里 ★ —— 我先是把它放到了 RunSession 的另一处
        /// （`_beamFiring` 旁边），而刷怪逻辑在**嵌套类**里，于是编译报"名字不存在"。
        /// 这已经是这一批第四次"成员放错类"，教训：**加字段前先确认拥有它的方法在哪个类**。
        /// </summary>
        private bool _pigSpawnedThisWave;

        /// <summary>v123：上一次刷怪时的波次号，用于检测"进入新回合"并复位小猪保底。
        /// 放在刷怪类内部自包含地复位，避免跨类访问。</summary>
        private int _pigWaveMark = -1;

        /// <summary>随机挑一个刷怪点（预警与真正生成用同一个点）。</summary>
        private Vector2 PickSpawnPos(World w)
        {
            float a = w.Rng.Range(0f, 6.2831853f);
            float r = w.Rng.Range(w.Tuning.waves.spawnRingInner, w.Tuning.waves.spawnRingRadius);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        // v155：改为 public —— 计时制的 Boss 时间表需要从 RunSession 调用它 ✓
        // （它原本是子系统私有方法，而 Boss 刷新现在由【时间】触发，不在子系统内部 ✗）
        public void SpawnOne(WaveBatchDef b, World w) => SpawnOne(b, w, Vector2.zero);

        private void SpawnOne(WaveBatchDef b, World w, Vector2 at)
        {
            // ★★ v300：训练场 ⇒ 这条生成路径整体关闭 ★★
            //   为什么守在**这里**而不只守 `Tick`：本方法有**两个**活调用方 ——
            //     ① `Tick` 的预警队列（波次小怪，已由上面那条 `NoWaves` 拦住）
            //     ② `RunSession` 的 BOSS 时刻表（`Waves.SpawnOne(...)`，RunSession.cs:4790 附近）
            //   只守 `Tick` 的话，第 10 分钟 BOSS 照样会刷进训练场 ✗
            //   守在这里 ⇒ **两条路径一次拦干净**（BOSS 与小怪共用这一个生成出口）✓
            //   关掉开关时这一行不成立，原语句（`EnemyDef def = …`）原样执行 ✓
            if (TrainingMode.NoWaves) return;
            EnemyDef def = TableDatabase.GetEnemy(b.EnemyId);
            Vector2 pos;
            if (at != Vector2.zero && def.Kind != EnemyKind.Boss) pos = at;
            else if (def.Kind == EnemyKind.Boss)
            {
                pos = new Vector2(0f, Mathf.Min(15f, w.Tuning.farm.arenaHalf - 2f));
            }
            else
            {
                float a = w.Rng.Range(0f, 6.2831853f);
                float r = w.Rng.Range(w.Tuning.waves.spawnRingInner, w.Tuning.waves.spawnRingRadius);
                pos = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            pos = World.ClampToArena(pos, def.Radius, w.Tuning.farm.arenaHalf);
            int bossIndex = def.Kind == EnemyKind.Boss ? Mathf.Max(1, w.WaveIndex / 5) : 1;
            w.SpawnEnemy(b.EnemyId, pos, w.WaveIndex, bossIndex);
        }

        /// <summary>v10：周期刷新下"批次"概念已废弃，恒视为刷完（结束条件只看时间与清场）。</summary>
        private bool AllSpawned() => true;

        /// <summary>
        /// 调试/自动化：直接推进到本分钟结束（再把场上清空即可立刻收尾）。
        /// v165：结束点从 `Wave.Duration` 改成【一分钟】，与 <see cref="IsComplete"/> 同一条时钟 ✓
        /// </summary>
        public void SkipToEnd()
        {
            if (Wave == null) return;      // 防御性兜底（表为空才走到；越界已由 GetWave 复用最后一条拦住）
            GraceRemaining = 0f;
            Elapsed = Mathf.Max(Elapsed, Clock.SecondsPerMinute + GraceSeconds);
            for (int i = 0; i < _spawned.Length; i++) _spawned[i] = Wave.Batches[i].Count;
        }

        /// <summary>
        /// 本回合是否走完。
        ///
        /// ★ v165：不再按 `Wave.Duration` 判，改成按【60 秒时钟】★
        /// 旧节奏是 waves.json 里 30/36/42/…/90 秒（**全部 &lt; 60 秒**），
        /// 也就是"每 30~60 秒一波并强制进商店"——与"每分钟一回合"是两套真相 ✗
        /// 新玩法是"第 N 分钟 = 第 N 个成长刻度"，一回合恒为 60 秒 ✓
        ///
        /// 结束条件本身不变：种植时间结束 + 分钟走完 + 全部刷完 + **场上清空**。
        /// 旧版那条"BOSS 波只看清场"的特例随同一段旧节奏删除：那一分钟的 BOSS
        /// 现在由 <see cref="BossSchedule"/> 刷，不由本表驱动 ✓
        /// </summary>
        public bool IsComplete(World w)
        {
            if (Wave == null) return true;      // 防御性兜底，保留（不再被日常触发）
            if (InGrace) return false;
            if (!AllSpawned()) return false;
            if (CombatElapsed < Clock.SecondsPerMinute) return false;
            return w.Enemies.Count == 0;
        }
    }

    /// <summary>
    /// 商店的**三类商品**：种子 / 插件 / 道具。
    ///
    /// ★ 第三类由 `Tool`（工具升级）改成 `Plugin`（插件）★
    /// 旧的三类是"种子 / 永久属性道具 / 武器升级"，而工具升级的权重
    /// （`tuning.json` 的 `shop.toolWeight`）是 **0** ⇒ 那个货位从来抽不出来 ✓
    /// 现在换成插件，权重是 25 ⇒ 三类都真的会出现在货架上 ✓
    ///
    /// **不留 `Tool` 兼容别名** ✗ —— 留着它，`switch` 里就会多一条永远走不到的分支，
    /// 而"编译通过"会让人以为工具升级还在商店里卖 ✓（假删除比不删更糟）
    /// </summary>
    public enum OfferKind { Seed, Plugin, Item, Sold }

    public sealed class ShopOffer
    {
        public OfferKind Kind;
        public SeedDef Seed;
        public string MutationId;
        /// <summary>种子品质档位（0 白 … 4 金）。</summary>
        public int Quality;
        /// <summary>
        /// ★ v185：种子商品的**合成等级**（1 / 2 / 3）—— 与品质是**两条正交的轴** ★
        ///
        /// ★ 用户说的"2 级 / 3 级种子"指的是这一个字段，不是 `Quality` ★
        ///   · `Quality`（0..4）= D/C/B/A/S 档位，由 `def.RarityTier` 决定，买定不变 ✓
        ///   · `Level`（1..3）= 这一颗种子"养到第几级"，由 `SeedBag.Merge` 的 **2 合 1** 链升上去 ✓
        ///   依据：`Defs.cs:549-553`（品质"不再参与合成"）+ `SeedBag.CanMerge/Merge`
        ///   （两槽同种同级 ⇒ 目标槽 `Level++`，材料槽被消耗）✓
        ///
        /// ★ 非种子商品恒为 1 ★ —— 插件 / 道具不走等级轴，读它没有意义 ✗
        /// （`SeedSlot.StartingLevel` = 1，即"买来就是 1 级"这条老规则 ✓）
        /// </summary>
        public int Level = SeedSlot.StartingLevel;
        public ItemDef Item;
        /// <summary>
        /// 插件商品的静态定义（SO 资产）。
        /// 购买后只入 `PlayerGoods.Plugins`（`WeaponSlot.None`）✗ ——
        /// **装到哪把武器由玩家之后自己决定**，不由购买动作替他决定 ✓
        /// </summary>
        public PluginAsset Plugin;
        public int Price;
        public string Title;
        public string Subtitle;
        /// <summary>v15：锁定后不会被刷新掉，并保留到下一回合。</summary>
        public bool Locked;

        public string QualityName => TableDatabase.Tuning.quality.NameOf(Quality);

        public string Label()
        {
            // v75：名字前不再挂品质前缀。
            // 旧版是 "[D] [普通] 毒蘑菇"、" [道具] 放大镜" ——
            //   ① 品质在**下一行**已经单独显示了（GradeText），这里再挂一次是重复
            //   ② 前缀把名字挤到后面，一行的宽度被吃掉近一半，长名字还会被裁
            // 卡片上换成"图标 + 品质色"来表达稀有度，本来就比文字前缀更直观。
            switch (Kind)
            {
                case OfferKind.Seed:
                    return MutationId == null ? Seed.Name : Seed.Name + "·变异";
                case OfferKind.Item: return Item.Name;
                case OfferKind.Plugin: return Plugin != null ? Plugin.displayName : "插件";
                default: return Item != null ? Item.Name : "商品";
            }
        }
    }

    public sealed class ShopSystem
    {
        public readonly List<ShopOffer> Offers = new List<ShopOffer>();
        public int RerollCount;

        /// <summary>
        /// ★ v168：**刷新费减免**（道具机制 `reroll_discount` 的消费点）★
        ///
        /// 由 `RunSession.SyncItemRules()` 从已购道具算出并写进来 ✓
        ///
        /// ★ 为什么是"存在这里的一个数"，而不是让 `RerollCost` 去遍历道具表 ★
        /// ① 那会让定价函数依赖 `TableDatabase` + `RunSession`，把一处纯计算变成三处耦合 ✗
        /// ② **更重要**：`ImGuiHud`（本任务禁止修改）也用 `s.Shop.RerollCost(s.T)` 显示价格。
        ///    如果扣费按打折、显示按原价，界面上就会**撒谎** ✗
        ///    存在这里 ⇒ 唯一的定价函数同时服务扣费与显示，两者不可能不一致 ✓
        ///
        /// 下限由 `RerollCost` 的 `Max(1, …)` 保证：减免再多也不会出现"刷新倒给钱" ✓
        /// </summary>
        public int RerollDiscount;

        /// <summary>
        /// ★★ v192：**刷新价封顶已移除** —— 用户需求④："商店刷新价格和抽卡刷新价格被设置了极限，改为无限增长" ★★
        ///
        /// ── 那个"极限"在哪（找到了，就在这里）──
        /// v180 给 `RerollMult` 加过一句 `Mathf.Min(2^count, RerollMaxMult)`：
        ///   `count = 0..∞ ⇒ 倍数 3/3/6/12/**24**/**24**/**24**…`（第 4 次触顶 = 基础 ×8）
        /// 这正是用户看到的"被设置了极限" ✓ 现在上限从**倍率**上整条去掉：
        ///   第 1..12 次 = 3 / 6 / 12 / 24 / 48 / 96 / 192 / 384 / 768 / 1536 / 3072 / 6144（基础 3）
        ///   第 10 波（基础值 = round(3 × 1.13⁹) = 9）⇒ 9 / 18 / 36 / 72 / 144 / 288 … ✓
        ///
        /// ⚠️ **这个常量仍然是 `public`** —— 不是因为还在生效，而是因为
        /// `Assets/Scripts/Debug/ProbeV189.cs:238` 在**打印**它（"封顶倍数 = …"）。
        /// 删掉它会让那个探针编译不过，而探针不在本任务的允许改动名单里 ✗
        /// ⇒ 保留成**历史值**并在此处声明"已退役"：它现在只表示"v180~v191 曾经用过的那条上限" ✓
        /// （`SelfCheck` 与 `ProbeV192Num` 都改为断言"**单调倍增、无双顶**"，即它不再被任何定价路径读取 ✓）
        /// </summary>
        public const int RerollMaxMult = 8;

        /// <summary>
        /// ★ v189：**刷新价的波次**（用户需求②："基础值也随时间增加，同一套公式"）★
        ///
        /// 为什么它是一个**字段**而不是 `RerollCost(t, wave)` 的参数：
        /// `ImGuiHud`（本任务禁止修改）里的显示点写的是 `s.Shop.RerollCost(s.T)` —— 一个参数 ✗
        /// 若改成两参数，界面上"按钮写的价"与"扣掉的价"就会分叉（而 HUD 是唯一不能改的那一侧）✓
        /// 于是由 **`Refresh`** 在每次建立货架时把当前波次写进来（商店唯一的正式入口都会经过它）：
        ///   商店车 → `TickShopTruck` → `ResetForPhase` + `Refresh` ✓
        ///   调试口 → `ForceShopForDebug` → 同上 ✓
        ///   HUD 读价时货架刚建好 ⇒ 读到的一定是**当前波次**（不是上次商店的波次）✓
        /// 缺省 1 = 波次曲线第 1 波（倍率 1.0）⇒ 未建货架时读到的基础值与改前一致 ✓
        /// </summary>
        public int Wave = 1;

        /// <summary>
        /// **刷新价的"基础值"** —— 与商品走**同一条波次曲线**（`shop.priceWaveStep` 的 1.13^(波−1)）✓
        ///
        /// ★ 改前的病根（用户报的"刷新价基础值是固定的"）★
        /// 旧式子 `round(rerollBase × 2^次数)` 里 `rerollBase` 是从 `tuning.json` 直接读的**常数 3** ⇒
        /// 商品在涨（`PriceOf` 带 `WavePriceMult`）、刷新费一分钟都没涨过 ⇒
        /// 到第 10 分钟商品涨到 1.13⁹ ≈ 3.0 倍，而刷新还是 3 金 ⇒ 玩家靠刷新把商店当自动售货机 ✗
        /// 现在基础值也乘同一条曲线 ⇒ 第 10 分钟首次刷新 = round(3 × 3.00) = **9 金** ✓
        /// </summary>
        public static int RerollBaseAt(TuningDef t, int wave)
            => Mathf.Max(1, Mathf.RoundToInt(t.shop.rerollBase * t.shop.RerollWaveMult(wave)));

        /// <summary>
        /// 第 <paramref name="count"/> 次刷新的**倍数** = 1.5^count —— 不封顶。
        ///
        /// ★★ v195：底数 2 → **1.5**（用户原话："重抽几个重新计算，前期个位数，
        ///    后期 20 分钟初次重抽价格控制在以 10 为单位，30 分钟能达到控制在 100 内"）★★
        ///
        /// 算式（基础价 3、`rerollWaveStep` 0.0682、`post20PriceSlope` 0.05）：
        ///   第 1 分钟基础值 round(3×1.00) = 3 ⇒ 序列 **3 / 5 / 7 / 10**（前三次个位数 ✓）
        ///   第 20 分钟基础值 round(3×3.50) = **11**（初次重抽就在十位 ✓）
        ///   第 30 分钟基础值 round(3×6.77×1.5) = **30** ⇒ 30 / 45 / 68 / 101（前三次 ≤ 100 ✓）
        /// 若沿用 ×2：第 20 分钟 = 11/22/44/88（可以），但第 30 分钟 = 30/60/120 ✗ 第三次就越过 100 ✓
        /// ⇒ 1.5 是"前期更像个位数、后期更贴 100 以内"的最小改动 ✓
        /// 用 `double` 而不是 `float`：`1.5^24` 之后 float 还能表示，但 `basis × 1.5^n` 会先撞 `int` 上限 ✓
        /// </summary>
        public static double RerollMult(int count)
            => Math.Pow(1.5, Math.Max(0, count));

        /// <summary>
        /// **刷新价的唯一实现**（商店刷新 与 抽卡重抽 共用这一条）✓
        ///
        ///   刷新价 = round( 基础值(波次) × 2^次数 ) − 道具减免，下限 1
        ///   基础值(波次) = round(basePrice × (1+step)^(波−1))
        ///
        /// ★ 为什么商店与抽卡必须共用它 ★
        /// 两处各有自己的一套 `×2^n` 写法是本工程已经吃过一次的亏（`RewardRerollCost` 与
        /// `RerollCost` 各写一遍，结果一份涨一份不涨）⇒ 只有一份实现才不会分叉 ✓
        /// 两者的差别只剩**基础价**（`shop.rerollBase` vs `levelReward.rerollBasePrice`）与**减免**
        /// （抽卡没有 `reroll_discount` 这件道具）—— 这两个都是**入参**，不是第二个算式 ✓
        ///
        /// ★ 两处取整的顺序是刻意的 ★
        /// 先把基础值取整、再乘倍数、最后取整 ⇒ 屏幕上每一个数都是"整数金"，且
        /// `RerollBaseAt` 可以**单独**被 HUD/探针读出来对照（"基础值随波次涨"的判据就是它）✓
        ///
        /// ★ v192：**去掉上限 + 溢出保护** ★
        /// 用户要求"无限增长"⇒ 倍数上不再有 `Min(…, 8)` ✓
        /// 但 `int` 只有 2.1e9：基础 9 金时第 **28** 次 = 9 × 2²⁸ = 2.4e9 已经越界 ⇒
        /// 用 `double` 算、**撞顶就返回 `int.MaxValue`**（不是回绕成负数）✗
        /// 这一步不是"新的极限"，而是"整数类型本身的边界"：到那一步价格已经是 21 亿金，
        /// 玩家一辈子攒不到 ✓
        /// </summary>
        public static int RerollPrice(TuningDef t, int basePrice, int wave, int count, int discount)
        {
            // ★ v195：基础值走**刷新价自己的**波次曲线（`RerollWaveMult`），商品仍走 `WavePriceMult` ✓
            int basis = Mathf.Max(1, Mathf.RoundToInt(basePrice * t.shop.RerollWaveMult(wave)));
            double raw = basis * RerollMult(count);
            int price = raw >= int.MaxValue ? int.MaxValue : Mathf.RoundToInt((float)raw);
            return Mathf.Max(1, price - Mathf.Max(0, discount));
        }

        // v37：刷新价格按倍数增长（3 → 6 → 12 → 24 …）；v168：再减去道具带来的减免
        // v180：★ 倍数**封顶 `RerollMaxMult`** ⇒ 3 / 6 / 12 / 24 / 24 / 24 … ★
        //       封顶放在**倍率**上（不封最终价）⇒ 道具减免仍作用在同一个口径上 ✓
        // v189：★ **基础值也随波次涨**（用户需求②）⇒ 3/6/12/24 → 第 10 波 9/18/36/72 ★
        // v192：★★ **上限整条去掉**（用户需求④"改为无限增长"）★★ 现在：
        //   第 1 波 3 / 6 / 12 / 24 / **48 / 96 / 192 / 384 / 768 / 1536 / 3072 / 6144**…
        //   第 10 波（基础 9）9 / 18 / 36 / 72 / 144 / 288 / …（同一个式子、同一个倍增）
        //   判据：`ProbeV192Num` 打 1..12 次序列，要求**严格 2 倍递增、无双顶** ✓
        public int RerollCost(TuningDef t)
            => RerollPrice(t, t.shop.rerollBase, Wave, RerollCount, RerollDiscount);

        public void ResetForPhase()
        {
            RerollCount = 0;
        }

        /// <summary>v15：刷新时保留锁定项（先放回锁定的，再补满其余槽位）。</summary>
        /// <summary>v20：同一件商品只保留一次（锁定去重）。</summary>
        private static bool ContainsSame(List<ShopOffer> list, ShopOffer o)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Kind == o.Kind && list[i].Seed == o.Seed
                    && list[i].Item == o.Item && list[i].Plugin == o.Plugin
                    // ★ v185：**等级也要进这个键** ★ —— 否则同一种的 1 级与 2 级
                    //   会互相当成"同一件商品" ⇒ 两个都锁了也只会保留一个 ✗
                    && list[i].Level == o.Level) return true;
            return false;
        }

        public void Refresh(RunSession s)
        {
            // ★ v189：定价用的波次在**建立货架这一刻**落定（见 `Wave` 的说明）★
            //   放在 `Refresh` 的第一行 ⇒ 同一次调用里"过滤上限 / 重新定价 / 补货"读的是同一个波次 ✓
            Wave = Mathf.Max(1, s.WaveIndex);
            // v20：锁定项去重（同一件商品只保留一次，避免"一次锁了多个"的错觉）
            var keep = new List<ShopOffer>();
            for (int i = 0; i < Offers.Count; i++)
                if (Offers[i].Locked && !ContainsSame(keep, Offers[i])) keep.Add(Offers[i]);
            for (int i = 0; i < s.CarriedOffers.Count; i++)
                if (s.CarriedOffers[i].Locked && !ContainsSame(keep, s.CarriedOffers[i])) keep.Add(s.CarriedOffers[i]);
            s.CarriedOffers.Clear();

            Offers.Clear();
            for (int i = 0; i < keep.Count; i++)
            {
                // ★ v186：**锁定项也要按当前波次重新定价** ★
                //
                // 症状（用户原话）："部分商品价格没有跟随上涨，或者上涨幅度过低" ✓
                // 病因：保留锁定项时**连价格一起原样带过来** ✗
                //   同一货架上的其余货位都由 `RollOne → Roll*` 按 `s.WaveIndex` 现算，
                //   而锁定那一格带着"上一次刷新时的分钟"的价 ⇒
                //   一分钟一分钟过去，那一格的价格**冻在原地**，别的都在涨 ✓
                //   ⇒ 玩家看到的就是"部分商品没涨"，而且越是锁着等的那件差得越多 ✓
                // 为什么"锁价"不能当设计：锁定是"这件商品我想要、别刷掉"，
                //   不是"这个价格我要保住" —— 价格链 basePrice × 档位 × (1+13%)^(波-1)
                //   描述的是**货位在当前分钟该值多少钱**，与"锁没锁"无关 ✓
                // 修法：定价仍走**唯一实现** `PriceOf`（与 `RollSeed/RollPlugin/RollItem` 同一个函数），
                //   只是把它也用在保留项上 ⇒ 不可能出现第二套算式 ✓
                ShopOffer k = keep[i];
                // ★ v189：**锁定项也要过上限**（用户需求③）★
                //   锁定发生在"还没买够"的时候；玩家可能在这一分钟里从别处凑够了上限
                //   （合成种子、又买了一件道具）⇒ 若只过滤新抽的货位，锁定那一格会
                //   **一直挂着一件买不了的商品**占住货架（点了只会报"已叠满"）✗
                //   与"满上限的货架不再出现"是同一条规矩，只是它必须对新货位与锁定货位**都成立** ✓
                if (OfferAtCap(s, k)) continue;
                k.Price = PriceOf(k, s.WaveIndex);
                Offers.Add(k);
            }
            int slots = Mathf.Max(1, s.T.shop.slots);
            for (int i = Offers.Count; i < slots; i++) Offers.Add(RollOne(s));
        }

        /// <summary>
        /// ★ v186：**某个货位在某一分钟该卖多少钱** —— 定价的**唯一实现** ★
        ///
        /// 三类商品的式子（数值来源都在 `tuning.json`，本函数里没有第二个常数）：
        ///   种子   `SeedLevelPrice( basePrice × 档位倍率 × 1.13^(波-1), 等级 )`
        ///   插件   `basePrice × 档位倍率 × 1.13^(波-1)`
        ///   道具   `basePrice × 档位倍率 × 1.13^(波-1)`
        /// ★ 三类是**同一个式子**，差别只在输入（种子还要乘等级链）★
        /// 所以"某一类不涨价"在结构上不可能发生 —— 只要所有取价处都走这里 ✓
        /// `DebugApi.ProbePriceRise` 会把三类 × 第 1/5/10/15 波的表直接打出来 ✓
        /// </summary>
        public static int PriceOf(ShopOffer o, int wave)
        {
            if (o == null) return 0;
            int w = Mathf.Max(1, wave);
            switch (o.Kind)
            {
                case OfferKind.Seed:
                    if (o.Seed == null) return Mathf.Max(1, o.Price);
                    return SeedLevelPrice(SeedSlot.ShopPriceFrom(o.Seed.Price, o.Quality, w), o.Level);
                case OfferKind.Plugin:
                    if (o.Plugin == null) return Mathf.Max(1, o.Price);
                    return SeedSlot.ShopPriceFrom(o.Plugin.basePrice, o.Quality, w);
                case OfferKind.Item:
                    if (o.Item == null) return Mathf.Max(1, o.Price);
                    return SeedSlot.ShopPriceFrom(o.Item.Price, o.Quality, w);
                default:
                    return Mathf.Max(0, o.Price);     // 已售出/空位：没有价可算，原样返回
            }
        }

        /// <summary>v15：切换某个商品的锁定状态；锁定的商品会被带到下一回合。</summary>
        public bool ToggleLock(int index, RunSession s, out string reason)
        {
            reason = null;
            if (index < 0 || index >= Offers.Count) return false;
            ShopOffer o = Offers[index];
            // v47：已售出的空位不能锁定——锁定的占位符会被 Refresh 带进下一回合，永久占一个货架位
            if (o.Kind == OfferKind.Sold)
            {
                reason = "已售出的空位不能锁定";
                return false;
            }
            o.Locked = !o.Locked;
            reason = o.Locked ? "已锁定（保留到下一回合）" : "已解锁";
            return true;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v189：**拥有上限 ⇒ 货架不再出现**（用户需求③）
        //
        // 用户原话：「每种/道具达到拥有上限时取消在商店的刷新」
        //
        // ── 改前的状态（grep 证据）──
        //   `TryBuy` 里**已经有**三条上限检查（道具 `ItemStacks >= MaxStack`、插件
        //   `CanStackMore`、种子合成），但它检查的时机是**玩家按下购买那一下** ✗
        //   ⇒ 满上限的商品照样会刷到货架上，玩家点它只得到一句"该道具已叠满" ✓
        //   商店 slots = 4，一件买不了的商品占掉 25% 的货架 —— 越到后期越糟：
        //   玩家上限堆满的东西越多，能买的东西越少，而货架照旧把这些"废格子"摆满 ✓
        //
        // ── 为什么过滤放在**掷商品**这一层，而不是"掷完再替换"──
        //   放在掷这一层才能保证"4 个货位都买得了"（替换法会漏掉"替换出来的又是满的"这条链）✓
        //   而且三条 `Roll*` 各自知道自己掷出来的是什么（种子还带等级），
        //   在这里判就**不需要**把商品反解析成"它属于哪一类上限" ✓
        //
        // ── 三条上限各自的口径 ──
        //   插件：同一件插件（按 `id`）已叠到 `maxStack` ⇒ 满；`Unique` 件有 1 个就满
        //   道具：`ItemStacks[id] >= ItemDef.MaxStack`（`TryBuy` 用的**同一条判据**）⇒ 满
        //   种子：包里已有**同一种、3 级**的种子 ⇒ 满
        //     （3 级是 `SeedSlot.MaxLevel` 封顶 ⇒ 再买同种任何等级都合成不进去；
        //       而种子是无限的（`unlimitedSeeds`）⇒ 第二份同种也不多一种作物 ✓）
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>这件插件是否已经叠到上限（按 `id` 认"同一件"）。</summary>
        public static bool PluginAtCap(RunSession s, PluginAsset a)
        {
            if (s == null || s.Goods == null || a == null) return false;
            List<PluginInstance> list = s.Goods.Plugins;
            for (int i = 0; i < list.Count; i++)
            {
                PluginInstance pi = list[i];
                if (pi == null || pi.Asset == null) continue;
                if (pi.Asset.id != a.id) continue;
                if (pi.Asset.stackRule == StackRule.Unique) return true;      // 唯一件：有 1 个就满
                if (pi.Stacks >= Mathf.Max(1, pi.Asset.maxStack)) return true;
            }
            return false;
        }

        /// <summary>这种道具是否已经叠到上限（与 `TryBuy` 里那条判据**逐字相同**）。
        ///
        /// ★★ v314：**复活道具族走"合计上限"，不走单件 `maxStack`** ★★
        ///   用户原话：「上限 3 个（天使降临和陨落天使的羽毛相加共同决定其拥有数量）」。
        ///   ⇒ 判据必须是 `angel_descent 层数 + fallen_angel_feather 层数 >= 3`，
        ///     而不是"某一件自己叠满 3"——后者的后果是**4 件**（3 天使 + 1 羽毛）✗
        ///
        /// ── 为什么改在这里，而不是在 `TryBuy` 里再写一条 ──
        ///   本函数已经是"到上限了吗"的**唯一实现**：商店掷档（`RollItem` 的过滤）
        ///   与购买前置（`TryBuy`）都读它 ⇒ 一处判据两个出口，不会出现
        ///   "货架摆着一件买不了的" 或 "买的时候才说满" 这种两套真相 ✓
        ///   （`angel_descent` 仍然**可以**进池；它只是到合计上限后就不再刷出 / 买不进）
        /// </summary>
        public static bool ItemAtCap(RunSession s, ItemDef it)
        {
            if (s == null || it == null) return false;
            // ★ v314：复活道具族的**合计上限**（见上面整段）
            if (IsRevivalFamily(it.Id)) return RevivalStacks(s) >= RevivalFamilyCap;
            return s.ItemStacks.TryGetValue(it.Id, out int n) && n >= Mathf.Max(1, it.MaxStack);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ★★ v314：**复活道具族**（「天使降临」+「陨落天使的羽毛」）★★
        //
        //  用户原话：「上限 3 个（天使降临和陨落天使的羽毛相加共同决定其拥有数量）…
        //            … 依然占用位置，替换为一个新的道具「陨落天使的羽毛」」
        //
        //  ★ 为什么是"族"（两件算一个额度），而不是各算各的上限 ★
        //    羽毛是天使的**残渣**：复活一次 = 天使 −1 + 羽毛 +1 ⇒ 两者之和
        //    恰好等于"这个额度被用掉了几格"。若各算各的，玩家可以先买满 3 件天使，
        //    死后得到 3 根羽毛，再回头**又买 3 件天使** ⇒ 总共 6 格额度 ✗ 与用户口径冲突 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>这一件的 id 是否属于复活道具族（唯一实现）。</summary>
        public static bool IsRevivalFamily(string id)
            => string.Equals(id, ItemIds.AngelDescent, StringComparison.OrdinalIgnoreCase)
            || string.Equals(id, ItemIds.FallenAngelFeather, StringComparison.OrdinalIgnoreCase);

        /// <summary>复活道具族**当前占用的额度** = 天使层数 + 羽毛层数（唯一实现）。</summary>
        public static int RevivalStacks(RunSession s)
        {
            if (s == null) return 0;
            int angel = s.ItemStacks.TryGetValue(ItemIds.AngelDescent, out int a) ? Mathf.Max(0, a) : 0;
            int fell = s.ItemStacks.TryGetValue(ItemIds.FallenAngelFeather, out int f) ? Mathf.Max(0, f) : 0;
            return angel + fell;
        }

        /// <summary>
        /// 复活道具族的**合计上限** —— 直接取表里 `angel_descent` 的 `maxStack`（3）。
        ///
        /// ★ 为什么不在这里再写一个 `const int 3` ★
        ///   那样"上限是 3"就有了两份真相：改表里的 `maxStack` 面板会跟着变，
        ///   而这里的判据还是 3 ⇒ 玩家看到"上限 2"却能持有 3 ✗
        ///   取表 ⇒ 改表即改行为 ✓ 表里两行的 `maxStack` 都是 3（羽毛那行只是为了让
        ///   面板印得出"持有上限 3"，判定一律走本属性）✓
        /// </summary>
        public static int RevivalFamilyCap
        {
            get
            {
                ItemDef angel = TableDatabase.GetItem(ItemIds.AngelDescent);
                return angel != null && angel.MaxStack > 0 ? angel.MaxStack : 3;
            }
        }

        /// <summary>这种种子是否已经到"拥有上限"（包里已有同种 3 级）。</summary>
        public static bool SeedAtCap(RunSession s, SeedDef def)
        {
            if (s == null || s.Bag == null || def == null) return false;
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot sl = s.Bag[i];
                if (sl == null || sl.IsEmpty || sl.Def == null) continue;
                if (sl.Def.Id != def.Id) continue;
                if (sl.Level >= SeedSlot.MaxLevel) return true;
            }
            return false;
        }

        /// <summary>一个**货位**是否已经到上限（三类分派；已售出的空位不算）。</summary>
        public static bool OfferAtCap(RunSession s, ShopOffer o)
        {
            if (s == null || o == null) return false;
            switch (o.Kind)
            {
                case OfferKind.Plugin: return o.Plugin != null && PluginAtCap(s, o.Plugin);
                case OfferKind.Item: return o.Item != null && ItemAtCap(s, o.Item);
                case OfferKind.Seed: return o.Seed != null && SeedAtCap(s, o.Seed);
                default: return false;
            }
        }

        private ShopOffer RollOne(RunSession s)
        {
            ShopDef cfg = s.T.shop;
            // 三类商品：种子 / 插件 / 道具（v6 的"工具升级"这一类已由插件取代）
            int seedW = Mathf.Max(0, cfg.seedWeight);
            int pluginW = Mathf.Max(0, cfg.pluginWeight);
            int itemW = Mathf.Max(0, cfg.itemWeight);
            int total = seedW + pluginW + itemW;
            // 权重全为 0 ⇒ 退到种子 ✗ 否则会抽出一个永远空着的货位（旧版 `toolWeight: 0` 就是这样）
            if (total <= 0) return RollSeed(s);
            int roll = s.World.Rng.RangeInt(0, total);
            if (roll < seedW) return RollSeed(s);
            if (roll < seedW + pluginW) return RollPlugin(s);
            return RollItem(s);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v185：**种子货架的合成等级（1/2/3 级）与它的定价** ★
        //
        // 用户原话：「商店种子刷新额外增加 2 级种子和 3 级种子，价格略低于实际合成
        //           所需的金币值，高于 1 级」
        //
        // ★ 合成规则（唯一实现是 `SeedBag.CanMerge` / `Merge`，本段只是它的读者）★
        //   两个**同种同级**的种子 ⇒ 目标槽 `Level + 1`、材料槽被消耗 ⇒ **2 合 1** ⇒
        //     1 个 2 级 = 2 个 1 级，1 个 3 级 = 2 个 2 级 = **4 个 1 级** ✓
        //   ⚠️ 不是 3 合 1 —— 所以 `SeedMergeArity` 必须是 **2**；
        //      写成 3 会让"合成成本"凭空多 50% ⇒ 折扣表全错 ✓
        //
        // ★ 定价式子（用户给的形状，逐级打折的**链式**写法）★
        //   2 级价 = 合成成本(2级) × k2 = (2 × 1级价) × k2
        //   3 级价 = 合成成本(3级) × k3 = (2级价 × 2) × k3
        //   "合成成本" = 凑出这一级要花多少金币买 1 级材料 ✓（合并不收费）
        //
        // ★ k2 = k3 = 0.90 的理由 ★
        //   ① 落在用户给的 0.85~0.95 区间内、且靠**保守**一端 ⇒ 每步只便宜 10%，
        //      累积到 3 级 = 4 × 1级价 × 0.81 ⇒ 比"自己合"便宜 19% ✓
        //   ② 为什么不吃到 0.85：那样 3 级只花 0.7225 倍成本（便宜 28%），
        //      就不再是"略低"—— 商店会把**合成**这条核心循环挤掉（买永远比合划算）✗
        //   ③ 为什么不取 0.95：D 档 1 级价只有 20、2 级成本 40 ⇒ 只省 2 金币，
        //      玩家看不出差别，这条规则等于没写 ✓
        //   ④ 两个 k 相等，是为了让"每跨一级打九折"成为一句能背下来的话 ✓
        //
        // ★ 为什么 2/3 级种子**稀有**（权重 14 / 4）★
        //   定价上"买"比"合"便宜是用户的要求 ✓ 那"买"就必须**不常出现**，
        //   否则 2 合 1 这条链会被货架掏空。稀有度是另一半的平衡手段：
        //   一次刷新里种子货位约 2.2 个 ⇒ 见到任一 2 级以上种子的概率约 35%
        //   ⇒ 合成仍是主线，商店那几格是彩票 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>合成所需**同级种子数** —— 与 `SeedBag.CanMerge/Merge` 同源（2 合 1）✓</summary>
        public const int SeedMergeArity = 2;
        /// <summary>2 级种子价相对其**合成成本**的折扣 k2（&lt;1 = 略低于合成成本）。理由见上。</summary>
        public const float SeedLv2Discount = 0.90f;
        /// <summary>3 级种子价相对其**合成成本**的折扣 k3（&lt;1 = 略低于合成成本）。理由见上。</summary>
        public const float SeedLv3Discount = 0.90f;

        /// <summary>
        /// 种子货架的**等级权重**：下标 = 等级（0 位不用），合计 100。
        /// 1 级 82 / 2 级 14 / 3 级 4 —— 越高的级越稀有，理由见上面的整段说明 ✓
        /// </summary>
        public static readonly int[] SeedLevelWeights = { 0, 82, 14, 4 };

        /// <summary>某一级的折扣系数 k（1 级 = 1.00）。</summary>
        public static float SeedLevelDiscount(int level)
            => level >= 3 ? SeedLv3Discount : level == 2 ? SeedLv2Discount : 1f;

        /// <summary>
        /// **实际合成成本** = 凑出 1 个 <paramref name="level"/> 级种子要花多少金币买 1 级材料。
        ///   = 1级价 × arity^(level−1)（2 合 1 链：2 级 = 2× ／ 3 级 = **4×**）✓
        /// 合并不收费 ⇒ 材料价就是全部成本 ✓ 它是折扣这条规则的分母 ✓
        /// </summary>
        public static int SeedMergeCost(int lv1Price, int level)
        {
            int n = Mathf.Clamp(level, 1, SeedSlot.MaxLevel) - 1;
            long v = Mathf.Max(1, lv1Price);
            for (int i = 0; i < n; i++) v *= SeedMergeArity;
            return v > int.MaxValue ? int.MaxValue : (int)v;
        }

        /// <summary>
        /// **商店价**：1 级价 → 逐级打折的链式定价（与用户给的式子同形）。
        ///   2 级 = round(1级价 × 2 × k2)　3 级 = round(2级价 × 2 × k3)
        /// ★ 两条硬约束都在这个循环里保证 ★
        ///   · `Max(p + 1, …)` ⇒ **严格高于上一级**（四舍五入也不会把 2 级压回 1 级价）✓
        ///   · 折扣 &lt; 1 且 arity = 2 ⇒ **严格低于合成成本**（成本恰是上一级的 2 倍）✓
        /// 1 级**原样返回** ⇒ 既有 1 级种子的价格逐位不变 ✓
        /// </summary>
        public static int SeedLevelPrice(int lv1Price, int level)
        {
            int p = Mathf.Max(1, lv1Price);
            for (int lv = 2; lv <= Mathf.Clamp(level, 1, SeedSlot.MaxLevel); lv++)
                p = Mathf.Max(p + 1, Mathf.RoundToInt(p * SeedMergeArity * SeedLevelDiscount(lv)));
            return p;
        }

        /// <summary>
        /// 掷这一格种子商品的**等级**（1/2/3）。
        ///
        /// ★ 为什么不用 `s.World.Rng`（本段最要紧的一处设计）★
        /// 用世界随机流掷就等于**多消耗一个随机数** ⇒ 之后所有种子/道具/刷怪的抽样
        /// 全部移位 ⇒ SelfCheck 里那些"固定种子 + 固定期望"的断言会成批漂移 ✗
        /// 而本任务不许改 SelfCheck ⇒ 这里改用**派生随机**：
        /// 由 (世界种子, 分钟, 重抽次数, 已抽货位数) 混出一个整数。
        /// 它依然**完全确定**（同样状态 ⇒ 同样等级，回放可复现 ✓），
        /// 且**一个随机数都不占** ⇒ 既有序列逐位不变 ✓
        ///
        /// ⚠️ 它是实例方法（要读 `RerollCount` 与 `Offers.Count`）✗ 别改成 static ✓
        /// </summary>
        public int RollSeedLevel(RunSession s)
        {
            int total = 0;
            for (int i = 0; i < SeedLevelWeights.Length; i++) total += Mathf.Max(0, SeedLevelWeights[i]);
            if (total <= 0) return SeedSlot.StartingLevel;
            ulong h = s != null ? s.Seed : 0UL;
            int salt = (s != null ? Mathf.Max(1, s.WaveIndex) : 1) * 131 + RerollCount * 17 + Offers.Count * 7;
            h ^= (ulong)(uint)salt * 0x9E3779B97F4A7C15UL;
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL;
            h ^= h >> 33;
            int roll = (int)(h % (ulong)total);
            int acc = 0;
            for (int lv = 1; lv < SeedLevelWeights.Length; lv++)
            {
                acc += Mathf.Max(0, SeedLevelWeights[lv]);
                if (roll < acc) return lv;
            }
            return SeedSlot.MaxLevel;
        }

        /// <summary>
        /// 种子货位（★ v189：**上限过滤**在外层做一次，掷法本身不变）✓
        /// 满了（包里已有同种 3 级）就重掷；它是最后一类兜底商品，连掷都是满的时宁可给一件也不留空货位。
        /// </summary>
        private ShopOffer RollSeed(RunSession s)
        {
            ShopOffer offer = null;
            for (int attempt = 0; attempt < CapRetries; attempt++)
            {
                offer = RollSeedOnce(s);
                if (offer == null || offer.Seed == null) break;
                if (!SeedAtCap(s, offer.Seed)) break;
            }
            // ★ v252：天气种子保底（见 `ApplyWeatherSeedPity` 的整段说明）★
            return ApplyWeatherSeedPity(s, offer);
        }

        /// <summary>
        /// ★ v214：**植物（种子）抽样** —— `TableDatabase.RandomSeed` 的**池化镜像**。
        /// 掷法与它逐句相同（"数出该档位几件 → **一次** `RangeInt` → 顺序走"），
        /// 唯一差别是把 `ByGrade[grade]` 换成**过完解锁门的候选池**里该档位的那几件 ✓
        ///
        /// 为什么不直接改 `TableDatabase.RandomSeed`：本轮可写范围只有本文件（`Data/**` 只读）✓
        /// 为什么它不会让抽样顺序漂：候选池没被过滤时**直接调原函数**（下面那三处 `return`）⇒
        /// 随机数消耗与返回结果**逐位等于改前** ✓
        /// </summary>
        private static SeedDef PickSeedOfGrade(ref Pcg32 rng, SeedGrade grade)
        {
            if (!UnlockGateActive) return TableDatabase.RandomSeed(ref rng, grade);
            SeedDef[] pool = SeedCandidates();
            // 没被过滤（原引用）/ 空池 ⇒ 原函数，逐位等于改前
            if (pool.Length == 0 || ReferenceEquals(pool, TableDatabase.Seeds))
                return TableDatabase.RandomSeed(ref rng, grade);
            int count = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i].Grade == grade) count++;
            // 该档位全被锁 ⇒ 退回原口径（宁可多给一件，也不让商店整类没货）✓
            if (count == 0) return TableDatabase.RandomSeed(ref rng, grade);
            int pick = rng.RangeInt(0, count);
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].Grade != grade) continue;
                if (pick-- == 0) return pool[i];
            }
            return pool[0];     // 不会走到（count > 0 ⇒ 上面必返回）
        }

        private ShopOffer RollSeedOnce(RunSession s)
        {
            ShopDef cfg = s.T.shop;
            // v35：变异词条系统已彻底删除（原来这里有一段死代码会读 null 词条 → 空引用崩溃）

            // ★★ v277：种子档位**不再吃静态表** —— 改走 `RollGoodsTier`（与道具/插件同一个算式）★★
            //
            // ── 改前（v252~v276）的那段在这里，逐字留档便于回滚 ──
            //   float weightTotal = 0f;
            //   for (...) weightTotal += Mathf.Max(0f, cfg.gradeWeights[i].weight);
            //   float roll = s.World.Rng.Range(0f, Mathf.Max(0.0001f, weightTotal));
            //   for (...) { acc += ...; if (roll < acc) { grade = ...; break; } }
            //   ⇒ 种子表是**静态**的：无论权重填什么数，S 档在 1 / 5 / 30 分钟都是**同一个百分比**
            //     （v276 因此只能给种子表取一个常数 S = 1%，并在注释里写明"要让种子也随时间
            //       爬升必须改代码"）✓
            //
            // ── 现在（v277 起）──
            //   `cfg.itemGradeWeights` 只再承担**一件事**：D:C 之间的内部比例。
            //   S / A / B 三档的概率由 `shop.rarityCurve` 的时间曲线给出 ⇒
            //   道具 / 插件 / 种子三类商品**同一条膨胀曲线**，不再有"种子静态、道具动态"的分叉 ✓
            //
            // ★★ v282：**品阶表合并成一张**（用户原话：「只按照品质产出」）★★
            //   改前这里读的是种子自己的 `cfg.gradeWeights`（D 73.2 / C 18.3 / B 5 / A 2.5 / S 1），
            //   道具与插件读 `cfg.itemGradeWeights` ⇒ 同一个"品质"在三类之间是**两套概率** ✗
            //   现在 `shop.gradeWeights` 已从表里删除、C# 字段也一起退役 ⇒
            //   三类**共用 `itemGradeWeights` 这一张唯一来源** ✓
            //   行为读数：旧表 D:C = 73.2:18.3 = 80.00:20.00，新表 68.77:17.19 ≈ 80.006:19.994，
            //   而 S/A/B 本来就被 `rarityCurve` 覆盖 ⇒ 种子档位的有效概率**逐位不变** ✓
            //
            //   ⚠️ 随机数消耗**不变**：`RollGoodsTier` 也只掷**一次** `Range(0f, 合计)`，
            //      与改前那句 `Range(0f, weightTotal)` 一一对应 ⇒ 抽样序列的长度不变 ✓
            int wave = Mathf.Max(1, s.WaveIndex);
            int picked = RollGoodsTier(s, cfg.itemGradeWeights, wave);
            SeedGrade grade = cfg.itemGradeWeights != null && picked >= 0 && picked < cfg.itemGradeWeights.Length
                ? DefParser.ParseEnum(cfg.itemGradeWeights[picked].grade, SeedGrade.D)
                : SeedGrade.D;
            SeedDef def = PickSeedOfGrade(ref s.World.Rng, grade);
            int quality = def.RarityTier;   // v44: seed rarity is fixed per seed
            // ★ v185：**等级轴**（1/2/3 级种子）—— 与品质正交，用户要的"额外 2 级 / 3 级种子"✓
            //   掷法不占世界随机数（见 `RollSeedLevel` 的说明）⇒ 既有序列逐位不变 ✓
            int level = RollSeedLevel(s);
            // v118：1 级价走**统一定价入口**（必须带波次，否则种子不随回合涨价）✓
            //   v185：2/3 级在它之上逐级打折（`SeedLevelPrice` 的 1 级 = 原样返回）✓
            int lv1Price = SeedSlot.ShopPriceFrom(def.Price, quality, s.WaveIndex);
            ShopOffer offer = new ShopOffer
            {
                Kind = OfferKind.Seed,
                Seed = def,
                Quality = quality,
                Level = level,
                Title = def.Name,
                // 副标题把"几级种子"写在最显眼处 —— 商店卡的第二行读的就是它 ✓
                Subtitle = level > SeedSlot.StartingLevel
                    ? $"{level} 级种子 · {Rarity.NameOf(quality)} · 合成成本 {SeedMergeCost(lv1Price, level)}"
                    : $"{Rarity.NameOf(quality)} · {RunSession.AtkTypeText(def)}"
            };
            // v186：价格**不在这里算** —— 走 `PriceOf`（三类商品同一个出口，锁定项复用它）✓
            offer.Price = PriceOf(offer, s.WaveIndex);
            return offer;
        }

        /// <summary>
        /// 插件商品：先按"商品等级权重"掷一个档位，再在该档位的插件里随机一件。
        ///
        /// ★ 插件从哪来 ★
        /// `Assets/Resources/Data/plugins.json` → `TableDatabase.Plugins`（见 <see cref="PluginPool"/>）✓
        /// **不在这里硬编码一份插件清单** ✗ —— 那样"策划加一件插件"就要改代码，
        /// 而忘了改的时候不报错，只是商店里永远刷不出来 ✓
        ///
        /// ★ 为什么档位复用 `itemGradeWeights` ★
        /// 种子有自己的 `gradeWeights`（主力商品，分布更平）✓
        /// 插件与道具同属"配角商品" ⇒ 用同一条曲线，才不会出现
        /// "同一次刷新里插件全是史诗、道具全是普通"这种双标 ✓ 而且只调一处 ✓
        /// </summary>
        private ShopOffer RollPlugin(RunSession s)
        {
            // ★ v214：候选池 = **过完主城解锁门**的那一份（抽之前就过滤，见上面整段说明）★
            PluginAsset[] pool = PluginCandidates();
            if (pool.Length == 0) return RollSeed(s);   // 一件插件都没有 ⇒ 退到种子 ✗ 不留空货位 ✓
            // ★ v189：**全部插件都满上限** ⇒ 这一类不再出货（不必白掷 6 次随机数）✓
            if (AllPluginsAtCap(s, pool)) return RollSeed(s);

            ShopDef cfg = s.T.shop;
            int wave = Mathf.Max(1, s.WaveIndex);
            // ★ v189：抽到"已满上限"的那一件就**重抽**（最多 `CapRetries` 次）★
            //   第一次迭代与改前**逐位相同**（同样两个随机数、同样的选取）⇒
            //   没到上限的局里随机序列一个数都不变 ✓ 只有真到上限时才多掷 ✓
            for (int attempt = 0; attempt < CapRetries; attempt++)
            {
                int tier = RollGoodsTier(s, cfg.itemGradeWeights, wave);
                PluginAsset picked = PickPluginOfTier(s, pool, tier);
                if (picked == null) return RollSeed(s);
                if (PluginAtCap(s, picked)) continue;      // ★ ③ 满上限 ⇒ 这个货位不该出现它

                int quality = TableDatabase.Tuning.quality.Clamp((int)picked.quality);
                ShopOffer offer = new ShopOffer
                {
                    Kind = OfferKind.Plugin,
                    Plugin = picked,
                    Quality = quality,
                    Title = picked.displayName,
                    Subtitle = $"{Rarity.NameOf(quality)} · 插件（装到哪把武器由你决定）"
                };
                // v186：价格走 `PriceOf` —— 与种子/道具、以及"锁定项重新定价"同一个出口 ✓
                offer.Price = PriceOf(offer, s.WaveIndex);
                return offer;
            }
            return RollSeed(s);     // 连掷 6 次都撞上限 ⇒ 退到种子（比摆一件买不了的商品好）✓
        }

        /// <summary>上限过滤的重试次数（掷到满上限的商品就再掷一次，最多这么多次）。</summary>
        private const int CapRetries = 6;

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v252：**天气类植物（晴天 / 雨天）的种子保底 ★★
        //
        // 用户原话：「设计的晴天、雨天效果种子异常不刷新，调整正常刷新」。
        //
        // ── 病因（可复算，不是猜）──
        //   `sunflower_crown`（SunnyBloom）/ `rainlily`（RainCloud）都是 **S 档**，
        //   而 `tuning.json` 的 `shop.gradeWeights` 里 S 的权重是 **0.5 / 100**
        //   ⇒ 一个种子货位抽到"某一株 S"的概率 ≈ 0.5% × (1/S 档株数) ≈ **0.05%**；
        //   一次刷新约 2.2 个种子货位 ⇒ **平均 900 次刷新才见到一次** ✓
        //   玩家看到的现象就是"这两株我设计了，但商店永远不刷新它" ✓
        //   （这与 v237 记的"机制种子完全不刷新"是同一个病的两个来源：那一次是 `onSale:false`
        //     把 4 株锁在池外，这一次是 S 档权重太低）
        //
        // ── 修法：**确定性保底**（不掷骰）──
        //   计数"连续几个种子货位不是天气种子"，到 3 就强制换成天气种子（晴天/雨天交替）。
        //   ⇒ 包里没有天气植物时，**最多 4 个种子货位之内必定出现一株** ✓
        //
        // ── 为什么不加随机数、不加权重 ──
        //   ① 改权重会连带把**所有** S 档植物一起抬高（用户没要求），而"保底"只针对这两株 ✓
        //   ② **多掷一次随机数**会让整条抽样序列整体漂移 ⇒ 钉种子的自检/回放/基准全变红，
        //      而红的原因不是"天气种子错了"，是"我悄悄改了随机序列" ✗
        //      本实现**一个随机数都不碰**（用两个自增计数决定该给哪一株）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>距离上一次"天气种子货位"过了几个种子货位（>= <see cref="WeatherSeedPitySlots"/> 就强制给一株）。</summary>
        private int _seedSlotsSinceWeather;
        /// <summary>天气种子保底的交替计数（偶数 ⇒ 晴天株，奇数 ⇒ 雨天株）。</summary>
        private int _weatherPickCount;

        /// <summary>
        /// 保底间隔：连续这么多个种子货位都不是天气种子 ⇒ 下一个种子货位强制给天气种子。
        ///
        /// ★★ v279：**3 → 7**（用户原话：「日光葵/雨语莲的刷新概率过高，明显不符合传奇品质」）★★
        /// 读法：天气种子出现的**上限频率 = 1/(N+1) 个种子货位 ≈ 12.5%**（N=7），改前 N=3 ⇒ **25%**。
        /// 改前那 25% 是硬塞的 ⇒ 与 `rarityCurve` 的 S 档（第 1 分钟 0.6%、30 分钟封顶 10%，
        /// 且 S 档 10 株平分 ⇒ 单株 0.06%~1%）**完全无关**，所以用户看到的是"传奇比普通还常见"✓
        /// 保底**不取消**（用户说的是"概率过高"，不是"不要"）—— 它仍是"永远刷不到"那条病的解 ✓
        /// 想更稀：这个数是"连续 N 个非天气种子货位"⇒ N 越大越稀，改 `rarityCurve` 对它无效 ✗
        /// </summary>
        public const int WeatherSeedPitySlots = 20;   // ★ v281：7 → 20（用户选"更稀"档）★
        //   改前 3：每 100 个种子格出 33 株天气种子（日/雨各 16.6），是第 1 分钟自然单株率(0.12/100)的 278 倍。
        //   改到 7：14.3 株/100 格 —— 用户仍觉得"不符合传奇品质"。
        //   现在 20：约 **10.6 株/100 格**（日 5.3 + 雨 5.3），自然率 0.12~1.95/100 ⇒ 保底仍占大头，但存在感明显下降。
        //   回滚：把 20 改回 3 即可（保底机制本身一个字节没动）。

        /// <summary>这一件是不是"天气类"植物（机制 4 / 5）。</summary>
        public static bool IsWeatherSeed(SeedDef def)
        {
            return def != null
                && (PlantMechanics.Has(def, PlantMechanics.SunnyBloom)
                    || PlantMechanics.Has(def, PlantMechanics.RainCloud));
        }

        /// <summary>包里**已经拥有**这种植物（任意等级）。</summary>
        public static bool OwnsSeed(RunSession s, SeedDef def)
        {
            if (s == null || s.Bag == null || def == null) return false;
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot sl = s.Bag[i];
                if (sl == null || sl.IsEmpty || sl.Def == null) continue;
                if (sl.Def.Id == def.Id) return true;
            }
            return false;
        }

        /// <summary>包里有没有任何一株天气植物。</summary>
        public static bool OwnsAnyWeatherSeed(RunSession s)
        {
            SeedDef[] all = SeedCandidates();
            for (int i = 0; i < all.Length; i++)
                if (IsWeatherSeed(all[i]) && OwnsSeed(s, all[i])) return true;
            return false;
        }

        /// <summary>
        /// 保底该给哪一株天气种子（晴天/雨天交替；交替指针**只在真的发放时**前进）。
        /// 返回 null = 池子里两株都没有（理论上不会 —— 它们是老批次，默认解锁）。
        /// </summary>
        private SeedDef PickWeatherSeed()
        {
            SeedDef sunny = null, rain = null;
            SeedDef[] all = TableDatabase.Seeds;
            for (int i = 0; i < all.Length; i++)
            {
                SeedDef d = all[i];
                if (d == null) continue;
                if (sunny == null && PlantMechanics.Has(d, PlantMechanics.SunnyBloom)) sunny = d;
                else if (rain == null && PlantMechanics.Has(d, PlantMechanics.RainCloud)) rain = d;
            }
            if (sunny == null && rain == null) return null;
            if (rain == null) return sunny;
            if (sunny == null) return rain;
            return (_weatherPickCount & 1) == 0 ? sunny : rain;
        }

        /// <summary>
        /// 把已经掷出来的种子货位过一遍"天气保底"。返回（可能被替换的）货位。
        /// 计数规则：天气种子 ⇒ 清零；否则 +1，到了阈值就替换并把交替指针 +1。
        /// </summary>
        private ShopOffer ApplyWeatherSeedPity(RunSession s, ShopOffer offer)
        {
            if (offer == null || offer.Seed == null) return offer;      // 空货位：不参与计数
            if (IsWeatherSeed(offer.Seed))
            {
                _seedSlotsSinceWeather = 0;
                return offer;
            }
            _seedSlotsSinceWeather++;
            if (_seedSlotsSinceWeather < WeatherSeedPitySlots) return offer;
            // ★ v279：(b) **不许"抢"自然 S 档** ★
            //   这一格已经按 `rarityCurve` 掷出 S ⇒ 概率预算**已经花掉了**；
            //   把它换成天气种子 = 把一次 S 白扔 + 天气种子当白送的第二次 S ⇒ 这一格出两株 S 的价 ✗
            //   所以：S 档不替换（计数照旧累积，下一个非 S 货位再补）✓
            if (offer.Seed.Grade == SeedGrade.S) return offer;
            if (OwnsAnyWeatherSeed(s)) return offer;                    // 已经有了就不再硬塞
            SeedDef pick = PickWeatherSeed();
            if (pick == null) return offer;

            _weatherPickCount++;
            _seedSlotsSinceWeather = 0;
            int quality = pick.RarityTier;
            var sub = new ShopOffer
            {
                Kind = OfferKind.Seed,
                Seed = pick,
                Quality = quality,
                Level = SeedSlot.StartingLevel,
                Title = pick.Name,
                Subtitle = $"{Rarity.NameOf(quality)} · {RunSession.AtkTypeText(pick)} · 天气种子保底",
            };
            sub.Price = PriceOf(sub, s.WaveIndex);
            Log.Info($"[商店 v252] 天气种子保底：连续 {WeatherSeedPitySlots} 个种子货位没出天气植物 ⇒ 本格给「{pick.Name}」");
            return sub;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v214：**刷新候选池 = 主城商店解锁表过滤之后的那一份** ★★
        //
        // 用户原话：「商店卖的是**道具、插件、植物**，**买了之后才会在战斗刷新这些**」
        // ⇒ 三类的候选池在**抽样之前**先过一遍 `TownUnlocks.IsUnlocked(id)`：
        //     种子（= 植物，`RollSeedOnce`）／插件（`RollPlugin`）／道具（`RollItemOnce`）
        //
        // ★ 为什么过滤必须发生在**抽样之前**（而不是"抽到之后再判没解锁就重掷"）★
        //   重掷会**多吃随机数** ⇒ 同一种子下的整条抽样序列全部漂移 ⇒
        //   `SelfCheck` 里那些"固定种子 + 固定期望"的断言成批变红 ✗
        //   先过滤后掷 ⇒ 每一次抽样**仍然只掷一个 `RangeInt`** ⇒ 掷法与改前逐句相同 ✓
        //
        // ★★ `fail-open` 的处置：**表未就绪 ⇒ 不过滤（保持改前行为）** ★★
        //   `TownUnlocks` 是运行期静态：域重载之后、`Load()` 之前，它对一切 id 返回 `true`
        //   （主城那路已通报的已知缺陷）。若照它过滤，就等于"表没载入 ⇒ 全解锁" ✗
        //   两种合规处置里选了 **② 直接不过滤**（而不是 ① 按 id 白名单退回只用老货）：
        //     ① 白名单要在战斗侧**再抄一份"哪些算老货"** —— 而 `TownUnlocks` 的整条设计
        //       恰恰是"表里只登记新的那批、查不到的一律默认解锁"；
        //       抄一份 = 造第二个真值源，两处必然分叉 ✗
        //     ② 不过滤 ⇒ 逐位等于改前 ⇒ 自检 / 回放 / 确定性全部不受影响（见 `UnlockGateActive`）
        //   代价（写在这里，不藏）：**表没载入时，新货照样能刷出来**。
        //   正常进场不会发生 —— 主城 `TownBootstrap.Awake` 先 `Load()`，再进战斗 ✓
        //   而**无头自检**（`SelfCheckRunner`）从不进主城 ⇒ 永远走"不过滤"这条 ⇒
        //   那条"每件插件都能被抽到"的断言读数逐位不变 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>解锁门是否生效 = 主城解锁表已载入。见上面整段（`fail-open` 的处置）。</summary>
        private static bool UnlockGateActive => TownUnlocks.Ready;

        /// <summary>"门没生效"只提示一次 —— 每次抽样都打日志会把控制台埋掉 ✗</summary>
        private static bool _unlockGateWarned;

        private static void NoteGateInactive()
        {
            if (_unlockGateWarned) return;
            _unlockGateWarned = true;
            Log.Info("[商店 v214] 主城解锁表未载入 ⇒ 道具/插件/植物候选池**不过滤**（保持改前行为）。" +
                     "正常进场（主城 Awake 载入后进战斗）不会走到这里。");
        }

        /// <summary>
        /// 过滤掉"表里登记了、但这次运行里还没买"的 id —— **唯一实现**（三类共用）✓
        ///
        /// ★ 三种"不过滤"的返回（都返回**原数组引用**：零分配、逐位等于改前）★
        ///   ① 门没生效（表未载入）
        ///   ② 一件都没被锁 —— 老档位的常态（`TownUnlocks` 只登记新那批，表外的默认解锁）
        ///   ③ 整类全被锁 —— 宁可多给，也不把一整类商品从货架上删空
        ///      （空池会让商店整类退化成"退到种子"，那是比"多出一件"更坏的失败 ✗）
        /// </summary>
        private static T[] UnlockedOnly<T>(T[] all, Func<T, string> idOf) where T : class
        {
            if (!UnlockGateActive) { NoteGateInactive(); return all; }
            if (all == null || all.Length == 0) return all;
            int locked = 0, kept = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (IsInPool(idOf(all[i]))) kept++;
                else locked++;
            }
            if (locked == 0 || kept == 0) return all;
            var pool = new T[kept];
            int n = 0;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && IsInPool(idOf(all[i]))) pool[n++] = all[i];
            return pool;
        }

        /// <summary>
        /// ★★ v252：**这一件算不算"该出现在战斗商店车里"** —— 唯一判据 ★★
        ///
        ///   `IsUnlocked(id)`      买过 / 表里没登记（老货）        ⇒ 出现
        ///   `TownUnlocks.OnSale(id)` 主城**正在上架卖**            ⇒ 出现（本轮新增）
        ///
        /// ── 为什么"上架"也要算进池 ──
        ///   用户原话：「将主城商店中开放购买的内容在删除重复后**全部加入实际战斗的商店车中**」。
        ///   ⇒ "主城能买到的" = "战斗商店车里也该有" ⇒ 上架行不再等"买过"才出现在战斗货架上 ✓
        ///
        /// ── 代价（写在这里，不藏）──
        ///   上架行既然直接进池，主城那一下"购买"对上架行的作用就只剩 **永久记账 + 提前解锁**
        ///   （未上架行仍然锁着，等它上架；老货照旧默认解锁）⇒ 这是本条需求的直接推论，
        ///   不是顺手改语义。要退回 v214 的行为只需把这一行改回 `IsUnlocked(id)` ✓
        /// </summary>
        private static bool IsInPool(string id)
        {
            // ★ v284：**只看 IsUnlocked**（= 买过，或表里没登记的老货）★
            //   用户原话：「只有购买后才在战斗刷新」。
            //   语义（改后）：
            //     · 老货（town_unlocks.json 里查不到）⇒ `IsUnlocked` 恒 true ⇒ 仍在战斗池
            //       （它们本来就在池里，本轮把商店那一行下架不影响它们）
            //     · 新货（表里有、还没买）  ⇒ `IsUnlocked` = false ⇒ **不进战斗池**
            //     · 新货买过                ⇒ `Purchased` 命中 ⇒ 进池 ✓
            //   ⚠️ 与 v214 的差别：`OnSale(id)` 这一项**被删掉**了 —— 那一项会让
            //   "在商店上架"等于"已经在战斗池里"，与"只有购买后才刷新"直接矛盾。
            //
            // ★ 训练场仓库的**唯一**战斗侧守卫 ★：`TrainingShop.SellEverything` 默认 false
            //   ⇒ 本行在正常战斗里**恒不成立**，返回值与改前逐位相同；只有训练场把开关打开时
            //   （`TrainingMode.Enable` 会顺带调 `TrainingShop.Enable`，或玩家按 B）才让
            //   seeds / items / plugins 三类全进池 ✓
            //
            //   ⚠️ v301 去重：本轮曾有**两份**语义相同的守卫（`TrainingMode.ShopSellsEverything`
            //   与这里的 `TrainingShop.SellEverything`）。同一个语义留两份是维护隐患
            //   （以后有人只改一份就会出现"某一类没解锁"），已合并成**这一处**；
            //   `TrainingMode.Enable()` 里有一行 `TrainingShop.SellEverything = true;`
            //   负责把它一起置位（⚠️ v302 更正：是**直接写字段**，不是调 `TrainingShop.Enable()`，
            //   因为后者还会装驱动器、在训练场外调用没有意义）。
            if (TrainingShop.SellEverything) return true;
            return TownUnlocks.IsUnlocked(id);
        }

        /// <summary>★ 只读：**插件候选池**（已过解锁门）。供自检/探针查"这一件现在能不能刷出来"。</summary>
        public static PluginAsset[] PluginCandidates() => UnlockedOnly(PluginPool(), p => p.id);

        /// <summary>★ 只读：**道具候选池**（已过解锁门）。⚠️ 别改返回的数组（它可能就是要出货的那一份）。
        ///
        /// ★★ v314：底座从 `TableDatabase.Items` 换成 `TableDatabase.RollableItems` ★★
        ///   —— 差别只有一条：`noDrop == true` 的道具（本轮是「陨落天使的羽毛」）**不在底座里**
        ///   ⇒ 无论"解锁门"生不生效（正常进战斗 / 无头自检），它都不可能出现在候选池里 ✓
        ///   "它能不能掉"这件事仍然只有一处真相（`TableDatabase.RollableItems`），
        ///   本函数只负责"再过一道解锁门" ✓
        /// </summary>
        public static ItemDef[] ItemCandidates() => UnlockedOnly(TableDatabase.RollableItems, it => it.Id);

        /// <summary>★ 只读：**植物（种子）候选池**（已过解锁门）。</summary>
        public static SeedDef[] SeedCandidates() => UnlockedOnly(TableDatabase.Seeds, sd => sd.Id);

        /// <summary>插件池里**是不是每一件都满上限了**（是 ⇒ 这一整类不再出货）。</summary>
        private static bool AllPluginsAtCap(RunSession s, PluginAsset[] pool)
        {
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && !PluginAtCap(s, pool[i])) return false;
            return true;
        }

        /// <summary>
        /// 插件池 = `TableDatabase.Plugins`（源码是 `Data/plugins.json`）。
        ///
        /// ★ 为什么这里没有缓存 ★
        /// 缓存一份"读出来的东西"必须处理"读到空"的情形 ✓
        /// 而缓存空 ⇒ 把"还没有"固化成"永远没有"，且**完全静默** ✗
        /// 表已经在 `TableDatabase` 里加载过一次并常驻 ⇒ 这里再缓存一次只是给池子
        /// 多加一个"什么时候刷新"的问题 ✓ 所以直接转发，不缓存 ✓
        /// </summary>
        private static PluginAsset[] PluginPool() => TableDatabase.Plugins;

        /// <summary>
        /// 供自检 / 调试查询：**当前能刷出几件插件**（只读，不触发抽取）。
        /// ★ v214：从"表里共几件"改成"**过完解锁门还剩几件**" —— 字面意思就是它 ✓
        ///   表未载入时（无头自检）两者相等 ⇒ 既有断言读数不变 ✓
        /// </summary>
        public static int PluginPoolSize() => PluginCandidates().Length;

        /// <summary>
        /// ★ v237：**植物（种子）刷新池的一行式读数**（只读，不掷任何随机数）★
        ///
        /// 用户原话：「我设计的几个机制种子在目前的商店完全不刷新」。
        /// 这个问题要回答三件事，本方法一次说全：
        ///   ① 数据文件里到底几条（`seeds.json` → `TableDatabase.Seeds`）；
        ///   ② 池里能刷出几条（`SeedCandidates()` = 过完 `TownUnlocks.IsUnlocked` 之后的那一份）；
        ///   ③ **差额是哪几条、被哪个条件滤掉**（逐条点名 + 原因）。
        ///
        /// 也正因为它是"读数"而不是"猜测"：`TownUnlocks` 表没载入时（无头自检）它会明说
        /// "解锁门未生效"，而不是把 0 条差额当成"没问题" ✓
        /// </summary>
        public static string SeedPoolReport()
        {
            SeedDef[] all = TableDatabase.Seeds;
            SeedDef[] pool = SeedCandidates();
            var sb = new System.Text.StringBuilder(512);
            sb.Append("植物刷新池：数据文件（seeds.json）").Append(all.Length).Append(" 条")
              .Append(" → 池里可刷出 ").Append(pool.Length).Append(" 条")
              .Append("｜差额 ").Append(all.Length - pool.Length).Append(" 条")
              .Append("｜解锁门 = ").Append(UnlockGateActive ? "生效（TownUnlocks 已载入）" : "**未生效**（表没载入 ⇒ 不过滤）");

            if (all.Length > pool.Length)
            {
                sb.Append("\n  被 `TownUnlocks.IsUnlocked(id)` 滤掉的：");
                for (int i = 0; i < all.Length; i++)
                {
                    SeedDef d = all[i];
                    if (d == null || TownUnlocks.IsUnlocked(d.Id)) continue;
                    int idx = TownUnlocks.IndexOf(d.Id);
                    TownUnlockDef u = TownUnlocks.At(idx);
                    sb.Append("\n    · ").Append(d.Id).Append('（').Append(d.Name).Append("｜").Append(d.Grade)
                      .Append('｜').Append(d.Mechanic).Append("）← 表里登记，尚未买下");
                    if (u != null) sb.Append(u.onSale ? "（在售，价格 " + u.price + " 金）"
                                                     : "（**onSale:false ⇒ 买不到 ⇒ 永远进不了池**）");
                }
            }

            // 机制种子（`mechanic != None`）单独数一遍：用户点名的就是这一批
            int mechAll = 0, mechPool = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !IsMechanicSeed(all[i])) continue;
                mechAll++;
                if (TownUnlocks.IsUnlocked(all[i].Id)) mechPool++;
            }
            sb.Append("\n  其中**机制种子**（mechanic ≠ None）：").Append(mechAll).Append(" 条，池里 ")
              .Append(mechPool).Append(" 条");
            return sb.ToString();
        }

        private static bool IsMechanicSeed(SeedDef d)
        {
            string m = d.Mechanic;
            return !string.IsNullOrEmpty(m) && !string.Equals(m, "None", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>在该档位的插件里随机一件；该档位没有资产时退到全部 ✗（否则这个货位会空着）</summary>
        private static PluginAsset PickPluginOfTier(RunSession s, PluginAsset[] pool, int tier)
        {
            // 先数该档位有几件，再用**一次** RangeInt 定位 ⇒ 抽取只消耗一个随机数 ✓
            int count = 0;
            for (int i = 0; i < pool.Length; i++)
                if (pool[i] != null && (int)pool[i].quality == tier) count++;

            if (count > 0)
            {
                int pick = s.World.Rng.RangeInt(0, count);
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i] == null || (int)pool[i].quality != tier) continue;
                    if (pick-- == 0) return pool[i];
                }
            }

            int total = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i] != null) total++;
            if (total == 0) return null;
            int any = s.World.Rng.RangeInt(0, total);
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] == null) continue;
                if (any-- == 0) return pool[i];
            }
            return null;
        }

        /// <summary>
        /// 按"商品等级权重"掷一个档位，并随波次向高档倾斜。
        ///
        /// **唯一实现** ✗ —— `RollItem` 与 `RollPlugin` 都走它 ✓
        /// 原先这段内联在 `RollItem` 里；插件接进来时如果照抄一份，
        /// 就是"同一条曲线两种写法"（本项目已经因为"三处定价各写一份"吃过一次亏）✓
        /// 返回值是**权重表的下标**，同时也等于档位序号（表按 D/C/B/A/S 排列）。
        /// </summary>
        /// <summary>
        /// ★ v277：**已进行分钟数**（波次 → 分钟）—— 全工程唯一换算处 ✓
        ///
        /// `Clock.cs:101` 写死了 `WaveIndex => MinuteIndex + 1`，而 `MinuteIndex = ⌊RunTime / 60⌋`
        /// ⇒ 已进行分钟 `t = WaveIndex − 1`（第 1 波 = 第 0 分钟 = 开局）。
        ///
        /// 为什么用波次换算、而不是直接读 `s.RunTime`：
        ///   ① 商店**正好开在整分边界上**（`RunTime` 跨过 60 的倍数才进商店阶段）⇒
        ///      `⌊RunTime / 60⌋ == WaveIndex − 1`，两条路逐位同值 ✓
        ///   ② `TierWeightsAt(s, wave)` 的自检/探针**只给波次**（自己 `new RunSession()` 后
        ///      `RunTime` 还是 0）⇒ 若改读 `RunTime`，第 1/5/10 波会读成同一个 t，
        ///      `SelfCheck.cs:4244` 那条"逐波回升"就变成三个相等的数 ⇒ 无端变红 ✗
        ///   ③ 纯函数（只依赖 wave）⇒ 钉种子的读数可复现 ✓
        /// </summary>
        public static float MinutesAtWave(int wave) => wave > 1 ? wave - 1 : 0f;

        /// <summary>
        /// ★★ v277：**档位权重的唯一算式** —— 道具 / 插件 / 种子**三类共用** ★★
        ///
        /// 输入 = 表里的 `gradeWeights`（只用来定 D:C 之间的**内部比例**）+ 当前波次；
        /// 输出 = 与输入等长的权重数组，**已经归一**（合计 = 1）。
        ///
        /// ── 算式 ──
        ///   t     = `MinutesAtWave(wave)`
        ///   S/A/B = `s.T.shop.RarityCurve` 的 `SAt/AAt/BAt(t)`（公式在 `Defs.cs` 的 `RarityCurveDef`）
        ///   高档合计 > `lowCap` ⇒ 三者按同一系数缩回 `lowCap`（安全网，正常参数不触发）
        ///   D/C   = 剩余 (1 − S − A − B)，按表里 D:C 的**原比例**分
        ///
        /// ── 为什么不再乘写死的波次倾斜（0.05 / 0.04）──
        ///   乘性倾斜在 (5min ≤ 1%, 30min ≤ 10%, 单调) 三条约束下，S(10 分钟) 的**数学上界只有
        ///   2.55%**（上一轮 520 万组穷举的结论）⇒ 用旧机制到不了用户要的 5% ✓
        ///   改成加性目标后，三条约束是**构造上成立**的，不靠拟合 ✓
        ///
        /// ⚠️ 返回的是**概率**而不是相对权重：`RollGoodsTier` / `TierWeightsAt` 都会再按合计归一，
        ///    而合计恒为 1 ⇒ 归一是幂等的 ⇒ 两条路径读到的数逐位相同 ✓
        /// </summary>
        private static float[] TierWeightsRaw(RunSession s, GradeWeightDef[] weights, int wave)
        {
            if (weights == null || weights.Length == 0) return System.Array.Empty<float>();
            int n = weights.Length;
            var eff = new float[n];

            // ① 先按**品阶名**定位 S / A / B 三档（不按下标 ⇒ 表里换顺序也不会错位）
            int iS = -1, iA = -1, iB = -1;
            for (int i = 0; i < n; i++)
            {
                SeedGrade g = DefParser.ParseEnum(weights[i].grade, SeedGrade.D);
                if (g == SeedGrade.S) iS = i;
                else if (g == SeedGrade.A) iA = i;
                else if (g == SeedGrade.B) iB = i;
            }
            // ② 三档缺任何一档 ⇒ 退回"表里怎么写就怎么掷"（逐位等于 v276）
            //    这样一张只有 D/C/B 的表也不会被曲线改坏 ✓
            if (iS < 0 || iA < 0 || iB < 0)
            {
                for (int i = 0; i < n; i++) eff[i] = Mathf.Max(0.01f, weights[i].weight);
                return eff;
            }

            RarityCurveDef c = s.T.shop.RarityCurve;
            float minutes = MinutesAtWave(wave);
            float pS = c.SAt(minutes), pA = c.AAt(minutes), pB = c.BAt(minutes);
            float high = pS + pA + pB;
            // ③ 安全网：高档合计不得超过 `lowCap`（见 `RarityCurveDef.lowCap` 的说明）
            if (high > c.lowCap && high > 0f)
            {
                float shrink = c.lowCap / high;
                pS *= shrink; pA *= shrink; pB *= shrink; high = c.lowCap;
            }
            // ④ 低档（D/C）吃剩余，按表里原比例分 ⇒ "普通+优良吃剩余，总和 = 100%"
            float rest = Mathf.Max(0f, 1f - high);
            float lowTable = 0f;
            int lowCount = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == iS || i == iA || i == iB) continue;
                lowTable += Mathf.Max(0f, weights[i].weight);
                lowCount++;
            }
            for (int i = 0; i < n; i++)
            {
                if (i == iS || i == iA || i == iB) continue;
                eff[i] = lowTable > 0f
                    ? rest * Mathf.Max(0f, weights[i].weight) / lowTable
                    : (lowCount > 0 ? rest / lowCount : 0f);
            }
            eff[iS] = pS; eff[iA] = pA; eff[iB] = pB;
            return eff;
        }

        /// <summary>
        /// ★ v277：**某一波下每个档位的有效概率**（已归一，D→S 依次）—— 道具/插件那张表。
        ///
        /// 与 `RollGoodsTier` 用**同一个算式**（掷骰按这份权重做轮盘；自检也读它）
        /// ⇒ "前期高品质抑制"只有一个真相，不可能出现"表里一套、断言里另一套" ✓
        /// </summary>
        public static float[] TierWeightsAt(RunSession s, int wave)
        {
            return TierWeightsAt(s, s != null && s.T != null ? s.T.shop.itemGradeWeights : null, wave);
        }

        /// <summary>v277：**指定表的有效概率**（种子表 / 道具表都能读；与掷骰同一个算式）。</summary>
        public static float[] TierWeightsAt(RunSession s, GradeWeightDef[] table, int wave)
        {
            if (s == null || s.T == null) return System.Array.Empty<float>();
            GradeWeightDef[] use = table != null && table.Length > 0 ? table : s.T.shop.itemGradeWeights;
            float[] eff = TierWeightsRaw(s, use, wave);
            float total = 0f;
            for (int i = 0; i < eff.Length; i++) total += eff[i];
            for (int i = 0; i < eff.Length; i++) eff[i] = total > 0f ? eff[i] / total : 0f;
            return eff;
        }

        private static int RollGoodsTier(RunSession s, GradeWeightDef[] weights, int wave)
        {
            if (weights == null || weights.Length == 0) return 0;
            // ★ v277：把**传进来的这张表**一起交给算式（改前它被忽略，永远读 itemGradeWeights）
            //   —— 种子要接同一条曲线，就必须让这张表能是 `gradeWeights` ✓
            float[] eff = TierWeightsRaw(s, weights, wave);
            if (eff.Length == 0) return 0;
            float total = 0f;
            for (int i = 0; i < eff.Length; i++) total += eff[i];
            float roll = s.World.Rng.Range(0f, total);
            int picked = 0;
            for (int i = 0; i < eff.Length; i++)
            {
                picked = i;
                roll -= eff[i];
                if (roll <= 0f) break;
            }
            return picked;
        }

        /// <summary>
        /// v9 道具产出：先按"道具等级权重"掷等级（越到后期、幸运越高越容易出高等级），
        /// 再在该等级里随机一件道具；价格 = 基准价 ×(1 + 等级步进×档位) ×(1 + 每波增长×(波次-1))。
        /// </summary>
        /// <summary>
        /// 道具货位（★ v189：**上限过滤**在外层做一次，掷法本身不变）✓
        /// `ItemStacks[id] >= MaxStack` 的那一种不再出现（`TryBuy` 里那条判据的**同一条**）。
        /// </summary>
        private ShopOffer RollItem(RunSession s)
        {
            ShopOffer offer = null;
            for (int attempt = 0; attempt < CapRetries; attempt++)
            {
                offer = RollItemOnce(s);
                if (offer == null || offer.Item == null) return offer;
                if (!ItemAtCap(s, offer.Item)) return offer;
            }
            return offer;
        }

        /// <summary>
        /// ★ v214：**道具抽样** —— `TableDatabase.RandomItemOfGrade` 的**池化镜像**（与
        /// <see cref="PickSeedOfGrade"/> 同一套写法）：掷法逐句相同，只把 `Items` 换成候选池 ✓
        /// 候选池没被过滤时直接调原函数 ⇒ 随机序列与结果逐位等于改前 ✓
        /// </summary>
        private static ItemDef PickItemOfGrade(ref Pcg32 rng, SeedGrade grade)
        {
            if (!UnlockGateActive) return TableDatabase.RandomItemOfGrade(ref rng, grade);
            ItemDef[] pool = ItemCandidates();
            // ★ v314：快路的比较对象跟着换成 `RollableItems`（`ItemCandidates` 的底座）——
            //   两者引用相等 = "解锁门一件都没筛掉" ⇒ 走原函数（随机序列与结果逐位等于改前）✓
            if (pool.Length == 0 || ReferenceEquals(pool, TableDatabase.RollableItems))
                return TableDatabase.RandomItemOfGrade(ref rng, grade);
            int count = 0;
            for (int i = 0; i < pool.Length; i++) if (pool[i].Grade == grade) count++;
            if (count == 0) return TableDatabase.RandomItemOfGrade(ref rng, grade);
            int pick = rng.RangeInt(0, count);
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].Grade != grade) continue;
                if (pick-- == 0) return pool[i];
            }
            return pool[0];     // 不会走到（count > 0 ⇒ 上面必返回）
        }

        private ShopOffer RollItemOnce(RunSession s)
        {
            ShopDef cfg = s.T.shop;
            int wave = Mathf.Max(1, s.WaveIndex);
            // v107：幸运属性已删除，档位权重里那项幸运加成随之去掉。
            // 保留**波次**这条驱动：高等级随波次变多、低等级随之变少 —— 这是进度感的来源，与幸运无关。

            GradeWeightDef[] weights = cfg.itemGradeWeights;
            // 新：掷档位这段抽出成 `RollGoodsTier`，插件与道具**共用同一条曲线** ✓
            int picked = RollGoodsTier(s, weights, wave);
            SeedGrade grade = weights != null && picked < weights.Length
                ? DefParser.ParseEnum(weights[picked].grade, SeedGrade.D)
                : SeedGrade.D;

            ItemDef item = PickItemOfGrade(ref s.World.Rng, grade);
            ShopOffer offer = new ShopOffer
            {
                Kind = OfferKind.Item,
                Item = item,
                Quality = picked,
                Title = item.Name,
                Subtitle = $"[{grade} 级道具] {item.Desc}"
            };
            // v186：价格走 `PriceOf` —— 与种子/插件、以及"锁定项重新定价"同一个出口 ✓
            offer.Price = PriceOf(offer, wave);
            return offer;
        }

        public bool TryBuy(int index, RunSession s, out string reason)
        {
            reason = null;
            if (index < 0 || index >= Offers.Count) return false;
            ShopOffer o = Offers[index];
            // v45 修复：买走后的槽位是 Sold（Item/Seed 均为 null）。旧代码会走到 o.Item.Id → NullReferenceException。
            if (o.Kind == OfferKind.Sold)
            {
                reason = "该格已售出（想换货请花金币刷新商店）";
                return false;
            }
            if (s.Gold < o.Price)
            {
                reason = "金币不足";
                return false;
            }

            switch (o.Kind)
            {
                case OfferKind.Seed:
                {
                    if (s.Bag.FirstEmpty() < 0)
                    {
                        // ★ 满袋时买入"与袋中相同"的种子 → 直接合成（旧版合成规则）★
                        // 没有这一条会形成死锁：袋满 ⇒ 买不进用于合成的材料 ⇒ 永远合不了 ✓
                        // ★ v185：进货的等级不再恒为 1 ⇒ 传**这一格商品自己的等级** ✓
                        //   （传 StartingLevel 会把"买 2 级种子"降级成"给 1 级槽位喂料"✗）
                        if (s.Bag.TryMergeWithExisting(o.Seed, o.MutationId, o.Level, out SeedSlot _))
                            break;      // 已被合成消化 —— 钱照扣 ✓
                        reason = "种子袋已满，且没有同种同级的种子可以合成（先合成或出售）";
                        return false;
                    }
                    if (!s.Bag.AddSeed(o.Seed, o.MutationId, o.Quality, out reason, o.Level, out int boughtSlot)) return false;
                    // ★ 根系 B（用户原话："卖掉已有植物，再买入新的植物，依然没有替换"）★
                    //   病灶就在这里：买进来的种子落进 `FirstEmpty()` 那个槽，
                    //   而 `Bag.Selected` **一个字节没动** ⇒
                    //     ① `SelectedSlot` 还是原来那一颗（甚至是被卖掉后的空槽）；
                    //     ② 战斗里按 E 播种读 `Bag.SelectedSlot`（见 `sowSeed` 那三行）
                    //        ⇒ 种下去的仍是"旧的那颗"或 null ⇒ 田里当然不换 ✓
                    //   修法：**买到哪一格，当前选中就是哪一格** —— 落点由 `AddSeed` 回报，
                    //   不在这里用 `FirstEmpty()` 再猜一遍（那会是槽位的第二个真相）✓
                    s.Bag.Selected = boughtSlot;
                    s.AutoPlantSeed(o.Seed, o.Level);   // v24：买到的新种子自动种下｜v185：按商品等级种（2 级种子要长出 2 级的田）✓
                    break;
                }
                case OfferKind.Item:
                {
                    // ★★ v314：判据从"这一件自己叠满了吗"换成 `ItemAtCap` ★★
                    //   —— 对 149 件普通道具，`ItemAtCap` 就是原来那条式子（逐位相同）；
                    //   对复活道具族，它多兑现一条"与羽毛合计 ≤ 3"的**合计上限** ✓
                    //   ⚠️ 提示语一个字没改：两种"满"对玩家是同一件事（这件你已经拿不下了）✓
                    if (ItemAtCap(s, o.Item))
                    {
                        reason = "该道具已叠满";
                        return false;
                    }
                    s.ApplyItem(o.Item);
                    break;
                }
                case OfferKind.Plugin:
                {
                    // ★ v184：买完**立刻装到它适用的武器上** ★（用户需求⑥："买了插件没有挂载"）
                    //
                    // 旧版只做 `Goods.Plugins.Add(.., WeaponSlot.None)` —— 插件以"待装"状态
                    // 进了背包，而**全工程唯一的装配入口 `AutoInstallPending` 一个调用者都没有** ✗
                    // （grep 证据：它只在 RunSession 里定义，UI 里没有接线）
                    // ⇒ 玩家买了插件，背包里多一件、面板上插件位还是 0/6、效果一点没有 ✓
                    // 症状与"插件是假的"完全一样，所以用户报成"买了没挂载" ✓
                    //
                    // 为什么这里可以直接替玩家选武器：每件插件在 `plugins.json` 里
                    // `allowedWeapons` **只有一把**（12 件全是单元素数组）⇒ "装到哪"没有歧义 ✓
                    // 仍然走 `AutoInstallPending` → `WeaponSystem.Install` 那条链，
                    // 适用性 / 互斥 / 叠加上限 / 6 个插槽的检查一个都不跳过 ✓
                    if (s.Goods == null || o.Plugin == null) { reason = "插件栏不可用"; return false; }
                    // ★★ v287：**先验槽位，再扣钱** —— 装不下就整笔交易不发生 ★★
                    //
                    // ── 病因（用户原话：「插件已经满了，再次购买时应该购买失败，提示没有足够插件位置」）──
                    //   旧代码在这一格里只做 `Goods.Plugins.Add(..)` + `AutoInstallPending`，
                    //   而"插槽满"的判断在 `WeaponSystem.Install` **里面** ⇒ 装不上时插件以
                    //   `Slot == None` 留在背包，金币却在分支之后（本方法末尾）照扣 ✗
                    //   ⇒ 钱白花、提示只有一句"自动装配 0 件；背包剩余 N 件" ✓（用户报的就是这条）
                    //
                    // ── 为什么判据放在 `RunSession.CanTakePlugin` 而不是抄在这里 ──
                    //   商店 UI 也要用它把按钮灰化（见 `ImGuiHud.DrawShop`）⇒ 一个出口，两处共用，
                    //   不可能出现"按钮亮着但点下去说没有位置"这种两套真相 ✓
                    //   「叠到已有同 id」的例外也在那个方法里（③），这里不重复判断 ✓
                    //
                    // ⚠️ 返回 false 的代价：不扣钱、不建实例、`Offers[index]` 保持原样（仍可购买）✓
                    if (!s.CanTakePlugin(o.Plugin, out reason)) return false;
                    // ★★ v286：**一次购买只产生一个实例，并且只把这个实例交给自动装配** ★★
                    //
                    // ── 病因（用户本轮原话：「检查是否出现一次购买多次获取情况」）──
                    //   v278 为了让 `can_ripen` / `hoe_warmth` 买下就生效，给
                    //   `AutoInstallPending` 加了 `PluginAsset justPurchased`。它是**按资产（= 按 id）**
                    //   比的 ⇒ `pi.Asset != justPurchased` 对**背包里同 id 的每一份**都成立 ✓
                    //   而这两件同时是"开局白送"（`WeaponSystem.DefaultOwnedPlugins`）⇒
                    //   背包里**本来就躺着一份**（那份玩家从没选择装）✗
                    //   ⇒ 买 1 件 `can_ripen`：白送的那份 + 刚买的那份**一起被装掉**
                    //     ⇒ 水壶面板 `催熟 ×2`、提示语写"自动装配 **2** 件"
                    //     ⇒ 玩家看到的就是"**买 1 件，拿到 2 件**" ✓（就是本轮报的这个）
                    //
                    // ── 改法 ──
                    //   传**实例引用**而不是资产：装配队列里只有"这一次买下的那一格"被放行，
                    //   背包里同 id 的其它副本照旧按 v238 的规矩**留在背包**（发放 ≠ 装备）✓
                    //   ⚠️ 幂等：方法体里**只 Add 这一次**（买 1 件 = 背包 +1），
                    //      装成功后那一格一定会被 `InstallFromPending` 挪走（`AutoInstallPending` 里还有兜底删除）✓
                    PluginInstance bought = new PluginInstance(o.Plugin, WeaponSlot.None);
                    s.Goods.Plugins.Add(bought);
                    // ★ v278：把**刚买下的这一件**传进去 —— 它在 `DefaultOwnedPlugins` 里也要装
                    //   （否则 `can_ripen` / `hoe_warmth` 买了等于没买，见 `AutoInstallPending` 的说明）
                    // ★ v286：传的是**实例**，不是 `o.Plugin` —— 理由见上面整段
                    s.AutoInstallPending(out string autoSummary, bought);
                    Telemetry.Emit("plugin_autoequip", ("id", o.Plugin.id), ("summary", autoSummary));
                    break;
                }
            }

            s.Gold -= o.Price;
            string purchaseId = o.Kind == OfferKind.Seed ? o.Seed.Id
                             : o.Kind == OfferKind.Plugin ? o.Plugin.id
                             : o.Item.Id;
            Telemetry.Emit("shop_purchase", ("kind", o.Kind.ToString()), ("id", purchaseId),
                ("price", o.Price), ("gold_after", s.Gold));
            // v20：买走即解除锁定（锁定只是"保留到能买为止"）
            o.Locked = false;
            s.CarriedOffers.RemoveAll(x => x == o);
            // v44：买走一件后**不再自动补货**（格子空着，想换货只能花金币刷新）
            Offers[index] = new ShopOffer { Kind = OfferKind.Sold };
            return true;
        }

        public bool TryReroll(RunSession s, out string reason)
        {
            reason = null;
            int cost = RerollCost(s.T);
            if (s.Gold < cost)
            {
                reason = "金币不足";
                return false;
            }
            s.Gold -= cost;
            RerollCount++;
            Telemetry.Emit("shop_reroll", ("count", RerollCount), ("cost", cost));
            Refresh(s);
            return true;
        }
    }

    /// <summary>局会话：唯一逻辑入口 RunSession.FixedTick。</summary>
    public sealed class RunSession
    {
        public ulong Seed;
        public TuningDef T;
        public World World;
        public PlayerProgress Progress;
        public readonly SeedBag Bag;
        public readonly ShopSystem Shop = new ShopSystem();
        /// <summary>v15：上一回合锁定的商品（进商店时并入）。</summary>
        public readonly List<ShopOffer> CarriedOffers = new List<ShopOffer>();
        public readonly WaveDirector Waves = new WaveDirector();
        public readonly Dictionary<string, int> ItemStacks = new Dictionary<string, int>();

        /// <summary>
        /// ★ v197：**玩家身上的临时增益**（唯一持有者，实现见 `Prog/PlayerBuffs.cs`）。
        /// 写侧：`RunSession.HarvestPlot`（植物机制 1/2）与 `ProgMechanics`（道具爬升）；
        /// 读侧：`ProgMechanics.SyncRuntimeStats`（换层数）与**增益 UI**（另一路，只读 `Active`）✓
        /// </summary>
        public readonly PlayerBuffs Buffs = new PlayerBuffs();

        /// <summary>
        /// v197：水壶弹药上限的**运行时**值 = 基础 `WaterAmmoMax`（100）+ 道具"丰水壶"每层 +20。
        /// ★ 为什么不做成改 `WaterAmmoMax` 这个 const ★
        ///   它是 `const`，被 `DebugApi` / 探针两处按契约当**常量**读 ⇒
        ///   改成属性会牵动那些文件（其中 `DebugApi` 不在本轮范围内）✗
        ///   而"上限是多少"本来就是一个运行期的问句 ⇒ 开一个属性，clamp 点读它 ✓
        /// ★ v198：`ImGuiHud` 的水壶进度条**已改为按本属性作分母** ✓
        ///   （旧注释里那条"进度条会满格并溢出"的已知显示债在这一版还清，
        ///    `OnRainPickup` 的提示文案也从 `WaterAmmoMax` 同步换成它 ✓）
        /// </summary>
        public int WaterAmmoCap => ProgMechanics.ItemPanel.Of(this).WaterAmmoCap;

        /// <summary>
        /// 本局玩家持有的**插件**（新商品体系的运行时一侧）。
        ///
        /// ★ 为什么插件走它、而道具仍走 `ItemStacks` ★
        /// 道具是旧表（`items.json` + `ItemDef`）里的东西，`ItemStacks` 已经是它的唯一实现 ✓
        /// 插件没有旧表对应物，静态定义按新体系走 SO 资产 ⇒ 运行时状态也走新体系的 `PlayerGoods` ✓
        /// 强行把插件塞进 `ItemStacks`（`Dictionary&lt;string,int&gt;`）会丢掉"装在哪把武器上"这个状态 ✗
        /// </summary>
        public readonly PlayerGoods Goods = new PlayerGoods();

        /// <summary>v47：物体旁边的浮动文字（掉血 / 回血 / 成熟 / 缺水 / 收获）。</summary>
        public sealed class FloatingText
        {
            public Vector2 Pos;
            public string Text;
            public Color Color;
            public PopupKind Kind;
            public float Ttl;
            public float Total;
            public float Scale;
            /// <summary>
            /// ★ v282：**这一条弹字属于哪个插件样式**（`None` = 不是插件特殊伤害）★
            ///
            /// ── 它取代了 v279 那个"用**字面后缀字符**带样式"的做法 ✗ ──
            ///   改前：`PluginHitFx.Apply` 往文本尾部追加 `↗ ◎ / ↯ ♥`，
            ///   绘制侧 `StyleOfText` 读最后一个字符反查样式。
            ///   用户这一轮的原话是「剑气的弹字**取消 `/` 符号**，用一个剑加伤害的形式」
            ///   ⇒ 后缀字符**必须消失**，而样式又必须还能传到绘制侧 ⇒ 只能换载体 ✓
            ///   当年不这么做的理由是"要改 `RunSession` 的对外结构，它被 8 个探针读着"——
            ///   但**加**一个字段是纯增量：所有既有的对象初始化器都不写它 ⇒ 默认 `None`，
            ///   探针读到的既有字段一个字不变 ✓
            ///
            /// ── 为什么不能继续靠"文本里塞暗号" ✗ ──
            ///   ① 用户明确要求去掉这些字符（它们是玩家**看得见**的乱码感后缀）；
            ///   ② 本项目字体是雅黑，`↗ ◎ ↯ ♥` 有字形纯属运气，换成别的符号就成豆腐块
            ///      （`⚡` 的坑已经踩过，见 `PluginHitFx` 顶部注释）✗
            ///   ③ 加了图标之后，"后缀字符 + 图标"会同时出现 ⇒ 一条弹字两个符号 ✓
            /// ⚠️ 它不是 `[Serializable]`、不进存档、不进网络：只在一帧的绘制路径上被读 ✓
            /// </summary>
            public PluginPopupStyle Plugin;
        }

        /// <summary>v47：当前存活的浮动文字。逻辑在 FixedTick 里推进，表现层（ImGuiHud）只读它画字。</summary>
        public readonly List<FloatingText> Popups = new List<FloatingText>();

        /// <summary>
        /// ★ v181：**弹字口径计数器**（只读，供 `ImGuiHud.ProbeRunScope` 读；不参与任何玩法判定）★
        ///
        /// 为什么要在逻辑层记一对 "收到 / 上屏" 而不是只记"弹了几条" ✗
        ///   用户第 ③ 条要的是"删除'收获'弹字，只弹经验和金币" ⇒ 判据是
        ///   **收到了几条 `Harvest`、其中几条真的上了屏** ✓
        ///   只记"上屏"证明不了"发布端还在发"（那件事才是遗留风险的来源）✗
        ///   ⇒ `…Seen` 在**过滤之前**加，`…Kept` 在**过滤之后**加 ✓
        ///   改后的期望值：`Seen(Harvest) ≥ 1`（发布端仍会发，由本漏斗拦下）而 `Kept(Harvest) == 0` ✓
        /// </summary>
        public int PopupHarvestSeen, PopupGoldSeen, PopupExpSeen;
        public int PopupHarvestKept, PopupGoldKept, PopupExpKept;

        /// <summary>同时存在的浮动文字上限（防止极端情况下刷屏）。</summary>
        private const int MaxPopups = 48;

        public PhaseState Phase { get; private set; } = PhaseState.Boot;
        public PhaseState PhaseBeforePause { get; private set; } = PhaseState.Boot;
        public float PhaseTimer;
        public int Gold;
        // ── v23：弹药（整数）──
        /// <summary>水壶弹药（激光）。上限 100，每 0.5 秒消耗 10，回合开始 +50，雨滴 +20。</summary>
        /// <summary>v40：本帧鼠标世界坐标（肥料在鼠标位置爆炸用）。</summary>
        public Vector2 AimWorldLast;
        public int WaterAmmo;
        /// <summary>肥料弹药（炸弹）。上限 10，每次投掷 −1，回合开始 0，小猪掉落 +1。</summary>
        public int FertAmmo;
        public const int WaterAmmoMax = 100;
        public const int FertAmmoMax = 10;
        /// <summary>水壶激光的射击节拍（每 0.5 秒一次伤害）。</summary>
        public float LaserTimer;
        public int WaveIndex;
        public SeedDef[] InitialChoices = Array.Empty<SeedDef>();
        /// <summary>开局三选一各自的品质档位（与 InitialChoices 一一对应）。</summary>
        public int[] InitialChoiceQuality = Array.Empty<int>();
        public SeedDef InitialSeed;
        /// <summary>初始种子的品质档位。</summary>
        public int InitialQuality;
        /// <summary>种子袋合体交互：已选中的槽位（-1 未选）。</summary>
        public int MergeSelected = -1;

        // ── v6 工具升级（与种子同一套品质机制）：锄头 / 水壶 / 肥料 / 播种手套
        public const int ToolHoe = 0;
        public const int ToolCan = 1;
        public const int ToolFertilizer = 2;
        public const int ToolGlove = 3;
        public const int ToolCount = 4;

        /// <summary>
        /// ★ v186：**武器"品质"这个概念已删除** ★（用户原话："删除武器品质的概念，武器没有品质区分"）
        ///
        /// 这是一把工具的**升级档位**（0..4），它只影响两件与伤害无关的事：
        ///   · 同时操控的耕地数（`WorkCapability` = 1 + 档位）
        ///   · 水壶/肥料的生长减免（`tools.waterBonusPerTier / fertBonusPerTier`）
        /// ★ 它**不乘伤害、不上公式、不参与卡面配色** ★ ——
        /// 上一版的 `ToolMult()` 就是"档位 → 数值倍率"的那一层乘法，
        /// 卡面因此印出 `× 品质 1.00`，而用户要的式子里**根本没有这一项** ✗
        /// ⇒ `ToolMult` 已**整个删除**，`quality.mult` 在战斗与显示路径上零读者 ✓
        /// （`QualityDef` 这个类型本身保留：种子/道具/插件仍用它表达稀有度）
        /// </summary>
        public readonly int[] ToolLevel = { -1, -1, -1, -1 };
        /// <summary>工具升级档上限（0..4）。**与品质无关**：没有名字、没有颜色、不乘伤害。</summary>
        public const int ToolTierMax = 4;
        /// <summary>本帧动作实际作用的耕地数量（HUD 显示"同时 n 块"）。</summary>
        public int ActiveGroupCount;
        /// <summary>v8：本帧正在耕作 → 禁止任何攻击（含左键）。</summary>
        public bool FarmingThisFrame;
        /// <summary>v8 度量：耕作期间被击中次数（用来评估"耕作禁止攻击"的代价）。</summary>
        public int FarmHits;
        /// <summary>v14：当前地块已完成的施肥次数与所需次数（HUD 弹出施肥 UI 用）。</summary>
        public int FertilizeHits;
        public int FertilizeTarget;

        // ★ v186：`ToolMult(int toolIndex)` **已删除** ✗ ★
        //
        // 它就是那个"档次 → 数值倍率"的乘法层（`T.quality.MultOf(ToolLevel[..])`）：
        //   · 运行时锄头/肥料各乘一次（水壶漏了 ⇒ 三条线本来就不一致）
        //   · 卡面再乘一次印成 `× 品质 1.00`
        // 用户给的式子是 `(基础 + 对应伤害) × 伤害倍数` —— **没有品质这一项** ✓
        // 即使当前表里 `quality.mult` = [1,1,1,1,1]（数值上不漂移），
        // 留着它就是留一个"能把伤害改掉的口子"，而且它必然会被重新接到某个显示上 ✗
        // ⇒ 整个函数删除，武器从此没有任何"品质 × 基础值"的通道 ✓

        /// <summary>
        /// 同时操控的耕地数 = 1 + 升级档位，上限 tools.workCapMax（默认 4）。
        /// v45：删除 WorkCap 属性加成——授予它的"操作手册"道具早已不存在，该项恒为 0。
        /// v186：**档位不是品质** —— 这里只读升级档，不读任何品质表 ✓
        /// </summary>
        public int WorkCapability(int toolIndex)
        {
            int cap = Mathf.Max(1, T.tools.workCapMax);
            if (toolIndex < 0 || toolIndex >= ToolCount) return 1;
            return Mathf.Clamp(1 + Mathf.Max(0, ToolLevel[toolIndex]), 1, cap + (cap - 1) * 3);
        }

        /// <summary>动作 → 对应的工具索引。</summary>
        public static int ToolIndexOf(PlotAction act)
        {
            switch (act)
            {
                case PlotAction.Dig: return ToolHoe;
                case PlotAction.Sow: return ToolGlove;
                case PlotAction.Water: return ToolCan;
                case PlotAction.Fertilize: return ToolFertilizer;
                default: return ToolHoe;
            }
        }

        public string ToolName(int toolIndex)
        {
            switch (toolIndex)
            {
                case ToolCan: return "水壶";
                case ToolFertilizer: return "肥料";
                case ToolGlove: return "播种手套";
                default: return "锄头";
            }
        }

        /// <summary>
        /// 升级 / 合体一件工具：未拥有 → 获得该档；同档 → 合体升一档；更高档 → 直接替换；更低 → 拒绝。
        ///
        /// ★ v186：**参数与实现都不再经过品质表** ★
        /// 旧签名是 `UpgradeTool(int toolIndex, int quality, ...)`，内部走
        /// `T.quality.Clamp()` / `T.quality.Max` —— 于是"武器"与"品质"这两个概念在代码里
        /// 仍然被绑在一起（哪怕伤害公式已经不乘它）✗
        /// 现在档位用 `ToolTierMax` 钳制：武器只有"升级了几档"，没有"是哪一品"✓
        /// 行为与改前逐条相同（同样 0..4、同样"同档合体 +1"、同样"更低档拒绝"）✓
        /// </summary>
        public bool UpgradeTool(int toolIndex, int tier, out string reason)
        {
            reason = null;
            if (toolIndex < 0 || toolIndex >= ToolCount) { reason = "无效的工具"; return false; }
            int q = Mathf.Clamp(tier, 0, ToolTierMax);
            int owned = ToolLevel[toolIndex];
            if (owned < 0) { ToolLevel[toolIndex] = q; return true; }
            if (q == owned)
            {
                if (owned >= ToolTierMax) { reason = "该武器已是最高档"; return false; }
                ToolLevel[toolIndex] = Mathf.Min(ToolTierMax, owned + 1);
                return true;
            }
            if (q > owned) { ToolLevel[toolIndex] = q; return true; }
            reason = "你已有更高档的" + ToolName(toolIndex);
            return false;
        }

        // ════════════════════════════════════════════════════════════════════════
        // v171：**插件接线**
        //
        // 在此之前：`WeaponSystem.Aggregate` / `Install` 零调用 ⇒ 买到的插件只进背包，
        // **没有任何战斗效果**（它只是个收藏）✗
        // 这一段把"武器 → 插件修正"变成攻击路径真的读得到的东西 ✓
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>本局要求的插件插槽数（来自用户新标准："每个武器可以装备 6 个插件"）。</summary>
        public static int PluginsPerWeapon => PlayerGoods.MaxPluginsPerWeapon;

        /// <summary>
        /// **某把武器此刻的插件修正**。三条攻击路径的攻击入口都调它一次 ✓
        ///
        /// ★ 为什么是"每次攻击算一次"，而不是缓存 ★
        /// 缓存在本工程有一个已知的失效面：`PlayerGoods.Plugins` 可以在任意时刻被
        /// 商店/ToolLevel/调试改动，而**没有任何变更通知**✗
        /// 缓存就得给每一处写入都补一次失效，漏一处就是"插件装了不生效"这种最难查的 bug ✓
        /// 代价是一次线性扫描（插件总数 ≤ 6 × 3 = 18，可忽略）——
        /// 用可忽略的开销换掉一整类失效模式 ✓
        /// </summary>
        public WeaponModifiers ModsFor(WeaponSlot slot) => WeaponSystem.Aggregate(Goods, slot);

        /// <summary>
        /// ★ v287：**这件插件现在买得下吗** —— 商店购买的前置校验（唯一出口：`TryBuy` 与商店 UI 共用）★
        ///
        /// ── 病因（用户原话：「插件已经满了，再次购买时应该购买失败，提示没有足够插件位置」）──
        ///   `ShopSystem.TryBuy` 的 Plugin 分支原本**只有** `Goods.Plugins.Add(..)` + `AutoInstallPending`，
        ///   而"插槽满"这道判断藏在 `WeaponSystem.Install` **里面** ⇒
        ///   装不上时插件变成 `Slot == None` 的待装件**留在背包**，金币却已经在分支之后扣掉了 ✗
        ///   ⇒ 玩家看到的就是"钱花了、插件没装上、只有一句『自动装配 0 件』" ✓
        ///   ⚠️ 别和 `ShopSystem.PluginAtCap` 混：那条管的是**同一 id 叠到 maxStack**，
        ///      **不是**总槽位满 —— 两条判据互不替代，这里两条都要有 ✓
        ///
        /// ── 判据为什么逐条镜像 `WeaponSystem.Install` ──
        ///   买下之后唯一的自动落点是 `PreferredSlotFor(a)`（`allowedWeapons[0]`；空数组 = 当前手持那把，
        ///   与 `AutoInstallPending` 用的是**同一个**出口）⇒
        ///   照着 `Install` 的检查顺序把目标武器判一遍，就能提前知道"买下装不装得上" ✓
        ///   ⚠️ 不另写上限规则：`Fits` / `Find` / `CanStackMore` / `SlotsUsedOn` 全是既有的只读判据 ✓
        ///
        /// ── 「叠到已有同 id」为什么必须放行 ──
        ///   武器位满 6 件时，**同 id 的那一件还能叠层**（`PlayerGoods.Acquire` 有意先查同名、再查总上限）
        ///   ⇒ 这种情况**不算"没有位置"**，必须允许买 ✓
        ///   反过来（目标武器上没有同 id、6 个槽全占）才是真的买不下 ✓
        /// </summary>
        public bool CanTakePlugin(PluginAsset a, out string reason)
        {
            reason = null;
            if (a == null || Goods == null) { reason = "插件栏不可用"; return false; }

            // ① 同一 id 已到上限（Unique 有 1 个 / Stackable 到 maxStack）⇒ 买来也叠不进去
            //    与货架的 `PluginAtCap` 过滤是**同一条**判据，玩家不会看到"货架上有、点下去说满了" ✓
            if (ShopSystem.PluginAtCap(this, a))
            {
                reason = a.stackRule == StackRule.Unique ? "该插件唯一，已经拥有了" : "该插件已叠满";
                return false;
            }

            // ② 买下之后会自动装到这把武器上（与 `AutoInstallPending` 同一个出口）
            WeaponSlot target = PreferredSlotFor(a);
            if (target == WeaponSlot.None || !a.Fits(target))
            {
                reason = "这件插件没有适用的武器";
                return false;
            }

            // ③ 例外：目标武器上已有同 id 且还能叠 ⇒ 不占新插槽 ⇒ 槽位满也照样买得下 ✓
            PluginInstance have = Goods.Find(a, target);
            if (have != null)
            {
                if (have.CanStackMore && !a.exclusivePerWeapon) return true;
                reason = a.exclusivePerWeapon ? "这把武器已经装过同类插件" : "该插件已叠满";
                return false;
            }

            // ④ 要新占一个插槽 ⇒ 必须还有空位（`SlotsUsedOn` 数的是**实例**，不是层数）
            int used = Goods.SlotsUsedOn(target);
            if (used < PlayerGoods.MaxPluginsPerWeapon) return true;

            // ⚠️ 后缀不写"先卸下一件再买"：本局**没有**卸载插件的入口（grep `Uninstall` = 0 处），
            //    写了就是一句玩家做不到的指引 ⇒ 只说"买下也装不上"这句实话 ✓
            reason = $"没有足够插件位置：{WeaponSlotName(target)} 插件位"
                   + $" {used}/{PlayerGoods.MaxPluginsPerWeapon} 已满（买下也装不上）";
            return false;
        }

        /// <summary>
        /// **玩家把一件插件装到某把武器上**（唯一的玩家侧安装入口）。
        ///
        /// 它自己不判断任何规则 ✗ —— 适用武器 / 互斥 / 叠加上限 / **6 个插槽** 全在
        /// `WeaponSystem.Install` → `PlayerGoods` 这一条链上 ✓
        /// 这里只负责"把结果说清楚"（提示文案）✓
        ///
        /// 接受两种情况，因为它们对玩家是同一件事：
        ///   · 插件已买到手（在背包里 `Slot == None`）→ **挪**到这把武器上 ✓
        ///   · 插件还没买（商店直接装）→ 直接进这把武器 ✓
        /// </summary>
        public bool InstallPlugin(PluginAsset plugin, WeaponSlot slot, out string reason)
        {
            bool ok = WeaponSystem.Install(Goods, plugin, slot, out reason);
            if (ok)
            {
                PluginInstance inst = Goods.Find(plugin, slot);
                SetPrompt($"{plugin.displayName} → {WeaponSlotName(slot)}"
                          + $"（{Goods.SlotsUsedOn(slot)}/{PlayerGoods.MaxPluginsPerWeapon} 插件位，"
                          + $"叠 {inst?.Stacks ?? 1} 层）");
                Telemetry.Emit("plugin_install", ("id", plugin.id), ("slot", (int)slot),
                    ("used", Goods.SlotsUsedOn(slot)));
            }
            else SetPrompt("装不上：" + reason);
            return ok;
        }

        /// <summary>
        /// 把背包里**所有**待装插件（`Slot == None`）分别装到它们各自适用的武器上。
        /// 返回装成功的件数。
        ///
        /// 给"一键装配"用（UI 那一层直接调它即可）✓
        /// 装不下的会**留在背包里**并保持原样 —— 不许出现"背包少了、武器上也没有"✗
        ///
        /// ★ v238：**默认拥有的那批（`WeaponSystem.DefaultOwnedPlugins`）一律不自动装** ★
        /// 用户原话：「战斗模式异常地获取了大量插件，我没有选择的前提下」。
        /// 病根就在这个方法：`TryBuy` 买**任何一件**插件都会调它，而它当时把背包里所有待装件一次装完
        /// ⇒ 开局白送的那 8 件被"顺手"装到三把武器上（3/6 · 3/6 · 2/6）⇒ 变成"没选择就拿到一堆插件" ✗
        /// 现在的语义：**发放 ≠ 装备**。
        ///   · 商店买的插件 → 照旧自动装（采购动作本身就是玩家的选择）✓
        ///   · 默认拥有那批 → **留在背包**，等玩家自己选武器（`InstallPlugin` / 面板）✓
        /// </summary>
        /// <param name="justPurchased">
        /// ★ 刚在商店**花金币买下**的那一件（其余待装件照旧按 v238 的规矩留在背包）★
        /// 为什么需要这个参数：`WeaponSystem.IsDefaultOwned` 是**按 id** 判的，而 `can_ripen`
        /// 既是"开局白送的默认拥有"、又是商店里能买的商品 ⇒ 旧写法把**买来的那一份也一起跳过**，
        /// 症状就是"买了催熟插件，背包多一件、武器上零件、效果一点没有"（上一轮报的就是这个）✗
        /// 采购动作本身就是玩家的选择 ⇒ 它不该被"默认拥有不替你装"那条规则挡住 ✓
        ///
        /// ★★ v286：参数类型 `PluginAsset` → `PluginInstance`（**按实例**放行，不按 id）★★
        /// 病因（本轮用户原话：「检查是否出现一次购买多次获取情况」）：
        ///   v278 按资产比 ⇒ `pi.Asset != justPurchased` 对背包里**同 id 的每一份**都成立，
        ///   而 `can_ripen` / `hoe_warmth` 开局就白送一份躺在背包里（玩家从没选择装）✗
        ///   ⇒ 买 1 件会把"白送的那份 + 刚买的那份"一起装掉 ⇒ 面板 `×2`、提示"自动装配 2 件"
        ///   ⇒ 看起来就是"一次购买多次获取" ✓
        /// 现在只放行**这一次买下的那一格**；同 id 的其它副本照旧留在背包（v238：发放 ≠ 装备）✓
        /// </param>
        public int AutoInstallPending(out string summary, PluginInstance justPurchased = null)
        {
            int ok = 0, kept = 0;
            var left = new List<string>();
            for (int i = 0; i < Goods.Plugins.Count; i++)
            {
                PluginInstance pi = Goods.Plugins[i];
                if (pi == null || pi.Asset == null || pi.Slot != WeaponSlot.None) continue;
                // ★ v238：默认拥有的不替你装（见上面整段说明）
                // ★ v278：**但刚花钱买下的那一件必须装**
                // ★ v286：判据改成**实例相等**（`pi != justPurchased`）—— 按 id 判会把背包里
                //   同 id 的白送份一起装掉 ⇒ "买 1 件装 2 件"（用户报的那条）✗
                if (WeaponSystem.IsDefaultOwned(pi.Asset) && pi != justPurchased) { kept++; continue; }
                WeaponSlot target = PreferredSlotFor(pi.Asset);
                if (target == WeaponSlot.None) { left.Add(pi.Asset.displayName + "（无适用武器）"); continue; }
                if (WeaponSystem.Install(Goods, pi.Asset, target, out string why))
                {
                    ok++;
                    // ★ v286 幂等兜底：`InstallFromPending` 是"挪"不是复制，成功后背包里那一格
                    //   应当已经消失。这里再确认一次 —— 万一它还在（装上但背包那份没被移走），
                    //   就地删掉，**绝不留下"1 装 + 1 待装"的两份**（上一轮标注的那条遗留风险）✗
                    if (i < Goods.Plugins.Count && ReferenceEquals(Goods.Plugins[i], pi))
                        Goods.Plugins.RemoveAt(i);
                    i--;                          // 背包里少了一件，索引回退一格
                }
                else left.Add($"{pi.Asset.displayName}（{why}）");
            }
            summary = $"自动装配 {ok} 件；背包剩余 {Goods.PendingCount} 件"
                      + (kept > 0 ? $"（其中 {kept} 件是默认拥有：**保留在背包**，要你自己选武器装）" : "")
                      + (left.Count > 0 ? "，未装上：" + string.Join("、", left) : "");
            SetPrompt(summary);
            if (kept > 0)
                Log.Info($"[插件] 自动装配跳过 {kept} 件默认拥有（发放 ≠ 装备）：自动装 {ok} 件，背包剩 {Goods.PendingCount} 件");
            return ok;
        }

        /// <summary>插件**优先**装到哪把武器（`allowedWeapons` 的第一项）。空数组 = 任意 ⇒ 用当前手持的那把。</summary>
        private WeaponSlot PreferredSlotFor(PluginAsset a)
        {
            if (a == null) return WeaponSlot.None;
            if (a.allowedWeapons != null && a.allowedWeapons.Length > 0) return a.allowedWeapons[0];
            // 通用插件（本表已无此类，机制保留）：装到玩家当前手持的武器上
            int w = World != null && World.Player != null ? World.Player.WeaponIndex : 0;
            switch (w)
            {
                case PlayerActor.WeaponCan: return WeaponSlot.Can;
                case PlayerActor.WeaponFertilizer: return WeaponSlot.Fertilizer;
                default: return WeaponSlot.Hoe;
            }
        }

        /// <summary>武器槽的名字（提示文案与自检共用一份说法）。</summary>
        public static string WeaponSlotName(WeaponSlot w)
        {
            switch (w)
            {
                case WeaponSlot.Hoe: return "锄头";
                case WeaponSlot.Can: return "水壶";
                case WeaponSlot.Fertilizer: return "肥料";
                default: return "未指定";
            }
        }

        public string ResultText = "";
        public bool Paused;

        /// <summary>
        /// ★ v183：抽卡/升级选卡面板是否正开着 —— **世界时间停摆的唯一判据** ★
        ///
        /// 用 `Phase == LevelReward` 表达，而不是让 HUD 反过来推一个
        /// `Session.WorldPaused = true` 之类的标志位 ⇒ **不新增可被写错的状态** ✓
        /// （阶段机已经是"抽卡正在进行"这件事的真值；再从外面写一个布尔就是第二份真相 ✗）
        ///
        /// 它只管一件事：`FixedTick` 里的 `RunTime += dt` / `TickShopTruck` 不再执行 ✓
        /// `Paused`（Esc 菜单）仍然在更外层短路，两者互不替代 ✓
        /// </summary>
        public bool RewardPanelOpen => Phase == PhaseState.LevelReward;

        /// <summary>v11：升级后待抽的卡（每次升级 +drawsPerLevel 次）。</summary>
        public int PendingRewardDraws;
        /// <summary>
        /// ★ v201：上一次已经发过抽卡的等级 —— 抽卡次数只认"等级差"，不认"事件数" ★
        /// 消费点只有一个：`OnLevelUp` 里 `(e.Level - _rewardLevelMark) × drawsPerLevel`。
        /// `Start()` 里跟着新一局的 `Progress` 一起复位。
        /// </summary>
        private int _rewardLevelMark = 1;
        /// <summary>当前三张卡（已经过融合）。</summary>
        public readonly List<LevelRewardCard> RewardCards = new List<LevelRewardCard>();
        /// <summary>v13：本轮已重抽次数（价格 = 基础 + 步进 × 次数）。</summary>
        public int RewardRerollCount;
        /// <summary>v12：当前选中的卡（-1 未选，需再点"确认"才生效）。</summary>
        public int RewardSelected = -1;
        /// <summary>本次抽卡的整体进度（第 n / 总 N 次）。</summary>
        public int RewardDrawIndex;
        public int RewardDrawTotal;

        public int CurrentPlotIndex = -1;
        public string Prompt = "";
        public float PromptProgress;
        /// <summary>当前动作（浇水/施肥等）的进度 0–1，与生长进度分开显示。</summary>
        public float ActionProgress;
        /// <summary>当前动作名称（HUD 显示用）。</summary>
        public string ActionName = "";

        public float RunTime;
        /// <summary>耕作动作实际耗时累计（验收指标：耕作占比 25%–45%）。</summary>
        public float FarmActionSeconds;
        /// <summary>战斗阶段总时长（耕作占比的分母）。</summary>
        public float CombatSeconds;
        public int TotalRerolls;
        public RunBot Bot;

        private float _promptHold;

        public RunSession()
        {
            T = TableDatabase.Tuning;
            Bag = new SeedBag(T.seedBag.slotCount, T.seedBag.unlimitedSeeds);
        }

        public bool IsCombat => Phase == PhaseState.Combat;
        /// <summary>
        /// 本局是否已结束。★ v165：只剩失败一条 ★
        /// 计时制是无限模式，**唯一的结束条件是玩家死亡**；
        /// 原来的 `|| Phase == PhaseState.Victory` 已随"守住全部 N 波"的判定一起删除
        /// （那条判定依赖 `TotalWaves`，也已删除）。
        /// </summary>
        public bool IsOver => Phase == PhaseState.Defeat;
        public int Kills => World?.KillCount ?? 0;

        public void Start(ulong seed)
        {
            Seed = seed;
            PluginMech.Reset();   // ★ v253：跨帧机制状态**每局清零**（上一局的回响不许打到这一局）
            PluginField.Reset();  // ★ v255：埋着的雷 / 敌人身上的印 / 护盾计时同样不许跨局
            _pluginFxFaulted = false;   // ★ v281：表现层异常的"只报一次"闸也随局复位（见 OnFloatingText）
            _goldBonusPending = 0f;     // ★ v282：富豪的金币弹字账也随局清零（见 WithGoldDamage）
            Telemetry.Clear();
            World = new World(T, seed);
            Progress = new PlayerProgress(T);
            _rewardLevelMark = Progress.Level;   // ★ v201：新一局的抽卡计数从当前等级（1）起算
            // ★ v197：机制运行时的跨局隔离（唯一复位点，理由见 `ProgMechanics.Reset`）★
            ProgMechanics.Reset();
            Buffs.Clear();

            var player = new PlayerActor
            {
                Id = World.NextActorId(),
                Team = Team.Player,
                Progress = Progress,
                Pos = new Vector2(0f, -7f),
                // v85：玩家受击半径**略微小于模型**（需求原话）。
                // 模型可视半径 ≈ 0.719m/2 × CharScale(2.6) × PlayerScale(1.2) ≈ 1.12m，
                // 这里取 0.62 作为基准半径 —— 经 ScaledRadius 后约 0.92m，
                // 比模型边缘再收 ~0.2m。判定框比模型小，观感上"擦过去了就是没打中"，
                // 比"看着没碰到却掉血"舒服。
                //
                // ★ v180：**基准 0.62 → 0.50**（用户要求"减小玩家受击面积"）✗
                //   有效半径 = 0.50 × HitRadiusScale(1.486) = **0.743m**（改前 0.921m，−19.4%）
                //   受击面积 π r² 从 2.667 m² 降到 1.734 m²（**−35%**）
                //   ★ 为什么不是"再收 0.1m"那种小步 ★
                //     0.62→0.56 只降 −9.7% 面积，玩家感知不到；
                //     而 0.45 以下会小于**敌人的基础半径**（grunt 0.45×1.486=0.669），
                //     出现"敌人贴脸站着却打不到"的反向手感 ✗
                //     ⇒ 0.50 是"明显变小、但仍大于敌人半径"的那一档 ✓
                //   ⚠️ 这条是**玩法数值**：敌人靠近判定（`TouchTick` 的 reach = 双方半径之和）
                //     与弹幕命中都会跟着变小 ⇒ 双向都是"更难被打中" ✓
                Radius = Actor.ScaledRadius(0.50f)
            };
            player.Health.Setup(Progress.Stats.Get(StatId.MaxHp), Progress.Stats.Get(StatId.Shield));
            World.Player = player;
            World.Farm.SetMaxPlots(Progress.MaxPlots);
            SyncSeedSlots();   // v29：耕地增加 → 种子袋上限同步（1:1）

            // v48：开局发放金币——Prepare 阶段就能进商店，第一回合前不至于空手
            Gold = Mathf.Max(0, T.player.startGold);
            WaveIndex = 0;
            RunTime = 0f;
            // ★ v175：**新一局必须重新武装商店车** ✗ —— 否则它的状态机会**跨局泄漏** ★
            //
            // 病因：`TickShopTruck` 只在 `!_truckArmed` 时调 `Truck.Reset(Clock)` ✓
            //   而 `_truckArmed` 从来没人清 ✗ ⇒ 同一 `RunSession` 上**再开一局**时：
            //   · `Reset` 不再跑 ⇒ `_nextCycleAt` 还是**上一局**的绝对时刻 ✗
            //   · 更糟：若上一局死在车**某个阶段中途**，`Truck.Current` **保持那个阶段** ✓
            //     ⇒ 新一局开局就有一辆"半路出现"的车（可能在 `Dock`、也可能在 `Leaving`）✗
            // 实测线索：真机上见过 `TRUCK t=54.10s｜Entering` ✗
            //   而 `Reset` 锚定的是 `GameTime + 60` ⇒ 首轮入场**应当**在 t≈60.03 ✓
            //   （headless 400s 实测：出现时刻 60.03 / 120.07 / 180.10 ✓ 间隔 60.03~60.04 ✓）
            // ⚠️ 这里**只清锁**、不调 `Truck.Reset(Clock)` ✗ ——
            //   此刻 `Clock.GameTime` 还是**旧的**（`Clock.SyncTo` 发生在下一次 `FixedTick`）✗
            //   在这里 Reset 会把锚定算成 `旧时间 + 60` ⇒ 又错一次 ✓
            //   清锁之后，下一次 `TickShopTruck` 会用**新的** `GameTime`（≈0）重新锚定 ✓
            _truckArmed = false;
            TotalRerolls = 0;
            // ★ 阶段 3：新一局必须清空"上一局残留"的三样跨帧状态 ★
            //   ① 持续效果表（灼烧/减速/毒区）—— 不清的话新局的敌人会带着上一局的火 ✓
            //   ② 双击的待执行刀 —— 不清的话开局第一帧会凭空多出一刀 ✓
            //   ③ 自充的零头 —— 不清的话新局第一次回水会多回 1 点 ✓
            Dots.Clear();
            _pendingSwings.Clear();
            ClearSwordWaves();          // ★ v185：在飞的剑气也是"跨帧状态" ⇒ 新一局必须清空
            ClearWaterBalls();          // ★需求④：在飞的水球同理（且发射计数归零 ⇒ 探针读到的是本局）
            WaterBallSpawnedTotal = 0;
            _autoChargeAccum = 0f;
            // ★ v282：吸血的批次与"1 秒桶"也是跨帧状态 ⇒ 新一局必须清零
            //   （不清的话上一局残留的累计伤害/已回额度会让开局第一刀少回或超回）
            _lsBatchOpen = false;
            _lsBatchDmg = 0f;
            _lsBatchPct = 0f;
            _lsBatchOwner = null;
            _lsSecStart = -1e9f;
            _lsSecUsed = 0f;
            // ★ v181：弹字口径计数器也是"跨帧状态" ⇒ 新一局必须归零（否则探针读到的是累计值）★
            PopupHarvestSeen = 0;
            PopupGoldSeen = 0;
            PopupExpSeen = 0;
            PopupHarvestKept = 0;
            PopupGoldKept = 0;
            PopupExpKept = 0;
            InitialChoices = RollInitialChoices();
            // v9：初始种子固定为「D 级 + 白品质」
            InitialChoiceQuality = new int[InitialChoices.Length];
            for (int i = 0; i < InitialChoiceQuality.Length; i++) InitialChoiceQuality[i] = 0;
            InitialQuality = 0;
            MergeSelected = -1;
            InitialSeed = null;
            Bag.Selected = 0;

            // ★ v237：**默认拥有的插件**（用户原话："加 6~10 条『普通/一般』插件，默认拥有"）★
            //   清单在 `WeaponSystem.DefaultOwnedPlugins`（唯一真值源），这里只是"开局发一次" ✓
            //   ★ 发放形态 = **背包（`WeaponSlot.None`）**，不是直接装上 ★
            //     理由写在 `WeaponSystem.DefaultOwnedPlugins` 那一段：`Aggregate` 只聚合
            //     `Slot` 等于该武器的实例 ⇒ 背包里的这几件对**任何一局**的平衡都是零影响，
            //     装上之后才生效（走 `AutoInstallPending` / `InstallPlugin` 那条既有链路）✓
            // ★ v290：把这一局的三个句柄交给 `PluginExtra2`（7 件新机制的唯一执行点）
            //   放在 `GrantDefaultOwned` 之前：它内部会 `Reset()`，
            //   而发握默认插件不依赖本类的状态 ⇒ 先后顺序不影响任何读数 ✓
            PluginExtra2.Attach(Goods, World, player);
            // ★★ v308：训练场开局**不白送**这两件（用户原话："默认不要装备任何东西
            //    （插件存在异常装备）"）★★
            //
            //  ── 病因（上一轮漏掉的那一处）──
            //    本行是**战斗与训练共用**的开局路径：训练场也是 `new RunSession()` +
            //    `Session.Start(seed)`，于是 `WeaponSystem.GrantDefaultOwned` 照样把
            //    `hoe_warmth` / `can_ripen` 以 `Slot == None`（背包待装）塞进 `Goods.Plugins`。
            //    而 `TrainingVault` 左栏的"已装"标记（`PluginCountAll`）当时**不看 Slot**
            //    ⇒ 打开仓库就看见两件插件写着"已装 ×1"，武器上却一格没有 ⇒ 正是用户报的
            //    "插件存在异常装备" ✓
            //
            //  ── 改法：一个三元判断，`TrainingMode.CleanLoadout` 默认 false ──
            int granted = TrainingMode.CleanLoadout ? 0 : WeaponSystem.GrantDefaultOwned(Goods);
            //  ── "false 时不变"逐条论证 ──
            //    ① 战斗场景（`SeedfallArena.unity`）里没有任何组件调 `TrainingMode.Enable()`
            //       （唯一调用点是 `TrainingBootstrap.Awake`，该组件只挂在 `Training.unity`）
            //       ⇒ 战斗里 `CleanLoadout` 恒为字段初值 **false** ⇒ 三元结果恒为
            //       `WeaponSystem.GrantDefaultOwned(Goods)`，与本行改前**逐字节相同** ✓
            //    ② `granted` 这个局部变量只被下面那一个 `if (granted > 0)` 读，语义不变 ✓
            //    ③ 训练场里 `granted == 0` ⇒ 不打印"[插件] 默认拥有"那行日志 ⇒ 日志也如实 ✓
            if (granted > 0)
            {
                Log.Info($"[插件] 默认拥有：本次开局发放 {granted} 件普通/一般插件（背包待装，" +
                         $"合计 {Goods.PendingCount} 件待装）");
            }

            SetPhase(PhaseState.Prepare, T.shop.firstPrepareTime);
            Telemetry.Emit("run_start", ("seed", seed), ("build", Application.unityVersion));

            Bus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            Bus.Subscribe<RainPickupEvent>(OnRainPickup);   // v23：雨滴 → 水壶 +20
            Bus.Subscribe<ExpOrbDroppedEvent>(OnExpOrbDropped);
            Bus.Subscribe<ExpOrbPickupEvent>(OnExpOrbPickup);
            Bus.Subscribe<LevelUpEvent>(OnLevelUp);
            // ★ T2：`PlantSpawnedEvent` / `PlantDiedEvent` 的订阅**已删除** ✗ ★
            //   两个事件的**唯一发布点**都在 `PlantAlly`（出生）与 `PlantAlly.OnDeath`（死亡）里 ✓
            //   而 `PlantAlly` 已整条下线 ⇒ 它们永远不会被发布 ⇒ 留着订阅等于留两条死通道 ✓
            //   （对比先例：`ExpOrbPickupEvent` 是"只发不收"，那两个是"收发两边都死"——
            //     所以这次可以一起删，不存在"删了谁编译不过"的问题 ✓）
            Bus.Subscribe<FloatingTextEvent>(OnFloatingText);
        }

        /// <summary>退订必须与 Start 的订阅一一对应——旧版漏退 3 个，多局共存时会把事件投给已结束的会话。</summary>
        public void Dispose()
        {
            Bus.Unsubscribe<EnemyKilledEvent>(OnEnemyKilled);
            Bus.Unsubscribe<RainPickupEvent>(OnRainPickup);
            Bus.Unsubscribe<ExpOrbDroppedEvent>(OnExpOrbDropped);
            Bus.Unsubscribe<ExpOrbPickupEvent>(OnExpOrbPickup);
            Bus.Unsubscribe<LevelUpEvent>(OnLevelUp);
            // ★ T2：两个植物事件的退订**一并删除**（与上面的订阅一一对应 ✓）
            Bus.Unsubscribe<FloatingTextEvent>(OnFloatingText);
        }

        private SeedDef[] RollInitialChoices()
        {
            int n = Mathf.Max(1, T.seedBag.initialChoices);
            SeedDef[] pool = TableDatabase.SeedsOfGrade(SeedGrade.D);
            var chosen = new List<SeedDef>();
            var used = new HashSet<string>();
            for (int i = 0; i < n && used.Count < pool.Length; i++)
            {
                SeedDef pick = pool[World.Rng.RangeInt(0, pool.Length)];
                if (!used.Add(pick.Id)) { i--; continue; }
                chosen.Add(pick);
            }
            return chosen.ToArray();
        }

        private void OnExpOrbDropped(ExpOrbDroppedEvent e)
        {
            Telemetry.Emit("exp_orb_drop", ("gold", e.Gold), ("exp", e.Exp), ("x", e.Pos.x), ("y", e.Pos.y));
        }

        private void OnExpOrbPickup(ExpOrbPickupEvent e)
        {
            Telemetry.Emit("exp_orb_pickup", ("gold", e.Gold), ("exp", e.Exp), ("gold_total", Gold));
        }

        private void OnRainPickup(RainPickupEvent e)
        {
            AddWaterAmmo(e.Ammo);
            SetPrompt($"拾取雨滴 → 水壶 +{e.Ammo}（{WaterAmmo}/{WaterAmmoCap}）");
            Telemetry.Emit("rain_pickup", ("water", WaterAmmo));
        }

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
            // ★ 本轮：「余温」（`hoe_warmth`）插件已按用户要求删除 ⇒ 这里不再有"击杀回血"结算
            //   （`OnWarmthKill` / `WeaponModifiers.KillHeal` 一并删除，不留死代码）
            // ★★★ v213：**BOSS 死亡 ⇒ 掉 1 颗主城种子**（★★ 战斗侧唯一的改动点，两行 ★★）★★★
            //   → 「掉哪一种（品质权重）」「进没进主城仓库」「战斗里那张奖励卡」全部由
            //     `TownDrops.OnBossKilled` 一处负责（新文件，与战斗逻辑无耦合）。
            //   → 这里只负责"认出这是 BOSS 死了"，判断走 `TownDrops.IsBossId`（读 enemies.json 的
            //     kind==Boss，不把 "boss"/"moth" 硬写在调用点）⇒ 将来加第三种 BOSS 不会静默不掉落 ✓
            //   → 下面那条小猪掉肥料是**并列的另一条**掉落，一个字没动（pig 的 kind 不是 Boss）✓
            if (TownDrops.IsBossId(e.EnemyId))
            {
                // ★ v254：第三个参数 = 这一击的**稳定身份**（`{种子}:w{波次}`）★★
                //   有了它，`TownDrops` 才能在击杀的**这一刻**把那一颗写进主城存档，
                //   并且保证"同一击被重复上报"不会发两次（幂等键，见 `TownDropLedger.KeyOf`）。
                //   ★ 掉落的种类与数量一个字没改（还是 `PickSeedIndex(BossKills)` 抽 1 颗）★
                TownDrops.OnBossKilled(e.EnemyId, e.Pos, TownDrops.RunKeyOf(Seed, WaveIndex));
                // ★ v218：**boss 死亡掉落 ⇒ 时间停止**（用户原话："boss死亡掉落时增加时间停止效果"）★
                //   只加这一行调用：掉落逻辑一个字没动（上面那行照旧先执行）✓
                //   ⚠️ 计时在 `CombatFeedback` 里用的是**未缩放时间**——
                //      用 `Time.deltaTime` 的话 timeScale=0 后它恒为 0，停止就永远回不来 ✗
                CombatFeedback.HitStopBoss();
            }

            // v23：击败小猪 → 掉落 1 个肥料弹药
            if (e.EnemyId == "pig")
            {
                AddFertAmmo(1);
                SetPrompt($"击败小猪 → 肥料 +1（{FertAmmo}/{FertAmmoMax}）");
                Telemetry.Emit("pig_killed", ("fert", FertAmmo));
            }
        }
        private void OnLevelUp(LevelUpEvent e)
        {
            // v11：每次升级累积一次抽卡。
            // v165：**消费点从"本波结束"搬到"升级这一刻"** ✗
            //   原来抽卡要等 `IsComplete` 之后才弹，而计时制下 `IsComplete` 永远不成立
            //   （分钟刻度要求 CombatElapsed ≥ 60s，而每分钟的 `StartWave` 恰恰把它归零 ⇒ 互斥），
            //   于是 `PendingRewardDraws` 只涨不消费、玩家永远抽不到卡 ✗
            //   用户原话是"只要升级了，直接弹出"，所以就在这里开界面 ✓
            // ★★ v201：**次数 = 实际涨了几级 × drawsPerLevel** ✗ 不是"升级事件来了几次"★★
            //   用户原话："刚刚只升级了一次，但抽卡却提供了两次机会……根据实际升级值决定次数"
            //
            //   ── 病因（旧代码数的是"事件/计数"，不是"等级差"）──
            //     ① `PendingRewardDraws += drawsPerLevel`（=1）本身没错，错在下面那行
            //        `RewardDrawTotal = RewardDrawIndex + PendingRewardDraws`：
            //        `RewardDrawIndex` 是**整局累加**（只在 `PickRewardCard` 里 ++，从不归零）✗
            //        ⇒ 第 2 次升级：1 + 1 = **2** ⇒ 只升了一级，面板却写"第 2/2 次"（看着是两次机会）✗
            //     ② `PlayerProgress.AddExp` 的 while 循环**只在循环后发一次** `LevelUpEvent`
            //        ⇒ 一次涨两级也只有一个事件 ⇒ 旧写法跨两级只给 1 次（少给）✗
            //   ⇒ 改成"等级差"：一次涨两级 ⇒ 2 次；同一等级重复到达 ⇒ 0 次（幂等）✓
            int gained = e.Level - _rewardLevelMark;
            if (gained <= 0) return;                       // 同一等级重复到达 ⇒ 不重复计数
            _rewardLevelMark = e.Level;
            PendingRewardDraws += gained * Mathf.Max(1, T.levelReward.drawsPerLevel);
            // v47：升级在玩家旁边弹出
            if (World != null && World.Player != null)
                Bus.Publish(new FloatingTextEvent
                {
                    Pos = World.Player.Pos, Text = "Lv " + e.Level, Kind = PopupKind.LevelUp
                });
            Telemetry.Emit("level_up", ("level", e.Level), ("max_plots", e.MaxPlots), ("pending_rewards", PendingRewardDraws));

            // v165：**立刻弹出抽卡**。
            // 只在战斗阶段弹 —— 升级只可能来自战斗中的经验获取；而在商店/抽卡界面里
            // 抢走界面会让玩家丢掉正在做的事 ✗
            if (Phase == PhaseState.Combat && !IsOver)
            {
                // ★ v201：新的一次升级从"第 1 次"数起 —— `RewardDrawIndex` 是**整局累加**
                //   （升级时从不归零）⇒ 直接加进总次数会让"只升一级"显示成"第 2/2 次" ✗
                RewardDrawIndex = 0;
                RewardDrawTotal = RewardDrawIndex + PendingRewardDraws;
                DrawRewardCards();
                // ★★ v303 训练场守卫：**跳过「选属性」面板**（用户原话："训练模式跳过升级选择属性"）★★
                //
                //  ── 守卫为 false（= 正常战斗）时，本段**不执行**，下面两行原样跑 ──
                //     `SetPhase(PhaseState.LevelReward, 0f);` → 面板照旧弹出
                //     `Telemetry.Emit("level_reward_start", …);` → 埋点照旧上报
                //  ⇒ 表达式没有一个是"被改过的"，只是**多了一个不成立的 if** ✓
                //
                //  ── 守卫为 true（训练场）时干了什么 ──
                //     **不切阶段**（于是 `RewardPanelOpen` 恒 false、世界时间照常走），
                //     而是就地把已经抽好的卡**全部按"第一张"选掉**：
                //       · 走的是 `PickRewardCard(0)` —— 玩家点卡片时的**同一个**函数
                //         ⇒ 属性入账、`PendingRewardDraws` 递减、`RewardDrawIndex` 递增、
                //           抽完最后一次的 `EnterCombatAfterReward()`（`SetPhase(Combat)`）全部照原样
                //         ⇒ **升级仍然有意义**（每升一级自动拿到一张卡的属性），
                //           不是"跳过面板就不给属性" ✓
                //       · `for` 的上限 64 是死循环保险：`PendingRewardDraws` 每轮必减 1，
                //         理论上 `Mathf.Max(1, drawsPerLevel)` × 这次涨的级数就结束；
                //         万一 `PickRewardCard` 因为表空返回 false，`break` 立刻收手（不留空转）✓
                //       · `return` 跳过的是**只有那两行**（面板 + 埋点），本方法后面的语句为 0 行 ⇒
                //         与"不 return 让它自然结束"逐字节等价 ✓
                if (TrainingMode.SkipLevelUpChoice)
                {
                    for (int guard = 0; guard < 64 && PendingRewardDraws > 0; guard++)
                        if (!PickRewardCard(0)) break;
                    return;
                }
                SetPhase(PhaseState.LevelReward, 0f);
                Telemetry.Emit("level_reward_start", ("draws", PendingRewardDraws), ("trigger", "level_up"));
            }
        }

        /// <summary>
        /// v47：把逻辑层发来的浮动文字收进列表，由 FixedTick 推进生命周期。
        ///
        /// ★ v181（用户第 ③ 条）：**"收获"那一条不再上屏** —— 只弹"经验"和"金币" ★
        ///   用户原话："删除'收获'弹字，只弹经验和金币"
        ///
        ///   ── 改前一次收获会弹几条（代码 + 实测）✗ ──
        ///     一次收获 = **3 条**，三条都在 `Actors.HarvestPlot` 里发布（`Actors.cs:905-934`）：
        ///       ① `PopupKind.Harvest`  · 文字 `"收获 " + 作物名` · 绿 ×1.15
        ///       ② `PopupKind.GoldGain` · 文字 `"金币+" + 数值`   · 黄 ×1.35
        ///       ③ `PopupKind.ExpGain`  · 文字 `"经验值+" + 数值` · 绿 ×1.35
        ///     ⇒ 要删的是第 ① 条；② ③ 原样保留 ✓
        ///
        ///   ── 为什么判据放在**这个漏斗**里，而不是去删 `Actors.cs` 那三行 ✗ ★（刻意的）★ ──
        ///     ① `Actors.cs` **不在本次可改范围**（别的任务正在改它）✓
        ///     ② 位置本身也更对：`OnFloatingText` 是**所有**浮动文字进屏的**唯一漏斗**
        ///        （`Bus.Subscribe&lt;FloatingTextEvent&gt;` 全工程只有 `Start()` 这一处 ✓
        ///          `Actors` / `WorldView` / `Combat` 都只是往总线**发**）
        ///        ⇒ "哪一类字不上屏"从此只有**一处**定义 ✓
        ///        写在发布端则要为每一个发布点各写一遍（今天只有 1 个，明天加收获弹字的人不会知道）✗
        ///     ③ 保留枚举值 `PopupKind.Harvest` 与 `PopupStyle` 里它的配色/字号：
        ///        那不是"注释掉的旧实现"，而是**枚举表的完整性**（`PopupStyle` 按枚举值分档）✓
        ///     ⚠️ 代价（写明）：`PopupKind.Harvest` 从此**没有上屏路径** ✓
        ///        发布端（`Actors.cs:905`）仍在发 —— 那是别人的文件，我按范围只收不改 ✓
        /// </summary>
        private void OnFloatingText(FloatingTextEvent e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            // ★ v181：① 收获那一条**不上屏**。②③（金币 / 经验）与其它种类一条都没动 ✓
            if (e.Kind == PopupKind.Harvest)
            {
                PopupHarvestSeen++;
                return;
            }
            // v181：口径计数（Seen = 过滤前，Kept = 过滤后）—— 只读，见上方字段说明
            if (e.Kind == PopupKind.GoldGain) { PopupGoldSeen++; PopupGoldKept++; }
            else if (e.Kind == PopupKind.ExpGain) { PopupExpSeen++; PopupExpKept++; }
            if (Popups.Count >= MaxPopups) Popups.RemoveAt(0);
            // v48：在对象周围的小圆内随机落点——同一目标连续弹字时不会完全重叠。
            //      用表现随机流（VisualRng），不污染逻辑确定性。
            Vector2 offset = World != null
                ? World.VisualRng.InsideCircle(Mathf.Max(0f, T.ui.popupRadius))
                : Vector2.zero;
            // ★ v191 补丁：玩家的红/蓝两行**再叠一个固定错位** ★
            //
            // 病灶：两行的锚点是**同一个** `Actor.Pos`，唯一的错位只有上面那个
            //   **半径 0.6m 的随机散布** ⇒ 随机的东西保证不了不重合 ✗
            //   实测抓图：`盾 2 + 血 2` 的受击，蓝字直接盖在红字上，看着像 `-22` ✗
            //   （证据：`Docs/captures/zoom-BEFORE-two-lines.png`）
            // 修法：错位规则放在 `PopupStyle.StackOffsetOf` —— **只在一处定义**，
            //   这里只负责"把它加到坐标上" ⇒ 以后要调间距不必翻文件 ✓
            // ⚠️ 固定错位把两行的**中心**拉开 0.76m，散布再各自抖 ≤0.6m；
            //   垂直分量上两者的最小中心距 = 0.76 − 0.6 = 0.16m，但**平均**约 0.76m ✓
            //   （随机散布保留不动：它的作用是"同一目标的连击不叠成一坨"，与这条正交）
            Vector2 stack = PopupStyle.StackOffsetOf(e.Kind);
            FloatingText ft = new FloatingText
            {
                Pos = e.Pos + offset + stack,
                // ★ 本地化：这里**只存原文，不存译文** ★
                //   在漏斗里 `L10n.S()` 会把译文固化进 `FloatingText.Text` ⇒ 切语言后这条不变 ✗
                //   而 `ImGuiHud.DrawPopups` → `Txt()` 本来就会再过一次 `L10n.S()`（幂等）
                //   ⇒ 存原文、**画的时候才翻**：切语言下一帧就是新语言 ✓
                Text = PopupStyle.TextOf(e.Kind, e.Text),
                Kind = e.Kind,
                // v69：颜色与字号统一由 PopupStyle 决定，发射点不再各写各的。
                // 事件里若显式给了 Scale（例如伤害按数值大小放大），就在类型基准上再乘。
                Color = PopupStyle.ColorOf(e.Kind),
                Ttl = PopupLifetime,
                Total = PopupLifetime,
                Scale = PopupStyle.ScaleOf(e.Kind) * (e.Scale <= 0f ? 1f : e.Scale)
            };
            // ★ 插件特殊伤害（弹射/收束/剑气/回响/伏雷/灼印/吸血）的弹字变体与命中特效 ★
            //   全工程**唯一**的插入点：判定/配色/后缀/特效都在 `PluginHitFx` 里，这里只负责递上去 ✓
            //
            // ★★ v281：**表现层的任何异常都不许冻结逻辑帧**（本轮最重要的结构闸）★★
            //
            // ── 为什么这里必须有 try/catch（用户报的"灼印叠加后直接卡住"）──
            //   本方法是由 `Bus.Publish` 在**逻辑帧内部同步调用**的：
            //     `FixedTick` → `HandlePlayer`/`BlastDamageAt` → `World.DealDamage`
            //       → `Actor.OnHitLanded` → `Bus.Publish(FloatingTextEvent)` → **本方法**
            //       → `PluginHitFx.Apply` → `Spawn`（对象池 / `LineRenderer` / `Shader.Find`）
            //   ⇒ 这是个"表现层代码跑在逻辑帧调用栈里"的地方。`Spawn` 一旦抛
            //     （池根被销毁后的 `MissingReferenceException`、材质/着色器缺失、
            //       池区间越界……任何一条），异常会**一路穿出 `FixedTick`** ⇒
            //     `GameBootstrap.Update` 里那句 `_clock.Advance(...)` 直接中断 ⇒
            //     这一帧的 `World.Simulate` 不跑 ⇒ **`PlayerActor.Tick` 不跑** ⇒
            //     `WeaponCds` / `SwitchWindup` 不再衰减 ⇒ `CanAttack` 恒假 ⇒
            //     **锄头 / 肥料永久出不了手**（`Actors.cs:671`）。
            //   而水壶的持续激光走的是 `HandlePlayer` 里**在 `CanAttack` 之外**的那个分支
            //   ⇒ 水壶照样能打 —— 这正是用户报的"卡住，但是使用了水壶可以攻击" ✓
            //   而"攻击敌人触发印记后恢复"也对得上：那条弹字只在 `PluginField.ConsumeMark`
            //   真的消耗到一枚印时才发（`Actors.cs` 的 `markMul != 1f` 分支）⇒ 印被消耗后
            //   这条路不再走、异常消失、下一帧 `World.Simulate` 恢复 ✓
            //
            // ── 口径（不是"吞掉异常"）──
            //   · 只包**表现层**这一句；`Popups.Add(ft)` 在 try 外面 ⇒ 弹字本身照常上屏 ✓
            //   · 异常**照旧上报**（`Log.Error` 带上下文），只是不再让它穿出逻辑帧 ✓
            //   · 每局只报第一次（`_pluginFxFaulted`）：一个每帧都抛的特效会把控制台刷满，
            //     反而把真正的那一条埋掉 ✓
            //   · 逻辑层的状态（印记 / 护盾 / 雷）全在 `PluginField` 里，**一个字都不依赖这里** ✓
            try
            {
                PluginHitFx.Apply(ft, e.Pos, this, Popups);
            }
            catch (System.Exception ex)
            {
                if (!_pluginFxFaulted)
                {
                    _pluginFxFaulted = true;
                    Log.Error($"[PluginHitFx] 命中特效/弹字变体抛异常（已隔离，逻辑帧继续）：" +
                              $"text=\"{ft.Text}\" kind={ft.Kind} pos=({e.Pos.x:0.00},{e.Pos.y:0.00})｜{ex}");
                }
            }
            Popups.Add(ft);
            // ★★ v282：富豪「金币转伤害」—— **补一条金币弹字，打在挨打的那个敌人身上** ★★
            //
            // ── 判据（为什么是这两个 kind）──
            //   只有 `DamageToEnemy` / `Crit` 是"**这一下武器打中了它**"的弹字：
            //   它们由 `Actor.OnHitLanded` 在 `World.DealDamage` 的栈里发出，
            //   而 `e.Pos` 就是**那个敌人自己的坐标**（`Actor.Popup` 里 `Pos = Pos`）✓
            //   ⇒ 这正是用户要的"敌人身上冒出一个金币样式图案加伤害" ✓
            //   ✗ 明确排除：`DamageBurn/Slow/Poison`（持续伤害每秒一跳，不是"这一下"）、
            //     `DamageToPlayer*`（那是"我"在掉血）、`Heal` / `Evade` / 收益类 ✓
            // ── 为什么要消费掉（`= 0`）──
            //   这笔账属于"一次出手"。不消费的话，之后**任何**一次敌人受击都会
            //   再补一条金币弹字 ⇒ 一次出手刷出一串硬币 ✗
            if (_goldBonusPending > 0f &&
                (e.Kind == PopupKind.DamageToEnemy || e.Kind == PopupKind.Crit))
            {
                float extra = _goldBonusPending;
                _goldBonusPending = 0f;
                EmitGoldBonusPopup(ft.Pos, extra);
            }
        }

        /// <summary>
        /// ★★ v282：富豪的**金币弹字**（硬币图标 + 纯数字 + 金色，打在敌人身上）★★
        ///
        /// ── 为什么在漏斗里 `Popups.Add`，而不是 `Bus.Publish` ✗ ──
        ///   本函数由 `OnFloatingText` 调用，而那条栈**本身就是 `Bus.Publish` 的派发中途**
        ///   （`Bus.Subscribe&lt;FloatingTextEvent&gt;` 的处理器里）。
        ///   在处理器里再 `Publish` 同一个事件类型 = **递归进同一个处理器** ✗
        ///   ⇒ 直接构造 `FloatingText` 加进队列：不走总线、不递归、不多一个订阅 ✓
        ///   （代价：颜色/字号必须从 `PopupStyle` **静态**取而不是由漏斗统一赋 —— 两者都调同一对函数，
        ///     口径没有分家 ✓）
        ///
        /// ── 用户口径（逐条对）──
        ///   · 「金币样式图案」 ⇒ `PopupKind.GoldBonus` → `PopupStyle.IconOf` = `PopupIcon.Coin`（硬币贴图）✓
        ///   · 「加伤害」       ⇒ 文本是**这个额外伤害的数**（`0.##`，与敌人受击弹字同一套格式），
        ///                        **没有** `+`、**没有**「金」字 —— 用户原话要的是"图案加伤害"✓
        ///   · 「金色」         ⇒ `PopupStyle.ColorOf(GoldBonus)` = `(1.00,0.85,0.30)` ✓
        ///   · 「敌人身上」     ⇒ `pos` 传进来的是**那个敌人**的弹字坐标（`ft.Pos`，含既有散布）✓
        ///
        /// ── 为什么不用 `World.VisualRng` 再散一次 ✗ ──
        ///   漏斗已经为那条**受击弹字**抽过一次散布，金币这条**搭它的坐标**即可；
        ///   再抽一次会**多消耗一次表现随机数** ⇒ 之后所有弹字的落点全部偏移，
        ///   等于顺手改掉了别的表现（`ProbePopups` 的抓图会整体错位）✗
        ///   ⇒ 这里用**固定**的对角错位（向右上），既不叠在伤害数字上，也不动 RNG ✓
        /// </summary>
        private void EmitGoldBonusPopup(Vector2 pos, float extra)
        {
            if (extra <= 0f) return;
            if (Popups.Count >= MaxPopups) Popups.RemoveAt(0);
            // 屏幕上的右上方向：Vector2 的两个分量在这里分别是世界 x / z ⇒ 对角挪开一档
            Vector2 off = new Vector2(0.34f, 0.34f);
            Popups.Add(new FloatingText
            {
                Pos = pos + off,
                Text = extra.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                Kind = PopupKind.GoldBonus,
                Color = PopupStyle.ColorOf(PopupKind.GoldBonus),
                Ttl = PopupLifetime,
                Total = PopupLifetime,
                Scale = PopupStyle.ScaleOf(PopupKind.GoldBonus),
                Plugin = PluginPopupStyle.None,     // 它不是插件样式，图标由 kind 分派
            });
        }

        /// <summary>
        /// ★ v281：本局是否已经上报过一次"表现层异常"（只报第一次，理由见 `OnFloatingText`）。
        /// 换局时由 `Reset` 归零 ⇒ 每局都能看到自己那条 ✓
        /// </summary>
        private bool _pluginFxFaulted;

        /// <summary>浮动文字存活时长（秒）。
        /// v69：从 0.9 提到 1.15——新增了"爆开→回落→缩小淡出"三段动画，
        /// 0.9 秒里塞三段会看不清中间那段稳定期。</summary>
        public const float PopupLifetime = 1.15f;

        private void TickPopups(float dt)
        {
            for (int i = Popups.Count - 1; i >= 0; i--)
            {
                Popups[i].Ttl -= dt;
                if (Popups[i].Ttl <= 0f) Popups.RemoveAt(i);
            }
        }
        // ★ T2：`OnPlantSpawned` / `OnPlantDied` 两个遥测处理器**已删除** ✗ ★
        //   它们对应的事件（`PlantSpawnedEvent` / `PlantDiedEvent`）随 `PlantAlly` 一起下线 ⇒
        //   处理器双双成为零调用者 ✓
        //   ⚠️ **遥测口径变了**（要写进埋点说明）✗：
        //     · `plant_spawn`  —— 不再上报（没有"植物实体出生"这件事了 ✓）
        //     · `plant_death`  —— 不再上报
        //     ★ "植物"这个概念的埋点**取而代之**的是地块口径 ★：
        //       `plot_harvest` / `plot_release` / `plot_action`（都在下面 ✓）
        //       以及 `RunSimulator.PlantsAlive`（= `Farm.PlantCount`，一株 = 一列 ✓）

        public void TogglePause()
        {
            if (IsOver) return;
            if (!Paused)
            {
                PhaseBeforePause = Phase;
                Paused = true;
            }
            else
            {
                Paused = false;
            }
        }

        private void SetPhase(PhaseState next, float timer)
        {
            PhaseState from = Phase;
            Phase = next;
            PhaseTimer = timer;

            // ══════════════════════════════════════════════════════════════════════
            // ★ v207：**关闭商店面板 = 商店车立刻开走**（用户原话："我只要关闭商店，
            //    就应该离开"）★
            //
            // ── 为什么接在 `SetPhase` 而不是逐个退出点 ──
            //   "商店面板开着"这件事的真值**就是** `Phase == PhaseState.Shop` ✗
            //   而离开它的入口有五条（全都在本文件里，见下），逐条接线必然漏一条 ✓
            //   ⇒ 本方法**是**这个阶段唯一的写入口 ⇒ 接在这里 = 五条全覆盖 ✓
            //   这与 v189 那条 `Phase == Shop` 时间闸门同一个思路：判据只留一份 ✓
            //
            // ── 现存的全部"离开商店"入口（都已因此覆盖）──
            //   ① `TickShop` 空格离开（`input.Skip`）      → :4946 → ②/③/④
            //   ② `TickShop` 离开按钮（`Hud.PendingNextWave` → `input.Skip`，`ImGuiHud.cs:6881`）→ 同上
            //   ③ `TickShop` 倒计时归零（`PhaseTimer <= 0`）→ 同上
            //   ④ 离店时时钟已跨分：`StartWave(...)` 末尾 `SetPhase(Combat)`（:5280，`StartWave` 内）
            //      未跨分：`else SetPhase(PhaseState.Combat, 0f)`（:4971）
            //   ⑤ 调试/自动化：`SetPhasePublic`（:3484，`DebugApi` / 探针）✓
            //   另外 `Defeat` 也走本方法（战斗中死亡），那时 `from != Shop` ⇒ 不触发 ✓
            //
            // ── 为什么先判 `next != Shop` ──
            //   `SetPhase(Shop)` 自己也是本方法的调用者（进店）✗ 不加这条会把"进店"当成"离店" ✓
            //
            // ── 为什么返回值才播提示 ──
            //   `MarkPlayerLeft` 在"没进过店"（调试直接开店 / 车不在停靠期）时返回 false
            //   ⇒ 那种情况车本来就没动，播"开走了"就是撒谎 ✓
            //
            // ── 与另一条离场触发的关系（v208：**只剩两条**）──
            //   ① `ShopTruck.Tick` 的 `Dock` 到期（停满 `DockSeconds`）✓
            //   ② 本方法（关面板）✓
            //   ✗ v208 已删除"走出交互半径 ⇒ 立刻开走"那条 ✗
            //      （用户口径："不要玩家离开范围车就开走，只有时间到或者关闭商店才开走"）
            //   ⇒ 两条都收敛到 `ShopTruck.Enter(..., Phase.Leaving, ...)`：任一先到，
            //     另一条随后返回 false ⇒ 不会重复 ✓
            //   离场表现（贴地、沿 -X、出界 8m）与时长**完全复用**既有 `Leaving` 阶段，一行没改 ✓
            // ══════════════════════════════════════════════════════════════════════
            if (from == PhaseState.Shop && next != PhaseState.Shop && Truck.MarkPlayerLeft(Clock))
                SetPrompt("商店车开走了");

            Bus.Publish(new PhaseChangedEvent { From = from, To = next, Wave = WaveIndex });
        }

        /// <summary>
        /// **买下一件道具**（v168：三类别）。
        ///
        /// 流程：层数 +1 → 把**全部**道具重新写进属性（幂等）→ 把机制系数写进它们的消费点 ✓
        ///
        /// ★ 为什么是"全部重算"而不是"只加这一件" ★
        /// ① 成长型道具（`growPerWave`）的数值随回合变，只有全量重算才能同时覆盖
        ///    "刚买"与"跨回合变强"两种情况 ✓
        /// ② 全量重算天然幂等 —— 调 1 次和调 100 次结果一样，不可能重复叠加 ✓
        ///    （旧实现是"往 StatBlock 里累加一条"，一旦某次调用被跳过就永久少加、且不报错 ✗）
        /// </summary>
        public void ApplyItem(ItemDef item)
        {
            if (item == null) return;
            ItemStacks.TryGetValue(item.Id, out int n);
            ItemStacks[item.Id] = n + 1;

            ReapplyItems();
            SyncItemRules();

            // ★★ v216：**"改上限"的道具买下之后立刻写一次**（别等下一帧）★★
            //
            // ── 病因（用户报"闪避头盔没有正确修改闪避率上限，仍 60%"）──
            //   改上限的唯一写侧是 `ProgMechanics.ApplyCapsToTable`（→ `caps.dodge = 0.7`），
            //   而它**只在 `ProgMechanics.Tick` 里被调用一次/帧**（`ProgMechanics.cs:344`）。
            //   可 `RunSession.FixedTick` 在 `ProgMechanics.Tick` **之前**就有四道闸门 `return`：
            //     · `Paused`（暂停菜单，见：`if (Paused) return;`）
            //     · `RewardPanelOpen`（抽卡）
            //     · `Phase == Shop`（**买道具就发生在这一阶段**）
            //     · `Phase == Prepare`（开局三选一）
            //   ⇒ 玩家在**商店里**买下这件之后，`caps.dodge` 还是 0.6；
            //     而"属性面板"恰好是在**菜单/商店**里看的 ⇒ **看到的上限永远是 60%** ✗
            //     （菜单期间 `Paused` 为真 ⇒ 那几帧同样不写 ⇒ 这条闸门把面板彻底挡在外面）
            //   ⇒ 症状不是"算式错"，而是"**写侧被帧闸门挡在第 2 帧之后**"✓
            //
            // ── 改法 ──
            //   在**购买的这一刻**同一次调用那个函数。它本来就是幂等的
            //   （每帧调它只写同一个值），所以多这一次不会改变任何数值，
            //   只是把"生效时刻"从"下一帧战斗"提前到"买下的这一帧" ✓
            //   ⚠️ 不另写一份 `caps.dodge = …`：写侧必须**只有一个实现**（否则又是两套真相）✓
            ProgMechanics.ApplyCapsToTable(this);

            World.Farm.SetMaxPlots(Progress.MaxPlots);
            SyncSeedSlots();   // v29：耕地增加 → 种子袋上限同步（1:1）
        }

        /// <summary>
        /// **把全部道具的增益/减益按当前层数与当前回合重新写进属性**。幂等 ✓
        ///
        /// ★ 减益也要真的生效 ★
        /// 类别 2 的设计是"增益大于减益"，而"大于"只有在**两半都真的写进去**时才有意义 ✗
        /// 所以 `Effects` 与 `Costs` 走**同一条**写入路径（`Costs` 的值在表里写成正数，
        /// 这里按减号写）⇒ 不可能出现"表里有牺牲、游戏里没有" ✓
        /// </summary>
        public void ReapplyItems()
        {
            // ① 先按来源逐条撤销（`StatBlock` 只提供精确匹配的删除接口）
            foreach (KeyValuePair<string, int> kv in ItemStacks)
                Progress.Stats.RemoveModifiersFrom("item:" + kv.Key);

            // ② 再按当前层数与回合重新写一遍
            foreach (KeyValuePair<string, int> kv in ItemStacks)
            {
                ItemDef def = TableDatabase.GetItem(kv.Key);
                if (def == null) continue;
                int stacks = Mathf.Max(1, kv.Value);
                string src = "item:" + kv.Key;

                for (int i = 0; i < def.Effects.Length; i++)
                    WriteBonus(def.Effects[i], def.GrowPerWave, stacks, src, +1f);
                for (int i = 0; i < def.Costs.Length; i++)
                    WriteBonus(def.Costs[i], def.GrowPerWave, stacks, src, -1f);
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ★★ v314：道具「天使降临」的**死亡拦截 + 复活** ★★
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 玩家血池见底时**先问这里**：身上还有「天使降临」就复活一次并返回 `true`
        /// （调用方据此**跳过判负**）。没有这件道具 ⇒ 返回 `false`，判定路径一个字都不变。
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// 一、接入点 = `TickCombat` 里那一格 `if (World.Player.Health.IsDead)`
        /// ══════════════════════════════════════════════════════════════════
        /// 它是**全工程唯一**把玩家死写成 `PhaseState.Defeat` 的地方（grep `PhaseState.Defeat`
        /// 逐条核对：另一处是 BOSS 超时，与本道具无关）⇒ 在这里拦 = 一定拦在"游戏结束"之前 ✓
        /// 复活之后玩家的 `Hp > 0` ⇒ 下一帧这一格自然不成立 ⇒ 不需要任何"已复活"标志位 ✓
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// 二、顺序（每一项都对应一句用户原话）
        /// ══════════════════════════════════════════════════════════════════
        ///   ① 消耗 1 件天使、**同一格**变成 1 件羽毛（"依然占用位置"）
        ///   ② `ReapplyItems` ⇒ 羽毛的「最大生命 −3」**当帧**进属性（不是等下一帧）
        ///   ③ 回满生命（用**新的**上限 ⇒ 复活那一帧血条就是满的，不会有一帧"复活了只剩 1 点血"）
        ///   ④ 停帧 2 秒（走 `CombatFeedback.HitStopFor`，**不直接写 `timeScale`**）
        ///   ⑤ 羽毛飘落（`AttackVfx.SpawnAngelFeathers`，对象池、零 Instantiate）
        /// ⚠️ ④⑤ 都是**表现**：即使特效/停帧被跳过（编辑模式、无头自检），复活本身已经生效 ✓
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// 三、为什么"消耗一层"必须**删键**而不是写 0
        /// ══════════════════════════════════════════════════════════════════
        /// `ReapplyItems` 里是 `Mathf.Max(1, kv.Value)` ⇒ 写 0 仍按 **1 层**结算 ✓
        /// 而本函数的入口判据读的是字典里的真实层数 ⇒ 写 0 会被下一次死亡读成"还有" ✗
        /// （`TrainingVault` 里那条"卸下必须删键"的注释是同一个坑）
        /// </summary>
        public bool TryAngelRevive()
        {
            if (World == null || World.Player == null) return false;
            PlayerActor p = World.Player;
            if (!p.Health.IsDead) return false;                       // 只在"确实死了"这一刻生效
            if (!ItemStacks.TryGetValue(ItemIds.AngelDescent, out int have) || have <= 0) return false;

            // ① 消耗 1 件天使（归零就删键，见上面第三节）
            if (have <= 1) ItemStacks.Remove(ItemIds.AngelDescent);
            else ItemStacks[ItemIds.AngelDescent] = have - 1;

            // ①b **同一格**变成 1 件羽毛 —— 键数不变 ⇒ 道具栏那一格不会空出来
            //     （`ImGuiHud` 的道具栏按 `ItemStacks.Keys` 排版，`Count` 就是"占了几个位置"）
            ItemStacks.TryGetValue(ItemIds.FallenAngelFeather, out int fell);
            ItemStacks[ItemIds.FallenAngelFeather] = fell + 1;

            // ② 重算属性（含 `ProgMechanics.ApplyCapsToTable` —— 与 `ApplyItem` 走**同一条**链，
            //    免得"买进来的道具立刻生效、复活换来的要等下一帧"这种两种时序）
            ReapplyItems();
            SyncItemRules();
            ProgMechanics.ApplyCapsToTable(this);
            World.Farm.SetMaxPlots(Progress.MaxPlots);

            // ③ 回满生命（+ 护盾，见 `HealthPool.Setup` 的 `refill` 语义）。
            //    ★ 直接写血池、**不走 `Heal`**：`Heal` 有 `TrainingMode.NoHeal` 守卫，
            //      而"复活"不是"回血"（训练场里也不该把玩家按死两次）✓
            //    ★ 上限取 `Progress.Stats` 的**当前值**（羽毛已生效）⇒ 满血 = 新上限 ✓
            p.Health.Setup(Progress.Stats.Get(StatId.MaxHp), Progress.Stats.Get(StatId.Shield), true);

            // ④ 停帧（表里的 `ruleValue` = 秒数；缺表/写 0 时退回 2 秒）
            ItemDef def = TableDatabase.GetItem(ItemIds.AngelDescent);
            float pause = def != null && def.RuleValue > 0f ? def.RuleValue : 2f;
            CombatFeedback.HitStopFor(pause);

            // ⑤ 羽毛飘落（表现层；池化，运行期零 Instantiate / Destroy）
            AttackVfx.SpawnAngelFeathers(p.Pos, pause);

            // ⑥ 提示 + 埋点（"剩下几次"是玩家最想立刻知道的数）
            int left = ItemStacks.TryGetValue(ItemIds.AngelDescent, out int rest) ? rest : 0;
            SetPrompt($"天使降临：复活（还剩 {left} 次）", 2f);
            Telemetry.Emit("angel_revive", ("wave", WaveIndex), ("left", left),
                ("feathers", ItemStacks[ItemIds.FallenAngelFeather]), ("hp", p.Health.Hp));
            return true;
        }

        /// <summary>
        /// ★★ v216：**道具表里 `Dodge` 用「整数百分点」写** —— `add: 6` 表示 +6%，不是 +6.0 ★★
        ///
        /// ── 为什么必须单开一条换算（不能复用 `IsFractionStat`）──
        ///   `TableDatabase.IsFractionStat` 那条例外只让 `CritRate` / `Luck` 在表里写小数
        ///   （它们写的是 **0..1 单位**：`gamble_glove` 的 `CritRate add 0.099` = +9.9 个百分点）；
        ///   而 `Dodge` **不在**那个例外里 ⇒ 表校验 (`TableDatabase.cs:442`) 只接受整数
        ///   ⇒ 若照 `CritRate` 的写法写 `0.06`，**整张表载入失败**（本工程真的 CRASH 过）✗
        ///   反过来，若照原始单位写 `Add 1`，它加的是 **1.0 = +100 个百分点**
        ///   ⇒ 无论上限是 60 还是 70，都会**一口顶到上限** ⇒ 玩家看到的是「闪避率被赋成 60%」✗
        ///   （v216 之前 `evade_helm` 写的正是 `add: 1`，症状就是这个）
        ///
        /// ── 现行口径 ✓ ──
        ///   `Dodge` 的 `Add` 是**整数百分点**：`6` ⇒ `/100` ⇒ `+0.06` ⇒ 面板 **+6%** ✓
        ///   换算点**只有这一处**（`WriteBonus` 是所有道具增益/减益的唯一写入路径）✓
        ///   ⚠️ 它**不改** `StatBlock` 的单位（`Dodge` 存值仍是 0..1），也不改上限（上限是 `caps.dodge`）✓
        /// </summary>
        private const float DodgePercentPointScale = 100f;

        /// <summary>
        /// 写一条效果。**加减益的唯一写入路径** ✓
        ///
        /// `sign` = +1 增益 / −1 减益。
        /// Add 类：值 = `±(add + 成长) × 层数`（层数直接相加，符合"叠 3 层 = 3 倍"的直觉）✓
        /// Percent 类：值 = `1 ± (add + 成长) × 层数 / 100` —— **合并成一次乘法** ✗
        ///   不能写成 1.15 × 1.15（那是复利，叠 3 层会从 +45% 变成 +52%）✓
        /// ★ v216：Add 类里 `Dodge` 多一步 `/100`（表里写整数百分点），见 `DodgePercentPointScale` ★
        /// </summary>
        private void WriteBonus(ItemBonus b, float growPerWave, int stacks, string source, float sign)
        {
            if (b.IsPercent)
            {
                float pct = (b.Add + growPerWave * Mathf.Max(0, WaveIndex - 1)) * stacks;
                Progress.Stats.AddModifier(new StatModifier(b.Stat, 1f + sign * pct / 100f, ModOp.Mul, source));
            }
            else
            {
                float flat = b.Scaled(growPerWave, WaveIndex) * stacks;
                // ★ v216：`Dodge` 的整数 = 百分点 ⇒ 换到 0..1 单位（详见 `DodgePercentPointScale`）★
                if (b.Stat == StatId.Dodge) flat /= DodgePercentPointScale;
                Progress.Stats.AddModifier(new StatModifier(b.Stat, sign * flat, ModOp.Add, source));
            }
        }

        /// <summary>
        /// **按规则键累加已购道具的机制值**（含成长与层数）。没有解释器的键永远不会被问到这里 ✓
        ///
        /// 例：`ItemRule(ItemRules.RerollDiscount)` = Σ(`ruleValue + growPerWave×(回合−1)`) × 层数。
        /// </summary>
        public float ItemRule(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0f;
            float sum = 0f;
            foreach (KeyValuePair<string, int> kv in ItemStacks)
            {
                ItemDef def = TableDatabase.GetItem(kv.Key);
                if (def == null || !string.Equals(def.Rule, key, System.StringComparison.OrdinalIgnoreCase)) continue;
                sum += (def.RuleValue + def.GrowPerWave * Mathf.Max(0, WaveIndex - 1)) * Mathf.Max(1, kv.Value);
            }
            return sum;
        }

        /// <summary>
        /// **把机制算出的系数写进它们的消费点**。
        ///
        /// 这是"机制拓展"这一类道具的**接线处** —— 三道写入，各对应一条规则：
        ///   `reroll_discount`  → `Shop.RerollDiscount`（`ShopSystem.RerollCost` 读它，HUD 也读它）
        ///   `harvest_gold_pct` → `World.HarvestGoldMult`（`World.HarvestPlot` 读它）
        ///   `harvest_dew`      → `World.HarvestDew`（`World.HarvestPlot` 读它）
        ///
        /// 每次购买、以及**每回合开始时**都要重算一次 —— 后者让"随回合成长"的规则也成立 ✓
        /// </summary>
        public void SyncItemRules()
        {
            if (World == null) return;
            Shop.RerollDiscount = Mathf.Max(0, Mathf.RoundToInt(ItemRule(ItemRules.RerollDiscount)));
            World.HarvestGoldMult = 1f + ItemRule(ItemRules.HarvestGoldPct) / 100f;
            World.HarvestDew = Mathf.Max(0, Mathf.RoundToInt(ItemRule(ItemRules.HarvestDew)));
        }

        public void AddGoldDirect(int amount) => Gold += Mathf.Max(0, amount);

        /// <summary>
        /// ★★ v197「富豪」：**每次攻击把 1% 金币取整转成额外伤害**（唯一实现）★★
        ///
        ///     `extra = min(29.9, Round(Gold × 0.01))`      ← 上限 29.9 是用户追加的要求
        ///     总伤害 = 原伤害 + extra                      ← **只钳 extra 这一项**，不钳总伤
        ///
        /// ★ 隔了多少金币？—— **不扣**（这一条是理解口径，写在这里免得下一个人改错）★
        ///   用户原话"每次攻击使用 1% 金币"，但 `items.json` 的 desc 与 `ProgMechanics.GoldExtraDamage`
        ///   实现的是"**金币 × 1% 作为伤害值**"（`extra = Round(Gold × 0.01)`）✓
        ///   按"不扣钱"实现：`Gold` 在这一行是**只读**的，函数里没有任何 `Gold -=` ✓
        ///   理由：扣 1% 每次攻击会让钱飞速归零，与"钱越多砸得越重"的成长曲线直接矛盾 ✗
        ///   （要改成"真扣钱"只需在这里加一条 `Gold -= …`，但那是**数值改动**，不在本轮）
        ///
        /// ★ 为什么做成"包一层"的辅助函数，而不是在四条武器路径里各写一遍 ★
        ///   四条（锄头 / 水壶激光 / 水球 / 肥料）都在本文件里算 `RawValue`，
        ///   各写一遍 = 四条独立的加数，改成第五条武器时必然漏一处 ✗
        ///   包一层之后，"哪条路吃这件道具"就是"哪条路调了它"——一次 grep 可见 ✓
        ///
        /// ★★ v282：金币弹字**不在这里发**（这是用户报的那个 bug 的修法）★★
        ///   ── 改前 ✗ ──
        ///     `if (p != null) p.PopupGoldBonus(extra);` —— `p` 是 **`PlayerActor`**，
        ///     而 `Actor.PopupGoldBonus` 里写的是 `Pos = Pos` ⇒ 弹字**钉在玩家脚下**，
        ///     文案是 `金+12` ⇒ 玩家看到的是"金币+在我身上" ✓（正是用户报的）
        ///   ── 为什么不能就地改成"打在目标身上" ✗ ──
        ///     这里**没有目标**：它在**出手**那一刻算伤害，
        ///     而"打在谁身上"要到 `World.DealDamage` 才确定：
        ///       · 锄头一弧最多 3 个敌人（`hoe.hits`）
        ///       · 水球 / 激光 / 爆炸各自在**后面的帧**才落地
        ///     ⇒ 在这里发 = 只能瞎猜一个位置（改前就是猜成了玩家自己）✗
        ///   ── 改后 ✓ ──
        ///     这里只把 `extra` **记在账上**（`_goldBonusPending`），
        ///     由唯一的弹字漏斗 `OnFloatingText` 在**第一个真的挨了这一下的敌人**的
        ///     那条受击弹字到来时，在**那个敌人的坐标**上补一条金币弹字 ✓
        ///     消费点见 `EmitGoldBonusPopup` 的注释。
        /// </summary>
        private float WithGoldDamage(PlayerActor p, float damage)
        {
            int stacks = ProgMechanics.ItemPanel.Of(this).GoldDamageStacks;
            if (stacks <= 0) { _goldBonusPending = 0f; return damage; }
            float extra = ProgMechanics.GoldExtraDamage(Gold, stacks);
            if (extra <= 0f) { _goldBonusPending = 0f; return damage; }
            // ★ v282：记在账上，等"真的挨了这一下的那个敌人"的受击弹字来认领
            //   每次调用都**覆写**（不是累加）：一波连击里只有最后一次出手的数是"这一下"的 ✓
            _goldBonusPending = extra;
            return damage + extra;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  ★ v282：富豪「金币转伤害」的弹字账
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 待认领的金币额外伤害（&gt;0 = 上一次出手真的吃到了富豪的加成，还没找到"打中了谁"）。
        /// 由 `WithGoldDamage` 写、由 `OnFloatingText` 消费并清零。
        /// ★ 为什么它是一个数、而不是挂在某个 `DamageRequest` 上 ✗
        ///   `extra` 在**出手**时就已经并进了 `dmg`（见 `WithGoldDamage`），
        ///   结算侧拿到的只是一个总伤害 ⇒ 除了这里"记一笔"没有第二处知道那个差额 ✓
        /// ★ 残留风险（如实记）：万一这一下**一个敌人都没打中**（挥空 / 全被闪避），
        ///   这笔账会留到**下一次**敌人受击时才兑现 ⇒ 金币弹字会晚一次出现。
        ///   它只影响**表现**（伤害早已正确落账），而且下一次出手会覆写它，
        ///   持续的伤害类型（灼烧/中毒跳字）**不会**认领它（见 `OnFloatingText` 的 kind 判据）✓
        /// </summary>
        private float _goldBonusPending;

        /// <summary>v11 升级抽卡：一张卡 = 一个主要属性 + 增量（相同属性会融合成一张）。</summary>
        public sealed class LevelRewardCard
        {
            public RewardStatDef Def;
            public int Amount;      // 已融合的增量（数值相加）
            public int Copies;      // 融合了几张
            public bool IsPercent => Def != null && string.Equals(Def.mode, "Percent", System.StringComparison.OrdinalIgnoreCase);

            /// <summary>
            /// 这张卡的数值是不是**百分点**（显示要带 `%`）。
            ///
            /// ★ v180：除了 `Percent` 模式，**0..1 小数属性**（当前只有 `CritRate`）的 Add 卡
            /// 也是百分点 —— 表里写 `amount 3` = "+3%"，写入时 `/100`（见 `PickRewardCard`）✓
            /// 不加这一条的话，卡片显示成 `暴击率 +3`，玩家读成"加 3 点"，
            /// 而实际加的是 3% ⇒ **显示与效果对不上**，正是这次 bug 的另一半 ✓
            /// </summary>
            public bool IsPercentPoints
            {
                get
                {
                    if (Def == null) return false;
                    if (IsPercent) return true;
                    StatIds.TryParse(Def.id, out StatId s);
                    return TableDatabase.IsFractionStat(s);
                }
            }

            public string Label => Def == null ? "?" : (IsPercentPoints ? $"{Def.name} +{Amount}%" : $"{Def.name} +{Amount}");
        }

        /// <summary>按权重掷"重复模式"：全不同 60% / 两同一异 30% / 全同 10%。</summary>
        private string RollRewardPattern()
        {
            LevelRewardDef cfg = T.levelReward;
            int total = 0;
            for (int i = 0; i < cfg.patterns.Length; i++) total += Mathf.Max(0, cfg.patterns[i].weight);
            if (total <= 0) return "AllDiff";
            int roll = World.Rng.RangeInt(0, total);
            for (int i = 0; i < cfg.patterns.Length; i++)
            {
                roll -= Mathf.Max(0, cfg.patterns[i].weight);
                if (roll < 0) return cfg.patterns[i].kind;
            }
            return "AllDiff";
        }

        /// <summary>抽三张卡，按模式生成相同/不同属性并融合（相同 = 数值相加）。</summary>
        public bool DrawRewardCards()
        {
            LevelRewardDef cfg = T.levelReward;
            if (cfg.stats == null || cfg.stats.Length == 0) return false;
            RewardCards.Clear();
            string pattern = RollRewardPattern();
            int n = cfg.stats.Length;
            var picked = new List<RewardStatDef>();

            if (pattern == "AllSame")
            {
                RewardStatDef one = cfg.stats[World.Rng.RangeInt(0, n)];
                picked.Add(one); picked.Add(one); picked.Add(one);
            }
            else if (pattern == "TwoSame")
            {
                RewardStatDef a = cfg.stats[World.Rng.RangeInt(0, n)];
                RewardStatDef b = a;
                for (int guard = 0; guard < 64 && b == a; guard++) b = cfg.stats[World.Rng.RangeInt(0, n)];
                picked.Add(a); picked.Add(a); picked.Add(b);
            }
            else
            {
                for (int guard = 0; guard < 128 && picked.Count < 3; guard++)
                {
                    RewardStatDef s = cfg.stats[World.Rng.RangeInt(0, n)];
                    if (picked.Contains(s)) continue;
                    picked.Add(s);
                }
                while (picked.Count < 3) picked.Add(cfg.stats[World.Rng.RangeInt(0, n)]);
            }

            for (int i = 0; i < picked.Count; i++)
            {
                LevelRewardCard found = null;
                for (int j = 0; j < RewardCards.Count; j++)
                    if (RewardCards[j].Def.id == picked[i].id) { found = RewardCards[j]; break; }
                if (found == null)
                    RewardCards.Add(new LevelRewardCard { Def = picked[i], Amount = picked[i].amount, Copies = 1 });
                else
                {
                    found.Amount += picked[i].amount;
                    found.Copies++;
                }
            }
            RewardRerollCount = 0;
            RewardSelected = -1;
            Telemetry.Emit("level_reward_draw", ("pattern", pattern), ("cards", RewardCards.Count));
            return true;
        }

        /// <summary>
        /// v13：重抽消耗金币（无限次，每次价格递增）。
        ///
        /// ★ v180：价格 = 基础价 × 2^次数，**倍数封顶 `ShopSystem.RerollMaxMult`(8)** ★
        ///   ⇒ 3 → 6 → 12 → 24 → 24 → 24 …（与商店刷新同一口径、同一个常量 ✓）
        ///   `Max(1, …)` 是既有约定：**不会出现"重抽倒给钱"** ✓（原来这里没有它，为对称补上）
        ///
        /// ★★ v192：**封顶已从倍率上去掉**（用户需求④："抽卡刷新价格被设置了极限，改为无限增长"）★★
        /// 抽卡与商店**仍然共用 `ShopSystem.RerollPrice`** ⇒ 去掉上限是**一处改动、两边同时生效** ✓
        ///   ⇒ 3 → 6 → 12 → 24 → **48 → 96 → 192 → 384 → 768 → 1536 → 3072 → 6144**（第 1 波）
        ///   第 10 波（基础 9）：9 → 18 → 36 → 72 → 144 → 288 → …
        ///
        /// ★ v189：**基础价也随时间涨**（用户需求②："商店和抽卡刷新价格基础值也随时间增加，同一套公式"）★
        /// 改前这里仍是 `基础价 × 2^次数`，而 `rerollBasePrice` 是表里的**常数 3** ⇒
        /// 抽卡重抽一分钟都没涨过（第 10 波还是 3 金），而商店那边已经改成随波次涨了 ✗
        /// 现在**整条式子交给 `ShopSystem.RerollPrice`**（商店与抽卡共用唯一实现）：
        ///   = round( round(base × (1+13%)^(波-1)) × 2^次数 )，下限 1
        /// ⇒ 波次 1 / 5 / 10 / 15 的首次重抽 = 3 / 5 / 9 / 17 金（与商店刷新**逐格相同**）✓
        /// 抽卡没有 `reroll_discount` 这件道具 ⇒ 减免恒传 0 ✓
        /// </summary>
        public int RewardRerollCost =>
            ShopSystem.RerollPrice(T, T.levelReward.rerollBasePrice,
                                   Mathf.Max(1, WaveIndex), RewardRerollCount, 0);

        /// <summary>
        /// 花金币重抽三张卡。
        ///
        /// ★ v180：**这是"抽卡界面刷新价没有按 ×2 增长"的根因修复** ★
        ///
        /// ── 病因（读码可证，见 `DrawRewardCards` 末尾那句 `RewardRerollCount = 0;`）──
        /// 原实现三步：
        ///   `Gold -= cost;` → `RewardRerollCount++;` → `DrawRewardCards();`
        /// 而 `DrawRewardCards()` 的最后一段是 `RewardRerollCount = 0;`（"新的一抽从基础价算"✓）
        /// ⇒ 递增**同一次调用里**就被清零 ⇒ `RewardRerollCount` **恒为 0**
        /// ⇒ `RewardRerollCost` 恒 = 基础价 **3**（玩家看到"永远 3"，一次也没涨过）✗
        /// ⇒ 顺带 `SetPrompt` 里那句"下次 {RewardRerollCost}" 也永远报 3 ✓（同一现象）
        ///
        /// ── 为什么修在这里，而不是去掉 `DrawRewardCards` 里那句清零 ──
        /// 那句清零**本身是对的**：升级、以及"选完卡后的下一抽"，都该从基础价重新算 ✓
        /// 只是**重抽不是新的一抽**。而把清零挪走会动 `DrawRewardCards` 的 4 个调用点
        /// （升级首抽 / 下一抽 / 调试弹卡 / 它自己），全在"刷新价那一段"之外 ✗
        /// ⇒ 在这里把次数**先存后写回**，语义等价、只碰这一段 ✓
        /// </summary>
        public bool RerollRewards()
        {
            int cost = RewardRerollCost;
            if (Gold < cost)
            {
                SetPrompt($"金币不足（重抽需要 {cost}）");
                return false;
            }
            Gold -= cost;
            // ★ v180：先算出本次之后该是第几次，调完 `DrawRewardCards` 再写回 ★
            int times = Mathf.Max(0, RewardRerollCount) + 1;
            DrawRewardCards();              // 里面会把 RewardRerollCount 清 0（见上面的说明 ✓）
            RewardRerollCount = times;
            SetPrompt($"已重抽（花费 {cost}，下次 {RewardRerollCost}）");
            Telemetry.Emit("level_reward_reroll", ("cost", cost), ("count", RewardRerollCount), ("gold_after", Gold));
            return true;
        }

        /// <summary>选一张卡：按卡片内容加主要属性（整数 / 整数%），然后下一抽或进商店。</summary>
        public bool PickRewardCard(int index)
        {
            if (index < 0 || index >= RewardCards.Count) return false;
            LevelRewardCard card = RewardCards[index];
            if (card.Def == null) return false;
            StatIds.TryParse(card.Def.id, out StatId stat);
            // ★ v180 修复：**0..1 小数的属性，卡上的 amount 是"百分点"，必须 /100** ✗
            //
            // 病因（用户报的"没有 100% 加成却 100% 暴击"）：
            //   `tuning.json` 的 `levelReward.stats` 里 `CritRate` 写的是 `amount: 3`
            //   —— 语义是"暴击率 +3%"（卡片标签也显示成 `暴击率 +3`）。
            //   而 `CritRate` 在引擎里是 **0..1 小数**（`items.json` 里同一个属性写 `0.06`）✓
            //   ⇒ 旧代码把 3 直接当**绝对值**加进去：`0 + 3 = 3`
            //   ⇒ 再被 `caps.critRate = 1.0` 钳成 `1.0`
            //   ⇒ **选一张写着 +3 的卡，暴击率变成 100%** ✗（而玩家以为只加了 3%）
            //
            // 修法：判据复用 `TableDatabase.IsFractionStat` —— 与表校验（`Validate`）、
            //   自检（`ITEM-INT`）**同一条判定**，不另写一份 `== StatId.CritRate` ✓
            //   `/100` 之后 `amount 3` ⇒ `+0.03`，与 `items.json` 的 `0.06` 同一套单位 ✓
            //
            // ⚠️ 这张卡的 `mode` 写的是 `Add`（不是 `Percent`）：对 0..1 属性来说，
            //   "Add 百分点"才是唯一能表达"暴击率 +3%"的写法 —— `Percent` 是**乘法**，
            //   基底 0 时 `0 × (1+p)` 恒为 0 ⇒ 卡片会变成死的 ✗（见 `IsFractionStat` 的长注释）
            if (TableDatabase.IsFractionStat(stat))
                Progress.Stats.AddModifier(new StatModifier(stat, card.Amount / 100f, ModOp.Add, "reward:" + card.Def.id));
            else if (card.IsPercent)
                Progress.Stats.AddModifier(new StatModifier(stat, 1f + card.Amount / 100f, ModOp.Mul, "reward:" + card.Def.id));
            else
                Progress.Stats.AddModifier(new StatModifier(stat, card.Amount, ModOp.Add, "reward:" + card.Def.id));
            Telemetry.Emit("level_reward_pick", ("stat", card.Def.id), ("amount", card.Amount), ("copies", card.Copies));
            SetPrompt($"升级奖励：{card.Label}");
            RewardCards.Clear();
            RewardDrawIndex++;
            if (PendingRewardDraws > 0) PendingRewardDraws--;
            if (PendingRewardDraws > 0) DrawRewardCards();
            else EnterCombatAfterReward();
            return true;
        }

        /// <summary>
        /// v165：抽卡结束 → **回到战斗**。
        ///
        /// 旧版走 `EnterShopAfterReward()`（把玩家送进"波间购买"），而计时制没有"波间" ✗
        /// 商店的入口是**商店车**（靠近按交互键），不是抽完卡之后的必经之地 ✓
        /// 所以抽完卡直接回战斗，玩家想去商店就自己去找车。
        /// </summary>
        private void EnterCombatAfterReward() => SetPhase(PhaseState.Combat, 0f);

        /// <summary>供自动化/自检调用：直接切阶段。</summary>
        public void SetPhasePublic(PhaseState phase) => SetPhase(phase, 0f);

        // ★ 2026-10：`EnterShopAfterReward()` 已删除 ✗
        //
        // 它做的事情是「抽卡结束 → 进商店阶段（**波间购买**）」：
        // 把锁定商品塞进 `CarriedOffers` → `Shop.ResetForPhase()` → `Shop.Refresh(this)`
        // → `SetPhase(Shop, T.shop.prepareTime)`。
        //
        // 它变成【0 调用者】的原因：唯一的调用点在 `TickCombat` 那段"波末结算 → 商店/抽卡"里，
        // 而那段在计时制下**永不触发**，已随本次清理整块删除 ✓
        //
        // ★ 删掉它**零行为变化** ★
        //   它的调用点从未执行 ⇒ `CarriedOffers.Add`（本方法 L1966）也从未执行
        //   ⇒ `CarriedOffers` 一直是**空表** ⇒ 本方法的三行副作用一个都没发生过 ✓
        //   而"锁定商品保留"本身不靠它：`Shop.Refresh` 自己就会保留 `Offers[i].Locked` ✓
        //
        // 商店现在的唯一入口是**商店车**（`TickShopTruck` → `Truck.CanShop → SetPhase(Shop)`）✓
        // 抽卡的唯一入口是**升级即弹**（`OnLevelUp` → `DrawRewardCards`）✓

        /// <summary>
        /// v50：**攻击模式**看 attackKind（近战 / 远程），不是 attackType（伤害类型）。
        /// 旧实现拿伤害类型当模式用——`Physical → "近战"`——于是"物理 + 远程"的豌豆射手被显示成"近战"。
        /// 支援型不攻击，单独标注它在队伍里的角色。
        /// </summary>
        public static string AtkTypeText(SeedDef d)
        {
            if (d == null) return "?";
            if (d.AttackType == AttackType.Support)
            {
                switch (d.Support)
                {
                    case SupportMode.Heal: return "治疗";
                    case SupportMode.Guard: return "护卫";
                    default: return "辅助";
                }
            }
            return d.AttackKind == AttackKind.Melee ? "近战" : "远程";
        }

        /// <summary>v50：伤害类型（物理 / 异常 / 支援）——与攻击模式分开显示，两者是正交的。</summary>
        public static string DamageTypeText(SeedDef d)
        {
            if (d == null) return "?";
            switch (d.AttackType)
            {
                case AttackType.Ailment: return "异常";
                case AttackType.Support: return "支援";
                default: return "物理";
            }
        }

        public static string AtkKindText(SeedDef d)
            => d.AttackKind == AttackKind.Melee ? "近战" : "远程";

        /// <summary>
        /// v24：把种子自动种到第一块空地（开局选种 / 买到新种子时调用）。
        ///
        /// ★ v168：带上了**等级与蓄水上限** ★
        /// 改动前无论几级都传 `T.farm.waterMaxDefault`（=100）⇒
        /// `SeedSlot.WaterNeeded`（100/140/180）**只在面板上存在**，田里永远是 100 ✗
        /// 那是"蓄水需求随等级递增"这条设计**根本没接线** ✓
        /// 现在：种下的一刻就把这一级的蓄水上限与生长速度写进地块 ✓
        /// </summary>
        public bool AutoPlantSeed(SeedDef def, int level = SeedSlot.StartingLevel)
        {
            if (def == null || World == null) return false;
            for (int i = 0; i < World.Farm.Slots.Length; i++)
            {
                FarmPlot plot = World.Farm.Slots[i];
                if (plot == null || plot.State != PlotState.Empty) continue;
                plot.BeginPlant(T.farm, def, SeedSlot.WaterMaxFor(def, level), def.WaterDrain, level);
                SetPrompt($"{def.Name}（{level} 级）已自动种下，等待成熟后用锄头收获");
                Telemetry.Emit("auto_plant", ("seed", def.Id), ("plot", plot.Index), ("level", level));
                return true;
            }
            return false;
        }

        /// <summary>按种子袋槽位种下（等级与蓄水上限都取槽位的）。</summary>
        public bool AutoPlantSeed(SeedSlot slot)
            => slot != null && !slot.IsEmpty && AutoPlantSeed(slot.Def, slot.Level);

        // ════════════════════════════════════════════════════════════════
        // ★★ v215：**战斗模式换种**（用户原话："战斗模式下的植物存在此 bug，
        //            没有同步更新，更换了种子都没改变耕地"）★★
        //
        // ── 改前的事实（取证见探针 `ProbeV215SeedSwap` 的"入口"一节）──
        //   `Bag.Selected`（= "我选中了哪颗种子"）在**人类输入里根本没有写入口**：
        //     · `input.SelectSeed` 唯一被赋正值的地方是 `SelfCheck`（单测）✗
        //     · `input.SlotClicked` 的来源 `ImGuiHud.PendingSlotClick` 全工程从未被赋值 ✗
        //   ⇒ `HandleBagClick` / `TickShop` 里那两行 `Bag.Selected = …` 都是死路 ✓
        //   而"种下去"只有两条活路：开局三选一、商店买种子（`AutoPlantSeed` 找第一条空列）✗
        //   ⇒ **"在同一块地上换另一个种子"这件事从来没有存在过** ——
        //     玩家换了种子，田里当然不会变（它读的是 `FarmPlot.CropSeedId`，没有任何写者）✓
        //
        // ── 现在的语义（三条，与判据一一对应）──
        //   ① 这一列从 A 变成 B（`CropSeedId` / `CropDef` / 外观 / 收割全部跟着走）
        //   ② A 退回种子袋（**+1** 个槽位）—— 换种不该吞掉旧种子
        //   ③ B 从种子袋里扣掉（**−1** 个槽位）—— 它已经长在地里了
        //   净变化 = 0 ⇒ 换种不消耗、也不凭空生种子 ✓
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v215：换种的**入口实现** —— 把 <paramref name="plot"/> 换成"种子袋里下一个与它不同的种子"。
        ///
        /// 为什么是"下一个"而不是"当前选中格"：`Bag.Selected` 在人类输入里没有写入口
        /// （见上面那段取证）⇒ 拿它当换种目标，实际上永远是第 0 格，
        /// 玩家会觉得"按了键什么都没变" ✗
        /// 从"当前选中格的下一格"开始绕一圈找**第一个不同的种子**，则：
        ///   · 不需要任何新的选种 UI ✓
        ///   · 连按 Q 就在自己的几颗种子之间轮换 ✓（每次换完 `Bag.Selected` 停在刚换出的那一格）
        /// </summary>
        public bool TryReplaceSeedOnPlot(FarmPlot plot, out string reason)
        {
            reason = null;
            if (World == null || World.Farm == null || Bag == null) { reason = "换种：没有耕地"; return false; }
            if (plot == null) { reason = "换种：站到一块耕地旁边再按 Q"; return false; }
            FarmPlot head = plot.IsColumnHead ? plot : World.Farm.ColumnHead(plot.Column);
            if (head == null || string.IsNullOrEmpty(head.CropSeedId) || head.CropDef == null)
            {
                reason = "换种：这块地上还没有作物（换种只换已有作物，不负责首次播种）";
                return false;
            }

            int n = Bag.SlotCount;
            if (n <= 0) { reason = "换种：种子袋是空的"; return false; }
            int start = Mathf.Clamp(Bag.Selected, 0, n - 1);
            for (int k = 1; k <= n; k++)
            {
                SeedSlot slot = Bag[(start + k) % n];
                if (slot == null || slot.IsEmpty || slot.Def == null) continue;
                if (slot.Def.Id == head.CropSeedId) continue;      // 同一种不必换
                return ReplaceSeedOnPlot(head, slot, out reason);
            }
            reason = $"换种：种子袋里没有别的种子（这块地种的是 {head.CropDef.Name}）";
            return false;
        }

        /// <summary>
        /// ★★ v215：把 <paramref name="plot"/> 所在列上的作物 <paramref name="slot"/> 换成新种子 ★★
        ///
        /// ── 四步，顺序不能乱 ──
        ///   ① 记下旧作物（品种 / 等级 / 变异）—— 铲掉之后就只剩这里还记得
        ///   ② 铲掉整列（一列 = 一株，`FarmPlot.RemoveGrowing` 只管 Growing，成熟态会漏 ⇒ 逐格复位）
        ///   ③ 旧种子**就地**写回被扣掉的那个槽位（⇒ 槽位总数守恒，不需要空闲槽）
        ///   ④ 种下新种子，并把这一列**与种子袋解绑**（`DetachColumnSeed`，理由见 `FarmPlot.DetachSeedDriver`）
        ///
        /// ⚠️ ③ 必须在 ④ 之前："B 扣掉"的动作就是"把 A 写进 B 的那个槽位" ——
        ///    两件事是同一次写入，因此**不可能出现"扣了 B 但没还 A"**这种半截状态 ✓
        /// </summary>
        public bool ReplaceSeedOnPlot(FarmPlot plot, SeedSlot slot, out string reason)
        {
            reason = null;
            if (World == null || World.Farm == null || Bag == null) { reason = "换种：没有耕地"; return false; }
            if (plot == null) { reason = "换种：站到一块耕地旁边再按 Q"; return false; }
            if (slot == null || slot.IsEmpty || slot.Def == null) { reason = "换种：先选一颗种子"; return false; }
            int slotIndex = Bag.IndexOf(slot);
            if (slotIndex < 0) { reason = "换种：这颗种子不在种子袋里"; return false; }

            FarmGrid grid = World.Farm;
            FarmPlot head = plot.IsColumnHead ? plot : grid.ColumnHead(plot.Column);
            if (head == null) { reason = "换种：这块地的列首不存在"; return false; }
            if (string.IsNullOrEmpty(head.CropSeedId) || head.CropDef == null)
            {
                reason = "换种：这块地上还没有作物（换种只换已有作物，不负责首次播种）";
                return false;
            }
            if (head.CropSeedId == slot.Def.Id) { reason = $"换种：这块地上已经是 {slot.Def.Name}"; return false; }

            // ① 旧作物（铲掉之前唯一的记录点）＋ 新种子（③ 会把槽位覆盖成旧种子 ⇒ 必须先取）
            SeedDef oldDef = head.CropDef;
            string oldId = head.CropSeedId;
            string oldMutation = head.MutationId;
            int oldLevel = head.Level;
            int oldQuality = oldDef.RarityTier;

            // ⚠️ 变异只从**旧**那边读：`FarmPlot.BeginPlant` 每次种下都把 `MutationId` 写成 null
            //    （见那里），所以地里的作物从来不带变异 ⇒ 新种子这一侧没有可恢复的变异可读 ✓
            SeedDef newDef = slot.Def;
            int newLevel = slot.Level;

            // ② 整列铲回未开垦（列首自己也复位）
            grid.ClearColumn(head);
            head.ResetToEmpty();

            // ③ 旧种子退回：直接占用被扣掉的那个槽位 ⇒ 种子总数守恒（A+1 / B−1，净变化 0）
            slot.Def = oldDef;
            slot.MutationId = oldMutation;
            slot.Quality = oldQuality;
            slot.Level = oldLevel;

            // ④ 种下新种子（列首种下 ⇒ `BeginPlant` 自动把整列补齐，等级/蓄水上限一起写进去）
            head.BeginPlant(T.farm, newDef, SeedSlot.WaterMaxFor(newDef, newLevel), newDef.WaterDrain, newLevel);
            // ⑤ 这一列从此由**地块自己**持有（新种子的槽位已经被 A 占回去了，
            //    若还挂着 `SeedId = 新种子`，下次 `SyncPlotsToSeeds` 会把它当多余地块回收 ✗）
            grid.DetachColumnSeed(head);

            // UI：选中格停在"刚换出来的那颗旧种子"上 ⇒ 再按一次 Q 就换到下一颗 ✓
            Bag.Selected = Mathf.Clamp(slotIndex, 0, Bag.SlotCount - 1);
            SyncOwnedSeeds();
            SetPrompt($"换种：{oldDef.Name} → {newDef.Name}（{newLevel} 级）｜{oldDef.Name} 已退回种子袋", 1.6f);
            Telemetry.Emit("plot_reseed", ("plot", head.Index), ("from", oldId), ("to", newDef.Id),
                ("level", newLevel), ("old_level", oldLevel), ("slot", slotIndex),
                ("rows", Mathf.Max(1, grid.Rows)));
            return true;
        }

        /// <summary>v23：雨滴 → 水壶 +20；小猪 → 肥料 +1。</summary>
        public void AddWaterAmmo(int amount)
        {
            WaterAmmo = Mathf.Clamp(WaterAmmo + amount, 0, WaterAmmoCap);
        }

        public void AddFertAmmo(int amount)
        {
            FertAmmo = Mathf.Clamp(FertAmmo + amount, 0, FertAmmoMax);
        }

        public void SetPrompt(string text, float hold = 1.2f)
        {
            Prompt = text;
            _promptHold = hold;
        }

        // ------------------------------------------------------------ 主循环

        /// <summary>
        /// 调试用：立刻弹出升级抽卡界面。
        /// 正常路径是"波次结束 → 有未结算升级 → 抽卡"，这条捷径让验收发牌动画不必等机器人自然升级。
        /// </summary>
        public void DebugShowReward()
        {
            RewardDrawTotal = RewardDrawIndex + 1;
            DrawRewardCards();
            SetPhase(PhaseState.LevelReward, 0f);
            Telemetry.Emit("level_reward_start", ("draws", 1), ("debug", true));
        }

        /// <summary>
        /// 调试用：把六种浮动文字一次性放出来，用来核对配色 / 字号 / 叹号规则。
        /// 逐个在实战里等事件太慢，而且很难同一张图里凑齐。
        /// </summary>
        public void DebugPopupDemo()
        {
            if (World == null || World.Player == null) return;
            Vector2 p = World.Player.Pos;
            // 扇形铺开，避免叠在一起看不清
            Demo(PopupKind.DamageToEnemy, "-12", p, new Vector2(-1.6f, 0.9f));
            Demo(PopupKind.DamageToPlayer, "-7", p, new Vector2(-0.8f, 0.9f));
            Demo(PopupKind.Heal, "+5", p, new Vector2(0f, 0.9f));
            Demo(PopupKind.PlantMature, "成熟", p, new Vector2(0.8f, 0.9f));
            Demo(PopupKind.PlantThirst, "缺水", p, new Vector2(-1.2f, -0.6f));
            Demo(PopupKind.Crit, "-31", p, new Vector2(0f, -0.6f));
            Demo(PopupKind.Bomb, "bomb！！！", p, new Vector2(1.2f, -0.6f));
        }

        private void Demo(PopupKind kind, string text, Vector2 at, Vector2 off)
        {
            Bus.Publish(new FloatingTextEvent { Pos = at + off, Text = text, Kind = kind });
        }

        /// <summary>调试用：立刻放一次近战挥击特效（验收挥击弧）。</summary>
        public void DebugSwing()
        {
            if (World == null || World.Player == null) return;
            PlayerActor p = World.Player;
            // ★ v205：调试挥击发的是**视觉几何**，且与真实挥击**同一条式子** ★
            //   改前写死 `Range = 3.2f, ArcDeg = 110f` —— 那是旧射程与"网格自身张角"，
            //   与现在的 `range 3.8 / arc 100` 对不上 ⇒ 截图验收量的是**一个不存在的弧** ✗
            float range = T.tools.hoe.range * Progress.Stats.Get(StatId.AtkRange);
            // ★ v304：调试挥击也读**真实的巨型标记**（与上面 `Fx` 读真实插件同一条纪律）——
            //   否则"装了 hoe_giant 之后截图验收环形特效"这件事没有入口（只能靠实战抖动镜头）
            WeaponModifiers dbgPm = ModsFor(WeaponSlot.Hoe);
            Bus.Publish(new MeleeSwingEvent
            {
                Pos = p.Pos,
                Dir = p.AimDir.sqrMagnitude > 0.001f ? p.AimDir : new Vector2(0f, 1f),
                Range = MeleeFxRadius(range),          // = 判定半径 × 0.95 ⇒ 3.729m（v273）
                ArcDeg = MeleeFxArcDeg(T.tools.hoe.arc), // = 判定张角 − 4° ⇒ 96°
                // v176：调试挥击也读**当前真实插件** ⇒ 截图对比时"装了什么"直接看得见
                Fx = PluginFx.For(dbgPm, WeaponSlot.Hoe),
                FullRing = dbgPm.GiantRing
            });
        }

        /// <summary>
        /// 自检专用：**已排进队列、还没落下的刀数**（双击的第二刀）。
        /// 给"第二刀延迟 0.12s"这条断言一个可读的数（不是靠肉眼看画面）✓
        /// </summary>
        public int PendingSwingCount => _pendingSwings.Count;

        /// <summary>
        /// v181 探针专用：**已布置、还没引爆**的肥料数（前摇 0.5 秒内）。
        /// 给"充能在哪一帧掉、伤害在哪一帧结算"一个可读的数（不是靠肉眼看画面）✓
        /// </summary>
        public int PendingBlastCount => _pendingBlasts.Count;

        /// <summary>
        /// v181 探针专用：**只投掷、不推进前摇** —— 用来逐帧观察一发肥料的全过程。
        /// 与 <see cref="DebugAttackOnce"/> 的肥料分支**唯一**的差别就是最后那一句
        /// `TickPendingBlasts` 不调用（它会就地引爆，逐帧就看不到了）✓
        /// 走的是玩家按键时同一个 `UseTool` ⇒ 扣充能的时机与实战逐字一致 ✓
        /// </summary>
        public void DebugThrowOnly()
        {
            if (World == null || World.Player == null) return;
            PlayerActor p = World.Player;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            UseTool(p, T.tools.fertilizer, true);
        }

        /// <summary>
        /// 自检专用：把"排队的攻击"推进 <paramref name="seconds"/> 秒
        /// （双击的第二刀 + 肥料的前摇爆炸 + **在飞的剑气**）。
        /// 与 `DebugAttackOnce` 配套：一次攻击 = 发起 + 推进到全部落完 ✓
        ///
        /// ★ v185：剑气变成**跨帧存活**的东西之后，这里必须推进它 ★
        /// 否则 `DebugAttackOnce` 只是"把剑气发射出去"，一发都还没飞 ⇒
        /// 探针读到的永远是 0 伤害，而那不是"剑气无效"，是**根本没让它飞** ✗
        /// 走的是 `TickSwordWaves` 同一个函数（内部按 1/60 切子步）⇒
        /// 自检里的判定序列与实时游戏**逐帧一致** ✓
        /// </summary>
        public void DebugAdvanceAttack(float seconds)
        {
            if (World == null) return;
            TickPendingSwings(seconds);
            TickMeleeWindow(seconds);  // ★ v211：判定窗口也要推完（否则探针量不到"窗口的余生"）★
            TickPendingBlasts(seconds);
            TickSwordWaves(seconds);   // v185：在飞的剑气也要推完
        }

        /// <summary>
        /// v171：自动化/自检专用 —— **立刻走一次指定武器的完整攻击结算**
        /// （不经冷却、不检查弹药，其余全按玩家按键时那条路走）。
        ///
        /// ★ 它**不是**第四条攻击路径 ★
        /// 里面调的就是玩家按键时走的那三段判定：
        /// 锄头 → `SwingHoe`；水壶 → `FireBeam`；肥料 → `UseTool` + `TickPendingBlasts` ✓
        ///
        /// 存在的理由：无头自检"按不下鼠标"，而这三段判定又必须能被逐个断言 ✗
        /// 若为了测试另抄一份判定，"测的"与"玩的"就分了家 ——
        /// 本文件刚刚才因为这个问题删掉过一段**从未执行**的激光副本（见 `UseTool` 的注释）✓
        ///
        /// ⚠️ 阶段 3 起它**只管"发起"**：双击的第二刀与肥料的爆炸都要靠
        /// `DebugAdvanceAttack` 推过去 —— 否则"延迟 0.12s"这条设计在自检里会变成"没有第二刀"✓
        /// </summary>
        public void DebugAttackOnce(WeaponSlot slot)
        {
            if (World == null || World.Player == null) return;
            PlayerActor p = World.Player;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            switch (slot)
            {
                case WeaponSlot.Hoe:
                    SwingHoe(p, T);
                    break;
                case WeaponSlot.Can:
                    FireBeam(p);
                    break;
                case WeaponSlot.Fertilizer:
                    // 走**真正的投掷入口**（爆炸点 +N 的展开在它里面），再把前摇推完 ⇒ 真炸
                    FertAmmo = Mathf.Max(FertAmmo, 1);
                    UseTool(p, T.tools.fertilizer, true);
                    // ★ v310：这里必须与 `ArmBlast` 选**同一个**引信 —— 装黑洞时引信是 1.02s，
                    //   照旧灌 0.51s 的话这一次调试投掷会**留在队列里不炸**（漩涡开了、没有爆炸）✗
                    TickPendingBlasts(BlastFuseFor(ModsFor(WeaponSlot.Fertilizer)) + 0.01f);
                    break;
            }
        }

        /// <summary>
        /// v53：**帧级一次性输入**——这些是"相对增量"或"带花费的脉冲"，天生不能重放。
        /// GameClock 为追帧会在一个渲染帧里跑最多 maxCatchUpTicks(5) 次 FixedTick，并把同一个
        /// FrameInput 原样传进去；放进固定步处理会重复生效：
        ///   · 滚轮 +1 × 5 次 = 轮转 5 步，而只有 3 把武器 → 跳过中间那把（玩家反馈的"乱跳/跳过中间"）
        ///   · 刷新商店 × 5 次 = 被扣 5 次金币
        /// 所以它们必须在帧级被消费一次后立刻清零，固定步循环里只会看到 0 / false。
        /// 调用点：GameBootstrap.Update（真实帧循环）与 RunSimulator.Run（无头回放），都在 FixedTick 之前。
        /// </summary>
        public void ConsumeFrameEdges(ref FrameInput input)
        {
            // ★ v189：**短按要留闩**（用户："短按单点时…什么都没只有音效 / 点了没反应"）★
            //
            // ── 病因（两条，实测都能复现）──
            //   ① **渲染帧与固定步不是一一对应**：`GameBootstrap.Update` 是
            //      `input.Fire = isPressed` → `ConsumeFrameEdges` → `GameClock.Advance(Time.deltaTime, 5, dt => FixedTick(dt, input))`
            //      而 `Advance` 在累加器 `< FixedDt` 时**一个固定步都不跑** ✗
            //      ⇒ 120fps 下单帧点按（按下与松开落在同一个渲染帧）**一次固定步都遇不到它** ⇒ 点击整个消失
            //      实测（`DebugApi.ProbeTap` 的 120fps 行）：动画=无｜伤害=无 ⇒ "这次点击整个被吞掉了"
            //   ② **冷却把输入吃掉**：`Fire` 只是**电平**，冷却没到时 `CanAttack` 为假，
            //      这一次按下就被丢弃；玩家看到的是"点了没反应，得再点一次"✗
            //      实测：间隔 500ms 连点、锄头冷却 700ms ⇒ 20 次里 **10 次被吞**
            //
            // ── 修法 ──
            //   在**帧级**（本方法每渲染帧只跑一次）记一个"按下沿"，并给 0.25 秒的兑现窗口：
            //     · 高帧率下"点击落在没有固定步的那一帧" ⇒ 闩留到下一个固定步兑现 ✓
            //     · 冷却只剩 ≤0.25s 时按下 ⇒ 冷却一好就出手，不再整次丢弃 ✓
            //   为什么是 0.25s：它是"人点了一下、愿意等它兑现"的量级，
            //   也远小于最短武器冷却（锄头 0.7s）⇒ **不会把一个按键变成两刀** ✓
            //   ⚠️ 它不改 `Fire` 电平本身：按住仍然是按住（激光的持续判定照旧读 `input.Fire`）✓
            if (input.Fire && !_fireWasDown) _fireTapLatch = FireTapBufferSeconds;
            _fireWasDown = input.Fire;

            // v94：选种页是**锁定页面**（需求："处理 esc 外其它操作锁定"）。
            //
            // 这一处是唯一漏掉阶段判断的帧级入口：以前在选种界面滚轮，
            // 武器会真的切过去、还弹一条"武器：水壶（前摇 …）"的提示——
            // 页面因此看起来"能用"，但当前阶段根本没有战斗，切了也没意义，只让人困惑。
            // 旁边两处（重抽 / 奖励重抽）本来就有 `Phase == …` 判断，只有这里没有。
            //
            // ESC 不受影响：Paused 在 FixedTick 的 switch 之前就短路了。
            // ★★ v199（用户原话："锁定在商店、抽卡、菜单里滚轮旋转武器的操作"）★★
            //
            // 门控**只有一条**：`Phase == Combat` 且没被菜单暂停 ⇒ 滚轮才转武器 ✓
            //   · 商店（`Shop`）／抽卡（`LevelReward`）／菜单（`Paused = true`）／结算 ⇒ **不转** ✓
            //   · 战斗里照常转 ✓
            //   · 选种页（`Prepare`）本来就不转（v94 的门控，继续成立）✓
            // 为什么把"抽卡"和"菜单"一起并进来：它们与选种页是**同一类** —— 玩家眼前是一个列表/对话框，
            //   滚轮在那个语境里的第一语义是"滚列表"（IMGUI 的 `BeginScrollView` 自己会吃滚轮），
            //   而"顺手换了武器"是纯副作用：换完还弹一条"前摇 0.7s 无法攻击"的提示 ✗
            // ⚠️ 本次**只加阶段门控**，不动 `CycleWeapon`、不动 `WheelScroll` 的清零时机 ✓
            if (input.WheelScroll != 0)
            {
                bool canWheelWeapon = Phase == PhaseState.Combat && !Paused
                                      && World != null && World.Player != null && World.Player.Alive;
                if (canWheelWeapon)
                {
                    World.Player.CycleWeapon(input.WheelScroll, World);
                    SetPrompt($"武器：{World.Player.WeaponName}（前摇 {T.tools.switchWindup:0.0}s 无法攻击）");
                    Telemetry.Emit("weapon_switch", ("weapon", World.Player.WeaponName), ("index", World.Player.WeaponIndex));
                }
                input.WheelScroll = 0;   // 无论走不走，增量都要清掉，否则会滞留到下一帧
            }

            if (input.Reroll)
            {
                input.Reroll = false;
                if (Phase == PhaseState.Shop && !Shop.TryReroll(this, out string reason)) SetPrompt(reason);
            }

            if (input.RewardReroll)
            {
                input.RewardReroll = false;
                if (Phase == PhaseState.LevelReward) RerollRewards();
            }

            // v95：**锁定商品**挪到这里（帧级），修掉"锁定按键不灵敏"。
            //
            // 它原来待在 TickShop 与 HandlePlayer 里，也就是**每固定帧**的路径上。
            // 而 OnGUI 每个**渲染帧**只置一次标志，FixedTick 一帧却可能跑多次（追帧），
            // 标志又没人清 —— 于是那一次点击被重复消费：开→关→开…，净效果看着像"没反应"。
            //
            // 这和 v53 修掉的重抽 / 商店刷新 / 滚轮切武器是**同一类 bug**，
            // 那三处当时就挪到了这里，只有锁定漏下了。现在四处一致。
            if (input.OfferLockToggled)
            {
                input.OfferLockToggled = false;          // ★ 必须清，否则追帧时重复消费
                if (input.ToggleOfferLock >= 0 &&
                    (Phase == PhaseState.Shop || Phase == PhaseState.Combat))
                {
                    if (Shop.ToggleLock(input.ToggleOfferLock, this, out string lockReason)
                        && !string.IsNullOrEmpty(lockReason))
                        SetPrompt(lockReason);
                }
            }

            // ★★ v215：**Q = 换种**（战斗模式）★★
            //
            // 为什么落在这里（而不是 `HandlePlayer`）：
            //   ① `HandlePlayer` 跑在**固定帧**上，`FixedTick` 一帧可能跑多次（追帧），
            //      而 Q 是一次性动作 ⇒ 会换多次种（与 v53/v95 修的重抽、锁定是同一类 bug）✓
            //   ② 本方法每**渲染帧**只跑一次，且 `GameBootstrap` 在 `_clock.Advance` **之前**调它 ✓
            // 目标地块在这里自己算（`NearestUnlocked`），不依赖 `HandlePlayer` 留下的 `CurrentPlotIndex` ✓
            // 清标志：无论走不走，脉冲都必须清掉，否则会滞留到追帧的第二个固定步 ✗
            //
            // ══════════════════════════════════════════════════════════════════════
            // ★★ v216：**商店里也要能换种**（用户原话："战斗模式下，换了种子，耕地上的实际
            //    种植更新有延迟，比如我换掉了一个种子，但离开商店时没有更新，而是过了一段时间
            //    才实际作用"）★★
            //
            // ── 病因（这一条与"闪避上限"那件**不是**同一个闸门，别混）──
            //   本方法（`ConsumeFrameEdges`）**不在 `FixedTick` 里**：`GameBootstrap.Update`
            //   先调它（`GameBootstrap.cs:349`）、再 `_clock.Advance(… FixedTick)`（`:352`）
            //   ⇒ `FixedTick` 那四道 `return`（`Paused` / `RewardPanelOpen` / `Shop` / `Prepare`）
            //     **管不到这里** ✓
            //   真正的闸门是**本方法自己**那一句阶段判定：改前写的是
            //       `if (Phase == PhaseState.Combat && !Paused && …)`
            //   而玩家进店就是 `SetPhase(PhaseState.Shop, …)`（`TickShopTruck`，`RunSession.cs:3600`）
            //   ⇒ **在商店里按 Q：脉冲被第 3411 行无条件清掉、`if` 体整段跳过**
            //     ⇒ 这一按**不是"延迟"，是"整个丢掉了"** ✗
            //   玩家看到的"过一段时间才实际作用"＝离开商店回到 `Combat` 之后**又按了一次**才生效 ✓
            //
            // ── 改法（只放宽到"商店"，别的阶段一个字不动）──
            //   `Combat` 与 `Shop` 都算"战斗模式中的一次操作"⇒ 都能换种：
            //     · `Shop` 是**同一个世界的暂停**，不是另一套玩法；耕地就在身后，
            //       玩家在这里调整下一波种什么是完全正常的意图 ✓
            //     · `Prepare`（开局三选一，地里还没有作物）／`LevelReward`（卡片页，
            //       按键属于卡片）／`Paused`（菜单）继续**不换** ——
            //       与上面"滚轮转武器"那条门控（v199）保持同一套口径 ✓
            //   ⚠️ 消费时机一个字没变：仍在**帧级**、仍在 `FixedTick` 之前 ⇒
            //     "按 Q 到 `FarmPlot.CropSeedId` 变化"是 **0 帧**（同一次调用里就写完），
            //     不需要任何 `SyncPlotsToSeeds` 兜底 ✓
            // ══════════════════════════════════════════════════════════════════════
            if (input.ReplaceSeed)
            {
                input.ReplaceSeed = false;
                bool canSwapSeed = (Phase == PhaseState.Combat || Phase == PhaseState.Shop)
                                   && !Paused
                                   && World != null && World.Player != null && World.Farm != null;
                if (canSwapSeed)
                {
                    int plotIdx = World.Farm.NearestUnlocked(World.Player.Pos, T.farm.interactRange);
                    FarmPlot target = plotIdx >= 0 ? World.Farm.Slots[plotIdx] : null;
                    if (!TryReplaceSeedOnPlot(target, out string reseedReason)) SetPrompt(reseedReason);
                }
            }
        }

        // ★ 2026-10：`TimeDrivenWaves` 开关已删除 ★
        //
        // 它曾经是「回合制 → 计时制」的切换开关（`public static bool TimeDrivenWaves = true`），
        // 文档注释里还写着"默认 false，只验证派生正确性"。而计时制现在是**唯一**玩法 ✗
        // 一个恒为 true 的开关不是配置，是**旧回合制的后门**：
        // 把它翻成 false，被删掉的"波末结算 → 商店/抽卡"就会复活 ✓ 所以必须删掉，而不是留着 ✓
        //
        // 唯一的读取点（主循环里 `if (TimeDrivenWaves && ...)`）已去掉开关前缀，
        // 那里现在是**无条件**执行 —— 与删前行为完全一致（删前恒为 true）✓

        /// <summary>
        /// v154：Boss 倒计时文本（给顶部 UI）。空字符串 = 还没有 Boss 计划。
        ///
        /// 用的是已实现并断言过的 <see cref="BossSchedule"/> ✓
        /// 序列 10/8/6/4/2 之后恒为 2 分钟，且**从刷新起计时**（拖不掉）✓
        /// </summary>
        public string BossCountdownText()
        {
            if (_boss == null || !_bossArmed) return "";
            float s = _boss.SecondsUntilNext(Clock);
            return "BOSS " + Clock.CountdownText(s);
        }

        /// <summary>
        /// v157：**商店车状态机**。
        ///
        /// ★ 为什么在 RunSession 而不是 WorldView ★
        /// 我最初把它放在 `WorldView`（因为"车是表现"）✗ 但那是**分层错误**：
        ///   · 四态机、时序、`CanShop` 决定的是【能不能交互、什么时候开店】= 游戏逻辑 ✗
        ///   · 只有 `PointAt` 的采样与 `SetFacing` 才是表现 ✓
        /// 更直接的问题：开店 = `SetPhase(PhaseState.Shop, …)` 是**会话状态** ✗
        /// 状态机在表现层就够不着它 ✓
        ///
        /// **不加"桥"**（让视图每帧把 canShop 写进会话）—— 那会造出第二份真相 ✗
        /// 症状会是"车已经开走了，商店还能开" ✓
        /// 这是本会话第四次遇到同一个病根，所以这次直接修分层，不打补丁 ✓
        /// </summary>
        public readonly ShopTruck Truck = new ShopTruck();
        private bool _truckArmed;

        /// <summary>
        /// v168：**商店车的时序播报开关**（默认关闭）。
        ///
        /// 打开后 `TickShopTruck` 会在"阶段变化"与"每 5 秒"各打一条日志 ✓
        /// 这是我定位"车卡在 Looping"时加的诊断工具 ✓ **它有效，所以保留** ✗
        ///
        /// ★ 为什么默认关闭 ★
        /// `SelfCheck` 会建很多 world 并反复 `FixedTick` ✓
        /// ⇒ 两条日志被触发上千次 ⇒ 一局自检的日志涨到 400KB+ ✗
        /// ⇒ 真正的证据（自检结果）被淹掉 ✓
        ///
        /// **要在编辑器里追车的时序时，把它置 true**（或在调用处临时打开）✓
        /// </summary>
        public static bool LogShopTruck = false;
        private ShopTruck.Phase _lastTruckPhase = ShopTruck.Phase.Idle;
        private float _truckLogAt;

        /// <summary>Boss 计划（v154 接入）。</summary>
        private readonly BossSchedule _boss = new BossSchedule();
        private bool _bossArmed;

        /// <summary>
        /// v157：商店车 —— 推进状态机、处理交互、执行巡回清扫。
        ///
        /// 三件事都在**逻辑层**做，因为它们都要改会话/世界状态 ✗
        /// 表现层只负责"把车摆到该在的位置"（读 `Truck.Current` 与 `PointAt`）✓
        /// </summary>
        private void TickShopTruck(in FrameInput input)
        {
            // ★★ v306 纯插入守卫：训练场里**完全不生成**商店车（用户原话："不要让训练模式出现商店车"）★★
            //
            //  ── 守卫为 false（= 正常战斗）时，本段**不执行** ──
            //    `TrainingMode.NoShopTruck` 的初值是 false，而它**只**由
            //    `TrainingBootstrap.Awake` 里的 `TrainingMode.Enable()` 置真；
            //    `SeedfallArena.unity` 里没有 `TrainingBootstrap`（也没有 `TrainingWarehouse` /
            //    `TrainingVault`）⇒ 战斗模式下这个字段恒为 false
            //    ⇒ 下面这一整段等于 `if (false) { … }` ⇒ 下面每一行原样执行，**逐字节相同** ✓
            //
            //  ── 守卫为 true（训练场）时干了什么 ──
            //    ① 不 `Truck.Reset` / 不 `Truck.Tick` ⇒ `ShopTruck.Current` **永远是 `Phase.Idle`**
            //       ⇒ `WorldView.SyncShopTruck` 里 `visible = (Current != Idle)` 恒假
            //       ⇒ 车模型从第一帧起就是 `SetActive(false)` ⇒ **画面上永远不会出现车** ✓
            //       （它不是"生成了但拦着不动"，是"状态机一次都不推进"）
            //    ② `_shopKeyLatch` 按原语义**读走即清**（与原第 `_shopKeyLatch = false;` 那一行同义），
            //       否则 `RequestShopOpen()` 的闩会一直挂着，回到战斗场景后第一次 Tick 就误开商店 ✗
            //    ③ `return` 之前的语句为 **0 行** ⇒ 与"不 return 让它自然结束"逐字节等价 ✓
            if (TrainingMode.NoShopTruck)
            {
                _shopKeyLatch = false;
                return;
            }
            if (!_truckArmed && !IsOver) { Truck.Reset(Clock); _truckArmed = true; }
            bool truckPhaseChanged = Truck.Tick(Clock);

            // ══════════════════════════════════════════════════════════════════════
            // ★ v208：**离场只剩两个触发**（用户最终口径："不要玩家离开范围车就开走，
            //    只有时间到或者关闭商店才开走"）★
            //
            // ── ① 停满计时 ──
            //   `ShopTruck.Tick` 的 `case Phase.Dock: Enter(…, Leaving, …)`
            //   ⇒ `PhaseElapsed >= DockSeconds`（10 秒）时开走 ✓ 这是**唯一**的计时出口 ✓
            //
            // ── ② 关闭商店面板 ──
            //   `SetPhase` 里 `from == Shop && next != Shop ⇒ Truck.MarkPlayerLeft(Clock)` ✓
            //   （见 `SetPhase` 那段：它是"面板开着"的唯一写入口 ⇒ 五条离店路径全覆盖 ✓）
            //
            // ── ✗ v208 删除：**"走出交互半径 ⇒ 立刻开走"** ✗ ──
            //   v206 曾让"进入过 + 此刻走出半径 ⇒ 立刻 `Leaving`"，本轮按用户口径整段删除 ✓
            //   ⇒ 下面这次 `ReportPlayerPresence` 调用**只做一件事**：在停靠期累计玩家
            //     在半径内的停留时长，够 `InRangeEnterSeconds` 就把 `_playerEntered` 置位 ✓
            //     它是 `MarkPlayerLeft`（关店即走）的前提条件 ⇒ **保留** ✓
            //   ⇒ 方法已改成 **`void`**（原来返回"本帧是否触发离场"），
            //     所以这里**不能也不该**再据此播"商店车开走了" —— 那会是撒谎 ✗
            //     "开走了"的提示现在只由下面 `truckPhaseChanged → Idle` 与 `SetPhase` 两处播 ✓
            //
            // ── 距离判据**不在这里复写** ──
            //   `TruckPlayerInRange()` 内部调引擎自己的 `Truck.CanShop(dist)`（唯一阈值
            //   `ShopTruck.InteractRange`），与本方法下面按 E 进店、`TruckCanShop`（HUD 的 E 提示）
            //   用的是**同一条**算式 ⇒ "提示说能进 / 判定说能进 / 进入过判定"不可能互相矛盾 ✓
            // ══════════════════════════════════════════════════════════════════════
            Truck.ReportPlayerPresence(Clock, TruckPlayerInRange());

            // ★ 商店车到了 / 开走了：播一条提示 ✗ ★
            //
            // 为什么必须播：`ImGuiHud` 里**没有任何**与商店车相关的文案（grep 过 `Truck` / `按 E` = 0 处）✗
            // ⇒ 停靠期玩家看不到"这里可以按 E"⇒ 功能存在但**没人知道** ✓
            // 而这正是用户说的"没有加入 E 键互动"的感受来源之一 ✓
            // 用既有的 `SetPrompt` 通道（不碰 HUD，也就不引入第二份真相）✓
            if (truckPhaseChanged)
            {
                if (Truck.Current == ShopTruck.Phase.Dock)
                    SetPrompt($"商店车到了 —— 走到车下按 E 进店（停留 {ShopTruck.DockSeconds:0} 秒）");
                else if (Truck.Current == ShopTruck.Phase.Idle)
                    SetPrompt("商店车开走了");
            }

            // v168：**车播报改为 opt-in** ✗
            //
            // 原实现（v158/v159）在阶段变化与每 5 秒各打一条 ✓ 它帮我定位过真问题 ✓
            // 但 `SelfCheck` 会建很多 world 并反复 `FixedTick` ⇒ 这两条日志被触发**上千次** ✓
            // 批量跑一局的自检日志因此涨到 **400KB+** ✗ 把真正的证据淹掉了 ✓
            //
            // ⇒ 改成静态开关，**默认关闭** ✗
            //   要在编辑器里追车的时序时，把 `LogShopTruck` 置 true（或在调用处临时打开）✓
            //   ★ 注意：这不是"删掉诊断"，是"让它按需开启" ✓ 诊断能力保留 ✓
            if (LogShopTruck)
            {
                if (_truckArmed && _lastTruckPhase != Truck.Current)
                {
                    _lastTruckPhase = Truck.Current;
                    Vector2 wp = Truck.SamplePoint(Clock) * T.farm.arenaHalf;
                    Debug.Log($"[Seedfall] 商店车 → {Truck.Current}｜{Clock.TimeText()}"
                              + $"｜进度 {Truck.SampleT(Clock):0.00}"
                              + $"｜位置 {wp.x:0.0},{wp.y:0.0}"
                              + $"｜场地半宽 {T.farm.arenaHalf:0.0}");
                }
                if (_truckArmed && Clock.GameTime >= _truckLogAt)
                {
                    _truckLogAt = Clock.GameTime + 5f;
                    Debug.Log($"[Seedfall] 车 {Truck.Current}"
                              + $"｜t={Clock.GameTime:0.0}s"
                              + $"｜已历 {Truck.PhaseElapsed(Clock.GameTime):0.0}s"
                              + $"／时长 {ShopTruck.DurationOf(Truck.Current):0.0}s"
                              + $"｜周期 {ShopTruck.CycleSeconds:0}s");
                }
            }
            else if (_lastTruckPhase != Truck.Current)
            {
                // 关掉日志时仍要维护 `_lastTruckPhase` ✗
                // 否则下次打开开关会**一次吐出全部历史阶段**（那正是它们变化过）✓
                _lastTruckPhase = Truck.Current;
            }

            // ── 交互：停靠期 + 玩家靠近 + 按交互键 → 进入商店阶段 ──
            // 进入 Shop 阶段**本身就等于暂停一切** ✓
            // （FixedTick 的 switch 在该阶段只跑 TickShop，战斗/刷怪/计时都不推进）
            // 所以"暂停所有游戏画面和计时"不需要额外机制 ✓ 这就是单一闸门 ✓
            //
            // ★ 两个入口汇成**一条**判定 ★ —— 真实 E 键（`input.ShopOpen`）
            //   与自动化闩（`RequestShopOpen`，见那里的说明）✓
            //   闩**读走即清**，所以它等价于"这一帧按了一下 E"（`wasPressedThisFrame`）✓
            //   ⇒ 不会出现"自动化一按就每帧重进商店"这种只有机器人会遇到的怪状态 ✓
            bool shopKeyPressed = input.ShopOpen || _shopKeyLatch;
            _shopKeyLatch = false;
            if (Truck.Current == ShopTruck.Phase.Dock && !IsOver && shopKeyPressed
                && World != null && World.Player != null)
            {
                // ★ 必须用 `SamplePoint` 而不是 `PointAt(Truck.PhaseT(...))` ★
                //   停靠期车**停在停靠位**，而 `PointAt(PhaseT)` 会让它又跑一整圈 ⇒
                //   真正的可交互窗只剩最后约 0.5 秒（实测），窗口名存实亡 ✗
                //   （见 `ShopTruck.SamplePoint` 的说明与实测数字 ✓）
                Vector2 np = Truck.SamplePoint(Clock);
                float half = T.farm.arenaHalf;
                Vector2 dockPos = np * half;
                float d = (World.Player.Pos - dockPos).magnitude;
                if (Truck.CanShop(d))                {
                    // ★ v170：**进店前必须填货架** ✗ —— 这是真人可见的 bug ★
                    //
                    // ── 事故记录（"按 E 进店看到一个空货架"）──
                    // 根因：`TickShopTruck` 原来【只调 SetPhase】✗
                    //   而没有 `Shop.ResetForPhase()` 与 `Shop.Refresh(this)` ✓
                    //   而 `Refresh` 是【唯一的货架填充者】（L835 是唯一的 `Offers.Clear()`）✓
                    //
                    // ⇒ 于是 `Offers` 保持空列表 ⇒ 店里【一个货位都没有】✗
                    //   · 玩家按 E 进店 ⇒ 看到空货架 ✓
                    //   · Bot 进店 ⇒ 遍历 0 个货位 ⇒ 买 0 份 ✓
                    //
                    // ★ 为什么它长期没被发现 ★：
                    //   另外几处都会填货架 —— `EnterShopAfterReward`（**已于 2026-10 删除** ✗，
                    //   它是"波间购买"的旧入口、0 调用者）/  `ForceShopForDebug`（调试 ✓）/
                    //   `TryReroll`（重抽 ✓，L1202）
                    //   ⇒ ★ 而"自动那条路"会填 ✓ 所以从那条路进店的测试看不出问题 ★
                    //     （实测证据：Rush 进店 3 次走的是自动那条路 ⇒ 它没暴露这个 bug ✓
                    //       而它目标株数=1 ⇒ 本来就不买 ⇒ 连"能不能买"都没验到 ✓）
                    //
                    //   ⇒ 删掉那个旧入口之后，填货架的**活路径**只剩三条：
                    //     `TickShopTruck`（L2355 唯一正式入口 ✓）/ `ForceShopForDebug`（调试 ✓）
                    //     / `TryReroll`（重抽 ✓）—— 而 `Refresh` 仍是唯一的货架填充者 ✓
                    //
                    // ⇒ ★ 这正是本会话反复出现的形状：**同一个机制有多条入口，
                    //    只有一条接了该接的调用** ★ ✓
                    //
                    // ★ 顺序要求 ★：先 `ResetForPhase()`（清掉上一轮的遗留 ✓）
                    //   再 `Refresh(this)`（按当前分钟重新抽 ✓）
                    //   最后才 `SetPhase`（进店 ✓）
                    //   —— 反过来的话，进店那一帧会看到上一轮的货架 ✓
                    Shop.ResetForPhase();
                    Shop.Refresh(this);
                    // ★ v206：**开过面板 ⇒ 记成"进入过"** ★
                    //   没有这一条，"刚踏进半径就按 E"这种真实操作会漏记：
                    //   `ReportPlayerPresence` 要求**在半径内停留** `InRangeEnterSeconds` 才置位，
                    //   而按 E 是瞬时的 ⇒ 只认它的话，玩家买完东西走开、如果中途没停够 0.5s，
                    //   车就**不会**立刻走（回到"数到 10 秒"那条老路）✗
                    //   ⇒ 两条入口都置位：**停留够** 或 **开过面板** ✓（需求原文用"/"并列的正是这两条）
                    Truck.MarkPlayerEntered();
                    SetPhase(PhaseState.Shop, T.shop.prepareTime);
                }
            }

            // ── 巡回清扫 = ★ v175：**直接击杀** ★ ──
            //
            // 用户新时序原话："5 秒环绕战场走一圈（期间**直接击杀撞到的敌人**）" ✓
            // 规则复用已断言过的 `ShouldSweep`（排除 Boss 与玩家、且只在 Looping 清）✓
            //
            // ★ v175 改的是**怎么死**，不是**谁能死** ★
            //   旧实现：`World.Enemies.RemoveAt(i)` —— 从列表里搬走 ✗
            //     ⇒ 它**不经过死亡路径** ⇒ `World.Enemies[i].Alive` 仍是 true、
            //       `KillCount` 不增、`WorldView` 里已经建好的那个模型**不会被回收**
            //       （视图按 id 增量同步，敌人是"被搬走的"而不是"死掉的"✗）
            //   新实现：`e.Health.Kill()` —— **项目里既有的击杀入口** ✓
            //     （`Combat.Health.Kill()` 只有一行 `Hp = 0f` ✓ 与 `DebugApi.KillAll` 同一入口 ✓）
            //     ⇒ 下一帧由 `World.Cleanup()`（`Actors.cs:1437`）走**标准死亡结算**：
            //       `Alive = false` → `KillCount++` → 掉落判定 → 从列表移除 ✓
            //
            // ★ 为什么不是 `Combat.Resolve(DamageRequest)` ★
            //   那条路会算**伤害公式**（暴击/闪避/异常/护盾）✗ 而"被车撞"没有这些语义 ✓
            //   而且它的 `Target` 只接受 `Actor` 且会发 `EvadeEvent` 之类 ✗ 徒增分支 ✓
            //   `Kill()` 是更直接的"直接击杀"⇒ 正是用户那个词 ✓
            //
            // ⚠️ 已知后果（写明）：走死亡路径 ⇒ **保留该路径本来就有的掉落**
            //   （`Actors.cs:1466` 的爱心掉落率，默认 0 级 = 必不掉 ✓ 玩家堆了"爱心掉落率"才会掉）
            //   而"不掉落经验值"这条**仍然成立** ✓ —— 经验球那条路 v167 已整体删除，
            //   金币/经验的唯一来源是收割（`HarvestPlot`）✓ 两者不冲突 ✓
            if (World == null || Truck.Current != ShopTruck.Phase.Looping) return;
            // ★ 这里在 `Looping` 阶段，`SamplePoint` 与 `PointAt(PhaseT)` **逐位相同** ★
            //   ⇒ 换成统一入口**不改变清扫行为** ✓ 只是不再留下"同一个量有两种取法"的坑 ✓
            // ★ v175：**点检测不需要改成线段扫掠** ✗ —— 这里把判据算清，免得下次有人凭感觉加 ✗
            //   · 车速 = 路径 104.84m ÷ 5s ≈ **20.97 m/s**（半宽 20m，见 SelfCheck 的速度断言 ✓）
            //   · `FixedTick` 步长 0.02s ⇒ 每步位移 **0.42m**
            //   · 判定半径 `r = arenaHalf × 0.16 = **3.2m**`
            //   ⇒ 3.2 ≫ 0.42（**7.6 倍**）⇒ 相邻两步的判定圆重叠极多 ✓
            //     敌人要"漏"必须同时落在两个圆的**外面**✗ 而两圆心相距 0.42m ✗ 不存在这种位置 ✓
            //   （即使按 60fps ≈ 0.35m/帧 或掉帧到 0.8m/帧算，结论不变 ✓）
            //   ⚠️ 这个结论有**前提**：步长不要涨到 `r` 的量级（≈3m ⇒ 0.15s/步）✗
            //      真要那样，这里得改成"上一帧位置→这一帧位置"的线段判定 ✓ 公式是
            //      `点到线段最短距离`（6 行）✓ 现在不加，是因为它**不会更对**，只会多一段代码 ✓
            Vector2 np2 = Truck.SamplePoint(Clock);
            Vector2 truckPos = np2 * T.farm.arenaHalf;
            float r = T.farm.arenaHalf * 0.16f;
            for (int i = World.Enemies.Count - 1; i >= 0; i--)
            {
                EnemyActor e = World.Enemies[i];
                if (e == null) continue;
                bool isBoss = e.Def != null && e.Def.Kind == EnemyKind.Boss;
                if (!Truck.ShouldSweep(isBoss, false)) continue;
                if ((e.Pos - truckPos).sqrMagnitude > r * r) continue;
                // ★ 走既有的死亡入口；**不自己扣血**（不是 `ApplyDamage(9999)`）★
                //   `Kill()` 的语义就是"这一击必定致死"⇒ 高血量敌人也不会出现
                //   "被撞了却没死"的歧义 ✓ 这也正是"直接击杀"要的效果 ✓
                e.Health.Kill();
            }
        }

        /// <summary>v139：上次播报时刻（用 RunTime，避免新增状态）。**初值 -1 = 尚未播报过**。</summary>
        private float _timeLogAt = -1f;

        /// <summary>
        /// v141：**计时制的时间视图**。
        /// 它**不持有时间真值** —— 真值是 `RunTime`，本对象每帧由 `Clock.SyncTo(RunTime)` 同步 ✓
        /// 存在的理由是给 `BossSchedule` / `ShopTruck` 一个稳定的读取接口，
        /// 让它们不必知道 `RunSession` 的内部字段 ✗
        ///
        /// ★ 声明位置就在 `FixedTick` 旁边 ★ —— 前五次"放错类"都是因为按邻近内容猜作用域 ✗
        /// 放在使用它的方法旁边，就不需要猜 ✓
        /// </summary>
        public readonly Clock Clock = new Clock();

        // ── v189：短按输入闩（见 `ConsumeFrameEdges` 的说明）──
        /// <summary>上一次渲染帧左键是不是按下的（用来取"按下沿"，电平本身不动）。</summary>
        private bool _fireWasDown;
        /// <summary>按下沿还能兑现多久（秒）。0 = 没有待兑现的点按。</summary>
        private float _fireTapLatch;
        /// <summary>兑现窗口：0.25s（理由见 `ConsumeFrameEdges` 的"为什么是 0.25s"）。</summary>
        private const float FireTapBufferSeconds = 0.25f;

        /// <summary>
        /// v139：**从既有的 `RunTime` 派生分钟/波次，并每分钟播报一次**。
        ///
        /// ★ 这里没有任何新状态 ★
        /// 分钟与派生波次都是**纯计算**，播报游标也只用 RunTime 本身做比较 ✓
        /// 所以本阶段（只派生、不接管）**不可能影响玩法** ✓
        ///
        /// 播报内容里的 `差` 是**接线正确性的直接指标**：
        ///   - 第一阶段（不接管）：它随时间**越拉越大**，这是预期的 ✓
        ///   - 第二阶段（RUN 接管）：它应当**恒为 0** ✓
        /// 如果第二阶段它还越拉越大，说明有地方没走派生值 ✗
        /// </summary>
        private void TickTimeLog(World w)
        {
            // v142：**第一次调用就播报**（`_timeLogAt < 0` 视为未播报过）。
            //
            // 为什么改：上一版只在 RunTime≥60 时播报，结果跑了两次都没出现，
            // 于是"没被调用"和"时间没涨"这两种可能**分不开** ✗
            // 先让它立刻说一次，就能一次定论 —— 这是"让程序自己播报"该有的样子：
            // **诊断本身要是二值的，不能有多种解释。**
            bool first = _timeLogAt < 0f;
            if (!first && RunTime < _timeLogAt + 60f) return;
            _timeLogAt = RunTime;
            int minute = (int)(RunTime / 60f);          // 与 Clock 的派生规则一致（截断，非四舍五入）
            int derivedWave = minute + 1;
            Debug.Log($"[Seedfall] 计时制{(first ? "（首次·调用链自检）" : "")} "
                      + $"{minute:00}:{(int)(RunTime % 60f):00}｜"
                      + $"分钟 {minute + 1}｜派生波 {derivedWave}｜实际回合 {WaveIndex}｜"
                      + $"差 {derivedWave - WaveIndex}｜敌 {w.Enemies.Count}");
        }

        /// <summary>
        /// v175：**车的世界位置（3D）** —— 表现层的唯一取位入口 ✓
        ///
        /// `WorldView.SyncShopTruck`（摆模型）与 `ImGuiHud`（在车上方写浮动「E」）
        /// 都必须走它 ✗ —— 否则"看得见的车在 A、写着 E 的车在 B"⇒ **界面在撒谎** ✗
        /// （这正是 `ShopTruck.SamplePoint` 注释里记着的那个坑 ✓ 这里把它堵在会话层 ✓）
        /// </summary>
        public Vector3 TruckWorldPos => Truck.SampleWorld(Clock, T.farm.arenaHalf);

        /// <summary>
        /// v175：**玩家此刻能不能真的进店**（= 车在停靠期 ⇒ "战场正上方"，且玩家在
        /// `InteractRange` 内）✓
        ///
        /// ★ 它是"E 提示该不该出现"的**唯一**判据 ★
        /// 里面直接调引擎自己的 `Truck.CanShop(...)` ✗ 不复写那个距离算式 ✗
        /// ⇒ 提示与"按下去真的会发生什么"**不可能不一致** ✓
        ///    （若在 HUD 里另写一遍 `dist <= 6` 就是第二份真相：
        ///     将来改 `InteractRange` 时提示会说谎 ✗ —— 本会话已经踩过同类坑 ✓）
        ///
        /// ⚠️ 它还要求世界与玩家存在 ✓ 车在绕场（`Looping`）/离场（`Leaving`）/不在场（`Idle`）时
        ///    `CanShop` **恒为 false** ⇒ 提示自动不显示 ✓ 不需要额外的阶段判断 ✓
        /// </summary>
        public bool TruckCanShop
        {
            get
            {
                return TruckPlayerInRange();
            }
        }

        /// <summary>
        /// ★ v206：**玩家此刻是否在车的交互半径内**（车不在停靠期 ⇒ 恒为 false）★
        ///
        /// 它是"玩家与车距离"的**唯一**算式 —— `TruckCanShop`（HUD 的 E 提示）与
        /// `TickShopTruck` 里的"进入过 ⇒ 离开即开走"判定**都读它** ✓
        ///
        /// ★ 为什么返回的是"能不能进店"而不是裸距离 ★
        ///   阈值只有 `ShopTruck.InteractRange` 一处，且由引擎自己的 `Truck.CanShop` 判 ✗
        ///   本方法**不写** `dist <= 6` —— 那是第二份真相（改常量时两边会分歧 ✗）
        ///   车上不在 `Dock` 期时 `CanShop` 恒 false ⇒ 调用方不需要额外判阶段 ✓
        /// </summary>
        private bool TruckPlayerInRange()
        {
            if (World == null || World.Player == null || T == null) return false;
            Vector2 truckGround = Truck.SamplePoint(Clock) * T.farm.arenaHalf;
            return Truck.CanShop((World.Player.Pos - truckGround).magnitude);
        }

        public void FixedTick(float dt, in FrameInput input)        {
            TickPopups(dt);   // v47：浮动文字即使在暂停/结算中也自然消失，不卡在屏幕上
            if (IsOver) return;
            if (Paused) return;

            // ★★ v300：训练场 ⇒ 金币每帧补满（用户原话「相当于无限金币」）★★
            //   位置为什么在这里：
            //     · 在 `Paused` 之后 ⇒ 暂停菜单期间不再刷金币（与"世界停摆"同一口径）
            //     · 在三条面板闸门（RewardPanelOpen / Shop / Prepare）**之前** ⇒
            //       商店里的每一次 `Gold -= price`（RunSession.cs:2267/2290）都发生在
            //       本行**之后**，于是"买完还剩多少"永远是 `GoldTopUp − price`，
            //       下一帧又被补满 ⇒ 买不完 ✓（若放在闸门之后，商店那几条早退路径
            //       会绕过补满，玩家买几次就真的没钱了 ✗）
            //   关掉开关时这一行不成立，下面的原语句原样执行 ⇒ 战斗金币逻辑逐字节相同 ✓
            //   ⚠️ 它**只写 `Gold`**：不碰 `Goods` / `Bag` / `World`，也不消费随机数 ✓
            if (TrainingMode.InfiniteGold) Gold = TrainingMode.GoldTopUp;
            //   ⚠️ v301：`TrainingShop` 也有一条自己的 `TopUpGold`（它按"余额不足才补"来写，
            //   与上面这条"每帧写满"等价）。两者**不冲突**（都只把 Gold 抬高到同一量级），
            //   保留两条是刻意的：上面这条保证"面板提前 return 的那些帧也补得上"，
            //   `TrainingShop` 那条在训练场宿主驱动时兜底一次。整场战斗里两条都不成立 ✓

            // ══════════════════════════════════════════════════════════════════════
            // ★ v183：抽卡/升级选卡面板打开 ⇒ **世界时间停摆** ★
            //
            // 用户原话："抽卡进入后时间没有停止，商店车还在运行，导致错过商店车"✓
            //
            // ── 病因（读代码即可复现，不必猜）──
            //   `RunTime += dt` 写在**阶段 switch 之前** ⇒ 面板开着的每一帧，
            //   世界时钟照样往前走 ✓ 而时钟是**一件事的唯一真值**：
            //     · `Truck.SampleWorld(Clock, …)` —— 商店车的位置就是 `Clock` 的函数 ✗
            //       ⇒ 车照跑：停靠窗（10 秒）在面板里也能整段过去 ⇒ 玩家出来时车已经走了 ✓
            //     · `BossSchedule.Due(Clock)` —— Boss 会在选牌期间刷出来
            //     · `Clock.WaveIndex` —— 难度刻度照走
            //   ⚠️ 不是"抽卡专属"的 bug 类别：任何"不该推进世界的界面"都该走这同一条闸门 ✓
            //
            // ── 为什么闸门放在这里，而不是 `Time.timeScale = 0` ✗ ──
            //   本工程**从来不用** `Time.timeScale`（grep 全库 0 处）——
            //   `Paused` 是逻辑层闸门，`if (Paused) return;` 就在上面三行 ✓
            //   照它的做法（逻辑 dt 归零），而不是另起一套"引擎暂停" ✓
            //   ★ 而且这套做法天然把 UI 动画分开 ★：
            //     `ImGuiHud` 的出现/融合动画吃的是 `Time.deltaTime`（v66 起就如此），
            //     它**不受本方法影响** ⇒ 世界停、动画照走 ✓
            //     （若图省事用 `Time.timeScale = 0`，面板里的融合动画会一起冻住 ⇒
            //       `_fuseT` 永远到不了终点 ⇒ 面板卡死 —— 那正是 BUG 1 的形态 ✓）
            //
            // ── 为什么**不逐个字段停** ──
            //   只停 `RunTime`（+ 它派生出来的 `Clock`）就够：车、Boss、难度刻度
            //   全是 `Clock` 的函数 ⇒ 一个闸门管住全部 ✓（"两套真相必然分歧"那条老规矩）
            //
            // ── 为什么**不 return** ──
            //   阶段机必须继续跑：`TickLevelReward` 要靠它消费"确认选卡"的输入，
            //   return 掉就等于把面板焊死在这帧 ✓（`Paused` 那个闸门可以 return，
            //   是因为暂停菜单的"继续"走的是 Esc，不经过阶段机 ✓）
            //
            // ── 停摆范围 ──
            //     RunTime / Clock（世界时钟）＋ 商店车状态机（`TickShopTruck`）
            //   后者一并停，是因为它除了读时钟还**接交互键**：
            //   面板盖在屏幕上时按 E 不该把玩家塞进商店（那会连带丢掉这次升级奖励）✗
            // ══════════════════════════════════════════════════════════════════════
            if (RewardPanelOpen)
            {
                // 世界时钟、商店车、Boss 计时、难度刻度全部不动；
                // 只让**抽卡这一段的阶段机**继续跑，好把选卡输入收进去 ✓
                TickLevelReward(dt, input);
                return;
            }

            // ★ v189：**商店面板打开 ⇒ 世界时间同样停摆** ✗ ★
            //
            // ── 为什么这条必须补上（实测）──
            //   抽卡那条闸门（上面）只认 `Phase == LevelReward` ⇒ **`PhaseState.Shop` 不设防**，
            //   而 `RunTime += dt` 与 `TickShopTruck(input)` 都在阶段 switch **之前**执行 ✓
            //   ⇒ 玩家在店里挑东西时：世界时钟照走 ⇒ 商店车照跑 ⇒ 出来时车已经走完这一圈 ✗
            //   实测（探针读数见交付）：商店阶段停留 1 秒，`Clock.GameTime` +1.000s、车坐标位移 > 1m。
            //
            // ── 这条不是"新设计"，而是**补上早就写在注释里的原意** ──
            //   `Clock` 类上那句 `Paused` 的注释写着"唯一的时间闸门。**商店**/暂停菜单期间置真"，
            //   可全工程 grep `Clock.Paused =` ⇒ **0 处赋值** ⇒ 那句话从没生效过 ✗
            //   现在按它写的语义接上：进店 = 世界停 ✓
            //
            // ── 为什么是"停 RunTime"而不是"停 TickShop" ──
            //   商店自己的倒计时（`PhaseTimer`）与购买/ESC 必须继续跑，否则店就焊死了 ✗
            //   所以照样调 `TickShop(dt, input)`，只是**不推进 `RunTime`** ✓
            //   ⇒ 车、Boss 时刻表、难度刻度全部冻结，而店里的交互一切照旧 ✓
            if (Phase == PhaseState.Shop)
            {
                _promptHold -= dt;
                if (_promptHold <= 0f && PromptProgress <= 0f) Prompt = "";
                TickShop(dt, input);
                return;
            }

            // ★ v200：**开局三选一（`PhaseState.Prepare`）⇒ 世界时间同样停摆** ✗ ★
            //
            // ── 用户原话 ──
            //   "选择初始种子时没有暂停游戏，导致计时器已经开跑，解决问题并检查
            //    菜单，抽卡，商店是否有正确暂停所有"
            //
            // ── 病因（读代码即可复现）──
            //   上面两条闸门分别只认 `RewardPanelOpen`（= `LevelReward`）与 `Phase == Shop`，
            //   而 `RunTime += dt` 写在它们**之后**、阶段 switch **之前** ⇒
            //   `PhaseState.Prepare` **不在任何闸门里** ⇒ 开局三选一期间世界时钟照样走 ✓
            //   实测（改前）：停在三选一界面 1.0 秒，`RunTime` +1.000s、`Clock.GameTime` +1.000s、
            //   顶部计时器从 00:00 涨到 00:01 —— 玩家还没落地，计时器已经开跑 ✓
            //   ⚠️ 而 `tuning.shop.infinitePrepare = true` ⇒ 这个阶段**会一直持续到玩家选完**，
            //     所以它不是"一瞬间的窗口"，而是**整局的第一段** ✓
            //
            // ── 为什么闸门放在这里（而不是 `infinitePrepare` 判断里）──
            //   `infinitePrepare` 管的是"`PhaseTimer` 要不要倒数"（选种**不限时**）✗
            //   它**从来没有**说过"时钟不许走" —— 两件事，别混 ✓
            //   本闸门管的是"世界时间不许走"，与 `infinitePrepare` 正交：
            //     不限时 = 你可以一直看三张卡；停摆 = 看的这段时间世界不动 ✓
            //   所以 `TickPrepare` 里 `if (!infinite) PhaseTimer -= dt;` **一个字不改** ✓
            //   （若将来把 `infinitePrepare` 关掉，倒计时照常读秒 —— 它是 UI 计时，不是世界时间 ✓）
            //
            // ── 为什么照 `Shop` 那段的写法（而不是 `return` 掉整帧）──
            //   阶段机必须继续跑：`TickPrepare` 要靠它消费 `input.InitialChoice`（玩家点的那张卡），
            //   return 掉就等于把三选一界面焊死在这一帧 ✗（与抽卡那条的理由逐字相同 ✓）
            //   所以：**只停世界时间，不停阶段机** ✓
            //
            // ── 停摆范围（一处闸门管全部）──
            //   `RunTime` / `Clock`（世界时钟）＋ 它派生出来的一切：
            //     商店车（`Truck.SampleWorld` / `SamplePoint` 都是 `Clock` 的函数）✓
            //     Boss 时刻表（`_boss.Due(Clock)`）✓
            //     难度刻度（`Clock.WaveIndex`）✓
            //     临时增益与机制运行时（`Buffs.Tick` / `ProgMechanics.Tick`）✓
            //   ⇒ 与 `Shop` / `LevelReward` / `Paused` 三条的停摆范围**完全一致** ✓
            //   ⚠️ 副作用（如实记）：车在开局阶段不再自己跑 ⇒ **开局阶段进不了店**（
            //     停靠期是 `Clock` 的窗口）✓ 这是"世界停摆"的应有之义：
            //     改前那种"边选种子边逛店"恰恰是计时器在走的同一个病因 ✓
            //     开局金币（`player.startGold`）仍在 `Start` 里发放，第一分钟照样买得到东西 ✓
            if (Phase == PhaseState.Prepare)
            {
                // 只让**三选一这一段的阶段机**继续跑，好把选卡输入收进去 ✓
                TickPrepare(dt, input);
                return;
            }

            RunTime += dt;
            Clock.SyncTo(RunTime);      // v141：Clock 是 RunTime 的派生视图，不自己累加（消债）

            // ★ v197：**临时增益与机制运行时的每帧推进**（唯一调用点）★
            //   放在 `RunTime += dt` 之后、阶段 switch 之前：
            //   · 世界时间推进了，增益的"还剩几秒"才该跟着走 ✓
            //   · 商店 / 抽卡面板在前面已经 return（世界停摆）⇒ 增益也停 ✓ 与既有闸门一致 ✓
            Buffs.Tick(dt);
            ProgMechanics.Tick(this, dt);

            // v157：**商店车**（状态机 + 清扫 + 交互）
            TickShopTruck(input);
            if (!_bossArmed && !IsOver) { _boss.Reset(Clock); _bossArmed = true; }
            if (_bossArmed && !IsOver && _boss.Due(Clock))
            {
                // v155：**真正刷出 Boss**（接入既有入口 `SpawnOne`，不另造刷怪路径）✓
                //
                // 既有机制（RunSession:553/599/600）：
                //   SpawnOne(new WaveBatchDef { EnemyId = "boss", Count = 1, ... }, w)
                //   → bossIndex = max(1, WaveIndex / 5)  →  w.SpawnEnemy(...)
                // 我直接复用，所以 Boss 的血量曲线、预警、击退等行为**与旧版一致** ✓
                // 若自己另写一份刷怪，就会像"三处定价"那样多出一套真相 ✗
                // ★ v201：**Boss 种类改由 `BossSchedule.IdOf` 决定** ★
                //
                // 起因：飞蛾 BOSS（v201 新增，三阶段）写好后**真实战局里刷不出来** ——
                //   这里把 EnemyId 硬编码成 "boss"，于是时刻表/种类全对、场上永远是旧 BOSS ✗
                //
                // 语义：`IdOf(n)` = 第 n 次刷新的敌人 id
                //   1 → "boss"（旧版）｜2 → "moth"（飞蛾）｜≥3 → 派生哈希在两者间随机
                // ★ `_boss.SpawnedCount + 1` 是**必须**的：`OnSpawned` 在下一行才自增，
                //   所以此刻 `SpawnedCount` 还是"已刷出的次数"，+1 才是"正在刷的这一只" ✓
                // ★ `IdOf` 是**纯函数**（splitmix32 ＋ 固定盐），不读不写 `World.Rng`
                //   ⇒ 既有随机序列一位都不漂（自检 `BossSchedule 不消费 World.Rng` 钉着）✓
                Waves.SpawnOne(new WaveBatchDef
                {
                    EnemyId = BossSchedule.IdOf(_boss.SpawnedCount + 1),
                    Count = 1,
                    StartAt = 0f,
                    Interval = 1f,
                }, World);

                // **先记录再播报**：OnSpawned 会把倒计时重新锚定到"现在" ✓
                _boss.OnSpawned(Clock);
                Debug.Log($"[Seedfall] ★ BOSS 刷新（第 {_boss.SpawnedCount} 次）"
                          + $"｜时刻 {Clock.TimeText()}｜下次间隔 {BossSchedule.GapMinutesOf(_boss.SpawnedCount)} 分钟");
            }

            // ── **时间接管回合**（唯一路径；v154 起为正式路径，2026-10 删除开关）──
            //
            // 这里曾是 `if (TimeDrivenWaves && ...)` —— 一个"回合制 / 计时制"的切换开关。
            // 开关已删除：计时制是**唯一**玩法，留着开关等于给旧回合制留一扇后门 ✗
            // 去掉 `TimeDrivenWaves &&` 是**零行为变化**（它删前恒为 true）✓
            // 本段与游戏时间**同一帧**推进，所以 WaveIndex 永远等于 Clock 的派生值。
            //
            // ★ 判据是"落后就补"，不是"等于就推进" ★
            // 用 `>` 而不是 `==`：如果某一帧卡顿导致时间跨过两分钟（追帧丢弃时可能发生），
            // `==` 会让那一分钟**永久丢失** ✗ 而 `>` 会补齐 ✓
            //
            // 为什么用 `Clock.WaveIndex` 而不是自己算 `RunTime / 60`：
            // 派生的规则只应有一处 ✓（本会话的口头禅：两套真相必然分歧）
            // ★ v165：加了 `Phase == PhaseState.Combat` 门槛 ★
            //
            // 原因：进商店/抽卡时 `RunTime` **仍在推进**（`RunTime += dt` 在阶段 switch 之前），
            // 于是"分钟刻度"会在商店/抽卡界面里照样跨过去 ⇒ 原来的写法会把玩家
            // **从商店或抽卡界面里踢出来**（SetPhase(Combat)）✗
            // 抽卡现在由升级触发，被踢出去就等于白白丢掉一次升级奖励 ⇒ 必须挡住 ✓
            //
            // 挡住之后 `WaveIndex` 会短暂落后于 `Clock.WaveIndex` —— 这是**设计内的**：
            // 本段的判据本来就是"落后就补"（见上），回到战斗的那一帧会一次补齐到当前分钟 ✓
            // 难度仍然只由**时钟**决定（第 N 分钟 = 第 N 个成长刻度），不受"商店里待了多久"影响 ✓
            if (!IsOver && Phase == PhaseState.Combat && Clock.WaveIndex > WaveIndex)
                StartWave(Clock.WaveIndex);
            // ── v139：计时制接线（第一阶段：只派生、只播报，不接管）──
            //
            // ★ 重要：**没有新增时间源** ★
            // 我原本写了一个 Clock 类来持有 GameTime，但读到这里才发现
            // **RunTime 早就是游戏时间**，而且它已经位于 `if (Paused) return;` 之后 ——
            // 也就是说"暂停时不计时"这个语义**工程里本来就有** ✓
            // 再引入一个并行的时间源只会制造两套真相 ✗（这正是本会话反复出现的病根）
            //
            // 所以这里只做两件事，且都是加法：
            //   ① 从 RunTime **派生**分钟/波次（纯计算，不改任何状态）
            //   ② 每分钟播报一次派生值（可验证它算得对）
            //
            // `WaveIndex` 仍然由既有的"下一波"按钮推进，本阶段**不接管** ✓
            TickTimeLog(w: World);
            _promptHold -= dt;
            if (_promptHold <= 0f && PromptProgress <= 0f) Prompt = "";

            switch (Phase)
            {
                case PhaseState.Prepare: TickPrepare(dt, input); break;
                case PhaseState.Combat: TickCombat(dt, input); break;
                case PhaseState.Shop: TickShop(dt, input); break;
                case PhaseState.LevelReward: TickLevelReward(dt, input); break;
            }
        }

        private void TickPrepare(float dt, in FrameInput input)
        {
            // ★★ v307（训练场专用，**纯插入**）：开局不选植物 —— 空手空袋空田直接进战斗 ★★
            //
            //  用户原话："训练模式修改开局不选择植物，空植物进入"
            //
            //  ── 为什么放在方法首行 ──
            //    下面整段（`InitialSeed = …` / `Bag.AddSeed` / `SyncOwnedSeeds` / `AutoPlantSeed` /
            //    `Telemetry` / `StartWave(1)`）就是"开局三选一 = 白送一颗种子 + 自动种下"的**全部**实现。
            //    在它前面 return 掉，等价于"这一次开局没有三选一这回事" ✓
            //    `StartWave(1)` 仍然是**原来那一句**（同一个入口）：它负责 `SetPhase(PhaseState.Combat)`
            //    （`RunSession.cs:5660`）⇒ 阶段机照常前进，只是没有种子 ✓
            //
            //  ── 战斗模式为什么**逐字节不变** ──
            //    `TrainingMode.EmptyStart` 初值 false，且**只有** `TrainingMode.Enable()` 会把它置真，
            //    而 `Enable()` 的唯一调用点是 `TrainingBootstrap.Awake`（它只存在于 `Training.unity`）
            //    ⇒ 战斗场景里本行等价于 `if (false) { … }` ⇒ 不执行 ⇒ 下面的语句**原样**按原顺序跑 ✓
            //    （连"三选一是否存在""InitialSeed 是否赋值""种子袋里有没有那颗 D 级种子"都一致）
            if (TrainingMode.EmptyStart) { StartWave(1); return; }

            // v4：初始种子选择不限时（只能由玩家自己选定）
            bool infinite = T.shop.infinitePrepare;
            if (!infinite) PhaseTimer -= dt;
            int choice = input.InitialChoice;
            if (choice < 0 && !infinite && input.Skip) choice = 0;
            if (choice < 0 && !infinite && PhaseTimer <= 0f) choice = 0;
            if (choice < 0) return;
            choice = Mathf.Clamp(choice, 0, Mathf.Max(0, InitialChoices.Length - 1));
            InitialSeed = InitialChoices[choice];
            // 初始种子同样带品质（开局三选一的品质也由权重决定）
            InitialQuality = InitialChoiceQuality[Mathf.Clamp(choice, 0, InitialChoiceQuality.Length - 1)];
            Bag.AddSeed(InitialSeed, null, InitialQuality, out _);
            SyncOwnedSeeds();
            // v24：初始种子直接种到土地上（不需要玩家操作）
            AutoPlantSeed(InitialSeed);
            Telemetry.Emit("initial_seed", ("id", InitialSeed.Id), ("quality", InitialQuality));
            StartWave(1);
        }

        private void TickCombat(float dt, in FrameInput input)
        {
            CombatSeconds += dt;

            // v189：短按闩的兑现窗口（只在战斗里流逝 —— 冷却也只在战斗里走，两者必须同一个进度）
            if (_fireTapLatch > 0f) _fireTapLatch = Mathf.Max(0f, _fireTapLatch - dt);

            // v30：每帧对齐「种子袋容量 = 最大耕地数」（UI 格子随之增加）
            if (Bag.SlotCount != Mathf.Max(1, Progress.MaxPlots)) SyncSeedSlots();

            Waves.Tick(dt, World);
            HandlePlayer(dt, input);
            TickBeamCds(dt);       // v90：衰减"同一目标的再受伤间隔"（放在玩家更新之后，本帧刚打中的目标拿到完整 CD）
            TickPendingBlasts(dt); // v91：推进肥料爆炸前摇
            PluginExtra2.Tick(dt); // ★ v290：海浪的移速加成计时器（只递减计时器，不碰判定）
            TickPendingSwings(dt); // 阶段 3：推进双击的第二刀（间隔 = 0.2 ÷ 攻速，见 SwingGapFor）
            TickMeleeWindow(dt);   // ★ v211：推进锄头的**持久判定窗口**（窗口内重扫 + 去重，见该函数）★
            TickSwordWaves(dt);    // v185：推进**在飞的剑气**（子弹类，每帧一次判定）
            TickWaterBalls(dt);    // ★需求④：推进**在飞的水球**（自充，扣水那一下发射）
            Dots.Tick(dt, World, World.Player);   // 阶段 3：持续效果表（灼烧 / 减速 / 毒区）
            PluginMech.Tick(dt, World, World.Player);   // ★ v253：回响（延迟结算）与收束（连击）的跨帧状态
            // ★ v255：伏雷（踩踏/自爆）、灼印（到期）、护壤（护盾到期）
            //   爆炸回调用 `ExplodeWithMult`：把"倍率"交回本类 ⇒ 伤害算式仍然只有一处 ✓
            PluginField.Tick(dt, World, World.Player, ExplodeWithMult);
            TickAutoCharge(dt);                   // 阶段 3：自充（每秒回水）
            World.Simulate(dt);

            // 掉落进入钱包（World.GoldEarned 是统计值，这里同步到可花费金币）
            SyncGoldFromWorld();

            if (World.Player.Health.IsDead)
            {
                // ★★ v314：**先问「天使降临」** —— 拿着它就复活一次，本帧到此为止（不判负）★★
                //   为什么放在这一格：本处是**全工程唯一**把玩家死写成 `PhaseState.Defeat` 的地方
                //   （grep `PhaseState.Defeat` 只有本格与下面那条 BOSS 超时）⇒ 拦在这里
                //   一定拦在"游戏结束"之前 ✓
                //   复活后 `Hp > 0` ⇒ 下一帧这一格自然不成立 ⇒ 不需要"已复活"标志位 ✓
                //   没有这件道具时 `TryAngelRevive()` 立刻返回 false ⇒ 下面几行**逐字节不变** ✓
                if (TryAngelRevive()) return;
                ResultText = $"你在第 {WaveIndex} 分钟倒下了";
                Telemetry.Emit("run_end", ("result", "defeat"), ("wave", WaveIndex), ("duration", RunTime),
                    ("kills", World.KillCount), ("gold", Gold));
                SetPhase(PhaseState.Defeat, 0f);
                return;
            }

            // BOSS 波时限：与自愈 Boss 无限僵持不算"守住"，超时判负
            //
            // ★ v165 备注：本条在计时制下**已不可达** ★
            // `CombatElapsed` 每分钟被 `StartWave` 归零（`Clock.WaveIndex > WaveIndex` 每分钟必成立），
            // 最大值 = 60s < bossTimeLimit(90s) ⇒ 永远不成立。
            // 它是旧回合制（每回合 30~90 秒）时才可能走到的那条路，而那条路已被彻底删除
            // （波末结算整块 + `TimeDrivenWaves` 开关，2026-10）；本次只把文案里的"波"改成"分钟"。
            // 规则本身要不要连根删掉，见交付报告 —— 它现在是**不可达的防御性代码**，
            // 与上面那段死分支不同：它不会因为任何开关而复活 ✓
            if (Waves.IsBossWave && T.waves.bossTimeLimit > 0f && Waves.CombatElapsed > T.waves.bossTimeLimit)
            {
                ResultText = $"未能在 {T.waves.bossTimeLimit:0}s 内击败第 {WaveIndex} 分钟的 BOSS";
                Telemetry.Emit("run_end", ("result", "boss_timeout"), ("wave", WaveIndex),
                    ("duration", RunTime), ("elapsed", Waves.CombatElapsed));
                SetPhase(PhaseState.Defeat, 0f);
                return;
            }

            // ★ v186：**"分钟硬上限清场" 与 "分钟走完清残敌" 两条整块删除** ✗ ★
            //
            // 原文（保留在此，免得下一个人按老思路再写一遍）：
            //     float cap = Mathf.Max(1f, T.waves.hardCapFactor);
            //     float minuteLen = Clock.SecondsPerMinute;
            //     if (!Waves.IsBossWave && Waves.CombatElapsed > minuteLen * cap)
            //     {
            //         int despawned = World.DespawnAllEnemies();
            //         if (despawned > 0) Telemetry.Emit("wave_timeout", …);
            //     }
            //     if (!Waves.IsBossWave && Waves.CombatElapsed >= minuteLen
            //         && World.Enemies.Count > 0)
            //         World.DespawnAllEnemies();
            //
            // ── 这是用户报的"部分波次异常清除所有敌人"的**第二处**根因（第一处是 BOSS 出场清场）──
            // 实测（`DebugApi.ProbeLongRun` 20 分钟无头跑局，seed 20240607，留证在报告里）：
            //   整场清除发生 **11 次**，每一条的形状都一样：
            //     清除#1 @300.0s CombatElapsed=60.02s minuteLen=60s IsBoss=False WaveIndex=5/Clock=5 阶段=Combat
            //   ⇒ `Waves.Elapsed` 比时钟**早几帧**归零（`StartWave` 在跨分那一帧先执行），
            //     于是本分钟**最后一帧**的 `CombatElapsed` 已经是 60.02s ⇒ `>= 60` 成立 ✔
            //   ⇒ 而那一帧时钟还没跨过整分（`Clock.WaveIndex == WaveIndex`）⇒ 清场**不在**跨分那一帧被覆盖 ✗
            //   ⇒ 只要那一帧场上还有敌人，就整批清掉 ⇒ 表现正是"某些分钟敌人凭空消失" ✓
            //
            // ── 为什么"清场"在计时制下本身就不该存在 ──
            // 它是**回合制**的收尾动作：那时"一波"有明确终点，清残敌是为了让下一波从空场开始。
            // 现在回合概念已整体删除、波次是连续的（敌人跨分钟存活是设计）⇒
            // 清场等于**每分钟抹掉玩家正在打的那一批**，与"敌人越积越多"的难度曲线直接冲突 ✗
            //
            // ── 删掉之后软锁风险在哪 ──
            // 不靠收尾清场兜底：敌人数量由**刷怪侧**的 `waves.aliveLimitEnabled / maxAlive*` 管，
            // 与"分钟收尾"是两件事 ✓ `World.DespawnAllEnemies()` **本身保留** ——
            // `JumpToWave`（调试跳分钟）与自检夹具仍在用它 ✓

            // ★ 2026-10：**"波末结算 → 商店/抽卡"整块已删除** ★
            //
            // 这一段曾经是「波末结算 → 有未结算升级就抽卡 → 进商店」：
            //     if (Waves.IsComplete(World)) { World.SettleWaveEnd(); ... EnterShopAfterReward(); }
            // 而计时制下它**永不触发** ✗
            //   `Clock.WaveIndex > WaveIndex` 每分钟先把 `Waves.Elapsed` 归零，
            //   于是 `IsComplete` 的"分钟走完"条件永远差一个边界帧 ⇒ 本块一次也进不去。
            //
            // 两个入口都已经在别处，且是**唯一**入口：
            //   · 商店 ⇒ 【商店车】（`TickShopTruck` 的 `Truck.CanShop → SetPhase(Shop)`）✓
            //   · 抽卡 ⇒ 【升级即弹】（`OnLevelUp` → `DrawRewardCards`）✓
            //
            // ★ 为什么必须删而不是留着 ★
            // 它留着的唯一效果是"开关一翻就复活" ✗ —— 一段看起来还能跑的旧回合制流程
            // 会让人以为它仍是活路径，也与新玩法（无限计时、商店车、升级即弹）直接冲突 ✓
            // 所以整块连同 `SettleWaveEnd()` / `wave_end` 埋点 / `TimeDrivenWaves` 开关一起删除 ✓
            //
            // 注：`Waves.IsComplete(World)` **保留** —— 它是纯计算、无副作用，
            // 且 `SelfCheck.cs` 有两条断言在读它 ✓（"保留一个纯函数"不等于"保留回合概念"）
        }

        /// <summary>v11 抽卡阶段：等待玩家点卡或重抽（不限时）。</summary>
        /// <summary>
        /// v23：攻击即互动——攻击命中地块时：锄头=收获成熟植物、水壶=补水、肥料=立即催熟。
        /// 自动攻击与鼠标攻击都会走这里。
        /// </summary>
        /// <summary>
        /// v27：用**攻击判定框**（扇形）检测所有成熟植物，命中即收割。
        /// 不再依赖"最近地块"，因此不会被空地/未熟地块挡掉。
        /// 返回本帧收割的数量。
        /// </summary>
        public int HarvestRipeInMeleeBox(Vector2 origin, Vector2 dir, float range, float arcDeg)
        {
            int harvested = 0;
            if (World == null || World.Farm == null) return 0;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
            dir = dir.normalized;
            float half = Mathf.Max(30f, arcDeg) * 0.5f;

            for (int i = 0; i < World.Farm.Slots.Length; i++)
            {
                FarmPlot plot = World.Farm.Slots[i];
                if (plot == null || !plot.IsRipe) continue;
                Vector2 to = plot.World - origin;
                float dist = to.magnitude;
                if (dist > range + PlotGrabRadius) continue;                          // 判定框（含地块半径）
                if (dist > 0.01f && Vector2.Angle(dir, to) > half) continue;          // 扇形内
                if (HarvestPlot(plot)) harvested++;
            }
            return harvested;
        }

        /// <summary>v27：收割一块成熟土地。v47：逻辑收敛到 World.HarvestPlot（锄头与肥料共用），这里只做提示与埋点。</summary>
        public bool HarvestPlot(FarmPlot plot) => HarvestPlot(plot, 1f);

        /// <summary>
        /// ★ v290：带**收益系数**的重载（锋利的水壶收割 = 0.70）。
        /// 系数直接传给 `World.HarvestPlot(plot, yieldScale)` —— 收益的唯一实现仍在那里，
        /// 本函数只负责提示与埋点（与无参重载逐字相同）✓
        /// </summary>
        public bool HarvestPlot(FarmPlot plot, float yieldScale)
        {
            if (plot == null || !plot.IsRipe) return false;
            // ★ v166 修复：这里要的是"**这一格上长的是什么**" ⇒ 必须读 `CropSeedId` ★
            //
            // 原来读 `plot.SeedId` —— 而新模型下只有**列首**登记 `SeedId`（那是给
            // `SyncPlotsToSeeds` 的 "1 种子 : 1 地块" 契约为前提的，见 `FarmPlot.SeedId`）✓
            // 一列第 2/3/4… 行的作物 `SeedId` 是 null ⇒ `TableDatabase.GetSeed(null)`
            // 抛 ArgumentNullException ⇒ **玩家对着熟地挥一次锄头就崩一次**（每帧复发）✗
            string seedId = plot.CropSeedId;
            int plotIndex = plot.Index;
            if (!World.HarvestPlot(plot, yieldScale)) return false;
            // ★ v168：这里要的是**地里种的那一份定义**，不是"按 id 回查表" ✗
            // `TableDatabase.GetSeed` 对**不存在的 id 抛异常**（只有**空** id 才返回 null ✓）
            // ⇒ 与其同形的下一行（`InteractPlotByAttack`）一样，这行会在"某种 id 不在表里"时崩 ✓
            //   —— 而收割路径撞上它就是**挥一次锄头崩一次**（与 v166 修过的 `GetSeed(null)` 同形）✗
            // `plot.CropDef` 就是 `BeginPlant` 传进来的那份定义 ⇒ 每格都有，且**不需要查表** ✓
            SeedDef def = plot.CropDef;
            // ★★ v197：**收割触发机制**（植物机制 1 / 2）★★
            //   用户原话："收割此植物 ⇒ 3 秒内攻击倍率/攻击速度 +3%，最多 12 层，每次叠加刷新时长，每升 1 级 +1%"
            //   放在 `World.HarvestPlot` **成功之后**：没熟（收割失败）就不该给增益 ✓
            ProgMechanics.OnHarvest(this, def, plot.Level);
            // ★ v166：提示文案原来写「→ 植物帮手参战」✗ 而 v165 已经删掉"收割产生帮手" ✓
            // ⇒ 这句话在**对着玩家的界面**上撒谎：他说收割会得到帮手，但什么也没多出来 ✗
            // 收益的真实内容是 World.HarvestPlot 发的**金币与经验**（每格 price/5 与 price/10）✓
            SetPrompt($"收割 {def?.Name} → 金币与经验（土地重新生长）");
            // ★ T2：遥测里的 `("plants", World.Plants.Count)` **已删除** ✗ ★
            //   它上报的是"场上植物**实体**数"—— 那个列表已下线 ⇒ 字段无处可读 ✓
            //   ★ 存活植株口径另有 `Farm.PlantCount`（新玩法：一株 = 一列地块）★ ✓
            Telemetry.Emit("plot_harvest", ("seed", seedId), ("plot", plotIndex));
            return true;
        }

        public void InteractPlotByAttack(FarmPlot plot, int toolIndex, float radius)
        {
            if (plot == null || World == null) return;
            switch (toolIndex)
            {
                case ToolHoe:
                    if (!plot.IsRipe)
                    {
                        // v25：给玩家明确反馈（此前"打不动"没有任何提示）
                        SetPrompt(plot.GrowthPaused
                            ? $"土地缺水（{plot.Water}/{plot.WaterMax}）→ 用水壶攻击补水"
                            : $"还没成熟（{plot.GrowthRemaining:0} 秒，进度 {plot.GrowthProgress01 * 100f:0}%）");
                        break;
                    }
                    if (plot.IsRipe)
                    {
                        // ★ v166：删除"收割即参战"的**最后一份副本** ★
                        //
                        // 这里原来是 `SpawnPlantFromPlot` + `FreeFromPlot` + `SetGrown`
                        // —— 即"收割的奖励是多一个战斗单位" ✗
                        // 而 `World.HarvestPlot` 里同样的一段已在 v165 被删掉 ✓
                        // ⇒ 同一个规则有两个副本，v165 只删了一处 ⇒ "植物不战斗"其实是假的 ✗
                        //
                        // 现在两条收割路径都收敛到 `World.HarvestPlot`（唯一实现）✓
                        // 收益口径因此一致：每格 `price/5` 金币 + `price/10` 经验 ✓
                        //
                        // ⚠️ 关键：**不能只删 spawn 而保留 `plot.Harvest(...)`** ✗
                        //    那样玩家既拿不到帮手、也拿不到金币 ⇒ "收割没有奖励" ✗
                        //    删除与改道必须同时做 ✓
                        if (World.HarvestPlot(plot))
                        {
                            // ★ v168：同 `RunSession.HarvestPlot` —— 提示文案读**地块持有的定义**，
                            // 不按 id 回查表（`GetSeed` 对不存在的 id 抛异常）✓
                            SeedDef def = plot.CropDef;
                            SetPrompt($"收获 {def?.Name} → 金币与经验（地块重新生长）");
                            Telemetry.Emit("plot_harvest", ("seed", plot.CropSeedId));
                        }
                    }
                    break;
                case ToolCan:
                    // ★ v253：**催熟**（`PluginEffectKind.Ripen`）—— 水柱扫到"已种下但没成熟"的地块
                    //   就推进成长。它与"补水"是两件事：补水改的是水，催熟改的是进度 ✓
                    //   ⚠️ 只对**生长中**的地块生效：空地/已成熟/已枯死一律不推进（否则等于白送成熟）✓
                    {
                        WeaponModifiers rip = WeaponSystem.Aggregate(Goods, WeaponSlot.Can);
                        if (rip.RipenSeconds > 0f && plot.CropDef != null && plot.State == PlotState.Growing)
                            plot.TickGrowth(rip.RipenSeconds);
                    }
                    int added = plot.AddWater(T.farm.waterPerHit > 0 ? T.farm.waterPerHit : 10);   // v167：每次命中 10 水（原 50 ⇒ 两帧就补满，浇水近乎免费）
                    if (added > 0)
                    {
                        // ★ v167：**补水要花水壶的水**（见 `WaterPerAmmo` 的算式）★
                        // 只对实际送出去的水收费 ⇒ 满水的地免费（`added == 0` 直接不进这个分支）✓
                        //
                        // 收费按**累计**算，不按每次命中算 ✗
                        //   一次只倒 10 水 ⇒ 若每次 `ceil(10/50)=1` 就扣 ⇒ 补满一格要 10 点
                        //   而按总量只该 2 点 ⇒ 价格虚高 5 倍 ✓（`_waterUnbilled` 就是为这个存在的）
                        //
                        // 付不起也**照补**：只是把弹药夹到 0 ✓
                        //   理由：持枪本身已经付过起手费（TickBeam 里 10/0.5 秒）✓
                        //   在这里"拒绝补水"会做出一个半截状态（扣了枪费、水又没浇上）✓
                        //   弹药见底的自然表现是**下一拍激光停火**（TickBeam 的余额检查）✓
                        _waterUnbilled += added;
                        int cost = 0;
                        while (_waterUnbilled >= WaterPerAmmo)
                        {
                            _waterUnbilled -= WaterPerAmmo;
                            cost++;
                        }
                        if (cost > 0) WaterAmmo = Mathf.Max(0, WaterAmmo - cost);
                        Telemetry.Emit("plot_water", ("plot", plot.Index), ("added", added),
                            ("water", plot.Water), ("ammo", WaterAmmo), ("cost", cost));
                    }
                    break;
                case ToolFertilizer:
                    // 肥料爆炸波及 → 立即成熟一次
                    if (plot.State == PlotState.Growing)
                    {
                        plot.TickGrowth(999f);
                        SetPrompt("肥料催熟 → 立刻成熟（用锄头收获）");
                        Telemetry.Emit("plot_fertilize", ("plot", plot.Index));
                    }
                    break;
            }
        }

        private void TickLevelReward(float dt, in FrameInput input)
        {
            if (input.RewardSelectSet && input.RewardSelect >= 0 && input.RewardSelect < RewardCards.Count)
            {
                RewardSelected = input.RewardSelect;
                return;
            }
            if (input.RewardPickConfirmed && input.RewardPick >= 0 && input.RewardPick < RewardCards.Count)
            {
                PickRewardCard(input.RewardPick);
                return;
            }
            // 防御：没有任何卡时直接回战斗，避免卡死在抽卡界面
            // v165：原来是 `EnterShopAfterReward()` —— 抽卡不该把玩家送进商店（见 EnterCombatAfterReward）
            // 2026-10：`EnterShopAfterReward()` 本身也已删除（0 调用者的旧"波间购买"入口 ✗）
            if (RewardCards.Count == 0) EnterCombatAfterReward();
            // v53：重抽是"带花费的脉冲"，已挪到 ConsumeFrameEdges 帧级消费（否则追帧会扣多次金币）
        }

        private void TickShop(float dt, in FrameInput input)
        {
            // v4：回合间购买物资不限时（空格/按钮离开）
            bool infinite = T.shop.infinitePrepare;
            if (!infinite) PhaseTimer -= dt;

            if (input.BuyOffer >= 0)
            {
                if (!Shop.TryBuy(input.BuyOffer, this, out string reason)) SetPrompt(reason);
            }
            // v53：商店刷新已挪到 ConsumeFrameEdges 帧级消费（否则追帧会扣多次金币）
            if (input.SelectSeed >= 0) Bag.Selected = Mathf.Clamp(input.SelectSeed, 0, Bag.SlotCount - 1);
            // v95：商品锁定已挪到 ConsumeFrameEdges（见那里的说明）

            if (input.SlotClicked) HandleBagClick(input.ClickedSlot);

            // 合成：点某个种子的"合成"→ 找同种同级的另一个合成（等级 +1）
            if (input.SlotMergeClicked && input.SlotMerge >= 0 && input.SlotMerge < Bag.SlotCount)
            {
                int idx = input.SlotMerge;
                string mergeReason;
                if (Bag.MergeSlot(idx, out mergeReason))
                {
                    Bag.Selected = idx;
                    SeedSlot merged = Bag[idx];
                    SetPrompt($"合成成功 → {merged.Label} {merged.Level} 级（卖出返还 ×{merged.LevelYieldMult:0.00}）");
                    Telemetry.Emit("seed_merge", ("seed", merged.Def.Id), ("level", merged.Level));
                    SyncOwnedSeeds();
                }
                else SetPrompt(mergeReason);
            }


            // 出售种子（点击"卖"按钮），返还 50%'
            if (input.SellSlot >= 0 && input.SellSlot < Bag.SlotCount)
            {
                // v19：至少保留 1 个种子（只剩 1 个时禁止出售）
                if (Bag.OccupiedCount <= 1)
                    SetPrompt("至少保留 1 个种子，无法出售");
                else if (Bag.Sell(input.SellSlot, WaveIndex, out int refund))
                {
                    Gold += refund;
                    // v46：一块耕地对应一个种子——把种子卖掉，它驱动的那块耕地必须还回来
                    SyncPlotsToSeeds(out int releasedPlots, out _);
                    SyncOwnedSeeds();
                    SetPrompt(releasedPlots > 0
                        ? $"已出售，返还 {refund}G（释放 {releasedPlots} 块耕地）"
                        : $"已出售，返还 {refund}G");
                    Telemetry.Emit("shop_sell", ("slot", input.SellSlot), ("refund", refund),
                        ("plots_released", releasedPlots));
                }
            }

            bool next = input.Skip || (!infinite && PhaseTimer <= 0f);
            if (!next) return;

            // ★★ 离开商店【不能】无条件把分钟数 +1 ★★
            //
            // 原实现：`StartWave(WaveIndex + 1);` ✗ —— 它**与时钟无关** ✓
            //   ⇒ 每进一次商店，难度分钟就凭空 +1 ⇒ `World.WaveIndex` 前进 ⇒
            //     敌人 HP / 伤害 / 在场上限 / 每拍刷新量**全部提前跳一档** ✗
            //
            // ★ 为什么它以前没被发现 ★：修商店窗之前，玩家与机器人整局进出 **0~1 次**
            //   ⇒ 这条几乎不触发 ✓ ⇒ ★ 它一直被"商店进不去"那个 bug **掩盖**着 ★ ✓
            //   修好商店窗之后每局进出 **~3 次** ⇒ 难度曲线会跑到真实时间前面 1~3 分钟 ✓
            //   （实测形态：t=170.1s 第 4 分钟 ⇒ t=173.6s 第 5 分钟 ⇒ t=177.2s 第 6 分钟，
            //     7 秒里跳了两档 ✗ —— 玩家在商店里"停一下"，出来敌人就变强了一整档 ✓）
            //
            // ★ 正确做法：交给既有的"**落后就补**"契约 ★ ——
            //   `FixedTick` 里已有：
            //     `if (!IsOver && Phase == Combat && Clock.WaveIndex > WaveIndex) StartWave(Clock.WaveIndex);`
            //   ⇒ 时钟真的走过了就补到当前分钟 ✓；没走过就**只是回到战斗** ✓
            //
            // ⚠️ 一处**有意的语义变化**（必须说明）：原实现每次离店都白送一次
            //   `StartWave` ⇒ 顺带 `ResetPlayerForNewWave()` **满血 + 满盾** ✓
            //   改后只有"时钟真的跨分钟"时才补 ⇒ **离店不再白送满血** ✓
            //   这与"补满血是每分钟一次的事件"（`StartWave` 的语义）一致 ✓
            if (Clock.WaveIndex > WaveIndex) StartWave(Clock.WaveIndex);
            else SetPhase(PhaseState.Combat, 0f);
        }

        private void SyncGoldFromWorld()
        {
            // World 统计累计收益，RunSession.Gold 记录可花费余额
            int diff = World.GoldEarned - _goldCredited;
            if (diff <= 0) return;
            Gold += diff;
            _goldCredited = World.GoldEarned;
        }

        private int _goldCredited;

        /// <summary>调试/自动化：把当前回合的时间推到本分钟结束（再清场即可立刻收尾）。</summary>
        public void WaveDirectorSkipToEnd() => Waves.SkipToEnd();

        /// <summary>调试/自动化：跳到指定分钟（清空场上敌人与残留弹道/掉落）。</summary>
        public void JumpToWave(int index)
        {
            World.Enemies.Clear();
            World.Projectiles.Clear();
            World.Pickups.Clear();
            ClearSwordWaves();          // v185：在飞的剑气也一起清（否则会飞进新一分钟）
            ClearWaterBalls();          // ★需求④：在飞的水球同理
            // v161：**去掉上限钳制** ✗ —— v165 补完：那条钳制用的 `TotalWaves` 现在整个删掉了。
            // 原来是 `StartWave(Mathf.Clamp(index, 1, TotalWaves))` —— 把回合数卡在 20 以内 ✓
            // 计时制下回合由**时间**派生（`Clock.WaveIndex`），没有上限 ✗
            // 用 `Max(1, ...)` 只保留"不能是 0 或负数"这个下限 ✓
            StartWave(Mathf.Max(1, index));
        }

        /// <summary>调试/自动化：直接进入商店阶段（不推进波次）。</summary>
        public void ForceShopForDebug()
        {
            Shop.ResetForPhase();
            Shop.Refresh(this);
            SetPhase(PhaseState.Shop, T.shop.prepareTime);
        }

        /// <summary>
        /// ★ **按 E** 的可自动化入口 ★ —— 等价于"玩家这一帧按下了 E"，用于机器人 / 外部脚本。
        ///
        /// ★ 为什么必须留它 ★
        /// E 键由 `GameBootstrap.ReadInput` 从 `Keyboard.current.eKey` 读 ✗
        /// 而 Unity 的 `Input`/`InputSystem` 读的是**操作系统**的键盘状态 ⇒
        /// 自动化既点不到那个键，也无法在无头回放里"按键" ✓
        /// （本会话已知的硬约束：键盘驱动的游戏必须**自己留一个方法**给自动化调 ✓）
        ///
        /// ★ 为什么是"拉一下闩"而不是"直接开店" ★
        /// 直接 `SetPhase(Shop)` 会**绕过玩法判定**（停靠期 + 玩家在 4 米内）✗
        /// 那样自动化测出来的就是"这个方法能被调"，而不是"玩家按 E 能进店" ✓
        /// 这里只置一个一次性闩，下一帧由 `TickShopTruck` 和真实 E 键**走同一条分支** ⇒
        /// 条件不满足时它和按键一样**什么都不发生** ✓
        ///
        /// ★ 一次性 ★：被消费的当帧就清掉（否则会变成"一直按着 E"⇒ 每帧重进商店）✓
        /// </summary>
        public void RequestShopOpen() => _shopKeyLatch = true;

        /// <summary>`RequestShopOpen` 的一次性闩（见那里的说明）。</summary>
        private bool _shopKeyLatch;
        /// <summary>
        /// v46：**一块耕地对应一个种子**（第 9 波教学：种子位与耕地 1:1）。
        /// 把"种子袋里每种种子的数量"与"占着耕地、由该种子驱动的地块数量"对齐：
        ///   地比种子多 → **释放多余的地**（典型场景：玩家在商店把种子卖了）
        ///   地比种子少 → 用空地把种子补种上（典型场景：买到新种子 / 上一波铲除了植株）
        /// 旧版只有"补种"没有"释放"，所以卖掉种子后地块会继续生长，出现"2 个种子 3 块地"。
        /// </summary>
        public void SyncPlotsToSeeds(out int released, out int planted)
        {
            released = 0;
            planted = 0;
            if (World == null || Bag == null || World.Farm == null) return;

            // ① 袋里每种种子的数量（v168：顺便记住**等级**，补种与同步都要用）
            var ownedBySeed = new Dictionary<string, int>();
            var slotOfSeed = new Dictionary<string, SeedSlot>();
            for (int i = 0; i < Bag.SlotCount; i++)
            {
                SeedSlot slot = Bag[i];
                if (slot == null || slot.IsEmpty || slot.Def == null) continue;
                ownedBySeed.TryGetValue(slot.Def.Id, out int c);
                ownedBySeed[slot.Def.Id] = c + 1;
                // 同种多槽时取**等级最高**的那个：地块只有一个等级，
                // 取最高的才不会让"刚合成出来的 2 级"被一个残留的 1 级槽位压回去 ✓
                if (!slotOfSeed.TryGetValue(slot.Def.Id, out SeedSlot kept) || slot.Level > kept.Level)
                    slotOfSeed[slot.Def.Id] = slot;
            }

            // ② 占着耕地的地块，按"驱动它的种子"分组
            var plotsBySeed = new Dictionary<string, List<FarmPlot>>();
            for (int i = 0; i < World.Farm.Slots.Length; i++)
            {
                FarmPlot plot = World.Farm.Slots[i];
                if (plot == null || string.IsNullOrEmpty(plot.SeedId)) continue;
                if (plot.State != PlotState.Growing && plot.State != PlotState.Grown && plot.State != PlotState.Seeded) continue;
                if (!plotsBySeed.TryGetValue(plot.SeedId, out List<FarmPlot> group))
                {
                    group = new List<FarmPlot>();
                    plotsBySeed[plot.SeedId] = group;
                }
                group.Add(plot);
            }

            // ③ 地多出来的部分一律释放（袋里没有这个种子时 owned = 0，等于全部释放）
            foreach (KeyValuePair<string, List<FarmPlot>> kv in plotsBySeed)
            {
                ownedBySeed.TryGetValue(kv.Key, out int owned);
                for (int i = owned; i < kv.Value.Count; i++)
                {
                    FarmPlot plot = kv.Value[i];
                    // ★ T2：这里原来先 `FindPlantOnPlot` → `plant.Health.Kill()` 再 `RemoveGrowing()` ✗ ★
                    //   而 `World.Plants` 已无生产者 ⇒ `FindPlantOnPlot` **恒返回 null**
                    //   ⇒ `plant != null && plant.Alive` 恒假 ⇒ 那两行**从来什么都没做** ✓
                    //   ★ 真正的释放动作是下面这行 `RemoveGrowing()` ★ —— 它做 `ClearColumn` + `ResetToEmpty` ✓
                    //   ⇒ 删掉那两行**不改变任何行为** ✓（已按"先读调用点再删"的规矩读过调用方 ✓）
                    plot.RemoveGrowing();          // Growing/Grown → Empty，并清掉 SeedId
                    released++;
                    Telemetry.Emit("plot_release", ("seed", kv.Key), ("plot", plot.Index), ("owned", owned));
                }
            }

            // ④ 地不够的部分用空地补种（等级取该种子在袋里的最高级）
            foreach (KeyValuePair<string, int> kv in ownedBySeed)
            {
                plotsBySeed.TryGetValue(kv.Key, out List<FarmPlot> have);
                int missing = kv.Value - (have == null ? 0 : have.Count);
                if (missing <= 0) continue;
                SeedDef def = TableDatabase.GetSeed(kv.Key);
                slotOfSeed.TryGetValue(kv.Key, out SeedSlot slot);
                int level = slot != null ? slot.Level : SeedSlot.StartingLevel;
                for (int k = 0; k < missing; k++)
                {
                    if (!AutoPlantSeed(def, level)) break;   // 没有空地了就停下
                    planted++;
                }
            }

            // ⑤ v168：**把地里的等级同步成袋里的等级**（合成之后那一株跟着升级）✓
            //
            // 为什么必须在**每回合开始时**做一次，而不是"合成时立刻改地"：
            //   合成发生在商店阶段，此刻玩家可能正在读地块状态 ✗
            //   而这里已经是既有的"种子袋 ↔ 耕地"对齐点 ⇒ 挂在这里只有一个入口 ✓
            // 幂等：`FarmGrid.SetColumnLevel` → `FarmPlot.SetLevel` 在等级没变时直接返回 ✓
            int reLeveled = 0;
            for (int i = 0; i < World.Farm.Slots.Length; i++)
            {
                FarmPlot plot = World.Farm.Slots[i];
                if (plot == null || !plot.IsColumnHead || string.IsNullOrEmpty(plot.SeedId)) continue;
                if (plot.State != PlotState.Growing && plot.State != PlotState.Grown && plot.State != PlotState.Seeded) continue;
                if (!slotOfSeed.TryGetValue(plot.SeedId, out SeedSlot slot) || slot == null) continue;
                if (plot.Level == slot.Level) continue;
                World.Farm.SetColumnLevel(plot, slot.Level);
                reLeveled++;
            }
            if (reLeveled > 0)
                Telemetry.Emit("plots_relevel", ("columns", reLeveled));

            if (released > 0 || planted > 0)
                Telemetry.Emit("plots_sync", ("released", released), ("planted", planted),
                    ("owned_slots", Bag.OccupiedCount));
        }

        /// <summary>占着耕地、由该种子驱动的地块数（自检与 UI 用）。</summary>
        public int GrowingPlotCountOf(string seedId)
        {
            if (World == null || World.Farm == null || string.IsNullOrEmpty(seedId)) return 0;
            int n = 0;
            for (int i = 0; i < World.Farm.Slots.Length; i++)
            {
                FarmPlot plot = World.Farm.Slots[i];
                if (plot == null || plot.SeedId != seedId) continue;
                if (plot.State != PlotState.Growing && plot.State != PlotState.Grown && plot.State != PlotState.Seeded) continue;
                n++;
            }
            return n;
        }

        /// <summary>当前有多少块耕地被种子占着（= 应该等于种子袋的非空槽位数）。</summary>
        public int GrowingPlotCount()
        {
            if (World == null || World.Farm == null) return 0;
            int n = 0;
            for (int i = 0; i < World.Farm.Slots.Length; i++)
            {
                FarmPlot plot = World.Farm.Slots[i];
                if (plot == null || string.IsNullOrEmpty(plot.SeedId)) continue;
                if (plot.State != PlotState.Growing && plot.State != PlotState.Grown && plot.State != PlotState.Seeded) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// v29：种子袋容量 = 耕地数量（1:1）。耕地增加时（升级 / 开局）必须同步调用，
        /// 否则会出现"耕地变多了但种子位还是 1"的问题。
        /// </summary>
        public void SyncSeedSlots()
        {
            if (World == null || Bag == null) return;
            int plots = Mathf.Max(1, Progress.MaxPlots);   // v30：容量 = 最大耕地数
            if (Bag.SlotCount != plots)
            {
                Bag.SetCapacity(plots);
                Telemetry.Emit("seed_slots_sync", ("plots", plots), ("bag_slots", Bag.SlotCount));
            }
        }

        /// <summary>把种子袋快照同步给 World（树的产出清单）。</summary>
        public void SyncOwnedSeeds()
        {
            if (World != null) World.SyncOwnedSeeds(Bag);
        }

        public void StartWave(int index)
        {
            WaveIndex = index;
            // ★ v189：**"清瞬时状态"只在【开局那一波】做，不再每分钟做** ✗ ★
            //
            // ── 用户原话 ──
            //   "每次到整点1分钟、2分钟、3分钟等出现了异常刷新状态，回合机制残留，
            //    导致玩家攻击突然中断再触发（导致攻速异常），或者充能异常消失"
            //
            // ── 病因（这就是那句"回合机制残留"的落点）──
            //   `StartWave` 在计时制下**每 60 秒被调用一次**（`FixedTick` 的
            //   `Clock.WaveIndex > WaveIndex` ⇒ 每分钟必然成立）✗
            //   而它第一句是 `ClearTransientCombatState()` —— 那是 v100 **回合制**的产物：
            //   回合末丢出、回合初才炸的炸弹属于"跨回合垃圾" ✓
            //   ★ 可计时制里**没有回合**，一分钟边界不是"一局重开"★
            //   ⇒ 每 60 秒把玩家的**在飞/在读条的事**清空：
            //       · `_pendingBlasts.Clear()` → 前摇中的肥料**凭空消失**（充能已扣）✗
            //       · `StopBeam()`              → 按住的水壶激光被掐断，下一帧**重新收起手费 20 点**
            //                                     ⇒ 一秒里被扣两次 ⇒ "充能异常消失" ✗
            //       · `ClearWaterBalls()`       → 在飞的水球消失 ✗
            //       · `_beamHitCd.Clear()`      → 每个目标的再受伤间隔被重置 ✗
            //   这四处都是"活过一帧仍然有意义"的过程量 —— 它们该活过分钟边界，因为
            //   **分钟边界不是新的一局** ✓
            //
            // ── 判据 ──
            //   只有"这一次 StartWave 是**本局第一次**"时清：那时界面上确实没有上一局的东西 ✓
            //   实测（`DebugApi.ProbeMinute` / `ProbeBoundaryFx`，见交付报告）：
            //     改前 跨分瞬间 待爆肥料 1 → 0、按住的激光多扣一次 20；
            //     改后 两者都是 0 次变化 ✓
            bool firstWaveOfRun = !_waveEverStarted;
            _waveEverStarted = true;
            if (firstWaveOfRun) ClearTransientCombatState();
            SyncOwnedSeeds();
            World.WaveIndex = index;
            // ★ v168：道具的**成长**与**机制**在每回合开始时重算一次 ★
            //
            // `ReapplyItems` 让 `growPerWave`（"加入成长"）真的随回合变强 ——
            // 只买的时候算一次的话，成长型道具会永远停在买入那一刻的数值 ✗
            // `SyncItemRules` 同理：机制类道具也可以带成长 ✓
            //
            // 顺序：**先 `WaveIndex = index` 再重算** —— 成长公式读的就是 `WaveIndex` ✓
            ReapplyItems();
            SyncItemRules();
            Waves.StartWave(index);
            ResetPlayerForNewWave();
            // v46：每回合开始对齐「种子数 ↔ 生长中的耕地数」（补种 + 释放多余地块）
            SyncPlotsToSeeds(out int releasedPlots, out int plantedPlots);
            if (releasedPlots > 0 || plantedPlots > 0)
                Telemetry.Emit("wave_plots_sync", ("released", releasedPlots), ("planted", plantedPlots));
            // ★ v178：`CenterPlayerForNewWave()` **已整段退役** ✗ —— 它每回合把玩家写回 (0,0) ★
            //
            // 症状（用户原话）："玩家位置异常，每分钟玩家会被异常刷新到初始地点
            // （疑似回合遗留垃圾）"。
            //
            // 那两句是 **回合制** 的遗物（v19）：那时"下一波"由玩家点按钮推进，
            // 一次开波 = 一次有意识的重开 ⇒ 把角色摆回世界中心是合理开场 ✓
            // 但回合制已被彻底删除，现在 `StartWave` 由**时钟**驱动（见 `FixedTick`）：
            //   `Clock.WaveIndex > WaveIndex`，而 `Clock.WaveIndex = MinuteIndex + 1`、
            //   `MinuteIndex = (int)(GameTime / 60f)` ⇒ **每 60 秒必然成立一次** ✗
            // ⇒ "每回合回中心"就退化成"每分钟把玩家从他站着的地方拽到 (0,0)" ✗
            //
            // 实测（全自动跑的，脚本见交付说明）：
            //   · 关掉 Bot 让玩家静止 ⇒ 2.5 秒内**自身位移 0.0000m**（不会自己动）
            //   · 一次 `StartWave` 之后 ⇒ 位移 **7.0000m**
            //   · 起点 `(0, 0, -7)`（出生点，`Start()` 里写的 `new Vector2(0f, -7f)`）
            //     终点 `(0, 0,  0)`（原实现写的是 `Vector2.zero`）
            //     ⇒ `|−7 − 0| = 7.0000` **逐位吻合** ✓
            //
            // ── 为什么连 `player_recenter` 遥测也一起退役 ──
            //   若只删"写位置"那一句，函数就只剩 `Telemetry.Emit("player_recenter", …)`
            //   ⇒ 会每回合上报一次 "recenter"，而实际什么都没 recenter ✗
            //   那正是本工程反复踩过的"名字/日志与实际不符"。所以整个函数退役 ✓
            //
            // ★ 为什么不是"在别处把位置改回来" ★
            //   `World.Player.Pos` 是**逻辑真值**：敌人索敌、耕地交互距离、雨滴落点全读它。
            //   在表现层（`WorldView.SyncPlayer`）补一次写回，只会让逻辑与表现各持一套真相 ✗
            //   位置只该由**移动**改，不该由"新的一分钟开始"改 ⇒ 删在源头 ✓
            //
            // ⚠️ `ResetPlayerForNewWave()`（上面 :3457 调用）**保留不动**：
            //   它做的是 Heal / RefillShield / 清冷却与前摇 —— "每波满血"是现行设计
            //   （见 `DeathDiagnostics` 的"分钟边界"一节），与"位置被重置"是两件事，
            //   用户只点了后者 ✓
            // v23：每回合开始弹药——水壶 +50（上限 100），肥料 0
            WaterAmmo = Mathf.Min(WaterAmmoCap, WaterAmmo + 50);
            // ★ v182：肥料**不再每分钟清零**，改为**跨波保留**（上限仍是 `FertAmmoMax = 10`）★
            //
            // 这不是平衡改动，是修掉一个"让玩家白攒的 bug"：
            //   `FertAmmo` 的唯一来源是"击败小猪 +1"（`AddFertAmmo`），而 v23 那句
            //   `FertAmmo = 0` 每 60 秒把它清零一次 ⇒ 玩家攒到 7/10 过一分钟就**凭空没了** ✗
            //   用户原话"肥料充能出现偶尔自动消失"最可能就是它 —— 而且"偶尔"很贴切：
            //   只有在你**手上还有余弹**时过分钟才会被吃掉 ✓
            // 保留 `+ _fertRefund`：跨分钟被丢掉的那几发炮弹同样退回来（见 ClearTransientCombatState）✓
            FertAmmo = Mathf.Clamp(FertAmmo + _fertRefund, 0, FertAmmoMax);
            _fertRefund = 0;
            Telemetry.Emit("ammo_reset", ("water", WaterAmmo), ("fert", FertAmmo));
            SetPhase(PhaseState.Combat, 0f);
            Telemetry.Emit("wave_start", ("wave", index), ("player_level", Progress.Level),
                ("plots_used", World.Farm.UsedPlotCount), ("gold", Gold),
                ("grace", Waves.GraceSeconds), ("enemy_budget", EnemyBudget(index)));
        }

        /// <summary>
        /// v100：清掉**跨回合不该继承**的瞬时战斗状态。
        /// ★ v189：调用时机从"每次 `StartWave`"收窄到"**本局第一波**" ✗ ★
        ///
        /// 为什么要收窄：计时制下 `StartWave` 每 60 秒跑一次，而"分钟边界"**不是新的一局** ⇒
        /// 在这里清空等于每分钟把玩家在飞的东西抹掉一次（肥料前摇 / 水球 / 按住的激光）✗
        /// 详见 `StartWave` 开头那段（含改前改后的实测读数）✓
        ///
        /// 本方法本身**一行没改** —— 它在"开局"这个语义下仍然完全正确：
        /// 那时界面上确实不该有上一局残留的东西 ✓
        ///
        /// 原始触发点（保留，作为"为什么这个函数存在"的记录）：
        /// 玩家在回合最后一秒丢出肥料，**爆炸却发生在下一回合**。
        /// 根因是 v91 加的前摇队列 `_pendingBlasts` —— 它记着"0.5 秒后炸"，
        /// 而 0.5 秒后回合已经切了，于是炸弹在新回合的场上开花。
        ///
        /// 同一类问题不止那一处：v90 的连续激光还留了"同一目标的再受伤间隔"与扣费计时，
        /// 它们同样会跨回合继承——新回合开局就带着上一回合的 CD，第一发光束会被静默吃掉。
        ///
        /// 判据很简单：**凡是"活过一帧就没有意义"的状态，回合切换时必须清零。**
        /// 它们不是存档数据，是过程量。
        /// </summary>
        private void ClearTransientCombatState()
        {
            // v104 诊断：报告这一回合清掉了什么。
            // 关键看 pending —— 只要它 >0，就说明"上一回合真的有一颗还没炸的肥料"，
            // 也就是玩家报的"最后一秒丢出、下一回合才炸"**确实会复现**，而这次被拦下了。
            // 全是 0 也是有效信息：说明这段窗口没被踩中，需要换时机再试。
            int pendB = _pendingBlasts.Count, cdB = _beamHitCd.Count;
            bool firingB = _beamFiring;

            // ★ v181：被丢掉的炮弹**把充能退回去**（不静默吞掉）★
            // 上面那句 `_pendingBlasts.Clear()` 是 v100 为了"别让上一回合的炸弹在下回合开花"加的，
            // 方向是对的；但它顺手把**已经扣掉的充能**也一起吞了 ✗
            // 玩家在分钟边界前半秒丢出一发肥料 ⇒ 充能 −1、爆炸被丢 ⇒
            // 这才是"肥料充能偶尔自动消失"里最难解释的一种（每分钟只有 0.5/60 的窗口）✓
            // 计数留到 `StartWave` 里兑现 —— 因为那句 `FertAmmo = 0` 在本函数之后才执行 ✓
            _fertRefund = pendB;
            _pendingBlasts.Clear();      // 未引爆的肥料：不该在新回合才炸
            _beamHitCd.Clear();          // 激光的每目标 CD：新回合每个目标都该是可打的
            MultiJetSharedSkips = 0;     // ★ v305：多柱共享计数按回合归零（读数，不参与判定）
            StopBeam();                  // 激光的"正在发射"与扣费计时：新回合要重新收起手费
            ClearWaterBalls();           // ★需求④：在飞的水球也是"活过一帧仍然有意义"的过程量 ⇒ 新回合清空
            // v123：小猪保底不复位在这里 —— 刷怪逻辑在**嵌套类**里，这里的字段它看不到。
            // 由刷怪逻辑自己检测波次变化来复位（见 _pigWaveMark），跨类访问就此避免。
            // 表现层的残留不用管：激光自己 0.12 秒后就销毁了，跨不过回合边界。
            // 真正会跨回合的只有上面这几个"活过一帧仍然有意义"的过程量。

            UnityEngine.Debug.Log(
                $"[Seedfall] 回合 {WaveIndex} 清理瞬时状态：待爆肥料={pendB} 激光CD={cdB} 激光发射中={(firingB ? 1 : 0)}");
        }

        private int EnemyBudget(int index)
        {
            WaveDef w = TableDatabase.GetWave(index);
            if (w == null) return 0;
            int n = 0;
            for (int i = 0; i < w.Batches.Length; i++) n += w.Batches[i].Count;
            return n;
        }

        // ------------------------------------------------------------ 玩家操作
        private void HandlePlayer(float dt, in FrameInput input)
        {
            PlayerActor p = World.Player;
            StatBlock st = Progress.Stats;
            TuningDef t = T;
            // v8 度量：上一帧在耕作、且这一帧发现自己刚挨打 → 记录一次"耕作被打"
            if (FarmingThisFrame && p.SinceDamaged < 0.05f) FarmHits++;
            FarmingThisFrame = false;   // v8：本帧是否正在耕作（决定能否攻击）

            // ★★ 选种在**耕作判定之前**结算（原来这段在本方法**末尾**）★★
            //   病因：播种读的是 `Bag.SelectedSlot`，而写 `Bag.Selected` 的那两行在**同一个方法的后面**
            //   ⇒ 本帧点选种子、本帧播种 ⇒ 种下去的仍是**上一帧**那颗（玩家读作"切了种子有延迟"）✗
            //   ⇒ 把它俩提到最前面：选种 → 播种，**同帧同一次调用内**，中间没有任何缓存/快照 ✓
            if (input.SlotClicked) HandleBagClick(input.ClickedSlot);
            if (input.SelectSeed >= 0) Bag.Selected = Mathf.Clamp(input.SelectSeed, 0, Bag.SlotCount - 1);

            // v48：数字键 1/2/3 直接切到 锄头 / 水壶 / 肥料
            if (input.WeaponSelectSet && input.SelectWeapon >= PlayerActor.WeaponHoe
                && input.SelectWeapon <= PlayerActor.WeaponFertilizer && input.SelectWeapon != p.WeaponIndex)
            {
                p.SwitchWeapon(input.SelectWeapon, World);
            }

            // 耕地交互
            int plotIdx = World.Farm.NearestUnlocked(p.Pos, t.farm.interactRange);
            CurrentPlotIndex = plotIdx;
            PromptProgress = 0f;

            if (plotIdx >= 0)
            {
                FarmPlot plot = World.Farm.Slots[plotIdx];
                PlotAction act = plot.NextAction;

                if (input.RemovePlant)
                {
                    if (plot.State == PlotState.Growing)
                    {
                        // ★ T2：原来先 `FindPlantOnPlot` → `growingPlant.Health.Kill()` 再 `RemoveGrowing()` ✗ ★
                        //   同上：`FindPlantOnPlot` 恒返回 null ⇒ 那两行是**空转** ✓
                        //   ⇒ 删掉不改变行为：铲除的真正实现是 `RemoveGrowing()` ✓
                        plot.RemoveGrowing();
                        Telemetry.Emit("plot_action", ("plot", plot.Index), ("action", "remove"), ("duration", 0f));
                        SetPrompt("已铲除幼苗");
                    }
                    else if (plot.State == PlotState.Grown)
                    {
                        SetPrompt("成株无法铲除（T-04）");
                    }
                }

                // v23：施肥小游戏与 E 耕作动作已删除（攻击即互动）
                if (input.Interact && act != PlotAction.None)
                {
                    // ★ 同帧直读权威字段：`Bag.SelectedSlot` 就是"当前选中种子"（种子袋唯一的权威）
                    //   原来这里写死 `SeedDef sowSeed = null;`（`selected` 取了却没用）⇒
                    //   `BeginPlant(null)` 走 "def == null ⇒ 保留旧值" 的分支 ⇒
                    //   新开垦的地种出来的仍是**上一颗**种子，且旧作物身份一路留到收割 ✗
                    SeedSlot selected = Bag.SelectedSlot;
                    SeedDef sowSeed = selected != null && !selected.IsEmpty ? selected.Def : null;

                    if (act != PlotAction.None)
                    {
                        float required = plot.RequiredTime(act, t.farm);
                        // v6：一次动作同时作用于**同一行相邻的 n 块地**（n = 1 + 对应工具/手套品质，上限 4）
                        int toolIndex = ToolIndexOf(act);
                        int cap = WorkCapability(toolIndex);
                        int groupCount = World.Farm.BuildActionGroup(plot.Index, cap, act);
                        float waterBonus = t.tools.waterBonusPerTier * Mathf.Max(0, ToolLevel[ToolCan]);
                        float fertBonus = t.tools.fertBonusPerTier * Mathf.Max(0, ToolLevel[ToolFertilizer]);

                        bool finished = false;
                        for (int gi = 0; gi < groupCount; gi++)
                        {
                            FarmPlot member = World.Farm.Plot(World.Farm.GroupMember(gi));
                            if (member == null) continue;
                            if (member.TickAction(act, dt, p.AttackSpeed, t.farm, sowSeed, waterBonus, fertBonus))
                                finished = true;
                        }
                        ActiveGroupCount = groupCount;

                        if (t.farm.rootWhileActing) p.MoveInput = Vector2.zero;
                        FarmingThisFrame = groupCount > 0;
                        // 动作进度与生长进度分开上报（HUD 用两条进度条）
                        ActionProgress = Mathf.Clamp01(plot.Progress / Mathf.Max(0.01f, required));
                        ActionName = ActionLabel(act);
                        PromptProgress = ActionProgress;

                        if (finished)
                        {
                            // v10：播下的是树——成熟时自动产出你拥有的每一种种子
                            Telemetry.Emit("plot_action", ("plot", plot.Index), ("action", act.ToString().ToLowerInvariant()),
                                ("duration", required), ("group", groupCount));
                            FarmActionSeconds += required / Mathf.Max(0.05f, p.AttackSpeed);
                            SetPrompt(groupCount > 1 ? $"{ActionText(act)}（同时 {groupCount} 块）" : ActionText(act), 0.6f);
                            ActionProgress = 0f;
                            ActionName = "";
                            PromptProgress = 0f;
                            ActiveGroupCount = 0;
                        }
                    }
                }
            }

            // 三把工具 → 统一为"当前武器"：默认朝鼠标方向，G 锁定最近目标，中键滚轮换武器
            p.MoveInput = input.Move;
            Vector2 aim = input.AimWorld - p.Pos;
            if (aim.sqrMagnitude > 0.01f) p.AimDir = aim.normalized;

            // v10：不再需要锁定按键——始终自动锁定"最近的敌人"

            // v9 自动锁定：只要处于锁定状态，就持续保持"最近的那个敌人"
            //   · 目标死亡 → 改锁最近的
            //   · 出现更近的敌人 → 自动切换
            //   · 全部敌人清空 → 保持锁定开启，等下一个敌人出现自动锁上
            if (p.IsLocked)
            {
                EnemyActor locked = World.FindEnemyById(p.LockTargetId);
                EnemyActor nearest = World.NearestEnemyTo(p.Pos, 0f);
                if (nearest != null && (locked == null || nearest.Id != locked.Id))
                {
                    float dLocked = locked == null ? float.MaxValue : Vector2.Distance(p.Pos, locked.Pos);
                    float dNearest = Vector2.Distance(p.Pos, nearest.Pos);
                    if (locked == null || dNearest < dLocked) locked = nearest;
                    if (locked != null) p.LockTargetId = locked.Id;
                }
                else if (locked == null && nearest == null)
                {
                    p.LockTargetId = -1;   // 场上无人：保持锁定开关，等下一个目标
                }
                if (locked != null)
                {
                    Vector2 toLock = locked.Pos - p.Pos;
                    if (toLock.sqrMagnitude > 0.0001f) p.AimDir = toLock.normalized;
                }
            }

            // v53：滚轮切武器已挪到帧级入口（ConsumeFrameEdges）——它是"相对增量"，
            //      放进固定步循环会被追帧重放多次（一格滚轮 = 5 步 % 3 = 跳过中间那把）。

            // 攻击优先级：
            //   ① 按下鼠标左键 → 朝鼠标位置方向主动攻击（不依赖目标）
            //   ② G 锁定中 → 自动朝锁定目标攻击
            //   ③ 其它情况 → 自动攻击射程内最近的敌人
            // v8：**耕作期间禁止任何攻击**（含左键主动攻击）——"种地 or 打架"必须二选一
            AimWorldLast = input.AimWorld;   // v40：记录鼠标位置

            // ★ v189：短按要兑现 —— `input.Fire`（电平）**或**帧级记下的"按下沿还在窗口内" ★
            //   见 `ConsumeFrameEdges` 那段：高帧率下单帧点按不会再有固定步遇到它；
            //   冷却只剩 ≤0.25s 时点一下也不会再整次被丢弃 ✓
            bool wantsFire = input.Fire || _fireTapLatch > 0f;

            // v90：水壶 = **持续激光**。
            // 必须放在 p.CanAttack 之外 —— CanAttack 里含通用武器冷却，
            // 放在里面就退化成"打一次等一次 CD"，正是要改掉的那个模型。
            // 松开左键（或换武器）时把 _beamFiring 清掉，下次按住重新收起手费。
            if (p.WeaponIndex == PlayerActor.WeaponCan && wantsFire && !FarmingThisFrame && !p.IsDead)
            {
                Vector2 toMousePanel = input.AimWorld - p.Pos;
                if (toMousePanel.sqrMagnitude > 0.0001f) p.AimDir = toMousePanel.normalized;
                TickBeam(p);
                _fireTapLatch = 0f;      // 这次点按已兑现（起手成功与否都算"已处理"，不再重复兑现）
            }
            else
            {
                if (p.WeaponIndex == PlayerActor.WeaponCan) StopBeam();

            if (p.CanAttack && !FarmingThisFrame)
            {
                // v33：**只保留手动攻击**——不按左键就不攻击（自动攻击与自动锁定攻击已删除）
                // v189：`wantsFire` 而不是 `input.Fire`（短按闩，见上）
                bool manual = wantsFire;
                EnemyActor target = null;

                bool fire = false;
                if (manual)
                {
                    Vector2 toMouse = input.AimWorld - p.Pos;
                    if (toMouse.sqrMagnitude > 0.0001f) p.AimDir = toMouse.normalized;
                    fire = true;
                }
                else if (target != null)
                {
                    float range = WeaponRange(p, t) + target.Radius;
                    if (Vector2.Distance(p.Pos, target.Pos) <= range)
                    {
                        Vector2 toTarget = target.Pos - p.Pos;
                        if (toTarget.sqrMagnitude > 0.0001f) p.AimDir = toTarget.normalized;
                        fire = true;
                    }
                }

                if (fire)
                {
                    // ★ v189：**判定与表现同一个门** —— 只有真的出手了才清闩 ★
                    //   于是"表现已发、判定被跳过"这种配对不可能出现：
                    //   `SwingHoe` / `UseTool` 内部是先扣冷却再发事件再结算伤害的整体 ✓
                    _fireTapLatch = 0f;
                    switch (p.WeaponIndex)
                    {
                        // v90：水壶不在这里了 —— 它走上面的持续激光分支
                        case PlayerActor.WeaponFertilizer:
                            UseTool(p, t.tools.fertilizer, true);
                            break;
                        default:
                            SwingHoe(p, t);
                            break;
                    }
                }
            }
            }   // v90：闭合"水壶持续激光 / 其他武器"的 else 分支

            // ★ 选种已挪到本方法**开头**（先结算选种、再判定耕作）—— 见那里的说明 ✓
        }

        /// <summary>本局是否已经起过至少一波（v189：给"清瞬时状态只在开局做"用）。</summary>
        private bool _waveEverStarted;

        /// <summary>
        /// ★★ v192：**整点回血 —— 从"回合遗留的 bug"转成正式机制**（用户需求⑦）★★
        ///
        /// 用户原话："现在每分钟整点会触发一次恢复玩家健康状态，疑似回合的机制遗留成为 bug，
        ///           利用这个 bug 改为机制"。
        ///
        /// ── 它为什么确实是回合遗留 ──
        /// 计时制下 `Clock.WaveIndex > WaveIndex` **每 60 秒必然成立一次**（见 `StartWave` 的说明）
        /// ⇒ `StartWave` 每分钟跑一次，而它末尾调用本方法 ⇒
        /// 那句 `Heal(MaxHp)` + `RefillShield()` 就从"一回合开始补满状态"退化成
        /// "玩家什么都没做，整点白回满一次" ✓ 那不是 bug 的**数值**，是**语义**：
        /// 行为本身玩家觉得有意思（用户明确要求保留），所以要把它写进表、写进事件、能被预期 ✓
        ///
        /// ── 改了什么（三件，金额一个没变）──
        ///   ① 回血量进表：`tuning.player.minuteHealPercent`（默认 **1.0** ⇒ `Heal(MaxHp × 1.0)`
        ///      = 与改前**逐位等价**；0.25 = 每分钟回四分之一；0 = 不回血）
        ///   ② 广播 `MinuteHealEvent`：表现层订阅它播**全屏回血特效**（本方法不认识任何特效）✓
        ///   ③ 语义写清楚：这是"整点奖励"，不是"新回合开场"——
        ///      过程量（`WeaponCds` / `SwitchWindup`）**仍然不清零**（v189 的结论，见下面那段）✓
        ///
        /// ★ 数值（本版默认值下的确切读数）★
        ///   生命：`Heal(100 × 1.0)` ⇒ **回满到 100/100**（受伤到 40 的话回 +60）
        ///   护盾：`RefillShield()` ⇒ **补满到 20/20**（与生命同一句"健康状态恢复"）
        ///   无敌帧 `InvulnRemaining = 0`、`HitFlash = 0`、`SinceDamaged = 999`（这三项是既有行为）✓
        ///
        /// ★★ v193（本版）：**第 1 波不发** ★★
        ///   用户："每分钟触发一次的回血在0分钟（初始）不触发，增加条件判断"。
        ///   判据 `WaveIndex &lt;= 1`（= 本局第 1 波）—— **不是** `Clock.GameTime &gt; 0`：
        ///   `Prepare` 阶段会吃掉真实时间（实测 `StartWave(1)` 落在 t≈2.5s），
        ///   用时钟判据会在开局照样回一次血 ✗ 详见方法体。
        ///   回血 / 回盾 / 事件**一起拦**（开局本来满血满盾 ⇒ 拦它不改变任何可观察结果）；
        ///   `InvulnRemaining` / `HitFlash` / `SinceDamaged` 三句不拦（它们不是"补满血"）。
        ///   读数：t=0 事件 0 次、全屏特效 0 次；t=60s 事件 1 次、特效 1 次 ✓
        /// </summary>
        private void ResetPlayerForNewWave()
        {
            PlayerActor p = World.Player;
            if (p == null) return;

            // ★★ v193：**整点回血从第 2 波（t=60s 之后的第一次跨分）起算，开局第 1 波不发** ★★
            //   （用户原话："每分钟触发一次的回血在0分钟（初始）不触发，增加条件判断"）
            //
            // ── 判据：**本局第 1 波** ★ 而**不是**"时钟 > 0" ★ ──
            //   我先写的候选是 `Clock.GameTime > 0f`（理由：整点 = 跨过 60 秒边界，t=0 没跨过）。
            //   **实测把它否掉了**：开局阶段是 `PhaseState.Prepare`，而 `TickPrepare` 要等
            //   `input.InitialChoice`（`tuning.shop.infinitePrepare = true` ⇒ 它会一直等玩家）
            //   ⇒ 真正那一句 `StartWave(1)` 落在**玩家选完初始种子之后**，
            //   上一局实测 t≈2.5s（10:28 那局的日志："[Seedfall] 回合 1 清理瞬时状态"，
            //   栈是 `TickPrepare` → `StartWave`）✗
            //   ⇒ 用时钟判据时 `GameTime` 早就 > 0 ⇒ **开局照样回一次血**，判据跑偏了 ✗
            //   "第 1 波"则是本局的**语义起点**：与 Prepare 被玩家拖了 2 秒还是 5 分钟无关 ✓
            //   `WaveIndex` 在 `StartWave` 的**第一句**就被写成 `index`（见那边 `WaveIndex = index;`），
            //   所以进到本方法时它已经是"这一次的波号" ⇒ 第 1 波判定只要 `<= 1` ✓
            //   （新一局 `ResetPlayerForNewWave` 之前 `WaveIndex` 会被写回 0，见 `Reset`：
            //     `WaveIndex = 0;` ⇒ 重开一局同样是第 1 波，同样不回血 ✓）
            bool isFirstWaveOfRun = WaveIndex <= 1;

            // ★ v192：回血量改成表值（`EffectiveMinuteHealPercent` 钳在 0..1；默认 1.0 = 回满）★
            float hpBefore = p.Health.Hp;
            float shieldBefore = p.Health.Shield;
            float healPct = T.EffectiveMinuteHealPercent;
            // ★★ "补满血"与"发事件"**一起拦**，当成同一个"整点奖励"整体 ★★
            //
            // ── 为什么不只拦事件、让补血照旧 ──
            //   开局那一刻玩家**本来就是满的**：`CreatePlayer` 里
            //   `player.Health.Setup(Stats.MaxHp, Stats.Shield)`，而 `HealthPool.Setup` 默认
            //   `refill = true` ⇒ 进这一行时 `Hp == MaxHp`、`Shield == MaxShield` ✓
            //   ⇒ t=0 这两句本来就是**空操作**（`Heal` 涨 0 点，连 "+N" 弹字都不会发 ——
            //      `HealthPool.Heal` 里 `gained >= 1f` 才弹）✗
            //   ⇒ 所以"拦住它"**不改变任何可观察结果**，却能让代码与语义一致：
            //      不会出现"事件没发、血却被补了一次"这种**一半状态**（那正是以后被人当 bug 报的形状）✓
            //   ⚠️ 反过来说：如果哪天开局改成"残血继承"，这条判据会**真的**让开局不回满 ——
            //      那也仍然是对的读数，因为"整点回血"在任何数值下都不该在开局那一波发生 ✓
            if (!isFirstWaveOfRun)
            {
                if (healPct > 0f) p.Health.Heal(p.Health.MaxHp * healPct);
                p.Health.RefillShield();
            }
            // ★ 下面三句**不跟着拦** ★
            //   它们不是"补满血"，而是"清掉瞬时受击状态"：`InvulnRemaining` / `HitFlash` /
            //   `SinceDamaged`。三者在新一局的开局值本来就是干净的
            //   （`SinceDamaged` 的字段初值就是 `999f`，见 `Actors.cs`）
            //   ⇒ 留在这里一字不动，改动的爆炸半径就只有上面那两句回血 ✓
            p.InvulnRemaining = 0f;
            p.HitFlash = 0f;
            p.SinceDamaged = 999f;
            // ★ v189：**不再清零 `WeaponCds` 与 `SwitchWindup`** ✗ ★
            //
            // ── 用户原话 ──
            //   "回合机制残留，导致玩家攻击突然中断再触发（导致攻速异常）"
            //
            // ── 病因（实测读数，改前每次跨分都出现）──
            //   `DebugApi.ProbeMinute` 打出的跨分前后读数：
            //     1→2 分钟：锄头 CD **0.5833 → 0.0000**
            //     2→3 分钟：锄头 CD **0.6000 → 0.0000**
            //   句柄就是本方法末尾那句 `for (…) p.WeaponCds[i] = 0f;` 与 `SwitchWindup = 0f`。
            //
            //   为什么它会表现成"攻击中断再触发"：
            //     玩家按住左键时，攻击节奏由 `WeaponCds[Hoe]` 决定（0.7s ÷ 攻速）。
            //     跨分那一帧 CD 被写成 0 ⇒ `CanAttack` 立刻放行 ⇒
            //     **下一刀比正常早 0.58~0.60 秒出现**，然后 CD 又被拉回 0.7s
            //     ⇒ 视觉上就是"一刀突然提速、接着又卡回去" ✓
            //   这与"位置被重置"（v178 已删）是同一个病根：**回合制的字段在计时制下还在被读** ✓
            //
            // ── 为什么保留上面的回血/回盾 ──
            //   "每分钟补满"是**现行设计**（`DeathDiagnostics` 的"分钟边界"一节明确写着），
            //   用户没有点它 ✓ 而"冷却"是**过程量**，不是一局重开就该送的资源 ✗
            //   ⇒ 只删过程量那两句，回血/护盾/无敌帧一字不动 ✓
            // ★ v192：上面那句"现行设计"**升级成正式机制**（用户需求⑦）—— 回血量进
            //   `tuning.player.minuteHealPercent`，并且**广播 `MinuteHealEvent`** 给表现层
            //   播全屏回血特效（见方法头那段）✓ 数值仍是"回满"，所以这一节的结论不变 ✓
            // ★ v193：与上面那两句回血**同一道闸** —— 第 1 波不发事件 ★
            //   ⇒ 表现层（`WorldView.OnMinuteHeal` → `StartHealFx`）本来就只订阅这一个事件，
            //     于是"开局第一次全屏回血特效"也跟着没有了 ✓ 这正是用户要的：
            //     特效是"整点机制在跑"的读数，而第 1 波没有整点可跑 ✗
            if (!isFirstWaveOfRun)
            {
                Bus.Publish(new MinuteHealEvent
                {
                    Wave = WaveIndex,
                    HealHp = p.Health.Hp - hpBefore,
                    HealShield = p.Health.Shield - shieldBefore,
                    Hp = p.Health.Hp,
                    MaxHp = p.Health.MaxHp,
                    Pos = p.Pos
                });
            }
            Telemetry.Emit("player_reset", ("wave", WaveIndex), ("hp", p.Health.Hp), ("shield", p.Health.Shield));
        }

        /// <summary>供 UI/自动化调用：设置提示文案。</summary>
        public void SetPromptPublic(string text, float hold = 1.2f) => SetPrompt(text, hold);

        /// <summary>供自动化/调试调用：等价于鼠标点击种子袋槽位。</summary>
        public void HandleBagClickPublic(int slot) => HandleBagClick(slot);

        /// <summary>种子袋点击：点一下**只选中**该槽位。合成走种子卡上的"合成"按钮（同种同级 → 等级 +1）。</summary>
        private void HandleBagClick(int slot)
        {
            if (slot < 0 || slot >= Bag.SlotCount) return;
            if (Bag[slot].IsEmpty)
            {
                MergeSelected = -1;
                SetPrompt("空槽位");
                return;
            }
            // v7：点击槽位 = 切换当前种子；合成统一走商店的"合成"按钮或自动合成
            Bag.Selected = slot;
            MergeSelected = slot;
            SetPrompt($"当前种子：{Bag[slot].Label}");
        }
        /// <summary>当前武器的攻击距离（用于自动攻击的射程判定）。</summary>
        private float WeaponRange(PlayerActor p, TuningDef t)
        {
            float mult = Mathf.Max(0.1f, p.RangeMult);
            switch (p.WeaponIndex)
            {
                case PlayerActor.WeaponCan: return t.tools.can.range * mult;
                case PlayerActor.WeaponFertilizer: return t.tools.fertilizer.range * mult;
                default: return t.tools.hoe.range * mult;
            }
        }

        private static string ActionText(PlotAction act)
        {
            switch (act)
            {
                case PlotAction.Dig: return "掘地完成";
                case PlotAction.Sow: return "播种完成";
                case PlotAction.Water: return "已洒水（生长 -30%）";
                case PlotAction.Fertilize: return "已施肥（生长 -50%）";
                default: return "";
            }
        }

        /// <summary>动作名（进度条标题用）。</summary>
        private static string ActionLabel(PlotAction act)
        {
            switch (act)
            {
                case PlotAction.Dig: return "掘地";
                case PlotAction.Sow: return "播种";
                case PlotAction.Water: return "洒水";
                case PlotAction.Fertilize: return "施肥";
                default: return "";
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // 插件在"命中之后"的机制 —— 三条攻击路径**共用这一份**
        //
        // ★ 为什么必须共用 ★
        // 灼烧 / 减速 / 吸血这三条，锄头、水壶、肥料都要能触发 ✗
        // 若各写一份，"同一件插件插在不同武器上效果不同"就会变成隐式约定，
        // 而且改一处必漏两处 ✓（本文件里已经有三处重复的激光判定代码作为前车之鉴）
        //
        // ★ 为什么放在命中**之后** ★
        // 它们不改伤害公式，而是挂"副作用"：挂一层持续效果、回一点血 ✓
        // ★ 阶段 3 起，"数值区"整体不存在了 ★ ⇒ 这里就是插件的**全部**战斗影响里
        // 与伤害有关的那一半（另一半是"多打谁"，在三条攻击路径里）✓
        // ════════════════════════════════════════════════════════════════════════

        // ════════════════════════════════════════════════════════════════════════
        // ★ 用户需求②：剑气流**不重叠**的扇形分布 ★
        // （改前：2 条 = ±6°、3 条 = −12/0/+12 —— 你说"重叠"，是对的）
        //
        // 旧值 12°/条 是**拍出来的**：两条剑气在参考距离 6.65m 处的横向距离只有
        //   2 × 6.65 × sin(6°) = 1.39m，而弯月的宽度是 2 × 2.911 = 5.82m
        // ⇒ 屏幕上的三条弯月**大部分叠在一起**（只是中心线岔开了几度）✗
        //
        // 现在把"不重叠"写成**判据**，角度由判据**算出来**，不再填常数：
        //
        //   判据：同一距离 d 上，两条相邻剑气的**横向距离 ≥ 弯月宽度**
        //         横向距离 = 2 · d · sin(Δθ/2)     （两发同源、夹角 Δθ）
        //         弯月宽度 = 2 · HalfWidth          （= 2 · MeleeBoxHalfWidth(range, arc)）
        //   ⇒ 2·d·sin(Δθ/2) ≥ 2·HalfWidth
        //   ⇒ **Δθ ≥ 2 · asin( HalfWidth / d )**
        //
        //   参考距离 d 取**框心飞到终点的距离**（派生值，不是拍的数）：
        //         d_ref = HalfLen + Dist = range·0.5 + range·SwordWaveDistMult = **1.75·range**
        //
        //   ⇒ Δθ_min = 2·asin( range·sin(arc/2) / (1.75·range) ) = 2·asin( sin(arc/2) / 1.75 )
        //
        //   ★ 它与射程**无关**（range 上下约掉了）★ —— 只由近战张角 arc 决定：
        //     arc = 100° ⇒ sin50° = 0.76604 ⇒ asin(0.43774) = 25.96° ⇒ Δθ = **51.9°**
        //     "攻击范围属性"涨上去以后弯月变宽、飞行距离也按同一比例变长
        //     ⇒ 需要的角度**不变**，扇形自相似 ✓（写死一个常数做不到这一点）
        //
        //   余量 `SwordWaveNoOverlapMargin = 1.05`：判据取等号时两片弯月**刚好相切**，
        //   浮点与像素都不吃相切 ⇒ 留 5%：Δθ = 2·asin(1.05·sin(arc/2)/1.75) = **54.72°** ✓
        //   实测读数（arc=100°、range=3.8m）：d_ref = 6.65m，
        //     横向距离 = 2 × 6.65 × sin(27.36°) = **6.114m** ≥ 弯月宽度 2 × 2.911 = **5.822m** ✓
        //   ★ v273：上面这两行是 `range = 3.8` 时的读数；`tools.hoe.range` 已按 0.90 缩到 **3.42**
        //     ⇒ d_ref 6.65 → **5.985m**、横向 6.114 → **5.503m**、弯月宽 5.822 → **5.240m**
        //     （判据对 range 是**自相似**的 ⇒ 比值与不等式方向一个都不变）✓
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>不重叠判据的余量：横向距离 = 弯月宽度 × 本值（1.05 = 留 5%）。</summary>
        public const float SwordWaveNoOverlapMargin = 1.05f;

        /// <summary>
        /// 相邻剑气的**最小夹角**（度）—— 由"横向距离 ≥ 弯月宽度"这条判据**算出来**，
        /// 参考距离 = 框心飞到终点的距离（推导见上面那一段）。
        /// </summary>
        public static float SwordWaveSpreadDeg(float meleeRange, float meleeHalfWidth)
        {
            float dRef = Mathf.Max(0f, meleeRange) * (0.5f + SwordWaveDistMult);   // = 1.75 × range
            if (dRef <= 1e-4f) return 90f;                                         // 退化：没有射程就没有方向
            float ratio = Mathf.Clamp01(Mathf.Max(0f, meleeHalfWidth) * SwordWaveNoOverlapMargin / dRef);
            return 2f * Mathf.Asin(ratio) * Mathf.Rad2Deg;
        }

        // ════════════════════════════════════════════════════════════════════════
        // ★ v185：剑气 = **飞行子弹**（不再是"瞬时扫一条矩形"）★
        //
        // 用户原话："剑气视为子弹类（与近战攻击相同的判定框，远程发射）"
        //        "剑气的飞行距离与攻击范围有关"
        // ⇒ 三个数决定全部行为，且**都用系数表达、不写死米数**：
        //     · 判定框  = 与近战框**同一个** `MeleeBoxHalfWidth(range, arc)` + 框长 = `range`
        //                （框长/半宽都是 `range` 的函数 ⇒ 调射程时框自动跟着变）
        //     · 飞行距离 = `攻击范围 × SwordWaveDistMult`
        //     · 速度     = `SwordWaveSpeed`（定值）⇒ **存活时间 `life = 飞行距离 / 速度`**（派生，不是常数）
        //
        // ★ `SwordWaveDistMult = 1.25` 是怎么来的（量级参考，**不是**硬编码的米数）★
        //   需求给的量级参考是"剑气初始飞行范围 ≈ 水壶射程的一半"。
        //   水壶基础射程 9.5m（`tuning.json: tools.can.range`）× 0.5 = 4.75m（量级参考，不引用）
        //   锄头基础射程 3.8m（`tools.hoe.range`）⇒ 系数 = 4.75 / 3.8 = **1.25** ✓
        //   ★ v273：`tools.hoe.range` 已按 0.90 缩到 **3.42**（4.75/3.42 = 1.389），但本系数
        //     **保持 1.25 不动** —— 用户只要求"缩小锄头的初始攻击范围和特效范围"，
        //     改它等于顺手把剑气飞得更远（方向相反）✗ 副作用：剑气飞行距离随 range 一起缩小
        //     8.9%（3.42×1.25 = 4.275m，改前 4.75m），这是"射程是它的上游"的必然结果 ✓
        //   所以这里存的是**比值**：射程翻到 7.0m 时飞行距离自己变成 8.75m，
        //   而不是继续飞 4.75m（那才是写死）✓
        //   ⚠️ 4.75 这个数字**只出现在这段注释里**，代码里一次都没有 ✓
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>**飞行距离 = 攻击范围 × 本系数**。见上面那段推导（量级参考：水壶射程的一半）。</summary>
        public const float SwordWaveDistMult = 1.25f;

        /// <summary>
        /// 剑气**飞行速度**（米/秒）。取 18：与水壶出膛速度（`tools.can.projectileSpeed = 18`）
        /// 同一量级 ⇒ 玩家读起来是"和激光一样快地飞出去"的一发，而不是一团慢慢飘的气。
        /// ★ 它是定值，但**存活时间不是**：`life = 飞行距离 / 速度`（随攻击范围变化）✓
        /// </summary>
        public const float SwordWaveSpeed = 18f;

        /// <summary>
        /// 子步进上限（秒）。`DebugAdvanceAttack(0.5f)` 与掉帧的大 `dt` 都会从这里被切开，
        /// ⇒ 判定与"每帧判一次"的实时表现**逐帧一致**，也不会因为一步跨过整个框而漏判（隧穿）✓
        /// </summary>
        private const float SwordWaveMaxStep = 1f / 60f;

        /// <summary>判定框的**前向半长** = 攻击范围 / 2 ⇒ 框长 = 攻击范围（与近战框同长）。</summary>
        public static float SwordWaveHalfLen(float range) => Mathf.Max(0f, range) * 0.5f;

        /// <summary>**飞行距离**（米）—— 由攻击范围派生，与 <see cref="SwordWaveDistMult"/> 成正比。</summary>
        public static float SwordWaveDistance(float range) => Mathf.Max(0f, range) * SwordWaveDistMult;

        /// <summary>**寿命**（秒）= 飞行距离 / 速度。它是**派生值**，不是第四个常数 ✓</summary>
        public static float SwordWaveLife(float range) => SwordWaveDistance(range) / SwordWaveSpeed;

        /// <summary>
        /// 剑气**能打到的正前方最远处**（米）。
        ///
        /// ★ 注意它是 `攻击范围 + 飞行距离`，不是 `飞行距离` ★
        /// 框心从 `半长` 飞到 `半长 + 飞行距离`，而判定用的是**整个框**
        /// （`|along − 框心| ≤ 半长`）⇒ 前缘最远 = `2 × 半长 + 飞行距离 = 攻击范围 + 飞行距离` ✓
        /// 这个数拿来和"改前 3.80m"对齐读数（见 `DebugApi.ProbeWaveFlight` 的对应表）✓
        /// </summary>
        public static float SwordWaveReach(float range) => range + SwordWaveDistance(range);

        /// <summary>
        /// **近战判定框的横向半宽**（米）= 近战扇形的外接矩形半宽 = `range × sin(arc/2)`。
        ///
        /// ★ v181：剑气的判定宽度就取这个值 ★
        /// 需求原话是"判定大小与近战攻击判定框大小一致"，那么它必须是**同一个式子**，
        /// 不能再另写一个常数 —— 旧版写死 `0.9f`，而近战框是 `3.2 × sin(50°) = 2.45m`，
        /// 剑气只有近战框的 **37%** 宽 ⇒ 玩家看到的就是"一条细线"，细到打不到人 ✗
        /// 抽成公共函数还有一个作用：将来调 `tools.hoe.arc` 时剑气会**自动**跟着变宽变窄 ✓
        /// </summary>
        public static float MeleeBoxHalfWidth(float range, float arcDeg)
            => Mathf.Max(0f, range) * Mathf.Sin(Mathf.Clamp(arcDeg, 0f, 180f) * 0.5f * Mathf.Deg2Rad);

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v205：锄头的「**判定** / **特效**」几何契约 ★★
        //
        // 用户原话："调整锄头实际攻击范围，用扇形判定框覆盖特效，且实际范围大于特效一点"
        //
        // ★ 先把**改前**的事实说清楚（两个方向，一个是反的）★
        //   · **判定**（`DoHoeSwing`）：`dist ≤ range + e.Radius` ＋ `|夹角| ≤ arc/2`
        //     —— 那两行本来就是"**圆 ∩ 方向锥 = 扇形**"，不是矩形走廊；
        //        `MeleeBoxHalfWidth` 只被**剑气**用（`FireSwordWaves`），锄头近战从不走它 ✓
        //     —— 而 `e.Radius` **已含** `HitRadiusScale = 1.486`：
        //        `Actors.cs:1671` `e.Radius = Actor.ScaledRadius(def.Radius)`
        //        ⇒ 实际命中距离 = `range + radius × 1.486`（`grunt` 0.36 ⇒ **4.335m**）✓
        //   · **特效**（`WorldView.FxBladeMesh`）：径向 = `e.Range`（今天 **3.8m**）、
        //     张角 = 网格里烘死的 `±55°` = **110°**
        //     ⇒ **径向短了**（3.8 < 4.335）、**角向宽了**（110° > 2×50° = 100°）✗
        //     一个宽、一个短 ⇒ 站在弧边缘的怪"看着被劈到却不掉血" ✓ 这就是本轮的病灶
        //
        // ★ 改后：视觉**在两个方向都进到判定里面** ★
        //   · 径向：`视觉半径 = 判定半径 × 0.95` ⇒ 3.8 → **4.09m**（判定最小的敌人在 4.305）✓
        //   · 角向：`视觉张角 = 判定张角 − 4°` ⇒ 100° → **96°**（网格 `halfDeg` 55° → 48°）✓
        //   两个方向都留 5% / 2°⇒ 用户要的"**大于特效一点**"是一个**可量的数**，不是手感 ✓
        //
        // ★ 为什么视觉半径里要含一个"敌人半径"★
        //   判定是**圆心到圆心**的 `range + e.Radius` —— 敌人越胖，它圆心站得越远还挨打 ✓
        //   而**视觉只有一条弧**，不可能按每个敌人的胖瘦各自变粗 ✗
        //   ⇒ 取**表里最瘦的敌人**当参考（`shooter radius 0.34`）：
        //     于是"视觉 < 判定"对**每一个**敌人都成立，且对最瘦的那个余量最小（= "一点"）✓
        //   ⇒ 换了更瘦的敌人这张表就变红（`SelfCheck` 的 `HoeReachChecks` 逐敌人验）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>**视觉半径 / 判定半径**（用户需求："实际范围大于特效一点"）。</summary>
        public const float MeleeFxRadiusScale = 0.95f;

        /// <summary>**视觉张角 = 判定张角 − 本值**（度）。"大一点"要在**两个方向**上都成立。</summary>
        public const float MeleeFxArcShrinkDeg = 4f;

        /// <summary>
        /// 视觉半径的**参考敌人半径**（`enemies.json` 的**未缩放**原值）
        /// = 表里最瘦的 `shooter 0.34`。
        /// 唯一作用是把这个"一条弧"对齐到"一组按敌人胖瘦变化的判定半径"的**下界** ✓
        /// </summary>
        public const float MeleeFxRefBaseRadius = 0.34f;

        /// <summary>
        /// **判定半径**（米）= `攻击范围 + 敌人半径 × HitRadiusScale`。
        /// 调用方传 `e.Radius`（`Actor.ScaledRadius` 之后的值，已含 1.486）⇒ 不要再乘一次 ✓
        /// </summary>
        public static float MeleeJudgeRadius(float range, float scaledHitRadius)
            => Mathf.Max(0f, range) + Mathf.Max(0f, scaledHitRadius);

        /// <summary>**判定半张角**（度）= `arc / 2`（`tuning.tools.hoe.arc = 100` ⇒ **50°**）。</summary>
        public static float MeleeJudgeHalfArcDeg(float arcDeg)
            => Mathf.Clamp(arcDeg, 0f, 180f) * 0.5f;

        /// <summary>
        /// **视觉半径**（米）= `判定半径(参考敌人) × 0.95`。
        /// `range = 3.42` ⇒ `(3.42 + 0.34 × 1.486) × 0.95` = **3.729m**（v273 前 `range 3.8` ⇒ 4.090m）✓
        /// ★ v273：这个数的**唯一上游**是 `tuning.json tools.hoe.range`（3.8 → 3.42，×0.90）
        ///   ⇒ 它一缩，`WorldView` 主弧、`AttackVfx` 26 片、判定外径**同时**缩，天生同步 ✓
        /// </summary>
        public static float MeleeFxRadius(float range)
            => MeleeJudgeRadius(range, MeleeFxRefBaseRadius * Actor.HitRadiusScale) * MeleeFxRadiusScale;

        /// <summary>
        /// **视觉张角**（度）= `判定张角 − 4°`。`arc = 100` ⇒ **96°**（= `WorldView.FxBladeMesh` 的 `2 × 48°`）。
        /// ★ 网格那边必须同步：`WorldView.cs` 的 `halfDeg` 与本函数的 `arc/2` 是**同一个数** ✓
        /// </summary>
        public static float MeleeFxArcDeg(float arcDeg)
            => Mathf.Max(8f, Mathf.Clamp(arcDeg, 0f, 180f) - MeleeFxArcShrinkDeg);

        /// <summary>
        /// ★ v184：**"圆心重合"的判定距离**（米）—— 小于它就跳过扇形角度判定、直接算命中 ★
        ///
        /// 为什么需要这个数：`Vector2.normalized` 对零向量返回**零向量**（Unity 规定），
        /// 于是"敌人压在玩家圆心"时点积恒为 0 ⇒ 永远落在扇形外 ⇒ **贴脸的怪打不动** ✗
        /// 取 5cm：小于玩家与敌人半径（0.67m）两个数量级，
        /// 所以它只吃"真的重合"这一种退化，不会把"侧面擦过去"的怪误判成命中 ✓
        /// </summary>
        public const float MeleeCoincidentDist = 0.05f;

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v222：近战判定 = **以玩家为圆心、内半径 0、总张角 180° 的半圆** ★★
        //
        // 用户原话：「修改锄头攻击范围内直径，侧面范围包裹角色，
        //            整体攻击范围呈现接近半圆」
        //
        // ★ 从 v221（圆环扇区：内 0.55 / 总 100°）到这一版，**只改形状两处** ★
        //   ① 内半径 0.55 → **0**（"内直径归零"：去掉贴脸死区）
        //   ② 半张角 50°（由 `hoe.arc` 推导）→ **固定 90°**（总张角 100° → **180°**）
        //   其余（伤害、去重、名额、窗口、`PluginPostHit`）**一个字未动** ✓
        //
        // ★ 单一判据（`MeleePointHit` 里只有这一条式子）★
        //     命中 ⇔ dist ≤ MeleeCoincidentDist                  // 圆心重合例外（v184 原样保留）
        //          ∨ (dist ≥ MeleeRingInner − r                  // 内沿（内半径 0 ⇒ 恒真）
        //             ∧ dist ≤ MeleeRingOuter(range) + r         // 外沿（**吃 r**）
        //             ∧ Dot(delta/dist, dir) ≥ cos(MeleeRingHalfArcDeg))
        //   `r` = 敌人半径（`e.Radius`，`Actors.cs` 写入时**已含** `HitRadiusScale 1.486`）
        //
        // ★ 为什么内半径取 0（用户要的"侧面包裹角色"）★
        //   v221 的 0.55 是为了让"环"真的有个内径；用户这次明确要**去掉它**：
        //   "内直径归零" ⇒ 判据里 `dist ≥ 0 − r` 对任何 `dist ≥ 0` **恒真**
        //   ⇒ 敌人贴在玩家身侧（夹角 ±90°）也照样命中，不再有"站在脸上反而打不着" ✓
        //   ★ 式子保留、不删 ★：形状参数仍留在判据里（改回带内径的环只需动 `MeleeRingInner` 一处），
        //     注释写清"内半径 0 ⇒ 这一条恒通过"的语义（不是忘了删的死代码）。
        //
        // ★ 为什么半张角**写死 90°**，不再由 `hoe.arc` 推导 ★
        //   用户要的是"接近半圆"这个**形状**，而 `hoe.arc = 100` 是武器表里的**特效**参数
        //   ⇒ 两者解耦：判定 = 180° 半圆，特效 = 96°（且只实铺 60% ≈ 57.6°）。
        //   **判定比视觉大是故意的**：判定负责手感（挥一下，身侧的人也被扫到），
        //   视觉只做引导（玩家看到的是"这一下扫出去的方向"，不是判定的边界）✓
        //
        // ── ★ v273 判定形状表（`tuning.tools.hoe.range = 3.42`、`arc = 100`、`AtkRange = 1.0`）★ ──
        //   表里的 `range` 由 **3.8 → 3.42（×0.90）**（"按比例略微同步缩小初始攻击范围"），
        //   所以下表与 v222 那一版是**同比例 ×0.9117**（不是 ×0.90：`MeleeFxRadius` 里
        //   `最瘦敌人 0.34 × HitRadiusScale 1.486 = 0.505` 这一项**不随 range 缩**）✓
        //
        //   量                  来源式                                    现行 v273       v222 当时
        //   ──────────────────────────────────────────────────────────────────────────────────
        //   特效中心半径 R      MeleeFxRadius(range)                        3.729 m        4.090 m
        //   特效外沿            1.05R（= BladeR1 1.00 + Depth/2 0.05）      3.915 m        4.294 m
        //   判定外径            3.915 × 1.15                              **4.503 m**    **4.939 m**
        //   ▶ 外径 / 特效外沿                                               1.150 ✔        1.150 ✔
        //   ▶ `max(…, range)` 的 `range` 项顶不顶住                          不顶住         不顶住
        //     （3.42 < 4.503 ⇒ 判定仍由"特效外沿 × 1.15"决定，没被 clamp 住）  ✓              ✓
        //   判定内半径          MeleeRingInner                             **0.000 m**    0.000 m
        //   判定半张角          MeleeRingHalfArcDeg                        **90.0°**      90.0°
        //   判定总张角                                                      **180.0°**     180.0°
        //   覆盖面积            π·外径²/2                                    **31.85 m²**   38.31 m²
        //   特效实拍半张角      (96°/2) × 0.60 + 1.152°                     29.952°        29.952°
        //   ▶ 判定半张角 / 特效半张角                                        3.005          3.005
        //   ──────────────────────────────────────────────────────────────────────────────────
        //   ⇒ 判定外径 / 视觉半径 / 特效外沿 **三者同比例**（都挂在 `range` 这一棵树上）；
        //     半圆张角 90°、内半径 0、片数 26、贴图、配色**一个字节没动**（用户只要求缩范围）✓
        //
        // ★ v273 镜像纪律（两个文件里写的是**同一个数** 0.90）★
        //   「初始攻击范围」与「特效范围」的 0.90 只落 **一处**：`tuning.json: tools.hoe.range`。
        //   视觉侧的 `AttackVfx.BladeR0 / BladeR1 / bandD` 是**比例**（不是米），
        //   它们吃的是已经缩小的 `e.Range`（= 本文件的 `MeleeFxRadius`）⇒ 那边**不许**再乘 0.90：
        //   再乘一次会让 26 片的外沿变成 0.945R，缩进 `WorldView.FxBladeMesh` 的主弧（1.00R）里，
        //   并把"判定 / 最宽视觉"从 1.15 打到 1.087 ✗
        //   （`MeleeFxBladeR1 ↔ AttackVfx.BladeR1`、`MeleeFxSliceDepthRatio ↔ AttackVfx.bandD`
        //     在两边注释里互相点名 ⇒ 改任意一边都必须同步另一边，不然"判定包住特效"就断了 ✓）
        //
        // ★ v273：下面这些形状结论（张角 / 内半径 / 侧面命中）**一个数都没动** ★
        //   ⇒ 180° 的半圆里，**只有正中约 58° 有画面**（60% 实铺），其余 122° 是"判定有、视觉无"。
        //   ▶ **侧面任意距离可命中**：夹角恰为 ±90° 时 `Dot = 0`，判据是 `Dot ≥ cos(90°)`；
        //     float 里 `cos(90° × Deg2Rad) = −4.37e-8`（**不是** 0）⇒ `cosHalf` 在
        //     `MeleePointHit` 里被 **钳到 0**（`Mathf.Max(0f, …)`）
        //     ⇒ 90° 整这一点**正好落在边界内侧**，不会被浮点误差切掉 ✓
        //   对照 v221（旧形状）：内 0.550 m / 总 100° / 覆盖 21.02 m² ⇒ 面积 ×1.82
        //
        // ★ 未动 ★：伤害数值、`PluginPostHit` 的调用位置与参数、
        //   `_meleeHitIds` / `_meleeWinHitIds` 去重、`_meleeWinHitsLeft` 名额、窗口机制、
        //   `MeleeJudgeRadius` / `MeleeJudgeHalfArcDeg` / `MeleeBoxHalfWidth`（剑气与探针还在用）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ v222：**判定内半径**（米）= **0** —— 没有贴脸死区。
        ///
        /// 判据里那一条（`dist ≥ MeleeRingInner − r`）**保留**，内半径 0 时它**恒真**
        /// （`dist ≥ 0` 且 `r ≥ 0` ⇒ 恒有 `dist ≥ −r`）⇒ 侧面贴身也命中；
        /// 而"环"这个形状参数仍留在判据里（改回带内径的环只需改**这一个数**）。
        /// v221 的 0.55 已按用户要求归零（"内直径归零，侧面范围包裹角色"）✓
        /// </summary>
        private const float MeleeRingInner = 0f;

        /// <summary>
        /// ★ v221：**判定外径 / 特效外沿** 的比值（用户需求："放大判定、包裹特效"）★
        /// `MeleeRingOuter = MeleeFxOuterEdge(range) × MeleeRingOuterScale`（且不小于 `range`）。
        /// </summary>
        private const float MeleeRingOuterScale = 1.15f;

        /// <summary>
        /// ★ v222：**判定半张角**（度）= **90°（固定）** ⇒ 总张角 **180°**，"接近半圆"。
        ///
        /// 用户要求：「整体攻击范围呈现接近半圆」。
        /// ★ 不再由 `hoe.arc` / 特效半张角推导 ★（v221 的 `max(arc/2, 特效半角 × 1.15)` 整段删除）：
        ///   · `hoe.arc = 100` 是**特效**参数，用户要的是**形状** ⇒ 两者解耦
        ///   · 特效只实铺约 57.6°（半角 29.952°）⇒ 判定是它的 **3.005 倍**
        ///   · **判定比视觉大是故意为之**：判定容纳手感，视觉只做引导 ✓
        ///   · 正因为不再依赖 `hoe.arc`，`arcDeg` 这个形参从 `MeleePointHit` / `OpenMeleeWindow` /
        ///     `MeleeHitScan` 三处，以及窗口里那份"记下的张角"字段，**一并删除**
        ///     （否则它们全是"只写不读"的死参数/死字段）✓
        /// </summary>
        public const float MeleeRingHalfArcDeg = 90f;

        /// <summary>
        /// ★ v221：特效弧**每一片**的径向长 = `0.10 × 视觉半径`
        /// （`AttackVfx.SpawnSwing` 的 `bandD = Max(0.05, R × 0.10)`）★
        /// 片被摆在自己的半径上、`Depth` 是它的径向长度 ⇒ 片的**外沿**比中心半径多出**半个 Depth**。
        /// 不把这 5% 算进"特效外沿"，判据就会静默少包 5%（而这 5% 正好是外缘羽化带）。
        /// ★ v273：它是**比例**（不是米）⇒ 视觉侧 0.90 的缩小已由 `range`（3.8 → 3.42）承担，
        ///   这里和 `AttackVfx.bandD` **都不许**再乘 0.90（理由见上面「v273 镜像纪律」）✓
        /// </summary>
        private const float MeleeFxSliceDepthRatio = 0.10f;

        /// <summary>
        /// ★ v221：特效薄带的**最大半径比** = `AttackVfx.BladeR1`（= 1.00R）★
        /// 必须与那边同步：薄带外径一改，这里"包住"的基准就跟着错。
        /// ★ v273：`1.00` ↔ `AttackVfx.BladeR1 = 1.00f` **逐位相同**；它是**比例**不是米，
        ///   所以「特效范围 ×0.90」不落在这里（落在 `range`）—— 两边同时乘会让判定少包 8.7% ✗
        /// </summary>
        private const float MeleeFxBladeR1 = 1.00f;

        /// <summary>
        /// ★ v221：特效弧只铺整条弧的 **60%**（`AttackVfx.ArcCover`）★
        /// ⇒ 算"特效实拍张角"必须乘这个，不能用 `arc/2`（那会把特效当成铺满 ±48°）。
        /// ★ v222：判定已改成固定 180° 半圆、不再与特效张角比大小，
        ///   但这个数仍是"视觉到底画了多宽"的唯一依据（形状表里 57.6° / 29.952° 由它算出）✓
        /// </summary>
        private const float MeleeFxArcCover = 0.60f;

        /// <summary>
        /// ★ v221：特效弧的**片数** = `AttackVfx.ArcSlices`（18 → **26**）★
        /// 只用来算"片自身的切向半宽"：相邻两片角间距 = 视觉张角 / (片数 − 1)，
        /// 每片在角向外扩半个弦 ⇒ 实拍外沿比"片中心张角"再宽约 `Δ/2`（今天 1.152°）。
        /// 不把这一角算进去，最外侧那两片就会被判定切掉一条边。
        /// </summary>
        private const int MeleeFxSliceCount = 26;

        /// <summary>
        /// ★ v221：**特效的真正外沿**（米）= `BladeR1 × R + Depth/2` = `R × 1.05`
        /// ⇒ ★ v273：`range = 3.42` 时 3.729 → **3.915m**（v273 前 `range 3.8` ⇒ 4.295m，同比例 ×0.9117）✓
        /// </summary>
        public static float MeleeFxOuterEdge(float range)
            => Mathf.Max(0f, MeleeFxRadius(range)) * (MeleeFxBladeR1 + MeleeFxSliceDepthRatio * 0.5f);

        /// <summary>
        /// ★ v221：**判定外径**（米）= `特效外沿 × 1.15`，且**不小于** `range × 1.0`。
        /// ★ v273：`range = 3.42` ⇒ `3.915 × 1.15` = **4.503m**
        ///   （v273 前 `range 3.8` ⇒ 4.939m；`max` 的 `range` 项仍**没顶住**：3.42 &lt; 4.503）✓
        /// </summary>
        public static float MeleeRingOuter(float range)
            => Mathf.Max(MeleeFxOuterEdge(range) * MeleeRingOuterScale, Mathf.Max(0f, range));

        /// <summary>
        /// **特效实拍半张角**（度）= `视觉张角/2 × ArcCover + 半个片的角宽` —— **视觉**读数，不是判据。
        /// `arc = 100` ⇒ `96/2 × 0.60` = 28.800°，片角间距 `57.6°/25` ⇒ +1.152° ⇒ **29.952°** ✓
        ///
        /// ★ v222：判定**不再**由它推导（半张角改成固定 `MeleeRingHalfArcDeg = 90°`）★
        /// 保留本函数只是因为 `MeleeFxArcCover` / `MeleeFxSliceCount` 这两个数
        /// 在 `AttackVfx.cs` 里被**点名声明为镜像源**（见那边的 `ArcSlices` / `ArcCover` 注释），
        /// 而形状表里"判定 90° 是特效 29.952° 的 3.005 倍"这个数必须能**现场复算**（不是手抄的）。
        /// ⇒ 它是"特效几何"的唯一计算处，删了那两个镜像常量就成了孤儿 ✓
        /// </summary>
        public static float MeleeFxHalfArcDeg(float arcDeg)
        {
            float visArc = MeleeFxArcDeg(arcDeg);                        // 96°（= arc − 4°）
            float half = visArc * 0.5f * MeleeFxArcCover;                // 28.800°
            float stepRad = visArc * MeleeFxArcCover * Mathf.Deg2Rad
                            / Mathf.Max(1, MeleeFxSliceCount - 1);       // 每片角间距（弧度）
            return half + Mathf.Atan(Mathf.Sin(stepRad * 0.5f)) * Mathf.Rad2Deg;   // + 1.152°
        }

        /// <summary>
        /// ★ v222：**这一刀打不打得到这一个敌人** —— "半圆"的唯一判据本体 ★
        ///
        /// 判据在**相对坐标**里做（只吃 `delta = 敌人位置 − origin`），
        /// 所以 `origin` / `圆心` 本身不必传进来 ✓
        ///
        /// 命中条件（**唯一一条**，逐字对应需求）：
        /// ```
        /// if (dist &lt;= MeleeCoincidentDist) return true;            // 圆心重合例外
        /// return dist &gt;= MeleeRingInner − r                        // 内沿吃 r（内半径 0 ⇒ 恒真）
        ///     &amp;&amp; dist &lt;= MeleeRingOuter(range) + r                 // 外沿吃 r
        ///     &amp;&amp; Dot(delta/dist, dir) &gt;= cos(MeleeRingHalfArcDeg);  // 90° ⇒ 总 180°
        /// ```
        /// 角向用**点积**写（`Dot(delta/dist, dir) ≥ cos(半张角)`）：与 `angleBetween ≤ 半张角`
        /// 数学等价，但省掉一次 `acos`，也不受 `acos` 在 0°/180° 附近的精度影响 ✓
        ///
        /// ★ v222：`arcDeg` 形参**已删除** ★ —— 半张角是固定 90°，不再由 `hoe.arc` 推导；
        ///   留在形参里就是"只写不读"，所以签名从 `(arcDeg, range, dir, delta, dist, r)`
        ///   收紧成 `(range, dir, delta, dist, r)` ✓
        /// ★ `dir` 先归一化：`dist` 与 `delta` 的单位必须是米。若 `dir` 不是单位向量，
        ///   点积阈值就不再是 `cos(半张角)` ⇒ 角向边界会缩水或膨胀 ✗
        ///   本工程传进来的 `p.AimDir` 本来就是单位向量 ⇒ 归一化是**幂等**的防御 ✓
        /// ★ `cos(90°)` 在 float 里是 `−4.37e-8`（不是 0）⇒ 必须**钳到 0**，
        ///   否则"夹角正好 90°"（`Dot == 0`）这一点会被浮点误差判成未命中，
        ///   用户要的"侧面在任意距离都能命中"就正好在边界上塌掉 ✗ → 见下面那行 `Mathf.Max(0f, …)` ✓
        /// ★ 这里**没有任何副作用**（不 Add、不扣名额、不发伤害）⇒
        ///   去重（`_meleeHitIds` / `_meleeWinHitIds`）与名额（`_meleeWinHitsLeft`）
        ///   仍然只在 `MeleeHitScan` 命中之后那几行里发生，一次命中只结算一次 ✓
        /// </summary>
        internal static bool MeleePointHit(float range, Vector2 dir, Vector2 delta,
                                           float dist, float enemyRadius)
            => MeleePointHit(range, dir, delta, dist, enemyRadius, false);

        /// <summary>
        /// ★ v290：**巨型**（`hoe_giant`）—— `fullRing = true` 时判定形状从"面前 180° 半圆"
        /// 换成**环绕玩家的整圈**（360°）：只有"内径 ~ 外径"两道径向判据，**没有角向判据**。
        ///
        /// 口径（逐条）：
        ///   · 外径 **沿用** `MeleeRingOuter(range)` —— 与半圆那一版是**同一个数**,
        ///     所以"外沿变了"这件事一定来自"内径加了"，不来自"范围变大了" ✓
        ///   · 内径 = 外径 × `PluginExtra2.GiantInnerRatio`(0.35) ⇒ 贴脸那一圈**不算**（环不是饼）
        ///   · 内沿同样**吃 `r`**（`dist &lt; inner − r` 才判不中），与半圆版的外沿同一条宽容度 ✓
        ///   · 圆心重合例外（`MeleeCoincidentDist`）保留：站在玩家脚下的敌人仍然必中 ✓
        /// ★ 为什么"没有角向判据"就是 360° ★ 半圆版的角向判据是 `Dot ≥ cos(90°) = 0`；
        ///   直接删掉它 = 任何方向都成立 ⇒ 整圈。写成"另加一条 360° 的判定"会变成两份真相 ✗
        /// </summary>
        internal static bool MeleePointHit(float range, Vector2 dir, Vector2 delta,
                                           float dist, float enemyRadius, bool fullRing)
        {
            // 唯一的例外：圆心重合 ⇒ 必中（v184 的退化修复，原样保留）
            if (dist <= MeleeCoincidentDist) return true;

            float r = Mathf.Max(0f, enemyRadius);

            // ── ① 径向：外沿吃 r（两版共用）──
            if (dist > MeleeRingOuter(range) + r) return false;

            // ── ①b 巨型：内径也吃 r；命中 ⇒ **整圈**，角向一个字节都不看 ✓ ──
            if (fullRing)
            {
                float inner = MeleeRingOuter(range) * Mathf.Clamp01(PluginExtra2.GiantInnerRatio);
                if (dist < inner - r) return false;
                return true;
            }

            // ── ② 径向（半圆版）：内沿**也吃 r**，外沿同样吃 r ──
            // ★ v222：`MeleeRingInner = 0` ⇒ 下面这一条**恒为假**（`dist ≥ −r` 对任何 `dist ≥ 0` 都成立）
            //   语义 = "没有内径、没有贴脸死区" ⇒ 侧面贴身命中 ✓
            //   保留（不删）：形状参数仍由 `MeleeRingInner` 一处控制，改回带内径的环不必重写判据。
            if (dist < MeleeRingInner - r) return false;
            if (dist > MeleeRingOuter(range) + r) return false;

            // ── ② 角向：`angleBetween(dir, delta) ≤ 90°`（用点积等价实现）──
            // `dir` 归一化（对已归一化的输入幂等）；零向量退化时退到"正右"，避免 NaN
            Vector2 fwd = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector2.right;
            Vector2 dn = dist > 1e-8f ? delta / dist : fwd;   // 单位方向（dist 已过内沿检查 ⇒ > 0）
            // ★ 钳到 0：float 的 `cos(90°)` 是 −4.37e-8 ⇒ 不钳就让"正好 90°"落到命中区外 ✗
            float cosHalf = Mathf.Max(0f, Mathf.Cos(MeleeRingHalfArcDeg * Mathf.Deg2Rad));
            return Vector2.Dot(dn, fwd) >= cosHalf;
        }

        // ── ① 水柱：**不重叠**的扇形分布（用户需求①："水柱不重叠，扇形分布"）──
        //
        // 旧值 8°/道 是"表里写的 amount2"，两条水柱在射程末端只错开
        //   2 × 9.5 × sin(4°) = 1.33m，而柱宽是 2 × 1.4 = 2.8m ⇒ 两条柱子**大部分重叠** ✗
        // 现在按与剑气②**同一条判据**算角度（只是把"弯月宽度"换成"柱宽"）：
        //   2·d·sin(Δθ/2) ≥ 2·halfWidth  ⇒  Δθ ≥ 2·asin(halfWidth / d)
        // 参考距离 d 取**射程**：水柱是从玩家射出的一条线，射程末端才是它真要覆盖的地方；
        //   贴脸那一段两条同源直线必然相交 —— 那是几何，不是设计缺陷（近战框本来就覆盖那里）。
        //   d = 9.5m、halfWidth = 1.4m ⇒ Δθ_min = 2·asin(0.14737) = 16.98° ⇒ 留 5% 余量 = **17.83°** ✓
        //   ★ 加粗（`can_thickjet`）会让 halfWidth 变大 ⇒ 夹角自动变大（仍然不重叠）✓

        /// <summary>水柱不重叠判据的余量（横向距离 = 柱宽 × 本值，1.05 = 留 5%）。</summary>
        public const float BeamNoOverlapMargin = 1.05f;

        /// <summary>
        /// 相邻两道水柱的**最小夹角**（度）= `2·asin(半宽 / 射程)`。
        /// 传**实际**半宽（已吃 `can_thickjet`）与**实际**射程（已吃攻击范围属性）⇒ 自相似 ✓
        /// </summary>
        public static float BeamFanStepDeg(float halfWidth, float range)
        {
            float r = Mathf.Max(0f, range);
            if (r <= 1e-4f) return 90f;                    // 退化：没有射程就没有方向可言
            float ratio = Mathf.Clamp01(Mathf.Max(0f, halfWidth) * BeamNoOverlapMargin / r);
            return 2f * Mathf.Asin(ratio) * Mathf.Rad2Deg;
        }

        // ── ③ 双击：**间隔 = 0.2 / 攻速**（用户需求③：0.2 秒初始值，受攻速影响）──
        //
        // 旧值 `DoubleStrikeDelay = 0.12s` 是**死常数**：攻速堆到 2.0 时挥击冷却从 0.7 缩到 0.35，
        // 而第二刀仍然固定等 0.12s ⇒ "打得快"对双击这一档几乎没有感觉 ✗
        // 现在与挥击冷却用**同一个**攻速（`p.AttackSpeed`）⇒ 攻速对双击是等比缩短 ✓

        /// <summary>双击的**初始间隔**（秒，攻速 1.0 时）= 0.2。</summary>
        public const float DoubleStrikeGap = 0.2f;

        /// <summary>双击的间隔 = `0.2 / 攻速`（攻速越高越短），与挥击冷却同一个攻速源 ✓</summary>
        public static float SwingGapFor(float attackSpeed)
            => DoubleStrikeGap / Mathf.Max(0.05f, attackSpeed);

        /// <summary>把向量绕原点旋转 <paramref name="rad"/> 弧度（确定性、不依赖 UnityEngine 的隐式转换）。</summary>
        private static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>
        /// **持续效果表**（阶段 3 新增）。灼烧 / 减速 / 毒区都挂在这里 ✓
        /// 它是一局的状态 ⇒ 跟着 `RunSession` 走，`Start` 时清空 ✓
        /// </summary>
        public readonly PluginDotTable Dots = new PluginDotTable();

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v282：吸血的**批次状态**（唯一一套；三个函数读写它）★★
        //
        // 为什么需要状态：一条挥击的判定横跨 `MeleeSwingWindowSeconds`
        // （`MeleeHitScan` 会在窗口内被重扫多次），一发剑气/一束激光/一次爆炸
        // 也各自在一个循环里打多个敌人 ⇒ "这一下的总伤害"只有在**事件结束**时才知道 ✓
        //
        // 跨局隔离：`Start` 里清（`_lsBatchOpen = false` / 秒桶归零），
        // 与 `Dots.Clear()` / `_pendingSwings.Clear()` 同一批重置（上一局的血不许流到这一局）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>本批次累计的伤害（**敌人 HP 域**，多个敌人的和）。</summary>
        private float _lsBatchDmg;
        /// <summary>本批次命中的**最大**吸血比例（同一把武器的插件修正，各敌相同；取 max 是防御）。</summary>
        private float _lsBatchPct;
        /// <summary>本批次回血的归属（玩家）。</summary>
        private PlayerActor _lsBatchOwner;
        /// <summary>批次是否开着（`false` ⇒ `LifestealSettle` 立刻返回，不做任何事）。</summary>
        private bool _lsBatchOpen;

        /// <summary>当前"1 秒桶"的起点（`RunTime`；`-1e9f` = 还没开过桶）。</summary>
        private float _lsSecStart = -1e9f;
        /// <summary>当前"1 秒桶"里已经回掉的血（玩家血池域）。</summary>
        private float _lsSecUsed;

        /// <summary>★ v309：吸血拖尾的锚点高度（米）—— 与 `PluginHitFx.Spawn` 里那 4 滴血的
        /// `Origin.y = 1.05f` **同一个数** ⇒ 拖尾点与血滴在同一层，不会一个浮在另一个上面 ✓</summary>
        private const float LifestealFxHeight = 1.05f;

        /// <summary>★ v309：一次爆炸里**最多画几个**灼印脉冲环（`AttackVfx.SpawnMarkPulse`）。
        /// 取 3 的唯一依据是 `Bit` 池的账（主环 16 + 二次环 16 + 土粒 12 + 6×3 = 62 ≤ 64）；
        /// 逻辑侧的落印**不受它影响** —— 半径内**每一个**敌人都会照旧挂印 ✓</summary>
        private const int MarkPulseMax = 3;

        /// <summary>
        /// 开一个**攻击事件批次**（一次挥击窗口 / 一发剑气判定 / 一束激光 / 一次爆炸）。
        /// 上一批若还没结（双击窗口重叠时会这样）⇒ **先结掉**：血不会丢，也不会跨事件混账 ✓
        /// </summary>
        private void LifestealBegin(PlayerActor owner)
        {
            if (_lsBatchOpen) LifestealSettle();
            _lsBatchOpen = true;
            _lsBatchDmg = 0f;
            _lsBatchPct = 0f;
            _lsBatchOwner = owner;
        }

        /// <summary>把这一次命中的伤害累加进批次（**不结算**）。</summary>
        private void LifestealAccumulate(float damage, in WeaponModifiers m, PlayerActor source)
        {
            float pct = Mathf.Max(0f, m.LifestealPct);
            if (pct <= 0f) return;                                     // 没装吸血 ⇒ 连批次都不开
            // 防御：某个调用点忘了 `LifestealBegin` 也照样结算（开一个只有自己的批次）
            if (!_lsBatchOpen) LifestealBegin(source);
            _lsBatchDmg += Mathf.Max(0f, damage);
            _lsBatchPct = Mathf.Max(_lsBatchPct, pct);
            if (_lsBatchOwner == null) _lsBatchOwner = source;
        }

        /// <summary>本秒桶的**剩余额度**（`+∞` = 上限被关掉了，不夹）。</summary>
        private float LifestealSecondBudget(float ownerMaxHp)
        {
            float cap = WeaponSystem.LifestealSecondCap(ownerMaxHp);
            if (float.IsPositiveInfinity(cap)) return cap;
            // 桶按「`RunTime` 走过 1 秒」翻页（不是滚动窗口）—— 确定性、不依赖帧率、
            // 也不需要每帧 `Tick` 一台计时器：只在本函数被调用时检查一次 ✓
            // `RunTime < _lsSecStart`（时钟被拨回 / 新局）也翻页，避免负数差值把桶卡死 ✓
            if (RunTime < _lsSecStart || RunTime - _lsSecStart >= 1f)
            {
                _lsSecStart = RunTime;
                _lsSecUsed = 0f;
            }
            return Mathf.Max(0f, cap - _lsSecUsed);
        }

        /// <summary>
        /// **结算本批次**：总伤害 × 比例 × 单位 → 夹「单次上限」与「本秒余额」→ 回一次血。
        /// 弹字（`PluginPopupStyle.Lifesteal`）与音效仍在**真正回血的那一刻**发一次 ✓
        /// </summary>
        private void LifestealSettle()
        {
            if (!_lsBatchOpen) return;
            _lsBatchOpen = false;
            float dmg = _lsBatchDmg;
            float pct = _lsBatchPct;
            PlayerActor owner = _lsBatchOwner;
            _lsBatchDmg = 0f;
            _lsBatchPct = 0f;
            _lsBatchOwner = null;
            if (dmg <= 0f || pct <= 0f || owner == null || owner.Health == null) return;

            float maxHp = owner.Health.MaxHp;
            float heal = WeaponSystem.LifestealSettle(dmg, pct, EnemyDef.PlayerHpUnit,
                                                      WeaponSystem.LifestealSwingCap(maxHp),
                                                      LifestealSecondBudget(maxHp));
            if (heal <= 0f) return;                                    // 上限用完 ⇒ 这一批不回（也不响）
            _lsSecUsed += heal;
            // 这次回的是**吸血**：立一次性标签（弹字侧据此发红字+♥ 与小号绿字）
            PluginHitFx.Tag(PluginPopupStyle.Lifesteal);
            try { owner.Health.Heal(heal); }
            finally { PluginHitFx.Clear(); }
            // ★ v309：**血滴的拖尾**（用户：「血滴改成带拖尾的（每滴后跟 2 个更小更淡的点）」）
            //   那 4 滴血本身由 `PluginHitFx` 画（那个文件本轮不许碰），拖尾由本类补：
            //   锚点与弹字**同一个坐标/同一个高度**（玩家位、1.05m），4 滴 × 2 点 = 8 格 `Bit`。
            //   放在 `Heal` **之后**：真回了血才有这一笔（上限用完的那一批不画）✓
            //   纯表现：不消耗随机数、不改任何数值、不影响吸血上限的账 ✓
            AttackVfx.SpawnBloodTrail(new Vector3(owner.Pos.x, LifestealFxHeight, owner.Pos.y));
            GameAudio.PlayPlugin(PluginEffectKind.Lifesteal);           // ★ 声音：真的吸到血才响
        }

        /// <summary>
        /// **一剑/一束/一炸命中之后**要发生的插件副作用。三条路都调它 ✓
        ///
        /// ★ 阶段 3 只剩三件：灼烧 / 减速 / 吸血 ★
        /// 它们都**不改伤害公式**，而是"打完这一下之后额外发生的事" ✓
        /// 上一版的寒冷 / 连锁 / 暴击炸裂 / 回弹药随枚举一起删除 ——
        /// 那四件里有三件本质是"再加一份伤害"，与"只改攻击方式"的新标准不符 ✗
        ///
        /// ★★ v282：本函数仍然是**逐敌**调用的（四个攻击循环各调一次），
        /// 但**吸血已经不再在这里结算** —— 它只往"本次攻击事件"的批次里累加伤害，
        /// 由 `LifestealBegin` / `LifestealSettle` 在事件边界上一次性结算 ✓
        /// 理由：逐敌结算 ⇒ 回血 ∝ 敌人数（用户反馈的"面对大量敌人回血过多"）。
        /// 改动只在吸血这一支：灼烧 / 减速 / 收束 / 回响 / 弹射 / 潮汐 / 催熟 全部**逐字未动** ✓
        /// </summary>
        /// <param name="e">被打到的敌人</param>
        /// <param name="damage">这一击打出的原始伤害（吸血与**三种持续伤害**都按它的比例算 —— v279）</param>
        /// <param name="m">该武器的插件修正</param>
        /// <param name="source">伤害来源（玩家；吸血回他的生命）</param>
        private void PluginPostHit(EnemyActor e, float damage, in WeaponModifiers m, PlayerActor source)
        {
            if (e == null || !m.HasPostHit) return;

            // ① 吸血：伤害 × **3%**/层 转生命（`Heal` 自己收到 `MaxHp`）
            //    ★ v216：表值 `plugins.json` 的 `hoe_lifesteal.amount` **0.20 → 0.05**（现为 0.01）
            //    ★ v282：**本行不再结算，只累加** ★（用户反馈："面对大量敌人时回血过多"）
            //      改前：`float steal = LifestealHeal(damage, m) * PlayerHpUnit;` 然后立刻 Heal
            //         ⇒ 这是**逐敌**的 ⇒ 一刀命中 N 个敌人就回 N 份 ✗
            //      改后：把 `damage` 累加进本批次，事件结束（挥击窗口收窗 / 剑气这一帧判完 /
            //         一束激光扫完 / 一次爆炸结算完）时按**总伤害**结算**一次**，
            //         并被「单次上限 5% MaxHp」与「每秒上限 3% MaxHp」夹住 ✓
            //    ★ 只在这里发生 ⇒ 持续伤害（`PluginDotTable`）不经过本函数 ⇒ 吸血对 DoT 不生效 ✓
            //    ★ v192：**回血量要换到玩家血池的单位上** ★ —— 那个换算挪到了 `LifestealSettle` 里
            //      （同一个 `EnemyDef.PlayerHpUnit`，仍在"域转换"那一层，不在纯函数里）✓
            //    ★ 弹字 / 音效不变 ★：仍由 `PluginHitFx.Tag(PluginPopupStyle.Lifesteal)` 与
            //      `GameAudio.PlayPlugin(PluginEffectKind.Lifesteal)` 发，只是从"每敌一次"变成
            //      "每次攻击事件一次"（群怪时不再刷屏，单挑时逐字相同）✓
            LifestealAccumulate(damage, m, source);

            // ② 灼烧：命中附"插件层数"层，每层每秒 = **这一击的实际伤害 × `tuning.dots.burnPercent`**
            //    ★ v279：口径从**属性**改成**这一刀的伤害**（用户原话："燃烧、减速、中毒伤害都修改为
            //      继承自对应武器的伤害百分比，而不是继承属性"）★
            //      改前这里是 `st.Get(StatId.MeleeDamage)` ⇒ 开局近战属性 0 ⇒ 每秒 0 ⇒
            //      `ApplyBurn` 连行都不建（点了火却什么都不烧，且不报错）✗
            //      改后两种情形都自然正确：属性为 0 但武器本体有基础伤害（锄头 8）时，
            //      灼烧 = 8 × 0.5 = 4/秒，与 `plugins.json` 那句描述逐位吻合 ✓
            //    ⚠️ `damage` 已经是**这一刀最终打出去的伤害**（已吃多次挥舞的 ×2/3、暴击与富豪加成），
            //      不是"武器基础值" ⇒ 攻速/暴击/削刀都会等比带动 DoT（这是"继承武器伤害"的本意）✓
            if (!e.IsDead && m.BurnLayersPerHit > 0)
            {
                float perLayer = WeaponSystem.BurnPerLayerPerSecond(damage, m);
                Dots.ApplyBurn(e.Id, perLayer, m.BurnLayersPerHit);
                GameAudio.PlayPlugin(PluginEffectKind.BurnOnHit);   // ★ 声音：点着了
            }

            // ★★ v253：六个新机制里"命中即发生"的四个 ★★
            //   放在灼烧/减速**之前**：它们属于"这一下的账"，DoT 属于"之后的账" ✓
            //   ① 收束：先数"第几次"—— 到点就立刻补一刀处决伤害
            if (m.FocusEvery > 0 && !e.IsDead)
            {
                if (PluginMech.BumpFocus(e.Id, m.FocusEvery, RunTime))
                {
                    float bonus = damage * m.FocusBonusPct;
                    if (bonus > 0f)
                    {
                        // ★★ v308：处决弹字要带**实际造成的伤害**（用户：「所有特殊伤害弹字
                        //   统一必须增加实际造成的伤害数值」）★★
                        //   数字取 `DamageResult.Dealt`（= 这一刀处决**真正扣掉的血/盾**），
                        //   不是 `bonus`（那是算出来的量，会被残血截断、会被闪避归零）✓
                        //   被闪避 / 免伤 ⇒ `Dealt == 0` ⇒ `DamageLabel` 返回 null ⇒ **不发弹字** ✓
                        DamageResult focusHit = World.DealDamage(new DamageRequest
                        {
                            Type = DamageType.Physical, RawValue = bonus, Source = source,
                            Target = e, Knockback = 0f, Crit = true,
                        });
                        string focusText = PluginHitFx.DamageLabel("收束", focusHit.Dealt);
                        if (focusText != null)
                        {
                            Bus.Publish(new FloatingTextEvent
                            {
                                Pos = e.Pos, Text = focusText, Kind = PopupKind.Crit, Scale = 0.9f,
                            });
                        }
                    }
                }
            }
            //   ② 回响：把"这一下的 40%"排进延迟队列（`PluginMech.Tick` 到点才结算）
            if (m.EchoPct > 0f)
                PluginMech.QueueEcho(e.Id, damage * m.EchoPct, m.EchoDelay, e.Pos);
            //   ③ 弹射：命中后跳到**最近的另一个**敌人，只跳一次（不是无限连锁）
            if (m.RicochetPct > 0f && !e.IsDead && source != null)
            {
                EnemyActor second = World.NearestEnemyToExcept(e.Pos, m.RicochetRadius, e);
                if (second != null)
                {
                    // ★★ v308：弹射弹字带**实际造成的伤害**（同「收束」，取 `Dealt` 不取算出来的量）★★
                    //   被闪避 ⇒ `Dealt == 0` ⇒ `DamageLabel` 返回 null ⇒ 只画**水圈**、**不发弹字** ✓
                    //   （水圈仍然留在 `if (second != null)` 里：那是"真的弹过去了"的表现，
                    //     与"这一下有没有打出伤害"是两件事 —— 闪掉的那一发也真实地弹到了第二个人）
                    DamageResult ricHit = World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical, RawValue = damage * m.RicochetPct,
                        Source = source, Target = second, Knockback = 0f, Crit = false,
                    });
                    string ricText = PluginHitFx.DamageLabel("弹射", ricHit.Dealt);
                    if (ricText != null)
                    {
                        Bus.Publish(new FloatingTextEvent
                        {
                            Pos = second.Pos, Text = ricText, Kind = PopupKind.DamageToEnemy, Scale = 0.85f,
                        });
                    }
                    // ★★ v309：**弹射的小激光已删除**（用户：「去除激光分支，改为受到弹射伤害的
                    //    敌人脚下扩散一个水圈」）★★
                    //
                    //   ── 删掉了哪几行 ──
                    //     `PluginHitFx.SpawnLaser(new Vector3(e.Pos.x, 0.85f, e.Pos.y),
                    //                             new Vector3(second.Pos.x, 0.85f, second.Pos.y),
                    //                             PluginFx.For(m, WeaponSlot.Can));`
                    //     ⇒ 原目标 → 弹射目标之间那条 0.12s 的小激光**不再画**。
                    //     ⚠️ `PluginHitFx.SpawnLaser` 这个**方法本身保留**：`PluginHitFx.cs` 本轮
                    //        归另一路改动（弹字大小），一个字符都不动；而且它在本工程里**只有
                    //        这一个调用点**（`grep SpawnLaser` 只剩定义）⇒ 现在是无调用者的死 API，
                    //        由下一轮清理 `PluginHitFx` 时一并删 ✓
                    //
                    //   ── 换成了什么 ──
                    //     每个**受到弹射伤害的敌人脚下**扩散一个**水圈**（贴地、0.15m → 0.90m、
                    //     0.35s、淡出、水壶那套色再往蓝绿推）。
                    //     位置就在 `if (second != null)` 里面 ⇒ 只有**真的发生弹射**才画；
                    //     而且是**逐目标**调用 ⇒ 若将来改成连锁（A→B→C），每一段的目标脚下各有一个 ✓
                    //     形态/配色/池账见 `AttackVfx.SpawnRicochetWaterRing`（13 格 `Bit`）。
                    AttackVfx.SpawnRicochetWaterRing(second.Pos, PluginFx.For(m, WeaponSlot.Can));
                }
            }
            //   ④ 潮汐：沿"玩家→敌人"方向把目标推开（位移，不是减速）
            //   ★★ v305：**改成"按米位移"**（用户实测"潮汐的实际距离不对"）★★
            //
            //   ── 改前（错的，代码 + 数字都给在这里）──
            //     `e.KnockbackVel += away.normalized * (m.TidePush * 6f);`
            //       卡面 `amount` = 1.2 m/层（`Goods.cs` 的注释与 `plugins.json` 都是这个）。
            //       那是一个**初速**：v0 = 1.2 × 6 = **7.2 m/s**，
            //       而 `Enemies.MoveStep` 每帧 `KnockbackVel` 按 λ=7 指数衰减 ⇒
            //       真实位移 = ∫v0·e^{-7t}dt = v0/7 = **1.0286 m**（连 1.2 m 都不到）✗
            //       更致命的是同一条式子里敌人**也在走**：基础兵 `moveSpeed` 2.7 m/s，
            //       推开的过程约 0.5 s ⇒ 它朝玩家走了 1.35 m ⇒ **净位移 ≈ −0.32 m**，
            //       即"推完它反而更靠近了" —— 玩家眼里就是"潮汐完全没推开" ✗
            //   ── 改后 ──
            //     `PluginExtra2.StartTidePush` 建一单**位移**（0.25 s 内推 `TidePush` 米），
            //     且这 0.25 s 内 `IsTideHeld` 让它的自主移动归零 ⇒ 净位移 = 卡面值 ✓
            //     实测读数见 `PluginExtra2.TideLastMeters` / `TideLastWant`。
            //   ⚠️ 仍然只在**命中那一刻**发生（与改前同一个调用点），触发频率没变 ✓
            if (m.TidePush > 0f && !e.IsDead && source != null)
            {
                Vector2 away = e.Pos - source.Pos;
                if (away.sqrMagnitude > 0.0001f)
                    // ★ v312：多传一个 `FxSpec`（水壶这一路的插件色）—— 推力反馈要用它上色
                    //   （`AttackVfx.SpawnTidePush`：120° 水波弧 + 拖尾，见该处池账）。
                    //   这一段本来就在 `m.TidePush > 0` 里面 ⇒ 特效的门控与推开**同一个条件**，
                    //   不可能出现"有特效没推开"或"推开了没特效"✓
                    PluginExtra2.StartTidePush(e, away.normalized, m.TidePush, m.TidePush,
                                               PluginFx.For(m, WeaponSlot.Can));
            }

            // ③ 减速：命中附"插件层数"层，每层每秒 = **这一束的实际伤害 × `tuning.dots.slowPercent`**
            //    ★ v279：口径从 `Stats.Get(StatId.RangedDamage)`（属性）改成这一束的伤害（同 ②）★
            //      改前开局远程属性 0 ⇒ 减速的**掉血**部分是 0（移速那部分不受影响 —— 它读 `m.SlowPctPerLayer`）✓
            if (!e.IsDead && m.SlowLayersPerHit > 0)
            {
                float perLayer = WeaponSystem.SlowPerLayerPerSecond(damage, m);
                Dots.ApplySlow(e.Id, perLayer, m.SlowPctPerLayer, m.SlowLayersPerHit);
                GameAudio.PlayPlugin(PluginEffectKind.SlowOnHit);    // ★ 声音：冻住了
                // 当场把移速写回去（不等下一帧的 `Tick`）⇒ "命中即变慢" ✓
                // ★ 少了这一行，探针"打一下→立刻读移速"会读到 1.00 ⇒ 把已接上的效果报成没变化 ✓
                Dots.SyncSlow(World);
            }
        }

        /// <summary>
        /// ★攻击形式拓展★ **剑气流**：挥击的同时沿瞄准方向劈出 <paramref name="waves"/> 道剑气。
        ///
        /// ★ v185 重做：从"瞬时判定一条矩形"改成**沿朝向飞出去的判定体** ★
        ///
        /// 需求原话："剑气视为子弹类（与近战攻击相同的判定框，远程发射）"
        /// ⇒ 它不再是"挥完这一帧就把整条走廊判完"，
        ///   而是**一个带着近战框的子弹**，每帧推进一小步、每帧只判自己所在的那一格 ✓
        ///
        /// ── 为什么必须改（旧版留给玩家的读数）──
        /// 旧版 `FireSwordWaves` 是**同一次函数调用里**把 `along ∈ (0, meleeRange]` 整段扫完
        /// ⇒ 第 0 帧就结算完毕，帧与帧之间**没有任何中间状态**：
        /// 玩家看到的是"一道光柱瞬间铺满 3.8m，敌人同时掉血"，读起来像判定框而不是剑气 ✗
        /// 现在的读数是"它真的在飞"：`DebugApi.ProbeWaveFlight` 逐帧打印弹丸位置，
        /// 同一发子弹在第 0/1/2/… 帧的 `Pos` 各不相同 ✓
        ///
        /// ── 四条约束（与旧版一一对应，只把"瞬时"换成"飞行"）──
        ///   ① **判定框 = 近战框本身**：框长 `= range`、半宽 `= MeleeBoxHalfWidth(range, arc)`
        ///      —— 与 `DoHoeSwing` **同一个函数**，调 `arc` / `range` 两边一起变 ✓
        ///      出生时框的**后缘压在玩家脚下** ⇒ 与近战框**字面重合**（不是"差不多大"）✓
        ///   ② **每帧一次判定**，几何与近战框同一套（`|along| ≤ 半长 + 半径`、`|perp| ≤ 半宽 + 半径`）✓
        ///   ③ 一次剑气流里同一个敌人**只结算一次**（`HitIds`，跨条共享 —— 与 v181 的跨条去重等价）✓
        ///   ④ 已经被**这一刀的挥击本体**打过的敌人，剑气不再补一次（`SkipIds` 快照）
        ///      ★ 为什么是快照而不是继续读 `_meleeHitIds` ★
        ///      剑气现在**跨帧存活**（约 0.26s），而双击的第二刀会在 0.12s 落下并把
        ///      `_meleeHitIds` **清空重填** ⇒ 还在飞的那一发读到的会是**别人那一刀**的名单 ✗
        ///      快照让每一发只认"自己那一刀"的名单，两条路各自独立 ✓
        ///
        /// ── 命中之后：**继续飞（穿透）**，不是命中即消失 ──
        /// 理由（这是我做的选择，需求让我自己定并说明）：
        ///   · 需求要的是"**怎么飞**"（子弹语义），**没有**要求削弱覆盖面；
        ///     旧版瞬时矩形会打到走廊里的**所有人**，改成"命中即消失"= 一梭子只砍一个人 ⇒
        ///     这是用户没要求的行为回归 ✗
        ///   · 本作是成群小怪压上的局面，剑气的作用就是"一劈扫一片"；
        ///     首杀即灭会让"多买几层剑气"（`SwordWaveCount`）在尸群面前几乎无效 ✗
        ///   · 本插件族里**没有**穿透/连锁计数（`allowedWeapons` 只挂了锄头，效果只有"多一道"），
        ///     所以不存在"穿透会与某个插件语义打架"的问题 ✓
        ///   · 去重仍按"一发对一个敌人只结算一次" ⇒ 穿透不会变成多发白嫖同一个目标 ✓
        ///   ⇒ 若将来要改回"命中即消失"，只需在 `JudgeSwordWaveBox` 命中后置 `s.Dead = true`（一行）✓
        /// </summary>
        private void FireSwordWaves(PlayerActor p, in WeaponModifiers m, int waves,
                                    float dmg, bool crit, float meleeRange, float meleeHalfWidth, float knock)
        {
            if (waves <= 0) return;
            // ★ 声音：剑气流真的劈出去了才响（`waves == 0` 上面已经 return ⇒ 不会空响）★
            GameAudio.PlayPlugin(PluginEffectKind.SwordWave);

            float halfLen = SwordWaveHalfLen(meleeRange);     // 框的**前向半长**（框长 = 攻击范围）
            float dist = SwordWaveDistance(meleeRange);       // **飞行距离** = 攻击范围 × 系数
            Vector2 baseDir = p.AimDir.sqrMagnitude < 0.001f ? Vector2.right : p.AimDir.normalized;
            FxSpec fx = PluginFx.For(m, WeaponSlot.Hoe);

            // ③/④ 的两份名单：这一刀**只建一次**，本次的全部剑气共享（与 v181 的跨条去重等价）
            var hitIds = new List<int>(8);
            var skipIds = new List<int>(_meleeHitIds.Count);
            foreach (int id in _meleeHitIds) skipIds.Add(id);

            // ★ 需求②：夹角由"横向距离 ≥ 弯月宽度"**算出来**（见 `SwordWaveSpreadDeg` 的推导）
            //   1 条 ⇒ 0°（正对瞄准方向）；2 条 ⇒ ±27.36°；3 条 ⇒ −54.72/0/+54.72 ✓
            float stepDeg = SwordWaveSpreadDeg(meleeRange, meleeHalfWidth);
            for (int k = 0; k < waves; k++)
            {
                float off = stepDeg * (k - (waves - 1) * 0.5f);
                Vector2 dir = Rotate(baseDir, off * Mathf.Deg2Rad);
                // 出生位置：框的**后缘**落在玩家脚下 ⇒ 框心 = 脚下 + 半长
                Vector2 spawn = p.Pos + dir * halfLen;
                _swordWaves.Add(new SwordWaveShot
                {
                    Pos = spawn,
                    Dir = dir,
                    HalfLen = halfLen,
                    HalfWidth = meleeHalfWidth,
                    Dist = dist,
                    Travel = 0f,
                    Dmg = dmg,
                    Knock = knock * 0.5f,
                    Crit = crit,
                    Src = p,
                    Mods = m,
                    HitIds = hitIds,
                    SkipIds = skipIds
                });
                // 表现：一发**会飞的**弯月剑气（`SwordWaveEvent`，WorldView 那边负责形状与飞行）
                Bus.Publish(new SwordWaveEvent
                {
                    Origin = spawn,
                    Dir = dir,
                    HalfLen = halfLen,
                    HalfWidth = meleeHalfWidth,
                    Dist = dist,
                    Speed = SwordWaveSpeed,
                    Fx = fx
                });
            }
        }

        /// <summary>
        /// 一道**飞行中的剑气**。它不是"一次判定"，而是一个跨帧存活的判定体 ✓
        /// `Mods` / `Src` 在出生时快照：命中时的插件副作用（灼烧 / 减速 / 吸血）必须按
        /// **劈出去那一刻**的插件算，而不是按"飞到一半时又被装了别的插件"算 ✓
        /// </summary>
        private struct SwordWaveShot
        {
            public Vector2 Pos;        // 框心
            public Vector2 Dir;        // 单位朝向
            public float HalfLen;      // 前向半长（= 攻击范围 / 2）
            public float HalfWidth;    // 横向半宽（= MeleeBoxHalfWidth(range, arc)）
            public float Dist;         // 总飞行距离
            public float Travel;       // 已飞距离
            public float Dmg, Knock;
            public bool Crit;
            public PlayerActor Src;
            public WeaponModifiers Mods;
            public List<int> HitIds;   // ③ 一次剑气流里已结算过的敌人
            public List<int> SkipIds;  // ④ 这一刀的挥击本体已打过的敌人
        }

        /// <summary>场上所有在飞的剑气（一局的状态 ⇒ 跟 `RunSession` 走）。</summary>
        private readonly List<SwordWaveShot> _swordWaves = new List<SwordWaveShot>();

        /// <summary>调试/自检读数：现在有几发剑气在飞。</summary>
        public int LiveSwordWaveCount => _swordWaves.Count;

        /// <summary>调试/自检读数：第 <paramref name="i"/> 发剑气的**框心**位置（世界坐标 XZ）。</summary>
        public Vector2 SwordWavePosAt(int i)
            => i >= 0 && i < _swordWaves.Count ? _swordWaves[i].Pos : Vector2.zero;

        /// <summary>调试/自检读数：第 <paramref name="i"/> 发剑气**已经飞了多远**（米）。</summary>
        public float SwordWaveTravelAt(int i)
            => i >= 0 && i < _swordWaves.Count ? _swordWaves[i].Travel : 0f;

        /// <summary>调试/自检读数：第 <paramref name="i"/> 发剑气的**总飞行距离**（米）。</summary>
        public float SwordWaveDistAt(int i)
            => i >= 0 && i < _swordWaves.Count ? _swordWaves[i].Dist : 0f;

        /// <summary>清空在飞的剑气（换局 / 跳分钟 / 收尾时用）。</summary>
        public void ClearSwordWaves() => _swordWaves.Clear();

        /// <summary>
        /// ★ 剑气的**逐帧推进**（v185）★ —— 复用本文件已有的"每帧待处理效果"那条链
        /// （`TickPendingSwings` / `TickPendingBlasts` / `Dots.Tick` 都在 `TickCombat` 同一处），
        /// **没有**另起一个更新入口 ✓
        ///
        /// ★ 为什么不用 `World.Projectiles` ★（这是查证结果，不是推测）
        ///   `Projectile.Tick → TickPlayerShot`（`Actors.cs:559`）的命中形状是**圆**
        ///   （`Vector2.Distance(e.Pos, Pos) > e.Radius + Radius`），而需求要的是
        ///   **与近战框同一个矩形** ⇒ 用不上 ✗
        ///   并且 `Actors.cs` 不在本轮允许改动的文件里 ⇒ 不往那边加"矩形命中"模式 ✓
        ///
        /// `dt` 会被切成 ≤ `SwordWaveMaxStep` 的若干步：实时帧是 1/60（一步），
        /// `DebugAdvanceAttack(0.5f)` 是 30 步 ⇒ **两条路的判定序列逐帧一致**，且不会隧穿 ✓
        /// </summary>
        private void TickSwordWaves(float dt)
        {
            if (_swordWaves.Count == 0 || dt <= 0f) return;
            float remain = dt;
            while (remain > 1e-5f)
            {
                float step = Mathf.Min(SwordWaveMaxStep, remain);
                remain -= step;
                StepSwordWaves(step);
                if (_swordWaves.Count == 0) return;
            }
        }

        /// <summary>推进一个子步：**先判出生位置，再前进，再判到达位置**，到末端即消失。</summary>
        private void StepSwordWaves(float step)
        {
            for (int w = _swordWaves.Count - 1; w >= 0; w--)
            {
                SwordWaveShot s = _swordWaves[w];
                // ① 出生那一帧：框与近战框重合，这一格必须判一次（否则贴脚的目标要等下一帧）
                if (s.Travel <= 0f) JudgeSwordWaveBox(ref s);
                // ② 推进（最后一帧只推到末端，不越界 ⇒ 帧位置可复现）
                float move = Mathf.Min(SwordWaveSpeed * step, s.Dist - s.Travel);
                if (move < 0f) move = 0f;
                s.Pos += s.Dir * move;
                s.Travel += move;
                // ③ 到达位置再判一次（每帧**一次**判定，几何与近战框同一套）
                JudgeSwordWaveBox(ref s);
                // ④ 飞完即消失
                if (s.Travel >= s.Dist - 1e-4f) _swordWaves.RemoveAt(w);
                else _swordWaves[w] = s;
            }
        }

        /// <summary>
        /// 以剑气当前的**框心**做一次矩形判定 —— 与近战框**同一套几何**：
        /// `|along| ≤ 半长 + 敌半径` 且 `|perp| ≤ 半宽 + 敌半径`。
        ///
        /// ★ 半宽来自 `MeleeBoxHalfWidth(range, arc)`（`DoHoeSwing` 用的是同一个函数）★
        /// ⇒ 「判定框大小与锄头近战框一致」这件事**不是靠核对两个数字**，
        ///   而是**同一个表达式的同一个结果**（自检 `ProbeSwordWave` 会把这个值打出来）✓
        /// </summary>
        private void JudgeSwordWaveBox(ref SwordWaveShot s)
        {
            // ★ v282：这一帧的剑气判定 = 一个吸血批次（多个敌人累加，判完结算一次）✓
            LifestealBegin(s.Src);
            for (int i = 0; i < World.Enemies.Count; i++)
            {
                EnemyActor e = World.Enemies[i];
                if (e == null || e.IsDead) continue;
                if (ContainsId(s.SkipIds, e.Id)) continue;                 // ④ 本刀近战已打过
                Vector2 rel = e.Pos - s.Pos;
                if (Mathf.Abs(Vector2.Dot(rel, s.Dir)) > s.HalfLen + e.Radius) continue;   // 框长
                if (Mathf.Abs(rel.x * s.Dir.y - rel.y * s.Dir.x) > s.HalfWidth + e.Radius) continue; // 框宽
                if (ContainsId(s.HitIds, e.Id)) continue;                  // ③ 一次剑气流只结算一次
                s.HitIds.Add(e.Id);
                // 这一下是**剑气**打的：弹字只有纯数字 `-N`，文案上分不出来 ⇒ 立一次性标签
                PluginHitFx.Tag(PluginPopupStyle.SwordWave);
                try
                {
                    World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical,
                        RawValue = s.Dmg,
                        Source = s.Src,
                        Target = e,
                        Knockback = s.Knock,
                        Crit = s.Crit
                    });
                }
                finally { PluginHitFx.Clear(); }
                PluginPostHit(e, s.Dmg, s.Mods, s.Src);
            }
            // ★ v282：这一帧剑气打到的全部敌人 ⇒ 一次吸血结算 ✓（必须在这一帧的循环之后，
            //   否则又会退回"每个敌人各回一份"）
            LifestealSettle();

            // ★ v186：**剑气也能收割成熟作物** ★（用户原话："让剑气也可以收割植物"）
            //
            // ★ 为什么放在这个函数里，而不是另起一次判定 ★
            //   需求要的是"判定几何与敌人一致（同一个框）" ⇒ 只有**在同一处**用 `s.HalfLen/HalfWidth`
            //   才能让这句话是结构性的，而不是"两处数字碰巧一样" ✗
            //   作物这边唯一的差别是"地块没有 `e.Radius`" ⇒ 用一个常量顶替（见 `PlotGrabRadius`）✓
            // ★ 收成走的还是既有入口 `HarvestPlot` ★（金币/经验/弹字/地块复位全在它里面）
            //   ⇒ 剑气收割与锄头收割**产出完全一样**，不是第二套收成规则 ✓
            // ★ 不成熟的收不了 ★ —— 判据就是 `plot.IsRipe`（与锄头那一侧同一个字段）✓
            // ★ 会不会重复收 ★ —— 收完那一格立刻回到 `Growing`（`HarvestPlot` → `FarmPlot.Harvest`）
            //   ⇒ 同一发剑气下一帧再判到它时 `IsRipe` 已经是 false ✓ 不需要额外的命中名单 ✓
            HarvestRipeInSwordWave(s);
        }

        /// <summary>
        /// 地块在"攻击判定"里的**等效半径**（米）—— 与 `HarvestRipeInMeleeBox` 里的 `range + 1.2f`
        /// 是同一个数（地块半径 1.25m 的一半左右：让"脚尖踩到地块边缘"就算命中）✓
        /// ★ 只此一处定义 ★ —— 锄头与剑气都读它，改数字时两边一起变 ✓
        /// </summary>
        private const float PlotGrabRadius = 1.2f;

        /// <summary>
        /// 用**剑气的矩形框**收割成熟作物，返回收了几株（v186）。
        /// 几何与 `JudgeSwordWaveBox` 的敌人判定**逐字相同**（长/宽各自加一个半径）✓
        /// </summary>
        private int HarvestRipeInSwordWave(in SwordWaveShot s)
        {
            if (World == null || World.Farm == null || World.Farm.Slots == null) return 0;
            int harvested = 0;
            FarmPlot[] slots = World.Farm.Slots;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot plot = slots[i];
                if (plot == null || !plot.IsRipe) continue;                    // 不成熟的不能收 ✓
                Vector2 rel = plot.World - s.Pos;
                if (Mathf.Abs(Vector2.Dot(rel, s.Dir)) > s.HalfLen + PlotGrabRadius) continue;   // 框长
                if (Mathf.Abs(rel.x * s.Dir.y - rel.y * s.Dir.x) > s.HalfWidth + PlotGrabRadius) continue; // 框宽
                if (HarvestPlot(plot)) harvested++;
            }
            return harvested;
        }

        /// <summary>小列表的线性包含判断（名单长度是"这一刀打中几个"，用不着哈希）。</summary>
        private static bool ContainsId(List<int> list, int id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == id) return true;
            return false;
        }

        /// <summary>
        /// **这一刀的挥击本体**打中了谁（v181）。
        /// 剑气的判定框现在从玩家脚下起算，与近战扇形**必然重叠** ⇒
        /// 不记这份名单的话，框内的敌人会被剑气再打一次（隐蔽的双倍伤害）✗
        /// 每次 `DoHoeSwing` 开头清空 ⇒ 双击的第二刀有自己的一份名单 ✓
        /// </summary>
        private readonly HashSet<int> _meleeHitIds = new HashSet<int>();

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v211：锄头挥击的「**持久判定窗口**」★★
        //
        // 用户原话："锄头的攻击总是出现特效碰到但没伤害的情况，延长攻击判定存在时间，
        //            但确保不要重复触发；快速修改"
        //
        // ── 病因（改前的事实，按代码顺序）──
        //   `DoHoeSwing` 在**挥击那一帧**把整个 `World.Enemies` 扫一遍（见 `MeleeHitScan`），
        //   然后就**再也不判了** ✗ 而挥击特效是**跨帧存在**的（`WorldView` 那边按
        //   `MeleeSwingEvent` 播一段弧）⇒ 特效还在画面上飘的那段时间里，判定已经收摊了 ✓
        //   于是玩家看到的就是：**怪明明在弧里、特效压在身上，血条却不动** ✓
        //   · 不是几何算错（`range 3.8 + e.Radius` 与当时的 ±50° 都对；v222 已放宽到 ±90°）
        //   · 不是特效太大（v205 已把视觉收进判定里：4.090m < 4.305m、96° < 100°）
        //   ⇒ 是**判定只活一帧**这一个事实 ✓
        //
        // ── 修法：把"一刀的判据"记下来，在窗口内**反复重跑**同一个判据 ──
        //   ① 挥击那一刻记下 `(起点, 朝向, range, 伤害, 暴击, 击退, 命中上限)`
        //   ② 窗口内每 `MeleeSwingScanInterval`（1/30s ≈ 每 2 帧）用**同一个** `MeleeHitScan` 重扫
        //      ★ 同一个函数，不是"照抄一遍判据" —— 两处数字分家的病在本工程出现过太多次 ✓
        //   ③ 命中过的敌人记进 `_meleeWinHitIds`（`HashSet<int>`，键 = `EnemyActor.Id`）
        //      ⇒ 重扫时**跳过**他 ⇒ 这就是用户要的"不要重复触发" ✓
        //      ★ 不用 `List.Contains` 线性扫：怪多时那是 O(n²) ✓
        //   ④ 窗口结束 ⇒ 集合清空（下一次挥击有自己的新的一份）
        //
        // ── 三条不许动的线 ──
        //   · **伤害公式一个字不动**：窗口里重扫时用的是**这一刀已经算好的** `swingDmg` / `crit`，
        //     不重掷暴击、不重算倍率 ⇒ 还是走 `World.DealDamage` 那一扇唯一的门 ✓
        //     （重掷暴击会同时破坏"每次挥击各判一次"与确定性重放 ✓）
        //   · **挥击视觉不动**：`MeleeSwingEvent` 那边一个字段没碰 ✓
        //   · **命中上限不变**：`hoe.hits`（=3）现在由一个窗口共享，不是"每次重扫各 3 个"
        //     ⇒ 一刀最多打到的人数与改前完全一样 ✓
        //
        // ── 时间源 ──
        //   窗口按**会话固定步长**倒数（`TickMeleeWindow(dt)`，dt 就是 `World.Simulate` 里
        //   `Time += dt` 用的那个 dt）⇒ 与 `World.Time` 同一套口径，与 `Time.time` 无关 ✓
        //   暂停 / 选卡 / 商店期间 `FixedTick` 早退 ⇒ 窗口**一并不走**（不会"暂停时特效还在打人"）✓
        //   到期时刻的 `World.Time` 读数见 `MeleeWindowEndWorldTime` ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **判定窗口时长**（秒）——"这一刀打得到人"持续多久。
        ///
        /// 取值依据：用户给的区间是 **0.18~0.25s**，取中点 **0.22s** ✓
        /// · 与挥击弧的可见时长同量级 ⇒ "特效还在，判定也还在" ✓
        /// · **必须 ≤ 挥击冷却**：`tuning.json` 的 `tools.hoe.cooldown = 0.7s`（攻速 1.0）⇒
        ///   0.22 ≪ 0.7，窗口绝不会盖住下一次挥击、更不会让"攻速"变成两层收益 ✓
        /// · 双击的第二刀落在 `0.2 ÷ 攻速`（攻速 1.0 ⇒ 0.2s）——比窗口短一点点，
        ///   这是**故意的**：那一刀自己会开一个新窗口，`_meleeWinHitIds` 随之清空 ⇒
        ///   "两刀各打一次"成立，且不会出现"同一刀打两遍" ✓
        /// </summary>
        public const float MeleeSwingWindowSeconds = 0.22f;

        /// <summary>
        /// 窗口内**重扫间隔**（秒）—— 60fps 下约每 2 帧一次（0.22s 里约 6 次）。
        ///
        /// 为什么不逐帧扫：站桩怪一帧只位移几毫米，逐帧重扫纯属浪费；
        /// 而"怪走进扇形"是**连续**的，1/30s 的采样对 0.22s 的窗口已经足够密
        /// （走 0.033s 的位移量远小于 `e.Radius` 量级）✓
        /// </summary>
        public const float MeleeSwingScanInterval = 1f / 30f;

        /// <summary>窗口是否还开着（探针/自检读数用）。</summary>
        public bool MeleeWindowActive => _meleeWinActive;

        /// <summary>窗口还剩多少秒（探针读数用）。</summary>
        public float MeleeWindowSecondsLeft => _meleeWinLeft;

        /// <summary>
        /// 窗口到期时刻，**`World.Time` 口径**（探针/日志读数用）。
        ///
        /// ★ 它是一个**派生值**（`World.Time + _meleeWinLeft`），不是第二份状态 ★
        /// 理由：`TickCombat` 里 `_meleeWinLeft` 每帧减 dt、而 `World.Simulate` 里的
        /// `World.Time` 每帧加**同一个** dt ⇒ 两者之和在窗口打开后是**常数** ✓
        /// 所以这个数只在一处计算（这里），不会与倒数脱钩 ✓
        /// </summary>
        public float MeleeWindowEndWorldTime => World != null ? World.Time + _meleeWinLeft : 0f;

        /// <summary>本窗口还剩几个"打谁"的名额（= `hoe.hits` 减去已打中的）——探针读数用。</summary>
        public int MeleeWindowHitsLeft => _meleeWinHitsLeft;

        /// <summary>本窗口已经命中过几个不同敌人（= 去重集合的大小）——探针读数用。</summary>
        public int MeleeWindowHitCount => _meleeWinHitIds.Count;

        private bool _meleeWinActive;
        private float _meleeWinLeft;      // 剩余秒数（口径见 `MeleeWindowEndWorldTime`）
        private float _meleeWinScan;      // 距离下一次重扫还有多久
        private Vector2 _meleeWinPos;     // ★ 挥击那一刻的起点（不跟玩家走：判定与特效同一处）★
        private Vector2 _meleeWinDir;     // ★ 挥击那一刻的朝向 ★
        private float _meleeWinRange;
        private float _meleeWinDmg;       // ★ 这一刀**已经算好**的伤害（不重掷）★
        private bool _meleeWinCrit;       // ★ 这一刀的暴击标记（同上：不重掷）★
        private float _meleeWinKnock;
        private WeaponModifiers _meleeWinMods;   // 命中后效果（灼烧/减速/吸血）也要照旧
        private PlayerActor _meleeWinSrc;        // 伤害来源（吸血回他）
        private int _meleeWinHitsLeft;           // 窗口共享的命中名额（= `hoe.hits`）

        /// <summary>
        /// **本窗口已经打过的敌人**（键 = `EnemyActor.Id`）。
        /// ★ 这就是"确保不要重复触发"的全部实现 ★ —— 重扫时 `Contains` 就跳过，
        /// 因此同一敌人同一窗口**只掉一次血**（不管它在扇形里站了多久）✓
        /// 窗口结束立刻清空（`EndMeleeWindow`）⇒ 不会跨窗口残留，也不会误伤下一次挥击 ✓
        /// </summary>
        private readonly HashSet<int> _meleeWinHitIds = new HashSet<int>();

        /// <summary>
        /// ★ v283：`MeleeHitScan` 的**可复用候选缓冲**（按距离排序用）★
        ///
        /// 为什么要有它：`MeleeHitScan` 会在判定窗口内被**每 1/30 秒重扫一次**，
        /// 每帧 new 一个数组/List 会造成持续 GC。这里只在敌人数量变大时扩一次，之后全程复用。
        /// 容量按"当前最大曾出现敌人数"增长，够用即可（敌人数量有上限，不会无限涨）。
        /// </summary>
        private EnemyActor[] _meleeScanBuf;

        /// <summary>
        /// ★ v283：`MeleeHitScan` 距离排序用的**距离快照**（与 `_meleeScanBuf` 同索引）★
        /// 单独存一份 d² 避免排序过程中反复算 `Vector2` 距离（排序比较次数是 O(m²) 级的）。
        /// </summary>
        private float[] _meleeScanDist;

        /// <summary>
        /// 双击的**待执行刀**：只存"还要等多久"。
        /// ★ 为什么不在挥击时就先把伤害算好 ★
        /// 规格要求"各自独立结算（各判暴击）"—— 若在排入队列时就把 dmg/crit 算好，
        /// 两刀吃的是**同一个随机数**，那就不是"各判"，而是"一刀算两次"✗
        /// 所以这里只排队，真正开打时（`DoHoeSwing`）才掷暴击、才算伤害 ✓
        /// </summary>
        private struct PendingSwing { public float T; }
        private readonly System.Collections.Generic.List<PendingSwing> _pendingSwings =
            new System.Collections.Generic.List<PendingSwing>();

        private void SwingHoe(PlayerActor p, TuningDef t)
        {
            StatBlock st = Progress.Stats;
            ToolDef hoe = t.tools.hoe;
            WeaponModifiers pm = ModsFor(WeaponSlot.Hoe);

            // ★ 阶段 3：攻速**不再吃插件** ★（插件不改玩家属性 ⇒ 挥击间隔只由 AttackSpeed 决定）
            p.WeaponCds[PlayerActor.WeaponHoe] = hoe.cooldown / Mathf.Max(0.05f, p.AttackSpeed);

            // 范围只算一次：收割判定 / 表现事件 / 伤害判定共用同一个值 ✓
            float range = hoe.range * st.Get(StatId.AtkRange);

            // ═══ v27：锄头挥击 = 用攻击判定框检测成熟植物并收割（最高优先级，先于打敌人）═══
            int justHarvested = HarvestRipeInMeleeBox(p.Pos, p.AimDir, range, hoe.arc);

            // ★攻击形式★ 双击：`SwingCount = 1 + 插件额外刀数` ✓
            // 第 0 刀立刻打；第 k 刀排队等 `间隔 × k`，其中 **间隔 = 0.2 / 攻速** ✓
            //   ★ 需求③：改前是死常数 0.12s（不吃攻速），改后吃攻速 ⇒
            //     攻速 1.0 → 0.200s｜1.5 → 0.133s｜2.0 → 0.100s ✓
            int swings = pm.SwingCount;
            DoHoeSwing(p, t, pm, range);
            float gap = SwingGapFor(p.AttackSpeed);
            for (int k = 1; k < swings; k++)
                _pendingSwings.Add(new PendingSwing { T = gap * k });
            // ★ 声音：双击响应**一次**（而不是每一刀一次）★
            // 主挥击那一下已经有 `sfx_hoe_swing` 了；再给每一刀配音就会把
            // "第二刀 0.12s 后落下"这条规格听成一个独立的音效而不是同一刀的延续 ✗
            if (swings > 1) GameAudio.PlayPlugin(PluginEffectKind.DoubleStrike);

            if (justHarvested > 0) SetPrompt($"收割 {justHarvested} 株成熟植物");
        }

        /// <summary>每固定帧推进"双击的第二刀"。放在逻辑层，保证确定性重放一致。</summary>
        private void TickPendingSwings(float dt)
        {
            if (_pendingSwings.Count == 0) return;
            PlayerActor p = World != null ? World.Player : null;
            if (p == null) { _pendingSwings.Clear(); EndMeleeWindow(); return; }
            for (int i = _pendingSwings.Count - 1; i >= 0; i--)
            {
                PendingSwing s = _pendingSwings[i];
                s.T -= dt;
                if (s.T > 0f) { _pendingSwings[i] = s; continue; }
                _pendingSwings.RemoveAt(i);
                // 刀落下时**重新**算伤害与暴击（见 `PendingSwing` 的说明）
                DoHoeSwing(p, T, ModsFor(WeaponSlot.Hoe), T.tools.hoe.range * Progress.Stats.Get(StatId.AtkRange));
            }
        }

        /// <summary>
        /// **一刀**：扇形判定 + 伤害 + 剑气流。
        /// 挥击本体与双击的后续刀走的是同一个函数 ⇒ 两刀除了时间以外完全等价 ✓
        /// </summary>
        private void DoHoeSwing(PlayerActor p, TuningDef t, in WeaponModifiers pm, float range)
        {
            StatBlock st = Progress.Stats;
            ToolDef hoe = t.tools.hoe;

            // 近战判定框特效（表现层订阅）—— 每一刀都发 ⇒ 双击在画面上就是"闪两下"
            // ★ v205：发出去的是**视觉**几何（半径 ×0.95、张角 −4°）★
            //   ⇒ 画面上那条弧**一定落在打得到的范围里面**（用户需求："判定框覆盖特效，且实际大于特效一点"）
            // ★ v222：判定那边**不再读** `hoe.arc`（固定 180° 半圆）⇒ 视觉只负责"扫出去的方向"，
            //   不负责标出判定边界；`MeleeSwingEvent` 的字段与数值**一个字没动** ✓
            Bus.Publish(new MeleeSwingEvent
            {
                Pos = p.Pos, Dir = p.AimDir,
                Range = MeleeFxRadius(range),           // v273: 3.729m（判定外径 4.503m）
                ArcDeg = MeleeFxArcDeg(hoe.arc),        // 96°（判定 100°）
                Fx = PluginFx.For(pm, WeaponSlot.Hoe),
                // ★ v304：巨型 ⇒ 整圈。**纯插入**：这一行不改变任何判定/伤害，
                //   只是把"这一刀是整圈"这件事告诉表现层（见 `MeleeSwingEvent.FullRing`）。
                //   `pm.GiantRing` 由 `WeaponSystem.Aggregate` 的 `case PluginEffectKind.Giant` 置真 ⇒
                //   没装 ho_giant 时它恒为 false ⇒ 半圆路径逐位不变 ✓
                FullRing = pm.GiantRing
            });

            // ★ 阶段 3：**伤害公式的入参里没有一个来自插件** ★
            // v162（A3）：锄头 → **近战伤害**；公式 = (基准伤害 + 近战伤害) × 伤害倍率
            //
            // ★ v184：**去掉 `× ToolMult(品质)` 这一层乘法** ★（用户需求①）
            // 旧写法 `hoe.baseValue * ToolMult(ToolHoe)` 是把品质**又乘了一次**，
            // 于是同一件武器的伤害有两个来源（基础值 + 品质倍率）✗
            // 用户的定义只有一条：`(武器基础伤害 + 近战伤害) × 伤害倍率` ——
            // 品质应当体现在**基础值本身**（`tuning.json` 的 `tools.hoe.baseValue`），
            // 而不是在公式里再插一层 ✓
            // ⚠️ 数值上这次改动**零漂移**：`quality.mult` 当前是 [1,1,1,1,1] ⇒ `ToolMult` 恒为 1 ✓
            //    （`ImGuiHud` 的武器卡仍会印出"× 品质 1.00"，那个文件不在本任务范围内）
            float baseValue = hoe.baseValue;
            float damageStat = st.Get(StatId.MeleeDamage);
            float atkMult = st.Get(StatId.AtkMult);
            float critDmg = st.Get(StatId.CritDmg);

            // ★ v184：暴击掷骰收敛到 `DamageResolver.RollCrit`（它内部钳 0..1）★
            // 旧版这里自己写 `Mathf.Clamp01(...)` + `Rng.Chance(...)`，水壶那边又抄一遍 ✗
            bool crit = DamageResolver.RollCrit(ref World.Rng, st.Get(StatId.CritRate));   // ★ 每一刀各判一次
            // ★ v218：**暴击 ⇒ 轻微镜头抖动**（用户原话："暴击带来轻微抖动"）★
            //   只加这一行调用：掷骰与伤害计算一个字没动 ✓
            //   抖动走 `CombatFeedback` 的 trauma 累加器（不是直接动相机）⇒ 与爆炸同时发生时
            //   按 `trauma²` 叠成一次更大的抖，而不是两次相加失控 ✓
            if (crit) CombatFeedback.ShakeCrit();
            float dmg = DamageResolver.WeaponDamage(baseValue, damageStat, atkMult, crit, critDmg,
                                                    t.formula.minDamage);
            // ★ v197「富豪」：这一刀也吃"1% 金币转额外伤害"（四条武器路径逐条接，见 `WithGoldDamage`）
            dmg = WithGoldDamage(p, dmg);
            // ★ v189（用户需求④）：**多次挥舞 = 每层把这一刀 ×2/3（累计相乘）** ★
            //   0 层 ×1.000｜1 层 ×0.667｜2 层 ×0.444｜3 层 ×0.296
            //   层数来自 `DoubleStrikeLayers`（不是 `Swings - 1`，理由见那个字段的注释）✓
            //   这一档卖的是"分几刀"（每刀各判暴击、各叠一次灼烧/减速），
            //   不是"凭空多一倍伤害"⇒ 一次挥击的总伤害是收敛的：
            //     1 层 2 刀 = 1.333｜2 层 3 刀 = 1.333｜3 层 4 刀 = 1.185（相对单刀）✓
            float swingDmg = WeaponSystem.DoubleStrikeDamage(dmg, pm.DoubleStrikeLayers);
            float knock = st.Get(StatId.Knockback) * 0.5f;
            // ★ v181：剑气要用的"近战框半宽"与近战判定**共用同一个式子**（见 `MeleeBoxHalfWidth`）
            float meleeHalfWidth = MeleeBoxHalfWidth(range, hoe.arc);
            _meleeHitIds.Clear();                          // 这一刀自己的命中名单（双击的第二刀重新记）
            // ★ v211：**开窗口 + 本帧立刻判一次** ★
            //   本帧这一下与改前**逐字相同**（同一个 `MeleeHitScan`、同一个 `range`），
            //   改的只是"判完之后判定不立刻死掉"——它现在还要活 `MeleeSwingWindowSeconds` ✓
            //   `hoe.hits` 作为**窗口共享**的命中上限传进去（一刀最多还是打 3 个）✓
            // ★ v222：`hoe.arc` **不再**传给判定 —— 半张角固定 90°（`MeleeRingHalfArcDeg`），
            //   判定与特效参数解耦（见上面那块形状说明）；`hoe.arc` 仍用于特效视觉与剑气 ✓
            OpenMeleeWindow(p, swingDmg, crit, knock, pm, range, hoe.hits);
            MeleeHitScan(p, p.Pos, p.AimDir, range, swingDmg, crit, knock, pm);

            // ★攻击形式★ 剑气流：`SwordWaveCount` 道/层
            // ★ v189：**剑气伤害 = 这一刀的 50%**（用户需求④"只继承锄头伤害的 50%"）★
            //   传进去的是 `swingDmg`（这一刀**实际**打出去的伤害）⇒
            //   双击 2 层时剑气 = 0.444 × 锄头 × 0.5 = **0.222 × 锄头** ✓
            int waves = Mathf.Max(0, pm.SwordWaveCount);
            if (waves > 0)
                FireSwordWaves(p, pm, waves, WeaponSystem.SwordWaveDamage(swingDmg), crit,
                               range, meleeHalfWidth, knock);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v211：持久判定窗口 —— 三个函数 + 一套判据 ★★
        //
        //   `OpenMeleeWindow`  挥击那一刻**记下**这一刀的全部判据，并开窗
        //   `MeleeHitScan`     **唯一的判据本体** —— 挥击那一帧与窗口内每次重扫都走它
        //   `TickMeleeWindow`   每固定帧倒数 + 到点重扫 + 到期收窗（内部按 1/60 切子步）
        //   `EndMeleeWindow`    收窗并**清空去重集合**
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 挥击那一刻：把"这一刀"存下来，然后开一个 `MeleeSwingWindowSeconds` 的窗口。
        ///
        /// ★ 存的是**已经算好的** `dmg` / `crit`，不是"入参"★
        /// 窗口内重扫若重掷暴击，一次挥击就会掷出好几次暴击判定 ⇒
        /// 既破坏了"每一刀各判一次"（这条是 v184 定下的），也会让同种子的重放结果发散 ✗
        /// 所以伤害与暴击在**挥击那一帧结算一次**，窗口只是"把同一下打到更多/更晚进来的人"✓
        /// </summary>
        private void OpenMeleeWindow(PlayerActor p, float dmg, bool crit, float knock,
                                     in WeaponModifiers pm, float range, int hitCap)
        {
            _meleeWinActive = true;
            _meleeWinLeft = MeleeSwingWindowSeconds;
            // 本帧刚刚判过一次 ⇒ 下一次重扫等一个间隔（不是立刻再判一遍）
            _meleeWinScan = MeleeSwingScanInterval;
            _meleeWinPos = p.Pos;
            _meleeWinDir = p.AimDir;
            _meleeWinRange = range;
            _meleeWinDmg = dmg;
            _meleeWinCrit = crit;
            _meleeWinKnock = knock;
            _meleeWinMods = pm;
            _meleeWinSrc = p;
            _meleeWinHitsLeft = Mathf.Max(1, hitCap);   // 与改前同一个下限（至少打得中 1 个）
            _meleeWinHitIds.Clear();                    // 新的一刀 ⇒ 新的一份去重集合
            // ★ v282：这一刀 = 一个吸血批次 —— 窗口内所有重扫到的敌人**累加**，收窗时结算一次 ✓
            LifestealBegin(p);
        }

        /// <summary>
        /// **这一刀打得到谁**（唯一的判据本体，v211 从 `DoHoeSwing` 里原样搬出来）。
        ///
        /// 参数化的只有"从哪判"（`origin` / `dir`）：
        ///   · 挥击那一帧传 `p.Pos` / `p.AimDir` ⇒ 与改前**逐字相同** ✓
        ///   · 窗口重扫传 `OpenMeleeWindow` 记下的那一对 ⇒ 判定跟着**特效**走（不跟玩家走）✓
        ///     这一点是"延长存在时间"的关键：特效留在挥出的位置，判定也必须留在那里，
        ///     否则玩家往前走一步，判据就会跟着挪到另一个地方去打人 ✗
        ///
        /// 与改前的四处差别（全部是加上去的，没有一处放松既有限制）：
        ///   ① 先进去重集合查一次（`_meleeWinHitIds`）⇒ 同一窗口内**不重复触发** ✓
        ///   ② 命中名额改由**窗口共享**（`_meleeWinHitsLeft`）⇒ 一刀最多还是 `hoe.hits` 个 ✓
        ///   ③ 返回"这一次扫到几个"（探针/自检读数用，不参与判定）✓
        ///   ④ ★ v222 ★ 判据本体的**形状** = `MeleePointHit` 里的**内半径 0、总张角 180° 的半圆**
        ///      （v220 的"单一矩形框"、v221 的"圆环扇区"都已删除）；循环结构、去重、名额、伤害、
        ///      `PluginPostHit` **全部逐字未动** ⇒ 换形状只改"够不够得着"，不改"打多重、打几次" ✓
        /// </summary>
        private int MeleeHitScan(PlayerActor p, Vector2 origin, Vector2 dir, float range,
                                 float dmg, bool crit, float knock, in WeaponModifiers pm)
        {
            int hits = 0;
            // ★★ v283：**命中名额改成"最近优先"** ★★
            //
            // 病灶（用户报的"多次挥砍只有第一次做判定，后续刀敌人进范围也不计算"）：
            //   原实现按 `World.Enemies` 的**出生顺序**遍历（`Actors` 里那个 List 只 Add/RemoveAt，
            //   从不按距离排序），而名额（`hoe.hits`，默认 3）由**最先遇到的 3 个**吃掉。
            //   ⇒ 新刷出/新走进弧内的敌人在表尾，只要老的 3 个还在弧内，新人**永远轮不到名额**。
            //   双击/多段只是把这个症状放大成"第 2 刀看起来没判定"——其实每刀都重扫了，
            //   只是每刀都重新挑到同一批最老的 3 个。
            //
            // 修法：先收集**本窗口还没打过的**候选，再**按距离升序**（d² 相同时按 `Id` 兜底，
            //   保证同一输入永远同一结果 = 确定性不变）消耗名额。
            //   ⇒ 贴脸进圈的敌人立刻能吃到名额，不再被"出生早"的老怪挤掉。
            //
            // 不变的东西：一刀最多仍 `hoe.hits` 个、每刀伤害/暴击/`PluginPostHit`/去重集合全部照旧，
            //   总伤害上限不变 —— 只是"名额给谁"从"谁先出生"改成"谁更近"。
            {
                int n = World.Enemies.Count;
                if (_meleeScanBuf == null || _meleeScanBuf.Length < n)
                {
                    // ★ 扩容**留余量**：原来按 `n` 精确分配 ⇒ 敌人数量在同屏每多 1 只就
                    //   `new` 一次（高峰期=每 1/30s 一次 GC）✗ 现在按倍增 + 下限 16 取，
                    //   与工程里 `List` 的扩容习惯一致 ⇒ 稳态**完全零分配** ✓
                    int prev = _meleeScanBuf == null ? 0 : _meleeScanBuf.Length;
                    int cap = Mathf.Max(16, Mathf.Max(n, prev * 2));
                    _meleeScanBuf = new EnemyActor[cap];
                    _meleeScanDist = new float[cap];
                }
                int m = 0;
                for (int i = 0; i < n; i++)
                {
                    EnemyActor e = World.Enemies[i];
                    if (e == null || !e.Alive) continue;
                    if (_meleeWinHitIds.Contains(e.Id)) continue;   // ★ v211：本窗口已经打过他 ⇒ 不进候选
                    _meleeScanBuf[m] = e;
                    Vector2 d0 = e.Pos - origin;
                    _meleeScanDist[m] = d0.sqrMagnitude;
                    m++;
                }
                // 插入排序：m 一般就是"同屏敌人数量"，且大多已经接近有序 ⇒ 实测远快于 Array.Sort + 比较器
                // （比较器会装箱/委托调用，在这个每 1/30 秒跑一次的热点上不划算）
                for (int i = 1; i < m; i++)
                {
                    EnemyActor curE = _meleeScanBuf[i];
                    float curD = _meleeScanDist[i];
                    int j = i - 1;
                    while (j >= 0)
                    {
                        EnemyActor prevE = _meleeScanBuf[j];
                        float prevD = _meleeScanDist[j];
                        // 更远的排后面；距离相等时 `Id` 小的排前面（确定性兜底）
                        bool swap = prevD > curD || (prevD == curD && prevE.Id > curE.Id);
                        if (!swap) break;
                        _meleeScanBuf[j + 1] = prevE;
                        _meleeScanDist[j + 1] = prevD;
                        j--;
                    }
                    _meleeScanBuf[j + 1] = curE;
                    _meleeScanDist[j + 1] = curD;
                }

                for (int k = 0; k < m && _meleeWinHitsLeft > 0; k++)
                {
                    EnemyActor e = _meleeScanBuf[k];
                    // ★ 轮到他之前必须**再查一次存活**：收集候选之后、消费名额之前，
                    //   他可能已经被这一刀更近的目标顺手打死（`PluginPostHit` 的「收束」补刀
                    //   与「弹射」都会打别人）⇒ 不查的话死人会白吃一个名额：
                    //   `DealDamage` 只会返回 Miss，但 `_meleeWinHitsLeft--` 与
                    //   `PluginPostHit`（吸血按"伤害"回血）都会照走 ✗
                    //   改前那句 `if (!e.Alive) continue;` 就是这条守卫，排序后它被挪到了
                    //   收集阶段 ⇒ 这里补回来 ✓
                    if (e == null || !e.Alive) continue;
                    Vector2 delta = e.Pos - origin;
                    float dist = delta.magnitude;
                    // ★ v205：`e.Radius` 已含 `HitRadiusScale 1.486`；视觉那边取
                    //   `MeleeFxRadius(range)` = 判定半径对最瘦敌人的值 × 0.95 ⇒ 特效永远在内侧 ✓
                    //
                    // ★ v222：判据本体在 `MeleePointHit` 里，是一个**内半径 0、总张角 180° 的半圆** ★
                    //   `dist ≤ MeleeRingOuter(range) + r`（内沿 `MeleeRingInner = 0` ⇒ 恒真）
                    //   ∧ `Dot(delta/dist, dir) ≥ cos(90°)`（= 0，已钳）
                    //   ＋ 圆心重合（`MeleeCoincidentDist`）必中的例外 ✓
                    //   ★ 一个敌人一次迭代只问**一次** bool ⇒ 绝不会让同一个人结算两次；
                    //     去重仍是上面的 `_meleeWinHitIds.Contains` 与下面那两个 `Add` ✓
                    if (!MeleePointHit(range, dir, delta, dist, e.Radius, pm.GiantRing)) continue;
                    World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical,
                        RawValue = dmg,          // ★ v189：这一刀的伤害（已吃多次挥舞的 ×2/3）
                        Source = p,
                        Target = e,
                        Knockback = knock,
                        Crit = crit            // v87：把暴击标记传下去，弹字才会走暴击样式
                    });
                    PluginPostHit(e, dmg, pm, p);      // ★ 灼烧 / 减速 / 吸血（按这一刀的实际伤害算）
                    _meleeHitIds.Add(e.Id);            // v181：剑气不再重复打这个人
                    _meleeWinHitIds.Add(e.Id);         // v211：本窗口不再重复打这个人
                    _meleeWinHitsLeft--;               // 名额是"窗口"的，不是"这一次扫描"的
                    hits++;
                }
                // 清掉引用，避免刚刚被打死的敌人被缓冲区多留一帧（不是正确性问题，只是别拖着对象）
                for (int i = 0; i < m; i++) _meleeScanBuf[i] = null;
            }
            return hits;
        }

        /// <summary>
        /// 推进判定窗口 `<paramref name="dt"/>` 秒。
        ///
        /// ★ 按 `GameClock.FixedDt` 切子步 ★
        /// 实时帧的 dt 本来就是 1/60 ⇒ 一次循环就完事；
        /// 而 `DebugAdvanceAttack(0.5f)`（自检/探针一次推半秒）会被切成 30 步 ⇒
        /// "重扫节奏"与实时帧**逐帧一致**，探针读数才对得上实机 ✓
        /// （同一个理由在 `TickSwordWaves` 里已经写过一遍，见那里的子步注释）
        /// </summary>
        private void TickMeleeWindow(float dt)
        {
            if (!_meleeWinActive || dt <= 0f) return;
            float rest = dt;
            while (rest > 1e-6f)
            {
                float step = Mathf.Min(rest, GameClock.FixedDt);
                rest -= step;
                StepMeleeWindow(step);
                if (!_meleeWinActive) return;
            }
        }

        /// <summary>窗口的一小步：倒数 → 到点重扫 → 到期收窗。</summary>
        private void StepMeleeWindow(float step)
        {
            _meleeWinLeft -= step;
            if (_meleeWinLeft <= 0f) { EndMeleeWindow(); return; }   // 到期 ⇒ 判定结束（含去重集合清空）
            _meleeWinScan -= step;
            if (_meleeWinScan > 0f) return;
            _meleeWinScan = MeleeSwingScanInterval;
            if (_meleeWinSrc == null) { EndMeleeWindow(); return; }
            // ★ 重扫用的全是**挥击那一刻**记下的判据与数值 ⇒ 这一刀不会因为时间过去而变强/变弱 ✓
            MeleeHitScan(_meleeWinSrc, _meleeWinPos, _meleeWinDir, _meleeWinRange,
                         _meleeWinDmg, _meleeWinCrit, _meleeWinKnock, _meleeWinMods);
        }

        /// <summary>收窗：判定不再存在，去重集合清掉（用户要求："窗口结束就清掉"）。</summary>
        private void EndMeleeWindow()
        {
            _meleeWinActive = false;
            _meleeWinLeft = 0f;
            _meleeWinScan = 0f;
            _meleeWinHitsLeft = 0;
            _meleeWinSrc = null;
            _meleeWinMods = default;
            _meleeWinHitIds.Clear();
            // ★ v282：收窗 = 这一刀的吸血事件结束 ⇒ **按窗口内的总伤害结算一次** ★
            //   放在最后：去重集合/名额的清空与吸血结算互不依赖，但"收窗就结账"这条语义要有唯一一处 ✓
            LifestealSettle();
        }

        // ──────────────────────────────────────────── v90：水壶 = 持续激光
        //
        // 旧版水壶走的是**通用武器冷却**：攻击一次 → 扣一次弹药 → 等 CD。那是"点按"的模型。
        // 需求要的是**连续激光**：按住就一直在，于是三件事都要跟着改：
        //   ① 判定脱离冷却，改成按固定帧持续判定
        //   ② 每个受判对象**独立 CD** —— 否则同一个敌人每帧掉血，一秒 60 次
        //   ③ 充能改成**按时间扣**：起手付 10，之后每 0.5 秒付 10
        //
        // 状态放在逻辑层（RunSession）而不是表现层：它参与判定，必须进确定性重放。

        /// <summary>
        /// 水壶一次扣费的水量（**每道**）。
        /// ★ 用户需求①："修改水壶多水柱插件效果…水分消耗量加 10" ★
        /// 改前 **10** → 改后 **20**（10 基线 + 10）。多柱仍然按**道数**乘（见 `TickBeam` 的 `cost`）——
        /// 这一条没有改：一次判定 N 道就付 N 份 ✓
        /// 实测"连射 1 秒的耗水"（同一个探针、同一个水箱上限 100）：
        ///   裸枪 1 道：**26 → 46**｜双柱 2 道：**46 → 86**（每 0.5 秒扣一次 ⇒ 1 秒多花 20 点）✓
        /// </summary>
        // ★★ v216：`private` → `internal` —— HUD 的水壶卡要**读真的那个数**，不再自带镜像 ★★
        //   改前 `ImGuiHud` 里有一份 `BeamWaterCostPerTick = 10` 的镜像常量（注释里写着
        //   "两个数是 RunSession 的 private const，UI 读不到"），而本常量早已是 **20**
        //   ⇒ 卡面印「耗水 10/0.5s（20/秒）」、实际扣 **40/秒** ⇒ 玩家看到水条掉得比写明的快一倍，
        //     报成"多柱还有额外消耗"✗（`evade_helm` 那条"镜像值与真值分家"的同一个形状）
        //   ⇒ 现在 UI 直接读这里，**没有第二份数**，也就没有"改了没跟上"的可能 ✓
        internal const int BeamAmmoCost = 20;
        internal const float BeamAmmoInterval = 0.5f;   // 秒 / 次扣费
        private const float BeamPerTargetCd = 0.5f;    // 同一目标的再受伤间隔

        /// <summary>
        /// ★ v167：**每 1 点弹药能补多少地块蓄水** = 50。
        ///
        /// ★ 为什么会有这个常数 ★
        /// 原设计里"给地块补水"**不扣任何东西**（`FarmPlot.AddWater` 白送），
        /// 而扣弹药的只有"持枪"这一件事（10 / 0.5 秒）⇒
        /// 于是"浇地"与"开枪"**不是两次扣费**，玩家摸到地就顺手浇满 ✗
        /// ⇒ 策划案里写的"要么浇地、要么当弹药"这个**二选一实际不成立** ✓
        /// 这一条改动就是把那个取舍**真的接上**：水给地了就少了开枪的份 ✓
        ///
        /// ★ 只对**真的送出去的水**收费 ★（最自然的一种，也免掉一套计时器）
        /// `AddWater` 返回的是**实际加进去的量** ⇒ 地块满时返回 0 ⇒ **一分钱不扣** ✓
        /// 所以不需要"是否缺水"的额外判断，也不需要节流：满水的地自然免费 ✓
        ///
        /// ★ 为什么是 50（算式用**当前**的耕地模型，不是旧模型）★
        /// v166 的耕地是 **6 列 × 行数**（`farm.gridSize=6`、开局 `initialRows=3` ⇒ 18 格）✓
        /// 而"**一列 = 一株植物**"，种下列首时**同列其余格自动补齐**（`FarmGrid.FillRows`）
        /// ⇒ 播 1 份种子 = **3 格同时生长**，且**每格各有一份蓄水与 Drain** ✓
        ///   （`BeginPlant(..., head.WaterMax, head.WaterDrain)` 是**逐格**设置 ✓）
        ///
        ///   每格消耗：`TickGrowth2` 每 **5 秒**扣 `waterDrain=60`（种子表 60~75）⇒ **12 水/秒** ✓
        ///   ⇒ 1 份种子（3 格）= **36 水/秒**；行数长到 6 后 `FillRows` 补齐到 6 格 = **72 水/秒** ✓
        ///   价格 50 水/弹药 ⇒ 1 份种子 **≈0.72 弹药/秒** ⇒ 60 秒波 **≈43 弹药** ✓
        ///     · 1 份种子（开局）：≈ 43/波 ⇒ 供给（≈100）的一半 ⇒ **边打边浇可行** ✓
        ///     · 2 份：≈ 86/波 ⇒ 基本吃满 ✓
        ///     · 3 份：≈ 129/波 ⇒ 超过供给 ⇒ 必须放掉一块 ✓
        ///     · 6 份（满列）：≈ 259/波 ⇒ 只养得起 1/3 ⇒ **弃地是必然** ✓
        ///   对照 `waterPerHit=10`：补满**一格**（100 水）= **2 弹药** ⇒ 1 份种子补满 = 6 弹药 ✓
        ///
        ///   ⚠️ 我先前按"1 块地 = 1 个浇水目标"算成 25 ✗ —— 那是**旧模型**（一格一份种子）✓
        ///      现在一份种子是**3 格** ⇒ 水费乘 3 ⇒ 25 会让 1 份种子就吃掉整波供给 ✗
        ///      所以这里是 **50**：同一条曲线，换算到**当前的格/列尺度** ✓
        ///
        ///   ⚠️ 一条算不平的东西（先说清楚，别以为这个数能两头都满足）：
        ///     "一次扫射覆盖 1 块地"的**单次**加价只有 +0.4~2 弹药 ✓
        ///     相对"持枪 20 弹药/秒"并不"显著更贵" ✓
        ///     因为送水速度（`waterPerHit=10` × 60 帧 = 600/秒）仍是单格消耗（12/秒）的 50 倍 ✓
        ///     ⇒ 真要"一次扫射显著更贵"，该动的是**送水速度**或加节流，
        ///       而不是这里的价格（价格只决定**总量**曲线）✓
        ///     `waterPerHit` 50→10 已经是往那个方向的第一步：补水不再"两帧补满"✓
        /// </summary>
        private const float WaterPerAmmo = 50f;

        /// <summary>
        /// v167：**已送出、但还没攒够一整点弹药的水**。
        ///
        /// 为什么需要它：一次补水只倒 `waterPerHit=10` 水 ✓
        /// 若每帧按 `ceil(10/50)=1` 扣 ⇒ 补满一格要扣 10 点（实际只该 2 点）✗ 价格虚高 5 倍 ✓
        /// ⇒ 把送出去的水**累计**起来，攒够 `WaterPerAmmo` 才扣 1 点 ✓
        ///   于是单价**精确**等于"1 弹药 / 50 水"，与补水频率无关 ✓
        ///
        /// 跨波不清零：残留最多 49 水（&lt; 1 弹药）✓ 清了反而要解释"为什么这波的水白送了"✓
        /// </summary>
        private float _waterUnbilled;

        private bool _beamFiring;
        // v123 清理：这里原本被我误加了一份 `_pigSpawnedThisWave` 的重复声明
        // （刷怪逻辑在**嵌套类**里，字段必须声明在那个类中，见 _pending 旁边）。
        // 同时修复：之前一次编辑删掉了一个换行，把两个字段挤在了同一行。
        private float _beamAmmoTimer;
        private readonly System.Collections.Generic.Dictionary<int, float> _beamHitCd =
            new System.Collections.Generic.Dictionary<int, float>();
        private readonly System.Collections.Generic.List<int> _beamCdScratch =
            new System.Collections.Generic.List<int>();

        /// <summary>
        /// ★ v305：**多柱"共享"的读数**（只读；不参与判定）——
        /// 累计"本该多算、但被共享掉"的 (道, 敌人) 对数量。
        /// 读法：装 N 层多柱（= N+1 道）对着 1 个敌人喷一次 ⇒ 这一格 +N；
        ///   若它一直是 0，说明扇形让每道只罩自己的目标（覆盖是真的、重叠是 0）。
        /// 判据价值：它把"共享"从"靠 `_beamHitCd` 副作用"变成**可数的结构事实** ✓
        /// </summary>
        public int MultiJetSharedSkips { get; private set; }

        /// <summary>水壶连续激光的每固定帧推进。调用方保证"按住 + 装备水壶 + 不在耕作"。</summary>
        private void TickBeam(PlayerActor p)
        {
            // v100：扣费间隔与"同一目标的再受伤间隔"都**吃攻击速度**。
            //
            // 原本这两个都是常量，于是水壶是全场唯一一件不吃攻速的武器
            // （锄头 cooldown/AttackSpeed、土地操作 required/AttackSpeed 都吃）。
            // 攻速堆起来的玩家会明显感觉"水壶没跟着变强"。
            // 代价是耗水也跟着变快——这是对的：打得更快就该更耗弹药，
            // 否则攻速对水壶是纯收益，会变成唯一解。
            // ★ v171：攻速也吃插件（`can_turbine` 涡流叶轮）★
            // 扣费间隔与"同一目标的再受伤间隔"必须用**同一个**攻速式子，
            // 否则"打得更快"会变成纯收益（见上面 v100 那段说明）✓
            float aspd = Mathf.Max(0.05f, p.AttackSpeed);
            float interval = BeamAmmoInterval / aspd;

            // ★攻击形式★ **多柱**（`can_multijet`）：一次判定打 N 道 ⇒ ★ **水费不按道数翻倍** ★
            //
            // ── 三代口径（与 `WeaponSystem.MultiJetSharesOneCharge` 那段同一份说明）──
            //   v197 ✗ 每道 +5：(20+5)×道数 ⇒ 4 道 = 100 = 水箱上限，喷一次扣光
            //   v214 ✗ 不额外加价、但每道仍各付：20×道数 ⇒ 2 道 40 / 3 道 60 / 4 道 80
            //          ⇒ 用户原话（本轮）："多柱额外生成的攻击导致消耗量翻倍了，修复bug" ✗
            //   v217 ✓ **一次喷射的总耗 = 水壶本体 20，几道都一样**（多柱是"同一份水打更多道"）
            //          ⇒ 1/2/3/4 道都是 20 ｜水箱 100 ⇒ 能喷 **5 次**
            //   本行代码**一个字没改**（仍然调那个纯函数，口径只此一处）—— 改的是那个函数 ✓
            //
            // ⚠️ 多出来的那几道**不是免费伤害**，是**免费覆盖**：
            //   下面的 `FireBeam` 按 `beams = max(1, pm.JetCount)` 铺扇形，每个敌人有独立 CD
            //   （`_beamHitCd` 按敌人 id）⇒ 多一道 = 多罩一片位置，**同一敌人不会多吃几份** ✓
            WeaponModifiers canMods = ModsFor(WeaponSlot.Can);
            int cost = WeaponSystem.BeamWaterCostPerFire(BeamAmmoCost, canMods);
            // ★ 需求④：水球的触发点是"**扣水那一下**"，所以这里要记住这一帧到底有没有扣 ✓
            bool paidThisTick = false;

            if (!_beamFiring)
            {
                // 起手先付一次——不然第一帧白打
                if (WaterAmmo < cost) { SetPrompt("水壶没水了（拾取雨滴 +20）"); return; }
                WaterAmmo -= cost;
                paidThisTick = true;
                _beamAmmoTimer = 0f;
                _beamFiring = true;
            }
            else
            {
                _beamAmmoTimer += GameClock.FixedDt;
                if (_beamAmmoTimer >= interval)
                {
                    _beamAmmoTimer -= interval;
                    if (WaterAmmo < cost)
                    {
                        _beamFiring = false;      // 断了，下次按住要重新付起手费
                        SetPrompt("水壶没水了（拾取雨滴 +20）");
                        return;
                    }
                    WaterAmmo -= cost;
                    paidThisTick = true;
                }
            }
            FireBeam(p);
            // ★攻击形式★ **自充的水球**（需求④）：每扣一次水 ⇒ 发射 `层数 × 2` 颗，打最近的敌人 ✓
            // 放在 `FireBeam` **之后**：水球的伤害由"水壶一次命中的远程伤害"派生，
            // 而那个值与本帧的判定无关 —— 顺序不影响数值，只是让"扣水 ⇒ 发球"读起来是同一下 ✓
            if (paidThisTick) FireWaterBalls(p, canMods);
            // ★ v290：**臭水沟** —— 每累计消耗 20 点水掉一个巨大香蕉皮。
            //   钩子就在“**扣水那一下**”（与自充水球同一个判据：`paidThisTick`）⇒ 不是每帧都算，
            //   而且“每 20 点”是**累计计数**（不消耗随机数）✓
            if (paidThisTick) PluginExtra2.OnWaterSpent(p, canMods, cost);
        }

        /// <summary>松开攻击键时调用——让下一次按住重新收起手费。</summary>
        private void StopBeam()
        {
            _beamFiring = false;
            _beamAmmoTimer = 0f;
        }

        // ════════════════════════════════════════════════════════════════════════
        // ★ 用户需求④：**自充的水球**（`can_autocharge` 的新功能）★
        //
        // 需求原话："每次消耗水分时发射 2 颗水球攻击距离最近的敌人（可重复插件层数 ⇒
        //           增加水球数量），水球伤害为远程伤害的 50%，顺带制作水球特效"
        //
        // 三条边界的写法（写清为什么，免得下一个人以为是随手定的）：
        //
        //   ① **触发点 = "扣水那一下"**（`TickBeam` 里的 `WaterAmmo -= cost`），**不是每帧** ✗
        //      每帧发的话 1 秒 60 帧 × 2 颗/层 = 120 颗/层/秒 ⇒ 那不是"水球"，是弹幕游戏 ✓
        //      扣水 1 秒 2 次（`BeamAmmoInterval = 0.5 ÷ 攻速`）⇒ **2 颗/层/次 = 4 颗/层/秒** ✓
        //
        //   ② **目标 = 距离玩家最近的敌人**（需求原话），发射那一刻**取一次快照** ✓
        //      快照之后目标死了/走开了，球**继续朝那个点飞**、到点即散 —— 不改打别人 ✓
        //      （"改打别人"会让"最近"这个词失去意义，而且判定变得不可复现）
        //
        //   ③ **伤害走 `DamageResolver.WeaponDamage` 的同一个出口再乘 0.50** ✓
        //      即 `水球 = (水壶基础值 3 + 远程伤害) × 伤害倍率 × 0.50`
        //      —— 与"灼烧 = 近战命中 × 0.50 / 减速 = 远程命中 × 0.50"是**同一条惯例** ✓
        //      （若只乘 `StatId.RangedDamage` 这个裸属性，开局它是 0 ⇒ 水球 0 伤害 ✗
        //        详见 `WeaponSystem.WaterBallDamage` 那段）
        //
        //   ④ **直线飞行**（不追踪），速度与表现层**同一个常数** ⇒
        //      "看得见的落点 = 判定打到的落点"（与 v185 剑气同一条纪律）✓
        //      表现层拿 `Dist / Speed` 算寿命，慢放抓图时也不会"画到了、判定没到" ✓
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>水球飞行速度（米/秒）。略慢于水壶弹速 18 ⇒ 读得出"它在飞"，而不是一闪即中。</summary>
        public const float WaterBallSpeed = 14f;

        /// <summary>水球的命中半径（米，与敌人半径相加）。比水柱细，但比一根线粗。</summary>
        public const float WaterBallRadius = 0.32f;

        /// <summary>水球最长存活（秒）。超过即散 —— 防止目标绕圈时的无限追。</summary>
        public const float WaterBallLife = 2.5f;

        /// <summary>
        /// 同时在飞的水球上限。3 层自充 ⇒ 每次 6 颗、每秒 2 次 ⇒ 稳态 ≤ 12 颗；
        /// 取 48 是"连按打断 + 攻速 2.0 + 多柱"叠起来的兜底，正常打不满 ✓
        /// </summary>
        public const int WaterBallMaxLive = 48;

        /// <summary>相邻两颗水球的**出膛横向间隔**（米）—— 不这样错开，一轮 6 颗会叠成一颗。</summary>
        private const float WaterBallSpawnGap = 0.22f;

        /// <summary>子步进上限（秒）：与剑气同一条"不隧穿"的纪律（掉帧/大步进也逐帧等价）。</summary>
        private const float WaterBallMaxStep = 1f / 60f;

        /// <summary>一颗**直线飞行**的水球。射击时快照目标的当前位置，之后不再索敌。</summary>
        private struct WaterBall
        {
            public Vector2 Pos;      // 当前位置
            public Vector2 Dir;      // 单位方向
            public float Travel;     // 已飞距离
            public float Dist;       // 落点距离（= 出膛时的目标距离）
            public float T;          // 已存活时间
            public float Dmg;        // 伤害 = 水壶命中伤害 × 0.50
        }

        /// <summary>场上所有在飞的水球（一局的状态 ⇒ 跟 `RunSession` 走）。</summary>
        private readonly List<WaterBall> _waterBalls = new List<WaterBall>();

        /// <summary>调试/自检读数：现在有几颗水球在飞。</summary>
        public int LiveWaterBallCount => _waterBalls.Count;

        /// <summary>
        /// 调试/自检读数：本局一共发射过多少颗水球（不清零除了 `Start`）。
        /// ★ 它是"层数 ⇒ 发数"这条需求的**实测入口** ★ —— 不是复述公式，而是数真的发了几颗 ✓
        /// </summary>
        public int WaterBallSpawnedTotal { get; private set; }

        /// <summary>调试/自检读数：第 <paramref name="i"/> 颗水球的位置（世界 XZ）。</summary>
        public Vector2 WaterBallPosAt(int i)
            => i >= 0 && i < _waterBalls.Count ? _waterBalls[i].Pos : Vector2.zero;

        /// <summary>调试/自检读数：一颗水球的伤害值（同一轮里每颗都相同）。</summary>
        public float WaterBallDmgAt(int i)
            => i >= 0 && i < _waterBalls.Count ? _waterBalls[i].Dmg : 0f;

        /// <summary>清空在飞的水球（换局 / 跳分钟 / 收尾时用）。</summary>
        public void ClearWaterBalls() => _waterBalls.Clear();

        /// <summary>
        /// ★攻击形式★ **一次扣水 ⇒ 发射 `层数 × 2` 颗水球**，全部打**距离最近的敌人**。
        /// 没装自充（层数 0）⇒ 什么都不做（**不消耗随机数**，不装插件时的 RNG 流逐位不变 ✓）。
        /// </summary>
        private void FireWaterBalls(PlayerActor p, in WeaponModifiers pm)
        {
            int count = WeaponSystem.WaterBallCount(pm);
            if (count <= 0) return;
            EnemyActor target = World.NearestEnemyTo(p.Pos, 0f);   // 0 = 不限距离
            if (target == null) return;                            // 场上没人 ⇒ 不空放（省特效，也不吃 RNG）

            ToolDef tool = T.tools.can;
            StatBlock st = Progress.Stats;
            // 与 `FireBeam` **同一个**伤害出口与同一个公式（不另写一份乘区）
            float hit = DamageResolver.WeaponDamage(tool.baseValue, st.Get(StatId.RangedDamage),
                                                    st.Get(StatId.AtkMult), false, st.Get(StatId.CritDmg),
                                                    T.formula.minDamage);
            float dmg = WeaponSystem.WaterBallDamage(hit);
            // ★ v197「富豪」：水球这条支线也吃（与其它三条同一个辅助函数）
            dmg = WithGoldDamage(p, dmg);
            FxSpec fx = PluginFx.For(pm, WeaponSlot.Can);

            Vector2 aim = p.AimDir.sqrMagnitude < 0.001f ? Vector2.right : p.AimDir.normalized;
            Vector2 perp = new Vector2(-aim.y, aim.x);              // 瞄准方向的垂线（出膛排开用）
            int room = Mathf.Max(0, WaterBallMaxLive - _waterBalls.Count);
            int n = Mathf.Min(count, room);
            Vector2 toTarget = target.Pos - p.Pos;
            float dist0 = toTarget.magnitude;
            for (int i = 0; i < n; i++)
            {
                Vector2 pos = p.Pos + perp * ((i - (n - 1) * 0.5f) * WaterBallSpawnGap);
                Vector2 to = target.Pos - pos;
                Vector2 dir = to.sqrMagnitude < 1e-5f ? aim : to.normalized;
                float d = Mathf.Max(0.15f, dist0);
                _waterBalls.Add(new WaterBall
                {
                    Pos = pos, Dir = dir, Travel = 0f, Dist = d, T = 0f, Dmg = dmg
                });
                WaterBallSpawnedTotal++;
                // 表现：一颗会飞的水球（`WorldView.OnWaterBall` 负责形状与飞行）
                Bus.Publish(new WaterBallEvent
                {
                    From = pos, Dir = dir, Dist = d, Speed = WaterBallSpeed,
                    Index = i, Count = n, Fx = fx
                });
            }
        }

        /// <summary>
        /// 推进在飞的水球。与剑气同一套子步进：`dt` 切成 ≤ 1/60 的若干步 ⇒
        /// 实时帧与 `DebugAdvanceAttack(0.5)` 的判定序列**逐帧一致**，也不会一步跨过目标（隧穿）✓
        /// </summary>
        private void TickWaterBalls(float dt)
        {
            if (_waterBalls.Count == 0 || dt <= 0f) return;
            float remain = dt;
            while (remain > 1e-5f)
            {
                float step = Mathf.Min(WaterBallMaxStep, remain);
                remain -= step;
                StepWaterBalls(step);
                if (_waterBalls.Count == 0) return;
            }
        }

        /// <summary>推进一个子步：先按线段判定命中，再前进；飞满 `Dist` 或超时即散。</summary>
        private void StepWaterBalls(float step)
        {
            PlayerActor p = World.Player;
            for (int i = _waterBalls.Count - 1; i >= 0; i--)
            {
                WaterBall b = _waterBalls[i];
                float move = Mathf.Min(WaterBallSpeed * step, Mathf.Max(0f, b.Dist - b.Travel));
                Vector2 from = b.Pos;
                Vector2 to = from + b.Dir * move;

                // ③ 命中判定：**线段 vs 圆**（球在这一步扫过的整条线段，不只看终点）
                EnemyActor hit = null;
                for (int k = 0; k < World.Enemies.Count; k++)
                {
                    EnemyActor e = World.Enemies[k];
                    if (e == null || !e.Alive) continue;
                    if (SegCircleHit(from, to, e.Pos, e.Radius + WaterBallRadius)) { hit = e; break; }
                }
                if (hit != null)
                {
                    World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical, RawValue = b.Dmg, Source = p, Target = hit,
                        Knockback = 0f, Crit = false
                    });
                    FxSpec fx = PluginFx.For(ModsFor(WeaponSlot.Can), WeaponSlot.Can);
                    Bus.Publish(new WaterBallHitEvent { Pos = hit.Pos, Damage = b.Dmg, Fx = fx });
                    _waterBalls.RemoveAt(i);
                    continue;
                }

                b.Pos = to;
                b.Travel += move;
                b.T += step;
                bool done = b.Travel >= b.Dist - 1e-4f || b.T >= WaterBallLife;
                if (done) { _waterBalls.RemoveAt(i); continue; }
                _waterBalls[i] = b;
            }
        }

        /// <summary>线段 `a→b` 与圆（圆心 `c`、半径 `r`）是否相交。纯几何，可离线断言。</summary>
        private static bool SegCircleHit(Vector2 a, Vector2 b, Vector2 c, float r)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 <= 1e-9f ? 0f : Mathf.Clamp01(Vector2.Dot(c - a, ab) / len2);
            Vector2 closest = a + ab * t;
            return (c - closest).sqrMagnitude <= r * r;
        }

        /// <summary>"自充"攒到但还没发出去的零头（&lt; 1 点水）。跨节拍保留，不清零 ✓</summary>
        private float _autoChargeAccum;

        /// <summary>"自充"的节拍计时器（秒）。只在 `TickAutoCharge` 里读写 ✓</summary>
        private float _autoChargeTimer;

        /// <summary>
        /// ★攻击形式★ **自充**（`can_autocharge`）：**每 `tools.canAutoChargeInterval` 秒**一次性回
        /// `AutoChargePerSecond × 节拍` 点水量，上限 `WaterAmmoMax` ✓
        ///
        /// ── v192（用户需求①："水壶充能插件，修改为每 0.5 秒恢复一次"）改了什么 ──
        /// 改前是**逐帧累加、攒够 1 点就发** ⇒ 10 点/秒的插件实际上每秒触发 10 次"发 1 点"，
        /// 手感是"水条一直在跳"，`GameAudio` 也被迫把冷却压到 0.32s 去合帧 ✓
        /// 现在**只在节拍边界结算一次**：节拍 0.5s、每秒 10 点时每次发 `10 × 0.5 = 5` 点 ✓
        ///
        /// ── v216（用户原话："水壶自充 ⇒ 每秒恢复 5 点水"）★★ ──
        /// `plugins.json` 的 `can_autocharge.amount` **10 → 5** ⇒ 每秒 **5** 点：
        ///   每拍 `5 × 0.5 = 2.5` 点（`WaterAmmo` 是 int ⇒ 整数部分立刻发、0.5 的零头留到下一拍，
        ///   两拍正好凑满 5 点 ⇒ **与帧率无关、不丢水**）｜单层回满 100 点水要 **20 秒**（改前 10 秒）
        ///   叠满 3 层 = 15 点/秒 ✓
        /// ⇒ 速率**只有一个来源**（表里的 `amount` → `m.AutoChargePerSecond`），本文件不写死任何数 ✓
        ///
        /// ── 三处不能想当然的地方 ──
        /// ① `WaterAmmo` 是 **int** ⇒ 一次发的水必须是整数：`rate × 节拍` 先累进 `_autoChargeAccum`，
        ///    取整数部分发出去、小数部分留到下一拍 ⇒ **与帧率无关、不丢水** ✓
        /// ② 一帧跨多拍要补齐：`beats = floor(timer / interval)` ⇒ 掉帧/卡顿不会少回水 ✓
        /// ③ 节拍 `≤ 0` 按 0.05s 兜底 —— 零周期会让"取整数拍"除零 ✓
        ///
        /// ★ 它回的是**弹药**，不是玩家属性 ★ —— 所以它属于"改攻击方式"（打得下去），不是"改玩家"✓
        /// 判据（`ProbeV192Num` 打读数）：v216 之后 2 秒应回 **4 拍**、合计 **10** 点 ✓
        /// （改前 10/秒时是合计 20 点 —— 这个数跟着表走，别在读数里写死）✓
        /// </summary>
        private void TickAutoCharge(float dt)
        {
            float rate = ModsFor(WeaponSlot.Can).AutoChargePerSecond;
            if (rate <= 0f) { _autoChargeAccum = 0f; _autoChargeTimer = 0f; return; }
            if (WaterAmmo >= WaterAmmoCap) { _autoChargeAccum = 0f; _autoChargeTimer = 0f; return; }   // 满了不留零头（否则一消耗就瞬补）
            float interval = T.EffectiveCanAutoChargeInterval;
            _autoChargeTimer += dt;
            if (_autoChargeTimer < interval) return;                    // 还没到节拍：这一帧一点水都不回 ✓
            int beats = Mathf.FloorToInt(_autoChargeTimer / interval);   // 跨多拍补齐（见 ②）
            _autoChargeTimer -= beats * interval;
            _autoChargeAccum += rate * interval * beats;
            int whole = Mathf.FloorToInt(_autoChargeAccum);
            if (whole <= 0) return;                                     // 这拍的量还不够 1 点 ⇒ 残差留到下一拍
            _autoChargeAccum -= whole;
            WaterAmmo = Mathf.Min(WaterAmmoCap, WaterAmmo + whole);
            // ★ 声音：自充真的回了水才响（`rate <= 0` / 已满 / 未到节拍 都在上面 return ⇒ 不会空响）★
            // 每 0.5 秒回一次 ⇒ 这段现在每秒最多走到 2 次（改前是 5~10 次）
            GameAudio.PlayPlugin(PluginEffectKind.AutoCharge);
        }

        /// <summary>每固定帧衰减"同一目标的再受伤间隔"。</summary>
        private void TickBeamCds(float dt)
        {
            if (_beamHitCd.Count == 0) return;
            // 不能边遍历边改字典
            _beamCdScratch.Clear();
            foreach (var kv in _beamHitCd) if (kv.Value > 0f) _beamCdScratch.Add(kv.Key);
            for (int i = 0; i < _beamCdScratch.Count; i++)
            {
                int id = _beamCdScratch[i];
                float v = _beamHitCd[id] - dt;
                if (v <= 0f) _beamHitCd.Remove(id);
                else _beamHitCd[id] = v;
            }
        }

        /// <summary>
        /// 水壶激光的一次判定 + 表现。**不扣充能**——充能由 TickBeam 按时间扣。
        /// 从原 UseTool 里搬出来，就是为了让"触发"和"扣费"解耦：
        /// 点按模型下它们是同一件事，持续模型下必须分开。
        /// </summary>
        private void FireBeam(PlayerActor p)
        {
            TuningDef tt = T;
            ToolDef tool = tt.tools.can;
            StatBlock st = Progress.Stats;
            WeaponModifiers pm = ModsFor(WeaponSlot.Can);          // ★ v171：插件修正

            float range = tool.range * st.Get(StatId.AtkRange);
            // ★ 阶段 3：**伤害公式的入参里没有一个来自插件** ★
            // v162（A3）：水壶 → **远程伤害**；公式 = (基准伤害 + 远程伤害) × 伤害倍率
            float baseValue = tool.baseValue;
            float damageStat = st.Get(StatId.RangedDamage);
            float atkMult = st.Get(StatId.AtkMult);
            float critDmg = st.Get(StatId.CritDmg);

            // ★ v184：与锄头/肥料**同一个出口**（`WeaponDamage`）＋ 同一个掷骰（`RollCrit`）★
            // 旧版这里自己又抄了一遍 `Clamp01` + `Rng.Chance` + `AttackFormula` ✗
            bool beamCrit = DamageResolver.RollCrit(ref World.Rng, st.Get(StatId.CritRate));
            float beamDmg = DamageResolver.WeaponDamage(baseValue, damageStat, atkMult, beamCrit, critDmg,
                                                        tt.formula.minDamage);
            // ★ v197「富豪」：水壶也吃（与锄头同一个辅助函数，不另写一份加数）
            beamDmg = WithGoldDamage(p, beamDmg);
            // ★ v290：记一笔“**这一次出手的水壶伤害**”—— 臭水沟的香蕉皮（60%）
            //   与海浪（30%）都按它派生，与“灼烧 / 减速 = 这一下的实际伤害 × 比例”同一条惯例 ✓
            PluginExtra2.NoteCanDamage(beamDmg);
            Vector2 origin = p.Pos;
            Vector2 dir = p.AimDir.sqrMagnitude < 0.001f ? Vector2.right : p.AimDir.normalized;
            // ★攻击形式★ **加粗**（`can_thickjet`）：只改宽，**不改长** ✓
            // 长度仍然只由 `range`（射程属性）决定 —— 规格原文"只改宽不改长"
            float baseHalfWidth = tool.beamWidth > 0f ? tool.beamWidth : 1.4f;
            float halfWidth = WeaponSystem.BeamHalfWidth(baseHalfWidth, pm);
            // ★ 声音：加粗（同理，`FireBeam` 每帧都到 ⇒ 靠 0.30s 冷却节流）★
            if (pm.BeamWidthPct > 0f) GameAudio.PlayPlugin(PluginEffectKind.ThickJet);

            // v120：水壶激光加细微散布（需求："玩家水壶的远程攻击"）。
            // 与植物共用同一个系数 —— 玩家与植物走同一条"看起来自然"的表现约定。
            // ★ v171：散布只对**瞄准方向**生效一次，之后再由它派生出多条光束 ✗
            //   每条光束各抖一次的话，"弹道数 +1"会顺带改变随机数消耗序列，
            //   于是"装插件前后的差异"就混进了随机性，无法归因 ✓
            float aimJit = T.formula.aimJitterDeg;
            if (aimJit > 0f)
            {
                float jrad = World.Rng.Range(-aimJit, aimJit) * Mathf.Deg2Rad;
                dir = Rotate(dir, jrad);
            }

            // ★攻击形式★ 激光条数 = 1 + 多柱道数（`can_multijet`）
            int beams = Mathf.Max(1, pm.JetCount);
            // ★ 声音：多柱（≥2 道才响）★ 这一处在 `FireBeam` 里 —— 按住水壶时每固定帧都会走到，
            // 所以真正的节流是 `GameAudio` 里那条 0.30s 冷却（它 ≥ 任务要求的 0.1s 下限）✓
            if (beams > 1) GameAudio.PlayPlugin(PluginEffectKind.MultiJet);
            // ★ 需求①：**扇形分布**且**不重叠** —— 夹角由 `2·asin(半宽/射程)` 算出（17.83°，见函数注释）★
            //   旧版是表里写死的 8°/道 ⇒ 射程末端两条柱子只错开 1.33m，而柱宽 2.8m ⇒ 叠在一起 ✗
            //   半宽用**实际值**（含 `can_thickjet` 加粗）⇒ 加粗后夹角自动变大，仍然不重叠 ✓
            float fanStep = BeamFanStepDeg(halfWidth, range);
            float knock = st.Get(StatId.Knockback) * 0.2f;
            // 攻速既缩短"同一目标的再受伤间隔"，也缩短扣费间隔（TickBeam 里读同一个式子）
            float fireRate = Mathf.Max(0.05f, p.AttackSpeed);

            // ★ v282：一束激光的判定 = 一个吸血批次（多道 × 多个敌人全部累加，扫完结算一次）✓
            //   装的是锄头的吸血插件时 `pm.LifestealPct == 0` ⇒ 整个批次连开都不开，零开销 ✓
            LifestealBegin(p);
            // ★★ v305：**多柱共享同一次结算**（用户实测"多柱没有共享"）★★
            //
            // ── 改前那道循环为什么不够 ──
            //   `for (b 道) { for (每个敌人) { …伤害… } }` —— 去重**只靠** `_beamHitCd` 的副作用：
            //   第 0 道打中 E 时把 CD 写上，第 1 道再遇到 E 时靠 `cd > 0f` 跳过。
            //   于是"共享"不是写出来的语义，而是**别人顺手留下的副作用**：
            //   任何一处把 `_beamHitCd` 清掉 / 换键 / 提前 return，两柱就会各算一次 ✗
            //
            // ── 现在：**先数覆盖、再结算一次** ──
            //   每个敌人只扫一遍，记下"被几道罩住"（`covered`）：
            //     · `covered == 0` ⇒ 没被扫到
            //     · `covered >= 1` ⇒ **只结算一次**（伤害 / 弹字 / 灼烧减速吸血潮汐全部一份）
            //   ⇒ a) 扣水：本来就是"一次喷射一份"（`WeaponSystem.BeamWaterCostPerFire` 不乘道数）✓
            //     b) 每目标受伤冷却：现在由**这一次扫描**保证，不再依赖 `_beamHitCd` ✓
            //     c) 每道各自结算：改掉了 —— 结算收敛到一处，`PluginPostHit` 每敌人每喷射至多一次 ✓
            //   数字：2 层多柱（3 道）对着 1 个敌人喷一次 ⇒ 伤害仍是 1 份（不是 3 份），
            //         `MultiJetSharedSkips += 2`；扣水仍是 20（不是 60）。
            int sharedSkips = 0;
            for (int i = 0; i < World.Enemies.Count; i++)
            {
                EnemyActor e = World.Enemies[i];
                if (e == null || e.IsDead) continue;
                Vector2 rel = e.Pos - origin;

                int covered = 0;
                for (int b = 0; b < beams; b++)
                {
                    Vector2 bdir = beams == 1
                        ? dir
                        : Rotate(dir, fanStep * (b - (beams - 1) * 0.5f) * Mathf.Deg2Rad);
                    float along = Vector2.Dot(rel, bdir);
                    if (along < 0f || along > range) continue;
                    float perp = Mathf.Abs(rel.x * bdir.y - rel.y * bdir.x);
                    if (perp > halfWidth + e.Radius) continue;
                    covered++;
                }
                if (covered == 0) continue;
                if (covered > 1) sharedSkips += covered - 1;   // 被多道同时罩住 ⇒ 只算一次

                // ⑦：**每个受判对象独立 CD** —— 没有这条，光束里的敌人会每帧掉一次血
                if (_beamHitCd.TryGetValue(e.Id, out float cd) && cd > 0f) continue;
                // v100：这个 CD 也吃攻速，否则"攻速提升"对激光的伤害频率没有任何作用
                _beamHitCd[e.Id] = BeamPerTargetCd / fireRate;
                World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical, RawValue = beamDmg, Source = p, Target = e,
                    Knockback = knock,
                    Crit = beamCrit
                });
                PluginPostHit(e, beamDmg, pm, p);      // ★ 灼烧 / 减速 / 吸血
            }
            MultiJetSharedSkips += sharedSkips;

            // 一条完整射线（不是逐敌发）——v44 的结论，别退回去
            // v176：激光外观吃水壶这一路的插件（颜色 / 粗细 / 拖尾 / 火花 / 发光）
            // ★ v200：`ThickenPct` = **加粗比例本身**，让表现层能算出"比原来粗多少" ★
            //   不能只靠 `HalfWidth` 推：表现层那条 `Clamp(…, 0.07, 0.30)` 在**没装加粗时就已经到顶**，
            //   于是"判定 1.4 → 2.8"换算出来的可视宽度两次都是 0.30 ⇒ 画面看不出任何变化 ✗
            //   （判据：`ProbeV200` 同时打 `HalfWidth` 与 `visualW` 两个读数，改前 0.30 → 0.30，
            //     改后 0.30 → 0.60）
            for (int b = 0; b < beams; b++)
            {
                Vector2 bdir = beams == 1
                    ? dir
                    : Rotate(dir, fanStep * (b - (beams - 1) * 0.5f) * Mathf.Deg2Rad);
                Bus.Publish(new BeamEvent
                {
                    From = origin, To = origin + bdir * range, HalfWidth = halfWidth,
                    ThickenPct = Mathf.Max(0f, pm.BeamWidthPct),
                    Fx = PluginFx.For(pm, WeaponSlot.Can),
                    // ★ v307：带上"这是第几道" ⇒ 表现层的节流器才能每道各算一份。
                    //   不带的话 N 个事件在同一帧共用同一个计时变量 ⇒ 只有第 0 道有特效 ✗
                    Slot = b,
                    // ★ v307：总道数 ⇒ 表现层按 `SplashInterval × SlotCount` 逐道分摊，
                    //   于是"每道都有水花"与"总量不变（不冲空 Bit 池）"同时成立 ✓
                    SlotCount = beams
                });
            }
            // ★ v282：全部道 × 全部敌人扫完 ⇒ 一次吸血结算（浇水那一段与吸血无关，放在它前面）✓
            LifestealSettle();
            // ★ v290：**海浪** —— 喷水那一下脚底起一道浪（+50% 移速 1 秒，扫到的敌人吃 30%）。
            //   放在射线与水补之后：它不改任何射线判定，只是“这一次出手顺带了什么”✓
            PluginExtra2.OnCanAttack(p, pm);

            // 激光同时给射线上的土地补水。
            // ★ 只用**主光束**（`dir`）补水 ✗ —— 否则"暴雨阀"会顺带把浇水效率也翻倍，
            //   那是另一条线上的隐性改动，玩家与策划都看不见它 ✓
            for (int i = 0; i < World.Farm.Slots.Length; i++)
            {
                FarmPlot fp = World.Farm.Slots[i];
                if (fp == null || fp.State == PlotState.Locked) continue;
                Vector2 rel = fp.World - origin;
                float along = Vector2.Dot(rel, dir);
                if (along < 0f || along > range) continue;
                float perp = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                if (perp > 1.4f) continue;
                InteractPlotByAttack(fp, ToolCan, range);
            }

            // ★★ v197「锋利水枪」（规则 `sharp_water`）：**水壶的远程攻击也能收割成熟作物** ★★
            //   用户原话："水壶获得收割植物的效果"
            //   判据与上面那条"补水"完全相同（同 origin / 同 dir / 同 range / 同 1.4m 宽度）
            //   ⇒ "水柱扫到的地方"与"水柱收割的地方"是**同一个形状**，不会出现"看着扫到了却没割" ✓
            //   ★ 为什么只收割**已成熟**的格 ★ `HarvestPlot` 自身就有 `!plot.IsRipe` 守卫，
            //     这里不重复判 —— 一道守卫两处写就会出现"改了一处另一处还在拦" ✓
            // ★ 本轮：「锋利」（`can_sharp`）插件已按用户要求删除 ⇒ 水壶能不能收割
            //   重新只由**项目规则** `sharp_water`（「锋利水枪」）决定，产量恒为**全额**（`HarvestPlot(plot)`）✓
            bool canHarvestRipe = ProgMechanics.ItemPanel.Of(this).SharpWaterStacks > 0;
            if (canHarvestRipe)
            {
                for (int i = 0; i < World.Farm.Slots.Length; i++)
                {
                    FarmPlot fp = World.Farm.Slots[i];
                    if (fp == null || !fp.IsRipe) continue;
                    Vector2 rel = fp.World - origin;
                    float along = Vector2.Dot(rel, dir);
                    if (along < 0f || along > range) continue;
                    float perp = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                    if (perp > 1.4f) continue;
                    HarvestPlot(fp);
                }
            }
        }

        private void UseTool(PlayerActor p, ToolDef tool, bool ailment)
        {
            int slot = ailment ? PlayerActor.WeaponFertilizer : PlayerActor.WeaponCan;
            WeaponModifiers pm = ModsFor(ailment ? WeaponSlot.Fertilizer : WeaponSlot.Can);
            // ★ 阶段 3：攻速**不再吃插件**（插件不改玩家属性）
            float fireRate = Mathf.Max(0.05f, p.AttackSpeed);

            if (!ailment)
            {
                // ════════════════════════════════════════════════════════════════════
                // ★ v171：**这一段原来是死代码** ★
                //
                // 全工程只有一处调用 `UseTool`，传的是 `ailment: true`（肥料）✗
                // 所以这段"水壶点按激光"从来**没有执行过** ——
                // 水壶真正的路是 `TickBeam → FireBeam`（v90 把它改成了持续激光）✓
                //
                // 处理方式不是"把它也接上插件"，而是**收敛到唯一的激光实现**：
                // 两份激光判定并存正是"改一处漏一处"的温床 ✗
                // v44（一条完整射线）、v100（CD 吃攻速）、v120（瞄准散布）、v171（多发）四条结论
                // 只写在 `FireBeam` 里 —— 再留一份副本，就是给下一个人埋一个"改了没生效"✓
                // ════════════════════════════════════════════════════════════════════
                if (WaterAmmo < 10) { SetPrompt("水壶没水了（拾取雨滴 +20）"); return; }
                WaterAmmo -= 10;
                p.WeaponCds[slot] = tool.cooldown / fireRate;
                FireBeam(p);
                return;
            }

            // v23 弹药：肥料每次投掷 −1（上限 10）
            if (FertAmmo <= 0) { SetPrompt("没有肥料了（击败小猪获取）"); return; }

            // ★攻击形式★ **免耗**（`fert_freeburst`）：12%/层 概率**这一投不扣充能** ✓
            // ★ 掷骰只在装了这件插件时才发生 ★ —— `Chance` 会消耗一个随机数，
            //   无条件掷会改变所有局面（包括没装插件的）的 RNG 流 ⇒ 那就是跨版本不可比 ✓
            bool freeBurst = pm.FreeBurstChance > 0f
                             && World.Rng.Chance(WeaponSystem.FreeBurstChanceOf(pm));
            if (!freeBurst) FertAmmo--;
            else SetPrompt($"免耗：这一投不扣肥料（{FertAmmo}/{FertAmmoMax}）");
            // ★ 声音：免耗（**只在这一投真的免了才响** —— 响在 RNG 之后，不碰随机数流）★
            if (freeBurst) GameAudio.PlayPlugin(PluginEffectKind.FreeBurst);

            p.WeaponCds[slot] = tool.cooldown / fireRate;

            // ── v40：肥料 = 在鼠标对应位置产生一次爆炸（不再飞行）──
            // v46：落点**必须被投掷射程钳制**——射程 = tools.fertilizer.range × 攻击范围属性。
            //      旧写法把"钳制前的向量"缓存下来又复用，判定顺序含混；现在收敛到 CurrentBlastCenter 一处。
            // v91：不再立即爆炸，改为**先亮 0.5 秒区域标记再炸**（需求"增加爆炸前摇"）。
            // 阶段 3：**连环**（爆炸次数 +1/层）在 `ArmBlast` 里展开 —— 它同时决定前摇标记的个数，
            //       所以"标记"与"真炸"必然是同一组落点 ✓
            ArmBlast(CurrentBlastCenter(), p);
        }

        // ──────────────────────────────────────────── v91：肥料爆炸前摇
        private const float BlastWindup = 0.5f;

        /// <summary>
        /// ★ v310：**这一次投掷的引信长度**（秒）—— 唯一口径，两个消费点共用：
        ///   · `ArmBlast`（真投掷，排 `_pendingBlasts` + 投 `FertilizerTelegraphEvent.Duration`）
        ///   · `DebugAttackOnce`（调试一次攻击，要把队列推完才看得到真炸）
        /// 规则一条：**装了黑洞 ⇒ 1.0 秒前摇 + 一帧余量（1.02s）**；没装 ⇒ `BlastWindup`(0.5s) 逐位不变。
        /// 为什么黑洞必须更长：`TickPendingBlasts` 用"`b.T` 跨过 `BlackHoleWindup`"来开漩涡，
        /// 引信比前摇短的话那条判据永远不成立（详见 `PluginExtra2.BlackHoleFuseSeconds`）✓
        /// </summary>
        private float BlastFuseFor(in WeaponModifiers pm)
            => pm.BlackHole ? PluginExtra2.BlackHoleFuseSeconds : BlastWindup;

        /// <summary>★ v311：一颗"0.5 秒后炸"的炮弹。
        /// `Spot` / `Spots` 是**这一次投掷的落点身份**（连环）—— 黑洞前摇要靠它们做到
        /// "每个落点各开一套漩涡、各占一格吸附场地"（详见 `TickPendingBlasts` 与
        /// `PluginExtra2.BlackHoleTelegraph`）✓</summary>
        private struct PendingBlast
        {
            public Vector2 Center;
            public float T;
            public int SourceId;
            public int Spot;      // 这是第几个落点（0 = 主落点）
            public int Spots;     // 这一次投掷一共几个落点（1/2/3）
        }
        private readonly System.Collections.Generic.List<PendingBlast> _pendingBlasts =
            new System.Collections.Generic.List<PendingBlast>();

        /// <summary>
        /// v181：被"跨回合清理"丢掉、而**充能已经付过**的炮弹数 —— 在 `StartWave` 里退回给玩家。
        /// 只在 `ClearTransientCombatState` 写、在 `StartWave` 读一次就清 0 ⇒ 不会跨回合残留 ✓
        /// </summary>
        private int _fertRefund;

        /// <summary>v111：爆炸基础半径的缺省值（米）。数据里没填时用它。</summary>
        public const float DefaultBaseBlastRadius = 3f;

        /// <summary>
        /// v111：整数基础半径 → 实际基础半径。≤0 一律按缺省处理。
        /// **为什么不在解析时就填默认值**：那样"没填"和"填了 3"就分不出来，
        /// 以后想改缺省值得回头动数据。缺省放在读取处，语义更清楚。
        /// </summary>
        public static float BlastBaseRadius(int baseMeters)
            => baseMeters > 0 ? baseMeters : DefaultBaseBlastRadius;

        /// <summary>
        /// 爆炸半径 = 基础半径 × 爆炸范围属性。★ 阶段 3 起**不吃插件**（没有半径类插件了）★
        /// 前摇标记与真正的爆炸必须用**同一个值**，否则标记是骗人的 ✗ ⇒ 仍然收敛在这个函数 ✓
        ///
        /// ★ v197：**结果钳 0** ★ —— `StatBlock.FloorOf` 已放开 `ExplodeRange` 的下限
        ///   （用户："其它的数值可以为负数，并造成负面效果"）⇒ 负倍数会让半径变负。
        ///   负半径的玩法含义就是"炸不到任何人"（负面效果本身），但负数一旦流到表现层
        ///   （前摇圈 / 爆炸环的尺寸）就会被画成翻转的圈 ⇒ **在唯一的生产点钳 0**，
        ///   前摇与真炸仍然同源、同值 ✓
        /// </summary>
        private float BlastRadiusNow() => Mathf.Max(0f,
            BlastBaseRadius(T.tools.fertilizer.baseExplodeRange) * Progress.Stats.Get(StatId.ExplodeRange));

        /// <summary>
        /// ★攻击形式拓展★ 第 <paramref name="i"/> 个爆炸点的落点（**连环** `fert_chain`）。
        /// i=0 就是主落点；其余分在距主落点 <paramref name="gap"/> 米（规格 2.5m）的环上 ✓
        ///
        /// ★ 为什么第 1 个点落在 **180°**（主落点外侧）★
        /// 规格原话是"第二爆点落**主爆点外** 2.5m" —— "外"是相对**投掷方向**说的：
        /// 主爆点已经在玩家与目标之间，再往外推 2.5m 才是"覆盖更远的一段"✓
        /// 所以这里按"玩家 → 主爆点"的方向再推 gap（不再绕圆心均匀分角）✓
        /// 抽成静态函数是为了让"第二个爆点真的落在不同位置"这条可以被自检直接断言 ✓
        /// </summary>
        public static Vector2 BlastSpotCenter(Vector2 center, int i, int spots, float gap, Vector2 outward)
        {
            if (i <= 0 || spots <= 1) return center;
            Vector2 dir = outward.sqrMagnitude < 0.0001f ? Vector2.right : outward.normalized;
            // 第 k 个额外爆点：沿 outward 推 k 段（k=1 就是规格里的"外 2.5m"）
            return center + dir * (Mathf.Max(0.1f, gap) * i);
        }

        /// <summary>
        /// 布置一次延迟爆炸：亮出区域标记，0.5 秒后引爆。
        /// ★ 阶段 3：**连环**（爆炸次数 +1/层）在这里展开 ★
        /// 一次投掷布置 `BlastCount = 1 + 连环层数` 个落点 ✓
        /// 标记与真炸走同一个 `spots` 循环 ⇒ 不可能出现"标记 1 个、真炸 2 个"✓
        /// ★ 每个落点都走一遍完整的 `ExplodeAt` ⇒ 半径与伤害**与主爆完全相同**
        ///   （规格："半径同主爆，不是子爆的 0.7×"）✓
        /// </summary>
        private void ArmBlast(Vector2 center, PlayerActor source)
        {
            WeaponModifiers pm = ModsFor(WeaponSlot.Fertilizer);
            int spots = pm.BlastCount;
            // ★ 声音：连环（≥2 个落点才响；标记与真炸是同一组落点 ⇒ 这里响一次就够）★
            if (spots > 1) GameAudio.PlayPlugin(PluginEffectKind.ChainBurst);
            // 前摇标记与真炸共用**同一份**插件外观 ⇒ 不可能出现"标记一个色、真炸另一个色"
            FxSpec fx = PluginFx.For(pm, WeaponSlot.Fertilizer);

            Vector2 outward = center - (source != null ? source.Pos : center);
            // ★★ v255：**伏雷** —— 装了它就不排"到点就炸"，改成埋一颗等敌人来踩 ★★
            //   用户定稿：踩中 150%、没人踩则 6 秒自爆 80%
            //   ⚠️ 这里**只排雷、不排 `_pendingBlasts`**：否则 0.5 秒后照旧自动爆，
            //      伏雷就退化成"延迟 0.5 秒的普通肥"（玩家以为埋了，其实没埋）✗
            if (pm.MineHitPct > 0f)
            {
                // ★★ v305 修 bug：**一次投掷会放 `spots` 颗雷，而上限原来恒为 3** ★★
                //   用户报「放新的会直接导致旧的丢失」的根因就在这里：
                //   装了连环（`fert_chain`，`spots = 1 + 层数`，最多 3）时，一次投掷的第 2、3 颗
                //   就会把 `Mines` 顶到 `MineCap = 3` ⇒ `QueueMine` 的兜底"顶掉最早一颗"
                //   挤掉的**正是上一批埋下的旧雷** ⇒ 一次投掷把整个雷区换掉 ✗
                //   修法：把 `spots` 传给 `QueueMine`，容量由 `PluginField.MineCapFor(spots)` 出
                //   = `max(3, 2×spots)` ⇒ 没连环仍是 3（逐位不变）、连环 2 层（3 落点）是 6 颗 ✓
                for (int i = 0; i < spots; i++)
                {
                    Vector2 c = BlastSpotCenter(center, i, spots, pm.ChainBurstGap, outward);
                    // ★★ v311：**黑洞也要传给雷**（用户：「黑洞…搭配伏雷会导致前摇消失」）★★
                    //   雷不走 `_pendingBlasts` ⇒ 黑洞的前摇只能由**雷自己**承担，见
                    //   `PluginField.TickMines` 里那两处 `BlackHoleMineWindup` / `BlackHoleMineBurst` 的调用。
                    //   这里把 `pm.BlackHole` 记进 `Mine` 结构：**埋的时候装没装黑洞**是那次投掷的事实，
                    //   不该在 6 秒后炸的那一刻再去读一次玩家手上的插件表（那时他可能已经换掉了）
                    PluginField.QueueMine(c, BlastRadiusNow(),
                                          pm.MineArmTime, pm.MineSenseRadius,
                                          pm.MineHitPct, pm.MineMissPct, spots,
                                          pm.BlackHole);
                    // 埋雷也要有"这里放了东西"的提示：复用前摇光圈，但给更长的时长与更暗的样子
                    Bus.Publish(new FertilizerTelegraphEvent
                    {
                        Pos = c, Radius = pm.MineSenseRadius, Duration = pm.MineArmTime + 0.35f, Fx = fx
                    });
                }
                // 埋完就走：不走下面那条"到点炸"的路。
                // ★ v311：**黑洞前摇不跟着这条 return 一起消失** —— 它挂在雷的引信上
                //   （`PluginField.TickMines`：`FuseLeft` 跨过 `BlackHoleWindup` 那一帧开漩涡；
                //    被踩爆则当场闪一次），所以"伏雷 + 黑洞"照样有前摇 ✓
                return;
            }

            for (int i = 0; i < spots; i++)
            {
                Vector2 c = BlastSpotCenter(center, i, spots, pm.ChainBurstGap, outward);
                // ★★ v310：**引信长度按"装没装黑洞"分档** ★★
                //   黑洞的 1 秒前摇**必须**有 1 秒的引信承载：`TickPendingBlasts` 是在
                //   `b.T` 跨过 `BlackHoleWindup` 的那一帧开漩涡（判据 `b.T > Windup && b.T−dt ≤ Windup`），
                //   若引信仍是 0.5s < 前摇 1.0s，那条判据**一次都不会成立** ⇒ 装了黑洞反而什么都不发生 ✗
                //   ⇒ 装黑洞：引信 = `BlackHoleWindup + 0.02`（= 1.02s，爆炸落在投掷后 1.02s）；
                //      没装：**逐位不变**，仍是 `BlastWindup`(0.5s) ✓
                //   ⚠️ 也就是说：**黑洞这一炮的伤害结算比改前晚 0.5 秒**（不是"1 秒前摇 + 原位立刻爆"）。
                //      这是"1 秒前摇"这条需求在物理上唯一自洽的读法 —— 前摇就是引信的最后一段，
                //      引信不延长，前摇就不存在。结算的**位置/半径/数值**一个都没变 ✓
                float fuse = pm.BlackHole ? PluginExtra2.BlackHoleFuseSeconds : BlastWindup;
                // ★ v311：落点身份一路带到 `TickPendingBlasts` —— 黑洞前摇靠它做到
                //   "每个落点各开一套漩涡、各占一格吸附场地"（用户报的是"只有末端有黑洞"）
                _pendingBlasts.Add(new PendingBlast
                {
                    Center = c, T = fuse, SourceId = source.Id, Spot = i, Spots = spots
                });
                Bus.Publish(new FertilizerTelegraphEvent
                {
                    Pos = c, Radius = BlastRadiusNow(), Duration = fuse, Fx = fx
                });
            }
        }

        /// <summary>每固定帧推进延迟爆炸。放在逻辑层，保证确定性重放一致。</summary>
        private void TickPendingBlasts(float dt)
        {
            // ★★ v311 修 bug：**倒序遍历 + 按帧去重 = "只有末端能触发黑洞"** ★★
            //
            // ── 用户报的原话 ──
            //   「**黑洞搭配连环会只有末端可以触发黑洞**」
            //
            // ── 两个成因（都在本方法与 `AttackVfx.SpawnCollapseRing` 里）──
            //   ① **倒序遍历**：同一帧里第一个被处理的是**下标最大**的那颗 = 最后端那一炮；
            //   ② **按帧去重**（`AttackVfx._lastCollapseFrame`）：它画完一套漩涡，同帧其余两颗
            //      （**含主落点 i=0**）全被 `return` 掉 ⇒ 玩家看到的只有末端那一个 ✗
            //   更糟的是"吸"和"看"分家了：`BlackHoleTelegraph` 三次都会写场地 ⇒ 最后写入的是
            //   **主落点**，于是"漩涡在末端、吸附在主落点" ⇒ 两头都对不上 ✓
            //
            // ── 修法（选"**每个落点各画各的**"，不选"主落点独占"）──
            //   · 主落点独占的话，连环另外两炮照样炸、照样有伤害，却一个黑洞都不显示 ⇒
            //     玩家仍会觉得"我这里也有黑洞，凭什么不生效"（同一个抱怨换个说法）✗
            //   · 每落点一套的代价只是**格数**，而格数是可控的：`AttackVfx.SpawnCollapseRing`
            //     按 `spots` 分档（3 套 = 57 ≤ 64），池账写在那个方法的注释里 ✓
            //   · 去重职责交回**这条跨越式判定**：它对每颗 `PendingBlast` **恰好成立一次**
            //     ⇒ 天然就是"按落点去重"，不再需要任何 `frameCount` 标记 ✓
            //   · 倒序遍历本身**保留**：`RemoveAt(i)` 只有倒着走才安全（正着走会跳元素）。
            //     现在每个落点都画自己的，遍历方向不再影响"谁能看到黑洞" ✓
            for (int i = _pendingBlasts.Count - 1; i >= 0; i--)
            {
                PendingBlast b = _pendingBlasts[i];
                // ★ v290：**黑洞的吸引阶段** —— 前摇剩下 `BlackHoleWindup`(1.0s) 那一帧开漩涡
                //   （`AttackVfx.SpawnCollapseRing`）。
                //   判据写成"跨过 1.0 这一点"而不是"每帧都画"⇒ 每一爆**恰好一次**，不需要额外字段 ✓
                // ★ v311：`b.Spot` / `b.Spots` 一起传 ⇒ 每个落点一套漩涡 + 自己那一格吸附场地 ✓
                if (b.T > PluginExtra2.BlackHoleWindup
                    && b.T - dt <= PluginExtra2.BlackHoleWindup)
                {
                    PluginExtra2.BlackHoleTelegraph(b.Center, BlastRadiusNow(),
                                                    ModsFor(WeaponSlot.Fertilizer),
                                                    b.Spot, b.Spots);
                }
                b.T -= dt;
                if (b.T > 0f) { _pendingBlasts[i] = b; continue; }
                _pendingBlasts.RemoveAt(i);
                // 来源恒为玩家（肥料只有玩家能扔）；这里不查 SourceId，
                // 因为前摇期间跨回合时玩家对象会被重建，按 id 找反而可能找不到。
                ExplodeAt(b.Center, World.Player);
                // ★★ v204：**这里原来有一句 `ReturnToHoeAfterBlastHarvest(World.Player)`，已删除** ★★
                //   那是 v202/v203 为了修"炸完之后收成永久变成 1/4"而加的**自动切回锄头**，
                //   用户 v204 报的原话是"出现武器自动切换到锄头的 bug" ⇒ 那正是这条行为 ✓
                //   ⇒ 整套撤掉：字段 `LastBlastHarvested`、方法 `ReturnToHoeAfterBlastHarvest`、
                //     本行调用、`SelfCheck` 的 `v202④` 两条断言，一处不留 ✓
                //   ★ 但**没有**连带撤掉的是真正的成因修复 ★：
                //     · `v202①/②/③` 三条断言还在（手挥全额 / 炸收 = 1/4 / 炸后手挥仍是全额）
                //     · 也就是说"武器状态"从来不是收益的来源 —— 收益只由**这一次出手走哪条路径**决定；
                //       而玩家想换回锄头是他自己的事（滚轮 / 数字键），系统不再代劳 ✓
            }
        }

        /// <summary>
        /// v46：当前肥料的实际落点 = 鼠标世界坐标 → 射程钳制 → 场地钳制。
        /// 玩家操作与表现层落点准星读的是同一个函数，保证"看到的就是炸到的"。
        /// </summary>
        public Vector2 CurrentBlastCenter()
        {
            if (World == null || World.Player == null) return Vector2.zero;
            PlayerActor p = World.Player;
            float reach = FertilizerThrowReach();
            Vector2 center = BlastCenterFor(p.Pos, AimWorldLast, p.AimDir, reach);
            // v46：射程内也可能指到墙外——落点同样不能穿墙
            return World.ClampToArena(center, 0.5f, T.farm.arenaHalf);
        }

        /// <summary>
        /// 肥料投掷射程 = `tools.fertilizer.range` × 攻击范围属性。
        /// ★ 阶段 3 起**不吃插件**（没有范围类插件了）★
        /// 抽成一个函数是因为落点、准星变色、射程判定**三处必须一致** ✗
        /// 三处各写一遍的话，"准星说超程、实际却打得到"这种偏差必然出现 ✓
        /// </summary>
        private float FertilizerThrowReach()
            => T.tools.fertilizer.range * Progress.Stats.Get(StatId.AtkRange);

        /// <summary>鼠标是否已超出肥料投掷射程（表现层用它给落点准星变色）。</summary>
        public bool IsBlastOutOfRange
        {
            get
            {
                if (World == null || World.Player == null) return false;
                return Vector2.Distance(World.Player.Pos, AimWorldLast) > FertilizerThrowReach();
            }
        }

        /// <summary>
        /// v46：肥料爆炸落点 = 鼠标世界坐标，但被**投掷射程**钳制在玩家周围的一段距离内。
        /// · 鼠标在射程内 → 落点就是鼠标位置
        /// · 鼠标超出射程 → 落点落在"玩家 → 鼠标"方向上的射程边界
        /// · 鼠标正好压在玩家身上 → 沿瞄准方向抛出射程距离（避免 0 向量）
        /// 抽成静态函数是为了让"落点必须在射程内"这条规则可以被自检直接断言。
        /// </summary>
        public static Vector2 BlastCenterFor(Vector2 playerPos, Vector2 aimWorld, Vector2 aimDir, float throwRange)
        {
            float range = Mathf.Max(0.1f, throwRange);
            Vector2 toAim = aimWorld - playerPos;
            if (toAim.sqrMagnitude > range * range)
                return playerPos + toAim.normalized * range;
            if (toAim.sqrMagnitude < 0.0001f)
            {
                Vector2 dir = aimDir.sqrMagnitude < 0.0001f ? Vector2.right : aimDir.normalized;
                return playerPos + dir * range;
            }
            return aimWorld;
        }

        /// <summary>
        /// v42：肥料爆炸的完整结算（可被测试直接调用）——
        /// ① 对半径内敌人造成（爆炸伤害 + 攻击伤害）× 攻击倍率 × 肥料品质倍率；
        /// ② 广播事件（表现层画爆炸）；③ 触发半径内土地催熟。
        /// 返回被爆炸命中的敌人数。
        ///
        /// ★ 阶段 3：插件在这一处接的是"形状"★
        ///   · **子爆**（`fert_subburst`）：主爆结算完之后，在主爆旁的六边形角上补几下小爆 ✓
        ///     v189：落点从"主爆半径 ×0.9"改成 **×1.75**（用户需求⑤："间距增加"）——
        ///     旧值让子爆整个压在主爆里面（两圆几乎重合），玩家读不出"还有一个子爆" ✓
        ///   · **毒素**（`fert_poison`）：爆点留下一块毒区（交给 `PluginDotTable` 每秒结算）✓
        ///     v189：**子爆也铺毒**，半径 = 主毒区 × 子爆半径比（小一号）⇒ 块数 = 主 1 + 子 N ✓
        ///   · **连环**（`fert_chain`）**不在这里**：它在 `ArmBlast` 展开成两次本函数的调用，
        ///     所以"半径同主爆"是**自动成立**的（同一个函数、同一个半径式子）✓
        /// ★ 伤害公式的入参里没有一个来自插件 ★（阶段 3：插件不改玩家属性）
        /// </summary>
        /// <summary>v255：`PluginField.Tick` 的回调 —— 埋雷踩中/超时都走这里。
        /// ★ v308：**改成有返回值** —— 返回"这一次爆炸真实造成的伤害"（`LastBlastDealt`），
        /// 让「伏雷」弹字能印出实际数值（`PluginField.TickMines` 里"先炸再发字"那一段）✓
        /// 为什么用 `LastBlastDealt` 这个字段而不是给 `ExplodeAt` 加返回值：
        ///   `ExplodeAt` 返回的是**命中人数**（`int`），那是自检 / 探针 / `DebugApi` 都在用的既有契约，
        ///   动它就是动一片调用点；而"总伤害"是**这一次调用**的临时读数 ⇒ 用专用字段最省 ✓</summary>
        private float ExplodeWithMult(Vector2 center, PlayerActor source, float mult)
        {
            ExplodeAt(center, source, mult);
            return LastBlastDealt;
        }

        /// <summary>
        /// ★ v308：**最近一次 `ExplodeAt` 真实造成的总伤害**（= 主爆 + 全部子爆里，
        /// 每个被罩到的敌人的 `DamageResult.Dealt` 之和）。
        ///
        /// 口径（与用户要求逐字对齐）：**真正扣掉的血/盾量**，不是"算出来的量" —
        ///   · 残血敌人只按它剩下的血算（`Dealt = min(剩余, RawValue)`）✓
        ///   · 被闪避 / BOSS 入场免伤 ⇒ 那一下 `Dealt = 0` ⇒ 不计入 ✓
        /// ⇒ 一颗雷炸空（圈里没人）时它 **= 0** ⇒ 调用方据此不发弹字
        ///   （"弹一条 0 是噪音"，用户原话）✓
        /// ⚠️ 它是"最近一次"的临时读数，**只在 `ExplodeAt` 返回之后有意义**；
        ///   本工程里没有任何地方在两次 `ExplodeAt` 之间读它（唯一读者是 `ExplodeWithMult`，
        ///   它拿到值的下一行就交回 `PluginField`）✓
        /// </summary>
        public float LastBlastDealt { get; private set; }

        public int ExplodeAt(Vector2 center, PlayerActor source) => ExplodeAt(center, source, 1f);

        /// <summary>
        /// ★ v255：带**伤害倍率**的重载（伏雷踩中 1.5 / 超时 0.8 走这里）。
        /// 为什么用倍率而不是"算好的伤害"：爆炸伤害的算式只该有一份（上面那条链），
        /// 传一个乘数进来 ⇒ 雷炸出来的数与主爆**永远同源** ✓
        /// </summary>
        public int ExplodeAt(Vector2 center, PlayerActor source, float dmgMult)
        {
            // ★ v308：本次爆炸的"真实伤害"累计清零（唯一写入点是下面的 `BlastDamageAt`）
            //   主爆与每一发子爆都累加到同一个数上 ⇒ 一次投掷 / 一颗雷 = 一个总数 ✓
            LastBlastDealt = 0f;
            StatBlock st = Progress.Stats;
            WeaponModifiers pm = ModsFor(WeaponSlot.Fertilizer);
            // v109：爆炸伤害 = **肥料这件武器走一遍普通攻击公式** 的结果 × 爆炸倍率。
            // 顺序不能反：先算普通，再乘爆炸。这样"堆伤害属性/伤害倍率"对爆炸同样生效，
            // 两套成长自动同步——旧版那条独立公式只吃攻击伤害，基础伤害都被忽略。
            // v162（A3）：肥料 → **爆炸伤害**；公式 = (基准伤害 + 爆炸伤害) × 伤害倍率
            ToolDef fertTool = T.tools.fertilizer;
            // ★ v184：同锄头，**去掉 `× ToolMult(品质)` 那一层**（需求①）★
            float baseValue = fertTool.baseValue;
            float damageStat = st.Get(StatId.BlastDamage);
            float atkMult = st.Get(StatId.AtkMult);
            // ★ v184：肥料**也掷暴击**，且暴击乘在整条算完的伤害上（需求②）★
            //
            // 旧写法是 `AttackFormula(..., crit: false, critDmg: 1f, ...)` —— 写死"不暴击" ✗
            // 而武器卡（`ImGuiHud` 的伤害行）是按"普通 / 暴击 ×爆炸倍率"两个数在显示的，
            // 于是**卡面承诺了引擎不给的东西**：玩家看到"暴击 41"，实战永远只有 21 ✓
            // 现在两条线一致：先 `(基础 + 爆炸伤害) × 伤害倍率`，暴击则再 × 暴击伤害，
            // 最后才乘爆炸倍率 —— 与卡面顺序逐项相同 ✓
            bool blastCrit = DamageResolver.RollCrit(ref World.Rng, st.Get(StatId.CritRate));
            // ★ v195：**爆炸倍率整条删除** ⇒ 肥料与另外两把武器同一个式子（用户原话见 `StatId` 的说明）★
            float explodeDmg = DamageResolver.WeaponBlastDamage(baseValue, damageStat, atkMult,
                                                               blastCrit, st.Get(StatId.CritDmg), 0f);
            // ★ v197「富豪」：肥料也吃（四条武器路径逐条接）
            explodeDmg = WithGoldDamage(source, explodeDmg);
            // ★ v255：伏雷的倍率乘在**整条算完之后**（与暴击、富豪同一个次序）
            if (dmgMult != 1f) explodeDmg *= Mathf.Max(0f, dmgMult);
            // v111：爆炸基础半径改为读**这件武器自己的**整数值（需求："每个武器有自己的基础爆炸范围基础值"）。
            // 缺省 3 米，所以老数据不改也能跑；要调某件武器的爆炸体量只需填 baseExplodeRange。
            // ★ v197：结果钳 0 —— 与 `BlastRadiusNow()` 同一条兜底（`ExplodeRange` 的下限已放开）★
            float blastRadius = Mathf.Max(0f, BlastBaseRadius(fertTool.baseExplodeRange) * st.Get(StatId.ExplodeRange));
            float knock = st.Get(StatId.Knockback);
            // ★★ v290：**黑洞** —— 爆炸结算**之前**把范围内的敌人拉到爆心 ★★
            //   放在 `BlastDamageAt` 之前（且**不改它的署名**）：拉拢是“这一爆之前发生的一次位移”，
            //   而伤害判定仍然只有一份（那个函数里的 `distance ≤ radius + e.Radius`）✓
            //   没装黑洞时它第一行就返回 ⇒ 零副作用、零随机数 ✓
            PluginExtra2.BlackHolePull(center, blastRadius, pm);
            int hit = BlastDamageAt(center, blastRadius, explodeDmg, knock, source, pm, true, blastCrit);

            // ★★ v255：**护壤**（用户定稿：2 点、持续 3 秒）★★
            //   判据 = 玩家**自己在爆炸半径内**（身体半径也算，贴边站不该被算成在外面）
            if (pm.WardShield > 0f && source != null)
            {
                PlayerActor who = World.Player;
                if (who != null &&
                    (who.Pos - center).sqrMagnitude <= (blastRadius + who.Radius) * (blastRadius + who.Radius))
                    PluginField.GrantWard(who, pm.WardShield, pm.WardSeconds);
            }
            // ★★ v286：**灼印的落下时机已挪走** ★★
            //   改前它在"主爆结算之后、子爆之前" ⇒ 同一次投掷的**子爆**立刻就能吃到自己这一炮刚挂上的印
            //   （子爆量 = 主爆 × 0.5 ⇒ 等于用最弱的一下把印用掉）。
            //   现在它落在**整次投掷结算完之后**（子爆与毒区都登记完），代码在下面
            //   「★★ v255 / v286：灼印 —— 整次投掷结算完之后才落印 ★★」那一段，
            //   规矩一句话：**这一投掷吃不到自己挂的印，只吃上一投掷留下的** ✓

            // ◆攻击形式◆ **子爆**（`fert_subburst`）：额外 1 个子爆/层 ✓
            // 落点：正六边形角分布（0°/60°/…/300°）、距主爆心 = 主爆半径 × 0.9 ✓
            // 半径：= 主爆 × 0.70 ✓ 伤害：同比例（规格只给了半径，取同一个比例，见 `WeaponSystem.SubBurstDamage`）
            // ★ 子爆**不再触发** `PluginPostHit` 之外的任何机制 ★：它走的是同一个 `BlastDamageAt`，
            //   机制照常（灼烧/减速），但**不会**再派生子爆（`subBurst: false`）⇒ 不会指数繁殖 ✓
            int subCount = Mathf.Max(0, pm.SubBurstCount);
            int subHit = 0;
            int subPoison = 0;
            // ★ v189：毒区层数**提前读**——子爆也要铺毒（用户需求⑤），所以它得在子爆之前就有值 ✓
            int poisonLayers = Mathf.Max(0, pm.PoisonLayers);
            if (subCount > 0)
            {
                GameAudio.PlayPlugin(PluginEffectKind.SubBurst);   // ★ 声音：子爆（一次爆炸响一次）
                float subRadius = WeaponSystem.SubBurstRadius(blastRadius, pm);
                float subDmg = WeaponSystem.SubBurstDamage(explodeDmg, pm);
                for (int i = 0; i < subCount; i++)
                {
                    Vector2 sc = WeaponSystem.SubBurstCenter(center, i, blastRadius);
                    subHit += BlastDamageAt(sc, subRadius, subDmg, knock * 0.5f, source, pm, false, blastCrit);
                    // ★ v189（用户需求⑤）：**子爆也召唤毒区，半径小一号** ★
                    //   半径 = 主毒区 × 子爆半径比（0.8R × 0.7 = 0.56R，见 `WeaponSystem.SubPoisonRadius`）✓
                    //   每秒伤害也按子爆的伤害算（= 主爆 ×0.70）⇒ "小一号的爆配小一号的毒" ✓
                    //   判据（用户要的读数）：一次肥料爆炸的毒区块数 = **主 1 + 子 N** ——
                    //   块数由 `PluginDotTable` 的行数决定，而 `AddPoisonZone` 只在 0.15m 内合并 ✗
                    //   子爆圆心距主爆心 1.75R、彼此 1.75R（弦长）⇒ 每一块都是**独立一行** ✓
                    if (poisonLayers > 0)
                    {
                        float spr = WeaponSystem.SubPoisonRadius(blastRadius, pm);
                        float subPer = WeaponSystem.PoisonPerLayerPerSecond(subDmg, pm);
                        Dots.AddPoisonZone(sc, spr, subPer, poisonLayers);
                        subPoison++;
                        Bus.Publish(new EnemyAttackFieldEvent { Pos = sc, Radius = spr });
                    }
                    Bus.Publish(new FertilizerBlastEvent
                    {
                        Pos = sc, Radius = subRadius, Fx = PluginFx.For(pm, WeaponSlot.Fertilizer)
                    });
                }
            }

            // ◆攻击形式◆ **毒素**（`fert_poison`）：爆点留 3 秒毒区/层 ✓
            // 半径 = 主爆 × 0.8 ✓ 每层每秒 = **这一炮的实际伤害 × `tuning.dots.poisonPercent`**（默认 0.50）✓
            // ★ 毒区**只登记、不结算** ★：每秒掉血由 `PluginDotTable.Tick` 负责 ——
            //   若在这里也掉一次血，"每秒"就会变成"这一帧额外补一次"✓
            if (poisonLayers > 0)
            {
                float pr = WeaponSystem.PoisonRadius(blastRadius);
                float perLayer = WeaponSystem.PoisonPerLayerPerSecond(explodeDmg, pm);
                Dots.AddPoisonZone(center, pr, perLayer, poisonLayers);
                GameAudio.PlayPlugin(PluginEffectKind.PoisonZone);  // ★ 声音：毒区铺下了
                Bus.Publish(new EnemyAttackFieldEvent { Pos = center, Radius = pr });
            }

            // ★★ v255 / v286：**灼印** —— 整次投掷结算完之后才落印 ★★
            //   判据用**同一个** `blastRadius`（不是另外算一个半径）⇒ 与"谁掉了血"必然一致 ✓
            //
            //   ── 为什么落在**最后**（改前在主爆之后、子爆之前）──
            //   次序问题就是用户报的「配合时不生效」的一半：
            //     改前的次序 = 主爆 → **落印** → 子爆 → 毒区
            //     ⇒ ① 同一次投掷的**子爆**（主爆 ×0.5）立刻吃掉自己刚挂上的印 ✗
            //       ② 之后 3 秒里毒区每 1 秒一跳，**跳跳都比主爆小一个数量级**，
            //          而改前那一跳也会把印摘走（见 `PluginDotTable.Hit` 的 `NoMarkConsume`）✗
            //     改后的次序 = 主爆 → 子爆 → 毒区登记 → **落印**
            //     ⇒ 这一投掷谁都吃不到自己挂的印，印只服务**下一次**伤害 ✓
            //       （与 `PluginField.MarkEnemy` 的语义"下一次受到伤害"逐字对齐）
            if (pm.MarkBonusPct > 0f)
            {
                // ★ v309：**灼印的短脉冲环**（用户：「命中时目标身上加一圈焦橙色的短脉冲环
                //   （贴地，0.2 秒）」）
                //   · 逻辑一行不改：`MarkEnemy` 的调用条件/参数**逐字未动**，脉冲环只是**纯表现**
                //   · 只看**前 `MarkPulseMax` 个**被罩到的敌人（不是"随机挑几个"，是列表顺序里
                //     最先满足半径的那几个 ⇒ 确定性）—— 理由纯粹是 `Bit` 池的账：
                //     同一次爆炸里主环 16 + 二次环 16 + 土粒 12 = 44 格已经占掉，
                //     6 段一圈 × 3 = 18 ⇒ 峰值 62 ≤ 64 ✓（再多就会把土粒挤掉）
                //   · 没被画到的敌人在控制台上仍然有印（`ImGuiHud` 每敌画一次 X + 层数角标），
                //     所以"印在哪"这件事依然读得出来 ✓
                int pulsed = 0;
                for (int i = 0; i < World.Enemies.Count; i++)
                {
                    EnemyActor en = World.Enemies[i];
                    if (en == null || en.IsDead) continue;
                    float rr = blastRadius + en.Radius;
                    if ((en.Pos - center).sqrMagnitude > rr * rr) continue;
                    PluginField.MarkEnemy(en.Id, pm.MarkBonusPct, pm.MarkSeconds);
                    if (pulsed < MarkPulseMax)
                    {
                        AttackVfx.SpawnMarkPulse(en.Pos);
                        pulsed++;
                    }
                }
            }

            // v47：肥料不再只"催熟"——直接催熟 + 收割（呼叫植物帮手），玩家不需要再补一锄头。
            //      逻辑直接作用于本世界；事件只留给表现层（WorldView 画爆炸环）。
            int matured = World.MatureAndHarvestInRadius(center, blastRadius);
            Bus.Publish(new FertilizerBlastEvent
            {
                Pos = center, Radius = blastRadius,
                // 爆炸外观吃肥料这一路的插件
                Fx = PluginFx.For(pm, WeaponSlot.Fertilizer)
            });
            if (matured > 0) SetPrompt($"肥料催熟并收获 {matured} 株植物 → 金币与经验");   // v166：原写"帮手已参战"，而已无帮手
            Telemetry.Emit("fertilizer_blast", ("hit", hit), ("radius", blastRadius),
                ("dmg", explodeDmg), ("harvested", matured), ("subHit", subHit),
                ("poison", poisonLayers), ("subPoison", subPoison), ("poisonZones", Dots.PoisonZoneCount));
            return hit;
        }

        /// <summary>
        /// 一次爆炸的**纯伤害结算**（主爆 / 子爆共用这一份）。
        /// 返回命中的敌人数。
        ///
        /// ★ 为什么抽出来 ★ 主爆与子爆唯一的差别是"圆心 / 半径 / 伤害 / 要不要写日志"，
        /// 判定与结算必须逐字相同 —— 否则"子爆是不是真的像小一号的主爆"就没人能说清 ✓
        /// </summary>
        private int BlastDamageAt(Vector2 center, float radius, float damage, float knock,
                                  PlayerActor source, in WeaponModifiers pm, bool mainBlast, bool crit)
        {
            int hit = 0;
            // ★ v223：爆炸的镜头抖动 —— **只在主爆**抖，子爆不抖 ★
            // 为什么在这里 `mainBlast` 要判：主爆与子爆共用这一份结算（见本函数注释），
            // 若两处都抖 ⇒ 一次爆炸会连抖 2~3 次 ⇒ 手感是"哆嗦"而不是"一记重击" ✓
            if (mainBlast) CombatFeedback.ShakeExplosion();
            // ★ v282：一次爆炸 = 一个吸血批次（主爆/子爆各调一次本函数 ⇒ 各自结算一次）✓
            LifestealBegin(source);
            for (int i = 0; i < World.Enemies.Count; i++)
            {
                EnemyActor e = World.Enemies[i];
                if (e == null || e.IsDead) continue;
                // ★ v181 修复：判定要用"圆心到**身体边缘**"，不是"圆心到圆心" ★
                //
                // 用户报的"爆炸明明罩住了却一点伤害都没有"，根因就是这一行少加了 `e.Radius`：
                // 敌人半径 0.67m，站在爆环上（圆心距 3.2m）时，画面上它的**半个身子在火里**，
                // 而这里判的是 `3.2 > 3.0` ⇒ 直接 continue，一点血都不掉 ✗
                //
                // 同一份判据在另外两处**早就是对的**，只有这里漏了：
                //   近战  —— `RunSession.DoHoeSwing`：`delta.magnitude > range + e.Radius`
                //   激光  —— 见 `FireBeam` 的射线-圆相交
                // 三处必须同一条口径，否则"看着打到了、其实没打到"会在不同武器上表现不一致 ✓
                if (Vector2.Distance(e.Pos, center) > radius + e.Radius) continue;
                // ★ v308：`DealDamage` 的返回值就是"这一下真正扣掉的血/盾"
                //   ⇒ 累进 `LastBlastDealt`（「伏雷」弹字要用它印实际数值）。
                //   只累加 `Dealt`，不累加 `damage`：残血 / 闪避 / 免伤都会让两者分家，
                //   而用户要的是"真实掉的血" ✓
                LastBlastDealt += World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Ailment, RawValue = damage, Source = source, Target = e,
                    Knockback = knock,
                    // ★ v184：把暴击标记传下去 —— 否则肥料的暴击只会"数值变大"，弹字仍是白字 ✗
                    // （与锄头 `DoHoeSwing` 里 `Crit = crit` 同一条纪律，见 `DamageRequest.Crit` 的注释）
                    Crit = crit,
                    // ★★ v286：灼印的"吃倍率但摘不摘印" —— **只有主爆有资格摘印** ★★
                    //   用户报的"灼印与子爆配合时不生效"，就是子爆把印吃掉了：
                    //   子爆量 = 主爆 ×0.5（`WeaponSystem.SubBurstDamage`），却和主爆一样排在
                    //   "下一次投掷的主爆"之前 ⇒ 改前每一次都是最弱的子爆把印用掉 ✗
                    //   现在：子爆照吃倍率（用户要"所有伤害都被放大"），但**不摘印** ⇒
                    //   印留给下一次主爆 / 直击 ✓（规则本体在 `World.DealDamage` 那一个闸门里）
                    NoMarkConsume = !mainBlast
                }).Dealt;
                PluginPostHit(e, damage, pm, source);      // ★ 灼烧 / 减速 / 吸血
                hit++;
            }
            // ★ v282：这一炸罩到的全部敌人累加完 ⇒ 一次吸血结算 ✓
            LifestealSettle();
            if (mainBlast && radius > 0f) Bus.Publish(new EnemyAttackFieldEvent { Pos = center, Radius = radius });
            return hit;
        }
    }
}
