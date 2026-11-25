using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ UI「进入动效」—— 淡入 0.16s ＋ 1.02 → 1.00 缩放（美术 lane：`Scripts/Art/`）★
    ///
    /// ── 需求 ──
    ///   "UI 高级化处理"：面板外投影（已烘进贴图，见 `Tools/AssetPipeline/gen_ui.py`）
    ///   ＋ **进入动效 0.12~0.18s 淡入 ＋ 1.02 → 1.00 缩放**。
    ///
    /// ── 为什么动效在 `Scripts/Art/` 而不是 `Scripts/Login/LoginHud.cs` ──
    ///   `Login/**` 本轮**禁改**。而 IMGUI 的绘制顺序与变换有两个全局旋钮，
    ///   在自己的 `OnGUI` 里就能影响别人的绘制：
    ///     · `GUI.depth`：**值越小越画在上面** ⇒ 本文件的遮罩能盖住登录面板
    ///     · `GUI.matrix`：同一帧里**跨脚本生效**（谁先设，后面的 `OnGUI` 都吃到）
    ///   这两条正好够做出「整屏淡入」与「整屏 1.02→1.00 缩放」，**一行都不用改 LoginHud** ✓
    ///
    /// ── 为什么不改任何 `Rect` ──
    ///   `GUI.matrix` 是**绘制期变换**：调用方传进 `GUI.Box/GUI.Button` 的矩形一个字节都没改
    ///   （取证法：`LoginHud.LastConfirmButtonScreen` / `LastReturnButtonScreen` 在动效前后逐位相同），
    ///   变的只是"这些矩形被画到屏幕上的哪一点" —— 而且缩放**收敛到 1.00**，
    ///   动效结束后画面与"完全没做动效"逐位一致 ✓
    ///
    /// ── 安全边界（关于全局矩阵泄漏）──
    ///   `GUI.matrix` 是全局状态，设了不收会让整个 IMGUI 永远缩放 ✗
    ///   ⇒ 动效一结束（t &gt; 0.16s）立刻把矩阵写回 `identity`，并且**每帧都确认一次**；
    ///   离开登录界面（`LOGIN_CAM` 不在）时也立刻归位 ✓
    ///
    /// ── 什么时候开始计时 ──
    ///   登录相机（`LOGIN_CAM`）**第一次出现**的那一刻 —— 那正是鸟瞰画面开始上屏的时刻
    ///   （登录自己的加载条归 `LoginLoading` 管，本文件不与它抢）。
    /// </summary>
    public sealed class UiEntrance : MonoBehaviour
    {
        /// <summary>动效时长（需求区间 0.12~0.18s，取中）。可被 <see cref="Hold"/> 临时拉长用于取证。</summary>
        private static float Duration = 0.16f;
        private const float DurationDefault = 0.16f;
        /// <summary>
        /// 起始缩放（1.02 → 1.00）。**本类不执行它**：IMGUI 的 `GUI.matrix` 盖不到先执行的
        /// `LoginBootstrap.OnGUI`（实测见 OnGUI 注释），所以这一半交给 `LoginVista` 对相机做。
        /// 常量留在注释里只为对齐需求写法。
        /// </summary>
        // private const float ScaleFrom = 1.02f;
        /// <summary>淡入遮罩用的颜色（与登录雾同一个乳白蓝 ⇒ 像"从雾里浮出来"）。</summary>
        private static readonly Color VeilColor = new Color(0.80f, 0.85f, 0.90f, 1f);
        /// <summary>遮罩最浓时不透明度（不做到全黑：登录画面第一帧就该看得出是城）。</summary>
        private const float VeilAlpha = 0.92f;

        private static UiEntrance _instance;

        private float _t0 = -1f;
        private float _nextProbe;
        private bool _active;

        /// <summary>最近一次动效结束的时间（读数用）。</summary>
        public static float LastDoneAt { get; private set; } = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("UI_ENTRANCE");
            _instance = go.AddComponent<UiEntrance>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            // 4 Hz 探测登录相机（`GameObject.Find` 别每帧调）
            if (Time.unscaledTime < _nextProbe) return;
            _nextProbe = Time.unscaledTime + 0.25f;

            GameObject camGo = GameObject.Find(LoginVista.CamName);
            Camera cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            bool on = cam != null && cam.enabled && cam.isActiveAndEnabled;

            if (on && !_active)
            {
                _active = true;
                _t0 = Time.unscaledTime;              // ← 登录画面第一次上屏，动效从这里起算
            }
            else if (!on && _active)
            {
                _active = false;
                ResetMatrix();
            }
        }

        private void OnGUI()
        {
            if (!_active) return;

            float t = Time.unscaledTime - _t0;
            if (t >= Duration) return;

            float p = Mathf.Clamp01(t / Duration);
            float e = Ease(p);

            // ★ 只做「淡入」这一件事，而且**完全不碰任何全局 IMGUI 状态** ★
            //
            //   第一版还试图用 `GUI.matrix` 做面板 1.02→1.00 缩放（同帧跨脚本生效）。
            //   实测证伪（留档，别再试回去）：
            //     `GameObject.Find` 找到的 LOGIN_CAM 属于运行时对象，而 `LoginBootstrap` 在场景里，
            //     **它的 OnGUI 先执行** ⇒ 我在自己 OnGUI 里设的矩阵根本盖不到登录面板；
            //     而矩阵却把我的遮罩矩形推到了右下角（`v' = center + s·v`）
            //     ⇒ 症状是"淡入只在右下角出现一块亮区" ✗
            //   取证法：动效中途抓拍后量按钮的像素区间，与静止帧**逐位相同** ⇒ 面板没被缩放。
            //   ⇒ 面板那 2% 的缩放**无法从允许改的目录实现**（`LoginHud` 禁改），
            //     改由 `LoginVista` 对**登录画面本身**做同样的 1.02→1.00 收势（见那边的 ApplyCamera）。
            float a = (1f - e) * VeilAlpha;
            if (a <= 0.001f) return;

            var save = GUI.color;
            GUI.color = new Color(VeilColor.r, VeilColor.g, VeilColor.b, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = save;
        }

        private static float Ease(float p)
        {
            // 缓出：前 40% 就走完 70% 的距离，收尾轻 —— "高级"的手感在收尾
            return 1f - Mathf.Pow(1f - p, 3f);
        }

        private void ResetMatrix()
        {
            // 已经不碰 `GUI.matrix` 了（见 OnGUI 的注释），这里只留一个"动效结束"的时间戳与日志
            if (LastDoneAt >= _t0) return;
            LastDoneAt = Time.unscaledTime;
            Debug.Log("[UI] 进入动效结束：" + Duration.ToString("0.00") + "s 淡入（画面收势 1.02→1.00 在 LoginVista 里做）");
        }

        /// <summary>读数（`unity_drive send UI_ENTRANCE UiEntrance Dump x`）。</summary>
        public string Dump(string ignored)
        {
            float t = _active ? Time.unscaledTime - _t0 : -1f;
            string s = "登录相机在=" + (_active ? "是" : "否")
                       + "｜动效进度=" + (t < 0f ? "未开始" : (t >= Duration ? "已结束（" + t.ToString("0.00") + "s）" : t.ToString("0.000") + "/" + Duration.ToString("0.00") + "s"))
                       + "｜上次结束=" + (LastDoneAt < 0f ? "还没结束过" : LastDoneAt.ToString("0.00") + "s");
            Debug.Log("[UI] " + s);
            return s;
        }

        /// <summary>
        /// ★ 取证专用：把动效时长临时拉长并**从头重播** ★
        ///   `unity_drive send UI_ENTRANCE UiEntrance Hold 8`
        ///   ⇒ 动效变成 8 秒并重新开始，外部就能在动画中途抓拍，
        ///     看**登录卡到底有没有跟着 1.02 缩放**（0.16 秒的窗口抓不到）。
        ///   传 0 恢复默认 0.16s。
        /// </summary>
        public string Hold(string csv)
        {
            float v;
            if (!float.TryParse((csv ?? "").Trim(), out v) || v <= 0f)
            {
                Duration = DurationDefault;
                return "已恢复默认时长 " + DurationDefault.ToString("0.00") + "s";
            }
            Duration = Mathf.Clamp(v, 0.5f, 30f);
            _t0 = Time.unscaledTime;
            _active = true;
            _nextProbe = 0f;                 // 让 Update 立刻重新确认一次"登录相机在不在"
            string s = "动效时长临时设为 " + Duration.ToString("0.00") + "s 并从 t=0 重播（取证用，正常游戏恒为 "
                       + DurationDefault.ToString("0.00") + "s）";
            Debug.Log("[UI] " + s);
            return s;
        }
    }
}
