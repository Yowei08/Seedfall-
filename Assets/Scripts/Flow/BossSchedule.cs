using UnityEngine;

namespace Seedfall
{
    /// <summary>**Boss 种类**（v201：不再是"只有一只 BOSS"）。</summary>
    public enum BossKind
    {
        /// <summary>旧版 BOSS「墓园守望者」（`enemies.json` 的 `boss`）。</summary>
        Old = 0,
        /// <summary>新版 BOSS「飞蛾」（`enemies.json` 的 `moth`，三阶段：毛毛虫 / 蛹 / 飞蛾）。</summary>
        Moth = 1,
    }

    /// <summary>
    /// **Boss 刷新时间表**（计时制）。
    ///
    /// ── v201 改版（用户原话：「10分钟刷新旧版boss，20分钟刷新飞蛾boss，后续boss为随机」）──
    ///   第 10 分钟 → **旧版 BOSS**
    ///   第 20 分钟 → **飞蛾 BOSS**      （⇒ 第 2 次的间隔仍然是 10 分钟）
    ///   第 20 分钟之后 → **间隔与种类都随机**（间隔 8~14 分钟，步长 0.5 分钟）
    ///
    /// ★ 为什么"随机"必须用**派生哈希**，不许吃 `World.Rng` ★
    /// `World.Rng` 是**唯一的逻辑随机源**（见 `Pcg32` 的类注释）：刷怪位置、战利品、暴击
    /// 全都从同一条流里取。只要 BOSS 时刻表去喝一口，**之后每一个既有随机读数都会整体漂一位** ✗
    /// 而工程里已经有一批把种子钉死的自检与探针（`DeterminismCheck` / `RunSimulator`）——
    /// 它们会一起变红，而变得**不是"新功能错了"，是"我悄悄改了全局随机序列"** ✗
    /// ⇒ 这里用 `DerivedHash(index)`：一个**只由刷新序号决定**的纯函数（splitmix32 一轮），
    ///   不读、不写任何随机源 ⇒ `World.Rng` 的状态在整局里**一个字节都没动过** ✓
    ///   证据：自检 `BossSchedule 不消费 World.Rng（派生哈希）` 与探针 `ProbeV201Moth.Schedule()`。
    ///
    /// ── 保留的旧语义（一条都没改）──
    /// ★ 关键是"从【刷新】起重新计时，而不是从【死亡】起" ★
    ///   从【死亡】起计时 → 玩家只要拖着不打，下一个 Boss 就永远不来 ✗ 可以被滥用
    ///   从【刷新】起计时 → **Boss 还活着时倒计时已经在走** ✓ 拖不掉
    /// 所以 <see cref="OnSpawned"/> 记录的是"下一次的绝对时刻"，
    /// 而 <see cref="SecondsUntilNext"/> 是从"现在"算 —— 与 Boss 的生死无关 ✓
    /// </summary>
    public sealed class BossSchedule
    {
        /// <summary>第 1 次 BOSS（旧版）的时刻（分钟）。</summary>
        public const float FirstBossMinute = 10f;

        /// <summary>第 2 次 BOSS（飞蛾）的时刻（分钟）。⇒ 两次之间的间隔同样是 10 分钟。</summary>
        public const float SecondBossMinute = 20f;

        /// <summary>第 2 次之后随机间隔的下界（分钟）。</summary>
        public const float RandomGapMinMinutes = 8f;

        /// <summary>第 2 次之后随机间隔的上界（分钟）。</summary>
        public const float RandomGapMaxMinutes = 14f;

        /// <summary>随机间隔的步长（分钟）—— 取 0.5 是为了让时刻表落在"半分钟"网格上，便于读数。</summary>
        public const float RandomGapStepMinutes = 0.5f;

        /// <summary>
        /// 间隔序列（分钟）：前两次固定 10 → 10（第 10 分钟旧版、第 20 分钟飞蛾）。
        /// ★ 这个数组仍然**只表达"前两次"**；第 3 次起由 <see cref="GapMinutesOf"/> 走派生哈希 ✓
        /// </summary>
        public static readonly float[] GapsMinutes = { 10f, 10f };

        /// <summary>随机段的最小间隔（分钟）。★ 语义变了：v200 前它是"序列走完后的恒定值 2"，现在是随机段下界 ✓</summary>
        public const float MinGapMinutes = RandomGapMinMinutes;

        private float _nextAt;          // 下一次刷新的**绝对** GameTime
        private int _spawnedCount;
        private bool _armed;

        /// <summary>已经刷过几次（0 = 一次都还没）。</summary>
        public int SpawnedCount => _spawnedCount;

        // ══════════════════════════════════════════════════════════════════
        // 一、派生哈希（唯一随机源 = 刷新序号；**不碰任何 Pcg32 流**）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>哈希的固定盐（"SEEDFALL" 的十六进制戏法，纯装饰：它只影响序列，不影响正确性）。</summary>
        public const uint HashSalt = 0x5EEDFA11u;

        /// <summary>
        /// **派生哈希**：`index` → 一个 32 位伪随机数。splitmix32 的一轮（位混合 3 步）。
        ///
        /// 三个必须成立的性质：
        ///   ① **纯函数**：同一个 index 永远同一个数 ⇒ 时刻表可重放 ✓
        ///   ② **不读全局状态**：不碰 `World.Rng` / `UnityEngine.Random` / `Time` ✓
        ///   ③ **相邻 index 不相关**：混洗后 `h(1)` 与 `h(2)` 的低位无关 ⇒ 种类不会变成"交替" ✓
        /// </summary>
        public static uint DerivedHash(int index)
        {
            uint x = (uint)index * 0x9E3779B9u + HashSalt;
            x ^= x >> 16; x *= 0x21F0AAADu;
            x ^= x >> 15; x *= 0x735A2D97u;
            x ^= x >> 15;
            return x;
        }

        /// <summary>
        /// 第 `spawnedCount` 次刷新（**1 起**）出的是哪种 BOSS。
        ///   1 → 旧版（第 10 分钟）
        ///   2 → 飞蛾（第 20 分钟）
        ///   ≥3 → 派生哈希决定（两种各 50%）
        /// </summary>
        public static BossKind KindOf(int spawnedCount)
        {
            if (spawnedCount <= 1) return BossKind.Old;
            if (spawnedCount == 2) return BossKind.Moth;
            return (DerivedHash(spawnedCount) & 1u) != 0u ? BossKind.Moth : BossKind.Old;
        }

        /// <summary>第 `spawnedCount` 次刷新要刷的 **`enemies.json` id**（`boss` / `moth`）。</summary>
        public static string IdOf(int spawnedCount)
            => KindOf(spawnedCount) == BossKind.Moth ? "moth" : "boss";

        /// <summary>
        /// 下一次间隔（分钟）—— **刷新次数 → 之后的间隔**。
        ///
        /// 入参语义与旧版一致：传的是**已经刷过几次**（自增后的值），不是"第几次之前"。
        ///   0 → 还没刷过 → 10（开局到首次的等待，见 `Reset`）
        ///   1 → 刷过 1 次 → 10 ⇒ 第 20 分钟（飞蛾）
        ///   2 及以上 → **随机** 8~14 分钟（步长 0.5），由 `DerivedHash` 决定 ✓
        /// </summary>
        public static float GapMinutesOf(int spawnedCount)
        {
            if (spawnedCount <= 0) return FirstBossMinute;
            if (spawnedCount < GapsMinutes.Length) return GapsMinutes[spawnedCount];
            return RandomGapMinutes(spawnedCount);
        }

        /// <summary>随机段的间隔（分钟）：`8 + (h % 13) × 0.5` ⇒ 8.0 ~ 14.0 共 13 档。</summary>
        public static float RandomGapMinutes(int spawnedCount)
        {
            int steps = Mathf.RoundToInt((RandomGapMaxMinutes - RandomGapMinMinutes) / RandomGapStepMinutes); // 12
            // ★ 与 `KindOf` 用不同的偏移（+977）：否则"种类"与"间隔"会从同一个哈希位出，
            //   实测那样会退化成"飞蛾总配短间隔"这种可被玩家读出来的规律 ✗
            uint h = DerivedHash(spawnedCount + 977);
            int slot = (int)(h % (uint)(steps + 1));                 // 0..12
            return RandomGapMinMinutes + slot * RandomGapStepMinutes;
        }

        // ══════════════════════════════════════════════════════════════════
        // 二、时刻表（与 v154 逐条同款，只有"间隔从哪来"换了算法）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>开局：第一次 Boss 在 10 分钟后。</summary>
        public void Reset(Clock clock)
        {
            _spawnedCount = 0;
            _nextAt = clock.GameTime + FirstBossMinute * Clock.SecondsPerMinute;
            _armed = true;
        }

        /// <summary>
        /// Boss **刷新的那一刻**调用（不是死亡时）。
        /// 它会按"第几次刷新"取下一个间隔，并把倒计时重新锚定在**当前时刻** ✓
        /// ★ 先自增、再查表 ★（序列里的 10 是"首次的初始等待"，不是"第 1 次刷新后的间隔"）
        ///   第 1 次刷新（0→1）→ 间隔 = Gaps[1] = 10 → 第 20 分钟（飞蛾）✓
        ///   第 2 次刷新（1→2）→ 随机 8~14 → 第 3 次在 28~34 分钟 ✓
        /// </summary>
        public void OnSpawned(Clock clock)
        {
            _spawnedCount++;
            float gap = GapMinutesOf(_spawnedCount);
            _nextAt = clock.GameTime + gap * Clock.SecondsPerMinute;
        }

        /// <summary>距离下一次刷新还有多少秒（与 Boss 是否存活无关）。</summary>
        public float SecondsUntilNext(Clock clock)
            => _armed ? Mathf.Max(0f, _nextAt - clock.GameTime) : 0f;

        /// <summary>到点了没有。</summary>
        public bool Due(Clock clock) => _armed && clock.GameTime >= _nextAt;

        /// <summary>
        /// 到点但**上一个 Boss 还活着**时怎么办：**顺延**（默认，与旧版一字不变）。
        /// 从"当前 Boss 死亡的那一刻"再等一个完整间隔 —— 为什么不允许并存见旧版说明
        /// （并存会叠出多个 Boss，而 Boss 有 90 秒限时与冲撞机制 ⇒ 基本无法处理 ✗）。
        /// </summary>
        public void DeferAfter(Clock clock)
        {
            float gap = GapMinutesOf(_spawnedCount);   // 已经计过数，这里取的是"下一次之后"的间隔
            _nextAt = clock.GameTime + gap * Clock.SecondsPerMinute;
        }

        /// <summary>下一次刷新的绝对时刻（调试/UI 用）。</summary>
        public float NextAt => _nextAt;

        /// <summary>
        /// 时刻表预览（分钟）：从"现在"起往后推 `count` 次刷新，给出每次的**分钟数 + 种类**。
        /// 纯读：不改本实例的任何状态（探针/自检/报告共用这一份，杜绝第二套算法）✓
        /// </summary>
        public string Preview(int count, Clock clock)
        {
            var sb = new System.Text.StringBuilder();
            float at = _armed ? _nextAt : clock.GameTime + FirstBossMinute * Clock.SecondsPerMinute;
            int idx = _spawnedCount;
            for (int i = 0; i < count; i++)
            {
                idx++;
                float minute = at / Clock.SecondsPerMinute;
                if (i > 0) sb.Append(" → ");
                sb.Append(minute.ToString("0.0")).Append('（')
                  .Append(KindOf(idx) == BossKind.Moth ? "飞蛾" : "旧版").Append('）');
                at += GapMinutesOf(idx) * Clock.SecondsPerMinute;
            }
            return sb.ToString();
        }
    }
}
