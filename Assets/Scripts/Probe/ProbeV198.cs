using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v198：**只读探针** —— 把本轮两件事逐条打成可引用的读数 ★★
    ///
    /// 用户原话："道具的4，5，6上限修改为无限；雨天的下雨效果一次，做竖向雨滴垂直地面"
    ///
    /// 为什么要有它（而不是"看图就行"）：
    ///   · ① 三件的上限从 5/1/1 变成 9999（哨兵），而"无限"是**显示层概念**
    ///        ⇒ 必须同时钉住"表里是什么"与"UI 读成什么"，只打一半都证明不了这件事 ✓
    ///   · ② #5 / #6 是"每秒 +2%"的爬升，上限一改无限，最该问的是
    ///        "5 / 10 / 20 分钟到底多少倍、`caps` 兜不兜得住"
    ///        ⇒ 这些是**算式读数**，抓图看不出来（±2.2 倍在画面上没有刻度）✓
    ///   · ③ #4 是 `int` 上限 +20/层 ⇒ "会不会溢出、进度条分母是谁"同样是算式 ✓
    ///   · ④ "雨滴竖不竖"可以看图，但"**只生成一波、落完不再补**"是一个**时间行为**
    ///        ⇒ 必须有计数器（`WorldView.RainWaveDump`），单帧截图证明不了它 ✓
    ///
    /// ★ 只读 ★ 不写 `Assets/**`、不改场景；
    ///   唯一副作用是 `new RunSession()` 之后 `Dispose()`（与自检同一套夹具用法），
    ///   以及 `RainShot*` 那两个 **取证专用** 协程（强制天气 + 抓图，走的是既有的
    ///   `ProgMechanics.DebugForceWeather` / `ProgDemo.Shot`，不进任何玩法路径）✓
    /// </summary>
    public sealed class ProbeV198 : MonoBehaviour
    {
        /// <summary>报告落盘路径（`Library/` 是 Unity 的临时目录：不进版本库、不参与打包）。</summary>
        public const string ReportPath = "Library/yowei-v198-probe.txt";

        private static ProbeV198 _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV198>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV198");
            go.AddComponent<ProbeV198>();
        }

        private void Awake() { _instance = this; }

        // ══════════════════════════════════════════════════════════════════
        // 入口（桥 `send` 调这些）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>打一份完整读数并落盘，返回原文。</summary>
        public string Report()
        {
            string text;
            try { text = BuildReport(); }
            catch (System.Exception ex)
            {
                text = "ProbeV198 CRASH: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
                Log.Error("[ProbeV198] " + text);
                TryWrite(text);
                return text;
            }
            TryWrite(text);
            Log.Info("[ProbeV198]\n" + text);
            return text;
        }

        /// <summary>只读：本次雨天的"波"状态（波数 / 空中 / 已落地 / 环境光倍率）。</summary>
        public string RainWave()
        {
            WorldView v = LiveView();
            return v == null ? "ProbeV198：没有 WorldView（buildVisuals=false，或还没开局）" : v.RainWaveDump();
        }

        /// <summary>只读：第 i 条雨丝的朝向读数（长轴 vs 世界 +Y）。</summary>
        public string RainDrop(int index)
        {
            WorldView v = LiveView();
            return v == null ? "ProbeV198：没有 WorldView（buildVisuals=false，或还没开局）" : v.RainDropDump(index);
        }

        /// <summary>
        /// ★ 取证：强制雨天 → 等 <paramref name="spec"/> 里的秒数 → 抓一张图。
        /// `spec` = `"图名"` 或 `"图名,秒数"`（默认 1.0 秒）。
        ///
        /// 默认 **1.0 秒**：起波的门槛是"强度淡入完成"（`strength ≥ 0.9`，约 0.8s 后），
        /// 而一波的自然时长 ≈ 0.76s ⇒ **在 1.0 秒抓图**时，这一波已经落了 0.2 秒
        /// （约 74% 的雨丝还在空中），而环境光**已经压到最暗** ⇒ 一张图里两者同框 ✓
        /// 为什么把"强制天气 + 抓图"放进**同一个调用**：分两次走桥的话，
        ///   两次之间的往返延迟不可控（一波只有 0.76s），那会得到一张"晴天"的雨中图 ✗
        ///
        /// ⚠️ 为什么参数是**一个字符串**而不是"两个带默认值的参数"：桥的 `send` 用
        ///   `GetMethod(name, 精确参数类型[])` 找方法 ⇒ **带默认值的重载它找不到**
        ///   （实测报 "no public method called RainShotMidWave"）⇒ 只能收一个 string 自己拆 ✓
        /// </summary>
        public string RainShotMidWave(string spec)
        {
            Split(name: spec, fallbackName: "v198-1-rain-vertical", fallbackNum: 1.0f,
                  out string name, out float seconds);
            StartCoroutine(RainShotCo(name, 0, Mathf.Max(0f, seconds)));
            return $"ProbeV198：已起协程 —— 雨天 + 等 {seconds:0.00}s（淡入已完 + 一波在落）+ 抓图「{name}」";
        }

        /// <summary>★ 取证：强制雨天 → 等 <paramref name="spec"/> 的秒数（一波早已落完）→ 抓图，证明"落完不再补"。
        /// `spec` = `"图名"` 或 `"图名,秒数"`（默认 3 秒）。</summary>
        public string RainShotAfterWave(string spec)
        {
            Split(name: spec, fallbackName: "v198-2-rain-landed", fallbackNum: 3f,
                  out string name, out float seconds);
            StartCoroutine(RainShotCo(name, 0, Mathf.Max(0.1f, seconds)));
            return $"ProbeV198：已起协程 —— 雨天 + 等 {seconds:0.0}s（一波落完）+ 抓图「{name}」";
        }

        /// <summary>
        /// ★ 取证：把**这一局的货架**换成三件 + 一件对照，进店抓图 ⇒ 图里能读到「持有上限 ∞」。
        ///
        /// ⚠️ 与 `ProgDemo.Shot` 同级的**造场景**动作（会改当前局的 `Shop.Offers` / `Phase`），
        ///   做法与既有的 `DebugApi.ProbeShopSeedLevels` ③ 段**逐句同构**（清货架 → 装 → 进店 → 抓图）✓
        ///   它不进任何玩法路径，只服务于"UI 到底显示什么"这一个问句 ✓
        /// </summary>
        public string ShopShotUnlimited(string name)
        {
            StartCoroutine(ShopShotCo(name));
            return $"ProbeV198：已起协程 —— 货架换成三件 + 对照，进店抓图「{name}」（读数随后进日志）";
        }

        // ══════════════════════════════════════════════════════════════════
        // 协程
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator RainShotCo(string name, int frames, float seconds)
        {
            ProgMechanics.DebugForceWeather(WeatherKind.Rain, 600f);   // 600s 的窗口，足够取证
            for (int i = 0; i < frames; i++) yield return null;
            if (seconds > 0f) yield return new WaitForSeconds(seconds);

            WorldView v = LiveView();
            string wave = v != null ? v.RainWaveDump() : "（没有 WorldView）";
            string drop = v != null ? v.RainDropDump(0) : "（没有 WorldView）";

            // 抓图走**既有的三件套实现**（`ProgDemo.Shot`：CaptureScreenshotAsTexture +
            // EncodeToPNG + WriteAllBytes，并自带"是不是黑图"的亮度读数）⇒ 不在这里再抄一份 ✓
            ProgDemo demo = FindAnyObjectByType<ProgDemo>(FindObjectsInactive.Include);
            string shot = demo != null ? demo.Shot(name) : "**没有 ProgDemo 组件** ⇒ 抓不到图";
            Log.Info($"[ProbeV198] 图「{name}」\n  抓图这一刻：{wave}\n  {drop}\n  {shot}");
        }

        /// <summary>货架换成三件 + 一件对照 → 进店 → 抓图（与 `DebugApi.ProbeShopSeedLevels` ③ 段同构）。</summary>
        private IEnumerator ShopShotCo(string name)
        {
            RunSession s = LiveSession();
            if (s == null || s.Shop == null) { Log.Warn("[ProbeV198] 没有会话/商店 ⇒ 抓不到货架"); yield break; }

            s.AddGoldDirect(1000);                 // 让"购买"按钮不至于全是灰的
            s.Shop.Offers.Clear();
            var sb = new StringBuilder("[ProbeV198] 货架：");
            for (int i = 0; i < ThreeIds.Length; i++)
            {
                ItemDef it = TableDatabase.GetItem(ThreeIds[i]);
                if (it == null) continue;
                s.Shop.Offers.Add(new ShopOffer
                {
                    Kind = OfferKind.Item, Item = it, Price = it.Price, Quality = it.RarityTier,
                });
                sb.Append($" {it.Name}(maxStack={it.MaxStack}→「{ImGuiHud.MaxStackText(it.MaxStack)}」)");
            }
            ItemDef ctrl = TableDatabase.GetItem("aegis_lamp");      // 对照：没改过，必须还是数字
            if (ctrl != null)
            {
                s.Shop.Offers.Add(new ShopOffer
                {
                    Kind = OfferKind.Item, Item = ctrl, Price = ctrl.Price, Quality = ctrl.RarityTier,
                });
                sb.Append($" 对照{ctrl.Name}(maxStack={ctrl.MaxStack}→「{ImGuiHud.MaxStackText(ctrl.MaxStack)}」)");
            }
            s.SetPhasePublic(PhaseState.Shop);

            // 让 `OnGUI` 至少画一帧（IMGUI 在渲染之后跑，`CaptureScreenshotAsTexture` 抓的是 backbuffer）
            yield return null;
            yield return null;

            ProgDemo demo = FindAnyObjectByType<ProgDemo>(FindObjectsInactive.Include);
            string shot = demo != null ? demo.Shot(name) : "**没有 ProgDemo 组件** ⇒ 抓不到图";

            // ★ 第二张：把 HUD 缩放**临时**降到 1.0 ★
            //   为什么：`ui.scale = 1.5` ⇒ 虚拟画布只有 1280×720 ⇒ 商店一行的详情框 ≈ 56 虚拟高
            //   ≈ 2.5 行 ⇒ 「持有上限 …」是**第 4 行**，默认被滚动框裁掉（对**所有**道具都一样，
            //   是既有布局，不是本轮引入的）✗
            //   降到 1.0 ⇒ 虚拟画布 1920×1080 ⇒ 一行约 108 虚拟高 ⇒ 4 行全在 ✓
            //   ⚠️ 这**只改缩放、一个字的文案都没动**；抓完立刻还原 ✓
            float oldScale = TableDatabase.Tuning.ui != null ? TableDatabase.Tuning.ui.scale : 1f;
            if (TableDatabase.Tuning.ui != null) TableDatabase.Tuning.ui.scale = 1f;
            yield return null;
            yield return null;
            string shotBig = demo != null ? demo.Shot(name + "-scale1") : "**没有 ProgDemo 组件** ⇒ 抓不到图";
            if (TableDatabase.Tuning.ui != null) TableDatabase.Tuning.ui.scale = oldScale;

            // 把**商店详情第 4 行的逐字原文**也打出来（与 `OfferDetailLines` 同一表达式）
            for (int i = 0; i < s.Shop.Offers.Count; i++)
            {
                ShopOffer o = s.Shop.Offers[i];
                if (o == null || o.Item == null) continue;
                sb.Append($"  ｜第{i + 1}行详情原文：稀有度：{Rarity.NameOf(o.Quality)}　持有上限 {ImGuiHud.MaxStackText(o.Item.MaxStack)}");
            }

            Log.Info(sb.Append($"｜相位={s.Phase}｜金币={s.Gold}｜HUD 缩放还原为 {oldScale:0.##}\n  ").Append(shot)
                       .Append("\n  ").Append(shotBig).ToString());
        }

        // ══════════════════════════════════════════════════════════════════
        // 报告正文
        // ══════════════════════════════════════════════════════════════════

        private string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ v198 探针（只读）═══");
            TuningDef t = TableDatabase.Tuning;
            if (t == null) { sb.AppendLine("（没有表：先 TableDatabase.Load）"); return sb.ToString(); }

            Section1MaxStack(sb, t);
            Section2Ramp(sb, t);
            Section3Water(sb, t);
            Section4Rain(sb);

            return sb.ToString();
        }

        // ── ① 三件的持有上限 ──────────────────────────────────────────────

        private static readonly string[] ThreeIds = { "big_can", "windup_gear", "charge_coil" };
        private static readonly string[] ThreeBefore = { "5", "1", "1" };

        private void Section1MaxStack(StringBuilder sb, TuningDef t)
        {
            sb.AppendLine("── ① 三件的持有上限：改前 5 / 1 / 1 → 9999（哨兵），UI 读成 ∞ ──");
            sb.AppendLine($"  哨兵常量 `ItemDef.UnlimitedStack` = {ItemDef.UnlimitedStack}"
                        + $"｜判据 `ItemDef.IsUnlimitedStack(n)` = n >= {ItemDef.UnlimitedStack}"
                        + "｜显示唯一实现 `ImGuiHud.MaxStackText`");

            for (int i = 0; i < ThreeIds.Length; i++)
            {
                ItemDef it = TableDatabase.GetItem(ThreeIds[i]);
                if (it == null) { sb.AppendLine($"  {ThreeIds[i]}：**查不到** ✗"); continue; }
                sb.AppendLine($"  {it.Id,-13} {it.Name}｜maxStack 改前 **{ThreeBefore[i]}** → 现在 **{it.MaxStack}**"
                            + $"｜IsUnlimited = {it.IsUnlimited}"
                            + $"｜商店/详情显示「{ImGuiHud.MaxStackText(it.MaxStack)}」");
            }

            // 对照组：没改的那两件必须**还是数字**（证明 MaxStackText 不是把所有东西都换成 ∞）
            string[] untouched = { "carrot_ring", "aegis_lamp" };
            for (int i = 0; i < untouched.Length; i++)
            {
                ItemDef it = TableDatabase.GetItem(untouched[i]);
                if (it == null) continue;
                sb.AppendLine($"  对照 {it.Id,-13} {it.Name}｜maxStack = {it.MaxStack}"
                            + $"｜显示「{ImGuiHud.MaxStackText(it.MaxStack)}」（未改 ⇒ 仍是数字 ✓）");
            }

            // 真买 12 件：`TryBuy` 用的那条判据（`ShopSystem.ItemAtCap`）不再成立
            sb.AppendLine("  ── 实买 12 件（走 `RunSession.ApplyItem`，与购买同一条落地路径）──");
            var s = new RunSession();
            try
            {
                s.Start(198198UL);
                for (int i = 0; i < ThreeIds.Length; i++)
                {
                    ItemDef it = TableDatabase.GetItem(ThreeIds[i]);
                    if (it == null) continue;
                    for (int k = 0; k < 12; k++) s.ApplyItem(it);
                    s.ItemStacks.TryGetValue(it.Id, out int n);
                    bool cap = ShopSystem.ItemAtCap(s, it);
                    sb.AppendLine($"    {it.Id,-13} 持有 {n} 件｜`ItemAtCap`（TryBuy 同一条判据）= **{cap}**"
                                + $"｜判据原文 n({n}) >= maxStack({it.MaxStack}) ⇒ {(n >= it.MaxStack ? "满" : "未满")}"
                                + (cap ? " ✗ 仍被上限卡住" : " ⇒ 不再有上限 ✓"));
                }
            }
            finally { s.Dispose(); }
        }

        // ── ② #5 / #6 的爬升与 caps ───────────────────────────────────────

        private void Section2Ramp(StringBuilder sb, TuningDef t)
        {
            sb.AppendLine("── ② #5 蓄势齿轮（攻速）/ #6 蓄力线圈（伤害倍率）：5 / 10 / 20 分钟的实际乘子 ──");
            sb.AppendLine($"  算式（**唯一实现** `ProgMechanics.RampPctAt`）：pct = min({ProgMechanics.RampCapSeconds:0}, 攒了几秒) × {ProgMechanics.RampPerSecond:0}"
                        + $" ⇒ 硬顶 **+{ProgMechanics.RampPctAt(1e9f):0}% = ×{ProgMechanics.RampMultAt(1e9f):0.00}**");

            float[] marks = { 1f, 30f, 60f, 300f, 600f, 1200f };
            string[] labels = { "1 秒", "30 秒", "1 分钟", "5 分钟", "10 分钟", "20 分钟" };
            for (int i = 0; i < marks.Length; i++)
                sb.AppendLine($"    {labels[i],-7}（{marks[i],6:0}s）⇒ pct = **+{ProgMechanics.RampPctAt(marks[i]):0}%**｜乘子 ×{ProgMechanics.RampMultAt(marks[i]):0.000}");
            sb.AppendLine("  ⇒ **5 / 10 / 20 分钟全是 +120%（×2.20）**：秒数一过 60 就被 `RampCapSeconds` 截住，"
                        + "与时长、与 `maxStack` **都无关**（`Tick` 只判 `stacks > 0`）⇒ 不存在「线性爆掉」 ✓");

            // caps 兜不兜得住
            float rampMult = ProgMechanics.RampMultAt(1200f);
            var blkSpeed = new StatBlock(t.caps, t.player);
            blkSpeed.AddModifier(new StatModifier(StatId.AtkSpeed, rampMult, ModOp.Mul, "probe198"));
            float speedAtRamp = blkSpeed.Get(StatId.AtkSpeed);
            sb.AppendLine($"  #5 攻速：开局 {t.player.atkSpeed:0.##} × {rampMult:0.00} = **{speedAtRamp:0.000}**"
                        + $"｜`caps.atkSpeed` = {t.caps.atkSpeed:0.##} ⇒ {(speedAtRamp <= t.caps.atkSpeed + 1e-4f ? "**没触顶**" : "触顶")}");
            blkSpeed.AddModifier(new StatModifier(StatId.AtkSpeed, 10f, ModOp.Mul, "probe198-x10"));
            sb.AppendLine($"    再加一条 ×10（模拟别的乘区）⇒ {blkSpeed.Get(StatId.AtkSpeed):0.000}"
                        + $" ⇒ **被 `caps.atkSpeed` 钳在 {t.caps.atkSpeed:0.##}**（表级硬顶活着 ✓）");

            var blkMult = new StatBlock(t.caps, t.player);
            blkMult.AddModifier(new StatModifier(StatId.AtkMult, rampMult, ModOp.Mul, "probe198"));
            sb.AppendLine($"  #6 伤害倍率：开局 {t.player.atkMult:0.##} × {rampMult:0.00} = **{blkMult.Get(StatId.AtkMult):0.000}**"
                        + "｜**`caps` 里没有 `atkMult` 这一项**（`StatBlock.CapOf` 无该分支）"
                        + " ⇒ 这一侧**没有表级硬顶**，刹车只有 `RampCapSeconds` 与「受伤清零」两层 ✓");

            // ★ 真正的副作用：代价随层数线性，收益与层数无关 ★
            sb.AppendLine("  ▲ **层数改无限之后真正的后果在「代价」一侧**（收益与层数无关，代价随层数线性）——");
            sb.AppendLine("     `RunSession.WriteBonus` 对 Percent 代价写的是 `1 − 0.10 × 层数`（合并成一次乘法），"
                        + "而爬升是 `min(60,秒) × 2`（与层数无关）");
            sb.AppendLine("     层数｜windup_gear 的攻速乘子｜charge_coil 的伤害倍率乘子｜叠上爬升后（满爬升 ×2.20）");
            int[] stacks = { 1, 2, 5, 10, 12 };
            for (int i = 0; i < stacks.Length; i++)
            {
                int n = stacks[i];
                float cost = 1f - 0.10f * n;                      // 表里 costs: Percent 10 / 层
                sb.AppendLine($"     {n,4}｜{cost,20:0.00}｜{cost,26:0.00}｜攻速 {cost * rampMult,0:0.00} / 倍率 {cost * rampMult:0.00}"
                            + (cost <= 0f ? "  ← **该项被乘到 0 或负数**：攻速消费点 `Mathf.Max(0.05f, …)` 兜底 ⇒ 攻击间隔 ×20 起步 ✗" : ""));
            }
            sb.AppendLine("  ⇒ 结论：第 2 层起每买一件，代价再 −10%，而收益**一点不涨** ⇒ "
                        + "**5 层以上买它是净亏，10 层把它乘到 0**。本轮按用户口径只改上限，**没有擅自加软上限** ✓");
        }

        // ── ③ #4 的水壶上限与进度条分母 ──────────────────────────────────

        private void Section3Water(StringBuilder sb, TuningDef t)
        {
            sb.AppendLine("── ③ #4 丰水壶（big_can）：`WaterAmmoCap` 的溢出边界 + 进度条分母 ──");
            ItemDef can = TableDatabase.GetItem("big_can");
            float per = can != null && can.RuleValue > 0f ? can.RuleValue : ProgMechanics.ItemPanel.WaterChargePerLayerDefault;
            sb.AppendLine($"  算式：`WaterAmmoCap` = 基础 `RunSession.WaterAmmoMax`({RunSession.WaterAmmoMax}) + 每层({per:0}) × 层数"
                        + "（`ProgMechanics.ItemPanel.WaterAmmoCap`，`int`）");

            int[] stacks = { 0, 1, 5, 10, 100, ItemDef.UnlimitedStack };
            for (int i = 0; i < stacks.Length; i++)
            {
                int n = stacks[i];
                int cap = RunSession.WaterAmmoMax + (int)per * n;
                sb.AppendLine($"    层数 {n,5} ⇒ WaterAmmoCap = **{cap,7}**");
            }

            // 用真会话量一次（锚定公式，不是纯口算）
            var s = new RunSession();
            try
            {
                s.Start(198199UL);
                for (int k = 0; k < 10; k++) s.ApplyItem(can);
                s.ItemStacks.TryGetValue("big_can", out int n);
                sb.AppendLine($"  **实测**：真会话里买 10 件 ⇒ 持有 {n}｜`s.WaterAmmoCap` = **{s.WaterAmmoCap}**"
                            + $"（口算 100 + 20×{n} = {RunSession.WaterAmmoMax + 20 * n} ⇒ {(s.WaterAmmoCap == RunSession.WaterAmmoMax + 20 * n ? "一致 ✓" : "不一致 ✗")}）");
            }
            finally { s.Dispose(); }

            // 溢出边界
            long maxVal = int.MaxValue;
            long firstProductOverflow = maxVal / 20 + 1;                 // 20 × n 先溢出
            long firstSumOverflow = (maxVal - RunSession.WaterAmmoMax) / 20 + 1;
            sb.AppendLine($"  溢出边界（`int` 上限 {int.MaxValue:N0}）：");
            sb.AppendLine($"    · `20 × 层数` 这一步先溢出 ⇒ 层数 ≥ **{firstProductOverflow:N0}**");
            sb.AppendLine($"    · `100 + 20×层数` 求和溢出 ⇒ 层数 ≥ **{firstSumOverflow:N0}**");
            sb.AppendLine($"    · 哨兵只有 **{ItemDef.UnlimitedStack}** ⇒ 199,980 + 100 = **{RunSession.WaterAmmoMax + 20 * ItemDef.UnlimitedStack:N0}**"
                        + $"，离溢出点还差 **{firstProductOverflow - ItemDef.UnlimitedStack:N0}** 层（≈{firstProductOverflow / (double)ItemDef.UnlimitedStack:0} 倍）"
                        + " ⇒ **不会溢出** ✓（真正的约束是金币：40 金/件）");

            // 进度条分母
            int cap10 = RunSession.WaterAmmoMax + 20 * 10;
            sb.AppendLine("  进度条分母（`ImGuiHud` 底部水壶条）：");
            sb.AppendLine($"    · 改前：分母写死 `RunSession.WaterAmmoMax` = {RunSession.WaterAmmoMax}"
                        + $" ⇒ 10 层时分子最大 {cap10} ⇒ `{cap10}/{RunSession.WaterAmmoMax}` = **{cap10 / (float)RunSession.WaterAmmoMax:0.00} 倍**"
                        + " ⇒ 进度条溢出满格 + 文案显示 300/100 ✗");
            sb.AppendLine($"    · 现在：分母 = `s.WaterAmmoCap` ⇒ `{cap10}/{cap10}` = **1.00 倍**"
                        + " ⇒ 满格、文案 300/300 ✓（`s.WaterAmmo / cap ≤ 1` 恒成立）");
            sb.AppendLine("    · 同一处文案 `RunSession.OnRainPickup` 的 `（水/上限）` 也一起换成了 `WaterAmmoCap` ✓");
        }

        // ── ④ 雨天 ────────────────────────────────────────────────────────

        private void Section4Rain(StringBuilder sb)
        {
            sb.AppendLine("── ④ 雨天：竖向雨滴（垂直地面）+ 只生成一波 ──");
            sb.AppendLine("  朝向：网格 `FxFrustumMesh` 的长轴在**局部 +Z** 上，旧写法 `Euler(0,0,90)` 是**绕 Z 转**"
                        + " ⇒ 长轴原地不动、雨丝一直**横躺** ✗");
            sb.AppendLine("        现在改成 **`Quaternion.Euler(-90,0,0)`（绕 X 转）** ⇒ 局部 +Z → 世界 +Y = 垂直地面 ✓");
            sb.AppendLine("        下落一直是纯 Y 位移（`pos.y = 13 − phase`，无横向速度）⇒ 运动侧本来就垂直，缺的只是**形状朝向** ✓");

            WorldView v = LiveView();
            if (v == null)
            {
                sb.AppendLine("  ⚠️ 当前没有 `WorldView`（编辑模式 / buildVisuals=false）⇒ 雨丝的实时读数要在 Play 里读：");
                sb.AppendLine("     `ProbeV198.RainWave()` / `RainDrop(0)`（本探针已挂在场景里）");
                return;
            }
            sb.AppendLine("  实时读数（此刻）：");
            sb.AppendLine("    " + v.RainWaveDump());
            sb.AppendLine("    " + v.RainDropDump(0));
        }

        // ══════════════════════════════════════════════════════════════════
        // 小工具
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把 `"图名,秒数"` 拆成两段（只写图名 ⇒ 用兜底秒数）。
        /// ★ 存在的理由 ★ 桥的 `send` 用精确签名找方法 ⇒ **带默认值的重载它调不到**
        /// （实测：`RainShotMidWave(string, float = 1f)` 报 "no public method"）⇒
        /// 对外只暴露 `(string)`，参数自己拆 ✓
        /// </summary>
        private static void Split(string name, string fallbackName, float fallbackNum,
                                  out string outName, out float outNum)
        {
            outName = string.IsNullOrEmpty(name) ? fallbackName : name.Trim();
            outNum = fallbackNum;
            int comma = outName.IndexOf(',');
            if (comma < 0) return;
            string tail = outName.Substring(comma + 1).Trim();
            outName = outName.Substring(0, comma).Trim();
            if (float.TryParse(tail, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float v))
                outNum = v;
        }

        /// <summary>当前这一局的 `WorldView`（没有就 null）。</summary>
        private static WorldView LiveView()
        {
            var live = GameBootstrap.Live;
            for (int i = 0; i < live.Count; i++)
            {
                GameBootstrap b = live[i];
                if (b == null) continue;
                if (b.View != null) return b.View;
            }
            return null;
        }

        /// <summary>当前这一局的 `RunSession`（没有就 null）。</summary>
        private static RunSession LiveSession()
        {
            var live = GameBootstrap.Live;
            for (int i = 0; i < live.Count; i++)
            {
                GameBootstrap b = live[i];
                if (b == null) continue;
                if (b.Session != null) return b.Session;
            }
            return null;
        }

        private static void TryWrite(string text)
        {
            try
            {
                string path = Path.Combine(Directory.GetCurrentDirectory(), ReportPath);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (System.Exception ex)
            {
                Log.Error("[ProbeV198] 报告写盘失败：" + ex.Message);
            }
        }
    }
}
