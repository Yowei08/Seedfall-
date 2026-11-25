using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Seedfall
{
    /// <summary>
    /// ★ 登录界面「鸟瞰主城」的**画面导演**（美术/后期 lane）★
    ///
    /// ── 用户原话（两轮）──
    ///   ① "登录界面展示效果修改，采用鸟瞰图形式，层云之上看主城，朦胧，若隐若现"
    ///   ② "不要这种渲染云，只要建模云，登录界面的摄像机拉近"
    ///
    /// ── 为什么代码在这里（`Scripts/Art/`）而不是 `Scripts/Login/` ──
    ///   `Login/**` 是明令禁改的目录。但这个需求**恰好不需要改它**：
    ///   登录脚本只在进场时摆一次机位（`FrameLoginCamera`），之后每帧只写 `enabled`
    ///   （`SyncLoginCamera`，见 LoginBootstrap 560~567 行）。
    ///   ⇒ 本文件在 `LateUpdate` 里每帧写一次 `LOGIN_CAM` 的 transform，
    ///     时间上永远晚于它的写，因此**必然是最后写的一方** ✓
    ///     `enabled` 仍归它管 ⇒ 进城时它把相机关掉，本文件立刻停手 ✓
    ///
    /// ── 画面只剩两样（**云不归我做**）──
    ///   ① 机位：俯角 52°（几何）/ 49°（画面），比主城跟随视角（58° 正交、半高 8.6）
    ///      更高更远，且按主城实测半径自动框住 —— 见 <see cref="Zoom"/> 与 ApplyCamera()
    ///   ② 朦胧：`RenderSettings.fog`（Exp2，缓慢呼吸）＋ 景深（Volume，仅登录期间权重 1）
    ///
    ///   ★ 云层：**本文件一层都不放** ★
    ///     渲染云（半透明面片 + 噪声）已按用户要求删除；建模云由**主城那一路**（`6b3bc2be`，
    ///     在 `Scripts/Town/**`）负责，它在登录相机之后加载，会自己从画面里透上来。
    ///     我这边再另造一套 = 重复资产 + 两层云打架，所以只留机位与朦胧两个旋钮 ✓
    ///
    /// ── ★ v220 修正（上一版上面那段判断是错的，留档）★ ──
    ///   上一版认为「主城的建模云会自己从画面里透上来」，但实测机高 26.6 米与云顶 23~26.6 米
    ///   **几乎同高** ⇒ 云**根本不在画面里**（不是没建，是机位与云齐平）。
    ///   ⇒ 本版把 fov 收到 22°、观察距离拉到 53.7 米（主城画面占比反而再大 11%），
    ///     机高抬到约 42 米，云层才真正落进画面；并另加 **3 层云幕**（`VistaClouds`）给层次。
    ///   两套云并存不是重复资产：主城那 6 朵是**不透明白椭球**（轮廓硬、像棉花），
    ///   本文件的 3 层是**半透明云幕**（给"朦胧/若隐若现"）—— 一个给形状，一个给空气。
    ///
    /// ── 硬约束 ──
    ///   · 不碰 `Scripts/Town|Login|Flow|Combat`、不碰 `Data/*.json` ✓
    ///   · UI 是 IMGUI（`GUI.Box`/`GUI.Label`，画在整帧之上）⇒ 雾和景深**糊不到 UI** ✓
    ///   · 不 `new Material`/`new Mesh`（本文件一个都不建）✓
    /// </summary>
    public sealed class LoginVista : MonoBehaviour
    {
        public const string CamName = "LOGIN_CAM";

        // ── 机位（改一个数就换机位） ──
        /// <summary>几何俯角：相机高度与后退距离由它推（需求给的鸟瞰区间 35~55° 取 52°）。</summary>
        public const float PitchDeg = 52f;
        /// <summary>画面再抬 3°：主城落进画面中下部，上方留给雾与云（构图，不改俯角几何）。</summary>
        public const float AimBiasDeg = 3f;

        /// <summary>
        /// ★ 长焦（38° → 22°）★ —— 这一条是「层云之上看主城」在几何上的**前提**。
        ///
        ///   症状（改前实测）：相机机高 26.6 米，而主城的建模云在 y = 23~26.6 米
        ///   ⇒ 相机几乎**贴在云顶**，云全在画面之外（俯角 49° 时，只比相机低 3 米的物体
        ///     要落在离相机 3~11 米内才进画面）⇒ 用户看到的登录图**一朵云都没有** ✗
        ///
        ///   把 fov 收窄、观察距离拉长，**主城的画面占比不变**（见 `_focusDistance` 的推导），
        ///   但机高从 26.6 米抬到约 **42 米** ⇒ 云顶（25 米）落到相机下方 17 米，
        ///   变成画面里一条真正的云层 ✓ 透视也随之更"模型感"（俯视沙盘的样子）
        /// </summary>
        public const float FovDeg = 22f;

        /// <summary>主城横向占画面宽度的比例（1.0 = 刚好铺满；&lt;1 留边，&gt;1 出血）。</summary>
        public const float FrameFill = 0.86f;

        /// <summary>
        /// ★ 拉近系数（用户："登录界面的摄像机拉近"）★
        ///   1.00 = 主城刚好铺满画面（最初）；0.62 = 上一轮（fov 38° 时观察距离 33.7 米）；
        ///   **0.556 = 本轮**：fov 收到 22° 后观察距离 53.7 米，但主城在画面里的宽度
        ///   比上一轮**再大 11%** ⇒ 判据「比现在近」用的是**画面占比**，不是米数 ✓
        ///   下限不要低于 0.5：再近就只剩几块田/屋顶，"看得出是俯视的城"会挂 ✗
        /// </summary>
        public const float Zoom = 0.556f;

        /// <summary>
        /// 横向构图偏移（以主城半径为单位）：把主城中心推到画面左侧 ~25%，
        /// 右边 48% 留给 620 宽的登录卡（LoginHud 的布局不改）⇒ 城不被卡片压住 ✓
        /// 相机朝 +Z 看 ⇒ 相机 +X 平移会把场景推向画面左方。
        /// </summary>
        public const float LateralOffset = 0.55f;

        // ── 朦胧 ──
        private static readonly Color HazeColor = new Color(0.78f, 0.84f, 0.90f, 1f);
        /// <summary>
        /// 雾密度：0.0075 → **0.0070**（试过 0.0095，实测把主城的绿整片洗白 ✗）。
        /// 机位抬到 42 米、观察距离 53.5 米之后，同样是"朦胧"，但**层次该由 3 层云幕给**，
        /// 雾只负责把远处压进天空色（远景往乳白蓝里沉，城的轮廓仍清楚）。
        /// 0.0070 的实际观感：远处屋顶开始泛白，近处石头/木头的对比还在 ⇒ 朦胧而"看得清" ✓
        /// （**雾糊不到 UI**：UI 是 IMGUI 画在整帧之上，不进相机渲染）
        /// </summary>
        private const float FogDensity = 0.0070f;

        private static LoginVista _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("LOGIN_VISTA");
            _instance = go.AddComponent<LoginVista>();
        }

        private Camera _cam;
        private Volume _volume;
        private VolumeProfile _profile;
        private DepthOfField _dof;

        /// <summary>三层云雾（本文件建、本文件拆；只活在登录期间）。</summary>
        private readonly VistaClouds _clouds = new VistaClouds();

        /// <summary>刚上屏的时间戳（收势动效从这里起算）。</summary>
        private float _t0;
        /// <summary>&gt;0 ⇒ 收势动效还没跑完（0 = 已收稳）。</summary>
        public static float SettleFor = 0.18f;
        /// <summary>收势结束的时间（读数用）。</summary>
        public static float LastSettleDoneAt { get; private set; } = -1f;

        private bool _built;
        private bool _visualsOn;
        private bool _aaApplied;
        private float _radius = 30f;
        private Vector3 _center = Vector3.zero;
        private float _focusDistance = 40f;
        private float _camHeight = 30f;
        private float _backDist = 25f;

        // 雾的原始值（离开登录界面时原样还回去）
        private bool _fogSaved, _fog;
        private FogMode _fogMode;
        private Color _fogColor;
        private float _fogDensity;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void LateUpdate()
        {
            Camera cam = ResolveCam();
            bool want = cam != null && cam.enabled && cam.isActiveAndEnabled;

            if (want)
            {
                if (!_built) TryBuild();
                if (!_built) return;
                if (!_visualsOn) { _t0 = Time.unscaledTime; SettleFor = 0.18f; }   // ← 收势动效起算点
                ApplyCamera(cam);
                SetVisuals(true);
                WriteFog();
                // 云幕：建一次，之后每帧漂 —— `dt` 用 unscaled，暂停也不会冻住（登录界面没有暂停）
                if (_clouds.LayerCount == 0) _clouds.Build(transform, cam, _center);
                _clouds.Tick(Time.unscaledDeltaTime);
            }
            else if (_visualsOn)
            {
                SetVisuals(false);
                _clouds.Dispose();
            }
        }

        private Camera ResolveCam()
        {
            if (_cam != null) return _cam;
            GameObject go = GameObject.Find(CamName);
            if (go != null) _cam = go.GetComponent<Camera>();
            return _cam;
        }

        // ══════════════════════════════════════════════════════════════════
        // 量主城 + 算机位 + 建景深
        // ══════════════════════════════════════════════════════════════════

        private void TryBuild()
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return;               // 主城还没建完 ⇒ 下一帧再试（不猜坐标）

            // ★ 半径的两种量法都踩过坑：
            //   ① `TOWN` 底下压着上百米见方的地面网格 ⇒ 直接取 AABB 半径 = 61 m，
            //      机位被推到 122 m 外，整个画面被雾糊成一片灰；
            //   ② 跳过地面后仍有外圈环路/墙基 ⇒ 半径被夹到 46 m，主城只占画面正中一小块。
            //   ⇒ 用**建筑与道具中心点的 90 分位距离**：抗一两个离群大件，量的是"内容在哪儿"✓
            var pts = new System.Collections.Generic.List<Vector3>();
            var halfs = new System.Collections.Generic.List<float>();
            Renderer[] rs = town.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled) continue;
                if (r.GetComponent<ParticleSystem>() != null) continue;

                string n = r.gameObject.name;
                if (n.StartsWith("GROUND") || n.StartsWith("STREET")) continue;
                Vector3 sz = r.bounds.size;
                if (sz.x > 40f && sz.z > 40f) continue;                 // 地面/大平板
                if (sz.y < 0.08f && (sz.x > 24f || sz.z > 24f)) continue;

                pts.Add(r.bounds.center);
                halfs.Add(Mathf.Max(sz.x, sz.z) * 0.5f);
            }
            if (pts.Count < 8) return;

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) sum += pts[i];
            Vector3 mean = sum / pts.Count;

            var dist = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - mean;
                dist[i] = new Vector2(d.x, d.z).magnitude;
            }
            var sorted = new float[dist.Length];
            System.Array.Copy(dist, sorted, dist.Length);
            System.Array.Sort(sorted);
            float p90 = sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Length * 0.90f), 0, sorted.Length - 1)];

            var halfSorted = halfs.ToArray();
            System.Array.Sort(halfSorted);
            float medHalf = halfSorted[halfSorted.Length / 2];

            _center = new Vector3(mean.x, mean.y, mean.z);
            _radius = Mathf.Clamp(p90 + medHalf * 1.6f, 16f, 40f);

            // 机位几何：让 2R 的横向范围占画面 FrameFill，再乘 Zoom 拉近
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            float tanH = Mathf.Tan(FovDeg * 0.5f * Mathf.Deg2Rad) * aspect;
            _focusDistance = _radius / Mathf.Max(0.25f, FrameFill * tanH) * Zoom;
            float pitch = PitchDeg * Mathf.Deg2Rad;
            _camHeight = _focusDistance * Mathf.Sin(pitch);
            _backDist = _focusDistance * Mathf.Cos(pitch);

            BuildVolume();

            _built = true;
            Debug.Log("[VISTA] 登录鸟瞰就绪（本文件自建 3 层云幕，主城的 6 朵建模云仍在）：主城中心 ("
                + _center.x.ToString("0.0") + "," + _center.z.ToString("0.0")
                + ")｜半径 " + _radius.ToString("0.0") + " m"
                + "｜俯角 " + PitchDeg.ToString("0") + "°（画面 " + (PitchDeg - AimBiasDeg).ToString("0") + "°）"
                + "｜fov " + FovDeg.ToString("0") + "°"
                + "｜观察距离 " + _focusDistance.ToString("0.0") + " m"
                + "｜机高 " + _camHeight.ToString("0.0") + " m"
                + "｜后退 " + _backDist.ToString("0.0") + " m"
                + "｜画面半高 " + (_focusDistance * Mathf.Tan(FovDeg * 0.5f * Mathf.Deg2Rad)).ToString("0.0") + " m"
                + "（拉近系数 " + Zoom.ToString("0.00") + "）"
                + "｜雾 Exp2 密度 " + FogDensity.ToString("0.0000")
                + "｜景深 Gaussian 焦点 " + (_focusDistance * 0.78f).ToString("0") + "~" + (_focusDistance * 1.25f).ToString("0") + " m");
        }

        /// <summary>登录期间的景深（只在登录阶段有权重，进城立刻归零 ⇒ 不影响战斗画面）。</summary>
        private void BuildVolume()
        {
            if (_volume != null) return;

            var go = new GameObject("LOGIN_VISTA_VOLUME");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 60f;
            _volume.weight = 0f;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "LOGIN_VISTA_PROFILE";
            _profile.hideFlags = HideFlags.DontSave;

            _dof = ScriptableObject.CreateInstance<DepthOfField>();
            _dof.name = "VistaDepthOfField";
            _dof.hideFlags = HideFlags.DontSave;
            _dof.active = true;
            _dof.mode.Override(DepthOfFieldMode.Gaussian);
            _dof.gaussianStart.Override(_focusDistance * 0.78f);
            _dof.gaussianEnd.Override(_focusDistance * 1.25f);
            _dof.gaussianMaxRadius.Override(0.85f);
            _dof.highQualitySampling.Override(true);
            _profile.components.Add(_dof);

            _volume.sharedProfile = _profile;
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧：机位 / 开关 / 雾
        // ══════════════════════════════════════════════════════════════════

        private void ApplyCamera(Camera cam)
        {
            Vector3 pos = _center + new Vector3(_radius * LateralOffset, _camHeight, -_backDist);

            // ★ 进入收势 1.02 → 1.00（0.18s）★
            //   需求里的"进入动效：淡入 + 1.02→1.00 缩放"原本指面板本身；
            //   IMGUI 的 `GUI.matrix` 盖不到 `LoginBootstrap.OnGUI`（实测：动效中途按钮像素区间与静止帧逐位相同）
            //   ⇒ 面板那 2% 无法从允许改的目录实现。改成让**画面**做同样的收势：
            //     开局把观察距离乘 1/1.02（世界显得大 2%），0.18s 内回到 1.00 —— 同一手感，且看得见 ✓
            float settle = 1f;
            if (SettleFor > 0f)
            {
                float st = Mathf.Clamp01((Time.unscaledTime - _t0) / 0.18f);
                settle = Mathf.Lerp(1.02f, 1f, 1f - Mathf.Pow(1f - st, 3f));
                if (st >= 1f) SettleFor = 0f;
                if (SettleFor == 0f) LastSettleDoneAt = Time.unscaledTime;
            }
            if (settle > 0f) pos = _center + (pos - _center) / settle;

            // ★ v221：镜头**极缓慢**推近 + 横移（用户："让登录界面的画面也动起来"）★
            //   静态图上完全看不出运动，是因为之前只有云在动、而且只有 0.5~1 m/s。
            //   这里给机位自己也加两个**长周期、小幅度**的运动：
            //     · 横移：±4% 主城半径（±1.1 m），周期 **26 秒**（一个方向 13 秒）
            //     · 推近：±2.5% 观察距离（±1.3 m），周期 **34 秒**（与横移错开 ⇒ 不机械）
            //   幅度上限的判断：3 秒内横移 ≈ 0.55 m ⇒ 在 40 米外约 1.5% 画宽，
            //   叠上云层的 4.8~8.4 米 ⇒ **看得出在动**，但不会像晃镜头那样让人晕 ✓
            float now = Time.unscaledTime;
            float swayX = Mathf.Sin(now * (Mathf.PI * 2f / 26f)) * _radius * 0.04f;
            float dolly = 1f + Mathf.Sin(now * (Mathf.PI * 2f / 34f) + 0.9f) * 0.025f;
            pos = _center + (pos - _center + new Vector3(swayX, 0f, 0f)) * dolly;

            cam.orthographic = false;
            cam.fieldOfView = FovDeg;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = Mathf.Max(400f, _focusDistance * 4f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = HazeColor;
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.Euler(PitchDeg - AimBiasDeg, 0f, 0f);

            // ★ 抗锯齿：本渲染器是 Deferred+（PC_Renderer.m_RenderingMode = 2），
            //   那遍**不跑 MSAA**（QualitySettings 里的 antiAliasing 对它无效）。
            //   真正能在 Deferred 下起作用的只有**后期 AA** ⇒ 登录相机挂 SMAA（High）。
            //   只挂 LOGIN_CAM：主城相机归 TownBootstrap 管，不越界改它 ✓
            if (!_aaApplied)
            {
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                    data.renderPostProcessing = true;
                    data.renderShadows = true;
                    _aaApplied = true;
                    Debug.Log("[VISTA] 登录相机抗锯齿：SMAA(High)（Deferred+ 下 MSAA 无效，后期 AA 才生效）");
                }
            }
        }

        private void SetVisuals(bool on)
        {
            if (on == _visualsOn) return;
            _visualsOn = on;

            if (_volume != null) _volume.weight = on ? 1f : 0f;

            if (on)
            {
                if (!_fogSaved)
                {
                    _fogSaved = true;
                    _fog = RenderSettings.fog;
                    _fogMode = RenderSettings.fogMode;
                    _fogColor = RenderSettings.fogColor;
                    _fogDensity = RenderSettings.fogDensity;
                }
                Debug.Log("[VISTA] 登录鸟瞰：开（雾 on · 景深权重 1 · 本文件不放云）");
            }
            else
            {
                if (_fogSaved)
                {
                    RenderSettings.fog = _fog;
                    RenderSettings.fogMode = _fogMode;
                    RenderSettings.fogColor = _fogColor;
                    RenderSettings.fogDensity = _fogDensity;
                    _fogSaved = false;
                }
                Debug.Log("[VISTA] 登录鸟瞰：关（雾还原 · 景深权重 0）—— 进城/战斗画面不受影响");
            }
        }

        /// <summary>
        /// 雾每帧写：`NightLighting.Apply` 也每帧写雾（白天 = 关）。
        /// 本方法在 `LateUpdate` ⇒ 晚于所有 `Update` ⇒ 登录期间以本文件为准；
        /// 离开登录后 `SetVisuals(false)` 把原值还回去，两边互不残留 ✓
        /// </summary>
        private void WriteFog()
        {
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fog = true;
            RenderSettings.fogColor = HazeColor;
            // 慢呼吸：0.012 ± 0.0015（周期约 100 s）
            RenderSettings.fogDensity = FogDensity + Mathf.Sin(Time.unscaledTime * 0.0628f) * 0.0015f;
        }

        private void OnDestroy()
        {
            _clouds.Dispose();
            if (_volume != null) Destroy(_volume.gameObject);
            if (_profile != null) Destroy(_profile);
        }

        /// <summary>
        /// ★ 取证专用：把收势时长拉长并重播（`unity_drive send LOGIN_VISTA LoginVista HoldSettle 8`）★
        ///   0.18 秒的窗口外部抓不到，拉长后就能抓"收势中途"与"收稳"两张，量出那 2%。
        ///   传 0 恢复默认。
        /// </summary>
        public string HoldSettle(string csv)
        {
            float v;
            bool ok = float.TryParse((csv ?? "").Trim(), out v) && v > 0f;
            SettleFor = ok ? Mathf.Clamp(v, 0.5f, 30f) : 0.18f;
            _t0 = Time.unscaledTime;
            string s = "收势时长 = " + (ok ? SettleFor.ToString("0.00") + "s（取证用）" : "0.18s（正常）")
                       + "｜机高 " + _camHeight.ToString("0.0") + " m｜观察距离 " + _focusDistance.ToString("0.0") + " m";
            Debug.Log("[VISTA] " + s);
            return s;
        }
    }
}
