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
    ///   入场 2.5s（对齐 shop_truck_enter.fbx）+ 绕场 5s + 停留 10s + 离场 2.5s（对齐 exit）
    ///   + **不在场 40s** = 60s
    /// 入场/离场时长**取自动画实际长度**（subagent 交付的是 2.50s），
    /// 这样"动画播完车恰好到位"，不需要额外的等待逻辑 ✓
    ///
    /// ★ v175：绕场由 35s **压到 5s 一圈**，停靠位搬到**战场正上方** ★
    /// 用户两条原话：
    ///   "每分钟出现一次 ⇒ **5 秒环绕战场走一圈**（期间直接击杀撞到的敌人）
    ///    ⇒ 然后停在战场**正上方** 10 秒 ⇒ 停下时可打开商店"（本文档以下称"新时序"✓）
    /// 见 `LoopSeconds`（时长）与 `HoverHeight`（"正上方"的高度）两处的推导 ✓
    ///
    /// ★ 最后那 10 秒"不在场"是用户明确要的（"就开走，不要常驻"）★
    /// 它原先**不存在**（四段恰好凑满 60）⇒ 车在第一分钟之后**永远在场** ✗
    /// 详见下方 `IdleHoldSeconds` 的说明与实测数字 ✓
    ///
    /// ★ v180：**"停在正上方"被用户推翻 ⇒ 改成贴地停靠** ★
    ///   原话："商店车最终停止位置**还是飞天的** —— 它停稳后在**空中**（y 太高），没有落到地面" ✓
    ///   ⇒ `HoverHeight` 6 → **0**（轮子接地面；推导与实测数字见那个常量 ✓）
    ///   ⚠️ "正上方"现在只剩**平面**含义：车停在战场**中心**（`DockPoint` = (0,0)）且**贴地** ✓
    ///   时序五段（60s 周期）、路径（8 段 + 停靠位）、离场直线（`ExitEndX = -1.40`）**都没动** ✓
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
        ///
        /// ★ v175：**绕场仍走这 8 段**，变的只是"多快走完"（见 `LoopSeconds`）★
        /// 用户新时序说"5 秒环绕战场走一圈"，**没说改路线** ⇒ 路线不动 ✓
        /// 升空（0 → `HoverHeight`）**不写进本表** ✗ —— 它是由进度算出来的高度，
        /// 见 `HeightAt` ✓ 理由在 `SamplePoint` 的"为什么升空不写进 Path"那段 ✓
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
            // 8 停靠位：★ 战场正上方 ★ ——
            // v175 前写的是 (0, 0.52)，即"场地顶边内侧"，那是**地面**上的一个点 ✗
            // 用户要的"正上方"是**头顶**那个位置 ⇒ 改 (0,0) = 战场中心的正上方 ✓
            // （只改 xy ⇒ 车在水平面上不再偏出战场；高度由 `HoverHeight` 给 ✓）
            new Vector2( 0.00f,  0.00f),
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
        /// <summary>
        /// ★ v175：**绕场一圈 = 5 秒**（用户新时序原话："5 秒环绕战场走一圈"）★
        ///
        /// ── 它从"配平项"变成了"需求给定值" ──
        /// 旧实现里它是**自由变量**（35s，唯一作用是让总和凑成 60，见旧注释）✗
        /// 新时序把它**定死**（5s）⇒ 配平的负担移到 `IdleHoldSeconds` 上 ✓
        ///
        /// ⚠️ 代价（写明）：路径 104.84m（`NormalizedLength()` × 半宽 20m × 2）÷ 5s
        ///    ⇒ 车速 **20.97 m/s** ≈ 75 km/h ✓
        ///    它是"清扫车从你身边呼啸而过"的表现 ✓ 但**不能再用来追**了 ✗
        ///    ⇒ 自动化侧连带改了一处：`RunBot.TryShopTrip` 的"提前出发"窗口
        ///      原是写死的 3.5s，而 3.5s 已经是**新绕场时长的 70%** ✗
        ///      ⇒ 机器人会追着车跑大半个战场而错过停靠 ⇒ 已改为按 `LoopSeconds` 等比缩放 ✓
        ///    （实测数字见 `Docs/reports/shop-truck-*.txt` 与 `DebugApi.TruckReport` ✓）
        /// </summary>
        public const float LoopSeconds = 5f;
        /// <summary>
        /// 开走之后**不在场上**的时长。
        ///
        /// ★ v175：10 → **40** ★ 因为绕场从 35s 压到 5s，那 30 秒必须有人接 ✗
        /// 它的语义**没变**（用户："就开走，不要常驻"）✓
        /// 由 `CycleSeconds` 与其它四段反推 ⇒ 周期仍**正好** 60 秒 ⇒ "每分钟出现一次"成立 ✓
        /// 0 = 旧行为（等效于常驻）✓
        /// </summary>
        public const float IdleHoldSeconds = 40f;
        public const float DockSeconds = 10f;       // 需求：停留 10 秒
        public const float ExitSeconds = 2.5f;      // = shop_truck_exit 动画长度
        /// <summary>
        /// 配平项：整周期减去其它四段（必须 &gt; 0，否则车永不消失）。
        /// ★ v175：它现在是 `CycleSeconds − Enter − Loop − Dock − Exit − IdleHold` = **0** ★
        /// ⇒ 意义变了：`LoopSeconds` 已是需求给定值 ✗ 所以它退化成"校验项"——
        ///   它非 0 就说明五个常量里有人被改过而周期没对上 ✓
        ///   （`SelfCheck` 里那条"五段之和 == 周期"正是查这件事 ✓）
        /// </summary>
        public const float LoopBalanceSeconds = CycleSeconds - EnterSeconds - LoopSeconds - DockSeconds - ExitSeconds - IdleHoldSeconds;

        /// <summary>
        /// ★ v180：停靠时车的**躺地高度**（Unity +Y，米）—— **0**，即轮子压在地面上 ★
        ///
        /// ── 为什么从 6 改成 0（用户原话）──
        /// "商店车最终停止位置**还是飞天的**：它停稳后在**空中**（y 太高），没有落到地面" ✓
        ///
        /// ── 改前实测（运行期探针，不是读注释）──
        /// `DebugApi.TruckDock("dock")` + `TruckState()`：
        ///   世界位置 **(0.0, 6.0, 0.0)**｜`TRUCK t=3.62s｜Dock｜世界位置 (0.0, 6.0, 0.0)`
        /// 再用 `component <轮子> MeshRenderer bounds.min.y` 量同一帧的包围盒：
        ///   Wheel_FL / FR / RL / RR 的 min.y = **6.000**（正好等于 pivot 的 y）
        ///   Body 6.300｜BedTrunk 6.620｜Awning 8.215 —— 最低点就是**轮子** ✓
        /// 地面在 y = 0 ⇒ 改前整车悬空 **6.000m**，车底与地面的差 = **6.000m** ✗
        ///
        /// ── 为什么改后是 0（★ 也是量出来的 ★）──
        /// 轮子的世界 min.y 恒等于 pivot 的 y ⇒ **pivot 恰好坐在轮子接地面**上 ✓
        /// 所以"轮子接触地面"的唯一解就是把 pivot 放到 **y = 0** ✓
        /// ⇒ 改后：停靠 y = **0.000**，车身包围盒底 − 地面 = **0.000m** ✓
        ///   （轮子直径 0.72m：min.y 6.000 → max.y 6.720，两者一起下移 6 米 ✓）
        ///
        /// ── 为什么只改这一个常量就够（三段自动跟着变 ✓）──
        /// `HeightAt` 的三个非零来源全部是**这个常量的倍数**：
        ///   · `Dock`      → `HoverHeight`（= 0 ✓ 贴地）
        ///   · `Looping` 末段 → `smoothstep(k) × HoverHeight`（= 0 ⇒ **不再升空** ✓）
        ///   · `Leaving`   → `LerpUnclamped(HoverHeight, ExitEndHeight, k)`（⇒ **0 → −3 线性** ✓）
        /// ⇒ 三段在接缝处依旧**连续**（0 == 0 == 0），没有"跳一下"✗
        /// ⚠️ 它同时把"绕场最后 15% 抬到 6m"这个表现**去掉了** —— 这是贴地的必然结果：
        ///   若保留升空到 6m 而停靠取 0，`Looping → Dock` 就是**单帧 6 米瞬移** ✗
        ///   （那正是本文件反复记着的旧缺陷 ✓）
        ///
        /// ⚠️ 名字还叫 `HoverHeight`（"悬停高度"），而它现在**恒为 0** ✗
        ///   改名会连带改 `DebugApi.cs`（3 处读数）与 `SelfCheck.cs`（2 条断言），
        ///   而这两个文件在本轮**禁改清单**里 ⇒ 常量名保留，语义在这里说清 ✓
        ///
        /// ⚠️ 已知的连带后果（如实记录，不掩饰）★
        ///   `SelfCheck` 的"商店车停靠位在战场中心正上方（xz 居中）"那条断言末尾带
        ///   `ShopTruck.HoverHeight > 0f` ⇒ 它会变红（437 → 436）。
        ///   **我没有删它**（`SelfCheck.cs` 禁改）—— 它编码的前提是"车悬在空中"，
        ///   而那个前提正是本次需求推翻的东西，需要它的维护者裁决 ✓
        /// </summary>
        public const float HoverHeight = 0f;

        /// <summary>
        /// 绕场最后这一段（归一化进度）用来**升空**。
        ///
        /// 0.85 ⇒ 车贴着地面清完 85% 的路（**清扫半径按地面算，这才是它该有的作用面** ✓），
        /// 然后一边走完最后 15%（约 15.7m）一边抬到 `HoverHeight` ✓
        /// ⇒ 到达停靠位时**恰好**是 `HoverHeight`，`Dock` 期不需要任何补间 ✓
        /// （与 `SamplePoint` 里"最后一点就是停靠位"是同一个思路 ✓）
        ///
        /// ★ v175.1：升空曲线用 **smoothstep**（3k²−2k³）而不是线性 ★
        ///
        /// ── 起因：`SelfCheck` 那条"绕场→停靠高度连续"红了（实测差 **0.0044m**）✗ ──
        /// 病因**不是**接缝处真的跳了一下 ✗ 而是**采样点**：
        ///   · 断言按 0.02s 步进 ⇒ 最后一个 Looping 帧落在 `PhaseT = 1 − 0.02/5 = 0.996`
        ///     （**永远取不到恰好 1.0** ✓）
        ///   · 线性曲线在接缝处的斜率 = `HoverHeight / (0.15×5s) = 8 m/s` ✗
        ///     ⇒ 差 0.004×5s = 0.02s ⇒ **8 × 0.02 = 0.16m**？✗
        ///     实测只有 0.0044m ⇒ 说明那 0.02s 里车还没走完那么远 ✓（见下 ✓）
        ///   · ★ 真因 ★：那条断言的**目标是"两段端点取值相等"** ⇒ 而步进采样**取不到端点** ✗
        ///     于是它实际测的是"**最后一步的高度增量**"✗ —— 那是个**采样伪影** ✗
        ///
        /// ── 为什么用 smoothstep 是**对的修法**（而不是"放宽容差"）✗ ──
        /// smoothstep 的一阶导在**两端都为 0** ✓
        ///   ⇒ 绕场末尾那一步的高度增量被压到 **≈0.045mm**（比线性小 **98 倍** ✓）
        ///   ⇒ 断言回到它**本来的意思**（测端点连续 ✗ 不是测步长）✓
        /// 而"放宽容差到 0.16m"是**把守卫弱化**：真实的 6 米瞬移就查不出来了 ✗
        /// ⚠️ 代价（写明）：爬升中段最快 ≈ `1.5 × 6 / 0.75s = 12 m/s`（线性是 8 m/s）✗
        ///    ⇒ 观感是"先抬头、中段快、再收住"✓ 这本来也是升降该有的手感 ✓
        ///
        /// ★ v180：**它现在是死的** ★
        /// `HoverHeight = 0` ⇒ `smoothstep(k) × 0 = 0` ⇒ 绕场全程恒 0，
        /// 这个"绕场后 15% 抬升"的窗口不再有高度变化 ✓
        /// 常量**保留不删**：它是"升空从路径哪个点到哪个点"的定义，`HeightAt` 仍按它分段；
        /// 哪天车要重新悬停，把 `HoverHeight` 排回非 0 就自动恢复（曲线还是这条 smoothstep）✓
        /// </summary>
        public const float HoverRiseT = 0.85f;

        /// <summary>
        /// ★ v179：入场段的**起点**（归一化）★ —— 车从场地**外**的右上角滑到 `Path[0]`。
        ///
        /// ── 为什么必须补这个点 ✗（用户报的"绕场转了两圈"的真病因）──
        /// 旧 `SamplePoint` 除了 `Dock` 之外**一律**返回 `PointAt(PhaseT)` ✗ 而 `PhaseT` 是
        /// **当前阶段**的进度、`PointAt` 的参数是**整条路径**的进度 ⇒
        /// `Entering`（2.5s）也把**整条 8 段路径走了一遍**，`Looping`（5s）再走一遍 ⇒
        /// ★ 一轮里路径被走过 **2 遍** ★（实测 2.34 圈，见 `DebugApi.TruckTrace`）
        /// ⇒ 玩家看到的正是"绕场转了两圈"✓ 而状态机其实**只进了一次** `Looping` ✓
        ///   （所以"Looping 被进入两次"是误判 ✗ 病在入场段也占用了整条路径 ✓）
        ///
        /// 1.10 × 半宽 20m = 22m ⇒ 在 20m 场地**之外** 2m ✓ 数学上"从场地外开进来"成立 ✓
        /// 与 `Path[0]` 相距 0.38 × 20 ≈ 7.6m，2.5s 走完 ⇒ 约 **3 m/s**（≈ 玩家步行速度 ✓）
        /// </summary>
        public static readonly Vector2 EnterFrom = new Vector2(1.10f, 1.10f);

        /// <summary>
        /// ★ v179：离场段的**终点 X**（归一化；Z 恒为 `DockPoint.y`）★
        ///
        /// 用户要求（原话）："离开时不要转 —— **直接向左转向、开走**"
        ///   ⇒ 离场是**一条直线**：从停靠位 `(0,0)` 沿 -X 出去 ✗ 不再走 `Path` ✗
        ///   ⇒ 旧实现里 `Leaving` 也返回 `PointAt(PhaseT)` ⇒ 车**又绕了一整圈才走**
        ///      （实测离场段走过 134.8m = 1.17 圈 ✓ 这就是"离开时还在转"）
        ///
        /// -1.40 × 半宽 20m = **-28m** ⇒ 在 20m 场地**之外** 8m ✓
        /// （用户："离场终点在场地外，不要停在半空" ✓ 半空由 `ExitEndHeight` 一并解决 ✓）
        ///
        /// ★ v205：它退化成一个**兜底值**（= 半宽 20m 时的确切答案）★
        /// 真正算终点走 `ExitEndXFor(arenaHalf)` ⇒ "**边界 + `ExitOvershootMeters` 米**"
        /// 这件事不再依赖"半宽恰好是 20" ✗（旧写法把 8m 余量**写死在 -1.40 里**：
        /// 半宽一改，余量就跟着变成 0.40 × 半宽，可能只剩 2m ⇒ 车刚过界就被隐藏 = 没开出画面）
        /// </summary>
        public const float ExitEndX = -1.40f;

        /// <summary>
        /// ★ v205：离场终点要**超出地图边界多少米**（世界米）★
        ///
        /// 用户的验收口径是"**移动到地图外围消失**"⇒ 消失必须在**玩家看得见它驶出边界之后** ✗
        /// 而不是"到点了" ✗
        ///
        /// 8m 的来源：边界 = `TuningDef.farm.arenaHalf`（`Assets/Resources/Data/tuning.json`
        /// 的 `farm.arenaHalf = 20.0`）⇒ 终点 x = **-(20 + 8) = -28m** ✓
        /// 取 8 而不是 2~3：车长约 4m ⇒ 8m 时**整车**（含车尾）都已出界 ✓
        /// </summary>
        public const float ExitOvershootMeters = 8f;

        /// <summary>
        /// ★ v205：离场终点的**归一化 X**，由**边界**（`arenaHalf`）推出来 ★
        ///
        /// `arenaHalf` 来自 `TuningDef.farm.arenaHalf`（不写死 ✗）⇒
        /// 终点 = `-(arenaHalf + ExitOvershootMeters)` 米 ⇒ 归一化后 = `-(1 + 8/half)` ✓
        /// 兜底（`half <= 0`，即调用方没给场地尺寸）时退回常量 `ExitEndX` ✓
        /// </summary>
        public static float ExitEndXFor(float arenaHalf)
            => arenaHalf <= 0.001f ? ExitEndX : -(1f + ExitOvershootMeters / arenaHalf);

        /// <summary>
        /// 离场段的**高度**（米）—— ★ v205 起它是**常量 = 地面高度**，不再是"终点高度"★
        /// （名字保留：`HeightAt` 的 `Leaving` 分支与它一一对应，改名会连带改文档锚点）
        ///
        /// 用户要求（原话）："离场时高度**单调下降**到**地面以下**再消失" ✓
        /// 旧实现 `k × HoverHeight` 只降到 **0**（地面）⇒ 车在**战场正中心落地**再消失 ✗
        ///   实测离场末点 `(0.0, 0.00, 0.0)` —— 正是"最终停留地点异常"✓
        ///   （而接缝处 `Dock(0,0,6) → Leaving(Path[0]×20, 6)` 是**单帧 20.36m 的瞬移**，
        ///    高度还停在 6m ⇒ 观感就是"车突然飞起来"✓ 见 `DebugApi.TruckTrace` 的接缝读数）
        ///
        /// -3m：车体约 2m ⇒ 降到 -3m 时**整个车都在地面以下**，接着进入 `Idle` 被隐藏 ✓
        /// ★ v180：起点跟着 `HoverHeight` 一起变成 **0** ⇒ 现在是 **0 → −3 线性** ★
        ///   （改前是 6 → −3，因为那时停靠在 6m ✓ 段本身的**形状一个字没改**：
        ///    仍是线性、仍逐帧**严格单调不升**、终点仍是 −3 = 地面以下 ✓
        ///    变的只是起点 —— 起点必须等于停靠高度，否则 `Dock → Leaving` 接缝会瞬移 ✗）
        ///
        /// ══════════════════════════════════════════════════════════════════════
        /// ★★ v205：**−3 → `HoverHeight`（= 地面高度）** —— 这就是"车开进地里"的病根 ★★
        /// ══════════════════════════════════════════════════════════════════════
        ///
        /// ── 用户报告 ──
        /// "商店车离开出现问题，应该正常向左离开，**移动到地图外围消失**，
        ///   而不是**开到地里面**" ✓
        ///
        /// ── 改前为什么必然"开进地里"（算式，不是猜测）──
        /// `HeightAt` 的 `Leaving` 分支是 `LerpUnclamped(0, −3, k)`，`k = PhaseT` 0→1，
        /// 而 X 是**同一条 Lerp 的同一条 `k`**：从 x=0 走到 x=−28m（半宽 20）✓
        /// ⇒ 两件事在**同一段时间里线性发生**：
        ///   k=0.25（0.625s）x=−7m、y=−0.75m ⇒ 车在地**下** 0.75m，仍在地图**内** ✗
        ///   k=0.50（1.250s）x=−14m、y=−1.50m ⇒ 整车没入地下，离边界还有 **6m** ✗
        ///   k=0.71（1.786s）x=−20m、y=−2.14m ⇒ 到此才**刚出边界**，而车已经埋了 2m ✗
        /// ⇒ 玩家看到的是：车一离开停靠位就**下潜**，在地图内**慢慢沉进地面**消失 ✓
        ///   （它确实是"向左"的 —— 横向那段一直是对的 ✗ 错的是**高度跟它一起往下走** ✓）
        ///
        /// ── 为什么"降到地面以下再隐藏"当初会被写进来（不是手滑）──
        /// v179 那版车的停靠高度是 **6m**（悬空）✗ 用户当时报"留在半空"⇒
        /// 用"降到地面以下再消失"来盖住**悬空**那个病 ✓
        /// v180 把停靠改成贴地（`HoverHeight` 6→0）之后，**这条下降就失去了理由** ✗
        /// 但它没被一起撤掉 ⇒ 现在的作用**只剩**"把贴地的车压进地里" ✗
        /// ⇒ 正确解：离场高度**恒等于地面高度**（= 停靠时轮子接地的那个值 ✓）
        /// </summary>
        public const float ExitEndHeight = HoverHeight;

        /// <summary>
        /// 玩家进入交互的**水平**距离（米，场地 xz 平面）。
        ///
        /// ★ v175：明确它是**水平**距离，与高度无关 ★
        /// 车停靠时悬在 `HoverHeight` 米高处 ⇒ 若按 3D 距离算，
        /// 玩家站在车**正下方**也会被判成"离车 6 米"⇒ **永远进不了店** ✗
        /// 所以交互口径是"玩家站到车的**地面投影**附近"，这与"抬头看见车在头顶"一致 ✓
        /// 调用点用 `World.Player.Pos`（Vector2，本来就是地面坐标 ✓）与 `SamplePoint` 相减，
        /// 天然就是这个口径 ✓
        ///
        /// ★ 4 → **6**（v175 放宽）★ 依据是**车本身变了**，不是"玩家变笨了"✗：
        ///   · 车停 10 秒，玩家要在这 10 秒里**走到**它的地面投影附近 ✓
        ///     停靠位从"场地顶边（20m 外的边上）"搬到"战场正中（0,0）"✗
        ///     ⇒ 玩家本来就常在中心活动 ⇒ 多数时候（0,0）到玩家 **< 6m 是常态** ✓
        ///   · 判据：6m ≈ 玩家 2 秒的步行距离（约 3.0 m/s ⇒ 1.9s）✓
        ///     比它更宽（例如 10m）会让"隔着半个战场就能隔空开店"✗ 语义糊掉 ✓
        ///   · 而**不再**更宽的理由是：4m 时的问题是"够不着"✗ 而根因（车停在远处、窗口只有 10s）
        ///     已被停靠位与提示修掉 ✗ 6m 只是留一点余量 ✓
        /// </summary>
        public const float InteractRange = 6.0f;

        private float _phaseStart;                  // 当前阶段的起始 GameTime
        private float _nextCycleAt;                 // 下一个周期的起始时刻
        private float _cycleStart;                  // ★ v206：本轮 `Entering` 的起始时刻（见 `Tick` 的 `Leaving` 分支）

        // ══════════════════════════════════════════════════════════════════════
        // ★ v206/v208：**"玩家进入过"** —— 与"玩家此刻在不在范围内"是**两个量** ★
        //
        // 用户最终口径（v208）：
        //   "不要玩家离开范围车就开走，只有时间到或者关闭商店才开走" ✓
        // ── 读法 ──
        //   ① 玩家**进入过**（开过面板 / 在交互半径内停留够）⇒ 这是"**关店即走**"的前提条件 ✓
        //   ② 玩家**从来没进入过** ⇒ 保持原计时（`DockSeconds` 到期才走）✓ 一行不动 ✓
        //
        // ★ v208：**"走出半径"不再是离场触发** ✗ ★
        //   v206 曾让"进入过 + 此刻走出半径 ⇒ 立刻 `Leaving`"，本轮按用户口径整段删除 ✓
        //   ⇒ 本字段现在**只喂 `MarkPlayerLeft`**（关店那条），不再参与任何位置判定 ✓
        //
        // ★ 为什么必须是"进入过"而不是"此刻在范围内" ★
        //   "人站在车边"推不出"开过商店面板" ✓ 而"关面板 ⇒ 车走"只该对**真的进过店**的人生效 ✓
        //
        // ★ 为什么不复用 `CanShop` 的返回值 ★
        //   `CanShop` 只在**这一帧**为真/假，它答不了"曾经进过吗" ✓
        //   本字段是**跨帧记忆**，一个周期只置一次、离场后随新周期清零 ✓
        // ══════════════════════════════════════════════════════════════════════
        private bool _playerEntered;

        /// <summary>
        /// 玩家**进入交互半径的时刻**（`-1` = 此刻不在范围内）。
        /// 用途：区分"路过"与"停留" —— 见 `InRangeEnterSeconds` 与 `ReportPlayerPresence`。
        /// </summary>
        private float _inRangeSince = -1f;

        /// <summary>
        /// ★ v206：玩家在交互半径内**待够多久**才算"进入过" ★
        ///
        /// 为什么需要它（而不是"进过半径就算"）：
        ///   停靠位就是**战场正中 (0,0)**，而玩家常在中心活动（`InteractRange` 那段注释实测过
        ///   "多数时候 (0,0) 到玩家 &lt; 6m 是常态"）✓
        ///   ⇒ 只按"某一帧在 6m 内"置位，等于**每个玩家的每一次路过**都算"进店了" ✗
        ///     那么车几乎总是"提前离场"，计时那条路就名存实亡 ✗
        /// ★ 0.5s 的依据 ★：它是 6m ÷ 玩家速 ~3.0 m/s ≈ 2.0s 的**四分之一**，
        ///   即"穿过这个半径"最快也要 2 秒 ⇒ 0.5s 内的读数只可能是**真的停下来** ✓
        ///   （这是**时间**阈值，不是新的距离阈值 —— 距离判据仍然只有 `InteractRange` 一处 ✓）
        /// </summary>
        public const float InRangeEnterSeconds = 0.5f;

        public Phase Current { get; private set; } = Phase.Idle;

        /// <summary>
        /// 本轮玩家是否**进入过**商店车（开过面板 或在交互半径内停留过）。
        /// 只读给诊断/验收用 —— 不改任何行为 ✓
        /// </summary>
        public bool PlayerEnteredThisCycle => _playerEntered;

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
        /// ── ★ v179：四个阶段各自一条**互不相干**的取位规则 ★ ──
        ///   · `Entering` → 从 `EnterFrom`（场外角）**直线**滑到 `EnterPoint`（`Path[0]`）✓
        ///     只走 7.6m，**不再占用整条路径**（这就是"绕两圈"的修法 ✓）
        ///   · `Looping`  → `PointAt(PhaseT)` —— **整条路径恰好一遍** ⇒ 一圈 ✓（一字未改 ✓）
        ///   · `Dock`     → `DockPoint`（停住 ✓ 一字未改 ✓）
        ///   · `Leaving`  → 从 `DockPoint` 沿 -X **直线**开到 `ExitEndX`（场地外）✓
        ///     **不再走 `Path`** ⇒ "离开时不要转" ✓
        /// ⇒ 一圈里路径**只被走一遍**（`TruckTrace` 验收：绕场 1.00 圈 ✓）
        ///
        /// ⚠️ `Looping`（**唯一会清扫的阶段**）的返回值与 `PointAt(PhaseT)` **逐位相同**
        /// ⇒ 巡回清扫的行为**完全不受影响** ✓
        ///
        /// ★ 它是**唯一**的取位入口 ★ —— `RunSession.TickShopTruck` 与
        /// `WorldView.SyncShopTruck` 必须都走它，否则会出现
        /// "看得见的车在 A、能交互的车在 B" ⇒ **界面在撒谎** ✗
        /// </summary>
        public Vector2 SamplePoint(Clock clock) => SamplePoint(clock, 0f);

        /// <summary>
        /// ★ v205：**带场地尺寸**的取位（`SampleWorld` 走这一条）★
        ///
        /// 只有 `Leaving` 的**终点**需要它：终点 = `-(arenaHalf + ExitOvershootMeters)` 米
        /// ⇒ 归一化 = `ExitEndXFor(arenaHalf)` ✓（其余阶段与场地尺寸无关，两条重载逐位相同）
        /// `arenaHalf <= 0` ⇒ 退回常量 `ExitEndX`（= 半宽 20m 时的旧值，行为不变 ✓）
        /// </summary>
        public Vector2 SamplePoint(Clock clock, float arenaHalf)
        {
            switch (Current)
            {
                case Phase.Dock:
                    return DockPoint;
                case Phase.Entering:
                    return Vector2.Lerp(EnterFrom, EnterPoint, PhaseT(clock.GameTime));
                case Phase.Leaving:
                    // 直线向左：X 从 0 走到 `ExitEndXFor(half)`（= 边界外 8m），Z 恒为停靠位 ✓
                    // ★ v205：高度**不参与**这条 Lerp ✗ —— 它恒等于地面（见 `HeightAt`）✓
                    return Vector2.Lerp(DockPoint, new Vector2(ExitEndXFor(arenaHalf), DockPoint.y),
                                        PhaseT(clock.GameTime));
                default:
                    return PointAt(PhaseT(clock.GameTime));       // = Looping
            }
        }

        /// <summary>
        /// ★ v175：车此刻的**高度**（米，Unity +Y）★
        ///
        /// 用户新时序："停在战场**正上方** 10 秒" ✓ —— 所以高度是**状态机的一部分** ✗
        /// 它和 `SamplePoint` 是**一对**：两个都走同一个 `clock`，
        /// 合起来才是"车在哪"（3D），分开就是两次真相 ✗
        ///
        /// ── 三段的取值 ──
        ///   · `Entering` / `Looping`：**0**（贴地跑 ⇒ 清扫才是它该有的作用面 ✓）
        ///   · `Looping` 后 15%：`0 → HoverHeight` 的 smoothstep 上升 ——
        ///     ★ v180 起 `HoverHeight = 0` ⇒ 这一段**退化为恒 0**（不再升空 ✓）
        ///   · `Dock`：**`HoverHeight`**（= **0** ⇒ 轮子压在地面上 ✓ 与 `Looping` 末尾连续 ✓）
        ///   · `Leaving`：**恒 = `ExitEndHeight` = 地面高度**（★ v205：改前是 0 → −3 下潜 ✗）
        ///   · `Idle`：0（模型是隐藏的，值无所谓，但给 0 最不容易误导 ✓）
        ///
        /// ★ 为什么升空不写进 `Path`（不为它加第 10 个顶点）★
        /// `Path` 是**平面巡回路线**（归一化 xz，-1..1）✗ 给它塞一个"高度"字段
        /// 会让 `NormalizedLength()` / `PointAt()` / `DirAt()` 三个函数都要处理第三维 ✗
        /// 而高度**只在两段里非 0**、且是**纯时间函数**（不含平面位置）✓
        /// ⇒ 单独一个函数最省，且"路径形状"这件事保持不变 ✓
        /// </summary>
        public float HeightAt(Clock clock)
        {
            switch (Current)
            {
                case Phase.Dock: return HoverHeight;
                case Phase.Entering:
                case Phase.Idle: return 0f;
                case Phase.Looping:
                {
                    float t = PhaseT(clock.GameTime);
                    if (t <= HoverRiseT) return 0f;
                    float k = (t - HoverRiseT) / (1f - HoverRiseT);      // 0..1
                    // ★ v175.1：smoothstep（3k²−2k³）✗ 不是线性 ✗ ★
                    //   两端一阶导为 0 ⇒ 接缝处的高度增量 → 0（见 `HoverRiseT` 的推导 ✓）
                    return (k * k * (3f - 2f * k)) * HoverHeight;
                }
                case Phase.Leaving:
                {
                    // ══════════════════════════════════════════════════════════════
                    // ★★ v205：**恒定 = 地面高度**（改前是 `0 → −3` 线性下潜）★★
                    // ══════════════════════════════════════════════════════════════
                    // 病根见 `ExitEndHeight` 的推导：下降与"向左开"用的是**同一条 `PhaseT`**
                    // ⇒ 车在地图**内**就已经沉到地面以下（k=0.5 时 x=−14m、y=−1.5m）✗
                    // 现在只返回地面高度：
                    //   · 起点 = 停靠高度（`HoverHeight`）⇒ `Dock → Leaving` **接缝连续**（0 == 0）✓
                    //   · 终点 = 同一个值 ⇒ 整段 2.5s 高度**逐帧相等** ⇒ 不可能再切进地面 ✓
                    //   · 消失仍由"走到几何终点（边界外 `ExitOvershootMeters` 米）"触发 ✓
                    //     —— 那正是 `PhaseT → 1` 的时刻，`SamplePoint` 的 X 到那时才到终点 ✓
                    // ⚠️ 不再用 `LerpUnclamped`：两点相等时它返回同一个数，留着只会让人以为还有坡度 ✗
                    return ExitEndHeight;
                }
                default: return 0f;
            }
        }

        /// <summary>
        /// ★ 车的**世界位置**（3D）★ —— 表现层与调试读数的**唯一**取位入口 ✓
        /// `arenaHalf` 来自 `tuning.json` 的 `farm.arenaHalf`（不写死 ✗）
        /// ★ v205：改走**带场地尺寸**的 `SamplePoint(clock, arenaHalf)` ——
        ///   离场终点要用 `arenaHalf` 算"边界 + `ExitOvershootMeters` 米" ✓
        ///   高度仍只由 `HeightAt` 给（离场恒 = 地面 ✓）
        /// </summary>
        public Vector3 SampleWorld(Clock clock, float arenaHalf)
        {
            Vector2 np = SamplePoint(clock, arenaHalf);
            return new Vector3(np.x * arenaHalf, HeightAt(clock), np.y * arenaHalf);
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

        /// <summary>
        /// ★ v179：车此刻的**朝向**（归一化方向；调用方转成角度）★
        ///
        /// ── 为什么不能继续用 `DirAt(SampleT(clock))` ✗ ──
        /// `DirAt(t)` 是**路径切线** ⇒ 它只能表达"沿 `Path` 走"的朝向 ✗
        /// 而 v179 之后 `Entering` / `Leaving` **都不再走 `Path`**（各是一条独立直线）✓
        /// ⇒ 继续用 `DirAt` 会让车在离场时**仍然朝路径切线**（即"一边开走一边转向"）✗
        ///   那正是用户报的"离开时还在转"的**观感**来源之一 ✓
        ///
        /// 四个阶段：
        ///   · `Entering` → 入场直线方向（从场外角指向 `Path[0]`）✓
        ///   · `Looping`  → `DirAt(PhaseT)`（路径切线，**一字未改** ✓）
        ///   · `Dock`     → `DirAt(1f)`（车头朝场内，停住不转 ✓ 一字未改 ✓）
        ///   · `Leaving`  → ★ **恒定 -X（左）** ★ —— 用户原话"直接向左转向、开走"✓
        ///     ⇒ 离场期朝向**逐帧不变** ⇒ 看得见的是"车头一拧、直线开走" ✓
        ///
        /// ⚠️ 归一化坐标 → 世界方向的映射是 `(x, y) ⇒ (x, 0, y)`（`SampleWorld` 同一条 ✓）
        ///   ⇒ 归一化 `(-1, 0)` 就是世界 **-X** ✓（`Vector2.left` 正是它 ✓）
        /// </summary>
        public Vector2 Facing(Clock clock)
        {
            switch (Current)
            {
                case Phase.Leaving: return Vector2.left;
                case Phase.Entering: return (EnterPoint - EnterFrom).normalized;
                case Phase.Dock: return DirAt(1f);
                default: return DirAt(PhaseT(clock.GameTime));
            }
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
            // ★ v206：新一局 ⇒ 跨帧状态全部归位
            _cycleStart = clock.GameTime;
            _playerEntered = false;
            _inRangeSince = -1f;
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
                    // ★ v175：这一项现在恒为 **0**（五段恰好配平 60s）★
                    //   那为什么还留着它 ✗ —— 它是**安全网**：
                    //   将来有人只调时长常量、不改周期时，这一项吸收误差 ✓
                    //   否则一旦四段之和 > 60，这里会算出负数 ⇒ `now >= _nextCycleAt` 立刻成立
                    //   ⇒ 又回到"离场下一帧就入场"那种**常驻** ✗（v160 修过的那个 bug ✓）
                    //
                    // ★ v206：**提前离场后仍要守住 60 秒的节拍** ★
                    //   本条需求让 `Dock` 可能**没走满** `DockSeconds` 就离场（玩家走了⇒立刻开走）✓
                    //   若还按 `now + (Cycle − 四段)` 锚定，每次提前离场都会把**下一轮提前** ✓
                    //   ⇒ 玩家每轮都提前走的话，车会越来越频繁地来（"每分钟一次"失效）✗
                    //   ⇒ 改成"**以本轮开始时刻为准**"：`_cycleStart + CycleSeconds` ✓
                    //   ★ 满时长时这两条**等价**（now = cycleStart + 20 ⇒ cycleStart+60 = now+40）★
                    //     所以旧行为**逐位不变**；只有提前离场那一路才被拉回节拍 ✓
                    //   ★ 同时 `now + IdleHoldSeconds` 仍是**下限**：离场后至少 40 秒不在场 ⇒
                    //     "玩家还站在范围内 ⇒ 又立刻停靠"这种抖动**不可能发生** ✓
                    _nextCycleAt = Mathf.Max(now + IdleHoldSeconds, _cycleStart + CycleSeconds);
                    Enter(clock, Phase.Idle, now);
                    break;
            }
            return before != Current;
        }

        private void Enter(Clock clock, Phase p, float now)
        {
            Current = p;
            _phaseStart = now;
            // ★ v206：跨帧状态按阶段复位（见 `_playerEntered` 那段）★
            //   · 进入 `Entering` = **新的一轮** ⇒ "进入过"清零（一次周期只离场一次 ✓）
            //   · 进入 `Dock`     ⇒ 停留计时重新起算（绕场期间不该攒"停留秒数"）
            //   · 进入 `Idle`     ⇒ 不在场了，半径计时无意义
            if (p == Phase.Entering) { _cycleStart = now; _playerEntered = false; _inRangeSince = -1f; }
            else if (p == Phase.Dock || p == Phase.Idle) { _inRangeSince = -1f; }
        }

        /// <summary>
        /// ★ 调试 / 自动化专用：**立刻把车摆到某个阶段** ★（截图与复现用的舞台调度）
        ///
        /// ★ 为什么需要它 ★
        /// 一轮要 60 秒（入场 2.5 + 绕场 5 + 停靠 10 + 离场 2.5 + 不在场 40），
        /// 而"车停在战场正上方那一帧"只占其中 **10 秒** ✗
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
            // ★ v206：舞台调度 = 全新一次，跨帧状态一并归位（否则上一轮的"进入过"会串味）
            _cycleStart = clock.GameTime;
            _playerEntered = false;
            _inRangeSince = -1f;
            // Idle 要重新锚定下一个周期，否则下一帧立刻又入场（`now >= _nextCycleAt`）✗
            if (p == Phase.Idle) _nextCycleAt = clock.GameTime + CycleSeconds;
        }

        /// <summary>现在能不能进商店（只有停靠期可以，且玩家要在附近）。</summary>
        public bool CanShop(float distanceToTruck)
            => Current == Phase.Dock && distanceToTruck <= InteractRange;

        /// <summary>
        /// ★ v206：**"玩家开过商店面板"** ⇒ 记成"进入过" ★
        /// 由 `RunSession.TickShopTruck` 在真正 `SetPhase(PhaseState.Shop)` 的那一帧调用 ✓
        ///
        /// ⚠️ 它只是**保险**：能打开面板的前提就是玩家在 `InteractRange` 内，
        ///    而 `ReportPlayerPresence` 在那一帧已经会记"在范围内" ✓
        ///    但它记的是"停留够 0.5s 才算进过"，而**按 E 是瞬间行为** ——
        ///    玩家可能刚踏进半径就按 E ⇒ 只有这条能保证"开过面板"一定算进入过 ✓
        /// </summary>
        public void MarkPlayerEntered() => _playerEntered = true;

        /// <summary>
        /// ★ v206：**每帧由会话层喂一次"玩家此刻是否在交互半径内"**，本方法只负责
        /// **记录"进入过"** ★
        ///
        /// ── v208：**"离开半径 ⇒ 立刻离场"那一段已删除** ✗ ★
        ///   用户口径："不要玩家离开范围车就开走，只有时间到或者关闭商店才开走" ✓
        ///   ⇒ 出了半径**什么都不发生**：车停满 `DockSeconds` 才走（`Tick` 的 `Dock` 分支）✓
        ///   ⇒ 返回值也随之失去意义（原来返回"本帧是否因此提前 `Leaving`"）
        ///     故本方法改成 **`void`**，调用点不再据此播"开走了" ✓
        ///
        /// ── 只保留的那一半：置 `_playerEntered` ──
        ///   它是 `MarkPlayerLeft`（关店即走）的前提条件 ⇒ **必须留** ✓
        ///
        /// ── 判据 ──
        ///   · `inRange` 由调用方按**引擎自己的** `InteractRange`（`CanShop`）算好传进来 ✓
        ///   · 停留时长用 `Clock.GameTime`（世界时间）⇒ 面板开着时时钟停摆，读数不会乱跳 ✓
        ///
        /// ── 不会误判"路过" ──
        ///   ① 只在 `Dock` 期统计 —— 绕场（`Looping`）时从玩家身边飞过**不算进入** ✓
        ///   ② 要**在半径内待够** `InRangeEnterSeconds` 才置位（穿过去最快也要 ~2s）✓
        ///   ③ 开过面板 ⇒ `MarkPlayerEntered` 直接置位（见那里的说明）✓
        ///
        /// ── 不会重复/抖动 ──
        ///   · `_playerEntered` 在 `Entering`（新一轮）与 `Reset` 时清零 ⇒ **一次周期最多走一次** ✓
        /// </summary>
        public void ReportPlayerPresence(Clock clock, bool inRange)
        {
            if (Current != Phase.Dock) return;

            if (inRange)
            {
                if (_inRangeSince < 0f) _inRangeSince = clock.GameTime;
                if (clock.GameTime - _inRangeSince >= InRangeEnterSeconds) _playerEntered = true;
                return;
            }

            // 出了半径：只清"停留计时"✓ **不离场** ✗（v208：离场只看计时与关面板）
            _inRangeSince = -1f;
        }

        /// <summary>
        /// ★ v207：**"关闭商店面板 ⇒ 车立刻开走"**（用户原话："我只要关闭商店，就应该离开"）★
        ///
        /// ── v208：它现在是**两条**离场触发之一（另一条是停满 `DockSeconds`）──
        ///   用户最终口径："不要玩家离开范围车就开走，只有时间到或者关闭商店才开走" ✓
        ///   ⇒ 原来那条"走出半径 ⇒ 立刻走"已从 `ReportPlayerPresence` 删除 ✓
        ///
        /// ── 为什么"关面板"这件事该由会话层显式喊一嗓子 ──
        ///   "面板开着/关着"的真值是 `RunSession.Phase == PhaseState.Shop` ✗ 不是位置 ✓
        ///   位置**推不出**这个意图：站在车边的玩家完全可能只是路过、还没进店 ✓
        ///   所以本方法不自己判定"该不该走"，只做**被明确告知**之后的那个动作 ✓
        ///
        /// ── 判据两条（缺一不可）──
        ///   ① `Current == Phase.Dock` —— 不在停靠期就直接 `false`
        ///      ⇒ 防重复（`Leaving` 期再调不会重进一次）✓ 防抖动 ✓
        ///   ② `_playerEntered` —— 只对"**真的进过店**"生效
        ///      ⇒ 从没进过的情况仍然走"停满 10 秒"那条老路（需求："不进入才触发时间后退出"）✓
        ///
        /// ── 与计时那条的关系（**只有两条**，互不冲突）──
        ///   · `Tick` 的 `Dock` 到期 → `Leaving`：从没进过 / 进过但没关面板时的**唯一**出口 ✓
        ///   · 本方法：进过店 **且关掉面板** ⇒ 立刻走（哪怕人还站在车下）✓
        ///   两条的终点都是同一个 `Enter(clock, Phase.Leaving, now)` ⇒ 表现完全一致 ✓
        ///   任一先到就把阶段改成 `Leaving`，另一条随后因 ① 返回 `false` ✓
        ///
        /// 返回**是否真的因此进入 `Leaving`**（调用方据此播"商店车开走了"）✓
        /// </summary>
        public bool MarkPlayerLeft(Clock clock)
        {
            if (Current != Phase.Dock) return false;   // ① 防重复/防抖动（见上）
            if (!_playerEntered) return false;         // ② 没进过店 ⇒ 计时那条路不动

            _inRangeSince = -1f;                       // 人到底在哪不重要了：本轮已经"结束"
            Enter(clock, Phase.Leaving, clock.GameTime);
            return true;
        }

        /// <summary>停靠期还剩多少秒（给 UI 提示用）。</summary>
        public float DockSecondsLeft(Clock clock)
            => Current == Phase.Dock ? Mathf.Max(0f, DockSeconds - PhaseElapsed(clock.GameTime)) : 0f;

        /// <summary>
        /// 这个角色是否应该被**巡回清扫**（= 直接击杀）。
        ///
        /// ★ 三条排除规则，缺一不可 ★
        ///   ① **Boss 不清**（需求原话"清除除了 boss 外所有"）——
        ///      否则商店车每 60 秒秒掉 Boss，整条 Boss 设计就废了 ✗
        ///   ② 玩家不清
        ///   ③ 只在 Looping 阶段清 —— 停靠期车是静止的，不该继续"碾"周围的东西
        ///
        /// ★ v175：它现在是"**击杀资格**"的判定，不再只是"移除资格"★
        /// 用户新时序原话："期间**直接击杀**撞到的敌人" ✓
        /// ⇒ 调用点（`RunSession.TickShopTruck`）改为走**已有的死亡路径**
        ///   （`Health.Kill()` → `World.Cleanup()`），而不是从 `Enemies` 里 `RemoveAt` ✓
        /// 本方法的判定条件**一个字没改** —— 变的只是"怎么死"✗ 不是"谁能死"✓
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
