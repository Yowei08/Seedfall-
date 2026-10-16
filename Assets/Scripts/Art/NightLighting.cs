using UnityEngine;
using UnityEngine.Rendering;

namespace Seedfall
{
    /// <summary>
    /// 夜晚光照（v74）。
    ///
    /// 三件事：
    ///   ① **半夜暗光**——不是全黑。月亮当主光：低强度、偏冷蓝，但**开阴影**，
    ///      于是夜里还能靠影子读出物体的立体关系。环境光压到很低但不为零，
    ///      否则暗部纯黑、什么都看不见，那就不是"夜晚"而是"关灯"。
    ///   ② **角色火焰光**——以玩家为中心的一盏暖橙点光，带**火苗抖动**。
    ///      它同时承担玩法作用：夜里唯一稳定的近场光源，玩家自然会把注意力放在自己周围。
    ///   ③ **特效投光池**——子弹、爆炸不去各点一盏灯（数量不可控），
    ///      而是共用一个**固定大小的光池**，谁离相机近谁占用。
    ///
    /// ★ 为什么必须做光池 ★
    /// URP 的"每物体附加光源数"是有限的（管线设置里定，通常 4~8）。
    /// 每颗子弹点一盏灯的话：灯一多，超出的会被静默丢弃——症状是"有的子弹照亮、有的不照亮"，
    /// 而且完全取决于相机位置，极难排查。固定池大小 = 行为可预测、开销可预测。
    /// </summary>
    public class NightLighting : MonoBehaviour
    {
        public static NightLighting Instance { get; private set; }

        /// <summary>同时存在的特效光源数量上限。超过 URP 的每物体上限就会静默丢弃，所以这里要克制。</summary>
        private const int PoolSize = 4;

        private Light _moon;
        private Light _sunset;   // v125：一侧微弱夕阳
        private Light _fire;
        private readonly Light[] _pool = new Light[PoolSize];
        private readonly float[] _poolUntil = new float[PoolSize];

        private float _flickerPhase;
        /// <summary>v125：阻尼后的抖动值（位置/亮度平滑用）。</summary>
        private float _flickerSmoothed;
        private bool _fireInit;

        // ── 傍晚参数（集中在这里，方便一眼调）──
        //
        // v125 改动主题：从"深夜"改成"**半晚**"——需求"半晚暗光环境，但是一侧存在微弱夕阳光（非全局黑）"。
        // 做法不是把整体调亮（那会失去夜的氛围），而是**加一盏低角度的暖色方向光**，
        // 方向与月光**相反**：于是场地一侧是暖橙的余晖、另一侧是冷蓝的月光。
        // 这种"一侧亮一侧暗"才是傍晚的真实观感，也让画面有了方向性。
        private static readonly Color MoonColor = new Color(0.52f, 0.60f, 0.88f);
        private const float MoonIntensity = 0.20f;      // v126：0.34→0.20，环境压暗，把画面让给光效
        private static readonly Color Ambient = new Color(0.09f, 0.09f, 0.13f);   // v126：0.17/0.16/0.21 → 接近黑
        private static readonly Color FireColor = new Color(1f, 0.58f, 0.22f);
        // v125：**放大火焰**（需求"放大玩家的火焰亮光特效"）。
        // 它是玩家唯一的光源，也是"我在哪"的视觉锚点 —— 夜里太小会让玩家找不到自己。
        // v126：环境压暗之后火焰反而要更亮一点，否则玩家自己也没入黑暗里。
        private const float FireRange = 12.0f;          // 7.5 → 11.0 → 12.0
        private const float FireIntensity = 4.0f;       // 2.6 → 3.4 → 4.0

        /// <summary>v125：一侧微弱夕阳。刻意很弱 —— 它的作用是"让画面不全黑、有方向感"，
        /// 不是照亮全场。v126 随环境一起压暗（0.34→0.22），否则它在暗环境里会显得比月光还亮。 </summary>
        private static readonly Color SunsetColor = new Color(1f, 0.62f, 0.34f);
        private const float SunsetIntensity = 0.22f;

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
            // ── 月亮：复用场景里已有的平行光，避免又多一盏主光 ──
            Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional) { _moon = lights[i]; break; }
            }
            if (_moon == null)
            {
                var go = new GameObject("Moon");
                _moon = go.AddComponent<Light>();
                _moon.type = LightType.Directional;
            }
            _moon.color = MoonColor;
            _moon.intensity = MoonIntensity;
            _moon.shadows = LightShadows.Soft;                 // 夜里靠影子读立体关系
            _moon.shadowStrength = 0.72f;
            _moon.shadowBias = 0.05f;
            // 月亮角度：比正午低一些（月牙感），但不贴地平线——太平的话影子会拉得满屏都是
            _moon.transform.rotation = Quaternion.Euler(38f, 152f, 0f);

            // ── v128：强化阴影（需求"强化画面阴影效果，敌人阴影缺失"）──
            //
            // 排查结论：**没有任何代码显式关闭阴影**（全工程搜不到 shadowCastingMode），
            // 所以模型本身是投影的。那"敌人阴影缺失"最可能的原因是**阴影距离太短**：
            // 相机是 60° 俯视、离地 38 米，而 URP 默认的 shadowDistance 大约是 50 米 ——
            // 玩家周围有影子、稍远一点的敌人就落在级联之外，看起来就是"敌人没影子"。
            //
            // 另外把**环境光再压低一点**：阴影的可见度取决于"亮面/暗面"的对比，
            // 环境光越高，阴影越淡。这在上一版压到 0.09 的基础上再收，让影子真正读得出来。
            QualitySettings.shadowDistance = 140f;          // 覆盖整个场地（默认约 50）
            QualitySettings.shadowCascades = 4;             // 4 级联：近处精细、远处不漏
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowProjection = ShadowProjection.StableFit;

            // ── v125：一侧微弱夕阳 ──
            // 方向刻意与月亮（152°）**相反**（305°），于是场地一侧暖、一侧冷。
            // 角度压得很低（14°），余晖才有"斜射"的感觉；太高就变成第二个正午光源。
            var sunGo = new GameObject("SunsetLight");
            sunGo.transform.SetParent(transform, false);
            _sunset = sunGo.AddComponent<Light>();
            _sunset.type = LightType.Directional;
            _sunset.color = SunsetColor;
            _sunset.intensity = SunsetIntensity;
            _sunset.shadows = LightShadows.Soft;
            _sunset.shadowStrength = 0.45f;
            _sunset.shadowBias = 0.05f;
            _sunset.transform.rotation = Quaternion.Euler(14f, 305f, 0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.fog = false;

            // ── 角色火焰 ──
            var fireGo = new GameObject("FireLight");
            fireGo.transform.SetParent(transform, false);
            _fire = fireGo.AddComponent<Light>();
            _fire.type = LightType.Point;
            _fire.color = FireColor;
            _fire.range = FireRange;
            _fire.intensity = FireIntensity;
            _fire.shadows = LightShadows.None;   // 点光阴影很贵，火焰不投影

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
                // 真正提供空间信息的是**月光与夕阳**（两个方向光，各一张图，代价恒定）✓
                // 特效光的影子只闪 0.12 秒，玩家根本来不及读取它 ✓
                l.shadows = LightShadows.None;
                _pool[i] = l;
                _poolUntil[i] = -1f;
            }
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

            _fire.intensity = FireIntensity * (1f + _flickerSmoothed * 0.16f);
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

        private void Update()
        {
            // 过期的灯熄灭（不销毁，池是常驻的）
            float now = Time.time;
            for (int i = 0; i < PoolSize; i++)
            {
                if (_pool[i] == null) continue;
                if (_poolUntil[i] < now && _pool[i].intensity > 0.01f) _pool[i].intensity = 0f;
            }
        }
    }
}
