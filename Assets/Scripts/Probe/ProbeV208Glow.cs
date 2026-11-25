using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v208 取证探针（**只读** · 平时什么都不做）★★
    ///
    /// 本轮要回答三个问题，三个都要**读数**或**图**，所以收在同一个入口：
    ///   ① "耕地与植物偶尔全部变蓝" —— 到底是谁把颜色写进了共享材质 / 谁把植物染蓝
    ///   ② 三套描边（敌人红 / 玩家白 / 植物品质五色）的**实际生效值**（从 MPB 与材质里读，不读代码）
    ///   ③ 植物等级的收益/成熟影响（1 级 vs 3 级同株）
    ///
    /// ── 判据怎么落成读数 ──
    ///   ① 双快照：t=0 与 t=+N 秒各扫一遍**每份材质的 `_BaseColor`**，
    ///      按 `sharedMaterial.GetInstanceID()` 归组 ⇒ 打印
    ///      "这份材质被 K 个对象共用，其中哪几个的颜色在这 N 秒里被改了" ——
    ///      共享材质被就地改写就是"一个对象改了颜色、别的一起变"的**唯一**机制 ✓
    ///   ② 逐组（`MET_*` / `PLT_*` / `farm_plot` / `farmer`）打印 `_NeonColor`(MPB) /
    ///      `_NeonWidth`(MPB) / 材质级 `_NeonPixels` / `_OutlineWidth`，
    ///      再按相机距离算出**霓虹在黑线外实际外扩多少米**——
    ///      判据：这个值远大于植株半径时，霓虹壳会把整株吞掉（"全变蓝"就是这么来的）✓
    ///   ③ 直接调 `ImGuiHud.PlantCellInfoLines`（`internal`，同程序集）取**真的会上屏的字**，
    ///      与 `SeedEconomy` 的算式并排打印 ⇒ 卡片与结算不可能各说各话 ✓
    ///
    /// ★ 为什么抓图不用 `ScreenCapture.CaptureScreenshot(路径)` ✗ ★
    ///   它把写盘排到帧末且没有返回值；这里用 `CaptureScreenshotAsTexture()` 同步拿像素 ✓
    ///
    /// ★ 为什么**一个会话只抓一张** ★
    ///   URP 下同一会话的第二次渲染回读会拿到旧帧 ⇒ 图不可信。要第二张请重开 play 会话 ✓
    ///
    /// ★ 它改了什么 ★
    ///   只在**局内**：把指定品种种到耕地上（与 `DebugApi.DebugPlantPair` 同一个 `BeginPlant` 入口）。
    ///   不写 `Assets/**`、不改表、不改场景；产物只落在 `<工程>/Library/` ✓
    ///
    /// ★ 硬约束：`MonoBehaviour` 的**字段初始化器**里不许建 `Texture2D/Mesh/Material` ★
    ///   （上一轮 `ProbeV206Outline` 就是被这条炸的：`CreateImpl ... not allowed to be called
    ///   from a MonoBehaviour constructor (or instance field initializer)`）
    ///   ⇒ 本类的 `MaterialPropertyBlock` 走 `Lazy()`，**不在字段初始化器里 new** ✓
    ///
    /// ★ 怎么用（桥）★
    ///   `{"action":"send","path":"ProbeV208Glow","type":"ProbeV208Glow",
    ///     "method":"Setup","value":"moontree,moss_shield,healing_clover"}`
    ///   `... "method":"Observe","value":"20"` ⇒ 双快照 + 手抓一张图 + 写报告
    ///   `... "method":"LevelReport","value":"radish_guard"` ⇒ 只出 ③ 的读数
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV208Glow : MonoBehaviour
    {
        // ★ 自动建实例：本探针**只被桥调用**，不订阅任何引擎事件、平时零帧开销 ★
        //   与 `ProbeV206Outline` 的区别：那个把自动创建**关掉**了（它的任务已完成）；
        //   本类还要被用，所以保留 —— 但同样**不注册 Update**，在场上一分钱不花 ✓
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV208Glow>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV208Glow");
            go.AddComponent<ProbeV208Glow>();
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string LibraryDir => Path.Combine(ProjectRoot, "Library");
        private static string ReportPath => Path.Combine(LibraryDir, "yowei-v208-glow.txt");
        private static string ShotPath => Path.Combine(LibraryDir, "yowei-v208-glow.png");
        private static string SelfCheckPath => Path.Combine(LibraryDir, "yowei-v208-selfcheck.txt");

        /// <summary>最近一次动作的结论（桥上直接看得见，不必去翻文件）。</summary>
        public static string LastStatus = "(还没跑过)";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
        private static readonly int NeonWidthId = Shader.PropertyToID("_NeonWidth");
        private static readonly int NeonPixelsId = Shader.PropertyToID("_NeonPixels");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int TintMulId = Shader.PropertyToID("_TintMul");

        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>惰性创建的 MPB —— **不在字段初始化器里 new**（见类注释的硬约束）。</summary>
        private MaterialPropertyBlock _mpb;
        private MaterialPropertyBlock Mpb => _mpb != null ? _mpb : (_mpb = new MaterialPropertyBlock());

        // ─────────────────────────── 入口 ───────────────────────────

        /// <summary>
        /// 把 <paramref name="seedList"/> 里的品种**轮着种满耕地**，并推到接近成熟。
        /// 为什么要"种满"：用户报的症状是"**全部**变蓝"——只种一格看不出"全部"。
        /// </summary>
        public string Setup(string seedList)
        {
            GameBootstrap boot = Live();
            if (boot == null || boot.Session == null || boot.Session.World == null)
                return Fail("没有活会话（先进 play 模式）");
            RunSession s = boot.Session;
            FarmGrid grid = s.World.Farm;
            if (grid == null) return Fail("没有农场");

            string[] ids = (seedList ?? "moontree").Split(',');
            int want = Mathf.Max(1, grid.PlotCount);
            if (grid.PlotCount < want) grid.SetMaxPlots(want);

            var sb = new StringBuilder();
            int planted = 0;
            for (int i = 0; i < grid.PlotCount; i++)
            {
                FarmPlot p = grid.Plot(i);
                if (p == null) continue;
                string id = ids[i % ids.Length].Trim();
                SeedDef def;
                try { def = TableDatabase.GetSeed(id); }
                catch (Exception) { sb.Append(id + " 不存在；"); continue; }
                p.ResetToEmpty();
                p.TickGrowth(0.05f);                       // 先让它离开 Empty，走正常的种下路径
                p.BeginPlant(s.T.farm, def, 100, 1);
                // 推到 90%：既完全是"成体"档，又不触发成熟收割（不会自己消失）
                for (int k = 0; k < 4000 && p.GrowthProgress01 < 0.90f; k++) p.TickGrowth(0.05f);
                planted++;
            }
            LastStatus = $"已种下 {planted} 格：{string.Join(",", ids)}｜{sb}";
            Log.Info("[PROBEV208] " + LastStatus);
            return LastStatus;
        }

        /// <summary>
        /// 摆一张"一张图说清三套描边"的场面：
        ///   ① 五个品质档各一株，种在相邻地块上（D/C/B/A/S ⇒ 白/绿/蓝/紫/金）
        ///   ② 玩家站在第 0 块地旁边（白霓虹）
        ///   ③ 三只敌人贴着玩家（红霓虹）—— 不在画面里就验不了"突出敌人"
        /// 配合 `DebugApi.FocusFarm` 把机位压到耕地上方，再 `Observe` 抓唯一一张图。
        /// </summary>
        public string Stage(string seedList)
        {
            GameBootstrap boot = Live();
            if (boot == null || boot.Session == null || boot.Session.World == null)
                return Fail("没有活会话（先进 play 模式）");
            RunSession s = boot.Session;
            World w = s.World;
            FarmGrid grid = w.Farm;
            if (grid == null) return Fail("没有农场");

            // ── ① 五档各一株 ──
            string[] ids = (seedList ?? "radish_guard,starvine,moontree,char_flower,plague_mother").Split(',');
            int want = Mathf.Max(ids.Length, grid.PlotCount);
            if (grid.PlotCount < want) grid.SetMaxPlots(want);
            for (int i = 0; i < grid.PlotCount; i++)
            {
                FarmPlot p = grid.Plot(i);
                if (p == null) continue;
                SeedDef def;
                try { def = TableDatabase.GetSeed(ids[i % ids.Length].Trim()); }
                catch (Exception) { continue; }
                p.ResetToEmpty();
                p.TickGrowth(0.05f);
                p.BeginPlant(s.T.farm, def, 100, 1);
                for (int k = 0; k < 4000 && p.GrowthProgress01 < 0.95f; k++) p.TickGrowth(0.05f);
            }

            // ── ② 玩家站到第一块地旁边（离开地块中心 1.4 米，别挡住作物）──
            FarmPlot first = grid.Plot(0);
            Vector2 at = first != null ? new Vector2(first.World.x, first.World.y - 1.4f) : new Vector2(0f, -2f);
            if (w.Player != null)
            {
                w.Player.Pos = at;
                w.Player.Health.Heal(9999f);
            }

            // ── ③ 三只敌人贴着玩家（种类混着放，验"每只都红"）──
            SpawnSafe(w, "grunt", at + new Vector2(-1.5f, 1.1f), 1);
            SpawnSafe(w, "shooter", at + new Vector2(1.6f, 1.0f), 1);
            SpawnSafe(w, "jumper", at + new Vector2(0.2f, -1.5f), 1);

            LastStatus = $"已摆场：{string.Join(",", ids)}｜玩家 {at}｜敌 {w.Enemies.Count} 只";
            Log.Info("[PROBEV208] " + LastStatus);
            return LastStatus;
        }

        private static void SpawnSafe(World w, string id, Vector2 pos, int wave)
        {
            try { w.SpawnEnemy(id, pos, wave); }
            catch (Exception e) { Log.Warn("[PROBEV208] 刷 " + id + " 失败：" + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>只出读数（不抓图，可以反复调）。</summary>
        public string Dump()
        {
            GameBootstrap boot = Live();
            if (boot == null) return Fail("没有活会话（先进 play 模式）");
            BuildReport(boot, null);
            string where = WriteReport();
            LastStatus = "ok：" + where;
            return LastStatus;
        }

        /// <summary>
        /// 跑一遍 `SelfCheck.RunAll`，**原文**落 `Library/yowei-v208-selfcheck.txt`。
        /// 为什么要塞进探针：自检只能在 play 模式里跑，而每次进 play 都要冒一次
        /// "GPU 设备丢失"的风险（本轮已实测崩过一次）⇒ 一个会话里把能做的都做完 ✓
        /// </summary>
        public string SelfCheckNow()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            try
            {
                List<CheckResult> results = SelfCheck.RunAll();
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].Passed) pass++;
                    else
                    {
                        fail++;
                        sb.AppendLine("FAIL  " + results[i].Name);
                        sb.AppendLine("      " + results[i].Detail);
                    }
                }
                string head = $"自检 {(fail == 0 ? "PASS" : "FAIL")}：{pass}/{results.Count} 通过，{fail} 失败";
                sb.Insert(0, head + "\n\n");
                Log.Info("[PROBEV208] " + head);
            }
            catch (Exception e)
            {
                sb.Insert(0, "自检 CRASH：" + e.GetType().Name + ": " + e.Message + "\n"
                             + (e.StackTrace ?? "").Split('\n')[0] + "\n");
                Log.Error("[PROBEV208] 自检 CRASH：" + e.GetType().Name + ": " + e.Message);
            }
            try
            {
                if (!Directory.Exists(LibraryDir)) Directory.CreateDirectory(LibraryDir);
                File.WriteAllText(SelfCheckPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e) { Log.Error("[PROBEV208] 写自检文件失败：" + e.Message); }
            LastStatus = sb.ToString().Split('\n')[0];
            return LastStatus;
        }

        /// <summary>
        /// ★ 蓝色猎手：用户报的是"**偶尔**全部变蓝" ⇒ 一次快照抓不到，必须连续采样 ★
        ///
        /// 每 1 秒扫一遍全场 renderer，算它的**实际底色** = 材质 `_BaseColor` × MPB `_TintMul`，
        /// 判"偏蓝"用 b 比 r/g 都高出一截（不是判等，只判倾向）。
        /// 记下**蓝对象最多的那一次**的明细（谁、什么材质、什么颜色），
        /// 并把当时的方向光/环境光/雾色一起打出来 ——
        /// 因为"整片全蓝"还有一条与材质无关的通路：**灯**。
        /// </summary>
        public string WatchBlue(string seconds)
        {
            float wait = 60f;
            if (!string.IsNullOrEmpty(seconds)) float.TryParse(seconds.Trim(), out wait);
            if (wait < 3f) wait = 3f;
            StartCoroutine(WatchBlueCo(wait));
            LastStatus = "queued（连续采样 " + wait.ToString("0") + " 秒）";
            return LastStatus;
        }

        private IEnumerator WatchBlueCo(float total)
        {
            if (Live() == null) { LastStatus = "没有活会话"; yield break; }

            int worstPlantsPlots = 0, worstAll = 0;
            string worstDetail = "（一次都没采到偏蓝）";
            float worstAt = -1f;
            float t = 0f;

            _sb.Clear();
            _sb.AppendLine("═══ v208 蓝色猎手 ═══");
            _sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            while (t < total)
            {
                int plantsPlots, all;
                string detail = ScanBlue(out plantsPlots, out all);
                if (plantsPlots > worstPlantsPlots || (plantsPlots == worstPlantsPlots && all > worstAll))
                {
                    worstPlantsPlots = plantsPlots;
                    worstAll = all;
                    worstAt = t;
                    worstDetail = detail;
                }
                t += 1f;
                yield return new WaitForSeconds(1f);
            }

            _sb.AppendLine($"采样 {total:0}s｜**植物+耕地里偏蓝的最多 {worstPlantsPlots} 个渲染器**"
                         + $"（全场偏蓝最多 {worstAll} 个，出现在第 {worstAt:0}s）");
            _sb.AppendLine("最坏那一次的光照与明细：");
            _sb.AppendLine(worstDetail);
            _sb.AppendLine("判据：若植物/耕地在某一刻**成批**偏蓝，就是「偶尔全部变蓝」；"
                         + "明细里能直接看出是**材质底色**蓝、还是**灯**把一切压蓝 ✓");
            string where = WriteReport();
            LastStatus = $"偏蓝 植物+耕地 {worstPlantsPlots}／全场 {worstAll} ⇒ {where}";
            Log.Info("[PROBEV208] " + LastStatus);
        }

        /// <summary>扫一遍：返回"植物+耕地"与"全场"里偏蓝的渲染器数，并给出最坏情况明细。</summary>
        private string ScanBlue(out int plantsPlots, out int allBlue)
        {
            plantsPlots = 0;
            allBlue = 0;
            var sb = new StringBuilder();
            var rows = new List<string>();
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Material m = r.sharedMaterial;
                if (m == null) continue;
                Color baseC = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                r.GetPropertyBlock(Mpb);
                Color tint = Mpb.GetColor(TintMulId);
                if (tint.a < 0.001f) tint = Color.white;
                Color eff = new Color(baseC.r * tint.r, baseC.g * tint.g, baseC.b * tint.b);
                if (!Bluish(eff)) continue;
                allBlue++;
                string grp = GroupName(r.transform);
                bool isPlantOrPlot = grp.StartsWith("植物", StringComparison.Ordinal)
                                  || grp.StartsWith("耕地", StringComparison.Ordinal);
                if (isPlantOrPlot) plantsPlots++;
                if (rows.Count < 16)
                    rows.Add($"    {grp,-28} 材质 {m.name,-32} 底色 {Col(baseC)}×染色 {Col(tint)} = {Col(eff)}");
            }
            sb.AppendLine($"    方向光：{(RenderSettings.sun != null ? Col(RenderSettings.sun.color) + $" 强度 {RenderSettings.sun.intensity:0.00}" : "(无)")}");
            sb.AppendLine($"    环境光 {Col(RenderSettings.ambientLight)}｜雾 {(RenderSettings.fog ? "开" : "关")} {Col(RenderSettings.fogColor)}");
            sb.AppendLine($"    相机背景 {Col(Camera.main != null ? Camera.main.backgroundColor : Color.black)}");
            for (int i = 0; i < rows.Count; i++) sb.AppendLine(rows[i]);
            return sb.ToString();
        }

        private static bool Bluish(Color c)
            => c.b > 0.42f && c.b > c.r + 0.12f && c.b > c.g + 0.05f;

        /// <summary>③ 指令：1 级 vs 3 级同株的收益 / 成熟读数（直接读"会上屏的那份字符串"）。</summary>
        public string LevelReport(string seedId)
        {
            GameBootstrap boot = Live();
            _sb.Clear();
            _sb.AppendLine("═══ v208 ③ 植物等级影响（读写同源）═══");
            AppendSeedLevels(string.IsNullOrEmpty(seedId) ? "radish_guard" : seedId.Trim());
            AppendAllSeedRamps();
            string where = WriteReport();
            LastStatus = "ok：" + where;
            Log.Info("[PROBEV208] " + LastStatus);
            return _sb.ToString();
        }

        /// <summary>
        /// 双快照（t=0 / t=+N 秒）→ **抓唯一一张图** → 写报告。
        /// 图的用途：直接看"耕地与植物是不是蓝的"，读数用来说明"为什么蓝"。
        /// </summary>
        public string Observe(string seconds)
        {
            float wait = 20f;
            if (!string.IsNullOrEmpty(seconds)) float.TryParse(seconds.Trim(), out wait);
            if (wait < 1f) wait = 1f;
            StartCoroutine(ObserveCo(wait));
            LastStatus = "queued（等 " + wait.ToString("0.#") + " 秒后抓一张图）";
            return LastStatus;
        }

        // ─────────────────────────── 协程 ───────────────────────────

        private IEnumerator ObserveCo(float wait)
        {
            GameBootstrap boot = Live();
            if (boot == null || boot.Session == null || boot.Session.World == null)
            {
                LastStatus = "没有活会话（先进 play 模式）";
                yield break;
            }

            // ── ① 第一张快照：只记"材质 instanceID → _BaseColor" ──
            Dictionary<Material, MatSnap> first = SnapshotMaterials(out Dictionary<Material, List<string>> owners);

            // ── ② 等 N 秒（这段时间里 `WorldView.SyncPlots` 会每帧往耕地材质写颜色）──
            float t = 0f;
            while (t < wait) { t += Time.unscaledDeltaTime; yield return null; }

            // ── ③ 抓**唯一**一张图 ──
            string shot = "（没抓到）";
            byte[] png = null;
            Texture2D tex = null;
            yield return new WaitForEndOfFrame();
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex != null) png = tex.EncodeToPNG();
                shot = tex != null
                    ? $"{tex.width}×{tex.height}｜{png.Length} 字节"
                    : "CaptureScreenshotAsTexture() 返回 null";
            }
            catch (Exception e)
            {
                shot = "抓图抛异常 " + e.GetType().Name + ": " + e.Message;
            }
            if (tex != null) Destroy(tex);
            string written = "（未写盘）";
            if (png != null)
            {
                try
                {
                    if (!Directory.Exists(LibraryDir)) Directory.CreateDirectory(LibraryDir);
                    File.WriteAllBytes(ShotPath, png);
                    written = ShotPath;
                }
                catch (Exception e) { shot += "｜写盘失败 " + e.Message; }
            }

            // ── ④ 第二张快照 + 报告 ──
            BuildReport(boot, new FirstPass(first, owners));
            _sb.AppendLine("── 图（本会话唯一一张）──");
            _sb.AppendLine("文件：" + written);
            _sb.AppendLine("读数：" + shot);
            _sb.AppendLine($"等待时长：{wait:0.#}s（URP 同一会话第二次回读会拿到旧帧 ⇒ 只抓这一张）");
            string where = WriteReport();
            LastStatus = "ok：" + shot + " ⇒ " + where;
            Log.Info("[PROBEV208] " + LastStatus);
        }

        // ─────────────────────────── 报告 ───────────────────────────

        private void BuildReport(GameBootstrap boot, FirstPass first)
        {
            _sb.Clear();
            _sb.AppendLine("═══ v208 探针读数（只读）═══");
            _sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _sb.AppendLine("相机场位：" + CamText());
            _sb.AppendLine();

            AppendMaterialSharing(first);
            AppendToonKeyCollisions();
            AppendNeon(boot);
            AppendLevelReality();
        }

        // ── 材质共享 / 就地改写 ──

        private struct MatSnap
        {
            public string Name;
            public Color BaseColor;
        }

        private sealed class FirstPass
        {
            public readonly Dictionary<Material, MatSnap> Snap;
            public readonly Dictionary<Material, List<string>> Owners;
            public FirstPass(Dictionary<Material, MatSnap> snap, Dictionary<Material, List<string>> owners)
            { Snap = snap; Owners = owners; }
        }

        /// <summary>
        /// 把每份被 renderer 引用的 **sharedMaterial** 按"这一份材质对象"归组，并记下是谁在用。
        /// ☆ 这是本轮 ① 的核心取证：材质本来"共享"是设计（池子），
        ///   但只要有人对它 `SetColor`/`m.color=`，**同组的每个对象都会跟着变**。
        ///
        /// ★ 为什么用 `Material` 当字典键而不是 `GetInstanceID()` ★
        ///   Unity 6.4 已把 `GetInstanceID()` 标成 obsolete（CS0618）——
        ///   而本工程有一条硬约束："警告总数保持 3 条"。探针不该给自己引入新警告 ✗
        ///   `Material` 本身就能当键（`UnityEngine.Object` 的 GetHashCode/Equals 按对象身份），
        ///   既不废弃、也比 int 更直接 ✓
        /// </summary>
        private Dictionary<Material, MatSnap> SnapshotMaterials(out Dictionary<Material, List<string>> owners)
        {
            owners = new Dictionary<Material, List<string>>();
            var snap = new Dictionary<Material, MatSnap>();
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Material m = r.sharedMaterial;
                if (m == null) continue;
                if (!snap.ContainsKey(m))
                {
                    snap[m] = new MatSnap
                    {
                        Name = m.name,
                        BaseColor = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white
                    };
                }
                if (!owners.TryGetValue(m, out List<string> list))
                {
                    list = new List<string>();
                    owners[m] = list;
                }
                if (list.Count < 12) list.Add(OwnerName(r.transform));
            }
            return snap;
        }

        private void AppendMaterialSharing(FirstPass first)
        {
            _sb.AppendLine("── ① 共享材质：谁和谁共用一个 Material（判据：共用的材质**不许**被人就地改色）──");

            Dictionary<Material, MatSnap> now = SnapshotMaterials(out Dictionary<Material, List<string>> owners);
            var rows = new List<(string owner, Material mat, string name, Color c0, Color c1, int share)>();
            foreach (KeyValuePair<Material, MatSnap> kv in now)
            {
                Material mat = kv.Key;
                Color c1 = kv.Value.BaseColor;
                Color c0 = c1;
                MatSnap s0 = default;
                bool hadFirst = first != null && first.Snap.TryGetValue(mat, out s0);
                if (hadFirst) c0 = s0.BaseColor;
                int share = owners.TryGetValue(mat, out List<string> l) ? l.Count : 0;
                string owner = (owners.TryGetValue(mat, out List<string> l2) && l2.Count > 0) ? l2[0] : "(?)";
                rows.Add((owner, mat, kv.Value.Name, c0, c1, share));
            }
            rows.Sort((a, b) => b.share.CompareTo(a.share));

            int shown = 0, changedShared = 0;
            for (int i = 0; i < rows.Count && shown < 18; i++)
            {
                var r = rows[i];
                bool changed = first != null && !Same(r.c0, r.c1);
                if (changed && r.share > 1) changedShared++;
                string tag = changed ? "★在这段时间里被改过★" : "";
                _sb.AppendLine($"  {r.owner,-26} 材质[{shown}] {r.name,-34} 共用 {r.share,3} 个"
                             + $"｜_BaseColor {(first != null ? $"{Col(r.c0)} → {Col(r.c1)}" : Col(r.c1))} {tag}");
                shown++;
            }
            if (first != null)
            {
                _sb.AppendLine($"  ⇒ 这 {rows.Count} 份材质里，**有 {changedShared} 份被共用、且在这段时间里颜色变了**"
                             + (changedShared == 0 ? "（本轮已修，没有就地改写）✓" : " ✗ 这就是「全部变蓝」的机制"));
            }
            if (rows.Count > 0)
            {
                // 挑一份共用最多的，把使用者名字列出来 —— "改了它，这些人一起变"
                Material topMat = rows[0].mat;
                if (topMat != null && owners.TryGetValue(topMat, out List<string> top))
                    _sb.AppendLine($"  共用最多的那份（{rows[0].name}）使用者：{string.Join(" / ", top)}");
            }
            _sb.AppendLine();
        }

        // ── ToonLook 缓存键 ──

        /// <summary>
        /// ★ 本轮 ① 的第二个取证点：`ToonLook.Get` 的**缓存键**。
        /// 旧式子是 `((rgb*397) ^ widthKey) * (simple ? 2 : 1)` ——
        /// `simple` 那一支只是把整个键**乘 2**，并没有把两个命名空间分开：
        /// 于是 `simple=true` 的键全部落在**偶数**上，而 `simple=false` 的键**一半也是偶数**
        /// ⇒ 只要两个 hash 满足 `H_nonSimple == 2 * H_simple` 就撞进同一份材质 ✗
        /// 撞上的后果不是报错，而是"**这株植物拿到了别人的材质**"（颜色/着色器都不对）✓
        /// 这里把旧式子在**场景里真实出现的颜色与描边宽度**上跑一遍，把撞的实例打出来。
        /// </summary>
        private void AppendToonKeyCollisions()
        {
            _sb.AppendLine("── ① ToonLook 缓存键：旧式子 `((rgb*397)^w) * (simple?2:1)` 的碰撞体检 ──");

            var inputs = new List<(int rgb, int w, bool simple)>();
            var seen = new HashSet<string>();
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Material m = r.sharedMaterial;
                if (m == null) continue;
                if (m.shader == null) continue;
                string sn = m.shader.name;
                if (!sn.StartsWith("Seedfall/Toon", StringComparison.Ordinal)) continue;
                Color c = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                int w = m.HasProperty(OutlineWidthId) ? Mathf.RoundToInt(m.GetFloat(OutlineWidthId) * 10000f) : 0;
                bool simple = sn.EndsWith("ToonSimple", StringComparison.Ordinal);
                string sig = $"{Rgb(c)}/{w}/{(simple ? 1 : 0)}";
                if (!seen.Add(sig)) continue;
                inputs.Add((Rgb(c), w, simple));
            }

            // 旧键 vs 新键（新键 = 三维分开，见本轮改动）
            var oldGroups = new Dictionary<int, List<string>>();
            var newGroups = new Dictionary<long, List<string>>();
            for (int i = 0; i < inputs.Count; i++)
            {
                var x = inputs[i];
                string label = $"{(x.simple ? "S" : "F")}({x.rgb:X6},w={x.w})";
                int oldKey = ((x.rgb * 397) ^ x.w) * (x.simple ? 2 : 1);
                long newKey = NewKey(x.rgb, x.w, x.simple);
                if (!oldGroups.TryGetValue(oldKey, out List<string> a)) { a = new List<string>(); oldGroups[oldKey] = a; }
                a.Add(label);
                if (!newGroups.TryGetValue(newKey, out List<string> b)) { b = new List<string>(); newGroups[newKey] = b; }
                b.Add(label);
            }

            int oldBad = 0, newBad = 0;
            string firstBad = "";
            foreach (KeyValuePair<int, List<string>> kv in oldGroups)
            {
                if (kv.Value.Count <= 1) continue;
                oldBad++;
                if (firstBad.Length == 0) firstBad = $"键 {kv.Key} ← {string.Join(" 与 ", kv.Value)}";
            }
            foreach (KeyValuePair<long, List<string>> kv in newGroups)
                if (kv.Value.Count > 1) newBad++;

            _sb.AppendLine($"  场景里真实出现的 (颜色,描边,simple) 组合：{inputs.Count} 种");
            _sb.AppendLine($"  旧键碰撞组：{oldBad} 组{(firstBad.Length > 0 ? "｜首例：" + firstBad : "")}");
            _sb.AppendLine($"  新键碰撞组：{newBad} 组（键 = rgb、描边、simple **三维分开**，不靠乘法错开）");
            _sb.AppendLine();
        }

        /// <summary>本轮改动后的键：三段拼成一个 64 位整数，**任何一段不同就不可能相等**。</summary>
        private static long NewKey(int rgb, int widthKey, bool simple)
            => ((long)rgb << 32) ^ ((long)Mathf.Clamp(widthKey, 0, 0xFFFF) << 16) ^ (simple ? 1L : 0L);

        // ── 霓虹读数 ──

        private void AppendNeon(GameBootstrap boot)
        {
            _sb.AppendLine("── ② 描边读数（从 MPB 与材质里读实际生效值，不是读代码里的常量）──");
            Camera cam = Camera.main;
            float m11 = 1f, screenH = 720f, worldPerPixel = 0f;
            if (cam != null)
            {
                screenH = cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height;
                m11 = cam.projectionMatrix.m11;
                worldPerPixel = 2f / Mathf.Max(0.0001f, m11 * screenH);
            }
            _sb.AppendLine($"  相机 {(cam != null ? cam.name : "(无 Camera.main)")}｜像素高 {screenH:0}"
                         + $"｜m11 {m11:0.000}｜**每像素 {worldPerPixel * 100f:0.00} 厘米**（距离 1 米处）");

            var groups = new Dictionary<string, Agg>();
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Material m = r.sharedMaterial;
                if (m == null || m.shader == null) continue;
                if (!m.shader.name.StartsWith("Seedfall/Toon", StringComparison.Ordinal)) continue;
                string g = GroupName(r.transform);
                if (!groups.TryGetValue(g, out Agg a)) { a = new Agg(); groups[g] = a; }
                a.Renderers++;
                r.GetPropertyBlock(Mpb);
                Color nc = Mpb.GetColor(NeonColorId);
                if (nc.a > 0.01f)
                {
                    a.Neon++;
                    if (a.Color.a <= 0.01f)
                    {
                        a.Color = nc;
                        a.NeonWidth = Mpb.GetFloat(NeonWidthId);
                        // ★ v208：`_NeonPixels` 也必须**从 MPB 读** ★
                        // 它现在是逐实例的（`ModelView.SetNeon` 写进 MPB），
                        // 第一版这里读的是 `m.GetFloat(NeonPixelsId)`（材质级，恒为 2.0）
                        // ⇒ 作物明明被压到 0.8 像素，读数却显示 2.0 ⇒ "量出来的不是生效值" ✗
                        // MPB 里没有（== 0）才回落到材质的默认值 ✓
                        float px = Mpb.GetFloat(NeonPixelsId);
                        if (px <= 0.0001f) px = m.HasProperty(NeonPixelsId) ? m.GetFloat(NeonPixelsId) : 0f;
                        a.NeonPixels = px;
                        a.OutlineWidth = m.HasProperty(OutlineWidthId) ? m.GetFloat(OutlineWidthId) : 0f;
                        a.Dist = cam != null ? Vector3.Distance(cam.transform.position, r.bounds.center) : 0f;
                        a.Name = m.name;
                    }
                }
            }

            var keys = new List<string>(groups.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                Agg a = groups[keys[i]];
                if (a.Neon == 0)
                {
                    _sb.AppendLine($"  {keys[i],-24} renderer {a.Renderers,3}｜**无霓虹**");
                    continue;
                }
                // 屏幕像素下限换算成米（距离 dist 处）
                float pixelFloor = a.Dist * worldPerPixel * a.NeonPixels;
                float effNeon = Mathf.Max(a.NeonWidth, pixelFloor);
                float shell = a.OutlineWidth + effNeon;
                _sb.AppendLine($"  {keys[i],-24} renderer {a.Renderers,3}｜霓虹 {a.Neon,3}｜色 {Col(a.Color)}"
                             + $"｜宽(MPB) {a.NeonWidth * 100f:0.00}cm｜像素下限 {a.NeonPixels:0.0}px→{pixelFloor * 100f:0.00}cm"
                             + $"｜**实际 {effNeon * 100f:0.00}cm**｜外壳总外扩 {shell * 100f:0.00}cm｜距 {a.Dist:0.0}m｜{a.Name}");
            }
            _sb.AppendLine("  判据：外壳总外扩 **远大于植株半径** ⇒ 霓虹壳把整株吞掉，看上去就是一坨纯色（B 档=蓝）");
            _sb.AppendLine();
        }

        /// <summary>
        /// ★ 必须是 **class** 而不是 struct ★
        /// 第一版写成 struct，`Agg a = groups[key]; a.Renderers++;` 改的是**副本**，
        /// 读回来的永远是 0（实测读数全是 `renderer 0｜无霓虹`）。
        /// 这就是"看起来跑了、量出来全是零"的那类假读数 —— 换成 class 后字典里存的是引用 ✓
        /// </summary>
        private sealed class Agg
        {
            public int Renderers, Neon;
            public Color Color;
            public float NeonWidth, NeonPixels, OutlineWidth, Dist;
            public string Name;
        }

        /// <summary>按对象名前缀归组：作物 = `MDL_xxx`，耕地 = `MDL_farm_plot`，玩家 = `MDL_farmer`。</summary>
        private static string GroupName(Transform t)
        {
            string leaf = "(场景/装饰)";
            for (Transform c = t; c != null; c = c.parent)
            {
                string n = c.name;
                if (n.StartsWith("MDL_farm_plot", StringComparison.Ordinal)) return "耕地 MDL_farm_plot";
                if (n.StartsWith("MDL_farmer", StringComparison.Ordinal)) return "玩家 MDL_farmer";
                if (n.StartsWith("BUG_", StringComparison.Ordinal)) return "敌人(程序化)";
                if (n.StartsWith("MDL_imp", StringComparison.Ordinal)) return "敌人 " + n;
                if (n.StartsWith("MDL_brute", StringComparison.Ordinal)) return "敌人 " + n;
                if (n.StartsWith("MDL_pig", StringComparison.Ordinal)) return "小猪 " + n;
                if (n.StartsWith("MDL_", StringComparison.Ordinal)) { leaf = "植物 " + n; break; }
            }
            return leaf;
        }

        private static string OwnerName(Transform t) => GroupName(t);

        // ── 等级 ──

        private void AppendLevelReality()
        {
            _sb.AppendLine("── ③ 植物等级：收益与成熟的**读写同源**读数 ──");
            AppendSeedLevels("radish_guard");
            _sb.AppendLine();
        }

        /// <summary>同一品种 1/2/3 级：卡片上会画出去的那几行 + 结算算式用的倍率。</summary>
        private void AppendSeedLevels(string seedId)
        {
            SeedDef d;
            try { d = TableDatabase.GetSeed(seedId); }
            catch (Exception e) { _sb.AppendLine("  取不到种子 " + seedId + "：" + e.Message); return; }

            _sb.AppendLine($"  品种 {d.Id}（{d.Grade} 档，基础成熟 {d.GrowTime:0.#}s，"
                         + $"goldYield {SeedEconomy.CellGold(d):0.##}，expYield {SeedEconomy.CellExp(d):0.##}）");
            for (int lv = SeedSlot.StartingLevel; lv <= SeedSlot.MaxLevel; lv++)
            {
                var slot = new SeedSlot { Def = d, Quality = d.RarityTier, Level = lv };
                float speed = SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, lv);
                float secs = d.GrowTime / speed;
                float harvest = slot.LevelHarvestMult;
                float gold = Mathf.Max(1f, Mathf.RoundToInt(SeedEconomy.CellGold(d) * harvest
                                                           * PlantMechanics.HarvestGoldMult(d, null)));
                float exp = Mathf.Max(1f, Mathf.RoundToInt(SeedEconomy.CellExp(d) * harvest));
                _sb.AppendLine($"    Lv{lv}：收获倍率 ×{harvest:0.00}"
                             + $"｜速度倍率 ×{speed:0.00} ⇒ 成熟 {secs:0.##}s"
                             + $"（{d.GrowTime:0.#}s → {(1f - 1f / speed) * 100f:0.#}%）"
                             + $"｜金币 {gold:0}｜经验 {exp:0}");
                string[] card = ImGuiHud.PlantCellInfoLines(slot, false);
                _sb.AppendLine("         卡面：" + string.Join(" ｜ ", card));
            }
        }

        /// <summary>22 品种 × 3 级的金币/经验/成熟全表 —— 用来确认"等级到底乘在哪几处"。</summary>
        private void AppendAllSeedRamps()
        {
            _sb.AppendLine();
            _sb.AppendLine("  22 品种 × 1/2/3 级（金币 / 经验 / 成熟秒）：");
            SeedDef[] all = TableDatabase.Seeds;
            for (int i = 0; i < all.Length; i++)
            {
                SeedDef d = all[i];
                if (d == null) continue;
                var sb = new StringBuilder();
                for (int lv = SeedSlot.StartingLevel; lv <= SeedSlot.MaxLevel; lv++)
                {
                    var slot = new SeedSlot { Def = d, Quality = d.RarityTier, Level = lv };
                    float h = slot.LevelHarvestMult;
                    float sp = SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, lv);
                    float g = Mathf.Max(1f, Mathf.RoundToInt(SeedEconomy.CellGold(d) * h
                                                             * PlantMechanics.HarvestGoldMult(d, null)));
                    float e = Mathf.Max(1f, Mathf.RoundToInt(SeedEconomy.CellExp(d) * h));
                    sb.Append($"  Lv{lv} {g:0}/{e:0}/{d.GrowTime / sp:0.#}s");
                }
                _sb.AppendLine($"    {d.Id,-18}{d.Grade} {sb}");
            }
        }

        // ─────────────────────────── 杂项 ───────────────────────────

        private static string CamText()
        {
            Camera c = Camera.main;
            if (c == null) return "(没有 Camera.main)";
            Vector3 e = c.transform.eulerAngles;
            return $"{c.name} pos={c.transform.position.ToString("F1")} euler={e.ToString("F1")}"
                 + $" fov={c.fieldOfView:0.#} 像素={c.pixelWidth}×{c.pixelHeight}";
        }

        private static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

        private static int Rgb(Color c)
            => (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16)
             | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8)
             |  Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);

        private static string Col(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00},a{c.a:0.00})";

        private static GameBootstrap Live()
        {
            var list = GameBootstrap.Live;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Session != null && list[i].Session.World != null) return list[i];
            return null;
        }

        private string Fail(string why)
        {
            LastStatus = why;
            Log.Warn("[PROBEV208] " + why);
            return why;
        }

        private string WriteReport()
        {
            try
            {
                if (!Directory.Exists(LibraryDir)) Directory.CreateDirectory(LibraryDir);
                File.WriteAllText(ReportPath, _sb.ToString(), Encoding.UTF8);
                return ReportPath;
            }
            catch (Exception e)
            {
                Log.Error("[PROBEV208] 写读数失败：" + e.Message);
                return "(写文件失败：" + e.Message + ")";
            }
        }
    }
}
