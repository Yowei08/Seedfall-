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
    ///   入场 2.5s（对齐 shop_truck_enter.fbx）+ 绕场 35s + 停留 10s + 离场 2.5s（对齐 exit）
    ///   + **不在场 10s** = 60s
    /// 入场/离场时长**取自动画实际长度**（subagent 交付的是 2.50s），
    /// 这样"动画播完车恰好到位"，不需要额外的等待逻辑 ✓
    ///
    /// ★ 最后那 10 秒"不在场"是用户明确要的（"就开走，不要常驻"）★
    /// 它原先**不存在**（四段恰好凑满 60）⇒ 车在第一分钟之后**永远在场** ✗
    /// 详见下方 `IdleHoldSeconds` 的说明与实测数字 ✓
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
        //
        // ★ 用户的四句话就是这五个常量 ★
        //   "每分钟刷新一次"   ⇒ `CycleSeconds = 60`（每 60 秒**到场一次**）✓
        //   "转一圈到目的地"   ⇒ `LoopSeconds`（绕场一圈）✓
        //   "接着停 10 秒"     ⇒ `DockSeconds = 10`（需求原文，不能动）✓
        //   "就开走，不要常驻" ⇒ ★ `IdleHoldSeconds = 10` ★ —— 开走之后**真的不在场上** ✓
        //
        // ★ 为什么必须补 `IdleHoldSeconds` ★
        // 原来四个阶段**恰好**加起来 = 60（2.5 + 45 + 10 + 2.5）✗
        // ⇒ `Tick` 里那句 `_nextCycleAt = now + (CycleSeconds − 四段之和)` 算出来是 **0** ✓
        // ⇒ 离场的下一帧立刻又入场 ⇒ ★ 第一分钟之后车**100% 在场上**★
        //   实测（同一套观测、修前）：不在场累计 60.10s，其中开局等待 60.0s、
        //   **轮与轮之间的间隙 ≈ 0.10s**（就是 1~2 个固定步）✗
        //   玩家看到的因此是"一辆车永远在场上绕" ⇒ 正是用户说的**常驻** ✓
        //
        // ★ 为什么压缩的是绕场，而不是停靠或入/离场 ★
        //   · `DockSeconds` 是需求原文写的 10 秒 ✗ 不能动 ✓
        //   · `EnterSeconds` / `ExitSeconds` **等于动画的实际长度**（各 2.5s）✗
        //     改短了动画播不完（车会在半路被瞬移）✓
        //   · `LoopSeconds` 原本就写着"**配平项**"—— 它存在的唯一理由是把总和凑成 60 ✓
        //     ⇒ 要腾出"不在场"的时间，只能从它身上腾 ✓ 它本来就是自由变量 ✓
        //
        // ⚠️ 代价（写明）：绕场从 45s 压到 35s ⇒ 车速 2.33 → **2.99 m/s**（路径 104.8m / 35s），
        //    与玩家移动速度（约 3.0 m/s）同档 ✓
        //    要改回去只需改 `IdleHoldSeconds`（0 = 旧行为，四段总和仍为 60）✓
        public const float CycleSeconds = 60f;
        public const float EnterSeconds = 2.5f;     // = shop_truck_enter 动画长度
        /// <summary>开走之后**不在场上**的时长（用户："不要常驻"）。0 = 旧行为（等效于常驻）。</summary>
        public const float IdleHoldSeconds = 10f;
        public const float DockSeconds = 10f;       // 需求：停留 10 秒
        public const float ExitSeconds = 2.5f;      // = shop_truck_exit 动画长度
        /// <summary>配平项：整周期减去其它四段（必须 &gt; 0，否则车永不消失）。</summary>
        public const float LoopSeconds = CycleSeconds - EnterSeconds - DockSeconds - ExitSeconds - IdleHoldSeconds;

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

        /// <summary>
        /// ★ 车此刻应该被"摆"在哪（归一化；调用方乘 `arenaHalf` 得世界坐标）★
        ///
        /// ── 为什么不能直接用 `PointAt(PhaseT(now))` ──
        /// 两个函数的参数**语义不同** ✗：
        ///   · `PointAt(t)` 的 `t` 是【**整条路径**】的进度 —— 0..1 走完 `Path` 的全部 8 段 ✓
        ///   · `PhaseT(now)` 是【**当前阶段**】的进度 —— **每个阶段开始时都从 0 重新计** ✓
        /// 两者**只在 `Looping` 阶段重合**（那时阶段进度恰好就是路径进度）✓
        /// 其余阶段 `PhaseT` 归零 ⇒ 车被"算"成**从入场点 `Path[0]` 重新出发** ✗
        ///
        /// ⇒ 这直接造成了一个玩家可见的缺陷：
        ///   `Dock` 的 **10 秒**里，车**不是停在停靠位** ✗ 而是又跑了一整圈 ✓
        ///   而 `CanShop` 要求"玩家离车 ≤ `InteractRange`(4 米)"⇒
        ///   10 秒的停靠窗里真正可交互的只剩**最后那一小段** ✓
        ///   后果：玩家走到看得见的车旁边按 E，**什么都不发生** ✗
        ///
        /// ── 为什么停靠期就该停在 `DockPoint` ──
        /// `Path` 的**最后一点就是** `DockPoint`，而 `Looping` 结束时 `PhaseT → 1`
        /// ⇒ 车此刻**正好在**停靠位 ⇒ "停靠期原地不动"是唯一的**连续**解 ✓
        /// （这也正是本类注释写明的意图：
        ///   "最后一点就是停靠位，所以 `Looping → Dock` 不需要额外求位置" ✓）
        ///
        /// ── ★ 改动范围被刻意压到最小 ★ ──
        /// **只改 `Dock`** ✓ —— 入场 / 绕场 / 离场三个阶段**保持原样** ✓
        /// 并且 `Looping`（**唯一会清扫的阶段**）的返回值与 `PointAt(PhaseT)` **逐位相同**
        /// ⇒ 巡回清扫的行为**完全不受影响** ✓
        ///
        /// ★ 它是**唯一**的取位入口 ★ —— `RunSession.TickShopTruck` 与
        /// `WorldView.SyncShopTruck` 必须都走它，否则会出现
        /// "看得见的车在 A、能交互的车在 B" ⇒ **界面在撒谎** ✗
        /// </summary>
        public Vector2 SamplePoint(Clock clock)
        {
            if (Current == Phase.Dock) return DockPoint;
            return PointAt(PhaseT(clock.GameTime));
        }

        /// <summary>
        /// 与 <see cref="SamplePoint"/> 配对的"朝向采样进度"。
        /// 停靠期取 `1f` ⇒ `DirAt(1f)` 给出最后一段的方向（车头朝着场内），
        /// 而不是让车在原地**空转一圈** ✗ ✓
        /// </summary>
        public float SampleT(Clock clock)
        {
            if (Current == Phase.Dock) return 1f;
            return PhaseT(clock.GameTime);
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

        /// <summary>
        /// ★ 调试 / 自动化专用：**立刻把车摆到某个阶段** ★（截图与复现用的舞台调度）
        ///
        /// ★ 为什么需要它 ★
        /// 一轮要 60 秒（入场 2.5 + 绕场 35 + 停靠 10 + 离场 2.5 + 不在场 10），
        /// 而"车停在门口那一帧"只占其中 **10 秒** ✗
        /// 靠真实等待去抓那一帧，等于让截图脚本赌时间 ⇒ 不可复现 ✓
        ///
        /// ⚠️ 它**只搬阶段指针** ✗ —— 不改 `Path`、不改时长常量、不做任何交互判定 ✓
        ///    所以"按 E 能不能进店"仍然由 `CanShop` 独立判定（本方法不能让玩家进店）✓
        ///    调用方是 `DebugApi`，它不在任何玩法路径上 ✓
        /// </summary>
        public void DebugForcePhase(Phase p, Clock clock)
        {
            _phaseStart = clock.GameTime;
            Current = p;
            // Idle 要重新锚定下一个周期，否则下一帧立刻又入场（`now >= _nextCycleAt`）✗
            if (p == Phase.Idle) _nextCycleAt = clock.GameTime + CycleSeconds;
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
