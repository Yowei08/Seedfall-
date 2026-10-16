using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// **Boss 刷新时间表**（计时制）。
    ///
    /// 需求：初始 10 分钟刷一次，每次刷新后下次减 2 分钟 —— 10, 8, 6, 4, 2，最低 2 分钟。
    ///
    /// ★ 关键是"从【刷新】起重新计时，而不是从【死亡】起" ★
    ///
    /// 这个区别看起来很小，但它决定了玩家能不能"拖 Boss"：
    ///   从【死亡】起计时 → 玩家只要拖着不打，下一个 Boss 就永远不来 ✗ 可以被滥用
    ///   从【刷新】起计时 → **Boss 还活着时倒计时已经在走** ✓ 拖不掉
    ///
    /// 所以 <see cref="OnSpawned"/> 记录的是"下一次的绝对时刻"，
    /// 而 <see cref="SecondsUntilNext"/> 是从"现在"算 —— 与 Boss 的生死无关 ✓
    ///
    /// 由此产生一个**必须处理的边界**：Boss 还活着时下一个就到点了。
    /// 见 <see cref="Due"/> 的说明。
    /// </summary>
    public sealed class BossSchedule
    {
        /// <summary>间隔序列（分钟）：10 → 8 → 6 → 4 → 2，之后恒为 2。</summary>
        public static readonly float[] GapsMinutes = { 10f, 8f, 6f, 4f, 2f };

        /// <summary>最低间隔（分钟）。序列走完之后恒为它。</summary>
        public const float MinGapMinutes = 2f;

        private float _nextAt;          // 下一次刷新的**绝对** GameTime
        private int _spawnedCount;
        private bool _armed;

        /// <summary>已经刷过几次（0 = 一次都还没）。</summary>
        public int SpawnedCount => _spawnedCount;

        /// <summary>
        /// 下一次间隔（分钟）—— **刷新次数 → 之后的间隔**。
        ///
        /// 注意入参语义：传的是**已经刷过几次**（自增后的值），不是"第几次之前"。
        ///   0 → 还没刷过 → 10（这是**开局到首次**的等待，见 <see cref="Reset"/>）
        ///   1 → 刷过 1 次 → 8
        ///   2 → 6   3 → 4   4 → 2   5 及以上 → 2
        /// </summary>
        public static float GapMinutesOf(int spawnedCount)
        {
            if (spawnedCount <= 0) return GapsMinutes[0];
            return spawnedCount < GapsMinutes.Length ? GapsMinutes[spawnedCount] : MinGapMinutes;
        }

        /// <summary>开局：第一次 Boss 在 10 分钟后。</summary>
        public void Reset(Clock clock)
        {
            _spawnedCount = 0;
            _nextAt = clock.GameTime + GapsMinutes[0] * Clock.SecondsPerMinute;
            _armed = true;
        }

        /// <summary>
        /// Boss **刷新的那一刻**调用（不是死亡时）。
        /// 它会按"第几次刷新"取下一个间隔，并把倒计时重新锚定在**当前时刻** ✓
        ///
        /// ★ 先自增、再查表 ★（这里我第一版写反了，算序列时才发现）
        /// 序列 10,8,6,4,2 里的 **10 是"首次的初始等待"**，不是"第 1 次刷新后的间隔" ✗
        /// 所以：
        ///   第 1 次刷新（计数 0→1）→ 间隔 = Gaps[1] = 8   → 第 2 次在 18 分钟 ✓
        ///   第 2 次刷新（1→2）  → 6  → 24
        ///   第 3 次（2→3）      → 4  → 28
        ///   第 4 次（3→4）      → 2  → 30
        ///   之后恒为 2
        /// 若在自增【前】查表，第 1 次会取到 10 → 第 2 次在 20 分钟 ✗ 整条表都错位一格。
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
        /// 到点但**上一个 Boss 还活着**时怎么办。
        ///
        /// 架构文档里这是一条待决策项，这里采用**顺延**（默认）：
        /// 从"当前 Boss 死亡的那一刻"再等一个完整间隔。
        ///
        /// 为什么不选择"允许并存"：并存会叠出多个 Boss，
        /// 而 Boss 有 90 秒限时与冲撞机制，两个同时在场基本等于无法处理 ✗
        ///
        /// 顺延的代价是"玩家的拖延实际上延后了下一个" —— 但这比并存安全得多，
        /// 而且 <see cref="OnSpawned"/> 的锚定方式已经保证了"只要不拖延就按表走" ✓
        /// </summary>
        public void DeferAfter(Clock clock)
        {
            float gap = GapMinutesOf(_spawnedCount);   // 已经计过数，这里取的是"下一次之后"的间隔
            _nextAt = clock.GameTime + gap * Clock.SecondsPerMinute;
        }

        /// <summary>下一次刷新的绝对时刻（调试/UI 用）。</summary>
        public float NextAt => _nextAt;
    }
}
