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
    }

    public struct MeleeSwingEvent
    {
        public Vector2 Pos;
        public Vector2 Dir;
        public float Range;
        public float ArcDeg;
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
        /// 由商店的"合成"把两个**同种同级**的种子并成一个高一级的种子得来 ✓
        /// 品质与等级是**两个正交的维度** ✗ ：品质是"这是哪一品级的种子"（买定即固定，决定配色与定价），
        /// 等级是"这一个种子养到第几级"（合成才涨）✓ 所以两者并存，不是二选一 ✓
        /// </summary>
        public int Level = StartingLevel;

        public bool IsEmpty => Def == null;

        /// <summary>合成链的终点：1 → 2 → 3，3 级不能再合。</summary>
        public const int MaxLevel = 3;
        /// <summary>买到 / 开局拿到的种子都是 1 级。</summary>
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
        public SeedSlot SelectedSlot => _slots[Mathf.Clamp(Selected, 0, _slots.Length - 1)];

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
        /// 商店的"一键合成"按钮与"袋满时的购买"都走这里 ✓
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
        /// 而"买进来的种子恒为 1 级"⇒ 只找 **1 级**的同种槽位 ✓
        /// （找任意等级会把 1 级材料并进 3 级的槽 ⇒ 3 级封顶后再买就是白扔钱）
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

        public int IndexOf(SeedSlot slot)
        {
            for (int i = 0; i < _slots.Length; i++) if (_slots[i] == slot) return i;
            return -1;
        }

        /// <summary>
        /// 放入一个种子。v5 起**允许同名重复**：两个相同种子是**合成**的材料，
        /// 所以每次都占用一个新槽位（种子本身无限使用，不消耗）。
        /// 买进来的种子恒为 <see cref="SeedSlot.StartingLevel"/> 级 ✓
        /// </summary>
        public bool AddSeed(SeedDef def, string mutationId, int quality, out string reason)
        {
            reason = null;
            int idx = FirstEmpty();
            if (idx < 0)
            {
                reason = "种子袋已满，先合成或出售";
                return false;
            }
            _slots[idx].Def = def;
            _slots[idx].MutationId = mutationId;
            _slots[idx].Quality = TableDatabase.Tuning.quality.Clamp(quality);
            _slots[idx].Level = SeedSlot.StartingLevel;
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
                bool forcePig = !_pigSpawnedThisWave;
                if (forcePig || w.Rng.Chance(Mathf.Clamp01(cfg.pigChance)))
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

        // v37：刷新价格按倍数增长（3 → 6 → 12 → 24 …）；v168：再减去道具带来的减免
        public int RerollCost(TuningDef t)
            => Mathf.Max(1, Mathf.RoundToInt(t.shop.rerollBase * Mathf.Pow(2f, RerollCount)) - Mathf.Max(0, RerollDiscount));

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
                    && list[i].Item == o.Item && list[i].Plugin == o.Plugin) return true;
            return false;
        }

        public void Refresh(RunSession s)
        {
            // v20：锁定项去重（同一件商品只保留一次，避免"一次锁了多个"的错觉）
            var keep = new List<ShopOffer>();
            for (int i = 0; i < Offers.Count; i++)
                if (Offers[i].Locked && !ContainsSame(keep, Offers[i])) keep.Add(Offers[i]);
            for (int i = 0; i < s.CarriedOffers.Count; i++)
                if (s.CarriedOffers[i].Locked && !ContainsSame(keep, s.CarriedOffers[i])) keep.Add(s.CarriedOffers[i]);
            s.CarriedOffers.Clear();

            Offers.Clear();
            for (int i = 0; i < keep.Count; i++) Offers.Add(keep[i]);
            int slots = Mathf.Max(1, s.T.shop.slots);
            for (int i = Offers.Count; i < slots; i++) Offers.Add(RollOne(s));
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

        private ShopOffer RollSeed(RunSession s)
        {
            ShopDef cfg = s.T.shop;
            // v35：变异词条系统已彻底删除（原来这里有一段死代码会读 null 词条 → 空引用崩溃）

            int weightTotal = 0;
            for (int i = 0; i < cfg.gradeWeights.Length; i++) weightTotal += Mathf.Max(0, cfg.gradeWeights[i].weight);
            int roll = s.World.Rng.RangeInt(0, Mathf.Max(1, weightTotal));
            SeedGrade grade = SeedGrade.D;
            int acc = 0;
            for (int i = 0; i < cfg.gradeWeights.Length; i++)
            {
                acc += Mathf.Max(0, cfg.gradeWeights[i].weight);
                if (roll < acc)
                {
                    grade = DefParser.ParseEnum(cfg.gradeWeights[i].grade, SeedGrade.D);
                    break;
                }
                grade = DefParser.ParseEnum(cfg.gradeWeights[i].grade, SeedGrade.D);
            }
            SeedDef def = TableDatabase.RandomSeed(ref s.World.Rng, grade);
            int quality = def.RarityTier;   // v44: seed rarity is fixed per seed
            return new ShopOffer
            {
                Kind = OfferKind.Seed,
                Seed = def,
                Quality = quality,
                // v118：走统一定价入口（**必须带波次**，否则种子不随回合涨价）
                Price = SeedSlot.ShopPriceFrom(def.Price, quality, s.WaveIndex),
                Title = def.Name,
                Subtitle = $"{Rarity.NameOf(quality)} · {RunSession.AtkTypeText(def)}"
            };
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
            PluginAsset[] pool = PluginPool();
            if (pool.Length == 0) return RollSeed(s);   // 一件插件都没有 ⇒ 退到种子 ✗ 不留空货位 ✓

            ShopDef cfg = s.T.shop;
            int wave = Mathf.Max(1, s.WaveIndex);
            int tier = RollGoodsTier(s, cfg.itemGradeWeights, wave);
            PluginAsset picked = PickPluginOfTier(s, pool, tier);
            if (picked == null) return RollSeed(s);

            int quality = TableDatabase.Tuning.quality.Clamp((int)picked.quality);
            return new ShopOffer
            {
                Kind = OfferKind.Plugin,
                Plugin = picked,
                Quality = quality,
                // v118：同样走统一定价入口（**必须带波次**）
                Price = SeedSlot.ShopPriceFrom(picked.basePrice, quality, s.WaveIndex),
                Title = picked.displayName,
                Subtitle = $"{Rarity.NameOf(quality)} · 插件（装到哪把武器由你决定）"
            };
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

        /// <summary>供自检 / 调试查询：当前能刷出几件插件 ✗（只读，不触发抽取）</summary>
        public static int PluginPoolSize() => PluginPool().Length;

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
        private static int RollGoodsTier(RunSession s, GradeWeightDef[] weights, int wave)
        {
            if (weights == null || weights.Length == 0) return 0;
            float total = 0f;
            var eff = new float[weights.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                // 等级越高：随波次提升越多；低等级反之下降（不会为负）
                float gradeBias = i / Mathf.Max(1f, weights.Length - 1f);
                float mult = 1f + gradeBias * (0.05f * (wave - 1))
                                  - (1f - gradeBias) * 0.04f * (wave - 1);
                eff[i] = Mathf.Max(0.01f, weights[i].weight * Mathf.Max(0.05f, mult));
                total += eff[i];
            }
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
        private ShopOffer RollItem(RunSession s)
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

            ItemDef item = TableDatabase.RandomItemOfGrade(ref s.World.Rng, grade);
            // v118：走统一定价入口 —— 之前三处各写一份，只有这里是对的
            int price = SeedSlot.ShopPriceFrom(item.Price, picked, wave);
            return new ShopOffer
            {
                Kind = OfferKind.Item,
                Item = item,
                Quality = picked,
                Price = price,
                Title = item.Name,
                Subtitle = $"[{grade} 级道具] {item.Desc}"
            };
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
                        // 买进来的种子恒为 1 级 ⇒ 传 StartingLevel ✓
                        if (s.Bag.TryMergeWithExisting(o.Seed, o.MutationId, SeedSlot.StartingLevel, out SeedSlot _))
                            break;      // 已被合成消化 —— 钱照扣 ✓
                        reason = "种子袋已满，且没有同种同级的种子可以合成（先合成或出售）";
                        return false;
                    }
                    if (!s.Bag.AddSeed(o.Seed, o.MutationId, o.Quality, out reason)) return false;
                    s.AutoPlantSeed(o.Seed);   // v24：买到的新种子自动种下
                    break;
                }
                case OfferKind.Item:
                {
                    if (s.ItemStacks.TryGetValue(o.Item.Id, out int n) && n >= o.Item.MaxStack)
                    {
                        reason = "该道具已叠满";
                        return false;
                    }
                    s.ApplyItem(o.Item);
                    break;
                }
                case OfferKind.Plugin:
                {
                    // 插件只入背包（`WeaponSlot.None`）✗ —— **装到哪把武器由玩家之后自己决定** ✓
                    // 购买动作替玩家选武器会让"同一件插件装错了就废了"，
                    // 而安装本身有适用性 / 互斥 / 上限检查，那些检查属于 `WeaponSystem.Install` ✓
                    if (s.Goods == null || o.Plugin == null) { reason = "插件栏不可用"; return false; }
                    s.Goods.Plugins.Add(new PluginInstance(o.Plugin, WeaponSlot.None));
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
        }

        /// <summary>v47：当前存活的浮动文字。逻辑在 FixedTick 里推进，表现层（ImGuiHud）只读它画字。</summary>
        public readonly List<FloatingText> Popups = new List<FloatingText>();

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

        /// <summary>各工具的品质档位（-1 = 尚未拥有）。</summary>
        public readonly int[] ToolLevel = { -1, -1, -1, -1 };
        /// <summary>本帧动作实际作用的耕地数量（HUD 显示"同时 n 块"）。</summary>
        public int ActiveGroupCount;
        /// <summary>v8：本帧正在耕作 → 禁止任何攻击（含左键）。</summary>
        public bool FarmingThisFrame;
        /// <summary>v8 度量：耕作期间被击中次数（用来评估"耕作禁止攻击"的代价）。</summary>
        public int FarmHits;
        /// <summary>v14：当前地块已完成的施肥次数与所需次数（HUD 弹出施肥 UI 用）。</summary>
        public int FertilizeHits;
        public int FertilizeTarget;

        /// <summary>工具品质带来的数值倍率（未拥有按白档算）。</summary>
        public float ToolMult(int toolIndex)
        {
            if (toolIndex < 0 || toolIndex >= ToolCount) return 1f;
            return T.quality.MultOf(Mathf.Max(0, ToolLevel[toolIndex]));
        }

        /// <summary>
        /// 同时操控的耕地数 = 1 + 品质档位，上限 tools.workCapMax（默认 4）。
        /// v45：删除 WorkCap 属性加成——授予它的"操作手册"道具早已不存在，该项恒为 0。
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
        /// 购买 / 合体一件工具（与种子同一套机制）：
        /// 未拥有 → 直接获得该品质；同品质 → 合体升一档；更高品质 → 直接替换；更低 → 拒绝。
        /// </summary>
        public bool UpgradeTool(int toolIndex, int quality, out string reason)
        {
            reason = null;
            if (toolIndex < 0 || toolIndex >= ToolCount) { reason = "无效的工具"; return false; }
            int q = T.quality.Clamp(quality);
            int owned = ToolLevel[toolIndex];
            if (owned < 0) { ToolLevel[toolIndex] = q; return true; }
            if (q == owned)
            {
                if (owned >= T.quality.Max) { reason = "该武器已是最高档"; return false; }
                ToolLevel[toolIndex] = T.quality.Clamp(owned + 1);
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
        /// </summary>
        public int AutoInstallPending(out string summary)
        {
            int ok = 0;
            var left = new List<string>();
            for (int i = 0; i < Goods.Plugins.Count; i++)
            {
                PluginInstance pi = Goods.Plugins[i];
                if (pi == null || pi.Asset == null || pi.Slot != WeaponSlot.None) continue;
                WeaponSlot target = PreferredSlotFor(pi.Asset);
                if (target == WeaponSlot.None) { left.Add(pi.Asset.displayName + "（无适用武器）"); continue; }
                if (WeaponSystem.Install(Goods, pi.Asset, target, out string why))
                {
                    ok++;
                    i--;                          // 背包里少了一件（`InstallFromPending` 是**挪**不是复制），索引回退一格
                }
                else left.Add($"{pi.Asset.displayName}（{why}）");
            }
            summary = $"自动装配 {ok} 件；背包剩余 {Goods.PendingCount} 件"
                      + (left.Count > 0 ? "，未装上：" + string.Join("、", left) : "");
            SetPrompt(summary);
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
        /// <summary>v11：升级后待抽的卡（每次升级 +drawsPerLevel 次）。</summary>
        public int PendingRewardDraws;
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
            Telemetry.Clear();
            World = new World(T, seed);
            Progress = new PlayerProgress(T);

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
                Radius = Actor.ScaledRadius(0.62f)
            };
            player.Health.Setup(Progress.Stats.Get(StatId.MaxHp), Progress.Stats.Get(StatId.Shield));
            World.Player = player;
            World.Farm.SetMaxPlots(Progress.MaxPlots);
            SyncSeedSlots();   // v29：耕地增加 → 种子袋上限同步（1:1）

            // v48：开局发放金币——Prepare 阶段就能进商店，第一回合前不至于空手
            Gold = Mathf.Max(0, T.player.startGold);
            WaveIndex = 0;
            RunTime = 0f;
            TotalRerolls = 0;
            InitialChoices = RollInitialChoices();
            // v9：初始种子固定为「D 级 + 白品质」
            InitialChoiceQuality = new int[InitialChoices.Length];
            for (int i = 0; i < InitialChoiceQuality.Length; i++) InitialChoiceQuality[i] = 0;
            InitialQuality = 0;
            MergeSelected = -1;
            InitialSeed = null;
            Bag.Selected = 0;

            SetPhase(PhaseState.Prepare, T.shop.firstPrepareTime);
            Telemetry.Emit("run_start", ("seed", seed), ("build", Application.unityVersion));

            Bus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            Bus.Subscribe<RainPickupEvent>(OnRainPickup);   // v23：雨滴 → 水壶 +20
            Bus.Subscribe<ExpOrbDroppedEvent>(OnExpOrbDropped);
            Bus.Subscribe<ExpOrbPickupEvent>(OnExpOrbPickup);
            Bus.Subscribe<LevelUpEvent>(OnLevelUp);
            Bus.Subscribe<PlantSpawnedEvent>(OnPlantSpawned);
            Bus.Subscribe<PlantDiedEvent>(OnPlantDied);
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
            Bus.Unsubscribe<PlantSpawnedEvent>(OnPlantSpawned);
            Bus.Unsubscribe<PlantDiedEvent>(OnPlantDied);
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
            SetPrompt($"拾取雨滴 → 水壶 +{e.Ammo}（{WaterAmmo}/{WaterAmmoMax}）");
            Telemetry.Emit("rain_pickup", ("water", WaterAmmo));
        }

        private void OnEnemyKilled(EnemyKilledEvent e)
        {
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
            PendingRewardDraws += Mathf.Max(1, T.levelReward.drawsPerLevel);
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
                RewardDrawTotal = RewardDrawIndex + PendingRewardDraws;
                DrawRewardCards();
                SetPhase(PhaseState.LevelReward, 0f);
                Telemetry.Emit("level_reward_start", ("draws", PendingRewardDraws), ("trigger", "level_up"));
            }
        }

        /// <summary>v47：把逻辑层发来的浮动文字收进列表，由 FixedTick 推进生命周期。</summary>
        private void OnFloatingText(FloatingTextEvent e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            if (Popups.Count >= MaxPopups) Popups.RemoveAt(0);
            // v48：在对象周围的小圆内随机落点——同一目标连续弹字时不会完全重叠。
            //      用表现随机流（VisualRng），不污染逻辑确定性。
            Vector2 offset = World != null
                ? World.VisualRng.InsideCircle(Mathf.Max(0f, T.ui.popupRadius))
                : Vector2.zero;
            Popups.Add(new FloatingText
            {
                Pos = e.Pos + offset,
                Text = PopupStyle.TextOf(e.Kind, e.Text),
                Kind = e.Kind,
                // v69：颜色与字号统一由 PopupStyle 决定，发射点不再各写各的。
                // 事件里若显式给了 Scale（例如伤害按数值大小放大），就在类型基准上再乘。
                Color = PopupStyle.ColorOf(e.Kind),
                Ttl = PopupLifetime,
                Total = PopupLifetime,
                Scale = PopupStyle.ScaleOf(e.Kind) * (e.Scale <= 0f ? 1f : e.Scale)
            });
        }

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
        private void OnPlantSpawned(PlantSpawnedEvent e)
        {
            Telemetry.Emit("plant_spawn", ("seed", e.SeedId), ("grade", e.Grade.ToString()),
                ("mutation", e.MutationId ?? "none"), ("plot", e.PlotIndex));
        }
        private void OnPlantDied(PlantDiedEvent e)
        {
            Telemetry.Emit("plant_death", ("seed", e.SeedId), ("plot", e.PlotIndex), ("lifetime", e.Lifetime));
        }

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

        /// <summary>
        /// 写一条效果。**加减益的唯一写入路径** ✓
        ///
        /// `sign` = +1 增益 / −1 减益。
        /// Add 类：值 = `±(add + 成长) × 层数`（层数直接相加，符合"叠 3 层 = 3 倍"的直觉）✓
        /// Percent 类：值 = `1 ± (add + 成长) × 层数 / 100` —— **合并成一次乘法** ✗
        ///   不能写成 1.15 × 1.15（那是复利，叠 3 层会从 +45% 变成 +52%）✓
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

        /// <summary>v11 升级抽卡：一张卡 = 一个主要属性 + 增量（相同属性会融合成一张）。</summary>
        public sealed class LevelRewardCard
        {
            public RewardStatDef Def;
            public int Amount;      // 已融合的增量（数值相加）
            public int Copies;      // 融合了几张
            public bool IsPercent => Def != null && string.Equals(Def.mode, "Percent", System.StringComparison.OrdinalIgnoreCase);
            public string Label => Def == null ? "?" : (IsPercent ? $"{Def.name} +{Amount}%" : $"{Def.name} +{Amount}");
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

        /// <summary>v13：重抽消耗金币（无限次，每次价格递增）。</summary>
        public int RewardRerollCost =>
            Mathf.RoundToInt(T.levelReward.rerollBasePrice * Mathf.Pow(2f, RewardRerollCount));   // v37：倍数增长

        public bool RerollRewards()
        {
            int cost = RewardRerollCost;
            if (Gold < cost)
            {
                SetPrompt($"金币不足（重抽需要 {cost}）");
                return false;
            }
            Gold -= cost;
            RewardRerollCount++;
            DrawRewardCards();
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
            if (card.IsPercent)
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

        /// <summary>v23：雨滴 → 水壶 +20；小猪 → 肥料 +1。</summary>
        public void AddWaterAmmo(int amount)
        {
            WaterAmmo = Mathf.Clamp(WaterAmmo + amount, 0, WaterAmmoMax);
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
            Bus.Publish(new MeleeSwingEvent
            {
                Pos = p.Pos,
                Dir = p.AimDir.sqrMagnitude > 0.001f ? p.AimDir : new Vector2(0f, 1f),
                Range = 3.2f,
                ArcDeg = 110f
            });
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
                    TickPendingBlasts(BlastWindup + 0.01f);
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
            // v94：选种页是**锁定页面**（需求："处理 esc 外其它操作锁定"）。
            //
            // 这一处是唯一漏掉阶段判断的帧级入口：以前在选种界面滚轮，
            // 武器会真的切过去、还弹一条"武器：水壶（前摇 …）"的提示——
            // 页面因此看起来"能用"，但当前阶段根本没有战斗，切了也没意义，只让人困惑。
            // 旁边两处（重抽 / 奖励重抽）本来就有 `Phase == …` 判断，只有这里没有。
            //
            // ESC 不受影响：Paused 在 FixedTick 的 switch 之前就短路了。
            if (input.WheelScroll != 0)
            {
                if (Phase != PhaseState.Prepare && World != null && World.Player != null && World.Player.Alive)
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
            if (!_truckArmed && !IsOver) { Truck.Reset(Clock); _truckArmed = true; }
            Truck.Tick(Clock);

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
                    Vector2 wp = ShopTruck.PointAt(Truck.PhaseT(Clock.GameTime)) * T.farm.arenaHalf;
                    Debug.Log($"[Seedfall] 商店车 → {Truck.Current}｜{Clock.TimeText()}"
                              + $"｜进度 {Truck.PhaseT(Clock.GameTime):0.00}"
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
            if (Truck.Current == ShopTruck.Phase.Dock && !IsOver && input.ShopOpen
                && World != null && World.Player != null)
            {
                Vector2 np = ShopTruck.PointAt(Truck.PhaseT(Clock.GameTime));
                float half = T.farm.arenaHalf;
                Vector2 dockPos = np * half;
                float d = (World.Player.Pos - dockPos).magnitude;
                if (Truck.CanShop(d))
                {
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
                    SetPhase(PhaseState.Shop, T.shop.prepareTime);
                }
            }

            // ── 巡回清扫 ──
            // 规则复用已断言过的 ShouldSweep（排除 Boss 与玩家、且只在 Looping 清）✓
            // **移除但不结算任何奖励** —— 直接移出列表 = 不走击杀路径 = 不触发经验/掉落 ✓
            // 这正是需求"清除路径上的怪物和植物，不掉落经验值"最直接的实现 ✓
            if (World == null || Truck.Current != ShopTruck.Phase.Looping) return;
            Vector2 np2 = ShopTruck.PointAt(Truck.PhaseT(Clock.GameTime));
            Vector2 truckPos = np2 * T.farm.arenaHalf;
            float r = T.farm.arenaHalf * 0.16f;
            for (int i = World.Enemies.Count - 1; i >= 0; i--)
            {
                EnemyActor e = World.Enemies[i];
                if (e == null) continue;
                bool isBoss = e.Def != null && e.Def.Kind == EnemyKind.Boss;
                if (!Truck.ShouldSweep(isBoss, false)) continue;
                if ((e.Pos - truckPos).sqrMagnitude > r * r) continue;
                World.Enemies.RemoveAt(i);
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

        public void FixedTick(float dt, in FrameInput input)        {
            TickPopups(dt);   // v47：浮动文字即使在暂停/结算中也自然消失，不卡在屏幕上
            if (IsOver) return;
            if (Paused) return;

            RunTime += dt;
            Clock.SyncTo(RunTime);      // v141：Clock 是 RunTime 的派生视图，不自己累加（消债）

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
                Waves.SpawnOne(new WaveBatchDef { EnemyId = "boss", Count = 1, StartAt = 0f, Interval = 1f }, World);

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

            // v30：每帧对齐「种子袋容量 = 最大耕地数」（UI 格子随之增加）
            if (Bag.SlotCount != Mathf.Max(1, Progress.MaxPlots)) SyncSeedSlots();

            Waves.Tick(dt, World);
            HandlePlayer(dt, input);
            TickBeamCds(dt);       // v90：衰减"同一目标的再受伤间隔"（放在玩家更新之后，本帧刚打中的目标拿到完整 CD）
            TickPendingBlasts(dt); // v91：推进肥料爆炸前摇
            World.Simulate(dt);

            // 掉落进入钱包（World.GoldEarned 是统计值，这里同步到可花费金币）
            SyncGoldFromWorld();

            if (World.Player.Health.IsDead)
            {
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

            // 分钟硬上限：只跑不打会让场上越积越多（软锁），到点强制收尾
            // ★ v165：触发点从 `Wave.Duration × cap`（30~90 秒 × 1.35）改成【一分钟 × cap】★
            float cap = Mathf.Max(1f, T.waves.hardCapFactor);
            float minuteLen = Clock.SecondsPerMinute;
            if (!Waves.IsBossWave && Waves.CombatElapsed > minuteLen * cap)
            {
                int despawned = World.DespawnAllEnemies();
                if (despawned > 0)
                    Telemetry.Emit("wave_timeout", ("wave", WaveIndex), ("elapsed", Waves.CombatElapsed), ("despawned", despawned));
            }

            // v13: 分钟走完就结束本回合（残敌清场），避免"时间为 0 却一直运行"
            if (!Waves.IsBossWave && Waves.CombatElapsed >= minuteLen
                && World.Enemies.Count > 0)
            {
                World.DespawnAllEnemies();
            }

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
                if (dist > range + 1.2f) continue;                                    // 判定框（含地块半径）
                if (dist > 0.01f && Vector2.Angle(dir, to) > half) continue;          // 扇形内
                if (HarvestPlot(plot)) harvested++;
            }
            return harvested;
        }

        /// <summary>v27：收割一块成熟土地。v47：逻辑收敛到 World.HarvestPlot（锄头与肥料共用），这里只做提示与埋点。</summary>
        public bool HarvestPlot(FarmPlot plot)
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
            if (!World.HarvestPlot(plot)) return false;
            // ★ v168：这里要的是**地里种的那一份定义**，不是"按 id 回查表" ✗
            // `TableDatabase.GetSeed` 对**不存在的 id 抛异常**（只有**空** id 才返回 null ✓）
            // ⇒ 与其同形的下一行（`InteractPlotByAttack`）一样，这行会在"某种 id 不在表里"时崩 ✓
            //   —— 而收割路径撞上它就是**挥一次锄头崩一次**（与 v166 修过的 `GetSeed(null)` 同形）✗
            // `plot.CropDef` 就是 `BeginPlant` 传进来的那份定义 ⇒ 每格都有，且**不需要查表** ✓
            SeedDef def = plot.CropDef;
            // ★ v166：提示文案原来写「→ 植物帮手参战」✗ 而 v165 已经删掉"收割产生帮手" ✓
            // ⇒ 这句话在**对着玩家的界面**上撒谎：他说收割会得到帮手，但什么也没多出来 ✗
            // 收益的真实内容是 World.HarvestPlot 发的**金币与经验**（每格 price/5 与 price/10）✓
            SetPrompt($"收割 {def?.Name} → 金币与经验（土地重新生长）");
            Telemetry.Emit("plot_harvest", ("seed", seedId), ("plants", World.Plants.Count), ("plot", plotIndex));
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
                            Telemetry.Emit("plot_harvest", ("seed", plot.CropSeedId), ("plants", World.Plants.Count));
                        }
                    }
                    break;
                case ToolCan:
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
            StartWave(WaveIndex + 1);
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
                    PlantAlly plant = World.FindPlantOnPlot(plot.Index);
                    if (plant != null && plant.Alive) plant.Health.Kill();
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
            ClearTransientCombatState();   // v100：先清干净上一回合残留的瞬时状态
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
            CenterPlayerForNewWave();   // v19：每回合开始回到世界中心
            // v23：每回合开始弹药——水壶 +50（上限 100），肥料 0
            WaterAmmo = Mathf.Min(WaterAmmoMax, WaterAmmo + 50);
            FertAmmo = 0;
            Telemetry.Emit("ammo_reset", ("water", WaterAmmo), ("fert", FertAmmo));
            SetPhase(PhaseState.Combat, 0f);
            Telemetry.Emit("wave_start", ("wave", index), ("player_level", Progress.Level),
                ("plots_used", World.Farm.UsedPlotCount), ("gold", Gold),
                ("grace", Waves.GraceSeconds), ("enemy_budget", EnemyBudget(index)));
        }

        /// <summary>
        /// v100：清掉**跨回合不该继承**的瞬时战斗状态。
        ///
        /// 触发这条的是一个具体现象：玩家在回合最后一秒丢出肥料，**爆炸却发生在下一回合**。
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

            _pendingBlasts.Clear();      // 未引爆的肥料：不该在新回合才炸
            _beamHitCd.Clear();          // 激光的每目标 CD：新回合每个目标都该是可打的
            StopBeam();                  // 激光的"正在发射"与扣费计时：新回合要重新收起手费
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
                        PlantAlly growingPlant = World.FindPlantOnPlot(plot.Index);
                        if (growingPlant != null) growingPlant.Health.Kill();
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
                    // v10：播种只种树（不再选择种子）；树的产物 = 玩家拥有的所有种子
                    SeedDef sowSeed = null;
                    SeedSlot selected = Bag.SelectedSlot;

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

            // v90：水壶 = **持续激光**。
            // 必须放在 p.CanAttack 之外 —— CanAttack 里含通用武器冷却，
            // 放在里面就退化成"打一次等一次 CD"，正是要改掉的那个模型。
            // 松开左键（或换武器）时把 _beamFiring 清掉，下次按住重新收起手费。
            if (p.WeaponIndex == PlayerActor.WeaponCan && input.Fire && !FarmingThisFrame && !p.IsDead)
            {
                Vector2 toMousePanel = input.AimWorld - p.Pos;
                if (toMousePanel.sqrMagnitude > 0.0001f) p.AimDir = toMousePanel.normalized;
                TickBeam(p);
            }
            else
            {
                if (p.WeaponIndex == PlayerActor.WeaponCan) StopBeam();

            if (p.CanAttack && !FarmingThisFrame)
            {
                // v33：**只保留手动攻击**——不按左键就不攻击（自动攻击与自动锁定攻击已删除）
                bool manual = input.Fire;
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

            // 种子袋点击：选槽 / 合体
            // v95：商品锁定已挪到 ConsumeFrameEdges（见那里的说明）

            if (input.SlotClicked) HandleBagClick(input.ClickedSlot);

            if (input.SelectSeed >= 0) Bag.Selected = Mathf.Clamp(input.SelectSeed, 0, Bag.SlotCount - 1);
        }

        /// <summary>每回合开始自动重置玩家状态：生命/护盾回满、冷却与前摇清零、无敌帧清零。</summary>
        /// <summary>v19：每回合开始把角色刷新到世界中心。</summary>
        private void CenterPlayerForNewWave()
        {
            if (World == null || World.Player == null) return;
            World.Player.Pos = Vector2.zero;
            Telemetry.Emit("player_recenter", ("wave", WaveIndex));
        }

        private void ResetPlayerForNewWave()
        {
            PlayerActor p = World.Player;
            if (p == null) return;
            p.Health.Heal(p.Health.MaxHp);
            p.Health.RefillShield();
            p.InvulnRemaining = 0f;
            p.HitFlash = 0f;
            p.SwitchWindup = 0f;
            p.SinceDamaged = 999f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
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
        // v171：插件在"命中之后"的机制 —— 三条攻击路径**共用这一份**
        //
        // ★ 为什么必须共用 ★
        // 灼烧/寒冷/连锁/暴击炸裂/回弹药这五条，锄头、水壶、肥料都要能触发 ✗
        // 若各写一份，"同一件插件插在不同武器上效果不同"就会变成隐式约定，
        // 而且改一处必漏两处 ✓（本文件里已经有三处重复的激光判定代码作为前车之鉴）
        //
        // ★ 为什么放在命中**之后** ★
        // 它们不改伤害公式，而是挂"副作用"：进异常状态、向别人溅射、回弹药 ✓
        // 所以数值上它们和 `WeaponModifiers` 的数值区是**两条不同的路** ✓
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>剑气流：每条剑气之间的夹角（度）。12° 在 6.4m 处错开 ≈1.3m ⇒ 能覆盖不同的一条线。</summary>
        private const float SwordWaveSpreadDeg = 12f;
        /// <summary>剑气流射程 = 近战判定范围 × 本倍数。</summary>
        private const float SwordWaveRangeMult = 2f;
        /// <summary>剑气流判定半宽（米）。比激光（1.4）窄一点：它是一道刃，不是一条水柱。</summary>
        private const float SwordWaveHalfWidth = 0.9f;
        /// <summary>激光多发时，每条之间的夹角（度）。</summary>
        private const float BeamSpreadDeg = 8f;
        /// <summary>爆炸点 +N 时，额外落点绕主落点的半径（米）。</summary>
        private const float BlastSpotRingRadius = 2.5f;
        /// <summary>暴击炸裂的半径（米）。</summary>
        private const float CritBlastRadius = 3f;
        /// <summary>共振连锁的搜索半径（米）。</summary>
        private const float ChainSearchRadius = 6f;

        /// <summary>把向量绕原点旋转 <paramref name="rad"/> 弧度（确定性、不依赖 UnityEngine 的隐式转换）。</summary>
        private static Vector2 Rotate(Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>
        /// **一剑/一束/一炸命中之后**要发生的插件副作用。三条路都调它 ✓
        /// </summary>
        /// <param name="e">被打到的敌人</param>
        /// <param name="damage">这一击打出的原始伤害（灼烧/连锁/炸裂都按它的比例算）</param>
        /// <param name="crit">这一击是否暴击（暴击炸裂的触发条件）</param>
        /// <param name="m">该武器的插件修正</param>
        /// <param name="source">伤害来源（玩家）</param>
        /// <param name="slot">哪把武器 —— 决定"击杀回弹药"回到哪个弹药池</param>
        private void PluginPostHit(EnemyActor e, float damage, bool crit, in WeaponModifiers m,
                                   PlayerActor source, WeaponSlot slot)
        {
            if (e == null || !m.HasPostHit) return;
            FormulaDef f = T.formula;

            // ③ 小机制：命中减速（在既有寒冷体系上**再加**几层，不是另起一套减速）
            if (m.ChillPerHit > 0 && !e.IsDead) e.Status.AddChill(m.ChillPerHit, damage, f);

            // ④ 大机制：命中灼烧（走既有 Burn；"烧着的敌人再被非异常命中会炸开"由 Combat.cs 负责）
            float burn = WeaponSystem.BurnAmountFor(damage, m);
            if (burn > 0f && !e.IsDead) e.Status.Apply(AilmentKind.Burn, burn, f, 1f);

            // ⑤ 特殊效果：暴击炸裂（只在真暴击时）
            float critBlast = WeaponSystem.CritBlastDamage(damage, crit, m);
            if (critBlast > 0f) PluginSplash(e.Pos, critBlast, source, CritBlastRadius, e);

            // ④ 大机制：共振连锁（一跳一次，不递归 —— 溅射走的是普通 DealDamage，不会再进这里）
            float step = WeaponSystem.ChainStepDamage(damage, m);
            if (step > 0f) PluginChain(e, step, m.ChainTargets, source);

            // ③ 小机制：击杀回弹药
            if (m.AmmoPerKill > 0f && e.IsDead) RefillAmmo(slot, Mathf.RoundToInt(m.AmmoPerKill));
        }

        /// <summary>插件溅射：以 <paramref name="center"/> 为心、半径内除 <paramref name="exclude"/> 外全部吃一份伤害。</summary>
        private void PluginSplash(Vector2 center, float damage, PlayerActor source, float radius, EnemyActor exclude)
        {
            if (damage <= 0f) return;
            for (int i = 0; i < World.Enemies.Count; i++)
            {
                EnemyActor e = World.Enemies[i];
                if (e == null || e.IsDead || e == exclude) continue;
                if (Vector2.Distance(e.Pos, center) > radius + e.Radius) continue;
                World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical,
                    RawValue = damage,
                    Source = source,
                    Target = e,
                    IgnoreDodge = true        // 溅射不是"瞄准"来的，不该被闪避（与植物反伤同一条约定）
                });
            }
        }

        /// <summary>
        /// 共振连锁：从 <paramref name="from"/> 出发，向最近的其它敌人**逐个跳** <paramref name="hops"/> 次。
        /// 每次跳跃的落点成为下一次的起点（与星藤植物的 Chain 同一形状）✓
        /// </summary>
        private void PluginChain(EnemyActor from, float stepDamage, int hops, PlayerActor source)
        {
            if (stepDamage <= 0f || hops <= 0) return;
            Vector2 cursor = from.Pos;
            EnemyActor prev = from;
            for (int n = 0; n < hops; n++)
            {
                EnemyActor next = null;
                float best = ChainSearchRadius * ChainSearchRadius;
                for (int i = 0; i < World.Enemies.Count; i++)
                {
                    EnemyActor e = World.Enemies[i];
                    if (e == null || e.IsDead || e == prev) continue;
                    float d = (e.Pos - cursor).sqrMagnitude;
                    if (d < best) { best = d; next = e; }
                }
                if (next == null) break;
                World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical,
                    RawValue = stepDamage,
                    Source = source,
                    Target = next,
                    IgnoreDodge = true
                });
                Bus.Publish(new BeamEvent { From = cursor, To = next.Pos, HalfWidth = 0.35f });   // 表现层画一跳
                cursor = next.Pos;
                prev = next;
            }
        }

        /// <summary>
        /// 击杀回弹药。**按武器进各自的池**：水壶 → 水壶弹药；肥料 → 肥料弹药 ✓
        /// 锄头自己没有弹药池 ⇒ 回**水壶**弹药（锄头的剑气是远程手段，补给通道就是水壶充能）✓
        /// </summary>
        private void RefillAmmo(WeaponSlot slot, int amount)
        {
            if (amount <= 0) return;
            if (slot == WeaponSlot.Fertilizer) FertAmmo = Mathf.Min(FertAmmoMax, FertAmmo + amount);
            else WaterAmmo = Mathf.Min(WaterAmmoMax, WaterAmmo + amount);
        }

        /// <summary>
        /// ★攻击形式拓展★ **剑气流**：挥击的同时沿瞄准方向劈出 <paramref name="waves"/> 道剑气。
        ///
        /// 三条约束，每条都是为了"它真的是新形式，而不是换个说法的加伤"：
        ///   ① 剑气只打**近战判定框之外**的敌人（`along > meleeRange`）✗
        ///      —— 框内的已经被挥击打过了，再打一次就是隐蔽的双倍伤害 ✓
        ///   ② 同一个敌人**只被一道剑气命中**（跨条去重）✗
        ///      —— 否则"剑气 +1 条"会变成"单体伤害 +100%"，与"形式拓展"的语义不符 ✓
        ///   ③ 第 k 道绕瞄准方向偏 `12°×(k-(n-1)/2)` ⇒ **多一条 = 多盖一条线** ✓
        /// </summary>
        private void FireSwordWaves(PlayerActor p, in WeaponModifiers m, int waves,
                                    float dmg, bool crit, float meleeRange, float knock)
        {
            if (waves <= 0) return;
            float waveRange = meleeRange * SwordWaveRangeMult;
            Vector2 baseDir = p.AimDir.sqrMagnitude < 0.001f ? Vector2.right : p.AimDir.normalized;
            _swordWaveHit.Clear();
            for (int k = 0; k < waves; k++)
            {
                float off = SwordWaveSpreadDeg * (k - (waves - 1) * 0.5f);
                Vector2 dir = Rotate(baseDir, off * Mathf.Deg2Rad);
                for (int i = 0; i < World.Enemies.Count; i++)
                {
                    EnemyActor e = World.Enemies[i];
                    if (e == null || e.IsDead) continue;
                    if (!_swordWaveHit.Add(e.Id)) continue;              // ② 跨条去重
                    Vector2 rel = e.Pos - p.Pos;
                    float along = Vector2.Dot(rel, dir);
                    if (along <= meleeRange || along > waveRange) continue;   // ① 只在挥击框之外
                    float perp = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                    if (perp > SwordWaveHalfWidth + e.Radius) continue;
                    World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical,
                        RawValue = dmg,
                        Source = p,
                        Target = e,
                        Knockback = knock * 0.5f,
                        Crit = crit
                    });
                    PluginPostHit(e, dmg, crit, m, p, WeaponSlot.Hoe);
                }
                // 表现：一道直线的剑气（复用 BeamEvent；表现层要单独的样子是美术那一层的事）
                Bus.Publish(new BeamEvent { From = p.Pos, To = p.Pos + dir * waveRange, HalfWidth = SwordWaveHalfWidth });
            }
        }

        /// <summary>一次剑气流里已经被打过的敌人（跨条去重，见 FireSwordWaves 的 ②）。</summary>
        private readonly HashSet<int> _swordWaveHit = new HashSet<int>();

        private void SwingHoe(PlayerActor p, TuningDef t)
        {
            StatBlock st = Progress.Stats;
            ToolDef hoe = t.tools.hoe;
            WeaponModifiers pm = ModsFor(WeaponSlot.Hoe);          // ★ v171：插件修正

            // 攻速也吃插件（0.15 = 间隔 ÷1.15）
            p.WeaponCds[PlayerActor.WeaponHoe] =
                hoe.cooldown / Mathf.Max(0.05f, p.AttackSpeed * (1f + pm.AttackSpeedPct));

            // ★ v171：范围只算一次 ★
            // 原来 `hoe.range * st.Get(StatId.AtkRange)` 在同一段里被算了三遍
            //（收割判定 / 表现事件 / 伤害判定）—— 三处只要有一处漏改，"看到的"与"打到的"就会分家 ✗
            // 现在是一个值喂三处，插件的 Range 自然三处一起生效 ✓
            float range = hoe.range * st.Get(StatId.AtkRange) * (1f + pm.RangePct);

            // ═══ v27：锄头挥击 = 用攻击判定框检测成熟植物并收割（最高优先级，先于打敌人）═══
            int justHarvested = HarvestRipeInMeleeBox(p.Pos, p.AimDir, range, hoe.arc);

            // 近战判定框特效（表现层订阅）
            Bus.Publish(new MeleeSwingEvent { Pos = p.Pos, Dir = p.AimDir, Range = range, ArcDeg = hoe.arc });

            // ★ v171：插件改的是**攻击公式的入参**，公式本身仍然只有一处 ✓
            // v162（A3）：锄头 → **近战伤害**；公式 = (基准伤害 + 近战伤害) × 伤害倍率
            float baseValue = hoe.baseValue * ToolMult(ToolHoe);
            float damageStat = st.Get(StatId.MeleeDamage);
            float atkMult = st.Get(StatId.AtkMult);
            float critRate = st.Get(StatId.CritRate);
            float critDmg = st.Get(StatId.CritDmg);
            WeaponSystem.ApplyToAttack(pm, ref baseValue, ref damageStat, ref atkMult, ref critRate, ref critDmg);
            // ⑤ 满血增伤：要读**当前**血量 ⇒ 只能在公式外面乘（它不是一个静态属性）
            atkMult *= WeaponSystem.FullHpMult(p.Health.Hp >= p.Health.MaxHp, pm);

            bool crit = World.Rng.Chance(critRate);
            float dmg = DamageResolver.AttackFormula(baseValue, damageStat, atkMult, crit, critDmg, t.formula.minDamage);
            float cosLimit = Mathf.Cos(hoe.arc * 0.5f * Mathf.Deg2Rad);
            float knock = st.Get(StatId.Knockback) * 0.5f + pm.KnockbackFlat;    // ★ 击退也吃插件
            int hits = 0;
            for (int i = 0; i < World.Enemies.Count && hits < Mathf.Max(1, hoe.hits); i++)
            {
                EnemyActor e = World.Enemies[i];
                if (!e.Alive) continue;
                Vector2 delta = e.Pos - p.Pos;
                if (delta.magnitude > range + e.Radius) continue;
                if (Vector2.Dot(p.AimDir, delta.normalized) < cosLimit) continue;
                World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Physical,
                    RawValue = dmg,
                    Source = p,
                    Target = e,
                    Knockback = knock,
                    Crit = crit            // v87：把暴击标记传下去，弹字才会走暴击样式
                });
                PluginPostHit(e, dmg, crit, pm, p, WeaponSlot.Hoe);   // ★ v171：灼烧/寒冷/连锁/炸裂/回弹药
                hits++;
            }

            // ★攻击形式★ 剑气流
            // 条数 = max(1, SwordWave) + ProjectileCount（两者任一存在就带剑气）——
            // 这样「剑气刃」和「裂空刃」**各自单独装都有用**，一起装才是 1+1 条 ✓
            int waves = (pm.SwordWaveCount > 0 || pm.ProjectileCount > 0)
                ? Mathf.Max(1, pm.SwordWaveCount) + Mathf.Max(0, pm.ProjectileCount) : 0;
            if (waves > 0) FireSwordWaves(p, pm, waves, dmg, crit, range, knock);

            if (justHarvested > 0) SetPrompt($"收割 {justHarvested} 株成熟植物");
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

        private const int BeamAmmoCost = 10;
        private const float BeamAmmoInterval = 0.5f;   // 秒 / 次扣费
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
            float aspd = Mathf.Max(0.05f, p.AttackSpeed * (1f + ModsFor(WeaponSlot.Can).AttackSpeedPct));
            float interval = BeamAmmoInterval / aspd;

            if (!_beamFiring)
            {
                // 起手先付一次——不然第一帧白打
                if (WaterAmmo < BeamAmmoCost) { SetPrompt("水壶没水了（拾取雨滴 +20）"); return; }
                WaterAmmo -= BeamAmmoCost;
                _beamAmmoTimer = 0f;
                _beamFiring = true;
            }
            else
            {
                _beamAmmoTimer += GameClock.FixedDt;
                if (_beamAmmoTimer >= interval)
                {
                    _beamAmmoTimer -= interval;
                    if (WaterAmmo < BeamAmmoCost)
                    {
                        _beamFiring = false;      // 断了，下次按住要重新付起手费
                        SetPrompt("水壶没水了（拾取雨滴 +20）");
                        return;
                    }
                    WaterAmmo -= BeamAmmoCost;
                }
            }
            FireBeam(p);
        }

        /// <summary>松开攻击键时调用——让下一次按住重新收起手费。</summary>
        private void StopBeam()
        {
            _beamFiring = false;
            _beamAmmoTimer = 0f;
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

            float range = tool.range * st.Get(StatId.AtkRange) * (1f + pm.RangePct);
            // ★ v171：插件改的是**攻击公式的入参**
            // v162（A3）：水壶 → **远程伤害**；公式 = (基准伤害 + 远程伤害) × 伤害倍率
            float baseValue = tool.baseValue;
            float damageStat = st.Get(StatId.RangedDamage);
            float atkMult = st.Get(StatId.AtkMult);
            float critRate = st.Get(StatId.CritRate);
            float critDmg = st.Get(StatId.CritDmg);
            WeaponSystem.ApplyToAttack(pm, ref baseValue, ref damageStat, ref atkMult, ref critRate, ref critDmg);
            atkMult *= WeaponSystem.FullHpMult(p.Health.Hp >= p.Health.MaxHp, pm);

            bool beamCrit = World.Rng.Chance(critRate);
            float beamDmg = DamageResolver.AttackFormula(baseValue, damageStat, atkMult, beamCrit, critDmg, tt.formula.minDamage);
            Vector2 origin = p.Pos;
            Vector2 dir = p.AimDir.sqrMagnitude < 0.001f ? Vector2.right : p.AimDir.normalized;
            float halfWidth = tool.beamWidth > 0f ? tool.beamWidth : 1.4f;

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

            // ★攻击形式★ 激光条数 = 1 + ProjectileCount（`can_rainstorm` 暴雨阀）
            int beams = WeaponSystem.VolleyCount(1, pm);
            float knock = st.Get(StatId.Knockback) * 0.2f + pm.KnockbackFlat;
            // 攻速既缩短"同一目标的再受伤间隔"，也缩短扣费间隔（TickBeam 里读同一个式子）
            float fireRate = Mathf.Max(0.05f, p.AttackSpeed * (1f + pm.AttackSpeedPct));

            for (int b = 0; b < beams; b++)
            {
                Vector2 bdir = beams == 1
                    ? dir
                    : Rotate(dir, BeamSpreadDeg * (b - (beams - 1) * 0.5f) * Mathf.Deg2Rad);

                for (int i = 0; i < World.Enemies.Count; i++)
                {
                    EnemyActor e = World.Enemies[i];
                    if (e.IsDead) continue;
                    Vector2 rel = e.Pos - origin;
                    float along = Vector2.Dot(rel, bdir);
                    if (along < 0f || along > range) continue;
                    float perp = Mathf.Abs(rel.x * bdir.y - rel.y * bdir.x);
                    if (perp > halfWidth + e.Radius) continue;
                    // ⑦：**每个受判对象独立 CD** —— 没有这条，光束里的敌人会每帧掉一次血
                    //    它也是"多发不叠加"的保证：同一敌人被两条光束同时罩住，也只吃一次 ✓
                    if (_beamHitCd.TryGetValue(e.Id, out float cd) && cd > 0f) continue;
                    // v100：这个 CD 也吃攻速，否则"攻速提升"对激光的伤害频率没有任何作用
                    _beamHitCd[e.Id] = BeamPerTargetCd / fireRate;
                    World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical, RawValue = beamDmg, Source = p, Target = e,
                        Knockback = knock,
                        Crit = beamCrit
                    });
                    PluginPostHit(e, beamDmg, beamCrit, pm, p, WeaponSlot.Can);   // ★ v171
                }

                // 一条完整射线（不是逐敌发）——v44 的结论，别退回去
                Bus.Publish(new BeamEvent { From = origin, To = origin + bdir * range, HalfWidth = halfWidth });
            }

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
        }

        private void UseTool(PlayerActor p, ToolDef tool, bool ailment)
        {
            int slot = ailment ? PlayerActor.WeaponFertilizer : PlayerActor.WeaponCan;
            WeaponModifiers pm = ModsFor(ailment ? WeaponSlot.Fertilizer : WeaponSlot.Can);
            // 攻速吃插件（速燃引信 / 涡流叶轮）
            float fireRate = Mathf.Max(0.05f, p.AttackSpeed * (1f + pm.AttackSpeedPct));

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
            FertAmmo--;

            p.WeaponCds[slot] = tool.cooldown / fireRate;

            // ── v40：肥料 = 在鼠标对应位置产生一次爆炸（不再飞行）──
            // v46：落点**必须被投掷射程钳制**——射程 = tools.fertilizer.range × 攻击范围属性。
            //      旧写法把"钳制前的向量"缓存下来又复用，判定顺序含混；现在收敛到 CurrentBlastCenter 一处。
            // v91：不再立即爆炸，改为**先亮 0.5 秒区域标记再炸**（需求"增加爆炸前摇"）。
            // v171：**爆炸点 +N** 在 ArmBlast 里展开 —— 它同时决定前摇标记的个数，
            //       所以"标记"与"真炸"必然是同一组落点 ✓
            ArmBlast(CurrentBlastCenter(), p);
        }

        // ──────────────────────────────────────────── v91：肥料爆炸前摇
        private const float BlastWindup = 0.5f;

        private struct PendingBlast { public Vector2 Center; public float T; public int SourceId; }
        private readonly System.Collections.Generic.List<PendingBlast> _pendingBlasts =
            new System.Collections.Generic.List<PendingBlast>();

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
        /// 按当前属性 + **插件**（`fert_pressure` 增压药罐）算爆炸半径。
        /// 前摇标记与真正的爆炸必须用**同一个值**，否则标记是骗人的 ✗
        /// ⇒ 插件的 `ExplodeRadius` 在这里加一次，两处自然一起变 ✓
        /// </summary>
        private float BlastRadiusNow() => BlastBaseRadius(T.tools.fertilizer.baseExplodeRange)
                                          * Progress.Stats.Get(StatId.ExplodeRange)
                                          * (1f + ModsFor(WeaponSlot.Fertilizer).ExplodeRadiusPct);

        /// <summary>
        /// ★攻击形式拓展★ 第 <paramref name="i"/> 个爆炸点的落点。
        /// i=0 就是主落点；其余**均匀分布**在半径 <see cref="BlastSpotRingRadius"/> 的环上 ✓
        /// 抽成静态函数是为了让"爆炸点 +N 真的落在不同位置"这条可以被自检直接断言 ✓
        /// </summary>
        public static Vector2 BlastSpotCenter(Vector2 center, int i, int spots)
        {
            if (i <= 0 || spots <= 1) return center;
            float ang = Mathf.PI * 2f * (i - 1) / Mathf.Max(1, spots - 1);
            return center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * BlastSpotRingRadius;
        }

        /// <summary>
        /// 布置一次延迟爆炸：亮出区域标记，0.5 秒后引爆。
        /// ★ v171：**爆炸点 +N** 在这里展开 ★
        /// 一次投掷布置 `1 + ProjectileCount` 个落点（`fert_split` 分裂弹丸）✓
        /// 标记与真炸走同一个 `spots` 循环 ⇒ 不可能出现"标记 1 个、真炸 2 个"✓
        /// </summary>
        private void ArmBlast(Vector2 center, PlayerActor source)
        {
            int spots = WeaponSystem.VolleyCount(1, ModsFor(WeaponSlot.Fertilizer));
            for (int i = 0; i < spots; i++)
            {
                Vector2 c = BlastSpotCenter(center, i, spots);
                _pendingBlasts.Add(new PendingBlast { Center = c, T = BlastWindup, SourceId = source.Id });
                Bus.Publish(new FertilizerTelegraphEvent
                {
                    Pos = c, Radius = BlastRadiusNow(), Duration = BlastWindup
                });
            }
        }

        /// <summary>每固定帧推进延迟爆炸。放在逻辑层，保证确定性重放一致。</summary>
        private void TickPendingBlasts(float dt)
        {
            for (int i = _pendingBlasts.Count - 1; i >= 0; i--)
            {
                PendingBlast b = _pendingBlasts[i];
                b.T -= dt;
                if (b.T > 0f) { _pendingBlasts[i] = b; continue; }
                _pendingBlasts.RemoveAt(i);
                // 来源恒为玩家（肥料只有玩家能扔）；这里不查 SourceId，
                // 因为前摇期间跨回合时玩家对象会被重建，按 id 找反而可能找不到。
                ExplodeAt(b.Center, World.Player);
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
        /// 肥料投掷射程 = `tools.fertilizer.range` × 攻击范围属性 × **插件范围** ✓
        /// ★ v171：抽成一个函数，是因为落点、准星变色、射程判定**三处必须一致** ✗
        /// 三处各写一遍的话，"装上加长类插件后准星说超程、实际却打得到"这种偏差必然出现 ✓
        /// </summary>
        private float FertilizerThrowReach()
            => T.tools.fertilizer.range * Progress.Stats.Get(StatId.AtkRange)
               * (1f + ModsFor(WeaponSlot.Fertilizer).RangePct);

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
        /// v171：**插件接进来** —— 伤害（`ApplyToAttack`）、半径（`ExplodeRadius`）、
        /// 击退（`Knockback`）、满血增伤（`FullHpBonus`）、命中机制（灼烧/寒冷/连锁/回弹药）
        /// 与**爆炸回声**（`BlastEcho`）全部在这一处生效 ✓
        /// （**爆炸点 +N** 不在这里：它在 `ArmBlast`，因为一个落点就是一次 `ExplodeAt` 调用 ✓）
        /// </summary>
        public int ExplodeAt(Vector2 center, PlayerActor source)
        {
            StatBlock st = Progress.Stats;
            WeaponModifiers pm = ModsFor(WeaponSlot.Fertilizer);      // ★ v171：插件修正
            // v109：爆炸伤害 = **肥料这件武器走一遍普通攻击公式** 的结果 × 爆炸倍率。
            // 顺序不能反：先算普通，再乘爆炸。这样"堆伤害属性/伤害倍率"对爆炸同样生效，
            // 两套成长自动同步——旧版那条独立公式只吃攻击伤害，基础伤害都被忽略。
            // v162（A3）：肥料 → **爆炸伤害**；公式 = (基准伤害 + 爆炸伤害) × 伤害倍率
            ToolDef fertTool = T.tools.fertilizer;
            float baseValue = fertTool.baseValue * ToolMult(PlayerActor.WeaponFertilizer);
            float damageStat = st.Get(StatId.BlastDamage);
            float atkMult = st.Get(StatId.AtkMult);
            // ★ 爆炸**不掷暴击**（v171 保持原样：原来这里写死 `false, 1f`）★
            // ⇒ 「暴击炸裂 CritBlast」这一类插件只挂在锄头这一条路上，不放肥料表 ✓
            //    （数据侧也由自检钉住：`CritBlast` 的 allowedWeapons 不允许出现肥料）
            float critRateUnused = 0f, critDmgUnused = 1f;
            WeaponSystem.ApplyToAttack(pm, ref baseValue, ref damageStat, ref atkMult,
                                       ref critRateUnused, ref critDmgUnused);
            // ⑤ 满血增伤（`fert_fullcharge` 满仓药罐）
            atkMult *= WeaponSystem.FullHpMult(source != null && source.Health.Hp >= source.Health.MaxHp, pm);
            float fertNormal = DamageResolver.AttackFormula(baseValue, damageStat, atkMult, false, 1f, 0f);
            float explodeDmg = DamageResolver.ExplodeFormula(fertNormal, st.Get(StatId.ExplodeDamage));
            // v111：爆炸基础半径改为读**这件武器自己的**整数值（需求："每个武器有自己的基础爆炸范围基础值"）。
            // 缺省 3 米，所以老数据不改也能跑；要调某件武器的爆炸体量只需填 baseExplodeRange。
            float blastRadius = BlastBaseRadius(fertTool.baseExplodeRange) * st.Get(StatId.ExplodeRange)
                                * (1f + pm.ExplodeRadiusPct);
            float knock = st.Get(StatId.Knockback) + pm.KnockbackFlat;   // ★ 击退吃插件（肥料全额）
            int hit = 0;
            for (int i = 0; i < World.Enemies.Count; i++)
            {
                EnemyActor e = World.Enemies[i];
                if (e == null || e.IsDead) continue;
                if (Vector2.Distance(e.Pos, center) > blastRadius) continue;
                World.DealDamage(new DamageRequest
                {
                    Type = DamageType.Ailment, RawValue = explodeDmg, Source = source, Target = e,
                    Knockback = knock
                });
                PluginPostHit(e, explodeDmg, false, pm, source, WeaponSlot.Fertilizer);   // ★ v171
                hit++;
            }

            // ◆大机制◆ 爆炸回声（`fert_echocan`）：第一次炸完**原地再结算一次** ✓
            // ★ 回声**只补伤害，不重复触发机制** ★
            //   若回声也走 `PluginPostHit`，一件插件就会变成两件（灼烧量翻倍、连锁翻倍）
            //   —— 那不是"回声"的语义，而且没人能从表里看出来 ✓
            float echo = WeaponSystem.BlastEchoDamage(explodeDmg, pm);
            int echoHit = 0;
            if (echo > 0f)
            {
                for (int i = 0; i < World.Enemies.Count; i++)
                {
                    EnemyActor e = World.Enemies[i];
                    if (e == null || e.IsDead) continue;
                    if (Vector2.Distance(e.Pos, center) > blastRadius) continue;
                    World.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Ailment, RawValue = echo, Source = source, Target = e, Knockback = 0f
                    });
                    echoHit++;
                }
            }

            Bus.Publish(new EnemyAttackFieldEvent { Pos = center, Radius = blastRadius });
            // v47：肥料不再只"催熟"——直接催熟 + 收割（呼叫植物帮手），玩家不需要再补一锄头。
            //      逻辑直接作用于本世界；事件只留给表现层（WorldView 画爆炸环）。
            int matured = World.MatureAndHarvestInRadius(center, blastRadius);
            Bus.Publish(new FertilizerBlastEvent { Pos = center, Radius = blastRadius });
            if (matured > 0) SetPrompt($"肥料催熟并收获 {matured} 株植物 → 金币与经验");   // v166：原写"帮手已参战"，而已无帮手
            Telemetry.Emit("fertilizer_blast", ("hit", hit), ("radius", blastRadius),
                ("dmg", explodeDmg), ("harvested", matured), ("echoHit", echoHit));
            return hit;
        }
    }
}
