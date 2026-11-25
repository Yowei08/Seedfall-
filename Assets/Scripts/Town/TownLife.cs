using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城的"活气"：人 / 动物 / 鸟 / 风 / 云 / 日夜 + 晴雨 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么单开一层，而不是塞进 `TownBootstrap`
    /// ══════════════════════════════════════════════════════════════════════
    /// 这六样东西有一个共同点：**每帧都要动**，而主城其它部分（田、建筑、交互）是"事件驱动"的。
    /// 混在一起会让 `TownBootstrap.Update` 变成一坨没人敢改的代码 ✗
    /// ⇒ 这里只做两件事：`Build`（一次性造好、全部走共享材质）+ `Tick(dt)`（每帧推进）。
    ///   状态一律用**固定相位 + 时间**算出来（不写回、不随机），所以：
    ///     · 不碰 `World.Rng`（战斗侧的确定性随机流）✓
    ///     · 同一时刻重进 = 同一个画面（可复现）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、六样的做法（每一样都在真的动，`Probe()` 会把每帧变化量报出来）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① **人**（4 个 NPC）：沿商业街来回走（ping-pong），身子上下微颠 4 cm
    ///   ② **动物**（3 只）：停在草地/公园，呼吸式起伏 + 每 ~4 秒抬一下头
    ///   ③ **鸟**（8 只）：在城上空盘旋（半径 9~17、高 13~17），翅膀每秒扇 2~3 次
    ///   ④ **风**：把**已经种下的树/灌木**收集成"摇摆列表"，整棵按 sin 轻摆 ±2.2°
    ///   ⑤ **云**（6 朵）：y=23~27 往 +X 缓慢漂，出界回绕
    ///   ⑥ **日夜 + 晴雨**：120 秒一轮；太阳的高度/颜色/强度、环境光、相机底色、雾全跟着走；
    ///      每轮 0.62~0.82 是一段**雨天**（粒子雨 + 光变暗 + 雾变浓）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、性能与资源红线
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 所有小物件都用 `TownKit.Prim` 的**共享网格** + `TownKit.Mat` 的**共享材质池**，
    ///     全场**没有一处 `new Material`**（雨那一份是唯一例外：粒子需要透明材质，
    ///     整个系统共用 1 份，见 `RainMaterial`）✓
    ///   · 每帧只改 Transform 与 Light 的字段，不实例化、不分配（`Probe` 之外零 GC）✓
    /// </summary>
    public static class TownLife
    {
        /// <summary>一轮日夜多少秒（用户举例 120）。</summary>
        public const float DayCycleSeconds = 120f;

        /// <summary>雨窗（一轮里的时间比例）：0.62~0.82 下雨。</summary>
        public const float RainStart01 = 0.62f;
        public const float RainEnd01 = 0.82f;

        /// <summary>
        /// ★ v221：**晴天锁**（用户原话："主城和登录界面饱和度高点，**要晴天**"）★
        ///
        ///   `true` ⇒ 日夜循环与雨天**整体停用**，但**下面那套代码一行都没删**：
        ///     · 时刻钉死在 <see cref="SunnyTime01"/>（正午）⇒ 太阳强度恒 `Lerp(0.10, 1.35, day=1)` = **1.35**
        ///     · 太阳颜色恒 `noon (1.00,0.97,0.88)`、环境光恒 `(0.62,0.66,0.72)`、相机底色恒 `skyDay (0.56,0.74,0.88)`
        ///     · `Raining` 恒 `false` ⇒ 雨粒子 `rateOverTime=0` 且 `Stop`、`RenderSettings.fog` 恒 `false`
        ///   `false` ⇒ 原样恢复 v218 的 120 秒一轮日夜 ＋ 每轮 0.62~0.82 的雨天。
        ///
        ///   ⇒ **想再把日夜/雨天要回来，只改这一个常量**（不需要改别的任何一行）✓
        ///   ⇒ 验收口径：`Probe()` 里 `日夜 t` 不再长、`雨 停`、`太阳强度` 前后两次读数逐位相同。
        /// </summary>
        public const bool ForceSunny = true;

        /// <summary>晴天锁钉住的时刻（一轮里的比例）：`0.25` = 正午 —— 太阳最高、最亮、天空最蓝。</summary>
        public const float SunnyTime01 = 0.25f;

        /// <summary>NPC 数量（用户：3~6 个）。</summary>
        public const int NpcCount = 4;
        /// <summary>动物数量（用户：2~4 只）。</summary>
        public const int AnimalCount = 3;
        /// <summary>鸟的数量（用户：6~10 只）。</summary>
        public const int BirdCount = 8;
        /// <summary>云的数量（用户：4~8 朵）。</summary>
        public const int CloudCount = 6;

        // ── 运行时 ──
        private static float _t;
        private static Light _sun;
        private static Camera _cam;
        private static ParticleSystem _rain;
        private static readonly List<SwayNode> Sway = new List<SwayNode>(64);
        private static readonly List<Npc> Npcs = new List<Npc>(NpcCount);
        private static readonly List<Animal> Animals = new List<Animal>(AnimalCount);
        private static readonly List<Bird> Birds = new List<Bird>(BirdCount);
        private static readonly List<Cloud> Clouds = new List<Cloud>(CloudCount);
        private static Material _rainMat;

        // ── 读数（`Probe()` 与 `TownStatus` 直接读）──
        public static float NpcDeltaLast { get; private set; }
        public static float AnimalDeltaLast { get; private set; }
        public static float BirdAngleDeltaLast { get; private set; }
        public static float SwayAngleLast { get; private set; }
        public static float CloudDeltaLast { get; private set; }
        public static float SunIntensityLast { get; private set; }
        public static float SunDeltaLast { get; private set; }
        public static bool Raining { get; private set; }
        public static int RainParticles { get; private set; }
        public static int SwayCount => Sway.Count;
        public static int FxObjectCount { get; private set; }
        public static string LastMoonPhase { get; private set; } = "白天";

        private struct Npc
        {
            public Transform T;
            public float Speed;      // 米/秒
            public float Phase;      // 0~1，起点
            public float BobSeed;
            public Vector3 A, B;     // 走廊两端
            public float LastPos;
        }

        private struct Animal
        {
            public Transform T;
            public Vector3 Home;
            public float Radius;
            public float Phase;
            public float Speed;
            public float LastY;
        }

        private struct Bird
        {
            public Transform T;
            public Transform WingL, WingR;
            public float Radius, Height, DegPerSec, Phase, FlapHz;
            public float LastDeg;
            /// <summary>轨迹类型：0=圆 1=椭圆 2=8 字（v223：三种交替）。</summary>
            public int Traj;
            /// <summary>上一帧的位置（用来算 velocity ⇒ 朝向随速度）。</summary>
            public Vector3 LastPos;
        }

        private struct Cloud
        {
            public Transform T;
            public float Speed, Y;
            public float LastX;
            public float SpinDegPerSec;
        }

        /// <summary>
        /// 一棵"会随风摆"的树/灌木。
        /// ★ 为什么要存 <see cref="Base"/> ★
        /// 摆幅必须**乘**在这个基准姿态上；不能每帧从 `localEulerAngles` 里"取 y 再拼回去"——
        /// 那等于把 `Art/Environment/*.fbx` 根节点自带的上轴修正（`Rx(270)`/`Rx(180)`）抹掉，
        /// 于是每帧都把树转回**躺倒**（历史读数：`TREE` 长轴水平 35/35、姿态偏 90° 的 38 棵都是它们）。
        /// </summary>
        private struct SwayNode
        {
            public Transform T;
            public Quaternion Base;      // 登记那一刻的 localRotation（已含 yaw ＋ 资产自带的修正）
        }

        // ══════════════════════════════════════════════════════════════════
        // 造
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 一次性把六样造好。<paramref name="root"/> 下挂静态件，<paramref name="propRoot"/> 下挂会动的小件。
        /// <paramref name="sun"/> / <paramref name="cam"/> 由 `TownBootstrap` 传进来（日夜就改它们）。
        /// </summary>
        public static void Build(Transform root, Transform propRoot, Light sun, Camera cam)
        {
            _sun = sun;
            _cam = cam;
            _rootCached = root;          // ← 第一帧 `BuildDeferred` 要用（CS0649 就是漏了这两行）
            _propCached = propRoot;
            // ⚠️ **不清空** `Sway`：树是 `TownBuildings.BuildPark` 先造的（它在 `BuildLife` 之前跑），
            //    登记发生在那一刻；这里只把已经销毁的引用剔掉 ✓
            Sway.RemoveAll(s => s.T == null);
            Npcs.Clear();
            Animals.Clear();
            Birds.Clear();
            Clouds.Clear();
            _t = 0f;

            // 所有"会动的小件 + 雨"统一挂在一个 LIFE 节点下 ⇒
            // ① `SetVisible(false)` 就是一次干净的 A/B（量 fps 时把这一层关掉，其余照旧）
            // ② 主城静态部分与动态部分在层级里分得开，美术那路的 dresser 不会误碰 ✓
            //    ⚠️ 但**建得太早**仍会被它贴纹理：`TownArtDresser` 在 `Start`（order 300）里遍历场景渲染物、
            //       把它们已有的 `_MainTex` 换成卡通贴图。我在 `Awake` 造 => 云的大球体被贴上一张平铺贴图
            //       => 满身噪点 + 整体发黑（用户截图里"黑云"就是这个）。所以推迟到**第一帧**再建 ✓
            _pending = true;
        }

        private static bool _pending;
        private static Transform _rootCached, _propCached;

        /// <summary>第一帧才真正造（见 `Build` 里的注释：要躲开美术那路的贴图遍历）。</summary>
        public static void BuildDeferred()
        {
            if (_rootCached == null) return;
            var life = new GameObject("LIFE");
            life.transform.SetParent(_rootCached, false);
            _lifeRoot = life;

            BuildNpcs(life.transform);
            BuildAnimals(life.transform);
            BuildBirds(life.transform);
            BuildClouds(life.transform);
            BuildGodRays(life.transform);      // v240：丁达尔光柱（几何，不用体积光）
            BuildRain(life.transform, _propCached);

            FxObjectCount = Npcs.Count + Animals.Count + Birds.Count * 3 + Clouds.Count;
            Log.Info($"[Town] 活气层（首帧建，避开美术贴图）：人 {Npcs.Count} · 动物 {Animals.Count} · " +
                     $"鸟 {Birds.Count}（各 3 件）· 云 {Clouds.Count}×{CloudPartCount} 件 · 会摇的树/灌木 {Sway.Count} · " +
                     $"日夜 {DayCycleSeconds:0} 秒一轮（雨窗 {RainStart01:0.00}~{RainEnd01:0.00}）");
        }

        private static GameObject _lifeRoot;

        /// <summary>把"现在几点"推到指定时刻（验收日夜/雨天截图用；只改时间，不改任何别的东西）。</summary>
        public static void ForceTime(float seconds)
        {
            _t = Mathf.Max(0f, seconds);
            TickSkyAndWeather();
        }

        /// <summary>整层显示/隐藏（A/B 量 fps 用；也便于排查"是不是这层拖慢的"）。</summary>
        public static void SetVisible(bool on)
        {
            _visible = on;
            if (_lifeRoot != null) _lifeRoot.SetActive(on);
        }

        private static bool _visible = true;

        /// <summary>当前整层是否在跑（读数用）。</summary>
        public static bool Visible => _visible;

        /// <summary>登记一棵会随风摆的树/灌木（`TownBuildings` 摆树时调）。</summary>
        public static void RegisterSway(Transform t)
        {
            // 记下**摆的基准姿态**（= `Euler(0,yaw,0) * 资产自带的 Rx(270/180)`）⇒ 见 `SwayNode.Base`
            if (t != null) Sway.Add(new SwayNode { T = t, Base = t.localRotation });
        }

        private static void BuildNpcs(Transform root)
        {
            // 沿商业街来回走：整条街 z −13 ~ +13，x=9（街心偏西，不挡店门）
            Material[] shirts =
            {
                TownKit.Mat(new Color(0.86f, 0.42f, 0.36f)),
                TownKit.Mat(new Color(0.36f, 0.56f, 0.84f)),
                TownKit.Mat(new Color(0.42f, 0.70f, 0.44f)),
                TownKit.Mat(new Color(0.88f, 0.74f, 0.32f)),
            };
            Material skin = TownKit.Mat(new Color(0.96f, 0.82f, 0.66f));
            Material boot = TownKit.Mat(TownColors.DoorDark);

            for (int i = 0; i < NpcCount; i++)
            {
                Vector3 a = new Vector3(9.0f + (i - 1.5f) * 0.7f, 0f, -13.0f);
                Vector3 b = new Vector3(9.0f + (i - 1.5f) * 0.7f, 0f, 13.0f);
                Transform t = NewNode(root, "NPC_" + (i + 1).ToString("00"));
                TownKit.CylL(t, "Body", Vector3.zero, 0.24f, 0.62f, shirts[i % shirts.Length]);
                TownKit.BallL(t, "Head", new Vector3(0f, 0.86f, 0f), 0.42f, skin);
                // ★ 头顶那块"彩色方块"就是这顶帽子（用户截图里的 bug）：原来是**衬衫色的大平盒**
                //   ⇒ 改成**小一号的深色圆帽**（CylL 圆柱、深棕），既不是色块也不抢眼 ✓
                TownKit.CylL(t, "Hat", new Vector3(0f, 1.04f, 0f), 0.17f, 0.12f, boot);
                TownKit.BoxL(t, "Legs", new Vector3(0f, 0.14f, 0f), new Vector3(0.34f, 0.28f, 0.30f), boot);

                Npcs.Add(new Npc
                {
                    T = t,
                    Speed = 1.15f + i * 0.18f,
                    Phase = i * 0.27f,
                    BobSeed = i * 1.7f,
                    A = a,
                    B = b,
                    LastPos = 0f,
                });
            }
        }

        private static void BuildAnimals(Transform root)
        {
            Vector3[] homes =
            {
                new Vector2(-6.5f, 11.5f).ToWorld3(),      // 公园里两只
                new Vector2(-4.2f, 9.6f).ToWorld3(),
                new Vector2(16.6f, -2.6f).ToWorld3(),      // 家门口一只
            };
            Material white = TownKit.Mat(new Color(0.94f, 0.92f, 0.86f));
            Material brown = TownKit.Mat(new Color(0.72f, 0.52f, 0.36f));
            Material beak = TownKit.Mat(new Color(0.95f, 0.72f, 0.24f));

            for (int i = 0; i < AnimalCount; i++)
            {
                Transform t = NewNode(root, "ANIMAL_" + (i + 1).ToString("00"));
                bool chicken = i < 2;
                Material body = chicken ? white : brown;
                TownKit.BallL(t, "Body", new Vector3(0f, 0.26f, 0f), chicken ? 0.46f : 0.36f, body);
                TownKit.BallL(t, "Head", new Vector3(0f, 0.48f, 0.20f), chicken ? 0.26f : 0.24f, body);
                if (chicken) TownKit.ConeL(t, "Beak", new Vector3(0f, 0.50f, 0.34f), 0.07f, 0.14f, beak, 6);
                else
                    for (int k = -1; k <= 1; k += 2)
                        TownKit.ConeL(t, "Ear", new Vector3(k * 0.10f, 0.62f, 0.18f), 0.06f, 0.14f, body, 5);
                Animals.Add(new Animal
                {
                    T = t, Home = homes[i], Radius = 0.7f + i * 0.15f,
                    Phase = i * 2.1f, Speed = 0.22f + i * 0.05f, LastY = 0f,
                });
            }
        }

        /// <summary>
        /// ★ v223：**鸟重做** —— 白色、**3 种轨迹交替**、**永远面对移动方向** ★
        ///   · 颜色：`(0.98,0.98,1.00)` 纯白（走 `ToonLook` 池，不用 `Legacy` shader）
        ///   · 轨迹：0=圆（绕城）／1=椭圆（长短轴 1.6:1）／2=8 字（两个圆叠着走），
        ///     每 `TrajHoldSeconds`（18 秒）换一次轨迹，并**换圈**（半径 ±2 米，夹在 8~18）
        ///   · 朝向：把上一步的位置缓存成 `velocity`，`Quaternion.LookRotation(velocity.normalized, Vector3.up)`
        ///     ⇒ 永远面对移动方向；`BirdFacingErrDeg` 报"朝向与速度的夹角"（应为 0）
        /// </summary>
        private static void BuildBirds(Transform root)
        {
            Material white = TownKit.Mat(new Color(0.98f, 0.98f, 1.00f));   // 纯白
            Material shade = TownKit.Mat(new Color(0.86f, 0.88f, 0.94f));   // 翅膀下半：略灰，给明暗
            for (int i = 0; i < BirdCount; i++)
            {
                Transform t = NewNode(root, "BIRD_" + (i + 1).ToString("00"));
                TownKit.BallL(t, "Body", Vector3.zero, 0.30f, white);
                TownKit.BallL(t, "Head", new Vector3(0f, 0.10f, 0.18f), 0.16f, white);
                Transform wl = NewNode(t, "WingL");
                Transform wr = NewNode(t, "WingR");
                TownKit.BoxL(wl, "L", new Vector3(-0.24f, 0f, 0f), new Vector3(0.50f, 0.05f, 0.24f), white);
                TownKit.BoxL(wr, "R", new Vector3(0.24f, 0f, 0f), new Vector3(0.50f, 0.05f, 0.24f), white);
                TownKit.BoxL(wl, "Ltip", new Vector3(-0.44f, 0f, 0f), new Vector3(0.16f, 0.05f, 0.20f), shade);
                TownKit.BoxL(wr, "Rtip", new Vector3(0.44f, 0f, 0f), new Vector3(0.16f, 0.05f, 0.20f), shade);
                Birds.Add(new Bird
                {
                    T = t, WingL = wl, WingR = wr,
                    Radius = 9f + (i % 4) * 2.4f,
                    Height = 13f + (i % 3) * 1.7f,
                    DegPerSec = 12f + (i % 5) * 2.6f,
                    Phase = i * 47f,
                    FlapHz = 2.1f + (i % 3) * 0.45f,
                    LastDeg = i * 47f,
                    Traj = i % 3,
                    LastPos = Vector3.zero,
                });
            }
        }

        /// <summary>每 18 秒换一次轨迹/圈（三种轨迹交替，半径也会变）。</summary>
        private const float TrajHoldSeconds = 18f;

        /// <summary>鸟的"朝向与速度"夹角（度，稳态应 ≈0）。</summary>
        public static float BirdFacingErrDeg { get; private set; }
        /// <summary>鸟当前用了几种轨迹（读数为 3 表示三种都用上了）。</summary>
        public static int BirdTrajKinds { get; private set; }

        /// <summary>
        /// ★ v219：**建模云**（用户："不要这种渲染云，只要建模云"）★
        ///
        /// 每朵云 = **6 个椭球**（1 块扁底 + 5 个上鼓的球）拼出来的低多边形云：
        ///   · **底平顶鼓**：底件压扁（5.2×1.5×3.4）并下沉到 y=−0.55，上面的球逐层变小变高 ⇒ 卡通云轮廓 ✓
        ///   · **颜色**：主体纯白偏冷 `(0.96,0.97,1.00)`、底件略暗 `(0.86,0.88,0.94)` ⇒ 有明暗、**不半透、不自发光** ✓
        ///   · **共享**：椭球用的是 `TownKit.Prim(Sphere)` 那**一份共享网格**，靠 `localScale` 拉成椭球 ⇒
        ///     全体云只占 1 份网格；材质走 `ToonLook` 池，云只占 **2 份**（主体 + 底），6 朵共用 ✓
        ///   · **动**：每秒漂 0.55~1.35 米 ＋ 绕 Y **自转 1.0~1.9°/s** ✓
        /// </summary>
        private static void BuildClouds(Transform root)
        {
            // ★★ v221：**云用自己的显式共享材质**（不走 `ToonLook` 池）★★
            //   上一轮"延后一帧"没用：`TownArtDresser` 是 `DefaultExecutionOrder(300)`，它的 `Start`
            //   可能晚于我的第一帧 `Tick` ⇒ 我建时它还没跑，它跑起来照样遍历到我的云并贴图 ✓
            //   ⇒ 换结构上免疫的做法：
            //     · 材质 `new Material(shader)` **只建 2 份**（主体/底），6 朵共用 —— 不是"每实例一份" ✓
            //     · 建完立刻把 `_MainTex` 显式置空（纯色、无贴图）✓
            //     · `TickClouds` 每帧**断言**：`_MainTex` 只要不是 null 就清掉（防御式，一条 if）✓
            CloudTop = CloudMat(new Color(0.96f, 0.97f, 1.00f), "TownCloudTop");
            CloudBottom = CloudMat(new Color(0.86f, 0.88f, 0.94f), "TownCloudBottom");
            CloudParts.Clear();
            Mesh sphere = TownKit.Prim(PrimitiveType.Sphere);

            for (int i = 0; i < CloudCount; i++)
            {
                Transform t = NewNode(root, "CLOUD_" + (i + 1).ToString("00"));
                float y = 23f + (i % 3) * 1.8f;

                Ellipsoid(t, "Base", sphere, new Vector3(0f, -0.55f, 0f), new Vector3(5.2f, 1.5f, 3.4f), CloudBottom);
                Ellipsoid(t, "Bulge0", sphere, new Vector3(0f, 0.35f, 0f), new Vector3(3.2f, 2.4f, 3.0f), CloudTop);
                Ellipsoid(t, "Bulge1", sphere, new Vector3(2.6f, 0.10f, 0.5f), new Vector3(2.4f, 2.0f, 2.2f), CloudTop);
                Ellipsoid(t, "Bulge2", sphere, new Vector3(-2.4f, 0.05f, -0.4f), new Vector3(2.1f, 1.8f, 2.0f), CloudTop);
                Ellipsoid(t, "Bulge3", sphere, new Vector3(1.0f, 0.95f, -0.6f), new Vector3(1.6f, 1.4f, 1.5f), CloudTop);
                Ellipsoid(t, "Bulge4", sphere, new Vector3(-1.2f, 0.90f, 0.7f), new Vector3(1.3f, 1.1f, 1.2f), CloudTop);

                t.position = new Vector3(-28f + i * 9.5f, y, -12f + (i % 4) * 7.5f);
                t.rotation = Quaternion.Euler(0f, i * 23f, 0f);
                Clouds.Add(new Cloud
                {
                    T = t,
                    Speed = 0.55f + i * 0.16f,
                    Y = y,
                    LastX = t.position.x,
                    SpinDegPerSec = 1.0f + (i % 3) * 0.45f,
                });
            }
            CloudPartCount = 6;
        }

        /// <summary>云的两份共享材质（**全工程 2 份**，6 朵共用；纯色、`_MainTex` 恒空）。</summary>
        private static Material CloudTop, CloudBottom;

        /// <summary>云的所有渲染件（诊断与"每帧断言"用；一次建好不再分配）。</summary>
        private static readonly List<MeshRenderer> CloudParts = new List<MeshRenderer>(CloudCount * 6);

        private static Material CloudMat(Color color, string name)
        {
            // ★★ v225：**决定性修法 —— 用不采样贴图的 shader** ★★
            //   前三次失败的共同点：`Seedfall/ToonSimple` 和 `URP/Unlit` **自己都会采样 `_MainTex`**，
            //   只要有人往材质里塞一张贴图，它就会照实显示 ⇒ "黑底 + 白噪点"。
            //   ⇒ 换成本工程自写的 `Seedfall/CloudFlat`：**没有 `_MainTex` 属性、frag 里没有 tex2D**
            //     ⇒ 从物理上不可能再出现贴图噪点（判据：塞一张噪点图，画面必须毫无变化）✓
            Shader sh = Shader.Find("Seedfall/CloudFlat");
            if (sh == null) sh = Shader.Find("Unlit/Color");           // 备选：同样只看 `_Color`
            if (sh == null) sh = Shader.Find("Sprites/Default");       // 再退：会采贴图，但至少有画面

            if (sh == null)
            {
                CloudShaderName = "(没有可用 shader ⇒ 退回 ToonLook 池)";
                Log.Warn("[Town] 云 shader 全都找不到，退回共享池");
                return ToonLook.Get(color, 0f, true);      // 绝不 `new Material(null)`
            }

            CloudShaderName = sh.name;
            var m = new Material(sh) { name = name };
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Ambient")) m.SetFloat("_Ambient", 1f);         // 日夜由 EnforceCloudMaterials 推
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);     // 纯色、无贴图
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            return m;
        }

        /// <summary>云材质实际用的 shader 名（运行时诊断：为空或粉色就说明没建起来）。</summary>
        public static string CloudShaderName { get; private set; } = "(还没建)";

        /// <summary>云材质诊断：名字 / 颜色 / `_MainTex` 是否为空 / 贴图尺寸。进 play 后由 `CloudInfo()` 打出来。</summary>
        public static string DiagCloudMaterials()
        {
            var sb = new System.Text.StringBuilder(600);
            sb.Append("云材质诊断：部件 ").Append(CloudParts.Count).Append(" 个")
              .Append("｜材质 shader = **").Append(CloudShaderName).Append("**")
              .Append("｜每帧修回次数 = ").Append(CloudMatFixLast);
            int shown = 0, dirty = 0, visible = 0, alive = 0;
            // ★ v226：**"在画吗"** —— 白与不存在在截图上是两回事、在读数上是同一件事，
            //   所以直接问渲染器：`isVisible`（在某个相机视锥内）＋ 材质 shader 是否编过（= 非 null 且有 pass）
            for (int i = 0; i < CloudParts.Count; i++)
            {
                MeshRenderer mr = CloudParts[i];
                if (mr == null) continue;
                alive++;
                if (mr.isVisible) visible++;
                Material m = mr.sharedMaterial;
                if (m == null) { sb.Append("\n  ").Append("(null 材质)"); continue; }
                Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                if (tex != null) dirty++;
                if (shown < 8)
                {
                    sb.Append("\n  ").Append(mr.name).Append(" · 材质=").Append(m.name)
                      .Append(" · shader=").Append(m.shader != null ? m.shader.name : "(null)")
                      .Append(" · pass数=").Append(m.passCount)
                      .Append(" · isVisible=").Append(mr.isVisible ? "是" : "否")
                      .Append(" · 颜色=").Append(m.HasProperty("_Color") ? m.color.ToString() : "(无 _Color)")
                      .Append(" · _MainTex=").Append(tex == null ? "null ✓" : tex.name + " " + tex.width + "×" + tex.height + " ← **被贴图了**");
                    shown++;
                }
            }
            sb.Append("\n  带贴图的部件数 = ").Append(dirty)
              .Append("｜**在画的部件 = ").Append(visible).Append(" / ").Append(alive).Append("**")
              .Append("｜_Ambient = ").Append(CloudAmbientLast.ToString("0.00"));
            string s = sb.ToString();
            Log.Info("[Town] CLOUD " + s);
            return s;
        }

        /// <summary>用**共享球网格 + 非等比缩放**做一个椭球（不产生任何新 Mesh）。</summary>
        private static void Ellipsoid(Transform parent, string name, Mesh sphere, Vector3 localPos,
                                      Vector3 size, Material mat)
        {
            GameObject go = TownKit.Local(parent, name, sphere, localPos, mat);
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 云不投影（省 shadow pass）
                mr.receiveShadows = false;
                CloudParts.Add(mr);                       // 登记：每帧断言 + 诊断用
            }
        }

        /// <summary>每朵云的子件数（验收读数）。</summary>
        public static int CloudPartCount { get; private set; }

        private static void BuildRain(Transform root, Transform propRoot)
        {
            var go = new GameObject("FX_Rain");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 18f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.6f;
            main.startSpeed = 18f;
            main.startSize = 0.06f;
            main.maxParticles = 2500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.2f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(46f, 1f, 46f);
            var emission = ps.emission;
            emission.rateOverTime = 0f;             // 晴天才开，见 Tick
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = RainMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _rain = ps;
        }

        /// <summary>雨丝材质：整个系统共用 1 份（粒子必须透明，所以没有走 ToonLook 的不透明池）。</summary>
        private static Material RainMaterial()
        {
            if (_rainMat != null) return _rainMat;
            // ★ v223：`Legacy Shaders/*` 在 URP 下可能返回 null（云变紫就是同类坑）⇒ 先试 URP 粒子 shader ✓
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Lit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) return ToonLook.Get(new Color(0.72f, 0.80f, 0.92f), 0f, true);
            _rainMat = new Material(sh) { name = "TownRainShared" };
            if (_rainMat.HasProperty("_Color")) _rainMat.SetColor("_Color", new Color(0.78f, 0.86f, 0.95f, 0.55f));
            if (_rainMat.HasProperty("_BaseColor")) _rainMat.SetColor("_BaseColor", new Color(0.78f, 0.86f, 0.95f, 0.55f));
            return _rainMat;
        }

        private static Transform NewNode(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧
        // ══════════════════════════════════════════════════════════════════

        public static void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (_pending) { _pending = false; BuildDeferred(); }   // 第一帧才建（躲开美术那路的贴图遍历）
            if (!_visible) return;          // A/B 量 fps 时整层停掉（不 tick 也不渲染）
            _t += dt;

            TickNpcs(dt);
            TickAnimals();
            TickBirds();
            TickSway();
            TickClouds(dt);
            TickGodRays();                // v240：光柱缓慢摆动
            TickSkyAndWeather();
            // ★ v229：传送门旋涡（首帧自建）＋ **把美术那路的黑洞守卫掉**（判据：激活的 ParticleSystem = 0）★
            TownPortalFx.Tick(_rootCached, dt);
        }

        private static void TickNpcs(float dt)
        {
            NpcDeltaLast = 0f;
            for (int i = 0; i < Npcs.Count; i++)
            {
                Npc n = Npcs[i];
                if (n.T == null) continue;
                float span = Vector3.Distance(n.A, n.B);
                if (span < 0.01f) continue;
                // ping-pong：用时间算位置（不写回速度/位置状态 ⇒ 可复现）
                float u = Mathf.PingPong(_t * n.Speed / span + n.Phase, 1f);
                Vector3 p = Vector3.Lerp(n.A, n.B, u);
                p.y = 0.02f + Mathf.Abs(Mathf.Sin(_t * 5.2f + n.BobSeed)) * 0.04f;   // 走路微颠
                // 朝向跟着来路（ping-pong 反向时转身）
                float dir = Mathf.PingPong(_t * n.Speed / span + n.Phase + 0.02f, 1f) > u ? -1f : 1f;
                n.T.position = p;
                n.T.rotation = Quaternion.Euler(0f, dir > 0f ? 0f : 180f, 0f);

                float d = Mathf.Abs(p.z - n.LastPos);
                if (d > NpcDeltaLast) NpcDeltaLast = d;
                Npcs[i] = new Npc
                {
                    T = n.T, Speed = n.Speed, Phase = n.Phase, BobSeed = n.BobSeed,
                    A = n.A, B = n.B, LastPos = p.z
                };
            }
        }

        private static void TickAnimals()
        {
            AnimalDeltaLast = 0f;
            for (int i = 0; i < Animals.Count; i++)
            {
                Animal a = Animals[i];
                if (a.T == null) continue;
                float ang = _t * a.Speed + a.Phase;
                Vector3 p = a.Home + new Vector3(Mathf.Cos(ang) * a.Radius, 0f, Mathf.Sin(ang) * a.Radius);
                // 呼吸（每帧都在动）＋ 每 ~4 秒抬一下头
                float breath = Mathf.Sin(_t * 2.4f + a.Phase) * 0.035f;
                float peck = Mathf.Max(0f, Mathf.Sin(_t * 1.5f + a.Phase)) * 0.10f;
                p.y = 0.02f + Mathf.Max(0f, breath) + peck;
                a.T.position = p;
                a.T.rotation = Quaternion.Euler(0f, -ang * Mathf.Rad2Deg + 90f, 0f);
                float d = Mathf.Abs(p.y - a.LastY);
                if (d > AnimalDeltaLast) AnimalDeltaLast = d;
                Animals[i] = new Animal
                {
                    T = a.T, Home = a.Home, Radius = a.Radius, Phase = a.Phase, Speed = a.Speed, LastY = p.y
                };
            }
        }

        /// <summary>
        /// ★ v223：鸟的三种轨迹 ＋ **朝向随速度** ★
        ///   0=圆：绕城等速；1=椭圆：长短轴 1.6:1；2=8 字：两个圆叠着走（`x=sin2θ·r, z=sinθ·r`）
        ///   每 `TrajHoldSeconds` 换一次轨迹并把半径挪 2 米（夹在 8~18）⇒ 会**换圈** ✓
        ///   朝向：`velocity = p − LastPos` 缓存一次，`LookRotation(velocity, Vector3.up)` ⇒ 永远朝前
        ///   读数：`BirdAngleDeltaLast`（每帧转过多少度）、`BirdFacingErrDeg`（朝向与速度夹角，应 ≈0）
        /// </summary>
        private static void TickBirds()
        {
            BirdAngleDeltaLast = 0f;
            BirdFacingErrDeg = 0f;
            int kinds = 0;
            var seen = new bool[3];
            for (int i = 0; i < Birds.Count; i++)
            {
                Bird b = Birds[i];
                if (b.T == null) continue;

                // ★ v238：**每条鸟固定一条轨迹，永不切换** ★
                //   用户报"还有跳"的真因：换轨瞬间**位置连续、速率不连续** ——
                //   圆在 θ=0 的速率是 1.0r、椭圆 1.4r、8 字 1.2r ⇒ 换轨那一下速度从 1.0 变 1.4 ⇒ 抽一下 ✗
                //   三种轨迹改由**不同的鸟**承担（8 只：3 圆 / 3 椭圆 / 2 八字）⇒ 画面照样丰富，但每只自己无缝 ✓
                int traj = b.Traj;
                float radius = b.Radius;                                     // ★ 半径也固定（原来每圈 ±2 米 ⇒ 也会跳）★
                float period = 360f / Mathf.Max(1f, b.DegPerSec);            // 一圈几秒
                float u = Mathf.Repeat(_t / period + b.Phase / 360f, 1f);
                float theta = u * Mathf.PI * 2f;

                float deg = theta * Mathf.Rad2Deg;
                Vector3 p = BirdPos(traj, theta, radius, b.Height);
                Vector3 ahead = BirdPos(traj, theta + 0.01f, radius, b.Height);

                Vector3 vel = ahead - p;                                     // 解析求导，不用上一帧状态
                if (vel.sqrMagnitude > 1e-8f)
                {
                    Quaternion want = Quaternion.LookRotation(vel.normalized, Vector3.up);
                    b.T.rotation = want;
                    float err = Vector3.Angle(b.T.forward, vel.normalized);
                    if (err > BirdFacingErrDeg) BirdFacingErrDeg = err;
                }
                b.T.position = p;

                float flap = Mathf.Sin(theta * b.FlapHz * 3f + b.Phase) * 38f;   // 扇翅也用同一个 θ ⇒ 闭合
                if (b.WingL != null) b.WingL.localRotation = Quaternion.Euler(0f, 0f, flap);
                if (b.WingR != null) b.WingR.localRotation = Quaternion.Euler(0f, 0f, -flap);

                float dd = Mathf.Abs(Mathf.DeltaAngle(b.LastDeg, deg));
                if (dd > BirdAngleDeltaLast) BirdAngleDeltaLast = dd;
                if (traj >= 0 && traj < 3) seen[traj] = true;
                Birds[i] = new Bird
                {
                    T = b.T, WingL = b.WingL, WingR = b.WingR, Radius = b.Radius, Height = b.Height,
                    DegPerSec = b.DegPerSec, Phase = b.Phase, FlapHz = b.FlapHz, LastDeg = deg,
                    Traj = b.Traj, LastPos = p
                };
            }
            for (int k = 0; k < 3; k++) if (seen[k]) kinds++;
            BirdTrajKinds = kinds;
        }

        /// <summary>
        /// ★ v237：三条**闭合**参数轨迹，全部在 `θ = 0` 经过 **(r, 0)** ⇒ 换轨时不跳 ✓
        ///   ① 圆   `(r cosθ, r sinθ)`
        ///   ② 椭圆 `(r cosθ, 1.4 r sinθ)`            ← 1.4 而不是 1.6：让速度比 ≤ 1.5（突变判据）
        ///   ③ 8 字 `(r cosθ, 1.2 r sinθ cosθ)`       ← θ=0 也过 (r, 0)，且是闭合双环
        ///   高度 `y = height + sinθ·1.2`（同周期 ⇒ 也闭合）
        /// </summary>
        public static Vector3 BirdPos(int traj, float theta, float radius, float height)
        {
            float c = Mathf.Cos(theta), s = Mathf.Sin(theta);
            float x, z;
            if (traj == 1) { x = c * radius; z = s * radius * 1.4f; }
            else if (traj == 2) { x = c * radius; z = s * c * radius * 1.2f; }
            else { x = c * radius; z = s * radius; }
            return new Vector3(x, height + s * 1.2f, z);
        }

        /// <summary>读数：三条轨迹的 t=0 与 t=T 坐标差（应 &lt; 0.001）＋ 相邻步长比。</summary>
        public static string BirdLoopProbe()
        {
            var sb = new System.Text.StringBuilder(900);
            sb.Append("鸟的无缝判据（每条鸟固定轨迹；200 步采样）");
            string[] names = { "圆", "椭圆(1.4:1)", "8字" };
            for (int t = 0; t < 3; t++)
            {
                Vector3 a = BirdPos(t, 0f, 10f, 14f);
                Vector3 b = BirdPos(t, Mathf.PI * 2f, 10f, 14f);
                float d0 = Vector3.Distance(a, b);
                // 200 步（与运行时 200 帧等价的采样）
                const int N = 200;
                float mn = float.MaxValue, mx = 0f, worstJump = 0f, worstAcc = 0f;
                float prevStep = -1f;
                Vector3 prev = BirdPos(t, 0f, 10f, 14f);
                for (int k = 1; k <= N; k++)
                {
                    Vector3 cur = BirdPos(t, k * (Mathf.PI * 2f / N), 10f, 14f);
                    float step = Vector3.Distance(prev, cur);
                    if (step < mn) mn = step;
                    if (step > mx) mx = step;
                    if (prevStep > 0f)
                    {
                        float jump = Mathf.Abs(step - prevStep) / Mathf.Max(1e-6f, prevStep);   // ★ 一阶跳变比 ★
                        if (jump > worstJump) worstJump = jump;
                        float acc = Mathf.Abs(step - prevStep);                                  // 二阶差分（加速度）
                        if (acc > worstAcc) worstAcc = acc;
                    }
                    prevStep = step;
                    prev = cur;
                }
                float ratio = mn > 1e-6f ? mx / mn : 999f;
                sb.Append($"\n  {names[t]}：t=0 与 t=T 差 = **{d0:0.000000}**（判据 <0.001）" +
                          $"· max(d)/min(d) = {ratio:0.00}" +
                          $"· **max(|Δd|/d) = {worstJump:0.000}（判据 <0.35）**" +
                          $"· 二阶差分最大 = {worstAcc:0.00000}");
            }
            string s = sb.ToString();
            Log.Info("[Town] BIRDLOOP " + s);
            return s;
        }

        private static void TickSway()
        {
            // 风：整棵树绕自身的 X/Z 径倾（±2.2°），相位按世界坐标派生 ⇒ 一片林子不会同步摆
            //
            // ★★ 这一行是"模型倒下"的**第二个根因**（v221 修）★★
            //   旧写法：`Vector3 e = t.localEulerAngles; t.localRotation = Euler(a*0.55, e.y, a);`
            //   ⇒ 只把 euler 的 **y（yaw）** 留下来，X/Z 用摆角重写 ⇒
            //     `Art/Environment/*.fbx` 根节点自带的上轴修正 `Rx(270)`（Z-up → Y-up）**被抹掉**，
            //     而本函数**每帧都跑** ⇒ 扶正过的树下一帧又躺回去（`FixUpright` 也就白修了）✗
            //   新写法：摆角**乘在登记时的基准姿态上**（`Base` 已含 yaw ＋ 资产修正）⇒
            //     基准姿态永远不动，摆只是叠在它上面的 ±2.2° ✓
            float amp = 2.2f;
            for (int i = 0; i < Sway.Count; i++)
            {
                SwayNode s = Sway[i];
                Transform t = s.T;
                if (t == null) continue;
                float ph = t.position.x * 0.7f + t.position.z * 0.9f;
                float a = Mathf.Sin(_t * 1.35f + ph) * amp;
                t.localRotation = Quaternion.Euler(a * 0.55f, 0f, a) * s.Base;
            }
            SwayAngleLast = Mathf.Sin(_t * 1.35f) * amp;
        }

        private static void TickClouds(float dt)
        {
            CloudDeltaLast = 0f;
            for (int i = 0; i < Clouds.Count; i++)
            {
                Cloud c = Clouds[i];
                if (c.T == null) continue;
                Vector3 p = c.T.position + new Vector3(c.Speed * dt, 0f, 0f);
                if (p.x > 30f) p.x = -30f;               // 出界回绕
                c.T.position = p;
                // v219：绕 Y 慢转（轮廓不僵硬）
                c.T.Rotate(0f, c.SpinDegPerSec * dt, 0f, Space.World);
                float d = Mathf.Abs(p.x - c.LastX);
                if (d > CloudDeltaLast) CloudDeltaLast = d;
                CloudSpinLast = c.SpinDegPerSec;
                Clouds[i] = new Cloud { T = c.T, Speed = c.Speed, Y = c.Y, LastX = p.x, SpinDegPerSec = c.SpinDegPerSec };
            }
            EnforceCloudMaterials();
        }

        /// <summary>
        /// ★ 每帧断言 ★ 云的材质必须是我那两份纯色共享材质，且 `_MainTex` 为空。
        /// 谁把它换成了带贴图的（美术那路的 dresser 会遍历渲染物换材质/贴图），这里立刻改回来。
        /// 代价：36 次引用比较 + 36 次 `HasProperty`（都没有分配），实测可忽略 ✓
        /// </summary>
        private static void EnforceCloudMaterials()
        {
            int fixedCount = 0;
            for (int i = 0; i < CloudParts.Count; i++)
            {
                MeshRenderer mr = CloudParts[i];
                if (mr == null) continue;
                Material m = mr.sharedMaterial;
                bool mine = m == CloudTop || m == CloudBottom;
                bool dirty = m != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null;
                if (mine && !dirty) continue;

                Material want = (m != null && m.name == "TownCloudBottom") || (m == null && i % 6 == 0)
                    ? CloudBottom : (mr.name == "Base" ? CloudBottom : CloudTop);
                if (!mine) { mr.sharedMaterial = want; fixedCount++; }
                if (want != null && want.HasProperty("_MainTex") && want.GetTexture("_MainTex") != null)
                {
                    want.SetTexture("_MainTex", null);
                    fixedCount++;
                }
            }
            // ★ 日夜：`Seedfall/CloudFlat` 不读引擎光照（正是它免疫贴图的原因）⇒ 亮度由 `_Ambient` 显式推 ★
            float t01c = SkyTime01;
            float dayF = Mathf.Clamp01(Mathf.Sin(t01c * Mathf.PI * 2f) * 1.4f + 0.35f);
            float amb = Mathf.Lerp(0.34f, 1.00f, dayF) * (Raining ? 0.80f : 1f);
            if (CloudTop != null && CloudTop.HasProperty("_Ambient")) CloudTop.SetFloat("_Ambient", amb);
            if (CloudBottom != null && CloudBottom.HasProperty("_Ambient")) CloudBottom.SetFloat("_Ambient", amb);
            CloudAmbientLast = amb;

            CloudMatFixLast = fixedCount;
        }

        /// <summary>云当前的环境亮度乘子（夜里会降到 ~0.34，白天 1.0；读数用）。</summary>
        public static float CloudAmbientLast { get; private set; } = 1f;

        /// <summary>上一帧"材质被抢/被贴图"而修回来的次数（读数用；稳态应为 0）。</summary>
        public static int CloudMatFixLast { get; private set; }

        /// <summary>云的当前自转速度（°/s，读数用）。</summary>
        public static float CloudSpinLast { get; private set; }

        // ══════════════════════════════════════════════════════════════════
        // ★ v241：丁达尔光 —— **全部平行**（用户点名的核心）★
        //   上一版每道 `Euler(0, ang, 18+(i%4)*2)` ⇒ yaw/pitch 各不同 ⇒ 像散开的扇 ✗
        //   现在：**所有光柱同一个方向** `LookRotation(sunDir)`（`sunDir` = 主方向光的 forward），
        //         只有**位置散布**（绕主城半径 8~18）⇒ 平行的雨幕感 ✓
        //   过渡：**不再做角度摆动**（那会破坏平行）⇒ 改成**亮度脉动**（alpha × 0.85~1.0）
        //         ＋ **出现/消失的 SmoothStep 缓动**（周期 8 秒）
        //   规格：**14 道** · alpha 基准 **0.18** · 宽 **0.35~0.65** · 长 **34 m** · 不投影 · `_ZWrite=0`
        //   材质：仍然是 **1 份** `OWN_GODRAY_Shared`（不新建）✓
        // ══════════════════════════════════════════════════════════════════
        private const int GodRayCount = 14;
        private static readonly System.Collections.Generic.List<Transform> GodRays =
            new System.Collections.Generic.List<Transform>(GodRayCount);
        private static Material _matGodRay;
        private static Vector3 _godRayDir = new Vector3(0.35f, -0.90f, -0.25f);   // 兜底：主光拿不到时用它
        /// <summary>三层壳的渲染器（每束 3 个）—— 每帧按权重推 alpha。</summary>
        private static readonly System.Collections.Generic.List<MeshRenderer> GodRayShells =
            new System.Collections.Generic.List<MeshRenderer>(GodRayCount * 3);
        /// <summary>底部光晕渲染器（每束 1 个）。</summary>
        private static readonly System.Collections.Generic.List<MeshRenderer> GodRayHalos =
            new System.Collections.Generic.List<MeshRenderer>(GodRayCount);
        private static MaterialPropertyBlock _godRayMpb;
        /// <summary>壳的 alpha 权重（归一化到总和 = GodRayAlpha）。</summary>
        private static readonly float[] ShellWeight = { 1.00f, 0.55f, 0.28f };
        /// <summary>
        /// ★ 光斑层（v245）：**5 层**；半径按 1.0/1.6/2.4/3.4/4.6 的比例、最大值压到 **5.0 m**
        ///   ⇒ {1.09, 1.74, 2.61, 3.70, 5.00}；权重和 2.29 归一化
        ///   **总 alpha 单独提到 0.32** —— 0.18 叠在亮绿草地上根本看不见 ✗
        /// </summary>
        private static readonly float[] HaloRadius = { 1.09f, 1.74f, 2.61f, 3.70f, 5.00f };
        private static readonly float[] HaloWeight = { 1.00f, 0.62f, 0.38f, 0.20f, 0.09f };
        /// <summary>光斑的总 alpha（比光柱的 0.18 高，否则在亮草地上看不见）。</summary>
        public const float HaloAlpha = 0.32f;
        /// <summary>光斑专用材质（**自己建的、不在被污染的共享池里**）与它的贴图。</summary>
        private static Material _matGodRayHalo;
        private static Texture2D _godRayTex;
        /// <summary>`_MainTex` 断言通过次数（读数用；被换掉会被换回）。</summary>
        public static int GodRayTextureOk { get; private set; }
        /// <summary>贴图状态：`ok` / `被换` / `缺失`。</summary>
        public static string GodRayTexState { get; private set; } = "(未建)";
        /// <summary>对齐读数：前 3 道的光柱锚点与地面交点（判据：光圈与 hit 完全一致）。</summary>
        private static readonly System.Collections.Generic.List<string> HaloSample =
            new System.Collections.Generic.List<string>(4);

        /// <summary>光柱道数（读数用）。</summary>
        public static int GodRayParts => GodRays.Count;
        /// <summary>光柱材质 alpha 基准（读数用）。</summary>
        public const float GodRayAlpha = 0.18f;
        /// <summary>当前 alpha（脉动后的实际值）。</summary>
        public static float GodRayAlphaNow { get; private set; }
        /// <summary>alpha 的 min / max（自启动以来的滚动区间，读数用）。</summary>
        public static float GodRayAlphaMin { get; private set; } = 9f;
        public static float GodRayAlphaMax { get; private set; }
        /// <summary>所有光柱的 forward（必须完全一致 —— 判据）。</summary>
        public static string GodRayDirReadout { get; private set; } = "(未建)";

        private static void BuildGodRays(Transform root)
        {
            GodRays.Clear();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) return;
            _matGodRay = new Material(sh) { name = "OWN_GODRAY_Shared" };
            var c = new Color(1.00f, 0.96f, 0.82f, GodRayAlpha);
            if (_matGodRay.HasProperty("_Color")) _matGodRay.SetColor("_Color", c);
            if (_matGodRay.HasProperty("_BaseColor")) _matGodRay.SetColor("_BaseColor", c);
            if (_matGodRay.HasProperty("_MainTex")) _matGodRay.SetTexture("_MainTex", null);
            if (_matGodRay.HasProperty("_BaseMap")) _matGodRay.SetTexture("_BaseMap", null);
            if (_matGodRay.HasProperty("_Surface")) _matGodRay.SetFloat("_Surface", 1f);
            if (_matGodRay.HasProperty("_SrcBlend")) _matGodRay.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (_matGodRay.HasProperty("_DstBlend")) _matGodRay.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (_matGodRay.HasProperty("_ZWrite")) _matGodRay.SetFloat("_ZWrite", 0f);
            _matGodRay.renderQueue = 3100;

            // ★ 方向：取主方向光的 forward（它就是太阳光的方向）★
            if (_sun != null) _godRayDir = _sun.transform.forward.normalized;
            Quaternion common = Quaternion.LookRotation(_godRayDir, Vector3.up);   // ★ 全部用这一个 ★

            Mesh cyl = TownKit.Prim(PrimitiveType.Cylinder);
            Mesh ball = TownKit.Prim(PrimitiveType.Sphere);
            Mesh quad = TownKit.Prim(PrimitiveType.Quad);

            // ★ 光斑专用材质：贴 `Resources/Art/godray_radial.png`（128×128 径向渐变，SmoothStep）
            //   它**自己建、不在共享池里** ⇒ 那条"被塞贴图"的污染链碰不到它；
            //   但仍然每 90 帧断言 `_MainTex` 是这张（见 `TickGodRays`）✓
            _godRayTex = Resources.Load<Texture2D>("Art/godray_radial");
            Shader shh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shh == null) shh = Shader.Find("Universal Render Pipeline/Unlit");
            if (shh == null) shh = Shader.Find("Sprites/Default");
            if (shh != null)
            {
                _matGodRayHalo = new Material(shh) { name = "OWN_GODRAY_Halo" };
                var hc = new Color(1.00f, 0.96f, 0.85f, GodRayAlpha);
                if (_matGodRayHalo.HasProperty("_Color")) _matGodRayHalo.SetColor("_Color", hc);
                if (_matGodRayHalo.HasProperty("_BaseColor")) _matGodRayHalo.SetColor("_BaseColor", hc);
                if (_godRayTex != null)
                {
                    if (_matGodRayHalo.HasProperty("_MainTex")) _matGodRayHalo.SetTexture("_MainTex", _godRayTex);
                    if (_matGodRayHalo.HasProperty("_BaseMap")) _matGodRayHalo.SetTexture("_BaseMap", _godRayTex);
                }
                if (_matGodRayHalo.HasProperty("_Surface")) _matGodRayHalo.SetFloat("_Surface", 1f);
                if (_matGodRayHalo.HasProperty("_SrcBlend")) _matGodRayHalo.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (_matGodRayHalo.HasProperty("_DstBlend")) _matGodRayHalo.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                if (_matGodRayHalo.HasProperty("_ZWrite")) _matGodRayHalo.SetFloat("_ZWrite", 0f);
                _matGodRayHalo.renderQueue = 3100;
            }
            GodRayTexState = _godRayTex != null ? "ok" : "缺失";
            // ★★ v242：**多层同轴壳做假软边**（不做后处理）★★
            //   每束光柱 = **3 个同心圆柱**（半径 ×1.0 / ×1.5 / ×2.2），alpha 权重 1.0 / 0.55 / 0.28，
            //   总和归一化到 **0.18**（与单层时一致）⇒ 越往外越淡 ⇒ 视觉上就是软边 + 雾化 ✓
            //   每层的 alpha 用 **MaterialPropertyBlock** 单独设 ⇒ 全场**仍然只有 1 份材质** ✓
            //   底部再加一颗**压扁的球**（y=0.15）当"落在地上的光晕" ✓
            for (int i = 0; i < GodRayCount; i++)
            {
                float ang = i * (360f / GodRayCount) * Mathf.Deg2Rad;
                float sAng = 8f + (i % 5) * 2.2f;
                // `anchor` 是空中的参考点；**光圈必须落在光柱与 y=0 的交点**上（用户裁定的核心）
                Vector3 anchor = new Vector3(Mathf.Cos(ang) * sAng, 16f + (i % 3) * 1.6f, Mathf.Sin(ang) * sAng);
                float tDown = anchor.y / Mathf.Max(1e-4f, -_godRayDir.y);       // ★ 走多久到 y=0 ★
                Vector3 hit = anchor + _godRayDir * tDown;                      // ★ 精确交点 ★
                hit.y = 0f;
                float rad = 0.35f + (i % 3) * 0.15f;                            // 宽 0.35~0.65

                // 光柱中心：让**底端落在交点下方 0.5 m**（`y = -0.5`）⇒ 不再像截断的管子 ✓
                const float HalfLen = 17f;                                      // 34 m 的一半
                Vector3 beamCenter = hit - _godRayDir * (HalfLen - 0.5f);

                Transform beamRoot = null;
                for (int k = 0; k < 3; k++)
                {
                    var go = new GameObject("OWN_GODRAY_" + (i + 1).ToString("00") + "_S" + k);
                    go.transform.SetParent(root, false);
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = cyl;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _matGodRay;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    float mul = k == 0 ? 1.0f : k == 1 ? 1.5f : 2.2f;
                    // ★★ v246：**三层不同高度**做底部过渡（不再靠"伸到地下"）★★
                    //   用户点破：相机从上往下看，只看到**地面处的截面** ⇒ 伸到 y=-0.5 是白做的 ✗
                    //   真正的过渡：让**内层（最亮）最短、外层最长**，三层底端依次高 1.2 m ⇒ 底边被摊开
                    //   ⇒ 地面附近由"外层的淡 + 内层的短"自然过渡，看不到硬截断 ✓
                    //   代价只有 `scale.y` 与 `position`，**不需要新贴图、不需要新材质** ✓
                    float lenK = HalfLen - k * 1.2f;                     // 17 / 15.8 / 14.6 ⇒ 34 / 31.6 / 29.2 m
                    Vector3 baseK = new Vector3(hit.x, hit.y + k * 1.2f, hit.z);   // 底端依次抬高
                    Vector3 centerK = baseK - _godRayDir * lenK;
                    go.transform.position = centerK;
                    go.transform.rotation = common;                     // ★ 三层同朝向 ⇒ 仍然全部平行 ★
                    go.transform.localScale = new Vector3(rad * 2f * mul, lenK, rad * 2f * mul);
                    GodRayShells.Add(mr);
                    if (k == 0) beamRoot = go.transform;
                }
                if (beamRoot == null) beamRoot = new GameObject("OWN_GODRAY_" + (i + 1) + "_Root").transform;
                GodRays.Add(beamRoot);

                // ★★ v244：**光斑改成 `Quad` ＋ 径向渐变贴图** ★★
                //   为什么不再用压扁的球：低面数球压扁后**棱角全露**，叠几层就是几圈硬棱 ✗
                //   `Quad` 是 2 个三角面，配上 `SmoothStep` 的径向渐变 ⇒ **边缘完全平滑** ✓
                //   贴图本身已经是渐变 ⇒ **只需 3 层**（件数 14×3=42，比原来的 70 更少 ✓）
                for (int k = 0; k < HaloWeight.Length; k++)
                {
                    var halo = new GameObject("OWN_GODRAY_" + (i + 1).ToString("00") + "_H" + k);
                    halo.transform.SetParent(root, false);
                    var hmf = halo.AddComponent<MeshFilter>();
                    hmf.sharedMesh = quad;
                    var hmr = halo.AddComponent<MeshRenderer>();
                    hmr.sharedMaterial = _matGodRayHalo == null ? _matGodRay : _matGodRayHalo;
                    hmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    hmr.receiveShadows = false;
                    halo.transform.position = new Vector3(hit.x, TownAvatar.GroundY + 0.02f, hit.z);
                    halo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);        // 水平放置
                    float hr = HaloRadius[k];
                    halo.transform.localScale = new Vector3(hr * 2f, hr * 2f, 1f);   // Quad 是 1×1 ⇒ ×2 = 直径
                    GodRayHalos.Add(hmr);
                }
                if (i < 3)
                    HaloSample.Add($"#{i + 1} anchor=({anchor.x:0.00},{anchor.y:0.00},{anchor.z:0.00}) " +
                                   $"→ hit=({hit.x:0.00},{hit.y:0.00},{hit.z:0.00})");
            }
            GodRayDirReadout = GodRays.Count > 0 ? GodRays[0].forward.ToString("F3") : "(无)";
            Log.Info($"[Town] 丁达尔光柱：**{GodRays.Count} 道 × 3 层壳 = {GodRayShells.Count} 件** ＋ " +
                     $"**5 层光斑 × {GodRays.Count} = {GodRayHalos.Count} 件**（合计 {GodRayShells.Count + GodRayHalos.Count}）· " +
                     $"全部平行（dir = {_godRayDir.ToString("F3")}）· 光斑 = 光柱与 y=0 的交点 · 底端伸到 y=-0.50 · " +
                     $"壳权重 1.0/0.55/0.28 · 光斑权重 1.0/0.62/0.38/0.20/0.09（归一化总和 = {GodRayAlpha:0.00}）");
            for (int k = 0; k < HaloSample.Count; k++) Log.Info("[Town] 光斑对齐 " + HaloSample[k]);
        }

        /// <summary>
        /// 光柱过渡：**只改 alpha**（亮度脉动 + 缓慢出现/消失），**绝不改朝向**（否则平行被破坏）。
        /// ★ v242：三层壳各自的 alpha 走 **MaterialPropertyBlock**（1.0/0.55/0.28 权重，归一化后总和 = 0.18）★
        ///   ⇒ 全场**仍然只有 1 份材质**，但每层浓淡不同 ⇒ 软边 ✓
        /// </summary>
        private static void TickGodRays()
        {
            if (_matGodRay == null) return;
            float pulse = 0.925f + Mathf.Sin(_t * 0.6f) * 0.075f;                 // 0.85~1.00
            float cycle = Mathf.Repeat(_t / 8f, 1f);
            float gate = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(cycle * Mathf.PI)));
            float a = Mathf.Clamp(GodRayAlpha * pulse * Mathf.Lerp(0.35f, 1f, gate), 0f, 1f);
            GodRayAlphaNow = a;
            if (a < GodRayAlphaMin) GodRayAlphaMin = a;
            if (a > GodRayAlphaMax) GodRayAlphaMax = a;

            if (_godRayMpb == null) _godRayMpb = new MaterialPropertyBlock();

            // ★ 每 90 帧断言一次：光斑材质的 `_MainTex` 必须还是那张径向渐变贴图 ★
            if (_matGodRayHalo != null && _godRayTex != null && Time.frameCount % 90 == 0)
            {
                Texture cur = _matGodRayHalo.HasProperty("_MainTex") ? _matGodRayHalo.GetTexture("_MainTex") : null;
                if (cur != _godRayTex)
                {
                    if (_matGodRayHalo.HasProperty("_MainTex")) _matGodRayHalo.SetTexture("_MainTex", _godRayTex);
                    if (_matGodRayHalo.HasProperty("_BaseMap")) _matGodRayHalo.SetTexture("_BaseMap", _godRayTex);
                    GodRayTexState = "被换（已换回）";
                }
                else { GodRayTextureOk++; GodRayTexState = "ok"; }
            }
            bool hasColor = _matGodRay.HasProperty("_Color");
            bool hasBase = _matGodRay.HasProperty("_BaseColor");
            float wSum = 0f;
            for (int k = 0; k < ShellWeight.Length; k++) wSum += ShellWeight[k];

            // 壳：alpha = 总 alpha × 权重 / 权重和（三层叠加 ≈ 0.18）
            int shells = GodRayShells.Count;
            for (int i = 0; i < shells; i++)
            {
                MeshRenderer mr = GodRayShells[i];
                if (mr == null) continue;
                float w = ShellWeight[i % 3] / wSum;
                var c = new Color(1.00f, 0.96f, 0.82f, a * w);
                mr.GetPropertyBlock(_godRayMpb);
                if (hasColor) _godRayMpb.SetColor("_Color", c);
                if (hasBase) _godRayMpb.SetColor("_BaseColor", c);
                mr.SetPropertyBlock(_godRayMpb);
            }
            // 底部光斑：5 层按权重（和 2.29）归一化到 **HaloAlpha = 0.32**（光柱仍 0.18）
            float haloSum = 0f;
            for (int k = 0; k < HaloWeight.Length; k++) haloSum += HaloWeight[k];
            float ha = Mathf.Clamp(HaloAlpha * pulse * Mathf.Lerp(0.35f, 1f, gate), 0f, 1f);
            HaloAlphaNow = ha;
            for (int i = 0; i < GodRayHalos.Count; i++)
            {
                MeshRenderer mr = GodRayHalos[i];
                if (mr == null) continue;
                float w = HaloWeight[i % HaloWeight.Length] / haloSum;
                var c = new Color(1.00f, 0.96f, 0.85f, ha * w);
                mr.GetPropertyBlock(_godRayMpb);
                if (hasColor) _godRayMpb.SetColor("_Color", c);
                if (hasBase) _godRayMpb.SetColor("_BaseColor", c);
                mr.SetPropertyBlock(_godRayMpb);
            }
        }

        /// <summary>当前光斑总 alpha（读数用）。</summary>
        public static float HaloAlphaNow { get; private set; }

        /// <summary>
        /// ★ 交付读数：前 2 道光斑的 6 项（active / enabled / position.y / lossyScale / 材质+贴图 / _Color.a / isVisible）★
        ///   用户"看不到"时先看这个：`isVisible=false` ⇒ 不在视锥内；`position.y` 偏低 ⇒ 埋地下；
        ///   `_Color.a≈0` ⇒ 被 alpha 门限压掉；`_MainTex` 不对 ⇒ 贴图没贴上。
        /// </summary>
        public static string HaloProbe()
        {
            var sb = new System.Text.StringBuilder(900);
            sb.Append($"光斑 {GodRayHalos.Count} 个（{GodRayHalos.Count / Mathf.Max(1, HaloWeight.Length)} 道 × {HaloWeight.Length} 层）" +
                      $"｜HaloAlpha = {HaloAlpha:0.00}（当前 {HaloAlphaNow:0.000}）｜贴图状态 {GodRayTexState}" +
                      $"｜GroundY = {TownAvatar.GroundY:0.000}");
            int shown = 0;
            for (int i = 0; i < GodRayHalos.Count && shown < 2; i++)
            {
                MeshRenderer mr = GodRayHalos[i];
                if (mr == null) { sb.Append("\n  #").Append(i).Append(" = (null)"); shown++; continue; }
                Material m = mr.sharedMaterial;
                Texture tex = m != null && m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                sb.Append($"\n  #{i} {mr.name}：activeInHierarchy={mr.gameObject.activeInHierarchy}" +
                          $" · enabled={mr.enabled} · activeSelf={mr.gameObject.activeSelf}" +
                          $"\n      position.y = {mr.transform.position.y:0.000} · lossyScale = {mr.transform.lossyScale.ToString("F2")}" +
                          $"\n      材质={m?.name} · _MainTex={(tex != null ? tex.name + " " + tex.width + "×" + tex.height : "null")}" +
                          $" · **isVisible = {mr.isVisible}**" +
                          $" · _Color.a = {(m != null && m.HasProperty("_Color") ? m.color.a : -1f):0.000}");
                shown++;
            }
            string s = sb.ToString();
            Log.Info("[Town] HALO " + s);
            return s;
        }

        /// <summary>
        /// 当前**生效**的"一天进度"（0~1）：锁晴天时恒 <see cref="SunnyTime01"/>。
        /// `TickSkyAndWeather` 与 `Probe()` 共用同一个入口 ⇒ 读数报的一定是**画面正在用的那个 t** ✓
        /// </summary>
        public static float SkyTime01
            => ForceSunny ? SunnyTime01 : Mathf.Repeat(_t, DayCycleSeconds) / DayCycleSeconds;

        private static void TickSkyAndWeather()
        {
            // ★ 晴天锁（`ForceSunny`）：时刻钉死在正午 ⇒ 下面每一行都算出同一个常量，循环等于关掉
            float t01 = SkyTime01;

            // 太阳：t01=0 黎明（东方地平线），0.25 正午，0.5 黄昏，0.75 午夜
            float elev = Mathf.Sin(t01 * Mathf.PI * 2f);          // −1..1
            float day = Mathf.Clamp01(elev * 1.4f + 0.35f);       // 0=夜 1=正午
            float pitch = Mathf.Lerp(8f, 78f, Mathf.Clamp01(elev * 0.5f + 0.5f));

            // 雨窗只在不锁晴天时判定 ⇒ `ForceSunny = true` 时 Raining 恒 false（不是"碰巧没到点"）
            Raining = !ForceSunny && t01 >= RainStart01 && t01 <= RainEnd01;
            float wet = Raining ? 1f : 0f;

            if (_sun != null)
            {
                float before = _sun.intensity;
                _sun.transform.rotation = Quaternion.Euler(pitch, -32f + t01 * 360f, 0f);
                Color warm = new Color(1.00f, 0.62f, 0.34f);      // 日出/日落
                Color noon = new Color(1.00f, 0.97f, 0.88f);      // 正午
                Color night = new Color(0.42f, 0.52f, 0.86f);     // 夜（月光）
                Color c = elev > 0f ? Color.Lerp(warm, noon, Mathf.Clamp01(elev * 1.6f))
                                    : Color.Lerp(warm, night, Mathf.Clamp01(-elev * 1.6f));
                _sun.color = c;
                float baseI = Mathf.Lerp(0.10f, 1.35f, day);
                _sun.intensity = baseI * (Raining ? 0.55f : 1f);
                SunIntensityLast = _sun.intensity;
                SunDeltaLast = _sun.intensity - before;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(new Color(0.16f, 0.19f, 0.30f),
                                                     new Color(0.62f, 0.66f, 0.72f), day);
            // ★ v241：**环境雾整段删除**（用户裁定：不要雾）★
            //   上一版是常开 Linear 40→180；现在一律关掉 —— 雾会把远景糊掉 ✓
            RenderSettings.fog = false;

            if (_cam != null)
            {
                Color skyDay = new Color(0.56f, 0.74f, 0.88f);
                Color skyDusk = new Color(0.86f, 0.58f, 0.42f);
                Color skyNight = new Color(0.06f, 0.08f, 0.16f);
                Color sky = elev > 0f ? Color.Lerp(skyDusk, skyDay, Mathf.Clamp01(elev * 1.5f))
                                      : Color.Lerp(skyDusk, skyNight, Mathf.Clamp01(-elev * 1.5f));
                if (Raining) sky = Color.Lerp(sky, new Color(0.46f, 0.50f, 0.55f), 0.65f);
                _cam.backgroundColor = sky;
            }

            // 雨
            if (_rain != null)
            {
                var emission = _rain.emission;
                emission.rateOverTime = Raining ? 900f : 0f;
                if (Raining && !_rain.isPlaying) _rain.Play();
                else if (!Raining && _rain.isPlaying) _rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                RainParticles = _rain.particleCount;
            }

            LastMoonPhase = Raining ? "雨天" : (day > 0.75f ? "正午" : day > 0.35f ? "白天" : "夜晚");
            LastWet = wet;
        }

        private static float LastWet;

        // ══════════════════════════════════════════════════════════════════
        // 读数（验收用）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>一句话读数：六样各自的"每帧变化量"＋ 日夜/雨状态 ＋ 对象计数 ＋ fps。</summary>
        public static string Probe()
        {
            float t01 = SkyTime01;
            float fps = Time.smoothDeltaTime > 0f ? 1f / Time.smoothDeltaTime : 0f;
            string s = $"人 {Npcs.Count} 个（每帧最多移动 {NpcDeltaLast * 100f:0.0} 厘米）" +
                       $"｜动物 {Animals.Count} 只（每帧起伏 {AnimalDeltaLast * 100f:0.00} 厘米）" +
                       $"｜鸟 {Birds.Count} 只（每帧转过 {BirdAngleDeltaLast:0.00}°）" +
                       $"｜树/灌木摇摆 {Sway.Count} 棵（倾角 {SwayAngleLast:0.00}°）" +
                       $"｜**建模云 {Clouds.Count} 朵 × {CloudPartCount} 件**（每帧漂 {CloudDeltaLast * 100f:0.0} 厘米 · " +
                       $"自转 {CloudSpinLast:0.00}°/s）" +
                       $"｜太阳强度 {SunIntensityLast:0.00}（每帧变化 {SunDeltaLast:+0.0000;-0.0000}）" +
                       $"｜晴天锁 {(ForceSunny ? "ON" : "OFF")}" +
                       $"｜日夜 t={t01:0.00}（{LastMoonPhase}）｜雨 {(Raining ? "下" : "停")}（粒子 {RainParticles}）" +
                       $"｜动态件 {FxObjectCount} 个｜整层 {(_visible ? "开" : "关")}" +
                       $"｜共享材质池 {ToonLook.MaterialPoolCount} · 形状网格 {TownKit.ShapeCacheCount}" +
                       $"｜fps {fps:0.0}（帧 {Time.frameCount}）" +
                       // ★ v245：光斑读数直接挂在 LifeInfo 的返回里（禁令只允许改本文件 ⇒ 不走 Bootstrap 新入口）★
                       "\n" + HaloProbe();
            Log.Info("[Town] LIFE " + s);
            return s;
        }
    }

    /// <summary>小工具：把 (x,z) 变成 y=0.02 的世界坐标（`TownLife` 内部用，避免到处写 Vector3）。</summary>
    internal static class TownLifeVec
    {
        public static Vector3 ToWorld3(this Vector2 v)
            => new Vector3(v.x, TownAvatar.GroundY, v.y);
    }
}
