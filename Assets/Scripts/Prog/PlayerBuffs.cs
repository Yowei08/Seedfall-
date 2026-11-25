using System;
using System.Collections.Generic;

namespace Seedfall
{
    /// <summary>
    /// ★★ v197：**一条临时增益**（玩家身上的"叠层 + 倒计时"）★★
    ///
    /// ── 它是什么 ──
    /// 一个**纯数据**快照：`Id` 认身份、`Label` 给界面印字、`Stacks` 是层数、
    /// `Remain` 是还剩几秒、`IconMaskIdx` 指向 `TableDatabase.ItemIconMasks` 的下标。
    ///
    /// ── 为什么是 `struct` 而不是 `class` ★
    /// `Active` 会被 UI **每帧遍历**（另一路的增益栏）。`class` 会让每次读都过一遍堆对象，
    /// 而层数/剩余时间这两项本来就每帧在变 ⇒ 每帧改同一批对象等于每帧制造一次""
    /// 谁在持有引用"的歧义；`struct` 是**值快照** ⇒ 读的人拿到的是那一刻的真相 ✓
    /// （`PlayerBuffs` 内部的 `List<BuffInstance>` 只增删、不改字段，见 `Tick` 的写法）
    ///
    /// ── 为什么这几个字段是**字段**而不是属性 ★
    /// 本工程的枚举量/快照结构（`DamageResult` / `StatModifier` / `MouseState`）一律是
    /// public readonly 字段或 public 字段，UI 直接读 ⇒ 这里保持一致，
    /// 免得另一边写出 `.Label` 与 `.label` 两种写法 ✓
    /// </summary>
    public struct BuffInstance
    {
        /// <summary>身份（同一 Id 只会有一条，累层不重开）。</summary>
        public string Id;
        /// <summary>界面印的名字（中文，短）。</summary>
        public string Label;
        /// <summary>当前层数（≥ 1）。</summary>
        public int Stacks;
        /// <summary>剩余秒数（≤ 0 的那一帧就会被移除）。</summary>
        public float Remain;
        /// <summary>
        /// `TableDatabase.ItemIconMasks` 的**下标**（由 `PlayerBuffs.IconIndexOf` 解析）。
        ///
        /// ⚠️ 它**不是**道具 id：增益栏要画的是"一个 11×11 图标"，
        /// 而图标表的键是道具 id ⇒ 用下标可以让 UI 不必再认识任何道具 ✓
        /// `-1` = 这张增益没有指定图标（UI 自己兜底成品质色块）。
        /// </summary>
        public int IconMaskIdx;
    }

    /// <summary>
    /// ★★ v197：**玩家身上全部临时增益的唯一持有者** ★★
    ///
    /// ── 谁往里写 ──
    ///   ① `RunSession.HarvestPlot` —— 收割"带增益机制的植物"时叠一层（机制 1 / 2）
    ///   ② `ProgMechanics` —— 机制道具的时变增益（攻速/伤害倍率爬升）
    ///   ③ 以后的任何"限时状态"都往这里写，**不要再开第二份倒计时表** ✓
    ///
    /// ── 谁往里读 ──
    ///   ① `RunSession.SyncBuffModifiers` —— 把层数换算成 `StatBlock` 的乘法修正
    ///   ② 增益 UI（**另一路**）—— 只读 `Active`，字段就是 `BuffInstance` 那五个 ✓
    ///
    /// ── 判据（本轮需求原文，一条都不许走样）──
    ///   · `refresh == true`  ⇒ **只刷新时长**，层数 **+1**（封顶 `MaxStacks`）
    ///   · `refresh == false` ⇒ **不累层**（只按需要延长时长）
    ///   · `Tick(dt)` 到期**逐条移除**（不是整表清空）
    ///
    /// ── 为什么不放在 `RunSession` 里当三个字段 ★
    /// 增益会越来越多（植物 2 条 + 道具 2 条，后面还会加），
    /// 而"层数 / 剩余 / 标签 / 图标"四样必须**同时**正确 ⇒ 散成字段一定会出现
    /// "层数清了、时间没清"这种半衰状态 ✓ 这里用一张表 + 一个 `Changed` 事件消掉它。
    /// </summary>
    public sealed class PlayerBuffs
    {
        /// <summary>
        /// 单条增益的**层数上限**（默认 12 —— 用户需求："最多 12 层"）。
        /// 特例在 `MaxStacksOf` 里按 id 覆盖，不改这个默认值 ✓
        /// </summary>
        public const int DefaultMaxStacks = 12;

        private readonly List<BuffInstance> _active = new List<BuffInstance>(8);

        /// <summary>表变了（增 / 改 / 删）就发一次。消费者据此重算 `StatBlock` 或重绘。</summary>
        public event Action Changed;

        /// <summary>当前全部增益（**只读视图**；每次读都不分配）。</summary>
        public IReadOnlyList<BuffInstance> Active => _active;

        public int Count => _active.Count;

        /// <summary>
        /// 某个 id 的层数上限。默认 `DefaultMaxStacks`（12）。
        /// ★ 为什么按 id 覆盖而不是给 `Apply` 加参数 ★
        /// 契约里 `Apply` 的签名是固定的（另一路按它写 UI）⇒ 上限必须由**身份**决定，
        /// 不能由调用点临时决定（否则同一个 id 在两处能叠出不同上限）✓
        /// </summary>
        public static int MaxStacksOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 1;
            switch (id)
            {
                case BuffIds.PlantAtkMult: return 12;      // 用户原话"最多 12 层"
                case BuffIds.PlantAtkSpeed: return 12;
                case BuffIds.ItemAtkSpeedRamp: return 60;  // 每秒 1 层，封顶 60 层（= +120%）
                case BuffIds.ItemDamageRamp: return 60;
                default: return DefaultMaxStacks;
            }
        }

        /// <summary>
        /// ★★ 契约方法 ★★ 叠一层（或新建）一条增益。
        /// </summary>
        /// <param name="id">身份。同一个 id 只会有一条记录。</param>
        /// <param name="label">界面印的字（空 = 保留原标签）。</param>
        /// <param name="stacks">本次"新建"时的初始层数（≥1；`refresh` 为真时不使用）。</param>
        /// <param name="duration">本次时长（秒）。已存在时取 `max(旧剩余, 本次)` ⇒ **不会缩短**。</param>
        /// <param name="refresh">
        /// 真 = 这条增益"又被触发了一次"⇒ 时长刷新 **且** 层数 +1（封顶）；
        /// 假 = 只是时长续上，**层数不动** ✓
        /// </param>
        public void Apply(string id, string label, int stacks, float duration, bool refresh)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (duration <= 0f) return;                    // 0 秒 = 一条立刻消失的增益，没有意义
            int max = MaxStacksOf(id);

            for (int i = 0; i < _active.Count; i++)
            {
                BuffInstance b = _active[i];
                if (!string.Equals(b.Id, id, StringComparison.Ordinal)) continue;

                // ── 已存在：只动"时长"与（refresh 时的）"层数" ──
                if (!string.IsNullOrEmpty(label)) b.Label = label;
                if (b.Remain < duration) b.Remain = duration;
                if (refresh) b.Stacks = Math.Min(max, b.Stacks + 1);
                if (b.IconMaskIdx < 0) b.IconMaskIdx = IconIndexOf(IconItemOf(id));
                _active[i] = b;
                Changed?.Invoke();
                return;
            }

            // ── 新建 ──
            int init = refresh ? 1 : Math.Max(1, stacks);
            _active.Add(new BuffInstance
            {
                Id = id,
                Label = string.IsNullOrEmpty(label) ? id : label,
                Stacks = Math.Min(max, init),
                Remain = duration,
                IconMaskIdx = IconIndexOf(IconItemOf(id)),
            });
            Changed?.Invoke();
        }

        /// <summary>
        /// 每条增益推进 `dt` 秒；`Remain` 归零的**逐条移除**，并抛一次 `Changed`。
        /// 逐条移除（而不是"发现有过期的就整表重建"）是刻意的：
        /// 整表重建会让同一帧里所有增益的 `Remain` 都重新发一遍 `Changed` ⇒ UI 每帧重绘 ✓
        /// </summary>
        public void Tick(float dt)
        {
            if (_active.Count == 0 || dt <= 0f) return;
            bool changed = false;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                BuffInstance b = _active[i];
                b.Remain -= dt;
                if (b.Remain <= 0f)
                {
                    _active.RemoveAt(i);
                    changed = true;
                }
                else
                {
                    _active[i] = b;
                    changed = true;     // 剩余时间变了 ⇒ 界面要重画进度条
                }
            }
            if (changed) Changed?.Invoke();
        }

        /// <summary>清空（新一局 / 测试夹具用）。</summary>
        public void Clear()
        {
            if (_active.Count == 0) return;
            _active.Clear();
            Changed?.Invoke();
        }

        /// <summary>某条增益现在是几层（没有 = 0）。**不分配**。</summary>
        public int StacksOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            for (int i = 0; i < _active.Count; i++)
                if (string.Equals(_active[i].Id, id, StringComparison.Ordinal)) return _active[i].Stacks;
            return 0;
        }

        /// <summary>某条增益还剩几秒（没有 = 0）。</summary>
        public float RemainOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0f;
            for (int i = 0; i < _active.Count; i++)
                if (string.Equals(_active[i].Id, id, StringComparison.Ordinal)) return _active[i].Remain;
            return 0f;
        }

        public bool TryGet(string id, out BuffInstance buff)
        {
            for (int i = 0; i < _active.Count; i++)
                if (string.Equals(_active[i].Id, id, StringComparison.Ordinal)) { buff = _active[i]; return true; }
            buff = default(BuffInstance);
            return false;
        }

        // ────────────────────────────────────────────────────────────────
        // 图标：`ItemIconMasks` 的下标
        //
        // ★ 为什么增益要"借"一张道具掩码 ★
        //   增益图标与道具图标是**同一套 11×11 位掩码**（`TableDatabase.ItemIconMasks`，
        //   已烘进色调、已过 `ITEM-ICON-MASK/UNIQUE` 两条自检）。
        //   另起一套"增益图标"等于再造 62 张没人验的图 ⇒ 借现有掩码是唯一不增债的做法 ✓
        // ★ 为什么要**下标**而不是 id ★
        //   需求给的字段是 `IconMaskIdx`（下标），UI 拿它直接去 `ItemIconMasks` 的**枚举顺序**
        //   取第 n 张 —— 所以这里必须用**与 UI 完全同一条**枚举顺序解析，
        //   否则"界面画的图标与这里算的数不一致"，而那种错不会报错 ✓
        // ────────────────────────────────────────────────────────────────

        private static string[] _iconKeys;

        /// <summary>道具 id → 在 `ItemIconMasks` 枚举顺序里的下标（找不到 = −1）。</summary>
        public static int IconIndexOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;
            if (_iconKeys == null) BuildIconKeys();
            for (int i = 0; i < _iconKeys.Length; i++)
                if (string.Equals(_iconKeys[i], itemId, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static void BuildIconKeys()
        {
            // ★ 顺序 = `Dictionary` 的枚举顺序 = 表里的**书写顺序**（C# 的 Dictionary 保序）★
            //   这个前提必须只有一处依赖：UI 也走 `ItemIconMasks` 的枚举，两边同源 ✓
            var keys = new List<string>(TableDatabase.ItemIconMasks.Count);
            foreach (KeyValuePair<string, string[]> kv in TableDatabase.ItemIconMasks) keys.Add(kv.Key);
            _iconKeys = keys.ToArray();
        }

        /// <summary>自检用：把缓存的键表丢掉（表在运行期被重建时）。</summary>
        public static void ResetIconCache() { _iconKeys = null; }

        /// <summary>
        /// 每条增益默认借哪张道具掩码 —— 挑的是**语义最近**的那件现有道具（都是表里真有的 id）：
        ///   攻击倍率增益 ⇒ `berserk_mask`（狂战面 · 伤害倍率 +20%）
        ///   攻击速度增益 ⇒ `ferment_jar`（发酵罐 · 攻击速度 +15%）
        ///   攻速爬升     ⇒ `frenzy_band`（狂热带 · 攻击速度 +18%）
        ///   伤害爬升     ⇒ `overclock_core`（超载核心 · 攻击倍率 +18%）
        /// ★ 一个字节都没新增图标：这四条只是**指向已有掩码的下标** ⇒ 62 张的总数与唯一性不受影响 ✓
        /// </summary>
        private static string IconItemOf(string id)
        {
            switch (id)
            {
                case BuffIds.PlantAtkMult: return "berserk_mask";
                case BuffIds.PlantAtkSpeed: return "ferment_jar";
                case BuffIds.ItemAtkSpeedRamp: return "frenzy_band";
                case BuffIds.ItemDamageRamp: return "overclock_core";
                default: return "";
            }
        }
    }

    /// <summary>
    /// 增益的**身份常量**。集中在这儿，因为写侧（植物机制 / 道具机制）与读侧（属性同步 / UI）
    /// 必须用同一批字面量 —— 各写一份字符串就会出现"写的是 atkMult、读的是 AtkMult"这种
    /// **不报错、只是永远叠不上**的错 ✓
    /// </summary>
    public static class BuffIds
    {
        /// <summary>植物机制 1：收割 ⇒ 攻击倍率 +3%/层（12 层封顶，3 秒，刷层刷新时长）。</summary>
        public const string PlantAtkMult = "plant_atk_mult";
        /// <summary>植物机制 2：收割 ⇒ 攻击速度 +3%/层（同上）。</summary>
        public const string PlantAtkSpeed = "plant_atk_speed";
        /// <summary>道具：攻速每秒 +2%（受伤重置）—— 层数 = 已爬升的秒数。</summary>
        public const string ItemAtkSpeedRamp = "item_atk_speed_ramp";
        /// <summary>道具：伤害倍率每秒 +2%（受伤重置）。</summary>
        public const string ItemDamageRamp = "item_damage_ramp";
    }
}
