using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v206 取证探针（**只读** · 平时什么都不做）★★
    ///
    /// 本轮三件事里两件需要**读数**、一件需要**图**，所以把它们收在同一个入口：
    ///   ① 灼烧/毒区/减速的**层数上限**：直接对 `PluginDotTable` 连施三次、把
    ///      `Stacks` / `PerSecond` 打出来（判据："连烧三次 ⇒ 仍是 1 层、每秒不翻倍"）
    ///   ② 敌人**霓虹红描边**：把每个角色的 renderer 数、**有霓虹的** renderer 数、
    ///      实际 `_NeonColor`/`_NeonWidth`（从 MPB 里读，不是读代码）+ `ToonLook` 材质池份数
    ///      —— 判据：敌人 > 0 且偏红、**玩家 = 0**、材质池份数**不随实例增长**
    ///   ③ 一张图：玩家 + 四只怪 + 一只小猪在同一帧里（离得近，红圈看得清）
    ///
    /// ★ 为什么抓图不用 `ScreenCapture.CaptureScreenshot(路径)` ✗ ★
    ///   同 `ProbeV196Fx4` 的记录：它把写盘排到帧末且没有返回值。这里用
    ///   `CaptureScreenshotAsTexture()` + `EncodeToPNG()` + `File.WriteAllBytes`，同步拿像素 ✓
    ///
    /// ★ 为什么**一个会话只抓一张** ★
    ///   URP 下同一会话的第二次渲染回读会拿到旧帧 ⇒ 图不可信。需要第二张就重开一个 play 会话 ✓
    ///
    /// ★ 它改了什么 ★
    ///   只在**局内**：清场、把玩家挪到空地、刷四只怪 + 一只小猪（与 `DebugApi.SpawnEnemy` 同一句）。
    ///   不写 `Assets/**`、不改表、不改场景；产物只落在 `<工程>/Library/` ✓
    ///
    /// ★ 怎么用（桥）★
    ///   `/input` action=send、path=ProbeV206Outline、method=ArrangeAndShoot、value=2.0
    ///   ⇒ 摆场面 → 等 2 秒（相机追随落定）→ **抓一张图** → 把读数写到 `Library/yowei-v206-report.txt`
    ///   `/input` ... method=SelfCheckNow ⇒ 跑一遍 `SelfCheck.RunAll`，结果写 `Library/yowei-v206-selfcheck.txt`
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV206Outline : MonoBehaviour
    {
        // ★ v207：**停用自动创建**（本探针本轮任务已完成：三件读数与那张图都拿到了）★
        //
        // 病因（Unity 原话）：
        //   `CreateImpl is not allowed to be called from a MonoBehaviour constructor
        //    (or instance field initializer), call it in Awake or Start instead.
        //    Called from MonoBehaviour 'ProbeV206Outline'`
        //   ⇒ 本类里有**实例字段初始化器**在做 CreateImpl 系的事（纹理/网格/材质/字体），
        //     而 `AddComponent` 会在构造阶段就跑字段初始化器 ⇒ 引擎直接抛 ✓
        //
        // ⚠️ 这条改动只切断"每次进 play 自动建实例"这一条路径，**没有**把那些字段搬进 `Awake`
        //    ⇒ 若日后手动 `AddComponent<ProbeV206Outline>()`，**同一个异常还会出现** ✗
        //    真要复用它，必须先把实例字段里的 `new Texture2D/Mesh/Material` 改成
        //    `Awake()` 或 `LazyInit()` 里创建（工程既有的"缓存只建一次"写法就是这样）✓
        //
        // 为什么摘掉自动创建是对的：取证探针本就该**平时什么都不做**，
        //   在每一次进 play 时被建出来属于越界 ✓
        // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate_Disabled_V207()
        {
            if (FindAnyObjectByType<ProbeV206Outline>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV206Outline");
            go.AddComponent<ProbeV206Outline>();
        }

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string LibraryDir => Path.Combine(ProjectRoot, "Library");
        private static string ReportPath => Path.Combine(LibraryDir, "yowei-v206-report.txt");
        private static string SelfCheckPath => Path.Combine(LibraryDir, "yowei-v206-selfcheck.txt");

        private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
        private static readonly int NeonWidthId = Shader.PropertyToID("_NeonWidth");
        private static readonly int TintMulId = Shader.PropertyToID("_TintMul");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        /// <summary>最近一次动作的结论（桥上看得见，不必去翻文件）。</summary>
        public static string LastStatus = "(还没跑过)";

        private readonly StringBuilder _sb = new StringBuilder();
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        // ─────────────────────────── 入口 ───────────────────────────

        /// <summary>摆场面 → 等若干秒（相机追随落定）→ 抓**一张**图 → 写读数。</summary>
        public string ArrangeAndShoot(string seconds)
        {
            float wait = 2.0f;
            if (!string.IsNullOrEmpty(seconds)) float.TryParse(seconds.Trim(), out wait);
            if (wait < 0.2f) wait = 0.2f;
            StartCoroutine(ArrangeAndShootCo(wait));
            LastStatus = "queued（等 " + wait.ToString("0.##") + " 秒后抓一张图）";
            return LastStatus;
        }

        /// <summary>只出读数（不抓图）。</summary>
        public string ReadReport()
        {
            GameBootstrap boot = Live();
            if (boot == null) return "没有活会话（先进 play 模式）";
            BuildReport(boot);
            return "ok：" + WriteReport();
        }

        /// <summary>跑一遍自检，结果落 `Library/yowei-v206-selfcheck.txt`。</summary>
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
                sb.Insert(0, head + "\n");
                Log.Info("[PROBEV206] " + head);
            }
            catch (Exception e)
            {
                sb.Insert(0, "自检 CRASH：" + e.GetType().Name + ": " + e.Message + "\n"
                             + (e.StackTrace ?? "").Split('\n')[0] + "\n");
                Log.Error("[PROBEV206] 自检 CRASH：" + e.GetType().Name + ": " + e.Message);
            }
            try
            {
                if (!Directory.Exists(LibraryDir)) Directory.CreateDirectory(LibraryDir);
                File.WriteAllText(SelfCheckPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e) { Log.Error("[PROBEV206] 写自检文件失败：" + e.Message); }
            LastStatus = sb.ToString().Split('\n')[0];
            return LastStatus;
        }

        // ─────────────────────────── 摆场面 + 抓图 ───────────────────────────

        private IEnumerator ArrangeAndShootCo(float wait)
        {
            GameBootstrap boot = Live();
            if (boot == null || boot.Session == null || boot.Session.World == null)
            {
                LastStatus = "没有活会话（先进 play 模式）";
                yield break;
            }
            RunSession s = boot.Session;
            World w = s.World;

            // ── ① 清场：把场上已有的敌人全部打死（与 `DebugApi.KillAll` 同一句），
            //      这样图里只有我摆的那五只，读数也不会被别人的层数污染 ──
            for (int i = w.Enemies.Count - 1; i >= 0; i--)
                if (w.Enemies[i] != null) w.Enemies[i].Health.Kill();
            yield return null;

            // ── ② 玩家挪到田地外侧的空地（离作物远一点，玩家身上不该有红圈）──
            Vector2 at = new Vector2(0f, -9.5f);
            if (w.Player != null)
            {
                w.Player.Pos = at;
                w.Player.Health.Heal(9999f);
            }
            yield return null;

            // ── ③ 四只敌人 + 一只小猪：贴玩家 1.4~2.6 米，保证同一帧里都在画面中央 ──
            //      种类混着放：普通兵 / 射手 / 跳砸 / BOSS 之外的最大体型，验"每只都红"
            //      小猪（Wander）**故意放近**：它是中立单位，判据是它**不该**有红圈 ✓
            SpawnSafe(w, "grunt",  at + new Vector2(-1.6f, 1.5f), 1);
            SpawnSafe(w, "grunt",  at + new Vector2(1.5f, 1.6f), 1);
            SpawnSafe(w, "shooter", at + new Vector2(-2.4f, 0.2f), 1);
            SpawnSafe(w, "jumper", at + new Vector2(2.4f, -0.2f), 1);
            SpawnSafe(w, "pig",    at + new Vector2(0.2f, 2.6f), 1);

            // ── ④ 等相机追上来（`followLerp = 10`，1 秒足够）──
            float t = 0f;
            while (t < wait) { t += Time.unscaledDeltaTime; yield return null; }

            // ── ⑤ 抓**唯一**一张图 ──
            // ★ `yield` 不能写在带 catch 的 try 里（CS1626）⇒ 把"等帧末"提到 try 外 ✓
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
            string shotPath = "(未写盘)";
            if (png != null)
            {
                try
                {
                    if (!Directory.Exists(LibraryDir)) Directory.CreateDirectory(LibraryDir);
                    shotPath = Path.Combine(LibraryDir, "yowei-v206-outline.png");
                    File.WriteAllBytes(shotPath, png);
                }
                catch (Exception e) { shot = shot + "｜写盘失败 " + e.Message; }
            }

            // ── ⑥ 读数 + 落盘 ──
            BuildReport(boot);
            _sb.AppendLine("── 图 ──");
            _sb.AppendLine("文件：" + shotPath);
            _sb.AppendLine("读数：" + shot);
            _sb.AppendLine("（同一会话只抓这一张 —— URP 下第二次回读会拿到旧帧）");
            string where = WriteReport();
            LastStatus = "ok：" + shot + " ⇒ " + where;
            Log.Info("[PROBEV206] " + LastStatus);
        }

        private static void SpawnSafe(World w, string id, Vector2 pos, int wave)
        {
            try { w.SpawnEnemy(id, pos, wave); }
            catch (Exception e) { Log.Warn("[PROBEV206] 刷 " + id + " 失败：" + e.GetType().Name + ": " + e.Message); }
        }

        // ─────────────────────────── 读数 ───────────────────────────

        private void BuildReport(GameBootstrap boot)
        {
            _sb.Clear();
            _sb.AppendLine("═══ v206 探针读数（只读）═══");
            _sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _sb.AppendLine();

            AppendDotCaps(boot.Session);
            AppendNeon(boot);
            AppendMaterialPool(boot);
            AppendScreenOutline();
        }

        /// <summary>
        /// ① 三个持续效果的**层数上限**：直接对表连施三次，看层数与每秒。
        /// 放在这里是因为"连烧三次 ⇒ 层数仍是 1、伤害不翻倍"这条判据需要**读数**，
        /// 而自检只给 PASS/FAIL（判据写错时两者不一样）。
        /// </summary>
        private void AppendDotCaps(RunSession s)
        {
            _sb.AppendLine("── ① 持续效果的层数上限（表内）──");
            _sb.AppendLine($"常量：BurnMaxStacks = {PluginDotTable.BurnMaxStacks}"
                         + $"｜PoisonMaxStacks = {PluginDotTable.PoisonMaxStacks}"
                         + $"｜SlowMaxStacks = {PluginDotTable.SlowMaxStacks}");
            PluginAsset burn = Plugin("hoe_burn");
            PluginAsset poison = Plugin("fert_poison");
            PluginAsset slow = Plugin("can_slow");
            _sb.AppendLine($"表内 maxStack：hoe_burn = {(burn != null ? burn.maxStack : -1)}"
                         + $"｜fert_poison = {(poison != null ? poison.maxStack : -1)}"
                         + $"｜can_slow = {(slow != null ? slow.maxStack : -1)}");
            if (burn != null) _sb.AppendLine("   灼烧描述：" + burn.description);
            if (poison != null) _sb.AppendLine("   毒素描述：" + poison.description);
            if (slow != null) _sb.AppendLine("   减速描述：" + slow.description);

            float melee = s != null && s.Progress != null ? s.Progress.Stats.Get(StatId.MeleeDamage) : 8f;
            float ranged = s != null && s.Progress != null ? s.Progress.Stats.Get(StatId.RangedDamage) : 6f;
            float blast = s != null && s.Progress != null ? s.Progress.Stats.Get(StatId.BlastDamage) : 12f;

            var t = new PluginDotTable();
            float perLayer = melee * 0.5f;
            t.ApplyBurn(1, perLayer, 1);
            string afterOne = $"{t.BurnStacks(1)} 层 / 每秒 {t.BurnPerSecond(1):0.00} / 剩 {t.BurnRemain(1):0.00}s";
            t.ApplyBurn(1, perLayer, 1); t.ApplyBurn(1, perLayer, 1);
            string afterThree = $"{t.BurnStacks(1)} 层 / 每秒 {t.BurnPerSecond(1):0.00} / 剩 {t.BurnRemain(1):0.00}s";
            _sb.AppendLine($"连烧三次：第 1 次 = {afterOne}");
            _sb.AppendLine($"            第 3 次 = {afterThree}"
                         + $"（判据：仍是 1 层、每秒 {(melee * 0.5f):0.00} ⇒ {(t.BurnStacks(1) == 1 ? "PASS" : "FAIL")}）");

            var p = new PluginDotTable();
            float perPoison = blast * 0.5f;
            p.AddPoisonZone(new Vector2(1f, 1f), 2.4f, perPoison, 1);
            p.AddPoisonZone(new Vector2(1f, 1f), 2.4f, perPoison, 1);   // 同一处再炸
            p.AddPoisonZone(new Vector2(1f, 1f), 2.4f, perPoison, 1);
            _sb.AppendLine($"同一处炸三次：毒区 {p.PoisonZoneCount} 块 / 每秒 {p.Rows[0].PerSecond:0.00}"
                         + $" / ×{p.Rows[0].Stacks} 层"
                         + $"（判据：仍 1 层、每秒 {(blast * 0.5f):0.00} ⇒ {(p.Rows[0].Stacks == 1 ? "PASS" : "FAIL")}）");

            var sl = new PluginDotTable();
            sl.ApplySlow(1, ranged * 0.5f, 0.30f, 1);
            sl.ApplySlow(1, ranged * 0.5f, 0.30f, 1);
            sl.ApplySlow(1, ranged * 0.5f, 0.30f, 1);
            _sb.AppendLine($"连减速三次：×{sl.SlowStacks(1)} 层 / 移速 ×{sl.SlowFactor(1):0.00}"
                         + $" / 每秒 {sl.SlowPerSecond(1):0.00}"
                         + $"（判据：仍 1 层、移速 ×0.70 ⇒ {(sl.SlowStacks(1) == 1 && Mathf.Abs(sl.SlowFactor(1) - 0.70f) < 0.001f ? "PASS" : "FAIL")}）");
            _sb.AppendLine();
        }

        /// <summary>
        /// ② 每个角色的霓虹：**从 MPB 里读**实际生效的颜色与宽度（不是读代码里的常量）。
        /// 判据：敌人 count &gt; 0 且红；玩家 = 0（没红圈）；小猪 = 0（中立单位）。
        /// </summary>
        private void AppendNeon(GameBootstrap boot)
        {
            _sb.AppendLine("── ② 敌人霓虹红描边（逐角色，读的是 MPB 实际值）──");
            // ★ `WorldView` 是**普通类**（不是 MonoBehaviour），拿不到它的 root 变换 ✗
            //   ⇒ 直接扫全场 Renderer，再按**对象名前缀**归组（名字是 `ModelView.Spawn` /
            //   `ProceduralBugs` 建对象时写死的，见 `MDL_<id>` / `BUG_<id>`）✓
            //   用**不带** `FindObjectsSortMode` 的重载：那两版都已废弃，会**多一条警告**（本工程基线是 3 条警告）
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            var groups = new Dictionary<string, NeonAgg>();
            Collect(all, groups);
            var keys = new List<string>(groups.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                NeonAgg g = groups[keys[i]];
                _sb.AppendLine($"{keys[i],-28} renderer {g.Renderers,3}｜有霓虹 {g.Neon,3}"
                             + $"｜色 {(g.Neon > 0 ? g.Color.ToString("F2") : "—")}"
                             + $"｜宽 {(g.Neon > 0 ? g.Width.ToString("0.0000") : "—")} 米");
            }
            _sb.AppendLine("判据：敌人（`BUG_` / `MDL_imp*` / `MDL_brute`）⇒ 有霓虹 > 0 且**红**；"
                         + "`MDL_farmer`（玩家）= 0；`MDL_pig`（小猪）= 0 ✓");
            _sb.AppendLine();
        }

        /// <summary>把每个对象（按名字前缀归组）的 renderer 数与霓虹读数收集起来。</summary>
        private void Collect(Renderer[] all, Dictionary<string, NeonAgg> groups)
        {
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                string owner = OwnerName(r.transform);
                if (!groups.TryGetValue(owner, out NeonAgg g)) { g = new NeonAgg(); groups[owner] = g; }
                g.Renderers++;
                r.GetPropertyBlock(_mpb);
                Color c = _mpb.GetColor(NeonColorId);
                float w = _mpb.GetFloat(NeonWidthId);
                if (c.a > 0.01f)
                {
                    g.Neon++;
                    if (g.Color.a <= 0.01f) { g.Color = c; g.Width = w; }
                }
            }
        }

        /// <summary>往上找第一个带已知前缀的祖先（敌人是 `BUG_xxx`，角色/作物是 `MDL_xxx`）。</summary>
        private static string OwnerName(Transform t)
        {
            for (Transform c = t; c != null; c = c.parent)
            {
                string n = c.name;
                if (n.StartsWith("BUG_", StringComparison.Ordinal)) return "敌人 " + n;
                if (n.StartsWith("MDL_", StringComparison.Ordinal)) return n;
            }
            return "(场景/装饰)";
        }

        /// <summary>③ 材质池份数：实例多了它也不该涨（染色/霓虹走 MPB，不是每实例 new Material）。</summary>
        private void AppendMaterialPool(GameBootstrap boot)
        {
            _sb.AppendLine("── ③ 材质池（判据：实例增长而份数不涨）──");
            int live = boot.Session != null && boot.Session.World != null ? boot.Session.World.Enemies.Count : 0;
            int poolNow = ToonLook.MaterialPoolCount;
            int matTotal = 0, matToon = 0;
            Material[] allMats = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < allMats.Length; i++)
            {
                if (allMats[i] == null) continue;
                matTotal++;
                if (allMats[i].shader == ToonLook.ToonShader) matToon++;
            }
            _sb.AppendLine($"场上敌人 {live} 只｜ToonLook 材质池 {poolNow} 份｜全场 Material 总数 {matTotal}｜其中 Seedfall/Toon {matToon} 份");

            // 再刷 6 只，看池子有没有涨（只在读数里做，不抓图）
            if (boot.Session != null && boot.Session.World != null && boot.Session.World.Player != null)
            {
                Vector2 at = boot.Session.World.Player.Pos;
                int n = boot.Session.World.Enemies.Count;
                for (int i = 0; i < 6; i++)
                    SpawnSafe(boot.Session.World, "grunt", World.ClampToArena(
                        at + new Vector2(3.5f + i * 0.7f, 3.5f), 0.5f, TableDatabase.Tuning.farm.arenaHalf), 1);
                int poolAfter = ToonLook.MaterialPoolCount;
                _sb.AppendLine($"又刷 6 只后：敌人 {boot.Session.World.Enemies.Count} 只（+{boot.Session.World.Enemies.Count - n}）"
                             + $"｜材质池 {poolAfter} 份（{(poolAfter == poolNow ? "**没涨** ✓" : "涨了 " + (poolAfter - poolNow) + " 份 ✗")}）");
            }
            _sb.AppendLine();
        }

        /// <summary>④ 全屏后处理描边（`ScreenOutlineFeature`）现在的开关状态 —— 只读，不动它。</summary>
        private void AppendScreenOutline()
        {
            _sb.AppendLine("── ④ 全屏后处理描边 `ScreenOutlineFeature` 的开关状态（本轮没动它）──");
#if UNITY_EDITOR
            string[] paths = { "Assets/Settings/PC_Renderer.asset", "Assets/Settings/Mobile_Renderer.asset" };
            for (int i = 0; i < paths.Length; i++)
            {
                var data = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.ScriptableRendererData>(paths[i]);
                if (data == null) { _sb.AppendLine(Path.GetFileName(paths[i]) + "：找不到"); continue; }
                var so = new UnityEditor.SerializedObject(data);
                var list = so.FindProperty("m_RendererFeatures");
                bool found = false;
                if (list != null)
                {
                    for (int k = 0; k < list.arraySize; k++)
                    {
                        UnityEngine.Object feature = list.GetArrayElementAtIndex(k).objectReferenceValue;
                        if (feature == null || !(feature is ScreenOutlineFeature)) continue;
                        found = true;
                        var fso = new UnityEditor.SerializedObject(feature);
                        var act = fso.FindProperty("m_Active");
                        var str = fso.FindProperty("strength");
                        var sv = fso.FindProperty("enableInSceneView");
                        _sb.AppendLine(Path.GetFileName(paths[i]) + "：**已注册** ScreenOutlineFeature"
                                     + $"｜m_Active = {(act != null && act.boolValue ? "1（开）" : "0（关）")}"
                                     + $"｜strength = {(str != null ? str.floatValue.ToString("0.##") : "?")}"
                                     + $"｜enableInSceneView = {(sv != null && sv.boolValue ? 1 : 0)}");
                    }
                }
                if (!found) _sb.AppendLine(Path.GetFileName(paths[i]) + "：**没有**装 ScreenOutlineFeature");
            }
#else
            _sb.AppendLine("（非编辑器构建，读不到渲染器资产）");
#endif
            _sb.AppendLine("它描的是**全部**（深度/法线突变），与「敌人红霓虹」是两条独立的通道 ✓");
            _sb.AppendLine();
        }

        // ─────────────────────────── 杂项 ───────────────────────────

        /// <summary>按 id 取插件行（与 `ProbeV200` 同一个写法：表没有按 id 的索引，就线性扫）。</summary>
        private static PluginAsset Plugin(string id)
        {
            PluginAsset[] all = TableDatabase.Plugins;
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }

        private static GameBootstrap Live()
        {
            var list = GameBootstrap.Live;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Session != null && list[i].Session.World != null) return list[i];
            return null;
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
                Log.Error("[PROBEV206] 写读数失败：" + e.Message);
                return "(写文件失败：" + e.Message + ")";
            }
        }

        private struct NeonAgg
        {
            public int Renderers;
            public int Neon;
            public Color Color;
            public float Width;
        }
    }
}
