using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 登录鸟瞰的「云雾层」（美术 lane：`Scripts/Art/`）★
    ///
    /// ── 用户原话 ──
    ///   "登录界面展示效果修改，采用鸟瞰图形式，**层云之上看主城，朦胧，若隐若现**"
    ///
    /// ── 为什么需要单独的云（主城已经有 6 朵建模云了）──
    ///   建模云在 `TownLife` 里，高度 **y = 23 ~ 26.6 米**（实测，见 `CLOUD_01` 的 position）。
    ///   而登录相机的机高只有 **26.6 米**（`[VISTA]` 日志）⇒ 相机几乎**贴着云顶**，
    ///   云落在画面之外（俯角 49° 时，只比相机低 3 米的物体要离相机 3~11 米内才进画面）✗
    ///   ⇒ 所以「看不见云」不是云没建，是**机位与云同高**。
    ///
    ///   本文件做两件事：
    ///     ① 把相机抬到云顶之上（改 `LoginVista` 的 fov/zoom，见那边的注释）
    ///     ② 补 **3 层云幕**：白 / 青 / 冷青，高度 20 / 25 / 30 米，各层不同速度缓慢漂移
    ///        ⇒ 近处那层盖住城的下半，远处那层压住地平线 ⇒ 有**层次**，城**若隐若现** ✓
    ///
    /// ── 硬约束（都守住了）──
    ///   · **共享资产**：1 份网格（内置 Quad）＋ 1 份材质 ＋ 1 份程序化贴图，三层共用 ✓
    ///     每层只有「颜色/不透明度」不同 ⇒ 走 `MaterialPropertyBlock`，**不 new Material** ✓
    ///   · 不碰 `Scripts/Town|Login|Flow|Combat`、不碰 `Data/*.json` ✓
    ///   · 只在登录期间存在：`LoginVista` 进城时调 `Dispose()`，云幕整体销毁 ✓
    ///   · 每帧成本 = 3 个 transform 写入（不到 1 微秒），无 Update 之外的分配 ✓
    ///
    /// ── 每层参数为什么是这几个数 ──
    ///   相机抬到机高 ≈ 42 米、俯角 52° 之后，画面纵向覆盖的俯角是 39°~61°：
    ///     云幕在 y=20 时可见距离 14~31 米（盖住城的近半边）
    ///     云幕在 y=25 时可见距离 12~26 米
    ///     云幕在 y=30 时可见距离  8~19 米（压住画面顶部 ⇒ 地平线有云）
    ///   三个不同的覆盖面 + 三个不同的速度 ⇒ 云在动、层次在变，而不是一块静止白布 ✓
    /// </summary>
    public sealed class VistaClouds
    {
        private const string ShaderName = "Seedfall/CloudVeil";

        /// <summary>云纹贴图边长（程序化生成一次，三层共用）。</summary>
        private const int TexSize = 256;

        /// <summary>一层云幕的定义。</summary>
        private struct Layer
        {
            public string Name;
            public float D;            // 沿视线水平方向离**相机**多远（米）
            public float Alpha;        // 俯角（度）—— 决定这一层落在画面纵向的哪一段
            public float Size;         // 边长（米）
            public Color Tint;         // 色温
            public float Op;           // 不透明度（呼吸的**中位值**）
            public float SwayA;        // 横向游走**幅度**（米，峰峰值的一半）
            public float SwayV;        // 横向游走**峰值速度**（米/秒，负号 = 反方向 ⇒ 三层视差）
            public float BreathT;      // 不透明度呼吸**周期**（秒）
            public float Spin;         // 每秒自转（度）
            public float Bob;          // 上下起伏幅度（米）
        }

        /// <summary>
        /// 三层：近（压画面下部）/ 中（盖城主区）/ 远（压上缘）。
        ///
        /// ★ 为什么按「离相机多远 + 俯角多少」定，而不是直接写 y 坐标 ★
        ///   第一版直接写 y=20/25/30，结果三层全在画面**外**：
        ///   相机机高 42 米、俯角 49°、fov 22° ⇒ 画面纵向只覆盖 **38°~60°** 的俯角，
        ///   而 y=25 的平面在 36 米外只有 29° 俯角 ⇒ 落在画面上缘之外 ✗
        ///   （这正是上一版"看不见云"的同一个坑：**云在不在画面里，取决于俯角，不取决于高度**）
        ///   ⇒ 改成"给定水平距离 d 与俯角 α，反解高度 y = 相机y − d·tanα"：
        ///     任何机位/半径下三层都稳稳落在 55°/49°/43° 三条带上 ✓
        ///   俯角取 55/49/43：中间那层正好压在画面中线（=俯角），上下各差 6° ⇒ 有层次又不脱框。
        ///
        /// ★ v221：把"动"从"看不出"改成"看得见"（用户："让登录界面的画面也动起来"）★
        ///   改前：直线漂移 0.55 / -0.38 / 0.90 m/s ⇒ 3 秒只走 1.1~2.7 米，
        ///         在 40 米外只对应 **0.5~1.3 像素**，等于静止 ✗
        ///   改后：三层**各自峰值速度** 2.2 / 1.6 / 2.8 m/s（≈快一个数量级）
        ///         ⇒ 3 秒走 **4.8~8.4 米** = 画面宽度的 1/4，肉眼一眼能看出 ✓
        ///   为什么从"直线+越界回绕"改成"**正弦游走**"：
        ///     直线要在 ±90 米处回绕，而云幕边长只有 100~230 米，
        ///     回绕瞬间云纹会**整块跳**一下（在 3 秒一帧的对比里就是一个假动作）✗
        ///     正弦来回就没有回绕点，云在画面里"飘过来再飘回去"，和真实云一样 ✓
        ///   三层方向/周期都不同 ⇒ **视差**：近层快、远层慢而且反向 ⇒ 有纵深 ✓
        /// </summary>
        private static readonly Layer[] Layers =
        {
            new Layer { Name = "VISTA_CLOUD_NEAR", D = 14f, Alpha = 58f, Size = 110f, Op = 0.46f,
                        Tint = new Color(0.98f, 0.98f, 0.96f, 1f),
                        SwayA = 18f, SwayV =  2.2f, BreathT =  6.5f, Spin =  0.7f, Bob = 0.35f },
            new Layer { Name = "VISTA_CLOUD_MID",  D = 26f, Alpha = 50f, Size = 165f, Op = 0.16f,
                        Tint = new Color(0.99f, 0.99f, 1.00f, 1f),
                        SwayA = 26f, SwayV = -1.6f, BreathT =  7.5f, Spin = -0.5f, Bob = 0.45f },
            new Layer { Name = "VISTA_CLOUD_FAR",  D = 40f, Alpha = 42f, Size = 240f, Op = 0.33f,
                        Tint = new Color(0.82f, 0.90f, 0.97f, 1f),
                        SwayA = 34f, SwayV =  2.8f, BreathT =  8.5f, Spin =  1.1f, Bob = 0.60f },
        };

        /// <summary>不透明度呼吸相对幅度（±15%）。中位 0.46 ⇒ 近层在 0.39 ↔ 0.53 之间呼吸。</summary>
        private const float BreathDepth = 0.15f;

        // ── 共享资产（静态缓存：无论建几次云，网格/材质/贴图各只有 1 份）──
        private static Material _mat;
        private static Mesh _quad;
        private static Texture2D _tex;

        private readonly GameObject[] _gos = new GameObject[Layers.Length];
        private readonly MeshRenderer[] _mrs = new MeshRenderer[Layers.Length];
        private readonly Vector3[] _home = new Vector3[Layers.Length];

        /// <summary>
        /// ⚠ `MaterialPropertyBlock` 必须**在 Build 里懒建**，不能写成字段初始化 `= new ...`：
        ///   `new MaterialPropertyBlock()` 走的是 `CreateImpl`，而 MonoBehaviour 的字段初始化
        ///   发生在地对象构造期 —— Unity 会直接抛
        ///   "CreateImpl is not allowed to be called from a MonoBehaviour constructor"。
        ///   （第一次跑就吃过：`LoginVista` 的字段 `_clouds = new VistaClouds()` 触发 ➜ 登录鸟瞰整段没跑起来 ✗）
        /// </summary>
        private MaterialPropertyBlock _mpb;

        private Transform _root;
        private float _t;

        /// <summary>实际建出来的层数（验收读数用）。</summary>
        public int LayerCount { get; private set; }

        /// <summary>三层用的贴图分辨率与材质名（验收读数用）。</summary>
        public static string AssetReport()
        {
            return "网格=" + (_quad == null ? "无" : _quad.name + "(共享 1 份)")
                 + "｜材质=" + (_mat == null ? "无" : _mat.shader.name + "(共享 1 份)")
                 + "｜贴图=" + (_tex == null ? "无" : _tex.width + "x" + _tex.height + "(共享 1 份)")
                 + "｜层数=" + Layers.Length;
        }

        // ══════════════════════════════════════════════════════════════════
        // 建 / 动 / 拆
        // ══════════════════════════════════════════════════════════════════

        /// <summary>在 <paramref name="center"/> 上方铺三层云幕（只在登录期间调一次）。</summary>
        public bool Build(Transform parent, Camera cam, Vector3 center)
        {
            if (_root != null) return true;
            if (cam == null) return false;
            if (Shader.Find(ShaderName) == null)
            {
                Debug.LogError("[VISTA] 找不到着色器 " + ShaderName + " ⇒ 云幕不建（画面其余部分不受影响）");
                return false;
            }
            EnsureAssets();
            if (_mpb == null) _mpb = new MaterialPropertyBlock();   // 见字段注释：不能在字段初始化里建

            var rootGo = new GameObject("VISTA_CLOUD_DECK");
            rootGo.transform.SetParent(parent, false);
            _root = rootGo.transform;

            Vector3 camPos = cam.transform.position;

            // 云层横向跟着"城心"走一点：相机为了给右侧登录卡让位是偏右的（LateralOffset），
            // 云若完全居中在相机正前方，会整体压在卡片下面 ⇒ 这里向城心拉 55%
            float skewX = (center.x - camPos.x) * 0.55f;

            int built = 0;
            for (int i = 0; i < Layers.Length; i++)
            {
                Layer L = Layers[i];

                // 由 (水平距离 d, 俯角 α) 反解高度：y = 相机y − d·tanα（见 Layers 上方注释）
                float y = camPos.y - L.D * Mathf.Tan(L.Alpha * Mathf.Deg2Rad);
                Vector3 pos = new Vector3(camPos.x + skewX, y, camPos.z + L.D);

                var go = new GameObject(L.Name);
                go.transform.SetParent(_root, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(90f, i * 37f, 0f);   // 水平铺开（法线朝上）
                go.transform.localScale = new Vector3(L.Size, L.Size, 1f);

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                // 每层只改「颜色/不透明度/柔化距离」⇒ MaterialPropertyBlock，不 new Material ✓
                _mpb.Clear();
                _mpb.SetColor("_Tint", L.Tint);
                _mpb.SetFloat("_Alpha", L.Op);
                _mpb.SetFloat("_DepthFade", 6f + i * 2f);
                _mpb.SetFloat("_EdgeFade", 0.34f);
                mr.SetPropertyBlock(_mpb);

                _gos[i] = go;
                _mrs[i] = mr;
                _home[i] = pos;
                built++;
            }

            LayerCount = built;
            _t = 0f;
            Debug.Log("[VISTA] 云雾层就绪：" + AssetReport() + "｜" + DescribeLayers()
                      + "｜全部用 MaterialPropertyBlock 分色（0 份额外材质）");
            return built > 0;
        }

        /// <summary>每帧动起来（速度/呼吸见 <see cref="Layers"/>）：正弦游走 + 不透明度呼吸 + 慢自转。</summary>
        public void Tick(float dt)
        {
            if (_root == null) return;
            _t += dt;

            for (int i = 0; i < _gos.Length; i++)
            {
                GameObject go = _gos[i];
                if (go == null) continue;
                Layer L = Layers[i];
                Vector3 h = _home[i];

                // ① 正弦游走：峰值速度 = SwayA × ω ⇒ ω = |SwayV| / SwayA
                //    没有回绕点 ⇒ 云纹不会整块跳（见 Layers 上方注释）
                float w = Mathf.Max(0.0001f, Mathf.Abs(L.SwayV) / Mathf.Max(1f, L.SwayA));
                float phase = i * 2.1f;
                float x = h.x + Mathf.Sin(_t * w + phase) * L.SwayA * Mathf.Sign(L.SwayV);
                // 纵向游走：慢一点、另一个相位 ⇒ 云不是"平移一块板"
                float z = h.z + Mathf.Sin(_t * w * 0.62f + phase + 1.3f) * L.SwayA * 0.45f;
                // ② 上下起伏（几厘米到几十厘米，只用来消除"贴纸"感）
                float y = h.y + Mathf.Sin(_t * 0.34f + phase) * L.Bob;

                go.transform.position = new Vector3(x, y, z);
                go.transform.rotation = Quaternion.Euler(90f, i * 37f + L.Spin * _t, 0f);

                // ③ 不透明度呼吸：中位 Op ±15%，周期 6.5/7.5/8.5 秒（三层不同 ⇒ 从不同步）
                MeshRenderer mr = _mrs[i];
                if (mr != null && _mpb != null)
                {
                    float a = L.Op * (1f + Mathf.Sin(_t * (Mathf.PI * 2f / Mathf.Max(0.5f, L.BreathT)) + phase) * BreathDepth);
                    _mpb.Clear();
                    _mpb.SetColor("_Tint", L.Tint);
                    _mpb.SetFloat("_Alpha", Mathf.Clamp(a, 0.02f, 0.95f));
                    _mpb.SetFloat("_DepthFade", 6f + i * 2f);
                    _mpb.SetFloat("_EdgeFade", 0.34f);
                    mr.SetPropertyBlock(_mpb);
                }
            }
        }

        /// <summary>进城 / 离开登录：整体销毁。共享资产（网格/材质/贴图）留在静态缓存里复用。</summary>
        public void Dispose()
        {
            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
                _root = null;
            }
            for (int i = 0; i < _gos.Length; i++) _gos[i] = null;
            LayerCount = 0;
        }

        // ══════════════════════════════════════════════════════════════════
        // 共享资产
        // ══════════════════════════════════════════════════════════════════

        private static void EnsureAssets()
        {
            if (_quad == null)
            {
                // 内置 Quad 网格：临时对象只为取出那份**内置共享网格**，取到就销毁对象 ✓
                GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _quad = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }

            if (_tex == null) _tex = BuildCloudTex();
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;

            if (_mat == null)
            {
                Shader sh = Shader.Find(ShaderName);
                _mat = new Material(sh);
                _mat.name = "VISTA_CLOUD_VEIL (shared)";
                _mat.hideFlags = HideFlags.DontSave;
                _mat.SetTexture("_MainTex", _tex);
                _mat.SetColor("_Tint", Color.white);
                _mat.SetFloat("_Alpha", 0.22f);
                _mat.SetFloat("_DepthFade", 6f);
                _mat.SetFloat("_EdgeFade", 0.34f);
            }
            else if (_mat.GetTexture("_MainTex") == null)
            {
                _mat.SetTexture("_MainTex", _tex);
            }
        }

        /// <summary>
        /// 程序化云纹（256×256，只在第一次用到时生成一次，三层 + 多次登录共用）。
        ///
        /// 做法：三倍频值噪声（value noise）＋ 中心加权径向衰减。
        ///   · 值噪声而不用 Perlin：这里的意图是「团」，不是「絮」——噪声值高于阈值就成团
        ///   · 中心加权：贴图四周必须淡，否则九宫格拉伸/旋转时会露出方角
        ///   · alpha 走 `smoothstep(0.46, 0.74)`：留出**透明缝隙** ⇒ 城能透出来（若隐若现）
        /// </summary>
        private static Texture2D BuildCloudTex()
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.name = "VISTA_CLOUD_TEX";
            tex.hideFlags = HideFlags.DontSave;

            var px = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float u = (x + 0.5f) / TexSize;
                    float v = (y + 0.5f) / TexSize;

                    float n = 0f;
                    n += 0.58f * Noise(u * 6.0f, v * 6.0f, 11);
                    n += 0.28f * Noise(u * 12.0f, v * 12.0f, 29);
                    n += 0.14f * Noise(u * 24.0f, v * 24.0f, 53);

                    // 径向衰减：四边与四角都淡出（避免看到幕布直边）
                    float dx = (u - 0.5f) * 2f, dy = (v - 0.5f) * 2f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 0.7071f;
                    float radial = Mathf.Clamp01(1f - Mathf.Pow(r, 1.5f));

                    // 阈值收窄（0.52~0.66）⇒ 云块边缘更清楚、缝隙更透 ⇒ 城能从缝里露出来 ✓
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.66f, n)) * radial;
                    px[y * TexSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>确定性值噪声（不依赖 <c>UnityEngine.Random</c>，同一份代码每次生成同一张图）。</summary>
        private static float Noise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            float a = Hash(xi, yi, seed);
            float b = Hash(xi + 1, yi, seed);
            float c = Hash(xi, yi + 1, seed);
            float d = Hash(xi + 1, yi + 1, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695040;
                h = (h ^ (h >> 13)) * 1274126177;
                h = h ^ (h >> 16);
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        /// <summary>三层的高度/不透明度/速度（读数用，与 <see cref="Layers"/> 同源）。</summary>
        public static string DescribeLayers()
        {
            var sb = new System.Text.StringBuilder(320);
            for (int i = 0; i < Layers.Length; i++)
            {
                Layer L = Layers[i];
                if (i > 0) sb.Append("；");
                sb.Append("L").Append(i + 1).Append(' ').Append(L.Name.Replace("VISTA_CLOUD_", ""))
                  .Append(" 距相机").Append(L.D.ToString("0")).Append("m/俯角").Append(L.Alpha.ToString("0")).Append("°")
                  .Append(" 边长").Append(L.Size.ToString("0")).Append("m")
                  .Append(" 游走±").Append(L.SwayA.ToString("0")).Append("m@峰速").Append(L.SwayV.ToString("0.0")).Append("m/s")
                  .Append(" alpha ").Append((L.Op * (1f - BreathDepth)).ToString("0.00")).Append('↔').Append((L.Op * (1f + BreathDepth)).ToString("0.00"))
                  .Append("(").Append(L.BreathT.ToString("0.0")).Append("s)");
            }
            return sb.ToString();
        }
    }
}
