using UnityEngine;
using UnityEngine.SceneManagement;

namespace Seedfall
{
    /// <summary>
    /// ★★ v218：**战斗反馈层** —— 镜头抖动（累加器）＋ 时间停止（hitstop）★★
    /// ★★ v224：爆炸震动 **×2.10 幅度 / +32% 时长 / 按距离衰减**＋**白闪 / 受击红晕**（见 `CombatFeedback.Post.cs`）★★
    ///
    /// 用户原话：「为战斗模式添加效果，爆炸带来镜头抖动，暴击带来轻微抖动，
    ///            抽卡的融合过程增加动态演示，boss死亡掉落时增加时间停止效果」
    /// 追加（v224）：「爆炸产生的画面震动加强」「为画面加一些炫酷的效果」
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么抖动是**累加器 + trauma²**，而不是各自动相机
    /// ══════════════════════════════════════════════════════════════════════
    /// 相机的位置由 `GameBootstrap.UpdateCamera` **每帧绝对赋值**（跟焦 + 板边裁剪）。
    /// 若在各处写 `Camera.main.transform.position += …`：
    ///   ① 会被下一帧的跟焦**覆盖**（爆炸抖一下就没）✗
    ///   ② 两个来源同时触发时**直接相加** ⇒ 爆炸＋暴击 = 抖动翻倍到失控 ✗
    /// 所以这里只做**一个 trauma 累加器**（0..`TraumaMax`）：
    ///   · 各来源 `Add(trauma)`；v224 起是「**取最大 + 小的按 30% 叠**」（见 `Add`）
    ///   · 位移 = `trauma² × 幅度` ⇒ 平方让"小的那一点叠加"几乎不放大，**天然抗失控**
    ///     （两个 0.5：取 max 0.5 + 0.30×0.5 = 0.65 ⇒ 位移 0.4225 而非 2 倍）✓
    ///   · 在 `LateUpdate`（`[DefaultExecutionOrder(30000)]`）里**最后一步**叠加到相机上
    ///     ⇒ 与跟焦不打架：跟焦写"应有的位置"，本类写"这一帧该抖多少"，职责不重叠 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、时间停止：**必须用 `unscaledDeltaTime` 计时**
    /// ══════════════════════════════════════════════════════════════════════
    /// `timeScale = 0` 之后 `Time.deltaTime` 也就是 0 ⇒ 用缩放时间计时的恢复计时器
    /// **永远走不动** ⇒ 游戏永久卡住、回不来 ✗（这是本类最要紧的一行）
    /// 所以：计时 `Time.unscaledDeltaTime`、恢复时把 `timeScale` **写回进入前的那个值**
    /// （本工程不用 timeScale 做别的用途，正常是 1 ⇒ "恢复后逐位回到 1" 可断言）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、为什么一律加 `Application.isPlaying` 闸门
    /// ══════════════════════════════════════════════════════════════════════
    /// `SelfCheck` 会直接调 `RunSession` 的战斗路径（爆炸 / boss 死亡都在里面）。
    /// 若它在**编辑模式**跑时把 `Time.timeScale` 写成 0，编辑器会当场冻住 ✗
    /// ⇒ 所有写引擎状态的分支都先问 `Application.isPlaying`；
    ///   不满足就只记账（`_statRequested++`），**绝不**碰 `Time.timeScale` 与相机 ✓
    ///
    /// ⚠️ 本类**不消耗** `World.Rng`（抖动用 `Time.unscaledTime` 的哈希）⇒ 一局的确定性不变 ✓
    /// </summary>
    public static class CombatFeedback
    {
        // ══════════════════════════════════════════════════════════════════
        // ★★ v224：**爆炸震动加强**（用户原话：「爆炸产生的画面震动加强」）★★
        //
        // ── 改前 → 改后（世界单位；峰值 = trauma² × 幅度）──
        //   幅度 `ExplosionAmp`     0.20 → **0.42**   （×2.10，落在要求的 1.8~2.2 倍内）
        //   时长 `ExplosionDecay`   0.22 → **0.29**   （+31.8%，落在要求的 +30%）
        //   单次爆炸峰值位移        0.2000 → **0.4200**（×2.10）
        //   硬上限 `TraumaMax`      1.00 → **1.20**   （给"连炸"留 20% 成长，再高会失控）
        //   叠加方式：**取最大 + 小的按 30% 叠**（`TraumaStackRatio`），**不再直接相加**
        //       · 两记同时炸：trauma = min(1.20, 1.00 + 0.30×1.00 = 1.30) = 1.20
        //         ⇒ 位移 = 1.20² × 0.42 = **0.6048**（单次的 1.44 倍）
        //         （改前同样两记：trauma 被 1.00 封顶 ⇒ 0.2000 = 单次的 **1.00 倍**，连炸等于不叠）
        //       · 暴击叠爆炸：trauma = min(1.20, 1.00 + 0.30×0.45 = 1.135) ⇒ 1.288 × 0.42 = 0.5410
        //       · 旧写法的 `Trauma += amount`：两记满额就封到 1.00，看似"稳"，
        //         但再叠一记暴击也毫无变化 ⇒ 手感读成"抖了一下就没了"；新写法是"一记重击 + 一点余震" ✓
        //
        // ── 距离衰减（以**玩家**为参照，离爆心越远越弱）──
        //   factor = lerp(1, 0.35, smoothstep(4m, 18m, dist))，作用在**位移幅度**上：
        //     · ≤ 4 米（炸在脚边）：1.00 ⇒ 位移 0.4200（单次峰值，满额 ×2.10）
        //     · 11 米（中场）      ：≈ 0.62 ⇒ 位移 ≈ 0.26（比改前满额 0.20 还强 30%）
        //     · ≥ 18 米（远角）    ：0.35 ⇒ 位移 0.1470（改前恒为 0.2000 的 73%）
        //   ⇒ **近处更狠、远处更轻**：爆炸的"分量"从屏幕中心传出去，而不是全屏同幅硬抖 ✓
        //   ⚠️ 爆心从 `BlastDamageAt` 末尾那条 `EnemyAttackFieldEvent` 拿到（见 `NoteBlastOrigin`）；
        //      拿不到就**不衰减**（factor=1，即等于"满额"，不会把抖动做没）✓
        //
        // ── 暴击一概不动（用户只要求加强爆炸）──
        //   0.04 / 0.08s / trauma 0.45 ⇒ 峰值 0.0081（与 v218 逐位相同）✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>爆炸：世界单位峰值位移（trauma=1、无距离衰减时）。v224：0.20 → 0.42。</summary>
        public const float ExplosionAmp = 0.42f;
        /// <summary>爆炸：从满 trauma 衰减到 0 的秒数。v224：0.22 → 0.29（+31.8%）。</summary>
        public const float ExplosionDecay = 0.29f;
        /// <summary>爆炸：投入的 trauma 量。</summary>
        public const float ExplosionTrauma = 1.00f;

        /// <summary>连炸叠加比例：小的那一份只按 30% 叠上去（**不是直接相加**）。</summary>
        public const float TraumaStackRatio = 0.30f;

        /// <summary>距离衰减：这个距离以内满幅（米，世界单位）。</summary>
        public const float BlastNear = 4f;
        /// <summary>距离衰减：这个距离以外只剩 `BlastFloor`（米，世界单位）。</summary>
        public const float BlastFar = 18f;
        /// <summary>距离衰减的下限系数（远处仍有 35% 的"余震"，不是完全静音）。</summary>
        public const float BlastFloor = 0.35f;

        /// <summary>暴击：峰值位移（"轻微"—— 爆炸的 1/5）。</summary>
        public const float CritAmp = 0.04f;
        /// <summary>暴击：衰减秒数。</summary>
        public const float CritDecay = 0.08f;
        /// <summary>暴击：投入的 trauma 量（0.45 ⇒ 单独触发时位移 = 0.45²×0.04 ≈ 0.008）。</summary>
        public const float CritTrauma = 0.45f;

        /// <summary>
        /// 叠加时的硬上限。v224：1.00 → 1.20。
        /// 判据：位移 = trauma² × 幅度 ⇒ 1.20² = 1.44 ⇒ **最坏情况也只有单次的 1.44 倍**（不到 1.5×，
        /// 与 v218 那条"同时触发不超单次 1.5 倍"的纪律同口径，只是把余量从 0 放到 20%）✓
        /// </summary>
        public const float TraumaMax = 1.20f;

        // ── 时间停止（boss 掉落）：用户给的区间 0.25~0.4s ──
        /// <summary>停帧时长（秒，未缩放时间）。</summary>
        public const float HitStopSeconds = 0.32f;
        /// <summary>
        /// ★ v263：停帧的**墙钟宽限**（秒）—— 绝对终点 `_hitStopEndsAt` 过后再等这么久，
        /// 若 `timeScale` 还没回 1 就由安全网强行拉回（防"计时没人推进"的残局）✓
        /// </summary>
        public const float HitStopGrace = 0.10f;
        /// <summary>时间停止期间的 timeScale（0.05 = 几乎停住但还有一点动，比硬 0 更有"顿"感）。</summary>
        public const float HitStopScale = 0.05f;

        // ── 状态 ──
        /// <summary>当前 trauma（0..`TraumaMax`，即 0..1.20）。所有来源往这里加。</summary>
        public static float Trauma { get; private set; }
        /// <summary>最近一次爆炸的峰值位移（读数用；**已含**距离衰减）。</summary>
        public static float LastExplosionPeak { get; private set; }
        public static float LastCritPeak { get; private set; }
        /// <summary>累计请求次数（自检/编辑模式下只记账、不写引擎状态）。</summary>
        public static int StatRequested { get; private set; }
        /// <summary>时间停止剩余秒数（**未缩放**时间口径）。</summary>
        public static float HitStopLeft { get; private set; }
        /// <summary>进入时间停止前的 `Time.timeScale`（**只作读数**；恢复一律写 1，见 `Tick`）。</summary>
        private static float _scaleBefore = 1f;
        /// <summary>本次停帧的**绝对终点**（未缩放时间）。续时不能超过它 ⇒ 时长恒 ≤ `HitStopSeconds`。</summary>
        private static float _hitStopEndsAt;
        /// <summary>本次会话里时间停止触发过几次。</summary>
        public static int HitStopCount { get; private set; }
        /// <summary>
        /// ★ v314：最近一次 `HitStopFor(seconds)` 请求的**标称时长**（读数用；boss 那一路不写它）。
        /// 用来在桥/探针里核对"表里的 ruleValue = 2 ⇒ 请求的就是 2s"。
        /// </summary>
        public static float LastHitStopSeconds { get; private set; }

        private static CombatFeedbackRig _rig;

        // ── v224：距离衰减的运行时状态 ──
        /// <summary>最近一次爆炸的**未衰减**峰值（读数用；用来和衰减后的值对照）。</summary>
        public static float LastExplosionPeakRaw { get; private set; }
        /// <summary>最近一次爆炸实际用的距离衰减系数（0.35~1.00）。</summary>
        public static float LastExplosionAtten { get; private set; } = 1f;
        /// <summary>当前生效的衰减系数（`Offset` 乘它）。</summary>
        public static float BlastAtten { get; private set; } = 1f;
        /// <summary>最近一次爆炸的爆心 ↔ 玩家距离（米；没拿到爆心时是 0）。</summary>
        public static float LastBlastDistance { get; private set; }
        /// <summary>累计的爆炸抖动请求次数（`StatRequested` 把暴击/时间停止也算进去了，这个是爆炸专用）。</summary>
        public static int ExplosionCount { get; private set; }

        /// <summary>还在等"同一帧里那一条爆心事件"补位置。</summary>
        private static bool _seekOrigin;
        /// <summary>爆心是否有效（拿到就当帧用掉，用完立刻置假 ⇒ 不会把上一次的爆心套到这一次）。</summary>
        private static bool _blastPosValid;
        /// <summary>本次爆炸的爆心（世界平面坐标，与 `Actor.Pos` 同口径）。</summary>
        private static Vector2 _blastPos;
        /// <summary>本帧还有一记爆炸没决定衰减系数（决定在 `Tick` 里做，那时爆心已经到了）。</summary>
        private static bool _attenPending;
        /// <summary>衰减系数保持到什么时候（未缩放时间）——超过这个时刻就回落到 1.00。</summary>
        private static float _attenHoldUntil = -1f;

        /// <summary>爆炸抖动（幅度大）。**爆心未知** ⇒ 不做距离衰减（等于满额）。</summary>
        public static void ShakeExplosion() => ShakeExplosion(null);

        /// <summary>
        /// 爆炸抖动，已知爆心 ⇒ 位移按"**玩家** ↔ 爆心"的距离衰减（见文件头的数字表）。
        ///
        /// ★ 为什么爆心是个可选参数、而不是必填 ★
        ///   唯一的调用点是 `RunSession.BlastDamageAt`（本轮不让改）⇒ 旧签名必须照样能编。
        ///   真实爆心由**同一帧**里 `BlastDamageAt` 末尾发的 `EnemyAttackFieldEvent` 补上
        ///   （`CombatFeedbackFxHost` 订阅 → `NoteBlastOrigin`）：
        ///     逻辑先跑（`Update`/`FixedTick`）→ 事件在本帧末之前到达 → `LateUpdate` 的 `Tick` 才拍衰减系数
        ///   ⇒ 画面用的仍是**这一次**爆炸的爆心 ✓
        /// ★ 为什么不是"即时算" ★
        ///   `ShakeExplosion()` 在 `BlastDamageAt` 的**第一行**、事件在**最后一行** ⇒
        ///   同一帧里先抖后发，即时算只能拿到上一记的爆心 ✗ ⇒ 所以延到 `Tick`（同一帧，晚但正确）✓
        /// </summary>
        public static void ShakeExplosion(Vector2? blastCenter)
        {
            StatRequested++;
            ExplosionCount++;
            LastExplosionPeakRaw = ExplosionTrauma * ExplosionTrauma * ExplosionAmp;

            if (blastCenter.HasValue)
            {
                _blastPos = blastCenter.Value;
                _blastPosValid = true;
                _seekOrigin = false;
            }
            else
            {
                _seekOrigin = true;     // 等本帧那条 `EnemyAttackFieldEvent` 把爆心递过来
            }
            _attenPending = true;

            // trauma **立刻**入累加器（读数与编辑模式下的自检都马上看得见），
            // 衰减系数只作用在 `Offset` 的幅度上 ⇒ 两者互不干扰 ✓
            Add(ExplosionTrauma, ExplosionDecay);

            if (!Application.isPlaying)        // 编辑模式：不吃爆心、不闪屏（只记账）
            {
                LastExplosionAtten = 1f;
                LastExplosionPeak = LastExplosionPeakRaw;
                _attenPending = false;
                _blastPosValid = false;
                _seekOrigin = false;
            }
        }

        /// <summary>
        /// 记下一条爆心（由 `CombatFeedbackFxHost` 订阅 `EnemyAttackFieldEvent` /
        /// `FertilizerBlastEvent` 后转发）。
        /// **只在"本帧有一记爆炸正等着爆心"时收** —— 否则敌人自己放的范围技（跳跃/自爆/激光）
        /// 也会发同一种事件，随手记下就会把抖动按到别人的坐标上去 ✗
        /// </summary>
        public static void NoteBlastOrigin(Vector2 pos)
        {
            if (!_seekOrigin) return;
            _blastPos = pos;
            _blastPosValid = true;
            _seekOrigin = false;
        }

        /// <summary>
        /// 距离衰减系数：玩家离爆心越远越小（`BlastNear` 内 1.00，`BlastFar` 外 `BlastFloor`）。
        /// 拿不到玩家或爆心 ⇒ 1.00（**不衰减**，宁可震得足也不要把抖动做没）✓
        /// </summary>
        public static float BlastAttenuationFor(Vector2 blastPos, Vector2 playerPos)
        {
            float d = Vector2.Distance(playerPos, blastPos);
            float t = Mathf.InverseLerp(BlastNear, BlastFar, d);
            return Mathf.Lerp(1f, BlastFloor, Mathf.SmoothStep(0f, 1f, t));
        }

        /// <summary>玩家位置（世界平面坐标）。没有活会话时回落到相机的 xz（相机就跟着玩家）。</summary>
        private static bool TryPlayerPosition(out Vector2 pos)
        {
            var live = GameBootstrap.Live;
            for (int i = 0; i < live.Count; i++)
            {
                GameBootstrap b = live[i];
                if (b == null) continue;
                RunSession s = b.Session;
                if (s == null || s.World == null || s.World.Player == null) continue;
                pos = s.World.Player.Pos;
                return true;
            }
            Camera cam = Camera.main;
            if (cam != null)
            {
                // 世界坐标 ↔ 玩法坐标：`GameBootstrap.UpdateCamera` 用 `(focus.x, 0, focus.y)`
                // ⇒ 反向取 `(cam.x, cam.z)` 就是玩家附近的地面点 ✓
                Vector3 p = cam.transform.position;
                pos = new Vector2(p.x, p.z);
                return true;
            }
            pos = Vector2.zero;
            return false;
        }

        /// <summary>本帧该用哪个衰减系数（在 `Tick` 里算一次，之后 `ExplosionDecay` 秒内沿用）。</summary>
        private static float ResolveAttenuation()
        {
            LastBlastDistance = 0f;
            if (!_blastPosValid) return 1f;
            Vector2 player;
            if (!TryPlayerPosition(out player)) return 1f;
            LastBlastDistance = Vector2.Distance(player, _blastPos);
            return BlastAttenuationFor(_blastPos, player);
        }

        /// <summary>暴击抖动（轻微）。</summary>
        public static void ShakeCrit()
        {
            StatRequested++;
            LastCritPeak = CritTrauma * CritTrauma * CritAmp;
            Add(CritTrauma, CritDecay);
        }

        /// <summary>
        /// boss 死亡掉落 ⇒ 时间停止（期间掉落物才弹出）。
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// ★ v263：修「爆炸后玩家/敌人全都不动，但 HUD/时钟还在走」★
        /// ══════════════════════════════════════════════════════════════════
        /// 症状是"scaled 时间的东西全停、unscaled 时间的东西照走"的典型形态 ⇒ 病根只可能是
        /// `Time.timeScale` 被留在低位（`HitStopScale = 0.05`）。旧实现有三处会留下它：
        ///
        ///   ① **嵌套重捕获**：守卫只认 `timeScale ≈ 0.05`。
        ///      两次停帧之间只要有任何一处把 `timeScale` 写成别的值（例：宿主兜底写了 1、
        ///      而 `HitStopLeft` 还 > 0），第二次进入就会把 `_scaleBefore` **重捕获**成那个值，
        ///      退出时再写回去 ⇒ "恢复"变成了"回到上一手留下的状态"，而不是回到 1 ✗
        ///      修法：守卫改成 **`HitStopLeft > 0f` 就一律不重捕获**（只续时）。
        ///   ② **续时无上限**：旧实现每次调用都 `HitStopLeft = HitStopSeconds` ⇒ 只要有一路
        ///      每帧/高频地调 `HitStopBoss()`（连炸、连杀），计时永远回不到 0 ⇒ `0.05` 永久 ✗
        ///      修法：引入绝对终点 `_hitStopEndsAt`，续时只能**变小不能变大** ⇒ 停帧时长恒 ≤ 0.32s。
        ///      ★ v314 追加口径：这条"≤ 0.32s"说的是**本方法**（boss 掉落）。
        ///        「天使降临」的 2 秒停帧走 `HitStopFor(seconds)`（另一个入口，同一个状态机），
        ///        它**允许把终点延后** —— 详见那个方法的第二节（为什么必须允许、以及它靠什么保证恢复）✓
        ///   ③ **恢复值是"进入前的值"而不是 1**：全库没有任何别的地方写 `timeScale`
        ///      （唯一写入点就是本文件 + `GlobalJuice.RestoreTimeScaleFallback`）⇒ 正常值只能是 1，
        ///      于是这里**捕获不再有语义**，恢复一律写 1（见 `Tick`）✓
        /// </summary>
        public static void HitStopBoss()
        {
            StatRequested++;
            HitStopCount++;
            if (!Application.isPlaying)
            {
                HitStopLeft = HitStopSeconds;           // 编辑模式/自检：只记账，绝不碰引擎
                return;
            }
            EnsureRig();

            float now = Time.unscaledTime;

            // ① 嵌套守卫：计时还在走 ⇒ 只续时、**不重捕获**（也绝不重复写 0.05）
            if (HitStopLeft > 0f)
            {
                float remain = Mathf.Max(0f, _hitStopEndsAt - now);   // ② 封顶在绝对终点
                HitStopLeft = Mathf.Max(HitStopLeft, remain);
                return;
            }

            _scaleBefore = Time.timeScale;      // 只为读数/兼容保留；恢复一律写 1（见 Tick）
            _hitStopEndsAt = now + HitStopSeconds;
            HitStopLeft = HitStopSeconds;
            Time.timeScale = HitStopScale;
        }

        /// <summary>
        /// ★★ v314：**任意时长的停帧**（本轮唯一使用者：道具「天使降临」的复活 2 秒）★★
        ///
        /// 用户原话：「获得时死亡会暂停时间 2 秒，天使羽毛飘落，并完成一次复活，
        ///            恢复所有血量，时间恢复正常」。
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// 一、**没有**新开一条时间通道 —— 复用的就是本类这套状态机
        /// ══════════════════════════════════════════════════════════════════
        /// 同一份 `HitStopLeft` / `_hitStopEndsAt` / `Time.timeScale` / rig，只是"时长"从常量变成参数。
        /// ★ 为什么不直接写 `Time.timeScale = 0` ★
        ///   本工程真出过"卡在低 timeScale 不恢复"的事故（`HitStopBoss` 的注释里有完整病根清单）。
        ///   那套修复 = ① 嵌套守卫 ② 绝对终点 ③ 恢复一律写 1，全部长在**这一个状态机**里 ⇒
        ///   新开一条通道就等于把这三条纪律再抄一遍，而抄漏一条的症状是"游戏永久慢放"✗
        ///
        /// ── 为什么与 boss 停帧**用同一个枚举**（`HitStopBoss` 一个字节没改）──
        ///   表现上它们本来就该是同一件事（都是"时间顿住"）。差别只有时长，所以：
        ///     · boss 掉落的 0.32s 走 `HitStopBoss()`（原样）
        ///     · 复活 2s 走本方法
        ///   两者**同时发生**时由同一条规则裁决：终点取**更晚**的那个（不会互相截断，见下）。
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// 二、"2 秒后一定恢复"的五道保险（用户铁律：不能卡在低 timeScale）
        /// ══════════════════════════════════════════════════════════════════
        ///   ① **计时用未缩放时间**：`Tick` 里 `HitStopLeft -= Time.unscaledDeltaTime`
        ///      ⇒ 哪怕 `timeScale = 0.05`，这个计时器照样按墙钟走完（用缩放时间会永远回不来）✓
        ///   ② **绝对终点** `_hitStopEndsAt = Time.unscaledTime + seconds`：`Time.unscaledTime`
        ///      与"有没有人推进粒子/逻辑"无关 ⇒ 它天然会过去 ✓
        ///   ③ **安全网 `GuardTimeScale`**：每帧由两个常驻 DDOL 宿主各调一次
        ///      （`CombatFeedbackRig.LateUpdate` + `GlobalJuiceFxHost.LateUpdate`）——
        ///      判据是"墙钟已过 `_hitStopEndsAt + HitStopGrace` 却 scale 还偏低 ⇒ 强行写 1"
        ///      ⇒ 就算 rig 自己没了（`Tick` 没人调），墙钟一过也一定恢复 ✓
        ///   ④ **宿主被禁用/销毁** ⇒ `OnDisable` / `OnDestroy` → `ForceRestoreTimeScale()` ✓
        ///   ⑤ **换局 / 退战斗** ⇒ `ResetAll()` 里那句"scale 偏低就写 1" ✓
        ///   ⚠️ 暂停菜单**不冲突**：本工程的暂停是**固定步闸门**（`Session.Paused`），
        ///     全程不碰 `Time.timeScale`（见 `RunSession.cs:4787` 那段）。所以暂停菜单打开时
        ///     停帧照旧按墙钟走完，不会出现"暂停里冻住、回来后还冻着"✓
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// 三、与 `HitStopBoss` 的唯一行为差异：**允许延长**
        /// ══════════════════════════════════════════════════════════════════
        /// `HitStopBoss` 的嵌套分支是"续时只能变小"（保证 boss 停帧恒 ≤ 0.32s）。
        /// 本方法不能照抄：若 boss 停帧正在走（剩 0.2s）而玩家此刻复活，需要的是**完整的 2 秒**，
        /// 截到 0.2s 会让"暂停 2 秒"变成一句空话 ✗
        /// 所以这里只在"新终点更晚"时延长 `_hitStopEndsAt`，且**永远以墙钟为基准**
        /// ⇒ 上限是 `max(已有终点, now + seconds)`，**不可能无限续**（每次请求都有确定的终点）✓
        /// ★ 它不影响 boss 那一条：`HitStopBoss()` 自己的分支一行没改 ✓
        /// </summary>
        public static void HitStopFor(float seconds)
        {
            if (!(seconds > 0f)) return;
            StatRequested++;
            HitStopCount++;
            LastHitStopSeconds = seconds;

            if (!Application.isPlaying)
            {
                // 编辑模式 / 无头自检：**只记账，绝不碰引擎状态**（与 `HitStopBoss` 同一条纪律）
                HitStopLeft = Mathf.Max(HitStopLeft, seconds);
                return;
            }

            EnsureRig();
            float now = Time.unscaledTime;
            float end = now + seconds;

            // 已经在停（boss 掉落的 0.32s / 上一记复活）：只延长到**更晚**的那个终点
            if (HitStopLeft > 0f)
            {
                if (end > _hitStopEndsAt)
                {
                    _hitStopEndsAt = end;
                    HitStopLeft = seconds;
                }
                return;      // 已经在 0.05 了，不重写 `timeScale`、不重捕获 `_scaleBefore`
            }

            _scaleBefore = Time.timeScale;      // 只为读数保留；恢复一律写 1（见 Tick）
            _hitStopEndsAt = end;
            HitStopLeft = seconds;
            Time.timeScale = HitStopScale;
        }

        /// <summary>
        /// 往累加器里加 trauma。v224 起是「**取最大 + 小的按 30% 叠**」，封顶 `TraumaMax`。
        ///
        /// 改前：`Trauma = min(1.00, Trauma + amount)` —— 两记满额爆炸直接封到 1.00（= 一记，叠加毫无意义）；
        /// 改后：`Trauma = min(1.20, max(旧, 新) + 0.30 × min(旧, 新))`
        ///   ⇒ 主力那记决定"有多重"，第二记只加一点余震 ⇒ **连炸是重击接余震，不是数字相加** ✓
        /// </summary>
        public static void Add(float amount, float decaySeconds)
        {
            if (amount <= 0f) return;
            float hi = Mathf.Max(Trauma, amount);
            float lo = Mathf.Min(Trauma, amount);
            Trauma = Mathf.Min(TraumaMax, hi + TraumaStackRatio * lo);
            if (!Application.isPlaying) return;         // 编辑模式绝不碰相机
            EnsureRig();
            if (_rig != null) _rig.SetDecay(decaySeconds);
        }

        /// <summary>复位（换局 / 退出战斗时调用）：trauma 清零、timeScale 还原、屏效清干净。</summary>
        public static void ResetAll()
        {
            Trauma = 0f;
            HitStopLeft = 0f;
            LastHitStopSeconds = 0f;      // ★ v314：停帧请求的标称时长（读数）一并归零
            BlastAtten = 1f;
            LastExplosionAtten = 1f;
            LastExplosionPeak = 0f;
            LastExplosionPeakRaw = 0f;
            LastBlastDistance = 0f;
            _attenPending = false;
            _seekOrigin = false;
            _blastPosValid = false;
            _attenHoldUntil = -1f;
            CombatFeedbackScreen.Reset();
            // ★ v263：复位一律写 1（不再写 `_scaleBefore`）—— 与 `Tick` 的恢复同一口径 ✓
            if (Application.isPlaying && Time.timeScale < 0.9f) Time.timeScale = 1f;
            _scaleBefore = 1f;
            _hitStopEndsAt = 0f;
            if (_rig != null) _rig.enabled = true;
        }

        /// <summary>桥/探针读数：抖动与世界停止的当前值（只读）。</summary>
        public static string Report()
        {
            float ex = LastExplosionPeak;                       // 已含距离衰减
            float raw = LastExplosionPeakRaw;                   // 未衰减（满额）
            float cr = LastCritPeak;
            float stacked = TraumaMax * TraumaMax * ExplosionAmp;
            return "COMBATFEEDBACK"
                 + "｜trauma=" + Trauma.ToString("0.000")
                 + "｜爆炸峰值=" + ex.ToString("0.0000") + " 世界单位（时长 " + ExplosionDecay.ToString("0.00") + "s"
                 + "｜衰减×" + LastExplosionAtten.ToString("0.00") + "，满额 " + raw.ToString("0.0000")
                 + "，爆心距玩家 " + LastBlastDistance.ToString("0.0") + "m）"
                 + "｜暴击峰值=" + cr.ToString("0.0000") + " 世界单位（时长 " + CritDecay.ToString("0.00") + "s）"
                 + "｜同时触发峰值=" + stacked.ToString("0.0000")
                 + "（单次满额 " + raw.ToString("0.0000") + " 的 "
                 + (stacked / Mathf.Max(0.0001f, raw)).ToString("0.00") + " 倍）"
                 + "｜爆炸抖动 " + ExplosionCount + " 次｜屏效白闪 " + CombatFeedbackScreen.FlashCount + " 次"
                 + "｜屏效红晕 " + CombatFeedbackScreen.HurtCount + " 次"
                 + "｜timeScale=" + Time.timeScale.ToString("0.####")
                 + "｜停止剩余=" + HitStopLeft.ToString("0.###") + "s｜触发过 " + HitStopCount + " 次"
                 + "｜isPlaying=" + Application.isPlaying
                 + "｜scene=" + SceneManager.GetActiveScene().name;
        }

        // ══════════════════════════════════════════════════════════════════
        // 内部
        // ══════════════════════════════════════════════════════════════════

        private static void EnsureRig()
        {
            if (_rig != null) return;
            var go = new GameObject("CombatFeedbackRig");
            Object.DontDestroyOnLoad(go);           // 跨场景活一整个 play 会话（不随战斗场景销毁）
            _rig = go.AddComponent<CombatFeedbackRig>();
        }

        /// <summary>由 rig 每帧回调：推进衰减与时间停止。**只用未缩放时间**。</summary>
        internal static void Tick(float unscaledDt)
        {
            // ⓪ v224：本帧的爆炸衰减系数（爆心在这之前已经由 Bus 事件递到；拿不到就是不衰减）
            if (_attenPending)
            {
                _attenPending = false;
                _seekOrigin = false;
                float atten = ResolveAttenuation();
                _blastPosValid = false;                       // 爆心用完即弃，不复用到下一记
                BlastAtten = atten;
                LastExplosionAtten = atten;
                LastExplosionPeak = LastExplosionPeakRaw * atten;
                _attenHoldUntil = Time.unscaledTime + ExplosionDecay;
                // 白闪跟着爆炸一起亮（远端爆炸的闪光也变淡 —— 与震动同一个衰减口径）
                CombatFeedbackScreen.RequestFlash(atten);
            }
            else if (Time.unscaledTime > _attenHoldUntil)
            {
                BlastAtten = 1f;                              // 爆炸窗口过了 ⇒ 暴击等其它来源按满幅
            }

            // ① trauma 衰减：按 `decaySeconds` 从 1 走到 0
            if (Trauma > 0f)
            {
                float rate = 1f / Mathf.Max(0.01f, _rig != null ? _rig.DecaySeconds : CritDecay);
                Trauma = Mathf.Max(0f, Trauma - rate * unscaledDt);
            }
            // ② 时间停止：**未缩放**计时（用缩放时间会永远回不来）
            if (HitStopLeft > 0f)
            {
                HitStopLeft = Mathf.Max(0f, HitStopLeft - unscaledDt);
                if (HitStopLeft <= 0f)
                {
                    // ★ v263：恢复**一律写 1**，不再写 `_scaleBefore` ★
                    //   全库写 `timeScale` 的地方只有这里 + `GlobalJuice.RestoreTimeScaleFallback`
                    //   ⇒ 正常值只能是 1；写回"进入前的值"的唯一效果是把任何一次异常的
                    //   进入前状态**永久固化**下来（那正是"爆炸后永久慢放"的形态）✗
                    Time.timeScale = 1f;
                    _scaleBefore = 1f;
                    _hitStopEndsAt = 0f;
                }
            }
        }

        /// <summary>
        /// ★ v263：`timeScale` 兜底安全网 ★
        ///
        /// 由两个**常驻 DDOL 宿主**每帧各调一次（`CombatFeedbackRig.LateUpdate` 与
        /// `GlobalJuiceFxHost.LateUpdate`）—— 后者存在，是为了在「rig 被销毁/被禁用」
        /// 这条单点故障里仍然有人能把 `timeScale` 拉回来（旧的恢复路径**只**依赖 rig 的
        /// `LateUpdate`，rig 一没就再也没人写回 1）✓
        ///
        /// 判据写成"**没有正在进行的停帧 + scale 明显偏低**"，而不是"scale == 0.05"：
        /// 前者对任何来源的低值都成立（例如将来有人加了别的慢动作），后者只认自己那一档 ✓
        /// 这是安全网，**不能**替代 `HitStopBoss` 里的根治（守卫 + 封顶 + 一律回 1）✓
        /// </summary>
        internal static void GuardTimeScale()
        {
            if (!Application.isPlaying) return;
            // ★ 判据用**墙钟绝对终点**，不是 `HitStopLeft > 0` ★
            //   工程里已有两处兜底（`GlobalJuice.RestoreTimeScaleFallback`、
            //   `PluginHitFx.RestoreStuckTimeScale`）都写着 `if (HitStopLeft > 0) return;`
            //   ⇒ 一旦揉抖宿主（`CombatFeedbackRig`）先没了，`HitStopLeft` 就**再没人减**
            //   而永远 > 0 ⇒ 两处兜底全都主动放弃 ⇒ `timeScale` 永久 0.05 ✗
            //   `Time.unscaledTime` 与有没有人 Tick 无关 ⇒ 拿它当"停帧该不该结束"的唯一真值 ✓
            bool shouldStillBeStopped = HitStopLeft > 0f
                                        && Time.unscaledTime <= _hitStopEndsAt + HitStopGrace;
            if (shouldStillBeStopped) return;              // 真在停帧：不动
            if (Time.timeScale < 0.9f) Time.timeScale = 1f; // 该结束却偏低 ⇒ 拉回 1（顺带清零计时）
            if (HitStopLeft > 0f) { HitStopLeft = 0f; _hitStopEndsAt = 0f; _scaleBefore = 1f; }
        }

        /// <summary>
        /// ★ v263：**无条件**收尾（宿主被禁用/销毁时用）——
        /// 计时归零 + `timeScale` 回 1，之后没有任何东西会再把它压下去 ✓
        /// </summary>
        internal static void ForceRestoreTimeScale()
        {
            HitStopLeft = 0f;
            _hitStopEndsAt = 0f;
            _scaleBefore = 1f;
            if (!Application.isPlaying) return;
            if (Time.timeScale < 0.9f) Time.timeScale = 1f;
        }

        /// <summary>
        /// 本帧应叠加给相机的位移（trauma² × 幅度 × 距离衰减，方向由未缩放时间哈希给出）。
        /// v224：多乘了一个 `BlastAtten`（爆炸窗口内 0.35~1.00，由 `Tick` 按爆心距离刷新）✓
        /// </summary>
        internal static Vector3 Offset(float amplitude)
        {
            if (Trauma <= 0f) return Vector3.zero;
            float atten = Time.unscaledTime <= _attenHoldUntil ? BlastAtten : 1f;
            float amp = Trauma * Trauma * amplitude * atten;
            // 用未缩放时间做伪随机方向（**不碰 World.Rng** ⇒ 不影响一局的确定性）
            float t = Time.unscaledTime * 37.13f;
            float hx = Mathf.PerlinNoise(t, 0.13f) * 2f - 1f;
            float hy = Mathf.PerlinNoise(0.71f, t) * 2f - 1f;
            return new Vector3(hx, hy, 0f) * amp;
        }

        internal static float RigDecayOrDefault => _rig != null ? _rig.DecaySeconds : CritDecay;
    }

    /// <summary>
    /// 相机抖动的**唯一施加点**：`LateUpdate` 里在跟焦之后叠加位移。
    /// `[DefaultExecutionOrder(30000)]` ⇒ 一定晚于 `GameBootstrap`（默认 0）的 `Update` ✓
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public sealed class CombatFeedbackRig : MonoBehaviour
    {
        /// <summary>当前衰减时长（爆炸 0.29 / 暴击 0.08；后到的那个覆盖）。</summary>
        public float DecaySeconds { get; private set; } = CombatFeedback.CritDecay;

        /// <summary>本帧叠加的位移（读数用，判断"真的动了"。）</summary>
        public Vector3 LastOffset { get; private set; }

        internal void SetDecay(float seconds) => DecaySeconds = Mathf.Max(0.01f, seconds);

        /// <summary>桥/探针入口：读数（`unity_drive send CombatFeedbackRig CombatFeedbackRig Report`）。</summary>
        public string Report()
            => CombatFeedback.Report()
             + "｜rig.LastOffset="
             + LastOffset.magnitude.ToString("0.0000")
             + "｜rig.Decay=" + DecaySeconds.ToString("0.00");

        /// <summary>桥/探针入口：手动触发一次（`explosion` / `crit` / `boss`），用于验峰值与时长。</summary>
        public string Pulse(string spec)
        {
            string s = (spec ?? "").Trim().ToLowerInvariant();
            if (s == "boss") CombatFeedback.HitStopBoss();
            else if (s == "crit") CombatFeedback.ShakeCrit();
            else if (s == "both") { CombatFeedback.ShakeExplosion(); CombatFeedback.ShakeCrit(); }
            else CombatFeedback.ShakeExplosion();
            return "pulse=" + s + "｜" + Report();
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            CombatFeedback.Tick(dt);
            // ★ v263：安全网（第一道）—— 没有正在进行的停帧却 scale 偏低 ⇒ 拉回 1 ✓
            CombatFeedback.GuardTimeScale();

            Camera cam = Camera.main;
            if (cam == null || CombatFeedback.Trauma <= 0f) { LastOffset = Vector3.zero; return; }

            // 幅度取"这一帧该用哪个"：爆炸比暴击大 ⇒ 用 trauma 已包含两者的量级
            // ★ v224：必须 `Clamp01` —— trauma 现在可以到 1.20（连炸叠加），
            //   不钳的话 `Lerp` 会**外插**：1.20 ⇒ 0.42 + 0.2×(0.42−0.04) = 0.496，
            //   再乘 trauma²(1.44) ⇒ 一趟叠出 0.71 世界单位，正好是"震到失控" ✗
            //   钳到 1.00 后：连炸峰值 = 1.44 × 0.42 = 0.6048 = 单次的 1.44 倍 ✓
            float amp = Mathf.Lerp(CombatFeedback.CritAmp, CombatFeedback.ExplosionAmp,
                                   Mathf.Clamp01(CombatFeedback.Trauma));
            Vector3 off = CombatFeedback.Offset(amp);
            LastOffset = off;
            // 只加偏移、不重算跟焦（跟焦属于 `GameBootstrap.UpdateCamera`）✓
            cam.transform.position += off;
        }

        /// <summary>
        /// ★ v263：**本宿主被禁用/销毁 ⇒ 绝不把 `timeScale` 留在低位** ★
        /// 旧实现的恢复路径**只**在 `LateUpdate → Tick` 里 ⇒ 只要 rig 先没了（被 Destroy、
        /// 被 `enabled = false`、或对象被别的清场逻辑带走），`HitStopLeft` 就再没人推进、
        /// `Time.timeScale` 就**永久停在 0.05** ⇒ 玩家/敌人/物理全近乎静止，
        /// 而吃 `unscaledDeltaTime` 的 HUD/时钟照走 —— 与用户报的症状逐条吻合 ✗
        /// </summary>
        private void OnDisable()
        {
            CombatFeedback.ForceRestoreTimeScale();
        }

        private void OnDestroy()
        {
            CombatFeedback.ForceRestoreTimeScale();
        }
    }
}
