// ★★ v225：云的专用着色器 —— **完全不采样任何贴图** ★★
//
// 为什么必须自己写一个：
//   `Seedfall/ToonSimple` 与 `Universal Render Pipeline/Unlit` 都会采样 `_MainTex`；
//   只要有任何代码（美术那路的 `TownArtDresser` 会给场景渲染物换材质/塞贴图）把一张 64×64 贴上去，
//   它就会显示出来 ⇒ 云变成"黑底 + 白噪点"（用户截图连报三次就是这个）。
//   ⇒ 这个 shader **没有 `_MainTex` 属性、frag 里也没有任何 tex2D** ⇒ 塞什么贴图都不会显示 ✓
//     （判据因此可以是硬读数：强行 `SetTexture("_MainTex", 噪点图)` 画面毫无变化）
//
// 颜色与明暗：
//   · `_Color`     = 云的底色（脚本设 `(0.96,0.97,1.00)`）
//   · `_ShadeDir`  = 固定的光照方向（不依赖引擎 uniform，URP/内置管线都能跑）
//   · `_Ambient`   = 环境亮度乘子（TownLife 按日夜推它 ⇒ 夜里云会一起变暗 ✓）
//   明暗主要靠"多层椭球叠出来的形状"，这个 shader 只补一层柔和的朝向光，不引入任何贴图细节。
//
// 为什么用 `CGPROGRAM`（内置管线写法）而不是 URP 的 HLSL：
//   这个 Pass **显式带 `Tags { "LightMode" = "UniversalForward" }`**（v226 补的，见下），
//   URP 会把它当"前向不透明"那一遍画出来；而 `CGPROGRAM` 不依赖 URP 的 include 路径 ⇒ 换管线也编得过 ✓
//   ⚠️ 曾经这里写着"不带 LightMode 会当 SRPDefaultUnlit 正常渲染"——**那是错的**：
//      本工程渲染器是 `Deferred+`，那种 Pass 根本不被绘制（云会整个不见）。见 Pass 里的说明。
Shader "Seedfall/CloudFlat"
{
    Properties
    {
        _Color ("Base Color", Color) = (0.96, 0.97, 1.0, 1.0)
        _Ambient ("Ambient Multiplier", Float) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            // ★★ v226：**必须写光照模式标签**（这是"云整个不见"的真正原因）★★
            //   本工程 `PC_Renderer.asset` 是 `m_RenderingMode: 2` = **Deferred+**；
            //   在 Deferred+ 下，**没有 `LightMode` 的 Pass 被当成 `SRPDefaultUnlit`，而它根本不被绘制** ——
            //   表现不是"变白"，而是**整朵云不见**。"白"和"不存在"在截图上是两回事，在读数上是同一件事 ✗
            //   本场已用四组实验查过：外扩宽度推到 0.25m 无线 / `ZTest Always` 无线 / 两种标签都试 /
            //   **只有 `UniversalForward` 会画** ✓
            Tags { "LightMode" = "UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Ambient;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.nrm = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 固定光向（不读任何引擎/贴图资源）⇒ 顶面亮、底面暗，云看起来是立体的
                const float3 L = float3(0.354, 0.900, -0.253);
                float ndl = saturate(dot(normalize(i.nrm), L));
                float lit = lerp(0.78, 1.06, ndl) * _Ambient;
                return fixed4(_Color.rgb * lit, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
