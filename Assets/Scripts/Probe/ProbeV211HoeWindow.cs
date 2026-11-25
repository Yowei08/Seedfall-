using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v211：锄头「挥击判定窗口」的**只读**探针 ★★
    ///
    /// ── 用户原话 ──
    ///   "锄头的攻击总是出现特效碰到但没伤害的情况，延长攻击判定存在时间，
    ///     但确保不要重复触发；快速修改"
    ///
    /// ── 它量什么（全部是**跑出来的**读数，不是复述自检的结论）──
    ///   ① 参数摘要（时长 / 扫描间隔 / 去重结构 / 时间源 / 命中名额）
    ///   ② 判据一：站桩怪在窗口内被反复扫过 ⇒ 血量**只掉一次**（附逐帧时间线）
    ///   ③ 判据二：怪在窗口**中途**走进扇形 ⇒ **也掉血**（这一条就是修复本身的读数）
    ///   ④ 判据三：窗口**结束之后**才走进来 ⇒ 不掉血，要等**下一次挥击**
    ///   另：`RunSelfCheck` 会把 `SelfCheck.RunAll()` 的**全部原文**（含每条的读数）落盘，
    ///   因为 `SelfCheck.Report` 只印红的 —— 绿的读数只能从这份文件里看 ✓
    ///
    /// ── 只读性 + 「平时什么都不做」 ──
    ///   · ★ 没有 `[RuntimeInitializeOnLoadMethod]` ★：进 play **不会**自动建实例，
    ///     对实机零影响（上一轮 `ProbeV206Outline` 的教训：自动创建会在构造阶段跑字段初始化器
    ///     里的 `CreateImpl`，引擎直接抛 ⇒ 这一条必须守住）✓
    ///   · 只调 `RunSession` / `SelfCheck` 的**公开**入口；不写 `Assets/**`、不改表、不改场景
    ///   · 报告只落在 `<工程>/Library/`（Unity 的临时目录）；**自己用完了自己销毁**
    ///     （`Cleanup`），不在场景里留垃圾 ✓
    ///
    /// ── 怎么跑（两条路，都不需要动实机流程）──
    ///   ① 无头批处理：`-executeMethod Seedfall.ProbeV211HoeWindow.RunBatch`
    ///      （先跑自检、再跑本探针；退出码 = 自检红了几条，-1 = 自检跑不起来）
    ///   ② 编辑器里的桥：先 `/scene` create 一个挂着本组件的空物体（名字 `ProbeV211`），然后
    ///      `/input` action=send path=ProbeV211 method=Run          → ①~④ 落盘
    ///      `/input` action=send path=ProbeV211 method=RunSelfCheck → 自检原文落盘
    ///      `/input` action=send path=ProbeV211 method=Cleanup      → 把这个物体删掉
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV211HoeWindow : MonoBehaviour
    {
        public const string ReportPath = "Library/yowei-v211-hoewindow.txt";
        public const string SelfCheckPath = "Library/yowei-v211-selfcheck.txt";

        /// <summary>摆点用的敌人（`enemies.json` 的 `grunt`）。</summary>
        private const string ProbeEnemyId = "grunt";

        /// <summary>夹具落点：正前方 2m（在扇形里）。与自检同一个值。</summary>
        private static readonly Vector2 InCone = new Vector2(2.0f, 0f);

        /// <summary>夹具落点：正前方 2m 偏 0.3m（约 8.5°，仍在判定 ±50° 之内）。</summary>
        private static readonly Vector2 InConeShifted = new Vector2(2.0f, 0.3f);

        /// <summary>夹具落点：正后方（180°，扇形外）。</summary>
        private static readonly Vector2 OutOfCone = new Vector2(-2.0f, 0f);

        // ───────────────────────── 桥/批处理入口 ─────────────────────────

        /// <summary>桥入口：跑①②③④并落盘，返回一句话结论（`/input` 的返回值里看得见）。</summary>
        public string Run()
        {
            string outcome = Execute();
            Debug.Log("[PROBEV211] " + outcome);
            return outcome;
        }

        /// <summary>桥入口：把 `SelfCheck.RunAll()` 的**全部原文**落盘（绿的也印），返回总数。</summary>
        public string RunSelfCheck()
        {
            string outcome = ExecuteSelfCheck();
            Debug.Log("[PROBEV211] " + outcome);
            return outcome;
        }

        /// <summary>桥入口：用完把自己删掉（不在场景里留垃圾）。</summary>
        public string Cleanup()
        {
            string name = gameObject.name;
            DestroyImmediate(gameObject);
            return "removed " + name + "（探针用完自己删掉，场景不留垃圾）";
        }

        /// <summary>
        /// 无头批处理入口：**先跑自检、再跑探针**，退出码 = 自检红了几条（-1 = 自检跑不起来）。
        /// 顺序的理由：自检是闸门、探针是证据；闸门不过时证据也照样落盘，好一起读 ✓
        /// </summary>
        public static void RunBatch()
        {
            int fail = SelfCheckRunner.Run();
            Execute();
            ExecuteSelfCheck();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(fail);
#endif
        }

        // ───────────────────────── ① 参数摘要 ─────────────────────────

        private static string Execute()
        {
            var sb = new StringBuilder();
            string outcome;
            try
            {
                TableDatabase.Load(true);
                DumpParams(sb);
                DumpStationary(sb);
                DumpMidWindow(sb);
                DumpAfterWindow(sb);
                outcome = "ok：" + Write(ReportPath, sb);
            }
            catch (Exception ex)
            {
                sb.AppendLine("PROBE-V211 异常：" + ex.GetType().Name + "：" + ex.Message);
                sb.AppendLine(ex.StackTrace);
                outcome = "EXCEPTION：" + ex.GetType().Name + "：" + ex.Message;
                Debug.LogError("[PROBEV211] " + outcome);
                Write(ReportPath, sb);
            }
            return outcome;
        }

        private static void DumpParams(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            ToolDef hoe = t.tools.hoe;
            sb.AppendLine("═══ ① 窗口参数（数是编译器里的，不在这里另抄一份）═══");
            sb.AppendLine($"窗口时长      = {F(RunSession.MeleeSwingWindowSeconds)}s"
                          + "（用户给的区间 0.18~0.25，取中点）");
            sb.AppendLine($"重扫间隔      = {F(RunSession.MeleeSwingScanInterval)}s"
                          + "（= 1/30 ⇒ 60fps 下约每 2 帧一次；窗口内约 "
                          + $"{F(RunSession.MeleeSwingWindowSeconds / RunSession.MeleeSwingScanInterval)} 次重扫）");
            sb.AppendLine($"挥击冷却      = {F(hoe.cooldown)}s（攻速 1.0）⇒ 窗口"
                          + (RunSession.MeleeSwingWindowSeconds <= hoe.cooldown ? " ≤ " : " > ")
                          + $"冷却（余量 {F(hoe.cooldown - RunSession.MeleeSwingWindowSeconds)}s）");
            sb.AppendLine("去重结构      = HashSet<int>，键 = EnemyActor.Id（本窗口已命中集合，收窗即清空）");
            sb.AppendLine("时间源        = 会话固定步长 dt（= World.Simulate 里 `Time += dt` 用的同一个 dt）"
                          + $"｜GameClock.FixedDt = {F(GameClock.FixedDt)}s｜与 Time.time 无关");
            sb.AppendLine("重扫判据      = 与挥击那一帧**同一个函数** MeleeHitScan"
                          + $"（半径 ≤ range {F(hoe.range)} + e.Radius，夹角 ≤ ±{F(RunSession.MeleeJudgeHalfArcDeg(hoe.arc))}°）");
            sb.AppendLine($"命中名额      = hoe.hits = {hoe.hits}（窗口共享，不是「每次重扫各 3 个」）");
            sb.AppendLine();
        }

        // ───────────────────────── ② 判据一：站桩怪 ─────────────────────────

        private static void DumpStationary(StringBuilder sb)
        {
            sb.AppendLine("═══ ② 判据一：站桩怪在窗口内被反复扫过 ⇒ 掉 1 次血 ═══");
            RunSession s = Bench(20260711UL, out EnemyActor e, InCone);
            try
            {
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                float first = hp0 - e.Health.Hp;
                sb.AppendLine($"挥击那一帧：掉 {F(first)}｜窗口开 {s.MeleeWindowActive}"
                              + $"｜剩 {F(s.MeleeWindowSecondsLeft)}s｜到期 World.Time {F(s.MeleeWindowEndWorldTime)}");
                sb.AppendLine("帧 | 窗口 | 剩(s)   | 已命中 | 怪总掉血 | 说明");
                sb.AppendLine("---|------|---------|--------|----------|------");
                for (int f = 1; f <= 20; f++)
                {
                    s.DebugAdvanceAttack(GameClock.FixedDt);
                    float lost = hp0 - e.Health.Hp;
                    sb.AppendLine($"{f,2} | {(s.MeleeWindowActive ? "开" : "关"),-4} | {F(s.MeleeWindowSecondsLeft),7} | "
                                  + $"{s.MeleeWindowHitCount,6} | {F(lost),8} | "
                                  + (s.MeleeWindowActive ? "窗口内重扫中（血不再掉 = 去重生效）"
                                                         : "窗口已关，判定结束"));
                }
                float total = hp0 - e.Health.Hp;
                int times = first > 1e-4f ? Mathf.RoundToInt(total / first) : 0;
                sb.AppendLine($"⇒ 站桩怪总掉血 {F(total)}（挥击那一帧 {F(first)}，之后 20 帧窗口开着却没有第二下）");
                sb.AppendLine($"⇒ 判据一 {(Mathf.Abs(total - first) < 1e-3f && first > 0f ? "PASS" : "FAIL")}"
                              + $"：掉血次数 = {times}（期望 1）");
            }
            finally { s.Dispose(); }
            sb.AppendLine();
        }

        // ───────────────────────── ③ 判据二：窗口中途走进来 ─────────────────────────

        private static void DumpMidWindow(StringBuilder sb)
        {
            sb.AppendLine("═══ ③ 判据二：怪在窗口**中途**走进扇形 ⇒ 也会掉血 ═══");
            RunSession s = Bench(20260712UL, out EnemyActor e, OutOfCone);
            try
            {
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                float atSwing = hp0 - e.Health.Hp;
                sb.AppendLine($"挥击时它在背后（{F(OutOfCone.x)}, {F(OutOfCone.y)}）⇒ 掉 {F(atSwing)}（扇形外，应为 0）");
                sb.AppendLine("帧 | 窗口 | 剩(s)   | 怪总掉血 | 说明");
                sb.AppendLine("---|------|---------|----------|------");
                int hitFrame = -1;
                float midWalk = 0f;
                for (int f = 1; f <= 20; f++)
                {
                    bool walkIn = f == 6;                       // 第 6 帧（0.10s）走进扇形：窗口还开着
                    if (walkIn) e.Pos = InConeShifted;
                    float before = hp0 - e.Health.Hp;
                    s.DebugAdvanceAttack(GameClock.FixedDt);
                    float lost = hp0 - e.Health.Hp;
                    bool justHit = lost > before + 1e-4f;
                    if (hitFrame < 0 && justHit) hitFrame = f;
                    midWalk = lost;
                    sb.AppendLine($"{f,2} | {(s.MeleeWindowActive ? "开" : "关"),-4} | {F(s.MeleeWindowSecondsLeft),7} | "
                                  + $"{F(lost),8} | "
                                  + (walkIn ? $"← 这一帧走进扇形（{F(InConeShifted.x)}, {F(InConeShifted.y)}）"
                                            : (justHit ? "重扫命中 ⇒ 掉血" : "未命中")));
                }
                sb.AppendLine($"⇒ 中途走进来的怪在**第 {hitFrame} 帧**掉血（窗口内），共掉 {F(midWalk)}");
                sb.AppendLine($"⇒ 判据二 {(atSwing < 1e-3f && midWalk > 1e-3f ? "PASS" : "FAIL")}"
                              + $"：挥击时 0 次、中途进来 {F(midWalk)}（期望掉一次）");
            }
            finally { s.Dispose(); }
            sb.AppendLine();
        }

        // ───────────────────────── ④ 判据三：窗口结束之后 ─────────────────────────

        private static void DumpAfterWindow(StringBuilder sb)
        {
            sb.AppendLine("═══ ④ 判据三：窗口**结束之后**才走进来 ⇒ 不掉血，要等下一次挥击 ═══");
            RunSession s = Bench(20260713UL, out EnemyActor e, OutOfCone);
            try
            {
                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                s.DebugAdvanceAttack(0.30f);                     // 把窗口走完（它仍在扇形外）
                float afterWindow = hp0 - e.Health.Hp;
                bool closed = !s.MeleeWindowActive;
                e.Pos = InConeShifted;                           // 窗口已关之后才走进扇形
                s.DebugAdvanceAttack(0.30f);
                float late = hp0 - e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);                // 下一次挥击
                float nextSwing = hp0 - e.Health.Hp;
                float reach = RunSession.MeleeJudgeRadius(
                    TableDatabase.Tuning.tools.hoe.range,
                    Actor.ScaledRadius(TableDatabase.GetEnemy(ProbeEnemyId).Radius));
                sb.AppendLine($"窗口走完（关闭 = {closed}）时掉 {F(afterWindow)}（应为 0）");
                sb.AppendLine($"窗口关闭后再走进扇形、又推 0.30s ⇒ 掉 {F(late)}（应为 0：判定已经不存在）");
                sb.AppendLine($"再挥一刀 ⇒ 掉 {F(nextSwing)}（应为一次命中；命中距离 {F(reach)}m 之内）");
                sb.AppendLine($"⇒ 判据三 {(late < 1e-3f && nextSwing > 1e-3f ? "PASS" : "FAIL")}"
                              + "：结束后 0 次、下一次挥击掉一次（不是 0 次）");
            }
            finally { s.Dispose(); }
            sb.AppendLine();
        }

        // ───────────────────────── 自检原文（绿的也印） ─────────────────────────

        private static string ExecuteSelfCheck()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            try
            {
                List<CheckResult> results = SelfCheck.RunAll();
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].Passed) pass++;
                    else fail++;
                    sb.AppendLine("  " + results[i]);
                }
                string head = $"自检 {(fail == 0 ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过，{fail} 失败";
                sb.Insert(0, head + "\n");
                Write(SelfCheckPath, sb);
                return head;
            }
            catch (Exception ex)
            {
                string crash = "SELFCHECK CRASH：" + ex.GetType().Name + "：" + ex.Message;
                sb.Insert(0, crash + "\n");
                Write(SelfCheckPath, sb);
                Debug.LogError("[PROBEV211] " + crash);
                return crash;
            }
        }

        // ───────────────────────── 夹具 / 杂项 ─────────────────────────

        /// <summary>
        /// 一局**只含我放的这一只怪**的可控战斗：玩家在原点、朝向 +X、暴击归零（伤害完全确定）。
        /// 与自检的 `PluginBench` 同一个形状，但这里只用**公开** API（探针不碰私有夹具）✓
        /// </summary>
        private static RunSession Bench(ulong seed, out EnemyActor enemy, Vector2 at)
        {
            var s = new RunSession();
            s.Start(seed);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);                       // 量的是"我打出多少"，不该被反杀打断
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);   // 暴击率归零 ⇒ 每一下伤害确定

            s.World.SpawnEnemy(ProbeEnemyId, at, 1);
            enemy = s.World.Enemies[s.World.Enemies.Count - 1];
            enemy.Pos = at;
            enemy.Health.Setup(1e5f, 0f);                    // 打不死，读数干净
            return s;
        }

        private static string Write(string relativePath, StringBuilder sb)
        {
            try
            {
                string path = Path.Combine(ProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return "报告已写入 " + relativePath + $"（{sb.Length} 字符）";
            }
            catch (Exception ex)
            {
                return "报告写盘失败：" + ex.Message;
            }
        }

        /// <summary>工程根：`Application.dataPath` 的上一级。编辑器与播放模式下都成立。</summary>
        private static string ProjectRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
