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
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ToonSimple"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex SimpleVert
            #pragma fragment SimpleFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _TintMul;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
            };

            Varyings SimpleVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 SimpleFrag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = dot(N, mainLight.direction) * 0.5 + 0.5;
                ndl *= mainLight.shadowAttenuation;

                float edge = smoothstep(_ShadeThreshold - _ShadeSoft,
                                        _ShadeThreshold + _ShadeSoft, ndl);
                half3 albedo = _BaseColor.rgb * lerp(_ShadeColor.rgb, half3(1, 1, 1), edge);
                albedo *= _TintMul.rgb;

                // 主光颜色（含强度）—— 与 SeedfallToon 保持一致，改一处要改两处
                half3 col = albedo * mainLight.color;

                uint addCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < addCount; li++)
                {
                    Light l = GetAdditionalLight(li, IN.positionWS);
                    half nl = saturate(dot(N, l.direction)) * 0.75h + 0.25h;
                    col += albedo * l.color * (nl * l.distanceAttenuation * l.shadowAttenuation);
                }

                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                half rim = pow(saturate(1.0h - saturate(dot(N, V))), _RimPower) * _RimColor.a;
                col += _RimColor.rgb * rim;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
