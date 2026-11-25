using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v203 只读探针：园丁光标（三态 / 热点 / Update 里设 / 只在状态变化时设）★★
    ///
    /// 为什么单独一个探针、不写进 `SelfCheck`：
    ///   本工程早已立过这条规矩（见 `DebugApi` 里三处"为什么不写进 SelfCheck：断言分母被钉在 N 条"）——
    ///   自检的分母是**约定值**，加一条就是基线漂移；而光标这件事还牵着**运行期状态**
    ///   （`Cursor.SetCursor` 真调了几次、焦点进出、悬停判定），那不是"数值对不对"，是"行为对不对" ✓
    ///
    /// 它验三组东西：
    ///   A 资源：三张 PNG 在不在、48×48、alpha 只有 0/255、≤6 色、**导入后仍可读**（Read/Write 开了没）
    ///   B 形状：热点 == 图上的铲尖像素；按下态 == 默认态整张下移 2px（逐像素比）
    ///   C 行为：`GardenCursor` 的实时读数（状态 / 调用次数 / 是否从 OnGUI 误调 / visible / 焦点）
    ///
    /// 还给两个"把悬停这条链真的跑起来"的动作（判据不变，只换鼠标坐标）：
    ///   `PickButton()` 把 `ImGuiHud.ProbeMouse` 放到**本帧真实存在的某个按钮中心**
    ///   `ClearProbe()` 还原成真实鼠标
    /// 以及 `Toggle()` 开关一块 4× 预览叠图（截图用）。
    ///
    /// ★ 只读：不写盘、不改场景、不改玩法状态（`PauseToggle` 会切一次暂停，用完请再切回来）★
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ProbeV203Cursor : MonoBehaviour
    {
        private const int TexSize = 48;

        /// <summary>截图上要画的三态（顺序 = `GardenCursor.State`）。</summary>
        private static readonly GardenCursor.State[] States =
        {
            GardenCursor.State.Default, GardenCursor.State.Hover, GardenCursor.State.Press,
        };
        private static readonly string[] StateNames = { "默认 default", "悬停 hover", "按下 press" };

        private GardenCursor _cursor;
        private bool _show;

        /// <summary>`DebugApi` 有 `AutoCreate` 的先例：探针也要保证 play 模式下存在，
        /// 否则 `unity_drive send` 会报"找不到组件"，而人第一反应会以为是窗口/端口的问题 ✗</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProbeV203Cursor>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProbeV203Cursor");
            go.AddComponent<ProbeV203Cursor>();
        }

        private GardenCursor CursorComp
        {
            get
            {
                if (_cursor == null) _cursor = FindAnyObjectByType<GardenCursor>(FindObjectsInactive.Include);
                return _cursor;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  入口（`unity_drive send ProbeV203Cursor ProbeV203Cursor Run`）
        // ══════════════════════════════════════════════════════════════════
        public string Run() => RunReport();

        public static string RunReport()
        {
            var sb = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(bool ok, string name, string detail)
            {
                if (ok) pass++; else fail++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
            }

            sb.AppendLine("═══ v203 探针（只读）：园丁光标 ═══");
            GardenCursor cur = FindAnyObjectByType<GardenCursor>(FindObjectsInactive.Include);
            if (cur == null)
            {
                sb.AppendLine("（场景里没有 GardenCursor：先 play，`GameBootstrap.Awake` 会挂上）");
                return sb.ToString();
            }

            // ── A 资源 ──
            sb.AppendLine();
            sb.AppendLine("── A 资源（导入后的真实状态，不是磁盘上的文件）──");
            Texture2D[] tex = new Texture2D[3];
            for (int i = 0; i < 3; i++) tex[i] = cur.TextureOf(States[i]);
            int missing = 0;
            for (int i = 0; i < 3; i++)
            {
                if (tex[i] == null)
                {
                    missing++;
                    sb.AppendLine($"     · {States[i]}：**缺图**（Resources.Load 返回 null）");
                    continue;
                }
                sb.AppendLine($"     · {tex[i].name}：{tex[i].width}×{tex[i].height} fmt={tex[i].format}"
                            + $" readable={tex[i].isReadable} filter={tex[i].filterMode} mip={tex[i].mipmapCount}");
            }
            Check(missing == 0, "A1 三张光标贴图都在（Resources.Load 非空）", $"缺 {missing}/3");
            bool sizeOk = true, readOk = true;
            for (int i = 0; i < 3; i++)
            {
                if (tex[i] == null) continue;
                if (tex[i].width != TexSize || tex[i].height != TexSize) sizeOk = false;
                if (!tex[i].isReadable) readOk = false;
            }
            Check(sizeOk, "A2 尺寸都是 48×48（生成器的统一规范）", "");
            Check(readOk, "A3 三张都开了 Read/Write（`Cursor.SetCursor` 的硬要求）",
                readOk ? "否则 SetCursor 不报错也不生效" : "★ 会静默失效，见 UiTextureImport 的 cursor_ 分支");

            // ── B 形状（要读像素，所以必须在 A3 之后）──
            sb.AppendLine();
            sb.AppendLine("── B 形状（从**导入后的贴图**上读像素重算，不是信生成器的自述）──");
            if (readOk)
            {
                bool alphaOk = true, colorOk = true, boxOk = true;
                var detail = new StringBuilder();
                for (int i = 0; i < 3; i++)
                {
                    if (tex[i] == null) continue;
                    var stats = PixelStats(tex[i]);
                    detail.AppendLine($"     · {States[i]}：{stats.Colors} 色，alpha 集合 {{{stats.AlphaList}}}，"
                                    + $"包围盒 {stats.BoxW}×{stats.BoxH}（x {stats.MinX}..{stats.MaxX}，y {stats.MinY}..{stats.MaxY}）");
                    if (!stats.AlphaOnlyHard) alphaOk = false;
                    if (stats.Colors > 6 || stats.HasPureBlack) colorOk = false;
                    if (stats.BoxW < 33 || stats.BoxH < 33) boxOk = false;
                }
                sb.Append(detail);
                Check(alphaOk, "B1 alpha 只有 0/255（硬边像素，没有抗锯齿）", "");
                Check(colorOk, "B2 每张 ≤6 色，且没有纯黑（描边是暖灰 #3A3A38）", "");
                Check(boxOk, "B3 主体包围盒 ≥33px（看得见）", "");
                // 热点 = 铲尖
                Vector2 tip = TipPixel(tex[0]);
                bool tipOk = Mathf.RoundToInt(tip.x) == GardenCursor.HotspotX
                          && Mathf.RoundToInt(tip.y) == GardenCursor.HotspotY;
                Check(tipOk, "B4 热点 (4,6) 就是默认态的铲尖像素（图上最靠前的那个不透明像素）",
                    $"图上读到 ({tip.x},{tip.y})／代码里写 ({GardenCursor.HotspotX},{GardenCursor.HotspotY})");
                // 按下 = 默认整体下移 2px
                Check(PressEqualsDefaultShifted(tex[0], tex[2], GardenCursor.PressDropPixels),
                    "B5 按下态 == 默认态整张下移 2px（逐像素；热点因此不用换）",
                    "这条一红就说明「按下时指针会跳」");
            }
            else
            {
                sb.AppendLine("（跳过 B：贴图不可读 ⇒ 读不了像素。这本身就是 A3 的红）");
            }

            // ── C 行为 ──
            sb.AppendLine();
            sb.AppendLine("── C 行为（运行期读数量出来的）──");
            GameBootstrap boot = FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            ImGuiHud hud = boot != null ? boot.Hud : null;
            sb.AppendLine("     · " + GardenCursor.Status());
            sb.AppendLine($"     · Hud.UiHover={hud?.UiHover} HoverFrame={hud?.HoverFrame} "
                        + $"ButtonRects={ImGuiHud.ButtonRects.Count} ProbeMouse={ImGuiHud.ProbeMouse}");
            Check(GardenCursor.SetCursorFromOnGui == 0,
                "C1 SetCursor 从未在 OnGUI 期间被调用（Event.current != null 的次数为 0）",
                $"次数 {GardenCursor.SetCursorFromOnGui}");
            Check(UnityEngine.Cursor.visible, "C2 Cursor.visible 保持 true（没有把系统光标藏起来）",
                $"visible={UnityEngine.Cursor.visible}");
            Check(GardenCursor.MissingTextures == 0, "C3 加载期没报缺图", $"缺 {GardenCursor.MissingTextures}");

            sb.AppendLine();
            sb.AppendLine($"小计：PASS {pass} ／ FAIL {fail}");
            return sb.ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        //  动作：把悬停这条链真的跑起来（只换鼠标坐标，不换判定）
        // ══════════════════════════════════════════════════════════════════
        /// <summary>挑本帧真实存在的第一个按钮，把探针鼠标放到它中心。返回做了什么。</summary>
        public string PickButton()
        {
            IReadOnlyList<Rect> rects = ImGuiHud.ButtonRects;
            if (rects.Count == 0)
            {
                return "本帧 HUD 里**一个按钮都没有**（战斗阶段本来就没有按钮；先 PauseToggle 打开菜单再试）";
            }
            Rect r = rects[0];
            ImGuiHud.ProbeMouse = r.center;
            return $"探针鼠标 → 按钮 #0 中心 ({r.center.x:0.#},{r.center.y:0.#})　矩形 {r}　共 {rects.Count} 个按钮";
        }

        /// <summary>还原真实鼠标。</summary>
        public string ClearProbe()
        {
            ImGuiHud.ProbeMouse = null;
            return "探针鼠标已清空（回到真实鼠标）";
        }

        /// <summary>切一次暂停（战斗阶段没有按钮，菜单里才有）。</summary>
        public string PauseToggle()
        {
            GameBootstrap boot = FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (boot == null || boot.Session == null) return "没有会话";
            boot.Session.TogglePause();
            return $"暂停切换 → Paused={boot.Session.Paused}";
        }

        /// <summary>读一次当前状态（不跑全套报告）。</summary>
        public string Read()
        {
            GameBootstrap boot = FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            ImGuiHud hud = boot != null ? boot.Hud : null;
            return GardenCursor.Status()
                 + $"\nHud.UiHover={hud?.UiHover} HoverFrame={hud?.HoverFrame} "
                 + $"HoverOnButton={hud?.HoverOnButton} HoverOnTip={hud?.HoverOnTip} "
                 + $"ButtonRects={ImGuiHud.ButtonRects.Count} ProbeMouse={ImGuiHud.ProbeMouse}";
        }

        // ══════════════════════════════════════════════════════════════════
        //  截图叠图：三态 4× 预览 + 实时读数
        // ══════════════════════════════════════════════════════════════════
        public string Toggle()
        {
            _show = !_show;
            return "光标预览叠图 " + (_show ? "开" : "关");
        }

        private void OnGUI()
        {
            if (!_show) return;
            GardenCursor cur = CursorComp;
            if (cur == null) return;

            const int zoom = 4;
            int cell = TexSize * zoom;                 // 192
            float x0 = 24f, y0 = 90f;
            float w = cell * 3 + 48f, h = cell + 116f;

            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.05f, 0.04f, 0.92f);
            GUI.DrawTexture(new Rect(x0 - 12f, y0 - 40f, w + 24f, h + 24f), Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(new Rect(x0, y0 - 34f, w, 24f),
                "v203 园丁光标 三态（4× 放大，最近邻；青框 = 热点 (4,6) 铲尖）", LabelStyle(15, Color.white));
            GUI.Label(new Rect(x0, y0 - 14f, w, 22f),
                GardenCursor.Status(), LabelStyle(13, new Color(0.6f, 1f, 0.85f)));

            for (int i = 0; i < 3; i++)
            {
                Texture2D t = cur.TextureOf(States[i]);
                float cx = x0 + i * (cell + 24f);
                if (t != null)
                {
                    GUI.DrawTexture(new Rect(cx, y0 + 8f, cell, cell), t, ScaleMode.StretchToFill, true);
                }
                else
                {
                    GUI.Label(new Rect(cx, y0 + 8f, cell, cell), "缺图", LabelStyle(16, Color.red));
                }
                // 热点标记
                GUI.color = new Color(0f, 1f, 1f, 0.9f);
                GUI.DrawTexture(new Rect(cx + GardenCursor.HotspotX * zoom, y0 + 8f + GardenCursor.HotspotY * zoom,
                    zoom, zoom), Texture2D.whiteTexture);
                GUI.color = prev;

                bool live = (int)GardenCursor.Applied == (int)States[i];
                GUI.Label(new Rect(cx, y0 + 12f + cell, cell, 20f),
                    (live ? "▶ " : "   ") + StateNames[i], LabelStyle(14, live ? Color.yellow : Color.gray));
            }
            GUI.Label(new Rect(x0, y0 + cell + 62f, w + 40f, 44f),
                $"Cursor.SetCursor 调用 {GardenCursor.SetCursorCalls} 次（只在状态变化时调）｜"
                + $"从 OnGUI 误调 {GardenCursor.SetCursorFromOnGui} 次｜visible={UnityEngine.Cursor.visible}｜"
                + $"focused={Application.isFocused}\n当前贴图 = {GardenCursor.AppliedTexture}",
                LabelStyle(13, new Color(1f, 0.92f, 0.72f)));
        }

        private static GUIStyle _labelStyle;
        private static int _labelSize = -1;
        private static Color _labelColor;

        private static GUIStyle LabelStyle(int size, Color color)
        {
            if (_labelStyle == null || _labelSize != size || _labelColor != color)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true };
                _labelStyle.normal.textColor = color;
                _labelSize = size;
                _labelColor = color;
            }
            return _labelStyle;
        }

        // ══════════════════════════════════════════════════════════════════
        //  像素读数（贴图坐标：GetPixel 的原点在**左下**，报告里一律换成左上）
        // ══════════════════════════════════════════════════════════════════
        private struct Stats
        {
            public int Colors, BoxW, BoxH, MinX, MaxX, MinY, MaxY;
            public bool AlphaOnlyHard, HasPureBlack;
            public string AlphaList;
        }

        private static Stats PixelStats(Texture2D tex)
        {
            var s = new Stats
            {
                MinX = tex.width, MinY = tex.height, MaxX = -1, MaxY = -1,
                AlphaOnlyHard = true,
            };
            var colors = new HashSet<int>();
            var alphas = new HashSet<int>();
            for (int y = 0; y < tex.height; y++)
            {
                for (int x = 0; x < tex.width; x++)
                {
                    Color32 c = tex.GetPixel(x, y);
                    alphas.Add(c.a);
                    if (c.a == 0) continue;
                    if (c.a != 255) s.AlphaOnlyHard = false;
                    colors.Add((c.r << 16) | (c.g << 8) | c.b);
                    if (c.r == 0 && c.g == 0 && c.b == 0) s.HasPureBlack = true;
                    int iy = tex.height - 1 - y;               // 换成左上原点
                    if (x < s.MinX) s.MinX = x;
                    if (x > s.MaxX) s.MaxX = x;
                    if (iy < s.MinY) s.MinY = iy;
                    if (iy > s.MaxY) s.MaxY = iy;
                }
            }
            s.Colors = colors.Count;
            var sb = new StringBuilder();
            foreach (int a in alphas) sb.Append(sb.Length == 0 ? "" : ",").Append(a);
            s.AlphaList = sb.ToString();
            s.BoxW = s.MaxX - s.MinX + 1;
            s.BoxH = s.MaxY - s.MinY + 1;
            return s;
        }

        /// <summary>铲尖 = 沿工具轴最靠前（x+y 最小）的不透明像素，并列取 x 更小的那个。</summary>
        private static Vector2 TipPixel(Texture2D tex)
        {
            int bestSum = int.MaxValue, bestX = int.MaxValue;
            Vector2 best = new Vector2(-1f, -1f);
            for (int y = 0; y < tex.height; y++)
            {
                for (int x = 0; x < tex.width; x++)
                {
                    if (tex.GetPixel(x, y).a == 0) continue;
                    int iy = tex.height - 1 - y;
                    int sum = x + iy;
                    if (sum < bestSum || (sum == bestSum && x < bestX))
                    {
                        bestSum = sum; bestX = x; best = new Vector2(x, iy);
                    }
                }
            }
            return best;
        }

        private static bool PressEqualsDefaultShifted(Texture2D def, Texture2D press, int dy)
        {
            if (def == null || press == null) return false;
            for (int y = 0; y < def.height; y++)          // y 是"左上原点"的图坐标
            {
                for (int x = 0; x < def.width; x++)
                {
                    int sy = y - dy;
                    Color32 want = sy >= 0 && sy < def.height
                        ? (Color32)def.GetPixel(x, def.height - 1 - sy)   // 换回左下原点
                        : new Color32(0, 0, 0, 0);
                    Color32 got = press.GetPixel(x, press.height - 1 - y);
                    if (got.r != want.r || got.g != want.g || got.b != want.b || got.a != want.a)
                        return false;
                }
            }
            return true;
        }
    }
}
