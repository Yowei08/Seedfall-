using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 玩家（豆豆人）的**验收台**（v198）：把同一个模型按不同状态/不同武器摆成一排，
    /// 把某一只钉在某一帧姿态上，再把画面**同一个主线程调用**里渲染落盘 ★
    ///
    /// 为什么要有它（而不是"进游戏里截一张"）：
    ///   · 五个状态（走路/待机/挥击/受击/闪避）在实机里**不会同时出现**，
    ///     而验收要的是一张能并排比的图 ⇒ 只能是纯表现层的摆样 ✓
    ///   · 挥击的"那一帧"必须可重复：`Pose("attack-hoe:0.45")` 把 `u` 钉死，
    ///     否则同一段动作每次拍到的是不同时刻，改前/改后没法比 ✓
    ///   · 三把武器的握法要能同框对照 ⇒ 一格一把 ✓
    ///
    /// 与 `BugShowcase` 同一套自举方式（`RuntimeInitializeOnLoadMethod` + 一个 GameObject），
    /// 由桥的 `/input`（`action=send, path=PlayerShowcase, method=Show/Pose/Focus/Snap/Hide/Report`）驱动。
    /// **不占用任何玩法状态、不写任何玩法字段** ⇒ 关掉它之后游戏与从前一模一样 ✓
    ///
    /// ★ 抓图为什么不用桥的 `/capture` ★
    /// `/capture` 是"桥线程 → 主线程排一次 Render"，中间隔着若干帧，
    /// 而这台编辑器上还有别的自动化在摆自己的机位（实测被抢过）✗
    /// 这里把"摆机位 + Render + 回读"放在**同一个主线程调用**里 ⇒ 中间不可能有人插进来 ✓
    /// 回读走 `RenderTexture` + `ReadPixels`，**不用** `ScreenCapture.CaptureScreenshot(路径)`
    ///（那个依赖编辑器窗口获得焦点，本机上会拍出全黑的图）✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerShowcase : MonoBehaviour
    {
        /// <summary>
        /// 摆样清单：格子 id → (姿态名, 武器索引)。**顺序即从左到右**。
        /// 一格一个"状态 × 武器"，覆盖验收要的五态与三把武器 ✓
        /// </summary>
        private static readonly string[] SlotIds =
        {
            "idle", "walk", "attack_hoe", "attack_can", "attack_fert", "hit", "evade", "swap",
        };

        /// <summary>摆样的世界横向间距（米）。豆豆人最宽处是帽檐 0.68m×缩放，放大后留 1.9m 够看情楚。</summary>
        private const float SlotGap = 1.90f;

        /// <summary>摆样的显示放大：与游戏里 `CharScale × PlayerScale`（1.6302）**一致** ⇒ 图上就是实战大小 ✓</summary>
        private const float DisplayScale = 2.60f * 0.627f;

        private GameObject _holder;
        private readonly Dictionary<string, ModelView> _views = new Dictionary<string, ModelView>();
        private readonly Dictionary<string, float> _frozenU = new Dictionary<string, float>();
        private readonly Dictionary<string, GameObject[]> _tools = new Dictionary<string, GameObject[]>();
        private bool _cameraPinned;
        private float _autoRelease = -1f;

        private string _focusId;
        private Vector3 _focusPos;
        private float _focusHeight = 1.2f;
        private float _focusDist = 3.0f;
        /// <summary>整排视角的相对角度（度）：0=正面(从 +Z 拍) 90=侧面 45=斜前 180=背面</summary>
        private float _rowYaw = 0f;
        /// <summary>俯视角度（度）。0 = 平视，60 = 斜俯，89 = 接近正俯视。</summary>
        private float _rowPitch = 12f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<PlayerShowcase>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("PlayerShowcase");
            go.AddComponent<PlayerShowcase>();
        }

        private void Update()
        {
            if (_autoRelease > 0f)
            {
                _autoRelease -= Time.unscaledDeltaTime;
                if (_autoRelease <= 0f) Hide();
            }
            if (_views.Count == 0) return;
            float dt = Time.deltaTime;
            foreach (var kv in _views)
            {
                if (kv.Value == null || kv.Value.Go == null) continue;
                if (_frozenU.ContainsKey(kv.Key)) continue;   // 冻住的一格不动（截图要可重复）
                kv.Value.Tick(dt);
            }
        }

        private void LateUpdate() => PinCamera(Camera.main);

        private void PinCamera(Camera cam)
        {
            if (!_cameraPinned || cam == null || cam != Camera.main) return;
            ApplyCameraPose(cam);
        }

        private void ApplyCameraPose(Camera cam)
        {
            if (cam == null) return;
            Vector3 focus;
            float dist;
            if (_focusId != null)
            {
                focus = _focusPos + new Vector3(0f, _focusHeight * 0.42f, 0f);
                dist = _focusDist;
            }
            else
            {
                focus = new Vector3(_focusPos.x, 0.55f * DisplayScale * 0.5f + 0.10f, _focusPos.z);
                float span = (SlotIds.Length - 1) * SlotGap + 1.2f;
                dist = Mathf.Max(4.2f, span * 0.62f);
            }

            // ★ 机位公式（第一版写错了，这里说清楚）★
            //   `dir` = 从**焦点指向相机**的方向，水平分量由 `_rowYaw` 决定：
            //       yaw 0°   ⇒ 相机在 +Z 侧 ⇒ 拍到模型**正面**（本地 +Z 就是正面）
            //       yaw 90°  ⇒ 相机在 +X 侧 ⇒ **侧面**
            //       yaw 180° ⇒ 相机在 −Z 侧 ⇒ **背面**
            //   `_rowPitch` 是**俯角**：正值表示相机在焦点**上方**
            //   ⚠️ 第一版写成 `Euler(-pitch,0,0) * (Euler(0,180,0) * forward)`
            //      —— 那个 180° 把相机放到了**背后**，而 Unity 的 +X 旋转会把前向量压**向下**
            //      ⇒ 相机实际在"背后 + 地面以下"，是**从下往上拍背面** ✗
            //      ⇒ 抓图上"模型悬空、没有眼睛、地面跑到下面去了"，三件事全是这一个符号造成的 ✓
            //   Unity 的 Euler(x=pitch) 作用在 forward 上是 y=−sin(pitch) ⇒ 要**取负**才是上方 ✓
            Vector3 dir = Quaternion.Euler(-_rowPitch, _rowYaw, 0f) * Vector3.forward;
            cam.transform.position = focus + dir * dist;
            cam.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        }

        // ── 桥能调的六个方法 ────────────────────────────────────────────────

        /// <summary>
        /// 摆出 8 格（状态 × 武器）并接管机位。摆样点：玩家右侧 6.5m 的草地上（与虫怪验收台同一块干净地）。
        /// </summary>
        public string Show()
        {
            Clear();
            // ★ 摆样点要**躲开别的验收台** ★
            // `BugShowcase` 的默认落点是 (8.6, 0, 1.5)、相对玩家 +8.6/+1.5；
            // 两者挨着时抓图上会互相挡（第一轮实测：豆豆人面前正好立着一个灰色方块）✗
            // 所以本台走**另一侧**：默认 (−9.5, 0, 9.0)，相对玩家 −8.0/+8.0 ✓
            Vector3 origin = new Vector3(-9.5f, 0f, 9.0f);
            if (GameBootstrap.Live.Count > 0 && GameBootstrap.Live[0] != null &&
                GameBootstrap.Live[0].Session != null && GameBootstrap.Live[0].Session.World.Player != null)
            {
                PlayerActor p = GameBootstrap.Live[0].Session.World.Player;
                origin = new Vector3(p.Pos.x - 8.0f, 0f, p.Pos.y + 8.0f);
            }

            _holder = new GameObject("PLAYER_SHOWCASE");
            _holder.transform.position = origin;
            _focusId = null;
            _focusPos = origin;
            _focusHeight = PlayerBean.TotalHeight * DisplayScale;

            var sb = new StringBuilder();
            sb.Append("PLAYER-SHOWCASE 摆样 ").Append(SlotIds.Length).Append(" 格：");
            for (int i = 0; i < SlotIds.Length; i++)
            {
                string id = SlotIds[i];
                float x = (i - (SlotIds.Length - 1) * 0.5f) * SlotGap;
                // ★ 走**生产路径** ★：模型用 `ModelView.SpawnPlayer`、武器用 `MountTool`，
                //   与游戏里 `BuildPlayer`/`AttachTools` 是同一条链路 ✓
                ModelView mv = ModelView.SpawnPlayer(_holder.transform);
                if (mv == null || mv.Go == null) { sb.Append(" 【" + id + " 建不出来】"); continue; }
                mv.Go.transform.localPosition = new Vector3(x, 0f, 0f);
                mv.SetScaleFactor(DisplayScale);
                _views[id] = mv;

                var tools = new GameObject[PlayerBean.ToolIds.Length];
                int weapon = WeaponOf(id);
                for (int w = 0; w < tools.Length; w++)
                {
                    tools[w] = mv.MountTool(w);
                    if (tools[w] != null) tools[w].SetActive(w == weapon);
                }
                _tools[id] = tools;

                PoseOne(id, mv);
                sb.Append(' ').Append(id);
            }

            _cameraPinned = true;
            Camera.onPreCull -= PinCameraOnCull;
            Camera.onPreCull += PinCameraOnCull;
            _autoRelease = 300f;
            string text = sb.ToString() + "\n机位已接管（300 秒后自动交还）";
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        /// <summary>
        /// 机位写两处：`LateUpdate` + `Camera.onPreCull` ——
        /// 单靠其中一个都会失败（`LateUpdate` 会被别人的相机脚本盖掉；
        /// 而 URP 下 `camera.Render()` 不保证触发 `onPreCull`），两个都写才稳 ✓
        /// （这套结论是从 `BugShowcase` 那两次实测搬过来的，不是猜的）
        /// </summary>
        private void PinCameraOnCull(Camera cam) => PinCamera(cam);

        /// <summary>
        /// 把某一格钉在某个姿态。
        /// spec 写法：
        ///   `id`                例 `attack_hoe`      —— 用该格的默认姿态（挥击格默认钉在 u=0.45）
        ///   `id:u`              例 `attack_hoe:0.30` —— 挥击冻结在第 u 帧（0..1）
        ///   `id:weapon`         例 `idle:2`          —— 换手上那把武器（0 锄 1 壶 2 肥）
        ///   `id:none`           例 `attack_can:none` —— 摘掉武器（拍"空手"对照）
        /// </summary>
        public string Pose(string spec)
        {
            string[] parts = (spec ?? "").Split(':');
            string id = parts.Length > 0 ? parts[0].Trim() : "";
            if (id.Length == 0) return "用法：Pose(\"attack_hoe:0.45\") / Pose(\"idle:2\")";
            if (!_views.TryGetValue(id, out ModelView mv) || mv == null) return "摆样里没有 " + id + "（先调 Show）";
            if (mv.Bean == null) return id + " 不是程序化豆豆人（模型没建成）";

            if (parts.Length > 1)
            {
                string arg = parts[1].Trim().ToLowerInvariant();
                if (arg == "none")
                {
                    SetToolWeapon(id, -1);
                }
                else if (float.TryParse(arg, System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out float v))
                {
                    if (id.StartsWith("attack") || id == "hit" || id == "swap")
                    {
                        mv.Bean.DebugFreezeU = id.StartsWith("attack") ? Mathf.Clamp01(v) : -1f;
                        _frozenU[id] = v;
                    }
                    else
                    {
                        SetToolWeapon(id, (int)v);
                    }
                }
            }
            else
            {
                PoseOne(id, mv);
            }
            mv.Tick(0f);
            string text = "POSE " + id + " | " + mv.Bean.Describe() + " | clamp=" + mv.Current;
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        /// <summary>按格子 id 决定"这一格摆什么"——Show 与 Pose 共用同一张表，避免两处走样。</summary>
        private void PoseOne(string id, ModelView mv)
        {
            PlayerBean bean = mv.Bean;
            switch (id)
            {
                case "walk":
                    // ★ 走路也**冻结相位** ★ —— 不冻的话 Update 每帧推进，抓到的图不可重复，
                    // 而且相位可能正好落在"两脚并拢"那一帧 ⇒ 图上看着像没走 ✗
                    // 0.14s / 0.52s 周期 ⇒ 相位 0.27 ⇒ sin ≈ 0.99 ⇒ 摆幅最大的那一帧 ✓
                    mv.Play(ModelView.State.Walk);
                    mv.Tick(0.14f);
                    _frozenU[id] = 0.14f;
                    break;
                case "attack_hoe":
                    mv.SetAttackWeapon(0);
                    mv.SetAttackDuration(0.70f);       // = tuning.tools.hoe.cooldown
                    mv.Bean.DebugFreezeU = 0.45f;      // 挥到"最低点前一帧"：弧线最明显
                    _frozenU[id] = 0.45f;
                    mv.PlayForced(ModelView.State.Attack);
                    break;
                case "attack_can":
                    mv.SetAttackWeapon(1);
                    mv.SetAttackDuration(0.80f);       // = tuning.tools.can.cooldown
                    mv.Bean.DebugFreezeU = 0.45f;
                    _frozenU[id] = 0.45f;
                    mv.PlayForced(ModelView.State.Attack);
                    break;
                case "attack_fert":
                    mv.SetAttackWeapon(2);
                    mv.SetAttackDuration(1.20f);       // = tuning.tools.fertilizer.cooldown
                    mv.Bean.DebugFreezeU = 0.50f;
                    _frozenU[id] = 0.50f;
                    mv.PlayForced(ModelView.State.Attack);
                    break;
                case "hit":
                    mv.Bean.DebugFreezeU = -1f;
                    mv.PlayForced(ModelView.State.Hit);
                    // 采到"顶到位"那一帧就冻住（受击 0.30s，0.10s 是弹到位后的峰值）
                    mv.Tick(0.10f);
                    _frozenU[id] = 0.10f;
                    break;
                case "evade":
                    mv.SetEvade(0.26f);                // 闪避中段：前倾最大
                    _frozenU[id] = 0f;
                    mv.Play(ModelView.State.Idle);
                    break;
                case "swap":
                    mv.Bean.DebugFreezeU = -1f;
                    mv.SetAttackWeapon(1);
                    mv.PlayForced(ModelView.State.Swap);
                    _frozenU[id] = 0.25f;
                    mv.Tick(0.25f * 0.5f);
                    break;
                default:
                    mv.Play(ModelView.State.Idle);
                    _frozenU.Remove(id);
                    break;
            }
            if (bean != null && _frozenU.ContainsKey(id)) mv.Tick(0f);
        }

        private static int WeaponOf(string id)
        {
            if (id == "attack_can") return 1;
            if (id == "attack_fert") return 2;
            if (id == "swap") return 1;
            return 0;
        }

        private void SetToolWeapon(string id, int weapon)
        {
            if (!_tools.TryGetValue(id, out GameObject[] tools) || tools == null) return;
            for (int i = 0; i < tools.Length; i++)
                if (tools[i] != null) tools[i].SetActive(i == weapon);
        }

        /// <summary>把相机钉在某一格（特写）。spec 为空 = 回到整排。</summary>
        public string Focus(string spec)
        {
            string id = (spec ?? "").Trim();
            if (id.Length == 0)
            {
                _focusId = null;
                _focusPos = _holder != null ? _holder.transform.position : Vector3.zero;
                SetVisibleAll(true);
                return "机位回到整排";
            }
            if (!_views.TryGetValue(id, out ModelView mv) || mv == null || mv.Go == null)
                return "摆样里没有 " + id + "（先调 Show）";

            SetVisibleAll(false);
            mv.Go.SetActive(true);
            _focusId = id;
            _focusPos = mv.Go.transform.position;
            // 取景按**世界包围盒**反算（含帽檐与手上的武器），不按常数
            Renderer[] rs = mv.Go.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds();
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                if (!any) { b = rs[i].bounds; any = true; } else b.Encapsulate(rs[i].bounds);
            }
            float spanH = any ? b.size.y : PlayerBean.TotalHeight * DisplayScale;
            float spanW = any ? Mathf.Max(b.size.x, b.size.z) : 0.7f;
            _focusHeight = Mathf.Max(0.6f, spanH);
            _focusDist = Mathf.Clamp(Mathf.Max(spanH / 0.484f, spanW / 0.86f) * 0.82f, 1.6f, 40f);
            string text = "特写 " + id + " 高=" + spanH.ToString("0.00") + "m 宽/长=" + spanW.ToString("0.00")
                        + "m ⇒ 距离 " + _focusDist.ToString("0.00") + "m（其余已隐藏）";
            Debug.Log("[Seedfall] FOCUS " + text);
            return text;
        }

        private void SetVisibleAll(bool on)
        {
            foreach (var kv in _views)
            {
                if (kv.Value == null || kv.Value.Go == null) continue;
                kv.Value.Go.SetActive(on);
            }
        }

        /// <summary>
        /// 视角：spec = `正面` / `侧面` / `俯视` / `斜前` / `背面` 或 `yaw:pitch`。
        /// 只看整排（特写时用 Focus 的距离，角度仍取这里的 pitch/yaw）。
        /// </summary>
        public string Angle(string spec)
        {
            string s = (spec ?? "").Trim().ToLowerInvariant();
            if (s == "front" || s == "正面") { _rowYaw = 0f; _rowPitch = 12f; }
            else if (s == "side" || s == "侧面") { _rowYaw = 90f; _rowPitch = 10f; }
            else if (s == "iso" || s == "斜前") { _rowYaw = 35f; _rowPitch = 18f; }
            else if (s == "back" || s == "背面") { _rowYaw = 180f; _rowPitch = 12f; }
            else if (s == "top" || s == "俯视") { _rowYaw = 0f; _rowPitch = 78f; }
            else
            {
                string[] p = s.Split(':');
                if (p.Length == 2 &&
                    float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pi))
                { _rowYaw = y; _rowPitch = Mathf.Clamp(pi, 0f, 89f); }
                else return "用法：Angle(\"front\") / Angle(\"side\") / Angle(\"top\") / Angle(\"back\") / Angle(\"35:18\")";
            }
            string text = $"视角 yaw={_rowYaw:0}° pitch={_rowPitch:0}°";
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        /// <summary>
        /// **一次调用完成"摆机位 + 渲染 + 回读 + 落盘"**。
        /// spec = `"文件名"` 或 `"文件名:宽:高"`，落在 `&lt;项目&gt;/Docs/captures/&lt;文件名&gt;.jpg`。
        /// </summary>
        public string Snap(string spec)
        {
            string[] parts = (spec ?? "").Split(':');
            string name = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : "player-shot";
            int w = parts.Length > 1 ? (int)ParseFloat(parts[1], 1600f) : 1600;
            int h = parts.Length > 2 ? (int)ParseFloat(parts[2], 900f) : 900;
            w = Mathf.Clamp(w, 64, 1920);
            h = Mathf.Clamp(h, 64, 1080);

            Camera cam = Camera.main;
            if (cam == null)
            {
                Camera[] all = FindObjectsByType<Camera>();
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].enabled && all[i].gameObject.activeInHierarchy) { cam = all[i]; break; }
            }
            if (cam == null) return "没有相机，抓不了图";

            RenderTexture target = null;
            Texture2D readback = null;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                ApplyCameraPose(cam);
                target = RenderTexture.GetTemporary(w, h, 24);
                cam.targetTexture = target;
                cam.Render();
                cam.targetTexture = null;
                RenderTexture.active = target;
                readback = new Texture2D(w, h, TextureFormat.RGB24, false);
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readback.Apply();

                byte[] jpg = readback.EncodeToJPG(92);
                string root = System.IO.Path.GetDirectoryName(Application.dataPath);
                string dir = System.IO.Path.Combine(root, "Docs", "captures");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, name + ".jpg");
                System.IO.File.WriteAllBytes(file, jpg);

                // 顺手报一次"这张图不是全黑"的量化证据（中心 5×5 的平均亮度）
                string lum = CenterLuma(readback, w, h);
                string text = "SNAP " + file + "  " + jpg.Length + "B  " + w + "x" + h
                            + (_focusId != null ? ("  focus=" + _focusId) : "  focus=row")
                            + $"  yaw={_rowYaw:0} pitch={_rowPitch:0} centerLuma={lum}";
                Debug.Log("[Seedfall] " + text);
                return text;
            }
            catch (System.Exception e)
            {
                return "抓图失败：" + e.Message;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previousActive;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (readback != null) Destroy(readback);
            }
        }

        /// <summary>画面中心 5×5 的平均亮度（0..255）。用来当场证明"这张不是黑图"，而不是靠眼睛猜。</summary>
        private static string CenterLuma(Texture2D tex, int w, int h)
        {
            if (tex == null) return "n/a";
            float sum = 0f;
            int n = 0;
            for (int y = h / 2 - 2; y <= h / 2 + 2; y++)
                for (int x = w / 2 - 2; x <= w / 2 + 2; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    Color c = tex.GetPixel(x, y);
                    sum += (c.r + c.g + c.b) / 3f;
                    n++;
                }
            return n == 0 ? "n/a" : (sum / n * 255f).ToString("0.0");
        }

        /// <summary>
        /// 摆到**指定世界坐标**（`spec` = `"x:z"`）。默认 `Show()` 的位置是"玩家 −8/+8"，
        /// 而那一片在林带边上 —— 实测抓图上有一根**前景树干**从帽檐斜穿到胯部，
        /// 一开始被我当成模型自带的一条带子查了半天 ✗ 需要干净背景时就换一块空草地 ✓
        /// </summary>
        public string ShowAt(string spec)
        {
            string[] p = (spec ?? "").Split(':');
            if (p.Length != 2 ||
                !float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                return "用法：ShowAt(\"0:0\")";
            string text = Show();
            if (_holder != null)
            {
                _holder.transform.position = new Vector3(x, 0f, z);
                _focusPos = _holder.transform.position;
                _focusId = null;
                SetVisibleAll(true);
            }
            text += $"\n已挪到世界坐标 ({x:0.0}, {z:0.0})";
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        /// <summary>
        /// 只读探针：把某一格里**每个渲染器**的名字、世界包围盒尺寸与中心列出来。
        /// 用途很具体 —— 抓图上出现过一条"从帽檐斜穿到胯部"的浅色带子，
        /// 肉眼在几种可能的部件之间分不出来；列一次尺寸就能一眼定位 ✓
        /// （结论：那是**前景的树干**，不是模型的部件 —— 换到空草地上就消失了）
        /// </summary>
        public string DumpParts(string spec)
        {
            string id = (spec ?? "").Trim();
            if (id.Length == 0) id = "attack_hoe";
            if (!_views.TryGetValue(id, out ModelView mv) || mv == null || mv.Go == null)
                return "摆样里没有 " + id + "（先调 Show）";
            var sb = new StringBuilder();
            sb.Append("DUMP-PARTS ").Append(id).Append('\n');
            Renderer[] rs = mv.Go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null) continue;
                Bounds b = rs[i].bounds;
                sb.Append("  ").Append(rs[i].gameObject.activeSelf ? " " : "°")
                  .Append(rs[i].name.PadRight(12))
                  .Append(" size=").Append(b.size.x.ToString("0.000")).Append('×')
                  .Append(b.size.y.ToString("0.000")).Append('×').Append(b.size.z.ToString("0.000"))
                  .Append("  center=").Append(b.center.x.ToString("0.00")).Append(',')
                  .Append(b.center.y.ToString("0.00")).Append(',').Append(b.center.z.ToString("0.00"))
                  .Append('\n');
            }
            string text = sb.ToString();
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        /// <summary>
        /// 握持角度的**运行时旋钮**（验收用）：对所有已挂上去的武器，把局部旋转设成 `Euler(x,y,z)`。
        /// `spec` = `"x:y:z"`，例如 `"0:0:0"` / `"90:0:0"` / `"0:0:90"`。
        ///
        /// 为什么要有它：武器的"握法"是**看出来的**，不是算出来的 ——
        /// 三把 FBX 的网格在文件里是 Z-up（长轴 Z），导入器在根上留了 `Rx(270)` 把它立成 Y-up，
        /// 而 `MountTool` 会**覆盖**根旋转 ⇒ 想摆正就得自己补那个换算。
        /// 靠推理来回改一次要一个编译周期（约 1 分钟），而这里一次会话就能把候选角度全试完 ✓
        /// 定下来的角度再写回 `PlayerBean.ToolGripEuler`（真值只在那一处）✓
        /// </summary>
        public string Grip(string spec)
        {
            string[] p = (spec ?? "").Split(':');
            if (p.Length != 3) return "用法：Grip(\"90:0:0\")";
            if (!float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                return "用法：Grip(\"90:0:0\")";
            int n = 0;
            foreach (var kv in _tools)
            {
                GameObject[] arr = kv.Value;
                if (arr == null) continue;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) continue;
                    arr[i].transform.localRotation = Quaternion.Euler(x, y, z);
                    n++;
                }
            }
            string text = $"握持 Rot=({x:0},{y:0},{z:0})° 已应用到 {n} 把";
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        public string Hide()
        {
            int n = _views.Count;
            Clear();
            _cameraPinned = false;
            _autoRelease = -1f;
            _focusId = null;
            Camera.onPreCull -= PinCameraOnCull;
            string text = "摆样已撤（" + n + " 格）+ 机位交还";
            Debug.Log("[Seedfall] " + text);
            return text;
        }

        private void OnDisable() => Camera.onPreCull -= PinCameraOnCull;
        private void OnDestroy() => Camera.onPreCull -= PinCameraOnCull;

        private void Clear()
        {
            _views.Clear();
            _tools.Clear();
            _frozenU.Clear();
            if (_holder != null)
            {
                Destroy(_holder);
                _holder = null;
            }
        }

        /// <summary>
        /// 只读报告：几何统计 + 三把武器包围盒 + 一个实例的实际世界尺寸。
        /// 它**不建摆样、不动机位**，可以在任何时刻调 ✓
        /// </summary>
        public string Report()
        {
            var sb = new StringBuilder();
            sb.Append("PLAYER-BEAN REPORT（程序化豆豆人：运行时权威）\n");
            sb.Append("生成方式：Assets/Scripts/Art/PlayerBean.cs（几何）＋ ModelView.SpawnPlayer（两层装配）\n");
            sb.Append("尺寸表：总高 ").Append(PlayerBean.TotalHeight.ToString("0.000")).Append("m")
              .Append("；头 0.441m ／ 身 0.399m ⇒ 头:身 = 1:1.10\n");
            sb.Append("最宽处：草帽檐 0.680m（俯视剪影的主形）\n\n");
            sb.Append(PlayerBean.GeometryStats()).Append('\n');
            sb.Append(PlayerBean.ProbeTools()).Append('\n');

            // 生产路径建一个、量它的世界尺寸与状态姿态（用完即弃）
            ModelView mv = ModelView.SpawnPlayer(null);
            if (mv != null && mv.Go != null)
            {
                Renderer[] rs = mv.Go.GetComponentsInChildren<Renderer>(true);
                Bounds b = new Bounds();
                bool any = false;
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] == null) continue;
                    if (!any) { b = rs[i].bounds; any = true; } else b.Encapsulate(rs[i].bounds);
                }
                sb.Append("单实例世界尺寸（scale=1）：")
                  .Append(any ? (b.size.x.ToString("0.000") + "×" + b.size.y.ToString("0.000") + "×" + b.size.z.ToString("0.000") + "m") : "量不到")
                  .Append("  RawHeight=").Append(mv.RawHeight.ToString("0.000")).Append("m\n");
                // 五状态逐个采一帧，把姿态角打出来 ⇒ 改前/改后可按数字对比
                foreach (string id in SlotIds)
                {
                    if (id == "walk") { mv.Play(ModelView.State.Walk); mv.Tick(0.13f); }
                    else if (id == "hit") { mv.PlayForced(ModelView.State.Hit); mv.Tick(0.10f); }
                    else if (id == "swap") { mv.PlayForced(ModelView.State.Swap); mv.Tick(0.15f); }
                    else if (id.StartsWith("attack"))
                    {
                        int w = id == "attack_can" ? 1 : id == "attack_fert" ? 2 : 0;
                        mv.SetAttackWeapon(w);
                        mv.SetAttackDuration(w == 2 ? 1.20f : w == 1 ? 0.80f : 0.70f);
                        mv.Bean.DebugFreezeU = 0.45f;
                        mv.PlayForced(ModelView.State.Attack);
                        mv.Tick(0f);
                    }
                    else if (id == "evade") { mv.SetEvade(0.26f); mv.Play(ModelView.State.Idle); mv.Tick(0.05f); }
                    else { mv.Play(ModelView.State.Idle); mv.Tick(0.05f); }
                    sb.Append("  ").Append(id.PadRight(12)).Append(mv.Bean.Describe()).Append('\n');
                }
                Destroy(mv.Go);
            }
            else sb.Append("生产路径建不出来 ✗\n");

            string text = sb.ToString();
            try
            {
                string root = System.IO.Path.GetDirectoryName(Application.dataPath);
                string dir = System.IO.Path.Combine(root, "Docs", "reports");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "player-bean.txt"), text, new UTF8Encoding(false));
            }
            catch (System.Exception e) { Debug.LogWarning("[Seedfall] 豆豆人报告写盘失败：" + e.Message); }
            Debug.Log("[Seedfall] PLAYER-BEAN REPORT 已写 Docs/reports/player-bean.txt（" + text.Length + " 字）");
            return text;
        }

        private static float ParseFloat(string text, float fallback)
        {
            return float.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
}
