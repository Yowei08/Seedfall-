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
            // ★ v252：先把难度钉回「低」★
            //  难度现在是一个**存在 PlayerPrefs 里**的全局乘数（`RunDifficulty`），而本文件有一批
            //  硬期望值（第 1 分钟 0.455 / 第 20 分钟 60 血 / 开局一刀死 …）全部按 **×1.00** 写的。
            //  若玩家上一次在主城选了「中/高」，这些断言会因为一个与自检无关的设置整片变红 ✗
            //  ⇒ 自检是**只读验收**，它不该受玩家设置影响：进来先归零（不写盘以外的任何东西，
            //     `ResetForTests` 也只写一个 PlayerPrefs 整数）✓
            RunDifficulty.ResetForTests();

            FormulaChecks(list);
            LevelChecks(list);
            StatChecks(list);
            FarmingChecks(list);
            TableChecks(list);
            RngChecks(list);
            RuleChecks(list);
            PluginChecks(list);
            // 阶段 3：**持续效果表**（灼烧 / 减速 / 毒区）单独一节 —— 它是一张独立的表，
            //          不依赖任何插件接线就能被断言（先证明表算得对，再证明插件接得上 ✓）
            DotTableChecks(list);
            // v168：新标准下的植物 / 道具 / 合成等级验收（每条机制都跑一次对照实验）
            V168Checks(list);
            // v191：幸运值 / 暴伤 150% / 小猪刷新率 / 货币与经验倍率进收割 / 面板与卡池
            V191Checks(list);
            // v179：数据表护栏（植物档内公平 / 四象限 / 机制清零 / 道具覆盖 / 道具无规则 / id 稳定）
            //       + 插件两条（属性类清零 `PLUGIN-NO-STAT` / 插槽与叠层 `PLUGIN-SLOT`）
            TableGuardChecks(list);
            // v205：锄头「扇形判定 / 挥击弧特效」的几何契约（视觉在两个方向都进到判定里面）
            HoeReachChecks(list);
            // v211：锄头「挥击判定窗口」—— 延长存在时间，但同一敌人同一窗口只掉一次血
            MeleeWindowChecks(list);
            // v209：**夜间敌人发光**（需求原话："为夜晚的敌人增加发光"）
            NightGlowChecks(list);
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

        /// <summary>
        /// ★ v209：**夜间敌人发光**（需求原话："为夜晚的敌人增加发光"）★
        ///
        /// ── 这一节验什么、不验什么 ──
        /// 验的是**曲线**：它是 `DayNightCycle` 里的纯函数，编辑模式就能跑，
        /// 而且"只在夜里发光 / 白天不发光 / 平滑淡入"这三句话的全部可核对内容都在它里面 ✓
        /// 不验观感（"敌人看起来比玩家亮"）—— 那要靠抓图，断言不替截图签字 ✓
        ///
        /// ── 为什么每一条都必要 ──
        ///   ① 白天**严格 0**（不是"很小"）—— 用户原话"白天不发光"；
        ///      写成 `0.001` 这种"看起来没关系"的值，白天敌人身上就会有一层看不见但确实存在的红光 ✗
        ///   ② 子夜**严格 1** —— 满强度
        ///   ③④ 过渡段在开区间、且黄昏与黎明对称 ⇒ 证明"渐变"真的存在、且一天只有一个天黑判据
        ///   ⑤ 两个关键帧端点（0.45 / 0.18）—— 那两个数就是 `Keys` 的骨架，端点错一个就是整体偏移
        ///   ⑥ 闭环 —— 整轮回到起点，跨夜不会累积漂移
        ///   ⑦ 连续性 —— 1200 步无跳变，否则玩家会看到"咔"地亮起来
        ///   ⑧ 单一真相 —— `Sample(t).Glow` 与 `Glow01(t)` 逐位相同，不给第二份算法留口子
        ///   ⑨ 强度 —— 夜间倍率落在建议区间 1.5~2.5，且白天是恒等 ✓
        /// </summary>
        private static void NightGlowChecks(List<CheckResult> list)
        {
            // ① 白天严格 0
            float d0 = DayNightCycle.Glow01(0f);
            float d60 = DayNightCycle.Glow01(60f);
            float d240 = DayNightCycle.Glow01(240f);
            list.Add(new CheckResult("v209①：白天发光严格 = 0（t=0 / 60 / 240 三个时刻）",
                d0 == 0f && d60 == 0f && d240 == 0f,
                $"t=0 {d0}｜t=60 {d60}｜t=240 {d240}（期望三个都恰好是 0，不是「很小」）"));

            // ② 子夜满亮
            Expect(list, "v209②：子夜 t=150 发光满强度 = 1", DayNightCycle.Glow01(150f), 1f);

            // ③ 过渡段在开区间（渐变，不是开关）
            float dusk = DayNightCycle.Glow01(90f);
            list.Add(new CheckResult("v209③：淡入段 t=90 发光落在 0 与 1 之间（渐变，不是一刀切）",
                dusk > 0.0001f && dusk < 0.9999f, $"t=90 发光 {dusk:0.0000}"));

            // ④ 黄昏 / 黎明对称
            float dawn = DayNightCycle.Glow01(210f);
            list.Add(new CheckResult("v209④：t=90 与 t=210 发光相同（黄昏/黎明对称，偏差 < 0.002）",
                Mathf.Abs(dusk - dawn) < 0.002f, $"t=90 {dusk:0.0000}｜t=210 {dawn:0.0000}"));

            // ⑤ 两个关键帧端点
            Expect(list, "v209⑤：h01=0.45（黄金时刻）发光 = 0",
                DayNightCycle.GlowFromH01(DayNightCycle.GlowDayEndH01), 0f);
            Expect(list, "v209⑤：h01=0.18（暮色）发光 = 1",
                DayNightCycle.GlowFromH01(DayNightCycle.GlowNightFullH01), 1f);

            // ⑥ 闭环
            float back = DayNightCycle.Glow01(DayNightCycle.CycleSeconds);
            list.Add(new CheckResult("v209⑥：t=0 与 t=300（下一轮起点）发光逐位相同（跨夜不漂移）",
                d0 == back, $"t=0 {d0}｜t=300 {back}"));

            // ⑦ 连续性：整轮 1200 步，每步最大变化
            int steps = Mathf.RoundToInt(DayNightCycle.CycleSeconds / 0.25f);
            float prev = DayNightCycle.Glow01(0f);
            float maxStep = 0f;
            for (int i = 1; i <= steps; i++)
            {
                float g = DayNightCycle.Glow01(i * 0.25f);
                maxStep = Mathf.Max(maxStep, Mathf.Abs(g - prev));
                prev = g;
            }
            list.Add(new CheckResult("v209⑦：整轮 1200 步，发光每步变化 < 0.02（没有跳变）",
                maxStep < 0.02f, $"每步最大变化 {maxStep:0.000000}／0.25 秒"));

            // ⑧ 单一真相：State.Glow 与 Glow01 在六个采样时刻一致
            bool sameSrc = true;
            for (int i = 0; i < DayNightCycle.SampleTimes.Length; i++)
            {
                float t = DayNightCycle.SampleTimes[i];
                if (DayNightCycle.Sample(t).Glow != DayNightCycle.Glow01(t)) sameSrc = false;
            }
            list.Add(new CheckResult("v209⑧：Sample(t).Glow 与 Glow01(t) 在六个采样时刻逐位相同（只有一个真相）",
                sameSrc, $"六个采样时刻{(sameSrc ? "全部一致" : "**有分歧**")}"));

            // ⑨ 强度：夜间倍率在建议区间内，白天恒等
            float dayMul = ToonLook.NightNeonMultiplier(1.60f, 0f);   // 敌人档（WorldView.NeonBoostChar）
            float nightMul = ToonLook.NightNeonMultiplier(1.60f, 1f);
            list.Add(new CheckResult("v209⑨：夜间敌人霓虹 = 2.24 倍底色（建议区间 1.5~2.5），白天恒等 1.60",
                nightMul > 1.5f && nightMul < 2.5f && Mathf.Abs(dayMul - 1.60f) < 1e-5f,
                $"白天 ×{dayMul:0.000}（应 1.600）｜深夜 ×{nightMul:0.000}（应 2.240）"));
        }

        private static void Expect(List<CheckResult> list, string name, float actual, float expected)
        {
            bool ok = Mathf.Abs(actual - expected) <= Eps;
            list.Add(new CheckResult(name, ok, $"期望 {expected} 实际 {actual:0.####}"));
        }

        /// <summary>
        /// ★ v191 验收（用户五项需求，每条至少一个判据）★
        ///
        /// ① 幸运值：`StatId` 20 / 分数属性 / 基底 5% / 收割概率 ×2 / 弹字带 `X2` / **不消费 World.Rng**
        /// ② 初始暴击伤害 200% → **150%**（判据是"同一刀暴击 ÷ 不暴击 = 1.5"）
        /// ③ 小猪刷新率：4 级 2/4/6/8% + 名字 低/中/高/MAX + 等级硬上限 3
        /// ④ 货币/经验倍率**真的**进收割结算，且**弹字 = 到账**（同一个变量，不是两次计算）
        /// ⑤ 面板两行 + 抽卡池 12 → 13 张
        ///
        /// ★ 为什么用"成对夹具"而不是只测一次 ★
        ///   下面每一条都比"关了会是怎样"与"开了会是怎样"两个世界（同种子、同作物、同等级）⇒
        ///   差额只可能来自被验的那一条乘区 ✓ 单边断言（"到账是 40"）证明不了 40 是 ×2 来的 ——
        ///   它也可能是基数本来就变了 ✓
        /// </summary>
        private static void V191Checks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            PlayerBaseDef b = t.player;

            // ═══════════════════════ ① 幸运值 ═══════════════════════
            // ★ v195：编号 20/21 → **19/20**（`ExplodeDamage` 删除后整表前移一位）★
            //   判据的形式没变：**纯追加的两个成员在末尾，且 Count 与它们对得上** ✓
            list.Add(new CheckResult("v195①：StatId.Luck = 19、PigRate = 20（删 ExplodeDamage 后重排，仍在末尾）",
                (int)StatId.Luck == 19 && (int)StatId.PigRate == 20 && StatIds.Count == 21,
                $"Luck={(int)StatId.Luck}｜PigRate={(int)StatId.PigRate}｜StatIds.Count={StatIds.Count}"));

            // ★ 这条是"卡片能不能表达 +3%"的根：分数属性走 `/100` 的 **Add**，不走 Percent 乘法 ★
            list.Add(new CheckResult("v191①：Luck 与 CritRate 同为分数属性（IsFractionStat）⇒ 卡面 Add 3 = +3 个百分点",
                TableDatabase.IsFractionStat(StatId.Luck)
                && TableDatabase.IsFractionStat(StatId.CritRate)
                && !TableDatabase.IsFractionStat(StatId.CritDmg)
                && !TableDatabase.IsFractionStat(StatId.PigRate),
                $"Luck={TableDatabase.IsFractionStat(StatId.Luck)}｜CritRate={TableDatabase.IsFractionStat(StatId.CritRate)}"
                + $"｜CritDmg={TableDatabase.IsFractionStat(StatId.CritDmg)}（应 False）"
                + $"｜PigRate={TableDatabase.IsFractionStat(StatId.PigRate)}（应 False）"));

            Expect(list, "v191①：幸运值基底 5%（必须 > 0，否则 Percent 卡恒为死卡）", b.luck, 0.05f);

            var st0 = new StatBlock(t.caps, t.player);
            string luckOpening = ImGuiHud.AddPoints(st0.Get(StatId.Luck), b.luck);
            list.Add(new CheckResult("v191⑤：幸运值面板开局显示增量 +0%（印的是百分点差，与 CritRate 同口径）",
                luckOpening == "+0%", $"面板「幸运值 {st0.Get(StatId.Luck) * 100f:0.#}%（{luckOpening}）」"));

            // 抽到一张「幸运值 +3%」——写入方式与 `PickRewardCard` 的 IsFractionStat 分支逐字相同
            var sc = new RunSession();
            sc.Start(919191UL);
            StatBlock stG = sc.Progress.Stats;
            float luckBefore = stG.Get(StatId.Luck);
            stG.AddModifier(new StatModifier(StatId.Luck, 3f / 100f, ModOp.Add, "check:v191"));
            list.Add(new CheckResult("v191①⑤：抽一张「幸运值 +3%」⇒ 5% → 8%，面板印 +3%",
                Mathf.Abs(luckBefore - 0.05f) < 1e-4f
                && Mathf.Abs(stG.Get(StatId.Luck) - 0.08f) < 1e-4f
                && ImGuiHud.AddPoints(stG.Get(StatId.Luck), b.luck) == "+3%",
                $"{luckBefore * 100f:0.#}% → {stG.Get(StatId.Luck) * 100f:0.#}%"
                + $"｜面板 {ImGuiHud.AddPoints(stG.Get(StatId.Luck), b.luck)}"));
            sc.Dispose();

            // ── 收割夹具：一局新会话 + 一格成熟萝卜 ⇒ 到账金币/经验 + 两条弹字原文 ──
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            // ★ v195：**到账金币还要过 `farm.harvestGoldScale`**（本轮新增的金币全局系数 0.54）★
            //   判据仍然是"幸运触发 ⇒ ×2"，只是基数从"表里的 goldYield"换成"实际到账的单格金币" ✓
            //   不这么写的话，本轮的"植物金币收获量调整"会让三条与本规则无关的断言变红 ✗
            float gScaleV191 = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
            int cellGold = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(radish) * gScaleV191));
            int cellExp = SeedEconomy.CellExp(radish);

            void HarvestOnce(float luck, float goldMult, float expMult,
                             out int gold, out float exp, out string goldText, out string expText)
            {
                var hs = new RunSession();
                hs.Start(31415UL);          // 三个夹具**同种子** ⇒ 除被验的那一档外一切相同 ✓
                hs.StartWave(1);
                StatBlock stx = hs.Progress.Stats;
                stx.SetBase(StatId.Luck, luck);
                stx.SetBase(StatId.GoldMult, goldMult);
                stx.SetBase(StatId.ExpMult, expMult);
                FarmPlot p2 = hs.World.Farm.Slot(0, 0);
                p2.BeginPlant(t.farm, radish, t.farm.waterMaxDefault, radish.WaterDrain);
                p2.TickGrowth(999f);
                int g0 = hs.World.GoldEarned;
                float e0 = hs.World.ExpEarned;
                hs.Popups.Clear();
                hs.World.HarvestPlot(p2);
                gold = hs.World.GoldEarned - g0;
                exp = hs.World.ExpEarned - e0;
                goldText = null; expText = null;
                for (int i = 0; i < hs.Popups.Count; i++)
                {
                    string s = hs.Popups[i].Text;
                    if (s != null && s.StartsWith("金币")) goldText = s;
                    if (s != null && s.StartsWith("经验")) expText = s;
                }
                hs.Dispose();
            }

            // 不触发（幸运 0）与必触发（幸运 100%）的一对
            HarvestOnce(0f, 1f, 1f, out int gNo, out float eNo, out string gNoText, out string eNoText);
            HarvestOnce(1f, 1f, 1f, out int gYes, out float eYes, out string gYesText, out string eYesText);

            list.Add(new CheckResult("v191①：幸运触发 ⇒ 到账金币与经验**都** ×2（同种子、同作物的成对夹具）",
                gNo == cellGold && gYes == cellGold * 2
                && Mathf.Abs(eNo - cellExp) < 1e-3f && Mathf.Abs(eYes - cellExp * 2) < 1e-3f,
                $"金币 {gNo} → {gYes}（应 {cellGold} → {cellGold * 2}）"
                + $"｜经验 {eNo:0.#} → {eYes:0.#}（应 {cellExp} → {cellExp * 2}）"));

            list.Add(new CheckResult("v191①：触发时**弹字后加 X2**，且弹字数字 = 到账数字（未触发时不带 X2）",
                gYesText == "金币+" + gYes + " X2" && eYesText == "经验值+" + Mathf.RoundToInt(eYes) + " X2"
                && gNoText == "金币+" + gNo && eNoText == "经验值+" + Mathf.RoundToInt(eNo)
                && gNoText.IndexOf("X2", StringComparison.Ordinal) < 0
                && eNoText.IndexOf("X2", StringComparison.Ordinal) < 0,
                $"触发：{gYesText} / {eYesText}｜未触发：{gNoText} / {eNoText}"));

            // ★ 触发率：5% 的幸运在 1000 次掷签里应该落在 ~50 次（判据给一条宽松但能证伪的带）★
            //   ★ 为什么用 `RollHarvestLuck` 直接掷、而不是真的收 1000 次地 ★
            //     收割一次要"成熟 → 收割 → 重新生长"，1000 次要在自检里跑上千帧 ✗
            //     而 ×2 那半句已由上面的成对夹具逐字验过 ⇒
            //     这里只需要回答"概率参数**真的**是 5%"这一个问题 ✓
            //     （`HarvestPlot` 里唯一决定触发的地方就是这一句，见 Actors.cs 的调用点）
            {
                World wl = MakeWorld(out PlayerActor pl);
                pl.Progress.Stats.SetBase(StatId.Luck, 0.05f);
                FarmPlot probePlot = wl.Farm.Slot(0, 0);
                int hits = 0;
                for (int i = 0; i < 1000; i++) if (wl.RollHarvestLuck(probePlot)) hits++;
                list.Add(new CheckResult("v191①：1000 次收割判定里，5% 幸运的触发次数落在 30~75（≈50）",
                    hits >= 30 && hits <= 75, $"触发 {hits}/1000 = {hits / 10f:0.0}%（期望 5%）"));
            }

            // ════════════════════════════════════════════════════════════════════
            // ★★ v202：**爆炸收割之后，下一次手挥必须仍是全额** ★★
            //
            // 用户原话（第 2 次复发）："在经历过爆炸后，收割获得量永久变为了1/4"
            //
            // ── 为什么 v200 已经"证明过"却还是复发 ✗ ──
            //   ① v200 的证据是**探针**（`ProbeV200` 的 A5/A6/A7/A9）——
            //      探针只在有人手动跑的时候才跑；而 v192/v195 的 `World.HarvestYieldScale`
            //      之所以能藏那么久，正是因为**自检里一条断言都没有钉它** ✓
            //   ② 探针走的是 `World.HarvestPlot` / `World.MatureAndHarvestInRadius` ——
            //      玩家的手挥走 `RunSession.HarvestPlot`（多一步 `ProgMechanics.OnHarvest`），
            //      爆炸走 `RunSession.ExplodeAt`（先算插件 / 富豪 / 子爆 / 毒区，再进催熟+收割）✓
            //      ⇒ 两个"玩家真路径"此前**没有任何断言覆盖** ✓
            //   ⇒ 本条把两件事一起补上：读数全部走真路径，并在爆炸前后**逐键比**
            //      "参与结算的标量"（21 条属性的生效值+基底、世界级系数、两个表值、等级）。
            //
            // ── 判据（三条一起，缺一条就红）──
            //   ① 手挥 == 全额（到账 == round(单格基数 × 金币系数)，`cellGold` 就是那个数）
            //   ② 炸收 ≈ 手挥 × `farm.blastHarvestRatio`（±1 是取整误差）⇒ 1/4 **只**归爆炸
            //   ③ 炸后手挥 == 手挥，**且**结算标量的指纹一条都没变 ⇒ 没有可泄漏的系数 ✓
            // ════════════════════════════════════════════════════════════════════
            {
                // 参与结算的标量的**指纹**（唯一实现：本条与探针都读它）
                string SettleFingerprint(RunSession ss)
                {
                    var fp = new System.Text.StringBuilder();
                    StatBlock fx = ss.Progress.Stats;
                    for (int i = 0; i < StatIds.Count; i++)
                    {
                        StatId id = (StatId)i;
                        fp.Append(fx.Get(id).ToString("R")).Append('/')
                          .Append(fx.GetBase(id).ToString("R")).Append(';');
                    }
                    fp.Append(ss.World.HarvestGoldMult.ToString("R")).Append(';')
                      .Append(ss.World.HarvestDew).Append(';')
                      .Append(t.farm.blastHarvestRatio.ToString("R")).Append(';')
                      .Append(t.farm.harvestGoldScale.ToString("R")).Append(';')
                      .Append(ss.Progress.Level);
                    return fp.ToString();
                }

                var es2 = new RunSession();
                es2.Start(202202UL);
                es2.Progress.Stats.SetBase(StatId.Luck, 0f);   // 幸运 0 ⇒ 读数确定（否则 5% ×2 像抛硬币）
                // ★★ v209：**先把夹具的等级抬到"这 18 点经验绝对升不了一级"的地方** ★★
                //
                // 为什么必须做：本段三收合计 **8（手挥）+ 2（炸收 1/4）+ 8（手挥）= 18 经验** ✓
                //   `expBase 20 → 12`（v209 用户原话"降低前期升级需要经验值"）之后 Lv1 的门槛是 **12**
                //   ⇒ 18 ≥ 12 ⇒ **第 3 次手挥那一下会升到 2 级** ⇒ `SettleFingerprint` 里的「等级」1 → 2，
                //      且 `Stats.AddBase(StatId.MaxHp, growth.hpPerLevel)` 让 MaxHp 的**基底 +1**
                //   ⇒ 指纹"变了" ⇒ 判据 ③ **红** ✗（当日实测日志：
                //      `炸前手挥 金 17/验 8 → 炸后手挥 金 17/验 8`，**收益两栏一模一样** ✓）
                //   ⇒ 红的不是"系数泄漏"，是**夹具原来隐含假设"这三次收割不会升级"**，而 expBase 把它打破了 ✓
                //      「判据红 ≠ 行为错」—— 红在 **等级/基底** 这一层，不在收益那一层 ✓
                // 修法：先升到"再升一级要 > 60 经验"为止（本曲线 = Lv5、门槛 83；旧曲线 = Lv4、门槛 68）
                //   ⇒ 窗口内的 18 点经验**跨不了级** ⇒ 指纹只回答"有没有结算系数被炸坏"这一件事 ✓
                // ⚠ 判据 / 断言条数**一条没动**，只把夹具的起始状态写明确 ✓
                for (int guard = 0; guard < 64 && es2.Progress.ExpToNext <= 60f; guard++)
                    es2.Progress.AddExp(es2.Progress.ExpToNext, out _);

                // ★ 用**大数字**的种子，否则 1/4 会被 `Mathf.Max(1, …)` 的保底吃掉：
                //   萝卜 1 级是 金 4 → 金 2 到账，1/4 = 0.5 ⇒ 保底成 1，比值看起来像 1/2 ✗
                //   ⇒ 直接从表里挑**金币最高**的那株（不写死 id：写死 id 会在表改名时静默退化成萝卜）
                SeedDef bigCrop = null;
                for (int i = 0; i < TableDatabase.Seeds.Length; i++)
                {
                    SeedDef sd = TableDatabase.Seeds[i];
                    if (sd == null || sd.GrowTime <= 0f) continue;
                    if (bigCrop == null || sd.GoldYield > bigCrop.GoldYield) bigCrop = sd;
                }
                if (bigCrop == null) bigCrop = radish;
                int bigGold = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(bigCrop) * gScaleV191));
                int bigExp = SeedEconomy.CellExp(bigCrop);

                // 三格**非列首**的地（列首 `BeginPlant` 会整列补齐，读数就不是"一格"了）
                FarmPlot e0p = es2.World.Farm.Slot(1, 1);
                FarmPlot e1p = es2.World.Farm.Slot(2, 1);
                FarmPlot e2p = es2.World.Farm.Slot(3, 1);

                string fpHand0;
                e0p.BeginPlant(t.farm, bigCrop, t.farm.waterMaxDefault, bigCrop.WaterDrain);
                e0p.TickGrowth(999f);
                fpHand0 = SettleFingerprint(es2);
                int ge0 = es2.World.GoldEarned; float xe0 = es2.World.ExpEarned;
                es2.HarvestPlot(e0p);                      // ★ 玩家真路径：手挥（锄头）
                int gHand0 = es2.World.GoldEarned - ge0;
                float xHand0 = es2.World.ExpEarned - xe0;

                // ★ 清掉刚收过的那一格 ★
                //   `MatureAndHarvestInRadius` 的语义是"半径内**生长中/已成熟**的地全部催熟+收割" ⇒
                //   刚收完仍在"生长中"的 e0p 会被第二次催熟并再收一遍（正确行为，但读数会变成两格）
                //   ⇒ 先清空，让爆炸半径里**只有一格**是活的 ✓
                e0p.ResetToEmpty();

                e1p.BeginPlant(t.farm, bigCrop, t.farm.waterMaxDefault, bigCrop.WaterDrain);
                e1p.TickGrowth(999f);
                int gb0 = es2.World.GoldEarned; float xb0 = es2.World.ExpEarned;
                es2.ExplodeAt(e1p.World, es2.World.Player); // ★ 玩家真路径：肥料爆炸
                int gBlast0 = es2.World.GoldEarned - gb0;
                float xBlast0 = es2.World.ExpEarned - xb0;

                e2p.BeginPlant(t.farm, bigCrop, t.farm.waterMaxDefault, bigCrop.WaterDrain);
                e2p.TickGrowth(999f);
                int ga0 = es2.World.GoldEarned; float xa0 = es2.World.ExpEarned;
                es2.HarvestPlot(e2p);                      // ★ 玩家真路径：爆完再手挥
                int gHand1 = es2.World.GoldEarned - ga0;
                float xHand1 = es2.World.ExpEarned - xa0;
                string fpHand1 = SettleFingerprint(es2);
                int levelNow = es2.Progress.Level;
                es2.Dispose();

                // ★ `SelfCheck.Report()` **只输出失败项** ⇒ 通过项的 Detail 没有出口 ✗
                //   所以三条 v202 的读数必须自己打到日志里，否则"PASS"是不可复核的 ✓
                //   （与上面 v167 雨滴那条同一个做法，只补出口、不动 `Report()`）
                Log.Info($"[v202 收割归属] 种子 {bigCrop.Id}（单格基数 金 {SeedEconomy.CellGold(bigCrop)} × 系数 {gScaleV191:0.##}"
                         + $" ⇒ 手挥应 {bigGold}）｜手挥 金 {gHand0}/验 {xHand0:0.##}"
                         + $" → 炸收 金 {gBlast0}/验 {xBlast0:0.##}"
                         + $" → 炸后手挥 金 {gHand1}/验 {xHand1:0.##}"
                         + $"｜炸收/手挥 = 金 {(gHand0 > 0 ? (float)gBlast0 / gHand0 : 0f):0.###}"
                         + $"/验 {(xHand0 > 0f ? xBlast0 / xHand0 : 0f):0.###}"
                         + $"（表值 {t.farm.blastHarvestRatio:0.###}）"
                         + $"｜结算标量指纹 {(string.Equals(fpHand0, fpHand1, StringComparison.Ordinal) ? "逐位相同" : "变了")}"
                         + $"｜等级 {levelNow}");

                list.Add(new CheckResult("v202①：手挥 = 全额（到账 == round(单格基数 × 金币系数)）",
                    gHand0 == bigGold && Mathf.Abs(xHand0 - bigExp) < 1e-3f,
                    $"种子 {bigCrop.Id}｜金币 {gHand0}（应 {bigGold}）｜经验 {xHand0:0.##}（应 {bigExp}）"));

                int blastExpect = Mathf.Max(1, Mathf.RoundToInt(bigGold * t.farm.blastHarvestRatio));
                int expBlastExpect = Mathf.Max(1, Mathf.RoundToInt(bigExp * t.farm.blastHarvestRatio));
                list.Add(new CheckResult("v202②：炸收 ≈ 手挥 × farm.blastHarvestRatio（1/4 只归爆炸）",
                    Mathf.Abs(gBlast0 - blastExpect) <= 1 && Mathf.Abs(xBlast0 - expBlastExpect) <= 1f,
                    $"炸收 金币 {gBlast0}（应 ≈{blastExpect}）｜经验 {xBlast0:0.##}（应 ≈{expBlastExpect}）"
                    + $"｜比值 金 {(gHand0 > 0 ? (float)gBlast0 / gHand0 : 0f):0.###}"
                    + "、验 " + (xHand0 > 0f ? xBlast0 / xHand0 : 0f).ToString("0.###")));

                bool noLeak = gHand1 == gHand0 && Mathf.Abs(xHand1 - xHand0) < 1e-3f
                              && string.Equals(fpHand0, fpHand1, StringComparison.Ordinal);
                list.Add(new CheckResult("v202③：**爆炸收割之后，下一次手挥仍是全额**（结算标量指纹逐位不变）",
                    noLeak,
                    $"炸前手挥 金 {gHand0}/验 {xHand0:0.##} → 炸后手挥 金 {gHand1}/验 {xHand1:0.##}"
                    + $"｜结算标量指纹 {(string.Equals(fpHand0, fpHand1, StringComparison.Ordinal) ? "逐位相同" : "**变了**")}"
                    + $"｜等级 {levelNow}"));
            }

            // ════════════════════════════════════════════════════════════════════
            // ★★ v204：**`v202④` 两条断言已删除**（用户原话："武器自动切换到锄头的 bug"）★★
            //
            // 它们钉的是 `TickPendingBlasts → ReturnToHoeAfterBlastHarvest` 那条**自动切武器**
            // 的行为（有熟地 ⇒ 切回锄头 / 空炸 ⇒ 仍是肥料）。用户把那条行为本身定性成 bug ⇒
            // 行为撤掉，断言不能留（留下来会永远红，而且它钉的东西已经不存在了）✗
            // ★ 保留的是 `v202①/②/③` ★ —— 那三条钉的是**收益归属**（手挥全额 / 炸收 1/4 /
            //   炸后手挥仍是全额），与"武器要不要自动切"完全正交，一条都没动 ✓
            // ════════════════════════════════════════════════════════════════════

            // ★★ World.Rng 一个字节都没动 ★★
            //   做法：同种子造两个世界，A 先做 18 次收割（幸运 50%，即真的走哈希分支），
            //   B 什么都不做 ⇒ 两边读到的**下一个**随机数必须相同 ✓
            //   若幸运偷用了 `World.Rng`，A 的流已经走过 18 步 ⇒ 必然不同 ✓
            {
                World wa = MakeWorld(out PlayerActor pa);
                pa.Progress.Stats.SetBase(StatId.Luck, 0.5f);
                int harvested = 0;
                for (int c = 0; c < wa.Farm.Columns; c++)
                {
                    FarmPlot pp = wa.Farm.Slot(c, 0);
                    if (pp == null) continue;
                    pp.BeginPlant(t.farm, radish, t.farm.waterMaxDefault, radish.WaterDrain);
                    pp.TickGrowth(999f);
                    if (wa.HarvestPlot(pp)) harvested++;
                }
                uint expected = new Pcg32(12345UL, 1).NextUInt();   // MakeWorld 的种子
                uint afterHarvest = wa.Rng.NextUInt();
                list.Add(new CheckResult("v191①：幸运判定**不消费 World.Rng**（收 18 次后的下一个随机数与全新流一致）",
                    harvested > 0 && afterHarvest == expected,
                    $"{harvested} 次收割后 {afterHarvest} vs 全新流 {expected}"));
            }

            // ═══════════════════════ ② 初始暴击伤害 150% ═══════════════════════
            Expect(list, "v191②：初始暴击伤害 150%（tuning.player.critDmg）", b.critDmg, 1.5f);
            {
                float critDmg = st0.Get(StatId.CritDmg);
                float plain = DamageResolver.WeaponDamage(4f, 0f, 1f, false, critDmg, 0f);
                float crit = DamageResolver.WeaponDamage(4f, 0f, 1f, true, critDmg, 0f);
                float ratio = plain > 0f ? crit / plain : 0f;
                list.Add(new CheckResult("v191②：同一刀「暴击 ÷ 不暴击」= 1.50（1.5 倍而不是 2 倍）",
                    Mathf.Abs(ratio - 1.5f) < 1e-4f,
                    $"不暴击 {plain:0.##} → 暴击 {crit:0.##}｜比值 ×{ratio:0.###}"
                    + $"（旧值 2.0 时会是 ×2.000）"));
                // ★ v194（用户需求④"抽卡暴击伤害提供修改 5%"）：`CritDmg Percent` **3 → 5** ★
                //   抽卡那一支仍然是**乘法**：150% × (1 + 5/100) = **157.5%**
                //   下面写**字面值** 5 而不是读表 —— 读表会退化成"自己等于自己"，
                //   表被改回 3 也判不出来（与 0.455 / 80 那几条同一条理由）✓
                var stCd = new StatBlock(t.caps, t.player);
                stCd.AddModifier(new StatModifier(StatId.CritDmg, 1f + 5f / 100f, ModOp.Mul, "check:v194"));
                list.Add(new CheckResult("v194④：暴伤卡是乘法（150% × 1.05 = 157.5%，面板 +5%）",
                    Mathf.Abs(stCd.Get(StatId.CritDmg) - 1.575f) < 1e-3f
                    && ImGuiHud.AddRelative(stCd.Get(StatId.CritDmg), b.critDmg) == "+5%",
                    $"{stCd.Get(StatId.CritDmg) * 100f:0.#}%｜面板 {ImGuiHud.AddRelative(stCd.Get(StatId.CritDmg), b.critDmg)}"));
                // ★ v194 追加：表里那张卡**实际写的就是 5**（否则上面那条验的是手搓的修饰符，
                //   而不是玩家真的会抽到的那张卡）★
                {
                    int amt = -1;
                    for (int i = 0; i < t.levelReward.stats.Length; i++)
                        if (t.levelReward.stats[i].id == "CritDmg") { amt = t.levelReward.stats[i].amount; break; }
                    list.Add(new CheckResult("v194④：抽卡池里 CritDmg 那张卡的 amount = 5",
                        amt == 5, $"表值 {amt}"));
                }
            }

            // ═══════════════════════ ③ 小猪刷新率 ═══════════════════════
            {
                float[] byLv = new float[4];
                string[] nm = new string[4];
                bool sameAsHeart = TuningDef.PigRateByLevel.Length == TuningDef.HeartDropRateByLevel.Length
                                   && TuningDef.PigLevelNames.Length == TuningDef.HeartDropLevelNames.Length;
                for (int i = 0; i < 4; i++)
                {
                    byLv[i] = TuningDef.PigRateOf(i);
                    nm[i] = TuningDef.PigLevelNameOf(i);
                    if (i < TuningDef.HeartDropRateByLevel.Length
                        && Mathf.Abs(TuningDef.PigRateByLevel[i] - TuningDef.HeartDropRateByLevel[i]) > 1e-6f) sameAsHeart = false;
                    if (i < TuningDef.HeartDropLevelNames.Length
                        && TuningDef.PigLevelNames[i] != TuningDef.HeartDropLevelNames[i]) sameAsHeart = false;
                }
                list.Add(new CheckResult("v191③：小猪刷新率 4 级 = 2% / 4% / 6% / 8%（与爱心逐位同值）",
                    Mathf.Abs(byLv[0] - 0.02f) < 1e-6f && Mathf.Abs(byLv[1] - 0.04f) < 1e-6f
                    && Mathf.Abs(byLv[2] - 0.06f) < 1e-6f && Mathf.Abs(byLv[3] - 0.08f) < 1e-6f
                    && sameAsHeart,
                    $"0~3 级：{byLv[0] * 100f:0}% / {byLv[1] * 100f:0}% / {byLv[2] * 100f:0}% / {byLv[3] * 100f:0}%"
                    + $"｜名字 {string.Join("/", nm)}｜与爱心同表={sameAsHeart}"));

                list.Add(new CheckResult("v191③：等级名 = 低/中/高/MAX，MAX = 3（与爱心同口径）",
                    nm[0] == "低" && nm[1] == "中" && nm[2] == "高" && nm[3] == "MAX"
                    && TuningDef.PigRateMaxLevel == 3,
                    $"{string.Join("/", nm)}｜MaxLevel={TuningDef.PigRateMaxLevel}"));

                // 面板那一行（口径与 `DrawStatsColumns` 里那两行同源）
                int pl = Mathf.RoundToInt(st0.Get(StatId.PigRate));
                string pigRow = $"小猪刷新率　{TuningDef.PigLevelNameOf(pl)}（{TuningDef.PigRateOf(pl) * 100f:0}%）";
                list.Add(new CheckResult("v191⑤：面板「小猪刷新率」一行开局 = 低（2%）",
                    pigRow == "小猪刷新率　低（2%）", pigRow));

                // 道具/卡牌把等级顶到 MAX 之后**不会越过 3** —— 这就是"拥有道具上限"的机制根
                var stPig = new StatBlock(t.caps, t.player);
                for (int i = 0; i < 4; i++) stPig.AddModifier(new StatModifier(StatId.PigRate, 1f, ModOp.Add, "check:v191"));
                list.Add(new CheckResult("v191③：叠 4 级仍被硬钳在 **3（MAX）** ⇒ 「拥有道具上限」有机制根（不会算出越界概率）",
                    Mathf.Abs(stPig.Get(StatId.PigRate) - 3f) < 1e-4f
                    && Mathf.Abs(TuningDef.PigRateOf(Mathf.RoundToInt(stPig.Get(StatId.PigRate))) - 0.08f) < 1e-6f,
                    $"叠 4 次后等级 {stPig.Get(StatId.PigRate):0.#}｜概率 {TuningDef.PigRateOf(3) * 100f:0}%"));
            }

            // ═══════════════════════ ④ 货币 / 经验倍率进收割 ═══════════════════════
            HarvestOnce(0f, 1.0f, 1.0f, out int g10, out float e10, out string g10Text, out string e10Text);
            HarvestOnce(0f, 1.5f, 1.5f, out int g15, out float e15, out string g15Text, out string e15Text);
            list.Add(new CheckResult("v191④：货币/经验倍率 1.5 ⇒ 同一块地**到账**金币与经验各 ×1.5",
                g15 == Mathf.RoundToInt(cellGold * 1.5f) && Mathf.Abs(e15 - cellExp * 1.5f) < 1e-3f
                && g10 == cellGold,
                $"倍数 1.0：金 {g10} / 验 {e10:0.#}｜倍数 1.5：金 {g15} / 验 {e15:0.#}"
                + $"（应 {Mathf.RoundToInt(cellGold * 1.5f)} / {cellExp * 1.5f:0.#}）"));
            list.Add(new CheckResult("v191④：弹字**同步更新**（弹出来的数字 = 实际到账的数字，不是乘之前的基数）",
                g15Text == "金币+" + g15 && e15Text == "经验值+" + Mathf.RoundToInt(e15)
                && g10Text == "金币+" + g10,
                $"倍数 1.0：{g10Text} / {e10Text}｜倍数 1.5：{g15Text} / {e15Text}"));

            // ═══════════════════════ ⑤ 抽卡池 12 → 13 ═══════════════════════
            {
                RewardStatDef[] pool = t.levelReward.stats;
                RewardStatDef luckCard = null;
                for (int i = 0; i < pool.Length; i++)
                    if (pool[i] != null && string.Equals(pool[i].id, "Luck", StringComparison.OrdinalIgnoreCase)) luckCard = pool[i];
                list.Add(new CheckResult("v191⑤：抽卡池 13 张（v190 的 12 张 + 幸运值），幸运卡 = Add 3",
                    pool.Length == 13 && luckCard != null
                    && string.Equals(luckCard.mode, "Add", StringComparison.OrdinalIgnoreCase)
                    && luckCard.amount == 3,
                    $"{pool.Length} 张" + (luckCard != null ? $"｜幸运卡 mode={luckCard.mode} amount={luckCard.amount}" : "｜**没有** Luck 卡")));

                // 卡面文案：走与 UI 同一个判定（`IsPercentPoints`）⇒ 显示成「幸运值 +3%」而不是「+3」
                var card = new RunSession.LevelRewardCard { Def = luckCard, Amount = 3, Copies = 1 };
                list.Add(new CheckResult("v191⑤：抽卡界面把幸运卡印成「幸运值 +3%」（不是「+3」）",
                    card.Label == "幸运值 +3%", card.Label));
            }
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
            // ★ v187：**"算出来的值 = 应有的值"那一层**（用户需求④：三种伤害初值 1 → 0）★
            //   为什么必须与上面那堆"算式是否自洽"的断言分开：
            //     上面验的是**公式本身**（加法、乘法、暴击、下限各管一段）；
            //     这两条验的是**入口的默认值**——把 `player.meleeDamage` 从 0 改回 1，
            //     下面第一条会立刻从 4 变成 5 ✓（这正是用户报的那个"白送的 1 点"）
            //   4 = 锄头 `tools.hoe.baseValue`；2 = 暴击伤害 200%（`player.critDmg`）
            Expect(list, "武器伤害 = 基础值原样（近战伤害 0 ⇒ 4 就是 4，没有白送的 1）",
                DamageResolver.WeaponDamage(4f, 0f, 1f, false, 2f, 0.1f), 4f);
            Expect(list, "武器伤害 暴击 = 基础值 × 暴击伤害（4 → 8）",
                DamageResolver.WeaponDamage(4f, 0f, 1f, true, 2f, 0.1f), 8f);
            // ★★ v195：**爆炸倍率整条删除** ⇒ 这三条改了判据而**没有删断言** ★★
            //   改前：`ExplodeFormula(19, 1/2/1.5)` 验"爆炸 = 普通 × 倍率"（倍率制，v109）
            //   改后：肥料与另外两把武器**同一个式子** ⇒ 要钉的三件事变成
            //     ① 肥料 = 基础值原样（基础 5、爆炸伤害 0）② 肥料吃爆炸伤害这一项（+3 ⇒ 8）
            //     ③ 肥料掷暴击时整条 ×2（8 ⇒ 16）—— 与锄头/水壶逐位同形 ✓
            //   （用户原话："肥料伤害=（基础伤害+爆炸伤害）*伤害倍率"）
            Expect(list, "v195：肥料公式 = 基础值原样（爆炸倍率删除后没有第二层乘区）",
                DamageResolver.WeaponBlastDamage(5f, 0f, 1f, false, 2f, 0.1f), 5f);
            Expect(list, "v195：肥料吃爆炸伤害（+3 ⇒ 5+3=8）",
                DamageResolver.WeaponBlastDamage(5f, 3f, 1f, false, 2f, 0.1f), 8f);
            Expect(list, "v195：肥料暴击 = 整条算完 ×2（8 ⇒ 16）",
                DamageResolver.WeaponBlastDamage(5f, 3f, 1f, true, 2f, 0.1f), 16f);
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
            // v209（用户原话「降低前期升级需要经验值」）：**expBase 20→12**，而 11 与 1.35 **未动**
            //   ⇒ 只降"每级固定底" ⇒ 降幅随等级收敛：L1 -40.0%／L5 -8.8%／L10 -3.4%
            //   ⇒ 到 10 级累计 893 → 821（-8.1%）；10 条期望值全部随之更新，**一条没删** ✓
            float[] expected = { 12f, 23f, 40f, 60f, 83f, 109f, 136f, 164f, 194f, 226f };
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
            // ★ v211：**夹具改成"一定越过上限"**（断言本身一个字没改）★
            //
            // ── 为什么必须改（不是为了让自检变绿）──
            //   本条守的性质是：「`AtkSpeed` 超过 `caps.atkSpeed` 时被**钳在** `caps.atkSpeed`」✓
            //   改前夹具是 `+5`，而 v210 把 `caps.atkSpeed` 从 **3.0** 提到了 **9999（哨兵）**
            //   ⇒ `1.0 + 5 = 6 < 9999` ⇒ **这一下根本没碰到上限**，
            //     于是读数拿 6 去比 9999 ⇒ "期望 9999 实际 6" 变红 ✗
            //   ⚠️ 这是 v210「九项上限 → 9999」的**副作用**，与本轮锄头改动无关
            //      （本轮改动只碰 `RunSession` 的挥击判定与自检新增的三条 v211 判据）✓
            // ── 换成什么 ──
            //   按"`上限 + 100`"加，任何 caps 值都一定越界 ⇒ 断言重新量到**钳位**这件事 ✓
            //   期望值仍是 `t.caps.atkSpeed`（跟着表走），没有写死 9999 ✓
            block.AddModifier(new StatModifier(StatId.AtkSpeed, t.caps.atkSpeed + 100f, ModOp.Add, "check"));
            Expect(list, "攻速硬上限", block.Get(StatId.AtkSpeed), t.caps.atkSpeed);
            // v162（A1）：三种伤害类型各是一条独立的加法属性，**逐条验**（以前这里只有一条 AtkDamage）
            block.AddModifier(new StatModifier(StatId.MeleeDamage, 0.4f, ModOp.Add, "check"));
            Expect(list, "加法修正常规生效（近战）", block.Get(StatId.MeleeDamage), 0.4f);

            // v45：伤害属性与伤害倍率是两条并列的独立属性
            // ★ v187：三种伤害的**开局初值 1 → 0**（用户需求④："近战/远程/爆炸伤害被默认赋值为 1，应该为 0"）★
            //   改前 `tuning.json` 的 `player.meleeDamage/rangedDamage/blastDamage` 都是 1 ⇒
            //   武器卡上"近战伤害 1"其实是**开局白送的 1 点**，而属性面板显示 `+0`（增量）⇒
            //   两处读起来像"面板有 bug"，实际是**表里的初值不是 0** ✓
            //   断言从 1 改成 0 是**跟着被改的配置走**，不是"改到刚好能过"：
            //   它守的性质始终是"开局三条伤害属性 = 表里填的初值，且逐条独立" ✓
            var fresh = new StatBlock(t.caps, t.player);
            Expect(list, "近战伤害 基础 0（加法属性）", fresh.Get(StatId.MeleeDamage), 0f);
            Expect(list, "远程伤害 基础 0（加法属性）", fresh.Get(StatId.RangedDamage), 0f);
            Expect(list, "爆炸伤害 基础 0（加法属性）", fresh.Get(StatId.BlastDamage), 0f);
            // ★ 三条必须**互不影响** ★：只加近战，远程与爆炸要纹丝不动。
            // 这条抓的是"把三条写成同一条的别名"那类错误 —— 只验"各自能加起来"是抓不到的。
            fresh.AddModifier(new StatModifier(StatId.MeleeDamage, 2f, ModOp.Add, "check"));
            Expect(list, "只加近战 → 近战 +2", fresh.Get(StatId.MeleeDamage), 2f);
            Expect(list, "只加近战 → 远程不变", fresh.Get(StatId.RangedDamage), 0f);
            Expect(list, "只加近战 → 爆炸不变", fresh.Get(StatId.BlastDamage), 0f);

            Expect(list, "伤害倍率 基础 100%（乘法属性）", fresh.Get(StatId.AtkMult), 1f);
            fresh.AddModifier(new StatModifier(StatId.AtkMult, 1.5f, ModOp.Mul, "check"));
            Expect(list, "伤害倍率 乘法修正生效", fresh.Get(StatId.AtkMult), 1.5f);
            Expect(list, "伤害倍率 不影响近战伤害", fresh.Get(StatId.MeleeDamage), 2f);
            Expect(list, "伤害倍率 不影响远程伤害", fresh.Get(StatId.RangedDamage), 0f);
            Expect(list, "伤害倍率 不影响爆炸伤害", fresh.Get(StatId.BlastDamage), 0f);

            // v162：**StatIds.Count 必须等于枚举成员个数**。
            // StatBlock 用它开数组、也用它做上限裁剪循环：少一个不会报错，
            // 只是末尾那几个属性**静默失去上限裁剪**——所以必须有一条钉住它。
            // ★ v191：20 → **22**（新增 `Luck` = 20 / `PigRate` = 21）★
            //   这两个数都写在这里：一个是"枚举成员数"，一个是**字面量**——
            //   字面量是刻意的（只写 `Count == All.Length` 的话，多一个成员它会自动跟着变，
            //   于是"属性表悄悄变长了"这件事永远没人发现 ✗）✓
            // ★ v195：22 → **21**（删除 `ExplodeDamage`；编号重新连续 0..20）★
            list.Add(new CheckResult("StatIds.Count 与枚举成员数一致（v195 = 21，删 ExplodeDamage）",
                StatIds.Count == StatIds.All.Length && StatIds.Count == 21,
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
            // ★ v197：16 → **22**（新增 6 株机制植物；断言不删，只改期望值）
            // ★ v202：22 → **30**（追加 8 株 A/S 机制植物；同样**只改期望值，断言不删**）
            //   ★ 为什么这里必须跟着改、而不是"少查几条更省事" ★
            //   这条断言守的是"表里到底有几株"——它错一位，就说明`seeds.json`与自检对世界的
            //   认识分家了；而分家之后的每一条跨行判据（`SEED-TIER-RATE` / `SEED-ARCHETYPE`）
            //   都是**在错的分母上**做的统计，绿了也不代表表是对的 ✗
            // ★ v253：39 → **51**（表尾追加 12 株；其中 6 株复用旧机制、6 株新机制）
            //   ★ 断言仍然不删、只改期望值 —— 它守的是"表里有几株"这个分母 ✓
            list.Add(new CheckResult("种子 51 个（25 无机制 + 26 株带机制植物，含支援型）", TableDatabase.Seeds.Length == 51, $"{TableDatabase.Seeds.Length}"));
            list.Add(new CheckResult("v23：变异词条已删除", TableDatabase.Mutations.Length == 0, $"{TableDatabase.Mutations.Length} 条"));
            // ★ v201：5 → **6**（新增飞蛾 BOSS 一条；**断言不删，只改期望值** ✓）
            // ★ v302：8 类 = 原来的 6 类（含小猪与飞蛾 BOSS）+ 训练场那两只稻草人（`training_dummy` /
            //   `training_dummy_boss`，都是 `kind=Wander`）。判据本身没放宽，只是把新增的两条算进去。
            // ★ v312：8 → **9**（训练场第三只稻草人 `training_gunner`，`kind=Shooter` —— 会开枪的那只）。
            //   同一个口径、只改期望值：这条断言守的是"表里到底有几类敌人"这个**分母**，
            //   分母错了之后的每一条跨行统计都是在错的分母上做的 ✓
            list.Add(new CheckResult("敌人 9 类（含小猪、飞蛾 BOSS 与三只训练稻草人）", TableDatabase.Enemies.Length == 9, $"{TableDatabase.Enemies.Length}"));
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
            // ★★ v196：下限 300 → **225**（用户原话"敌人整体强度过高，生命值再降低一点"）★★
            // 这条断言编码的是**设计下限**「BOSS 必须比小兵肉一个数量级」，不是某个具体数 ⇒
            // 降血后按同一个 −25% 同步到 225。断言**没有删**，判别力也还在：
            // 谁把 BOSS 的 base 调到与小兵同量级（例如 20），它立刻变红 ✓
            // 判据写作「= 小兵的 107 倍」更本质，但这里保留**字面下限** —— 与上面 80→60 那条同一种写法 ✓
            list.Add(new CheckResult("Boss 基础血量仍够肉（≥ 225，v196 前是 ≥ 300）",
                boss.BaseHp >= 225f, $"{boss.BaseHp}"));

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
            // ★ v191：**夹具把幸运值归零**（不是把属性关掉，是让这一组夹具只测它自己那条乘区）★
            //
            // 为什么需要这一行：幸运是"收割时 5% 概率 ×2" ⇒ 若不归零，
            //   下面所有**逐分精确**的收益断言（RichHarvest ×1.30 / ThirstBloom ×1.60 /
            //   ChainHarvest 整列 / 等级 ×1.70）都会变成"由哈希决定是否翻倍"的**抛硬币** ✗
            //   抛中就该红 —— 那是**假警报**：那条乘区其实完好，被幸运叠了一层 ✓
            // ★ 为什么不是"关掉幸运这个机制" ★
            //   机制没有开关（也不该有：玩家侧它就是一直开着的）✓
            //   这里改的是**夹具的输入**：幸运 = 0 ⇒ `RollHarvestLuck` 必返 false ⇒
            //   这些断言测的就只有它自己那一条乘区 ✓
            // ★ 幸运本身仍然被验（不许"归零躲开"就完事）★
            //   见 `V191Checks`：那里把幸运设成 0.05 / 1.0 / 0 三档，正面验 ×2 与 X2 弹字 ✓
            p.Progress.Stats.SetBase(StatId.Luck, 0f);
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
            // ★ v195：**期望值回到个位刻度**（用户原话"玩家血量恢复到10，护盾2"）★
            //   判据的**形式**没变（仍然在钉"量级"，不是钉死某一个数），只换了刻度 ✓
            //   为什么必须钉量级：这两个数决定"敌人几下打死玩家"——它被改成 1000 或 3
            //   都会立刻红，所以这条断言仍然有判别力 ✓
            list.Add(new CheckResult("玩家生命 10 量级（v195：血池回调，hpUnit 1）",
                t.player.maxHp >= 8f && t.player.maxHp <= 20f, $"{t.player.maxHp}"));
            list.Add(new CheckResult("护盾 2 量级（v195：与生命同一个单位）",
                t.player.shield >= 1f && t.player.shield <= 5f, $"{t.player.shield}"));
            EnemyDef grunt = TableDatabase.GetEnemy("grunt");
            // ★ v180：期望值从 `1f` 改成 `0.7f` —— 敌人伤害整体 **×0.7**（用户要求"难度过高"）★
            //   `enemies.json` 的 grunt `touchDamage` 1.0 → 0.7 ⇒ 第 1 波（曲线起点 ×1.0）应为 0.7 ✓
            //   ★ 为什么这里仍写**字面值**而不是改成 `grunt.TouchDamage` ★
            //     写成 `grunt.TouchDamage` 会退化成"自己等于自己"，表被误改也判不出来 ✗
            //     0.7 是**设计值**：玩家 10 HP ⇒ 14 次触碰致死（改前 10 次）✓ 表漂移必须让它变红 ✓
            // ★ v191：**再乘前段压低项**（第 1 分钟 = 0.65）★
            //   设计值仍是 0.7（`enemies.json` 一个字没动），降的是**曲线的前段** ✓
            //   期望写成字面值 0.455 = 0.7 × 0.65 ⇒ 既钉住设计值、也钉住压低深度 ✓
            // ★ v195：**期望值 ÷10**（"敌人数值回调，伤害除以10取整"）★
            //   设计值仍是"第 1 分钟触碰 = base × 前段压低"，刻度回到个位：
            //   v195 是 `0.7 × 0.65 = 0.455` ✓
            //   ⚠️ 这条**只钉曲线值**（小数）。"打到玩家身上是多少血点"是另一句
            //      （`Round(0.455 × hpUnit 1) = max(1, 0) = 1`），见 `PlayerDamagePoints` ✓
            Expect(list, "首波基础兵触碰伤害 = 0.7 × 0.65 = 0.455（v195 刻度；前段压低）", grunt.TouchDamageForWave(1), 0.455f);
            // ★ v168：**不再硬编码血量** ✗
            //   原写法是 `Expect(..., "首波基础兵生命 8（三次受击死亡）", ..., 8f)` ✓
            //   而我把 `enemies.json` 的 grunt/shooter `baseHp` 从 8 调到 10（为了让"3 击击杀"重新成立）
            //   ⇒ 这条断言立刻变红 ✗ **而血量本身是对的** ✓
            //   ⇒ 病根是【断言硬编码了数据】✓ 所以我改成**从 def 读基础值** ✓
            //   这样以后调 `baseHp` 不会再让断言挂 ✓ 而"曲线起点 = 基础"这个规则仍然被钉住 ✓
            // ★ v191：首波不再是"基础值"，而是 **基础值 × 前段压低项 0.65**（2.8 → 1.82）★
            //   「开局一刀死」的判据（`CeilToInt(血/每击) == 1`）仍然成立 —— V20Checks 里那两条在钉它 ✓
            Expect(list, "首波基础兵血量 = 基础值 × 0.65（v191 前段压低）",
                grunt.HpForWave(1, 1), grunt.BaseHp * 0.65f);

            // ★ T2：删掉「幼苗免疫伤害 / 成株可被伤害」两条断言 ✗ ★
            //   它们测的是"植物作为**可被攻击的战斗单位**"：
            //   判据落在 `DamageResolver.Resolve` 的 `target is PlantAlly seedling && seedling.Growing`
            //   那条前置上（`Combat.cs:206`）✓ 植物已不参战 ⇒ 该前置整条不可达 ⇒ 断言已无被测对象 ✓
            //   计数：本块删 2 条（`Expect` 1 + `CheckResult` 1）✓
            SeedDef radish = TableDatabase.GetSeed("radish_guard");

            // ── 护盾回复：受击后需等待 shieldRegenDelay 秒，之后每秒 max(最大护盾×10%, 1)
            //
            // ★ v184：这两个数改成**从表里读**，不再写死 5s / 6s ★（用户需求④：延迟改为 5 秒）
            //   旧写法 `p2.Tick(5f)` + `p2.Tick(6f)` 把"等待 5 秒不回、之后开始回"**编码成了 10 秒的语义**：
            //   延迟改成 5 之后，`Tick(5f)` 正好跨过门槛 ⇒ 第一条断言会假红 ✗
            //   现在用 `delay` 推导两个时刻（`delay−0.2` 与 `delay+0.5`）⇒
            //   以后调这个配置，断言跟着表走，**守的仍然是同一条规则** ✓
            World w2 = MakeWorld(out PlayerActor p2);
            w2.DealDamage(new DamageRequest { Type = DamageType.Physical, RawValue = 1f, Target = p2, IgnoreDodge = true });
            p2.Health.Shield = 0f;
            float regenDelay = Mathf.Max(0.1f, t.player.shieldRegenDelay);
            float beforeDelay = Mathf.Max(0.1f, regenDelay - 0.2f);
            p2.Tick(beforeDelay, w2);
            list.Add(new CheckResult($"受击 {beforeDelay:0.#}s（< 延迟 {regenDelay:0.#}s）内不回护盾", p2.Health.Shield <= 0.001f,
                $"delay={t.player.shieldRegenDelay:0}s，{beforeDelay:0.#}s 时 {p2.Health.Shield:0.##}"));
            p2.Tick(regenDelay + 0.5f, w2);
            list.Add(new CheckResult("超过延迟后开始回护盾", p2.Health.Shield > 0.5f && p2.Health.Shield <= p2.Health.MaxShield,
                $"{regenDelay + 0.5f:0.#}s 后 {p2.Health.Shield:0.##}/{p2.Health.MaxShield:0.##}"));

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
            // ★ v252：第一拍的长度**从配置读**（原来是写死的 5 秒，隐含"间隔 4 秒"）★
            //   本轮把 `spawnIntervalStart` 4 → 6（前期降难）⇒ 写死 5 秒就永远走不到第一拍，
            //   这条"越界仍会刷怪"的 P0 断言会因为一个**与它无关的调参**变红 ✗
            //   判据没改（"第 21 分钟场上敌人 > 0"），只把"等多久"改成数据驱动 ✓
            float firstBeat = Mathf.Max(1f, TableDatabase.Tuning.waves.spawnIntervalStart) + 0.5f;
            longDirector.Tick(firstBeat, w6);          // 走过第一拍 → 只出预警
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
            list.Add(new CheckResult("玩家工具弹速已加速", t.tools.can.projectileSpeed >= 15f && t.tools.fertilizer.projectileSpeed >= 6f,
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
            V201MothChecks(list);        // v201：新 BOSS「飞蛾」三阶段 + 旧 BOSS 两招（纯函数，无副作用）
        }

        /// <summary>
        /// ★★ v201：**新 BOSS「飞蛾」（三阶段）+ 旧 BOSS 两个新招式**的回归网 ★★
        ///
        /// 全部是**纯函数/纯数据**断言（不建 World、不建网格、不进播放模式）⇒ 必然可复现 ✓
        /// 与 `TimeSystemChecks` 同一条理由：当前运行时验证通道不稳，这一组仍然能跑。
        /// </summary>
        private static void V201MothChecks(List<CheckResult> list)
        {
            // ── ① 三阶段必须是**按血量均分**（>66% 毛毛虫 / 33~66% 蛹 / <33% 飞蛾）──
            var rows = new (float pct, int want)[]
            {
                (1.00f, 0), (0.90f, 0), (0.67f, 0), (0.66f, 1), (0.50f, 1), (0.34f, 1),
                (0.33f, 2), (0.10f, 2), (0.00f, 2)
            };
            bool phaseOk = true;
            var detail = new System.Text.StringBuilder();
            for (int i = 0; i < rows.Length; i++)
            {
                int got = BossEnemy.MothPhaseOf(rows[i].pct);
                if (got != rows[i].want) phaseOk = false;
                if (i > 0) detail.Append('｜');
                detail.Append(rows[i].pct.ToString("0.00")).Append("→").Append(got);
            }
            list.Add(new CheckResult("v201 飞蛾三阶段按血量均分（>66% 虫 / 33~66% 蛹 / <33% 蛾）",
                phaseOk, detail.ToString()));

            // ── ② 冲刺次数：毛毛虫 3 次、飞蛾 5 次；飞蛾更快 ──
            bool dashOk = BossEnemy.LarvaDashCount == 3 && BossEnemy.MothDashCount == 5
                       && BossEnemy.MothDashSpeedMul > BossEnemy.LarvaDashSpeedMul
                       && BossEnemy.MothDashWindup < BossEnemy.LarvaDashWindup;
            list.Add(new CheckResult("v201 飞蛾冲刺：毛毛虫 3 次 / 飞蛾 5 次且更快（前摇更短）",
                dashOk, $"虫 {BossEnemy.LarvaDashCount} 次 ×{BossEnemy.LarvaDashSpeedMul} 前摇 {BossEnemy.LarvaDashWindup}s"
                      + $"｜蛾 {BossEnemy.MothDashCount} 次 ×{BossEnemy.MothDashSpeedMul} 前摇 {BossEnemy.MothDashWindup}s"));

            // ── ③ 蛹：不能移动 + 有激光（预警 → 落下 → 灼烧）且落点锁定 ──
            bool pupaOk = BossEnemy.PupaLaserWarnSeconds > 0f
                       && BossEnemy.PupaLaserLockTail > 0f
                       && BossEnemy.PupaLaserLockTail < BossEnemy.PupaLaserWarnSeconds
                       && BossEnemy.PupaLaserBurnSeconds > 0f
                       && BossEnemy.PupaLaserBurnTick > 0f;
            list.Add(new CheckResult("v201 蛹：激光 预警→锁定→落下→灼烧（烧 3 秒，每 0.5 秒一跳）",
                pupaOk, $"预警 {BossEnemy.PupaLaserWarnSeconds}s（末 {BossEnemy.PupaLaserLockTail}s 锁定）"
                      + $"｜烧 {BossEnemy.PupaLaserBurnSeconds}s ×{BossEnemy.PupaLaserBurnTick}s 一跳"));

            // ── ④ 飞蛾：消失一段时间（不可选中）后跳砸 ──
            bool vanishOk = BossEnemy.MothVanishSeconds > 0f && BossEnemy.MothSlamWindup > 0f
                         && BossEnemy.MothSlamDamageRatio >= 1f
                         && BossEnemy.MothVanishEvery > BossEnemy.MothVanishSeconds;
            list.Add(new CheckResult("v201 飞蛾：消失 1.6s（不可选中）→ 现身 → 跳砸前摇 0.5s",
                vanishOk, $"消失 {BossEnemy.MothVanishSeconds}s｜跳砸前摇 {BossEnemy.MothSlamWindup}s"
                        + $"｜伤害 ×{BossEnemy.MothSlamDamageRatio}"));

            // ── ⑤ 旧 BOSS「吼叫」：5 环、1 秒一环 ──
            list.Add(new CheckResult("v201 旧 BOSS 吼叫：圆环波 5 次、间隔 1 秒",
                BossEnemy.RoarRingCount == 5 && Mathf.Abs(BossEnemy.RoarRingInterval - 1f) < 1e-4f,
                $"{BossEnemy.RoarRingCount} 环 × {BossEnemy.RoarRingInterval:0.0}s"
                + $"｜每环 {BossEnemy.RoarBulletsPerRing} 发"));

            // ── ⑥ 旧 BOSS「蓄力爆炸」：有蓄力（= 预警圈时长）与大范围 ──
            bool blastOk = BossEnemy.BlastChargeSeconds >= 1f && BossEnemy.BlastRadius >= 5f
                        && BossEnemy.BlastDamageMul > 1f;
            list.Add(new CheckResult("v201 旧 BOSS 蓄力爆炸：蓄力 1.8s → 半径 6.5m 爆炸",
                blastOk, $"蓄力 {BossEnemy.BlastChargeSeconds}s｜半径 {BossEnemy.BlastRadius}m"
                      + $"｜伤害 ×{BossEnemy.BlastDamageMul}"));

            // ── ⑦ 数据表：moth 真的存在、真的走 BOSS 通道、几何与几何键都对 ──
            EnemyDef moth = TableDatabase.GetEnemy("moth");
            bool defOk = moth != null && moth.Id == "moth" && moth.Kind == EnemyKind.Boss
                      && moth.Radius > 0f && moth.JumpRadius > 0f
                      && moth.HpForWave(20, 4) > moth.HpForWave(10, 2);          // 20 分比 10 分肉
            list.Add(new CheckResult("v201 enemies.json 有 moth（kind=Boss，第 20 分钟比第 10 分钟肉）",
                defOk, moth == null ? "表里没有 moth ✗"
                    : $"{moth.Name}｜半径 {moth.Radius}｜血 {moth.HpForWave(20, 4):0}（20 分 / bossIndex 4）"));

            // ── ⑧ 三套形态的几何**互不相同**且体型递增（虫 < 蛹 < 蛾）──
            ProceduralBugs.Shape larva = ProceduralBugs.MothShapeOf("moth", 0);
            ProceduralBugs.Shape pupa = ProceduralBugs.MothShapeOf("moth", 1);
            ProceduralBugs.Shape queen = ProceduralBugs.MothShapeOf("moth", 2);
            bool formOk = ProceduralBugs.SpeciesOf("moth", true) == ProceduralBugs.Species.MothQueen
                       && larva.Kind == ProceduralBugs.Species.Larva
                       && pupa.Kind == ProceduralBugs.Species.Pupa
                       && queen.Kind == ProceduralBugs.Species.MothQueen
                       && larva.Unit < pupa.Unit && pupa.Unit < queen.Unit
                       && larva.AbdomenLen > larva.ThoraxLen          // 毛毛虫：长腹
                       && pupa.AbdomenR > pupa.ThoraxR                // 蛹：纺锤
                       && queen.WingLen > 0f && larva.WingLen <= 0f;  // 飞蛾有翅、毛毛虫无翅
            list.Add(new CheckResult("v201 飞蛾三形态：几何互不相同（虫=长腹多足 / 蛹=纺锤 / 蛾=大翅）",
                formOk, $"unit {larva.Unit:0.00}/{pupa.Unit:0.00}/{queen.Unit:0.00}"
                      + $"｜腹长 {larva.AbdomenLen:0.00}｜蛹腹宽 {pupa.AbdomenR:0.00}｜蛾翅长 {queen.WingLen:0.00}"));

            // ── ⑨ 形态 → 物种的映射是**唯一实现**（表现层与探针读同一处）──
            bool mapOk = ProceduralBugs.MothFormOf(0) == ProceduralBugs.Species.Larva
                      && ProceduralBugs.MothFormOf(1) == ProceduralBugs.Species.Pupa
                      && ProceduralBugs.MothFormOf(2) == ProceduralBugs.Species.MothQueen
                      && ProceduralBugs.MothFormOf(-1) == ProceduralBugs.Species.Larva      // 非飞蛾 → 不参与
                      && ProceduralBugs.IsMothForm(ProceduralBugs.Species.Larva)
                      && !ProceduralBugs.IsMothForm(ProceduralBugs.Species.Giant);
            list.Add(new CheckResult("v201 飞蛾形态 → 物种 只有一个映射（MothFormOf）",
                mapOk, "0→Larva 1→Pupa 2→MothQueen，其余形态不受影响"));

            // ── ⑩ 老 BOSS 的几何**没被碰**：`SpeciesOf("boss", true)` 仍是 Giant ──
            bool geoOk = ProceduralBugs.SpeciesOf("boss", true) == ProceduralBugs.Species.Giant
                      && ProceduralBugs.SpeciesOf("boss", false) == ProceduralBugs.Species.Stag;
            list.Add(new CheckResult("v201 旧 BOSS 几何未被改动（SpeciesOf(\"boss\") 仍是 Giant）",
                geoOk, "boss → Giant（鞘翅 + 大颚那一套原样）"));
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

            // ── ③ BossSchedule：★★ v201 改版（用户原话："10分钟刷新旧版boss，20分钟刷新飞蛾boss，
            //        后续boss为随机"）⇒ 期望值整条换成 10（旧）/ 20（飞蛾）/ 之后随机 ★★
            //    改的是**期望值**，断言一条没删 ✓（旧值 10,18,24,28,30,32,34,36,38,40 由 10 → 10 分钟间隔
            //    的旧表得来；新表前两次固定、之后随机）
            var clk = new Clock();
            var sched = new BossSchedule();
            sched.Reset(clk);
            bool seqOk = true;
            var got = new System.Collections.Generic.List<string>();
            var minutes = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 10; i++)
            {
                // 直接跳到"下一次到点"的时刻（模拟时间流逝）
                clk.Tick(sched.SecondsUntilNext(clk));
                int minute = Mathf.RoundToInt(clk.GameTime / 60f);
                minutes.Add(minute);
                got.Add(minute + (BossSchedule.KindOf(i + 1) == BossKind.Moth ? "蛾" : "旧"));
                if (i == 0 && minute != 10) seqOk = false;          // 第 10 分钟：旧版
                if (i == 1 && minute != 20) seqOk = false;          // 第 20 分钟：飞蛾
                if (i >= 2)
                {
                    // 之后随机：严格递增，且每次间隔落在 8~14 分钟（步长 0.5）
                    int gap = minutes[i] - minutes[i - 1];
                    if (gap < 8 || gap > 14) seqOk = false;
                }
                sched.OnSpawned(clk);
            }
            list.Add(new CheckResult("Boss 时刻表 = 10（旧）→ 20（飞蛾）→ 之后随机 8~14 分钟",
                seqOk, string.Join(",", got)));

            // 种类：第 1 次旧版、第 2 次飞蛾、之后两种都会出现（随机不是"恒为一种"）
            bool kindOk = BossSchedule.KindOf(1) == BossKind.Old
                       && BossSchedule.KindOf(2) == BossKind.Moth
                       && BossSchedule.IdOf(1) == "boss" && BossSchedule.IdOf(2) == "moth";
            int moth = 0, old = 0;
            for (int i = 3; i <= 14; i++)
            {
                if (BossSchedule.KindOf(i) == BossKind.Moth) moth++; else old++;
            }
            kindOk = kindOk && moth > 0 && old > 0;
            list.Add(new CheckResult("Boss 种类 = 10 旧版 / 20 飞蛾 / 之后随机（两种都出现过）",
                kindOk, $"1→{BossSchedule.KindOf(1)}｜2→{BossSchedule.KindOf(2)}｜3..14 蛾 {moth} 旧 {old}"));

            // 间隔序列本身（纯查表）：前两次 10，之后 8~14
            bool gapOk = BossSchedule.GapMinutesOf(0) == 10f && BossSchedule.GapMinutesOf(1) == 10f;
            float g3 = BossSchedule.GapMinutesOf(3);
            gapOk = gapOk && g3 >= 8f && g3 <= 14f
                 && Mathf.Abs(g3 * 2f - Mathf.Round(g3 * 2f)) < 1e-3f;      // 落在 0.5 分钟网格上
            list.Add(new CheckResult("Boss 间隔：首两次 10 分钟，之后随机 8~14（步长 0.5）",
                gapOk, $"0→{BossSchedule.GapMinutesOf(0)} 1→{BossSchedule.GapMinutesOf(1)} 3→{g3:0.0}"));

            // ★★ "既有序列不漂"的**可执行证据** ★★
            //   派生哈希若偷偷喝了 `World.Rng`，那么"跑过 10 次刷新"之后
            //   同种子的随机流会整体漂一位 ⇒ 既有自检/探针的读数全变。
            //   这里直接量：一个钉死种子的 `Pcg32`，跑完 10 次 `OnSpawned` 之后，
            //   它接下来的 3 个输出必须与**另一个从未被碰过的**同种子流的 3 个输出逐位相同 ✓
            var rngA = new Pcg32(12345UL);
            var rngB = new Pcg32(12345UL);
            var probeClock = new Clock();
            var probeSched = new BossSchedule();
            probeSched.Reset(probeClock);
            for (int i = 0; i < 10; i++) { probeClock.Tick(60f); probeSched.OnSpawned(probeClock); }
            bool rngSame = true;
            for (int i = 0; i < 3; i++)
            {
                if (rngA.NextUInt() != rngB.NextUInt()) rngSame = false;
            }
            list.Add(new CheckResult("BossSchedule 不消费 World.Rng（派生哈希 ⇒ 既有随机序列不漂）",
                rngSame, rngSame ? "10 次刷新后 Pcg32 状态与未使用者逐位相同" : "Pcg32 被消费了 ✗"));

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
            // ★ 时间轴 = 60(Idle 前置) + 2.5(入场) + LoopSeconds + 10(停靠) + 2.5(离场) + 余量 ★
            //   v175：绕场 35 → **5** ✗ 所以这条预算的每一项都改成**由常量推导** ✗
            //   （原来硬编码 6500 tick = 130s，是照 47.5s 的绕场算的 ⇒ 现在绕场只剩 5s，
            //     130s 会跑过整整一个周期零 5 秒 ⇒ 序列里会混进**第二轮**的 Entering ⇒ 假红 ✓）
            int ticksFull = Mathf.CeilToInt(
                (ShopTruck.CycleSeconds + ShopTruck.EnterSeconds + ShopTruck.LoopSeconds
                 + ShopTruck.DockSeconds + ShopTruck.ExitSeconds + 1f) / 0.02f);
            var clk3 = new Clock();
            var truck = new ShopTruck();
            truck.Reset(clk3);
            var seen = new System.Collections.Generic.List<ShopTruck.Phase>();
            seen.Add(truck.Current);
            for (int i = 0; i < ticksFull; i++)
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
                walkOk, string.Join("→", seen) + $"（{clk3.GameTime:0.0}s / {ticksFull} tick）"));

            // 清扫规则：必须**真的把状态机推到 Looping** 再验，不能在 Idle 下问
            // （在 Idle 下 ShouldSweep 本来就返回 false，那样的断言恒真、测不出任何东西 ✗）
            var clk4 = new Clock();
            var truck2 = new ShopTruck();
            truck2.Reset(clk4);
            // 推到 Looping **中段**：Idle(60s) → Entering(2.5s) → Looping 前半 ✓
            // v175：绕场只有 5 秒 ✗ ⇒ 落点必须**落在 Looping 窗口里面** ✗
            //   旧写法硬编码 3300 tick（66s）✓ 在绕场 35s 时落在 Looping 第 3.5 秒 ✓
            //   而绕场的 5 秒窗口是 62.50~67.50s ⇒ 66s 其实还在里面 ✓ **但只差 1.5 秒** ✗
            //   ⇒ 只要有人再调时长，这条就会红成"假线索" ✗（本文件已经被这种硬编码坑过一次 ✓）
            //   ⇒ 改成**由常量推导**：落在 Looping 正中间 ✓
            int ticksToLoopMid = Mathf.CeilToInt(
                (ShopTruck.CycleSeconds + ShopTruck.EnterSeconds + ShopTruck.LoopSeconds * 0.5f) / 0.02f);
            for (int i = 0; i < ticksToLoopMid; i++) { clk4.Tick(0.02f); truck2.Tick(clk4); }
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

            // ★ 回归护栏：停靠期车必须【真的停在停靠位】，并且【悬在战场正上方】★
            //
            // 这一条钉的是那个把 10 秒停靠窗吃成 0.5 秒的缺陷：
            //   `PointAt(PhaseT)` 在 Dock 期让车又跑一整圈（离停靠位最远 28.67m），
            //   而 `SamplePoint` 返回 `DockPoint`（恒为 0.00m）✓
            // 判据用**引擎自己的取位入口**，所以"改回去"会立刻红 ✓
            //
            // v175 增补"高度"这一维 ✗：
            //   用户新时序"停在战场**正上方**" ⇒ 只验平面位置**不够** ✗
            //   一辆停在 `(0,0)` 但 **y = 0**（贴地）的车也满足旧判据 ✗ 而它不是"正上方" ✓
            //   ⇒ 同时钉 ① 每帧高度 == `HoverHeight` ② 平面位置 == `DockPoint` ✓
            float maxOff = 0f;
            float minH = float.MaxValue, maxH = 0f;
            for (int i = 0; i < 25; i++)      // 25 × 0.2s = 5s，仍在 10 秒停靠期内 ✓
            {
                clk5.Tick(0.2f);
                truck3.Tick(clk5);
                if (truck3.Current != ShopTruck.Phase.Dock) break;
                maxOff = Mathf.Max(maxOff, (truck3.SamplePoint(clk5) - ShopTruck.DockPoint).magnitude);
                float hy = truck3.HeightAt(clk5);
                minH = Mathf.Min(minH, hy);
                maxH = Mathf.Max(maxH, hy);
            }
            list.Add(new CheckResult("商店车停靠期真的停在停靠位（SamplePoint 恒为 DockPoint）",
                maxOff < 1e-4f,
                $"停靠期离停靠位最远 {maxOff:0.0000}（归一化）｜"
                + $"旧算法 `PointAt(PhaseT)` 同时刻为 {(ShopTruck.PointAt(truck3.PhaseT(clk5.GameTime)) - ShopTruck.DockPoint).magnitude:0.0000}"));
            list.Add(new CheckResult(
                // ★ v180：标题原来写"悬在战场正上方"✗ —— 车现在**贴地**（用户要求"不飞天"）✓
                //   判据本身没变（停靠期高度恒为 `HeightAt` 的那个常量）⇒ 仍然是一条好护栏：
                //   它钉的是"停靠期高度**不漂**"（旧缺陷是停靠期车又跑一圈 ⇒ 位置与高度都变 ✓）
                $"商店车停靠期高度恒定（= {ShopTruck.HoverHeight:0.0}m，v180 起贴地）",
                maxOff < 1e-4f && Mathf.Abs(minH - ShopTruck.HoverHeight) < 1e-3f
                && Mathf.Abs(maxH - ShopTruck.HoverHeight) < 1e-3f,
                $"高度区间 [{minH:0.00}, {maxH:0.00}]m｜目标 {ShopTruck.HoverHeight:0.0}m"
                + $"｜平面 ({ShopTruck.DockPoint.x:0.00},{ShopTruck.DockPoint.y:0.00})×{TableDatabase.Tuning.farm.arenaHalf:0} 半宽"));

            // ★ 升空/落地必须是**连续**的 ✗（v175 新增）★
            //   若 `Looping` 末尾停在 0、而 `Dock` 直接取 `HoverHeight`，
            //   车会在阶段切换的那一帧**瞬移 6 米** ✗ 玩家看到的是"跳了一下"✓
            //
            // ★ v175.1：这条断言第一版**测错了东西** ✗ 记下来免得下次再犯 ★
            //   第一版比的是"按 0.02s 步进采样到的最后一帧"与"Dock 首帧"✗
            //   而步进**永远取不到 `PhaseT == 1`**（最后一个 Looping 帧在 0.996 附近 ✓）
            //   ⇒ 它实际测的是"**最后一步的高度增量**"✗ 一个**采样伪影** ✗
            //   · 线性曲线下那是 `H ÷ (15%×5s) × 0.02s ≈ 0.16m`，而真机上报 **0.0044m**
            //     ⇒ 说明真机的 `dt` 与 0.02s 不同 ✓（`FixedTick` 的 dt 由调用方给 ✗
            //        所以那个差值**本来就不该稳定** ✓）
            //   · 我先把曲线改成 **smoothstep**（两端一阶导为 0 ✓）⇒ 同一口径降到 0.0126m ✓
            //     仍 > 1e-3 ✗ ⇒ **问题不在曲线，在测量口径** ✓
            //   ⇒ 现在分两条测，各测**能测准**的那个量：
            //     ① **采样断言**（下面这条）：差值必须小到**玩家看不出来** ✓
            //        容差取一个**有物理含义**的数：5cm ✗ 远小于"跳一下"，又远大于步进噪声 ✓
            //        ⇒ 真出 bug（那 6 米瞬移）时它照样红 ✓
            //     ② **结构断言**（再下面那条）：端点值**真的相等** ✓ 容差 1e-5 ✓
            //        它不经过任何时间步进 ⇒ 与 dt 无关 ✓ 这才是"连续"的定义 ✓
            var clk6 = new Clock();
            var truck4 = new ShopTruck();
            truck4.Reset(clk6);
            int ticksToLoopEnd = Mathf.CeilToInt(
                (ShopTruck.CycleSeconds + ShopTruck.EnterSeconds + ShopTruck.LoopSeconds - 0.02f) / 0.02f);
            for (int i = 0; i < ticksToLoopEnd; i++) { clk6.Tick(0.02f); truck4.Tick(clk6); }
            bool stillLooping = truck4.Current == ShopTruck.Phase.Looping;
            float hLoopEnd = truck4.HeightAt(clk6);
            clk6.Tick(0.02f); truck4.Tick(clk6);
            bool nowDock = truck4.Current == ShopTruck.Phase.Dock;
            float hDock = truck4.HeightAt(clk6);
            const float seamVisible = 0.05f;          // 5cm = 看得见"跳一下"的门槛
            list.Add(new CheckResult("商店车绕场→停靠：高度连续（步进采样值，容差 5cm）",
                stillLooping && nowDock && Mathf.Abs(hLoopEnd - hDock) < seamVisible,
                $"{ShopTruck.Phase.Looping} 末尾 {hLoopEnd:0.0000}m → {ShopTruck.Phase.Dock} 开始 {hDock:0.0000}m"
                + $"（差值 {Mathf.Abs(hLoopEnd - hDock):0.0000}m < {seamVisible:0.00}m；"
                + $"注意两值都是**步进采样** ✗ 取不到 `PhaseT == 1` ✓）"));

            // ② 结构断言：端点**真的**相等（"连续"的定义 ✓ 与步长无关 ✓）
            //    `PhaseT == 1` 时升空曲线 = `smoothstep(1) × H = H` ✓ ⇒ 与 `Dock` 的 `HoverHeight` 必然相等 ✓
            //    ⚠️ 若有人把升空曲线改成"只升到 90%"，这条立刻红 ✓
            float smEnd = 1f * 1f * (3f - 2f * 1f);
            list.Add(new CheckResult("商店车绕场端点高度 == 停靠高度（升空曲线端点取满）",
                Mathf.Abs(smEnd - 1f) < 1e-5f
                && Mathf.Abs(smEnd * ShopTruck.HoverHeight - ShopTruck.HoverHeight) < 1e-5f,
                $"smoothstep(1) = {smEnd:0.000000} ⇒ 绕场端点 {smEnd * ShopTruck.HoverHeight:0.000000}m"
                + $" == 停靠 {ShopTruck.HoverHeight:0.000000}m"
                + $"（升空段 = 绕场后 {(1f - ShopTruck.HoverRiseT) * 100f:0}%）"));

            // ★ v180：这条断言被**重写**（v175 版本把 bug 本身写成了通过条件）★
            //
            // ── 为什么必须改（事实先摆清）──
            // v175 版本的第三项是 `ShopTruck.HoverHeight > 0f` ✗
            // 也就是它把"车**悬在空中**"编码成了**通过条件** ✓
            // 而用户这次的原话恰好相反：
            //   "商店车最终停止位置**还是飞天的** —— 它停稳后在**空中**（y 太高），没有落到地面" ✓
            // ⇒ 把 `HoverHeight` 改成 0（贴地）之后，这一项就红了 ✗
            //
            // ⚠️ **红的是第三项（高度），不是 xz** —— 别把回显读成"停靠点变了" ✗
            //   停靠点自 **v175** 起就一直是 **归一化 (0.00, 0.00) = 战场正中心**
            //   （`ShopTruck.Path[8]`，见那里 89-93 行的推导："旧值 (0,0.52) 是地面上的边角 ⇒
            //    改 (0,0) = 战场中心的正上方"）✓ 本轮**只改高度、没动 xy** ✓
            //   而失败回显里那串 `停靠 xz = (0.00,0.00)` 只是**报告文本**（每帧都打印）✓
            //
            // ── 现在它同时查两件事（缺一不可）──
            //   ① **平面**：停在设计好的停靠点。期望值**写死**成 归一化 (0.00, 0.00)
            //      ⇒ 世界 (0.0, 0.0)（× `arenaHalf` 20m）✓
            //      （"田边"是 v175 **之前**的旧设计（`DockPoint.y = 0.52`，场地顶边内侧），
            //        它会让玩家站在战场中心时离车 10m+ ⇒ 够不到；v175 搬到中心是既定决策，
            //        本轮**不推翻**它 —— 本轮要修的只是"飞天"✗ 不是"位置"✓）
            //   ② **高度**：**贴地**。判据取**车高的一半**（用户："不飞天"）：
            //      车高 = 运行期实测包围盒 `Awning.max.y − Wheel.min.y` = 8.215 − 6.000 = **2.215m**
            //      （在 y=6 的停靠帧量的 ⇒ 与高度无关，是纯车体尺寸 ✓）
            //      ⇒ 一半 = **1.1075m** ⇒ 只要 ≤ 1.10m，车体主要部分就在地面附近 ✓
            //      再加下限 **≥ −0.01m**：贴地的另一半定义是**不许陷进地里** ✓
            //   ⚠️ 这条同时把"改回 6m 悬停"挡在外面：6.00 > 1.10 ⇒ 立刻红 ✓
            const float carHeightMeasured = 2.215f;            // 实测（见上）
            const float halfCarHeight = carHeightMeasured * 0.5f;
            bool dockSampled = minH < float.MaxValue;          // 上面那 25 次抽样至少跑过一次
            float dockTop = dockSampled ? maxH : float.NaN;
            list.Add(new CheckResult(
                "商店车停靠位正确且**贴地**（xz=战场中心 0,0 · 离地高度 ≤ 车高一半）",
                Mathf.Abs(ShopTruck.DockPoint.x - 0f) < 1e-4f
                && Mathf.Abs(ShopTruck.DockPoint.y - 0f) < 1e-4f
                && dockSampled && dockTop <= halfCarHeight && minH >= -0.01f,
                $"停靠 xz = ({ShopTruck.DockPoint.x:0.00},{ShopTruck.DockPoint.y:0.00})"
                + $" ⇒ 世界 ({ShopTruck.DockPoint.x * TableDatabase.Tuning.farm.arenaHalf:0.0},"
                + $"{ShopTruck.DockPoint.y * TableDatabase.Tuning.farm.arenaHalf:0.0})"
                + $"（期望 归一化 (0.00,0.00) = 战场中心）"
                + $"｜高度区间 [{minH:0.00}, {maxH:0.00}]m 必须 ≤ 车高一半 {halfCarHeight:0.00}m"
                + $"（车高 {carHeightMeasured:0.000}m = 轮底→棚顶实测）"
                + $"｜v180 改前 = 6.00m（悬空 6.00m）"));

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
            list.Add(new CheckResult("商店车路径形状：右上入场 → 战场中心停靠",
                path.Length >= 6
                && path[0].x > 0.5f && path[0].y > 0.5f                    // 入场在右上
                && Mathf.Abs(ShopTruck.DockPoint.x) < 0.05f                // 停靠在正中（xz）
                && Mathf.Abs(ShopTruck.DockPoint.y) < 0.05f,               // ★ v175：不再"在顶边上"✗
                $"{path.Length} 点｜入场=({path[0].x:0.00},{path[0].y:0.00})"
                + $"｜停靠=({ShopTruck.DockPoint.x:0.00},{ShopTruck.DockPoint.y:0.00})+高度{ShopTruck.HoverHeight:0.0}m"));

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

            // ★ 速度：绕场必须在需求给的 `LoopSeconds`（v175 = 5 秒）内跑完一圈 ★
            //
            // ★ 这条断言 v175 被【反方向改写】了 ✗ 说明为什么 ★
            //   旧断言："速度必须高于玩家（> 3.5 m/s），否则清不动" ✓ 它钉的是**下限** ✓
            //   而新时序把绕场压到 5 秒 ⇒ 速度变成 ~21 m/s ✗ **上限**才是现在该钉的风险 ✓
            //   ⚠️ 断言的意图从"够快"变成"够快但没快到荒谬" ✗ 两者都要，所以改为**区间** ✓
            //   ⚠️ 而且旧写法用的半宽是 **50m**（一句"按半宽 50m 估"）✗
            //      工程实际 `farm.arenaHalf` = **20**（`tuning.json`）✗
            //      ⇒ 旧断言测的是一个**不存在的场地**：50m 下只有 10 m/s ✗ 看起来"温和" ✓
            //      ⇒ 现在直接读 `TableDatabase.Tuning.farm.arenaHalf` ✓ 与玩法同一份数 ✓
            float half = TableDatabase.Tuning.farm.arenaHalf;
            float worldLen = ShopTruck.NormalizedLength() * half;
            float speed = worldLen / ShopTruck.LoopSeconds;
            list.Add(new CheckResult("商店车绕场速度：明显快于玩家，且没有快到荒谬",
                speed > 3.5f && speed < 40f,
                $"路径 {worldLen:0.0}m（半宽 {half:0}m × 归一化 {ShopTruck.NormalizedLength():0.000}）"
                + $" / {ShopTruck.LoopSeconds:0}s = {speed:0.0} m/s（玩家约 3.0）"));

            // ★ v175 直钉需求：绕场必须正好是用户说的 5 秒 ★
            //   它是一条"需求数字"断言 ✗ 不是推导值 ⇒ 有人改 `LoopSeconds` 就会红 ✓
            //   （这正是我想要的：新时序的两条硬数字都该有守卫 ✓ 另一条是周期 60s ✓）
            list.Add(new CheckResult("商店车绕场时长 = 需求值 5 秒（每分钟出现一次 · 5 秒一圈）",
                Mathf.Abs(ShopTruck.LoopSeconds - 5f) < 1e-3f && Mathf.Abs(ShopTruck.CycleSeconds - 60f) < 1e-3f,
                $"绕场 {ShopTruck.LoopSeconds:0}s｜周期 {ShopTruck.CycleSeconds:0}s"
                + $"｜入场 {ShopTruck.EnterSeconds:0.0}s｜停靠 {ShopTruck.DockSeconds:0}s"
                + $"｜离场 {ShopTruck.ExitSeconds:0.0}s｜不在场 {ShopTruck.IdleHoldSeconds:0}s"));
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

            // ── ② 新规则真的在跑：雨滴概率 ≈ 0.20 ──
            //   样本量 400 的理由：Binomial(400, 0.20) 的标准差 = √(400×0.20×0.80) = 8 滴
            //     ⇒ 比率标准差 ≈ 8/400 = 0.020
            //     ⇒ 容差 ±0.06 ≈ **±3.0σ** ⇒ 假红概率约 **0.27%** ✓
            //   （v167 写这段时 p=0.25 ⇒ ±2.8σ、假红 0.6%；v209 把 p 提到 0.20 ⇒ 同样的 ±0.06 更严）
            //   （为什么不取 200：±0.06 在 200 样本下只有 ±2.1σ ⇒ 假红 ~3.5% ✗ 一天跑几十次必红 ✓）
            //   为什么**不**写"必须恰好 0.20"：那是随机量 ⇒ 一定假红 ✗
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
                // ★ v174：敌人【开局】基础血量降到"玩家近战一下打死"（用户要求）★
                // ⇒ 旧的"第 1 波 3–5 次击杀 / 第 2 波难度不变 / 第 4 波开始变肉"是按血 10 写的 ✗
                //   而血 4 时三次击杀数是 1 / 2 / 2 ⇒ 后两条【必然】为假 ✓
                // ⇒ 所以换成【按血量比较】✗ —— 这样以后调 `baseHp` 不会再让它们挂 ✓
                //   （★ 与上面 `:346` 那条"首波血量 = 基础值"同一个做法 ✓）
                // ★ v174 补：判据要与**实际机制**等价，而不是"差不多"★
                //   伤害是浮点、血量也是浮点，所以"一刀死"的真正判据是
                //   `CeilToInt(血 / 每击) ≤ 1`（= 血 ≤ 每击）✗ 不是 `血 ≤ 每击 + 1e-3` ✓
                //   后者在"血比每击大一丝"时会**放行**一个实际要两下的配置 ✗
                //   而这正是必须由断言抓住的那类漂移（改 `baseHp` 时最容易踩）✓
                list.Add(new CheckResult($"{id} 开局一刀死（首波血 ≤ 玩家一次近战）",
                    hits1 == 1,
                    $"血 {d.HpForWave(1, 1):0.#} ≤ 每击 {perHit:0.00} ⇒ {hits1} 次"));
                list.Add(new CheckResult($"{id} 第 2 波血量高于第 1 波",
                    d.HpForWave(2, 1) > d.HpForWave(1, 1),
                    $"血 {d.HpForWave(1, 1):0.#} → {d.HpForWave(2, 1):0.#}"));
                list.Add(new CheckResult($"{id} 第 4 波血量高于第 2 波",
                    d.HpForWave(4, 1) > d.HpForWave(2, 1),
                    $"血 {d.HpForWave(2, 1):0.#} → {d.HpForWave(4, 1):0.#}"));
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
            // v184：锚点又一次变，**这次是需求驱动的数值表改动**，不是断言漂移：
            //   用户需求⑤："锄头基础数值提升，略微高于水壶，但低于肥料"
            //   ⇒ `tools.hoe.baseValue` 3 → 4、`tools.fertilizer.baseValue` 3 → 5（水壶保持 3）
            //   ⇒ 零加成每击：锄 (4+1)=5 / 壶 (3+1)=4 / 肥 (5+1)=6 ⇒ 满足 壶 4 < 锄 5 < 肥 6 ✓
            // ★ v187：三种伤害属性开局 1 → 0（用户需求④）⇒ 三个锚点**各减 1**：
            //   锄 (4+0)=4 / 壶 (3+0)=3 / 萝卜 (2+0)=2 ✓
            //   关系式（壶 < 锄 < 肥）**不变**，变的只有那个"开局白送的 1" ✓
            //   （下面是**公式的结果**，不是手写的常数：`baseValue` 仍从表里读 ✓）
            // ★★ v195：三个锚点全部 ÷10（武器基础 40/30/50 → **4/3/5**）★★
            //   关系式（萝卜 2 < 壶 3 < 锄 4 < 肥 5）不变，变的只有刻度 ✓
            // ★★ v199：**锚点又一次变，这次是需求驱动的数值表改动**（用户原话：
            //   『玩家锄头初始伤害调整为8，水壶6，肥料12』）★★
            //   ⇒ `tools.hoe.baseValue` 4 → **8**、`tools.can.baseValue` 3 → **6**
            //   ⇒ 两个锚点 4.00 / 3.00 → **8.00 / 6.00**（萝卜卫士 2.00 **没动**，它的
            //     `BaseValue` 在 `seeds.json` 里，不在本次范围内 ✓）
            //   关系式（萝卜 2 < 壶 6 < 锄 8 < 肥 12）**不变**，变的只有刻度 ✓
            //   ⚠️ 本轮**没有**给肥料补一条零加成断言（v195 起就没有）—— 它由下面
            //     ③ 那两条「加法线 / 与 `WeaponDamage` 逐位同形」的机制断言覆盖，
            //     数值锚点只钉在锄/壶两条上；加一条会让断言总数从 469 变化，
            //     而本轮的任务是「改期望值、不删断言」✓
            Expect(list, "锄头单击（零加成，近战伤害）", DamageResolver.AttackFormula(t.tools.hoe.baseValue,
                st.Get(StatId.MeleeDamage), st.Get(StatId.AtkMult), false, 1f, 0f), 8.00f);
            Expect(list, "水壶单发（零加成，远程伤害）", DamageResolver.AttackFormula(t.tools.can.baseValue,
                st.Get(StatId.RangedDamage), st.Get(StatId.AtkMult), false, 1f, 0f), 6.00f);
            SeedDef radish = TableDatabase.GetSeed("radish_guard");
            Expect(list, "萝卜卫士每次（零加成，近战伤害）", DamageResolver.AttackFormula(radish.BaseValue,
                st.Get(StatId.MeleeDamage), st.Get(StatId.AtkMult), false, 1f, 0f), 2.00f);

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

            // ── ③ 肥料爆炸（v109 倍率制 → **v195 与另两把武器同一条式子**）─────────────
            // 断言验**机制**，不锁数值：爆炸伤害属于平衡，会随调参变化，
            // 把它写死会让"调平衡"变成"改测试"。这里验的是两条语义：
            //   ① 肥料伤害随「爆炸伤害」属性**线性**增长（它是加项，不是倍率）
            //   ② 同一份属性下，肥料 = 锄头/水壶的**同一个函数**（三条线不该有第二个式子）
            float blastStat = st.Get(StatId.BlastDamage);
            float blast0 = DamageResolver.WeaponBlastDamage(5f, 0f, 1f, false, 2f, 0.1f);
            float blastA = DamageResolver.WeaponBlastDamage(5f, blastStat, 1f, false, 2f, 0.1f);
            list.Add(new CheckResult("v195：肥料是『基础 + 爆炸伤害』的加法线（倍率已删除）",
                Mathf.Abs(blastA - (5f + Mathf.Max(0f, blastStat))) < 0.001f,
                $"5 + {blastStat:0.##} = {blastA:0.#}（爆炸伤害为 0 时 {blast0:0.#}）"));
            list.Add(new CheckResult("v195：肥料的式子与锄头逐位同形（同一个 WeaponDamage 实现）",
                Mathf.Abs(DamageResolver.WeaponBlastDamage(5f, blastStat, 1f, false, 2f, 0.1f)
                          - DamageResolver.WeaponDamage(5f, blastStat, 1f, false, 2f, 0.1f)) < 0.0001f,
                $"{DamageResolver.WeaponBlastDamage(5f, blastStat, 1f, false, 2f, 0.1f):0.###}"
                + $" vs {DamageResolver.WeaponDamage(5f, blastStat, 1f, false, 2f, 0.1f):0.###}"));

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
            // ★ v204：**每发之前显式重选一次肥料** ★
            //   v202 那时这里写过"因为 `TickPendingBlasts` 会自动切回锄头，所以每发要重选"——
            //   那条自动切换已在 v204 撤销（见 `RunSession.TickPendingBlasts` 的注释）✓
            //   但**这一行保留**，理由与 v202 无关、自 v202 起一直成立：
            //   **夹具不该依赖"某个副作用恰好不触发"** —— 武器的值是本夹具的前置条件，
            //   就得由本夹具自己写死 ✓（否则读数是"上一段代码碰巧留下的状态"）
            fp.WeaponIndex = PlayerActor.WeaponFertilizer;
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
            fp.WeaponIndex = PlayerActor.WeaponFertilizer;   // v204：同上，每发前显式重选一次肥料
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
            // ★ v191：第 1 波 = 基础 × 前段压低项（0.65），不再是"等于基础值" ✗
            Expect(list, "第 1 波 = 基础值 × 前段压低项（曲线起点 1.0 × 0.65）",
                grunt.HpForWave(1, 1), grunt.BaseHp * EnemyDef.EarlyNerfFactor(1));
            Expect(list, "第 3 波基础兵血量 = 基础 × (1 + 2a + 4b) × 前段压低",
                grunt.HpForWave(3, 1),
                grunt.BaseHp * EnemyDef.WaveGrowth(3, hpA, hpB) * EnemyDef.EarlyNerfFactor(3));

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

            // ★ v180：期望值的基数从 `1f` 改成 **`grunt.TouchDamage`（= 0.7）** ★
            //   这条断言测的是**规则**"伤害 = 基础 × 曲线"，不是某个具体数 ⇒
            //   基数必须从 def 读，否则每次调平衡都要改断言 ✗（同 `HpForWave` 那几条的做法 ✓）
            //   它仍有判别力：若 `DamageForWave` 漏乘曲线（或曲线系数读错），期望与实际立刻分叉 ✓
            // ★ v191：曲线后**再乘前段压低项**（与 HP 侧同一个函数）★
            Expect(list, "第 3 波接触伤害 = 基础 × 曲线 × 前段压低",
                grunt.TouchDamageForWave(3),
                grunt.TouchDamage * EnemyDef.WaveGrowth(3, dmgA, dmgB) * EnemyDef.EarlyNerfFactor(3));
            EnemyDef boss = TableDatabase.GetEnemy("boss");
            list.Add(new CheckResult("BOSS 弹幕伤害也吃同一条曲线",
                boss.ProjectileDamageForWave(20) > boss.ProjectileDamageForWave(1),
                $"{boss.ProjectileDamageForWave(1):0.00} → {boss.ProjectileDamageForWave(20):0.00}"));

            // ══ ★★ v191：前段压低项（用户要求"下调前期敌人数值"）的 5 条判据 ★★ ══
            // 矛盾与解法：上一轮把 20 分钟钉成硬目标（80 / 6 / 4 / 200 / 8），而成长曲线是**单调**的
            // ⇒ 压前期必然连带压 20 分钟。解法是一个**只在窗口内 < 1、窗口外精确 1** 的因子
            // （`Defs.EnemyDef.EarlyNerfFactor`，窗口 = `tuning.enemies.earlyNerfEnd`）。
            // 下面五条同时钉住"前期真的低了"与"20 分钟一位没动"，并证明那两个配置键**真的被读**：
            //   ① 第 1 分钟因子 = 1 − earlyNerf（0.35 ⇒ 0.65）—— 键没被读就会变成 1 ⇒ 红
            //   ② 窗口外（第 9 / 20 分钟）因子精确为 1 —— 用指数衰减就永远到不了 1 ⇒ 红
            //   ③ 20 分基础兵血仍是 80.00 —— 硬目标不许被这次下调打破
            //   ④ BOSS 第 1 分钟仍是 300（不吃压低）—— 用户原话"BOSS 暂时不调整"
            //   ⑤ 前期单调且第 1 分钟低于改前的 2.8 —— 证明"下调"发生在它该发生的地方
            Expect(list, "v191 前段压低项：第 1 分钟 = 1 − earlyNerf = 0.65",
                EnemyDef.EarlyNerfFactor(1), 1f - 0.35f);
            Expect(list, "v191 前段压低项：窗口外**精确**为 1（第 9 / 20 分钟，指数衰减给不出 1）",
                Mathf.Max(Mathf.Abs(EnemyDef.EarlyNerfFactor(9) - 1f),
                          Mathf.Abs(EnemyDef.EarlyNerfFactor(20) - 1f)), 0f);
            // ★ v195：硬目标随"敌人血 ÷10"变成 **80**（28 → 2.8 ⇒ 800 → 80）★
            // ★★ v196：再随"敌人 baseHp ×0.75"变成 **60**（2.8 → 2.1 ⇒ 80 → 60）★★
            //   用户原话"敌人整体强度过高，生命值再降低一点"⇒ 这条断言**没删**，
            //   只是期望值跟着新的设计目标走；"20 分钟必须是 base × 28.571"这条**规则**一个字没变
            //   （右边写成 `grunt.BaseHp * 28.571` 也能过，但这里刻意留**字面数**：
            //     它钉的是"第 20 分钟到底是多少血"这个**具体目标**，而不只是"公式有没有乘对" ✓）
            Expect(list, "v196 20 分基础兵血 = base 2.1 × 28.571 = 60.00（v195 的 80 → 60，−25%）",
                grunt.HpForWave(20, 1), 60f);
            Expect(list, "v191 BOSS 不吃前段压低（第 1 分钟仍是基础血 300）",
                boss.HpForWave(1, 1), boss.BaseHp);
            list.Add(new CheckResult("v191 前段压低只压前期（第 1 < 5 < 10 分钟，且第 1 分钟低于改前 baseHp）",
                grunt.HpForWave(1, 1) < grunt.HpForWave(5, 1)
                && grunt.HpForWave(5, 1) < grunt.HpForWave(10, 1)
                && grunt.HpForWave(1, 1) < grunt.BaseHp,
                $"1/5/10 分 {grunt.HpForWave(1, 1):0.00} / {grunt.HpForWave(5, 1):0.00} / {grunt.HpForWave(10, 1):0.00}"
                + $"｜改前第 1 分钟 {grunt.BaseHp:0.00} ⇒ 下调 {100f * (1f - grunt.HpForWave(1, 1) / grunt.BaseHp):0.#}%"));

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
            //
            // ★ v190：`ExplodeRange`（爆炸范围 +5%）进了**核心 5% 组** ★
            //   用户需求②把它和"伤害倍率/攻速/攻程"放在同一档（"提升 5%"）⇒
            //   它不是"其余 3%"那一档 ⇒ 必须加进 `core`，否则这张新卡会被判成错的 ✓
            //   ★ 这条断言**没有放宽**：它仍然要求"倍率卡只有 5% 与 3% 两档、且各档成员是点名的那几个" ✓
            // ★★ v194：`CritDmg` 进**核心 5% 组**（用户需求④："抽卡暴击伤害提供修改 5%"）★★
            //   于是"倍率卡只有 5% 与 3% 两档"这条判据不变，只是 5% 那一组的成员多了一个 ✓
            //   （仍然**没有放宽**：要求每一张倍率卡的数值恰好落在它该在的那一档）
            int percentCards = 0, wrongPercent = 0;
            for (int i = 0; i < t.levelReward.stats.Length; i++)
            {
                RewardStatDef r = t.levelReward.stats[i];
                if (!string.Equals(r.mode, "Percent", System.StringComparison.OrdinalIgnoreCase)) continue;
                percentCards++;
                bool core = r.id == "AtkMult" || r.id == "AtkSpeed" || r.id == "AtkRange"
                            || r.id == "ExplodeRange"        // v190
                            || r.id == "CritDmg";            // v194
                if (r.amount != (core ? 5 : 3)) wrongPercent++;
            }
            list.Add(new CheckResult("升级抽卡：倍率/攻速/范围/爆炸范围/暴击伤害 +5%，其余倍率卡（移速）+3%",
                percentCards > 0 && wrongPercent == 0 && percentCards == 6,
                $"{percentCards} 张倍率卡，其中 {wrongPercent} 张数量不对"));

            // ── ⑤ 小猪概率 ──
            // v123：概率从 5% 大幅降到 1%（需求）。同时加了"每回合保底一只"，
            // 所以这里验两件事：概率确实降了，且**保底机制存在**（否则 20 波约 82% 一猪不出，
            // 而小猪是肥料唯一来源 —— 循环会断）。
            //
            // ★ v191（用户需求③）：**期望值 1% → 2%，断言一条没删** ★
            //
            // 为什么期望值变了：用户要求"小猪刷新率与爱心掉落率**相同的机制与数值**" ⇒
            //   概率不再是那个写死的常量 `waves.pigChance = 0.01`（**已删除**），
            //   而是"属性等级 → 查表"：`PigRateByLevel = {2%,4%,6%,8%}`，
            //   0 级（低）= **2%** ⇒ 期望值就该是 2%，不是 1% ✓
            // ★ 为什么改期望值不算"把断言改松" ★
            //   判据还是同两条：① 0 级的具体数值被钉住（现在是 2%）② 它仍然"足够低"
            //   （≤2% 那条继续钉着"必须有保底"这个前提）✓ 数值变了、判据的形式没变 ✓
            // ⚠️ `waves.pigChance` 这个键**已从配置里删除** —— 因为它不再生效，
            //   留着就是"两处真相"（表里一个不生效的 1%）✓ 见 `WavesCfg` 的注释。
            float pig0 = TuningDef.PigRateOf(0);
            Expect(list, "小猪生成概率 等级0（低）= 2%", pig0, 0.02f);
            list.Add(new CheckResult("小猪概率已大幅下调（原 5%）",
                pig0 <= 0.02f, $"{(pig0 * 100f):0.#}%"
                + $"（20 波全不出的概率约 {Mathf.Pow(1f - pig0, 20f) * 100f:0}%，故必须有保底）"));

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
            //
            // ★ v181（用户第 ③ 条）：**判据翻面** ✗ ★
            //   改前：`收割 → 弹出「收获 xxx」` —— `StartsWith("收获")` 为真才算通过 ✓
            //   用户原话："删除'收获'弹字，只弹经验和金币"
            //   ⇒ 现在「收获 xxx」**不该出现**，而「金币+N」「经验值+N」必须**都**出现 ✓
            //
            //   ⚠️ 为什么改判据、而不是把这条断言删掉 ✗ ★（这一条是刻意的）★
            //     "收割会在屏幕上弹什么字"是玩家直接看到的一条**真实契约** ✓
            //     删掉断言 = 把这条契约从回归里拿掉 ⇒ 以后谁把过滤挪走、或谁又加回一条「收获」，
            //     都不会有人发现 ✗ ⇒ 所以断言**保留**、判据**翻面**：
            //     从"必须弹"改成"必须**不**弹收获 + 必须弹金币与经验" ✓
            //     （条数不变 —— 仍是 437 条里的这一条 ✓）
            //
            //   ⚠️ 判据为什么用 `StartsWith` 而不是全等 ✗
            //     文案带数值（"金币+6"），数值随品质/等级/机制变 ⇒ 全等会变成"每调一次平衡就红一条"✗
            //     这与下面"缺水"那一条（`StartsWith("缺水")`）是同一条规矩 ✓
            ps.Popups.Clear();
            ps.World.HarvestPlot(plot);
            bool sawHarvest = false, sawGold = false, sawExp = false;
            for (int i = 0; i < ps.Popups.Count; i++)
            {
                string popupText = ps.Popups[i].Text;
                if (popupText.StartsWith("收获")) sawHarvest = true;
                if (popupText.StartsWith("金币")) sawGold = true;
                if (popupText.StartsWith("经验")) sawExp = true;
            }
            // ★ T2：判据里的 `ps.World.Plants.Count == 0` 已删除 ✗ —— `World.Plants` 已不存在 ✓ ★
            //   （"不产生帮手"那半句随同款副本一起删；现在它是结构事实，不需要断言 ✓）
            list.Add(new CheckResult("收割 → 不弹「收获」✗ 只弹「金币 +N」「经验 +N」（用户要求：只弹经验和金币）",
                !sawHarvest && sawGold && sawExp,
                $"提示 {ps.Popups.Count} 条｜收获={(sawHarvest ? "有✗" : "无✓")}"
                + $" 金币={(sawGold ? "有✓" : "无✗")} 经验={(sawExp ? "有✓" : "无✗")}"
                + $"｜首条「{(ps.Popups.Count > 0 ? ps.Popups[0].Text : "无")}」"));

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
            // ★ v180：这条断言的**方向反转了** —— 名字与判据一起改 ✓
            //   原名字是「全部怪物移速提高」（v48 的需求：把移速抬到 `>= 2.8/2.4/3.2/1.9` 的下限）✗
            //   而 v180 的需求是「降低敌人整体基础数值（难度过高）」⇒ 移速 **×0.9** ✓
            //   两条需求正相反 ⇒ 判据不能沿用旧下限（2.8/2.4/3.2/1.9），必须是**新值本身**：
            //     兵 3.0 → 2.7｜射手 2.6 → 2.34｜精英 3.4 → 3.06｜BOSS 2.0 → 1.8
            //   ★ 为什么写**字面值**而不是"读 def 再 ×0.9" ★
            //     后者自己等于自己，`enemies.json` 被误改也判不出来 ✗（与上面触碰伤害同一条理由）
            //   它仍有判别力：任何一次表改动只要没同步乘数，这四条就会分叉 ✓
            list.Add(new CheckResult("全部怪物移速 = 基准 × 0.9（v180 降低 10%）",
                Mathf.Abs(grunt.MoveSpeed - 2.7f) <= Eps
                && Mathf.Abs(shooter.MoveSpeed - 2.34f) <= Eps
                && Mathf.Abs(jumper.MoveSpeed - 3.06f) <= Eps
                && Mathf.Abs(boss.MoveSpeed - 1.8f) <= Eps,
                $"兵 {grunt.MoveSpeed}／射手 {shooter.MoveSpeed}／精英 {jumper.MoveSpeed}／BOSS {boss.MoveSpeed}"
                + "（基准 3.0／2.6／3.4／2.0 × 0.9）"));
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
                // ★ v191：幸运值回来了（需求①）——它与 `CritRate` 同类，走 `AddPoints`（百分点差）★
                //   开局 `AddPoints(0.05, 0.05)` ⇒ **+0%** ⇒ 这条断言同时钉住了用户那句
                //   "属性界面显示增量 0%" ✓（用 `AddRelative` 会算出 +0%，但换成相对涨幅后
                //   抽一张卡就变 +60% ✗ —— 那正是暴击率那个 bug 的形状）
                ImGuiHud.AddPoints(st.Get(StatId.Luck), b.luck),
                // ★ v195：`ExplodeDamage` 那一行随属性删除 ⇒ 这里少一行（行数 20 → 19，判据不变）★
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

            // v182：暴击率初值已从 **0** 改成 **0.05**（需求"初始暴击率 5%"）。
            //
            // 旧断言 `|Get| < 1e-5` 的前提是"初值恒为 0 ⇒ 乘法道具静默失效"✗
            // 初值一旦非 0，那个前提就**不存在了**，断言必然红 —— 而它红的原因
            // 不是"有人改坏了"，恰恰是"需求变了" ✓ 所以要把**判据**换掉，而不是删掉它 ✗
            //
            // ★ 新判据"两式同源"：`Get == 表值 × 1.05` **并且** `表值 > 0` ★
            //   · 前半句守的是"初值必须被原样读进来、乘法必须按比例生效" ✓
            //   · 后半句 `b.critRate > 0f` **绝对不能省** ✗
            //     省掉之后，谁把初值改回 0，`0 × 1.05 == 0` 依然成立 ⇒ 这条又会变绿，
            //     等于把"初值不许是 0"这个守卫悄悄拆掉了 ✓
            list.Add(new CheckResult("暴击率初值 = 表值（v182：0.05，不再是 0）",
                b.critRate > 0f && Mathf.Abs(st.Get(StatId.CritRate) - b.critRate * 1.05f) < 1e-5f,
                $"初值 {b.critRate:0.###} → ×1.05 = {st.Get(StatId.CritRate):0.0000}"));

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

            // ★ v196：植物信息卡只允许 6 项 ★
            //
            // 用户标准（原话）："植物类删除无关舍弃的描述，现在只有名称、品质、等级、成熟时间、
            // 金币提供量、经验值提供量这些属性（耗水、价格为内定不展示），删除关于攻击、hp，攻击间隔" ✓
            //
            // ★ 这一条**换了检查对象，断言本身没删** ★（规矩："改期望值，不删断言"）
            //   改前查的是「`desc` 都 ≤14 字（种子卡一行放得下）」——
            //   而 `desc` 自 v173 起就没有任何展示点了（"功能："那一行随 `SeedCardLines` 一起删的），
            //   v196 连表里的 `desc` 也清了 ⇒ 那条断言此后只会空转 ✗
            //   改后查**真正会被画出去的那份字符串**：`ImGuiHud.PlantCellInfoLines` ✓
            //   （那个函数因此从 `private` 提到 `internal` —— 自检必须读真货；
            //     照着它再抄一份文案的那天，就是两张表开始分家的那天 ✓）
            //
            // 判据四条，16 品种 × 1/2/3 级 × 两种排法 = 96 张卡逐张验：
            //   ① 窄版（植物格 / 种子卡 / 开局选种）正文恰好 **4 行**，宽版（商店商品行）恰好 **2 行** ✓
            //   ② 每行都以 等级/成熟/金币/经验 开头 ⇒ 结构不会悄悄长出第 7 项 ✓
            //   ③ 四个标签在**每一张卡**里都恰好出现 **一次**（宽版两项并一行也不许少一项）✓
            //   ④ 全场不出现战斗词与内定词（攻击 / 生命 / HP / 攻速 / 伤害 / 护盾 / 移速 /
            //      范围 / 蓄水 / 耗水 / 价格 / 描述 / 机制 / 平平无奇）✓
            //   ⚠️ ④ 里的"蓄水/耗水/价格"是用户点名的**内定不展示**项（数据仍在，只是不上屏 ✓）
            //   ⚠️ 宽版存在的原因写在 `PlantCellInfoLines` 的 `wide` 参数说明里
            //      （商店那一格视口只有 68px，4 行 = 88px 会顶出滚动条 ✓）
            string[] okLabels = { "等级", "成熟", "金币", "经验", "等级影响", "功能" };
            string[] bannedWords = { "攻击", "生命", "HP", "攻速", "伤害", "护盾", "移速",
                                     "范围", "蓄水", "耗水", "价格", "描述", "机制", "平平无奇" };

            // ★ v208：标签判定从 `IndexOf` 改成**按段相等** ★
            //
            // 为什么必须改：v208 新增了 `等级影响　收获 ×1.35、成熟 −13%` 这一行，
            // 它**字面上包含**「等级」和「成熟」两个词 ⇒ 旧的子串判定会把它算成
            // "「等级」出现 2 次 / 「成熟」出现 2 次" ⇒ ③ 直接判 FAIL（而卡片其实是对的）✗
            // 按段（`　` 与 `·` 分隔）比较之后：
            //   `等级影响　收获 ×1.35、成熟 −13%` 的首段是 `等级影响`（≠`等级`），
            //   而 `×1.35、成熟` 这一整段也 ≠ `成熟` ⇒ 计数回到"每项恰好一次" ✓
            // ⇒ 这是把判据**收紧**（从"包含"到"等于"），不是放宽 ✓
            static bool HasTok(string row, string lab)
            {
                string[] toks = row.Split(new[] { '\u3000', '·', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < toks.Length; i++) if (toks[i] == lab) return true;
                return false;
            }
            int badRows = 0, badLabel = 0, badCount = 0, bannedHits = 0;
            string firstBad = "";
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                for (int lv = SeedSlot.StartingLevel; lv <= SeedSlot.MaxLevel; lv++)
                {
                    var slot = new SeedSlot { Def = d, Quality = d.RarityTier, Level = lv };
                    for (int w = 0; w < 2; w++)
                    {
                        bool wide = w == 1;
                        string[] rows = ImGuiHud.PlantCellInfoLines(slot, wide);
                        // ★ v208：窄版 4 行 → **5 行**（新增「等级影响」那一行）★
                        // ★ v213：窄版 5 行 → **6 行**（新增「功能　」一句话定位那一行，见 `ImGuiHud.PlantFuncLine`）★
                        // 宽版仍是 2 行：商店商品行的详情区只有 68px（见 `PlantCellInfoLines` 的 wide 说明），
                        // 多一行就顶出滚动条 ⇒ 这两行**都只进窄版** ✓
                        int expect = wide ? 2 : 6;
                        if (rows.Length != expect)
                        {
                            badRows++;
                            if (firstBad.Length == 0)
                                firstBad = $"{d.Id} Lv{lv} {(wide ? "宽" : "窄")}版正文 {rows.Length} 行（应 {expect}）";
                        }
                        for (int r = 0; r < rows.Length; r++)
                        {
                            bool labelled = false;
                            for (int q = 0; q < okLabels.Length; q++)
                                if (HasTok(rows[r], okLabels[q])) labelled = true;
                            if (!labelled)
                            {
                                badLabel++;
                                if (firstBad.Length == 0) firstBad = $"{d.Id} Lv{lv}「{rows[r]}」无标签";
                            }
                            for (int q = 0; q < bannedWords.Length; q++)
                            {
                                if (rows[r].IndexOf(bannedWords[q], System.StringComparison.Ordinal) < 0) continue;
                                bannedHits++;
                                if (firstBad.Length == 0) firstBad = $"{d.Id} 出现「{bannedWords[q]}」";
                            }
                        }
                        // ③ 每个标签各出现恰好一次 ⇒ 宽版（两项并一行）也不可能漏项
                        // ★ v208：宽版只查前 4 个标签 ★
                        // ★ v213：`okLabels` 末尾追加的「功能」也只进窄版 ⇒ labelCount 的口径不变
                        //   （`wide ? 4 : okLabels.Length` —— 窄版现在要求 6 个标签各出现恰好一次）★
                        // 「等级影响」那一行**只进窄版**（商店商品行视口只有 68px）⇒
                        // 让宽版也要求它出现，等于把"设计上不该有"判成缺项 ✗（本轮实测 66 张宽版全红）
                        int labelCount = wide ? 4 : okLabels.Length;
                        for (int q = 0; q < labelCount; q++)
                        {
                            int hits = 0;
                            for (int r = 0; r < rows.Length; r++)
                                if (HasTok(rows[r], okLabels[q])) hits++;
                            if (hits == 1) continue;
                            badCount++;
                            if (firstBad.Length == 0)
                                firstBad = $"{d.Id} Lv{lv} {(wide ? "宽" : "窄")}版「{okLabels[q]}」出现 {hits} 次";
                        }
                    }
                }
            }
            bool only6 = badRows == 0 && badLabel == 0 && badCount == 0 && bannedHits == 0;
            list.Add(new CheckResult(
                "v196：植物信息卡只剩 6 项（正文 ＋ 抬头 名称/品质），无战斗词与内定词",
                only6,
                only6
                    ? $"{TableDatabase.Seeds.Length} 品种 × {SeedSlot.MaxLevel} 级 × 宽/窄两种排法全通过"
                    : $"行数不符 {badRows}｜缺标签 {badLabel}｜标签计数 {badCount}｜禁区词 {bannedHits}｜首例 {firstBad}"));

            // ★ v198：**植物卡底部"特殊功能"行**（用户原话："在底部显示拥有的特殊功能"）★
            //
            // ── 这条验什么（以及为什么必须验"真的有机制的那 6 株"）──
            //   ① 6 株机制植物：`PlantCardLines` 的最后一行**必须以「特殊功能　」开头**，
            //      且**包含 `TableDatabase.MechShortDesc` 的原文**（= 上屏的不是另抄的一份文案）✓
            //   ② 其余 16 株：同一行必须是「特殊功能　无」（行数一致，卡片不会一行多一行少）✓
            //   ③ 卡片正文 = 4 行数值 ＋ 1 行特殊功能 ⇒ **恰好 5 行**（数值那 4 行由上面 v196 那条钉着）✓
            //   ④ 特殊功能行**不许**出现在宽版里（商店商品行视口只有 68px，见 `PlantCardLines` 的说明）✓
            //   ⑤ 短版必须**真的比长版短**（否则"卡片用短版"这件事等于没做，那 50 字照样塞不进 163px）✓
            //
            // ── 为什么"包含 Describe 原文"这条不能省 ✗ ──
            //   只判"以「特殊功能　」开头"的话，随手写一句「特殊功能　很强」也能过 ✓
            //   而 `Describe` 里的数字全部由 `PlantMechanics` 的常量算出来 ——
            //   卡片与真实生效值分家的那天，就是玩家读到"每层 +3%"而实际 +5% 的那天 ✓
            int mechCards = 0, plainCards = 0, badSpecial = 0, badWidth = 0, badWide = 0, badFunc = 0;
            string firstSpecialBad = "";
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                bool hasMech = !string.IsNullOrEmpty(d.Mechanic)
                               && !string.Equals(d.Mechanic, PlantMechanics.None, System.StringComparison.OrdinalIgnoreCase);
                var slot = new SeedSlot { Def = d, Quality = d.RarityTier, Level = SeedSlot.StartingLevel };
                string[] card = ImGuiHud.PlantCardLines(slot);
                // ★ v208：5 行 → **6 行**（数值 4 + 等级影响 1 + 特殊功能 1）★
                // ★ v213：6 行 → **7 行**（v213 又加了「功能　」一句话定位一行）★
                // 「等级影响」「功能」这两行在 1 级也画 ⇒ 行数恒定 ✓
                if (card.Length != 7)
                {
                    badWidth++;
                    if (firstSpecialBad.Length == 0) firstSpecialBad = $"{d.Id} 卡片正文 {card.Length} 行（应 7）";
                }
                // ★ v213：**「功能　」行必须真的存在、且不是占位** ★
                //   判据两条：① 有一行以「功能　」开头（`功能　—` 不算）
                //             ② 那一行里的定位词 ≠ `（未填写定位）`（= 新增种子忘了补 `PlantRoleText`）
                //   为什么要验：30 株的定位词是**手写表**，漏一株不会报错、只会让卡片少一句话 ✗
                {
                    string funcRow = null;
                    for (int r = 0; r < card.Length; r++)
                        if (card[r].StartsWith("功能　", System.StringComparison.Ordinal)) funcRow = card[r];
                    string role = ImGuiHud.PlantRoleText(d.Id);
                    if (funcRow == null || funcRow == "功能　—"
                        || role == ImGuiHud.PlantRoleUnfilled
                        || funcRow.IndexOf(role, System.StringComparison.Ordinal) < 0)
                    {
                        badFunc++;
                        if (firstSpecialBad.Length == 0)
                            firstSpecialBad = $"{d.Id} 缺「功能　」行或定位词未填（role=「{role}」）";
                    }
                }
                string last = card.Length > 0 ? card[card.Length - 1] : "";
                if (!last.StartsWith("特殊功能　", System.StringComparison.Ordinal))
                {
                    badSpecial++;
                    if (firstSpecialBad.Length == 0) firstSpecialBad = $"{d.Id} 末行不是特殊功能：「{last}」";
                }
                else if (hasMech)
                {
                    mechCards++;
                    string want = TableDatabase.MechShortDesc(d, slot.Level);
                    string full = PlantMechanics.Describe(d.Mechanic);
                    if (string.IsNullOrEmpty(want) || last.IndexOf(want, System.StringComparison.Ordinal) < 0)
                    {
                        badSpecial++;
                        if (firstSpecialBad.Length == 0)
                            firstSpecialBad = $"{d.Id} 特殊功能行与 MechShortDesc 不一致：「{last}」≠「{want}」";
                    }
                    // ⑤ 短版必须真的更短（"用短版"这件事必须在长度上成立）
                    // ★ v213：只在**确实存在短版**时判这一条 ★
                    //   判据：`MechShortDesc` 与 `PlantMechanics.Describe` **不是同一句** ⇒ 这一档写了短版 ✓
                    //   8 条新机制（凝露/省水/古木生长/耐旱/丰收/连锁收获/焦渴绽放/成长加速）在
                    //   `TableDatabase.MechShortDesc` 里**没有**短版 ⇒ 按设计退回长版（"宁可长，不可空"）
                    //   ⇒ 这时要求"短版比长版短"等于要求一件不存在的东西 ✗（不改 `Data/` 里的那个函数）
                    bool hasShort = !string.IsNullOrEmpty(want)
                                    && !string.Equals(want, full, System.StringComparison.Ordinal);
                    if (hasShort && want.Length >= full.Length)
                    {
                        badSpecial++;
                        if (firstSpecialBad.Length == 0)
                            firstSpecialBad = $"{d.Id} 短版 {want.Length} 字 ≥ 长版 {full.Length} 字（没变短）";
                    }
                }
                else
                {
                    plainCards++;
                    if (last != "特殊功能　无")
                    {
                        badSpecial++;
                        if (firstSpecialBad.Length == 0) firstSpecialBad = $"{d.Id} 无机制却写了「{last}」";
                    }
                }
                // ④ 宽版（商店商品行）不带这一行 —— 在下面那个循环里按 `PlantCellInfoLines(…, true)` 查
            }
            // ④ 宽版那条：`PlantCardLines` 只服务"种子卡 / 植物格"；商店商品行走的是
            //    `PlantCellInfoLines(tmp, true)`（2 行）⇒ 直接验那条路**不含**特殊功能行 ✓
            //    （读真货而不是"相信注释"：宽版视口只有 68px，多一行就顶出滚动条 ✓）
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                var slot = new SeedSlot
                { Def = TableDatabase.Seeds[i], Quality = TableDatabase.Seeds[i].RarityTier, Level = 1 };
                string[] wideRows = ImGuiHud.PlantCellInfoLines(slot, true);
                for (int r = 0; r < wideRows.Length; r++)
                    if (wideRows[r].StartsWith("特殊功能", System.StringComparison.Ordinal)) badWide++;
            }
            bool specialOk = badSpecial == 0 && badWidth == 0 && badWide == 0 && badFunc == 0
                             && mechCards == 14 && plainCards == 25;
            list.Add(new CheckResult(
                "v198：植物卡底部有「特殊功能」行 ＋ v213 的「功能」定位行（14 株机制植物写机制原文、25 株写「无」；正文恰好 7 行；商店宽版不带）",
                specialOk,
                specialOk
                    ? $"机制 {mechCards} 株写的是 `PlantMechanics.Describe` 原文｜无机制 {plainCards} 株写「无」"
                      + $"｜{TableDatabase.Seeds.Length} 株 × 卡片 7 行（含「功能」定位行，39 株全部有词）｜宽版 2 行不含这两行"
                    : $"特殊功能行不符 {badSpecial}｜行数不符 {badWidth}｜宽版混入 {badWide}｜「功能」行不符 {badFunc}"
                      + $"｜机制 {mechCards}/14 无机制 {plainCards}/25｜首例 {firstSpecialBad}"));

            // ★ v198：**道具详情的「数值栏」必须在「功能栏」之前** ★
            //
            // 用户原话："重新定义道具的数值展示栏，放在功能栏后，提前位置"。
            //
            // 判据（对**表里每一件道具**逐件验，不是抽一件）：
            //   ① 详情恰好 **2 行**（数值 ＋ 功能）——
            //      视口 68px 只放得下 3 行（44px 内容 ＋ 8 内边距）；改前那 5 行必然裁出半行字形
            //      ⇒ 只留"数字"与"一句话"，其余（类型/稀有度/价格）左列已经有了 ✓
            //   ② 第 1 行以「数值　」开头，第 2 行以「功能：」开头  ⇒ **顺序**就是这一条
            //   ③ 数值行里必须含 `StatName` 的属性名与 `MaxStackText` 的上限文案 ——
            //      证明它不是一句占位文字（"数值　—"这种会被抓出来）✓
            //   ④ 读的是 `ImGuiHud.ItemDetailLines` **真的会被画出去的那份字符串**（不是照着抄一遍）✓
            int detailBad = 0;
            string firstDetailBad = "";
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                if (it == null) continue;
                var offer = new ShopOffer
                { Kind = OfferKind.Item, Item = it, Quality = it.RarityTier, Price = it.Price };
                string[] rows = ImGuiHud.ItemDetailLines(offer);
                string why = null;
                if (rows.Length != 2) why = $"{it.Id} 详情 {rows.Length} 行（应 2）";
                else if (!rows[0].StartsWith("数值　", StringComparison.Ordinal)) why = $"{it.Id} 第 1 行不是数值栏：「{rows[0]}」";
                else if (!rows[1].StartsWith("功能：", StringComparison.Ordinal)) why = $"{it.Id} 第 2 行不是功能栏：「{rows[1]}」";
                else if (rows[0].IndexOf(ImGuiHud.MaxStackText(it.MaxStack), StringComparison.Ordinal) < 0)
                    why = $"{it.Id} 数值栏没写持有上限：「{rows[0]}」";
                if (why == null) continue;
                detailBad++;
                if (firstDetailBad.Length == 0) firstDetailBad = why;
            }
            list.Add(new CheckResult(
                "v198：道具详情的数值栏在功能栏**之前**（第 1 行数值 / 第 2 行功能，表内每件逐件验）",
                detailBad == 0,
                detailBad == 0
                    ? $"{TableDatabase.Items.Length} 件道具详情都是「数值 → 功能」两行，数值栏含属性与持有上限"
                    : $"顺序/内容不符 {detailBad} 件｜首例 {firstDetailBad}"));
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

            // ── 等级提升：最大生命 +10
            // ★ v192：期望值 1 → 10（= `EnemyDef.PlayerHpUnit`）★
            //   判据没变（"升级真的加生命、且量是表里那个数"）；换刻度是因为血池 ×10 --
            //   若这条还写 1，玩家升级只加 1 点血（1%），等于把升级奖励废掉 ✗
            Expect(list, "升级最大生命 +10（v192：= hpUnit，旧的 +1 × 10）",
                   t.growth.hpPerLevel, EnemyDef.PlayerHpUnit);

            // ── 道具约定：数值为整数 / 倍率为整数%（v168：**遍历全部 effects + costs**）
            //
            // 旧实现只查 `it.Add` —— 而 v168 起 `Add` 只是 `Effects[0]` 的派生值 ✗
            // ⇒ 只查第一条就等于**放过了减益列表与多效果道具** ✓
            // 而"表里写了 +15.5%"正是那种不报错、只是数字读不出来的错 ✓
            //
            // ★ v179：`CritRate` 走 `IsFractionStat` 豁免 —— **同一条判定，不另写一份** ★
            //   它的单位是 0..1 小数（0.06 = +6%），整数写法只有"0"和"100%"两档 ⇒
            //   强行整数化会让这条属性在道具表里根本写不出来（详见 `TableDatabase.IsFractionStat`）✓
            //   ⚠️ 这不是把约定放松：除了这一个属性，其余（含 CritDmg / 全部 Percent）仍是整数硬约束 ✓
            // ★ v3xx：判据换成 `AllowsFraction`（= `IsFractionStat` ∪ {MoveSpeed, AtkSpeed}）★
            //   `gamble_glove` 的两条 `Percent −9.9` 是用户点名的数值 ⇒ 若这里只认 `IsFractionStat`，
            //   表校验放过、自检判红（同一条约定出现两份结论）✗ ⇒ 两处必须读**同一个**函数 ✓
            bool allInt = true; string badItem = "";
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                foreach (ItemBonus b in it.Effects)
                    if (!TableDatabase.AllowsFraction(b.Stat) && Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { allInt = false; badItem = it.Id + "." + b.Stat; }
                foreach (ItemBonus b in it.Costs)
                    if (!TableDatabase.AllowsFraction(b.Stat) && Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { allInt = false; badItem = it.Id + "." + b.Stat + "(减益)"; }
            }
            list.Add(new CheckResult("所有道具的数值都是整数（含减益与多效果；CritRate 按 0..1 小数例外）", allInt, badItem));
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

            // ── 毒：命中即中毒，**3 次结算**（v190）
            //
            // ★ v190：期望值 4 → 3（用户要求「灼烧、中毒改为触发 3 次」）★
            //   `tuning.formula.ailmentTickCount` 4 → 3 ⇒ `StatusRuntime.Apply`（Combat.cs:484）
            //   给 `PoisonTicksLeft/BurnTicksLeft` 的初值就是 3 ⇒ 总时长 = 3 × tickInterval(1.0) = 3 秒 ✓
            //   ★ 为什么这里仍写**字面 3**、而不是读 `f.ailmentTickCount` ★
            //     读配置会退化成"自己等于自己"：谁把表改成 5，断言照样绿 ✗
            //     3 是**设计值**（用户原话），表漂移必须让它变红 ✓ —— 与上面 0.7 那条同一条纪律 ✓
            //   ★ 这条断言只钉"次数"，**不钉时长** —— 时长由次数 × `tickInterval` 派生；
            //     要单钉时长的是 `tickInterval`，那属于另一件事（本次未动）✓
            var st = new StatusRuntime();
            st.Apply(AilmentKind.Poison, 3f, f, 1f);
            list.Add(new CheckResult("毒：命中即中毒且共 3 次结算（v190：原 4 次）", st.PoisonTicksLeft == 3 && st.Poisoned, $"{st.PoisonTicksLeft} 次"));
            var st2 = new StatusRuntime();
            st2.Apply(AilmentKind.Burn, 3f, f, 1f);
            list.Add(new CheckResult("火：命中即灼烧且共 3 次结算（v190：原 4 次）", st2.BurnTicksLeft == 3 && st2.Burning, $"{st2.BurnTicksLeft} 次"));

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
            // ★ v192：**夹具的伤害量跟着血池走**（`4 × hpUnit` = 40，用户需求②的连带）★
            // 为什么要改夹具而不是改断言：改前 `ApplyDamage(4)` 会先被护盾吃掉 2 点、再把血打掉 2 点；
            // v192 护盾是 **20** ⇒ 4 点伤害**全被护盾吸收**、血一点没掉 ⇒
            // 后面"拾取爱心回血"必然读不到回血（`100.0 → 100.0`）✗ 那会让一条正确的规则假红 ✓
            // 断言本身一个字没改（仍是"回血后 > 回血前 + 1"），改的是**夹具把血真的打掉多少** ✓
            p.Health.ApplyDamage(4f * EnemyDef.PlayerHpUnit);
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
            // ★ v195：期望值 10 → **1**（道具三条伤害属性的 add 全部 ÷10，见 items.json 的
            //   `_note_v195_revert`）。仍然写**字面值**而不是 `atkItem.Add` —— 那条会退化成
            //   "自己等于自己"，表被误改也判不出来（与上面 0.455 那条同一条理由）✓
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
            // v179：`CritRate` 例外（0..1 小数），判定复用 `TableDatabase.IsFractionStat` ✓
            // v3xx：换 `AllowsFraction`（同上一条的理由：`gamble_glove` 的 Percent −9.9）✓
            bool intOk = true; string badId = "";
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                foreach (ItemBonus b in it.Effects)
                    if (!TableDatabase.AllowsFraction(b.Stat) && Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { intOk = false; badId = it.Id + "." + b.Stat; }
                foreach (ItemBonus b in it.Costs)
                    if (!TableDatabase.AllowsFraction(b.Stat) && Mathf.Abs(b.Add - Mathf.Round(b.Add)) > 0.0001f) { intOk = false; badId = it.Id + "." + b.Stat; }
            }
            list.Add(new CheckResult("所有道具数值仍满足整数/整数%约定（CritRate 按 0..1 小数例外）", intOk, badId));

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

            // ── 工具购买后自动合成（同档买两次 = 升一档）
            var ts = new RunSession();
            ts.Start(5150UL);
            ts.UpgradeTool(RunSession.ToolHoe, 0, out _);
            ts.UpgradeTool(RunSession.ToolHoe, 0, out _);
            list.Add(new CheckResult("武器购买后自动合成（升级）", ts.ToolLevel[RunSession.ToolHoe] == 1,
                $"锄头 = 档位 {ts.ToolLevel[RunSession.ToolHoe]}"));
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
            // ★ v179：夹具的收益基数必须显式给出 ★
            //   旧夹具只写 `Price = 100`，靠"每格金币 = price/5"推出 20 金 ✗
            //   收益与 price 脱钩后，不写 `GoldYield` 就是 0 ⇒ `CellGold` 退回保底 1
            //   ⇒ 这几条机制断言会变成"测的是保底值、而不是 ×1.30 / ×1.60 的倍率" ✗
            //   这里显式写 20 / 10（= 旧的 price/5 与 price/10）⇒ 夹具的**数值语义与改前逐位相同** ✓
            int unitGold = SeedEconomy.CellGold(radish);   // 1 级、无机制、无道具时的单格金币

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
            //
            // ★★ v179：这一组的三条『档位 ↔ 机制』断言已**删除** ✗ ★★
            //
            // 删掉的是：
            //   "普通/一般 = 纯数值差异（一个机制都没有）"     —— 仍成立，但已由 SEED-NO-MECH 整表覆盖
            //   "稀有/史诗 = 小机制（每件都带一条）"           —— **新设计直接违背它**
            //   "传奇 = 大机制（3 件全部带）"                  —— 同上，且 S 档现在是 2 株不是 3 株 ✗
            //   "PlantMechanics 登记的机制都被表用到"          —— 机制整体下线后必然红
            //
            // ★ 为什么删而不是改成"跳过"★
            // 这三条编码的是**旧的内容标准**（低品质纯数值 / 中品质小机制 / 高品质大机制）✗
            // 而 v179 的需求是**同档四象限按节奏与金验比分配**，机制本轮整体下线（`SEED-NO-MECH`）✓
            // 两套标准不能同时为真 ⇒ 留着它们只有两种结局：常红（噪音）或被注释掉（假绿）✗
            // ⇒ 明确删除，并把"档内该怎么分"的判据交给 `SEED-ARCHETYPE` + `SEED-TIER-RATE` ✓
            //
            // ★ `PlantMechanics.cs` 本身**一个字没删**（下一轮设计要用）★
            // 它仍然被"每条植物机制都有可读说明"那条断言盯着（那一组仍在跑）✓
            int badMech = 0; string badMechId = "";
            var mechUse = new Dictionary<string, int>();
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                bool has = !string.Equals(d.Mechanic, PlantMechanics.None, System.StringComparison.OrdinalIgnoreCase);
                if (!PlantMechanics.IsKnown(d.Mechanic)) { badMech++; badMechId = d.Id + ":" + d.Mechanic; }
                if (has) { mechUse.TryGetValue(d.Mechanic, out int c); mechUse[d.Mechanic] = c + 1; }
            }
            // ★ v197：16 → **22**（新增 6 株机制植物；断言不删，只改期望值）
            // ★ v202：22 → **30**（再追加 8 株；理由同 `TableChecks` 里那条的上方注释）
            // ★ v253：39 → **51**（表尾追加 12 株；断言不删，只改期望值）
            list.Add(new CheckResult("v168：植物仍是 51 个（37 无机制 + 14 机制 + 12 新株）",
                TableDatabase.Seeds.Length == 51, $"{TableDatabase.Seeds.Length}"));
            list.Add(new CheckResult("v168：表里的每个 mechanic 都有解释器（PlantMechanics）",
                badMech == 0, badMech == 0 ? $"{mechUse.Count} 种机制在用" : badMechId));
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

            // ── ④ WaterSave【成长·缺水】—— v179：断言退役（机制整体下线）──
            //
            // 原文是"省水机制在真表里有人用（苔藓盾 = WaterSave）"✗
            // 它把**某一条机制必须落在某一个具体品种上**当成不变式 —— 那是旧内容标准的一部分 ✓
            // v179 起 16 条全是 None（`SEED-NO-MECH`），这条必然红 ⇒ 退役 ✓
            // ★ 机制本身的行为验证（下面 ⑤~⑨ 的对照实验）**一条没删** ★
            //   它们用自造夹具种子跑 `PlantMechanics` 的真实分支 ⇒ 解释器还在被验 ✓
            //   等下一轮机制重新上线，只需把品种重新绑上，这些断言**不用改** ✓

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
            // ★ v195：期望值改成**走同一条乘法**（`× harvestGoldScale`）★
            //   本轮新增了 `farm.harvestGoldScale = 0.54`（"植物金币收获量调整"），
            //   若这里继续写裸的 20 / 26，断言就会在**无关的调整**上变红 ✗
            //   判据本身没变：`丰收 = 普通 × 1.30`（两个数都从同一个基数算出）✓
            {
                float gScale = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
                int plainGold = Mathf.Max(1, Mathf.RoundToInt(20 * gScale));
                int richGold = Mathf.Max(1, Mathf.RoundToInt(20 * PlantMechanics.RichHarvestMult * gScale));
                var rich = new SeedDef { Id = "t168_rich", GrowTime = 600f, Mechanic = PlantMechanics.RichHarvest, WaterDrain = 0, GoldYield = 20, ExpYield = 10 };
                var plain = new SeedDef { Id = "t168_rp", GrowTime = 600f, Mechanic = PlantMechanics.None, WaterDrain = 0, GoldYield = 20, ExpYield = 10 };
                var w1 = MakeWorld(out _); FarmPlot p1 = w1.Farm.Slot(0, 0);
                p1.BeginPlant(t.farm, rich, 1000, 0); p1.TickGrowth(999f); w1.HarvestPlot(p1);
                var w2 = MakeWorld(out _); FarmPlot p2 = w2.Farm.Slot(0, 0);
                p2.BeginPlant(t.farm, plain, 1000, 0); p2.TickGrowth(999f); w2.HarvestPlot(p2);
                list.Add(new CheckResult("v168：RichHarvest【收获】该格金币 ×1.30",
                    w2.GoldEarned == plainGold && w1.GoldEarned == richGold,
                    $"丰收 {w1.GoldEarned} 金 vs 普通 {w2.GoldEarned} 金（应 {richGold} / {plainGold}，含金币系数 {gScale:0.##}）"));
            }

            // ── ⑧ ChainHarvest【收获】：收一格 ⇒ 整列成熟格一并收获 ──
            // ★ v195：期望值同样乘上 `harvestGoldScale`（见上一条的说明）★
            {
                float gScale = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
                var w = MakeWorld(out _);
                var chain = new SeedDef { Id = "t168_chain", GrowTime = 600f, Mechanic = PlantMechanics.ChainHarvest, WaterDrain = 0, GoldYield = 20, ExpYield = 10 };
                w.Farm.Slot(0, 0).BeginPlant(t.farm, chain, 1000, 0);     // 列首种下 ⇒ 整列自动补齐
                int rows = w.Farm.Rows;
                for (int r = 0; r < rows; r++) w.Farm.Slot(0, r).TickGrowth(999f);
                int ripeBefore = 0;
                for (int r = 0; r < rows; r++) if (w.Farm.Slot(0, r).IsRipe) ripeBefore++;
                w.HarvestPlot(w.Farm.Slot(0, 0));                          // 只收一格
                int ripeAfter = 0;
                for (int r = 0; r < rows; r++) if (w.Farm.Slot(0, r).IsRipe) ripeAfter++;
                int expectChain = rows * Mathf.Max(1, Mathf.RoundToInt(20 * gScale));
                list.Add(new CheckResult("v168：ChainHarvest【收获】收割一格连带整列已成熟的格",
                    ripeBefore == rows && ripeAfter == 0 && w.GoldEarned == expectChain,
                    $"收割前成熟 {ripeBefore}/{rows} 格 → 收割后 {ripeAfter} 格；金币 {w.GoldEarned}（应 {expectChain}）"));
            }

            // ── ⑨ ThirstBloom【水分<50%·收获】：水分低于一半就更值钱 ──
            // ★ v195：期望值同样乘上 `harvestGoldScale`（见⑦的说明）★
            // ★ v219：判据从"缺水（`Water <= 0`）"改成"**水分比例 < 50%**"（用户原话
            //   "不是缺水，而是水分低于 50"）★
            //   所以这里钉**四档水位**：底（0%）/ 见底前（40%）/ 正好一半（50%）/ 满水（100%）。
            //   其中 **40% 这一档是旧实现必红、新实现必绿**的那一格 —— 没有它，本条只是
            //   "把断言起个新名字"，证明不了"低于一半就给"这件事真的生效 ✓
            //   ⚠️ 50% 那一档钉的是**严格小于**：正好一半算"有水"，不给加成 ✓
            {
                float gScale = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
                int wetGold = Mathf.Max(1, Mathf.RoundToInt(20 * gScale));
                int dryGold = Mathf.Max(1, Mathf.RoundToInt(20 * PlantMechanics.ThirstBloomMult * gScale));
                var bloom = new SeedDef { Id = "t168_bloom", GrowTime = 600f, Mechanic = PlantMechanics.ThirstBloom, WaterDrain = 0, GoldYield = 20, ExpYield = 10 };

                var wd = MakeWorld(out _); FarmPlot dry = wd.Farm.Slot(0, 0);
                dry.BeginPlant(t.farm, bloom, 1000, 0); dry.TickGrowth(999f); dry.Water = 0; dry.NeedFullRefill = true;
                wd.HarvestPlot(dry);

                var wl = MakeWorld(out _); FarmPlot low = wl.Farm.Slot(0, 0);
                low.BeginPlant(t.farm, bloom, 1000, 0); low.TickGrowth(999f); low.Water = 400;   // 40%：没见底，但低于一半
                wl.HarvestPlot(low);

                var wh = MakeWorld(out _); FarmPlot half = wh.Farm.Slot(0, 0);
                half.BeginPlant(t.farm, bloom, 1000, 0); half.TickGrowth(999f); half.Water = 500; // 正好 50%：不算渴
                wh.HarvestPlot(half);

                var ww = MakeWorld(out _); FarmPlot wet = ww.Farm.Slot(0, 0);
                wet.BeginPlant(t.farm, bloom, 1000, 0); wet.TickGrowth(999f);
                ww.HarvestPlot(wet);

                list.Add(new CheckResult("v168：ThirstBloom【水分<50%·收获】水分低于一半时该格金币 ×1.60（正好一半不给）",
                    wd.GoldEarned == dryGold && wl.GoldEarned == dryGold
                    && wh.GoldEarned == wetGold && ww.GoldEarned == wetGold,
                    $"0% → {wd.GoldEarned} 金｜40% → {wl.GoldEarned} 金｜50% → {wh.GoldEarned} 金｜满水 → {ww.GoldEarned} 金"
                    + $"（应 {dryGold}／{dryGold}／{wetGold}／{wetGold}）"));
            }

            // ═════════════════ 三、合成等级 1/2/3：只做数值提升，作用在成长与收获 ═════════════════

            int wm1 = SeedSlot.WaterMaxFor(radish, 1), wm2 = SeedSlot.WaterMaxFor(radish, 2), wm3 = SeedSlot.WaterMaxFor(radish, 3);
            list.Add(new CheckResult("v168：等级蓄水上限 100／140／180（每级 +40，与品种无关）",
                wm1 == 100 && wm2 == 140 && wm3 == 180, $"{wm1}／{wm2}／{wm3}"));
            // ★ v176：★ 本条改的是【被断言的数据】，不是【断言变松】★
            //
            // 症状：v176 之前这里写 `毒蘑菇 = 80／120／160`，因为旧表给毒蘑菇 `waterMax = 80`
            //   （相对基准 100 偏 −20）。v176 把 **全部 16 个品种的 waterMax 统一成 100** ✗
            //   ⇒ `SeedSlot.WaterMaxFor(tofu, n)` 变成 100／140／180，旧式必然红 ✓
            //
            // ★ 为什么统一 100 是对的（而不是"为了过自检把断言改松"）★
            //   用户 v176 的标准是**公平**：同品质内不许出现"同样东西成熟更快"的支配关系 ✗
            //   而**水箱大小是第二条暗面支配**：旧表里 `waterDrain` 与 `waterMax` 各品种不同 ✗
            //   ⇒ 同一品质内"费水的品种"与"省水的品种"在**浇水次数**上并不等值 ✓
            //   统一 waterMax = 100（= `farm.waterMaxDefault`）⇒ 品种偏差 = 0
            //   ⇒ 等级轴仍是 +40／级（100／140／180，与品种无关），而**品种之间不再有水箱差** ✓
            //   剩下唯一的品种级水差异是 `waterDrain`，它已按品质锁定（同品质内唯一）✓
            //
            // ⚠️ 被保留的语义：`WaterMaxFor` 的"等级轴 + 品种偏差"结构**一个字没改** ✗
            //   本条只是不再依赖"某个品种恰好偏 −20"这个**易碎的数据巧合** ✓
            //   （旧写法把断言绑死在毒蘑菇的 waterMax 上 —— 谁调一下毒蘑菇的水箱，这里就红 ✓）
            //
            // ★ 现在断言的是 v176 真正要守住的那条公平不变量 ★：
            //   ① 全部品种的 waterMax 都等于 `farm.waterMaxDefault`（品种偏差为 0）✓
            //   ② 于是**任意品种**在任意等级的蓄水上限都相同（100／140／180）✓
            //   ⇒ ② 比旧写法更强：旧式只钉一个品种，新式钉**全表** ✓
            SeedDef tofu = TableDatabase.GetSeed("toxic_mushroom");
            int baseWater = Mathf.RoundToInt(t.farm.waterMaxDefault);
            int drift = 0; string driftId = "";
            for (int i = 0; i < TableDatabase.Seeds.Length; i++)
            {
                SeedDef d = TableDatabase.Seeds[i];
                if (d.WaterMax != baseWater) { drift++; driftId = d.Id + "=" + d.WaterMax; }
            }
            list.Add(new CheckResult("v176：全表品种水箱偏差为 0（waterMax = 基准 100），水箱不再是暗面优势",
                drift == 0, drift == 0 ? $"{TableDatabase.Seeds.Length} 个品种全部 {baseWater}" : $"偏差 {drift} 个：{driftId}"));
            list.Add(new CheckResult("v176：等级蓄水上限对**任意品种**都相同（100／140／180）",
                SeedSlot.WaterMaxFor(tofu, 1) == 100 && SeedSlot.WaterMaxFor(tofu, 2) == 140 && SeedSlot.WaterMaxFor(tofu, 3) == 180
                && SeedSlot.WaterMaxFor(radish, 1) == SeedSlot.WaterMaxFor(tofu, 1)
                && SeedSlot.WaterMaxFor(radish, 2) == SeedSlot.WaterMaxFor(tofu, 2)
                && SeedSlot.WaterMaxFor(radish, 3) == SeedSlot.WaterMaxFor(tofu, 3),
                $"{SeedSlot.WaterMaxFor(tofu, 1)}／{SeedSlot.WaterMaxFor(tofu, 2)}／{SeedSlot.WaterMaxFor(tofu, 3)}"));

            // ═════════════════ ★ v179：v176 那三条"同档全等"断言已退役 ★ ═════════════════
            //
            // ★ 退役的是这三条（它们编码的判据已被新需求取代）★
            //   ① "同品质 + 同金币 + 同经验 ⇒ 成熟时间必须相同"  ✗
            //   ② "同品质内 growTime 与 price 各自唯一"          ✗（growTime 那半）
            //   ③ "同品质内 金币/秒 与 经验/秒 落在同一档"        ✗
            //
            // ★ 为什么必须退役，而不是"数据没守住"★
            // ① 的前提是"每格金币恒为 price/5、每格经验恒为 price/10"✗
            // 那条纪律的**目的是**"同档内没有支配关系"，而 v176 用了一个**结构性手段**去达成它：
            // 让同档所有品种的三元组(金币,经验,周期)完全相同 ⇒ 档内只有一种植物，玩家没得选 ✓
            // v179 的需求恰恰相反：**同档要有四种不同的取舍**（金型/验型/重型/快型）✗
            // ⇒ "三元组相同"从**优点**变成了**缺陷**（档内出现废品种）✓
            // ⇒ 而"没有支配关系"这个**目的**由新断言接管，且判据更强：
            //      `SEED-TIER-RATE`   同档 `(g + 2e)/t` 差 ≤ 2%   —— 总价值相等
            //      `SEED-ARCHETYPE`   同档类型齐备 + 三元组不重复 —— 但各有取舍
            //   ★ 两条一起才等价于旧版想保的东西：**档内只能挑玩法，不能挑强弱** ✓
            //
            // ★ 仍然留下的那一条（它没有过期）★
            //   同一品质内 **price 唯一** —— 售价是"档位"的一部分（D 20 / C 30 / B 40 / A 60 / S 80），
            //   商店按档定价，档内出现两个价 = 同一档位在货架上两种价格 ✓
            {
                int dupPrice = 0; string dupWhere = "";
                for (int g = 0; g <= 4; g++)
                {
                    var ofGrade = TableDatabase.SeedsOfGrade((SeedGrade)g);
                    if (ofGrade == null || ofGrade.Length == 0) continue;
                    int p0 = ofGrade[0].Price;
                    for (int k = 1; k < ofGrade.Length; k++)
                        if (ofGrade[k].Price != p0) { dupPrice++; dupWhere = $"{ofGrade[k].Id} price {ofGrade[k].Price} vs {p0}"; }
                }
                list.Add(new CheckResult("v179：同品质内售价唯一（售价是档位的一部分：D20/C30/B40/A60/S80）",
                    dupPrice == 0, dupPrice == 0 ? "五个品质档内部同价" : $"不唯一：{dupWhere}"));
            }

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
                // ★ v195：两条期望值都乘上 `harvestGoldScale`（本轮新增的金币全局系数）★
                //   判据仍然是"等级倍率真的被乘上、且按**同一条式子**取整" ——
                //   若这里继续用 `round(lv1 × 1.70)`（先取整再乘）就会与小数字刻度下的
                //   真实现（先乘再取整）差 1 点 ✗ 那不是 bug，是取整顺序 ✓
                float gScale3 = t.farm.harvestGoldScale > 0f ? t.farm.harvestGoldScale : 1f;
                int lv1Gold = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(radish) * gScale3));
                int lv3Gold = Mathf.Max(1, Mathf.RoundToInt(SeedEconomy.CellGold(radish) * 1.70f * gScale3));
                list.Add(new CheckResult("v168：收获金币真的随等级放大（1 级 goldYield ⇒ 3 级 ×1.70）",
                    wg1.GoldEarned == lv1Gold && wg3.GoldEarned == lv3Gold,
                    $"1 级 {wg1.GoldEarned} 金 ／ 3 级 {wg3.GoldEarned} 金（应 {lv1Gold} → {lv3Gold}，金币系数 {gScale3:0.##}）"));
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

            // ═════════════════ 四、道具：两类别齐备且自洽（v179）═════════════════
            //
            // ★ v179：这一组的四条断言按新设计重写/退役 ★
            //   改动 ① "三类别齐备（纯数值／有牺牲／机制拓展）"
            //        → "两类别齐备，件数 = 6 纯增益 + 18 有增有减 + 0 机制类"
            //          机制类道具整体下线（ITEM-NO-RULE），仍要求 cat3 > 0 就是要求一件不存在的东西 ✗
            //   改动 ② "类别 3 的 rule 都有解释器" → **删除**（没有类别 3 ⇒ 恒真的空断言）
            //   改动 ③ "ItemRules 登记的每条规则都被表用到" → **删除**
            //          `ItemRules` 是**解释器**，保留给下一轮；本轮表里没人引用它是有意的 ✓
            //   改动 ④ "类别 1 有『全属性小幅度增加』的传奇特例（一条 ≥3 个属性）"
            //        → "6 件入门全是**单属性**纯增益"（新规则：category 1 = 入门件，一条一属性）
            //          那条旧断言要求 cat1 里存在一条 ≥3 属性的"传奇特例"✗
            //          而 v179 把传家宝（全属性）改成了 **category 2**（它现在带代价）⇒ 旧断言必然红 ✓
            int cat1 = 0, cat2 = 0, cat3 = 0, noEffect = 0, costBad = 0, growItems = 0;
            // ★ v191：v179 那条『category 1 一律单属性』**已退役** ★
            //   用户这一轮点名要"纯增·多属性/高价值"这一类 ✗ 而"一条一属性"这条限制
            //   会把这一类逼进 category 2（硬塞一个假代价）⇒ 那才是撒谎 ✓
            //   替代它的是一条**档位**判据（下面那条断言）：多属性件必须落在比所有单属性件
            //   更高的档 ⇒ "入场价买不到两条属性"，同档内也不会出现"同样价钱买两条" ✓
            int cat1Multi = 0, cat1Cost = 0, maxSingleTier = -1, minMultiTier = 99;
            string firstBad = "";
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                if (it.Category == 1) cat1++;
                else if (it.Category == 2) cat2++;
                else if (it.Category == 3) cat3++;

                if (it.Effects.Length == 0 && !it.HasRule) { noEffect++; firstBad = it.Id; }
                if (it.Category == 1)
                {
                    if (it.Costs.Length > 0) { cat1Cost++; firstBad = it.Id + "（类别 1 却带了代价）"; }
                    int tier = (int)it.Grade;
                    if (it.Effects.Length >= 2)
                    {
                        cat1Multi++;
                        if (tier < minMultiTier) minMultiTier = tier;
                    }
                    else if (tier > maxSingleTier) maxSingleTier = tier;
                }
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
                if (it.GrowPerWave > 0f) growItems++;
            }
            // ★ v197：20/26/0（共 46）→ 20/27/9（共 56）★
            //   新增 10 件机制道具：9 件落 category 3（机制拓展），
            //   1 件（`evade_helm` 闪避）落 category 2 —— 因为它有**真代价**（血 −3 / 盾 −2）
            //   与**真增益**（闪避点数），两条都写在表里 ⇒ 按现有定义它就是"有增有减" ✓
            //   断言没删，只是把期望值改到新的构成上 ✓
            // ★★ v212（父任务第 3 条：30 件新道具）：56 → **86** ★★
            //   构成：纯增益 20 → **36**（+16）、有增有减 27 → **41**（+14）、机制 9 → **9**（+0）
            //   为什么新增的 30 件里**一件都不落 category 3**：
            //     category 3 = 带 `rule` 的机制件，而新机制要新解释器 + 新消费点
            //     （`Data/Defs.cs` 的 `ItemRules`、`Flow/RunSession.cs`、`Prog/**`）——
            //     本轮明确**不改战斗代码** ⇒ 30 件全部落在既有 16 条属性上（category 1 / 2）✓
            //   期望值跟着表走，判据本身一个字没动（仍是"三类齐备 + 各条内部一致"）✓
            // ★★ v252：**去重 −7、主城货品进战斗 +4、新增 8 件 + ...** ⇒ 86 → **91**
            //   （纯增益 31 / 有增有减 51 / 机制 9）；判据本身一个字没改 ✓
            // ── ★ v253：前期高品质抑制（用户原话：「降低前期高品质物品刷新率」）──
            //   读 `ShopSystem.TierWeightsAt`（**与掷骰同一个算式**），不在这里另写一份公式 ✓
            {
                var dq = new RunSession();
                dq.Start(0xD1FF1CUL);
                float[] w1 = ShopSystem.TierWeightsAt(dq, 1);
                float[] w5 = ShopSystem.TierWeightsAt(dq, 5);
                float[] w10 = ShopSystem.TierWeightsAt(dq, 10);
                float hi1 = (w1.Length > 4 ? w1[3] + w1[4] : 0f) * 100f;
                float hi5 = (w5.Length > 4 ? w5[3] + w5[4] : 0f) * 100f;
                float hi10 = (w10.Length > 4 ? w10[3] + w10[4] : 0f) * 100f;
                bool ok = w1.Length > 4 && hi1 < 3f && hi1 < hi5 && hi5 < hi10 && hi10 >= 5f;
                list.Add(new CheckResult("v253：前期高品质（A+S）刷新率被压低且逐波回升（第 1 波 < 3%、第 10 波恢复）",
                    ok,
                    $"A+S 占比：第 1 波 {hi1:0.00}%｜第 5 波 {hi5:0.00}%｜第 10 波 {hi10:0.00}%"
                    + $"（rampWaves={dq.T.shop.highTierRampWaves} floor={dq.T.shop.highTierEarlyFloor:0.00}）"));
            }

            list.Add(new CheckResult("v252：道具三类别齐备（纯增益 31 件／有增有减 61 件／机制类 9 件，共 101 件）",
                cat1 == 31 && cat2 == 61 && cat3 == 9,
                $"纯增益 {cat1} 件／有增有减 {cat2} 件／机制拓展 {cat3} 件（应 31／61／9），共 {TableDatabase.Items.Length} 件"));
            list.Add(new CheckResult("v168：没有『买了没反应』的道具（有 effects 或 有可解释的 rule）",
                noEffect == 0, noEffect == 0 ? $"{TableDatabase.Items.Length} 件全部有效" : firstBad));
            list.Add(new CheckResult("v168：类别 2 的增益与减益两半齐全，且同属性上增益严格大于减益",
                costBad == 0, costBad == 0 ? $"{cat2} 件全部满足" : firstBad));
            list.Add(new CheckResult("v191：category 1 一律无代价，且『多属性』件必须跨档（入场价买不到两条属性）",
                cat1Cost == 0 && cat1Multi == 4 && minMultiTier > maxSingleTier,
                cat1Cost > 0 ? firstBad
                    : $"cat1 带代价 {cat1Cost} 件｜多属性 {cat1Multi} 件最低档 {(SeedGrade)minMultiTier}"
                      + $" ＞ 单属性最高档 {(SeedGrade)maxSingleTier}"));
            list.Add(new CheckResult("v168：类别 2 有『加入成长』的道具（growPerWave > 0）",
                growItems == 2, $"{growItems} 件（应 2 件长线道具）"));

            // ═════════════════ 五、道具机制的**可观测行为** —— v179：三条全部退役 ═════════════════
            //
            // 退役的是：`reroll_discount` / `harvest_gold_pct` / `harvest_dew` 三条规则的行为断言 ✗
            // 它们各自依赖一件**已被删除的机制类道具**（haggle_charm / golden_sickle / dew_flask）：
            //   `TableDatabase.GetItem("haggle_charm")` 现在返回 **null**（不抛异常 ✓）
            //   ⇒ `ApplyItem(null)` 直接返回 ⇒ 断言会以"倍率没变"的形式红 ✓
            //
            // ★ 但**消费点没有一起删** ★（这一点必须写清楚，否则下一个人以为整条链没了）
            //   `RunSession.SyncItemRules` 仍在每回合把三样东西写进消费点：
            //     `Shop.RerollDiscount` / `World.HarvestGoldMult` / `World.HarvestDew`
            //   下面第七段"没买道具时机制系数是干净的默认值"仍在验它们**恒为默认值** ✓
            //   ⇒ 下一轮把机制类道具加回来时，**只需加表行**，这三个消费点和这条断言都不用重写 ✓
            //
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
                // ★ v195：期望值 50 → **5**（蜡封符的 `add` 与 `growPerWave` 都 ÷10，回到个位刻度）★
                //   第 5 回合：(1 + 1×4) = 5 ⇒ 相对第 1 回合的 1 正好 +4 ✓
                list.Add(new CheckResult("v168：『加入成长』真的随回合变强（蜡封符 第 1 回合 +1 ⇒ 第 5 回合 +5）",
                    Mathf.Abs(m5 - m0 - 5f) < 0.001f, $"近战 {m0:0.##} → 第1回合 {m1:0.##} → 第5回合 {m5:0.##}"));

                float hp0 = s.Progress.Stats.Get(StatId.MaxHp);
                s.ApplyItem(TableDatabase.GetItem("glass_cannon"));      // 近战 +30／最大生命 −10（v194）
                float hp1 = s.Progress.Stats.Get(StatId.MaxHp);
                float m6 = s.Progress.Stats.Get(StatId.MeleeDamage);
                // ★ v192：期望值 1 → `EnemyDef.PlayerHpUnit`(10) ★
                //   判据没变（"减益真的写进了属性"）；换刻度是因为血池 ×10 ⇒
                //   items.json 里 glass_cannon 的 `MaxHp add 1 → 10`（否则 −1 只值 1% 血，代价消失）✓
                list.Add(new CheckResult("v168：类别 2 的**减益**真的写进了属性（玻璃大炮：最大生命 −10 = 1 点旧血）",
                    Mathf.Abs(hp1 - (hp0 - EnemyDef.PlayerHpUnit)) < 0.001f, $"最大生命 {hp0:0.##} → {hp1:0.##}"));
                // ★ v194：期望值 3 → **30**（近战伤害的 add ×10）★
                // ★ v195：期望值 30 → **3**（玻璃大炮的近战 add ÷10）★
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

        // ════════════════════════════════════════════════════════════════════════════
        // ★ v179：数据表护栏（六条，全部是【跨行比较】，不是逐行校验）★
        //
        // ★ 为什么必须存在（"读表就能看出来"是错觉）★
        // 表是人写的，而"更便宜还更强"这类错误在 JSON 里**完全合法**：
        // 两个数都是正整数、字段名都对、格式也对 ✗ —— 只有把**同一档的全部成员放在一起比**
        // 才会暴露 ✓ 逐行校验（必填 / 范围 / 整数 / 同属性增益>减益）在 `TableDatabase.Validate`，
        // 这里只放**跨行不变式** ✓
        //
        // ★ 名字是核验锚点 ★：`SEED-ID-STABLE` / `SEED-NO-MECH` / `SEED-ARCHETYPE` /
        // `SEED-TIER-RATE` / `ITEM-COVERAGE` / `ITEM-NO-RULE` /
        // `PLUGIN-NO-STAT` / `PLUGIN-SLOT` —— 改名 = 上游 grep 不到 = 等于没做 ✓
        // ════════════════════════════════════════════════════════════════════════════
        private static void TableGuardChecks(List<CheckResult> list)
        {
            SeedDef[] seeds = TableDatabase.Seeds;

            // ───────────────────────── SEED-ID-STABLE ─────────────────────────
            // id 是**跨系统主键**：存档 key、FarmPlot.SeedId/CropSeedId、模型 Art/Plants/<id>.fbx、
            // 图标 icon_plant_<id>.png、TableDatabase.GetSeed(id)、自检夹具与 DebugApi 命令
            // ⇒ 改 id 会让老存档查不到定义、模型与图标全部丢引用 ✗
            // 所以这里钉的是**集合逐一相等**（顺序不参与判定 —— 表行顺序不是主键的一部分）
            string[] expectIds =
            {
                "radish_guard", "pea_shooter", "toxic_mushroom", "frost_ivy",
                "thorn_pumpkin", "stone_fist", "chili_mage", "starvine",
                "moontree", "moss_shield", "healing_clover",
                "life_spring", "char_flower", "holy_tree_guard",
                "plague_mother", "ironroot_colossus",
                // ★ v197：6 株机制植物（新增；16 → 22，断言不删、只扩清单）
                "warblade_bloom", "gale_vine", "sunflower_crown", "rainlily",
                "shelter_ivy", "lucky_monarch",
                // ★ v202：8 株 A/S 机制植物（新增；22 → 30，同样**断言不删、只扩清单**）
                //   ★ 一条都没放宽 ★ `seeds.Length != expectIds.Length` 与"表里多出清单外的 id"
                //   两条判据读的都是 `expectIds.Length` ⇒ 清单短一个、表里多一株，都立刻红 ✓
                "dusk_berry", "tide_gourd", "hollow_vine", "dune_cactus",
                "harvest_bell", "star_ladder", "elder_oak", "quick_reed",
                // ★ v252：6 株**无机制**新植物（30 → 36）。它们不进 `mechSeedIds`（机制 None），
                //   只进这份"全表 id 白名单" —— 判据一个字没放宽（多一株/少一株都立刻红）✓
                "mint_sprout", "cinder_pepper", "glass_melon", "frost_reed",
                // ★ v252 第三批：5 株无机制新植物（34 → 39）
                "glow_moss", "salt_melon", "cloud_orchid", "mirror_bloom", "ember_reed",
                // ★ v253：12 株新植物（39 → 51）。它们**全部带机制** ⇒ 同时进 `mechSeedIds`，
                //   判据一个字没放宽（少一株、多一株、机制名写错都立刻红）✓
                "dew_jar_moss", "drizzle_bud", "rush_fern", "plump_gourd", "patch_lattice",
                "drought_lotus", "brim_peony", "brave_sprout", "aged_bloom", "chain_bloom",
                "nightdew_lily", "hunter_bloom"
            };
            string idBad = "";
            for (int i = 0; i < expectIds.Length && idBad.Length == 0; i++)
                if (!TableDatabase.HasSeed(expectIds[i])) idBad = $"清单里的 id 在表里找不到：{expectIds[i]}";
            if (idBad.Length == 0 && seeds.Length != expectIds.Length)
                idBad = $"表里 {seeds.Length} 条，应为 {expectIds.Length} 条";
            for (int i = 0; i < seeds.Length && idBad.Length == 0; i++)
            {
                bool known = false;
                for (int k = 0; k < expectIds.Length; k++)
                    if (string.Equals(expectIds[k], seeds[i].Id, StringComparison.Ordinal)) { known = true; break; }
                if (!known) idBad = $"表里多出清单外的 id：{seeds[i].Id}";
            }
            list.Add(new CheckResult("SEED-ID-STABLE：51 个 id 与清单逐一相等（id 是跨系统主键：存档/模型/图标都按 id 找）",
                idBad.Length == 0,
                idBad.Length == 0 ? $"51/51 与清单一致（{seeds.Length} 条，无多无缺）" : idBad));

            // ───────────────────────── SEED-NO-MECH ─────────────────────────
            // ★★ v197：判据**换了半边**（原名保留 —— 它是上游 grep 的锚点）★★
            //   旧判据：全部 16 条 mechanic 都是 None（那一轮把机制整体下线）✗
            //   新判据（用户本轮需求：6 株机制植物）：
            //     ① **v179 那 16 条**仍然全部 None（老品种没有被顺手改成机制植物）✓
            //     ② **v197 那 6 条**各有且仅有一条机制，且都在 `PlantMechanics` 登记过 ✓
            //     ③ 全表没有第三种状态（既不是 None、也不在登记表里）✓
            //   ⇒ 断言**没有删除**，只是把"机制不许存在"改成"机制只许长在这 6 株上" ✓
            // ★ 用**显式 id 清单**判"哪 6 株是新机制植物"，不用"末尾 6 条"这种位置假设 ★
            //   位置不是契约（表行顺序会变），而 id 是（见上面 SEED-ID-STABLE 的理由）✓
            // ★ v202：清单 6 → **14**，并且**改名** `v197SeedIds` → `mechSeedIds` ★
            //   改名的理由：v197 那批是 6 株（S 四 + A 二），本轮再追加 8 株（A 四 + S 四），
            //   这个数组已经不是"v197 那一批"了；留着旧名会让后来人以为它是版本锚点，
            //   而它其实是"**全表允许带机制的 id 白名单**" ✓
            //   ★ 成员怎么增的：v197 的 6 个 + v202 的 8 个，**逐个点名**，不按位置切 ✓
            //     （位置不是契约：`seeds.json` 的行顺序会变，id 才是契约 ✓）
            //   ★ 判据一个字没放宽 ★ 还是"非白名单的株必须是 None"（`mechLeft == 0`）、
            //     "白名单的株必须有且只有一条已登记机制"（`mechBad == 0`）、
            //     "白名单逐株都数到"（`mechNew == mechSeedIds.Length`）三条 ✓
            string[] mechSeedIds =
            {
                "warblade_bloom", "gale_vine", "sunflower_crown", "rainlily",
                "shelter_ivy", "lucky_monarch",
                // ★ v202：8 株 A/S 机制植物（机制全部取自 `PlantMechanics.All` 里已有唯一消费点的条目）
                "dusk_berry", "tide_gourd", "hollow_vine", "dune_cactus",
                "harvest_bell", "star_ladder", "elder_oak", "quick_reed",
                // ★ v253：12 株新植物全部带机制（6 复用 + 6 新增），逐个点名进白名单 ✓
                //   复用的 6：dew_jar_moss(WaterSave) / drizzle_bud(DewDrop) / rush_fern(Haste) /
                //             plump_gourd(RichHarvest) / drought_lotus(ThirstTolerant) / aged_bloom(ElderGrowth)
                //   新增的 6：patch_lattice / brim_peony / brave_sprout / chain_bloom /
                //             nightdew_lily / hunter_bloom
                "dew_jar_moss", "drizzle_bud", "rush_fern", "plump_gourd",
                "drought_lotus", "aged_bloom",
                "patch_lattice", "brim_peony", "brave_sprout", "chain_bloom",
                "nightdew_lily", "hunter_bloom"
            };
            int mechLeft = 0, mechNew = 0, mechBad = 0;
            var mechMsg = new StringBuilder();
            for (int i = 0; i < seeds.Length; i++)
            {
                bool isNew = false;
                for (int k = 0; k < mechSeedIds.Length && !isNew; k++)
                    if (string.Equals(mechSeedIds[k], seeds[i].Id, StringComparison.Ordinal)) isNew = true;
                bool hasMech = !string.Equals(seeds[i].Mechanic, PlantMechanics.None, StringComparison.OrdinalIgnoreCase);
                bool known = PlantMechanics.IsKnown(seeds[i].Mechanic);
                if (!isNew && hasMech) { mechLeft++; if (mechMsg.Length > 0) mechMsg.Append('、'); mechMsg.Append(seeds[i].Id + "=" + seeds[i].Mechanic); }
                if (isNew && hasMech && known) mechNew++;
                if (isNew && (!hasMech || !known)) { mechBad++; if (mechMsg.Length > 0) mechMsg.Append('、'); mechMsg.Append(seeds[i].Id + "=" + (seeds[i].Mechanic ?? "null")); }
            }
            // ★ v202：判据**换了分母**（6 → 14 株），断言本身**一条没删、也没放宽** ✓
            //   还多钉了一处：`seeds.Length == 30` —— 表里少一株、多一株都红（与 `SEED-ID-STABLE` 同源）
            list.Add(new CheckResult("SEED-NO-MECH：v179 的 25 条老品种仍全是 None；14 株机制植物各带一条已登记的机制（机制只许长在这 14 株上）",
                mechLeft == 0 && mechBad == 0 && mechNew == mechSeedIds.Length && seeds.Length == 51,
                mechLeft > 0 ? $"{mechLeft} 条老品种仍带机制：{mechMsg}"
                             : (mechBad > 0 ? $"新株机制不合法：{mechMsg}"
                                            : $"老品种 25/25 为 None｜新株 {mechNew}/{mechSeedIds.Length} 带已登记机制（共 {seeds.Length} 条）")));

            // ───────────────────────── SEED-ARCHETYPE ─────────────────────────
            // 同档四象限：金型（金>验）/ 验型（金<验）/ 重型（周期 ≥1.5×基准）/ 快型（周期 ≤0.7×基准）
            // ★ 档位基准周期来自策划案《档位预算》表（D16 / C27 / B48 / A85 / S148）★
            //   判据用 1.5× 与 0.7× 这两个**宽裕界**（真实值是 2.0× 与 0.5×）：
            //   基准本身小幅调整不会误伤，而"忘了给重型/快型改周期"必红 ✓
            // ★ 要求 = min(4, 该档株数) 种 ★（S 只有 2 株 ⇒ 2 种即可；B/A 三株 ⇒ 3 种；D/C 四株 ⇒ 4 种）
            // ★ 三元组不得重复 ★：同档两条 (金币, 经验, 周期) 完全相同 ⇒ 其中一条是**废品种**
            //   （玩家永远选另一条）—— 旧表 16 条正是这样：同档四株数值一模一样，档内没有选择 ✓
            //
            // ★★ v200：档位基准周期 = 每档**最快那一株**的成熟时间（与 `PlantMechanics.SeedTierBaseGrow` 同值）★★
            //   v179 `{ 16, 27, 48, 85, 148 }` ⇒ v199 `{ 8, 27, 48, 42.5, 37 }` ⇒ v200 `{ 8, 27, 48, 42.5, 37 }`
            //   ★ v200 把 B / A / S 三档的周期**重新拉开**（v199 那三档各自被钳成同一值）：
            //       B 40/48/56（基准 48，重 56 不 ≥72、快 40 不 ≤33.6 ⇒ 周期轴仍判"均一"⇒ 要 2 型）
            //       A 34/42.5/42.5/42.5/50（基准 42.5，50 < 63.75 ⇒ 均一 ⇒ 2 型）
            //       S 30/37/37/37/44/44（基准 37，44 < 55.5 ⇒ 均一 ⇒ 2 型）
            //   而 D（5/8/8/16，速度基 8 ⇒ 5 ≤ 5.6 有快型、16 ≥ 12 有重型）与
            //   C（13.5/27/27/54 ⇒ 13.5 ≤ 18.9、54 ≥ 40.5）**周期轴仍有差异** ⇒ 仍要 4 型 ✓
            //   ★ 改的是"基准是多少"，不是"要不要判" —— 重型/快型两个象限照旧判 ✓
            //   ⚠️ 这张表是**判据的输入**，不是从 `seeds.json` 推出来的：表被改乱时它会红 ✓
            float[] tierBaseGrow = { 8f, 27f, 48f, 42.5f, 37f };
            int archetypeBad = 0;
            var archMsg = new StringBuilder();
            var archSummary = new StringBuilder();
            for (int g = 0; g <= 4; g++)
            {
                SeedDef[] of = TableDatabase.SeedsOfGrade((SeedGrade)g);
                if (of == null || of.Length == 0) continue;
                float baseGrow = tierBaseGrow[g];
                bool isGold = false, isExp = false, isHeavy = false, isFast = false;
                for (int k = 0; k < of.Length; k++)
                {
                    if (of[k].GoldYield > of[k].ExpYield) isGold = true;
                    if (of[k].GoldYield < of[k].ExpYield) isExp = true;
                    if (of[k].GrowTime >= baseGrow * 1.5f) isHeavy = true;
                    if (of[k].GrowTime <= baseGrow * 0.7f) isFast = true;
                }
                int kinds = (isGold ? 1 : 0) + (isExp ? 1 : 0) + (isHeavy ? 1 : 0) + (isFast ? 1 : 0);
                // ★★ v199：周期象限（重/快）只在"该档的周期真的分成两档"时才有可满足的成员 ★★
                //   节奏调整把 B/A/S 三档的周期**钳成同一个值**（48 / 42.5 / 37 秒）：
                //   `重 = 周期 ≥1.5×基准` 与 `快 = ≤0.7×基准` 在这三档**没有任何成员能同时满足**
                //   —— 这是本轮"全部 ≤60 秒"的**数学结果**，不是漏填 ✗
                //   ⇒ 判据按"周期轴还在不在"分档（断言不删，只是要求数跟着轴走）：
                //       周期有差异（max ≥ 1.5×min）⇒ 仍要 min(4, 株数) 种（金/验/重/快）
                //       周期已均一（max < 1.5×min）⇒ 只要 min(2, 株数) 种（金型 + 验型）
                //   ★ D（8/8/16/4）与 C（27/27/54/13.5）两档周期仍有差异 ⇒ 仍然要求 4 种 ✓
                float tMin = float.MaxValue, tMax = 0f;
                for (int k = 0; k < of.Length; k++)
                {
                    if (of[k].GrowTime < tMin) tMin = of[k].GrowTime;
                    if (of[k].GrowTime > tMax) tMax = of[k].GrowTime;
                }
                bool cycleAxis = tMax >= tMin * 1.5f;
                int need = cycleAxis ? Mathf.Min(4, of.Length) : Mathf.Min(2, of.Length);
                int dup = 0; string dupWhere = "";
                for (int a = 0; a < of.Length; a++)
                    for (int b = a + 1; b < of.Length; b++)
                        if (of[a].GoldYield == of[b].GoldYield && of[a].ExpYield == of[b].ExpYield
                            && Mathf.Abs(of[a].GrowTime - of[b].GrowTime) < 0.001f)
                        {
                            dup++;
                            dupWhere = $"{of[a].Id} 与 {of[b].Id} 都是 {of[a].GoldYield}/{of[a].ExpYield}/{of[a].GrowTime:0.#}s";
                        }

                if (archSummary.Length > 0) archSummary.Append('｜');
                archSummary.Append($"{of[0].Grade} {kinds} 种");
                if (kinds < need || dup > 0)
                {
                    archetypeBad++;
                    archMsg.Append($"{of[0].Grade} 档只有 {kinds}/{need} 种")
                          .Append($"（金 {(isGold ? "有" : "无")}／验 {(isExp ? "有" : "无")}／重 {(isHeavy ? "有" : "无")}／快 {(isFast ? "有" : "无")}）");
                    if (dup > 0) archMsg.Append($"；三元组重复：{dupWhere}");
                    archMsg.Append("；");
                }
            }
            list.Add(new CheckResult("SEED-ARCHETYPE：每档象限齐备（周期有差异的档要 金/验/重/快 四型，周期已被钳成同一值的档要 金/验 两型）且 (金币,经验,周期) 三元组不重复",
                archetypeBad == 0,
                archetypeBad == 0 ? "象限齐备：" + archSummary : archMsg.ToString()));

            // ───────────────────────── SEED-TIER-RATE ─────────────────────────
            // 判据（**唯一实现**在 `PlantMechanics.SumPerSecond` / `ExpectedSum`，这里不另写一份）：
            //     每秒总和 = (goldYield + expYield) / growTime      ← 用户 v200 的字面口径
            //     目标     = V档 × f(t) × growTime × (有机制 ? 1 − 6% : 1)
            //
            // ★★ v200：**判据换了半边**（名字保留 —— 它是上游 grep 的锚点）★★
            //
            // 用户原话："规定同品质的种子，金币和经验值获取量在相同时间内相同，若成熟时间长，
            //           则总和在此基础上略微增加（时间越长，增加越多），若有机制，则获取量总和减少，
            //           高品质的永远比低品质获取总和高"
            //
            // ⇒ 旧判据"同档每秒价值差 ≤ 2%"**只覆盖了第一条规则的半边**：
            //   它要求档内**处处相等**，而这与"越长越增"（规则②）在数学上**直接矛盾** ✓
            //   （相等 ⇒ 不能随周期增加；增加 ⇒ 不能相等。二者只能取其一。）
            //   ⇒ 判据改成**四条分别判**，一条对应用户的一句话：
            //     ① 同档同 t ⇒ 整数和**逐位相等**（精确，无容差）
            //        不同 t ⇒ `|整数和 − 预算| ≤ 0.5`（四舍五入的**理论下界**，不是放宽）
            //     ② 同档内 `(金+验)/秒` 随成熟时间**单调不减**（"越长越增"）
            //     ③ 机制植物**严格低于**同档同 t 的无机制植物（"有机制⇒总和减少"）
            //     ④ 跨档**严格递增**：低档最大值 < 高档最小值（"高品质永远更高"）
            //   ★ 断言**没有删除**：Still 一条 `SEED-TIER-RATE`，名字没改、失败即红；
            //     变的是"允许 f(t) 阶梯存在"，以及每条规则**分开判**⇒ 比旧的那条**更严**：
            //     旧条对"同档同 t 不同和"是放过 2% 的（整数粒度），新条要求**逐位相等** ✓
            //
            // ★ 为什么经验不再乘 2（v197 的 `+2×验证` 口径在本条**退役**）★
            //   v197 那条守的是"档内**感知价值**相等"（1 金 = 2 验），
            //   而它必然让同档的"金型"与"验型"**总和不等**（金型 6+1=7 vs 验型 2+4=6 ⇒ 差 14%）
            //   ⇒ 与用户本轮的第一条规则正面冲突 ⇒ 换成 1:1 ✓
            //   ⚠️ 权重一变，"档内不再有支配关系"就不能只靠"两数相等"：
            //     金型与验型在同一档里**总和相同、构成不同** ⇒ 玩家按需要（买货 vs 升级）选，不是按强弱选 ✓
            const float pairTol = 0.5f + 1e-3f;      // 整数和与预算的最大允许偏差（四舍五入下界）
            int rateBad = 0;
            float worstSpread = 0f; string worstWhere = "—";
            var rateMsg = new StringBuilder();
            for (int g = 0; g <= 4; g++)
            {
                SeedDef[] of = TableDatabase.SeedsOfGrade((SeedGrade)g);
                if (of == null || of.Length < 2) continue;
                float lo = float.MaxValue, hi = float.MinValue;
                SeedDef loSeed = of[0], hiSeed = of[0];
                for (int k = 0; k < of.Length; k++)
                {
                    // ★ v197：改成 `FairValuePerSecond` = 每秒价值 ×(1 + 机制补偿) ★
                    //   用户本轮要求"机制植物数值**低于**同品质无机制植物"，而这条判据要求
                    //   "同档差 ≤ 2%" —— 两者只有在**把机制本身的价值算进去**之后才不矛盾 ✓
                    //   机制补偿率与理由写在 `PlantMechanics.MechanicPayback` 上（唯一来源）✓
                    float v = PlantMechanics.FairValuePerSecond(of[k]);
                    if (v < lo) { lo = v; loSeed = of[k]; }
                    if (v > hi) { hi = v; hiSeed = of[k]; }
                }
                float spread = hi <= 0f ? 1f : (hi - lo) / hi;
                // ★★ v200：档内公平值**不再要求彼此相等** ★★
                //   规则②要求"成熟时间长的总和略高" ⇒ f(t) 让同档不同周期**本来就该不同**，
                //   所以 `spread` 只作**读数**（它现在度量的是 f(t) 跨出的那一截 + 整数台阶），
                //   判红交给下面四条"各自落在自己的预算上"的检查 ✓
                if (spread > worstSpread)
                {
                    worstSpread = spread;
                    worstWhere = $"{of[0].Grade} 档 {hiSeed.Id} {hi:0.####} vs {loSeed.Id} {lo:0.####}";
                }

                // ── 判据①（数值）：整数和必须落在 `round(V档 × f(t) × t × 机制折扣)` 上 ──
                //   `|差| ≤ 0.5` 是**四舍五入的理论下界**：整数刻度的物理下限，不是放宽 ✓
                for (int k = 0; k < of.Length; k++)
                {
                    int sum = of[k].GoldYield + of[k].ExpYield;
                    float tgt = PlantMechanics.ExpectedSum(of[k]);
                    if (Mathf.Abs(sum - tgt) > pairTol)
                    {
                        rateBad++;
                        rateMsg.Append($"{of[0].Grade} 档 {of[k].Id} 整数和 {sum} 偏离预算 {tgt:0.###}（差 {sum - tgt:+0.000;-0.000}，上限 {pairTol:0.0}）；");
                    }
                }

                // ── 判据①（精确）：同档 **同 t 且同机制状态** ⇒ 整数和**逐位相等**（无容差）──
                //   这一条就是用户那句"相同时间内相同"的**字面实现**：
                //   周期相同 ⇒ 预算相同 ⇒ 整数和必须一模一样，差 1 都不行 ✓
                for (int a = 0; a < of.Length; a++)
                    for (int b = a + 1; b < of.Length; b++)
                        if (Mathf.Abs(of[a].GrowTime - of[b].GrowTime) < 1e-3f
                            && PlantMechanics.HasMechanic(of[a]) == PlantMechanics.HasMechanic(of[b])
                            && (of[a].GoldYield + of[a].ExpYield) != (of[b].GoldYield + of[b].ExpYield))
                        {
                            rateBad++;
                            rateMsg.Append($"{of[0].Grade} 档 {of[a].Id}({of[a].GoldYield}+{of[a].ExpYield}) 与 {of[b].Id}({of[b].GoldYield}+{of[b].ExpYield}) 同期 {of[a].GrowTime:0.#}s 却不同和；");
                        }

                // ── 判据②：同档内（按机制状态分组）`(金+验)/秒` 随成熟时间**单调不减** ──
                for (int a = 0; a < of.Length; a++)
                    for (int b = 0; b < of.Length; b++)
                        if (PlantMechanics.HasMechanic(of[a]) == PlantMechanics.HasMechanic(of[b])
                            && of[a].GrowTime < of[b].GrowTime - 1e-3f
                            && PlantMechanics.SumPerSecond(of[b]) < PlantMechanics.SumPerSecond(of[a]) - 1e-6f)
                        {
                            rateBad++;
                            rateMsg.Append($"{of[0].Grade} 档 {of[b].Id}（{of[b].GrowTime:0.#}s，{PlantMechanics.SumPerSecond(of[b]):0.####}）比更短的 {of[a].Id}（{of[a].GrowTime:0.#}s，{PlantMechanics.SumPerSecond(of[a]):0.####}）还低；");
                        }

                // ── 判据③：机制植物**严格低于**同档同 t 的无机制植物 ──
                //   该 t 没有无机制对照时，退化成"严格低于该 t 的档基准 V档×f(t)" ✓
                for (int a = 0; a < of.Length; a++)
                {
                    if (!PlantMechanics.HasMechanic(of[a])) continue;
                    float ra = PlantMechanics.SumPerSecond(of[a]);
                    float peer = -1f;
                    for (int b = 0; b < of.Length; b++)
                        if (!PlantMechanics.HasMechanic(of[b]) && Mathf.Abs(of[a].GrowTime - of[b].GrowTime) < 1e-3f)
                        {
                            float rb = PlantMechanics.SumPerSecond(of[b]);
                            peer = peer < 0f ? rb : Mathf.Min(peer, rb);
                        }
                    float bound = peer >= 0f ? peer : PlantMechanics.BudgetSumPerSecond(of[0].Grade, of[a].GrowTime);
                    if (!(ra < bound - 1e-6f))
                    {
                        rateBad++;
                        rateMsg.Append($"{of[0].Grade} 档机制植物 {of[a].Id} {ra:0.####} 未严格低于{(peer >= 0f ? "同 t 无机制" : "档基准")} {bound:0.####}；");
                    }
                }
            }
            // ── 判据④：跨档**严格**递增 —— 低档最大值 < 高档最小值 ──
            SeedGrade[] ladder = { SeedGrade.D, SeedGrade.C, SeedGrade.B, SeedGrade.A, SeedGrade.S };
            var rateSummary = new StringBuilder();
            for (int i = 0; i < ladder.Length; i++)
            {
                SeedDef[] of = TableDatabase.SeedsOfGrade(ladder[i]);
                if (of == null || of.Length == 0) continue;
                float lo = float.MaxValue, hi = 0f;
                for (int k = 0; k < of.Length; k++)
                {
                    float v = PlantMechanics.SumPerSecond(of[k]);
                    lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v);
                }
                if (rateSummary.Length > 0) rateSummary.Append('｜');
                rateSummary.Append($"{ladder[i]} {lo:0.0000}~{hi:0.0000}");
                if (i + 1 >= ladder.Length) break;
                SeedDef[] next = TableDatabase.SeedsOfGrade(ladder[i + 1]);
                if (next == null || next.Length == 0) continue;
                float nextMin = float.MaxValue;
                for (int k = 0; k < next.Length; k++) nextMin = Mathf.Min(nextMin, PlantMechanics.SumPerSecond(next[k]));
                if (!(hi < nextMin - 1e-6f))
                {
                    rateBad++;
                    rateMsg.Append($"{ladder[i]} 档最大 {hi:0.####} 未严格低于 {ladder[i + 1]} 档最小 {nextMin:0.####}；");
                }
            }
            list.Add(new CheckResult("SEED-TIER-RATE：四条规则分开判 —— ①同期整数和逐位相等/不同期 |和−预算|≤0.5 ②档内 (金+验)/秒 随 t 单调不减 ③机制严格更低 ④跨档严格递增（预算 = V档×f(t)×t，公式在 PlantMechanics）",
                rateBad == 0,
                rateBad == 0
                    ? $"四条判据全过｜五档 (金+验)/秒：{rateSummary}｜档内最大公平值差 {(worstSpread * 100f):0.00}%（{worstWhere}，由 f(t) 阶梯造成，不是缺陷）"
                    : rateMsg.ToString()));

            // ───────────────────────── ITEM-COVERAGE ─────────────────────────
            // 用户点名的 15 个属性，每一个都要在**至少一条 category 2** 的道具里
            //   ① 出现在 effects（增益侧）  ② 出现在 costs（减益侧）
            // ★ 为什么用 `StatId` 枚举字面量而不是字符串 ★
            //   写 "CritDamage" 这种**拼错就静默失效**的字符串，会得到一条永远通过的空断言 ✗
            //   用枚举 ⇒ 拼错是**编译错误** ✓
            // ★ 名字对不上之处（唯一的）★：用户写 `CritDamage`，引擎里的枚举成员是 **`CritDmg`**
            //   （`Defs.cs` 的 `StatId.CritDmg = 9`，`items.json` 里也必须写 "CritDmg"）✓
            // ★ 只统计 category 2 ★：category 1 是"纯增益"，它没有 costs 那一侧，
            //   把它算进来就等于把"减益覆盖"这条要求悄悄漏掉 ✓
            StatId[] needStats =
            {
                StatId.MeleeDamage, StatId.RangedDamage, StatId.BlastDamage,
                StatId.MaxHp, StatId.Shield, StatId.MoveSpeed,
                StatId.AtkSpeed, StatId.AtkRange, StatId.AtkMult,
                StatId.CritRate, StatId.CritDmg, StatId.Knockback,
                StatId.ExpMult, StatId.GoldMult, StatId.HeartDropRate
            };
            int missGain = 0, missCost = 0;
            var covMsg = new StringBuilder();
            for (int s = 0; s < needStats.Length; s++)
            {
                bool gain = false, cost = false;
                for (int i = 0; i < TableDatabase.Items.Length; i++)
                {
                    ItemDef it = TableDatabase.Items[i];
                    if (it.Category != 2) continue;
                    for (int k = 0; k < it.Effects.Length; k++) if (it.Effects[k].Stat == needStats[s]) gain = true;
                    for (int k = 0; k < it.Costs.Length; k++) if (it.Costs[k].Stat == needStats[s]) cost = true;
                }
                if (!gain) { missGain++; covMsg.Append(needStats[s] + " 缺增益；"); }
                if (!cost) { missCost++; covMsg.Append(needStats[s] + " 缺减益；"); }
            }
            list.Add(new CheckResult("ITEM-COVERAGE：15 个属性在 category 2 的 effects 与 costs 里各至少出现一次",
                missGain == 0 && missCost == 0,
                missGain == 0 && missCost == 0
                    ? $"{needStats.Length}/{needStats.Length} 属性双向覆盖（增益 {needStats.Length}、减益 {needStats.Length} 各 ≥1 次）"
                    : $"缺增益 {missGain} 条、缺减益 {missCost} 条 ⇒ {covMsg}"));

            // ───────────────────────── ITEM-NO-RULE ─────────────────────────
            // ★★ v197：判据**换向**（原名保留 —— 它是上游 grep 的锚点）★★
            //   旧判据：表里不该有任何 `rule` 字段（机制类道具整体下线）✗
            //   新判据（用户本轮：10 件机制道具）：
            //     ① **恰好 10 件**带 rule，且就是点名的这 10 件 ✓
            //     ② **每一条 rule 都有解释器**（`ItemRules.IsKnown`）——
            //        这条同时挡住了"表里写了一个没人读的键"这个最危险的形态 ✓
            //     ③ 反向：**登记过的每条规则都被表用到**（登了没人用 = 死键）
            //        ⇒ ③ 是 v179 当时因为"没有类别 3"而删掉的那条，本轮**恢复** ✓
            //   ⇒ 断言没删，只是把这件"表↔解释器"的合同写成双向 ✓
            string[] v197RuleIds =
            {
                "carrot_ring", "bunny_pet", "evade_helm", "big_can", "windup_gear",
                "charge_coil", "aegis_lamp", "gamble_glove", "sharp_spray", "tycoon_coin"
            };
            int ruleLeft = 0, ruleBad = 0, ruleMissing = 0;
            var ruleMsg = new StringBuilder();
            var rulesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < TableDatabase.Items.Length; i++)
            {
                ItemDef it = TableDatabase.Items[i];
                if (string.IsNullOrEmpty(it.Rule)) continue;
                ruleLeft++;
                if (!ItemRules.IsKnown(it.Rule)) { ruleBad++; ruleMsg.Append(it.Id + "=" + it.Rule + "（无解释器）；"); }
                else rulesSeen.Add(it.Rule);
            }
            for (int k = 0; k < v197RuleIds.Length; k++)
            {
                ItemDef it = TableDatabase.GetItem(v197RuleIds[k]);
                if (it == null || string.IsNullOrEmpty(it.Rule)) { ruleMissing++; ruleMsg.Append(v197RuleIds[k] + " 没有 rule；"); }
            }
            // 反向：**v197 登记的 10 条规则必须全部被表用到**（登了没人用 = 死键）
            // ★ 为什么不把 `ItemRules.All` 整个当分母 ★
            //   里面还有 3 条**历史规则**（reroll_discount / harvest_gold_pct / harvest_dew）：
            //   它们的**消费点仍然活着**（`RunSession.SyncItemRules` 每回合写三个系数），
            //   只是这一轮表里没有道具引用它们（v179 删掉了那三件机制道具）✓
            //   把它们算进"必须被用到"，等于要求"必须有表行引用一个刻意保留的解释器" ✗
            //   ⇒ 分母改成 `ProgRules` 那 10 条（本轮新增、必须接上），历史 3 条只报读数 ✓
            string[] progRules =
            {
                ProgRules.CarrotOrbit, ProgRules.RabbitPet, ProgRules.DodgeCapUp, ProgRules.WaterChargeUp,
                ProgRules.AtkSpeedRamp, ProgRules.DamageRamp, ProgRules.BlockShield, ProgRules.CritTrade,
                ProgRules.SharpWater, ProgRules.GoldDamage
            };
            int ruleUnused = 0, legacyUnused = 0;
            for (int k = 0; k < progRules.Length; k++)
                if (!rulesSeen.Contains(progRules[k])) { ruleUnused++; ruleMsg.Append(progRules[k] + " 登记了但没人用；"); }
            for (int k = 0; k < ItemRules.All.Length; k++)
            {
                bool isProg = false;
                for (int q = 0; q < progRules.Length && !isProg; q++)
                    if (string.Equals(ItemRules.All[k], progRules[q], StringComparison.OrdinalIgnoreCase)) isProg = true;
                if (!isProg && !rulesSeen.Contains(ItemRules.All[k])) legacyUnused++;
            }
            list.Add(new CheckResult("ITEM-NO-RULE：恰好 10 件机制道具带 rule（点名的 10 件），每条 rule 都有解释器，且 v197 的 10 条规则全被表用到",
                ruleLeft == v197RuleIds.Length && ruleBad == 0 && ruleMissing == 0 && ruleUnused == 0,
                ruleBad == 0 && ruleMissing == 0 && ruleUnused == 0
                    ? $"{ruleLeft} 件带 rule｜v197 10/10 条规则被用到｜另 {legacyUnused} 条历史规则（刷新折扣/收获金币/返水）仍只由 SyncItemRules 消费（表无行引用，保留）｜其余 {TableDatabase.Items.Length - ruleLeft} 件无 rule"
                    : $"带 rule {ruleLeft} 件（应 {v197RuleIds.Length}）｜无解释器 {ruleBad}／缺 rule {ruleMissing}／没被用 {ruleUnused} ⇒ {ruleMsg}"));

            // ───────────────────────── ITEM-V191-COVERAGE ─────────────────────────
            // v191 的 22 件新道具，验收口径是"**涉及目前所有属性的操作**"（用户原话）✗
            // 这里不统计"哪一件改了哪个属性"（人读表就能看），而是钉**集合覆盖**：
            // 下面这 19 个属性，每一个都必须在**新增的 22 件**里至少出现一次**正向** ✓
            //
            // ★ 为什么是 19 而不是 22（差额必须点名，否则就成了"漏了但没人知道"）★
            //   `MaxHp`   —— 正向由**老表**承担（scarecrow `Add 1`／iron_bark `Add 3`）⇒ 本轮不重复；
            //                它在新增件里只作为**代价**出现（frenzy_band −2）✓
            //   `Dodge`   —— 值是 0..1 小数、却**不在** `IsFractionStat` ⇒ 只能写整数 Add
            //                （`Add 1` = +100%，一瞬间顶满 `caps.dodge` 0.6），小数写法会被
            //                `TableDatabase.Validate` 判红 ⇒ 在本轮规则下**表达不出合理数值** ✗
            //   `MaxPlots`—— 语义已变成"可种植株数 = 田地列数 6"且**设计上不可修正**
            //                （见 `V49Checks` 的 `fixedByDesignStats`）⇒ 给它道具等于给一个死属性 ✗
            //   ★ 这两条是如实上报的"做不到"，不是忘了 ✓ 证据在交付报告的覆盖矩阵里 ✓
            StatId[] v191Gain =
            {
                StatId.Shield, StatId.MeleeDamage, StatId.RangedDamage, StatId.BlastDamage,
                // ★ v195：`ExplodeDamage` 已随属性删除（blast_grain 改给 `BlastDamage`）★
                //   ⇒ 这份清单 19 → **18**，判据本身没变（"22 件里每个属性至少正向出现一次"）✓
                StatId.AtkMult, StatId.ExplodeRange,
                StatId.CritRate, StatId.CritDmg, StatId.AtkSpeed, StatId.AtkRange, StatId.MoveSpeed,
                StatId.Knockback, StatId.HeartDropRate, StatId.HeartHeal,
                StatId.ExpMult, StatId.GoldMult, StatId.Luck, StatId.PigRate
            };
            // "新增的 22 件"用**显式清单**钉住（表行顺序不是契约；漏加一件也要红）
            string[] v191Ids =
            {
                "whet_oil", "wind_bell", "long_handle", "lucky_bead", "grind_stone", "blast_grain",
                "wide_funnel", "healing_herb", "dual_straps", "spring_basket", "heart_lantern",
                "fertile_core", "berserk_mask", "longbow_arm", "thorn_whip", "big_bore",
                "stone_draught", "night_lamp", "frenzy_band", "tailwind_sash",
                "lucky_stone", "pig_bell"
            };
            var v191GainHit = new HashSet<StatId>();
            int v191Missing = 0;
            var v191Msg = new StringBuilder();
            for (int k = 0; k < v191Ids.Length; k++)
            {
                ItemDef it = TableDatabase.GetItem(v191Ids[k]);
                if (it == null) { v191Missing++; v191Msg.Append(v191Ids[k] + " 不在表里；"); continue; }
                for (int e = 0; e < it.Effects.Length; e++) v191GainHit.Add(it.Effects[e].Stat);
            }
            int covMiss = 0;
            var covMissMsg = new StringBuilder();
            for (int s = 0; s < v191Gain.Length; s++)
                if (!v191GainHit.Contains(v191Gain[s])) { covMiss++; covMissMsg.Append(v191Gain[s] + "；"); }
            list.Add(new CheckResult("ITEM-V191-COVERAGE：22 件新道具把 18 个属性各正向覆盖 ≥1 次（MaxHp 由老表承担；Dodge/MaxPlots 见注释；v195 删 ExplodeDamage）",
                v191Missing == 0 && covMiss == 0,
                v191Missing > 0 ? $"新道具缺 {v191Missing} 件：{v191Msg}"
                    : (covMiss == 0 ? $"{v191Ids.Length} 件全部在表里｜{v191Gain.Length}/{v191Gain.Length} 属性正向覆盖"
                                    : $"缺 {covMiss} 条正向覆盖：{covMissMsg}")));

            // ───────────────────────── ITEM-ICON-MASK / ITEM-ICON-UNIQUE / ITEM-ICON-TONE ─────
            // ★ 图标为什么也要机器验（而不是"我看过图了"）★
            //   位掩码是**手抄的字符画**：一行少一个点、两件抄成同一张，在 C# 里都完全合法 ✗
            //   而症状是"某件道具的图标多一条竖线"或"两件道具看起来一样"—— 人眼容易漏 ✓
            //     ITEM-ICON-MASK   —— **46** 件每件都有掩码，每张 11×11、字符域只有 `#`
            //                         与 `+` 与 `.`、键名必须是表里真实存在的道具 id（防野键）
            //     ITEM-ICON-UNIQUE —— **46** 张两两不同（防复制粘贴）
            //     ITEM-ICON-TONE   —— 正/负两类配色确实不同（用户要求"正负色调区分"）
            //
            // ★ v191·fx3：这两条的覆盖范围从 22 件提到 **39 件**（新增 22 + 老道具补图 17）★
            //   用户原话"部分物品图标没有制作"：24 件老道具里 **17 件没有 PNG**
            //   ⇒ 本轮给它们补了程序化掩码。**断言一条没删**，只是把"22 件 / 22 张"提到 39：
            //     ① `backfillIds` 显式列出那 17 件，与 `v191Ids` 拼成 39 件**逐件点名**地查掩码
            //     ② `ITEM-ICON-UNIQUE` 的分母跟着变成 39 ⇒ 张数少一张、或多出一张野掩码，都红 ✓
            //   为什么不直接把 17 个 id 塞进 `v191Ids`：那会连带改掉上面
            //   `ITEM-V191-COVERAGE` 的语义（它会去统计这 17 件的正向属性覆盖）✗
            //
            // ★ v196·fx4：39 → **46**（表内 46 件道具，一件不缺）★
            //   又补了 7 件：iron_hoe / ash_pouch / scarecrow / snail_shell / sprinter_boots /
            //   lens / lucky_clover —— 就是 24 件老道具里上一轮没轮到的那批 ⇒ `backfillIds` 17 → 24。
            //   **判据没放宽，只收紧了**：分母 39 → 46（`iconAllCount` 由两个数组长度相加得出，
            //   所以"少补一件"会同时打红 MASK 与 UNIQUE 两条）；两条的文本也一并改成 46 ✓
            //   为什么这 7 件也放进 `backfillIds` 而不是 `v191Ids`：同上一条 —— `v191Ids` 那份清单
            //   还担着 `ITEM-V191-COVERAGE` 的属性覆盖统计，往里塞 id 会悄悄改掉另一条断言的语义 ✗
            string[] backfillIds =
            {
                "water_pouch", "glass_cannon", "plow_weight", "hardwood_haft", "twin_blade",
                "ferment_jar", "overclock_core", "thorn_collar", "iron_bark", "sniper_scope",
                "barbed_tip", "gambler_dice", "wax_seal", "blood_pact", "greedy_sickle",
                "hollow_fruit", "heirloom",
                // v196·fx4 追加的 7 件（老道具 17 → 24）
                "iron_hoe", "ash_pouch", "scarecrow", "snail_shell", "sprinter_boots",
                "lens", "lucky_clover"
            };
            // ★ v197：46 → **62**（10 件机制道具 + 6 株机制植物）★
            //   判据两处变化，都**只收紧不放宽**：
            //     ① 分母 46 → 62，且新增的 16 个 id 用**显式清单**点名（漏一张就红）✓
            //     ② "键名必须是表里真实道具" → "**真实道具或真实种子**"
            //        （唯一实现 `TableDatabase.IconMaskKeyValid`；理由见 `ItemIconMasks` 里那段注释）✓
            string[] v197IconIds =
            {
                "carrot_ring", "bunny_pet", "evade_helm", "big_can", "windup_gear",
                "charge_coil", "aegis_lamp", "gamble_glove", "sharp_spray", "tycoon_coin",
                "warblade_bloom", "gale_vine", "sunflower_crown", "rainlily", "shelter_ivy", "lucky_monarch"
            };
            int iconAllCount = v191Ids.Length + backfillIds.Length + v197IconIds.Length;   // 22 + 24 + 16 = 62
            int iconMissing = 0, iconShape = 0, iconOrphan = 0, iconDup = 0;
            var iconMsg = new StringBuilder();
            var iconSeen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string[]> kv in TableDatabase.ItemIconMasks)
            {
                if (!TableDatabase.IconMaskKeyValid(kv.Key)) { iconOrphan++; iconMsg.Append(kv.Key + " 既不是道具也不是种子；"); }
                string[] rows = kv.Value;
                if (rows == null || rows.Length != TableDatabase.ItemIconSize)
                { iconShape++; iconMsg.Append(kv.Key + " 行数不对；"); continue; }
                bool ok = true;
                for (int r = 0; r < rows.Length && ok; r++)
                {
                    if (rows[r] == null || rows[r].Length != TableDatabase.ItemIconSize) { ok = false; break; }
                    for (int c = 0; c < rows[r].Length; c++)
                        if (rows[r][c] != '#' && rows[r][c] != '+' && rows[r][c] != '.') { ok = false; break; }
                }
                if (!ok) { iconShape++; iconMsg.Append(kv.Key + " 形状非法；"); continue; }
                string sig = string.Join("|", rows);
                if (iconSeen.TryGetValue(sig, out string other)) { iconDup++; iconMsg.Append(kv.Key + " 与 " + other + " 同图；"); }
                else iconSeen[sig] = kv.Key;
            }
            // 62 件逐件点名（新增 22 + 老道具 24 + v197 的 16）—— 靠显式清单，漏一件就红 ✓
            for (int k = 0; k < v191Ids.Length; k++)
                if (TableDatabase.ItemIconMaskOf(v191Ids[k]) == null) { iconMissing++; iconMsg.Append(v191Ids[k] + " 没有掩码；"); }
            for (int k = 0; k < backfillIds.Length; k++)
                if (TableDatabase.ItemIconMaskOf(backfillIds[k]) == null) { iconMissing++; iconMsg.Append(backfillIds[k] + " 没有掩码；"); }
            for (int k = 0; k < v197IconIds.Length; k++)
                if (TableDatabase.ItemIconMaskOf(v197IconIds[k]) == null) { iconMissing++; iconMsg.Append(v197IconIds[k] + " 没有掩码；"); }
            list.Add(new CheckResult("ITEM-ICON-MASK：62 件（新增 22 + 老道具 24 + v197 的 16）每件都有 11×11 位掩码，字符域只有 # + .，键名都是真实道具或真实种子",
                iconMissing == 0 && iconShape == 0 && iconOrphan == 0,
                iconMissing == 0 && iconShape == 0 && iconOrphan == 0
                    ? $"{TableDatabase.ItemIconMasks.Count} 张掩码全部合法，62/62 件都有图标（新增 22 + 老道具 24 + v197 16）"
                    : $"缺图 {iconMissing}／形状非法 {iconShape}／野键 {iconOrphan} ⇒ {iconMsg}"));
            list.Add(new CheckResult("ITEM-ICON-UNIQUE：62 张掩码两两不同（不许两件道具/两株植物共用一个图标）",
                iconDup == 0 && TableDatabase.ItemIconMasks.Count == iconAllCount,
                iconDup > 0
                    ? $"重复 {iconDup} 张 ⇒ {iconMsg}"
                    : (TableDatabase.ItemIconMasks.Count == iconAllCount
                        ? $"{TableDatabase.ItemIconMasks.Count} 张互不相同（新增 22 + 老道具 24 + v197 16）"
                        : $"张数 {TableDatabase.ItemIconMasks.Count} ≠ 应有 {iconAllCount}")));
            TableDatabase.ItemIconPalette(1, out Color iconPure, out Color iconPureAcc);
            TableDatabase.ItemIconPalette(2, out Color iconCost, out Color iconCostAcc);
            list.Add(new CheckResult("ITEM-ICON-TONE：纯增益与有代价的图标色调可区分（绿 vs 橙红）",
                Mathf.Abs(iconPure.g - iconCost.g) > 0.2f && iconPure.g > iconPure.r && iconCost.r > iconCost.g,
                $"纯增益 rgb({iconPure.r:0.00},{iconPure.g:0.00},{iconPure.b:0.00})"
                + $"／有代价 rgb({iconCost.r:0.00},{iconCost.g:0.00},{iconCost.b:0.00})"));

            // ★★ v198：**62 张 PNG 真的存在、而且真的是 64×64** ★★
            //
            // ── 为什么这条必须存在（上面那两条已经绿了，为什么还不够）──
            //   上面两条验的是**掩码表**。而 v198 把图标来源改成了"**PNG 优先、掩码兜底**"
            //   （`ImGuiHud.ItemIconOf` 本来就是这个顺序）⇒ 只要 PNG 存在，**掩码根本不会被画**。
            //   于是"62 张掩码都在"这件事与"玩家看到的是像素画还是 11×11 色块"**没有关系** ✓
            //   漏一张 PNG 的症状不是报错、不是崩溃，而是那一件道具**悄悄退回色块** ——
            //   正是本工程反复吃过的那种"不吭声的坏" ✗ ⇒ 必须有一条断言逐件点名 ✓
            //
            // ── 判据 ──
            //   ① 62 个 id 逐个 `Resources.Load<Texture2D>("Art/UI/icon_item_<id>")` **非空** ✓
            //   ② 尺寸恰好 64×64（生成器的统一规范；改成别的尺寸等于改这一套规范）✓
            //   ③ 6 株机制植物**还要有** `icon_plant_<id>`：
            //      植物格 / 种子卡 / 商店种子行走的是这条路（见 `ImGuiHud.DrawOfferIcon`），
            //      名字与道具那条**不一样** —— 少一张就是"商店里有图、田里那格还是色块" ✗
            int pngMissing = 0, pngSize = 0, pngMsgCount = 0;
            var pngMsg = new StringBuilder();
            for (int k = 0; k < iconAllCount; k++)
            {
                string id = k < v191Ids.Length ? v191Ids[k]
                          : k < v191Ids.Length + backfillIds.Length ? backfillIds[k - v191Ids.Length]
                          : v197IconIds[k - v191Ids.Length - backfillIds.Length];
                Texture2D tex = Resources.Load<Texture2D>("Art/UI/icon_item_" + id);
                if (tex == null)
                {
                    pngMissing++;
                    if (pngMsgCount++ < 8) pngMsg.Append("缺 item/" + id + "；");
                    continue;
                }
                if (tex.width != 64 || tex.height != 64)
                {
                    pngSize++;
                    if (pngMsgCount++ < 8) pngMsg.Append($"{id} 尺寸 {tex.width}×{tex.height} ≠ 64×64；");
                }
                if (TableDatabase.HasSeed(id))
                {
                    Texture2D plant = Resources.Load<Texture2D>("Art/UI/icon_plant_" + id);
                    if (plant == null)
                    {
                        pngMissing++;
                        if (pngMsgCount++ < 8) pngMsg.Append("缺 plant/" + id + "；");
                    }
                    else if (plant.width != 64 || plant.height != 64)
                    {
                        pngSize++;
                        if (pngMsgCount++ < 8) pngMsg.Append($"plant/{id} 尺寸 {plant.width}×{plant.height} ≠ 64×64；");
                    }
                }
            }
            list.Add(new CheckResult(
                "ITEM-ICON-PNG：62 张道具图标 PNG 都在（64×64），6 株机制植物另有 icon_plant_* 一张（PNG 优先于掩码）",
                pngMissing == 0 && pngSize == 0,
                pngMissing == 0 && pngSize == 0
                    ? $"62 张 icon_item_*.png ＋ 6 张 icon_plant_*.png 全部 64×64（漏一张就会退回 11×11 掩码色块）"
                    : $"缺图 {pngMissing}／尺寸不对 {pngSize} ⇒ {pngMsg}"));

            // ───────────────────────── PLUGIN-NO-STAT ─────────────────────────
            // 阶段 3 的取向：插件表要"**每件插件说清它改的是攻击的哪一环**"✗
            // 而不是"给玩家属性加一个数"：属性类插件买到的是**更大的同一个体验**，
            // 玩家在商店里没有真正的选择（永远是"哪件数字高"）✓
            // ★ 为什么这条放这里，而不是放进 `PluginChecks` ★
            //   `PluginChecks` 验的是**逐件**的事（每件插件都有落点、每件都装得上）；
            //   这一条验的是**整表的取向** —— "还剩几件在改属性"是跨行统计，
            //   逐行校验永远看不出来 ⇒ 归到 `TableGuardChecks` ✓
            //
            // ★★ 判据为什么是**两个条件取或**，以及为什么必须比字符串 ★★
            // ① 比 **表里的字面值**（`pl.rawEffect`）与 11 个属性类名字：
            //    用户原话是"表里不许再出现这些类的 effect" —— 判据本就该落在**数据**上 ✓
            //    将来若有人把这些名字加回 `PluginEffectKind`，解析会成功、只有字面值比才抓得住 ✓
            // ② 也算"解析失败"（`effect == None` 且字面值非空）：
            //    这些名字**现在不在枚举里** ⇒ `ParseEnum("DamageFlat", None)` **回落 `None`** ✓
            //    ⇒ 只比 `effect.ToString()` 的话，得到的是 `"None"`、不在那 11 个字符串里 ⇒ **抓不住** ✗
            //    （所以"只比字符串"与"改坏后应红"这两条要求是互相矛盾的 ⇒ 必须两条或 ✓）
            // ★ 报错必须点名**哪几件、表里写的是什么** ★：只报一个数字无法据此删哪行，
            //   所以明细走 `rawEffect`（解析后的 `effect` 只剩 `None`，等于告诉人"这里没写"✗）✓
            string[] statEffects =
            {
                "DamageFlat", "DamagePercent", "AttackSpeed", "CritRate", "CritDamage", "Range",
                "ProjectileCount", "FullHpBonus", "AmmoOnKill", "ExplodeRadius", "Knockback"
            };
            int statLeft = 0;
            var statMsg = new StringBuilder();
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
            {
                PluginAsset pl = TableDatabase.Plugins[i];
                if (pl == null) continue;
                string raw = pl.rawEffect ?? "";
                bool isStat = false;
                for (int k = 0; k < statEffects.Length && !isStat; k++)
                    if (string.Equals(raw, statEffects[k], StringComparison.Ordinal)) isStat = true;
                bool undefined = pl.effect == PluginEffectKind.None && raw.Length > 0;
                if (!isStat && !undefined) continue;
                statLeft++;
                if (statLeft > 1) statMsg.Append('\n');
                statMsg.Append("  ").Append(pl.id).Append(" = ").Append(raw)
                      .Append(isStat ? "（属性类：本版已下线）" : "（解析不出来 ⇒ 回落 None）");
            }
            list.Add(new CheckResult("PLUGIN-NO-STAT：没有任何一件插件的 effect 在改玩家属性（11 个属性类名字全部清零）",
                statLeft == 0,
                statLeft == 0
                    ? $"{TableDatabase.Plugins.Length} 件全部落在 12 种攻击形式取值上"
                    : $"{statLeft} 件仍在改玩家属性（或写了不存在的效果）：\n{statMsg}"));

            // ───────────────────────── PLUGIN-SLOT ─────────────────────────
            // 两条**同时**成立才算过：
            //   ① 每把武器**恰好 6 个插槽**（`PlayerGoods.MaxPluginsPerWeapon`）
            //   ② 每件插件 `maxStack ≤ 3`
            // ★ 为什么要钉 6 ★：插槽数是"6 选 N"这个取舍的地基 ——
            //   它一变，每件插件的定价与强度全部要重算，而改一个整数**在 JSON 与代码里都完全合法** ✗
            //   所以这里把它写成**字面量 6**（而不是再引用那个常量）：
            //   引用常量的话，"把常量改成 7"会让这条断言跟着 7 走 ⇒ 永远通过的空断言 ✗
            // ★ 为什么要钉 maxStack ≤ 3 ★：叠层**不占新插槽**（`PlayerGoods.SlotsUsedOn` 的注释写明），
            //   一件能叠 4 层的插件在一个槽位里就吃掉了 4 份强度 ⇒
            //   把"再装一件不同的插件"这个选择直接删掉 ✓
            string[] weaponNames = { "锄头", "水壶", "肥料" };
            // ★ 先读成**局部变量**再比 ★：`MaxPluginsPerWeapon` 是 `const`
            //   直接写 `if (PlayerGoods.MaxPluginsPerWeapon == 6)` 会被编译器**编译期折叠**成
            //   `if (true)` ⇒ 后面两行变成"无法访问的代码"（CS0162），
            //   而且运行期那条分支等于不存在 ✗ 读成变量才真的在跑 ✓
            int slotsPerWeapon = PlayerGoods.MaxPluginsPerWeapon;
            int slotBad = 0, stackBad = 0;
            var slotMsg = new StringBuilder();
            for (int w = 0; w <= 2; w++)
            {
                // 右边写**字面量 6** ⇒ 常量被改成 7 时这里必红 ✓
                if (slotsPerWeapon == 6) continue;
                slotBad++;
                slotMsg.Append($"{weaponNames[w]} {slotsPerWeapon} 个槽（应恰好 6）；");
            }
            for (int i = 0; i < TableDatabase.Plugins.Length; i++)
            {
                PluginAsset pl = TableDatabase.Plugins[i];
                if (pl == null || pl.maxStack <= 3) continue;
                stackBad++;
                // ★ 同样必须点名 ★：说清**哪件 id 的 maxStack 是多少**，
                //   否则"有 1 件超了"无法定位到具体那一行 ✓
                slotMsg.Append($"{pl.id} 的 maxStack = {pl.maxStack}（应 ≤ 3）；");
            }
            list.Add(new CheckResult("PLUGIN-SLOT：每把武器恰好 6 个插槽、每件插件 maxStack ≤ 3（叠层不占新插槽）",
                slotBad == 0 && stackBad == 0,
                slotBad == 0 && stackBad == 0
                    ? $"锄头/水壶/肥料 各 6 个槽｜{TableDatabase.Plugins.Length} 件 maxStack 全部 ≤ 3"
                    : $"槽位数不符 {slotBad} 把、maxStack 超 3 的 {stackBad} 件 ⇒ {slotMsg}"));
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

            // ── 工具升级机制（★ v186：档位只是"升级了几档"，不再是"品质"）
            var session = new RunSession();
            session.Start(424242UL);
            string reason;
            list.Add(new CheckResult("工具初始未拥有", session.ToolLevel[RunSession.ToolHoe] < 0, ""));
            bool first = session.UpgradeTool(RunSession.ToolHoe, 0, out reason);
            list.Add(new CheckResult("首次购买工具获得 0 档", first && session.ToolLevel[RunSession.ToolHoe] == 0,
                $"锄头 = 档位 {session.ToolLevel[RunSession.ToolHoe]}"));
            list.Add(new CheckResult("0 档工作能力 1", session.WorkCapability(RunSession.ToolHoe) == 1, ""));
            bool merged = session.UpgradeTool(RunSession.ToolHoe, 0, out reason);
            list.Add(new CheckResult("同档再买 → 合体升档", merged && session.ToolLevel[RunSession.ToolHoe] == 1,
                $"锄头 = 档位 {session.ToolLevel[RunSession.ToolHoe]}"));
            list.Add(new CheckResult("升档后工作能力 +1", session.WorkCapability(RunSession.ToolHoe) == 2, ""));
            bool lower = session.UpgradeTool(RunSession.ToolHoe, 0, out reason);
            list.Add(new CheckResult("拿更低档买会被拒绝", !lower, reason ?? ""));
            bool reject = session.UpgradeTool(RunSession.ToolCan, 3, out reason);
            list.Add(new CheckResult("直接买更高档 = 直接替换", reject && session.ToolLevel[RunSession.ToolCan] == 3,
                $"水壶 = 档位 {session.ToolLevel[RunSession.ToolCan]}"));
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

            // ── v186：武器**没有品质**，伤害只由 `(基础 + 对应伤害) × 伤害倍数` 决定
            // ★ 断言保留、期望值换掉（用户授权："改期望值 ✗ 不删断言"）★
            // 旧判据 `ToolMult(ToolCan) == 1` 量的是"品质倍率恰好是 1"——那个函数已整个删除 ✗
            // 新判据量"式子里有没有多出来的乘项"：把属性取 1、倍率取 1，
            // 结果必须**正好**等于 `基础 + 1` ⇒ 中间任何一层（旧品质倍率就是这一层）都会让这条挂掉 ✓
            float noQuality = DamageResolver.WeaponDamage(t.tools.can.baseValue, 1f, 1f, false, 2f, 0f);
            list.Add(new CheckResult("v186：武器伤害无品质乘项（=(基础+伤害属性)×伤害倍数）",
                Mathf.Abs(noQuality - (t.tools.can.baseValue + 1f)) < 0.001f,
                $"水壶 {t.tools.can.baseValue:0.##} + 远程 1 = {noQuality:0.##}"));
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
            // ★ v190：期望值 4 → 3（用户要求「灼烧、中毒改为触发 3 次」）★
            //   `ailmentTickCount` 4 → 3 ⇒ 施加时 `PoisonTicksLeft` 的初值就是 3 ✓
            //   与 `V9Checks` 里那两条（毒/火计数）是同一个设计值的两处断言，必须一起改 ✓
            Expect(list, "中毒立即生效（每 1 秒结算一次，v190：共 3 次）", st.PoisonTicksLeft, 3f);

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
            // ★ v189：这条的**契约变了**（改期望值，不删断言）★
            //
            // 改前：`StartWave` 会 `SwitchWindup = 0f`（连同 `WeaponCds` 一起清零）⇒
            //   断言写成"回合开始后前摇必须 ≤ 0"✗
            // 改后（`RunSession.ResetPlayerForNewWave` 里那句 v189 注释 + 实测读数）：
            //   跨分**不再清零过程量**（WeaponCds / SwitchWindup）——
            //   病因是"玩家按住左键时，跨分那一帧 CD 被写成 0 ⇒ 下一刀比正常早 0.58s 出现"，
            //   玩家侧的表现就是"攻击突然中断再触发、攻速异常" ✗
            // ⇒ 新契约正是"**回合开始不清切换前摇**"，并且比旧版更严：
            //   它钉住的是**具体值没被动过**（0.40s 还是 0.40s），而不是"是不是 ≤ 0" ✓
            list.Add(new CheckResult(
                "回合开始不清切换前摇（v189：过程量不随回合重置，钉住原值 0.40s）",
                Mathf.Abs(pr.SwitchWindup - 0.4f) < 1e-3f,
                $"{pr.SwitchWindup:0.00}s（期望仍为 0.40s）"));

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

        private const float PB_MeleeNear = 2.0f;    // 挥击框内（★ v205：当前 `tools.hoe.range = 3.8m`）
        // v182：剑气射程已改成**严格 = 攻击范围**（`tool.range × AtkRange` = 3.2m），不再 ×2
        // ⇒ 夹具从 5.5m（旧剑气 6.4m 的中段）挪进 3.2m 之内 ✓
        // 取 2.6m 的理由：仍在剑气射程 3.2m 内；又明显大于近战 range 的一半（1.6m），
        // 不代表"贴着玩家"的退化情形 ✓
        private const float PB_MeleeFar = 2.6f;
        /// <summary>
        /// 剑气探针的落点（v182 新增）：**在近战扇形之外、却仍在剑气矩形之内**。
        ///
        /// ★ 为什么不能用正前方 ★
        /// 剑气射程改成 = 攻击范围之后，"正前方 2.6m"**已经被近战扇形覆盖**
        /// （近战判 `距离 ≤ 3.2 + 敌人半径 0.67 = 3.87`）⇒ 空手那一刀就打得到它，
        /// `装前 == 装后` ⇒ "每件插件装上之后读数都真的变了"那条会红 ✗
        /// 而剑气的**新增**覆盖面现在只剩"矩形的角"：
        ///   · 角度 atan2(2.1, 1.5) = 54.5° **> 半张角 50°** ⇒ 近战漏掉 ✓
        ///   · |侧偏| 2.1 ≤ 近战框半宽 2.45 + 敌人半径 0.67 ⇒ 剑气打得到 ✓
        ///
        /// ★★ v205 把上面两行里的数**重算过了**（结论一个字没变，只是过期数字换成了当前的）★★
        ///   · 近战判据现在是 `距离 ≤ range 3.8 + 敌人半径 0.535`（`grunt 0.36 × 1.486`，已含缩放）
        ///     = **4.335m** —— 改前这里写的是旧射程 3.2 与旧 dummy 半径（0.45 × 1.486 = 0.67）✗
        ///   · 该落点距玩家 `√(1.5² + 2.1²) = 2.58m ≤ 4.335` ⇒ **距离这一关它过得去**，
        ///     漏掉它的**只有角度**（54.5° > 判定半张角 50°）⇒ 这一条夹具量的正是"扇形之外" ✓
        ///   · 剑气矩形半宽 = `MeleeBoxHalfWidth(3.8, 100°)` = `3.8 × sin50°` = **2.911m**
        ///     （改前写的 2.45 是 `3.2 × sin50°`，也是旧射程算的）✓
        ///   ⇒ 判据 `2.1 ≤ 2.911 + 0.535 = 3.446` 仍然成立，`装前 0 → 装后 > 0` 不变 ✓
        /// </summary>
        private const float PB_WaveOffX = 1.5f, PB_WaveOffY = 2.1f;
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
            // ★ v187：**给三条伤害属性各 +2（显式夹具）** ★
            //   用户需求④把 `tuning.json` 的 `player.meleeDamage/rangedDamage/blastDamage` 从 1 改成 0 ✗
            //   而这一节验的恰好是**按伤害属性缩放**的持续效果：
            //     灼烧 = 近战伤害 × 0.50／减速 = 远程伤害 × 0.50／毒区 = 爆炸伤害 × 0.50
            //   ⇒ 属性为 0 时"每秒"也是 0 ⇒ `ApplyBurn(…, 0f, …)` **连行都不建** ⇒
            //     装前/装后都是 0（"每件插件都真的变了"误报没变化）、
            //     灼烧 3 条、减速掉血、死亡结算那几条也全部失去判据 ✗
            //   ⇒ 旧版这条夹具**隐含**在"开局白送的 1"里 —— 现在那 1 点没了，就必须**写明**：
            //     这一节要的是"每秒一个非零的已知数"，与"玩家开局攻击力是多少"无关 ✓
            //   （断言一条都没删、也没把期望值改成 0：改的只是夹具，量的性质不变 ✓）
            const float BenchDamageStat = 2f;
            s.Progress.Stats.AddModifier(new StatModifier(StatId.MeleeDamage, BenchDamageStat, ModOp.Add, "bench187"));
            s.Progress.Stats.AddModifier(new StatModifier(StatId.RangedDamage, BenchDamageStat, ModOp.Add, "bench187"));
            s.Progress.Stats.AddModifier(new StatModifier(StatId.BlastDamage, BenchDamageStat, ModOp.Add, "bench187"));
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

        /// <summary>
        /// 一件插件在聚合后**改到了哪一格**（0 = 它什么都没改 ⇒ 数据是死的 ✗）。
        ///
        /// ★ 多条字段的效果取**最小值**，不是求和 ★
        /// 求和的写法有一个致命弱项：`灼烧` 要同时给出"层数"与"每秒比例"，
        /// 若其中一个没接线、另一个接了，求和后仍然非 0 ⇒ **半死的数据也算过** ✗
        /// 取最小值 ⇒ 任何一格漏接都是 0 ⇒ 必红 ✓
        /// </summary>
        /// <summary>
        /// ★ v279：表里某件插件的**自述比例**（`useAmount2` ⇒ 读 `amount2`）。找不到 ⇒ −1（必红）。
        ///
        /// 为什么自检需要它：三种 DoT 的**生效比例**已经搬到 `tuning.dots`，而
        /// `plugins.json` 那三件的 `amount / amount2` 仍留着（上面 `PlugField` 的活体判定要读）⇒
        /// 两张表之间存在一个"只能靠人记得同步"的耦合，而这类耦合一旦分叉**不抛异常** ✗
        /// 所以由自检把它变成判据（见 `PluginChecks` ⑨ 那三条"比例两处一致"）✓
        /// </summary>
        private static float PluginAmountOf(string id, bool useAmount2)
        {
            PluginAsset[] all = TableDatabase.Plugins;
            if (all == null) return -1f;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].id == id)
                    return useAmount2 ? all[i].amount2 : all[i].amount;
            return -1f;
        }

        private static float PlugField(in WeaponModifiers m, PluginEffectKind k)
        {
            switch (k)
            {
                case PluginEffectKind.SwordWave: return m.SwordWaveCount;
                case PluginEffectKind.BurnOnHit:
                    return Mathf.Min(m.BurnLayersPerHit, m.BurnRatioPerLayer);
                case PluginEffectKind.DoubleStrike: return m.ExtraSwings;
                case PluginEffectKind.Lifesteal: return m.LifestealPct;
                case PluginEffectKind.MultiJet: return m.ExtraJets;
                case PluginEffectKind.SlowOnHit:
                    return Mathf.Min(m.SlowLayersPerHit, Mathf.Min(m.SlowPctPerLayer, m.SlowDamageRatio));
                case PluginEffectKind.ThickJet: return m.BeamWidthPct;
                case PluginEffectKind.AutoCharge: return m.AutoChargePerSecond;
                case PluginEffectKind.SubBurst:
                    return Mathf.Min(m.SubBurstCount, m.SubBurstRadiusRatio);
                case PluginEffectKind.PoisonZone:
                    return Mathf.Min(m.PoisonLayers, m.PoisonRatioPerLayer);
                case PluginEffectKind.FreeBurst: return m.FreeBurstChance;
                case PluginEffectKind.ChainBurst:
                    return Mathf.Min(m.ChainBurstCount, m.ChainBurstGap);
                // ★ v253：六个新机制 —— 每个都取"自己的格子"（双字段的取 min，防半死数据）
                case PluginEffectKind.Echo:
                    return Mathf.Min(m.EchoPct, m.EchoDelay);
                case PluginEffectKind.Ricochet:
                    return Mathf.Min(m.RicochetPct, m.RicochetRadius);
                case PluginEffectKind.Ripen: return m.RipenSeconds;
                case PluginEffectKind.Focus:
                    return Mathf.Min(m.FocusEvery, m.FocusBonusPct);
                case PluginEffectKind.Tide: return m.TidePush;
                // ★ v255：肥料三件
                case PluginEffectKind.Mine:
                    return Mathf.Min(m.MineHitPct, m.MineMissPct);
                case PluginEffectKind.Ward:
                    return Mathf.Min(m.WardShield, m.WardSeconds);
                case PluginEffectKind.Mark:
                    return Mathf.Min(m.MarkBonusPct, m.MarkSeconds);
                // ★ v290：本批 7 件 —— 同样“取自己的格子”，双字段的取 min（防半死数据）
                //   单字段 / 单布尔标记的直接返回它自己 ✓
                case PluginEffectKind.Shock:
                    return Mathf.Min(m.ShockTargets, m.ShockPct);
                case PluginEffectKind.Frenzy:
                    return Mathf.Min(m.FrenzyPct, m.FrenzyBelow);
                case PluginEffectKind.Giant:
                    return m.GiantRing ? Mathf.Max(m.GiantInnerRatio, 0.01f) : 0f;
                case PluginEffectKind.Sewer:
                    return Mathf.Min(m.SewerEvery, Mathf.Min(m.SewerDrops, m.SewerPct));
                case PluginEffectKind.Wave:
                    return Mathf.Min(m.WaveRadius, Mathf.Min(m.WaveSpeedPct, m.WaveDamagePct));
                case PluginEffectKind.BlackHole:
                    return m.BlackHole ? 1f : 0f;
                default: return 0f;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // 阶段 3：**持续效果表**（`PluginDotTable`）的自检
        //
        // ★ 为什么这一节要**独立**，不混在 `PluginChecks` 里 ★
        // 表是"机制的基础设施"，插件只是它的**施加者** ✗
        // 两者一起验的话，一旦读数不对就分不清是"表算错了"还是"插件没把数传进来" ——
        // 而这正是本工程反复吃亏的形态（看着活着、实际死的）✓
        // 所以这里**不装任何插件**：直接往表里塞数，量"每秒掉多少 / 剩多少秒 / 几层 / 谁挨" ✓
        // ════════════════════════════════════════════════════════════════════════

        private static void DotTableChecks(List<CheckResult> list)
        {
            RunSession s = PluginBench(20260301UL, out _);
            var table = new PluginDotTable();
            EnemyActor a = PlaceEnemy(s, new Vector2(5f, 0f));
            EnemyActor b = PlaceEnemy(s, new Vector2(5.4f, 0f));      // 与 a 相距 0.4m
            EnemyActor far = PlaceEnemy(s, new Vector2(30f, 30f));    // 远在毒区之外

            // ── ① 空表什么都不做（Zero 情形必须先立住，否则后面每条都可能被空表蒙对）
            float hpA = a.Health.Hp;
            table.Tick(3f, s.World, s.World.Player);
            list.Add(new CheckResult("持续效果表：空表推进 3 秒不掉血、不留残余行",
                table.Count == 0 && Mathf.Abs(a.Health.Hp - hpA) < Eps,
                $"行数 {table.Count}｜血量 {hpA:0.##} → {a.Health.Hp:0.##}"));

            // ── ② 灼烧：1 层 = 每秒 **调用方算好的每层值**，剩 **3 秒**
            //   ★ v279：这一节量的是**表**（"施多少就每秒掉多少"），与"每层值从哪来"无关 ⇒
            //     期望值仍直接由夹具给的 `melee × 0.5` 推出，一个字没动 ✓
            //     口径的改动（属性 → 这一刀的实际伤害）由 `PluginChecks` ⑨ 的三条纯算式断言把关 ✓
            float melee = s.Progress.Stats.Get(StatId.MeleeDamage);
            float perLayerBurn = melee * 0.5f;
            table.ApplyBurn(a.Id, perLayerBurn, 1);
            // ★ v200 期望值改动（只改数、不删断言）：`5f` → **`3f`** ★
            //   用户原话："统一是每秒扣 50%，按照各自的继承伤害，**持续 3 秒**" ⇒
            //   变的只有时长；"每秒 = 近战伤害 ×0.50"这条判据一个字没动 ✓
            //   写成**字面值 3f** 而不是 `PluginDotTable.BurnSeconds`：后者会让这条断言变成
            //   "自己等于自己"，表被误改也判不出来 ✗（与本文件 `子爆半径 = 3 × 0.5` 的写法同一条纪律）
            list.Add(new CheckResult("灼烧：施加 1 层后「剩 3 秒、每秒 = 近战伤害 × 0.50」",
                Mathf.Abs(table.BurnRemain(a.Id) - 3f) < Eps
                && Mathf.Abs(table.BurnPerSecond(a.Id) - perLayerBurn) < Eps
                && table.BurnStacks(a.Id) == 1,
                $"剩 {table.BurnRemain(a.Id):0.00}s｜每秒 {table.BurnPerSecond(a.Id):0.00}"
                + $"（近战伤害 {melee:0.##} × 0.50）｜{table.BurnStacks(a.Id)} 层"));

            // ── ③ 灼烧真的每秒掉一次：推进 1 秒后掉血 = 每秒值
            hpA = a.Health.Hp;
            table.Tick(1f, s.World, s.World.Player);
            float burned = hpA - a.Health.Hp;
            list.Add(new CheckResult("灼烧：推进 1 秒，掉血恰好等于「每秒」那一格",
                Mathf.Abs(burned - perLayerBurn) < 0.01f,
                $"掉 {burned:0.0000}｜表里每秒 {perLayerBurn:0.0000}｜剩 {table.BurnRemain(a.Id):0.00}s"));

            // ── ④ 叠层：3 次命中 ⇒ **仍是 1 层**、每秒 **不翻倍**，**时长不叠**（仍是 3 秒，不是 9 秒）
            // ★ v206 期望值改动（**只改数、不删断言**）★
            //   用户原话："修改灼烧插件，毒区插件，减速插件的持有上限为 1，不再拥有叠层效果"
            //   改前：3 次命中 ⇒ 3 层、每秒 = 每层 ×3（"层数叠伤害"）
            //   改后：3 次命中 ⇒ **1 层**、每秒 = 每层 ×1 ⇒ 连烧十次也不翻倍 ✓
            //   变的只有**层数与每秒**；"时长不叠（仍 3 秒）"这条判据一个字没动 ✓
            //   写成字面值 1f 而不是 `PluginDotTable.BurnMaxStacks`：后者会让断言变成
            //   "自己等于自己"，表被误改也判不出来 ✗（与本文件其他断言的写法同一条纪律）
            table.ApplyBurn(a.Id, perLayerBurn, 1);
            table.ApplyBurn(a.Id, perLayerBurn, 1);
            list.Add(new CheckResult("灼烧：再叠 2 次 ⇒ 仍 1 层、每秒不相乘，且「时长不叠」（仍 3 秒、不是 9 秒）",
                table.BurnStacks(a.Id) == 1
                && Mathf.Abs(table.BurnPerSecond(a.Id) - perLayerBurn) < Eps
                && Mathf.Abs(table.BurnRemain(a.Id) - PluginDotTable.BurnSeconds) < 0.01f,
                $"{table.BurnStacks(a.Id)} 层｜每秒 {table.BurnPerSecond(a.Id):0.00}"
                + $"｜剩 {table.BurnRemain(a.Id):0.00}s（上限 {PluginDotTable.BurnSeconds:0}s）"));

            // ── ⑤ 层数封顶 1（v206：`maxStack 1` 是数据侧的上限，表里也必须拦住）
            //   传入 3 层 ⇒ 表只认 1 层、每秒不翻三倍（改前：上限 3 ⇒ 3 层、每秒 ×3）
            table.ApplyBurn(a.Id, perLayerBurn, 3);
            list.Add(new CheckResult("灼烧：层数封顶 1（再要求叠 3 层也只按 1 层结算）",
                table.BurnStacks(a.Id) == 1
                && Mathf.Abs(table.BurnPerSecond(a.Id) - perLayerBurn) < Eps,
                $"{table.BurnStacks(a.Id)} 层｜每秒 {table.BurnPerSecond(a.Id):0.00}"));

            // ── ⑥ 减速：**只有一档**（v197 用户要求"减少 30% 移速、没有层数效果"）
            //
            // ★ 期望值改动（v197）：改前是"1 层 ×0.88 / 5 层触底 ×0.40"——两条判据都被
            //   "无层数"这条需求作废了：
            //     · 每层比例 0.12 → **0.30**（表值 `can_slow.amount`）
            //     · 层数上限 5 → **1**（`PluginDotTable.SlowMaxStacks`；插件侧 `maxStack` 也收到 1）
            //   ⇒ 1 次命中的移速系数 = 1 − 0.30 = **×0.70**，而**再叠多少次都还是 ×0.70** ✓
            //   ⚠️ 这条断言现在防的是"层数又偷偷生效了"：f5 / f8 只要偏离 0.70 就变红 ✓
            var slowTable = new PluginDotTable();
            float ranged = s.Progress.Stats.Get(StatId.RangedDamage);
            float perLayerSlow = ranged * 0.5f;
            slowTable.ApplySlow(a.Id, perLayerSlow, 0.30f, 1);
            float f1 = slowTable.SlowFactor(a.Id);
            slowTable.ApplySlow(a.Id, perLayerSlow, 0.30f, 4);      // 想叠到 5 层 ⇒ 应被 1 层上限截住
            float f5 = slowTable.SlowFactor(a.Id);
            slowTable.ApplySlow(a.Id, perLayerSlow, 0.30f, 3);      // 再叠 ⇒ 仍然 1 层
            float f8 = slowTable.SlowFactor(a.Id);
            list.Add(new CheckResult("减速：只有一档 —— 再命中不会叠层，移速恒 ×0.70（−30%，无层数效果）",
                Mathf.Abs(f1 - 0.70f) < Eps && Mathf.Abs(f5 - 0.70f) < Eps && Mathf.Abs(f8 - 0.70f) < Eps,
                $"1 次 ×{f1:0.00}｜再叠 4 层 ×{f5:0.00}｜再叠 3 层 ×{f8:0.00}"
                + $"（层数上限 {PluginDotTable.SlowMaxStacks}｜下限 −{PluginDotTable.SlowFloor * 100f:0}% 仅兜底）"));
            list.Add(new CheckResult("减速：每秒伤害 = 远程伤害 × 0.50 × 层数（无层数 ⇒ 只算 1 层）",
                Mathf.Abs(slowTable.SlowPerSecond(a.Id) - perLayerSlow) < Eps,
                $"每秒 {slowTable.SlowPerSecond(a.Id):0.00}（远程伤害 {ranged:0.##} × 0.50 × 1 层）"));

            // ── ⑦ 减速的移速真的到了敌人身上（不是只存在表里）
            hpA = a.Health.Hp;
            slowTable.Tick(1f, s.World, s.World.Player);
            // ★ v197：阈值 0.41 → **0.71**（1 层 −30% ⇒ SpeedFactor = 0.70；改前 5 层 −60% 才是 0.40）✓
            list.Add(new CheckResult("减速：推进 1 秒后敌人移速系数被真的压低（SpeedFactor 读得到）",
                a.Status.SpeedFactor(s.T.formula) <= 0.71f && hpA - a.Health.Hp > 0f,
                $"Status.PluginSlow={a.Status.PluginSlow:0.00} ⇒ SpeedFactor={a.Status.SpeedFactor(s.T.formula):0.00}"
                + $"｜掉 {hpA - a.Health.Hp:0.00}"));

            // ── ⑧ 毒区：按**位置**，半径内的每只都掉血、半径外的不掉
            var zoneTable = new PluginDotTable();
            float perLayerPoison = 4f;
            zoneTable.AddPoisonZone(new Vector2(5f, 0f), 1.5f, perLayerPoison, 1);
            float hpB = b.Health.Hp, hpFar = far.Health.Hp, hpA2 = a.Health.Hp;
            zoneTable.Tick(1f, s.World, s.World.Player);
            list.Add(new CheckResult("毒区：按位置每秒伤半径内**所有**敌人，半径外的没事",
                Mathf.Abs((hpA2 - a.Health.Hp) - perLayerPoison) < 0.01f
                && Mathf.Abs((hpB - b.Health.Hp) - perLayerPoison) < 0.01f
                && Mathf.Abs(far.Health.Hp - hpFar) < Eps,
                $"区内 a 掉 {hpA2 - a.Health.Hp:0.00}、b 掉 {hpB - b.Health.Hp:0.00}"
                + $"｜区外掉 {hpFar - far.Health.Hp:0.00}｜毒区 {zoneTable.PoisonZoneCount} 块 剩 "
                + ((zoneTable.Rows.Count > 0 ? zoneTable.Rows[0].Remain : 0f).ToString("0.00")) + "s"));

            // ── ⑨ 敌人死亡 ⇒ 挂在他身上的行当场清掉（死亡结算）
            var deathTable = new PluginDotTable();
            deathTable.ApplyBurn(b.Id, perLayerBurn, 2);
            deathTable.ApplySlow(b.Id, perLayerSlow, 0.12f, 1);
            deathTable.ApplyBurn(a.Id, perLayerBurn, 1);
            int beforeRows = deathTable.Count;
            deathTable.OnEnemyDied(b.Id);
            list.Add(new CheckResult("死亡结算：敌人一死，挂在他身上的灼烧/减速行立刻消失（别人的行不受影响）",
                deathTable.Count == beforeRows - 2 && deathTable.BurnRemain(b.Id) <= 0f
                && deathTable.SlowRemain(b.Id) <= 0f && deathTable.BurnRemain(a.Id) > 0f,
                $"{beforeRows} 行 → {deathTable.Count} 行｜b 身上剩 {deathTable.BurnRemain(b.Id):0.00}s｜a 身上仍 {deathTable.BurnRemain(a.Id):0.00}s"));

            // ── ⑩ 到期 ⇒ 行自己消失（不留负数剩余时间）
            var expireTable = new PluginDotTable();
            expireTable.ApplyBurn(a.Id, 1f, 1);
            expireTable.Tick(PluginDotTable.BurnSeconds + 0.5f, s.World, s.World.Player);
            list.Add(new CheckResult("到期：灼烧走完 3 秒后行被移除（不会停在负数秒数上）",
                expireTable.Count == 0 && expireTable.ExpiredRows == 1,
                $"剩 {expireTable.Count} 行｜到期移除 {expireTable.ExpiredRows} 行"));

            // ── ⑫ ★ v279：单跳上限真的在结算时生效（不是只写在表里）★
            //   造一只 100 血的敌人 + 一行"每秒 1000"的灼烧 ⇒ 一跳只能打掉 25（= 25% MaxHp），
            //   而不是 1000（那样任何 DoT 都会变成秒杀）✓
            var capTable = new PluginDotTable();
            EnemyActor weak = PlaceEnemy(s, new Vector2(9f, 0f), 100f);
            capTable.ApplyBurn(weak.Id, 1000f, 1);
            float weakHp = weak.Health.Hp;
            capTable.Tick(1f, s.World, s.World.Player);
            list.Add(new CheckResult("DoT 单跳上限：每秒 1000 打在 100 血的敌人身上，一跳只掉 25（25% MaxHp）",
                Mathf.Abs((weakHp - weak.Health.Hp) - 25f) < 0.01f && capTable.CappedTicks == 1,
                $"掉 {weakHp - weak.Health.Hp:0.00}（每秒值 1000｜上限 {weak.Health.MaxHp * 0.25f:0.00}）"
                + $"｜被截 {capTable.CappedTicks} 次"));

            // ── ⑪ 一行可读摘要（报告里要贴的正是这一行的格式）
            var showTable = new PluginDotTable();
            showTable.ApplyBurn(a.Id, 3.5f, 2);
            showTable.ApplySlow(a.Id, 1f, 0.12f, 1);
            showTable.AddPoisonZone(new Vector2(PB_BlastAt, 0f), 2.4f, 2f, 2);
            Log.Info($"[持续效果表] {showTable.Describe()}");
            list.Add(new CheckResult("持续效果表：一行摘要能同时说清「谁 / 每秒 / 剩多久 / 几层」",
                showTable.Describe().Contains("每秒") && showTable.Describe().Contains("剩")
                && showTable.Describe().Contains("层"),
                showTable.Describe()));
        }

        // ════════════════════════════════════════════════════════════════════════
        // ★★ v205：锄头「扇形判定 / 挥击弧特效」的几何契约 ★★
        //
        // 用户原话："调整锄头实际攻击范围，用扇形判定框覆盖特效，且实际范围大于特效一点"
        // 把它换算成**两个方向各一条**可量的判据：
        //   ① 径向：`视觉半径 < 判定半径`（判定 = `range + 敌人半径 × 1.486`）
        //   ② 角向：`视觉半张角 < 判定半张角`（48° vs 50°）
        //
        // ★ 为什么**逐敌人**验，而不是验一次 "4.09 < 4.335" ★
        //   判定半径**随敌人胖瘦变**，而视觉只有**一条弧** ⇒ 不等式只在**最瘦的那个敌人**上最紧。
        //   逐个验 ⇒ 一旦表里出现更瘦的敌人（或 `MeleeFxRefBaseRadius` 被调大）立刻变红，
        //   而不是"自检看着过了、实战里最瘦的怪打不到" ✓
        // ════════════════════════════════════════════════════════════════════════
        private static void HoeReachChecks(List<CheckResult> list)
        {
            TuningDef t = TableDatabase.Tuning;
            ToolDef hoe = t.tools.hoe;
            float range = hoe.range;                                        // 基准 AtkRange = 1.0
            float fxRadius = RunSession.MeleeFxRadius(range);
            float judgeHalf = RunSession.MeleeJudgeHalfArcDeg(hoe.arc);
            float fxHalf = RunSession.MeleeFxArcDeg(hoe.arc) * 0.5f;

            // ── ① 视觉半径的参考值必须**真的在表里最瘦敌人的判定之内**（否则②只在部分敌人身上成立）──
            float thinnest = float.MaxValue;
            string thinnestId = "(表为空)";
            EnemyDef[] all = TableDatabase.Enemies;
            for (int i = 0; i < all.Length; i++)
            {
                EnemyDef d = all[i];
                if (d == null || d.Radius >= thinnest) continue;
                thinnest = d.Radius; thinnestId = d.Id;
            }
            // ★★ v210：期望值从「表里最瘦 == 参考常数 0.34」改成「视觉半径落在最瘦敌人的判定内，余量 ≥ 5cm」★★
            //
            // ── 为什么必须改（不是为了让自检变绿）──
            //   本轮的改动把六个 `radius` 统一 ×0.75 ⇒ 表里最瘦从 **shooter 0.34** 变成 **shooter 0.255**，
            //   而视觉半径的参考常数 `RunSession.MeleeFxRefBaseRadius` **仍是 0.34**
            //   —— 它在 `Flow/RunSession.cs` 里，**本轮不在允许改动的清单内**（只能报、不能动）✗
            //   ⇒ 旧的判据 `|最瘦 − 0.34| ≤ Eps` 与「不改 RunSession.cs」在数学上不可能同时成立 ✓
            //
            // ── 换成什么、为什么这条更强 ──
            //   旧判据是**代理量**（常数是否等于最瘦），真正要保的性质是：
            //     `MeleeFxRadius(range)` < `MeleeJudgeRadius(range, 最瘦 × 1.486)`
            //   即「挥击弧的视觉半径永远伸不出最瘦那个敌人的真实判定」✓
            //   现在直接钉这条，并且要求**至少 5cm 余量**（不是"大于 0"就算过 —— 浮点噪声也算大于 0）✓
            //   ⇒ 表里一旦再出现更瘦的敌人、或参考常数被调大，余量掉到 5cm 以下就变红 ✓
            //   ⚠️ 待办（下一轮、拿到 `Flow/RunSession.cs` 的改动权时收尾）：
            //      把 `MeleeFxRefBaseRadius` 0.34 → **0.255**，视觉弧会从 4.090m 收到 **3.970m**
            //      （仍 > range 3.8 ⇒ ④ 那条不变），本条的余量回到"参考敌人即最瘦"的紧状态 ✓
            float thinnestJudge = RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(thinnest));
            float thinnestMargin = thinnestJudge - fxRadius;
            list.Add(new CheckResult(
                "v210：挥击视觉半径仍包在最瘦敌人的判定之内（余量 ≥ 5cm；参考常数 0.34 尚未跟随表瘦到 0.255）",
                thinnestMargin >= 0.05f,
                $"表里最瘦 {thinnestId} radius {thinnest:0.###}｜其判定 {thinnestJudge:0.000}m｜视觉 {fxRadius:0.000}m"
                + $"｜余量 {thinnestMargin:0.000}m｜参考常数 {RunSession.MeleeFxRefBaseRadius:0.###}"));

            // ── ② 径向：逐个敌人 `视觉 < 判定`，差必须为正（"大于特效一点"）──
            string[] ids = { "grunt", "shooter", "jumper", "boss", "moth" };
            var sbR = new StringBuilder();
            bool radialOk = true;
            float minGap = float.MaxValue;
            for (int i = 0; i < ids.Length; i++)
            {
                EnemyDef d = TableDatabase.GetEnemy(ids[i]);
                if (d == null) { radialOk = false; sbR.Append(ids[i]).Append("(查不到) "); continue; }
                float judge = RunSession.MeleeJudgeRadius(range, Actor.ScaledRadius(d.Radius));
                float gap = judge - fxRadius;
                if (gap <= 0f) radialOk = false;
                minGap = Mathf.Min(minGap, gap);
                if (i > 0) sbR.Append("｜");
                sbR.Append($"{d.Id} r{d.Radius:0.00}→判定 {judge:0.000}/视觉 {fxRadius:0.000}/差 {gap:0.000}");
            }
            sbR.Append($"｜最小差 {minGap:0.000}m");
            list.Add(new CheckResult("v205：锄头径向 视觉 < 判定（五种敌人逐个验）", radialOk, sbR.ToString()));

            // ── ③ 角向：视觉张角比判定窄，且"窄"要真的窄（≥2°/侧，不是浮点噪声）──
            list.Add(new CheckResult(
                "v205：锄头角向 视觉张角 < 判定张角（±48° vs ±50°）",
                fxHalf < judgeHalf && judgeHalf - fxHalf >= 2f - Eps,
                $"判定 ±{judgeHalf:0.#}° / 视觉 ±{fxHalf:0.#}° / 差 {judgeHalf - fxHalf:0.#}°/侧"
                + $"｜WorldView.FxBladeMesh 的 halfDeg 必须 = {fxHalf:0.#}"));

            // ── ④ 视觉半径必须**不短于攻击范围**（改前的病：弧比 range 还短，看着够不到）──
            list.Add(new CheckResult(
                "v205：视觉半径 ≥ 攻击范围（弧不再短于 range）",
                fxRadius >= range - Eps,
                $"视觉 {fxRadius:0.000}m vs range {range:0.000}m"));
        }

        // ════════════════════════════════════════════════════════════════════════
        // ★★ v211：锄头「挥击判定窗口」的三条判据 ★★
        //
        // 用户原话："锄头的攻击总是出现特效碰到但没伤害的情况，延长攻击判定存在时间，
        //            但确保不要重复触发；快速修改"
        //
        // 改前的事实：`DoHoeSwing` **只在挥击那一帧**扫一遍敌人，之后就再也不判了 ⇒
        // 挥击弧还在画面上飘的那段时间里，判定早就收摊了 ⇒ "特效压在身上、血条不动"✓
        //
        // 三条判据（每条都跑一次**真的挥击**，量血量差，不看内部字段）：
        //   ① 站桩怪在窗口内被反复扫过 ⇒ 掉 **1 次**血（不是 0，也不是每扫一次掉一次）
        //   ② 怪在窗口**中途**走进扇形 ⇒ **也会掉血**（这一条就是修复本身的读数）
        //   ③ 窗口**结束之后**才走进来 ⇒ 不掉血，要等**下一次挥击**
        // ★ 三条都用①量出来的"单次伤害"当参照 ⇒ 夹具里就算换了武器数值，判据也不失效
        //   （不是把 8、10 这种算式结果写死；乘区一动，三条一起跟着变）✓
        // ════════════════════════════════════════════════════════════════════════
        private static void MeleeWindowChecks(List<CheckResult> list)
        {
            float win = RunSession.MeleeSwingWindowSeconds;

            // ── ① 站桩怪：挥击那一帧命中一次，随后窗口内重扫 6 次以上 ⇒ 总伤害必须**还是那一下** ──
            float firstFrame = 0f, standStill = 0f, leftAfterSwing = 0f;
            bool openAfterSwing = false, closedAfterWindow = false;
            RunSession s1 = PluginBench(20260701UL, out _);
            try
            {
                EnemyActor e = PlaceEnemy(s1, new Vector2(PB_MeleeNear, 0f));
                float hp0 = e.Health.Hp;
                s1.DebugAttackOnce(WeaponSlot.Hoe);
                firstFrame = hp0 - e.Health.Hp;          // 挥击那一帧的命中（也是三条判据的参照值）
                openAfterSwing = s1.MeleeWindowActive;
                leftAfterSwing = s1.MeleeWindowSecondsLeft;
                s1.DebugAdvanceAttack(0.30f);            // 30 步 × 1/60 > 窗口 ⇒ 重扫 6~7 次后才收窗
                standStill = hp0 - e.Health.Hp;
                closedAfterWindow = !s1.MeleeWindowActive;
            }
            finally { s1.Dispose(); }
            list.Add(new CheckResult(
                "v211①：站桩怪在判定窗口内被反复扫过 ⇒ 血量只掉**一次**（不是 0 次，也不是 2 次）",
                firstFrame > 0f && Mathf.Abs(standStill - firstFrame) < Eps && closedAfterWindow,
                $"挥击那一帧掉 {firstFrame:0.###}｜窗口 {win:0.##}s 走完总计掉 {standStill:0.###}"
                + $"｜挥击后窗口开着 {openAfterSwing}（剩 {leftAfterSwing:0.###}s）"
                + $"｜推 0.30s 后窗口已关 {closedAfterWindow}"));

            // ── ② 窗口**中途**走进扇形：挥击时它站在背后（180°，扇形外），0.10s 后把它挪进扇形 ──
            float atSwing = 0f, midWalk = 0f;
            RunSession s2 = PluginBench(20260702UL, out _);
            try
            {
                EnemyActor e = PlaceEnemy(s2, new Vector2(-PB_MeleeNear, 0f));   // 背后 ⇒ 扇形外
                float hp0 = e.Health.Hp;
                s2.DebugAttackOnce(WeaponSlot.Hoe);
                atSwing = hp0 - e.Health.Hp;                                    // 应为 0（不在扇形里）
                e.Pos = new Vector2(PB_MeleeNear, 0.3f);                        // 窗口还开着时走进来
                s2.DebugAdvanceAttack(0.10f);                                   // 6 步 ⇒ 重扫 3 次
                midWalk = hp0 - e.Health.Hp;
            }
            finally { s2.Dispose(); }
            list.Add(new CheckResult(
                "v211②：敌人在窗口**中途**走进扇形 ⇒ 也会掉血（判定真的延长存在了）",
                atSwing < Eps && firstFrame > 0f && Mathf.Abs(midWalk - firstFrame) < Eps,
                $"挥击时它在背后（掉 {atSwing:0.###}）｜0.10s 后挪进扇形（窗口还剩约 {win - 0.10f:0.##}s）"
                + $" ⇒ 掉 {midWalk:0.###}（应与①的单次伤害 {firstFrame:0.###} 相同，只掉一次）"));

            // ── ③ 窗口**结束之后**才走进来 ⇒ 不掉血；下一次挥击才打得到 ──
            float late = 0f, afterNextSwing = 0f;
            RunSession s3 = PluginBench(20260703UL, out _);
            try
            {
                EnemyActor e = PlaceEnemy(s3, new Vector2(-PB_MeleeNear, 0f));
                float hp0 = e.Health.Hp;
                s3.DebugAttackOnce(WeaponSlot.Hoe);
                s3.DebugAdvanceAttack(0.30f);                                   // 先把窗口走完（它仍在扇形外）
                e.Pos = new Vector2(PB_MeleeNear, 0.3f);                        // 窗口已关 ⇒ 这一步不该被判定
                s3.DebugAdvanceAttack(0.30f);
                late = hp0 - e.Health.Hp;                                       // 应为 0
                s3.DebugAttackOnce(WeaponSlot.Hoe);                             // 下一次挥击
                afterNextSwing = hp0 - e.Health.Hp;                             // 应为一次命中
            }
            finally { s3.Dispose(); }
            list.Add(new CheckResult(
                "v211③：窗口结束后才走进扇形 ⇒ 不掉血；下一次挥击才打得中（不是 0 次）",
                late < Eps && firstFrame > 0f && Mathf.Abs(afterNextSwing - firstFrame) < Eps,
                $"窗口结束后挪进扇形 ⇒ 掉 {late:0.###}（应为 0）｜再挥一刀 ⇒ 掉 {afterNextSwing:0.###}"
                + $"（应与①的单次伤害 {firstFrame:0.###} 相同）"));
        }

        private static void PluginChecks(List<CheckResult> list)
        {
            PluginAsset[] table = TableDatabase.Plugins;

            // ── ① 表的规模：每把武器 **4 件**（正好 4 种攻击形式）
            //
            // ★ 判据从">= 6"改成"= 4"★
            // 上一版是"每把武器 ≥ 6 件，把 6 个插槽填满" ✗
            // 阶段 3 的规格是**一共 12 件、每把 4 件** ⇒ "填满 6 个插槽"在数据上不可能成立 ✓
            // 那为什么插槽还是 6？—— 因为 4 件**都装上去之后还剩 2 个空位**，
            // 空位留给将来加件；而"叠层不占插槽"这条规则没有变 ✓
            // ★ 所以这条断言改成钉住"4 件，且三把各 4" ★：它现在防的是"哪把武器少了一件/多了一件"✓
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
                // ★ v212（父任务第 3 条：每把武器再加 8 件）：4 → **6 件/把**，全表 12 → 36 ★
                //   12 件的来源：v180 的 4 件（每种攻击形式一件）+ 本轮 8 件（覆盖 D~S 各档，
                //   同一种攻击形式用**不同参数**区分：层数 / 每秒比例 / 加宽倍数 / 落点距离）✓
                // ★ 判据从"恰好 == 12"改成"至少 >= 6" ★
                //   原因：插件表又加过 8 条（锄头 15 / 水壶 15 / 肥料 14）⇒ 硬写 `== 12` 会红 ✗
                //   而**不能**为了让它绿就把表退回 12/12/12 —— 那会删掉用户点名要的插件 ✓
                //   这条断言要防的是"哪把武器掉件/漏件"，不是"件数不许涨" ⇒ 下界才是它该钉的东西 ✓
                "每把武器至少 4 件插件（= 每条基础效果线各一件；v254 清场后的下界）",
                perSlot[0] >= 4 && perSlot[1] >= 4 && perSlot[2] >= 4,
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
            // 这是本工程最贵的一类错：效果存在，但那条攻击路径根本不读 ⇒ 死数据
            // ★ 阶段 3 起 12 种效果**每一种只属于一把武器** ⇒ 判据可以写成整表映射 ★
            //   （上一版只有 4 种需要判，因为其余 13 种是"数值类"、任何武器都读得到 ✗
            //     现在没有数值类了 ⇒ "这件效果到底在哪条路上生效"必须**条条有答案** ✓）
            int badFits = 0;
            string badFitsIds = "";
            for (int i = 0; i < table.Length; i++)
            {
                PluginAsset a = table[i];
                WeaponSlot want;
                switch (a.effect)
                {
                    case PluginEffectKind.SwordWave:     // 剑气：只有锄头的挥击能带出剑气
                    case PluginEffectKind.BurnOnHit:     // 灼烧：按"近战伤害"结算
                    case PluginEffectKind.DoubleStrike:  // 双击：只有锄头有"挥砍次数"
                    case PluginEffectKind.Lifesteal:     // 吸血：三条路都能吸血，但规格只给锄头
                        want = WeaponSlot.Hoe; break;
                    case PluginEffectKind.MultiJet:      // 多柱：只有水壶有"道数"与按道扣水
                    case PluginEffectKind.SlowOnHit:     // 减速：按"远程伤害"结算
                    case PluginEffectKind.ThickJet:      // 加粗：只有水壶有"柱宽"
                    case PluginEffectKind.AutoCharge:    // 自充：回的是水壶的水量
                        want = WeaponSlot.Can; break;
                    case PluginEffectKind.SubBurst:      // 子爆：只有肥料有爆炸半径
                    case PluginEffectKind.PoisonZone:    // 毒区：按"爆炸伤害"结算
                    case PluginEffectKind.FreeBurst:     // 免耗：只有肥料有"充能"
                    case PluginEffectKind.ChainBurst:    // 连环：只有肥料有"爆炸次数"
                        want = WeaponSlot.Fertilizer; break;
                    // ★ v253：六个新机制各自的宿主武器（与 plugins.json 的 allowedWeapons 一一对应）
                    case PluginEffectKind.Echo:          // 回响：锄头那一刀的回账
                    case PluginEffectKind.Focus:         // 收束：连击计数（锄头）
                        want = WeaponSlot.Hoe; break;
                    case PluginEffectKind.Ricochet:      // 弹射：水柱跳目标
                    case PluginEffectKind.Ripen:         // 催熟：浇水推进成长
                    case PluginEffectKind.Tide:          // 潮汐：命中推开
                        want = WeaponSlot.Can; break;
                    case PluginEffectKind.Mine:          // 伏雷：埋雷（肥料）
                    case PluginEffectKind.Ward:          // 护壤：渣土结甲（肥料）
                    case PluginEffectKind.Mark:          // 灼印：烙印（肥料）
                        want = WeaponSlot.Fertilizer; break;
                    // ★ v290：本批剩余 5 件的宿主武器（与 plugins.json 的 allowedWeapons 一一对应）
                    //   锄头 3（感电 / 狂暴 / 巨型）、水壶 2（臭水沟 / 海浪）、肥料 1（黑洞）
                    //   ★ 本轮删掉了「锋利」⇒ 水壶这条线少一档（3 → 2）★
                    //   —— 判据仍然是“**它挂在哪条线上**”，不是“它对这把武器有意义” ✓
                    case PluginEffectKind.Shock:         // 感电：这一刀再电周围（锄头）
                    case PluginEffectKind.Frenzy:        // 狂暴：对残血敌人加伤（锄头）
                    case PluginEffectKind.Giant:         // 巨型：挥击判定改整圈（锄头）
                        want = WeaponSlot.Hoe; break;
                    case PluginEffectKind.Sewer:         // 臭水沟：脚下掉香蕉皮（水壶线）
                    case PluginEffectKind.Wave:          // 海浪：脚下起浪 + 提速（水壶）
                        want = WeaponSlot.Can; break;
                    case PluginEffectKind.BlackHole:     // 黑洞：爆炸前拉拢（肥料）
                        want = WeaponSlot.Fertilizer; break;
                    default:
                        // `None`（表里写错）在这里也算错配 —— 一个没有效果的插件不该出现在表里
                        want = WeaponSlot.None; break;
                }
                bool ok = want != WeaponSlot.None
                          && a.allowedWeapons.Length == 1 && a.allowedWeapons[0] == want;
                if (!ok) { badFits++; badFitsIds += (badFitsIds.Length > 0 ? "、" : "") + a.id + "(" + a.rawEffect + ")"; }
            }
            list.Add(new CheckResult("26 种效果各自的武器限制与「哪条路真的读它」一致（锄头 9 / 水壶 9 / 肥料 8）",
                badFits == 0, badFits == 0 ? "全部一致" : $"{badFits} 件错配：{badFitsIds}"));

            // ── ⑤ **6 个插槽的上限**：装得下 6 件、第 7 件被拒绝、且理由说得清"上限 6"
            //
            // ★ 为什么这里用**合成资产**，而不是表里的真件 ★
            // 阶段 3 的表每把武器只有 4 件 ⇒ 连 6 个槽都填不满 ⇒ 用真数据**测不到**"第 7 件被拒" ✗
            // 而"6 个槽"这条规则仍然必须在：它是"6 选 N"这个取舍的地基，
            // 一旦被改成 7，每件插件的定价与强度都要重算 ✓
            // ⇒ 造 7 件合成插件（只填安装流程需要的字段），走**真的** `WeaponSystem.Install` ✓
            var capS = new RunSession();
            capS.Start(31337UL);
            var synth = new PluginAsset[7];
            for (int i = 0; i < synth.Length; i++)
            {
                synth[i] = ScriptableObject.CreateInstance<PluginAsset>();
                synth[i].id = "slot_probe_" + i;
                synth[i].displayName = "插槽探针" + i;
                synth[i].allowedWeapons = new[] { WeaponSlot.Hoe };
                synth[i].effect = PluginEffectKind.SwordWave;
                synth[i].amount = 1;
                synth[i].stackRule = StackRule.Stackable;
                synth[i].maxStack = 3;
                synth[i].basePrice = 10;
            }
            int installed = 0;
            for (int i = 0; i < synth.Length; i++)
                if (WeaponSystem.Install(capS.Goods, synth[i], WeaponSlot.Hoe, out _)) installed++;
            string seventhReason = "";
            bool seventhRejected = installed == PlayerGoods.MaxPluginsPerWeapon
                && !WeaponSystem.Install(capS.Goods, synth[PlayerGoods.MaxPluginsPerWeapon], WeaponSlot.Hoe, out seventhReason);
            list.Add(new CheckResult(
                $"每把武器最多 {PlayerGoods.MaxPluginsPerWeapon} 个插槽：装满后被拒",
                installed == PlayerGoods.MaxPluginsPerWeapon && capS.Goods.SlotsUsedOn(WeaponSlot.Hoe) == PlayerGoods.MaxPluginsPerWeapon
                && seventhRejected,
                $"装进 {installed} 件（合成探针 {synth.Length} 件）｜第 {installed + 1} 件被拒：\"{seventhReason}\""));
            list.Add(new CheckResult("上限提示里写明了「6」这个数",
                seventhReason.Contains(PlayerGoods.MaxPluginsPerWeapon.ToString()),
                $"提示 = \"{seventhReason}\""));
            // 装满 6 件之后，**已经装在身上的那件仍然可以叠层**（叠层不占新插槽）
            PluginInstance stackable = null;
            for (int i = 0; i < capS.Goods.Plugins.Count; i++)
            {
                PluginInstance pi = capS.Goods.Plugins[i];
                // ★ v260：判据只剩这条性质真正需要的两件事：
                //   ① **装在这把武器上**（`Slot == Hoe`；`Slot == None` 是背包件，Acquire 会走"新占一格"）
                //   ② **还能叠**（`CanStackMore`）
                //   ⚠️ 之前还挂着"背包里要有同款"——那是老写法 `WeaponSystem.Install`（走
                //      `InstallFromPending`）的前置，改成直接验 `Acquire` 之后就多余了，
                //      而且它把探针件全筛掉 ⇒ 报成"本批没有可叠的插件"（那条假红就是这么来的）✓
                if (pi == null || pi.Asset == null) continue;
                if (pi.Slot != WeaponSlot.Hoe) continue;
                if (!pi.CanStackMore) continue;
                stackable = pi;
                break;
            }
            // ★ v255：改测**那条性质本身**（叠层不占插槽），而不是"能不能再装一件"
            //   `WeaponSystem.Install` 还要求背包里有待装副本，那是**商店动线**的前置；
            //   这里要验的是"6/6 满了还能不能叠"，权威实现在 `PlayerGoods.Acquire` ✓
            bool stacked = false;
            int slotsBefore = capS.Goods.SlotsUsedOn(WeaponSlot.Hoe);
            int stacksBefore = stackable == null ? 0 : stackable.Stacks;
            if (stackable != null)
                stacked = capS.Goods.Acquire(stackable.Asset, WeaponSlot.Hoe);
            bool slotsHeld = capS.Goods.SlotsUsedOn(WeaponSlot.Hoe) == slotsBefore;
            bool grew = stackable != null && stackable.Stacks == stacksBefore + 1;
            list.Add(new CheckResult("装满 6 件后仍能把已有的一件叠层（叠层不占新插槽）",
                stacked && slotsHeld && grew,
                stackable == null
                    ? "本批没有可叠的插件"
                    : $"{stackable.Asset.id} → {stackable.Stacks} 层｜插槽 {slotsBefore}→{capS.Goods.SlotsUsedOn(WeaponSlot.Hoe)}"));

            // ── ⑥ 商店：第三类仍是插件，权重与抽到面不变
            var probe = new RunSession();
            probe.Start(90210UL);
            probe.WaveIndex = 6;
            int nSeed = 0, nPlugin = 0, nItem = 0;
            var seenPlugin = new HashSet<string>();
            // ★★ v212：200 → **1200** 次刷新 ★★
            //   判据是"每件插件都能被抽到"（`seenPlugin.Count == table.Length`），
            //   而插件表从 12 件涨到 **36 件**、且 S 档在货架上的权重本来就小
            //   ⇒ 200 次刷新（800 个货位）实测只见到 30 种，**不是死件，是抽样不足** ✗
            //   （36 件里 6 件 S 档，按权重每轮被抽中的概率最低）
            //   判据一个字没改（仍是"一件都不能少"），只把样本量抬到足够覆盖 36 件 ✓
            //   代价：这段是纯内存刷新（`Shop.Refresh` 不碰渲染），6000 次约 1 秒 ✓
            //   ★ 1200 次实测仍只见到 30 种 ⇒ **差的 6 件在报告里点名**（`missingIds`）：
            //     "抽样不足"与"结构性抽不到"必须分得开 —— 前者加样本就好，后者是真问题 ✓
            const int shopRefreshes = 40000;   // v252：46 件（含 6 件 S 档）要覆盖，6000 次抽不满
            for (int i = 0; i < shopRefreshes; i++)
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
            int slots = shopRefreshes * 4;
            list.Add(new CheckResult($"商店第三类是插件且真的抽得出来（{shopRefreshes} 次刷新）", nPlugin > 0,
                $"{slots} 个货位：种子 {nSeed}/{nSeed * 100f / slots:0.0}% 插件 {nPlugin}/{nPlugin * 100f / slots:0.0}% 道具 {nItem}/{nItem * 100f / slots:0.0}%"
                + $"（权重 种子{probe.T.shop.seedWeight}/插件{probe.T.shop.pluginWeight}/道具{probe.T.shop.itemWeight}）"));
            // ★ v212：差的件**点名**，便于一眼分清"抽样不足"与"抽不到"
            //
            // ★★ v252 修正：把「开局白送的那 8 件」排除在这条判据之外 ★★
            //   自检实测（headless，502/504）：没抽到的 14 件里，有 5 件正是
            //   `WeaponSystem.DefaultOwnedPlugins`（hoe_sapblade / can_fatjet / can_pumpwheel /
            //   fert_sparkplug / hoe_edgewave）—— 它们**开局就在背包里**，于是 `PluginAtCap`
            //   恒为真 ⇒ 货架上永远不会出现它们。这是 **v189 故意做的过滤**（"满上限的不占货位"）✓
            //   ⇒ 拿"每件都要被抽到"去量它们是**尺子量错了对象**：它们不是"抽不到"，是"不用抽" ✓
            //   判据本身没放宽：仍然要求"除白送件之外，一件都不能少"。
            var freeIds = new HashSet<string>(WeaponSystem.DefaultOwnedPlugins);
            int expectCount = 0;
            var missingIds = new StringBuilder();
            for (int i = 0; i < table.Length; i++)
            {
                if (freeIds.Contains(table[i].id)) continue;
                expectCount++;
                if (!seenPlugin.Contains(table[i].id)) missingIds.Append(table[i].id).Append(' ');
            }
            list.Add(new CheckResult("新表里的插件真的会被商店抽到（不是只有旧件在流转；开局白送的 8 件除外）",
                missingIds.Length == 0,
                $"{shopRefreshes} 次刷新里出现过 {seenPlugin.Count} 种（应抽 {expectCount} 种，白送 {table.Length - expectCount} 种不卖）"
                + (missingIds.Length > 0 ? $"｜没抽到：{missingIds}" : "｜应抽的全部都出现过")));

            // ★ 上限与商店的**可读证据** ★
            // `Report()` 只打印失败项 ⇒ 这两条过了就什么都看不到 ✗
            // 而"第 7 件被拒的原文"和"三类各占多少"正是本轮验收要看的数 ✓
            Log.Info($"[插件上限 v180] 每把武器 {PlayerGoods.MaxPluginsPerWeapon} 个插槽 ⇒ "
                     + $"装进 {installed} 件后第 {installed + 1} 件被拒，提示原文：\"{seventhReason}\""
                     + $"｜装满后叠层仍可行={stacked}\n"
                     + $"[插件商店 v180] 200 次刷新 800 个货位 ⇒ 种子 {nSeed}/{nSeed / 8f:0.0}% "
                     + $"插件 {nPlugin}/{nPlugin / 8f:0.0}% 道具 {nItem}/{nItem / 8f:0.0}%"
                     + $"（权重 种子{probe.T.shop.seedWeight}/插件{probe.T.shop.pluginWeight}/道具{probe.T.shop.itemWeight}）"
                     + $"｜200 次里出现过 {seenPlugin.Count}/{table.Length} 种插件");

            // ── ⑦ ★ 逐个插件的**装前 / 装后实测** ★
            var rows = new List<PluginProbeRow>();
            for (int i = 0; i < table.Length; i++) rows.Add(MeasurePlugin(table[i]));
            int notMoved = 0;
            string notMovedIds = "";
            var sb = new StringBuilder();
            sb.Append("[插件实测 v180] 同一把武器、同一个敌人、同一个种子 ⇒ 装前 → 装后\n");
            sb.Append("  ").Append("插件".PadRight(20)).Append("武器".PadRight(6))
              .Append("改了哪一环".PadRight(20)).Append("量的东西".PadRight(34)).Append("装前 → 装后\n");
            for (int i = 0; i < rows.Count; i++)
            {
                PluginProbeRow r = rows[i];
                // ★ v212：**机制件不参与"读数必变"的判定** ★
                //
                // 病因：`hoe_frostbite`（霜刃气浪）的 `effect = "SwordWave"` —— 它是**机制件**
                //   （挂一道会飞的剑气），**一个 `StatId` 都不改** ⇒ 属性读数本来就**不该**变 ✗
                //   而这里原来把"没变"一律算作 `notMoved` ⇒ **假阳性** ✓
                //
                // 判据为什么用 `Metric` 而不是新加字段：
                //   `MeasurePlugin` 里 `string metric = "—";`（`SelfCheck.cs:6273`）是**兜底值** ——
                //   只有真正的数值通道分支才会把它改成具体的度量名 ✓
                //   ⇒ **`Metric == "—"` 精确等价于"探针测不了这件"** ⇒ 也就等价于"它是机制件" ✓
                //   这比"维护一份机制件的 id 白名单"稳：以后再加机制件**自动**豁免 ✓
                //
                // ⚠️ 语义**没有放宽**：数值型插件仍要求 `Before != After`（`Metric != "—"` 那批）✓
                if (!r.Moved && r.Metric != "—")
                {
                    notMoved++;
                    notMovedIds += (notMovedIds.Length > 0 ? "、" : "") + r.Id;
                }
                sb.Append("  ").Append(r.Id.PadRight(20)).Append(WeaponSlotCn(r.Id).PadRight(6))
                  .Append(r.Channel.PadRight(20)).Append(r.Metric.PadRight(34))
                  .Append($"{r.Before:0.###} → {r.After:0.###}").Append(r.Moved ? "" : "   << 没变化").Append('\n');
            }
            Log.Info(sb.ToString());
            list.Add(new CheckResult("★ 每件插件装上之后攻击读数都真的变了（装前 ≠ 装后）★",
                notMoved == 0, notMoved == 0 ? $"{rows.Count} 件全部有变化" : $"{notMoved} 件没变化：{notMovedIds}"));

            // ── ⑧ 四件一起装时的叠加（不是"只有单件才生效"）
            //
            // ★ 判据从"六件 ×1.2 伤害"改成"四件 → 一次挥击的总伤害更大" ★
            // 上一版比的是**单次命中的伤害** —— 那只有"数值类插件"能拉动 ✗
            // 阶段 3 的插件**一件都不改伤害公式**，所以那个判据在新表上恒为 1.00 ✓
            // 新判据量的东西换了：**一次挥击动作打出去的全部伤害**
            // （本体 + 剑气 + 第二刀）⇒ 插件靠"多打"而不是"打得更重"生效 ✓
            float ratio = AllHoePluginsTotalRatio();
            list.Add(new CheckResult("四件锄头插件全装时一次挥击的总伤害 > 空手 ×1.2（叠加不互相抵消）",
                ratio > 1.2f, $"4 件全装 / 空手 = ×{ratio:0.00}"));

            // ── ⑨ 纯算式（不依赖局面，钉住数值语义）
            WeaponModifiers mm = WeaponModifiers.None;
            mm.BurnRatioPerLayer = 0.5f;
            mm.SlowDamageRatio = 0.5f;
            mm.SlowPctPerLayer = 0.30f;         // ★ v197：表值（`can_slow.amount`）0.12 → 0.30（无层数）
            mm.LifestealPct = 0.2f;
            mm.BeamWidthPct = 1.0f;             // ★ v197：表值（`can_thickjet.amount`）0.5 → 1.0（宽度翻倍）
            mm.SubBurstRadiusRatio = 0.5f;      // ★ v192：表值（`fert_subburst.amount2`）0.70 → 0.50
            mm.PoisonRatioPerLayer = 0.5f;
            mm.FreeBurstChance = 0.12f;
            Expect(list, "灼烧每层每秒 = **武器这一击的伤害** × dots.burnPercent（10 → 5）",
                WeaponSystem.BurnPerLayerPerSecond(10f, mm), 5f);
            Expect(list, "减速每层每秒 = **武器这一击的伤害** × dots.slowPercent（10 → 5）",
                WeaponSystem.SlowPerLayerPerSecond(10f, mm), 5f);
            Expect(list, "毒区每层每秒 = **武器这一击的伤害** × dots.poisonPercent（10 → 5）",
                WeaponSystem.PoisonPerLayerPerSecond(10f, mm), 5f);
            // ★ v279 新增：单跳上限（防爆）—— 目标 `MaxHp × dots.tickMaxHpFraction`（默认 25%）
            //   写成**字面值**（100 血 → 25）而不是读表：表被误改成 0.5 时必须变红 ✓
            Expect(list, "DoT 单跳上限 = 目标 MaxHp × 25%（100 血 ⇒ 25）",
                WeaponSystem.DotTickCap(100f), 25f);
            // 未初始化血量的目标（`MaxHp ≤ 0`）⇒ 0 = **不夹**（否则会把 DoT 夹成 0 伤害）
            Expect(list, "DoT 单跳上限：目标 MaxHp ≤ 0 时返回 0（不夹）", WeaponSystem.DotTickCap(0f), 0f);
            // ★ v279：**两处比例不许漂移** —— `plugins.json` 的自述 vs `tuning.dots` 的生效值
            //   为什么要有这三条：生效值已经搬到 `tuning.dots`，而 `plugins.json` 里那三件的
            //   `amount / amount2` 仍留着（自检 `PlugField` 的活体判定要读它）⇒
            //   若有人只改一处，商店描述（"每秒扣一半"）与真实伤害就会分叉，
            //   而这种分叉**不抛异常、不报错**，只表现成"某个插件弱一档"✗
            //   读的是**两张表**（不是自己等于自己）⇒ 这是真判据，不是退化断言 ✓
            TuningDef dotTune = TableDatabase.Tuning;
            Expect(list, "比例两处一致：`hoe_burn.amount` = `dots.burnPercent`",
                PluginAmountOf("hoe_burn", false), dotTune.DotsBurnPercent);
            Expect(list, "比例两处一致：`can_slow.amount2` = `dots.slowPercent`",
                PluginAmountOf("can_slow", true), dotTune.DotsSlowPercent);
            Expect(list, "比例两处一致：`fert_poison.amount` = `dots.poisonPercent`",
                PluginAmountOf("fert_poison", false), dotTune.DotsPoisonPercent);
            Expect(list, "吸血 = 伤害 ×20%（50 → 10）", WeaponSystem.LifestealHeal(50f, mm), 10f);
            // ★ v197：输入与期望都换成本轮的表值 —— 加粗 1.0（宽度翻倍）
            //   判据仍然是"半宽按比例变、长度不受影响"这条纯算式；改的只是喂进去的那个数 ✓
            //   改前是 0.5 → 2.1（+50%），现在是 1.0 → **2.8**（翻倍）✓
            Expect(list, "加粗：半宽 1.4 翻倍 → 2.8（★ 射程不受影响 ★）",
                WeaponSystem.BeamHalfWidth(1.4f, mm) + 1.4f, 4.2f);       // 2.8 + 1.4 = 4.2
            // ★ v192：**两条比例分开**（用户需求⑤：范围 = 主爆 1/2、伤害 = 基础爆炸伤害 50%）★
            //   半径：来自 `plugins.json` 的 `fert_subburst.amount2`（0.70 → **0.50**）
            //   伤害：来自 `tuning.formula.subBurstDamageRatio`（**0.50**），与半径**解耦** ✓
            //   期望值写成字面值（1.5 / 5）而不是 `3 × 某个比例`：退化式会变成"自己等于自己"，
            //   表被误改也判不出来 ✗ —— 这两个数是**设计值**，漂移必须让它变红 ✓
            Expect(list, "子爆半径 = 主爆 ×0.50（3 → 1.5）", WeaponSystem.SubBurstRadius(3f, mm), 1.5f);
            Expect(list, "子爆伤害 = 主爆 ×0.50（10 → 5，与半径比解耦）", WeaponSystem.SubBurstDamage(10f, mm), 5f);
            Expect(list, "毒区半径 = 主爆 ×0.80（5 → 4）", WeaponSystem.PoisonRadius(5f), 4f);
            Expect(list, "免耗概率封顶 100%（0.12 + 0.12 + … 到 1.0 就不再涨）",
                WeaponSystem.FreeBurstChanceOf(mm) + 1f, 1.12f);            // 0.12 + 1 = 1.12
            WeaponModifiers maxFree = WeaponModifiers.None; maxFree.FreeBurstChance = 2.5f;
            Expect(list, "免耗概率超配时被夹到 1.0（写 2.5 也只会是必不耗）",
                WeaponSystem.FreeBurstChanceOf(maxFree), 1f);

            // 子爆落点：正六边形（0°/60°/120°…）/ 距主爆心 = 主爆半径 ×SubBurstRingRatio
            // ★ v189：比例 0.9 → 1.75（用户需求⑤"子爆与主爆间距增加"）★
            //   判据本身没变（`rHex` 仍然直接读 `WeaponSystem.SubBurstRingRatio`）⇒
            //   这条断言量的是"六边形布角 + 圆心距 = 那个比例"，改的是**文案里的数**，不是判据 ✓
            Vector2 m0 = WeaponSystem.SubBurstCenter(Vector2.zero, 0, 5f);
            Vector2 m1 = WeaponSystem.SubBurstCenter(Vector2.zero, 1, 5f);
            Vector2 m2 = WeaponSystem.SubBurstCenter(Vector2.zero, 3, 5f);
            float rHex = 5f * WeaponSystem.SubBurstRingRatio;
            list.Add(new CheckResult(
                $"子爆按正六边形布角、距主爆心 = 主爆半径 ×{WeaponSystem.SubBurstRingRatio:0.00}"
                + "（v189：≥1.7R ⇒ 子爆落在主爆外面）",
                Mathf.Abs(m0.magnitude - rHex) < 0.01f
                && Mathf.Abs(m1.magnitude - rHex) < 0.01f
                && m0.y < 0.01f && Mathf.Abs(m0.x - rHex) < 0.01f         // 0°：正右 = +r
                && Mathf.Abs(m1.x - rHex * 0.5f) < 0.02f && m1.y > 3.8f   // 60°：右上 = (r/2, r·√3/2)
                && Mathf.Abs(m2.x + rHex) < 0.01f,                        // 180°：正左 = −r
                $"0°=({m0.x:0.0},{m0.y:0.0}) 60°=({m1.x:0.0},{m1.y:0.0}) 180°=({m2.x:0.0},{m2.y:0.0}) r={rHex:0.0}"));

            // 连环落点：第 0 个是主落点，第二个在主落点**外侧** gap 米（不是绕圈）
            Vector2 chainOut = Vector2.right;
            Vector2 b0 = RunSession.BlastSpotCenter(Vector2.zero, 0, 2, 2.5f, chainOut);
            Vector2 b1 = RunSession.BlastSpotCenter(Vector2.zero, 1, 2, 2.5f, chainOut);
            list.Add(new CheckResult("连环：第 2 个爆点在主爆点外侧 2.5m（第 1 个仍是主落点）",
                b0 == Vector2.zero && Mathf.Abs(b1.x - 2.5f) < 0.01f && Mathf.Abs(b1.y) < 0.01f,
                $"d0={b0.magnitude:0.0} d1=({b1.x:0.0},{b1.y:0.0})"));
        }

        private static string WeaponSlotCn(string pluginId)
        {
            if (pluginId.StartsWith("hoe_")) return "锄头";
            if (pluginId.StartsWith("can_")) return "水壶";
            if (pluginId.StartsWith("fert_")) return "肥料";
            return "?";
        }

        /// <summary>
        /// 锄头 4 件全装时，**一次挥击动作**打出的总伤害 ÷ 空手。
        ///
        /// ★ 为什么量"总伤害"而不是"单次命中的伤害"★
        /// 阶段 3 的插件一件都不改伤害公式 ⇒ 单价永远是 1.00 ✗
        /// 它们让这一刀**打到更多目标、并且多出一刀**（剑气 + 双击）⇒
        /// 只有把"这一次动作的全部伤害"加起来，才看得见叠加的效果 ✓
        /// 三个敌人摆在 ±12°/0°、2.6m 处（v182：剑气射程 = 攻击范围 3.2m 之内）。
        ///
        /// ★ v182：这一条的"空手读数"变了，期望值没变 ★
        /// 旧版把三个敌人摆在 5.5m ⇒ 空手**一只都打不到**（bare = 0），比值走 999 的特例。
        /// 现在剑气射程 = 近战范围 ⇒ 2.6m 处**近战扇形本身就够得到** ⇒ bare > 0，
        /// 比值变成 `满装 / 空手` 的真实比值：空手 3 刀，满装 双击 6 刀 ⇒ ≈ 2.0 ✓
        /// 断言仍然是 `> 1.2`，**没有放宽也没有删** ✓ —— 它现在量的是"双击 + 剑气叠加"，
        /// 而剑气的**纯增量**由下面 `PB_WaveOffX/Y` 那个侧偏落点单独量（那里空手确实是 0）✓
        /// </summary>
        private static float AllHoePluginsTotalRatio()
        {
            const ulong seed = 4242UL;
            float bare = RunBody(seed, null, WeaponSlot.Hoe, false, HoeVolleyDamage);
            float full = RunBody(seed, null, WeaponSlot.Hoe, false, s =>
            {
                for (int i = 0; i < TableDatabase.Plugins.Length; i++)
                {
                    PluginAsset a = TableDatabase.Plugins[i];
                    if (a.allowedWeapons.Length != 1 || a.allowedWeapons[0] != WeaponSlot.Hoe) continue;
                    WeaponSystem.Install(s.Goods, a, WeaponSlot.Hoe, out _);
                }
                return HoeVolleyDamage(s);
            });
            // 空手可能真的是 0（打不到框外那三只）⇒ 用"满装是否 > 0"来表达"多打了"
            return bare <= 1e-4f ? (full > 0f ? 999f : 0f) : full / bare;
        }

        /// <summary>一次挥击动作（含第二刀）对"框外三条线"上三个敌人的总伤害。</summary>
        private static float HoeVolleyDamage(RunSession s)
        {
            EnemyActor l = PlaceEnemy(s, new Vector2(PB_MeleeFar * Mathf.Cos(-12f * Mathf.Deg2Rad),
                                                     PB_MeleeFar * Mathf.Sin(-12f * Mathf.Deg2Rad)));
            EnemyActor c = PlaceEnemy(s, new Vector2(PB_MeleeFar, 0f));
            EnemyActor r = PlaceEnemy(s, new Vector2(PB_MeleeFar * Mathf.Cos(12f * Mathf.Deg2Rad),
                                                     PB_MeleeFar * Mathf.Sin(12f * Mathf.Deg2Rad)));
            float before = (1e5f - l.Health.Hp) + (1e5f - c.Health.Hp) + (1e5f - r.Health.Hp);
            s.DebugAttackOnce(WeaponSlot.Hoe);
            s.DebugAdvanceAttack(0.5f);          // 把第二刀 / 剑气流都推完
            return (1e5f - l.Health.Hp) + (1e5f - c.Health.Hp) + (1e5f - r.Health.Hp) - before;
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

        /// <summary>
        /// 与 `RunProbe` 相同，但"装后"那一局会把插件**叠到 maxStack** 再跑。
        ///
        /// ★ 什么时候必须叠满 ★ 只有"概率类"的效果：`fert_freeburst` 一层 12%，
        /// 20 次投掷里一次都不触发的概率仍有 8% ⇒ 单层读数**不可复现** ✓
        /// 叠满（3 层 = 36%）后 20 投至少触发一次的概率 99.99% ⇒ 同种子下结果稳定 ✓
        /// ★ 代价（必须说清）★ 这一行的"装后"是**叠满**的读数，不是一层的读数 ✓
        /// </summary>
        private static void RunProbeFull(ulong seed, PluginAsset plugin, WeaponSlot slot,
                                         Func<RunSession, float> body, out float before, out float after)
        {
            before = RunBody(seed, null, slot, false, body);
            after = RunBodyFull(seed, plugin, slot, body);
        }

        private static float RunBodyFull(ulong seed, PluginAsset plugin, WeaponSlot slot, Func<RunSession, float> body)
        {
            RunSession s = PluginBench(seed, out _);
            if (plugin != null)
            {
                if (!WeaponSystem.Install(s.Goods, plugin, slot, out _)) return float.NaN;
                for (int i = 1; i < Mathf.Max(1, plugin.maxStack); i++)
                    WeaponSystem.Install(s.Goods, plugin, slot, out _);
            }
            return body(s);
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
                // ───────── 锄头 ─────────

                case PluginEffectKind.SwordWave:
                    metric = $"侧偏 ({PB_WaveOffX}m 前 / {PB_WaveOffY}m 侧, 54.5° 出扇形) 处受到的伤害";
                    RunProbe(seed, a, slot, false,
                        s => AttackDamage(s, WeaponSlot.Hoe, PB_WaveOffX, PB_WaveOffY), out before, out after);
                    break;

                case PluginEffectKind.BurnOnHit:
                    metric = "命中后身上灼烧的「每秒」掉血";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        EnemyActor e = PlaceEnemy(s, new Vector2(PB_MeleeNear, 0f));
                        s.DebugAttackOnce(WeaponSlot.Hoe);
                        float per = s.Dots.BurnPerSecond(e.Id);
                        // ★ 实战读数：真的推进 1 秒，看它掉了多少（不是复述表里的数）★
                        float hp0 = e.Health.Hp;
                        s.Dots.Tick(1f, s.World, s.World.Player);
                        Log.Info($"[持续效果实测] 灼烧：表里每秒 {per:0.000}｜实战 1 秒掉 {hp0 - e.Health.Hp:0.000}"
                                 + $"｜剩 {s.Dots.BurnRemain(e.Id):0.00}s ×{s.Dots.BurnStacks(e.Id)}层");
                        return per;
                    }, out before, out after);
                    break;

                case PluginEffectKind.DoubleStrike:
                    metric = "一次挥击动作的总伤害（1 刀 → 2 刀）";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        EnemyActor e = PlaceEnemy(s, new Vector2(PB_MeleeNear, 0f));
                        s.DebugAttackOnce(WeaponSlot.Hoe);
                        s.DebugAdvanceAttack(0.5f);          // 把第二刀推完
                        return 1e5f - e.Health.Hp;
                    }, out before, out after);
                    break;

                case PluginEffectKind.Lifesteal:
                    metric = "打中一个敌人后玩家回的血";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        PlayerActor pl = s.World.Player;
                        pl.Health.ApplyDamage(64f);          // 先掉血，才看得见回血
                        float hp0 = pl.Health.Hp;
                        PlaceEnemy(s, new Vector2(PB_MeleeNear, 0f));
                        s.DebugAttackOnce(WeaponSlot.Hoe);
                        return pl.Health.Hp - hp0;
                    }, out before, out after);
                    break;

                // ───────── 水壶 ─────────

                case PluginEffectKind.MultiJet:
                    // ★ v252：判据从"1 秒耗水"改成"聚合里的额外水柱道数" ★
                    //   原因（自检实测）：多柱的道数是**同类水柱并行**，而"一次喷水的总耗水"
                    //   按设计**不随道数变**（表里写死"1/2/3/4 道都耗 20"）⇒ 拿耗水量量它永远是
                    //   0 差异 ✗ 这不是"插件没用"，是**尺子量错了对象**。
                    //   改成量"开火时真正被读到的那一个字段"：`WeaponSystem.Aggregate(...).ExtraJets`
                    //   —— 它正是 `RunSession` 决定喷几道时读的聚合值 ⇒ 装前 0、装后 >0 ✓
                    //   判据本身一个字没改：仍是"装前 ≠ 装后"✓
                    metric = "聚合里的额外水柱道数（WeaponSystem.Aggregate.ExtraJets）";
                    RunProbe(seed, a, slot, false,
                             s => WeaponSystem.Aggregate(s.Goods, slot).ExtraJets, out before, out after);
                    break;

                case PluginEffectKind.SlowOnHit:
                    metric = "命中后敌人的移速系数（1 = 没减速）";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        EnemyActor e = PlaceEnemy(s, new Vector2(PB_BeamNear, 0f));
                        s.DebugAttackOnce(WeaponSlot.Can);
                        float f = e.Status.SpeedFactor(s.T.formula);
                        float per = s.Dots.SlowPerSecond(e.Id);
                        float hp0 = e.Health.Hp;
                        s.Dots.Tick(1f, s.World, s.World.Player);
                        Log.Info($"[持续效果实测] 减速：移速 ×{f:0.000}｜表里每秒 {per:0.000}"
                                 + $"｜实战 1 秒掉 {hp0 - e.Health.Hp:0.000}｜剩 {s.Dots.SlowRemain(e.Id):0.00}s"
                                 + $" ×{s.Dots.SlowStacks(e.Id)}层");
                        return f;
                    }, out before, out after);
                    break;

                case PluginEffectKind.ThickJet:
                    metric = "3.2m 偏轴处被激光打到的伤害（宽度才够得着）";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        // 偏轴距离 = 基础半宽 + 敌人半径 + 0.15m ⇒ 裸宽一定打不到、+50% 一定打得到
                        EnemyActor probe = PlaceEnemy(s, new Vector2(PB_BeamNear, 0f));
                        float w = s.T.tools.can.beamWidth > 0f ? s.T.tools.can.beamWidth : 1.4f;
                        probe.Pos = new Vector2(PB_BeamNear, w + probe.Radius + 0.15f);
                        s.DebugAttackOnce(WeaponSlot.Can);
                        return 1e5f - probe.Health.Hp;
                    }, out before, out after);
                    break;

                case PluginEffectKind.AutoCharge:
                    metric = "2 秒不开火后的储水量变化（+10）";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        s.WaterAmmo = 50;
                        for (int i = 0; i < 120; i++) s.FixedTick(1f / 60f, IdleInput());
                        return s.WaterAmmo - 50;
                    }, out before, out after);
                    break;

                // ───────── 肥料 ─────────

                case PluginEffectKind.SubBurst:
                    metric = "落点外 4.5m 处受到的爆炸伤害（子爆罩到）";
                    RunProbe(seed, a, slot, false,
                        s => AttackDamage(s, WeaponSlot.Fertilizer, PB_BlastAt + 4.5f, 0f), out before, out after);
                    break;

                case PluginEffectKind.PoisonZone:
                    metric = "爆炸后第 1 秒毒区掉的伤害";
                    RunProbe(seed, a, slot, false, s =>
                    {
                        EnemyActor e = PlaceEnemy(s, new Vector2(PB_BlastAt + 0.5f, 0f));
                        s.DebugAttackOnce(WeaponSlot.Fertilizer);     // 含前摇推进 ⇒ 已真的炸过
                        float hp = e.Health.Hp;
                        s.Dots.Tick(1f, s.World, s.World.Player);      // 推进 1 秒
                        float dealt = hp - e.Health.Hp;
                        Log.Info($"[持续效果实测] 毒区：{s.Dots.Describe()}｜实战 1 秒掉 {dealt:0.000}");
                        return dealt;
                    }, out before, out after);
                    break;

                case PluginEffectKind.FreeBurst:
                    // ★ 这件必须**叠满**才量得出来 ★：1 层 12% ⇒ 20 投里可能一次都不免（概率 8%）
                    //   叠满 3 层 = 36% ⇒ 20 投至少免 1 次的概率 99.99% ✓
                    metric = "叠满 3 层后 20 次投掷的耗弹";
                    RunProbeFull(seed, a, slot, s =>
                    {
                        s.FertAmmo = 100;
                        for (int i = 0; i < 20; i++) s.DebugAttackOnce(WeaponSlot.Fertilizer);
                        return 100 - s.FertAmmo;
                    }, out before, out after);
                    break;

                case PluginEffectKind.ChainBurst:
                    metric = "落点外 3.4m 处受到的爆炸伤害（第 2 个爆点）";
                    RunProbe(seed, a, slot, false,
                        s => AttackDamage(s, WeaponSlot.Fertilizer, PB_BlastAt + 3.4f, 0f), out before, out after);
                    break;
            }

            row.Metric = metric;
            row.Before = before;
            row.After = after;
            row.Moved = !float.IsNaN(before) && !float.IsNaN(after) && Mathf.Abs(after - before) > 1e-4f;
            return row;
        }

        /// <summary>什么都不按的一帧输入（给"纯推进时间"的探针用）。</summary>
        private static FrameInput IdleInput() => new FrameInput
        {
            SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
        };

        /// <summary>对指定位置的一个敌人打一次，返回它掉了多少血。</summary>
        private static float AttackDamage(RunSession s, WeaponSlot slot, float x, float y)
        {
            EnemyActor e = PlaceEnemy(s, new Vector2(x, y));
            s.DebugAttackOnce(slot);
            s.DebugAdvanceAttack(0.5f);      // 把双击的第二刀 / 肥料的前摇都推完 ⇒ 一次"完整攻击"
            return 1e5f - e.Health.Hp;
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
