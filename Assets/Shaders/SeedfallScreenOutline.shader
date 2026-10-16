// Seedfall 守园者 · 屏幕空间描边（手绘后期）
// ------------------------------------------------------------------
// 和着色器里的"反向外壳描边"是互补的两件事：
//   反向外壳：贴着模型长墨线，粗细随物体尺寸，硬边处会断开（立方体/岩石的棱角）
//   屏幕空间：在整张画面上找"深度或法线突变"，因此**任何物体边界都连续**，包括棱角、
//             物体与物体之间的交叠——这才是参考美术里那种"整幅画面盖了一层墨线"的观感
//
// 用 URP 的 FullScreenPassRendererFeature 驱动（不需要自己写 Renderer Feature 的图形管线接线）：
// 顶点用 Blit.hlsl 提供的 Vert，画面来源是 _BlitTexture。
Shader "Seedfall/ScreenOutline"
{
    Properties
    {
        _OutlineColor ("描边颜色", Color) = (0.14,0.11,0.09,1)
        _DepthThreshold ("深度阈值", Range(0.002,0.20)) = 0.045
        _NormalThreshold ("法线阈值", Range(0.05,1.5)) = 0.42
        _Thickness ("描边宽度(像素)", Range(0.5,4.0)) = 1.25
        _FadeStart ("淡出起点(米)", Range(1,60)) = 14
        _FadeEnd ("完全淡出(米)", Range(2,80)) = 34
        _Strength ("强度", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ScreenOutline"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float  _DepthThreshold;
            float  _NormalThreshold;
            float  _Thickness;
            float  _FadeStart;
            float  _FadeEnd;
            float  _Strength;

            // 取某点的"线性视深"与"法线"；深度用线性值，否则远近阈值无法统一
            void SampleAt(float2 uv, out float depth, out float3 normal)
            {
                float raw = SampleSceneDepth(uv);
                depth = LinearEyeDepth(raw, _ZBufferParams);
                normal = SampleSceneNormals(uv);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 uv = IN.texcoord;
                float2 texel = _ScreenParams.zw - 1.0;      // (1/宽, 1/高)
                texel *= _Thickness;

                float3 src = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // Roberts 十字：四个对角采样
                float2 o1 = float2( texel.x,  texel.y);
                float2 o2 = float2(-texel.x,  texel.y);
                float2 o3 = float2( texel.x, -texel.y);
                float2 o4 = float2(-texel.x, -texel.y);

                float d1, d2, d3, d4;
                float3 n1, n2, n3, n4;
                SampleAt(uv + o1, d1, n1);
                SampleAt(uv + o2, d2, n2);
                SampleAt(uv + o3, d3, n3);
                SampleAt(uv + o4, d4, n4);

                float dC;
                float3 nC;
                SampleAt(uv, dC, nC);

                // 深度边缘：除以中心深度做距离归一，远景才不会满屏线
                float depthDiff = abs(d1 + d4 - d2 - d3);
                float depthEdge = smoothstep(_DepthThreshold * dC,
                                             _DepthThreshold * dC * 2.2, depthDiff);

                // 法线边缘
                float3 nDiff = abs(n1 + n4 - n2 - n3);
                float normalEdge = smoothstep(_NormalThreshold,
                                              _NormalThreshold * 1.8,
                                              (nDiff.x + nDiff.y + nDiff.z));

                float edge = saturate(max(depthEdge, normalEdge));

                // 天空（深度=无限远）不画线，否则地平线会糊成一条带
                edge *= step(dC, _ProjectionParams.z * 0.99);

                // 远景淡出：近处墨线清楚，远处收敛，画面才有层次
                float fade = 1.0 - smoothstep(_FadeStart, _FadeEnd, dC);
                edge *= fade * _Strength;

                float3 col = lerp(src, _OutlineColor.rgb, edge);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
