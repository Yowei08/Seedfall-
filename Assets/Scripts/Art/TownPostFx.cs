using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Seedfall
{
    /// <summary>
    /// ★ 主城 / 登录的**画面后期总闸**（美术 lane：`Scripts/Art/`）★
    ///
    /// ── 用户原话 ──
    ///   "主城和登录界面饱和度高点，要晴天"
    ///
    /// ── 为什么需要这个文件 ──
    ///   饱和度唯一干净的旋钮是 URP 后处理里的 `ColorAdjustments.saturation`，
    ///   而它**只在开了后处理的相机上才跑**。实测（改前）：
    ///     · 登录相机 `LOGIN_CAM` —— `LoginVista` 自己把 `renderPostProcessing = true` 打开了 ✓
    ///     · 主城相机 `Main Camera` —— 场景里**根本没有** `UniversalAdditionalCameraData` 组件，
    ///       URP 在运行时补一个时它的默认值 `m_RenderPostProcessing = false`
    ///       ⇒ 主城这一路**后处理从来没跑过**（Bloom / Vignette / Tonemapping / 饱和度 全部无效）✗
    ///   ⇒ 所以"给主城提饱和度"必须先补这一件事，否则改 profile 对主城毫无效果。
    ///
    /// ── 为什么不去改场景文件或 `TownBootstrap` ──
    ///   · 往场景 YAML 里手插 `UniversalAdditionalCameraData` 块要编 fileID，改坏就是整画黑屏 ✗
    ///   · `TownBootstrap` 属于主城 lane，不该由后期 lane 动 ⇒ 这里用运行时补齐，**可回退**（删掉本文件即还原）✓
    ///
    /// ── 色彩本身在哪改 ──
    ///   `Assets/Settings/DefaultVolumeProfile.asset` 的 `ColorAdjustments.saturation`（**改前 0 → 改后 +22**）。
    ///   那个 profile 是 `UniversalRenderPipelineGlobalSettings` 里的 **URP 默认 Volume Profile**，
    ///   由 `VolumeManager` 当作"组件默认状态"铺给**所有相机** ⇒ 主城与登录**共用同一个**，
    ///   两边饱和度天然一致 ✓（登录另有一份 `LOGIN_VISTA_PROFILE`，但它只有景深，不碰饱和度）
    ///
    /// ── 硬约束 ──
    ///   · 不碰 `LOGIN_CAM`（归 `LoginVista` 管，它自己会打开后处理）✓
    ///   · 不 `new Material` / 不 `new Mesh` / 不碰共享材质 ✓
    ///   · 已生效后每帧**零开销**（只有相机没了才重新找，最多 4 次/秒）✓
    /// </summary>
    public sealed class TownPostFx : MonoBehaviour
    {
        /// <summary>期望的饱和度（= `DefaultVolumeProfile.asset` 上的值，仅作读数比对，运行时**不写**它）。</summary>
        public const float SaturationTarget = 22f;

        private static TownPostFx _instance;

        private Camera _done;
        private float _nextProbe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("TOWN_POSTFX");
            _instance = go.AddComponent<TownPostFx>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void LateUpdate()
        {
            // 已经挂好、相机也还在 ⇒ 什么都不做（这条是热路径，必须先返回）
            if (_done != null && _done.isActiveAndEnabled) return;

            if (Time.unscaledTime < _nextProbe) return;
            _nextProbe = Time.unscaledTime + 0.25f;

            Camera cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled) return;
            if (cam.gameObject.name == LoginVista.CamName) return;   // 登录相机归 LoginVista

            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;

            bool before = data.renderPostProcessing;
            data.renderPostProcessing = true;
            _done = cam;

            Log.Info("[POSTFX] 主城相机后处理：" + (before ? "本来就是 ON（不该发生，报出来）" : "OFF → ON（改前主城一路没跑过后处理）") +
                     "｜相机 " + cam.gameObject.name +
                     "｜saturation 目标 " + SaturationTarget.ToString("0") + "｜" + Report());
        }

        /// <summary>
        /// 读数（`unity_drive send TOWN_POSTFX TownPostFx ReportNow` 触发，会打进日志）：
        /// 谁在提供 `saturation`、值多少、当前相机是不是真的在跑后期。
        /// </summary>
        public string ReportNow() => Report();

        /// <summary>同上，静态版（`Awake`/`LateUpdate` 内部与自检用）。</summary>
        public static string Report()
        {
            var sb = new StringBuilder(220);

            Camera cam = Camera.main;
            if (cam != null)
            {
                UniversalAdditionalCameraData d = cam.GetUniversalAdditionalCameraData();
                sb.Append("主城相机 ").Append(cam.gameObject.name)
                  .Append(" renderPostProcessing=").Append(d != null && d.renderPostProcessing ? "ON" : "OFF");
            }
            else sb.Append("主城相机 （当前场景没有 MainCamera）");

            VolumeManager vm = VolumeManager.instance;
            if (vm != null)
            {
                sb.Append("｜全局默认 profile：").Append(Describe(vm.globalDefaultProfile));
                sb.Append("｜画质默认 profile：").Append(Describe(vm.qualityDefaultProfile));

                if (vm.isInitialized && vm.stack != null)
                {
                    ColorAdjustments ca = vm.stack.GetComponent<ColorAdjustments>();
                    sb.Append("｜**生效 stack saturation=")
                      .Append(ca != null ? ca.saturation.value.ToString("0.#") : "（无 ColorAdjustments）")
                      .Append("**");
                }
            }

            string s = sb.ToString();
            Log.Info("[POSTFX] " + s);
            return s;
        }

        private static string Describe(VolumeProfile p)
        {
            if (p == null) return "（无）";
            if (p.TryGet(out ColorAdjustments ca))
                return p.name + "[saturation=" + ca.saturation.value.ToString("0.#") +
                       (ca.saturation.overrideState ? " · 覆盖=是]" : " · 覆盖=否]");
            return p.name + "[无 ColorAdjustments]";
        }
    }
}
