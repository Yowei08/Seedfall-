using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v255：肥料三件新机制的**跨帧状态**（伏雷 / 护壤 / 灼印）★★
    ///
    /// 与 `PluginMech`（回响 / 收束）分开成两个文件，理由不是好看：
    ///   · `PluginMech` 的两个是**跟着一次攻击走**的账（延迟结算、连续命中计数）；
    ///   · 这里的三个是**世界上的物体 / 玩家身上的状态**（埋着的雷、护盾到期、敌人身上的印）。
    ///   两类东西的生命周期完全不同（前者跟敌人走、后者跟世界走）⇒ 混在一张表里，
    ///   "重置本局"那一步就会漏掉一半 ✓
    ///
    /// ── 确定性 ──
    ///   本类**不消耗任何随机数**（只用位置、时间、距离）⇒ 钉种子的自检读数不会因为装这三件而漂移 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// ★ v281：修「异常灼印叠加后直接卡住」—— 本文件这一轮的四条硬纪律 ★
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户原话：「异常灼印叠加后直接卡住，但是使用了水壶可以攻击，且攻击敌人触发印记后就会恢复正常」
    ///
    /// 逐条对上症状之后的结论（不是猜的，每条都能在本文件里指到行）：
    ///   ① **卡住 = 逻辑帧被打断，不是 `timeScale`，也不是 Update 停**：
    ///      水壶的持续激光走 `RunSession.HandlePlayer` 里那个**在 `p.CanAttack` 之外**的分支
    ///      （`RunSession.cs` 的 `WeaponCan` 段），而锄头 / 肥料要先过
    ///      `p.CanAttack => SwitchWindup <= 0f && WeaponCds[WeaponIndex] <= 0f`（`Actors.cs:671`）。
    ///      `WeaponCds` / `SwitchWindup` **只在 `PlayerActor.Tick`（`World.Simulate` 里）衰减** ⇒
    ///      只要某个逻辑帧在 `World.Simulate` **之前**抛出去，冷却就再也不衰减 ⇒
    ///      `CanAttack` 恒假 ⇒ **锄头/肥料出不了手**，而水壶照样能打 ✓（两条症状严格对齐）
    ///   ② **"消耗印记后恢复" ⇒ 抛点被"有印"这个条件包着**：本工程里唯一由"有印"分叉、
    ///      又在逻辑帧里同步执行的表现层调用，就是 `Actors.DealDamage` 里那句
    ///      `Bus.Publish("印")` → `RunSession.OnFloatingText` → `PluginHitFx.Apply`（同步栈）。
    ///      印记被消耗后那个分支不再走 ⇒ 异常消失 ⇒ 下一帧 `World.Simulate` 跑起来 ⇒ 一切恢复 ✓
    ///   ③ 所以这一轮在本文件里做的是"**把印记这一路变成不可能无限增长、也不可能重入**"：
    ///      · **层数硬上限 `MarkMaxStacks = 5`**（超了只刷新时长，不再叠层）
    ///      · **倍率不随层数累乘**（印记只放大"下一次伤害"一次，见 `ConsumeMark`）
    ///      · **消耗是单步 `Remove`，全程没有 `while` / 没有递归** ⇒ 必定有限步退出
    ///      · **`Tick` 里绝不在遍历字典的过程中回写**（先收集、再写回、最后删到期）
    ///      · **`Tick` 加显式重入闸**：伏雷自爆的回调链若再次回到本方法，直接跳过（本帧已处理）
    ///   ④ 表现层那一路（`PluginHitFx`）的隔离在 `RunSession.OnFloatingText`：那里用 try/catch
    ///      把表现层的任何异常**关在逻辑帧之外** ⇒ 以后无论哪个特效炸了，都不会再冻结世界 ✓
    /// ══════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class PluginField
    {
        // ═════════════════════════ 伏雷 ═════════════════════════
        public struct Mine
        {
            public Vector2 Pos;
            public float Radius;        // 爆炸半径（与主爆同一个数）
            public float Sense;         // 感应半径
            public float ArmLeft;       // 还要多久上膛（>0 时不响应踩踏）
            public float FuseLeft;      // 上膛后还有多久自爆
            public float HitMult;       // 踩中倍率（1.5）
            public float MissMult;      // 超时倍率（0.8）
            /// <summary>★ v309：**下一次地面脉冲**还有多久（秒）。见 <see cref="MinePulseInterval"/>。</summary>
            public float PulseLeft;
            /// <summary>★ v311：**埋这颗雷的那次投掷装没装黑洞**（`pm.BlackHole` 的快照）。
            /// 为什么快照而不是炸的时候再读：雷能活 6 秒，那时玩家手上的插件可能已经换掉了；
            /// "这颗雷是不是黑洞雷"是**埋的那一刻**的事实 ✓</summary>
            public bool BlackHole;
            /// <summary>★ v311：这一次投掷一共几个落点（连环）—— 黑洞漩涡按它**分档**省格数
            /// （3 颗雷前摇同帧开 ⇒ 每套只能占 19 格，见 `AttackVfx.SpawnCollapseRing`）✓</summary>
            public int Spots;
            /// <summary>★ v311：这颗雷的**黑洞前摇**是否已经开过（`FuseLeft` 跨过
            /// `PluginExtra2.BlackHoleWindup` 那一帧置 true）⇒ 同一次爆炸不会被画两套漩涡 ✓</summary>
            public bool BhWindup;
        }

        private static readonly List<Mine> Mines = new List<Mine>(8);
        /// <summary>
        /// 场上**保底**的同时可存几颗伏雷（= 一次只放 1 颗时的口径，即改前那个 3）。
        /// 实际容量看 <see cref="MineCapFor"/> —— 装了连环之后一次投掷会放多颗，容量必须跟着涨。
        /// </summary>
        public const int MineCap = 3;

        /// <summary>
        /// ★★ v305：场上最多存**几次投掷的量**（见 <see cref="MineCapFor"/>）★★
        /// 取 2 = "上一批 + 这一批"：新雷最多只能挤掉**上一次投掷的那一批**，
        /// 而且只在场上真的已经堆满两批时才发生 ⇒ 不会出现"放一颗新的，旧的整片没了"✓
        /// </summary>
        public const int MineThrowsHeld = 2;

        /// <summary>伏雷上膛后多久自爆（秒）。</summary>
        public const float MineFuse = 6f;

        /// <summary>
        /// ★★ v309：**埋着的雷每隔多久在地面闪一下**（秒）★★
        ///
        /// 用户原话：「伏雷：埋雷期间地面加**微弱的脉冲光点**（让玩家能看见雷在哪）」。
        /// 改前埋下去的雷在地上**没有任何标记**：只有埋那一刻的前摇光圈，`MineArmTime + 0.35s`
        /// 就灭了，之后要等最多 6 秒 —— 玩家完全看不见它埋在哪 ✗
        ///
        /// · 值取 **0.55s**：比心跳快一点、比"常亮"慢得多 ⇒ 一明一暗读作"还有东西在下面"；
        ///   与寿命 0.45s（`AttackVfx.MinePulseLife`）配起来中间有 0.10s 的**全灭**，
        ///   所以它真的是"脉冲"而不是"一个会呼吸的圈" ✓
        /// · 节流走**固定帧**（本方法只被 `RunSession` 的固定 tick 调）⇒ 确定性、
        ///   与帧率无关、回放一致 ✓（和 `_frenzyFxCd` 同一套做法）
        /// · 常量与 `AttackVfx.MinePulseEvery` **是同一个数**（那边用它算脉冲长度，这里用它排周期）
        /// </summary>
        public const float MinePulseInterval = AttackVfx.MinePulseEvery;

        /// <summary>
        /// ★★ v305：本局**累计放下过**多少颗伏雷（诊断读数）★★
        /// 与 <see cref="MineCount"/>（此刻场上剩几颗）配对看，一次就能分清两种病：
        ///   · `MineDroppedTotal` 不涨 ⇒ 根本没埋下去（投掷路径的问题）
        ///   · `MineDroppedTotal` 涨而 `MineCount` 不涨 ⇒ **埋了又被顶掉**（本轮修的就是这一种）
        /// 用户报「放新的会直接导致旧的丢失」时，这两个读数一对比就是证据 ✓
        /// </summary>
        public static int MineDroppedTotal { get; private set; }

        /// <summary>
        /// ★★ v305：此刻场上最多能存几颗伏雷 = `max(MineCap, MineThrowsHeld × 一次投掷的落点数)` ★★
        ///
        /// ── 为什么容量必须跟着"一次投掷放几颗"变（用户报的那个 bug 的本体）──
        ///   改前容量恒为 `MineCap = 3`，而装了**连环**（`fert_chain`）之后
        ///   **一次投掷就要放 `1 + 连环层数` 颗**（`RunSession.ArmBlast` 的 `spots` 循环，
        ///   `plugins.json` 里 `fert_chain.maxStack = 2` ⇒ 最多 3 颗）。
        ///   ⇒ 容量 3 被**一次投掷就吃满**，而 `QueueMine` 的兜底是"顶掉最早一颗"
        ///   ⇒ 同一次投掷的第 2、3 颗走兜底时，挤掉的正是**上一批埋的旧雷** ✗
        ///   玩家看到的正是「放新的，旧的直接没了」（不是"慢"，是**当场删掉**）。
        ///
        /// ── 数值口径（每一项都能对账）──
        ///   · 落点数 = `WeaponSystem.WeaponModifiers.BlastCount = 1 + ChainBurstCount`
        ///     （`plugins.json`：`fert_chain.amount = 1`、`maxStack = 2` ⇒ 1/2/3 个落点）
        ///   · 容量 = `MineThrowsHeld(2) × 落点数`，但**不低于 `MineCap`(3)**
        ///   · 没连环（落点 1）⇒ `max(3, 2×1) = 3` —— **与改前逐位相同**，不趁机放大 ✓
        ///   · 连环 1 层（落点 2）⇒ `max(3, 2×2) = 4` 颗
        ///   · 连环 2 层（落点 3）⇒ `max(3, 2×3) = 6` 颗 ✓（用户口径"3 层时场上最多 6 颗"）
        ///   ⚠️ 这是**执法上限**，不是数值设计 ⇒ 按本文件既有纪律（见 `MarkMaxStacks` 的取数理由）
        ///      留在代码里，不进 `plugins.json` ✓
        /// </summary>
        public static int MineCapFor(int spotsPerThrow)
            => Mathf.Max(MineCap, MineThrowsHeld * Mathf.Max(1, spotsPerThrow));

        /// <summary>
        /// 最近一次投掷算出的容量（只读诊断用）。表现层/探针不必知道 `spots` 就能打印
        /// 「场上 <see cref="MineCount"/> / 上限 <see cref="MineCapNow"/> / 累计放下 <see cref="MineDroppedTotal"/>」，
        /// 这三个数一起看就能直接判断"是不是被顶掉了" ✓（没埋过雷时 = <see cref="MineCap"/>）
        /// </summary>
        public static int MineCapNow { get; private set; } = MineCap;

        /// <summary>只读给表现层（`ImGuiHud` 画埋雷标记 / 探针读数）。</summary>
        public static int MineCount => Mines.Count;
        public static Mine MineAt(int i) => Mines[i];

        /// <summary>
        /// 埋一颗伏雷。
        /// <paramref name="spotsPerThrow"/> = **这一次投掷一共放几颗**（`ArmBlast` 的 `spots`）——
        /// 容量由它决定（见 <see cref="MineCapFor"/>）：这样"一次投掷"永远吃不满容量，
        /// 旧雷只在**真的超过上限**时才会被顶掉 ✓
        /// <paramref name="blackHole"/> = 埋的时候装了黑洞吗（★ v311）。存在雷身上，
        /// 由 <see cref="TickMines"/> 决定"这一颗雷什么时候开漩涡"（见那里的两处注释）✓
        /// </summary>
        public static void QueueMine(Vector2 pos, float radius,
                                     float armTime, float sense, float hitMult, float missMult,
                                     int spotsPerThrow, bool blackHole = false)
        {
            // ★ v305：兜底保留（用户口径"超上限顶掉最早一颗"），但阈值从死数 3
            //   改成**随落点数涨的容量** ⇒ 一次投掷不会再把自己那批之外的旧雷挤掉 ✓
            //   `while` 而非 `if`：容量可能比上一批时**更小**（卖掉连环插件后容量回落），
            //   那种情况必须一次削到新上限，否则会留下超量的雷。循环上界 = 现有雷数，必退出 ✓
            int cap = MineCapFor(spotsPerThrow);
            MineCapNow = cap;
            while (Mines.Count >= cap) Mines.RemoveAt(0);
            Mines.Add(new Mine
            {
                Pos = pos, Radius = radius,
                ArmLeft = Mathf.Max(0.05f, armTime), FuseLeft = MineFuse,
                Sense = Mathf.Max(0.5f, sense), HitMult = hitMult, MissMult = missMult,
                // ★ v309：第一下脉冲**立刻**给（埋下去当场就看得见），之后按周期循环
                PulseLeft = 0f,
                // ★ v311：黑洞雷的两样身份（见 `Mine` 结构里的注释）
                BlackHole = blackHole,
                Spots = Mathf.Max(1, spotsPerThrow),
                BhWindup = false,
            });
            MineDroppedTotal++;
        }

        public static void ClearMines() => Mines.Clear();

        // ═════════════════════════ 灼印 ═════════════════════════

        /// <summary>
        /// 敌人身上的一枚印记。
        ///
        /// ★ `Stacks` 是**本轮新增的读数**，不是新的一条数值轴 ★
        ///   · `Bonus` 只由 `MarkEnemy` 的入参决定（`plugins.json` 的 `amount × 插件层数`），
        ///     **不乘 `Stacks`** ⇒ 打击伤害与改前逐位相同（用户报的就是"叠加"出问题，
        ///     这一轮的方向是**封掉叠加的后果**，不是把叠加做成一条新乘区 ✓）
        ///   · `Stacks` 回答的是"这一枚印在一次投掷里被重新盖了几次"，
        ///     由 <see cref="MarkMaxStacks"/> 封顶 —— 它同时是**绘制侧唯一该读的层数**
        ///     （`ImGuiHud` 每敌画一次 X + 一个数字角标，成本与层数无关 ✓）
        /// </summary>
        private struct Mark
        {
            public float Bonus;     // 0.5 = +50%（每次挂印都是这个值，不随层数累乘）
            public float Left;      // 剩余秒数
            public int Stacks;      // 叠了几次（1..MarkMaxStacks）
        }

        /// <summary>
        /// ★★ 灼印的层数硬上限：**5** ★★
        ///
        /// 为什么是 5（不是一个随手取的大数，也不是 1）：
        ///   · 一次投掷最多能对**同一只敌人**盖几次印 = 落点数 × 每次落点的判定次数：
        ///     `pm.BlastCount`（主爆 + 连环 `fert_chain`，1 + 层数）个落点，
        ///     再各自带 `pm.SubBurstCount`（子爆，1 + 层数）个子爆 —— 见 `RunSession.ExplodeAt`。
        ///     商店里 1~3 层的实际配置下，最坏情况正好落在 **3~5** 这个区间 ✓
        ///   · 5 既"够表达这一投掷打了几次"，又让**任何按层数展开的东西**
        ///     （读数、绘制、将来的逐层结算）有一个确定的上界 ✓
        ///   · 为什么不取 1：印记本来就只放大"下一次伤害"一次（`ConsumeMark` 会把它删掉），
        ///     层数 1 会让"同一投掷里盖了几次"这条读数消失，玩家看不出"技能真的命中了多下" ✗
        ///   · 为什么不放进 `plugins.json`：它不是一条**数值设计**（不影响任何伤害/时长），
        ///     而是**防无界增长的执法上限** ⇒ 与 `MineCap` / `BurnMaxStacks` / `SlowMaxStacks`
        ///     同一个位置（代码常量），理由相同：数值表可以被调，执法点不行 ✓
        /// </summary>
        public const int MarkMaxStacks = 5;

        private static readonly Dictionary<int, Mark> Marks = new Dictionary<int, Mark>(16);

        /// <summary>`Tick` 的写回缓冲（复用，不每帧 new）。</summary>
        private static readonly List<KeyValuePair<int, Mark>> TickWrites =
            new List<KeyValuePair<int, Mark>>(16);

        /// <summary>`Tick` 的重入闸：伏雷自爆 → `ExplodeAt` → （任何路径）再次回到 `Tick` 时，
        /// 这一次直接跳过 —— 本帧的印记/护盾/伏雷**已经处理过** ✓</summary>
        private static bool _ticking;

        /// <summary>
        /// 给敌人挂印。**同一只敌人只有一枚印**：再挂 = 层数 +1（封顶 <see cref="MarkMaxStacks"/>）
        /// + 时长刷新，**倍率不累乘**。
        /// </summary>
        public static void MarkEnemy(int enemyId, float bonus, float seconds)
        {
            if (bonus <= 0f || seconds <= 0f) return;
            int stacks = 1;
            if (Marks.TryGetValue(enemyId, out Mark old))
                stacks = old.Stacks < MarkMaxStacks ? old.Stacks + 1 : MarkMaxStacks;
            // `Left = seconds`（覆盖，不累加）—— 与改前逐位相同；"超上限只刷新时长"就落在这一行 ✓
            Marks[enemyId] = new Mark { Bonus = bonus, Left = seconds, Stacks = stacks };
        }

        /// <summary>这颗印还在吗（表现层画「X」用，不消耗）。</summary>
        public static bool HasMark(int enemyId) => Marks.ContainsKey(enemyId);

        /// <summary>这颗印叠了几层（1..<see cref="MarkMaxStacks"/>；没印 = 0）。表现层角标只读它。</summary>
        public static int MarkStacks(int enemyId)
            => Marks.TryGetValue(enemyId, out Mark m) ? m.Stacks : 0;

        /// <summary>这颗印还剩多少秒（没印 = 0）。探针/自检读数用。</summary>
        public static float MarkRemain(int enemyId)
            => Marks.TryGetValue(enemyId, out Mark m) ? Mathf.Max(0f, m.Left) : 0f;

        /// <summary>场上带印的敌人数（只读；自检用它断言"印记一定会被清干净"）。</summary>
        public static int MarkCount => Marks.Count;

        /// <summary>
        /// ★★ v286：**只看不给** —— 这只敌人此刻的印记倍率（有印 = 1+bonus，没印 = 1，**不删除**）★★
        ///
        /// 用户原话："灼印插件疑似在与毒区、自爆等插件配合时不生效。"
        ///
        /// ── 病根：改前"读倍率"与"摘印"是**同一步**（`ConsumeMark`）──
        ///   于是"谁先打到就谁吃掉"。而肥料线里排在**下一次主爆之前**的伤害有两种，
        ///   两种都比主爆小得多：
        ///     · 毒区 tick（`PluginDotTable.Hit`，每秒一跳、量 = 这一炮的 50%×层数）
        ///     · 子爆（`RunSession.BlastDamageAt(..., mainBlast:false)`，量 = 主爆 × 0.5）
        ///   ⇒ 印记永远被**最小的那一下**用掉，主爆的数字一点没变 ⇒ 玩家读成"不生效" ✓
        ///
        /// ── 改法：拆成两步（都在 `World.DealDamage` 这一个闸门里，顺序固定）──
        ///   ① `MarkMultiplier`（本方法）：**所有**伤害源都读它 ⇒ 持续伤害也吃倍率 ✓
        ///   ② `ConsumeMark`：只在"**这一次真的把印用掉**"的那一路、并且**真打中了**之后调 ✓
        /// 谁来声明"自己能不能摘印"：`DamageRequest.NoMarkConsume`（默认 false = 能）✓
        /// 于是规则一句话：**任何伤害都吃倍率，只有直接伤害才摘印** ✓
        ///
        /// ★ 有限步承诺（不变）★
        ///   一次 `TryGetValue`，没有循环、没有递归、不改任何状态 ✓
        /// </summary>
        public static float MarkMultiplier(int enemyId)
            => Marks.TryGetValue(enemyId, out Mark m) ? 1f + m.Bonus : 1f;

        /// <summary>
        /// 取用一次印记：有印 ⇒ 返回放大倍率（1+bonus）并**消耗掉**；没印 ⇒ 返回 1。
        /// 调用点**只有一个**：`World.DealDamage`（唯一伤害闸门）——
        ///   跨伤害源是灼印的全部意义，放在闸门才能保证"任何来源都吃印" ✓
        ///
        /// ★ v286：调用时机变了（这是本轮的修复本体）★
        ///   改前：进闸门就调 ⇒ 连"被闪避""被格挡""BOSS 入场免伤"都会把印摘掉 ✗
        ///   现在：`Resolve` 之后、判过 `Dealt &gt; 0` 才调 ⇒ **只有真打中的直接伤害才摘印** ✓
        ///   倍率本身由 <see cref="MarkMultiplier"/> 出（那一刻已经乘进 `RawValue`）✓
        ///
        /// ★ 有限步承诺（本轮加固）★
        ///   这里**没有 `while`、没有递归、没有"递减到 0"的循环**：
        ///   一次 `TryGetValue` + 一次 `Remove`，无论层数是多少都是 O(1) 且必退出 ✓
        ///   层数只影响返回的读数（见 `MarkStacks`），**不影响**这次的倍率 ⇒
        ///   "层数很大 ⇒ 消耗要循环很多次"这条隐患在结构上不存在 ✓
        /// </summary>
        public static float ConsumeMark(int enemyId)
        {
            if (!Marks.TryGetValue(enemyId, out Mark m)) return 1f;
            Marks.Remove(enemyId);
            return 1f + m.Bonus;
        }

        // ═════════════════════════ 护壤 ═════════════════════════
        private static float WardLeft;      // 剩余秒数
        private static float WardPoints;    // 还没退掉的那部分护盾

        /// <summary>护盾还在吗（表现层画环用）。</summary>
        public static bool WardActive => WardLeft > 0f;
        public static float WardRemain => Mathf.Max(0f, WardLeft);

        /// <summary>给玩家上盾（重复获得只刷新时间，不叠层 —— 与用户口径"2 点、持续 3 秒"一致）。
        /// ★ v308：同时放一层**六边形护盾视觉**（用户：「在玩家周围生成一圈六边形护盾面片」）★
        ///   · 视觉与逻辑**同一次调用**触发 ⇒ 不会出现"盾亮了但没给盾"或反之 ✓
        ///   · 颜色取肥料那一路的基础外观（`PluginFx.Or(default, Fertilizer)`）：本方法拿不到
        ///     `pm`（调用点只传两个数），所以用这一路的**默认配色**，而不是瞎写一个 RGB ✓
        ///   · 纯表现：不改任何数值、不占用逻辑层的池（渲染体全在 `AttackVfx` 的池里复用）✓
        /// </summary>
        public static void GrantWard(PlayerActor p, float shield, float seconds)
        {
            if (p == null || shield <= 0f) return;
            // 先把上一轮还没退掉的那部分收回去，再按新值给（避免连炸叠成 4、6、8 点）
            if (WardPoints > 0f) p.Health.Shield = Mathf.Max(0f, p.Health.Shield - WardPoints);
            WardPoints = shield;
            p.Health.Shield += shield;
            WardLeft = Mathf.Max(WardLeft, seconds);
            AttackVfx.SpawnWardHexes(p.Pos, 1.55f, PluginFx.Or(default(FxSpec), WeaponSlot.Fertilizer));
            Bus.Publish(new FloatingTextEvent
            {
                Pos = p.Pos, Text = $"护盾 +{shield:0}", Kind = PopupKind.DamageToPlayerShield, Scale = 0.9f,
            });
        }

        /// <summary>每局开始清空（埋着的雷 / 印记 / 护盾计时都属本局）。</summary>
        public static void Reset()
        {
            Mines.Clear();
            Marks.Clear();
            TickWrites.Clear();
            _ticking = false;
            WardLeft = 0f;
            WardPoints = 0f;
            MineDroppedTotal = 0;   // ★ v305：累计埋雷数是"本局"的读数，跨局不累计 ✓
            MineCapNow = MineCap;   // 容量读数也回到保底值（还没有任何投掷决定过容量）
        }

        /// <summary>
        /// 每固定帧推进（`RunSession.FixedTick`，与 `Dots.Tick` 同一批）。
        /// <paramref name="explode"/> 由 `RunSession` 传进来 —— 这样本类不认识 `ExplodeAt`，
        /// 也就不会和"爆炸里再排雷"绕成环（爆炸的来源永远只有 RunSession 一条）✓
        ///
        /// ★ v308：`explodeWithMult` 的**返回值**是"这一次爆炸真实造成的伤害"
        ///   （`RunSession.LastBlastDealt`，= 主爆 + 子爆里每个敌人的 `DamageResult.Dealt` 之和）
        ///   ⇒ 「伏雷」弹字因此能印出实际数值（见 `TickMines` 里"先炸再发字"的那一段）✓
        ///
        /// ★ 本轮加的三道结构闸（都写在最外层的 `try/finally` 里）★
        ///   ① **重入闸** `_ticking`：伏雷自爆的回调链若再次回到本方法 ⇒ 直接返回。
        ///      改前没有任何闸，一旦有人在回调里再调一次 `Tick`（`ExplodeAt` 里将来加一句就够），
        ///      就是**同帧的递归展开** ⇒ 层数/雷数一大就是主线程卡死 ✓
        ///   ② **不在遍历中回写字典**：`foreach (Marks)` 里只读、只往 `TickWrites` 里收，
        ///      写回与删除都在 `foreach` **外面**做 ⇒ 不依赖 BCL"给已存在的 key 赋值不涨版本号"
        ///      这条实现细节（那是真的，但不该被逻辑层当成前提）✓
        ///   ③ 全部是 `for`/`foreach` 的**单遍**扫描，没有 `while`、没有递归 ✓
        /// </summary>
        public static void Tick(float dt, World world, PlayerActor player,
                                System.Func<Vector2, PlayerActor, float, float> explodeWithMult)
        {
            if (_ticking) return;                    // ① 重入闸
            _ticking = true;
            try
            {
                TickMarks(dt, world);
                TickWard(dt, player);
                TickMines(dt, world, player, explodeWithMult);
            }
            finally { _ticking = false; }
        }

        private static void TickMarks(float dt, World world)
        {
            if (Marks.Count == 0) return;
            // ── 第一遍：只读 + 收集（**绝不在这里写 `Marks`**）──
            TickWrites.Clear();
            var expired = ListPool;
            expired.Clear();
            foreach (KeyValuePair<int, Mark> kv in Marks)
            {
                Mark m = kv.Value;
                m.Left -= dt;
                if (m.Left <= 0f || world == null || world.FindEnemyById(kv.Key) == null)
                {
                    expired.Add(kv.Key);
                    continue;
                }
                TickWrites.Add(new KeyValuePair<int, Mark>(kv.Key, m));
            }
            // ── 第二遍：写回（每行一次，O(带印敌人数)）──
            for (int i = 0; i < TickWrites.Count; i++)
                Marks[TickWrites[i].Key] = TickWrites[i].Value;
            // ── 第三遍：摘掉到期 / 目标已消失的 ──
            for (int i = 0; i < expired.Count; i++) Marks.Remove(expired[i]);
        }

        private static void TickWard(float dt, PlayerActor player)
        {
            if (WardLeft <= 0f) return;
            WardLeft -= dt;
            if (WardLeft > 0f || player == null || WardPoints <= 0f) return;
            // 只收回"还没被打掉"的那部分：玩家被打穿的盾不该倒扣
            float take = Mathf.Min(WardPoints, player.Health.Shield);
            player.Health.Shield = Mathf.Max(0f, player.Health.Shield - take);
            WardPoints = 0f;
        }

        private static void TickMines(float dt, World world, PlayerActor player,
                                      System.Func<Vector2, PlayerActor, float, float> explodeWithMult)
        {
            if (Mines.Count == 0) return;
            for (int i = Mines.Count - 1; i >= 0; i--)
            {
                Mine m = Mines[i];
                m.FuseLeft -= dt;
                // ★ v309：**地面脉冲光点**（用户：「埋雷期间地面加微弱的脉冲光点」）
                //   放在"上膛判定"**之前** ⇒ 还没上膛的雷也看得见（那正是玩家最需要看见的时候）。
                //   每颗雷各自计时、固定帧递减 ⇒ 确定性；`PulseLeft` 落到 0 就放一次并补回一个周期。
                m.PulseLeft -= dt;
                if (m.PulseLeft <= 0f)
                {
                    m.PulseLeft += MinePulseInterval;
                    if (m.PulseLeft <= 0f) m.PulseLeft = MinePulseInterval;   // 掉帧兜底（dt 很大时）
                    AttackVfx.SpawnMinePulse(m.Pos);
                }
                if (m.ArmLeft > 0f)
                {
                    m.ArmLeft -= dt;
                    Mines[i] = m;
                    continue;                                   // 还没上膛：踩了也不响
                }
                bool stepped = false;
                if (world != null)
                {
                    for (int e = 0; e < world.Enemies.Count; e++)
                    {
                        EnemyActor en = world.Enemies[e];
                        if (en == null || en.IsDead) continue;
                        if ((en.Pos - m.Pos).sqrMagnitude <= m.Sense * m.Sense) { stepped = true; break; }
                    }
                }
                bool fuseOut = m.FuseLeft <= 0f;
                // ★★ v311 修 bug：**伏雷 + 黑洞的前摇**（用户：「黑洞…搭配伏雷会导致前摇消失」）★★
                //
                // ── 为什么前摇要挂在"雷的引信"上，而不是像投掷那样挂在 `_pendingBlasts` 上 ──
                //   `RunSession.ArmBlast` 装了伏雷就**只排雷、不排 `_pendingBlasts`**（否则 0.5 秒后
                //   照旧自动爆，伏雷就退化成一枚延迟肥）⇒ 那条"`b.T` 跨过 `BlackHoleWindup`"
                //   的黑洞判据**一次都不会成立** ⇒ 前摇彻底消失 ✗（用户报的就是这个。）
                //   而雷**本来就有自己的引信**：上膛 `ArmTime` + 自爆 `MineFuse`(6s)。
                //   那 6 秒就是它的前摇 ⇒ **不需要、也不该再额外加 1 秒**。
                //
                // ── 两处挂点（对应两种爆法）──
                //   ① **超时自爆**（下面这一段）：`FuseLeft` 跨过 `BlackHoleWindup`(1.0s) 的那一帧
                //      开漩涡 + 开吸附场 ⇒ 玩家的观感是"最后一秒雷开始转、把人往中心拖，然后炸"，
                //      与投掷那一路**同构**（同一个常量、同一条吸附曲线、同一套漩涡）✓
                //      `BhWindup` 保证**每颗雷只开一次**（跨越式判据在掉帧时可能连中两帧）
                //   ② **被踩中爆**（后面 `!m.BhWindup` 那一支）：踩中是**瞬时事件**，
                //      没有"前 1 秒"可以挂 ⇒ 只能"炸的同时闪一次漩涡"。
                //      ⚠️ 刻意**不**把爆炸推迟 1 秒：那会让"踩中就炸"的反馈晚一整个前摇，
                //         伏雷就不再是伏雷了（这是本轮判定的取舍，见交付说明）。
                //   ⚠️ 顺序：**先开漩涡、后结算伤害** —— `explodeWithMult` 里
                //      `RunSession.ExplodeAt` 会先 `BlackHolePull`（把范围内敌人收口到 8%R）再算伤害 ✓
                if (m.BlackHole && !m.BhWindup && m.FuseLeft > 0f
                    && m.FuseLeft - dt <= PluginExtra2.BlackHoleWindup)
                {
                    m.BhWindup = true;
                    PluginExtra2.BlackHoleMineWindup(m.Pos, m.Radius, m.Spots);
                }
                if (!stepped && !fuseOut) { Mines[i] = m; continue; }
                // ★ 先摘再炸：爆炸链里无论发生什么，这颗雷都不会被第二次触发 ✓
                Mines.RemoveAt(i);
                // ★ v311：被**踩爆**（或在前摇开始前就提前炸）⇒ 没有前摇可等，当场闪一次
                //   （`BhWindup` 已经开过就不重复画，否则同一次爆炸会有两套漩涡抢 `Bit` 池 ✗）
                if (m.BlackHole && !m.BhWindup) PluginExtra2.BlackHoleMineBurst(m.Pos, m.Radius);
                float mult = stepped ? m.HitMult : m.MissMult;
                // ★★ v308：**顺序反转 —— 先炸、再发字** ★★
                //   改前弹字发在 `explodeWithMult` **之前** ⇒ 那一行只能印名字（"伏雷"），
                //   而"这一颗雷到底打掉了多少血"要等爆炸算完才知道 ✗
                //   现在 `explodeWithMult` 的返回值就是**这一次爆炸真实造成的总伤害**
                //   （`RunSession.LastBlastDealt` = 主爆 + 全部子爆里每个敌人的 `DamageResult.Dealt` 之和，
                //     全程是"真正扣掉的血/盾"口径，不是估算）✓
                //   ⇒ 发字挪到后面，文本就能带上那个数：`伏雷 -18` / `伏雷！ -27` ✓
                //   ⚠️ 仍然**只有真的一次爆炸**才走到这里（摘雷 → 炸 → 发字），
                //     顺序变化不影响任何玩法状态：`RemoveAt` 已经在炸之前 ✓
                float dealt = explodeWithMult != null ? explodeWithMult(m.Pos, player, mult) : 0f;
                // 声音/弹字：踩中与超时是两种事件，玩家要能从反馈上分清
                //   ★ v308：`Dealt < 0.05`（没罩到人 / 全被闪避）时 `DamageLabel` 返回 null ⇒ 不发弹字 ✓
                string text = PluginHitFx.DamageLabel(stepped ? "伏雷！" : "伏雷", dealt);
                if (text != null)
                {
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = m.Pos, Text = text,
                        Kind = stepped ? PopupKind.Crit : PopupKind.DamageToEnemy,
                        Scale = stepped ? 1.15f : 0.9f,
                    });
                }
            }
        }

        private static readonly List<int> ListPool = new List<int>(16);
    }
}
