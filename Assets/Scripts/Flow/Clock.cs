using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// **计时制的唯一时间源**（替代"回合"）。
    ///
    /// ─────────────────────────────────────────────────────────────
    /// ★ 与既有的 <see cref="GameClock"/> 的分工（重要，别混）★
    /// ─────────────────────────────────────────────────────────────
    ///
    /// | | GameClock（既有，在 Core/Bus.cs） | Clock（本类） |
    /// |---|---|---|
    /// | 职责 | **帧节拍**：渲染帧 → N 个固定逻辑帧 | **游戏时间**：秒 → 分钟 |
    /// | 单位 | 1/60 秒的 tick | 游戏秒 / 分钟 |
    /// | 关心 | 步长稳定、追帧上限 | 进行多久、**暂停** |
    /// | 暂停概念 | 没有 | **有**（唯一闸门） |
    ///
    /// **两者不冲突，是上下游关系**：
    ///
    ///     GameClock.Advance(渲染dt, maxCatchUp, dt => {   // dt 恒为 FixedDt
    ///         session.FixedTick(dt);                      // ← 逻辑帧
    ///         // 里面：clock.Tick(dt)
    ///     });
    ///
    /// ★★ 关键正确性要求：Tick 必须收到 FixedDt，不能收到渲染帧的 dt ★★
    ///
    /// `GameClock` 存在的**全部意义**就是"渲染帧率变化不影响逻辑结果" ✗
    /// 如果这里收渲染帧的 dt，游戏时间就会随帧率漂移 —— 120fps 下走得更快、
    /// 掉帧时更慢，而且**暂停恢复后还会累积误差** ✗
    /// 换句话说：那样做等于**在工程里引入一个违背 GameClock 设计的东西** ✗
    ///
    /// 所以本类**不收渲染 dt**，只收固定步长 ✓
    ///
    /// ─────────────────────────────────────────────────────────────
    ///
    /// ★ 设计要点：不重写消费者，重定义生产者 ★
    ///
    /// `WaveIndex` 原本是一个被外部赋值的**整型状态**，被 30+ 处引用
    /// （敌人数值、商品价格、刷怪数量、商店品质、UI 文案……）。
    /// 若把每一处都改成"分钟"，就是 30 处改动、且必然漏掉几处 ✗
    ///
    /// 本类的做法是把它**降级为派生量**：
    ///
    ///     GameTime（秒，只增） → MinuteIndex → WaveIndex = MinuteIndex + 1
    ///
    /// 于是那 30 处**一行不改**，而语义自动从"第 N 回合"变成"第 N 分钟"，
    /// 上限也从 20 变成无限（因为 GameTime 没有上限）。
    ///
    /// 这个套路是本次会话的教训直接应用：之前"三处定价公式各写一份"
    /// 导致只有一处带波次项，玩家一眼就看出来不对。**先收敛入口，再改语义。**
    ///
    /// ★ 暂停必须只有这一个闸门 ★
    /// 需求要求"进商店暂停所有游戏画面和计时"。如果每处各自判断"是否在商店里"，
    /// 就会出现"敌人停了但子弹还在飞""计时停了但 Boss 倒计时还在走"这类不一致 ✗
    /// 所以所有 dt 都从这里过一遍，<see cref="Paused"/> 是唯一的开关。
    /// </summary>
    public sealed class Clock
    {
        /// <summary>本局已进行的秒数。**只增不减**；暂停时不增长。</summary>
        public float GameTime { get; private set; }

        /// <summary>
        /// ★ v141：**从唯一真值同步**（消债）★
        ///
        /// 我最初让本类自己累加 `dt`，但读代码时发现
        /// `RunSession.FixedTick` 里**早就有 `RunTime += dt;`**，
        /// 而且它位于 `if (Paused) return;` **之后** —— 也就是说
        /// "暂停时不计时"这个语义**工程里本来就有** ✓
        ///
        /// 我当时又造了一个并行的时间源 ✗ 这正是本会话反复出现的病根
        /// （"三处定价公式各写一份，只有一处带波次项"）。
        /// **两套真相必然分歧** ✗
        ///
        /// 所以本类不再自己累加：它变成 `RunTime` 的**派生视图** ——
        /// 时间真值归 `RunSession.RunTime`，本类只负责
        /// 分钟/波次派生、格式化、以及给 `BossSchedule`/`ShopTruck` 一个
        /// 稳定的读取接口 ✓
        ///
        /// `Max` 保证只增不减（即使外部传入回退的值也不会倒流）✓
        /// </summary>
        public void SyncTo(float runTime)
        {
            if (runTime > GameTime) GameTime = runTime;
        }

        /// <summary>唯一的时间闸门。商店/暂停菜单期间置真，此后一切不再推进。</summary>
        public bool Paused { get; set; }

        /// <summary>已完成的分钟数（从 0 起）。</summary>
        public int MinuteIndex => Mathf.Max(0, (int)(GameTime / SecondsPerMinute));

        /// <summary>
        /// **旧契约，新语义**：第几分钟（从 1 起）。
        ///
        /// 保留这个名字是为了让既有 30+ 处引用不改动 —— 它们读到的值
        /// 从"回合序号"平滑变成"分钟序号"，而成长曲线本身一行未动。
        /// 名字确实不再准确，但**改名是纯机械操作，可以延后**；
        /// 现在优先保证行为可回归。
        /// </summary>
        public int WaveIndex => MinuteIndex + 1;

        /// <summary>给 UI 显示用（与 WaveIndex 同值，但语义直白）。</summary>
        public int MinuteNumber => MinuteIndex + 1;

        /// <summary>一分钟的秒数。做成常量而不是散落的 60，避免有人写 59 或 61。</summary>
        public const float SecondsPerMinute = 60f;

        /// <summary>推进时间。<paramref name="dt"/> 用**未缩放**还是缩放时间由调用方决定。</summary>
        public void Tick(float dt)
        {
            if (Paused || dt <= 0f) return;
            GameTime += dt;
        }

        /// <summary>本局重置（重开一局）。</summary>
        public void Reset()
        {
            GameTime = 0f;
            Paused = false;
        }

        /// <summary>格式化成 mm:ss，给顶部计时器用。</summary>
        public string TimeText()
        {
            int total = Mathf.Max(0, (int)GameTime);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        /// <summary>把"还剩多少秒"格式化成 mm:ss，给 Boss 倒计时用。</summary>
        public static string CountdownText(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }
    }
}
