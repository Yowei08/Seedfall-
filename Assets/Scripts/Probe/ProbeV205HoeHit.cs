using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v205：锄头「扇形判定 / 挥击弧特效」的**只读**探针 ★★
    ///
    /// ── 量两件事 ──
    ///   ① **算**：五个敌人各自的 `判定半径 / 视觉半径 / 差`，以及两个方向上的张角
    ///      （数直接取自**编译后的** `RunSession.MeleeFxRadius` / `MeleeJudgeRadius`，
    ///       不是本文件里另抄一份 → 探针不会"自己和自己对答案"✓）
    ///   ② **测**：在一局可控战斗里，把 `grunt` 摆在若干 (距离, 角度) 上，**真挥一刀**，
    ///      读它的血量差 ⇒ 命中包络是**跑出来的**，不是推出来的。
    ///      摆的点刻意跨过三条边界：视觉半径 4.09m / 判定半径 4.335m / 半张角 50° ✓
    ///
    /// ── 为什么需要它（而不是只看自检）──
    ///   自检断言的是"视觉 < 判定"这个**不等式**；它证明不了"判定真的是个扇形"。
    ///   包络表里 `4.34m 命中 / 4.36m 未中`、`49° 命中 / 51° 未中` 这两对才是几何证据 ✓
    ///
    /// ── 只读性 ──
    ///   只调 `SelfCheck` / `RunSession` 的既有入口，**不写任何工程文件**，只往 `Library/` 写报告 ✓
    ///   只在 `Library/.yowei-run-v205` **存在**时跑一次（跑完删旗），平时零影响 ✓
    ///   图注那一段要往场景里放临时物体，**单独一个旗标** `.yowei-run-v205-shot` 才启用，
    ///   画完 30 秒由 `Object.Destroy(go, 30f)` 自行清掉，不进场景存档 ✓
    /// </summary>
    public static class ProbeV205HoeHit
    {
        public const string FlagPath = "Library/.yowei-run-v205";
        public const string ShotFlagPath = "Library/.yowei-run-v205-shot";
        public const string ReportPath = "Library/yowei-v205-hoehit.txt";

        /// <summary>摆点用的敌人（`enemies.json` 的 `grunt`，radius 0.36 ⇒ 缩放后 0.535）。</summary>
        private const string ProbeEnemyId = "grunt";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            bool wantShot = false;
            try
            {
                string root = Directory.GetCurrentDirectory();
                string flag = Path.Combine(root, FlagPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(flag)) return;              // ← 常态：零影响
                File.Delete(flag);
                wantShot = File.Exists(Path.Combine(root, ShotFlagPath.Replace('/', Path.DirectorySeparatorChar)));

                var sb = new StringBuilder();
                DumpGeometry(sb);
                DumpEnvelope(sb);
                DumpSelfCheck(sb);
                DumpSwingEvent(sb);

                string path = Path.Combine(root, ReportPath.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Debug.Log($"PROBE-V205 报告已写入 {ReportPath}（{sb.Length} 字符）");
            }
            catch (Exception ex)
            {
                Debug.LogError("PROBE-V205 异常：" + ex.GetType().Name + "：" + ex.Message);
            }

            if (!wantShot) return;
            try
            {
                var go = new GameObject("ProbeV205ShotDriver");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<ShotDriver>();
                UnityEngine.Object.Destroy(go, 40f);
            }
            catch (Exception ex)
            {
                Debug.LogError("PROBE-V205 图注驱动异常：" + ex.GetType().Name + "：" + ex.Message);
            }
        }

        // ───────────────────────── ① 几何（算） ─────────────────────────

        private static void DumpGeometry(StringBuilder sb)
        {
            TableDatabase.Load(true);
            TuningDef t = TableDatabase.Tuning;
            ToolDef hoe = t.tools.hoe;
            float range = hoe.range;
            float fxR = RunSession.MeleeFxRadius(range);
            float judgeHalf = RunSession.MeleeJudgeHalfArcDeg(hoe.arc);
            float fxArc = RunSession.MeleeFxArcDeg(hoe.arc);

            sb.AppendLine("═══ ① 几何：判定 vs 视觉（数取自编译后的 RunSession 常量）═══");
            sb.AppendLine($"锄头：range = {F(range)}m｜arc = {F(hoe.arc)}°｜hits = {hoe.hits}"
                          + $"｜baseValue = {F(hoe.baseValue)}｜HitRadiusScale = {F(Actor.HitRadiusScale)}");
            sb.AppendLine($"判定：半径 = range + e.Radius（e.Radius 已含 1.486）｜半张角 = arc/2 = {F(judgeHalf)}°"
                          + $" ⇒ 张角 {F(judgeHalf * 2f)}°");
            sb.AppendLine($"视觉：半径 = (range + 最瘦敌人radius×1.486) × {F(RunSession.MeleeFxRadiusScale)}"
                          + $" = {F(fxR)}m｜张角 = arc − {F(RunSession.MeleeFxArcShrinkDeg)}° = {F(fxArc)}°"
                          + $"（半张角 {F(fxArc * 0.5f)}°）");
            sb.AppendLine($"视觉参考敌人半径（未缩放）= {F(RunSession.MeleeFxRefBaseRadius)}"
                          + $"（= 表里最瘦的 shooter 0.34）");
            sb.AppendLine();
            sb.AppendLine("名称      | radius | 缩放后 | 判定半径 | 视觉半径 | 差(判定−视觉) | 判定张角 | 视觉张角");
            sb.AppendLine("----------|--------|--------|----------|----------|---------------|----------|--------");

            string[] ids = { "grunt", "shooter", "jumper", "boss", "moth", "pig" };
            int bad = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                EnemyDef d = TableDatabase.GetEnemy(ids[i]);
                if (d == null) { sb.AppendLine($"{ids[i],-9} | (表里查不到)"); bad++; continue; }
                float scaled = Actor.ScaledRadius(d.Radius);
                float judge = RunSession.MeleeJudgeRadius(range, scaled);
                float gap = judge - fxR;
                if (gap <= 0f) bad++;
                sb.AppendLine($"{d.Id,-9} | {F(d.Radius),6} | {F(scaled),6} | {F(judge),8} | {F(fxR),8} | "
                              + $"{F(gap),13} | {F(judgeHalf * 2f),8} | {F(fxArc),6}");
            }
            sb.AppendLine($"⇒ 视觉 < 判定 对以上每种敌人都成立：{(bad == 0 ? "是" : $"否（{bad} 条不成立）")}");
            sb.AppendLine("⇒ 两个方向的外圈都是判定：半径差全部为正、张角差 "
                          + $"{F(judgeHalf * 2f - fxArc)}°（{F(judgeHalf - fxArc * 0.5f)}°/侧）");
            sb.AppendLine();
        }

        // ───────────────────────── ② 实测包络（测） ─────────────────────────

        /// <summary>一个摆点：距离（米）与方位角（度，0 = 正前方）。</summary>
        private struct Shot { public float Dist, Deg; public string Why; }

        private static void DumpEnvelope(StringBuilder sb)
        {
            TableDatabase.Load(true);
            TuningDef t = TableDatabase.Tuning;
            float range = t.tools.hoe.range;
            float fxR = RunSession.MeleeFxRadius(range);
            float judgeR = RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(TableDatabase.GetEnemy(ProbeEnemyId).Radius));

            // 摆点：跨过三条边界
            var shots = new[]
            {
                new Shot { Dist = 4.33f, Deg = 0f,   Why = "判定 4.335 内 0.005 ⇒ 应命中（边界就在 4.335）" },
                new Shot { Dist = 4.34f, Deg = 0f,   Why = "判定 4.335 外 0.005 ⇒ 应未中（边界就在 4.335）" },
                new Shot { Dist = 4.20f, Deg = 0f,   Why = "视觉 4.09 外、判定 4.335 内 ⇒ 应命中（判定在外圈）" },
                new Shot { Dist = 4.05f, Deg = 0f,   Why = "视觉 4.09 内 ⇒ 应命中" },
                new Shot { Dist = 2.00f, Deg = 49f,  Why = "半张角 50° 内 ⇒ 应命中" },
                new Shot { Dist = 2.00f, Deg = 51f,  Why = "半张角 50° 外 ⇒ 应未中" },
                new Shot { Dist = 2.58f, Deg = 54.5f, Why = "自检夹具同一落点（(1.5,2.1)）⇒ 应未中" },
                new Shot { Dist = 3.60f, Deg = 30f,  Why = "斜向中段 ⇒ 应命中" },
            };

            sb.AppendLine("═══ ② 实测包络：真挥一刀，读敌人血量差 ═══");
            sb.AppendLine($"摆的是 `{ProbeEnemyId}`（radius 0.36 ⇒ 缩放后 "
                          + $"{F(Actor.ScaledRadius(TableDatabase.GetEnemy(ProbeEnemyId).Radius))}）"
                          + $"｜视觉半径 {F(fxR)}m｜判定半径 {F(judgeR)}m｜半张角 "
                          + $"{F(RunSession.MeleeJudgeHalfArcDeg(t.tools.hoe.arc))}°");
            sb.AppendLine("距离      | 角度  | 公式期望 | 实测伤害 | 一致 | 说明");
            sb.AppendLine("----------|-------|----------|----------|------|------");

            for (int i = 0; i < shots.Length; i++)
            {
                Shot s = shots[i];
                float dmg = SwingOnceAt(s.Dist, s.Deg);
                bool hit = dmg > 0f;
                // 期望：距离 ≤ 判定半径 且 角度 ≤ 半张角
                bool expect = s.Dist <= judgeR + 1e-4f
                              && Mathf.Abs(s.Deg) <= RunSession.MeleeJudgeHalfArcDeg(t.tools.hoe.arc) + 1e-4f;
                string verdict = hit == expect ? "OK  " : "差异";
                sb.AppendLine($"{F(s.Dist),8}m | {F(s.Deg),4}° | {(expect ? "命中" : "未中")}   | "
                              + $"{F(dmg),8} | {verdict} | {(hit ? "命中" : "未中")}｜{s.Why}");
            }
            sb.AppendLine();
        }

        /// <summary>
        /// 在一局干净战斗里，把 `grunt` 摆到 (dist, deg)，**真的挥一刀**（走 `DebugAttackOnce`，
        /// 与玩家按键同一条 `UseTool`），返回它掉的血。
        /// ★ 一局一个新 session：`hits = 3` 的每刀上限、以及双击的第二刀都会污染读数 ✓
        /// </summary>
        private static float SwingOnceAt(float dist, float deg)
        {
            var s = new RunSession();
            try
            {
                s.Start(20260501UL);
                s.StartWave(1);
                s.World.Enemies.Clear();
                PlayerActor p = s.World.Player;
                p.Pos = Vector2.zero;
                p.AimDir = Vector2.right;
                p.SwitchWindup = 0f;
                for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
                p.Health.Setup(1e6f, 0f);
                s.Progress.Stats.SetBase(StatId.CritRate, 0f);      // 伤害完全确定

                float rad = deg * Mathf.Deg2Rad;
                Vector2 at = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * dist;
                s.World.SpawnEnemy(ProbeEnemyId, at, 1);
                EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
                e.Pos = at;
                e.Health.Setup(1e5f, 0f);                            // 打不死，读数干净

                float hp0 = e.Health.Hp;
                s.DebugAttackOnce(WeaponSlot.Hoe);
                return hp0 - e.Health.Hp;
            }
            finally
            {
                s.Dispose();
            }
        }

        // ───────────────────────── ③ 自检计数 ─────────────────────────

        private static void DumpSelfCheck(StringBuilder sb)
        {
            List<CheckResult> rs = SelfCheck.RunAll();
            int bad = 0;
            for (int i = 0; i < rs.Count; i++) if (!rs[i].Passed) bad++;
            sb.AppendLine("═══ ③ 自检 ═══");
            sb.AppendLine($"SELFCHECK {rs.Count - bad}/{rs.Count}（失败 {bad} 条）");
            for (int i = 0; i < rs.Count; i++)
                if (rs[i].Name != null && rs[i].Name.StartsWith("v205", StringComparison.Ordinal))
                    sb.AppendLine("  " + rs[i]);
            if (bad > 0)
            {
                sb.AppendLine("—— 失败清单 ——");
                for (int i = 0; i < rs.Count; i++) if (!rs[i].Passed) sb.AppendLine("  " + rs[i]);
            }
            sb.AppendLine();
        }

        // ───────────────────────── ④ 事件里真的发了什么 ─────────────────────────

        private static void DumpSwingEvent(StringBuilder sb)
        {
            TableDatabase.Load(true);
            TuningDef t = TableDatabase.Tuning;
            float range = t.tools.hoe.range;
            sb.AppendLine("═══ ④ MeleeSwingEvent 的两个字段（表现层读的就是这两个数）═══");
            sb.AppendLine($"Range  = {F(RunSession.MeleeFxRadius(range))}m"
                          + $"（判定那边是 {F(RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(0.34f)))}"
                          + $" ~ {F(RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(1.28f)))}）");
            sb.AppendLine($"ArcDeg = {F(RunSession.MeleeFxArcDeg(t.tools.hoe.arc))}°"
                          + $"（判定 {F(RunSession.MeleeJudgeHalfArcDeg(t.tools.hoe.arc) * 2f)}°）");
            sb.AppendLine($"⇒ WorldView.FxBladeMesh 的 halfDeg 必须 = {F(RunSession.MeleeFxArcDeg(t.tools.hoe.arc) * 0.5f)}"
                          + "（改过那一行就对齐；不改则弧比判定宽）");
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        // ───────────────────────── 图注（可选，旗标控制） ─────────────────────────

        /// <summary>
        /// 抓图用：等场景稳定 → 把 `DebugSwingHold` 拉长 → 用**真实视觉几何**发一次挥击
        /// → 画三圈标注（视觉边界 / grunt 判定边界 / boss 判定边界）→ 交给外部抓图。
        /// 所有物体 30 秒后自动销毁 ✓
        /// </summary>
        private sealed class ShotDriver : MonoBehaviour
        {
            private float _t;
            private bool _fired;

            private void Update()
            {
                if (_fired) return;
                _t += Time.unscaledDeltaTime;
                if (_t < 3f) return;            // 等 WorldView 把场景搭好
                _fired = true;
                try
                {
                    TuningDef t = TableDatabase.Tuning;
                    float range = t.tools.hoe.range;
                    WorldView.DebugSwingHold = 24f;      // 正常 0.20s，抓图通道拉长（既有开关）
                    Bus.Publish(new MeleeSwingEvent
                    {
                        Pos = new Vector2(0f, 0f),
                        Dir = new Vector2(0f, 1f),
                        Range = RunSession.MeleeFxRadius(range),
                        ArcDeg = RunSession.MeleeFxArcDeg(t.tools.hoe.arc),
                        Fx = default
                    });
                    Ring("ProbeJudge_grunt", RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(0.36f)),
                         RunSession.MeleeJudgeHalfArcDeg(t.tools.hoe.arc), new Color(1f, 0.20f, 0.20f));
                    Ring("ProbeJudge_boss", RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(1.20f)),
                         RunSession.MeleeJudgeHalfArcDeg(t.tools.hoe.arc), new Color(1f, 0.55f, 0.05f));
                    Ring("ProbeFx", RunSession.MeleeFxRadius(range),
                         RunSession.MeleeFxArcDeg(t.tools.hoe.arc) * 0.5f, new Color(0.20f, 0.95f, 1f));
                }
                catch (Exception ex)
                {
                    Debug.LogError("PROBE-V205 图注异常：" + ex.GetType().Name + "：" + ex.Message);
                }
            }

            /// <summary>画一条圆弧 + 两条半径线（纯标注，白色/彩色的线，不参与任何判定）。</summary>
            private static void Ring(string name, float radius, float halfDeg, Color color)
            {
                var go = new GameObject(name);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = false;
                lr.widthMultiplier = 0.06f;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = color; lr.endColor = color;
                const int segs = 48;
                var pts = new List<Vector3>(segs + 4);
                for (int i = 0; i <= segs; i++)
                {
                    float a = Mathf.Lerp(-halfDeg, halfDeg, i / (float)segs) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(Mathf.Sin(a) * radius, 0.62f, Mathf.Cos(a) * radius));
                }
                // 两条边界半径线
                for (int k = 0; k < 2; k++)
                {
                    float a = (k == 0 ? -halfDeg : halfDeg) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(0f, 0.62f, 0f));
                    pts.Add(new Vector3(Mathf.Sin(a) * radius, 0.62f, Mathf.Cos(a) * radius));
                }
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
                UnityEngine.Object.Destroy(go, 30f);
            }
        }
    }
}
