// Seedfall 守园者 · 漫画卡通着色器（描边 + 霓虹高亮）
// ------------------------------------------------------------------
// 四层，由外到内：
//   ① 霓虹高亮描边（v68）——最外层，加色混合，颜色由 MaterialPropertyBlock 逐实例设置
//      敌人红 / 植物与种子按品质色 / 敌人子弹红
//   ② 黑色墨线（反向外壳）——参考美术的勾边
//   ③ 硬边分层光照 —— 两级色块，不是平滑渐变
//   ④ 边缘光 rim —— 把角色从背景里抠出来
//
// ★ 编辑约定 ★
// 本文件含中文注释，**不要用 PowerShell 的 Get-Content/Set-Content 做文本替换**：
// Windows PowerShell 5.1 按 ANSI 读、按 UTF-8 写，中文会变成乱码，换行也会被吃掉。
// 这个坑已经踩过两次。改动请用编辑器工具直接写文件。
//
// 目录约定：Shaders/ 是**人类手写资产**，不参与程序化重建；
//           Resources/Art/** 是脚本产物，可整目录删除重建。两者永不混放。
Shader "Seedfall/Toon"
{
    Properties
    {
        [MainColor] _BaseColor ("亮部颜色", Color) = (1,1,1,1)
        _ShadeColor ("暗部颜色", Color) = (0.62,0.60,0.68,1)
        _ShadeThreshold ("明暗分界", Range(0,1)) = 0.42
        _ShadeSoft ("分界柔和度", Range(0.001,0.5)) = 0.03
        [HDR] _RimColor ("边缘光颜色", Color) = (1,1,1,0.30)
        _RimPower ("边缘光范围", Range(0.5,8)) = 3.2
        // ★★ v214：描边回到**真正的黑**，并且**真的画得出来** ★★
        //
        // ── 颜色：(0.16,0.13,0.11) → (0.05,0.05,0.05) ──
        // 旧值是"墨棕"，在奶油底 + 暖光的卡通场景里读作"深褐",不是墨线。
        // 用户本轮的原话是"加入**黑色**描边"⇒ 三通道近似相等且压到 5%。
        // 为什么不给 0：纯黑在最暗的 `_ShadeColor` 面上会与阴影糊成一片，
        // 反而看不出"有一条线"；0.05 在亮部是黑边、在暗部仍能与暗面区分 ✓
        //
        // ── 宽度：0.013 → 0.030（Range 上限 0.06 → 0.10）──
        // 见 `ToonLook.Apply` 的读数：0.013 在玩法机位（20.8m / 62°fov / 1080p）
        // 下只有 **0.56 像素** —— 一个像素都没留下。
        // 0.030 是 1.3 像素，再靠下面的 `_OutlinePixels` 兜到 2.2 像素 ✓
        _OutlineColor ("描边颜色", Color) = (0.05,0.05,0.05,1)
        _OutlineWidth ("描边粗细(米)", Range(0,0.10)) = 0.030
        // ★★ v214：描边的**屏幕宽度下限（像素）** ★★
        // 这一条以前只写在 C# 里（`ToonLook.OutlineMinPixels` → `mat.SetFloat("_OutlinePixels", …)`），
        // 而**着色器里既没声明、也没使用它** ⇒ 那个值设了个寂寞，
        // 描边宽度一直只有"米数"一条腿 ⇒ 远处/小物体上的墨线全是亚像素 ✗
        // （`ProbeV210Outline` 已经把这个字段读出来报了 — 这里补上真正的实现）
        // 换算式与霓虹那一份**逐字相同**（见 NeonVert 里的推导），只是各自读自己的属性 ✓
        _OutlinePixels ("描边最小屏幕宽度(像素)", Range(0,8)) = 2.2
        // 远景淡出：22 → 200（C# 侧 `OutlineFadeMeters` 早就是 200，这里只是把默认值对齐，
        // 免得"靠 shader 默认值"的地方悄悄比靠 C# 的那一批更淡）✓
        _OutlineDepthFade ("描边远景淡出", Range(0,400)) = 200

        // ★★ v214：卡通贴图（交付项 ②：带纹理的贴图材质）★★
        // 与 `_BaseColor` 是**乘算**关系：贴图只负责"表面的明暗结构"（木纹/石缝/瓦垄/织纹），
        // 颜色仍然由 `_BaseColor` 决定 ⇒ 同一张 `toon_wood` 能同时当浅色栅栏和深色桶身。
        // 默认 "white" ⇒ 没挂贴图的材质**逐位不变**（老资产零影响）✓
        _MainTex ("卡通纹理", 2D) = "white" {}
        // 0 = 用网格自带 UV（道具、模型）；1 = 用世界 XZ 投影（地面/屋顶这类大平面）
        // 为什么要两个模式：`TownKit.Box` 的方块 UV 是 0~1/面，把一张 64 的图拉过 76 米
        // ⇒ 1 texel ≈ 1.2 米，糊成一片色；世界投影下 tiling 才和"每米几张"直接对应 ✓
        _TexMode ("贴图坐标模式(0=网格UV 1=世界XZ)", Range(0,1)) = 0

        // 运行时染色乘数（受击闪白 / 冰冻 / 灼烧 / 血量变暗）。
        // 为什么单独开一个属性而不是改 _BaseColor：MaterialPropertyBlock 是**按渲染器**生效的，
        // 一个角色身上有 6~11 个材质槽，用 MPB 改 _BaseColor 会把所有槽位覆盖成同一个颜色
        // （实测表现：敌人渲染成深色一团）。染色必须是乘数，才能保留各槽位自己的底色。
        _TintMul ("染色乘数", Color) = (1,1,1,1)

        // 霓虹高亮描边：铺在墨线之外，alpha=0 表示关闭。
        // 同样必须独立属性——靠 MPB 逐实例设置，否则改一处会串到所有同类模型上。
        [HDR] _NeonColor ("霓虹描边颜色", Color) = (0,0,0,0)
        _NeonWidth ("霓虹描边额外宽度(米)", Range(0,0.08)) = 0.010
        // ★ v206：霓虹的**屏幕宽度下限（像素）** —— 见 NeonVert 里"为什么必须按像素算"★
        //   0 = 回到旧行为（只按 _NeonWidth 的米数算）
        _NeonPixels ("霓虹描边最小屏幕宽度(像素)", Range(0,6)) = 1.4
        // ★ v206：霓虹的**淡出距离**（米），与黑墨线的 `_OutlineDepthFade` 分开 ★
        _NeonFade ("霓虹在多少米处淡出", Range(5,200)) = 80

        // v177 新增：光照模型的两个旋钮
        //   环境光参与度 —— 0 = 回到旧行为（暗部只有 _ShadeColor，完全无视 RenderSettings.ambientLight）
        //                    1 = 环境光全量参与间接漫反射
        //   屏幕空间 AO 强度 —— 只影响"接触处压暗"，不影响整体亮度
        _AmbientStrength ("环境光参与度", Range(0,2)) = 0.45
        _AOStrength ("屏幕空间AO强度", Range(0,1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ────────────────────────────────── ① 霓虹高亮描边（最外层）
        //
        // ★★★ v222 结论：这一条**在 Deferred+ 下不画**（保持 `SRPDefaultUnlit`，见下方 ② 的说明）★★★
        //
        // ── 本项目为什么 `SRPDefaultUnlit` 一条都不画 ──
        // `Assets/Settings/PC_Renderer.asset` 的 `m_RenderingMode: 2` ⇒ **Deferred+**。
        // 延迟管线只画 `UniversalGBuffer`；没有它的材质落到 URP 的 **forward-only** 那一遍，
        // 而那一遍的 ShaderTagId 表里没有 `SRPDefaultUnlit` ✗
        // ⇒ 本 Pass 与 ② 的 Outline 此前**都**一帧没画过（实测见 ② 的三条证据）。
        //
        // ── 那为什么把"会被画"的那个标签给了 Outline，而不是给霓虹 ──
        // `UniversalForwardOnly` 只能给**一个** Pass：同一个材质里两个 Pass 挂同一个标签时，
        // 仍然是**第一个**赢（正是相邻两条 `SRPDefaultUnlit` 的老毛病）。
        // 黑墨线是"每个模型都要有"的那条，霓虹是敌人身上可选的一层红圈 ⇒ 把标签给黑墨线 ✓
        // ⚠️ 要让红圈回来：给它单独走 GBuffer Pass 或一个 RendererFeature，不要再来抢这个标签。
        Pass
        {
            Name "Neon"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One One          // 加色混合：发光感来自叠加，不是靠不透明度

            HLSLPROGRAM
            #pragma vertex NeonVert
            #pragma fragment NeonFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                float4 _NeonColor;
                // ★ v214：贴图的 tiling/offset 必须进 `UnityPerMaterial` ★
                // 这是 SRP Batcher 的硬要求：一个 shader 的**所有** Pass 必须声明
                // 布局逐字节相同的 `UnityPerMaterial`，否则整个 shader 掉出批处理
                // （不掉功能、不报错，只是 draw call 悄悄涨上去）。
                float4 _MainTex_ST;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _OutlinePixels;
                float  _NeonWidth;
                float  _NeonPixels;
                float  _NeonFade;
                float  _OutlineDepthFade;
                float  _AmbientStrength;
                float  _AOStrength;
                float  _TexMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float dist : TEXCOORD0; };

            Varyings NeonVert(Attributes IN)
            {
                Varyings OUT;
                // ★ 霓虹关闭时把三角形退化成空 ★
                // 不这样做的话，即使片元里 clip 掉了，整个模型**照样要提交一次绘制**
                // （顶点跑完、三角形光栅化后才在片元被丢弃）。场上有 400+ 装饰渲染器，
                // 每个都白跑一遍霓虹 Pass，实测直接把 draw call 从 ~800 推到 1690。
                // 写一个 w=0 的裁剪空间坐标，GPU 在裁剪阶段就整块丢掉，成本接近零。
                if (_NeonColor.a < 0.01h)
                {
                    OUT.positionCS = float4(0, 0, 0, 0);
                    OUT.dist = 0;
                    return OUT;
                }

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                float  dist  = length(posWS - GetCameraPositionWS());

                // ★★★ v206：霓虹的宽度 = max(米数, 屏幕像素数换算成米) ★★★
                //
                // ── 为什么必须按像素算（★ 实测，不是推断 ★）──
                //   游戏机位（`tuning.camera`：fov 62°、机位离地约 38 米）下，
                //   720p 的 **1 像素 ≈ 6.3 厘米**。而敌人身上的霓虹宽度来自
                //   `ProceduralBugs`（`Clamp(Unit × 0.02, 0.012, 0.09)` ⇒ 小怪 1.4 厘米）
                //   或 `ModelView.SetNeon`（`RawHeight × 0.05` 夹 1.8~9 厘米）
                //   ⇒ **不足 1/4 像素** ⇒ 画面上一个红点都没有 ✓
                //   （黑墨线同病：`Clamp(maxDim × 0.016, 0.004, 0.05)`，小怪只有 7 毫米）
                //   ⇒ 这就是"代码里明明有霓虹红描边、屏幕上却看不见"的**唯一原因** ✓
                //
                // ── 换算式（透视）──
                //   距离 d 处，一屏高的世界尺寸 = 2·d·tan(fov/2)；
                //   URP 的 `UNITY_MATRIX_P._m11 = cot(fov/2) = 1/tan(fov/2)`
                //   ⇒ 每像素 = 2·d / (_m11 · screenHeight) ✓
                //   正交相机走另一条（`unity_OrthoParams`：xy = 半宽/半高，w = 1 表示正交）✓
                //   ⇒ 这样"几像素宽"在任何分辨率、任何机位下都成立，不会在大屏上变细 ✓
                float worldPerPixel;
                if (unity_OrthoParams.w > 0.5h)
                    worldPerPixel = 2.0 * unity_OrthoParams.y / max(1.0, _ScreenParams.y);
                else
                    worldPerPixel = 2.0 * dist / max(0.0001, UNITY_MATRIX_P._m11 * _ScreenParams.y);
                float neonWidth = max(_NeonWidth, worldPerPixel * _NeonPixels);

                // ★★★ v206：霓虹铺在**黑墨线外面**（v123 那版把它铺在里面 ⇒ 被整条吃掉）★★★
                //
                // ── v123 写的"两圈相邻不重叠"是错的（几何上不成立）★ ──
                //   反向外壳（Cull Front + 沿法线外扩 d）在屏幕上**占据的环带是 [R, R+d]**，
                //   不是 [R+d-N, R+d] ✗ ⇒ 外壳越大，占的环越**宽且包含**内圈 ✓
                //   v123 让霓虹扩 `_OutlineWidth`、黑边扩 `_OutlineWidth + _NeonWidth`
                //   ⇒ 黑边环 [R, R+W+N] **完整包含**霓虹环 [R, R+W] ✗
                //   ⇒ 而两者 ZTest 都是 LEqual、黑边离相机更近（外壳更大 ⇒ 更贴相机）
                //   ⇒ **每一像素都是黑边赢** ⇒ 霓虹一个像素都留不下 ✓
                //   这就是"敌人没有红描边"的第二层原因（与上面的亚像素叠加）✓
                //
                // ── 现在的分工（这才是"相邻不重叠"）──
                //   黑墨线：扩 `_OutlineWidth`            ⇒ 环 [R, R+W]      （贴着模型，定义轮廓）
                //   霓虹  ：扩 `_OutlineWidth + 霓虹宽`   ⇒ 环 [R+W, R+W+N]  （在黑线**外面**发光）
                //   ⇒ 内环被更近的黑壳遮住（ZTest LEqual 判掉）、外环只有霓虹壳能覆盖 ✓
                posWS += nrmWS * (_OutlineWidth + neonWidth);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.dist = length(posWS - GetCameraPositionWS());
                return OUT;
            }

            half4 NeonFrag(Varyings IN) : SV_Target
            {
                clip(_NeonColor.a - 0.01h);       // alpha 当作开关
                // ★ v206：霓虹用**自己的**淡出距离（`_NeonFade`），不再借用黑墨线的
                //   `_OutlineDepthFade`（26 米）。游戏机位离地约 38 米 ⇒
                //   旧式子在玩法视角下把霓虹压到 **8.7% 亮度** ⇒ 红描边几乎等于关掉 ✓
                //   黑墨线仍然用 26 米淡出（"远景角色糊成一团黑"那条理由对它成立，对霓虹不成立）
                half fade = saturate(1.0h - IN.dist / max(0.001h, _NeonFade));
                clip(fade - 0.02h);
                return half4(_NeonColor.rgb * _NeonColor.a * fade, 0.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────────── ② 黑色墨线（反向外壳）
        //
        // ★★★ v222：`LightMode` = `SRPDefaultUnlit` → **`UniversalForwardOnly`** ★★★
        //
        // ── 根因（实测，不是推断）──
        // `Assets/Settings/PC_Renderer.asset` 的 `m_RenderingMode: 2` ⇒ 这是 **Deferred+** 管线。
        // 延迟管线只画 `UniversalGBuffer` 那个 Pass；没有它的材质落到 URP 的 **forward-only** 一遍，
        // 而这一遍的 ShaderTagId 表里**没有 `SRPDefaultUnlit`** ✗
        // ⇒ 本 Pass 此前**一帧都没被画过**。三条实测证据：
        //   ① `OutlineProbe` 把全部 `Seedfall/Toon` 材质的 `_OutlineWidth` 推到 **0.25 米**
        //      （玩家身高的 38%）再截图：玩家身上**仍然一条线都没有**；
        //   ② 本 Pass 的 `ZTest` 临时改成 `Always`（排除"被深度判掉"）再试：**还是一条线都没有**；
        //   ③ 玩家/敌人的材质值本身是对的（`M_Toon_DEBD66_4mm`：`_OutlineWidth=0.004 m`、
        //      `_OutlinePixels=2.20 px`、`_OutlineColor=(0.05,0.05,0.05)`，不是 0）。
        // ⇒ 玩家/敌人此前那条边 **100% 来自屏幕描边**（v221 在战斗里关掉它 ⇒ 边就没了）✓
        //
        // ── 改法 ──
        // `UniversalForwardOnly` 正是 URP 给"不能走延迟、必须走前向"的物体准备的标签，
        // 它一定在 forward-only 的表里 ⇒ 本 Pass 会被画到。
        // 于是三个 Pass 各占一个真正会被绘制的标签：
        //     Outline = UniversalForwardOnly ｜ Toon = UniversalForward ｜ Neon = SRPDefaultUnlit
        // ⚠️ 霓虹那条仍是 `SRPDefaultUnlit` ⇒ 在 Deferred+ 下**依旧不画**（它此前也一样没画过）。
        //    本轮不顺手改它：它要再占一个"会被绘制"的标签，而前向表里已经没有第三个了 ——
        //    真要让红圈出现，得给它单独走 GBuffer 或 RendererFeature，是另一件事。
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                float4 _NeonColor;
                // ★ v214：贴图的 tiling/offset 必须进 `UnityPerMaterial` ★
                // 这是 SRP Batcher 的硬要求：一个 shader 的**所有** Pass 必须声明
                // 布局逐字节相同的 `UnityPerMaterial`，否则整个 shader 掉出批处理
                // （不掉功能、不报错，只是 draw call 悄悄涨上去）。
                float4 _MainTex_ST;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _OutlinePixels;
                float  _NeonWidth;
                float  _NeonPixels;
                float  _NeonFade;
                float  _OutlineDepthFade;
                float  _AmbientStrength;
                float  _AOStrength;
                float  _TexMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float dist : TEXCOORD0; };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                float  dist  = length(posWS - GetCameraPositionWS());

                // ★★★ v214：描边宽度 = max(米数, 屏幕像素数换算成米) ★★★
                //
                // ── 为什么这条**必须有**（实测读数，不是推断）──
                // `ToonLook.Apply` 给的描边是"包围盒最大边 × 1.6%，夹 4mm~5cm"。
                // 玩家 65 像素高、一支草 8 像素高 ⇒ 描边 4~16 mm。
                // 而玩法机位（20.84 米、62° fov、1080p）下 **1 像素 = 2.32 厘米**
                // ⇒ 那条线是 **0.17 ~ 0.69 像素** ⇒ 光栅化时被丢干净，屏幕上从来没有墨线 ✗
                // C# 侧早就写了 `mat.SetFloat("_OutlinePixels", …)`，
                // 但这个着色器**没声明也没使用**它 ⇒ 那行代码一直是死设置 ✓
                //
                // ── 换算式（与 `NeonVert` 逐字相同，推导见那一处）──
                //   透视：每像素 = 2·d / (cot(fov/2) · 屏高)
                //   正交：每像素 = 2·半高 / 屏高（`unity_OrthoParams.y` 就是半高）
                //   主城是**正交**机位（半高 9.5）⇒ 1 像素 = 19/1080 = 1.76 厘米
                //   ⇒ 2.2 像素 = 3.9 厘米，与 0.030 米那条腿同一个量级，两者互为兜底 ✓
                float worldPerPixel;
                if (unity_OrthoParams.w > 0.5h)
                    worldPerPixel = 2.0 * unity_OrthoParams.y / max(1.0, _ScreenParams.y);
                else
                    worldPerPixel = 2.0 * dist / max(0.0001, UNITY_MATRIX_P._m11 * _ScreenParams.y);

                float outlineWidth = max(_OutlineWidth, worldPerPixel * _OutlinePixels);

                // ★ 宽度为 0 时把三角形退化成空 ★
                // 与霓虹那条同一个道理：只在片元里 `clip` 的话，整个模型照样要提交一次绘制。
                // 写 w=0 的裁剪坐标 ⇒ GPU 在裁剪阶段整块丢掉，成本接近零 ✓
                // 这一条现在很关键：`Seedfall/ToonSimple`（装饰用的那一份）也接了同样的描边 Pass，
                // 而主城 400+ 装饰的描边宽度是 0 ⇒ 靠这句保证他们一分钱都不多花 ✓
                if (outlineWidth < 0.00001h)
                {
                    OUT.positionCS = float4(0, 0, 0, 0);
                    OUT.dist = 0;
                    return OUT;
                }

                // 沿法线外扩：轮廓线就长在模型外面一圈
                // ★★ v206：黑墨线回到**内圈**（只扩 `_OutlineWidth`）★★
                //   v123 让它扩 `_OutlineWidth + _NeonWidth`（外圈）——
                //   而反向外壳占的是环带 [R, R+d] ⇒ 外圈**整条包含**内圈 ✗
                //   ⇒ 霓虹（内圈、离相机更远）被这条黑壳全遮住 ⇒ 红描边永远看不见 ✓
                //   现在：黑墨线占 [R, R+Outline]（贴着模型，定义轮廓），
                //        霓虹占 [R+Outline, R+Outline+霓虹宽]（在黑线外面发光）✓
                //   两圈**真的**相邻不重叠 —— 这才是 v123 注释里想写的那件事 ✓
                posWS += nrmWS * outlineWidth;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.dist = length(posWS - GetCameraPositionWS());
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                // 远处描边淡出：否则远景角色会糊成一团黑
                half fade = saturate(1.0h - IN.dist / max(0.001h, _OutlineDepthFade));
                clip(fade - 0.02h);
                return half4(_OutlineColor.rgb, 1.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────────── ③ 卡通本体
        Pass
        {
            Name "Toon"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            // ★ 关键字必须与 URP 17 的 `Lit.shader` 前向 Pass **逐条对齐** ★
            // 漏一条的后果不是报错，而是**静默降级**：URP 那边把变体/缓冲都备好了，
            // 而这里没有对应的 #if 分支去用 ⇒ 画面上一片正常但少了一样东西，
            // 且极难定位（"为什么开了软阴影还是硬边？" / "为什么特效不照亮？"）。
            // 旧版只有 3 条（主光阴影 + `_SHADOWS_SOFT`），缺的正是下面这几条 ←
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            // ★ 软阴影在 URP 17 被拆成 LOW/MEDIUM/HIGH 三个变体 ★
            // `PC_RPAsset` 的 m_SoftShadowQuality = 3(High) ⇒ 引擎只开 `_SHADOWS_SOFT_HIGH`。
            // 旧版只声明了 `_SHADOWS_SOFT` ⇒ 那个变体**永远不会被选中** ⇒
            // 软阴影退化成 1 次采样的硬边，而 `Shadows.hlsl` 里的 5/9/17 次采样一次也没跑过。
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            // ★ Forward+ 的簇光照 ★ 本工程 m_RenderingMode = 2 (ForwardPlus)，
            // 没有这个关键字 ⇒ `GetAdditionalLightsCount()` 直接返回 0 ⇒ 附加光源循环空转。
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // AmbientOcclusion.hlsl 提供 GetScreenSpaceAmbientOcclusion / AmbientOcclusionFactor。
            // 它依赖 Core.hlsl 已经带进来的 Input.hlsl（`_AmbientOcclusionParam` 在那边声明）✓
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                float4 _NeonColor;
                // ★ v214：贴图的 tiling/offset 必须进 `UnityPerMaterial` ★
                // 这是 SRP Batcher 的硬要求：一个 shader 的**所有** Pass 必须声明
                // 布局逐字节相同的 `UnityPerMaterial`，否则整个 shader 掉出批处理
                // （不掉功能、不报错，只是 draw call 悄悄涨上去）。
                float4 _MainTex_ST;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _OutlinePixels;
                float  _NeonWidth;
                float  _NeonPixels;
                float  _NeonFade;
                float  _OutlineDepthFade;
                float  _AmbientStrength;
                float  _AOStrength;
                float  _TexMode;
            CBUFFER_END

            // ★ v214：贴图在 `UnityPerMaterial` 之外声明（SRP Batcher 的规矩：
            //   纹理槽不属于 per-material 常量缓冲，混进去会让布局对不上）
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv0        : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                float2 uv         : TEXCOORD3;
            };

            Varyings ToonVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(p.positionCS.z);
                // 换成网格 UV 就乘 `_MainTex_ST`；世界 XZ 模式在片元里从 positionWS 取，
                // 两条路都在片元里决定，顶点这里只负责把两者都带过去 ✓
                OUT.uv = IN.uv0;
                return OUT;
            }

            half4 ToonFrag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float2 ssUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // ── 主光 ──
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // ★ 光照模型：半兰伯特 → 真 N·L ★
                // 旧式 `dot*0.5+0.5` 把背光面抬到 0.5，而阈值是 0.44 ⇒
                //   `dot > -0.12` 的一切都算"亮部" ⇒ **约 84% 的球面是亮的**，
                //   明暗分界线几乎看不见，模型读起来就是"一块平色"。
                // 换成 saturate(N·L) 之后阈值 0.44 落在"略少于一半朝光"的位置：
                //   俯视视角里大量存在的**侧立面**（墙、桶、敌人身体侧面）落进暗部 ⇒
                //   体块关系才出来 ✓ 这是"光影全面升级"里最实质的一条。
                float ndl = saturate(dot(N, mainLight.direction));
                float lit = ndl * mainLight.shadowAttenuation;

                float edge = smoothstep(_ShadeThreshold - _ShadeSoft,
                                        _ShadeThreshold + _ShadeSoft, lit);
                half3 albedo = _BaseColor.rgb * lerp(_ShadeColor.rgb, half3(1, 1, 1), edge);

                // ── 卡通贴图（v214）──
                // 与 `_BaseColor` 是乘算：贴图只给"表面结构"，颜色仍由材质决定。
                // 默认 `_MainTex` = "white" ⇒ 不挂贴图的材质结果**逐位不变** ✓
                // `_TexMode` = 1 时改用世界 XZ 投影：地面/屋顶这种"一整块大平面"网格 UV 是
                // 0~1/面，直接铺会把 64 的图拉过 76 米糊成纯色 ⇒ 必须按世界米数取 ✓
                {
                    float2 uv = (_TexMode > 0.5)
                        ? IN.positionWS.xz * _MainTex_ST.xy
                        : IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                    half3 texMul = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                    albedo *= texMul;
                }

                albedo *= _TintMul.rgb;    // 运行时染色（受击 / 冰冻 / 灼烧 / 血量）

                // ── 屏幕空间环境光遮蔽 ──
                // ★ 之前这个 Renderer Feature 在本工程上等于**空转** ✗
                // `PC_Renderer.asset` 里 SSAO 一直是开着的，但只有**采样它**的着色器才用得上；
                // toon shader 从来没采样过 ⇒ 只有地面（走 URP/Lit）吃到了 AO，
                // 而角色 / 敌人 / 植物 / 装饰这些真正需要接触阴影的东西一点没吃到。
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(ssUV);

                // 主光颜色（含强度）—— 灯光真正参与着色，昼夜循环才拉得动画面
                half3 col = albedo * mainLight.color;
                // 直接光也吃一点 AO：角色贴地处、草丛根部压暗，"贴着地"才读得出来
                col *= lerp(1.0h, ao.directAmbientOcclusion, _AOStrength * 0.35h);

                // ── 环境光（间接光）★ 新增项 ★ ──
                // 旧版**完全没有间接光**：主光一落到 0，模型就是纯 `_ShadeColor` 一坨，
                // 昼夜循环写进 `RenderSettings.ambientLight` 的那个值对着色器**毫无影响** ✗
                // 现在用 URP 自己的间接漫反射 `SampleSHVertex` 取环境光 ⇒
                // 夜里是冷蓝的环境、黄昏是暖橙 —— 观感才叫"有光"，而不是"关灯"。
                half3 ambientSH = SampleSHVertex(N);
                col += albedo * ambientSH * (_AmbientStrength * ao.indirectAmbientOcclusion);

                // ── 附加光源：火焰 / 爆炸 / 子弹 / 特效光池 ──
                // ★ 必须用 LIGHT_LOOP_BEGIN/END，不能用裸 for ★
                // `PC_Renderer.asset` 的 m_RenderingMode = 2 ⇒ **Forward+**，
                // 附加光源由**簇（cluster）**索引。裸 for + `GetAdditionalLightsCount()`
                // 在簇模式下返回 0 ⇒ 循环一次都不执行 ⇒ "特效根本不照亮任何东西"，且不报错。
                // 宏在簇模式下会自动换成 `ClusterInit/ClusterNext` 的遍历 ✓
                // 注意：宏里**硬写了 `inputData`** 这个名字 ⇒ 变量名必须叫 inputData
#if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.normalizedScreenSpaceUV = ssUV;
                inputData.positionWS = IN.positionWS;
                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light l = GetAdditionalLight(lightIndex, IN.positionWS);
                    half nl = saturate(dot(N, l.direction)) * 0.75h + 0.25h;
                    half atten = l.distanceAttenuation * l.shadowAttenuation;
                    col += albedo * l.color * (nl * atten);
                LIGHT_LOOP_END
#endif

                // 边缘光：把角色从背景里抠出来
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                half rim = pow(saturate(1.0h - saturate(dot(N, V))), _RimPower) * _RimColor.a;
                col += _RimColor.rgb * rim;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────────── ④ 投影（ShadowCaster）★ 后补 ★
        //
        // ★ 为什么必须有这个 Pass ★
        // URP 生成阴影贴图时，只会把**声明了 `LightMode=ShadowCaster` 的 Pass** 画进去。
        // 一个 shader 没有这个 Pass ⇒ 物体**被照亮但不投影**，而且**不会报错、不会有警告**。
        //
        // 本工程所有角色 / 敌人 / 植物 / 装饰 / 建筑都经 `ToonLook.Apply` 换成
        // `Seedfall/Toon` 或 `Seedfall/ToonSimple` ⇒ 补上它之前，**整场一个影子都没有**。
        // 这才是"敌人阴影缺失"的真正原因 ✗ —— `NightLighting.cs` 里那句
        // "把 shadowDistance 调到 140 就有影子了"的推断方向是错的：
        // `QualitySettings.shadowDistance` 在 URP 下**根本不参与**（URP 读 RPAsset 自己的值），
        // 而 RPAsset 的值再大也没用——没有 ShadowCaster Pass 的物体压根不进阴影贴图。
        //
        // ★ 顶点处理必须与 `UniversalForward` 逐位一致 ★
        // 若主 Pass 做了顶点变形（风摆 / 膨胀 / 溶解）而这里没做 ⇒ 影子与模型**错位**。
        // 本 shader 的 forward Pass 没有任何顶点变形（只有 `GetVertexPositionInputs`），
        // 所以直接复用 URP 官方的 `ShadowCasterPass.hlsl` 就是一致的 ✓
        // （将来若给主 Pass 加了变形，**这里必须同步加同一段变形**）
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0          // 只写深度：阴影贴图不需要颜色
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            // 方向光与点光/聚光的法线偏移公式不同，URP 用这个关键字区分
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ────────────────────────────────── ⑤ 仅深度（DepthOnly）
        // 不声明它时，`m_RequireDepthTexture` 打开、或某个 Renderer Feature 请求 depth prepass 时，
        // 本 shader 的物体在深度图里会**缺一块**（表现为后处理/深度雾在那里读到天空深度）。
        // 顶点同样是零变形，所以与 forward 一致 ✓
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // ────────────────────────────────── ⑥ 深度+法线（给屏幕空间描边 / SSAO 用）
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            // ★ 法线编码必须跟 URP 走同一个关键字 ★
            // URP 17 在 Forward 下会把法线缓冲标记为 _GBUFFER_NORMALS_OCT（八面体压成 2 通道）。
            // 旧版这里恒输出 `normal*0.5+0.5` 的原始法线 ✗ ⇒
            //   SSAO（`PC_Renderer` 里 Source=DepthNormals）解出来的法线是**错的**，
            //   于是"屏幕空间环境光遮蔽"在本工程基本等于没生效。
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                float4 _NeonColor;
                // ★ v214：贴图的 tiling/offset 必须进 `UnityPerMaterial` ★
                // 这是 SRP Batcher 的硬要求：一个 shader 的**所有** Pass 必须声明
                // 布局逐字节相同的 `UnityPerMaterial`，否则整个 shader 掉出批处理
                // （不掉功能、不报错，只是 draw call 悄悄涨上去）。
                float4 _MainTex_ST;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _OutlinePixels;
                float  _NeonWidth;
                float  _NeonPixels;
                float  _NeonFade;
                float  _OutlineDepthFade;
                float  _AmbientStrength;
                float  _AOStrength;
                float  _TexMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings DNVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DNFrag(Varyings IN) : SV_Target
            {
                // 与 URP 官方 DepthNormalsPass.hlsl 完全一致的两种编码
                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 nWS = normalize(IN.normalWS);
                    float2 octNormalWS = PackNormalOctQuadEncode(nWS);        // [-1, 1]
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remappedOctNormalWS), 0.0h);
                #else
                    return half4(normalize(IN.normalWS) * 0.5h + 0.5h, 0.0h);
                #endif
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
