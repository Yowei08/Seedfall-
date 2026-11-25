using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v200：**四相位"世界时间是否停摆"只读探针** ★★
    ///
    /// ── 为什么需要它 ──
    ///   用户原话："选择初始种子时没有暂停游戏，导致计时器已经开跑，解决问题并检查
    ///             菜单，抽卡，商店是否有正确暂停所有"
    ///   ⇒ 要交的证据不是"我改了哪一行"，而是**逐相位 1 秒内的 `RunTime` 变化量** ✓
    ///     0.000 = 停住了，1.000 = 还在跑（1 秒 = 60 个 `FixedTick`）✓
    ///
    /// ── 为什么测的是 `RunSession.FixedTick` 而不是"进游戏按一下" ──
    ///   ① 计时器的唯一真值是 `RunSession.RunTime`（`Clock.GameTime` 是它的派生视图），
    ///      而它只在这里推进 ⇒ 测这个方法就是测计时器 ✓
    ///   ② 真人进游戏需要**键盘输入**（选种子 / 按 Esc），自动化按不到系统键盘 ✗
    ///   ③ 本探针走的是**与游戏完全相同**的代码路径（同一个 `FixedTick`、同一个 `FrameInput`），
    ///      不是"另写一条简化逻辑"✓ 差别只在"输入是人填的还是探针填的" ✓
    ///
    /// ── 只读性 ──
    ///   不建 GameObject、不改场景、不碰存档、不改任何表 ✓
    ///   只往 `Library/` 写一份报告（Unity 的临时目录，不进版本库、不进包）✓
    ///   只有 `Library/.yowei-run-pause-probe` 存在时才跑一次，跑完删旗 ⇒ 平时零影响 ✓
    /// </summary>
    public static class ProbeV200Pause
    {
        /// <summary>旗标文件（相对工程根）。存在 ⇒ 本次进播放跑一次，然后删掉它。</summary>
        public const string FlagPath = "Library/.yowei-run-pause-probe";

        /// <summary>报告路径（相对工程根）。</summary>
        public const string ReportPath = "Library/yowei-v200-pause.txt";

        /// <summary>一种相位测几秒（秒 × 60 = 固定的 `FixedTick` 次数）。</summary>
        private const float SecondsPerProbe = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            try
            {
                string root = Directory.GetCurrentDirectory();
                string flag = Path.Combine(root, FlagPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(flag)) return;          // ← 常态：直接返回，零影响
                File.Delete(flag);

                string text = Run();
                string path = Path.Combine(root, ReportPath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(path, text, new UTF8Encoding(false));
                Debug.Log("PROBE-V200 相位停摆报告已写入 " + ReportPath + "\n" + text);
            }
            catch (Exception ex)
            {
                Debug.LogError("PROBE-V200 异常：" + ex.GetType().Name + "：" + ex.Message + "\n" + ex.StackTrace);
            }
        }

        /// <summary>空输入（与 `DebugApi` 的探针同构：所有"点选"类字段都显式写 -1）。</summary>
        private static FrameInput Idle() => new FrameInput
        {
            Move = Vector2.zero, AimWorld = Vector2.zero,
            Fire = false, Interact = false, ShopOpen = false, Skip = false,
            BuyOffer = -1, SellSlot = -1, InitialChoice = -1,
            SelectSeed = -1, RewardPick = -1, RewardSelect = -1,
            RewardSelectSet = false, RewardPickConfirmed = false,
            SlotClicked = false, ClickedSlot = -1, SlotMergeClicked = false, SlotMerge = -1,
            ToggleOfferLock = -1, OfferLockToggled = false,
            WeaponSelectSet = false, SelectWeapon = 0, WheelScroll = 0
        };

        /// <summary>跑一段固定时长，返回 `RunTime` / `Clock.GameTime` / 车坐标的位移。</summary>
        private sealed class Reading
        {
            public string Phase = "";
            /// <summary>这一行"应该停住"（true）还是"应该推进"（false）。</summary>
            public bool ExpectFrozen;
            public float RunTime0, RunTime1, Clock0, Clock1;
            public Vector3 Truck0, Truck1;
            /// <summary>测这段时商店车处在哪个相位（`Idle/Entering/Looping/Dock/Leaving`）——
            /// 车只在 `Entering/Looping/Leaving` 期间**位移**，`Idle/Dock` 是停着的 ⇒
            /// 不写这一列，"车位移 0.000"会被误读成"世界停了"（在 Idle 段它本来就该是 0）✓</summary>
            public string TruckPhase = "";
            public int Ticks;
            public float RunDelta => RunTime1 - RunTime0;
            public float ClockDelta => Clock1 - Clock0;
            public float TruckMoved => (Truck1 - Truck0).magnitude;

            /// <summary>停住 = `RunTime` 与 `Clock` 都**逐位不动**（1e-6 是 float 的比较余量）。</summary>
            public bool Frozen => Mathf.Abs(RunDelta) < 1e-6f && Mathf.Abs(ClockDelta) < 1e-6f;

            /// <summary>推进 = 走了至少 90% 的墙钟时长（少掉的那 10% 留给"这一帧恰好在切相位"）。</summary>
            public bool Advanced(float seconds) => RunDelta > seconds * 0.9f;

            public bool Pass => ExpectFrozen ? Frozen : Advanced(Ticks * GameClock.FixedDt);
        }

        private static Reading Step(RunSession s, int ticks, FrameInput input, bool expectFrozen)
        {
            var r = new Reading
            {
                Phase = s.Phase + (s.Paused ? "（Paused）" : ""),
                ExpectFrozen = expectFrozen,
                RunTime0 = s.RunTime,
                Clock0 = s.Clock.GameTime,
                Truck0 = s.TruckWorldPos,
                TruckPhase = s.Truck.Current.ToString(),
                Ticks = ticks
            };
            for (int i = 0; i < ticks; i++)
            {
                // 玩家每帧被"扶住"：本探针测的是时钟，不是生死
                // （做法照抄 `DebugApi.KeepAlive` / `RunSimulator.TopUpPlayer`：
                //   不扶住的话玩家会在 Combat 相位里被怪打死 ⇒ `IsOver` 短路 ⇒ 后续相位读数为 0
                //   ⇒ 探针自己先死，产出"全 0"的假阳性 ✗）
                PlayerActor p = s.World != null ? s.World.Player : null;
                HealthPool hp = p != null ? p.Health : null;
                if (hp != null)
                {
                    if (hp.Hp < hp.MaxHp) hp.Hp = hp.MaxHp;
                    if (hp.Shield < hp.MaxShield) hp.Shield = hp.MaxShield;
                }
                s.FixedTick(GameClock.FixedDt, input);
            }
            r.RunTime1 = s.RunTime;
            r.Clock1 = s.Clock.GameTime;
            r.Truck1 = s.TruckWorldPos;
            return r;
        }

        public static string Run()
        {
            int ticks = Mathf.RoundToInt(SecondsPerProbe / GameClock.FixedDt);
            var sb = new StringBuilder();
            sb.AppendLine("══════ v200 四相位停摆实测（每种相位连续跑 " + SecondsPerProbe.ToString("0.#")
                          + " 秒 = " + ticks + " 个 FixedTick；单位：秒）══════");
            sb.AppendLine("  判据：停住的相位 Δ 必须是 0.0000；战斗必须是 +" + SecondsPerProbe.ToString("0.0"));
            sb.AppendLine("  做法：先打 63.5 秒战斗，让商店车走到 **Looping（绕场）** 那 5 秒里 —— ");
            sb.AppendLine("        车的时刻表（`ShopTruck`）：Idle 0~60（v160：开局不刷新车）/ Entering 60~62.5 /");
            sb.AppendLine("        Looping 62.5~67.5 / Dock 67.5~77.5 / Leaving 77.5~80 ⇒ 63.5 秒正落在绕场段内 ✓");
            sb.AppendLine("        车只在 Entering/Looping/Leaving 期间位移，Idle/Dock 是停着的");
            sb.AppendLine("        ⇒ 不选这段的话「车位移 0.000」根本不能当旁证（它在 Idle 里本来就该是 0）。");
            sb.AppendLine("        之后每种相位各测 1 秒。");
            sb.AppendLine();

            var rows = new System.Collections.Generic.List<Reading>();
            var idle = Idle();

            // ── ① Prepare（开局三选一）══ 本轮修的 bug ══
            var s = new RunSession();
            s.Start(20241018UL);
            rows.Add(Step(s, ticks, idle, true));           // 不给 InitialChoice ⇒ 停在三选一

            // ── ② Combat 暖场 63.5 秒（不进表）：把车开进 Looping 窗口 ──
            var pick = Idle();
            pick.InitialChoice = 0;                          // 点第一张卡 ⇒ StartWave(1)
            s.FixedTick(GameClock.FixedDt, pick);
            int warm = Mathf.RoundToInt(63.5f / GameClock.FixedDt);
            for (int i = 0; i < warm; i++)
            {
                HealthPool hp0 = s.World.Player.Health;
                if (hp0.Hp < hp0.MaxHp) hp0.Hp = hp0.MaxHp;
                if (hp0.Shield < hp0.MaxShield) hp0.Shield = hp0.MaxShield;
                s.FixedTick(GameClock.FixedDt, idle);
            }

            // ── ③ Combat（对照：必须推进；同时看车动没动）──
            rows.Add(Step(s, ticks, idle, false));

            // ── ④ Menu（Esc 暂停菜单；阶段仍是 Combat）──
            s.TogglePause();
            rows.Add(Step(s, ticks, idle, true));

            // ── ⑤ 关掉菜单之后（必须恢复推进 —— 否则"暂停"修成了"卡死"）──
            s.TogglePause();
            rows.Add(Step(s, ticks, idle, false));

            // ── ⑥ LevelReward（抽卡）──
            s.DebugShowReward();
            rows.Add(Step(s, ticks, idle, true));

            // ── ⑦ Shop（商店）──
            s.ForceShopForDebug();
            rows.Add(Step(s, ticks, idle, true));

            int bad = 0;
            foreach (Reading r in rows) if (!r.Pass) bad++;

            sb.AppendLine("  ┌──────────────────┬──────────┬──────────────┬──────────────┬──────────────────┬──────────────┬────────┐");
            sb.AppendLine("  │ 相位             │ 车相位   │ RunTime 初值 │ RunTime Δ    │ Clock.GameTime Δ │ 车位移(m)    │ 结论   │");
            sb.AppendLine("  ├──────────────────┼──────────┼──────────────┼──────────────┼──────────────────┼──────────────┼────────┤");
            foreach (Reading r in rows)
            {
                string verdict = r.Pass
                    ? (r.ExpectFrozen ? "停摆 ✔" : "推进 ✔")
                    : (r.ExpectFrozen ? "在走 ✘" : "没推进 ✘");
                sb.Append("  │ ").Append(Pad(r.Phase, 16))
                  .Append(" │ ").Append(Pad(r.TruckPhase, 8))
                  .Append(" │ ").Append(Pad(r.RunTime0.ToString("0.0000"), 12))
                  .Append(" │ ").Append(Pad(r.RunDelta.ToString("+0.0000;-0.0000"), 12))
                  .Append(" │ ").Append(Pad(r.ClockDelta.ToString("+0.0000;-0.0000"), 16))
                  .Append(" │ ").Append(Pad(r.TruckMoved.ToString("0.000"), 12))
                  .Append(" │ ").Append(Pad(verdict, 6)).AppendLine(" │");
            }
            sb.AppendLine("  └──────────────────┴──────────┴──────────────┴──────────────┴──────────────────┴──────────────┴────────┘");
            sb.AppendLine();
            sb.AppendLine("  说明：`RunTime` 是本局世界时间的**唯一真值**（顶部计时器读它派生出来的 `Clock`）；");
            sb.AppendLine("        `Clock.GameTime` 是 `Clock.SyncTo(RunTime)` 的派生视图 ⇒ 两者必须同步停/同步走 ✓");
            sb.AppendLine("        车坐标 = `ShopTruck.SamplePoint(Clock)` ⇒ 停摆行里车位移 0.000 是「世界真的冻住了」的旁证 ✓");
            sb.AppendLine();
            sb.AppendLine("  结论：" + (bad == 0
                ? "四个必须停的相位全部停摆（Δ = 0.0000），战斗与「关掉菜单之后」都恢复推进 ✔"
                : bad + " 个相位不符合预期 ✘"));
            s.Dispose();
            return sb.ToString();
        }

        /// <summary>按**显示宽度**右侧补空格（中文按 2 计），避免表格错行。</summary>
        private static string Pad(string text, int width)
        {
            if (text == null) text = "";
            int w = 0;
            for (int i = 0; i < text.Length; i++) w += text[i] > 0x2E80 ? 2 : 1;
            return w >= width ? text : text + new string(' ', width - w);
        }
    }
}
