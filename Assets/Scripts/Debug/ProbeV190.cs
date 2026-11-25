using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v190：本轮四项改动的**只读探针**（每项一张可复读的读数表）★
    ///
    /// ── 为什么是**一个新文件** ──
    /// 与 `ProbeV189` 同一条纪律：探针只往新文件里加，不去改 `DebugApi.cs` 那 6700 行
    /// （本工程有并行改动线在反复追加那个文件，"谁最后写谁赢"会丢改动）✓
    /// `DebugApi.cs` 那边**一行都不用动**：桥可以直接按组件名调到本类 ✓
    ///
    /// ── 它怎么被调到 ──
    /// `[RuntimeInitializeOnLoadMethod]` 在 play 模式自动建一个 `ProbeV190` 对象
    /// （与 `DebugApi.AutoCreate` / `ProbeV189.AutoCreate` 同一个套路），之后桥：
    ///   `send ProbeV190 ProbeV190 ProbeCurve`
    ///   `send ProbeV190 ProbeV190 ProbeReward`
    ///   `send ProbeV190 ProbeV190 ProbeWater`
    ///   `send ProbeV190 ProbeV190 ProbeAilment`
    /// 四个方法都**只读**：只建临时 `World` / `RunSession` 并在结束时还原被临时改过的表值 ✓
    ///
    /// ── 读数为什么必须是"真的走一遍" ──
    ///   ① 曲线：走 `EnemyDef.HpForWave` / `TouchDamageForWave`（**真消费点**），
    ///      并额外算一次"假如精英走标准曲线是多少" ⇒ 证明 `eliteHpStepA/B` **真的被读**（不是死配置）
    ///   ② 抽卡：走 `RunSession.PickRewardCard`（**真发卡路径**），读前/读后各取一次 `StatBlock`
    ///   ③ 耗水：走 `World.Simulate`（**真世界推进**），只改一个数（`farm.waterFactor`）做 A/B，
    ///      同一个世界、同一块地、同一株作物 ⇒ 差异只可能来自那个系数
    ///   ④ 异常：走 `StatusRuntime.Apply` + `Tick`（**真结算路径**），结算次数是**数出来的**，
    ///      不是把配置读回来念一遍 ✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV190 : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV190>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV190");
            go.AddComponent<ProbeV190>();
        }

        private static string Emit(StringBuilder sb)
        {
            string text = sb.ToString();
            Log.Info(text);
            return text;
        }

        /// <summary>与 `SelfCheck.MakeWorld` 逐字同一套最小世界（玩家 + 空敌人表）。</summary>
        private static World MakeWorld(TuningDef t)
        {
            var w = new World(t, 12345UL);
            var p = new PlayerActor
            {
                Id = w.NextActorId(),
                Team = Team.Player,
                Progress = new PlayerProgress(t),
                Pos = Vector2.zero,
                Radius = 0.5f
            };
            p.Health.Setup(p.Progress.Stats.Get(StatId.MaxHp), p.Progress.Stats.Get(StatId.Shield));
            w.Player = p;
            return w;
        }

        // ════════════════════════════════════════════════════════════════
        // ① 敌人成长曲线（三项目标：血 80 / 200，伤害 6 / 4 / 8）
        // ════════════════════════════════════════════════════════════════

        /// <summary>每击基准伤害 = 玩家 t=0 的近战单击（锄头 baseValue 4 + meleeDamage 0）× atkMult 1.0。</summary>
        private static float PerHit(TuningDef t) =>
            (t.tools.hoe.baseValue + t.player.meleeDamage) * Mathf.Max(0.0001f, t.player.atkMult);

        public string ProbeCurve(string spec)
        {
            try { return ProbeCurveCore(); }
            catch (System.Exception ex)
            {
                // 探针自己报错：桥只回一句 NullReferenceException 是查不出行的 ⇒ 把堆栈也打出来
                Log.Error("PROBE190-① CRASH " + ex);
                return "PROBE190-① CRASH：" + ex.GetType().Name + " @ " + ex.StackTrace;
            }
        }

        private string ProbeCurveCore()
        {
            TuningDef t = TableDatabase.Tuning;
            EnemyGrowthDef g = TableDatabase.EnemyGrowth;
            var sb = new StringBuilder();
            float perHit = PerHit(t);
            sb.Append("═══ PROBE190-① 敌人成长曲线 ═══\n");
            sb.Append($"公式 WaveGrowth(w,a,b) = 1 + a·t + b·t²（t = 分钟−1 ⇒ 第 20 分钟 t=19）｜玩家每击 {perHit:0.##}\n");
            sb.Append($"标准 HP   a={g.hpStepA:0.######} b={g.hpStepB:0.######} ⇒ 20 分倍率 {EnemyDef.WaveGrowth(20, g.hpStepA, g.hpStepB):0.#####}\n");
            sb.Append($"精英 HP   a={g.eliteHpStepA:0.######} b={g.eliteHpStepB:0.######} ⇒ 20 分倍率 {EnemyDef.WaveGrowth(20, g.eliteHpStepA, g.eliteHpStepB):0.#####}\n");
            sb.Append($"伤害      a={g.dmgStepA:0.######} b={g.dmgStepB:0.######} ⇒ 20 分倍率 {EnemyDef.WaveGrowth(20, g.dmgStepA, g.dmgStepB):0.#####}\n");

            // ── 精英曲线"真的被读"的差分证明 ──
            //   同一只 jumper：真值（走 def） vs 标准曲线算出来的值。两者必须不同。
            EnemyDef jumper = TableDatabase.GetEnemy("jumper");
            jumper.HpCurveCoefficients(out float ja, out float jb);
            float jReal = jumper.HpForWave(20, 1);
            float jIfStd = jumper.BaseHp * EnemyDef.WaveGrowth(20, g.hpStepA, g.hpStepB);
            sb.Append($"【精英键差分】jumper 第 20 分钟：真实 {jReal:0.###}｜若走标准曲线 {jIfStd:0.###}｜" +
                      $"用的是({ja:0.######},{jb:0.######})｜UsesEliteHpCurve={jumper.UsesEliteHpCurve}\n");
            sb.Append($"【精英键差分】grunt 同参数下 HpForWave 与标准曲线一致 = " +
                      $"{(Mathf.Abs(TableDatabase.GetEnemy("grunt").HpForWave(20, 1) - TableDatabase.GetEnemy("grunt").BaseHp * EnemyDef.WaveGrowth(20, g.hpStepA, g.hpStepB)) < 1e-4f)}\n");

            // ── 主表：血量 + 三招伤害 + 几刀死 ──
            EnemyDef grunt = TableDatabase.GetEnemy("grunt");
            EnemyDef shooter = TableDatabase.GetEnemy("shooter");
            EnemyDef boss = TableDatabase.GetEnemy("boss");
            sb.Append("\n分钟│ grunt 血 │ grunt 碰撞 │ shooter 子弹 │ jumper 血 │ jumper 跳砸 │ grunt 几刀│ jumper 几刀\n");
            int[] minutes = { 1, 5, 10, 15, 20 };
            for (int i = 0; i < minutes.Length; i++)
            {
                int m = minutes[i];
                float gh = grunt.HpForWave(m, 1);
                float jh = jumper.HpForWave(m, 1);
                sb.Append($"{m,4}│{gh,9:0.00}│{grunt.TouchDamageForWave(m),11:0.00}│{shooter.ProjectileDamageForWave(m),13:0.00}" +
                          $"│{jh,9:0.00}│{jumper.JumpDamageForWave(m),11:0.00}│{Mathf.CeilToInt(gh / perHit),9}│{Mathf.CeilToInt(jh / perHit),10}\n");
            }

            // ── 判据行：用户给的六个绝对值必须逐位命中 ──
            sb.Append($"\n【判据】20 分基础兵血 = {grunt.HpForWave(20, 1):0.####}（目标 80）｜跳跃者血 = {jumper.HpForWave(20, 1):0.####}（目标 200）\n");
            sb.Append($"【判据】20 分碰撞 = {grunt.TouchDamageForWave(20):0.####}（目标 6）｜子弹 = {shooter.ProjectileDamageForWave(20):0.####}（目标 4）｜跳砸 = {jumper.JumpDamageForWave(20):0.####}（目标 8）\n");
            sb.Append($"【判据·开局一刀死】第 1 分钟基础兵血 {grunt.HpForWave(1, 1):0.####} ≤ 每击 {perHit:0.##} ⇒ 第 {Mathf.CeilToInt(grunt.HpForWave(1, 1) / perHit)} 刀死\n");
            // ── BOSS：自己的两条曲线（血 + 三招），判据是"25 血能挨几下" ──            //   BOSS 改前（吃标准旧曲线 a=0.12/b=0.006 ⇒ 20 分 3.603×）：血 1634 / 碰撞 3.03 / 子弹 2.02 / 跳砸 6.31
            boss.HpCurveCoefficients(out float ba, out float bb);
            boss.DmgCurveCoefficients(out float bda, out float bdb);
            float bossIfStdHp = boss.BaseHp * EnemyDef.WaveGrowth(20, g.hpStepA, g.hpStepB);
            float bossIfStdJump = boss.JumpDamage * EnemyDef.WaveGrowth(20, g.dmgStepA, g.dmgStepB);
            sb.Append($"\n【BOSS键差分·血】20 分真实 {boss.HpForWave(20, 1):0.#}｜若走标准曲线 {bossIfStdHp:0.#}｜用的是({ba:0.#######},{bb:0.########})｜UsesBossHpCurve={boss.UsesBossHpCurve}\n");
            sb.Append($"【BOSS键差分·伤害】20 分跳砸真实 {boss.JumpDamageForWave(20):0.##}｜若走标准曲线 {bossIfStdJump:0.##}｜用的是({bda:0.#######},{bdb:0.########})｜UsesBossDmgCurve={boss.UsesBossDmgCurve}\n");
            const float BossHpRef = 25f;      // 用户判据："玩家 25 血时能挨 2~3 下（跳砸）"
            sb.Append("\n分钟│ BOSS 血 │ BOSS 碰撞 │ BOSS 子弹 │ BOSS 跳砸 │ 25血挨跳砸(第N下死/挨几下不死)│ 碰撞几下半│ 子弹几下半\n");
            for (int i = 0; i < minutes.Length; i++)
            {
                int m = minutes[i];
                float bh = boss.HpForWave(m, 1);
                float bt2 = boss.TouchDamageForWave(m);
                float bp = boss.ProjectileDamageForWave(m);
                float bj = boss.JumpDamageForWave(m);
                int jumpHits = Mathf.CeilToInt(BossHpRef / Mathf.Max(0.0001f, bj));
                sb.Append($"{m,4}│{bh,9:0.0}│{bt2,10:0.00}│{bp,10:0.00}│{bj,10:0.00}│{jumpHits,12}次死/{jumpHits - 1}下不死│{Mathf.CeilToInt(BossHpRef / Mathf.Max(0.0001f, bt2)),11}│{Mathf.CeilToInt(BossHpRef / Mathf.Max(0.0001f, bp)),11}\n");
            }
            sb.Append($"【判据·BOSS 血】20 分 = {boss.HpForWave(20, 1):0}（目标带 3000~3500｜改前 1634 ⇒ {(boss.HpForWave(20, 1) / 1634f):0.00}× 改前）\n");
            sb.Append($"【判据·BOSS 三招】20 分 碰撞 {boss.TouchDamageForWave(20):0.00}（带 4~5）｜子弹 {boss.ProjectileDamageForWave(20):0.00}（带 2.5~3）｜跳砸 {boss.JumpDamageForWave(20):0.00}（带 8~10）\n");
            float bj20 = boss.JumpDamageForWave(20);
            int bjHits = Mathf.CeilToInt(BossHpRef / Mathf.Max(0.0001f, bj20));
            sb.Append($"【判据·25 血挨跳砸】{BossHpRef:0} / {bj20:0.00} = {BossHpRef / bj20:0.00} ⇒ 第 {bjHits} 下死（挨 {bjHits - 1} 下还剩 {BossHpRef - (bjHits - 1) * bj20:0.00} 血）\n");
            return Emit(sb);
        }

        // ════════════════════════════════════════════════════════════════
        // ② 抽卡五项（真发卡路径）
        // ════════════════════════════════════════════════════════════════

        public string ProbeReward(string spec)
        {
            TuningDef t = TableDatabase.Tuning;
            var sb = new StringBuilder();
            sb.Append("═══ PROBE190-② 抽卡五项（走 RunSession.PickRewardCard）═══\n");
            RewardStatDef[] stats = t.levelReward.stats;
            sb.Append($"奖励池 {stats.Length} 项：");
            for (int i = 0; i < stats.Length; i++)
            {
                string how = stats[i].mode == "Percent"
                    ? " " + stats[i].amount + "%"
                    : " " + stats[i].amount;
                sb.Append(i == 0 ? "" : "／").Append(stats[i].id).Append("(").Append(stats[i].mode).Append(how).Append(")");
            }
            sb.Append('\n');

            var s = new RunSession();
            s.Start(20240607UL);
            StatBlock st = s.Progress.Stats;
            sb.Append("\n属性          │      发卡前 │      发卡后 │ 增量\n");
            string[] wanted = { "MaxHp", "Shield", "BlastDamage", "ExplodeRange", "CritDmg" };
            for (int k = 0; k < wanted.Length; k++)
            {
                RewardStatDef def = null;
                for (int i = 0; i < stats.Length; i++)
                    if (stats[i].id == wanted[k]) { def = stats[i]; break; }
                if (def == null) { sb.Append($"{wanted[k],-12}│ 表里没有这张卡 ✗\n"); continue; }
                StatIds.TryParse(def.id, out StatId id);
                float before = st.Get(id);
                s.RewardCards.Clear();
                s.RewardCards.Add(new RunSession.LevelRewardCard { Def = def, Amount = def.amount, Copies = 1 });
                bool ok = s.PickRewardCard(0);
                float after = st.Get(id);
                sb.Append($"{def.name}({def.id})│{before,11:0.####}│{after,11:0.####}│{after - before,+8:0.####}  mode={def.mode} amount={def.amount} pick={ok}\n");
            }

            // 爆炸范围的真实消费点：半径 = BlastBaseRadius(baseExplodeRange) × Stats.Get(ExplodeRange)
            float radius = RunSession.BlastBaseRadius(t.tools.fertilizer.baseExplodeRange) * st.Get(StatId.ExplodeRange);
            sb.Append($"\n【判据·ExplodeRange 有消费点】当前爆炸半径 = BlastBaseRadius({t.tools.fertilizer.baseExplodeRange}) × {st.Get(StatId.ExplodeRange):0.###} = {radius:0.###} 米\n");
            sb.Append($"【判据·CritDmg】{st.Get(StatId.CritDmg) * 100f:0.##}%（基准 critDmg {t.player.critDmg * 100f:0.##}% ⇒ Percent 模式是乘法 (1+3/100)，不是 +3 个百分点）\n");
            sb.Append($"【判据】IsFractionStat(CritDmg) = {TableDatabase.IsFractionStat(StatId.CritDmg)}（只有 CritRate 是 0..1 小数属性）\n");
            s.Dispose();
            return Emit(sb);
        }

        // ════════════════════════════════════════════════════════════════
        // ③ 耕地耗水（A/B 只差 farm.waterFactor 一个数）
        // ════════════════════════════════════════════════════════════════

        /// <summary>0 → 空 要多少秒：同一个 World、第 0 块地、同一株作物，只改 `farm.waterFactor`。</summary>
        private static float SecondsToEmpty(TuningDef t, float factor)
        {
            float saved = t.farm.waterFactor;
            t.farm.waterFactor = factor;
            World w = MakeWorld(t);
            // 作物：取 D 档真实参数（waterDrain 6、蓄水 100），只把成熟时间拉长 ——
            // 否则地块会在水耗尽前先成熟（成熟后 TickGrowth2 直接返回 ⇒ 量不到"耗完"）✓
            var crop = new SeedDef
            {
                Id = "probe_water", Name = "探针作物", GrowTime = 1e6f,
                Mechanic = PlantMechanics.None, WaterDrain = 6
            };
            FarmPlot plot = w.Farm.Slots[0];
            plot.BeginPlant(t.farm, crop, 100, 6, 1);
            const float dt = GameClock.FixedDt;
            int frames = 0;
            while (plot.Water > 0 && frames < 400000) { w.Simulate(dt); frames++; }
            t.farm.waterFactor = saved;      // ★ 还原：探针不许改表 ★
            return frames * dt;
        }

        public string ProbeWater(string spec)
        {
            TuningDef t = TableDatabase.Tuning;
            var sb = new StringBuilder();
            float factor = Mathf.Max(0.0001f, t.farm.waterFactor);
            float before = SecondsToEmpty(t, 1f);        // 改前口径 = 系数 1（= 老行为）
            float after = SecondsToEmpty(t, factor);     // 当前表值
            sb.Append("═══ PROBE190-③ 耕地耗水 0→100（World.Simulate 真推进，1/60 步）═══\n");
            sb.Append("作物参数：waterDrain 6 / 蓄水上限 100（= D 档真实参数，成熟时间拉到 1e6 以便量到「耗完」）\n");
            sb.Append($"farm.waterFactor = {t.farm.waterFactor}（表值）\n");
            sb.Append($"改前（系数 1）：{before:0.00} 秒 ｜ 改后（系数 {factor:0.##}）：{after:0.00} 秒 ｜ 比值 {before / Mathf.Max(0.001f, after):0.000}\n");
            sb.Append($"【判据】理论值：100 / 6 次 × 5 秒 = {100f / 6f * 5f:0.00} 秒 ⇒ 减半 {100f / 6f * 5f / 2f:0.00} 秒\n");
            // 水壶侧必须没被乘：补水/弹药这两个数不在水耗链路里
            sb.Append($"【没被乘的两处】waterPerHit = {t.farm.waterPerHit}（水壶命中补水量，未变）｜RunSession.WaterAmmoMax = {RunSession.WaterAmmoMax}（水壶弹药上限，未变）\n");
            return Emit(sb);
        }

        // ════════════════════════════════════════════════════════════════
        // ④ 灼烧 / 中毒：结算次数（数出来的）
        // ════════════════════════════════════════════════════════════════

        /// <summary>施加一次异常，按 1/60 步真结算到状态结束，数出结算次数与总伤害。</summary>
        private static string RunAilment(TuningDef t, AilmentKind kind, out int ticks, out float total, out float seconds)
        {
            FormulaDef f = t.formula;
            World w = MakeWorld(t);
            w.SpawnEnemy("grunt", new Vector2(5f, 0f), 1);
            EnemyActor e = w.Enemies[w.Enemies.Count - 1];
            e.Health.Setup(500f, 0f);                     // 血够厚 ⇒ 不会在测量中死掉
            // ★ 血量为什么取 500 而不是 1e5 ★
            //   总伤害是用 `hp0 − hp` **量**出来的，而 float 在 1e5 量级的分辨率约 0.0078
            //   ⇒ 用 1e5 量 1.35 的伤害会带上 ±0.008 的量化误差（实测读到 1.3594）✗
            //   500 的量级分辨率约 3e-5 ⇒ 读数回到 1.3500 ✓ 血量仍远大于毒/火总伤（≤1.62）✓
            float hp0 = e.Health.Hp;
            e.Status.Apply(kind, 3f, f, 1f);
            int leftPrev = kind == AilmentKind.Poison ? e.Status.PoisonTicksLeft : e.Status.BurnTicksLeft;
            ticks = 0;
            seconds = 0f;
            const float dt = GameClock.FixedDt;
            int frames = 0;
            bool Active() => kind == AilmentKind.Poison ? e.Status.PoisonTicksLeft > 0 : e.Status.BurnTicksLeft > 0;
            while (Active() && frames < 60000)
            {
                e.Status.Tick(dt, e, w);
                int left = kind == AilmentKind.Poison ? e.Status.PoisonTicksLeft : e.Status.BurnTicksLeft;
                if (left < leftPrev) { ticks++; leftPrev = left; }   // 每一次"结算"= TicksLeft 减 1
                frames++;
                seconds += dt;
            }
            total = hp0 - e.Health.Hp;
            float perTick = (kind == AilmentKind.Poison ? f.poisonTickFactor : f.burnTickFactor) * 3f;
            return $"{(kind == AilmentKind.Poison ? "毒" : "火")}：结算次数 {ticks}｜总伤害 {total:0.####}｜时长 {seconds:0.##}s｜" +
                   $"单跳 {perTick:0.####}×{ticks} = {perTick * ticks:0.####}｜配置 ailmentTickCount = {f.ailmentTickCount}，tickInterval = {f.tickInterval:0.##}s";
        }

        public string ProbeAilment(string spec)
        {
            TuningDef t = TableDatabase.Tuning;
            var sb = new StringBuilder();
            sb.Append("═══ PROBE190-④ 灼烧 / 中毒（StatusRuntime 真结算）═══\n");
            sb.Append(RunAilment(t, AilmentKind.Poison, out int pt, out float pd, out float ps)).Append('\n');
            sb.Append(RunAilment(t, AilmentKind.Burn, out int bt, out float bd, out float bs)).Append('\n');
            sb.Append($"【判据】毒 {pt} 次 / 火 {bt} 次（目标都是 3）｜总伤害 毒 {pd:0.####} / 火 {bd:0.####}｜时长 毒 {ps:0.##}s / 火 {bs:0.##}s\n");
            sb.Append("【口径说明】「解 B」：次数改成 3、间隔仍是 1 秒 ⇒ 总时长 3 秒 ✓ 减速（寒冷/冰冻）与 chillPerHit 本轮未动\n");
            return Emit(sb);
        }
    }
}
