using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 训练场的**伤害统计**：每秒伤害（滑动 1 秒窗口）＋ 累计伤害 ＋ 10 秒无伤自动归零 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是**纯 C# 类**（不是 MonoBehaviour）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 统计不关心引擎回调：谁打、打多少由 `TrainingDummy.TakeDamage` 调 `AddDamage` 喂进来，
    ///     时间由 `TrainingGame.Update` 调 `Tick(dt)` 推进 ⇒ 没有 `Update` 也能算，
    ///     把 `dt` 序列灌进来就是**可复现的单元测试**（不需要 Play 模式）✓
    ///   · 训练场里**只有一份**（`TrainingGame` 持有），因此不需要跨场景保留、
    ///     不需要 `Serializable`、也不进任何战斗存档 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、DPS 的算法：**定长环形缓冲，20 格 × 50 ms = 最近 1 秒**
    /// ══════════════════════════════════════════════════════════════════════
    ///   每 50 ms 一个桶，`_head` 指向"当前正在累计的那个桶"：
    ///     ① `AddDamage(a)`：`_buckets[_head] += a`、`_windowSum += a`、`_idle = 0`、`_total += a`
    ///     ② `Tick(dt)`：`_headAge += dt`；只要 `_headAge ≥ 0.05` 就**往前滚一格**，
    ///        滚之前先把旧格的值从 `_windowSum` 里减掉、再把该格清零
    ///     ③ `Dps = _windowSum / 1.0f`
    ///   ⇒ **没有乘除遍历、没有 LINQ、没有每帧 new**：`_windowSum` 是增量维护的和，
    ///     每帧成本 = 一次加法 + 最多几次整数取模（`Tick` 里 `dt` 被 `Mathf.Min` 钳在 0.25 s，
    ///     所以 while 最多转 5 圈；真被卡成更长的帧时走"整窗清零"分支，不会转 100 圈）✓
    ///
    ///   例：t=0 打 100，则 0~1 s 内 DPS=100；到 t=1.00 s 那一格滚出窗口 ⇒ DPS 归 0。
    ///   （不是"1 秒后突然掉一半再掉一半"那种估算，是精确的 1 s 窗）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、10 秒无伤归零
    /// ══════════════════════════════════════════════════════════════════════
    ///   `_idle` 是"距上一次造成伤害的时间"，每次 `AddDamage` 清零。
    ///   `Tick` 里一旦 `_idle ≥ 10 s` ⇒ `Reset()`：`Total = 0`、整窗清零、`Dps = 0`、
    ///   连命中数与峰值一起清（用户原话"清除归 0"——留一个旧峰值只会让人以为没清干净）✓
    ///
    ///   注意：DPS 本身早在 1 秒后就已经是 0 了；10 秒这一条清的是**累计伤害**。
    ///   HUD 用 `IdleTime` / `SecondsToReset` 显示"还有几秒归零"的倒计时 ✓
    /// </summary>
    public sealed class TrainingStats
    {
        /// <summary>DPS 的统计窗口长度（秒）。</summary>
        public const float WindowSeconds = 1f;
        /// <summary>窗口内的桶数（20 × 50 ms = 1 s）。</summary>
        public const int BucketCount = 20;
        /// <summary>单桶时长 = 50 ms。</summary>
        public const float BucketSeconds = WindowSeconds / BucketCount;
        /// <summary>无伤自动归零的阈值（秒）。</summary>
        public const float ResetIdleSeconds = 10f;

        // 定长数组：整个生命周期内只有这一份，不扩容、不 new
        private readonly float[] _buckets = new float[BucketCount];
        private int _head;          // 当前正在累计的桶
        private float _headAge;     // 当前桶已经过去了多久
        private float _windowSum;   // 窗口内伤害之和（增量维护）
        private float _idle;        // 距上一次造成伤害的时间

        /// <summary>累计伤害（10 秒无伤后归零）。</summary>
        public float Total { get; private set; }
        /// <summary>最近 1 秒的每秒伤害。</summary>
        public float Dps => _windowSum / WindowSeconds;
        /// <summary>本窗口内出现过的最高 DPS（同样随归零一起清）。</summary>
        public float PeakDps { get; private set; }
        /// <summary>命中次数（随归零一起清）。</summary>
        public int Hits { get; private set; }
        /// <summary>距今（秒）多久没有造成伤害。</summary>
        public float IdleTime => _idle;
        /// <summary>还有几秒触发归零（`IdleTime ≥ 10` 时为 0）。</summary>
        public float SecondsToReset => Mathf.Max(0f, ResetIdleSeconds - _idle);
        /// <summary>是否正在倒计时（判据：有累计伤害且已经空闲）。</summary>
        public bool IsIdleCounting => Total > 0.001f && _idle > 0.05f;

        /// <summary>记一笔伤害。`amount ≤ 0` 直接忽略（不脏窗口、不重置空闲计时）。</summary>
        public void AddDamage(float amount)
        {
            if (amount <= 0f || float.IsNaN(amount)) return;

            Total += amount;
            Hits++;
            _buckets[_head] += amount;
            _windowSum += amount;
            _idle = 0f;

            float dps = Dps;
            if (dps > PeakDps) PeakDps = dps;
        }

        /// <summary>推进时间。`dt` 由调用方钳好（本类仍做一次防御性钳制）。</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (dt > 0.25f) dt = 0.25f;          // 与 `TrainingGame` 的口径一致：长帧不做慢动作补偿

            _idle += dt;

            _headAge += dt;
            int guard = 0;
            while (_headAge >= BucketSeconds)
            {
                _headAge -= BucketSeconds;
                _head = (_head + 1) % BucketCount;
                _windowSum -= _buckets[_head];
                _buckets[_head] = 0f;
                if (++guard >= BucketCount)          // 整窗都滚完了还在超 ⇒ 直接清零，不做无意义的空转
                {
                    _windowSum = 0f;
                    System.Array.Clear(_buckets, 0, BucketCount);
                    _headAge = 0f;
                    break;
                }
            }
            if (_windowSum < 0f) _windowSum = 0f;    // 浮点误差兜底（不能出现负的 DPS）

            float dps = Dps;
            if (dps > PeakDps) PeakDps = dps;

            // ★ 10 秒无伤 ⇒ 全部归零 ★
            if (_idle >= ResetIdleSeconds) Reset();
        }

        /// <summary>清零（10 秒判定与"手动重开"共用这一个实现）。</summary>
        public void Reset()
        {
            Total = 0f;
            Hits = 0;
            PeakDps = 0f;
            _idle = 0f;
            _headAge = 0f;
            _head = 0;
            _windowSum = 0f;
            System.Array.Clear(_buckets, 0, BucketCount);
        }

        /// <summary>单行读数（日志/自检用，不进每帧路径）。</summary>
        public override string ToString()
            => $"DPS {Dps:0.0} · 累计 {Total:0} · 命中 {Hits} · 峰值 {PeakDps:0.0} · 空闲 {_idle:0.0}s";
    }
}