// Seedfall 守园者 · 战斗特效着色器（v70）
// ------------------------------------------------------------------
// 用途：子弹、激光、挥击弧、爆炸环 —— 一切需要**永远画在最上层**的战斗表现。
//
// 为什么必须单独一个着色器，而不是给 Toon 加个开关：
//   "置顶"是三个设置**同时**成立才有效的，少一个就会在混战里被花草或敌人挡住：
//     ① Tags Queue = Overlay  —— 排在所有不透明物体之后绘制
//     ② ZTest Always          —— 不参与深度测试，前方的几何体挡不住它
//     ③ ZWrite Off            —— 自己不写深度，否则会反过来遮挡后面的特效
//   把它固定成独立着色器，调用方就不可能漏配其中一条。
//
// 代价要说清楚：置顶是**牺牲正确遮挡**换可读性。子弹会穿到近处草丛的前面。
// 对这个俯视混战游戏是对的取舍——看不见子弹比"遮挡不真实"严重得多。
Shader "Seedfall/Vfx"
{
    Properties
    {
        [MainColor] _BaseColor ("主色", Color) = (1,1,1,1)
        [HDR] _RimColor ("霓虹边缘色", Color) = (0,0,0,0)
        _RimPower ("边缘收窄", Range(0.5,8)) = 2.4
        _CoreBoost ("中心提亮", Range(1,4)) = 1.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"          // ① 排在最后
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "VfxOverlay"
            Tags { "LightMode" = "UniversalForward" }
            ZTest Always                 // ② 不测深度
            ZWrite Off                   // ③ 不写深度
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex VfxVert
            #pragma fragment VfxFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float  _RimPower;
                float  _CoreBoost;
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
            };

            Varyings VfxVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 VfxFrag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);

                // 菲涅尔：正对相机的地方最亮（弹体的核心），掠射角形成一圈霓虹边
                half fres = pow(saturate(1.0h - saturate(dot(N, V))), _RimPower);

                // 中心提亮让球体有"发光弹丸"的体积感，而不是一个平色圆片
                half3 core = _BaseColor.rgb * _CoreBoost;
                half3 col = lerp(core, _RimColor.rgb, saturate(fres * _RimColor.a)) + _RimColor.rgb * fres * 0.6h;

                half a = saturate(_BaseColor.a + fres * _RimColor.a);
                return half4(col, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
