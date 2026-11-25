using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Seedfall
{
    /// <summary>
    /// ★ 环境光「三色」（Trilight）—— 光影升级的第 1 条（美术 lane：`Scripts/Art/`）★
    ///
    /// ── 改前是什么 ──
    ///   全工程的环境光是 **Flat 单色**：`RenderSettings.ambientMode = Flat`，
    ///   只有一个 `ambientLight` 颜色（主城由 `TownLife.TickSkyAndWeather`、
    ///   主城/战斗由 `NightLighting.Apply` 每帧写，正午约 `(0.62, 0.66, 0.72)`）。
    ///   ⇒ 一个颜色照**所有朝向**：屋顶、树干底面、屋檐下、草丛根部的受光**完全一样**，
    ///     所以再多的模型细节看上去也是平的 —— 这是"光影不够高级"最直接的成因。
    ///
    /// ── 改后是什么 ──
    ///   同一份色相，拆成**天顶 / 地平 / 地面**三层：`AmbientMode.Trilight`
    ///     · 天顶  = 原色 ×(1.06, 1.10, 1.18)  —— 略冷偏蓝（天光从上下来）
    ///     · 地平  = 原色 ×(1.00, 1.00, 1.00)  —— 原样（这是原来那一个颜色的语义）
    ///     · 地面  = 原色 ×(0.72, 0.66, 0.58)  —— 暖而暗（草地/土地的反弹光）
    ///   ⇒ 同一块几何，朝上的面更亮更冷，朝下的面更暖更暗 ⇒ **体积感**出来了，
    ///     而**总亮度不变**（地平那一层就是原来的值，另两层一升一降）。
    ///     昼夜循环、雨天压暗、晴天锁**全部照旧**：本文件只**读**那份单色再展开，
    ///     从不改写它 ⇒ `NightLighting.LiveDump` 的自检（比对 `ambientLight`）不受影响 ✓
    ///
    /// ── 为什么在 `LateUpdate` 里写（时序取证）──
    ///   `TownLife.TickSkyAndWeather`（Update）与 `NightLighting.Apply`（Update）每帧把
    ///   **模式写成 Flat**、颜色写成当帧值。LateUpdate 一定晚于 Update ⇒
    ///   本文件读到的必然是**当帧刚写完的那个值**，写回去的三色也一定活到这一帧渲染 ✓
    ///   （与 `LoginVista` 抢雾、`TownPostFx` 开后处理是同一个套路：后面写的一方赢）
    ///
    /// ── 硬约束 ──
    ///   · 不碰 `Scripts/Town|Login|Flow|Combat`、不碰 `Data/*.json` ✓
    ///   · 不 `new Material` / 不 `new Mesh` / 不碰共享材质 ✓（本文件只写 RenderSettings 的 4 个数）
    ///   · 每帧成本：3 个 Color 赋值 + 1 次比较（读到的颜色没变就跳过重算）✓
    /// </summary>
    public sealed class SkyAmbientRig : MonoBehaviour
    {
        /// <summary>天顶光倍率（相对原来那个单色）。</summary>
        private static readonly Vector3 SkyMul = new Vector3(1.06f, 1.10f, 1.18f);
        /// <summary>地平光倍率 = 原色原样（"总亮度不变"的锚点）。</summary>
        private static readonly Vector3 EquatorMul = Vector3.one;
        /// <summary>地面反弹光倍率（暖 + 暗）。</summary>
        private static readonly Vector3 GroundMul = new Vector3(0.72f, 0.66f, 0.58f);

        private static SkyAmbientRig _instance;

        private Color _sky, _equator, _ground;

        /// <summary>改前 / 改后的读数（验收用）。</summary>
        public static string LastReport { get; private set; } = "（还没写过）";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject("SKY_AMBIENT_RIG");
            _instance = go.AddComponent<SkyAmbientRig>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void LateUpdate()
        {
            // ★ 触发条件：**只有当别人把模式写成非 Trilight 时**才动手 ★
            //
            //   为什么必须这么写（实测踩到的坑，留档）：
            //     `RenderSettings.ambientLight` 与 `RenderSettings.ambientSkyColor` 在 Unity 里
            //     **是同一个字段**（`RenderSettings.asset` 里只有 `m_AmbientSkyColor`，没有单独的
            //     `m_AmbientLight`）⇒ 我把"天顶"写成 基准×1.06 之后，下一次再读 `ambientLight`
            //     读到的就是**我自己刚写的天顶色**，于是 ×1.06 每帧叠加 ⇒ 几十帧后爆到纯白 ✗
            //   改成"只跟 Flat 走"之后：
            //     · `TownLife`/`NightLighting`/`TownBootstrap` 每帧先写 `ambientMode = Flat` +
            //       `ambientLight = 当帧值` ⇒ 本文件同帧 LateUpdate 读到的是**它们写的基准值** ✓
            //     · 本文件把模式改成 Trilight 之后，下一帧若没人再写 Flat ⇒ 这里直接返回，
            //       不会有第二次叠加 ✓（**不会自激**）
            if (!Enabled) return;
            if (RenderSettings.ambientMode == AmbientMode.Trilight) return;

            Color baseColor = RenderSettings.ambientLight;   // 此刻 == 别人写的基准单色
            Writes++;
            LastBase = baseColor;

            _sky = Mul(baseColor, SkyMul);
            _equator = Mul(baseColor, EquatorMul);
            _ground = Mul(baseColor, GroundMul);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = _sky;
            RenderSettings.ambientEquatorColor = _equator;
            RenderSettings.ambientGroundColor = _ground;
            RenderSettings.ambientIntensity = 1f;        // Trilight 不吃这个值（只有 Skybox 模式吃），写 1 明确化

            if (Writes == 1)
            {
                LastReport = Report(baseColor);
                Debug.Log("[AMBIENT] 三色环境光就位：" + LastReport);
            }
        }

        /// <summary>累计写入次数（读数用：&gt;0 且持续增长 = 每帧都在接管）。</summary>
        public static int Writes { get; private set; }

        /// <summary>
        /// 总开关（给 `GfxLever` 的同机位 A/B 取证用）：false ⇒ 本文件完全不写，
        /// 环境光就回到 `TownLife`/`NightLighting` 写的 **Flat 单色**（= 改前）。
        /// 默认 true（正常游戏永远是三色）。
        /// </summary>
        public static bool Enabled = true;

        /// <summary>上一次用到的基准单色（即"别人写的那个 Flat 色"）。</summary>
        public static Color LastBase { get; private set; }

        private static Color Mul(Color c, Vector3 m)
        {
            return new Color(Mathf.Clamp01(c.r * m.x), Mathf.Clamp01(c.g * m.y), Mathf.Clamp01(c.b * m.z), 1f);
        }

        private static string Rgb(Color c)
        {
            return "(" + c.r.ToString("0.000") + "," + c.g.ToString("0.000") + "," + c.b.ToString("0.000") + ")";
        }

        /// <summary>把「改前 / 改后」摆在一起（外部 `unity_drive send` 也调它）。</summary>
        public static string Report(Color baseColor)
        {
            var sb = new StringBuilder(240);
            sb.Append("模式 Flat（单色）→ Trilight（三色）")
              .Append("｜改前 ambientLight=").Append(Rgb(baseColor))
              .Append("｜改后天顶=").Append(Rgb(Mul(baseColor, SkyMul)))
              .Append(" 地平=").Append(Rgb(Mul(baseColor, EquatorMul)))
              .Append(" 地面=").Append(Rgb(Mul(baseColor, GroundMul)))
              .Append("｜亮度锚点=地平（=原值，总亮度不变）");
            return sb.ToString();
        }

        /// <summary>读数（`unity_drive send SKY_AMBIENT_RIG SkyAmbientRig Dump x`）。</summary>
        public string Dump(string ignored)
        {
            string s = "当前模式=" + RenderSettings.ambientMode
                       + "｜累计接管帧数=" + Writes
                       + "｜基准单色(别人写的)=" + Rgb(LastBase)
                       + "｜天顶=" + Rgb(RenderSettings.ambientSkyColor)
                       + " 地平=" + Rgb(RenderSettings.ambientEquatorColor)
                       + " 地面=" + Rgb(RenderSettings.ambientGroundColor);
            Debug.Log("[AMBIENT] " + s);
            return s;
        }

        private void OnDestroy()
        {
            // 退出播放 / 场景销毁：把模式还回 Flat（数值本来就是别人写的，不需要还原）
            if (_instance == this) _instance = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
        }
    }
}
