using System.Text;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Seedfall
{
    /// <summary>
    /// ★ 光影三条改动的**同机位 A/B 闸门**（美术 lane：`Scripts/Art/`）★
    ///
    /// 为什么要这个文件：交付要求"每条光影改动各带一张**对比图**"。
    /// 如果靠"改资产 → 退出 → 重进 → 再拍"，两次的**机位、云、天气、时间**全都可能不同
    /// ⇒ 图上的差异无法归因到那一条改动（那正是"看着不一样"和"证明是它"的区别）✗
    /// 所以这里把三条改动都做成**运行时可开关的旋钮**，在同一局、同一机位下拍 A/B 两张 ✓
    ///
    /// 三条（改前 = 0 / 改后 = 1）：
    ///   ① `ambient` 环境光：Flat 单色 → Trilight 三色（开关 `SkyAmbientRig.Enabled`）
    ///   ② `ssao`    屏幕空间 AO：`PC_Renderer` 里那条 `ScreenSpaceAmbientOcclusion` 开/关
    ///      （`ScriptableRendererFeature.SetActive`，URP 公开 API）
    ///   ③ `bias`    主光阴影贴地：depthBias / normalBias 回到 0.15 / 0.80（改前）或 0.06 / 0.45（改后）
    ///
    /// 用法（播放中）：
    ///   `unity_drive send GFX_LEVER GfxLever Lever all,0`   ← 全部回到改前
    ///   `unity_drive send GFX_LEVER GfxLever Lever ambient,1`
    ///   `unity_drive send GFX_LEVER GfxLever Lever ssao,1`
    ///   `unity_drive send GFX_LEVER GfxLever Lever bias,1`
    ///   `unity_drive send GFX_LEVER GfxLever State x`       ← 打印当前三个旋钮的实际值
    /// </summary>
    public sealed class GfxLever : MonoBehaviour
    {
        /// <summary>改前的主光阴影偏移（URP 资产原值）。</summary>
        public const float BiasDepthBefore = 0.15f, BiasNormalBefore = 0.80f;
        /// <summary>改后的主光阴影偏移（贴地）。</summary>
        public const float BiasDepthAfter = 0.06f, BiasNormalAfter = 0.45f;

        private static GfxLever _instance;

        private ScriptableRendererFeature _ssao;
        private static bool _ambientOn = true;
        private static bool _ssaoOn = true;
        private static bool _biasOn = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("GFX_LEVER");
            _instance = go.AddComponent<GfxLever>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>一次调用切一个旋钮：<c>"ambient,1"</c> / <c>"ssao,0"</c> / <c>"bias,0"</c> / <c>"all,0"</c>。</summary>
        public string Lever(string csv)
        {
            string[] p = (csv ?? "").Split(',');
            if (p.Length < 2 || !int.TryParse(p[1].Trim(), out int on)) return "需要 \"ambient,1\" 这种写法";
            string key = p[0].Trim().ToLowerInvariant();
            if (key == "all")
            {
                SetAmbient(on != 0);
                SetSsao(on != 0);
                SetBias(on != 0);
            }
            else if (key == "ambient") SetAmbient(on != 0);
            else if (key == "ssao") SetSsao(on != 0);
            else if (key == "bias") SetBias(on != 0);
            else return "未知旋钮：" + key + "（ambient / ssao / bias / all）";

            string s = StateText("切到 " + key + "=" + (on != 0 ? "1（改后）" : "0（改前）"));
            Debug.Log("[LEVER] " + s);
            return s;
        }

        private static void SetAmbient(bool on)
        {
            _ambientOn = on;
            SkyAmbientRig.Enabled = on;
            if (!on) RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        }

        private void SetSsao(bool on)
        {
            _ssaoOn = on;
            if (_ssao == null) _ssao = FindSsao();
            if (_ssao != null) _ssao.SetActive(on);
        }

        private static void SetBias(bool on)
        {
            _biasOn = on;
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            urp.shadowDepthBias = on ? BiasDepthAfter : BiasDepthBefore;
            urp.shadowNormalBias = on ? BiasNormalAfter : BiasNormalBefore;
        }

        private static ScriptableRendererFeature FindSsao()
        {
            var datas = Resources.FindObjectsOfTypeAll<ScriptableRendererData>();
            for (int i = 0; i < datas.Length; i++)
            {
                if (datas[i] == null) continue;
                for (int k = 0; k < datas[i].rendererFeatures.Count; k++)
                {
                    ScriptableRendererFeature f = datas[i].rendererFeatures[k];
                    if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return f;
                }
            }
            return null;
        }

        /// <summary>当前三个旋钮的**实际值**（不是我想设的值 —— 从引擎对象读回来）。</summary>
        public string State(string ignored)
        {
            string s = StateText("读数");
            Debug.Log("[LEVER] " + s);
            return s;
        }

        private string StateText(string head)
        {
            var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (_ssao == null) _ssao = FindSsao();
            var sb = new StringBuilder(240);
            sb.Append(head).Append(" ⇒ ")
              .Append("ambient=").Append(RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Trilight ? "1 三色（改后）" : "0 单色（改前）")
              .Append("｜ssao=").Append(_ssao == null ? "找不到特性" : (_ssao.isActive ? "1 开（改后）" : "0 关（改前）"))
              .Append("｜bias=").Append(urp == null ? "无 URP" :
                    ("depth=" + urp.shadowDepthBias.ToString("0.000") + " normal=" + urp.shadowNormalBias.ToString("0.000")
                     + (_biasOn ? "（改后 贴地）" : "（改前）")));
            return sb.ToString();
        }
    }
}
