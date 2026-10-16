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
        public string CurrentClipName => CurrentClip != null ? CurrentClip.name : "(无)";

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
        }

        /// <summary>诊断用：把三把武器的攻击片段是否取到报出来（一次性，不刷屏）。</summary>
        public string DescribeAttackClips()
        {
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
            // 判据用实测：网格自身的 Z 尺寸 ≥ 0.05 就说明它已经是米制（草 0.242、树 4.229），
            // 那就把根缩放压回 1；否则是 1/100 存储（角色网格 0.01），保留根上的 100 还原。
            // 两种约定被这个判据干净地分开，且**不依赖任何目录名**。
            float meshZ = MeshRawHeight(view.Go) / Mathf.Max(0.0001f, Mathf.Abs(view.BaseScale.z));
            if (meshZ >= 0.05f)
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
                if (view._hasAnim) view.Play(State.Idle);
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
        /// </summary>
        public void SetNeon(Color color, float widthMeters = 0f)
        {
            if (_renderers == null) return;
            _neon = color;
            // v92：描边加宽 0.035→0.050（需求"加强霓虹光描边"）。
            // 宽度是"加强"最直接的手段——只提亮不提宽，在远处小模型上几乎看不出变化。
            _neonWidth = widthMeters > 0.001f ? widthMeters : Mathf.Clamp(RawHeight * 0.050f, 0.018f, 0.09f);
            PushBlock();
        }

        private Color _tint = Color.white;
        private Color _neon = new Color(0f, 0f, 0f, 0f);
        private float _neonWidth = 0.022f;

        /// <summary>把染色与霓虹一次性写进 MaterialPropertyBlock（逐实例，不污染共享材质）。</summary>
        private void PushBlock()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null) continue;
                _mpb.Clear();
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(TintMulId, _tint);
                _mpb.SetColor(NeonColorId, _neon);
                _mpb.SetFloat(NeonWidthId, _neonWidth);
                r.SetPropertyBlock(_mpb);
            }
        }

        // ──────────────────────────────────────────── 动画
        public void Play(State st)
        {
            if (_state == st) return;
            _state = st;
            _stateTime = 0f;
        }

        /// <summary>强制切状态（受击/死亡这类打断）。</summary>
        public void PlayForced(State st)
        {
            _state = st;
            _stateTime = 0f;
        }

        /// <summary>按游戏时钟推进动画并采样。dt 用固定步长，保证确定性。</summary>
        public void Tick(float dt)
        {
            if (!_hasAnim) return;
            // v172：Walk 吃**移速**倍率（修"移速变了、脚步频率没变"的滑步）
            // v174：Attack 吃**攻速**倍率（修"三把攻速不同、攻击动作却一样长"）
            //   ⇒ 两条都只改**推进速度** ✗ 不改"选哪一段" ✓
            //      ⇒ 与 `WorldView.SyncPlayer` 那条"攻击/切换期间不跑 Walk/Idle 迁移"的
            //        让位守卫**互不冲突**：守卫管选段、这里管速度 ✓
            // 其余状态（Hit / Death / Swap）恒按原速推进 —— 那几段的前摇要看得清 ✓
            float rate = _state == State.Attack ? _attackSpeed
                       : _state == State.Walk ? _playbackSpeed : 1f;
            _stateTime += dt * rate;

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
                t = len > 0.0001f ? Mathf.Repeat(_stateTime, len) : 0f;
            }
            else if (_state == State.Death)
            {
                t = Mathf.Min(_stateTime, len);          // 停住最后一帧
            }
            else
            {
                t = Mathf.Min(_stateTime, len);
                if (_stateTime >= len) Play(State.Idle);  // 一次性动作播完回待机
            }
            clip.SampleAnimation(_animRoot != null ? _animRoot : Go, t);

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
        }
    }
}
