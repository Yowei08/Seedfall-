// Seedfall 守园者 · 黑洞动态特效（v214）
// ------------------------------------------------------------------
// 用户原话："战斗开始的传送门记得添加，并附上建模和黑洞的动态特效"
// 建模归另一条线（门框/立柱/拱顶/符文），本文件只管**黑洞本身**。
//
// ★★ 这个着色的全部动画都来自 `_Time` —— **零 CPU 开销、零逐帧脚本** ★★
//   · 没有 Animator、没有 Update 里改材质、没有粒子系统
//   · 三张面片共用**一份**材质，靠 `MaterialPropertyBlock` 的 `_Layer` 分速度
//   ⇒ 场上多一个黑洞 = 多 3 个 draw call，不多一份材质 ✓
//
// ── 四层效果，各自解决一个观感问题 ──
//   ① 旋涡 UV 绕中心转 + 内圈更快（`1/(0.18+r)`）—— 读出"被吸进去"
//   ② 吸积盘亮环（高斯环，半径随时间轻微呼吸）—— 读出"物质在盘上绕"
//   ③ 吸入物质（沿 r 的锯齿波往中心推 + 尖峰化）—— 读出"有东西在掉进去"
//   ④ 核心压暗 + 外圈暗角脉动 —— 黑洞的"黑"必须是真的暗，否则只是个发光圆
//
// ── 为什么不用贴图 ──
// 一张 64 的旋涡图在 3 米直径上是 5 厘米/texel，转起来会看出**贴图的重复与接缝**；
// 而程序化旋涡是**连续**的（角度与半径都是解析函数），任意分辨率下都不露馅 ✓
// 顺带省掉一张资源与一次采样 ✓
//
// ── 混合方式 ──
// `Blend One One`（加色）+ `ZWrite Off` + `Cull Off`：
//   加色让"发光"来自叠加而不是不透明度 ⇒ 与卡通场景的奶油/木色背景叠加时自然
//   Cull Off 让玩家从背面看也是同一个黑洞（传送门通常单面朝向，玩家会绕到侧后面）
Shader "Seedfall/BlackHole"
{
    Properties
    {
        // 三档颜色都是 [HDR]：颜色可以 > 1 ⇒ 加色混合下才有"发光强度"这个自由度
        [HDR] _CoreColor ("核心色（紫）", Color) = (0.42, 0.10, 0.85, 1)
        [HDR] _HotColor ("吸积盘亮部（紫白）", Color) = (0.85, 0.62, 1.00, 1)
        [HDR] _EdgeColor ("边缘色（亮青）", Color) = (0.22, 0.95, 1.00, 1)

        _SwirlSpeed ("旋涡角速度", Range(0,6)) = 1.7
        _InwardSpeed ("向心流速", Range(0,6)) = 1.2
        _Arms ("旋臂数", Range(1,8)) = 3
        _Intensity ("总强度", Range(0,4)) = 1.15
        _Pulse ("呼吸幅度", Range(0,1)) = 0.30
        _RingRadius ("吸积盘半径(UV)", Range(0.2,0.98)) = 0.76
        _Vignette ("外圈暗角", Range(0,1)) = 0.45
        // 逐实例：0/1/2 三层，速度与方向各不相同（由 MaterialPropertyBlock 写，不新建材质）
        _Layer ("层（逐实例 0~2）", Range(0,2)) = 0
        // ★ 暗核开关：只有最内那一层打开 ★（见下面 Core 那个 Pass 的注释）
        _Core ("暗核（逐实例 0/1）", Range(0,1)) = 0
        _CoreColor2 ("暗核颜色", Color) = (0.02,0.01,0.04,1)
        _CoreRadius ("暗核半径(UV)", Range(0.2,0.95)) = 0.62
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "BlackHole"
            Tags { "LightMode"="UniversalForward" }

            Blend One One          // 加色：发光来自叠加
            ZWrite Off
            ZTest LEqual
            Cull Off               // 正反两面都画：玩家会绕到传送门后面
            Lighting Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _HotColor;
                float4 _EdgeColor;
                float4 _CoreColor2;
                float  _SwirlSpeed;
                float  _InwardSpeed;
                float  _Arms;
                float  _Intensity;
                float  _Pulse;
                float  _RingRadius;
                float  _Vignette;
                float  _Layer;
                float  _Core;
                float  _CoreRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // ── 极坐标：把方形面片裁成圆盘 ──
                float2 p = IN.uv * 2.0 - 1.0;
                float  r = length(p);
                if (r > 1.0) discard;              // 圆外不画（面片是方的，圆盘是圆的）
                float  a = atan2(p.y, p.x);
                float  t = _Time.y;

                // ── 层：奇数层反转、各层角速度不同 ⇒ 叠加后读成"多个旋臂在互相追" ──
                float layer = floor(_Layer + 0.5);
                float dir   = (fmod(layer, 2.0) > 0.5) ? -1.0 : 1.0;
                float spd   = _SwirlSpeed * (1.0 + 0.42 * layer) * dir;

                // ① 旋涡：内圈角速度更高（1/(0.18+r)）⇒ 拖出螺旋而不是整体转圈
                float swirl  = spd * t / (0.18 + r);
                // ② 向心流：半径上跑一个随时间前进的相位
                float inflow = _InwardSpeed * t * (1.0 + 0.25 * layer);

                // ③ 旋臂 + 细纹
                float arms = sin(a * _Arms + swirl * 6.2831853 + r * 9.0 - inflow * 2.0);
                float fine = sin(a * (_Arms * 3.0) - swirl * 4.0 + r * 23.0);
                float band = saturate(arms * 0.5 + 0.5) * (0.60 + 0.40 * (fine * 0.5 + 0.5));

                // ④ 吸入的物质：沿半径的锯齿波向内推，尖峰化 ⇒ 一条条"往下掉"的亮痕
                float flow   = frac(r * 3.0 + inflow * 0.85 - a * 0.04);
                float matter = pow(saturate(1.0 - flow), 6.0) * saturate(1.0 - r * 0.9);

                // ⑤ 吸积盘亮环（高斯环，半径随时间轻微呼吸）
                float ringR = _RingRadius + 0.035 * sin(t * 1.7 + layer * 1.1);
                float ring  = saturate(1.0 - abs(r - ringR) / 0.14);
                ring = ring * ring;

                // ⑥ 核心压暗：中心必须是**暗**的，否则读成一个发光盘子而不是黑洞
                float coreMask = smoothstep(0.36, 0.03, r);
                float discMask = smoothstep(1.0, 0.88, r);      // 外沿柔化，避免硬圆边

                // ⑦ 颜色：盘面偏紫白、环与吸入物偏亮青
                float3 col = lerp(_CoreColor.rgb, _HotColor.rgb, saturate(band * 0.9));
                col = lerp(col, _EdgeColor.rgb, saturate(ring * 0.85 + matter * 0.45));
                // 外沿再压一点紫，让边缘的亮青有衬托
                col = lerp(_CoreColor.rgb * 0.9h, col, saturate(discMask));

                float bright = (band * 0.70 + ring * 1.20 + matter * 0.90) * discMask;
                bright *= (1.0 - coreMask * 0.88);              // 核心吃掉大部分亮度
                bright *= 1.0 + _Pulse * sin(t * 2.3 - layer * 0.9);
                // 三层叠加会互相加亮 ⇒ 每层按层号压一点，避免中心糊成一个白饼
                bright *= 1.0 - 0.18 * layer;
                bright *= _Intensity * (1.0 - _Vignette * r * 0.55);

                return half4(col * bright, 1.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────── ② 暗核（**真的黑**）★ 必须单独一个 Pass ★
        //
        // ── 为什么加色 Pass 不够 ──
        // 加色混合只能**变亮**：黑洞的中心压在传送门那块很亮的青色门面上时，
        // 中心永远是青的 ⇒ 读成"一个发光圆盘"，读不成"洞" ✗
        // 实测就是这么翻的：第一版只有加色 Pass，截图里中心是一片亮青。
        //
        // ── 所以这一层用普通 alpha 混合把中心**压黑** ──
        // `Blend SrcAlpha OneMinusSrcAlpha` + 近黑的紫色 ⇒ 中心真的暗下去，
        // 而外面那圈亮度仍在 ⇒ 才有"黑心 + 亮环"的黑洞读数 ✓
        //
        // ★ 三层面片各自的 `_CoreRadius` 由 C# 按"**同一个世界半径**"换算 ★
        // 否则三个 Pass 画出三个不同大小的黑饼，叠加后边缘会出现台阶。
        // 现在三层的暗核在世界上**完全重合**（都是直径 1.56 米）⇒ 不依赖绘制顺序 ✓
        Pass
        {
            Name "BlackHoleCore"
            // ★★ 必须是 `SRPDefaultUnlit`，不能也是 `UniversalForward` ★★
            // 实测：两个 Pass 都写 `UniversalForward` 时，URP **只画第一个匹配的** ⇒
            // 暗核那一层根本不渲染，黑洞中心还是亮的（截图里是一团白）✗
            // `SeedfallToon.shader` 的墨线/霓虹 Pass 也是这么分开的（它们用 SRPDefaultUnlit）✓
            Tags { "LightMode"="SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Lighting Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCore
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _HotColor;
                float4 _EdgeColor;
                float4 _CoreColor2;
                float  _SwirlSpeed;
                float  _InwardSpeed;
                float  _Arms;
                float  _Intensity;
                float  _Pulse;
                float  _RingRadius;
                float  _Vignette;
                float  _Layer;
                float  _Core;
                float  _CoreRadius;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 FragCore(Varyings IN) : SV_Target
            {
                // `_Core` 为 0 的层直接不画（片元级 clip，代价可忽略：三层都只是 1 个 quad）
                clip(_Core - 0.5h);

                float2 p = IN.uv * 2.0 - 1.0;
                float  r = length(p);

                // 暗核：内部近乎全黑，边缘 0.55~1.0 倍半径处柔化消失
                float a = saturate(smoothstep(_CoreRadius, _CoreRadius * 0.55, r));
                // 再叠一点点脉动，避免"一个完全静止的黑饼"
                a *= 0.97 + 0.02 * sin(_Time.y * 2.3);

                return half4(_CoreColor2.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
