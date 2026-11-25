using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v197：**只读探针** —— 把本轮六件事逐条打成可引用的读数 ★
    ///
    /// 为什么要有它（而不是"看图就行"）：
    ///   · ① 闪避上限 / 虚化 / 灰色弹字：**半透明与颜色在无头环境里看不见**，
    ///        而它们是需求的一半 ⇒ 把"淡出曲线的那个数"抽成纯函数（`ImGuiHud.EvadeGhostAlpha`）
    ///        并在这里打出来，签收才有据 ✓
    ///   · ② 四个插件：要同时打"表值"与"消费点算出来的值"——
    ///        只打表值证明不了它会生效，只打结果说不清是哪个数在起作用 ✓
    ///   · ③ 三件"是否削了伤害"：要打**设计值 vs 实现值**两列（就是交付里那张表）✓
    ///
    /// ★ 只读 ★ 不写 `Assets/**`、不改场景、不进 Play；
    ///   唯一副作用是 `new RunSession()` 之后 `Dispose()`（与自检同一套夹具用法）✓
    /// </summary>
    public static class ProbeV197
    {
        public static string Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ v197 探针（只读）═══");
            TuningDef t = TableDatabase.Tuning;
            if (t == null) { sb.AppendLine("（没有表：先 TableDatabase.Load）"); return sb.ToString(); }

            // ══════════════════════════════════════════════════════════
            // A. ① 闪避上限
            // ══════════════════════════════════════════════════════════
            sb.AppendLine("── A. 闪避上限（需求：60%）──");
            sb.AppendLine($"A1 表值 `caps.dodge` = {t.caps.dodge:0.###}（需求 0.6）｜IsFractionStat(Dodge) = {TableDatabase.IsFractionStat(StatId.Dodge)}");
            var blk = new StatBlock(t.caps, t.player);
            sb.AppendLine($"A2 开局闪避 = {blk.Get(StatId.Dodge):0.####}（`player.dodge` = {t.player.dodge:0.####}）");
            blk.AddModifier(new StatModifier(StatId.Dodge, 5f, ModOp.Add, "probe197"));
            sb.AppendLine($"A3 再 +5 点（道具侧唯一的写法是 Add 整数）⇒ Get = {blk.Get(StatId.Dodge):0.####} ⇒ **被 caps 钳住**");
            blk.AddModifier(new StatModifier(StatId.Dodge, 10f, ModOp.Mul, "probe197"));
            sb.AppendLine($"A4 再 ×10 ⇒ {blk.Get(StatId.Dodge):0.####}（上限不会被乘算突破）");

            // ══════════════════════════════════════════════════════════
            // B. ① 闪避成功 → 虚化 + 灰色「闪避」
            // ══════════════════════════════════════════════════════════
            sb.AppendLine("── B. 闪避成功：虚化 + 灰色文字弹字 ──");
            sb.AppendLine($"B1 判定口径：`PopupStyle.IsTextKind(Evade)` = {PopupStyle.IsTextKind(PopupKind.Evade)}"
                        + $"｜`IsTextKind(DamageToEnemy)` = {PopupStyle.IsTextKind(PopupKind.DamageToEnemy)}（应 False）");
            Color gray = PopupStyle.ColorOf(PopupKind.Evade);
            sb.AppendLine($"B2 弹字颜色 = ({gray.r:0.00}, {gray.g:0.00}, {gray.b:0.00})｜字号倍率 = {PopupStyle.ScaleOf(PopupKind.Evade):0.00}"
                        + $"｜图标 = {PopupStyle.IconOf(PopupKind.Evade)}｜加感叹号 = {PopupStyle.WantsBang(PopupKind.Evade)}");
            sb.AppendLine($"B3 虚化时长 = {PlayerActor.EvadeGhostSeconds:0.000}s");
            // 淡出曲线（就是 `DrawEvadeGhost` 用的那条）
            sb.AppendLine("B4 残影 alpha 曲线（层0 内实 / 层1 / 层2 外雾）：");
            float[] ks = { 1.00f, 0.75f, 0.50f, 0.25f, 0.00f };
            for (int i = 0; i < ks.Length; i++)
            {
                float k = ks[i];
                sb.AppendLine($"     k={k:0.00} ⇒ α = {ImGuiHud.EvadeGhostAlpha(0, k):0.000}"
                            + $" / {ImGuiHud.EvadeGhostAlpha(1, k):0.000}"
                            + $" / {ImGuiHud.EvadeGhostAlpha(2, k):0.000}"
                            + $"｜放大 = {ImGuiHud.EvadeGhostGrow(0, k):0.00}"
                            + $" / {ImGuiHud.EvadeGhostGrow(1, k):0.00}"
                            + $" / {ImGuiHud.EvadeGhostGrow(2, k):0.00}");
            }

            // 真会话：让闪避必定发生 ⇒ 看结算 + 上屏原文
            var s = new RunSession();
            s.Start(197197UL);
            PlayerActor p = s.World.Player;
            StatBlock st = s.Progress.Stats;
            st.SetBase(StatId.Dodge, 1f);            // 会被 caps 钳到 0.6
            float dodgeNow = st.Get(StatId.Dodge);
            sb.AppendLine($"B5 把基底写成 1.0 ⇒ Get = {dodgeNow:0.####}（上限生效）");
            s.Popups.Clear();
            p.EvadeGhost = 0f;
            p.OnEvaded();                            // 闪避判成时 Combat 调的就是这一句
            sb.AppendLine($"B6 `OnEvaded()` 之后：虚化剩余 = {p.EvadeGhost:0.000}s｜弹字条数 = {s.Popups.Count}");
            for (int i = 0; i < s.Popups.Count; i++)
            {
                RunSession.FloatingText f = s.Popups[i];
                sb.AppendLine($"     上屏原文「{f.Text}」｜Kind = {f.Kind}"
                            + $"｜颜色 = ({f.Color.r:0.00}, {f.Color.g:0.00}, {f.Color.b:0.00}, {f.Color.a:0.00})"
                            + $"｜字号倍率 = {f.Scale:0.00}｜Ttl = {f.Ttl:0.00}s");
            }
            // 结算侧：闪避成功必须"一点血都不掉"（虚化只是表现，不能顺手改结算）
            //
            // ⚠️ 每次攻击前**必须清无敌帧**：否则第一下挨打后玩家进 1 秒无敌，
            //   后面 199 次在 `Resolve` 开头就被 `IsInvulnerable` 拦成 `Miss`（`Dodged = false`）
            //   ⇒ 实测"闪避率"会掉到 2% —— v197 第一版读数正是这么被自己骗到的 ✗
            int dodged = 0, hits = 0;
            float hp0 = p.Health.Hp;
            for (int i = 0; i < 200; i++)
            {
                p.InvulnRemaining = 0f;
                if (p.Health.Hp < 5f) p.Health.Heal(5f);      // 别让它死：死了就不会再闪
                var req = new DamageRequest { Type = DamageType.Physical, RawValue = 5f, Target = p, Source = null };
                DamageResult r = DamageResolver.Resolve(req, s.World);
                if (r.Dodged) dodged++; else hits++;
            }
            sb.AppendLine($"B7 打 200 次（闪避率 {dodgeNow * 100f:0.#}%）：闪避 {dodged} 次 / 命中 {hits} 次"
                        + $"｜血 {hp0:0.##} → {p.Health.Hp:0.##}"
                        + $"｜实测闪避率 = {(dodged + hits > 0 ? dodged * 100f / (dodged + hits) : 0f):0.#}%");
            sb.AppendLine($"B8 虚化是**纯表现**：它不写 `InvulnRemaining`（现在是 {p.InvulnRemaining:0.000}s）、"
                        + $"也不进 `DamageResult` 的任何字段（见 `Combat.Resolve` 的返回：Dodged=true / Dealt=0）");
            s.Dispose();

            // ══════════════════════════════════════════════════════════
            // C. ② 四个插件：表值 → 消费点算式
            // ══════════════════════════════════════════════════════════
            sb.AppendLine("── C. 四个插件（表值 → 消费点）──");
            PluginAsset[] table = TableDatabase.Plugins;
            DumpPlugin(sb, table, "can_thickjet");
            DumpPlugin(sb, table, "can_slow");
            DumpPlugin(sb, table, "can_autocharge");
            DumpPlugin(sb, table, "can_multijet");

            // 加粗：半宽（单层 / 两层）
            const float BaseHalfWidth = 1.4f;        // `RunSession.FireBeam` 的兜底基础半宽
            WeaponModifiers one = WeaponModifiers.None; one.BeamWidthPct = 1.0f;
            WeaponModifiers two = WeaponModifiers.None; two.BeamWidthPct = 2.0f;   // 2 层 × 每层 1.0
            sb.AppendLine($"C1 加粗 1 层：半宽 {BaseHalfWidth:0.##} → {WeaponSystem.BeamHalfWidth(BaseHalfWidth, one):0.##}"
                        + $"（×{WeaponSystem.BeamHalfWidth(BaseHalfWidth, one) / BaseHalfWidth:0.##}）");
            sb.AppendLine($"C2 加粗 2 层：半宽 {BaseHalfWidth:0.##} → {WeaponSystem.BeamHalfWidth(BaseHalfWidth, two):0.##}"
                        + $"（+{(WeaponSystem.BeamHalfWidth(BaseHalfWidth, two) / BaseHalfWidth - 1f) * 100f:0}%）★ 副作用：每层都翻倍 ⇒ 叠满 = +200%");

            // 减速：层数封顶 1
            var dots = new PluginDotTable();
            dots.ApplySlow(1, 1f, 0.30f, 1);
            float f1 = dots.SlowFactor(1);
            int n1 = dots.SlowStacks(1);
            dots.ApplySlow(1, 1f, 0.30f, 4);
            float f5 = dots.SlowFactor(1);
            int n5 = dots.SlowStacks(1);
            dots.ApplySlow(1, 1f, 0.30f, 3);
            sb.AppendLine($"C3 减速：1 次命中 ⇒ 移速 ×{f1:0.00}、{n1} 层｜再叠 4 层 ⇒ ×{f5:0.00}、{n5} 层"
                        + $"｜再叠 3 层 ⇒ ×{dots.SlowFactor(1):0.00}、{dots.SlowStacks(1)} 层（上限 {PluginDotTable.SlowMaxStacks} ⇒ 无层数效果）");
            sb.AppendLine($"C4 减速的每秒伤害（远程命中 ×0.50 × 层数）= {dots.SlowPerSecond(1):0.00}（1 层 ⇒ 只算 1 份）");

            // 多柱：耗水
            const int BaseCost = 20;      // `RunSession.BeamAmmoCost`（真实值；不是 10）
            WeaponModifiers mjNone = WeaponModifiers.None;
            WeaponModifiers mj1 = WeaponModifiers.None; mj1.ExtraJets = 1;
            WeaponModifiers mj2 = WeaponModifiers.None; mj2.ExtraJets = 2;
            sb.AppendLine($"C5 多柱耗水（单价 {BaseCost}／每道加 {WeaponSystem.MultiJetExtraWaterCost}）："
                        + $"没装 = {WeaponSystem.BeamWaterCostPerFire(BaseCost, mjNone)}｜"
                        + $"多 1 道（{(int)(1 + mj1.ExtraJets)} 道）= {WeaponSystem.BeamWaterCostPerFire(BaseCost, mj1)}｜"
                        + $"多 2 道（{(int)(1 + mj2.ExtraJets)} 道）= {WeaponSystem.BeamWaterCostPerFire(BaseCost, mj2)}"
                        + $"（水量上限 {RunSession.WaterAmmoMax}）");

            // 自充：每秒 / 每拍
            float interval = t.EffectiveCanAutoChargeInterval;
            sb.AppendLine($"C6 自充：表值 amount = 10（语义**每秒**）｜节拍 = {interval:0.###}s ⇒ 每拍回 {10f * interval:0.##} 点、每秒 {10f:0.##} 点"
                        + $"｜3 层 = 每秒 {30f:0.##} 点");

            // ══════════════════════════════════════════════════════════
            // D. ③ 三件事：设计值 vs 实现值（只报不改）
            // ══════════════════════════════════════════════════════════
            sb.AppendLine("── D. 双击 / 剑气 / 子爆：设计值 vs 实现值 ──");
            const float Hoe = 40f;
            sb.AppendLine($"D1 双击：`DoubleStrikeDamageRatioPerLayer` = {WeaponSystem.DoubleStrikeDamageRatioPerLayer:0.######}（每层 ×2/3，累计相乘）");
            for (int layer = 0; layer <= 2; layer++)
            {
                float perSwing = WeaponSystem.DoubleStrikeDamage(Hoe, layer);
                int swings = 1 + layer;
                sb.AppendLine($"     {layer} 层：每刀 {perSwing:0.00} × {swings} 刀 = **{perSwing * swings:0.00}**"
                            + $"，相对一刀 = ×{perSwing * swings / Hoe:0.000}");
            }
            sb.AppendLine($"D2 剑气：`SwordWaveDamageRatio` = {WeaponSystem.SwordWaveDamageRatio:0.###}"
                        + $"（{Hoe:0.##} 的一刀 ⇒ 剑气 {WeaponSystem.SwordWaveDamage(Hoe):0.##}）");
            sb.AppendLine($"D3 子爆：伤害比 = {WeaponSystem.SubBurstDamageRatio:0.###}（`tuning.formula.subBurstDamageRatio`）"
                        + $"｜半径比 = {SafeAmount2(table, "fert_subburst"):0.###}（`plugins.json` `fert_subburst.amount2`）");
            sb.AppendLine($"     主爆 12.50 ⇒ 子爆伤害 {WeaponSystem.SubBurstDamage(12.5f, WeaponModifiers.None):0.00}"
                        + $"｜主爆半径 2.00 ⇒ 子爆半径 {WeaponSystem.SubBurstRadius(2f, SubBurstMods(table)):0.00}");

            // ══════════════════════════════════════════════════════════
            // E. 抓图用的"长曝光"（★ 只影响当前这一局 play 会话 ★）
            // ══════════════════════════════════════════════════════════
            // 虚化只有 0.40 秒、弹字 1.15 秒 ⇒ 从"打完读数"到"外部发 /capture"之间早就消失了 ✗
            // ⇒ 这里把**同一个效果**再点亮一次、并把计时人为拉长（6 秒），
            //   这样截图上能同时看到"半透明残影"与"灰色『闪避』" ✓
            // ⚠️ 必须作用在**活的那一局**上（`GameBootstrap.Live[0].Session`）——
            //   `ImGuiHud.DrawEvadeGhost` 读的是它，探针自己 new 出来的那局根本不会被画 ✗
            // ⚠️ 它**不改任何参数**：颜色 / 曲线 / 文案仍然走 `OnEvaded()` 那条真实路径 ✓
            RunSession live = null;
            for (int i = 0; i < GameBootstrap.Live.Count; i++)
            {
                GameBootstrap b = GameBootstrap.Live[i];
                if (b != null && b.Session != null) { live = b.Session; break; }
            }
            if (live == null || live.World == null || live.World.Player == null)
            {
                sb.AppendLine("E1 抓图准备：**没有活着的会话**（不在 play 模式 / 没 Boot 完）⇒ 这一步跳过");
            }
            else
            {
                PlayerActor lp = live.World.Player;
                lp.OnEvaded();              // 真实入口：虚化 0.40s + 灰色「闪避」
                lp.EvadeGhost = 6f;         // 只为抓图延长显示（不改 `EvadeGhostSeconds` 常量）
                sb.AppendLine($"E1 抓图准备：活会话 id={lp.Id} 位置=({lp.Pos.x:0.0},{lp.Pos.y:0.0})"
                            + $"｜虚化计时拉到 {lp.EvadeGhost:0.0}s（真实时长 {PlayerActor.EvadeGhostSeconds:0.00}s）"
                            + $"｜暂停={live.Paused}｜阶段={live.Phase}");
                sb.AppendLine($"E2 上屏条数 = {live.Popups.Count}"
                            + (live.Popups.Count > 0 ? $"｜最后一条「{live.Popups[live.Popups.Count - 1].Text}」" : ""));
            }

            return sb.ToString();
        }

        private static WeaponModifiers SubBurstMods(PluginAsset[] table)
        {
            WeaponModifiers m = WeaponModifiers.None;
            for (int i = 0; i < table.Length; i++)
                if (table[i].id == "fert_subburst") m.SubBurstRadiusRatio = table[i].amount2;
            return m;
        }

        private static float SafeAmount2(PluginAsset[] table, string id)
        {
            for (int i = 0; i < table.Length; i++) if (table[i].id == id) return table[i].amount2;
            return 0f;
        }

        private static void DumpPlugin(StringBuilder sb, PluginAsset[] table, string id)
        {
            for (int i = 0; i < table.Length; i++)
            {
                PluginAsset a = table[i];
                if (a.id != id) continue;
                sb.AppendLine($"    {a.id,-16} effect={a.effect,-14} amount={a.amount:0.###}"
                            + $" amount2={a.amount2:0.###} maxStack={a.maxStack} basePrice={a.basePrice}");
                return;
            }
            sb.AppendLine($"    {id} —— **表里没有这一件** ✗");
        }
    }

    /// <summary>
    /// ★ v197：探针的**play 模式入口**（编辑器桥只能调"某个 GameObject 上的 MonoBehaviour 方法"）。
    ///
    /// 为什么需要它：`ProbeV197.Run()` 是静态的 ⇒ 桥的 `/input send` 找不到它
    /// （那条路要先 `go.GetComponents&lt;Component&gt;()` 再按类型名匹配）。
    /// 而本轮**必须**在 play 模式下抓读数（自检要一局真会话）。
    /// ⇒ 一个自举的空组件，只暴露一个无参方法把报告打进控制台 ✓
    /// ★ 它不写任何东西 ★ 不加 DontDestroyOnLoad（与 `DebugApi.AutoCreate` 同一个理由：
    ///   跨 play 会话的旧实例会让下一次自举早退）。
    /// </summary>
    public sealed class ProbeV197Host : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV197Host>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV197Host");
            go.AddComponent<ProbeV197Host>();
        }

        /// <summary>把本轮六件事的读数打一遍（桥：`/input` → action=send, method=RunOnce）。</summary>
        public void RunOnce()
        {
            Log.Info(ProbeV197.Run());
        }

        /// <summary>
        /// 抓一张**含 IMGUI 叠加层**的截图（虚化残影 + 灰色「闪避」都画在 IMGUI 上）。
        ///
        /// ★ 为什么不用桥的 `/capture` ★ 那条路是 `camera.Render()` 到 RenderTexture ——
        ///   **只有 3D 场景**，IMGUI（`OnGUI`）根本不参与 ⇒ 残影与弹字都不会出现 ✗
        ///   `ScreenCapture.CaptureScreenshot` 抓的是**合成后的整屏** ⇒ 两层都在 ✓
        /// ★ 先 `OnEvaded()` 再把计时拉长到 6 秒，是为了让"抓图这一帧"仍然看得见 ★
        ///   （真实时长 0.40 秒，抓图必然抓空）
        /// </summary>
        public void Capture()
        {
            string dir = System.IO.Path.Combine(
                System.IO.Path.Combine(Application.dataPath, ".."), "Docs", "captures");
            try { System.IO.Directory.CreateDirectory(dir); } catch { }
            string path = System.IO.Path.Combine(dir, "v197-evade-ghost-popup.png");
            RunSession live = null;
            for (int i = 0; i < GameBootstrap.Live.Count; i++)
            {
                GameBootstrap b = GameBootstrap.Live[i];
                if (b != null && b.Session != null) { live = b.Session; break; }
            }
            if (live != null && live.World != null && live.World.Player != null)
            {
                live.World.Player.OnEvaded();
                live.World.Player.EvadeGhost = 6f;
            }
            ScreenCapture.CaptureScreenshot(path);
            Log.Info("[v197] 截图已排队（含 IMGUI）：" + path);
        }
    }
}
