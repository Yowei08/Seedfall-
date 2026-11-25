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
        // ★★ v214：这是**整幅画面的墨线**，也是本轮"加入黑色描边"里唯一覆盖
        //    "没有反向外壳的物体"（装饰、草地贴片、建筑积木）的那一层 ★★
        //
        // ── 颜色 (0.14,0.11,0.09) → (0.04,0.04,0.04) ──
        // 旧值是暖褐，叠在奶油底/木色上读作"脏"，不读作"墨"。要的是漫画那种**中性黑**。
        //
        // ── 为什么以前"开了也看不见"（实测读数）──
        //   `_FadeStart = 14`、`_FadeEnd = 34` ⇒ 34 米外 `fade = 0`。
        //   而两个机位都在这个死区之外或边缘：
        //     · 主城跟随档：正交、俯角 58°、距焦点 **34 米** ⇒ fade ≈ 0 ✗
        //     · 战斗机位：相机离地约 38 米、到敌人 20.8 米 ⇒ 只在最近的一圈有 fade
        //   ⇒ 这个 Renderer Feature 一直是 `m_Active: 1`、强度 1，画面上却几乎没有线 ✓
        //   （`PC_Renderer.asset` 里它确实装着，不是没装 —— 所以问题一定在参数上）
        // ⇒ 窗口改成 26 → 130：主城 34 米处 `smoothstep(26,130,34) ≈ 0.08` ⇒ **92% 强度**；
        //    战斗 20.8 米处 ≈ 0 ⇒ **100% 强度**；真正远的远景（>130 米）仍然收敛，
        //    不会出现"整片远景糊成黑"的老问题 ✓
        _OutlineColor ("描边颜色", Color) = (0.04,0.04,0.04,1)
        _DepthThreshold ("深度阈值", Range(0.002,0.20)) = 0.045
        // ★★ v223：法线阈值 **0.42 → 0.68** ★★
        //
        // ── 为什么提高（用户："草地上不该有边，人和敌人要有一道清晰的边"）──
        // 一条边的强度是 `normalEdge = smoothstep(_NormalThreshold, _NormalThreshold*1.8, Σ|Δn|)`，
        // 所以阈值越低 ⇒ 越小的法线起伏也被当成轮廓。
        // 0.42 时：草地叶片、木纹、砖缝这类**表面细节**的法线变化已经越过它
        // ⇒ 同一个轮廓旁边又冒出一圈假边，整幅画面读成"描边被画了两遍" ✗
        // 0.68 时：只剩"物体与物体/背景交界"那种**真正的法线断裂**能过线 ✓
        // ⚠️ 上限：别超过 **0.85**。再高真轮廓也会一起丢（人/敌人身上就没边了）。
        //    实测过渡见 `OutlineProbe.Normal(...)` 的运行期 A/B（同一机位、同一帧内容）。
        _NormalThreshold ("法线阈值", Range(0.05,1.5)) = 0.68
        // 1.25 → 1.7 像素：与外壳描边的 2.2 像素下限配合，
        // 屏幕墨线负责"物体与物体的交界"，外壳负责"物体自己的轮廓" ✓
        // v223：**先不动**（改了阈值再看）—— 阈值的误触面积比线宽的影响大得多 ✓
        _Thickness ("描边宽度(像素)", Range(0.5,6.0)) = 1.7
        _FadeStart ("淡出起点(米)", Range(1,200)) = 26
        _FadeEnd ("完全淡出(米)", Range(2,400)) = 130
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
