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
        _OutlineColor ("描边颜色", Color) = (0.16,0.13,0.11,1)
        _OutlineWidth ("描边粗细(米)", Range(0,0.06)) = 0.013
        _OutlineDepthFade ("描边远景淡出", Range(0,60)) = 22

        // 运行时染色乘数（受击闪白 / 冰冻 / 灼烧 / 血量变暗）。
        // 为什么单独开一个属性而不是改 _BaseColor：MaterialPropertyBlock 是**按渲染器**生效的，
        // 一个角色身上有 6~11 个材质槽，用 MPB 改 _BaseColor 会把所有槽位覆盖成同一个颜色
        // （实测表现：敌人渲染成深色一团）。染色必须是乘数，才能保留各槽位自己的底色。
        _TintMul ("染色乘数", Color) = (1,1,1,1)

        // 霓虹高亮描边：铺在墨线之外，alpha=0 表示关闭。
        // 同样必须独立属性——靠 MPB 逐实例设置，否则改一处会串到所有同类模型上。
        [HDR] _NeonColor ("霓虹描边颜色", Color) = (0,0,0,0)
        _NeonWidth ("霓虹描边额外宽度(米)", Range(0,0.08)) = 0.010
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        // ────────────────────────────────── ① 霓虹高亮描边（最外层）
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
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _NeonWidth;
                float  _OutlineDepthFade;
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
                // v123：霓虹改成**内圈**（只扩 _OutlineWidth）。
                // 它原本是 (_OutlineWidth + _NeonWidth)，也就是套在黑色描边**外面** ——
                // 于是剪影被霓虹撑糊，远看就是一团发光的边，"看不清模型"。
                // 现在黑边负责定义轮廓（在外），霓虹贴着模型当辉光（在内）。
                posWS += nrmWS * _OutlineWidth;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.dist = length(posWS - GetCameraPositionWS());
                return OUT;
            }

            half4 NeonFrag(Varyings IN) : SV_Target
            {
                clip(_NeonColor.a - 0.01h);       // alpha 当作开关
                half fade = saturate(1.0h - IN.dist / max(0.001h, _OutlineDepthFade * 1.6h));
                clip(fade - 0.02h);
                return half4(_NeonColor.rgb * _NeonColor.a * fade, 0.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────────── ② 黑色墨线（反向外壳）
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
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
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _NeonWidth;
                float  _OutlineDepthFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float dist : TEXCOORD0; };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(IN.normalOS));
                // 沿法线外扩：轮廓线就长在模型外面一圈
                // v123：黑色描边改成**最外圈**（扩 _OutlineWidth + _NeonWidth）。
                // 它原本只扩 _OutlineWidth，比霓虹小一圈，于是"轮廓"被压在霓虹里面，
                // 玩家看到的是"发光的一坨"而不是"一个有黑边的角色"——就是反馈里的"描边混乱看不清模型"。
                // 两圈**相邻不重叠**：霓虹占 [R, R+Outline]，黑边占 [R+Outline, R+Outline+Neon]，
                // 所以两者都完整可见，且黑边在外、霓虹在内。
                posWS += nrmWS * (_OutlineWidth + _NeonWidth);
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                float4 _NeonColor;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _NeonWidth;
                float  _OutlineDepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
            };

            Varyings ToonVert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogCoord = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            half4 ToonFrag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);

                // 硬边分层光照（两级色块，不做平滑渐变）
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = dot(N, mainLight.direction) * 0.5 + 0.5;   // 半兰伯特：暗部不死黑
                ndl *= mainLight.shadowAttenuation;

                float edge = smoothstep(_ShadeThreshold - _ShadeSoft,
                                        _ShadeThreshold + _ShadeSoft, ndl);
                half3 albedo = _BaseColor.rgb * lerp(_ShadeColor.rgb, half3(1, 1, 1), edge);
                albedo *= _TintMul.rgb;    // 运行时染色（受击 / 冰冻 / 灼烧 / 血量）

                // ★ 乘上主光颜色（含强度）★
                // 旧版这里直接输出 albedo，等于**完全忽略灯光**——把主光调暗画面纹丝不动。
                // 要做"半夜暗光"和"火光/子弹照亮周围"，就必须让灯光真正参与着色。
                half3 col = albedo * mainLight.color;

                // ★ 附加光源：火焰、爆炸、子弹 ★
                // URP 的附加光源数量由管线设置决定（本项目 ≤8），这里逐个累加。
                // 用半兰伯特 + 距离衰减，近处亮、远处自然暗下去。
                uint addCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < addCount; li++)
                {
                    Light l = GetAdditionalLight(li, IN.positionWS);
                    half nl = saturate(dot(N, l.direction)) * 0.75h + 0.25h;
                    half atten = l.distanceAttenuation * l.shadowAttenuation;
                    col += albedo * l.color * (nl * atten);
                }

                // 边缘光：把角色从背景里抠出来
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                half rim = pow(saturate(1.0h - saturate(dot(N, V))), _RimPower) * _RimColor.a;
                col += _RimColor.rgb * rim;

                col = MixFog(col, IN.fogCoord);
                return half4(col, 1.0h);
            }
            ENDHLSL
        }

        // ────────────────────────────────── ④ 深度+法线（给屏幕空间描边备用）
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float4 _RimColor;
                float4 _OutlineColor;
                float4 _TintMul;
                float4 _NeonColor;
                float  _ShadeThreshold;
                float  _ShadeSoft;
                float  _RimPower;
                float  _OutlineWidth;
                float  _NeonWidth;
                float  _OutlineDepthFade;
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
                return half4(normalize(IN.normalWS) * 0.5h + 0.5h, 0.0h);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
