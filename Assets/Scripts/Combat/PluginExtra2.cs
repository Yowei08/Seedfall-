using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v290：**7 个全新机制的执行点**（`PluginExtra2`，替代已删除的 `PluginExtra.cs`）★★
    ///
    /// ── 为什么是**新文件**，不复活 `PluginExtra.cs` ──
    ///   上一轮那 12 件已经整套回滚（数据 / 枚举 / 聚合 / 自检 / l10n / 解锁表），
    ///   文件也删了。这一轮的 7 件**不是它们的替代品**（判定输入、结算位置、层数读法都不同），
    ///   写回同一个文件会让"某一行到底是哪一轮加的"再也说不清 ⇒ 新开一支 ✓
    ///
    /// ── 三组机制，三个执行点（各归其位，不混）──
    ///   ① **闸门组**（`BeforeResolve` / `AfterResolve`）—— 感电 / 狂暴 / 黑洞
    ///      位置与「灼印」同源：`World.DealDamage`（`Actors.cs`）是全工程"每一下伤害都必经"的主干，
    ///      "按目标血量加伤"与"命中后电周围"只有放在这里才跨得住来源（近战 / 激光 / 剑气 /
    ///      主爆 / 子爆 / 毒区 tick 七条路一一都走它）✓
    ///   ② **几何组**（`GiantInnerRatio`）—— 巨型
    ///      挥击判定形状。判据本体只有一处（`RunSession.MeleePointHit`），本类只提供"内径比例"，
    ///      ⇒ 不会出现"判定是环、特效是半圆、自检读第三个值"那种三份真相 ✗
    ///   ③ **动作组**（`OnWaterSpent` / `OnCanAttack` / `HarvestYieldScale` / `BlackHolePull`）——
    ///      臭水沟 / 海浪 / 锋利 / 黑洞的"拉拢"
    ///      它们挂在**具体的那一次出手**上（扣水那一下 / 喷水那一下 / 收割那一格 / 爆炸那一下），
    ///      与闸门组正交：闸门管"这一下多重"，动作组管"这一下顺带做了什么" ✓
    ///
    /// ── 确定性（硬纪律，与上一轮同一条）──
    ///   本类**不消费任何随机数**（不用 `Pcg32`、不用 `World.Rng`、不用 `Random`）⇒
    ///   钉种子的自检读数不会因为装了这 7 件而漂移 ✓
    ///   · "每 N 点水" = **累计计数**（`_waterSpent`），不是概率
    ///   · "每层" = **层数乘**（`stacks × 常量`），不是掷骰
    ///   · 香蕉皮的落点 = 由 `_peelCount` 派生的**整数哈希**（`PeelHash01`）在**玩家前方扇形**里取点
    ///     （★v313；改前是绕玩家一圈的黄金角），不是 `UnityEngine.Random`、也不消费 `Pcg32`
    ///     ⇒ 同一个序号永远同一个落点，见 `DropPeel` ✓
    ///
    /// ── 有限步（防递归）──
    ///   感电会**再次**调 `World.DealDamage` ⇒ 派生伤害必须被一条闸挡住：
    ///   `_emitting` 只在本类发起派生伤害期间为真，期间 `BeforeResolve` / `AfterResolve` 直接返回
    ///   ⇒ 展开深度恒为 1（不会"电到的人再电别人"）✓
    ///
    /// ── 数值参数的唯一真相 ──
    ///   全部集中在本文件顶部的常量区（`Plugins2` 段）。
    ///   `plugins.json` 里这 7 件的 `amount / amount2` 只是**镜像值**（给策划读、给 UI 读），
    ///   运行时一律读这里的常量 ⇒ 改数值只改一处 ✓
    /// </summary>
    public static class PluginExtra2
    {
        // ════════════════════════════════════════════════════════════════════
        //  ★ 数值参数（唯一真相；改数值只改这一段）
        // ════════════════════════════════════════════════════════════════════

        // ── ① 锄头 · 感电（hoe_shock）──
        /// <summary>每一层电击**额外的**目标数（3 层 ⇒ 3/6/9 人）。</summary>
        public const int ShockTargetsPerStack = 3;
        /// <summary>每个被电到的敌人吃"这一刀伤害"的比例（0.10 = 10%）。</summary>
        public const float ShockDamagePct = 0.10f;
        /// <summary>电击的搜索半径（米）—— 以**主目标**为圆心，不是以玩家为圆心。</summary>
        public const float ShockRadius = 3.5f;
        /// <summary>一次电击最多打几个（3 层 × 3 = 9，取 12 留余量；同时是候选缓冲的上界）。</summary>
        public const int ShockMaxTargets = 12;
        /// <summary>折线电流离地多高（米）—— 与弹字高度（1.05）错开一点，免得被字压住。</summary>
        public const float ShockFxHeight = 0.90f;
        /// <summary>★ v307：被电到的敌人身上那颗**电光球**的高度（米）。与折线同高
        /// ⇒ 球正好长在电流的端点上，读作"电流打中的那一点在发光" ✓</summary>
        public const float ShockOrbY = ShockFxHeight;
        /// <summary>★ v307：电光球半径（米）。0.26 —— 比敌人（半径 ~0.36）小一圈，
        /// 是"身上一颗亮点"，不是"把人包起来" ✓</summary>
        public const float ShockOrbRadius = 0.26f;
        /// <summary>★ v307：电光球的寿命（秒）。用户给的就是 0.12 ⇒ "一次短促的"。</summary>
        public const float ShockOrbLife = 0.12f;
        /// <summary>★ v307：电光球的颜色。电青偏白（比折线更白一档 ⇒ 它是"最亮的那一点"）。</summary>
        public static readonly Color ShockOrbColor = new Color(0.78f, 0.95f, 1f, 1f);
        /// <summary>★ v309：**电流起点**那颗光球的半径倍率（相对 `ShockOrbRadius`）——
        /// 大 15% ⇒ 与远端（被打中的敌人身上）那颗分得出主次。</summary>
        public const float ShockSourceOrbScale = 1.15f;
        /// <summary>★ v309：**电流起点**那颗光球的颜色（比远端更白一档 = 电流的源头）。</summary>
        public static readonly Color ShockSourceOrbColor = new Color(0.90f, 0.99f, 1f, 1f);

        // ── ② 锄头 · 狂暴（hoe_frenzy）—— ★ 是"敌人残血时**你打它**更疼"，不是"你残血" ★ ──
        /// <summary>目标生命低于该比例时生效（0.40 = 40%）。</summary>
        public const float FrenzyBelowFraction = 0.40f;
        /// <summary>生效时**每层**的伤害加成比例（0.10 = 10%/层 ⇒ 2 层 20%）。</summary>
        public const float FrenzyPctPerStack = 0.10f;
        /// <summary>狂暴"赤红窄环"的半径 / 寿命 / 最小间隔（秒）—— 只影响表现，不参与判定。</summary>
        public const float FrenzyFxRadius = 0.80f;
        public const float FrenzyFxLife = 0.18f;
        public const float FrenzyFxInterval = 0.20f;

        // ── ③ 锄头 · 巨型（hoe_giant）──
        /// <summary>整圈判定的**内径 = 外径 × 本值**（0.35）。外径沿用既有的 `MeleeRingOuter(range)`。</summary>
        public const float GiantInnerRatio = 0.35f;

        // ── ④ 水壶 · 臭水沟（can_sewer）──
        /// <summary>每消耗这么多水掉一次香蕉皮（20 点）。</summary>
        public const int SewerWaterPerDrop = 20;
        /// <summary>**每层**额外掉几个（1 ⇒ 1 层 1 个 / 2 层 2 个）。</summary>
        public const int SewerDropsPerStack = 1;
        /// <summary>每个香蕉皮吃"水壶伤害"的比例（0.60 = 60%）。</summary>
        public const float SewerDamagePct = 0.60f;
        /// <summary>
        /// 香蕉皮的**踩踏判定半径**（米）—— "巨大"指的是它罩住一整片，不是贴图大。
        /// ★ v312：**1.7 → 2.4**（+41.2%）。用户原话：「**污水掉落的香蕉皮攻击范围扩大**」。
        ///   唯一消费点是 `LandPeel` 的判定循环（`rr = SewerPeelRadius + e.Radius`）。
        ///   ⚠️ `AttackVfx.SpawnMudSplash` 虽然也接了这个半径，但它只用它把溅泥初速乘一个
        ///   `min(1, R×0.5 + 0.6)` 的系数 —— **1.7 与 2.4 都已经饱和在 1.0**
        ///   ⇒ 溅泥那一处的长相**不随本次改动变化**（不该变：脏东西还是那么多，只是范围大了）✓
        ///   ⚠️ 视觉侧：皮模型 `AttackVfx.PeelScale` = 2.8（网格半径 0.62 ⇒ 世界半径 **1.74m**）。
        ///   ★ v313：落地那圈**深绿渍**（原 `AttackVfx.PeelStainRadius` = 2.25）已按用户要求
        ///   **整块删除**（「掉落特效的绿色判定框删除」）⇒ 现在**只剩皮模型**这一个范围读法，
        ///   而 1.74 &lt; 2.4 ⇒ 皮看起来比真正会被踩到的范围**小 27.5%**，
        ///   这个后果用户已知情并明确选择删（不是遗漏，也不是待修）✓
        /// </summary>
        public const float SewerPeelRadius = 2.4f;

        // ── 香蕉皮的**落点**（★ v313：从"玩家周围一圈"改成"玩家前方一个扇形"）──
        /// <summary>
        /// 落点扇形的**半角**（度）—— 以玩家朝向（`p.AimDir`）为中轴，左右各 35°（张角 70°）。
        /// · 为什么不更大：90°+ 时两侧的落点已经绕到玩家侧面，读起来又回到"在周围撒"✗
        /// · 为什么不更小：20° 以下每一颗几乎都落在同一条线上，"随机"看不出来 ✗
        /// · 为什么是 35°：2m 处的横向散布 = `2·tan35° ≈ ±1.4m` ⇒ 一眼看得出是"前方一片"✓
        /// </summary>
        public const float SewerPeelHalfAngle = 35f;
        /// <summary>
        /// 落点距玩家的**最近**距离（米）。1.2 —— 玩家自身半径 0.45
        /// ⇒ 最近那一颗的中心也在脚外 **0.75m**，不会"掉在脚底下"。
        /// ⚠️ 皮模型的世界半径是 1.74 ⇒ 近端那颗的**模型**仍会压到玩家脚边
        /// （皮本来就比人宽，这是"扔到跟前"的读法，不是"落在脚下"✗）
        /// </summary>
        public const float SewerPeelMinDist = 1.2f;
        /// <summary>
        /// 落点距玩家的**最远**距离（米）。2.6 —— 皮是"扔出去的"，不是"扔到屏幕外"：
        /// 2.6 + 判定半径 2.4 ⇒ 一颗最多罩到身前 **5.0m**（玩家往前两米扔一个、盖住前方五米）。
        /// ⚠️ 2.6 &gt; 2.4 ⇒ **最远的那几颗，判定圈够不到玩家自己**；
        ///   本插件的皮只伤敌人（`LandPeel` 只遍历 `_world.Enemies`）⇒ 不存在自伤/自卫问题 ✓
        /// </summary>
        public const float SewerPeelMaxDist = 2.6f;
        /// <summary>
        /// 角度的**分层格数**（★ v313）。第 `k` 颗固定落在第 `k % 5` 格里，再在格内抖一点
        /// ⇒ 同一次触发的 1~3 颗**必然**分在不同格（不会两颗叠在同一处），格内仍随机 ✓
        /// 为什么是 5：一次掉 `drops`(1~3) 颗、跨次按 `drops` 步进，1/2/3 与 5 都互质
        /// ⇒ 走满 `5/ gcd(5,drops) = 5` 次就把 5 格均匀走一遍，没有任何一格被饿死 ✓
        /// 格子宽度 = 70°/5 = 14°，格内抖动后看不出"格子"的存在 ✓
        /// </summary>
        public const int SewerPeelSectors = 5;
        /// <summary>
        /// ★ v304：香蕉皮**从天上落到地面用的秒数** —— 与视觉**同一个常量**
        /// （`AttackVfx.PeelFallSeconds`：那里用它算下落位置，这里用它算"什么时候结算伤害"）。
        /// ★ 为什么必须共用这一个数：用户报的是"逻辑与视觉是否错位"。
        ///   若两边各写一个数（比如逻辑 0、视觉 0.45s），敌人会在皮还在 7 米高时掉血 ⇒
        ///   玩家读到的是"这个插件不落地也生效" ✗ 现在两边读同一个值 ⇒ 不可能错位 ✓
        /// </summary>
        public const float PeelFallSeconds = AttackVfx.PeelFallSeconds;
        /// <summary>同时在飞/在躺的香蕉皮上限（= `AttackVfx.PeelPool`）⇒ 池里永远有格子可用。</summary>
        private const int PeelQueueMax = AttackVfx.PeelPool;
        /// <summary>"绿浑浊"的激光配色（`PluginFx.For` 读它，见 `PluginFx` 的 Sewer 分支）。</summary>
        public static readonly Color SewerCore = new Color(0.36f, 0.62f, 0.26f, 0.95f);
        /// <summary>同上，霓虹边缘（更黄一点的沼绿）。</summary>
        public static readonly Color SewerRim = new Color(0.62f, 0.78f, 0.22f, 1f);

        // ── ⑤ 水壶 · 海浪（can_wave）──
        /// <summary>触发时的移速加成比例（0.50 = +50%）。</summary>
        public const float WaveSpeedPct = 0.50f;
        /// <summary>加成持续秒数（1.0）。</summary>
        public const float WaveSeconds = 1f;
        /// <summary>浪的基础半径（米）。**每层 +50% 宽度** ⇒ 1 层 4.5 / 2 层 6.75 / 3 层 9.0。
        /// ★ v313：**3.0 → 4.5**（用户原话「海浪判定扩大」，走 v312 交付报告里的 (b) 方案值）。
        ///   · 判定 = 以玩家为心、半径 `pm.WaveRadius` 的**圆**（`WeaponSystem:404` 写入）
        ///     ⇒ 面积 `π·r²`：28.27 → **63.62 m²**（1 层），**×2.25**（半径 ×1.5 ⇒ 面积 ×1.5²）
        ///   · 唯一消费点仍是 `OnCanAttack` 里那一句 `radius = pm.WaveRadius`；
        ///     `WaveDamagePct`(0.30) / `WaveSpeedPct`(0.50) / `WaveSeconds`(1.0) **一个数都没动** ✓
        ///   · 视觉那一侧（`WaveFxAdvance` = 4.0）**一个数都不用改**：推进距离本来就夹在
        ///     `radius − AttackVfx.WaveWallBulge`，半径变大后夹子自动放开到 4.0（见 `OnCanAttack`）✓
        /// </summary>
        public const float WaveRadiusBase = 4.5f;
        /// <summary>每层额外加宽的**比例**（0.50）。</summary>
        public const float WaveWidthPerStack = 0.50f;
        /// <summary>被浪扫到的敌人吃"水壶伤害"的比例（0.30）—— 它不是主伤害，是"顺带推一下"。</summary>
        public const float WaveDamagePct = 0.30f;

        // ── ⑧ 水壶 · 潮汐（can_tide）—— ★ v305：推开改成"按米位移"，不再按速度 ──
        /// <summary>
        /// 一次潮汐把目标推开**用多少秒**（0.25）。
        /// ★ 为什么需要这个数（v305 修的是"实际距离不对"）：
        ///   改前 `e.KnockbackVel += away * (m.TidePush * 6f)` —— 那是一个**初速**，
        ///   而 `Enemies.MoveStep` 每帧还会把敌人朝玩家方向推 `speed`（基础兵 2.7 m/s），
        ///   并让 `KnockbackVel` 以 λ=7 指数衰减 ⇒ 真的位移只有
        ///     `∫ v0·e^{-7t}dt = v0/7 = (1.2×6)/7 = **1.029 m**`（连 1.2 m 都不到），
        ///   而同时敌人自己走了 2.7 × 0.5s ≈ 1.35 m **朝玩家回来** ⇒ 净位移 ≈ **0**。
        ///   ⇒ 玩家看到的"推开"根本不成立，卡面写的 1.2 m 一次都没出现过 ✗
        /// 改后：推开走**位移单**（`TickTideJobs` 每帧直接把 `Pos` 推 `Speed·dt` 米），
        ///   并且这 0.25 秒内该敌人的**自主移动被压住**（`IsTideHeld` ⇒ `MoveStep` 里 `v = 0`）
        ///   ⇒ 净位移 = 卡面值（逐帧累加，误差 ≤ 1 帧×速度 = 0.08 mm）✓
        /// </summary>
        public const float TidePushSeconds = 0.25f;
        /// <summary>同时存在的潮汐位移单上限（一波水柱最多罩几个人；满了丢最老的一单）。</summary>
        private const int TideJobMax = 16;

        // ── ⑦ 肥料 · 黑洞（fert_blackhole）──
        /// <summary>★ v310：拉拢终点的半径比例 **0.28 → 0.08**（用户：「吸附范围内所有敌人到爆炸中心」）。
        ///   0.28 时人只是"进到爆心的内四分之一"，读起来仍像"被推开了一点"；0.08 才是
        ///   **贴在中心**（爆心半径的 8%，3 米半径 ⇒ 24cm）。
        ///   为什么不是 0：全塞到圆心会让多个敌人**完全重叠**（`BlastDamageAt` 判的是圆心距，
        ///   拉到 0.08R 已经必然在范围内，且 `0.08R ≈ 24cm` 与敌人半径同量级 ⇒ 模型不会插成一坨）✓
        ///   ⚠️ 唯一消费点有两处，都读这个常量：`TickBlackHole` 的 `keep`（每帧不再施力）
        ///   与 `BlackHolePull` 的 `want`（爆炸那一帧的收口）⇒ 改一处就够 ✓</summary>
        public const float BlackHolePullToFraction = 0.08f;
        /// <summary>★ v310：吸引阶段的时长（秒）—— **0.25 → 1.00**（用户：「在爆炸前增加 1 秒前摇」）。
        /// 它同时是"漩涡特效的寿命"，所以改这一个数 = 前摇、吸附窗口、漩涡时长三者一起变 ✓
        /// ⚠️ **它必须严格小于引信**（`BlackHoleFuseSeconds`），否则
        ///   `RunSession.TickPendingBlasts` 那条"跨过这一刻"的判据**永远不会成立**
        ///   （判据是 `b.T > Windup && b.T − dt ≤ Windup`，而 `b.T` 从引信长度开始往下走）✗</summary>
        public const float BlackHoleWindup = 1.00f;
        /// <summary>★ v310：装了黑洞时**这一次投掷的引信长度**（秒）= 前摇 + 一帧余量。
        ///   为什么不是恰好 1.00：`TickPendingBlasts` 用的是"上一帧在 1.0 以上、这一帧在 1.0 以下"
        ///   的**跨越判据**；若 `T` 恰好等于 1.00，第一次判断 `1.00 > 1.00` 为假 ⇒ 前摇一帧都不会开 ✗
        ///   取 1.02（≈1.2 帧的余量）⇒ 漩涡在投掷后第 2 个固定帧开、正好持续 1.00 秒、爆炸落在 1.02 秒 ✓
        ///   ⚠️ **只对装了黑洞的投掷生效**（`RunSession.ArmBlast` 里按 `pm.BlackHole` 选）——
        ///   没装黑洞的肥料仍然是原来的 0.5s 引信，手感一点没变 ✓</summary>
        public const float BlackHoleFuseSeconds = BlackHoleWindup + 0.02f;
        /// <summary>米 → 击退速度的换算（与「潮汐」/「引力」同一个系数 6）。</summary>
        private const float KnockbackPerMeter = 6f;
        /// <summary>★ v308：持续吸附的**基础拉力**（米/秒）。终点前的那一记"收口"再乘 `BlackHolePullPower`。</summary>
        private const float BlackHoleCruiseSpeed = 5.2f;
        /// <summary>★ v308：末段加速倍率（`k²` 曲线把拉力推到 1 + 本值 倍）。</summary>
        private const float BlackHolePullPower = 2.6f;

        /// <summary>
        /// ★ v308：**正在吸的黑洞**（前摇最后 `BlackHoleWindup` 秒）。
        ///
        /// ── 为什么要一个"活跃区"而不是只在爆炸时拉一次 ──
        ///   用户要的是「**持续吸附**：在爆炸前摇的 0.25 秒内**连续**把敌人往中心拉
        ///   （不只是最后一瞬），读作'被吸进去'」。一次性拉拢（v290 的做法）在 0.25s 里
        ///   只发生一帧 ⇒ 敌人是"瞬移"过去的，中间没有任何"被拖行"的过程 ✗
        ///   ⇒ 现在：`BlackHoleTelegraph`（前摇起点，`RunSession.TickPendingBlasts` 里那一帧）
        ///     **开一个场地**，`AttackVfx` 画三层特效；之后**每个固定帧**由 `Tick(dt)` 推进，
        ///     每帧给范围内敌人一个**朝圆心的速度**（`KnockbackVel` 通道，与潮汐/引力同一套）。
        ///
        /// ── 确定性（这条最要紧）──
        ///   · 没有任何随机数：方向 = 归一化的 `center - e.Pos`，大小 = 固定曲线
        ///     `cruise × (1 + power·k²)`，其中 `k = 已经吸了多久 / 前摇时长`（0→1）
        ///   · 只读固定 tick 的 `dt` ⇒ 同一串输入在回放里逐帧一致 ✓
        ///   · **直接赋值**而不是累加（`KnockbackVel = 方向 × 速度`）：累加会让衰减系数 λ=7
        ///     与帧率耦合出一个"速度上不封顶"的隐患；赋值后每帧的位移是**解析可算**的 ✓
        ///   · ★★ v311：从**一个槽位**改成 **`BlackHoleSlots` 个槽位** ★★
        ///     改前只有一个 `_bhOn/_bhCenter/_bhRadius/_bhLeft`，"后者覆盖前者"。
        ///     但连环（`fert_chain`，最多 3 落点）是**同一帧**开 3 个黑洞 ⇒ 覆盖的结果是
        ///     "只有最后处理的那一颗在吸"，其余落点的吸引被吞掉 ✗
        ///     （用户报的「黑洞搭配连环会只有末端可以触发黑洞」正是这一族症状之一：
        ///       视觉被"按帧去重"吞掉、吸附被"单槽覆盖"吞掉，两头都只剩一颗。）
        ///     现在每个落点占**自己那一格**（下标 = 落点序号），三格各自计时、各自施力 ✓
        ///     ⚠️ 定长数组（不是 `List`）+ 运行期零分配；下标一律经 `ShotSlot` 夹在界内 ✓
        /// </summary>

        /// <summary>★ v311：同时最多几个黑洞在吸 = 连环的最大落点数。
        /// `plugins.json` 里 `fert_chain.maxStack = 2`、`amount = 1` ⇒ `BlastCount ≤ 3`
        /// （与 `PluginField.MineCapFor` 的取数依据是同一个数）✓</summary>
        public const int BlackHoleSlots = 3;

        private static readonly bool[] _bhOn = new bool[BlackHoleSlots];
        private static readonly Vector2[] _bhCenter = new Vector2[BlackHoleSlots];
        private static readonly float[] _bhRadius = new float[BlackHoleSlots];
        private static readonly float[] _bhLeft = new float[BlackHoleSlots];   // 还要吸多久（秒）

        /// <summary>★ v311：伏雷**被踩爆**时的漩涡寿命（秒）。
        /// 踩中是瞬时事件 ⇒ 没有 1 秒前摇可等，只能"炸的同时闪一次"。
        /// 取 0.30s：比 `BlackHoleWindup`(1.0) 短得多（它不是前摇，是"黑洞炸开了"的记号），
        /// 又长到能看清中心暗球长大。⚠️ 它要和爆炸那 44 格 `Bit` **同帧共存** ⇒
        /// 必须用"最省档"（17 格，池账见 `AttackVfx.SpawnCollapseRing`）✓</summary>
        public const float BlackHoleMineBurstLife = 0.30f;

        /// <summary>这一刻是否**至少有一个**黑洞在吸（读数 / 探针用）。</summary>
        public static bool BlackHoleActive
        {
            get
            {
                for (int i = 0; i < BlackHoleSlots; i++) if (_bhOn[i]) return true;
                return false;
            }
        }

        /// <summary>落点序号 → 槽位。`BlastCount` 实际不会超过 `BlackHoleSlots`，
        /// 这里仍然夹一次 ⇒ 任何越界输入都不会写坏数组 ✓</summary>
        private static int ShotSlot(int spot)
            => spot <= 0 ? 0 : (spot < BlackHoleSlots ? spot : BlackHoleSlots - 1);

        /// <summary>清空**所有**吸附场（跨会话 / 世界没了 / 爆炸那一帧）。</summary>
        private static void ClearBlackHoles()
        {
            for (int i = 0; i < BlackHoleSlots; i++)
            {
                _bhOn[i] = false;
                _bhLeft[i] = 0f;
                _bhRadius[i] = 0f;
            }
        }

        /// <summary>★ v311：伏雷要开前摇时**取一个空槽**（全占满就退到 0 号格，
        /// 与"最近一次吸引"的直觉一致，且仍然确定性）✓</summary>
        private static int ClaimBlackHoleSlot()
        {
            for (int i = 0; i < BlackHoleSlots; i++) if (!_bhOn[i]) return i;
            return 0;
        }

        // ════════════════════════════════════════════════════════════════════
        //  会话内引用
        // ════════════════════════════════════════════════════════════════════

        private static PlayerGoods _goods;
        private static World _world;
        private static PlayerActor _player;

        /// <summary>`Attach` 之后才生效；没接上（自检里手搓的裸 `World`）⇒ 一切效果为 0 ✓</summary>
        public static bool Attached => _goods != null && _player != null;

        // ── 闸门组：感电的派生单（结算前排、结算后发）──
        private static bool _shockPending;
        private static float _shockDamage;
        private static int _shockTargets;
        private static Vector2 _shockCenter;
        private static int _shockExcludeId = -1;

        /// <summary>派生伤害闸（见类注释「有限步」）。</summary>
        private static bool _emitting;

        /// <summary>★ v304：狂暴"赤红窄环"的节流计时（在 `Tick` 里按固定步长递减 ⇒ 确定性）。</summary>
        private static float _frenzyFxCd;

        // ── 动作组：臭水沟 ──
        /// <summary>水壶这一次出手的**实际伤害**（`RunSession.FireBeam` 每次现记；水球/香蕉皮按它算）。</summary>
        private static float _canDamage;
        /// <summary>已经累计消耗了多少点水（到 `SewerWaterPerDrop` 归零并发皮）。</summary>
        private static int _waterSpent;
        /// <summary>掉落序号 —— 落点的**唯一**伪随机种子（同一个序号永远同一个落点，见 `DropPeel`）。</summary>
        private static int _peelCount;

        /// <summary>
        /// ★ v304：**已经扔出去、还在空中的香蕉皮**。
        /// `T` 是"还要飞多久"，到 0 那一刻才结算伤害 + 砸坑特效。
        /// ★ 为什么是**定长数组**而不是 `List`：这条路径在"按住水壶"时每 0.5 秒就走一次，
        ///   `List` 的扩容/移除虽然摊还很便宜，但这里上限本来就是 8（= 池格数）
        ///   ⇒ 定长 + `On` 标记，运行期**零分配**、且索引全在界内 ✓
        /// ★ 结算顺序 = 数组下标顺序（不是"谁先谁都行"）⇒ 确定性重放稳定 ✓
        /// </summary>
        private struct PendingPeel
        {
            public bool On;
            public float T;          // 剩余飞行秒数
            public Vector2 At;       // 落点
            public float Damage;     // 落地时每个被罩到的敌人吃多少
        }

        private static readonly PendingPeel[] _peelQueue = new PendingPeel[PeelQueueMax];

        // ── 动作组：海浪 ──
        /// <summary>移速加成的剩余秒数（> 0 就提速）。</summary>
        private static float _waveLeft;
        /// <summary>这一次浪的移速加成比例（触发时按层数快照；层数变了不追溯已触发的那一次）。</summary>
        private static float _waveSpeedPct;

        // ── 动作组：潮汐（按位移推开，见 `TidePushSeconds`）──
        /// <summary>一单"还要推多远"的位移任务。定长数组 ⇒ 运行期零分配（与 `_peelQueue` 同一条纪律）。</summary>
        private struct TideJob
        {
            public bool On;
            public EnemyActor E;
            public int Id;
            public Vector2 Dir;      // 单位方向（玩家 → 敌人）
            public Vector2 From;     // 起始位置（读数用）
            public float Left;       // 还要推多少米
            public float Want;       // 这一单一共要推多少米（读数用）
            public float Speed;      // 米/秒 = Left / `TidePushSeconds`
        }

        private static readonly TideJob[] _tideJobs = new TideJob[TideJobMax];

        // ── 自证读数（只读；自检/探针引用）──
        /// <summary>本局一共电击过多少个"非主目标"的敌人。</summary>
        public static int ShockHits { get; private set; }
        /// <summary>本局一共掉过几个香蕉皮。</summary>
        public static int PeelsDropped { get; private set; }
        /// <summary>本局一共触发过几次海浪。</summary>
        public static int WavesTriggered { get; private set; }
        /// <summary>本局一共拉拢过多少个敌人（黑洞）。</summary>
        public static int BlackHolePulled { get; private set; }

        // ════════════════════════════════════════════════════════════════════
        //  ★ v305：用户实测三条的**计数器**（只读，供探针/控制台读数；不参与任何判定）
        //
        //  为什么需要它们：上一轮改了"弹字坐标"，用户实测说没生效 —— 而"没生效"有两种可能，
        //    · 改动没编进去（代码没跑），或
        //    · 代码跑了，但**走了另一条分支**（罩不到人 ⇒ 落回玩家脚下）
        //  这两种在画面上都长得一样。计数器把"分支"直接摊开：
        //    `SewerPops/WavePops` 只在**真的打到敌人**时 +1（= 弹字必在敌人身上）；
        //    `SewerMisses/WaveMisses` 记"罩到 0 个人"的次数（= 旧版本会在玩家脚下弹的那一次）；
        //    两者相加 = 触发次数 ⇒ 一眼看出"上一轮那 1.2 米到底有没有生效" ✓
        // ════════════════════════════════════════════════════════════════════
        /// <summary>「污水」弹字**真的打在敌人身上**的次数（只在 hit &gt; 0 时 +1）。
        /// ★ v308：再叠一条判据 —— 只有**弹字真的发出去**才 +1（`Dealt &lt; 0.05` 时不发、也不 +1）。</summary>
        public static int SewerPops { get; private set; }
        /// <summary>香蕉皮落地但**一个敌人都没罩到**的次数（v305 起：不发弹字）。</summary>
        public static int SewerMisses { get; private set; }
        /// <summary>最后一次「污水」弹字的世界坐标（XZ）。</summary>
        public static Vector2 SewerLastPopupPos { get; private set; }
        /// <summary>最后一次香蕉皮的落点（用来和弹字坐标对照：两者不同才算"弹字在敌人身上"）。</summary>
        public static Vector2 SewerLastLandPos { get; private set; }

        /// <summary>「海浪」弹字**真的打在敌人身上**的次数。
        /// ★ v308：同上 —— 只有弹字真的发出去才 +1。</summary>
        public static int WavePops { get; private set; }
        /// <summary>起浪但**一个人都没扫到**的次数（v305 起：不发弹字）。</summary>
        public static int WaveMisses { get; private set; }
        /// <summary>最后一次「海浪」弹字的世界坐标（XZ）。</summary>
        public static Vector2 WaveLastPopupPos { get; private set; }
        /// <summary>最后一次起浪时玩家的位置（对照用）。</summary>
        public static Vector2 WaveLastFromPlayer { get; private set; }

        /// <summary>完成过多少单潮汐位移（一单 = 一个敌人被推开一次）。</summary>
        public static int TidePushes { get; private set; }
        /// <summary>最后一单潮汐的**实测净位移**（米）—— 与 `TideLastWant` 对比即"卡面值 vs 实际值"。</summary>
        public static float TideLastMeters { get; private set; }
        /// <summary>最后一单潮汐的**卡面值**（米，= `TidePush` 聚合值）。</summary>
        public static float TideLastWant { get; private set; }
        public static Vector2 TideLastFrom { get; private set; }
        public static Vector2 TideLastTo { get; private set; }
        /// <summary>正在被推（= 自主移动被压住）的敌人数量。</summary>
        public static int TideActive
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _tideJobs.Length; i++) if (_tideJobs[i].On) n++;
                return n;
            }
        }

        // ═══════════════════════════ 生命周期 ═══════════════════════════

        /// <summary>每局开始接上这一局的三个句柄（`RunSession.Start`）。**同时清空跨帧状态** ✓</summary>
        public static void Attach(PlayerGoods goods, World world, PlayerActor player)
        {
            _goods = goods;
            _world = world;
            _player = player;
            Reset();
        }

        /// <summary>清空全部跨帧状态（每个会话开始时调）。</summary>
        public static void Reset()
        {
            _shockPending = false;
            _shockDamage = 0f;
            _shockTargets = 0;
            _shockCenter = Vector2.zero;
            _shockExcludeId = -1;
            _emitting = false;
            _frenzyFxCd = 0f;
            _canDamage = 0f;
            _waterSpent = 0;
            _peelCount = 0;
            // ★ v304：还在空中的香蕉皮属于"本局"，开局必须清干净
            //   （不清的后果：上一局最后 0.45s 扔出去的那颗会在新一局开场砸下来）
            for (int i = 0; i < _peelQueue.Length; i++) _peelQueue[i] = default;
            _waveLeft = 0f;
            _waveSpeedPct = 0f;
            ShockHits = 0;
            PeelsDropped = 0;
            WavesTriggered = 0;
            BlackHolePulled = 0;
            // ★ v308：吸附场地是**本局**的状态 ⇒ 换局必须关掉（否则上一局停在吸附窗口里，
            //   下一局开局会有几个固定帧莫名把敌人往一个旧坐标拖 —— 极难归因）
            // ★ v311：一格 → `BlackHoleSlots` 格 ⇒ 改成整组清（同一个理由，只是多清两格）
            ClearBlackHoles();
            // ★ v305：新增的读数与位移单一起归零
            for (int i = 0; i < _tideJobs.Length; i++) _tideJobs[i] = default;
            SewerPops = 0; SewerMisses = 0;
            SewerLastPopupPos = Vector2.zero; SewerLastLandPos = Vector2.zero;
            WavePops = 0; WaveMisses = 0;
            WaveLastPopupPos = Vector2.zero; WaveLastFromPlayer = Vector2.zero;
            WaveLastAdvance = 0f;       // ★ v312：上一道浪的推进距离也是"本局"的读数
            WaveLastRadius = 0f;        // ★ v313：上一道浪的判定半径，同一个理由
            TidePushes = 0; TideLastMeters = 0f; TideLastWant = 0f;
            TideLastFrom = Vector2.zero; TideLastTo = Vector2.zero;
        }

        /// <summary>每固定帧推进（`RunSession` 在推进延迟爆炸的同一处调）。只递减计时器，不碰判定。</summary>
        public static void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (_waveLeft > 0f) _waveLeft = Mathf.Max(0f, _waveLeft - dt);
            if (_frenzyFxCd > 0f) _frenzyFxCd = Mathf.Max(0f, _frenzyFxCd - dt);
            TickPeels(dt);       // ★ v304：香蕉皮的落地倒计时（到点才结算 + 砸坑）
            TickTideJobs(dt);    // ★ v305：潮汐的**按米位移**（到米数才收单）
            TickBlackHole(dt);   // ★ v308/v310：黑洞的**持续吸附**（前摇 1.0s 内每帧把敌人往中心拉）
        }

        // ═══════════════════════════ 动作组：潮汐 ═══════════════════════════

        /// <summary>
        /// ★ v305：**开一单潮汐位移**（`RunSession.PluginPostHit` 的潮汐分支调它）。
        /// `dir` 必须是单位向量（玩家 → 敌人），`meters` 是卡面值（`amount × 层数`）。
        /// 同一敌人身上已有单据时**接着推**（`Left += meters`），不新开一单 ——
        /// 否则两次命中会互相打断，实测位移只会等于最后那一次的量 ✗
        /// </summary>
        public static void StartTidePush(EnemyActor e, Vector2 dir, float meters, float want, in FxSpec fx)
        {
            if (e == null || e.IsDead || _world == null) return;
            if (meters <= 1e-4f) return;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir = dir.normalized;

            int slot = -1;
            for (int i = 0; i < _tideJobs.Length; i++)
            {
                if (_tideJobs[i].On && _tideJobs[i].Id == e.Id)
                {
                    TideJob merge = _tideJobs[i];
                    merge.Left += meters;
                    merge.Want += want;
                    merge.Speed = merge.Left / TidePushSeconds;
                    _tideJobs[i] = merge;
                    // ★ v312：合并到**同一单**时不重发特效 —— 这是同一次推的延续（0.25s 内又打中一下），
                    //   再发一套弧+拖尾会在同一个点上叠两层，读成"闪了一下"。新开一单才画 ✓
                    return;
                }
                if (!_tideJobs[i].On && slot < 0) slot = i;
            }
            if (slot < 0)
            {
                // 满了：顶掉剩余米数最少的那一单（与香蕉皮队列同一条"环形复用最老"纪律）
                slot = 0;
                float best = float.MaxValue;
                for (int i = 0; i < _tideJobs.Length; i++)
                    if (_tideJobs[i].Left < best) { best = _tideJobs[i].Left; slot = i; }
            }

            TideJob job = default;
            job.On = true;
            job.E = e;
            job.Id = e.Id;
            job.Dir = dir;
            job.From = e.Pos;
            job.Left = meters;
            job.Want = want;
            job.Speed = meters / TidePushSeconds;
            _tideJobs[slot] = job;

            // ★ v312：**推力反馈** —— 被推的敌人脚下起一道 120° 水波弧 + 沿推开方向的渐隐拖尾。
            //   门控：本函数只被 `RunSession.PluginPostHit` 的 `m.TidePush > 0` 分支调用
            //   ⇒ 没装 `can_tide` 时这里**一次都不会执行**（`AttackVfx.TideArcs` 恒 0，可验）✓
            //   池账：每目标 8 格 `Bit`（弧 4 + 拖尾 4），见 `AttackVfx.SpawnTidePush` 的注释。
            AttackVfx.SpawnTidePush(e.Pos, dir, in fx);
        }

        /// <summary>
        /// **这个敌人现在正被潮汐推着吗** —— `Enemies.MoveStep` / `MoveStepBounce` 读它。
        /// 返回 true ⇒ 那两处把敌人**自己的**移动速度按 0 算（追击被压住），
        /// 否则"推开 1.2m"会被它自己朝玩家走的 2.7 m/s 当场吃回去（见 `TidePushSeconds`）✓
        /// ⚠️ 没装潮汐时 `_tideJobs` 全为 `On=false` ⇒ 每次最多 16 次 bool 判断，行为零变化 ✓
        /// </summary>
        public static bool IsTideHeld(int enemyId)
        {
            for (int i = 0; i < _tideJobs.Length; i++)
                if (_tideJobs[i].On && _tideJobs[i].Id == enemyId) return true;
            return false;
        }

        /// <summary>推进全部潮汐位移单：每帧直接推 `Speed·dt` 米（**位移**，不是速度）。</summary>
        private static void TickTideJobs(float dt)
        {
            for (int i = 0; i < _tideJobs.Length; i++)
            {
                if (!_tideJobs[i].On) continue;
                TideJob job = _tideJobs[i];
                EnemyActor e = job.E;
                if (e == null || e.IsDead || e.Id != job.Id)
                {
                    _tideJobs[i] = default;      // 人没了 ⇒ 单作废（不计入 `TidePushes`）
                    continue;
                }
                float step = Mathf.Min(job.Left, job.Speed * dt);
                if (step > 0f)
                    e.Pos += job.Dir * step;
                // 与 `MoveStep` 同一条边界：推不出场地
                e.Pos = World.ClampToArena(e.Pos, e.Radius, _world.Tuning.farm.arenaHalf);
                job.Left -= step;
                if (job.Left <= 1e-4f)
                {
                    job.On = false;
                    _tideJobs[i] = default;
                    TidePushes++;
                    TideLastMeters = (e.Pos - job.From).magnitude;
                    TideLastWant = job.Want;
                    TideLastFrom = job.From;
                    TideLastTo = e.Pos;
                    continue;
                }
                _tideJobs[i] = job;
            }
        }

        /// <summary>
        /// ★ v304：推进**还在空中的香蕉皮**；到点 ⇒ 落地：砸坑特效 + 结算伤害 + 弹字。
        ///
        /// 与视觉的时序（见 `PeelFallSeconds`）：视觉由 `AttackVfx.SpawnPeelFall` 在**生成那一刻**
        /// 开始下落，落地时刻 = 生成 + `PeelFallSeconds`；本函数用的是**同一个常量**，
        /// 而 `RunSession` 的固定步长把 dt 切成 1/60 ⇒ 最多差**半帧（≈8ms）**，
        /// 肉眼与判定都读不出这个差 ✓
        /// </summary>
        private static void TickPeels(float dt)
        {
            if (_world == null) return;
            for (int i = 0; i < _peelQueue.Length; i++)
            {
                if (!_peelQueue[i].On) continue;
                PendingPeel p = _peelQueue[i];
                p.T -= dt;
                if (p.T > 0f) { _peelQueue[i] = p; continue; }
                _peelQueue[i] = default;
                LandPeel(in p);
            }
        }

        /// <summary>香蕉皮**落地**：砸坑（尘环）+ 结算这一片的伤害 + 在被打到的敌人身上弹字。</summary>
        private static void LandPeel(in PendingPeel peel)
        {
            PlayerActor owner = _player;
            FxSpec fx = PluginFx.For(ModsOf(WeaponSlot.Can), WeaponSlot.Can);

            // ① 砸坑反馈：**向上溅起的泥点**（9 颗，先上后落）
            // ★ v305：改前这里是 `AttackVfx.SpawnGroundRing(peel.At, SewerPeelRadius, fx, 0.30f, 0.25f)`
            //   —— 那一圈是 16 段 `ModeRadial` 从 `0.17m` **往外张开到 1.7m** 的环，
            //   与水环 / 冲击环 / 浪**同一套几何 + 同一个池 + 同一个动画方向**
            //   ⇒ 用户实测读成"污水末端产生了一个波"✗（那确实就是一道向外推的波）
            //   现在换成"泥点向上溅"：`ModeBallistic`、重力 16、走抛物线、**不向外扩任何东西**
            //   ⇒ "落地有反馈"保住了，"波"这个观感没有了 ✓
            AttackVfx.SpawnMudSplash(peel.At, SewerPeelRadius, fx);
            // ② 屏震：走**既有的** `CombatFeedback`（不自己动 timeScale / 相机）
            //    轻微一档 —— 香蕉皮不是爆炸，用 `ShakeCrit`（暴击那一档的轻抖）足够
            CombatFeedback.ShakeCrit();

            // ③ 结算：落地那一刻罩到谁就伤谁（与"踩踏半径"同一个数 `SewerPeelRadius`）
            int hit = 0;
            Vector2 popupAt = peel.At;
            // ★ v308：弹字要印**被罩到的第一个敌人真正掉了多少血**（不是 `peel.Damage`）
            float popupDealt = 0f;
            List<EnemyActor> es = _world.Enemies;
            for (int i = 0; i < es.Count; i++)
            {
                EnemyActor e = es[i];
                if (e == null || e.IsDead) continue;
                float rr = SewerPeelRadius + e.Radius;
                if ((e.Pos - peel.At).sqrMagnitude > rr * rr) continue;
                // ★ v304：打在**第一个被罩到的敌人**身上
                // ★ v308：**同一个人身上的那条弹字，数字必须是他自己的** —— 所以先记坐标、再记
                //   `DealTo` 的返回值（`DamageResult.Dealt`）；后面再被罩到的不影响这一条 ✓
                float d = DealTo(e, peel.Damage, owner);
                if (hit == 0) { popupAt = e.Pos; popupDealt = d; }
                hit++;
            }
            // ══════════════════════════════════════════════════════════════════
            // ④ 弹字 —— ★★ v305：**罩不到人就不发**（这才是"上一轮没生效"的真原因）★★
            //
            // ── 病灶（用户实测"污水弹字没移到敌人身上"，而代码看起来是对的）──
            //   改前本函数**无条件**发一条「污水」，坐标是 `popupAt`；
            //   而 `popupAt` 的初值就是 `peel.At`（那时的落点 = 玩家身边 1.5 m）✗
            //   ⇒ 只要这一刻**罩到 0 个敌人**，弹字就落在玩家脚边 —— 而那恰恰是最常见的情况：
            //      · 香蕉皮落在玩家周围 1.5 m 的**黄金角**方向（每颗转 137.5°）——
            //        ★ v313 起已改成"前方 35° 扇形 / 1.2~2.6m"，这段说的是当时的病灶；
            //      · 判定半径 `SewerPeelRadius`（v312 起 = 2.4，那时是 1.7）+ 敌人半径(0.36)；
            //        圆心是**落点**，落点又只在玩家周围 1.5m 的黄金角上；
            //      · 玩家对着 3~6 m 外的怪喷水时，落点附近常常一个敌人都没有
            //   ⇒ 玩家看到的「污水」几乎每次都在自己脚下，于是判定"上一轮根本没改"✗
            //   上一轮只改了 `hit==0 ? peel.At : e.Pos` 里**真被罩到**那一支，
            //   而 `hit==0` 那一支仍然是"在玩家脚下弹一条" —— 那不是坐标错了，是**不该有这条弹字**：
            //   它表示"对敌人造成了伤害"，可这一刻没有任何敌人受到伤害 ✓
            //
            // ── 现在 ──
            //   · `hit > 0` ⇒ 弹字**只**打在被罩到的第一个敌人身上（`popupAt = e.Pos`）✓
            //   · `hit == 0` ⇒ **不发**（只把 `SewerMisses` +1，供读数）✓
            //   ⚠️ 这不是"表现层取舍"，而是"这条弹字的语义就是'敌人挨打'"——
            //     凡是"敌人受到的伤害"，弹字就必须在敌人身上（用户原话）
            // ══════════════════════════════════════════════════════════════════
            SewerLastLandPos = peel.At;
            if (hit <= 0) { SewerMisses++; return; }
            // ★★ v308：文本 = 「污水 -<这一下真正扣掉的血>」★★
            //   数字来自 `DamageResult.Dealt`（上面 `DealTo` 的返回值，就是**第一个被罩到的
            //   那个敌人**这一下真的掉了多少）；`DamageLabel` 在 `dealt < 0.05` 时返回 null
            //   ⇒ 被闪避 / 入场免伤 / 目标已死（Dealt = 0）**这一条弹字整条不发** ✓
            //   `hit` 仍然 > 0（真的罩到了人）所以这与"压根没罩到人"是两件事：
            //   前者记 `SewerMisses`，后者只记"没发弹字"（不额外计数，避免把两种原因混成一个数）✓
            string sewerText = PluginHitFx.DamageLabel("污水", popupDealt);
            if (sewerText == null) return;
            SewerPops++;
            SewerLastPopupPos = popupAt;
            Bus.Publish(new FloatingTextEvent
            {
                Pos = popupAt, Text = sewerText, Kind = PopupKind.DamageToEnemy,
                Scale = 1.0f,
            });
        }

        /// <summary>
        /// **移速倍率**（玩家移动那段读它）。
        /// 海浪生效期间 = `1 + 0.50`，其余时候**恒为 1**（没装插件 ⇒ 一个字节都不动）✓
        /// </summary>
        public static float MoveSpeedScale => _waveLeft > 0f ? 1f + _waveSpeedPct : 1f;

        /// <summary>探针读数：移速加成的剩余秒数。</summary>
        public static float WaveRemaining => _waveLeft;

        // ═══════════════════════════ 闸门：结算前 ═══════════════════════════

        /// <summary>
        /// 结算**之前**：按"攻击发生时的世界状态"改这一下的伤害，并排下感电的派生。
        /// `req` 是闸门里的**可变副本**（`World.DealDamage` 已经为灼印复制过一份）⇒ 直接改它 ✓
        /// </summary>
        public static void BeforeResolve(ref DamageRequest req)
        {
            // ★ 每一下进场先**作废上一拍的派生单** ★ —— 与上一轮 `PluginExtra` 同一条：
            //   上一下若是"被闪避 / 目标已死"，`AfterResolve` 会因为 `Dealt <= 0` 提前返回 ⇒
            //   那张单子没人消费；不清的话"下一拍不是直击"（毒区 tick / 子爆）就会替它电一次 ✗
            _shockPending = false;
            if (_emitting || !Attached || _world == null) return;

            // ① 玩家**承受**伤害那一侧：这 7 件没有一件是防御件 ⇒ 直接返回
            if (req.Target == _player) return;

            // ② 玩家**打出**伤害那一侧：只认"直击本体"（`NoMarkConsume` = 派生伤害的既有标记）
            if (req.Source != _player || req.NoMarkConsume) return;
            EnemyActor target = req.Target as EnemyActor;
            if (target == null) return;

            WeaponModifiers m = ModsOf(CurrentSlot());

            // ── 狂暴：**目标**残血 ⇒ 这一下更疼 ──
            //   ★ 方向必须写清：是"敌人血量低于 40% 时你打它 +10%/层"，
            //     不是"你自己残血时你打它更疼"（后者是上一轮回收掉的那一版）★
            if (m.FrenzyPct > 0f && m.FrenzyBelow > 0f && target.Health.MaxHp > 0f)
            {
                float frac = target.Health.Hp / target.Health.MaxHp;
                if (frac <= m.FrenzyBelow)
                {
                    req.RawValue *= 1f + m.FrenzyPct;
                    // ★ v304：**狂暴的可辨识特效**（用户需求⑥）—— 纯插入，不改任何数值。
                    //   赤红的窄环套在**残血目标**脚下 ⇒ "这一下是狂暴打的"看得见。
                    //   节流走 `_frenzyFxCd`（在 `Tick` 里按固定步长递减）⇒ **确定性**、
                    //   且不会因为"多数敌人都残血"而把这个环刷满屏 ✓
                    if (_frenzyFxCd <= 0f && _world != null)
                    {
                        _frenzyFxCd = FrenzyFxInterval;
                        AttackVfx.SpawnGroundRing(target.Pos, FrenzyFxRadius,
                                                  PluginFx.For(ModsOf(WeaponSlot.Hoe), WeaponSlot.Hoe),
                                                  FrenzyFxLife, 0.05f);
                    }
                }
            }

            // ── 感电：记下"这一刀要电谁"（真正的伤害在结算后发，见 `AfterResolve`）──
            if (m.ShockTargets > 0 && m.ShockPct > 0f)
            {
                _shockPending = true;
                _shockDamage = req.RawValue * m.ShockPct;
                _shockTargets = m.ShockTargets;
                _shockCenter = target.Pos;
                _shockExcludeId = target.Id;
            }
        }

        // ═══════════════════════════ 闸门：结算后 ═══════════════════════════

        /// <summary>
        /// 结算**之后**：发感电的派生伤害。只对"真的打进去"的伤害生效
        /// （`Dealt &gt; 0`）—— 被闪避 / 格挡 / 入场免伤都不算 ✓
        /// </summary>
        public static void AfterResolve(in DamageRequest req, in DamageResult r)
        {
            if (_emitting || !Attached || r.Dealt <= 0f) return;
            // 派生单只可能是**玩家直击**排下的 ⇒ 不是玩家打的就整张作废
            if (req.Source != _player) { _shockPending = false; return; }
            if (!_shockPending) return;
            _shockPending = false;

            float dmg = _shockDamage;
            int want = _shockTargets;
            Vector2 center = _shockCenter;
            int exceptId = _shockExcludeId;
            if (dmg <= 0f || want <= 0 || _world == null) return;

            _emitting = true;
            try { ShockChain(center, exceptId, want, dmg, req.Source); }
            finally { _emitting = false; }
        }

        /// <summary>
        /// 感电：以**主目标**为圆心，电击 `want` 米内**除主目标外**最近的若干个敌人，各吃 `damage`。
        /// 排序判据与 `MeleeHitScan` 的"最近优先"同一套（距离升序、同距按 `Id` 兜底 ⇒ 确定性不变）✓
        /// </summary>
        private static void ShockChain(Vector2 center, int exceptId, int want, float damage, Actor source)
        {
            if (_world == null) return;
            int cap = Mathf.Min(Mathf.Max(0, want), ShockMaxTargets);
            if (cap == 0) return;

            // 候选缓冲：**预分配 + 复用**（这个方法在挥击路径上会被频繁调用，每次 new 就是给 GC 喂垃圾）
            if (_shockBuf == null || _shockBuf.Length < ShockMaxTargets)
            {
                _shockBuf = new EnemyActor[ShockMaxTargets];
                _shockDist = new float[ShockMaxTargets];
            }

            float r2 = ShockRadius * ShockRadius;
            int n = 0;
            List<EnemyActor> es = _world.Enemies;
            for (int i = 0; i < es.Count; i++)
            {
                EnemyActor e = es[i];
                if (e == null || e.IsDead || e.Id == exceptId) continue;
                float d2 = (e.Pos - center).sqrMagnitude;
                if (d2 > r2) continue;
                if (n >= _shockBuf.Length) break;
                // 插入排序（n ≤ 12 ⇒ 代价可忽略）：距离升序，同距按 Id
                int j = n;
                while (j > 0)
                {
                    float pd = _shockDist[j - 1];
                    EnemyActor pe = _shockBuf[j - 1];
                    bool swap = pd > d2 || (pd == d2 && pe.Id > e.Id);
                    if (!swap) break;
                    _shockBuf[j] = _shockBuf[j - 1];
                    _shockDist[j] = pd;
                    j--;
                }
                _shockBuf[j] = e;
                _shockDist[j] = d2;
                n++;
            }

            int hit = 0;
            Vector2 first = center;
            // ★ v308：弹字要印**第一个被电到的敌人真正掉了多少血**（不是 `damage`）
            float firstDealt = 0f;
            // ★ v304：**电流弹射的可视化**（用户需求③）—— 外观只算一次（`Aggregate` 不做缓存）
            FxSpec fx = PluginFx.For(ModsOf(WeaponSlot.Hoe), WeaponSlot.Hoe);
            // ★ v309：**主折线的两端各一个小光球**（用户：「主折线加端点亮点（两端各一个小光球）」）
            //   · 「远端」（被电到的敌人）那一颗本来就有一颗（下面循环里的 `SpawnOrb`）⇒ 保留
            //   · 「近端」（主目标 / 电流的起点）这一颗是**本轮新加**的，只画**一次**
            //     （不是每条支流各一颗 —— 那是同一个点，叠十几颗只会变成一块白斑 ✗）
            //   颜色/半径：起点球比目标球**大 15%、更白**
            //     ⇒ 一眼看得出"电是从这个人身上窜出去的"，而不是十几条无源的折线 ✓
            //   池账：1 格 `Mist` × `ShockOrbLife`(0.12s)，每次感电只加这 1 颗 ✓
            AttackVfx.SpawnOrb(new Vector3(center.x, ShockOrbY, center.y),
                               ShockOrbRadius * ShockSourceOrbScale, ShockSourceOrbColor,
                               ShockOrbLife);
            for (int i = 0; i < n && hit < cap; i++)
            {
                EnemyActor e = _shockBuf[i];
                if (e == null || e.IsDead) continue;
                // ★ 先画再打：`DealTo` 可能把它打死（`e.Pos` 仍然是它倒下时的位置，两者一致）✓
                //   起点 = **主目标**（`center` 就是 `BeforeResolve` 记下的主目标坐标），
                //   终点 = 被电到的这个敌人 ⇒ 用户要的"主目标 → 每个被电到的敌人"✓
                PluginHitFx.SpawnBolt(new Vector3(center.x, ShockFxHeight, center.y),
                                      new Vector3(e.Pos.x, ShockFxHeight, e.Pos.y),
                                      in fx);
                // ★ v307：**被电到的敌人身上加一次短促的电光球**（用户④：
                //   「被电到的敌人身上加一次短促的电光球（小球，0.12s）」）。
                //
                //   为什么需要它：改前"被电到"这件事在**敌人身上**没有任何标记 ——
                //   只有一条从主目标连过去的折线。而一根线落在谁身上、是不是只擦过、
                //   哪几个是被电的，全靠玩家自己顺着线找 ✗ 一个钉在身上的小球把这件事
                //   变成"谁身上亮了谁就被电了" ✓
                //   放在 `DealTo` **之前**画（与折线同一个顺序）⇒ 被电死的那个也能看到这一下 ✓
                //   池账：1 格 `Mist` 池 × 0.12s；一次最多 `ShockMaxTargets`(12) 个目标 ✓
                AttackVfx.SpawnOrb(new Vector3(e.Pos.x, ShockOrbY, e.Pos.y),
                                   ShockOrbRadius, ShockOrbColor, ShockOrbLife);
                // ★ v308：返回值 = 这个被电到的敌人这一下**真正扣掉的血/盾**
                //   第一个被电到的那一个才是弹字锚点 ⇒ 只记它的数（同一个人身上的字，
                //   数字必须是**他自己的**；后面被电到的不影响这一条）✓
                float d = DealTo(e, damage, source);
                if (hit == 0) { first = e.Pos; firstDealt = d; }
                hit++;
            }
            for (int i = 0; i < n; i++) _shockBuf[i] = null;   // 别拖着对象

            if (hit > 0)
            {
                ShockHits += hit;
                // ★★ v308：文本 = 「感电 -<这一下真正扣掉的血>」★★
                //   数字来自 `DamageResult.Dealt`（`DealTo` 的返回值）；
                //   `DamageLabel` 对 `dealt < 0.05` 返回 null ⇒ 被闪避 / 免伤 / 已死（Dealt = 0）
                //   **整条弹字不发**（用户判据：弹一条 0 是噪音）✓
                string shockText = PluginHitFx.DamageLabel("感电", firstDealt);
                if (shockText != null)
                {
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = first, Text = shockText, Kind = PopupKind.DamageToEnemy, Scale = 0.9f,
                    });
                }
            }
        }

        private static EnemyActor[] _shockBuf;
        private static float[] _shockDist;

        // ═══════════════════════════ 动作组：水壶 ═══════════════════════════

        /// <summary>
        /// `RunSession.FireBeam` 每次算完"这一束水柱的实际伤害"就记一笔 ——
        /// 臭水沟的香蕉皮（60%）与海浪（30%）都按**这一次出手的水壶伤害**派生，
        /// 与"灼烧/减速 = 这一下的实际伤害 × 比例"是同一条惯例 ✓
        /// </summary>
        public static void NoteCanDamage(float damage)
        {
            _canDamage = Mathf.Max(0f, damage);
        }

        /// <summary>
        /// **每消耗 `cost` 点水**调一次（`RunSession.TickBeam` 里"扣水那一下"）。
        /// 累计到 `SewerEvery` 就掉香蕉皮：`1 层 1 个 / 2 层 2 个`，各自吃 `水壶伤害 × 60%`。
        /// ★ 不消费随机流：落点由**掉落序号的整数哈希**在**玩家前方扇形**内取点（同一个序号永远同一个落点）✓
        /// </summary>
        public static void OnWaterSpent(PlayerActor p, in WeaponModifiers pm, int cost)
        {
            if (pm.SewerEvery <= 0 || cost <= 0 || _world == null || p == null) return;
            _waterSpent += cost;
            while (_waterSpent >= pm.SewerEvery)
            {
                _waterSpent -= pm.SewerEvery;
                int drops = Mathf.Max(0, pm.SewerDrops);
                float each = _canDamage * pm.SewerPct;
                for (int k = 0; k < drops; k++) DropPeel(p, each, pm);
            }
        }

        /// <summary>
        /// 掉一个巨大香蕉皮：**玩家前方扇形内**的落点 → **从 7 米高砸下来** → 落地才结算。
        ///
        /// ★★ v304：**时序从"生成即结算"改成"落地才结算"**（用户需求②）★★
        ///   ── 改前（v290）──
        ///     `DropPeel` 一进来就 `DealTo(...)` 立刻扣血，而视觉（那时只有地面上一圈水环）
        ///     根本不表现"香蕉皮"⇒ 玩家看到的是"脚下闪一下，远处的怪掉血"，因果读不出来 ✗
        ///   ── 现在 ──
        ///     ① 立刻 `AttackVfx.SpawnPeelFall` 起一颗**真在 7 米高空**的香蕉皮（0.45s 落地）
        ///     ② 把"落点 / 伤害"排进 `_peelQueue`，`TickPeels` 到点（同一个 0.45s）才结算
        ///   ⇒ 视觉与判定**同一个常量**驱动 ⇒ 不可能错位 ✓
        ///   ⇒ 这条改动是**唯一一处故意的时序变化**（伤害晚 0.45s 到账），
        ///     只在装了 `can_sewer` 时存在；没装插件时本函数一次都不会被调用 ⇒ 其它战斗行为零影响 ✓
        ///
        /// ★ 为什么不消费随机流：落点由**掉落序号的整数哈希**决定（见 `PeelLandingPoint` / `PeelHash01`）✓
        /// ★ 满队列怎么办：直接丢弃最老的那一颗（与对象池"环形复用最老"同一条纪律），
        ///   而不是扩容 —— 扩容会让"每帧零分配"这条硬约束失效 ✗
        ///
        /// ★★ v313：**落点从"玩家周围一圈"改成"玩家前方一片"**（用户需求）★★
        ///   原话：「修改香蕉皮掉落位置，在角色前方区域随机掉落，不是在角色周围」。
        ///   ── 改前 ──
        ///     黄金角 `(_peelCount × 137.5°)` × `SewerPeelDistance`(1.5)
        ///     ⇒ 第 0 颗在正东、第 1 颗在 137.5°、第 2 颗在 275°…… 皮**绕着玩家转圈撒**；
        ///     朝东喷水时，第 1 颗就落在**玩家背后** ✗ 与"扔出去的皮"这件事完全无关
        ///   ── 现在 ──
        ///     落点 = `p.AimDir` 左右各 `SewerPeelHalfAngle`(35°) 的扇形里、
        ///            距玩家 `SewerPeelMinDist`(1.2)~`SewerPeelMaxDist`(2.6) 的一点
        ///     ⇒ 皮恒在**你正在喷的那个方向**的前方一片里，且每一颗位置都不同 ✓
        ///
        /// ★ 为什么这里的"随机"必须是**确定性的**（这条不能破）──
        ///   · `UnityEngine.Random`：全工程的禁令（会破坏定种子回放）✗
        ///   · `Pcg32`：它是**全工程共享的逻辑随机流**，本类若去消费它，
        ///     后面每一次判定（暴击 / 掉落 / AI 选路）都会跟着漂移 ⇒ 自检读数全变 ✗
        ///   · 现在用的 `PeelHash01`：**无状态**整数哈希，输入只有 `_peelCount` 与一个盐值
        ///     ⇒ 同一个 `_peelCount` **永远**得到同一个 (角度, 距离)，
        ///       回放 / 自检可以逐帧复现，且不碰任何全局流 ✓
        ///   （角度→坐标仍用 `Mathf.Cos/Sin`、距离→半径用 `Mathf.Sqrt`：
        ///     与改前的黄金角同一种算法，没有引入新的不确定性来源）
        ///
        /// ★ 兜底链（`AimDir` 可能是零向量）：`p.AimDir` → `p.MoveInput` → `Vector2.up`，
        ///   推导与阈值见 `PeelLandingPoint`。
        /// </summary>
        private static void DropPeel(PlayerActor p, float damage, in WeaponModifiers pm)
        {
            if (_world == null || damage <= 0f) return;
            // ★ v313：落点 = 前方扇形内的确定性伪随机点（同一个 _peelCount ⇒ 同一个点）
            Vector2 at = PeelLandingPoint(p, _peelCount);
            _peelCount++;

            // ① 视觉：一颗真在空中的香蕉皮（落点 XZ 不变，只是从 7 米高下来）
            AttackVfx.SpawnPeelFall(at, PluginFx.For(pm, WeaponSlot.Can));

            // ② 逻辑：排进落地队列（满则顶掉最老的那一颗）
            PendingPeel item = default;
            item.On = true;
            item.T = PeelFallSeconds;
            item.At = at;
            item.Damage = damage;
            int slot = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < _peelQueue.Length; i++)
            {
                if (!_peelQueue[i].On) { slot = i; break; }
                if (_peelQueue[i].T < bestT) { bestT = _peelQueue[i].T; slot = i; }
            }
            if (slot >= 0) _peelQueue[slot] = item;

            PeelsDropped++;
        }

        /// <summary>
        /// ★ v313：**香蕉皮的落点** —— 玩家前方扇形内的一点（确定性；调用方见 `DropPeel`）。
        ///
        /// ── 几何 ──
        ///   中轴 `axis`、角度 `[-SewerPeelHalfAngle, +SewerPeelHalfAngle)`、
        ///   距离 `[SewerPeelMinDist, SewerPeelMaxDist]`，落点 = `p.Pos + rotate(axis, ang) · r`
        ///   · 距离取 **`sqrt(lerp(min², max², u))`** 而不是 `lerp(min, max, u)`：
        ///     扇环的面积权重 ∝ r ⇒ 线性取距离会让**内侧**每单位面积的点更密，
        ///     读起来是"皮老掉在脚边"✗；开根号后点**按面积均匀**，近端远端一样密 ✓
        ///   · 角度**分层**（`SewerPeelSectors` 格 + 格内抖动）而不是整段直接哈希：
        ///     纯哈希偶尔会连着给出两个接近的角度 ⇒ "两颗叠在一起"；
        ///     第 `k` 颗固定在第 `k % 5` 格 ⇒ 同一次触发的 1~3 颗**必然**不同格 ✓
        ///
        /// ── 兜底链（本方法唯一的输入风险：`AimDir` 可能为零向量）──
        ///   ① `p.AimDir` —— `Actor` 的字段，正常恒为单位向量（`RunSession` 每帧按鼠标归一化写入）
        ///   ② 为零 ⇒ `p.MoveInput` —— 正在跑动时的朝向
        ///   ③ 仍为零 ⇒ `Vector2.up` —— 就是 `Actor.AimDir` 的**字段初值**
        ///      （玩家站着不动、且这一次会话里从未瞄过的那一帧才会走到这里）
        ///   判据 `sqrMagnitude &gt; 1e-4`（模长 &gt; 0.01）⇒ 后面那次除法**不可能**除以 0 ✓
        ///
        /// ── 为什么不额外做"夹到墙内 / 避开脚下"──
        ///   · 脚下：`SewerPeelMinDist`(1.2) 已经排除了（玩家半径 0.45 ⇒ 中心在脚外 0.75m）✓
        ///   · 墙外：皮是**一次范围伤害**，落在人堆外围是正常的（判定圈罩到谁算谁）；
        ///     再加一次 `ClampToArena` 会让贴墙时所有落点挤回同一条线 ⇒ 反而丢掉"随机"✗
        /// </summary>
        private static Vector2 PeelLandingPoint(PlayerActor p, int idx)
        {
            // ① 中轴：AimDir → MoveInput → up（见上面"兜底链"）
            Vector2 axis = p.AimDir;
            if (axis.sqrMagnitude <= 1e-4f) axis = p.MoveInput;
            if (axis.sqrMagnitude <= 1e-4f) axis = Vector2.up;
            axis /= axis.magnitude;                       // 上面三级都保证模长 > 0.01 ⇒ 安全

            // ② 角度：先选格（保证连续掉落不同格），再在格内抖
            int sector = idx % SewerPeelSectors;
            float jitter = PeelHash01(idx, 0x68E31DA4u);
            float t = (sector + jitter) / SewerPeelSectors;                     // [0,1)
            float ang = (t * 2f - 1f) * SewerPeelHalfAngle * Mathf.Deg2Rad;     // [-35°,+35°)

            // ③ 距离：按**面积**均匀（见上面"为什么开根号"）
            float u = PeelHash01(idx, 0x9E3779B1u);
            float r2 = Mathf.Lerp(SewerPeelMinDist * SewerPeelMinDist,
                                  SewerPeelMaxDist * SewerPeelMaxDist, u);
            float r = Mathf.Sqrt(r2);

            // ④ 绕中轴转 ang 度
            float c = Mathf.Cos(ang), s = Mathf.Sin(ang);
            Vector2 dir = new Vector2(axis.x * c - axis.y * s, axis.x * s + axis.y * c);
            return p.Pos + dir * r;
        }

        /// <summary>
        /// ★ v313：`(序号, 盐)` → **[0,1)** 的纯整数哈希（Wellons "lowbias32" 的三级混淆）。
        /// 为什么不用 `Pcg32` / `UnityEngine.Random`：见 `DropPeel` 的「为什么必须是确定性」——
        /// 本函数**无状态**：同一个输入永远同一个输出、不消费任何全局流 ⇒ 回放与自检稳定 ✓
        /// 两个盐值（`0x68E31DA4` / `0x9E3779B1`）非零且互不相关 ⇒ 角度与距离两条序列相互独立
        /// （不会出现"远的一定偏左"这种相关性）✓
        /// `(x &gt;&gt; 8)` 取高 24 位再除 2²⁴ ⇒ 结果严格落在 [0,1)（不含 1，`Lerp` 不会越界）✓
        /// </summary>
        private static float PeelHash01(int idx, uint salt)
        {
            unchecked
            {
                uint x = (uint)idx * 0x9E3779B1u + salt;
                x ^= x >> 16; x *= 0x7FEB352Du;
                x ^= x >> 15; x *= 0x846CA68Bu;
                x ^= x >> 16;
                return (x >> 8) * (1f / 16777216f);
            }
        }

        /// <summary>
        /// **喷水那一下**调一次（`RunSession.FireBeam`）：脚下起一道向外扩散的海浪。
        /// · 脚下 `radius` 米内的敌人各吃 `水壶伤害 × 30%`
        /// · 玩家获得 `+50%` 移速，持续 1 秒
        /// ★ 触发是**有间隔**的（上一道还没散就不再来一道）⇒ 按住水壶时不会每帧刷一层浪 ✓
        /// </summary>
        public static void OnCanAttack(PlayerActor p, in WeaponModifiers pm)
        {
            if (pm.WaveSpeedPct <= 0f || p == null || _world == null) return;
            if (_waveLeft > 0f) return;                 // 一道浪只认一次（幂等 + 不刷屏）

            _waveLeft = Mathf.Max(0.05f, pm.WaveSeconds);
            _waveSpeedPct = pm.WaveSpeedPct;
            WavesTriggered++;

            float radius = pm.WaveRadius > 0f ? pm.WaveRadius : WaveRadiusBase;
            float dmg = _canDamage * pm.WaveDamagePct;
            int hit = 0;
            Vector2 popupAt = p.Pos;
            // ★ v308：弹字要印**第一个被扫到的敌人真正掉了多少血**（不是 `dmg`）
            float popupDealt = 0f;
            if (dmg > 0f)
            {
                List<EnemyActor> es = _world.Enemies;
                for (int i = 0; i < es.Count; i++)
                {
                    EnemyActor e = es[i];
                    if (e == null || e.IsDead) continue;
                    float rr = radius + e.Radius;
                    if ((e.Pos - p.Pos).sqrMagnitude > rr * rr) continue;
                    // ★ v304：「海浪」弹字打在**第一个被扫到的敌人**身上（用户需求⑦）
                    // ★ v308：同上 —— 坐标与他自己的 `Dealt` 一起记 ✓
                    float d = DealTo(e, dmg, p);
                    if (hit == 0) { popupAt = e.Pos; popupDealt = d; }
                    hit++;
                }
            }
            // ★★ v305：与「污水」同一条 —— **一个人都没扫到就不发弹字** ★★
            //   改前无条件发、坐标初值 = `p.Pos` ⇒ 玩家对着 3 m 外的怪喷水时
            //   （水柱射程 6.5 m，浪的半径 4.5/6.75/9.0 m —— ★v313 起）敌人常在圈外
            //   ⇒ 「海浪」每次都在**玩家脚下**弹 ⇒ 用户判定"没移到敌人身上"✗
            //   现在：`hit > 0` 才发，且坐标是第一个被扫到的敌人；`hit == 0` 只记 `WaveMisses` ✓
            WaveLastFromPlayer = p.Pos;
            if (hit <= 0) { WaveMisses++; }
            else
            {
                // ★★ v308：文本 = 「海浪 -<这一下真正扣掉的血>」★★
                //   数字来自 `DamageResult.Dealt`（`DealTo` 的返回值，即**第一个被扫到的敌人**
                //   这一下真的掉了多少）；`DamageLabel` 对 `dealt < 0.05` 返回 null
                //   ⇒ 被闪避 / 免伤（Dealt = 0）**整条弹字不发** ✓
                string waveText = PluginHitFx.DamageLabel("海浪", popupDealt);
                if (waveText != null)
                {
                    WavePops++;
                    WaveLastPopupPos = popupAt;
                    Bus.Publish(new FloatingTextEvent
                    {
                        Pos = popupAt, Text = waveText, Kind = PopupKind.DamageToEnemy,
                        // ★ 本轮（拉平字号）：1.05 → **1.00** ⇒ 20px（改前 21px）★
                        //   上屏乘积 = `26 × 0.769 × PopupStyle.DamageToEnemy(1.00) × 本值 × 插件表(1.00)`
                        //   ⇒ 这 1.05 是"海浪比普通大一号"的**唯一**来源（插件表里 Wave 本来就是 1.00）
                        //   ⇒ 用户要求"除 bomb/暴击外一律 ≤ 20px"，此处必须收回到 1.00 ✓
                        //   ⚠️ 与「污水」对称：那条的发布侧本来就是 `Scale = 1.0f`
                        //     （见上面那一支），两边现在同为 1.00 ⇒ 都是 20px ✓
                        Scale = 1.00f,
                    });
                }
            }
            // ══════════════════════════════════════════════════════════════════
            // ★★ v304：**海浪重做**（用户需求⑤）★★
            //   改前：`SpawnGroundRing` —— 一圈贴地往外摊的水环，读作"扩散波纹"，
            //         与"海浪"差得远（用户原话："现在是一个扩散水环。重做成更像海浪"）✗
            //   改后：`SpawnWaveWall` —— 玩家脚底起一道**向上卷起的浪墙**，
            //         沿**移动方向**推进 2.5m / 0.5s，浪顶带一条更亮的白沫窄带 ✓
            //   宽度仍 = `radius`（= `pm.WaveRadius`，本来就 +50%/层）⇒ 用户要的
            //   「保留宽度随层数 +50%」**一个数都没动** ✓
            //   方向：优先**移动方向**（`MoveInput`），站着不动时退到**瞄准方向** ——
            //     不这么退的话，站着喷水那一下浪墙方向会突然变成 (0,0)，墙会原地不动 ✗
            // ══════════════════════════════════════════════════════════════════
            Vector2 dir = p.MoveInput.sqrMagnitude > 1e-4f ? p.MoveInput : p.AimDir;
            // ══════════════════════════════════════════════════════════════════
            // ★★ v312：**推进距离按判定半径夹一次**（用户：「海浪的向前推进范围扩大」）★★
            // ★★ v313：**(b) 已落地** —— `WaveRadiusBase` 3.0 → **4.5**（用户：「海浪判定扩大」）★★
            //
            // ── 先查清"判定到底用哪个半径"（这一步不能猜）──
            //   判定：本函数上面那段循环 —— `rr = radius + e.Radius`，`radius = pm.WaveRadius`
            //        （`WeaponSystem:404` 写入 = `WaveRadiusBase 4.5 × (1 + 50%×(层−1))`）
            //        ⇒ **以玩家为圆心**的圆，1/2/3 层 = 4.5 / 6.75 / 9.0 m
            //   视觉：本调用 —— `advance` 只是浪墙从玩家脚下**往前推多远**，它**不参与判定**
            //   ⇒ 两个半径是**两件事**（任务书里点出的那个陷阱，确认成立）
            //
            // ── v312 当时为什么只改了视觉（(a)），以及 v313 为什么补成 (b) ──
            //   半径还是 3.0 时，若把视觉直接拉到 4.0：
            //     1 层：4.00 + 0.28 = **4.28 m** > 判定 3.0 m ⇒ 越界 **1.28 m**（42%）
            //     ⇒ 站在 3.4~4.3 m 的敌人**画面被浪糊到、血条不动** —— 正是"以为打到了其实没打到"✗
            //   所以 v312 选 (a)：adv 被夹到 `min(4.0, radius − 0.28)`（1 层 2.72 / 2·3 层 4.00）。
            //   v313 用户批准了 (b)：**判定半径本身放大到 4.5** ⇒ 夹子
            //   `min(WaveFxAdvance, radius − WaveWallBulge)` 自动放开到 4.00（半径 4.5 − 0.28 = 4.22 > 4.0）
            //   ⇒ `WaveFxAdvance` / `WaveFxSeconds` / `WaveWallBulge` **一个数都没改** ✓
            //
            // ── v313 的三层读数（`bulge` = 中段前凸 0.28 m，浪速 6.15 m/s）──
            //     1 层：判定 4.50 m｜adv = min(4.0, 4.22) = **4.00**｜最远视觉点 **4.28 m**｜比值 0.951 ✓
            //     2 层：判定 6.75 m｜adv = **4.00**                 ｜最远视觉点 **4.28 m**｜比值 0.634 ✓
            //     3 层：判定 9.00 m｜adv = **4.00**                 ｜最远视觉点 **4.28 m**｜比值 0.476 ✓
            //   ⇒ 视觉仍然永远 **≤ 判定半径**（"视觉不越判定"这条不变量在新半径下**比改前更松**）✓
            //   ⇒ 三层的最远视觉点都是 4.28（adv 全被常量 4.0 夹住）⇒ 三层都是 0.65 s 寿命、6.15 m/s
            //      （v312 时 1 层因为被夹到 2.72 而只有 0.442 s；现在 1 层也吃满）✓
            //
            // ── 时长为什么跟着 `adv` 算，而不是钉 0.65 ──
            //   钉死 0.65 的话，矮半径时的浪速会掉（v312 的 1 层：2.72 / 0.65 = 4.18 m/s，比改前还慢）。
            //   规则仍是**浪速恒定**：
            //     速度 = `WaveFxAdvance / WaveFxSeconds` = 4.0 / 0.65 = **6.15 m/s**（v312 前 5.0）
            //     时长 = adv / 速度 ⇒ 现在三层都是 **0.65 s**（adv 都是 4.00）
            //   ⇒ 半径放大后这条公式**结果变了、代码没变**，正好是"一个数改、派生量自动跟上" ✓
            // ══════════════════════════════════════════════════════════════════
            float waveSpeed = WaveFxAdvance / WaveFxSeconds;                       // 6.15 m/s
            float adv = Mathf.Min(WaveFxAdvance, radius - AttackVfx.WaveWallBulge);
            adv = Mathf.Max(0.5f, adv);
            WaveLastAdvance = adv;
            // ★ v313：供探针读"这一道的判定半径"，与 `WaveFurthestPoint` 一比就知道有没有越界
            WaveLastRadius = radius;
            float life = Mathf.Max(0.08f, adv / waveSpeed);
            AttackVfx.SpawnWaveWall(p.Pos, dir, radius, PluginFx.For(pm, WeaponSlot.Can),
                                    life, adv);
        }

        /// <summary>最后一次起浪实际用的推进距离（米）—— 探针读数：`≤ WaveRadius − WaveWallBulge`。</summary>
        public static float WaveLastAdvance { get; private set; }
        /// <summary>★ v313：**最后一次起浪用的判定半径**（米）= 那一次的 `pm.WaveRadius`
        /// （1/2/3 层 = 4.5 / 6.75 / 9.0）。探针把它与 `WaveFurthestPoint` 并排读就能现场验
        /// "视觉不越判定"（应当恒有 `WaveFurthestPoint ≤ WaveLastRadius`）。</summary>
        public static float WaveLastRadius { get; private set; }
        /// <summary>最后一次起浪**最远视觉点离玩家中心**的距离（米）= 推进 + 中段前凸。
        /// 它必须 **≤ 那一次的判定半径**（`pm.WaveRadius`）—— 这就是"视觉不越判定"的可读数证据。</summary>
        public static float WaveFurthestPoint => WaveLastAdvance + AttackVfx.WaveWallBulge;

        /// <summary>
        /// 浪墙存在时长（秒）—— ★ v312：**0.5 → 0.65**。
        /// 它与 `WaveFxAdvance` 一起只用来定**浪速**（4.0 / 0.65 = 6.15 m/s，改前 2.5 / 0.5 = 5.0）；
        /// 每一道浪的实际寿命 = `推进距离 / 浪速`（见 `OnCanAttack`）⇒ 距离被夹短时寿命自动变短，
        /// 速度恒定。为什么不是"距离照旧 4.0、时长照旧 0.5"：那样浪速 8 m/s，
        /// 0.5 秒的墙读起来是"一闪"，用户说的"太急"就是这个 ✓
        /// </summary>
        public const float WaveFxSeconds = 0.65f;
        /// <summary>
        /// 浪墙推进距离（米）—— ★ v312：**2.5 → 4.0**（+60%）。用户：「海浪的向前推进范围扩大」。
        /// ⚠️ 它是**视觉**推进，不是判定（判定是 `pm.WaveRadius` 那个以玩家为心的圆）。
        /// ★ v313：判定半径放大到 4.5/6.75/9.0 之后，夹子 `radius − WaveWallBulge` 在**三层都 ≥ 4.22**
        /// ⇒ 实际 `adv` 三层都是 **4.00**（被本常量夹住），"最远视觉点"三层都是 **4.28 m**，
        /// 恒 ≤ 判定半径 —— 推导与三层读数都在 `OnCanAttack` 里。
        /// ★ v312 写下的那句"若将来批准 (b)，这里一个数都不用改"**已经兑现**：本轮只动了
        /// `WaveRadiusBase`，本常量与 `WaveFxSeconds` / `WaveWallBulge` 一个字节没改 ✓
        /// </summary>
        public const float WaveFxAdvance = 4.0f;

        // ═══════════════════════════ 动作组：黑洞 ═══════════════════════════

        /// <summary>
        /// 黑洞：**爆炸结算之前**把半径内的敌人朝爆心拉拢（这一刻的"收口"）。
        /// 拉到的终点是"距爆心 `radius × BlackHolePullToFraction`（★v310 = 0.08）"而不是圆心本身：
        /// 全塞进 0 会让被拉的敌人互相重叠，而 `BlastDamageAt` 判的是"圆心距 ≤ radius + r"
        /// ⇒ 拉到 8% 已经**必然在爆炸范围内**，且几乎贴在中心 ✓
        /// 走的是与「潮汐」/「引力」同一个击退通道（`KnockbackVel`）⇒ 不新开一套位移系统 ✓
        /// ★ v308：这一记现在是**最后一下收口** —— 前摇那 `BlackHoleWindup`（★v310 = 1.0s）里
        ///   `TickBlackHole` 已经把范围内的人持续拖到附近；这里只把"还差的那点距离"补齐
        ///   （已经在 0.08R 内的直接跳过）✓
        /// </summary>
        public static void BlackHolePull(Vector2 center, float radius, in WeaponModifiers pm)
        {
            if (!pm.BlackHole || _world == null || radius <= 0f) return;
            // 前摇结束 / 爆炸那一帧 ⇒ 场地**全部**关掉（下一颗黑洞由 `BlackHoleTelegraph` 重新开）
            // ★ v311：一格 → 多格之后这里必须**全清**：连环的 3 个落点在同一帧各炸一次，
            //   谁的 `BlackHolePull` 先跑到都只关一次，后面的调用照样拿得到自己那个 center ✓
            ClearBlackHoles();
            float want = radius * Mathf.Clamp01(BlackHolePullToFraction);
            List<EnemyActor> es = _world.Enemies;
            for (int i = 0; i < es.Count; i++)
            {
                EnemyActor e = es[i];
                if (e == null || e.IsDead) continue;
                Vector2 to = center - e.Pos;
                float d = to.magnitude;
                if (d <= want + 0.01f || d < 0.0001f) continue;   // 已经在里面 ⇒ 不推
                if (d > radius) continue;                          // 只吸"这一次爆炸罩得住"的人
                e.KnockbackVel += to / d * ((d - want) * KnockbackPerMeter);
                BlackHolePulled++;
            }
        }

        /// <summary>
        /// ★ v308：**每固定帧的持续吸附**（`Tick` 里调，只在前摇窗口内跑）。
        /// 拉力曲线：`v = cruise × (1 + power·k²)`，`k` = 这一场吸引的进度 0→1
        ///   · `k = 0`：5.2 m/s（一开场就明显在拖，不是"最后才动"）
        ///   · `k = 1`：5.2 × (1+2.6) ≈ **18.7 m/s**（末段猛地被吸进去）
        /// 方向 = 指向爆心；**只作用于半径内的敌人**（半径外的人这一爆本来也打不到）。
        /// 已经在 `0.08R`（★v310）内的不再施力（否则它们会在圆心附近来回抖）✓
        /// ★ v310：窗口从 0.25s 拉长到 **1.0s**，曲线与速度值**一个数都没动** ⇒
        ///   `k` 仍然 0→1、仍然 `5.2 × (1 + 2.6k²)`，只是"爬满这条曲线"用了 1 秒而不是 0.25 秒
        ///   ⇒ 同样的总位移分摊到 4 倍的时间里 = **拖得更稳、更不像瞬移**（手感上是变强，见交付说明）✓
        /// </summary>
        private static void TickBlackHole(float dt)
        {
            if (dt <= 0f) return;
            if (_world == null) { ClearBlackHoles(); return; }
            float total = Mathf.Max(0.01f, BlackHoleWindup);
            List<EnemyActor> es = _world.Enemies;
            // ★ v311：逐槽推进 —— 连环 3 落点各占一格，各吸各的（改前只有一格，后开的覆盖先开的）
            for (int s = 0; s < BlackHoleSlots; s++)
            {
                if (!_bhOn[s]) continue;
                _bhLeft[s] -= dt;
                if (_bhLeft[s] <= 0f) { _bhOn[s] = false; _bhLeft[s] = 0f; continue; }

                // k：这一格已经吸了多久（0 = 刚开，1 = 到点）
                float k = Mathf.Clamp01(1f - _bhLeft[s] / total);
                float speed = BlackHoleCruiseSpeed * (1f + BlackHolePullPower * k * k);
                float keep = _bhRadius[s] * Mathf.Clamp01(BlackHolePullToFraction);
                float r2 = _bhRadius[s] * _bhRadius[s];

                for (int i = 0; i < es.Count; i++)
                {
                    EnemyActor e = es[i];
                    if (e == null || e.IsDead) continue;
                    Vector2 to = _bhCenter[s] - e.Pos;
                    float d = to.magnitude;
                    if (d <= keep + 0.02f || d < 0.0001f) continue;    // 已经吸到内圈 ⇒ 不再推
                    if ((e.Pos - _bhCenter[s]).sqrMagnitude > r2) continue;
                    // 直接赋值（不累加）：每帧的速度大小是解析量，与帧数无关 ⇒ 可复现
                    // ⚠️ 多格同时罩到同一个敌人时，**后面的格子赢**（下标顺序固定 ⇒ 仍然确定性）。
                    //    三个落点相距 `ChainBurstGap`(2.5m)、爆心半径 3m ⇒ 重叠区很小，
                    //    且两边的力都指向"离自己更近的那个心"，视觉上都是"被吸进去" ✓
                    e.KnockbackVel = to / d * speed;
                }
            }
        }

        /// <summary>
        /// 黑洞：**吸引阶段的入口**（爆炸前 `BlackHoleWindup` 秒那**一帧**由
        /// `RunSession.TickPendingBlasts` 调，而它对**每一颗** `PendingBlast` 恰好触发一次 ⇒
        /// 连环的每个落点都会开自己那一套 ✓）。
        /// 做两件事：① 开一个**吸附场地**（之后由 `Tick` 每固定帧持续拉人，见 `TickBlackHole`）；
        /// ② 放**四层**视觉（`AttackVfx.SpawnCollapseRing`：贴地暗环 + 旋转螺旋臂 + 中心暗球 +
        ///    边缘暗紫外光，程序化、无美术资产）✓
        /// ★ v310：`BlackHoleWindup` = 1.0s ⇒ 这些层一共存在整整 1 秒（"持续可见的旋转漩涡"）
        /// ★★ v311：**每个落点一套**，套数 `spots` 一起传下去 ⇒ `AttackVfx` 按套数缩每一套，
        ///    3 套同时在场时 `Bit` 峰值 57 ≤ 64（池账在 `SpawnCollapseRing` 的注释里）✓
        /// ⚠️ 只在这里改 `_bhOn[slot]`（一次性）；`BlackHolePull` 负责在爆炸那一帧关掉 ⇒
        ///    整个窗口恰好是"从前摇起点到爆炸"，不需要任何额外的计时器 ✓
        /// </summary>
        /// <param name="spot">这是第几个落点（0 = 主落点）。
        /// ★ v311：它决定**吸附场地占哪一格**，也是"每个落点各开一套"的下标来源。</param>
        /// <param name="spots">这一次投掷一共几个落点（1/2/3）⇒ 决定单套漩涡的档位。</param>
        public static void BlackHoleTelegraph(Vector2 center, float radius, in WeaponModifiers pm,
                                              int spot = 0, int spots = 1)
        {
            if (!pm.BlackHole || radius <= 0f) return;
            int slot = ShotSlot(spot);
            _bhOn[slot] = true;
            _bhCenter[slot] = center;
            _bhRadius[slot] = Mathf.Max(0.35f, radius);
            _bhLeft[slot] = Mathf.Max(0.01f, BlackHoleWindup);
            AttackVfx.SpawnCollapseRing(center, radius, PluginFx.For(pm, WeaponSlot.Fertilizer),
                                        BlackHoleWindup, spots);
        }

        // ─────────────────────── 伏雷 × 黑洞（★ v311）───────────────────────
        /// <summary>
        /// ★★ v311 修 bug：**伏雷 + 黑洞**（用户：「黑洞…搭配伏雷会导致前摇消失」）★★
        ///
        /// ── 根因 ──
        ///   `RunSession.ArmBlast` 在装了伏雷（`pm.MineHitPct > 0`）时**只排雷就 `return`**，
        ///   一颗 `PendingBlast` 都不排；而"开漩涡"这件事原来唯一挂在
        ///   `RunSession.TickPendingBlasts` 那条"`b.T` 跨过 `BlackHoleWindup`"的判据上
        ///   ⇒ 队列永远是空的 ⇒ 前摇一次都不会发生 ✗（"吸"还在，但它是爆炸那一帧的一记瞬移，
        ///   既看不见也没有"被拖过去"的过程。）
        ///
        /// ── 修法：把黑洞从"跟着投掷走"改成"跟着雷走" ──
        ///   雷有**自己的引信**（`RunSession` 排雷时 `ArmTime` 上膛、之后 `PluginField.MineFuse`
        ///   = 6 秒自爆），那**就是它的前摇** ⇒ 不需要、也不该再额外加 1 秒：
        ///     · **超时自爆**：在 `PluginField.TickMines` 里，`FuseLeft` 跨过 `BlackHoleWindup`
        ///       的那一帧调本方法 ⇒ 玩家的观感是"埋下的雷在最后 1 秒开始转漩涡、把人往中心拖"，
        ///       然后雷炸 —— 与投掷那一路的 1 秒前摇**完全同构**（同一个 `BlackHoleWindup`、
        ///       同一条吸附曲线、同一套漩涡）✓
        ///     · **被踩中爆**：踩中是**瞬时事件**，没有"前 1 秒"可以挂 ⇒ 只能
        ///       「炸的同时闪一次漩涡」，见 `BlackHoleMineBurst`。这里刻意**不去**把爆炸推迟
        ///       1 秒 —— 那会让"踩中就炸"的反馈延迟一整个前摇，伏雷就不再是伏雷了 ✗
        /// </summary>
        public static void BlackHoleMineWindup(Vector2 center, float radius, int spots)
        {
            if (radius <= 0f) return;
            int slot = ClaimBlackHoleSlot();
            _bhOn[slot] = true;
            _bhCenter[slot] = center;
            _bhRadius[slot] = Mathf.Max(0.35f, radius);
            _bhLeft[slot] = Mathf.Max(0.01f, BlackHoleWindup);
            AttackVfx.SpawnCollapseRing(center, radius,
                                        PluginFx.For(ModsOf(WeaponSlot.Fertilizer), WeaponSlot.Fertilizer),
                                        BlackHoleWindup, spots);
        }

        /// <summary>
        /// ★★ v311：伏雷**被踩爆**时的黑洞记号（没有前摇可等，见 `BlackHoleMineWindup` 的说明）。
        /// 只画漩涡、**不开吸附场** —— 因为同一帧 `RunSession.ExplodeAt` 会先调
        /// `BlackHolePull`（把范围内的敌人"收口"到 8%R）再结算伤害，那一次收口就是这一爆的"吸"；
        /// 再开一个 1 秒的场反而会让"炸完还在吸"，与"雷已炸掉"矛盾 ✗
        /// ⚠️ 用**最省档**（`spots = 0` ⇒ 17 格），因为它和爆炸的 44 格同帧：17 + 44 = 61 ≤ 64 ✓
        /// </summary>
        public static void BlackHoleMineBurst(Vector2 center, float radius)
        {
            if (radius <= 0f) return;
            AttackVfx.SpawnCollapseRing(center, radius,
                                        PluginFx.For(ModsOf(WeaponSlot.Fertilizer), WeaponSlot.Fertilizer),
                                        BlackHoleMineBurstLife, 0);
        }

        // ═══════════════════════════ 小工具 ═══════════════════════════

        /// <summary>派生伤害：**不摘印**（与子爆/回响同一条判据 —— 只有直击本体才摘印）。
        ///
        /// ★ v308：**改成返回"真正扣掉了多少"**（`DamageResult.Dealt`）★
        ///   用户要求三种特殊弹字（污水 / 感电 / 海浪）都带上**实际造成的伤害数值** →
        ///   而本方法是那三条**唯一的结算出口** ⇒ 返回值就是它们能拿到的最准的那个数：
        ///     · 不是 `damage`（那是"算出来的量"，会被血条截断、会被闪避归零）✗
        ///     · 是 `Dealt`（`DamageResolver.Resolve` 写出的"真正从血/盾里扣掉的量"）✓
        ///   被闪避 / 入场免伤 / 目标已死 ⇒ 返回 0 ⇒ 调用方据此**不发**弹字 ✓
        /// </summary>
        private static float DealTo(EnemyActor e, float damage, Actor source)
        {
            if (e == null || e.IsDead || damage <= 0f || _world == null) return 0f;
            DamageResult r = _world.DealDamage(new DamageRequest
            {
                Type = DamageType.Physical,
                RawValue = damage,
                Source = source,
                Target = e,
                Knockback = 0f,
                Crit = false,
                NoMarkConsume = true,
            });
            return r.Dealt;
        }

        /// <summary>当前手持的那把武器（`WeaponIndex` 由 `Actors` 定死 0/1/2，与 `WeaponSlot` 同序）。</summary>
        private static WeaponSlot CurrentSlot()
        {
            if (_player == null) return WeaponSlot.None;
            switch (_player.WeaponIndex)
            {
                case 0: return WeaponSlot.Hoe;
                case 1: return WeaponSlot.Can;
                case 2: return WeaponSlot.Fertilizer;
                default: return WeaponSlot.None;
            }
        }

        /// <summary>取某把武器当前的聚合修正（每次现算、不缓存 —— 理由与上一轮同：缓存要处理失效，比它更贵）。</summary>
        private static WeaponModifiers ModsOf(WeaponSlot slot)
            => _goods == null || slot == WeaponSlot.None
                ? WeaponModifiers.None
                : WeaponSystem.Aggregate(_goods, slot);
    }
}