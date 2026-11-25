using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
// ⚠️ URP 自己也定义了一个 `ShadowQuality`（`UnityEngine.Rendering.Universal.ShadowQuality`），
//    与 `UnityEngine.ShadowQuality` 同名 ⇒ 两个 using 都在时会 CS0104 二义。
//    这里显式钉死用引擎那个（`QualitySettings.shadows` 的类型），别让它跟着 URP 版本漂。
using ShadowQuality = UnityEngine.ShadowQuality;

namespace Seedfall
{
    /// <summary>画质档（低 / 中 / 高）。数值即存档里的整数。</summary>
    public enum QualityTier
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>一条按键绑定（存档用：context + action + Input System 路径）。</summary>
    [Serializable]
    public sealed class BindingEntry
    {
        public string context = "town";
        public string action = "";
        public string path = "";
    }

    /// <summary>
    /// ★★ 设置数据（可直接 `JsonUtility` 序列化）★★
    /// 字段名就是 JSON 键名 —— 改字段名 = 改存档格式，旧文件会静默丢那一项，别改。
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public int version = 1;

        // ── 音频（0..1，1 = 工程既有基准电平，见 GameAudio.BgmVolume / SfxVolume）──
        public float musicVolume = 1f;
        public float sfxVolume = 1f;

        // ── 画面 ──
        public int qualityTier = (int)QualityTier.Medium;
        public int frameRate = 60;
        public int resolutionWidth = 1600;
        public int resolutionHeight = 900;
        public bool fullScreen;

        // ── 按键（两套上下文：town / combat）──
        public BindingEntry[] bindings = Array.Empty<BindingEntry>();
    }

    /// <summary>
    /// ★★ 设置的读 / 写 / **真正生效**，以及"生效到什么程度"的读数（`Probe`）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、落盘位置（为什么不是 `Assets/Resources/`）
    /// ══════════════════════════════════════════════════════════════════════
    ///   `Application.persistentDataPath/Seedfall/settings.json`
    /// `Assets/Resources/**` 在**打包后是只读的**（打进包体），运行时写不进去 ⇒
    /// 玩家真正的设置只能落在 persistentDataPath。工程里此前没有任何持久化代码（grep 取证），
    /// 所以这个目录是本轮新开的，和存档目录 `Seedfall/saves/` 平级。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、五项设置各自真改了什么（不是"存了个数"）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① 音乐音量 → `GameAudio` 那两个 `AudioSource` 里 **loop=true 的那个** 的 `volume`
    ///      公式：`MasterVolume(1.0) × BgmVolume(0.62) × musicVolume` ⇒ musicVolume=1 时与改动前**逐位相同** ✓
    ///   ② 音效音量 → 同一根对象上 **loop=false 的那个** 的 `volume`
    ///      公式：`MasterVolume(1.0) × SfxVolume(0.85) × sfxVolume` ⇒ sfxVolume=1 时不变 ✓
    ///      （`GameAudio` 建源时就是这么算的，这里只是把"用户系数"乘进去）
    ///   ③ 画质 → `QualitySettings.shadows / shadowDistance / antiAliasing` ＋ URP `renderScale / msaaSampleCount`
    ///   ④ 帧率 → `Application.targetFrameRate`（并 `vSyncCount=0`，否则垂直同步会把 120 压回显示器刷新率）
    ///   ⑤ 分辨率 → `Screen.SetResolution(w, h, Windowed / FullScreenWindow)`
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、**副作用回收**（编辑器里必须做，否则会改到工程资产）
    /// ══════════════════════════════════════════════════════════════════════
    /// `QualitySettings.*` 与 `UniversalRenderPipelineAsset.renderScale` 改的是**内存里的那份设置/资产**。
    /// 编辑器退出 play 模式时不会自动还原 ⇒ 工程里的 `UniversalRenderPipelineAsset` 会被"改成中档"，
    /// 下次打开工程就是另一个画面，而且没人知道是设置面板干的 ✗
    /// ⇒ 所以 <see cref="CaptureOriginals"/> 在第一次改动前把原值存下来，
    ///   <see cref="RestoreOriginals"/> 在 `LoginBootstrap.OnDestroy/OnApplicationQuit` 里还原 ✓
    /// </summary>
    public static class LoginSettings
    {
        public const string Tag = "[Login] ";
        public const string FolderName = "Seedfall";

        /// <summary>`GameAudio` 自建的音频根（名字在 `Audio/GameAudio.cs` 里写死，这里按名找）。</summary>
        public const string AudioRootName = "GameAudio (auto)";

        public static string Dir => Path.Combine(Application.persistentDataPath, FolderName);
        public static string FilePath => Path.Combine(Dir, "settings.json");

        /// <summary>当前生效的设置。**永不返回 null**。</summary>
        public static GameSettings Current { get; private set; } = new GameSettings();

        /// <summary>上一次读/写失败的原文（面板上直接显示它，而不是静默用默认值跑）。</summary>
        public static string LastError { get; private set; }

        /// <summary>最近一次落盘的时间戳（读数用）。</summary>
        public static string LastSavedAt { get; private set; } = "（未保存）";

        // ══════════════════════════════════════════════════════════════════
        // 读 / 写
        // ══════════════════════════════════════════════════════════════════

        /// <summary>从磁盘读设置。文件不存在 ⇒ 用默认值（不算错误）；解析失败 ⇒ 记 `LastError` 后仍用默认值。</summary>
        public static void Load()
        {
            LastError = null;
            if (!File.Exists(FilePath))
            {
                Current = new GameSettings();
                return;
            }
            try
            {
                string text = File.ReadAllText(FilePath);
                GameSettings parsed = JsonUtility.FromJson<GameSettings>(text);
                if (parsed == null) throw new InvalidOperationException("settings.json 解析结果为空");
                Current = Clamp(parsed);
            }
            catch (Exception e)
            {
                LastError = "读 settings.json 失败：" + e.Message;
                Log.Warn(Tag + LastError);
                Current = new GameSettings();
            }
        }

        /// <summary>写回磁盘。返回是否成功（失败时 `LastError` 有原文）。</summary>
        public static bool Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
                LastSavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                LastError = null;
                return true;
            }
            catch (Exception e)
            {
                LastError = "写 settings.json 失败：" + e.Message;
                Log.Error(Tag + LastError);
                return false;
            }
        }

        /// <summary>把外部给的值夹进合法区间（防止手改 JSON 后把音量写成 9.0、帧率写成 -1）。</summary>
        public static GameSettings Clamp(GameSettings s)
        {
            if (s == null) return new GameSettings();
            s.musicVolume = Mathf.Clamp01(s.musicVolume);
            s.sfxVolume = Mathf.Clamp01(s.sfxVolume);
            s.qualityTier = Mathf.Clamp(s.qualityTier, 0, 2);
            s.frameRate = s.frameRate == 30 || s.frameRate == 60 || s.frameRate == 120 ? s.frameRate : 60;
            if (s.resolutionWidth < 640 || s.resolutionHeight < 360)
            {
                s.resolutionWidth = 1600;
                s.resolutionHeight = 900;
            }
            if (s.bindings == null) s.bindings = Array.Empty<BindingEntry>();
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // 应用（"真的生效"就在这里）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>四项一起应用（音量的两项由 <see cref="ApplyAudio"/> 一起做）。</summary>
        public static void ApplyAll()
        {
            ApplyAudio();
            ApplyQuality();
            ApplyFrameRate();
            ApplyResolution();
        }

        /// <summary>音乐 / 音效音量 → `GameAudio` 的两个 `AudioSource`。</summary>
        public static void ApplyAudio()
        {
            GameObject root = null;
            try
            {
                root = GameObject.Find(AudioRootName);
            }
            catch (Exception)
            {
                root = null;
            }
            if (root == null)
            {
                // 还没播过任何声音 ⇒ `GameAudio.EnsureSources` 没跑过，这里不给它建对象（那是它的职责），
                // 只把"设置已记下、待它建源后生效"写进日志，避免"改了音量没反应"变成不可解释现象。
                Log.Info(Tag + "音频源尚未建立（本帧还没有任何声音），音量已记录，起播后即按设置生效");
                return;
            }

            AudioSource[] sources = root.GetComponents<AudioSource>();
            float bgmTarget = Mathf.Clamp01(GameAudio.MasterVolume * GameAudio.BgmVolume * Current.musicVolume);
            float sfxTarget = Mathf.Clamp01(GameAudio.MasterVolume * GameAudio.SfxVolume * Current.sfxVolume);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null) continue;
                sources[i].volume = sources[i].loop ? bgmTarget : sfxTarget;   // loop = BGM（见 EnsureSources 的建法）
            }
        }

        /// <summary>画质档 → 阴影 / 抗锯齿 / URP 渲染缩放 + MSAA。</summary>
        public static void ApplyQuality()
        {
            CaptureOriginals();

            switch ((QualityTier)Current.qualityTier)
            {
                case QualityTier.Low:
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.shadowDistance = 15f;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.pixelLightCount = 1;
                    QualitySettings.lodBias = 0.5f;
                    SetUrpQuality(0.70f, 1);
                    break;
                case QualityTier.High:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowDistance = 80f;
                    QualitySettings.antiAliasing = 4;
                    QualitySettings.pixelLightCount = 8;
                    QualitySettings.lodBias = 2.0f;
                    SetUrpQuality(1.00f, 4);
                    break;
                default:      // Medium
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.shadowDistance = 40f;
                    QualitySettings.antiAliasing = 2;
                    QualitySettings.pixelLightCount = 4;
                    QualitySettings.lodBias = 1.0f;
                    SetUrpQuality(0.85f, 2);
                    break;
            }
        }

        private static void SetUrpQuality(float renderScale, int msaa)
        {
            UniversalRenderPipelineAsset urp = UrpAsset();
            if (urp == null) return;                 // 走的是 Built-in ⇒ 上面那三个 QualitySettings 已经够了
            urp.renderScale = renderScale;
            urp.msaaSampleCount = msaa;
        }

        /// <summary>帧率 → `Application.targetFrameRate`。**必须同时关 vsync**，否则 120 会被垂直同步吃掉。</summary>
        public static void ApplyFrameRate()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = Current.frameRate;
        }

        /// <summary>分辨率 → `Screen.SetResolution`。</summary>
        public static void ApplyResolution()
        {
            Screen.SetResolution(Current.resolutionWidth, Current.resolutionHeight,
                Current.fullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        }

        // ══════════════════════════════════════════════════════════════════
        // 原值保存 / 还原（见类注释第三节）
        // ══════════════════════════════════════════════════════════════════

        private static bool _originCaptured;
        private static ShadowQuality _origShadows;
        private static float _origShadowDistance;
        private static int _origAntiAliasing;
        private static int _origPixelLightCount;
        private static float _origLodBias;
        private static int _origVSync;
        private static int _origTargetFrameRate;
        private static float _origRenderScale = 1f;
        private static int _origMsaa = 1;
        private static bool _origUrpCaptured;

        /// <summary>把"我们马上要覆盖的值"先抄一份。只在第一次调用时抄。</summary>
        public static void CaptureOriginals()
        {
            if (_originCaptured) return;
            _originCaptured = true;
            _origShadows = QualitySettings.shadows;
            _origShadowDistance = QualitySettings.shadowDistance;
            _origAntiAliasing = QualitySettings.antiAliasing;
            _origPixelLightCount = QualitySettings.pixelLightCount;
            _origLodBias = QualitySettings.lodBias;
            _origVSync = QualitySettings.vSyncCount;
            _origTargetFrameRate = Application.targetFrameRate;

            UniversalRenderPipelineAsset urp = UrpAsset();
            if (urp != null)
            {
                _origUrpCaptured = true;
                _origRenderScale = urp.renderScale;
                _origMsaa = urp.msaaSampleCount;
            }
        }

        /// <summary>把原值还回去（退出 play 模式前调一次）。</summary>
        public static void RestoreOriginals()
        {
            if (!_originCaptured) return;
            _originCaptured = false;

            QualitySettings.shadows = _origShadows;
            QualitySettings.shadowDistance = _origShadowDistance;
            QualitySettings.antiAliasing = _origAntiAliasing;
            QualitySettings.pixelLightCount = _origPixelLightCount;
            QualitySettings.lodBias = _origLodBias;
            QualitySettings.vSyncCount = _origVSync;
            Application.targetFrameRate = _origTargetFrameRate;

            if (_origUrpCaptured)
            {
                UniversalRenderPipelineAsset urp = UrpAsset();
                if (urp != null)
                {
                    urp.renderScale = _origRenderScale;
                    urp.msaaSampleCount = _origMsaa;
                }
                _origUrpCaptured = false;
            }
            Log.Info(Tag + "画质 / 帧率原值已还原（工程资产不受设置面板影响）");
        }

        private static UniversalRenderPipelineAsset UrpAsset()
            => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        // ══════════════════════════════════════════════════════════════════
        // 读数（交付里那"五项各自的读数"就是这两段打印出来的）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>音频读数：设置值 + 两个 AudioSource 的实际 volume + 是否在响。</summary>
        public static string ProbeAudio()
        {
            string bgm = "无源", sfx = "无源", playing = "否";
            GameObject root = null;
            try { root = GameObject.Find(AudioRootName); } catch (Exception) { root = null; }
            if (root != null)
            {
                AudioSource[] sources = root.GetComponents<AudioSource>();
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i] == null) continue;
                    if (sources[i].loop)
                    {
                        bgm = sources[i].volume.ToString("0.000");
                        playing = sources[i].isPlaying ? "是（" + (sources[i].clip != null ? sources[i].clip.name : "?") + "）" : "否";
                    }
                    else sfx = sources[i].volume.ToString("0.000");
                }
            }
            return $"音乐音量设置={Current.musicVolume:0.00} → BGM.volume={bgm}｜" +
                   $"音效音量设置={Current.sfxVolume:0.00} → SFX.volume={sfx}｜" +
                   $"AudioListener.volume={AudioListener.volume:0.00}｜BGM 正在播={playing}";
        }

        /// <summary>画质读数：档位 + 四个真的被改掉的引擎值。</summary>
        public static string ProbeQuality()
        {
            UniversalRenderPipelineAsset urp = UrpAsset();
            string urpText = urp == null
                ? "URP=未启用"
                : $"URP.renderScale={urp.renderScale:0.00} MSAA={urp.msaaSampleCount}";
            return $"画质档={TierName(Current.qualityTier)}｜阴影={QualitySettings.shadows}｜" +
                   $"阴影距离={QualitySettings.shadowDistance:0}m｜抗锯齿={QualitySettings.antiAliasing}x｜" +
                   $"灯光数={QualitySettings.pixelLightCount}｜LOD={QualitySettings.lodBias:0.0}｜{urpText}";
        }

        /// <summary>帧率读数：设置值 + 引擎值 + 实测（`1/Time.smoothDeltaTime`，做不得假）。</summary>
        public static string ProbeFrameRate()
        {
            float measured = Time.smoothDeltaTime > 0.0001f ? 1f / Time.smoothDeltaTime : 0f;
            return $"帧率设置={Current.frameRate}｜Application.targetFrameRate={Application.targetFrameRate}" +
                   $"｜vSyncCount={QualitySettings.vSyncCount}｜实测≈{measured:0.0} fps";
        }

        /// <summary>分辨率读数：设置值 + 引擎当前值（编辑器里 Game 视图不吃 SetResolution，见报告"没做到"一节）。</summary>
        public static string ProbeResolution()
        {
            return $"分辨率设置={Current.resolutionWidth}x{Current.resolutionHeight}" +
                   $"（{(Current.fullScreen ? "全屏" : "窗口")}）｜Screen={Screen.width}x{Screen.height}" +
                   $"｜Screen.fullScreenMode={Screen.fullScreenMode}｜显示器={Screen.currentResolution.width}x{Screen.currentResolution.height}";
        }

        public static string TierName(int tier)
        {
            switch ((QualityTier)tier)
            {
                case QualityTier.Low: return "低";
                case QualityTier.High: return "高";
                default: return "中";
            }
        }
    }
}
