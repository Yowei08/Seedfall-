// ★ 登录鸟瞰的「云雾层」着色器（美术 lane：Shaders/）★
//
// 用途：半透明白/青的云幕，铺在主城上方，缓慢漂移 ⇒「层云之上看主城，朦胧，若隐若现」。
//
// 为什么不用 URP/Unlit + 运行时改材质：那要猜 `_Surface/_Blend/_ZWrite` 这套属性名，
// 换个 URP 小版本就可能静默失效（不报错、只是不透明）。这里自己写一遍，行为确定。
//
// 三条柔化（缺一条就会露出「一片平铺的四边形」）：
//   ① **UV 边缘淡出** `_EdgeFade`：贴图四边渐隐，看不到幕布的直边
//   ② **深度柔化** `_DepthFade`：片元离场景越近越淡 ⇒ 穿透屋顶/树冠时不会切出一条硬缝
//      （依赖 `_CameraDepthTexture`；PC_RPAsset 的 m_RequireDepthTexture = 1 ✓）
//      取不到深度时 sceneZ 为大值 ⇒ 柔化系数 ≈ 1 ⇒ 退化成普通半透明，不会黑屏 ✓
//   ③ **双面** `Cull Off`：从下往上看（俯视相机的极角变化）也不会突然消失
//
// 不做光照：云是「自发光白」的，用 `_Tint` 给每层不同色温即可（青 / 白 / 暖）。
Shader "Seedfall/CloudVeil"
{
    Properties
    {
        _MainTex ("云纹 (alpha = 云量)", 2D) = "white" {}
        _Tint ("色 (rgb = 颜色, a = 总透明度)", Color) = (0.96, 0.98, 1, 1)
        _Alpha ("本层不透明度", Range(0, 1)) = 0.22
        _DepthFade ("深度柔化距离 (米)", Float) = 6
        _EdgeFade ("边缘淡出宽度 (UV)", Range(0.01, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CloudVeil"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Tint;
                float  _Alpha;
                float  _DepthFade;
                float  _EdgeFade;
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
                float  eyeZ       : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.eyeZ = -TransformWorldToView(wp).z;      // 视空间深度（米）
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;

                // ① 边缘淡出
                float2 e = min(i.uv, 1.0 - i.uv);
                float edge = saturate(min(e.x, e.y) / max(_EdgeFade, 1e-4));
                a *= edge;

                // ② 深度柔化（取不到深度 ⇒ 系数≈1，退化成普通半透明）
                float rawZ = SampleSceneDepth(i.positionCS.xy);
                float sceneZ = LinearEyeDepth(rawZ, _ZBufferParams);
                a *= saturate((sceneZ - i.eyeZ) / max(_DepthFade, 1e-4));

                // ③ 本层不透明度
                a *= _Alpha * _Tint.a;

                return half4(_Tint.rgb, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
