using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 三种武器攻击的**附加**特效层：刀光 / 水柱 / 冲击环 + 各自的粒子 ★
    ///
    /// ── 这一层为什么存在 ─────────────────────────────────────────────
    /// `WorldView.OnMeleeSwing` / `OnBeam` / `OnFertBlast` 已经把"本体形状"画出来了
    /// （刃弧、方块拼的水柱、蘑菇云）。本文件**不替换**它们，只在同一时刻**加一层**
    /// 更小、更碎、寿命更短的颗粒语言上去 —— 用户需求原话："增加各种攻击特效，增加粒子特效" ✓
    /// 两层同时存在是**有意**的：本体负责"这是哪把武器、打到多远"，
    /// 本层负责"这一下打实了没有"（溅开的水、崩起的土、扫过的刀光）✓
    ///
    /// ── 三条硬约束（交付要求）──────────────────────────────────────
    /// ① **不碰调用点**：全部靠 `Bus.Subscribe` 订阅**已经存在、且确实会被发布**的事件。
    ///    三个事件与它们在 **2026-11-05 本次落地时**的真实发布点（`RunSession.cs` 正被别的
    ///    改动并行编辑，行号会漂 ⇒ **以"类型名 + 方法名"为准**，行号只作定位参考）：
    ///      · `MeleeSwingEvent`（定义 `RunSession.cs:66`）
    ///          ← `RunSession.DoHoeSwing`      **RunSession.cs:4885**（每一刀都发）
    ///          ← `RunSession.DebugSwing`      **RunSession.cs:2774**（调试通道，同一份几何）
    ///      · `BeamEvent`（定义 `Actors.cs:1209`）
    ///          ← `RunSession.FireBeam`        **RunSession.cs:5433**（水壶激光，按住时每固定帧一次）
    ///          另有"锄头剑气 / 插件连锁"两个来源（见 `Actors.cs:1209` 的注释）与
    ///          `DebugApi` / `ProbeV200` 的投递 —— 本层对它们一视同仁：
    ///          都是一根细长亮条，这正是它们该有的样子 ✓
    ///      · `FertilizerBlastEvent`（定义 `Actors.cs:1162`）
    ///          ← `RunSession.ExplodeAt`      **RunSession.cs:5749**（主爆）/ **:5732**（子爆）
    ///          ← `Projectile.Explode`        **Actors.cs:726**（投射物自爆，吃的是基础外观）
    ///    ★ 没有用**轮询**「`PlayerActor` 本帧是否处于挥击窗口」的兜底通道 ★
    ///      理由：`MeleeSwingEvent` 是**活的**（上面两个发布点都在，且 `GameAudio.cs:362`、
    ///      `WorldView.cs:312` 都在订阅它 ⇒ 它不是 `ExpOrbDroppedEvent` 那种死事件）⇒ 有事件就不该轮询。
    ///      轮询还会多出两条隐性依赖：① "帧序号 / 挥击窗口边界"必须与 `RunSession` 的固定步长对齐，
    ///      而特效跑在渲染帧上；② 双击的第二刀是**两次事件**，轮询窗口却可能连续为真
    ///      ⇒ 会与事件路径**重复计数**，同一刀画出两套刀光 ✗
    ///
    /// ② **自托管**：照 `CombatFeedbackFxHost`（`Flow/CombatFeedback.Post.cs:243`）的做法，
    ///    用 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 自建 `DontDestroyOnLoad` 宿主
    ///    ⇒ 不依赖场景里有没有人挂它、不动任何场景资源 ✓
    ///
    /// ③ **零资产**：没有 `.png`、没有 `.mat`、没有 `ParticleSystem`、没有新 Mesh。
    ///    网格是 Unity 内置 `PrimitiveType.Cube` / `PrimitiveType.Sphere`（启动时各取一份共享 mesh），
    ///    材质由 `Shader.Find` 到的 URP 粒子 shader（逐级兜底）在**运行期**建，
    ///    颜色走 `MaterialPropertyBlock` ⇒ 256 个池化渲染体共用 **4 份材质**（1 份共享 +
    ///    3 份"刀光 UV 段"轮换，见第五版说明）+ **2 份网格**，不产生任何磁盘资产 ✓
    ///    ★ 唯一一张贴图（刀光刃面 **256×32**，第五版从 128×16 加密并叠了三层纹理）也是
    ///      **代码生成**的 `Texture2D`，带 `HideFlags.DontSave` ⇒ 同样不进资产库、不写盘 ✓
    ///    粒子系统是**自己写的**（位置 / 速度 / 重力 / 寿命 / 缩放 / alpha 六个量），
    ///    见 `Step()` —— 不用 `ParticleSystem` 的理由是它参数太多、无法在无头环境里逐项验证 ✓
    ///
    /// ── 为什么是**方块**而不是 Quad ───────────────────────────────
    /// Quad 有背面剔除：从 60° 俯视的相机看，一张没有朝向相机的 Quad 会**整个消失**，
    /// 而"消失"在截图验收里会被读成"没做出来"✗ 压扁的方块（Y 很薄）从任何角度都在，
    /// 而且与工程既有的颗粒语言一致（`WorldView` 的碎片 / 蘑菇云全部是方块）✓
    ///
    /// ── 本轮（第三版 · 用户两条新要求）改的两件事 ──────────────────
    ///   ① 锄头刀光：**"大扇形块" → "窄刀光弧"**（用户原话"大扇形还是突兀了，改纹理和形状"）
    ///      · 张角收窄到整弧的 60%（`ArcCover`）、以瞄准方向为中心
    ///      · 径向从 0.42R→R 的实心面，改成 0.78R→R 的**薄带**，切向长度按 `sin(πu)` 做成月牙
    ///      · 贴图 64×8 的"斑马条纹" → 64×16 的**刃面反光**（2 道细锐高光 + 上下各 25% 羽化）
    ///        ★ 第四版 → **128×16**，两道高光各加一条 1px 锐边（磨刃），羽化位置不变
    ///      · 18 片、寿命 0.15s，且**逐片 0.06s 扫过淡出**（不再所有片同时收尾）
    ///        ★ 第四版 → **26 片** + 26 片内缘亮线；弦长精确、厚度随 u ±25%、淡出改 EaseOut
    ///
    /// ── 第四版（本轮）只提精度、**不动覆盖范围** ──────────────────
    ///   硬约束：外径不变（R）、张角不变（0.60 × 半弧）、颜色仍从 `fx.Core` / `fx.Rim` 派生。
    ///   五处提升：① 26 片 ② 弦长 `2·sin(Δ/2)` ③ 厚度随 u ±25% ④ 内缘亮线 ⑤ 贴图 128 + 1px 锐边
    ///   外加：逐片扫过的淡出从 `1−k²` 改成 EaseOut `1−k³`（只作用于刀光，其余特效的曲线**没动**）
    ///   ② 水壶水柱：**删掉命中端法阵**（用户原话"取消末端的方形效果，只保留射线"）
    ///      · `SpawnCircle` / `ModeCircle` / `_circlePiece` / `CircleTexture` / `CirclesSpawned` 全部移除
    ///      · 射线从"一根等宽亮条"改成**3 段渐细渐淡**（起点 1.0× → 末端 0.55×）
    ///      · 像素水花保留，散发方向仍以命中端为中心（它本就不依赖法阵）
    ///   两路都仍然只走池：**运行期 0 次 `Instantiate` / `Destroy`** ✓
    ///
    /// ── 第五版（本轮）"看得出纹理"：只改贴图内容与 UV 分配，**不动几何** ────
    ///   用户原话：「再次优化锄头攻击特效，增加纹理」⇒ 重点不是再加片，而是**贴图里真的有东西**。
    ///   硬约束全部保持：外径 `BladeR1`=1.00、张角 `ArcCover`=0.60、片数 26、寿命 `ArcLife`=0.15
    ///   （`RunSession.cs` 里 `MeleeFxArcCover / MeleeFxSliceCount / MeleeFxBladeR1 /
    ///    MeleeFxSliceDepthRatio` 是它们的镜像常量，动这里就等于把"判定包住特效"打破 ✗）。
    ///
    /// ── ★ v273：锄头「初始攻击范围 / 特效范围」按 **0.90** 同步缩小 ★ ──
    ///   用户原话：「按比例略微同步缩小锄头初始攻击范围和特效范围」。
    ///   **0.90 只落一处**：`tuning.json` 的 `tools.hoe.range` **3.8 → 3.42**
    ///   （= `RunSession` 判定侧与 `WorldView` / 本文件视觉侧的**共同上游**）。
    ///   本文件的半径**不是绝对值**：`SpawnSwing` 第 715 行 `float range = Mathf.Max(0.8f, e.Range)`，
    ///   而 `e.Range` = `RunSession.MeleeFxRadius(range)`（4.090 → **3.729m**，已吃到那一缩小）
    ///   ⇒ `BladeR0 / BladeR1 / bandD` 这些**比例**参数**不许**再乘 0.90 ✗
    ///   再乘一次的后果（算过）：
    ///     · 26 片的外沿 = (0.90 + 0.045)R = **0.945R**，缩进 `WorldView.FxBladeMesh` 的主弧（1.00R）**里面**
    ///     · 「判定 / 最宽视觉」从 **1.150 掉到 1.087**（判定 4.051m vs 主弧 3.729m）⇒ 用户要的"包住"变薄
    ///   ⇒ 视觉与判定两棵树都只从 `range` 长出来，天生同步（`RunSession` 那边有对应的「v273 镜像纪律」）✓
    ///   ① 贴图 **128×16 → 256×32**，同一张图里叠**三层可辨纹理**（全白，只靠 α/亮度表达）：
    ///      · 层 A **刃面高光**：2 道主高光 + 1 道**很淡的第三道**（强度 0.12），各带 1px 锐边（旧做法保留）
    ///      · 层 B **磨痕**：26 条极细随机短线（长 5~18px、α 0.10~0.22），位置由**固定种子哈希**决定
    ///        ⇒ 每次生成完全一样（不是 `Random`，不吃运行期状态）
    ///      · 层 C **崩刃/缺口**：3 处不规则小缺口，从 **v 高的一侧（= 外缘）**咬进去，`α` 直接归 0
    ///        ⇒ 刃口不完美，这是"看起来有纹理"最有效的一笔
    ///   ② **UV 改成按片分段**：贴图横向切 **3 段**，26 片按 `i % 3` 分给 3 份材质，
    ///      每份材质的 `_MainTex/_BaseMap` 各自 `SetTextureOffset` 到自己的段起点 ⇒ 相邻两片
    ///      看到的是贴图的**不同一段**，磨痕/缺口**逐片不同**，肉眼能看到"纹理在变" ✓
    ///   ③ 颜色仍全部从 `fx.Core` / `fx.Rim` 派生（贴图存的是**白灰明度**，染色交给
    ///      `MaterialPropertyBlock`），没有写死任何 RGB ✓
    ///   ★ 为什么是 **3 份材质**而不是逐片设 UV ★
    ///     `MaterialPropertyBlock` 只对"被逐实例覆盖的属性"生效；`_MainTex_ST` / `_BaseMap_ST`
    ///     在 URP 的 SRP Batcher 下是**从材质 CBUFFER 读**的，塞进 MPB 不保证生效（有版本差异）。
    ///     给 26 片各建一份材质则要 26 份材质常驻，且每片都要 `new Material` ⇒ 直接破"零实例化/池化共享"✗
    ///     ⇒ 折中：**3 份材质轮换**（`i % 3`），池还是那 64 个渲染体、材质对象固定 3 个、
    ///      `sharedMaterial` 只在 `SpawnSwing` 里换指针（不 new、不 Destroy）✓
    /// </summary>
    public static class AttackVfx
    {
        // ══════════════════════════════════════════════════════════════
        // 池：**每类 64**、环形复用（下一个拿到的永远是"最老的那个"）
        // 四类 × 64 = 256 个渲染体 ≤ 交付要求的同屏 320 上限 ✓
        // ★ 第三版：法阵（原来的第 257 个独占渲染体）已按用户要求删除
        //   ⇒ 渲染体总数从 257 降到 **256**，仍不 `Instantiate` / `Destroy` ✓
        // ══════════════════════════════════════════════════════════════
        public const int PoolPerKind = 64;
        /// <summary>★ v304：**香蕉皮**专用池（每颗皮只占 1 个渲染体，自带程序化网格）。
        /// 单开一个更小的池而不是把 5 类都撑到 64：渲染体总数是硬约束（≤ 320），
        /// 而香蕉皮同屏最多 8 个（掉落节流 + 每颗 ~1.0s 寿命）⇒ 8 格足够、不浪费 ✓</summary>
        public const int PeelPool = 8;
        public const int PoolKindCount = 5;
        public const int MaxRenderers = PoolPerKind * 4 + PeelPool;    // 264 ≤ 320

        private enum PoolKind { Slice = 0, Bar = 1, Bit = 2, Mist = 3, Peel = 4 }
        private const int ModeBallistic = 0, ModeRadial = 1, ModeAxis = 2, ModeMist = 3;
        // ★ v304 新增两档（都只被新写的 `Spawn*` 使用 ⇒ 旧调用点一个都碰不到）★
        /// <summary>**浪墙**：沿 `Angle` 方向推进 `R1` 米，侧向偏移 `R0`，前凸 `Depth`，抬升 `Lift`/`S1`。</summary>
        private const int ModeWave = 4;
        /// <summary>**天降香蕉皮**：从 `R1` 米高处落到 `End`，`R0` = 下落秒数，落地后停在原地。</summary>
        private const int ModePeel = 5;
        /// <summary>★ v308：**螺旋吸入**（黑洞）：绕 `Origin` 从 `R0` 螺旋收进 `R1`，同时从
        /// `SpiralY0` 升到 `SpiralY1`；进度用 `k²` ⇒ 越靠近越快（"被吸进去"）。</summary>
        private const int ModeSpiral = 6;
        /// <summary>★ v310：**旋转漩涡**（黑洞 1 秒前摇的主视觉）—— 绕 `Origin` 以**固定半径缓慢内收**
        /// 并持续旋转，转角用 `k²` ⇒ **越转越快**；`S0` = 沿切向的条长、`Thick`/`Depth` = 另两轴。
        /// 与 `ModeSpiral`（螺旋吸入，半径按 `k²` 收）的区别只有两条：
        ///   ① 半径按 `k` 线性收（臂**一直看得见**，不是"末段才猛地收进去"）
        ///   ② 不用 `R1 = 0.08R` 那种收口（它是"漩涡的臂"，不是"被吸走的碎屑"）
        /// 默认 0 / 只有 `ModeVortex` 读这些字段 ⇒ 对其余全部模式恒等 ✓</summary>
        private const int ModeVortex = 7;

        /// <summary>
        /// ★ v314：**天使羽毛飘落**（道具「天使降临」复活那 2 秒的唯一表现）。
        ///
        /// 位置同样是**寿命的解析函数**（与 `ModePeel` / `ModeSpiral` 同一条纪律：
        /// 不做速度积分 ⇒ 与 `Time.deltaTime` 抖动无关、天然可复现）：
        ///   · 下落：`End.y` 起往上 `R1` 米，按 `u = Age/R0` **匀速**落到地面（羽毛靠空气阻力，近乎匀速）
        ///   · 摇摆：X / Z 各一条**不同频率**的正弦，振幅随下落变大（`0.45 → 1.00`）⇒ 越落越飘 ✓
        ///   · 自转：`Rot0 + Spin·Age` 缓慢翻转 ⇒ 羽片正面/侧面交替朝镜头 ✓
        ///   · 形状：`S0 × Thick × S1` = 一片**细长**的方片（宽 0.075 / 厚 0.012 / 长 0.42 米）
        /// 字段口径：`End` 落点 · `R0` 下落秒数 · `R1` 起始高度 · `Angle` 相位 · `Chord/Depth` 摇摆幅度 ·
        ///           `S0/S1/Thick` 三个尺寸 · `Rot0/Spin` 自转。
        /// ★ 只有本模式读 `Realtime`（见 `Piece.Realtime`）⇒ 其余模式逐位不变 ✓
        /// </summary>
        private const int ModeFeather = 8;

        /// <summary>一个池化渲染体。字段全公开给本类的静态函数用（私有嵌套类，不泄漏到外面）。</summary>
        private sealed class Piece
        {
            public Transform T;
            public MeshRenderer Mr;
            public bool Active;
            /// <summary>负值 = "还没轮到它"（刀光的逐片扫过用它错峰）。
            /// `Step` 对 `Age &lt; 0` 的片输出 **alpha 0** ⇒ 没被扫到的片不提前亮起来 ✓</summary>
            public float Age, Life;
            /// <summary>★ v279：出生时刻（`Time.unscaledTime`）。**墙钟**寿命兜底的唯一依据 ——
            /// `Age` 只在 `Tick` 里长，而 `Tick` 在 `dt <= 0` 的帧不积分 ⇒ 只看 `Age` 的实现
            /// 会在顿帧/暂停期间把这块粒子**一直留在屏幕上**。它不参与任何动画 ✓</summary>
            public float SpawnRealtime;
            public int Mode;
            public Color Col;
            public float A0;

            // ModeRadial：以 Origin 为圆心、Angle 方向、半径 R0→R1 张开
            // （高度直接取 `Origin.y` —— 不要再单独存一个 Y：多一个字段就多一处"忘了赋值、
            //   特效贴在地面上"的坑，这个坑在本文件第一版真的踩过一次 ✗）
            public Vector3 Origin;
            public float Angle;
            public float R0, R1;
            public float Chord;      // X 尺寸 = 当前半径 × Chord（沿弧的弦长系数）
            public float Thick;      // Y 尺寸
            public float Depth;      // Z 尺寸
            public float YawDeg;

            // ModeBallistic：抛物线
            public Vector3 Pos;
            public Vector3 Vel;
            public float Gravity;
            public float S0, S1;
            public Vector3 Rot0;
            public Vector3 Spin;

            // ModeAxis：两点之间的一根细条
            public Vector3 End;

            // ── 本轮新增 ────────────────────────────────────────────
            /// <summary>沿半径的两端色。只有 `Grad = true` 的刀光片才读它 ⇒
            /// **默认 `false`**，这样任何"忘了设 `ColB`"的旧调用点都不会被拉成黑色（这是这套字段唯一的安全用法）。</summary>
            public Color ColB;
            public bool Grad;
            /// <summary>
            /// ★ 第四版：这一片属于"刀光扫过"序列 ⇒ 淡出走 **EaseOut**（`1 − k³`）
            /// 而不是全类共用的 `1 − k²`。
            /// 为什么要一个独立开关而不是直接改全局曲线：`1 − k²` 还服务冲击环 / 土粒 / 烟雾，
            /// 动它就是"顺手改了另外三种特效"✗ 这个标记只由 `SpawnSwing` 打开 ⇒ 其余一处不变 ✓
            /// </summary>
            public bool Sweep;
            /// <summary>
            /// ★ v306：淡出走 **"先咬住、末段整体收掉"**（`1 − k⁴`），而不是一路衰减。
            /// 只给**巨型的整圈环带 / 内外描边 / 地面投影环**用 —— 它们要在 0.30s 里
            /// 始终读作"这一圈已经定了"，而不是像刀光那样一出来就开始暗。
            /// **默认 `false`** ⇒ `1 − k²`（旧全局）与 `1 − k³`（`Sweep`）两条曲线逐字不变 ✓
            /// ⚠️ 池化字段：`Take` 必须清（与 `Lift` 同一条纪律，漏了就"上一个使用者留下旧值"✗）
            /// </summary>
            public bool Hold;

            // ModeMist（立体烟雾）：一"团"由同一个 Pos/Drift/Rise 驱动，各球只差 Orbit 偏移
            public Vector3 Drift;        // 水平漂移速度 m/s
            public float Rise;           // 向上浮速度 m/s（随时间衰减）
            public float OrbitRad, OrbitAng, OrbitSpeed;   // 球在团内的偏移：半径 / 相位 / 角速度
            public float YOff;           // 团内垂直错位
            public float Gray0, Gray1;   // 灰度 0.25 → 0.55

            /// <summary>
            /// ★ v304：**竖直抬升**（米）。**默认 0**，且只有 `ModeRadial` / `ModeWave` 读它
            /// ⇒ 对全部既有调用点（刀光片 / 冲击环 / 沼绿水环 / 内收暗环）是**恒等变换** ✓
            /// 两个用途：浪墙的**白沫**（同一道浪抬高到浪顶）、浪墙的**浪峰起伏**。
            /// </summary>
            public float Lift;

            /// <summary>
            /// ★ v308：**`ModeSpiral`（黑洞的螺旋吸入）专用**：总转角（弧度）与起止高度（米，
            /// 相对 `Origin.y`）。**默认 0** 且只有 `ModeSpiral` 读它们 ⇒ 对其余全部模式恒等 ✓
            /// </summary>
            public float SpiralTurns;
            public float SpiralY0, SpiralY1;

            /// <summary>
            /// ★ v307：**这一发要用哪张网格**（null = 用本池的默认网格 `BaseMesh`）。
            ///
            /// 为什么需要它：水系圆盘**必须换形状**（贴地扁圆），而池是按"类"预建渲染体的 ——
            /// 不能为一个新形状再开一个池（渲染体总数是硬约束，见 `MaxRenderers`）。
            /// ⇒ 网格**共享**（一份 Mesh 被多个渲染体引用，不 Instantiate），只在**换形的那一刻**
            ///   改一个引用 ⇒ 零新增资产、零运行期分配 ✓
            /// ★ v308：剑刃 / 刃口线已删 ⇒ 现在**唯一**的使用者是贴地扁圆 `_discMesh` ★
            /// ⚠️ 池化字段：`Take` 必须清回 null。
            /// </summary>
            public Mesh UseMesh;
            /// <summary>★ v307：本池的**默认网格**（建池时记下）。`UseMesh` 为 null 时 `Activate` 回落到它。</summary>
            public Mesh BaseMesh;
            /// <summary>★ v307：网格挂在 `MeshFilter` 上（`MeshRenderer` 没有 `sharedMesh`）——
            /// 建池时存好，换形时直接改引用，不用每条路径去 `GetComponent` ✓</summary>
            public MeshFilter Mf;

            /// <summary>
            /// ★★ v314：**这一片按"墙钟"推进，而不是按 `Time.deltaTime`** ★★
            ///
            /// ── 为什么需要它（复活羽毛的**全部意义**就在这一条）──
            ///   复活时会停帧 2 秒（`timeScale = 0.05`），而 `Tick` 收的是 `Time.deltaTime`
            ///   ⇒ 那 2 秒里粒子只前进 **0.1 秒**的"粒子时间"，羽毛几乎不动，
            ///   然后被墙钟兜底（`MaxRealtimeLife` 2.5s）**整个抹掉** ✗
            ///   而用户要的正是「时间停住、羽毛**照样飘落**」—— 时间停住 ⇔ 羽毛必须走墙钟 ✓
            /// ⇒ 打开本标记的片子在 `Tick` 里改用 `Time.unscaledDeltaTime`（同样的 0.1s 上限钳制）。
            ///
            /// ⚠️ 池化字段：`Take` 必须清回 `false` —— 不清的话，下一位借到这个格子的
            ///    刀光片/土粒会在**其余特效都慢下来的顿帧里照常快进**（"顺手改了另外五种特效"）✗
            /// </summary>
            public bool Realtime;
        }

        private sealed class Pool
        {
            public Piece[] Items;
            public int Cursor;
        }

        /// <summary>★ v304：**每一类各自的格数**（原来的池是"4 × 64"，现在多了 Peee 池）。
        /// `Take` / `EnsureBuilt` 一律走这里，不再假设"所有池一样大" ✓</summary>
        private static int SizeOf(PoolKind k) => k == PoolKind.Peel ? PeelPool : PoolPerKind;

        private static readonly Pool[] Pools = new Pool[PoolKindCount];
        private static Transform _root;
        private static Material _mat;          // 无贴图：冲击环 / 水柱 / 火星 / 土粒 / 像素水花
        /// <summary>
        /// ★ 第五版：刀光材质从 1 份变成 **3 份**（`BladeTexSections` 份）★
        /// 每份的 `_MainTex/_BaseMap` 都指同一张贴图，但 `SetTextureOffset` 到**不同的横向段**
        /// ⇒ 26 片按 `i % 3` 轮流用，相邻片就看到贴图的不同一段（磨痕/缺口逐片不同）。
        /// 为什么不是逐片 UV：见文件头"为什么是 3 份材质"。池里的渲染体数量**没变** ✓
        /// </summary>
        private static Material[] _bladeMats;
        private static Mesh _mesh;             // Cube
        private static Mesh _sphereMesh;       // Sphere（立体烟雾）
        /// <summary>★ v304：**香蕉皮**的程序化网格（三瓣 + 翘尖，见 `BuildPeelMesh`）。
        /// 整个会话只有这一份，池里 8 个渲染体共享它 ⇒ 仍然"零美术资产" ✓</summary>
        private static Mesh _peelMesh;
        /// <summary>★ v307：**贴地的扁圆**网格（污水落地处那一圈深绿渍）。
        /// 为什么不能用球网格均匀缩放：`ModeBallistic` 的缩放是**等比**的，
        /// 球等比缩放永远是球 ⇒ 想要"一张贴地的扁圆"就必须**生来就是扁的** ✓</summary>
        private static Mesh _discMesh;
        /// <summary>★ v308：**护壤的六边形护盾面片**（一片共享的六边形，双面）。
        /// 与香蕉皮/扁圆同一条纪律：代码生成、`HideFlags.DontSave`、不写盘 ⇒ 零美术资产 ✓
        /// 池里的渲染体共享这一份（`Piece.UseMesh` 只改引用）⇒ 运行期不建 Mesh ✓</summary>
        private static Mesh _hexMesh;
        private static MaterialPropertyBlock _mpb;
        private static bool _ready;
        private static bool _hasColor, _hasBaseColor;
        private static bool _hasMainTex, _hasBaseMap;
        private static int _colId, _baseColId;
        /// <summary>刀光贴图 UV 沿 X 的流动相位 0..1（三份刀光材质共用这个相位）。</summary>
        private static float _bladeFlow;

        /// <summary>贴图**只生成一次**的静态缓存（跨 play 会话保留，带 `HideFlags.DontSave`）。</summary>
        private static Texture2D _bladeTex;

        /// <summary>当前活着的渲染体（读数用；`Tick` 里重算）。</summary>
        public static int LiveRenderers { get; private set; }
        /// <summary>本 play 会话累计：刀光次数 / 水柱条数 / 冲击环次数（读数用）。</summary>
        public static int Swings, Beams, Blasts, SplashBursts, SparksSpawned, DirtSpawned;
        /// <summary>像素水花 / 烟雾团（读数用）。</summary>
        public static int PixelsSpawned, SmokeSpawned;
        /// <summary>★ v304：浪墙道数 / 香蕉皮个数（读数用）。</summary>
        public static int WavesSpawned, PeelsSpawned;
        /// <summary>★ v308：黑洞前摇（三层：贴地暗环 + 螺线 + 中心暗球）触发次数（读数用）。</summary>
        public static int BlackHoles;
        // ★ v311：**"同一帧只画一套漩涡"的去重字段已删除**（原 `_lastCollapseFrame`）。
        //   它就是用户报的「黑洞搭配连环只有末端可以触发黑洞」两个成因之一（另一个是倒序遍历）。
        //   现在每个落点各画各的，池账靠 `SpawnCollapseRing` 的 `spots` 分档压住
        //   （3 套 = 57 ≤ 64）；"按落点去重"的职责交回 `RunSession.TickPendingBlasts` 那条
        //   "跨过 `BlackHoleWindup`"的判定 —— 它对每颗 `PendingBlast` **恰好成立一次** ✓
        /// <summary>★ v308：护壤的六边形护盾触发次数（读数用）。</summary>
        public static int WardShields;
        /// <summary>★ v307：感电的电光球个数 / 弹射落点涟漪圈数（读数用）。</summary>
        public static int OrbsSpawned, RipplesSpawned;
        /// <summary>★ v309：本轮的五个新件（读数用；全部只增不清，`ResetForNewSession` 归零）。
        /// 水圈 = 弹射落点的水圈圈数 · 白闪 = 刀口白闪段数 · 二次环 = 爆炸的第二圈 ·
        /// 灼印 = 焦橙脉冲环个数 · 血尾 = 吸血拖尾点个数 · 雷光 = 伏雷地面脉冲个数。</summary>
        public static int WaterRings, BladeFlashes, EchoRings, MarkPulses, BloodTrailDots, MinePulses;
        /// <summary>★ v310（读数用）：巨型的**刃痕片数** / 灼烧**余烬颗数** / 感电**电弧段数**。
        /// 三个都是"只增不清"，`ResetForNewSession` 归零 —— 用来验证门控：
        /// 没装 `hoe_burn` 时 `BurnEmbers` 必须**一次都不涨**，没装 `hoe_shock` 时 `ShockBolts` 同理 ✓</summary>
        public static int GiantBlades, BurnEmbers, ShockBolts;
        /// <summary>★ v312（读数用）：潮汐推开反馈的**触发次数**（一次 = 一个敌人被推 = 1 弧 + 1 拖尾）。
        /// 门控证据：没装 `can_tide` 时 `PluginExtra2.StartTidePush` 一次都不会被调 ⇒ 这个数必须恒 0 ✓</summary>
        public static int TideArcs;
        /// <summary>
        /// ★ v314（读数用）：「天使降临」复活时飘落的**羽毛片数**（只增不清，`ResetForNewSession` 归零）。
        /// 门控证据：没拿到这件道具时它必须**恒 0** —— 羽毛只有 `RunSession.TryAngelRevive` 一个发布点 ✓
        /// </summary>
        public static int AngelFeathers;
        /// <summary>所用 shader 名（`Shader.Find` 的兜底链走到哪一级）。</summary>
        public static string ShaderName = "(未建立)";

        // ── ① 锄头刀光（第三版：窄刀光弧）────────────────────────────
        private const float ArcLife = 0.15f;      // 刀光总时长：0.18 → **0.15**（更利落）
        private const float SparkLife = 0.35f;    // 火星：0.35s 消失，重力 12
        private const float SparkGravity = 12f;
        private const float BarLife = 0.10f;      // 水柱：0.10s
        private const float RingLife = 0.25f;     // 冲击环：0.25s
        // ── ★ v309：爆炸的**二次环**（冲击波扩散）──
        /// <summary>二次环出现在主环之后多少秒（用户给的 0.12）。用**负 `Age`** 实现，不需要任何计时器。</summary>
        private const float EchoRingDelay = 0.12f;
        /// <summary>二次环张开到的半径 = `R × 本值`（1.18 ⇒ 比主环大 18%，读作"推出去的第二道"）。</summary>
        private const float EchoRingScale = 1.18f;
        /// <summary>二次环的不透明度（0.52；主环是 0.90 ⇒ 更淡）。</summary>
        private const float EchoRingAlpha = 0.52f;
        private const float DirtLife = 0.60f;     // 土粒：0.60s
        private const float DirtGravity = 14f;

        // ── ★ v314：「天使降临」复活时的**羽毛飘落**（`ModeFeather`）──
        /// <summary>羽毛片数（用户口径 6~10 片；取 8）。同一帧全部出生 ⇒ 一次占 8 个 `Bit` 格（池 64）。</summary>
        public const int AngelFeatherCount = 8;
        /// <summary>起始高度（米）：5.2 ⇒ 从画面上方落进视野，而不是凭空出现在玩家头顶。</summary>
        private const float FeatherDropHeight = 5.2f;
        /// <summary>羽毛在 X 方向的摇摆幅度（米，落到底时的最大值；顶部按 45% 打折）。</summary>
        private const float FeatherSwayX = 0.62f;
        /// <summary>羽毛在 Z 方向的摇摆幅度（米）。与 X **不同频率**，俯视才是一条乱飘的螺旋而不是斜直线。</summary>
        private const float FeatherSwayZ = 0.46f;
        /// <summary>摇摆角频率（弧度/秒）：3.4 ⇒ 2 秒里约摆 1.1 个来回。</summary>
        private const float FeatherSwayRate = 3.4f;
        /// <summary>羽片三个尺寸（米）：宽 / 厚 / 长 ⇒ **细长方片**（宽:长 ≈ 1:5.6）。</summary>
        private const float FeatherWidth = 0.075f;
        private const float FeatherThick = 0.012f;
        private const float FeatherLen = 0.42f;
        /// <summary>
        /// 羽毛寿命的钳制区间（秒）。
        /// ★ 上限 2.3 不是随手写的：本文件的墙钟兜底是 `MaxRealtimeLife = 2.5s`
        ///   （见那个常量），超过它就会被**提前抹掉** ⇒ 钳在它下面，保证"落到底"这件事一定看得见 ✓
        /// ★ 下限 0.5：表里若写 0（或漏填 `ruleValue`），羽毛仍然要落完，不能瞬间消失 ✓
        /// </summary>
        private const float FeatherLifeMin = 0.5f;
        private const float FeatherLifeMax = 2.3f;
        private const float SwingY = 0.55f;       // 刀光高度（比 `WorldView` 的 0.50 略高，两层不打架）
        private const float BeamY = 0.70f;        // 与水柱的视觉高度一致（`WorldView.OnBeam` 用 0.7）
        private const float GroundY = 0.09f;      // 冲击环贴地
        private const int ArcSlices = 26;         // 18 → **26**：更细的弧（片数只改精度，不改覆盖范围）
        // ★ 第四版：这个数被 `RunSession.MeleeFxSliceCount` **镜像**了 ★
        //   判定那边要用它算"片自身的切向半宽带来的角余量"（Δ/2 = 1.152°），才能真的包住特效
        //   ⇒ 改这里的片数必须同步改那边，否则判定的"包裹"会少掉这一角。
        /// <summary>弧光只铺**整条判定弧的 60%**（规格 55%~65%），且**以瞄准方向为中心**
        /// ⇒ 偏 ±0.60 × halfArc。不铺满 = 不再读作"一个饼"，而是"刀劈过去的一道痕" ✓
        /// ★ 第四版：这个数被 `RunSession.MeleeFxArcCover` **镜像**了 ★
        ///   （判定那边要用它算"特效实拍半张角"才能包住特效）⇒ 改这里必须同步改那边。</summary>
        private const float ArcCover = 0.60f;
        /// <summary>
        /// 薄带的两个半径比：内径 `BladeR0 = 0.78R`、外径 `BladeR1 = 1.00R`
        /// ⇒ 径向厚度基准 = 0.22R（原来是 0.42R→R 铺满）。
        /// ★ v273：这两个是**比例**（不是米），`RunSession.MeleeFxBladeR1` 是它们的**镜像**（必须逐位相同）★
        ///   锄头的 0.90 缩小落在 `tuning.json` 的 `tools.hoe.range`（3.8 → 3.42）上，
        ///   本文件收到的 `e.Range` 已经是缩小后的数（4.090 → **3.729m**）
        ///   ⇒ **不要**在这里再乘 0.90：那会让 26 片外沿变成 0.945R、缩进主弧（1.00R）以内，
        ///   并把"判定 / 最宽视觉"从 1.15 打到 1.087（详见文件头 v273 那段）✗
        /// </summary>
        private const float BladeR0 = 0.78f;
        private const float BladeR1 = 1.00f;
        /// <summary>
        /// ★ 第四版：片**径向厚度随 u 的摆幅**（±25%）★
        /// 厚度(u) = 0.22R × (1 + 0.25 × (2·sin(πu) − 1)) ⇒ 中间 **1.25×**、两端 **0.75×**
        /// ⇒ 0.275R ～ 0.165R。**外径恒定 = R**（只动内径）⇒ 覆盖范围不变，月牙更像刃 ✓
        /// </summary>
        private const float BladeThickSwing = 0.25f;
        /// <summary>
        /// ★ 第四版：独立的**内缘亮线**（刀刃反光）★
        /// 宽度 = `0.012R`（径向）、α = 0.85、颜色 = `Lerp(white, fx.Core, 0.55)`，
        /// 沿同一条弧铺、跟着同一套扫过淡出 —— 它只加一条极细的轮廓，不动外径/张角/配色来源。
        /// </summary>
        private const float EdgeLineWidth = 0.012f;
        private const float EdgeLineAlpha = 0.85f;
        private const float EdgeLineMix = 0.55f;
        private const float EdgeLineThick = 0.05f;    // 亮线的竖直厚度（比刀光片的 0.06 再薄一点）
        /// <summary>每一片被"扫过"之后单独淡掉的时间 ⇒ 不同时收尾，尾巴不会拖一条等亮的弧。</summary>
        private const float SweepFade = 0.06f;
        /// <summary>刀光从起手扫到收手用的时间（片数的错峰跨度）= ArcLife − SweepFade
        /// ⇒ 最后一片正好在 ArcLife 收尾，总时长仍等于常量（与片数无关）✓</summary>
        private const float SweepSpan = ArcLife - SweepFade;

        // ── ①d ★ v308 的旧方案（"巨型 = 同款刀光铺满一整圈"）**已被 v310 取代** ──
        //   v308 把 26 片首尾相接铺满 2π（角间距 2π/26、精确弦长）⇒ 一个**连续实心环带**；
        //   用户 v310 的原话是「需要剑刃感，而不是完整的环」⇒ 那条路整个撤掉。
        //   ⚠️ 这段旧文字**只作历史记录**（它描述的几何已经不在代码里）：
        //     · `dAng = 2π/26`、`u = i/26` 的周期厚度包络、26 片 + 26 条内缘亮线
        //     · 池账 52（Slice）
        //   ⇒ 现在的做法见下面 ★v310 那一段（8 段 × 30°、段间 15°、每段沿切向收成刃尖）。
        //   唯一**保留下来**的是：同一张 256×32 刃纹贴图（`_bladeMats` 按 `i % 3` 轮换 UV 段）、
        //   同一个 `ModeRadial` 薄片、外沿恒为 `BladeR1`(1.00R)、`Age = 0` 同刻亮起、
        //   `Hold` 淡出、0.30s 寿命、地面投影环保留 ✓

        // ══════════════════════════════════════════════════════════════════
        // ★★ v310：**巨型 = 8 段分离的刃痕**（用户：「重新制作巨型的特效，需要剑刃感，
        //    而不是完整的环，且颜色过于鲜艳」）★★
        //
        //  ── 改前（v308/v309）为什么被否 ──
        //    26 片**首尾相接**铺满 2π（角间距 = 2π/26、精确弦长）⇒ 那是一个**连续的实心环带**；
        //    颜色又是 `(1.00,0.84,0.35)` 的饱和金、峰值 α 0.95 ⇒ 一圈金箍，读不出"刀" ✗
        //
        //  ── 现在：一圈**断口** ──
        //    8 段 × 每段 30°，段间留 15° 空隙 ⇒ 8 × (30+15) = **360°** 正好闭合成一圈，
        //    但画面上一眼是"八道刀痕绕着人扫过留下的断口"，不是一个环 ✓
        //    每段由 5 片拼成（6°/片，弦长取精确 `2·sin(Δ/2)` ⇒ 同段内首尾相接、不露缝）。
        //
        //  ── 剑刃感从哪来（三个数）──
        //    ① **径向厚度沿切向收尖**：`thick(u) = 0.20R × belly(u) × (1−u)^0.90`，
        //       其中 `belly(u) = 0.30 + 0.70·min(1, 4u)` ⇒ 刃根 0.30×、u=0.25 处最厚 1.00×、
        //       到刃尖（u→1）收到 `GiantThickMinRatio`(0.010R ≈ 3.7cm)
        //       ⇒ 一段是"薄颈 → 鼓腹 → 长尖"的刃形，不是等宽的弧带 ✓
        //    ② **外沿恒为 R**（1.00R）：所有片的外侧都压在同一个半径上 ⇒ 那是**刃口**那条线；
        //       厚度只吃掉内侧（`R0 = R1 = R − thick/2`、`Depth = thick`）⇒ 外缘整齐如刃 ✓
        //    ③ **刃口白芯**：每段靠内的 2 片（`GiantCorePerArc`）在 1.00R 内侧
        //       `GiantCoreDepth`(0.016R) 处压一条 `GiantWhiteCore` 极亮线 ⇒ 反光的那道刃 ✓
        //
        //  ── 颜色降饱和（用户：「颜色过于鲜艳」）──
        //    主体 `GiantBlade` = **(0.92, 0.84, 0.62)**（原 1.00/0.84/0.35 的饱和金 → 偏灰的金）
        //    白芯 `GiantWhiteCore` = **(1.00, 0.98, 0.94)**（原 1.00/0.97/0.90，更中性）
        //    峰值 α `GiantAlphaPeak` = **0.80**（原 0.95）⇒ 不再糊成一片 ✓
        //    地面投影环同样降饱和：`GiantBlade × GiantGroundDarken(0.50)` = (0.46,0.42,0.31) ✓
        //
        //  ── 保留的四条（用户点名不要动的）──
        //    · **同刻亮起**：全部 `Age = 0`、`Sweep = false` ⇒ 没有"沿圈转过去"的重影
        //    · **0.30 秒**（`GiantRingLife`）
        //    · **`Hold` 淡出**（`1 − k⁴`：前段咬住、末段整体收掉）
        //    · **地面投影环**（贴地、更粗、更暗、覆盖判定的整条 0.4226R~1.05R）
        //
        //  ── 池账（`PoolPerKind` = 64）──
        //    `Slice`：刃痕 8×5 = **40** + 白芯 8×2 = **16** ⇒ **56 ≤ 64** ✓（余 8）
        //    `Bit`  ：地面环 32 + 火星 ≤10 + 灼烧火星 6 + 感电电弧 12 ⇒ **≤ 60 ≤ 64** ✓
        //  ⚠️ 半圆刀光那一支（`halfCount`）**一个字符都没动**，它的 26 片/`ArcSlices` 仍被
        //    `RunSession.MeleeFxSliceCount` 镜像着 ⇒ 不能借这个改动去动 `ArcSlices` ✗
        // ══════════════════════════════════════════════════════════════════
        /// <summary>整圈寿命（秒）。取 **0.30**：一眼要读成"整圈范围"，
        /// 就必须让这一圈同时存在得够久；0.15s 里同时亮起的整圈更像"闪一下"✗</summary>
        private const float GiantRingLife = 0.30f;
        /// <summary>刃痕的段数（8 段；8×(30+15)=360 ⇒ 一圈断口刚好闭合）。</summary>
        private const int GiantArcCount = 8;
        /// <summary>每一段刃痕的张角（度）。</summary>
        private const float GiantArcSpanDeg = 30f;
        /// <summary>段与段之间的空隙（度）。**不是 0** —— 空隙就是"不是完整环"的那一笔。</summary>
        private const float GiantArcGapDeg = 15f;
        /// <summary>段间距（度）= 张角 + 空隙 = 45°。</summary>
        private const float GiantArcStepDeg = GiantArcSpanDeg + GiantArcGapDeg;
        /// <summary>每段用几片薄片拼（5 片 ⇒ 6°/片；片数只提精度，不改覆盖范围）。</summary>
        private const int GiantSegPerArc = 5;
        /// <summary>段内每片的张角（度）。</summary>
        private const float GiantSegPerDeg = GiantArcSpanDeg / GiantSegPerArc;
        /// <summary>每段压几条白芯（靠内的前 2 片 = 刃身最厚的 2/5 ⇒ 白芯只落在"有肉"的地方）。</summary>
        private const int GiantCorePerArc = 2;
        /// <summary>刃根厚度基准（×`range`）。</summary>
        private const float GiantBladeThick = 0.20f;
        /// <summary>刃尖的最薄厚度（×`range`）—— 不能是 0，否则那一片整块消失。</summary>
        private const float GiantThickMinRatio = 0.010f;
        /// <summary>★ 峰值 α：0.95 → **0.80**（用户：「降低整体 α，避免糊成一片」）。</summary>
        private const float GiantAlphaPeak = 0.80f;
        /// <summary>刃口白芯的径向厚度（×`range`）与亮度。</summary>
        private const float GiantCoreDepth = 0.016f;
        private const float GiantCoreAlpha = 0.80f;
        /// <summary>地面投影环的内 / 外半径（× `range`）—— 保留 v306 的取值不动 ✓</summary>
        private const float GiantGroundR0 = 0.4226f;
        private const float GiantGroundR1 = 1.05f;
        /// <summary>地面投影环的片数（走 `Bit` 池）。32 片 = 每片 11.25°。</summary>
        private const int GiantGroundSlices = 32;
        /// <summary>★ v307：地面投影环"更暗、更粗"（用户：「地面投影环保留（更暗更粗）」）。
        /// 0.03 → **0.045**（粗 1.5×）、α 0.42 → **0.34**、压暗比例 0.55 → **0.66**。</summary>
        private const float GiantGroundThick = 0.045f;
        private const float GiantGroundAlpha = 0.34f;
        /// <summary>
        /// ★ v309：地面投影环的**压暗比例**（0.66 → **0.50**）。
        /// 配色也从"插件 Rim 压暗"改成"**金色同色相压暗**"（见 `SpawnSwing` 里的 `gCol`）：
        ///   `gCol = GiantGold × 0.50` = **(0.50, 0.42, 0.175)** ⇒ 与环带**同一个色相**、
        ///   只是明度压掉一半 ⇒ 读作"这一圈在地上的金色影子"，而不是另一套配色 ✓
        /// </summary>
        private const float GiantGroundDarken = 0.50f;

        // ══════════════════════════════════════════════════════════════════
        // ★★ v309：**巨型 = 纯金白色光圈**（用户：「巨型特效修改为金色，纯金白色光圈」）★★
        //
        //  ── 为什么这一招**不吃插件配色** ──
        //    用户点明"不要沿用插件配色（巨型这一招固定金色）"。所以这一支里
        //    `fx.Core / fx.Rim` **一个都不读**，四个图层全部从下面三个常量派生
        //    ⇒ 装不装插件、装哪件，巨型画出来都是同一个金白色 ✓
        //    （半圆刀光那一支**逐字未动**：它仍然 `Lerp(white, fx.Core/Rim, …)`）
        //
        //  ── 分层（从内到外四层，读作"金边·白芯"）──
        //    ① **内缘亮线 ×26**（`Slice` 池）= **白芯** `GiantWhiteCore` (1.00, 0.97, 0.90)、
        //        α 0.95（0.85 → 0.95：白芯要压得住金环带）、宽 0.016R（0.012R → 更宽一点才读得出"芯"）
        //    ② **环带 ×26**（`Slice` 池）= **金** `GiantGold` (1.00, 0.84, 0.35)
        //        出生（内）`GiantGoldDim` (0.72, 0.52, 0.16) α 0.18 → 末段（外）`GiantGold` α 0.95
        //        ⇒ 保留 v306 的"内低外高"径向 α 规格，只把色相换成金 ✓
        //    ③ **外缘暗金 ×16**（`Bit` 池）= `GiantGoldDim` (0.72, 0.52, 0.16)、α 0.55、
        //        贴在 1.02R 上 ⇒ "金边"这道外轮廓（用户给的第三个选项"外缘再压一层更暗的金"）✓
        //    ④ **地面投影环 ×32**（`Bit` 池）= `GiantGold × 0.50` = (0.50, 0.42, 0.175)
        //        ⇒ 地面那一圈也变金、只是压暗 ✓
        //  ── 池账（`PoolPerKind` = 64）──
        //    `Slice`：① 26 + ② 26 + 白闪 6 = **58 ≤ 64** ✓
        //    `Bit`  ：③ 16 + ④ 32 + 火星 ≤10 = **58 ≤ 64** ✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>刃痕的**主体色**：**偏灰的金** `(0.92, 0.84, 0.62)`
        /// （原 `(1.00, 0.84, 0.35)` 是饱和金，用户原话"颜色过于鲜艳" ⇒ 明度不变、把饱和度拉下来）。</summary>
        private static readonly Color GiantBlade = new Color(0.92f, 0.84f, 0.62f, 1f);
        /// <summary>**白芯 / 刃口反光** `(1.00, 0.98, 0.94)` —— 比原来的 (1.00,0.97,0.90) 更中性，
        /// 这样它读作"金属高光"而不是"一层淡金"。</summary>
        private static readonly Color GiantWhiteCore = new Color(1.00f, 0.98f, 0.94f, 1f);
        /// <summary>★ v310：**外缘暗金描边（连续 16 片一圈）已删除** —— 用户要的是"不是完整的环"，
        /// 一圈连续的外描边会把 8 段的断口重新连成一个环 ✗
        /// 刃口那条线改由**每段自己的白芯**（`GiantCorePerArc`）承担 ⇒ 断口处也断开 ✓</summary>

        // ── ①e ★ v309：刀光的**白闪**（普通挥击专用，巨型不加）──────
        /// <summary>白闪的寿命（秒）—— 用户给的就是 0.04："一闪就没"。</summary>
        private const float WhiteFlashLife = 0.04f;
        /// <summary>白闪的段数。6 段正好接满那 26 片铺开的同一条弧（26/6 ≈ 4.3 片一段）。</summary>
        private const int WhiteFlashStripes = 6;

        private const int RingSegs = 16;
        private const int SwingSparks = 8;        // 6~10 的中间值

        // ── ★ v310：锄头挥舞的**灼烧 / 感电** 粒子（只在装了对应插件时出现）──────
        /// <summary>灼烧余烬的颗数 —— 用户要"**略微**"（例如 6~9 颗）⇒ 取 **6**（下限：
        /// 巨型那一路的 `Bit` 池只剩 4 格余量，详见 `SpawnSwing` 里的池账）。</summary>
        private const int SwingBurnEmbers = 6;
        /// <summary>余烬的重力（m/s²）。比普通火星的 12 更沉 ⇒ 落得快、更像"灰"而不是"烟花"。</summary>
        private const float BurnEmberGravity = 9f;
        /// <summary>余烬寿命区间（秒）—— 用户给的是 0.25~0.4。</summary>
        private const float BurnEmberLifeMin = 0.25f;
        private const float BurnEmberLifeMax = 0.40f;
        /// <summary>感电电弧的条数 —— 用户给的是 3~5 条，取 **4**。</summary>
        private const int SwingShockBolts = 4;
        /// <summary>每条电弧由几段折线拼成（3 段 = 两个折点）。</summary>
        private const int ShockArcSegs = 3;
        /// <summary>电弧每段沿切向的张角（度）—— 4° 在 3.7m 半径上 ≈ 26cm/段 ⇒ 三段 ≈78cm。</summary>
        private const float ShockArcStepDeg = 4f;
        /// <summary>电弧所在的半径（×`range`）—— 压在刃口（1.00R）上。</summary>
        private const float ShockArcRadius = 1.00f;
        /// <summary>电弧折点的半径抖动幅度（×`range`，±）⇒ 折线有"跳"的感觉。</summary>
        private const float ShockArcJitterAmp = 0.035f;
        /// <summary>电弧的粗细（米）—— 极细（0.022m）。</summary>
        private const float ShockArcWidth = 0.022f;
        /// <summary>电弧寿命（秒）—— 用户给的是"0.1 秒内闪灭"。</summary>
        private const float ShockArcLife = 0.10f;
        private const int DirtBits = 12;
        private const int BladeStreaks = 3;       // 层 A 的**细锐高光**道数：2 道主 + 1 道很淡的第三道
        private const float BladeStreakFaint = 0.12f; // 第三道高光的强度（规格 0.12；主两道 = 1.0 / 0.80）
        private const float BladeFlowSpeed = 0.60f; // 贴图沿 X 流动的相位速度（圈/秒）
        // ★ 第五版：1.6 → **0.60** ★ 因为每片现在只看到贴图的 **1/3 段**，
        //   而一片只活 `SweepFade`=0.06s：0.60×0.06 = 0.036u ≈ 9px，正好"磨痕在动但还看得出形状"；
        //   沿用 1.6 的话同样 0.06s 要滑 24px（≈ 1.5 条磨痕长）⇒ 纹理糊成一片，等于白加 ✗

        // ── ①c 刀光贴图（第五版：256×32 · 三层纹理 · 横向 3 段 UV）──────
        private const int BladeTexW = 256;        // 128 → **256**（细节翻倍）
        private const int BladeTexH = 32;         // 16 → **32**
        /// <summary>贴图横向切成几段 = 几份刀光材质（两者**必须相等**：
        /// 段宽 = `1/BladeTexSections`，材质的 `SetTextureScale` 就按这个数算）。</summary>
        private const int BladeTexSections = 3;
        /// <summary>层 B 磨痕条数（规格 20~30 的中点）。每条长 5~18px、α 0.10~0.22。</summary>
        private const int GrindMarkCount = 26;
        /// <summary>层 C 崩刃/缺口数（规格 2~3）。</summary>
        private const int NotchCount = 3;
        /// <summary>层 B / 层 C 的固定种子（`xorshift32`）⇒ 同一张图**每次生成字节相同**，
        /// 不受 `UnityEngine.Random` 的运行期状态影响（回放/截图对比才有意义）✓</summary>
        private const uint BladeTexSeed = 0x5EEDFA11u;

        // ── ①b 水柱射线：3 段渐细渐淡（起点 1.0× → 末端 0.55×）────────
        private const int BeamSegs = 3;
        private const float BeamTipWidth = 0.55f;      // 末端相对起点的宽度/亮度倍数
        private const float BeamBaseAlpha = 0.95f;     // 起点透明度
        // ── ★ v309：水柱的**外晕 / 内核**两层（用户：「内层更亮更细」）──
        /// <summary>★ v311 修 bug：外晕相对柱体宽度的倍数 **1.75 → 1.00** ★
        ///
        /// ── 用户报的原话 ──
        ///   「**水柱的初始宽度被修改了，恢复原来初始宽度**」
        ///
        /// ── 根因（算得出，不是感觉）──
        ///   `SpawnBeam` 里柱体那 3 段的宽度算式**一个数都没动**（`w` 就是判定半宽、
        ///   锥度仍 1.0× → `BeamTipWidth` 0.55×）；变宽的是 v309 新加的这层**外晕**：
        ///   `Thick = w × 1.75` ⇒ 整根水柱的**视觉外沿**变成判定的 **1.75 倍**。
        ///   玩家看到的"水柱"是"柱体 + 外晕"的并集 ⇒ 视觉宽度凭空涨 75% ✗
        ///
        /// ── 现在取 1.00× ──
        ///   外晕的最外沿**正好压在 `w` 上** ⇒ **最外沿宽度严格 = `w`**（`Thick` 口径，
        ///   与判定同源：`w = Clamp(HalfWidth × 加粗, 0.06, 0.90)`）。
        ///   柱体自身最宽的那一段只有 `w × 0.925`（3 段锥度 1.0→0.55 的第 1 段取中点 fMid=0.1667）
        ///   ⇒ 1.00× 的外晕只在两侧各露出 `0.0375w` 的一丝柔边，读不出"变粗" ✓
        ///   「层次感」改由**亮度**承担（见 `HaloAlpha`，0.24 → 0.32）而不是宽度 ✓
        /// </summary>
        private const float HaloWidthScale = 1.00f;
        /// <summary>★ v311：外晕的透明度 **0.24 → 0.32** ——
        /// 宽度收回 `w` 之后，"外层"这件事不能再靠宽度了：同宽的一层柔光压在柱体上、
        /// 颜色再往白里推一档、α 提一点 ⇒ 读作"柱体外面那层雾"或"边缘的柔光"，
        /// 而整根水柱**一个像素都没有变宽** ✓</summary>
        private const float HaloAlpha = 0.32f;
        /// <summary>外晕寿命（0.12s：比柱体的 0.10s 略长 ⇒ 主体灭了余晖还在）。</summary>
        private const float HaloLife = 0.12f;
        /// <summary>内核相对柱体宽度的倍数（0.42× ⇒ 一条细芯）。</summary>
        private const float CoreWidthScale = 0.42f;
        /// <summary>内核寿命（0.07s：比柱体的 0.10s 短 ⇒ 亮芯先收，留柱体余晖）。</summary>
        private const float CoreLife = 0.07f;

        // ── ② 像素水花（原来 8 颗圆球）────────────────────────────────
        private const int SplashPixels = 16;          // 规格 14~20 的中点，再按 `fx.Sparks` 抖动
        private const float PixelSizeMin = 0.05f;     // 规格 0.05~0.09 m
        private const float PixelSizeMax = 0.09f;
        private const float PixelLifeMin = 0.25f;     // 规格 0.25~0.45 s
        private const float PixelLifeMax = 0.45f;

        // ── ③ 立体烟雾 ──────────────────────────────────────────────
        /// <summary>★ v309：**10 团**（12 → 10）。为什么反而变少：团内球数从 3~4 涨到 **4~6**
        /// ⇒ `Mist` 池的硬账逼着团数让位：12 × 6 = **72 &gt; 64** ✗，
        /// 10 × 6 = **60 ≤ 64** ✓。糊脸的密度还涨了（改前平均 12×3.5 = 42 球、现在 10×5 = 50 球）✓</summary>
        private const int SmokePuffs = 10;
        /// <summary>★ v309：每团 **4~6** 个球（用户：「团内球数 3~4 → 4~6」）。</summary>
        private const int SmokeBlobsMin = 4;
        private const int SmokeBlobsMax = 6;
        private const float SmokeLifeMin = 0.80f;     // 规格 0.8~1.2 s
        private const float SmokeLifeMax = 1.20f;
        private const float SmokeSize0 = 0.25f;       // 规格：尺寸 0.25 → 0.90 m
        private const float SmokeSize1 = 0.90f;
        private const float SmokeGray0 = 0.25f;       // 规格：灰度由深到浅 0.25 → 0.55
        private const float SmokeGray1 = 0.55f;
        // ── ★ v309：**团内越晚出现的球越亮**（用户：「模拟火光穿透烟雾」）──
        /// <summary>团内**最后**一个球的起始灰度（0.25 → 0.52：一开始就比第一个亮一倍多）。</summary>
        private const float SmokeGray0Later = 0.52f;
        /// <summary>团内**最后**一个球的收尾灰度（0.55 → 0.80：接近白 ⇒ 读作"被火光打过").</summary>
        private const float SmokeGray1Later = 0.80f;
        /// <summary>团内**第一个**球的不透明度倍率（相对 `SmokeAlpha`）。</summary>
        private const float SmokeAlphaFirst = 0.80f;
        /// <summary>团内**最后**一个球的不透明度倍率（0.80 → 1.55 ⇒ 后出来的球更"实"）。</summary>
        private const float SmokeAlphaLast = 1.55f;
        /// <summary>团内后出来的球往**暖橙**里推多少（0.55 ⇒ 火光穿透的暖调）。</summary>
        private const float SmokeHotMix = 0.55f;
        /// <summary>团内相邻两球的出场错开（秒）。给负 `Age` ⇒ 后面的球"稍晚才亮"。</summary>
        private const float SmokeBlobStagger = 0.02f;
        /// <summary>"火光"的暖橙（只用于把后面的烟球往暖里推）。</summary>
        private static readonly Color SmokeHot = new Color(1f, 0.72f, 0.30f, 1f);
        private const float SmokeRiseDecay = 2.2f;    // 上浮速度的衰减（m/s²）：越飘越慢
        private const float SmokeTint = 0.22f;        // 烟雾里掺多少插件本体色（剩下的恒为灰度）
        private const float SmokeAlpha = 0.55f;       // 单球 alpha：球要半透，堆叠起来才有体积感

        /// <summary>水柱命中端的水花节流：按住水壶时 `BeamEvent` 是**每固定帧一次**（60/s）。
        /// ★ 本轮从 0.10s 放大到 0.18s ★ 的依据是**池容量算术**（不是手感）：
        ///   像素方块 14~20 个、寿命平均 0.35s ⇒ 0.10s 一圈就是 10/s × 17 × 0.35 ≈ **60 个常驻**，
        ///   而 `Bit` 池只有 64 格，还要同时供养火星（8/刀）+ 冲击环（16/爆）+ 土粒（12/爆）
        ///   ⇒ 刀光的火星会被水花挤掉 ✗。0.18s 一圈 ⇒ 5.6/s × 17 × 0.35 ≈ **33 个常驻**，
        ///   `Bit` 池还剩 30 格给火星/环/土粒 ✓（法阵共用这个节流 ⇒ 同一次命中只出 1 个）</summary>
        private const float SplashInterval = 0.18f;
        /// <summary>
        /// ★★ v307：水花节流必须是**每道一份**，不能全局面共享 ★★
        ///
        /// ── 改前的真 bug（用户：「水壶的多柱…只有第一个水柱有特效」）──
        ///   改前只有**一个** `_nextSplashAt`。而 `RunSession.FireBeam` 是
        ///   `for (b = 0; b < beams; b++) Bus.Publish(new BeamEvent{…})` ——
        ///   **同一帧**连发 N 个事件、`Time.unscaledTime` 完全相同 ⇒
        ///     第 0 道：`now >= _nextSplashAt` 成立 ⇒ 出水花，并把 `_nextSplashAt = now + 0.18`
        ///     第 1..N−1 道：`now < _nextSplashAt` ⇒ **直接跳过水花** ✗
        ///   ⇒ 屏幕上就是"只有第一根水柱有末端水花"，而其余柱子光秃秃的。
        ///   这不是调参问题，是**一个变量被 N 条并发的路径共用**。
        /// ── 现在按 `BeamEvent.Slot`（这一道是第几道）分开计时 ⇒ 每道都有自己的节流 ✓
        ///   `Slot` 默认 0 ⇒ 剑气 / 共振连锁这两个**非多柱**来源走 0 号格，行为与改前一致 ✓
        /// </summary>
        private const int BeamFxSlots = 8;
        private static readonly float[] _nextSplashAt = new float[BeamFxSlots];

        // ══════════════════════════════════════════════════════════════
        // 建立 / 拆除
        // ══════════════════════════════════════════════════════════════

        /// <summary>新的一次 play 会话开始前清干净（关掉 Domain Reload 时静态字段会活过上一轮）。</summary>
        public static void ResetForNewSession()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            _ready = false;
            LiveRenderers = 0;
            Swings = Beams = Blasts = SplashBursts = SparksSpawned = DirtSpawned = 0;
            PixelsSpawned = SmokeSpawned = 0;
            WavesSpawned = PeelsSpawned = 0;
            BlackHoles = 0;
            WardShields = 0;
            OrbsSpawned = RipplesSpawned = 0;
            WaterRings = BladeFlashes = EchoRings = MarkPulses = BloodTrailDots = MinePulses = 0;
            // ★ v310：巨型刃痕 / 灼烧余烬 / 感电电弧
            //   ★ v311：原来这一格里还有"黑洞同帧去重"的那一格，那个字段已随 bug 修复删除 ✓
            GiantBlades = BurnEmbers = ShockBolts = 0;
            // ★ v312：潮汐推力反馈的次数（与上面同一批"只增不清、新局归零"的读数）
            TideArcs = 0;
            // ★ v314：复活羽毛的片数（同上）
            AngelFeathers = 0;
            for (int i = 0; i < _nextSplashAt.Length; i++) _nextSplashAt[i] = 0f;
            _bladeFlow = 0f;
            for (int i = 0; i < Pools.Length; i++) Pools[i] = null;
            if (_mat != null) UnityEngine.Object.Destroy(_mat);
            // ★ 第五版：刀光材质是 3 份（UV 段轮换）⇒ 逐份销毁
            if (_bladeMats != null)
            {
                for (int i = 0; i < _bladeMats.Length; i++)
                    if (_bladeMats[i] != null) UnityEngine.Object.Destroy(_bladeMats[i]);
            }
            _mat = null;
            _bladeMats = null;
            _mpb = null;
            _mesh = null;
            _sphereMesh = null;
            // ★ v304：香蕉皮的网格是**我们自己建的**（不是内置共享网格）⇒ 必须真的销毁，
            //   否则每次重置都漏一份 Mesh。下一次 `EnsureBuilt` 会重建一份一模一样的（纯函数）✓
            if (_peelMesh != null) UnityEngine.Object.Destroy(_peelMesh);
            _peelMesh = null;
            // ★ v307：贴地扁圆的网格也是我们自己建的 ⇒ 同步销毁（否则每次重置漏一份 Mesh）
            if (_discMesh != null) UnityEngine.Object.Destroy(_discMesh);
            if (_hexMesh != null) UnityEngine.Object.Destroy(_hexMesh);
            _discMesh = null;
            _hexMesh = null;
            // ★ 贴图**不**在这里销毁：它是"只生成一次"的静态缓存（带 `HideFlags.DontSave`），
            //   下一轮 play 直接复用 ⇒ 连 `SetPixel` 的那 256×32 = 8192 次循环也不再跑 ✓
        }

        /// <summary>建根 + 材质 + 网格 + 四个池（幂等）。由宿主 `Awake` 调用一次。</summary>
        public static void EnsureBuilt(Transform host)
        {
            if (_ready) return;

            _mesh = PrimMesh(PrimitiveType.Cube);
            if (_mesh == null) { Log.Warn("[AttackVfx] 取不到 Cube 网格 ⇒ 特效层不启用"); return; }
            _sphereMesh = PrimMesh(PrimitiveType.Sphere);   // 立体烟雾用；取不到就退回 Cube（不致命）
            // ★ v304：香蕉皮的程序化网格（三瓣 + 翘尖）。**代码生成、不写盘** ⇒ 仍然零美术资产 ✓
            _peelMesh = BuildPeelMesh();
            // ★ v307：贴地扁圆的程序化网格（污水落地的深绿渍）
            _discMesh = BuildDiscMesh();
            // ★ v308：护壤的六边形护盾面片（一片六边形，双面）
            _hexMesh = BuildHexMesh();

            _mat = MakeMaterial(null, "AttackVfxShared");
            if (_mat == null) { Log.Warn("[AttackVfx] 没有任何可用透明 shader ⇒ 特效层不启用"); return; }

            // 一张贴图：**只生成一次**（静态缓存 + HideFlags.DontSave），
            // 但它喂 **3 份**刀光材质 —— 每份只看贴图的 1/3 段（`SetTextureOffset` 到各自段起点），
            // 26 片按 i%3 轮换 ⇒ 相邻片看到的纹理不同（第五版的核心）✓
            Texture2D bladeTex = BladeTexture();
            _bladeMats = new Material[BladeTexSections];
            for (int s = 0; s < BladeTexSections; s++)
            {
                Material bm = MakeMaterial(bladeTex, "AttackVfxBlade" + s);
                if (bm != null) ApplyBladeUvWindow(bm, s);
                _bladeMats[s] = bm;
            }

            var rootGo = new GameObject("AttackVfxPool");
            rootGo.transform.SetParent(host, false);
            _root = rootGo.transform;

            _mpb = new MaterialPropertyBlock();
            _colId = Shader.PropertyToID("_Color");
            _baseColId = Shader.PropertyToID("_BaseColor");

            for (int k = 0; k < PoolKindCount; k++)
            {
                PoolKind kind = (PoolKind)k;
                int size = SizeOf(kind);
                // 烟雾池用球网格，香蕉皮池用**程序化网格**（每个渲染体共享同一份），其余用方块网格
                // —— 网格是共享的，仍然"1 份 Mesh 多个渲染体" ✓
                Mesh m = kind == PoolKind.Mist && _sphereMesh != null ? _sphereMesh
                       : kind == PoolKind.Peel && _peelMesh != null ? _peelMesh
                       : _mesh;
                Material mat = (k == (int)PoolKind.Slice && _bladeMats != null && _bladeMats[0] != null)
                             ? _bladeMats[0] : _mat;
                var p = new Pool { Items = new Piece[size], Cursor = 0 };
                for (int i = 0; i < size; i++)
                {
                    var go = new GameObject("FX" + k + "_" + i.ToString("00"));
                    go.transform.SetParent(_root, false);
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.enabled = false;                 // 回收 = 关渲染，不 SetActive（更便宜）
                    p.Items[i] = new Piece { T = go.transform, Mr = mr, Active = false, BaseMesh = m, Mf = mf };
                }
                Pools[k] = p;
            }

            // ★ 第三版：原来的"法阵"独占渲染体（`FXCircle` / `_circlePiece`）已按用户要求删除
            //   ⇒ 池外不再有任何渲染体，总数 = 4 × 64 = 256 ✓

            _ready = true;
            Log.Info("[AttackVfx] 三路攻击特效层就绪：" +
                     $"{PoolKindCount} 池（4×{PoolPerKind} + 香蕉皮 {PeelPool}）= {MaxRenderers} 个渲染体（同屏上限 320）· " +
                     $"网格=Cube/Sphere/Peel/Disc(共享 4 份) · 材质={1 + BladeTexSections} 份(1 共享 + {BladeTexSections} 刀光 UV 段轮换, shader={ShaderName}) · " +
                     $"贴图 1 张({BladeTexW}×{BladeTexH}·3 层纹理·{BladeTexSections} 段 UV, 代码生成/DontSave) · " +
                     $"刀光 {ArcSlices} 片/{ArcLife:0.00}s(张角 {ArcCover:0.00}×弧·{BladeR0:0.00}R~{BladeR1:0.00}R·扫过淡出 {SweepFade:0.00}s) · " +
                     $"整圈 = {GiantArcCount} 段分离刃痕（每段 {GiantArcSpanDeg:0}°/空 {GiantArcGapDeg:0}°、"
                     + $"每段 {GiantSegPerArc} 片、峰值α {GiantAlphaPeak:0.00}）"
                     + $"/{GiantRingLife:0.00}s + 白芯 {GiantArcCount * GiantCorePerArc} + 地面环 {GiantGroundSlices} · " +
                     $"火星 {SwingSparks} 颗/{SparkLife:0.00}s · " +
                     $"水柱 {BeamSegs} 段/{BarLife:0.00}s(末端 {BeamTipWidth:0.00}×) · " +
                     $"像素水花 {SplashPixels}({PixelSizeMin:0.00}~{PixelSizeMax:0.00}m)/{PixelLifeMin:0.00}~{PixelLifeMax:0.00}s(节流 {SplashInterval:0.00}s) · " +
                     $"冲击环 {RingSegs} 段/{RingLife:0.00}s · 土粒 {DirtBits} 颗/{DirtLife:0.00}s · " +
                     $"烟雾 {SmokePuffs} 团×{SmokeBlobsMin}~{SmokeBlobsMax} 球/{SmokeLifeMin:0.00}~{SmokeLifeMax:0.00}s");
        }

        /// <summary>取一份内置网格（借用 `CreatePrimitive` 的共享 mesh，然后立刻销毁那个临时物体）。
        /// 全程不新建任何 Mesh ⇒ 零资产 ✓</summary>
        private static Mesh PrimMesh(PrimitiveType type)
        {
            GameObject tmp = null;
            try
            {
                tmp = GameObject.CreatePrimitive(type);
                // `CreatePrimitive` 会带一个 Collider，而且它在 `Awake` 里就已经是激活的。
                // 立刻关掉：这一帧物理世界就不该看见这个临时物体（回合内可能有投射物做碰撞查询）✓
                tmp.SetActive(false);
                var mf = tmp.GetComponent<MeshFilter>();
                return mf != null ? mf.sharedMesh : null;
            }
            catch (Exception e)
            {
                Log.Warn("[AttackVfx] 取内置网格失败：" + e.Message);
                return null;
            }
            finally
            {
                if (tmp != null) UnityEngine.Object.Destroy(tmp);
            }
        }

        /// <summary>
        /// ★ v304：**香蕉皮的程序化网格**（用户需求："污水的香蕉皮单独建模…不要用现有图元凑"）。
        ///
        /// ── 形状怎么来的（不是"三个方块拼一下"）──
        ///   真实剥开的香蕉皮是一个**躺在地上的三瓣三脚架**：每瓣从中间往一个方向摊开、
        ///   越往外越薄、**瓣尖向上翘起**；瓣的横截面是**中脊略高的浅槽**（不是平板）。
        ///   这份网格就按这三条建：
        ///     · 3 瓣，方位角 0° / 120° / 240°（再各 +15° 偏一档，免得与坐标轴对齐显得"摆"）
        ///     · 沿瓣向 `u`：半径 `0.045 → 0.62`（m），半宽 `0.16·(1−u)^0.6 + 0.012`（**往尖收**）
        ///     · 高度 `h(u) = 0.30·u^2.4` ⇒ **瓣尖翘起 0.30m**（低次幂 = 根部还是贴地的）
        ///     · 横截面 3 列（v = −1 / 0 / +1），中脊再抬 `0.05·(1−v²)` ⇒ 浅槽 ✓
        ///   三角数：3 瓣 × 9 段 × 2 四边 × 2 面 = 216 个三角 —— 一个 8 格池的共享网格，代价可忽略。
        ///
        /// ── 纪律 ──
        ///   · `new Mesh()` 是**代码生成**（与刀光贴图同一个先例）⇒ 不写盘、不进资产库 ✓
        ///   · `HideFlags.DontSave` ⇒ 不会被当成资产序列化 ✓
        ///   · 只建一次（`EnsureBuilt` 里），`ResetForNewSession` 里真的 `Destroy` ✓
        ///   · 颜色**烘不进网格**（共享材质、顶点色在不同 shader 上行为不一致）⇒
        ///     一律由 `MaterialPropertyBlock` 染色（与池里其余特效同一条做法）✓
        /// </summary>
        private static Mesh BuildPeelMesh()
        {
            const int lobes = 3;
            const int segs = 9;
            const int cols = 3;                 // v = −1 / 0 / +1
            const float r0 = 0.045f, r1 = 0.62f;
            const float curl = 0.30f;           // 瓣尖翘起高度（米）
            const float camber = 0.05f;         // 中脊比两侧高多少（米）= 浅槽

            var tri = new List<Vector3>(lobes * segs * 2 * 6 * 3);
            for (int b = 0; b < lobes; b++)
            {
                float phi = (b * 120f + 15f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi));
                var side = new Vector3(Mathf.Cos(phi), 0f, -Mathf.Sin(phi));
                for (int i = 0; i < segs; i++)
                {
                    float u0 = i / (float)segs, u1 = (i + 1) / (float)segs;
                    for (int c = 0; c < cols - 1; c++)
                    {
                        float v0 = c / (float)(cols - 1) * 2f - 1f;      // −1 … +1
                        float v1 = (c + 1) / (float)(cols - 1) * 2f - 1f;
                        AddPeelQuad(tri, dir, side, u0, u1, v0, v1, r0, r1, curl, camber);
                    }
                }
            }

            var m = new Mesh { name = "PeelMesh", hideFlags = HideFlags.DontSave };
            m.SetVertices(tri);
            // ══════════════════════════════════════════════════════════════════
            // ★ v306：**根因修复 —— 必须给三角形索引** ★
            //
            // ── 改前（只 `SetVertices`、没有 `SetTriangles`）为什么"代码全对但看不见" ──
            //   `Mesh` 的顶点缓冲**不等于**三角形：没有索引 ⇒ `triangles` 为空、索引数为 **0**
            //   ⇒ 网格里**一个三角形都没有**，Unity 不会画任何东西
            //   （不是"画错/画糊"，是"没有可画的图元"）。
            //   连带后果：`RecalculateNormals()` 没有三角形可算法线 ⇒ 法线全是零向量。
            //   ⇒ 香蕉皮池里 8 个渲染体从头到尾挂着一张**空网格**：
            //     `PeelsSpawned` 一路涨、位置/旋转/缩放全都算对、`mr.enabled = true`，
            //     屏幕上却一个像素都没有 —— 这正是用户报的"香蕉皮还没有做"。
            //   ⇒ 落地时的泥点（`SpawnMudSplash`，走 Bit 池的 **Cube** 网格）看得见、
            //     只有皮看不见，也是同一份证据：问题在**这一张网格**，不在调用链。
            //
            // ── 为什么是"顺序索引" ──
            //   `tri` 本来就是**三角形汤**（`AddPeelQuad` 每 3 个顶点一个三角、顶点不共享）
            //   ⇒ 索引就是 0,1,2,3,… 直接把顶点缓冲按三角表消费掉，不需要重排、不需要去重。
            //   `subMeshCount` 由 `SetTriangles(idx, 0)` 设为 1 ⇒ 渲染走第 0 个 submesh ✓
            //   ⚠️ 本项目其余 12 处程序化网格（`PlayerBean` / `TownKit` / `ProceduralBugs` …）
            //     全部是 `SetVertices` + `SetTriangles` 两件套 —— 只有这一个漏了第二件。
            // ══════════════════════════════════════════════════════════════════
            var peelIdx = new int[tri.Count];
            for (int i = 0; i < peelIdx.Length; i++) peelIdx[i] = i;
            m.SetTriangles(peelIdx, 0);
            m.RecalculateNormals();       // 现在有三角形了 ⇒ 法线是真实的面法线
            m.RecalculateBounds();
            return m;
        }

        /// <summary>香蕉皮的一小块四边（**双面**：俯视/侧视都在，不与背面剔除打架）。</summary>
        private static void AddPeelQuad(List<Vector3> tri, Vector3 dir, Vector3 side,
                                        float u0, float u1, float v0, float v1,
                                        float r0, float r1, float curl, float camber)
        {
            Vector3 a = PeelPoint(dir, side, u0, v0, r0, r1, curl, camber);
            Vector3 b = PeelPoint(dir, side, u1, v0, r0, r1, curl, camber);
            Vector3 c = PeelPoint(dir, side, u1, v1, r0, r1, curl, camber);
            Vector3 d = PeelPoint(dir, side, u0, v1, r0, r1, curl, camber);
            // 正面
            tri.Add(a); tri.Add(b); tri.Add(c);
            tri.Add(a); tri.Add(c); tri.Add(d);
            // 反面
            tri.Add(a); tri.Add(c); tri.Add(b);
            tri.Add(a); tri.Add(d); tri.Add(c);
        }

        /// <summary>香蕉皮表面的一点（局部坐标；y=0 是地面）。</summary>
        private static Vector3 PeelPoint(Vector3 dir, Vector3 side, float u, float v,
                                         float r0, float r1, float curl, float camber)
        {
            float rad = Mathf.Lerp(r0, r1, u);
            float halfW = 0.16f * Mathf.Pow(Mathf.Max(0f, 1f - u), 0.6f) + 0.012f;
            float y = curl * Mathf.Pow(u, 2.4f) + camber * (1f - v * v);
            return dir * rad + side * (halfW * v) + Vector3.up * y;
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v308：**剑刃的两张程序化网格已删除**（用户改口径：「巨型的特效修改，改为与
        //    普通的攻击特效相同，只是变成一个环」）★★
        //   v307 的 `_bladeMesh` / `_bladeEdgeMesh` + `BuildBladeMesh` / `BladePoint` /
        //   `BuildBladeEdgeMesh` / `BladeEdgePoint` 全部撤掉：整圈现在就是
        //   **普通刀光的那一套片状几何**铺满 360°，不需要任何专用网格 ⇒ 一个字符都不留 ✓
        //   （程度说明：撤掉之后 `Slice` 池里每一格都回到本池默认的 Cube 网格，
        //     `Piece.UseMesh` 只剩贴地扁圆 `_discMesh` 一个使用者。）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>一个四边 → 两个三角，**正反两圈绕向都写**（材质不一定 `Cull Off`，兜底）。</summary>
        private static void AddQuad(List<Vector3> tri, List<Vector2> uv,
                                    Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                                    Vector2 A, Vector2 B, Vector2 C, Vector2 D)
        {
            tri.Add(a); tri.Add(b); tri.Add(c); uv.Add(A); uv.Add(B); uv.Add(C);
            tri.Add(a); tri.Add(c); tri.Add(d); uv.Add(A); uv.Add(C); uv.Add(D);
            // 反面
            tri.Add(a); tri.Add(c); tri.Add(b); uv.Add(A); uv.Add(C); uv.Add(B);
            tri.Add(a); tri.Add(d); tri.Add(c); uv.Add(A); uv.Add(D); uv.Add(C);
        }

        /// <summary>把三角汤 + UV 装成一份网格。**`SetTriangles` 不能漏**（见 v306 香蕉皮那段复盘）。</summary>
        private static Mesh FinishMesh(string name, List<Vector3> tri, List<Vector2> uv)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.SetVertices(tri);
            m.SetUVs(0, uv);
            var idx = new int[tri.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;      // 三角汤 ⇒ 顺序索引
            m.SetTriangles(idx, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// ★ v307：**贴地的扁圆**（半径 0.5、半厚 0.018 局部单位）。
        /// 用法：`ModeBallistic` + 等比缩放 `S` ⇒ 世界半径 = `0.5·S`、世界厚度 = `0.036·S`。
        /// 半径 1.7m 时厚度只有 0.12m ⇒ 明明是一张**贴地的渍**，不是一颗扁球 ✓
        /// 三角数：20 段 × (顶扇 1 + 底扇 1 + 边 2) = 80，正反两圈绕向 ⇒ 160。
        /// </summary>
        private static Mesh BuildDiscMesh()
        {
            const int segs = 20;
            const float rad = 0.5f;        // 局部半径
            const float half = 0.018f;     // 局部半厚（world = half/rad × 世界半径）
            var tri = new List<Vector3>(segs * 6 * 3);
            var uv = new List<Vector2>(segs * 6 * 3);

            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs;
                float a1 = (i + 1) * Mathf.PI * 2f / segs;
                var p0 = new Vector3(Mathf.Cos(a0) * rad, 0f, Mathf.Sin(a0) * rad);
                var p1 = new Vector3(Mathf.Cos(a1) * rad, 0f, Mathf.Sin(a1) * rad);
                Vector2 u0 = new Vector2(p0.x / rad * 0.5f + 0.5f, p0.z / rad * 0.5f + 0.5f);
                Vector2 u1 = new Vector2(p1.x / rad * 0.5f + 0.5f, p1.z / rad * 0.5f + 0.5f);
                var uc = new Vector2(0.5f, 0.5f);
                // 顶 / 底两片扇面（中心 → 相邻两点）
                AddQuad(tri, uv,
                        new Vector3(0f, half, 0f), p0 + Vector3.up * half,
                        p1 + Vector3.up * half, new Vector3(0f, half, 0f), uc, u0, u1, uc);
                AddQuad(tri, uv,
                        new Vector3(0f, -half, 0f), p0 - Vector3.up * half,
                        p1 - Vector3.up * half, new Vector3(0f, -half, 0f), uc, u0, u1, uc);
                // 侧边（让它在斜视角下不是一张零厚的纸）
                AddQuad(tri, uv,
                        p0 + Vector3.up * half, p1 + Vector3.up * half,
                        p1 - Vector3.up * half, p0 - Vector3.up * half, u0, u1, u1, u0);
            }
            return FinishMesh("DiscMesh", tri, uv);
        }

        /// <summary>
        /// ★ v308：**六边形面片**（护壤的护盾）。XY 平面上一个外接半径 0.5 的正六边形，
        /// 双面（正反两圈绕向都写）⇒ 从圈外任何角度看都可见 ✓
        /// 缩放在 `ModeRadial` 里被解释成 `(r×Chord, Thick, Depth)`：
        /// X = 面片宽度（随环半径一起张开）、Y = 面片高度（竖直站在玩家周围）、Z = 厚度。
        /// 三角数：6 段 × 2 面 = 12（共享一份，代价可忽略）。
        /// </summary>
        private static Mesh BuildHexMesh()
        {
            const int sides = 6;
            var tri = new List<Vector3>(sides * 6);
            var uv = new List<Vector2>(sides * 6);
            var uc = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, Mathf.Sin(a0) * 0.5f, 0f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, Mathf.Sin(a1) * 0.5f, 0f);
                var u0 = new Vector2(p0.x + 0.5f, p0.y + 0.5f);
                var u1 = new Vector2(p1.x + 0.5f, p1.y + 0.5f);
                // 正面
                tri.Add(Vector3.zero); tri.Add(p0); tri.Add(p1);
                uv.Add(uc); uv.Add(u0); uv.Add(u1);
                // 反面（绕向相反）
                tri.Add(Vector3.zero); tri.Add(p1); tri.Add(p0);
                uv.Add(uc); uv.Add(u1); uv.Add(u0);
            }
            return FinishMesh("WardHexMesh", tri, uv);
        }

        /// <summary>透明材质：URP 粒子 → URP Unlit → Sprites → 旧版粒子，逐级兜底。
        /// 与 `TownDust.MakeMaterial` 同一条兜底链，避免"这个工程里哪个 shader 真的在"再猜一次。
        /// 贴图同时往 `_MainTex`（内置/Sprites）与 `_BaseMap`（URP）上挂，并且**两边都先 `HasProperty` 探一遍**
        /// ⇒ 换 shader 也不会因为挂错名字而静默丢纹理 ✓</summary>
        private static Material MakeMaterial(Texture2D tex, string matName)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) { ShaderName = "(无)"; return null; }

            ShaderName = sh.name;
            var m = new Material(sh) { name = matName };
            _hasColor = m.HasProperty("_Color");
            _hasBaseColor = m.HasProperty("_BaseColor");
            if (_hasColor) m.SetColor("_Color", Color.white);
            if (_hasBaseColor) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // URP：Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);       // URP：Alpha
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);

            if (tex != null)
            {
                _hasMainTex = m.HasProperty("_MainTex");
                _hasBaseMap = m.HasProperty("_BaseMap");
                if (_hasMainTex) m.SetTexture("_MainTex", tex);
                if (_hasBaseMap) m.SetTexture("_BaseMap", tex);
            }

            m.renderQueue = 3000;
            return m;
        }

        // ══════════════════════════════════════════════════════════════
        // 程序化贴图（**只生成一次**，静态缓存 + HideFlags.DontSave）
        // ★ 第五版：只有刀光这一张（256×32，三层纹理）★
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 刀光贴图 **256×32 · 三层纹理叠加的"磨过的刃面"**
        /// （第三版：不再是"斑马条纹"；第四版：64 → 128 加密度；★第五版：128 → **256×32**，
        ///   并加"磨痕 + 崩刃"两个图层，横向切 3 段供 26 片轮流取样）。
        ///
        /// ── 为什么是"三层"而不是"再调一次高光" ──────────────────────
        /// 上一版只有高光 + 羽化 ⇒ 贴图在**明度上几乎是一条均匀的带**，256 宽的细节会读不出来。
        /// 用户这次要的正是"看得出纹理"，所以三层各自负责一种**可辨的视觉证据**：
        ///   · **层 A 刃面高光**（明暗）：2 道主高光（强度 1.00 / 0.80，半宽 4.5px / 2.0px）
        ///     + 1 道**很淡的第三道**（强度 `BladeStreakFaint` = 0.12，半宽 3.0px）；
        ///     每道高光的**外侧界**上仍压 **1px 锐边**（第四版做法原样保留 —— 磨过的刃口在光下是一条亮线）。
        ///   · **层 B 磨痕**（明暗 + 极细）：`GrindMarkCount`=**26** 条沿 X 的随机短线，
        ///     长 **5~18px**、α **0.10~0.22**、1px 高；位置/长度/α 全由**固定种子 xorshift32** 决定
        ///     ⇒ 像砂轮磨出的丝纹，且**每次生成字节相同**（不是 `UnityEngine.Random`）。
        ///   · **层 C 崩刃/缺口**（轮廓）：`NotchCount`=**3** 处不规则小缺口，
        ///     从 **v 高的一侧（= 外缘 / 刃口）** 咬进去，`α` **直接归 0** ⇒ 刃口不完美。
        ///     这是"有纹理"最有效的一笔：连续性被打破，眼睛立刻读到"这是一把用过的刃"。
        ///
        /// ── 坐标与三个图层为什么都落在"段内坐标"上 ───────────────────
        /// 贴图横向切成 `BladeTexSections`=**3** 段（段宽 `secW`=85px），每份刀光材质只看其中一段。
        /// 层 A 在**三个段里同构** ⇒ 每片都能看到完整的"高光 + 锐边"，弧的整体亮度不会因为分段而忽明忽暗；
        /// **逐片差异由层 B / 层 C 提供** —— 它们按**整图绝对 x**（带环绕）布点，
        /// 三个段各自的磨痕/缺口组合互不相同 ⇒ 相邻片（i%3）看到的是不同的纹理 ✓
        ///
        /// ── 其余细节 ───────────────────────────────────────────────
        /// · **Y 方向两端羽化**：上下各 **25%** 由 0 → 1 渐显（`smoothstep`）⇒ 片的径向两缘化开，
        ///   不再有硬直边（这是"薄带"不显得像纸片的关键）。★ 羽化位置/比例**没动** ✓
        ///   羽化仍是**唯一的权威**：层 B 的 α 也乘 `feather` ⇒ 磨痕不会把已经化开的
        ///   径向两端又抬成硬边（那会让片看起来像贴纸）✗
        /// · 层 C 的咬入深度 0.10~0.45（v 单位）⇒ 缺口能咬进羽化结束后的**实心区**（v ≤ 0.75），
        ///   否则它咬的全是本来就 α≈0 的地方，等于没画
        /// · 方块默认 UV 每个面都是 0..1；X = 沿弧方向的弦长，顶面（俯视相机主要看到的那面）
        ///   的 v 沿**径向**走 ⇒ 缺口咬在 v 高的一侧就是"外缘崩刃" ✓
        /// · 只跑一次：静态缓存，第二次调用直接返回（`SetPixel` 那 8192 次也只跑一次）✓
        /// </summary>
        private static Texture2D BladeTexture()
        {
            if (_bladeTex != null) return _bladeTex;

            // 128×16 → **256×32**：细节（磨痕 / 缺口 / 锐边）才有地方落
            const int W = BladeTexW, H = BladeTexH;
            const float FeatherBand = 0.25f;      // 上下各 25% 羽化（★ 第五版未动 ★）
            int secW = W / BladeTexSections;      // 85：一个 UV 段的像素宽
            // 段内 1px 的 u 宽度：锐边按"**恰好 1 像素**"收边，所以这个数必须由段宽算出来
            float pxU = secW <= 1 ? 1f : 1f / (secW - 1);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.name = "AttackVfxBladeEdge";
            tex.hideFlags = HideFlags.DontSave;      // 不进资产库、不保存到磁盘
            tex.wrapMode = TextureWrapMode.Repeat;   // 流动靠 wrap
            tex.filterMode = FilterMode.Bilinear;

            // ── 层 B（磨痕）/ 层 C（崩刃）先算进两张小表，再与层 A 合成 ──────────
            // 为什么要中间表：三层要在**同一像素**上叠加（B 抬 α/亮度、C 把 α 归零），
            // 而写进 `Texture2D` 之后就读不回来了。这两张表只在这一次生成里存在，
            // 之后不再分配任何数组 ✓
            float[] grind = new float[W * H];
            bool[] bitten = new bool[W * H];
            uint seed = BladeTexSeed;

            // ── 层 B：磨痕 26 条 ──────────────────────────────────────
            for (int m = 0; m < GrindMarkCount; m++)
            {
                float r0 = NextUnit(ref seed), r1 = NextUnit(ref seed);
                float r2 = NextUnit(ref seed), r3 = NextUnit(ref seed);
                int x0 = (int)(r0 * (W - 1));            // 起点 x（环绕右边界）
                int len = 5 + (int)(r1 * 14f);           // 长度 5 ~ 18 px
                // y 只落在羽化区**内部**（约 v 0.13~0.87）：最外两行的 α 已被羽化压到 0，
                // 画在那里等于白画（条数会浪费在看不见的地方）
                int yy = (H / 8) + (int)(r2 * (H - H / 4 - 1));
                float a = 0.10f + r3 * 0.12f;            // α 0.10 ~ 0.22
                for (int k = 0; k < len; k++)
                {
                    int xx = (x0 + k) % W;               // 环绕 ⇒ 跨段连续，不会有断头
                    int gi = yy * W + xx;
                    if (a > grind[gi]) grind[gi] = a;    // 重叠取亮的那条（叠亮会糊成一块白）
                }
            }

            // ── 层 C：崩刃 3 处（从 v 高的一侧 = 外缘咬进去，α 归 0）──────
            // ★ 一处**按段分配**（`n % BladeTexSections`）而不是纯随机 ★
            //   纯随机撒 3 处时实测有三个段只有两段吃到缺口（`x[85,171)` 段 0 个像素）——
            //   而 26 片是 `i%3` 轮流取样的 ⇒ 会有 1/3 的片永远看不到崩刃，等于白做，
            //   而且"崩刃"是最有效的纹理证据，更不能缺席 ⇒ 每段保底一处，**段内位置仍由哈希决定**
            //   ⇒ 三段各一处、彼此位置不同，相邻片看到的缺口位置也不同 ✓
            for (int n = 0; n < NotchCount; n++)
            {
                float r0 = NextUnit(ref seed), r1 = NextUnit(ref seed);
                int sec = n % BladeTexSections;
                int cx = sec * secW + (int)(r0 * (secW - 1));   // 缺口中心 x（钉在自己的段内）
                int half = 2 + (int)(r1 * 5f);           // 半宽 2~6 px ⇒ 缺口宽 5~13 px
                for (int dx = -half; dx <= half; dx++)
                {
                    int xx = ((cx + dx) % W + W) % W;
                    float t = 1f - Mathf.Abs(dx) / (float)(half + 1);      // 中间深、两端浅
                    float jag = 0.55f + 0.45f * NextUnit(ref seed);        // 逐列锯齿 ⇒ 不规则
                    float depth = Mathf.Lerp(0.10f, 0.45f, t * jag);       // 咬入深度（v 单位）
                    // v ≥ 1−depth 的像素被吃掉：`RoundToInt` 的显式 (int) 转换是给桩工程用的，
                    // 真 Unity 的 `Mathf.RoundToInt` 返回 int，多一次转换也无害 ✓
                    int vCut = (int)Mathf.RoundToInt((1f - depth) * (H - 1));
                    for (int yy = vCut; yy < H; yy++) bitten[yy * W + xx] = true;
                }
            }

            // ── 层 A：高光（段内坐标，三个段同构）──────────────────────
            // 中心位置（段内 u）/ 半宽（按**像素**给再换算）/ 强度
            // （数组长度直接用 `BladeStreaks` ⇒ 道数与常量**编译期**对齐，改常量忘改数组会立刻报错）
            float[] centers = new float[BladeStreaks] { 0.26f, 0.66f, 0.90f };
            float[] halfW = new float[BladeStreaks] { 4.5f * pxU, 2.0f * pxU, 3.0f * pxU };
            float[] strength = new float[BladeStreaks] { 1.00f, 0.80f, BladeStreakFaint };

            for (int y = 0; y < H; y++)
            {
                float v = H <= 1 ? 0.5f : y / (float)(H - 1);          // 0..1：径向（内缘→外缘）
                float edge = Mathf.Min(v, 1f - v);                     // 0（两端）→ 0.5（中间）
                float feather = Mathf.Clamp01(edge / FeatherBand);
                feather = feather * feather * (3f - 2f * feather);     // smoothstep：两端羽化

                for (int x = 0; x < W; x++)
                {
                    // ★ 第五版：改用**段内** u —— 每份材质只看贴图的一段，
                    //   于是"高光在哪"要按段算，层 A 才是三个段同构的
                    int xl = x % secW;
                    float u = secW <= 1 ? 0f : xl / (float)(secW - 1);
                    float streak = 0f, core = 0f, rim = 0f;
                    for (int s = 0; s < centers.Length; s++)
                    {
                        float d = Mathf.Abs(u - centers[s]);
                        if (d > 0.5f) d = 1f - d;                      // 环形距离 ⇒ wrap 无接缝
                        float t = Mathf.Clamp01(1f - d / halfW[s]);
                        float body = t * t * strength[s];              // 中心粗、两侧细（第三道很淡）
                        float sharp = Mathf.Clamp01(1f - d / (halfW[s] * 0.45f)) * strength[s];
                        if (body > streak) streak = body;
                        if (sharp > core) core = sharp;
                        // 磨刃锐边 —— 在第 s 道高光的**外侧界**（d = halfW[s]）上压 1px
                        // `Clamp01(1 − |d − halfW| / pxU)`：只有落在那一像素里的采样点才亮 ⇒ 宽度恒为 1px
                        float dEdge = Mathf.Abs(d - halfW[s]);
                        float sharpEdge = Mathf.Clamp01(1f - dEdge / pxU) * strength[s];
                        if (sharpEdge > rim) rim = sharpEdge;
                    }

                    // 高光之外留一层很淡的底光（0.18），免得两道光之间是纯黑的黑洞。
                    // 锐边同时抬 alpha 与亮度（它是"刃口反光"，本来就该比高光更亮更实）
                    float a = feather * Mathf.Clamp01(0.18f + 0.82f * Mathf.Max(streak, rim));
                    float l = Mathf.Clamp01(0.55f + 0.28f * streak + 0.17f * core + 0.22f * rim);

                    int idx = y * W + x;
                    // ── 叠层 B：磨痕乘法 = α 加上去、亮度也抬一点（"白色、靠亮暗表达"）──
                    // 乘 `feather` 是让羽化保持权威（见方法注释），不破坏径向化开的边
                    float gm = grind[idx] * feather;
                    if (gm > 0f)
                    {
                        a = Mathf.Clamp01(a + gm);
                        l = Mathf.Clamp01(l + gm * 0.85f);
                    }
                    // ── 叠层 C：崩刃 —— α 直接归 0（缺口是"没有东西"，不是"暗一点"）──
                    if (bitten[idx]) a = 0f;

                    tex.SetPixel(x, y, new Color(l, l, l, a));
                }
            }
            tex.Apply(false, false);
            _bladeTex = tex;
            return _bladeTex;
        }

        /// <summary>
        /// ★ 第五版新增：把一份刀光材质钉在贴图的第 `section` 段上 ★
        ///   `_MainTex`（内置/Sprites）与 `_BaseMap`（URP）**各自先探一遍**再设
        ///   ⇒ 换 shader 不会因为挂错名字而静默丢"分段"，也不会刷警告 ✓
        /// 段宽 = `1/BladeTexSections`，起点 = `section/BladeTexSections`；
        /// 运行期再由 `ScrollBladeUv` 在这个起点上叠流动相位（wrap 起来无缝）✓
        /// </summary>
        private static void ApplyBladeUvWindow(Material m, int section)
        {
            if (m == null) return;
            float scaleX = 1f / BladeTexSections;
            float offX = (section % BladeTexSections) * scaleX;
            var scale = new Vector2(scaleX, 1f);
            var offset = new Vector2(offX, 0f);
            if (m.HasProperty("_MainTex"))
            {
                m.SetTextureScale("_MainTex", scale);
                m.SetTextureOffset("_MainTex", offset);
            }
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTextureScale("_BaseMap", scale);
                m.SetTextureOffset("_BaseMap", offset);
            }
        }

        /// <summary>
        /// ★ 第五版新增：固定种子的 `xorshift32` ⇒ 返回 `[0,1)` ★
        /// 为什么不用 `UnityEngine.Random`：那个是**全局有状态**的流，回调顺序一变（比如别的系统
        /// 多抽一次）图就变；而这张贴图是"一次生成、整个会话复用"的静态缓存，
        /// 必须**可复现**（截图对比、回放都得是同一张图）⇒ 自带种子、纯函数式的哈希 ✓
        /// </summary>
        private static float NextUnit(ref uint s)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFFu) * (1f / 16777216f);
        }

        /// <summary>
        /// ★ v310：**一段刃痕在段内位置 `u` 处的径向厚度（米）**。
        ///   `thick(u) = GiantBladeThick(0.20R) × belly(u) × (1−u)^0.90`，其中
        ///   `belly(u) = 0.30 + 0.70·min(1, 4u)` ⇒
        ///     u=0.1 → **0.106R**（刃根，细颈）｜u=0.3 → **0.145R**（鼓腹，最厚）
        ///     u=0.5 → **0.107R**｜u=0.7 → **0.068R**｜u=0.9 → **0.025R**（刃尖，收到一点）
        ///   ⇒ 形状是"薄颈 → 鼓腹 → 长尖"，这是"刃"的轮廓；等宽弧带没有这个起伏 ✗
        ///   下限 `GiantThickMinRatio`(0.010R)：刃尖不能是 0，否则最后一片整块消失。
        /// 纯函数、无状态、无随机数 ⇒ 同一刀每次画出来逐片一致 ✓
        /// </summary>
        private static float GiantThickness(float u, float range)
        {
            float uu = Mathf.Clamp01(u);
            float belly = 0.30f + 0.70f * Mathf.Clamp01(uu * 4f);
            float taper = Mathf.Pow(1f - uu, 0.90f);
            return Mathf.Max(GiantThickMinRatio * range, GiantBladeThick * range * belly * taper);
        }

        /// <summary>
        /// ★ v310：**感电电弧折点的固定抖动** ∈ [−1, 1]。
        ///   为什么不用 `UnityEngine.Random`：电弧是"这一刀的一部分"，同一刀重放（调试挥击 /
        ///   回放）应该长同一个样子；而且这里只需要"看起来不整齐"，不需要真随机 ✓
        ///   三段整数哈希（与 `SpawnMudSplash` 的 `(i×47)%360` 同一种写法，但取值落在 ±1）
        /// </summary>
        private static float ShockArcJitter(int i)
        {
            int h = (i * 73856093) ^ 0x3F1A2B;
            h ^= h >> 13;
            h *= 1274126177;
            int m = h & 0xFFFF;                       // 0..65535
            return (m / 32767.5f) - 1f;               // → [−1, 1]
        }

        // ══════════════════════════════════════════════════════════════
        // 三种攻击 → 三组特效（只读事件字段，不写回任何东西）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// **锄头挥击** —— `MeleeSwingEvent`（发布点：`RunSession.DoHoeSwing` / `DebugSwing`）。
        ///   ① 刀光：**26 片窄刀光弧**（第四版；18 → 26 只提精度，**不动覆盖范围**）
        ///      · 张角仍只铺整条判定弧的 **60%**（`ArcCover`，未改）、**以瞄准方向为中心**
        ///        ⇒ 视觉上是一道偏正前方的劈痕，而不是盖满 ±48° 的一整块饼 ✓
        ///      · 径向仍是 **0.78R → R 的薄带**（外径恒为 R，**未改**），
        ///        但厚度**随 u 变**：中间 1.25×（0.275R）、两端 0.75×（0.165R）⇒ 更像刃 ✓
        ///      · 每片沿切向的弦长用 **`2·sin(Δ/2)` 精确算**（不再是 `× 1.34` 的经验系数）
        ///        ⇒ 相邻两片端点恰好接上，不会露缝 ✓
        ///      · 内缘低透明、外缘高透明（径向上随 `growth` 由 `Col` 插到 `ColB`，**未改**）
        ///      · **逐片扫过淡出**：第 i 片在 `u×SweepSpan` 时刻被扫到，之后只在 `SweepFade`=0.06s 内淡掉，
        ///        曲线从 `1−k²` 换成 **EaseOut `1−k³`** ⇒ 前段几乎不掉、末段干净收掉 ✓
        ///   ①b **内缘亮线**（第四版新增）：0.012R 宽、α 0.85、`Lerp(white, fx.Core, 0.55)`，
        ///      压在刀光片的内缘上、沿同一弧铺、同一套扫过淡出 ⇒ "刀刃反光"，轮廓更锐 ✓
        ///   ② 火星：8 颗，从挥击点**向前上方**抛出，重力 12，0.35s 消失
        ///   ①c ★ 第五版：每一片按 `i % BladeTexSections` 选一份**刀光材质** ⇒ 相邻片取样贴图的
        ///      不同横向段 ⇒ 磨痕（26 条）、缺口（3 处）、高光锐边**逐片不同**，
        ///      "纹理在变"肉眼可见。只换 `sharedMaterial` 指针，不 new 材质、不改池 ✓
        /// ★ 池账 ★ 每刀占 `Slice` 池 = 26（刀光）+ 26（亮线）= **52 ≤ 64** ✓
        ///   双击在**极高攻速**下可能让两刀重叠（间隔 = 0.2/攻速，攻速 2.0 时 0.1s &lt; 0.15s 寿命），
        ///   此时环形复用会回收**最老的那几片** —— 它们按 `delay` 早就淡完了，读数上看不见 ✓
        /// </summary>
        public static void SpawnSwing(in MeleeSwingEvent e)
        {
            if (!_ready) return;
            Swings++;

            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Hoe);

            float range = Mathf.Max(0.8f, e.Range);
            float arcDeg = Mathf.Max(30f, e.ArcDeg);
            float halfArc = arcDeg * 0.5f;
            Vector2 dn = e.Dir.sqrMagnitude > 1e-4f ? e.Dir.normalized : Vector2.up;
            float yawDeg = Mathf.Atan2(dn.x, dn.y) * Mathf.Rad2Deg;   // 与 `WorldView.OnMeleeSwing` 同一套角度约定

            // ══════════════════════════════════════════════════════════════════
            // ★★ v304：**巨型 = 整圈**（用户需求：「巨型…特效也没有变成环形」）★★
            //
            // ── 为什么是一个 **if 分支**，而不是"把半圆参数放大" ──
            //   半圆那一套（26 片铺 `半弧 × ArcCover`、从 `-halfVis` 扫到 `+halfVis`）
            //   在张角 360° 时会退化成"两片重叠在背后、中间一大段没有片" ✗ ——
            //   弦长公式 `2·sin(Δ/2)` 的前提是"相邻片角间距 = 视觉张角/(片数−1)"，
            //   而整圈要求**首尾闭合**（角间距 = 视觉张角/片数）。两条不同的几何，
            //   硬合并就是"一份代码两套语义"⇒ 分开写，各写各的 ✓
            //
            // ── ★ v306：整圈**一次性同时亮起**（用户：「巨型不应该出现一个回响一样的特效」）──
            //   · 旧写法（v304）把 26 片刻意错峰：第 i 片在 `SweepSpan×i/(N−1)` 才点亮、
            //     只亮 `SweepFade`=0.06s ⇒ 一圈光**沿着圆转过去**。
            //     那正是**回响（`hoe_echo`）的视觉签名**：同一个形状隔一会儿再出现一次 ✗
            //     ⇒ 用户读到"巨型打出了一个回响"，是**读对了**：我们画的确实是一次次重影。
            //   · 现在的形态：环带 + 内外描边 + 地面投影环，全部 `Age = 0` 同刻亮起、
            //     `GiantRingLife`=0.30s、`Hold=true`（`1−k⁴`）整体淡出 ⇒ 只在**一次**出现。
            //     详细几何与池账见下面 `if (ring)` 那段（新路径独立成块，不再借用半圆循环）。
            //   ⚠️ **半圆的循环在 `ring` 时执行 0 次**（`halfCount`），循环体一个字符都没改 ✓
            //   ⚠️ 半径仍是 `0.78R→1.00R` 那一档的外沿 ⇒ 与判定形状
            //     （`MeleePointHit` 的 `fullRing`：内径 = 外径×0.35、无角向判据）一致：
            //     **看得见的环一定落在打得到的环里面** ✓
            // ══════════════════════════════════════════════════════════════════
            bool ring = e.FullRing;

            // ★ 第三版：只铺 60% 的弧，且**对称于瞄准方向** ⇒ 偏 ±0.60×halfArc
            // ★ v304：整圈（巨型）时铺满 2π，不再有"±"这个概念
            float halfVis = ring ? 180f : halfArc * ArcCover;
            float arcRad = ring ? Mathf.PI * 2f : arcDeg * ArcCover * Mathf.Deg2Rad;   // 视觉张角（弧度）

            // 底色仍是"近白 + 只掺 30% 插件本体色"（规格要"白色刀光"，且不把整条弧染成插件色）。
            // 渐变的两端**全部从 `fx.Rim` / `fx.Core` 派生**，不写死任何颜色 ⇒ 装了插件颜色依然成立 ✓
            Color white = Color.Lerp(Color.white, fx.Core, 0.30f);
            white.a = 1f;

            var origin = new Vector3(e.Pos.x, SwingY, e.Pos.y);
            // ── 弦长：**精确** `2·sin(Δ/2)`（不再是 `arcRad/N × 1.34` 的经验系数）──
            // Δ = 相邻两片的角间距 = 视觉张角 / (片数 − 1)。
            // 片的 X 尺寸 = 当前半径 × chord（`Step` 里乘），所以 chord 取"单位圆上的弦长"
            // ⇒ 相邻两片的**端点恰好接上**，不需要任何放大系数，也不会因为放大而互相盖住 ✗
            float dAng = ring
                ? Mathf.PI * 2f / ArcSlices                    // 整圈：角间距 = 2π/片数 ⇒ **首尾闭合**
                : arcRad / Mathf.Max(1, ArcSlices - 1);        // 半圆：两端不重叠
            float chord = 2f * Mathf.Sin(dAng * 0.5f);
            float bandR1 = range * BladeR1;                       // 1.00R：薄带外径（外沿由 Depth 再补半个）
            float radialThick = range * BladeR1 - range * BladeR0; // 0.22R：**基准**径向厚度（随 u 变）
            float bandD = Mathf.Max(0.05f, range * 0.10f);         // 每一时刻的径向厚度（薄带自身的那 10%）
            // ★ v273：`0.10` 是**比例**（镜像常量 `RunSession.MeleeFxSliceDepthRatio`），
            //   `range` 已经按 0.90 缩小过（3.8 → 3.42 ⇒ 这里 4.090R → 3.729R）⇒ **不许**再乘 0.90 ✗
            //   片的**外沿** = `bandR1 + bandD/2` = 1.05R；判定外径 = 1.05R × 1.15（这段关系本轮一个数没动）
            Color edgeCol = Color.Lerp(Color.white, fx.Core, EdgeLineMix);   // 内缘亮线：白 → 插件本体色
            edgeCol.a = EdgeLineAlpha;                                       // 0.85
            float edgeDepth = Mathf.Max(0.004f, range * EdgeLineWidth);      // 0.012R：极细

            // ★ v306：**整圈不再借用这段循环**（它铺的是半弧、错峰点亮）。
            //   `ring` 时 `halfCount = 0` ⇒ 下面整块**一次都不执行**，
            //   循环体因此可以一个字符不改 ⇒ 普通挥击的外观**保证**与 v305 逐像素一致 ✓
            //   （这是本轮"只动巨型"那条纪律的物理实现：不是"改了但读起来一样"，是"根本没跑"。）
            int halfCount = ring ? 0 : ArcSlices;
            for (int i = 0; i < halfCount; i++)
            {
                // ★ 第五版：这一片用哪一份刀光材质 = 它看贴图的哪一段 ★
                //   `i % BladeTexSections` ⇒ **相邻两片**取样的是贴图里**不同的一段**，
                //   于是磨痕/缺口逐片不同，肉眼能看到"纹理在变" ✓
                //   （只换 `sharedMaterial` 指针：不 new 材质、不变池规模、不破"零实例化"）
                Material sliceMat = (_bladeMats != null && _bladeMats.Length > 0)
                                  ? _bladeMats[i % _bladeMats.Length] : null;
                // u：0 = 起始角，1 = 结束角（沿弧方向）
                float u = ArcSlices <= 1 ? 0.5f : i / (float)(ArcSlices - 1);
                // ★ v304：整圈 ⇒ 均匀铺满 2π（`i × 360/片数`，末片与首片正好接上）；
                //   半圆 ⇒ 从 `-halfVis` 扫到 `+halfVis`（**逐字不变**）
                float ang = ring
                    ? (yawDeg + i * (360f / ArcSlices)) * Mathf.Deg2Rad
                    : (yawDeg + Mathf.Lerp(-halfVis, halfVis, u)) * Mathf.Deg2Rad;
                float delay = SweepSpan * u;

                // ── 径向厚度：**中间厚、两端薄**（±25%，sin 包络）──
                // 厚度(u) = 0.22R × (1 + 0.25 × (2·sin(πu) − 1)) ⇒ 0.275R（中）～ 0.165R（端）
                // ★ 只动**内径**：R1（外径）恒定 = R ⇒ "不许改外径"这条硬约束仍然成立 ✓
                float thick = radialThick * (1f + BladeThickSwing * (2f * Mathf.Sin(u * Mathf.PI) - 1f));
                float rIn = Mathf.Max(0f, bandR1 - thick);

                Piece p = Take(PoolKind.Slice);
                if (p == null) break;
                if (sliceMat != null) p.Mr.sharedMaterial = sliceMat;   // ← 第五版：这一段 UV
                p.Mode = ModeRadial;
                p.Origin = origin;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = rIn;                        // 内径（随 u 变：中段更深、两端更浅 = 月牙的刃背）
                p.R1 = bandR1;                     // 外径（恒定 = R，不许改）
                // ★ 第四版：弦长改成精确值 ⇒ 已经**首尾相接**，不再乘 0.68~1 的月牙包络
                //   （那个包络会把端点的弦长压到角间距以下，正是"露出缝隙"的原因）。
                //   月牙的形状改由**径向厚度**表达（上一段），这才是"刃"该有的变化 ✓
                p.Chord = chord;
                p.Thick = 0.06f;                   // 竖直方向更薄 ⇒ 读作"刃光"而不是"一块板"
                p.Depth = bandD;

                // ── 径向渐变 + 内低外高的透明度 ────────────────────────
                // 色相：沿弧从 `fx.Rim` 侧让位给 `fx.Core` 侧（保留第二版的双轴调色）。
                // 透明度：内缘 0.15、外缘 0.95 —— 用户要求"内缘 α 低、外缘 α 高"
                //   ⇒ 薄带的内侧化开、外侧才是那道亮刃口 ✓
                Color innerC = Color.Lerp(white, fx.Rim, Mathf.Lerp(0.62f, 0.34f, u));
                Color outerC = Color.Lerp(white, fx.Core, Mathf.Lerp(0.30f, 0.62f, u));
                innerC.a = 0.15f;
                outerC.a = 0.95f;
                p.Col = innerC;                    // growth = 0 时的颜色（内缘）
                p.ColB = outerC;                   // growth = 1 时的颜色（外缘）
                p.Grad = true;                     // ★ 只有这里打开：其余旧调用点 ColB 默认黑也安全
                p.Sweep = true;                    // ★ 第四版：淡出走 EaseOut（`1 − k³`）★

                // 中部亮、两端暗：读作"刀锋扫过"，不是"一根均匀的灯管"
                float mid = 0.55f + 0.45f * (1f - Mathf.Abs(u - 0.5f) * 2f);
                p.A0 = 0.92f * mid;

                // ── 逐片扫过：第 i 片在 u×SweepSpan 时刻被扫到，之后只在 SweepFade 内淡掉 ──
                // 负 Age = "还没轮到它"（`Step` 对 Age<0 输出 alpha 0 ⇒ 不会提前亮）；
                // Life 只有 0.06s，且最后一片（u=1）的死亡时刻 = SweepSpan + SweepFade = ArcLife
                // ⇒ 总时长仍精确等于 ArcLife，而各片**不同时收尾** ✓
                p.Life = SweepFade;
                p.Age = -delay;
                Activate(p);

                // ── ①b 内缘亮线（第四版新增）──────────────────────────
                // 一条**独立**的、极细的亮线，正好压在刀光片的**内缘**上（rIn 就是那个半径）：
                //   · 宽 0.012R（只有薄带厚度的 5%）、α 0.85、颜色 `Lerp(white, fx.Core, 0.55)`
                //   · 与刀光片同角、同弦长、同 delay ⇒ 一起被"扫过"，不会自己先亮或先灭
                //   · `R0 == R1` ⇒ 不张开（它是一条轮廓，不是一片会长的面）
                // 它只加一条反光轮廓：外径（R1）、张角（halfVis）、配色来源都没动 ✓
                Piece q = Take(PoolKind.Slice);
                if (q == null) break;
                if (sliceMat != null) q.Mr.sharedMaterial = sliceMat;   // 与它那片刀光同一段 UV
                q.Mode = ModeRadial;
                q.Origin = origin;
                q.Angle = ang;
                q.YawDeg = ang * Mathf.Rad2Deg;
                q.R0 = rIn;
                q.R1 = rIn;
                q.Chord = chord;
                q.Thick = EdgeLineThick;           // 0.05
                q.Depth = edgeDepth;               // 0.012R
                q.Col = edgeCol;
                q.Grad = false;                    // 单色：α 直接取 edgeCol.a = 0.85
                q.Sweep = true;
                q.A0 = 1f;
                q.Life = SweepFade;
                q.Age = -delay;
                Activate(q);
            }

            // ══════════════════════════════════════════════════════════════════
            // ★★ v310：**巨型 = 8 段分离的刃痕**（用户：需要剑刃感、不是完整的环、颜色别太艳）
            //    ⚠️ 下面这段注释是 v308 的历史说明，**已被 v310 替代**：
            //      v308 的做法是"把半圆那 26 片首尾相接铺满 2π"⇒ 一个**连续实心环带**，
            //      用户看到的就是"一个金箍" ⇒ v310 改成 8 段 × 30°、段间空 15°（详见常量段）✓
            //
            // ── 与 v308 一致、本轮**没动**的三条 ──
            //   · `Age = 0` 同刻亮起（不逐片扫过 ⇒ 无重影、不像回响）
            //   · `GiantRingLife` = 0.30s
            //   · `Hold`（`1 − k⁴`）整体淡出 + 地面投影环保留
            //
            // ── 池账（`PoolPerKind` = 64）──
            //   `Slice`：刃痕 8×5 = 40 + 白芯 8×2 = 16 ⇒ **56 ≤ 64** ✓
            //   `Bit`  ：地面环 32 + 火星 ≤10 + 灼烧 6 + 感电 12 ⇒ **≤ 60 ≤ 64** ✓
            //
            // ── ⚠️ 与"回响（`hoe_echo`）"的区别仍然成立 ──
            //   回响 = 同一个图形隔 0.2~0.5s **再飘一次**（第二次、时间错开）；
            //   巨型 = 全部 `Age = 0` **同刻**亮起、只出现**一次**、`Hold` 整体淡出 ⇒ 永远没有第二次 ✓
            //   而 v310 又把"形状"彻底分开了：回响是同形状的重影，巨型是**八段断开的刃痕** ——
            //   即使两者的时间行为被看成一样，形状也不可能混 ✓
            // ══════════════════════════════════════════════════════════════════
            if (ring)
            {
                // ★ v310：本支**不再借用**半圆那套 `chord`（2π/26 的间距）/`radialThick`/`bandD`
                //   —— 8 段刃痕是另一套几何：段内 6° 的精确弦长、厚度由 `GiantThickness`
                //   沿切向收尖、外沿恒为 `bandR1`(1.00R)。半圆那三个数在**它自己的循环**里照旧用 ✓
                // ★ 配色**不吃插件**（v309 起就是这条纪律）：刃痕固定 `GiantBlade`，
                //   装不装插件、装哪件，巨型画出来都是同一个偏灰的金 ✓

                // 段内相邻两片的**精确弦长**（Δ = 6°）⇒ 同段内首尾相接、不露缝
                float subChord = 2f * Mathf.Sin(GiantSegPerDeg * Mathf.Deg2Rad * 0.5f);

                for (int gi = 0; gi < GiantArcCount * GiantSegPerArc; gi++)
                {
                    int a = gi / GiantSegPerArc;      // 第几段刃痕（0..7）
                    int i = gi % GiantSegPerArc;      // 段内第几片（0..4）
                    // 逐片轮换 UV 段（与半圆逐字相同）⇒ 相邻片看到的磨痕/缺口不同
                    Material sliceMat = (_bladeMats != null && _bladeMats.Length > 0)
                                      ? _bladeMats[gi % _bladeMats.Length] : null;
                    // u：0 = 刃根，1 = 刃尖（**段内**归一化，取片中心）
                    float u = (i + 0.5f) / GiantSegPerArc;
                    // 角度 = 本段起点 + 段内偏移；段起点按 `GiantArcStepDeg`(45°) 递进
                    // ⇒ 8 段铺满 2π，每段 30°、段间空 15° ✓
                    float ang = (yawDeg + a * GiantArcStepDeg + (i + 0.5f) * GiantSegPerDeg) * Mathf.Deg2Rad;
                    // 径向厚度：根部薄 → 中前段最厚 → 刃尖收成一点（见 `GiantThickness`）
                    float thick = GiantThickness(u, range);
                    float rMid = bandR1 - thick * 0.5f;    // 片心半径：外沿恒压在 1.00R 上

                    // ① 刃痕片：`R0 == R1` ⇒ **静止**（它是一道留下的痕，不是一个向外张开的面）
                    Piece p = Take(PoolKind.Slice);
                    if (p == null) break;
                    if (sliceMat != null) p.Mr.sharedMaterial = sliceMat;
                    p.Mode = ModeRadial;
                    p.Origin = origin;
                    p.Angle = ang;
                    p.YawDeg = ang * Mathf.Rad2Deg;
                    p.R0 = rMid;             // 静止（不外滑）
                    p.R1 = rMid;
                    p.Chord = subChord;      // 6° 的精确弦长 ⇒ 同段内相接
                    p.Thick = 0.06f;         // 与半圆同厚（竖直）
                    p.Depth = thick;         // 径向厚度：根部 0.20R → 刃尖 0.01R
                    p.Col = GiantBlade;      // ★ 偏灰的金（不读 fx：这一招固定这个色）
                    p.ColB = GiantBlade;
                    p.Grad = false;          // 单色（刃面的高光/磨痕/缺口由 `sliceMat` 提供）
                    p.Sweep = false;         // ★ 不逐片扫过（扫过 = 回响/转一圈的签名）
                    p.Hold = true;           // ★ 1 − k⁴：前段咬住、末段整体收掉
                    p.A0 = GiantAlphaPeak;   // 0.80（原 0.95）
                    p.Life = GiantRingLife;
                    p.Age = 0f;              // ★★ 同刻亮起 ⇒ 一次性、无重影
                    Activate(p);
                    GiantBlades++;

                    // ② 刃口**白芯**：只在每段靠内的 `GiantCorePerArc`(2) 片上压一条极亮线 ——
                    //    那 12° 正好是刃身最厚的地方，白芯落在那里才读作"反光"而不是"划痕"。
                    //    半径压在 1.00R 内侧 `GiantCoreDepth`(0.016R) 处 ⇒ 它与刃口是同一条线 ✓
                    if (i < GiantCorePerArc)
                    {
                        Piece q = Take(PoolKind.Slice);
                        if (q != null)
                        {
                            if (_mat != null) q.Mr.sharedMaterial = _mat;   // 纯白不该被刃纹啃掉
                            float coreR = bandR1 - GiantCoreDepth * range * 0.5f;
                            q.Mode = ModeRadial;
                            q.Origin = origin;
                            q.Angle = ang;
                            q.YawDeg = ang * Mathf.Rad2Deg;
                            q.R0 = coreR;
                            q.R1 = coreR;                                   // 不张开（一条轮廓）
                            q.Chord = subChord;
                            q.Thick = 0.05f;
                            q.Depth = GiantCoreDepth * range;               // 0.016R：一条线
                            q.Col = GiantWhiteCore;
                            q.Grad = false;
                            q.Sweep = false;
                            q.Hold = true;
                            q.A0 = GiantCoreAlpha;
                            q.Life = GiantRingLife;
                            q.Age = 0f;
                            Activate(q);
                        }
                    }
                }

                // ═══ ③ ★ v310：**外缘暗金描边已删除** ═══
                //   原来这里是一圈**连续 16 片**的暗金描边（压在 1.02R）。用户要的是
                //   「不是完整的环」⇒ 一圈连续的外描边会把 8 段的断口**重新连成一个环** ✗
                //   刃口那条线改由每段自己的白芯（②）承担 ⇒ 断口处也真的断开 ✓
                //   顺带：`Bit` 池因此从 58 掉到 42，给灼烧火星（6）与感电电弧（12）留出了位置 ✓

                // ④ 地面投影环：贴地、更粗（0.045m）、α 0.34，
                //    覆盖判定的整条 0.4226R ~ 1.05R ⇒ 明确"这一圈地上**都**打得到" ✓
                //    ★ v310：颜色改成 **`GiantBlade × 0.50` = (0.46, 0.42, 0.31)**
                //      （原来跟着饱和金走是 (0.50,0.42,0.175)，偏黄；现在与刃痕同一个低饱和色相）✓
                //    ★ 它**仍然是一整圈**：用户点名"保留地面投影环" —— 它画的是"打得到的范围"，
                //      本来就该是闭合的；"不是完整的环"说的是头上那层刃光 ✓
                float gdAng = Mathf.PI * 2f / GiantGroundSlices;
                float gMid = range * (GiantGroundR0 + GiantGroundR1) * 0.5f;
                float gDepth = Mathf.Max(0.05f, range * (GiantGroundR1 - GiantGroundR0));
                // 弦长按**外径**算（再留 6% 重叠）⇒ 外沿不会出现缝隙；
                // 内沿因此重叠约 3 倍 ⇒ 内边界更实，正好强化"这里就是内圈"这个读法 ✓
                float gdChord = gdAng * (GiantGroundR1 / ((GiantGroundR0 + GiantGroundR1) * 0.5f)) * 1.06f;
                Color gCol = new Color(GiantBlade.r * GiantGroundDarken,
                                       GiantBlade.g * GiantGroundDarken,
                                       GiantBlade.b * GiantGroundDarken, 1f);
                gCol.a = 1f;
                var gOrigin = new Vector3(e.Pos.x, GroundY, e.Pos.y);
                for (int i = 0; i < GiantGroundSlices; i++)
                {
                    float a2 = (yawDeg + i * (360f / GiantGroundSlices)) * Mathf.Deg2Rad;
                    Piece g = Take(PoolKind.Bit);
                    if (g == null) break;
                    g.Mode = ModeRadial;
                    g.Origin = gOrigin;
                    g.Angle = a2;
                    g.YawDeg = a2 * Mathf.Rad2Deg;
                    g.R0 = gMid;
                    g.R1 = gMid;
                    g.Chord = gdChord;
                    g.Thick = GiantGroundThick;
                    g.Depth = gDepth;
                    g.Col = gCol;
                    g.Grad = false;
                    g.Sweep = false;
                    g.Hold = true;
                    g.A0 = GiantGroundAlpha;
                    g.Life = GiantRingLife;
                    g.Age = 0f;
                    Activate(g);
                }
            }

            // ══════════════════════════════════════════════════════════════════
            // ★★ v309：**刀光的白闪**（用户：「加一次极短的白闪（刃口最亮处 0.04 秒纯白）
            //    让切割感更锐」）★★
            //
            // ── 为什么只在**半圆**路径加、巨型不加 ──
            //   巨型那三层的规格是"26 片 + 同刻亮起 + 0.30s + 无重影 + 地面投影环"，
            //   它自己已经有一条**白芯**亮线（`GiantWhiteCore`）当"最亮的那条线"。
            //   再叠一层 0.04s 的纯白只会把白芯的层次抹平 ⇒ 巨型**不加白闪** ✓
            //
            // ── 形态 ──
            //   6 段 **纯白**（1,1,1）、贴在**刃口**（半径 = `bandR1` = 1.00R，刀光薄带的外沿）
            //   · 径向只有 `0.028m` 厚（是一条线，不是一片光）
            //   · 切向按 `chord × 4.3` 铺 ⇒ 6 段正好接满那 26 片铺开的同一条弧，不留缝
            //   · `Life = WhiteFlashLife`（0.04s）、`Sweep = true`（`1 − k³`）⇒ 一闪就没
            //   · 与刀光片**同刻**（`Age = 0`，不设负 Age）⇒ 先看到劈痕、白闪压在刃口上
            // 池账：6 格（`Slice`）× 0.04s ⇒ 半圆一刀 26 + 26 + 6 = **58 ≤ 64** ✓
            // ══════════════════════════════════════════════════════════════════
            if (!ring)
            {
                Color flash = Color.white;
                flash.a = 1f;
                float flashChord = chord * (float)((ArcSlices - 1) / WhiteFlashStripes);
                for (int i = 0; i < WhiteFlashStripes; i++)
                {
                    // 沿**同一条视觉弧**均匀摆 6 段（取段中点 ⇒ 首尾都不会多出半段）
                    float uf = (i + 0.5f) / WhiteFlashStripes;
                    float af = (yawDeg + Mathf.Lerp(-halfVis, halfVis, uf)) * Mathf.Deg2Rad;
                    Piece p = Take(PoolKind.Slice);
                    if (p == null) break;
                    if (_mat != null) p.Mr.sharedMaterial = _mat;   // 无贴图：纯白不该被刃纹啃掉
                    p.Mode = ModeRadial;
                    p.Origin = origin;
                    p.Angle = af;
                    p.YawDeg = af * Mathf.Rad2Deg;
                    p.R0 = bandR1;              // 刃口（薄带外沿）：最亮的那一条线就在这
                    p.R1 = bandR1;              // 不张开
                    p.Chord = flashChord;
                    p.Thick = 0.075f;           // 竖直比刀光片（0.06）厚一点点 ⇒ 白闪"压"在刃上
                    p.Depth = 0.028f;           // 径向极薄
                    p.Col = flash;
                    p.Grad = false;
                    p.Sweep = true;             // 1 − k³：一闪就收
                    p.Hold = false;
                    p.A0 = 1f;
                    p.Life = WhiteFlashLife;    // 0.04
                    p.Age = 0f;
                    Activate(p);
                    BladeFlashes++;
                }
            }

            // ── 火星：向前上方抛 ──
            int n = Mathf.Clamp(SwingSparks + fx.Sparks / 2, 6, 10);
            // ★ v309：巨型的火星跟着**主色**走（这一招固定色，火星不该是插件色）
            // ★ v310：主色从饱和金换成 `GiantBlade`(0.92,0.84,0.62) ⇒ 火星一起降饱和
            Color ember = ring ? Color.Lerp(GiantBlade, GiantWhiteCore, 0.40f)
                               : Color.Lerp(fx.Core, new Color(1f, 0.74f, 0.26f, 1f), 0.50f);
            ember.a = 1f;
            var dir3 = new Vector3(dn.x, 0f, dn.y);
            for (int i = 0; i < n; i++)
            {
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                SparksSpawned++;
                float side = UnityEngine.Random.Range(-0.75f, 0.75f);
                // ★ v304：整圈（巨型）⇒ 火星朝**四面八方**抛，并从**环带半径**上起飞。
                //   不这么做的话，"一圈环 + 一堆只往瞄准方向飞的星"会读成"背后其实没扫到" ✗
                //   半圆路径逐字不变（`fwd3 == dir3`、起飞半径仍是 0.10~0.45m）✓
                Vector3 fwd3 = dir3;
                float spawnR = UnityEngine.Random.Range(0.10f, 0.45f);
                if (ring)
                {
                    float ra = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    fwd3 = new Vector3(Mathf.Sin(ra), 0f, Mathf.Cos(ra));
                    spawnR = range * UnityEngine.Random.Range(0.72f, 1.00f);
                }
                var lateral = new Vector3(-fwd3.z, 0f, fwd3.x) * side;
                p.Mode = ModeBallistic;
                p.Grad = false;
                p.Pos = origin + fwd3 * spawnR;
                p.Vel = fwd3 * UnityEngine.Random.Range(1.6f, 3.4f)
                      + lateral * UnityEngine.Random.Range(0.4f, 1.2f)
                      + Vector3.up * UnityEngine.Random.Range(2.6f, 4.6f);   // 向上抛
                p.Gravity = SparkGravity;                                        // 12
                p.S0 = UnityEngine.Random.Range(0.055f, 0.105f);
                p.S1 = p.S0 * 0.25f;
                p.Rot0 = new Vector3(0f, 0f, 0f);
                p.Spin = new Vector3(UnityEngine.Random.Range(-540f, 540f),
                                     UnityEngine.Random.Range(-540f, 540f),
                                     UnityEngine.Random.Range(-540f, 540f));
                p.Col = ember;
                p.A0 = 1f;
                p.Life = SparkLife;                                             // 0.35
                p.Age = 0f;
                Activate(p);
            }

            // ══════════════════════════════════════════════════════════════════
            // ★★ v310：**灼烧 / 感电** 的挥舞粒子（用户：「增加灼烧特效表现，锄头挥舞附带
            //    略微燃烧粒子」「增加感电表现，锄头挥舞增加闪电粒子」）★★
            //
            // ── 门控（这一条最要紧：没装插件就**一个粒子都不加**）──
            //   `FxSpec.Kinds` 是 `PluginFx.For` 对 `WeaponModifiers` 算出来的位掩码，而它的
            //   第 `BurnOnHit` / `Shock` 位**只在**下面两个字段为正时才会被置上（`PluginFx.cs`）：
            //     · `m.BurnLayersPerHit > 0`（`hoe_burn`）⇒ `PluginEffectKind.BurnOnHit`
            //     · `m.ShockTargets   > 0`（`hoe_shock`）⇒ `PluginEffectKind.Shock`
            //   ⇒ 本层读的是"这一刀的外观规格里有没有这两档"，**不重新查一遍插件表**、
            //     也就不存在"两处判据不一致"的隐患 ✓
            //   为什么走 `FxSpec.Kinds` 而不是给 `MeleeSwingEvent` 加字段：外观规格本来就该
            //   由 `PluginFx` 那一处**唯一分派**产出，加字段等于把同一个判断复制到两个地方 ✗
            //
            // ── 池账（仍然只用 `Slice` / `Bit`，**不加新池**）──
            //   灼烧 6 颗 + 感电 4×3 = 12 段 ⇒ 最多再多占 `Bit` **18** 格。
            //   峰值组合（巨型 + 双插件）：地面环 32 + 火星 10 + 灼烧 6 + 电弧 12 = **60 ≤ 64** ✓
            //   普通挥击：火星 10 + 6 + 12 = **28 ≤ 64** ✓（`Slice` 那一侧一个格子都不占）
            // ══════════════════════════════════════════════════════════════════
            uint kinds = fx.Kinds;
            bool burnOn = (kinds & PluginFx.Bit(PluginEffectKind.BurnOnHit)) != 0u;
            bool shockOn = (kinds & PluginFx.Bit(PluginEffectKind.Shock)) != 0u;

            // ── ②a 灼烧：**少量**火星，橙红、朝挥击方向抛、带重力、0.25~0.4s 熄灭 ──
            //   "略微"这条是用户点名的：6 颗、速度 1.1~2.7 m/s、重力 9（比普通火星的 12 更沉，
            //   落得更快），尺寸 0.045~0.075m ⇒ 读作"刃口带出来的几点余烬"，不是一次爆炸 ✓
            if (burnOn)
            {
                Color fireCol = new Color(1.00f, 0.42f, 0.12f, 1f);   // 橙红
                for (int i = 0; i < SwingBurnEmbers; i++)
                {
                    Piece p = Take(PoolKind.Bit);
                    if (p == null) break;
                    BurnEmbers++;
                    // 固定比例的"抖动"（`i × 黄金比 mod 1`）⇒ 不引用 `UnityEngine.Random`、
                    // 同一刀每次都一样，画面上仍看得出散开（与 `SpawnMudSplash` 的写法同源）
                    float j1 = (i * 0.6180f) % 1f;
                    float j2 = (i * 0.4142f) % 1f;
                    // 巨型（整圈）时余烬沿**整圈**抛；半圆时只朝**瞄准方向**抛（与火星同规矩）
                    Vector3 fwd3 = dir3;
                    float spawnR = 0.20f + 0.55f * j1;
                    if (ring)
                    {
                        float ra = (i * (Mathf.PI * 2f / SwingBurnEmbers)) + 0.35f;
                        fwd3 = new Vector3(Mathf.Sin(ra), 0f, Mathf.Cos(ra));
                        spawnR = range * (0.72f + 0.26f * j1);
                    }
                    var lat = new Vector3(-fwd3.z, 0f, fwd3.x) * ((j2 - 0.5f) * 0.9f);
                    p.Mode = ModeBallistic;
                    p.Grad = false;
                    p.Sweep = false;
                    p.Hold = false;
                    p.Pos = origin + fwd3 * spawnR + Vector3.up * 0.04f;
                    p.Vel = fwd3 * (1.1f + 1.6f * j2)
                          + lat * 0.8f
                          + Vector3.up * (1.4f + 1.6f * j1);      // 只往上抛一点（不是火箭）
                    p.Gravity = BurnEmberGravity;                  // 9
                    p.S0 = 0.045f + 0.030f * j2;
                    p.S1 = p.S0 * 0.28f;
                    p.Rot0 = Vector3.zero;
                    p.Spin = new Vector3(0f, 0f, 0f);              // 余烬不转（省两次随机）
                    p.Col = fireCol;
                    p.A0 = 0.95f;
                    p.Life = BurnEmberLifeMin + (BurnEmberLifeMax - BurnEmberLifeMin) * j1;  // 0.25~0.40
                    p.Age = 0f;
                    Activate(p);
                }
            }

            // ── ②b 感电：**短促电弧**，4 条、每条 3 段极细折线，沿刀光弧分布，0.10s 闪灭 ──
            //   几何：每条电弧占视觉弧上的一个角位，由 3 根 `ModeAxis` 细条**首尾相接**成折线；
            //   折点半径在 `1.00R` 附近上下抖 ±0.035R（固定的三段哈希，不是随机数）
            //   ⇒ 读作"刃口上跳了一下电"，时间上只有 0.10s（`ShockArcLife`）⇒ 闪一下就没了 ✓
            if (shockOn)
            {
                Color arcCol = new Color(0.72f, 0.94f, 1.00f, 1f);      // 电青
                for (int b = 0; b < SwingShockBolts; b++)
                {
                    // 沿视觉弧均匀取 4 个角位（巨型取整圈，半圆取 ±halfVis）
                    float angDeg = ring
                        ? yawDeg + b * (360f / SwingShockBolts) + 12f
                        : yawDeg + Mathf.Lerp(-halfVis, halfVis, (b + 0.5f) / SwingShockBolts);
                    for (int s2 = 0; s2 < ShockArcSegs; s2++)
                    {
                        Piece p = Take(PoolKind.Bit);
                        if (p == null) break;
                        ShockBolts++;
                        // 折线的两个端点：角度沿切向推进 `ShockArcStepDeg`(4°)，半径按固定哈希抖 ±0.035R
                        //   （4° 在 3.7m 半径上 ≈ 26cm ⇒ 三段拼成一条 ≈78cm 的可见电弧；
                        //    0.9° 只有 5.8cm，画面上根本看不出来 ✗）
                        float a0 = (angDeg + (s2 - 1.5f) * ShockArcStepDeg) * Mathf.Deg2Rad;
                        float a1 = (angDeg + (s2 - 0.5f) * ShockArcStepDeg) * Mathf.Deg2Rad;
                        float jr0 = ShockArcJitter(s2 + b * 7) * ShockArcJitterAmp;
                        float jr1 = ShockArcJitter(s2 + 1 + b * 7) * ShockArcJitterAmp;
                        float r0 = range * (ShockArcRadius + jr0);
                        float r1 = range * (ShockArcRadius + jr1);
                        p.Mode = ModeAxis;
                        p.Grad = false;
                        p.Sweep = true;          // 1 − k³：闪一下就收
                        p.Hold = false;
                        if (_mat != null) p.Mr.sharedMaterial = _mat;
                        p.Origin = origin + new Vector3(Mathf.Sin(a0) * r0, 0f, Mathf.Cos(a0) * r0);
                        p.End = origin + new Vector3(Mathf.Sin(a1) * r1, 0f, Mathf.Cos(a1) * r1);
                        p.Thick = ShockArcWidth;
                        p.Col = arcCol;
                        p.A0 = 1f;
                        p.Life = ShockArcLife;   // 0.10
                        p.Age = 0f;
                        Activate(p);
                    }
                }
            }
        }

        /// <summary>
        /// **水壶激光** —— `BeamEvent`（`RunSession.FireBeam` :5433；另有剑气 / 连锁两个来源）。
        ///   ① **射线**：`BeamSegs`=3 段短条拼成一条亮线，**越远越细、越淡**
        ///      （起点 1.0×、末端 `BeamTipWidth`=0.55×），0.10s，高 0.7m（与本体水柱同高）
        ///   ② 命中端 **14~20 个像素方块**的"像素水花"：向外 + 向上喷，重力 12，0.25~0.45s
        ///      （方块 + 小尺寸 + 蓝白抖动 = 像素水花 ✓）
        /// ★ 第三版：命中端那个**压扁的方块"法阵"已删除**（用户原话"取消末端的方形效果，只保留射线"）。
        ///   删掉之后 ① 的池占用从 6 升到 18（3 段 × 60 次/s × 0.10s），仍然远小于 `Bar` 池的 64 ✓
        /// ★ 水花**节流**（`SplashInterval`）：按住水壶时本事件 60/s 连发，
        ///   不节流会把池冲空（见常量注释的容量算术）✓
        /// </summary>
        public static void SpawnBeam(in BeamEvent e)
        {
            if (!_ready) return;

            Vector3 from = new Vector3(e.From.x, BeamY, e.From.y);
            Vector3 to = new Vector3(e.To.x, BeamY, e.To.y);
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.2f) return;            // 与 `WorldView.OnBeam` 同一个下限

            Beams++;
            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Can);

            // 宽 = HalfWidth（判定半宽）。**必须夹一个上限**：判定的 `HalfWidth` 叠满加粗可到 2.8m，
            // 原样画出来就是一块盖住半个场地的板子（这个事故 `WorldView.cs:3150` 记着）；
            // 上限取 0.60 = `WorldView` 那条"可视半宽上限 0.30"的两倍 ⇒ 两层宽度**一致**而不是互相打架 ✓
            float w = Mathf.Clamp(e.HalfWidth > 0f ? e.HalfWidth : 0.14f, 0.06f, 0.60f);
            float thicken = 1f + Mathf.Max(0f, e.ThickenPct) * 0.25f;   // 加粗在附加层上也要看得见一点
            w = Mathf.Clamp(w * thicken, 0.06f, 0.90f);

            // ── ① 射线：3 段拼接，宽度与亮度一起从 1.0 收到 0.55 ──
            // 为什么要分段而不是"一根等宽带 + 顶点色"：这一层只用 Cube + 每物体一套
            // `MaterialPropertyBlock`，没有正经的顶点流；分段是**零新增资产**又真能看出锥度的做法 ✓
            Color rayCol = Color.Lerp(Color.white, fx.Core, 0.35f);
            rayCol.a = 1f;
            for (int j = 0; j < BeamSegs; j++)
            {
                Piece bar = Take(PoolKind.Bar);
                if (bar == null) break;
                float f0 = j / (float)BeamSegs;
                float f1 = (j + 1) / (float)BeamSegs;
                float fMid = (f0 + f1) * 0.5f;
                float taper = Mathf.Lerp(1f, BeamTipWidth, fMid);        // 1.0 → 0.55
                bar.Mode = ModeAxis;
                bar.Grad = false;
                bar.Origin = from + d * f0;
                bar.End = from + d * f1;
                bar.Thick = Mathf.Max(0.02f, w * taper);
                bar.Col = rayCol;
                bar.A0 = BeamBaseAlpha * taper;                          // 越远越淡
                bar.Life = BarLife;
                bar.Age = 0f;
                Activate(bar);
            }

            // ── ★ v309：**外晕 + 内核两层**（用户：「柱体加内核 + 外晕两层（内层更亮更细）」）──
            //   改前只有那 3 段等宽的"柱体"，读起来是一根**均匀的亮条** ⇒
            //   没有"光从里面透出来"的层次。现在给整根柱体再套两圈：
            //     · **外晕**：整条 From→To 一根，宽 `HaloWidthScale`=**1.00×**（★ v311：
            //       1.75 → 1.00，见常量注释"水柱初始宽度被改宽"那条根因）、α **0.32**、
            //       颜色比柱体再淡一档（往白里推 10%）、寿命 **0.12s**（比柱体 0.10 略长，
            //       让"余晖"在柱体消失后还留一瞬）⇒ 读作柱体外面那层雾 ✓
            //       ⚠️ 1.00× ⇒ **整根柱子的最外沿严格 = `w`**（判定宽度），
            //          不会再出现"判定 0.14、画面 0.245"这种视觉与判定打架 ✓
            //     · **内核**：整条 From→To 一根，宽 `CoreWidthScale`=**0.42×**、α **1.0**、
            //       颜色 = `Lerp(white, fx.Core, 0.10)`（几乎纯白）、寿命 **0.07s**
            //       ⇒ 柱体正中那条**更亮更细**的芯 ✓
            //   为什么用"一整根"而不是"每段再套两层"：柱体已经用 3 段做锥度了，
            //   内核/外晕不需要锥度（它们是同心的壳），一整根更省格 ⇒
            //   **每次 BeamEvent 只多 2 格**（3 + 1 + 1 = 5，原 3），而 3 段版本要 9 ✗
            //   池账（`Bar` 池 64）：单道 60 次/s × 5 格 × 0.10s ≈ **30 格**常驻（原 18）；
            //   多柱 4 道时约 120 格 ⇒ 环形复用只回收"最老的"，读起来是"远端的柱体先淡"，
            //   不会崩（`Bar` 池本来就是给这种高频短命条用的）✓
            {
                Piece halo = Take(PoolKind.Bar);
                if (halo != null)
                {
                    Color haloCol = Color.Lerp(rayCol, Color.white, 0.10f);
                    haloCol.a = 1f;
                    halo.Mode = ModeAxis;
                    halo.Grad = false;
                    halo.Origin = from;
                    halo.End = to;
                    halo.Thick = Mathf.Max(0.03f, w * HaloWidthScale);
                    halo.Col = haloCol;
                    halo.A0 = HaloAlpha;                    // 0.24
                    halo.Life = HaloLife;                   // 0.12
                    halo.Age = 0f;
                    Activate(halo);
                }
                Piece core = Take(PoolKind.Bar);
                if (core != null)
                {
                    Color coreCol = Color.Lerp(Color.white, fx.Core, 0.10f);
                    coreCol.a = 1f;
                    core.Mode = ModeAxis;
                    core.Grad = false;
                    core.Origin = from;
                    core.End = to;
                    core.Thick = Mathf.Max(0.015f, w * CoreWidthScale);
                    core.Col = coreCol;
                    core.A0 = 1f;                           // 全亮：它就是"最里面那道光"
                    core.Life = CoreLife;                   // 0.07
                    core.Age = 0f;
                    Activate(core);
                }
            }

            // ── 命中端：像素水花（**每道各一份节流**，见 `_nextSplashAt` 的 v307 复盘）──
            float now = Time.unscaledTime;
            int slot = e.Slot >= 0 ? e.Slot % BeamFxSlots : 0;
            // ★ 逐道分摊：总道数越多、每道的水花越稀 ⇒ **每道都有**、而**总量不变**（见 `SlotCount`）✓
            int fan = Mathf.Max(1, e.SlotCount);
            if (now >= _nextSplashAt[slot])
            {
                _nextSplashAt[slot] = now + SplashInterval * fan;
                SplashBursts++;

                // 蓝白之间随机抖动。基色仍然从 `fx.Core` 派生（原来的 0.72/0.92/1 只是"水的蓝白"基底）✓
                Color waterBase = Color.Lerp(new Color(0.72f, 0.92f, 1f, 1f), fx.Core, 0.30f);
                waterBase.a = 1f;
                Vector3 hit = to;

                int pix = Mathf.Clamp(SplashPixels + fx.Sparks / 4, 14, 20);
                for (int i = 0; i < pix; i++)
                {
                    Piece p = Take(PoolKind.Bit);
                    if (p == null) break;
                    PixelsSpawned++;
                    float a = i * 2.39996f;                     // 黄金角散开 ⇒ 不会连成一条
                    var outw = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    p.Mode = ModeBallistic;
                    p.Grad = false;
                    p.Pos = hit + Vector3.up * 0.05f;
                    p.Vel = outw * UnityEngine.Random.Range(0.9f, 2.4f)                   // 向外
                          + Vector3.up * UnityEngine.Random.Range(1.4f, 3.2f);            // 向上（本轮改）
                    p.Gravity = SparkGravity;                                             // 12
                    // ★ 像素点：尺寸 0.05~0.09 m，且**几乎不缩**（S1 = S0 × 0.9）
                    //   ——像素方块的价值就在于"一直是方块"，缩成点就没有像素感了 ✓
                    float sz = UnityEngine.Random.Range(PixelSizeMin, PixelSizeMax);
                    p.S0 = sz;
                    p.S1 = sz * 0.90f;
                    p.Spin = Vector3.zero;                                                // 不转：像素不翻滚
                    p.Rot0 = Vector3.zero;
                    // 蓝白抖动：白 ↔ 水色之间随机取
                    p.Col = Color.Lerp(Color.white, waterBase, UnityEngine.Random.Range(0.15f, 0.95f));
                    p.A0 = 1f;
                    p.Life = UnityEngine.Random.Range(PixelLifeMin, PixelLifeMax);        // 0.25~0.45
                    p.Age = 0f;
                    Activate(p);
                }

                // ★ 第三版：这里原来还有一行 `SpawnCircle(hit, w, fx)`
                //   （末端压扁方块的"法阵"）。用户要求"取消末端的方形效果，只保留射线" ⇒ 已删除。
                //   水花仍然以命中端 `to` 为中心散发 —— 它本来就不依赖法阵 ✓
            }
        }

        /// <summary>
        /// ★ v290：**地面上的一圈环** —— 给「海浪」与「臭水沟」用（程序化，**零美术资产**）。
        ///
        /// 与 `SpawnBlast` 的 ① 是同一个做法（16 段沿圆排布、`ModeRadial` 从半径 0 张开），
        /// 只差三件事：寿命由调用方给、厚度更薄、颜色直接吃插件的 `FxSpec`。
        /// 为什么不做成"正经的水面网格"：这一层全工程只有 Cube + `MaterialPropertyBlock`，
        /// 没有顶点流；为了两道插件特效引进一份网格资产，会把"插件特效 = 零资产"这条纪律破掉 ✓
        /// </summary>
        /// <param name="center">圆心（世界 XZ）</param>
        /// <param name="radius">张开到的**判定半径**（米，与伤害判定同一个数，不额外放大）</param>
        /// <param name="fx">外观（吃水壶这一路的插件配色）</param>
        /// <param name="life">寿命（秒）</param>
        /// <param name="thick">环的厚度（米，海0.06 / 沼0.25）</param>
        public static void SpawnGroundRing(Vector2 center, float radius, in FxSpec fx, float life, float thick)
        {
            if (!_ready) return;
            float R = Mathf.Max(0.25f, radius);
            var c0 = new Vector3(center.x, GroundY, center.y);
            float chord = Mathf.PI * 2f / RingSegs * 1.10f;
            Color col = Color.Lerp(fx.Valid ? fx.Rim : Color.white, Color.white, 0.25f);
            col.a = 1f;
            for (int i = 0; i < RingSegs; i++)
            {
                float ang = i * (Mathf.PI * 2f / RingSegs);
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Origin = c0;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = R * 0.10f;
                p.R1 = R;
                p.Chord = chord;
                p.Thick = Mathf.Max(0.02f, thick);
                p.Depth = Mathf.Max(0.06f, R * 0.04f);
                p.Col = col;
                p.A0 = 0.85f;
                p.Life = Mathf.Max(0.05f, life);
                p.Age = 0f;
                Activate(p);
            }
        }

        /// <summary>
        /// ★ v307：**一次短促的电光球** —— `hoe_shock` 的被电目标身上（用户：「被电到的敌人身上
        /// 加一次短促的电光球（小球，0.12s）」）。
        ///
        /// ── 为什么走 `Mist` 池而不是 `Bit` 池 ──
        ///   本类只有 `Mist` 池的默认网格是**球**（`_sphereMesh`）；`Bit` 池是方块。
        ///   "电光球"必须是球 —— 方块只会读成"又一颗火星"，与 `Bit` 池已经在发的火花撞脸 ✗
        /// ── 为什么 `ModeBallistic` + `Vel = 0` + `Gravity = 0` ──
        ///   该模式下位置只在 `dt > 0` 时积分速度；速度与重力都给 0 ⇒ **钉在原地不动**，
        ///   而 `S0 → S1` 仍然按寿命收缩 ⇒ 正好是"闪一下就收"的小球 ✓
        /// 池账：每颗占 1 格 × 0.12s；感电同一次最多 12 个目标 ⇒ 同屏 ≤ 12 ≤ `Mist` 64 ✓
        /// </summary>
        public static void SpawnOrb(Vector3 pos, float radius, Color col, float life)
        {
            if (!_ready) return;
            Piece p = Take(PoolKind.Mist);          // Mist 池的默认网格 = 球
            if (p == null) return;
            OrbsSpawned++;
            float s = Mathf.Max(0.04f, radius) * 2f;   // 球网格半径 = 0.5 ⇒ 世界半径 = s × 0.5
            p.Mode = ModeBallistic;
            p.Grad = false;
            p.Sweep = false;
            p.Hold = false;
            p.Lift = 0f;
            p.Pos = pos;
            p.Vel = Vector3.zero;                   // 不飞
            p.Gravity = 0f;                         // 不掉
            p.S0 = s;
            p.S1 = s * 0.35f;                       // 缩到 35% ⇒ "一闪就收"
            p.Rot0 = Vector3.zero;
            p.Spin = Vector3.zero;
            p.Col = col;
            p.A0 = 1f;
            p.Life = Mathf.Max(0.05f, life);
            p.Age = 0f;
            Activate(p);
        }

        /// <summary>
        /// ★ v307：**落点的一圈小涟漪**（`can_ricochet` 弹射落点，用户：「落点一圈小涟漪」）。
        ///
        /// 与 `SpawnGroundRing` 的差别只在**量级**，不在形态 —— 刻意不复用它，因为那个函数
        /// 是海浪 / 臭水沟那一档（16 段、寿命 0.30~0.55s、厚度 0.25m 起）：
        /// 用它画弹射落点会读成"又炸了一下"✗ 这里是 **10 段、0.20s、很细** ⇒ 一点涟漪 ✓
        /// 池账：10 格 × 0.20s（`Bit` 池）；弹射受"每目标受伤 CD"限制，频率很低 ✓
        /// </summary>
        public static void SpawnRipple(Vector2 center, float radius, in FxSpec fx, float life)
        {
            if (!_ready) return;
            const int segs = 10;
            float R = Mathf.Max(0.15f, radius);
            var c0 = new Vector3(center.x, GroundY, center.y);
            float chord = Mathf.PI * 2f / segs * 1.12f;      // 留 12% 重叠 ⇒ 外沿不出缝
            Color col = Color.Lerp(fx.Valid ? fx.Rim : Color.white, Color.white, 0.35f);
            col.a = 1f;
            RipplesSpawned++;
            for (int i = 0; i < segs; i++)
            {
                float ang = i * (Mathf.PI * 2f / segs);
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Sweep = false;
                p.Hold = false;
                p.Origin = c0;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = R * 0.45f;          // 从半张开始（不是从 0 ⇒ 不会读成"爆炸"）
                p.R1 = R;
                p.Chord = chord;
                p.Thick = 0.028f;          // 很细的一圈
                p.Depth = Mathf.Max(0.030f, R * 0.10f);
                p.Col = col;
                p.A0 = 0.85f;
                p.Life = Mathf.Max(0.08f, life);
                p.Age = 0f;
                Activate(p);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v309：**弹射落点的水圈**（用户：「去除激光分支，改为受到弹射伤害的敌人脚下
        //    扩散一个水圈」）★★
        //
        //  ── 为什么不是复用 `SpawnRipple` ──
        //    `SpawnRipple` 是"一点小涟漪"（10 段、0.20s、从 0.45R 张开、颜色往白里推 35%）——
        //    它回答的是"这儿被溅了一下"。用户这次要的是"**这个敌人被弹射打到了**"：
        //    更慢（0.35s）、更大（0.15m → 0.90m）、**偏蓝绿**（水壶色系），而且脚下要有**水**。
        //    ⇒ 改成一个两层的形态：**外缘细环 + 内部水盘** ✓
        //
        //  ── 两层各管什么 ──
        //    ① **外缘细环 ×12**（`Bit` 池）：`0.15m → 0.90m` 张开、厚 0.024m、
        //       颜色 = 水壶 `Core` 往蓝绿推进 45% 再掺 30% 白 ⇒ 读作"水的边缘在扩散" ✓
        //    ② **内部水盘 ×1**（`Bit` 池 + 贴地扁圆 `_discMesh`）：半径 `0.15m → 0.65m`、
        //       α 0.30、颜色是同一色相再压深一档 ⇒ 环里面那层**薄薄的水**（不是空的圈）✓
        //    —— 只用一层环的话，俯视看就是"又一个圈"，与冲击环（16 段、土色、0.25s）
        //       和海浪（白沫墙）在**形状**上撞车；加了贴地水盘才读得出是"水"
        //
        //  ── 与另外两个"圈"的区分（用户点名要分得开）──
        //      冲击环 = 16 段 · 土色 `fx.Rim` · 0.25s · 从 0.04R 猛地张开
        //      海浪   = 立起来的墙 · 白沫 · 沿方向推进
        //      水圈   = **12 段 + 贴地水盘 · 蓝绿 · 0.35s · 从 0.15m 慢慢荡开** ✓
        //
        //  ── 池账 ──
        //    每个被弹射到的目标 **13 格 `Bit`**（12 环 + 1 盘）× 0.35s。
        //    弹射受"每目标受伤 CD"限制、且一束水柱一次只跳一个目标 ⇒ 同屏 1~3 个水圈
        //    ⇒ 峰值约 13~39 格 ≤ 64 ✓（改前那道小涟漪是 10 格，净增 3 格）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>水圈的起始半径（米）—— 用户给的就是"从 0.15m 扩到约 0.9m"。</summary>
        private const float WaterRingR0 = 0.15f;
        /// <summary>水圈张开到的半径（米）。</summary>
        private const float WaterRingR1 = 0.90f;
        /// <summary>水圈的寿命（秒）。0.35 —— 比冲击环（0.25）慢，读作"水在渗开"。</summary>
        private const float WaterRingLife = 0.35f;
        /// <summary>水圈外缘环的段数（12 段 = 每段 30°，比冲击环的 16 段更"水"、更少棱角）。</summary>
        private const int WaterRingSegs = 12;
        /// <summary>水壶那套色被推向**蓝绿**的目标色（用户："偏蓝绿，与冲击环（土色）、海浪（白沫）区分"）。</summary>
        private static readonly Color WaterRingTeal = new Color(0.26f, 1.00f, 0.80f, 1f);
        /// <summary>内部水盘再压深一档用的目标色（深青）。</summary>
        private static readonly Color WaterRingDeep = new Color(0.06f, 0.34f, 0.30f, 1f);

        /// <summary>
        /// ★ v309：**一个被弹射打到的敌人脚下的水圈**。
        /// 调用点：`RunSession.PluginPostHit` 的弹射分支 —— **每一个**被弹射到的目标各调一次
        /// （连锁 A→B→C 时，每一段的目标脚下都会各有一个 ⇒ 不会只在起点/终点点一下）✓
        /// </summary>
        /// <param name="center">敌人脚下（世界 XZ）</param>
        /// <param name="fx">外观（吃水壶这一路：`PluginFx.For(pm, WeaponSlot.Can)`）</param>
        public static void SpawnRicochetWaterRing(Vector2 center, in FxSpec fx)
        {
            if (!_ready) return;
            WaterRings++;

            // 水壶的 Core / Rim → 往蓝绿推 45%（`fx` 没写就用 Can 的基础色，永不画成纯黑）
            Color seedC = fx.Valid ? fx.Core : new Color(0.40f, 0.95f, 1f, 1f);
            Color seedR = fx.Valid ? fx.Rim : new Color(0.35f, 0.85f, 1f, 1f);
            Color teal = Color.Lerp(seedC, WaterRingTeal, 0.45f);
            Color rim = Color.Lerp(Color.Lerp(seedR, WaterRingTeal, 0.45f), Color.white, 0.30f);
            teal.a = 1f;
            rim.a = 1f;

            var c0 = new Vector3(center.x, GroundY, center.y);

            // ① 外缘细环 ×12：0.15m → 0.90m
            float chord = Mathf.PI * 2f / WaterRingSegs * 1.14f;    // 14% 重叠 ⇒ 外沿不留缝
            for (int i = 0; i < WaterRingSegs; i++)
            {
                float ang = i * (Mathf.PI * 2f / WaterRingSegs);
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Sweep = false;
                p.Hold = false;
                p.Origin = c0;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = WaterRingR0;
                p.R1 = WaterRingR1;
                p.Chord = chord;
                p.Thick = 0.024f;                                   // 很细的一圈（不是冲击环那种 0.07）
                p.Depth = Mathf.Max(0.035f, WaterRingR1 * 0.10f);
                p.Col = rim;
                p.A0 = 0.85f;
                p.Life = WaterRingLife;
                p.Age = 0f;
                Activate(p);
            }

            // ② 内部水盘 ×1（贴地扁圆）：半径 0.15m → 0.65m、α 0.30
            //    `_discMesh` 的局部半径 = 0.5 ⇒ 世界半径 = 缩放 × 0.5
            if (_discMesh != null)
            {
                Piece d = Take(PoolKind.Bit);
                if (d != null)
                {
                    d.UseMesh = _discMesh;                          // 换形：共享网格，不新建
                    d.Mode = ModeBallistic;
                    d.Grad = false;
                    d.Sweep = false;
                    d.Hold = false;
                    d.Lift = 0f;
                    d.Pos = new Vector3(center.x, GroundY * 0.55f, center.y);   // 压在环下面（更贴地）
                    d.Vel = Vector3.zero;
                    d.Gravity = 0f;
                    d.S0 = WaterRingR0 * 2f;                        // 0.30 ⇒ 半径 0.15
                    d.S1 = WaterRingR1 * 0.72f * 2f;                // 1.30 ⇒ 半径 0.65（比环小、在里面）
                    d.Rot0 = Vector3.zero;
                    d.Spin = Vector3.zero;
                    d.Col = Color.Lerp(teal, WaterRingDeep, 0.35f);
                    d.A0 = 0.30f;                                   // 薄薄一层水，不是不透明的盘
                    d.Life = WaterRingLife;
                    d.Age = 0f;
                    Activate(d);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v312：**潮汐的推力反馈**（用户：「给潮汐增加特效」）★★
        //
        // ── 改前是什么样子 ──
        //   `PluginExtra2.StartTidePush` 把敌人**按米**推开（0.25s / 卡面米数，见 `TidePushSeconds`），
        //   期间 `IsTideHeld` 压住它的自主移动 —— 逻辑很干净，**画面上什么都没有**。
        //   玩家看到的只是"那个敌人忽然平移了一下"，读成瞬移 / 卡顿，而不是"被水顶出去"✗
        //
        // ── 改后两件东西，都长在**被推的那个敌人脚下** ──
        //   ① **向外推的水波弧形**（不是整圈）：
        //      `TideArcSegs`(4) 段沿 **`TideArcSpanDeg`(120°) 的弧** 排开，**弧口朝被推的方向**
        //      ⇒ "水从这一面把人顶出去"。
        //      为什么必须是弧而不是圈：整圈 = `SpawnGroundRing` / 弹射水圈 / 冲击环 的形状，
        //      读起来是"以它为中心的波纹"；用户要的"**向外推的弧形**"表达的是**方向**，
        //      而"人被推开的方向"只有一个 ⇒ 120° 的弧，缺口朝着敌人被推走的那一侧的反面 ✓
        //      尺寸 `TideArcR0`(0.30) → `TideArcR1`(1.00)，寿命 `TideArcLife`(0.20s) 淡出。
        //   ② **拖尾**：沿"敌人 → 推开方向"留 `TideTrailSegs`(4) 片小水痕，`TideTrailLife`(0.25s)
        //      ⇒ 0.30 → 1.05m 一路排开，"被顶出去"这条路线上有痕迹（不是只有一个点）。
        //      ⚠️ 这两件都**不跟随敌人移动**：位置在调用那一刻就固定（敌人 0.25s 内往外走的正是
        //         这个方向）⇒ 与 `ModeWave` / 浪的水痕同一套"解析位置 + 负 Age 错峰"手法 ✓
        //
        // ── 配色（用户点名：水壶那套、偏青蓝，与海浪白沫 / 弹射水圈的蓝绿区分）──
        //   读 `FxSpec` 的 Core/Rim（`PluginFx.For(pm, WeaponSlot.Can)` = 水壶这一路的插件色），
        //   再往 `TideCyan` 推 ⇒ **G < B**（青蓝）。对照：
        //     · 弹射水圈 `WaterRingTeal` = (0.26, 1.00, 0.80) ⇒ **G > B**（蓝绿）
        //     · 海浪白沫 = 水壶色掺白 0.82 ⇒ 近乎白
        //   三者在**色相**上就分得开，不靠亮度硬区分 ✓
        //
        // ── 池账（`Bit` 池 64）──
        //   每个被推的敌人 = 弧 4 + 拖尾 4 = **8 格**。
        //   同一帧最紧的组合：`TideJobMax`(16) 单 ⇒ 16×8 = 128 > 64，
        //     第 9 个目标起池会环形复用掉**最老的片** ⇒ 是"后 8 个目标各少几片"，不是崩溃；
        //     现实里 `PluginPostHit` 一次命中只开一单（水柱单体 + 至多 1 个弹射）= 1~2 目标 ⇒ 8~16 格，
        //     即使同帧还有 13 格弹射水圈，也只有 29 格，宽松 ✓
        //   没有新增池、没有新增渲染体：全部取 `PoolKind.Bit`（Cube 网格）✓
        // ── 零随机 ──
        //   段的角度、拖尾的距离/大小/相位全是**下标**的固定函数 ⇒ 同一次推 f 每次都一样 ✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>潮汐推力弧的张角（度）—— 120°，弧口朝"被推走的方向"。</summary>
        private const float TideArcSpanDeg = 120f;
        /// <summary>潮汐推力弧的段数（4 段 ⇒ 每段 30°）。</summary>
        private const int TideArcSegs = 4;
        /// <summary>潮汐推力弧的起 / 止半径（米）—— 规格 0.3 → 1.0。</summary>
        private const float TideArcR0 = 0.30f;
        private const float TideArcR1 = 1.00f;
        /// <summary>潮汐推力弧的寿命（秒）—— 规格 0.20。</summary>
        private const float TideArcLife = 0.20f;
        /// <summary>潮汐拖尾的片数（规格 3~5，取 4）。</summary>
        private const int TideTrailSegs = 4;
        /// <summary>潮汐拖尾的寿命（秒）—— 规格 0.25。</summary>
        private const float TideTrailLife = 0.25f;
        /// <summary>潮汐的**青蓝**目标色：G(0.80) &lt; B(1.00)。
        /// 与弹射水圈的 `WaterRingTeal`(0.26,1.00,0.80，G &gt; B 蓝绿) 在色相上正面区分 ✓</summary>
        private static readonly Color TideCyan = new Color(0.24f, 0.80f, 1.00f, 1f);

        /// <summary>
        /// ★ v312：**一个敌人被潮汐推开那一刻的脚下反馈** —— 120° 水波弧（0.30→1.00m / 0.20s）
        /// + 沿推开方向 4 片渐隐拖尾（0.25s）。
        /// 调用点：`PluginExtra2.StartTidePush`（**每开一单**调一次，合并到已有单据时不重发）。
        /// 门控：调用点只在 `m.TidePush &gt; 0`（= 装了 `can_tide`）时进入 ⇒ 没装插件时本函数零调用 ✓
        /// </summary>
        /// <param name="at">被推敌人的脚下（世界 XZ）</param>
        /// <param name="dir">推开方向（玩家 → 敌人；退化时退到 `Vector2.up`）</param>
        /// <param name="fx">外观（水壶这一路：`PluginFx.For(pm, WeaponSlot.Can)`）</param>
        public static void SpawnTidePush(Vector2 at, Vector2 dir, in FxSpec fx)
        {
            if (!_ready) return;
            TideArcs++;

            Vector2 dn = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector2.up;
            float yawDeg = Mathf.Atan2(dn.x, dn.y) * Mathf.Rad2Deg;
            float yawRad = yawDeg * Mathf.Deg2Rad;
            var c0 = new Vector3(at.x, GroundY, at.y);

            // 水壶色 → 往青蓝推（`fx` 没写就用 Can 的基础色，永不画成纯黑）
            Color seedC = fx.Valid ? fx.Core : new Color(0.40f, 0.95f, 1f, 1f);
            Color seedR = fx.Valid ? fx.Rim : new Color(0.35f, 0.85f, 1f, 1f);
            Color body = Color.Lerp(seedC, TideCyan, 0.55f);
            Color edge = Color.Lerp(Color.Lerp(seedR, TideCyan, 0.45f), Color.white, 0.22f);
            body.a = 1f;
            edge.a = 1f;

            // ① 弧 ×4：绕"推开方向"左右各摊一半张角 ⇒ 弧口正对着被推走的那一侧
            float spanRad = TideArcSpanDeg * Mathf.Deg2Rad;
            float chord = spanRad / TideArcSegs * 1.20f;    // 20% 重叠 ⇒ 4 段的弧也不留缝
            for (int i = 0; i < TideArcSegs; i++)
            {
                float off = (i + 0.5f) / TideArcSegs - 0.5f;    // −0.375 … +0.375（占张角的比例）
                float ang = yawRad + off * spanRad;
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Sweep = false;
                p.Hold = false;
                p.Lift = 0f;
                p.Origin = c0;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = TideArcR0;
                p.R1 = TideArcR1;
                p.Chord = chord;
                p.Thick = 0.05f;                                // 水面一道薄脊（不是冲击环那种 0.07 的板）
                p.Depth = 0.13f;                                // 径向厚度：一小段"墙根"
                p.Col = edge;
                p.A0 = 0.80f;
                p.Life = TideArcLife;
                p.Age = 0f;
                Activate(p);
            }

            // ② 拖尾 ×4：沿推开方向 0.30 → 1.05m 依次亮起（负 `Age` 错峰，不需要计时器）
            for (int k = 0; k < TideTrailSegs; k++)
            {
                float f = (k + 1) / (float)TideTrailSegs;        // 0.25 / 0.50 / 0.75 / 1.00
                float d = Mathf.Lerp(0.30f, 1.05f, f);           // 这一段离敌人的距离（米）
                float sz = Mathf.Lerp(0.34f, 0.16f, f);          // 越远越小 ⇒ "尾巴散掉"
                Piece q = Take(PoolKind.Bit);
                if (q == null) break;
                q.Mode = ModeRadial;                             // 贴地片（R0 == R1 ⇒ 原地不扩张）
                q.Grad = false;
                q.Sweep = false;
                q.Hold = false;
                q.Lift = 0f;
                q.Origin = c0;
                q.Angle = yawRad;
                q.YawDeg = yawDeg;
                q.R0 = d;
                q.R1 = d;                                        // 不向外推 —— 拖尾是"留在原地"的
                q.Chord = sz / Mathf.Max(0.05f, d);              // X 尺寸 = d × Chord = sz
                q.Thick = 0.05f;
                q.Depth = sz * 0.75f;
                q.Col = Color.Lerp(body, edge, f * 0.55f);
                q.A0 = 0.62f - 0.10f * k;                        // 越远越淡（0.62 / 0.52 / 0.42 / 0.32）
                q.Life = TideTrailLife;
                q.Age = -0.055f * k;                             // 依次晚 55ms 亮起
                Activate(q);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v309：**灼印的短脉冲环**（用户：「命中时目标身上加一圈焦橙色的短脉冲环
        //    （贴地，0.2 秒）」）★★
        //
        //  形态：6 段极细的环、贴地、`0.12m → 0.50m`、0.20s —— 刻意做得**比水圈更小更短**：
        //    水圈（12 段 / 0.90m / 0.35s / 蓝绿）与它（6 段 / 0.50m / 0.20s / 焦橙）
        //    在**时长、直径、段数、色相**四个维度都不同 ⇒ 一眼分得开 ✓
        //  为什么只有 6 段：这一发是在**一次爆炸里对每个被罩到的敌人**各来一圈，
        //    而 `Bit` 池在同一帧还要发 主环 16 + 二次环 16 + 土粒 12 = 44 格。
        //    6 段 × 最多 3 个敌人（调用点那边有 `MarkPulseMax` 上限）= 18 格 ⇒ 44 + 18 = **62 ≤ 64** ✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>灼印脉冲环的段数（6 段 = 每段 60° ⇒ 一眼就是"一小圈"，不是"一波"）。</summary>
        private const int MarkPulseSegs = 6;
        /// <summary>灼印脉冲环的寿命（秒）—— 用户给的 0.2。</summary>
        private const float MarkPulseLife = 0.20f;
        /// <summary>灼印脉冲环的起 / 止半径（米）：从目标脚下 0.12m 荡到 0.50m。</summary>
        private const float MarkPulseR0 = 0.12f;
        private const float MarkPulseR1 = 0.50f;
        /// <summary>焦橙（用户点名"焦橙色"）—— 与灼印本体色（`PluginFx` 的 Mark 分支）同一色系，
        /// 但这里**不读插件**：灼印的色在任何配置下都是这个焦橙，玩家才能把它与别的环分开。</summary>
        private static readonly Color MarkPulseColor = new Color(1.00f, 0.45f, 0.10f, 1f);

        /// <summary>★ v309：一次灼印命中 → 目标脚下一圈焦橙短脉冲环（贴地、0.2s）。</summary>
        public static void SpawnMarkPulse(Vector2 center)
        {
            if (!_ready) return;
            MarkPulses++;
            var c0 = new Vector3(center.x, GroundY, center.y);
            float chord = Mathf.PI * 2f / MarkPulseSegs * 1.16f;    // 16% 重叠 ⇒ 6 段也不留缝
            for (int i = 0; i < MarkPulseSegs; i++)
            {
                float ang = i * (Mathf.PI * 2f / MarkPulseSegs);
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Sweep = false;
                p.Hold = false;
                p.Origin = c0;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = MarkPulseR0;
                p.R1 = MarkPulseR1;
                p.Chord = chord;
                p.Thick = 0.032f;
                p.Depth = 0.050f;
                p.Col = MarkPulseColor;
                p.A0 = 0.85f;
                p.Life = MarkPulseLife;
                p.Age = 0f;
                Activate(p);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v309：**吸血血滴的拖尾**（用户：「血滴改成带拖尾的（每滴后跟 2 个更小更淡的点）」）★★
        //
        //  ── 为什么拖尾只能在这里做（`PluginHitFx.cs` 本轮**不许碰**）──
        //    那 4 滴血是 `PluginHitFx` 用 `LineRenderer` 画的（`PluginPopupStyle.Lifesteal`）。
        //    那个文件归另一路改动，本轮一个字都不能动 ⇒ 拖尾改由**本类**在
        //    `RunSession.LifestealSettle`（真正回血那一刻）补上去。
        //    它的坐标系与弹字一致：4 滴血从 `(玩家位, 1.05m)` 向玩家飘（吸血弹字就发在玩家身上）
        //    ⇒ 这里按**同一个锚点、同一个高度**落 8 个点，读起来就是"每滴后面跟着两个小点" ✓
        //
        //  ── 每个点怎么摆 ──
        //    第 k 滴的漂移方向 `dir_k` = 黄金角（每帧/每次完全一样，**不消费随机数**）
        //    拖尾点 d（1、2）落在 `at + dir_k·(0.05·d)` 再**压低** `0.06·d`
        //    ⇒ 点比血滴更小（0.045 → 0.033 → 0.021）、更淡（0.75 → 0.53 → 0.31）、
        //      出现更晚（负 `Age` −0.03·d）⇒ 正是"后面跟着两个更小更淡的点" ✓
        //  ── 池账 ──
        //    4 滴 × 2 点 = **8 格 `Bit`** × 0.30s。吸血是**每个攻击事件结算一次**（不是每敌一次）
        //    ⇒ 频率远低于刀光/水花，8 格不构成压力 ✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>血滴的颗数（与 `PluginHitFx` 的 `CostOf(Lifesteal)` = 4 对齐）。</summary>
        private const int BloodDrops = 4;
        /// <summary>每滴血后面跟几个拖尾点（用户给的 2）。</summary>
        private const int BloodTrailPerDrop = 2;
        /// <summary>血的颜色（暗红；压得比弹字暗一档 ⇒ 拖尾不该抢弹字）。</summary>
        private static readonly Color BloodTrailColor = new Color(0.62f, 0.07f, 0.11f, 1f);

        /// <summary>★ v309：吸血那一刻在玩家身上补 4 条带拖尾的血滴（每滴 2 个更小更淡的点）。</summary>
        /// <param name="at">吸血弹字的锚点（世界坐标；高度已在调用点给成 1.05m，与弹字同高）</param>
        public static void SpawnBloodTrail(Vector3 at)
        {
            if (!_ready) return;
            for (int k = 0; k < BloodDrops; k++)
            {
                float a = k * (Mathf.PI * 2f / BloodDrops) + 0.55f;     // 固定相位 ⇒ 每次完全一致
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                for (int d = 1; d <= BloodTrailPerDrop; d++)
                {
                    Piece p = Take(PoolKind.Bit);
                    if (p == null) return;
                    BloodTrailDots++;
                    float f = d;                                        // 1、2
                    p.Mode = ModeBallistic;
                    p.Grad = false;
                    p.Sweep = false;
                    p.Hold = false;
                    p.Lift = 0f;
                    p.Pos = at + dir * (0.055f * f) - Vector3.up * (0.065f * f);
                    p.Vel = Vector3.zero;                               // 钉住（不飞、不掉）
                    p.Gravity = 0f;
                    p.S0 = 0.045f - 0.012f * f;                         // 0.033 / 0.021
                    p.S1 = p.S0 * 0.45f;                                // 越往后越缩
                    p.Rot0 = Vector3.zero;
                    p.Spin = Vector3.zero;
                    p.Col = BloodTrailColor;
                    p.A0 = 0.75f - 0.22f * f;                           // 0.53 / 0.31（更淡）
                    p.Life = 0.30f - 0.05f * f;                         // 0.25 / 0.20
                    p.Age = -0.03f * f;                                 // 出现更晚（负 Age ⇒ 先透明）
                    Activate(p);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v309：**伏雷的地面脉冲光点**（用户：「埋雷期间地面加微弱的脉冲光点
        //    （让玩家能看见雷在哪）」）★★
        //
        //  ── 改前的样子 ──
        //    `PluginField` 埋下的雷在场景里**没有任何持久标记**：只有埋下去那一瞬间的
        //    前摇光圈（`FertilizerTelegraphEvent`，`MineArmTime + 0.35s` 就灭了）。
        //    之后雷在地上要等 6 秒，玩家完全看不见它埋在哪 —— 这是用户点名的那条 ✓
        //
        //  ── 现在：一个**很微弱的、周期性的**点 ──
        //    · 贴地扁圆（`_discMesh`）从半径 `0.11m` 荡到 `0.40m`、α 只有 **0.26**、
        //      暖土色（与"伏雷 = 埋在土里的能量"同色系）
        //    · 每颗雷每 `MinePulseEvery`=0.55s 来一次、每次 0.45s ⇒ **脉冲**，不是常亮
        //      （常亮会让雷变成"地上的一个圆"，与毒区/水渍撞脸；一明一暗才是"心跳"）✓
        //    · 节流与计数在 `PluginField.TickMines` 里（固定帧、确定性，不用 `Time.deltaTime`）
        //  ── 池账 ──
        //    **1 格 `Bit`** × 0.45s，每颗雷每 0.55s 一次 ⇒ 6 颗雷峰值约 6 格 ✓
        // ══════════════════════════════════════════════════════════════════
        /// <summary>伏雷脉冲的周期（秒）—— 与调用点 `PluginField` 里那个节流常量**同一个数**。</summary>
        public const float MinePulseEvery = 0.55f;
        /// <summary>伏雷脉冲的寿命（秒）。比周期短 ⇒ 有"灭掉"的那一段 = 脉冲感。</summary>
        private const float MinePulseLife = 0.45f;
        /// <summary>伏雷脉冲的暖土色（微弱的暖黄 —— 比前摇光圈的土色亮一点，才"看得见"）。</summary>
        private static readonly Color MinePulseColor = new Color(0.98f, 0.66f, 0.26f, 1f);

        /// <summary>★ v309：一颗埋着的雷脚下的一次**微弱脉冲**（贴地扁圆，1 格）。</summary>
        public static void SpawnMinePulse(Vector2 center)
        {
            if (!_ready || _discMesh == null) return;
            Piece p = Take(PoolKind.Bit);
            if (p == null) return;
            MinePulses++;
            p.UseMesh = _discMesh;
            p.Mode = ModeBallistic;
            p.Grad = false;
            p.Sweep = false;
            p.Hold = false;
            p.Lift = 0f;
            p.Pos = new Vector3(center.x, GroundY * 0.5f, center.y);   // 压在地上
            p.Vel = Vector3.zero;
            p.Gravity = 0f;
            p.S0 = 0.22f;                                              // 半径 0.11m
            p.S1 = 0.80f;                                              // 半径 0.40m
            p.Rot0 = Vector3.zero;
            p.Spin = Vector3.zero;
            p.Col = MinePulseColor;
            p.A0 = 0.26f;                                              // "微弱"
            p.Life = MinePulseLife;
            p.Age = 0f;
            Activate(p);
        }

        /// <summary>
        /// ★ v305：**香蕉皮砸地的泥点** —— 只有"向上溅"，**不向外扩任何东西**。
        ///
        /// 为什么需要这个新方法（用户实测原话：「**污水带来的末端产生一个波**」）：
        ///   改前香蕉皮落地调的是 `SpawnGroundRing` —— 那是 16 段 `ModeRadial` 从 0.17m
        ///   **往外张开到 1.7m** 的环，与「水环 / 冲击环 / 浪墙」**同一套几何 + 同一个池 +
        ///   同一个动画方向** ⇒ 玩家读成"又推了一道波" ✗
        ///   这里换成 `ModeBallistic` 抛物线泥点：落地时泥点**往上溅**，
        ///   没有任何向外扩张的环 ⇒「有落地反馈」保住、「波」这个观感消失 ✓
        ///
        /// 与其它特效的区分点（用户要求"每个都要有特色"）：
        ///   · 冲击环 = 贴地向外张开      · 浪墙 = 直立弧形墙推进
        ///   · 黑洞环 = 向内收缩          · **泥点 = 纯抛物线、纵向为主、零水平扩张** ✓
        /// </summary>
        public static void SpawnMudSplash(Vector2 center, float radius, in FxSpec fx)
        {
            if (!_ready) return;

            // ★ v307：9 → **14**（用户：「落地时向上溅的泥点更密（现在 9 颗，加到 14）」）
            //   仍然是"比冲击环的 16 少 + 全部向上" ⇒ 不会因为加密就重新读成"一圈" ✓
            const int MudBits = 14;
            const float MudGravity = 16f;   // 比土粒 14 略大 ⇒ 溅得高、落得快
            const float MudLife = 0.42f;

            float R = Mathf.Max(0.25f, radius);
            Color mud = Color.Lerp(new Color(0.40f, 0.34f, 0.22f, 1f),
                                   fx.Valid ? fx.Rim : Color.white, 0.30f);
            mud.a = 1f;

            for (int i = 0; i < MudBits; i++)
            {
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                DirtSpawned++;

                // 黄金角散开，但**纵向分量占优**（y ≥ 1.15 ⇒ 一定往上；水平半径 ≤ 1）
                float a = i * 2.39996f;
                float rr = 0.35f + 0.65f * ((i * 7 % MudBits) / (float)MudBits);
                var up = new Vector3(Mathf.Cos(a) * rr,
                                     1.15f + 0.85f * ((i % 3) * 0.5f),
                                     Mathf.Sin(a) * rr);

                p.Mode = ModeBallistic;
                p.Grad = false;
                p.Pos = new Vector3(center.x, GroundY + 0.10f, center.y);
                p.Vel = up.normalized * (2.4f + 1.6f * ((i % 4) * 0.25f))
                        * Mathf.Min(1f, R * 0.5f + 0.6f);
                p.Gravity = MudGravity;
                p.S0 = 0.07f + 0.05f * ((i % 3) * 0.5f);
                p.S1 = p.S0 * 0.35f;
                p.Rot0 = new Vector3((i * 47f) % 360f, (i * 83f) % 360f, (i * 29f) % 360f);
                p.Spin = new Vector3(((i * 71f) % 360f) - 180f,
                                     ((i * 113f) % 360f) - 180f,
                                     ((i * 37f) % 360f) - 180f);
                p.Col = mud;
                p.A0 = 1f;
                p.Life = MudLife;
                p.Age = 0f;
                Activate(p);
            }
        }

        /// <summary>
        /// ★ v310：**黑洞前摇 = 黑色漩涡**（用户：「黑洞表现修改，在爆炸前增加 1 秒前摇，
        ///   吸附范围内所有敌人到爆炸中心（黑色漩涡特效）」）。
        ///
        /// ── 改前（v308）是什么、为什么不够 ──
        ///   前摇 0.25 秒、三层：贴地暗环 ×16 + **上升螺线 ×14** + 中心暗球 ×1。
        ///   螺线是"从外沿钻进中心"的**碎屑流**（半径按 `k²` 收到 0.08R）⇒ 它读作
        ///   "有东西被吸进去"，但**中心本身不动**，1 秒的长度里会显得"空转"✗
        ///
        /// ── 现在四层（全部同一时刻起、同一个寿命 `life` = 1.0s 前摇）──
        ///   ① **贴地暗环 ×16**（`Bit`）：`R → 0.12R` 向内收 ⇒ "范围在缩"（保留 v308）
        ///   ② **螺旋臂 ×4 臂 × 8 段 = 32**（`Bit`，`ModeVortex`）：4 条臂绕中心**持续旋转**，
        ///      臂的外端在 `0.96R`、内端收到 `0.55R`（沿臂向内），半径在 1 秒里再收 38%；
        ///      转角 `SpiralTurns = 7.0 rad`（≈400°）且按 **`k²`** 走 ⇒ **越转越快**
        ///      （起步静止、末段 800°/s）；高度从 0.16m 抬到 1.7m ⇒ 漩涡是**斜的** ✓
        ///   ③ **中心暗球 ×1**（`Bit` + `_sphereMesh`）：0.30m → `min(R×0.5, 1.4)`，`Hold`
        ///      咬到最后一刻再整体收掉 ⇒ "被吸成一个点"（保留 v308）
        ///   ④ **边缘暗紫外光圈 ×12**（`Bit`，`ModeVortex` 的慢速版）：压在 `0.98R` 上、
        ///      1 秒里慢慢转 92°，`Hold` ⇒ 用户要的"边缘一圈暗紫外光" ✓
        ///
        /// ── 配色：**近黑** + 暗紫（与爆炸的暖白彻底分开）──
        ///   · 漩涡臂 `(0.085, 0.050, 0.130)` → 外端压到 `(0.045, 0.030, 0.070)`（更黑）
        ///   · 中心球 `(0.030, 0.018, 0.050)`（几乎纯黑，α 0.92 ⇒ 真的把背后的东西压暗）
        ///   · 边缘光 `(0.46, 0.26, 0.72)` 暗紫、α 0.55
        ///   ★ 不再从 `fx.Core` 派生：黑洞这一招固定"黑 + 暗紫"，装什么肥料插件都一样
        ///     （与巨型"固定偏灰金"同一条纪律；v308 那版是 `fx.Core × 0.34/0.28/0.50`）✓
        ///
        /// ── 确定性 ──
        ///   没有随机数：臂相位 = `a·(2π/4) + fj·1.15`、转角 = `SpiralTurns·k²`、
        ///   半径 = `lerp(R0,R1,k)` ⇒ 全部是"这一发的序号 + 时间"的解析函数 ✓
        ///
        /// ── ★ v311 修 bug：**去掉了"同一帧只画一套"的按帧去重** ★
        ///   用户报的原话：「**黑洞搭配连环会只有末端可以触发黑洞**」。
        ///   两道错叠在一起（`RunSession.TickPendingBlasts` 那段注释记着全账）：
        ///     ① 倒序遍历 ⇒ 同一帧里**第一个**被处理的落点是下标最大的那颗 = **最后端那一炮**；
        ///     ② `_lastCollapseFrame` 按**帧**去重 ⇒ 它画完，其余两颗（含**主落点 i=0**）全被 return 掉。
        ///   ⇒ 玩家看到的正是"只有末端能触发黑洞"，而**吸**却发生在主落点（`_bhCenter` 被最后那次
        ///     覆盖 ⇒ 视觉与玩法各在一个地方）✗
        ///   现在：**每个落点各画各的**，去重改成"按落点"—— 判据是 `RunSession` 里那条
        ///   "`b.T` 跨过 `BlackHoleWindup`"的跨越式判定，它对**每一颗** `PendingBlast` 恰好成立一次
        ///   ⇒ 不需要任何额外去重标记，次数天然正确 ✓
        ///
        /// ── 池账（`PoolPerKind` = 64，全部取 `Bit`；按 `spots` 分档，见下）──
        ///   `spots = 1`（没装连环）：16 + 32 + 1 + 12 = **61 ≤ 64** ✓ **与 v310 逐位相同**
        ///   `spots = 2`（连环 1 层）：10 + 12 + 1 + 6 = **29/套 ⇒ 两套 58 ≤ 64** ✓
        ///   `spots = 3`（连环 2 层）： 6 + 12 + 1 + 0 = **19/套 ⇒ 三套 57 ≤ 64** ✓
        ///   `spots ≤ 0`（★ v311 伏雷的**一次性爆发版**）：6 + 10 + 1 = **17**，
        ///     且它出现在**爆炸那一帧**（爆炸本身要 16 环 + 16 二次环 + 12 土粒 = 44）
        ///     ⇒ 17 + 44 = **61 ≤ 64** ✓
        ///   ⚠️ 与爆炸那一帧的**边界关系**（照实记，不粉饰）──
        ///      爆炸落在引信 1.02s，漩涡开在"`T` 从 1.02 跨到 ≈0.9867"那一帧 ⇒ 漩涡活 **1.00s**、
        ///      爆炸在它开出来 **0.9867s** 之后 ⇒ 两者**同刻**结束，只差浮点/帧序的一线。
        ///      也就是说爆炸那一帧的画面峰值理论上可以是 `漩涡 57 + 爆炸 44 = 101 &gt; 64`；
        ///      但 `Take` 取空**返回 null**、调用点都是 `if (p == null) break;` ⇒
        ///      不会崩、不会新建对象，只是**多出来的碎片被丢掉**。
        ///      ★ 这一线重叠 **v310 就有**（单落点时是 61 + 44 = 105，只剩 3 格余量），
        ///        本轮没有新增它；而且 3 落点档（57）反而比 v310 的单落点档（61）**多 4 格余量** ✓
        /// </summary>
        /// <param name="spots">这一次投掷的**落点数**（1/2/3）。
        /// **0 或负数 = "一次性爆发"档**（伏雷用：寿命短、格数最少，要和爆炸 44 格同帧共存）。</param>
        public static void SpawnCollapseRing(Vector2 center, float radius, in FxSpec fx, float life,
                                             int spots = 1)
        {
            if (!_ready) return;
            BlackHoles++;
            // ── 档位：`Bit` 池 64 是全工程共用的 ⇒ 同时开几套，就得按套数缩每一套 ──
            //    每档的三组数就是上面池账里的三行，改任何一个都要把那一行重算 ✗
            int ringSegs, arms, armSegs, rimBits;
            if (spots <= 0) { ringSegs = 6; arms = 2; armSegs = 5; rimBits = 0; }        // 17/套（爆发）
            else if (spots == 1) { ringSegs = RingSegs; arms = 4; armSegs = 8; rimBits = 12; } // 61
            else if (spots == 2) { ringSegs = 10; arms = 2; armSegs = 6; rimBits = 6; }  // 29
            else { ringSegs = 6; arms = 2; armSegs = 6; rimBits = 0; }                   // 19（3 套以上）
            float R = Mathf.Max(0.35f, radius);
            float T = Mathf.Max(0.05f, life);
            var c0 = new Vector3(center.x, GroundY, center.y);

            // ── 配色：近黑 + 暗紫（固定，不读 fx）──
            Color armInner = new Color(0.045f, 0.030f, 0.070f, 1f);   // 臂的外端（更黑）
            Color armOuter = new Color(0.085f, 0.050f, 0.130f, 1f);   // 臂的内端（略亮一点紫）
            Color darkRing = new Color(0.070f, 0.045f, 0.105f, 1f);   // 贴地暗环
            Color rimViolet = new Color(0.46f, 0.26f, 0.72f, 1f);     // 边缘暗紫外光
            Color ballC = new Color(0.030f, 0.018f, 0.050f, 1f);      // 中心球：近黑

            // ── ① 贴地暗环：向内收（保留 v308 的形状，只换配色）──
            //   ★ v311：段数改读本档的 `ringSegs`（1 落点 16、2 落点 10、3 落点 6、爆发 6）
            float chord = Mathf.PI * 2f / ringSegs * 1.10f;
            for (int i = 0; i < ringSegs; i++)
            {
                float ang = i * (Mathf.PI * 2f / ringSegs);
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Sweep = false;
                p.Hold = false;
                p.Origin = c0;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = R;                    // 从判定半径起
                p.R1 = R * 0.12f;            // 收到圆心附近 ⇒ 读成"被吸进去"
                p.Chord = chord;
                p.Thick = 0.10f;
                p.Depth = Mathf.Max(0.08f, R * 0.05f);
                p.Col = darkRing;
                p.A0 = 0.95f;
                p.Life = T;
                p.Age = 0f;
                Activate(p);
            }

            // ── ② **螺旋臂 ×4**（每臂 8 段，`ModeVortex`）──
            //   一条臂 = 8 颗沿半径铺开的短光条，相邻两颗在角向上落后 `ArmLag`(1.15 rad)
            //   ★ v311：臂数 / 每臂段数改读本档的 `arms` / `armSegs`
            //   （1 落点 4×8、2 落点 2×6、3 落点 2×6、爆发 2×5）
            const float ArmLag = 1.15f;                    // 臂的角向滞后（弧度，内→外）
            for (int a = 0; a < arms; a++)
            {
                for (int j = 0; j < armSegs; j++)
                {
                    float fj = armSegs <= 1 ? 0f : j / (float)(armSegs - 1);   // 0 = 内端，1 = 外端
                    Piece p = Take(PoolKind.Bit);
                    if (p == null) break;
                    float r0 = R * Mathf.Lerp(0.55f, 0.96f, fj);               // 臂的半径：内→外
                    p.Mode = ModeVortex;
                    p.Grad = false;
                    p.Sweep = false;
                    p.Hold = true;                       // ★ 1 − k⁴：整条臂咬住 1 秒，末段一起收
                    p.Origin = c0;
                    p.Angle = a * (Mathf.PI * 2f / arms) + fj * ArmLag;        // 螺旋相位（无随机）
                    p.R0 = r0;                            // 起点半径
                    p.R1 = r0 * 0.62f;                    // 1 秒内再收 38%（往里卷）
                    p.SpiralTurns = 7.0f;                 // ≈400°：越转越快（角速度 ∝ k）
                    p.SpiralY0 = 0.16f + 0.95f * fj;      // 外端更高 ⇒ 漩涡是**斜**的
                    p.SpiralY1 = p.SpiralY0 + 0.35f;
                    p.S0 = 0.34f + 0.14f * fj;            // 沿切向的条长（外端更长 ⇒ 臂越往外越粗）
                    p.S1 = p.S0 * 0.72f;
                    // 竖直 / 径向都到 0.075~0.12m：8 颗沿臂铺开时**相邻两颗在切向上刚好接住**
                    // （切向步长 ≈ 0.16 rad × r ≈ 0.33m，条长 0.34~0.48m）⇒ 读作一条连续的臂，
                    // 不是 8 根飞散的短棍 ✓
                    p.Thick = 0.075f;                     // 竖直厚度
                    p.Depth = 0.12f;                      // 径向厚度
                    p.Col = Color.Lerp(armOuter, armInner, fj);   // 外端更黑
                    p.A0 = 0.72f;
                    p.Life = T;
                    p.Age = 0f;
                    Activate(p);
                }
            }

            // ── ④ **边缘暗紫外光环 ×12**（用户：「边缘一圈暗紫外光」）──
            //   12 颗沿 `0.98R` 排开、切向条长取周长 1/12（8% 重叠 ⇒ 不断线），
            //   用 `ModeVortex` 的慢速版：1 秒只转 92°、半径几乎不动 ⇒ 一圈**缓慢自转**的紫边 ✓
            //   ★ v311：颗数改读本档的 `rimBits`（1 落点 12、2 落点 6、3 落点与爆发 **0 ⇒ 这一层不画**）。
            //     为什么先砍这一层：它是**装饰性外壳**（α 0.55、压在 0.98R 上），
            //     砍掉之后"这里有一个黑洞"仍然成立 —— 中心暗球 + 螺旋臂 + 贴地暗环三样都在 ✓
            float rimChord = rimBits <= 0
                ? 0f
                : 2f * Mathf.Sin(Mathf.PI / rimBits) * R * 0.98f * 1.08f;
            for (int i = 0; i < rimBits; i++)
            {
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeVortex;
                p.Grad = false;
                p.Sweep = false;
                p.Hold = true;
                p.Origin = c0;
                p.Angle = i * (Mathf.PI * 2f / rimBits);
                p.R0 = R * 0.98f;
                p.R1 = R * 0.96f;                     // 几乎不动
                p.SpiralTurns = 1.6f;                 // ≈92°：缓慢自转
                p.SpiralY0 = 0.20f;
                p.SpiralY1 = 0.34f;
                p.S0 = rimChord;                      // 切向条长（相邻两颗 8% 重叠）
                p.S1 = rimChord * 0.94f;
                p.Thick = 0.05f;
                p.Depth = 0.05f;
                p.Col = rimViolet;
                p.A0 = 0.55f;
                p.Life = T;
                p.Age = 0f;
                Activate(p);
            }

            // ── ③ 中心暗球：越吸越大，然后整体收掉 ──
            Piece ball = Take(PoolKind.Bit);
            if (ball != null)
            {
                if (_sphereMesh != null) ball.UseMesh = _sphereMesh;   // 共享球网格（不新建）
                ball.Mode = ModeBallistic;
                ball.Grad = false;
                ball.Sweep = false;
                ball.Pos = c0 + Vector3.up * 0.85f;    // 悬在圆心上方（前三层都在这个高度收拢）
                ball.Vel = Vector3.zero;
                ball.Gravity = 0f;
                ball.S0 = 0.30f;                        // 小球
                ball.S1 = Mathf.Clamp(R * 0.50f, 0.30f, 1.40f);   // 越吸越大（夹上限，别盖住整个场地）
                ball.Rot0 = Vector3.zero;
                ball.Spin = Vector3.zero;
                ball.Col = ballC;
                ball.A0 = 0.88f;
                ball.Hold = true;                       // ★ 1 − k⁴：咬住到最后一刻再收掉
                ball.Life = T;
                ball.Age = 0f;
                Activate(ball);
            }
        }

        /// <summary>
        /// ★ v308：**护壤 = 玩家周围罩上一层六边形护盾**（用户：「在玩家周围生成一圈六边形护盾面片」）。
        ///
        /// 改前只有"护盾值 + 弹字"，看不出"罩上了一层" ⇒ 现在补一层视觉：
        ///   · **8 片**六边形（`HexCount`），沿玩家周围均匀排布、面朝外，站在 0.72m 高处
        ///     ⇒ 俯视 60° 的相机看到的是"一圈立起来的六边形"，不是"地上的圈" ✓
        ///   · **张开**：`ModeRadial` 的 `R0 → R1`（`growth = √k`，起步快）让环半径
        ///     从 `0.60R` 涨到 `1.00R`；片宽 `X = 当前半径 × Chord` 跟着一起涨
        ///     ⇒ 读作"从贴身一下子张开成罩子"✓
        ///     ⚠️ 精确时序：`Life = 0.65s`、`R1 = 1.11R`（`0.60 + 0.40/√0.615`）⇒ **0.40s 那一刻
        ///        环半径正好 = R**，之后继续微涨着淡出（`Hold` = `1 − k⁴`）⇒
        ///        "0.4 秒内从 0.6 倍张开到 1.0 倍后淡出"这条规格在**时间轴上成立** ✓
        ///   · 半透明：`A0 = 0.50`，颜色从 `fx` 派生（护壤走肥料那一套暗绿/土色）✓
        ///
        /// 池账：8 片取自 `Slice` 池（64）⇒ 与刀光 52 不共存（护壤由肥料触发，武器互斥）✓
        /// </summary>
        public static void SpawnWardHexes(Vector2 center, float radius, in FxSpec fx)
        {
            if (!_ready || _hexMesh == null) return;
            WardShields++;
            const int HexCount = 8;
            float R = Mathf.Max(0.6f, radius);
            var c0 = new Vector3(center.x, 0.72f, center.y);   // 站起来的罩子（不是贴地的圈）

            Color c = fx.Valid ? fx.Core : new Color(0.55f, 0.85f, 0.55f, 1f);
            c = Color.Lerp(c, new Color(0.72f, 1.00f, 0.78f, 1f), 0.35f);   // 提亮一档 ⇒ 半透明也看得见
            c.a = 1f;                                                      // α 交给 A0

            for (int i = 0; i < HexCount; i++)
            {
                // 每片均匀 45°，再整体偏 22.5° ⇒ 不与坐标轴对齐（否则读起来像"贴了 4 面墙"）
                float a = (i * 360f / HexCount + 22.5f) * Mathf.Deg2Rad;
                Piece p = Take(PoolKind.Slice);
                if (p == null) break;
                if (_mat != null) p.Mr.sharedMaterial = _mat;   // 无贴图的共享材质（不是刀光那 3 份）
                p.UseMesh = _hexMesh;                            // 换形：共享六边形网格
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Sweep = false;
                p.Origin = c0;
                p.Angle = a;
                p.YawDeg = a * Mathf.Rad2Deg;
                p.R0 = R * 0.60f;            // 起点：0.6 倍
                p.R1 = R * 1.11f;            // 终点：0.40s 那一刻正好到 1.0 倍（见 summary）
                p.Chord = 0.42f;             // X = 当前半径 × 0.42 ⇒ 片宽 ≈ 环周长的 1/8（相邻片略叠）
                p.Thick = 0.95f;             // 竖直高度 0.95m（罩住玩家的大半身）
                p.Depth = 0.02f;             // 面片很薄
                p.Col = c;
                p.Hold = true;               // 1 − k⁴：张开后咬住，末段整体收掉
                p.A0 = 0.50f;                // 半透明
                p.Life = 0.65f;
                p.Age = 0f;
                Activate(p);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v304：两件**新形态**（用户点名要的：海浪重做 / 香蕉皮单独建模）★★
        // ══════════════════════════════════════════════════════════════════

        // ── ⑤ 海浪：**更高、更密的浪墙 + 两层细碎白沫 + 水痕** ───────
        /// <summary>★ v308：浪墙片数 **9 → 16**（用户：「升级海浪…更多段（14~18）」）。
        /// 墙身 16 段 + 白沫两层各 16 段 = **48** 个渲染体取自 `Slice` 池（64）✓</summary>
        public const int WaveSegs = 16;
        /// <summary>★ v308：浪墙高度 1.10 → **1.35m**（"浪峰更高"）。</summary>
        private const float WaveHeight = 1.35f;
        /// <summary>★ v308：浪峰额外抬起 0.55 → **0.80m**（中段比两端高这么多）。</summary>
        private const float WaveCrest = 0.80f;
        /// <summary>白沫带的高度（相对墙顶再抬这么多米）。</summary>
        private const float WaveFoamLift = WaveHeight * 0.88f;
        /// <summary>★ v308：白沫**两层** —— 下层的竖直厚度（米，"浪顶那一条更亮的窄带"）。</summary>
        private const float WaveFoamThick = 0.11f;
        /// <summary>★ v308：白沫**上层**的竖直厚度（米）。比下层细一半、再高 0.16m
        /// ⇒ 两层错开 = "细碎的白沫"（一层是水脊、一层是飞起来的沫）✓</summary>
        private const float WaveFoamThick2 = 0.055f;
        /// <summary>★ v308：白沫上层的额外抬升（米）。</summary>
        private const float WaveFoamLift2 = 0.16f;
        /// <summary>★ v308：水痕段数（贴地、渐隐）。3~5 段里取 **4**。</summary>
        private const int WaveTrailSegs = 4;
        /// <summary>★ v308：水痕相对浪墙的寿命比例（浪墙 0.5s ⇒ 水痕 0.45s 后才完全消失）。</summary>
        private const float WaveTrailLifeScale = 0.90f;
        /// <summary>
        /// ★ v312：浪墙**中段的前凸量**（米）。原来是 `SpawnWaveWall` 里的一处字面量 `0.28f`，
        /// 现在提成常量，因为 `PluginExtra2` 需要一个数回答"这道浪最远推到哪"：
        ///   最远视觉点离玩家中心的距离 = **`advance + WaveWallBulge`**
        ///   （中段那几片在推进终点上再往前凸这么多，两端的片前凸 ≈ 0）。
        /// ★ 为什么这个数决定"视觉能不能越出判定"：浪的**伤害**是以玩家为心、半径 `pm.WaveRadius`
        ///   的一个圆（`PluginExtra2.OnCanAttack` 里 `(e.Pos - p.Pos)`）⇒
        ///   只要 `advance + WaveWallBulge ≤ WaveRadius`，凡是**画面被浪墙碰到**的敌人
        ///   必然也在判定圆内（推出去的那一道平面到玩家中心的距离就是视觉触及的最远边界）✓
        ///   调用点据此把 `advance` 夹在 `WaveRadius − WaveWallBulge` 以内 —— 视觉越界会被玩家
        ///   读成"明明打到了却没掉血"，那是这一条改动唯一能造成的错位。
        /// </summary>
        public const float WaveWallBulge = 0.28f;

        /// <summary>
        /// ★ v304：**海浪 = 一道向上卷起、沿移动方向推进的浪墙**（用户需求⑤"海浪效果重做"）。
        ///
        /// ── 改前是什么、为什么不够 ──
        ///   改前是 `SpawnGroundRing`：一圈**贴地**往外摊的水环，读起来是"扩散的波纹"，
        ///   与"海浪"这个语义差得远（用户原话："现在是一个扩散水环。重做成更像海浪"）✗
        ///
        /// ── 改后的两件东西（墙身 / 白沫）──
        ///   · **墙身**：`WaveSegs` 段竖直薄片，沿**垂直于移动方向**的一条弧排开（中段前凸 0.28m）
        ///     → 用 `ModeWave` 推进：`0.6m → 0.6 + advance` 米，`growth = √k` ⇒ 起步猛、末段缓
        ///     → 高度 `WaveHeight`，中段再抬 `WaveCrest · sin(π·k)` ⇒ **先卷起来、末段砸下去**
        ///   · **白沫**：同样 9 段，抬到浪顶（`WaveFoamLift`）、厚度只有 `0.11m`、颜色掺白 0.82
        ///     ⇒ 浪顶那一条**更亮的窄带**（用户点名要的"带白沫"）✓
        ///
        /// ── 参数 ──
        ///   · `advance` = 2.5m（规格 2~3 米的中间值），`life` = 0.5s ⇒ 平均 5 m/s 推出去
        ///   · `width` = **调用方给的判定宽度**（`pm.WaveRadius`：★v313 起 1/2/3 层 = 4.5/6.75/9.0m）
        ///     ⇒ 用户要求「宽度随层数 +50%」**原样保留**（它本来就在 `WaveRadius` 里，
        ///        这里只是把它当成"墙有多长"来铺，没有第二份数）✓
        ///
        /// ── 池账 ──
        ///   墙身 16 + 白沫下层 16 + 白沫上层 16 + 水痕 4 = **52**（`Slice` 池 48 片 + `Bit` 池 4 片）
        ///   —— 那个 `Slice` 池**主要服务锄头刀光**，而浪是水壶的技能，两个武器互斥（一次只有一把在手）
        ///   ⇒ 稳态不会互相挤掉 ✓
        ///   ⚠️ 浪的**间隔 = 寿命**（`PluginExtra2.OnCanAttack` 用 `_waveLeft` 保证"上一道没散不来第二道"）
        ///      ⇒ 正常情况下两道浪不共存；万一将来把间隔调短到寿命以下，第二道会把第一道**最老的片**
        ///      挤掉（池是环形复用）—— 是"少一片"而不是崩溃，届时该把 `WaveSegs` 调回 12~14 ✓
        /// </summary>
        /// <param name="center">玩家脚下（世界 XZ）</param>
        /// <param name="moveDir">推进方向（单位向量；退化时退到 `Vector2.up`）</param>
        /// <param name="width">墙的横向长度（米）= 判定宽度，随层数 +50%</param>
        /// <param name="fx">外观（吃水壶这一路的插件配色）</param>
        /// <param name="life">存在时长（秒），规格 0.5</param>
        /// <param name="advance">推进距离（米），规格 2~3</param>
        public static void SpawnWaveWall(Vector2 center, Vector2 moveDir, float width,
                                         in FxSpec fx, float life, float advance)
        {
            if (!_ready) return;
            WavesSpawned++;

            Vector2 dn = moveDir.sqrMagnitude > 1e-4f ? moveDir.normalized : Vector2.up;
            float yawDeg = Mathf.Atan2(dn.x, dn.y) * Mathf.Rad2Deg;
            float ang = yawDeg * Mathf.Deg2Rad;
            var origin = new Vector3(center.x, GroundY, center.y);

            float w = Mathf.Max(1.0f, width);
            float adv = Mathf.Max(0.5f, advance);
            float segW = w / WaveSegs * 1.06f;      // 6% 重叠 ⇒ 段与段之间不露缝
            float stepCF = w / Mathf.Max(1, WaveSegs - 1);
            float mid = (WaveSegs - 1) * 0.5f;

            Color body = Color.Lerp(fx.Valid ? fx.Core : new Color(0.36f, 0.72f, 1f, 1f),
                                    new Color(0.30f, 0.68f, 1f, 1f), 0.45f);
            body.a = 1f;
            Color foam = Color.Lerp(body, Color.white, 0.82f);
            foam.a = 1f;

            for (int pass = 0; pass < 3; pass++)
            {
                // ★ v308：pass 0 = 墙身，pass 1 = 白沫下层，pass 2 = 白沫上层（更细更高）
                bool isFoam = pass > 0;
                bool isTop = pass == 2;
                for (int i = 0; i < WaveSegs; i++)
                {
                    Piece p = Take(PoolKind.Slice);
                    if (p == null) break;
                    // ★ v304：`Slice` 池的**默认材质**是刀光那 3 份（带 256×32 刃纹贴图）。
                    //   浪墙不是刀光 ⇒ 显式换回**共享无贴图材质** `_mat`（只换指针，不 new）✓
                    //   下一次 `SpawnSwing` 会各自再设回去，互不影响。
                    if (_mat != null) p.Mr.sharedMaterial = _mat;
                    float off = (i - mid) * stepCF;
                    p.Mode = ModeWave;
                    p.Grad = false;
                    p.Sweep = false;
                    p.Origin = origin;
                    p.Angle = ang;
                    p.YawDeg = yawDeg;
                    p.R0 = off;                                        // 侧向偏移
                    p.R1 = adv;                                        // 推进距离
                    // 前凸：越靠中段越往前，读成"弧"而不是一根直板
                    // （★ v312：字面量 `0.28f` 提成 `WaveWallBulge` —— 调用点要用它算"最远视觉点"）
                    p.Depth = WaveWallBulge * (1f - Mathf.Abs(off) / Mathf.Max(0.01f, w * 0.5f));
                    p.Chord = segW;
                    p.Thick = isTop ? WaveFoamThick2 : (isFoam ? WaveFoamThick : WaveHeight);
                    p.Lift = isTop ? WaveFoamLift + WaveFoamLift2 : (isFoam ? WaveFoamLift : 0f);
                    // 浪峰抬起（先卷后落）：上层的起伏更大一点 ⇒ 两层不会永远平行
                    p.S1 = isTop ? WaveCrest * 0.72f : (isFoam ? WaveCrest * 0.55f : WaveCrest);
                    // 白沫上层再往白里推一档 ⇒ 两层之间有明度差（"细碎"读得出来）
                    p.Col = isTop ? Color.Lerp(foam, Color.white, 0.45f) : (isFoam ? foam : body);
                    p.A0 = isTop ? 0.85f : (isFoam ? 0.95f : 0.80f);
                    p.Life = Mathf.Max(0.08f, life);
                    p.Age = 0f;
                    Activate(p);
                }
            }

            // ★ v308：**水痕 ×4**（贴地、渐隐）—— 浪墙推过去之后，路径上留下几段短促的水印。
            //   实现：`ModeRadial` 在 `adv × f` 处放一片**很薄很扁**的亮片（`Thick` = 0.03 ⇒ 贴地），
            //   `Age` 取负 ⇒ 浪头走到它那一刻才亮；`Life` 取 `T×(1−f)×0.9` ⇒ 正好在浪消失前后收掉。
            //   池：`Bit`（4 个；与爆炸/火星那批不重叠 —— 浪是水壶的，爆炸是肥料的）✓
            Color trailC = Color.Lerp(body, Color.white, 0.30f);
            trailC.a = 1f;
            for (int i = 0; i < WaveTrailSegs; i++)
            {
                float f = 0.22f + i * 0.20f;                    // 0.22 / 0.42 / 0.62 / 0.82（沿推进方向排开）
                float r = adv * f;                              // 这一段水痕离玩家的距离
                Piece g = Take(PoolKind.Bit);
                if (g == null) break;
                g.Mode = ModeRadial;
                g.Grad = false;
                g.Sweep = false;
                g.Origin = origin;
                g.Angle = ang;
                g.YawDeg = yawDeg;
                g.R0 = r;
                g.R1 = r;                                       // 不张开（水痕是留下的，不是推出去的）
                // X 尺寸 = r × Chord ⇒ 要横跨整道墙的宽度：Chord = w / r
                g.Chord = w / Mathf.Max(0.05f, r);
                g.Thick = 0.03f;                                // 贴地（读作地面上的水印）
                g.Depth = 0.30f;                                // 径向很薄的一段
                g.Col = trailC;
                g.A0 = 0.32f;                                   // 比浪身暗 ⇒ 是"痕迹"不是"墙"
                g.Life = Mathf.Max(0.10f, Mathf.Max(0.08f, life) * (1f - f) * WaveTrailLifeScale);
                g.Age = -Mathf.Max(0.08f, life) * f;            // 浪头到它才亮
                Activate(g);
            }
        }

        // ── ② 香蕉皮：**从天而降** ───────────────────────────────────────
        /// <summary>香蕉皮的下落高度（米）—— 用户给的范围是 6~8，取中间值。</summary>
        public const float PeelDropHeight = 7.0f;
        /// <summary>香蕉皮落到地面用的秒数（与"逻辑等到落地才结算"**同一个数**，见 `PluginExtra2`）。</summary>
        public const float PeelFallSeconds = 0.45f;
        /// <summary>落地后还留多久（秒）—— 压扁回弹 + 淡出。</summary>
        private const float PeelSettleSeconds = 0.55f;
        /// <summary>
        /// 香蕉皮的**视觉**尺寸倍率（`BuildPeelMesh` 的 `r1` = 0.62m 是网格自身半径
        /// ⇒ 世界半径 = 0.62 × 本值，世界**直径** = 1.24 × 本值）。
        /// ★ v312：**2.0 → 2.8**（×1.40，与判定半径 1.7 → 2.4 的 ×1.41 同步）
        ///   ⇒ 世界半径 0.62×2.8 = **1.74m**、直径 **3.47m**。
        ///   为什么不拉到"和判定一样大"（那要 2.4/0.62 = 3.87 ⇒ 4.8m 宽的香蕉皮）：
        ///   皮会被撑成一坨认不出的黄块，而"这东西在这儿"的读法只需要它比人大一圈 ✓
        ///
        /// ★★ v313：**这现在是唯一的范围读法，而且比判定小 27.5%** ★★
        ///   原来它下面还有一圈深绿扁圆（`PeelStainRadius` = 2.25 ⇒ 直径 4.5m ≈ 判定 4.8m 的 94%），
        ///   玩家靠那滩渍判断"踩到哪儿会挨打"；用户要求「掉落特效的绿色判定框删除」⇒ 那滩渍**已整块删掉**
        ///   ⇒ 现在只剩皮模型：**世界半径 1.74m vs 判定半径 2.4m ⇒ 看起来比实际小 27.5%**
        ///     （判定循环里还再加敌人的 `e.Radius` ⇒ 真实边界比 2.4m 更外一圈，差距更大）。
        ///   ⚠️ 这是**用户明确要求并接受的后果**，不要擅自加回替代物（不要环、不要描边、不要淡圈）✗
        ///   本值 2.8 **不动** —— 用户说的是删判定框，不是把皮放大 ✓
        /// </summary>
        private const float PeelScale = 2.8f;

        /// <summary>
        /// ★ v304：**一颗从天而降的香蕉皮**（用户需求②）。
        ///
        /// ── 与逻辑的**同步**（这是本条最要紧的判断，写在这里当下次改动的依据）──
        ///   · 视觉：本函数让皮从 `PeelDropHeight`(7m) 高处、`PeelFallSeconds`(0.45s) 内落到 `at`
        ///     ⇒ **它落地的那一刻 = 生成后 0.45s**
        ///   · 逻辑：`PluginExtra2` 的伤害结算改成**同样等 0.45s**（见 `PluginExtra2.PendingPeel`）
        ///   ⇒ 两者读**同一个常量**（本类的 `PeelFallSeconds`，由 `PluginExtra2` 直接引用）
        ///      ⇒ 不存在"数字延迟一秒、砸坑延迟 0.45 秒"的第二份真相 ✓
        ///   ⚠️ 为什么选"让逻辑等"而不是"让视觉一开始就在落点"：
        ///      用户要的就是"从天上砸下去"。如果视觉一开始就在落点，那就没有"砸"这个动作；
        ///      而如果逻辑仍瞬间结算，敌人会在皮还在 7 米高时就掉血 —— 那是**假反馈**，
        ///      玩家会读到"这个插件不落地也生效"✗ 代价：`can_sewer` 的伤害晚 0.45s 到账
        ///      （这是本轮唯一一处**故意的**时序改动，见报告⑧）。
        /// </summary>
        /// <param name="at">落点（世界 XZ）</param>
        /// <param name="fx">外观（沼绿）</param>
        public static void SpawnPeelFall(Vector2 at, in FxSpec fx)
        {
            if (!_ready || _peelMesh == null) return;
            Piece p = Take(PoolKind.Peel);
            if (p == null) return;
            PeelsSpawned++;

            // 黄褐是主体，掺 30% 沼绿 ⇒ 既是"香蕉皮"，也读得出是"污水掉出来的"
            Color shell = new Color(0.86f, 0.68f, 0.20f, 1f);
            if (fx.Valid) shell = Color.Lerp(shell, fx.Rim, 0.30f);
            shell.a = 1f;

            p.Mode = ModePeel;
            p.Grad = false;
            p.Sweep = false;
            p.Lift = 0f;
            p.Origin = new Vector3(at.x, 0f, at.y);                 // 落点 XZ
            p.End = new Vector3(at.x, GroundY, at.y);               // 静止高度
            p.R0 = PeelFallSeconds;                                 // 下落秒数
            p.R1 = PeelDropHeight;                                  // 起始高度
            p.S0 = PeelScale;
            // 翻滚：固定初相位 + 固定角速度（不用随机 ⇒ 每次砸下来都一致）
            p.Rot0 = new Vector3(0f, 34f, 0f);
            p.Spin = new Vector3(310f, 190f, -120f);
            p.Col = shell;
            p.A0 = 1f;
            p.Life = PeelFallSeconds + PeelSettleSeconds;
            p.Age = 0f;
            Activate(p);
            SpawnPeelTrailAt(at, in fx);
            // ★ v313：这里原来还有一行 `SpawnPeelStain(at, PeelStainRadius, in fx)`
            //   —— 落地处一圈**深绿扁圆**。用户：「掉落特效的绿色判定框删除」⇒ 整块删掉，
            //   **没有加任何替代物**（用户明确要删，不是要换个画法）。
            //   落地那一刻的反馈仍然在：`PluginExtra2.LandPeel` 的泥点（`SpawnMudSplash`）+ 轻屏震 ✓
            //   ⇒ 代价：皮模型的世界半径 1.74m &lt; 判定半径 2.4m ⇒ 范围看起来比实际**小 27.5%**
            //     （见 `PeelScale` 的注释，这是已知且被用户接受的结果）
        }

        /// <summary>
        /// ★ v307：**香蕉皮下落轨迹上的拖尾**（`can_sewer` 升级 ①）——
        ///   用户：「升级**下落轨迹上的拖尾**（空气中留几段渐隐的绿色弧）」。
        ///
        /// ── 为什么可以在**落地前**就把整条拖尾一次摆好 ──
        ///   香蕉皮的下落位置在 `ModePeel` 里是**寿命的解析函数**
        ///   （`h = R1·(1−u²)`，见 `Step`）⇒ 第 k 段拖尾在哪个时刻、哪一点出现，
        ///   落地那一刻就能全部算出来。于是：
        ///     · 位置 = 那一刻皮所在的高度（`PeelDropHeight·(1−u²)`）
        ///     · 时刻 = 用 **负 `Age`** 表达（`fade` 对 `Age < 0` 输出 0 ⇒ 还没轮到它就不亮）
        ///   ⇒ **不需要任何额外的运行时状态 / 计时器 / 回调**，一遍循环摆完 ✓
        ///   这也是本文件一直在用的手法（刀光片的逐片"扫过"用的就是负 `Age`）。
        ///
        /// ── 为什么是"弧"而不是一条竖直虚线 ──
        ///   每一段再绕下落轴**螺旋偏移** `TrailSwirl` 米 ⇒ 俯视是一条螺旋，不是一串点 ✓
        /// 池账：`PeelTrailSegs`=5 格 × `PeelTrailLife`=0.22s，走 `Bit` 池 ✓
        /// </summary>
        private static void SpawnPeelTrailAt(Vector2 at, in FxSpec fx)
        {
            const int segs = 5;
            const float swirl = 0.16f;      // 绕下落轴的螺旋半径（米）
            const float life = 0.22f;       // 每段自己活多久
            float tf = PeelFallSeconds;
            Color trail = fx.Valid
                ? Color.Lerp(new Color(0.42f, 0.66f, 0.24f, 1f), fx.Rim, 0.45f)
                : new Color(0.42f, 0.66f, 0.24f, 1f);
            trail.a = 1f;

            for (int k = 0; k < segs; k++)
            {
                // 第 k 段出现在 `u` 处（从 1/6 到 5/6，避开"刚出手"与"已落地"两端）
                float u = (k + 1) / (float)(segs + 1);
                float t = tf * u;                       // 它在第几秒出现
                float h = GroundY + PeelDropHeight * (1f - u * u);   // 与 `ModePeel` 同一个式子
                float ang = k * 2.39996f;               // 黄金角 ⇒ 螺旋均匀、不重叠
                float size = Mathf.Lerp(0.20f, 0.09f, u);   // 越往下越小（越接近落地越散）

                Piece q = Take(PoolKind.Bit);
                if (q == null) break;
                q.Mode = ModeBallistic;
                q.Grad = false;
                q.Sweep = false;
                q.Hold = false;
                q.Lift = 0f;
                q.Pos = new Vector3(at.x + Mathf.Cos(ang) * swirl, h, at.y + Mathf.Sin(ang) * swirl);
                q.Vel = Vector3.zero;      // 拖尾留在空气里不动（"留几段"就是这个意思）
                q.Gravity = 0f;
                q.S0 = size;
                q.S1 = size * 0.55f;
                q.Rot0 = Vector3.zero;
                q.Spin = Vector3.zero;
                q.Col = trail;
                q.A0 = 0.72f;
                q.Life = life;
                q.Age = -t;                // ★ 负 Age = "还没轮到它"（`fade` 输出 0）
                Activate(q);
            }
        }

        /// <summary>
        /// ★★ v314：**天使羽毛飘落** —— 道具「天使降临」复活那 2 秒的唯一表现 ★★
        ///
        /// 用户原话：「获得时死亡会暂停时间 2 秒，**天使羽毛飘落**，并完成一次复活」。
        ///
        /// ── 硬约束（本文件一贯的三条，这一发没有例外）──
        ///   ① **零美术资产**：用池里默认的 Cube 网格，缩放成 `0.075 × 0.012 × 0.42` 米的细长方片
        ///      （共享网格、共享材质，颜色走 `MaterialPropertyBlock`）✓
        ///   ② **运行期零 Instantiate / Destroy**：8 片全部从 `Bit` 池 `Take`（池 64 格，
        ///      寿命 ≤ 2.3s ⇒ 同时最多 8 格，与其它特效不抢）✓
        ///   ③ **不吃 `World.Rng`**：位置/相位由**黄金角** `i·2.39996` 与片序号算出
        ///      ⇒ 一局的确定性不受影响（与 `CombatFeedback` 用 `unscaledTime` 哈希同一个思路）✓
        ///
        /// ── 为什么必须是"墙钟"推进（`Realtime = true`）──
        ///   这一发**恰好**发生在 `timeScale = 0.05` 的停帧里。若按 `Time.deltaTime` 积分，
        ///   2 秒墙钟只等于 0.1 秒粒子时间 ⇒ 羽毛几乎不动、随后被墙钟兜底抹掉 ✗
        ///   用户要的画面正是"时间停了、羽毛还在落" ⇒ 走未缩放时间（见 `Piece.Realtime`）✓
        ///
        /// ── 参数 ──
        ///   `at`     世界平面坐标（玩家脚下），落点以它为中心用黄金角散开 0.55~1.39 米
        ///   `seconds` 停帧秒数（= 表里 `angel_descent` 的 `ruleValue`），同时作为羽毛寿命；
        ///             钳到 `[FeatherLifeMin, FeatherLifeMax]`（见那两个常量）
        /// ── 颜色 ──
        ///   暖白 `(1.00, 0.965, 0.88)` 为主，每 3 片掺一片金 `(1.00, 0.845, 0.42)`
        ///   ⇒ 读作"圣光/羽翼"，而不是雪花 ✓
        /// </summary>
        public static void SpawnAngelFeathers(Vector2 at, float seconds)
        {
            if (!_ready) return;                       // 编辑模式 / 任一平台未建成：静默跳过（表现层不该拦逻辑）
            float life = Mathf.Clamp(seconds > 0f ? seconds : 2f, FeatherLifeMin, FeatherLifeMax);

            Color warmWhite = new Color(1.00f, 0.965f, 0.880f, 1f);
            Color warmGold = new Color(1.00f, 0.845f, 0.420f, 1f);

            for (int i = 0; i < AngelFeatherCount; i++)
            {
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;                  // 池被别的特效占满 ⇒ 画多少算多少（不阻塞复活）
                AngelFeathers++;

                // 黄金角散开 + 三档半径：8 片的落点既不重叠、也不吃掉随机数
                float ang = i * 2.39996f;
                float rad = 0.55f + 0.42f * (i % 3);

                p.Mode = ModeFeather;
                // ★ 池化字段**逐个显式赋值**（`Take` 只清 Lift/Hold/UseMesh/Realtime 四项）
                p.Realtime = true;
                p.Grad = false;
                p.Sweep = false;
                p.Hold = false;
                p.Lift = 0f;
                p.Origin = new Vector3(at.x, GroundY, at.y);
                p.End = new Vector3(at.x + Mathf.Cos(ang) * rad, GroundY, at.y + Mathf.Sin(ang) * rad);
                p.R0 = life;                            // 下落秒数 = 寿命（落到底的同时淡到 0）
                p.R1 = FeatherDropHeight;
                p.Angle = ang * 1.7f;                   // 摇摆相位（逐片不同）
                p.Chord = FeatherSwayX;
                p.Depth = FeatherSwayZ;
                p.S0 = FeatherWidth;
                p.Thick = FeatherThick;
                p.S1 = FeatherLen;
                p.Rot0 = new Vector3(0f, ang * Mathf.Rad2Deg, 12f + i * 9f);
                p.Spin = new Vector3(150f, 210f, -170f);   // 缓慢翻转（2s 约 0.5 圈）
                p.Col = (i % 3 == 0) ? warmGold : warmWhite;
                p.A0 = 1f;
                p.Life = life;
                // 错峰：负 Age = "还没轮到它"（`Step` 对 Age < 0 输出 alpha 0，位置先摆好）
                p.Age = -0.05f * (i % 4);
                Activate(p);
            }
        }

        /// <summary>
        /// **肥料爆炸** —— `FertilizerBlastEvent`（`RunSession.ExplodeAt` 主爆 / 子爆；`Actors.cs` 自爆）。
        ///   ① 冲击环：16 段沿圆排布，半径 0 → `Radius`，0.25s 淡出
        ///   ①b ★ v309：**二次环**（16 段，`−0.12s` 的负 `Age` ⇒ 主环之后再张一圈，
        ///      更大 1.18×、更淡 0.52、更薄、更白）⇒ 读作"冲击波推出去的第二道" ✓
        ///   ② 12 颗土色碎粒：向外 + 向上抛，重力 14，0.60s 消失
        ///   ③ ★ v309：**10 团立体烟雾**，每团 **4~6** 个球，向外飘 + 向上浮（越飘越慢），
        ///      尺寸 0.25 → 0.90 m、灰度 0.25 → 0.55（**团内后出来的球更亮更暖**：
        ///      灰度推到 0.52 → 0.80、A0 推到 1.55×、往暖橙推 55%、出场晚 0.02·b 秒），
        ///      0.8~1.2s 淡出 ⇒ 体积堆叠、向上翻滚，且有"火光穿透烟雾"的层次 ✓
        /// ★ 半径**不乘** `FxSpec.Scale`：`Radius` 已经是判定的真实半径，
        ///   再乘一次就会画出"大于真炸范围"的圈（`WorldView.cs:3539` 的纪律）✓
        /// ★ v309 池账（`Bit` 16+16+12 = 44 ≤ 64 · `Mist` 10×6 = 60 ≤ 64）✓
        /// </summary>
        public static void SpawnBlast(in FertilizerBlastEvent e)
        {
            if (!_ready) return;
            Blasts++;

            FxSpec fx = PluginFx.Or(e.Fx, WeaponSlot.Fertilizer);
            float R = Mathf.Max(0.30f, e.Radius);
            var center = new Vector3(e.Pos.x, GroundY, e.Pos.y);

            // ① 冲击环：16 段，X 尺寸随半径缩放（r × chord）⇒ 张开的过程中**不断开**
            float chord = Mathf.PI * 2f / RingSegs * 1.10f;
            Color ringCol = Color.Lerp(fx.Rim, Color.white, 0.22f);
            ringCol.a = 1f;
            for (int i = 0; i < RingSegs; i++)
            {
                float ang = i * (Mathf.PI * 2f / RingSegs);
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                p.Mode = ModeRadial;
                p.Grad = false;
                p.Origin = center;
                p.Angle = ang;
                p.YawDeg = ang * Mathf.Rad2Deg;
                p.R0 = R * 0.04f;          // 从 0 涨起
                p.R1 = R;                  // 涨到真实爆炸半径
                p.Chord = chord;
                p.Thick = 0.07f;
                p.Depth = Mathf.Max(0.08f, R * 0.045f);
                p.Col = ringCol;
                p.A0 = 0.90f;
                p.Life = RingLife;
                p.Age = 0f;
                Activate(p);
            }

            // ── ★ v309：**二次环**（用户：「加二次环（0.12 秒后一个更淡更大的环，读作冲击波扩散）」）──
            //   形态与①**同构**（16 段、同一个 `ModeRadial`、同一个池），只改四个数：
            //     · 半径更大：`R → R × EchoRingScale(1.18)`
            //     · 更淡：`A0 = 0.52`（主环 0.90）
            //     · 更薄：`Thick 0.07 → 0.05`、径向 `Depth 0.045R → 0.030R`
            //     · 更白：往白里推 45% ⇒ 一眼分得出"第二圈是空气被推开的痕迹"
            //   **怎么做到"0.12 秒后才出现"而不用任何计时器 / 状态**：
            //     出生就把 `Age` 置成 **−EchoRingDelay(−0.12)** —— `Step` 对 `Age < 0`
            //     输出 alpha **0**（这条机制刀光的"逐片扫过"与香蕉皮拖尾都在用）⇒
            //     前 0.12s 它在屏幕上完全不可见，`Age` 跨过 0 之后才开始张开、开始显形 ✓
            //   池账：16 格（`Bit`）× 0.12s 延迟 + 0.25s 寿命 ⇒ 与其他爆炸件同一帧不同窗口：
            //     主环 16 + 二次环 16 + 土粒 12 = **44 ≤ 64** ✓
            {
                float eChord = chord * 1.18f;
                Color eCol = Color.Lerp(ringCol, Color.white, 0.45f);
                eCol.a = 1f;
                for (int i = 0; i < RingSegs; i++)
                {
                    float ang = i * (Mathf.PI * 2f / RingSegs);
                    Piece p = Take(PoolKind.Bit);
                    if (p == null) break;
                    EchoRings++;
                    p.Mode = ModeRadial;
                    p.Grad = false;
                    p.Sweep = false;             // 池化字段必须显式清（`Take` 只清 Lift/Hold/UseMesh）
                    p.Hold = false;
                    p.Origin = center;
                    p.Angle = ang;
                    p.YawDeg = ang * Mathf.Rad2Deg;
                    p.R0 = R * 0.04f;
                    p.R1 = R * EchoRingScale;
                    p.Chord = eChord;
                    p.Thick = 0.05f;
                    p.Depth = Mathf.Max(0.06f, R * 0.030f);
                    p.Col = eCol;
                    p.A0 = EchoRingAlpha;
                    p.Life = RingLife;
                    p.Age = -EchoRingDelay;      // ★ 负 Age = 0.12s 后才亮
                    Activate(p);
                }
            }

            // ② 12 颗土色碎粒
            Color soil = Color.Lerp(new Color(0.46f, 0.34f, 0.21f, 1f), fx.Core, 0.35f);
            soil.a = 1f;
            for (int i = 0; i < DirtBits; i++)
            {
                Piece p = Take(PoolKind.Bit);
                if (p == null) break;
                DirtSpawned++;
                // 球面均分（黄金角）：爆炸是球壳向外炸，不能只在水平面撒
                float a = i * 2.39996f;
                float yy = 1f - 2f * (i + 0.5f) / DirtBits;
                float rr = Mathf.Sqrt(Mathf.Max(0f, 1f - yy * yy));
                var outw = new Vector3(Mathf.Cos(a) * rr, Mathf.Abs(yy) * 0.85f + 0.25f, Mathf.Sin(a) * rr);
                p.Mode = ModeBallistic;
                p.Grad = false;
                p.Pos = center + Vector3.up * 0.18f;
                p.Vel = outw.normalized * UnityEngine.Random.Range(2.2f, 4.4f) * Mathf.Min(1f, R * 0.5f + 0.45f);
                p.Gravity = DirtGravity;
                p.S0 = UnityEngine.Random.Range(0.09f, 0.17f);
                p.S1 = p.S0 * 0.30f;
                p.Rot0 = new Vector3(UnityEngine.Random.Range(0f, 360f),
                                     UnityEngine.Random.Range(0f, 360f),
                                     UnityEngine.Random.Range(0f, 360f));
                p.Spin = new Vector3(UnityEngine.Random.Range(-360f, 360f),
                                     UnityEngine.Random.Range(-360f, 360f),
                                     UnityEngine.Random.Range(-360f, 360f));
                p.Col = soil;
                p.A0 = 1f;
                p.Life = DirtLife;
                p.Age = 0f;
                Activate(p);
            }

            // ③ 立体烟雾：**10 团 × 4~6 球**（≤ 60 个球 ≤ Mist 池 64）✓
            // 一团 = 同一个 Pos/Drift/Rise 母体 + 各球在团内的轨道偏移 ⇒
            // 球与球**错开、互相穿插**，读起来是"一团烟"，而不是"一堆球"✓
            Color smokeTint = Color.Lerp(fx.Rim, fx.Core, 0.20f);
            smokeTint.a = 1f;
            for (int i = 0; i < SmokePuffs; i++)
            {
                float a = i * 2.39996f + 0.7f;
                var dirOut = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector3 spawn = center + dirOut * (R * 0.12f) + Vector3.up * UnityEngine.Random.Range(0.10f, 0.30f);
                float speed = UnityEngine.Random.Range(0.8f, 1.8f);                       // 规格 0.8~1.8 m/s
                Vector3 drift = dirOut * speed;
                float rise = UnityEngine.Random.Range(0.4f, 1.0f);                        // 规格 0.4~1.0 m/s
                float life = UnityEngine.Random.Range(SmokeLifeMin, SmokeLifeMax);        // 0.8~1.2s
                int blobs = UnityEngine.Random.Range(SmokeBlobsMin, SmokeBlobsMax + 1);   // ★ v309：4~6 球

                // ★ v309：**团内越晚出现的球越亮**（用户：「模拟火光穿透烟雾」）
                //   `bq` = 这个球在团内排第几（0 = 最先、1 = 最后）⇒ 四件事一起往上推：
                //     ① 灰度更高（0.25/0.55 → 0.52/0.80）② 更实（A0 ×0.80 → ×1.55）
                //     ③ 更暖（往 `SmokeHot` 推 0~55%）④ 出场略晚（`Age = −0.02·b`）
                //   ⇒ 一团烟里"先出来的是深灰、后冒出来的是被火照亮的那几个球" ✓
                for (int b = 0; b < blobs; b++)
                {
                    float bq = blobs <= 1 ? 1f : b / (float)(blobs - 1);
                    Piece p = Take(PoolKind.Mist);
                    if (p == null) break;
                    SmokeSpawned++;
                    p.Mode = ModeMist;
                    p.Grad = false;
                    p.Pos = spawn;
                    p.Drift = drift * UnityEngine.Random.Range(0.85f, 1.15f);
                    p.Rise = rise * UnityEngine.Random.Range(0.90f, 1.10f);
                    // 团内偏移：4~6 个球绕团心错开 ⇒ 有体积、有前后层
                    p.OrbitRad = UnityEngine.Random.Range(0.06f, 0.16f);
                    p.OrbitAng = b * (Mathf.PI * 2f / Mathf.Max(1, blobs)) + UnityEngine.Random.Range(-0.40f, 0.40f);
                    p.OrbitSpeed = UnityEngine.Random.Range(-1.2f, 1.2f);                 // 慢慢翻滚
                    p.YOff = UnityEngine.Random.Range(-0.05f, 0.10f);
                    // 单球尺寸取整团尺寸的 55%~75% ⇒ 若干球叠加后的外廓 0.25 → 0.90 ✓
                    p.S0 = SmokeSize0 * UnityEngine.Random.Range(0.55f, 0.75f);
                    p.S1 = SmokeSize1 * UnityEngine.Random.Range(0.55f, 0.75f);
                    // ★ v309：只作"染色"，灰度由 `Gray0→Gray1` 决定；后出的球往暖橙里推
                    p.Col = Color.Lerp(smokeTint, SmokeHot, bq * SmokeHotMix);
                    p.Gray0 = Mathf.Lerp(SmokeGray0, SmokeGray0Later, bq);
                    p.Gray1 = Mathf.Lerp(SmokeGray1, SmokeGray1Later, bq);
                    p.A0 = SmokeAlpha * Mathf.Lerp(SmokeAlphaFirst, SmokeAlphaLast, bq);
                    p.Life = life * UnityEngine.Random.Range(0.92f, 1.08f);
                    p.Age = -SmokeBlobStagger * b;      // 后出的球稍晚才亮（负 Age ⇒ alpha 0）
                    Activate(p);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 池 / 每帧推进
        // ══════════════════════════════════════════════════════════════

        /// <summary>环形取一个槽位 —— 光标一直往前走 ⇒ 拿到的永远是**最老的那个**（交付要求）。</summary>
        private static Piece Take(PoolKind kind)
        {
            Pool p = Pools[(int)kind];
            if (p == null || p.Items == null || p.Items.Length == 0) return null;
            Piece piece = p.Items[p.Cursor];
            p.Cursor = (p.Cursor + 1) % p.Items.Length;   // ★ v304：格数改读 `Items.Length`（不再写死 `PoolPerKind`）
            // ★ v304：**跨次复用的脏字段必须在这里清** —— `Lift` 是池里新加的抬升量，
            //   若上一个使用者是浪墙（`Lift`≈0.97），而这一位被 `SpawnGroundRing` 借去当水环，
            //   不清理就会把那圈水环整体抬到浪顶高 ✗（这类"复用留下旧值"的坑一次都嫌多）
            //   `Take` 一定发生在**调用方赋值之前** ⇒ 现在的清理顺序是对的 ✓
            piece.Lift = 0f;
            // ★ v306：`Hold` 是同一类脏字段（巨型的整圈把它打开）。不清的话，
            //   下一位借到这个格子的刀光片会**一直不衰减**（`1 − k⁴` 在 k=0.5 还有 0.94）
            //   ⇒ 半圆刀光会变成"整片挂住"，那是"顺手改了另外四种特效"✗
            piece.Hold = false;
            // ★ v307：`UseMesh` 是同一类脏字段（换形的那一发把它打开）。
            //   不清的话，下一位借到这个格子的**刀光片**会挂着**上一发换过的网格** ✗
            piece.UseMesh = null;
            // ★ v314：`Realtime` 是同一类脏字段（复活的羽毛把它打开）。
            //   不清的话，下一位借到这个格子的特效会在顿帧里"照常快进"✗
            piece.Realtime = false;
            return piece;
        }

        private static void Activate(Piece p)
        {
            if (p == null) return;
            p.Active = true;
            p.SpawnRealtime = Time.unscaledTime;   // ★ v279：墙钟出生时刻（寿命兜底用）
            // ★ v307：换形（贴地扁圆 / 电光球 / 护壤的六边形）—— 只改一个共享引用，不建 Mesh ✓
            //   `UseMesh == null` ⇒ 回到本池默认网格 ⇒ 全部旧调用点**逐字不变** ✓
            Mesh want = p.UseMesh != null ? p.UseMesh : p.BaseMesh;
            if (want != null && p.Mf != null && p.Mf.sharedMesh != want) p.Mf.sharedMesh = want;
            p.Mr.enabled = true;
            Step(p, 0f);      // 立刻摆到第 0 帧的位置 ⇒ 不会有一帧停在原点
        }

        /// <summary>
        /// ★ v279：墙钟寿命上限（秒）——**不看 `Age`、不看 `dt`**。
        /// 最长的一档是立体烟雾（`SmokeLifeMax` 1.20 × 1.08 ≈ 1.30s，再加刀光片的负 Age 偏移
        /// ≤ 0.09s）⇒ 2.5s 不会误杀正常特效，又能在"没人推进 / 推进被 dt=0 挡住"时
        /// 给出一个**确定的**回收时刻 ✓
        /// </summary>
        private const float MaxRealtimeLife = 2.5f;

        /// <summary>每帧一次（宿主 `Update`）。**不用 `Instantiate` / `Destroy`**，只改 transform 与颜色。</summary>
        public static void Tick(float dt)
        {
            if (!_ready) return;
            // ★ v279：`dt <= 0` **不再直接 return** ★
            //   改前 `if (dt <= 0f) return;` 在清理之前返回 ⇒ 顿帧（`timeScale` 被压到 0.05）
            //   或暂停期间，粒子既不推进、也**永远不会被回收**，停在中途一直画。
            //   改后：dt <= 0 只表示"这一帧不积分、不重摆"，墙钟兜底（`MaxRealtimeLife`）照跑 ⇒
            //   最多 2.5s 一定收干净，与"有没有人推进"无关 ✓
            bool frozen = dt <= 0f;
            if (dt > 0.1f) dt = 0.1f;                // 卡顿过后的那一帧不让粒子瞬移

            // ★ v314：**墙钟口径的第二条时间轴** —— 只有 `p.Realtime` 的片子用它（见 `Piece.Realtime`）。
            //   钳制同 `dt`（0.1s），否则切场景/断点回来的那一帧会让羽毛瞬移。
            //   ⚠️ 对**没有** `Realtime` 的片子，下面那两个局部量与改前逐位相同 ⇒ 其余五种特效不变 ✓
            float realDt = Time.unscaledDeltaTime;
            bool realFrozen = realDt <= 0f;
            if (realDt > 0.1f) realDt = 0.1f;

            // 刀光贴图沿 X 流动（刃纹"扫"起来）。只改一个材质的 UV 偏移 ⇒ 每帧 1 次调用，不是每个粒子 ✓
            if (!frozen) ScrollBladeUv(dt);

            int live = 0;
            for (int k = 0; k < PoolKindCount; k++)
            {
                Pool pool = Pools[k];
                if (pool == null || pool.Items == null) continue;
                for (int i = 0; i < pool.Items.Length; i++)
                {
                    Piece p = pool.Items[i];
                    if (p == null || !p.Active) continue;

                    // ① 墙钟兜底 —— 先于积分，且与 `dt` 无关
                    if (Time.unscaledTime - p.SpawnRealtime >= MaxRealtimeLife)
                    {
                        p.Active = false;
                        p.Mr.enabled = false;
                        continue;
                    }

                    // ② 冻结帧：不积分、不重摆（保留上一帧姿态），但寿命已在 ① 里检查过
                    //    ★ v314：`Realtime` 的片子看的是**墙钟**那一档（顿帧里它不冻结）
                    bool pieceFrozen = p.Realtime ? realFrozen : frozen;
                    float pdt = p.Realtime ? realDt : dt;
                    if (pieceFrozen) { live++; continue; }

                    p.Age += pdt;
                    if (p.Age >= p.Life)
                    {
                        p.Active = false;
                        p.Mr.enabled = false;        // 回收
                        continue;
                    }
                    live++;
                    Step(p, pdt);
                }
            }

            LiveRenderers = live;
        }

        /// <summary>刀光 UV 流动：相位 0..1 循环（用减法取模，不依赖 `Mathf.Repeat`）。
        /// ★ 第五版：要滚的是 **3 份**材质（每份钉在自己的段上）★
        ///   每份的偏移 = **段起点 + 流动相位**，再 wrap 到 [0,1) ⇒ 窗口在整张贴图上滑，
        ///   层 A 的图案周期正好等于段宽（`1/3`）⇒ 滑过一整段后画面**自洽无缝**；
        ///   层 B/C 不周期 ⇒ 磨痕/缺口会**不断地飘过窗口**，这正是"纹理在变"的来源 ✓
        /// `_MainTex`（内置/Sprites）与 `_BaseMap`（URP）**各自先探一遍**再设 ⇒ 换 shader 不报错、不丢纹理 ✓
        /// 每帧只改 3 份材质的偏移（3~6 次调用），不是每个粒子 ✓</summary>
        private static void ScrollBladeUv(float dt)
        {
            if (_bladeMats == null) return;
            _bladeFlow += dt * BladeFlowSpeed;
            if (_bladeFlow > 1f) _bladeFlow -= Mathf.Floor(_bladeFlow);
            if (_bladeFlow < 0f) _bladeFlow += 1f;
            float scaleX = 1f / BladeTexSections;
            for (int s = 0; s < _bladeMats.Length; s++)
            {
                Material m = _bladeMats[s];
                if (m == null) continue;
                float o = s * scaleX + _bladeFlow;      // 段起点 + 流动相位
                o -= Mathf.Floor(o);                    // wrap 到 [0,1)
                var off = new Vector2(o, 0f);
                if (_hasMainTex) m.SetTextureOffset("_MainTex", off);
                if (_hasBaseMap) m.SetTextureOffset("_BaseMap", off);
            }
        }

        /// <summary>一个渲染体的第 N 帧：位置 / 朝向 / 缩放 / 颜色 全是寿命的纯函数。</summary>
        private static void Step(Piece p, float dt)
        {
            float k = p.Life > 0f ? Mathf.Clamp01(p.Age / p.Life) : 1f;
            float growth = Mathf.Sqrt(k);            // 爆炸式张开：先快后慢
            Color outCol = p.Col;

            switch (p.Mode)
            {
                case ModeRadial:
                {
                    float r = Mathf.LerpUnclamped(p.R0, p.R1, growth);
                    float s = Mathf.Sin(p.Angle), c = Mathf.Cos(p.Angle);
                    p.T.position = new Vector3(p.Origin.x + s * r, p.Origin.y + p.Lift, p.Origin.z + c * r);
                    // ★ v308：`SpinDeg` 随剑刃一并删除 ⇒ 这里的欧拉只剩 `YawDeg`（与 v306 逐字相同）
                    p.T.rotation = Quaternion.Euler(0f, p.YawDeg, 0f);
                    p.T.localScale = new Vector3(Mathf.Max(0.01f, r * p.Chord), p.Thick, p.Depth);
                    // ★ 刀光的第二条渐变轴：内缘色 → 外缘色，跟着 growth（张开进度）走
                    if (p.Grad) outCol = Color.Lerp(p.Col, p.ColB, growth);
                    break;
                }
                case ModeAxis:
                {
                    Vector3 d = p.End - p.Origin;
                    float len = d.magnitude;
                    p.T.position = (p.Origin + p.End) * 0.5f;
                    if (len > 1e-4f) p.T.rotation = Quaternion.LookRotation(d / len, Vector3.up);
                    p.T.localScale = new Vector3(p.Thick, p.Thick, Mathf.Max(0.01f, len));
                    break;
                }
                case ModeMist:
                {
                    // 向上浮 + 向外飘（上浮速度随时间衰减 ⇒ "翻滚着散开"而不是"匀速上升"）
                    if (dt > 0f)
                    {
                        p.Rise = Mathf.Max(0f, p.Rise - SmokeRiseDecay * dt);
                        p.Pos += (p.Drift + Vector3.up * p.Rise) * dt;
                    }
                    // 团内偏移随时间缓慢旋转 ⇒ 几个球互相穿插、边界不断变化 = 体积感
                    float ang = p.OrbitAng + p.Age * p.OrbitSpeed;
                    var off = new Vector3(Mathf.Cos(ang) * p.OrbitRad, p.YOff, Mathf.Sin(ang) * p.OrbitRad);
                    p.T.position = p.Pos + off;
                    float s = Mathf.LerpUnclamped(p.S0, p.S1, growth);
                    p.T.localScale = new Vector3(Mathf.Max(0.01f, s), Mathf.Max(0.01f, s), Mathf.Max(0.01f, s));
                    p.T.rotation = Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f);
                    // 灰度 0.25 → 0.55（由深到浅），再掺 22% 插件色 ⇒ 不写死颜色、也仍然读作"烟" ✓
                    float g = Mathf.Lerp(p.Gray0, p.Gray1, k);
                    outCol = Color.Lerp(new Color(g, g, g, 1f), p.Col, SmokeTint);
                    break;
                }
                case ModeWave:
                {
                    // ★ v304：**浪墙**（海浪重做）—— 一道沿 `Angle` 推进的弧形墙 + 浪峰抬起
                    //   · `R1` = 推进距离（米）：`growth = √k` ⇒ 起步快、末段慢（浪头砸下来的观感）
                    //   · `R0` = 这一片在墙上的**侧向偏移**（−半宽 … +半宽）
                    //   · `Depth` = 前凸量：中段比两端更靠前 ⇒ 弧，而不是一根直板
                    //   · `S1` = 浪峰抬升：`sin(π·k)` ⇒ 先卷起来、到末段又落下去
                    //   · `Lift` = 常驻抬升（白沫用的就是这个：整条沫带抬高到浪顶）
                    float a = p.Angle;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    var side = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                    float adv = Mathf.LerpUnclamped(0f, p.R1, growth);
                    float crest = p.S1 * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
                    p.T.position = p.Origin + dir * (adv + p.Depth) + side * p.R0
                                 + Vector3.up * (p.Lift + crest);
                    p.T.rotation = Quaternion.Euler(0f, p.YawDeg, 0f);
                    p.T.localScale = new Vector3(Mathf.Max(0.02f, p.Chord),
                                                 Mathf.Max(0.02f, p.Thick),
                                                 0.16f);   // 径向厚度恒定：墙是一"片"，不是一根柱子
                    break;
                }
                case ModePeel:
                {
                    // ★ v304：**天降香蕉皮** —— 从 `R1` 米高处落到 `End`，`R0` = 下落秒数
                    //   位置是**寿命的解析函数**（`h = drop·(1−u²)` 等价于匀加速），
                    //   不做速度积分 ⇒ 与 `Time.deltaTime` 抖动无关、天然可复现 ✓
                    float tf = Mathf.Max(0.02f, p.R0);
                    Vector3 land = p.End;
                    if (p.Age < tf)
                    {
                        float u = Mathf.Clamp01(p.Age / tf);
                        p.T.position = new Vector3(land.x, land.y + p.R1 * (1f - u * u), land.z);
                        p.T.rotation = Quaternion.Euler(p.Rot0 + p.Spin * p.Age);
                        p.T.localScale = new Vector3(p.S0, p.S0, p.S0);
                    }
                    else
                    {
                        // 落地：停住 + 一下短促的"压扁回弹"（读作"砸进土里"）
                        float v = Mathf.Clamp01((p.Age - tf) / Mathf.Max(0.01f, p.Life - tf));
                        float squash = 1f + 0.30f * Mathf.Exp(-v * 9f) * Mathf.Cos(v * 26f);
                        p.T.position = land;
                        p.T.rotation = Quaternion.Euler(p.Rot0 + p.Spin * tf);
                        p.T.localScale = new Vector3(p.S0 * squash,
                                                     p.S0 / Mathf.Max(0.35f, squash),
                                                     p.S0 * squash);
                    }
                    break;
                }
                case ModeSpiral:
                {
                    // ★ v308：**黑洞的螺旋吸入** —— 位置是寿命的**解析函数**（无速度积分 ⇒ 可复现）：
                    //   · 角度：`Angle + SpiralTurns·e` ⇒ 一路转着往里收（读作"被旋进去"）
                    //   · 半径：`R0 → R1` 也按 `e` 走 ⇒ **角速度与线速度同时变快**
                    //   · 高度：`SpiralY0 → SpiralY1` ⇒ 从贴地升到中心暗球的高度
                    //   `e = k²`：起步慢、末段猛地加速 ⇒ 正是"越靠近中心越快"✓
                    float e = k * k;
                    float r = Mathf.LerpUnclamped(p.R0, p.R1, e);
                    float a = p.Angle + p.SpiralTurns * e;
                    float y = Mathf.LerpUnclamped(p.SpiralY0, p.SpiralY1, e);
                    p.T.position = p.Origin + new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
                    float sp = Mathf.LerpUnclamped(p.S0, p.S1, e);
                    sp = Mathf.Max(0.005f, sp);
                    p.T.localScale = new Vector3(sp, sp, sp);
                    p.T.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
                    break;
                }
                case ModeVortex:
                {
                    // ★ v310：**黑洞的旋转漩涡**（1 秒前摇的主视觉）—— 位置同样是寿命的解析函数：
                    //   · 转角：`Angle + SpiralTurns·k²` ⇒ 角速度 = `2·SpiralTurns·k / Life`
                    //     ⇒ **从静止开始转、越转越快**（用户：「越转越快」）✓
                    //   · 半径：`R0 → R1` 按 **k**（线性）收 ⇒ 臂**整段 1 秒都在**，只是慢慢往里卷
                    //     （对比 `ModeSpiral` 用 k² 收半径 ⇒ 末段才猛地钻进去，那是"碎屑"不是"臂"）
                    //   · 高度：`SpiralY0 → SpiralY1` ⇒ 漩涡是斜的，不是一个平面上的转盘
                    //   · 形状：`S0/S1` = **沿切向的条长**（`Euler(0,a,0)` 下本地 X 就是切向），
                    //     `Thick/Depth` = 另两轴 ⇒ 每颗是一根"短光条"，连起来就是一条螺旋臂 ✓
                    //   纯函数、无随机数、无速度积分 ⇒ 同一串输入逐帧一致 ✓
                    float a = p.Angle + p.SpiralTurns * (k * k);
                    float r = Mathf.LerpUnclamped(p.R0, p.R1, k);
                    float y = Mathf.LerpUnclamped(p.SpiralY0, p.SpiralY1, k);
                    p.T.position = p.Origin + new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
                    float sx = Mathf.Max(0.01f, Mathf.LerpUnclamped(p.S0, p.S1, k));
                    p.T.localScale = new Vector3(sx, Mathf.Max(0.01f, p.Thick), Mathf.Max(0.01f, p.Depth));
                    p.T.rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
                    break;
                }
                case ModeFeather:
                {
                    // ★ v314：**天使羽毛飘落**（复活那 2 秒）—— 位置 / 姿态同样是寿命的纯函数：
                    //   · 高度：`End.y + R1·(1−u)`（匀速下落；羽毛受空气阻力，不做加速）
                    //   · 摇摆：X / Z 两条**不同频率**的正弦，振幅 `0.45 → 1.00` 随下落变大
                    //     ⇒ 高处还在打旋，快落地时飘得更开（"落"而不是"掉"）
                    //   · 自转：`Rot0 + Spin·Age` ⇒ 羽片正反面交替朝镜头，看得出它是**一片**薄片
                    //   · 尺寸：`(S0, Thick, S1)` = 宽 / 厚 / 长 —— 细长方片，不是方块
                    //   淡出走全局的 `1−k²`（k = Age/Life = u）⇒ 落到地面时正好淡到 0 ✓
                    float tf = Mathf.Max(0.02f, p.R0);
                    float u = Mathf.Clamp01(p.Age / tf);
                    float sway = 0.45f + 0.55f * u;
                    float dx = Mathf.Sin(p.Age * FeatherSwayRate + p.Angle) * p.Chord * sway;
                    float dz = Mathf.Cos(p.Age * FeatherSwayRate * 0.77f + p.Angle * 1.31f) * p.Depth * sway;
                    p.T.position = new Vector3(p.End.x + dx, p.End.y + p.R1 * (1f - u), p.End.z + dz);
                    p.T.rotation = Quaternion.Euler(p.Rot0 + p.Spin * p.Age);
                    p.T.localScale = new Vector3(Mathf.Max(0.01f, p.S0),
                                                 Mathf.Max(0.004f, p.Thick),
                                                 Mathf.Max(0.01f, p.S1));
                    break;
                }
                default:   // ModeBallistic
                {
                    if (dt > 0f)
                    {
                        p.Vel.y -= p.Gravity * dt;
                        p.Pos += p.Vel * dt;
                    }
                    p.T.position = p.Pos;
                    float s = Mathf.LerpUnclamped(p.S0, p.S1, growth);
                    p.T.localScale = new Vector3(Mathf.Max(0.005f, s), Mathf.Max(0.005f, s), Mathf.Max(0.005f, s));
                    if (p.Spin.sqrMagnitude > 1e-4f)
                        p.T.rotation = Quaternion.Euler(p.Rot0 + p.Spin * p.Age);
                    break;
                }
            }

            // 二次方淡出：前段还亮着，尾巴迅速收掉（线性淡出读起来是"半亮地挂很久"）
            // ★ 第四版：**刀光（`Sweep`）单独走 EaseOut** ★
            //   需求原话是"逐片扫过的淡出曲线从线性改成 EaseOut（`1-(1-t)³`）"。
            //   把 t 取成**寿命进度取反**（t = 1 − k）之后，亮度包络 = `1 − (1−(1−k))³` = **`1 − k³`**：
            //   k=0.5 时还有 0.875（原来 `1−k²` 是 0.75）⇒ 前段几乎不掉、**末段才干净收掉** ✓
            //   ★ 为什么不用一条全局曲线 ★  `1 − k²` 同时服务冲击环 / 土粒 / 烟雾 / 火星 / 水花，
            //   动它等于顺手改了另外五种特效的外观 —— 那不在本轮"只提精度"的范围内 ✗
            // ★ 再乘 `outCol.a`：刀光靠它实现"内缘低透明（0.15）→ 外缘高透明（0.95）"的径向 alpha；
            //   其余模式给 `Col` 时 a 都是 1 ⇒ 这一步对它们是**恒等**，不会改变旧外观 ✓
            // ★ `Age < 0` = 这一片还没被扫到（只有刀光会这么用）⇒ 输出 0 透明，位置照样先摆好 ✓
            // ★ v306：`Hold`（巨型的整圈）走 `1 − k⁴` —— 前 70% 都还亮着，末段干净收掉。
            //   巨型的规格是"整圈一次性亮起 + 整体淡出"：如果它和刀光共用 `1 − k³`，
            //   0.30s 的环在 k=0.5 只剩 0.875、k=0.8 剩 0.49 ⇒ 读起来仍像"在扫过去"。
            //   `1 − k⁴` 在 k=0.8 还有 0.59 ⇒ 前段**咬住**，才读得成"这一圈范围已经定了"✓
            float fadeBase = p.Hold ? (1f - k * k * k * k)
                           : p.Sweep ? (1f - k * k * k)
                           : (1f - k * k);
            float fade = p.Age < 0f ? 0f : Mathf.Clamp01(p.A0 * fadeBase * outCol.a);
            ApplyColor(p.Mr, new Color(outCol.r, outCol.g, outCol.b, fade));
        }

        private static void ApplyColor(MeshRenderer mr, Color c)
        {
            mr.GetPropertyBlock(_mpb);
            if (_hasColor) _mpb.SetColor(_colId, c);
            if (_hasBaseColor) _mpb.SetColor(_baseColId, c);
            mr.SetPropertyBlock(_mpb);
        }

        // ══════════════════════════════════════════════════════════════
        // 读数（桥 / 探针用：`unity_drive send AttackVfx AttackVfxHost Report`）
        // ══════════════════════════════════════════════════════════════

        /// <summary>一行自述：池规模 / 当前活着几个 / 三路各触发了几次 / 用了哪个 shader。</summary>
        public static string Probe()
        {
            string s = "ATTACKVFX"
                     + "｜池 4×" + PoolPerKind + "+" + PeelPool + "=" + MaxRenderers
                     + "（同屏上限 320）"
                     + "｜当前可见 " + LiveRenderers
                     + "｜刀光 " + Swings + " 次 · 水柱 " + Beams + " 条（水花 " + SplashBursts + " 簇/像素 "
                     + PixelsSpawned + " 个）"
                     + " · 冲击环 " + Blasts + " 次（烟雾 " + SmokeSpawned + " 球）"
                     + "｜火星 " + SparksSpawned + " 颗 · 土粒 " + DirtSpawned + " 颗"
                     + " · 浪墙 " + WavesSpawned + " 道 · 香蕉皮 " + PeelsSpawned + " 个"
                     + " · 潮汐弧(★v312) " + TideArcs + " 次"
                     + " · 黑洞 " + BlackHoles + " 次(1s漩涡)"
                     + " · 护壤 " + WardShields + " 次(六边形罩)"
                     + "｜★v314 复活羽毛 " + AngelFeathers + " 片(" + AngelFeatherCount + "片/次, 墙钟推进)"
                     + "｜★v310 巨型刃痕 " + GiantBlades + " 片(" + GiantArcCount + "段×" + GiantSegPerArc
                     + "片, 段间空 " + GiantArcGapDeg + "°, α峰值 " + GiantAlphaPeak.ToString("0.00") + ")"
                     + " · 灼烧余烬 " + BurnEmbers + " 颗 · 感电电弧 " + ShockBolts + " 段"
                     + "｜★v309 水圈 " + WaterRings + " · 白闪 " + BladeFlashes + " 段"
                     + " · 二次环 " + EchoRings + " 段 · 灼印脉冲 " + MarkPulses
                     + " · 血尾 " + BloodTrailDots + " 点 · 雷光 " + MinePulses
                     + "｜网格 4 份(Cube/Sphere/Peel/Disc) + 护壤六边形 1 份 · 材质 " + (1 + BladeTexSections) + " 份(1 共享 + "
                     + BladeTexSections + " 刀光 UV 段, shader=" + ShaderName + ")"
                     + "｜贴图 " + (_bladeTex != null ? 1 : 0)
                     + " 张(代码生成 " + BladeTexW + "×" + BladeTexH + "·3 层纹理·" + BladeTexSections + " 段 UV)"
                     + "｜ready=" + _ready;
            Log.Info(s);
            return s;
        }
    }

    /// <summary>
    /// 攻击特效宿主：**进 play 模式就自己起一个**（`RuntimeInitializeOnLoadMethod`），
    /// 不依赖场景里有没有人挂它 —— 与 `CombatFeedbackFxHost`（`Flow/CombatFeedback.Post.cs:243`）
    /// 完全同一条做法。本宿主只做三件事：订阅三个既有攻击事件 → 转发给 `AttackVfx` → 每帧推进粒子 ✓
    /// </summary>
    [DefaultExecutionOrder(31000)]
    public sealed class AttackVfxHost : MonoBehaviour
    {
        private Action<MeleeSwingEvent> _onSwing;
        private Action<BeamEvent> _onBeam;
        private Action<FertilizerBlastEvent> _onBlast;
        private bool _subscribed;

        /// <summary>进 play 模式后（首场景加载完）自动建宿主，跨场景活一整个会话。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            AttackVfx.ResetForNewSession();      // 关掉 Domain Reload 时清掉上一轮的静态状态
            var go = new GameObject("AttackVfx");
            DontDestroyOnLoad(go);
            go.AddComponent<AttackVfxHost>();
        }

        private void Awake()
        {
            AttackVfx.EnsureBuilt(transform);
            Subscribe();
        }

        /// <summary>只订阅**已经存在、且确实会被发布**的三个事件（发布点见 `AttackVfx` 文件头）。</summary>
        private void Subscribe()
        {
            if (_subscribed) return;
            _onSwing = OnSwing;
            _onBeam = OnBeam;
            _onBlast = OnBlast;
            Bus.Subscribe(_onSwing);
            Bus.Subscribe(_onBeam);
            Bus.Subscribe(_onBlast);
            _subscribed = true;
        }

        private void OnDestroy()
        {
            if (!_subscribed) return;
            Bus.Unsubscribe(_onSwing);
            Bus.Unsubscribe(_onBeam);
            Bus.Unsubscribe(_onBlast);
            _subscribed = false;
        }

        private static void OnSwing(MeleeSwingEvent e) => AttackVfx.SpawnSwing(in e);
        private static void OnBeam(BeamEvent e) => AttackVfx.SpawnBeam(in e);
        private static void OnBlast(FertilizerBlastEvent e) => AttackVfx.SpawnBlast(in e);

        /// <summary>每帧推进粒子。用 `Time.deltaTime`（不是 unscaled）⇒ 顿帧时特效跟着慢下来；
        /// ★ v279：寿命回收是**墙钟**的（`MaxRealtimeLife`）⇒ 再慢也不会停在屏幕上 ✓</summary>
        private void Update()
        {
            if (!Application.isPlaying) return;
            AttackVfx.Tick(Time.deltaTime);
        }

        /// <summary>桥/探针入口：`unity_drive send AttackVfx AttackVfxHost Report`。</summary>
        public string Report() => AttackVfx.Probe();
    }
}
