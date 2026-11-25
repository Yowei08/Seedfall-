using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 玩家：土豆兄弟式 **Q 版豆豆人（农夫）** ★ —— 程序化几何 + 程序化姿态。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是程序化（而不是再做一套 FBX + 片段）
    /// ══════════════════════════════════════════════════════════════════════
    /// 改前玩家是 `Resources/Art/Characters/player.fbx`（19 根骨骼、刚性父子绑定、非蒙皮），
    /// 姿态由 `ModelView` 用 `AnimationClip.SampleAnimation` **手驱** 7 段合并片段
    ///（`Resources/Art/AnimMerged/player_*.asset`，来自 Blender 导出的 `Art/Anim/player_*.fbx`）。
    ///
    /// 那条路**能跑**，但对本任务的三条要求都够不着：
    ///   ① 形状 —— 现有骨架是按"人形农夫"绑的，头身比接近 1:1.6，改成 1:1 要重新绑骨骼+蒙皮；
    ///   ② 姿态 —— 那 7 段片段每段只有 **6 个关键帧**，曲线是三次 Hermite；
    ///      用户报的"抽搐"就是它。前几轮已经证明**重采样对它是恒等变换**（同值同斜率 ⇒ 同一条曲线）
    ///      ⇒ 靠改采样率根治是不可能的，只能换姿态来源；
    ///   ③ 三把武器 —— 现有片段只驱动了 12 根上半身骨骼，挥击轨迹是三把**各自不同**的片段，
    ///      而"锄头顶端扫一道弧"需要**按武器算出来的几何**，片段里没有这个东西。
    ///
    /// ⇒ 所以走 `ProceduralBugs` 那一条：**几何在代码里按参数生成、姿态按时间解析求值**。
    ///   好处是确定性（同输入同输出，可离屏重放）、可调（头身比是一个常数）、
    ///   零片段（没有 Hermite 抽搐这一说）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、共享资源契约（**不许每实例 new Mesh / new Material**）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 网格：**全部来自 Unity 内建图元**（球/胶囊/圆柱/立方），在 `PrimMesh` 里取一次
    ///     `sharedMesh` 后存静态表 ⇒ 场上第 1 个和第 N 个玩家共用同一批 Mesh 对象。
    ///     自建网格只有两个（帽子锥、挥击弧带），同样按"形状键"缓存在 `MeshCache` 里，
    ///     **只建一次**；没有 `new Mesh()` 落在任何 per-instance 路径上 ✓
    ///   · 材质：一律走 `ToonLook.Get(color, width)` —— 那是 `Cache` 里的**共享**材质，
    ///     键是（RGB, 描边粗细）✓ 不 `new Material`
    ///   · 染色：不在这里做。受击闪白/无敌高亮仍由 `ModelView.SetTint` 用
    ///     `MaterialPropertyBlock` 乘法写 `_TintMul`（逐渲染器，不污染共享材质）✓
    ///     ⇒ 本类**完全不碰**任何颜色属性，只写 Transform。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、朝向可读性（俯视/斜俯一眼看出朝哪）
    /// ══════════════════════════════════════════════════════════════════════
    /// 模型正面 = 本地 **+Z**（与 `ModelView.SetFacing` 的约定一致：`Euler(0,yaw,0) * BaseRotation`）。
    /// 四个**只在前方**存在的特征，所以从斜上方看也不会看反：
    ///   ① 两只眼睛（深色，贴在头部 +Z 面）  ② 围裙胸兜（前胸一块平板）
    ///   ③ 两条背带（只在前胸）              ④ 脚尖朝 +Z 的两只靴子
    /// 另有**草帽宽檐**把剪影撑成"上大下小"的蘑菇形 —— 远看就知道是个戴帽子的东西，
    /// 而旧版靠 `AimTip` 那个小球当箭头（球是各向同性的，读不出方向）✗
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、状态与时间
    /// ══════════════════════════════════════════════════════════════════════
    /// 状态枚举沿用 `ModelView.State`（Idle/Walk/Attack/Hit/Death/Swap），
    /// 因为 `WorldView.SyncPlayer` 的"让位守卫"就是按它判的 —— 换一套枚举等于把那段重写 ✗
    /// 五个必需状态（走路/待机/挥击/受击/闪避）里，**闪避不占枚举位**：
    /// 它是叠加层（`EvadeK`），因为闪避期间玩家仍然可以走/可以被打断，
    /// 真做成一个状态就会与"走"互斥 ⇒ 闪避时脚步停住，反而更怪 ✗
    /// </summary>
    public sealed class PlayerBean
    {
        // ══════════════════════════════════════════════════════════════════
        // ★ 尺寸表（米）★ —— 总高 ≈ 1.20m，头 : 身 ≈ 1 : 1.1
        //
        // 自上而下：草帽尖 1.20 ｜ 头顶 1.096 ｜ 头底 0.655 ｜ 身顶 0.64 ｜ 身底 0.24 ｜ 脚底 0
        // 头高 0.441 ／ 身高 0.399 ⇒ 1 : 1.10 ✓（土豆兄弟那种"头是半个身子"的比例）
        // 最宽处在**帽檐** 0.68m ⇒ 俯视剪影是"一个帽子 + 两只脚"，侧向轮廓是"蛋"✓
        //
        // ⚠️ 第一版把腿的枢轴写成了 `torso` 局部 `-HipY` ✗ —— `torso` 本身就在髋高度上，
        //    再减一次 0.25 就把整条腿与靴子做到**地面以下**（实测：抓图上膝盖处是断的，
        //    两条腿整个消失，只剩靴尖露在地面线上）✓ 枢轴应当是 `0`（就在髋上）✓
        // ══════════════════════════════════════════════════════════════════
        public const float TotalHeight = 1.20f;

        private const float HipY = 0.250f;          // 髋关节高度（腿长）
        private const float LegR = 0.052f;
        private const float LegH = 0.200f;
        private const float LegX = 0.085f;

        private const float BodyR = 0.210f;         // 身体（蛋形，横比纵略宽）
        private const float BodyCY = 0.190f;        // 身体中心相对髋
        private const float HeadR = 0.225f;         // 头（比身体略大）
        private const float HeadCY = 0.215f;        // 头中心相对颈
        private const float NeckY = 0.410f;         // 颈相对髋

        private const float ShoulderX = 0.228f;     // 比身体半宽(0.21)再外一点 ⇒ 手臂不被身体吞掉
        private const float ShoulderY = 0.345f;     // 肩相对髋
        private const float ArmR = 0.045f;
        private const float ArmH = 0.190f;
        private const float HandDrop = 0.190f;      // 手掌相对肩

        private const float HatBrimR = 0.340f;      // 帽檐半径（0.68 直径）
        private const float HatBrimT = 0.022f;
        private const float HatBrimY = 0.175f;      // 相对头心：落在头**收窄**的那一段上（见 hat 段注释）
        private const float HatCrownR = 0.175f;
        private const float HatCrownH = 0.145f;
        private const float HatBandY = 0.190f;

        // ── 配色（农夫：草帽草黄 + 亚麻衫 + 背带蓝布 + 皮肤暖棕 + 木屐深褐）──
        private static readonly Color SkinCol = new Color(0.93f, 0.72f, 0.50f);
        private static readonly Color LinenCol = new Color(0.88f, 0.85f, 0.74f);   // 亚麻衫（身体）
        private static readonly Color ClothCol = new Color(0.33f, 0.47f, 0.62f);   // 背带裤（围裙/背带/裤腿）
        private static readonly Color StrawCol = new Color(0.87f, 0.74f, 0.40f);   // 草帽
        private static readonly Color StrawDark = new Color(0.72f, 0.57f, 0.28f);  // 帽带
        private static readonly Color BootCol = new Color(0.30f, 0.21f, 0.16f);    // 靴
        private static readonly Color EyeCol = new Color(0.09f, 0.08f, 0.07f);
        private static readonly Color GlintCol = new Color(0.98f, 0.98f, 0.96f);
        private static readonly Color ArcCol = new Color(0.99f, 0.94f, 0.70f);     // 挥击弧
        private static readonly Color JetCol = new Color(0.62f, 0.87f, 1.00f);     // 水柱

        /// <summary>描边粗细（米）。**整体一个值**，不按部件自适应 ——
        /// 内建 `ToonLook.Apply` 是按"该渲染器最大边的 1.6%"算的，
        /// 套到 4cm 的眼珠上会算出 0.4mm 的下限 ⇒ 眼珠被黑线糊死 ✗</summary>
        private const float Outline = 0.011f;
        private const float OutlineTiny = 0.004f;

        /// <summary>
        /// 挥击弧带的半径（米）。弧带的网格是**单位半径**（1.0），靠这个常数缩到实战尺寸。
        ///
        /// ⚠️ 第一版漏了这一步：网格半径 1.0 直接挂上去 ⇒ 一道 **2 米宽**的月牙罩住整个角色
        ///    （抓图上是一根黄色大叶子插在头上）✗
        /// 取值依据：手在肩下 0.19m、锄头再往前 0.62m ⇒ 刃尖扫过的半径 ≈ 0.6m；
        /// 弧带取 0.46m 表示"扫过的那一道"，比刃尖略小 ⇒ 看起来是被挥出来的，而不是背景板 ✓
        /// </summary>
        private const float ArcRadius = 0.62f;

        // ══════════════════════════════════════════════════════════════════
        // 共享资源缓存（**静态**：全工程只有一份）
        // ══════════════════════════════════════════════════════════════════
        private static readonly Dictionary<PrimitiveType, Mesh> PrimCache = new Dictionary<PrimitiveType, Mesh>();
        private static readonly Dictionary<string, Mesh> MeshCache = new Dictionary<string, Mesh>();
        private static readonly Dictionary<string, GameObject> ToolCache = new Dictionary<string, GameObject>();

        // ── 节点 ──
        public GameObject Go { get; private set; }
        private Transform _hip, _torso, _neck;
        private Transform _legL, _legR, _footL, _footR;
        private Transform _armL, _armR, _handL, _handR;
        private Transform _head;
        /// <summary>武器挂点（★ 名字保持 `Bone_Hand_R` ★：`WorldView` 与旧探针都按名字找它）</summary>
        public Transform HandMount { get; private set; }

        /// <summary>挥击弧带（锄/肥用）。挂在**肩**上而不是手上 —— 手在扫、弧得留在空中 ✓</summary>
        private GameObject _arc;
        /// <summary>水柱（壶用）。挂在手上，朝掌心外侧（本地 +Z）✓</summary>
        private GameObject _jet;
        /// <summary>
        /// ★ v203/v204：脚下的**朝向指示** —— 一圈贴地的**圆环贴图** ＋ 环上一个**圆形方向点**。
        /// 形态/贴图/尺寸/抬高/调色板的真值在 `WorldView.BuildFacingIndicator`
        /// （网格 `_fxMeshCache[5920]`、两张 `Texture2D`、两份材质都是**静态一份** ⇒ 没有任何
        /// per-instance 新建）★
        /// 挂在根节点下 ⇒ 朝向 = 模型最终朝向，零同步代码 ✓
        /// </summary>
        private GameObject _facingRing;

        /// <summary>朝向指示的物件（只读；验收探针用它量"指示 vs 模型"的夹角）。</summary>
        public GameObject FacingRing => _facingRing;

        // ── 时间与姿态状态 ──
        private float _walkPhase;      // 只增不减（同 `ModelView._locoTime` 的原则：任何状态都不重置）
        private float _breathPhase;
        private float _attackSeconds = 0.70f;
        private int _weapon;
        private float _evadeK;         // 闪避叠加层权重 0..1

        /// <summary>挥击姿态的第 u 帧（验收冻结用，-1 = 不冻结）。</summary>
        public float DebugFreezeU = -1f;

        public float AttackSeconds => _attackSeconds;

        /// <summary>
        /// ★ v204：**攻击姿态的最短时长（秒）** —— 与 `WorldView.MinAttackAnimSeconds` 同一个数、
        /// 同一条理由（攻速极高时不能一帧播完）。两个类各存一份常量会漂移，
        /// 所以这里**引用** `WorldView` 的那个常量（同一个程序集、`internal` 可见）✓
        /// ★ 下限落在两处，是因为两处各自被不同路径读到：
        ///   · `WorldView.AttackSeconds` 决定**交给**模型的秒数（含攻速换算）
        ///   · 本类的 `ApplyAttack` 决定**姿态自己**按多长求值（`u = t / len`）
        ///   两处不同值会让"姿态在 0.06s 处演完、而状态层还以为在 0.14s 内"✗
        /// </summary>
        private const float MinAttackSeconds = WorldView.MinAttackAnimSeconds;

        /// <summary>数据缺失时的兜底时长（= 锄头 `tools.hoe.cooldown`）—— "不知道多快"就别乱变 ✓</summary>
        private const float DefaultAttackSeconds = 0.70f;

        /// <summary>
        /// ★ v204：设置攻击姿态的总时长（秒）★
        /// 下限从 0.12 放宽到 **0.06**（`MinAttackSeconds`）：0.12 是旧版"片段驱动"时代的
        /// 经验值，它会让"攻速 ≥ 5.9 倍"的出手**无法反映到动画上**（动画比出手慢 ⇒ 越打越卡）✗
        /// `seconds <= 0`（数据缺 / 表没加载）走兜底，而不是被夹成下限 —— 那会把"没数据"伪装成"极快" ✗
        /// </summary>
        public void SetAttackSeconds(float seconds)
            => _attackSeconds = seconds <= 0.001f
                ? DefaultAttackSeconds
                : Mathf.Clamp(seconds, MinAttackSeconds, 3f);
        public void SetWeapon(int weaponIndex) => _weapon = Mathf.Clamp(weaponIndex, 0, 2);

        /// <summary>闪避叠加层：`remaining` 是 `PlayerActor.EvadeGhost` 的剩余秒数。</summary>
        public void SetEvade(float remaining)
        {
            // 0.40s 总时长（`PlayerActor.EvadeGhostSeconds`）；前 25% 起势、后 45% 落回
            const float Total = 0.40f;
            float t = 1f - Mathf.Clamp01(remaining / Total);      // 0 = 刚触发，1 = 结束
            float up = Mathf.Clamp01(t / 0.25f);
            float down = 1f - Mathf.Clamp01((t - 0.55f) / 0.45f);
            _evadeK = Mathf.SmoothStep(0f, 1f, up) * down;
        }

        // ══════════════════════════════════════════════════════════════════
        // 构建
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 在 `parent` 下建一个豆豆人。**几何一次建好、之后只写 Transform**。
        /// 返回的对象由调用方（`ModelView.SpawnPlayer`）持有并驱动。
        /// </summary>
        public static PlayerBean Build(Transform parent)
        {
            var b = new PlayerBean();
            GameObject root = new GameObject("ANIM_farmer");
            root.transform.SetParent(parent, false);
            b.Go = root;
            Transform R = root.transform;

            // ── 髋：所有摆动都在它下面，它是"整体起伏/倾斜"的唯一入口 ──
            b._hip = Node(R, "HIP", 0f, HipY, 0f);
            b._torso = Node(b._hip, "TORSO", 0f, 0f, 0f);

            // ── 腿 + 靴 ──
            // 枢轴就在髋高度上（`torso` 已在髋），腿从这里往下长 0.20m 到地面 ✓
            b._legL = Node(b._torso, "LEG_L", -LegX, 0f, 0f);
            b._legR = Node(b._torso, "LEG_R", +LegX, 0f, 0f);
            Part(b._legL, "Leg_L", PrimMesh(PrimitiveType.Capsule), ClothCol, Outline,
                new Vector3(0f, -LegH * 0.5f, 0f), new Vector3(2f * LegR, LegH * 0.5f, 2f * LegR));
            Part(b._legR, "Leg_R", PrimMesh(PrimitiveType.Capsule), ClothCol, Outline,
                new Vector3(0f, -LegH * 0.5f, 0f), new Vector3(2f * LegR, LegH * 0.5f, 2f * LegR));
            // 靴子：脚尖沿 +Z 拉长 —— 俯视时它就是"朝哪边"的第一线索
            // 底面要**正好贴 y=0**：枢轴在 0.05m，半高 0.043 ⇒ 中心再下 0.007 ✓
            b._footL = Node(b._legL, "FOOT_L", 0f, -LegH, 0f);
            b._footR = Node(b._legR, "FOOT_R", 0f, -LegH, 0f);
            foreach (Transform f in new[] { b._footL, b._footR })
            {
                Part(f, "Boot", PrimMesh(PrimitiveType.Sphere), BootCol, Outline,
                    new Vector3(0f, -0.007f, 0.030f), new Vector3(0.150f, 0.086f, 0.205f));
            }

            // ── 身体（蛋形）= **背带裤**本体 ──
            //
            // ⚠️ 第一版把身体做成亚麻色、胸兜做成蓝色平板 ✗ 实测那两块蓝板被蛋形身体吞掉了
            //    （抓图上胸前什么都没有）⇒ 换成"**身体本身就是背带裤**"：
            //    蓝色从任何角度都读得出，背带与胸兜只做**加项**，不再靠"贴片露出来"✓
            Part(b._torso, "Body", PrimMesh(PrimitiveType.Sphere), ClothCol, Outline,
                new Vector3(0f, BodyCY, 0f), new Vector3(BodyR * 2f, BodyR * 1.90f, BodyR * 1.76f));
            // 胸兜：比身体略浅的一块，只在前胸（+Z）。用**压扁的球**而不是方块 ——
            // 方块的四条棱会在蛋面上割出硬边，压扁的球贴着蛋面走 ✓
            Part(b._torso, "Bib", PrimMesh(PrimitiveType.Sphere), LinenCol, Outline,
                new Vector3(0f, BodyCY + 0.020f, BodyR * 0.62f),
                new Vector3(0.235f, 0.190f, 0.135f));
            // 两条背带：草绳色（与蓝布形成对比），只在前胸 —— 最强的"前后"线索 ✓
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject strap = Part(b._torso, "Strap", PrimMesh(PrimitiveType.Capsule), StrawDark, Outline,
                    new Vector3(s * 0.090f, BodyCY + 0.140f, BodyR * 0.66f),
                    new Vector3(0.052f, 0.145f, 0.052f));
                strap.transform.localRotation = Quaternion.Euler(-12f, s * 12f, s * 9f);
                // 扣子：背带下端那颗小方扣（3cm，够小但一眼能认）
                Part(b._torso, "Buckle", PrimMesh(PrimitiveType.Cube), StrawCol, OutlineTiny,
                    new Vector3(s * 0.072f, BodyCY + 0.055f, BodyR * 0.80f),
                    new Vector3(0.050f, 0.042f, 0.030f));
            }

            // ── 手臂：肩为枢轴，手在末端；**右手（+X）带武器挂点** ──
            // ★ 坐标系：模型正面 = 本地 +Z、上 = +Y ⇒ `transform.right` = +X = 角色的右手 ✓
            //   （第一版把 L/R 放反了：+X 写成了 SHOULDER_L —— 挂点因此长在左手上 ✗ 已纠正）
            b._armL = Node(b._torso, "SHOULDER_L", -ShoulderX, ShoulderY, 0f);
            b._armR = Node(b._torso, "SHOULDER_R", +ShoulderX, ShoulderY, 0f);
            b._armL.localRotation = Quaternion.Euler(0f, 0f, -10f);
            b._armR.localRotation = Quaternion.Euler(0f, 0f, 10f);
            Part(b._armL, "Arm_L", PrimMesh(PrimitiveType.Capsule), SkinCol, Outline,
                new Vector3(0f, -ArmH * 0.45f, 0f), new Vector3(2f * ArmR, ArmH * 0.55f, 2f * ArmR));
            Part(b._armR, "Arm_R", PrimMesh(PrimitiveType.Capsule), SkinCol, Outline,
                new Vector3(0f, -ArmH * 0.45f, 0f), new Vector3(2f * ArmR, ArmH * 0.55f, 2f * ArmR));
            b._handL = Node(b._armL, "Hand_L", 0f, -HandDrop, 0f);
            b._handR = Node(b._armR, "Hand_R", 0f, -HandDrop, 0f);
            Part(b._handL, "Hand_L", PrimMesh(PrimitiveType.Sphere), SkinCol, Outline,
                Vector3.zero, Vector3.one * 0.125f);
            Part(b._handR, "Hand_R", PrimMesh(PrimitiveType.Sphere), SkinCol, Outline,
                Vector3.zero, Vector3.one * 0.125f);
            // ★ 挂点：名字与旧骨架**同名** ⇒ `WorldView.AttachTools` 按名字找得到 ✓
            b.HandMount = Node(b._handR, "Bone_Hand_R", 0f, 0f, 0f);

            // ── 颈 + 头（大圆头 + 草帽 + 眼睛）──
            b._neck = Node(b._torso, "NECK", 0f, NeckY, 0f);
            b._head = Node(b._neck, "HEAD", 0f, HeadCY, 0f);
            Part(b._head, "Head", PrimMesh(PrimitiveType.Sphere), SkinCol, Outline,
                Vector3.zero, new Vector3(HeadR * 2f * 1.03f, HeadR * 2f * 0.98f, HeadR * 2f * 0.96f));
            // 眼睛：压扁的球**压在头的表面上**——头在 z 上的半径是 0.216，
            // 第一版把眼心放在 0.18、半深 0.031 ⇒ 最前面只到 0.211 ⇒ **整个埋进头里**，
            // 抓图上是一张没有五官的脸 ✗ 现在眼心 0.190、半深 0.040 ⇒ 露出约 1.4cm ✓
            for (int s = -1; s <= 1; s += 2)
            {
                Part(b._head, "Eye", PrimMesh(PrimitiveType.Sphere), EyeCol, OutlineTiny,
                    new Vector3(s * 0.092f, 0.040f, HeadR * 0.845f),
                    new Vector3(0.098f, 0.120f, 0.080f));
                Part(b._head, "Glint", PrimMesh(PrimitiveType.Sphere), GlintCol, OutlineTiny,
                    new Vector3(s * 0.104f, 0.074f, HeadR * 0.95f), Vector3.one * 0.034f);
            }
            // 草帽：宽檐（俯视的主剪影）+ 锥形帽冠 + 帽带
            // ★ 帽檐的高度是**算出来的**，不是随手放的 ★
            //   头在 y=0.175 处的水平半径 = 0.213 × √(1−(0.175/0.2205)²) ≈ 0.127m
            //   ⇒ 帽冠底半径 0.175 **盖得住**这一段（头顶不会从帽子里戳出来）✓
            //   第一版把檐放在 0.1395：那里头的半径还有 0.16m，于是头顶直接从檐面上穿出来，
            //   看起来像"戴了个铁圈"✗
            GameObject brim = Part(b._head, "HatBrim", PrimMesh(PrimitiveType.Cylinder), StrawCol, Outline,
                new Vector3(0f, HatBrimY, 0.012f),
                new Vector3(HatBrimR * 2f, HatBrimT * 0.5f, HatBrimR * 2f));
            brim.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);   // 略前压：朝向线索 + 不呆板
            Part(b._head, "HatCrown", ConeMesh(), StrawCol, Outline,
                new Vector3(0f, HatBrimY + 0.004f, 0f), new Vector3(HatCrownR, HatCrownH, HatCrownR));
            Part(b._head, "HatBand", PrimMesh(PrimitiveType.Cylinder), StrawDark, Outline,
                new Vector3(0f, HatBandY, 0f), new Vector3(HatCrownR * 2.08f, 0.012f, HatCrownR * 2.08f));

            // ── 挥击道具（默认隐藏；由姿态驱动显隐与缩放）──
            // ★ 弧带**平铺**在脚前的地面上（见 `ArcMesh` 的推导）★
            //   位置取"身体中心前方 0.10m、离地 0.10m"—— 太高会切进身体，太低会被地面吃掉 ✓
            //   挂在 `_torso` 上（不挂手臂）⇒ 手臂从扇带里扫过去，扇带留在原地 ✓
            b._arc = Part(b._torso, "SwingArc", ArcMesh(), ArcCol, OutlineTiny,
                new Vector3(0f, -0.15f, 0.10f), Vector3.one * ArcRadius);
            b._arc.SetActive(false);
            b._jet = Part(b._handR, "Jet", PrimMesh(PrimitiveType.Cylinder), JetCol, OutlineTiny,
                new Vector3(0f, 0f, 0.30f), new Vector3(0.055f, 0.30f, 0.055f));
            b._jet.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // 圆柱的轴 Y → 指向 +Z
            b._jet.SetActive(false);

            // ── v203/v204 脚下的朝向指示（`FacingRing`）── **v220 按用户要求整件删除** ──
            // 用户 v220 原话："玩家脚下那片异常贴图删除 —— 这个好像是登入界面为了效果添加的"，
            // 描述是"玩家脚下一片**米色多边形薄片**（半透）"。
            //
            // ── 它到底是什么（v220 实测取证，`TownArtDresser.FeetReport` 的输出）──
            //   名字 `FacingRing`，世界坐标 (0.000, 0.020, -18.600)，
            //   父级链 `TOWN/TownPlayer/FacingRing`，两个子物体：
            //     · `FacingRingDisc` —— 一张四边形（= 多边形薄片），底色 `FacingPixelBody`
            //       原值 (0.949,0.937,0.902) 就是**米色**；贴的是环状 alpha 图
            //     · `FacingDot`      —— 一个圆形方向点（`Sprites/Default`、队列 3001）
            //   它由本文件第 338 行调 `WorldView.BuildFacingIndicator` 建出来，
            //   跟着玩家走遍 **登录界面 → 主城**（所以用户记成"登入界面为了效果添加的"）✓
            //
            // ── 为什么要"删"而不是"修好看" ──
            // 用户在 v203/v204 要过它，v220 明确要它消失；本轮按最新需求执行。
            // 更要命的是它**已经被别处改坏**：`TownArtDresser.RetextureTownSurface` 的
            // "又大又扁又贴地 ⇒ 地面类"那条归纳把这张四边形也算了进去，
            // 把它的环状贴图换成了**不透明石地材质** ⇒ 屏幕上真的就是"一片米色多边形薄片" ✗
            //（v220 同时给那条归纳加了"角色部件不参与"的护栏，双保险）
            //
            // 删除方式：**不建**。`_facingRing` 保持 null ⇒ `ApplyFacingRing` 第一行就返回，
            // `FacingRing` 属性读出 null。`WorldView.BuildFacingIndicator` 本体保留不动
            //（`ProbeV203Facing` 等验收探针都按名字/按 null 判，不会编译不过）✓
            b._facingRing = null;

            b.ApplyIdle(0f);
            return b;
        }

        // ══════════════════════════════════════════════════════════════════
        // 姿态：按状态解析求值（**没有片段、没有关键帧**）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 推进一帧姿态。
        /// `t` = 该状态已播秒数；`walkRate` = 走路播放倍率（跟移速）；
        /// `dt` 只用于相位累加（相位**不随状态重置**，见 `_walkPhase`）。
        /// </summary>
        public void Pose(ModelView.State st, float t, float walkRate, float dt)
        {
            // ★ 走路相位：**只增不减**，且按"一个步态周期 = 0.52s"的物理时长推进 ★
            // 为什么不用 `_stateTime` 当相位：它每次换状态就归零 ⇒ 出手一结束腿就跳回第 0 帧
            //（这正是 `ModelView` v182 用 `_locoTime` 修掉的"抽搐"，同一个病根）✓
            _walkPhase = Mathf.Repeat(_walkPhase + dt * Mathf.Max(0.15f, walkRate) / WalkCycle, 1f);
            _breathPhase = Mathf.Repeat(_breathPhase + dt / 2.60f, 1f);

            switch (st)
            {
                case ModelView.State.Walk: ApplyWalk(_walkPhase); break;
                case ModelView.State.Attack: ApplyAttack(t); break;
                case ModelView.State.Hit: ApplyHit(t); break;
                case ModelView.State.Death: ApplyDeath(t); break;
                case ModelView.State.Swap: ApplySwap(t); break;
                default: ApplyIdle(_breathPhase); break;
            }
            ApplyEvade();
            ApplyFacingRing(st);      // v203：脚下的朝向指示（显隐 + 闪避放大）
        }

        /// <summary>一个完整步态周期（两脚各迈一次）的秒数。走 0.52s ≈ 每秒两步，与移速倍率相乘。</summary>
        private const float WalkCycle = 0.52f;

        /// <summary>站姿：呼吸起伏 + 头微摆 + 手臂轻晃。幅度小，但**静止时也有活气**。</summary>
        private void ApplyIdle(float phase)
        {
            float s = Mathf.Sin(phase * Mathf.PI * 2f);
            float s2 = Mathf.Sin(phase * Mathf.PI * 2f + 0.9f);

            _hip.localPosition = new Vector3(0f, HipY + 0.008f * s, 0f);
            _hip.localRotation = Quaternion.Euler(0f, 0f, 0.8f * s);
            _torso.localRotation = Quaternion.Euler(1.2f + 1.0f * s, 0f, 0f);
            _neck.localRotation = Quaternion.Euler(1.5f * s, 0f, 0f);
            _head.localRotation = Quaternion.Euler(-1.5f * s2, 3.0f * s2, 1.5f * s);

            _legL.localRotation = Quaternion.Euler(-1.5f, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(1.5f, 0f, 0f);
            _footL.localRotation = Quaternion.identity;
            _footR.localRotation = Quaternion.identity;

            _armL.localRotation = Quaternion.Euler(3f * s2, 0f, -12f);
            _armR.localRotation = Quaternion.Euler(-3f * s2, 0f, 12f);

            SetFx(0f, 0f);
        }

        /// <summary>走姿：大腿交替摆 + 脚掌反相补偿 + 双臂反向摆 + 躯干起伏与侧滚。</summary>
        private void ApplyWalk(float phase)
        {
            float w = phase * Mathf.PI * 2f;
            float sw = Mathf.Sin(w);
            float step = 26f * sw;                       // 大腿摆幅（度）
            float bob = 0.022f * (0.5f - 0.5f * Mathf.Cos(w * 2f));

            _hip.localPosition = new Vector3(0f, HipY + bob, 0f);
            _hip.localRotation = Quaternion.Euler(2.0f, 0f, 3.0f * sw);
            _torso.localRotation = Quaternion.Euler(4.5f, 2.5f * sw, -1.6f * sw);
            _neck.localRotation = Quaternion.Euler(-2.0f, 0f, 0f);
            _head.localRotation = Quaternion.Euler(-3.0f, -2.0f * sw, -2.6f * sw);

            _legL.localRotation = Quaternion.Euler(step, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-step, 0f, 0f);
            // 脚掌：抵消一部分摆动（落地时贴地），再叠一点抬脚
            _footL.localRotation = Quaternion.Euler(-step * 0.72f + 6f * Mathf.Max(0f, -sw), 0f, 0f);
            _footR.localRotation = Quaternion.Euler(step * 0.72f + 6f * Mathf.Max(0f, sw), 0f, 0f);

            _armL.localRotation = Quaternion.Euler(-20f * sw, 0f, -13f);
            _armR.localRotation = Quaternion.Euler(20f * sw, 0f, 13f);

            SetFx(0f, 0f);
        }

        /// <summary>
        /// 挥击：**三把武器三段完全不同的动作**（依据各自的功能形态，不是同一段换速度）。
        ///   锄（0）：过顶下砸 —— 抬到头顶后方 → 快速下劈 → 收招；弧带在劈下的那一段出现
        ///   壶（1）：前推倾倒 —— 手臂前伸、手腕下压（壶嘴朝前）→ 水柱在出手段生长
        ///   肥（2）：双手过顶重砸 —— 抬得更高、落得更低（"重"靠幅度与蹲深表达）
        /// </summary>
        private void ApplyAttack(float t)
        {
            // ★ v204：下限 0.12 → 0.06（`MinAttackSeconds`）—— 见 `SetAttackSeconds` 的注释
            float len = Mathf.Max(MinAttackSeconds, _attackSeconds);
            float u = DebugFreezeU >= 0f ? Mathf.Clamp01(DebugFreezeU) : Mathf.Clamp01(t / len);

            // 起势 / 出手 / 收招的归一化进度
            float wind, strike, recover;
            if (_weapon == 0)      { wind = Seg(u, 0f, 0.34f); strike = Seg(u, 0.34f, 0.52f); recover = Seg(u, 0.60f, 1f); }
            else if (_weapon == 1) { wind = Seg(u, 0f, 0.30f); strike = Seg(u, 0.30f, 0.58f); recover = Seg(u, 0.66f, 1f); }
            else                   { wind = Seg(u, 0f, 0.44f); strike = Seg(u, 0.44f, 0.60f); recover = Seg(u, 0.68f, 1f); }

            float ease = Mathf.SmoothStep(0f, 1f, strike);
            float back = 1f - Mathf.SmoothStep(0f, 1f, recover);

            float armR, armL, torsoX, torsoY, hipDip, headX, arc, jet;
            if (_weapon == 0)
            {
                // 锄：抬 135° → 砸到 +58°
                armR = Mathf.Lerp(10f, -135f, wind);
                armR = Mathf.Lerp(armR, 58f, ease);
                armL = Mathf.Lerp(6f, -46f, wind);
                armL = Mathf.Lerp(armL, 26f, ease);
                torsoX = Mathf.Lerp(4f, -14f, wind);
                torsoX = Mathf.Lerp(torsoX, 22f, ease);
                torsoY = Mathf.Lerp(0f, -14f, wind);
                torsoY = Mathf.Lerp(torsoY, 12f, ease);
                hipDip = Mathf.Lerp(0f, 0.010f, wind);
                hipDip = Mathf.Lerp(hipDip, -0.030f, ease);
                headX = Mathf.Lerp(-6f, -12f, wind);
                headX = Mathf.Lerp(headX, 6f, ease);
                arc = Mathf.Clamp01(Seg(u, 0.33f, 0.40f)) * (1f - Mathf.Clamp01(Seg(u, 0.52f, 0.75f)));
                jet = 0f;
            }
            else if (_weapon == 1)
            {
                // 壶：抬 38° → 前推 62°、手腕下压 40°（壶嘴朝前倒水）
                armR = Mathf.Lerp(10f, -38f, wind);
                armR = Mathf.Lerp(armR, 62f, ease);
                armL = Mathf.Lerp(6f, -22f, wind);
                armL = Mathf.Lerp(armL, 18f, ease);
                torsoX = Mathf.Lerp(4f, -6f, wind);
                torsoX = Mathf.Lerp(torsoX, 11f, ease);
                torsoY = Mathf.Lerp(0f, -6f, wind);
                torsoY = Mathf.Lerp(torsoY, 6f, ease);
                hipDip = Mathf.Lerp(0f, 0.006f, wind);
                hipDip = Mathf.Lerp(hipDip, -0.012f, ease);
                headX = Mathf.Lerp(-4f, 4f, ease);
                armR_extra = Mathf.Lerp(0f, -34f, ease);        // 手腕下压
                arc = 0f;
                jet = Mathf.Clamp01(Seg(u, 0.32f, 0.44f)) * (1f - Mathf.Clamp01(Seg(u, 0.60f, 0.82f)));
            }
            else
            {
                // 肥：双手过顶 155° → 重砸到 +74°，蹲得更深
                armR = Mathf.Lerp(10f, -155f, wind);
                armR = Mathf.Lerp(armR, 74f, ease);
                armL = Mathf.Lerp(6f, -150f, wind);
                armL = Mathf.Lerp(armL, 62f, ease);
                torsoX = Mathf.Lerp(4f, -18f, wind);
                torsoX = Mathf.Lerp(torsoX, 26f, ease);
                torsoY = Mathf.Lerp(0f, -6f, wind);
                torsoY = Mathf.Lerp(torsoY, 6f, ease);
                hipDip = Mathf.Lerp(0f, 0.018f, wind);
                hipDip = Mathf.Lerp(hipDip, -0.052f, ease);
                headX = Mathf.Lerp(-8f, -16f, wind);
                headX = Mathf.Lerp(headX, 10f, ease);
                arc = Mathf.Clamp01(Seg(u, 0.43f, 0.50f)) * (1f - Mathf.Clamp01(Seg(u, 0.62f, 0.85f)));
                jet = 0f;
            }
            if (_weapon != 1) armR_extra = 0f;

            // 收招：把三段的终点一律拉回站姿
            armR = Mathf.Lerp(10f, armR, back);
            armL = Mathf.Lerp(6f, armL, back);
            torsoX = Mathf.Lerp(1.2f, torsoX, back);
            torsoY *= back;
            hipDip *= back;
            headX *= back;
            armR_extra *= back;

            _hip.localPosition = new Vector3(0f, HipY + hipDip, 0f);
            _hip.localRotation = Quaternion.Euler(torsoX * 0.25f, 0f, 0f);
            _torso.localRotation = Quaternion.Euler(torsoX, torsoY, 0f);
            _neck.localRotation = Quaternion.Euler(headX * 0.4f, 0f, 0f);
            _head.localRotation = Quaternion.Euler(headX, -torsoY * 0.5f, 0f);

            // 下盘：起势时后坐、出手时前压（腿不参与挥击，但有重心转移）
            float stance = 6f + 10f * ease;
            _legL.localRotation = Quaternion.Euler(-stance, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(stance * 0.6f, 0f, 0f);
            _footL.localRotation = Quaternion.Euler(stance * 0.5f, 0f, 0f);
            _footR.localRotation = Quaternion.Euler(-stance * 0.4f, 0f, 0f);

            _armR.localRotation = Quaternion.Euler(armR, 0f, 12f + armR_extra * 0.3f);
            _armL.localRotation = Quaternion.Euler(armL, 0f, -12f);
            _handR.localRotation = Quaternion.Euler(armR_extra, 0f, 0f);

            SetFx(arc, jet);
        }

        /// <summary>手腕下压角（只有水壶用；提出来是为了让"壶嘴朝前"这件事有一个数可读）。</summary>
        private float armR_extra;

        /// <summary>受击：整个人被顶得后仰 + 压扁，0.30s 内弹回。</summary>
        private void ApplyHit(float t)
        {
            const float Len = 0.30f;
            float k = Mathf.Clamp01(t / Len);
            // 快上慢下：0→0.12 冲到位，之后按 1-k 的平方回弹
            float punch = k < 0.35f ? Mathf.SmoothStep(0f, 1f, k / 0.35f) : Mathf.Pow(1f - (k - 0.35f) / 0.65f, 1.8f);

            _hip.localPosition = new Vector3(0f, HipY - 0.030f * punch, -0.020f * punch);
            _hip.localRotation = Quaternion.Euler(-8f * punch, 0f, 2f * punch);
            _torso.localRotation = Quaternion.Euler(-14f * punch, 0f, 0f);
            _neck.localRotation = Quaternion.Euler(-8f * punch, 0f, 0f);
            _head.localRotation = Quaternion.Euler(-14f * punch, 0f, 4f * punch);

            // 压扁：靠**缩放**（不改材质、不改 alpha）
            float sq = 1f - 0.10f * punch;
            _torso.localScale = new Vector3(1f + 0.08f * punch, sq, 1f + 0.08f * punch);

            _legL.localRotation = Quaternion.Euler(10f * punch, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-6f * punch, 0f, 0f);
            _footL.localRotation = Quaternion.identity;
            _footR.localRotation = Quaternion.identity;

            _armL.localRotation = Quaternion.Euler(-34f * punch, 0f, -34f * punch);
            _armR.localRotation = Quaternion.Euler(-34f * punch, 0f, 34f * punch);
            _handR.localRotation = Quaternion.identity;

            SetFx(0f, 0f);
        }

        /// <summary>换武器：一次下沉+甩手的小动作（0.50s，与 `SwitchWindup` 同量级）。</summary>
        private void ApplySwap(float t)
        {
            const float Len = 0.50f;
            float k = Mathf.Clamp01(t / Len);
            float dip = Mathf.Sin(k * Mathf.PI);          // 0→1→0
            float flick = Mathf.Sin(k * Mathf.PI * 1.6f);

            _hip.localPosition = new Vector3(0f, HipY - 0.035f * dip, 0f);
            _hip.localRotation = Quaternion.Euler(6f * dip, 0f, 0f);
            _torso.localRotation = Quaternion.Euler(9f * dip, -12f * flick, 0f);
            _neck.localRotation = Quaternion.identity;
            _head.localRotation = Quaternion.Euler(-4f * dip, 10f * flick, 0f);
            _torso.localScale = Vector3.one;

            _legL.localRotation = Quaternion.Euler(-4f * dip, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(4f * dip, 0f, 0f);
            _footL.localRotation = Quaternion.identity;
            _footR.localRotation = Quaternion.identity;

            _armR.localRotation = Quaternion.Euler(-48f * dip, 0f, 12f + 10f * dip);
            _armL.localRotation = Quaternion.Euler(-18f * dip, 0f, -12f);
            _handR.localRotation = Quaternion.Euler(0f, 0f, -40f * dip);

            SetFx(0f, 0f);
        }

        /// <summary>死亡：向后倒下（本项目会给玩家播 Death 的场合很少，但不能是个 T-pose）。</summary>
        private void ApplyDeath(float t)
        {
            float k = Mathf.Clamp01(t / 0.60f);
            float e = Mathf.SmoothStep(0f, 1f, k);
            _hip.localPosition = new Vector3(0f, HipY - 0.140f * e, -0.05f * e);
            _hip.localRotation = Quaternion.Euler(-70f * e, 0f, 0f);
            _torso.localRotation = Quaternion.Euler(-10f * e, 0f, 0f);
            _neck.localRotation = Quaternion.Euler(-6f * e, 0f, 0f);
            _head.localRotation = Quaternion.Euler(-18f * e, 0f, 0f);
            _torso.localScale = new Vector3(1f + 0.06f * e, 1f - 0.06f * e, 1f + 0.06f * e);
            _legL.localRotation = Quaternion.Euler(28f * e, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(20f * e, 0f, 0f);
            _footL.localRotation = Quaternion.identity;
            _footR.localRotation = Quaternion.identity;
            _armL.localRotation = Quaternion.Euler(-40f * e, 0f, -30f);
            _armR.localRotation = Quaternion.Euler(-40f * e, 0f, 30f);
            _handR.localRotation = Quaternion.identity;
            SetFx(0f, 0f);
        }

        /// <summary>
        /// 闪避叠加层：**加**在已求值的姿态上，不改状态机。
        /// 与 `PlayerActor.EvadeGhost`（0.40s 虚化残影，画在 HUD 上）同时发生但互不覆盖：
        /// 残影是"画面上多一个半透明的我"，这里是"我这个本体前倾一下"✓
        /// </summary>
        private void ApplyEvade()
        {
            float k = _evadeK;
            if (k <= 0.0001f) return;
            _hip.localPosition += new Vector3(0f, -0.045f * k, 0.055f * k);
            _hip.localRotation *= Quaternion.Euler(16f * k, 0f, 0f);
            _torso.localRotation *= Quaternion.Euler(10f * k, 0f, 0f);
            _legL.localRotation *= Quaternion.Euler(-10f * k, 0f, 0f);
            _legR.localRotation *= Quaternion.Euler(-10f * k, 0f, 0f);
            _armL.localRotation *= Quaternion.Euler(24f * k, 0f, 0f);
            _armR.localRotation *= Quaternion.Euler(24f * k, 0f, 0f);
        }

        /// <summary>挥击弧 / 水柱的显隐与形态。**只用缩放与位移**（不逐实例改 alpha）。</summary>
        private void SetFx(float arc, float jet)
        {
            if (_arc != null)
            {
                bool on = arc > 0.02f;
                if (_arc.activeSelf != on) _arc.SetActive(on);
                if (on)
                {
                    // 半径固定 `ArcRadius`，用 0.86~1.06 的缩放做出"扫出来 / 散掉"的呼吸感
                    float s = ArcRadius * Mathf.Lerp(0.86f, 1.06f, arc);
                    _arc.transform.localScale = new Vector3(s, s, s);
                    _arc.transform.localRotation = Quaternion.identity;
                }
            }
            if (_jet != null)
            {
                bool on = jet > 0.02f;
                if (_jet.activeSelf != on) _jet.SetActive(on);
                if (on)
                {
                    // 水柱沿 +Z 生长：长度 = 0.12 → 0.62m
                    float len = Mathf.Lerp(0.10f, 0.34f, jet);
                    _jet.transform.localPosition = new Vector3(0f, 0f, 0.06f + len);
                    _jet.transform.localScale = new Vector3(0.055f, len, 0.055f);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // ★ v203/v204：脚下的**朝向指示**（用户原话 v203："为玩家面向位置增加一个指示，放在玩家脚下"；
        //   v204："重做方向指标，用2d贴图方式展示，一个圆环包裹玩家，一个圆形代表方向"）。
        //   形态/贴图/尺寸/抬高/调色板的真值全部写在 `WorldView` 的"★ v204：改成 2D 贴图"那一段
        //   （`FacingRingTex` / `FacingDotTex` / `BuildFacingIndicator`）——
        //   这里只负责"挂上去"与"按状态反应" ★
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 建那一片朝向指示（**每个豆豆人一份物件，但网格 / 贴图 / 材质是全场共享的**）。
        ///
        /// ★ v204：**全套搬到 `WorldView.BuildFacingIndicator`** ★
        ///  世界米 → 模型米的换算（÷ `CharScale × PlayerScale = 1.6302`）只有那边的一条缩放链知道；
        ///  放在这里就意味着本类要抄一份 1.6302，改一处忘一处必然漂移 ✗
        ///  本方法现在只剩"挂到根节点"这一个动作 —— 而"挂在根节点"正是**朝向的来源**：
        ///  根节点的 rotation 就是模型的最终朝向（`ModelView.SetFacing` 写它）✓
        /// </summary>
        /// <summary>
        /// ★ v220：朝向指示已按用户要求删除 —— 本方法**不再被调用**，保留只为不再改动
        /// `WorldView.BuildFacingIndicator`（那边仍是"想做回来时"的唯一实现）★
        /// 若将来要恢复：把 `Build` 里的 `b._facingRing = null;` 换回 `b._facingRing = BuildFacingRing(R);`
        /// 并**同时**给 `TownArtDresser.RetextureTownSurface` 的角色护栏放行 ✓
        /// </summary>
        private static GameObject BuildFacingRing(Transform root)
            => WorldView.BuildFacingIndicator(root);

        /// <summary>
        /// 朝向指示的状态反应（★ 规则由本轮定，逐条写清为什么 ★）：
        ///   · **死亡 / 尸体** ⇒ **隐藏** —— 一具尸体还指着某个方向是**假信息** ✗
        ///   · **闪避** ⇒ 放大到 1.14 倍（跟 `_evadeK` 走）—— "这一下我在动"读得出来 ✓
        ///   · **受击 / 攻击 / 换武器 / 走路** ⇒ **不变**。受击的白闪已经由
        ///     `ModelView.SetTint` 表达；指示再闪一次会把"是模型被打"读成"是地面被打" ✗
        /// 只用**显隐与缩放**（共享材质的契约：永不逐实例改 alpha，见 `VfxMatCached` 的说明）✓
        /// </summary>
        private void ApplyFacingRing(ModelView.State st)
        {
            if (_facingRing == null) return;

            bool show = st != ModelView.State.Death;
            if (_facingRing.activeSelf != show) _facingRing.SetActive(show);
            if (!show) return;

            float k = 1f + 0.14f * Mathf.Clamp01(_evadeK);
            _facingRing.transform.localScale = new Vector3(k, 1f, k);
        }

        // ══════════════════════════════════════════════════════════════════
        // 武器挂载（★ 三把各一个真实挂点，切换就换模型 ★）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>三把武器的资源 id（下标 = `PlayerActor.WeaponHoe/Can/Fertilizer`）。</summary>
        public static readonly string[] ToolIds = { "tool_hoe", "tool_can", "tool_fert" };

        /// <summary>
        /// 三把武器在**掌心局部系**里的握持（局部旋转 / 缩放）。
        ///
        /// ★ 长轴是 **Y**，不是 Z（第一版判断错了）★
        /// `ProbeTools()` 量的世界包围盒：锄 `0.305×1.265×0.104`、壶 `0.326×0.291×0.183`、
        /// 肥 `0.276×0.504×0.300` —— 三把的**最长边都在 Y 上**（1.265 / 0.291 / 0.504）✗
        /// 所以"手柄沿 +Z"那条交接单描述对本工程这三份资产**不成立** ✓
        /// 实测代价：第一版按"绕 X 转 −90°"取，`R_x(−90)` 把 +Y 映到 **−Z（身后）**
        /// ⇒ 抓图上锄头是"从背上支出去的一根杆"，刃在手上、柄在身后 ✗
        /// 现在改成 **+90°**：`R_x(+90)` 把 +Y 映到 **+Z（身前）** ⇒ 柄在手里、刃朝前方 ✓
        ///
        /// ★ 三把的缩放**不同** ★ 因为三把的原始尺寸差 4 倍（锄 1.27m ／ 壶 0.29m ／ 肥 0.50m），
        /// 同一个系数会让壶小得像颗糖、锄长得像根旗杆 ✗
        /// 目标（挂上去之后的实际长度）：锄 ≈ 0.70m ／ 壶 ≈ 0.25m ／ 肥 ≈ 0.30m ✓
        /// </summary>
        /// <summary>
        /// 摆正的**基准旋转**：绕 X 转 +90°。
        ///
        /// ★ 这个值是**在画面上选出来的**，不是推出来的 ★
        /// 推导能给的只有"长轴在网格里是 Z（Z-up 建模）、导入器在根上留了 `Rx(270)`"这两条；
        /// 而"哪一头是刃、刃口朝哪"从包围盒里看不出来 ⇒
        /// 用 `PlayerShowcase.Grip("x:y:z")` 在一次会话里把 4 个候选全试了一遍
        ///（`Docs/captures/grip_*.jpg`），取 `(90,0,0)`：
        ///   · 锄：柄从掌心**竖直向下**杵到地上、刃在底端 ⇒ "拄着锄头站"的那个剪影 ✓
        ///   · `(0,0,0)` 时刃卡在掌心高度、柄斜着往下 —— 读起来像"握着柄的中段"✗
        /// </summary>
        private static readonly Vector3 ToolGripEuler = new Vector3(30f, 0f, 0f);
        // ★ v212（用户原话："玩家现在手中拿的锄头模型方向不对，让它指着正面"）：(90,0,0) → (30,0,0) ★
        //
        // 旧值 `(90,0,0)` 让柄从掌心**竖直向下**杵到地上（"拄着锄头站"的剪影）——
        //   那个剪影本身好看，但**工具的长轴指向地面**，而玩家面朝 +Z ⇒
        //   **在屏幕上看不出"这把锄头朝哪"**，正是用户说的"方向不对" ✗
        //
        // 新值 `(30,0,0)`：柄指向 **+Z 偏下 30°**（= 玩家正面偏下）⇒
        //   · 长轴投影落在**正面**，方向可读 ✓
        //   · 仍保留 30° 的下倾 ⇒ 不是"平举在掌心"那种怪姿势（旧注释担心的 `(0,0,0)` 问题）✓
        //   · 三把工具共用这个常量 ⇒ 水壶/肥料喷头也一并"朝前"，与挥击朝前一致 ✓
        //
        // ⚠️ 若实机看着仍偏，**先只调这一个数**（X 分量 0~45 之间），别去动挂点名字与偏移 ✓

        private static readonly float[] ToolScale = { 0.55f, 0.85f, 0.60f };
        private static readonly Vector3[] ToolOffset =
        {
            new Vector3(0f, 0f, 0.03f),
            new Vector3(0f, 0f, 0.03f),
            new Vector3(0f, 0f, 0.03f),
        };

        /// <summary>
        /// 绕**手柄自身轴**的滚转（度）。★ 只有锄头需要 ★
        ///
        /// 锄头的刀刃是一块**平板**，模型里它的法线沿 +Z ⇒ 直接按 `Rx(90)` 摆正之后，
        /// 那块板变成**水平**的（贴着地面躺）⇒ 从侧面看只有一条线，抓图上就是"一根棍子"✗
        /// 绕手柄轴滚 90° ⇒ 板面立起来、法线朝 ±X ⇒ 从侧面（也就是挥击平面）看得见整片刃 ✓
        ///
        /// 实现上必须**先滚再摆正**：`Rx(90) * Ry(roll)`（四元数左乘 = 后作用）
        /// —— 写成单个 `Euler(90, 90, 0)` 是不等价的：Unity 的欧拉顺序是 `Ry*Rx*Rz`，
        /// 那会把"手柄"先转到 X 轴上（实测手柄变成横向平伸）✗
        /// </summary>
        private static readonly float[] ToolRoll = { 0f, 0f, 0f };

        /// <summary>
        /// 把第 `weaponIndex` 把武器挂到右手掌心。返回挂上去的 GameObject（调用方负责显隐），
        /// 取不到资源时返回 null 并**不抛异常**（玩家不该因为缺一把武器就消失）。
        /// ★ 三把都挂上、只显示一把：切换是 `SetActive` 的开关，不是重建 ✓
        /// </summary>
        public GameObject MountTool(int weaponIndex)
        {
            if (weaponIndex < 0 || weaponIndex >= ToolIds.Length) return null;
            if (HandMount == null) return null;

            string id = ToolIds[weaponIndex];
            ModelView mv = ModelView.SpawnTool(id, HandMount);
            if (mv == null || mv.Go == null)
            {
                Debug.LogWarning($"[Seedfall] 取不到武器模型 Resources/Art/Tools/{id}.fbx —— 第 {weaponIndex} 把不会出现在手上 ✗");
                return null;
            }
            GameObject go = mv.Go;
            go.transform.localPosition = ToolOffset[weaponIndex];
            // 先绕手柄轴滚（`Ry`），再摆正到身前（`Rx`）—— 见 `ToolRoll` 的推导
            go.transform.localRotation = Quaternion.Euler(ToolGripEuler)
                                        * Quaternion.Euler(0f, ToolRoll[weaponIndex], 0f);
            go.transform.localScale = Vector3.one * ToolScale[weaponIndex];
            return go;
        }

        // ══════════════════════════════════════════════════════════════════
        // 探针（只读，供验收台与报告引用）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 姿态名（**不是一个 AnimationClip**，没有对应资产 ✗）。
        ///
        /// 它存在的理由只有一个：`ModelView.CurrentClipName` 是控制台与验收台上
        /// "现在在播哪一段"的唯一出口，直接填 `(无)` 会让"换成程序化"看起来像"没接上"✗
        /// 所以给它一个**能被复核**的名字：`farmer_attack_hoe` 表示"按锄头那一套算的姿态"✓
        /// </summary>
        public string ClipName(ModelView.State st, int weapon)
        {
            string w = weapon == 1 ? "can" : weapon == 2 ? "fert" : "hoe";
            switch (st)
            {
                case ModelView.State.Attack: return "farmer_attack_" + w;
                case ModelView.State.Walk: return "farmer_walk";
                case ModelView.State.Hit: return "farmer_hit";
                case ModelView.State.Swap: return "farmer_swap";
                case ModelView.State.Death: return "farmer_death";
                default: return "farmer_idle";
            }
        }

        /// <summary>一句话描述当前姿态（角度都是**局部欧拉角**，可直接与上位机对照）。</summary>
        public string Describe()
        {
            Vector3 a = _armR != null ? _armR.localEulerAngles : Vector3.zero;
            Vector3 l = _legL != null ? _legL.localEulerAngles : Vector3.zero;
            Vector3 h = _head != null ? _head.localEulerAngles : Vector3.zero;
            // `hip.y` 报的是**髋的局部高度**（站立 0.250m）—— 起伏/下蹲都直接读得出来。
            // 报世界 y 没有意义：摆样时根节点就在原点，恒为 0 ✗
            return $"armR={D(a.x)}° legL={D(l.x)}° head={D(h.x)}° hipY={(_hip != null ? _hip.localPosition.y : 0f):0.000}"
                 + $" weapon={_weapon} attackLen={_attackSeconds:0.00}s evade={_evadeK:0.00}"
                 + $" arc={(_arc != null && _arc.activeSelf ? "ON" : "off")} jet={(_jet != null && _jet.activeSelf ? "ON" : "off")}";
        }

        private static string D(float v) => Mathf.DeltaAngle(0f, v).ToString("0.0");

        /// <summary>顶点/三角统计（报告用：证明几何是代码算的、且没有 per-instance 新网格）。</summary>
        public static string GeometryStats()
        {
            int parts = 0, tris = 0, verts = 0;
            var sb = new System.Text.StringBuilder();
            sb.Append("BEAN-GEOMETRY 内建图元（共享）＋ 自建（缓存）\n");
            foreach (var kv in PrimCache)
            {
                if (kv.Value == null) continue;
                sb.Append($"  builtin {kv.Key,-9} v={kv.Value.vertexCount,4} t={kv.Value.triangles.Length / 3,4}\n");
            }
            foreach (var kv in MeshCache)
            {
                if (kv.Value == null) continue;
                sb.Append($"  custom  {kv.Key,-9} v={kv.Value.vertexCount,4} t={kv.Value.triangles.Length / 3,4}\n");
            }
            sb.Append($"  缓存条目：图元 {PrimCache.Count} ＋ 自建 {MeshCache.Count}（**全场共享**，不随实例增长）\n");

            // 一个实例实际用到的部件数与三角形数
            var probe = Build(null);
            if (probe != null && probe.Go != null)
            {
                MeshFilter[] mfs = probe.Go.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < mfs.Length; i++)
                {
                    Mesh m = mfs[i].sharedMesh;
                    if (m == null) continue;
                    parts++;
                    verts += m.vertexCount;
                    tris += m.triangles.Length / 3;
                }
                sb.Append($"  单实例：部件 {parts} 个 / 引用顶点 {verts} / 三角形 {tris}\n");
                Object.Destroy(probe.Go);
            }
            return sb.ToString();
        }

        /// <summary>三把武器模型的世界尺寸（米）。挂点角度是按它定的，所以它得可复核。</summary>
        public static string ProbeTools()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("TOOL-BOUNDS（改前 = 挂载到旧 FBX 骨架时量的同一口径：世界 AABB）\n");
            for (int i = 0; i < ToolIds.Length; i++)
            {
                ModelView mv = ModelView.SpawnTool(ToolIds[i], null);
                if (mv == null || mv.Go == null) { sb.Append($"  {ToolIds[i]}: 取不到 ✗\n"); continue; }
                Renderer[] rs = mv.Go.GetComponentsInChildren<Renderer>(true);
                Bounds bd = new Bounds();
                bool any = false;
                for (int k = 0; k < rs.Length; k++)
                {
                    if (rs[k] == null) continue;
                    if (!any) { bd = rs[k].bounds; any = true; } else bd.Encapsulate(rs[k].bounds);
                }
                if (any)
                    sb.Append($"  {ToolIds[i],-10} 尺寸 {bd.size.x:0.000}×{bd.size.y:0.000}×{bd.size.z:0.000}m"
                            + $"  缩放后 {bd.size.x * ToolScale[i]:0.000}×{bd.size.y * ToolScale[i]:0.000}×{bd.size.z * ToolScale[i]:0.000}m"
                            + $" （grip scale {ToolScale[i]:0.00} roll {ToolRoll[i]:0}°"
                            + $" 导入根旋转 {E(mv.BaseRotation.eulerAngles)}）\n");
                Object.Destroy(mv.Go);
            }
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        // 几何工具
        // ══════════════════════════════════════════════════════════════════

        private static Transform Node(Transform parent, string name, float x, float y, float z)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, y, z);
            return go.transform;
        }

        /// <summary>造一个带网格的部件。**网格与材质都是共享的**（见类注释第二节）。</summary>
        private static GameObject Part(Transform parent, string name, Mesh mesh, Color color, float outline,
                                       Vector3 localPos, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ToonLook.Get(color, outline);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            return go;
        }

        /// <summary>
        /// 取 Unity 内建图元的**共享网格**。
        /// 手法：建一个临时图元 → 抄走 `sharedMesh` → 立刻销毁那个 GameObject。
        /// 内建网格存在默认资源里，销毁实例不会带走它 ✓
        /// ⇒ 每个图元只查一次，之后全场复用（**没有 per-instance 的 new Mesh**）✓
        /// </summary>
        private static Mesh PrimMesh(PrimitiveType type)
        {
            if (PrimCache.TryGetValue(type, out Mesh cached) && cached != null) return cached;
            GameObject tmp = GameObject.CreatePrimitive(type);
            MeshFilter mf = tmp.GetComponent<MeshFilter>();
            Mesh m = mf != null ? mf.sharedMesh : null;
            if (Application.isPlaying) Object.Destroy(tmp);
            else Object.DestroyImmediate(tmp);
            if (m != null) PrimCache[type] = m;
            return m;
        }

        /// <summary>草帽帽冠：底半径 0.5、高 1 的闭口锥（底在 y=0，尖在 y=1），缓存一份。</summary>
        private static Mesh ConeMesh()
        {
            const string Key = "cone16";
            if (MeshCache.TryGetValue(Key, out Mesh m) && m != null) return m;

            const int Seg = 16;
            const float R = 0.5f;
            var verts = new List<Vector3>((Seg + 1) * 3 + 1);
            var tris = new List<int>(Seg * 6);

            // 侧面（每条边一个三角形；法线由 RecalculateNormals 生成）
            for (int i = 0; i < Seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / Seg, a1 = (i + 1) * Mathf.PI * 2f / Seg;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * R, 0f, Mathf.Cos(a0) * R));
                verts.Add(new Vector3(Mathf.Sin(a1) * R, 0f, Mathf.Cos(a1) * R));
                verts.Add(new Vector3(0f, 1f, 0f));
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
            }
            // 底面封口（从下往上看要能看见，法线朝下）
            int capCenter = verts.Count;
            verts.Add(Vector3.zero);
            for (int i = 0; i < Seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / Seg, a1 = (i + 1) * Mathf.PI * 2f / Seg;
                int b = verts.Count;
                verts.Add(new Vector3(Mathf.Sin(a0) * R, 0f, Mathf.Cos(a0) * R));
                verts.Add(new Vector3(Mathf.Sin(a1) * R, 0f, Mathf.Cos(a1) * R));
                tris.Add(capCenter); tris.Add(b + 0); tris.Add(b + 1);
            }

            m = new Mesh { name = "bean_cone" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            MeshCache[Key] = m;
            return m;
        }

        /// <summary>
        /// 挥击弧带：一条**水平面（X-Z）里**的扇带（内半径 0.55、外半径 1.0、张角 −58°..+58°）。
        ///
        /// ★ 为什么是水平面，而不是竖着的月牙（第一版就是竖的）★
        /// 竖着放（Y-Z 平面）在**正侧面**看得最清楚 —— 那正是我第一轮抓图的角度 ✓
        /// 而这个游戏的镜头是**俯视**的：与地面 60°（`SelfCheck` 那条"镜头与地面 60 度"钉着它）✗
        /// ⇒ 竖着的弧带在俯视下退化成**一条线**（实测 `z_attack_hoe.jpg` 里几乎看不见）✗
        /// 平铺的扇带在俯视下**整片面积**都看得见 ✓
        /// 而且它符合这件事本身：扇带 = 刃尖在地面上的扫掠投影 ✓
        /// 双面（正反各一套三角形）—— 平面片单面会从背面看不见 ✗
        /// </summary>
        private static Mesh ArcMesh()
        {
            const string Key = "arc";
            if (MeshCache.TryGetValue(Key, out Mesh m) && m != null) return m;

            const int Seg = 24;
            const float ROut = 1.0f, RIn = 0.55f;
            const float A0 = -58f, A1 = 58f;

            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= Seg; i++)
            {
                float a = Mathf.Lerp(A0, A1, i / (float)Seg) * Mathf.Deg2Rad;
                // 弧在 X-Z 平面里：z = cos（0° 指向身前 +Z），x = sin（横向）
                verts.Add(new Vector3(Mathf.Sin(a) * ROut, 0f, Mathf.Cos(a) * ROut));
                verts.Add(new Vector3(Mathf.Sin(a) * RIn, 0f, Mathf.Cos(a) * RIn));
            }
            for (int i = 0; i < Seg; i++)
            {
                int b = i * 2;
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                // 反面
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 0);
                tris.Add(b + 3); tris.Add(b + 2); tris.Add(b + 1);
            }

            m = new Mesh { name = "bean_arc" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            MeshCache[Key] = m;
            return m;
        }

        private static string F(float v) => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>欧拉角三元组（报告用）。</summary>
        private static string E(Vector3 v) =>
            v.x.ToString("0") + "," + v.y.ToString("0") + "," + v.z.ToString("0");

        /// <summary>把 `u` 从 [a,b] 映射到 [0,1] 并夹取（分段进度用）。</summary>
        private static float Seg(float u, float a, float b)
        {
            if (b <= a) return u >= b ? 1f : 0f;
            return Mathf.Clamp01((u - a) / (b - a));
        }
    }
}
