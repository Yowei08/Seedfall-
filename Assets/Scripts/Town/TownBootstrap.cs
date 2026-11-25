using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城（Town）的唯一宿主 —— `Assets/Scenes/Town.unity` 里只有它一个对象 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么整座城是**代码建的**，而不是在场景里摆好
    /// ══════════════════════════════════════════════════════════════════════
    /// 与 `GameBootstrap` 同一条理由（那是本工程的既有约定：架构案 A-2 "代码运行时构建，保证可复现"）：
    ///   · 36 块地、六座建筑、8 根路灯的坐标全部来自 `TownLayout` 的**一组常量**，
    ///     交付里报的坐标与场景里跑出来的坐标**是同一份**，不会"文档写着 A、场景摆成 B"✓
    ///   · `Town.unity` 因此只有 1 个对象、**零预制体引用** ⇒ 加进去不会污染工程里的任何既有资源 ✓
    ///   · 「不要修改原有战斗内容」：本文件不 import / 不引用 `Combat` `Actors` `Flow` 里的任何类型，
    ///     主城与战斗唯一的耦合是"传送门按名字 LoadScene 回去"✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、布局（★ v214：**圆形大公园**，坐标全部由 `TownLayout` 给 ★）
    /// ══════════════════════════════════════════════════════════════════════
    ///   世界 +X = 屏幕右、+Z = 屏幕上（与战斗场景同向，见 `TownAvatar` 第三节）
    ///
    ///                  传送门 90°(r=11.8)
    ///                 ·————— 外环路 r 8.35~9.50 —————·
    ///        仓库 205°  │    田圃环 r 5.50~8.30        │  服装店 25°
    ///        (r=11.5)   │   （36 块地 = 2 圈 × 18）    │  (r=11.6)
    ///                   │  内环路 r 4.20~5.30          │
    ///                   │  草坪环 r 2.70~4.20          │  商店 322°
    ///                   │  ┌ 中央广场 r≤2.70 + 喷泉 ┐   │  (r=11.55)
    ///                   ·————— 集市帐篷 240° ————————·   南门 270°(r=12.9)
    ///                        ↑ 玩家从南侧 (0,-9.4) 进城，面朝圆心
    ///
    /// ⚠️ 上一版是"方形 6×6 农田 + 方形环路 + 东向商业街（6 栋同尺寸房子）"——
    ///    用户点名要推翻的就是它（"突兀的地块""高度重复的排列和模型"）✗
    ///    这一版：**同心圆 + 两圈交错的花瓣状田圃 + 六座互不相同的建筑**。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、`E` 交互：**一个最近可交互物**，判定半径见各自的 `*Radius` 常量
    /// ══════════════════════════════════════════════════════════════════════
    /// 每帧算一次"玩家到各锚点的距离 ÷ 该锚点半径"，取**归一化距离最小**的那个
    ///（不是取绝对距离最小 —— 大门半径 3 米、耕地半径 1.1 米，用绝对距离会让站在耕地上的人
    /// 反而被 2 米外的门抢走焦点）✓ 于是"站在耕地上按 E"永远落在耕地这条分支上 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、这一轮改了什么（对照用户原话）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① 圆形结构 / 整体是一个大公园 / 中心是公园中央 …… `TownLayout` 的同心环 + 中央喷泉 ✓
    ///   ② 射线检测碰撞、禁止穿透所有建筑 …………………… `TownAvatar.MoveWithCollision`（胶囊扫掠）+ 逐零件 Collider ✓
    ///   ③ 所有建筑单独建模、拒绝高度重复 …………………… `TownBuildings` 一栋一函数、屋顶形状各不相同 ✓
    ///   ④ 商店与服装店拆成两栋楼、两个锚点 ……………… `ShopAnchor` / `ClothingAnchor`，各一个面板 ✓
    ///   ⑤ 商店不卖种子（只卖道具/插件/成品）…………… `town_shop.json` + `TownGoods` ✓
    ///   ⑥ 种子只能靠 BOSS 掉落、移除开局供给 …………… `TownState.StarterStock` 全 0 + 仓库面板红字 ✓
    ///   ⑦ 摄像机拉近 ………………………………………………… `FollowOrthoSize 9.5→8.6`、机位距离 `34→26`、俯视档 `21→14` ✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TownBootstrap : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════
        // 一、布局（★ 全部转发到 `TownLayout`：坐标只有一个来源 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>地块**切向**宽（米）。</summary>
        public const float PlotSize = TownLayout.PlotWidth;

        /// <summary>路面 / 耕地薄板的上表面高度（玩家脚底 = 这个值）。</summary>
        public const float SlabTopY = 0.02f;

        /// <summary>耕地薄板再抬的一点点（避免与田圃土面 z-fighting）。作物站在这层上。</summary>
        public const float PlotTopY = 0.035f;

        // ── 四个可交互地标的锚点与判定半径（★ 商店与服装店是两个不同的锚点 ★）──
        public static readonly Vector2 WarehouseAnchor = TownLayout.WarehouseAnchor;
        public const float WarehouseRadius = 2.4f;
        public static readonly Vector2 PortalAnchor = TownLayout.PortalAnchor;
        public const float PortalRadius = 2.9f;   // ★ v257：判定挪到门上后略放宽（门洞宽 4.95）
        public static readonly Vector2 ShopAnchor = TownLayout.ShopAnchor;
        public const float ShopRadius = 2.4f;
        public static readonly Vector2 ClothingAnchor = TownLayout.ClothingAnchor;
        public const float ClothingRadius = 2.4f;
        /// <summary>家（图鉴）的锚点与判定半径。</summary>
        public static readonly Vector2 HomeAnchor = TownLayout.HomeAnchor;
        public const float HomeRadius = 2.2f;
        public const float PlotRadius = 1.10f;

        /// <summary>玩家出生点：南侧外环路上，面朝圆心（正北）。</summary>
        public static readonly Vector3 SpawnPos = TownLayout.SpawnPos;

        /// <summary>小地图取景（世界 x/z）。</summary>
        public static readonly Rect MapWorldRect = TownLayout.MapWorldRect;

        /// <summary>回战斗时加载的场景名（与 `ProjectSettings/EditorBuildSettings.asset` 里的名字一致）。</summary>
        public const string BattleSceneName = "SeedfallArena";

        /// <summary>
        /// 训练场场景名（`Assets/Scenes/Training.unity`，已在 Build Settings 里）。
        /// ★ v258：与 `LoginBootstrap.ActTraining()` 里的 `TrainingScene` 是**同一个场景**，
        /// 所以两条入口用的是同一份判定（`Application.CanStreamedLevelBeLoaded`）与同一个训练场实现 ✓
        /// </summary>
        public const string TrainingSceneName = "Training";

        /// <summary>第 <paramref name="index"/> 块耕地的世界中心 (x/z)。（`TownLayout` 是唯一实现）</summary>
        public static Vector2 PlotCenter(int index) => TownLayout.PlotCenter(index);

        // ══════════════════════════════════════════════════════════════════
        // 二、运行时字段
        // ══════════════════════════════════════════════════════════════════
        private TownState _state;
        private TownAvatar _avatar;
        private Camera _cam;
        /// <summary>主平行光（v218：交给 `TownLife` 做日夜，所以要在本类里留一个引用）。</summary>
        private Light _sunLight;
        private TownPanel _panel = TownPanel.None;
        private int _panelPlot = -1;
        private string _dataError;

        private Transform _root;
        private Transform _propRoot;
        private Transform _plantRoot;

        private readonly PlotView[] _plotViews = new PlotView[TownState.PlotCount];

        private bool _topView;
        private int _lastPoolCount = -1;

        private ActKind _actKind = ActKind.None;
        private int _actPlot = -1;
        private string _actHint;

        // 自动化：按方位角连续走一段（用来验收"射线碰撞挡住了没有"）
        private Vector2 _walkDir;
        private float _walkLeft;

        /// <summary>主城场景自己的日志前缀（与 `[Seedfall]` 同风格，但能一眼分辨来自主城）。</summary>
        private const string Tag = "[Town] ";

        private enum ActKind { None, Warehouse, Portal, Shop, Clothing, Home, Plot }

        private sealed class PlotView
        {
            public int Seed = -1;
            public GameObject Root;
            public Transform Stem;
            public Transform Head;
            public GameObject MatureMark;
        }

        // ══════════════════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;

            try
            {
                TownData.Load();
                _state = TownState.Ensure();
                TownDrops.TryLoad();            // BOSS 掉落表（拿不到就只是"不掉"，不影响建城）
                TownSkins.TryLoad();            // 皮肤表（服装店用）
                TownUnlocks.Load();             // 商店的解锁表（拿不到就只是"商店空着"，不影响建城）
                TownCodex.Load();               // 图鉴（只读战斗那三张表；读不到会降级，不抛）
            }
            catch (System.Exception e)
            {
                _dataError = TownData.LastError ?? e.Message;
                Log.Error(Tag + "主城启动失败（配置表）：" + _dataError);
                BuildCamera();
                BuildLight();
                return;
            }

            _lastPoolCount = ToonLook.MaterialPoolCount;
            BuildScene();
            Log.Info(Tag + $"主城已构建（圆形城界 + 西侧三区）：{TownState.PlotCount} 块耕地（{TownLayout.PlotCols}×" +
                           $"{TownLayout.PlotRows} 方格）· 建筑 6 座 · 城半径 {TownLayout.TownRadius:0.0} 米 · " +
                           $"种子 {TownData.Seeds.Length} 种 · " +
                           $"商店解锁表 {TownUnlocks.Count} 条（上架 {TownUnlocks.OnSaleCount} 条）· " +
                           $"共享材质池 {_lastPoolCount} → {ToonLook.MaterialPoolCount} · " +
                           $"形状网格 {TownKit.ShapeCacheCount}");
            Log.Info(Tag + _state.Snapshot());
            Log.Info(Tag + "商店：" + TownUnlocks.Describe());
        }

        /// <summary>
        /// ★ v237：商业街杂物**在自己的协程里扫** —— 这是"刻意不依赖 `Update`" ★
        ///   为什么光在 `Update` 里每 90 帧不够（实测抓到的**第二个病根**）：
        ///     · `TownArtDresser`（`DefaultExecutionOrder(300)`）在 `Start` 里才挂 `DEC_*`，
        ///       而 `BuildLife` 那一次在 `Awake` 里 —— **早于它** ⇒ 首扫必然是 0；
        ///     · 登录页把主城当**背景**，而 `Login/LoginBootstrap.cs:197` 在登录阶段执行
        ///       `_town.enabled = false` ⇒ **`TownBootstrap.Update` 整帧不跑**。
        ///       实测：在登录阶段停了 15 秒，`DEC_*` 一个都没被停用（累计读数 0），
        ///       直到我手动调 `ClutterInfo` 才一次停掉 11 个。
        ///   而 Unity 的**协程不会因为组件 `enabled = false` 而停**（只有 `SetActive(false)`/销毁才停）
        ///   ⇒ 重扫放进协程：登录背景里照扫，进城后照扫 ✓（`Update` 里那条 90 帧保留，见 `Update`）
        /// </summary>
        private void Start()
        {
            StartCoroutine(RoadClutterSweep());
        }

        private System.Collections.IEnumerator RoadClutterSweep()
        {
            // 等美术通道（order 300 的 `Start`）把 `DEC_*` 挂完，再扫第一遍
            yield return null;
            yield return null;
            StripRoadClutter(true);                     // 首轮：**有没有都打一行**（这就是"是否生效"的判据）
            while (true)
            {
                for (int i = 0; i < 90; i++) yield return null;    // ★ 每 90 帧重扫一次 ★
                StripRoadClutter(false);
            }
        }

        private void Update()
        {
            if (_dataError != null) return;
            if (_avatar == null) return;      // BuildScene 中途抛过异常 ⇒ 不要在这里再 NRE 一次

            float dt = Mathf.Min(Time.deltaTime, 0.25f);

            ReadInput(dt);

            // ★ 金币与成熟都从这里走（`TownState.Advance` 是唯一实现）★
            _state.Advance(dt);

            SyncPlots();
            UpdateActTarget();
            UpdateCamera();
            TownLife.Tick(dt);            // ★ v218：人/动物/鸟/风/云/日夜/雨（全部靠固定相位算，不碰 Rng）★
            TownFarmPark.Tick(dt);        // ★ v227：公园中心 3 个人的呼吸 + 转头（**位置一帧都不写**）★
            // ★ v224：脚步灰尘 —— 只在**真的在走**时生成；建池是"第一次调用时懒建"（一行，不占建城时间）★
            if (_avatar != null)
            {
                if (_root != null) TownDust.Build(_root);
                TownDust.Tick(_avatar.Position, _avatar.Moving, dt);
            }
            // ★ v233：商业街杂物 —— 美术那路在 `Start` 里后挂的 `ENV_*` 也要扫到 ⇒ 每 90 帧再清一次 ★
            if (Time.frameCount % 90 == 0) StripRoadClutter(false);
            // ★ v236：椅子/喷泉 —— 美术那路会在 `Start` 之后重设材质/贴图 ⇒ 每 30 帧重扫并**修回** ★
            if (Time.frameCount % 30 == 0) UnshareSpecialRenderers(false);
        }

        private void OnGUI()
        {
            if (_dataError != null)
            {
                TownHud.Begin();
                TownHud.DrawError(_dataError);
                return;
            }
            if (_topView) return;         // 俯视图截图时隐藏 UI（见 SetView）

            TownHud.Begin();
            TownHud.MapWorldRect = MapWorldRect;
            TownHud.DrawTopBar(_state, TownState.PlotCount);
            TownHud.DrawHint(_actHint);

            switch (_panel)
            {
                case TownPanel.Warehouse:
                    if (TownHud.DrawWarehouse(_state)) ClosePanel();
                    break;
                case TownPanel.SeedPicker:
                    // 兜底：地块序号非法就不要画（否则 DrawSeedPicker 会拿 -1 去索引数组）
                    if (_panelPlot < 0 || _panelPlot >= TownState.PlotCount) { ClosePanel(); break; }
                    HandlePick(TownHud.DrawSeedPicker(_state, _panelPlot, PlotCenter(_panelPlot)));
                    break;
                case TownPanel.Shop:
                    {
                        int r = TownHud.DrawShop(_state);
                        if (r == TownHud.PickClose) ClosePanel();
                    }
                    break;
                case TownPanel.Clothing:
                    {
                        int r = TownHud.DrawClothing(_state);
                        if (r == TownHud.PickClose) ClosePanel();
                    }
                    break;
                case TownPanel.Codex:
                    {
                        int r = TownHud.DrawCodex(_state);
                        if (r == TownHud.PickClose) ClosePanel();
                    }
                    break;
                case TownPanel.Difficulty:
                    {
                        int r = TownHud.DrawDifficulty();
                        if (r == TownHud.PickClose) ClosePanel();
                        else if (r == TownHud.PickBattle) { ClosePanel(); GoBattle(); }
                        // ★ v258：第四个出口 —— 训练场（自由练习）。与进战斗同一层级，各自关面板再切场景 ✓
                        else if (r == TownHud.PickTraining) { ClosePanel(); GoTraining(); }
                    }
                    break;
            }

            TownHud.DrawMinimap(this, _state);
            // ★ v216 ⑤：世界空间名字标签画在最后、压在所有面板之上（但面板打开时不画，避免挡字）★
            if (_panel == TownPanel.None) TownHud.DrawWorldLabels(_cam);
        }

        // ══════════════════════════════════════════════════════════════════
        // 输入
        // ══════════════════════════════════════════════════════════════════

        private void ReadInput(float dt)
        {
            Keyboard kb = Keyboard.current;

            Vector2 move = Vector2.zero;
            if (_walkLeft > 0f)
            {
                // 自动化 Walk：按方位角连续走，**走的是与键盘完全同一条移动/碰撞路径**
                move = _walkDir;
                _walkLeft -= dt;
                if (_walkLeft <= 0f)
                {
                    _walkLeft = 0f;
                    Log.Info(Tag + $"Walk 结束：停在 ({_avatar.Position.x:0.00},{_avatar.Position.z:0.00})" +
                                  $"｜被挡帧数 {_avatar.BlockedFrames}｜最后一次 {_avatar.LastHit}");
                }
            }
            else if (kb != null)
            {
                float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                move = new Vector2(x, y);
            }
            // ★ v252：**难度面板开着时钉住脚步** ★
            //  这是本工程第一处"面板吃输入"的地方，所以把理由写在这里（也写进交付说明）：
            //  难度面板是从传送门打开的，而传送门的判定是"离 `PortalAnchor` ≤ 2.6 m"——
            //  玩家若一边看档位一边按 W 走开，面板还开着、人已经不在门前，
            //  点「开始战斗」会显得莫名其妙（"我不在门口怎么进去了"）。
            //  其它五个面板保持原行为（能边走边看）⇒ 只影响这一块面板，不改既有手感 ✓
            if (_panel == TownPanel.Difficulty) move = Vector2.zero;
            _avatar.Tick(move, dt);

            if (kb == null) return;
            // E：面板开着 ⇒ 关；没开 ⇒ 对最近的可交互物做动作（见类注释第三节）
            if (kb.eKey.wasPressedThisFrame)
            {
                if (_panel != TownPanel.None) ClosePanel();
                else Interact();
            }

            // v251：统一走 `EscKey`（上升沿锁存 ⇒ 不漏帧；`Keyboard.current == null` 时不会静默失效）
            if (EscKey.Pressed() && _panel != TownPanel.None) ClosePanel();

            // 选种面板开着时：数字键 1..8 = 直接种对应种子
            if (_panel == TownPanel.SeedPicker)
            {
                for (int i = 0; i < TownData.Seeds.Length && i < 9; i++)
                {
                    Key k = (Key)((int)Key.Digit1 + i);
                    if (kb[k].wasPressedThisFrame) { Plant(_panelPlot, i); break; }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 交互
        // ══════════════════════════════════════════════════════════════════

        private void UpdateActTarget()
        {
            Vector2 p = new Vector2(_avatar.Position.x, _avatar.Position.z);
            float best = float.MaxValue;
            _actKind = ActKind.None;
            _actPlot = -1;
            _actHint = null;

            Consider(p, WarehouseAnchor, WarehouseRadius, ActKind.Warehouse, -1, ref best);
            Consider(p, PortalAnchor, PortalRadius, ActKind.Portal, -1, ref best);
            Consider(p, ShopAnchor, ShopRadius, ActKind.Shop, -1, ref best);
            Consider(p, ClothingAnchor, ClothingRadius, ActKind.Clothing, -1, ref best);
            Consider(p, HomeAnchor, HomeRadius, ActKind.Home, -1, ref best);

            // 36 块地各算一次（36 次距离计算，够便宜）
            for (int i = 0; i < TownState.PlotCount; i++)
                Consider(p, PlotCenter(i), PlotRadius, ActKind.Plot, i, ref best);

            if (_actKind == ActKind.None) return;

            switch (_actKind)
            {
                case ActKind.Warehouse:
                    _actHint = _state.WarehouseTotal > 0
                        ? "【E】打开种子仓库"
                        : "【E】打开种子仓库（空着 —— 种子要靠打 BOSS 掉落）";
                    break;
                case ActKind.Portal:
                    _actHint = "【E】选定难度 · 走进传送门开盘";
                    break;
                case ActKind.Shop:
                    // ★ v223：文字更正 —— 商店现在卖的是「道具 / 插件 / **植物**」（不是"成品"）✓
                    _actHint = "【E】杂货商店 · 道具 / 插件 / 植物（不卖种子）";
                    break;
                case ActKind.Clothing:
                    _actHint = "【E】服装店 · 皮肤";
                    break;
                case ActKind.Home:
                    _actHint = "【E】回家 · 查看图鉴（植物 / 道具 / 插件 / 成就）";
                    break;
                case ActKind.Plot:
                    {
                        int si = _state.PlotSeed[_actPlot];
                        if (si < 0)
                        {
                            // ★ §⑥：仓库空时的提示要指向"打 BOSS"，不能再提"去商店买" ★
                            _actHint = _state.HasAnySeed()
                                ? "【E】在这块空地种下种子"
                                : "仓库里没有种子 —— 先回战斗里打 BOSS 掉种子（商店不卖）";
                        }
                        else
                        {
                            TownSeedDef d = TownData.SeedAt(si);
                            double el = _state.PlotElapsed[_actPlot];
                            // ★ 本地化：**先把变量翻掉，再套格式** —— 整串去表里找是找不到的 ✗
                            //   ① 种子名：它自己就是一条 `name.<id>` 键 ⇒ `T("name." + d.id)` ✓
                            //   ② 再 `T(key, args)` 把名字 / 秒数填进句式 ✓
                            //   （这样日文的语序也能整句换，而不用中文语序硬套）
                            string nm = d == null ? "" : L10n.T("name." + d.id);
                            if (string.IsNullOrEmpty(nm) && d != null) nm = L10n.S(d.name);
                            // ★ 成熟 ⇒ 提示改成"收获"（成熟不再自动返种，得收才会回仓库）★
                            _actHint = _state.PlotMature[_actPlot]
                                ? L10n.T("hint.harvestNow", nm)
                                : L10n.T("hint.replaceCrop", nm, el.ToString("0"),
                                         (d == null ? 0 : d.GrowSeconds).ToString("0"));
                        }
                    }
                    break;
            }
        }

        private void Consider(Vector2 p, Vector2 anchor, float radius, ActKind kind, int plot, ref float best)
        {
            float d = Vector2.Distance(p, anchor);
            if (d > radius) return;
            float norm = d / radius;             // 归一化（见类注释第三节）
            if (norm >= best) return;
            best = norm;
            _actKind = kind;
            _actPlot = plot;
        }

        /// <summary>对当前最近的可交互物执行动作（等于按一次 E）。</summary>
        public void Interact()
        {
            if (_dataError != null) return;

            // 与 E 键**完全同一条分支**：面板开着 ⇒ 这一下是"关面板"，不会穿透到脚下那块地
            if (_panel != TownPanel.None)
            {
                ClosePanel();
                Log.Info(Tag + "关闭面板（等同于按 E）");
                return;
            }

            UpdateActTarget();
            switch (_actKind)
            {
                case ActKind.Warehouse:
                    _panel = TownPanel.Warehouse;
                    Log.Info(Tag + "打开种子仓库（仓库 " + _state.WarehouseTotal + " 颗）");
                    break;
                case ActKind.Shop:
                    _panel = TownPanel.Shop;
                    Log.Info(Tag + "打开杂货商店（道具/插件/成品，不卖种子）");
                    break;
                case ActKind.Clothing:
                    _panel = TownPanel.Clothing;
                    Log.Info(Tag + "打开服装店（只卖皮肤）");
                    break;
                case ActKind.Home:
                    _panel = TownPanel.Codex;
                    Log.Info(Tag + $"打开图鉴（植物 {TownCodex.Plants.Length} / 道具 {TownCodex.Items.Length} / " +
                                  $"插件 {TownCodex.Plugins.Length} 条已解锁）");
                    break;
                case ActKind.Portal:
                    // ★ v252：**先选难度，再进战斗** ★
                    //  用户原话：「在传送门进入战斗前增加难度选择功能和对应界面，开放低、中、高」
                    //  ⇒ 传送门按 E 打开难度面板（原来这里直接 `GoBattle()`）；
                    //    真正切场景的是面板里那颗「开始战斗」（见 `OnGUI` 的 Difficulty 分支）✓
                    _panel = TownPanel.Difficulty;
                    Log.Info(Tag + "打开难度选择（当前 " + RunDifficulty.NameOf(RunDifficulty.Current) + "）");
                    break;
                case ActKind.Plot:
                    _panel = TownPanel.SeedPicker;
                    _panelPlot = _actPlot;
                    Log.Info(Tag + $"打开选种面板（第 {_actPlot + 1} 号耕地）");
                    break;
                default:
                    Log.Info(Tag + "附近没有可交互的东西");
                    break;
            }
        }

        private void ClosePanel()
        {
            _panel = TownPanel.None;
            _panelPlot = -1;
        }

        private void HandlePick(int code)
        {
            if (code == TownHud.PickClose) { ClosePanel(); return; }
            if (code == TownHud.PickNone) return;
            if (_panelPlot < 0) { ClosePanel(); return; }
            if (code == TownHud.PickHarvest)
            {
                // ★ 收获：作物离开田位 ＋ 种子回仓库（两件事都在 `TownState.Harvest` 里一起做）★
                int si = _state.PlotSeed[_panelPlot];
                if (_state.Harvest(_panelPlot))
                    Log.Info(Tag + $"第 {_panelPlot + 1} 号耕地收获：{TownData.SeedAt(si)?.name} ⇒ 种子回仓库" +
                                  $"（仓库这种 {_state.Warehouse[si]} 颗）");
                else
                    Log.Info(Tag + $"第 {_panelPlot + 1} 号耕地还收不了（没成熟 / 空地）");
                ClosePanel();
                return;
            }
            if (code == TownHud.PickClear)
            {
                if (_state.Clear(_panelPlot)) Log.Info(Tag + $"第 {_panelPlot + 1} 号耕地已铲除");
                ClosePanel();
                return;
            }
            if (code >= 0) Plant(_panelPlot, code);
        }

        /// <summary>在 <paramref name="plot"/> 种下 <paramref name="seedIndex"/>（空地种 / 已有作物则换种）。</summary>
        public bool Plant(int plot, int seedIndex)
        {
            if (plot < 0 || plot >= TownState.PlotCount) return false;
            TownSeedDef d = TownData.SeedAt(seedIndex);
            if (d == null) return false;

            bool wasOccupied = _state.PlotSeed[plot] >= 0;
            if (!_state.Plant(plot, seedIndex))
            {
                Log.Info(Tag + $"第 {plot + 1} 号耕地种不下 {d.name}（同种 / 仓库无货）");
                return false;
            }
            Vector2 c = PlotCenter(plot);
            Log.Info(Tag + $"第 {plot + 1} 号耕地{(wasOccupied ? "换种" : "种下")}：{d.name}" +
                           $"（{c.x:0.0},{c.y:0.0} · 成熟 {d.GrowSeconds:0} 秒 · {d.GoldPerSecond:0.0} 金/秒 · " +
                           $"仓库剩 {_state.Warehouse[seedIndex]}）");
            ClosePanel();
            return true;
        }

        /// <summary>
        /// 传送门：**切回战斗场景**。用 `SceneManager.LoadScene(名字)` —— 名字必须在 Build Settings 里。
        /// </summary>
        public void GoBattle()
        {
            Log.Info(Tag + "走进传送门 → LoadScene(" + BattleSceneName + ")");
            // ★★ v251：**切战斗场景之前先把主城进度写盘** ★★
            // 进战斗是单场景加载 ⇒ `Login` 场景（含 `LoginBootstrap`，即"当前存档位"的唯一持有者）
            // 会被销毁 ⇒ 战斗退出回主城时只能重建一座空城（用户报"回来后主城进度没了"）。
            // 先存一次，退出战斗时 `LoginBootstrap.AutoEnterSlot` 再把这个档读回来 ✓
            LoginBootstrap.SaveBeforeBattle();
            if (Application.CanStreamedLevelBeLoaded(BattleSceneName))
            {
                SceneManager.LoadScene(BattleSceneName);
            }
            else
            {
                Log.Error(Tag + $"场景 {BattleSceneName} 不在 Build Settings 里，传送门无法生效");
            }
        }

        /// <summary>
        /// ★★ v258：传送门难度面板里的「训练场（自由练习）」★★
        ///
        /// 用户原话：「把训练场模式加入到传送门选择中」—— 训练场原来只有登录页一颗按钮
        /// （`LoginBootstrap.ActTraining`），进了主城就再也回不去，所以这里给它第二个入口。
        ///
        /// ── 与 `GoBattle` 的关系：同一种做法（单场景 `LoadScene`），只有一处不同 ──
        ///   · **不调 `LoginBootstrap.SaveBeforeBattle()`**：那条是给"进战斗"专用的一句话说法，
        ///     而训练场根本不需要额外存档 —— `LoginBootstrap.OnDestroy()` 里已经写着
        ///     `if (_stage == Stage.Playing) SaveCurrent("离开场景")`（`LoginBootstrap.cs:482-484`），
        ///     单场景加载销毁它的时候主城进度**照样会落盘** ⇒ 这里再存一次是重复写盘 ✓
        ///   · 训练场是"练枪房"：不读存档、不写存档、不进 `Stage.Playing`（与 `ActTraining` 的注释一致），
        ///     `TrainingGame` 自建地面/玩家/稻草人/HUD，**不依赖**本类的任何状态 ✓
        ///   · 遮罩不用在这里开：`LoginLoadingHost.OnSceneLoaded` 对"非 Login / 非 Town"的场景
        ///     会自动盖一张"载入完成"确认遮罩 —— `Training` 正好落在这个分支里 ✓
        ///
        /// ── 失败兜底（不静默）──
        ///   场景不在 Build Settings 里 ⇒ **不切**，写一条 `Log.Error` 带上场景名，人留在主城、
        ///   面板已经关掉（`OnGUI` 是先 `ClosePanel()` 再调本方法），不会卡在半路 ✓
        /// </summary>
        public void GoTraining()
        {
            if (!Application.CanStreamedLevelBeLoaded(TrainingSceneName))
            {
                Log.Error(Tag + $"场景 {TrainingSceneName} 不在 Build Settings 里，训练场入口无法生效" +
                               "（Assets/Scenes/Training.unity 需在 Build Settings 中被勾选）");
                return;
            }
            Log.Info(Tag + "传送门 → 训练场（自由练习，不影响存档）→ LoadScene(" + TrainingSceneName + ")");
            SceneManager.LoadScene(TrainingSceneName);
        }

        // ══════════════════════════════════════════════════════════════════
        // 场景构建
        // ══════════════════════════════════════════════════════════════════

        private void BuildScene()
        {
            _root = new GameObject("TOWN").transform;
            _propRoot = new GameObject("TOWN_Props").transform;
            _propRoot.SetParent(_root, false);
            _plantRoot = new GameObject("TOWN_Plants").transform;
            _plantRoot.SetParent(_root, false);

            BuildCamera();
            BuildLight();
            BuildGround();
            BuildRoadsAndPlaza();
            BuildZones();                  // 西侧三区的铺地（农田土块 / 公园草坪 / 商业街）
            BuildFarm();
            TownBuildings.BuildFarmDressing(_root, _propRoot);   // v220：田地篱笆/小草/石头（都不压田格）
            BuildHedge();
            BuildWarehouseTerrace();       // 仓库门前的装卸广场（垫在建筑下面，属于地面，不是建筑）

            // ★ 所有立体的东西（六座建筑 + 公园绿化 + 路灯 + 喷泉）都在 `TownBuildings` ★
            Transform[] buildings = TownBuildings.BuildAll(_root, _propRoot);
            TownBuildings.BuildFountain(_root, _propRoot);
            TownBuildings.BuildPark(_propRoot);
            TownBuildings.BuildLamps(_root, _propRoot);
            TownBuildings.BuildDecorHouses(_root, _propRoot);   // v216：右上角 3 栋装饰屋
            TownBuildings.BuildUnbuiltLot(_root, _propRoot);    // v216：右下角围栏 + 「未开发」牌
            Log.Info(Tag + "建筑：" + string.Join(" · ", System.Array.ConvertAll(buildings, b => b.name)) +
                          $" + 装饰屋 {TownLayout.DecorHouses.Length} 栋 + 未开发地块 1 块 · " +
                          $"名字标签 {TownBuildings.Labels.Count} 个");

            BuildPlayer();
            BuildLife();                       // v218：人/动物/鸟/云/雨 + 把树登记进"会摇"的列表

            for (int i = 0; i < TownState.PlotCount; i++) _plotViews[i] = new PlotView();
            SyncPlots();

            // ★ 碰撞体是世界构建完才加上的 ⇒ 让物理引擎把 transform 同步一次 ★
            //   （`Physics.autoSyncTransforms` 在新版本默认关闭；不同步的话
            //    建城那一帧的 CapsuleCast 会按"上一次的位置"判，读到的是空）
            Physics.SyncTransforms();
            Log.Info(Tag + "碰撞体：" + DescribeColliders());

            // ★ 装饰选点的收尾：一件一条的「落点在田格上 ⇒ 跳过」警告已经作废（见
            //   `TownBuildings.PropPolar/PropAt`：撞田先**换点**，确定性重试 12 次）。
            //   只有 12 次都换不出来、真的少摆了的，才在这里打**一条**汇总；
            //   一个都没有 ⇒ 什么都不打（主城日志不再刷黄）★
            TownBuildings.FlushDecorSkipReport();
        }

        // ── 地面：一整块草地（城外也是草地，玩家走不出去靠圆形边界）──
        private void BuildGround()
        {
            GameObject g = TownKit.Box(_root, "GROUND_Grass", new Vector3(0f, -0.5f, 0f),
                                       new Vector3(110f, 1.0f, 110f), TownKit.Mat(TownColors.Grass));
            g.isStatic = true;
        }

        // ── 路网（v217）：中央环岛 ＋ **右侧一条商业街** ＋ 南向进城街 ──
        private void BuildRoadsAndPlaza()
        {
            Material road = TownKit.Mat(TownColors.Road);
            Material roadDark = TownKit.Mat(TownColors.RoadDark);
            Material plaza = TownKit.Mat(TownColors.Plaza);
            Material park = TownKit.Mat(TownColors.ParkGrass);

            // 中央广场（环岛中心，只留喷泉那一圈）+ 深色边石
            Plate("PLAZA_Disc", TownKit.Disc(TownLayout.PlazaRadius, 48), SlabTopY + 0.004f, plaza);
            Plate("PLAZA_CurbRing", TownKit.Ring(TownLayout.PlazaRadius - 0.26f, TownLayout.PlazaRadius, 48),
                  SlabTopY + 0.010f, roadDark);
            // 内环路（1 米宽的环岛）
            Plate("RING_RoadInner", TownKit.Ring(TownLayout.InnerRoadIn, TownLayout.InnerRoadOut, 48),
                  SlabTopY, road);
            // 绿带（环岛 → r 6.4）：一圈草地
            Plate("RING_GreenBelt", TownKit.Ring(TownLayout.InnerRoadOut, TownLayout.GreenBeltOuter, 48),
                  SlabTopY - 0.004f, park);

            // 南向进城街
            float h = TownLayout.StreetHalf;
            SlabWorld("STREET_South", 0f, (TownLayout.InnerRoadOut + TownLayout.StreetEndSouth) * 0.5f,
                  h * 2f, Mathf.Abs(TownLayout.StreetEndSouth) - TownLayout.InnerRoadOut, road, SlabTopY);

            // ★ 右侧商业街（南北向）：互动建筑全排在它两边，门都对着它 ★
            float sx = (TownLayout.ShopStreetXMin + TownLayout.ShopStreetXMax) * 0.5f;
            float sw = TownLayout.ShopStreetXMax - TownLayout.ShopStreetXMin;
            float sz0 = TownLayout.ShopStreetZMin, sz1 = TownLayout.ShopStreetZMax;
            SlabWorld("STREET_Shop", sx, (sz0 + sz1) * 0.5f, sw, sz1 - sz0, road, SlabTopY);
            for (int i = -1; i <= 1; i += 2)
                SlabWorld(i < 0 ? "STREET_ShopCurbW" : "STREET_ShopCurbE",
                      sx + i * (sw * 0.5f - 0.16f), (sz0 + sz1) * 0.5f, 0.32f, sz1 - sz0, roadDark, SlabTopY + 0.004f);
            // ★★ v264：**传送门前的完整路面**（用户："传送门前路面加宽，让传送门前有完整路面"）★★
            //   改前：商业街只到 x 6.20~11.80 ⇒ 门（x=6.00，台基半径 5.90）的**西半边是草地** ✗
            //   现在补两块，把门口铺成一片连续的路面（都在 SlabTopY，与既有路面同高 ⇒ 无闪面）
            {
                Material curbDark = TownKit.Mat(TownColors.RoadDark);
                // ① 西侧月台：把商业街往西扩到门台基外沿（x −0.80 ~ 6.20）
                float ax0 = -0.80f, ax1 = TownLayout.ShopStreetXMin;      // −0.80 ~ 6.20
                float az0 = 10.60f, az1 = 17.20f;
                SlabWorld("STREET_PortalApronW", (ax0 + ax1) * 0.5f, (az0 + az1) * 0.5f,
                          ax1 - ax0, az1 - az0, road, SlabTopY);
                // 西沿 + 南沿边石（与既有边石同一档高度）
                SlabWorld("STREET_PortalApronCurbW", ax0 + 0.16f, (az0 + az1) * 0.5f,
                          0.32f, az1 - az0, curbDark, SlabTopY + 0.004f);
                SlabWorld("STREET_PortalApronCurbS", (ax0 + ax1) * 0.5f, az0 + 0.16f,
                          ax1 - ax0, 0.32f, curbDark, SlabTopY + 0.004f);
                // ② 北侧补条：商业街 z 到 16.50，门在 16.00 ⇒ 门后沿那 0.9 米也铺上
                SlabWorld("STREET_PortalApronN", sx, (TownLayout.ShopStreetZMax + 17.40f) * 0.5f,
                          sw, 17.40f - TownLayout.ShopStreetZMax, road, SlabTopY);
            }

            // 环岛 ↔ 商业街 的短接
            SlabWorld("STREET_Link", (TownLayout.InnerRoadOut + TownLayout.ShopStreetXMin) * 0.5f, 0f,
                  TownLayout.ShopStreetXMin - TownLayout.InnerRoadOut, h * 2f, road, SlabTopY);

            // ══════════════════════════════════════════════════════════════════
            // ★ v269：**道路边缘绿化带**（用户原话：「主城的道路边缘尝试增加绿化带丰富」）★
            // ══════════════════════════════════════════════════════════════════
            // 为什么这次不会再返工 —— 三条纪律，每条都对着前几轮的病根：
            //   ① **与路面 / 边石零重叠**：每块草带的 x/z 边界都**正好落在路沿上**（abut，绝不 cross）。
            //      路沿位置全部取自 `TownLayout`，一个都不是手填的：
            //        商业街 x 6.20 / 11.80（`ShopStreetXMin/Max`）· 南向街 x ±2.40（`StreetHalf`）
            //        · 环岛绿带外沿 6.40（`GreenBeltOuter`）
            //   ② **高度 = `SlabTopY − 0.002` = 0.018**：
            //      路面顶 0.020、边石顶 0.024 ⇒ 草带比路面低 2 mm、比边石低 6 mm。
            //      ⇒ 从俯视镜头看，路沿外沿自然多出一道"压低一档的草沿"；
            //        草带与路面**从来不在同一个平面**，所以不可能出现闪面（z-fighting）
            //   ③ 名字统一 `GREEN_*`，而且**每个名字里都带 `Bush`** —— 这一条是必须的：
            //      `StripRoadClutter`（v233）会把商业街 ±6 m 范围内"有 Renderer 且名字不在白名单里"的
            //      节点**整枝 `SetActive(false)`**，而白名单 `KeepWord` 里正有 "bush"
            //      ⇒ 名字带上它，这一组整组被 `KeptByAncestor` 保下来，
            //        不必去改白名单表（本轮只允许改 `BuildRoadsAndPlaza` / `BuildZones` 两个方法）✓
            const float GreenTopY = SlabTopY - 0.002f;                       // 0.018（路面 0.020）
            Material verge = TownKit.Mat(TownColors.ParkGrass);              // 草带：与 `RING_GreenBelt` 同色
            Material vergeDark = TownKit.Mat(TownColors.Hedge);              // 深绿：环岛外围草沿 + 一半灌木
            Material vergeMid = TownKit.Mat(new Color(0.30f, 0.55f, 0.32f));// 中绿：另一半灌木（新色，只多 1 份材质）

            // ── ① 商业街（南北向，x 6.20~11.80）**东西两侧各一条** ──
            //   西侧 0.55 m：x 5.65 ~ 6.20（6.20 = 街面西沿）
            //     · 断在 z −3.20 ~ 3.05：那里是 `STREET_Link`（x 4.40~6.20, z ±2.40）与
            //       `RING_GreenBelt`（r ≤ 6.40）的地盘；z 3.05 这个端点是这样算出来的：
            //       x 5.65 处要到 r ≥ 6.40 才不压环岛绿带 ⇒ |z| ≥ √(6.40² − 5.65²) = 3.01 ✓
            //     · 北端停在 10.00：`PORTAL_Platform`（圆心 (6,16)、最外一级 r 5.90）最近到 z 10.10，
            //       再往北就是门前 `STREET_PortalApronW`（z 10.60 起）⇒ 留 0.1 / 0.6 米余量 ✓
            SlabWorld("GREEN_BushVerge_ShopW1", 5.9250f, -9.3500f, 0.55f, 12.30f, verge, GreenTopY);   // z −15.50~−3.20
            SlabWorld("GREEN_BushVerge_ShopW2", 5.9250f, 6.5250f, 0.55f, 6.95f, verge, GreenTopY);    // z   3.05~ 10.00
            //   东侧 0.45 m：x 11.80 ~ 12.25（11.80 = 街面东沿 + 边石外沿；12.25 = 服装店方台基西沿
            //   ⇒ 正好贴住、**不插进楼体**）
            //     · 南段停在 3.30：杂货商店圆台基（圆心 (13.80,5.50)、r 2.10）最近到 z 3.40 ✓
            //     · 北段 7.70 起（同上，让开店台基）、14.70 止（传送门台基在 x 11.80 处最南到 z 14.92 ✓）
            SlabWorld("GREEN_BushVerge_ShopE1", 12.0250f, -6.1000f, 0.45f, 18.80f, verge, GreenTopY); // z −15.50~ 3.30
            SlabWorld("GREEN_BushVerge_ShopE2", 12.0250f, 11.2000f, 0.45f, 7.00f, verge, GreenTopY);  // z   7.70~14.70

            // ── ② 南向进城街（x ±2.40，z −14.20~0）**两侧各一条 0.50 m** ──
            //   北端只铺到 −6.70：那一带的 |z| 已经从 1.8（广场 r 3.00 的边界）涨到 6.7，
            //   在 (2.40,−6.70) 处 r = 7.12 ⇒ 比环岛绿带外沿 6.40 还有 **0.7 米** 余量，
            //   既不压广场圆盘（0.024）、也不压环岛绿带（0.016），更不压路面 ✓
            SlabWorld("GREEN_BushVerge_SouthL", -2.6500f, -10.4500f, 0.50f, 7.50f, verge, GreenTopY); // x −2.90~−2.40
            SlabWorld("GREEN_BushVerge_SouthR", 2.6500f, -10.4500f, 0.50f, 7.50f, verge, GreenTopY);  // x  2.40~ 2.90

            // ── ③ 环岛绿带（`RING_GreenBelt`，r 4.40~6.40）**外侧**再加一圈 0.35 m 的深色草沿 ──
            //   为什么是**逐段弦块**而不是一个 `TownKit.Ring`：
            //     整圈 r 6.40~6.75 会横穿商业街 / 环岛↔商业街短接 / 南向进城街 / **整片农田土面**
            //     —— 农田土面顶 0.014 **低于**草沿的 0.018 ⇒ 环会浮在田上、还会从 36 块地的缝隙里穿过去 ✗
            //   ⇒ 48 段弦块，每段先判"中心点这里有没有路 / 田"，空的才铺；并要求**本段 + 左右邻段**
            //     三段连空才铺（≈ 2.6 m 起）—— 只按单段判会留下孤零零 0.86 m 的碎片，
            //     那比不铺更难看（离线复算：单段判会在 311° 一带碎出一段孤弧，三段连判就没有了）。
            //   弦长用 `tan` 取：相邻两段**恰好共角**（不叠也不缝），内沿与 r 6.40 相切
            //   ⇒ 与 `RING_GreenBelt` 只"贴着"，不压上去 ✓
            const int RingSeg = 48;
            float ringStepDeg = 360f / RingSeg;
            float ringMidR = TownLayout.GreenBeltOuter + 0.175f;             // 6.400 + 半带宽 0.175
            float ringSideLen = 2f * ringMidR * Mathf.Tan(Mathf.PI / RingSeg);

            // 这一段草沿的中心点是否"干净"（余量 0.60：半弦 0.43 + 半带宽 0.18 + 余量）
            bool FreeOfBuilt(float x, float z)
            {
                const float m = 0.60f;
                // 商业街
                if (x >= TownLayout.ShopStreetXMin - m && x <= TownLayout.ShopStreetXMax + m &&
                    z >= TownLayout.ShopStreetZMin - m && z <= TownLayout.ShopStreetZMax + m) return false;
                // 环岛 ↔ 商业街 短接（与上面那块 `STREET_Link` 同尺寸）
                if (x >= TownLayout.InnerRoadOut - m && x <= TownLayout.ShopStreetXMin + m &&
                    z >= -TownLayout.StreetHalf - m && z <= TownLayout.StreetHalf + m) return false;
                // 南向进城街（与上面那块 `STREET_South` 同尺寸：x ±StreetHalf、z StreetEndSouth~0）
                if (x >= -TownLayout.StreetHalf - m && x <= TownLayout.StreetHalf + m &&
                    z >= TownLayout.StreetEndSouth - m && z <= m) return false;
                // 农田土面（`ZONE_FarmSoil`，含 36 块田格）
                float fx = TownLayout.FarmCenter.x, fz = TownLayout.FarmCenter.y, fh = TownLayout.FarmHalf;
                if (x >= fx - fh - m && x <= fx + fh + m && z >= fz - fh - m && z <= fz + fh + m) return false;
                // 36 块田格（双保险：`TownLayout.IsOnPlot` 就是"任何东西都不许站上去"的唯一判据）
                if (TownLayout.IsOnPlot(x, z, m)) return false;
                return true;
            }

            bool[] ringFree = new bool[RingSeg];
            for (int i = 0; i < RingSeg; i++)
            {
                Vector2 c = TownLayout.Polar(i * ringStepDeg + ringStepDeg * 0.5f, ringMidR);
                ringFree[i] = FreeOfBuilt(c.x, c.y);
            }
            int ringBands = 0;
            for (int i = 0; i < RingSeg; i++)
            {
                // 本段 + 左右邻段都空才铺 ⇒ 最短一条草沿也有 3 × 0.86 ≈ 2.6 m（不留孤零零的碎片），
                // 同时路口两侧自然各让出一段，草沿不会顶到路沿上
                if (!ringFree[i] || !ringFree[(i + RingSeg - 1) % RingSeg] ||
                    !ringFree[(i + 1) % RingSeg]) continue;
                float deg = i * ringStepDeg + ringStepDeg * 0.5f;
                Vector2 c = TownLayout.Polar(deg, ringMidR);
                GameObject band = TownKit.Box(_root, "GREEN_BushVerge_Ring" + i.ToString("00"),
                    new Vector3(c.x, GreenTopY - 0.025f, c.y),                 // 厚 0.05 ⇒ 中心 = 顶面 − 0.025
                    new Vector3(ringSideLen, 0.05f, 0.35f), vergeDark);
                band.transform.rotation = Quaternion.Euler(0f, 90f - deg, 0f);  // 与 `BuildHedge` 同一套切向公式
                ringBands++;
            }

            // ── ④ 绿化带上的**矮灌木**：1.2~1.6 m 一株、深绿/中绿两档 ──
            //   位置**不用 `Random`**：同一个 (段号, 序号) 永远得到同一组数 ⇒ 每次进主城长得一模一样。
            //   哈希算法**照抄** `TownFarmDecor.Hash01`（"用户指定口径"：h = Hash(seed) ^ 0x9E3779B9，
            //   再 frac(h × 0.6180339887)）—— 那里是 private，所以这里复刻同一份，
            //   主城的两处"确定性撒点"因此是同一个口径、也都不碰 `World.Rng` ✓
            float Hash01(int seed, int salt)
            {
                unchecked
                {
                    uint h = (uint)seed * 374761393u + (uint)salt * 668265263u;
                    h = (h ^ (h >> 15)) * 2246822519u;
                    h ^= 0x9E3779B9u;                       // ← 黄金分割常量（与 `TownFarmDecor` 同一行）
                    h = (h ^ (h >> 13)) * 3266489917u;
                    h ^= h >> 16;
                    float f = (h & 0xFFFFFFu) / 16777216f;  // 0 ~ 1（左闭右开）
                    f *= 0.6180339887f;
                    f -= Mathf.Floor(f);                    // = 口径里的 frac(...)
                    // ★ 再归一化回 0~1 ★
                    //   `(h & 0xFFFFFF)/2^24 × 0.618` 必然小于 1 ⇒ 上面那个 `Floor` 是**空操作**，
                    //   直接用它的取值域只有 0~0.618：灌木会整批偏小（实测半径只落在 0.121~0.144）。
                    //   除回去即恢复 0~1，尺寸才真的铺满 0.12~0.16 / 0.18~0.28 这两档 ✓
                    return f / 0.6180339887f;
                }
            }

            int bushCount = 0;
            void BushRow(string label, float x, float zFrom, float zTo, int seed)
            {
                float z = zFrom;
                for (int k = 0; z <= zTo; k++)
                {
                    float rad = 0.12f + Hash01(seed, k * 5 + 1) * 0.04f;         // 半径 0.12~0.16
                    float hgt = 0.18f + Hash01(seed, k * 5 + 2) * 0.10f;         // 高   0.18~0.28
                    float ox = (Hash01(seed, k * 5 + 3) - 0.5f) * 0.08f;         // 横向 ±4 cm（不越出带边）
                    Material mat = Hash01(seed, k * 5 + 4) < 0.5f ? vergeDark : vergeMid;
                    // `CylL` 的 localPos 是**底面中心**（内部再抬 height*0.5）⇒ y 直接给草带顶面
                    TownKit.CylL(_root, "GREEN_Bush_" + label + k.ToString("00"),
                                 new Vector3(x + ox, GreenTopY, z), rad, hgt, mat);
                    bushCount++;
                    z += 1.20f + Hash01(seed, k * 5 + 5) * 0.40f;                // 间距 1.20~1.60 m
                }
            }

            // 六条带子各一排（两端各内收 0.2~0.3 m ⇒ 灌木不悬在带子端头）
            BushRow("ShopW1_", 5.9250f, -15.30f, -3.50f, 101);
            BushRow("ShopW2_", 5.9250f, 3.25f, 9.75f, 102);
            BushRow("ShopE1_", 12.0250f, -15.30f, 3.10f, 103);
            BushRow("ShopE2_", 12.0250f, 7.90f, 14.50f, 104);
            BushRow("SouthL_", -2.6500f, -14.00f, -6.90f, 105);
            BushRow("SouthR_", 2.6500f, -14.00f, -6.90f, 106);

            Log.Info(Tag + "绿化带：" +
                           "草带 6 条（商业街西 0.55 m ×2 段 / 东 0.45 m ×2 段 / 南向街两侧 0.50 m ×2 段）· " +
                           $"环岛外围深色草沿 {ringBands}/{RingSeg} 段（其余段让开路面与农田）· " +
                           $"矮灌木 {bushCount} 株 · 草带顶面 {GreenTopY:0.000}（路面 {SlabTopY:0.000}、边石 {SlabTopY + 0.004f:0.000}）");
        }

        /// <summary>
        /// 地面分区（v217）：**放大的农田土面** ＋ 公园草坪 ＋ 商业街两端的收口。
        /// 只铺"地"，不动任何建筑与装饰 ⇒ 地面与立体物分开，改一层不牵另一层 ✓
        /// </summary>
        private void BuildZones()
        {
            Material soil = TownKit.Mat(TownColors.TilledSoil);
            Material park = TownKit.Mat(TownColors.ParkGrass);
            Material curb = TownKit.Mat(TownColors.StoneDark);

            // ① 农田区：一整块方形土面（14.3 × 14.3），占据城左半边
            Vector2 f = TownLayout.FarmCenter;
            SlabWorld("ZONE_FarmSoil", f.x, f.y, TownLayout.FarmHalf * 2f, TownLayout.FarmHalf * 2f,
                      soil, SlabTopY - 0.006f);
            SlabWorld("ZONE_FarmCurbS", f.x, f.y - TownLayout.FarmHalf + 0.06f, TownLayout.FarmHalf * 2f, 0.12f,
                      curb, SlabTopY + 0.002f);
            SlabWorld("ZONE_FarmCurbN", f.x, f.y + TownLayout.FarmHalf - 0.06f, TownLayout.FarmHalf * 2f, 0.12f,
                      curb, SlabTopY + 0.002f);
            SlabWorld("ZONE_FarmCurbW", f.x - TownLayout.FarmHalf + 0.06f, f.y, 0.12f, TownLayout.FarmHalf * 2f,
                      curb, SlabTopY + 0.002f);
            SlabWorld("ZONE_FarmCurbE", f.x + TownLayout.FarmHalf - 0.06f, f.y, 0.12f, TownLayout.FarmHalf * 2f,
                      curb, SlabTopY + 0.002f);

            // ② 公园区：一块独立草坪（8 × 8）+ 十字小径
            Vector2 p = TownLayout.ParkCenter;
            SlabWorld("ZONE_ParkLawn", p.x, p.y, TownLayout.ParkHalf * 2f, TownLayout.ParkHalf * 2f,
                      park, SlabTopY - 0.004f);
            SlabWorld("ZONE_ParkPathX", p.x, p.y, TownLayout.ParkHalf * 2f - 0.6f, 1.0f, TownKit.Mat(TownColors.Road),
                      SlabTopY + 0.004f);
            SlabWorld("ZONE_ParkPathZ", p.x, p.y, 1.0f, TownLayout.ParkHalf * 2f - 0.6f, TownKit.Mat(TownColors.Road),
                      SlabTopY + 0.004f);
        }


        /// <summary>世界坐标的圆盘/圆环薄板。<paramref name="topY"/> = 上表面的世界高度。</summary>
        private void Plate(string name, Mesh flat, float topY, Material mat)
        {
            GameObject go = TownKit.Plate(_root, name, flat, new Vector3(0f, topY, 0f), mat);
            // ★ 薄板没有厚度 ⇒ 用一块"托底"的深色底盘避免从侧面看到镂空 ★
            if (flat != null) go.isStatic = true;
        }

        /// <summary>一块矩形薄板（路 / 田的贴片）。<paramref name="topY"/> = 上表面的世界高度。</summary>
        private void SlabWorld(string name, float x, float z, float w, float d, Material mat, float topY)
        {
            const float Thickness = 0.05f;
            TownKit.Box(_root, name, new Vector3(x, topY - Thickness * 0.5f, z),
                        new Vector3(w, Thickness, d), mat);
        }

        // ── 田圃：**6×6 方格**（用户点名要方格；土面已在 `BuildZones` 铺好）──
        private void BuildFarm()
        {
            Material plotSoil = TownKit.Mat(TownColors.PlotSoil);

            // 36 块耕地：**同一个材质**，只有 Transform 不同 ⇒ 没有 per-instance 材质
            for (int i = 0; i < TownState.PlotCount; i++)
            {
                Vector2 c = PlotCenter(i);
                TownKit.Box(_root, "PLOT_" + (i + 1).ToString("00"),
                    new Vector3(c.x, PlotTopY - 0.02f, c.y),
                    new Vector3(TownLayout.PlotWidth, 0.05f, TownLayout.PlotDepth), plotSoil);
            }
        }

        // ── 城外的灌木环（圆形轮廓的"边界"，没有碰撞：玩家被圆形边界挡住）──
        private void BuildHedge()
        {
            Material hedge = TownKit.Mat(TownColors.Hedge);
            const int Seg = 56;
            for (int i = 0; i < Seg; i++)
            {
                float deg = i * (360f / Seg) + (180f / Seg);
                float chord = 2f * TownLayout.TownRadius * Mathf.Sin(Mathf.PI / Seg) * 1.08f;
                Vector3 p = TownLayout.Polar3(deg, TownLayout.TownRadius - 0.35f, 0.36f);
                GameObject b = TownKit.Box(_root, "HEDGE_" + i.ToString("00"), p,
                                           new Vector3(chord, 0.85f, 0.70f), hedge);
                b.transform.rotation = Quaternion.Euler(0f, 90f - deg, 0f);
            }
        }

        // ── 仓库门前的装卸广场（地面的一部分，不是建筑：没有碰撞）──
        private void BuildWarehouseTerrace()
        {
            Material stone = TownKit.Mat(TownColors.RoadDark);
            // v217：仓库在农田南侧、正面朝北 ⇒ 门前广场在它北侧（+Z 方向）
            Vector3 p = new Vector3(TownLayout.WarePos.x, SlabTopY - 0.02f, TownLayout.WarePos.y + 2.55f);
            GameObject t = TownKit.Box(_root, "TERRACE_Warehouse", p, new Vector3(5.6f, 0.05f, 1.6f), stone);
            t.transform.rotation = Quaternion.Euler(0f, TownLayout.WareYaw, 0f);
        }

        // ── 玩家 ──
        private void BuildPlayer()
        {
            _avatar = TownAvatar.Build(_root, SpawnPos, 0f);     // 面朝 +Z = 北 = 广场与传送门方向
            _avatar.BoundsRadius = TownLayout.WalkRadius;
            Log.Info(Tag + $"玩家出生：({SpawnPos.x:0.00},{SpawnPos.z:0.00}) 面朝北（广场方向）· " +
                           $"身宽 {TownAvatar.BodyRadius * 2f:0.00} 米 · 可达半径 {TownLayout.WalkRadius:0.00}");
        }

        private void BuildCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            if (cam.GetComponent<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.orthographicSize = FollowOrthoSize;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.56f, 0.74f, 0.88f);
            _cam = cam;
            UpdateCamera();
        }

        private void BuildLight()
        {
            // ★ 复用场景里已有的平行光，没有才新建 ★（新建场景时 Unity 会自带一盏，否则亮度翻倍）
            Light l = null;
            Light[] all = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].type == LightType.Directional) { l = all[i]; break; }
            }
            if (l == null)
            {
                var go = new GameObject("TownLight");
                l = go.AddComponent<Light>();
                l.type = LightType.Directional;
            }
            l.color = new Color(1.00f, 0.96f, 0.88f);
            l.intensity = 1.25f;
            l.shadows = LightShadows.Soft;
            l.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            _sunLight = l;                       // v218：日夜要驱动它

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.60f, 0.64f, 0.70f);
            RenderSettings.fog = false;
        }

        // ── 活气层（v218）：人 / 动物 / 鸟 / 风 / 云 / 日夜 / 晴雨 ──
        private void BuildLife()
        {
            TownLife.Build(_root, _propRoot, _sunLight, _cam);
            // ★ v238：农田装饰**重新加上**（稻草人 2 / 杂草 16 / 水桶 1 / 草垛 1 / 锄头 1 / 鸡 2）
            //   实现在 `TownFarmDecor`（坐标从 `TownLayout` 量、材质是 OWN_FARM_* + 每 90 帧校验）★
            TownFarmPark.BuildFarmDecor(_root);
            TownFarmPark.BuildParkPeople(_root);
            StripRoadClutter(true);            // v233/v237：商业街上的杂物堆**停用**（SetActive(false)，不销毁）
            UnshareSpecialRenderers(true);     // v235：椅子/喷泉/门/摊位台面 **断开共享材质**
            Physics.SyncTransforms();
        }

        // ══════════════════════════════════════════════════════════════════
        // v233：**商业街杂物清理**（用户三次说"没删干净"）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>匹配到的杂物名字（读数用；同一次清理里不重复）。</summary>
        private static readonly System.Collections.Generic.List<string> Stripped = new System.Collections.Generic.List<string>(64);
        private static int _strippedTotal;

        /// <summary>
        /// ★ v234：**改成白名单** ★（关键词猜不中是必然的：那些杂物多半叫 `ENV_*`/`DECOR_*` 甚至没名字）
        ///   商业街 ±6 米内，**只保留**这些前缀，其余**凡有 Renderer/Collider 的一律停用**。
        ///   ⚠️ 额外保留（用户点名要留的东西）：名字里带 `tree`/`bush`/`lamp`/`NPC` 的（树、灌木、灯、人）
        /// </summary>
        private static readonly string[] KeepPrefix =
        {
            "SHOP_", "TENT_", "WARE_", "HOME_", "CLOTH_", "PLOT_", "ZONE_", "ROAD_", "PLAZA", "SURF_",
            "GROUND", "FENCE", "LOT_", "PORTAL", "PARK_PERSON", "NPC", "BIRD_", "ANIMAL_", "CLOUD_",
            "DUST_", "LAMP", "LIFE", "FX_", "TOWN_", "GATE",
            // ★ v237：下面这些**全部**是 `RoadAudit` 跑出来的误杀，逐条有据可查 ★
            //   `PARK_`/`BENCH_` ：公园长椅就在街边（x 1.3~2.3 在 ±6 矩形内），
            //                      零件 `BENCH_Seat/Back/Leg`、父 `PARK_Bench`，原清单一条不命中
            //                      ⇒ 照清单硬套会把**长椅整条停掉**
            //   `DEC_Sign`       ：美术通道挂的店铺招牌，吊在 1.0~1.4 m，不是地面杂物
            //   `STREET_`        ：路面铺装（`STREET_Shop/CurbW/Link`），此前只靠 `ZONE_` 父级侥幸留住
            //   `HEDGE_`         ：城外**树篱环**（`BuildHedge`，`HEDGE_00`…），17 个落在矩形内
            //   `DECOR_`         ：右下角**装饰小屋**（`BuildDecorHouses`，`DECOR_ShedBody/Roof/Door`）
            //   `SIGN_`          ：未开发地块的**立牌**（`SIGN_Post/Board/Frame`）
            //   `MDL_`/`ANIM_`   ：角色模型根（`ANIM_farmer` = 玩家本体）——
            //                      玩家的水壶嘴就叫 `Jet`，父链上**没有**带 player 的名字，
            //                      不靠这两条就会被当杂物停掉 ⇒ 玩家手里少一件工具 ✗
            //   `FOUNT`/`WATER`  ：中央喷泉（`FOUNT_Basin/Water/Column…`）—— 用户点名要留
            "PARK_", "BENCH_", "DEC_Sign", "STREET_",
            "HEDGE_", "DECOR_", "SIGN_", "MDL_", "ANIM_", "FOUNT", "WATER",
        };

        /// <summary>
        /// 把商业街 ±6 米内的**杂物堆**（箱子/篮子/木桶/石堆/草垛…）停用。
        ///
        /// ★ 为什么在 `Update` 里隔一段再跑一次 ★
        ///   美术那路的 `TownArtDresser` 是 `DefaultExecutionOrder(300)`、在 `Start` 里挂装饰，
        ///   **晚于**本类的 `Awake` ⇒ 只在 `BuildLife` 里删一次会**漏掉它后加的那批**。
        /// ⇒ 首帧删一次（`BuildLife`）+ `Update` 里每 90 帧再扫一次。
        ///
        /// ★★ v237：真正的病根不是"跑得不够勤"，是**够不到** ★★
        ///   老 `StripWalk` 只走 `TOWN` 的**两层**（节点自己 + 直接子），而美术通道的杂物挂在
        ///       TOWN / TOWN_DRESS / DECO / **DEC_crate_stack**   ← 第三层
        ///   ⇒ 那个白名单**一次都没被 DEC_ 节点调用过**，扫一万帧也还是 0。
        ///   现在 `StripWalk` 是**全深度递归**（`RoadAudit` 报的 36 组 346 件全在同一棵树下）。
        /// </summary>
        private void StripRoadClutter(bool verbose)
        {
            if (_root == null) return;
            float x0 = TownLayout.ShopStreetXMin - 6f, x1 = TownLayout.ShopStreetXMax + 6f;
            float z0 = TownLayout.ShopStreetZMin - 6f, z1 = TownLayout.ShopStreetZMax + 6f;
            int n = 0;
            Stripped.Clear();
            for (int i = _root.childCount - 1; i >= 0; i--)
            {
                Transform child = _root.GetChild(i);
                if (child == null) continue;
                n += StripWalk(child, x0, x1, z0, z1);
            }
            _strippedTotal += n;
            // ★ v237：命中就一定要说话 —— 老版本 `Update` 那条传 `false`，
            //   于是"每 90 帧其实清掉了东西"这件事**永远不出现在控制台**，
            //   用户只能说"没删干净"，我也拿不到任何读数 ✗
            if (verbose || n > 0)
                Log.Info($"[Town] 商业街杂物清理：本次停用 {n} 个｜累计 {_strippedTotal} 个｜名字 = " +
                         (n > 0
                             ? string.Join(" / ", Stripped.GetRange(0, Mathf.Min(12, Stripped.Count))) +
                               (Stripped.Count > 12 ? " …（共 " + Stripped.Count + " 个）" : "")
                             : "(本帧无新增)") +
                         $" · 范围 x {x0:0.0}~{x1:0.0} z {z0:0.0}~{z1:0.0}");
        }

        /// <summary>
        /// **全深度**走完一棵子树，把裁决为杂物的节点 `SetActive(false)`，返回停用件数。
        /// 三条纪律：
        ///   ① **不用 `DestroyImmediate`** —— 只停用，随时能在 Inspector 里打开对照（也可一键恢复）
        ///   ② 整枝剪掉就**不再往下走**：父节点一停用，子树本来就不渲染，逐件再报只会把"1 个杂物"
        ///      数成"7 个零件"，读数就骗人了 ⇒ 计数 = **物件数**，不是零件数
        ///   ③ 已经停用的整枝**直接跳过**：`activeInHierarchy == false` 的子树里没有还能看见的东西，
        ///      不必每 90 帧重走一遍（这条是它敢全深度扫的性能依据）
        ///   ④ 空父节点（无 `Renderer`/`Collider`）永不被判为杂物 ⇒ 父级不会被我摘掉（裁决在 `WouldStrip` 里）
        /// </summary>
        private int StripWalk(Transform t, float x0, float x1, float z0, float z1)
        {
            if (t == null) return 0;
            if (!t.gameObject.activeInHierarchy) return 0;      // ③ 整枝已停用 ⇒ 不重复走
            if (IsClutter(t, x0, x1, z0, z1))
            {
                t.gameObject.SetActive(false);                  // ① 只停用，不销毁
                Stripped.Add(t.name);
                return 1;                                       // ② 整枝剪掉
            }
            int n = 0;
            for (int i = 0; i < t.childCount; i++)
                n += StripWalk(t.GetChild(i), x0, x1, z0, z1);   // 全深度递归（原来只到这里的一层）
            return n;
        }

        /// <summary>★ 白名单判定：在街上、有渲染件、且名字**不在**白名单 ⇒ 就是杂物 ★</summary>
        private static bool IsClutter(Transform t, float x0, float x1, float z0, float z1)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            Vector3 p = t.position;
            if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) return false;
            return WouldStrip(t);
        }

        /// <summary>
        /// 名字里**含**这些词也保留（不分大小写）：树 / 灌木 / 灯 / 人 / 花。
        /// `player` 不在用户给的清单里，但玩家本体 `TownPlayer` 在商业街正中央站着 ——
        /// 它的零件叫 `Bib`/`Arm_L`/`HatBrim`（**自己不带任何前缀**），
        /// 不靠这条兜住就会被当成杂物停用 ✗（这是"别把街清空"的底线，实测见 RoadAudit 读数）
        /// </summary>
        private static readonly string[] KeepWord = { "tree", "bush", "lamp", "npc", "flower", "player" };

        /// <summary>
        /// ★ 通道容器：**不把"保留权"传给子节点** ★
        ///   为什么要这一条：`TOWN_Props` / `TOWN_DRESS` 都以 `TOWN_` 开头，
        ///   若让前缀沿父链继承，那 `TOWN_DRESS/DECO/DEC_crate_stack` 会**跟着父名一起被保留** ⇒ 白名单失效 ✗
        ///   ⇒ 这几个名字只保它们自己，不保后代；后代必须**自己**命中前缀或词。
        ///   ⚠️ 必须写**完整名字**，不能写 `DEC` 这种短前缀 ——
        ///      否则 `DEC_Sign`（该留的招牌）也会被当通道，它的 `Board/Cap/PostL/PostR` 就保不住了 ✗
        /// </summary>
        private static readonly string[] ChannelNoInherit =
            { "TOWN_Props", "TOWN_DRESS", "TOWN_Plants", "DECO", "GRASS" };

        private static bool StartsAny(string nm, string[] keys)
        {
            for (int k = 0; k < keys.Length; k++)
                if (nm.StartsWith(keys[k], System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool ContainsAny(string nm, string[] keys)
        {
            for (int k = 0; k < keys.Length; k++)
                if (nm.IndexOf(keys[k], System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>名字自己命中保留规则（前缀 或 词）。</summary>
        private static bool MatchesKeep(string nm)
            => !string.IsNullOrEmpty(nm) && (StartsAny(nm, KeepPrefix) || ContainsAny(nm, KeepWord));

        /// <summary>
        /// 祖先里有一个"功能组"命中保留 ⇒ 这一件跟着留。
        /// 必须有这一条：公园人的零件叫 `Hat`/`Body`/`R`（**不带 LIFE_ 前缀**），
        /// 只判自己名字的话它们会被整批停用 —— 人只剩一顶帽子 ✗
        /// 而 `TOWN_`/`DECO`/`GRASS` 这些**通道**不传保留权（见 `ChannelNoInherit`）。
        /// </summary>
        private static bool KeptByAncestor(Transform t)
        {
            Transform p = t.parent;
            while (p != null)
            {
                string nm = p.name;
                if (!StartsAny(nm, ChannelNoInherit) && MatchesKeep(nm)) return true;
                p = p.parent;
            }
            return false;
        }

        /// <summary>
        /// ★★ 裁决的唯一实现（诊断 `RoadAudit` 与清理 `StripRoadClutter` 共用，保证同一口径）★★
        /// 返回 true = 这一件该停用。**不传矩形**：范围由调用方筛（诊断和清理的范围是同一个 ±6）。
        /// 顺序很重要：
        ///   ① 自己没有 Renderer 也没有 Collider ⇒ **不是杂物**（空父节点永不碰，否则会摘掉别人的父级）✗
        ///   ② 自己名字命中保留 ⇒ 留
        ///   ③ 有一个祖先命中保留（且该祖先不是通道容器）⇒ 留
        ///   ④ 其余 ⇒ 停用
        /// </summary>
        private static bool WouldStrip(Transform t)
        {
            if (t == null) return false;
            if (!t.gameObject.activeInHierarchy) return false;
            if (t.GetComponent<Renderer>() == null && t.GetComponent<Collider>() == null) return false;
            if (MatchesKeep(t.name)) return false;
            if (KeptByAncestor(t)) return false;
            return true;
        }

        /// <summary>
        /// 同 <see cref="WouldStrip"/>，但**不看 `activeInHierarchy`**。
        /// 用途：复核"已经停用的那批"是**不是我**停的 —— 诊断读数要能分得清
        /// 「我误杀」和「别的系统（传送门特效/LOD）自己关的」，否则停用数一涨全算我头上 ✗
        /// </summary>
        private static bool WouldStripIgnoringActive(Transform t)
        {
            if (t == null) return false;
            if (t.GetComponent<Renderer>() == null && t.GetComponent<Collider>() == null) return false;
            if (MatchesKeep(t.name)) return false;
            if (KeptByAncestor(t)) return false;
            return true;
        }


        /// <summary>读数：本次/累计清理了多少、名字各是什么；**若为 0 就打印街上所有渲染件的真实名字**。</summary>
        public string ClutterInfo(string ignored)
        {
            StripRoadClutter(true);
            var sb = new System.Text.StringBuilder(1800);
            sb.Append($"商业街杂物（白名单法）：本次停用 {Stripped.Count} 个｜累计 {_strippedTotal} 个｜" +
                      $"名字 = {(Stripped.Count > 0 ? string.Join(" / ", Stripped) : "(无匹配)")}");
            if (Stripped.Count == 0)
            {
                // ★ 两段式第二段：把街上剩下的真实名字全打出来，照着它写白名单 ★
                float x0 = TownLayout.ShopStreetXMin - 6f, x1 = TownLayout.ShopStreetXMax + 6f;
                float z0 = TownLayout.ShopStreetZMin - 6f, z1 = TownLayout.ShopStreetZMax + 6f;
                MeshRenderer[] all = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
                int shown = 0;
                sb.Append("\n  街上现有渲染件（真实名字，最多 40 个）：");
                for (int i = 0; i < all.Length && shown < 40; i++)
                {
                    MeshRenderer mr = all[i];
                    if (mr == null) continue;
                    Vector3 p = mr.transform.position;
                    if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue;
                    sb.Append("\n    ").Append(mr.name).Append("  @ (").Append(p.x.ToString("0.0"))
                      .Append(", ").Append(p.z.ToString("0.0")).Append(")");
                    shown++;
                }
                sb.Append("\n  ⇒ 共 ").Append(shown).Append(" 个（这份清单就是真实名字，照着它定白名单）");
            }
            string s = sb.ToString();
            Log.Info("[Town] CLUTTER " + s);
            return s;
        }

        /// <summary>农田装饰与公园中心人物的读数（坐标 + "没压田格" + 位移/朝向）。</summary>
        public string FarmParkInfo(string ignored) => TownFarmPark.Probe();

        /// <summary>
        /// ★ v238/v239：**农田装饰的只读诊断**（逐件：名字 / 世界坐标 / AABB / 到最近田格中心的距离 /
        /// 材质名 / `_MainTex`），外加杂草**两次计算的坐标比对**（确定性）与围栏汇总。
        /// 核心判据是汇总里那行"**最小距离 = X 米**"（必须 ≥ 1.5）。
        /// 实现在 `TownFarmDecor.Info()`；转发一份在这里，是因为自动化入口都在 `TownBootstrap` 上。
        /// </summary>
        public string FarmDecorInfo(string ignored) => TownFarmPark.FarmDecorInfo(ignored);

        /// <summary>强制跑一次材质校验（把 `OWN_FARM_*` / `OWN_FENCE_*` 换回、把 `_MainTex` 清空），返回修回处数。</summary>
        public int FarmDecorRepair(string ignored) => TownFarmDecor.Repair(true);

        /// <summary>
        /// ★ v239：**路边围栏的只读诊断** —— 到 `WalkRadius` 的最近距离（必须 &gt; 0）、门口缺口（3~4 米）、
        /// 柱距 / 柱高 / 材质 `OWN_FENCE_*`。几何来自纯函数 `TownFarmDecor.ComputeFencePlan`。
        /// </summary>
        public string RoadFenceInfo(string ignored) => TownFarmPark.RoadFenceInfo(ignored);

        // ══════════════════════════════════════════════════════════════════
        // v235：**椅子 / 喷泉 / 门 / 摊位台面 —— 断开共享材质**
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// 用户："椅子、喷泉异常地显示了地面的贴图"。
        /// 根因：`TownKit` 的图元共用材质池，美术那路的 `TownArtDresser` 会给**共享材质**塞 `_MainTex`
        ///       ⇒ 地面贴图跑到椅子/喷泉上 ✗
        /// 修法：给这几类"几何感强"的物件**每件一份独立材质**（用户已豁免），
        ///       shader 用 `Seedfall/ToonSimple`（工程必有）→ `Unlit/Color` → `Seedfall/CloudFlat`，
        ///       并且**显式把 `_MainTex` / `_BaseMap` 置空** ⇒ 谁都塞不进贴图 ✓
        /// 只做这几类（上限 12 件），**不做全场** ✓
        /// </summary>
        private static readonly System.Collections.Generic.List<string> Unshared =
            new System.Collections.Generic.List<string>(16);
        /// <summary>
        /// 关键词。★ 实测教训两条：
        ///   ① 喷泉零件叫 **`FOUNT_Basin`**（不是 `FOUNTAIN_*`）⇒ 第一版只写 `FOUNTAIN` 一个都没命中，
        ///      实测 `FOUNT_Basin` 的 `_MainTex` 仍是 `toon_stone` ✗（现在补了 `FOUNT`/`WATER`）
        ///   ② 曾经加了 `RIM` 想覆盖喷泉的"沿" ⇒ 结果误伤 `HatBrim`/`BasketRim`/`Trim`（公园人和衣服的边）
        ///      ⇒ **撤掉 `RIM`**（`FOUNT`/`WATER` 已覆盖喷泉全部 7 个零件）✓
        /// </summary>
        private static readonly string[] SpecialKw =
            { "FOUNT", "WATER", "CHAIR", "BENCH", "SEAT", "GATE", "COUNTER", "STALL", "TABLE" };

        /// <summary>
        /// ★ v236：**每个渲染器 ↔ 它那份 `OWN_*` 材质**的登记表。
        ///   为什么必须有：美术那路的 `TownArtDresser` 在 `Start`（order 300）里会给场景渲染物**重设材质/贴图**，
        ///   **晚于** `BuildLife` ⇒ 我克隆的 `OWN_*` 被它换掉（用户："一点都没变"就是这个）✗
        ///   ⇒ 每 30 帧重扫：**从表里取回那一份**（不新建，避免每 30 帧漏一堆材质），
        ///     材质被换掉就换回、`_MainTex` 被塞就清空 ✓
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<MeshRenderer, Material> Owned =
            new System.Collections.Generic.Dictionary<MeshRenderer, Material>(32);
        /// <summary>上一次重扫"修回"了几件（材质被换回 或 贴图被清掉）。</summary>
        public static int OwnedRepairedLast { get; private set; }
        /// <summary>当前登记在册的独立材质件数。</summary>
        public static int OwnedCount => Owned.Count;

        private int UnshareSpecialRenderers(bool verbose)
        {
            if (_root == null) return 0;
            Unshared.Clear();
            OwnedRepairedLast = 0;
            int made = 0;
            var all = _root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < all.Length && made < 24; i++)
            {
                MeshRenderer mr = all[i];
                if (mr == null) continue;
                // ★ 匹配"自己 + 父 + 祖父"的名字：喷泉的零件叫 `WATER_*`/`RIM_*`，关键字在父节点 `FOUNTAIN_*` 上
                //   （第一次实测只匹配了 `mr.name`，所以 12 件全是 BENCH_*，喷泉一个没命中 ✗）
                string nm = mr.name;
                string ctx = nm;
                Transform up = mr.transform.parent;
                for (int d = 0; d < 2 && up != null; d++)
                {
                    ctx += "|" + up.name;
                    up = up.parent;
                }
                bool hit = false;
                for (int k = 0; k < SpecialKw.Length; k++)
                    if (ctx.IndexOf(SpecialKw[k], System.StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                if (!hit) continue;

                // ★ 已在册：**不新建**，只把材质换回 / 把贴图清掉（每 30 帧重扫时走这条路）★
                if (Owned.TryGetValue(mr, out Material own) && own != null)
                {
                    bool repaired = false;
                    if (mr.sharedMaterial != own) { mr.sharedMaterial = own; repaired = true; }
                    if (own.HasProperty("_MainTex") && own.GetTexture("_MainTex") != null)
                    { own.SetTexture("_MainTex", null); repaired = true; }
                    if (own.HasProperty("_BaseMap") && own.GetTexture("_BaseMap") != null)
                    { own.SetTexture("_BaseMap", null); repaired = true; }
                    if (repaired) { OwnedRepairedLast++; Unshared.Add(nm + "(修回)"); }
                    made++;
                    continue;
                }

                Material old = mr.sharedMaterial;
                Color c = old != null && old.HasProperty("_Color") ? old.color : Color.grey;
                if (old != null && old.name != null && old.name.StartsWith("OWN_", System.StringComparison.Ordinal))
                    continue;                                  // 已经是独立材质，别重复克隆
                Shader sh = Shader.Find("Seedfall/ToonSimple");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                if (sh == null) sh = Shader.Find("Seedfall/CloudFlat");
                if (sh == null) continue;
                var m = new Material(sh) { name = "OWN_" + nm };
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
                mr.sharedMaterial = m;                         // 这一件从此与地面/其它件**不再共享** ✓
                Owned[mr] = m;                                 // ★ 登记：每 30 帧重扫时用这一份换回，不新建 ★
                Unshared.Add(nm);
                made++;
            }
            if (verbose)
                Log.Info($"[Town] 断开共享材质：**{made} 件**（在册 {Owned.Count} · 本次修回 {OwnedRepairedLast}）" +
                         (made > 0 ? "（" + string.Join(" / ", Unshared) + "）" : "（没匹配到椅/喷泉/门/台面）"));
            return made;
        }

        /// <summary>读数：断开了几件 + 名字；椅子/喷泉/地面 三者的材质 instanceID 必须互不相同。</summary>
        public string UnshareInfo(string ignored)
        {
            int n = UnshareSpecialRenderers(true);
            var sb = new System.Text.StringBuilder(900);
            sb.Append($"断开共享材质：{n} 件｜名字 = {(n > 0 ? string.Join(" / ", Unshared) : "(无)")}");
            string[] probeNames = { "CHAIR", "FOUNTAIN", "ZONE_" };
            for (int k = 0; k < probeNames.Length; k++)
            {
                MeshRenderer found = null;
                var all = _root != null ? _root.GetComponentsInChildren<MeshRenderer>(true) : new MeshRenderer[0];
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    if (all[i].name.IndexOf(probeNames[k], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { found = all[i]; break; }
                }
                if (found == null)
                {
                    // 退一步：按"父节点名"再找一次（喷泉零件叫 WATER_*/RIM_*，关键字在父节点上）
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null || all[i].transform.parent == null) continue;
                        if (all[i].transform.parent.name.IndexOf(probeNames[k], System.StringComparison.OrdinalIgnoreCase) >= 0)
                        { found = all[i]; break; }
                    }
                }
                if (found == null) { sb.Append($"\n  {probeNames[k]}：没找到"); continue; }
                Material m = found.sharedMaterial;
                // ⚠️ `GetInstanceID()` 在 6.4 已过时（会给项目加新警告 ✗）⇒ 用材质对象自身的 hash 作身份，
                //    同一份材质 hash 必相同 ⇒ "三者必须互不相同"的判据等价 ✓
                sb.Append($"\n  {probeNames[k]} 代表 = {found.name} · 材质={m?.name} · 身份={m?.GetHashCode()} · " +
                          $"_MainTex={(m != null && m.HasProperty("_MainTex") ? (m.GetTexture("_MainTex") == null ? "null ✓" : m.GetTexture("_MainTex").name) : "-")}");
            }
            sb.Append("\n  判读：CHAIR / FOUNTAIN / ZONE_ 三行的「身份」**必须互不相同**");
            string s = sb.ToString();
            Log.Info("[Town] UNSHARE " + s);
            return s;
        }

        /// <summary>传送门旋涡读数：每个环的 localRotation / position / lossyScale ＋ 激活的 ParticleSystem 数。</summary>
        public string PortalInfo(string ignored) => TownPortalFx.Probe();

        /// <summary>鸟轨迹闭合性：三条轨迹 t=0 与 t=T 的坐标差（应 &lt; 0.001）＋ 步长比。</summary>
        public string BirdLoopInfo(string ignored) => TownLife.BirdLoopProbe();

        /// <summary>
        /// ★ v232：**路边杂物清单（只报，不删）** ★
        ///   用户说"那堆杂物没删干净"，而它**不是**我刚删的两组（稻草人/杂草在田里、旋涡环在门里）。
        ///   ⇒ 先按**建它的那一个根物体**分组列出来，由用户裁定删哪组；本方法**只读不删**。
        ///   范围：商业街 ±2 米（`ShopStreetXMin/Max`、`ShopStreetZMin/Max` 各外扩 2）。
        /// </summary>
        public string RoadPropDiag(string ignored)
        {
            float x0 = TownLayout.ShopStreetXMin - 2f, x1 = TownLayout.ShopStreetXMax + 2f;
            float z0 = TownLayout.ShopStreetZMin - 2f, z1 = TownLayout.ShopStreetZMax + 2f;
            MeshRenderer[] all = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<MeshRenderer>>();
            int hits = 0;
            for (int i = 0; i < all.Length; i++)
            {
                MeshRenderer mr = all[i];
                if (mr == null) continue;
                Vector3 p = mr.transform.position;
                if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue;
                hits++;
                // 往上找到"直接挂在 TOWN 下"的那个祖先 = 建它的那组
                Transform t = mr.transform;
                while (t.parent != null && t.parent.name != "TOWN" && t.parent.parent != null) t = t.parent;
                string key = t.name;
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new System.Collections.Generic.List<MeshRenderer>();
                    groups[key] = list;
                }
                list.Add(mr);
            }
            var keys = new System.Collections.Generic.List<string>(groups.Keys);
            keys.Sort((a, b) => groups[b].Count.CompareTo(groups[a].Count));

            var sb = new System.Text.StringBuilder(1500);
            sb.Append($"路边（x {x0:0.0}~{x1:0.0}，z {z0:0.0}~{z1:0.0}）渲染件 = {hits} 个，按建它的根分组 {keys.Count} 组：");
            for (int k = 0; k < keys.Count && k < 14; k++)
            {
                var list = groups[keys[k]];
                float mnx = 999f, mxx = -999f, mnz = 999f, mxz = -999f;
                for (int j = 0; j < list.Count; j++)
                {
                    Vector3 p = list[j].transform.position;
                    if (p.x < mnx) mnx = p.x;
                    if (p.x > mxx) mxx = p.x;
                    if (p.z < mnz) mnz = p.z;
                    if (p.z > mxz) mxz = p.z;
                }
                sb.Append("\n  【").Append(keys[k]).Append("】**").Append(list.Count).Append(" 件**")
                  .Append(" · x ").Append(mnx.ToString("0.0")).Append("~").Append(mxx.ToString("0.0"))
                  .Append(" · z ").Append(mnz.ToString("0.0")).Append("~").Append(mxz.ToString("0.0"))
                  .Append(" · 代表：").Append(list[0].name);
                for (int j = 1; j < list.Count && j < 3; j++) sb.Append(" / ").Append(list[j].name);
            }
            sb.Append("\n  ⇒ 本方法**只报不删**；请按组名裁定要删哪一组");
            string s = sb.ToString();
            Log.Info("[Town] ROADPROPS " + s);
            return s;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v237：**商业街路面真相诊断**（只读：不删、不停用、不换材质）★
        // ══════════════════════════════════════════════════════════════════
        /// <summary>
        /// 用户第三次说"路面杂物一点都没变"。在再改一行清理逻辑之前，先把事实钉死。
        ///
        /// ── 为什么旧的 `StripRoadClutter` 必然无效（机理）──
        ///   `StripWalk` 只走 `TOWN` 的**两层**（节点自己 + 直接子节点）。
        ///   而美术通道 `TownArtDresser` 的东西是这么挂的：
        ///       TOWN / TOWN_DRESS / DECO / **DEC_xxx**      ← 第三个 '/' 之后 = 第三层 ✗
        ///       TOWN / TOWN_Props / ENV_xxx                  ← 第二层 ✓（这层其实扫到了）
        ///   ⇒ 真正的杂物（`DEC_crate_stack` / `DEC_haystack` …）**一次都没被访问过**，
        ///     白名单写得再准也不可能有读数。这就是"没删干净"的唯一根因。
        ///
        /// ── 本方法 ──
        ///   走 `TOWN` 下**全部深度**，凡 `Renderer` 在商业街 ±6 米矩形内的，
        ///   按**名字前缀**分组打印：件数 + 代表名字 + 每件的前两层父链/世界坐标/AABB 尺寸/材质名。
        ///   顺带给出"白名单会怎么判"的那一列（裁决逻辑与清理共用 `WouldStrip`，保证同一口径）。
        ///   调用：`unity_drive send TownBootstrap TownBootstrap RoadAudit ""`
        /// </summary>
        public string RoadAudit(string ignored)
        {
            if (_root == null) return "(TOWN 还没建 —— 本方法要在主城构建完之后调)";
            float x0 = TownLayout.ShopStreetXMin - 6f, x1 = TownLayout.ShopStreetXMax + 6f;
            float z0 = TownLayout.ShopStreetZMin - 6f, z1 = TownLayout.ShopStreetZMax + 6f;

            Renderer[] all = _root.GetComponentsInChildren<Renderer>(true);
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<Renderer>>();
            var order = new System.Collections.Generic.List<string>();
            int inRect = 0, liveInRect = 0, deadInRect = 0, doomed = 0;
            for (int i = 0; i < all.Length; i++)
            {
                Renderer rd = all[i];
                if (rd == null) continue;
                Vector3 p = rd.transform.position;
                if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue;
                inRect++;
                if (!rd.gameObject.activeInHierarchy) { deadInRect++; continue; }
                liveInRect++;
                if (WouldStrip(rd.transform)) doomed++;
                string key = PrefixOf(rd.transform.name);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new System.Collections.Generic.List<Renderer>();
                    groups[key] = list;
                    order.Add(key);
                }
                list.Add(rd);
            }
            order.Sort((a, b) => groups[b].Count.CompareTo(groups[a].Count));

            var sb = new System.Text.StringBuilder(6000);
            sb.Append("商业街 ±6 米（x ").Append(x0.ToString("0.0")).Append("~").Append(x1.ToString("0.0"))
              .Append("，z ").Append(z0.ToString("0.0")).Append("~").Append(z1.ToString("0.0"))
              .Append("）｜带 Renderer 共 **").Append(inRect).Append("** 个｜其中启用中 ").Append(liveInRect)
              .Append("（已停用 ").Append(deadInRect).Append("）｜按新白名单**会停用 ").Append(doomed).Append(" 个**")
              .Append("｜全部深度（含 TOWN_DRESS/DECO 第三层）｜分组 ").Append(order.Count).Append(" 组");

            // ── 一、按名字前缀分组：件数 + 代表名字 + 裁决 ──
            for (int k = 0; k < order.Count; k++)
            {
                var list = groups[order[k]];
                int killed = 0;
                for (int j = 0; j < list.Count; j++) if (WouldStrip(list[j].transform)) killed++;
                sb.Append("\n  【").Append(order[k]).Append("】**").Append(list.Count).Append(" 件**")
                  .Append(killed == list.Count ? " → 全部停用" : killed == 0 ? " → 全部保留" : " → 停用 " + killed + " 件")
                  .Append(" · 代表：");
                for (int j = 0; j < list.Count && j < 5; j++) sb.Append(j > 0 ? " / " : "").Append(list[j].transform.name);
                if (list.Count > 5) sb.Append(" …");
            }

            // ── 二、会被停用的每一件的完整取证（名字·父链·坐标·尺寸·材质）──
            sb.Append("\n── 会被停用的明细（名字｜父链前两层｜世界坐标｜AABB 尺寸｜共享材质）──");
            int shown = 0;
            for (int k = 0; k < order.Count && shown < 60; k++)
            {
                var list = groups[order[k]];
                for (int j = 0; j < list.Count && shown < 60; j++)
                {
                    Renderer rd = list[j];
                    if (rd == null || !WouldStrip(rd.transform)) continue;
                    Transform t = rd.transform;
                    Bounds b = rd.bounds;
                    Material m = rd.sharedMaterial;
                    sb.Append("\n  ").Append(t.name)
                      .Append("  ← ").Append(Chain2(t))
                      .Append("  @(").Append(t.position.x.ToString("0.00")).Append(", ")
                      .Append(t.position.y.ToString("0.00")).Append(", ").Append(t.position.z.ToString("0.00")).Append(')')
                      .Append("  size=(").Append(b.size.x.ToString("0.00")).Append(", ")
                      .Append(b.size.y.ToString("0.00")).Append(", ").Append(b.size.z.ToString("0.00")).Append(')')
                      .Append("  mat=").Append(m != null ? m.name : "**null**");
                    shown++;
                }
            }
            if (shown == 0) sb.Append("\n  （0 件 —— 街面上没有白名单之外的渲染件）");

            // ── 三、已经被停用的那些（老逻辑干的）：用来证明它**既漏又滥** ──
            var deadG = new System.Collections.Generic.Dictionary<string, int>();
            var deadNames = new System.Collections.Generic.Dictionary<string, string>();
            var deadRep = new System.Collections.Generic.Dictionary<string, Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer rd = all[i];
                if (rd == null || rd.gameObject.activeInHierarchy) continue;
                Vector3 p = rd.transform.position;
                if (p.x < x0 || p.x > x1 || p.z < z0 || p.z > z1) continue;
                string key = PrefixOf(rd.transform.name);
                deadG.TryGetValue(key, out int c);
                deadG[key] = c + 1;
                if (!deadNames.ContainsKey(key)) { deadNames[key] = rd.transform.name; deadRep[key] = rd.transform; }
                else if (c > 0 && c < 3) deadNames[key] += " / " + rd.transform.name;
            }
            sb.Append("\n── **已被停用**（矩形内 ").Append(deadInRect).Append(" 个，按前缀）──");
            foreach (var kv in deadG)
            {
                bool mine = WouldStripIgnoringActive(deadRep[kv.Key]);
                sb.Append("\n  【").Append(kv.Key).Append("】").Append(kv.Value).Append(" 件 · 代表 ")
                  .Append(deadNames[kv.Key])
                  .Append("  ← ").Append(Chain2(deadRep[kv.Key]))
                  .Append("  @(").Append(deadRep[kv.Key].position.x.ToString("0.0")).Append(", ")
                  .Append(deadRep[kv.Key].position.z.ToString("0.0")).Append(')')
                  .Append(mine ? " 〔本条是我停的〕" : " 〔**不是我**停的：另有系统停的〕");
            }

            // ── 四、美术通道 `TOWN_DRESS` 的容器结构（这是"够不到"的那一支）──
            Transform dress = _root.Find("TOWN_DRESS");
            sb.Append("\n── TOWN_DRESS 容器结构（" ).Append(dress != null ? "存在" : "不存在").Append("）──");
            if (dress != null)
            {
                for (int i = 0; i < dress.childCount; i++)
                {
                    Transform c = dress.GetChild(i);
                    sb.Append("\n  ").Append(c.name).Append('/')
                      .Append(" 渲染件=").Append(c.GetComponentsInChildren<Renderer>(true).Length)
                      .Append(" 子=").Append(c.childCount);
                    for (int j = 0; j < c.childCount && j < 24; j++)
                        sb.Append(j > 0 ? " / " : " ⇒ ").Append(c.GetChild(j).name);
                    if (c.childCount > 24) sb.Append(" …");
                }
            }

            string s = sb.ToString();
            Log.Info("[Town] ROADAUDIT " + s);
            return s;
        }

        /// <summary>名字前缀分组键：第一个 '_' 之前（**含 '_'**）；没有 '_' 就取开头的字母数字串。</summary>
        private static string PrefixOf(string nm)
        {
            if (string.IsNullOrEmpty(nm)) return "(无名)";
            int u = nm.IndexOf('_');
            if (u >= 0 && u <= 24) return nm.Substring(0, u + 1);
            int i = 0;
            while (i < nm.Length && i < 12 && (char.IsLetterOrDigit(nm[i]) || nm[i] == '.')) i++;
            return i > 0 ? nm.Substring(0, i) : "(符号)";
        }

        /// <summary>父链前两层，读作 `爷爷/爸爸/`（只报名字，供人对号）。</summary>
        private static string Chain2(Transform t)
        {
            string a = t.parent != null ? t.parent.name : "(无父)";
            string b = t.parent != null && t.parent.parent != null ? t.parent.parent.name : "";
            return b.Length > 0 ? b + "/" + a + "/" : a + "/";
        }

        /// <summary>
        /// ★ v228：**共享材质污染诊断**（用户："椅子、喷泉异常地显示了地面的贴图"）★
        ///   原理：`TownKit` 的图元都**共用**同一份材质（这正是"共享"的意义）。
        ///   只要任何一处写了 `mr.sharedMaterial.mainTexture = 地贴图`（或对该材质 `SetTexture`），
        ///   **所有用同一材质的物件都会一起变** —— 椅子/喷泉显示泥土贴图就是这个。
        ///   判据（按你给的）：把 **材质种类按 `mainTexture.name` 分组**，报每组几件 + 举 3 个 owner。
        ///     若"地面 / 椅子 / 喷泉"落在**同一个材质 instanceID** ⇒ 共享被改；
        ///     若 instanceID 不同而贴图名相同 ⇒ 是贴图分配错（另一类 bug）。
        /// </summary>
        public string MatDiag(string filter)
        {
            string f = (filter ?? "").Trim();
            // ⚠️ 用**不带 `FindObjectsSortMode`** 的重载（带它的那个在 6.4 里已标记过时，会给项目加新警告 ✗）
            MeshRenderer[] all = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude);
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<MeshRenderer>>();
            for (int i = 0; i < all.Length; i++)
            {
                MeshRenderer mr = all[i];
                if (mr == null) continue;
                if (f.Length > 0 && mr.name.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Material m = mr.sharedMaterial;
                // ⚠️ `GetInstanceID()` 在 6.4 已过时 ⇒ 用**材质对象自身的 hash** 作为身份（同一份材质 = 同一 hash）
                string key = m == null ? "(null 材质)"
                    : (m.mainTexture != null ? "贴图=" + m.mainTexture.name : "无贴图") +
                      "｜材质=" + m.name + "｜身份=" + m.GetHashCode();
                if (!groups.TryGetValue(key, out var list)) { list = new System.Collections.Generic.List<MeshRenderer>(); groups[key] = list; }
                list.Add(mr);
            }

            var keys = new System.Collections.Generic.List<string>(groups.Keys);
            keys.Sort((a, b) => groups[b].Count.CompareTo(groups[a].Count));

            var sb = new System.Text.StringBuilder(1600);
            sb.Append($"材质分组诊断：渲染件 {all.Length} 个（过滤=\"{f}\"）→ {keys.Count} 组");
            int dirty = 0, shown = 0;
            for (int k = 0; k < keys.Count; k++)
            {
                var list = groups[keys[k]];
                bool hasTex = keys[k].StartsWith("贴图=", System.StringComparison.Ordinal);
                if (hasTex) dirty += list.Count;
                if (shown < 10)
                {
                    sb.Append("\n  ").Append(keys[k]).Append(" ⇒ **").Append(list.Count).Append(" 件**：");
                    for (int j = 0; j < list.Count && j < 3; j++) sb.Append(list[j].name).Append(" / ");
                    shown++;
                }
            }
            sb.Append("\n  ⇒ 带贴图的渲染件合计 = ").Append(dirty)
              .Append(" ｜ 判读：**同一个「身份」却出现在地面/椅子/喷泉上 ⇒ 共享材质被改**；")
              .Append("「身份」不同而贴图名相同 ⇒ 贴图分配错（另一类 bug）");
            string s = sb.ToString();
            Log.Info("[Town] MATDIAG " + s);
            return s;
        }

        /// <summary>云材质运行时诊断：每个部件的 材质名 / 颜色 / `_MainTex` 是否为空 / 贴图尺寸。</summary>
        public string CloudInfo(string ignored) => TownLife.DiagCloudMaterials();

        /// <summary>活气层读数（六样各自的每帧变化量 ＋ 日夜/雨状态 ＋ fps）。`"off"`/`"on"` 切整层做 A/B。</summary>
        public string LifeInfo(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            if (m == "off") { TownLife.SetVisible(false); return "活气层已隐藏（A/B 量 fps 用）"; }
            if (m == "on") { TownLife.SetVisible(true); return "活气层已恢复"; }
            return TownLife.Probe();
        }

        /// <summary>强制把日夜推进到某个进度（0~1）—— 验收"雨天"截图用。</summary>
        public string SetDayTime(string t01)
        {
            if (!float.TryParse(t01, out float v)) { Log.Warn(Tag + "SetDayTime 需要 0~1"); return "参数不是数字"; }
            TownLife.ForceTime(Mathf.Repeat(v, 1f) * TownLife.DayCycleSeconds);
            return TownLife.Probe();
        }

        // ══════════════════════════════════════════════════════════════════
        // 相机（★ §⑦：拉近 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 跟随机位的正交半高（= 看多远）。**9.5 → 8.6**：视野收窄约 9%，
        /// 16:9 下可见地面从 34×19 米变成 30.6×17.2 米 ⇒ 豆豆人和建筑都更大一点。
        /// </summary>
        private const float FollowOrthoSize = 8.6f;

        /// <summary>
        /// 跟随机位到焦点的**距离**（米）。**34 → 26**：机位往前挪 8 米，
        /// 正交相机的位置不改视野大小，但改"透视感/阴影落点"与"玩家在画面里的位置"——
        /// 用户说的"摄像机拉近"这两条一起改才读得出来 ✓
        /// </summary>
        private const float FollowDistance = 26f;

        /// <summary>跟随档的俯角（度）。</summary>
        private const float FollowPitchDeg = 58f;

        /// <summary>
        /// 俯视档的正交半高（整图取景）。
        /// ⚠️ **21 → 14（v214，小城）→ 23.2（v215）**：用户这一轮要求"主城扩大半径"，
        ///    整城半径从 13.6 涨到 22.0 ⇒ 俯视档必须 ≥ 22 才能把整座城装进一张图（22 + 1.2 边距）✓
        ///    跟随档（玩家真正看到的那个）不受影响，仍然是 8.6 / 机位 26 ✓
        /// </summary>
        private const float TopOrthoSize = TownLayout.TownRadius + 1.2f;

        private void UpdateCamera()
        {
            if (_cam == null) return;

            if (_topView)
            {
                // 纯俯视（给"俯视图"截图用）：正交、90° 俯角、覆盖整张图
                _cam.orthographic = true;
                _cam.orthographicSize = TopOrthoSize;
                _cam.transform.position = new Vector3(0f, 40f, 0f);
                _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                return;
            }

            float pitch = FollowPitchDeg * Mathf.Deg2Rad;
            Vector3 focus = _avatar != null
                ? new Vector3(_avatar.Position.x, 0.8f, _avatar.Position.z)
                : new Vector3(0f, 0.8f, 0f);
            Vector3 offset = new Vector3(0f, Mathf.Sin(pitch), -Mathf.Cos(pitch)) * FollowDistance;

            // ★ 跟随档必须**把正交尺寸写回来** ★
            //   俯视档会把它推到 14，而 `orthographicSize` 是**粘性**的：只改位置与朝向后，
            //   跟随镜下仍然是 14 ⇒ 视野过大 + 能看到地图边缘以外的天空 ✗
            //   结论：两档各自把自己的**全部**相机参数写全，不做"只改变了的那个"的省事写法 ✓
            _cam.orthographic = true;
            _cam.orthographicSize = FollowOrthoSize;
            _cam.transform.position = focus + offset;
            _cam.transform.rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);
        }

        // ══════════════════════════════════════════════════════════════════
        // 作物表现（36 块地）
        // ══════════════════════════════════════════════════════════════════

        private void SyncPlots()
        {
            for (int i = 0; i < TownState.PlotCount; i++)
            {
                PlotView v = _plotViews[i];
                int si = _state.PlotSeed[i];
                if (si < 0)
                {
                    if (v.Root != null) v.Root.SetActive(false);
                    v.Seed = -1;
                    continue;
                }

                if (v.Root == null)
                {
                    BuildPlant(i, v);
                    v.Seed = -1;
                }
                if (v.Seed != si) { ApplyLook(v, si); v.Seed = si; }
                if (!v.Root.activeSelf) v.Root.SetActive(true);

                TownSeedDef d = TownData.SeedAt(si);
                if (d == null) continue;
                float prog = d.Progress(_state.PlotElapsed[i]);
                float h = d.size * Mathf.Lerp(0.30f, 1.0f, prog);

                float stemH = h * 0.55f;
                float headR = h * 0.42f;
                v.Stem.localScale = new Vector3(headR * 0.30f, stemH * 0.5f, headR * 0.30f);
                v.Stem.localPosition = new Vector3(0f, stemH * 0.5f, 0f);
                v.Head.localScale = new Vector3(headR, headR * 1.15f, headR);
                v.Head.localPosition = new Vector3(0f, stemH + headR * 0.35f, 0f);

                bool mature = _state.PlotMature[i];
                if (v.MatureMark != null) v.MatureMark.SetActive(mature);
            }
        }

        private void BuildPlant(int plot, PlotView v)
        {
            Vector2 c = PlotCenter(plot);
            var root = new GameObject("PLANT_" + (plot + 1).ToString("00"));
            root.transform.SetParent(_plantRoot, false);
            root.transform.position = new Vector3(c.x, PlotTopY, c.y);

            v.Root = root;
            v.Stem = PartLocal(root.transform, "Stem", TownKit.Prim(PrimitiveType.Cylinder), TownKit.Mat(TownColors.Grass));
            v.Head = PartLocal(root.transform, "Head", TownKit.Prim(PrimitiveType.Sphere), TownKit.Mat(TownColors.PlotSoil));
            v.MatureMark = PartLocal(root.transform, "Mature", TownKit.Prim(PrimitiveType.Sphere),
                                     TownKit.Mat(TownColors.Gold)).gameObject;
            v.MatureMark.transform.localScale = Vector3.one * 0.13f;
            v.MatureMark.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            v.MatureMark.SetActive(false);
        }

        private void ApplyLook(PlotView v, int seedIndex)
        {
            Material m = TownKit.Mat(TownData.TintAt(seedIndex));
            v.Head.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        private static Transform PartLocal(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go.transform;
        }

        // ══════════════════════════════════════════════════════════════════
        // 给 HUD / 自动化用的只读口
        // ══════════════════════════════════════════════════════════════════

        /// <summary>小地图上的玩家位置（世界 x/z）。</summary>
        public Vector2 PlayerMapPosition => _avatar != null
            ? new Vector2(_avatar.Position.x, _avatar.Position.z)
            : Vector2.zero;

        /// <summary>小地图上的朝向刻线。</summary>
        public Vector2 PlayerFacing => _avatar != null ? _avatar.Facing : Vector2.up;

        /// <summary>碰撞体的分类统计（"贴不贴合"这件事的读数）。</summary>
        private string DescribeColliders()
        {
            var all = _root.GetComponentsInChildren<Collider>(true);
            int box = 0, sphere = 0, mesh = 0, convex = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is BoxCollider) box++;
                else if (all[i] is SphereCollider) sphere++;
                else if (all[i] is MeshCollider mc) { mesh++; if (mc.convex) convex++; }
            }
            int rigidbody = _root.GetComponentsInChildren<Rigidbody>(true).Length;
            return $"共 {all.Length} 个（Box {box} · Sphere {sphere} · MeshCollider {mesh} 其中 convex {convex}）· " +
                   $"Rigidbody {rigidbody} 个（主城不用物理推动玩家）";
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 自动化入口（与 `DebugApi` 同一套用途：给外部一个可调用的口子）★
        //   全部只做"玩家能做的事"，不绕过任何规则：`Plant` 走 `TownState.Plant`（照样扣仓库），
        //   `Interact` 走与 E 键完全相同的那条分支，`Walk` 走与键盘完全相同的移动/碰撞路径。
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ 碰撞验收（**同步**跑完，不依赖帧循环/窗口焦点）★
        ///
        /// 对六座建筑各做一次「站在它的 E 锚点上 → 朝它走 <paramref name="seconds"/> 秒」：
        /// 逐步调用 <see cref="TownAvatar.Tick"/>（**与玩家按键完全同一条移动 + 碰撞代码**），
        /// 每步 1/60 秒，共 `seconds × 60` 步。跑完把"停在哪、被挡了几帧、最后一撞是谁"一次报出来。
        ///
        /// 为什么不用"真的按 W 走 3 秒"：那要 3 秒实时 + 窗口有焦点 + 没有别人抢桥；
        /// 而这个入口是**确定性**的（同输入同输出），报告里的坐标可以逐位复现 ✓
        /// </summary>
        public string CollideTour(string seconds)
        {
            if (_avatar == null) return "没有玩家";
            float secs = 3f;
            if (!string.IsNullOrWhiteSpace(seconds)) float.TryParse(seconds.Trim(), out secs);
            secs = Mathf.Clamp(secs, 0.2f, 10f);
            int frames = Mathf.RoundToInt(secs * 60f);

            var sb = new System.Text.StringBuilder(900);
            sb.Append("每栋楼：站到 E 锚点 → 朝楼走 ").Append(secs.ToString("0.0")).Append(" 秒（").Append(frames)
              .Append(" 步 @1/60）");
            TourOne(sb, frames, "大传送门", TownLayout.PortalAnchor, TownLayout.PortalPos);
            TourOne(sb, frames, "服装店", TownLayout.ClothingAnchor, TownLayout.ClothPos);
            TourOne(sb, frames, "杂货商店", TownLayout.ShopAnchor, TownLayout.ShopPos);
            TourOne(sb, frames, "种子仓库", TownLayout.WarehouseAnchor, TownLayout.WarePos);
            TourOne(sb, frames, "集市帐篷", new Vector2(TownLayout.TentPos.x, 1.2f), TownLayout.TentPos);
            TourOne(sb, frames, "家（图鉴）", TownLayout.HomeAnchor, TownLayout.HomePos);
            TourOne(sb, frames, "南门", new Vector2(0f, TownLayout.GatePos.y + 2.6f), TownLayout.GatePos);

            // 反例：从农田南端起朝北穿过 36 块耕地 + 草地 ⇒ 应该**一点也不挡**（"农田/道路要能走"）
            Vector3 startP = new Vector3(TownLayout.FarmCenter.x, TownAvatar.GroundY,
                                         TownLayout.FarmCenter.y - TownLayout.FarmHalf + 0.6f);
            _avatar.Teleport(startP);
            _avatar.ResetBlockedCounter();
            Vector3 before = _avatar.Position;
            for (int i = 0; i < frames; i++) _avatar.Tick(Vector2.up, 1f / 60f);
            float moved = new Vector2(_avatar.Position.x - before.x, _avatar.Position.z - before.z).magnitude;
            sb.Append("\n反例（可通行）：从农田西端 (")
              .Append(startP.x.ToString("0.00")).Append(',').Append(startP.z.ToString("0.00"))
              .Append(") 朝东穿过 36 块耕地 ⇒ 位移 ").Append(moved.ToString("0.00"))
              .Append(" 米（期望 ≈ ").Append((TownAvatar.WalkSpeed * secs).ToString("0.0"))
              .Append("）· 被挡 ").Append(_avatar.BlockedFrames).Append(" 帧")
              .Append(" · 终点 (").Append(_avatar.Position.x.ToString("0.00")).Append(',')
              .Append(_avatar.Position.z.ToString("0.00")).Append(")");

            _avatar.Teleport(SpawnPos);
            _avatar.ResetBlockedCounter();
            string text = sb.ToString();
            Log.Info(Tag + "COLLIDE " + text);
            return text;
        }

        private void TourOne(System.Text.StringBuilder sb, int frames, string label, Vector2 anchor, Vector2 target)
        {
            _avatar.Teleport(new Vector3(anchor.x, TownAvatar.GroundY, anchor.y));
            _avatar.ResetBlockedCounter();
            Vector2 outward = (target - anchor).normalized;      // 朝这栋楼
            for (int i = 0; i < frames; i++) _avatar.Tick(outward, 1f / 60f);

            Vector2 p = new Vector2(_avatar.Position.x, _avatar.Position.z);
            sb.Append("\n  ").Append(label).Append("：锚点 (")
              .Append(anchor.x.ToString("0.00")).Append(',').Append(anchor.y.ToString("0.00"))
              .Append(") → 停 (").Append(p.x.ToString("0.00")).Append(',').Append(p.y.ToString("0.00"))
              .Append(")｜被挡 ").Append(_avatar.BlockedFrames).Append(" 帧｜最后撞到 ").Append(_avatar.LastHit);
        }

        /// <summary>瞬移到 `"x,z"` 并回读此刻的「最近可交互物 + 提示文本」（§④ 的空仓库提示取证用）。</summary>
        public string HintAt(string csv)
        {
            Teleport(csv);
            UpdateActTarget();
            string line = $"位置 ({_avatar.Position.x:0.00},{_avatar.Position.z:0.00})｜焦点 {_actKind}" +
                          (_actPlot >= 0 ? "#" + (_actPlot + 1) : "") + "｜提示：" + (_actHint ?? "(无)") +
                          "｜仓库合计 " + _state.WarehouseTotal + " 颗";
            Log.Info(Tag + "HINT " + line);
            return line;
        }

        /// <summary>
        /// 「商店经济闭环」一条命令跑完（§③§⑤ 的读数来源）：
        ///   ① 打 N 次 BOSS（走 `TownDrops.OnBossKilled`，与战斗里真击杀同一条链路）→ 仓库有种子
        ///   ② 把仓库里所有主城种子**全部卖掉**（`TownState.SellSeed`，回收价来自 town_seeds.json）
        ///   ③ 用卖来的金币买**最便宜的一件上架解锁**（`TownUnlocks.TryUnlock`）
        ///   ④ 报买入前/后的 `IsUnlocked` 读数（老货恒 True、新货 False → True）
        /// </summary>
        public string EconomyTour(string kills)
        {
            int n = 6;
            if (!string.IsNullOrWhiteSpace(kills)) int.TryParse(kills.Trim(), out n);
            n = Mathf.Clamp(n, 1, 40);

            var sb = new System.Text.StringBuilder(900);
            sb.Append("① 仓库起始：").Append(_state.Snapshot());

            for (int k = 0; k < n; k++)
                TownDrops.OnBossKilled("boss", new Vector2(_avatar != null ? _avatar.Position.x : 0f,
                                                           _avatar != null ? _avatar.Position.z : 0f));
            sb.Append("\n② 打了 ").Append(n).Append(" 次 BOSS ⇒ 击杀 ").Append(TownDrops.BossKills)
              .Append(" 次、累计入库 ").Append(TownDrops.DroppedTotal).Append(" 颗｜仓库 ");
            AppendWarehouse(sb);

            long beforeCoins = _state.Coins;
            long gained = 0;
            var sold = new System.Text.StringBuilder(120);
            for (int i = 0; i < _state.Warehouse.Length; i++)
            {
                int have = _state.Warehouse[i];
                if (have <= 0) continue;
                long g = _state.SellSeed(i, have);
                gained += g;
                sold.Append(TownData.SeedAt(i)?.name).Append('×').Append(have).Append("=+").Append(g).Append(" 金  ");
            }
            sb.Append("\n③ 全部卖成金币：").Append(sold.Length > 0 ? sold.ToString() : "(仓库空，没得卖)")
              .Append("｜金币 ").Append(beforeCoins).Append(" → ").Append(_state.Coins);
            TownHud.LastSellLine = $"卖出合计 +{gained} 金（金币 {_state.Coins}）";

            // ④ 买最便宜的一件「上架」解锁
            int pick = -1;
            for (int i = 0; i < TownUnlocks.Count; i++)
            {
                TownUnlockDef d = TownUnlocks.At(i);
                if (!d.onSale || TownUnlocks.Owned(i)) continue;
                if (pick < 0 || d.price < TownUnlocks.At(pick).price) pick = i;
            }
            if (pick < 0)
            {
                sb.Append("\n④ 没有可买的解锁（表空 / 都买过了）");
            }
            else
            {
                TownUnlockDef d = TownUnlocks.At(pick);
                bool was = TownUnlocks.IsUnlocked(d.id);
                bool ok = TownUnlocks.TryUnlock(pick);
                sb.Append("\n④ 买解锁 #").Append(pick).Append("「").Append(d.name).Append("」(").Append(d.price)
                  .Append(" 金) ⇒ ").Append(ok ? "成交" : "失败：" + TownUnlocks.LastError)
                  .Append("｜IsUnlocked(\"").Append(d.id).Append("\")：").Append(was).Append(" → ")
                  .Append(TownUnlocks.IsUnlocked(d.id))
                  .Append("｜金币 ").Append(_state.Coins)
                  .Append("\n   老货对照仍是 True：IsUnlocked(\"iron_hoe\")=").Append(TownUnlocks.IsUnlocked("iron_hoe"));
            }

            string text = sb.ToString();
            Log.Info(Tag + "ECONOMY " + text);
            return text;
        }

        private void AppendWarehouse(System.Text.StringBuilder sb)
        {
            sb.Append('[');
            for (int i = 0; i < _state.Warehouse.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_state.Warehouse[i]);
            }
            sb.Append(']');
        }

        /// <summary>
        /// ★ v237 探针入口 ①：起一轮"商店 / 服装店 / 图鉴"三处滚动面板的实机读数。
        /// 探针自己建宿主对象（`TownUiProbe.Ensure`），读数逐行打在控制台的 `[TownProbe]` 里。
        /// 只读：不买东西、不改金币、不写盘 ✓
        /// </summary>
        public string UiScrollProbe(string ignored)
        {
            TownUiProbe.Run();
            return "已开始：控制台看 [TownProbe] 行（内容高/视口高/可滚/scroll.y/偏移）";
        }

        /// <summary>★ v237 探针入口 ②③：植物刷新池的差额读数 + 8 件默认插件的"装前/装后"对照。</summary>
        public string UiPoolProbe(string ignored) => TownUiProbe.PoolsReadout();

        /// <summary>商店读数：解锁表规模、各页上架数、回收价目（§③ 取证用）。</summary>
        public string ShopInfo(string ignored)
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("商店不卖种子｜解锁表 ").Append(TownUnlocks.Count).Append(" 条（上架 ").Append(TownUnlocks.OnSaleCount)
              .Append("）｜已解锁 ").Append(TownUnlocks.OwnedCount);
            sb.Append("\n  道具/插件/植物 待解锁 = ").Append(CountKind(TownUnlockDef.KindItem)).Append(" / ")
              .Append(CountKind(TownUnlockDef.KindPlugin)).Append(" / ").Append(CountKind(TownUnlockDef.KindPlant));
            sb.Append("\n  上架 = ").Append(CountOnSale(TownUnlockDef.KindItem)).Append(" 道具 / ")
              .Append(CountOnSale(TownUnlockDef.KindPlugin)).Append(" 插件 / ")
              .Append(CountOnSale(TownUnlockDef.KindPlant)).Append(" 植物");
            sb.Append("\n  收种子回收价：");
            for (int i = 0; i < TownData.Seeds.Length; i++)
                sb.Append(TownData.Seeds[i].name).Append('=').Append(TownData.Seeds[i].SellPrice).Append("金 ");
            sb.Append("\n  ").Append(TownUnlocks.Describe());
            // ★ v237：战斗侧刷新池的差额读数（用户报的"机制种子完全不刷新"就在这里取证）★
            try
            {
                sb.Append("\n  ").Append(ShopSystem.SeedPoolReport());
            }
            catch (System.Exception e)
            {
                sb.Append("\n  （植物刷新池读数失败：").Append(e.Message).Append("）");
            }
            string text = sb.ToString();
            Log.Info(Tag + "SHOP " + text);
            return text;
        }

        private static int CountKind(string kind)
        {
            int n = 0;
            for (int i = 0; i < TownUnlocks.Count; i++) if (TownUnlocks.At(i).kind == kind) n++;
            return n;
        }

        /// <summary>
        /// 图鉴读数（§④ 交付用）：三页各几条 / 各几页 / 成就页状态 / 每页首条长什么样。
        /// 一条命令回答"买了之后图鉴里有没有"这个问题 ✓
        /// </summary>
        public string CodexInfo(string ignored)
        {
            var sb = new System.Text.StringBuilder(700);
            sb.Append("图鉴（只列**已解锁**）：植物 ").Append(TownCodex.Plants.Length).Append(" 条 → ")
              .Append(TownCodex.PageCount(TownCodex.Page.Plants)).Append(" 页");
            sb.Append("｜道具 ").Append(TownCodex.Items.Length).Append(" 条 → ")
              .Append(TownCodex.PageCount(TownCodex.Page.Items)).Append(" 页");
            sb.Append("｜插件 ").Append(TownCodex.Plugins.Length).Append(" 条 → ")
              .Append(TownCodex.PageCount(TownCodex.Page.Plugins)).Append(" 页");
            sb.Append("｜成就 0 条 → 1 页（敬请期待）");

            AppendCodexSample(sb, TownCodex.Page.Plants, "植物");
            AppendCodexSample(sb, TownCodex.Page.Items, "道具");
            AppendCodexSample(sb, TownCodex.Page.Plugins, "插件");
            if (!string.IsNullOrEmpty(TownCodex.LastError)) sb.Append("\n降级：").Append(TownCodex.LastError);

            string text = sb.ToString();
            Log.Info(Tag + "CODEX " + text);
            return text;
        }

        private static void AppendCodexSample(System.Text.StringBuilder sb, TownCodex.Page page, string label)
        {
            if (!TownCodex.TryGet(page, 0, 0, out TownCodex.Entry e)) return;
            string d = e.Detail ?? "";
            if (d.Length > 70) d = d.Substring(0, 70) + "…";
            sb.Append("\n  ").Append(label).Append("首条：").Append(e.Name).Append("（").Append(e.Id).Append("）—— ").Append(d);
        }

        private static int CountOnSale(string kind)
        {
            int n = 0;
            for (int i = 0; i < TownUnlocks.Count; i++)
            {
                TownUnlockDef d = TownUnlocks.At(i);
                if (d.kind == kind && d.onSale) n++;
            }
            return n;
        }

        /// <summary>
        /// 自检入口（给桥/无头用）：跑 `SelfCheck.RunAll()`，把原文写进 `Library/` 并返回计数行。
        /// **只读**：不改任何数值、不落盘到工程资产；`SelfCheck.cs` 本身一行未动（禁令）✓
        /// </summary>
        public string RunSelfCheck(string path)
        {
            string file = string.IsNullOrWhiteSpace(path) ? "Library/yowei-v214-selfcheck.txt" : path.Trim();
            try
            {
                TableDatabase.Load();
            }
            catch (System.Exception e)
            {
                Log.Warn(Tag + "自检前载入战斗表失败（继续跑）：" + e.Message);
            }

            System.Collections.Generic.List<CheckResult> results = SelfCheck.RunAll();
            int failed = 0;
            for (int i = 0; i < results.Count; i++) if (!results[i].Passed) failed++;
            string head = $"自检 {(failed == 0 ? "PASS" : "FAIL")}：{results.Count - failed}/{results.Count} 通过，{failed} 失败";
            string body = SelfCheck.Report(results);
            try
            {
                System.IO.File.WriteAllText(file, head + "\n" + body, System.Text.Encoding.UTF8);
                Log.Info(Tag + head + "（原文 → " + file + "）");
            }
            catch (System.Exception e)
            {
                Log.Error(Tag + "自检报告写盘失败：" + e.Message);
            }
            return head;
        }

        /// <summary>
        /// ★ 只读探针：**"模型有没有倒"**（用户原话："模型出现倒下的问题，全部扶正"）★
        ///
        /// 判据：每个 `Renderer` 的 `transform.up` 与世界 +Y 的夹角。Y-up 的模型（本工程全部美术资源）
        /// 被正确摆放时这个角应该是 **0°**；被"躺倒"的物件会接近 90°。
        /// 阈值 **2°**（引擎里的浮点误差远小于它，而真实的"歪"不会只有 1°）。
        ///
        /// 输出：总数 / 倒下的件数 / 每一件的 `名字 · 夹角 · lossyScale · 父物体`（前 <paramref name="limit"/> 条），
        /// 以及按名字前缀分组的小结（判断"是我的积木还是美术那路的装饰"）。`path` 非空时写文件。
        /// **本方法不改任何东西** —— 先量后修 ✓
        /// </summary>
        public string UpAxisProbe(string path)
        {
            var sb = new System.Text.StringBuilder(4096);
            var bad = new System.Collections.Generic.List<string>(32);
            int total = 0, badCount = 0;
            var byPrefix = new System.Collections.Generic.Dictionary<string, int>(8);

            Renderer[] rs = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null) continue;
                total++;
                float ang = Vector3.Angle(r.transform.up, Vector3.up);
                if (ang <= 2f) continue;
                badCount++;
                string n = r.name;
                string key = n.Length >= 4 ? n.Substring(0, 4) : n;
                byPrefix.TryGetValue(key, out int c);
                byPrefix[key] = c + 1;
                if (bad.Count < 40)
                {
                    Vector3 ls = r.transform.lossyScale;
                    bad.Add($"    {n} · up×+Y = {ang:0.0}° · lossyScale ({ls.x:0.00},{ls.y:0.00},{ls.z:0.00})" +
                            $" · 父 {(r.transform.parent != null ? r.transform.parent.name : "(无)")}");
                }
            }

            sb.Append("直立探针：场景渲染物 ").Append(total).Append(" 个｜**倒下 ").Append(badCount)
              .Append(" 个**（判据：transform.up 与世界 +Y 夹角 > 2°）");
            if (badCount > 0)
            {
                sb.Append("\n  按名字前缀分组：");
                foreach (var kv in byPrefix) sb.Append(kv.Key).Append('×').Append(kv.Value).Append("  ");
                sb.Append("\n  逐条：");
                for (int i = 0; i < bad.Count; i++) sb.Append('\n').Append(bad[i]);
                if (badCount > bad.Count) sb.Append("\n    …（还有 ").Append(badCount - bad.Count).Append(" 个未列出）");
            }

            // ── 第二判据：**"躺倒"的另一种形态** —— 模型本身没被旋转（up 还是 +Y），
            //    但它的包围盒又扁又宽（本该是立着的树/围栏躺成了地毯）。
            //    只查 `ENV_*`（= `TownKit.EnvFit` 摆出来的装饰实例），避免把地面薄板/合并草皮算进来 ✓
            int flatCount = 0;
            var flatList = new System.Collections.Generic.List<string>(8);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.name.StartsWith("ENV_", System.StringComparison.Ordinal)) continue;
                Bounds b = r.bounds;
                float wide = Mathf.Max(b.size.x, b.size.z);
                if (wide < 0.9f || b.size.y > wide * 0.30f) continue;
                flatCount++;
                if (flatList.Count < 10)
                    flatList.Add($"    {r.name} · 包围盒 {b.size.x:0.00}×{b.size.y:0.00}×{b.size.z:0.00}" +
                                 $"（y/宽 = {b.size.y / wide:0.00} < 0.30 ⇒ 疑似躺倒）");
            }
            sb.Append("\n  第二判据（ENV_ 装饰的包围盒，扁平 = 疑似躺倒）：").Append(flatCount).Append(" 个");
            for (int i = 0; i < flatList.Count; i++) sb.Append('\n').Append(flatList[i]);
            string text = sb.ToString();
            Log.Info(Tag + "UP " + text);
            if (!string.IsNullOrWhiteSpace(path))
            {
                try { System.IO.File.WriteAllText(path.Trim(), text, System.Text.Encoding.UTF8); }
                catch (System.Exception e) { Log.Warn(Tag + "直立探针写盘失败：" + e.Message); }
            }
            return text;
        }

        /// <summary>
        /// 扶正：把 `up×+Y > 2°` 的渲染物**只保留 Y 轴朝向**（去掉 pitch/roll）。
        /// <paramref name="namePrefix"/> 为空 = 全部；给了前缀 = 只修名字以它开头的那些。
        /// 返回"修了几件"。**修完请再跑一次 <see cref="UpAxisProbe"/>**（改后读数）✓
        /// </summary>
        public string FixFallen(string namePrefix)
        {
            string p = namePrefix != null ? namePrefix.Trim() : "";
            int fixedCount = 0;
            Renderer[] rs = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null) continue;
                if (p.Length > 0 && !r.name.StartsWith(p, System.StringComparison.Ordinal)) continue;
                Transform t = r.transform;
                float ang = Vector3.Angle(t.up, Vector3.up);
                if (ang <= 2f) continue;
                float yaw = t.eulerAngles.y;
                t.rotation = Quaternion.Euler(0f, yaw, 0f);
                fixedCount++;
            }
            Physics.SyncTransforms();
            string line = $"扶正：修了 {fixedCount} 件（前缀「{(p.Length > 0 ? p : "全部")}」）⇒ 再跑 UpAxisProbe 取改后读数";
            Log.Info(Tag + line);
            return line;
        }

        /// <summary>战斗传送门特效挂点的世界坐标（美术那路的黑洞/粒子按名字 `FX_BossPortal` 找它）。</summary>
        public string FxHookInfo(string ignored)
        {
            GameObject fx = GameObject.Find(TownLayout.BossPortalFxName);
            if (fx == null)
            {
                string miss = "没有找到 " + TownLayout.BossPortalFxName + "（挂点丢了？）";
                Log.Warn(Tag + miss);
                return miss;
            }
            Vector3 w = fx.transform.position;
            string line = $"{TownLayout.BossPortalFxName} 世界坐标 = ({w.x:0.00}, {w.y:0.00}, {w.z:0.00})" +
                          $"｜朝向 yaw = {fx.transform.eulerAngles.y:0}°｜父 = {(fx.transform.parent != null ? fx.transform.parent.name : "(无)")}" +
                          $"｜本物体组件 = {fx.GetComponents<Component>().Length} 个（空物体：不渲染/不碰撞）";
            Log.Info(Tag + "FX " + line);
            return line;
        }

        /// <summary>
        /// ★ 只读探针：**田里的作物 ↔ 仓库计数**对不上时，用它把两边一起打出来 ★
        /// 每次返回：`warehouse[8]` ＋ 每块非空地的
        /// `{ 索引 / 状态里的种子 / 视觉缓存里的种子 / 根物体是否激活 / 是否成熟 }`，
        /// 末尾报"不一致格数"。`state` 与 `view` 不同 = 渲染没跟上状态；仓库与田位对不上 = 账本自己不一致。
        /// 命令（可空）：`""` 只读 ｜ `plant,<地块>,<种子>` ｜ `advance,<秒>` ｜ `clear,<地块>`
        /// </summary>
        public string StateProbe(string csv)
        {
            if (_state == null) return "没有状态";
            string cmd = (csv ?? "").Trim();
            string action = "dump";
            if (cmd.Length > 0)
            {
                string[] parts = cmd.Split(',');
                string verb = parts[0].Trim().ToLowerInvariant();
                if (verb == "plant" && parts.Length >= 3)
                {
                    int p = int.Parse(parts[1].Trim()), si = int.Parse(parts[2].Trim());
                    if (si >= 0 && si < _state.Warehouse.Length && _state.Warehouse[si] <= 0)
                        _state.AddSeed(si, 1);                   // 探针自己的准备动作：保证仓库有货
                    bool ok = Plant(p, si);
                    action = $"plant(#{p + 1},{si}) → {(ok ? "成功" : "失败")}";
                }
                else if (verb == "advance" && parts.Length >= 2)
                {
                    double secs = double.Parse(parts[1].Trim());
                    TownGrowth.AdvanceSeconds(secs);
                    action = $"advance({secs:0}s)";
                }
                else if (verb == "clear" && parts.Length >= 2)
                {
                    int p = int.Parse(parts[1].Trim());
                    bool ok = _state.Clear(p);
                    action = $"clear(#{p + 1}) → {(ok ? "成功" : "本来就没作物")}";
                }
            }

            SyncPlots();      // 与 `Update` 同一个视觉同步入口

            var sb = new System.Text.StringBuilder(1200);
            sb.Append("动作=").Append(action).Append("\n  warehouse[8]=[");
            for (int i = 0; i < _state.Warehouse.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_state.Warehouse[i]);
            }
            sb.Append("] 合计=").Append(_state.WarehouseTotal);
            sb.Append(" 已种=").Append(_state.PlantedCount).Append(" 成熟=").Append(_state.MatureCount);

            int desync = 0;
            sb.Append("\n  逐块（只列非空）：");
            for (int i = 0; i < TownState.PlotCount; i++)
            {
                int st = _state.PlotSeed[i];
                PlotView v = _plotViews[i];
                int view = v != null ? v.Seed : -99;
                bool hasRoot = v != null && v.Root != null;
                bool active = hasRoot && v.Root.activeSelf;
                if (st < 0 && !active) continue;
                bool bad = (st != view) || (st >= 0 && !active) || (st < 0 && active);
                if (bad) desync++;
                TownSeedDef d = TownData.SeedAt(st);
                sb.Append("\n    #").Append((i + 1).ToString("00"))
                  .Append(" state=").Append(st < 0 ? "空" : (d != null ? d.name : "?" + st))
                  .Append(" view=").Append(view == -99 ? "无View" : (view < 0 ? "空" : "?" + view))
                  .Append(" 根=").Append(hasRoot ? (active ? "激活" : "隐藏") : "无")
                  .Append(" 成熟=").Append(_state.PlotMature[i] ? "是" : "否")
                  .Append(bad ? "  ← **不一致**" : "  ✓");
            }
            sb.Append("\n  不一致格数=").Append(desync);
            string text = sb.ToString();
            Log.Info(Tag + "PROBE " + text);
            return text;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 只读探针：**「种 → 收」账本**（每个动作之后两边一起打）★
        // ══════════════════════════════════════════════════════════════════

        /// <summary>账本探针的默认剧本（`SeedLedger("")` 跑的就是它，把六个写入口全覆盖一遍）。</summary>
        private const string DefaultLedgerScript =
            "give,0,3;plant,0,0;advance,25;harvest,0;plant,1,0;give,1,1;plant,1,1;clear,0;sell,0,1;dump;roundtrip;diskrt";

        /// <summary>
        /// ★ 只读探针：把「种 → 收」整条链**逐步**打出来，每一步同时打印仓库与田地 ★
        ///
        /// 用法：`unity_drive send TownBootstrap TownBootstrap SeedLedger "脚本"`
        ///   · `""` = 跑 <see cref="DefaultLedgerScript"/>，一条命令出全部读数
        ///   · `"动作;动作;…"` = 自己给序列；动作用逗号带参
        /// 动作：`dump`｜`give,种子,数量`（走 `AddSeed`）｜`drop[,次数]`（走 `TownDrops.OnBossKilled`，与战斗里真击杀同一条链路）
        ///      ｜`plant,地块,种子`（空地上就是种、有作物就是换种）｜`advance,秒`（走 `TownGrowth.AdvanceSeconds`）
        ///      ｜`harvest,地块`（走 `TownState.Harvest`）｜`clear,地块`｜`sell,种子,数量`
        ///      ｜`roundtrip`（`TownSaveIO.Capture` → `Restore` 一次，再逐位比）
        ///      ｜`diskrt`（同上，但中间真的走一遍 `JsonUtility` ＋ 落盘再读回来）
        ///
        /// ★★ 为什么必须"两边一起打"★★
        /// 用户报的是「**耕地上的植物与实际拥有的种子不匹配，没有同步更新**」。
        /// 仓库 `Warehouse[]` 与田地 `PlotSeed[]` 是**两个数组** ⇒ 只要有一个写入口只动其中一边，
        /// 账就在那一步裂开。所以这里每一步都打印两个数组，并算出一个**从设计反推的**不变量：
        ///
        ///     T = Σ仓库 + 已种
        ///
        /// 依据（`TownState` 类注释 ②③，修完之后的契约 —— **一颗种子只能在「仓库」或「田里」其中一处**）：
        ///   ① 种下 ⇒ 仓库 −1、田 +1                  ⇒ T 不变
        ///   ② 成熟 ⇒ **什么都不动**（只翻一个观感标记）⇒ T 不变
        ///   ③ 收获 ⇒ 仓库 +1、田 −1                  ⇒ T 不变
        ///   ④ 掉落 / 给 ⇒ 只加仓库                    ⇒ +N
        ///   ⑤ 卖 ⇒ 只减仓库                           ⇒ −N
        ///   ⑥ 铲除 / 换种 ⇒ 旧的不退还                ⇒ −1
        /// **第一个 ΔT 与动作语义对不上的那一步，就是根因** ✓
        /// （修之前用的是 `Σ仓库 + 已种 − 已成熟`：那一版成熟会凭空返种，所以必须把成熟数减掉才配平 ——
        ///   这本身就是"两个真值源"的症状。现在不再需要那一项了。）
        /// </summary>
        public string SeedLedger(string script)
        {
            if (_state == null) return "没有状态";

            string body = string.IsNullOrWhiteSpace(script) ? DefaultLedgerScript : script.Trim();
            string[] ops = body.Split(';');

            var sb = new System.Text.StringBuilder(4096);
            sb.Append("剧本=").Append(body);
            SyncPlots();
            sb.Append("\n[0] 起点（动作前）").Append("\n     ").Append(LedgerLine());

            int pass = 0, fail = 0;
            string firstBad = null;

            for (int i = 0; i < ops.Length; i++)
            {
                string op = ops[i].Trim();
                if (op.Length == 0) continue;

                SyncPlots();
                int t0 = LedgerT();
                int t1;
                int expect;
                string note = ApplyLedgerOp(op, out expect);
                SyncPlots();
                t1 = LedgerT();

                int delta = t1 - t0;
                bool good = delta == expect;
                if (good) pass++;
                else { fail++; if (firstBad == null) firstBad = op; }

                sb.Append("\n[").Append(i + 1).Append("] ").Append(op)
                  .Append("　T ").Append(t0).Append("→").Append(t1)
                  .Append("（Δ").Append(delta >= 0 ? "+" : "").Append(delta)
                  .Append(" 期望 ").Append(expect >= 0 ? "+" : "").Append(expect).Append("）")
                  .Append(good ? "  ✓" : "  ✗ **账面对不上**")
                  .Append(string.IsNullOrEmpty(note) ? "" : "　｜" + note);
                sb.Append("\n     ").Append(LedgerLine());
            }

            sb.Append("\n结论：").Append(pass).Append(" 步对账通过，").Append(fail).Append(" 步对不上");
            if (firstBad != null) sb.Append("｜第一个裂开的是「").Append(firstBad).Append("」");
            sb.Append("\n  Snapshot() = ").Append(_state.Snapshot());

            string text = sb.ToString();
            Log.Info(Tag + "LEDGER\n" + text);
            return text;
        }

        /// <summary>
        /// 账面总数 `T = Σ仓库 + 已种`（**一颗种子只能在仓库或田里其中一处**）。
        /// **直接从两个数组算**（不读 `WarehouseTotal` / `PlantedCount` 这些派生量）——
        /// 这样派生量自己错了也藏不住，会在 <see cref="LedgerLine"/> 的 `派生量:` 那一段被抓出来 ✓
        /// </summary>
        private int LedgerT()
        {
            int w = 0, p = 0;
            for (int i = 0; i < _state.Warehouse.Length; i++) w += _state.Warehouse[i];
            for (int i = 0; i < TownState.PlotCount; i++)
                if (_state.PlotSeed[i] >= 0) p++;
            return w + p;
        }

        /// <summary>一行读数：仓库 8 格 ＋ 每块非空地的 `{ 索引 / 作物 id / 进度 / 成熟 }` ＋ 派生量核对。</summary>
        private string LedgerLine()
        {
            var sb = new System.Text.StringBuilder(360);
            sb.Append("warehouse[").Append(_state.Warehouse.Length).Append("]=[");
            for (int i = 0; i < _state.Warehouse.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_state.Warehouse[i]);
            }
            sb.Append("] 合计=").Append(_state.WarehouseTotal);

            sb.Append("　田：");
            int p = 0, m = 0;
            bool any = false;
            for (int i = 0; i < TownState.PlotCount; i++)
            {
                int si = _state.PlotSeed[i];
                if (si < 0) continue;
                any = true;
                p++;
                if (_state.PlotMature[i]) m++;
                TownSeedDef d = TownData.SeedAt(si);
                PlotView v = _plotViews[i];
                sb.Append("\n       #").Append((i + 1).ToString("00"))
                  .Append(" 作物=").Append(d != null ? d.id : ("?" + si))
                  .Append("(").Append(d != null ? d.name : "?")
                  .Append(" 索引").Append(si).Append(")")
                  .Append(" 进度=").Append(d != null ? d.Progress(_state.PlotElapsed[i]).ToString("0.00") : "-")
                  .Append(" 成熟=").Append(_state.PlotMature[i] ? "是" : "否")
                  .Append(" 视觉缓存=").Append(v != null ? (v.Seed < 0 ? "空" : v.Seed.ToString()) : "无View")
                  .Append(" 根=").Append(v != null && v.Root != null ? (v.Root.activeSelf ? "激活" : "隐藏") : "无")
                  .Append((v != null && v.Seed != si) ? "  ← **视觉与状态不一致**" : "");
            }
            if (!any) sb.Append("（空地）");

            sb.Append("\n       派生量：已种=").Append(_state.PlantedCount)
              .Append("（数组算 ").Append(p).Append(_state.PlantedCount == p ? " ✓" : " ✗）")
              .Append(" 成熟=").Append(_state.MatureCount)
              .Append("（数组算 ").Append(m).Append(_state.MatureCount == m ? " ✓" : " ✗）");
            return sb.ToString();
        }

        private int WarehouseOf(int seedIndex)
            => seedIndex >= 0 && seedIndex < _state.Warehouse.Length ? _state.Warehouse[seedIndex] : 0;

        private static string SeedLabel(int seedIndex)
        {
            TownSeedDef d = TownData.SeedAt(seedIndex);
            return d != null ? d.id + "(" + d.name + ")" : "#" + seedIndex;
        }

        private static bool LedgerInt(string[] parts, int at, out int value)
        {
            value = 0;
            return parts.Length > at && int.TryParse(parts[at].Trim(), out value);
        }

        /// <summary>
        /// 执行一个账本动作，并把**设计上应有的 ΔT** 写进 <paramref name="expect"/>。
        /// 返回一行"这一步发生了什么"的说明（失败也如实写，不吞）。
        /// </summary>
        private string ApplyLedgerOp(string op, out int expect)
        {
            expect = 0;
            string[] parts = op.Split(',');
            string verb = parts[0].Trim().ToLowerInvariant();

            switch (verb)
            {
                case "dump":
                    return "只读";

                case "give":
                {
                    if (!LedgerInt(parts, 1, out int si) || !LedgerInt(parts, 2, out int n))
                        return "参数要 \"give,种子,数量\"";
                    int before = WarehouseOf(si);
                    int added = _state.AddSeed(si, n);
                    expect = added;                         // 只有真进仓库的才配上账（上限 99 会截断）
                    return $"给 {SeedLabel(si)} ×{n} ⇒ 实际入仓 {added}（{before}→{WarehouseOf(si)}）";
                }

                case "drop":
                {
                    int n = 1;
                    if (parts.Length >= 2) LedgerInt(parts, 1, out n);
                    var sb = new System.Text.StringBuilder(160);
                    for (int k = 0; k < n; k++)
                    {
                        int idx = TownDrops.OnBossKilled("boss", Vector2.zero);
                        expect += TownDrops.LastAdded;
                        sb.Append("掉#").Append(k + 1).Append('=').Append(SeedLabel(idx)).Append(' ');
                    }
                    return "BOSS 掉落 " + n + " 次：" + sb;
                }

                case "plant":
                {
                    if (!LedgerInt(parts, 1, out int plot) || !LedgerInt(parts, 2, out int si))
                        return "参数要 \"plant,地块,种子\"";
                    bool occupied = _state.PlotSeed[plot] >= 0;
                    int before = WarehouseOf(si);
                    bool ok = _state.Plant(plot, si);
                    // 空地种：仓库 −1、田 +1 ⇒ ΔT 0；换种：旧作物被覆盖且不退还 ⇒ −1
                    expect = !ok ? 0 : (occupied ? -1 : 0);
                    return $"{(occupied ? "换种" : "种下")} {SeedLabel(si)} → #{plot + 1}" +
                           $"（{(ok ? "成功" : "失败")}，仓库 {before}→{WarehouseOf(si)}）";
                }

                case "advance":
                {
                    if (!double.TryParse(parts.Length > 1 ? parts[1].Trim() : "", out double secs))
                        return "参数要 \"advance,秒\"";
                    TownGrowth.AdvanceSeconds(secs);      // 成熟只翻标记、不碰仓库 ⇒ T 不变
                    expect = 0;
                    int m = 0;
                    for (int k = 0; k < TownState.PlotCount; k++) if (_state.PlotSeed[k] >= 0 && _state.PlotMature[k]) m++;
                    return $"推进 {secs:0}s（金币 {_state.Coins} · 地里现在成熟 {m} 块，等收获）";
                }

                case "harvest":
                {
                    if (!LedgerInt(parts, 1, out int plot)) return "参数要 \"harvest,地块\"";
                    int si = _state.PlotSeed[plot];
                    bool wasMature = si >= 0 && _state.PlotMature[plot];
                    int before = WarehouseOf(si);
                    bool ok = _state.Harvest(plot);
                    // 收获：仓库 +1、田 −1 ⇒ T 不变；收不了就什么都没发生
                    expect = 0;
                    return ok
                        ? $"收获 #{plot + 1} {SeedLabel(si)} ⇒ 仓库 {before}→{WarehouseOf(si)}（作物离开田位）"
                        : $"#{plot + 1} 收不了（{(si < 0 ? "空地" : (wasMature ? "?" : "还没成熟"))}）";
                }

                case "clear":
                {
                    if (!LedgerInt(parts, 1, out int plot)) return "参数要 \"clear,地块\"";
                    bool occupied = _state.PlotSeed[plot] >= 0;
                    bool wasMature = occupied && _state.PlotMature[plot];
                    bool ok = _state.Clear(plot);
                    // 铲除不退还 ⇒ 田 −1 ⇒ T −1（成熟与否都一样：成熟本来就不返种）
                    expect = ok ? -1 : 0;
                    return $"铲除 #{plot + 1}（{(ok ? "成功" : "本来就没作物")}{(occupied ? (wasMature ? "，原来是已成熟" : "，原来还没成熟") : "")}）";
                }

                case "sell":
                {
                    if (!LedgerInt(parts, 1, out int si) || !LedgerInt(parts, 2, out int n))
                        return "参数要 \"sell,种子,数量\"";
                    int before = WarehouseOf(si);
                    long gain = _state.SellSeed(si, n);
                    int removed = before - WarehouseOf(si);
                    expect = -removed;
                    return $"卖 {SeedLabel(si)} ×{removed} ⇒ +{gain} 金（仓库 {before}→{WarehouseOf(si)}）";
                }

                case "roundtrip":
                    return LedgerRoundTrip();

                case "diskrt":
                    return LedgerDiskRoundTrip();

                default:
                    return "认不出的动作（跳过）";
            }
        }

        /// <summary>
        /// 存一次、读一次，再**逐位**比：仓库 / 每块地的作物 / 每块地的进度 / 每块地的成熟 / 金币。
        /// 走的是真正会落盘的那两个方法（`TownSaveIO.Capture` → `TownSaveIO.Restore`），
        /// 不是"另写一份对照"。返回一行结论（不同处逐条列出）。
        /// </summary>
        private string LedgerRoundTrip()
        {
            int[] w0 = (int[])_state.Warehouse.Clone();
            int[] p0 = (int[])_state.PlotSeed.Clone();
            double[] e0 = (double[])_state.PlotElapsed.Clone();
            bool[] m0 = (bool[])_state.PlotMature.Clone();
            long coins0 = _state.Coins;
            string snap0 = _state.Snapshot();

            TownSaveData data = TownSaveIO.Capture(_state, 0.0, "账本探针");
            if (!TownSaveIO.Restore(data, out string error))
                return "**存读失败**：" + error;

            var diff = new System.Text.StringBuilder(200);
            int n = 0;
            for (int i = 0; i < w0.Length; i++)
                if (i >= _state.Warehouse.Length || w0[i] != _state.Warehouse[i]) { n++; diff.Append($"[仓库{i}:{w0[i]}→{WarehouseOf(i)}]"); }
            for (int i = 0; i < p0.Length; i++)
                if (p0[i] != _state.PlotSeed[i]) { n++; diff.Append($"[田{i}:{p0[i]}→{_state.PlotSeed[i]}]"); }
            for (int i = 0; i < e0.Length; i++)
                if (e0[i] != _state.PlotElapsed[i]) { n++; diff.Append($"[进度{i}:{e0[i]:0.###}→{_state.PlotElapsed[i]:0.###}]"); }
            for (int i = 0; i < m0.Length; i++)
                if (m0[i] != _state.PlotMature[i]) { n++; diff.Append($"[成熟{i}:{m0[i]}→{_state.PlotMature[i]}]"); }
            if (coins0 != _state.Coins) { n++; diff.Append($"[金币:{coins0}→{_state.Coins}]"); }

            if (n == 0)
                return "存→读 **逐位相同** ✓（Snapshot 也没变：" + (_state.Snapshot() == snap0 ? "一致" : "**不一致**") + "）";
            return $"存→读 **{n} 处不同**：{diff}";
        }

        /// <summary>
        /// 存一次、读一次（**经过 `JsonUtility` 与磁盘**），再逐位比。
        /// 与 <see cref="LedgerRoundTrip"/> 的区别：那个只比"内存里 Capture→Restore"，
        /// 这个把 `JsonUtility.ToJson` → 落盘 → `File.ReadAllText` → `FromJson` 这段也走一遍
        /// （`SaveStore.Write` / `Read` 用的就是这两句）⇒ 能抓到"序列化把某个数组吃掉了"这类错 ✓
        /// 写的是 `persistentDataPath` 下的探针专用文件名，**不碰 6 个存档位**。
        /// </summary>
        private string LedgerDiskRoundTrip()
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "yowei-ledger-roundtrip.json");
            int[] w0 = (int[])_state.Warehouse.Clone();
            int[] p0 = (int[])_state.PlotSeed.Clone();
            double[] e0 = (double[])_state.PlotElapsed.Clone();
            bool[] m0 = (bool[])_state.PlotMature.Clone();
            long coins0 = _state.Coins;

            TownSaveData data = TownSaveIO.Capture(_state, 0.0, "账本探针");
            TownSaveData back;
            try
            {
                System.IO.File.WriteAllText(path, JsonUtility.ToJson(data, true));
                back = JsonUtility.FromJson<TownSaveData>(System.IO.File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                return "**写盘/读盘失败**：" + e.Message;
            }
            if (back == null) return "**反序列化得到 null**";

            if (!TownSaveIO.Restore(back, out string error)) return "**读档失败**：" + error;

            var diff = new System.Text.StringBuilder(240);
            int n = 0;
            for (int i = 0; i < w0.Length; i++)
                if (i >= _state.Warehouse.Length || w0[i] != _state.Warehouse[i]) { n++; diff.Append($"[仓库{i}:{w0[i]}→{WarehouseOf(i)}]"); }
            for (int i = 0; i < p0.Length; i++)
                if (p0[i] != _state.PlotSeed[i]) { n++; diff.Append($"[田{i}:{p0[i]}→{_state.PlotSeed[i]}]"); }
            for (int i = 0; i < e0.Length; i++)
                if (e0[i] != _state.PlotElapsed[i]) { n++; diff.Append($"[进度{i}:{e0[i]:0.###}→{_state.PlotElapsed[i]:0.###}]"); }
            for (int i = 0; i < m0.Length; i++)
                if (m0[i] != _state.PlotMature[i]) { n++; diff.Append($"[成熟{i}:{m0[i]}→{_state.PlotMature[i]}]"); }
            if (coins0 != _state.Coins) { n++; diff.Append($"[金币:{coins0}→{_state.Coins}]"); }

            string head = $"经 JsonUtility+磁盘（{path}）";
            return n == 0 ? head + " 存→读 **逐位相同** ✓" : head + $" 存→读 **{n} 处不同**：{diff}";
        }

        /// <summary>
        /// ★ 只读探针：**田格上除了"我自己种的那株"以外还站着什么** ★
        ///
        /// 为什么要有这一条：用户说的是「**耕地上的植物**与实际拥有的种子不匹配」。
        /// 除了"状态里的田位"这条线，还有一条完全独立、**不经过 `TownState` 的**写入口 ——
        /// 美术层（`TownArtDresser`）往场景里撒的草/花/石/树。它要是撒进了 6×6 田格，
        /// 田里就会长出一株"你没种过、仓库里也没有对应种子"的植物 ⇒ 正是这条报障的形态 ✗
        ///
        /// 判据：拿 36 块地各自的世界矩形，遍历 `TOWN` 下所有 `Renderer`，
        /// 凡**不属于 `TOWN_Plants`**、名字也不以 `PLOT_` 开头的，只要它的 XZ 中心落进田格就算"入侵"。
        /// 返回入侵件数与逐条清单（按名字前缀归类）。**本方法不改任何东西** ✓
        /// </summary>
        public string FieldSquatters(string ignored)
        {
            if (_root == null) return "城还没建";

            const float HalfW = TownLayout.PlotWidth * 0.5f;
            const float HalfD = TownLayout.PlotDepth * 0.5f;

            Renderer[] all = _root.GetComponentsInChildren<Renderer>(true);
            var hits = new System.Collections.Generic.List<string>(16);
            var byPrefix = new System.Collections.Generic.Dictionary<string, int>(8);
            int checkedCount = 0, skipped = 0;

            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null) continue;

                // 我自己种的那株 ＋ 田埂本身：不算入侵
                if (r.transform.IsChildOf(_plantRoot)) { skipped++; continue; }
                if (r.name.StartsWith("PLOT_", System.StringComparison.Ordinal)) { skipped++; continue; }
                checkedCount++;

                Vector3 c = r.bounds.center;
                for (int p = 0; p < TownState.PlotCount; p++)
                {
                    Vector2 pc = PlotCenter(p);
                    if (Mathf.Abs(c.x - pc.x) > HalfW || Mathf.Abs(c.z - pc.y) > HalfD) continue;

                    string n = r.name;
                    string key = n.Length > 6 ? n.Substring(0, 6) : n;
                    byPrefix.TryGetValue(key, out int cnt);
                    byPrefix[key] = cnt + 1;
                    if (hits.Count < 30)
                        hits.Add($"    {n} @ ({c.x:0.00},{c.z:0.00}) 落在第 {p + 1} 号田格" +
                                 $"（该格状态=" + (_state.PlotSeed[p] < 0 ? "空" : "作物 " + TownData.SeedAt(_state.PlotSeed[p])?.id) + "）");
                    break;
                }
            }

            int total = 0;
            foreach (var kv in byPrefix) total += kv.Value;

            var sb = new System.Text.StringBuilder(900);
            sb.Append("田格入侵探针：遍历渲染物 ").Append(all.Length)
              .Append(" 个（跳过我自己种的/田埂 ").Append(skipped).Append(" 个，实查 ").Append(checkedCount).Append(" 个）")
              .Append("｜**落在 36 个田格里的外来物 ").Append(total).Append(" 个**");
            if (total > 0)
            {
                sb.Append("\n  按名字前 6 字归类：");
                foreach (var kv in byPrefix) sb.Append(kv.Key).Append('×').Append(kv.Value).Append("  ");
                for (int i = 0; i < hits.Count; i++) sb.Append('\n').Append(hits[i]);
                if (total > hits.Count) sb.Append("\n    …（还有 ").Append(total - hits.Count).Append(" 个未列出）");
            }
            else
            {
                sb.Append("\n  结论：田格里除了 `PLANT_*`（我按状态种下的）与 `PLOT_*`（田埂）之外**没有别的东西** ✓");
            }
            string text = sb.ToString();
            Log.Info(Tag + "SQUAT " + text);
            return text;
        }

        /// <summary>一句话状态（`unity_drive send TownBootstrap TownBootstrap TownStatus`）。</summary>
        public string TownStatus()
        {
            if (_dataError != null) return "DATA_ERROR " + _dataError;
            string s = _state.Snapshot()
                       + " 玩家=(" + PlayerMapPosition.x.ToString("0.0") + "," + PlayerMapPosition.y.ToString("0.0") + ")"
                       + " 半径=" + PlayerMapPosition.magnitude.ToString("0.00")
                       + " 焦点=" + _actKind + (_actPlot >= 0 ? "#" + (_actPlot + 1) : "")
                       + " 面板=" + _panel
                       + " 材质池=" + ToonLook.MaterialPoolCount
                       + " 形状网格=" + TownKit.ShapeCacheCount
                       + " 作物=" + CountPlants()
                       + " 解锁=" + TownUnlocks.OwnedCount + "/" + TownUnlocks.OnSaleCount
                       + " 商店=" + TownHud.LastSellLine
                       + " 被挡帧=" + (_avatar != null ? _avatar.BlockedFrames : 0)
                       + " 最后一撞=" + (_avatar != null ? _avatar.LastHit : "-");
            Log.Info(Tag + "STATUS " + s);
            return s;
        }

        private int CountPlants()
        {
            int n = 0;
            for (int i = 0; i < _plotViews.Length; i++)
                if (_plotViews[i] != null && _plotViews[i].Root != null) n++;
            return n;
        }

        /// <summary>瞬移：`"x,z"`（例：`"0,-9.4"` 到出生点）。</summary>
        /// <summary>
        /// ★ v252：**切界面语言**（`"zh"` / `"en"` / `"ja"`）。给自动化与验收用。
        ///
        /// 为什么需要一个方法而不是只靠设置面板里的那三颗按钮：
        /// 语言切换的验收要求"同一个界面在三种语言下各拍一张图"，而按钮点不了
        /// （IMGUI 不吃 EventSystem 的事件）⇒ 没有这个口子就只能靠人手截图，
        /// 而"人手截图"没法进回归 ✓
        /// 它做的**只有一件事**：`L10n.Set(lang)` —— 写 `PlayerPrefs`、清缓存、
        /// `FontGeneration++`（各 HUD 下一帧重建字体）。没有任何旁路，与按钮同一条路 ✓
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
            Log.Info(Tag + "语言 → " + L10n.Code(L10n.Current) + "（" + L10n.Native(L10n.Current) + "）");
            return "语言 = " + L10n.Code(L10n.Current) + "｜覆盖 " + L10n.Coverage(L10n.Current) + "/" + L10n.KeyCount();
        }

        public void Teleport(string csv)
        {            if (_avatar == null) return;
            string[] parts = (csv ?? "").Split(',');
            if (parts.Length < 2) { Log.Warn(Tag + "Teleport 需要 \"x,z\""); return; }
            if (!float.TryParse(parts[0].Trim(), out float x) || !float.TryParse(parts[1].Trim(), out float z))
            {
                Log.Warn(Tag + "Teleport 参数不是数字：" + csv);
                return;
            }
            _avatar.Teleport(new Vector3(x, TownAvatar.GroundY, z));
            UpdateActTarget();
            Log.Info(Tag + $"Teleport → ({x:0.0},{z:0.0}) 焦点={_actKind}{(_actPlot >= 0 ? "#" + (_actPlot + 1) : "")}");
        }

        /// <summary>按方位角走一段：`"deg,seconds"`（例：`"90,3"` = 朝正北走 3 秒）。**走的是真移动路径**。</summary>
        public void Walk(string csv)
        {
            if (_avatar == null) return;
            string[] parts = (csv ?? "").Split(',');
            if (parts.Length < 2) { Log.Warn(Tag + "Walk 需要 \"deg,seconds\""); return; }
            if (!float.TryParse(parts[0].Trim(), out float deg) || !float.TryParse(parts[1].Trim(), out float secs))
            {
                Log.Warn(Tag + "Walk 参数不是数字：" + csv);
                return;
            }
            _walkDir = TownLayout.Polar(deg, 1f).normalized;
            _walkLeft = Mathf.Max(0.05f, secs);
            _avatar.ResetBlockedCounter();
            Log.Info(Tag + $"Walk 开始：方位角 {deg:0}° × {secs:0.00} 秒，起点 " +
                           $"({_avatar.Position.x:0.00},{_avatar.Position.z:0.00})");
        }

        /// <summary>
        /// 站在当前位置，朝每个地标的锚点各扫一次（与移动同一条 CapsuleCast），
        /// 返回"撞到谁 / 什么形状 / 停在哪个坐标"。**这是 §② 的读数来源**。
        /// </summary>
        public string CastProbe(string ignored)
        {
            if (_avatar == null) return "没有玩家";
            Vector3 from = _avatar.Position;
            var sb = new System.Text.StringBuilder(512);
            sb.Append("起点 (").Append(from.x.ToString("0.00")).Append(',').Append(from.z.ToString("0.00")).Append(")：");
            AppendProbe(sb, "传送门", from, new Vector3(TownLayout.PortalPos.x, 0f, TownLayout.PortalPos.y));
            AppendProbe(sb, "服装店", from, new Vector3(TownLayout.ClothPos.x, 0f, TownLayout.ClothPos.y));
            AppendProbe(sb, "商店", from, new Vector3(TownLayout.ShopPos.x, 0f, TownLayout.ShopPos.y));
            AppendProbe(sb, "仓库", from, new Vector3(TownLayout.WarePos.x, 0f, TownLayout.WarePos.y));
            AppendProbe(sb, "集市帐篷", from, new Vector3(TownLayout.TentPos.x, 0f, TownLayout.TentPos.y));
            string outText = sb.ToString();
            Log.Info(Tag + "CAST " + outText);
            return outText;
        }

        private static void AppendProbe(System.Text.StringBuilder sb, string label, Vector3 from, Vector3 target)
        {
            Vector3 dir = new Vector3(target.x - from.x, 0f, target.z - from.z);
            float dist = dir.magnitude;
            if (dist < 1e-3f) { sb.Append(" ｜").Append(label).Append("=同点"); return; }
            sb.Append(" ｜").Append(label).Append(' ').Append(TownAvatar.CastProbe(from, dir.normalized, dist));
        }

        /// <summary>碰撞体清单（每栋楼的每个零件一行，证明"不是一个方块糊过去"）。</summary>
        public string ColliderInfo(string filter)
        {
            if (_root == null) return "城还没建";
            var all = _root.GetComponentsInChildren<Collider>(true);
            var sb = new System.Text.StringBuilder(2048);
            sb.Append(DescribeColliders());
            string f = (filter ?? "").Trim();
            int shown = 0;
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (f.Length > 0 && !n.Contains(f)) continue;
                Vector3 c = all[i].bounds.center;
                sb.Append("\n  ").Append(n).Append(" · ").Append(TownAvatar.ShapeOf(all[i]))
                  .Append(" @ (").Append(c.x.ToString("0.0")).Append(',').Append(c.z.ToString("0.0"))
                  .Append(") 高 ").Append(all[i].bounds.max.y.ToString("0.00"));
                shown++;
            }
            sb.Append("\n  列出 ").Append(shown).Append(" 个");
            string text = sb.ToString();
            Log.Info(Tag + "COLLIDERS " + text);
            return text;
        }

        /// <summary>种地：`"地块序号(0~35),种子序号(0~7)"`。</summary>
        public void PlantAt(string csv)
        {
            string[] parts = (csv ?? "").Split(',');
            if (parts.Length < 2) { Log.Warn(Tag + "PlantAt 需要 \"plot,seed\""); return; }
            if (!int.TryParse(parts[0].Trim(), out int plot) || !int.TryParse(parts[1].Trim(), out int seed))
            {
                Log.Warn(Tag + "PlantAt 参数不是整数：" + csv);
                return;
            }
            Plant(plot, seed);
            TownStatus();
        }

        /// <summary>
        /// 买一项**解锁**：`"0"`~`"61"`（= 解锁表里的下标）。走与面板按钮同一条 `TownUnlocks.TryUnlock`。
        /// </summary>
        public void Unlock(string index)
        {
            if (!int.TryParse(index, out int i))
            {
                Log.Warn(Tag + "Unlock 需要一个整数下标");
                return;
            }
            TownUnlocks.TryUnlock(i);
            TownStatus();
        }

        /// <summary>
        /// 卖种子换金币：`"种子下标,数量"`（例：`"0,3"` = 卖 3 颗赤玉萝卜）。走 `TownState.SellSeed`。
        /// </summary>
        public string Sell(string csv)
        {
            string[] parts = (csv ?? "").Split(',');
            if (parts.Length < 2) { Log.Warn(Tag + "Sell 需要 \"seed,count\""); return "参数不对"; }
            if (!int.TryParse(parts[0].Trim(), out int seed) || !int.TryParse(parts[1].Trim(), out int count))
            {
                Log.Warn(Tag + "Sell 参数不是整数：" + csv);
                return "参数不对";
            }
            int before = seed >= 0 && seed < _state.Warehouse.Length ? _state.Warehouse[seed] : 0;
            long gain = _state.SellSeed(seed, count);
            string line = $"卖 {TownData.SeedAt(seed)?.name} ×{(before - (seed >= 0 && seed < _state.Warehouse.Length ? _state.Warehouse[seed] : 0))}" +
                          $" ⇒ +{gain} 金（仓库剩 {(seed >= 0 && seed < _state.Warehouse.Length ? _state.Warehouse[seed] : 0)} 颗 · 金币 {_state.Coins}）";
            TownHud.LastSellLine = line;
            Log.Info(Tag + "SELL " + line);
            return line;
        }

        /// <summary>BOSS 掉落自检：`TownDrops.OnBossKilled("boss", 0)` 走一遍，前后仓库快照一起报。</summary>
        public string DropOne(string enemyId)
        {
            string before = _state.Snapshot();
            int idx = TownDrops.OnBossKilled(string.IsNullOrWhiteSpace(enemyId) ? "boss" : enemyId.Trim(),
                                             new Vector2(_avatar != null ? _avatar.Position.x : 0f,
                                                         _avatar != null ? _avatar.Position.z : 0f));
            string after = _state.Snapshot();
            string line = $"掉落前：{before}\n掉落中：{TownDrops.LastLine}\n掉落种子下标：{idx}｜入库 {TownDrops.LastAdded} 颗\n掉落后：{after}";
            Log.Info(Tag + "DROP\n" + line);
            return line;
        }

        /// <summary>切视图：`"top"` = 纯俯视（HUD 自动隐藏，给俯视图截图）、`"follow"` = 回到跟随视角。</summary>
        public void SetView(string mode)
        {
            _topView = string.Equals(mode, "top", System.StringComparison.OrdinalIgnoreCase);
            UpdateCamera();
            Log.Info(Tag + "视图 = " + (_topView ? "俯视" : "跟随") +
                          $"（正交半高 {(_topView ? TopOrthoSize : FollowOrthoSize):0.0} · " +
                          $"机位距离 {(_topView ? 40f : FollowDistance):0.0}）");
        }

        /// <summary>相机的三个读数（§⑦ 的"改前/改后"就是它）。</summary>
        public string CameraInfo(string ignored)
        {
            if (_cam == null) return "没有相机";
            return $"视图={(_topView ? "俯视" : "跟随")}｜正交半高={_cam.orthographicSize:0.0}" +
                   $"｜可见地面={(16f / 9f * _cam.orthographicSize * 2f):0.0}米宽 × {(_cam.orthographicSize * 2f):0.0}米高" +
                   $"｜机位距离={(_topView ? 40f : FollowDistance):0.0}｜俯角={(_topView ? 90f : FollowPitchDeg):0}°" +
                   $"｜相机位置=({_cam.transform.position.x:0.0},{_cam.transform.position.y:0.0},{_cam.transform.position.z:0.0})";
        }

        /// <summary>截一张 PNG（含 OnGUI 界面）。相对路径按工程根解析。</summary>
        public void Shot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Log.Warn(Tag + "Shot 需要路径");
                return;
            }
            ScreenCapture.CaptureScreenshot(path);
            Log.Info(Tag + "截图已排队 → " + path);
        }

        /// <summary>
        /// 战斗侧推成熟时间的手动替代：`AdvanceSeconds("120")`。
        /// **真正的战斗接线是 `TownGrowth.AdvanceSeconds`**，本入口只是让"接口有没有生效"可被外部点一次。
        /// </summary>
        public void AdvanceSeconds(string seconds)
        {
            if (!double.TryParse(seconds, out double s)) { Log.Warn(Tag + "AdvanceSeconds 参数不是数字"); return; }
            TownGrowth.AdvanceSeconds(s);
            TownStatus();
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ 只读探针：**"模型倒下"** —— 判据 = 渲染器**世界 AABB 的最长轴** ★
        //
        // 用户口径（逐字）："判据用世界 `AABB` 最长轴（不是 `transform.up`）"。
        //   为什么不能量 `up`：`Art/Environment/*.fbx` 是 Z-up 资产，根节点自带 `Rx(270)`，
        //   那是**正确**的修正；它把 `transform.up` 转到地平线上 ⇒ 量 `up` 会把
        //   **本来就对**的件全报成"倒"（实测假阳性 222 个），而错的是**网格的上轴**，不是 Transform ✗
        //   AABB 是几何事实，骗不了人 ✓
        //
        // 判"躺"：**细长件**（最长边 / 最短边 > 1.30）且最长边不在 Y 上。
        //   宽扁件（云、路、门匾）与立方件**没有站/躺之分** ⇒ 逐条报出但不计入"躺" ✓
        //
        // 另外两条（用户点名要的）：
        //   ② 每棵树/灌木的**当前姿态** vs 应有的 `Euler(0, yaw, 0) * assetFix` 的夹角
        //      —— 夹角 > 30° = 根节点自带的修正**被谁覆盖了**（不是"作者就想要这样"）
        //   ⑤ `FixUpright` 的战果：多少个 `ENV_*` **已经**是正确姿态（它跑过 ⇒ 非摇摆件都该是 ✓）
        //
        // 调用：`unity_drive send TownBootstrap TownBootstrap UprightProbe ""`
        //       （给路径时另存一份 UTF-8 文本；**本方法不改任何东西**）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>本来就该"长轴水平"的 id（用户点名豁免：木箱/长椅/路牌/柴堆/池塘…）。</summary>
        private static readonly string[] UpExempt =
        {
            "log", "crate_stack", "clay_jars", "laundry_line", "wheelbarrow", "pond",
            "haystack", "wall_ruin", "farm_plot", "fence", "garden_gate",
        };

        /// <summary>只读探针：按**世界 AABB 最长轴**逐类报"躺"的件数（判据见本节抬头）。</summary>
        public string UprightProbe(string path)
        {
            GameObject town = GameObject.Find("TOWN");
            if (town == null) return "[UP] 没有 TOWN（会话没启动？）";

            var inst = new System.Collections.Generic.List<Transform>(256);
            CollectInstances(town.transform, inst);

            // cats 每类：[0]=共 [1]=细长 [2]=躺(细长且长轴水平) [3]=姿态被覆盖 [4]=豁免/宽扁
            var cats = new System.Collections.Generic.SortedDictionary<string, int[]>(System.StringComparer.Ordinal);
            var lines = new System.Collections.Generic.SortedDictionary<string, System.Text.StringBuilder>(System.StringComparer.Ordinal);
            var shown = new System.Collections.Generic.SortedDictionary<string, int>(System.StringComparer.Ordinal);
            int envChecked = 0, envOk = 0, envOverridden = 0, envWorst = 0;

            for (int i = 0; i < inst.Count; i++)
            {
                Transform t = inst[i];
                if (t == null) continue;
                string name = t.name;
                string cat = UpCategory(name);
                bool expectUp = cat == "TREE" || cat == "BUSH" || cat == "ENV_其它" || cat == "DEC_";
                if (cat == "ENV_其它" && UpIsExempt(name)) expectUp = false;

                Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                for (int k = 1; k < rs.Length; k++) b.Encapsulate(rs[k].bounds);
                Vector3 s = b.size;
                float maxS = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                float minS = Mathf.Max(1e-4f, Mathf.Min(s.x, Mathf.Min(s.y, s.z)));
                bool slender = maxS / minS > 1.30f;
                char axis = s.x >= s.y && s.x >= s.z ? 'X' : (s.z >= s.y ? 'Z' : 'Y');
                bool lying = slender && axis != 'Y';

                if (!cats.TryGetValue(cat, out int[] acc)) { acc = new int[5]; cats[cat] = acc; }
                acc[0]++;
                if (slender) acc[1]++;
                if (lying && expectUp) acc[2]++;
                if (!slender || !expectUp) acc[4]++;

                // ② 姿态核对：`ENV_*` / `DEC_*` 的名字里带资源 id ⇒ 与"应有的 `Euler(0,yaw,0)*assetFix`"比夹角。
                //    < 0 = 拿不到资产（不参与）｜< 2° = 正确｜> 30° = **根节点自带的修正被覆盖**（= 真躺，与造型无关）。
                float ang = PostureOffsetDeg(t);
                bool overridden = ang > 30f;
                if (ang >= 0f)
                {
                    envChecked++;
                    if (ang < 2f) envOk++;
                    else if (overridden) envOverridden++;
                    if ((int)ang > envWorst) envWorst = (int)ang;
                }
                if (overridden) acc[3]++;

                if ((lying && expectUp) || overridden)
                {
                    if (!shown.TryGetValue(cat, out int cnt)) cnt = 0;
                    if (cnt < 6)
                    {
                        shown[cat] = cnt + 1;
                        if (!lines.TryGetValue(cat, out var one))
                        {
                            one = new System.Text.StringBuilder();
                            lines[cat] = one;
                        }
                        one.Append("   · ").Append(name)
                           .Append(" ｜size=").Append(s.x.ToString("0.00")).Append('×')
                           .Append(s.y.ToString("0.00")).Append('×').Append(s.z.ToString("0.00"))
                           .Append(" ｜长轴=").Append(axis);
                        if (expectUp) one.Append(" ｜期望=竖直"); else one.Append(" ｜期望=宽扁/豁免");
                        if (ang >= 0f) one.Append(" ｜姿态偏 ").Append(ang.ToString("0.0")).Append('°');
                        one.Append('\n');
                    }
                }
            }

            var sb = new System.Text.StringBuilder(4096);
            sb.Append("[UP] 判据 = 渲染器**世界 AABB 最长轴**｜细长件（最长/最短 > 1.30）且最长轴水平 ⇒ 判「躺」\n");
            sb.Append("     **不量 `transform.up`**：Z-up 资产的根节点 `Rx(270)` 是对的，量 up 会把对的件全报成倒 ✗\n");
            sb.Append("     「姿态偏」= 实例把**网格上轴**转到世界 +Y 的夹角（yaw 自由，且避开 ±90° 万向锁）\n");
            sb.Append("       > 30° ⇒ 根节点自带的 `Rx(270)/Rx(180)` 修正**被覆盖**（= 真躺）；\n");
            sb.Append("       「长轴水平」但「姿态偏」= 0 的（岩石/木箱/围栏/云…）= **造型本来就扁**，不是躺倒\n");
            sb.Append("     会随风摆的树/灌木（`TownLife` 登记）= ").Append(TownLife.SwayCount)
              .Append(" 棵｜摇摆相位读数 倾角 ").Append(TownLife.SwayAngleLast.ToString("0.00")).Append("°\n");
            sb.Append("     `FixUpright` 战果：核 ").Append(envChecked).Append(" 个 ENV_*/DEC_*｜")
              .Append("姿态正确 ").Append(envOk).Append(" 个｜")
              .Append("**被覆盖 ").Append(envOverridden).Append(" 个**（最大偏差 ").Append(envWorst).Append("°）\n");

            int totAll = 0, totLying = 0, totOver = 0;
            foreach (var kv in cats)
            {
                int[] a = kv.Value;
                totAll += a[0];
                totLying += a[2];
                totOver += a[3];
                sb.Append("   ").Append(kv.Key.PadRight(10))
                  .Append("共 ").Append(a[0].ToString().PadLeft(4))
                  .Append("｜细长 ").Append(a[1].ToString().PadLeft(4))
                  .Append("｜**躺 ").Append(a[2].ToString().PadLeft(4)).Append("**")
                  .Append("｜姿态偏 ").Append(a[3].ToString().PadLeft(4))
                  .Append("｜豁免/宽扁 ").Append(a[4]).Append('\n');
            }
            sb.Append("   ── 合计 ").Append(totAll).Append(" 个实例｜**长轴水平的应立件 ").Append(totLying)
              .Append(" 个**｜**姿态被覆盖 ").Append(totOver).Append(" 个**\n");
            if (lines.Count > 0)
            {
                sb.Append("   逐条（每类前 6 条）：\n");
                foreach (var kv in lines) sb.Append(kv.Value);
            }
            string text = sb.ToString();
            Log.Info(Tag + "UPRIGHT " + text);
            if (!string.IsNullOrWhiteSpace(path))
            {
                try { System.IO.File.WriteAllText(path.Trim(), text, System.Text.Encoding.UTF8); }
                catch (System.Exception e) { Log.Warn(Tag + "直立探针写盘失败：" + e.Message); }
            }
            return text;
        }

        /// <summary>把"每一件"收集成实例根：命中名字前缀即止（不再往里钻，免得把零件当实例）。</summary>
        private static void CollectInstances(Transform node, System.Collections.Generic.List<Transform> outv)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                Transform c = node.GetChild(i);
                if (c == null) continue;
                string n = c.name;
                if (n.StartsWith("ENV_", System.StringComparison.Ordinal) ||
                    n.StartsWith("DEC_", System.StringComparison.Ordinal) ||
                    n.StartsWith("DECOR_", System.StringComparison.Ordinal) ||
                    n.StartsWith("BUILD_", System.StringComparison.Ordinal) ||
                    n.StartsWith("CLOUD_", System.StringComparison.Ordinal) ||
                    n.StartsWith("NPC_", System.StringComparison.Ordinal) ||
                    n.StartsWith("ANIMAL_", System.StringComparison.Ordinal) ||
                    n.StartsWith("BIRD_", System.StringComparison.Ordinal))
                {
                    outv.Add(c);
                    continue;
                }
                CollectInstances(c, outv);
            }
        }

        /// <summary>分类（题面要求的"逐类"：树 / 灌木 / 建筑 / 装饰 / 云 / 活物）。</summary>
        private static string UpCategory(string name)
        {
            if (name.StartsWith("ENV_tree", System.StringComparison.Ordinal)) return "TREE";
            if (name.StartsWith("ENV_bush", System.StringComparison.Ordinal)) return "BUSH";
            if (name.StartsWith("ENV_", System.StringComparison.Ordinal)) return "ENV_其它";
            if (name.StartsWith("DEC_", System.StringComparison.Ordinal)) return "DEC_";
            if (name.StartsWith("DECOR_", System.StringComparison.Ordinal) ||
                name.StartsWith("BUILD_", System.StringComparison.Ordinal)) return "BUILD_";
            if (name.StartsWith("CLOUD_", System.StringComparison.Ordinal)) return "CLOUD_";
            if (name.StartsWith("NPC_", System.StringComparison.Ordinal) ||
                name.StartsWith("ANIMAL_", System.StringComparison.Ordinal) ||
                name.StartsWith("BIRD_", System.StringComparison.Ordinal)) return "LIFE_";
            return "其它";
        }

        /// <summary>`ENV_` 的 id 是否在"本来就该躺"的豁免表里。</summary>
        private static bool UpIsExempt(string name)
        {
            string id = name.StartsWith("ENV_", System.StringComparison.Ordinal) ? name.Substring(4) : name;
            for (int i = 0; i < UpExempt.Length; i++) if (id == UpExempt[i]) return true;
            return false;
        }

        /// <summary>
        /// 实例当前姿态 与 **"立正"** 的夹角（度）。返回 **−1** = 名字里没有资源 id / 资源取不到（不参与统计）。
        ///
        /// 判据的推导（为什么不是"和 `Euler(0,yaw,0)*assetFix` 比四元数"）：
        ///   ① `assetFix`（FBX 根节点自带的上轴修正）按定义把**网格自己的上轴**转成 +Y ⇒
        ///      网格上轴在资源空间里 = `assetFix⁻¹ * up`；
        ///   ② 于是"立正"= 实例的 rotation 把它转到**世界 +Y** ⇒ 只需看
        ///      `angle(rotation * (assetFix⁻¹ * up), up)`；yaw 完全自由，不参与 ✓
        ///   ③ 这条对 `Rx(270)` 与 `Rx(180)` 两族**同时成立**（不写死"网格上轴是 +Z"）✓
        ///   ④ 也避开了 `eulerAngles.y` 在 `x=±90` 处的**万向锁**：那里 yaw/roll 是一个自由度，
        ///      读回来的 `eulerAngles.y` 不再是原 yaw —— 直接比欧拉角会报出 80°~174° 的假偏差 ✗
        /// </summary>
        private static float PostureOffsetDeg(Transform t)
        {
            string n = t.name, id = null;
            if (n.StartsWith("ENV_", System.StringComparison.Ordinal)) id = n.Substring(4);
            else if (n.StartsWith("DEC_", System.StringComparison.Ordinal)) id = n.Substring(4);
            if (string.IsNullOrEmpty(id)) return -1f;
            GameObject prefab = Resources.Load<GameObject>("Art/Environment/" + id);
            if (prefab == null) return -1f;
            Vector3 meshUp = Quaternion.Inverse(prefab.transform.rotation) * Vector3.up;
            Vector3 worldUp = t.rotation * meshUp;
            return Vector3.Angle(worldUp, Vector3.up);
        }
    }
}
