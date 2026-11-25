using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace Seedfall
{
    /// <summary>
    /// ★★ 登录界面的宿主：**加载主城当背景 → 锁死控摄像机与输入 → 五个按钮 → 存档 / 设置** ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是"另起一个场景 Login.unity + 加法加载 Town"
    /// ══════════════════════════════════════════════════════════════════════
    /// 三个方案，选第三个：
    ///   ① 直接改 `Town.unity`，往里塞一个登录对象 —— 但那个场景**另一路正在用**，
    ///      一次保存就可能把对方尚未提交的改动覆盖掉 ✗
    ///   ② 复制一份 `LoginTown.unity` 摆一个假城 —— 那就是两张地图、两套坐标，必然对不上 ✗
    ///   ③ `Login.unity`（只有 1 个对象）＋ `SceneManager.LoadScene("Town", Additive)` ✓
    ///      · 背景 = **真主城**（同一份 `TownBootstrap`、同一份 `TownData`、同一个 `TownState`）✓
    ///      · 不动 `Town.unity`、不动 `TownBootstrap.cs`（明令禁改）✓
    ///      · `Town.unity` 已在 Build Settings 里 ⇒ 加法加载不需要新增构建设置项 ✓
    /// 而且 **Login 场景一直留在内存里**：进城之后 Esc 设置面板还要用它（用户要求"设置键只在主城能用"），
    /// 卸载它就得把设置面板搬进主城场景 —— 那就等于改了主城 ✗
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、"锁定摄像机和各种控制"是怎么做到的（一行都不改主城）
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownBootstrap` 是主城**唯一**的 `MonoBehaviour`（玩家 `TownAvatar` 是纯 C# 类，
    /// 没有 `Update`；作物/建筑全是静态网格）⇒ **禁用这一个组件**就等于：
    ///   · `Update` 停 ⇒ 不再读 WASD/E、不再推金币与成熟、不再跟随机位 ✓
    ///   · `OnGUI` 停 ⇒ 主城的 HUD / 小地图 / 面板全不画，登录面板上面是干净的 ✓
    ///   · 已建好的场景**保留** ⇒ 玩家角色、农田、传送门全都看得见（"玩家为内容"）✓
    /// 摄像机则被本文件**主动摆一个登录机位**（`FrameLoginCamera`）；进城时 `enabled=true`，
    /// `TownBootstrap.Update` 下一帧就把机位写回跟随视角（它每帧写全 pos/rot/size）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、阶段机
    /// ══════════════════════════════════════════════════════════════════════
    ///   Booting ──(Town 加法加载完 + 锁住)──▶ Login
    ///   Login ──[新的游戏]──▶ Playing                （重置状态 → 写第一个空位 → 进主城）
    ///   Login ──[继续游戏]──▶ Playing                （读最近一次存档 → 反射灌回 TownState）
    ///   Login ◀──[Esc]── SaveList ──[选择]──▶ Playing（读指定位）
    ///   Login / Playing ──[设置]──▶ 设置覆盖层（Esc 关；**只有主城能用 Esc 打开**）
    ///   Playing 期间每 15 秒自动存一次；离开场景 / 退出前再存一次
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、给自动化的口子（全部是公开实例方法，`unity_drive send Login LoginBootstrap <名> <值>`）
    /// ══════════════════════════════════════════════════════════════════════
    ///   LoginStatus() / Do("continue|new|saves|settings|quit|back") / Slot("3,select|delete|delete_confirm|copy")
    ///   SaveNow("标签") / LoadSlot("2") / DeleteSlot("2") / CopySlot("1,4")
    ///   SetSetting("music,0.35") / SettingsProbe("") / Bind("town,interact,<Keyboard>/f") / ResetBinds("")
    ///   ProbeInput("town,move_up") + ProbeInputResult("") / TownStateNow("") / SlotList("") / Shot("路径")
    /// 这些口子只做"玩家点得到的事"，不绕过任何规则（读档照样走 `TownSaveIO.Restore` 的长度/id 校验）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoginBootstrap : MonoBehaviour
    {
        public const string Tag = "[Login] ";

        /// <summary>登录场景名 —— 它必须同时是 **Build Settings 的第 0 条**（打包后的启动场景）。</summary>
        public const string LoginSceneName = "Login";

        public const string TownSceneName = "Town";

        /// <summary>
        /// ★★ v251：**当前存档位**的静态镜像（跨场景可读）★★
        /// 为什么必须是静态：进战斗是**单场景** `LoadScene` ⇒ 本类实例与场景一起被销毁，
        /// 它是"唯一知道当前存档位是谁"的地方（`GameBootstrap.cs:170` 的注释就写着这一点）。
        /// 战斗里要存档、以及回主城要读档，都只能从这里拿位号 ✓
        /// </summary>
        public static int ActiveSlot { get; private set; } = -1;

        /// <summary>
        /// ★★ v251：从战斗回到登录场景时，**要自动读回来的存档位**（-1 = 不自动继续）★★
        /// 由 `GameBootstrap.ExitToTownNow` 在 `LoadScene("Login")` 之前写入。
        /// </summary>
        public static int AutoEnterSlot = -1;

        /// <summary>存活实例（用于"进战斗前让主城存档"这类静态入口）。</summary>
        private static LoginBootstrap _inst;

        /// <summary>
        /// ★★ v251：**进战斗前把主城进度写盘** ★★
        /// 由 `TownBootstrap.GoBattle()` 在切战斗场景之前调用。
        /// 不做这一步，战斗期间 `TownState` 会随场景销毁 ⇒ 回主城只能重建一个空城（进度丢）。
        /// 无实例（例如直接在 Arena 场景 Play）时安静返回，不算错 ✓
        /// </summary>
        public static void SaveBeforeBattle()
        {
            if (_inst == null) { Log.Info(Tag + "进战斗前存档：没有存活实例（直接从战场场景启动？）⇒ 跳过"); return; }
            _inst.SaveCurrent("进入战斗");
        }
        private const float AutoSaveSeconds = 15f;

        private enum Stage { Booting = 0, Login = 1, SaveList = 2, Playing = 3 }

        private Stage _stage = Stage.Booting;
        private bool _settingsOpen;
        private TownBootstrap _town;
        private Camera _cam;
        /// <summary>
        /// ★ v214：登录专用的**第二台相机**（用户原话："进入界面制作单独的摄像头"）★
        /// 见 `FrameLoginCamera` 的注释：它画在 Town 相机之后（depth=1、SolidColor 清屏），
        /// 所以看到的完全是它；进城后立刻关掉，不占任何开销。
        /// </summary>
        private Camera _loginCam;
        private bool _loginCamWanted = true;
        private int _currentSlot = -1;
        private double _playedSeconds;
        private float _autoSaveTimer;
        private int _confirmDelete = -1;
        /// <summary>存档面板键盘选中的位（↑↓ 移动，金框标出）。</summary>
        private int _selectedSlot = -1;
        /// <summary>"返回开始界面"是否已进入二次确认态。</summary>
        private bool _returnTitleArmed;
        /// <summary>最近一次删除的结果（`SaveReport` 里报出来，回答"SaveStore.Delete 到底调没调、返回什么"）。</summary>
        private string _lastDeleteResult;
        private string _status = "";
        private string _bootError;
        private bool _settingsDirty;
        private float _lastSettingsSave;
        private float _probeTimer;
        private string[] _probeLines = Array.Empty<string>();
        private string _lastInputProbe = "（还没跑过按键探针）";
        private bool _restoredQuality;
        private string _newestLineCache;

        // ══════════════════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            // v251：登记存活实例 —— `SaveBeforeBattle()`（进战斗前存档）靠它找到这个实例
            _inst = this;
            Application.runInBackground = true;

            // ★ v252：**换语言时把"读盘缓存"作废** ★
            //   `SaveStore` 把每个存档位的摘要（`TownSaveIO.Describe`）缓存在 `_infos` 里，
            //   而摘要现在是**按当前语言**生成的一句话 ⇒ 切语言后不重算就会留着一整句旧语言的文字
            //   （截图证据：`v252-login-ja` 里前缀是日文、摘要却是英文）。
            //   `LoginHud` 的 `newestLine` 也有自己的一层缓存 ⇒ 一起清 ✓
            L10n.OnChanged += OnLangChanged;

            // ★★ 黑屏（`No cameras rendering`）的两层根治，都在这里起手 ★★
            // ① 先把"总有一台相机在渲染"钉死：`Login.unity` 建场景时那台相机被我设成 `m_IsActive: 0`，
            //    而主城是**加法加载 + 代码建城** ⇒ 从场景加载到城建好之间**零相机** ✗
            // ② 再把载入遮罩立起来：它是 `DontDestroyOnLoad` 的独立宿主，建城那几帧被它盖住 ✓
            EnsureFallbackCamera();
            LoginLoadingHostFactory.Ensure();
            LoginLoading.Begin("启动 · 准备场景");

            LoginSettings.Load();
            LoginSettings.ApplyAll();                       // ★ 设置先于一切生效（含帧率 / 分辨率）
            LoginInput.Build(LoginSettings.Current);
            RefreshSaves();

            Log.Info(Tag + "启动：设置 ← " + LoginSettings.FilePath +
                     $"（音乐 {LoginSettings.Current.musicVolume:0.00} / 音效 {LoginSettings.Current.sfxVolume:0.00}" +
                     $" / 画质 {LoginSettings.TierName(LoginSettings.Current.qualityTier)}" +
                     $" / {LoginSettings.Current.frameRate}fps / {LoginSettings.Current.resolutionWidth}x{LoginSettings.Current.resolutionHeight}）");
            Log.Info(Tag + "存档目录 ← " + SaveStore.Dir + $"（已用 {SaveStore.UsedCount}/{SaveStore.SlotCount}）");
        }

        private void Start()
        {
            StartCoroutine(Boot());
        }

        /// <summary>★ v252：语言变了 ⇒ 把两处"按语言生成的缓存串"作废（详见 `Awake` 里那段说明）。</summary>
        private void OnLangChanged()
        {
            _newestLineCache = null;
            SaveStore.Invalidate();
        }

        private IEnumerator Boot()
        {
            Scene townScene = SceneManager.GetSceneByName(TownSceneName);
            if (!townScene.isLoaded)
            {
                if (!Application.CanStreamedLevelBeLoaded(TownSceneName))
                {
                    Fail($"场景 {TownSceneName} 不在 Build Settings 里，做不了背景");
                    yield break;
                }
                // ★ 用**异步**加载：`LoadSceneAsync.progress` 是本工程唯一能拿到的"真进度" ★
                // 同步 `LoadScene` 期间**一帧都不吐** ⇒ 那条进度条只能是假的，那是在骗人 ✗
                AsyncOperation op = SceneManager.LoadSceneAsync(TownSceneName, LoadSceneMode.Additive);
                if (op == null)
                {
                    LoginLoading.Reset();
                    Fail("Town 场景异步加载没起来（LoadSceneAsync 返回 null）");
                    yield break;
                }
                op.allowSceneActivation = true;

                float waited = 0f;
                while (!op.isDone && waited < 20f)
                {
                    // Unity 的语义：`progress` 到 0.9 就停住，剩下 0.1 是"激活 + Awake"（建城就在这里面）
                    // ⇒ 0..0.9 映射到 0..0.70，剩下的 0.70..1.00 留给"锁主城 / 摆机位 / 起 BGM / 套设置"这几步 ✓
                    LoginLoading.SetProgress(Mathf.Clamp01(op.progress / 0.9f) * 0.70f);
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                townScene = SceneManager.GetSceneByName(TownSceneName);
                if (!townScene.isLoaded)
                {
                    LoginLoading.Reset();
                    Fail("20 秒内没有把 Town 场景加载完");
                    yield break;
                }
                LoginLoading.SetProgress(0.75f);
                Log.Info(Tag + $"Town 场景异步加载完成（进度 75%，实际耗时上限 {waited:0.0}s）");
            }
            else
            {
                LoginLoading.SetProgress(0.75f);
            }

            // 等 `TownBootstrap.Awake` 把整座城建完（加法加载在下一帧才跑 Awake）
            yield return null;
            yield return null;
            LoginLoading.SetProgress(0.82f);

            TownBootstrap[] boots = UnityEngine.Object.FindObjectsByType<TownBootstrap>(FindObjectsInactive.Exclude);
            if (boots.Length == 0)
            {
                LoginLoading.Reset();
                Fail("Town 场景里没有 TownBootstrap");
                yield break;
            }
            _town = boots[0];

            // ★ 锁定：禁用这一个组件 = 停 Update（不再读键、不再推金币）+ 停 OnGUI（不画主城 HUD）★
            _town.enabled = false;
            LoginLoading.SetProgress(0.88f);

            _cam = Camera.main;
            if (_cam == null)
            {
                LoginLoading.Reset();
                Fail("场景里没有 MainCamera，登录背景渲染不出来");
                yield break;
            }
            // 主城自己会给相机挂 `AudioListener`（见 TownBootstrap.BuildCamera）；这里只做兜底：
            // 万一先起播 BGM 再挂监听器，控制台会先报一条 "There are no audio listeners in the scene" ✗
            if (_cam.GetComponent<AudioListener>() == null) _cam.gameObject.AddComponent<AudioListener>();
            FrameLoginCamera();
            // ★ 相机/监听器收口必须在 `_cam` 与 `_loginCam` 都就位之后 ★
            // （用户报的 "2 audio listeners" 与 "视角贴地、下方被切" 都是主城那台没被驱动的相机引发的）
            NormalizeCameras();
            LoginLoading.SetProgress(0.92f);

            // 登录界面也有音乐（真的在响，音量滑块才有意义）
            GameAudio.PlayBgm("bgm_farm_day");
            LoginSettings.ApplyAudio();          // 音频源刚建出来 ⇒ 把设置里的音量再套一次

            // ★ 必须**在城建完之后**再套一遍设置 ★
            // `TownBootstrap.Awake` 里写死了 `Application.targetFrameRate = 60`（那是主城自己的既有行为，
            // 本轮不许改它）⇒ 加法加载时它的 Awake 在**我的 Awake 之后**跑，
            // 会把玩家选的 120 帧**悄悄改回 60**（实测：`帧率设置=120｜Application.targetFrameRate=60` ✗）
            // 所以这里再压一次：谁最后写谁生效，而"玩家选的那个"必须是最后写的 ✓
            LoginSettings.ApplyFrameRate();
            LoginSettings.ApplyQuality();
            LoginSettings.ApplyResolution();
            LoginSettings.ApplyAudio();

            _stage = Stage.Login;

            RefreshProbe();
            LoginLoading.SetProgress(1f);
            LoginLoading.Complete();          // 7 个蔬菜全亮 ⇒ 淡出 ✓
            Log.Info(Tag + "登录界面就绪（主城作背景 · 摄像机与输入已锁）→ " + LoginStatus());
            Log.Info(Tag + "主城状态 ← " + (TownState.Current != null ? TownState.Current.Snapshot() : "（空）"));
        }

        /// <summary>
        /// 相机 / 音频监听器的唯一性收口（两个用户可见问题的根因都在这里）：
        ///   ① `2 audio listeners in the scene` —— 主城场景自带一台相机，它带 `AudioListener`；
        ///      被 `TownBootstrap` 驱动的那台也有一份 ⇒ 两个监听器，Unity 每帧刷红字 ✗
        ///   ② 视角贴地/下方被切 —— 那台"没被驱动"的相机同样是 `depth = -1`，
        ///      与被驱动的相机同深度 ⇒ 渲染顺序不确定，实际看到的是贴地那台 ✗
        /// ⇒ 只保留 **我正在驱动的相机**（`_cam`）与 **登录专用相机**（`_loginCam`），
        ///   其余相机 `enabled = false`、并把它们的 `AudioListener` 销毁；再保证全场景只有 1 个监听器 ✓
        /// </summary>
        private void NormalizeCameras()
        {
            Camera[] all = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            int disabled = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Camera c = all[i];
                if (c == null) continue;
                if (c == _cam || c == _loginCam) continue;
                if (!c.enabled) continue;
                c.enabled = false;
                disabled++;
                Log.Info(Tag + $"停用多余相机：{c.gameObject.name}（场景={c.gameObject.scene.name} depth={c.depth}）");
            }

            // 监听器去重：优先留 `_cam` 上的那个
            AudioListener keep = _cam != null ? _cam.GetComponent<AudioListener>() : null;
            AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
            int killed = 0;
            for (int i = 0; i < listeners.Length; i++)
            {
                AudioListener l = listeners[i];
                if (l == null || l == keep) continue;
                if (keep == null) { keep = l; continue; }      // 第一个当保留
                UnityEngine.Object.Destroy(l);
                killed++;
            }
            if (keep == null && _cam != null)
            {
                _cam.gameObject.AddComponent<AudioListener>();
                Log.Warn(Tag + "全场没有 AudioListener ⇒ 已给主相机补一个");
            }
            Log.Info(Tag + $"相机/监听器收口：停用多余相机 {disabled} 台，销毁多余监听器 {killed} 个，保留监听器在 {(keep != null ? keep.gameObject.name : "无")}");
        }


        /// <summary>
        /// ★ 第一帧就得有相机 ★
        /// `Login.unity` 里那台 `Main Camera` 当初被我设成 `m_IsActive: 0`（为了不抢主城的渲染），
        /// 结果在"场景加载完、主城还没建好"的窗口里**一台相机都没有** ⇒ Game 视图直接弹
        /// `No cameras rendering`（用户截图里那行字就是它）✗
        /// 这里在 `Awake` 最前面兜底：没有可用主相机就**现建一台**（正交、`depth = -1`、
        /// 深墨绿底色 —— **不是纯黑**，黑屏正是用户抱怨的"故障感"）✓
        /// 主城建好后会**复用**这台相机（`TownBootstrap.BuildCamera` 里 `Camera.main` 非空就直接用），
        /// 所以场上不会多出第二台 ✓
        /// </summary>
        private void EnsureFallbackCamera()
        {
            if (Camera.main != null) return;

            var go = new GameObject("Login Camera (fallback)") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.depth = -1;                                   // 低于 LOGIN_CAM(1) ⇒ 登录相机盖在它上面
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.137f, 0.196f, 0.157f, 1f);   // 深墨绿
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;
            // 只在**全场一个监听器都没有**时才加 —— 否则就是用户报的 "2 audio listeners in the scene" ✗
            AudioListener[] existing = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
            if (existing.Length == 0) go.AddComponent<AudioListener>();
            Log.Warn(Tag + "场景里没有可用的 MainCamera ⇒ 已建一台兜底相机（depth=-1，深墨绿底），保证第一帧有画面");
        }

        private void Fail(string message)
        {
            _bootError = message;
            Log.Error(Tag + "启动失败：" + message);
        }

        private void Update()
        {
            if (_bootError != null) return;

            // v214：登录相机只在"还没进城"时存在。放在 Update 的最前面，
            // 是刻意的——它不属于任何一条状态分支，漏写一次就会在城里多渲染一整个场景。
            SyncLoginCamera();

            // 改键录制的收键在这里推进（Esc = 取消）
            // ★ 本地化：状态行用**具名键**（`T(key)`）而不是靠形状表 —— 因为它们是
            //   `前缀 + 运行期值` 拼出来的，拼完的整串在表里没有形状可命中 ✗
            if (LoginInput.Tick() && _settingsOpen)
                _status = L10n.T("status.rebound") + LoginInput.LastRebindLog;

            if (_settingsDirty && Time.unscaledTime - _lastSettingsSave > 0.4f)
            {
                LoginSettings.Save();
                _settingsDirty = false;
                _lastSettingsSave = Time.unscaledTime;
            }

            bool esc = LoginInput.Pressed(InputContext.Town, "settings");

            switch (_stage)
            {
                // ★★ v251：开始界面（Login）也要响应 Esc ★★
                // 用户报"开始界面的 esc 不管用"。根因取证：这个 `switch` 原来只有
                // `SaveList` 与 `Playing` 两个分支，`Stage.Login` **没有 case** ⇒
                // `esc` 算出来了却没人接 ⇒ Esc 在开始界面被整个吞掉 ✗
                // 行为与 `Playing` 一致：开/关设置面板（设置键就是 Esc）。
                case Stage.Login:
                    if (esc) SetSettings(!_settingsOpen);

                    // ★★ v251：**从战斗返回时自动读回进度** ★★
                    // `GameBootstrap.ExitToTownNow` 在切场景前把存档位写进 `AutoEnterSlot`
                    // ⇒ 这里读那个档、再进主城 ⇒ 金币/田地/仓库都回来（不再是空城）✓
                    if (AutoEnterSlot >= 0)
                    {
                        int backSlot = AutoEnterSlot;
                        AutoEnterSlot = -1;
                        LoadSlotIndex(backSlot, "从战斗返回主城");
                    }
                    break;

                case Stage.SaveList:
                    if (esc)
                    {
                        // Esc：先撤"确认删除"，没有待确认时才返回登录（以前一下就直接退回登录 ✗）
                        if (_confirmDelete >= 0) { _confirmDelete = -1; WriteStatus("已取消删除"); }
                        else { _stage = Stage.Login; _selectedSlot = -1; }
                    }
                    HandleSaveListKeys();
                    break;

                case Stage.Playing:
                    _playedSeconds += Time.unscaledDeltaTime;
                    if (esc) SetSettings(!_settingsOpen);          // ★ Esc 只在主城可用 ★
                    if (!_settingsOpen)
                    {
                        _autoSaveTimer += Time.unscaledDeltaTime;
                        if (_autoSaveTimer >= AutoSaveSeconds)
                        {
                            _autoSaveTimer = 0f;
                            SaveCurrent("自动保存");
                        }
                    }
                    break;
            }

            if (_settingsOpen)
            {
                _probeTimer -= Time.unscaledDeltaTime;
                if (_probeTimer <= 0f) { _probeTimer = 0.25f; RefreshProbe(); }
            }
        }

        private void OnGUI()
        {
            TownHud.Begin();
            LoginHud.Prepare();          // 把悬停/按下态的文字色钉住（见 LoginHud.Prepare 的注释）

            if (_bootError != null) { DrawBootError(); return; }

            if (_stage == Stage.Booting)
            {
                // Booting 期间画的是 `LoginLoading` 那层蔬菜进度条（`LoginLoadingHost` 的 OnGUI，
                // `GUI.depth = -1000` ⇒ 永远在最上面）。这里**不要再画第二个提示框**：
                // 两套载入提示叠在一起，正是"黑屏 + 一行弹字"那种观感的来源 ✗
                return;
            }

            // 载入遮罩在显示/淡出期间，底下的按钮不参与点击（否则玩家会点到看不见的按钮）
            if (LoginLoading.Active) return;

            if (_settingsOpen)
            {
                HandleSettings(LoginHud.DrawSettings(_stage == Stage.Playing, _probeLines, _status));
                return;
            }

            if (_stage == Stage.Login)
            {
                HandleLogin(LoginHud.DrawLogin(SaveStore.UsedCount > 0, NewestLine(), _status));
                return;
            }

            if (_stage == Stage.SaveList)
            {
                HandleSaveList(LoginHud.DrawSaveList(_confirmDelete, _status, _selectedSlot));
            }
        }

        private void OnApplicationQuit()
        {
            if (_stage == Stage.Playing) SaveCurrent("退出游戏");
            LoginSettings.RestoreOriginals();
        }

        private void OnDestroy()
        {
            if (_stage == Stage.Playing) SaveCurrent("离开场景");
            LoginSettings.RestoreOriginals();
            LoginInput.Dispose();
            // v214：登录相机是**本组件自己造的**，得自己收拾 ——
            // 否则回主菜单/重载场景时场上会多一台相机（画面被它整屏盖住，且不报错）
            if (_loginCam != null)
            {
                if (_loginCam.gameObject != null) Destroy(_loginCam.gameObject);
                _loginCam = null;
            }
            // ★ v252：撤掉语言回调（`Awake` 里挂的）—— 不撤的话换语言时会打到已销毁的实例上
            L10n.OnChanged -= OnLangChanged;
            if (_inst == this) _inst = null;
        }

        // ══════════════════════════════════════════════════════════════════
        // 摄像机：登录机位
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★★ v214：登录界面的**专用摄像头**（用户原话："进入界面制作单独的摄像头，
        /// 朝向玩家正面，整体视图为左侧玩家、右侧 UI"）★★
        ///
        /// ── 为什么是"第二台相机"，而不是把主相机挪过来 ──
        /// 改前这里是**把主相机改成一个 48° 俯角、半高 14.5 的俯瞰机位**，
        /// 背景是"整座主城的俯视图"。那样有三个问题：
        ///   ① 画面里没有"玩家"这个主体 —— 玩家只是 900 像素画面里的一个小点；
        ///   ② 主相机是 `TownBootstrap` 的东西，进城时它要按自己的规矩摆回来，
        ///      两边抢同一台相机，谁最后写谁生效；
        ///   ③ 俯瞰图很难和"右侧那张 620 宽的登录卡"做出左右分区 ——
        ///      因为画面里没有可以放在左边的**主体**。
        /// ⇒ 现在：**另起一台** `LOGIN_CAM`（`depth = 1`、`SolidColor` 清屏），
        ///   主相机原地不动（`Camera.main` 仍然有效，别处的代码一行都不用改）。
        ///   它画在主相机之后并整屏覆盖 ⇒ 画面上只有它 ✓
        ///   进城（`Stage.Playing`）时立刻 `enabled = false` ⇒ 运行时**零开销** ✓
        ///
        /// ── 机位怎么算（全部**从场景量**，不写死坐标）──
        ///   · 主体 = `TOWN/TownPlayer`（`TownAvatar` 建出来的那个根）
        ///   · 正面 = 玩家模型的 `forward` ⇒ 相机放在 `playerPos + forward × Dist`（他脸朝的那一侧）
        ///   · 高度/朝向 = 瞄准"胸口"（包围盒高度的 62%），而不是脚底 —— 瞄准脚底会看到仰视大下巴
        ///   · 左右分区 = 瞄准点**朝相机右方**偏移 `Lateral` 米
        ///     ⇒ 玩家落到画面左侧约 25% 处，右侧 59%~100% 留给登录卡 ✓
        ///   · 找不到玩家时**回退**到改前那套俯瞰机位（并 Log.Warn 说明）✓
        /// </summary>
        private void FrameLoginCamera()
        {
            if (_cam == null) return;

            // ── 主体：玩家 ──
            GameObject player = null;
            Transform town = _town != null ? _town.transform : null;
            if (town != null)
            {
                Transform p = town.Find("TOWN/TownPlayer");
                if (p == null) p = FindDeep(town, "TownPlayer");
                if (p != null) player = p.gameObject;
            }
            if (player == null) player = GameObject.Find("TownPlayer");

            if (_loginCam == null) BuildLoginCamera();

            if (player == null)
            {
                // ── 回退：改前那套俯瞰机位（保证"登录界面一定有背景画面"）──
                Log.Warn(Tag + "找不到 TOWN/TownPlayer —— 登录相机回退为俯瞰机位（左侧不会是玩家正面）");
                const float PitchDeg = 48f, FallbackDist = 34f;
                float pitch = PitchDeg * Mathf.Deg2Rad;
                var focus = new Vector3(1.5f, 0.8f, 4.5f);
                Vector3 offset = new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch)) * FallbackDist;
                _loginCam.orthographic = true;
                _loginCam.orthographicSize = 14.5f;
                _loginCam.transform.position = focus + offset;
                _loginCam.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
                return;
            }

            // ── 量玩家：包围盒给他"多高"，用它的 forward 定"正面" ──
            Bounds b = new Bounds(player.transform.position, Vector3.one * 0.5f);
            Renderer[] rs = player.GetComponentsInChildren<Renderer>(true);
            bool first = true;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                if (first) { b = rs[i].bounds; first = false; }
                else b.Encapsulate(rs[i].bounds);
            }
            float height = Mathf.Max(0.6f, b.size.y);
            Vector3 chest = new Vector3(b.center.x, b.min.y + height * 0.62f, b.center.z);

            // 玩家正面（水平投影）。模型 forward 到底朝哪一侧，由 `FrontSign` 兜底：
            // 万一那套模型的正面是 -Z（Blender 导出常见），把常量改成 -1 即可，只改一个数
            Vector3 fwd = player.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            fwd *= FrontSign;

            // 距离与机位高度都**按身高给**：小体型角色不会被拍成大头照，大体型也不会出画
            const float Dist = 3.15f;
            const float CamUp = 0.55f;      // 相机比胸口高一点：微微俯视
            const float Lateral = 0.80f;    // 瞄准点朝相机右方偏移 ⇒ 玩家落到左侧

            Vector3 camPos = chest + fwd * (height * Dist) + Vector3.up * (height * CamUp);
            Vector3 toTarget = (chest - camPos).normalized;
            Vector3 camRight = Vector3.Cross(Vector3.up, toTarget).normalized;
            Vector3 aim = chest + camRight * (height * Lateral);

            _loginCam.orthographic = false;
            _loginCam.fieldOfView = 34f;                 // 长焦一点：背景压扁、玩家不畸变
            _loginCam.transform.position = camPos;
            _loginCam.transform.rotation = Quaternion.LookRotation(aim - camPos, Vector3.up);

            Log.Info(Tag + $"登录相机（专用）→ 玩家高 {height:0.00} m｜机位 {camPos.x:0.00},{camPos.y:0.00},{camPos.z:0.00}" +
                          $"｜正面 {fwd.x:0.00},{fwd.z:0.00}｜玩家在画面左侧 {Lateral * 100f:0}% 身高处偏移｜fov 34");
        }

        /// <summary>
        /// 模型正面到底是 `forward` 还是 `-forward`。
        /// `TownAvatar.Build(_root, SpawnPos, 180f)` 是**面朝 −Z** 建的，而程序的 forward
        /// 在 yaw=180 时正好是 −Z ⇒ 取 `+1` 就是他真正的脸朝向。
        /// 若哪天美术把模型转了 180°，这里改成 `-1f` 即可（**只改一个数**）。
        /// </summary>
        private const float FrontSign = 1f;

        /// <summary>造一台登录专用相机（幂等）。</summary>
        private void BuildLoginCamera()
        {
            var go = new GameObject("LOGIN_CAM");
            _loginCam = go.AddComponent<Camera>();
            // depth = 1 ⇒ 画在 Town 相机之后；SolidColor ⇒ 整屏覆盖（画面上只有它）
            _loginCam.depth = 1f;
            _loginCam.clearFlags = CameraClearFlags.SolidColor;
            _loginCam.backgroundColor = new Color(0.58f, 0.75f, 0.88f);
            _loginCam.nearClipPlane = 0.05f;
            _loginCam.farClipPlane = 260f;
            _loginCam.fieldOfView = 34f;
            _loginCam.orthographic = false;
            _loginCam.allowHDR = true;
            _loginCam.allowMSAA = true;
            _loginCam.cullingMask = _cam != null ? _cam.cullingMask : ~0;
            // ★ 不挂 AudioListener ★ 主相机（`TownBootstrap.BuildCamera`）已经有了；
            //   挂两个会让 Unity 每帧报 "There are multiple audio listeners"
            Log.Info(Tag + "登录专用相机已创建：LOGIN_CAM（depth=1 · 独立机位 · 不占 AudioListener）");
        }

        /// <summary>按当前阶段开关登录相机。**每帧一次的不变量**，不给任何一条分支留忘记的机会。</summary>
        private void SyncLoginCamera()
        {
            if (_loginCam == null) return;
            bool want = _stage != Stage.Playing;
            if (want == _loginCamWanted) return;
            _loginCamWanted = want;
            _loginCam.enabled = want;
        }

        /// <summary>按名字在整棵树里找（`GameObject.Find` 只找 active，这里连 inactive 一起）。</summary>
        private static Transform FindDeep(Transform node, string name)
        {
            if (node == null) return null;
            if (node.name == name) return node;
            for (int i = 0; i < node.childCount; i++)
            {
                Transform hit = FindDeep(node.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }


        // ══════════════════════════════════════════════════════════════════
        // 五个按钮 / 存档面板 / 设置面板 的处理
        // ══════════════════════════════════════════════════════════════════

        private void HandleLogin(LoginHud.LoginAction action)
        {
            switch (action)
            {
                case LoginHud.LoginAction.Continue: ActContinue(); break;
                case LoginHud.LoginAction.NewGame: ActNewGame(); break;
                case LoginHud.LoginAction.SaveList:
                    _stage = Stage.SaveList;
                    _confirmDelete = -1;
                    _selectedSlot = -1;          // ★ v253：每次打开都从"无选中"起（不记住上次选的那一位）★
                    RefreshSaves();
                    WriteStatus("存档查看：共 " + SaveStore.SlotCount + " 位，已用 " + SaveStore.UsedCount);
                    break;
                case LoginHud.LoginAction.Settings:
                    SetSettings(true);
                    break;
                case LoginHud.LoginAction.Quit:
                    ActQuit();
                    break;
                // ★ 训练场（额外模式）：与"继续/新的游戏"完全并列的一条入口，不读存档、不进战斗流程 ★
                //   `Training.unity` 自带 `TrainingGame`，本方法只做一次 LoadScene，不传任何参数 ✓
                case LoginHud.LoginAction.Training:
                    ActTraining();
                    break;
            }
        }

        /// <summary>
        /// 存档面板的**键盘操作**（用户报的"没有选择存档的按键"）：
        /// ↑↓ 选位、Enter/Space 选择、Del 删除（再按一次确认）、C 复制、Esc 取消/返回（在 `Update` 里）。
        /// 键盘与鼠标走**同一套**业务函数（`LoadSlotIndex` / `DeleteSlot` / `CopyToFree`），不是第二份实现 ✓
        /// </summary>
        private void HandleSaveListKeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.upArrowKey.wasPressedThisFrame)
                _selectedSlot = _selectedSlot <= 0 ? SaveStore.SlotCount - 1 : _selectedSlot - 1;
            if (kb.downArrowKey.wasPressedThisFrame)
                _selectedSlot = (_selectedSlot + 1) % SaveStore.SlotCount;
            // ★ v253：**默认不选中任何位**（`-1` = 无选中，金框不出现）★
            //   改前这里是 `if (_selectedSlot < 0 || ... ) _selectedSlot = 0;` —— 无条件把没选中的状态
            //   钉成 0 ⇒ 存档面板**一打开就默认高亮"存档位 1"**，顺手敲一下 Enter 就把那一位读走了；
            //   而"存档位 1"往往正是玩家唯一的旧档 ⇒ 这就是本轮的"默认存档"。
            //   ⇒ 现在只把**越过上界**的收回到最后一位，`-1` 一路保留：先按 ↑↓ 才算选中 ✓
            if (_selectedSlot >= SaveStore.SlotCount) _selectedSlot = SaveStore.SlotCount - 1;

            // 没有选中位时，Enter/Del/C 三件事都无事可做（也绝不能拿 -1 去 `SaveStore.Exists` 后报"第 0 位"）✓
            if (_selectedSlot < 0)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                    WriteStatus("还没选中存档位：先按 ↑↓ 选一个");
                return;
            }

            if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                if (SaveStore.Exists(_selectedSlot)) LoadSlotIndex(_selectedSlot, "键盘选择存档");
                else WriteStatus("存档位 " + (_selectedSlot + 1) + " 是空的");
            }
            if (kb.deleteKey.wasPressedThisFrame)
            {
                if (_confirmDelete == _selectedSlot) DeleteSlot((_selectedSlot + 1).ToString(CultureInfo.InvariantCulture));
                else if (SaveStore.Exists(_selectedSlot))
                {
                    _confirmDelete = _selectedSlot;
                    WriteStatus("再按一次 Del（或点「确认删除」）才会真的删除存档位 " + (_selectedSlot + 1));
                }
            }
            if (kb.cKey.wasPressedThisFrame && SaveStore.Exists(_selectedSlot)) CopyToFree(_selectedSlot);
        }

        private void HandleSaveList(LoginHud.SaveClick click)
        {
            switch (click.Action)
            {
                case LoginHud.SaveAction.Close:
                    _stage = Stage.Login;
                    _confirmDelete = -1;
                    WriteStatus("返回登录");
                    break;
                case LoginHud.SaveAction.Select:
                    LoadSlotIndex(click.Slot, "选择存档");
                    break;
                case LoginHud.SaveAction.Delete:
                    // 第一次点 = 进入"确认"态（用户要求的二次确认）；再点「确认删除」才真删
                    _confirmDelete = click.Slot;
                    WriteStatus("再点一次「确认删除」才会真的删除存档位 " + (click.Slot + 1));
                    break;
                case LoginHud.SaveAction.DeleteConfirm:
                    // ★ 确认态里那一颗走的就是 `DeleteSlot`（与自动化 `Slot(..."delete_confirm")` 完全同一条路径）★
                    DeleteSlot((click.Slot + 1).ToString(CultureInfo.InvariantCulture));
                    break;
                case LoginHud.SaveAction.Copy:
                    CopyToFree(click.Slot);
                    break;
                case LoginHud.SaveAction.CancelDelete:
                    _confirmDelete = -1;
                    WriteStatus("已取消删除");
                    break;
            }
        }

        /// <summary>
        /// **存档面板的体检读数**（用户报的"确认删除无效"就要这几个值）：
        /// `_confirmDelete` 的当前值、`确认删除` 按钮的**屏幕矩形**（宽高为 0 ⇒ 控件没尺寸）、
        /// 最近一次 `SaveStore.Delete` 的**返回结果**、以及 6 位的落盘状态。
        /// </summary>
        public string SaveReport(string ignored)
        {
            Rect r = LoginHud.LastConfirmButtonScreen;
            var sb = new System.Text.StringBuilder(400);
            sb.Append("阶段=").Append(_stage)
              .Append("｜_confirmDelete=").Append(_confirmDelete < 0 ? "无" : (_confirmDelete + 1).ToString())
              .Append("｜_selectedSlot=").Append(_selectedSlot < 0 ? "无" : (_selectedSlot + 1).ToString())
              .Append("｜面板已画=").Append(LoginHud.SaveListDraws).Append(" 帧")
              .Append("（面板收到的 confirmSlot=").Append(LoginHud.LastSaveListConfirmSlot + 1).Append("）")
              .Append("｜确认删除按钮 ScreenRect=(").Append(r.x.ToString("0")).Append(',').Append(r.y.ToString("0"))
              .Append(") ").Append(r.width.ToString("0")).Append('x').Append(r.height.ToString("0"))
              .Append(r.width <= 0f || r.height <= 0f ? "  ✗ 尺寸为 0" : "  ✓ 有尺寸")
              .Append('\n').Append("最近一次删除：").Append(_lastDeleteResult ?? "（还没删过）")
              .Append('\n').Append("返回开始界面按钮 ScreenRect=(")
              .Append(LoginHud.LastReturnButtonScreen.x.ToString("0")).Append(',')
              .Append(LoginHud.LastReturnButtonScreen.y.ToString("0")).Append(") ")
              .Append(LoginHud.LastReturnButtonScreen.width.ToString("0")).Append('x')
              .Append(LoginHud.LastReturnButtonScreen.height.ToString("0"))
              .Append('\n').Append(SaveStore.List());
            string text = sb.ToString();
            Log.Info(Tag + "SAVE_UI " + text.Replace("\n", "  |  "));
            return text;
        }

        /// <summary>
        /// **绕过按钮**模拟存档面板的一次点击（证明"按钮 → 处理函数 → 落盘"这条链是好的）。
        /// `spec` = `"delete@3"` / `"confirm@3"` / `"select@3"` / `"copy@3"` / `"cancel@3"`（位号 1 基）。
        /// 它调用的就是 `HandleSaveList` —— 与鼠标点下去走的是**同一个函数** ✓
        /// </summary>
        public string UiClick(string spec)
        {
            string[] p = (spec ?? "").Split('@');
            if (p.Length < 2 || !int.TryParse(p[1], out int shown)) return "需要 \"delete@3\" 这种写法";
            int slot = shown - 1;
            if (slot < 0 || slot >= SaveStore.SlotCount) return "位号越界：" + shown;
            var click = new LoginHud.SaveClick { Slot = slot };
            switch (p[0].Trim().ToLowerInvariant())
            {
                case "delete": click.Action = LoginHud.SaveAction.Delete; break;
                case "confirm": click.Action = LoginHud.SaveAction.DeleteConfirm; break;
                case "select": click.Action = LoginHud.SaveAction.Select; break;
                case "copy": click.Action = LoginHud.SaveAction.Copy; break;
                case "cancel": click.Action = LoginHud.SaveAction.CancelDelete; break;
                default: return "未知动作：" + p[0] + "（delete/confirm/select/copy/cancel）";
            }
            HandleSaveList(click);
            return "已走 UI 同一条处理函数 → " + _status;
        }

        /// <summary>「返回开始界面」的状态回执（自动化 `Do return` 用）。</summary>
        private string WriteReturnBack()
        {
            return (_returnTitleArmed ? "⚠ 已进入确认态（再发一次 Do return 才真的返回）" : "已返回开始界面 / 正在切换")
                   + "｜" + _status;
        }

        /// <summary>「返回开始界面」：第一次点只进确认态，第二次才真的回 `Login` 场景（并先存档）。</summary>
        private void HandleReturnToTitle()
        {
            if (!_returnTitleArmed)
            {
                _returnTitleArmed = true;
                LoginHud.ReturnToTitleArmed = true;
                WriteStatus("再点一次「确认返回？再点一次」就回到开始界面（会先保存当前进度）");
                return;
            }
            _returnTitleArmed = false;
            LoginHud.ReturnToTitleArmed = false;
            if (_stage == Stage.Playing) SaveCurrent("返回开始界面");
            LoginSettings.Save();
            Log.Info(Tag + "返回开始界面 → LoadScene(" + LoginSceneName + ")");
            LoginLoading.Begin("返回开始界面");
            LoginLoading.SetProgress(1f);
            LoginLoading.Complete();
            if (Application.CanStreamedLevelBeLoaded(LoginSceneName)) SceneManager.LoadScene(LoginSceneName);
            else WriteStatus("Build Settings 里没有 " + LoginSceneName + " 场景，返回失败");
        }

        private void HandleSettings(LoginHud.SettingsEvent ev)
        {
            LoginHud.ReturnToTitleArmed = _returnTitleArmed;
            if (ev.ReturnToTitle) HandleReturnToTitle();
            if (ev.AudioChanged)
            {
                LoginSettings.ApplyAudio();
                _settingsDirty = true;
                _status = L10n.T("status.audio") + LoginSettings.ProbeAudio();
            }
            if (ev.QualityChanged)
            {
                LoginSettings.ApplyQuality();
                _settingsDirty = true;
                _status = L10n.T("status.quality") + LoginSettings.ProbeQuality();
            }
            if (ev.FrameRateChanged)
            {
                LoginSettings.ApplyFrameRate();
                _settingsDirty = true;
                _status = L10n.T("status.fps") + LoginSettings.ProbeFrameRate();
            }
            if (ev.ResolutionChanged)
            {
                LoginSettings.ApplyResolution();
                _settingsDirty = true;
                _status = L10n.T("status.resolution") + LoginSettings.ProbeResolution();
            }
            if (ev.BeginRebind) LoginInput.BeginRebind(ev.RebindContext, ev.RebindId);
            if (ev.ResetBindings)
            {
                LoginInput.ResetAll();
                _status = L10n.T("status.bindReset") + LoginInput.Summary();
            }
            if (ev.Close) SetSettings(false);

            if (ev.AudioChanged || ev.QualityChanged || ev.FrameRateChanged || ev.ResolutionChanged)
                RefreshProbe();
        }

        private void SetSettings(bool open)
        {
            _settingsOpen = open;
            // 设置面板开着时把主城也锁住：否则 WASD 还会推着角色走（"设置"里按 S 会往回走 ✗）
            if (_stage == Stage.Playing && _town != null) _town.enabled = !open;
            if (open) RefreshProbe();
        }

        private void WriteStatus(string text)
        {
            _status = text;
            Log.Info(Tag + text);
        }

        /// <summary>存档内容变了：重读 6 位摘要，并把"最近存档"那行缓存作废。</summary>
        private void RefreshSaves()
        {
            SaveStore.Refresh();
            _newestLineCache = null;
        }

        private void RefreshProbe()
        {
            _probeLines = new[]
            {
                "① " + LoginSettings.ProbeAudio(),
                "② " + LoginSettings.ProbeQuality(),
                "③ " + LoginSettings.ProbeFrameRate(),
                "④ " + LoginSettings.ProbeResolution(),
                "⑤ " + LoginInput.Summary(),
            };
        }

        private string NewestLine()
        {
            // ★ 这段不进 OnGUI 的逐帧路径 ★
            // `SaveStore.Newest` 要读 6 个 JSON 并解析 —— OnGUI 一帧被调 2~3 次，
            // 放进去等于每帧读 18 个文件（实测本该无感的界面会开始掉帧）。缓存 + 脏了才重算。
            if (_newestLineCache != null) return _newestLineCache;
            int newest = SaveStore.Newest;
            _newestLineCache = newest < 0
                // ★ v252：两段都改成具名键（拼接出来的中文串进不了词表 ⇒ 英文/日文界面会漏成中文）★
                ? L10n.T("login.newestNone")
                : L10n.T("login.newestSlot", (newest + 1).ToString(), SaveStore.Line(newest));
            return _newestLineCache;
        }

        // ══════════════════════════════════════════════════════════════════
        // 业务动作（按钮与自动化口子**共用同一条路径**）
        // ══════════════════════════════════════════════════════════════════

        private bool ActContinue()
        {
            int newest = SaveStore.Newest;
            if (newest < 0) { WriteStatus("没有任何存档，「继续游戏」不可用"); return false; }
            return LoadSlotIndex(newest, "继续游戏");
        }

        private bool ActNewGame()
        {
            int free = SaveStore.FirstFree;
            if (free < 0)
            {
                // ★ v252：改成具名键（原文串里有数字 6，拼接/占位都对不上词表）★
                WriteStatus(L10n.T("login.allSlotsFull", SaveStore.SlotCount.ToString()));
                return false;
            }
            TownSaveIO.ResetToNewGame();
            _currentSlot = free;
            _playedSeconds = 0;
            SaveCurrent("新的游戏");
            EnterTown();
            WriteStatus("新的游戏：主城已重置，写入存档位 " + (free + 1));
            return true;
        }

        private void ActQuit()
        {
            WriteStatus("退出游戏");
            if (_stage == Stage.Playing) SaveCurrent("退出游戏");
            LoginSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;      // 编辑器里 Application.Quit() 不会真的退
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// ★★ 训练场（额外模式）的唯一入口动作 ★★
        ///
        /// 只做三件事：打一行日志、判场景在不在 Build Settings 里、`LoadScene("Training")`。
        ///   · **不读存档、不写存档、不进 `Stage.Playing`** —— 训练场是"练枪房"，
        ///     它与主城/战斗唯一的耦合就是这一次单场景加载（与 `TownBootstrap.GoBattle` 同一种做法）✓
        ///   · 训练场侧自带 `TrainingGame`（自建地面/玩家/稻草人/HUD），**不依赖**任何 Session/World ⇒
        ///     就算主城那份存档坏了，训练场照样能进 ✓
        ///   · 场景不在 Build Settings 里时**不静默失败**：写一条明确的状态条，人留在登录页 ✓
        /// </summary>
        private void ActTraining()
        {
            const string TrainingScene = "Training";
            if (!Application.CanStreamedLevelBeLoaded(TrainingScene))
            {
                WriteStatus("训练场场景没进 Build Settings（Assets/Scenes/Training.unity），无法进入");
                Log.Warn(Tag + "训练场入口：CanStreamedLevelBeLoaded(\"Training\") = false");
                return;
            }
            WriteStatus("进入训练场（自由练习，不影响存档）");
            Log.Info(Tag + "训练场入口 → LoadScene(" + TrainingScene + ")");
            // 遮罩不用在这里开：`LoginLoadingHost.OnSceneLoaded` 对"非 Login / 非 Town"的场景
            // 会自动盖一张"载入完成"的确认遮罩（与进战斗那条同步 LoadScene 完全同一条路径）✓
            SceneManager.LoadScene(TrainingScene);
        }

        private bool LoadSlotIndex(int slot, string reason)
        {
            // ★ 载入动画的第二个触发点：**存档读取** ★
            // 读档本身很快（几毫秒），但玩家点"继续/选择"之后到主城能动之间有一段白屏感 ⇒ 用遮罩盖住，
            // 并给出三个真实里程碑：读文件 → 灌状态 → 进城 ✓（最短显示 0.55s，见 LoginLoading）
            LoginLoading.Begin(reason + " · 存档位 " + (slot + 1));
            LoginLoading.SetProgress(0.2f);

            if (!SaveStore.Read(slot, out TownSaveData data, out string readError))
            {
                LoginLoading.Reset();
                WriteStatus("读档失败：" + readError);
                return false;
            }
            LoginLoading.SetProgress(0.55f);

            // ★ v254：**读档之前**就把"当前存档位"摆正 ★
            //   读档尾部会调 `TownDropLedger.AfterRestore` —— 那里可能要给"战斗里打 BOSS 掉了、
            //   但上一次写盘失败"的那一颗**重试落盘**，它按 `ActiveSlot` 定位该写哪个档。
            //   这里若还是**上一个位号**（例如"返回开始界面"后改读另一档），重试就会写错档 ✗
            //   `EnterTown()` 稍后还会再赋一次同一个值，重复赋值无害 ✓
            ActiveSlot = slot;
            if (!TownSaveIO.Restore(data, out string restoreError))
            {
                LoginLoading.Reset();
                WriteStatus("读档失败：" + restoreError);
                return false;
            }
            LoginLoading.SetProgress(0.85f);

            _currentSlot = slot;
            _playedSeconds = data.playedSeconds;
            _confirmDelete = -1;
            EnterTown();
            LoginLoading.SetProgress(1f);
            LoginLoading.Complete();
            WriteStatus(reason + " → 存档位 " + (slot + 1));
            return true;
        }

        private void EnterTown()
        {
            _stage = Stage.Playing;
            _settingsOpen = false;
            _autoSaveTimer = 0f;
            // v251：把"当前存档位"镜像到静态字段 —— 进战斗后本类实例会被销毁，
            //       战斗退出要靠 `ActiveSlot` 才知道该读哪个档 ✓
            ActiveSlot = _currentSlot;
            if (_town != null) _town.enabled = true;
            Log.Info(Tag + "进入主城（控制已交回 TownBootstrap）← " +
                     (TownState.Current != null ? TownState.Current.Snapshot() : "（空）"));
        }

        private bool SaveCurrent(string reason)
        {
            if (_currentSlot < 0) return false;

            // ★ 状态没了就不写 ★
            // 踩过的坑：域重载（改脚本 / 停 play）时静态的 `TownState.Current` 会被清空，
            // 而**退出流程里的 `OnApplicationQuit` 仍会跑** ⇒ 那一下会把一个"金币 0 / 田地 0 / 空仓库"
            // 的**看起来合法**的存档，悄悄覆盖掉玩家上一个好档 ✗（实测：`slot_1.json` 从 3001 字节变成 389 字节）
            // ⇒ 宁可不存，也不能存一个空壳 ✓
            TownState state = TownState.Current;
            if (state == null)
            {
                Log.Warn(Tag + "保存被跳过：TownState 已被清空（域重载 / 退出流程中），不拿空状态覆盖存档位 " + (_currentSlot + 1));
                return false;
            }

            TownSaveData data = TownSaveIO.Capture(state, _playedSeconds + 0.0,
                                                   "主城存档 " + (_currentSlot + 1));
            if (!SaveStore.Write(_currentSlot, data, out string error))
            {
                WriteStatus("保存失败：" + error);
                return false;
            }
            RefreshSaves();
            // ★ 具名键 + `{0}`/`{1}` 占位：`T(key, args)` 走 `string.Format` ✓
            _status = L10n.T("status.saved", _currentSlot + 1, reason);
            return true;
        }

        private bool CopyToFree(int from)
        {
            int free = SaveStore.FirstFree;
            if (free < 0) { WriteStatus("没有空存档位，复制不可用"); return false; }
            if (!SaveStore.Copy(from, free, out string error)) { WriteStatus("复制失败：" + error); return false; }
            RefreshSaves();
            WriteStatus("已复制存档位 " + (from + 1) + " → " + (free + 1));
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 自动化口子（`unity_drive send Login LoginBootstrap <方法> [值]`）★
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// **Build Settings 里的场景顺序** —— 「登录界面为什么不出现」最直接的读数。
        ///
        /// 第 `0` 条 = **打包后一开游戏就进的那张图**。只要它不是 `Login.unity`，
        /// 玩家启动就直接落在主城/战斗里，登录界面**根本不会被加载**（不是"画错"，是"没跑"）✗
        /// 读的是**引擎内存里那份**（`SceneManager.sceneCountInBuildSettings` + `SceneUtility.GetScenePathByBuildIndex`），
        /// 所以它同时能回答"改完以后 Unity 到底认没认" ✓
        /// </summary>
        public string BuildSceneOrder(string ignored)
        {
            var sb = new System.Text.StringBuilder(320);
            int count = SceneManager.sceneCountInBuildSettings;
            sb.Append("Build Settings 共 ").Append(count).Append(" 条（第 0 条 = 启动场景）：");
            for (int i = 0; i < count; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                sb.Append('\n').Append(i).Append(": ").Append(path)
                  .Append(IsLoginPath(path) ? "   ← 登录界面（启动场景必须是它）" : "");
            }
            string flat = sb.ToString();
            Log.Info(Tag + "BUILD_SCENES " + flat.Replace("\n", "  |  "));
            return flat;
        }

        /// <summary>第 0 条是不是登录场景（`true` = 打包后先进登录界面）。</summary>
        public bool IsLoginFirstScene()
        {
            if (SceneManager.sceneCountInBuildSettings <= 0) return false;
            return IsLoginPath(SceneUtility.GetScenePathByBuildIndex(0));
        }

        private static bool IsLoginPath(string path)
            => !string.IsNullOrEmpty(path)
               && System.IO.Path.GetFileNameWithoutExtension(path) == LoginSceneName;

        /// <summary>一句话状态。</summary>
        public string LoginStatus()
        {
            string newest = SaveStore.Newest < 0 ? "无" : (SaveStore.Newest + 1).ToString();
            string s = $"阶段={_stage}｜设置面板={(_settingsOpen ? "开" : "关")}" +
                       $"｜当前存档位={(_currentSlot < 0 ? "无" : (_currentSlot + 1).ToString())}" +
                       $"｜存档={SaveStore.UsedCount}/{SaveStore.SlotCount}｜最新={newest}" +
                       $"｜主城控制={(_town == null ? "未载入" : (_town.enabled ? "已交回" : "已锁定"))}" +
                       $"｜游玩时长={TownSaveIO.Duration(_playedSeconds)}" +
                       $"｜持久化目录={Application.persistentDataPath}";
            Log.Info(Tag + "STATUS " + s);
            return s;
        }

        /// <summary>按 id 走一遍登录界面的按钮：continue / new / saves / settings / quit / back。</summary>
        public string Do(string id)
        {
            // ★ 还在 Booting 时不接受动作 ★
            // 实测踩到过：Boot 还没跑完（主城在异步加载）就发 `Do continue` ⇒ 状态被灌进主城，
            // 但 `_town` 还是 null（"主城控制=未载入"）⇒ 相机/输入没接上，登录界面也还没就绪 ✗
            // 与其给出一个半成品状态，不如明确回一句"还在载入" ✓
            if (_stage == Stage.Booting) return "⏳ 还在载入（Booting）：" + LoginLoading.Describe();

            string key = (id ?? "").Trim().ToLowerInvariant();
            switch (key)
            {
                case "continue": return ActContinue() ? LoginStatus() : _status;
                case "new":
                case "newgame": return ActNewGame() ? LoginStatus() : _status;
                case "saves":
                case "savelist":
                    _stage = Stage.SaveList;
                    _confirmDelete = -1;
                    _selectedSlot = -1;          // ★ v253：与鼠标点「存档查看」同一条规则：默认无选中 ★
                    RefreshSaves();
                    WriteStatus("存档查看：已用 " + SaveStore.UsedCount + "/" + SaveStore.SlotCount);
                    return LoginStatus();
                case "settings":
                    SetSettings(true);
                    return LoginStatus();
                case "closesettings":
                    SetSettings(false);
                    return LoginStatus();
                case "return":                       // 设置里的「返回开始界面」（要发两次 = 二次确认）
                    HandleReturnToTitle();
                    return WriteReturnBack();
                case "quit": ActQuit(); return "已请求退出";
                case "back":
                    _stage = Stage.Login;
                    _confirmDelete = -1;
                    return LoginStatus();
                default:
                    return "❌ 未知动作：" + id + "（可用 continue/new/saves/settings/closesettings/back/quit）";
            }
        }

        /// <summary>存档面板的一步："3,select" / "3,delete"（进入确认态）/ "3,delete_confirm" / "3,copy"。</summary>
        public string Slot(string csv)
        {
            string[] p = (csv ?? "").Split(',');
            if (p.Length < 2) return "❌ 需要 \"位号,动作\"，例：\"2,select\"";
            if (!int.TryParse(p[0].Trim(), out int shown)) return "❌ 位号不是 1..6 的整数：" + p[0];
            int slot = shown - 1;
            if (slot < 0 || slot >= SaveStore.SlotCount) return "❌ 位号越界：" + shown;
            string action = p[1].Trim().ToLowerInvariant();

            switch (action)
            {
                case "select":
                    {
                        bool ok = LoadSlotIndex(slot, "选择存档");
                        return ok ? "✔ 选择 → 位 " + shown + "\n" + LoginStatus() : "❌ " + _status;
                    }
                case "delete":
                    _confirmDelete = slot;
                    return "⚠ 已进入确认态（位 " + shown + "）：再发一次 delete_confirm 才会真删";
                case "delete_confirm":
                    return DeleteSlot(shown.ToString(CultureInfo.InvariantCulture));
                case "copy":
                    return CopyToFree(slot) ? "✔ 复制 → 空位\n" + SaveStore.List() : "❌ " + _status;
                case "info":
                    return "位 " + shown + "：" + SaveStore.Line(slot);
                case "cancel":
                    _confirmDelete = -1;
                    WriteStatus("已取消删除");
                    return "✔ 已取消（确认态清除）｜" + _status;
                default:
                    return "❌ 未知动作：" + action + "（select / delete / delete_confirm / copy / info）";
            }
        }

        /// <summary>把当前主城状态存进"当前存档位"（没有就取第一个空位）。</summary>
        public string SaveNow(string label)
        {
            if (_currentSlot < 0)
            {
                int free = SaveStore.FirstFree;
                if (free < 0) return "❌ 6 位都满了，没有地方可存";
                _currentSlot = free;
            }
            bool ok = SaveCurrent(string.IsNullOrEmpty(label) ? "手动保存" : label);
            return (ok ? "✔ " + _status : "❌ " + _status) + "\n" + SaveStore.Line(_currentSlot);
        }

        /// <summary>读某一位并进主城（1 基）。</summary>
        public string LoadSlot(string n)
        {
            if (!int.TryParse(n, out int shown)) return "❌ 需要 1..6";
            bool ok = LoadSlotIndex(shown - 1, "读档");
            return (ok ? "✔ " : "❌ ") + _status + "\n" + SnapshotNow();
        }

        /// <summary>删某一位（1 基；**入口层不做确认**，确认在 UI / Slot 的 delete→delete_confirm 两步里）。</summary>
        public string DeleteSlot(string n)
        {
            if (!int.TryParse(n, out int shown)) return "❌ 需要 1..6";
            int slot = shown - 1;
            if (!SaveStore.Delete(slot, out string error))
            {
                _lastDeleteResult = $"位 {shown} 删除失败：{error}";
                _confirmDelete = -1;          // 失败也要退出确认态，否则那一行永远卡在"确认删除"
                return "❌ " + error;
            }
            _lastDeleteResult = $"位 {shown} 删除成功（SaveStore.Delete 返回 true）";
            if (_currentSlot == slot) _currentSlot = -1;
            RefreshSaves();
            _confirmDelete = -1;
            WriteStatus("已删除存档位 " + shown);
            return "✔ 已删除位 " + shown + "\n" + SaveStore.List();
        }

        /// <summary>复制："1,4" = 把位 1 复制到位 4（目标必须为空）。</summary>
        public string CopySlot(string csv)
        {
            string[] p = (csv ?? "").Split(',');
            if (p.Length < 2) return "❌ 需要 \"源位,目标位\"";
            if (!int.TryParse(p[0].Trim(), out int from) || !int.TryParse(p[1].Trim(), out int to))
                return "❌ 两个数都要是 1..6";
            if (from < 1 || from > SaveStore.SlotCount || to < 1 || to > SaveStore.SlotCount)
                return "❌ 位号越界";
            if (!SaveStore.Copy(from - 1, to - 1, out string error)) return "❌ " + error;
            RefreshSaves();
            return "✔ 已复制 " + from + " → " + to + "\n" + SaveStore.List();
        }

        /// <summary>6 个存档位的读文本（落盘路径 + 内容摘要）。</summary>
        public string SlotList(string ignored)
        {
            string list = SaveStore.List();
            Log.Info(Tag + "SLOTS\n" + list);
            return list;
        }

        /// <summary>
        /// 改一项设置："music,0.35" / "sfx,0.6" / "quality,0|1|2" / "fps,30|60|120" /
        /// "res,1920x1080" / "fullscreen,1" / "windowed,1"。**立即生效并落盘**，返回五项读数。
        /// </summary>
        public string SetSetting(string csv)
        {
            string[] p = (csv ?? "").Split(',');
            if (p.Length < 2) return "❌ 需要 \"键,值\"";
            string key = p[0].Trim().ToLowerInvariant();
            string val = p[1].Trim();
            GameSettings s = LoginSettings.Current;

            switch (key)
            {
                case "music":
                    if (!float.TryParse(val, out float m)) return "❌ 音量要数字";
                    s.musicVolume = Mathf.Clamp01(m);
                    LoginSettings.ApplyAudio();
                    break;
                case "sfx":
                    if (!float.TryParse(val, out float f)) return "❌ 音量要数字";
                    s.sfxVolume = Mathf.Clamp01(f);
                    LoginSettings.ApplyAudio();
                    break;
                case "quality":
                    if (!int.TryParse(val, out int q)) return "❌ 画质要 0/1/2";
                    s.qualityTier = Mathf.Clamp(q, 0, 2);
                    LoginSettings.ApplyQuality();
                    break;
                case "fps":
                    if (!int.TryParse(val, out int fps)) return "❌ 帧率要 30/60/120";
                    if (fps != 30 && fps != 60 && fps != 120) return "❌ 帧率只支持 30/60/120";
                    s.frameRate = fps;
                    LoginSettings.ApplyFrameRate();
                    break;
                case "res":
                    {
                        string[] wh = val.ToLowerInvariant().Replace('×', 'x').Split('x');
                        if (wh.Length != 2 || !int.TryParse(wh[0], out int w) || !int.TryParse(wh[1], out int h))
                            return "❌ 分辨率写法：1920x1080";
                        s.resolutionWidth = w;
                        s.resolutionHeight = h;
                        LoginSettings.ApplyResolution();
                        break;
                    }
                case "fullscreen":
                    s.fullScreen = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase);
                    LoginSettings.ApplyResolution();
                    break;
                case "windowed":
                    s.fullScreen = !(val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase));
                    LoginSettings.ApplyResolution();
                    break;
                default:
                    return "❌ 未知键：" + key + "（music/sfx/quality/fps/res/fullscreen/windowed）";
            }

            LoginSettings.Save();
            RefreshProbe();
            WriteStatus("设置 " + key + " = " + val + "（已生效并落盘）");
            return "✔ " + key + " = " + val + "\n" + SettingsProbe("");
        }

        /// <summary>五项设置的**当前真实读数**（改动前后各打一次就能看出差别）。</summary>
        public string SettingsProbe(string ignored)
        {
            RefreshProbe();
            string text = string.Join("\n", _probeLines) + "\n" + LoginInput.Describe();
            Log.Info(Tag + "PROBE\n" + text);
            return text;
        }

        /// <summary>改键："town,interact,&lt;Keyboard&gt;/f"。</summary>
        public string Bind(string csv)
        {
            string[] p = (csv ?? "").Split(',');
            if (p.Length < 3) return "❌ 需要 \"上下文,动作,路径\"，例：town,interact,<Keyboard>/f";
            string ctxText = p[0].Trim().ToLowerInvariant();
            InputContext ctx = ctxText == "combat" ? InputContext.Combat : InputContext.Town;
            string id = p[1].Trim();
            string path = string.Join(",", p, 2, p.Length - 2).Trim();

            bool ok = LoginInput.SetPath(ctx, id, path);
            return (ok ? "✔ " : "❌ ") + LoginInput.LastRebindLog + "\n" + LoginInput.Describe();
        }

        /// <summary>两套上下文恢复默认键。</summary>
        public string ResetBinds(string ignored)
        {
            LoginInput.ResetAll();
            return "✔ " + LoginInput.LastRebindLog + "\n" + LoginInput.Describe();
        }

        /// <summary>
        /// 按键探针：往键盘设备塞一帧"按下"，看 ① 动作 ② 主城自己的硬编码读取 ③ 角色位置 三者是否一起动。
        /// 结果用 <see cref="ProbeInputResult"/> 取（协程要等两帧）。
        /// </summary>
        public string ProbeInput(string csv)
        {
            string[] p = (csv ?? "town,move_up").Split(',');
            InputContext ctx = p.Length > 1 && p[0].Trim().ToLowerInvariant() == "combat" ? InputContext.Combat : InputContext.Town;
            string id = p.Length > 1 ? p[1].Trim() : "move_up";
            _lastInputProbe = "（探针进行中：" + id + "）";
            StartCoroutine(ProbeInputRoutine(ctx, id));
            return "已排队：context=" + LoginInput.ContextName(ctx) + " action=" + id + "（用 ProbeInputResult 取结果）";
        }

        public string ProbeInputResult(string ignored) => _lastInputProbe;

        /// <summary>当前主城状态的快照（与 `TownBootstrap.TownStatus` 同一个字符串）。</summary>
        public string SnapshotNow()
        {
            TownState s = TownState.Current;
            return s == null ? "（没有主城状态）" : s.Snapshot();
        }

        /// <summary>
        /// ★ v252：**切界面语言**（`"zh"` / `"en"` / `"ja"`）—— 登录界面的那一版。
        ///
        /// `TownBootstrap.SetLang` 只能在主城里调；而语言验收要求"登录界面也各拍一张"，
        /// 登录界面又只有三颗 IMGUI 按钮（自动化点不到）⇒ 这里补同一个口子。
        /// 它做的仍然只有一件事：`L10n.Set(lang)`（写 `PlayerPrefs` + 清缓存 + 字体代数 +1）✓
        /// </summary>
        public string SetLang(string code)
        {
            string c = (code ?? "").Trim().ToLowerInvariant();
            switch (c)
            {
                case "zh":
                case "cn":
                case "chinese": L10n.Set(Lang.Zh); break;
                case "en":
                case "english": L10n.Set(Lang.En); break;
                case "ja":
                case "jp":
                case "japanese": L10n.Set(Lang.Ja); break;
                default: return "❌ 未知语言：" + code + "（可用 zh / en / ja）";
            }
            _newestLineCache = null;      // 那行是缓存串 ⇒ 换语言要重算，否则仍是旧语言的句子
            Log.Info("[Login] 语言 → " + L10n.Code(L10n.Current) + "（" + L10n.Native(L10n.Current) + "）");
            return "语言 = " + L10n.Code(L10n.Current) + "｜覆盖 " + L10n.Coverage(L10n.Current) + "/" + L10n.KeyCount();
        }

        public string TownStateNow(string ignored)
        {
            string snap = SnapshotNow();
            Log.Info(Tag + "STATE " + snap);
            return snap;
        }

        /// <summary>
        /// **相机读数**（黑屏问题的取证口子）：把场上每一台相机都列出来
        /// —— 名字 / 是否激活 / `enabled` / `depth` / 正交半高 / 属于哪个场景。
        /// `No cameras rendering` 的判据就是"**一台激活且 enabled** 的相机都没有"，
        /// 所以这里把 `activeInHierarchy` 与 `enabled` 分开列（Unity 里两个都会让渲染停掉）✓
        /// </summary>
        public string CameraReport(string ignored)
        {
            var sb = new System.Text.StringBuilder(640);
            Camera[] all = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            int rendering = 0;
            sb.Append("相机共 ").Append(all.Length).Append(" 台（含未激活）：");
            for (int i = 0; i < all.Length; i++)
            {
                Camera c = all[i];
                if (c == null) continue;
                bool on = c.isActiveAndEnabled;
                if (on) rendering++;
                sb.Append('\n').Append("  ").Append(i + 1).Append(". ").Append(c.gameObject.name)
                  .Append("｜场景=").Append(c.gameObject.scene.name)
                  .Append("｜activeInHierarchy=").Append(c.gameObject.activeInHierarchy)
                  .Append("｜enabled=").Append(c.enabled)
                  .Append("｜depth=").Append(c.depth)
                  .Append("｜").Append(c.orthographic ? $"正交 half={c.orthographicSize:0.0}" : $"透视 fov={c.fieldOfView:0}")
                  .Append("｜pos=(").Append(c.transform.position.x.ToString("0.0")).Append(',')
                  .Append(c.transform.position.y.ToString("0.0")).Append(',')
                  .Append(c.transform.position.z.ToString("0.0")).Append(')')
                  .Append(on ? "   ◀ 正在渲染" : "   ✗ 不渲染");
            }
            sb.Append('\n').Append("正在渲染的相机 = ").Append(rendering)
              .Append("（0 ⇒ 就是 No cameras rendering）")
              .Append("｜Camera.main = ").Append(Camera.main != null ? Camera.main.gameObject.name : "null")
              .Append("｜LOGIN_CAM = ").Append(_loginCam != null ? (_loginCam.isActiveAndEnabled ? "在（active）" : "在（停用）") : "无")
              .Append("｜兜底相机 = ").Append(GameObject.Find("Login Camera (fallback)") != null ? "已建" : "未建");
            string text = sb.ToString();
            Log.Info(Tag + "CAMERAS " + text.Replace("\n", "  |  "));
            return text;
        }

        /// <summary>载入遮罩的读数（进度 / 点亮了几个蔬菜 / 图标来自 Resources 还是现场画的）。</summary>
        public string LoadingProbe(string ignored)
        {
            string s = LoginLoading.Describe();
            Log.Info(Tag + "LOADING " + s);
            return s;
        }

        /// <summary>
        /// 把载入遮罩**钉**在某个进度上（自动化截图用：`"0"` / `"0.5"` / `"1"` / `"0.5,自定义标签"`）；
        /// 传 `"off"` 关掉。**只用于验收**，正常流程不走这里。
        /// </summary>
        public string ForceLoading(string csv)
        {
            string text = (csv ?? "").Trim();
            if (text.Equals("off", System.StringComparison.OrdinalIgnoreCase))
            {
                LoginLoading.Reset();
                return "遮罩已关：" + LoginLoading.Describe();
            }
            string[] parts = text.Split(',');
            if (!float.TryParse(parts[0], out float p))
            {
                LoginLoading.Reset();
                return "需要一个 0..1 的数字：\"0.5\"，或 \"0.5,载入多村\"；\"off\" 关掉";
            }
            LoginLoading.DebugPin(p, parts.Length > 1 ? parts[1] : null);
            return LoginLoading.Describe();
        }

        /// <summary>截一张 PNG（**含 OnGUI**；相对路径按工程根解析）。</summary>
        public string Shot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "❌ 需要路径";
            string root = Path.GetDirectoryName(Application.dataPath) ?? ".";
            string full = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
            try
            {
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                return "❌ 建目录失败：" + e.Message;
            }
            ScreenCapture.CaptureScreenshot(full);
            Log.Info(Tag + "截图已排队 → " + full);
            return "截图已排队 → " + full;
        }

        // ══════════════════════════════════════════════════════════════════
        // 按键探针（协程）
        // ══════════════════════════════════════════════════════════════════

        private IEnumerator ProbeInputRoutine(InputContext ctx, string id)
        {
            Keyboard kb = Keyboard.current;
            BindableAction action = LoginInput.Find(ctx, id);
            if (kb == null) { _lastInputProbe = "❌ 没有键盘设备"; yield break; }
            if (action == null) { _lastInputProbe = "❌ 没有这个动作：" + id; yield break; }

            if (!TryKeyFromPath(action.Path, out Key key))
            {
                _lastInputProbe = $"动作 {id} 绑的是 {action.Path}，不是键盘键 ⇒ 合成按键探针跳过" +
                                  $"（effectivePath 解析={action.Resolves}）";
                Log.Info(Tag + "PROBE " + _lastInputProbe);
                yield break;
            }

            Vector2 pos0 = _town != null ? _town.PlayerMapPosition : Vector2.zero;
            bool townActive = _town != null && _town.enabled;

            // ① 按下
            var press = new KeyboardState();
            press.Set(key, true);
            InputSystem.QueueStateEvent(kb, press);
            yield return null;
            yield return null;

            bool deviceSaw = DeviceKeyHeld(kb, key);
            bool actionSaw = action.Action != null && action.Action.IsPressed();

            // ② 按住 0.3 秒，看主城自己的硬编码读取会不会真的推动角色
            yield return new WaitForSeconds(0.3f);
            Vector2 pos1 = _town != null ? _town.PlayerMapPosition : Vector2.zero;

            // ③ 松开
            var release = new KeyboardState();
            release.Set(key, false);
            InputSystem.QueueStateEvent(kb, release);
            yield return null;
            yield return null;

            bool releasedOk = action.Action != null && !action.Action.IsPressed();
            float moved = Vector2.Distance(pos0, pos1);
            string moveText = townActive
                ? $"角色位移={moved:0.000} m（({pos0.x:0.00},{pos0.y:0.00}) → ({pos1.x:0.00},{pos1.y:0.00})）"
                : "角色位移=0（主城控制已锁 —— 登录界面就该是这样）";

            _lastInputProbe =
                $"上下文={LoginInput.ContextName(ctx)} 动作={id}｜绑定={action.Path}（解析={action.Resolves}）\n" +
                $"① 设备层 Keyboard.{key} 按下={deviceSaw}\n" +
                $"② InputAction.IsPressed={actionSaw}（松开后={!releasedOk}）\n" +
                $"③ {moveText}";
            Log.Info(Tag + "PROBE\n" + _lastInputProbe);
        }

        private static bool DeviceKeyHeld(Keyboard kb, Key key)
        {
            KeyControl control = kb[key];
            return control != null && control.isPressed;
        }

        /// <summary>把 "&lt;Keyboard&gt;/w" 这类路径换成 `Key` 枚举（数字键的控件名是 "1".."9"，枚举名是 Digit1..）。</summary>
        private static bool TryKeyFromPath(string path, out Key key)
        {
            key = Key.None;
            if (string.IsNullOrEmpty(path)) return false;
            if (!path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)) return false;
            string name = path.Substring("<Keyboard>/".Length);
            if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
                return Enum.TryParse("Digit" + name, true, out key);
            return Enum.TryParse(name, true, out key);
        }

        // ══════════════════════════════════════════════════════════════════
        // 启动失败时的红屏
        // ══════════════════════════════════════════════════════════════════

        private void DrawBootError()
        {
            var box = new Rect((Screen.width - 760f) * 0.5f, (Screen.height - 160f) * 0.5f, 760f, 160f);
            GUI.Box(box, "");
            GUI.color = new Color(0.8f, 0.28f, 0.24f, 1f);
            GUI.Label(new Rect(box.x + 18f, box.y + 14f, box.width - 36f, 34f),
                L10n.S("登录界面启动失败"), BigLabel());     // ★ 本地化：这条兜底页也是玩家可见的
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + 18f, box.y + 54f, box.width - 36f, 90f), _bootError, MidLabel());
        }

        private static GUIStyle _big;
        private static GUIStyle _mid;

        private static GUIStyle BigLabel()
        {
            if (_big != null) return _big;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            return _big;
        }

        private static GUIStyle MidLabel()
        {
            if (_mid != null) return _mid;
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            return _mid;
        }
    }
}
