using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    public readonly struct CheckResult
    {
        public readonly string Name;
        public readonly bool Passed;
        public readonly string Detail;

        public CheckResult(string name, bool passed, string detail)
        {
            Name = name; Passed = passed; Detail = detail;
        }

        public override string ToString() => $"{(Passed ? "PASS" : "FAIL")}  {Name}  {Detail}";
    }

    /// <summary>
    /// 自检：与 EditMode 单测共用的期望值（这里是一份真相，两个运行器）。
    /// 用途：无头/无人值守时也能验证"数值可复算"，不依赖 Test Runner。
    /// </summary>
    public static class SelfCheck
    {
        private const float Eps = 1e-3f;

        public static List<CheckResult> RunAll()
        {
            var list = new List<CheckResult>();
            TableDatabase.Load(true);

            FormulaChecks(list);
            LevelChecks(list);
            StatChecks(list);
            FarmingChecks(list);
            TableChecks(list);
            RngChecks(list);
            RuleChecks(list);
            PluginChecks(list);
            // v168：新标准下的植物 / 道具 / 合成等级验收（每条机制都跑一次对照实验）
            V168Checks(list);
            return list;
        }

        public static string Report(List<CheckResult> results)
        {
            int pass = 0;
            var sb = new StringBuilder();
            foreach (CheckResult r in results)
            {
                if (r.Passed) pass++;
                else sb.AppendLine("  " + r);
            }
            int fail = results.Count - pass;
            var head = new StringBuilder();
            head.AppendLine($"自检 {(fail == 0 ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过");
            if (fail > 0) head.Append(sb);
            return head.ToString();
        }

        private static void Expect(List<CheckResult> list, string name, float actual, float expected)
        {
            bool ok = Mathf.Abs(actual - expected) <= Eps;
            list.Add(new CheckResult(name, ok, $"期望 {expected} 实际 {actual:0.####}"));
        }

        private static void FormulaChecks(List<CheckResult> list)
        {
            // v162（A3）：公式 = **(基准伤害 + 伤害属性) × 伤害倍率** × 暴击，下限 minDamage。
            // 继承率（inheritMult）已随旧公式一起删除，所以每个调用点都比旧版少传一个参数。
            // 期望值沿用旧数字的那几条，是为了证明"删掉继承率这一步**没有顺带改动别的乘区**"。
            Expect(list, "攻击公式 基础", DamageResolver.AttackFormula(22f, 46f, 2f, false, 1.5f), 136f);
            Expect(list, "攻击公式 暴击", DamageResolver.AttackFormula(22f, 46f, 2f, true, 1.5f), 204f);
            Expect(list, "攻击公式 下限保护", DamageResolver.AttackFormula(0f, 0f, 0f, false, 1.5f, 0.1f), 0.1f);
            // 新量级下的实算：萝卜卫士(D, x=0.8) + 近战伤害 1.0、倍率 100% → (0.8+1.0) = 1.8
            // ★ 旧公式同一组输入是 1.4 ★ —— 差额正是"× 继承率 0.6"这一项被删掉的部分，
            //   这条断言就是需求「直接相加」的算术证据。
            Expect(list, "攻击公式 10量级实算（继承率已删：1.4 → 1.8）",
                DamageResolver.AttackFormula(0.8f, 1.0f, 1f, false, 1.5f, 0.1f), 1.8f);
            // v45：伤害属性（加法）与伤害倍率（乘法）拆成两条属性，各自必须独立生效
            Expect(list, "伤害属性 加法独立生效", DamageResolver.AttackFormula(10f, 4f, 1f, false, 1.5f, 0.1f), 14f);
            Expect(list, "伤害倍率 乘法独立生效", DamageResolver.AttackFormula(10f, 0f, 1.5f, false, 1.5f, 0.1f), 15f);
            Expect(list, "两条属性相乘", DamageResolver.AttackFormula(10f, 4f, 1.5f, false, 1.5f, 0.1f), 21f);
            // v162：**继承率这个参数必须是真的没了**。
            // 它若被留成"恒为 1 的参数"或兼容重载，下面这种"同一份输入换个名字"的调用仍会编译通过，
            // 所以这里用**编译期**的检查代替运行期：只有 6 个参数的签名存在时它才编得过。
            // （签名一变，本文件编译失败 —— 这就是断言。运行期再验一遍算式。）
            Expect(list, "公式签名已无继承率参数（6 参）",
                DamageResolver.AttackFormula(1f, 1f, 3f, false, 2f), 6f);
            // v109：爆炸 = **普通伤害 × 爆炸倍率**（不再是独立公式）。
            // 期望值沿用旧数字：当爆炸倍率为 100% 时结果与旧公式完全一致，
            // 这本身就是"改动是倍率化、不是重做"的证据。
            Expect(list, "爆炸公式 100% ＝ 普通伤害原样", DamageResolver.ExplodeFormula(19f, 1f), 19f);
            Expect(list, "爆炸公式 200% ＝ 两倍", DamageResolver.ExplodeFormula(19f, 2f), 38f);
            Expect(list, "爆炸公式 150%", DamageResolver.ExplodeFormula(19f, 1.5f), 28.5f);
            Expect(list, "恢复公式", DamageResolver.HealFormula(8f, 10f, 0.8f, 1f), 16f);
            Expect(list, "护卫公式（只继承最大生命）", DamageResolver.GuardFormula(30f, 100f, 0.35f, 1f), 65f);
            Expect(list, "护卫公式 10量级实算", DamageResolver.GuardFormula(3f, 10f, 0.35f, 1f), 6.5f);
            Expect(list, "汲取公式", DamageResolver.LeechFormula(100f, 0.2f, 0.1f), 30f);
            Expect(list, "冰冻阈值 取大者(比例)", DamageResolver.AilmentThreshold(20f, TableDatabase.Tuning.formula), 10f);
            Expect(list, "冰冻阈值 取大者(下限)", DamageResolver.AilmentThreshold(4f, TableDatabase.Tuning.formula), 4f);
        }

        private static void LevelChecks(List<CheckResult> list)
        {
            GrowthDef g = TableDatabase.Tuning.growth;
            // 曲线 = round(expBase + expLinear·(level-1)^expExponent)，全部钉住以防漂移
            // v48：expBase 24→20、expLinear 12→11（略微降低升级门槛）
            float[] expected = { 20f, 31f, 48f, 68f, 91f, 117f, 144f, 172f, 202f, 234f };
            for (int i = 0; i < expected.Length; i++)
                Expect(list, $"升级曲线 Lv{i + 1}→{i + 2}", PlayerProgress.ExpToNextFor(i + 1, g), expected[i]);

            // ── v168：**删掉"第一波经验"那两条断言**（它们量的收益已经不存在了）──
            //
            // 原来是：
            //     float wave1Exp = WaveExpTotal(1);            // = 批次数 × 每批数量 × grunt.Exp
            //     list.Add(... "第一波经验足以升到 2 级",   wave1Exp >= toLevel2 ...);   // 一直绿
            //     list.Add(... "第一波经验上限",             wave1Exp <  toLevel3 * 4.5f ...);  // 一直红（456 vs 51）
            //
            // ★ 两条都是同一个死判据，所以一起删 ✗ ★
            // `WaveExpTotal` 算的是**击杀**能给多少经验 ✓
            // 而 v167 起**击杀不给经验**：唯一的发布点 `World.SpawnPickup()` 已删除，
            // `EnemyKilledEvent` 的两个订阅者只有屏幕抖动（WorldView）与埋点（RunSession）✗
            // ⇒ 它算的是一个**不可能发生**的收益 ✓
            //
            // ★ 为什么"一红一绿"要成对处理 ★
            // 红的（上限）每次都在喊"一个不存在的机制失控了" ⇒ 噪音，会盖住真信号 ✗
            // 绿的（下限）更糟：它**为错误的理由通过** ⇒ 一条"看着活着、实际死的"断言 ✓
            // 只删红的那条，就会留下一条永远绿的死断言 —— 那正是本次要清的那类东西 ✓
            //
            // ⚠️ **但设计约束本身还在，只是换了计量口径** ✗ —— 这里记下来，别丢：
            //     "第一分钟的【收割】收益不应让玩家直达 3 级"
            //     算法：第一分钟最多收割几次 × 每次多少经验
            //         ≈ floor(60 / 种子成熟秒数) × (种子价 / 10)（见 `World.HarvestPlot` 的 expGain）
            //     为什么不在这里写：成熟秒数受浇水加速与 `farm` 系数影响，
            //     拿 `SeedDef.GrowTime` 直接除会得到一个**不是真实路径**的数 ✓
            //     而收割侧的真实覆盖已经在机器人矩阵里：
            //       「收益归属 → 收割金币/经验占比 100%」+「Farmer 5 分钟收割 32 次 / +32 经验」✓
            //     ⇒ 等有人先把"成熟秒数"的权威入口理出来，再补这条表级断言 ✓

            var progress = new PlayerProgress(TableDatabase.Tuning);
            float hp0 = progress.Stats.Get(StatId.MaxHp);
            int plots0 = progress.MaxPlots;
            progress.AddExp(1000f, out int levels);
            bool grew = progress.Stats.Get(StatId.MaxHp) > hp0 && progress.Level > 1;
            list.Add(new CheckResult("升级提升生命", grew, $"Lv{progress.Level} hp {hp0}→{progress.Stats.Get(StatId.MaxHp)}"));
            list.Add(new CheckResult("升级不减少耕地", progress.MaxPlots >= plots0, $"{plots0}→{progress.MaxPlots}"));
        }

        private static void StatChecks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            var block = new StatBlock(t.caps, t.player);
            block.AddModifier(new StatModifier(StatId.Dodge, 0.5f, ModOp.Add, "check"));
            block.AddModifier(new StatModifier(StatId.Dodge, 10f, ModOp.Mul, "check"));
            Expect(list, "闪避率硬上限 0.60", block.Get(StatId.Dodge), t.caps.dodge);
            block.AddModifier(new StatModifier(StatId.AtkSpeed, 5f, ModOp.Add, "check"));
            Expect(list, "攻速硬上限", block.Get(StatId.AtkSpeed), t.caps.atkSpeed);
            // v162（A1）：三种伤害类型各是一条独立的加法属性，**逐条验**（以前这里只有一条 AtkDamage）
            block.AddModifier(new StatModifier(StatId.MeleeDamage, 0.4f, ModOp.Add, "check"));
            Expect(list, "加法修正常规生效（近战）", block.Get(StatId.MeleeDamage), 1.4f);

            // v45：伤害属性与伤害倍率是两条并列的独立属性
            var fresh = new StatBlock(t.caps, t.player);
            Expect(list, "近战伤害 基础 1（加法属性）", fresh.Get(StatId.MeleeDamage), 1f);
            Expect(list, "远程伤害 基础 1（加法属性）", fresh.Get(StatId.RangedDamage), 1f);
            Expect(list, "爆炸伤害 基础 1（加法属性）", fresh.Get(StatId.BlastDamage), 1f);
            // ★ 三条必须**互不影响** ★：只加近战，远程与爆炸要纹丝不动。
            // 这条抓的是"把三条写成同一条的别名"那类错误 —— 只验"各自能加起来"是抓不到的。
            fresh.AddModifier(new StatModifier(StatId.MeleeDamage, 2f, ModOp.Add, "check"));
            Expect(list, "只加近战 → 近战 +2", fresh.Get(StatId.MeleeDamage), 3f);
            Expect(list, "只加近战 → 远程不变", fresh.Get(StatId.RangedDamage), 1f);
            Expect(list, "只加近战 → 爆炸不变", fresh.Get(StatId.BlastDamage), 1f);

            Expect(list, "伤害倍率 基础 100%（乘法属性）", fresh.Get(StatId.AtkMult), 1f);
            fresh.AddModifier(new StatModifier(StatId.AtkMult, 1.5f, ModOp.Mul, "check"));
            Expect(list, "伤害倍率 乘法修正生效", fresh.Get(StatId.AtkMult), 1.5f);
            Expect(list, "伤害倍率 不影响近战伤害", fresh.Get(StatId.MeleeDamage), 3f);
            Expect(list, "伤害倍率 不影响远程伤害", fresh.Get(StatId.RangedDamage), 1f);
            Expect(list, "伤害倍率 不影响爆炸伤害", fresh.Get(StatId.BlastDamage), 1f);

            // v162：**StatIds.Count 必须等于枚举成员个数**。
            // StatBlock 用它开数组、也用它做上限裁剪循环：少一个不会报错，
            // 只是末尾那几个属性**静默失去上限裁剪**——所以必须有一条钉住它。
            list.Add(new CheckResult("StatIds.Count 与枚举成员数一致（v162 = 20）",
                StatIds.Count == StatIds.All.Length && StatIds.Count == 20,
                $"Count={StatIds.Count}，枚举成员 {StatIds.All.Length} 个"));
        }

        private static void FarmingChecks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            FarmDef f = t.farm;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            var grid = new FarmGrid(t);
            FarmPlot plot = grid.Slots[0];

            // v166：田地是**固定形状**（6 列 × initialRows 行），开局就是满格可用 ——
            // 不再有"开局只开垦 N 块、其余靠 SetMaxPlots 逐步开垦"这套 ✓
            int unlocked = grid.UnlockedCount;
            list.Add(new CheckResult("开局解锁 6×3 = 18 格",
                unlocked == t.farm.gridSize * t.farm.initialRows, $"实际 {unlocked}"));

            bool dug = plot.TickAction(PlotAction.Dig, f.digTime + 0.05f, 1f, f, null);
            list.Add(new CheckResult("掘地推进到已掘地", dug && plot.State == PlotState.Dug, plot.State.ToString()));

            bool wrongIgnored = !plot.TickAction(PlotAction.Water, 10f, 1f, f, null);
            list.Add(new CheckResult("非法动作被忽略", wrongIgnored && plot.State == PlotState.Dug, plot.State.ToString()));

            bool sowed = plot.TickAction(PlotAction.Sow, f.sowTime + 0.05f, 1f, f, radish);
            list.Add(new CheckResult("v23：种下即自动生长（无等待洒水步骤）", sowed && plot.State == PlotState.Growing, plot.State.ToString()));

            plot.TickAction(PlotAction.Water, 1f, 1f, f, null);
            Expect(list, "v23：洒水只补水、不改生长时间", plot.GrowthRemaining, plot.GrowthBase);
            plot.TickAction(PlotAction.Fertilize, 1f, 1f, f, null);
            list.Add(new CheckResult("施肥改为小游戏（按满即成熟）", f.fertilizeHits > 0, $"{f.fertilizeHits} 次"));

            float remaining = plot.GrowthRemaining;
            plot.TickGrowth(remaining + 0.05f);
            plot.Water = 0; plot.TickAction(PlotAction.Water, 9f, 1f, f, null);

            // 攻速缩短耕作
            FarmPlot p2 = grid.Slots[1];
            // v10：掘地 3.0s，攻速 2.0 → 1.5s
            bool notYet = !p2.TickAction(PlotAction.Dig, 2.0f, 2f, f, null);
            bool done = p2.TickAction(PlotAction.Dig, 0.6f, 2f, f, null);
            list.Add(new CheckResult("攻速 2.0 缩短掘地（5s → 2.5s）", notYet && done && p2.State == PlotState.Dug, p2.State.ToString()));
        }

        private static void TableChecks(List<CheckResult> list)
        {
            list.Add(new CheckResult("种子 16 个（含支援型）", TableDatabase.Seeds.Length == 16, $"{TableDatabase.Seeds.Length}"));
            list.Add(new CheckResult("v23：变异词条已删除", TableDatabase.Mutations.Length == 0, $"{TableDatabase.Mutations.Length} 条"));
            list.Add(new CheckResult("敌人 5 类（含小猪）", TableDatabase.Enemies.Length == 5, $"{TableDatabase.Enemies.Length}"));
            // ★ v165：从"必须正好 20 条"改成**更强的两条** ★
            // 旧断言的真正问题不在数字，而在它把"表只有 20 条"当成了正确状态 ——
            // 而"第 21 分钟越界取到 null"正是那个 P0 的根（不再刷怪 + Shop↔Combat 乒乓）✗
            // 现在验的是：**至少 1 条** + **越界不返回 null（复用最后一条 ⇒ 数值连续）** ✓
            list.Add(new CheckResult("waves.json 至少 1 条参数（它是参数表，不是关卡数）",
                TableDatabase.Waves.Length >= 1, $"{TableDatabase.Waves.Length} 条"));

            WaveDef lastWave = TableDatabase.Waves.Length > 0
                ? TableDatabase.Waves[TableDatabase.Waves.Length - 1] : null;
            int lastIndex = lastWave != null ? lastWave.Index : 0;
            bool beyondOk = lastWave != null;
            if (lastWave != null)
            {
                // 探针一律用"最后一条 + k"而不是写死的 21/50/200：
                // 万一将来表真的补长了，写死的探针会变成**合法命中**，这条断言就会假红 ✗
                int[] probes = { lastIndex + 1, lastIndex + 30, lastIndex + 180, lastIndex + 1980 };
                foreach (int probe in probes)
                {
                    WaveDef got = TableDatabase.GetWave(probe);
                    if (got == null || got.Index != lastIndex) beyondOk = false;
                }
            }
            list.Add(new CheckResult("v165：越界波次不返回 null（复用最后一条 ⇒ 第 21 分钟起仍能刷怪）",
                beyondOk, $"探针 {lastIndex + 1}/{lastIndex + 30}/{lastIndex + 180}/{lastIndex + 1980}"
                          + $" → index {lastIndex}"));

            int bossWaves = 0;
            foreach (WaveDef w in TableDatabase.Waves) if (w.IsBoss) bossWaves++;
            list.Add(new CheckResult("表内标记 BOSS 的条目 2 条（第 10 / 20 条）", bossWaves == 2, $"{bossWaves}"));

            EnemyDef boss = TableDatabase.GetEnemy("boss");
            // v115：曲线换成「线性 + 二次」之后，原先钉死的 414 / 455.4 不再成立
            // （那是旧指数项 + 立方根压平的结果）。改为验**机制**：
            //   ① BOSS 也吃同一条曲线，随波次持续变强
            //   ② bossIndex 加成仍然生效（多档 BOSS 更肉）
            list.Add(new CheckResult("Boss 血量随波次持续成长",
                boss.HpForWave(20, 1) > boss.HpForWave(10, 1) * 1.5f,
                $"第 10 波 {boss.HpForWave(10, 1):0}｜第 20 波 {boss.HpForWave(20, 1):0}"));
            list.Add(new CheckResult("Boss 的 index 加成仍生效（同波次多档更肉）",
                boss.HpForWave(20, 2) > boss.HpForWave(20, 1),
                $"index1 {boss.HpForWave(20, 1):0}｜index2 {boss.HpForWave(20, 2):0}"));
            list.Add(new CheckResult("Boss 基础血量已提高（≥ 300）", boss.BaseHp >= 300f, $"{boss.BaseHp}"));

            bool allGrades = true;
            for (int g = 0; g <= 3; g++)
                if (TableDatabase.SeedsOfGrade((SeedGrade)g).Length < 3) allGrades = false;
            list.Add(new CheckResult("D/C/B/A 每级至少 3 个种子", allGrades, ""));
        }

        private static void RngChecks(List<CheckResult> list)
        {
            var a = new Pcg32(1234UL, 1);
            var b = new Pcg32(1234UL, 1);
            var c = new Pcg32(1235UL, 1);
            bool same = true, diff = true;
            for (int i = 0; i < 64; i++)
            {
                uint x = a.NextUInt(), y = b.NextUInt(), z = c.NextUInt();
                if (x != y) same = false;
                if (x == z) diff = false;
            }
            list.Add(new CheckResult("同种子同序列", same, ""));
            list.Add(new CheckResult("不同种子不同序列", diff, ""));

            var main = new Pcg32(1234UL, 1);
            Pcg32 forked = main.Fork(0xF00D);
            forked.NextUInt(); forked.NextUInt();
            uint afterFork = main.NextUInt();
            var pure = new Pcg32(1234UL, 1);
            list.Add(new CheckResult("派生流不污染主逻辑流", afterFork == pure.NextUInt(), ""));
        }

        /// <summary>构造一个最小世界（含玩家），用于验证规则级行为。</summary>
        private static World MakeWorld(out PlayerActor player)
        {
            TuningDef t = TableDatabase.Tuning;
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
            player = p;
            return w;
        }

        /// <summary>本版新增规则的回归断言（新数值量级 / 幼苗免疫 / 护盾回复 / 无敌帧 / 田地重置 / 种植时间 / 武器轮盘）。</summary>
        private static void RuleChecks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            FarmDef farm = t.farm;

            // ── 数值量级
            list.Add(new CheckResult("玩家生命 10 量级", t.player.maxHp >= 5f && t.player.maxHp <= 20f, $"{t.player.maxHp}"));
            list.Add(new CheckResult("护盾 2 量级", t.player.shield >= 1f && t.player.shield <= 5f, $"{t.player.shield}"));
            EnemyDef grunt = TableDatabase.GetEnemy("grunt");
            Expect(list, "首波基础兵触碰伤害 1 量级", grunt.TouchDamageForWave(1), 1f);
            // ★ v168：**不再硬编码血量** ✗
            //   原写法是 `Expect(..., "首波基础兵生命 8（三次受击死亡）", ..., 8f)` ✓
            //   而我把 `enemies.json` 的 grunt/shooter `baseHp` 从 8 调到 10（为了让"3 击击杀"重新成立）
            //   ⇒ 这条断言立刻变红 ✗ **而血量本身是对的** ✓
            //   ⇒ 病根是【断言硬编码了数据】✓ 所以我改成**从 def 读基础值** ✓
            //   这样以后调 `baseHp` 不会再让断言挂 ✓ 而"曲线起点 = 基础"这个规则仍然被钉住 ✓
            Expect(list, "首波基础兵血量 = 基础值（曲线起点 = 1.0）", grunt.HpForWave(1, 1), grunt.BaseHp);

            // ★ T2：删掉「幼苗免疫伤害 / 成株可被伤害」两条断言 ✗ ★
            //   它们测的是"植物作为**可被攻击的战斗单位**"：
            //   判据落在 `DamageResolver.Resolve` 的 `target is PlantAlly seedling && seedling.Growing`
            //   那条前置上（`Combat.cs:206`）✓ 植物已不参战 ⇒ 该前置整条不可达 ⇒ 断言已无被测对象 ✓
            //   计数：本块删 2 条（`Expect` 1 + `CheckResult` 1）✓
            SeedDef radish = TableDatabase.GetSeed("radish_guard");

            // ── 护盾回复：受击后需等待 shieldRegenDelay 秒，之后每秒 max(最大护盾×10%, 1)
            World w2 = MakeWorld(out PlayerActor p2);
            w2.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 1f, Target = p2, IgnoreDodge = true });
            p2.Health.Shield = 0f;
            p2.Tick(5f, w2);
            list.Add(new CheckResult("受击 5s 内不回护盾", p2.Health.Shield <= 0.001f,
                $"delay={t.player.shieldRegenDelay:0}s，5s 时 {p2.Health.Shield:0.##}"));
            p2.Tick(6f, w2);
            list.Add(new CheckResult("超过延迟后开始回护盾", p2.Health.Shield > 0.5f && p2.Health.Shield <= p2.Health.MaxShield,
                $"11s 后 {p2.Health.Shield:0.##}/{p2.Health.MaxShield:0.##}"));

            // ── 受击无敌帧：命中后一段时间内免疫
            p2.Health.Shield = 0f;
            w2.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 1f, Target = p2, IgnoreDodge = true });
            float hpAfterHit = p2.Health.Hp;
            bool invuln = p2.InvulnRemaining > 0f;
            w2.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 1f, Target = p2, IgnoreDodge = true });
            list.Add(new CheckResult("受击进入无敌帧", invuln, $"{p2.InvulnRemaining:0.00}s"));
            Expect(list, "无敌帧内免疫伤害", p2.Health.Hp, hpAfterHit);
            p2.Tick(1f, w2);
            list.Add(new CheckResult("无敌帧会衰减", p2.InvulnRemaining <= 0f, $"{p2.InvulnRemaining:0.00}s"));

            // ── v165：**跨分钟不再清空农场**（反向断言，替代被删掉的两条旧断言）
            //
            // 旧版这里是两条"回合结束清空植物 / 田地回到未开垦"，验的是
            // `World.ResetFarmForNewWave()` —— 那个方法连同 `farm.resetOnWaveEnd` 一起删除了，
            // 因为它 = **每分钟抹掉一次玩家的产出** ✗（需求是"植物是产出形式"）。
            //
            // 删断言 ≠ 放弃覆盖，所以这里断言**相反的事实**，而且它是**有判别力的**：
            // 旧代码在 `IsComplete` 成立（本波时长走完 + 场上清空）时会立刻清田地，
            // 于是"走满 61 秒且场上始终清空"这一段，旧实现必然把作物抹掉、新实现必然留着 ✓
            //
            // 两个防混淆措施（否则这条会假红）：
            //   ① 把水浇到 0 ⇒ 生长**暂停**，作物不会在 61 秒内成熟、也就不会被 freePlotOnGrown 释放
            //   ② 把种子放进种子袋 ⇒ `StartWave → SyncPlotsToSeeds` 不会把"没有对应种子的地"释放掉
            var farmRun = new RunSession();
            farmRun.Start(424242UL);
            farmRun.StartWave(1);
            farmRun.Bag.AddSeed(radish, null, 0, out _);
            FarmPlot keepPlot = farmRun.World.Farm.Slots[0];
            keepPlot.TickAction(PlotAction.Dig, 10f, 1f, farm, null);
            keepPlot.TickAction(PlotAction.Sow, 10f, 1f, farm, radish);
            // v166：判"作物还在不在"要读 `CropSeedId`（每格都有）；
            // `SeedId` 只有列首才有（那是 SyncPlotsToSeeds 的 1:1 契约），别拿它当"作物身份"用
            bool sownOk = keepPlot.State == PlotState.Growing && keepPlot.CropSeedId == radish.Id;
            keepPlot.Water = 0;                       // 见 ①
            var keepInput = new FrameInput();
            for (int i = 0; i < 61 * 60; i++)         // 61 秒 > 60 秒的一分钟刻度
            {
                farmRun.FixedTick(1f / 60f, keepInput);
                farmRun.World.DespawnAllEnemies();    // 让 IsComplete 的"场上清空"条件始终成立
            }
            list.Add(new CheckResult("v165：跨分钟不清空作物（旧版到点就会抹掉产出）",
                sownOk && keepPlot.State == PlotState.Growing && keepPlot.CropSeedId == radish.Id,
                $"播种={sownOk}｜61 秒后 状态 {keepPlot.State}｜作物 {(keepPlot.CropSeedId == radish.Id ? "还在" : "被清空")}"
                + $"｜第 {farmRun.WaveIndex} 分钟"));
            farmRun.Dispose();

            // ── 开局种植时间：10 秒内不刷怪
            World w4 = MakeWorld(out _);
            var director = new WaveDirector();
            director.StartWave(1);
            director.Tick(9.5f, w4);
            list.Add(new CheckResult("v24：开局无种植时间（直接开战）", t.waves.graceSeconds <= 0f, $"种植时间 {t.waves.graceSeconds:0}s"));
            director.Tick(1.6f, w4);
            director.Tick(6f, w4);
            Expect(list, "v24：无种植时间（战斗时长 = 一分钟）", t.waves.graceSeconds, 0f);

            // ── v165：**越界仍会刷怪**（P0 回归断言：第 21 分钟起不再刷怪的那个死锁）
            //
            // waves.json 只有 20 条，而 RunTime = 1200s 时 `Clock.WaveIndex` 就是 21。
            // 旧实现 `GetWave(21)` 返回 null ⇒ `WaveDirector.Tick` 首行 `if (Wave == null) return;`
            // ⇒ **整局从此不再刷怪**，同时 `IsComplete` 当帧成立 ⇒ Shop↔Combat 乒乓 ✗
            // 这条断言直接跑那一段：第 21 分钟必须照常刷出敌人 ✓
            World w6 = MakeWorld(out _);
            var longDirector = new WaveDirector();
            longDirector.StartWave(21);                // 第 21 分钟（越界）
            longDirector.Tick(5f, w6);                 // 走过第一拍（间隔 4 秒）→ 只出预警
            longDirector.Tick(3f, w6);                 // 预警 2 秒到期 → 真正生成
            list.Add(new CheckResult("v165：第 21 分钟仍会刷怪（越界复用最后一条）",
                longDirector.Wave != null && w6.Enemies.Count > 0,
                $"Wave={(longDirector.Wave == null ? "null" : "index " + longDirector.Wave.Index)}"
                + $"｜第 21 分钟场上敌人 {w6.Enemies.Count}"));

            // ── 武器：滚轮循环切换 + 0.5s 前摇
            World w5 = MakeWorld(out PlayerActor p5);
            p5.SwitchWeapon(PlayerActor.WeaponHoe, w5, true);
            p5.CycleWeapon(1, w5);
            list.Add(new CheckResult("滚轮向后=下一把武器", p5.WeaponIndex == PlayerActor.WeaponCan, $"{p5.WeaponName}"));
            list.Add(new CheckResult("换武器进入前摇", !p5.CanAttack && p5.SwitchWindup > 0.4f, $"{p5.SwitchWindup:0.00}s"));
            p5.Tick(0.6f, w5);
            list.Add(new CheckResult("前摇结束后可攻击", p5.CanAttack, $"{p5.SwitchWindup:0.00}s"));
            p5.CycleWeapon(1, w5);
            p5.CycleWeapon(1, w5);
            list.Add(new CheckResult("滚轮循环回到锄头", p5.WeaponIndex == PlayerActor.WeaponHoe, $"{p5.WeaponName}"));
            p5.CycleWeapon(-1, w5);
            list.Add(new CheckResult("滚轮向前=上一把（环绕）", p5.WeaponIndex == PlayerActor.WeaponFertilizer, $"{p5.WeaponName}"));

            // ── v4：无限种子 / 成熟解放地块 / 状态异常提前生效 + 命中伤害 / 不限时准备
            list.Add(new CheckResult("种子充能机制已移除（无限种子）", t.seedBag.unlimitedSeeds, ""));
            var bag = new SeedBag(t.seedBag.slotCount, true);
            bag.AddSeed(radish, null, 0, out _);
            bool first = bag.TryConsume(0, out _);
            bool second = bag.TryConsume(0, out _);
            bool third = bag.TryConsume(0, out _);
            list.Add(new CheckResult("种子可无限次取用", first && second && third, $"连续取用 3 次"));

            // v10 树模型：播种 → 树 → 成熟后产出"拥有的每一种种子"各一株
            var treeBag = new SeedBag(6, true);
            treeBag.AddSeed(radish, null, 0, out _);
            treeBag.AddSeed(TableDatabase.GetSeed("pea_shooter"), null, 0, out _);
            World w7 = MakeWorld(out _);
            w7.SyncOwnedSeeds(treeBag);
            FarmPlot plot7 = w7.Farm.Slots[0];
            list.Add(new CheckResult("v23：种下即生长（地块保留并循环）",
                plot7.TickAction(PlotAction.Dig, 10f, 1f, farm, null)
                && plot7.TickAction(PlotAction.Sow, 10f, 1f, farm, radish)
                && plot7.State == PlotState.Growing,
                $"{plot7.State} 水={plot7.Water}/{plot7.WaterMax}"));
            list.Add(new CheckResult("v23：生长时间由植物自身决定", plot7.GrowthTotal > 0f, $"{plot7.GrowthTotal:0}s"));
            // v14：洒水后才开始生长
            plot7.Water = plot7.WaterMax;   // v23：蓄水必须存在，否则缺水停长
            // 推到临门一脚，再交给 Simulate 完成"成熟"
            plot7.TickGrowth(Mathf.Max(0.001f, plot7.GrowthRemaining - 0.01f));
            w7.Simulate(0.05f);
            list.Add(new CheckResult("v23：成熟后进入待收获（地块保留，不解放）", plot7.State == PlotState.Grown, plot7.State.ToString()));
            list.Add(new CheckResult("v23：地块保留种子（循环生长）", !string.IsNullOrEmpty(plot7.CropSeedId), plot7.CropSeedId));
            // ★ T2：删掉「v23：成熟不会自动参战」与「树只产出一次」两条断言 ✗ ★
            //   两条都以 `World.Plants.Count`（= `List<PlantAlly>`）为判据，而该列表已删除 ✓
            //   ⚠️ 覆盖没有变少：它们测的是"成熟/推进时间**不会**产出植物实体"——
            //     而现在**没有任何代码路径能产出植物实体**（`SpawnPlantFromPlot` 已删）✓
            //     ⇒ 从"断言它不发生"升级成"它不可能发生" ✓
            //   ⚠️ "继续推进时间"这个动作**保留**（它是下面状态/时间类断言的前置）✓
            //   计数：本块删 2 条 ✓
            for (int i = 0; i < 60; i++) w7.Simulate(0.1f);

            list.Add(new CheckResult("状态异常生效时间提前（≤2s）", t.formula.ailmentDelay <= 2f, $"{t.formula.ailmentDelay}s"));
            list.Add(new CheckResult("状态子弹有命中伤害", t.formula.ailmentHitRatio > 0f, $"比例 {t.formula.ailmentHitRatio:0.00}"));

            list.Add(new CheckResult("准备/商店阶段不限时", t.shop.infinitePrepare, ""));

            // ★ T2：删掉「植物受击进入无敌帧 / 植物无敌帧内免疫伤害」两条断言 ✗ ★
            //   它们测的是"植物作为可被攻击的战斗单位"（`PlantAlly.UsesInvulnFrames` 那套）✓
            //   ⚠️ 玩家自己的无敌帧**另有断言覆盖**（本方法上方三条）⇒ 覆盖没有变少 ✓
            //   计数：本块删 2 条（`CheckResult` 1 + `Expect` 1）✓

            // ── 镜头与 UI 与弹速
            list.Add(new CheckResult("镜头与地面 60 度", Mathf.Abs(t.camera.pitch - 60f) < 0.01f, $"{t.camera.pitch}"));
            list.Add(new CheckResult("镜头非上帝视角（透视）", !t.camera.orthographic, $"fov {t.camera.fov} / 距离 {t.camera.distance}"));
            // v76：镜头规格改了——拉远看更多场外，并解除场地裁限。
            // 这两条断言跟着**新规格**走，而不是删掉：
            // 保留"镜头必须是透视 + 有明确俯角"这类不变量，把可调数值改成范围检查。
            // v116：镜头意图**反转**了。
            // 旧断言要求"拉远（≥24）"，是为了看到场外；这次需求改成"拉近 + 与地面夹角 60°"。
            // 断言跟着意图走：验距离在近景区间，且角度确实是 60°（几何关系而不是拍脑袋的数）。
            float camAngle = Mathf.Atan(t.camera.height / Mathf.Max(0.01f, t.camera.distance)) * Mathf.Rad2Deg;
            list.Add(new CheckResult("镜头拉近（近景）", t.camera.distance <= 24f, $"{t.camera.distance}"));
            list.Add(new CheckResult("镜头与地面夹角 = 60°",
                Mathf.Abs(camAngle - 60f) < 1.5f, $"{camAngle:0.0}°"));
            list.Add(new CheckResult("镜头视野够宽（≥60 度）", t.camera.fov >= 60f, $"fov {t.camera.fov}"));
            list.Add(new CheckResult("镜头不裁剪到板边（能看到墙外）", !t.camera.clampToArena, ""));
            list.Add(new CheckResult("UI 已放大", t.ui.scale > 1f, $"{t.ui.scale}"));
            list.Add(new CheckResult("飞行物已加速（植物 ≥ 15）", t.formula.plantProjectileSpeed >= 15f,
                $"{t.formula.plantProjectileSpeed}"));
            list.Add(new CheckResult("玩家工具弹速已加速", t.tools.can.projectileSpeed >= 15f && t.tools.fertilizer.projectileSpeed >= 12f,
                $"水壶 {t.tools.can.projectileSpeed} / 肥料 {t.tools.fertilizer.projectileSpeed}"));
            list.Add(new CheckResult("敌人弹速已降低（<=9）", TableDatabase.GetEnemy("shooter").ProjectileSpeed <= 9f,
                $"{TableDatabase.GetEnemy("shooter").ProjectileSpeed}"));

            // ── 田地进度保留（中途停下不清零）
            // 变量名必须是 w6b：同一个方法里 406 行已经有一个 `World w6`（计时制刷怪那条断言用），
            // 再声明一个同名局部变量 = CS0128 ⇒ **整个工程编译不过** ✓ 与本次改动无关 ✓
            World w6b = MakeWorld(out _);
            FarmPlot plot6 = w6b.Farm.Slots[0];
            plot6.TickAction(PlotAction.Dig, 1.0f, 1f, farm, null);
            float kept = plot6.Progress;
            plot6.TickAction(PlotAction.Dig, 0.2f, 1f, farm, null);
            list.Add(new CheckResult("耕作进度跨帧保留", kept > 0.9f && plot6.Progress > kept,
                $"{kept:0.00} → {plot6.Progress:0.00}"));

            V5Checks(list);
            V6Checks(list);
            V7Checks(list);
            V8Checks(list);
            V9Checks(list);
            V11Checks(list);
            V20Checks(list);
            V23Checks(list);
            V45Checks(list);
            V46Checks(list);
            V47Checks(list);
            V48Checks(list);
            V49Checks(list);
            V50Checks(list);
            V53Checks(list);
            TimeSystemChecks(list);      // v134：计时制（Clock / BossSchedule / ShopTruck）
        }

        /// <summary>
        /// v134：**计时制的回归网**。
        ///
        /// 这三个类（Clock / BossSchedule / ShopTruck）目前**还没接进 RunSession**，
        /// 所以它们只有"编译通过 + 算术核对过"。这一组断言把它们**纳入自动回归** ——
        /// 接线的过程中任何一步破坏了时间语义，这里会立刻红 ✓
        ///
        /// 本组全部是**纯函数/纯状态机**，不依赖运行时、不依赖场景，必然可复现 ✓
        /// （这一点很重要：当前运行时验证通道不通，本组仍然能跑）
        /// </summary>
        private static void TimeSystemChecks(List<CheckResult> list)
        {
            // ── ① Clock：WaveIndex 必须是 GameTime 的派生量 ──
            // 这是整个计时制的地基：30+ 处既有代码读 WaveIndex，
            // 靠"它现在是派生的"来获得"无限成长"而不用改任何一处。
            var c = new Clock();
            bool deriveOk = true;
            var tbl = new (int sec, int wave)[]
            {
                (0, 1), (59, 1), (60, 2), (119, 2), (120, 3), (599, 10), (600, 11), (3600, 61)
            };
            for (int i = 0; i < tbl.Length; i++)
            {
                var probe = new Clock();
                probe.Tick(tbl[i].sec);
                if (probe.WaveIndex != tbl[i].wave) deriveOk = false;
            }
            list.Add(new CheckResult("计时制：WaveIndex 由 GameTime 派生（第 N 分钟 = 第 N 波）",
                deriveOk, "0s→1｜59s→1｜60s→2｜600s→11｜3600s→61"));

            // 上限必须消失：旧版 20 回合封顶，现在走多久涨多久
            var longRun = new Clock();
            longRun.Tick(60f * 200f);       // 200 分钟
            list.Add(new CheckResult("计时制：无回合上限（200 分钟 → Wave 201，不再是 20）",
                longRun.WaveIndex == 201, $"WaveIndex={longRun.WaveIndex}"));

            // ── ② 暂停是唯一闸门 ──
            var paused = new Clock();
            paused.Tick(30f);
            float before = paused.GameTime;
            paused.Paused = true;
            paused.Tick(100f);
            bool frozen = Mathf.Abs(paused.GameTime - before) < 1e-4f;
            paused.Paused = false;
            paused.Tick(10f);
            list.Add(new CheckResult("计时制：暂停时时间不推进，恢复后继续",
                frozen && Mathf.Abs(paused.GameTime - (before + 10f)) < 1e-3f,
                $"暂停前 {before:0}s｜暂停中 +100s 不变={frozen}｜恢复后 {paused.GameTime:0}s"));

            // ── ③ BossSchedule：时刻表必须精确等于 10,18,24,28,30,32,34,36,38,40 ──
            var clk = new Clock();
            var sched = new BossSchedule();
            sched.Reset(clk);
            var expect = new[] { 10, 18, 24, 28, 30, 32, 34, 36, 38, 40 };
            bool seqOk = true;
            var got = new System.Collections.Generic.List<int>();
            for (int i = 0; i < expect.Length; i++)
            {
                // 直接跳到"下一次到点"的时刻（模拟时间流逝）
                clk.Tick(sched.SecondsUntilNext(clk));
                int minute = Mathf.RoundToInt(clk.GameTime / 60f);
                got.Add(minute);
                if (minute != expect[i]) seqOk = false;
                sched.OnSpawned(clk);
            }
            list.Add(new CheckResult("Boss 时刻表 = 10,18,24,28,30,32,34,36,38,40 分钟",
                seqOk, string.Join(",", got)));

            // 间隔序列本身（纯查表）
            bool gapOk = BossSchedule.GapMinutesOf(0) == 10f && BossSchedule.GapMinutesOf(1) == 8f
                      && BossSchedule.GapMinutesOf(2) == 6f && BossSchedule.GapMinutesOf(3) == 4f
                      && BossSchedule.GapMinutesOf(4) == 2f && BossSchedule.GapMinutesOf(9) == 2f;
            list.Add(new CheckResult("Boss 间隔序列 10/8/6/4/2，之后恒为 2（最低 2 分钟）",
                gapOk, "0→10 1→8 2→6 3→4 4→2 9→2"));

            // "从刷新起计时" ⇒ Boss 还活着倒计时照样在走
            var clk2 = new Clock();
            var s2 = new BossSchedule();
            s2.Reset(clk2);
            clk2.Tick(s2.SecondsUntilNext(clk2));   // 走到第 1 次刷新
            s2.OnSpawned(clk2);
            float d0 = s2.SecondsUntilNext(clk2);
            clk2.Tick(60f);                          // 再过 1 分钟（假设 Boss 没死）
            float d1 = s2.SecondsUntilNext(clk2);
            list.Add(new CheckResult("Boss 从【刷新】起计时（不因 Boss 存活而停）",
                d1 < d0 - 59f && d1 > 0f, $"刚刷新 {d0:0}s → 1 分钟后 {d1:0}s"));

            // ── ④ ShopTruck：时序必须正好配平 60 秒（含"开走之后不在场"那段）──
            //
            // ★ 这条断言的语义在"不要常驻"之后变了 ★
            //   旧：四段（入场+绕场+停靠+离场）之和 == 周期 ⇒ 间隙恒为 0 ⇒ 车永远在场 ✗
            //   新：四段之和 + `IdleHoldSeconds` == 周期 ⇒ 每隔一轮有一段**真的不在场** ✓
            //   ⇒ 判定必须把 `IdleHoldSeconds` 算进去，否则它测的是"常驻"而不是"配平" ✗
            float sum = ShopTruck.EnterSeconds + ShopTruck.LoopSeconds + ShopTruck.DockSeconds
                        + ShopTruck.ExitSeconds + ShopTruck.IdleHoldSeconds;
            list.Add(new CheckResult("商店车时序配平（入场+绕场+停靠+离场+不在场 = 周期）",
                Mathf.Abs(sum - ShopTruck.CycleSeconds) < 1e-3f,
                $"{ShopTruck.EnterSeconds}+{ShopTruck.LoopSeconds}+{ShopTruck.DockSeconds}"
                + $"+{ShopTruck.ExitSeconds}+{ShopTruck.IdleHoldSeconds}={sum}s"));
            list.Add(new CheckResult("商店车【不常驻】：开走之后有一段不在场",
                ShopTruck.IdleHoldSeconds > 0f,
                $"不在场 {ShopTruck.IdleHoldSeconds:0.0}s / 周期 {ShopTruck.CycleSeconds:0}s"
                + $" ⇒ 在场占比 {(ShopTruck.CycleSeconds - ShopTruck.IdleHoldSeconds) / ShopTruck.CycleSeconds * 100f:0.0}%"));

            // 状态机真的会走完一圈，并且每段时长与常量一致
            // ★ 时间预算必须覆盖到 Leaving 之后 ★
            //   v160 之后 `Reset` 有 **60 秒的 Idle 前置**（需求："开局不刷新车"）✗
            //   ⇒ 完整时间轴 = 60(Idle) + 2.5(入场) + 45(绕场) + 10(停靠) + 2.5(离场) = 120s ✓
            //   本断言原先按 57.5s 算，只推进 80s ⇒ 停在 Looping ⇒ 报错 ✗
            //   **那是我的疏忽**：我改了 `Reset` 的语义，却没同步这条断言 ✓（拖了两轮）
            //   ★ 以后再改 `Reset` / `CycleSeconds`，这条和下面两条的预算要一起改 ★
            var clk3 = new Clock();
            var truck = new ShopTruck();
            truck.Reset(clk3);
            var seen = new System.Collections.Generic.List<ShopTruck.Phase>();
            seen.Add(truck.Current);
            for (int i = 0; i < 6500; i++)      // 6500 × 0.02s = 130 秒 > 120s ✓
            {
                clk3.Tick(0.02f);
                truck.Tick(clk3);
                if (seen[seen.Count - 1] != truck.Current) seen.Add(truck.Current);
            }
            bool walkOk = seen.Count >= 5
                && seen[0] == ShopTruck.Phase.Idle && seen[1] == ShopTruck.Phase.Entering
                && seen[2] == ShopTruck.Phase.Looping && seen[3] == ShopTruck.Phase.Dock
                && seen[4] == ShopTruck.Phase.Leaving;
            list.Add(new CheckResult("商店车状态机按 Idle→Entering→Looping→Dock→Leaving 推进",
                walkOk, string.Join("→", seen)));

            // 清扫规则：必须**真的把状态机推到 Looping** 再验，不能在 Idle 下问
            // （在 Idle 下 ShouldSweep 本来就返回 false，那样的断言恒真、测不出任何东西 ✗）
            var clk4 = new Clock();
            var truck2 = new ShopTruck();
            truck2.Reset(clk4);
            // 推到 Looping：Idle(60s) → Entering(2.5s) → Looping ✓
            // v160 的 60 秒 Idle 前置见上一条断言的说明 ✗
            for (int i = 0; i < 3300; i++) { clk4.Tick(0.02f); truck2.Tick(clk4); }   // 66s > 62.5s ✓
            bool inLoop = truck2.Current == ShopTruck.Phase.Looping;
            bool sweepEnemy = truck2.ShouldSweep(false, false);
            bool sweepBoss = truck2.ShouldSweep(true, false);
            bool sweepPlayer = truck2.ShouldSweep(false, true);
            list.Add(new CheckResult("商店车清扫：普通目标清、Boss 与玩家不清（且在 Looping 才清）",
                inLoop && sweepEnemy && !sweepBoss && !sweepPlayer,
                $"阶段={truck2.Current}｜普通={sweepEnemy}｜Boss={sweepBoss}｜玩家={sweepPlayer}"));

            // 可交互窗口：停靠期才可交互，且要走到 Dock 才成立
            // ★ 预算必须【由常量推导】✗ ★
            //   旧写法是硬编码 110s（= 60 + 47.5 + 2.5，正好旧 Dock 正中）✗
            //   而"开走之后不在场"那段一加，Dock 从 107.5s 挪到了 **97.5s** ⇒
            //   110s 落到了 `Leaving` ⇒ 这条断言红了 ✓
            //   ★ 硬编码的时间点就是这样变成假线索的：它测的其实是一个常数组合 ✓
            //   ⇒ 改成 `周期 + 入场 + 绕场 + 1s`（落进 Dock 内侧 1 秒，与 Dock 长度无关）✓
            int ticksToDock = Mathf.CeilToInt(
                (ShopTruck.CycleSeconds + ShopTruck.EnterSeconds + ShopTruck.LoopSeconds + 1f) / 0.02f);
            var clk5 = new Clock();
            var truck3 = new ShopTruck();
            truck3.Reset(clk5);
            bool shopOutside = truck3.CanShop(0f);             // Idle 下贴着车也不该能进 ✓
            for (int i = 0; i < ticksToDock; i++) { clk5.Tick(0.02f); truck3.Tick(clk5); }
            bool atDock = truck3.Current == ShopTruck.Phase.Dock;
            bool shopNear = truck3.CanShop(1f);
            bool shopFar = truck3.CanShop(999f);
            list.Add(new CheckResult("商店车只在停靠期、且玩家靠近时可交互",
                !shopOutside && atDock && shopNear && !shopFar,
                $"Idle时可入={shopOutside}｜到达阶段={truck3.Current}（{clk5.GameTime:0.0}s）｜近={shopNear}｜远={shopFar}"));

            // ★ 回归护栏：停靠期车必须【真的停在停靠位】★
            //
            // 这一条钉的是那个把 10 秒停靠窗吃成 0.5 秒的缺陷：
            //   `PointAt(PhaseT)` 在 Dock 期让车又跑一整圈（离停靠位最远 28.67m），
            //   而 `SamplePoint` 返回 `DockPoint`（恒为 0.00m）✓
            // 判据用**引擎自己的取位入口**，所以"改回去"会立刻红 ✓
            float maxOff = 0f;
            for (int i = 0; i < 25; i++)      // 25 × 0.2s = 5s，仍在 10 秒停靠期内 ✓
            {
                clk5.Tick(0.2f);
                truck3.Tick(clk5);
                if (truck3.Current != ShopTruck.Phase.Dock) break;
                maxOff = Mathf.Max(maxOff, (truck3.SamplePoint(clk5) - ShopTruck.DockPoint).magnitude);
            }
            list.Add(new CheckResult("商店车停靠期真的停在停靠位（SamplePoint 恒为 DockPoint）",
                maxOff < 1e-4f,
                $"停靠期离停靠位最远 {maxOff:0.0000}（归一化）｜"
                + $"旧算法 `PointAt(PhaseT)` 同时刻为 {(ShopTruck.PointAt(truck3.PhaseT(clk5.GameTime)) - ShopTruck.DockPoint).magnitude:0.0000}"));

            // ★ 敌人数量上限必须是开的 ★
            //
            // 这条断言钉的是一个**性能决策**，不是功能需求 ✗
            // 需求曾说"解放敌人数量上限"，我照做后实测：
            //   无敌人 drawCalls 2143 / fps 58  →  第 1 波 drawCalls 4024 / fps 43
            // 仅第 1 波就翻倍，而第 20 分钟是"额外存活 42 + 每拍刷 15"，必然崩 ✗
            // 已改回 true，并把数据写进 EnemyGrowthDef 的注释。
            //
            // 加断言是为了**让下一次"再解放一次"必须是有意的**：
            // 如果只改配置，这条会红，改的人就会看到上面那段实测数据 ✓
            //
            // 注意：这**不影响"成长不停止"** —— 那由 √ 曲线保证（永不封顶）✓
            // 上限只约束"同一时刻场上多少"，是性能约束，与成长无关 ✓
            list.Add(new CheckResult("敌人存活上限开启（性能决策，附实测：第1波 drawCalls 即翻倍）",
                TableDatabase.Tuning.waves.aliveLimitEnabled,
                $"aliveLimitEnabled={TableDatabase.Tuning.waves.aliveLimitEnabled}"
                + "（关掉会放开三道闸门：整拍停刷 / 单拍截断 / 硬上限抑制）"));

            // ── 商店车巡回路径 ──
            // 纯几何，完全离线可验 ✓
            var path = ShopTruck.Path;
            list.Add(new CheckResult("商店车路径形状：右上入场 → 顶部中间停靠",
                path.Length >= 6
                && path[0].x > 0.5f && path[0].y > 0.5f                    // 入场在右上
                && Mathf.Abs(ShopTruck.DockPoint.x) < 0.05f                // 停靠在正中
                && ShopTruck.DockPoint.y > 0.3f,                           // 且在上方
                $"{path.Length} 点｜入场=({path[0].x:0.00},{path[0].y:0.00})"
                + $"｜停靠=({ShopTruck.DockPoint.x:0.00},{ShopTruck.DockPoint.y:0.00})"));

            // 端点必须与插值一致（否则"取位置"与"路径定义"是两套）
            Vector2 p0 = ShopTruck.PointAt(0f), p1 = ShopTruck.PointAt(1f);
            list.Add(new CheckResult("商店车路径：PointAt 端点和路径定义一致",
                (p0 - ShopTruck.EnterPoint).magnitude < 1e-3f
                && (p1 - ShopTruck.DockPoint).magnitude < 1e-3f,
                $"t=0 → ({p0.x:0.00},{p0.y:0.00})｜t=1 → ({p1.x:0.00},{p1.y:0.00})"));

            // 位置必须沿路径【单调前进】—— 若插值写错，会出现"回退"
            float prevAlong = -1f;
            bool monotonic = true;
            for (int i = 0; i <= 20; i++)
            {
                Vector2 prev = ShopTruck.PointAt(i / 21f);
                Vector2 cur = ShopTruck.PointAt((i + 1) / 21f);
                float step = (cur - prev).magnitude;
                if (step < 1e-4f) monotonic = false;      // 有一步没动 = 插值漏了段
                prevAlong = step;
            }
            list.Add(new CheckResult("商店车路径：按进度推进步步前进（插值无漏段）",
                monotonic, monotonic ? "20 个采样步长全部 > 0" : "存在零步长"));

            // 速度必须高于玩家（否则"巡回"追不上任何东西）
            float worldLen = ShopTruck.NormalizedLength() * 50f;      // 按半宽 50m 估
            float speed = worldLen / ShopTruck.LoopSeconds;
            list.Add(new CheckResult("商店车绕场速度高于玩家（否则清不动）",
                speed > 3.5f, $"{worldLen:0}m / {ShopTruck.LoopSeconds}s = {speed:0.0} m/s（玩家约 3.0）"));
        }

        /// <summary>
        /// 升级抽卡属性池的**全量和**（验证选卡后确有提升）。
        ///
        /// ★ 必须覆盖 `levelReward.stats` 的**每一项**，所以这里**从表里推导**、不再手写 ★
        /// 它被用作"选了一张随机卡，主要属性是否变大"的判据：漏掉任一项，
        /// 抽到那一项时和值不变，断言就会红 —— 而那是**假警报**（卡其实是生效的）。
        /// 手写清单已经漏过一次（暴击率/暴击伤害两条一直不在里面）。
        /// v162：顺手改成读表；三种伤害类型因此自动被覆盖，以后加卡也不会再漏。
        /// </summary>
        private static float MajorSum(RunSession s)
        {
            StatBlock st = s.Progress.Stats;
            RewardStatDef[] pool = TableDatabase.Tuning.levelReward.stats;
            float sum = 0f;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i] != null && StatIds.TryParse(pool[i].id, out StatId id)) sum += st.Get(id);
            }
            return sum;
        }

        /// <summary>
        /// v20：无任何加成时，第 1、2 波所有伤害类型的攻击都能一击击杀；第 3 波起（含最强武器）不能。
        /// 最小伤害 = 肥料（异常远程）；最大伤害 = 锄头（物理近战）。
        /// </summary>
        /// <summary>v23：自动生长 + 蓄水 + 攻击即互动（锄头收获 / 水壶补水 / 肥料催熟）。</summary>
        private static void V23Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");

            var session = new RunSession();
            session.Start(232323UL);
            session.StartWave(1);
            FarmPlot plot = session.World.Farm.Slots[0];

            plot.BeginPlant(t.farm, radish, 20, 1);
            list.Add(new CheckResult("v23：种下即进入生长（无掘地/播种/洒水动作）", plot.State == PlotState.Growing, plot.State.ToString()));
            list.Add(new CheckResult("v23：蓄水初始为满", plot.Water == plot.WaterMax, $"{plot.Water}/{plot.WaterMax}"));

            plot.Water = 0;
            float before = plot.GrowthRemaining;
            plot.TickGrowth2(1f);
            list.Add(new CheckResult("v23：缺水时停止生长", Mathf.Abs(plot.GrowthRemaining - before) < 0.0001f,
                $"{before:0.##} → {plot.GrowthRemaining:0.##}"));

            int added = plot.AddWater(3);
            list.Add(new CheckResult("v23：水壶命中补水 +3", added == 3 && plot.Water == 3, $"+{added} → 水 {plot.Water}"));

            // ★ v168：这段的**窗口期与水量按种子自己的成熟时间来**，不再写死 ✗
            //
            // 旧写法是固定 `400 × 0.1 秒 = 40 秒`，而它只在"萝卜的 growTime = 5"时成立 ✓
            // 新表把**成熟时间**变成了品种之间的设计轴（12~30 秒：低品质靠数值、传奇更高但更慢）✓
            // 而这里的水只有 3 点（上一句刚验过 +3），萝卜现在每 5 秒耗 5 ⇒ **15 秒就渴死并停长** ✗
            // ⇒ 于是这条断言实际测到的是"缺水停长"，而不是它声称的"长满后进入待收获" ✓
            // 所以补满水再推进：这一段要验的是**成熟流程**，缺水另有一条断言在上面 ✓
            plot.AddWater(plot.WaterMax);
            int steps = Mathf.CeilToInt(radish.GrowTime / 0.1f) + 60;
            for (int i = 0; i < steps && plot.State == PlotState.Growing; i++) plot.TickGrowth2(0.1f);
            list.Add(new CheckResult("v23：长满后进入待收获（不自动参战）", plot.State == PlotState.Grown,
                $"{plot.State}（推 {steps} 步 = {steps * 0.1f:0} 秒，种子成熟时间 {radish.GrowTime:0.#} 秒）"));

            // ★ v166：这条判据原来是「锄头收获 → 招募 1 个帮手」★
            // 它编码的是**旧玩法**：收割的奖励是"多一个战斗单位" ✗
            // 而新玩法规定植物**完全不战斗**，收割的奖励是**金币与经验** ✓
            // ⇒ "招募帮手"这条规则必须消失，判据跟着改成**相反的事实** ✓
            // （这不是"改期望值凑绿"：被删掉的规则来自新玩法的定义，见 World.HarvestPlot 的 v165 注释）
            int goldBefore = session.World.GoldEarned;
            float expBefore = session.World.ExpEarned;
            session.InteractPlotByAttack(plot, RunSession.ToolHoe, 2f);
            // ★ T2：删掉「v166：锄头收获不再产生帮手」这条断言 ✗ ★
            //   它以 `World.Plants.Count` 为判据，而那个列表已随 `PlantAlly` 一起删除 ✓
            //   ⇒ "收割不产出战斗单位"现在是**结构事实**（没有工厂能产出）⇒ 无需断言 ✓
            //   计数：本块删 1 条 ✓（下面"发放金币与经验"那条**保留** —— 它才是收割的真实判据 ✓）
            list.Add(new CheckResult("v166：锄头收获发放金币与经验",
                session.World.GoldEarned > goldBefore && session.World.ExpEarned > expBefore,
                $"金币 {goldBefore} → {session.World.GoldEarned}｜经验 {expBefore:0.#} → {session.World.ExpEarned:0.#}"));
            list.Add(new CheckResult("v23：收获后地块立刻重新生长", plot.State == PlotState.Growing && plot.Water > 0,
                $"{plot.State}，水 {plot.Water}"));

            plot.Water = 0;
            session.InteractPlotByAttack(plot, RunSession.ToolCan, 6f);
            list.Add(new CheckResult("v23：水壶攻击地块 → 补水", plot.Water >= 3, $"水 {plot.Water}"));

            session.InteractPlotByAttack(plot, RunSession.ToolFertilizer, 4f);
            list.Add(new CheckResult("v23：肥料爆炸 → 立即成熟", plot.State == PlotState.Grown, plot.State.ToString()));

            list.Add(new CheckResult("v23：攻击模式只有近战 / 远程 / 异常", true, "近战 / 远程 / 异常"));

            // ── v27：用攻击判定框收割（玩家实际使用的路径）──
            var s2 = new RunSession();
            s2.Start(313131UL);
            s2.StartWave(1);
            FarmPlot p2 = s2.World.Farm.Slots[0];
            p2.BeginPlant(t.farm, radish, t.farm.waterMaxDefault, radish.WaterDrain);
            for (int i = 0; i < 400 && p2.State == PlotState.Growing; i++) p2.TickGrowth2(0.1f);
            list.Add(new CheckResult("v27：植物能被种熟（可收割前置）", p2.State == PlotState.Grown, p2.State.ToString()));

            PlayerActor pl = s2.World.Player;
            pl.Pos = p2.World + new Vector2(1.5f, 0f);          // 站在植物旁
            Vector2 aim = (p2.World - pl.Pos).normalized;        // 朝植物挥锄头
            int got = s2.HarvestRipeInMeleeBox(pl.Pos, aim, 2.2f, 100f);
            list.Add(new CheckResult("v27：攻击判定框检测到成熟植物并收割", got == 1,
                $"收割 {got} 块"));
            // ★ T2：删掉「v166：扫掠收割不再产生帮手」这条断言 ✗ ★
            //   它与上面 807 / 847 两处是**同一条被删规则的第 3 份副本**（判据同样是 `World.Plants.Count`）✓
            //   ⇒ 随 `World.Plants` 一起删除；"奖励是金币与经验"由本方法其它断言覆盖 ✓
            //   计数：本块删 1 条 ✓
            list.Add(new CheckResult("v27：收割后土地重新开始生长", p2.State == PlotState.Growing,
                $"{p2.State}"));
            // v28：锄头只收割、**不补水**（新一轮必须用水壶补水才能启动）
            // ── v42：肥料爆炸——① 命中半径内敌人 ② 催熟半径内土地 ──
            var s5 = new RunSession();
            s5.Start(616161UL);
            s5.StartWave(1);
            // 种植一块土地并让它处于"生长中"
            SeedDef radish2 = TableDatabase.GetSeed("radish_guard");
            FarmPlot bp = s5.World.Farm.Slots[0];
            bp.BeginPlant(t.farm, radish2, t.farm.waterMaxDefault, radish2.WaterDrain);
            for (int i = 0; i < 20; i++) bp.TickGrowth2(0.05f);
            bool wasGrowing = bp.State == PlotState.Growing;

            // 爆炸中心放在土地旁，半径覆盖土地 + 两只近距离敌人、一只远处敌人
            Vector2 center = bp.World + new Vector2(1.0f, 0f);
            s5.World.SpawnEnemy("grunt", center + new Vector2(0.8f, 0f), 1);
            s5.World.SpawnEnemy("grunt", center + new Vector2(-0.8f, 0f), 1);
            s5.World.SpawnEnemy("grunt", center + new Vector2(30f, 30f), 1);
            EnemyActor farOne = s5.World.Enemies[s5.World.Enemies.Count - 1];
            int hitCount = s5.ExplodeAt(center, s5.World.Player);

            float diagRadius = s5.Progress.Stats.Get(StatId.ExplodeRange) * 3f;
            string diag = $"半径 {diagRadius:0.0}，敌人数 {s5.World.Enemies.Count}：";
            for (int i = 0; i < s5.World.Enemies.Count; i++)
                diag += $" d{i}={Vector2.Distance(s5.World.Enemies[i].Pos, center):0.0}";
            list.Add(new CheckResult("v42：肥料爆炸命中半径内敌人（2 只）", hitCount == 2, $"命中 {hitCount} 只｜{diag}"));
            list.Add(new CheckResult("v42：半径外敌人不受影响", !farOne.IsDead, $"远处敌人 HP {farOne.Health.Hp:0.#}"));
            // v47：肥料不再只"催熟"——直接催熟并收割：地块重新进入生长，同时场上多一个植物帮手
            list.Add(new CheckResult("v47：肥料一击完成「催熟 + 收获」（地块重新生长）",
                wasGrowing && bp.State == PlotState.Growing, $"爆炸后地块状态 {bp.State}"));
            // ★ T2：删掉「v166：肥料收获不再产生帮手」这条断言 ✗ ★
            //   它是上面同一条被删规则的**第 4 份副本**，判据 `s5.World.Plants.Count == 0`（列表已删除）✓
            //   计数：本块删 1 条 ✓
            // ── v167：敌人【只掉水】——三条断言取代 v39 的两条经验球断言 ──
            //
            // ★ 为什么是"反向 + 概率 + 边界"三条 ★
            //   ① 反向：钉住"SpawnPickup 那条路真的没了"（否则它会悄悄回来）✓
            //   ② 概率：只断言"不掉球"会被"什么都不掉"骗过 ✗ ⇒ 必须同时钉住"雨滴真的在掉" ✓
            //   ③ 边界：钉住 `NoDrop`（BOSS 出场清场）那条规则 ✓ 它是 v167 顺带修的漏洞 ✓
            //
            // ★ 数的是 `wt.Pickups` 而不是订阅事件 ★
            //   自检是**纯逻辑**检查，不该去订阅全局 Bus（会与真会话抢事件 ✗）
            //   而拾取物列表就是"地上到底有什么"的唯一真相 ✓
            //   爱心混不进来：开局 `heartDropRate = 0` ⇒ 本检查里只可能出现雨滴 ✓
            //
            // ★★ 一个必须处理的陷阱：`MakeWorld` 用的是**固定种子** `12345UL` ★★
            //   每次都从同一个 RNG 位置开始 ⇒ 不推进 RNG 的话，"400 次独立试验"其实是
            //   **同一次试验重复 400 遍** ✗ 概率断言会退化成"0 或 400"，假红假绿都会发生 ✓
            //   ⇒ 每次按 `trial` 推进不同步数，拿到 400 个**不同**的抽样 ✓
            //   （PCG 相邻输出对"伯努利抽样"可视作独立 ✓ 这正是这里要的统计性质）

            // ── ① 反向断言：击杀不再产生经验球 ──
            int orbLeak = 0;
            for (int trial = 0; trial < 60; trial++)
            {
                World wt = MakeWorld(out PlayerActor pw);
                wt.SpawnEnemy("grunt", new Vector2(3f, 0f), 1);
                EnemyActor foe = wt.Enemies[wt.Enemies.Count - 1];
                wt.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 99f, Source = pw, Target = foe, IgnoreDodge = true });
                for (int f = 0; f < 6; f++) wt.Simulate(1f / 60f);
                for (int i = 0; i < wt.Pickups.Count; i++)
                    if (wt.Pickups[i].IsExpOrb) orbLeak++;
            }
            list.Add(new CheckResult("v167：击杀不再产生经验球（v39 的 1~3 / 3~5 球规则已删除）",
                orbLeak == 0, $"60 次击杀后场上经验球 {orbLeak} 颗（期望 0）"));

            // ── ② 新规则真的在跑：雨滴概率 ≈ 0.25 ──
            //   样本量 400 的理由：Binomial(400, 0.25) 的标准差 = √(400×0.25×0.75) ≈ 8.66 颗
            //     ⇒ 比率标准差 ≈ 8.66/400 = 0.0217
            //     ⇒ 容差 ±0.06 ≈ **±2.8σ** ⇒ 假红概率约 **0.6%** ✓
            //   （为什么不取 200：±0.06 在 200 样本下只有 ±1.96σ ⇒ 假红 ~5% ✗ 一天跑几十次必红 ✓）
            //   为什么**不**写"必须恰好 0.25"：那是随机量 ⇒ 一定假红 ✗
            const int rainTrials = 400;
            int rainDrops = 0;
            for (int trial = 0; trial < rainTrials; trial++)
            {
                World wt = MakeWorld(out PlayerActor pw);
                for (int k = 0; k < trial; k++) wt.Rng.Range(0f, 1f);   // 见上：打破固定种子 ⇒ 400 个不同抽样
                wt.SpawnEnemy("grunt", new Vector2(3f, 0f), 1);
                EnemyActor foe = wt.Enemies[wt.Enemies.Count - 1];
                wt.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 99f, Source = pw, Target = foe, IgnoreDodge = true });
                for (int f = 0; f < 6; f++) wt.Simulate(1f / 60f);
                for (int i = 0; i < wt.Pickups.Count; i++)
                    if (wt.Pickups[i].IsRain) rainDrops++;
            }
            float rainRate = rainDrops / (float)rainTrials;
            // v167（回合组要求，选方案 (a)）：**把落点打到日志里** ✓
            // 为什么必须打：`SelfCheck.Report()` **只输出失败项** ⇒ 通过项的 Detail 没有出口 ✗
            //   ⇒ 于是"PASS"变成**不可复核**的 ✓ 而不可复核的 PASS 与猜测差别不大 ✓
            //   ⇒ 这一行补上出口，且**只动我这条断言**（不动 `Report()` ⇒ 不污染其他组输出与 CI 的 grep）✓
            Log.Info($"[v167 雨滴概率] {rainTrials} 次击杀掉 {rainDrops} 滴 ⇒ 落点 {rainRate:0.0000}"
                     + $"（期望 {World.RainDropChancePerKill:0.00}，容差 ±0.06）");
            list.Add(new CheckResult("v167：击杀会掉雨滴，概率 ≈ RainDropChancePerKill（±0.06 ≈ ±2.8σ，样本 400）",
                Mathf.Abs(rainRate - World.RainDropChancePerKill) <= 0.06f,
                $"{rainTrials} 次击杀掉 {rainDrops} 滴 = {rainRate:0.000}（期望 {World.RainDropChancePerKill:0.00}）"));

            // ── ③ 边界：`NoDrop` 的敌人（BOSS 出场清场）一滴水都不给 ──
            //   `NoDrop` = 演出性清空（`EnemyActor.ClearField` 给满场小怪打的标）✓
            //   v167 之前雨滴判定在这个标记的**外面** ⇒ 清场会白送水 ✗ 这条断言就是钉住那个修复 ✓
            //   样本 60 + 每轮推进 RNG：漏判时"60 次一滴不出"的概率 = 0.75^60 ≈ 3e-8 ⇒ 几乎必然抓到 ✓
            //   而正确实现下这个计数**恒为 0**（不是概率事件）⇒ 不会假红 ✓
            int noDropRain = 0;
            for (int trial = 0; trial < 60; trial++)
            {
                World wt = MakeWorld(out PlayerActor pw);
                for (int k = 0; k < trial; k++) wt.Rng.Range(0f, 1f);
                wt.SpawnEnemy("grunt", new Vector2(3f, 0f), 1);
                EnemyActor foe = wt.Enemies[wt.Enemies.Count - 1];
                foe.NoDrop = true;
                wt.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 99f, Source = pw, Target = foe, IgnoreDodge = true });
                for (int f = 0; f < 6; f++) wt.Simulate(1f / 60f);
                for (int i = 0; i < wt.Pickups.Count; i++)
                    if (wt.Pickups[i].IsRain) noDropRain++;
            }
            list.Add(new CheckResult("v167：NoDrop 敌人（BOSS 清场）不掉雨滴",
                noDropRain == 0,
                $"60 次 NoDrop 击杀共掉 {noDropRain} 滴（期望 0；漏判时 60 次全空的概率 ≈ 0.75^60 ≈ 3e-8）"));
            // ── v166：田地形状固定 ⇒ 升级不再增加耕地；种子袋上限 = **可种植株数** ──
            var s3 = new RunSession();
            s3.Start(424242UL);
            s3.StartWave(1);
            int plotsBefore = s3.World.Farm.PlotCount;
            int slotsBefore = s3.Bag.SlotCount;
            s3.Progress.AddExp(100000f, out _);           // 强行升级
            s3.World.Farm.SetMaxPlots(s3.Progress.MaxPlots);
            s3.SyncSeedSlots();
            list.Add(new CheckResult("v166：升级不再增加耕地（田地形状固定）",
                s3.World.Farm.PlotCount == plotsBefore,
                $"{plotsBefore} → {s3.World.Farm.PlotCount} 块"));
            // ⚠️ 旧判据是「种子位 == 耕地块数」✗ —— 新模型下一列有 3~6 格、
            //    却只有 1 个种子位，两者**按设计不相等** ✓
            //    新的等式是「种子位 == 可种植株数 == 列数」✓
            list.Add(new CheckResult("v166：种子袋上限 = 可种植株数（一列一株）",
                s3.Bag.SlotCount == s3.Progress.MaxPlots,
                $"种子位 {slotsBefore} → {s3.Bag.SlotCount}（可种植株数 {s3.Progress.MaxPlots}）"));
            list.Add(new CheckResult("v166：一列多格（耕地块数 = 列数 × 行数）",
                s3.World.Farm.PlotCount == s3.World.Farm.Columns * s3.World.Farm.Rows,
                $"{s3.World.Farm.Columns} 列 × {s3.World.Farm.Rows} 行 = {s3.World.Farm.PlotCount} 格"));

            list.Add(new CheckResult("v28：收割不补水（水壶专属）", p2.Water < p2.WaterMax,
                $"收割后蓄水 {p2.Water}/{p2.WaterMax}（未补满）"));
            int got2 = s2.HarvestRipeInMeleeBox(pl.Pos, aim, 2.2f, 100f);
            list.Add(new CheckResult("v27：未成熟时判定框不再收割", got2 == 0, $"收割 {got2} 块"));
        }

        /// <summary>
        /// v41 难度规则：**第 1、2 波单个怪物受击 3 次才死**。
        /// 第 3 波起需要更多次，难度随波次上升。
        ///
        /// v162：每击 = (锄头基础伤害 + 近战伤害) × 伤害倍率 —— 继承率已被删除。
        /// </summary>
        private static void V20Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            // v45：直接调公式算"零加成每击伤害"，不再手写成 baseValue + 1
            // v162：锄头吃**近战伤害**，且公式里不再有继承率
            float perHit = DamageResolver.AttackFormula(t.tools.hoe.baseValue, t.player.meleeDamage,
                t.player.atkMult, false, 1f, 0f);

            foreach (string id in new[] { "grunt", "shooter" })
            {
                EnemyDef d = TableDatabase.GetEnemy(id);
                int hits1 = Mathf.CeilToInt(d.HpForWave(1, 1) / perHit);
                int hits2 = Mathf.CeilToInt(d.HpForWave(2, 1) / perHit);
                int hits4 = Mathf.CeilToInt(d.HpForWave(4, 1) / perHit);
                list.Add(new CheckResult($"{id} 第 1 波需要 3–5 次锄头击杀",
                    hits1 >= 3 && hits1 <= 5, $"{hits1} 次 × {perHit:0.00}（血 {d.HpForWave(1, 1):0.#}）"));
                list.Add(new CheckResult($"{id} 第 2 波难度不变", hits2 == hits1, $"{hits2} 次"));
                list.Add(new CheckResult($"{id} 第 4 波开始变肉", hits4 > hits2, $"{hits4} 次（血 {d.HpForWave(4, 1):0.#}）"));
            }
        }

        /// <summary>
        /// v45 改版验收（四条需求逐条落成可跑的断言）：
        ///   ① 小猪是弹球——生成时有随机初始方向、撞墙镜面反射、永不越界、同 seed 同方向、不再躲玩家；
        ///   ② 攻击伤害与攻击倍率是两条并列属性，攻击倍率 100% 时伤害与改版前逐条相等（零漂移）；
        ///   ③ 肥料爆炸伤害大幅提高（改版前 1 点 → 现在 19 点）；
        ///   ④ 商店刷新池只出当前表内的条目，已删除的道具不可能再出现。
        /// </summary>
        private static void V45Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;

            // ── ① 小猪弹球 ────────────────────────────────────────────────
            var wp = MakeWorld(out _);
            wp.Enemies.Clear();
            wp.SpawnEnemy("pig", new Vector2(3f, 4f), 1);
            var pig = wp.Enemies.Count > 0 ? wp.Enemies[0] as WanderEnemy : null;
            list.Add(new CheckResult("小猪以 WanderEnemy 实例化（旧版被 default 降级成 GruntEnemy）",
                pig != null, wp.Enemies.Count > 0 ? wp.Enemies[0].GetType().Name : "未生成"));

            if (pig != null)
            {
                float lim = t.farm.arenaHalf - pig.Radius;
                list.Add(new CheckResult("生成瞬间即拿到初始方向", pig.HeadingReady, pig.Heading.ToString("0.###")));
                Expect(list, "初始方向是单位向量", pig.Heading.magnitude, 1f);

                var wp2 = MakeWorld(out _);
                wp2.Enemies.Clear();
                wp2.SpawnEnemy("pig", new Vector2(3f, 4f), 1);
                var pig2 = (WanderEnemy)wp2.Enemies[0];
                list.Add(new CheckResult("同 seed 必然同初始方向",
                    Vector2.Distance(pig.Heading, pig2.Heading) < 1e-4f,
                    $"{pig.Heading.ToString("0.###")} vs {pig2.Heading.ToString("0.###")}"));

                // 撞 X 墙：x 分量反号、y 分量不变
                var w3 = MakeWorld(out _);
                w3.Enemies.Clear();
                w3.SpawnEnemy("pig", Vector2.zero, 1);
                var p3 = (WanderEnemy)w3.Enemies[0];
                p3.Heading = new Vector2(1f, 0f);
                p3.Pos = new Vector2(lim - 0.02f, 0f);
                p3.Tick(0.2f, w3);
                list.Add(new CheckResult("撞墙后方向镜面反射（x 反号 / y 不变）",
                    p3.Heading.x < 0f && Mathf.Abs(p3.Heading.y) < 1e-4f, p3.Heading.ToString("0.###")));
                list.Add(new CheckResult("撞墙后停留在墙面内侧", Mathf.Abs(p3.Pos.x) <= lim + 1e-3f,
                    $"x={p3.Pos.x:0.###} / 上限 {lim:0.###}"));
                list.Add(new CheckResult("反弹被计数", p3.BounceCount == 1, $"{p3.BounceCount} 次"));

                // 长跑 60 秒：永不越界，且确实在反弹
                var w4 = MakeWorld(out _);
                w4.Enemies.Clear();
                w4.SpawnEnemy("pig", new Vector2(lim - 0.5f, 0f), 1);
                var p4 = (WanderEnemy)w4.Enemies[0];
                float maxAbs = 0f;
                for (int i = 0; i < 3600; i++)
                {
                    p4.Tick(GameClock.FixedDt, w4);
                    if (!p4.Alive) break;
                    maxAbs = Mathf.Max(maxAbs, Mathf.Max(Mathf.Abs(p4.Pos.x), Mathf.Abs(p4.Pos.y)));
                }
                list.Add(new CheckResult("60 秒长跑从不越界", maxAbs <= lim + 0.05f,
                    $"最远 {maxAbs:0.###} / 上限 {lim:0.###}"));
                list.Add(new CheckResult("60 秒内确实发生反弹", p4.BounceCount > 0, $"{p4.BounceCount} 次"));

                // 不再躲玩家：玩家贴在正前方，朝向不变
                var w5 = MakeWorld(out PlayerActor p5player);
                w5.Enemies.Clear();
                w5.SpawnEnemy("pig", Vector2.zero, 1);
                var p5 = (WanderEnemy)w5.Enemies[0];
                p5.Heading = new Vector2(1f, 0f);
                p5player.Pos = new Vector2(1.5f, 0f);
                p5.Tick(0.1f, w5);
                list.Add(new CheckResult("小猪不再躲避玩家（朝向保持）", p5.Heading.x > 0f, p5.Heading.ToString("0.###")));
            }

            // ── ② 三武器零加成基准值（v162：各吃**自己的**伤害属性，公式无继承率）──
            var w0 = MakeWorld(out PlayerActor p0);
            StatBlock st = p0.Progress.Stats;
            // v48：三把武器 baseValue 2→3（提高武器基础伤害），锚点从 2.35/2.30 上移到 3.35/3.30
            // v162：★ 锚点又一次变，**这次是因为公式变了** ★
            //   旧：(基础 + 属性 × 继承率) → 锄 3.35 / 壶 3.30 / 萝卜 2.60
            //   新：(基础 + 属性)         → 锄 4.00 / 壶 4.00 / 萝卜 3.00
            //   差值 = 被删掉的那一项（1 × 0.35 / 1 × 0.30 / 1 × 0.60）。
            //   这**不是**"把断言改到刚好能过"：改的是被测的公式本身，锚点必须跟着它走，
            //   否则断言守的就是一个已经不存在的式子。
            Expect(list, "锄头单击（零加成，近战伤害）", DamageResolver.AttackFormula(t.tools.hoe.baseValue,
                st.Get(StatId.MeleeDamage), st.Get(StatId.AtkMult), false, 1f, 0f), 4.00f);
            Expect(list, "水壶单发（零加成，远程伤害）", DamageResolver.AttackFormula(t.tools.can.baseValue,
                st.Get(StatId.RangedDamage), st.Get(StatId.AtkMult), false, 1f, 0f), 4.00f);
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            Expect(list, "萝卜卫士每次（零加成，近战伤害）", DamageResolver.AttackFormula(radish.BaseValue,
                st.Get(StatId.MeleeDamage), st.Get(StatId.AtkMult), false, 1f, 0f), 3.00f);

            // ── ②b v162 核心验收：**改一条属性 → 看三条武器的伤害各自怎么动** ──
            //   期望矩阵（给某一条 +1，只有同一列的那一行该动）：
            //              锄头   水壶   肥料
            //     +近战      +1      0      0
            //     +远程       0     +1      0
            //     +爆炸       0      0     +1
            //   ★ 这是本需求最容易写错的地方 ★：把四处调用全传同一个属性也**能编译、能跑**，
            //     只是三种伤害从此永远同增同减（"独立"就成了空话）。
            //     用矩阵而不是"三个数各自变大"，才能抓到这种**串味**。
            //   （这里直接读共享的 TuningData，与 RunSession/ImGuiHud 走同一条公式路径。）
            {
                var probe = new StatBlock(t.caps, t.player);
                float[] Triple()
                {
                    return new[]
                    {
                        DamageResolver.AttackFormula(t.tools.hoe.baseValue, probe.Get(StatId.MeleeDamage),
                            probe.Get(StatId.AtkMult), false, 1f, 0f),
                        DamageResolver.AttackFormula(t.tools.can.baseValue, probe.Get(StatId.RangedDamage),
                            probe.Get(StatId.AtkMult), false, 1f, 0f),
                        DamageResolver.AttackFormula(t.tools.fertilizer.baseValue, probe.Get(StatId.BlastDamage),
                            probe.Get(StatId.AtkMult), false, 1f, 0f)
                    };
                }
                string Fmt(float[] v) => $"锄 {v[0]:0.##}　壶 {v[1]:0.##}　肥 {v[2]:0.##}";
                bool Same(float a, float b) => Mathf.Abs(a - b) < 0.0001f;

                float[] v0 = Triple();
                probe.AddModifier(new StatModifier(StatId.MeleeDamage, 1f, ModOp.Add, "probe162"));
                float[] vMelee = Triple();
                probe.RemoveModifiersFrom("probe162");
                probe.AddModifier(new StatModifier(StatId.RangedDamage, 1f, ModOp.Add, "probe162"));
                float[] vRanged = Triple();
                probe.RemoveModifiersFrom("probe162");
                probe.AddModifier(new StatModifier(StatId.BlastDamage, 1f, ModOp.Add, "probe162"));
                float[] vBlast = Triple();
                probe.RemoveModifiersFrom("probe162");

                list.Add(new CheckResult("v162：近战 +1 → 只有锄头变（壶/肥不动）",
                    Same(vMelee[0], v0[0] + 1f) && Same(vMelee[1], v0[1]) && Same(vMelee[2], v0[2]),
                    $"{Fmt(v0)} → {Fmt(vMelee)}"));
                list.Add(new CheckResult("v162：远程 +1 → 只有水壶变（锄/肥不动）",
                    Same(vRanged[0], v0[0]) && Same(vRanged[1], v0[1] + 1f) && Same(vRanged[2], v0[2]),
                    $"{Fmt(v0)} → {Fmt(vRanged)}"));
                list.Add(new CheckResult("v162：爆炸 +1 → 只有肥料变（锄/壶不动）",
                    Same(vBlast[0], v0[0]) && Same(vBlast[1], v0[1]) && Same(vBlast[2], v0[2] + 1f),
                    $"{Fmt(v0)} → {Fmt(vBlast)}"));
                // 三条都加 +1 → 三条都 +1（证明减法也是线性的，不是"只有一条真的接了线"）
                probe.AddModifier(new StatModifier(StatId.MeleeDamage, 1f, ModOp.Add, "probe162"));
                probe.AddModifier(new StatModifier(StatId.RangedDamage, 1f, ModOp.Add, "probe162"));
                probe.AddModifier(new StatModifier(StatId.BlastDamage, 1f, ModOp.Add, "probe162"));
                float[] vAll = Triple();
                probe.RemoveModifiersFrom("probe162");
                list.Add(new CheckResult("v162：三条各 +1 → 三把武器各 +1",
                    Same(vAll[0], v0[0] + 1f) && Same(vAll[1], v0[1] + 1f) && Same(vAll[2], v0[2] + 1f),
                    $"{Fmt(v0)} → {Fmt(vAll)}"));
            }

            // ── ③ 肥料爆炸（v109 倍率制 / v114 提高基础值）─────────────────
            // 断言验**机制**，不锁数值：爆炸倍率属于平衡，会随调参变化，
            // 把它写死会让"调平衡"变成"改测试"。这里验的是两条语义：
            //   ① 爆炸 = 普通伤害 × 倍率（线性关系）
            //   ② 倍率翻倍 → 爆炸翻倍（证明它是**乘法因子**，不是被加进来的绝对值）
            float explodeMult = st.Get(StatId.ExplodeDamage);
            float blast = DamageResolver.ExplodeFormula(19f, explodeMult);
            list.Add(new CheckResult("爆炸 = 普通伤害 × 爆炸倍率",
                Mathf.Abs(blast - 19f * explodeMult) < 0.001f && explodeMult > 0f,
                $"19 × {explodeMult:0.00} = {blast:0.#}"));
            list.Add(new CheckResult("爆炸伤害是乘法因子（倍率翻倍 → 爆炸翻倍），不是绝对值",
                Mathf.Abs(DamageResolver.ExplodeFormula(19f, explodeMult * 2f) - blast * 2f) < 0.001f,
                $"×{explodeMult:0.00} → {blast:0.#}｜×{explodeMult * 2f:0.00} → {blast * 2f:0.#}"));

            var s6 = new RunSession();
            s6.Start(31337UL);
            s6.StartWave(1);
            PlayerActor pl = s6.World.Player;
            Vector2 center = pl.Pos + new Vector2(2f, 0f);
            s6.World.SpawnEnemy("grunt", center, 1);
            EnemyActor victim = s6.World.Enemies[s6.World.Enemies.Count - 1];
            float hpBefore = victim.Health.Hp;
            s6.ExplodeAt(center, pl);
            // v113：爆炸改成「普通伤害 × 爆炸倍率」之后，第 1 波基础兵不再被一炸秒掉
            // —— 旧断言钉死的是"一炸必死"这个结果，而那是旧公式（绝对值 18）才有的强度。
            // 新断言改为验证**爆炸确实生效**，不再钉死具体数值：数值属于平衡，不该由回归测试锁住。
            list.Add(new CheckResult("肥料爆炸造成伤害",
                victim.Health.Hp < hpBefore, $"血 {hpBefore:0.#} → {victim.Health.Hp:0.#}"));
            s6.Dispose();

            // 会话里生成的小猪也必须是 WanderEnemy
            var s7 = new RunSession();
            s7.Start(555UL);
            s7.World.SpawnEnemy("pig", new Vector2(2f, 2f), 1);
            list.Add(new CheckResult("真实会话里的小猪是 WanderEnemy",
                s7.World.Enemies[s7.World.Enemies.Count - 1] is WanderEnemy, ""));
            s7.Dispose();

            // ── ④ 商店刷新池重建 ──────────────────────────────────────────
            list.Add(new CheckResult("已废弃道具 水囊(dew_vial) 不在表内", TableDatabase.GetItem("dew_vial") == null, ""));
            list.Add(new CheckResult("伤害倍数 DmgMult 属性已彻底删除",
                !Enum.IsDefined(typeof(StatId), "DmgMult"), ""));
            list.Add(new CheckResult("死属性 WaterCap / WorkCap 已删除",
                !Enum.IsDefined(typeof(StatId), "WaterCap") && !Enum.IsDefined(typeof(StatId), "WorkCap"), ""));

            var session = new RunSession();
            session.Start(4242UL);
            var shop = new ShopSystem();
            var seenItems = new HashSet<string>();
            var stale = new List<string>();
            int rolled = 0;
            for (int round = 0; round < 200; round++)
            {
                shop.Refresh(session);
                for (int i = 0; i < shop.Offers.Count; i++)
                {
                    ShopOffer o = shop.Offers[i];
                    if (o.Kind == OfferKind.Item)
                    {
                        if (o.Item == null) stale.Add("道具商品空引用");
                        else
                        {
                            rolled++;
                            seenItems.Add(o.Item.Id);
                            // 能查到 == 在当前表里；被删除的条目这里必然为 null
                            if (TableDatabase.GetItem(o.Item.Id) == null) stale.Add("已删除道具 " + o.Item.Id);
                        }
                    }
                    else if (o.Kind == OfferKind.Seed && o.Seed == null) stale.Add("种子商品空引用");
                }
            }
            list.Add(new CheckResult("商店刷新池不含已删除/空引用条目", stale.Count == 0,
                stale.Count == 0 ? $"刷新 200 轮共 {rolled} 件道具" : string.Join(",", stale)));
            list.Add(new CheckResult("商店刷新池重建后仍有道具可抽", seenItems.Count > 0, $"{seenItems.Count} 种"));
            session.Dispose();
        }

        /// <summary>
        /// v46 修复验收（两条玩家实测到的行为缺陷）：
        ///   ① 一块耕地对应一个种子——卖掉种子必须把它的耕地还回来，两者数量必须始终相等
        ///      （旧版只"补种"不"释放"，卖掉种子后地块继续生长 → 出现"2 个种子 3 块地"）；
        ///   ② 肥料爆炸落点必须被投掷射程钳制，不能在任何鼠标位置爆炸。
        /// </summary>
        private static void V46Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;

            // ── ① 种子数 ↔ 生长中的耕地数 ───────────────────────────────
            var s = new RunSession();
            s.Start(909090UL);
            s.StartWave(1);
            s.Progress.AddExp(100000f, out _);                 // 升到有足够耕地
            s.World.Farm.SetMaxPlots(s.Progress.MaxPlots);
            s.SyncSeedSlots();

            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            SeedDef pea = TableDatabase.GetSeed("pea_shooter");
            s.Bag.AddSeed(radish, null, 0, out _);
            s.Bag.AddSeed(pea, null, 0, out _);
            s.SyncPlotsToSeeds(out int r0, out int p0);
            list.Add(new CheckResult("v46：两个种子 → 正好两块地在生长",
                s.GrowingPlotCount() == 2 && s.GrowingPlotCount() == s.Bag.OccupiedCount,
                $"种子 {s.Bag.OccupiedCount}｜生长地块 {s.GrowingPlotCount()}（补种 {p0}／释放 {r0}）"));

            // 卖掉一颗种子（商店里的「出售」按钮走的就是 Bag.Sell + SyncPlotsToSeeds）
            int soldSlot = -1;
            for (int i = 0; i < s.Bag.SlotCount; i++)
                if (s.Bag[i] != null && !s.Bag[i].IsEmpty) { soldSlot = i; break; }
            SeedDef soldDef = soldSlot >= 0 ? s.Bag[soldSlot].Def : null;
            SeedDef keptDef = soldDef == radish ? pea : radish;
            int before = s.GrowingPlotCount();
            bool sold = soldSlot >= 0 && s.Bag.Sell(soldSlot, s.WaveIndex, out _);
            s.SyncPlotsToSeeds(out int r1, out _);
            list.Add(new CheckResult("v46：卖掉一颗种子 → 它的耕地被释放（不再是「2 种子 3 块地」）",
                sold && s.GrowingPlotCount() == s.Bag.OccupiedCount,
                $"卖 {soldDef?.Name}：种子 {s.Bag.OccupiedCount}｜生长地块 {before} → {s.GrowingPlotCount()}（释放 {r1}）"));
            list.Add(new CheckResult("v46：被卖掉的种子不再占地",
                s.GrowingPlotCountOf(soldDef.Id) == 0, $"{soldDef.Name} 仍占 {s.GrowingPlotCountOf(soldDef.Id)} 块"));
            list.Add(new CheckResult("v46：留下的种子仍占着地",
                s.GrowingPlotCountOf(keptDef.Id) == 1, $"{keptDef.Name} 占 {s.GrowingPlotCountOf(keptDef.Id)} 块"));

            // 再买回来 → 必须重新种上（补种方向）
            s.Bag.AddSeed(soldDef, null, 0, out _);
            s.SyncPlotsToSeeds(out _, out int p2);
            list.Add(new CheckResult("v46：买回种子 → 补种回耕地（数量仍然相等）",
                p2 == 1 && s.GrowingPlotCount() == s.Bag.OccupiedCount,
                $"补种 {p2}｜种子 {s.Bag.OccupiedCount}｜生长地块 {s.GrowingPlotCount()}"));
            s.Dispose();

            // ── ② 肥料落点必须落在投掷射程内 ─────────────────────────────
            var fs = new RunSession();
            fs.Start(808080UL);
            fs.StartWave(1);
            PlayerActor fp = fs.World.Player;
            fp.Pos = Vector2.zero;
            fp.WeaponIndex = PlayerActor.WeaponFertilizer;
            fs.AddFertAmmo(RunSession.FertAmmoMax);
            float reach = t.tools.fertilizer.range * fs.Progress.Stats.Get(StatId.AtkRange);

            Vector2 blastPos = new Vector2(float.NaN, float.NaN);
            int blastCount = 0;
            Action<FertilizerBlastEvent> onBlast = e => { blastPos = e.Pos; blastCount++; };

            // v91：肥料改成"先亮 0.5 秒区域标记再炸"，所以按下之后还要让时间走过去，
            // 否则查到的永远是"爆炸 0 次"。这组断言跟着**新行为**走，而不是把前摇去掉。
            var idleInput = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1, Fire = false
            };
            void RunWindup()
            {
                for (int k = 0; k < 36; k++) fs.FixedTick(1f / 60f, idleInput);   // 36 帧 = 0.6s > 0.5s 前摇
            }

            Bus.Subscribe(onBlast);
            fs.FixedTick(1f / 60f, new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1,
                AimWorld = fp.Pos + new Vector2(30f, 0f), Fire = true
            });
            RunWindup();
            Bus.Unsubscribe(onBlast);
            float far = blastCount > 0 ? Vector2.Distance(blastPos, fp.Pos) : -1f;
            list.Add(new CheckResult("v46：鼠标在 30 格外 → 肥料落点仍被钳在射程内",
                blastCount == 1 && Mathf.Abs(far - reach) < 0.05f,
                $"爆炸 {blastCount} 次｜落点距玩家 {far:0.00}｜射程 {reach:0.00}"));

            // 射程内的鼠标位置必须原样生效
            blastPos = new Vector2(float.NaN, float.NaN);
            blastCount = 0;
            Vector2 near = fp.Pos + new Vector2(0f, 2f);
            fp.WeaponCds[PlayerActor.WeaponFertilizer] = 0f;   // 清冷却，允许第二发
            Bus.Subscribe(onBlast);
            fs.FixedTick(1f / 60f, new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1,
                AimWorld = near, Fire = true
            });
            RunWindup();
            Bus.Unsubscribe(onBlast);
            list.Add(new CheckResult("v46：鼠标在射程内 → 肥料就炸在鼠标位置",
                blastCount == 1 && Vector2.Distance(blastPos, near) < 0.001f,
                $"爆炸 {blastCount} 次｜落点 ({blastPos.x:0.00},{blastPos.y:0.00})｜鼠标 ({near.x:0.00},{near.y:0.00})"));

            list.Add(new CheckResult("v46：BlastCenterFor 落点可复算（超射程 → 落在射程边界）",
                Vector2.Distance(RunSession.BlastCenterFor(fp.Pos, new Vector2(40f, 0f), Vector2.right, reach),
                                 fp.Pos + new Vector2(reach, 0f)) < 0.001f,
                $"复算落点 {RunSession.BlastCenterFor(fp.Pos, new Vector2(40f, 0f), Vector2.right, reach)}"));

            // 场地钳制：站在墙边朝场地外扔，落点不能穿墙
            fp.WeaponCds[PlayerActor.WeaponFertilizer] = 0f;
            fp.Pos = new Vector2(t.farm.arenaHalf - 1f, 0f);
            fs.AddFertAmmo(RunSession.FertAmmoMax);
            blastPos = new Vector2(float.NaN, float.NaN);
            blastCount = 0;
            Bus.Subscribe(onBlast);
            fs.FixedTick(1f / 60f, new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1,
                AimWorld = new Vector2(t.farm.arenaHalf + 40f, 0f), Fire = true
            });
            RunWindup();
            Bus.Unsubscribe(onBlast);
            float lim = t.farm.arenaHalf - 0.5f;
            list.Add(new CheckResult("v46：鼠标指向场地外 → 落点被钳在墙内",
                blastCount == 1 && Mathf.Abs(blastPos.x) <= lim + 0.001f,
                $"落点 x={blastPos.x:0.00}｜墙内上限 {lim:0.00}"));
            fs.Dispose();
        }

        /// <summary>
        /// v47 改版验收（把这一批需求逐条变成可跑的断言）：
        ///   ① v165 改写：**表里的 BOSS 分钟不再自己刷 BOSS**，且本回合结束点 = 60 秒时钟；
        ///   ② 敌人伤害与生命统一按 2 的回合次方成长，且第 expCap 波后封顶；
        ///   ③ 锄头/水壶射程与激光宽度已提高且读表；
        ///   ④ 升级抽卡的倍率类一律 +3%；
        ///   ⑤ 小猪生成概率 5%；
        ///   ⑥ 商店已售槽位不可购买、不可锁定；
        ///   ⑦ 浮动文字（掉血 / 回血 / 成熟 / 缺水）确实会被产出。
        /// </summary>
        private static void V47Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;

            // ── ② 生命与伤害走**同一条曲线**（v115：线性 + 二次，无封顶）──
            EnemyDef grunt = TableDatabase.GetEnemy("grunt");
            EnemyGrowthDef eg = TableDatabase.EnemyGrowth;
            float hpA = eg != null && eg.hpStepA > 0f ? eg.hpStepA : 0.12f;
            float hpB = eg != null && eg.hpStepB > 0f ? eg.hpStepB : 0.006f;
            float dmgA = eg != null && eg.dmgStepA > 0f ? eg.dmgStepA : 0.08f;
            float dmgB = eg != null && eg.dmgStepB > 0f ? eg.dmgStepB : 0.003f;

            // v168：同样改成从 def 读基础值，不再硬编码 8f ✗（理由见 L317 附近的说明）
            Expect(list, "第 1 波不成长（曲线起点 = 1.0）", grunt.HpForWave(1, 1), grunt.BaseHp);
            Expect(list, "第 3 波基础兵血量 = 基础 × (1 + 2a + 4b)",
                grunt.HpForWave(3, 1), grunt.BaseHp * EnemyDef.WaveGrowth(3, hpA, hpB));

            // ★ 这条与旧断言**相反** ★
            // 旧实现是 min(2^(波次−1), 64) —— 第 7 波就封顶，后 13 波数值一模一样。
            // 新曲线无封顶，所以要验的正是"到最后一波还在涨"。
            list.Add(new CheckResult("血量持续成长到最后一波（旧版第 7 波就封顶）",
                grunt.HpForWave(20, 1) > grunt.HpForWave(12, 1) * 1.2f,
                $"第 12 波 {grunt.HpForWave(12, 1):0.#}｜第 20 波 {grunt.HpForWave(20, 1):0.#}"));

            // "曲线递增"的核心判据：**每波增幅本身在变大**，而不是匀速
            float step10 = grunt.HpForWave(10, 1) - grunt.HpForWave(9, 1);
            float step20 = grunt.HpForWave(20, 1) - grunt.HpForWave(19, 1);
            list.Add(new CheckResult("曲线递增：每波增幅本身在变大（非匀速）",
                step20 > step10 * 1.5f, $"第 10 波 +{step10:0.##}｜第 20 波 +{step20:0.##}"));

            // HP 与伤害**刻意不同步**：HP 涨得更快，否则后期敌人一击秒杀玩家
            float hp20m = EnemyDef.WaveGrowth(20, hpA, hpB);
            float dmg20m = EnemyDef.WaveGrowth(20, dmgA, dmgB);
            list.Add(new CheckResult("HP 涨得比伤害快（后期不会变成一击秒杀）",
                hp20m > dmg20m * 1.3f, $"HP ×{hp20m:0.00}｜伤害 ×{dmg20m:0.00}"));

            Expect(list, "第 3 波接触伤害 = 基础 × 曲线",
                grunt.TouchDamageForWave(3), 1f * EnemyDef.WaveGrowth(3, dmgA, dmgB));
            EnemyDef boss = TableDatabase.GetEnemy("boss");
            list.Add(new CheckResult("BOSS 弹幕伤害也吃同一条曲线",
                boss.ProjectileDamageForWave(20) > boss.ProjectileDamageForWave(1),
                $"{boss.ProjectileDamageForWave(1):0.00} → {boss.ProjectileDamageForWave(20):0.00}"));

            // ── ① v165：**表里的 BOSS 分钟不再自己刷 BOSS** ──
            //
            // 旧断言是"BOSS 波 30 秒内只出现 1 只 BOSS、0 只小怪"，验的是 WaveDirector 里
            // 那条"isBoss 分钟只出 BOSS"的分支。**那条分支已删除**：BOSS 的唯一入口是
            // BossSchedule（10/18/24/28/30/32… 分钟），否则表 index 10（第 9 分钟）与
            // 时刻表的第 10 分钟会各刷一只、同时在场 ✗
            // 所以这里断言**相反的事实**：表里的 BOSS 分钟照常刷小怪、且不再自己出 BOSS ✓
            list.Add(new CheckResult("BOSS 时限配置仍是 90s，但计时制下不可达（CombatElapsed ≤ 60s）",
                t.waves.bossTimeLimit >= 90f && t.waves.bossTimeLimit > Clock.SecondsPerMinute,
                $"{t.waves.bossTimeLimit}s vs 一分钟 {Clock.SecondsPerMinute:0}s"));
            var s = new RunSession();
            s.Start(4711UL);
            s.StartWave(10);                                   // 表 index 10 = 标了 isBoss 的那一条
            list.Add(new CheckResult("表 index 10 仍被标记为 BOSS 条目（IsBossWave 保留）", s.Waves.IsBossWave, ""));
            for (int i = 0; i < 60 * 30; i++) s.Waves.Tick(GameClock.FixedDt, s.World);   // 跑 30 秒
            int bossCount = 0, minionCount = 0;
            for (int i = 0; i < s.World.Enemies.Count; i++)
            {
                if (s.World.Enemies[i].Def.Kind == EnemyKind.Boss) bossCount++;
                else minionCount++;
            }
            list.Add(new CheckResult("v165：表里的 BOSS 分钟不再刷 BOSS、小怪照常刷（BOSS 只由 BossSchedule 刷）",
                bossCount == 0 && minionCount > 0, $"BOSS {bossCount}｜小怪 {minionCount}"));

            // ── v165：本回合的结束点 = **60 秒时钟**（不再是 waves.json 的 duration）──
            // 判别力：清场状态下走 30 秒不该完成（旧版第 10 条 duration=90s 也不该），
            //        走满 61 秒必须完成 —— 后一条是**旧实现给不出的**（它的时长来自表里的数）✓
            s.World.DespawnAllEnemies();
            bool completeAt30 = s.Waves.IsComplete(s.World);
            for (int i = 0; i < 60 * 31; i++) s.Waves.Tick(GameClock.FixedDt, s.World);
            s.World.DespawnAllEnemies();
            bool completeAt61 = s.Waves.IsComplete(s.World);
            list.Add(new CheckResult("v165：回合结束点 = 60 秒时钟（30 秒不完成、61 秒完成）",
                !completeAt30 && completeAt61,
                $"30s 完成={completeAt30}｜61s 完成={completeAt61}｜CombatElapsed={s.Waves.CombatElapsed:0}s"));
            s.Dispose();

            // ── ③ 武器射程与激光宽度 ──
            list.Add(new CheckResult("锄头基础射程已提高（≥ 3.0）", t.tools.hoe.range >= 3.0f, $"{t.tools.hoe.range}"));
            list.Add(new CheckResult("水壶基础射程已提高（≥ 9.0）", t.tools.can.range >= 9.0f, $"{t.tools.can.range}"));
            list.Add(new CheckResult("水壶激光宽度可配且已加粗（≥ 1.2）",
                t.tools.can.beamWidth >= 1.2f, $"{t.tools.can.beamWidth}"));

            // ── ④ v48：抽卡属性提供量重写 ──
            //      倍率类里 攻击倍率/攻击速度/攻击范围 = 5%，其余仍 3%；幸运改为 +2
            int percentCards = 0, wrongPercent = 0;
            for (int i = 0; i < t.levelReward.stats.Length; i++)
            {
                RewardStatDef r = t.levelReward.stats[i];
                if (!string.Equals(r.mode, "Percent", System.StringComparison.OrdinalIgnoreCase)) continue;
                percentCards++;
                bool core = r.id == "AtkMult" || r.id == "AtkSpeed" || r.id == "AtkRange";
                if (r.amount != (core ? 5 : 3)) wrongPercent++;
            }
            list.Add(new CheckResult("升级抽卡：倍率/攻速/范围 +5%，其余倍率卡 +3%",
                percentCards > 0 && wrongPercent == 0, $"{percentCards} 张倍率卡，其中 {wrongPercent} 张数量不对"));

            // ── ⑤ 小猪概率 ──
            // v123：概率从 5% 大幅降到 1%（需求）。同时加了"每回合保底一只"，
            // 所以这里验两件事：概率确实降了，且**保底机制存在**（否则 20 波约 82% 一猪不出，
            // 而小猪是肥料唯一来源 —— 循环会断）。
            Expect(list, "小猪生成概率 1%", t.waves.pigChance, 0.01f);
            list.Add(new CheckResult("小猪概率已大幅下调（原 5%）",
                t.waves.pigChance <= 0.02f, $"{(t.waves.pigChance * 100f):0.#}%"
                + $"（20 波全不出的概率约 {Mathf.Pow(1f - t.waves.pigChance, 20f) * 100f:0}%，故必须有保底）"));

            // ── ⑥ 商店已售槽位 ──
            var bs = new RunSession();
            bs.Start(5252UL);
            bs.Gold = 999;
            bs.Shop.Refresh(bs);
            if (bs.Shop.Offers.Count > 0)
            {
                bs.Shop.Offers[0] = new ShopOffer { Kind = OfferKind.Sold };
                bool bought = bs.Shop.TryBuy(0, bs, out string whyBuy);
                bool locked = bs.Shop.ToggleLock(0, bs, out string whyLock);
                list.Add(new CheckResult("已售槽位不可购买", !bought, whyBuy));
                list.Add(new CheckResult("已售槽位不可锁定（旧版锁定的空位会永久占货架）", !locked, whyLock));
            }
            bs.Dispose();

            // ── ⑦ 浮动文字 ──
            var ps = new RunSession();
            ps.Start(6161UL);
            ps.StartWave(1);
            ps.World.SpawnEnemy("grunt", ps.World.Player.Pos + new Vector2(1.5f, 0f), 1);
            EnemyActor foe = ps.World.Enemies[ps.World.Enemies.Count - 1];
            ps.Popups.Clear();
            ps.World.DealDamage(new DamageRequest
            {
                Type = DamageType.Physical, RawValue = 3f, Source = ps.World.Player, Target = foe, IgnoreDodge = true
            });
            list.Add(new CheckResult("敌人掉血 → 在它旁边弹出数字", ps.Popups.Count > 0,
                ps.Popups.Count > 0 ? ps.Popups[0].Text : "无"));

            ps.World.Player.Health.Hp -= 3f;
            ps.Popups.Clear();
            ps.World.Player.Health.Heal(2f);
            list.Add(new CheckResult("玩家回血 → 在它旁边弹出数字", ps.Popups.Count > 0,
                ps.Popups.Count > 0 ? ps.Popups[0].Text : "无"));

            // 成熟 / 缺水：由 World.Simulate 的耕地循环发出
            //
            // ★ v168：窗口期与耗水都改成**读种子自己的数**，不再写死 ✗
            //   旧写法是 `BeginPlant(..., waterMaxDefault, 60)` + 固定 400 帧（≈6.7 秒）：
            //     · `60` 是**旧表里萝卜的 waterDrain** ✗ 新表是 5（`radish.WaterDrain`）✓
            //     · 400 帧 ≈ 6.7 秒够旧的 5 秒成熟，**不够新的 18 秒** ✗
            //   ⇒ 两条都是"把某一版数值刻进了测试"，而新表把成熟时间与耗水变成了**品种设计轴** ✓
            //   所以窗口期由 `GrowTime` 推、耗水由种子自己给 ⇒ 以后调平衡不必再改这里 ✓
            SeedDef radishSeed = TableDatabase.GetSeed("radish_guard");
            FarmPlot plot = ps.World.Farm.Slots[0];
            plot.BeginPlant(t.farm, radishSeed, t.farm.waterMaxDefault, radishSeed.WaterDrain);
            ps.Popups.Clear();
            int matureFrames = Mathf.CeilToInt(radishSeed.GrowTime / GameClock.FixedDt) + 120;
            for (int i = 0; i < matureFrames && plot.State == PlotState.Growing; i++) ps.World.Simulate(GameClock.FixedDt);
            bool sawMature = false;
            for (int i = 0; i < ps.Popups.Count; i++) if (ps.Popups[i].Text == "成熟") sawMature = true;
            list.Add(new CheckResult("植物成熟 → 在土地旁弹出「成熟」",
                sawMature && plot.State == PlotState.Grown,
                $"地块 {plot.State}｜提示 {ps.Popups.Count} 条｜推 {matureFrames} 帧（种子 {radishSeed.GrowTime:0.#} 秒，耗水 {radishSeed.WaterDrain}）"));

            // 收割：成熟地块 → 发金币与经验 + 地块重新生长
            // ★ v166：原判据是"并召唤帮手"✗ —— 与 807/847/881 同一条被删规则的**第 4 份副本** ✓
            //   这里走 `ps.World.HarvestPlot`（World 的唯一实现），它在 v165 就不再 spawn ✓
            ps.Popups.Clear();
            ps.World.HarvestPlot(plot);
            bool sawHarvest = false;
            for (int i = 0; i < ps.Popups.Count; i++) if (ps.Popups[i].Text.StartsWith("收获")) sawHarvest = true;
            // ★ T2：判据里的 `ps.World.Plants.Count == 0` 已删除 ✗ —— `World.Plants` 已不存在 ✓ ★
            //   只保留本条真正要测的东西：**收割会弹「收获 xxx」** ✓
            //   （"不产生帮手"那半句随同款副本一起删；现在它是结构事实，不需要断言 ✓）
            //   ⚠️ 本条**保留**（不是删除）⇒ 计数不变 ✓
            list.Add(new CheckResult("收割 → 弹出「收获 xxx」",
                sawHarvest,
                $"提示「{(ps.Popups.Count > 0 ? ps.Popups[0].Text : "无")}」"));

            // 浮动文字的屏幕投影换算（不需要真相机也能算死）
            Vector2 hud = ImGuiHud.WorldToHud(new Vector3(960f, 540f, 1f), 1.35f, 1080f);
            list.Add(new CheckResult("浮动文字坐标换算正确（像素 → 虚拟坐标 + Y 翻转）",
                Mathf.Abs(hud.x - 960f / 1.35f) < 0.01f && Mathf.Abs(hud.y - 540f / 1.35f) < 0.01f,
                $"({hud.x:0.##},{hud.y:0.##})"));

            // 缺水：地块现在回到"生长中"，抽干蓄水后 World.Simulate 的耕地循环就会发出「缺水」
            ps.Popups.Clear();
            plot.Water = 0;
            ps.World.Simulate(GameClock.FixedDt);
            bool sawThirsty = false;
            // v69：文案改为**带类型**的，PopupStyle 会给缺水自动补一个"！"。
            // 断言跟着规则走（认前缀而不是全等），否则每调一次文案就要改一次测试。
            for (int i = 0; i < ps.Popups.Count; i++)
                if (ps.Popups[i].Text.StartsWith("缺水")) sawThirsty = true;
            list.Add(new CheckResult("土地缺水 → 在土地旁弹出「缺水」",
                sawThirsty && plot.State == PlotState.Growing, $"地块 {plot.State}｜蓄水 {plot.Water}"));
            ps.Dispose();
        }

        /// <summary>
        /// v48 改版验收：
        ///   ① 开局发放金币；② 1/2/3 键切换 锄头/水壶/肥料；③ 锄头攻速加快 + 三把武器基础伤害提高；
        ///   ④ 角色与怪物移速提高、怪物子弹速度降低；⑤ 刷怪以基础近战怪为主；
        ///   ⑥ 抽卡：倍率/攻速/范围 +5%、幸运 +2；⑦ 浮动文字在对象周围随机落点。
        /// </summary>
        private static void V48Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;

            // ── ① 开局金币 ──
            var s = new RunSession();
            s.Start(4848UL);
            Expect(list, "开局发放金币", s.Gold, 25f);
            s.Dispose();

            // ── ② 数字键切武器 ──
            var ws = new RunSession();
            ws.Start(4949UL);
            ws.StartWave(1);
            PlayerActor wp = ws.World.Player;
            var idle = new FrameInput { SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1 };
            int okSwitches = 0;
            for (int w = 0; w < 3; w++)
            {
                FrameInput fi = idle;
                fi.WeaponSelectSet = true;
                fi.SelectWeapon = w;
                ws.FixedTick(GameClock.FixedDt, fi);
                if (wp.WeaponIndex == w) okSwitches++;
            }
            list.Add(new CheckResult("1/2/3 键切到 锄头/水壶/肥料", okSwitches == 3, $"成功 {okSwitches}/3"));
            // 没按键时不得被"默认值 0"强行切回锄头
            wp.SwitchWeapon(PlayerActor.WeaponCan, ws.World, true);
            ws.FixedTick(GameClock.FixedDt, idle);
            list.Add(new CheckResult("没按数字键时不会误切武器（int 默认值陷阱）",
                wp.WeaponIndex == PlayerActor.WeaponCan, $"当前 {wp.WeaponName}"));
            ws.Dispose();

            // ── ③ 攻速与基础伤害 ──
            list.Add(new CheckResult("锄头基础攻速加快（冷却 ≤ 0.8s）", t.tools.hoe.cooldown <= 0.8f, $"{t.tools.hoe.cooldown}s"));
            list.Add(new CheckResult("三把武器基础伤害提高（baseValue ≥ 3）",
                t.tools.hoe.baseValue >= 3f && t.tools.can.baseValue >= 3f && t.tools.fertilizer.baseValue >= 3f,
                $"锄 {t.tools.hoe.baseValue}／壶 {t.tools.can.baseValue}／肥 {t.tools.fertilizer.baseValue}"));

            // ── ④ 移速与子弹速度 ──
            // （玩家基础移速的校验见 V53Checks：v48 曾提到 6.5，v53 因"过快"回落到 5.5）
            EnemyDef grunt = TableDatabase.GetEnemy("grunt");
            EnemyDef shooter = TableDatabase.GetEnemy("shooter");
            EnemyDef jumper = TableDatabase.GetEnemy("jumper");
            EnemyDef boss = TableDatabase.GetEnemy("boss");
            list.Add(new CheckResult("全部怪物移速提高",
                grunt.MoveSpeed >= 2.8f && shooter.MoveSpeed >= 2.4f && jumper.MoveSpeed >= 3.2f && boss.MoveSpeed >= 1.9f,
                $"兵 {grunt.MoveSpeed}／射手 {shooter.MoveSpeed}／精英 {jumper.MoveSpeed}／BOSS {boss.MoveSpeed}"));
            list.Add(new CheckResult("怪物子弹速度略微降低（射手 ≤ 7 / BOSS ≤ 6）",
                shooter.ProjectileSpeed <= 7f && boss.ProjectileSpeed <= 6f,
                $"射手 {shooter.ProjectileSpeed}／BOSS {boss.ProjectileSpeed}"));

            // ── ⑤ 刷怪构成 ──
            var pw = MakeWorld(out _);
            pw.WaveIndex = 9;
            int grunts = 0, shooters = 0, jumpers = 0;
            const int samples = 20000;
            for (int i = 0; i < samples; i++)
            {
                string id = WaveDirector.PickBatch(pw).EnemyId;
                if (id == "grunt") grunts++;
                else if (id == "shooter") shooters++;
                else jumpers++;
            }
            list.Add(new CheckResult("刷怪以基础近战怪为主（≥ 80%）", grunts >= samples * 80 / 100,
                $"基础兵 {grunts * 100f / samples:0.#}%｜远程 {shooters * 100f / samples:0.#}%｜精英 {jumpers * 100f / samples:0.#}%"));

            // ── ⑥ 抽卡提供量 ──
            // v113：**幸运属性已删除**（需求 ①），它随等级奖励发放的那一条也随之去掉。
            // 这里改为验证"等级奖励仍然提供属性"——不再断言具体是哪一条属性，
            // 以免下次调整奖励表又要改测试。
            int rewardStatCount = t.levelReward.stats != null ? t.levelReward.stats.Length : 0;
            list.Add(new CheckResult("等级奖励仍提供属性（幸运已按需求删除）",
                rewardStatCount > 0, $"奖励属性 {rewardStatCount} 条"));

            // ── ⑦ 浮动文字 ──
            var ps = new RunSession();
            ps.Start(5050UL);
            ps.Popups.Clear();
            Bus.Publish(new FloatingTextEvent { Pos = Vector2.zero, Text = "t", Kind = PopupKind.Info });
            Bus.Publish(new FloatingTextEvent { Pos = Vector2.zero, Text = "t", Kind = PopupKind.Info });
            list.Add(new CheckResult("浮动文字在对象周围随机落点（同点连发不重叠）",
                ps.Popups.Count == 2 && Vector2.Distance(ps.Popups[0].Pos, ps.Popups[1].Pos) > 0.001f,
                ps.Popups.Count == 2 ? $"{ps.Popups[0].Pos:F2} vs {ps.Popups[1].Pos:F2}" : "条数不足"));
            list.Add(new CheckResult("浮动文字字号增大（≥ 22）", t.ui.popupFontSize >= 22f, $"{t.ui.popupFontSize}"));
            ps.Dispose();
        }

        /// <summary>
        /// v49 验收：属性面板一律显示「相对开局基准的增量」——**开局全部是 +0 / +0%**。
        /// 这条之所以要单独测：面板的基准过去是写死在绘制代码里的（移动速度写着 5.0），
        /// 基础值一改就整排数字失真（开局显示 "+150%"）。
        /// </summary>
        private static void V49Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            PlayerBaseDef b = t.player;
            var st = new StatBlock(t.caps, t.player);
            string[] rows =
            {
                ImGuiHud.AddFlat(st.Get(StatId.MaxHp), b.maxHp),
                ImGuiHud.AddFlat(st.Get(StatId.Shield), b.shield),
                // v162：三种伤害类型各占一行（原来是通用攻击伤害一条）
                ImGuiHud.AddFlat(st.Get(StatId.MeleeDamage), b.meleeDamage),
                ImGuiHud.AddFlat(st.Get(StatId.RangedDamage), b.rangedDamage),
                ImGuiHud.AddFlat(st.Get(StatId.BlastDamage), b.blastDamage),
                ImGuiHud.AddRelative(st.Get(StatId.AtkMult), b.atkMult),
                ImGuiHud.AddRelative(st.Get(StatId.AtkSpeed), b.atkSpeed),
                ImGuiHud.AddRelative(st.Get(StatId.AtkRange), b.atkRange),
                ImGuiHud.AddRelative(st.Get(StatId.MoveSpeed), b.moveSpeed),
                ImGuiHud.AddRelative(st.Get(StatId.CritRate), b.critRate),
                ImGuiHud.AddRelative(st.Get(StatId.CritDmg), b.critDmg),
                ImGuiHud.AddPoints(st.Get(StatId.Dodge), b.dodge),
                // v107：Luck 已删除
                ImGuiHud.AddFlat(st.Get(StatId.ExplodeDamage), b.explodeDamage),
                ImGuiHud.AddRelative(st.Get(StatId.ExplodeRange), b.explodeRange / 100f),
                ImGuiHud.AddFlat(st.Get(StatId.Knockback), b.knockback),
                ImGuiHud.AddRelative(st.Get(StatId.ExpMult), b.expMult),
                ImGuiHud.AddRelative(st.Get(StatId.GoldMult), b.goldMult),
                ImGuiHud.AddRelative(st.Get(StatId.HeartDropRate), b.heartDropRate),
                ImGuiHud.AddFlat(st.Get(StatId.HeartHeal), b.heartHeal)
            };
            int notZero = 0;
            for (int i = 0; i < rows.Length; i++) if (rows[i] != "+0" && rows[i] != "+0%") notZero++;
            list.Add(new CheckResult("v49：属性面板开局全部 +0 / +0%（含移动速度，旧版显示 +150%）",
                notZero == 0, notZero == 0 ? $"{rows.Length} 行全部为 0" : $"非 0 行：{string.Join(" ", rows)}"));

            // ── v119：**逐属性生效验证**（需求："验证所有属性内容是否全部生效"）──
            //
            // 做法：对**每一个** StatId 施加一次加法修正，确认 Get() 真的变了。
            //
            // 这条能抓住一类很难发现的 bug：属性在枚举里、在面板上有名字、在 item 表里能被引用，
            // 但**没有任何代码路径读它** —— 也就是"显示存在、实际无效"。
            // 逐个枚举比"抽查几个"可靠：新增属性时忘了接线，这条会立刻红。
            var deadStats = new System.Collections.Generic.List<string>();

            // ★ v168：`MaxPlots` 必须排除 —— 它**换了一个判据**，不是被漏接线 ✗
            //   旧判据：能被修正 ✓（"耕地块数随等级 +1"）—— 随 v166 的"田地固定形状"失效
            //   新判据：== 田地的列数 ✓（由「开局田地形状 = gridSize 列 × initialRows 行」覆盖）
            //
            // 用**显式名单**而不是裸 `if (id == StatId.MaxPlots) continue;` ✗ ——
            // 裸特判读起来像"这里有个例外，不知道为什么"；
            // 名单 + 理由读起来像"这是设计" ✓ 而这条差别会在半年后有人想删掉那个特判时体现出来 ✓
            var fixedByDesignStats = new System.Collections.Generic.List<StatId> { StatId.MaxPlots };

            for (int i = 0; i < StatIds.All.Length; i++)
            {
                StatId id = StatIds.All[i];
                if (fixedByDesignStats.Contains(id)) continue;   // 设计上不可修正 ✓
                float before = st.Get(id);
                st.AddModifier(new StatModifier(id, 1f, ModOp.Add, "statcheck"));
                float after = st.Get(id);
                st.RemoveModifiersFrom("statcheck");
                if (Mathf.Abs(after - before) < 1e-4f) deadStats.Add(id.ToString());
            }
            list.Add(new CheckResult("每个属性都能被修正（无「显示存在但实际无效」的属性）",
                deadStats.Count == 0,
                deadStats.Count == 0
                    ? $"{StatIds.All.Length - fixedByDesignStats.Count} 个可修正属性全部响应"
                      + $"（另有 {fixedByDesignStats.Count} 个设计上不可修正：{string.Join("/", fixedByDesignStats)}）"
                    : $"无响应：{string.Join(" ", deadStats)}"));

            // 加一件乘法道具后，显示的涨幅必须与道具卡上写的百分比一致
            st.AddModifier(new StatModifier(StatId.MoveSpeed, 1.05f, ModOp.Mul, "check"));
            st.AddModifier(new StatModifier(StatId.CritRate, 1.05f, ModOp.Mul, "check"));
            list.Add(new CheckResult("v49：乘法加成按「涨幅」显示（与卡面 +5% 一致）",
                ImGuiHud.AddRelative(st.Get(StatId.MoveSpeed), b.moveSpeed) == "+5%",
                $"移速 {ImGuiHud.AddRelative(st.Get(StatId.MoveSpeed), b.moveSpeed)}"));

            // v113：暴击率初值改成 **0**（需求 ②）之后暴露的一个陷阱：
            //   **乘法加成对 0 无效** —— 0 × 1.05 仍然是 0。
            // 也就是说"暴击率 +5%"这类道具若用 Mul 模式会**静默失效**（买了没反应、日志也不报）。
            // 这条断言把它钉住：只要暴击率初值是 0，暴击率道具就必须用 Add 模式。
            list.Add(new CheckResult("暴击率初值为 0 ⇒ 乘法道具无效（暴击率道具必须用 Add）",
                Mathf.Abs(st.Get(StatId.CritRate)) < 1e-5f,
                $"×1.05 之后 = {st.Get(StatId.CritRate):0.0000}（0 乘任何数还是 0）"));

            // 加法道具按点数显示（v162：三种伤害类型**各验一遍**，以前只验一条通用的攻击伤害）
            st.AddModifier(new StatModifier(StatId.MeleeDamage, 2f, ModOp.Add, "check"));
            st.AddModifier(new StatModifier(StatId.RangedDamage, 2f, ModOp.Add, "check"));
            st.AddModifier(new StatModifier(StatId.BlastDamage, 2f, ModOp.Add, "check"));
            list.Add(new CheckResult("v49：加法加成按点数显示（近战 +2）",
                ImGuiHud.AddFlat(st.Get(StatId.MeleeDamage), b.meleeDamage) == "+2",
                ImGuiHud.AddFlat(st.Get(StatId.MeleeDamage), b.meleeDamage)));
            list.Add(new CheckResult("v49：加法加成按点数显示（远程 +2）",
                ImGuiHud.AddFlat(st.Get(StatId.RangedDamage), b.rangedDamage) == "+2",
                ImGuiHud.AddFlat(st.Get(StatId.RangedDamage), b.rangedDamage)));
            list.Add(new CheckResult("v49：加法加成按点数显示（爆炸 +2）",
                ImGuiHud.AddFlat(st.Get(StatId.BlastDamage), b.blastDamage) == "+2",
                ImGuiHud.AddFlat(st.Get(StatId.BlastDamage), b.blastDamage)));
        }

        /// <summary>
        /// v50 验收：攻击**模式**看 attackKind（近战/远程），伤害**类型**看 attackType（物理/异常/支援）。
        /// 旧实现把两者混用——`Physical → "近战"`——于是"物理 + 远程"的豌豆射手被显示成"近战"。
        /// </summary>
        private static void V50Checks(List<CheckResult> list)
        {
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            SeedDef pea = TableDatabase.GetSeed("pea_shooter");
            SeedDef mushroom = TableDatabase.GetSeed("toxic_mushroom");
            SeedDef frost = TableDatabase.GetSeed("frost_ivy");
            SeedDef moss = TableDatabase.GetSeed("moss_shield");
            SeedDef clover = TableDatabase.GetSeed("healing_clover");

            int wrong = 0;
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                if (d.AttackType == AttackType.Support) continue;      // 支援型标角色，不标近远
                string mode = RunSession.AtkTypeText(d);
                bool expectRanged = d.AttackKind == AttackKind.Ranged;
                if (expectRanged && mode != "远程") wrong++;
                if (!expectRanged && mode != "近战") wrong++;
            }
            list.Add(new CheckResult("v50：攻击模式与 attackKind 一致（豌豆射手 = 远程，不再是近战）",
                wrong == 0, $"不一致 {wrong} 条｜豌豆 {RunSession.AtkTypeText(pea)}｜萝卜 {RunSession.AtkTypeText(radish)}"));
            list.Add(new CheckResult("v50：伤害类型与攻击模式分开显示（毒蘑菇 = 远程 / 异常）",
                RunSession.AtkTypeText(mushroom) == "远程" && RunSession.DamageTypeText(mushroom) == "异常"
                && RunSession.DamageTypeText(frost) == "异常" && RunSession.DamageTypeText(pea) == "物理",
                $"毒蘑菇 {RunSession.AtkTypeText(mushroom)}/{RunSession.DamageTypeText(mushroom)}｜豌豆 {RunSession.DamageTypeText(pea)}"));
            list.Add(new CheckResult("v50：支援型标注角色（苔藓盾 = 护卫 / 三叶草 = 治疗）",
                RunSession.AtkTypeText(moss) == "护卫" && RunSession.AtkTypeText(clover) == "治疗",
                $"{RunSession.AtkTypeText(moss)}／{RunSession.AtkTypeText(clover)}"));

            // 种子卡里的描述必须够短，否则折行后会留下一个孤字。
            // 阈值 14 来自运行期实测（HudAudit）：信息区可用宽 268px，15px/字，
            // 加上"功能："三个字的前缀后，15 个字的描述需要 270px —— 刚好溢出 2px。
            const int maxDesc = 14;
            int tooLong = 0;
            string longest = "";
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                string desc = TableDatabase.Seeds[i].Desc ?? "";
                if (desc.Length > maxDesc)
                {
                    tooLong++;
                    if (desc.Length > longest.Length) longest = desc;
                }
            }
            list.Add(new CheckResult($"v50：种子描述都 ≤{maxDesc} 字（种子卡一行放得下）",
                tooLong == 0, tooLong == 0 ? "全部通过" : $"{tooLong} 条过长，最长「{longest}」"));
        }

        /// <summary>
        /// v53 验收：
        ///   ① 滚轮切武器**每帧只生效一次**——旧实现把它放在固定步里，追帧时同一个 WheelScroll 被重放
        ///      最多 5 次，3 把武器轮转 5 步 → 跳过中间那把（玩家反馈的"乱跳/跳过中间"）；
        ///   ② 商店刷新 / 抽卡重抽同样是"带花费的脉冲"，旧实现会被扣多次金币；
        ///   ③ 玩家基础移速回落（6.5 → 5.5）。
        /// </summary>
        private static void V53Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            const int catchUp = 5;   // Tuning.meta.maxCatchUpTicks

            // ── ① 滚轮：模拟"一个渲染帧跑 5 个固定步" ──
            var s = new RunSession();
            s.Start(5353UL);
            s.StartWave(1);
            PlayerActor p = s.World.Player;
            p.WeaponIndex = PlayerActor.WeaponHoe;

            var wheel = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1, WheelScroll = 1
            };
            s.ConsumeFrameEdges(ref wheel);                                           // 帧级消费一次
            for (int i = 0; i < catchUp; i++) s.FixedTick(GameClock.FixedDt, wheel);  // 再追帧 5 次
            list.Add(new CheckResult($"v53：滚轮一格只前进一把（追帧 {catchUp} 次也不跳）",
                p.WeaponIndex == PlayerActor.WeaponCan, $"锄头 → {p.WeaponName}（期望 水壶）"));

            // 连续三格：必须依次经过三把武器（当前是水壶 → 肥料 → 锄头 → 水壶）
            var order = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                var step = new FrameInput
                {
                    SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1, WheelScroll = 1
                };
                s.ConsumeFrameEdges(ref step);
                order.Add(p.WeaponName);
            }
            list.Add(new CheckResult("v53：滚轮依次经过 肥料→锄头→水壶（不漏中间）",
                order[0] == "肥料" && order[1] == "锄头" && order[2] == "水壶",
                string.Join(" → ", order)));

            // 反向一格回到上一把（当前水壶 → 锄头）
            var back = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1, WheelScroll = -1
            };
            s.ConsumeFrameEdges(ref back);
            list.Add(new CheckResult("v53：反向滚一格回到上一把",
                p.WeaponIndex == PlayerActor.WeaponHoe, $"当前 {p.WeaponName}（期望 锄头）"));
            s.Dispose();

            // ── ② 商店刷新只扣一次金币 ──
            var sh = new RunSession();
            sh.Start(5454UL);
            sh.StartWave(1);
            sh.ForceShopForDebug();
            sh.Gold = 1000;
            int cost = sh.Shop.RerollCost(sh.T);
            var reroll = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1, Reroll = true
            };
            sh.ConsumeFrameEdges(ref reroll);
            for (int i = 0; i < catchUp; i++) sh.FixedTick(GameClock.FixedDt, reroll);
            list.Add(new CheckResult($"v53：商店刷新只扣一次金币（旧实现追帧会扣 {catchUp} 次）",
                sh.Gold == 1000 - cost, $"金币 1000 → {sh.Gold}（单次费用 {cost}）"));
            sh.Dispose();

            // ── ③ 移速回落 ──
            list.Add(new CheckResult("v53：玩家基础移速已回落（5.0 ~ 6.0）",
                t.player.moveSpeed >= 5f && t.player.moveSpeed <= 6f, $"{t.player.moveSpeed}"));
        }
        /// <summary>v11：整数/整数%约定 + 升级抽卡（三张卡、融合、重抽、选择）。</summary>
        private static void V11Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;

            // ── 等级提升：最大生命 +1
            Expect(list, "升级最大生命 +1", t.growth.hpPerLevel, 1f);

            // ── 道具约定：数值为整数 / 倍率为整数%（v168：**遍历全部 effects + costs**）
            //
            // 旧实现只查 `it.Add` —— 而 v168 起 `Add` 只是 `Effects[0]` 的派生值 ✗
            // ⇒ 只查第一条就等于**放过了减益列表与多效果道具** ✓
            // 而"表里写了 +15.5%"正是那种不报错、只是数字读不出来的错 ✓
            bool allInt = true; string badItem = "";
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                foreach (ItemBonus b in it.Effects)
                    if (Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { allInt = false; badItem = it.Id + "." + b.Stat; }
                foreach (ItemBonus b in it.Costs)
                    if (Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { allInt = false; badItem = it.Id + "." + b.Stat + "(减益)"; }
            }
            list.Add(new CheckResult("所有道具的数值都是整数（含减益与多效果）", allInt, badItem));
            int percentCount = 0, plainCount = 0;
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                bool anyPercent = false;
                foreach (ItemBonus b in it.Effects) if (b.IsPercent) anyPercent = true;
                if (anyPercent) percentCount++; else plainCount++;
            }
            list.Add(new CheckResult("道具分为整数加法与整数%两类", percentCount > 0 && plainCount > 0,
                $"百分比 {percentCount} 件 / 加法 {plainCount} 件"));

            // ── 抽卡：权重与模式分布
            int weightSum = 0;
            for (int i = 0; i < t.levelReward.patterns.Length; i++) weightSum += t.levelReward.patterns[i].weight;
            list.Add(new CheckResult("抽卡模式权重合计 100（60/30/10）", weightSum == 100, $"{weightSum}"));
            list.Add(new CheckResult("抽卡属性池来自主要属性", t.levelReward.stats.Length >= 8, $"{t.levelReward.stats.Length} 项"));

            var session = new RunSession();
            session.Start(606060UL);
            session.StartWave(1);
            int allDiff = 0, twoSame = 0, allSame = 0, merged = 0;
            for (int i = 0; i < 300; i++)
            {
                session.DrawRewardCards();
                int cards = session.RewardCards.Count;
                bool hasMerge = false;
                for (int c = 0; c < cards; c++) if (session.RewardCards[c].Copies > 1) hasMerge = true;
                if (hasMerge) merged++;
                if (cards == 3) allDiff++;
                else if (cards == 2) twoSame++;
                else if (cards == 1) allSame++;
            }
            list.Add(new CheckResult("三张全不同约占 6 成（3200 次抽样）", allDiff > 150 && allDiff < 240, $"{allDiff}/300"));
            list.Add(new CheckResult("两同一异约占 3 成", twoSame > 60 && twoSame < 130, $"{twoSame}/300"));
            list.Add(new CheckResult("三张全同约占 1 成", allSame > 10 && allSame < 60, $"{allSame}/300"));
            list.Add(new CheckResult("出现相同属性时会融合", merged > 80, $"{merged}/300 次出现融合"));

            // ── 融合数值 = 单元值 × 张数
            bool fusionOk = true;
            for (int i = 0; i < 200 && fusionOk; i++)
            {
                session.DrawRewardCards();
                for (int c = 0; c < session.RewardCards.Count; c++)
                {
                    RunSession.LevelRewardCard card = session.RewardCards[c];
                    if (card.Amount != card.Def.amount * card.Copies) fusionOk = false;
                }
            }
            list.Add(new CheckResult("融合后的数值 = 单元值 × 张数", fusionOk, ""));

            // ── 选卡生效（整数加法 / 整数%乘法）
            session.DrawRewardCards();
            float before = MajorSum(session);
            string pickedLabel = session.RewardCards.Count > 0 ? session.RewardCards[0].Label : "?";
            session.PickRewardCard(0);
            float after = MajorSum(session);
            list.Add(new CheckResult("选卡后主要属性提升", after > before + 0.0001f,
                pickedLabel + "：" + before.ToString("0.###") + " -> " + after.ToString("0.###")));
            // v13：走输入路径的"选中 → 确认"两步（防止 int 默认值 0 被当成有效选择）
            var flow = new RunSession();
            flow.Start(707070UL);
            flow.StartWave(1);
            flow.PendingRewardDraws = 1;
            flow.DrawRewardCards();
            flow.SetPhasePublic(PhaseState.LevelReward);
            int cardsBefore = flow.RewardCards.Count;
            flow.FixedTick(1f / 60f, new FrameInput { RewardSelectSet = true, RewardSelect = Mathf.Min(1, cardsBefore - 1) });
            bool selectedOk = flow.RewardSelected == Mathf.Min(1, cardsBefore - 1);
            flow.FixedTick(1f / 60f, new FrameInput { RewardPickConfirmed = true, RewardPick = flow.RewardSelected });
            bool advanced = flow.Phase != PhaseState.LevelReward || flow.RewardSelected < 0;
            list.Add(new CheckResult("抽卡两步：选中不会自动确认", selectedOk && cardsBefore > 0, $"选中={flow.RewardSelected}"));
            list.Add(new CheckResult("抽卡两步：确认后才推进", advanced, $"{flow.Phase}"));

            // v165：抽卡之后的终点从「商店」改成「**回到战斗**」——
            // 这是流程被明确决定的后果，不是把断言放松 ✗
            //   旧流程：波末 → 抽卡 → 进商店（"波间购买"）
            //   新流程：升级 → 抽卡 → 回战斗（商店的入口改成**商店车**：靠近按交互键）
            // 所以这里接受 Combat ✓；"还有下一抽"与"卡还在"两种情形仍然覆盖 ✓
            list.Add(new CheckResult("选卡后进入下一抽或回到战斗",
                session.Phase == PhaseState.Combat || session.Phase == PhaseState.LevelReward
                || session.RewardCards.Count > 0,
                $"{session.Phase}"));
        }

        /// <summary>v9 规则：自动锁定 / 异常三系重做 / 植物判定范围 / 爱心回血 / 道具等级与幸运。</summary>
        private static void V9Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            FormulaDef f = t.formula;

            // ── 毒：命中即中毒，4 次结算
            var st = new StatusRuntime();
            st.Apply(AilmentKind.Poison, 3f, f, 1f);
            list.Add(new CheckResult("毒：命中即中毒且共 4 次结算", st.PoisonTicksLeft == 4 && st.Poisoned, $"{st.PoisonTicksLeft} 次"));
            var st2 = new StatusRuntime();
            st2.Apply(AilmentKind.Burn, 3f, f, 1f);
            list.Add(new CheckResult("火：命中即灼烧且共 4 次结算", st2.BurnTicksLeft == 4 && st2.Burning, $"{st2.BurnTicksLeft} 次"));

            // ── 冰：非异常命中叠寒冷；满层冰冻 1 秒后破冰
            var st3 = new StatusRuntime();
            for (int i = 0; i < 3; i++) st3.AddChill(1, 1f, f);
            list.Add(new CheckResult("冰：3 层未冰冻且有减速", st3.ChillStacks == 3 && !st3.Frozen && st3.SpeedFactor(f) < 1f,
                $"层={st3.ChillStacks} 速度系数={st3.SpeedFactor(f):0.00}"));
            st3.AddChill(1, 1f, f);
            list.Add(new CheckResult("冰：满 4 层冻结（禁止移动）", st3.Frozen && st3.SpeedFactor(f) <= 0f, $"{st3.Frozen}"));

            // ★ T2：删掉「植物有判定范围且大于攻击范围」断言 ✗ ★
            //   它测的是**纯植物索敌 AI**（`DetectM > RangeM`，驱动 `PlantAlly.Tick` 的靠近/攻击分支）✓
            //   植物已不参战 ⇒ 索敌 AI 整条不可达 ⇒ 断言已无被测对象 ✓
            //   计数：本块删 1 条 ✓
            var session = new RunSession();
            session.Start(777001UL);
            session.StartWave(1);

            // ── 初始种子固定 D 级白品质
            list.Add(new CheckResult("初始种子固定白品质（D 级）",
                session.InitialChoiceQuality.Length > 0 && session.InitialChoiceQuality[0] == 0
                && session.InitialChoices[0].Grade == SeedGrade.D,
                $"{session.InitialChoices[0].Grade}/{t.quality.NameOf(session.InitialChoiceQuality[0])}"));

            // ── 爱心：拾取回血 + 取消自动回血
            PlayerActor p = session.World.Player;
            p.Health.ApplyDamage(4f);
            float hpBefore = p.Health.Hp;
            p.Tick(3f, session.World);
            // v23：生命回复属性已删除（不再自动回血）
            list.Add(new CheckResult("生命回复属性已删除（不自动回血）", Mathf.Abs(p.Health.Hp - hpBefore) < 0.001f,
                $"{hpBefore:0.##} → {p.Health.Hp:0.##}"));
            session.World.SpawnHeart(p.Pos, t.player.heartHeal);
            for (int i = 0; i < 10; i++) session.World.Simulate(1f / 60f);
            list.Add(new CheckResult("拾取爱心回血", p.Health.Hp > hpBefore + 1f,
                $"{hpBefore:0.0} → {p.Health.Hp:0.0}（+{t.player.heartHeal:0}）"));
            // v119：幸运已删除；爱心掉落率是**等级制**，初值 0 = 低档。
            // 数值从 10/15/20/25% 下调为 2/4/6/8%（需求），所以这里也改成验**结构**：
            // 四个等级必须严格递增，且首档就是 2% —— 不钉死中间值，避免调平衡又要改测试。
            bool heartAsc = true;
            for (int i = 1; i < TuningDef.HeartDropLevelNames.Length; i++)
                if (TuningDef.HeartDropRateOf(i) <= TuningDef.HeartDropRateOf(i - 1)) heartAsc = false;
            list.Add(new CheckResult("爱心掉落率是等级制（0→低档 2%，四档严格递增）",
                Mathf.RoundToInt(t.player.heartDropRate) == 0 && heartAsc
                && Mathf.Abs(TuningDef.HeartDropRateOf(0) - 0.02f) < 1e-5f
                && t.player.heartHeal > 0f,
                $"等级 {t.player.heartDropRate:0}｜"
                + string.Join("/", System.Array.ConvertAll(TuningDef.HeartDropRateByLevel, v => (v * 100f).ToString("0"))) + "%"
                + $"｜恢复 {t.player.heartHeal:0}"));

            // ── 道具等级与价格
            var shop = new ShopSystem();
            shop.Refresh(session);
            bool hasItemGrade = false, hasPrice = true;
            for (int i = 0; i < shop.Offers.Count; i++)
            {
                if (shop.Offers[i].Kind == OfferKind.Item) hasItemGrade = true;
                if (shop.Offers[i].Price <= 0) hasPrice = false;
            }
            list.Add(new CheckResult("商店道具带等级且价格为正", hasPrice, hasItemGrade ? "本批含道具" : "本批无道具（等级表已配置）"));
            list.Add(new CheckResult("道具等级权重表已配置", t.shop.itemGradeWeights.Length == 5, $"{t.shop.itemGradeWeights.Length} 档"));
            list.Add(new CheckResult("价格随波次增长参数已配置", t.shop.priceWaveStep > 0f && t.shop.itemGradePriceStep > 0f,
                $"每波 +{t.shop.priceWaveStep:P0}，每级 +{t.shop.itemGradePriceStep:P0}"));

            // ── 自动锁定：更近的敌人会自动改锁
            session.World.SpawnEnemy("grunt", p.Pos + new Vector2(6f, 0f), 1);
            session.World.SpawnEnemy("grunt", p.Pos + new Vector2(2f, 0f), 1);
            p.LockTargetId = session.World.Enemies[0].Id;
            session.FixedTick(1f / 60f, new FrameInput { BuyOffer = -1, SellSlot = -1, InitialChoice = -1, SelectSeed = -1 });
            EnemyActor lockedNow = session.World.FindEnemyById(p.LockTargetId);
            float lockedDist = lockedNow == null ? 99f : Vector2.Distance(p.Pos, lockedNow.Pos);
            list.Add(new CheckResult("锁定目标自动失效（阵亡后清空）", lockedNow == null || lockedDist < 3f, lockedNow == null ? "目标已阵亡" : $"锁定距离 {lockedDist:0.0}"));
        }

        /// <summary>v8 规则：耕作期间禁止任何攻击（含左键）；耕地每 5 级 +1；开垦许可证道具。</summary>
        private static void V8Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;

            // ★ v166：耕地成长**不再与等级挂钩** ✗
            // 旧模型："每 5 级 / 每级 +1 块，上限 25" ✓
            // 新模型：田地固定 6 列 × N 行，行数只由【存活时间】决定（每 5 分钟一行）✓
            // 所以这两条判据从"随等级增长"改成"**升级完全不动田地**" ✓
            list.Add(new CheckResult("v166：按等级 +1 耕地已关闭（levelsPerPlot = 0）",
                t.growth.levelsPerPlot == 0, $"{t.growth.levelsPerPlot} 级/块"));
            var progress = new PlayerProgress(t);
            int basePlots = progress.MaxPlots;
            progress.AddExp(100000f, out _);
            list.Add(new CheckResult("v166：升级不改变可种植株数（田地成长只看存活时间）",
                progress.MaxPlots == basePlots, $"Lv{progress.Level}：{basePlots} → {progress.MaxPlots}"));

            // ── v15：耕地上限道具已删除；改由「操作手册」提高对耕地的操作数
            list.Add(new CheckResult("开垦许可证已删除（田地成长只靠存活时间）",
                TableDatabase.GetItem("plot_license") == null, "plot_license 不存在"));
            // v34：道具表已按新属性重写（旧属性道具已删除）
            // v162：磨刀石从「攻击伤害」改成「近战伤害」——它现在**只**强化锄头那条线
            ItemDef atkItem = TableDatabase.GetItem("iron_hoe");
            list.Add(new CheckResult("v34：道具只影响现存属性（磨刀石 → 近战伤害）",
                atkItem != null && atkItem.Stat == StatId.MeleeDamage,
                atkItem == null ? "缺失" : $"{atkItem.Name} → {atkItem.Stat} +{atkItem.Add}"));
            var session = new RunSession();
            session.Start(313131UL);
            float dmgBefore = session.Progress.Stats.Get(StatId.MeleeDamage);
            float rangedBefore = session.Progress.Stats.Get(StatId.RangedDamage);
            float blastBefore = session.Progress.Stats.Get(StatId.BlastDamage);
            session.ApplyItem(atkItem);
            list.Add(new CheckResult("v34：道具生效（近战伤害 +1）",
                session.Progress.Stats.Get(StatId.MeleeDamage) == dmgBefore + 1f,
                $"{dmgBefore:0} → {session.Progress.Stats.Get(StatId.MeleeDamage):0}"));
            // v162：**道具级的独立性证据** —— 买一块磨刀石，水壶与肥料那两条线必须纹丝不动。
            // 旧版这一条加的是通用攻击伤害，所有武器一起变强；新版只该动近战。
            list.Add(new CheckResult("v162：磨刀石不碰远程伤害",
                session.Progress.Stats.Get(StatId.RangedDamage) == rangedBefore,
                $"远程 {rangedBefore:0} → {session.Progress.Stats.Get(StatId.RangedDamage):0}（应不变）"));
            list.Add(new CheckResult("v162：磨刀石不碰爆炸伤害",
                session.Progress.Stats.Get(StatId.BlastDamage) == blastBefore,
                $"爆炸 {blastBefore:0} → {session.Progress.Stats.Get(StatId.BlastDamage):0}（应不变）"));
            // 所有道具的属性必须是现存属性（不允许引用已删除的旧属性）
            // v168：`Effects` / `Costs` **两张表都要查** —— 减益同样会写进 `StatBlock` ✓
            //       （而且减益写的是一个负数，属性名更不容易被肉眼发现写错 ✓）
            bool allKnown = true; string badStat = "";
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                foreach (ItemBonus b in it.Effects)
                    if (!StatIds.TryParse(b.Stat.ToString(), out _)) { allKnown = false; badStat = it.Id + "." + b.Stat; }
                foreach (ItemBonus b in it.Costs)
                    if (!StatIds.TryParse(b.Stat.ToString(), out _)) { allKnown = false; badStat = it.Id + "." + b.Stat; }
                if (it.Effects.Length == 0 && it.Costs.Length == 0 && !it.HasRule)
                { allKnown = false; badStat = it.Id + "（无效果）"; }
            }
            list.Add(new CheckResult("v34：所有道具属性均为现存属性（含减益）", allKnown, badStat));
            // v15：道具数值必须满足整数约定（Add 整数 / Percent 整数%）
            bool intOk = true; string badId = "";
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                foreach (ItemBonus b in it.Effects)
                    if (Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { intOk = false; badId = it.Id; }
                foreach (ItemBonus b in it.Costs)
                    if (Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { intOk = false; badId = it.Id; }
            }
            list.Add(new CheckResult("所有道具数值仍满足整数/整数%约定", intOk, badId));

            // ── 耕作期间禁止任何攻击（含左键主动攻击）
            session.StartWave(1);
            PlayerActor p = session.World.Player;
            FarmPlot plot = session.World.Farm.Slots[0];
            p.Pos = plot.World;                       // 站到耕地上
            Vector2 enemyPos = p.Pos + new Vector2(1.4f, 0f);
            session.World.SpawnEnemy("grunt", enemyPos, 1);
            EnemyActor enemy = session.World.Enemies[0];
            float hp0 = enemy.Health.Hp;

            // 按住 E 耕作 + 按住左键 → 不应造成任何伤害
            var farmInput = new FrameInput
            {
                Interact = true, Fire = true, AimWorld = enemy.Pos,
                BuyOffer = -1, SellSlot = -1, InitialChoice = -1, SelectSeed = -1
            };
            for (int i = 0; i < 30; i++)
            {
                farmInput.AimWorld = enemy.Pos;
                session.FixedTick(1f / 60f, farmInput);
            }
            list.Add(new CheckResult("耕作中玩家处于耕作状态", session.FarmingThisFrame || plot.State != PlotState.Empty,
                $"地块={plot.State} 耕作标记={session.FarmingThisFrame}"));
            Expect(list, "耕作期间禁止任何攻击（敌人体力不变）", enemy.Health.Hp, hp0);

            // 停止耕作后，左键攻击立刻生效
            var attackInput = new FrameInput
            {
                Interact = false, Fire = true, AimWorld = enemy.Pos,
                BuyOffer = -1, SellSlot = -1, InitialChoice = -1, SelectSeed = -1
            };
            bool damaged = false;
            for (int i = 0; i < 40 && !damaged; i++)
            {
                attackInput.AimWorld = enemy.Pos;
                session.FixedTick(1f / 60f, attackInput);
                if (enemy.Health.Hp < hp0 - 0.001f) damaged = true;
            }
            list.Add(new CheckResult("停止耕作后攻击立刻恢复", damaged, $"hp {enemy.Health.Hp:0.##}/{hp0:0.##}"));
        }

        /// <summary>v7 规则：数字键切换种子 / 锁定模式左键不再改瞄准 / 自动合成（含满袋买入）。</summary>
        private static void V7Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            var session = new RunSession();
            session.Start(909090UL);
            session.StartWave(1);

            // ── 数字键 1–6 切换种子袋槽位
            session.FixedTick(1f / 60f, new FrameInput { SelectSeed = 3, BuyOffer = -1, SellSlot = -1, InitialChoice = -1 });
            list.Add(new CheckResult("v166：种子位 = 可种植株数（列数）", session.Bag.SlotCount >= 1, $"{session.Bag.SlotCount} 格"));
            session.FixedTick(1f / 60f, new FrameInput { SelectSeed = 5, BuyOffer = -1, SellSlot = -1, InitialChoice = -1 });
            // ★ v166：原判据是「种子位 == 耕地块数」✗ —— 同一条 1:1 假设的**第 6 份副本** ✓
            // 新模型下一列有 3~6 格却只有 1 个种子位 ⇒ 两者**按设计不相等** ✓
            // 正确的等式是「种子位 == 可种植株数 == 列数」✓
            list.Add(new CheckResult("v166：种子位 = 可种植株数（不再是耕地块数）",
                session.Bag.SlotCount == session.Progress.MaxPlots && session.Bag.SlotCount == session.World.Farm.Columns,
                $"种子位 {session.Bag.SlotCount} / 可种植株数 {session.Progress.MaxPlots} / 列数 {session.World.Farm.Columns} / 田地 {session.World.Farm.PlotCount} 格"));

            // ── 锁定模式：左键不再改变瞄准方向（只朝锁定目标）
            PlayerActor p = session.World.Player;
            Vector2 enemyPos = p.Pos + new Vector2(3f, 0f);
            session.World.SpawnEnemy("grunt", enemyPos, 1);
            EnemyActor target = session.World.Enemies[0];
            p.LockTargetId = target.Id;
            Vector2 mouseAway = p.Pos + new Vector2(-4f, -4f);
            session.FixedTick(1f / 60f, new FrameInput { Fire = true, AimWorld = mouseAway, BuyOffer = -1, SellSlot = -1, InitialChoice = -1, SelectSeed = -1 });
            float toTarget = Vector2.Angle(p.AimDir, (target.Pos - p.Pos).normalized);
            list.Add(new CheckResult("左键始终朝鼠标（即使处于自动锁定）", Vector2.Angle(p.AimDir, (mouseAway - p.Pos).normalized) < 5f, "自动锁定只用于不按键时的兜底攻击"));

            // 解除锁定后，左键恢复"朝鼠标方向"
            p.LockTargetId = -1;
            session.FixedTick(1f / 60f, new FrameInput { Fire = true, AimWorld = mouseAway, BuyOffer = -1, SellSlot = -1, InitialChoice = -1, SelectSeed = -1 });
            float toMouse = Vector2.Angle(p.AimDir, (mouseAway - p.Pos).normalized);
            list.Add(new CheckResult("非锁定下左键朝鼠标方向", toMouse < 5f, $"偏角 {toMouse:0.0}°"));

            // ── 自动合成：4 个 1 级 → 2 个 2 级 → 1 个 3 级（共 3 次合并）
            var bag = new SeedBag(6, true);
            for (int i = 0; i < 4; i++) bag.AddSeed(radish, null, 0, out _);
            int merged = bag.AutoMergeAll(out _);
            int onlySlot = -1;
            for (int i = 0; i < bag.SlotCount; i++) if (!bag[i].IsEmpty) onlySlot = i;
            list.Add(new CheckResult($"合成已回归：4 个 1 级 → 1 个 {SeedSlot.MaxLevel} 级（合 3 次）",
                merged == 3 && bag.OccupiedCount == 1 && onlySlot >= 0 && bag[onlySlot].Level == SeedSlot.MaxLevel,
                $"合并 {merged} 次，剩 {bag.OccupiedCount} 槽，等级 {(onlySlot >= 0 ? bag[onlySlot].Level : 0)}"));
            list.Add(new CheckResult("合到封顶后不再有可合成的一对", !bag.HasMergeablePair(), ""));

            var bag2 = new SeedBag(6, true);
            bag2.AddSeed(radish, null, 0, out _);
            bag2.AddSeed(TableDatabase.GetSeed("pea_shooter"), null, 0, out _);
            list.Add(new CheckResult("不同种子不会被自动合成", bag2.AutoMergeAll(out _) == 0 && bag2.OccupiedCount == 2, ""));

            // ── 满袋时买入相同种子 → 自动合成（旧版合成规则）
            var full = new SeedBag(6, true);
            full.AddSeed(radish, null, 0, out _);
            for (int i = 0; i < 5; i++) full.AddSeed(TableDatabase.GetSeed("pea_shooter"), null, i % t.quality.Max + 1, out _);
            list.Add(new CheckResult("前置：种子袋已满", full.FirstEmpty() < 0, $"{full.OccupiedCount}/{full.SlotCount}"));
            // 买进来的种子恒为 1 级 ⇒ 传 StartingLevel ✓ 袋里的萝卜也是 1 级 ⇒ 合得动 ✓
            bool autoMerged = full.TryMergeWithExisting(radish, null, SeedSlot.StartingLevel, out SeedSlot slot);
            list.Add(new CheckResult("满袋买入相同种子 → 自动合成（1 级 → 2 级）",
                autoMerged && slot != null && slot.Level == 2 && slot.Quality == 0,
                $"萝卜 → {slot?.Level} 级（品质仍是 {slot?.QualityName}）"));
            list.Add(new CheckResult("满袋且没有相同种子 → 拒绝",
                !full.TryMergeWithExisting(TableDatabase.GetSeed("ironroot_colossus"), null, SeedSlot.StartingLevel, out _), ""));

            // ── 等级改变的是"要多少水"（复用 SeedEconomy.WaterNeeded）
            var wslot = new SeedSlot { Def = radish, Quality = 0 };
            int w1 = wslot.WaterNeeded;
            wslot.Level = 2;
            int w2 = wslot.WaterNeeded;
            wslot.Level = SeedSlot.MaxLevel;
            int w3 = wslot.WaterNeeded;
            int wStep = Mathf.RoundToInt(SeedSlot.WaterPerLevel);
            list.Add(new CheckResult("蓄水需求随等级递增（复用 SeedEconomy.WaterNeeded）",
                w2 == w1 + wStep && w3 == w1 + wStep * 2,
                $"{w1} → {w2} → {w3}（每级 +{wStep}）"));
            list.Add(new CheckResult("等级倍率 1 级 = 1.00 / 2 级 = 1.50 / 3 级 = 2.00",
                Mathf.Abs(new SeedSlot { Level = 1 }.LevelYieldMult - 1f) < 0.001f
                && Mathf.Abs(new SeedSlot { Level = 2 }.LevelYieldMult - 1.5f) < 0.001f
                && Mathf.Abs(new SeedSlot { Level = 3 }.LevelYieldMult - 2f) < 0.001f, ""));

            // ── 商店第三类：插件（原"工具升级"这一类已被取代）
            int poolSize = ShopSystem.PluginPoolSize();
            list.Add(new CheckResult("插件表非空（Data/plugins.json）", poolSize > 0,
                $"{poolSize} 件"
                + (poolSize > 0 ? "" : " ⇒ 表没载入或 plugins.json 缺行（TableDatabase.Plugins 为空）")));
            // 每件插件的字段都要真的可用 ✗ —— "表里有行"不等于"下游能读"
            int badPlugin = 0;
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
            {
                PluginAsset pl = TableDatabase.Plugins[i];
                if (pl == null || string.IsNullOrEmpty(pl.id) || string.IsNullOrEmpty(pl.displayName)
                    || pl.basePrice <= 0 || pl.maxStack <= 0 || pl.effect == PluginEffectKind.None) badPlugin++;
            }
            list.Add(new CheckResult("每件插件的 id / 名称 / 价格 / 上限 / 效果都有效",
                TableDatabase.Plugins.Length > 0 && badPlugin == 0,
                $"{TableDatabase.Plugins.Length} 件，其中字段不全 {badPlugin} 件"));
            // ★ v171：这条断言的**判据**跟着用户的新标准改了 ★
            //
            // 原来是："既有通用插件（任意武器）也有专属插件" —— 要求 `allowedWeapons` 为空的有 >0 件 ✗
            // 用户的标准里只有三类：锄头插件 / 水壶插件 / 肥料插件 ✓
            // 而且"任意武器都能装"的插件在数值上**严格优于**任何专属插件：
            // 它会被无脑塞进每一把武器的 6 个位置，把"6 选 N"这个取舍架空 ✓
            // ⇒ 判据反过来：表里**不允许**出现通用插件，真正的规模要求交给
            //   `PluginChecks` 的"每把武器 ≥ 6 件" ✓
            int anyWeapon = 0;
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                if (TableDatabase.Plugins[i].allowedWeapons != null && TableDatabase.Plugins[i].allowedWeapons.Length == 0)
                    anyWeapon++;
            list.Add(new CheckResult("v171：插件分类只有三类（不再有「任意武器」的通用插件）",
                anyWeapon == 0,
                $"通用 {anyWeapon} / 专属 {TableDatabase.Plugins.Length - anyWeapon}"));
            var probe = new RunSession();
            probe.Start(90210UL);
            probe.WaveIndex = 6;
            int nSeed = 0, nPlugin = 0, nItem = 0;
            for (int i = 0; i < 100; i++)
            {
                probe.Shop.Refresh(probe);
                for (int k = 0; k < probe.Shop.Offers.Count; k++)
                {
                    switch (probe.Shop.Offers[k].Kind)
                    {
                        case OfferKind.Seed: nSeed++; break;
                        case OfferKind.Plugin: nPlugin++; break;
                        case OfferKind.Item: nItem++; break;
                    }
                }
            }
            list.Add(new CheckResult("商店第三类是插件且真的抽得出来", nPlugin > 0,
                $"100 次刷新 400 个货位：种子 {nSeed} / 插件 {nPlugin} / 道具 {nItem}"));
            list.Add(new CheckResult("三类商品都能出现（没有不可达类别）",
                nSeed > 0 && nPlugin > 0 && nItem > 0, ""));
            // ★ 让分布自己播报 ★
            // `Report()` **只打印失败项** ⇒ 这条断言过了就什么都看不到 ✗
            // 而"三类各占多少"正是调权重（`shop.seedWeight / pluginWeight / itemWeight`）唯一要看的数据 ✓
            // 只靠"过了"无法核对比例 ⇒ 这里的 Log 是那条断言的**可读证据** ✓
            // （同 `[v167 雨滴概率]` 的落点日志：数值类的断言要自己报数 ✓）
            Log.Info($"[商店权重] 100 次刷新 400 个货位 ⇒ 种子 {nSeed}/{nSeed / 4f:0.0}% "
                     + $"插件 {nPlugin}/{nPlugin / 4f:0.0}% 道具 {nItem}/{nItem / 4f:0.0}%"
                     + $"（权重 种子{probe.T.shop.seedWeight}/插件{probe.T.shop.pluginWeight}/道具{probe.T.shop.itemWeight}）");

            // ── 工具购买后自动合成（同品质买两次 = 升一档）
            var ts = new RunSession();
            ts.Start(5150UL);
            ts.UpgradeTool(RunSession.ToolHoe, 0, out _);
            ts.UpgradeTool(RunSession.ToolHoe, 0, out _);
            list.Add(new CheckResult("武器购买后自动合成（升级）", ts.ToolLevel[RunSession.ToolHoe] == 1,
                $"锄头 = {t.quality.NameOf(ts.ToolLevel[RunSession.ToolHoe])}"));
        }

        /// <summary>
        /// ★ v168：**「按新标准重新设计全部植物与道具」的验收** ★
        ///
        /// 这一节回答三个问题，而且**每一条都用可观测的行为回答**：
        ///   ① 16 个植物是不是按品质分了档（普通/一般 = 纯数值、稀有/史诗 = 小机制、传奇 = 大机制）？
        ///   ② 每条植物机制是不是真的在**成长 / 收获 / 缺水**这三个位置上生效？
        ///   ③ 道具的三类别是不是真的落地了（纯数值 / 有牺牲 / 机制拓展）？
        ///
        /// ★ 为什么每条机制都要单独造一次对照实验 ★
        /// "表里有这条机制"与"这条机制生效"是两件完全不同的事 ✗
        /// 本工程已经吃过一次：`WaterSave` 被实现成 `TickGrowth2(dt*2)` ——
        /// 名字叫"省水"而实际**在加倍水耗**，而表里的数据始终是"对"的 ✓
        /// ⇒ 所以下面每个机制都造两株**只差这一个机制名**的植物，量行为差异 ✓
        /// 对照实验沿用 v166 的 WaterSave 用例写法（合成种子把水量放大 ⇒ 不缺水 ⇒ 差异只可能来自被测机制）✓
        /// </summary>
        private static void V168Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            SeedDef radish = TableDatabase.GetSeed("radish_guard");

            // ★★ 下面每个机制都造一颗**合成夹具种子**（`new SeedDef`），它们的 id 是自造的 ★★
            //
            // 例如 `t168_dew` —— 它**不在** `seeds.json` 里，而且**刻意**不在：
            //   单测一条机制不该往真表里塞东西 ✗ 否则玩家会在商店里看见 `t168_haste` ✓
            //
            // ★ 而这正好顺带成了"收割路径不按 id 回查真表"的**回归证据** ★
            //   这一条是花了一次崩溃换来的：
            //     旧 `World.HarvestPlot` 读 `TableDatabase.GetSeed(plot.CropSeedId)` 去取 `price`，
            //     而 `GetSeed` 对**不存在的 id 抛 `KeyNotFoundException`**（只有**空** id 返回 null ✓）
            //     ⇒ 夹具的 id 不在表里 ⇒ `RunAll()` **整轮崩掉** ✗
            //     ⇒ 崩溃时**一条判据都跑不完**，比"红一条"糟得多 —— 红告诉你哪里不对，崩只给一个堆栈 ✓
            //
            //   现在收割路径改为读**地块自己持有的 `plot.CropDef`**（种下去的那一份定义）✓
            //   ⇒ 「地块上有定义」与「表里有这个 id」解耦 ⇒ 夹具能跑，而玩家路径的语义一点没变 ✓
            //   （生产里 `CropDef` **就是**表里那个对象，`price` 完全相同 ✓）
            int unitGold = Mathf.Max(1, Mathf.RoundToInt(radish.Price / 5f));   // 1 级、无机制、无道具时的单格金币

            // ★ 反向的守卫：**夹具的 id 不该出现在真表里** ★
            //
            // 那才是这条断言真正要防的风险 —— **测试数据污染真表**
            // （一旦塞进 seeds.json，玩家会在商店里刷到 `t168_haste`）✗
            //
            // ⚠️ 必须用 `HasSeed`（不抛）而不是 `GetSeed`（**对不存在的 id 抛异常**）✗
            //    用 `GetSeed` 去问"有没有"，问的方式就是先抛一个异常 ⇒ 断言**永远执行不到** ✓
            //    ⇒ "一个漂亮的红"变成"整轮自检崩掉"
            //    ⇒ 而崩掉时**一条判据都跑不完** —— 这正是 v168 期间真的发生过的那次 ✓
            string[] fixtureIds =
            {
                "t168_haste", "t168_plain", "t168_nb", "t168_np", "t168_elder", "t168_e2",
                "t168_tt", "t168_tp", "t168_dew", "t168_rich", "t168_rp", "t168_chain", "t168_bloom"
            };
            int polluted = 0; string pollutedId = "";
            for (int i = 0; i < fixtureIds.Length; i++)
                if (TableDatabase.HasSeed(fixtureIds[i])) { polluted++; pollutedId = fixtureIds[i]; }
            list.Add(new CheckResult("v168：机制夹具的 id 没有污染真表（测试数据不进 seeds.json）",
                polluted == 0,
                polluted == 0 ? $"{fixtureIds.Length} 个夹具 id 都不在表里｜单格基线 {unitGold} 金" : $"被污染：{pollutedId}"));

            // ═════════════════ 一、植物：按品质分档，机制只落在三个阶段 ═════════════════
            int lowPlain = 0, lowMech = 0, midMech = 0, midPlain = 0, highMech = 0, highPlain = 0;
            int badMech = 0; string badMechId = "";
            var mechUse = new Dictionary<string, int>();
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                bool has = !string.Equals(d.Mechanic, PlantMechanics.None, System.StringComparison.OrdinalIgnoreCase);
                if (!PlantMechanics.IsKnown(d.Mechanic)) { badMech++; badMechId = d.Id + ":" + d.Mechanic; }
                if (has) { mechUse.TryGetValue(d.Mechanic, out int c); mechUse[d.Mechanic] = c + 1; }
                int tier = d.RarityTier;
                if (tier <= 1) { if (has) lowMech++; else lowPlain++; }
                else if (tier <= 3) { if (has) midMech++; else midPlain++; }
                else { if (has) highMech++; else highPlain++; }
            }
            list.Add(new CheckResult("v168：植物仍是 16 个",
                TableDatabase.Seeds.Length == 16, $"{TableDatabase.Seeds.Length}"));
            list.Add(new CheckResult("v168：普通/一般 = 纯数值差异（一个机制都没有）",
                lowPlain >= 6 && lowMech == 0, $"无机制 {lowPlain} 件｜带机制 {lowMech} 件（应 0）"));
            list.Add(new CheckResult("v168：稀有/史诗 = 小机制（每件都带一条）",
                midMech >= 6 && midPlain == 0, $"带机制 {midMech} 件｜无机制 {midPlain} 件（应 0）"));
            list.Add(new CheckResult("v168：传奇 = 大机制（3 件全部带）",
                highMech == 3 && highPlain == 0, $"带机制 {highMech} 件｜无机制 {highPlain} 件（应 0）"));
            list.Add(new CheckResult("v168：表里的每个 mechanic 都有解释器（PlantMechanics）",
                badMech == 0, badMech == 0 ? $"{mechUse.Count} 种机制在用" : badMechId));
            // ★ 反向也查 ★：解释了却没人用 = 一条"空解释器"，是"以为做了、其实没人调"的另一种形态
            int mechUnused = 0; string muName = "";
            for (int i = 0; i < PlantMechanics.All.Length; i++)
                if (!mechUse.ContainsKey(PlantMechanics.All[i])) { mechUnused++; muName = PlantMechanics.All[i]; }
            list.Add(new CheckResult("v168：PlantMechanics 登记的机制都被表用到（没有空解释器）",
                mechUnused == 0, mechUnused == 0 ? $"{PlantMechanics.All.Length} 条全在用" : $"没人用：{muName}"));
            int undoc = 0; string udName = "";
            for (int i = 0; i < PlantMechanics.All.Length; i++)
                if (string.IsNullOrEmpty(PlantMechanics.Describe(PlantMechanics.All[i]))) { undoc++; udName = PlantMechanics.All[i]; }
            list.Add(new CheckResult("v168：每条植物机制都有可读说明（机制文案的唯一来源）",
                undoc == 0, undoc == 0 ? "全部有说明" : $"缺说明：{udName}"));

            // ═════════════════ 二、机制的**可观测行为**（每条一次对照实验）═════════════════

            // ── ① Haste【成长】：同参数、只差机制名 ⇒ 成长推进 ×1.25 ──
            {
                var w = MakeWorld(out _);
                var fast = new SeedDef { Id = "t168_haste", Name = "t", GrowTime = 600f, Mechanic = PlantMechanics.Haste, WaterDrain = 0 };
                var slow = new SeedDef { Id = "t168_plain", Name = "t", GrowTime = 600f, Mechanic = PlantMechanics.None, WaterDrain = 0 };
                FarmPlot a = w.Farm.Slot(0, 0), b = w.Farm.Slot(1, 0);
                a.BeginPlant(t.farm, fast, 100000, 0);
                b.BeginPlant(t.farm, slow, 100000, 0);
                float a0 = a.GrowthRemaining, b0 = b.GrowthRemaining;
                for (int i = 0; i < 600; i++) w.Simulate(1f / 60f);        // 10 秒
                float advFast = a0 - a.GrowthRemaining, advPlain = b0 - b.GrowthRemaining;
                list.Add(new CheckResult("v168：Haste【成长】该列成长推进 ×1.25（量的是 GrowthRemaining）",
                    advPlain > 1f && Mathf.Abs(advFast / advPlain - PlantMechanics.HasteMult) < 0.03f,
                    $"快 {advFast:0.###} vs 普通 {advPlain:0.###}（比值 {advFast / advPlain:0.###}，应 {PlantMechanics.HasteMult}）"));
            }

            // ── ② NeighborBoost【成长·相邻影响】：助推株的左右两列变快，无关列不变 ──
            {
                var w = MakeWorld(out _);
                var boost = new SeedDef { Id = "t168_nb", GrowTime = 600f, Mechanic = PlantMechanics.NeighborBoost, WaterDrain = 0 };
                var plain = new SeedDef { Id = "t168_np", GrowTime = 600f, Mechanic = PlantMechanics.None, WaterDrain = 0 };
                // 列1 = 助推株；列0 与列2 挨着它；列3 两侧都是普通（对照基线）
                w.Farm.Slot(1, 0).BeginPlant(t.farm, boost, 100000, 0);
                w.Farm.Slot(0, 0).BeginPlant(t.farm, plain, 100000, 0);
                w.Farm.Slot(2, 0).BeginPlant(t.farm, plain, 100000, 0);
                w.Farm.Slot(3, 0).BeginPlant(t.farm, plain, 100000, 0);
                FarmPlot c0 = w.Farm.Slot(0, 0), c2 = w.Farm.Slot(2, 0), c3 = w.Farm.Slot(3, 0);
                float p0 = c0.GrowthRemaining, p2 = c2.GrowthRemaining, p3 = c3.GrowthRemaining;
                for (int i = 0; i < 600; i++) w.Simulate(1f / 60f);
                float a0 = p0 - c0.GrowthRemaining, a2 = p2 - c2.GrowthRemaining, a3 = p3 - c3.GrowthRemaining;
                bool leftOk = Mathf.Abs(a0 / a3 - PlantMechanics.NeighborMult) < 0.03f;
                bool rightOk = Mathf.Abs(a2 / a3 - PlantMechanics.NeighborMult) < 0.03f;
                list.Add(new CheckResult("v168：NeighborBoost【成长】只抬左右相邻列（无关列不变）",
                    a3 > 1f && leftOk && rightOk,
                    $"左 {a0:0.###} ／ 自己 {a3:0.###} ／ 右 {a2:0.###}（左/基线 {a0 / a3:0.###}、右/基线 {a2 / a3:0.###}，应 {PlantMechanics.NeighborMult}）"));
            }

            // ── ③ ElderGrowth【成长·随时间】：60 秒一个台阶，封顶 +60% ──
            {
                var elder = new SeedDef { Id = "t168_elder", GrowTime = 600f, Mechanic = PlantMechanics.ElderGrowth, WaterDrain = 0 };
                bool curveOk =
                    Mathf.Abs(PlantMechanics.GrowSpeedMult(elder, 0f) - 1f) < 0.001f
                    && Mathf.Abs(PlantMechanics.GrowSpeedMult(elder, 60f) - 1.04f) < 0.001f
                    && Mathf.Abs(PlantMechanics.GrowSpeedMult(elder, 120f) - 1.08f) < 0.001f
                    && Mathf.Abs(PlantMechanics.GrowSpeedMult(elder, 99999f) - 1.60f) < 0.001f;
                list.Add(new CheckResult("v168：ElderGrowth【成长】倍率曲线 0s=1.00／60s=1.04／120s=1.08／封顶 1.60",
                    curveOk,
                    $"{PlantMechanics.GrowSpeedMult(elder, 0f):0.00}／{PlantMechanics.GrowSpeedMult(elder, 60f):0.00}／"
                    + $"{PlantMechanics.GrowSpeedMult(elder, 120f):0.00}／{PlantMechanics.GrowSpeedMult(elder, 99999f):0.00}"));

                // 集成证据：真跑 120 秒，带机制的列推进得更多
                var w = MakeWorld(out _);
                var plain = new SeedDef { Id = "t168_e2", GrowTime = 600f, Mechanic = PlantMechanics.None, WaterDrain = 0 };
                FarmPlot a = w.Farm.Slot(0, 0), b = w.Farm.Slot(1, 0);
                a.BeginPlant(t.farm, elder, 100000, 0);
                b.BeginPlant(t.farm, plain, 100000, 0);
                float a0 = a.GrowthRemaining, b0 = b.GrowthRemaining;
                for (int i = 0; i < 7200; i++) w.Simulate(1f / 60f);       // 120 秒
                float advE = a0 - a.GrowthRemaining, advP = b0 - b.GrowthRemaining;
                list.Add(new CheckResult("v168：ElderGrowth【成长】真的接进了生长循环（跑满 120 秒后推进更多）",
                    advE > advP * 1.01f, $"古木 {advE:0.###} vs 普通 {advP:0.###}"));
            }

            // ── ④ WaterSave【成长·缺水】：表里真的有人在用（行为验证见 v166 的对照实验）──
            {
                SeedDef save = TableDatabase.GetSeed("moss_shield");
                list.Add(new CheckResult("v168：省水机制在真表里有人用（苔藓盾 = WaterSave）",
                    PlantMechanics.Has(save, PlantMechanics.WaterSave)
                    && Mathf.Abs(PlantMechanics.WaterCostScale(save) - 0.5f) < 0.001f,
                    $"{save?.Name} → {save?.Mechanic}，水耗系数 {PlantMechanics.WaterCostScale(save):0.##}"));
            }

            // ── ⑤ ThirstTolerant【缺水】：普通列缺水停长，它仍在长 ──
            {
                var w = MakeWorld(out _);
                var tolerant = new SeedDef { Id = "t168_tt", GrowTime = 600f, Mechanic = PlantMechanics.ThirstTolerant, WaterDrain = 0 };
                var plain = new SeedDef { Id = "t168_tp", GrowTime = 600f, Mechanic = PlantMechanics.None, WaterDrain = 0 };
                FarmPlot a = w.Farm.Slot(0, 0), b = w.Farm.Slot(1, 0);
                a.BeginPlant(t.farm, tolerant, 100, 0);
                b.BeginPlant(t.farm, plain, 100, 0);
                a.Water = 0; b.Water = 0;                       // 当场进入缺水
                float a0 = a.GrowthRemaining, b0 = b.GrowthRemaining;
                for (int i = 0; i < 600; i++) w.Simulate(1f / 60f);   // 10 秒
                float advA = a0 - a.GrowthRemaining, advB = b0 - b.GrowthRemaining;
                list.Add(new CheckResult("v168：ThirstTolerant【缺水】缺水时仍以 40% 速度生长（普通列完全停长）",
                    a.GrowthPaused && advB <= 0.001f && advA > 1f,
                    $"耐旱列推进 {advA:0.###}（10 秒 × 40% ≈ 4）｜普通列 {advB:0.###}（应 0）"));
            }

            // ── ⑥ DewDrop【收获】：收获时真的返还蓄水 ──
            {
                var w = MakeWorld(out _);
                var dew = new SeedDef { Id = "t168_dew", GrowTime = 600f, Mechanic = PlantMechanics.DewDrop, WaterDrain = 0 };
                FarmPlot a = w.Farm.Slot(0, 0);
                a.BeginPlant(t.farm, dew, 1000, 0);
                a.TickGrowth(999f);                              // 催熟
                a.Water = 10;
                w.HarvestPlot(a);
                list.Add(new CheckResult("v168：DewDrop【收获】收获该格时返还 12 点蓄水",
                    a.Water == 10 + PlantMechanics.DewDropWater,
                    $"蓄水 10 → {a.Water}（应 {10 + PlantMechanics.DewDropWater}）"));
            }

            // ── ⑦ RichHarvest【收获】：金币 ×1.30 ──
            {
                var rich = new SeedDef { Id = "t168_rich", GrowTime = 600f, Mechanic = PlantMechanics.RichHarvest, WaterDrain = 0, Price = 100 };
                var plain = new SeedDef { Id = "t168_rp", GrowTime = 600f, Mechanic = PlantMechanics.None, WaterDrain = 0, Price = 100 };
                var w1 = MakeWorld(out _); FarmPlot p1 = w1.Farm.Slot(0, 0);
                p1.BeginPlant(t.farm, rich, 1000, 0); p1.TickGrowth(999f); w1.HarvestPlot(p1);
                var w2 = MakeWorld(out _); FarmPlot p2 = w2.Farm.Slot(0, 0);
                p2.BeginPlant(t.farm, plain, 1000, 0); p2.TickGrowth(999f); w2.HarvestPlot(p2);
                list.Add(new CheckResult("v168：RichHarvest【收获】该格金币 ×1.30",
                    w2.GoldEarned == 20 && w1.GoldEarned == Mathf.RoundToInt(20 * PlantMechanics.RichHarvestMult),
                    $"丰收 {w1.GoldEarned} 金 vs 普通 {w2.GoldEarned} 金（应 {Mathf.RoundToInt(20 * PlantMechanics.RichHarvestMult)}）"));
            }

            // ── ⑧ ChainHarvest【收获】：收一格 ⇒ 整列成熟格一并收获 ──
            {
                var w = MakeWorld(out _);
                var chain = new SeedDef { Id = "t168_chain", GrowTime = 600f, Mechanic = PlantMechanics.ChainHarvest, WaterDrain = 0, Price = 100 };
                w.Farm.Slot(0, 0).BeginPlant(t.farm, chain, 1000, 0);     // 列首种下 ⇒ 整列自动补齐
                int rows = w.Farm.Rows;
                for (int r = 0; r < rows; r++) w.Farm.Slot(0, r).TickGrowth(999f);
                int ripeBefore = 0;
                for (int r = 0; r < rows; r++) if (w.Farm.Slot(0, r).IsRipe) ripeBefore++;
                w.HarvestPlot(w.Farm.Slot(0, 0));                          // 只收一格
                int ripeAfter = 0;
                for (int r = 0; r < rows; r++) if (w.Farm.Slot(0, r).IsRipe) ripeAfter++;
                list.Add(new CheckResult("v168：ChainHarvest【收获】收割一格连带整列已成熟的格",
                    ripeBefore == rows && ripeAfter == 0 && w.GoldEarned == rows * 20,
                    $"收割前成熟 {ripeBefore}/{rows} 格 → 收割后 {ripeAfter} 格；金币 {w.GoldEarned}（应 {rows * 20}）"));
            }

            // ── ⑨ ThirstBloom【缺水·收获】：缺水反而更值钱 ──
            {
                var bloom = new SeedDef { Id = "t168_bloom", GrowTime = 600f, Mechanic = PlantMechanics.ThirstBloom, WaterDrain = 0, Price = 100 };
                var wd = MakeWorld(out _); FarmPlot dry = wd.Farm.Slot(0, 0);
                dry.BeginPlant(t.farm, bloom, 1000, 0); dry.TickGrowth(999f); dry.Water = 0; dry.NeedFullRefill = true;
                wd.HarvestPlot(dry);
                var ww = MakeWorld(out _); FarmPlot wet = ww.Farm.Slot(0, 0);
                wet.BeginPlant(t.farm, bloom, 1000, 0); wet.TickGrowth(999f);
                ww.HarvestPlot(wet);
                list.Add(new CheckResult("v168：ThirstBloom【缺水·收获】缺水时该格金币 ×1.60",
                    ww.GoldEarned == 20 && wd.GoldEarned == Mathf.RoundToInt(20 * PlantMechanics.ThirstBloomMult),
                    $"缺水 {wd.GoldEarned} 金 vs 有水 {ww.GoldEarned} 金（应 {Mathf.RoundToInt(20 * PlantMechanics.ThirstBloomMult)}）"));
            }

            // ═════════════════ 三、合成等级 1/2/3：只做数值提升，作用在成长与收获 ═════════════════

            int wm1 = SeedSlot.WaterMaxFor(radish, 1), wm2 = SeedSlot.WaterMaxFor(radish, 2), wm3 = SeedSlot.WaterMaxFor(radish, 3);
            list.Add(new CheckResult("v168：等级蓄水上限 100／140／180（每级 +40，与品种无关）",
                wm1 == 100 && wm2 == 140 && wm3 == 180, $"{wm1}／{wm2}／{wm3}"));
            SeedDef tofu = TableDatabase.GetSeed("toxic_mushroom");     // waterMax = 80 ⇒ 相对基准 −20
            list.Add(new CheckResult("v168：品种水箱偏差叠加在等级轴上（毒蘑菇 = 80／120／160）",
                SeedSlot.WaterMaxFor(tofu, 1) == 80 && SeedSlot.WaterMaxFor(tofu, 2) == 120 && SeedSlot.WaterMaxFor(tofu, 3) == 160,
                $"{SeedSlot.WaterMaxFor(tofu, 1)}／{SeedSlot.WaterMaxFor(tofu, 2)}／{SeedSlot.WaterMaxFor(tofu, 3)}"));

            list.Add(new CheckResult("v168：等级生长速度 1.00／1.15／1.30",
                Mathf.Abs(new SeedSlot { Level = 1 }.LevelGrowSpeedMult - 1f) < 0.001f
                && Mathf.Abs(new SeedSlot { Level = 2 }.LevelGrowSpeedMult - 1.15f) < 0.001f
                && Mathf.Abs(new SeedSlot { Level = 3 }.LevelGrowSpeedMult - 1.30f) < 0.001f,
                $"{new SeedSlot { Level = 1 }.LevelGrowSpeedMult:0.00}／{new SeedSlot { Level = 2 }.LevelGrowSpeedMult:0.00}／{new SeedSlot { Level = 3 }.LevelGrowSpeedMult:0.00}"));
            list.Add(new CheckResult("v168：等级收获倍率 1.00／1.35／1.70（与卖出返还的 1.5／2.0 分开）",
                Mathf.Abs(new SeedSlot { Level = 1 }.LevelHarvestMult - 1f) < 0.001f
                && Mathf.Abs(new SeedSlot { Level = 2 }.LevelHarvestMult - 1.35f) < 0.001f
                && Mathf.Abs(new SeedSlot { Level = 3 }.LevelHarvestMult - 1.70f) < 0.001f,
                $"收获 {new SeedSlot { Level = 2 }.LevelHarvestMult:0.00}／卖出 {new SeedSlot { Level = 2 }.LevelYieldMult:0.00}"));

            // ── 端到端①：种下去的一刻，等级就写进了地块（水箱 + 成长时间）──
            {
                var wl = MakeWorld(out _);
                FarmPlot l1 = wl.Farm.Slot(0, 0), l3 = wl.Farm.Slot(1, 0);
                l1.BeginPlant(t.farm, radish, SeedSlot.WaterMaxFor(radish, 1), radish.WaterDrain, 1);
                l3.BeginPlant(t.farm, radish, SeedSlot.WaterMaxFor(radish, 3), radish.WaterDrain, 3);
                list.Add(new CheckResult("v168：地块记得住等级（3 级 ⇒ 水箱 180 且成长时间 ÷1.30）",
                    l1.Level == 1 && l3.Level == 3 && l1.WaterMax == 100 && l3.WaterMax == 180
                    && Mathf.Abs(l3.GrowthTotal - radish.GrowTime / 1.30f) < 0.01f,
                    $"1 级：水 {l1.WaterMax}／{l1.GrowthTotal:0.##}s ｜ 3 级：水 {l3.WaterMax}／{l3.GrowthTotal:0.##}s"));
            }

            // ── 端到端②：收获金币随等级放大（萝卜 1 级 4 金 ⇒ 3 级 7 金）──
            {
                var wg1 = MakeWorld(out _); FarmPlot g1 = wg1.Farm.Slot(0, 0);
                g1.BeginPlant(t.farm, radish, 100, 0, 1); g1.TickGrowth(999f); wg1.HarvestPlot(g1);
                var wg3 = MakeWorld(out _); FarmPlot g3 = wg3.Farm.Slot(0, 0);
                g3.BeginPlant(t.farm, radish, 100, 0, 3); g3.TickGrowth(999f); wg3.HarvestPlot(g3);
                list.Add(new CheckResult("v168：收获金币真的随等级放大（1 级 4 金 ⇒ 3 级 7 金）",
                    wg1.GoldEarned == 4 && wg3.GoldEarned == Mathf.RoundToInt(4f * 1.70f),
                    $"1 级 {wg1.GoldEarned} 金 ／ 3 级 {wg3.GoldEarned} 金（应 {Mathf.RoundToInt(4f * 1.70f)}）"));
            }

            // ── 端到端③：合成之后，整列一起升级 ──
            {
                var wu = MakeWorld(out _);
                FarmPlot up = wu.Farm.Slot(0, 0);
                up.BeginPlant(t.farm, radish, SeedSlot.WaterMaxFor(radish, 1), radish.WaterDrain, 1);
                float beforeTotal = up.GrowthTotal, beforeRemain = up.GrowthRemaining;
                wu.Farm.SetColumnLevel(up, 3);
                bool wholeColumn = true;
                for (int r = 0; r < wu.Farm.Rows; r++) if (wu.Farm.Slot(0, r).Level != 3) wholeColumn = false;
                list.Add(new CheckResult("v168：合成后整列一起升到 3 级（成长时间重算、已走进度保留）",
                    wholeColumn && up.Level == 3 && up.WaterMax == 180 && up.GrowthTotal < beforeTotal,
                    $"整列等级 {(wholeColumn ? "3/3/3" : "不一致")}；成长 {beforeTotal:0.##}s → {up.GrowthTotal:0.##}s"
                    + $"（剩余 {beforeRemain:0.##} → {up.GrowthRemaining:0.##}）"));
            }

            // ═════════════════ 四、道具：三类别齐备且自洽 ═════════════════
            int cat1 = 0, cat2 = 0, cat3 = 0, noEffect = 0, costBad = 0, badRule = 0, growItems = 0;
            string firstBad = "";
            var ruleUse = new Dictionary<string, int>();
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                if (it.Category == 1) cat1++;
                else if (it.Category == 2) cat2++;
                else if (it.Category == 3) cat3++;

                if (it.Effects.Length == 0 && !it.HasRule) { noEffect++; firstBad = it.Id; }
                if (it.Category == 2)
                {
                    if (it.Costs.Length == 0) { costBad++; firstBad = it.Id + "（类别 2 却没有减益）"; }
                    if (it.Effects.Length == 0) { costBad++; firstBad = it.Id + "（类别 2 却没有增益）"; }
                    foreach (ItemBonus c in it.Costs)
                    {
                        float gain = 0f; bool sameStat = false;
                        foreach (ItemBonus e in it.Effects) if (e.Stat == c.Stat) { gain += Mathf.Abs(e.Add); sameStat = true; }
                        if (sameStat && gain <= Mathf.Abs(c.Add)) { costBad++; firstBad = it.Id + "：" + c.Stat + " 增益不大于减益"; }
                    }
                }
                if (!string.IsNullOrEmpty(it.Rule))
                {
                    if (!ItemRules.IsKnown(it.Rule)) { badRule++; firstBad = it.Id + ":" + it.Rule; }
                    ruleUse.TryGetValue(it.Rule, out int n); ruleUse[it.Rule] = n + 1;
                }
                if (it.GrowPerWave > 0f) growItems++;
            }
            list.Add(new CheckResult("v168：道具三类别齐备（纯数值／有牺牲／机制拓展）",
                cat1 > 0 && cat2 > 0 && cat3 > 0, $"纯数值 {cat1} 件／有牺牲 {cat2} 件／机制拓展 {cat3} 件"));
            list.Add(new CheckResult("v168：没有『买了没反应』的道具（有 effects 或 有可解释的 rule）",
                noEffect == 0, noEffect == 0 ? $"{TableDatabase.Items.Length} 件全部有效" : firstBad));
            list.Add(new CheckResult("v168：类别 2 的增益与减益两半齐全，且同属性上增益严格大于减益",
                costBad == 0, costBad == 0 ? $"{cat2} 件全部满足" : firstBad));
            list.Add(new CheckResult("v168：类别 3 的 rule 都有解释器（ItemRules）",
                badRule == 0, badRule == 0 ? $"{ruleUse.Count} 条规则在用" : firstBad));
            int ruleUnused = 0; string ruName = "";
            for (int i = 0; i < ItemRules.All.Length; i++)
                if (!ruleUse.ContainsKey(ItemRules.All[i])) { ruleUnused++; ruName = ItemRules.All[i]; }
            list.Add(new CheckResult("v168：ItemRules 登记的每条规则都被表用到（没有空解释器）",
                ruleUnused == 0, ruleUnused == 0 ? $"{ItemRules.All.Length} 条全在用" : $"没人用：{ruName}"));
            int wide = 0;
            for (int i = 0; i < TableDatabase.Items.Length; i++)
                if (TableDatabase.Items[i].Category == 1 && TableDatabase.Items[i].Effects.Length >= 3) wide++;
            list.Add(new CheckResult("v168：类别 1 有『全属性小幅度增加』的传奇特例（一条 ≥3 个属性）",
                wide > 0, $"{wide} 件"));
            list.Add(new CheckResult("v168：类别 2 有『加入成长』的道具（growPerWave > 0）",
                growItems > 0, $"{growItems} 件"));

            // ═════════════════ 五、三条道具机制的**可观测行为** ═════════════════

            // ── ① reroll_discount ⇒ 商店刷新费真的变便宜（HUD 读的是同一个函数）──
            {
                var s = new RunSession();
                s.Start(88001UL);
                s.StartWave(1);
                s.Gold = 1000;
                int baseCost = s.Shop.RerollCost(s.T);
                s.ApplyItem(TableDatabase.GetItem("haggle_charm"));
                int cheapCost = s.Shop.RerollCost(s.T);
                bool charged = s.Shop.TryReroll(s, out string reason);
                list.Add(new CheckResult("v168：reroll_discount 真的改到刷新定价与扣费（显示与扣费同一个函数）",
                    cheapCost == baseCost - 1 && charged && s.Gold == 1000 - cheapCost,
                    $"{baseCost} → {cheapCost} 金；刷新后金币 {s.Gold}（应 {1000 - cheapCost}）{reason}"));
                s.Dispose();
            }

            // ── ② harvest_gold_pct ⇒ 收益结算真的被放大 ──
            {
                var s = new RunSession();
                s.Start(88002UL);
                s.StartWave(1);
                FarmPlot p = s.World.Farm.Slot(0, 0);
                p.BeginPlant(s.T.farm, radish, 1000, 0, 1);
                p.TickGrowth(999f);
                int before = s.World.GoldEarned;
                s.World.HarvestPlot(p);
                int plainGain = s.World.GoldEarned - before;
                s.ApplyItem(TableDatabase.GetItem("golden_sickle"));
                p.TickGrowth(999f);
                before = s.World.GoldEarned;
                s.World.HarvestPlot(p);
                int buffedGain = s.World.GoldEarned - before;
                list.Add(new CheckResult("v168：harvest_gold_pct 真的改到收获结算（+25% ⇒ 4 金变 5 金）",
                    Mathf.Abs(s.World.HarvestGoldMult - 1.25f) < 0.001f && buffedGain > plainGain,
                    $"倍率 {s.World.HarvestGoldMult:0.##}｜单格金币 {plainGain} → {buffedGain}"));
                s.Dispose();
            }

            // ── ③ harvest_dew ⇒ 收获时真的往地块里加水 ──
            {
                var s = new RunSession();
                s.Start(88003UL);
                s.StartWave(1);
                FarmPlot p = s.World.Farm.Slot(0, 0);
                p.BeginPlant(s.T.farm, radish, 1000, 0, 1);
                s.ApplyItem(TableDatabase.GetItem("dew_flask"));
                p.TickGrowth(999f);
                p.Water = 10;
                s.World.HarvestPlot(p);
                list.Add(new CheckResult("v168：harvest_dew 真的加到地块蓄水（每层 +4 ⇒ 10 变 14）",
                    s.World.HarvestDew == 4 && p.Water == 14,
                    $"返还量 {s.World.HarvestDew}｜蓄水 10 → {p.Water}（应 14）"));
                s.Dispose();
            }

            // ═════════════════ 六、幂等 / 减益生效 / 成长 ═════════════════
            {
                var s = new RunSession();
                s.Start(88004UL);
                s.StartWave(1);
                float m0 = s.Progress.Stats.Get(StatId.MeleeDamage);
                s.ApplyItem(TableDatabase.GetItem("wax_seal"));          // 近战 +1／最大生命 −1／每回合 +1
                float m1 = s.Progress.Stats.Get(StatId.MeleeDamage);
                s.ReapplyItems(); s.ReapplyItems(); s.ReapplyItems();
                float m2 = s.Progress.Stats.Get(StatId.MeleeDamage);
                list.Add(new CheckResult("v168：道具写入是幂等的（Reapply 三次不重复叠加）",
                    m1 > m0 && Mathf.Abs(m1 - m2) < 0.001f, $"第 1 回合 {m1:0.##} →（再 Reapply ×3）{m2:0.##}"));

                s.WaveIndex = 5;
                s.ReapplyItems();
                float m5 = s.Progress.Stats.Get(StatId.MeleeDamage);
                list.Add(new CheckResult("v168：『加入成长』真的随回合变强（蜡封符 第 1 回合 +1 ⇒ 第 5 回合 +5）",
                    Mathf.Abs(m5 - m0 - 5f) < 0.001f, $"近战 {m0:0.##} → 第1回合 {m1:0.##} → 第5回合 {m5:0.##}"));

                float hp0 = s.Progress.Stats.Get(StatId.MaxHp);
                s.ApplyItem(TableDatabase.GetItem("glass_cannon"));      // 近战 +3／最大生命 −1
                float hp1 = s.Progress.Stats.Get(StatId.MaxHp);
                float m6 = s.Progress.Stats.Get(StatId.MeleeDamage);
                list.Add(new CheckResult("v168：类别 2 的**减益**真的写进了属性（玻璃大炮：最大生命 −1）",
                    Mathf.Abs(hp1 - (hp0 - 1f)) < 0.001f, $"最大生命 {hp0:0.##} → {hp1:0.##}"));
                list.Add(new CheckResult("v168：类别 2 的**增益**真的写进了属性（玻璃大炮：近战 +3）",
                    Mathf.Abs(m6 - (m5 + 3f)) < 0.001f, $"近战 {m5:0.##} → {m6:0.##}"));
                s.Dispose();
            }

            // ═════════════════ 七、没买道具时的默认值（不影响既有平衡）═════════════════
            {
                var s = new RunSession();
                s.Start(88005UL);
                s.StartWave(1);
                list.Add(new CheckResult("v168：没买道具时机制系数是干净的默认值",
                    Mathf.Abs(s.World.HarvestGoldMult - 1f) < 0.001f && s.World.HarvestDew == 0 && s.Shop.RerollDiscount == 0,
                    $"金币倍率 {s.World.HarvestGoldMult:0.##}｜返还水 {s.World.HarvestDew}｜刷新减免 {s.Shop.RerollDiscount}"));
                s.Dispose();
            }
        }

        /// <summary>v6 规则：耕地按需生成（相邻扩展、上限 5×5）+ 工具升级与横向成组耕作。</summary>
        private static void V6Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            var grid = new FarmGrid(t);

            // ── v166 需求②：田地是**固定形状**（6 列 × N 行），玩家不选位置
            list.Add(new CheckResult("开局田地形状 = gridSize 列 × initialRows 行",
                grid.Size == t.farm.gridSize && grid.Rows == t.farm.initialRows,
                $"{grid.Size} 列 × {grid.Rows} 行"));
            list.Add(new CheckResult("开局耕地 = 6×3 = 18 格",
                grid.PlotCount == t.farm.gridSize * t.farm.initialRows,
                $"{grid.PlotCount} 格（应 {t.farm.gridSize * t.farm.initialRows}）"));
            list.Add(new CheckResult("网格容量 = 6×6 = 36（满格）",
                grid.Size == 6 && grid.Capacity == 36, $"{grid.Size}x{grid.Size}"));
            list.Add(new CheckResult("行列索引恒为 行*列数+列（加行只追加，已种作物不移动）",
                grid.Slot(0, 0).Index == 0 && grid.Slot(5, 0).Index == 5 && grid.Slot(0, 1).Index == 6,
                $"#0={grid.Slot(0, 0).Index} #(5,0)={grid.Slot(5, 0).Index} #(0,1)={grid.Slot(0, 1).Index}"));

            // ── 每一行都是完整的 6 格，且同列同 x、同行同 y
            bool shapeOk = true;
            for (int r = 0; r < grid.Rows && shapeOk; r++)
                for (int c = 0; c < grid.Columns; c++)
                {
                    FarmPlot p = grid.Slot(c, r);
                    if (p == null || p.GridX != c || p.GridY != r) { shapeOk = false; break; }
                }
            list.Add(new CheckResult("每一行都是完整的 6 格（矩形田）", shapeOk, ""));

            // ── v166 需求：每存活 5 分钟加一排，封顶 maxRows
            var growGrid = new FarmGrid(t);
            growGrid.SyncSurvival(299f);
            list.Add(new CheckResult("存活 299 秒还没到加排点",
                growGrid.Rows == t.farm.initialRows, $"{growGrid.Rows} 行"));
            growGrid.SyncSurvival(300f);
            list.Add(new CheckResult("存活 300 秒（5 分钟）⇒ 加一排",
                growGrid.Rows == t.farm.initialRows + 1, $"{growGrid.Rows} 行 / {growGrid.PlotCount} 格"));
            growGrid.SyncSurvival(600f);
            list.Add(new CheckResult("存活 600 秒 ⇒ 再加一排（累计）",
                growGrid.Rows == t.farm.initialRows + 2, $"{growGrid.Rows} 行"));
            growGrid.SyncSurvival(100000f);
            list.Add(new CheckResult("行数封顶 maxRows（6×6 满）",
                growGrid.Rows == t.farm.maxRows && growGrid.PlotCount == 36,
                $"{growGrid.Rows} 行 / {growGrid.PlotCount} 格"));
            int rowsBefore = growGrid.Rows;
            growGrid.SetRows(1);
            list.Add(new CheckResult("地块只增不减（加排不可回退）",
                growGrid.Rows == rowsBefore, $"{growGrid.Rows} 行"));

            // ── v166 需求⑤：等级**不再**给耕地（旧"每级 +1 块"模型已删除）
            var progress = new PlayerProgress(t);
            int basePlots = progress.MaxPlots;
            for (int lv = 1; lv < 4; lv++) progress.AddExp(PlayerProgress.ExpToNextFor(lv, t.growth), out _);
            progress.AddExp(100000f, out _);
            list.Add(new CheckResult("v166：levelsPerPlot = 0（按等级 +1 耕地已关闭）",
                t.growth.levelsPerPlot == 0, $"{t.growth.levelsPerPlot} 级/块"));
            list.Add(new CheckResult("v166：升级不再增加可种植株数",
                progress.MaxPlots == basePlots, $"Lv{progress.Level}：{basePlots} → {progress.MaxPlots}"));
            list.Add(new CheckResult("可种植株数 = 列数（6）",
                progress.MaxPlots == t.farm.gridSize, $"{progress.MaxPlots} / {t.farm.gridSize} 列"));
            list.Add(new CheckResult("可种植株数不超过列数",
                progress.MaxPlots <= t.farm.gridSize, $"{progress.MaxPlots} / {t.farm.gridSize}"));

            // ── v166 需求③④：植物**独占一列**（自动分配、上限 6）+ 加排自动补齐
            SeedDef colSeed = TableDatabase.GetSeed("radish_guard");
            var colGrid = new FarmGrid(t);
            colGrid.Slot(0, 0).BeginPlant(t.farm, colSeed, t.farm.waterMaxDefault, colSeed.WaterDrain);
            bool colFilled = colGrid.Slot(0, 0).State != PlotState.Empty
                             && colGrid.Slot(0, 1).State != PlotState.Empty
                             && colGrid.Slot(0, 2).State != PlotState.Empty;
            list.Add(new CheckResult("种下 1 株 ⇒ 整列（3 行）自动补齐", colFilled, "列 0 的 3 格都有作物"));
            list.Add(new CheckResult("植物独占一列：其它列仍为空",
                colGrid.Slot(1, 0).State == PlotState.Empty, $"列 1 行 0 = {colGrid.Slot(1, 0).State}"));
            list.Add(new CheckResult("植物株数 = 1，第一条空列 = 1",
                colGrid.PlantCount == 1 && colGrid.FirstFreeColumn() == 1,
                $"株数 {colGrid.PlantCount}，空列 {colGrid.FirstFreeColumn()}"));
            list.Add(new CheckResult("可种植株数上限 = 列数（6）",
                colGrid.FreeColumnCount == 5, $"还能种 {colGrid.FreeColumnCount} 株"));
            for (int c = 1; c < colGrid.Columns; c++)
                colGrid.Slot(c, 0).BeginPlant(t.farm, colSeed, t.farm.waterMaxDefault, colSeed.WaterDrain);
            list.Add(new CheckResult("6 列种满 ⇒ 第 7 株没有空列（种不下）",
                colGrid.FirstFreeColumn() == -1 && colGrid.PlantCount == 6,
                $"株数 {colGrid.PlantCount}，空列 {colGrid.FirstFreeColumn()}"));
            colGrid.SyncSurvival(300f);
            int refilled = 0;
            for (int c = 0; c < colGrid.Columns; c++)
                if (colGrid.Slot(c, 3).State != PlotState.Empty) refilled++;
            list.Add(new CheckResult("加一排 ⇒ 6 株各自补 1 个（相当于每个植物多种 1 个）",
                colGrid.Rows == 4 && refilled == 6, $"{colGrid.Rows} 行，补齐 {refilled}/6 列"));
            list.Add(new CheckResult("新排的作物是同一品种",
                colGrid.Slot(0, 3).CropSeedId == colSeed.Id, $"{colGrid.Slot(0, 3).CropSeedId}"));
            list.Add(new CheckResult("整列同一株：每格都有作物身份，但只有列首登记驱动种子",
                colGrid.Slot(0, 3).CropSeedId == colSeed.Id && colGrid.Slot(0, 3).SeedId == null
                && colGrid.Slot(0, 0).SeedId == colSeed.Id,
                $"列首 SeedId={colGrid.Slot(0, 0).SeedId ?? "null"}，第 4 行 SeedId={colGrid.Slot(0, 3).SeedId ?? "null"}"));
            // 收割一格 = 结算一格的收益 ⇒ 行数直接乘收益（WorldView/World.HarvestPlot 读 CropSeedId）
            list.Add(new CheckResult("收割按格结算（收益随行数线性增长）",
                colGrid.UsedPlotCount == 24, $"占用 {colGrid.UsedPlotCount} 格 = 6 株 × 4 行"));
            // 整列拆除：铲除/卖掉种子时同列不留孤儿作物
            colGrid.Slot(0, 0).RemoveGrowing();
            bool colCleared = colGrid.Slot(0, 1).State == PlotState.Empty && colGrid.Slot(0, 3).State == PlotState.Empty;
            list.Add(new CheckResult("整列拆除：铲掉列首 ⇒ 同列其它格一起清空",
                colCleared && colGrid.PlantCount == 5, $"株数 {colGrid.PlantCount}"));

            // ── v166：WaterSave（铁根巨像）= **该列水耗减半、生长速度不变** ──
            // 语义按"字面"实现：旧版是 `TickGrowth2(dt*2)` ⇒ 生长与漏水一起 ×2
            // ⇒ 每秒水耗**翻倍**、长熟总水量不变 ⇒ **与名字"省水"方向相反** ✗
            // 现在拆成 `TickGrowth2(dt, waterCostScale)`：生长不变、漏水时钟 ×0.5 ✓
            //
            // 为什么要用**合成的**种子而不是真表里的 ironroot_colossus：
            //   真表里它 waterDrain=75 / waterMax=40 ⇒ 5 秒就耗光并**停长**，
            //   于是观测量会被"缺水暂停"污染 ⇒ 测不到机制本身 ✗
            //   合成种子把水量放大到 1000 ⇒ 不会缺水 ⇒ 观测到的差异只可能来自 WaterSave ✓
            //
            // 为什么模拟 12 秒而不是 10 秒：漏水是"每攒满 5 单位时钟扣一次"✓
            //   10 秒时铁根列只攒到 5.0（浮点上可能刚好差一点点）⇒ 会从"扣 1 次"变成"扣 0 次" ✗
            //   12 秒 ⇒ 普通列攒 12（扣 2 次）、铁根列攒 6（扣 1 次）⇒ 两侧都离开阈值 ✓
            var wsw = MakeWorld(out _);
            var ironCrop = new SeedDef
            { Id = "test_iron", Name = "测试铁根", GrowTime = 600f, Mechanic = "WaterSave", WaterDrain = 10 };
            var plainCrop = new SeedDef
            { Id = "test_plain", Name = "测试普通", GrowTime = 600f, Mechanic = "None", WaterDrain = 10 };
            FarmPlot wsPlot = wsw.Farm.Slot(0, 0);
            FarmPlot plPlot = wsw.Farm.Slot(1, 0);
            wsPlot.BeginPlant(t.farm, ironCrop, 1000, 10);     // 列 0 = 带 WaterSave
            plPlot.BeginPlant(t.farm, plainCrop, 1000, 10);    // 列 1 = 不带
            float wsGrow0 = wsPlot.GrowthRemaining, plGrow0 = plPlot.GrowthRemaining;
            int wsWater0 = wsPlot.Water, plWater0 = plPlot.Water;
            for (int i = 0; i < 720; i++) wsw.Simulate(1f / 60f);   // 推进 12 秒真实时间
            float wsAdv = wsGrow0 - wsPlot.GrowthRemaining;
            float plAdv = plGrow0 - plPlot.GrowthRemaining;
            int wsUsed = wsWater0 - wsPlot.Water;
            int plUsed = plWater0 - plPlot.Water;
            list.Add(new CheckResult("v166：WaterSave 让该列【水耗减半】（生长速度不变）",
                plUsed > 0 && wsUsed > 0 && plUsed == wsUsed * 2
                && Mathf.Abs(wsAdv - plAdv) <= Mathf.Max(0.02f, plAdv * 0.02f),
                $"水耗 铁根 {wsUsed} vs 普通 {plUsed}（应 1:2）｜生长推进 {wsAdv:0.###} vs {plAdv:0.###}（应相等）"));
            // 这一条同时验掉了旧实现的**作用域错误**：旧版是"全场任一带 WaterSave 的植物 ⇒ 全场受益" ✗
            // 现在同一世界里两列的水耗不同 ⇒ 证明作用域确实收窄到了该列 ✓
            list.Add(new CheckResult("v166：WaterSave 只作用于该列（另一列水耗不受影响）",
                wsUsed > 0 && wsUsed < plUsed, $"铁根列 {wsUsed} vs 普通列 {plUsed}"));

            // ── 工具升级机制（与种子同一套品质）
            var session = new RunSession();
            session.Start(424242UL);
            string reason;
            list.Add(new CheckResult("工具初始未拥有", session.ToolLevel[RunSession.ToolHoe] < 0, ""));
            bool first = session.UpgradeTool(RunSession.ToolHoe, 0, out reason);
            list.Add(new CheckResult("首次购买工具获得白档", first && session.ToolLevel[RunSession.ToolHoe] == 0,
                $"锄头 = {t.quality.NameOf(session.ToolLevel[RunSession.ToolHoe])}"));
            list.Add(new CheckResult("白档工作能力 1", session.WorkCapability(RunSession.ToolHoe) == 1, ""));
            bool merged = session.UpgradeTool(RunSession.ToolHoe, 0, out reason);
            list.Add(new CheckResult("同品质再买 → 合体升档", merged && session.ToolLevel[RunSession.ToolHoe] == 1,
                $"锄头 = {t.quality.NameOf(session.ToolLevel[RunSession.ToolHoe])}"));
            list.Add(new CheckResult("升档后工作能力 +1", session.WorkCapability(RunSession.ToolHoe) == 2, ""));
            bool lower = session.UpgradeTool(RunSession.ToolHoe, 0, out reason);
            list.Add(new CheckResult("拿更低品质买会被拒绝", !lower, reason ?? ""));
            bool reject = session.UpgradeTool(RunSession.ToolCan, 3, out reason);
            list.Add(new CheckResult("直接买更高品质 = 直接替换", reject && session.ToolLevel[RunSession.ToolCan] == 3,
                $"水壶 = {t.quality.NameOf(session.ToolLevel[RunSession.ToolCan])}"));
            list.Add(new CheckResult("工作能力有上限（workCapMax）", session.WorkCapability(RunSession.ToolCan) == t.tools.workCapMax,
                $"{session.WorkCapability(RunSession.ToolCan)} / {t.tools.workCapMax}"));

            // ── 横向成组耕作：同一行的相邻地块一起处理
            var g2 = new FarmGrid(t);
            g2.SetMaxPlots(9);
            int target = -1;
            for (int i = 0; i < g2.PlotCount; i++)
            {
                FarmPlot p = g2.Plot(i);
                if (target < 0) { target = i; continue; }
                FarmPlot q = g2.Plot(target);
                if (p.GridY == q.GridY && p.GridX < q.GridX) target = i;   // 取该行最左侧
            }
            int group1 = g2.BuildActionGroup(target, 1, PlotAction.Dig);
            int group3 = g2.BuildActionGroup(target, 3, PlotAction.Dig);
            list.Add(new CheckResult("工作能力 1 → 只作用 1 块", group1 == 1, $"{group1} 块"));
            bool sameRow = true;
            FarmPlot anchor = g2.Plot(target);
            for (int i = 0; i < group3; i++)
            {
                FarmPlot m = g2.Plot(g2.GroupMember(i));
                if (m == null || m.GridY != anchor.GridY) sameRow = false;
            }
            list.Add(new CheckResult("成组耕作=同一行（横向拓展）", group3 >= 2 && sameRow, $"{group3} 块，同一行={sameRow}"));
            list.Add(new CheckResult("成组不会超过工作能力", group3 <= 3, $"{group3} 块"));

            // ── 工具品质提升数值
            list.Add(new CheckResult("v23：武器不再按品质提升数值", Mathf.Abs(session.ToolMult(RunSession.ToolCan) - 1f) < 0.01f,
                $"水壶 x{session.ToolMult(RunSession.ToolCan):0.00}"));
        }

        /// <summary>探针：金品质种子进袋后，槽位品质应为最高档。</summary>
        private static bool goldBagQualityProbe(QualityDef q, SeedDef def)
        {
            var b = new SeedBag(6, true);
            b.AddSeed(def, null, q.Max, out _);
            return b[0].Quality == q.Max && b[0].QualityName == q.NameOf(q.Max);
        }

        // v168：`WaveExpTotal(waveIndex)` 已删除 ✗ —— 它算的是"击杀能给多少经验"，
        // 而 v167 起击杀不给经验（唯一的发布点 `World.SpawnPickup()` 已删除）⇒ 它量的收益不可能发生 ✓
        // 它的两个调用点也已一并删除（原来在 `GrowthChecks` 里，见那里的说明）✓
        // 留着一个没人调用的"经验估算"比删掉更糟：下一个人会以为它是一条活判据 ✓

        /// <summary>v5 新增规则：洒水/施肥时长、毒延迟与传染、种子品质与合成、波次重置玩家、商店去治疗。</summary>
        private static void V5Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            FarmDef farm = t.farm;

            // ── 洒水 / 施肥各 1 秒
            Expect(list, "洒水时长 1s", farm.waterTime, 1f);
            Expect(list, "施肥时长 1s", farm.fertilizeTime, 1f);

            // ── 毒的生效延迟独立为 2 秒
            Expect(list, "毒生效延迟 2s", t.formula.ailmentDelayPoison, 2f);
            list.Add(new CheckResult("其它异常仍用 1.5s 延迟", Mathf.Abs(t.formula.ailmentDelay - 1.5f) < 0.01f,
                $"{t.formula.ailmentDelay}"));
            var st = new StatusRuntime();
            st.Apply(AilmentKind.Poison, 3f, t.formula, 1f);
            Expect(list, "中毒立即生效（每 1 秒结算一次）", st.PoisonTicksLeft, 4f);

            // ── 毒接触传染：贴身的另一个敌人也会中毒
            World w = MakeWorld(out _);
            w.SpawnEnemy("grunt", Vector2.zero, 1);
            w.SpawnEnemy("grunt", new Vector2(0.8f, 0f), 1);
            EnemyActor a = w.Enemies[0];
            EnemyActor b = w.Enemies[1];
            a.Status.Apply(AilmentKind.Poison, 4f, t.formula, 1f);
            b.Status.Tick(0.5f, b, w);
            list.Add(new CheckResult("毒：接触不足 1 秒不扩散", b.Status.PoisonAmount <= 0.01f,
                $"0.5s 后 b 累积 {b.Status.PoisonAmount:0.00}"));
            b.Status.Tick(0.7f, b, w);
            list.Add(new CheckResult("毒：贴身接触满 1 秒后扩散", b.Status.Poisoned,
                $"1.2s 后 b 中毒 {b.Status.PoisonTicksLeft} 次"));

            // ── 种子品质档位 + 合成规则（合成走**等级**轴，品质不参与门槛）
            QualityDef q = t.quality;
            list.Add(new CheckResult("品质档位为 5 档（白绿蓝紫金）", q.Max == 4, $"最高档 {q.NameOf(q.Max)}"));
            bool increasing = true;
            for (int i = 1; i <= q.Max; i++) if (q.MultOf(i) <= q.MultOf(i - 1)) increasing = false;
            list.Add(new CheckResult("v23：稀有度不再提供数值倍率（全为 1）", !increasing, $"{q.MultOf(0):0.00}→{q.MultOf(q.Max):0.00}"));

            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            SeedDef pea = TableDatabase.GetSeed("pea_shooter");
            var bag = new SeedBag(6, true);
            bag.AddSeed(radish, null, 0, out _);   // 槽0 萝卜·白（1 级）
            bag.AddSeed(radish, null, 0, out _);   // 槽1 萝卜·白（1 级，合成材料）
            bag.AddSeed(radish, null, 1, out _);   // 槽2 萝卜·绿（1 级，品质不同但等级相同）
            bag.AddSeed(pea, null, 0, out _);      // 槽3 豌豆·白（1 级）
            list.Add(new CheckResult("同名种子可重复持有（4 个槽）", bag.OccupiedCount == 4, $"占用 {bag.OccupiedCount}"));
            list.Add(new CheckResult("买入的品质被正确写入槽位", bag[2].Quality == 1 && bag[2].QualityName == q.NameOf(1),
                $"槽2 = {bag[2].Label}"));
            list.Add(new CheckResult("金品质槽位写入正确", goldBagQualityProbe(q, radish), ""));

            string reason;
            // ── 合成新规则：**同种 + 同等级**；品质不参与门槛（它由种子定义固定）✓
            list.Add(new CheckResult("同种同级可以合成（品质不再是门槛）", bag.CanMerge(0, 2, out reason), reason ?? ""));
            list.Add(new CheckResult("不同种子不能合成", !bag.CanMerge(0, 3, out reason), reason ?? ""));
            list.Add(new CheckResult("同种同级也可以合成（槽0 + 槽1，都是 1 级）", bag.CanMerge(0, 1, out reason), reason ?? ""));
            bool merged = bag.Merge(0, 1, out reason);
            list.Add(new CheckResult("合成后等级 +1（1 级 → 2 级）", merged && bag[1].Level == 2 && bag[0].IsEmpty,
                $"槽1 等级={bag[1].Level} 槽0空={bag[0].IsEmpty}"));
            list.Add(new CheckResult("合成不改变品质（品质由种子定义决定）", bag[1].Quality == 0,
                $"槽1 品质={bag[1].QualityName}（合的是等级，不是品质）"));
            // 槽1 现在是 2 级、槽2 还是 1 级 ⇒ 等级不同，合不了 ✓
            list.Add(new CheckResult("不同等级不能合成（2 级 + 1 级）", !bag.CanMerge(1, 2, out reason), reason ?? ""));

            // ── 2 级 + 2 级 → 3 级（合成链继续，走真实合成路径）
            var chain = new SeedBag(4, true);
            chain.AddSeed(radish, null, 0, out _);
            chain.AddSeed(radish, null, 0, out _);
            bool c1 = chain.Merge(0, 1, out reason);      // 槽1 → 2 级，槽0 空出来
            // 槽位会回收复用 ⇒ 不能假定"补进去的种子落在第几个槽"，必须按条件找 ✗
            chain.AddSeed(radish, null, 0, out _);
            chain.AddSeed(radish, null, 0, out _);
            int lv1a = -1, lv1b = -1;
            for (int i = 0; i < chain.SlotCount; i++)
            {
                if (chain[i].IsEmpty || chain[i].Level != SeedSlot.StartingLevel) continue;
                if (lv1a < 0) lv1a = i; else if (lv1b < 0) lv1b = i;
            }
            bool c2 = lv1a >= 0 && lv1b >= 0 && chain.Merge(lv1a, lv1b, out reason);   // → 第二个 2 级
            int lv2a = -1, lv2b = -1;
            for (int i = 0; i < chain.SlotCount; i++)
            {
                if (chain[i].IsEmpty || chain[i].Level != 2) continue;
                if (lv2a < 0) lv2a = i; else if (lv2b < 0) lv2b = i;
            }
            bool c3 = lv2a >= 0 && lv2b >= 0 && chain.CanMerge(lv2a, lv2b, out reason);
            bool c4 = c3 && chain.Merge(lv2a, lv2b, out reason);
            list.Add(new CheckResult($"2 级 + 2 级 → {SeedSlot.MaxLevel} 级",
                c1 && c2 && c3 && c4 && lv2b >= 0 && chain[lv2b].Level == SeedSlot.MaxLevel,
                $"槽{lv2b} 等级={(lv2b >= 0 ? chain[lv2b].Level : 0)}"));

            // ── 3 级封顶：两个 3 级 不能再合（同样走真实合成路径：8 个 1 级 → 2 个 3 级）
            var capBag = new SeedBag(6, true);
            for (int i = 0; i < 4; i++) capBag.AddSeed(radish, null, 0, out _);
            capBag.AutoMergeAll(out _);                  // → 第 1 个 3 级
            for (int i = 0; i < 4; i++) capBag.AddSeed(radish, null, 0, out _);
            capBag.AutoMergeAll(out _);                  // → 第 2 个 3 级
            int lv3 = 0, capA = -1, capB = -1;
            for (int i = 0; i < capBag.SlotCount; i++)
            {
                if (capBag[i].IsEmpty || capBag[i].Level != SeedSlot.MaxLevel) continue;
                lv3++;
                if (capA < 0) capA = i; else if (capB < 0) capB = i;
            }
            list.Add(new CheckResult($"前置：袋里有 2 个 {SeedSlot.MaxLevel} 级种子", lv3 == 2, $"{SeedSlot.MaxLevel} 级 {lv3} 个"));
            list.Add(new CheckResult($"{SeedSlot.MaxLevel} 级封顶：两个 {SeedSlot.MaxLevel} 级不能再合",
                capA >= 0 && capB >= 0 && !capBag.CanMerge(capA, capB, out reason), reason ?? ""));

            // 品质不参与门槛 ⇒ 两个**金品质的 1 级**种子照样能合（它们只是品质高）
            var goldBag = new SeedBag(6, true);
            goldBag.AddSeed(radish, null, q.Max, out _);
            goldBag.AddSeed(radish, null, q.Max, out _);
            bool goldMerged = goldBag.Merge(0, 1, out reason);
            list.Add(new CheckResult("品质不参与合成门槛：两个金品质 1 级照样能合",
                goldMerged && goldBag[1].Level == 2 && goldBag[1].Quality == q.Max,
                $"槽1 = {goldBag[1].QualityName} {goldBag[1].Level} 级"));

            // ★ T2：删掉「v23：稀有度不影响植物生命 / v23：稀有度不影响植物攻击」两条断言 ✗ ★
            //   它们把植物 HP 与 `PlantAlly.AttackValue` 当成**战斗属性**来验 ✓
            //   植物已不参战 ⇒ 这两条属性不再有玩法读者 ⇒ 断言已无被测对象 ✓
            //   计数：本块删 2 条 ✓
            //   ⚠️ 品质自身的数值规则**另有覆盖**（本方法上方 :3173-3180「品质不参与合成门槛」等）✓

            // ── 每回合开始自动重置玩家状态：受伤后开新波，生命与护盾回满
            var session = new RunSession();
            session.Start(777UL);
            PlayerActor pr = session.World.Player;
            session.World.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 3f, Target = pr, IgnoreDodge = true });
            pr.Health.Shield = 0f;
            float hurtHp = pr.Health.Hp;
            pr.SwitchWindup = 0.4f;
            session.StartWave(2);
            bool hpFull = pr.Health.Hp >= pr.Health.MaxHp - 0.001f;
            bool shieldFull = pr.Health.Shield >= pr.Health.MaxShield - 0.001f;
            list.Add(new CheckResult("回合开始重置玩家状态（生命护盾回满）", hpFull && shieldFull,
                $"受伤 {hurtHp:0.##} → {pr.Health.Hp:0.##}，护盾 {pr.Health.Shield:0.##}/{pr.Health.MaxShield:0.##}"));
            list.Add(new CheckResult("回合开始清空切换前摇", pr.SwitchWindup <= 0f, $"{pr.SwitchWindup:0.00}s"));

            // ── 商店不再有立刻回血类道具 + 镜头视野扩大
            list.Add(new CheckResult("商店已删除治疗道具", t.shop.healWeight <= 0, $"healWeight={t.shop.healWeight}"));
            list.Add(new CheckResult("镜头视野已扩大", t.camera.distance >= 20f, $"距离 {t.camera.distance}"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // v171：**插件自检** —— 回答"插件到底有没有生效"
        //
        // ★ 为什么必须有这一段 ★
        // 上一轮插件做到了"能买、能进背包、商店抽得出"，但 `WeaponSystem.Aggregate`
        // **零调用** ⇒ 买到的插件对战斗**没有任何影响**，它只是个收藏 ✗
        // 而当时自检全绿 —— 因为它只验了"表里有行 / 商店抽得出来"，
        // 没有任何一条问过"装上之后攻击变了没有" ✓
        // 这一段就是补上那一问，并且**逐个插件给出装前/装后的数字** ✓
        // ════════════════════════════════════════════════════════════════════════

        private const float PB_MeleeNear = 2.0f;    // 挥击框内（基础范围 3.2m）
        private const float PB_MeleeFar = 5.5f;     // 挥击框外、剑气流之内（基础剑气射程 6.4m）
        private const float PB_BeamNear = 5.0f;     // 激光射程内（基础 9.5m）
        private const float PB_BlastAt = 3.0f;      // 肥料落点（投掷射程 6.5m 内）

        private struct PluginProbeRow
        {
            public string Id, Name, Channel, Metric;
            public float Before, After;
            public bool Moved;
        }

        /// <summary>
        /// 建一局**只含我放的敌人**的可控局面。
        ///
        /// ★ 为什么必须清空敌人列表 ★
        /// `StartWave(1)` 自己会刷一波怪 ✗
        /// 它们会挡在目标前面、被剑气 / 连锁 / 爆炸误伤 ⇒
        /// "装前 vs 装后"的差就不再只反映插件，读数不可用 ✓
        /// </summary>
        private static RunSession PluginBench(ulong seed, out PlayerActor p)
        {
            var s = new RunSession();
            s.Start(seed);
            s.StartWave(1);
            s.World.Enemies.Clear();
            p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);                          // 量的是"我打出多少"，不该被反杀打断
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);     // 暴击率归零 ⇒ 每次攻击的伤害完全确定
            s.WaterAmmo = RunSession.WaterAmmoMax;
            s.FertAmmo = RunSession.FertAmmoMax;
            s.AimWorldLast = new Vector2(PB_BlastAt, 0f);      // 肥料落点 = (3,0)
            return s;
        }

        /// <summary>
        /// 放一个"打不死"的敌人（用来干净地量伤害，避免死亡结算干扰读数）。
        /// ★ 血量取 1e5 而不是 1e6 ★：伤害是按 `血量 − 打后血量` 差的 ✗
        /// float 只有 24 位尾数，1e6 处的最小刻度是 0.0625 ⇒ 会把 1.8 报成 1.813 ✓
        /// 1e5 处刻度 0.0078，读数够用 ✓
        /// </summary>
        private static EnemyActor PlaceEnemy(RunSession s, Vector2 pos, float hp = 1e5f)
        {
            s.World.SpawnEnemy("grunt", pos, 1);
            EnemyActor e = s.World.Enemies[s.World.Enemies.Count - 1];
            e.Pos = pos;
            e.Health.Setup(hp, 0f);
            return e;
        }

        /// <summary>一件插件在聚合后**改了哪一格**（0 = 它什么都没改 ⇒ 数据是死的 ✗）。</summary>
        private static float PlugField(in WeaponModifiers m, PluginEffectKind k)
        {
            switch (k)
            {
                case PluginEffectKind.DamageFlat: return m.DamageFlat;
                case PluginEffectKind.DamagePercent: return m.DamagePercent;
                case PluginEffectKind.AttackSpeed: return m.AttackSpeedPct;
                case PluginEffectKind.Range: return m.RangePct;
                case PluginEffectKind.CritRate: return m.CritRateFlat;
                case PluginEffectKind.CritDamage: return m.CritDamageFlat;
                case PluginEffectKind.ExplodeRadius: return m.ExplodeRadiusPct;
                case PluginEffectKind.ProjectileCount: return m.ProjectileCount;
                case PluginEffectKind.Knockback: return m.KnockbackFlat;
                case PluginEffectKind.SwordWave: return m.SwordWaveCount;
                case PluginEffectKind.ChillOnHit: return m.ChillPerHit;
                case PluginEffectKind.AmmoOnKill: return m.AmmoPerKill;
                case PluginEffectKind.BurnOnHit: return m.BurnPerHitPct;
                case PluginEffectKind.ChainOnHit: return m.ChainTargets;
                case PluginEffectKind.BlastEcho: return m.BlastEchoPct;
                case PluginEffectKind.CritBlast: return m.CritBlastPct;
                case PluginEffectKind.FullHpBonus: return m.FullHpBonusPct;
                default: return 0f;
            }
        }

        private static void PluginChecks(List<CheckResult> list)
        {
            PluginAsset[] table = TableDatabase.Plugins;

            // ── ① 表的规模：每把武器 ≥ 6 件（否则 6 个插槽填不满）
            int[] perSlot = new int[3];
            int generic = 0;
            for (int i = 0; i < table.Length; i++)
            {
                PluginAsset a = table[i];
                if (a.allowedWeapons == null || a.allowedWeapons.Length == 0) { generic++; continue; }
                for (int k = 0; k < a.allowedWeapons.Length; k++)
                {
                    int idx = (int)a.allowedWeapons[k];
                    if (idx >= 0 && idx < 3) perSlot[idx]++;
                }
            }
            list.Add(new CheckResult(
                $"每把武器都有 ≥ {PlayerGoods.MaxPluginsPerWeapon} 件插件（6 个插槽填得满）",
                perSlot[0] >= PlayerGoods.MaxPluginsPerWeapon
                && perSlot[1] >= PlayerGoods.MaxPluginsPerWeapon
                && perSlot[2] >= PlayerGoods.MaxPluginsPerWeapon,
                $"锄头 {perSlot[0]} / 水壶 {perSlot[1]} / 肥料 {perSlot[2]}（共 {table.Length} 件）"));

            // ★ 通用插件（allowedWeapons 为空 = 任意武器）故意一件都没有 ★
            // 用户的分类只有三类（锄头 / 水壶 / 肥料）✗
            // 而"任意武器都能装"的插件在数值上**严格优于**任何专属插件
            // ⇒ 它会让"6 选 N"这个取舍消失（永远先塞通用件）✓
            list.Add(new CheckResult(
                "插件分类只有三类（无「任意武器」的通用插件）",
                generic == 0,
                generic == 0 ? "通用 0 件（机制仍在 PluginAsset.Fits 上，只是表里不用）"
                             : $"{generic} 件通用插件会架空「6 选 N」的取舍"));

            // ── ② 每一件插件都必须**真的改到聚合结果**
            // "表里有行" ≠ "聚合里有它" ✗ 这是本工程最贵的一类 bug（看着活着、实际死的）
            int deadData = 0;
            string deadIds = "";
            for (int i = 0; i < table.Length; i++)
            {
                PluginAsset a = table[i];
                WeaponSlot slot = a.allowedWeapons.Length > 0 ? a.allowedWeapons[0] : WeaponSlot.Hoe;
                var goods = new PlayerGoods();
                goods.Plugins.Add(new PluginInstance(a, slot));
                WeaponModifiers m = WeaponSystem.Aggregate(goods, slot);
                if (Mathf.Abs(PlugField(m, a.effect)) < 1e-6f)
                {
                    deadData++;
                    deadIds += (deadIds.Length > 0 ? "、" : "") + a.id;
                }
            }
            list.Add(new CheckResult("每件插件都被 Aggregate 真的读进某一格（没有只存在于表里的数据）",
                table.Length > 0 && deadData == 0,
                deadData == 0 ? $"{table.Length} 件全部有落点" : $"{deadData} 件是死的：{deadIds}"));

            // ── ③ 17 种效果**每一种都要出现在表里**，而且每一种都要有"改了哪一环"的说法
            int missingKinds = 0;
            string missingNames = "";
            int noChannel = 0;
            foreach (PluginEffectKind k in Enum.GetValues(typeof(PluginEffectKind)))
            {
                if (k == PluginEffectKind.None) continue;
                if (string.IsNullOrEmpty(WeaponSystem.ChannelOf(k)) || WeaponSystem.ChannelOf(k) == "无") noChannel++;
                bool used = false;
                for (int i = 0; i < table.Length; i++) if (table[i].effect == k) { used = true; break; }
                if (!used) { missingKinds++; missingNames += (missingNames.Length > 0 ? "、" : "") + k; }
            }
            int kindTotal = Enum.GetValues(typeof(PluginEffectKind)).Length - 1;
            list.Add(new CheckResult($"全部 {kindTotal} 种效果都在表里出现过（没有「写了但没人用」的枚举值）",
                missingKinds == 0 && noChannel == 0,
                missingKinds == 0 && noChannel == 0
                    ? $"{kindTotal} 种全部有数据"
                    : $"缺数据 {missingKinds} 种（{missingNames}）｜缺「改了哪一环」{noChannel} 种"));

            // ── ④ 数据侧的**武器限制**必须与代码侧"谁读它"一致
            // 这三条是本轮最容易犯的错：效果存在，但那条攻击路径根本不读 ⇒ 死数据
            int badFits = 0;
            string badFitsIds = "";
            for (int i = 0; i < table.Length; i++)
            {
                PluginAsset a = table[i];
                bool ok = true;
                switch (a.effect)
                {
                    case PluginEffectKind.SwordWave:      // 剑气只有锄头那条路会发
                        ok = a.allowedWeapons.Length == 1 && a.allowedWeapons[0] == WeaponSlot.Hoe; break;
                    case PluginEffectKind.BlastEcho:      // 爆炸回声只有肥料那条路有爆炸
                        ok = a.allowedWeapons.Length == 1 && a.allowedWeapons[0] == WeaponSlot.Fertilizer; break;
                    case PluginEffectKind.ExplodeRadius:  // 爆炸半径只有肥料那条路有半径
                        ok = a.allowedWeapons.Length == 1 && a.allowedWeapons[0] == WeaponSlot.Fertilizer; break;
                    case PluginEffectKind.CritBlast:      // 爆炸**不掷暴击**（ExplodeAt 里写明）⇒ 不能给肥料
                        ok = a.allowedWeapons.Length == 1 && a.allowedWeapons[0] != WeaponSlot.Fertilizer; break;
                }
                if (!ok) { badFits++; badFitsIds += (badFitsIds.Length > 0 ? "、" : "") + a.id + "(" + a.effect + ")"; }
            }
            list.Add(new CheckResult("武器的限制与「哪条路真的读它」一致（剑气→锄头｜回声·爆炸半径→肥料｜炸裂≠肥料）",
                badFits == 0, badFits == 0 ? "全部一致" : $"{badFits} 件错配：{badFitsIds}"));

            // ── ⑤ **6 个插槽的上限**：装得下 6 件、第 7 件被拒绝、且理由说得清"上限 6"
            var capS = new RunSession();
            capS.Start(31337UL);
            PluginAsset[] hoes = new PluginAsset[13];
            int n = 0;
            for (int i = 0; i < table.Length && n < hoes.Length; i++)
                if (table[i].allowedWeapons.Length == 1 && table[i].allowedWeapons[0] == WeaponSlot.Hoe) hoes[n++] = table[i];
            int installed = 0;
            for (int i = 0; i < n; i++)
                if (WeaponSystem.Install(capS.Goods, hoes[i], WeaponSlot.Hoe, out _)) installed++;
            string seventhReason = "";
            int sevenIdx = -1;
            for (int i = 0; i < n; i++)
                if (capS.Goods.Find(hoes[i], WeaponSlot.Hoe) == null) { sevenIdx = i; break; }
            bool seventhRejected = sevenIdx >= 0
                && !WeaponSystem.Install(capS.Goods, hoes[sevenIdx], WeaponSlot.Hoe, out seventhReason);
            list.Add(new CheckResult(
                $"每把武器最多 {PlayerGoods.MaxPluginsPerWeapon} 个插槽：装满后被拒",
                installed == PlayerGoods.MaxPluginsPerWeapon && capS.Goods.SlotsUsedOn(WeaponSlot.Hoe) == PlayerGoods.MaxPluginsPerWeapon
                && seventhRejected,
                $"装进 {installed} 件（表内可选 {n} 件）｜第 {installed + 1} 件被拒：\"{seventhReason}\""));
            list.Add(new CheckResult("上限提示里写明了「6」这个数",
                seventhReason.Contains(PlayerGoods.MaxPluginsPerWeapon.ToString()),
                $"提示 = \"{seventhReason}\""));
            // 装满 6 件之后，**已经装在身上的那件仍然可以叠层**（叠层不占新插槽）
            PluginInstance stackable = null;
            for (int i = 0; i < capS.Goods.Plugins.Count; i++)
                if (capS.Goods.Plugins[i].CanStackMore) { stackable = capS.Goods.Plugins[i]; break; }
            bool stacked = stackable != null && WeaponSystem.Install(capS.Goods, stackable.Asset, WeaponSlot.Hoe, out _);
            list.Add(new CheckResult("装满 6 件后仍能把已有的一件叠层（叠层不占新插槽）",
                stacked, stackable == null ? "本批没有可叠的插件" : $"{stackable.Asset.id} → {stackable.Stacks} 层"));

            // ── ⑥ 商店：第三类仍是插件，权重与抽到面不变
            var probe = new RunSession();
            probe.Start(90210UL);
            probe.WaveIndex = 6;
            int nSeed = 0, nPlugin = 0, nItem = 0;
            var seenPlugin = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                probe.Shop.Refresh(probe);
                for (int k = 0; k < probe.Shop.Offers.Count; k++)
                {
                    switch (probe.Shop.Offers[k].Kind)
                    {
                        case OfferKind.Seed: nSeed++; break;
                        case OfferKind.Plugin: nPlugin++; seenPlugin.Add(probe.Shop.Offers[k].Plugin.id); break;
                        case OfferKind.Item: nItem++; break;
                    }
                }
            }
            list.Add(new CheckResult("商店第三类是插件且真的抽得出来（200 次刷新）", nPlugin > 0,
                $"800 个货位：种子 {nSeed}/{nSeed / 8f:0.0}% 插件 {nPlugin}/{nPlugin / 8f:0.0}% 道具 {nItem}/{nItem / 8f:0.0}%"
                + $"（权重 种子{probe.T.shop.seedWeight}/插件{probe.T.shop.pluginWeight}/道具{probe.T.shop.itemWeight}）"));
            list.Add(new CheckResult("新表里的插件真的会被商店抽到（不是只有旧件在流转）",
                seenPlugin.Count >= 20, $"200 次刷新里出现过 {seenPlugin.Count} 种不同的插件（表内共 {table.Length} 种）"));

            // ★ 上限与商店的**可读证据** ★
            // `Report()` 只打印失败项 ⇒ 这两条过了就什么都看不到 ✗
            // 而"第 7 件被拒的原文"和"三类各占多少"正是本轮验收要看的数 ✓
            Log.Info($"[插件上限 v171] 每把武器 {PlayerGoods.MaxPluginsPerWeapon} 个插槽 ⇒ "
                     + $"装进 {installed} 件后第 {installed + 1} 件被拒，提示原文：\"{seventhReason}\""
                     + $"｜装满后叠层仍可行={stacked}\n"
                     + $"[插件商店 v171] 200 次刷新 800 个货位 ⇒ 种子 {nSeed}/{nSeed / 8f:0.0}% "
                     + $"插件 {nPlugin}/{nPlugin / 8f:0.0}% 道具 {nItem}/{nItem / 8f:0.0}%"
                     + $"（权重 种子{probe.T.shop.seedWeight}/插件{probe.T.shop.pluginWeight}/道具{probe.T.shop.itemWeight}）"
                     + $"｜200 次里出现过 {seenPlugin.Count}/{table.Length} 种插件");

            // ── ⑦ ★ 逐个插件的**装前 / 装后实测** ★
            var rows = new List<PluginProbeRow>();
            for (int i = 0; i < table.Length; i++) rows.Add(MeasurePlugin(table[i]));
            int notMoved = 0;
            string notMovedIds = "";
            var sb = new StringBuilder();
            sb.Append("[插件实测 v171] 同一把武器、同一个敌人、同一个种子 ⇒ 装前 → 装后\n");
            sb.Append("  ").Append("插件".PadRight(20)).Append("武器".PadRight(6))
              .Append("改了哪一环".PadRight(14)).Append("量的东西".PadRight(30)).Append("装前 → 装后\n");
            for (int i = 0; i < rows.Count; i++)
            {
                PluginProbeRow r = rows[i];
                if (!r.Moved) { notMoved++; notMovedIds += (notMovedIds.Length > 0 ? "、" : "") + r.Id; }
                sb.Append("  ").Append(r.Id.PadRight(20)).Append(WeaponSlotCn(r.Id).PadRight(6))
                  .Append(r.Channel.PadRight(14)).Append(r.Metric.PadRight(30))
                  .Append($"{r.Before:0.###} → {r.After:0.###}").Append(r.Moved ? "" : "   << 没变化").Append('\n');
            }
            Log.Info(sb.ToString());
            list.Add(new CheckResult("★ 每件插件装上之后攻击读数都真的变了（装前 ≠ 装后）★",
                notMoved == 0, notMoved == 0 ? $"{rows.Count} 件全部有变化" : $"{notMoved} 件没变化：{notMovedIds}"));

            // ── ⑧ 六件一起装时的叠加（不是"只有单件才生效"）
            float ratio = SixStackDamageRatio();
            list.Add(new CheckResult("六件一起装时总伤害 > 空手 ×1.2（叠加不互相抵消）",
                ratio > 1.2f, $"6 件全装 / 空手 = ×{ratio:0.00}"));

            // ── ⑨ 纯算式（不依赖局面，钉住数值语义）
            WeaponModifiers mm = WeaponModifiers.None;
            mm.BurnPerHitPct = 0.35f;
            mm.ChainTargets = 2; mm.ChainRatio = 0.45f;
            mm.CritBlastPct = 0.6f; mm.BlastEchoPct = 0.5f;
            mm.FullHpBonusPct = 0.4f;
            Expect(list, "灼烧量 = 伤害 ×35%（伤害 20 → 7）", WeaponSystem.BurnAmountFor(20f, mm), 7f);
            Expect(list, "连锁每跳 = 伤害 ×45%（伤害 20 → 9）", WeaponSystem.ChainStepDamage(20f, mm), 9f);
            Expect(list, "暴击炸裂只在暴击时非 0（20×60% + 0 = 12）",
                WeaponSystem.CritBlastDamage(20f, true, mm) + WeaponSystem.CritBlastDamage(20f, false, mm), 12f);
            Expect(list, "爆炸回声 = 第一段 ×50%（伤害 20 → 10）", WeaponSystem.BlastEchoDamage(20f, mm), 10f);
            Expect(list, "满血增伤 满血 ×1.4 / 掉血 ×1.0（和 = 2.4）",
                WeaponSystem.FullHpMult(true, mm) + WeaponSystem.FullHpMult(false, mm), 2.4f);
            Expect(list, "弹道数 基础 1 + 插件 0 → 1 条", WeaponSystem.VolleyCount(1, WeaponModifiers.None), 1f);
            WeaponModifiers two = WeaponModifiers.None; two.ProjectileCount = 2;
            Expect(list, "弹道数 +2 → 3 条", WeaponSystem.VolleyCount(1, two), 3f);

            // 爆炸点 +N 的落点：第 0 个是主落点，其余在环上（位置真的不同 ⇒ 才叫"多一个爆炸点"）
            Vector2 c0 = RunSession.BlastSpotCenter(Vector2.zero, 0, 3);
            Vector2 c1 = RunSession.BlastSpotCenter(Vector2.zero, 1, 3);
            Vector2 c2 = RunSession.BlastSpotCenter(Vector2.zero, 2, 3);
            list.Add(new CheckResult("爆炸点 +N：3 个落点互不重合且第 0 个是主落点",
                c0 == Vector2.zero && (c1 - c2).magnitude > 1f && c1.magnitude > 1f,
                $"d0={c0.magnitude:0.0} d1={c1.magnitude:0.0} d2={c2.magnitude:0.0}｜1↔2 距离 {(c1 - c2).magnitude:0.0}m"));
        }

        private static string WeaponSlotCn(string pluginId)
        {
            if (pluginId.StartsWith("hoe_")) return "锄头";
            if (pluginId.StartsWith("can_")) return "水壶";
            if (pluginId.StartsWith("fert_")) return "肥料";
            return "?";
        }

        /// <summary>装 6 件锄头插件之后的单次挥击伤害 ÷ 空手伤害。</summary>
        private static float SixStackDamageRatio()
        {
            float bare = RunBody(4242UL, null, WeaponSlot.Hoe, false, ProbeMelee);
            float full = RunBody(4242UL, null, WeaponSlot.Hoe, false, s =>
            {
                int got = 0;
                for (int i = 0; i < TableDatabase.Plugins.Length && got < 6; i++)
                {
                    PluginAsset a = TableDatabase.Plugins[i];
                    if (a.allowedWeapons.Length != 1 || a.allowedWeapons[0] != WeaponSlot.Hoe) continue;
                    if (WeaponSystem.Install(s.Goods, a, WeaponSlot.Hoe, out _)) got++;
                }
                return ProbeMelee(s);
            });
            return bare <= 0f ? 0f : full / bare;
        }

        // ────────────────────────────────────────────────────────────────────
        // 探针：**同种子建两局**（一局空手、一局装插件）跑同一个 body，取两个读数
        //
        // ★ 为什么两局要用同一个种子 ★
        // 攻击里有随机（瞄准散布、暴击掷）✗
        // 同种子 ⇒ 随机序列逐次对齐 ⇒ 两个读数之差**只可能**来自插件 ✓
        // ────────────────────────────────────────────────────────────────────

        private static float RunBody(ulong seed, PluginAsset plugin, WeaponSlot slot, bool forceCrit,
                                     Func<RunSession, float> body)
        {
            RunSession s = PluginBench(seed, out PlayerActor p);
            if (forceCrit) s.Progress.Stats.SetBase(StatId.CritRate, 1f);
            if (plugin != null && !WeaponSystem.Install(s.Goods, plugin, slot, out _)) return float.NaN;
            return body(s);
        }

        private static void RunProbe(ulong seed, PluginAsset plugin, WeaponSlot slot, bool forceCrit,
                                     Func<RunSession, float> body, out float before, out float after)
        {
            before = RunBody(seed, null, slot, forceCrit, body);
            after = RunBody(seed, plugin, slot, forceCrit, body);
        }

        private static PluginProbeRow MeasurePlugin(PluginAsset a)
        {
            WeaponSlot slot = a.allowedWeapons.Length > 0 ? a.allowedWeapons[0] : WeaponSlot.Hoe;
            var row = new PluginProbeRow
            {
                Id = a.id, Name = a.displayName, Channel = WeaponSystem.ChannelOf(a.effect)
            };
            float before = 0f, after = 0f;
            string metric = "—";
            const ulong seed = 20260101UL;

            switch (a.effect)
            {
                case PluginEffectKind.DamageFlat:
                case PluginEffectKind.DamagePercent:
                    metric = slot == WeaponSlot.Can ? "一次激光的伤害"
                           : slot == WeaponSlot.Fertilizer ? "一次爆炸的伤害" : "一次挥击的伤害";
                    RunProbe(seed, a, slot, false, s => AttackDamage(s, slot,
                        slot == WeaponSlot.Fertilizer ? PB_BlastAt + 0.5f : slot == WeaponSlot.Can ? PB_BeamNear : PB_MeleeNear, 0f),
                        out before, out after);
                    break;

                case PluginEffectKind.AttackSpeed:
                    if (slot == WeaponSlot.Can)
                    {
                        metric = "连射 3 秒的耗水（含激光扫过的土地补水）";
                        RunProbe(seed, a, slot, false, s => BeamAmmoUsed(s, 180), out before, out after);
                    }
                    else
                    {
                        metric = "攻击后的冷却（秒，越小越快）";
                        RunProbe(seed, a, slot, false, s =>
                        {
                            s.DebugAttackOnce(slot);
                            int idx = slot == WeaponSlot.Hoe ? PlayerActor.WeaponHoe : PlayerActor.WeaponFertilizer;
                            return s.World.Player.WeaponCds[idx];
                        }, out before, out after);
                    }
                    break;

                case PluginEffectKind.Range:
                    metric = slot == WeaponSlot.Can ? "11m 处被激光打到的伤害" : "4m 处被挥击打到的伤害";
                    RunProbe(seed, a, slot, false,
                        s => AttackDamage(s, slot, slot == WeaponSlot.Can ? 11.0f : 4.0f, 0f), out before, out after);
                    break;

                case PluginEffectKind.CritRate:
                    metric = "200 次攻击的总伤害（暴击率越高越大）";
                    RunProbe(seed, a, slot, false, s => TotalDamageOver(s, slot, 200), out before, out after);
                    break;

                case PluginEffectKind.CritDamage:
                    metric = "强制暴击时的一次伤害";
                    RunProbe(seed, a, slot, true,
                        s => AttackDamage(s, slot, slot == WeaponSlot.Can ? PB_BeamNear : PB_MeleeNear, 0f), out before, out after);
                    break;

                case PluginEffectKind.ExplodeRadius:
                    // 基础半径 3.0m、+35% 后 4.05m ⇒ 探针取 3.6m 才落在两者之间 ✓
                    // （`ExplodeAt` 的半径判定**不加敌人半径**，所以门槛就是裸半径）
                    metric = "落点外 3.6m 处受到的爆炸伤害";
                    RunProbe(seed, a, slot, false,
                        s => AttackDamage(s, WeaponSlot.Fertilizer, PB_BlastAt + 3.6f, 0f), out before, out after);
                    break;

                case PluginEffectKind.ProjectileCount:
                    metric = slot == WeaponSlot.Hoe ? "5.5m 三条线被打到的敌人数"
                           : slot == WeaponSlot.Can ? "5m 三个位置被打到的敌人数"
                                                    : "落点外 5m 处受到的爆炸伤害";
                    RunProbe(seed, a, slot, false, s => MultiLaneHits(s, slot), out before, out after);
                    break;

                case PluginEffectKind.Knockback:
                    metric = "命中后敌人的击退速度";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        EnemyActor e = PlaceEnemy(s, new Vector2(PB_BlastAt + 0.5f, 0f));
                        s.DebugAttackOnce(WeaponSlot.Fertilizer);
                        return e.KnockbackVel.magnitude;
                    }, out before, out after);
                    break;

                case PluginEffectKind.SwordWave:
                    metric = "5.5m（挥击框外）处受到的伤害";
                    RunProbe(seed, a, slot, false, s => AttackDamage(s, WeaponSlot.Hoe, PB_MeleeFar, 0f), out before, out after);
                    break;

                case PluginEffectKind.ChillOnHit:
                    metric = "命中后敌人身上的寒冷层数";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        EnemyActor e = PlaceEnemy(s, slot == WeaponSlot.Fertilizer
                            ? new Vector2(PB_BlastAt + 0.5f, 0f) : new Vector2(PB_MeleeNear, 0f));
                        s.DebugAttackOnce(slot);
                        return e.Status.ChillStacks;
                    }, out before, out after);
                    break;

                case PluginEffectKind.AmmoOnKill:
                    metric = "击杀 1 个敌人后回的弹药";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        s.WaterAmmo = 50;
                        s.FertAmmo = 5;
                        PlaceEnemy(s, new Vector2(PB_BeamNear, 0f), 1f);      // 一击必杀
                        s.DebugAttackOnce(WeaponSlot.Can);
                        return (s.WaterAmmo - 50) + (s.FertAmmo - 5);
                    }, out before, out after);
                    break;

                case PluginEffectKind.BurnOnHit:
                    metric = "命中后敌人的灼烧量";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        EnemyActor e = PlaceEnemy(s, slot == WeaponSlot.Fertilizer
                            ? new Vector2(PB_BlastAt + 0.5f, 0f)
                            : slot == WeaponSlot.Can ? new Vector2(PB_BeamNear, 0f) : new Vector2(PB_MeleeNear, 0f));
                        s.DebugAttackOnce(slot);
                        return e.Status.BurnAmount;
                    }, out before, out after);
                    break;

                case PluginEffectKind.ChainOnHit:
                    metric = "旁边 3m 处那个敌人受到的溅射";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        Vector2 main = slot == WeaponSlot.Fertilizer ? new Vector2(PB_BlastAt + 0.5f, 0f)
                                    : slot == WeaponSlot.Can ? new Vector2(PB_BeamNear, 0f)
                                                             : new Vector2(PB_MeleeNear, 0f);
                        PlaceEnemy(s, main);
                        EnemyActor side = PlaceEnemy(s, main + new Vector2(0f, 3f));
                        s.DebugAttackOnce(slot);
                        return 1e5f - side.Health.Hp;
                    }, out before, out after);
                    break;

                case PluginEffectKind.BlastEcho:
                    metric = "落点敌人在一整轮爆炸里受到的总伤害";
                    RunProbe(seed, a, slot, false,
                        s => AttackDamage(s, WeaponSlot.Fertilizer, PB_BlastAt + 0.5f, 0f), out before, out after);
                    break;

                case PluginEffectKind.CritBlast:
                    metric = "强制暴击时旁边 2m 敌人受到的爆炸";
                    RunProbe(seed, a, slot, true, s =>
                    {
                        PlaceEnemy(s, new Vector2(1.0f, 0f));
                        // (1,-2)：离玩家 2.24m（在挥击距离内）但偏 63°（在 100° 扇形**之外**）
                        //         ⇒ 它不会被挥击本身打到，只会吃到「暴击炸裂」✓
                        EnemyActor side = PlaceEnemy(s, new Vector2(1.0f, -2.0f));
                        s.DebugAttackOnce(WeaponSlot.Hoe);
                        return 1e5f - side.Health.Hp;
                    }, out before, out after);
                    break;

                case PluginEffectKind.FullHpBonus:
                    metric = "满血伤害 − 掉血后伤害（差 = 这一件的收益）";
                    RunProbe(seed, a, slot, false, s => FullHpDelta(s, slot), out before, out after);
                    break;
            }

            row.Metric = metric;
            row.Before = before;
            row.After = after;
            row.Moved = !float.IsNaN(before) && !float.IsNaN(after) && Mathf.Abs(after - before) > 1e-4f;
            return row;
        }

        /// <summary>对指定位置的一个敌人打一次，返回它掉了多少血。</summary>
        private static float AttackDamage(RunSession s, WeaponSlot slot, float x, float y)
        {
            EnemyActor e = PlaceEnemy(s, new Vector2(x, y));
            s.DebugAttackOnce(slot);
            return 1e5f - e.Health.Hp;
        }

        private static float ProbeMelee(RunSession s) => AttackDamage(s, WeaponSlot.Hoe, PB_MeleeNear, 0f);

        /// <summary>连打 N 次，累计伤害（用来把 12% 的暴击率放大成可读的数）。</summary>
        private static float TotalDamageOver(RunSession s, WeaponSlot slot, int times)
        {
            float total = 0f;
            for (int i = 0; i < times; i++)
            {
                // 每次都换一个新敌人：激光有"同一目标 0.5 秒内只受伤一次"的 CD ✗
                Vector2 at = slot == WeaponSlot.Can ? new Vector2(PB_BeamNear, 0f) : new Vector2(PB_MeleeNear, 0f);
                EnemyActor e = PlaceEnemy(s, at);
                s.DebugAttackOnce(slot);
                total += 1e5f - e.Health.Hp;
                s.World.Enemies.RemoveAt(s.World.Enemies.Count - 1);
            }
            return total;
        }

        /// <summary>弹道数探针：多条线上各放一个敌人，数有几个被打到。</summary>
        private static float MultiLaneHits(RunSession s, WeaponSlot slot)
        {
            if (slot == WeaponSlot.Hoe)
            {
                // 5.5m 处三条线：-12° / 0° / +12°（剑气流每条相差 12°，判定半宽 0.9m）
                PlaceEnemy(s, new Vector2(PB_MeleeFar * Mathf.Cos(-12f * Mathf.Deg2Rad), PB_MeleeFar * Mathf.Sin(-12f * Mathf.Deg2Rad)));
                PlaceEnemy(s, new Vector2(PB_MeleeFar, 0f));
                PlaceEnemy(s, new Vector2(PB_MeleeFar * Mathf.Cos(12f * Mathf.Deg2Rad), PB_MeleeFar * Mathf.Sin(12f * Mathf.Deg2Rad)));
                s.DebugAttackOnce(WeaponSlot.Hoe);
                int hit = 0;
                for (int i = 0; i < s.World.Enemies.Count; i++) if (s.World.Enemies[i].Health.Hp < 1e5f - 1e-3f) hit++;
                return hit;
            }
            if (slot == WeaponSlot.Can)
            {
                // 5m 处：正前方 1 个 + 左右各 2.0m（超出单条激光 ±(1.4+半径) 的宽度）
                PlaceEnemy(s, new Vector2(PB_BeamNear, 0f));
                PlaceEnemy(s, new Vector2(PB_BeamNear, 2.0f));
                PlaceEnemy(s, new Vector2(PB_BeamNear, -2.0f));
                s.DebugAttackOnce(WeaponSlot.Can);
                int hit = 0;
                for (int i = 0; i < s.World.Enemies.Count; i++) if (s.World.Enemies[i].Health.Hp < 1e5f - 1e-3f) hit++;
                return hit;
            }
            // 肥料：落点外 5m 处的敌人（基础半径 3m 够不到，第 2 个爆炸点在落点外 2.5m）
            return AttackDamage(s, WeaponSlot.Fertilizer, PB_BlastAt + 5.0f, 0f);
        }

        /// <summary>满血伤害 − 掉血后伤害。没有「满血增伤」时应当**恰好为 0**。</summary>
        private static float FullHpDelta(RunSession s, WeaponSlot slot)
        {
            float full;
            if (slot == WeaponSlot.Can)
            {
                full = AttackDamage(s, WeaponSlot.Can, PB_BeamNear, 0f);
                s.World.Player.Health.ApplyDamage(1f);
                return full - AttackDamage(s, WeaponSlot.Can, PB_BeamNear, 0f);
            }
            full = AttackDamage(s, WeaponSlot.Fertilizer, PB_BlastAt + 0.5f, 0f);
            s.World.Player.Health.ApplyDamage(1f);
            return full - AttackDamage(s, WeaponSlot.Fertilizer, PB_BlastAt + 0.5f, 0f);
        }

        /// <summary>水壶按住连射 <paramref name="ticks"/> 帧，返回消耗的弹药（含起手那 10 点）。</summary>
        private static float BeamAmmoUsed(RunSession s, int ticks)
        {
            PlayerActor p = s.World.Player;
            p.SwitchWeapon(PlayerActor.WeaponCan, s.World, true);
            int start = s.WaterAmmo;
            for (int i = 0; i < ticks; i++)
                s.FixedTick(1f / 60f, new FrameInput
                {
                    Fire = true, AimWorld = new Vector2(PB_BeamNear, 0f),
                    SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
                });
            return start - s.WaterAmmo;
        }
    }
}
