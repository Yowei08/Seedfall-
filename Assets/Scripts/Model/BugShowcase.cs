using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 虫怪的**验收台**（v181）：把 5 种虫一次摆出来、把某一只钉在某个关键姿势上、把网格统计写成文件。
    ///
    /// 它为什么存在（而不是"用游戏里那几只敌人凑一张图"）：
    ///   · BOSS 出场的第一个 tick 会**清场**（`BossEnemy.ClearField` —— 打死全场其它怪），
    ///     所以"4 种虫 + BOSS 同框"在真实玩法里**不可能出现** ✗
    ///     ⇒ 同框图只能是**纯表现层**的摆样 ✓
    ///   · BOSS 的两种攻击姿势要能给出一帧可比的画面 ⇒ 需要"钉在 age=T 这一帧"的开关 ✓
    ///   · 网格统计（顶点/三角/包围盒）要有出口 ⇒ 离线副本（`Tools/mesh_gen`）才能和运行时对上 ✓
    ///
    /// 与 `DebugApi` 同一套自举方式（`RuntimeInitializeOnLoadMethod` + 一个 GameObject），
    /// 由桥的 `/input`（action=send, path=BugShowcase, method=Show/Pose/Hide/Report）驱动。
    /// 不占用任何玩法状态、不写任何玩法字段 ⇒ 关掉它之后游戏与从前来一模一样 ✓
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BugShowcase : MonoBehaviour
    {
        /// <summary>
        /// 摆样 id 顺序。★ BOSS 放**最远端** ★ ——
        /// 商店车会停在**玩家附近**卸货，而摆样就摆在玩家旁边 ⇒ 挨着玩家那一端会被车挡住。
        /// 所以 BOSS（画面主角）放在离玩家最远的一格 ✓
        /// </summary>
        private static readonly string[] ShowIds = { "pig", "grunt", "shooter", "jumper", "boss" };

        /// <summary>摆样的世界横向间距（米）。BOSS 占两格宽，所以最右端留得宽一点。</summary>
        private const float SlotGap = 2.30f;

        /// <summary>摆样的显示放大（与游戏里 `CharScale` 一致 ⇒ 图上看到的就是实战大小）。</summary>
        private const float DisplayScale = 2.60f;

        private GameObject _holder;
        private readonly Dictionary<string, ProceduralBugs.BugView> _views = new Dictionary<string, ProceduralBugs.BugView>();
        private readonly Dictionary<string, float> _frozen = new Dictionary<string, float>();
        private bool _cameraPinned;
        private float _autoRelease = -1f;

        /// <summary>
        /// ★ v182：第 0 格（"pig"）画的是**旧版 pig 模型**，不是虫怪 ★
        ///
        /// 需求："小猪回调旧版动画和模型" ⇒ 场上（`ProceduralBugs.SyncEnemies`）与验收台
        /// （这里）必须是同一个事实，否则"图上是虫、游戏里是猪"两头都不算证据 ✗
        /// 所以这一格走 `ModelView.Spawn("Characters","pig")`，与游戏里 `SyncPig` 同一条资产链 ✓
        /// </summary>
        private ModelView _pig;

        /// <summary>走旧版模型的格子 id（其余 4 格是虫怪）。</summary>
        private const string PigShowId = "pig";

        /// <summary>非空时相机只框这一只（特写）；null = 整排。</summary>
        private string _focusId;
        private Vector3 _focusPos;
        private float _focusHeight = 1.6f;
        /// <summary>特写机位到主体的距离（米）。由 `FramingDistance` 按主体包围盒反算。</summary>
        private float _focusDist = 3.4f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<BugShowcase>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("BugShowcase");
            go.AddComponent<BugShowcase>();
        }

        private void Update()
        {
            if (_autoRelease > 0f)
            {
                _autoRelease -= Time.unscaledDeltaTime;
                if (_autoRelease <= 0f) Hide();
            }
            if (_views.Count == 0 && _pig == null) return;
            float dt = Time.deltaTime;
            foreach (var kv in _views)
            {
                if (kv.Value == null || kv.Value.Go == null) continue;
                if (_frozen.ContainsKey(kv.Key)) continue;
                kv.Value.Tick(dt);      // 冻住的那些保持不动（截图要可重复）
            }
            // 旧版小猪：`ModelView` 自己按 `_stateTime` 播，没有"相位冻结"的口子 ⇒ 一律照常走
            if (_pig != null && _pig.Go != null && !_frozen.ContainsKey(PigShowId)) _pig.Tick(dt);
        }

        /// <summary>
        /// ★ 机位写两处：`LateUpdate` + `onPreCull` ★
        ///
        /// 实测踩过两次，是**两个不同的原因**：
        ///   ① `LateUpdate` 里钉好的机位被**别人的相机接管**覆盖（同一台编辑器上还有别的自动化在跑，
        ///      它也有一路 LateUpdate 在摆机位，谁后跑没有保证）⇒ 拍出来是一张俯视全景 ✗
        ///   ② 只挂 `onPreCull` 也不行 —— URP 下 `/capture` 走的是 `camera.Render()`，
        ///      这条路径**不保证**触发那个回调 ⇒ 机位一次都没生效 ✗
        /// 所以两处都写；抓图脚本另外把 `DebugApi` 那个抢机位的组件**临时禁用**（见报告里的命令）✓
        /// </summary>
        private void LateUpdate()
        {
            PinCameraOnCull(Camera.main);
        }

        private void PinCameraOnCull(Camera cam)
        {
            if (!_cameraPinned || cam == null || cam != Camera.main) return;
            ApplyCameraPose(cam);
        }

        /// <summary>把机位摆到"当前 focus"该在的位置（整排 or 特写）。判据只有 `_focusId`。</summary>
        private void ApplyCameraPose(Camera cam)
        {
            if (cam == null) return;
            if (_focusId != null)
            {
                // 特写：**距离由主体尺寸直接反算**（见 `FramingDistance`），机位略高于体轴
                // ★ 机位在 **+Z 侧**（朝 -Z 看）：商店车在摆样的 -Z 侧巡游，
                //   从 +Z 拍过去车就在虫怪**背后**，挡不到任何一只 ✓
                Vector3 focus = _focusPos + new Vector3(0f, _focusHeight * 0.40f, 0f);
                Vector3 offset = new Vector3(0f, _focusHeight * 0.45f + _focusDist * 0.10f, _focusDist);
                cam.transform.position = focus + offset;
                cam.transform.rotation = Quaternion.Euler(18f, 180f, 0f);
                return;
            }
            // 整排：一行 5 只全进画面（同上，机位在 +Z 侧）
            // ★ v191：距离**按行宽反算**（原来是写死的 6.40m）★
            //   v181 的 5 只都差不多宽，6.40m 刚好；现在最宽的是蛾（翼展 ≈2.1m）与 BOSS（腿展 ≈3.6m）
            //   ⇒ 写死的距离会把两端的 BOSS / pig 切出画面（实测 v191 第一轮抓图就是这样）✗
            float rowSpan = (ShowIds.Length - 1) * SlotGap + 3.6f;      // 行心距 + 最宽那两只的一半
            float rowDist = Mathf.Clamp(rowSpan * 0.78f, 6.40f, 40f);
            Vector3 wideFocus = new Vector3(_focusPos.x, 0.95f, _focusPos.z);
            cam.transform.position = wideFocus + new Vector3(0f, rowDist * 0.40f, rowDist);
            cam.transform.rotation = Quaternion.Euler(17f, 180f, 0f);
        }

        // ── 桥能调的六个方法 ────────────────────────────────────────────────

        /// <summary>
        /// ★ v191 验收探针：把 **5 个 id 的"程序化虫怪"** 全部并排摆出来（**包括 pig 的瓢虫壳**）★
        ///
        /// 为什么不复用 `Show()`：`Show()` 有意显示**场上真实构成** —— 第 0 格是**旧版 FBX 小猪**
        /// （用户此前的要求："小猪回调旧版动画和模型"，见 `PigShowId` 那段注释）✓
        /// 而这一轮的验收还要求"5 个 id 的轮廓各自看得出来" ⇒ 需要一条**纯程序化**的取景，
        /// 否则 `pig` 那一格永远拍不到新做的瓢虫 ✗
        /// 它只建表现体、只动机位，不碰任何玩法状态 ⇒ 关掉之后游戏与从前一模一样 ✓
        /// </summary>
        public string ShowProcedural()
        {
            Clear();
            Vector3 origin = new Vector3(8.6f, 0f, 1.5f);
            if (GameBootstrap.Live.Count > 0 && GameBootstrap.Live[0] != null &&
                GameBootstrap.Live[0].Session != null && GameBootstrap.Live[0].Session.World.Player != null)
            {
                PlayerActor p = GameBootstrap.Live[0].Session.World.Player;
                origin = new Vector3(p.Pos.x + 8.6f, 0f, p.Pos.y + 1.5f);
            }

            _holder = new GameObject("BUG_SHOWCASE_PROC");
            _holder.transform.position = origin;
            _focusId = null;
            _focusPos = origin;
            _focusHeight = 1.6f;

            var sb = new StringBuilder();
            sb.Append("BUG-SHOWCASE(程序化) 摆样 ").Append(ShowIds.Length).Append(" 只：");
            for (int i = 0; i < ShowIds.Length; i++)
            {
                string id = ShowIds[i];
                bool boss = id == "boss";
                float x = (i - (ShowIds.Length - 1) * 0.5f) * SlotGap;
                ProceduralBugs.BugView v = ProceduralBugs.BugView.Build(
                    _holder.transform, id, boss, ColorFor(id), DisplayScale);
                v.Go.transform.localPosition = new Vector3(x, 0f, 0f);
                v.SetFacing(300f);
                v.Play(ModelView.State.Walk);
                v.SetNeon(new Color(0f, 0f, 0f, 0f));
                v.Tick(0f);
                _views[id] = v;
                sb.Append(' ').Append(id).Append('(').Append(v.Kind).Append(')');
            }

            _cameraPinned = true;
            Camera.onPreCull -= PinCameraOnCull;
            Camera.onPreCull += PinCameraOnCull;
            _autoRelease = 300f;
            string text = sb.ToString() + "\n机位已接管（300 秒后自动交还）";
            Log.Info(text);
            return text;
        }

        /// <summary>摆出全部 5 种虫（4 种普通 + 巨型甲虫），并接管机位。</summary>
        public string Show()
        {            Clear();

            // 摆样点：玩家**右侧 8.6 米**（实测这块地干净：在耕地边缘的草地上，
            // 既不进画面中央、也避开了树带与围栏）
            Vector3 origin = new Vector3(8.6f, 0f, 1.5f);
            if (GameBootstrap.Live.Count > 0 && GameBootstrap.Live[0] != null &&
                GameBootstrap.Live[0].Session != null && GameBootstrap.Live[0].Session.World.Player != null)
            {
                PlayerActor p = GameBootstrap.Live[0].Session.World.Player;
                origin = new Vector3(p.Pos.x + 8.6f, 0f, p.Pos.y + 1.5f);
            }

            _holder = new GameObject("BUG_SHOWCASE");
            _holder.transform.position = origin;
            _focusId = null;
            _focusPos = origin;
            _focusHeight = 1.6f;

            var sb = new StringBuilder();
            sb.Append("BUG-SHOWCASE 摆样 ").Append(ShowIds.Length).Append(" 种：");
            for (int i = 0; i < ShowIds.Length; i++)
            {
                string id = ShowIds[i];
                bool boss = id == "boss";
                float x = (i - (ShowIds.Length - 1) * 0.5f) * SlotGap;
                Color defColor = ColorFor(id);

                // ★ v182：第 0 格 = **旧版 pig**（FBX + 旧动画），其余 4 格 = 程序化虫怪 ★
                if (id == PigShowId)
                {
                    _pig = ModelView.Spawn("Characters", id, _holder.transform);
                    if (_pig != null)
                    {
                        _pig.Go.transform.localPosition = new Vector3(x, 0f, 0f);
                        _pig.ScaleToHeight(_pig.RawHeight * DisplayScale);   // 与游戏里 `SyncPig` 同一句
                        _pig.SetFacing(300f);
                        _pig.Play(ModelView.State.Walk);
                        _pig.SetNeon(new Color(0f, 0f, 0f, 0f));             // 中立单位：无红边
                        _pig.Tick(0f);
                        sb.Append(' ').Append(id).Append("(旧版FBX)");
                    }
                    else
                    {
                        sb.Append(' ').Append(id).Append("(取不到 Characters/pig)");
                        Log.Warn("摆样：取不到旧版 pig 模型（Resources/Art/Characters/pig.fbx）");
                    }
                    continue;
                }

                ProceduralBugs.BugView v = ProceduralBugs.BugView.Build(
                    _holder.transform, id, boss, defColor, DisplayScale);
                v.Go.transform.localPosition = new Vector3(x, 0f, 0f);
                // 3/4 侧前朝向（300°）：正面会把头/胸/腹叠在一起 ⇒ 三段体节读不出来 ✗
                // 而纯侧面又看不到"两片鞘翅"的左右关系 ⇒ 300° 两边都照顾到 ✓
                // （机位在 +Z 侧朝 -Z 看 ⇒ 面朝镜头 = yaw 0，3/4 = 300°）
                v.SetFacing(300f);
                v.Play(ModelView.State.Walk);
                v.SetNeon(new Color(0f, 0f, 0f, 0f));
                v.Tick(0f);
                _views[id] = v;
                sb.Append(' ').Append(id);
            }

            _cameraPinned = true;
            Camera.onPreCull -= PinCameraOnCull;      // 幂等：重复 Show 不会挂两次
            Camera.onPreCull += PinCameraOnCull;
            _autoRelease = 300f;        // 兜底：没人记得关也会自己交还机位
            string text = sb.ToString() + "\n机位已接管（300 秒后自动交还）";
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// 把某一只钉在某个姿势。
        /// spec 三种写法：
        ///   `id:walk:相位`   例 `grunt:walk:0.13` —— 走路，相位锁在 0.13 秒（腿摆到某一帧）
        ///   `id:idle`        例 `pig:idle`        —— 待机，相位自由
        ///   `id:攻击段:age`  例 `boss:1:0.25`     —— BOSS 攻击姿势（1=环形弹幕 2=冲锋蓄力 3=冲锋突进）
        /// </summary>
        public string Pose(string spec)
        {
            string[] parts = (spec ?? "").Split(':');
            if (parts.Length < 2) return "用法：Pose(\"id:walk:0.13\") / Pose(\"boss:1:0.25\")";
            string id = parts[0].Trim();

            // ★ v182：旧版 pig 只能切状态，**不能冻结相位**（`ModelView` 没有那个口子）★
            if (id == PigShowId)
            {
                if (_pig == null || _pig.Go == null) return "摆样里没有 " + id + "（先调 Show）";
                string whatPig = parts[1].Trim().ToLowerInvariant();
                ModelView.State stPig = whatPig == "walk" ? ModelView.State.Walk
                                      : whatPig == "hit" ? ModelView.State.Hit
                                      : ModelView.State.Idle;
                _pig.Play(stPig);
                _pig.Tick(0f);
                string pigText = "POSE pig(旧版) " + whatPig + " | 状态=" + _pig.Current
                               + " | 片段=" + _pig.CurrentClipName
                               + " | 注意：旧模型不支持相位冻结";
                Log.Info(pigText);
                return pigText;
            }

            if (!_views.TryGetValue(id, out ProceduralBugs.BugView v) || v == null)
                return "摆样里没有 " + id + "（先调 Show）";

            string what = parts[1].Trim().ToLowerInvariant();
            float arg = parts.Length > 2 ? ParseFloat(parts[2], 0f) : 0f;
            bool froze = false;

            if (what == "walk" || what == "idle" || what == "hit" || what == "attack" || what == "death")
            {
                ModelView.State st = what == "walk" ? ModelView.State.Walk
                                   : what == "hit" ? ModelView.State.Hit
                                   : what == "attack" ? ModelView.State.Attack
                                   : what == "death" ? ModelView.State.Death
                                   : ModelView.State.Idle;
                v.Play(st);
                if (what == "walk" || what == "hit" || what == "attack")
                {
                    v.FreezePhase(arg);
                    _frozen[id] = arg;
                    froze = true;
                }
                else
                {
                    v.FreezePhase(-1f);
                    _frozen.Remove(id);
                }
            }
            else
            {
                int kind = (int)ParseFloat(what, -1f);
                if (kind < 1) return "说不清的姿势：" + what;
                v.Play(ModelView.State.Attack);
                v.SetAttackPose(kind, arg);
                v.FreezePhase(arg * 0.7f);
                _frozen[id] = arg;
                froze = true;
            }

            v.Tick(0f);
            string text = "POSE " + id + " " + what + (froze ? (" age/phase=" + arg.ToString("0.00")) : "")
                        + " | 腿角 " + v.LegAngles()
                        + " | 攻击段 " + v.AttackPose;
            Log.Info(text);
            return text;
        }

        /// <summary>
        /// 把相机钉在某一只身上（特写）。`spec` = id；传空则回到整排。
        ///
        /// ★ 特写时**把其它几只关掉** ★ —— 排距 2.3m，而 BOSS 的腿展接近 3.6m，
        ///   不关就会像上一次那样"拍到的是邻居的腿"（实测过）✗
        /// </summary>
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
            if (!_views.TryGetValue(id, out ProceduralBugs.BugView v) || v == null || v.Go == null)
            {
                // ★ v182：旧版 pig 的高度按"缩放后"算 —— `ScaleToHeight(RawHeight×DisplayScale)`
                //   ⇒ 世界高就是 `RawHeight × DisplayScale` ✓（与虫怪的 `ModelHeight` 同一个口径）
                if (id == PigShowId && _pig != null && _pig.Go != null)
                {
                    _focusId = id;
                    _focusPos = _pig.Go.transform.position;
                    _focusHeight = Mathf.Max(0.6f, _pig.RawHeight * DisplayScale);
                    _focusDist = FramingDistance(_focusHeight, _focusHeight);
                    SetVisibleAll(false);
                    _pig.Go.SetActive(true);
                    string pigFocus = "特写 pig(旧版) 高=" + _focusHeight.ToString("0.00") + "m（其余已隐藏）";
                    Log.Info("FOCUS " + pigFocus);
                    return pigFocus;
                }
                return "摆样里没有 " + id + "（先调 Show）";
            }
            _focusId = id;
            _focusPos = v.Go.transform.position;
            // ★ v191：取景改成**按整只虫在世界里的实际包围盒反算距离** ★
            //   v181 的 `_focusHeight * 2.40` 只用体节高度：新物种的"最宽处"未必在高度上
            //   （蛾的翼展 2.1m、蚱蜢后腿往后伸 0.9m、锹形虫大颚往前伸 0.5m、
            //    BOSS 全长 8m）⇒ 忽远忽近、两头都被切（实测过两轮）✗
            SpanOf(v, out float spanH, out float spanW);
            _focusHeight = Mathf.Max(0.6f, spanH);
            _focusDist = FramingDistance(spanH, spanW);
            SetVisibleAll(false);
            v.Go.SetActive(true);
            string text = "特写 " + id + " 高=" + spanH.ToString("0.00") + "m 宽/长=" + spanW.ToString("0.00")
                        + "m ⇒ 机位距离 " + _focusDist.ToString("0.00") + "m（其余已隐藏）";
            Log.Info("FOCUS " + text);
            return text;
        }

        /// <summary>一只虫在世界里的高度与水平跨度（米，已含显示缩放）。</summary>
        private static void SpanOf(ProceduralBugs.BugView v, out float height, out float width)
        {
            height = 0.6f;
            width = 0.6f;
            if (v == null || v.Go == null) return;
            Renderer[] rs = v.Go.GetComponentsInChildren<Renderer>(true);
            if (rs == null || rs.Length == 0)
            {
                height = Mathf.Max(0.6f, v.ModelHeight * DisplayScale);
                width = height;
                return;
            }
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            height = b.size.y;
            width = Mathf.Max(b.size.x, b.size.z);
        }

        /// <summary>
        /// 让主体占画面约 62% 的机位距离。
        ///
        /// 依据（实测标定，不是拍的）：v181 那套 `ModelHeight × 2.40` 在 grunt 上正好"装满但不切"，
        /// 而当时 `ModelHeight` ≈ 1.39m ⇒ 3.34m 处看到的高度约 2.6m ⇒ 垂直半视角的 tan ≈ 0.39。
        /// 于是 `d = 主体高 / (2 × 0.39 × 0.62) ≈ 高 / 0.484`；
        /// 横向还有 16:9 的额外余量（横向是竖向的约 1.78 倍）⇒ `d ≥ 宽 / (1.78 × 0.484 × 0.62) ≈ 宽 / 0.86` ✓
        /// 取两者较大值 ⇒ 长身的（BOSS 8m）按长度退、竖高的按高度退，都不会被切 ✓
        /// </summary>
        private static float FramingDistance(float height, float width)
        {
            float byHeight = height / 0.484f;
            float byWidth = width / 0.86f;
            return Mathf.Clamp(Mathf.Max(byHeight, byWidth), 2.4f, 40f);
        }

        private void SetVisibleAll(bool on)
        {
            foreach (var kv in _views)
            {
                if (kv.Value == null || kv.Value.Go == null) continue;
                kv.Value.Go.SetActive(on);
            }
            if (_pig != null && _pig.Go != null) _pig.Go.SetActive(on);
        }

        /// <summary>
        /// **一次调用完成"摆机位 + 渲染 + 落盘"**（截图用）。
        ///
        /// ★ 为什么不用桥的 `/capture` ★
        /// `/capture` 走的是"桥线程 → 主线程排一次 Render"，中间隔着**若干帧**；
        /// 而这台编辑器上还有别的自动化在跑（它会摆自己的机位、也会重编译触发域重载）⇒
        /// 实测被抢过三次机位（拍出来是俯视全景）✗
        /// 这里把三件事放在**同一个主线程调用**里 ⇒ 中间不可能有人插进来 ✓
        ///
        /// spec = `"文件名"` 或 `"文件名:宽:高"`，落在 `&lt;项目&gt;/Docs/captures/&lt;文件名&gt;.jpg`。
        /// </summary>
        public string Snap(string spec)
        {
            string[] parts = (spec ?? "").Split(':');
            string name = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : "bug-shot";
            int w = parts.Length > 1 ? (int)ParseFloat(parts[1], 1600f) : 1600;
            int h = parts.Length > 2 ? (int)ParseFloat(parts[2], 900f) : 900;
            w = Mathf.Clamp(w, 64, 1920);
            h = Mathf.Clamp(h, 64, 1080);

            Camera cam = Camera.main;
            if (cam == null)
            {
                // 与桥的 `/capture` 同一条兜底链：没有 MainCamera 就取第一个启用的相机
                Camera[] all = FindObjectsByType<Camera>();
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].enabled && all[i].gameObject.activeInHierarchy) { cam = all[i]; break; }
            }
            if (cam == null) return "没有相机，抓不了图";

            // 先按摆样机位摆好，再渲染（同一帧内，不会有人插进来）
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
                string text = "SNAP " + file + "  " + jpg.Length + "B  " + w + "x" + h
                            + (_focusId != null ? ("  focus=" + _focusId) : "  focus=row");
                Log.Info(text);
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

        /// <summary>撤掉摆样并交还机位。</summary>
        public string Hide()
        {
            int n = _views.Count + (_pig != null ? 1 : 0);
            Clear();
            _cameraPinned = false;
            _autoRelease = -1f;
            _focusId = null;
            Camera.onPreCull -= PinCameraOnCull;
            string text = "摆样已撤（" + n + " 只）+ 机位交还";
            Log.Info(text);
            return text;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= PinCameraOnCull;
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= PinCameraOnCull;
        }

        /// <summary>
        /// ★ v191 验收探针：**只打几何指纹**（5 个 id 各一行），供"改前 / 改后"逐位对比。
        ///
        /// 为什么单独开一个方法而不只挂在 `Report()` 里：改前要采一次基线、改后再采一次，
        /// 两次之间**别的机器/进程也可能在跑**，所以需要一条"短、纯、可 diff"的出口 ✓
        /// 它只读网格（`ProceduralBugs.MeshFingerprint`），不建对象、不动机位、不写玩法状态 ✓
        /// </summary>
        public string Fingerprint()
        {
            var sb = new StringBuilder();
            sb.Append("BUG-FINGERPRINT（顶点数据 crc32；改前=改后 ⇒ 该 id 一个字节没动）\n");
            for (int i = 0; i < ProceduralBugs.TypeIds.Length; i++)
            {
                string id = ProceduralBugs.TypeIds[i];
                sb.Append("  ").Append(ProceduralBugs.MeshFingerprint(id, id == "boss")).Append('\n');
            }
            string text = sb.ToString();
            Log.Info("BUG-FINGERPRINT\n" + text);
            return text;
        }

        /// <summary>
        /// ★ v191：**敌人移动抽搐探针**（只读）★
        ///
        /// ⚠️ **v191-b 起改用 `ProbeV191Motion`（组件名同名，桥的调用方式照旧）**：
        ///   `path=ProbeV191Motion, method=Arm, value="帧数:tag"` → 报告落 `Library/emotion2-report.txt`。
        ///   本方法（走 `ProceduralBugs.ArmEnemyMotionProbe`）保留可用，但它有两条实测出来的死角 ✗
        ///   ① 按**物种**记 4 个固定槽、只认第一次见到的那只实例 ⇒ 那只离场后该槽**永久失明**，
        ///      输出 `位置 maxΔ=0.0000m@f-1` —— 与"这个物种一点都不抽"**长得一模一样** ✓
        ///   ② 只量 Transform 的位置/朝向 ⇒ 腿的振幅、Walk↔Idle 的跳变全在视野外 ✓
        ///   新探针按**实例 id** 记账、并直接量 `BugView.SwingAmpDeg`（腿摆振幅）✓
        ///
        /// spec = `帧数[:tag]`，默认 `180:live`。布防后由 `ProceduralBugs.SyncEnemies` 逐帧抄
        /// **场上真实敌人**渲染出来的世界位置 / 朝向，采满自己往控制台打一行 `EMOTION …`。
        ///
        /// 为什么挂在验收台上：`syncEnemies` 是玩法每帧都走的那条路，探针只能"旁听"，
        /// 不能反过来驱动它 ✗ 而桥的 `/input` 只能调**场景里某个组件**的公开方法
        /// ⇒ 需要一个已经存在的 MonoBehaviour 当触发口（本类正是为此而存在，见类注释）✓
        /// </summary>
        public string EnemyMotion(string spec)
        {
            string text = (spec ?? string.Empty).Trim();
            int colon = text.IndexOf(':');
            int frames = 180;
            string tag = "live";
            if (colon > 0)
            {
                int.TryParse(text.Substring(0, colon), out frames);
                tag = text.Substring(colon + 1).Trim();
            }
            else if (text.Length > 0) int.TryParse(text, out frames);
            if (frames <= 0) frames = 180;
            ProceduralBugs.ArmEnemyMotionProbe(frames, tag);
            string msg = "已布防敌人移动探针 帧数=" + frames + " tag=" + tag
                       + "（采满自动打 [Seedfall] EMOTION …，走 /log 读）";
            Log.Info("DEBUG EnemyMotion " + msg);
            return msg;
        }

        /// <summary>上次 `EMOTION` 报告原文（桥可以直接取，不用翻 /log）。</summary>
        public string EnemyMotionReport() => ProceduralBugs.LastEnemyMotionReport;

        /// <summary>
        /// 网格统计 + 形状参数 + 腿相位证据，写进 `Docs/reports/bug-meshes.txt` 并返回同一份文本。
        /// ★ 这份文件是**离线副本对照**的输入 ★ ——`verify_bug_meshes.py` 拿它跟 Python 侧的重建逐项比。
        /// </summary>
        public string Report()
        {
            var sb = new StringBuilder();
            sb.Append("BUG-MESH REPORT（ProceduralBugs：运行时权威表）\n");
            sb.Append("生成方式：Assets/Scripts/Model/ProceduralBugs.cs\n");
            sb.Append("离线副本：Tools/mesh_gen/gen_bug_meshes.py（同一套常数与公式）\n\n");

            for (int i = 0; i < ProceduralBugs.TypeIds.Length; i++)
            {
                string id = ProceduralBugs.TypeIds[i];
                bool boss = id == "boss";
                ProceduralBugs.Shape s = ProceduralBugs.ShapeOf(id, boss);
                sb.Append("[TYPE] ").Append(id).Append('\n');
                sb.Append("  crc32=0x").Append(ProceduralBugs.Crc32(id).ToString("X8")).Append('\n');
                sb.Append("  boss=").Append(boss ? 1 : 0).Append(" unit=").Append(s.Unit.ToString("0.###")).Append('\n');
                sb.Append("  headLen=").Append(F(s.HeadLen)).Append(" thoraxLen=").Append(F(s.ThoraxLen))
                  .Append(" abdomenLen=").Append(F(s.AbdomenLen)).Append('\n');
                sb.Append("  headR=").Append(F(s.HeadR)).Append(" thoraxR=").Append(F(s.ThoraxR))
                  .Append(" abdomenR=").Append(F(s.AbdomenR)).Append('\n');
                sb.Append("  legLen=").Append(F(s.LegLen)).Append(" legThick=").Append(F(s.LegThick))
                  .Append(" antennaLen=").Append(F(s.AntennaLen)).Append('\n');
                sb.Append("  bodyY=").Append(F(s.BodyY)).Append(" sides=").Append(s.Sides)
                  .Append(" carapace=").Append(s.Carapace ? 1 : 0)
                  .Append(" horn=").Append(s.Horn ? 1 : 0)
                  .Append(" tailSpike=").Append(s.TailSpike ? 1 : 0).Append('\n');
                sb.Append("  hue=").Append(F(s.Hue)).Append(" legFreq=").Append(F(s.LegFreq)).Append('\n');
                sb.Append("  meshes=").Append(ProceduralBugs.MeshStats(id, boss)).Append('\n');
                sb.Append("  bounds=").Append(ProceduralBugs.MeshBounds(id, boss)).Append('\n');
                sb.Append('\n');
            }

            // ── v191：几何指纹（顶点数据本身的 crc32）──
            // 上面每个 [TYPE] 里的 `crc32=` 是 **id 字符串** 的 crc32 ⇒ id 不同它必然不同，
            // 证明不了"网格不同"。这一段算的是顶点坐标 ⇒ 换颜色换名字都不会让它变 ✓
            sb.Append("[MESH-FINGERPRINT] 顶点数据 crc32（不是 id 的 crc32；两两不同 ⇒ 不是同一个壳）\n");
            for (int i = 0; i < ProceduralBugs.TypeIds.Length; i++)
            {
                string fid = ProceduralBugs.TypeIds[i];
                sb.Append("  ").Append(ProceduralBugs.MeshFingerprint(fid, fid == "boss")).Append('\n');
            }
            sb.Append('\n');

            // ── 腿相位证据：同一只虫在 4 个相位上的六条腿摆角 ──
            sb.Append("[LEG-PHASE] 六条腿的摆角（度，正=向前）—— 相位每 0.05s 采样一次\n");
            sb.Append("          三对腿交替相位 ⇒ 同一时刻必然一半向前、一半向后\n");
            ProceduralBugs.BugView probe = null;
            if (_views.TryGetValue("grunt", out ProceduralBugs.BugView shown)) probe = shown;
            if (probe == null)
            {
                probe = ProceduralBugs.BugView.Build(null, "grunt", false, Color.white, 1f);
                sb.Append("  （摆样没开，临时建一只 grunt 采样）\n");
            }
            probe.Play(ModelView.State.Walk);
            float[] phases = { 0f, 0.05f, 0.10f, 0.15f };
            for (int i = 0; i < phases.Length; i++)
            {
                probe.FreezePhase(phases[i]);
                sb.Append("  t=").Append(F(phases[i])).Append("s  ").Append(probe.LegAngles()).Append('\n');
            }
            if (!_views.ContainsKey("grunt") && probe != null && probe.Go != null)
            {
                // 临时样例用完即弃（它不是池里的实例，直接销毁）
                Destroy(probe.Go);
            }

            sb.Append("\n[BOSS-POSE] M=2 种攻击各自的姿势参数（关键帧）\n");
            sb.Append("  barrage(1)  u=0.25 → 后仰24° 鞘翅开62° 腹抬26 触角扬46 大颚张32 整体后坐\n");
            sb.Append("  charge(2)   u=0.80 → 前倾15° 下沉22% 鞘翅夹-6° 大颚张52° 触角后收30\n");
            sb.Append("  charge(3)   age=0.00 → 前倾30° 整体前冲0.10×unit 鞘翅26° 大颚合3°\n");

            string text = sb.ToString();
            try
            {
                string root = System.IO.Path.GetDirectoryName(Application.dataPath);
                string dir = System.IO.Path.Combine(root, "Docs", "reports");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "bug-meshes.txt"), text, new UTF8Encoding(false));
            }
            catch (System.Exception e)
            {
                Log.Warn("虫怪报告写文件失败：" + e.Message);
            }
            Log.Info("BUG-MESH REPORT 已写 Docs/reports/bug-meshes.txt（" + text.Length + " 字）");
            return text;
        }

        // ── 内部 ────────────────────────────────────────────────────────────

        private void Clear()
        {
            _views.Clear();
            _frozen.Clear();
            _pig = null;                 // 它挂在 `_holder` 下面 ⇒ 跟着 `_holder` 一起销毁
            if (_holder != null)
            {
                Destroy(_holder);
                _holder = null;
            }
        }

        /// <summary>数据表里的颜色（与 `enemies.json` 的 color 一致）。摆样拿不到 EnemyDef 时用。</summary>
        private static Color ColorFor(string id)
        {
            switch (id)
            {
                case "pig": return new Color(0.961f, 0.663f, 0.722f);       // #F5A9B8
                case "grunt": return new Color(0.753f, 0.314f, 0.302f);     // #C0504D
                case "shooter": return new Color(0.612f, 0.420f, 0.710f);   // #9C6BB5
                case "jumper": return new Color(0.878f, 0.627f, 0.188f);    // #E0A030
                case "boss": return new Color(0.357f, 0.227f, 0.557f);      // #5B3A8E
                default: return Color.gray;
            }
        }

        private static string F(float v) => v.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

        private static float ParseFloat(string text, float fallback)
        {
            return float.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }
    }
}
