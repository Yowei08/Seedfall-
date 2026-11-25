using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ShadowQuality = UnityEngine.ShadowQuality;

namespace Seedfall
{
    /// <summary>
    /// ★ 画质 / 光影的**取证探针**（美术 lane：`Scripts/Art/`）★
    ///
    /// 为什么需要它：交付要求"报**改前/改后逐项数值**"，而编辑器控制台在本机的桥接里读不到行
    /// （`unity_log` 一直报 0 行）⇒ 这里把读数**写进文件** `&lt;工程&gt;/Library/gfxprobe.txt`，
    /// 外部直接读盘，不依赖任何日志通道 ✓
    ///
    /// 读数分四段：
    ///   ① `QualitySettings`：`names[]` 全部档名 ＋ `GetQualityLevel()` ＋ **逐档**的
    ///      shadowDistance / shadowCascades / antiAliasing / vSyncCount / shadowResolution /
    ///      shadows / pixelLightCount / lodBias / anisotropicTextures / realtimeReflectionProbes
    ///      （逐档读数要真去切档 —— 切完必须**切回原档**，否则会改掉用户当前的画质）
    ///   ② URP 资产（`PC_RPAsset`）：renderScale / MSAA / 阴影距离 / 级联数 / 主光阴影图分辨率 /
    ///      depthBias / normalBias / cascadeBorder / 柔性阴影 / 附加光上限
    ///   ③ 渲染器特性（`PC_Renderer`）：`m_RendererFeatures` 的**逐条名字 + 类型 + 是否启用**
    ///   ④ 后处理与光照现状：Volume 栈里的 saturation / bloom / vignette / 色调映射
    ///      ＋ 环境光三色 ＋ 主光 ＋ 登录相机机位 ＋ 云幕
    ///
    /// 用法（播放中）：
    ///   `unity_drive send GFX_PROBE GfxProbe DumpAll x`
    ///   ⇒ 打印返回值，并写 `Library/gfxprobe.txt`
    ///
    /// 只在被调用时才做事，运行时开销 = 0（没有 Update）✓
    /// </summary>
    public sealed class GfxProbe : MonoBehaviour
    {
        private static GfxProbe _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("GFX_PROBE");
            // v251：原来 `AddComponent` 的返回值被丢掉 ⇒ `_instance` 永远为 null（CS0649 + 单例守卫失效）
            //       现在接住它：既消掉警告，也让 `if (_instance != null) return;` 真的起到"只装一次"的作用 ✓
            _instance = go.AddComponent<GfxProbe>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>把全部读数写进 `Library/gfxprobe.txt` 并返回同一份文本。</summary>
        public string DumpAll(string ignored)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("==== Seedfall 画质/光影取证 " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");
            sb.AppendLine();
            SectionQuality(sb);
            SectionUrp(sb);
            SectionRendererFeatures(sb);
            SectionPostAndLight(sb);

            string text = sb.ToString();
            try
            {
                string root = Path.GetDirectoryName(Application.dataPath);
                File.WriteAllText(Path.Combine(root, "Library/gfxprobe.txt"), text);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[GFXPROBE] 写文件失败：" + e.Message);
            }
            Debug.Log("[GFXPROBE]\n" + text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════
        // ① QualitySettings 逐档
        // ══════════════════════════════════════════════════════════════════
        private static void SectionQuality(StringBuilder sb)
        {
            sb.AppendLine("── ① QualitySettings ──");
            sb.Append("names[] = ");
            for (int i = 0; i < QualitySettings.names.Length; i++)
                sb.Append('[').Append(i).Append(']').Append(QualitySettings.names[i]).Append(i == QualitySettings.names.Length - 1 ? "" : " ");
            sb.AppendLine();
            sb.AppendLine("GetQualityLevel() = " + QualitySettings.GetQualityLevel()
                          + "  ⇒ 当前档 = " + QualitySettings.names[QualitySettings.GetQualityLevel()]
                          + "（共 " + QualitySettings.names.Length + " 档，最高档 = names["
                          + (QualitySettings.names.Length - 1) + "] = " + QualitySettings.names[QualitySettings.names.Length - 1] + "）");
            sb.AppendLine("字段                 " + LevelHeader());

            int saved = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                sb.Append("  ").Append(QualitySettings.names[i].PadRight(8))
                  .Append(" shadows=").Append(QualitySettings.shadows.ToString().PadRight(8))
                  .Append(" shadowDistance=").Append(QualitySettings.shadowDistance.ToString("0").PadLeft(5))
                  .Append(" shadowCascades=").Append(QualitySettings.shadowCascades.ToString().PadLeft(2))
                  .Append(" antiAliasing=").Append(QualitySettings.antiAliasing.ToString().PadLeft(2))
                  .Append(" vSync=").Append(QualitySettings.vSyncCount.ToString().PadLeft(2))
                  .Append(" shadowResolution=").Append(QualitySettings.shadowResolution.ToString().PadRight(9))
                  .Append(" pixelLights=").Append(QualitySettings.pixelLightCount.ToString().PadLeft(3))
                  .Append(" lodBias=").Append(QualitySettings.lodBias.ToString("0.0").PadLeft(4))
                  .Append(" anisotropic=").Append(QualitySettings.anisotropicFiltering.ToString().PadRight(11))
                  .Append(" realtimeRefl=").Append(QualitySettings.realtimeReflectionProbes ? "1" : "0")
                  .Append(" RP=").AppendLine(QualitySettings.renderPipeline == null ? "（内置管线）" : QualitySettings.renderPipeline.name);
            }
            QualitySettings.SetQualityLevel(saved, false);
            sb.AppendLine("  （读完已切回原档 " + saved + " = " + QualitySettings.names[saved] + "）");
            sb.AppendLine("  注：URP 下 `shadowDistance / shadowCascades / shadowResolution / antiAliasing` **不生效**，");
            sb.AppendLine("      真正的值在 PC_RPAsset（见②）。这里列出来是为了对得上「画质档」的原始说法。");
            sb.AppendLine();
        }

        private static string LevelHeader()
        {
            return "（逐档实测）";
        }

        // ══════════════════════════════════════════════════════════════════
        // ② URP 资产
        // ══════════════════════════════════════════════════════════════════
        private static void SectionUrp(StringBuilder sb)
        {
            sb.AppendLine("── ② URP 渲染资产（真正生效的阴影/抗锯齿来源）──");
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) { sb.AppendLine("  （不是 URP，或无管线资产）"); return; }

            sb.AppendLine("  资产 = " + urp.name);
            sb.AppendLine("  renderScale = " + urp.renderScale.ToString("0.00") + "｜MSAA = " + urp.msaaSampleCount + "x");
            sb.AppendLine("  shadowDistance = " + urp.shadowDistance.ToString("0") + " m｜shadowCascadeCount = " + urp.shadowCascadeCount
                          + "｜cascadeBorder = " + urp.cascadeBorder.ToString("0.000"));
            sb.AppendLine("  主光阴影图 = " + urp.mainLightShadowmapResolution + " px｜supportsMainLightShadows = " + urp.supportsMainLightShadows
                          + "｜supportsSoftShadows = " + urp.supportsSoftShadows);
            sb.AppendLine("  附加光阴影图 = " + urp.additionalLightsShadowmapResolution + " px｜maxAdditionalLightsCount = " + urp.maxAdditionalLightsCount);
            sb.AppendLine("  shadowDepthBias = " + urp.shadowDepthBias.ToString("0.000")
                          + "｜shadowNormalBias = " + urp.shadowNormalBias.ToString("0.000") + "  ← 越小影子越贴地（本次升级项）");
            sb.AppendLine();
        }

        // ══════════════════════════════════════════════════════════════════
        // ③ 渲染器特性（PC_Renderer.m_RendererFeatures）
        // ══════════════════════════════════════════════════════════════════
        private static void SectionRendererFeatures(StringBuilder sb)
        {
            sb.AppendLine("── ③ 渲染器特性（PC_Renderer.m_RendererFeatures）──");
            var datas = Resources.FindObjectsOfTypeAll<ScriptableRendererData>();
            if (datas == null || datas.Length == 0) { sb.AppendLine("  （没找到 ScriptableRendererData）"); return; }

            for (int i = 0; i < datas.Length; i++)
            {
                ScriptableRendererData d = datas[i];
                if (d == null) continue;
                sb.Append("  ").Append(d.name).Append("：共 ").Append(d.rendererFeatures.Count).Append(" 条 ⇒ ");
                for (int k = 0; k < d.rendererFeatures.Count; k++)
                {
                    ScriptableRendererFeature f = d.rendererFeatures[k];
                    if (k > 0) sb.Append(" ｜ ");
                    sb.Append(f == null ? "(null)" : f.name + " [" + f.GetType().Name + "] active=" + (f.isActive ? "1" : "0"));
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        // ══════════════════════════════════════════════════════════════════
        // ④ 后处理 + 光照 + 登录机位
        // ══════════════════════════════════════════════════════════════════
        private static void SectionPostAndLight(StringBuilder sb)
        {
            sb.AppendLine("── ④ 后处理 / 光照 / 登录机位 ──");

            VolumeManager vm = VolumeManager.instance;
            if (vm != null && vm.isInitialized && vm.stack != null)
            {
                var ca = vm.stack.GetComponent<ColorAdjustments>();
                var bl = vm.stack.GetComponent<Bloom>();
                var vg = vm.stack.GetComponent<Vignette>();
                var tm = vm.stack.GetComponent<Tonemapping>();
                sb.Append("  Volume 栈：saturation=").Append(ca != null ? ca.saturation.value.ToString("0.0") : "无");
                sb.Append("｜contrast=").Append(ca != null ? ca.contrast.value.ToString("0.0") : "无");
                sb.Append("｜postExposure=").Append(ca != null ? ca.postExposure.value.ToString("0.00") : "无");
                sb.Append("｜bloom.intensity=").Append(bl != null ? bl.intensity.value.ToString("0.00") : "无");
                sb.Append("｜vignette.intensity=").Append(vg != null ? vg.intensity.value.ToString("0.00") : "无");
                sb.Append("｜tonemapping=").Append(tm != null ? tm.mode.value.ToString() : "无");
                sb.AppendLine();
            }
            else sb.AppendLine("  Volume 栈：未初始化（相机没开后处理时是这样）");

            sb.Append("  环境光模式=").Append(RenderSettings.ambientMode)
              .Append("｜天顶=").Append(Rgb(RenderSettings.ambientSkyColor))
              .Append(" 地平=").Append(Rgb(RenderSettings.ambientEquatorColor))
              .Append(" 地面=").Append(Rgb(RenderSettings.ambientGroundColor))
              .Append("｜ambientLight(别人写的)=").Append(Rgb(RenderSettings.ambientLight))
              .AppendLine();

            Light sun = RenderSettings.sun;
            sb.Append("  主光(RenderSettings.sun)=").Append(sun == null ? "未绑定" : sun.name)
              .Append("｜强度=").Append(sun != null ? sun.intensity.ToString("0.00") : "-")
              .Append("｜影强=").Append(sun != null ? sun.shadowStrength.ToString("0.00") : "-")
              .AppendLine();
            sb.Append("  雾 on=").Append(RenderSettings.fog).Append(" mode=").Append(RenderSettings.fogMode)
              .Append(" density=").Append(RenderSettings.fogDensity.ToString("0.00000"))
              .Append(" color=").Append(Rgb(RenderSettings.fogColor)).AppendLine();

            GameObject camGo = GameObject.Find(LoginVista.CamName);
            if (camGo != null)
            {
                Camera c = camGo.GetComponent<Camera>();
                if (c != null)
                {
                    Vector3 e = camGo.transform.eulerAngles;
                    sb.Append("  登录相机 ").Append(LoginVista.CamName)
                      .Append("：pos=(").Append(camGo.transform.position.x.ToString("0.0")).Append(',')
                      .Append(camGo.transform.position.y.ToString("0.0")).Append(',')
                      .Append(camGo.transform.position.z.ToString("0.0")).Append(")")
                      .Append("｜俯角=").Append((e.x > 180f ? e.x - 360f : e.x).ToString("0.0")).Append("°")
                      .Append("｜fov=").Append(c.fieldOfView.ToString("0.0")).Append("°")
                      .Append("｜fogDensity=").Append(RenderSettings.fogDensity.ToString("0.00000"))
                      .AppendLine();
                }
            }
            else sb.AppendLine("  登录相机：当前不在登录界面");

            sb.Append("  云幕：").Append(VistaClouds.AssetReport()).Append("｜").AppendLine(VistaClouds.DescribeLayers());
        }

        private static string Rgb(Color c) => "(" + c.r.ToString("0.000") + "," + c.g.ToString("0.000") + "," + c.b.ToString("0.000") + ")";
    }
}
