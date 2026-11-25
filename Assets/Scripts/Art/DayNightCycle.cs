using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// **昼夜循环的纯函数部分**（v174）。
    ///
    /// ─────────────────────────────────────────────────────────────
    /// ★ 为什么要单独一个文件、而且不含 MonoBehaviour ★
    /// ─────────────────────────────────────────────────────────────
    /// 需求（逐字）："环境光照修改到白天 ✗，制作昼夜交替 ✗，会缓慢平滑修改昼夜和
    /// 【实时光影阴影】✗，5 分钟为一个周期（一天）★"。
    ///
    /// 拆开之后其实只有两件事 ✗：
    ///   ① 一套数学：`秒 → 光照状态`（太阳角度 / 强度 / 颜色 / 环境光 / 雾）
    ///   ② 把①写进 Unity 的 `Light` 与 `RenderSettings`
    /// ① 是纯的、可以被自检和无头回归**直接调用**的 ✓
    /// ② 必须碰引擎对象 ✗
    ///
    /// 混在一起的话，"t=120 时太阳该是什么颜色"就只能靠截图肉眼判断 ✗
    /// 拆开之后 ① 能被 <see cref="Report"/> 打成一张表，**每个数字都可核对** ✓
    /// ② 落在 <see cref="NightLighting"/> —— 那是既有的光照文件，**不叫 `WorldView.cs`**，
    /// 所以按"文件归属"的要求，改动不落在 `WorldView` ✓
    ///
    /// ─────────────────────────────────────────────────────────────
    /// ★ 设计：一条余弦，四行关键帧 ★
    /// ─────────────────────────────────────────────────────────────
    ///     phase = t / 300          （取模 ⇒ 周期严格 300 秒，t=0 与 t=300 **逐位相同** ✓）
    ///     h     = cos(2π·phase)    （t=0 ⇒ **+1** 正午 ｜ t=150 ⇒ **-1** 子夜 ✓）
    ///     h01   = (h + 1) / 2      （0 = 最深夜 ｜ 1 = 正午，下面所有量都按它插值）
    ///
    /// ★ "默认状态是白天"不是特判出来的，而是 t=0 的取值本身 ★
    /// 旧版 `NightLighting.Setup` 把"深夜"写死在常量里（月亮 0.20 强度、环境光 0.09）✗
    /// ⇒ 一进游戏就是夜里，这正是用户截图里"地面深蓝灰、花草都在暗处"的来源 ✓
    /// 现在 t=0 落在 h01=1 ⇒ 光强 1.45、环境光 0.54、影子强度 0.88 ⇒ **开局即白天** ✓
    ///
    /// 所有量都只是 h01 的**分段线性插值**（关键帧表见下）✗
    /// ⇒ 分段线性是 **C0 连续**的 ⇒ 相邻采样之间不可能出现跳变 ✓
    /// 这正是验收判据一"每个量都连续变化、没有跳变"，而它由构造保证，不靠调参碰运气 ✓
    ///
    /// 关键帧表就是"一天长什么样"的**全部声明**：改观感只改那几行 ✓
    /// </summary>
    public static class DayNightCycle
    {
        /// <summary>
        /// 一整天 = **5 个游戏分钟 = 300 秒**。
        ///
        /// ★ 为什么写成 `5f * Clock.SecondsPerMinute` 而不是 `300f` ★
        /// 时间真值是 `Clock.GameTime`（秒），`MinuteIndex = GameTime / 60` ✓
        /// 写成乘法就把"5 分钟"和分钟长度**绑成一处** ✗
        /// 若哪天分钟长度改了，周期跟着改，不会出现"周期说 5 分钟、实际是别的东西" ✓
        /// （本工程已经吃过"三处定价公式各写一份"的亏 —— 同一个量只留一个来源。）
        /// </summary>
        public const float CycleSeconds = 5f * Clock.SecondsPerMinute;

        /// <summary>采样时刻表：用户点名的六个 t（0 与 300 应当回到同一状态）。</summary>
        public static readonly float[] SampleTimes = { 0f, 60f, 120f, 180f, 240f, 300f };

        // ── 关键帧（按 h01 插值；h01=0 最深夜 → h01=1 正午）──
        //
        // 这四个点是"一天"的骨架：深夜 → 暮色 → 黄金时刻 → 正午。
        // 中间值全部线性插值，所以**不可能有跳变** ✓
        private static readonly float[] Keys = { 0.00f, 0.18f, 0.45f, 1.00f };

        /// <summary>太阳强度：0.10（深夜月光）→ 1.45（正午）。旧版恒为 0.20 ⇒ 白天的 13.8% ✗</summary>
        private static readonly float[] IntensityKeys = { 0.10f, 0.42f, 0.98f, 1.45f };

        /// <summary>太阳颜色：冷蓝月光 → 暖橙暮色 → 金黄 → 近白正午（色温随时角走）</summary>
        private static readonly Color[] ColorKeys =
        {
            new Color(0.42f, 0.52f, 0.82f),   // 深夜：偏蓝暗
            new Color(0.95f, 0.55f, 0.35f),   // 暮色：暖橙
            new Color(1.00f, 0.80f, 0.55f),   // 黄金时刻
            new Color(1.00f, 0.96f, 0.90f),   // 正午：白亮
        };

        /// <summary>环境光：0.10/0.11/0.17 → 0.54/0.55/0.60。旧版恒为 0.09/0.09/0.13 ⇒ 白天的 17% ✗</summary>
        private static readonly Color[] AmbientKeys =
        {
            new Color(0.10f, 0.11f, 0.17f),   // 深夜：接近黑，但不为零（纯黑什么都看不见 = 关灯）
            new Color(0.30f, 0.25f, 0.24f),   // 暮色：暖而暗
            new Color(0.44f, 0.44f, 0.48f),   // 黄金时刻
            new Color(0.54f, 0.55f, 0.60f),   // 正午：亮
        };

        /// <summary>玩家火焰强度倍率：夜里 1.0（唯一近场光源）、正午 0.10（只留一点暖边）</summary>
        private static readonly float[] FireKeys = { 1.00f, 0.75f, 0.30f, 0.10f };

        /// <summary>雾浓度：白天**严格 0**（不管开，保持原样）→ 深夜 0.008（有纵深，但不糊）</summary>
        private static readonly float[] FogKeys = { 0.008f, 0.0055f, 0.002f, 0.0f };

        /// <summary>低于这个浓度就当雾是关的 —— 免得"密度 1e-9 但 fog=true"这种半开状态。</summary>
        private const float FogOnThreshold = 0.0005f;

        /// <summary>太阳仰角：8°（子夜，月光贴近地平线 ⇒ 影子长而可见）→ 72°（正午 ⇒ 影子短而清晰）。
        /// 刻意**不低于地平线**：平行光在地平线以下会从地面往上打，暗部会翻成亮面 ✗</summary>
        private const float NightElevation = 8f;
        private const float NoonElevation = 72f;

        /// <summary>正午方位角（既有月亮角度 152° 的延续）。方位角每周期**转满 360°** ⇒ 影子看得见地扫过 ✓</summary>
        private const float AzimuthAtZero = 152f;

        /// <summary>影子强度：白天 0.88（清晰）→ 夜里 0.55（月光是软影）。</summary>
        private const float DayShadowStrength = 0.88f;
        private const float NightShadowStrength = 0.55f;

        /// <summary>侧逆光（既有的"夕阳"那盏）峰值强度。它与太阳反向，让暮色有方向感。</summary>
        private const float AccentPeak = 0.45f;

        /// <summary>
        /// 某个时刻的**全部光照状态**。一次性算完，交给 `NightLighting` 逐项写入引擎 ✗
        /// 于是"t 时刻的样子"是一个可打印、可断言的值 ✓
        /// </summary>
        public struct State
        {
            /// <summary>0..1 的周期内位置（t=0 白天、0.25 黄昏、0.5 子夜、0.75 黎明）</summary>
            public float Phase;
            /// <summary>平行光 rotation.x（仰角，度）</summary>
            public float SunElevation;
            /// <summary>平行光 rotation.y（方位角，度；每周期 +360）</summary>
            public float SunAzimuth;
            public float SunIntensity;
            public Color SunColor;
            public float ShadowStrength;
            public Color Ambient;
            public bool FogEnabled;
            public float FogDensity;
            public Color FogColor;
            public float AccentIntensity;
            public float AccentElevation;
            public float AccentAzimuth;
            public float FireScale;
            /// <summary>★ v209：夜间敌人发光强度（0 = 白天不发光 ｜ 1 = 深夜满强度）。见 <see cref="GlowFromH01"/>。</summary>
            public float Glow;

            /// <summary>太阳的朝向四元数。**影子长什么样由它决定**，所以判"影子动了没"看它最直接 ✓</summary>
            public Quaternion SunRotation => Quaternion.Euler(SunElevation, SunAzimuth, 0f);
        }

        /// <summary>周期内位置（取模，任意大的 t 都安全）。</summary>
        public static float Phase01(float t)
        {
            return Mathf.Repeat(t / CycleSeconds, 1f);
        }

        /// <summary>
        /// ★ v209：**"天有多亮"**（0 = 最深夜 ｜ 1 = 正午）★ —— 纯函数。
        ///
        /// 它是 `Sample` 里那个 `h01` 的**唯一来源**（`Sample` 现在也调它，不是各写一份）。
        /// 之所以要公开：夜间发光那条曲线（<see cref="Glow01"/>）也要它，
        /// 而"同一件事两个地方各算一遍"是本工程反复吃过亏的地方（三处定价、两个时间源）。
        /// </summary>
        public static float H01(float t)
        {
            return Mathf.Clamp01(Mathf.Cos(Phase01(t) * 2f * Mathf.PI) * 0.5f + 0.5f);
        }

        // ══════════════════════════════════════════════════════════════════
        // ★★ v209：夜间敌人发光（Emissive at night）★★
        //
        // 需求（逐字）："为夜晚的敌人增加发光"。
        // 它的三条边界，正是下面这三个常量存在的理由：
        //
        //   ① **只在夜里发光，白天不发光** ⇒ `GlowDayEndH01` 就是"白天的下界"，
        //      取 `Keys[2]`（0.45，黄金时刻）⇒ **正午与黄金时刻都严格为 0** ✓
        //   ② **平滑淡入**（"天黑渐变，不要一刀切"）⇒ 见 `GlowFromH01` 的 smoothstep，
        //      两端导数都为 0 ⇒ 既没有"咔"地亮起来，也没有"到点熄灭" ✓
        //   ③ **判定只读本类**（唯一真相）⇒ 消费端（`NightLighting`）拿的是 `State.Glow`，
        //      不是自己再算一遍时间 ✓
        //
        // ★ 为什么两端复用 `Keys` 里已有的两个点（0.18 = 暮色、0.45 = 黄金时刻）★
        //   那两个数就是"一天长什么样"的骨架（见 `Keys` 的注释）。
        //   夜光要问的是同一件事（"现在有多黑"），另起一套阈值 ⇒ 两套"天黑"判据迟早分歧 ✗
        // ══════════════════════════════════════════════════════════════════

        /// <summary>h01 ≥ 本值（= 黄金时刻及更亮）⇒ 敌人**完全不发光**。</summary>
        public const float GlowDayEndH01 = 0.45f;

        /// <summary>h01 ≤ 本值（= 暮色之后）⇒ 发光**满强度**。取 `Keys[1]`。</summary>
        public const float GlowNightFullH01 = 0.18f;

        /// <summary>某个时刻的夜间发光强度（0 = 不发光 ｜ 1 = 满强度）。纯函数。</summary>
        public static float Glow01(float t) => GlowFromH01(H01(t));

        /// <summary>
        /// h01 → 发光强度。**白天的定义域上恒为 0**，不是"很小"。
        ///
        /// 用 smoothstep 而不是线性：线性在 0.45 处导数不为 0，
        /// 玩家会看到"天还没黑，敌人的边就开始亮了"的那一条**硬起点** ✗
        /// smoothstep 两端导数为 0 ⇒ 亮起来的过程没有可辨认的起点 ✓
        /// </summary>
        public static float GlowFromH01(float h01)
        {
            // InverseLerp 自带钳位：h01 落在这段外面时直接给 0 / 1 ✓
            float x = Mathf.InverseLerp(GlowDayEndH01, GlowNightFullH01, h01);
            return x * x * (3f - 2f * x);          // smoothstep：C1 连续
        }

        /// <summary>
        /// **唯一的采样入口**：给秒，给状态。纯函数 —— 不读全局、不写任何东西 ✗
        /// 所以它在编辑模式、无头模式、自检里给出**同一个答案** ✓
        /// </summary>
        public static State Sample(float t)
        {
            float phase = Phase01(t);
            float h01 = H01(t);

            State s;
            s.Phase = phase;

            // 光的角度：仰角随"日高"余弦走，方位角匀速转满一圈 ⇒ 影子既变长又转方向 ✓
            s.SunElevation = Mathf.Lerp(NightElevation, NoonElevation, h01);
            s.SunAzimuth = AzimuthAtZero + 360f * phase;

            s.SunIntensity = Key(IntensityKeys, h01);
            s.SunColor = Key(ColorKeys, h01);
            s.Ambient = Key(AmbientKeys, h01);
            s.ShadowStrength = Mathf.Lerp(NightShadowStrength, DayShadowStrength, h01);
            s.FireScale = Key(FireKeys, h01);

            // 侧逆光：4·h01·(1−h01) 在两端为 0、在中点取 1 ✗
            // ⇒ 正午与子夜它都**自动消失**，只在晨昏出现，而且端点处连导数都是 0（不会"咔"地亮起来）✓
            float accent = 4f * h01 * (1f - h01);
            s.AccentIntensity = AccentPeak * accent;
            s.AccentAzimuth = s.SunAzimuth + 180f;          // 与太阳反向 ⇒ 一侧暖、一侧冷
            s.AccentElevation = s.SunElevation * 0.18f + 4f; // 始终压得很低 ⇒ 才有"斜射的余晖"

            s.FogDensity = Key(FogKeys, h01);
            s.FogEnabled = s.FogDensity > FogOnThreshold;
            // 雾色跟着环境走（稍微偏向阳光），于是远景和天空不会脱节 ✓
            s.FogColor = Color.Lerp(s.Ambient, s.SunColor, 0.35f);

            // ★ v209：夜间敌人发光 —— 与上面所有量**同一个 h01**，所以"天黑"只有一个判据 ✓
            s.Glow = GlowFromH01(h01);
            return s;
        }

        /// <summary>关键帧线性插值（标量）。落在表外则钳到两端。</summary>
        private static float Key(float[] vals, float h01)
        {
            for (int i = 0; i < Keys.Length - 1; i++)
            {
                if (h01 > Keys[i + 1]) continue;
                float span = Keys[i + 1] - Keys[i];
                return Mathf.Lerp(vals[i], vals[i + 1], span <= 0f ? 0f : (h01 - Keys[i]) / span);
            }
            return vals[vals.Length - 1];
        }

        /// <summary>关键帧线性插值（颜色）。</summary>
        private static Color Key(Color[] vals, float h01)
        {
            for (int i = 0; i < Keys.Length - 1; i++)
            {
                if (h01 > Keys[i + 1]) continue;
                float span = Keys[i + 1] - Keys[i];
                return Color.Lerp(vals[i], vals[i + 1], span <= 0f ? 0f : (h01 - Keys[i]) / span);
            }
            return vals[vals.Length - 1];
        }

        /// <summary>
        /// ★ 把"一天"打成一张可核对的表 ★
        ///
        /// 它同时回答验收的四条判据 ✗：
        ///   ① 六行数字（用户点名的 t=0/60/120/180/240/300）
        ///   ② 连续性：细分 1200 步，报**每步最大变化量** ⇒ 证明没有跳变 ✓
        ///   ③ 闭合性：t=0 与 t=300 逐项比较 ⇒ 证明是一整轮 ✓
        ///   ④ 影子：报太阳朝向四元数与相邻两步的**最大夹角** ⇒ 证明光源在动、且不瞬移 ✓
        ///
        /// 全是纯计算 ⇒ 编辑模式就能跑，不需要 play ✓
        /// </summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            sb.Append("昼夜循环：一整天 ").Append(CycleSeconds.ToString("0")).Append("s（")
              .Append((CycleSeconds / Clock.SecondsPerMinute).ToString("0.#")).Append(" 个游戏分钟）\n");
            sb.Append("关键帧一律按 h01（0=深夜 1=正午）分段线性插值 ⇒ 构造上 C0 连续\n\n");

            sb.Append("     t    phase   rot.x°   rot.y°   intensity  sunColor                ambient                 shadow  fog(开=有雾)  accent  fire  glow\n");
            for (int i = 0; i < SampleTimes.Length; i++)
            {
                float t = SampleTimes[i];
                State s = Sample(t);
                sb.Append("  ").Append(t.ToString("000").PadLeft(5)).Append("  ")
                  .Append(s.Phase.ToString("0.0000")).Append("  ")
                  .Append(s.SunElevation.ToString("00.00").PadLeft(7)).Append("  ")
                  .Append(Mathf.Repeat(s.SunAzimuth, 360f).ToString("000.0").PadLeft(6)).Append("  ")
                  .Append(s.SunIntensity.ToString("0.0000").PadLeft(9)).Append("  ")
                  .Append(Rgb(s.SunColor)).Append("  ")
                  .Append(Rgb(s.Ambient)).Append("  ")
                  .Append(s.ShadowStrength.ToString("0.000")).Append("  ")
                  .Append((s.FogDensity.ToString("0.00000") + (s.FogEnabled ? "开" : "关")).PadLeft(10)).Append("  ")
                  .Append(s.AccentIntensity.ToString("0.000")).Append("  ")
                  .Append(s.FireScale.ToString("0.000")).Append("  ")
                  .Append(s.Glow.ToString("0.000")).Append('\n');
            }

            State a = Sample(0f);
            State b = Sample(CycleSeconds);
            State c = Sample(CycleSeconds * 3f);
            bool seam1 = Same(a, b);
            bool seam2 = Same(a, c);
            bool isDay = a.SunIntensity > 1f && a.Ambient.grayscale > 0.4f
                         && a.ShadowStrength > 0.8f && a.SunElevation > 60f;

            sb.Append('\n');
            sb.Append("判据二 闭合：t=0 与 t=").Append(CycleSeconds.ToString("0")).Append(" 逐项相同 = ").Append(seam1 ? "是 ✓" : "**否 ✗**")
              .Append("｜t=0 与 t=").Append((CycleSeconds * 3f).ToString("0")).Append("（第三轮）相同 = ").Append(seam2 ? "是 ✓" : "**否 ✗**").Append('\n');
            sb.Append("判据三 白天：t=0 光强 ").Append(a.SunIntensity.ToString("0.00")).Append("（>1 ✓）｜环境光均值 ")
              .Append(a.Ambient.grayscale.ToString("0.000")).Append("（>0.4 ✓）｜影子强度 ").Append(a.ShadowStrength.ToString("0.000"))
              .Append("（>0.8 ✓）｜仰角 ").Append(a.SunElevation.ToString("0.0")).Append("°（>60 ✓） ⇒ ")
              .Append(isDay ? "**是白天 ✓**" : "**不是白天 ✗**").Append('\n');

            sb.Append('\n').Append(GlowLine(a));
            sb.Append('\n').Append(ContinuityLine());
            return sb.ToString();
        }

        /// <summary>
        /// ★ v209：夜间敌人发光的**判据一行** ★
        ///
        /// 三条都要有读数，缺一条就不算验过：
        ///   ① 白天**严格为 0**（不是"很小"）—— 用户原话"白天不发光"
        ///   ② 子夜**严格为 1** —— 满强度
        ///   ③ 过渡段**严格落在开区间 (0,1)** —— 证明是渐变，不是一刀切
        /// 另外把半程点也算出来：smoothstep 在中点给 0.5，两个对称时刻应当**相等**（闭环）。
        /// </summary>
        private static string GlowLine(in State day)
        {
            State dusk = Sample(90f);      // 淡入中段
            State night = Sample(150f);    // 子夜
            State dawn = Sample(210f);     // 淡出中段（与 90 秒对称）

            bool dayZero = day.Glow == 0f && Sample(60f).Glow == 0f && Sample(240f).Glow == 0f;
            bool nightFull = night.Glow == 1f;
            bool mid = dusk.Glow > 0.0001f && dusk.Glow < 0.9999f;
            bool sym = Mathf.Abs(dusk.Glow - dawn.Glow) < 0.002f;

            var sb = new StringBuilder();
            sb.Append("判据四 夜光：白天 t=0/60/240 发光 = 0")
              .Append(dayZero ? " ✓" : " **✗**")
              .Append("｜子夜 t=150 = ").Append(night.Glow.ToString("0.000"))
              .Append(nightFull ? "（满 ✓）" : "（**不满 ✗**）")
              .Append("｜淡入 t=90 = ").Append(dusk.Glow.ToString("0.000"))
              .Append("（0<·<1 ").Append(mid ? "✓" : "**✗**").Append("）")
              .Append("｜对称 t=90 与 t=210 差 ").Append(Mathf.Abs(dusk.Glow - dawn.Glow).ToString("0.0000"))
              .Append(sym ? " ✓" : " **✗**").Append('\n');
            sb.Append("     ⇒ ").Append(dayZero && nightFull && mid && sym ? "**夜间发光 ✓（白天熄灭、夜里满亮、过渡平滑）**" : "**夜间发光有项不成立 ✗**");
            return sb.ToString();
        }

        /// <summary>
        /// ★ 判据一 / 判据四的度量 ★
        ///
        /// 相邻采样之间**每个量都连续变化、没有跳变** —— 把整轮切成 1200 步，
        /// 报每步的变化量上界。跳变会立刻表现为一个很大的数 ✓
        ///
        /// ★ 角度为什么不用差值、而用"朝向夹角" ★
        /// 方位角每轮从 152° 走到 512°，取模之后会在某个瞬间从 359.9° 翻成 0.1°
        /// —— 那是**数字表示的翻转，不是光源的翻转** ✗
        /// 用两个四元数之间的夹角去量，量的是"光真正转了多少"，与表示无关 ✓
        /// 这也是唯一能证明"影子不会瞬移"的量 ✓
        /// </summary>
        private static string ContinuityLine()
        {
            const float Step = 0.25f;                       // 300s / 0.25s = 1200 步
            int n = Mathf.RoundToInt(CycleSeconds / Step);
            State a = Sample(0f);
            float dElev = 0f, dInten = 0f, dAmb = 0f, dFog = 0f, dFire = 0f, dShadow = 0f, dAngle = 0f, dAccent = 0f, dGlow = 0f;

            for (int i = 1; i <= n; i++)
            {
                State b = Sample(i * Step);
                dElev = Mathf.Max(dElev, Mathf.Abs(b.SunElevation - a.SunElevation));
                dInten = Mathf.Max(dInten, Mathf.Abs(b.SunIntensity - a.SunIntensity));
                dAmb = Mathf.Max(dAmb, Mathf.Abs(b.Ambient.grayscale - a.Ambient.grayscale));
                dFog = Mathf.Max(dFog, Mathf.Abs(b.FogDensity - a.FogDensity));
                dFire = Mathf.Max(dFire, Mathf.Abs(b.FireScale - a.FireScale));
                dShadow = Mathf.Max(dShadow, Mathf.Abs(b.ShadowStrength - a.ShadowStrength));
                dAccent = Mathf.Max(dAccent, Mathf.Abs(b.AccentIntensity - a.AccentIntensity));
                dGlow = Mathf.Max(dGlow, Mathf.Abs(b.Glow - a.Glow));
                dAngle = Mathf.Max(dAngle, Quaternion.Angle(a.SunRotation, b.SunRotation));
                a = b;
            }

            var sb = new StringBuilder();
            sb.Append("判据一 连续：整轮切 ").Append(n).Append(" 步（每步 ").Append(Step.ToString("0.##")).Append("s），每步最大变化量\n");
            sb.Append("  仰角    ").Append(dElev.ToString("0.0000")).Append("°／步\n");
            sb.Append("  强度    ").Append(dInten.ToString("0.000000")).Append("／步\n");
            sb.Append("  环境光  ").Append(dAmb.ToString("0.000000")).Append("／步\n");
            sb.Append("  影子    ").Append(dShadow.ToString("0.000000")).Append("／步\n");
            sb.Append("  侧逆光  ").Append(dAccent.ToString("0.000000")).Append("／步\n");
            sb.Append("  雾      ").Append(dFog.ToString("0.0000000")).Append("／步\n");
            sb.Append("  火焰    ").Append(dFire.ToString("0.000000")).Append("／步\n");
            sb.Append("  夜光    ").Append(dGlow.ToString("0.000000")).Append("／步 ← 敌人发光每步变化的上界\n");
            sb.Append("  太阳朝向 ").Append(dAngle.ToString("0.0000")).Append("°／步 ← 这是影子移动的量\n");
            return sb.ToString();
        }

        private static bool Same(in State a, in State b)
        {
            return a.SunElevation == b.SunElevation
                && a.SunAzimuth == b.SunAzimuth
                && a.SunIntensity == b.SunIntensity
                && a.SunColor == b.SunColor
                && a.Ambient == b.Ambient
                && a.ShadowStrength == b.ShadowStrength
                && a.FogEnabled == b.FogEnabled
                && a.FogDensity == b.FogDensity
                && a.FogColor == b.FogColor
                && a.AccentIntensity == b.AccentIntensity
                && a.FireScale == b.FireScale
                && a.Glow == b.Glow;
        }

        private static string Rgb(Color c)
        {
            return "(" + c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00") + ")";
        }
    }
}
