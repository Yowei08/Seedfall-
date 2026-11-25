using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 一个角色/植物的"模型化"表现体。
    ///
    /// 它替掉 WorldView 里的图元（方块/胶囊/球），负责四件事：
    ///   ① 从 Resources 加载模型并实例化（美术流水线的产物）
    ///   ② 统一换上 Seedfall/Toon 卡通描边材质
    ///   ③ 染色反馈——用 MaterialPropertyBlock 而不是 material 实例：
    ///      ToonLook 的材质是按"颜色+描边粗细"共享的，直接改 material 会把所有同类角色一起改掉；
    ///      而且 material 每次都新建实例，会持续制造 GC 垃圾。
    ///   ④ 动画——用 AnimationClip.SampleAnimation 手驱，不挂 Animator、不需要 AnimatorController。
    ///      好处是时间完全由游戏时钟给，确定性与回放都不受影响（本项目的核心约束）。
    /// </summary>
    public sealed class ModelView
    {
        // ★ v172：**只能往末尾追加，不能插在中间** ★
        //
        // `Tick` 用 `(int)_state` 去索引 `_clips`，所以枚举值与片段槽位是**位置绑定**的。
        // 往中间插一个值会静默把所有后面的状态指到错误的片段上（不报错，只是动作全错）✗
        public enum State { Idle, Walk, Attack, Hit, Death, Swap }

        private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, AnimationClip[]> ClipCache = new Dictionary<string, AnimationClip[]>();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int TintMulId = Shader.PropertyToID("_TintMul");
        private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
        private static readonly int NeonWidthId = Shader.PropertyToID("_NeonWidth");
        private static readonly int NeonPixelsId = Shader.PropertyToID("_NeonPixels");
        /// <summary>★ v209：本体边缘光（`Seedfall/Toon` 的 `[HDR] _RimColor`）。夜间发光拿它做"身体也在亮"。</summary>
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        public GameObject Go { get; private set; }

        /// <summary>
        /// 被动画采样的内层节点（木偶根）。与 Go 分开的理由见 Spawn 里的说明：
        /// 动画会写根节点的位置与旋转，不能让它作用在负责摆位的那一层上。
        /// 没有动画的模型（植物/环境）为 null，采样退回 Go。
        /// </summary>
        private GameObject _animRoot;

        /// <summary>预制体根节点的原始旋转。**不要覆盖它**——见 Spawn 里的说明。</summary>
        public Quaternion BaseRotation { get; private set; }
        /// <summary>预制体根节点的原始缩放（Unity 从 FBX 导入的是 100，用来把 1/100 的网格还原成米）。</summary>
        public Vector3 BaseScale { get; private set; }

        private Renderer[] _renderers;
        private Color[][] _baseColors;
        private MaterialPropertyBlock _mpb;
        private AnimationClip[] _clips;      // 顺序：idle, walk, attack, hit, death, swap
        private float _stateTime;
        private State _state = State.Idle;
        private bool _hasAnim;
        public float Height => RawHeight;
        public State Current => _state;

        /// <summary>当前状态的时长（秒）。0 表示没有片段。用于判断"一次性动作播完没有"。</summary>
        public float StateTime => _stateTime;

        private static readonly string[] ClipNames = { "idle", "walk", "attack", "hit", "death", "swap" };

        /// <summary>
        /// 每把武器各自的攻击片段（下标 = `PlayerActor.WeaponIndex`）。
        ///
        /// ★ 为什么需要它 ★
        /// `ClipNames` 是**按状态**索引的固定表，一个状态只能有一个片段 ✗
        /// 而玩家的三把武器是三段不同的资产（`player_attack_hoe` / `_can` / `_fert`），
        /// 没有一个叫 `player_attack` 的资产 —— 于是 `_clips[2]` 恒为 null，
        /// 攻击一律静默回退到待机（这就是"挥锄播的是待机"的根因）✓
        ///
        /// ★ 为什么不是"把 ClipNames 改成每武器一套" ★
        /// 敌人与植物的片段**就是** `[id]_attack`（`imp_attack` / `brute_attack` 都在），
        /// 改造固定表会连带改掉它们走过的路径 ✓
        /// 这里改成"**可选覆盖**"：不设置就完全走原路径 ⇒ 敌人/植物/车不受任何影响 ✓
        /// </summary>
        private AnimationClip[] _attackByWeapon;
        private int _attackWeapon;
        private bool _warnedMissingAttack;

        /// <summary>
        /// ★ 程序化玩家的姿态体（`PlayerBean`）。非 null ⇒ 本实例是**豆豆人**，
        /// `Tick` 走解析姿态、**不采样任何片段** ★
        ///
        /// 为什么不另开一个类而把它挂在这里：
        /// `WorldView.SyncPlayer` 有 27 处 `_playerModel.XXX` —— 朝向、缩放、染色、受击脉动、
        /// 状态让位守卫、三层探针全在里面。换一套类型等于把那 27 处**一起**重写 ✗
        /// 而它们的语义（面向/缩放/染色/状态机）与"模型是不是片段驱动的"**正交** ✓
        /// ⇒ 挂一条可选分支：有姿态体就交给它，没有就完全走原来的片段路径 ✓
        /// 敌人 / 植物 / 小猪 / 商店车的 `Spawn` 不建姿态体 ⇒ 行为逐字不变 ✓
        /// </summary>
        private PlayerBean _bean;

        /// <summary>
        /// ★ 生成**程序化 Q 版豆豆人**（玩家专用）★
        ///
        /// 与 `Spawn` 的两层结构一致：外层 `MDL_farmer` 只管摆位（世界位置/朝向/缩放），
        /// 内层 `ANIM_farmer` 被姿态驱动 —— 于是姿态怎么写都不会顶掉游戏代码设的位置 ✓
        /// （这正是 `Spawn` 里那段"偏了 9.7 米"事故的修法，同一个原则）
        ///
        /// 与 FBX 路径的三个**有意不同**：
        ///   ① `BaseRotation = identity`：豆豆人是在代码里按 Y-up 建的，没有"FBX 根上的 270°"要保留
        ///   ② `RawHeight` **直接取值**而不是量包围盒：`MeshRawHeight` 量的是 `size.z`（FBX 是 Z-up），
        ///      对 Y-up 的豆豆人会把"帽檐进深 0.68"当成身高 ✗（这一处不量，是因为形状表就是真值）
        ///   ③ `_hasAnim = false`：没有片段 ⇒ `Tick` 里那条 `if (!_hasAnim) return` 不会把姿态饿死，
        ///      因为姿态分支在它**之前**就返回了（见 `Tick` 顶部）
        /// </summary>
        public static ModelView SpawnPlayer(Transform parent)
        {
            var view = new ModelView();
            view.Go = new GameObject("MDL_farmer");
            view.Go.transform.SetParent(parent, false);

            view._bean = PlayerBean.Build(view.Go.transform);
            view._animRoot = view._bean.Go;

            view.BaseRotation = view.Go.transform.rotation;
            view.BaseScale = Vector3.one;

            view._renderers = view.Go.GetComponentsInChildren<Renderer>(true);
            view._mpb = new MaterialPropertyBlock();
            view._baseColors = new Color[view._renderers.Length][];
            for (int i = 0; i < view._renderers.Length; i++)
            {
                Material[] mats = view._renderers[i].sharedMaterials;
                Color[] cols = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    cols[m] = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
                }
                view._baseColors[i] = cols;
            }
            view.CaptureRims();    // ★ v209：夜光的还原基准（白天回到这个值）★
            view.PushBlock();      // 先把"无染色/无霓虹"写死，别让着色器默认值决定观感

            view.RawHeight = PlayerBean.TotalHeight;
            view.LocalCenter = new Vector3(0f, PlayerBean.TotalHeight * 0.5f, 0f);
            view._hasAnim = false;
            view.Play(State.Idle);
            return view;
        }

        /// <summary>
        /// 取一把武器的模型表现体（给 `PlayerBean.MountTool` 与只读探针用）。
        /// `withAnim: false` —— 武器没有片段，不需要去 `Art/AnimMerged` 里找一遍 ✓
        /// </summary>
        public static ModelView SpawnTool(string id, Transform parent)
        {
            return Spawn("Tools", id, parent, false);
        }

        /// <summary>本实例是不是程序化豆豆人（验收台与探针据此选择读数口径）。</summary>
        public bool IsProceduralPlayer => _bean != null;

        /// <summary>豆豆人本体（非程序化时 null）。只读用途：验收台要能冻结到某一帧挥击。</summary>
        public PlayerBean Bean => _bean;

        /// <summary>
        /// 把第 `weaponIndex` 把武器挂到右手掌心，返回挂上去的 GameObject（调用方负责显隐）。
        /// 挂载参数的真值在 `PlayerBean` 里（见 `PlayerBean.ToolGripEuler` 的推导）。
        /// </summary>
        public GameObject MountTool(int weaponIndex)
        {
            return _bean != null ? _bean.MountTool(weaponIndex) : null;
        }

        /// <summary>
        /// ★ 把闪避的剩余秒数喂进来（`PlayerActor.EvadeGhost`）★
        ///
        /// 闪避在本类里**不是一个状态**：它不打断 Walk/Attack（玩家闪避后仍在跑、还能出手），
        /// 所以做成叠加层。而 `EvadeGhost` 的**虚化残影**是 HUD 画的（`ImGuiHud.DrawEvadeGhost`，
        /// 走 `GUI.color`），与本条互不覆盖：那条负责"多画一个半透明的我"，
        /// 本条负责"这个本体前倾一下" ✓
        /// </summary>
        public void SetEvade(float remainingSeconds)
        {
            if (_bean != null) _bean.SetEvade(remainingSeconds);
        }

        /// <summary>
        /// Walk 的播放倍率（v172）。默认 1 = 原速。
        ///
        /// 只作用于 Walk：它解决的是"移速变了、脚步频率没变"的滑步 ✗
        /// 攻击有它**自己的一路**倍率（见 `_attackSpeed`），不走这里 ✓
        /// </summary>
        private float _playbackSpeed = 1f;

        /// <summary>倍率上下限。上限按本工程数据留了余量，理由见 `SetPlaybackSpeed`。</summary>
        private const float MinPlaybackSpeed = 0.5f;
        private const float MaxPlaybackSpeed = 1.8f;

        /// <summary>
        /// ★ v174：攻击的播放倍率 ✗ —— ★★ 与 Walk **分开**的一路 ★★ ✓
        ///
        /// ── 它推翻的是 v172 写在本文件里的那条规则 ✗ ──
        /// v172 的原文是"攻击/受击/切换的时长是手感与可读性的一部分，
        /// 与移速无关，跟着一起变速是另一类错误"✗
        /// 而用户明确推翻了它 ✗ ★：
        ///   ★"★ 攻击动画与实际攻击速度不一致 ⇒ 让动画播放速度受到攻速影响 ★"★ ✓
        ///
        /// 他的理由是数据本身 ✗：三把武器的攻速**不同**
        ///（锄 `0.70s` ｜ 壶 `0.80s` ｜ 肥 `1.20s` ✗ 见 `tuning.json tools.*.cooldown`）
        /// ★ 而三段攻击片段【一样长】★（`player_attack_hoe/_can/_fert` 都是 `1.458s`）
        /// ⇒ 攻速相差 `1.71` 倍的两把武器，攻击动作的**真实时长完全相同** ✗
        /// ⇒ 这才是"表现与实际攻速不一致"的来源 ✓
        ///
        /// ── 与 Walk 那条是**同一类**错误 ✗ 不是两类 ──
        /// Walk 那条修的是"位移变快、脚步频率没变"，这一条修的是"出手变快、动作没变"✗
        /// 两者都是"表现跟不上逻辑已经开始的时间轴"✓
        /// v172 把"手感"当成不跟着走的理由 —— 那条理由站不住 ✗：
        /// 攻速**本来就是**数据决定的（还要吃攻速属性与插件），
        /// 动作照着它走才叫手感一致，固定片段长度才叫手感错位 ✓
        ///
        /// ⚠️ 夹取上限**不能**沿用 Walk 的 `1.8` ✗ ★（★ 这是本条最容易做错的地方 ★）★
        ///   基准数据下按"片段长 ÷ 攻速"算是：
        ///     锄 `1.458/0.70` = ★2.08★ ｜ 壶 `1.458/0.80` = ★1.82★ ｜ 肥 `1.458/1.20` = `1.22` ✓
        ///   ⇒ 若沿用 `1.8` ✗ ★锄头与水壶会被夹到同一个 1.8★
        ///     ⇒ ★★ 两把武器的动作一样快 ✗ 而这正是本任务要消灭的现象 ★★ ✓
        ///   ⇒ 用一个夹取上限把三把拍平是**自相矛盾** ✓
        ///   所以攻击单独给上限 ★4.0★ ✗：它覆盖"攻速堆到约 1.9 倍"
        ///   （锄头出手间隔 → `0.37s`）✓ 而**不**覆盖更多 ——
        ///   再快就是一片糊的残影，读不出"挥了"这件事 ✗
        ///   它的身份是**数据异常的兜底**（例如 cooldown 少写一位 ⇒ 倍率 20）✗
        ///   而【不是】一个设计用的旋钮 ✓
        /// </summary>
        private float _attackSpeed = 1f;

        private const float MinAttackSpeed = 0.25f;
        private const float MaxAttackSpeed = 4f;

        public float PlaybackSpeed => _playbackSpeed;

        /// <summary>★ v174：攻击那一路的倍率（探针用）★</summary>
        public float AttackPlaybackSpeed => _attackSpeed;

        /// <summary>
        /// ★ v174：**当前真正在被采样的**倍率 ✗ —— 探针要读的是这个 ✓
        ///
        /// 为什么不能直接报 `PlaybackSpeed` ✗：
        /// 它恒为 **Walk** 的倍率 ✗ ⇒ 攻击期间读到的是走路的那个 `1.00`
        /// ⇒ 看起来像"倍率没生效" ✗ 而实际生效的是 `_attackSpeed` ✓
        /// 报错量会把排查引向错误的方向 ——
        /// 本工程已有先例（`DebugApi.AnimState` 把两种原因合成一句话那段注释）✓
        /// </summary>
        public float EffectivePlaybackSpeed =>
            _state == State.Attack ? _attackSpeed
            : _state == State.Walk ? _playbackSpeed : 1f;

        /// <summary>当前真正会被采样的片段（已按武器解析过）。验证用。</summary>
        public AnimationClip CurrentClip => _hasAnim ? ClipFor(_state) : null;
        public string CurrentClipName =>
            _bean != null ? _bean.ClipName(_state, _attackWeapon)
                          : (CurrentClip != null ? CurrentClip.name : "(无)");

        /// <summary>
        /// 取出某个状态该用的片段。**攻击状态先看武器覆盖表**，其余一律走固定表。
        /// </summary>
        private AnimationClip ClipFor(State st)
        {
            if (_clips == null) return null;
            if (st == State.Attack && _attackByWeapon != null && _attackByWeapon.Length > 0)
            {
                int k = Mathf.Clamp(_attackWeapon, 0, _attackByWeapon.Length - 1);
                if (_attackByWeapon[k] != null) return _attackByWeapon[k];
            }
            int idx = (int)st;
            return idx >= 0 && idx < _clips.Length ? _clips[idx] : null;
        }

        /// <summary>
        /// 按 `Art/AnimMerged/{id}_{suffix}` 约定取一个片段。
        /// 路径知识留在本类里（与 `Spawn` 的加载同源），调用方只给 id 与后缀 ✓
        /// </summary>
        public static AnimationClip LoadClip(string id, string suffix)
        {
            return Resources.Load<AnimationClip>("Art/AnimMerged/" + id + "_" + suffix);
        }

        /// <summary>设置三把武器的攻击片段（下标 = 武器索引）。传 null 恢复固定表路径。</summary>
        public void SetAttackClips(AnimationClip[] clips)
        {
            _attackByWeapon = clips;
        }

        /// <summary>切换当前武器索引——决定 `Play(Attack)` 用哪一段。</summary>
        public void SetAttackWeapon(int weaponIndex)
        {
            _attackWeapon = Mathf.Max(0, weaponIndex);
            if (_bean != null) _bean.SetWeapon(_attackWeapon);   // 豆豆人：决定挥的是哪一套动作
        }

        /// <summary>诊断用：把三把武器的攻击片段是否取到报出来（一次性，不刷屏）。</summary>
        public string DescribeAttackClips()
        {
            if (_bean != null) return "(程序化姿态，无片段：farmer_attack_hoe/can/fert)";
            if (_attackByWeapon == null) return "(未设置，走固定表)";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _attackByWeapon.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_attackByWeapon[i] != null ? _attackByWeapon[i].name : "null");
            }
            return sb.ToString();
        }

        /// <summary>
        /// 设置 Walk 的播放倍率（1 = 原速）。
        ///
        /// ★ 为什么要夹取 ★
        /// 移速是**属性**，会随升级无限增长（`caps.moveSpeed` 暂定 9.0，基准 5.5 ⇒ 比值 1.64）✓
        /// 而脚步动画的倍率**不是**线性的：超过约 1.6~1.8 倍之后，
        /// 腿的摆动在人眼里已经读不出"走"，只剩抖动 ✗
        /// 所以上限定在 1.8 —— 覆盖当前数据的上限（1.64）并留一点余量，
        /// 又不会在某次数值膨胀后把模型抖成一团 ✓
        /// 下限 0.5 是给"减速"状态留的，避免慢放到看起来像卡住 ✓
        /// </summary>
        public void SetPlaybackSpeed(float k)
        {
            _playbackSpeed = Mathf.Clamp(k, MinPlaybackSpeed, MaxPlaybackSpeed);
        }

        /// <summary>
        /// ★ v174：让**当前武器的攻击片段**用 `seconds` 秒播完 ✗（单位是**真实秒** ✗ 不是倍率）★
        ///
        /// ── 为什么接口是"秒"而不是"倍率" ✗ ──
        /// 片段长度只有本类知道（`ClipFor(State.Attack)` ✗ 还要按武器解析 ✓）
        /// ⇒ 让调用方去算"片段长 ÷ 攻速"，就是把**路径知识漏到表现层的调度代码里** ✗
        /// 而调用方**真正掌握**的是玩法那边的"这一把几秒出手一次"✓
        /// 于是接口定在"秒"，换算留在本类 ✓
        /// —— 与 `LoadClip` 把 `Art/AnimMerged/` 这个路径留在本类是**同一条纪律** ✓
        ///
        /// ⚠️ 必须在 `SetAttackWeapon` **之后**调用 ✗ ★：
        ///    它解析的是**那一把**的片段 —— 顺序反了就会拿另一段的长度去算倍率 ✗
        ///    而算错的倍率看起来仍然"动了"（见 `SetPlaybackSpeed` 里同一类说明）✓
        /// ⚠️ `seconds <= 0`（数据缺 / 按 0 除）⇒ 退回 `1` 倍原速 ✗
        ///    不夹成 `4` 倍把动作糊成一团 —— "不知道多快"时就别变速 ✓
        /// ⚠️ 片段缺失（`_attackByWeapon` 没设置 / 资产没导）⇒ 同样退回 `1` ✗
        ///    这里【不】报错：缺片段那条由 `Tick` 统一报一次，不重复刷屏 ✓
        /// </summary>
        public void SetAttackDuration(float seconds)
        {
            // ★ 程序化豆豆人：**秒数就是秒数**，不折算倍率 ★
            // 片段路径上"倍率 = 片段长 ÷ 攻速"是因为片段长固定（1.458s）、攻速三把不同；
            // 而豆豆人的姿态是解析求值的，时长直接进 `u = t / len` ⇒ 三把自然各不相同时长 ✓
            // ⇒ 这里若也去夹 `1/seconds` 给 `_attackSpeed`，会变成"姿态时长 × 倍率"两次缩放 ✗
            if (_bean != null)
            {
                _bean.SetAttackSeconds(seconds);
                _attackSpeed = 1f;
                return;
            }
            AnimationClip c = ClipFor(State.Attack);
            if (c == null || c.length <= 0.0001f || seconds <= 0.001f) { _attackSpeed = 1f; return; }
            _attackSpeed = Mathf.Clamp(c.length / seconds, MinAttackSpeed, MaxAttackSpeed);
        }

        /// <summary>
        /// 生成一个模型表现体。
        /// category：Characters / Plants / Environment
        /// id：模型文件名（不含扩展名）
        /// 返回 null 表示资源不存在（调用方应退回图元，而不是崩）。
        /// </summary>
        public static ModelView Spawn(string category, string id, Transform parent, bool withAnim = true, bool simple = false)
        {
            string key = category + "/" + id;
            if (!PrefabCache.TryGetValue(key, out GameObject prefab))
            {
                // ★ 有动画的角色：优先用 **Anim FBX 的预制体** ★
                //
                // 为什么：SampleAnimation 是按**绑定路径**写值的，片段里的路径是
                // `AxisFix_pig/Root_pig/Body_pig/...`，而静态 FBX 导出来只有**一个网格节点**——
                // 路径对不上，采样**静默失效**（不报错，就是不动的）。
                // Anim FBX 里带着完整的关节层级，路径天然对得上。
                //
                // 这一条同时解释了"既有 20 个角色动画一个都没在跑"。
                // 回退链保证不会因此打不开：Anim 取不到就退回静态 FBX，再取不到返回 null 走图元。
                if (withAnim)
                    prefab = Resources.Load<GameObject>("Art/Anim/" + id + "_idle");
                if (prefab == null)
                    prefab = Resources.Load<GameObject>("Art/" + key);
                PrefabCache[key] = prefab;      // 缓存 null 也存，避免每帧重试加载
            }
            if (prefab == null) return null;

            var view = new ModelView();

            // ★ 两层结构：外层摆位，内层被动画采样 ★
            //
            // 为什么必须分开：SampleAnimation 会把片段里的**根节点位置/旋转曲线**也写进去，
            // 于是动画每帧都在覆盖游戏代码设好的世界位置。
            // 实测证据：玩家逻辑位置 (-2.3, 0.0)，模型却被推到 (7.44, 0, -1.06) —— 偏了 9.7 米。
            // 换成 Anim FBX 之前不暴露，是因为那时绑定路径对不上、采样根本没生效；
            // 上一轮为了拿关节层级换了预制体，这个覆盖才开始起作用——也就是"玩家模型位置异常"的来源。
            //
            // 分工：
            //   外层 Go      —— 只承载**预制体根上的轴向旋转与单位缩放**（下面那段"原样保留"的逻辑不变），
            //                   世界位置/朝向由游戏代码设，动画碰不到它
            //   内层 _animRoot —— 木偶根，SampleAnimation 作用在这一层，它怎么写都不影响摆位
            view.Go = new GameObject("MDL_" + id);
            view.Go.transform.SetParent(parent, false);
            GameObject inner = Object.Instantiate(prefab, view.Go.transform);
            inner.name = "ANIM_" + id;
            // 把预制体根上的**整个变换**搬到外层，内层归位——轴向与单位换算仍然只有一份，在 Go 上。
            //
            // ★ 位置也必须一起搬 ★
            // v98 修：第一版只搬了 rotation 与 scale，**漏了 localPosition**。
            // 于是每个模型的网格都还带着 FBX 根上的那个本地偏移，
            // 表现为"模型都在场上、drawCall 也正常，但位置/观感整体错乱"——
            // 树看不着、房子和耕地不见、装饰花乱飘，全都对得上。
            view.Go.transform.localPosition = inner.transform.localPosition;
            view.Go.transform.localRotation = inner.transform.localRotation;
            view.Go.transform.localScale = inner.transform.localScale;
            inner.transform.localPosition = Vector3.zero;
            inner.transform.localRotation = Quaternion.identity;
            inner.transform.localScale = Vector3.one;
            view._animRoot = inner;

            // ★ 原样保留预制体的旋转与缩放 ★
            //
            // 实测（MeshProbe）：Unity 从 FBX 导入出来的每个模型根节点都是
            //     scale = 100      —— 网格按 1/100 存储，乘回去才是米制
            //     rotation = 270°绕X —— 网格是 Z-up，靠它转成 Unity 的 Y-up
            // 两者都是**必需的**，不是冗余。
            //
            // 这里踩过一个大坑：我曾把 rotation 抹成 identity（以为导入器多转了一次），
            // 又把 localScale 直接赋成 1.35（以为是替换而非叠加）。
            // 结果——角色/耕地/植物因为代码里各自设了 rotation/scale 而"碰巧"还行，
            // 装饰（树/石/花）没人管，于是要么躺倒、要么被放大 100 倍。
            // 正确做法是组合：SetFacing() 乘上 BaseRotation，SetScaleFactor() 乘上 BaseScale。
            view.BaseRotation = view.Go.transform.rotation;
            view.BaseScale = view.Go.transform.localScale;

            // ★ 按**实测**归一网格单位，不按目录 ★
            //
            // 导入结果取决于 meta 是什么时候生成的：同一个 Blender 导出，
            // 有时进来是米制网格（tree 的 bounds.z = 4.229），有时是 1/100（0.042），
            // 而根节点**在两种情况下都是 scale=100**。
            // 于是"什么时候重新导入过"决定了模型是 4m 还是 400m —— 这个不确定性必须消掉。
            //
            // 判据用实测：**网格自身**的最大尺寸 ≥ 0.05 就说明它已经是米制（草 0.242、树 4.229），
            // 那就把根缩放压回 1；否则是 1/100 存储（角色网格 0.01、植物网格 0.015），保留根上的 100 还原。
            // 两种约定被这个判据干净地分开，且**不依赖任何目录名**。
            //
            // ★★★ v175 修复：判据以前用的是 `MeshRawHeight(Go) / |BaseScale.z|` ✗ ★★★
            //
            // ── 症状（用户原话）──
            // "选择了初始种子后，没有生成在耕地上，而是处于一个游走状态"
            // 实测：`MDL_pea_shooter*` 的 `localScale = 340.102`（`BaseScale = 100`），
            //       它站在 (-9.96, -11.85) —— 离它该在的耕地格 (-6.25, -6.25) **三条田宽** ✗
            //
            // ── 成因：**同一次换算被除了两遍** ✗ ──
            // `MeshRawHeight` 的契约是"**含根缩放**的尺寸"（它用 `localToWorldMatrix`，
            // 见本文件 :486 那段"两个消费者单位要求不同"的记录）✓
            // 而这里又除了一次 `BaseScale.z` ⇒ 量到的是**网格空间的尺寸** ⇒ 又要跟 0.05 比 ✗
            //   植物：`MeshRawHeight` = 0.6225m（真实米制）✗ 除以 100 ⇒ 0.0062 < 0.05
            //   ⇒ 判据**不触发** ⇒ `BaseScale` 保留 100 ⇒ 那 100 **被多乘了一次**
            //   ⇒ `ScaleToHeight(1.8m)` 得到 `localScale = 100 × 1.8/0.6225 ≈ 289`
            //   ⇒ 一株 0.62m 的苗变成 **一百八十米**，而 `CenterOn` 把**它的几何中心**对到格心
            //     ⇒ 于是它的**枢轴**被推到田外几十米 ⇒ 画面里就是"一株巨大的植物在田外游走"✓
            //
            // ★ 为什么不能只把 0.05 改小 ★：那个阈值本身没错（它问的是"网格是不是米制"），
            //   错的是**喂给它的数**量的是网格空间还是世界空间。改成 0.0005 只是把
            //   同一个错误挪到别处：真正米制的**小**模型（花 0.15m）会被重新判成 1/100 ✗
            //
            // ★ 为什么这次要用"网格自身"的度量 ★：判据问的是**存储约定**，
            //   那就必须看**存储**（网格坐标），而不是看已经被根缩放乘过的世界尺寸 ✓
            //   两者在 `|BaseScale| == 1` 时才相等 —— 而这条判据存在的唯一理由
            //   恰恰就是"根缩放不是 1"的情况 ✗ 所以旧写法在它唯一要起作用的场合必然错 ✓
            //
            // ★★★ v175 第二版：**两只都试过 ✗ 必须用"归一系数"这一条 ★★★
            //
            // 实测（`DebugApi.CropModelDiag`，同一格三档）：
            //   `pea_shooter_young`：`MeshRawHeight` = 0.2819m ✗ 网格自身 = 0.00385 ✗ 根缩放 = 100
            //   `pea_shooter_grown`：`MeshRawHeight` = 0.6225m ✗ 网格自身 = 0.00996 ✗ 根缩放 = 100
            //   `pea_shooter`      ：`MeshRawHeight` = 1.5383m ✗ 网格自身 = 0.01790 ✗ 根缩放 = 100
            // ⇒ ★ 这三行同时说明了两件事 ★：
            //   ① `MeshRawHeight` **已经含根缩放**（0.01538 × 100 = 1.5383 ✓）⇒ 它是**米制** ✓
            //   ② 网格自身的坐标是 **厘米制**（0.0179 单位 = 1.79 米 ✓ 与策划案"成体 1.62m"同量级 ✓）
            // ⇒ 所以"网格自身 ≥ 0.05 就是米制"这条判据对**本项目全部资产**都是假 ✗
            //   因为它们的网格坐标都存成厘米（草 0.0024 ✗ 树 0.042 ✗ 角色 0.01 ✓）
            //
            // ★ 而真正的病根比"判据选错分支"更具体：**那 100 被乘了两次** ✗
            //   ① `RawHeight = MeshRawHeight(Go)` 已经含根缩放 ⇒ 0.6225m ✓
            //   ② 而 `localScale` 又 = `BaseScale(100) × 系数` ⇒ 再乘一次 ✓
            //   ⇒ 于是 `ScaleToHeight(1.8m)` 算出 `系数 = 1.8/0.6225 = 2.89`
            //     而实际 `localScale = 289` ⇒ 一株 0.62m 的苗变成 **180 米** ✗✓
            //   ⇒ 而 `CenterOn` 对齐的是**它的几何中心** ⇒ 枢轴被推到田外几十米
            //   ⇒ 用户看到的正是"作物不在耕地上 ✗ 在田外游走"✓✓
            //
            // ★ 修法：**把根缩放压回 1** ★
            //
            // 为什么是 1 而不是"根缩放 100"✗ —— 这一点我搞错过一次，记在这里 ✗：
            //   · `MeshRawHeight(Go)` 量到的 0.2819 / 0.6225 / 1.5383m **就是这个世界里真实的尺寸** ✓
            //     （幼苗 0.29m ✗ 成长中 1.19m ✗ 成体 1.62m —— 与策划案《植物生长阶段对照》**同一组数** ✓）
            //   ⇒ ★ 所以这些模型**本来就是米制** ✗ 根上那个 ×100 是**多余的** ✓
            //   ⇒ `BaseScale` 必须 = 1 ✗ 否则 `localScale = 100 × 系数` ⇒ **那 100 被多乘一次** ✓
            //
            // ⚠️ 我上一版写成"网格自身 < 0.05 ⇒ 归一系数 = 根缩放"✗ 结果**与旧代码逐位相同** ✗
            //   因为网格自身的坐标是 $1.79/100$ 量级（0.0179）✗ 它**永远**小于 0.05 ✓
            //   ⇒ 判据两边都判假 ⇒ 又把 100 保留了 ✗
            //   ★ 教训：**"网格空间的尺寸"不能拿去跟"米制阈值"比** ✗
            //     要比就必须比**同一个空间**的量 —— 而这里唯一可信的米制量就是 `MeshRawHeight` ✓
            //
            // ★ 那这条判据还留着干什么（为什么不直接无条件压回 1）★
            //   因为 `MeshRawHeight` 的**兜底**是"测不到就返回 1f"（见本文件 :509 的 v147 记录）✓
            //   ⇒ 测不到时它返回 1 ⇒ 1 ≥ 0.05 会**误触发**"米制"⇒ 把一个真正的 1/100 模型压成 1 ✗
            //   ⇒ 所以先用"怎么量都 > 0"的那条路问一句"**归一化到底跑过没有**" ✗
            //     跑过就认 `MeshRawHeight` 是米制 ✓ 没跑过（`BaseScale == 1`）就**保持旧行为**（不动）✓
            //
            // ★ 这样 `RawHeight` 与 `localScale` 就**同单位**了 ★
            //   `ScaleToHeight(h)` ⇒ `系数 = h / RawHeight` ✗ 而 `localScale = 1 × 系数`
            //   ⇒ 世界高度 = `RawHeight × 系数` = **恰好 h** ✓✓
            //   ★ 实测（play 里读的）★：幼苗 世界=(-6.20,0.00,-6.33) ✗ 格心=(-6.25,-6.25)
            //     ⇒ 落在格心 **4~8 厘米**内 ✓；`MDL_pea_shooter*` 的 `d` 从 **12~17** 降到 **7.29**（在田内）✓
            //     而 `SelfCheck` 保持 **416/416 零失败** ✓
            // ★★ v176 回退我的回退 ✗：实证优先 ★★
            //
            // 我曾把这里改回"按网格空间的尺寸判是否米制"（`MeshRawHeight / |BaseScale.z| >= 0.05`）
            // —— 理由是"根上的 100 是厘米→米的换算，抹掉就小 100 倍"。
            // ★ 而用户实测：改回之后【作物又开始跑到田外】✗  ⇒ 那个理由不成立 ✓
            //
            // 反过来，下面这版（`rootScale > 1` 就把根缩放压回 1）是被实测证明过的 ✓：
            //   · 作物：世界坐标落回格心 **4~8 厘米**、`d` 从 12~17 降到 **7.29（田内）**、
            //     两时刻坐标【逐位相同】⇒ 不再游走 ✓
            //   · 连带复验六类：树 6.77m／耕地 scale 1.4793／玩家 1.8584／猪与小恶魔 2.6／栅栏 1
            //     ⇒ 【全部正常】⇒ 它并没有把别的模型弄小 ✓
            //
            // ⇒ 所以关键不是"网格是厘米还是米"，而是【让 `RawHeight` 与 `localScale` 同单位】✓
            //   而实测说明：把根缩放压回 1 之后两者同单位 ✓ 保留它则不同单位 ⇒ 作物被放大到田外 ✓
            float rootScale = Mathf.Abs(view.BaseScale.z);
            if (rootScale > 1.0001f)
            {
                view.Go.transform.localScale = Vector3.one;
                view.BaseScale = Vector3.one;
            }

            ToonLook.Apply(view.Go, simple);

            view._renderers = view.Go.GetComponentsInChildren<Renderer>(true);
            view._mpb = new MaterialPropertyBlock();
            view._baseColors = new Color[view._renderers.Length][];
            for (int i = 0; i < view._renderers.Length; i++)
            {
                Material[] mats = view._renderers[i].sharedMaterials;
                Color[] cols = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    cols[m] = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
                }
                view._baseColors[i] = cols;
            }

            view.CaptureRims();    // ★ v209：夜光的还原基准 ★

            // 记录模型的**实际世界高度**（米），用于 ScaleToHeight。
            // 用网格包围盒逐级累乘缩放，而不是 renderer.bounds —— 后者在实例化当帧不可信
            // （本项目已踩过一次）。
            view.RawHeight = MeshRawHeight(view.Go);
            view.LocalCenter = MeshLocalCenter(view.Go);

            if (withAnim)
            {
                if (!ClipCache.TryGetValue(key, out view._clips))
                {
                    var list = new AnimationClip[ClipNames.Length];
                    for (int i = 0; i < ClipNames.Length; i++)
                        list[i] = Resources.Load<AnimationClip>("Art/AnimMerged/" + id + "_" + ClipNames[i]);
                    view._clips = list;
                    ClipCache[key] = list;
                }
                view._hasAnim = view._clips != null && view._clips[0] != null;
                if (view._hasAnim)
                {
                    view.Play(State.Idle);
                    // ★ v180：解析骨骼 + 记下"站立基准帧"（`Idle@t=0`）★
                    // 骨骼不齐（敌人 / 植物 / 小猪）会自动 `_layered = false` ⇒ 保持旧行为 ✓
                    view.BuildLayeredPose();
                }
            }
            return view;
        }

        /// <summary>
        /// 模型的**实际世界高度**（米），把网格到根节点整条链上的缩放都算进去。
        ///
        /// ★ 两个坑都在这一个函数里 ★
        /// ① 网格是 Z-up 存的，所以身高在**本地 Z 轴**，用 Y 会拿到进深。
        /// ② 遍历必须**包含根节点**。第一版写成 `while (t != go.transform)` —— 把根排除了，
        ///    于是根上那个把 1/100 网格还原成米制的 scale=100 没被算进去；
        ///    我又在函数外面乘了一次 BaseScale 想补救，结果就成了"同一次换算算两遍"。
        ///    实测：0.5m 的植物被判成 50m，ScaleToHeight 反过来把实例缩到 3.3 倍，
        ///    世界尺寸变成 86 米，直接把相机埋进模型里（截图整屏都是土壤色）。
        /// </summary>
        /// <summary>
        /// 网格**并集包围盒**在根节点本地空间的中心与尺寸。两个函数共用同一套换算，避免各写一份。
        /// 换算规则：把每个网格的 bounds 经父链**逐级缩放**到根节点空间，再取并集。
        /// 不含根变换本身（根上的 270° 旋转与 scale=100 由调用方处理）。
        /// </summary>
        private static bool MeshLocalBounds(GameObject go, out Bounds bounds)
        {
            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            Bounds acc = new Bounds();
            bool any = false;
            for (int i = 0; i < mfs.Length; i++)
            {
                Mesh m = mfs[i].sharedMesh;
                if (m == null) continue;
                Vector3 s = Vector3.one;
                Transform t = mfs[i].transform;
                while (t != null)
                {
                    s = Vector3.Scale(s, t.localScale);
                    if (t == go.transform) break;      // 含根：根上的还原缩放也要算
                    t = t.parent;
                }
                var b = new Bounds(Vector3.Scale(m.bounds.center, s), Vector3.Scale(m.bounds.size, s));
                if (!any) { acc = b; any = true; }
                else acc.Encapsulate(b);
            }
            bounds = acc;
            return any;
        }

        /// <summary>网格几何中心，换算到根节点的本地空间。</summary>
        private static Vector3 MeshLocalCenter(GameObject go)
        {
            return MeshLocalBounds(go, out Bounds b) ? b.center : Vector3.zero;
        }

        /// <summary>
        /// 模型的**实际世界高度**（米）。
        ///
        /// ★ v98 回退：这里**必须取"最大单件的高度"，不能取并集包围盒的高度" ★
        ///
        /// v97 我一度把它改成并集（想把纵向铺开的多件模型量准，理由是"寒霜藤偏大"）。
        /// 结果是**整场景崩掉**：树/耕地/房子全部消失、植物异常。
        /// 原因：并集对**散件模型**极其脆弱——只要有一个部件离群，
        ///   或某个部件带着很大的累加缩放，并集就被撑爆，
        ///   高度被严重**高估** → ScaleToHeight(目标/超大高度) → 模型被缩到近乎不可见。
        /// "取最大单件"虽然对纵向铺开的模型偏小（会显得偏大），
        ///   但它**有上界**（不会超过最大的那一件），不会失控。
        ///
        /// 教训：一个用于**全局缩放**的度量，宁可偏保守，不能有失控方向。
        ///      偏小只是"某个品种偏大一点"，偏大是"整个场景消失"。
        ///      寒霜藤偏大的问题要单独治（调那一件模型的参数），不要动这个公共函数。
        ///
        /// 另外两个坑也在这个函数的历史里：
        ///   ① 网格是 Z-up 存的，所以身高在**本地 Z 轴**，用 Y 会拿到进深。
        ///   ② 父链遍历必须**包含根节点**——第一版把根排除了，根上那个"把 1/100 还原成米制"
        ///      的 scale=100 就没算进去，0.5m 的植物被判成 50m，相机直接被埋进模型里。
        /// </summary>
        private static float MeshRawHeight(GameObject go)
        {
            // ★ v147：同时支持 MeshFilter 与 SkinnedMeshRenderer，且**绝不返回 0** ★
            //
            // ── 事故记录（截图上"巨大灰色尖刺挡住整个画面"）──
            // 上一版只遍历 `MeshFilter`，遇到**蒙皮网格**一件都取不到，
            // 于是走到 `return any ? ... : 0f` 返回了 **0** ✗
            // 而 `ScaleToHeight(目标 ÷ RawHeight)` 里有 `Mathf.Max(0.0001f, RawHeight)`，
            // 0 被替换成 0.0001 → **放大 16247 倍** ✗✓
            // 实测：`MDL_pea_shooter_grown 世界高=173.72m  scale=16247.0`
            // 而 `_young` 是 351.8（RawHeight≈0.51，正常）✓ 所以只有 grown 出问题 ✓
            //
            // ★ 两条教训 ★
            // ① **`MeshRawHeight` 返回 0 是最危险的失败方式** ——
            //    它不是"不变"，而是"放大一万倍" ✗ 所以末尾必须有兜底 ✓
            // ② 蒙皮网格用 `SkinnedMeshRenderer`（**没有 MeshFilter**）✗
            //    同一个工程里两种渲染方式可以混用，测量函数必须都认 ✓
            //
            // 算法仍是世界空间 AABB（角点变换后取并集）——
            // 它正确处理旋转，这一点不要改回去 ✗（那曾导致 136m 的豌豆射手）
            Bounds acc = new Bounds();
            bool any = false;

            // ★ v153：**不再用 worldToLocalMatrix 抵消 go 自身缩放** ★
            //
            // ── 事故记录（树 676m / 武器 21088m / 玩家 7825m，画面被完全遮挡）──
            // 我为了让"旋转过的部件"量准，把这里从"本地尺寸 × 缩放"改成世界空间 AABB，
            // 并顺手用 `go.transform.worldToLocalMatrix` **抵消了 go 自身的缩放** ✗
            //
            // 但 `MeshRawHeight` 有**两个消费者，对单位的要求不同** ✗：
            //
            //   ① `ScaleToHeight`（本文件 L388 附近）—— 要"含根缩放的尺寸" ✓
            //   ② **米制/厘米制判据**（本文件 L143）：
            //        meshZ = MeshRawHeight(go) / |BaseScale.z|
            //        它**期望 MeshRawHeight 已经含根缩放**，然后自己再除一次 ✗
            //
            // 我抵消了缩放 ⇒ 调用处除了**两遍** ⇒
            //   树网格 4.229 ÷ 100（我）÷ 100（调用处）= 0.00042 < 0.05
            //   ⇒ 判据不触发 ⇒ BaseScale 保留 100 ⇒ lossyScale = 160 ⇒ **树变成 676 米** ✗✓
            //
            // ★ 所以这里必须用**含根缩放的** `localToWorldMatrix`（不抵消）★
            // 这样返回值与旧实现同单位，两个消费者都成立 ✓
            // 而我保留的改进（用角点变换、正确处理旋转）不受影响 ✓
            //
            // 教训：**改共享函数的语义时，必须逐个检查消费者的期望** ✗
            // 这已经是本会话第三次同类错误（三处定价、两个时间源、一个函数两种期望）✓
            MeshFilter[] mfs = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                Mesh m = mfs[i].sharedMesh;
                if (m == null || mfs[i].transform == null) continue;
                Encapsulate(ref acc, ref any, m.bounds, mfs[i].transform.localToWorldMatrix);
            }

            // 蒙皮网格：sharedMesh 在 SkinnedMeshRenderer 上，同样有 localToWorldMatrix ✓
            SkinnedMeshRenderer[] smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < smrs.Length; i++)
            {
                Mesh m = smrs[i].sharedMesh;
                if (m == null || smrs[i].transform == null) continue;
                Encapsulate(ref acc, ref any, m.bounds, smrs[i].transform.localToWorldMatrix);
            }

            if (!any) return 1f;      // ★ 兜底：测不到就按 1 米算（≈不变），绝不返回 0 ✗
            float h = Mathf.Abs(acc.size.z);
            return h > 0.0001f ? h : 1f;   // 退化包围盒同样兜底 ✓
        }

        /// <summary>把一个网格的本地包围盒（8 角点）变换后并入累加包围盒。</summary>
        private static void Encapsulate(ref Bounds acc, ref bool any, Bounds lb, Matrix4x4 toGo)
        {
            Vector3 c = lb.center, e = lb.extents;
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = toGo.MultiplyPoint3x4(c + new Vector3(
                    (k & 1) == 0 ? -e.x : e.x,
                    (k & 2) == 0 ? -e.y : e.y,
                    (k & 4) == 0 ? -e.z : e.z));
                if (!any) { acc = new Bounds(p, Vector3.zero); any = true; }
                else acc.Encapsulate(p);
            }
        }

        /// <summary>
        /// 设置朝向（绕世界 Y 轴）。**乘**在原始旋转上，不覆盖——
        /// 覆盖会连带把"网格 Z-up → 世界 Y-up"那次必需转换一起抹掉。
        ///
        /// ★ 乘法顺序不能反 ★
        /// 必须在**左边**：Euler(0,yaw,0) * BaseRotation。
        ///   · 右边（BaseRotation * Euler(0,yaw,0)）= 先在**网格空间**转 yaw，再做轴向转换。
        ///     而网格是 Z-up 的——网格空间的 +Y 是模型的**进深轴**，
        ///     于是这个"yaw"变成绕自身进深翻滚，模型被转倒（玩家看到的就是"模型全部倒下"）。
        ///   · 左边 = 先做轴向转换（把模型摆正），再绕世界 Y 偏航 —— 这才是"转身"。
        /// </summary>
        public void SetFacing(float yawDegrees)
        {
            Go.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f) * BaseRotation;
        }

        /// <summary>设置缩放系数（1 = 模型原始大小）。同样是乘，不是替换。</summary>
        public void SetScaleFactor(float k)
        {
            _baseScaleFactor = k;
            ApplyScale();
        }

        /// <summary>当前缩放系数（`SetScaleFactor` 写进去的那个 k，不含受击脉动）。
        /// 诊断用：`localScale = BaseScale × 它` ⇒ 两者一起看就知道基数是否被归一过 ✓</summary>
        public float ScaleFactor => _baseScaleFactor;

        /// <summary>
        /// 受击表现：**闪白 + 略微放大再瞬间缩回**（v88）。
        ///
        /// 为什么要把脉动单独叠一层，而不是直接改 localScale：
        /// 缩放每帧都会被 ScaleToHeight/SetScaleFactor 重设（生长、统一大小都靠它），
        /// 直接赋值会被下一帧覆盖掉。所以这里保存"基础倍率"，由 ApplyScale() 统一乘上脉动系数。
        ///
        /// 曲线是**快上慢下**：命中瞬间置 1（立刻胀一下），之后按固定速率回落 ——
        /// 需求原话是"模型略微放大再瞬间缩小为正常值"，所以回落要快，不能拖成弹簧。
        /// 闪白本身走 SetTint，这里只管体量。
        /// </summary>
        public void SetHitFlash(bool on, float dt)
        {
            if (on) _hitPulse = 1f;
            else if (_hitPulse > 0f) _hitPulse = Mathf.Max(0f, _hitPulse - dt / HitPulseDecay);
            ApplyScale();
        }

        private const float HitPulseDecay = 0.12f;   // 秒
        private const float HitPulseAmp = 0.14f;     // 最大放大 14%

        private float _baseScaleFactor = 1f;
        private float _hitPulse;

        /// <summary>把基础倍率与受击脉动一起写进缩放。**所有改缩放的地方都必须走这里。**</summary>
        private void ApplyScale()
        {
            Go.transform.localScale = BaseScale * (_baseScaleFactor * (1f + HitPulseAmp * _hitPulse));
        }

        /// <summary>
        /// 模型的**米制高度**（已经是"缩放系数 = 1"时的真实高度）。
        ///
        /// ★ 这里必须把 BaseScale 乘回去 ★
        /// 网格是按 1/100 存储的（Unity 从 FBX 导入时根节点 scale=100 就是为了还原它）。
        /// 如果这里给的是网格本地尺寸，ScaleToHeight 会算成
        ///     目标高度 / 网格尺寸 * BaseScale
        /// 也就是先放大 100 倍再乘系数——实测一个 0.59m 的植物被撑到 30500 倍，
        /// 变成 18 公里高，直接把相机埋进模型里（截图整屏都是一块土壤色）。
        /// </summary>
        public float RawHeight { get; private set; }

        /// <summary>
        /// 网格包围盒中心在**根节点本地空间**的位置。
        /// 模型的枢轴点不一定在几何中心（Blender 里物体原点在哪就是哪），
        /// 直接把 position 设成目标点会让物体看起来偏在一边——
        /// 作物"没对齐耕地中心"就是这么来的。
        /// </summary>
        public Vector3 LocalCenter { get; private set; }

        /// <summary>
        /// 把模型的**几何中心**（而不是枢轴）对到目标点的水平位置，底面贴 y=0。
        /// 先归位再按几何中心校正；几何中心是实例化时量好的，不依赖 renderer.bounds
        /// （后者在同一帧内不可信，本项目已踩过）。
        /// </summary>
        public void CenterOn(Vector3 target)
        {
            Go.transform.position = new Vector3(target.x, 0f, target.z);
            Vector3 wc = Go.transform.TransformPoint(LocalCenter);
            Go.transform.position += new Vector3(target.x - wc.x, 0f, target.z - wc.z);
        }

        /// <summary>把模型缩放到指定位高（米）。用于"按玩法尺度对齐"而不依赖导入缩放的具体数值。</summary>
        public void ScaleToHeight(float targetHeight)
        {
            if (RawHeight <= 0.0001f || targetHeight <= 0.01f) return;
            SetScaleFactor(targetHeight / RawHeight);
        }

        // ──────────────────────────────────────────── 染色

        /// <summary>
        /// 整体染色（乘法）。用于冰冻/灼烧/中毒/受击闪白/血量变暗这些反馈。
        ///
        /// ★ 只能写 _TintMul，绝不能写 _BaseColor ★
        /// MaterialPropertyBlock 是**按渲染器**生效的，一个角色身上有 6~11 个材质槽，
        /// 用 MPB 设 _BaseColor 会把所有槽位覆盖成同一个颜色——实测表现就是"敌人变成深色一团"
        /// （最后设置的那个材质的颜色赢了）。所以着色器里单独留了染色乘数，
        /// 由它在 shader 里与各槽位自己的底色相乘。
        /// </summary>
        public void SetTint(Color mul)
        {
            if (_renderers == null) return;
            _tint = mul;
            PushBlock();
        }

        /// <summary>
        /// 霓虹高亮描边（在黑色墨线之外再套一圈发光）。
        /// alpha 用作开关：0 = 关闭。宽度按模型尺寸自动换算——固定毫米数
        /// 在 0.7m 的小恶魔上糊成一坨、在 1.5m 的大恶魔上又看不见。
        ///
        /// ★ v208：新增 <paramref name="pixels"/> 与 <paramref name="intensity"/> ★
        ///
        /// ── `pixels` 为什么必须**逐实例**，而不是只在材质上写一次 ──
        /// 着色器里霓虹的实际宽度是 `max(_NeonWidth 米, 每像素米数 × _NeonPixels)`。
        /// 那个像素下限本来是为了"远处也看得见"（游戏机位 26 米、1080p 下 1 像素 ≈ 2.9 厘米
        /// ⇒ 1.4 像素 ≈ **4 厘米**）。对 1.5 米高的敌人这叫"看得见"；
        /// 对一株 0.4 米的作物，**4 厘米是往外扩一圈的壳**，霓虹壳会把整株吞掉变成一坨纯色 ✗
        /// ⇒ 敌人/玩家要粗（2.2 像素），作物要细（0.8 像素）。
        /// 而这三个值以前只能写在**材质**上（`ToonLook.Get` 里），作物与敌人共用同一份材质
        /// ⇒ 只能取一个折中值 ⇒ 两边都不对。
        /// 现在 `_NeonPixels` 走 **MaterialPropertyBlock**（逐渲染器），
        /// 一份材质可以同时服务"粗的敌人"和"细的作物"✓
        ///
        /// ── `intensity` 为什么不是"改颜色" ──
        /// 霓虹 Pass 是 `Blend One One` 的加色混合，颜色本身可以 **超过 1**（`[HDR]`）。
        /// 把它乘上去 = 在同样的几何宽度下**更亮**，暗处也压得住背景 ✓
        /// 这比"把颜色调浅"好：调浅会掉饱和度，红霓虹会变成粉 ✗
        /// </summary>
        public void SetNeon(Color color, float widthMeters = 0f, float pixels = 1.4f, float intensity = 1f)
        {
            if (_renderers == null) return;
            // ★ v209：这里改成"**记下底色与强度**，再由 `ComposeNeon` 合成"
            //   —— 因为夜里要把强度再抬一档（`SetNightGlow`），而抬档必须有个**不变的起点**。
            //   旧写法把 `color × intensity` 直接揉进 `_neon` ⇒ 夜里再乘一次就没有基准了
            //   （`SetNeon` 之后再 `SetNightGlow` 会把强度乘两遍，而且顺序不同结果不同）✓
            // 只放大 rgb；alpha 是**开关**，乘了 intensity 会把"关掉"（a=0）变成 a=0 ✓，但也可能把 a 推到 >1
            // ⇒ 显式保留 alpha 原值
            _neonBase = new Color(color.r, color.g, color.b, color.a);
            _neonBoost = intensity;
            _neonPixelsBase = pixels;
            // v92：描边加宽 0.035→0.050（需求"加强霓虹光描边"）。
            // 宽度是"加强"最直接的手段——只提亮不提宽，在远处小模型上几乎看不出变化。
            _neonWidth = widthMeters > 0.001f ? widthMeters : Mathf.Clamp(RawHeight * 0.050f, 0.018f, 0.09f);
            RebuildNightRim();      // ★ v209：夜间的本体边缘光目标色（由这个底色归一化算出）★
            ComposeNeon();
            PushBlock();
        }

        /// <summary>
        /// 由"底色 + 强度 + 夜间发光系数"合成写进 MPB 的那一份霓虹值。**唯一的合成点** ✓
        ///
        /// `g = 0`（白天）⇒ `boost = _neonBoost`、`pixels = _neonPixelsBase` ⇒ **与改前逐位相同** ✓
        /// </summary>
        private void ComposeNeon()
        {
            float g = Mathf.Clamp01(_nightGlow);
            float boost = ToonLook.NightNeonMultiplier(_neonBoost, g);   // g=0 ⇒ 原值，逐位相同 ✓
            _neon = new Color(_neonBase.r * boost, _neonBase.g * boost, _neonBase.b * boost, _neonBase.a);
            _neonPixels = _neonPixelsBase + g * ToonLook.NightGlowExtraPixels;
        }

        /// <summary>
        /// ★ v209：**夜间发光** —— 每帧由 `WorldView` 喂进来（值来自 `DayNightCycle` 的唯一真相）★
        ///
        /// `glow01`：0 = 白天完全不发光 ｜ 1 = 深夜满亮度。
        ///
        /// ── 它做什么 ──
        ///   ① 把 `_NeonColor` 的强度抬到 `1 + Glow01 × ToonLook.NightGlowGain` 倍（HDR 加色通道）
        ///   ② 霓虹壳加宽 `Glow01 × NightGlowExtraPixels` 个屏幕像素
        ///   ③ 把本体边缘光（`_RimColor`）往霓虹色推 ⇒ 光留在**身体轮廓**上，不只是外面一圈
        /// 三条**都只写 MaterialPropertyBlock**（逐实例）⇒ 绝不 `new Material` ✓
        ///
        /// ── 为什么"没变就早退" ──
        /// 场上敌人 82 个渲染器 × 每次 `PushBlock` 逐渲染器写 5 个属性。
        /// 而 `DayNightCycle.Glow01` 在**深夜的大半段时间里恒为 1**、白天恒为 0
        /// ⇒ 只有黄昏/黎明那约 28 秒会真的变化 ⇒ 早退让"其余时间零成本" ✓
        /// （阈值 5e-4 远小于一帧能变的最大量，见 `DayNightCycle.Report()` 的"夜光 xx／步"）
        ///
        /// ⚠️ **只有敌人会调它**（`WorldView` 的敌人同步循环）。
        ///   玩家与作物不调 ⇒ 它们的 `_nightGlow` 恒为 0 ⇒ 观感**一个像素都不变** ✓
        ///   这正是用户要的"敌人明显发亮、玩家/植物不那么亮" ✓
        /// </summary>
        public void SetNightGlow(float glow01)
        {
            if (_renderers == null) return;
            float g = Mathf.Clamp01(glow01);
            if (Mathf.Abs(g - _nightGlow) < 0.0005f) return;     // 没变 ⇒ 一次 MPB 写入都不做
            _nightGlow = g;
            // 霓虹本来就关着（小猪 / 无描边的对象）⇒ 没有什么可以点亮，别去碰它的 MPB ✓
            if (_neonBase.a <= 0.001f && !_rimPushed) return;
            ComposeNeon();
            PushBlock();
        }

        /// <summary>当前夜间发光系数（探针/自检读数用）。</summary>
        public float NightGlow => _nightGlow;

        /// <summary>
        /// ★ v209：**当前写进 MPB 的霓虹颜色**（已含夜间增益）。读数用。
        ///
        /// 它证明的是"MPB 里真的是这个值"，而不是"代码算了一遍"——
        /// 两者不是同一个断言（本工程已有先例：`NightLighting.LiveDump` 从引擎对象读回）。</summary>
        public Color NeonColorApplied => _neon;

        /// <summary>当前写进 MPB 的霓虹屏幕最小宽度（像素，已含夜间加宽）。</summary>
        public float NeonPixelsApplied => _neonPixels;

        private Color _tint = Color.white;
        private Color _neon = new Color(0f, 0f, 0f, 0f);
        private float _neonWidth = 0.022f;
        private float _neonPixels = 1.4f;

        // ── v209：夜间发光的四个格子 ──
        /// <summary>霓虹的**底色**（未乘强度）。`ComposeNeon` 的唯一输入之一。</summary>
        private Color _neonBase = new Color(0f, 0f, 0f, 0f);
        /// <summary>霓虹的基础强度倍率（`SetNeon` 的 `intensity`，敌人 = 1.60）。</summary>
        private float _neonBoost = 1f;
        /// <summary>霓虹屏幕最小宽度（像素，未含夜间加宽）。</summary>
        private float _neonPixelsBase = 1.4f;
        /// <summary>夜间发光系数 0..1（只有敌人会被喂）。</summary>
        private float _nightGlow;

        /// <summary>
        /// 每个渲染器的**原始边缘光颜色**（材质级 `_RimColor` 的快照）。
        /// 夜里把它往霓虹色推；回到白天时用它**精确还原** ⇒ 白天与改前逐位相同 ✓
        /// 只存一个 `Color` 而不是"每槽一份"：`ToonLook.Get` 对每个槽写的 `_RimColor` 是同一个值，
        /// 而 MPB 本来就是**按渲染器**生效的（一个渲染器只有一个 `_RimColor`）✓
        /// </summary>
        private Color[] _baseRims;
        private bool _rimPushed;

        /// <summary>把染色与霓虹一次性写进 MaterialPropertyBlock（逐实例，不污染共享材质）。</summary>
        private void PushBlock()
        {
            // ★ v209：夜光只在真的亮着时动 `_RimColor` ★
            // 白天（或者这个实例从没被喂过发光）⇒ **一次都不写边缘光** ⇒ 观感与改前逐位相同 ✓
            // 从亮到不亮的那一刻要写回**原始值**（材质级 `_RimColor` 的快照），
            // 否则 MPB 里那份红色边缘光会一直留在那儿 —— 天亮了敌人身上还有一圈红光 ✓
            bool wantRim = _nightGlow > 0.0005f && _neonBase.a > 0.001f && _nightRimTargetValid;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                _mpb.Clear();
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(TintMulId, _tint);
                _mpb.SetColor(NeonColorId, _neon);
                _mpb.SetFloat(NeonWidthId, _neonWidth);
                // ★ v208：霓虹的**屏幕最小宽度**也逐实例（见 SetNeon 的说明）★
                _mpb.SetFloat(NeonPixelsId, _neonPixels);
                if (wantRim)
                    _mpb.SetColor(RimColorId, Color.Lerp(OriginalRimAt(i), _nightRimTarget, _nightGlow));
                else if (_rimPushed)
                    _mpb.SetColor(RimColorId, OriginalRimAt(i));      // 还原成材质原值
                r.SetPropertyBlock(_mpb);
            }
            _rimPushed = wantRim;
        }

        /// <summary>
        /// 夜光的**本体边缘光目标色**：由霓虹底色归一化后提亮，透明度用 `NightGlowRimAlpha`。
        ///
        /// 用"归一化"而不是原样乘：霓虹底色是 (1.00,0.12,0.24) 这种高饱和红，
        /// 直接乘亮度只会更红更暗；先把它拉到最亮通道 = 亮度上限，再加权 ⇒ 才是**发光**的观感 ✓
        /// `_neonBase.a <= 0`（霓虹关着）⇒ 没有目标色，`_nightRimTargetValid = false` ✓
        /// </summary>
        private void RebuildNightRim()
        {
            float m = Mathf.Max(_neonBase.r, Mathf.Max(_neonBase.g, _neonBase.b));
            _nightRimTargetValid = _neonBase.a > 0.001f && m > 0.001f;
            if (!_nightRimTargetValid) { _nightRimTarget = Color.white; return; }
            float b = ToonLook.NightGlowRimBoost;
            _nightRimTarget = new Color(_neonBase.r / m * b, _neonBase.g / m * b, _neonBase.b / m * b,
                                        ToonLook.NightGlowRimAlpha);
        }

        private Color _nightRimTarget = Color.white;
        private bool _nightRimTargetValid;

        /// <summary>第 <paramref name="i"/> 个渲染器在材质里的原始 `_RimColor`（快照）。</summary>
        private Color OriginalRimAt(int i)
        {
            return _baseRims != null && i >= 0 && i < _baseRims.Length ? _baseRims[i] : Color.white;
        }

        /// <summary>
        /// 快照每个渲染器的材质级 `_RimColor`（夜光回落到白天时的还原值）。
        /// 只在 `Spawn*` 里调一次；**不在字段初始化器里建任何东西**（硬约束）✓
        /// </summary>
        private void CaptureRims()
        {
            if (_renderers == null) return;
            _baseRims = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                Color rim = Color.white;
                Material[] mats = r != null ? r.sharedMaterials : null;
                if (mats != null && mats.Length > 0 && mats[0] != null && mats[0].HasProperty(RimColorId))
                    rim = mats[0].GetColor(RimColorId);
                _baseRims[i] = rim;
            }
        }

        // ──────────────────────────────────────────── 动画
        public void Play(State st)
        {
            if (_state == st) return;
            EnterState(st);
        }

        /// <summary>强制切状态（受击/死亡这类打断）。</summary>
        public void PlayForced(State st) => EnterState(st);

        /// <summary>
        /// 状态切换的**唯一入口**（两个公开方法都走这里，避免两处各写一半）。
        ///
        /// ★ v182：**这里原来有一行 `_locoTime = _stateTime`，已删除** ★
        ///
        /// ── 为什么要删（★ 实测，不是推断 ★）──
        /// v180 加它是为了"进攻击时把走路相位接上"。但 `_stateTime` 在**这一行的下两行
        /// 就被归零**（`_stateTime = 0f`）⇒ 它接过去的其实是"上一个状态待了多久"✗
        /// 于是腿相位在**每次状态切换**上被重新赋值一次：
        ///   · 出手（Attack 结束自动回 Idle/Walk）⇒ 腿的采样时刻从 `_locoTime` 换成
        ///     `Repeat(_stateTime=0, len)` = **片段第 0 帧** ✗
        ///   · 连打（上一刀还没播完就再出手）⇒ `_locoTime = _stateTime`（攻击态的时间）
        ///   ⇒ 腿相位被拽回一个与走路**无关**的值 ✗
        /// 定步长重放实测（`AnimJerk` 的连打模式，3 段）：
        ///   `边界跳变 Thigh_L=29.58°@f111 Thigh_R=29.58°@f111`
        ///   而同一次攻击**段内**的 `max|Δ|` 只有 4.74°、`max|Δ²|` 0.83° ✗
        /// ⇒ 一帧之内腿跳 29.58° 就是用户说的"抽搐"：每次挥击都把腿抽回去一下 ✓
        ///
        /// ── 现在靠什么保证连续 ──
        /// `_locoTime` 变成**只增不减**的腿相位累加器（见 `Tick` 顶部）：
        /// 任何状态都推进它、**任何状态都不重置它** ⇒ 出手前后用的是同一个相位值 ✓
        /// 连续性不再依赖"在边界上补一次赋值"，而是从结构上不可能断 ✓
        /// </summary>
        private void EnterState(State st)
        {
            _state = st;
            _stateTime = 0f;
            // ★ v183：换状态 ⇒ 低通从头起步（第一帧直接吃目标姿态，不吃上一段的余尾）★
            // 否则"上一次攻击的收招姿态"会被 Slerp 拖进这一刀的第 0 帧 ⇒ 又造出一个边界跳变 ✗
            _smPoseValid = false;
        }

        /// <summary>
        /// ★ 程序化豆豆人的推进：状态时间轴 + 一次性动作回待机 + 姿态求值 ★
        ///
        /// 这一小段承担的是片段路径里 `Tick` 的一部分职责，两处**语义必须一致**，
        /// 所以逐条对齐（不变量：`WorldView` 只看 `Current` 与 `StateTime`，两边口径一样）：
        ///   ① 时间轴倍率：Attack 吃攻击倍率 / Walk 吃移速倍率 / 其余 1 倍 —— 与片段路径同一张表
        ///      （豆豆人的 `_attackSpeed` 恒为 1，见 `SetAttackDuration` 的注释 ✓）
        ///   ② 一次性动作（Attack / Hit / Swap）播完**回 Idle** —— `WorldView` 靠这条做状态恢复，
        ///      少了它，攻击之后玩家会永久停在攻击段的最后一帧 ✗
        ///   ③ Death 停住（不回待机）、Idle/Walk 循环 ✓
        /// </summary>
        private void TickBean(float dt)
        {
            float rate = _state == State.Attack ? _attackSpeed
                       : _state == State.Walk ? _playbackSpeed : 1f;
            _stateTime += dt * rate;

            float len = BeanStateSeconds(_state);
            if (len > 0f && _stateTime >= len) Play(State.Idle);

            _bean.Pose(_state, _stateTime, _playbackSpeed, dt);
        }

        /// <summary>豆豆人某个状态的长度（秒）。0 = 循环或停住，永不自动回待机。</summary>
        private float BeanStateSeconds(State st)
        {
            switch (st)
            {
                case State.Attack: return Mathf.Clamp(_bean.AttackSeconds, 0.12f, 3f);
                case State.Hit: return 0.30f;      // 与 `PlayerBean.ApplyHit` 的 Len 同值
                case State.Swap: return 0.50f;     // 与 `PlayerBean.ApplySwap` 的 Len 同值
                default: return 0f;                // Idle / Walk 循环；Death 停在末帧
            }
        }

        /// <summary>按游戏时钟推进动画并采样。dt 用固定步长，保证确定性。</summary>
        public void Tick(float dt)
        {
            // ★ 程序化豆豆人：姿态分支必须在 `!_hasAnim` 那条**之前** ★
            // 豆豆人没有片段（`_hasAnim == false`），放在后面会被那条直接 return 掉 ⇒ 一动不动 ✓
            if (_bean != null) { TickBean(dt); return; }

            if (!_hasAnim) return;
            // v181：`Δ片段=` 只在"这一帧真的从片段取了值"时有意义 ⇒ 每帧先清掉参照 ✓
            // 不清的话，攻击结束后的第一帧探针还会拿旧参照去比 ⇒ 读成"贴住了"
            _clipPoseValid = false;
            // v172：Walk 吃**移速**倍率（修"移速变了、脚步频率没变"的滑步）
            // v174：Attack 吃**攻速**倍率（修"三把攻速不同、攻击动作却一样长"）
            //   ⇒ 两条都只改**推进速度** ✗ 不改"选哪一段" ✓
            //      ⇒ 与 `WorldView.SyncPlayer` 那条"攻击/切换期间不跑 Walk/Idle 迁移"的
            //        让位守卫**互不冲突**：守卫管选段、这里管速度 ✓
            // 其余状态（Hit / Death / Swap）恒按原速推进 —— 那几段的前摇要看得清 ✓
            float rate = _state == State.Attack ? _attackSpeed
                       : _state == State.Walk ? _playbackSpeed : 1f;
            // v181：验收用的攻击状态保持 —— 只停攻击那条时间轴（见 `DebugHoldAttack`）
            bool holdAttack = DebugHoldAttack && _state == State.Attack;
            if (!holdAttack) _stateTime += dt * rate;

            // ══════════════════════════════════════════════════════════════════
            // ★ v182：**腿相位累加器 `_locoTime`：任何状态都推进，任何状态都不重置** ★
            //
            // 它把"腿播到哪一帧"从**状态时间轴**里彻底拿了出来 ⇒
            // 出手 / 收招 / 连打都不会让它归零，腿因此不会在边界上被抽回去 ✓
            //（`EnterState` 里那行 `_locoTime = _stateTime` 就是被这一条取代的，
            //  推导与实测数字写在 `EnterState` 的注释里：边界 29.58°/帧 → 见 `AnimJerk`）
            //
            // 速率与"这条腿正在播的那条时间轴"一致：
            //   Walk / Attack 吃走路倍率（`_playbackSpeed`，攻击期间腿**不跟攻速**）
            //   Idle / Hit / Death / Swap 按 1 倍
            // ★ 只对**分层模型**（玩家）生效 ★ —— 敌人/植物/小猪 `_layered == false`，
            //   它们仍然整段吃 `_stateTime`，行为与从前逐字相同 ✓
            // ══════════════════════════════════════════════════════════════════
            if (_layered)
                _locoTime += dt * (_state == State.Walk || _state == State.Attack ? _playbackSpeed : 1f);

            // ══════════════════════════════════════════════════════════════════
            // ★ v182：本帧的"攻击身份"必须在**任何状态跃迁之前**定下来 ★
            //
            // 坑（实测，`AnimJerk` 连打模式）：收招那一帧的顺序是
            //   ① `t` 取完 ⇒ ② `_stateTime >= len` ⇒ `Play(Idle)` 把 `_state`/`_stateTime` 归零
            //   ③ 到这里再判 `_state == Attack` ⇒ **已经是 false** ✗
            // ⇒ 这一帧掉进"整段采样"分支，把**攻击片段的腿**（作者写死成站立）盖到下半身上 ✗
            // ⇒ 一帧之内腿跳 **29.58°**，下一帧又回到走路 ⇒ 每挥一次抽一下 ✓
            // 而"下半身对攻击片段零贡献"正是分层设计的第一条 ⇒ 这一帧把分层整个绕过去了 ✗
            // 修法：身份与相位**先存下来**，后面无论怎么跃迁都按"本帧是攻击"处理后两条 ✓
            // ══════════════════════════════════════════════════════════════════
            bool wasAttack = _state == State.Attack;
            float attackAge = _stateTime;          // 跃迁会把它归零，先抄一份

            AnimationClip clip = ClipFor(_state);
            if (clip == null)
            {
                // ★ 攻击缺片段是**真缺陷**，不是正常回退 ★
                // 旧代码在这里无声地 `Play(Idle)`，于是"挥锄播的是待机"查了很久才定位到
                // "玩家要 player_attack、而资产叫 player_attack_hoe"✓
                // 报**一次**就够（每帧报会把控制台刷满，反而看不见别的错）✓
                if (_state == State.Attack && !_warnedMissingAttack)
                {
                    _warnedMissingAttack = true;
                    Debug.LogWarning($"[Seedfall] {Go?.name} 取不到攻击片段"
                                     + $"（武器索引 {_attackWeapon}，覆盖表 {DescribeAttackClips()}）⇒ 回退待机");
                }
                Play(State.Idle);
                return;
            }

            float len = clip.length;
            float t;
            if (_state == State.Idle || _state == State.Walk)
            {
                // ★ v182：**分层模型的腿走 `_locoTime`**，不再走 `_stateTime` ★
                // `_stateTime` 每次换状态都归零 ⇒ 用它当腿相位，出手一结束腿就跳回第 0 帧
                //（实测边界 29.58°/帧，见 `EnterState` 的注释）。`_locoTime` 只增不减 ⇒ 连续 ✓
                // 顺带把它折回 `[0, len)`：既避免长跑之后浮点精度变粗，又不会引入"取模跳变"——
                // 折回用的就是**这一帧真的会采样的那条片段**的长度 ✓
                if (_layered && len > 0.0001f) _locoTime = Mathf.Repeat(_locoTime, len);
                t = len > 0.0001f
                    ? (_layered ? _locoTime : Mathf.Repeat(_stateTime, len))
                    : 0f;
            }
            else if (_state == State.Death)
            {
                t = Mathf.Min(_stateTime, len);          // 停住最后一帧
            }
            else
            {
                t = Mathf.Min(_stateTime, len);
                // v181：`holdAttack` 期间**不许**自动回待机（否则读数/截图会落在 Idle 上）
                if (_stateTime >= len && !holdAttack) Play(State.Idle);  // 一次性动作播完回待机
            }

            // ══════════════════════════════════════════════════════════════════
            // ★ v181：攻击 = **上半身吃片段 / 下半身走 locomotion / 腰做线性混合** ★
            //
            // ── 它推翻了 v180 的第三条（"躯干写回站立基准帧"）✗ ──
            // v180 把攻击拆成"下半身←locomotion｜躯干←冻结｜右手←程序化"，
            // 理由是模型**刚性骨骼父子绑定、不是蒙皮**（`Tools/AssetPipeline/manifest.json`）：
            // 髋部网格挂在 `Bone_Hips` 上、不随 `Bone_Spine` 变形
            // ⇒ 上半身折 15~27° 时腰处会开出楔形缝 ✗
            //
            // ★ 用户拍板 ★：**攻击中玩家可以走 / 脚动画正常 / 上半身替换成攻击动画**
            // ⇒ 不再讨论"要不要上半身动"，只讨论"怎么把腰那一下做到最小" ✓
            //
            // ── 现在的取法（三条来源，各自负责一段骨架）──
            //   ① 下半身 7 根（Hips / Thigh / LowerLeg / Foot ×2）← **locomotion**
            //      攻击片段对它们的贡献 **= 0**（不是"事后压回 0"）✓
            //   ② 上半身 12 根（Spine / Chest / Neck / Head / Shoulder /
            //      UpperArm / LowerArm / Hand ×2）← **攻击片段**
            //   ③ 腰：`Bone_Spine` 在"locomotion 的脊柱"与"片段的脊柱"之间做一次
            //      **线性混合**，权重 `WaistKeepAtSpine`
            //      ★ v191-b：权重已从 0.5 改成 **0** ⇒ 脊柱完全不吃攻击片段（"不要扭腰"）★
            //        详见 `WaistKeepAtSpine` 上方的推导；下面的 0.5 推理只作为历史保留 ✓
            //
            // ── ③ 为什么是"衰减脊柱"，而不是"给髋补反向旋转"（两条路我都评估过）✗ ──
            //   · 反向补髋 ✗：★ `Bone_Hips` 是**两条大腿的共同父节点** ★
            //     转它 = 整条腿跟着摆 ⇒ ①"下半身只认 locomotion"当场破功
            //     ⇒ 脚被髋带着扫过地面 ⇒ **滑步回来**（正是本任务要消灭的现象）
            //     要么再写一次腿的世界旋转去抵消它 —— 那就是拿两套解算互相打架 ✗
            //   · 衰减脊柱 ✓：断层就发生在 `Hips↔Spine` **这一个关节**上
            //     （髋网格在 Hips 上、躯干网格在 Spine 上，缝的夹角 = Spine 的局部旋转）
            //     ⇒ 只动它，缝就按比例变小；而且**一根腿骨都不碰** ✓
            //
            // ── 0.5 是怎么来的（不是我拍脑袋调的）──
            //   从髋到胸是一条两节的链：Hips(0) → Spine(?) → Chest(1)
            //   线性混合 = 在这条链上从 0 走到 1 ⇒ 第一个关节**正好是中点 0.5** ✓
            //   ⇒ Spine 吃一半折叠、Chest 及其以上**一点不衰减** ✓
            //   ⇒ 剪影（挥击弧线、头朝哪、肩的起点）仍然完全由片段决定，
            //     只是"腰弯多少"这一项减半 ⇒ 缝也减半 ✓
            //   实测（锄头 t=0.875s 峰值，见下）：Spine 15.13° → 7.57°，Chest 20.76° 原样 ✓
            //
            // ── 还剩下什么（如实写）──
            //   缝**没有消失**，只是从 15.13° 降到 7.57°（壶 0.95°→0.48°、肥 18.76°→9.38°）✓
            //   要真正消除，得让髋部网格跟着脊柱变形 ⇒ 那是**换成蒙皮模型**，不是这一层能修的 ✓
            // ══════════════════════════════════════════════════════════════════
            // 片段长度仍然有用 —— 它算攻速倍率（`SetAttackDuration`）✓
            // ★ v182：判据用 `wasAttack`（跃迁之前抄下的）✗ 不能用 `_state` ——
            //   收招那一帧 `_state` 已经被 `Play(Idle)` 改掉了（见上面那段推导）
            bool layeredAttack = wasAttack && _layered;
            if (layeredAttack)
            {
                GameObject root = _animRoot != null ? _animRoot : Go;
                float u = len > 0.0001f ? Mathf.Clamp01(attackAge / len) : 0f;
                if (DebugAttackPoseAt >= 0f) u = Mathf.Clamp01(DebugAttackPoseAt);   // 验收冻结

                // ★ 首次用某一把武器打时，先问一句"这段片段的上半身到底有没有曲线" ★
                // （判据见 `ProbeUpperCurves`：没有就整段退回旧路径，**绝不半套**）
                if (_upperProbeWeapon != _attackWeapon)
                {
                    _upperProbeCalls++;                  // v182：抽搐探针要数"到底判了几次"
                    _upperOk = ProbeUpperCurves(clip);
                    _upperProbeWeapon = _attackWeapon;
                }

                // ① 下半身与髋 ← **locomotion**（站立 Idle / 移动 Walk）
                //    这一遍同时把整副骨架重置到"该走的样子"，是②③的底座 ✓
                // ★ v182：推进**不在这里**做 —— `_locoTime` 在 `Tick` 顶上统一推进
                //   （推进位置与"哪些状态会推进"必须是同一件事，否则又是两个时间源 ✗）
                AnimationClip loco = _locoMoving ? ClipFor(State.Walk) : ClipFor(State.Idle);
                if (loco != null && loco.length > 0.0001f)
                {
                    _locoTime = Mathf.Repeat(_locoTime, loco.length);   // 折回本片段（同一条腿，连续性不破）
                    loco.SampleAnimation(root, _locoTime);
                }
                else
                {
                    clip.SampleAnimation(root, 0f);
                }
                CaptureLocomotionPose();                        // 全部位置 + 全部旋转的快照

                if (_upperOk)
                {
                    // ② 上半身 ← **攻击片段**（Spine/Chest/Neck/Head/Shoulder/双臂/双手 共 12 根）
                    //    采样时刻用 `u × 片段长` ✗ 不能用 `_stateTime` ——
                    //    验收冻结时 `_stateTime` 被钉在 0（见 `DebugHoldAttack`），用它就永远在第 0 帧 ✓
                    clip.SampleAnimation(root, Mathf.Clamp(u * len, 0f, len));
                    CaptureClipUpperPose();                     // 探针的"片段原样"参照
                    BlendWaist();                               // ③ 腰：Spine 在髋与片段之间线性混合
                    RestoreLowerPose();                         // ④ 下半身写回 locomotion（含位置）
                }
                else
                {
                    // 旧路径（片段没有上半身曲线时的兜底，= v180 的行为）：
                    // 躯干/肩写回站立基准 + 右手链程序化姿势
                    RestoreTorso();
                    ApplyArmPose(_attackWeapon, u);
                }

                // ★ v183：低通放在**两条路径之后**、`CaptureJerkFrame` 之前 ★
                // 位置有讲究：它必须作用在本帧**已定稿**的上半身姿态上（两条路径都一样），
                // 又必须是抽搐探针读到的那一份（探针在本方法返回后、`Tick` 末行采样）✓
                SmoothAttackPose(dt);
            }
            else
            {
                // 非攻击：照旧整段采样（敌人 / 植物 / 小猪 / 玩家的 Idle·Walk·Hit·Swap·Death）
                clip.SampleAnimation(_animRoot != null ? _animRoot : Go, t);
            }

            // ★ v169：**把根节点的旋转也写回** ✗ —— 与 v151 同一个原则 ★
            //
            // ── 事故记录（用户报的"模型存在倒下的 bug"）──
            // 根因：玩家的那个 −90°（Rx270）被应用了【两次】✗
            //   · fbx 根对象自带 Rx(270)  ⇒ `Spawn` 把它搬到【外层】MDL_player ✓
            //   · 而 `player_*.fbx` 的 7 条动画片段【根曲线（path 为空）】也带常量 Rx(270) ✗
            //     ⇒ `SampleAnimation` 打在内层 ANIM_player 上 ⇒ 内层也转了 270 ✓
            //   ⇒ 外层 × 内层 = Rx(180) ⇒ 【翻倒】✗
            //   而其余 37 条片段（imp / brute / pig / 16 植物 / 商店车）**没有根曲线** ✓
            //     ⇒ 内层恒为 identity ⇒ 只用一次 ⇒ 正确 ✓
            //   ★ 这正好解释"为什么只有一部分模型有问题" ★
            //
            // ★ 实测证据（旋转审计扫了全部 132 个 fbx）★：
            //   `GlobalSettings.UpAxis = 1` ⇒ **文件声明 Y-up** ✗ 不是 Z-up ✓
            //   ⇒ 那个 270 **不是导入器加的轴转换**，而是 Blender 导出时【留在根对象上】的旋转 ✓
            //   ⇒ 它与 v151 修掉的那个根**缩放**轨道是【同一次重导】带进来的 ✓
            //     —— 缩放那条修了，旋转这条当时没发现 ✓
            //   ⚠️ 而工程里 `ModelView.cs:120-123` 与 `ArtImportSettings.cs:11` 的注释
            //      把因果说反了（写的是"Z-up 靠它转成 Y-up"）✗
            //      **结论"必需"是对的，理由不是** ✓ 上面这段是更正后的理由 ✓
            //
            // ★ 为什么可以【无条件】重置 ★：
            //   内层的职责是"被动画驱动"✗ 而"根的整体姿态"是【外层】的职责 ✓
            //   ⇒ 内层的**根旋转**本来就不该存在 ✓ 与 v151 对缩放的处理完全同构 ✓
            //
            // ⚠️ 只清 x/z、**保留 y** ✗ —— yaw 是 `SetFacing` 的职责 ✓
            //    而腿骨与上半身的旋转【不受影响】✓ 因为它们不是根 ✓
            //    （两段采样依赖腿部曲线为 0 ✓ 而这条改动不碰任何非根节点 ✓）
            //
            // ⚠️ 只在"确实有偏差"时才写 ✗ —— 避免每帧都往 Transform 上写值 ✓
            if (_animRoot != null)
            {
                Vector3 e = _animRoot.transform.localEulerAngles;
                if (Mathf.Abs(Mathf.DeltaAngle(0f, e.x)) > 0.01f
                    || Mathf.Abs(Mathf.DeltaAngle(0f, e.z)) > 0.01f)
                    _animRoot.transform.localEulerAngles = new Vector3(0f, e.y, 0f);
            }

            // ★ v151：**把根节点的缩放写回，不让动画片段决定模型大小** ★
            //
            // ── 事故记录（画面被巨大模型完全挡住）──
            // 实测高度排行：
            //   ANIM_player     高=7825.21m   lossyScale=16302.0     ← 应为 3.0m / 1.63
            //   ANIM_tool_hoe   高=2108833.00m lossyScale=1630202.0
            // 而 `SetScaleFactor(2.6 × 0.627) = 1.63` 这个公式**是对的** ✓
            // 多出来的 10000 倍来自 **动画片段里的根节点缩放轨道** ✗
            //
            // 本工程用 `AnimationClip.SampleAnimation` **手驱动画**（不挂 Animator）✓
            // 它是按**绑定路径直接写 Transform** 的 ✗
            // 于是片段里那条缩放曲线每一帧都把 100 倍写回根节点，
            // **覆盖掉 SetScaleFactor 设的值** ✗✓
            //
            // 为什么旧模型没事、换新模型才爆：
            // 旧 `player_idle.fbx` 没带那条轨道 ✗ 而 subagent 用 Blender 5.2 重导的新片段带上了 ✓
            // ⇒ **这不是模型的问题、也不是接线的问题，而是"手驱动画 + 片段带根缩放"的组合问题** ✗
            //
            // ★ 原则：**缩放是"摆位"的职责，不是"动画"的职责** ★
            // 这与本类"外层摆位 / 内层动画"的两层设计是同一个原则 ✓
            // 而片段里的根缩放破坏了它 —— 所以在这里恢复，是修复而不是补丁 ✓
            //
            // 注意只重置**缩放**，不碰位置/旋转：
            // 位置与旋转由两层结构已经隔离好了（内层怎么写都不影响外层摆位）✓
            // 而缩放没有隔离层 —— 它会直接乘进世界尺寸 ✗
            if (_animRoot != null) _animRoot.transform.localScale = Vector3.one;

            // ★ v181：脚滑采样放在**最后** ✗ —— 它必须读到本帧最终写进骨架的姿势 ✓
            // 放在采样之前会读到上一帧的脚位置 ⇒ 整体错一帧 ⇒ 比值偏小（看起来"更不滑"）
            // 即"量到的比实际的更好"✗ 这是最容易悄悄出现的一类偏差 ✓
            if (_slideRemaining > 0) StepFootSlide();

            // ★ v182：抽搐采样也在**最后**（同一理由：读本帧最终姿态）★
            if (_jerkRemaining > 0) CaptureJerkFrame();
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v180：攻击的**上下半身分层** —— 修"卡脚"★
        //
        // ── 卡脚到底是什么（实测，不是推断）──
        // `Tools/mesh_gen/anim_curve_audit.py` 读的是 **Unity 采样时真的会写的那份片段**
        //（`Assets/Resources/Art/AnimMerged/player_attack_*.asset`），逐节点量出来：
        //
        //   节点              锄头       水壶       肥料      通道
        //   Bone_Spine       15.13°     0.95°     18.76°    rotation
        //   Bone_Chest       20.76°     5.63°     26.74°    rotation
        //   Bone_Neck         5.20°     0.00°      2.08°    rotation
        //   Bone_Head        13.41°     1.89°     13.66°    rotation
        //   Bone_UpperArm_R  70.33°    28.35°    121.56°    rotation
        //   Bone_LowerArm_R  60.98°    46.00°     13.79°    rotation
        //   Bone_Hips         0.05°     0.05°      0.05°    ← **常量**
        //   Bone_Thigh_L/R    0.00°     0.00°      0.00°    ← **常量**
        //   Bone_LowerLeg_L/R 0.00°     0.00°      0.00°    ← **常量**
        //   Bone_Foot_L/R     0.03°     0.03°      0.03°    ← **常量**
        //   所有 Position 通道  0.0000 m（三把武器全部）
        //
        // ⇒ ① **不是位移**："卡脚"里没有任何一根骨骼发生了平移，全部是**旋转**✓
        //   ② 片子的结构是"**上半身大幅折叠 + 下半身写死成 0**"——
        //      躯干在转 15°~27°，髋与整条腿是常量 ✗
        //   ③ 而模型是**刚性骨骼父子绑定、不是蒙皮**（`Tools/AssetPipeline/manifest.json`）：
        //      髋部网格挂在 `Bone_Hips` 上、不会随 `Bone_Spine` 变形
        //      ⇒ 腰处断层 + "上半身在动、下半身像被钉在地里" = 用户说的**卡脚**✓
        //   ④ 边跑边打时更明显：脚步动画被那几条 0 曲线**强行压回站立** ⇒ 脚在地上滑 ✓
        //
        // ── 所以修法不是"把腿的曲线调大"，而是**别让攻击片段碰下半身** ──
        //   ① 下半身 ← **locomotion**（站立取 Idle、移动取 Walk，见 `_locoMoving`）
        //      攻击对它的贡献 **= 0**，而不是"事后压回 0"✓
        //   ② 躯干 / 肩 ← **站立基准帧** `Idle@t=0`（`RestoreTorso`）—— 不吃片段的折叠
        //   ③ 右手链 ← 三把武器各自的**程序化姿势**（`ApplyArmPose`）
        // ⇒ 攻击片段的曲线从此**一条都不参与采样**，它只剩"长度"这一个用途
        //   （算攻速倍率，见 `SetAttackDuration`）✓
        // ⇒ 可验证：攻击期间脚的局部旋转与基准的夹角、以及脚的世界坐标位移，
        //   由 `DescribeLayeredPose()` 直接报出来（探针走 `WorldView.ProbePlayerAnim`）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>躯干与肩：攻击期间**冻结在站立基准帧**的节点。</summary>
        private static readonly string[] FrozenBones =
        {
            "Bone_Spine", "Bone_Chest", "Bone_Neck", "Bone_Head",
            "Bone_Shoulder_L", "Bone_Shoulder_R",
        };

        /// <summary>下半身：攻击期间由 locomotion 驱动，用于**报告**（不写它，只量它）。</summary>
        private static readonly string[] LowerBones =
        {
            "Bone_Hips", "Bone_Thigh_L", "Bone_Thigh_R",
            "Bone_LowerLeg_L", "Bone_LowerLeg_R", "Bone_Foot_L", "Bone_Foot_R",
        };

        /// <summary>
        /// ★ v181：数组下标 → 骨骼名的**具名常量** ✗ —— 探针要逐根报数，不能写 `_lowerNodes[5]` ★
        ///
        /// 为什么不是 `_footLNode` 这类独立字段：那会与 `LowerBones` / `FrozenBones`
        /// 变成**两份真值**（本工程已经踩过"两个时间源"）。下标放到常量里，
        /// 表就是唯一真值，改名/换序时编译器不会静默指错 ✓
        /// ⚠️ 这两组常量与上面两个 `static readonly string[]` 的**顺序绑定** ✗ 改动其一必须同时改另一个 ✓
        /// </summary>
        private const int FrozenSpine = 0, FrozenChest = 1, FrozenNeck = 2, FrozenHead = 3;

        private const int LowerHips = 0, LowerThighL = 1, LowerThighR = 2,
                          LowerShinL = 3, LowerShinR = 4, LowerFootL = 5, LowerFootR = 6;

        /// <summary>
        /// ★ v181：**上半身 12 根** —— 攻击片段只负责这一组 ★
        ///
        /// 清单来自任务书（逐字）：
        ///   `Bone_Spine` / `Bone_Chest` / `Bone_Neck` / `Bone_Head` /
        ///   `Bone_Shoulder_L·R` / `Bone_UpperArm_L·R` / `Bone_LowerArm_L·R` / `Bone_Hand_L·R`
        /// 与 `Tools/mesh_gen/anim_curve_audit.py` 的 `UPPER_BODY` 分组同源 ✓
        ///
        /// ⚠️ `Bone_Shoulder_L/R` 在旧实现里是"冻结"组的一员 ✗ 现在归上半身 ⇒ 吃片段 ✓
        ///    任务书把肩列进上半身，理由也对：肩是**手臂发力的起点**，
        ///    只动上臂而不动肩，挥击的"根"就断了（剪影读不出力从哪来）✓
        /// </summary>
        private static readonly string[] UpperPoseBones =
        {
            "Bone_Spine", "Bone_Chest", "Bone_Neck", "Bone_Head",
            "Bone_Shoulder_L", "Bone_Shoulder_R",
            "Bone_UpperArm_L", "Bone_UpperArm_R",
            "Bone_LowerArm_L", "Bone_LowerArm_R",
            "Bone_Hand_L", "Bone_Hand_R",
        };

        private const int UpperBoneCount = 12;                       // = UpperPoseBones.Length
        private const int PoseBoneCount = UpperBoneCount + 7;        // 12 + LowerBones.Length

        private Quaternion[] _poseTmpRot;      // 采样 locomotion 之后的暂存（下半身要用它写回）
        private Vector3[] _poseTmpPos;         // 同上，位置
        private bool[] _poseRotFromClip;       // 哪些下标取片段的旋转（true = 上半身）
        private Quaternion[] _idleBaseRot;     // 站立基准 19 根（`ProbeUpperCurves` 的参照）
        private int _upperProbeWeapon = -1;    // 已经判定过的是哪一把（-1 = 还没判定）
        private bool _upperOk = true;          // 这一把的片段上半身有没有曲线

        private Transform[] _frozenNodes;
        private Quaternion[] _frozenBaseRot;
        private Vector3[] _frozenBasePos;
        private Transform[] _lowerNodes;

        private Transform _armUpper, _armLower, _armHand;
        private Quaternion _armUpperW0, _armLowerW0, _armHandW0;   // 基准世界旋转
        private Vector3 _armUpperD0, _armLowerD0, _armHandD0;      // 基准"朝子节点"的方向（世界系）
        private float _armSide = 1f;                               // 右手在角色本地系的哪一侧（±1）
        private Quaternion[] _lowerBaseRot;                        // 下半身基准（仅用于报告）

        /// <summary>分层是否可用（骨骼齐 + 片段齐）。不齐就整段退回旧行为，绝不半套。</summary>
        private bool _layered;

        /// <summary>攻击期间玩家在不在动 —— 决定下半身取 Walk 还是 Idle。</summary>
        private bool _locoMoving;
        private float _locoTime;

        /// <summary>
        /// ★ v183：攻击姿态**帧间低通**的上一帧输出（12 根上半身 `localRotation`）★
        ///
        /// 为什么需要它：片段的**关键帧太稀**，而相邻两帧的姿态之间只有 Hermite 插值 ⇒
        /// 60fps 采样时速度会在 2~3 帧内掉头（实测见 `SmoothAttackPose` 的说明）。
        /// 低通是**作用在时间轴上的**，所以必须记住上一帧的输出 —— 不能每帧从零算 ✓
        /// </summary>
        private Quaternion[] _smPoseRot;

        /// <summary>`_smPoseRot` 里有没有可用的上一帧（进攻击时清零 ⇒ 第一帧不吃延迟）。</summary>
        private bool _smPoseValid;

        // ══════════════════════════════════════════════════════════════════════
        // ★ v191：**箱式（有限冲激）平滑** —— 修"攻速一高就又开始抽" ★
        //
        // ── 为什么要换掉 v183 的一阶低通（实测，不是口味）──
        // v183 用 `q ← Slerp(q_prev, target, α)`，α = 1−e^(−dt/τ)（τ=60ms ⇒ α=0.24@60fps）
        // 它在**倍率 1.00** 下把 `Chest max|Δ²|` 从 1.63° 压到 0.69°、反向 1→0 ✓
        // 但倍率一上来就不行了（`AnimJerk` 的 `dur` 规格逐档实测，同一份代码）：
        //   倍率 1.00：1.63° → **0.69°**   反向 1 → **0**
        //   倍率 2.00：5.16° → **1.20°**   反向 3 → **1** ✗
        //   倍率 3.00：5.01° → **1.70°**   反向 3 → **1** ✗
        // ★ 而倍率 2 不是极端情形 ★：`AttackSeconds = tool.cooldown ÷ AttackSpeed`，
        //   锄头 `cooldown` 就是 0.70s ⇒ 开局倍率 = 1.458/0.70 = **2.08** ✓
        //   ⇒ **上线后玩家一直在 2 倍以上打**，v183 的调参点（1.00 倍）恰好是最不容易出事的那一档 ✗
        //
        // ── 为什么一阶低通在高速下必然掉链子（不是没调好）──
        // 片段那个拐角（`0.833→0.875`，Δu=0.0287）在真实时间里只有 0.042s÷倍率：
        //   倍率 1 ⇒ 跨 **2.5 帧**；倍率 2 ⇒ **1.25 帧**；倍率 3 ⇒ **0.83 帧（已不足一帧）** ✓
        // 而一阶低通对**一帧之内的一步**只能给出 `α×步长`：
        //   想让 `Δ²` 不随倍率涨，就得让 `α ∝ 1/倍率` ⇒ `τ ∝ 倍率` ⇒
        //   滞后 = `(1−α)/α ≈ 1/α` ⇒ 倍率 2 时 **≈7.7 帧（128ms）** ✗
        //   ⇒ **"Δ² 降下来"和"手感不变"在一阶低通上是互斥的**，不是参数没选好 ✓
        //   而且 v183 现在这条 α=0.24 的滞后已经是 `(1−0.24)/0.24 ≈ 3.2 帧`
        //   ⇒ ★ 它本来就违反了本轮的判据"起手 / 命中偏移 < 1 帧" ★ ✓
        //
        // ── 所以换成**箱式平滑**：把"这一帧之前若干帧的目标姿态"取加权平均 ──
        //   · 群延迟 = Σk·wₖ 帧，`[0.5,0.5]` ⇒ **0.5 帧**；`[0.25,0.5,0.25]` ⇒ **1.0 帧**
        //     ⇒ 比一阶低通的 3.2 帧**小一个量级** ✓（判据"< 1 帧"因此从"违反"变成"满足"）
        //   · 它在**奈奎斯特（周期 2 帧）处响应恰好为 0** ⇒ 对"一帧高、一帧低"这类
        //     混叠伪影的抑制比同宽的一阶低通强得多 ✓ 而"倍率 3 ⇒ 拐角 0.83 帧"正落在这一段 ✓
        //   · 代价：要记住前两帧的**目标**姿态（12 个四元数 ×2），不增加任何 `SampleAnimation` ✓
        //   · 位置一律不滤（与 v183 同一条理由：位置全由 locomotion 写回，滤它只会让脚多滑）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>平滑器选择。0=关 1=一阶低通(v183) 2=两帧箱式 3=三帧箱式 4=箱式→低通级联。</summary>
        public const int SmModeOff = 0, SmModeEma = 1, SmModeBox2 = 2, SmModeBox3 = 3, SmModeBox2Ema = 4;

        /// <summary>
        /// 默认平滑器。★ 一阶低通（v183 的思路，τ 由 60ms 提到 90ms —— v191）★
        ///
        /// 为什么最后还是选它（四个候选都在**同一份可执行文件**里量过，见 `SmoothAttackPose`）：
        /// 箱式滤波器（滞后小）**阻带太窄**，对本工程这条"峰值处的拐角"几乎无效：
        ///   倍率 2.08 时 `Chest max|Δ²|`：关 5.62° / 两帧箱式 3.44° / 三帧箱式 2.74° / **低通 1.20°** ✗
        /// 而 `箱式+低通` 级联只把 1.20° 压到 1.18°，却多出 0.5 帧滞后 ⇒ 不值 ✓
        /// ⇒ 真正起作用的是**低通的长尾**，所以保留低通，只把 τ 调大（数据见 `PoseLowPassTau`）✓
        ///
        /// ⚠️ 起手 / 峰值帧号**不随 τ 变化**（τ=60/90/120 三档都是同一个峰值帧）——
        /// 这是"手感不变"这条判据能同时成立的原因，不是巧合：
        /// 峰值落在 |Δ| 的极值处，低通在该处的**位置**不动，只削它的高度 ✓
        /// </summary>
        private const int PoseSmoothMode = SmModeEma;

        /// <summary>探针覆盖平滑器（−1 = 用 `PoseSmoothMode`）。只由 `AttackJerkReplay` 写。</summary>
        public static int DebugSmoothMode = -1;

        /// <summary>上一帧 / 上上帧的**目标**姿态（箱式的记忆；与 `_smPoseRot` 的输出分开存）。</summary>
        private Quaternion[] _smTarget1, _smTarget2;

        /// <summary>
        /// ★ v180：验收用的**相位冻结**（-1 = 关，正常游戏恒为 -1）★
        ///
        /// 为什么需要它：攻击动作只有 0.5~1.5 秒，而桥接往返一次就几百毫秒 ——
        /// 想拍"打到一半的姿势"和"引到身后的那一帧"，不冻结就永远落在随机相位上 ✗
        /// 与既有的 `WorldView.DebugFxHold` / `DebugSwingHold` 是同一类验收通道：
        /// **只在验收入口置位，不影响任何手感** ✓
        /// </summary>
        public static float DebugAttackPoseAt = -1f;

        /// <summary>
        /// ★ v181：验收用的**攻击状态保持**（默认 false，正常游戏恒为 false）★
        ///
        /// ── 为什么 `DebugAttackPoseAt` 还不够 ✗ ──
        /// 它只钉住"取片段哪一帧"，而 `_stateTime` 照样往前跑 ✗ ⇒ 攻击**照常结束** ✓
        /// 实测代价：本工程编辑器在后台时 `dt` 会很大（重场景 85 万三角形），
        /// 一次桥接往返 300ms 里攻击就能**播完** ⇒ 读到的 `state=Idle` ✗
        ///（第一次基线读数就是这样：`state=Idle t=1.473`，攻击早结束了）
        /// ⇒ 拍照与读数都落在随机时刻 ⇒ 两组截图**不可比** ✓
        ///
        /// 置 true 之后：攻击期间 `_stateTime` **不再前进**、也不会自动回 `Idle` ✓
        /// ⚠️ 只停**攻击那一条时间轴**：locomotion 的 `_locoTime` 照常推进 ⇒
        ///    "边走边打"的腿仍在走 —— 冻结不能把被测对象本身冻掉 ✓
        /// </summary>
        public static bool DebugHoldAttack;

        /// <summary>
        /// ★ v181 验收仪表①：**逐帧脚滑采样器** ★
        /// </summary>
        // ══════════════════════════════════════════════════════════════════════
        // ── 要回答的问题 ──
        // "边走边打的时候，脚还滑不滑"——这句话不能靠看截图判断，必须给数 ✓
        //
        // ── 为什么不能只比"脚的位移 vs 角色的位移"（任务书的原话）✗ ──
        // ★ 净位移这个口径**两边恒等**，回答不了问题 ✗★
        //   腿被冻住时，脚随身体刚性平移 ⇒ 脚净位移 = 角色净位移（比值 1）
        //   走路正常时，一个完整步态周期结束，脚又回到相对身体的同一相位 ⇒ 净位移**也**相等（比值 1）
        // ⇒ 用净位移判"滑不滑"，两种状态读数相同 ⇒ 判据失效 ✓
        //
        // ── 所以这里量三样东西（都可复算）──
        //   ① **路径长**：逐帧 |Δ位置| 累加。脚要被腿摆着动 ⇒ 脚路径 **>** 角色路径（比值 > 1）
        //      腿冻住时脚只随身体平移 ⇒ 比值 **≈ 1**（这是"卡脚"的指纹）✓
        //   ② **着地脚滑动比**：每帧取两只脚里位移**小的那只**（= 这一帧踩在地上的那只）✗
        //      再除以角色这一帧的位移 ⇒
        //         ≈ 0 = 踩地的那只脚在世界里**基本不动** = 不滑 ✓
        //         ≈ 1 = 踩地的脚**跟着身体一起滑** = 滑 ✓（腿冻住时的读数）
        //      这是"滑不滑"唯一有区分力的口径 ✓
        //   ③ 桶：只统计 **state=Attack 且这一帧真在动** 的帧（其余帧计入总览）——
        //      任务问的是"边走边打"，把所有帧混在一起会被站定帧稀释 ✓
        //
        // ⚠️ 单消费者假设：只有**分层**的模型（玩家）会推进它 ✓
        //   敌人/植物 `_layered == false` ⇒ 直接跳过，不会抢走这段采样 ✓
        // ══════════════════════════════════════════════════════════════════════

        private static int _slideRemaining;          // 静态布防：>0 = 还要采这么多帧
        private static string _slideTag = "";

        private int _slideFrames, _slideAtkFrames, _slideMoveFrames, _slideMixFrames;
        private float _slidePlayerPath, _slideFootLPath, _slideFootRPath;
        private float _slideMixPlayerPath, _slideMixFootLPath, _slideMixFootRPath, _slidePlantPath;
        private float _slideRatioSum;
        private int _slideRatioCount;
        private Vector3 _slidePrevPlayer, _slidePrevFootL, _slidePrevFootR;
        private Vector3 _slideMixFirstPlayer, _slideMixLastPlayer, _slideMixFirstFootL, _slideMixLastFootL;
        private bool _slideHasPrev, _slideHasMix;
        private string _slideMineTag;

        /// <summary>布防脚滑采样：采 `frames` 帧后打一行汇总到控制台。`tag` 只用于区分两组实验。</summary>
        public static void ArmFootSlideProbe(int frames, string tag)
        {
            _slideRemaining = Mathf.Clamp(frames, 1, 3000);
            _slideTag = string.IsNullOrEmpty(tag) ? "footslide" : tag;
        }

        /// <summary>采样中？供 `DebugApi` 判断"现在布防会不会被覆盖"。</summary>
        public static bool FootSlideArmed => _slideRemaining > 0;

        private void StepFootSlide()
        {
            if (!_layered || Go == null) return;                 // 只认玩家这条分层骨骼
            Transform fl = _lowerNodes != null ? _lowerNodes[LowerFootL] : null;
            Transform fr = _lowerNodes != null ? _lowerNodes[LowerFootR] : null;
            if (fl == null || fr == null) return;

            if (_slideMineTag == null) { _slideMineTag = _slideTag; _slideHasPrev = false; }

            Vector3 p = Go.transform.position;
            Vector3 l = fl.position, r = fr.position;
            Vector3 dp = p - _slidePrevPlayer;
            Vector3 dl = l - _slidePrevFootL;
            Vector3 dr = r - _slidePrevFootR;
            // 只认水平面：竖直方向的分量是走路起伏，与"滑步"无关，混进来会稀释比值
            float mp = new Vector2(dp.x, dp.z).magnitude;
            float ml = new Vector2(dl.x, dl.z).magnitude;
            float mr = new Vector2(dr.x, dr.z).magnitude;

            if (_slideHasPrev)
            {
                _slideFrames++;
                if (_state == State.Attack) _slideAtkFrames++;
                bool moving = mp > 0.0005f;
                if (moving) _slideMoveFrames++;
                _slidePlayerPath += mp;
                _slideFootLPath += ml;
                _slideFootRPath += mr;

                if (_state == State.Attack && moving)
                {
                    if (!_slideHasMix)
                    {
                        _slideMixFirstPlayer = _slidePrevPlayer;
                        _slideMixFirstFootL = _slidePrevFootL;
                        _slideHasMix = true;
                    }
                    _slideMixFrames++;
                    _slideMixPlayerPath += mp;
                    _slideMixFootLPath += ml;
                    _slideMixFootRPath += mr;
                    _slideMixLastPlayer = p;
                    _slideMixLastFootL = l;
                    // 着地脚 = 这一帧位移小的那只（踩在地上不动的那只）
                    _slidePlantPath += Mathf.Min(ml, mr);
                    _slideRatioSum += Mathf.Min(ml, mr) / mp;
                    _slideRatioCount++;
                }
            }

            _slidePrevPlayer = p;
            _slidePrevFootL = l;
            _slidePrevFootR = r;
            _slideHasPrev = true;

            _slideRemaining--;
            if (_slideRemaining > 0) return;

            float ratio = _slideRatioCount > 0 ? _slideRatioSum / _slideRatioCount : -1f;
            float footNet = _slideHasMix ? new Vector2(_slideMixLastFootL.x - _slideMixFirstFootL.x,
                                                      _slideMixLastFootL.z - _slideMixFirstFootL.z).magnitude : 0f;
            float bodyNet = _slideHasMix ? new Vector2(_slideMixLastPlayer.x - _slideMixFirstPlayer.x,
                                                      _slideMixLastPlayer.z - _slideMixFirstPlayer.z).magnitude : 0f;
            Debug.Log($"[Seedfall] FOOTSLIDE {_slideMineTag}"
                      + $" | 窗口帧={_slideFrames} 攻击帧={_slideAtkFrames} 移动帧={_slideMoveFrames}"
                      + $" | 攻击+移动帧={_slideMixFrames}"
                      + $" 角色路径={_slideMixPlayerPath:0.000}m"
                      + $" 脚L路径={_slideMixFootLPath:0.000}m 脚R路径={_slideMixFootRPath:0.000}m"
                      + $" 脚路径比={(bodyNet > 0f ? (_slideMixFootLPath + _slideMixFootRPath) * 0.5f / Mathf.Max(0.0001f, _slideMixPlayerPath) : 0f):0.000}"
                      + $" | 着地脚滑动={_slidePlantPath:0.000}m"
                      + $" 滑动比={ratio:0.000}(0=不滑,1=腿冻住)"
                      + $" | 净位移 脚L={footNet:0.000}m 角色={bodyNet:0.000}m"
                      + $" | 全窗口 角色={_slidePlayerPath:0.000}m 脚L={_slideFootLPath:0.000}m 脚R={_slideFootRPath:0.000}m");

            // 复位，下一轮从零开始（不 reset 会让第二次布防接着上一轮的数累加）
            _slideFrames = _slideAtkFrames = _slideMoveFrames = _slideMixFrames = 0;
            _slidePlayerPath = _slideFootLPath = _slideFootRPath = 0f;
            _slideMixPlayerPath = _slideMixFootLPath = _slideMixFootRPath = _slidePlantPath = 0f;
            _slideRatioSum = 0f;
            _slideRatioCount = 0;
            _slideHasPrev = _slideHasMix = false;
            _slideMineTag = null;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v182 验收仪表③：**抽搐**（一阶 / 二阶差分）采样器 ★
        //
        // ── 要回答的问题 ──
        // 用户原话："角色攻击动画异常卡顿抽搐"。这句话里其实是**两个不同的**现象，
        // 判据也必须是两个：
        //   · 卡顿 = 帧率低 / 采样跳步 ⇒ 一阶差分 Δ 忽大忽小，但**同号**（一直往一个方向转）
        //   · 抽搐 = 姿态**来回摆**   ⇒ Δ **换号**，二阶差分 Δ² 出现尖峰
        // ⇒ 所以判"抽搐"要看的是 **Δ²**（和它的符号翻转次数），不是 Δ 的大小 ✓
        //   本工程编辑器在后台时只有 9fps（每帧 111ms）⇒ 单看 Δ 会把"编辑器慢"
        //   误判成"动画坏" ✗ 而 Δ² 对**匀速**的慢采样不敏感 ✓
        //
        // ── 为什么采样点必须在 `Tick` 的最后一行 ──
        // 一帧里骨架被写了**三次**：locomotion → 片段 → 腰混合 / 下半身写回 ✗
        // 在中间任何一处读数，量到的都是**中间态**，不是玩家真的看见的那一帧 ✓
        // 所以它挂在 `StepFootSlide` 旁边（同一位置、同一理由）✓
        //
        // ── 为什么还要一个**离屏定步长重放**（`AttackJerkReplay`）──
        // 逐帧实测的 dt 由编辑器给，9~60fps 乱跳 ⇒ 改前 / 改后**帧数与相位都不同** ⇒ 不可比 ✗
        // 定步长重放把 dt 钉成 1/60、把攻击从 `PlayForced` 起固定跑 N 帧
        // ⇒ 两次实验落在**同一组相位**上，读数才可比 ✓
        // 它跑在**临时模型**上，不碰场上任何实例 ⇒ 只读 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>`Bone_Chest` 在 `UpperPoseBones` 里的下标（= 1）。**与那张表位置绑定**，改表必改它。</summary>
        private const int UpperChestIndex = 1;

        /// <summary>采样窗口上限（帧）。攻击最长约 1.5s × 60fps = 90 帧，600 留足余量。</summary>
        private const int JerkMaxFrames = 600;

        /// <summary>逐帧盯的**四根**骨头：上两（脊柱 / 胸）+ 下两（左右大腿）。</summary>
        private static readonly int[] JerkPoseIndex =
        {
            UpperSpineIndex, UpperChestIndex,
            UpperBoneCount + LowerThighL, UpperBoneCount + LowerThighR,
        };

        /// <summary>与 `JerkPoseIndex` **同序同长**的名字（报告里要能指名道姓）。</summary>
        private static readonly string[] JerkNames = { "Spine", "Chest", "Thigh_L", "Thigh_R" };

        private static int _jerkRemaining;
        private static int _jerkCount;
        private static string _jerkTag = "";
        private static string _jerkReport = "";
        private static int _jerkProbeCallsAtArm;
        private static Vector3[] _jerkEuler;     // [bone * JerkMaxFrames + frame]
        private static float[] _jerkStateTime;
        private static float[] _jerkLoco;
        private static float[] _jerkU;
        private static bool[] _jerkUpperOk;
        private static bool[] _jerkMoving;
        private static int[] _jerkState;

        /// <summary>
        /// ★ v191：报告里要不要附**逐帧表**（欧拉 / Δ / Δ² 一行一帧）。由 `AttackJerkReplay` 的
        /// `table` 片段置位，默认关 —— 摘要那一行已经够日常用，逐帧表只在定位"哪一帧"时才要 ✓
        /// </summary>
        private static bool _jerkTable;

        /// <summary>`ProbeUpperCurves` 被调用的**总次数**（诊断："是不是每帧重判"）。</summary>
        private static int _upperProbeCalls;

        /// <summary>布防逐帧抽搐采样：采 `frames` 帧后打一行 `JERK …` 到控制台。`tag` 只用于区分两组实验。</summary>
        public static void ArmJerkProbe(int frames, string tag)
        {
            _jerkTag = string.IsNullOrEmpty(tag) ? "jerk" : tag;
            _jerkCount = 0;
            _jerkRemaining = Mathf.Clamp(frames, 4, JerkMaxFrames);
            _jerkProbeCallsAtArm = _upperProbeCalls;
            _jerkReport = "";
            if (_jerkEuler == null || _jerkEuler.Length < JerkMaxFrames * JerkPoseIndex.Length)
            {
                _jerkEuler = new Vector3[JerkMaxFrames * JerkPoseIndex.Length];
                _jerkStateTime = new float[JerkMaxFrames];
                _jerkLoco = new float[JerkMaxFrames];
                _jerkU = new float[JerkMaxFrames];
                _jerkUpperOk = new bool[JerkMaxFrames];
                _jerkMoving = new bool[JerkMaxFrames];
                _jerkState = new int[JerkMaxFrames];
            }
        }

        /// <summary>采样中？供 `DebugApi` 判断"现在布防会不会被覆盖"。</summary>
        public static bool JerkArmed => _jerkRemaining > 0;

        /// <summary>最近一次 `JERK …` 报告原文（`DebugApi` 用它把结果回给桥）。</summary>
        public static string LastJerkReport => _jerkReport;

        /// <summary>把本帧**最终**姿态抄进 trace（`Tick` 末行调用，每帧一次）。</summary>
        private void CaptureJerkFrame()
        {
            if (_jerkEuler == null) return;
            int i = _jerkCount;
            if (i >= JerkMaxFrames) { _jerkRemaining = 0; return; }
            for (int b = 0; b < JerkPoseIndex.Length; b++)
            {
                int idx = JerkPoseIndex[b];
                Transform t = _poseNodes != null && idx >= 0 && idx < _poseNodes.Length ? _poseNodes[idx] : null;
                _jerkEuler[b * JerkMaxFrames + i] = t != null ? t.localEulerAngles : Vector3.zero;
            }
            _jerkStateTime[i] = _stateTime;
            _jerkLoco[i] = _locoTime;
            _jerkU[i] = _stateTime;      // 与攻击相位成正比（len 恒定 ⇒ 单调性等价）
            _jerkUpperOk[i] = _upperOk;
            _jerkMoving[i] = _locoMoving;
            _jerkState[i] = (int)_state;
            _jerkCount = i + 1;
            _jerkRemaining--;
            if (_jerkRemaining <= 0) LogJerkReport(_jerkTag);
        }

        /// <summary>某个向量的**主轴**（绝对值最大的分量下标 0/1/2）。</summary>
        private static int DominantAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return 0;
            return ay >= az ? 1 : 2;
        }

        private static string AxisName(int axis) => axis == 0 ? "X" : axis == 1 ? "Y" : "Z";

        /// <summary>
        /// 把 trace 算成报告：每根骨的 `max|Δ|` / `max|Δ²|`（含帧号与轴）、
        /// 以及四条**假设的排他性计数**（重判次数 / 路径翻转 / 时间轴回退 / 腿源翻转）。
        /// </summary>
        private static void LogJerkReport(string tag)
        {
            int n = _jerkCount;
            if (_jerkEuler == null || n < 4)
            {
                _jerkReport = "JERK " + tag + " 采样不足（" + n + " 帧）";
                Debug.Log("[Seedfall] " + _jerkReport);
                return;
            }

            // 攻击帧窗口（二阶差分**不能**跨状态边界算：边界的 Δ 是状态切换，不是抽搐）
            int atkStart = -1, atkEnd = -1;
            for (int i = 0; i < n; i++)
                if (_jerkState[i] == (int)State.Attack) { if (atkStart < 0) atkStart = i; atkEnd = i; }

            var sb = new System.Text.StringBuilder();
            sb.Append("[Seedfall] JERK ").Append(tag)
              .Append(" | 窗口=").Append(n).Append("帧")
              .Append(" 攻击帧=").Append(atkStart < 0 ? 0 : atkEnd - atkStart + 1)
              .Append(" | ProbeUpperCurves 重判=").Append(_upperProbeCalls - _jerkProbeCallsAtArm).Append("次");

            if (atkStart < 0)
            {
                sb.Append("\n    ⚠ 窗口里**没有**攻击帧 ⇒ 这组数据不能用（攻击没起来 / 已经结束）");
                _jerkReport = sb.ToString();
                Debug.Log(_jerkReport);
                return;
            }

            // ── 攻击段：一次窗口里可能连打好几下 ⇒ **每段都算，报最差的那段** ──
            //（只报"第一个攻击段"会把"第二下才抽"的情况整个漏掉 ✗ 而这正是间歇性缺陷的形状）
            var runStart = new System.Collections.Generic.List<int>();
            var runEnd = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n; i++)
            {
                if (_jerkState[i] != (int)State.Attack) continue;
                if (i == 0 || _jerkState[i - 1] != (int)State.Attack) runStart.Add(i);
                if (i == n - 1 || _jerkState[i + 1] != (int)State.Attack) runEnd.Add(i);
            }

            int worstRun = 0;
            float worstScore = -1f;
            for (int r = 0; r < runStart.Count; r++)
            {
                float sc = Mathf.Max(AnalyzeBone(0, runStart[r], runEnd[r]).MaxD2,
                                     AnalyzeBone(1, runStart[r], runEnd[r]).MaxD2);
                if (sc > worstScore) { worstScore = sc; worstRun = r; }
            }
            int ws = runStart[worstRun], we = runEnd[worstRun];
            sb.Append(" 攻击段=").Append(runStart.Count).Append(" 报最差段#").Append(worstRun + 1)
              .Append("(f").Append(ws).Append("-f").Append(we).Append(')');

            int worstBone = 0;
            float worstD2 = -1f;
            int worstFrame = -1;
            for (int b = 0; b < JerkNames.Length; b++)
            {
                JerkStats st = AnalyzeBone(b, ws, we);
                sb.Append("\n    ").Append(JerkNames[b].PadRight(8))
                  .Append(" max|Δ|=").Append(st.MaxD.ToString("0.00")).Append("°@f").Append(st.MaxDFrame)
                  .Append("  max|Δ²|=").Append(st.MaxD2.ToString("0.00")).Append("°@f").Append(st.MaxD2Frame).Append(AxisName(st.MaxD2Axis))
                  .Append("  反向=").Append(st.Flips).Append("次")
                  .Append("  平均|Δ|=").Append((st.Pairs > 0 ? st.SumD / st.Pairs : 0f).ToString("0.00")).Append("°")
                  // ★ v191：**起手 / 峰值**帧号 —— 这就是"手感"的可测口径 ★
                  // 起手 = 第一次 |Δ| 达到本段最大值的 10% 那一帧（"动起来了"）
                  // 峰值 = |Δ| 最大的那一帧（挥击最快处 ⇒ 命中感的来源）
                  // 平滑器一定有延迟 ⇒ 这两个帧号会被往后推；判据是**偏移 < 1 帧** ✓
                  // 只有同倍率 / 同 dt / 同帧数的两次重放才能这样直接比帧号（段内相位对齐）✓
                  .Append("  起手f").Append(st.OnsetFrame).Append(" 峰值f").Append(st.MaxDFrame);
                if (st.MaxD2 > worstD2) { worstD2 = st.MaxD2; worstBone = b; worstFrame = st.MaxD2Frame; }
            }

            // 四条假设的排他性计数（全部逐帧比对，不靠读代码推断）
            int upperFlips = 0, moveFlips = 0, locoRollback = 0, stateRollback = 0;
            float worstLocoDrop = 0f;
            for (int i = ws + 1; i <= we; i++)
            {
                if (_jerkUpperOk[i] != _jerkUpperOk[i - 1]) upperFlips++;
                if (_jerkMoving[i] != _jerkMoving[i - 1]) moveFlips++;
                float drop = _jerkLoco[i - 1] - _jerkLoco[i];
                if (drop > 1e-5f) { locoRollback++; if (drop > worstLocoDrop) worstLocoDrop = drop; }
                if (_jerkStateTime[i] < _jerkStateTime[i - 1] - 1e-5f) stateRollback++;
            }

            sb.Append("\n    假设核对：upperOk 翻转=").Append(upperFlips).Append("次")
              .Append(" | locoMoving 翻转=").Append(moveFlips).Append("次")
              .Append(" | locoTime 回退=").Append(locoRollback).Append("次(最大 ")
              .Append(worstLocoDrop.ToString("0.000")).Append("s)")
              .Append(" | stateTime 回退=").Append(stateRollback).Append("次");

            // 尖峰前后 ±2 帧（证明"哪一帧、摆了多少"）
            int lo = Mathf.Max(ws, worstFrame - 2), hi = Mathf.Min(we, worstFrame + 2);
            sb.Append("\n    尖峰窗口 ").Append(JerkNames[worstBone]).Append("：");
            Vector3 pe = _jerkEuler[worstBone * JerkMaxFrames + Mathf.Max(ws, worstFrame - 3)];
            for (int i = lo; i <= hi; i++)
            {
                Vector3 e = _jerkEuler[worstBone * JerkMaxFrames + i];
                Vector3 d = DeltaOf(pe, e);
                sb.Append(" f").Append(i).Append("Δ=").Append(d.x.ToString("0.0")).Append('/')
                  .Append(d.y.ToString("0.0")).Append('/').Append(d.z.ToString("0.0"));
                pe = e;
            }

            // ── 状态边界跳变：换状态那一帧的 |Δ| ──
            // ★ 这是"腿每次出手被拽回去"那一类缺陷**唯一**抓得住的口径 ★
            //   段内二阶差分看不见它（它只有一帧），而它表现成"走路一顿一顿"而不是"抽" ✗
            sb.Append("\n    边界跳变(换状态那帧的|Δ|)：");
            for (int b = 0; b < JerkNames.Length; b++)
            {
                float best = 0f;
                int at = -1;
                for (int i = 1; i < n; i++)
                {
                    if (_jerkState[i] == _jerkState[i - 1]) continue;
                    Vector3 d = DeltaOf(_jerkEuler[b * JerkMaxFrames + i - 1], _jerkEuler[b * JerkMaxFrames + i]);
                    float ad = Mathf.Abs(d[DominantAxis(d)]);
                    if (ad > best) { best = ad; at = i; }
                }
                sb.Append(' ').Append(JerkNames[b]).Append('=').Append(best.ToString("0.00")).Append("°@f").Append(at);
            }

            float upD2 = Mathf.Max(AnalyzeBone(0, ws, we).MaxD2, AnalyzeBone(1, ws, we).MaxD2);
            float loD2 = Mathf.Max(AnalyzeBone(2, ws, we).MaxD2, AnalyzeBone(3, ws, we).MaxD2);
            sb.Append("\n    判定：上两骨 max|Δ²|=").Append(upD2.ToString("0.00"))
              .Append("°  下两骨 max|Δ²|=").Append(loD2.ToString("0.00")).Append("°")
              .Append("  ⇒ 段内抽的是").Append(upD2 >= loD2 ? "**上半身**" : "**下半身**");

            // ══════════════════════════════════════════════════════════════════
            // ★ v191：**逐帧表** —— 交付物要的是"哪一帧、那一帧发生了什么" ★
            //
            // 摘要把 `max|Δ²|` 的帧号报出来了，但"那一帧为什么拐"单靠一个帧号说不出来：
            // Δ² 只能证明"速度在掉头"，**为什么**掉头有三种互斥的可能
            //   ① 片段关键帧的拐角（u 在这一帧跨过 0.833/0.875 那两个密集 key）
            //   ② 状态跃迁（st 在相邻帧变了 —— 但二阶差分**不跨**状态边界算，所以它对②本来就没有分辨力）
            //   ③ 上下身相位差（u 与 L 的步进速率不同 ⇒ 腿把腰拽一下）
            // 只有把 e（欧拉）/ d（Δ）/ d²（Δ²）与**同一帧的 st / u / L** 排在一行，
            // 才能一眼把①③排除掉或指认出来 ✓ ⇒ 所以这一节打印的是"证据本身"，不是又一份摘要
            // ══════════════════════════════════════════════════════════════════
            if (_jerkTable)
            {
                // 只列"真的动过"的骨：站定重放时两根大腿恒 0 ⇒ 省掉一半篇幅而不丢信息 ✓
                var live = new System.Collections.Generic.List<int>();
                for (int b = 0; b < JerkNames.Length; b++)
                    if (AnalyzeBone(b, ws, we).MaxD > 0.005f) live.Add(b);
                if (live.Count == 0) live.Add(0);

                sb.Append("\n    逐帧表 f0..f").Append(n - 1)
                  .Append("（一行一帧：st=状态 u=攻击相位 L=腿相位 up=上半身走片段 mv=腿取Walk；")
                  .Append("每骨 e=欧拉 d=Δ d²=Δ²，单位度）");
                var prevE = new Vector3[live.Count];
                var prevD = new Vector3[live.Count];
                var hasE = new bool[live.Count];
                var hasD = new bool[live.Count];
                for (int i = 0; i < n; i++)
                {
                    // ★ 换状态那一帧的 Δ 是"边界跳变"（真现象 ✓），但它的 Δ² 没有意义 ✗
                    //   二阶差分的前提是同一段连续运动；跨边界算出来的 Δ² 会把"换段"误报成"抽搐" ✗
                    //   ⇒ 边界帧只打 Δ、d² 位置打标记，读数的人不会看错 ✓
                    bool boundary = i > 0 && _jerkState[i] != _jerkState[i - 1];
                    sb.Append("\n      f").Append(i)
                      .Append(' ').Append(StateTag(_jerkState[i]))
                      .Append(" u").Append(_jerkU[i].ToString("0.000"))
                      .Append(" L").Append(_jerkLoco[i].ToString("0.000"))
                      .Append(" up").Append(_jerkUpperOk[i] ? '1' : '0')
                      .Append(" mv").Append(_jerkMoving[i] ? '1' : '0');
                    for (int k = 0; k < live.Count; k++)
                    {
                        int b = live[k];
                        Vector3 e = _jerkEuler[b * JerkMaxFrames + i];
                        sb.Append(" | ").Append(JerkNames[b].Replace("Thigh_", "Th"))
                          .Append(" e").Append(EulerText(e));
                        if (hasE[k])
                        {
                            Vector3 d = DeltaOf(prevE[k], e);
                            sb.Append(" d").Append(EulerText(d));
                            if (boundary) sb.Append(" d²=边界");
                            else if (hasD[k]) sb.Append(" d²").Append(EulerText(d - prevD[k]));
                            prevD[k] = d;
                            hasD[k] = true;
                        }
                        prevE[k] = e;
                        hasE[k] = true;
                    }
                }
            }

            _jerkReport = sb.ToString();
            Debug.Log(_jerkReport);
        }

        /// <summary>逐帧表里的状态简称（定宽，方便按列扫）。</summary>
        private static string StateTag(int s)
        {
            switch ((State)s)
            {
                case State.Idle: return "Idle";
                case State.Walk: return "Walk";
                case State.Attack: return "Atk ";
                case State.Hit: return "Hit ";
                case State.Death: return "Dead";
                case State.Swap: return "Swap";
                default: return "?";
            }
        }

        /// <summary>三元组的紧凑文本（逐帧表用；小数第二位已经足够判"拐没拐"）。</summary>
        private static string EulerText(Vector3 v)
            => v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0");

        /// <summary>一根骨在 `[from,to]` 上的一阶 / 二阶差分统计。</summary>
        private struct JerkStats
        {
            public float MaxD, MaxD2, SumD;
            public int MaxDFrame, MaxD2Frame, MaxD2Axis, Flips, Pairs;
            /// <summary>第一次 |Δ| 达到本段最大值 10% 的那一帧（"起手"的可测口径）。</summary>
            public int OnsetFrame;
        }

        /// <summary>两帧之间的**最短弧**角度差（跨 ±180 不假报警）。</summary>
        private static Vector3 DeltaOf(Vector3 a, Vector3 b)
            => new Vector3(Mathf.DeltaAngle(a.x, b.x), Mathf.DeltaAngle(a.y, b.y), Mathf.DeltaAngle(a.z, b.z));

        private static JerkStats AnalyzeBone(int bone, int from, int to)
        {
            var st = new JerkStats { MaxDFrame = -1, MaxD2Frame = -1, OnsetFrame = -1 };
            if (_jerkEuler == null || from < 0 || to < from) return st;
            Vector3 pe = Vector3.zero, pd = Vector3.zero;
            bool hasE = false, hasD = false;
            // 起手帧要"达到本段最大值的 10%"⇒ 得先知道最大值 ⇒ 先扫一遍
            float segMax = 0f;
            {
                Vector3 q0 = _jerkEuler[bone * JerkMaxFrames + from];
                for (int i = from + 1; i <= to; i++)
                {
                    Vector3 e = _jerkEuler[bone * JerkMaxFrames + i];
                    Vector3 d = DeltaOf(q0, e);
                    float ad = Mathf.Abs(d[DominantAxis(d)]);
                    if (ad > segMax) segMax = ad;
                    q0 = e;
                }
            }
            float onsetThresh = segMax * 0.10f;
            for (int i = from; i <= to; i++)
            {
                Vector3 e = _jerkEuler[bone * JerkMaxFrames + i];
                if (hasE)
                {
                    Vector3 d = DeltaOf(pe, e);
                    int axD = DominantAxis(d);
                    float ad = Mathf.Abs(d[axD]);
                    st.SumD += ad;
                    st.Pairs++;
                    if (ad > st.MaxD) { st.MaxD = ad; st.MaxDFrame = i; }
                    if (st.OnsetFrame < 0 && onsetThresh > 0.001f && ad >= onsetThresh) st.OnsetFrame = i;
                    if (hasD)
                    {
                        Vector3 dd = d - pd;
                        int ax2 = DominantAxis(dd);
                        float add = Mathf.Abs(dd[ax2]);
                        if (add > st.MaxD2) { st.MaxD2 = add; st.MaxD2Frame = i; st.MaxD2Axis = ax2; }
                        // "来回摆"的指纹：主轴上一帧一个符号、这一帧另一个符号
                        if (Mathf.Abs(d[ax2]) > 0.05f && Mathf.Abs(pd[ax2]) > 0.05f
                            && (d[ax2] > 0f) != (pd[ax2] > 0f)) st.Flips++;
                    }
                    pd = d;
                    hasD = true;
                }
                pe = e;
                hasE = true;
            }
            return st;
        }

        /// <summary>
        /// ★ v182：**离屏定步长重放** —— 把 `dt` 钉死，攻击从第 0 帧起跑 `frames` 帧，
        /// 逐帧记一阶 / 二阶差分。改前 / 改后落在**同一组相位**上 ⇒ 读数可比 ✓
        ///
        /// spec = `武器[:dt][:帧数][:move][:chain][:低通τ]`，默认 `hoe:1/60:120:stand`
        ///   · 武器：hoe / can / fert
        ///   · move：`locoMoving = true`（边走边打，下半身取 Walk 片段）；缺省站定（取 Idle）
        ///   · chain：连打模式（走 24 帧 → 出手 → 播完，重复 3 次）
        ///   · 低通τ：v183 的帧间低通时间常数（秒）。缺省 = `PoseLowPassTau`（60ms）；
        ///     **`0` = 关闭低通** ⇒ 用来在同一个可执行文件里复现"改前"的读数 ✓
        ///
        /// 它建一个**临时**模型（与 `WorldView.BuildPlayer` 同一条 `Spawn` 路径、
        /// 同一组攻击片段），跑完就销毁 ⇒ 只读探针，不碰场上任何实例 ✓
        /// </summary>
        public static string AttackJerkReplay(string spec)
        {
            string[] parts = (spec ?? "hoe").Split(':');
            string key = parts.Length > 0 ? parts[0].Trim().ToLowerInvariant() : "hoe";
            int weapon = key.StartsWith("can") || key == "1" ? 1
                       : key.StartsWith("fert") || key == "2" ? 2 : 0;
            float dt = parts.Length > 1 ? ParseFloatInvariant(parts[1], 1f / 60f) : 1f / 60f;
            int frames = parts.Length > 2 ? (int)ParseFloatInvariant(parts[2], 120f) : 120;
            bool move = parts.Length > 3 && parts[3].Trim().ToLowerInvariant().StartsWith("move");
            // parts[4..] = 语法糖：`chain`（连打模式）/ `tau<秒>` 或纯数字（低通 τ 覆盖）
            // ★ 为什么 τ 要能被覆盖 ★ 它让"改前 / 改后"跑在**同一个可执行文件**里：
            //   `tau0` 就是低通关闭 = 改前的行为，与"另一份构建"比才真的公平 ✓
            bool chain = false;
            float tauOverride = -1f;
            // ★ v191：`table` ⇒ 报告里附逐帧表（定位"哪一帧"时用；日常摘要不附，免得刷屏）★
            bool wantTable = false;
            // ★ v191：`dur<秒>` ⇒ 覆写**攻击时长**（默认 = 片段长 ⇒ 倍率 1.00）★
            // 为什么必须能覆写：真实局面里攻速由升级项决定，倍率 2 时片段那个
            // 0.833→0.875 的拐角（2.5 帧）被压进 1 帧 ⇒ 定步长重放若恒按 1.00 倍跑，
            // 就**永远测不到**"低通在高攻速下够不够"这一条 ✗
            float durOverride = -1f;
            // ★ v191：`mode<0|1|2|3>` ⇒ 覆写**平滑器**（0=关 1=一阶低通 2=两帧箱式 3=三帧箱式）★
            // 与 `tau0` / `dur` 同一套路：让"改前 / 改后 / 另一种滤波器"跑在**同一个可执行文件**里，
            // 否则三组读数来自三份构建，谁也说不清差异是代码改的还是构建改的 ✗
            int modeOverride = -1;
            for (int p = 4; p < parts.Length; p++)
            {
                string tag = (parts[p] ?? string.Empty).Trim().ToLowerInvariant();
                if (tag.Length == 0) continue;
                if (tag.StartsWith("chain")) { chain = true; continue; }
                if (tag.StartsWith("table")) { wantTable = true; continue; }
                if (tag.StartsWith("dur")) { durOverride = ParseFloatInvariant(tag.Substring(3), -1f); continue; }
                if (tag.StartsWith("mode")) { modeOverride = (int)ParseFloatInvariant(tag.Substring(4), -1f); continue; }
                if (tag.StartsWith("tau")) tauOverride = ParseFloatInvariant(tag.Substring(3), -1f);
                else if (tag != "move" && tag != "stand") tauOverride = ParseFloatInvariant(tag, tauOverride);
            }
            dt = Mathf.Clamp(dt, 1f / 240f, 0.2f);
            frames = Mathf.Clamp(frames, 8, JerkMaxFrames);

            string suffix = weapon == 1 ? "attack_can" : weapon == 2 ? "attack_fert" : "attack_hoe";

            // 离屏：摆到远离场地的地方（y=-400），Transform 照常算、镜头里看不见
            var holder = new GameObject("JERK_REPLAY");
            holder.transform.position = new Vector3(0f, -400f, 0f);
            float tauBefore = DebugPoseLowPassTau;
            int modeBefore = DebugSmoothMode;
            DebugPoseLowPassTau = tauOverride;      // 只在本方法的作用域内生效
            if (modeOverride >= 0) DebugSmoothMode = modeOverride;
            try
            {
                ModelView mv = Spawn("Characters", "player", holder.transform);
                if (mv == null)
                    return "JERK 取不到 Characters/player（Resources/Art/Anim/player_idle.fbx 在不在？）";

                AnimationClip atk = LoadClip("player", suffix);
                mv.SetAttackClips(new[]
                {
                    LoadClip("player", "attack_hoe"),
                    LoadClip("player", "attack_can"),
                    LoadClip("player", "attack_fert"),
                });
                mv.SetAttackWeapon(weapon);
                // 与游戏里同一件事：`SetAttackDuration(攻击时长)` ⇒ 倍率 = 片段长 / 时长
                // 这里默认给"时长 = 片段长" ⇒ **倍率 1.00**（最干净的一组，不掺攻速升级）
                // ★ 可用 spec 的 `dur<秒>` 覆写 ⇒ 量"倍率 2 / 3 时低通还够不够" ✓
                float dur = durOverride > 0.01f ? durOverride : (atk != null ? atk.length : 1f);
                mv.SetAttackDuration(dur);
                mv.SetLocomotion(move || chain);
                mv.PlayForced(State.Attack);

                int window = chain ? Mathf.Clamp(Mathf.Max(frames, 3 * (24 + 300)), 8, JerkMaxFrames) : frames;
                _jerkTable = wantTable;
                ArmJerkProbe(window, "replay-" + key + "-" + (move ? "move" : "stand")
                                     + (chain ? "-chain" : "")
                                     + "-dt" + (dt * 1000f).ToString("0.0") + "ms");
                if (!chain)
                {
                    for (int i = 0; i < frames && mv._state == State.Attack; i++) mv.Tick(dt);
                }
                else
                {
                    // ── 连打模式：走一段 → 出手 → 片段播完（自己回 Idle）→ 再走 → 再出手 ──
                    // 它复刻"边走边打、且打得很快"的真实节奏 ⇒ **状态边界那一帧也在窗口里** ✓
                    // （单段模式永远看不见边界，而"腿被拽回相位 0"只在边界那一帧发生）
                    for (int pass = 0; pass < 3 && _jerkRemaining > 0; pass++)
                    {
                        mv.PlayForced(State.Walk);
                        for (int i = 0; i < 24 && _jerkRemaining > 0; i++) mv.Tick(dt);
                        mv.PlayForced(State.Attack);
                        for (int i = 0; i < 400 && _jerkRemaining > 0 && mv._state == State.Attack; i++) mv.Tick(dt);
                    }
                }
                // Tick 里采满会自己打一行；采不满（窗口没走完）就在这里补一次
                if (_jerkRemaining > 0) { _jerkRemaining = 0; LogJerkReport(_jerkTag); }

                string text = _jerkReport;
                _jerkReport = "";
                float effTau = DebugPoseLowPassTau < 0f ? PoseLowPassTau : DebugPoseLowPassTau;
                int effMode = DebugSmoothMode >= 0 ? DebugSmoothMode : PoseSmoothMode;
                if (DebugPoseLowPassTau >= 0f && DebugPoseLowPassTau < 1e-6f) effMode = SmModeOff;
                string smoothDesc = effMode == SmModeOff ? "关"
                                  : effMode == SmModeEma
                                      ? "低通τ=" + (effTau * 1000f).ToString("0.0") + "ms(α="
                                        + (1f - Mathf.Exp(-dt / effTau)).ToString("0.00") + ")"
                                  : effMode == SmModeBox2 ? "两帧箱式[.5,.5] 滞后0.5帧"
                                  : effMode == SmModeBox3 ? "三帧箱式[.25,.5,.25] 滞后1.0帧"
                                  : effMode == SmModeBox2Ema
                                      ? "箱式+低通τ=" + (effTau * 1000f).ToString("0.0") + "ms(α="
                                        + (1f - Mathf.Exp(-dt / effTau)).ToString("0.00")
                                        + ") 滞后" + (0.5f + (1f - Mathf.Exp(-dt / effTau))
                                                     / Mathf.Exp(-dt / effTau)).ToString("0.0") + "帧"
                                  : "未知模式" + effMode;
                return "片段=" + (atk != null ? atk.name + " len=" + atk.length.ToString("0.000") + "s" : "(缺)")
                     + " 攻速倍率=" + mv.AttackPlaybackSpeed.ToString("0.00")
                     + " 时长=" + dur.ToString("0.000") + "s"
                     + " loco=" + (move || chain ? "Walk" : "Idle")
                     + (chain ? " 模式=连打×3" : "")
                     + " 平滑=" + smoothDesc
                     + (DebugSmoothMode < 0 ? "(默认)" : "(探针覆盖)")
                     + "\n" + text;
            }
            finally
            {
                DebugPoseLowPassTau = tauBefore;
                DebugSmoothMode = modeBefore;
                _jerkTable = false;         // 逐帧表是本方法的作用域选项，不许漏给下一次探针
                // ★ 编辑态里 `Destroy` 是错误用法（它只在播放中被延迟到帧末）★
                // 实测：`play` 刚被停掉时跑这个探针 ⇒ 控制台一条
                //   `Destroy may not be called from edit mode!`
                // 探针会被两边调用（编辑器里点一下 / play 里走桥），所以两种模式都要对 ✓
                if (Application.isPlaying) Object.Destroy(holder);
                else Object.DestroyImmediate(holder);
            }
        }

        private static float ParseFloatInvariant(string text, float fallback)
        {
            float v;
            if (float.TryParse((text ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        // ── 验收仪表②：攻击片段"自己那一帧"的原始姿态 ────────────────────
        //
        // `Δ片段=` 要的是一个**参照**：把片段忠实采样到骨架上之后、在腰部衰减**之前**
        // 立刻把上半身的局部旋转抄一份。之后探针拿"当前值"与它比：
        //   ≈0     ⇒ 原样吃片段（上半身真的跟着片段走）✓
        //   = (1−k)×片段值 ⇒ 被腰部衰减吃了（这是设计，不是缺陷）✓
        // 没有它，"贴没贴住片段"就只能靠心算，改一次代码要重算一遍 ✗
        private Quaternion[] _clipPoseRot;
        private Transform[] _poseNodes;
        private bool _clipPoseValid;

        private int ClipPoseIndexFor(Transform t)
        {
            if (_poseNodes == null) return -1;
            for (int i = 0; i < _poseNodes.Length; i++)
                if (_poseNodes[i] == t) return i;
            return -1;
        }

        /// <summary>由 `WorldView.SyncPlayer` 每帧喂进来：玩家这一帧在不在移动。</summary>
        public void SetLocomotion(bool moving)
        {
            _locoMoving = moving;
        }

        /// <summary>
        /// 解析骨骼、并把**站立基准帧**（`Idle@t=0`）存下来。
        ///
        /// ★ 为什么基准取 Idle 的第 0 帧而不是"绑定姿势（bind pose）"★
        /// bind pose 是 Blender 里的 A-pose，而 `Idle@0` 是**游戏里真正会出现的站姿**——
        /// 腿站直、髋不转、呼吸相位 0（`sin(0)=0`，所以 Chest / Spine 的呼吸位移也是 0）✓
        /// 拿它当基准，攻击结束回到 Idle 时**没有跳变** ✓
        ///
        /// ★ 任何一根骨骼找不到就整段放弃（`_layered = false`）★
        /// 半套的实现会产生"躯干冻住了、手却按旧片段动"这种更难查的形态 ✗
        /// 敌人 / 植物 / 小猪走同一条路径 —— 它们没有 `Bone_UpperArm_R`，于是自动保持旧行为 ✓
        /// </summary>
        private void BuildLayeredPose()
        {
            _layered = false;
            if (_clips == null || _clips.Length == 0 || _clips[0] == null) return;
            Transform root = _animRoot != null ? _animRoot.transform : Go.transform;

            _frozenNodes = new Transform[FrozenBones.Length];
            _frozenBaseRot = new Quaternion[FrozenBones.Length];
            _frozenBasePos = new Vector3[FrozenBones.Length];
            _lowerNodes = new Transform[LowerBones.Length];

            Transform hand = null;
            int found = 0;
            for (int i = 0; i < FrozenBones.Length; i++)
            {
                _frozenNodes[i] = FindBone(root, FrozenBones[i]);
                if (_frozenNodes[i] != null) found++;
            }
            for (int i = 0; i < LowerBones.Length; i++)
                _lowerNodes[i] = FindBone(root, LowerBones[i]);

            _armUpper = FindBone(root, "Bone_UpperArm_R");
            _armLower = FindBone(root, "Bone_LowerArm_R");
            _armHand = FindBone(root, "Bone_Hand_R");
            hand = _armHand;
            if (found < FrozenBones.Length || _armUpper == null || _armLower == null || hand == null)
                return;

            // ① 采样 Idle@0 ⇒ 记下躯干基准 + 手臂基准（世界系也一起记，姿势求解要用）
            _clips[0].SampleAnimation(_animRoot != null ? _animRoot : Go, 0f);
            for (int i = 0; i < _frozenNodes.Length; i++)
            {
                Transform t = _frozenNodes[i];
                if (t == null) continue;
                _frozenBaseRot[i] = t.localRotation;
                _frozenBasePos[i] = t.localPosition;
            }

            _armUpperW0 = _armUpper.rotation;
            _armLowerW0 = _armLower.rotation;
            _armHandW0 = _armHand.rotation;
            // 肩→肘、肘→手的方向：用**真实子节点位置**量，不对骨骼轴向做任何假设 ✓
            _armUpperD0 = (_armLower.position - _armUpper.position).normalized;
            _armLowerD0 = (_armHand.position - _armLower.position).normalized;
            // 手：它下面**还没有**骨骼子节点（武器是后面 `AttachTools` 挂上去的）
            // ⇒ 用 Blender 骨骼约定（骨骼沿自身 +Y），并在 `VerifyHandAxis()` 里运行期复核 ✓
            _armHandD0 = _armHandW0 * Vector3.up;

            // ② 右手在角色本地系的哪一侧：拿"肩相对根"的水平偏移在"侧向轴"上投影取符号。
            //    两侧都在**同一瞬间、同一个参考系**（`Go` 的朝向）里量 ⇒ 与 yaw 无关 ✓
            Vector3 fwdC = Fwd();
            Vector3 sideC = Vector3.Cross(Vector3.up, fwdC).normalized;      // 面向 +Z 时 = +X
            Vector3 lateral = Vector3.ProjectOnPlane(_armUpper.position - Go.transform.position, Vector3.up);
            _armSide = Vector3.Dot(lateral, sideC) < 0f ? -1f : 1f;

            // ③ 站立基准姿势：把两条"实测方向"换算成角色本地系的三分量。
            //    **在这里换算、之后每次都用同一组常量** ⇒ 不会随 yaw 变化而漂 ✓
            Vector3 sideS = sideC * _armSide;
            _restPose.Upper = ToLocal(_armUpperD0, fwdC, sideS);
            _restPose.Lower = ToLocal(_armLowerD0, fwdC, sideS);
            _restPose.Hand = 0f;
            _restPoseReady = true;

            // ④ 下半身基准（只用于**报告**"脚有没有被带动"）
            _lowerBaseRot = new Quaternion[_lowerNodes.Length];
            for (int i = 0; i < _lowerNodes.Length; i++)
                _lowerBaseRot[i] = _lowerNodes[i] != null ? _lowerNodes[i].localRotation : Quaternion.identity;

            // ⑤ v181：19 根骨骼的**一张表**（上 12 + 下 7）—— 采样/写回/报数都只认它 ✓
            //
            // 为什么必须是**一张**表而不是"上半身数组 + 下半身数组"：
            //   `Δ片段=` 要靠"把 Transform 反查成下标"（`ClipPoseIndexFor`）✗
            //   两张表就有**两个**下标空间，反查要写两遍、还有一半会查错 ✗
            //   一张表 + 一个 `_upperFromClip[]` 标记位 = 只有一个下标空间 ✓
            _poseNodes = new Transform[PoseBoneCount];
            _poseRotFromClip = new bool[PoseBoneCount];
            _poseTmpRot = new Quaternion[PoseBoneCount];
            _poseTmpPos = new Vector3[PoseBoneCount];
            _clipPoseRot = new Quaternion[PoseBoneCount];
            for (int i = 0; i < PoseBoneCount; i++)
            {
                _poseNodes[i] = FindBone(root, i < UpperBoneCount ? UpperPoseBones[i] : LowerBones[i - UpperBoneCount]);
                _poseRotFromClip[i] = i < UpperBoneCount;
            }
            _clipPoseValid = false;

            // ⑤-b v183：攻击姿态低通的状态（只在上半身 12 根上跑，见 `SmoothAttackPose`）
            _smPoseRot = new Quaternion[UpperBoneCount];
            // v191：箱式平滑的两帧目标记忆（与上面的输出状态分开存 ——
            // 一阶低通只需要"上一帧的输出"，箱式需要"前两帧的目标"，两者不能共用一格）
            _smTarget1 = new Quaternion[UpperBoneCount];
            _smTarget2 = new Quaternion[UpperBoneCount];
            _smPoseValid = false;

            // ⑥ v181：**站立基准的 19 根** —— `ProbeUpperCurves` 的参照 ✓
            //
            // 为什么不复用 ① 里那次 `Idle@0`：那一步跑在 ⑤ 之前（表还没建好）✗
            // 再采一次只要几微秒，而"把表建好"这件事只发生在 Spawn 里一次 ⇒ 直接重采更省心 ✓
            _idleBaseRot = new Quaternion[PoseBoneCount];
            _clips[0].SampleAnimation(_animRoot != null ? _animRoot : Go, 0f);
            for (int i = 0; i < PoseBoneCount; i++)
                _idleBaseRot[i] = _poseNodes[i] != null ? _poseNodes[i].localRotation : Quaternion.identity;
            _upperProbeWeapon = -1;          // 下一次攻击会重新判定这一把的片段
            _upperOk = true;                 // 判定之前按"可以"算（避免第一帧误走兜底）

            _layered = true;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v181：上半身采样 —— 三个动作（快照 / 片段 / 写回）★
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>`Bone_Spine` 在 `UpperPoseBones` 里的下标（= 0）。**与那张表位置绑定**，改表必改它。</summary>
        private const int UpperSpineIndex = 0;

        /// <summary>
        /// ★ v191-b：腰部混合的权重 —— **0 = 脊柱完全不吃攻击片段**（用户需求："不要扭腰，纯手挥动"）★
        ///
        /// ── 从 0.5 改成 0 的依据（用户原话，不是口味）──
        /// "重做玩家锄头攻击动画（**不要扭腰动作，纯手挥动**）"
        /// ⇒ 腰/脊柱**不参与挥击**，挥击由**手臂链**（肩→上臂→前臂→手）产生 ✓
        ///
        /// ── 0 到底意味着什么（一句话：`Bone_Spine` 保持 locomotion 的姿态）──
        /// `BlendWaist()` 是 `Slerp(locomotion 的脊柱, 片段的脊柱, w)`：
        ///   w=0 ⇒ 恰好等于 locomotion 的脊柱 ⇒ 攻击片段对**腰**的贡献 = 0（不是"事后压回 0"）✓
        /// 而 `Bone_Chest` 及以上的局部旋转**照旧吃片段** —— 它们是**肩的起点**：
        ///   只动手臂而不动胸/肩，挥击的"根"就断了（剪影读不出力从哪来）✓
        ///   而"腰"是 `Bone_Hips ↔ Bone_Spine` 那一处缝，用户说的"扭腰"就在那儿 ✓
        ///
        /// ── 为什么不是"删掉 `UpperPoseBones` 里的 Spine" ──
        /// 那张表的下标被 `UpperSpineIndex` / `UpperChestIndex` / `JerkPoseIndex` /
        /// `PoseBoneCount` 直接绑定 ⇒ 少一格会让**所有**探针读数整体错位一格 ✗
        /// 而权重 0 让"表还在、贡献为 0"，探针照样能读到 Spine 的那一行（读数是 locomotion）✓
        ///
        /// ── 可验证（`AnimJerk`，三档倍率）──
        ///   Spine  `max|Δ|`：0.70/1.10/1.23° → **≈0**（那不是"变小"，是"没在动"）
        ///   Chest  `max|Δ²|`：0.49/0.81/1.21° → **逐位不变**（Chest 的局部旋转与腰无关）✓
        /// ⚠️ 旧注释说"改成 0 会退化成 v180 的躯干冻结"—— 那条**只对 6 根 `FrozenBones` 成立**；
        ///    v181 起躯干已经分成"腰（可混合）／胸以上（吃片段）"两段，
        ///    所以 w=0 只冻结**腰这一根**，而胸/颈/头/肩/双臂**全部照常吃片段** ✓
        /// </summary>
        private const float WaistKeepAtSpine = 0f;

        /// <summary>
        /// ★ v191：攻击姿态低通的**时间常数** τ（秒）。默认 0.090 ⇒ 60fps 时 α = 0.17 ★
        ///
        /// ── 为什么是"时间常数"而不是直接写 α ★ ──
        /// `α = 1 − e^(−dt/τ)` ⇒ 滤波的**物理时长**恒定，只有"每帧走多少"跟着 dt 变：
        ///   60fps: α=0.17 ／ 30fps: α=0.31 ／ 17fps（编辑器后台）: α=0.55
        /// 写死 α 的话，17fps 下同一秒只滤 α×17 次 ⇒ 低帧率时**滤得更少**
        /// ⇒ "编辑器里不抽、实机更抽"这种最坏的方向 ✗（本工程 dt 实测在 17~60fps 之间跳）
        ///
        /// ── v183 定 0.060 时漏掉的那一维：**攻速倍率**（v191 补测）★ ──
        /// v183 的扫参全部跑在**倍率 1.00**（`SetAttackDuration(片段长)`）上 ✗
        /// 而真实局面里倍率不是自由参数 —— `AttackSeconds = tool.cooldown ÷ AttackSpeed`
        /// （`Resources/Data/tuning.json`：锄头 `cooldown = 0.70`）
        /// ⇒ **开局倍率 = 1.458 / 0.70 = 2.08**，攻速升满约 **3.12** ✓
        /// 倍率一高，那个 0.833→0.875 的关键帧拐角（Δu = 0.0287）在真实时间里被压缩：
        ///   倍率 1.00 ⇒ 跨 2.5 帧 ／ 2.08 ⇒ 1.25 帧 ／ 3.12 ⇒ **0.83 帧（已不足一帧）** ✓
        /// ⇒ 同一份 τ=60 的读数因此从 0.69° 一路涨到 1.96° ✗（下表第二列）
        ///
        /// ── v191 重扫（`AnimJerk` 的 `dur` 规格把三档倍率都钉死，同一份代码）★ ──
        ///   倍率   off            τ=60(v183)      τ=90(v191)      τ=120        峰值帧
        ///   1.00   1.64°/反1      0.70°/反0       0.49°/反0       0.37°/反0    f50→f51（不变）
        ///   2.08   5.62°/反1      1.20°/反2       0.81°/反2       0.58°/反2    f24→f24（**0 帧**）
        ///   3.12   8.57°/反3      1.96°/反3       1.21°/**反2**   0.84°/反2    f13→f16（+2 帧，τ 无关）
        ///   （列 = `Chest max|Δ²|` / 反向次数；Spine 同向更好）
        /// ⇒ **τ=90 在每一档都严格优于 τ=60**：Δ² 再降 30~38%，
        ///   倍率 3.12 那一档连"反向"都从 3 降到 2 ⇒ 两个判据同时下降 ✓
        /// ⇒ 而**峰值帧号在 τ=60/90/120 三档完全相同** ⇒ 加平滑没有换来手感偏移 ✓
        ///   （原因不是巧合：峰值落在 |Δ| 的极值处，低通只削它的高度、不动它的位置 ✓）
        /// ⚠️ 如实写：`峰值帧` 相对"关平滑"在高倍率下会推后 1~2 帧，**这条在 v183 的 τ=60 上就已经存在**
        ///    （τ=60 与 τ=90 的峰值帧逐档相同）⇒ 不是本次引入的 ✓
        /// ⚠️ 也如实写：τ 再往上（120）Δ² 还会更低（2.08 档 0.58°），但峰高 `max|Δ|` 继续被削
        ///    ⇒ 弧线变得更钝，那是改"手感"而不是改"抽" ⇒ 停在 90ms ✓
        /// </summary>
        private const float PoseLowPassTau = 0.090f;

        /// <summary>
        /// ★ v183：验收用 —— 覆盖低通 τ（&lt;0 = 用默认，0 = 关闭低通）★
        ///
        /// 只由**离屏重放**（`AttackJerkReplay` 的 spec 第 6 段）写，正常游戏恒为 −1 ✓
        /// 它的用途是让"改前 / 改后"跑在**同一个可执行文件**里（τ=0 复现改前的读数），
        /// 免得"两次构建的基线不可比"这种偏差 ✓
        /// 与既有的 `DebugAttackPoseAt` / `DebugHoldAttack` 是同一类验收通道 ✓
        /// </summary>
        public static float DebugPoseLowPassTau = -1f;

        /// <summary>把 locomotion 采样后的**整副姿势**（19 根的位置 + 旋转）抄一份。</summary>
        private void CaptureLocomotionPose()
        {
            for (int i = 0; i < PoseBoneCount; i++)
            {
                Transform t = _poseNodes[i];
                if (t == null) continue;
                _poseTmpRot[i] = t.localRotation;
                _poseTmpPos[i] = t.localPosition;
            }
        }

        /// <summary>把攻击片段**刚写进骨架**的上半身旋转抄一份 —— 探针的"片段原样"参照。</summary>
        private void CaptureClipUpperPose()
        {
            for (int i = 0; i < UpperBoneCount; i++)
            {
                Transform t = _poseNodes[i];
                if (t == null) continue;
                _clipPoseRot[i] = t.localRotation;
            }
            _clipPoseValid = true;
        }

        /// <summary>
        /// 腰部：`Bone_Spine` = 在 locomotion 的脊柱与片段的脊柱之间 `Slerp(WaistKeepAtSpine)`。
        ///
        /// ★ 为什么是 Slerp 而不是"把欧拉角乘 0.5" ★
        /// 欧拉角不是向量：X/Y/Z 各乘 0.5 得到的姿态**不是**两次旋转的中点 ✗
        /// 而四元数球面插值给的正是"转过一半的角度、绕同一根轴"⇒ 缝的角度恰好减半 ✓
        /// 这一点是可验的：探针的 `Δ片段=` 应读成 `(1−0.5) × 片段值` ✓
        /// </summary>
        private void BlendWaist()
        {
            Transform spine = _poseNodes[UpperSpineIndex];
            if (spine == null) return;
            spine.localRotation = Quaternion.Slerp(_poseTmpRot[UpperSpineIndex],
                                                   _clipPoseRot[UpperSpineIndex],
                                                   WaistKeepAtSpine);
        }

        /// <summary>
        /// 把**下半身 7 根**的旋转写回 locomotion；位置则 19 根**全部**写回 locomotion。
        ///
        /// ★ 位置为什么连上半身一起写回 ★
        /// `Tools/mesh_gen/anim_curve_audit.py` 逐节点量过三把武器的攻击片段：
        /// **所有 Position 通道的总变化量都是 0.0000 m**（常量）✓
        /// 也就是说它们对姿势**一点贡献都没有**，只可能带来"两个片段对绑定偏移的写法不一致"
        /// 这种跳变风险 ✗ ⇒ 位置一律以 locomotion 为准，等于白拿一次稳定性 ✓
        /// </summary>
        private void RestoreLowerPose()
        {
            for (int i = 0; i < PoseBoneCount; i++)
            {
                Transform t = _poseNodes[i];
                if (t == null) continue;
                if ((t.localPosition - _poseTmpPos[i]).sqrMagnitude > 1e-9f)
                    t.localPosition = _poseTmpPos[i];
                if (!_poseRotFromClip[i] && Quaternion.Angle(t.localRotation, _poseTmpRot[i]) > 0.02f)
                    t.localRotation = _poseTmpRot[i];
            }
        }

        /// <summary>
        /// ★ v183：攻击姿态的**帧间低通**（修"段内抽搐"）★
        ///
        /// ── 病根：不是状态边界了，是**片段本身太稀**（实测读数，不是推断）★ ──
        /// 上一轮修掉的是边界（Thigh 一帧 29.58° → 3.90°）。剩下的抖动在**段内**：
        /// 定步长重放（`AnimJerk`，dt=1/60，hoe，站定）实测
        ///   Chest max|Δ²|=1.63°@f52X ／ Spine 0.61° ／ 尖峰窗口 f50Δ=2.3 f51Δ=2.1
        ///   f52Δ=0.4 f53Δ=−1.2 f54Δ=−1.4 ⇒ 速度在 **2 帧内从 +2.1°/帧 掉头到 −1.2°/帧**
        /// 逐 key 数出来的原因（`Assets/Resources/Art/AnimMerged/player_attack_hoe.asset`）：
        ///   Bone_Chest  1.458s 只有 **6 个关键帧**：t=0 / 0.583 / 0.833 / 0.875 / 1.000 / 1.458
        ///   Bone_Spine  8 个；最大间隔 **0.583s = 35 帧@60fps**
        ///   而 0.833→0.875 只隔 **0.0417s（2.5 帧）**，且两端的 outSlope 一个 −0.279 一个 −0.065
        ///   ⇒ 片段在 2.5 帧里走完一段陡弧、紧接着下一段（slope +0.122）把速度拉回来
        ///   ⇒ **60fps 采样必然看到"拐角"**：这正是用户说的抽一下 ✓
        /// ⇒ 结论：这不是采样错了、也不是相位抖了（重放 dt 恒定，u 等步，读数照样 1.63°），
        ///   而是**曲线在时间上缺采样** ⇒ 只能在采样侧补一次低通 ✓
        ///
        /// ── 补的就是时间轴上的低通，逐骨骼 Slerp ──
        /// `q ← Slerp(q_prev, q_target, α)`，α = 1 − e^(−dt/τ)（τ=`PoseLowPassTau`）
        ///   · 低通把"2 帧内掉头"摊成 5~6 帧 ⇒ Δ² 按一阶滤波的频响被压下去 ✓
        ///   · 斜坡保持：一阶低通对匀速段只产生**固定滞后**（(1−α)/α 帧 ≈ 1.9 帧 @60fps），
        ///     不改变速度 ⇒ `max|Δ|`（挥击幅度）基本不动，动的是加速度 ✓
        ///   · 只滤**旋转**，位置一律不滤：位置本来全部由 locomotion 写回（攻击片段
        ///     的 Position 通道三把武器实测都是常量），滤它只会让脚在地上多滑 2 帧 ✗
        ///   · 只滤**上半身 12 根**：拐角在片段驱动的那 12 根上；腿走的是 `player_walk`
        ///     （Thigh 42 个关键帧的步态），那是**该有的摆动**，滤它等于给脚加延迟 ✗
        ///
        /// ── 为什么不"把片段重采样一份更密的" ──
        /// 那要改 `Assets/Resources/Art/AnimMerged/*.asset`（本次任务书不许动），
        /// 而且换片段就得重跑整条美术流水线；采样侧 5 行代码即可，且对所有武器同时生效 ✓
        /// </summary>
        private void SmoothAttackPose(float dt)
        {
            if (_smPoseRot == null || _poseNodes == null) return;

            // 探针口径：`tau0` = 关；`mode<0|2|3>` 另设（见 `AttackJerkReplay` 的规格）
            int mode = DebugSmoothMode >= 0 ? DebugSmoothMode : PoseSmoothMode;
            if (DebugPoseLowPassTau >= 0f && DebugPoseLowPassTau < 1e-6f) mode = SmModeOff;

            float tau = DebugPoseLowPassTau < 0f ? PoseLowPassTau : DebugPoseLowPassTau;
            float alpha = 1f;
            if ((mode == SmModeEma || mode == SmModeBox2Ema) && tau > 1e-6f)
                alpha = Mathf.Clamp01(1f - Mathf.Exp(-Mathf.Max(dt, 1e-4f) / tau));

            // 换状态后第一帧：把记忆**填成当前目标** ⇒ 输出 = 目标，不吃上一段的余尾 ✓
            // （这条从 v183 起就是对的：它保证"收招姿态"不会被拖进下一刀的第 0 帧）
            bool seed = !_smPoseValid;

            for (int i = 0; i < UpperBoneCount; i++)
            {
                Transform t = _poseNodes[i];
                if (t == null) continue;
                Quaternion target = t.localRotation;

                Quaternion q;
                switch (mode)
                {
                    case SmModeEma:
                        q = (seed || alpha >= 0.999f)
                            ? target
                            : Quaternion.Slerp(_smPoseRot[i], target, alpha);
                        break;

                    case SmModeBox2:
                        // [0.5, 0.5] ⇒ 群延迟 0.5 帧；奈奎斯特处响应 0
                        q = seed ? target : Quaternion.Slerp(_smTarget1[i], target, 0.5f);
                        _smTarget1[i] = target;
                        break;

                    case SmModeBox3:
                        // [0.25, 0.5, 0.25] ⇒ 群延迟 1.0 帧；两级 Slerp 得到同样的三角权重
                        q = seed
                            ? target
                            : Quaternion.Slerp(Quaternion.Slerp(_smTarget2[i], _smTarget1[i], 0.5f),
                                               target, 0.5f);
                        _smTarget2[i] = _smTarget1[i];
                        _smTarget1[i] = target;
                        break;

                    case SmModeBox2Ema:
                        {
                            // 级联：先进"两帧箱式"再进一阶低通
                            // 分工：箱式负责**奈奎斯特那一段**（一帧高 / 一帧低 —— 倍率 3 时
                            // 那个 0.83 帧的拐角正落在这里），低通负责**更低频的长尾**。
                            // 两者串联的群延迟 = 0.5 + (1−α)/α 帧 ⇒ 比起单纯低通省下 0.5 帧，
                            // 而 Δ² 只会更低（串联滤波的频响是两者的乘积）⇒ 两个判据同时变好 ✓
                            Quaternion boxed = seed
                                ? target
                                : Quaternion.Slerp(_smTarget1[i], target, 0.5f);
                            _smTarget1[i] = target;
                            q = (seed || alpha >= 0.999f)
                                ? boxed
                                : Quaternion.Slerp(_smPoseRot[i], boxed, alpha);
                            break;
                        }

                    default:
                        // 关：直接吃目标。记忆不维护 ⇒ 打开时第一帧必然走 seed 分支 ✓
                        q = target;
                        break;
                }

                _smPoseRot[i] = q;
                // ★ 无条件写回 ★ —— 曾经写过"差 < 0.01° 就不写"的小优化，删掉了：
                // 那会让**滤波状态**与**骨架上真正的值**分叉（状态每帧前进、骨架不动），
                // 下次写回时一次性补上 ⇒ 给 Δ 叠加 0.01° 的台阶
                // ⇒ 抽搐探针把 **0.01° 的量化台阶**数成"反向"（实测：同一 τ 下反向次数在 0~3 之间跳）✗
                // 写 12 个 localRotation 的代价远小于"读数不可信" ✓
                t.localRotation = q;
            }
            _smPoseValid = true;
        }

        /// <summary>
        ///
        /// 为什么必须问：`SampleAnimation` 对"片段里没写到的节点"**什么都不做**（不报错）✗
        /// ⇒ 若某把武器的片段只动了腿，上半身会**保留 locomotion 的姿势**
        /// ⇒ 看起来就是"这一把武器的攻击动作没了" ✗ 而且和"接错了片段"长得一模一样 ✓
        ///
        /// 判据：把片段采到 60% 处（三把武器的折叠峰值都在 0.46~0.60，实测见上表），
        /// 量 12 根上半身相对 `Idle@0` 的最大夹角 ⇒ > 0.5° 就算"有" ✓
        /// ⚠️ 它**会改动骨架** —— 调用点必须紧接着重新采样 locomotion（`Tick` 里就是这样的顺序）✓
        /// </summary>
        private bool ProbeUpperCurves(AnimationClip attack)
        {
            if (attack == null || attack.length <= 0.0001f || _idleBaseRot == null) return false;
            GameObject root = _animRoot != null ? _animRoot : Go;
            attack.SampleAnimation(root, attack.length * 0.6f);
            float dev = 0f;
            for (int i = 0; i < UpperBoneCount; i++)
            {
                Transform t = _poseNodes[i];
                if (t == null) continue;
                dev = Mathf.Max(dev, Quaternion.Angle(t.localRotation, _idleBaseRot[i]));
            }
            return dev > 0.5f;
        }

        /// <summary>按名字在模型里递归找骨骼（层级深度不稳定，所以不按路径找）。</summary>
        private static Transform FindBone(Transform root, string boneName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == boneName) return all[i];
            return null;
        }

        /// <summary>把躯干与肩写回站立基准帧。**只在攻击期间调用。**</summary>
        private void RestoreTorso()
        {
            if (_frozenNodes == null) return;
            for (int i = 0; i < _frozenNodes.Length; i++)
            {
                Transform t = _frozenNodes[i];
                if (t == null) continue;
                // 只在真的有偏差时写 —— 与 v169 那段"避免每帧往 Transform 上写值"同一纪律
                if (Quaternion.Angle(t.localRotation, _frozenBaseRot[i]) > 0.02f)
                    t.localRotation = _frozenBaseRot[i];
                if ((t.localPosition - _frozenBasePos[i]).sqrMagnitude > 1e-8f)
                    t.localPosition = _frozenBasePos[i];
            }
        }

        // ── 三把武器的手部姿势 ─────────────────────────────────────────────
        //
        // ★ 为什么姿势用"**方向 + 屈肘角**"，而不是"欧拉角"★
        // 骨头的局部系是 Blender 导出的（局部 Y 沿骨头自己），直接用欧拉写进去
        // 会把"抬手臂"变成"绕手臂轴自转"，角度读数正常、模型却只是拧了一下 ✗
        // （`gen_player_q.py` 的 `world_rot_local` 就是为这件事存在的。）
        // 这里换成**几何求解**：给出"上臂要指向哪、前臂要指向哪、手要不要翻"，
        // 用 `Quaternion.FromToRotation` 把基准方向对齐过去 ——
        // 与骨骼局部轴约定**完全无关**，也就不存在"读数对、姿势错"的形态 ✓
        //
        // 方向一律在**角色本地系**里给：(前 fwd, 侧 side, 上 up) 三个分量，
        // side 的符号由 `_armSide` 定（右手在哪一侧由 `BuildLayeredPose` 实测），
        // 所以这套姿势对"模型被镜像导出"也成立 ✓

        private struct ArmPose
        {
            public Vector3 Upper;     // 上臂指向（角色本地系，未归一化也可以）
            public Vector3 Lower;     // 前臂指向
            public float Hand;        // 手腕绕**侧向轴**的附加角（度，正 = 指向前方）

            public static ArmPose Lerp(in ArmPose a, in ArmPose b, float k)
            {
                ArmPose r = default;
                r.Upper = Vector3.Lerp(a.Upper, b.Upper, k);
                r.Lower = Vector3.Lerp(a.Lower, b.Lower, k);
                r.Hand = Mathf.Lerp(a.Hand, b.Hand, k);
                return r;
            }
        }

        /// <summary>站立基准：手臂自然下垂（由基准帧**量出来**，不是写死的）。</summary>
        private ArmPose _restPose;
        private bool _restPoseReady;

        /// <summary>把一条世界方向换算成角色本地系的三分量 (fwd, side, up)。</summary>
        private static Vector3 ToLocal(Vector3 worldDir, Vector3 fwd, Vector3 side)
            => new Vector3(Vector3.Dot(worldDir, fwd), Vector3.Dot(worldDir, side),
                           Vector3.Dot(worldDir, Vector3.up));

        /// <summary>
        /// 角色当前的水平前向。
        ///
        /// ★ 为什么用 `Go.transform.forward` 而不是自己记一个 yaw ★
        /// `SetFacing` 把朝向写成 `Euler(0,yaw,0) * BaseRotation`，而模型正面在**导出时**已烘焙成
        /// Unity +Z（`WorldView.SyncPlayer` 的注释写着这一条）⇒ `Go.forward` 就**是**角色朝向 ✓
        /// 自己再存一份 yaw 就是**第二个真值**，两者迟早不同步（本工程已经踩过"两个时间源"）✗
        /// </summary>
        private Vector3 Fwd()
        {
            Vector3 f = Vector3.ProjectOnPlane(Go.transform.forward, Vector3.up);
            if (f.sqrMagnitude < 0.0001f) f = Vector3.ProjectOnPlane(Go.transform.up, Vector3.up);
            if (f.sqrMagnitude < 0.0001f) f = Vector3.forward;
            return f.normalized;
        }

        /// <summary>角色本地系的"右手那一侧"轴。</summary>
        private Vector3 Side() => Vector3.Cross(Vector3.up, Fwd()).normalized * _armSide;

        /// <summary>
        /// 三把武器的关键姿势（`u` = 攻击进度 0..1）。
        ///
        /// 需求逐条对应：
        ///   · **锄头**：只挥**手**，保持站定，腿不动 ⇒ 起手举到身后上方 → 一斧劈到身前下方 → 收回
        ///   · **水壶**：**向前伸出手**的姿势（不是挥动，是伸直前探）⇒ 一段伸出 + 持有 + 收回
        ///   · **肥料**：**投掷姿势**（一只手向后上方引，再向前甩）⇒ 引到身后 → 前甩 → 收回
        /// 三套的"发力源"刻意不同（肩的上下 / 前臂的伸展 / 肩的纵向拉扯），
        /// 所以把武器藏起来、只看剪影也能分出是哪一把 ✓
        /// </summary>
        private ArmPose PoseFor(int weapon, float u)
        {
            // PlayerActor.WeaponHoe/Can/Fertilizer = 0/1/2
            switch (weapon)
            {
                case 1:   // ── 水壶：伸直前探（不是挥）──
                {
                    // 伸出 0..0.28 → 持有 0.28..0.72 → 收回 0.72..1
                    ArmPose reach = new ArmPose
                    {
                        Upper = new Vector3(0.94f, 0.10f, 0.02f),   // 上臂几乎水平朝前
                        Lower = new Vector3(0.99f, 0.08f, -0.06f),   // 前臂再伸直一点、略下压
                        Hand = 42f,                                  // 腕内翻 = "把壶倒过来"
                    };
                    if (u < 0.28f) return ArmPose.Lerp(_restPose, reach, EaseOut(u / 0.28f));
                    if (u < 0.72f) return reach;
                    return ArmPose.Lerp(reach, _restPose, EaseOut((u - 0.72f) / 0.28f));
                }

                case 2:   // ── 肥料：投掷（向后上方引 → 向前甩）──
                {
                    ArmPose windup = new ArmPose
                    {
                        Upper = new Vector3(-0.60f, 0.34f, 0.72f),   // 上臂向后上方举
                        Lower = new Vector3(-0.72f, 0.30f, 0.30f),   // 前臂折回身后（屈肘 ≈132°）
                        Hand = -16f,
                    };
                    ArmPose release_ = new ArmPose
                    {
                        Upper = new Vector3(0.86f, 0.12f, 0.14f),    // 上臂向前甩平
                        Lower = new Vector3(0.96f, 0.06f, -0.22f),   // 前臂几乎伸直、略下压
                        Hand = 18f,
                    };
                    if (u < 0.34f) return ArmPose.Lerp(_restPose, windup, EaseOut(u / 0.34f));
                    if (u < 0.56f) return ArmPose.Lerp(windup, release_, (u - 0.34f) / 0.22f);
                    return ArmPose.Lerp(release_, _restPose, EaseOut((u - 0.56f) / 0.44f));
                }

                default:  // ── 锄头：只挥手（腿不动、身体不动）──
                {
                    ArmPose windup = new ArmPose
                    {
                        Upper = new Vector3(-0.34f, 0.52f, 0.78f),   // 举到身后上方
                        Lower = new Vector3(-0.46f, 0.44f, 0.28f),   // 屈肘 ≈56°
                        Hand = -10f,
                    };
                    ArmPose strike = new ArmPose
                    {
                        Upper = new Vector3(0.80f, 0.28f, -0.52f),   // 一斧劈到身前下方
                        Lower = new Vector3(0.92f, 0.18f, -0.34f),   // 接近伸直（屈肘 ≈13°）
                        Hand = 12f,
                    };
                    if (u < 0.42f) return ArmPose.Lerp(_restPose, windup, EaseOut(u / 0.42f));
                    if (u < 0.60f) return ArmPose.Lerp(windup, strike, (u - 0.42f) / 0.18f);
                    return ArmPose.Lerp(strike, _restPose, EaseOut((u - 0.60f) / 0.40f));
                }
            }
        }

        private static float EaseOut(float k)
        {
            k = Mathf.Clamp01(k);
            return 1f - (1f - k) * (1f - k);
        }

        /// <summary>把姿势解算到骨骼上。**只写右手链三根**（上臂 / 前臂 / 手）。</summary>
        private void ApplyArmPose(int weapon, float u)
        {
            if (!_layered || !_restPoseReady) return;

            ArmPose pose = PoseFor(weapon, u);
            Vector3 fwd = Fwd();
            Vector3 side = Side();

            Vector3 upperDir = (fwd * pose.Upper.x + side * pose.Upper.y
                                + Vector3.up * pose.Upper.z).normalized;
            Vector3 lowerDir = (fwd * pose.Lower.x + side * pose.Lower.y
                                + Vector3.up * pose.Lower.z).normalized;
            // 手：前臂方向再绕**侧向轴**翻一个角（倒水 / 甩腕都靠它）
            Vector3 handDir = (Quaternion.AngleAxis(pose.Hand, side) * lowerDir).normalized;

            if (upperDir.sqrMagnitude < 0.5f) upperDir = _armUpperD0;
            if (lowerDir.sqrMagnitude < 0.5f) lowerDir = _armLowerD0;

            // ★ 根→梢逐节写**世界旋转**，不写局部欧拉 ★
            // `FromToRotation(d0, dir) * W0` 的几何意义：把"基准里指向子节点的方向"转到目标方向，
            // 骨骼自己的轴向约定完全不参与计算 ✓
            _armUpper.rotation = Quaternion.FromToRotation(_armUpperD0, upperDir) * _armUpperW0;
            _armLower.rotation = Quaternion.FromToRotation(_armLowerD0, lowerDir) * _armLowerW0;
            _armHand.rotation = Quaternion.FromToRotation(_armHandD0, handDir) * _armHandW0;
        }

        // ── 验收：把"攻击时到底动了什么"量出来 ─────────────────────────────

        /// <summary>
        /// 分层姿势的**实测读数**（给 `WorldView.ProbePlayerAnim` 拼进一行）。
        ///
        /// 报三件事：
        ///   ① 手部链三根骨骼的**局部欧拉角**（改前改后同一口径）
        ///   ② 躯干/肩相对站立基准的**最大偏差**（攻击期间必须 ≈0，否则"身体不动"没做到）
        ///   ③ 下半身七根相对基准的**最大偏差** + 脚的世界坐标（"脚有没有被带动"的直接证据）
        /// </summary>
        public string DescribeLayeredPose()
        {
            if (_bean != null) return "程序化豆豆人（无片段/无骨骼采样）｜" + _bean.Describe();
            if (!_layered) return "分层=关（骨骼不齐，走旧路径）";
            var sb = new System.Text.StringBuilder(256);
            sb.Append("分层=开 ");
            // 这一帧的姿势**来源**（比"开/关"更有用：能分清"吃了片段"还是"退回旧路径"）
            sb.Append(_clipPoseValid
                ? "源=攻击片段(腰w=" + WaistKeepAtSpine.ToString("0.00") + ") "
                : (Current == State.Attack ? "源=旧路径(躯干冻结) " : "源=非攻击帧 "));
            // ★ v181：**这一帧下半身取的是哪一段** ✗ —— 不加这一项就分不清"腿在走"与"腿在站" ✓
            // 它是 `WorldView.Moved` 逐帧判定的结果（≥5mm 且 >0.05m/s），不是本类算的 ✓
            // ⇒ 同一次"边走边打"里它会跳动：两条口径（本类的脚位移 / 它的速度阈值）阈值不同 ✓
            sb.Append(_locoMoving ? "loco=Walk " : "loco=Idle ");
            if (_armUpper != null) sb.Append("UpperArmR=").Append(Euler(_armUpper.localEulerAngles)).Append(' ');
            if (_armLower != null) sb.Append("LowerArmR=").Append(Euler(_armLower.localEulerAngles)).Append(' ');
            if (_armHand != null) sb.Append("HandR=").Append(Euler(_armHand.localEulerAngles)).Append(' ');

            float frozenDev = 0f;
            for (int i = 0; i < _frozenNodes.Length; i++)
            {
                Transform t = _frozenNodes[i];
                if (t == null) continue;
                frozenDev = Mathf.Max(frozenDev, Quaternion.Angle(t.localRotation, _frozenBaseRot[i]));
            }
            sb.Append("躯干最大偏差=").Append(frozenDev.ToString("0.00")).Append("° ");

            float lowerDev = 0f;
            for (int i = 0; i < _lowerNodes.Length; i++)
            {
                Transform t = _lowerNodes[i];
                if (t == null || _lowerBaseRot == null || i >= _lowerBaseRot.Length) continue;
                lowerDev = Mathf.Max(lowerDev, Quaternion.Angle(t.localRotation, _lowerBaseRot[i]));
            }
            sb.Append("下半身最大偏差=").Append(lowerDev.ToString("0.00")).Append("°");

            // 手的世界位置（相对模型根）—— 姿势"伸出去多远"的直接读数
            if (_armHand != null && Go != null)
            {
                Vector3 d = _armHand.position - Go.transform.position;
                sb.Append(' ').Append("手相对根=(")
                  .Append(d.x.ToString("0.00")).Append(',')
                  .Append(d.y.ToString("0.00")).Append(',')
                  .Append(d.z.ToString("0.00")).Append(')');
            }

            // ★ v181：**逐骨**读数（把"上半身到底跟没跟片段走"变成数字）★
            //
            // 上面那两个"最大偏差"是聚合量：它们能回答"有没有被带动"，
            // 但**分不出**"脊柱按片段走了、还是被衰减了一半"✗
            // ⇒ 这里把任务书点名的几根单独报出来，且同时给三个参照：
            //   `=(x,y,z)`  局部欧拉（改前改后同一口径）
            //   `Δ基=`      相对 `Idle@t=0` 的夹角 —— 站立时该 ≈0
            //   `Δ片段=`    相对**攻击片段自己那一帧**的夹角（见 `_clipPoseRot`）——
            //               ≈0 = 原样吃片段；= 片段值的 (1−k) = 被腰部衰减吃了 ✓
            AppendJoint(sb, "Spine", _frozenNodes, _frozenBaseRot, FrozenSpine, true);
            AppendJoint(sb, "Chest", _frozenNodes, _frozenBaseRot, FrozenChest, true);
            // ⚠️ 下半身三根用 `withClip: false` ✗ —— 它们**不从片段取值**（片段里是常量 0）
            //    传 true 会拿一个**从未写入**的参照（`_clipPoseRot` 的下半身槽位恒为默认四元数）
            //    去比 ⇒ 恒读 180° ✗ 而那个数看起来像"腿错了 180°"，其实只是没写 ✓
            AppendJoint(sb, "Hips", _lowerNodes, _lowerBaseRot, LowerHips, false);
            AppendJoint(sb, "ThighL", _lowerNodes, _lowerBaseRot, LowerThighL, false);
            AppendJoint(sb, "ThighR", _lowerNodes, _lowerBaseRot, LowerThighR, false);
            AppendJoint(sb, "ShinL", _lowerNodes, _lowerBaseRot, LowerShinL, false);

            // 两只脚的世界位置（相对模型根）—— 站定 vs 走动时该**明显不同** ✓
            AppendFoot(sb, "脚L", _lowerNodes, LowerFootL);
            AppendFoot(sb, "脚R", _lowerNodes, LowerFootR);
            return sb.ToString();
        }

        /// <summary>追加一根骨骼的"局部欧拉 + 相对基准 + 相对攻击片段"三档读数。</summary>
        private void AppendJoint(System.Text.StringBuilder sb, string label,
                                 Transform[] nodes, Quaternion[] baseRot, int index, bool withClip)
        {
            if (nodes == null || index < 0 || index >= nodes.Length) return;
            Transform t = nodes[index];
            if (t == null) return;
            sb.Append(' ').Append(label).Append("=(")
              .Append(Euler(t.localEulerAngles)).Append(')');
            if (baseRot != null && index < baseRot.Length)
                sb.Append("Δ基=").Append(Quaternion.Angle(t.localRotation, baseRot[index]).ToString("0.00"));
            // ★ 片段参照只有在"这一帧真的从片段取了值"时才有意义 ✗
            // 没有采过片段（非攻击 / 旧路径）时报的是**上一帧残留**的值 ⇒ 会读成"贴住了"✗
            if (withClip && _clipPoseValid && _clipPoseRot != null)
            {
                int k = ClipPoseIndexFor(t);
                if (k >= 0) sb.Append("Δ片段=").Append(Quaternion.Angle(t.localRotation, _clipPoseRot[k]).ToString("0.00"));
            }
        }

        private void AppendFoot(System.Text.StringBuilder sb, string label, Transform[] nodes, int index)
        {
            if (nodes == null || index < 0 || index >= nodes.Length) return;
            Transform t = nodes[index];
            if (t == null || Go == null) return;
            // ★ 参照点必须是**角色自己的摆位节点**（`Go`）✗ 不是 `Transform.root` ★
            // `Transform.root` 是**整条场景层级**的根 —— 玩家被挂在 `WorldView._root` 底下，
            // 那个根在世界原点 ⇒ 读数里会混进"玩家站在地图哪里"（实测 `脚L相对根=(-0.19,-6.88)`，
            // 那个 −6.88 就是玩家自己的 z ✗ 与脚的位置无关）✓
            // ⇒ 用 `Go` 之后读数才是"脚相对身体"，站定 ≈ ±0.2m、迈步时会出现 ±0.4m 级的差 ✓
            Vector3 d = t.position - Go.transform.position;
            sb.Append(' ').Append(label).Append("相对身=(")
              .Append(d.x.ToString("0.00")).Append(',')
              .Append(d.z.ToString("0.00")).Append(')')
              .Append("高=").Append(d.y.ToString("0.00"));
        }

        private static string Euler(Vector3 e)
        {
            // 转成 -180..180，读起来才知道"往前还是往后"
            float x = Mathf.DeltaAngle(0f, e.x), y = Mathf.DeltaAngle(0f, e.y), z = Mathf.DeltaAngle(0f, e.z);
            return "(" + x.ToString("0") + "," + y.ToString("0") + "," + z.ToString("0") + ")";
        }
    }
}
