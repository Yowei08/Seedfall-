using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 模型 → 卡通描边材质的转换口。
    ///
    /// 为什么需要它：FBX 导入时 Unity 会给模型挂上它自己生成的 URP/Lit 材质，
    /// 那样渲染出来是"塑料感"而不是参考美术的漫画感。这里做两件事：
    ///   ① 按原材质的底色生成 Seedfall/Toon 材质（颜色沿用 Blender 侧写的调色板）
    ///   ② 描边粗细按模型尺寸自动换算——固定毫米数在 0.7m 的小恶魔上糊成一坨、在 1.55m 的大恶魔上又看不见
    /// 转换是幂等的，可以重复调用。
    /// </summary>
    public static class ToonLook
    {
        public const string ShaderName = "Seedfall/Toon";
        /// <summary>装饰专用的轻量着色器（只有本体 Pass）。</summary>
        public const string SimpleShaderName = "Seedfall/ToonSimple";

        /// <summary>
        /// ★ v210：材质级的霓虹**屏幕最小宽度（像素）** —— 给
        /// **没有逐实例指定像素宽度的对象**用的默认值（**程序化虫怪 `BUG_*` 走的就是这一份**）。
        ///
        /// ── 为什么是 4.0（v206 是 1.4、v208 是 2.0，都不够）──
        /// v210 探针实测（玩法机位、1080p）：
        ///   相机到敌人 **20.84 米**｜每像素 ≈ **2.32 厘米**
        ///   `_NeonWidth`(MPB) = **1.44 厘米** ⇒ 只有 0.62 像素
        ///   像素兜底 2.0 ⇒ 霓虹环 **2.00 屏幕像素**
        /// 而敌人在那一档有 **74 像素**高 ⇒ 2 像素的环只占 2.7%，且加色混合下还被草色冲淡
        /// ⇒ 用户连着三轮说"没有"是**读数上说得通的**，不是他看错了 ✓
        ///
        /// 4.0 像素 ⇒ 环宽约 **5.5% 身高**，配 1.25 像素的黑墨线 = 一圈能数出来的红边 ✓
        ///
        /// ★ 为什么必须改这里而不是改 `ProceduralBugs` ★
        /// `ProceduralBugs.PushBlock` 只写 `_TintMul` / `_NeonColor` / `_NeonWidth`，
        /// **不写 `_NeonPixels`** ⇒ 虫怪的像素下限**只认材质这一份** ✓
        /// （读数佐证：探针从 MPB 读到 0、回落到材质值 2.0）
        ///
        /// ★ 作物**不走这个值** ★ 它们在 `ModelView.SetNeon` 里被逐实例设成 `WorldView.NeonPxPlant`
        /// （作物比敌人细：4 厘米的壳会把 0.4 米的植株吞掉 ⇒ 就是用户说的"植物变蓝"）。
        /// </summary>
        public const float NeonMinPixels = 4.0f;

        /// <summary>
        /// ★ v210：霓虹的**淡出距离（米）**，与黑墨线的 `_OutlineDepthFade` 分开 ★
        ///
        /// ── 实测把这本账重新算了一遍（v206 那一版的 80 也是不够的）──
        ///   v206 的理由是对的（不能借黑墨线的 26 米），但 80 米在玩法机位下仍要砍掉两成：
        ///   相机到敌人 20.84 米 ⇒ `fade = 1 − 20.84/80 = **0.740**`
        ///   ⇒ 霓虹的绿蓝分量只剩 0.74 倍，红反而被背景吃掉 —— 这正是加色混合最糟的区间 ✓
        ///
        /// ── 为什么取 240 而不是"刚好够"的 42 ★
        /// 用户上一轮的原话是"别靠刚好够"。240 ≈ **机位距离的 11.5 倍**：
        /// 20.84 米处 fade = **0.913**；40 米处 0.833；60 米处 0.75
        /// ⇒ 以后改机位、改 fov、改视野都不需要再动这个数 ✓
        /// （`SeedfallToon.shader` 里 `_NeonFade` 的 Range 上限同步放宽到 400，
        ///   否则"改取值范围"这件事会在下一次被 Range 卡住 —— 旧上限就是 200）
        /// </summary>
        public const float NeonFadeMeters = 240f;

        /// <summary>
        /// ★ v210：黑墨线的**淡出距离（米）** ★
        ///
        /// 旧值 26 米是"远景角色糊成一团黑"那条理由写下的 —— 但那条担心的前提是
        /// **墨线宽度按米数固定**（远处一条线在屏幕上永远是 1~2 像素，几百个敌人糊成黑面）。
        /// 现在墨线宽度改成了**按像素兜底**（`_OutlinePixels`，且只有 1.25 像素），
        /// 这条担心就从"宽度问题"变成了"深度裁剪问题"：
        /// 26 米在玩法机位（20.84 米）下只剩 19.9% 的可见度，稍远一点就直接被 clip ✓
        /// ⇒ 抬到 200（≈ 机位距离的 9.6 倍），20.84 米处 fade = 0.896 ✓
        /// </summary>
        public const float OutlineFadeMeters = 200f;

        /// <summary>
        /// ★ v214：黑墨线的**屏幕最小宽度（像素）** —— 1.25 → **2.2** ★
        ///
        /// ── 旧值 1.25 为什么不够 ──
        /// 1.25 像素是一条"在纸上刚好连得起来"的线，但它是**下限**，
        /// 而 `_OutlineWidth`（米数那条腿）在绝大多数物体上比它还小：
        /// 玩家 0.65 米高 ⇒ `Clamp(0.65×0.016, 0.004, 0.05)` = **1.04 厘米**；
        /// 玩法机位下 1 像素 = 2.32 厘米 ⇒ 那条腿只有 0.45 像素。
        /// ⇒ 实际画出来的永远是下限 1.25 像素，而且因为抗锯齿，
        ///   一条 1.25 像素的黑线在草地/土色底上几乎读不出来 ✓
        ///
        /// ── 2.2 是"能数出来"和"不糊成一团"的交点 ──
        /// 用户这一轮的原话是"加入**黑色描边**"，判据是"看得见"。
        /// 74 像素高的敌人身上 2.2 像素 ≈ **3% 身高**，是漫画墨线的常规比例；
        /// 再粗（3+）会让小体型的虫怪只剩轮廓、丢掉内部层次。
        ///
        /// ★ 这个值现在**真的生效** ★ —— 旧版它只被写进材质（`_OutlinePixels`），
        /// 而 `SeedfallToon.shader` 根本没声明这个属性 ⇒ 一直是死设置（v214 补齐）。
        /// </summary>
        public const float OutlineMinPixels = 2.2f;

        /// <summary>
        /// ★ v206：材质池里现在有几份卡通材质（**只读**，给探针/自检判"没有每实例 new Material"）★
        /// 实例增长而本数不变 ⇒ 说明染色/霓虹走的是 MPB 与共享材质，而不是逐实例新建 ✓
        /// </summary>
        public static int MaterialPoolCount => Cache.Count;

        /// <summary>
        /// ★ v209：**夜间敌人发光的强度参数 —— 一处集中 ★**
        ///
        /// 需求（逐字）："为夜晚的敌人增加发光"；判据是"敌人明显发亮、玩家/植物不那么亮，白天不发光"。
        ///
        /// ── 为什么改的是**霓虹那条加色通道**，而不是另开一个材质 ──
        /// 本 shader 的霓虹 Pass 是 `Blend One One` 的**加色混合**，且 `_NeonColor` 是 `[HDR]`
        /// （颜色可以 **>1**）⇒ 它本来就是"自发光"那条通道 ✓
        /// 而底色（`_BaseColor`）是被 `mainLight.color` 乘出来的，夜里主光只有 0.10
        /// ⇒ 同一个底色在夜里**怎么乘都亮不起来** ✗ 加色通道不受光照影响 ✓
        ///
        /// ── 数值（★ 这是交付里要报的那一组 ★）──
        ///   白天：发光项**严格 0** ⇒ `_NeonColor` 与改前逐位相同 ✓
        ///   深夜：`1.60 × (1 + 0.40) = ` **2.24 倍底色**（落在建议区间 1.5~2.5 内）
        ///   同时霓虹壳加宽 `+1.2` 屏幕像素（2.2 → 3.4 px）⇒ 读成"向外溢的光"而不是"一条更亮的细边"
        ///   本体边缘光（`_RimColor`）透明度 0.26 → 0.62、亮度 ×1.15
        ///     ⇒ 光留在**身体轮廓上**，不只是外面一圈 ✓
        ///
        /// ★ 为什么是"增益"而不是"夜间绝对值"★
        /// `× (1 + g·Gain)` 在 `g = 0` 时恒等于 1 ⇒ **白天逐位不变**，
        /// 不需要一个"白天"分支去维护 —— 少一个分支就少一处会与夜里跑偏的地方 ✓
        /// </summary>
        public const float NightGlowGain = 0.40f;

        /// <summary>夜间霓虹壳额外加宽的屏幕像素数（叠在逐实例的 `_NeonPixels` 上）。</summary>
        public const float NightGlowExtraPixels = 1.2f;

        /// <summary>夜间本体边缘光的目标透明度（白天的值来自材质的 `_RimColor.a` = 0.26）。</summary>
        public const float NightGlowRimAlpha = 0.62f;

        /// <summary>夜间本体边缘光的亮度倍数（白天恒 1 = 不干预）。</summary>
        public const float NightGlowRimBoost = 1.15f;

        /// <summary>
        /// 某一层霓虹在夜间要乘多少倍 —— **唯一实现**，`ModelView.ComposeNeon` 与自检都读它。
        ///
        /// 抽成纯函数的理由与本文件其它算式一样：它是"可离线断言的数"。
        /// `glow01 = 0` ⇒ 返回 `baseBoost` **原值**（逐位相同）⇒ 白天观感不变 ✓
        /// 敌人档（`baseBoost = 1.60`）在深夜 ⇒ `1.60 × 1.40 = ` **2.24** ✓
        /// </summary>
        public static float NightNeonMultiplier(float baseBoost, float glow01)
            => baseBoost * (1f + Mathf.Clamp01(glow01) * NightGlowGain);

        /// <summary>
        /// ★★ v214：卡通贴图的种类 —— **一张枚举就是"有哪些材质"的唯一清单** ★★
        ///
        /// 每张图的文件名是 `Resources/Art/Textures/toon_<名字>.png`，
        /// 由 `Tools/asset_gen/gen_toon_textures.py` 生成（64×64、可无缝平铺、
        /// 已归一到平均亮度 0.92 ⇒ 挂上贴图只会把表面**压出结构**，不会整体变暗）。
        ///
        /// 顺序即 id，**不要重排**：`MatKey` 把它当整数存，重排等于换语义。
        /// </summary>
        public enum ToonTex
        {
            None = 0,
            Wood = 1,
            Stone = 2,
            Roof = 3,
            Cloth = 4,
            Grass = 5,
        }

        /// <summary>贴图名（与文件名一一对应）。</summary>
        private static readonly string[] TexFiles = { null, "toon_wood", "toon_stone", "toon_roof", "toon_cloth", "toon_grass" };

        /// <summary>
        /// 贴图缓存：**只在第一次用某种贴图时 Load 一次**，之后全工程共用同一张 `Texture2D`。
        /// 与材质池同一条纪律 —— 不允许每个物体各持一份。
        /// </summary>
        private static readonly Dictionary<ToonTex, Texture2D> TexCache = new Dictionary<ToonTex, Texture2D>();

        /// <summary>取一张卡通贴图（缺失时返回 null，材质退回纯色 —— 不崩、不报错刷屏）。</summary>
        public static Texture2D Tex(ToonTex id)
        {
            if (id == ToonTex.None) return null;
            if (TexCache.TryGetValue(id, out Texture2D cached) && cached != null) return cached;
            string file = TexFiles[(int)id];
            Texture2D tex = Resources.Load<Texture2D>("Art/Textures/" + file);
            if (tex == null)
            {
                if (MissingTex.Add(id))
                    Log.Warn($"[Toon] 卡通贴图 Art/Textures/{file} 缺失（材质退回纯色，重跑 Tools/asset_gen/gen_toon_textures.py）");
                return null;
            }
            TexCache[id] = tex;
            return tex;
        }

        /// <summary>缺失贴图只报一次，不要每个物体刷一行。</summary>
        private static readonly HashSet<ToonTex> MissingTex = new HashSet<ToonTex>();

        /// <summary>已加载的贴图张数（给探针/自检判"没有重复加载"）。</summary>
        public static int TextureCount => TexCache.Count;

        private static Shader _shader;
        private static readonly Dictionary<MatKey, Material> Cache = new Dictionary<MatKey, Material>();

        /// <summary>
        /// ★★ v208：材质池的缓存键 —— **三个维度各自独立，任何一个不同就不可能相等** ★★
        ///
        /// ── 旧键为什么是错的（v206 留下的，v208 修）──
        /// 旧式子是 `int key = ((rgb * 397) ^ widthKey) * (simple ? 2 : 1);`
        /// `simple` 那一支**只是把整个键乘 2**，并没有把两个命名空间隔开：
        ///   · `simple = true` 的键**全部落在偶数**上；
        ///   · `simple = false` 的键里**约一半也是偶数**（`(rgb*397)^w` 的奇偶取决于 rgb^w）。
        /// ⇒ 只要存在 `H_完整 == 2 × H_简化`，两个**完全不同的请求**就落进同一个 `key`，
        ///   `Cache` 返回先到的那份 ⇒ **后到的那个对象拿到了别人的材质**：
        ///   底色不对、`Seedfall/ToonSimple`（无描边无霓虹）与 `Seedfall/Toon` 互相串。
        /// 这不会报错、不会崩，只会"看起来偶尔颜色不对"——最难查的那一类 ✓
        ///
        /// ── 现在为什么不可能再撞 ──
        /// 用一个三字段的结构体当键，靠**结构化相等**判断，不再依赖任何"乘一个数把两边错开"的技巧。
        /// 撞键从"概率问题"变成"不可能"——因为三个字段各自比较，不是塞进一个 int ✓
        /// </summary>
        private readonly struct MatKey : System.IEquatable<MatKey>
        {
            private readonly int _rgb;
            private readonly int _widthKey;
            private readonly bool _simple;
            // ★ v214：贴图也是**一个独立维度**，同样不许混进别的字段里 ★
            private readonly int _tex;
            private readonly int _tilingKey;
            private readonly bool _worldUV;

            public MatKey(int rgb, int widthKey, bool simple, int tex, int tilingKey, bool worldUV)
            {
                _rgb = rgb;
                _widthKey = widthKey;
                _simple = simple;
                _tex = tex;
                _tilingKey = tilingKey;
                _worldUV = worldUV;
            }

            public bool Equals(MatKey other)
                => _rgb == other._rgb && _widthKey == other._widthKey && _simple == other._simple
                   && _tex == other._tex && _tilingKey == other._tilingKey && _worldUV == other._worldUV;

            public override bool Equals(object obj) => obj is MatKey other && Equals(other);

            public override int GetHashCode()
            {
                // 混合只是"分桶"，**不影响正确性**（相等由上面六个字段决定）
                unchecked
                {
                    int h = _rgb;
                    h = (h * 31) ^ _widthKey;
                    h = (h * 31) ^ (_simple ? 1 : 0);
                    h = (h * 31) ^ _tex;
                    h = (h * 31) ^ _tilingKey;
                    h = (h * 31) ^ (_worldUV ? 1 : 0);
                    return h;
                }
            }
        }

        public static Shader ToonShader
        {
            get
            {
                if (_shader == null) _shader = Shader.Find(ShaderName);
                return _shader;
            }
        }

        /// <summary>
        /// 把一个物体（含子物体）的所有材质槽换成卡通材质，返回替换的槽数。
        /// simple=true 时用 Seedfall/ToonSimple（只有本体 Pass，无墨线无霓虹）——**给场景装饰用**。
        ///
        /// 为什么装饰要走简化版：它们常驻 400+ 个，每个都跑三个 Pass 会把 drawCall 推到 ~2000；
        /// 而描边对装饰本来就没意义——每根草勾一圈黑边，近看是一团黑线。
        /// 这是**性能与观感同时受益**，不是取舍。
        ///
        /// ★ v214：描边粗细 **1.6% → 2.6%**（夹 8mm ~ 7cm）★
        /// 用户本轮要"黑色描边**更粗**"。旧值在玩法机位下是 0.17~0.69 像素
        /// （见 `OutlineMinPixels` 的读数），即便有 2.2 像素的下限兜着，
        /// 近景的大体型敌人（1.55 米 ⇒ 2.5 厘米）也才刚够下限 ⇒ 线与线的粗细没有层次。
        /// 提到 2.6% 之后：玩家 1.7 厘米 / 敌人 2.5~4 厘米 / 房子 7 厘米，
        /// 与 2.2 像素下限配合，近处用米数（贴着模型）、远处用像素（不低于 2.2px）✓
        /// </summary>
        public static int Apply(GameObject root, bool simple = false)
        {
            if (root == null || ToonShader == null) return 0;
            int replaced = 0;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                Material[] mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;

                // 描边粗细：取该渲染器最大边长的 2.6%，并夹在 8mm ~ 7cm
                float maxDim = Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z));
                float outline = Mathf.Clamp(maxDim * 0.026f, 0.008f, 0.07f);

                for (int m = 0; m < mats.Length; m++)
                {
                    mats[m] = Get(SourceColor(mats[m]), outline, simple);
                    replaced++;
                }
                r.sharedMaterials = mats;
            }
            return replaced;
        }

        private static Color SourceColor(Material m)
        {
            if (m == null) return Color.white;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        /// <summary>
        /// 按 (底色, 描边粗细) 取一份**共享**卡通材质。
        ///
        /// ★ v181：`private` → `internal` ★
        /// `ProceduralBugs` 的虫怪网格没有 FBX 自带的材质可读，必须**直接**要一份材质。
        /// 让它自己 `new Material` 会绕开这个缓存 ⇒ 每只虫一份材质（正是需求禁止的事）✓
        /// 改成 `internal` 而不是 `public`：只在**本程序集**里可调，游戏外部代码仍然看不到它 ✓
        ///
        /// ★ v214：新增三个**带默认值**的参数（`tex` / `tiling` / `worldUV`）★
        /// 这样既有的 20 多处调用（`PlayerBean` / `SkinModels` / `ProceduralBugs` / `TownKit` …）
        /// **一个字都不用改**，拿到的仍然是逐位相同的那份材质（贴图维度 = None）✓
        /// 只有本轮新写的美术着装脚本显式传贴图。
        /// </summary>
        /// <summary>
        /// v251：`Shader.Find` 拿到 null 时的**一次性**告警（不刷屏）。
        /// 打包后如果看到这条，说明 `ProjectSettings/GraphicsSettings.asset` 的 `m_AlwaysIncludedShaders`
        /// 又漏了 `Seedfall/*`（那就是"全蓝"的根因）。
        /// </summary>
        private static bool _warnedMissingShader;
        private static void WarnMissingShader()
        {
            if (_warnedMissingShader) return;
            _warnedMissingShader = true;
            Log.Warn("[Toon] 取不到 `Seedfall/Toon`（或 ToonSimple）shader ⇒ 退回内置 unlit。" +
                     "若是 Player 里出现，请确认 ProjectSettings/GraphicsSettings.asset 的 " +
                     "m_AlwaysIncludedShaders 里包含 7 个 Seedfall/* shader（否则会被剥离）");
        }

        internal static Material Get(Color c, float outlineWidth, bool simple = false,
                                     ToonTex tex = ToonTex.None, float tiling = 1f, bool worldUV = false)
        {
            int rgb = (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16)
                    | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8)
                    | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
            int widthKey = Mathf.RoundToInt(outlineWidth * 10000f);
            int tilingKey = tex == ToonTex.None ? 0 : Mathf.RoundToInt(Mathf.Max(0.01f, tiling) * 100f);
            // ★ v208：各维度各自进键（见 `MatKey` 的说明）——
            //   旧式子 `((rgb * 397) ^ widthKey) * (simple ? 2 : 1)` 会让 simple 与完整版撞键 ✗
            // v214：贴图 / tiling / 世界UV 各自再占一个字段，同理不许混算 ✓
            var key = new MatKey(rgb, widthKey, simple, (int)tex, tilingKey, worldUV);

            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;

            Shader sh = ToonShader;
            if (simple)
            {
                if (_simpleShader == null) _simpleShader = Shader.Find(SimpleShaderName);
                if (_simpleShader != null) sh = _simpleShader;
            }

            // ★★ v251：**shader 拿不到时不许抛异常** ★★
            //   打包事故：`Shader.Find("Seedfall/Toon")` 在 Player 里返回 null（没被材质直接引用 ⇒ 被剥离）
            //   ⇒ `new Material(null)` 抛 `ArgumentNullException` ⇒ `TownBootstrap.Awake` 在 `BuildGround` 中断
            //   ⇒ **整座城没建、画面全蓝** ✗
            //   加这条兜底后，即使将来又漏加 `Always Included Shaders`，也只是"颜色不对"，不会整城消失 ✓
            if (sh == null)
            {
                Shader fallback = Shader.Find("Sprites/Default");
                if (fallback == null) fallback = Shader.Find("Unlit/Color");
                if (fallback == null)
                {
                    WarnMissingShader();
                    return null;                       // 连兜底都没有 ⇒ 返回 null，由调用方决定怎么降级
                }
                WarnMissingShader();
                sh = fallback;
            }

            string texTag = tex == ToonTex.None ? "" : "_" + TexFiles[(int)tex].Substring(5);
            Material mat = new Material(sh)
            {
                name = simple
                    ? $"M_ToonS_{rgb:X6}{texTag}"
                    : $"M_Toon_{rgb:X6}_{outlineWidth * 1000f:0.#}mm{texTag}"
            };
            mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 1f));
            mat.SetColor("_ShadeColor", Shade(c));
            mat.SetColor("_RimColor", new Color(1f, 0.98f, 0.92f, 0.26f));
            mat.SetFloat("_ShadeThreshold", 0.44f);
            mat.SetFloat("_ShadeSoft", 0.035f);
            mat.SetFloat("_RimPower", 3.4f);
            // v177：光照模型的两个旋钮（见 SeedfallToon.shader 的 Properties 注释）。
            // 必须**显式写**而不是靠 Properties 块的默认值 —— 默认值只在"新建材质并序列化"时生效，
            // 而这里是 `new Material(shader)` 之后逐项赋值再进缓存，
            // 不写就等于把观感交给 shader 的默认值，将来改 shader 会静默改掉全部材质 ✓
            mat.SetFloat("_AmbientStrength", 0.45f);
            mat.SetFloat("_AOStrength", 0.7f);

            // ── v214：贴图（两个 shader 都有这两个属性）──
            // 显式写死 `_TexMode` 与 `_MainTex_ST`：尤其是**不挂贴图时也要写**
            // （`_MainTex` 留空 ⇒ 采样默认的 "white" ⇒ 逐位不变；但 `_TexMode` 若不写，
            //   将来改 shader 默认值会静默改掉全部材质 —— 与本文件其它旋钮同一条纪律）
            if (tex != ToonTex.None)
            {
                Texture2D t = Tex(tex);
                if (t != null)
                {
                    mat.SetTexture("_MainTex", t);
                    mat.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
                }
                mat.SetFloat("_TexMode", worldUV ? 1f : 0f);
            }
            else
            {
                mat.SetFloat("_TexMode", 0f);
            }

            // ★ v214：描边属性**两个 shader 都写** ★
            // 改前这一段在 `if (!simple)` 里 —— 因为 `ToonSimple` 那时根本没有描边 Pass。
            // 现在它有（默认宽度 0 ⇒ 顶点退化 ⇒ 零成本），所以这里照样写：
            // `TownKit` 传的就是 `outline = 0` ⇒ 与改前逐位相同，装饰分文不付 ✓
            mat.SetColor("_OutlineColor", OutlineBlack);
            mat.SetFloat("_OutlineWidth", outlineWidth);
            mat.SetFloat("_OutlinePixels", simple ? 0f : OutlineMinPixels);
            mat.SetFloat("_OutlineDepthFade", OutlineFadeMeters);

            if (!simple)
            {
                // ★ v206：霓虹描边的那两个旋钮（霓虹**颜色/宽度**仍然逐实例走 MPB：
                //   敌人 = `SetNeon(红)`、作物 = 品质色、玩家/小猪 = alpha 0 关闭）
                //   这里写的是**材质级**参数：屏幕最小宽度与淡出距离。
                //   显式写而不是靠 shader 默认值——与上面那几个旋钮同一条纪律：
                //   默认值只在"新建材质"时生效，将来改 shader 会静默改掉全部材质的观感 ✓
                mat.SetFloat("_NeonPixels", NeonMinPixels);
                mat.SetFloat("_NeonFade", NeonFadeMeters);
            }

            Cache[key] = mat;
            return mat;
        }

        /// <summary>
        /// ★★ v214：描边颜色 —— **一处集中**（着色器的 Properties 默认值也必须是同一个数）★★
        ///
        /// `(0.16,0.13,0.11)` 是"墨棕"，在奶油底 + 暖光的主城里读作深褐，不是墨线。
        /// 用户本轮的原话是"加入**黑色**描边"⇒ 三通道压到 5%。
        /// 不给 0 的原因：纯黑在最暗的 `_ShadeColor` 面上会与暗部糊成一片，
        /// 反而看不出"那里有一条线"；5% 在亮部是黑、在暗部仍能与暗面分开 ✓
        /// </summary>
        public static readonly Color OutlineBlack = new Color(0.05f, 0.05f, 0.05f, 1f);

        private static Shader _simpleShader;

        /// <summary>
        /// 暗部颜色：往冷紫方向压一点再降明度。
        /// 纯按比例变暗会显得脏；参考美术的阴影都带一点冷色，这是"通透"的来源。
        /// </summary>
        private static Color Shade(Color c)
        {
            Color cold = new Color(0.44f, 0.42f, 0.56f, 1f);
            Color s = Color.Lerp(c, cold, 0.40f) * 0.88f;
            s.a = 1f;
            return s;
        }

        /// <summary>把场景里所有模型（含已生成的预览）都刷一遍，供美术批量验收。</summary>
        public static int ApplyAll()
        {
            int n = 0;
            // v251：`FindObjectsSortMode` 这个类型本身已 obsolete ⇒ 改用**不带 SortMode** 的重载
            Renderer[] all = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].gameObject.name.StartsWith("PREVIEW_")) continue;   // 预览已单独刷过
                Material[] mats = all[i].sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                float maxDim = Mathf.Max(all[i].bounds.size.x,
                                Mathf.Max(all[i].bounds.size.y, all[i].bounds.size.z));
                // v214：与 `Apply` 同步（1.6% / 4mm~5cm → 2.6% / 8mm~7cm）
                float outline = Mathf.Clamp(maxDim * 0.026f, 0.008f, 0.07f);
                bool changed = false;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null && mats[m].shader == ToonShader) continue;
                    mats[m] = Get(SourceColor(mats[m]), outline);
                    changed = true;
                }
                if (changed) { all[i].sharedMaterials = mats; n++; }
            }
            return n;
        }
    }
}
