using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v210：**敌人碰撞面积**的只读读数探针（接触层 + 命中距离）★★
    ///
    /// ── 它回答哪两个问题 ──
    ///   ① **命中距离**（玩家打得到多远的敌人）：`range + 敌人 Radius`，
    ///      六种敌人逐个报（数取自**编译后的** `RunSession.MeleeJudgeRadius` 与
    ///      `Actor.ScaledRadius`，不是本文件另抄一份 ⇒ 不会"自己和自己对答案"）
    ///   ② **接触判据**（敌人贴到多近才碰伤玩家）：`target.Radius + Radius × ContactScale + 0.06`，
    ///      并且**真的跑一遍**：把敌人摆在一串距离上、`Tick` 一帧、看玩家的
    ///      `Hp + Shield` 是否掉 ⇒ 阈值是**测出来的**，不是推出来的
    ///
    /// ── 为什么必须实测（而不是只写算式）──
    ///   两个数都是"看着对、实际不对"的高危区：
    ///     · `Radius` 在 `World.SpawnEnemy` 里已乘过 `HitRadiusScale` 1.486 ⇒ 手算时漏乘是最常见的错
    ///     · 接触判据同时被 `reach`（`TouchTick`）、追击停距（`GruntEnemy`）、撞车停距（`MothIdleTick`）
    ///       三处使用，任何一处漏乘 `ContactScale` 都会让"停下来了却不掉血"——
    ///       这种毛病不抛异常、不写日志，只有摆距离量一遍才看得见 ✗
    ///
    /// ── 只读性（重要）──
    ///   不写任何工程文件、不改场景、**不带任何 `[RuntimeInitializeOnLoadMethod]` 自动创建** ✓
    ///   它只在自己被挂在某个物体上、并被显式调用 `Dump()` 时执行一次 ✓
    ///   世界是**现建的**（与 `SelfCheck.MakeWorld` 同一套最小世界），不碰正在跑的那一局 ✓
    /// </summary>
    public sealed class ProbeV210Contact : MonoBehaviour
    {
        /// <summary>改前（v209）`enemies.json` 的六个 `radius`，只用于"改前 → 改后"的对照列。</summary>
        private static readonly string[] Ids = { "pig", "grunt", "shooter", "jumper", "boss", "moth" };
        private static readonly float[] RadiusBefore = { 0.40f, 0.36f, 0.34f, 0.56f, 1.20f, 1.28f };

        /// <summary>接触判据里的那个常数项（`TouchTick` 的 `+ 0.06f`）。</summary>
        private const float ReachEpsilon = 0.06f;

        /// <summary>摆点扫描的分辨率（米）。</summary>
        private const float ScanStep = 0.002f;

        public string Dump()
        {
            var sb = new StringBuilder();
            try
            {
                TableDatabase.Load(true);
                TuningDef t = TableDatabase.Tuning;
                float range = t.tools.hoe.range;
                float playerR = PlayerRadius(out string playerWhy);

                sb.AppendLine("═══ v210 敌人碰撞面积：接触层与命中距离（只读实测）═══");
                sb.AppendLine($"玩家 Radius = {F(playerR)}m（{playerWhy}）");
                sb.AppendLine($"锄头 range = {F(range)}m｜HitRadiusScale = {F(Actor.HitRadiusScale)}"
                              + $"｜ContactScale = {F(EnemyActor.ContactScale)}");
                sb.AppendLine($"视觉弧半径 MeleeFxRadius = {F(RunSession.MeleeFxRadius(range))}m"
                              + $"（参考敌人常数 {F(RunSession.MeleeFxRefBaseRadius)}，未随本轮半径缩小 ⇒ 见交付报告的冲突①）");
                sb.AppendLine();

                // ── ① 命中距离与接触距离的"算式"读数 ──
                sb.AppendLine("① 六种敌人：radius 改前→改后｜缩放后 Radius｜命中距离(改前→改后)"
                              + "｜接触 reach(改前→改后)｜追击停距(改前→改后)");
                sb.AppendLine("敌人     | radius改前→改后 | 缩放后 | 命中(前→后)      | 接触(前→后)      | 停距(前→后)");
                sb.AppendLine("---------|-----------------|--------|------------------|------------------|-----------------");
                for (int i = 0; i < Ids.Length; i++)
                {
                    EnemyDef d = TableDatabase.GetEnemy(Ids[i]);
                    if (d == null) { sb.AppendLine($"{Ids[i],-8} | (表里查不到；已跳过)"); continue; }
                    float scaledBefore = Actor.ScaledRadius(RadiusBefore[i]);
                    float scaledNow = Actor.ScaledRadius(d.Radius);          // = 运行时的 e.Radius ✓
                    float hitBefore = RunSession.MeleeJudgeRadius(range, scaledBefore);
                    float hitNow = RunSession.MeleeJudgeRadius(range, scaledNow);
                    float reachBefore = playerR + scaledBefore + ReachEpsilon;
                    float reachNow = playerR + scaledNow * EnemyActor.ContactScale + ReachEpsilon;
                    float stopBefore = playerR + scaledBefore;
                    float stopNow = playerR + scaledNow * EnemyActor.ContactScale;
                    sb.AppendLine($"{d.Id,-8} | {F(RadiusBefore[i])}→{F(d.Radius),-11} | {F(scaledNow),6} | "
                                  + $"{F(hitBefore)}→{F(hitNow),-9} | {F(reachBefore)}→{F(reachNow),-9} | "
                                  + $"{F(stopBefore)}→{F(stopNow)}");
                }
                sb.AppendLine();

                // ── ② 行为实测：接触阈值真的是"测出来的" ──
                sb.AppendLine("② 摆点实测：把敌人摆在一串距离上，Tick 一帧，看玩家 Hp+Shield 是否掉");
                sb.AppendLine("敌人     | 预期接触 reach | 实测阈值（最大掉血距离 / 最小不掉血距离） | 与算式差");
                sb.AppendLine("---------|----------------|-------------------------------------------|-----------");
                for (int i = 0; i < Ids.Length; i++)
                {
                    EnemyDef d = TableDatabase.GetEnemy(Ids[i]);
                    if (d == null) continue;
                    if (d.TouchDamage <= 0f) { sb.AppendLine($"{d.Id,-8} | 无接触伤害（touchDamage=0）⇒ 不测"); continue; }
                    // ★ 只测"每帧都会调 `TouchTick`"的两种（grunt 224 / shooter 256）★
                    //   精英跳跃者 / BOSS / 飞蛾的接触伤害在**具体招式状态**里才打
                    //   （跳砸、冲锋、冲刺 —— 由状态机里的那一行 `TouchTick` 负责）
                    //   ⇒ 空转一帧当然不掉血，摆点扫描对它们没有意义 ✗
                    //   它们那一层同样乘了 `ContactScale`（`reach` 是同一个方法），算式读数见 ① ✓
                    if (d.Id != "grunt" && d.Id != "shooter")
                    {
                        sb.AppendLine($"{d.Id,-8} | 接触伤害只在招式状态里打（跳砸/冲锋/冲刺）⇒ 摆点扫描不适用，见 ① 的算式读数");
                        continue;
                    }
                    float expected = playerR + Actor.ScaledRadius(d.Radius) * EnemyActor.ContactScale + ReachEpsilon;
                    float lo = expected - 0.08f;
                    float hi = expected + 0.08f;
                    float maxHit = float.NaN, minMiss = float.NaN;
                    for (float at = lo; at <= hi + 1e-4f; at += ScanStep)
                    {
                        if (TouchDamageAt(d.Id, at, playerR) > 0f) maxHit = at; else minMiss = at;
                    }
                    string diff = float.IsNaN(maxHit) || float.IsNaN(minMiss)
                        ? "（扫描窗口边界）"
                        : $"{F(minMiss - expected, 4)}m";
                    sb.AppendLine($"{d.Id,-8} | {F(expected),14} | 掉血到 {F(maxHit)} / 不掉从 {F(minMiss)} | {diff}");
                }
                sb.AppendLine();

                // ── ③ 结论行（一句话能贴进交付报告）──
                EnemyDef g = TableDatabase.GetEnemy("grunt");
                if (g != null)
                {
                    float scaled = Actor.ScaledRadius(g.Radius);
                    sb.AppendLine("③ 结论（grunt）");
                    sb.AppendLine($"   命中距离：{F(RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(RadiusBefore[1])))}"
                                  + $" → {F(RunSession.MeleeJudgeRadius(range, scaled))}m"
                                  + $"（{F(RunSession.MeleeJudgeRadius(range, scaled) - RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(RadiusBefore[1])))}）");
                    sb.AppendLine($"   接触距离：{F(playerR + Actor.ScaledRadius(RadiusBefore[1]) + ReachEpsilon)}"
                                  + $" → {F(playerR + scaled * EnemyActor.ContactScale + ReachEpsilon)}m"
                                  + $"（{F(playerR + scaled * EnemyActor.ContactScale + ReachEpsilon - (playerR + Actor.ScaledRadius(RadiusBefore[1]) + ReachEpsilon))}）");
                    sb.AppendLine($"   reach - 停距 = {F(EnemyActor.ContactScale * scaled + ReachEpsilon - EnemyActor.ContactScale * scaled)}m"
                                  + "（恒为那个 0.06 常数 ⇒ 停下来之后仍然在接触范围内 ✓）");
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("PROBE-V210 异常：" + e.GetType().Name + "：" + e.Message);
                sb.AppendLine(e.StackTrace ?? "");
            }
            string text = sb.ToString();
            Debug.Log("[PROBE-V210]\n" + text);
            return text;
        }

        /// <summary>
        /// 真实会话里的玩家半径（若没有活会话，退回那个**等价**的常数并说明来历）。
        ///
        /// ★ 实测教训（v210）：这个值不是 `Actor` 的默认 0.45 ★
        ///   `RunSession` 建玩家时给的是 `Actor.ScaledRadius(0.50f)` = **0.743m**
        ///   （`Flow/RunSession.cs` 里 v180 那条"减小玩家受击面积"的注释写的就是这个数）
        ///   ⇒ 用 0.45 去算接触距离会整体少 0.29m，与实战读数对不上 ✗
        /// </summary>
        private static float PlayerRadius(out string why)
        {
            for (int i = 0; i < GameBootstrap.Live.Count; i++)
            {
                GameBootstrap b = GameBootstrap.Live[i];
                if (b == null || b.Session == null) continue;
                PlayerActor p = b.Session.World != null ? b.Session.World.Player : null;
                if (p == null) continue;
                why = "取自正在跑的会话 World.Player";
                return p.Radius;
            }
            why = "无活会话 ⇒ 用 RunSession 给玩家的那个固定值 ScaledRadius(0.50) = 0.743";
            return Actor.ScaledRadius(0.50f);
        }

        /// <summary>
        /// 建一个最小世界（与 `SelfCheck.MakeWorld` 同形），把一只敌人摆在 <paramref name="dist"/> 米处，
        /// `Tick` 一帧，返回玩家掉的血（Hp + Shield 的减少量；0 = 这一帧没碰到）。
        ///
        /// ★ 为什么用 `Hp + Shield` ★
        ///   玩家开局有护盾 ⇒ 0.455 的接触伤害先吃护盾，只看 `Hp` 会**恒为 0**（假阴性）✗
        /// ★ 为什么每点都重建世界 ★
        ///   受击有**无敌帧** ⇒ 同一个玩家连测多个点，第二点起必然不掉血 ✗
        /// ★ 为什么玩家半径要传进来（v210 实测教训）★
        ///   第一版这里写死 0.45（`Actor` 的默认值），而**真实玩家是 0.743**（= 0.5 × HitRadiusScale）
        ///   ⇒ 扫描窗口整体偏了 0.29m，窗口里一格都不掉血、读数全 NaN ✗
        ///   现在一律用调用方量到的那个值，扫描窗口才算在正确的位置上 ✓
        /// </summary>
        private static float TouchDamageAt(string enemyId, float dist, float playerRadius)
        {
            TuningDef t = TableDatabase.Tuning;
            var w = new World(t, 12345UL);
            var p = new PlayerActor
            {
                Id = w.NextActorId(),
                Team = Team.Player,
                Progress = new PlayerProgress(t),
                Pos = Vector2.zero,
                Radius = playerRadius
            };
            p.Health.Setup(p.Progress.Stats.Get(StatId.MaxHp), p.Progress.Stats.Get(StatId.Shield));
            w.Player = p;

            w.SpawnEnemy(enemyId, new Vector2(dist, 0f), 1);
            if (w.Enemies.Count == 0) return 0f;
            EnemyActor e = w.Enemies[0];

            float before = p.Health.Hp + p.Health.Shield;
            e.Tick(0.0001f, w);                 // dt 极小 ⇒ 位移 ≤ 0.0003m，测的仍是"摆在哪"
            float after = p.Health.Hp + p.Health.Shield;
            return Mathf.Max(0f, before - after);
        }

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        private static string F(float v, int digits) => v.ToString("0." + new string('#', Mathf.Max(1, digits)), CultureInfo.InvariantCulture);

        /// <summary>
        /// 收工：把自己的宿主物体删掉 —— 它只是**临时脚手架**，量完就走 ✓
        ///
        /// 为什么需要它：`/scene create` 会在**当前场景**里建物体（并标脏）。桥没有删除路由 ⇒
        /// 没有这一步的话，一个叫 `ProbeV210` 的空物体就会留在场里，
        /// 将来谁按一次 Ctrl+S 就把它写进 `SeedfallArena.unity` ✗
        /// </summary>
        public string Dispose()
        {
            string name = gameObject.name;
            UnityEngine.Object.DestroyImmediate(gameObject);
            return "已删除宿主物体 " + name + "（场景未保存 ⇒ 磁盘上的 .unity 不变）";
        }
    }
}
