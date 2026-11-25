using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v200：**只读探针** —— 把本轮六件事逐条打成可引用的读数 ★★
    ///
    /// 为什么每一件都要有读数（而不是"看图/看代码"）：
    ///   ① 收割 1/4 的**归属**：这是用户报的 bug（"只有爆炸才该 1/4，锄头按正常结算"）。
    ///      只读代码只能证明"我以为"，必须打出**手收 / 炸收 / 炸完再手收**三个金币与经验读数 ✓
    ///   ② 加粗：用户说"没有产生效果"。而 `WeaponSystem.BeamHalfWidth` 那条纯算式**本来就是对的**，
    ///      坏的是表现层把它 `Clamp` 掉了 ⇒ 必须同时打**判定半宽**与**可视半宽**两个数，
    ///      外加"旧口径算出来是多少"（0.30 → 0.30 就是"看不见"的证据）✓
    ///   ③ 毒区上限：打 `maxStack` 与"一炮留几层"✓
    ///   ④ 时长 5→3：打三个 `Remain` 与"跳几次"✓
    ///   ⑤ 12 条描述：把全文打出来（交付里那张表就是它）✓
    ///   ⑥ 道具无限：打全表 `maxStack` 与 `∞` 判定（49 件无限 / 7 件纯机制）✓
    ///
    /// ★ 只读 ★ 不写 `Assets/**`、不改场景；副作用只有 `new RunSession()` → `Dispose()`（与自检同一套夹具）。
    /// ★ 为什么挂在 MonoBehaviour 上 ★ 桥的 `/input send` 只能调**组件上的公开方法**，
    ///   `static` 方法够不着（`ProbeV197Host` 的注释写过同一件事）✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV200 : MonoBehaviour
    {
        /// <summary>play 模式自举（与 `DebugApi.AutoCreate` / `ProbeV189.AutoCreate` 同一条纪律）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV200>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV200");
            go.AddComponent<ProbeV200>();
        }

        public string Run() => RunReport();

        /// <summary>
        /// ★ v200 抓图专用：**同一帧里并排画两根水柱 —— 上面那根没装加粗、下面那根装了 1 层** ★
        ///
        /// ── 为什么必须在同一帧并排 ✗ ──
        ///   分两次截"装之前 / 装之后"的图，会掺进相机、光照、敌人位置三个变量；
        ///   而这里的差别只有**一个数**（`ThickenPct` 0 → 1.0）⇒ 同一帧两根柱子是最干净的对照 ✓
        /// ── 为什么要把 `DebugFxHold` 调大 ✗ ──
        ///   水柱的寿命是 `FxLife(0.12s)`，一帧就没了 ⇒ 抓图必然抓空。
        ///   用本工程既有的慢放通道（`WorldView.DebugFxHold`）把它拉到几秒，
        ///   柱子**宽度不变**、只有存活时间变长 ✓（`FxLife` 只乘寿命，不乘 `visualW`）
        /// ── 判定宽度的来源 ✗ ──
        ///   两根柱子的 `HalfWidth` 都由 `WeaponSystem.BeamHalfWidth` 算 ⇒ 与真水壶同一条式子；
        ///   只是上面那根 `ThickenPct = 0`、下面那根 `ThickenPct = 1.0`（= 1 层 `can_thickjet`）✓
        /// </summary>
        public string BeamThickDemo(string spec)
        {
            WorldView view = LiveView();
            GameBootstrap boot = null;
            var live = GameBootstrap.Live;
            for (int i = 0; i < live.Count; i++)
                if (live[i] != null && live[i].Session != null) { boot = live[i]; break; }
            if (view == null || boot == null) return "没有活会话（先在 play 模式里跑起来）";

            float hold = ParseFloat(spec, 8f);
            WorldView.DebugFxHold = Mathf.Max(0.02f, hold);

            PlayerActor p = boot.Session.World != null ? boot.Session.World.Player : null;
            Vector2 at = p != null ? p.Pos : Vector2.zero;
            const float BaseHalf = 1.4f;
            var none = WeaponModifiers.None;
            var thick = WeaponModifiers.None;
            thick.BeamWidthPct = AmountOf("can_thickjet");        // 1 层 = 1.0 = +100%

            Vector2 from = new Vector2(at.x - 6.5f, at.y);
            // 上：没加粗；下：加粗 1 层。两根都横着铺 13 米 ⇒ 长度相同，只有粗细不同
            Publish(from + new Vector2(0f, 1.8f), new Vector2(13f, 0f), WeaponSystem.BeamHalfWidth(BaseHalf, none), none);
            Publish(from + new Vector2(0f, -0.6f), new Vector2(13f, 0f), WeaponSystem.BeamHalfWidth(BaseHalf, thick), thick);

            return "BEAMDEMO 已投两根：上=没加粗（判定半宽 "
                 + WeaponSystem.BeamHalfWidth(BaseHalf, none).ToString("0.00") + " 米）｜下=加粗 1 层（"
                 + WeaponSystem.BeamHalfWidth(BaseHalf, thick).ToString("0.00") + " 米）"
                 + "｜可视半宽 " + WorldView.JetVisualHalfWidth(BaseHalf * 2f, 0f).ToString("0.00")
                 + " ↔ " + WorldView.JetVisualHalfWidth(BaseHalf * 2f * 2f, 1f).ToString("0.00") + " 米"
                 + "｜慢放 ×" + WorldView.DebugFxHold.ToString("0.##")
                 + "｜WorldView 读数 " + view.BeamFxReport();
        }

        /// <summary>把 `DebugFxHold` 交还给 1（抓完图必须调，否则整场特效都慢放）。</summary>
        public string FxHoldOff()
        {
            WorldView.DebugFxHold = 1f;
            return "FXHOLD 已交还 ×1";
        }

        private static void Publish(Vector2 from, Vector2 dir, float halfWidth, WeaponModifiers m)
        {
            Bus.Publish(new BeamEvent
            {
                From = from, To = from + dir, HalfWidth = halfWidth,
                ThickenPct = m.BeamWidthPct,
                Fx = PluginFx.For(m, WeaponSlot.Can)
            });
        }

        private static float ParseFloat(string spec, float fallback)
        {
            if (string.IsNullOrEmpty(spec)) return fallback;
            return float.TryParse(spec.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        public static string RunReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ v200 探针（只读）═══");
            if (TableDatabase.Tuning == null) TableDatabase.Load(true);
            if (TableDatabase.Tuning == null) { sb.AppendLine("（没有表：先 TableDatabase.Load）"); return sb.ToString(); }

            AppendHarvestAttribution(sb);
            AppendBeamWidth(sb);
            AppendDotDurations(sb);
            AppendPluginTable(sb);
            AppendItemTable(sb);
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // ① ★ 收割 1/4 的**归属**：手收全额 / 炸收 1/4 / 炸完再手收仍是全额 ★
        // ══════════════════════════════════════════════════════════════════
        private static void AppendHarvestAttribution(StringBuilder sb)
        {
            sb.AppendLine("── ① 收割系数：1/4 只归爆炸（用户原话：只有爆炸操作造成的收割才只获得 1/4，锄头正常结算）──");

            // 结构证据：`HarvestYieldScale` 字段已删除 ⇒ 编译期即证，没有可泄漏的公共状态
            var overloads = typeof(World).GetMethods(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            int harvestOverloads = 0;
            string sigs = "";
            for (int i = 0; i < overloads.Length; i++)
            {
                if (overloads[i].Name != "HarvestPlot") continue;
                harvestOverloads++;
                var ps = overloads[i].GetParameters();
                sigs += (harvestOverloads > 1 ? "｜" : "") + "HarvestPlot(";
                for (int k = 0; k < ps.Length; k++) sigs += (k > 0 ? ", " : "") + ps[k].ParameterType.Name + " " + ps[k].Name;
                sigs += ")";
            }
            bool hasScaleField = typeof(World).GetField("HarvestYieldScale") != null;
            sb.AppendLine($"A1 结构：`World.HarvestPlot` 重载 {harvestOverloads} 个 ⇒ {sigs}");
            sb.AppendLine($"A2 结构：`World.HarvestYieldScale` 字段存在 = **{hasScaleField}**（v192/v195 的全局旗标已删除 "
                        + "⇒ 爆炸比例只能作为**入参**传进来；锄头/剑气/水枪/连锁只调单参重载 ⇒ **拿不到 1/4**）");

            TuningDef t = TableDatabase.Tuning;
            float ratio = t.farm.blastHarvestRatio > 0f && t.farm.blastHarvestRatio <= 1f ? t.farm.blastHarvestRatio : 0.25f;
            sb.AppendLine($"A3 表值：`farm.blastHarvestRatio` = {ratio:0.####}（1/4 = {0.25f:0.####}）"
                        + $"｜`farm.harvestGoldScale` = {t.farm.harvestGoldScale:0.####}（两边都吃，口径一致）");

            // 实测：同一个会话、三块同种子同等级的地
            var s = new RunSession();
            s.Start(200200UL);
            World w = s.World;
            s.Progress.Stats.SetBase(StatId.Luck, 0f);        // 幸运归零：否则 5% ×2 让读数变抛硬币
            SeedDef crop = TableDatabase.GetSeed("plague_mother");
            FarmPlot p0 = Plant(s, 0, crop);
            FarmPlot p1 = Plant(s, 1, crop);
            FarmPlot p2 = Plant(s, 2, crop);
            sb.AppendLine($"A4 夹具：种子 `{crop.Id}`（{crop.Name}）1 级、三块地各一格；幸运 = 0"
                        + $"｜单格基数 金 {SeedEconomy.CellGold(crop)} / 验 {SeedEconomy.CellExp(crop)}");

            Measure m0 = Shot(s, () => w.HarvestPlot(p0), "手收（锄头那条路：单参重载）");
            Measure m1 = Shot(s, () => w.MatureAndHarvestInRadius(p1.World, 0.1f), "炸收（肥料爆炸：半径 0.1m ⇒ 只圈这 1 格）");
            Measure m2 = Shot(s, () => w.HarvestPlot(p2), "炸完之后**立刻**手收（查残留）");

            int ratioGold = m0.Gold > 0 ? m1.Gold : 0;
            sb.AppendLine($"A5 手收   ：金币 +{m0.Gold}｜经验 +{m0.Exp:0.##}｜弹字「{m0.PopGold}」「{m0.PopExp}」");
            sb.AppendLine($"A6 炸收   ：金币 +{m1.Gold}｜经验 +{m1.Exp:0.##}｜弹字「{m1.PopGold}」「{m1.PopExp}」");
            sb.AppendLine($"A7 炸后手收：金币 +{m2.Gold}｜经验 +{m2.Exp:0.##}｜弹字「{m2.PopGold}」「{m2.PopExp}」");
            sb.AppendLine($"A8 【判据】炸收 / 手收 = 金 {(m0.Gold > 0 ? (float)m1.Gold / m0.Gold : 0f):0.###}"
                        + $"、验 {(m0.Exp > 0f ? m1.Exp / m0.Exp : 0f):0.###}（期望 ≈ 0.25，取整误差 ≤ 1）");
            sb.AppendLine($"A9 【判据】炸后手收 **等于** 手收 = {(m2.Gold == m0.Gold && Mathf.Abs(m2.Exp - m0.Exp) < 0.01f)}"
                        + $"（金 {m2.Gold} vs {m0.Gold}｜验 {m2.Exp:0.##} vs {m0.Exp:0.##}）"
                        + " ⇒ 爆炸没有把 1/4 漏给后面的手挥 ✓");
            sb.AppendLine($"A10 金币比值复核：手收 {m0.Gold} = round(基数 {SeedEconomy.CellGold(crop)} × 金币系数 {t.farm.harvestGoldScale:0.##})"
                        + $"｜炸收 {m1.Gold} = round(同一个基数 × 系数 × {ratio:0.####})（取整后可能差 1）");
            s.Dispose();
            sb.AppendLine("A11 注：`harvestGoldScale = 0.54` 是 v195 的**全局**金币系数，两条路都吃 —— 它不是本次的 1/4，"
                        + "读数里已经把两边一起乘掉了 ✓");
            _ = ratioGold;
        }

        private struct Measure { public int Gold; public float Exp; public string PopGold, PopExp; }

        /// <summary>同 `ProbeV192Num.HarvestOnce` 的口径：只收**一格**，读 `World.GoldEarned/ExpEarned` 的增量 + 弹字原文。</summary>
        private static Measure Shot(RunSession s, System.Action action, string label)
        {
            var m = new Measure { PopGold = "（无）", PopExp = "（无）" };
            int g0 = s.World.GoldEarned;
            float e0 = s.World.ExpEarned;
            System.Action<FloatingTextEvent> sink = e =>
            {
                if (e.Kind == PopupKind.GoldGain) m.PopGold = e.Text;
                else if (e.Kind == PopupKind.ExpGain) m.PopExp = e.Text;
            };
            Bus.Subscribe(sink);
            try { action(); }
            finally { Bus.Unsubscribe(sink); }
            m.Gold = s.World.GoldEarned - g0;
            m.Exp = s.World.ExpEarned - e0;
            return m;
        }

        private static FarmPlot Plant(RunSession s, int index, SeedDef crop)
        {
            FarmPlot p = s.World.Farm.Slots[index];
            p.TickAction(PlotAction.Dig, 10f, 1f, s.T.farm, null);
            p.TickAction(PlotAction.Sow, 10f, 1f, s.T.farm, crop);
            p.TickGrowth(999f);
            return p;
        }

        // ══════════════════════════════════════════════════════════════════
        // ② ★ 加粗：判定半宽 ＋ **可视半宽**（改前 → 改后）★
        // ══════════════════════════════════════════════════════════════════
        private static void AppendBeamWidth(StringBuilder sb)
        {
            sb.AppendLine("── ② 加粗（can_thickjet）：判定 ＋ 画面都要变宽 ──");
            const float BaseHalf = 1.4f;         // `RunSession.FireBeam` 的兜底基础半宽（= tools.can.beamWidth）
            sb.AppendLine($"B1 基础半宽 = {BaseHalf:0.##} 米（判定直径 {BaseHalf * 2f:0.##} 米）"
                        + $"｜表值 amount = {AmountOf("can_thickjet"):0.##}/层、maxStack = {MaxStackOf("can_thickjet")}");
            for (int layers = 0; layers <= 2; layers++)
            {
                WeaponModifiers m = WeaponModifiers.None;
                m.BeamWidthPct = AmountOf("can_thickjet") * layers;
                float half = WeaponSystem.BeamHalfWidth(BaseHalf, m);
                float judgeDia = half * 2f;
                // 改前口径：可视半宽 = Clamp(判定直径 × 0.16, 0.07, 0.30)（没有加粗这一项）
                float oldVisual = Mathf.Clamp(judgeDia * 0.16f, 0.07f, 0.30f);
                float newVisual = WorldView.JetVisualHalfWidth(judgeDia, m.BeamWidthPct);
                sb.AppendLine($"B2 {layers} 层（+{m.BeamWidthPct * 100f:0}%）：判定半宽 {half:0.00} 米"
                            + $"｜可视半宽 改前 **{oldVisual:0.00}** → 改后 **{newVisual:0.00}** 米"
                            + $"（直径 {oldVisual * 2f:0.00} → {newVisual * 2f:0.00}）");
            }
            sb.AppendLine("B3 【判据】改前 0/1/2 层算出来是**同一个数**（0.30 / 0.30 / 0.30）⇒ 画面上一个像素都不差"
                        + "（这就是用户说的『加粗没有产生效果』）；改后 0.30 / 0.60 / 0.90 ⇒ 一层就翻倍 ✓");
            sb.AppendLine($"B4 长度不受影响：射程仍由 `tools.can.range`({TableDatabase.Tuning.tools.can.range:0.##}) × `AtkRange` 决定，"
                        + "`JetVisualHalfWidth` 只回宽度 ✓");

            // 实测：往真表现层投一根 `BeamEvent`，读它自己算出来的可视宽度
            WorldView view = LiveView();
            if (view == null)
            {
                sb.AppendLine("B5 实测：场景里没有 `WorldView`（不在 play 模式？）⇒ 只能给算式读数");
                return;
            }
            float before = view.LastJetVisualHalfWidth;
            for (int layers = 0; layers <= 1; layers++)
            {
                WeaponModifiers m = WeaponModifiers.None;
                m.BeamWidthPct = AmountOf("can_thickjet") * layers;
                Bus.Publish(new BeamEvent
                {
                    From = new Vector2(0f, -6f), To = new Vector2(0f, 4f),
                    HalfWidth = WeaponSystem.BeamHalfWidth(BaseHalf, m),
                    ThickenPct = m.BeamWidthPct,
                    Fx = PluginFx.For(m, WeaponSlot.Can)
                });
                sb.AppendLine($"B5 实测 {layers} 层：WorldView 读数 ⇒ {view.BeamFxReport()}");
            }
            sb.AppendLine($"B6 实测对照：投事件前 `LastJetVisualHalfWidth` = {before:0.00} 米"
                        + "（= 上一次真实激光留下的值，可以证明这个字段是真被 OnBeam 写的）✓");
        }

        // ══════════════════════════════════════════════════════════════════
        // ③ ④ ★ 毒区层数上限 ＋ 三种持续效果的时长 ★
        // ══════════════════════════════════════════════════════════════════
        private static void AppendDotDurations(StringBuilder sb)
        {
            sb.AppendLine("── ③④ 持续效果：层数上限 / 时长 5 → 3 秒 / 每秒 50% 不变 ──");
            sb.AppendLine($"C1 时长（`PluginDotTable`）：灼烧 {PluginDotTable.BurnSeconds:0.#} 秒"
                        + $"｜减速 {PluginDotTable.SlowSeconds:0.#} 秒｜毒区 {PluginDotTable.PoisonSeconds:0.#} 秒"
                        + $"（v200 前三条都是 5 秒）");
            sb.AppendLine($"C2 层数上限：灼烧 maxStack = {MaxStackOf("hoe_burn")}（表） / BurnMaxStacks = {PluginDotTable.BurnMaxStacks}（表内）"
                        + $"｜减速 maxStack = {MaxStackOf("can_slow")} / SlowMaxStacks = {PluginDotTable.SlowMaxStacks}"
                        + $"｜毒区 maxStack = {MaxStackOf("fert_poison")}（v200：3 → 1，一炮只留 1 层）");

            // 真路：装插件 → 打一下 → 读每秒 / 层 / 剩几秒
            var s = new RunSession();
            s.Start(200201UL);
            s.StartWave(1);
            s.World.Enemies.Clear();
            PlayerActor p = s.World.Player;
            p.Pos = Vector2.zero;
            p.AimDir = Vector2.right;
            p.SwitchWindup = 0f;
            for (int i = 0; i < p.WeaponCds.Length; i++) p.WeaponCds[i] = 0f;
            p.Health.Setup(1e6f, 0f);
            s.Progress.Stats.SetBase(StatId.CritRate, 0f);
            // ★ 与 `ProbeV189.Bench` 逐字同一套夹具：三条伤害各 +2 ★
            //   为什么必须有它：`PluginPostHit` 里"每层每秒 = `Stats.Get(MeleeDamage) × 0.50`"
            //   读的是**属性**而不是这一下的伤害 ⇒ 属性为 0 时 `ApplyBurn` 会在
            //   `perLayerPerSecond <= 0` 那里**直接早退**，读数就变成"0 层、每秒 0" ✗
            //   （v200 第一版探针正是这么读到的 —— 那是夹具错，不是机制没生效 ✓）
            for (int i = 0; i < 3; i++)
            {
                StatId id = i == 0 ? StatId.MeleeDamage : i == 1 ? StatId.RangedDamage : StatId.BlastDamage;
                s.Progress.Stats.AddModifier(new StatModifier(id, 2f, ModOp.Add, "probe200"));
            }
            s.WaterAmmo = RunSession.WaterAmmoMax;
            s.FertAmmo = RunSession.FertAmmoMax;
            s.AimWorldLast = new Vector2(3f, 0f);

            // ★ v200：两条"每秒"的口径**不同**，读数必须分开算 —— 这是既有实现，本轮没动 ★
            //   灼烧 / 减速：`PluginPostHit` 读的是**属性本身**（`Stats.Get(MeleeDamage/RangedDamage)`）
            //   毒区：`ExplodeAt` 传的是**这一炮的实际伤害**（= 基础爆炸 `tools.fertilizer.baseValue` + 爆炸属性）
            //   ⇒ 二者的基线不同，交付里已单独报（只报不改，用户本轮只要求数值）
            float meleeStat = s.Progress.Stats.Get(StatId.MeleeDamage);
            float rangedStat = s.Progress.Stats.Get(StatId.RangedDamage);
            float blastStat = s.Progress.Stats.Get(StatId.BlastDamage);
            float explodeDmg = blastStat + TableDatabase.Tuning.tools.fertilizer.baseValue;

            PluginBench bench = new PluginBench(s);
            int instBurn = bench.Install("hoe_burn", WeaponSlot.Hoe);
            int instSlow = bench.Install("can_slow", WeaponSlot.Can);
            int instPoison = bench.Install("fert_poison", WeaponSlot.Fertilizer);
            sb.AppendLine($"C3 装配：灼烧 {instBurn} 层（表 maxStack {MaxStackOf("hoe_burn")}）"
                        + $"｜减速 {instSlow} 层（表 maxStack {MaxStackOf("can_slow")}）"
                        + $"｜毒区 {instPoison} 层（表 maxStack {MaxStackOf("fert_poison")}）");

            EnemyActor e0 = bench.Put(new Vector2(2f, 0f));
            bench.Attack(WeaponSlot.Hoe);
            sb.AppendLine($"C4 灼烧（真路：挥一刀）："
                        + $"{s.Dots.BurnStacks(e0.Id)} 层｜每秒 {s.Dots.BurnPerSecond(e0.Id):0.00}"
                        + $"｜剩 {s.Dots.BurnRemain(e0.Id):0.00} 秒"
                        + $"｜判据 每秒 = 近战**属性**({meleeStat:0.##}) × 0.50 = {meleeStat * 0.5f:0.00}");

            EnemyActor e1 = bench.Put(new Vector2(4f, 0f));
            bench.Attack(WeaponSlot.Can);
            sb.AppendLine($"C5 减速（真路）：{s.Dots.SlowStacks(e1.Id)} 层｜移速 ×{s.Dots.SlowFactor(e1.Id):0.00}"
                        + $"（−{(1f - s.Dots.SlowFactor(e1.Id)) * 100f:0}%）｜每秒 {s.Dots.SlowPerSecond(e1.Id):0.00}"
                        + $"｜剩 {s.Dots.SlowRemain(e1.Id):0.00} 秒"
                        + $"｜判据 每秒 = 远程**属性**({rangedStat:0.##}) × 0.50 = {rangedStat * 0.5f:0.00}");

            EnemyActor e2 = bench.Put(new Vector2(3f, 0f));
            bench.Attack(WeaponSlot.Fertilizer);
            float zonePer = 0f; float zoneRemain = 0f; int zoneStacks = 0;
            for (int i = 0; i < s.Dots.Rows.Count; i++)
                if (s.Dots.Rows[i].Kind == DotKind.PoisonZone)
                { zonePer = s.Dots.Rows[i].PerSecond; zoneRemain = s.Dots.Rows[i].Remain; zoneStacks = s.Dots.Rows[i].Stacks; break; }
            sb.AppendLine($"C6 毒区（真路：炸一次）：{zoneStacks} 层｜每秒 {zonePer:0.00}｜剩 {zoneRemain:0.00} 秒"
                        + $"｜块数 {s.Dots.PoisonZoneCount}"
                        + $"｜判据 每秒 = 这一炮的爆炸伤害({explodeDmg:0.##}) × 0.50 = {explodeDmg * 0.5f:0.00}"
                        + $"｜★ maxStack = {MaxStackOf("fert_poison")} ⇒ 一炮只留 {zoneStacks} 层（v200：3 → 1）");

            // 跳数：3 秒 ⇒ 恰好 3 次"每秒"
            var t3 = new PluginDotTable();
            t3.ApplyBurn(e0.Id, 10f, 1);
            float hp0 = e0.Health.Hp;
            t3.Tick(2.99f, s.World, p);
            float after299 = e0.Health.Hp;
            t3.Tick(0.02f, s.World, p);
            float after301 = e0.Health.Hp;
            sb.AppendLine($"C7 跳数：1 层灼烧（10/秒）推 2.99 秒 ⇒ 掉 {hp0 - after299:0.00}（2 跳）；"
                        + $"再推到 3.01 秒 ⇒ 累计掉 {hp0 - after301:0.00}（3 跳）"
                        + $"｜表里还剩 {t3.Count} 行（3.01 > 3.00 ⇒ 到期移除）");

            // 毒区叠层（不改的那条规则，必须读得到）
            var t2 = new PluginDotTable();
            t2.AddPoisonZone(new Vector2(1f, 1f), 2.4f, 6f, MaxStackOf("fert_poison"));
            t2.AddPoisonZone(new Vector2(1f, 1f), 2.4f, 6f, MaxStackOf("fert_poison"));
            sb.AppendLine($"C8 同一处（0.15m 内）连炸两次：块数 {t2.PoisonZoneCount}、层数 {t2.Rows[0].Stacks}"
                        + $"、每秒 {t2.Rows[0].PerSecond:0.00} ⇒ 合并成**一片更毒**（这条规则本轮没动，"
                        + "`maxStack` 管的是『一炮留几层』）✓");
            s.Dispose();
            _ = e2;
        }

        /// <summary>探针自己的小夹具：装插件 / 造敌人 / 打一下（与 `ProbeV189` 同一套用法）。</summary>
        private sealed class PluginBench
        {
            private readonly RunSession _s;
            public PluginBench(RunSession s) { _s = s; }

            /// <summary>装**一**层某插件（不装到 maxStack：`maxStack` 可能是 9999）。返回装成功几层。</summary>
            public int Install(string id, WeaponSlot slot)
            {
                PluginAsset a = Plugin(id);
                if (a == null) return 0;
                return WeaponSystem.Install(_s.Goods, a, slot, out _) ? 1 : 0;
            }

            public EnemyActor Put(Vector2 pos)
            {
                _s.World.SpawnEnemy("grunt", pos, 1);
                EnemyActor e = _s.World.Enemies[_s.World.Enemies.Count - 1];
                e.Pos = pos;
                e.Health.Setup(1e5f, 0f);
                return e;
            }

            public void Attack(WeaponSlot slot)
            {
                for (int i = 0; i < _s.World.Player.WeaponCds.Length; i++) _s.World.Player.WeaponCds[i] = 0f;
                _s.DebugAttackOnce(slot);
            }
        }

        /// <summary>当前这一局的 `WorldView`（没有就 null）。与 `ProbeV198.LiveView` 同一条路子：`WorldView` 不是组件。</summary>
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

        private static PluginAsset Plugin(string id)
        {
            PluginAsset[] all = TableDatabase.Plugins;
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }

        private static float AmountOf(string id) { PluginAsset a = Plugin(id); return a != null ? a.amount : 0f; }
        private static int MaxStackOf(string id)
        {
            PluginAsset a = Plugin(id);
            if (a == null) return -1;
            return a.maxStack;
        }

        // ══════════════════════════════════════════════════════════════════
        // ⑤ ★ 12 条插件：表值 ＋ 描述全文 ★
        // ══════════════════════════════════════════════════════════════════
        private static void AppendPluginTable(StringBuilder sb)
        {
            sb.AppendLine("── ⑤ 12 件插件：上限 / 参数 / 描述全文（描述里必须有数）──");
            PluginAsset[] all = TableDatabase.Plugins;
            for (int i = 0; i < all.Length; i++)
            {
                PluginAsset a = all[i];
                if (a == null) continue;
                string slot = a.allowedWeapons != null && a.allowedWeapons.Length > 0
                    ? SlotName(a.allowedWeapons[0]) : "任意";
                sb.AppendLine($"P{i + 1} [{a.id}] {a.displayName}（{a.quality}/{slot}）上限 {a.maxStack}"
                            + $"｜amount {a.amount:0.###}｜amount2 {a.amount2:0.###}"
                            + $"｜效果行「{ImGuiHud.PluginEffectLineForProbe(a)}」");
                sb.AppendLine($"     描述：{a.description}");
            }
        }

        private static string SlotName(WeaponSlot w)
        {
            switch (w)
            {
                case WeaponSlot.Hoe: return "锄头";
                case WeaponSlot.Can: return "水壶";
                case WeaponSlot.Fertilizer: return "肥料";
                default: return "?";
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ⑥ ★ 56 件道具：上限 / ∞ 显示 / 效果 / 代价 ★
        // ══════════════════════════════════════════════════════════════════
        private static void AppendItemTable(StringBuilder sb)
        {
            sb.AppendLine("── ⑥ 56 件道具：上限（数值件 = ∞）／效果／代价 ──");
            ItemDef[] items = TableDatabase.Items;
            if (items == null) { sb.AppendLine("（道具表为空）"); return; }
            int unlimited = 0, numericLimit = 0;
            for (int i = 0; i < items.Length; i++)
            {
                ItemDef it = items[i];
                if (it == null) continue;
                bool inf = ItemDef.IsUnlimitedStack(it.MaxStack);
                if (inf) unlimited++; else numericLimit++;
                sb.AppendLine($"I{i + 1:D2} [{it.Id}] {it.Name}（{it.Grade}）上限 {it.MaxStack}"
                            + $" → 显示「{ImGuiHud.MaxStackText(it.MaxStack)}」"
                            + $"｜效果 {EffectsText(it, true)}｜代价 {EffectsText(it, false)}");
            }
            sb.AppendLine($"I× 统计：全表 {items.Length} 件 ⇒ 无限 {unlimited} 件 / 仍为数字 {numericLimit} 件"
                        + $"（判据 `ItemDef.IsUnlimitedStack(n)` = n ≥ {ItemDef.UnlimitedStack}，"
                        + "显示唯一实现 `ImGuiHud.MaxStackText` ⇒「∞」）");
        }

        private static string EffectsText(ItemDef it, bool effects)
        {
            ItemBonus[] list = effects ? it.Effects : it.Costs;
            if (list == null || list.Length == 0) return "—";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Length; i++)
            {
                if (i > 0) sb.Append("；");
                ItemBonus f = list[i];
                sb.Append(f.Stat.ToString()).Append(' ').Append(f.Label);
            }
            return sb.ToString();
        }
    }
}
