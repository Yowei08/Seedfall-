// Seedfall 守园者 · 轻量卡通着色器（v72）
// ------------------------------------------------------------------
// 只有卡通本体一个 Pass——没有墨线、没有霓虹。给**场景装饰**用。
//
// 为什么要单独一份：
//   场上常驻 400+ 个装饰渲染器（草 170 / 花 90 / 石 14 / 蘑菇 / 芦苇 / 路灯 …）。
//   每个都跑"描边 + 霓虹 + 本体"三个 Pass，实测 drawCall 从 ~800 推到 1948。
//   而这两层对装饰**本来就没意义**：
//     · 每根草都勾一圈黑边，画面会脏（近看是一团黑线）
//     · 装饰不需要"霓虹高亮"这种辨识度手段——那是给敌人和植物用的
//   所以砍掉两个 Pass 是**性能与观感同时受益**，不是妥协。
//
// 与 SeedfallToon 的唯一差别：没有 Outline / Neon 两个 Pass，其余逐行一致。
// 两者必须同步维护——改了 Toon 的明暗逻辑，这里要跟着改。
Shader "Seedfall/ToonSimple"
{
    Properties
    {
        [MainColor] _BaseColor ("亮部颜色", Color) = (1,1,1,1)
        _ShadeColor ("暗部颜色", Color) = (0.62,0.60,0.68,1)
        _ShadeThreshold ("明暗分界", Range(0,1)) = 0.42
        _ShadeSoft ("分界柔和度", Range(0.001,0.5)) = 0.03
        [HDR] _RimColor ("边缘光颜色", Color) = (1,1,1,0.30)
        _RimPower ("边缘光范围", Range(0.5,8)) = 3.2
        _TintMul ("染色乘数", Color) = (1,1,1,1)
        // 与 SeedfallToon 同名同默认值（两处必须同步）
        _AmbientStrength ("环境光参与度", Range(0,2)) = 0.45
        _AOStrength ("屏幕空间AO强度", Range(0,1)) = 0.7

        // ══════════════════════════════════════════════════════════════
        // ★★ v214：**可选**的黑色描边 ＋ 卡通贴图（交付项 ① 与 ②）★★
        // ══════════════════════════════════════════════════════════════
        //
        // ── 为什么现在给装饰加描边，而文件开头那段"装饰不要描边"仍然成立 ──
        // 那段话反对的是"**每一件**装饰都跑描边 Pass"：400+ 渲染器 × 一个 Pass，
        // 实测 draw call 800 → 1948。这个结论**没有变**，所以这里的默认值是
        // **宽度 0**：宽度为 0 时顶点着色器把三角形退化成 w=0，
        // GPU 在裁剪阶段整块丢掉 ⇒ 一分钱都不多花（见 `SimpleOutlineVert`）✓
        //
        // 于是这份 shader 变成了"按需付费"：
        //   · `TownKit.Env` 走 `ToonLook.Get(color, 0f, true)` ⇒ 宽度 0 ⇒ 与改前逐位相同 ✓
        //   · 本轮新种的道具/草（`TownArtDresser`）显式给 0.012~0.02 ⇒ 它们有墨线 ✓
        // 也就是说"要不要描边"从**着色器的属性**变成了**材质的参数**，
        // 而不是把 400 个装饰一并拖进描边开销里 ✓
        _OutlineColor ("描边颜色", Color) = (0.05,0.05,0.05,1)
        _OutlineWidth ("描边粗细(米)", Range(0,0.10)) = 0
        _OutlinePixels ("描边最小屏幕宽度(像素)", Range(0,8)) = 0
        _OutlineDepthFade ("描边远景淡出", Range(0,400)) = 200

        // 卡通贴图：与 `_BaseColor` 乘算，默认 "white" ⇒ 不挂就逐位不变
        _MainTex ("卡通纹理", 2D) = "white" {}
        _TexMode ("贴图坐标模式(0=网格UV 1=世界XZ)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ────────────────────────── ⓿ 黑色墨线（反向外壳 · **按材质选开**）★ v214 ★
        //
        // 默认 `_OutlineWidth = 0` 且 `_OutlinePixels = 0` ⇒ 顶点把三角形退化成 w=0
        // ⇒ 这个 Pass **零成本**（见 Properties 里那段说明）。
        // 数学与 `SeedfallToon.shader` 的 Outline Pass **逐字相同**（含正交/透视两种像素换算），
        // 改一处必须改两处 —— 与本文件其余逻辑同一条纪律。
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex SimpleOutlineVert
            #pragma fragment SimpleOutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                float4 _MainTex_ST;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _OutlinePixels;
                float  _OutlineDepthFade;
                float  _AmbientStrength;
                float  _AOStrength;
                float  _TexMode;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float dist : TEXCOORD0; };

            Varyings SimpleOutlineVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                float  dist  = length(posWS - GetCameraPositionWS());

                // 宽度 = max(米数, 像素数换算成米)；换算式与 Toon / 霓虹两处一致
                float worldPerPixel;
                if (unity_OrthoParams.w > 0.5h)
                    worldPerPixel = 2.0 * unity_OrthoParams.y / max(1.0, _ScreenParams.y);
                else
                    worldPerPixel = 2.0 * dist / max(0.0001, UNITY_MATRIX_P._m11 * _ScreenParams.y);

                float outlineWidth = max(_OutlineWidth, worldPerPixel * _OutlinePixels);
                if (outlineWidth < 0.00001h)
                {
                    // 关闭态：整块在裁剪阶段丢掉，连光栅化都不进
                    OUT.positionCS = float4(0, 0, 0, 0);
                    OUT.dist = 0;
                    return OUT;
                }

                posWS += nrmWS * outlineWidth;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.dist = length(posWS - GetCameraPositionWS());
                return OUT;
            }

            half4 SimpleOutlineFrag(Varyings IN) : SV_Target
            {
                // 远景淡出：与 Toon 同口径（装饰多在小尺度，淡出距离取同一个 200 米即可）
                half fade = saturate(1.0h - IN.dist / max(0.001h, _OutlineDepthFade));
                clip(fade - 0.02h);
                return half4(_OutlineColor.rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ToonSimple"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex SimpleVert
            #pragma fragment SimpleFrag

            // 与 SeedfallToon 的前向 Pass **逐条对齐**（两边必须同步维护）。
            // 装饰占了场上渲染器的绝大多数 ⇒ 这里漏一条关键字 = 整个环境漏一条。
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"

            // v214：贴图在 UnityPerMaterial 之外声明（SRP Batcher 的规矩）
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                // v214：贴图 tiling/offset 必须进 UnityPerMaterial（SRP Batcher 全 Pass 布局一致）
                float4 _MainTex_ST;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _OutlinePixels;
                float  _OutlineDepthFade;
                float  _AmbientStrength;
                float  _AOStrength;
                float  _TexMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
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

            Varyings SimpleVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(p.positionCS.z);
                OUT.uv = IN.uv0;
                return OUT;
            }

            half4 SimpleFrag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float2 ssUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                // 真 N·L（与 SeedfallToon 一致；旧版是 `dot*0.5+0.5` 半兰伯特，
                // 等于把 84% 的球面判成亮部，装饰看起来是"一块平色"）
                float ndl = saturate(dot(N, mainLight.direction));
                float lit = ndl * mainLight.shadowAttenuation;

                float edge = smoothstep(_ShadeThreshold - _ShadeSoft,
                                        _ShadeThreshold + _ShadeSoft, lit);
                half3 albedo = _BaseColor.rgb * lerp(_ShadeColor.rgb, half3(1, 1, 1), edge);

                // v214：卡通贴图（与 `_BaseColor` 乘算；默认 white ⇒ 不挂就逐位不变）
                {
                    float2 uv = (_TexMode > 0.5)
                        ? IN.positionWS.xz * _MainTex_ST.xy
                        : IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                    albedo *= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                }

                albedo *= _TintMul.rgb;

                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(ssUV);

                // 主光颜色（含强度）—— 与 SeedfallToon 保持一致，改一处要改两处
                half3 col = albedo * mainLight.color;
                col *= lerp(1.0h, ao.directAmbientOcclusion, _AOStrength * 0.35h);

                // 环境光（间接光）—— 旧版完全没有，昼夜循环的 ambientLight 影响不到装饰
                half3 ambientSH = SampleSHVertex(N);
                col += albedo * ambientSH * (_AmbientStrength * ao.indirectAmbientOcclusion);

#if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.normalizedScreenSpaceUV = ssUV;
                inputData.positionWS = IN.positionWS;
                uint addCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCount)
                    Light l = GetAdditionalLight(lightIndex, IN.positionWS);
                    half nl = saturate(dot(N, l.direction)) * 0.75h + 0.25h;
                    col += albedo * l.color * (nl * l.distanceAttenuation * l.shadowAttenuation);
                LIGHT_LOOP_END
#endif

                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                half rim = pow(saturate(1.0h - saturate(dot(N, V))), _RimPower) * _RimColor.a;
                col += _RimColor.rgb * rim;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────────── ② 投影（ShadowCaster）★ 后补 ★
        // 与 `SeedfallToon.shader` 同因同解：没有这个 Pass ⇒ **受光但不投影**，且不报错。
        // 装饰占了场上渲染器的绝大多数（草/花/石/蘑菇/芦苇/路灯 400+），
        // 少了这一段，等于"环境中几乎所有东西都没有影子" ✗
        // 顶点零变形，与上面的 body Pass 一致 ✓
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ────────────────────────────────── ③ 仅深度（DepthOnly）
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

        // ────────────────────────────────── ④ 深度+法线（SSAO / 屏幕描边用）
        //
        // ★ 为什么装饰也必须写这个 Pass ★
        // `PC_Renderer` 的 SSAO 用的是 `Source = DepthNormals`（见 `PC_Renderer.asset`）。
        // 一个渲染器没有 DepthNormals Pass ⇒ 它在法线缓冲里是**空的** ⇒
        // 那一片像素的 SSAO 解出错误法线（旧版更糟：ToonSimple 连 Pass 都没有）。
        // 装饰铺满地面，所以这一段缺失会让 SSAO 在**绝大部分屏幕面积**上失效 ✓
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                // v214：贴图 tiling/offset 必须进 UnityPerMaterial（SRP Batcher 全 Pass 布局一致）
                float4 _MainTex_ST;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _OutlinePixels;
                float  _OutlineDepthFade;
                float  _AmbientStrength;
                float  _AOStrength;
                float  _TexMode;
            CBUFFER_END

            struct DNAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct DNVaryings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            DNVaryings DNVert(DNAttributes IN)
            {
                DNVaryings OUT;
                // 与 body Pass 保持逐位一致（这里没有顶点变形）
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DNFrag(DNVaryings IN) : SV_Target
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                    float3 nWS = normalize(IN.normalWS);
                    float2 octNormalWS = PackNormalOctQuadEncode(nWS);
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
