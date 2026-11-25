using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Seedfall
{
    /// <summary>
    /// 光照总装（v74 建、**v174 改造成昼夜循环**）。
    ///
    /// 四件事：
    ///   ① **昼夜循环**——由 `Clock.GameTime` 驱动，5 分钟一整天。
    ///      太阳（夜里就是月亮，**同一盏灯**）的角度、强度、色温、影子强度、
    ///      环境光与雾全部随时角连续变化。数学在 <see cref="DayNightCycle"/>，本类只负责写进引擎。
    ///   ② **角色火焰光**——以玩家为中心的一盏暖橙点光，带**火苗抖动**。
    ///      亮度随时角伸缩：夜里是唯一稳定的近场光源 ⇒ 满亮；
    ///      正午只剩一点暖边 ⇒ 缩到 10%，否则白天地上顶着一团橙色光斑 ✗
    ///   ③ **晨昏侧逆光**——既有的"夕阳"那盏（v125），现在只在中段出现：
    ///      强度按 `4·h01·(1−h01)` 走 ⇒ 正午与子夜自动为 0，晨昏最强，端点导数也是 0 ✓
    ///   ④ **特效投光池**——子弹、爆炸不去各点一盏灯（数量不可控），
    ///      而是共用一个**固定大小的光池**，谁离相机近谁占用。
    ///
    /// ★ 为什么必须做光池 ★
    /// URP 的"每物体附加光源数"是有限的（管线设置里定，通常 4~8）。
    /// 每颗子弹点一盏灯的话：灯一多，超出的会被静默丢弃——症状是"有的子弹照亮、有的不照亮"，
    /// 而且完全取决于相机位置，极难排查。固定池大小 = 行为可预测、开销可预测。
    ///
    /// ─────────────────────────────────────────────────────────────
    /// ★ v174 到底改掉了什么"把白天变成夜里的东西" ★
    /// ─────────────────────────────────────────────────────────────
    /// 旧 `Setup()` 里有**三行写死的夜里常量**，它们就是用户截图里那片深蓝灰的来源 ✗：
    ///     `_moon.intensity = MoonIntensity;`  // 0.20 —— 只有白天该有的 1.45 的 13.8%
    ///     `_moon.color     = MoonColor;`      // (0.52,0.60,0.88) 偏冷蓝
    ///     `RenderSettings.ambientLight = Ambient;` // (0.09,0.09,0.13) —— 白天该有的 17%
    /// 三行都不再存在：现在它们都是 `DayNightCycle.Sample(t)` 在 **t=0** 的取值 ✓
    /// 于是"开局是白天"不靠特判，靠 t=0 落在正午关键帧上 ✓
    /// </summary>
    public class NightLighting : MonoBehaviour
    {
        public static NightLighting Instance { get; private set; }

        /// <summary>
        /// 同时存在的特效光源数量上限。
        ///
        /// ★ v177：4 → 8 ★
        /// 这个数原来被"URP 每物体附加光源上限 8"这个约束框住（旧注释的原话），
        /// 但**那条约束在当前配置下并不成立**：`PC_Renderer.asset` 的 `m_RenderingMode = 2`，
        /// 也就是 **Forward+**；Forward+ 的附加光源走**簇（cluster）**索引，
        /// 不再有"每物体前 N 盏、超出静默丢弃"的行为 ⇒ 多几盏只会多一点着色开销，
        /// 不会出现"有的子弹照亮、有的不照亮"那种按相机位置随机发生的行为 ✓
        /// 翻倍的理由：需求③"所有特效都要照亮"——光束 / 冲击环 / 挥击弧 / 预警圈
        /// 现在都要占一盏，4 盏在混战里会互相抢占（表现为"这个特效亮、那个不亮"）。
        /// </summary>
        private const int PoolSize = 8;

        private Light _sun;      // v174：主平行光。太阳与月亮是同一盏，避免两盏各投一份影
        private Light _accent;   // v125 的"一侧微弱夕阳"，现在由昼夜循环驱动
        private Light _fire;
        /// <summary>v177：**激光专用光**。见 <see cref="IlluminateBeam"/> 里为什么不走光池。</summary>
        private Light _beam;
        private float _beamUntil = -1f;
        private readonly Light[] _pool = new Light[PoolSize];
        private readonly float[] _poolUntil = new float[PoolSize];

        private float _flickerPhase;
        /// <summary>v125：阻尼后的抖动值（位置/亮度平滑用）。</summary>
        private float _flickerSmoothed;
        private bool _fireInit;

        /// <summary>v174：当前时刻的火焰强度倍率（由昼夜循环给：夜里 1.0、正午 0.10）。
        /// 缓存下来是因为火焰亮度由 <see cref="FollowPlayer"/> 写，而循环在 <see cref="Update"/> 里跑，
        /// 两个回调的先后由引擎决定 —— 缓存一格就与顺序无关 ✓</summary>
        private float _fireScale = 1f;

        /// <summary>v174：上一次写进引擎的 t。用于 <see cref="LiveDump"/> 做"读数 vs 纯函数"的核对 ——
        /// 拿**同一时刻**的期望值比，才不会把"帧间差"误判成"接线错了" ✗</summary>
        private float _lastAppliedT;

        /// <summary>
        /// ★ v175：**仅供自动化取证的调试用时间覆盖**（默认 -1 = 关闭）★
        ///
        /// `-1` ⇒ `Update` 用 `Clock.GameTime` ⇒ **与没有这个开关时逐位相同** ✓
        /// `≥0` ⇒ 用这个秒数代替会话时钟 ⇒ 可以立刻停在任何相位（例：105 黄昏 / 150 夜里）拍图 ✓
        ///
        /// ★ 为什么需要它 ★
        /// 一整轮 300 秒 ⇒ 要看到黄昏/夜里，就得让会话【连续跑 105／150 秒不被打断】✗
        /// 而编辑器是共用的 ⇒ 能不能取到图取决于别人的作息 ✓
        /// 有了它，**观感取图不再取决于墙钟** ✓
        ///
        /// ★ 而它必须【默认关 ✗ 可关】★
        /// 行为是一回事、取证是另一回事：正常 play 时仍然只由 `Clock.GameTime` 驱动 ✓
        /// 用它拍出来的图，**时间是设的、不是自然走到的** —— 名字里必须写明 `forced` ✓
        /// </summary>
        private float _timeOverride = -1f;

        /// <summary>调试用：把昼夜停在指定秒数（任意值都行，超出周期会被取模）。</summary>
        public void SetTimeOverride(float seconds) { _timeOverride = seconds; }

        /// <summary>调试用：关闭覆盖，恢复由 `Clock.GameTime` 驱动（= 默认行为）。</summary>
        public void ClearTimeOverride() { _timeOverride = -1f; }

        /// <summary>
        /// ★★ v198：**外部环境光调暗倍率**（`1` = 完全不干预）★★
        ///
        /// ── 为什么需要这个入口 ──
        ///   "下雨 ⇒ 环境变暗"这个读法要求**整场**一起暗，而本类是环境光唯一的每帧写者
        ///   （`Apply` 里 `RenderSettings.ambientLight = st.Ambient`）。表现层
        ///   （`WorldView.Prog`）上一版**直接写** `RenderSettings.ambientLight` ⇒
        ///   两个写者抢同一个值，谁后写谁赢 ⇒ 实测雨幕出来了、**"环境变暗"没生效** ✗
        ///
        /// ── 它改了什么、没改什么 ──
        ///   改：`Apply` 把昼夜循环给的 `st.Ambient` **乘上这个倍率**再写进引擎。
        ///   没改：昼夜循环本身（`DayNightCycle.Sample` 一个数没动）、太阳/雾/火焰全部原逻辑，
        ///         倍率恒为 `1` 时 `st.Ambient × 1f` **逐位相同** ⇒ 不设它是一个字节都没变 ✓
        ///
        /// ⚠️ 它是 `static`：写侧（`WorldView.Prog`）与读侧（本类实例的 `Apply`）之间
        ///   没有任何引用关系（`WorldView` 不持有 `NightLighting`），传引用会多一条耦合边；
        ///   而本作同一时刻只有一个观感场景 ⇒ 与 `BlockShieldStacks` 同一个口径 ✓
        ///   复位点在 `WorldView.ClearProgVisuals()`（每局收尾）与雨停那一帧 ✓
        /// </summary>
        public static float AmbientDimMultiplier = 1f;

        /// <summary>
        /// ★★ v209：**当前夜间发光强度（0..1）—— 由 `DayNightCycle` 给出，本类是唯一写入方** ★★
        ///
        /// ── 它为什么必须在这里，而不是让 `WorldView` 自己去算 ──
        /// 需求（逐字）："为夜晚的敌人增加发光"，交付判据要求"夜里才发光"。
        /// 而"现在是不是夜里"这件事，本工程**只有一个真相**：`DayNightCycle.Sample(t).Glow`
        /// （它的骨架就是昼夜循环那张关键帧表）✗
        /// 如果 `WorldView` 自己拿 `Clock.GameTime` 再算一遍，就出现了**第二个天黑判据** ——
        /// 本工程已经因为"同一个量两处各算一份"吃过多次亏（三处定价、两个时间源）✓
        /// 所以：`Apply` 每帧把 `st.Glow` **抄到这一个静态字段**，消费端只读它 ✓
        ///
        /// `1` 是在 `Apply(0f, Sample(0f))`（`Setup` 末尾）写进去的 ⇒ **开局即白天 ⇒ 0** ✓
        /// 与 `AmbientDimMultiplier` 同一个口径：表现层与光照层之间不加引用边 ✓
        /// </summary>
        public static float NightGlow01 { get; private set; }

        /// <summary>把颜色按外部倍率压暗（**只乘不改**；倍率 1 时返回逐位相同的颜色）。</summary>
        private static Color Dim(Color c)
        {
            float k = Mathf.Clamp01(AmbientDimMultiplier);
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        // ── 只属于自己的观感参数（时角相关的量都在 DayNightCycle 的关键帧表里）──
        private static readonly Color FireColor = new Color(1f, 0.58f, 0.22f);
        // v125：**放大火焰**（需求"放大玩家的火焰亮光特效"）。
        // 它是玩家夜里的光源，也是"我在哪"的视觉锚点 —— 夜里太小会让玩家找不到自己。
        // v126：环境压暗之后火焰反而要更亮一点，否则玩家自己也没入黑暗里。
        private const float FireRange = 12.0f;          // 7.5 → 11.0 → 12.0
        private const float FireIntensity = 4.0f;       // 2.6 → 3.4 → 4.0（再乘昼夜循环给的倍率）

        /// <summary>v125：一侧微弱夕阳的颜色。刻意偏暖、偏弱 —— 它的作用是"让画面有方向感"，
        /// 不是照亮全场。v174：强度改由昼夜循环给（正午/子夜自动为 0，见 DayNightCycle.AccentPeak）。</summary>
        private static readonly Color AccentColor = new Color(1f, 0.62f, 0.34f);

        public static NightLighting Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("NightLighting");
            Instance = go.AddComponent<NightLighting>();
            Instance.Setup();
            return Instance;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void Setup()
        {
            // ── 主平行光：**太阳与月亮是同一盏灯** ──
            // 复用场景里已有的平行光，避免多一盏主光。
            // v174：从"两盏方向光"收敛成一盏 —— 一盏 = 一张 shadow map，代价恒定，
            // 而且不会在晨昏出现两套方向不同的影子（那正是旧版固定的 305° 夕阳造成的）✓
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional) { _sun = lights[i]; break; }
            }
            if (_sun == null)
            {
                var go = new GameObject("Sun");
                _sun = go.AddComponent<Light>();
                _sun.type = LightType.Directional;
            }
            // ★ 这三行原来是写死的夜里常量（0.20 强度 / 冷蓝 / 38°）✗
            //   现在角度、颜色、强度**全部**由 DayNightCycle 在 Setup 末尾按 t=0 写入 ⇒ 开局即白天 ✓
            _sun.shadows = LightShadows.Soft;                 // 实时光影阴影：全场唯一投影方向光 ✓
            _sun.shadowBias = 0.05f;

            // ── v128：强化阴影（需求"强化画面阴影效果，敌人阴影缺失"）──
            //
            // 排查结论：**没有任何代码显式关闭阴影**（全工程搜不到 shadowCastingMode），
            // 所以模型本身是投影的。那"敌人阴影缺失"最可能的原因是**阴影距离太短**：
            // 相机是 60° 俯视、离地 38 米，而 URP 默认的 shadowDistance 大约是 50 米 ——
            // 玩家周围有影子、稍远一点的敌人就落在级联之外，看起来就是"敌人没影子"。
            //
            // v174：原来这里写着"把环境光再压低一点"（压到 0.09）✗
            // 那一条与"白天"直接冲突 —— 影子淡是因为环境光高，但把环境光压到接近黑
            // 得到的是"关灯"而不是"白天" ✓ 现在这个权衡**随时角走**：
            // 正午环境光 0.54、影子强度 0.88（对比度靠影子强度给，不靠压黑环境）✓
            QualitySettings.shadowDistance = 140f;          // 覆盖整个场地（默认约 50）
            QualitySettings.shadowCascades = 4;             // 4 级联：近处精细、远处不漏
            // ★ v177：写全名 ★ `ShadowResolution` 有两个同名类型 ——
            // `UnityEngine.ShadowResolution`（内置 `QualitySettings.shadowResolution` 用的）与
            // `UnityEngine.Rendering.ShadowResolution`（URP 资产用的）。
            // 本文件同时 `using` 了 `UnityEngine.Rendering[.Universal]` ⇒ 裸写会 CS0104 歧义。
            // 这一行设的是 `QualitySettings` 的输入，所以必须取 **UnityEngine** 的那个。
            // （顺带再强调一次：URP 下 `QualitySettings` 这四个字段**全都不生效**，
            //  真正的阴影开关在 `PC_RPAsset.asset`，见 `ShadowSettingsLine()` 的注释）
            QualitySettings.shadowResolution = UnityEngine.ShadowResolution.High;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;

            // ── v125：一侧微弱夕阳（v174 改为循环驱动）──
            // 方向与太阳**相反**（`AccentAzimuth = 太阳方位 + 180°`），于是场地一侧暖、一侧冷。
            // 角度始终压得很低，余晖才有"斜射"的感觉；太高就变成第二个正午光源 ✓
            //
            // ★ v174：阴影从 Soft 改成 None ★
            // 它的峰值强度只有 0.45，本来就只提供"暖边"，不提供立体信息 ✗
            // 而在"主光收敛成一盏、只吃一份 shadow map"之后，再留一盏投影方向光
            // 只会在晨昏叠出两套方向不同的影子（看起来像重影），还要再吃一张 shadow map ✓
            // 留档：v135 记录过 shadow atlas 溢出 —— 少一个投影光源是往正确方向走 ✓
            var accentGo = new GameObject("AccentLight");
            accentGo.transform.SetParent(transform, false);
            _accent = accentGo.AddComponent<Light>();
            _accent.type = LightType.Directional;
            _accent.color = AccentColor;
            _accent.shadows = LightShadows.None;
            _accent.shadowBias = 0.05f;

            // ── 环境光与雾：**模式**设一次，**数值**每帧由昼夜循环写 ──
            RenderSettings.ambientMode = AmbientMode.Flat;   // Flat ⇒ 只有 ambientLight 一个颜色，可控且便宜
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fog = false;                      // 白天的取值就是"关"，见 DayNightCycle.FogKeys

            // ── 角色火焰 ──
            var fireGo = new GameObject("FireLight");
            fireGo.transform.SetParent(transform, false);
            _fire = fireGo.AddComponent<Light>();
            _fire.type = LightType.Point;
            _fire.color = FireColor;
            _fire.range = FireRange;
            _fire.intensity = FireIntensity;
            _fire.shadows = LightShadows.None;   // 点光阴影很贵，火焰不投影

            // ── 激光专用光（v177）──
            // 为什么不走光池：水壶是**持续激光**，`FireBeam` 每个固定帧发一次（≈60/s），
            // 每次光活 0.12s ⇒ 若走光池，8 个槽会在 8 帧内被它一个人占满，
            // 于是弹幕/爆炸反而抢不到光（表现为"激光很亮、别的特效不亮"）。
            // 单独一盏常驻灯来跟光束，代价恒定、行为可预测 ✓
            var beamGo = new GameObject("BeamLight");
            beamGo.transform.SetParent(transform, false);
            _beam = beamGo.AddComponent<Light>();
            _beam.type = LightType.Point;
            _beam.color = new Color(0.45f, 0.95f, 1f);
            _beam.range = 4f;
            _beam.intensity = 0f;
            _beam.shadows = LightShadows.None;

            // ── 特效光池 ──
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("VfxLight" + i);
                go.transform.SetParent(transform, false);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 4f;
                l.intensity = 0f;
                // v135：**光池阴影关闭**（实测回退）。
                //
                // 演进过程，留档以免以后有人再"优化"回去：
                //   v126  4 盏全开 Soft → shadow atlas 溢出，每帧刷屏，分辨率被降 2~4 倍
                //   v130  降到 2 盏    → 警告仍在（实测报 "12 shadow maps" / "6 shadow maps"）
                //                        说明投影光源比预想的多，减少光池并不能解决
                //   v135  全部关闭     → 见下方实测数据
                //
                // ★ 实测数据（机器人自动战斗，第 1 波进行中）★
                //     开启时：fps 39   drawCalls 4047
                //     基线（无光池阴影）：fps 58   drawCalls 2143
                // 代价是 fps 掉 33%、drawCalls 涨 1904，**而收益只有一个 0.12 秒的瞬时影子** ✗
                //
                // ★ 判断错在哪 ★
                // "环境黑了，影子是唯一能读出空间关系的信息"——这个判断本身是对的，
                // 但我把它当成了"所以必须开"，而没有问"**这一盏**值不值 33% 的帧率" ✗
                // 真正提供空间信息的是**主平行光**（v174 起只有它投影：一盏 = 一张图，代价恒定）✓
                // 特效光的影子只闪 0.12 秒，玩家根本来不及读取它 ✓
                l.shadows = LightShadows.None;
                _pool[i] = l;
                _poolUntil[i] = -1f;
            }

            // ★ 最后一步：按 **t=0** 写一次 ★
            // 这就是"默认状态必须是白天"的落点 —— 不必等第一帧 Update，也**不依赖有没有开局** ✓
            // 为什么这样写：旧版把"夜里"的数值写死在 Setup 的常量里 ⇒ 开局即夜 ✗
            // 现在"初始长什么样"与"第 0 秒长什么样"是**同一个函数**给出的，不可能不一致 ✓
            Apply(0f, DayNightCycle.Sample(0f));
        }

        /// <inheritdoc cref="FollowPlayer(Vector3, float)"/>
        /// <summary>每渲染帧：火焰跟随玩家并抖动。v125：位置与亮度都加了**阻尼**，过渡更平滑。</summary>
        public void FollowPlayer(Vector3 playerPos, float dt)
        {
            if (_fire == null) return;

            // v125：**位置平滑跟随**（需求"增加平滑过度"）。
            // 原来直接把灯吸到玩家身上，玩家一帧瞬移（受击击退、回合重置回中心）灯也跟着跳。
            // 阻尼之后灯是"追上来的"，观感上像一团飘动的火，而不是贴在角色身上的灯泡。
            Vector3 target = playerPos + new Vector3(0f, 0.9f, 0f);
            if (!_fireInit) { _fire.transform.position = target; _fireInit = true; }
            else
            {
                float k = 1f - Mathf.Exp(-9f * Mathf.Max(0f, dt));   // 帧率无关的指数阻尼
                _fire.transform.position = Vector3.Lerp(_fire.transform.position, target, k);
            }

            // 火苗抖动：两个不同频率的正弦叠加，比单一随机数更像"火"（随机数会闪得像坏灯）
            _flickerPhase += dt;
            float f = Mathf.Sin(_flickerPhase * 11.3f) * 0.5f + Mathf.Sin(_flickerPhase * 4.1f) * 0.5f;

            // v125：**亮度也阻尼**。原版把 f 直接乘进 intensity —— 11.3 rad/s 的抖动
            // 在 60 帧下每帧都跳，看上去是"闪"而不是"晃"。阻尼之后高频分量被压掉，
            // 留下低频的呼吸感，这才是火。
            _flickerSmoothed = Mathf.Lerp(_flickerSmoothed, f, 1f - Mathf.Exp(-7f * Mathf.Max(0f, dt)));

            // v174：再乘昼夜循环给的倍率 —— 夜里 1.0（唯一近场光源，满亮）、正午 0.10（只留一点暖边）。
            // 不这么做的话：白天地上会顶着一团 4.0 强度的橙色光斑，与"白天"直接矛盾 ✗
            _fire.intensity = FireIntensity * _fireScale * (1f + _flickerSmoothed * 0.16f);
            _fire.range = FireRange * (1f + _flickerSmoothed * 0.05f);
        }

        /// <summary>
        /// 请求一盏特效光（子弹 / 爆炸 / 激光）。
        /// 池满时**抢占最早过期的那盏**——保证"最新的特效一定看得见"，
        /// 而不是让新特效在池满时被静默丢弃。
        /// </summary>
        public static void Illuminate(Vector3 pos, Color color, float range, float intensity, float seconds = 0.12f)
        {
            NightLighting n = Instance;
            if (n == null) return;

            int slot = 0;
            float oldest = float.MaxValue;
            float now = Time.time;
            for (int i = 0; i < PoolSize; i++)
            {
                if (n._poolUntil[i] < now) { slot = i; break; }      // 有空位直接用
                if (n._poolUntil[i] < oldest) { oldest = n._poolUntil[i]; slot = i; }
            }
            Light l = n._pool[slot];
            l.transform.position = pos;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            n._poolUntil[slot] = now + seconds;
        }

        /// <summary>
        /// ★ v177：激光投光 ★（需求③"所有特效照亮"里原先漏掉的一个）
        ///
        /// 光跟着光束的中点走，射程按光束长度伸缩：
        /// 长激光（水壶 9.5m 射程）点亮一片，短的剑气只照亮脚边。
        /// 与 <see cref="Illuminate"/> 的区别是**它用一盏专用灯**，不占光池 ——
        /// 理由见 `Setup()` 里 `BeamLight` 的注释（持续激光会把 8 个池槽全部霸占）。
        /// </summary>
        public static void IlluminateBeam(Vector3 mid, float length, Color color, float intensity = 2.6f)
        {
            NightLighting n = Instance;
            if (n == null || n._beam == null) return;
            n._beam.transform.position = mid + new Vector3(0f, 0.15f, 0f);
            n._beam.color = color;
            n._beam.intensity = intensity;
            n._beam.range = Mathf.Clamp(2.2f + length * 0.22f, 2.5f, 7.5f);
            // 激光每 1/60 秒重发一次；0.15 秒的存活让连续发射看起来是**恒亮**而不是闪
            n._beamUntil = Time.time + 0.15f;
        }

        private void Update()
        {
            // 过期的灯熄灭（不销毁，池是常驻的）
            float now = Time.time;
            if (_beam != null && _beamUntil < now && _beam.intensity > 0.01f) _beam.intensity = 0f;
            for (int i = 0; i < PoolSize; i++)
            {
                if (_pool[i] == null) continue;
                if (_poolUntil[i] < now && _pool[i].intensity > 0.01f) _pool[i].intensity = 0f;
            }

            // ── v174：昼夜循环 ──
            // 时间真值只有一个：`Clock.GameTime`（秒）。**本类不自己累加 dt** ✗
            //     GameTime ──DayNightCycle.Sample──▶ State ──Apply──▶ Light / RenderSettings
            // 顺带得到两个正确的行为（都不是特意写的）：
            //   · 暂停时 `RunSession.FixedTick` 早退 ⇒ GameTime 不涨 ⇒ 昼夜**跟着暂停** ✓
            //   · 商店阶段 `RunTime` 照涨（既有行为，不在本项内）⇒ 昼夜照走 ✗ —— 这是既有语义的延续 ✓
            Clock clock = ResolveClock();
            // 默认（_timeOverride < 0）：完全由会话时钟驱动 ✓
            // 取证时（>= 0）：用设的秒数，好让观感取图不取决于"会话能不能连续跑 150 秒"✓
            float t = _timeOverride >= 0f ? _timeOverride : (clock != null ? clock.GameTime : 0f);
            Apply(t, DayNightCycle.Sample(t));
        }

        /// <summary>
        /// 找到**当前这一局**的时钟。
        ///
        /// ★ 为什么每帧去取，而不是启动时存一个 ★
        /// `GameBootstrap.StartRun` 每次都 `Session = new RunSession()` ✗
        /// 而 `RunSession.Clock` 是 `readonly Clock`（构造时新建）⇒ **每局换一个 Clock 对象** ✓
        /// 若在 `Awake` 时抓一个存起来，第二局起就会读到**上一局的时钟** ——
        /// 症状是"重开之后昼夜停在上一局结束的时刻"，而且极难联想到原因 ✗
        /// 每帧向 `GameBootstrap.Live` 取当前会话，取到的必然是当前那个 ✓
        /// （`DebugApi.Boot` 早就是这个写法，这里沿用既有机制，**不新增全局状态** ✓）
        ///
        /// ★ 没有会话 ⇒ null ⇒ 上层按 t=0 ⇒ 白天 ★
        /// 所以"进游戏之前""开局第一帧""场景里没有 bootstrap"三种情况都是白天，不需要任何特判 ✓
        /// </summary>
        private static Clock ResolveClock()
        {
            var live = GameBootstrap.Live;
            for (int i = 0; i < live.Count; i++)
            {
                GameBootstrap b = live[i];
                if (b == null) continue;
                RunSession s = b.Session;
                if (s != null) return s.Clock;
            }
            return null;
        }

        /// <summary>
        /// 把某一时刻的状态**写进引擎**。
        ///
        /// 每帧全量写，不做"变了才写"的优化 ✗
        /// 理由：值本身连续（见 <see cref="DayNightCycle"/> 的构造），
        /// **全量写与增量写在画面上完全等价**，而"变了才写"要多一份缓存 + 一处比较 ——
        /// 多出来的这两样都会成为将来不一致的来源 ✓
        /// 代价是每帧几次属性赋值，相对一次渲染可以忽略 ✓
        /// </summary>
        private void Apply(float t, in DayNightCycle.State st)
        {
            _fireScale = st.FireScale;      // 给 FollowPlayer 用（它由 WorldView 调，与本方法的先后不固定）
            _lastAppliedT = t;
            // ★ v209：夜间发光的**唯一发布点**（值直接来自 `DayNightCycle`，不在这里重算）★
            NightGlow01 = st.Glow;

            if (_sun != null)
            {
                _sun.transform.rotation = st.SunRotation;   // ★ 影子的"因"：角度每帧都在动 ⇒ 影子看得见地移动 ✓
                _sun.color = st.SunColor;
                _sun.intensity = st.SunIntensity;
                _sun.shadows = LightShadows.Soft;           // 实时光影阴影：始终开着，不随时角关掉 ✓
                _sun.shadowStrength = st.ShadowStrength;
            }

            if (_accent != null)
            {
                _accent.transform.rotation = Quaternion.Euler(st.AccentElevation, st.AccentAzimuth, 0f);
                _accent.intensity = st.AccentIntensity;
            }

            // ★ v198：环境光经过**外部倍率**（雨天变暗）。倍率 1 ⇒ 与改前逐位相同 ✓
            RenderSettings.ambientLight = Dim(st.Ambient);
            RenderSettings.fog = st.FogEnabled;
            RenderSettings.fogColor = st.FogColor;
            RenderSettings.fogDensity = st.FogDensity;
        }

        /// <summary>
        /// ★ 活的读数：从**引擎对象**上读回来 ★
        ///
        /// `DayNightCycle.Report()` 证明"某时刻**该**是什么" ✗
        /// 这个方法证明"**真的写进去了**" ✓ —— 是两个不同的断言，不能互相代替 ✓
        ///
        /// 它拿 `_lastAppliedT` 时刻的期望值去比，于是比较的是**同一时刻**，
        /// 不会把"这次调用与上次 Apply 之间的帧间差"误判成"接线错了" ✓
        /// </summary>
        public static string LiveDump()
        {
            NightLighting n = Instance;
            if (n == null) return "LIGHT LiveDump：NightLighting 未创建（buildVisuals=false，或还没走 Awake）";

            Clock clock = ResolveClock();
            float nowT = clock != null ? clock.GameTime : 0f;
            float at = n._lastAppliedT;
            DayNightCycle.State want = DayNightCycle.Sample(at);

            var sb = new StringBuilder();
            sb.Append("LIGHT LiveDump（从引擎对象读回，不是重算一遍）\n");
            sb.Append("  会话时间 t=").Append(nowT.ToString("0.00")).Append("s｜本次写入用的 t=")
              .Append(at.ToString("0.00")).Append("s｜相位 ").Append(DayNightCycle.Phase01(at).ToString("0.0000"))
              .Append("｜时间源=").Append(n._timeOverride >= 0f
                  ? "**调试覆盖 t=" + n._timeOverride.ToString("0.0") + "s（不是自然走到的 ✗ 仅供观感取证）**"
                  : (clock != null ? "Clock.GameTime ✓" : "**无会话 ⇒ 按 t=0**")).Append('\n');

            float dirErr = float.NaN;
            if (n._sun != null)
            {
                Vector3 e = n._sun.transform.rotation.eulerAngles;
                // 角度差用**朝向向量**量，不用 eulerAngles 相减 ——
                // eulerAngles 会把 152° 与 512° 归一化成同一个数，直接相减会得到假的 360 跳变 ✗
                dirErr = Vector3.Angle(n._sun.transform.forward, want.SunRotation * Vector3.forward);
                sb.Append("  主平行光 rot=(").Append(e.x.ToString("0.00")).Append(", ").Append(e.y.ToString("0.00"))
                  .Append(", ").Append(e.z.ToString("0.00")).Append(")°")
                  .Append(" intensity=").Append(n._sun.intensity.ToString("0.0000"))
                  .Append(" color=").Append(Rgb01(n._sun.color))
                  .Append(" shadows=").Append(n._sun.shadows)
                  .Append(" strength=").Append(n._sun.shadowStrength.ToString("0.000")).Append('\n');
            }
            else sb.Append("  主平行光 **缺失 ✗**\n");

            sb.Append("  环境光 ambientLight=").Append(Rgb01(RenderSettings.ambientLight))
              .Append(" mode=").Append(RenderSettings.ambientMode)
              // ★ v198：把**外部倍率**也打出来 —— 否则"环境光比 DayNightCycle 小"看着像接线错了
              .Append("｜外部倍率=").Append(AmbientDimMultiplier.ToString("0.000"))
              .Append("（1 = 无覆盖；<1 = 某个表现层在压暗）")
              .Append("｜雾 on=").Append(RenderSettings.fog)
              .Append(" mode=").Append(RenderSettings.fogMode)
              .Append(" density=").Append(RenderSettings.fogDensity.ToString("0.000000"))
              .Append(" color=").Append(Rgb01(RenderSettings.fogColor)).Append('\n');

            if (n._accent != null)
                sb.Append("  晨昏侧逆光 intensity=").Append(n._accent.intensity.ToString("0.000"))
                  .Append(" rot=(").Append(n._accent.transform.rotation.eulerAngles.x.ToString("0.00")).Append(", ")
                  .Append(n._accent.transform.rotation.eulerAngles.y.ToString("0.00")).Append(", ")
                  .Append(n._accent.transform.rotation.eulerAngles.z.ToString("0.00")).Append(")°\n");

            if (n._fire != null)
                sb.Append("  玩家火焰 intensity=").Append(n._fire.intensity.ToString("0.000"))
                  .Append(" range=").Append(n._fire.range.ToString("0.00"))
                  .Append("（倍率 ").Append(n._fireScale.ToString("0.000")).Append("，由循环给）\n");

            sb.Append("  ").Append(ShadowSettingsLine()).Append('\n');

            // ★ v198：比对用的期望值也要过**同一个倍率** ★
            //   否则雨天（倍率 0.45）一开，这里就会报"不一致 ✗" —— 那是**假警报**，
            //   而更糟的是：真假接线错误会被这条假警报埋在同一个红里 ✗
            float aErr = ColorDist(RenderSettings.ambientLight, Dim(want.Ambient));
            bool ok = n._sun != null
                      && dirErr < 0.05f
                      && Mathf.Abs(n._sun.intensity - want.SunIntensity) < 0.002f
                      && ColorDist(n._sun.color, want.SunColor) < 0.005f
                      && aErr < 0.005f
                      && Mathf.Abs(RenderSettings.fogDensity - want.FogDensity) < 0.0005f
                      && RenderSettings.fog == want.FogEnabled;
            sb.Append("  ⇒ 引擎里的值 vs DayNightCycle 在 t=").Append(at.ToString("0.00")).Append("s 的期望値：")
              .Append(ok ? "**一致 ✓**" : "**不一致 ✗**")
              .Append("（角度差 ").Append(dirErr.ToString("0.0000")).Append("°，环境光差 ").Append(aErr.ToString("0.0000")).Append("）");
            return sb.ToString();
        }

        /// <summary>
        /// ★ 自动化入口：一次调用把两件事都打出来 ★
        ///   ① `DayNightCycle.Report()` —— **该是什么**（纯函数，含连续性/闭合性/白天三组判据）
        ///   ② `LiveDump()` —— **实际是什么**（从引擎对象读回）
        ///
        /// ★ 为什么是实例方法而不是静态 ★
        /// 桥的 `send` 只能调**组件上的公共实例方法**（它先按路径找 GameObject，再找组件）✗
        /// 这是既有约束，不是设计选择 —— 换个调用方就能变回静态 ✓
        /// </summary>
        public string LightReport()
        {
            return DayNightCycle.Report() + "\n" + LiveDump();
        }

        /// <summary>
        /// ★ v177：**真正生效**的阴影参数在 RPAsset 上，不在 `QualitySettings` 上 ★
        ///
        /// 这是本工程踩了很久的一个坑，写清楚以免再有人照错的字段调：
        /// URP **完全忽略** `QualitySettings.shadowDistance / shadowCascades / shadowResolution /
        /// shadowProjection` —— 它只读 `UniversalRenderPipelineAsset` 自己的那几个字段。
        /// 旧版 `Setup()` 把 `QualitySettings.shadowDistance` 调到 140，并据此写下注释
        /// "阴影距离已经覆盖整个场地，所以敌人该有影子了" —— 那是一个**指向错误字段的结论** ✗
        /// 真正决定阴影的只有两件事：
        ///   ① `Assets/Settings/PC_RPAsset.asset` 的
        ///      `m_ShadowDistance / m_ShadowCascadeCount / m_MainLightShadowmapResolution / 软阴影`
        ///   ② **shader 里有没有 `LightMode=ShadowCaster` 的 Pass**（没有就一个影子都没有）
        /// 本方法只**读** RPAsset，为的是让 `LiveDump` 报出引擎真用的值。
        /// </summary>
        public static string ShadowSettingsLine()
        {
            UniversalRenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            string tail = "｜QualitySettings（URP 下**无效**，仅留档）distance="
                        + QualitySettings.shadowDistance.ToString("0")
                        + " cascades=" + QualitySettings.shadowCascades;
            if (rp == null) return "阴影（URP 资产）：**没找到 URP 资产 ✗**" + tail;
            return "阴影（URP 资产，真正生效）distance=" + rp.shadowDistance.ToString("0")
                 + " cascades=" + rp.shadowCascadeCount
                 + " mainMapRes=" + rp.mainLightShadowmapResolution
                 + " soft=" + rp.supportsSoftShadows
                 + " bias=" + rp.shadowDepthBias.ToString("0.00") + "/" + rp.shadowNormalBias.ToString("0.00")
                 + "｜MSAA=" + rp.msaaSampleCount + " renderScale=" + rp.renderScale.ToString("0.00")
                 + " HDR=" + rp.supportsHDR
                 + tail;
        }

        /// <summary>
        /// ★ 需求③「所有特效照亮」的**读数判据** ★
        ///
        /// 判据不是"我觉得亮了"，而是：
        ///   · 光池里**此刻有几盏 intensity &gt; 阈值**
        ///   · 它们的总强度 / 最远射程
        ///   · 池里是否有灯**在特效已经消失后还亮着**（那是 bug：会在地上留一块常亮光斑）
        /// 调用方式与其它调试入口一致：桥的 `/input` → `DebugApi` 上转一次即可
        /// （`NightLighting` 是场景里的组件，桥可以直接 `send` 到它身上）。
        /// </summary>
        public string VfxLightReport()
        {
            NightLighting n = Instance;
            if (n == null) return "VFX-LIGHT：NightLighting 未创建（buildVisuals=false？）";
            float now = Time.time;
            int lit = 0, stale = 0;
            float sumI = 0f, maxRange = 0f;
            var sb = new StringBuilder();
            sb.Append("VFX-LIGHT 光池 size=").Append(PoolSize).Append("（Forward+ 簇光照，不再是每物体 8 盏上限）\n");
            for (int i = 0; i < PoolSize; i++)
            {
                Light l = n._pool[i];
                if (l == null) { sb.Append("  [").Append(i).Append("] 缺失 ✗\n"); continue; }
                bool on = l.intensity > 0.01f;
                bool expired = n._poolUntil[i] < now;
                if (on) { lit++; sumI += l.intensity; if (l.range > maxRange) maxRange = l.range; }
                if (on && expired) stale++;
                sb.Append("  [").Append(i).Append("] ")
                  .Append(on ? "亮" : "灭").Append(" intensity=").Append(l.intensity.ToString("0.000"))
                  .Append(" range=").Append(l.range.ToString("0.00"))
                  .Append(" 剩余=").Append((n._poolUntil[i] - now).ToString("0.000")).Append('s')
                  .Append(expired && on ? "  ← **过期未灭 ✗**" : "")
                  .Append('\n');
            }
            sb.Append("  ⇒ 正在照亮的特效光源 ").Append(lit).Append('/').Append(PoolSize)
              .Append("｜总强度 ").Append(sumI.ToString("0.00"))
              .Append("｜最远射程 ").Append(maxRange.ToString("0.00")).Append("m");
            if (stale > 0) sb.Append("｜**过期未灭 ").Append(stale).Append(" 盏 ✗**");
            return sb.ToString();
        }

        private static float ColorDist(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        }

        private static string Rgb01(Color c)
        {
            return "(" + c.r.ToString("0.000") + "," + c.g.ToString("0.000") + "," + c.b.ToString("0.000") + ")";
        }
    }
}
