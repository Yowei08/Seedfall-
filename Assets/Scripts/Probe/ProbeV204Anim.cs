using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v204 只读探针：**玩家攻击动画** —— 随攻速加快 / 每次攻击重置 / 永不卡死 ★★
    ///
    /// 用户原话："玩家攻击动画优化，随攻速加快，且每次攻击可以重置动画，保证永远能播放"
    ///
    /// ── 为什么这个探针必须存在（自检测不出来）──
    ///  自检跑的是**玩法层**（金币/伤害/冷却），而这三条全是**表现层**的：
    ///  动画播多长、有没有被重开、连续 20 刀里有没有被吞 —— 玩法断言一条都测不到 ✗
    ///  而"被吞"这件事**不会报错、不会崩**，只表现成"这一刀没有动作"⇒ 只能靠读数 ✓
    ///
    /// ── 走的是**生产路径**（这一点是整个探针的价值所在）──
    ///  不是在探针里抄一份规则，而是真的：
    ///    `new RunSession()` → `Start` → `StartWave` → `SetPhasePublic(Combat)`
    ///    → `WorldView.Build(s)`（**与 `GameBootstrap` 同一个建法**）
    ///    → 每帧 `s.FixedTick(dt, input{Fire=true})` + `view.Sync(s, dt)`
    ///  ⇒ 出手的事件投递、`_pendingAttack` 的置位、消费处的重开判据、`ModelView` 的推进，
    ///    全部走**真实那一套代码** ✓
    ///
    /// ── 三档攻速 × 连打 20 下，每一下量三样 ──
    ///   ① `t_prev`：这次重开时，**上一次动画已经播到多远**（归一化 u）
    ///      ★ 这一条同时是"改动生效"的判据 ★：旧规则只在 `Current != Attack` 时才重开
    ///        ⇒ 能重开就说明上一次已经回 Idle（`t_prev` 恒为 0）✗
    ///        新规则下 `t_prev` ≈ 0.98（重开时上一次还在攻击态里）✓ 两者**不可能混** ✓
    ///   ② `armR` 的行程（最小 / 最大角）：锄头是"抬到 -135° → 砸到 +58°"
    ///      ⇒ 每一下都应该是同一条行程 ⇒ "每一下都有完整的前摇→砸下" ✓
    ///   ③ 时长：`view.LastAttackAnimSeconds` = `tool.cooldown / 攻速`，且有 **0.06s 下限**
    ///
    /// ★ 只读：会话与世界都在方法里 `new` 出来，跑完 `Dispose` / `Destroy` ✓
    /// ★ 确定性：固定步长 1/60、固定种子、**每帧清空敌人**（被打一下会插播受击动作，读数就脏了）✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV204Anim : MonoBehaviour
    {
        private const float Dt = 1f / 60f;
        private const int Hits = 20;

        /// <summary>锄头三档攻速（用户点名的 1.0 / 2.0 / 5.0），外加两档"极高攻速"验下限。</summary>
        private static readonly float[] Speeds = { 1.0f, 2.0f, 5.0f, 20.0f };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV204Anim>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV204Anim");
            go.AddComponent<ProbeV204Anim>();
        }

        /// <summary>桥：`/input` → action=send, path=ProbeV204Anim, method=Run。</summary>
        public string Run() => RunReport();

        /// <summary>一次出手的读数。</summary>
        private struct Attack
        {
            public float PeakU;              // 这一段里 t/len 的最大值（"播到多远"）
            public float MinHandY, MaxHandY; // 右手在**模型本地**的高度范围（抬到头顶 / 砸到最低）
            public float MinHandZ, MaxHandZ; // 右手在模型本地的前后范围（后拉 / 前送）
            public int Frames;               // 这一段持续了几帧
        }

        /// <summary>
        /// 一次"完整前摇→砸下"要求右手的**高度行程**至少这么多米。
        ///
        /// ★ 这个数不是拍的 ★ 锄头的设计动作是 `armR: 10° → -135° → +58°`，
        ///   上臂长 0.20m、前臂+手再 0.16m（`PlayerBean` 的尺寸表），
        ///   -135° 时手在**肩上方偏后**、+58° 时在**身前下方** ⇒ 设计行程约 0.55~0.70m。
        ///   取 **0.30m** 作判据：只要真的"抬起来又砸下去"，绝不会低于这个数；
        ///   而"只闪一帧 / 被吞掉 / 停在中间"的行程会远小于它 ✓
        /// </summary>
        private const float FullSwingMeters = 0.30f;

        public static string RunReport()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(bool ok, string name, string detail)
            {
                if (ok) pass++; else fail++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
            }

            sb.AppendLine("═══ v204 探针（只读）：玩家攻击动画 = 随攻速 + 每次重置 + 永不卡 ★══");
            if (TableDatabase.Tuning == null) TableDatabase.Load(true);
            if (TableDatabase.Tuning == null) { sb.AppendLine("（没有表：先 TableDatabase.Load）"); return sb.ToString(); }

            float hoeCd = TableDatabase.Tuning.tools.hoe.cooldown;
            sb.AppendLine($"夹具：武器 = 锄头（0）｜`tools.hoe.cooldown` = {hoeCd:0.###}s"
                        + $"｜动画时长应 = cooldown / 攻速，下限 {WorldView.MinAttackAnimSeconds:0.###}s"
                        + $"｜固定步长 1/60｜每档连打 {Hits} 下");

            for (int i = 0; i < Speeds.Length; i++)
                AppendSpeed(sb, Check, Speeds[i], hoeCd);

            AppendFloor(sb, Check);

            sb.AppendLine();
            sb.AppendLine($"════ 合计 {pass} PASS / {fail} FAIL ════");
            string text = sb.ToString();
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        /// <summary>
        /// ★★ 下限：请求极短的动画时，**实际播多久** ★★
        ///
        /// 为什么单独一段：`caps.atkSpeed` 封顶在 3.0（实测，见上面每档的"属性实测"列），
        /// 而锄头 `cooldown` 0.7s ⇒ 实战最快也只到 `0.7 / 3 = 0.233s`，
        /// **永远碰不到下限** ✗ ⇒ 光靠"跑三档攻速"根本证明不了下限存在 ✓
        /// 所以这里直接把极短时长喂给**生产接口**（`ModelView.SetAttackDuration`），
        /// 量两件事：
        ///   ① `PlayerBean.AttackSeconds` 存下来的值（下限 0.06s、`<=0` 走 0.70s 兜底）
        ///   ② 状态**实际持续几帧**（`ModelView.BeanStateSeconds` 另有 `Clamp(…, 0.12f, 3f)`）
        /// ⇒ 两者一起说明"再快也不会一帧播完" ✓
        /// </summary>
        private static void AppendFloor(StringBuilder sb, System.Action<bool, string, string> check)
        {
            sb.AppendLine();
            sb.AppendLine("── 下限：请求极短动画时，实际播多久 ──");
            var holder = new GameObject("PROBE_FLOOR");
            ModelView mv = ModelView.SpawnPlayer(holder.transform);
            if (mv == null || mv.Bean == null)
            {
                check(false, "F1 下限可测", "SpawnPlayer 失败 / 没有豆豆人");
                Object.Destroy(holder);
                return;
            }
            try
            {
                mv.SetScaleFactor(2.60f * 0.627f);      // 与 `WorldView.BuildPlayer` 同一个缩放
                mv.SetAttackWeapon(0);

                float[] reqs = { 0.001f, 0.02f, 0.06f, 0.14f, 0.70f };
                int minFrames = int.MaxValue;
                float minBean = float.MaxValue;
                var rows = new StringBuilder();
                for (int i = 0; i < reqs.Length; i++)
                {
                    mv.SetAttackDuration(reqs[i]);
                    float bean = mv.Bean.AttackSeconds;
                    mv.PlayForced(ModelView.State.Attack);
                    int frames = 0;
                    while (mv.Current == ModelView.State.Attack && frames < 900)
                    {
                        mv.Tick(Dt);
                        frames++;
                    }
                    if (frames < minFrames) minFrames = frames;
                    if (bean < minBean) minBean = bean;
                    rows.AppendLine($"     请求 {reqs[i]:0.000}s ⇒ `PlayerBean` 存 {bean:0.000}s"
                                  + $"｜状态持续 {frames} 帧 = {frames * Dt:0.000}s");
                }
                sb.Append(rows);

                // `<= 0`（数据缺 / 表没加载）⇒ 兜底 0.70s，而不是被夹成"极快"
                mv.SetAttackDuration(0f);
                float zeroBean = mv.Bean.AttackSeconds;

                check(minBean >= WorldView.MinAttackAnimSeconds - 1e-4f,
                    $"F1 `PlayerBean` 存的时长**永远 ≥ {WorldView.MinAttackAnimSeconds:0.###}s**"
                    + "（0.001s 的请求也不会被存成 0.001）",
                    $"最小时长 {minBean:0.000}s");

                check(minFrames >= 7,
                    "F2 **状态至少持续 7 帧**（60fps 下 ≥0.117s）⇒ 再快也不会一帧播完、看不出在打",
                    $"最短 {minFrames} 帧 = {minFrames * Dt:0.000}s"
                    + "（`ModelView.BeanStateSeconds` 另有 `Clamp(…, 0.12f, 3f)` ⇒ 有效下限 0.12s）");

                check(Mathf.Abs(zeroBean - 0.70f) < 1e-4f,
                    "F3 `seconds = 0`（数据缺 / 表没加载）⇒ 走 **0.70s 兜底**，不伪装成「极快」",
                    $"实测 {zeroBean:0.000}s");
            }
            catch (System.Exception e)
            {
                sb.AppendLine($"  ✗ 下限这一段抛异常：{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                Object.Destroy(holder);
            }
        }

        private static void AppendSpeed(StringBuilder sb, System.Action<bool, string, string> check,
            float atkSpeed, float hoeCd)
        {
            var s = new RunSession();
            WorldView view = null;
            try
            {
                s.Start(204001UL);
                s.StartWave(1);
                // ★ 出手只在 `TickCombat` 里发生（`TickPrepare` 不调 `HandlePlayer`）
                //   ⇒ 必须显式进 Combat，否则本探针量的是"什么都没发生"✓
                s.SetPhasePublic(PhaseState.Combat);
                PlayerActor p = s.World.Player;
                p.Progress.Stats.SetBase(StatId.AtkSpeed, atkSpeed);
                float atkShown = p.AttackSpeed;    // ★ 实测：属性有 `caps.atkSpeed` 封顶，请求值可能拿不到
                p.Pos = Vector2.zero;
                p.AimDir = Vector2.up;

                // ★ v204：`WorldView` **不是 MonoBehaviour**（`public sealed partial class WorldView`）——
                //   它由 `GameBootstrap` 直接 `new` 出来（`GameBootstrap.cs:57`），
                //   所有物件都挂在它自己建的 `_root`（"SeedfallWorld"）下。
                //   ⇒ 探针也 `new` 一份，**与生产同一条建法** ✓
                view = new WorldView();
                view.Build(s);

                // ── 待机基线：右手在模型本地的高度（对照"抬到头顶有多高"）──
                //   ★ 不按 `armR` 欧拉角判行程 ★：ZXY 分解在 x≈±90° 有奇异点，
                //   实测锄头挥到 -101° 时 `localEulerAngles.x` 读回来是 -78° ⇒ 读者看到"手自己弹回去"，
                //   而那是读法的假象（见 `WorldView.PlayerHandMount` 的注释）✓
                float idleHandY;
                {
                    var idle = new FrameInput
                    {
                        SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1,
                        AimWorld = new Vector2(0f, 6f),
                    };
                    for (int i = 0; i < 10; i++)
                    {
                        s.World.Enemies.Clear();
                        s.FixedTick(Dt, idle);
                        view.Sync(s, Dt);
                    }
                    if (!HandY(view, out idleHandY, out _)) idleHandY = 0f;
                }

                // 出手计数（玩法侧的真实事件）—— 用来和"动画重开次数"对拍
                int swings = 0;
                System.Action<MeleeSwingEvent> onSwing = _ => swings++;
                Bus.Subscribe(onSwing);
                try
                {
                    var attacks = new List<Attack>();
                    var input = new FrameInput
                    {
                        SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1,
                        AimWorld = new Vector2(0f, 6f), Fire = true,
                    };

                    int lastStarts = view.AttackAnimStarts;
                    Attack cur = InitAttack();
                    bool open = false;
                    float len = 0f;
                    int startFrames = 0;
                    var curve = new StringBuilder();     // 第 1 刀逐帧的 (u, 手高)，用来复核行程
                    var spark = new StringBuilder();     // 同一组数据的柱状图（"图"的那一半）

                    // 上限帧数：20 刀 × 期望间隔 × 2.5（防止任何"卡死"把探针挂住）
                    float expect = Mathf.Max(WorldView.MinAttackAnimSeconds, hoeCd / Mathf.Max(0.05f, atkShown));
                    int maxFrames = Mathf.CeilToInt(expect * (Hits + 2) * 2.5f / Dt);

                    for (int f = 0; f < maxFrames; f++)
                    {
                        // 清场：被打一下会 `PlayForced(Hit)` 插播受击动作 ⇒ 会把"这一刀"的行程截断
                        s.World.Enemies.Clear();
                        s.FixedTick(Dt, input);
                        view.Sync(s, Dt);

                        int now = view.AttackAnimStarts;
                        if (now != lastStarts)
                        {
                            if (open)
                            {
                                cur.Frames = f - startFrames;
                                attacks.Add(cur);
                            }
                            lastStarts = now;
                            len = view.LastAttackAnimSeconds;
                            cur = InitAttack();
                            startFrames = f;
                            open = true;
                        }
                        if (open && view.PlayerAnimState == ModelView.State.Attack)
                        {
                            float u = len > 0.001f ? view.PlayerAnimTime / len : 0f;
                            if (u > cur.PeakU) cur.PeakU = u;
                            if (HandY(view, out float hy, out float hz))
                            {
                                if (hy < cur.MinHandY) cur.MinHandY = hy;
                                if (hy > cur.MaxHandY) cur.MaxHandY = hy;
                                if (hz < cur.MinHandZ) cur.MinHandZ = hz;
                                if (hz > cur.MaxHandZ) cur.MaxHandZ = hz;
                                if (attacks.Count == 0 && curve.Length < 900)
                                {
                                    curve.Append($"{u:0.00}→{hy:0.00} ");
                                    // 柱状图：手高在 0.35~0.80m 之间取 8 档（纯可视化，不参与判据）
                                    int lv = Mathf.Clamp(Mathf.RoundToInt((hy - 0.35f) / 0.45f * 7f), 0, 7);
                                    spark.Append("▁▂▃▄▅▆▇█"[lv]);
                                }
                            }
                        }
                        if (attacks.Count >= Hits) break;
                    }
                    if (open && attacks.Count < Hits) attacks.Add(cur);

                    float anim = view.LastAttackAnimSeconds;
                    float expectLen = Mathf.Max(WorldView.MinAttackAnimSeconds, hoeCd / Mathf.Max(0.05f, atkShown));
                    // 探针自己的读数：长度、次数、每一下的归一化峰值与右手的行程
                    sb.AppendLine();
                    sb.AppendLine($"── 请求攻速 {atkSpeed:0.##} ── 属性实测 {atkShown:0.###}"
                                + $"（`caps.atkSpeed` 会封顶 ⇒ 请求值可能拿不到）"
                                + $"｜动画时长 {anim:0.000}s"
                                + $"（= cooldown {hoeCd:0.###} ÷ 攻速 = {hoeCd / Mathf.Max(0.05f, atkShown):0.000}s"
                                + $"，下限 {WorldView.MinAttackAnimSeconds:0.###}）"
                                + $"｜出手 {swings} 次 / 完整动画段 {attacks.Count} 段"
                                + $"｜待机手高 {idleHandY:0.000}m（模型的肩高量级）");
                    if (curve.Length > 0)
                    {
                        sb.AppendLine("   第 1 刀逐帧 (u→右手模型本地高度 m)：" + curve.ToString().TrimEnd());
                        sb.AppendLine("   同一组数据的柱状图（▁=0.35m █=0.80m，一格 = 一帧 1/60s）：" + spark);
                    }

                    int full = 0;
                    var peaks = new StringBuilder();
                    for (int i = 0; i < attacks.Count; i++)
                    {
                        Attack a2 = attacks[i];
                        // "完整的前摇→砸下" = 时间走到 ≥85%，**且右手真的抬起又落下**（行程 ≥0.30m）
                        bool ok = a2.PeakU >= 0.85f && (a2.MaxHandY - a2.MinHandY) >= FullSwingMeters;
                        if (ok) full++;
                        if (i < Hits)
                        {
                            if (i > 0) peaks.Append(' ');
                            peaks.Append($"{i + 1}:u={a2.PeakU:0.00}/手高[{a2.MinHandY:0.00},{a2.MaxHandY:0.00}]\u2191"
                                         + $"{a2.MaxHandY - a2.MinHandY:0.00}m/{(ok ? "✓" : "✗")}");
                        }
                    }
                    sb.AppendLine("   每一下（u = 这一段播到的最大 t/len；手高 = 右手在模型本地的高度区间；↑ = 行程）：");
                    sb.AppendLine("   " + peaks);

                    check(attacks.Count == Hits && swings >= Hits,
                        $"① 攻速 {atkSpeed:0.##}：连打 {Hits} 下 ⇒ **{Hits} 段完整动画、一次都不吞**",
                        $"出手 {swings} 次｜完整动画段 {attacks.Count} 段"
                        + $"（第 {swings} 次出手是拿来收尾第 {Hits} 段的）");

                    check(full == Hits && attacks.Count == Hits,
                        $"② 攻速 {atkSpeed:0.##}：**每一下**都有完整的前摇→砸下（u≥0.85 且右手行程 ≥{FullSwingMeters:0.00}m）",
                        $"达标 {full}/{attacks.Count}"
                        + (attacks.Count > 0
                           ? $"｜u 峰值最小 {MinPeak(attacks):0.00}｜右手行程最小 {MinSwing(attacks):0.00}m"
                             + $"（全体 {MinHandY(attacks):0.00}~{MaxHandY(attacks):0.00}m）"
                           : ""));

                    check(Mathf.Abs(anim - expectLen) < 0.005f,
                        $"③ 攻速 {atkSpeed:0.##}：动画时长 = cooldown ÷ 攻速（有下限）",
                        $"实测 {anim:0.000}s｜应 {expectLen:0.000}s"
                        + (expectLen <= WorldView.MinAttackAnimSeconds + 1e-4f ? "（**这一段撞到下限**）" : ""));

                    // ★ 重开时"上一次已经播到多远" —— 旧规则下这一列只可能是 0
                    float minPeakU = MinPeak(attacks);
                    if (attacks.Count > 1)
                    {
                        check(minPeakU >= 0.80f,
                            $"④ 攻速 {atkSpeed:0.##}：**重开时上一次都已播到 u≥0.80**"
                            + "（旧规则只在「上一段播完回 Idle」才重开 ⇒ 这一列会恒为 0）",
                            $"最小 u 峰值 {minPeakU:0.00}（= 每一刀都是「上一次还在攻击态里就被下一次重开」）✓");
                    }
                }
                finally
                {
                    Bus.Unsubscribe(onSwing);
                }
            }
            catch (System.Exception e)
            {
                sb.AppendLine($"  ✗ 攻速 {atkSpeed:0.##} 这一段抛异常：{e.GetType().Name}: {e.Message}");
                sb.AppendLine("    " + (e.StackTrace ?? "").Replace("\n", "\n    "));
            }
            finally
            {
                if (view != null) view.Clear();
                s.Dispose();
            }
        }

        private static float MinPeak(List<Attack> a)
        {
            float m = float.MaxValue;
            for (int i = 0; i < a.Count; i++) if (a[i].PeakU < m) m = a[i].PeakU;
            return a.Count == 0 ? 0f : m;
        }

        /// <summary>每一刀里**最小**的右手行程（"最差的那一下抬起了多少米"）。</summary>
        private static float MinSwing(List<Attack> a)
        {
            float m = float.MaxValue;
            for (int i = 0; i < a.Count; i++)
            {
                float s2 = a[i].MaxHandY - a[i].MinHandY;
                if (s2 < m) m = s2;
            }
            return a.Count == 0 ? 0f : m;
        }

        private static float MinHandY(List<Attack> a)
        {
            float m = float.MaxValue;
            for (int i = 0; i < a.Count; i++) if (a[i].MinHandY < m) m = a[i].MinHandY;
            return a.Count == 0 ? 0f : m;
        }

        private static float MaxHandY(List<Attack> a)
        {
            float m = float.MinValue;
            for (int i = 0; i < a.Count; i++) if (a[i].MaxHandY > m) m = a[i].MaxHandY;
            return a.Count == 0 ? 0f : m;
        }

        private static Attack InitAttack()
            => new Attack { MinHandY = 999f, MaxHandY = -999f, MinHandZ = 999f, MaxHandZ = -999f };

        /// <summary>
        /// 右手在**模型本地**坐标里的高度与前伸（米）。
        ///
        /// 为什么不在探针里写死"肩在 0.48m"这类常量：模型高度是 `PlayerBean` 的尺寸表说了算，
        /// 抄一份到探针就是第二份真相 ⇒ 这里只做**变换**（世界 → 模型本地），数一个都不猜 ✓
        /// 返回 false 表示拿不到手/根节点（例如模型走了占位图元分支）。
        /// </summary>
        private static bool HandY(WorldView view, out float y, out float z)
        {
            y = 0f; z = 0f;
            Transform hand = view.PlayerHandMount;
            GameObject root = view.PlayerModelGo;
            if (hand == null || root == null) return false;
            Vector3 local = root.transform.InverseTransformPoint(hand.position);
            y = local.y;
            z = local.z;
            return true;
        }
    }
}
