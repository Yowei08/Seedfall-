using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v232：传送门 = **黑色黑洞**（用户明说"黑色的"）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三件东西（尺寸都按门洞 4.95 × 6.90 卡）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① **黑盘**：直径 **3.60 m**（门洞半宽的一半左右，不像装饰满门）、纯黑 `(0.02,0.02,0.03)`
    ///   ② **边缘光**：一圈淡蓝白**环**（`TownKit.Ring(1.72, 1.84)` ⇒ 宽 **0.12 m**）
    ///      黑盘靠它"看得出是个洞"
    ///   ③ **内旋**：3 个**半透明环**（半径 1.50 / 1.00 / 0.50，alpha 0.75 / 0.55 / 0.35）
    ///      ★ 关键：环**做成椭圆**（`localScale.z = 0.82`）⇒ 绕法线转**看得见** ★
    ///      （上一版是实心圆盘，绕法线转是**不可见的**——旋转对称，这就是"转了没效果"的原因）
    ///   ④ **6 颗白色星点**从外向内螺旋（半径 1.95 → 0.25 循环）⇒ 被"吸进去"的感觉
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 朝向（上一版栽过的坑，这里全部锁死）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 根挂在 `TOWN` 下（**不是 FX 下**）⇒ 不继承父级旋转/缩放
    ///   · 每个件的基准朝向都是 `Quaternion.Euler(90, 0, 0)`（**竖直、法线朝街**），
    ///     每帧一条**断言**把它掰回去；"转"只体现为 `Euler(90, 0, spin)` 里的 **spin（绕法线）**
    ///   · 位置用**世界坐标**：圆心 = (门的 x, **y = 3.45**, 门的 z)
    ///
    /// 另外：**守卫保留** —— `FX_BossPortal` 下凡不是 `PORTALSWIRL_` 的子节点一律停用（压住美术那路的写实黑洞）
    /// </summary>
    public static class TownPortalFx
    {
        /// <summary>
        /// ★ v252：**圆心高度 3.45 → 3.85** ★
        /// 3.45 是"人眼高度"那一版的口径；而门洞（`PORTAL_Field`）的真实中心在 **y = 3.85**
        /// （门体 `TownBuildings.BuildPortal`：尺寸 4.95 × 6.90、中心 y 3.85）。
        /// 差 0.40 m 在实机里就是"黑洞压在下半扇门上" ⇒ 用户要的"特效与门结合"从几何上就没对上。
        /// 现在圆心 = 门洞中心 ⇒ 黑洞、光环、星点全部居中在门里 ✓
        /// </summary>
        public const float CenterY = 3.85f;
        /// <summary>黑盘直径（米）。★ v252：3.60 → **4.20** —— 门洞净宽 4.95，盘留 0.37 m 边框才读成"嵌进门里"。</summary>
        public const float DiscDiameter = 5.40f;   // ★ v268：4.60 → 4.80（门洞 4.86，基本贴满）
        /// <summary>
        /// ★ v252：**门洞填充**（竖直方向的软光柱）。门洞是 4.95×6.90 的矩形，
        /// 一个圆盘永远填不满它 ⇒ 补一层"扁椭圆"暗光把四角盖住，黑色区域才和门框严丝合缝。
        /// 半轴 = (2.42, 3.22)（比门洞各小 0.05 m，避免穿出门框）。
        /// </summary>
        public const float FillHalfW = 2.30f, FillHalfH = 3.10f;
        /// <summary>门框光缝的内/外半径（贴着门洞边缘的一圈亮线，静止不转 ⇒ 读成"门的一部分"）。</summary>
        public const float SeamInner = 2.46f, SeamOuter = 2.56f;
        /// <summary>
        /// 边缘光环的内/外半径 ⇒ 环宽 0.12 m。
        /// ★ v252：跟着黑盘直径一起改（3.60 → 4.20）—— 光环必须**贴在盘的边缘**上，
        ///   否则放大盘之后光圈会缩到洞里，读起来像"另一个环"而不是"洞的边" ✓
        /// </summary>
        public const float EdgeInner = 1.84f, EdgeOuter = 1.95f;
        /// <summary>三个内旋环的外半径 / 环宽 / 转速(°/s)。</summary>
        public static readonly float[] RingOuter = { 1.50f, 1.00f, 0.50f };
        public static readonly float[] RingWidth = { 0.10f, 0.08f, 0.06f };
        public static readonly float[] RingSpin = { 62f, -84f, 108f };      // 逐层反向、逐层更快
        public static readonly float[] RingAlpha = { 0.75f, 0.55f, 0.35f };
        // ══════════════════════════════════════════════════════════════════
        // ★ v259：**旋臂**（黑洞重做的核心）★
        //   为什么不用 Ring 而是用小段方块拼：
        //     同心环是**旋转对称**的 ⇒ 转起来画面完全不变，读不出"在转" ✗
        //     螺旋臂不对称 ⇒ 转速直接可见，这才是漩涡 ✓
        //   每条臂 12 段，沿对数螺旋 `r(θ) = r0 · exp(k·θ)` 排布（k 越大越"卷"）；
        //   段长与亮度都由 θ 决定（越往内越短、越暗）⇒ 自然收进中心 ✓
        // ══════════════════════════════════════════════════════════════════
        public const int ArmCount = 3;
        public const int ArmSegments = 12;
        /// <summary>每条臂的起始半径（米）。</summary>
        public const float ArmR0 = 0.34f;
        /// <summary>对数螺旋系数：r = r0 · exp(kθ)，k = 0.62 ⇒ 转一圈放大 ~49 倍，视觉上两圈多一点。</summary>
        public const float ArmK = 0.62f;
        /// <summary>相邻两条臂的相位差（度）。</summary>
        public const float ArmPhaseDeg = 120f;
        /// <summary>三条臂的转速（°/s）——**互不相同**，否则读成"一个整体在转" ✗</summary>
        public static readonly float[] ArmSpin = { 46f, -58f, 71f };

        /// <summary>星点。</summary>
        public const int StarCount = 6;
        public const float StarSpinDegPerSec = 120f;

        private static Transform _fx;                  // FX_BossPortal（只用于守卫 + 取世界 x/z）
        private static Transform _root;                // 旋涡根（挂 TOWN 下，identity）
        private static Transform _disc;                 // ★ v263：黑洞本体（贴图盘）
        /// <summary>★ v263：外层柔光晕（同一张图、更大更淡、反向转）⇒ 读出"在转"。</summary>
        private static Material _matDisc;
        /// <summary>v269: 漩涡层（叠在不透明黑盘前 2 cm）</summary>
        private static Transform _swirl;
        /// <summary>v264c: opaque black backing behind the swirl disc</summary>
        private static Transform _backing;
        private static Transform _edge;                 // （v263 起不再使用，留字段让旧探针能编译）
        /// <summary>★ v252：门洞填充（扁椭圆）与门框光缝 —— 让黑洞和门"长在一起"。
        /// v273 备注：这两个字段**从未被赋值**（CS0649），但 `Tick` 里仍带 null 守卫地读它们，
        /// 属于"预留钩子"而非死代码；删掉会编译不过（已试过）。要彻底清就删 Tick 里那段。</summary>
        private static Transform _fill, _seam;
        private static readonly List<Transform> InnerRings = new List<Transform>(3);
        /// <summary>★ v259：三条旋臂各自的父节点（整条臂绕法线转）。</summary>
        private static readonly List<Transform> Arms = new List<Transform>(ArmCount);
        /// <summary>v259: arm material</summary>
        private static Material _matArm;
        private static readonly List<Transform> Stars = new List<Transform>(StarCount);
        private static Material _matBlack, _matEdge, _matRing0, _matRing1, _matRing2, _matStar;
        private static float _t;
        private static bool _built;

        public static int KilledLast { get; private set; }

        /// <summary>
        /// ★★ v251：**累计**关掉过多少个美术那路的节点（`KilledLast` 每帧清零，看不出反复）★★
        /// 判据：站立 N 秒后这个数**不再增长** ⇒ 没有拉锯；若持续增长 ⇒ 有人每帧/每轮把它打开 ✗
        /// </summary>
        public static int KilledTotal { get; private set; }

        /// <summary>
        /// ★★ v251：本旋涡是否已经接管了传送门 ★★
        /// `TownArtDresser.SpawnPortalVfx` 用它判断"该不该再建美术那路的黑洞" ——
        /// 之前两边各建一套、代码这边每帧关美术那套，而美术侧的周期扫描又会重建
        /// ⇒ 用户看到"传送门在旧版和新版之间切换"。
        /// </summary>
        public static bool SwirlOwnsPortal => _built && _root != null;

        public static int LiveParticles { get; private set; }
        private static int _parts;
        public static int SwirlParts => _parts;
        public static bool Ready => _built;

        public static void Tick(Transform townRoot, float dt)
        {
            if (dt <= 0f) return;
            // ★ v269：**自愈** —— 挂点/根被销毁时（换存档、重进 Play、主城重建）
            //   `_built` 是 static，会一直是 true，于是"再也不重建" ⇒ 黑洞整个消失 ✗
            //   判据：`_built` 为真但 `_root == null`（已被销毁）⇒ 当作没建过 ✓
            if (_built && _root == null) _built = false;
            if (!_built)
            {
                if (townRoot == null) return;
                Build(townRoot);
                if (!_built) return;
            }
            _t += dt;
            Guard();
            Animate(dt);
        }

        private static void Build(Transform townRoot)
        {
            _fx = FindDeep(townRoot, TownLayout.BossPortalFxName);
            // ★ v269：找不到挂点**不能**把 `_built` 置真（置真 = 永远不再尝试）
            if (_fx == null) { return; }

            _matBlack = Flat(new Color(0.02f, 0.02f, 0.03f), 0.05f, "PortalBlack");            _matEdge = Flat(new Color(0.82f, 0.95f, 1.00f), 3.30f, "PortalEdgeGlow");   // ★ v259：2.20 → 3.30（与门口散射白光同族）
            _matRing0 = Translucent(Color.white, RingAlpha[0], "PortalRingA");
            _matRing1 = Translucent(Color.white, RingAlpha[1], "PortalRingB");
            _matRing2 = Translucent(Color.white, RingAlpha[2], "PortalRingC");
            _matStar = Flat(new Color(1.00f, 1.00f, 1.00f), 2.40f, "PortalStar");

            var rootGo = new GameObject("PORTALSWIRL_Root");
            rootGo.transform.SetParent(townRoot, false);
            rootGo.transform.rotation = Quaternion.identity;
            rootGo.transform.localScale = Vector3.one;
            _root = rootGo.transform;

            Vector3 center = new Vector3(_fx.position.x, CenterY, _fx.position.z);
            _root.position = center;
            _parts = 0;

            // ★ v264b：这里原来是三件（旧黑盘 / 门洞填充椭圆 / 门框光缝环），
            //   它们与新建的 `Seedfall/BlackHole` 盘**叠在一起** —— `PortalInfo` 实测里能看到
            //   两个 `PORTALSWIRL_Disc`（一个 CloudFlat、一个 BlackHole）⇒ 画面被旧灰盘糊住 ✗
            //   用户要的是「只保留黑色黑洞」⇒ 三件全部删除，门中心只剩下面那张 BlackHole 盘 ✓

            // ══════════════════════════════════════════════════════════════
            // ★★ v263：**门中心只留黑洞**（用户原话：「只保留黑色黑洞，为黑洞加入纹理和
            //   颜色渐变效果，以及动态；删除传送门中心的圆环和各种漂浮物，以及涡轮效果」）★★
            //
            //  删掉的：3 条内旋环 / 门框光缝环 / 边缘光环 / 6 颗星点 / 3 条对数螺旋臂
            //          （还有 `TownBuildings` 里那 3 条螺旋光丝管）
            //  留下的：**一层带纹理的盘 + 一层更淡的外晕**，两层反向旋转 + 呼吸 ⇒ "活的黑洞"
            //
            //  贴图是**运行时程序化生成**的（`MakeNebula`）：
            //    径向渐变 中心 #000000 → 深蓝 #060B18 → 边缘冷青 #16283F
            //    + 三圈频率不同的旋涡条纹（sin(kθ + w·r)）
            //    + 一点哈希噪声（避免"太干净"的塑料感）
            //  ⇒ 有纹理、有渐变（用户第 1、2 条要求），旋转/呼吸提供动态（第 3 条）✓
            // ══════════════════════════════════════════════════════════════
            // ★★ v264：换成工程里**已有的** `Seedfall/BlackHole` shader ★★
            //   上一版是 "CloudFlat + 运行时贴图"：贴图不随 UV 旋转 ⇒ 只是一张图在原地转，
            //   没有"旋涡"可言（用户："加入纹理效果和漩涡效果"）✗
            //   这个 shader 的旋涡是**在片元里按极坐标算的**：
            //     `_SwirlSpeed` 角速度、`_InwardSpeed` 向心流速、`_Arms` 旋臂数、
            //     `_Pulse` 呼吸、`_RingRadius` 吸积盘半径、`_Vignette` 外圈暗角、`_Core` 暗核开关
            //   ⇒ 纹理与漩涡都是真家伙，且**不占一张贴图**（省内存）✓
            //   ⚠️ 颜色刻意压得很暗：用户要的是"黑色黑洞"，不是紫色烟花 ✓
            // ③a **不透明近黑圆盘**（最可靠的一层：shader 出任何问题它都在）
            // v270: **改用 Unlit/Color**（不参与光照、不透明）——
            //   CloudFlat / BlackHole 两层在白天都会被冲淡成一层雾，这就是完全没有变化的原因
            Material opaqueBlack = UnlitBlack("PortalBlackOpaque");
            _disc = Node("PORTALSWIRL_Disc", TownKit.Disc(1f, 48), opaqueBlack);
            _disc.localScale = Vector3.one * (DiscDiameter * 0.5f);
            _parts++;

            // ③b 前面 2 cm 的**漩涡层**（`Seedfall/BlackHole`：有它就有纹理/漩涡，没有也不影响 ③a）
            _matDisc = BlackHoleMat();
            _swirl = Node("PORTALSWIRL_Swirl", TownKit.Disc(1f, 48), _matDisc);
            _swirl.localPosition = new Vector3(0f, 0f, 0.02f);
            _swirl.localScale = Vector3.one * (DiscDiameter * 0.5f * 0.98f);
            _parts++;
            // ★ v264c：**黑色底衬** —— Seedfall/BlackHole 是加色/半透明的，
            //   单靠它压在白天明亮的广场上会发白，读不出洞 ✗
            //   ⇒ 在它后面 3 cm 处垫一张**纯黑不透明盘**：中心读作没有光，漩涡叠在它上面 ✓
            Material back = Flat(new Color(0.008f, 0.008f, 0.012f), 0.02f, "PortalBlackBacking");
            _backing = Node("PORTALSWIRL_Backing", TownKit.Disc(1f, 48), back);
            // ★ v268：**底衬从 3 cm 退到 40 cm** —— 两个几乎共面的平面在描边通道眼里是
            //   一圈圈深度断层 ⇒ 那正是用户看到的"黑弧线"（越往外越稀）✗
            //   退开之后深度差足够大，不再产生环状描边；直径也与主盘一致（不再多出一圈边）✓
            _backing.localPosition = new Vector3(0f, 0f, -0.06f);   // v268b: 退到盘**后** 6 cm（原来是 +0.40 = 跑到盘前面去了）
            _backing.localScale = Vector3.one * (DiscDiameter * 0.5f);
            _parts++;

            _built = true;
            Log.Info($"[Town] 黑洞传送门：黑盘直径 **{DiscDiameter:0.00} m** ｜边缘光环宽 " +
                     $"{(EdgeOuter - EdgeInner):0.00} m｜内旋环 3 个（半径 {RingOuter[0]:0.00}/{RingOuter[1]:0.00}/{RingOuter[2]:0.00}，" +
                     $"alpha {RingAlpha[0]:0.00}/{RingAlpha[1]:0.00}/{RingAlpha[2]:0.00}，**椭圆 z×0.82** ⇒ 绕法线转可见）｜" +
                     $"星点 {StarCount} 颗（1.95→0.25 螺旋）｜圆心（世界）({center.x:0.00}, {center.y:0.00}, {center.z:0.00})｜" +
                     $"全部基准朝向 Euler(90,0,0) → 竖直、法线朝街 ✓｜件数 {_parts}");
        }

        /// <summary>在旋涡根下建一个件：**基准朝向 = Euler(90,0,0)**（竖直、法线朝街）。</summary>
        private static Transform Node(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localPosition = Vector3.zero;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        /// <summary>
        /// ★ v263：**程序化黑洞贴图**（不落盘、运行时生成一次、之后只读）。
        ///   r = 0（中心）→ 1（盘边）：
        ///     亮度 0% → 14% → 26%（中心纯黑，越到边上越亮一点点 ⇒ 有"洞口"的纵深）
        ///     色相 纯黑 → 深蓝 → 冷青（与 `TownColors.PortalGlow` 同一族，但不抢眼）
        ///   再叠 `sin(kθ + w·r)` 三条旋涡条纹（k = 3/5/8，w 不同）与一点哈希噪声 ⇒ 有纹理 ✓
        /// ⚠️ 用 `Mathf.Repeat` 保证每个像素结果确定（同一张图每次跑都一样，便于截图比对）✓
        /// </summary>
        private static Texture2D MakeNebula(int size, float radiusMeters)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PortalNebula",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    if (r > 1f) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }   // 圆外透明
                    float th = Mathf.Atan2(v, u);
                    // ① 径向渐变：中心纯黑 → 深蓝 → 冷青
                    float t = Mathf.Clamp01(r);
                    float lum = Mathf.Lerp(0.005f, 0.26f, t * t);
                    Color baseC = Color.Lerp(new Color(0f, 0f, 0.01f), new Color(0.09f, 0.16f, 0.25f), t);
                    // ② 三条旋涡条纹（频率不同、相位随半径推进 = 螺旋感）
                    float swirl = Mathf.Sin(th * 3f + r * 9.0f) * 0.35f
                                + Mathf.Sin(th * 5f - r * 13.5f) * 0.22f
                                + Mathf.Sin(th * 8f + r * 21.0f) * 0.12f;
                    swirl *= Mathf.SmoothStep(0f, 0.35f, r) * (1f - Mathf.SmoothStep(0.85f, 1f, r));
                    // ③ 哈希噪声（细颗粒，避免块面太干净）
                    uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    float noise = ((h >> 8) & 0xFF) / 255f * 0.05f - 0.025f;
                    float a = Mathf.Clamp01(lum + swirl * 0.10f + noise);
                    // ④ 边缘再压一点（读作"洞口边缘的暗环"，但不像圆环）
                    float edgeFade = 1f - Mathf.SmoothStep(0.94f, 1f, r);
                    Color c = baseC * Mathf.Clamp01(a * 2.2f + 0.06f);
                    px[y * size + x] = new Color32(
                        (byte)(Mathf.Clamp01(c.r) * 255f * edgeFade),
                        (byte)(Mathf.Clamp01(c.g) * 255f * edgeFade),
                        (byte)(Mathf.Clamp01(c.b) * 255f * edgeFade),
                        (byte)(255f * edgeFade));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// ★ v264：**黑洞材质**（用工程自带的 `Seedfall/BlackHole`）。
        /// 参数取值都偏低：这是"黑洞"，不是烟花 ——
        ///   核心紫 (0.10,0.06,0.18) / 亮部 (0.30,0.22,0.45) / 边缘青 (0.16,0.30,0.42)
        ///   强度 0.55、呼吸 0.18、吸积盘 0.70、暗角 0.60、暗核 0.55
        /// ⇒ 中心是黑的，外面一层**能看出在转的**薄雾 ✓
        /// 找不到 shader 时退回 `Flat`（纯黑盘）—— 那样至少不会花屏 ✓
        /// </summary>
        private static Material BlackHoleMat()
        {
            Shader sh = Shader.Find("Seedfall/BlackHole");
            if (sh == null) return Flat(new Color(0.01f, 0.01f, 0.02f), 0.05f, "PortalBlackOnly");
            var m = new Material(sh) { name = "PortalBlackHole" };
            void C(string n, Color c) { if (m.HasProperty(n)) m.SetColor(n, c); }
            void F(string n, float v) { if (m.HasProperty(n)) m.SetFloat(n, v); }
            C("_CoreColor", new Color(0.020f, 0.020f, 0.030f, 1f));   // ★ v265：定住的纯黑洞，不是紫旋涡
            C("_HotColor", new Color(0.16f, 0.18f, 0.26f, 1f));
            C("_EdgeColor", new Color(0.09f, 0.13f, 0.20f, 1f));
            C("_CoreColor2", new Color(0.008f, 0.008f, 0.016f, 1f));
            F("_SwirlSpeed", 0f);      // ★ v265：定住（用户："不要转动，定住"）
            F("_InwardSpeed", 0f);    // 向心流也停：定住 = 完全静止
            F("_Arms", 3f);
            F("_Intensity", 2.20f);
            F("_Pulse", 0f);            // 呼吸也关掉
            F("_RingRadius", 0.70f);
            F("_Vignette", 0.60f);
            F("_CoreRadius", 0.55f);
            F("_Layer", 0f);
            F("_Core", 1f);            // 最内层开暗核 ⇒ 中心是黑的
            return m;
        }

        /// <summary>v270：**不透明的纯黑材质**（`Unlit/Color`，不参与光照）。黑洞必须真的黑。</summary>
        private static Material UnlitBlack(string name)
        {
            Shader sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Seedfall/ToonSimple");
            if (sh == null) return Flat(Color.black, 0f, name);
            var m = new Material(sh) { name = name };
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.008f, 0.008f, 0.012f, 1f));
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.renderQueue = 2000;
            return m;
        }

        private static Material Flat(Color c, float ambient, string name)
        {
            Shader sh = Shader.Find("Seedfall/CloudFlat");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) return ToonLook.Get(c, 0f, true);
            var m = new Material(sh) { name = name };
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Ambient")) m.SetFloat("_Ambient", ambient);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            // ★ v252：**关掉背面剔除**（`_Cull = 0`，URP 的约定）★
            //   起因：实机截图里黑洞/内旋环/星点**一个都看不见**，而所有零件都是**单面片**
            //   （`TownKit.Disc` 的法线朝 −Y，经 `Euler(90,0,0)` 后朝 −Z）。
            //   单面片只要朝向与相机差 180°，就会**整片消失**——而"整片消失"在读数上
            //   与"没建出来"完全一样（`PortalInfo` 里件数 13 是对的）✗
            //   关剔除之后：从街上看、从门里看、从侧面看都在 ⇒ 这个类不再依赖"哪边是正面" ✓
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            return m;
        }

        private static Material Translucent(Color c, float alpha, string name)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return ToonLook.Get(c, 0f, true);
            var m = new Material(sh) { name = name };
            var col = new Color(c.r, c.g, c.b, alpha);
            if (m.HasProperty("_Color")) m.SetColor("_Color", col);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            // ★ v252：同样关掉背面剔除（理由见 `Flat()` 里那段）★
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.renderQueue = 3000;
            return m;
        }

        /// <summary>
        /// ★★ v251：守卫改成 **Destroy 而不是 SetActive(false)** ★★
        ///
        /// 原来的问题（用户报"传送门会在旧版和新版间切换"）：
        ///   这里是**每帧**把美术那路的 `FX_BlackHole` 停用。停用后如果美术侧的周期扫描
        ///   （`TownArtDresser` 的 90 帧 sweep）把它重新 `SetActive(true)`，就会出现
        ///   **关→开→关→开** ⇒ 视觉上旧版/新版交替闪。
        ///   `KilledLast` 每帧清零 ⇒ 这个拉锯在读数上完全看不出来 ✗（一个永远"正常"的判据）。
        ///
        /// 现在：
        ///   ① **销毁**（`Object.Destroy`）而不是停用 ⇒ 扫描再打开也没东西可开 ✓
        ///   ② `KilledTotal` **累计** ⇒ 判据能红：静置几秒后它不该再涨 ✓
        ///   ③ 配合 `SwirlOwnsPortal`：`TownArtDresser.SpawnPortalVfx` 在看到旋涡已接管后
        ///      不再重建美术那套 ⇒ 从源头断掉重复 ✓
        /// </summary>
        private static void Guard()
        {
            KilledLast = 0;
            if (_fx == null) { LiveParticles = 0; return; }
            for (int i = _fx.childCount - 1; i >= 0; i--)
            {
                Transform c = _fx.GetChild(i);
                if (c == null) continue;
                if (c.name.StartsWith("PORTALSWIRL_", System.StringComparison.Ordinal)) continue;

                // 美术那路（`FX_BlackHole` 等）⇒ 永久移除，不留"可以被再次打开"的东西
                if (c.gameObject.activeSelf) KilledLast++;
                KilledTotal++;
                Object.Destroy(c.gameObject);
            }
            var all = _fx.GetComponentsInChildren<ParticleSystem>(true);
            int live = 0;
            for (int k = 0; k < all.Length; k++)
                if (all[k] != null && all[k].gameObject.activeInHierarchy) live++;
            LiveParticles = live;
        }

        /// <summary>动画：三个内旋环绕**法线**转（椭圆 ⇒ 可见）、星点螺旋吸入、边缘光脉动。</summary>
        private static void Animate(float dt)
        {
            if (_root == null) return;
            for (int i = 0; i < InnerRings.Count; i++)
            {
                Transform r = InnerRings[i];
                if (r == null) continue;
                float spin = Mathf.Repeat(_t * RingSpin[i], 360f);
                // ★ 基准 90° 不变，spin 是"绕法线"的那一层 ⇒ 平面始终竖直、朝向一致 ★
                r.localRotation = Quaternion.Euler(90f, 0f, spin);
                if (Quaternion.Angle(r.localRotation, Quaternion.Euler(90f, 0f, spin)) > 0.5f)
                    r.localRotation = Quaternion.Euler(90f, 0f, spin);
            }
            // 星点：半径 1.95 → 0.25 收进去，到 0.25 后从 1.95 重来（循环，固定相位）
            for (int i = 0; i < Stars.Count; i++)
            {
                Transform s = Stars[i];
                if (s == null) continue;
                float phase = Mathf.Repeat(_t * 0.85f + i * (1f / StarCount), 1f);   // ★ v259：0.55 → 0.85（吸得更快）
                float radius = Mathf.Lerp(2.05f, 0.12f, phase);                        // ★ 外沿贴边进、消失在更里
                float ang = (i * (360f / StarCount) + _t * StarSpinDegPerSec) * Mathf.Deg2Rad;
                s.localPosition = new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, -0.02f);
                float fade = 1f - phase;                                          // 越靠里越淡
                s.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.11f, fade);
                s.gameObject.SetActive(fade > 0.08f);
            }
            // ★★ v263：动态 = 两层盘**反向**旋转（速度不同）+ 呼吸 ★★
            //   为什么是反向：同向转的话两层贴图叠在一起，视觉上像"一个盘"，看不出动 ✗
            //   反向转时，两层旋涡条纹相对滑移 ⇒ 不用位移贴图也能读出流动感 ✓
            // ★ v265：**不再旋转、不再呼吸**（用户："中间的特效不要转动，定住"）
            //   盘面朝向与缩放都在 Build 时定死 ⇒ 这里什么都不做 ✓
            // ★ v252：门框光缝极轻地呼吸（±1.2%），门洞填充极轻地涨缩（±0.8%）——
            //   幅度刻意做小：它们读起来应当是"门在呼吸"，而不是"另一套独立特效在动" ✓
            if (_seam != null)
            {
                float p = 1f + Mathf.Sin(_t * 1.6f) * 0.012f;
                _seam.localScale = new Vector3(p, 1f, p);
            }
            if (_fill != null)
            {
                float b = 1f + Mathf.Sin(_t * 0.9f) * 0.008f;
                _fill.localScale = new Vector3(FillHalfW * b, 1f, FillHalfH * b);
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>读数：黑盘直径 / 环数 / 转速 / 朝向 / 守卫（激活的 ParticleSystem 应为 0）。</summary>
        public static string Probe()
        {
            var sb = new System.Text.StringBuilder(900);
            sb.Append($"黑洞件数 {_parts}｜黑盘直径 {DiscDiameter:0.00} m｜边缘光环 {EdgeInner:0.00}~{EdgeOuter:0.00} m｜" +
                      $"内旋环 3 个（{(InnerRings.Count > 0 ? InnerRings[0].localRotation.eulerAngles.ToString() : "-")}）｜" +
                      $"转速 {RingSpin[0]:0}/{RingSpin[1]:0}/{RingSpin[2]:0} °/s｜**激活的 ParticleSystem = {LiveParticles}（判据 0）**" +
                      $"｜本帧停掉外来 = {KilledLast}");
            // ★ v252：**每个零件到底画没画**（诊断"件数对、画面空"这种症状的唯一手段）★
            //   `isVisible` 是"这一帧被任一相机看到"；`cull` 是材质的背面剔除档（0 = 两面都画）。
            //   两者一起看，就能分辨"没在视锥里 / 被别的东西挡住 / 单面片朝反了" ✓
            if (_root != null)
            {
                for (int i = 0; i < _root.childCount; i++)
                {
                    Transform t = _root.GetChild(i);
                    if (t == null) continue;
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr == null) continue;
                    Material mm = mr.sharedMaterial;
                    float cull = mm != null && mm.HasProperty("_Cull") ? mm.GetFloat("_Cull") : -1f;
                    sb.Append($"\n  {t.name}：visible={mr.isVisible}｜shader={(mm != null && mm.shader != null ? mm.shader.name : "null")}" +
                              $"｜queue={(mm != null ? mm.renderQueue : -1)}｜_Cull={cull:0.0}｜lossy={t.lossyScale}");
                }
            }
            for (int i = 0; i < InnerRings.Count; i++)
            {
                Transform r = InnerRings[i];
                if (r == null) continue;
                sb.Append($"\n  内环{i}：localRotation={r.localRotation.eulerAngles}（基准 90 不变）· localScale={r.localScale}（z=0.82 椭圆）" +
                          $" · lossyScale={r.lossyScale}");
            }
            if (_disc != null)
                sb.Append($"\n  黑盘：position={_disc.position} · lossyScale={_disc.lossyScale} ⇒ 世界直径 ≈ {_disc.lossyScale.x * 2f:0.00} m");
            string s = sb.ToString();
            Log.Info("[Town] PORTALFX " + s);
            return s;
        }
    }
}
