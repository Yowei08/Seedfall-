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

        // ══════════════════════════════════════════════════════════════════
        // ★★ v221：**在这几个场景里不画屏幕描边** ★★
        //
        // ── 用户原话 ──
        //   "战斗模式下的场景被异常施加了两次描边"
        //
        // ── 为什么是"两次"（两条描边同时在跑）──
        //   ① **物体描边** `SeedfallToon.shader` 的反向外壳：`_OutlineColor(0.05,0.05,0.05)`
        //      ＋ `_OutlinePixels = 2.2`（屏幕像素兜底）⇒ 每个模型自己有墨线 ✓
        //   ② **屏幕描边** 本 Feature：`SeedfallScreenOutline.shader` 找深度/法线突变画全屏墨线 ✓
        //   两条互相独立 ⇒ 画面上每条轮廓都有一条物体墨线 + 一条后期墨线 = **两道边** ✓
        //
        // ── 为什么上一轮才开始"看得见" ──
        //   ① 上一轮给 `_OutlinePixels` 补了真值 2.2 像素（旧版 shader 没声明这个属性，一直是死设置）
        //   ② 同一轮把屏幕描边的**淡出窗** `14~34 m → 26~130 m`（为了修"看不见"）
        //      而战斗机位到目标 **20.84 米** —— 旧窗 `smoothstep(14,34,20.84) ≈ 0.36`（只剩 64%），
        //      新窗 `smoothstep(26,130,20.84) = 0` ⇒ **100% 强度**
        //   ⇒ 两条同时到满 ⇒ 用户看到的"两道边" ✓
        //
        // ── 修法：**战斗只用物体描边**（主城照旧两条）──
        //   战斗是俯视/等距（20.84 m）且单位密集：屏幕描边是**后期**，在成群的小体型单位上
        //   会把各自的物体墨线连成一片灰边 ⇒ 既糊又没信息量；
        //   而物体描边在近景分量足（2.2 px 兜底就是按 20.84 米这一档定的，见 `ToonLook`）。
        //   主城机位 34 米、画面元素少、已按"两条叠加"调过 ⇒ 不动它 ✓
        //
        // ── 为什么按**场景名**判，而不是按相机参数 ──
        //   实测（`OutlineAudit` 打出来的读数）：
        //     主城    Main Camera ｜**正交=True**  ｜size 8.60 ｜fov 60 ｜机位 y 22.8
        //     战斗    Main Camera ｜**正交=False** ｜size 7.50 ｜fov 62 ｜机位 y 19.1（透视）
        //   看起来"正交/透视"能区分 —— 但那是**当前调参的结果**，不是契约：
        //   `CameraDef.orthographic` 是个可配字段，哪天战斗改成正交，这条判据就静默失效 ✗
        //   而"我在哪个场景里"是硬事实 ⇒ 按场景名判，改机位不会把它判错 ✓
        //   （名字来自 `TownBootstrap.BattleSceneName = "SeedfallArena"`，即当前打开的战场场景）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>战场场景名（只用来**记录**当前在哪个场景，见下方的 v222 结论）。</summary>
        public const string BattleScene = "SeedfallArena";

        // ══════════════════════════════════════════════════════════════════
        // ★★★ v222 结论：**战斗不再关屏幕描边**（v221 的 `disableInScenes` 已删除）★★★
        //
        // ── 为什么删（实测链条，逐条可复现）──
        //   v221 的判断是"物体描边 + 屏幕描边两条叠加 ⇒ 用户看到两道边"，
        //   于是这一版在战斗里把屏幕描边关掉了。
        //   但 v222 拆开一看：**`SeedfallToon` 的物体描边 Pass 从来没被画过**：
        //     · `PC_Renderer.asset` 的 `m_RenderingMode: 2` = **Deferred+**；
        //     · 该管线只画 `UniversalGBuffer`，没有它的材质落到 forward-only 那一遍；
        //     · 实测那一遍**只画 `UniversalForward`**：
        //         把 Outline 的 `LightMode` 改成 `SRPDefaultUnlit` → 不画；
        //         改成 `UniversalForwardOnly` → 也不画（运行期 `FindPassTagValue` 已确认改到了）；
        //         把 `_OutlineWidth` 推到 0.25 米、`ZTest` 改 `Always` → 还是什么都没有；
        //         而同一材质的**本体** Pass（`UniversalForward`）一直正常 ✓
        //   ⇒ 战斗里**本来就只有一条边**（屏幕描边）。v221 把它关掉 ⇒ 玩家和敌人一条边都没有 ✗
        //   ⇒ 所以"两道边"这个前提不成立；**战斗与主城都画屏幕描边**才是正确状态 ✓
        //
        // ── 那"两道边"的观感从哪来 ──
        //   屏幕描边本身在**表面细节**上会多次触发（`_NormalThreshold` 0.42 偏低，
        //   草地/木纹的起伏也被当成轮廓）⇒ 一条边上又叠了一层，读起来像"画了两遍"。
        //   这一版不动它（不在本轮授权范围内），但把事实写在这里：
        //   若之后用户仍嫌"太粗/像两道"，**正确的旋钮是 `_Thickness` 与 `_NormalThreshold`**，
        //   不是"关掉它"，也不是再动 `disableInScenes` ✓
        // ══════════════════════════════════════════════════════════════════

        /// <summary>当前场景是否画（恒为 `true`；留这个属性是为了让 `OutlineAudit` 的读数格式不变）。</summary>
        public bool ActiveHere => true;

        /// <summary>恒为 `false`（v222 起不再按场景抑制）。</summary>
        public bool SuppressedHere => false;

        /// <summary>入队帧数（验收读数用）。</summary>
        public static long EnqueuedFrames;
        /// <summary>被抑制的帧数 —— v222 起恒为 0（保留字段以免读数格式变化）。</summary>
        public static long SuppressedFrames;

        private string _sceneCache;

        /// <summary>
        /// 只做两件事：记下当前场景名（日志一次）、返回"要不要画"（v222 起恒为 `true`）。
        /// **每帧一次 `GetActiveScene()`**（返回结构体、无分配），只在场景名变化时打一行日志 ✓
        /// </summary>
        private bool DecideForCurrentScene()
        {
            UnityEngine.SceneManagement.Scene sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string nm = sc.name;
            if (nm == _sceneCache) return true;

            string was = _sceneCache;
            _sceneCache = nm;
            Log.Info(Tag + $"屏幕描边：**开**（场景 “{nm}”，上一场景 “{was}”）" +
                          " —— v222：战斗与主城都画，因为物体描边在这个 Deferred+ 渲染器下画不出来");
            return true;
        }

        private const string Tag = "[ScreenOutline] ";

        private Material _material;
        private OutlinePass _pass;

        private const string ShaderName = "Seedfall/ScreenOutline";

        // ══════════════════════════════════════════════════════════════════
        // ★ v223：**法线阈值** —— 草地/木纹上的假边来自它太低，不是线太宽 ★
        //
        // 用户判据（原话）："战斗里草地/木纹上不该有边；人和敌人仍有清晰一道边"。
        // 一条边的强度是 `smoothstep(_NormalThreshold, _NormalThreshold*1.8, Σ|Δn|)`，
        // 阈值越低 ⇒ 越小的法线起伏也判成轮廓。0.42 时草叶/木纹/砖缝全部过线
        // ⇒ 一个轮廓旁边再冒一圈假边，整幅画面读成"描边画了两遍"。
        // 0.68 时只剩真正的法线断裂（物体与物体/背景的交界）✓
        // ⚠️ 别超过 0.85 —— 再高真轮廓一起丢。
        // ★ 这个值的**真值在这里**（显式写进材质），不靠 shader 的 Properties 默认值 ——
        //   与 `ToonLook` 同一条纪律："默认值只在新建材质时生效，将来改 shader 会静默改掉观感" ✓
        // ══════════════════════════════════════════════════════════════════
        public const float DefaultNormalThreshold = 0.68f;

        [Tooltip("法线阈值：越大越只保留真轮廓（草地/木纹上的假边会消失）。建议 0.60~0.72，别超过 0.85")]
        [Range(0.05f, 1.5f)] public float normalThreshold = DefaultNormalThreshold;

        /// <summary>实际生效的法线阈值（序列化被清成 0 时回落到 `DefaultNormalThreshold`）。</summary>
        public float NormalThreshold =>
            normalThreshold > 0.001f ? normalThreshold : DefaultNormalThreshold;

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
            _material.SetFloat("_NormalThreshold", NormalThreshold);
            _pass = new OutlinePass(_material)
            {
                // 需要深度与法线：URP 会因此生成 _CameraDepthTexture / _CameraNormalsTexture
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
            };
        }

        /// <summary>
        /// 验收读数：这个渲染器上的屏幕描边现在什么状态、强度多少、本场景画不画。
        /// 由 `Assets/Editor/OutlineAudit.cs` 在进场景时打印（战场/主城各一次）。
        /// </summary>
        public string Readout()
        {
            bool assetActive = isActive;
            return $"name={name} ｜m_Active(isActive)={assetActive} ｜strength={strength:0.00}" +
                   $" ｜_NormalThreshold={NormalThreshold:0.00}" +
                   $" ｜_Thickness={( _material != null && _material.HasProperty("_Thickness") ? _material.GetFloat("_Thickness").ToString("0.0") + " px" : "—")}" +
                   $" ｜shader={( _material != null ? "已建材质" : "**材质为 null**")}" +
                   $" ｜Scene='{_sceneCache}' ｜本场景={(SuppressedHere ? "**不画（被场景禁用表命中）**" : "画")}" +
                   $" ｜累计 入队 {EnqueuedFrames} 帧 / 抑制 {SuppressedFrames} 帧";
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_pass == null || _material == null) return;

            CameraType type = renderingData.cameraData.cameraType;
            bool ok = type == CameraType.Game || type == CameraType.Preview
                      || (enableInSceneView && type == CameraType.SceneView);
            if (!ok) return;

            // ★ v222：不再按场景抑制 —— 战斗与主城都画（理由见文件上方那段结论）✓
            DecideForCurrentScene();

            _material.SetFloat("_Strength", strength);
            // ★ v223：每帧写一次法线阈值 —— 这样在 Inspector 里拖滑块是**立刻**看得到效果的，
            //   也让 `OutlineProbe.Normal()` 的运行期 A/B 直接生效 ✓
            _material.SetFloat("_NormalThreshold", NormalThreshold);
            renderer.EnqueuePass(_pass);
            EnqueuedFrames++;
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
