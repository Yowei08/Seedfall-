using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Seedfall
{
    /// <summary>
    /// 屏幕空间描边（手绘后期）。
    ///
    /// 与着色器里的反向外壳描边互补：
    ///   · 反向外壳：墨线贴着模型长，粗细随物体尺寸；但立方体/岩石这种硬边会裂开
    ///   · 屏幕空间：在画面上找深度/法线突变，任何边界都连续，整幅画面像盖了一层墨线
    ///
    /// 实现走 Unity 6 的 RenderGraph 路径（URP 17 默认开启 RenderGraph，
    /// 旧的 Execute(ScriptableRenderContext, ref RenderingData) 重载在这个版本下不会被调用）。
    /// </summary>
    public class ScreenOutlineFeature : ScriptableRendererFeature
    {
        [Tooltip("描边强度。0 等于关闭。")]
        [Range(0f, 1f)] public float strength = 1f;

        [Tooltip("勾选后在编辑器里也能看到效果")]
        public bool enableInSceneView = false;

        private Material _material;
        private OutlinePass _pass;

        private const string ShaderName = "Seedfall/ScreenOutline";

        public override void Create()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                _material = null;
                _pass = null;
                return;
            }
            if (_material == null || _material.shader != shader)
                _material = CoreUtils.CreateEngineMaterial(shader);

            _material.SetFloat("_Strength", strength);
            _pass = new OutlinePass(_material)
            {
                // 需要深度与法线：URP 会因此生成 _CameraDepthTexture / _CameraNormalsTexture
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || _material == null) return;

            CameraType type = renderingData.cameraData.cameraType;
            bool ok = type == CameraType.Game || type == CameraType.Preview
                      || (enableInSceneView && type == CameraType.SceneView);
            if (!ok) return;

            _material.SetFloat("_Strength", strength);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass = null;
        }

        // ────────────────────────────────────────────────
        private class OutlinePass : ScriptableRenderPass
        {
            private readonly Material _material;
            private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");

            private class PassData
            {
                public TextureHandle Source;
                public Material Material;
            }

            public OutlinePass(Material material)
            {
                _material = material;
                // 声明依赖：URP 会为本 Pass 准备深度与法线缓冲
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle source = resourceData.activeColorTexture;
                if (!source.IsValid()) return;

                // 目标纹理：复制源描述
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_SeedfallScreenOutline";
                desc.clearBuffer = false;
                desc.msaaSamples = MSAASamples.None;
                TextureHandle dest = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Seedfall Screen Outline", out var passData))
                {
                    passData.Source = source;
                    passData.Material = _material;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.SetRenderAttachment(dest, 0, AccessFlags.Write);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                    {
                        // 全屏 Blit：采样源画面 → 在 shader 里叠墨线 → 写进目标
                        Blitter.BlitTexture(ctx.cmd, data.Source, new Vector4(1f, 1f, 0f, 0f), data.Material, 0);
                    });
                }

                // 把描边后的画面交回管线
                resourceData.cameraColor = dest;
            }
        }
    }
}
