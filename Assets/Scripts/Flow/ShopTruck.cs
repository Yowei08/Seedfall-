using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// **流动商店车的状态机**（计时制）。
    ///
    /// 需求：每 1 分钟刷新一次；从地图**右上角**进入；**围绕地图开一圈**；
    /// 路径上碰到的怪物/植物**直接清除且不结算经验**（**排除 Boss**）；
    /// 最后到**顶部中间**停留 10 秒，期间玩家靠近按 E 进商店；10 秒后移走。
    /// Boss 与商店车**可以同时出现**（两者互不阻塞）。
    ///
    /// ★ 关于需求里的一处矛盾，以及我为什么这样解 ★
    /// 需求同时说了两句话：
    ///   "车每 1 分钟刷新一次，**存在 10 秒**，十秒后移走"
    ///   "从右上角进入，围绕地图开一圈……然后回到地图顶上中间区域**停留 10 秒**"
    /// 10 秒装不下"绕场一圈 + 停留" ✗
    /// 本实现按**后者**（更具体）执行，并把前者的"存在 10 秒"理解为
    /// **可交互的停留窗口 = 10 秒** —— 绕场是它的表现过程，不是可交互期 ✓
    ///
    /// ★ 时序配平：整周期必须正好 60 秒 ★
    ///   入场 2.5s（对齐 shop_truck_enter.fbx）+ 绕场 45s + 停留 10s + 离场 2.5s（对齐 exit）= 60s
    /// 入场/离场时长**取自动画实际长度**（subagent 交付的是 2.50s），
    /// 这样"动画播完车恰好到位"，不需要额外的等待逻辑 ✓
    /// </summary>
    public sealed class ShopTruck
    {
        public enum Phase
        {
            Idle,       // 不在场，等下一个周期
            Entering,   // 从右上角驶入
            Looping,    // 绕场一圈（沿途清扫）
            Dock,       // 顶部中间停留 —— **只有这一段可交互**
            Leaving,    // 驶离
        }

        /// <summary>
        /// **巡回路径**（归一化坐标，-1..1；调用方乘场地半宽得到世界坐标）。
        ///
        /// 需求：从**地图右上角**进入 → **围绕地图开一圈** → 回到**地图顶上中间**停留。
        ///
        /// ★ 为什么用归一化坐标而不是世界坐标 ★
        /// 场地尺寸来自 `tuning.json`（`farm.arenaHalf`），会随调参变化 ✗
        /// 把世界坐标写死在这里，调场地大小就得改代码 ✓
        /// 归一化之后，路径只表达"形状"，尺寸由调用方决定 ✓
        ///
        /// ★ 路线刻意是**顺时针**且**贴着边缘** ★（约 0.88 半径处）
        ///   贴边：巡回的意义是"清扫路径上的东西" ✓ 走中间会清掉玩家正在用的耕地 ✗
        ///   顺时针：与玩家"从中心出发向外扩"的动线相反，视觉上更容易被注意到 ✓
        ///
        /// 顶点顺序：右上入场点 → 右 → 下 → 左 → 上 → 顶部中间（停靠点）
        /// 最后一点**就是停靠位**，所以 `Looping → Dock` 不需要额外求位置 ✓
        /// </summary>
        public static readonly Vector2[] Path = new Vector2[]
        {
            // v158：半径从 0.88 收到 **0.72** ✗
            //
            // 实测证据（阶段播报）：
            //   商店车 → Looping｜位置 17.6,17.6｜场地半宽 20.0
            // 场地半宽只有 **20 米**，而 0.88 × 20 = 17.6 —— **正好落在树线里** ✗
            // 所以车上场了却看不见（我抓了两次图都找不到它）✗
            //
            // 我当初定 0.88 是为了"贴着场地边缘巡回" ✓ 但没核对场地实际尺寸 ✗
            // 0.72 × 20 = 14.4 米 —— 在开阔地上，离树线有 5.6 米余量 ✓
            new Vector2( 0.72f,  0.72f),   // 0 入场：右上角
            new Vector2( 0.72f,  0.28f),   // 1 沿右边向下
            new Vector2( 0.72f, -0.72f),   // 2 右下
            new Vector2( 0.00f, -0.72f),   // 3 下边中点
            new Vector2(-0.72f, -0.72f),   // 4 左下
            new Vector2(-0.72f,  0.28f),   // 5 沿左边向上
            new Vector2(-0.72f,  0.72f),   // 6 左上
            new Vector2( 0.00f,  0.72f),   // 7 上边中点
            new Vector2( 0.00f,  0.52f),   // 8 停靠位：顶部中间（略向场内，便于玩家靠近）
        };

        /// <summary>停靠点（路径最后一点）。</summary>
        public static Vector2 DockPoint => Path[Path.Length - 1];

        /// <summary>入场点（路径第一点）。</summary>
        public static Vector2 EnterPoint => Path[0];

        /// <summary>
        /// 归一化路径的**总长度**（用于按速度推时间）。
        /// 调用方乘场地半宽即得世界长度 ✓
        /// </summary>
        public static float NormalizedLength()
        {
            float sum = 0f;
            for (int i = 1; i < Path.Length; i++) sum += (Path[i] - Path[i - 1]).magnitude;
            return sum;
        }

        /// <summary>
        /// 在路径上按**归一化进度** 0..1 取位置（线性插值）。
        /// 用它而不是"等时间走等长" —— 后者在拐角处会变速 ✗
        /// </summary>
        public static Vector2 PointAt(float t)
        {
            float total = NormalizedLength();
            if (total <= 0.0001f) return Path[0];
            float want = Mathf.Clamp01(t) * total;
            float acc = 0f;
            for (int i = 1; i < Path.Length; i++)
            {
                float seg = (Path[i] - Path[i - 1]).magnitude;
                if (acc + seg >= want)
                {
                    float k = seg <= 0.0001f ? 0f : (want - acc) / seg;
                    return Vector2.Lerp(Path[i - 1], Path[i], k);
                }
                acc += seg;
            }
            return Path[Path.Length - 1];
        }

        /// <summary>某个归一化进度处的**前进方向**（用于 `SetFacing`）。</summary>
        public static Vector2 DirAt(float t)
        {
            Vector2 a = PointAt(Mathf.Max(0f, t - 0.01f));
            Vector2 b = PointAt(Mathf.Min(1f, t + 0.01f));
            Vector2 d = b - a;
            return d.sqrMagnitude < 1e-6f ? Vector2.up : d.normalized;
        }

        // ── 时序（秒）──
        public const float CycleSeconds = 60f;
        public const float EnterSeconds = 2.5f;     // = shop_truck_enter 动画长度
        public const float LoopSeconds = 45f;       // 配平项：60 − 2.5 − 10 − 2.5
        public const float DockSeconds = 10f;       // 需求：停留 10 秒
        public const float ExitSeconds = 2.5f;      // = shop_truck_exit 动画长度

        /// <summary>玩家进入交互的距离（米）。比车的尺寸略大一点，贴上去就能触发。</summary>
        public const float InteractRange = 4.0f;

        private float _phaseStart;                  // 当前阶段的起始 GameTime
        private float _nextCycleAt;                 // 下一个周期的起始时刻

        public Phase Current { get; private set; } = Phase.Idle;

        /// <summary>当前阶段已进行的秒数（0..时长）。</summary>
        public float PhaseElapsed(float now) => Mathf.Max(0f, now - _phaseStart);

        /// <summary>当前阶段归一化进度 0..1，给补间/动画采样用。</summary>
        public float PhaseT(float now)
        {
            float dur = DurationOf(Current);
            return dur <= 0.001f ? 1f : Mathf.Clamp01(PhaseElapsed(now) / dur);
        }

        public static float DurationOf(Phase p)
        {
            switch (p)
            {
                case Phase.Entering: return EnterSeconds;
                case Phase.Looping: return LoopSeconds;
                case Phase.Dock: return DockSeconds;
                case Phase.Leaving: return ExitSeconds;
                default: return 0f;
            }
        }

        public void Reset(Clock clock)
        {
            Current = Phase.Idle;
            _phaseStart = clock.GameTime;
            // ★ v160：**开局不刷新车**，第一个周期在 60 秒后 ✗
            //
            // 原来锚在"现在"，于是开局就有一辆车在跑 ✓ 而需求是"每分钟出来转一圈" ——
            // 第一分钟应当从**【没有车】**开始，第 60 秒才第一次来 ✓
            // 这不只是"晚 60 秒"的问题：开局车在场会挡住玩家布置，也和白天的准备期语义冲突 ✗
            _nextCycleAt = clock.GameTime + CycleSeconds;
        }

        /// <summary>
        /// 每帧推进。返回**本帧是否进入了新阶段** —— 调用方据此播放入场/离场动画、
        /// 切换可交互状态等，不需要自己比对枚举 ✓
        /// </summary>
        public bool Tick(Clock clock)
        {
            float now = clock.GameTime;
            Phase before = Current;

            if (Current == Phase.Idle)
            {
                if (now >= _nextCycleAt) Enter(clock, Phase.Entering, now);
                return before != Current;
            }

            // 按"已进行时长"推进，而不是累计 dt —— 这样即便掉帧也不会漂移 ✓
            if (PhaseElapsed(now) < DurationOf(Current)) return false;

            switch (Current)
            {
                case Phase.Entering: Enter(clock, Phase.Looping, now); break;
                case Phase.Looping: Enter(clock, Phase.Dock, now); break;
                case Phase.Dock: Enter(clock, Phase.Leaving, now); break;
                case Phase.Leaving:
                    // 一个周期结束，锚定下一个周期
                    _nextCycleAt = now + (CycleSeconds - (EnterSeconds + LoopSeconds + DockSeconds + ExitSeconds));
                    Enter(clock, Phase.Idle, now);
                    break;
            }
            return before != Current;
        }

        private void Enter(Clock clock, Phase p, float now)
        {
            Current = p;
            _phaseStart = now;
        }

        /// <summary>现在能不能进商店（只有停靠期可以，且玩家要在附近）。</summary>
        public bool CanShop(float distanceToTruck)
            => Current == Phase.Dock && distanceToTruck <= InteractRange;

        /// <summary>停靠期还剩多少秒（给 UI 提示用）。</summary>
        public float DockSecondsLeft(Clock clock)
            => Current == Phase.Dock ? Mathf.Max(0f, DockSeconds - PhaseElapsed(clock.GameTime)) : 0f;

        /// <summary>
        /// 这个角色是否应该被**巡回清扫**。
        ///
        /// ★ 三条排除规则，缺一不可 ★
        ///   ① **Boss 不清**（需求原话"清除除了 boss 外所有"）——
        ///      否则商店车每 60 秒秒掉 Boss，整条 Boss 设计就废了 ✗
        ///   ② 玩家不清
        ///   ③ 只在 Looping 阶段清 —— 停靠期车是静止的，不该继续"碾"周围的东西
        /// </summary>
        public bool ShouldSweep(bool isBoss, bool isPlayer)
            => Current == Phase.Looping && !isBoss && !isPlayer;

        /// <summary>
        /// 清扫是否给奖励。
        /// **恒为 false** —— 需求明确"不掉落经验值" ✓
        /// 做成方法是为了让调用点读起来是这个意思，而不是一个裸的 `false`
        /// （裸 false 很容易被后来的人当成"忘了给奖励"给改掉）。
        /// </summary>
        public static bool SweepGrantsReward => false;
    }
}
