using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Seedfall
{
    /// <summary>
    /// 世界与流程的唯一宿主：装配场景、读输入、按固定步长驱动 RunSession、同步表现。
    /// 场景里只需要一个挂着本组件的对象（架构案 A-2：代码运行时构建，保证可复现）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("Run")]
        [SerializeField] private long runSeed = 20240607L;
        [SerializeField] private bool autoStart = true;
        [Tooltip("-1 = 人类操作；0 Idle 1 Farmer 2 Balanced 3 Rush")]
        [SerializeField] private int botStrategy = -1;
        [SerializeField] private bool showHud = true;
        [SerializeField] private bool buildVisuals = true;

        public RunSession Session { get; private set; }
        public ImGuiHud Hud { get; private set; }
        public WorldView View { get; private set; }
        public bool Ready { get; private set; }

        /// <summary>存活实例登记（自动化诊断用：确认场上只有一个 bootstrap 在驱动）。</summary>
        public static readonly System.Collections.Generic.List<GameBootstrap> Live = new System.Collections.Generic.List<GameBootstrap>();
        public static int StartRunCount;

        private readonly GameClock _clock = new GameClock();
        private RunBot _bot;
        private int _restartCount;
        // 镜头跟随焦点（已按板边裁剪）
        private Vector2 _camFocus;

        private void Awake()
        {
            Live.Add(this);
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            try
            {
                TableDatabase.Load();
            }
            catch (Exception e)
            {
                Log.Error("配置表载入失败，游戏不会启动：" + e.Message);
                Ready = false;
                return;
            }

            if (buildVisuals)
            {
                ConfigureCamera();
                ConfigureLight();
                View = new WorldView();
            }
            Hud = new ImGuiHud();
            Ready = true;
        }

        private void Start()
        {
            if (!Ready)
            {
                Log.Error("Bootstrap 未就绪（配置表载入失败），不启动玩法");
                return;
            }
            if (!autoStart) return;
            StartRun((ulong)runSeed);
        }

        public void StartRun(ulong seed)
        {
            StartRunCount++;
            Session?.Dispose();
            Session = new RunSession();
            Session.Start(seed);
            if (buildVisuals && View != null) View.Build(Session);
            _clock.Reset();
            if (botStrategy >= 0) SetBot(botStrategy);
            // v166：田地是固定形状（列 × 行），所以这里报的是**形状 + 格数**，不再只是"耕地 N 块"
            Log.Info($"开局 seed={seed} 阶段={Session.Phase} Bot={(botStrategy < 0 ? "关闭" : ((BotStrategy)botStrategy).ToString())} " +
                     $"初始田地={Session.World.Farm.Columns}列×{Session.World.Farm.Rows}行={Session.World.Farm.UnlockedCount}格 " +
                     $"种子槽={Session.Bag.SlotCount} 每{Session.World.Farm.RowGrowthSeconds:0}s加一排(上限{Session.World.Farm.MaxRows}行)");
        }

        public void RestartRun()
        {
            _restartCount++;
            StartRun((ulong)runSeed + (ulong)_restartCount * 7919UL);
        }

        public void SetBot(int strategy)
        {
            botStrategy = strategy;
            _bot = strategy < 0 || Session == null ? null : new RunBot(Session, (BotStrategy)strategy);
        }

        public RunBot Bot => _bot;

        private void Update()
        {
            if (Session == null) return;

            FrameInput input;
            if (_bot != null)
            {
                input = _bot.Tick(Time.deltaTime);
                Keyboard kb = Keyboard.current;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Session.TogglePause();
            }
            else
            {
                input = ReadInput();
            }

            if (Hud != null)
            {
                Hud.ConsumeInto(ref input);
                if (Hud.PendingRestart)
                {
                    Hud.PendingRestart = false;
                    RestartRun();
                    return;
                }
                // 菜单里的"继续游戏"等价于再按一次 Esc
                if (Hud.PendingResume)
                {
                    Hud.PendingResume = false;
                    Session.TogglePause();
                }
                // 商店"一键合成"按钮 → 把袋里所有同种同级的对子合到 3 级
                if (Hud.PendingMergeAll)
                {
                    Hud.PendingMergeAll = false;
                    int merged = Session.Bag.AutoMergeAll(out string mergeReason);
                    Session.SetPrompt(merged > 0
                        ? $"自动合成 {merged} 次"
                        : (mergeReason ?? $"没有可合成的种子（需同种同级，且未到 {SeedSlot.MaxLevel} 级）"));
                }
            }

            // v53：**帧级一次性输入**必须在追帧循环之前消费掉。
            // _clock.Advance 一个渲染帧最多跑 maxCatchUpTicks(5) 次 FixedTick，且传的是同一个 input；
            // 滚轮（相对增量）与刷新（带花费）放进去会被重复应用——滚轮一格会轮转 5 步、跳过中间那把武器。
            Session.ConsumeFrameEdges(ref input);

            int catchUp = TableDatabase.Tuning != null ? Mathf.Max(1, TableDatabase.Tuning.meta.maxCatchUpTicks) : 5;
            _clock.Advance(Time.deltaTime, catchUp, dt => Session.FixedTick(dt, input));

            if (buildVisuals && View != null) View.Sync(Session, Time.deltaTime);
            UpdateCamera(Time.deltaTime);
        }

        private void OnGUI()
        {
            if (!showHud || Hud == null || Session == null) return;
            Hud.Draw(Session);
        }

        private void OnDestroy()
        {
            Live.Remove(this);
            Session?.Dispose();
            View?.Clear();
        }

        // ------------------------------------------------------------ 输入
        private FrameInput ReadInput()
        {
            var input = new FrameInput
            {
                SelectSeed = -1, SellSlot = -1, BuyOffer = -1, InitialChoice = -1
            };
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;

            Vector2 mouseWorld = MouseToWorld();
            input.AimWorld = mouseWorld;

            if (kb == null) return input;

            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            input.Move = new Vector2(x, y);

            // v23：E 耕作已删除（耕作改为"攻击地块"）
            // v160：E 键改绑【与商店车对话】✓
            // 用 `wasPressedThisFrame`（单次触发）而不是 `isPressed` ✗
            // 理由：开店是"点一下"的动作 ✓ 用 isPressed 会在按住时每帧重复请求，
            // 而 `SetPhase` 每帧被调一次虽然结果相同，但会让"追帧时重复消费"这类问题出现 ✗
            input.ShopOpen = kb.eKey.wasPressedThisFrame;
            // v16：删除 Q 键铲除幼苗（该操作不再提供）
            input.Skip = kb.spaceKey.wasPressedThisFrame;


            // v16：R 键——商店刷新；抽卡界面 = 重抽；Shift+R = 重新开始
            if (kb.rKey.wasPressedThisFrame && Session != null)
            {
                if (shiftKeyDown(kb) && (Session.Phase == PhaseState.Shop || Session.Phase == PhaseState.Paused))
                    Hud.PendingRestart = true;
                else if (Session.Phase == PhaseState.Shop) Hud.PendingReroll = true;
                else if (Session.Phase == PhaseState.LevelReward) Hud.PendingRewardReroll = true;
            }

            // Tab = 显示/隐藏左侧按键说明（v16 修复：上一轮误删导致失灵）
            if (kb.tabKey.wasPressedThisFrame && Hud != null) Hud.KeyHelpVisible = !Hud.KeyHelpVisible;

            // Esc = 打开/关闭菜单（暂停）
            if (kb.escapeKey.wasPressedThisFrame) Session.TogglePause();

            // ── 鼠标：左键攻击、滚轮切换武器、点击 UI
            if (mouse != null)
            {
                input.Fire = mouse.leftButton.isPressed;
                // v10：向下滚 = 下一个武器，向上滚 = 上一个；不受前摇限制
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) input.WheelScroll = scroll > 0f ? -1 : 1;
            }

            // v15：数字键在"同一功能上同时支持鼠标与键盘"（不同界面可重复绑定）
            int digit = ReadDigit(kb);
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (digit > 0 && Session != null)
            {
                switch (Session.Phase)
                {
                    case PhaseState.Shop:
                        if (shift) input.ToggleOfferLock = digit - 1;   // Shift+数字 = 锁定该商品
                        else input.BuyOffer = digit - 1;               // 数字 = 购买对应商品
                        break;
                    case PhaseState.LevelReward:
                        input.RewardSelectSet = true;
                        input.RewardSelect = digit - 1;                // 数字 = 选中卡片
                        break;
                    case PhaseState.Prepare:
                        // v48：开局三选一的按键之前根本没接上（数字键会掉到 default 去选种子位），
                        //      而界面文案一直写着"或按数字键 1–3"——现在真的能按了。
                        input.InitialChoice = digit - 1;
                        break;
                    default:
                        // v48：战斗中 1/2/3 = 锄头 / 水壶 / 肥料
                        input.WeaponSelectSet = true;
                        input.SelectWeapon = digit - 1;
                        break;
                }
            }
            if (Session != null && Session.Phase == PhaseState.LevelReward && kb.spaceKey.wasPressedThisFrame)
            {
                input.RewardPickConfirmed = true;                      // 空格 = 确认选卡
                input.RewardPick = Session.RewardSelected;
            }

            return input;
        }

        private static bool shiftKeyDown(Keyboard kb) => kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;

        private static int ReadDigit(Keyboard kb)
        {
            if (kb.digit1Key.wasPressedThisFrame) return 1;
            if (kb.digit2Key.wasPressedThisFrame) return 2;
            if (kb.digit3Key.wasPressedThisFrame) return 3;
            if (kb.digit4Key.wasPressedThisFrame) return 4;
            if (kb.digit5Key.wasPressedThisFrame) return 5;
            if (kb.digit6Key.wasPressedThisFrame) return 6;
            return -1;
        }

        private static Vector2 MouseToWorld()
        {
            Camera cam = Camera.main;
            Mouse mouse = Mouse.current;
            if (cam == null || mouse == null) return Vector2.zero;
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float enter))
            {
                Vector3 p = ray.GetPoint(enter);
                return new Vector2(p.x, p.z);
            }
            return Vector2.zero;
        }

        // ------------------------------------------------------------ 场景
        private void ConfigureCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            CameraDef cfg = TableDatabase.Tuning.camera;
            cam.orthographic = cfg.orthographic;
            cam.orthographicSize = cfg.orthoSize;
            cam.fieldOfView = cfg.fov > 0f ? cfg.fov : 45f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            _camFocus = Vector2.zero;
            UpdateCamera(1f);
        }

        /// <summary>
        /// 镜头：与地面固定 60° 夹角（不是上帝视角）、方向锁定、拉近跟随角色，
        /// 并把可视范围裁在场地边界内（绝不展示板外）。
        /// </summary>
        private void UpdateCamera(float dt)
        {
            Camera cam = Camera.main;
            if (cam == null || Session == null || Session.World == null || Session.World.Player == null) return;

            CameraDef cfg = TableDatabase.Tuning.camera;
            cam.orthographic = cfg.orthographic;
            cam.orthographicSize = cfg.orthoSize;
            cam.fieldOfView = cfg.fov > 0f ? cfg.fov : 45f;

            Vector2 half = VisibleGroundHalfExtents(cam, cfg);
            float limit = Mathf.Max(0f, TableDatabase.Tuning.farm.arenaHalf);
            float limX = cfg.clampToArena ? Mathf.Max(0f, limit - half.x) : limit * 10f;
            float limZ = cfg.clampToArena ? Mathf.Max(0f, limit - half.y) : limit * 10f;

            Vector2 target = Session.World.Player.Pos;
            var focus = new Vector2(Mathf.Clamp(target.x, -limX, limX), Mathf.Clamp(target.y, -limZ, limZ));
            float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, cfg.followLerp) * Mathf.Max(0f, dt));
            _camFocus = Vector2.Lerp(_camFocus, focus, dt <= 0f ? 1f : k);

            float pitchRad = Mathf.Max(5f, cfg.pitch) * Mathf.Deg2Rad;
            Vector3 look = new Vector3(_camFocus.x, 0f, _camFocus.y);
            float dist = cfg.orthographic ? Mathf.Max(1f, cfg.height) : Mathf.Max(3f, cfg.distance);
            Vector3 offset = new Vector3(0f, Mathf.Sin(pitchRad), -Mathf.Cos(pitchRad)) * dist;
            cam.transform.position = look + offset;
            cam.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        }

        /// <summary>当前镜头能看到的地面半宽/半深（透视按机位距离与 FOV 估算，正交按尺寸）。</summary>
        private static Vector2 VisibleGroundHalfExtents(Camera cam, CameraDef cfg)
        {
            float halfH;
            if (cfg.orthographic)
            {
                halfH = cam.orthographicSize;
            }
            else
            {
                float fov = Mathf.Max(10f, cfg.fov > 0f ? cfg.fov : 45f) * Mathf.Deg2Rad;
                halfH = Mathf.Max(1f, cfg.distance) * Mathf.Tan(fov * 0.5f);
            }
            float halfW = halfH * Mathf.Max(0.1f, cam.aspect);
            float sinPitch = Mathf.Max(0.1f, Mathf.Sin(Mathf.Max(5f, cfg.pitch) * Mathf.Deg2Rad));
            // v44：俯视透视下"屏幕下缘可见的地面纵深"比对称估算更小，
            //      直接用 halfH/sin 会让镜头把竞技场底边推到屏幕外（表现为看不到底部边界）。
            //      这里取"焦点下方真正可见的纵深" = halfH × (1/sin − 1/tan)，保证底边永远在画面内。
            float tanPitch = Mathf.Max(0.1f, Mathf.Tan(Mathf.Max(5f, cfg.pitch) * Mathf.Deg2Rad));
            float below = halfH * (1f / sinPitch - 1f / tanPitch);
            float halfZ = cfg.orthographic ? halfH / sinPitch : Mathf.Max(halfH * 0.25f, below);
            return new Vector2(halfW, halfZ);
        }

        private void ConfigureLight()
        {
            // v74：光照整体交给 NightLighting —— 夜晚环境、月亮阴影、角色火焰、特效光池。
            // 旧版这里直接配"正午阳光 + 亮环境光"，和现在要的半夜暗光完全相反，
            // 所以不再保留两处配置（两处都写光照参数必然会不同步）。
            NightLighting.Ensure();
        }
    }
}
