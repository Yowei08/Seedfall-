using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

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
        /// <summary>
        /// ★ v214：**开局那一刻的 RNG 指纹**（只读诊断，判"回主城再进来是不是新一局"用）★
        /// 取法 `World.Rng.Fork(seq).NextUInt()` —— `Fork` 派生一条**独立流**，
        /// 按 `Pcg32.Fork` 的约定"不得消费逻辑流"，所以读它对本局结果零影响 ✓
        /// 判据：同一个 `runSeed` 的两局，这个数必须**逐位相同**（= RNG 是从种子重建的，
        /// 没有继承上一局的状态）；退出战斗那条路径**一个随机数都不掷** ✓
        /// </summary>
        private uint _rngFpAtStart;
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
            // ★ v203：园丁光标（用户原话："为游戏设计一个特别的鼠标，替换现有鼠标"）★
            //   为什么挂成**组件**而不是在这里直接调 `Cursor.SetCursor`：
            //   它需要一个 `MonoBehaviour` 才能收到 `OnApplicationFocus` —— 失焦时把系统光标还回去。
            //   为什么把 `Hud` 交给它：悬停判定**必须**用 HUD 已有的命中测试（见 `ImGuiHud.UiHover`），
            //   在这里多存一份"哪些地方可点"的清单就变成第二套真相 ✗
            //   ⚠️ 它**不用** `buildVisuals` 管：光标与 3D 表现无关，关掉模型也照样该有 ✓
            GardenCursor gardenCursor = GetComponent<GardenCursor>();
            if (gardenCursor == null) gardenCursor = gameObject.AddComponent<GardenCursor>();
            gardenCursor.Hud = Hud;
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
            // ★ v214：开局那一刻的 RNG 指纹（见字段说明；不消耗逻辑流）
            _rngFpAtStart = Session.World.Rng.Fork(0x5EEDUL).NextUInt();
            if (buildVisuals && View != null) View.Build(Session);
            _clock.Reset();
            if (botStrategy >= 0) SetBot(botStrategy);
            // v166：田地是固定形状（列 × 行），所以这里报的是**形状 + 格数**，不再只是"耕地 N 块"
            // ★ v173：这一行原来写"每{RowGrowthSeconds:0}s加一排(上限{MaxRows}行)"✗
            // ⇒ 配置是 300 秒 ✓ 所以它报的是"每300s加一排"✗ —— ★ 而用户要的说法是【分钟】★ ✓
            // ⇒ 改成分钟，并把上限展开成"行×列=格"✗ 因为用户的要求就是"上限 6×6"✓
            // （★ 数值一个字节没动 ✗ 只改单位与写法 ✓ —— `tuning.json` 早就是 maxRows 6 / 300 秒 ✓）
            Log.Info($"开局 seed={seed} 阶段={Session.Phase} Bot={(botStrategy < 0 ? "关闭" : ((BotStrategy)botStrategy).ToString())} " +
                     $"初始田地={Session.World.Farm.Columns}列×{Session.World.Farm.Rows}行={Session.World.Farm.UnlockedCount}格 " +
                     $"种子槽={Session.Bag.SlotCount} 每{Session.World.Farm.RowGrowthSeconds / 60f:0.#}分钟加一排" +
                     $"(上限{Session.World.Farm.MaxRows}行×{Session.World.Farm.Columns}列={Session.World.Farm.MaxRows * Session.World.Farm.Columns}格)");
        }

        public void RestartRun()
        {
            _restartCount++;
            StartRun((ulong)runSeed + (ulong)_restartCount * 7919UL);
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v214：**退出战斗（回到主城）** ★★
        //   用户原话："战斗模式的菜单增加退出战斗（回到主城）"
        //
        //   ── 目标场景为什么是 `Town` 而不是 `Login` ✗ ──
        //     用户要的是"回到主城"：主城就是 `Assets/Scenes/Town.unity`；
        //     `Login` 是**开始界面**（登录那路的「返回开始界面」才是回 Login）。
        //     回 `Login` 等于把玩家踢回标题页，不是这条需求 ✓
        //     名字取**既有常量** `LoginBootstrap.TownSceneName`（`public const`）——
        //     战斗侧再写一份 `"Town"` 就会有两个真值源，改场景名时只改一处的那一半会静默失效 ✗
        //
        //   ── "退出前结算"落到了什么 ──
        //     ① **存档**：本工程**没有战斗存档** —— 一局的进度只活在 `RunSession` 里
        //        （`World`/`Bag`/`Goods`/`Progress`，全是运行时对象）。
        //        主城存档的唯一写者是 `LoginBootstrap.SaveCurrent`（`Login/**`，本轮禁改），
        //        而进战斗是**Single 场景加载** ⇒ 它随 Login 场景一起被销毁，战斗期根本不在场。
        //        更关键的是：**"当前存档位"只存在于它的私有字段 `_currentSlot`**，
        //        战斗侧无从得知；去猜一个位号写盘（例如 `SaveStore.Newest`）会覆盖玩家**别的**档
        //        ⇒ 那是比"不存档"坏得多的失败，所以这里**不写盘**，只把这件事写进日志 ✓
        //     ② **清局**：`Dispose()`（退订 `Bus` 事件 —— 它是**静态**的，不退订会带进下一局）＋
        //        `View.Clear()` ＋ 会话置空 ⇒ 之后 `Update/OnGUI` 立刻失效，不会再往外发一帧输入 ✓
        //        `OnDestroy` 里再调一次是无害的：`Bus.Unsubscribe` 幂等、`?.` 对 null 短路 ✓
        //     ③ **`World.Rng` 不碰**：整条退出路径**零随机数消耗**（不掷、不重播种）。
        //        下一局的 `World`/`Rng` 由 `StartRun(seed)` 重新构造；
        //        新 GameBootstrap 实例 ⇒ `_restartCount` 归零 ⇒ 仍是同一个 `runSeed` ✓
        //
        //   ── 二次确认 ──
        //     第一次点只进确认态（`Hud.ExitToTownArmed`），第二次点才真的走 `ExitToTownNow` ✓
        //     按钮与自动化入口（`DoExitToTown`）**共用这一处判定**，不会出现两套语义 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>「退出战斗（回到主城）」的处理函数：第一次点进确认态，第二次才真的退出。</summary>
        private void HandleExitToTown()
        {
            if (Hud == null) return;
            if (!Hud.ExitToTownArmed)
            {
                Hud.ExitToTownArmed = true;
                Log.Info("[退出战斗] 第一次点击 ⇒ 进入确认态（再点一次「确认退出？再点一次」才真的回主城）");
                return;
            }
            Hud.ExitToTownArmed = false;
            ExitToTownNow();
        }

        /// <summary>真的退出：存档结论 → 清局 → `LoadScene(Town)`。见上面整段说明。</summary>
        private void ExitToTownNow()
        {
            // ① 存档：战斗进度不入盘，理由见上（没有战斗存档 + 没有"当前存档位"这个信息）
            //
            // ★★ v254：**但 BOSS 掉的那颗种子必须在退出前确认已经在盘上** ★★
            //   用户报「击败 boss 获取的种子没有实际带到主城仓库」。
            //   修法是"击杀的那一刻就写盘"（`TownDrops.OnBossKilled` → `TownDropLedger`），
            //   所以正常情况这里**无事可做**（待发队列是空的）。
            //   这里再补一次是给"击杀时那一次写盘失败（位号未知 / 磁盘忙）"兜底 ——
            //   退出是这条链路上的**最后一个窗口**：过了这里场景就被卸载，没人再能写 ✓
            //   （幂等：盘上已有的键只结账、不重复加种子）
            TownDropLedger.Flush("退出战斗");
            Log.Info("[退出战斗] 存档：战斗进度不入盘（本工程无局内存档；主城存档的写者 `LoginBootstrap.SaveCurrent` " +
                     "在进战斗时已随 Login 场景销毁，且它持有唯一的「当前存档位」）" +
                     "；BOSS 掉落另走 `TownDropLedger`（击杀当场写进主城存档位）⇒ " + TownDropLedger.Describe());

            // ② 清局：事件总线的订阅（静态）、3D 表现、会话本身，以及菜单的确认态
            if (Session != null)
            {
                Session.Paused = false;
                Session.Dispose();
                Session = null;
            }
            if (View != null)
            {
                View.Clear();
                View = null;
            }
            _bot = null;
            _clock.Reset();
            if (Hud != null)
            {
                Hud.PendingExitToTown = false;
                Hud.ExitToTownArmed = false;
            }

            // ③ 不碰 `World.Rng`：上面这一段一个随机数都没有

            // ★★ v251：回主城要回到 **Login 场景**，不是直接 `LoadScene("Town")` ★★
            // 用户报"从战斗模式退出回到主城后，esc 会失效"。
            // 根因（取证）：这套架构里 `Login` 场景是**常驻**的 ——
            //   · `LoginBootstrap.cs:149` 用 `LoadSceneAsync("Town", Additive)` 把主城**叠**在登录场景上；
            //   · 主城的 **Esc 设置面板**由 `LoginBootstrap.Update()` 的 `case Stage.Playing` 处理
            //     （主城自己的 `TownPanel` 只有 仓库/选种/商店/服装店/图鉴，**没有设置面板**）。
            //   · 而这里原来是 `SceneManager.LoadScene(townScene)`（**单场景**）⇒ 把 `Login`
            //     连同 `LoginBootstrap` **一起卸载** ⇒ 回到主城后没人接 Esc ⇒ 按了没反应 ✗
            // ⇒ 改成回到登录场景，由它重新把主城 additive 加载起来，Esc/设置/存档都恢复 ✓
            const string loginScene = LoginBootstrap.LoginSceneName;
            const string townScene = LoginBootstrap.TownSceneName;   // 唯一真值源
            if (!Application.CanStreamedLevelBeLoaded(loginScene))
            {
                Log.Error($"[退出战斗] 场景 {loginScene} 不在 Build Settings 里，退出战斗失败（本局已清空）");
                return;
            }
            Log.Info($"[退出战斗] → LoadScene({loginScene})（回登录场景，由它把 {townScene} 重新 additive 叠起来；v251 修复 Esc 失效）");
            LoginBootstrap.AutoEnterSlot = LoginBootstrap.ActiveSlot;   // v251：回主城时读回这个存档位
            SceneManager.LoadScene(loginScene);
        }

        /// <summary>
        /// 桥/自动化入口：**与按钮同一条路**（`Do return` 之于登录那路的同形态）。
        /// 第一次调用 ⇒ 进确认态；第二次调用 ⇒ 真的回主城并返回当时的场景名 ✓
        /// </summary>
        public string DoExitToTown()
        {
            if (Hud == null) return "HUD 未就绪（Awake 还没跑完）";
            HandleExitToTown();
            if (Hud.ExitToTownArmed)
                return "⚠ 已进入确认态（再点一次「确认退出？再点一次」/ 再发一次 Do exit 才真的回主城）";
            // ★ 不要在切场景的同一帧里报"scene 现在是 Town" ✗
            //   实测：`SceneManager.LoadScene` 的**切换发生在帧末** —— 同一帧读 `GetActiveScene().name`
            //   仍然是旧场景名（上一版这里就报出了 `scene=SeedfallArena`，看着像"没切"）。
            //   ⇒ 两个读数分开报，**都不撒谎**：同帧能确定的只有"已发起"与"目标场景是否已在加载"
            const string townScene = LoginBootstrap.TownSceneName;
            return $"已退出战斗 → LoadScene({townScene}) 已发起"
                 + $"｜同帧 activeScene={SceneManager.GetActiveScene().name}（帧末才切换，属正常）"
                 + $"｜{townScene}.isLoaded={SceneManager.GetSceneByName(townScene).isLoaded}"
                 + "｜请再发一次读场景的调用确认已在 Town";
        }

        /// <summary>桥/自动化入口：**打开战斗菜单**（等价于按一次 Esc）。只切暂停位，不做别的。</summary>
        public string OpenMenu()
        {
            if (Session == null) return "无会话（Session=null）";
            Session.TogglePause();
            return "Paused=" + Session.Paused + "｜phase=" + Session.Phase;
        }

        /// <summary>
        /// 桥/自动化入口：**这一局的读数**（判"回主城再进来是不是新一局"用）。
        /// · `rngFp`  = 开局那一刻的 RNG 指纹（同种子两局必须相同 ⇒ 见字段说明）
        /// · `rngNow` = **此刻**的 RNG 指纹 —— 由 `Fork` 派生独立流后取一个数，
        ///   按 `Pcg32.Fork` 的约定**不消耗逻辑流** ✓ 所以可以随时读、不会影响本局结果；
        ///   它的用途是证明"退出战斗那条路径一个随机数都没掷"（点第一次前后它必须**逐位相同**）✓
        /// </summary>
        public string BattleReport()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (Session == null) return $"scene={scene}｜Session=null（已退出战斗 / 未开局）";
            RunSession s = Session;
            uint rngNow = s.World.Rng.Fork(0xABCDUL).NextUInt();
            return $"scene={scene}｜seed={s.Seed}｜phase={s.Phase}｜wave={s.WaveIndex}｜gold={s.Gold}" +
                   $"｜paused={s.Paused}｜startRuns={StartRunCount}｜rngFp={_rngFpAtStart:X8}｜rngNow={rngNow:X8}" +
                   $"｜bot={(_bot == null ? "关" : "开")}｜退出确认态={(Hud != null && Hud.ExitToTownArmed)}";
        }

        /// <summary>
        /// 桥/自动化入口：**菜单这一帧画出的按钮清单**（证明按钮真的被画出来、文本是什么）。
        ///
        /// ★ 为什么需要它（而不是截图）★
        ///   `unity_look` 抓的是**相机渲染**，IMGUI（`OnGUI`）**根本不在那张图里** ⇒
        ///   截图无法证明"菜单里有个「退出战斗（回到主城）」按钮"✗
        ///   而 `ImGuiHud.ButtonTexts` 是 `Btn()` 这一趟真的落笔的文本（与 `ButtonRects` 同下标）
        ///   ⇒ 它是"可见性 + 文本正确"的**运行时真值** ✓
        /// ⚠️ `Update` 在 `OnGUI` 之前跑 ⇒ 读到的是**上一帧** OnGUI 的结果 ✓
        /// </summary>
        public string ReportMenu()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("paused=").Append(Session != null && Session.Paused)
              .Append("｜armed=").Append(Hud != null && Hud.ExitToTownArmed)
              .Append("｜上一帧画出的按钮：");
            System.Collections.Generic.IReadOnlyList<string> texts = ImGuiHud.ButtonTexts;
            bool any = false;
            for (int i = 0; i < texts.Count; i++)
            {
                if (string.IsNullOrEmpty(texts[i])) continue;
                if (any) sb.Append(" ／ ");
                sb.Append('[').Append(texts[i]).Append(']');
                any = true;
            }
            if (!any) sb.Append("（无）");
            return sb.ToString();
        }

        public void SetBot(int strategy)
        {
            botStrategy = strategy;
            _bot = strategy < 0 || Session == null ? null : new RunBot(Session, (BotStrategy)strategy);
        }

        public RunBot Bot => _bot;

        /// <summary>
        /// 诊断开关：抽卡探针（见 `ImGuiHud.RewardProbe`）。
        /// 只 `Log.Info`，不改任何玩法状态 ⇒ 开着也不会影响一局的结果 ✓
        /// 打开方式（play 模式）：`unity_drive send GameBootstrap GameBootstrap ToggleRewardProbe`
        /// </summary>
        public void ToggleRewardProbe()
        {
            ImGuiHud.RewardProbe = !ImGuiHud.RewardProbe;
            Log.Info("[RewardProbe] " + (ImGuiHud.RewardProbe ? "开" : "关"));
        }

        private void Update()
        {
            if (Session == null) return;

            FrameInput input;
            if (_bot != null)
            {
                input = _bot.Tick(Time.deltaTime);
                // v251：统一走 `EscKey`（上升沿锁存 ⇒ 不漏帧；`Keyboard.current == null` 时也不会整条短路）
                if (EscKey.Pressed()) Session.TogglePause();
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
                // ★ v214：**退出战斗（回到主城）** —— 按钮只置请求位，判定与切场景在这里 ★
                //   放在 `PendingRestart`/`PendingResume` 之后：同一帧里"先继续游戏、再退出"
                //   这种顺序不会被前一个分支抢走 ✓
                if (Hud.PendingExitToTown)
                {
                    Hud.PendingExitToTown = false;
                    HandleExitToTown();
                    return;      // 本帧到此为止：`Session` 可能已经被置空（退出成功时）
                }
                // ★ v198（用户原话："删除商店一键合成按键"）★
                //   这里原来消费 `Hud.PendingMergeAll` → `Session.Bag.AutoMergeAll(...)`，
                //   而商店里那个按钮是它的**唯一**写者 ⇒ 按钮删了，这段消费也就没有入口了，
                //   一并删掉（否则是一条"每帧判断一个永远为 false 的开关"的死代码）✓
                //   ⚠️ `SeedBag.AutoMergeAll` 本身**没删**：自检有三条断言直接调它 ✓
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

        /// <summary>
        /// ★ v254：**退出游戏（含 Alt+F4 / 关窗口）时把 BOSS 掉落补一次** ★
        ///
        /// 为什么需要它：击杀当场已经写过盘（`TownDropLedger`），但游戏里有四条退出路径 ——
        /// 正常退回主城、主动退出、死亡、直接关窗口。前两条走 `ExitToTownNow`（那里也补一次），
        /// **关窗口不走任何场景切换** ⇒ 只有 `OnApplicationQuit` 这一个窗口。
        /// 只写"待发队列里还剩的"（幂等），正常情况这里是空操作 ✓
        /// </summary>
        private void OnApplicationQuit()
        {
            TownDropLedger.Flush("退出游戏");
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
            // ★ v304 纯插入（唯一一行；不成立时这一行等于 `if (false)`）★
            //   训练场里 E 也用来开关**实体仓库**（`TrainingWarehouse`）——
            //   面板开着 / 站在仓库门口时，E 不该**同时**再请求商店车（会弹出第二层界面）。
            //   `EKeyTakenByWarehouse` 在战斗场景的取值链（`TrainingWarehouse.cs`）：
            //     ① `TrainingMode.Active == false` ⇒ 第一行就 return false（战斗场景无 TrainingBootstrap）
            //     ② 即便走到后面，`_instance == null` ⇒ 也 return false（组件根本不存在）
            //   ⇒ 战斗模式下这一行**永不执行** ⇒ `input.ShopOpen` 保持上一行赋给它的值
            //   ⇒ 与改前逐字节相同 ✓（无任何既有语句被改写或删除）
            if (TrainingWarehouse.EKeyTakenByWarehouse) input.ShopOpen = false;
            // v16：删除 Q 键铲除幼苗（该操作不再提供）
            // ★★ v215：**Q 键 = 换种**（战斗模式）★★
            //   用户原话："战斗模式下的植物存在此 bug，没有同步更新，更换了种子都没改变耕地"
            //   改前 **"换种"这个动作根本不存在**（取证见 `RunSession.ReplaceSeedOnPlot` 上面那段：
            //   `Bag.Selected` 在人类输入里没有写入口 ⇒ 连"选中了哪颗种子"都改不了）✗
            //   现在 Q = 把最近地块上的作物换成袋里下一个不同的种子（旧种子退回 / 新种子扣掉）✓
            //   键位不复用任何现行绑定：v16 起 Q 一直空着（那时是"铲除幼苗"，已删除）✓
            //   用 `wasPressedThisFrame`（一次性脉冲），理由同 E 键 ✓
            input.ReplaceSeed = kb.qKey.wasPressedThisFrame;
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

            // Esc = 打开/关闭菜单（暂停）—— v251：统一走 `EscKey`（锁存上升沿，防漏帧/设备丢失）
            if (EscKey.Pressed()) Session.TogglePause();

            // ── 鼠标：左键攻击、滚轮切换武器、点击 UI
            if (mouse != null)
            {
                input.Fire = mouse.leftButton.isPressed;
                // ★ v305 纯插入（训练场仓库三栏 UI；**不改上面那一行，也不删任何既有语句**）★
                //   训练场里仓库面板是一整屏的点击界面（左栏选条目 / 各栏卸下按钮），
                //   而 IMGUI 的按钮**不会**拦掉 `input.Fire` ⇒ 每点一下都会顺带挥一次锄头。
                //   本行把面板打开期间的左键从"攻击"里摘出来。
                //   `TrainingVault.ConsumesMouse` 的取值链：`TrainingMode.Active && IsOpen`
                //     · 战斗场景（`SeedfallArena.unity`）里没有 `TrainingBootstrap` ⇒
                //       `TrainingMode.Active` 恒为 false（`TrainingMode.cs:50`，`Enable()` 只有它能置位）
                //       ⇒ 第一项就短路 ⇒ 属性恒 false ⇒ 本行等于 `if (false) …`
                //     ⇒ `input.Fire` **保持上一行赋给它的值**，与改前逐字节相同 ✓
                if (TrainingVault.ConsumesMouse) input.Fire = false;
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
