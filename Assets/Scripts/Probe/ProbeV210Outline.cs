using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v210 描边取证探针（**只读** · 只在桥调用时干活 · 不注册任何引擎回调）★★
    ///
    /// 要回答的问题只有一个：**敌人身上那圈红色发光描边，到底有没有被画到屏幕上**。
    /// 用户已经连着三轮说"还是没有"⇒ 这一轮不接受"代码里有"，只接受**读数**与**像素**。
    ///
    /// ── 为什么要量那四个数 ──
    /// `SeedfallToon.shader` 的霓虹 Pass 有两道闸门，任何一道关着，屏幕上就是一个像素都没有：
    ///   ① 几何闸：`neonWidth = max(_NeonWidth, 每像素米数 × _NeonPixels)`
    ///      再 `posWS += nrm * (_OutlineWidth + neonWidth)` ⇒ 环带 `[R + 描边, R + 描边 + 霓虹宽]`
    ///   ② 亮度闸：`fade = 1 − 距离 / _NeonFade`，`fade < 0.02` 直接 `clip` 掉
    /// ⇒ `Measure` 打印的是：**材质上的真属性值**、**MPB 上的逐实例值**、
    ///   **相机到这个渲染器的实际距离**、以及**按这两个值算出来的淡出系数**。
    ///   四个数缺一个都判不了案（上一轮就是只看了"代码里写了红"，没看距离）。
    ///
    /// ── `Pixels` 为什么必须读像素 ──
    /// "红描边存在"与"红描边看得见"是两件事。`Blend One One`（加色）在**亮草地**上
    /// 把红加到 (0.45,0.60,0.32) 上得到 (1.45,0.75,0.50) —— 读起来是**米黄**，不是红 ✗
    /// 唯一能证明"屏幕上真的有一圈红"的办法，是把当帧的帧缓冲读回来数像素 ✓
    ///
    /// ★ 抓图用 `CaptureScreenshotAsTexture()`（同步拿像素）★
    ///   不用 `ScreenCapture.CaptureScreenshot(路径)`：那个把写盘排到帧末且没有返回值，
    ///   拿不到像素就没法数，"数不出来"会被误读成"没有红" ✗
    ///
    /// ★ 它改了什么 ★
    ///   不改 `Assets/**`、不改表、不改场景。产物只落在 `<工程>/Library/yowei-v210-*` ✓
    ///
    /// ★ 硬约束：**字段初始化器里不许创建 `Texture2D/Mesh/Material`** ★
    ///   （`ProbeV206Outline` 就是被这条炸的）⇒ 这里全部走惰性/局部变量 ✓
    ///
    /// ★ 怎么用（桥）★
    ///   `POST /scene {"action":"create","kind":"empty","name":"ProbeV210","script":"ProbeV210Outline"}`
    ///   `POST /input {"action":"send","path":"ProbeV210","type":"ProbeV210Outline","method":"Measure"}`
    ///   `POST /input {... "method":"Pixels","value":"v210-before"}`
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV210Outline : MonoBehaviour
    {
        private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
        private static readonly int NeonWidthId = Shader.PropertyToID("_NeonWidth");
        private static readonly int NeonPixelsId = Shader.PropertyToID("_NeonPixels");
        private static readonly int NeonFadeId = Shader.PropertyToID("_NeonFade");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int OutlineDepthFadeId = Shader.PropertyToID("_OutlineDepthFade");
        private static readonly int OutlinePixelsId = Shader.PropertyToID("_OutlinePixels");

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string LibraryDir => Path.Combine(ProjectRoot, "Library");

        // ─────────────────────── 自动建实例 ───────────────────────
        //
        // ★ 为什么本类**可以**带自动创建 ★
        //
        // `ProbeV206Outline` 当年就是被这条炸的，病因写在它自己的类注释里（Unity 原话）：
        //   `CreateImpl is not allowed to be called from a MonoBehaviour constructor
        //    (or instance field initializer)`
        // ⇒ 那个类里有**实例字段初始化器**在建 `Texture2D/Mesh/Material`，
        //   而 `AddComponent` 会在**构造阶段**就跑字段初始化器 ⇒ 引擎抛异常。
        //
        // 本类没有任何实例字段初始化器（`_mpb` / `_lastGroups` 都是"用到才建"，
        // 且都不在字段初始化器里 new）⇒ 构造阶段一个 Unity 对象都不创建 ⇒ **不会踩那条** ✓
        // 这正是"别带自动创建"那条经验的**真实边界**：禁的是"字段初始化器里建引擎对象"，
        // 不是"自动创建"这个动作本身 ✓ 而且不自动创建，桥就没有任何入口能挂上本组件
        // （`/scene` 的 create 在 play 模式被 Unity 直接拒绝："This cannot be used during play mode."）✓
        // 另外：**不注册 Update/LateUpdate** ⇒ 场上一分钱不花 ✓
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            try
            {
                if (FindAnyObjectByType<ProbeV210Outline>(FindObjectsInactive.Include) != null) return;
                var go = new GameObject("ProbeV210");
                go.AddComponent<ProbeV210Outline>();
            }
            catch (Exception e)
            {
                Log.Warn("[PROBEV210] 自动创建失败（不影响玩法）：" + e.Message);
            }
        }

        /// <summary>最近一次动作的结论（桥上直接看得见）。</summary>
        public static string LastStatus = "(还没跑过)";

        /// <summary>惰性创建的 MPB —— **不在字段初始化器里 new**（见类注释的硬约束）。</summary>
        private MaterialPropertyBlock _mpb;
        private MaterialPropertyBlock Mpb => _mpb != null ? _mpb : (_mpb = new MaterialPropertyBlock());

        // ─────────────────────────── ① 读数 ───────────────────────────

        /// <summary>逐组打印"四个数"＋淡出系数。写 `Library/yowei-v210-measure.txt`。</summary>
        public string Measure(string spec)
        {
            Camera cam = Camera.main;
            if (cam == null) return Fail("没有 Camera.main（先进 play 模式）");
            var groups = Collect(cam, out float cmPerPixelAt1m, out float screenH, out float m11);

            var sb = new StringBuilder();
            sb.AppendLine("═══ v210 描边读数（只读 · 从材质与 MPB 里读，不读代码里的常量）═══");
            sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"相机 {cam.name}｜pos={cam.transform.position.ToString("F2")}"
                        + $"｜euler={cam.transform.eulerAngles.ToString("F1")}"
                        + $"｜fov={cam.fieldOfView:0.#}｜{cam.pixelWidth}×{cam.pixelHeight}");
            sb.AppendLine($"  每像素（1 米处）= {cmPerPixelAt1m:0.0000} 厘米"
                        + $" ⇒ 30 米处 1 像素 ≈ {cmPerPixelAt1m * 30f:0.00} 厘米"
                        + $"｜m11={m11:0.000} 像素高={screenH:0}");

            var keys = new List<string>(groups.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                Agg a = groups[keys[i]];
                if (a.Renderers >= 200) continue;                 // 装饰/草地不看，省篇幅
                sb.AppendLine();
                sb.Append($"  {keys[i],-26} renderer={a.Renderers,3}｜{a.Shader}｜材质 {a.MatName}");
                if (!a.HasNeon)
                {
                    sb.AppendLine("\n      **霓虹关闭**（_NeonColor.a = 0 ⇒ 顶点阶段整块丢弃）");
                    continue;
                }
                sb.AppendLine();

                float d = a.Dist;
                float perPixel = cmPerPixelAt1m * d / 100f;        // 米/像素
                float pixelFloor = perPixel * a.NeonPixels;
                float neonWidth = Mathf.Max(a.NeonWidth, pixelFloor);
                float neonFade = 1f - d / Mathf.Max(0.001f, a.NeonFade);
                float outlineFade = 1f - d / Mathf.Max(0.001f, a.OutlineFade);
                float outlinePx = a.OutlineWidth / Mathf.Max(1e-6f, perPixel);
                float neonPx = neonWidth / Mathf.Max(1e-6f, perPixel);
                float matOutlinePx = float.IsNaN(a.MatOutlinePixels)
                    ? a.OutlineWidth / Mathf.Max(1e-6f, perPixel)
                    : Mathf.Max(a.OutlineWidth, perPixel * a.MatOutlinePixels) / Mathf.Max(1e-6f, perPixel);

                sb.AppendLine($"      ① _OutlineWidth     = {a.OutlineWidth * 100f:0.000} cm"
                            + $" = {outlinePx:0.00} 屏幕像素"
                            + $"｜_OutlinePixels(材质)={(float.IsNaN(a.MatOutlinePixels) ? "—" : a.MatOutlinePixels.ToString("0.00"))}"
                            + $" ⇒ 墨线实际 {matOutlinePx:0.00} 像素");
                sb.AppendLine($"      ② _OutlineDepthFade = {a.OutlineFade:0.0} m   ＋ 相机距离 {d:0.00} m"
                            + $" ⇒ 墨线淡出系数 {outlineFade:0.000}"
                            + (outlineFade <= 0.02f ? "   ✗ <0.02 ⇒ **片元被 clip，墨线零像素**" : "   ✓ 活着"));
                sb.AppendLine($"      ③ _NeonColor(MPB)   = {Col(a.NeonColor)}"
                            + $"｜_NeonWidth(MPB) = {a.NeonWidth * 100f:0.00} cm"
                            + $"｜_NeonPixels(MPB→材质) = {a.NeonPixels:0.0} px");
                sb.AppendLine($"      ④ _NeonFade         = {a.NeonFade:0.0} m   ⇒ 霓虹淡出系数 {neonFade:0.000}"
                            + (neonFade <= 0.02f ? "   ✗ **被 clip**" : ""));
                sb.AppendLine($"      ⇒ 霓虹宽 = max({a.NeonWidth * 100f:0.00}cm, {a.NeonPixels:0.0}px×{perPixel * 100f:0.00}cm)"
                            + $" = **{neonWidth * 100f:0.00} cm = {neonPx:0.00} 屏幕像素**");
                sb.AppendLine($"      ⇒ 霓虹环带 = [R + {a.OutlineWidth * 100f:0.00}cm, R + {(a.OutlineWidth + neonWidth) * 100f:0.00}cm]");
                sb.AppendLine($"      霓虹关键词：{DescribeKeywords()}");
            }
            sb.AppendLine();
            sb.AppendLine("判据：②③④ 任一项被判死 ⇒ 屏幕上不可能有那一圈；都活着却仍看不见 ⇒"
                        + " 是**加色混合在亮背景上被冲淡**（见 Pixels 数出来的红像素数与最红偏差）。");

            string text = sb.ToString();
            Write("yowei-v210-measure.txt", text);
            LastStatus = "Measure 完成";
            Log.Info("PROBEV210 measure\n" + text);
            return Head(text);
        }

        /// <summary>
        /// 霓虹 Pass 里没有任何 `shader_feature` ⇒ 它**不可能因为关键字缺失被编译掉**。
        /// 这条要打印，因为"属性名不存在 ⇒ 整段被编译掉"是上一轮的怀疑对象之一，必须排除。
        /// </summary>
        private static string DescribeKeywords()
        {
            Shader sh = Shader.Find("Seedfall/Toon");
            if (sh == null) return "**Seedfall/Toon 找不到**";
            Material m = new Material(sh);
            try
            {
                string[] kw = m.shaderKeywords;
                return kw == null || kw.Length == 0
                    ? "材质上零关键字 ⇒ 霓虹 Pass 没有任何 #if 分支，不会被编译掉 ✓"
                    : string.Join(",", kw);
            }
            finally { UnityEngine.Object.DestroyImmediate(m); }
        }

        // ─────────────────────────── ② 数像素 ───────────────────────────

        /// <summary>
        /// ★ 决定性的存在性实验：把**所有** `Seedfall/Toon` 渲染器的霓虹改成
        /// **洋红 (1,0,1)**、宽度 **50 厘米**、像素下限 **8** ★
        ///
        /// ── 为什么非要做这一步 ──
        /// "属性值都对、几何关系也对、屏幕上一个像素都没有"这件事已经连续三轮了。
        /// 只读数据的探针**区分不了**下面两种情况，而它们的修法完全不同：
        ///   ① 霓虹 Pass **根本没被画**（URP 没选它 / 顶点阶段被早退 / MPB 没进 GPU）
        ///   ② 霓虹 Pass 画了，但被**别的什么东西盖住/冲淡**了
        /// 把颜色换成洋红、宽度放大到 50 厘米 ⇒ ①会得到**整帧一个洋红像素都没有**，
        /// ②会得到**一大片洋红**。这一条是二分的，不靠眼睛估 ✓
        ///
        /// ★ 它**改了**运行时的 MPB（只在局内，退出 play 自动还原）—— 这是本探针唯一一处写操作，
        ///   而且写的就是"让效果夸张到不可能看不见"，不是伪装一个通过 ✓
        /// </summary>
        public string Paint(string spec)
        {
            Color c = new Color(1f, 0f, 1f, 1f);
            float width = 0.50f;
            float pixels = 8f;
            string[] parts = (spec ?? string.Empty).Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.StartsWith("r=", StringComparison.Ordinal)) c.r = ParseF(p.Substring(2), 1f);
                else if (p.StartsWith("g=", StringComparison.Ordinal)) c.g = ParseF(p.Substring(2), 0f);
                else if (p.StartsWith("b=", StringComparison.Ordinal)) c.b = ParseF(p.Substring(2), 1f);
                else if (p.StartsWith("a=", StringComparison.Ordinal)) c.a = ParseF(p.Substring(2), 1f);
                else if (p.StartsWith("w=", StringComparison.Ordinal)) width = ParseF(p.Substring(2), 0.5f);
                else if (p.StartsWith("px=", StringComparison.Ordinal)) pixels = ParseF(p.Substring(3), 8f);
            }

            int painted = 0;
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null) continue;
                string sn = r.sharedMaterial.shader.name;
                if (!sn.StartsWith("Seedfall/Toon", StringComparison.Ordinal)) continue;
                if (sn.EndsWith("ToonSimple", StringComparison.Ordinal)) continue;   // 简化版没有霓虹 Pass
                r.GetPropertyBlock(Mpb);
                Mpb.SetColor(NeonColorId, c);
                Mpb.SetFloat(NeonWidthId, width);
                Mpb.SetFloat(NeonPixelsId, pixels);
                r.SetPropertyBlock(Mpb);
                painted++;
            }
            LastStatus = $"Paint：{painted} 个渲染器 → 霓虹 {Col(c)} 宽 {width * 100f:0}cm / {pixels:0}px";
            Log.Info("PROBEV210 " + LastStatus);
            return LastStatus;
        }

        private static float ParseF(string text, float fallback)
            => float.TryParse(text, System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        /// <summary>
        /// 读**当帧**帧缓冲，按每个敌人的屏幕框数"红主导"像素。写 PNG + 读数 txt。
        ///
        /// 红主导判据（改前改后同一把尺）：r &gt; 0.30 且 r &gt; g×1.25 且 r &gt; b×1.25。
        /// 草地 (0.45,0.60,0.32) 不满足；被加色冲淡成米黄的边缘 (1.0,0.75,0.50) 也不满足
        /// ⇒ 这个判据数的**正好是"肉眼读成红色"的那些像素** ✓
        /// </summary>
        public string Pixels(string label)
        {
            string tag = string.IsNullOrEmpty(label) ? "shot" : label.Trim();
            Camera cam = Camera.main;
            if (cam == null) return Fail("没有 Camera.main");

            Texture2D tex;
            try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception e) { return Fail("抓屏失败：" + e.Message); }
            if (tex == null) return Fail("抓屏返回 null");

            int w = tex.width, h = tex.height;
            Color32[] px = tex.GetPixels32();
            string folder = Path.Combine(LibraryDir, "yowei-v210");
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, tag + ".png"), tex.EncodeToPNG());
            }
            catch (Exception e) { Log.Warn("[PROBEV210] 写图失败：" + e.Message); }
            UnityEngine.Object.DestroyImmediate(tex);

            var sb = new StringBuilder();
            sb.AppendLine($"═══ v210 像素读数 · {tag} ═══");
            sb.AppendLine($"帧 {w}×{h}｜相机 {cam.name} pos={cam.transform.position.ToString("F1")}"
                        + $"｜fov={cam.fieldOfView:0.#}");
            int allRed = CountRed(px, w, 0, 0, w - 1, h - 1, out int allDiff);
            sb.AppendLine($"  整帧红主导像素：**{allRed}** 个｜最红像素 r−g = {allDiff / 255f:0.00}");

            // ★ 取**画面上最大的 3 个敌人渲染器**当样本 ★
            //   不用"该组第一个"：`FindObjectsByType` 的顺序与画面无关，
            //   基线那一版的样本框就落在一块栅栏上（框里是木板 + 一朵蓝花，根本不是敌人）✗
            var picks = new List<(Renderer r, Bounds b, int x0, int y0, int x1, int y1, int area)>();
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            var seenGroup = new HashSet<string>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                string g = GroupName(r.transform);
                if (!g.StartsWith("敌人", StringComparison.Ordinal)) continue;
                if (!seenGroup.Add(r.transform.root.name + "/" + g)) continue;
                Bounds b = r.bounds;
                if (!ProjectBox(cam, b, w, h, out int x0, out int y0, out int x1, out int y1)) continue;
                picks.Add((r, b, x0, y0, x1, y1, (x1 - x0 + 1) * (y1 - y0 + 1)));
            }
            picks.Sort((p, q) => q.area.CompareTo(p.area));
            int shown = 0;
            for (int i = 0; i < picks.Count && shown < 3; i++)
            {
                var p = picks[i];
                const int pad = 6;
                int n = CountRed(px, w, p.x0 - pad, p.y0 - pad, p.x1 + pad, p.y1 + pad, out int maxD);
                int boxArea = (p.x1 - p.x0 + 1) * (p.y1 - p.y0 + 1);
                float d = Vector3.Distance(cam.transform.position, p.b.center);
                sb.AppendLine($"  敌人#{shown + 1} `{p.r.transform.root.name}`/{p.r.name}"
                            + $" 屏幕框 [{p.x0},{p.y0}]–[{p.x1},{p.y1}]（{p.x1 - p.x0 + 1}×{p.y1 - p.y0 + 1} px）"
                            + $" 距 {d:0.0}m");
                sb.AppendLine($"      外扩{pad}px 内红主导 **{n}** 个｜占框 {(float)n / boxArea * 100f:0.0}%"
                            + $"｜最红 r−g = {maxD / 255f:0.00}");
                TrySaveCrop(px, w, h, p.x0 - 12, p.y0 - 12, p.x1 + 12, p.y1 + 12, $"{tag}-enemy{shown + 1}", folder);
                shown++;
            }
            if (shown == 0) sb.AppendLine("  画面里没有敌人在框内（先把敌人调到相机附近）");

            string text = sb.ToString();
            Write("yowei-v210-pixels-" + tag + ".txt", text);
            LastStatus = $"Pixels {tag}：整帧红 {allRed} 个";
            Log.Info("PROBEV210 pixels\n" + text);
            return Head(text);
        }

        /// <summary>把某块区域放大 6 倍另存一张 —— 判据"一眼看得出来"要能数清像素。</summary>
        private static void TrySaveCrop(Color32[] px, int w, int h, int x0, int y0, int x1, int y1,
                                        string name, string folder)
        {
            try
            {
                if (x0 < 0) x0 = 0;
                if (y0 < 0) y0 = 0;
                if (x1 > w - 1) x1 = w - 1;
                if (y1 > h - 1) y1 = h - 1;
                int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
                if (cw < 2 || ch < 2) return;
                const int scale = 6;
                var small = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                var colors = new Color[cw * ch];
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                        colors[y * cw + x] = px[(y0 + y) * w + (x0 + x)];   // 帧缓冲原点在左下，与 PNG 一致
                small.SetPixels(colors);
                small.Apply();
                var big = new Texture2D(cw * scale, ch * scale, TextureFormat.RGBA32, false);
                var outColors = new Color[cw * scale * ch * scale];
                for (int y = 0; y < ch * scale; y++)
                    for (int x = 0; x < cw * scale; x++)
                        outColors[y * cw * scale + x] = colors[(y / scale) * cw + (x / scale)];
                big.SetPixels(outColors);
                big.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + "-zoom.png"), big.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(big);
            }
            catch (Exception e) { Log.Warn("[PROBEV210] 裁剪图失败：" + e.Message); }
        }

        private int CountRed(Color32[] px, int w, int x0, int y0, int x1, int y1, out int maxDiff)
        {
            maxDiff = 0;
            int n = 0;
            int h = px.Length / w;
            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 > w - 1) x1 = w - 1;
            if (y1 > h - 1) y1 = h - 1;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * w;
                for (int x = x0; x <= x1; x++)
                {
                    Color32 c = px[row + x];
                    int diff = c.r - c.g;
                    if (diff > maxDiff) maxDiff = diff;
                    if (c.r > 76 && c.r > c.g * 1.25f && c.r > c.b * 1.25f) n++;
                }
            }
            return n;
        }

        /// <summary>世界包围盒 → 屏幕整数框（Unity 屏幕原点在左下）。</summary>
        private static bool ProjectBox(Camera cam, Bounds b, int w, int h,
                                       out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = int.MaxValue;
            x1 = y1 = int.MinValue;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                            (i & 2) == 0 ? -e.y : e.y,
                                            (i & 4) == 0 ? -e.z : e.z);
                Vector3 s = cam.WorldToScreenPoint(p);
                if (s.z <= 0f) return false;
                if (s.x < x0) x0 = Mathf.RoundToInt(s.x);
                if (s.y < y0) y0 = Mathf.RoundToInt(s.y);
                if (s.x > x1) x1 = Mathf.RoundToInt(s.x);
                if (s.y > y1) y1 = Mathf.RoundToInt(s.y);
            }
            return x1 >= 0 && x0 < w && y1 >= 0 && y0 < h;
        }

        // ─────────────────────────── 共用 ───────────────────────────

        private sealed class Agg
        {
            public int Renderers;
            public bool HasNeon, HasBounds;
            public string Shader, MatName;
            public float OutlineWidth = float.NaN, OutlineFade = float.NaN;
            public float NeonFade = float.NaN, MatNeonPixels = float.NaN, MatOutlinePixels = float.NaN;
            public Color NeonColor;
            public float NeonWidth, NeonPixels, Dist;
            public Bounds Bounds;
        }

        /// <summary>扫全场的 Seedfall/Toon 渲染器，按名字前缀归组，取每组的**代表渲染器**。</summary>
        private Dictionary<string, Agg> Collect(Camera cam, out float cmPerPixelAt1m, out float screenH, out float m11)
        {
            screenH = cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height;
            m11 = cam.projectionMatrix.m11;
            cmPerPixelAt1m = 100f * 2f / Mathf.Max(0.0001f, m11 * screenH);

            var groups = new Dictionary<string, Agg>();
            Renderer[] all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;
                Material shared = r.sharedMaterial;
                if (shared == null || shared.shader == null) continue;
                if (!shared.shader.name.StartsWith("Seedfall/Toon", StringComparison.Ordinal)) continue;

                string g = GroupName(r.transform);
                if (!groups.TryGetValue(g, out Agg a))
                {
                    a = new Agg { Shader = shared.shader.name, MatName = shared.name };
                    if (shared.HasProperty(OutlineWidthId)) a.OutlineWidth = shared.GetFloat(OutlineWidthId);
                    if (shared.HasProperty(OutlineDepthFadeId)) a.OutlineFade = shared.GetFloat(OutlineDepthFadeId);
                    if (shared.HasProperty(NeonFadeId)) a.NeonFade = shared.GetFloat(NeonFadeId);
                    if (shared.HasProperty(NeonPixelsId)) a.MatNeonPixels = shared.GetFloat(NeonPixelsId);
                    if (shared.HasProperty(OutlinePixelsId)) a.MatOutlinePixels = shared.GetFloat(OutlinePixelsId);
                    groups[g] = a;
                }
                a.Renderers++;

                r.GetPropertyBlock(Mpb);
                Color nc = Mpb.GetColor(NeonColorId);
                if (nc.a > 0.01f && !a.HasNeon)
                {
                    a.HasNeon = true;
                    a.NeonColor = nc;
                    // ★ 逐实例值必须**从 MPB 读**：`_NeonWidth`/`_NeonPixels` 都是逐实例写的，
                    //   材质上那份是"没被覆盖时的默认值"，拿它算出来的宽度不是屏幕上的宽度 ✗
                    a.NeonWidth = Mpb.GetFloat(NeonWidthId);
                    float mp = Mpb.GetFloat(NeonPixelsId);
                    a.NeonPixels = mp > 0.0001f ? mp : a.MatNeonPixels;
                    a.Dist = Vector3.Distance(cam.transform.position, r.bounds.center);
                    a.Bounds = r.bounds;
                    a.HasBounds = true;
                }
                else if (!a.HasBounds)
                {
                    a.Dist = Vector3.Distance(cam.transform.position, r.bounds.center);
                    a.Bounds = r.bounds;
                    a.HasBounds = true;
                }
            }
            return groups;
        }

        /// <summary>按对象名前缀归组：敌人 / 玩家 / 植物 / 其它。</summary>
        private static string GroupName(Transform t)
        {
            for (Transform c = t; c != null; c = c.parent)
            {
                string n = c.name;
                if (n.StartsWith("BUG_", StringComparison.Ordinal)) return "敌人(程序化)";
                if (n.StartsWith("MDL_imp", StringComparison.Ordinal)) return "敌人 imp";
                if (n.StartsWith("MDL_brute", StringComparison.Ordinal)) return "敌人 brute";
                if (n.StartsWith("Enemy_", StringComparison.Ordinal)) return "敌人(图元)";
                if (n.StartsWith("MDL_farmer", StringComparison.Ordinal)) return "玩家";
                if (n.StartsWith("MDL_pig", StringComparison.Ordinal)) return "小猪(中立)";
                if (n.StartsWith("MDL_farm_plot", StringComparison.Ordinal)) return "耕地";
                if (n.StartsWith("MDL_", StringComparison.Ordinal)) return "植物 " + n;
            }
            return "(场景/装饰)";
        }

        private static string Col(Color c) => $"({c.r:0.00},{c.g:0.00},{c.b:0.00},a{c.a:0.00})";

        private static string Head(string text)
        {
            string[] lines = text.Split('\n');
            return lines.Length <= 26 ? text : string.Join("\n", lines, 0, 26) + "\n…（全文见 Library/）";
        }

        private static void Write(string file, string text)
        {
            try
            {
                if (!Directory.Exists(LibraryDir)) Directory.CreateDirectory(LibraryDir);
                File.WriteAllText(Path.Combine(LibraryDir, file), text, Encoding.UTF8);
            }
            catch (Exception e) { Log.Warn("[PROBEV210] 写读数失败：" + e.Message); }
        }

        private string Fail(string why)
        {
            LastStatus = why;
            Log.Warn("[PROBEV210] " + why);
            return why;
        }
    }
}
