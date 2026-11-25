using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v196 只读探针（第 2 版）★★ —— 这一轮要的四组读数：
    ///   ① 自检原文
    ///   ② **小数伤害读数**：`4.55` 打 800 血的怪 ⇒ 实扣 4.55 / 弹字 `-4.55` / 无 `-0` /
    ///      同一个 4.55 打玩家仍落成 5 血点；DoT（毒 / 火）18.75 不取整
    ///   ③ **玩家成长 · DPS**（1→30 分）：30 分钟跑局采样（不死处理见那里的说明）
    ///   ④ **敌人数值表**：五种 × base + 曲线 + 1/10/20/30 分血量/整数血点/几刀打死
    ///   ⑤ **商品数值表**：种子 / 道具 / 插件 × D→S 档 × 1/5/10/15/20/25/30 分实付价
    ///   ⑥ **刷新 / 重抽价**（自己的曲线）
    ///   ⑦ 商品价硬检查：30 分钟最高 ≤ 2000 ?｜有没有 ≥ 10000 ?
    ///
    /// ── 第 1 版（v196.1）暴露的两个**测量缺陷**，本版修掉 ──
    ///   ① DoT 那两条读数被**上一个测试残留的寒冷**污染：同一个敌人挨了 4 次命中 ⇒
    ///      叠满寒冷 ⇒ 冻结 ⇒ 破冰伤害（1.2 × 累计伤害）混进"这一跳"里 ⇒
    ///      `57.57` / `37.5` 这两个数**不是**毒/火的跳伤 ✗
    ///      ⇒ 现在**每个子测试用一只全新的敌人**（新的 `EnemyActor` ⇒ 新的 `StatusRuntime`）✓
    ///   ② 成长跑局在第 3 分钟就结束（`RunSimulator` 的 `keepAlive` 只在**步前**补满，
    ///      仍可能死在一步之内）⇒ 采样只剩第 1 分钟 ⇒ 第 10/20/30 分钟的"每击"是假的 ✗
    ///      ⇒ observe 钩子里每步把血量顶到 1e7（与 `DebugApi.ProbeLongRun` 同一招）✓
    ///   两个缺陷都是**探针自己的错**，不是被测代码的错 —— 记在这里，免得下一个人重犯 ✓
    ///
    /// ★ 它是**只读**的 ★ 不写任何工程文件、不改场景、不改存档；`World` / `StatBlock` 都在内存里
    ///   新建、跑完就地丢掉 ✓ 开关是 `Library/yowei_v196_probe.on`，结果写 `Library/yowei-v196-probe.txt` ✓
    /// </summary>
    public static class ProbeV196Num
    {
        private static string ProjRoot => Path.Combine(Application.dataPath, "..");
        private static string FlagPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei_v196_probe.on");
        private static string OutPath => Path.Combine(Path.Combine(ProjRoot, "Library"), "yowei-v196-probe.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (!File.Exists(FlagPath)) return;

            var all = new StringBuilder();
            all.AppendLine("═══ v196 探针（只读）═══");

            try
            {
                List<CheckResult> results = SelfCheck.RunAll();
                int pass = 0;
                for (int i = 0; i < results.Count; i++) if (results[i].Passed) pass++;
                all.AppendLine("────── ① 自检 ──────");
                all.AppendLine($"自检 {(pass == results.Count ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过");
                var fails = new StringBuilder();
                for (int i = 0; i < results.Count; i++)
                    if (!results[i].Passed) fails.AppendLine(results[i].ToString());
                if (fails.Length > 0) all.Append(fails);
            }
            catch (Exception e)
            {
                all.AppendLine("自检 CRASH " + e.GetType().Name + ": " + e.Message);
                all.AppendLine(e.StackTrace ?? "");
            }

            try { all.AppendLine(Run()); }
            catch (Exception e)
            {
                all.AppendLine("PROBE CRASH " + e.GetType().Name + ": " + e.Message);
                all.AppendLine(e.StackTrace ?? "");
            }

            string text = all.ToString();
            try { File.WriteAllText(OutPath, text, new UTF8Encoding(false)); }
            catch (Exception e) { Log.Warn("PROBEV196 写结果文件失败：" + e.Message); }
            Log.Info("PROBEV196 结果已写入 " + OutPath);
            Log.Info("\n" + text);
        }

        public static string Run()
        {
            TableDatabase.Load(true);
            var sb = new StringBuilder();
            AppendFractionDamage(sb);
            List<Grow> grow = SampleGrowth(out string simLog);
            sb.Append(simLog);
            AppendEnemyTable(sb, grow);
            AppendShopTable(sb);
            AppendRerollTable(sb);
            AppendPriceChecks(sb);
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════
        // 弹字捕获
        // ══════════════════════════════════════════════════════════════

        /// <summary>直接订阅 `Bus`：读到的就是 `Actor.Popup` **发布出去的那一串**（不经 HUD）。</summary>
        private sealed class PopupSpy
        {
            public readonly List<string> Raw = new List<string>();
            private Action<FloatingTextEvent> _handler;

            public void Attach()
            {
                _handler = e => Raw.Add(e.Text);
                Bus.Subscribe(_handler);
            }

            public void Detach() { if (_handler != null) Bus.Unsubscribe(_handler); _handler = null; }
            public void Clear() { Raw.Clear(); }
            public int Count => Raw.Count;
            public string Last => Raw.Count > 0 ? Raw[Raw.Count - 1] : "（无弹字）";
            public string Only
            {
                get
                {
                    if (Raw.Count == 0) return "（无弹字）";
                    if (Raw.Count == 1) return Raw[0];
                    var sb = new StringBuilder();
                    sb.Append('（').Append(Raw.Count).Append(" 条：");
                    for (int i = 0; i < Raw.Count; i++) { if (i > 0) sb.Append(" / "); sb.Append(Raw[i]); }
                    sb.Append('）');
                    return sb.ToString();
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // ② 小数伤害读数
        // ══════════════════════════════════════════════════════════════

        /// <summary>一只**全新的**靶子（新的 `StatusRuntime`）—— 每个子测试一只，杜绝状态串味。</summary>
        private static EnemyActor Fresh(World w, float hp, int slot)
        {
            w.SpawnEnemy("grunt", new Vector2(2f + slot * 3.5f, 0f), 1);
            EnemyActor e = w.Enemies[w.Enemies.Count - 1];
            e.Health.Setup(hp, 0f);
            return e;
        }

        private static void AppendFractionDamage(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ② 小数伤害读数（判据：4.55 实扣 4.55 / 弹字 -4.55 / 无 -0 / DoT 18.75）──────");

            var s = new RunSession();
            s.Start(20261018UL);
            try
            {
                World w = s.World;
                var spy = new PopupSpy();
                spy.Attach();
                try
                {
                    // ── ① 玩家 → 敌人：4.55 打 800 血的怪 ⇒ 实扣 4.55（不是 5）──
                    EnemyActor e = Fresh(w, 800f, 0);
                    spy.Clear();
                    float before = e.Health.Hp;
                    DamageResult r = w.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical, RawValue = 4.55f, Source = w.Player, Target = e, IgnoreDodge = true
                    });
                    float after = e.Health.Hp;
                    sb.AppendLine($"  ① 敌人（800 血）挨 4.55 ⇒ 血 {before:0.###} → {after:0.###}｜实扣 **{before - after:0.###}**" +
                                  $"（`DamageResult.Dealt` = {r.Dealt:0.###}）｜弹字「{spy.Only}」" +
                                  $"⇒ {(Mathf.Abs((before - after) - 4.55f) < 1e-4f ? "实扣 4.55 ✓（不是 5）" : "✗ 不是 4.55")}");

                    // ── ② 连打两次不累积漂移 ──
                    spy.Clear();
                    before = e.Health.Hp;
                    w.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 4.55f, Target = e, IgnoreDodge = true });
                    sb.AppendLine($"  ② 同一只再挨一次 4.55 ⇒ 血 {before:0.###} → {e.Health.Hp:0.###}" +
                                  $"（两次合计 {800f - e.Health.Hp:0.###}）｜弹字「{spy.Last}」");

                    // ── ③ 格式 `0.##`（四只新靶子，互不干扰）──
                    float[] fmts = { 4.5f, 18.75f, 800f, 4f };
                    var shown = new string[fmts.Length];
                    for (int i = 0; i < fmts.Length; i++)
                    {
                        EnemyActor fe = Fresh(w, 5000f, 1 + i);
                        spy.Clear();
                        w.DealDamage(new DamageRequest
                        {
                            Type = DamageType.Physical, RawValue = fmts[i], Source = w.Player, Target = fe, IgnoreDodge = true
                        });
                        shown[i] = spy.Only;
                    }
                    sb.AppendLine($"  ③ 格式 `0.##`：4.5 →「{shown[0]}」｜18.75 →「{shown[1]}」｜800 →「{shown[2]}」｜4.0 →「{shown[3]}」" +
                                  $"（整数**不带**小数点、不补零；一位小数只印一位）");

                    // ── ④ `-0`：门槛之上一律有非零位，门槛之下一律不弹 ──
                    EnemyActor e4 = Fresh(w, 800f, 6);
                    spy.Clear();
                    w.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 0.05f, Target = e4, IgnoreDodge = true });
                    string tMin = spy.Only;
                    int nAbove = spy.Count;
                    spy.Clear();
                    w.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 0.004f, Target = e4, IgnoreDodge = true });
                    int nBelow = spy.Count;
                    sb.AppendLine($"  ④ `-0` 检查：0.05（刚好过门槛）→「{tMin}」（{nAbove} 条）｜0.004（门槛之下）→ {nBelow} 条" +
                                  $"｜门槛 {Actor.PopupMinAmount:0.####} ＞ 0.005（两位小数的半个单位）⇒ **`-0` 不可能出现**" +
                                  $"{(tMin == "-0.05" && nAbove == 1 && nBelow == 0 ? " ✓" : " ✗")}" +
                                  $"｜血量 800 → {e4.Health.Hp:0.###}（0.004 那一下真的扣了，只是不弹字）");

                    // ── ⑤ 同一个 4.55 打在**玩家**身上 ⇒ 仍是整数血点 ──
                    PlayerActor p = w.Player;
                    p.Health.Setup(10f, 0f);
                    p.InvulnRemaining = 0f;
                    spy.Clear();
                    float pBefore = p.Health.Hp;
                    w.DealDamage(new DamageRequest
                    {
                        Type = DamageType.Physical, RawValue = 4.55f, Target = p, IgnoreDodge = true
                    });
                    float pAfter = p.Health.Hp;
                    int expect = EnemyDef.PlayerDamagePoints(4.55f);
                    sb.AppendLine($"  ⑤ 玩家（10 血 / 无盾）挨**同一个 4.55** ⇒ 血 {pBefore:0.###} → {pAfter:0.###}" +
                                  $"｜实扣 **{pBefore - pAfter:0.###}** = 整数血点 {expect}（`PlayerDamagePoints(4.55)`）" +
                                  $"｜弹字「{spy.Only}」⇒ 敌人→玩家仍是整数 {(Mathf.Abs((pBefore - pAfter) - expect) < 1e-4f ? "✓" : "✗")}");

                    // ── ⑥ DoT 毒：0.15 × 125 = 18.75，**新靶子**（无寒冷残留）──
                    EnemyActor ep = Fresh(w, 800f, 7);
                    ep.Status.Apply(AilmentKind.Poison, 125f, t.formula, 1f);
                    before = ep.Health.Hp;
                    ep.Status.Tick(1.01f, ep, w);            // 一跳（tickInterval = 1.0）
                    after = ep.Health.Hp;
                    float poisonTick = t.formula.poisonTickFactor * 125f;
                    sb.AppendLine($"  ⑥ DoT 毒跳伤 = {t.formula.poisonTickFactor:0.####} × 125 = **{poisonTick:0.###}**" +
                                  $" ⇒ 血 {before:0.###} → {after:0.###}｜实扣 **{before - after:0.###}**" +
                                  $"{(Mathf.Abs((before - after) - poisonTick) < 1e-4f ? " ✓ 未取整（改前会被抬成 19）" : " ✗ 不是 18.75")}");

                    // ── ⑦ DoT 火：0.18 × 104.1667 = 18.75，另一只新靶子 ──
                    EnemyActor eb = Fresh(w, 800f, 8);
                    eb.Status.Apply(AilmentKind.Burn, 104.1667f, t.formula, 1f);
                    before = eb.Health.Hp;
                    eb.Status.Tick(1.01f, eb, w);
                    after = eb.Health.Hp;
                    float burnTick = t.formula.burnTickFactor * 104.1667f;
                    sb.AppendLine($"  ⑦ DoT 火跳伤 = {t.formula.burnTickFactor:0.####} × 104.1667 = **{burnTick:0.###}**" +
                                  $" ⇒ 血 {before:0.###} → {after:0.###}｜实扣 **{before - after:0.###}**" +
                                  $"{(Mathf.Abs((before - after) - burnTick) < 1e-4f ? " ✓ 未取整" : " ✗ 不是 18.75")}");

                    // ── ⑧ 破冰击（玩家造成的第三类小数伤害）：1.2 × 累计寒冷伤害 ──
                    EnemyActor ei = Fresh(w, 800f, 9);
                    ei.Status.AddChill(1, 50f, t.formula);       // 累计寒冷伤害 50
                    ei.Status.Frozen = true;
                    ei.Status.FrozenRemaining = 0.5f;
                    before = ei.Health.Hp;
                    ei.Status.Tick(0.6f, ei, w);
                    after = ei.Health.Hp;
                    float breakExpect = t.formula.freezeBreakFactor * 50f;
                    sb.AppendLine($"  ⑧ 破冰伤害 = {t.formula.freezeBreakFactor:0.##} × 50 = **{breakExpect:0.##}**" +
                                  $" ⇒ 血 {before:0.###} → {after:0.###}｜实扣 **{before - after:0.###}**" +
                                  $"{(Mathf.Abs((before - after) - breakExpect) < 1e-3f ? " ✓" : " ✗")}" +
                                  $"（它同样走 `ApplyDamageSplit` 的小数侧）");
                }
                finally { spy.Detach(); }
            }
            finally { s.Dispose(); }
        }

        // ══════════════════════════════════════════════════════════════
        // ③ 玩家成长 · DPS
        // ══════════════════════════════════════════════════════════════

        private struct Grow
        {
            public int Minute, Level, Kills, Gold;
            public float MaxHp, Melee, AtkMult, CritRate, CritDmg, AtkSpeed;
            public float PerHit, PerHitCrit, Expected, Interval;
            public float Dps => Interval <= 0f ? 0f : Expected / Interval;
            public float SwingsPerSec => Interval <= 0f ? 0f : 1f / Interval;
        }

        private static List<Grow> SampleGrowth(out string log)
        {
            TuningDef t = TableDatabase.Tuning;
            var rows = new List<Grow>();
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("────── ③ 玩家成长 · DPS（30 分钟跑局，Bot=Balanced，seed=20261018）──────");
            sb.AppendLine("  ★ 不死处理：observe 钩子里每步把玩家血量顶到 1e7（与 `DebugApi.ProbeLongRun` 同一招）★");
            sb.AppendLine("    目的只有一个：让跑局真的走到第 30 分钟，好量「能活到那一刻的玩家」有多少伤害。");
            sb.AppendLine("    它**不作平衡结论**（不是「这局能赢」的证据），只用于 DPS / 几刀这两个读数 ✓");

            float started = Time.realtimeSinceStartup;
            int lastMinute = -1;
            int steps = 0;
            float bestRunTime = 0f;
            string endInfo = "（跑局没返回）";
            try
            {
                // ★ 为什么不用 `RunSimulator.Run(maxSeconds)` ★
                // 它的 `maxSeconds` 是**步数上限**（`ceil(seconds / Dt)`），而本作在
                // `PhaseState.Shop` 期间**冻结 `RunTime`**（`RunSession.FixedTick` 里
                // `if (Phase == Shop) { TickShop; return; }`）⇒ Bot 逛店的每一步都**不推进游戏时间**
                // ⇒ 第 1 版跑满 108600 步只走到第 3 分钟（实测：RunTime 129.0s）✗
                // 所以这里按**游戏时间**收口：`while (session.RunTime < 1810f)`。
                // 循环体与 `RunSimulator.Run` 逐句同构（同一套 Bot 输入 → `ConsumeFrameEdges` → `FixedTick`），
                // 只是把"步数上限"换成"时间上限" + 一个 3e6 步的防挂死阀 ✓
                var session = new RunSession();
                session.Start(20261018UL);
                var bot = new RunBot(session, BotStrategy.Balanced);
                const float Dt = GameClock.FixedDt;
                const float TargetRunTime = 1810f;          // 1810 秒游戏时间（够到第 31 分钟）
                const int MaxSteps = 3000000;               // 阀：RunTime 卡住时也不会无限跑

                while (!session.IsOver && steps < MaxSteps && session.RunTime < TargetRunTime)
                {
                    // 不死处理：与 `DebugApi.ProbeLongRun` 同一招（每步顶满血量）
                    World w = session.World;
                    if (w != null && w.Player != null)
                    {
                        HealthPool hp = w.Player.Health;
                        if (hp.MaxHp < 1e6f) { hp.MaxHp = 1e7f; hp.Hp = 1e7f; hp.MaxShield = 0f; hp.Shield = 0f; }
                    }
                    if (session.RunTime > bestRunTime) bestRunTime = session.RunTime;

                    int minute = Mathf.FloorToInt(session.RunTime / 60f) + 1;
                    if (minute != lastMinute && session.Progress != null)
                    {
                        lastMinute = minute;
                        StatBlock st = session.Progress.Stats;
                        float melee = st.Get(StatId.MeleeDamage);
                        float atkMult = st.Get(StatId.AtkMult);
                        float critRate = Mathf.Clamp01(st.Get(StatId.CritRate));
                        float critDmg = st.Get(StatId.CritDmg);
                        float atkSpeed = Mathf.Max(0.05f, st.Get(StatId.AtkSpeed));
                        float perHit = DamageResolver.WeaponDamage(t.tools.hoe.baseValue, melee, atkMult, false,
                                                                   critDmg, t.formula.minDamage);
                        float perCrit = DamageResolver.WeaponDamage(t.tools.hoe.baseValue, melee, atkMult, true,
                                                                   critDmg, t.formula.minDamage);
                        rows.Add(new Grow
                        {
                            Minute = minute,
                            Level = session.Progress.Level,
                            Kills = session.World != null ? session.World.KillCount : 0,
                            Gold = session.Gold,
                            MaxHp = st.Get(StatId.MaxHp),
                            Melee = melee, AtkMult = atkMult, CritRate = critRate,
                            CritDmg = critDmg, AtkSpeed = atkSpeed,
                            PerHit = perHit, PerHitCrit = perCrit,
                            Expected = perHit * (1f - critRate) + perCrit * critRate,
                            Interval = t.tools.hoe.cooldown / atkSpeed
                        });
                    }

                    FrameInput input = bot.Tick(Dt);
                    session.ConsumeFrameEdges(ref input);
                    session.FixedTick(Dt, input);
                    steps++;
                }

                endInfo = $"跑局：走到第 {Mathf.FloorToInt(bestRunTime / 60f) + 1} 分钟（RunTime {bestRunTime:0.0}s）" +
                          $"｜等级 {session.Progress.Level}｜击杀 {session.World.KillCount}｜金币 {session.Gold}" +
                          $"｜步数 {steps}（其中大部分是**商店暂停**的帧，不推进游戏时间）｜IsOver={session.IsOver}";
                session.Dispose();
            }
            catch (Exception e)
            {
                sb.AppendLine("  跑局 CRASH " + e.GetType().Name + ": " + e.Message);
                sb.AppendLine(e.StackTrace ?? "");
            }
            float cost = Time.realtimeSinceStartup - started;
            sb.AppendLine($"  {endInfo}（耗时 {cost:0.0}s）");
            sb.AppendLine($"  锄头：基础 {t.tools.hoe.baseValue:0.##}｜冷却 {t.tools.hoe.cooldown:0.##}s｜扇形 {t.tools.hoe.arc:0}°" +
                          $"｜一刀最多打 {t.tools.hoe.hits} 个目标｜下限伤害 {t.formula.minDamage:0.##}");
            sb.AppendLine("  每击 = (基础 4 + 近战伤害) × 伤害倍率（暴击再 × 暴击伤害）；期望/击 = 每击×(1−暴击率) + 暴击×暴击率");
            sb.AppendLine("  挥击间隔 = 冷却 ÷ 攻击速度；单手 DPS = 期望/击 ÷ 间隔（对**单个目标**、不含剑气/爆炸/DoT）");
            sb.AppendLine("   分钟｜等级｜最大生命｜近战｜伤害倍率｜暴击率｜暴击伤害｜攻速｜间隔s｜每击｜暴击｜期望/击｜挥/秒｜单手DPS｜击杀｜金币");
            for (int i = 0; i < rows.Count; i++)
            {
                Grow g = rows[i];
                sb.AppendLine($"  {g.Minute,5}｜{g.Level,4}｜{g.MaxHp,8:0.#}｜{g.Melee,4:0.##}｜{g.AtkMult,8:0.000}｜" +
                              $"{g.CritRate * 100f,5:0.0}%｜{g.CritDmg,8:0.000}｜{g.AtkSpeed,4:0.00}｜{g.Interval,5:0.000}｜" +
                              $"{g.PerHit,4:0.##}｜{g.PerHitCrit,4:0.##}｜{g.Expected,6:0.##}｜{g.SwingsPerSec,5:0.00}｜" +
                              $"{g.Dps,7:0.##}｜{g.Kills,4}｜{g.Gold,5}");
            }
            if (rows.Count == 0) sb.AppendLine("  （没有采到任何分钟点）");

            log = sb.ToString();
            return rows;
        }

        private static Grow GrowAt(List<Grow> rows, int minute)
        {
            // 取"该分钟开始时的状态"；没有就取最接近的**前一个**（不允许看未来）
            Grow best = default;
            bool any = false;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Minute > minute) break;
                best = rows[i]; any = true;
            }
            if (!any && rows.Count > 0) return rows[0];
            if (!any) best.Minute = 0;
            return best;
        }

        // ══════════════════════════════════════════════════════════════
        // ④ 敌人数值表
        // ══════════════════════════════════════════════════════════════

        private static void AppendEnemyTable(StringBuilder sb, List<Grow> grow)
        {
            sb.AppendLine();
            sb.AppendLine("────── ④ 敌人数值表（base + 曲线 + 1 / 10 / 20 / 30 分）──────");
            sb.AppendLine("  血量 = baseHp × WaveGrowth × 前段压低(非 BOSS) × post20；BOSS 另乘 bossGrowth(index=1 ⇒ ×1)");
            sb.AppendLine("  整数血点 = CeilToInt(血量) —— 打完一只怪必须打掉的整数点数");
            sb.AppendLine("  「几刀」= CeilToInt(血量 ÷ 该分钟玩家的每击)；每击取自 ③ 的跑局采样（没有样本就印 —）");

            for (int i = 0; i < TableDatabase.Enemies.Length; i++)
            {
                EnemyDef d = TableDatabase.Enemies[i];
                sb.AppendLine();
                sb.AppendLine($"  ◆ {d.Id}（{d.Name}／{d.Kind}）");
                sb.AppendLine($"    baseHp {d.BaseHp,8:0.##}｜移速 {d.MoveSpeed,6:0.##}｜半径 {d.Radius,5:0.##}" +
                              $"｜金币 {d.Gold,4}｜经验 {d.Exp,4}");
                sb.AppendLine($"    三招 base：触碰 {d.TouchDamage,7:0.####}（冷却 {d.TouchCooldown:0.##}s）" +
                              $"｜弹幕 {d.ProjectileDamage,7:0.####}（射速 {d.ProjectileSpeed:0.##}／间隔 {d.AttackInterval:0.##}s）" +
                              $"｜跳砸 {d.JumpDamage,7:0.####}（半径 {d.JumpRadius:0.##}／冷却 {d.JumpCooldown:0.##}s）");
                sb.AppendLine($"    射程：攻击 {d.AttackRange:0.##}／安全 {d.SafeRange:0.##}／跳 {d.JumpRange:0.##}｜跳前摇 {d.JumpWindup:0.##}s");
                d.HpCurveCoefficients(out float a, out float b);
                d.DmgCurveCoefficients(out float da, out float db);
                sb.AppendLine($"    HP 曲线 a={a:0.######} b={b:0.######}（第 20 分 {EnemyDef.WaveGrowth(20, a, b):0.###}×）" +
                              $"｜伤害曲线 a={da:0.######} b={db:0.######}（第 20 分 {EnemyDef.WaveGrowth(20, da, db):0.###}×）" +
                              $"｜前段压低 {(d.Kind == EnemyKind.Boss ? "不吃" : $"{EnemyDef.EarlyNerfFactor(1):0.00}→1.00（1→8 分）")}" +
                              $"｜post20（第 30 分）{EnemyDef.Post20Factor(30):0.###}×");

                int[] mins = { 1, 10, 20, 30 };
                sb.AppendLine("    分钟｜曲线倍率｜   血量   ｜整数血点｜该分每击｜几刀死｜ 三招血点(触碰/弹幕/跳砸)");
                for (int k = 0; k < mins.Length; k++)
                {
                    int m = mins[k];
                    float hp = d.HpForWave(m, 1);
                    int whole = Mathf.CeilToInt(hp);
                    Grow g = GrowAt(grow, m);
                    bool has = g.Minute == m || (g.Minute > 0 && g.Minute < m);
                    string perHit = g.PerHit > 0f ? g.PerHit.ToString("0.##") : "—";
                    string hits = g.PerHit > 0f ? Mathf.CeilToInt(hp / g.PerHit).ToString() : "—";
                    sb.AppendLine($"    {m,4}｜{d.HpMultiplierForWave(m),8:0.000}｜{hp,9:0.00}｜{whole,7}｜{perHit,8}｜{hits,6}｜" +
                                  $" {d.TouchDamagePoints(m),3}/{d.ProjectileDamagePoints(m),3}/{d.JumpDamagePoints(m),3}");
                }
            }

            // 伤害侧回归：本轮**一个数没改**，只做读数
            TuningDef t = TableDatabase.Tuning;
            int php = Mathf.Max(1, Mathf.RoundToInt(t.player.maxHp));
            sb.AppendLine();
            sb.AppendLine($"  ◆ 伤害侧回归（玩家 {php} 血 / {t.player.shield:0.#} 盾 —— 三招 base、伤害曲线**一个数没改**）");
            sb.AppendLine("    分钟｜基础兵触碰｜远程弹幕｜精英跳砸｜BOSS 跳砸");
            int[] mm = { 1, 10, 20, 30 };
            for (int k = 0; k < mm.Length; k++)
            {
                int m = mm[k];
                EnemyDef gr = TableDatabase.GetEnemy("grunt");
                EnemyDef sh = TableDatabase.GetEnemy("shooter");
                EnemyDef jp = TableDatabase.GetEnemy("jumper");
                EnemyDef bo = TableDatabase.GetEnemy("boss");
                sb.AppendLine($"    {m,4}｜{Downs(gr.TouchDamagePoints(m), php),10}｜{Downs(sh.ProjectileDamagePoints(m), php),8}｜" +
                              $"{Downs(jp.JumpDamagePoints(m), php),10}｜{Downs(bo.JumpDamagePoints(m), php),10}");
            }
        }

        private static string Downs(int points, int hp)
        {
            if (points <= 0) return "—";
            return $"{points} 点×{Mathf.CeilToInt(hp / (float)points)} 下";
        }

        // ══════════════════════════════════════════════════════════════
        // ⑤ 商品数值表
        // ══════════════════════════════════════════════════════════════

        private static readonly int[] PriceMinutes = { 1, 5, 10, 15, 20, 25, 30 };
        private static readonly string[] TierNames = { "D", "C", "B", "A", "S" };

        private static void AppendShopTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ⑤ 商品数值表（实付价 = 基础价 × 档位倍率 × 波次倍率 W）──────");
            sb.Append("  波次倍率 W：");
            for (int i = 0; i < PriceMinutes.Length; i++)
                sb.Append($"{PriceMinutes[i]}分 {t.shop.WavePriceMult(PriceMinutes[i]):0.000}｜");
            sb.AppendLine();
            sb.Append("  档位倍率  ：");
            for (int g = 0; g < 5; g++) sb.Append($"{TierNames[g]} ×{t.quality.PriceMult(g):0.00}｜");
            sb.AppendLine();

            AppendKindTable(sb, "种子（`SeedDef.Price` → `SeedSlot.ShopPriceFrom`）", 0);
            AppendKindTable(sb, "道具（`ItemDef.Price` → `SeedSlot.ShopPriceFrom`）", 1);
            AppendKindTable(sb, "插件（`PluginAsset.basePrice` → `SeedSlot.ShopPriceFrom`）", 2);

            sb.AppendLine("  ── 各档基础价区间（表内实测，不是估的）──");
            for (int g = 0; g < 5; g++)
            {
                RangeOf(g, out int smin, out int smax, out int ns, out int imin, out int imax, out int ni,
                        out int pmin, out int pmax, out int np);
                sb.AppendLine($"    {TierNames[g]}：种子 {(ns > 0 ? $"{smin}（{ns} 种）" : "无")}" +
                              $"｜道具 {(ni > 0 ? $"{imin}（{ni} 件）" : "无")}" +
                              $"｜插件 {(np > 0 ? $"{pmin}（{np} 件）" : "无")}");
            }
        }

        private static void AppendKindTable(StringBuilder sb, string title, int kind)
        {
            sb.AppendLine();
            sb.AppendLine($"  ◆ {title}");
            sb.Append("    档位｜");
            for (int i = 0; i < PriceMinutes.Length; i++) sb.Append($"{PriceMinutes[i]}分".PadLeft(7)).Append('｜');
            sb.AppendLine();
            for (int g = 0; g < 5; g++)
            {
                RangeOf(g, out int smin, out _, out int ns, out int imin, out _, out int ni,
                        out int pmin, out _, out int np);
                int basePrice;
                switch (kind)
                {
                    case 0 when ns > 0: basePrice = smin; break;
                    case 1 when ni > 0: basePrice = imin; break;
                    case 2 when np > 0: basePrice = pmin; break;
                    default: continue;      // 该档这一类没有货：整行不印（不是 0 价）
                }
                sb.Append("    ").Append(TierNames[g].PadRight(4)).Append('｜');
                for (int i = 0; i < PriceMinutes.Length; i++)
                    sb.Append(SeedSlot.ShopPriceFrom(basePrice, g, PriceMinutes[i]).ToString().PadLeft(7)).Append('｜');
                sb.Append($"  （基础 {basePrice}）");
                sb.AppendLine();
            }
        }

        private static void RangeOf(int grade, out int smin, out int smax, out int ns,
                                    out int imin, out int imax, out int ni,
                                    out int pmin, out int pmax, out int np)
        {
            smin = int.MaxValue; smax = int.MinValue; ns = 0;
            imin = int.MaxValue; imax = int.MinValue; ni = 0;
            pmin = int.MaxValue; pmax = int.MinValue; np = 0;
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                if (d.RarityTier != grade) continue;
                smin = Mathf.Min(smin, d.Price); smax = Mathf.Max(smax, d.Price); ns++;
            }
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef d = TableDatabase.Items[i];
                if (d.RarityTier != grade) continue;
                imin = Mathf.Min(imin, d.Price); imax = Mathf.Max(imax, d.Price); ni++;
            }
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
            {
                PluginAsset d = TableDatabase.Plugins[i];
                if (d == null || (int)d.quality != grade) continue;
                pmin = Mathf.Min(pmin, d.basePrice); pmax = Mathf.Max(pmax, d.basePrice); np++;
            }
        }

        // ══════════════════════════════════════════════════════════════
        // ⑥ 刷新 / 重抽价
        // ══════════════════════════════════════════════════════════════

        private static void AppendRerollTable(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ⑥ 刷新价 / 抽卡重抽价（商店与抽卡共用 `RerollPrice`）──────");
            sb.AppendLine($"  shop.rerollBase = {t.shop.rerollBase}｜levelReward.rerollBasePrice = {t.levelReward.rerollBasePrice}" +
                          $"｜rerollWaveStep = {t.shop.rerollWaveStep:0.####}｜post20PriceSlope = {t.shop.post20PriceSlope:0.####}" +
                          $"｜倍数 = 1.5^次数");
            sb.AppendLine("    分钟｜波次倍率｜基础值｜首次｜第2次｜第3次｜第4次｜第5次｜第6次");
            int[] mins = { 1, 5, 10, 15, 20, 25, 30 };
            for (int i = 0; i < mins.Length; i++)
            {
                int m = mins[i];
                int basis = ShopSystem.RerollBaseAt(t, m);
                sb.Append($"    {m,4}｜{t.shop.RerollWaveMult(m),8:0.000}｜{basis,6}｜");
                for (int n = 0; n < 6; n++)
                    sb.Append(ShopSystem.RerollPrice(t, t.shop.rerollBase, m, n, 0).ToString().PadLeft(6)).Append('｜');
                sb.AppendLine();
            }
            sb.AppendLine($"  抽卡重抽 = 同一个函数、基础价 `levelReward.rerollBasePrice` = {t.levelReward.rerollBasePrice}" +
                          $"（见 `RunSession.RewardRerollCost`）⇒ 与商店刷新价逐位同一条曲线 ✓");
        }

        // ══════════════════════════════════════════════════════════════
        // ⑦ 商品价硬检查
        // ══════════════════════════════════════════════════════════════

        private static void AppendPriceChecks(StringBuilder sb)
        {
            TuningDef t = TableDatabase.Tuning;
            sb.AppendLine();
            sb.AppendLine("────── ⑦ 商品价硬检查（判据：30 分钟传奇 ≤ 2000、无人 ≥ 10000）──────");

            int maxAll = 0; string maxWhere = "（无）";
            int maxS = 0; string maxSWhere = "（无）";
            int count10k = 0;
            var tenk = new List<string>();

            void Note(string what, int price, int minute, int tier, ref int best, ref string where)
            {
                if (price > best) { best = price; where = $"{what}@{minute}分"; }
                if (price >= 10000) { count10k++; if (tenk.Count < 8) tenk.Add($"{what}@{minute}分={price}"); }
                _ = tier;
            }

            for (int m = 1; m <= 30; m++)
            {
                for (int i = 0; i < TableDatabase.Seeds.Length; i++)
                {
                    SeedDef d = TableDatabase.Seeds[i];
                    int p = SeedSlot.ShopPriceFrom(d.Price, d.RarityTier, m);
                    Note($"种子 {d.Id}", p, m, d.RarityTier, ref maxAll, ref maxWhere);
                    if (d.RarityTier >= 4) Note($"种子 {d.Id}", p, m, 4, ref maxS, ref maxSWhere);
                }
                for (int i = 0; i < TableDatabase.Items.Length; i++)
                {
                    ItemDef d = TableDatabase.Items[i];
                    int p = SeedSlot.ShopPriceFrom(d.Price, d.RarityTier, m);
                    Note($"道具 {d.Id}", p, m, d.RarityTier, ref maxAll, ref maxWhere);
                    if (d.RarityTier >= 4) Note($"道具 {d.Id}", p, m, 4, ref maxS, ref maxSWhere);
                }
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                {
                    PluginAsset d = TableDatabase.Plugins[i];
                    if (d == null) continue;
                    int p = SeedSlot.ShopPriceFrom(d.basePrice, (int)d.quality, m);
                    Note($"插件 {d.id}", p, m, (int)d.quality, ref maxAll, ref maxWhere);
                    if ((int)d.quality >= 4) Note($"插件 {d.id}", p, m, 4, ref maxS, ref maxSWhere);
                }
            }

            sb.AppendLine($"  【最高价】{maxAll}（{maxWhere}）⇒ ≤ 2000 ？**{(maxAll <= 2000 ? "是 ✓" : "否 ✗")}**");
            sb.AppendLine($"  【S 档最高】{maxS}（{maxSWhere}）⇒ ≤ 2000 ？**{(maxS <= 2000 ? "是 ✓" : "否 ✗")}**");
            sb.AppendLine($"  【有人 ≥ 10000 ？】**{(count10k > 0 ? $"有 {count10k} 项 ✗" : "没有 ✓")}**");
            for (int i = 0; i < tenk.Count; i++) sb.AppendLine($"      {tenk[i]}");
            sb.AppendLine($"  【样本量】种子 {TableDatabase.Seeds.Length} 种｜道具 {TableDatabase.Items.Length} 件｜插件 {TableDatabase.Plugins.Length} 件" +
                          $" × 30 个分钟点，**全遍历**（不是抽样）");
            sb.AppendLine($"  【表格对照】第 1 / 30 分钟：S 档 {SeedSlot.ShopPriceFrom(80, 4, 1)} / {SeedSlot.ShopPriceFrom(80, 4, 30)}" +
                          $"｜D 档 {SeedSlot.ShopPriceFrom(20, 0, 1)} / {SeedSlot.ShopPriceFrom(20, 0, 30)}" +
                          $"｜刷新首次 {ShopSystem.RerollPrice(t, t.shop.rerollBase, 1, 0, 0)} / {ShopSystem.RerollPrice(t, t.shop.rerollBase, 30, 0, 0)}");
        }
    }
}
