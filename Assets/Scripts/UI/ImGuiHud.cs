using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 原型 HUD（IMGUI）。只读 RunSession 快照，并把鼠标点击写成"待处理指令"，
    /// 由 GameBootstrap 在下一个固定帧并入 FrameInput。
    /// 生产替换路径：uGUI / UI Toolkit，接口不变（见架构案 A-1）。
    ///
    /// v5 布局（**所有键位说明只出现在左侧说明栏**，HUD 其它地方不提示按键）：
    ///   左上 = 生命/护盾/经验 + 金币/耕地
    ///   左侧 = 按键说明（Tab 显示/隐藏；隐藏后仍保留一行 Tab 提示）
    ///   右侧 = 武器面板（滚轮切换的可视化）
    ///   顶部 = 只显示阶段与倒计时
    ///   底部 = 耕地双进度条 + 水壶/肥料充能条
    ///   Esc  = 菜单（属性 + 继续游戏 + 重新开始）
    ///
    /// ★ v177：**战斗 HUD 的四类元素不再有底板**（血条 / 倒计时 / 充能 / 切换武器）★
    ///   需求："所有统一不使用背景 …… 靠描边/发光/对比度而不是底板来保证可读性"。
    ///   ⇒ 可读性由三层承担：投影（治亮底图）+ 描边（治暗底图）+ 文字四向描边（治杂乱底图）。
    ///     三层的颜色/宽度定在 `HudStroke` / `HudStrokeDim` / `HudShadow` / `HudInnerLine` 四处，
    ///     画法只有 `BareFrame`（框）、`BarBare`（条）、`HudLabel`（字）三个原语 ⇒ 口径不可分叉 ✓
    ///   ⇒ 仍然是亮底的面板（按键说明 / 耕地提示 / 商店 / 菜单）**一个字都没改** ✓
    ///   ⚠️ 因此本文件的文字样式有**两套音色**，调用点必须显式选边：
    ///     亮底 → 默认（墨棕）/ `WrapSmall`；压场景 → `HudBody`/`HudBold`/`HudTitle`/`HudCenter`
    ///     ——这正是 v102 立下的规矩（不靠默认值碰巧合适）✓
    ///
    /// ★ v177：**种子袋面板只在准备阶段绘制**，战斗阶段整块不进入绘制（见 `Draw` 的 `s.Phase == Prepare`）★
    /// </summary>
    public sealed class ImGuiHud
    {
        private Font _font;
        /// <summary>建字体那一刻的语言代数（≠ `L10n.FontGeneration` ⇒ 换字体，见 `Font` 属性）。</summary>
        private int _fontGen = -1;
        private Texture2D _white;
        private GUIStyle _label, _small, _bold, _button, _center, _title;
        private float _virtualW = 1280f;
        private float _virtualH = 720f;
        private bool _keyHelpInit;
        private Vector2 _statScroll, _invScroll, _itemScroll, _seedScroll;
        /// <summary>v83：商品改成一行一件的左图右文后，**每行各自有滚动位置**。
        /// 旧版 4 列窄卡共用一个底部详情区，所以只需要一条滚动量；现在每行的描述框是独立的，
        /// 共用一条滚动量会导致"滑一行动四行"。</summary>
        private readonly float[] _offerScrollPerRow = new float[4];
        private readonly float[] _offerScroll = new float[4];
        /// <summary>v52：商品详情区高度（4 列卡片下方）。</summary>
        private const float OfferDetailH = 96f;
        /// <summary>v52：当前显示详情的商品序号（卡片悬停切换；卖光后为 -1）。</summary>
        private int _offerShown;
        /// <summary>v19：卡片底部给按钮预留的高度（0 = 无按钮）。</summary>
        private float ButtonReserve;
        private readonly float[] _menuSeedScroll = new float[4];
        /// <summary>
        /// v180：**不再是 readonly** —— 它必须装得下 `Bag.SlotCount` 个格子。
        ///
        /// 改前 `new float[4]` 而袋子有 6 格 ⇒ 第 5/6 格的滚动量**永远存不住**
        /// （`DrawInfoList` 用 `scrollIndex &lt; scrollStore.Length` 做边界检查 ⇒ 越界就取 0）✓
        /// 症状是"在那两格里滚一下，下一帧弹回顶部" —— 一次真实的"跳" ✗
        /// 现在按需扩容（`EnsureScrollStore`）⇒ 有几格就有几个槽 ✓
        /// </summary>
        private float[] _seedScrollPerCard = new float[8];
        /// <summary>
        /// ★ v198：**植物格正文的滚动量**（一槽一个格子；用户原话："植物增加滑动条展示"）★
        ///
        /// 为什么要存成一个数组而不是一个 `Vector2`：
        ///   主界面植物区与菜单右列各 6 格，**每格独立滚**；共用一条滚动量会出现
        ///   "滑一格动六格"。做法与 `_seedScrollPerCard` 完全同构（`EnsureScrollStore` 按需扩容）✓
        /// ⚠️ 两个面板共用同一个数组：同一株植物在主界面和菜单里**滚到同一个位置**，
        ///   这是想要的行为（它是同一条信息，不该在两处显示成两页）✓
        /// </summary>
        private float[] _plantCellScroll = new float[8];
        private readonly float[] _choiceScroll = new float[3];
        private string _itemSelected;
        private GUIStyle _right;
        private float _statContentW = 300f;

        public int PendingBuyOffer = -1;
        public bool PendingReroll;
        public int PendingInitialChoice = -1;
        public bool PendingNextWave;
        public bool PendingRestart;
        public bool PendingResume;

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v214：**退出战斗（回到主城）** 的两个寄存器 ★★
        //   用户原话："战斗模式的菜单增加退出战斗（回到主城）"
        //
        //   · `ExitToTownArmed`  —— **二次确认态**。第一次点只把它翻成 true，
        //     按钮文本随之变成「确认退出？再点一次」；它**不是一局的临时态**，
        //     而是"这一次点击的临时态" ⇒ 菜单一关就清（见 `Draw` 的 `!s.Paused` 分支）✓
        //   · `PendingExitToTown` —— **一次性请求**，与 `PendingRestart`/`PendingResume`
        //     完全同一套消费方式：由 `GameBootstrap.Update` 取走。
        //     HUD **从不自己切场景**（`SceneManager` 只在 `GameBootstrap` 里用）✓
        //
        //   ⚠️ 真正的二次确认判定在 `GameBootstrap.HandleExitToTown`（按钮与桥入口共用它），
        //      这里只负责"显示哪个文本"与"把点击寄存下来" ✓
        // ══════════════════════════════════════════════════════════════════════
        public bool ExitToTownArmed;
        public bool PendingExitToTown;

        /// <summary>
        /// 上一帧菜单**是不是开着**（`Draw` 里维护）。用途只有一个：
        /// 判"菜单刚关掉"的那一沿 —— 见 `Draw` 里 `!s.Paused` 分支的说明。
        /// </summary>
        private bool _menuWasOpen;
        public int PendingSell = -1;
        /// <summary>种子袋被点击的槽位（-1 无）。</summary>
        public int PendingSlotClick = -1;
        // ★ v198（用户原话："删除商店一键合成按键"）：`PendingMergeAll` 字段已删除 ★
        //   链路是 按钮 → `PendingMergeAll` → `GameBootstrap` 消费 → `SeedBag.AutoMergeAll`
        //   ⇒ 按钮删了之后这条链**整条**都不再有入口，留着字段就是一条"永远为 false 的死开关" ✗
        //   ⚠️ `SeedBag.AutoMergeAll` **保留不动**：自检有三条断言直接调它（合成规则本身还在用）✓
        /// <summary>v11 抽卡：选中的卡（-1 无）与重抽请求。</summary>
        /// <summary>v11 抽卡：选中的卡（-1 无）与重抽请求。</summary>
        public int PendingRewardPick = -1;
        /// <summary>v15: shop offer lock toggle (-1 none).</summary>
        public int PendingOfferLock = -1;
        public int PendingRewardSelect = -1;
        public bool PendingRewardReroll;
        /// <summary>v9：对某个槽位点"合成"（找一个相同的种子合成）。</summary>
        public int PendingSlotMerge = -1;
        /// <summary>Tab 切换：左侧按键说明是否显示。</summary>
        public bool KeyHelpVisible = true;

        // ══════════════════════════════════════════════════════════════════════
        //  ★ v203：把"悬停"这件事**从已有的命中测试里读出来**，交给光标用 ★
        //
        //  ── 为什么不是新写一套矩形表 ✗ ──
        //    光标要"悬停在可点元素上就发光"。若这里另建一份"哪些地方可点"的矩形清单，
        //    它就与 `Btn()` 真实用的那块矩形**是两个真相**：一旦差 1px，光标会在按钮边缘说反话 ✗
        //    ⇒ 所以在 `Btn()` **内部**、拿**它自己的 Rect** 记一个布尔 ✓
        //      `GUI.Button(r, ...)` 判悬停靠的就是 `r.Contains(Event.current.mousePosition)`，
        //      这里用的是同一个 `r`、同一个 `Event.current.mousePosition`、同一个回调时刻 ⇒ 不可能分叉 ✓
        //    另把 `RequestTip()` 也算进来：被请求悬停框的元素虽然不是按钮（增益图标、武器格），
        //    但它在**UI 层**上且对鼠标有反应 —— 光标的语义是"你现在在 UI 上"，不是"你现在能点" ✓
        //
        //  ── 为什么一定要记"帧号" ✗ 而不是只记布尔 ★ ──
        //    `Event.current` 只在 `OnGUI` 里有效（用户原话）⇒ 悬停状态只能在 `OnGUI` 里记、
        //    在 `Update` 里用，于是 `Update` 读到的永远是**上一帧**的值。这没问题（1 帧 ≈ 16ms）。
        //    但一旦 `OnGUI` 不再被调用（`showHud = false` / 失焦 / 暂停时被跳过），
        //    那个布尔会**永远停在 true** ⇒ 光标一直"发光"、永远退不回默认 ✗
        //    ⇒ 记帧号，超过 `HoverStaleFrames` 帧没刷新就当作"没悬停" ✓
        //    （这就是"缓存要带失效条件"，只缓存不带失效是另一个坑）
        // ══════════════════════════════════════════════════════════════════════
        private const int HoverStaleFrames = 2;
        private bool _hoverBtn;
        private bool _hoverTip;
        private int _hoverFrame = -1;

        /// <summary>最近一次记录悬停的帧号（探针读数用；-1 = 从未记录过）。</summary>
        public int HoverFrame => _hoverFrame;

        /// <summary>最近一帧里命中的按钮个数（≥1 才算悬停；见 `_hoverBtn`）。</summary>
        public bool HoverOnButton => _hoverBtn;

        /// <summary>最近一帧里是否有人请求了悬停框（见 `RequestTip`）。</summary>
        public bool HoverOnTip => _hoverTip;

        /// <summary>
        /// ★ 光标用：HUD 现在是否处于"鼠标在 UI 元素上" ★
        /// 由 `Draw()` 期间记录、在 `Update` 里读取 ⇒ 描述的是**上一帧**（差 1 帧，可接受）。
        /// 超过 `HoverStaleFrames` 帧没刷新 ⇒ 一律 false（防止"永远发光"）。
        /// </summary>
        public bool UiHover
        {
            get
            {
                if (_hoverFrame < 0) return false;
                if (Time.frameCount - _hoverFrame > HoverStaleFrames) return false;
                return _hoverBtn || _hoverTip;
            }
        }

        // ── 探针钩子（只读探针用；HUD 自己从不读这两个）────────────────────────
        /// <summary>
        /// ★ 探针用：把"鼠标在哪"换成给定坐标 ★
        /// 判据（`r.Contains(...)`）**一个字没改**，改的只是喂进去的坐标 ——
        /// 与本文件 `DrawBuffs` 里既有的 `ForceBuffTipIndex` 是同一条路数（原注释："探针钩子只换坐标，不改判定"）。
        /// 为什么要它：悬停这条链的**真手**验证要求有人真的把鼠标挪到按钮上，
        /// 而自动化这边拿不到"游戏视图在屏幕上的位置"这个坐标 ⇒ 用注入坐标把同一段判定跑起来。
        /// `null` = 用真实鼠标（正常运行永远是这个值）。
        /// </summary>
        public static Vector2? ProbeMouse;

        private static readonly System.Collections.Generic.List<Rect> _btnRects =
            new System.Collections.Generic.List<Rect>(32);

        /// <summary>本帧 `Btn()` 画过的所有按钮矩形（探针据此挑一个"真的存在的按钮"）。</summary>
        public static System.Collections.Generic.IReadOnlyList<Rect> ButtonRects => _btnRects;

        // ★ v214：**与 `_btnRects` 同源的"文本"探针** ★
        //   为什么需要它：`unity_look` 抓的是**相机渲染**，IMGUI（`OnGUI`）根本不在那张图里 ⇒
        //   截图无法证明"按钮画出来了、文本是什么"。而 `_btnRects` 只有矩形没有字 ✗
        //   ⇒ 在同一个 `Btn()` 里顺手记下文本，读数就是**运行时真值**（这一帧真的下笔的那串字）✓
        //   下标与 `_btnRects` **一一对应**（同一个 `Btn()` 里连续 Add 两条）✓
        private static readonly System.Collections.Generic.List<string> _btnTexts =
            new System.Collections.Generic.List<string>(32);

        /// <summary>本帧 `Btn()` 画过的按钮文本（与 <see cref="ButtonRects"/> 同下标）。</summary>
        public static System.Collections.Generic.IReadOnlyList<string> ButtonTexts => _btnTexts;

        /// <summary>v47：物体旁浮动文字的样式（懒创建，字号随每条提示变化）。</summary>
        private GUIStyle _popup;

        /// <summary>
        /// ★ v281：灼印「X」旁边的**层数角标**样式（懒创建，与 `_popup` 分开一份）★
        ///
        /// 为什么不复用 `_popup`：`DrawPopups` 会就地改它的 `fontSize` / `textColor`，
        /// 而 `DrawPopups` 在**没有弹字时第一行就 return** ⇒ 复用就会出现"这一帧改了、下一帧没人改回来"
        /// 的串味窗口 ✗ 独立一份的成本是一个 `GUIStyle`，换来的是"角标怎么画"与"弹字怎么画"互不牵连 ✓
        /// </summary>
        private GUIStyle _markBadge;

        /// <summary>
        /// `ui.scale = 1.35` 是在这个分辨率下标定的（`1920×1080 / 1.35 = 虚拟 800` 高）。
        /// 缩放按 `Screen.height / 这个值` 线性跟随 ⇒ 虚拟高度恒定 ⇒ 布局不会被压。
        /// </summary>
        private const float UiCalibratedHeight = 1080f;

        /// <summary>自适应缩放的夹取范围（相对配置的 `ui.scale`）。</summary>
        private const float UiScaleMinFactor = 0.70f;
        private const float UiScaleMaxFactor = 1.60f;

        /// <summary>
        /// ★★ v251：**随屏幕高度自适应**（用户报"部分电脑上 UI 压缩"）★★
        ///
        /// ── 原来的问题（取证）──
        ///   v250 之前这里**只返回配置里的固定值** `t.ui.scale`（= 1.35），完全不看分辨率。
        ///   而 `Draw` 里 `_virtualW = Screen.width / scale`、`_virtualH = Screen.height / scale`
        ///   ⇒ 虚拟画布高度随屏幕线性变化：
        ///       1920×1080 → 虚拟 1422×**800**   （设计基准 ✓）
        ///       1600×900  → 虚拟 1185×**667**   （开始挤）
        ///       1366×768  → 虚拟 1012×**569**   ⇒ 比设计基准矮 231 px ⇒ 纵向被压/裁 ✗
        ///       1280×720  → 虚拟  948×**533**   ⇒ 更糟 ✗
        ///   那些低分辨率机器就是用户说的"UI 压缩"。
        ///
        /// ── 做法 ──
        ///       scale = base × (Screen.height / 1080)       再夹到 [base×0.70, base×1.60]
        ///   ⇒ 虚拟高度 = Screen.height / scale = 1080 / base**（与分辨率无关）**
        ///   ⇒ base = 1.35 时虚拟高度恒为 **800**，与 1920×1080 下完全一致 ✓
        ///
        /// ── 为什么夹下限用 0.70 而不是更低 ──
        ///   1366×768 需要 1.35×768/1080 = **0.96**（在范围内 ✓）；1280×720 需要 **0.90** ✓；
        ///   1024×600 需要 **0.75**（仍在下限 0.945 之上 —— 见下）；再低的窗口靠"窗口最小尺寸"挡。
        ///   下限 `base×0.70 = 0.945` ⇒ 虚拟高度最多 1143；上限 `base×1.60 = 2.16` ⇒ 虚拟高度最少 500。
        /// </summary>
        private static float UiScale
        {
            get
            {
                TuningDef t = TableDatabase.Tuning;
                float baseScale = t != null && t.ui != null && t.ui.scale > 0.1f ? t.ui.scale : 1f;

                int h = Screen.height;
                if (h <= 0) return baseScale;      // 无窗口/首帧时 `Screen.height` 可能为 0

                float fit = baseScale * (h / UiCalibratedHeight);
                return Mathf.Clamp(fit, baseScale * UiScaleMinFactor, baseScale * UiScaleMaxFactor);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v178：文字层 —— 把【字】从 `GUI.matrix` 里拆出来（像素字）★
        //
        //  ── 问题（取证，不是推测）──
        //    `Draw` 里 `GUI.matrix = TRS(scale = ui.scale = 1.35)` **同时缩放布局与字形**。
        //    `GUIStyle.fontSize` 是 **int** ⇒ 真凶是【整数字号 × 1.35 = 小数像素】：
        //      15 → 20.25    17 → 22.95    19 → 25.65    30 → 40.5
        //    字形先在整数尺寸上光栅化、再被矩阵拉到小数像素 ⇒ **双线性重采样** ⇒ 灰边、发糊 ✓
        //    这与字体资产无关：工程里**没有任何 .ttf/.otf**，字来自系统动态字体（见 `Font` 属性）⇒
        //    `FontRenderingMode` 那条路根本不存在（它是 Editor-only 的导入设置，不是运行时属性）✗
        //
        //  ── 做法（两条，互不干扰）──
        //    ① 【布局照旧】`GUI.matrix` 的缩放**一个字节都不改** ⇒
        //       面板的位置 / 宽度 / 九宫格底 / 描边 / 进度条填充 / 图标 全部与改前**逐像素相同** ✓
        //    ② 【字形脱矩阵】画文字时临时 `GUI.matrix = identity`，矩形按**当下矩阵**换算成屏幕像素，
        //       字号取 `round(虚拟字号 × 1.35)` 的**整数** ⇒ 字形落在整数像素格点上 ✓
        //    换算式（1.35 下的最终取值，左=旧虚拟字号 → 右=新屏幕整数字号）：
        //      12→16   15→20   16→22   17→23   18→24   19→26   26→35   30→40
        //    ⇒ 旧视觉尺寸 = 字号 × 1.35，新字号是它的**最近整数** ⇒ 视觉尺寸变化 ≤ 1.4% ✓
        //
        //  ── 为什么矩形要"按当下矩阵换算"，不能直接乘 UiScale ──
        //    `GUI.BeginScrollView` 会在 `GUI.matrix` 上**再叠一层平移**。
        //    写死 `r * UiScale` 会把滚动偏移吃掉 ⇒ 正文滚不动 ✗
        //    `MultiplyPoint3x4` 用**当下的**矩阵 ⇒ 缩放与平移一起接住 ✓
        //
        //  ── 不许跟着改的（硬约束）──
        //    九宫格底 / 描边 / 进度条填充 / 图标 全部走 `GUI.Box` 与 `GUI.DrawTexture` ⇒
        //    **一律留在矩阵里** ✓ 改它们等于改布局 —— 那正是"把 ui.scale 调成 1.0"那种做法 ✗
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>本帧的 UI 缩放。字号换算要用它 ⇒ 在 `Draw` 最前面赋值（早于 `EnsureStyles`）。</summary>
        private float _k = 1f;

        /// <summary>
        /// 当前嵌套的 GUI 裁剪区层数（`GUI.BeginScrollView` 开/关各改一次）。
        /// &gt; 0 时 `Txt` 必须走原路：**裁剪区不能在我改矩阵的那一笔里**，
        /// 否则裁剪框会跟着矩阵缩放（实测会把「插件 0/6」切成「插件」）✗
        /// </summary>
        private int _clipDepth;

        /// <summary>虚拟字号 → **屏幕整数字号**。`GUIStyle.fontSize` 是 int，小数号必被矩阵拉糊。</summary>
        private int Fs(float virtualSize)
        {
            float k = _k > 0.01f ? _k : 1f;
            return Mathf.Max(6, Mathf.RoundToInt(virtualSize * k));
        }

        /// <summary>
        /// ★ v259：把 <paramref name="st"/> 的**八个状态文字色**全部钉成它此刻的 `normal` 色。
        ///
        /// ── 为什么必须钉（用户原话："移除这部分界面上的效果" ⇒ 纯文字一律不要悬停变色）──
        ///  本文件的文字样式全部是 `new GUIStyle(GUI.skin.label)` 克隆来的（见 `Style`），
        ///  而克隆**只**显式设了 `normal.textColor` —— 内置皮肤 label 的
        ///  `hover` / `active` / `focused` 文字色是**浅色** ✗
        ///  ⇒ 这种样式一旦被交给 `GUI.Button` 去画字，鼠标压上去字就变浅。
        ///  （同一个坑在主城/登录界面已按 v253 收口，见 `TownHud.PinStateText`；
        ///    那边是"创建处钉死"，这里是"上屏处钉死"——
        ///    本文件有 17 处 `new GUIStyle(...)`，逐个创建点改必然漏一个，
        ///    而 `Txt` 是**全文件唯一的文字出口** ⇒ 在这一处收口，覆盖率是**可证的 100%** ✓）
        ///
        /// ── 为什么带"发散才重钉"的判断 ✗ 不是偷懒，是必须自愈 ★
        ///  `HudLabel` / `LabelOutlined` 会**临时**改 `normal.textColor` 画四向描边再还原，
        ///  同一帧里同一个样式的 normal 会出现两种值。每趟无条件钉会把"临时色"钉进去，
        ///  反而让状态永远追不上 normal。这里改成：**只要有一个状态 ≠ normal 就整组钉一次**
        ///  ⇒ 不论谁在什么时候动过 normal，下一次上屏时八个状态都等于它 ✓
        ///  （代价 = 每个字 7 次 `Color ==` 比较，本工程每帧几百次文字绘制 ⇒ 可忽略，
        ///    且**一次分配都没有**，不碰 IMGUI 的 GC 红线 ✓）
        ///
        /// ── 它不改任何像素 ✗ ★ ──
        ///  `GUI.Label` 只读 `normal`（Unity 的 `GUIStyle.Draw(rect, content, controlID)` 传的是
        ///  `isHover:false / isActive:false`）⇒ 纯文字**本来就**不随悬停变色；
        ///  这里钉的是"将来/别处把它交给 `GUI.Button`"那条路。图元与布局一个字没动 ✓
        /// </summary>
        private static void PinStateText(GUIStyle st)
        {
            if (st == null) return;
            Color c = st.normal.textColor;
            if (st.hover.textColor == c && st.active.textColor == c && st.focused.textColor == c
                && st.onNormal.textColor == c && st.onHover.textColor == c
                && st.onActive.textColor == c && st.onFocused.textColor == c) return;
            st.hover.textColor = c;
            st.active.textColor = c;
            st.focused.textColor = c;
            st.onNormal.textColor = c;
            st.onHover.textColor = c;
            st.onActive.textColor = c;
            st.onFocused.textColor = c;
        }

        /// <summary>
        /// 文字的**唯一出口**（全文件 `GUI.Label` 都改走这里）。
        ///
        /// 三件事一起做，少一件就出错：
        ///   ① 矩形按 `GUI.matrix` 的**缩放**换算到屏幕像素（**不吃平移** —— 平移属于裁剪区，见 ③）
        ///   ② 字形原点对齐到**整数像素**：起点落在 .5 上时，整数号一样会被采出灰边
        ///   ③ **把裁剪区原点补回来** ★ 这一条是实测定出来的，不是推的 ★
        ///      Unity 的裁剪区原点**不存在 `GUI.matrix` 里**，而是在矩阵外面再乘一次 ⇒
        ///      只把矩阵设成 `identity` 的话，`GUI.BeginScrollView` 里的文字会整体**上移左移**
        ///      （实测：滚动区内 `GUIUtility.GUIToScreenPoint(0,0)=(365.11,260.00)` 而 `m03=0`）。
        ///      ⇒ 所以矩阵取 `TRS(clipOrigin × (k − 1))`：它抵消缩放、又保住裁剪原点 ✓
        ///      ⇒ 不在任何裁剪区里时 `clipOrigin = (0,0)` ⇒ 退化成 identity，与普通文字完全一致 ✓
        ///
        ///   ★ 为什么号在**这里**换算、而不是建样式时换算 ★
        ///     `GUIStyle.fontSize` 必须保持**虚拟字号**：`RowsFor` 的审计与 `CalcSize` 排布都按虚拟口径算；
        ///     而且下面这条兜底路径（裁剪区内）也要用虚拟字号在矩阵下画。
        ///     ⇒ 屏幕整数字号只在**这一次绘制**里临时挂上去，画完立刻还原 ✓
        /// </summary>
        private void Txt(Rect r, string text, GUIStyle style)
        {
            // ★ v259：文字不随鼠标变色 —— 八个状态在**上屏这一刻**钉成同一个色（见 `PinStateText`）★
            PinStateText(style);
            // ★ 本地化：战斗 HUD 的**所有**文字都从这里出去（两条分支都在下面共用这一行结果）★
            //   放进方法首行而不是两个出口各写一次 —— 免得将来加第三条分支时漏掉 ✓
            text = L10n.S(text);
            // ★ 字体跟着语言走（第二步）：把样式手里的字体重指到当前语言那一份。
            //   放在"绘制那一刻"而不是"建样式那一刻"——样式有 17 处工厂，这里只要一条就不会漏 ✓
            //   比较的是引用，切语言之前每帧都是一次指针比较，代价为零 ✓
            if (style != null && style.font != Font) style.font = Font;
            // ⚠️ 兜底：GUI 裁剪区里（滚动列表）**不能**改矩阵。
            //    实测：裁剪区尺寸会跟着当前矩阵一起缩放 ⇒ 矩阵改成 identity 后裁剪框缩成 1/1.35，
            //    菜单武器行的「插件 0/6」被切成了「插件」。这类区域走原路（仍是模糊的，见交付报告的"没做到"）✓
            if (_clipDepth > 0 || style == null)
            {
                GUI.Label(r, text, style);
                return;
            }

            Matrix4x4 cur = GUI.matrix;
            float k = Mathf.Abs(cur.m00) > 0.01f ? Mathf.Abs(cur.m00) : 1f;
            Vector2 c = GUIUtility.GUIToScreenPoint(Vector2.zero);   // 非裁剪区里恒为 (0,0)

            float x = Mathf.Round(r.x * k);
            float y = Mathf.Round(r.y * k);
            float w = Mathf.Ceil(r.width * k);
            float h = Mathf.Ceil(r.height * k);

            // 居中/右对齐时，字形左边界 = x + 对齐偏移，这个和常常是 .5
            // ⇒ 只圆 x 不够，必须把【实际字形起点】圆到整数。偏移量由 CalcSize 实测。
            if (!string.IsNullOrEmpty(text))
            {
                int vs = style.fontSize;
                style.fontSize = Fs(vs);
                float tw = style.CalcSize(new GUIContent(text)).x;
                switch (style.alignment)
                {
                    case TextAnchor.UpperCenter:
                    case TextAnchor.MiddleCenter:
                    case TextAnchor.LowerCenter:
                        {
                            float off = (w - tw) * 0.5f;
                            x = Mathf.Round(x + off) - off;
                            break;
                        }
                    case TextAnchor.UpperRight:
                    case TextAnchor.MiddleRight:
                    case TextAnchor.LowerRight:
                        {
                            float off = w - tw;
                            x = Mathf.Round(x + off) - off;
                            break;
                        }
                }
                style.fontSize = vs;
            }

            // 最终屏幕位置 = clipOrigin × k + 内容位置 ⇒ 取整后反推矩阵的平移量
            float fx = c.x * k + x;
            float fy = c.y * k + y;
            float tx = c.x * (k - 1f) + (Mathf.Round(fx) - fx);
            float ty = c.y * (k - 1f) + (Mathf.Round(fy) - fy);

            int prev = style.fontSize;
            style.fontSize = Fs(prev);              // 只在这一笔上换屏幕整数字号
            GUI.matrix = Matrix4x4.TRS(new Vector3(tx, ty, 0f), Quaternion.identity, Vector3.one);
            GUI.Label(new Rect(x, y, w, h), text, style);
            GUI.matrix = cur;
            style.fontSize = prev;                  // 还原成虚拟字号（审计与 CalcSize 依赖它）
        }

        /// <summary>
        /// 按钮文字样式：从**全局皮肤按钮**克隆（颜色/内边距/对齐都与改前一致）。
        /// 字号记成**虚拟字号** 16（= `Font` 建立时的字号，皮肤本身 `fontSize = 0` 时用的就是它），
        /// 屏幕整数字号由 `Txt` 在绘制那一刻换算 ✓
        /// </summary>
        private GUIStyle _btnText;
        private GUIStyle BtnText
        {
            get
            {
                if (_btnText == null)
                {
                    _btnText = new GUIStyle(GUI.skin.button);
                    _btnText.fontSize = _btnText.fontSize > 0
                        ? _btnText.fontSize
                        : (_btnText.font != null && _btnText.font.fontSize > 0 ? _btnText.font.fontSize : 12);
                }
                return _btnText;
            }
        }

        /// <summary>
        /// 按钮：**底与交互留在矩阵里**（九宫格拉伸、悬停/按下状态全不变），只有字走文字层。
        /// `GUI.Button(r, "")` 的做法会让字也跟着被矩阵拉糊 ⇒ 必须拆开画 ✓
        ///
        /// ★ v203：顺手把"这一格被鼠标压住了"记下来（同一个 `r`、同一个 `Event.current.mousePosition`）
        ///   ⇒ 光标（`GardenCursor`）的悬停判定**不需要第二套矩形表** ✓
        /// </summary>
        private bool Btn(Rect r, string text)
        {
            Event ev = Event.current;
            Vector2 m = ev != null ? ev.mousePosition : new Vector2(-99999f, -99999f);
            if (ProbeMouse.HasValue) m = ProbeMouse.Value;      // ★ 探针：只换坐标，不改判定
            if (r.Contains(m))
            {
                _hoverBtn = true;
                _hoverFrame = Time.frameCount;
            }
            _btnRects.Add(r);                                   // 探针读数（HUD 自己不读）
            _btnTexts.Add(text);                                // ★ v214：与上一条同下标（文本探针）
            bool hit = GUI.Button(r, GUIContent.none);
            if (!string.IsNullOrEmpty(text)) Txt(r, text, BtnText);
            return hit;
        }

        private static readonly Color PanelBg = new Color(0.06f, 0.07f, 0.09f, 0.82f);
        private static readonly Color BarBg = new Color(0.15f, 0.15f, 0.18f, 0.9f);

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v177：无底板 HUD —— 血条 / 倒计时 / 充能 / 切换武器 四类元素不再画背景 ★
        //
        //  需求原话："所有统一不使用背景 …… 要去掉背景（保留数值与形态本身，
        //            靠描边/发光/对比度而不是底板来保证可读性）"
        //
        //  ★ 为什么"把底板调淡一点"不算完成 ✗ ★
        //    仍然是底板：淡到 30% 的奶油色压在夜空上是一块灰，压在麦田上是一块更亮的灰 ——
        //    两头都不干净。**要么有底板，要么没有**，没有中间态。
        //
        //  ★ 那没有底板靠什么"看得见" ✓ ★ ——三层叠加，每一层治一种底图：
        //    ① 投影（向下偏移的暗线/暗块）
        //       治【亮底图】：麦田、沙地、正午草地。亮描边在亮底上会失明，暗投影不会。
        //    ② 描边（1.5px 暖白线）
        //       治【暗底图】：夜间林地、深绿灌木。暗投影在暗底上会失明，亮描边不会。
        //       ⇒ ①②**成对出现** ⇒ 无论底图亮还是暗，总有一半在起作用 ✓
        //    ③ 文字描边（四向 1.5px 近黑 + 本帧 `HudLabel`）
        //       治【高对比底图】：花丛、红果、木栅栏 —— 任何"半亮半暗"的杂乱背景。
        //       这是本工程已有的成熟做法（`DrawPopups` / `LabelOutlined` / 商店车「E」都在用），
        //       不是为这一版新发明的路子 ⇒ 与既有视觉语言一致 ✓
        //  ⇒ 验收判据不是"我觉得能看见"，而是**在最坏的三种底图上各拍一张实测图**（见报告）。
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>描边亮线（与奶油面板的主色同源，换成冷白会在木色界面里发蓝）。</summary>
        private static readonly Color HudStroke = new Color(1f, 0.95f, 0.80f, 0.92f);
        /// <summary>暗描边：用于"未填充的那一段"——它必须能看见（否则看不出条有多长），但不能抢眼。
        /// ⚠️ 0.40 实测不够：在**农田木色**（最亮的底）上，0% 的空条只剩这一圈线，
        /// 0.40 的暖白线压上去几乎与木色同值 ⇒ "护盾 0/2 / 经验 0/20" 会读成"没有这条"。
        /// 提到 0.66 后三种底图都成立（见交付报告的三张实测图）。这个数是**量出来的**，不是随手加的。</summary>
        private static readonly Color HudStrokeDim = new Color(1f, 0.95f, 0.78f, 0.66f);
        /// <summary>"当前武器"的描边色（亮绿）：与 v89 的高亮绿同族，只是不再用填充表达。</summary>
        private static readonly Color HudStrokeCurrent = new Color(0.62f, 1f, 0.55f, 0.95f);
        /// <summary>投影：偏冷的中性黑。纯黑会在地面亮部留下一圈脏边。</summary>
        private static readonly Color HudShadow = new Color(0.02f, 0.03f, 0.02f, 0.36f);
        /// <summary>描边内侧的压线：让"条"有一层厚度，像嵌进去而不是贴上去。</summary>
        private static readonly Color HudInnerLine = new Color(0f, 0f, 0f, 0.42f);

        // ── v83：收纳格 / 卡片底色改用**暖棕** ──
        // 旧版是冷灰蓝 (0.16, 0.18, 0.22 一类)。在一整套奶油木色的界面里，
        // 那些格子像从别的游戏里抠过来的黑洞——尤其商店与菜单里成排的种子格，
        // 整片黑压压的。底色跟着主色走（木色系），界面才是一体的。
        // 明度仍然压得比面板低，保证"凹进去的收纳格"这个语义没丢。
        private static readonly Color SlotBg = new Color(0.27f, 0.20f, 0.14f, 0.98f);        // 空格
        private static readonly Color SlotBgFilled = new Color(0.21f, 0.16f, 0.11f, 0.98f);  // 有东西
        private static readonly Color CardBg = new Color(0.24f, 0.18f, 0.12f, 0.96f);        // 商品卡
        private static readonly Color IconWellBg = new Color(0.32f, 0.24f, 0.16f, 1f);       // 图标底衬
        private static readonly Color ItemTileSel = new Color(0.40f, 0.30f, 0.17f, 1f);      // 道具格·选中

        // ── v96：卷轴纸纹（需求"为棕色的框增加美术效果，增加纹理模仿卷轴效果"）──
        //
        // 贴图本身是**灰度**的（均值 238，接近白），配合 GUI.color 相乘：
        // 棕色调上去就是"有纸纹的棕"。若贴图直接画成棕色，乘完会变脏黑。
        // 生成脚本：Tools/AssetPipeline/gen_scroll_tex.py（固定随机种子，可复现）
        private static Texture2D _paperTex;
        private static bool _paperTried;

        private static Texture2D PaperTex
        {
            get
            {
                if (!_paperTried)
                {
                    _paperTried = true;   // 失败也只试一次，避免每帧重复 Resources.Load
                    _paperTex = Resources.Load<Texture2D>("Art/UI/ui_scroll_paper");
                }
                return _paperTex;
            }
        }

        /// <summary>
        /// 用卷轴纸纹填充矩形。**平铺，不是拉伸** —— 一张 256px 的纸铺满 900px 宽的卡片，
        /// 拉伸会直接把纹路糊成渐变，而卷轴纹理的价值恰恰在纹路本身。
        /// 贴图取不到时退回纯色（`White` + 当前 GUI.color），界面不会因此坏掉。
        /// </summary>
        private void PaperFill(Rect r)
        {
            Texture2D tex = PaperTex;
            if (tex == null) { GUI.DrawTexture(r, White); return; }
            const float Tile = 128f;   // 一"张"纸在屏幕上的边长（像素）
            GUI.DrawTextureWithTexCoords(r, tex,
                new Rect(0f, 0f, Mathf.Max(1f, r.width / Tile), Mathf.Max(1f, r.height / Tile)));
        }

        /// <summary>
        /// v105：把一块棕色背景画成**卷轴的形**（需求"棕色的背景增加卷轴形状和纹理图案"）。
        ///
        /// 纹理上一轮已经做了（PaperFill 平铺），缺的是"形"——
        /// 光有纸纹只是"一张旧纸"，要读成**卷轴**必须有**两端卷起来的轴**。
        ///
        /// 做法（由外到内，全部纯绘制、不改布局）：
        ///   ① 左右各一根**轴杆**：比底色略亮的竖条，两端收窄模拟卷筒的弧面
        ///   ② 轴杆内侧一道**压痕**：暗线，表示纸绕在轴上的转折
        ///   ③ 上下各一道**边缘暗线**：卷轴的两条长边
        /// 只在宽度够的时候画轴杆（窄条上画两根杆会挤掉内容）。
        /// </summary>
        private void ScrollFrame(Rect r, float alpha = 1f)
        {
            if (r.width < 120f || r.height < 18f) return;   // 太窄：退回纯色块，别硬塞装饰

            Color prev = GUI.color;
            float barW = Mathf.Clamp(r.height * 0.16f, 4f, 11f);

            // ① 轴杆：比底色亮一点，两端各收 1px 做出"卷筒"的收口
            GUI.color = new Color(0.62f, 0.48f, 0.32f, 0.55f * alpha);
            GUI.DrawTexture(new Rect(r.x, r.y, barW, r.height), White);
            GUI.DrawTexture(new Rect(r.xMax - barW, r.y, barW, r.height), White);
            GUI.color = new Color(0.78f, 0.64f, 0.44f, 0.42f * alpha);
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, barW - 2f, r.height - 2f), White);
            GUI.DrawTexture(new Rect(r.xMax - barW + 1f, r.y + 1f, barW - 2f, r.height - 2f), White);

            // ② 轴杆内侧的压痕（纸绕上去的转折）
            GUI.color = new Color(0.10f, 0.07f, 0.04f, 0.42f * alpha);
            GUI.DrawTexture(new Rect(r.x + barW, r.y, 1.5f, r.height), White);
            GUI.DrawTexture(new Rect(r.xMax - barW - 1.5f, r.y, 1.5f, r.height), White);

            // ③ 上下长边的暗线
            GUI.color = new Color(0.10f, 0.07f, 0.04f, 0.30f * alpha);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1.5f), White);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), White);

            GUI.color = prev;
        }

        /// <summary>稀有度色：白 / 绿 / 蓝 / 紫 / 金（普通 / 一般 / 稀有 / 史诗 / 传奇）。
        /// 必须与 Rarity.Names 一一对应——两张表对不上号就是"传奇显示成紫色"那次的成因。</summary>
        public static Color QualityColor(int quality)
        {
            switch (quality)
            {
                case 0: return new Color(0.86f, 0.86f, 0.86f);
                case 1: return new Color(0.35f, 0.85f, 0.35f);
                case 2: return new Color(0.35f, 0.65f, 1f);
                case 3: return new Color(0.72f, 0.42f, 0.95f);
                default: return new Color(1f, 0.82f, 0.25f);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v180：插件槽 —— 品质色的**两种命名都认** ★
        //
        //  ── 为什么不能只认 `(int)quality` ✗ ★
        //    表里现在写的是 `Common / Uncommon / Rare / Epic / Legendary`（`plugins.json` ✓）。
        //    另一条任务会把同一批值改成 `D / C / B / A / S` ★ —— 那是**同一个维度的两套写法** ✓
        //    ⇒ 若这里按 `(int)` 取色，两套写法都能对（枚举序不变）；
        //      但若哪天枚举**加了一档**或**改了序**，`(int)` 会**静默错位** ✗（"传奇显示成紫色"就是这么来的）
        //    ⇒ 所以这里走**名字**：`Enum.ToString()` → 一张显式的名字表 ✓
        //      `Common`/`D` 都落到 0，`Legendary`/`S` 都落到 4 ⇒ 命名改了，映射**自动就对** ✓
        //
        //  ── 未知值必须安全 ✗ ★
        //    认不出的品质 ⇒ 返回 **-1** ⇒ 调用方退回"空格那种近黑"而不是猜一个颜色 ✓
        //    （猜颜色的代价是界面在撒谎；退回中性色的代价只是"没有品质感" ✓ 后者可接受 ✓）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>品质名 → 档位 0..4；认不出 ⇒ **-1**（调用方退回默认色，不猜）。</summary>
        public static int QualityTierOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            switch (name.Trim().ToLowerInvariant())
            {
                // 现用命名
                case "common": return 0;
                case "uncommon": return 1;
                case "rare": return 2;
                case "epic": return 3;
                case "legendary": return 4;
                // 另一条任务要改到的命名（D/C/B/A/S）
                case "d": return 0;
                case "c": return 1;
                case "b": return 2;
                case "a": return 3;
                case "s": return 4;
                // 纯数字（表里写成 0..4 也能认）
                case "0": return 0;
                case "1": return 1;
                case "2": return 2;
                case "3": return 3;
                case "4": return 4;
                default: return -1;
            }
        }

        /// <summary>枚举 → 档位（走名字 ⇒ 将来改名 `D/C/B/A/S` 也自动对）。</summary>
        public static int QualityTierOf(GoodsQuality q) => QualityTierOf(q.ToString());

        /// <summary>空插件槽的底色 = **近黑**（用户第 ② 条："空槽 = 黑色，纯黑或近黑，不要灰色"）。</summary>
        private static readonly Color PluginSlotEmpty = new Color(0.015f, 0.015f, 0.02f, 0.98f);

        /// <summary>空插件槽的细边（比近黑略亮一点点，仅为让 6 个格子数得出来）。</summary>
        private static readonly Color PluginSlotEdge = new Color(0.30f, 0.30f, 0.33f, 0.85f);

        /// <summary>品质色；**认不出 ⇒ 近黑**（与空槽同色，不猜、不崩）。</summary>
        public static Color PluginQualityColor(GoodsQuality q)
        {
            int tier = QualityTierOf(q);
            return tier >= 0 ? QualityColor(tier) : PluginSlotEmpty;
        }

        /// <summary>插件图标：工程里**没有**插件图标资源，缺图时返回 null（调用方画短名兜底）。
        /// 一旦 `Assets/Resources/Art/UI/icon_plugin_&lt;id&gt;.png` 存在，这里**自动**改成真图标 ✓
        /// ⚠️ 结果要缓存：`Resources.Load` 每帧被调 18 次（3 把 × 6 槽）是白白开销 ✗
        ///    连"没有这张图"也要缓存 —— 否则每帧都在资源系统里查一个**确定不存在**的名字 ✓</summary>
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _pluginIconCache =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        private static Texture2D PluginIconOf(PluginAsset a)
        {
            if (a == null) return null;
            return PluginIconOf(a.id);
        }

        /// <summary>
        /// ★ v274：按 **id** 取插件图标（商店货架要用）★
        /// 与上面那个重载共用同一张缓存 ⇒ 槽位与货架不会各查一遍；
        /// null 也存（缺图只查一次），与 `ItemIconOf` 同一条规矩 ✓
        /// </summary>
        private static Texture2D PluginIconOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_pluginIconCache.TryGetValue(id, out Texture2D cached)) return cached;
            Texture2D t = Resources.Load<Texture2D>("Art/UI/icon_plugin_" + id);
            _pluginIconCache[id] = t;      // null 也存 ⇒ 缺图只查一次
            return t;
        }

        private Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                return _white;
            }
        }

        private Font Font
        {
            get
            {
                // ★ 字体跟着语言走：切语言后 `L10n.FontGeneration` 加一 ⇒ 这里换字体。
                //   为什么要换：`Microsoft YaHei` 没有 Hangul、没有 Arabic ⇒ 韩文/乌尔都文会画成空白。
                //   为什么不在这里清样式的缓存：样式有 17 处 `new GUIStyle(...)`，
                //   逐个清必然漏；改成在**绘制那一刻**把字体重指到样式上（见 `Txt`）——
                //   任何样式只要上过一次屏就一定带着当前字体 ✓
                if (_font != null && _fontGen == L10n.FontGeneration) return _font;
                _fontGen = L10n.FontGeneration;
                _font = null;
                string[] candidates = L10n.Fonts;
                for (int i = 0; i < candidates.Length; i++)
                {
                    Font f = Font.CreateDynamicFontFromOSFont(candidates[i], 16);
                    if (f != null) { _font = f; break; }
                }
                return _font;
            }
        }

        private GUIStyle Style(ref GUIStyle style, int size, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            if (style != null) return style;
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                alignment = anchor,
                wordWrap = false
            };
            if (Font != null) style.font = Font;
            // v102：默认文字色 白 → **墨棕**。
            //
            // 界面在 v74 前后整体改成了奶油/木色底（Panel 系列），但样式的默认色还是白色，
            // 于是所有的面板标题、属性行、种子卡信息都变成了"亮底上的白字"——
            // 能看见，但对比度极低，读起来发飘。玩家反馈的就是这个。
            //
            // 为什么敢改默认值：现在的界面**绝大部分面积是亮底**，
            // 而少数暗面（深色条、弹出文字）在代码里本来就是**逐处显式设色**的
            // （见 WrapSmall / OnDark 系列 / DrawPopups 的 style.normal.textColor 赋值）。
            // 也就是说：改默认值影响的是"没被显式指定"的那些，而那些恰好都落在亮底上。
            //
            // ★ v177：这句话要补一句 —— 血条/倒计时/充能/切换武器四类**已经没有亮底了**，
            //   它们压在场景上，所以它们全部改走 `HudBody/HudBold/HudTitle/HudCenter`（近白 + 描边）。
            //   **墨棕这个默认值本身没有改**：它服务的仍然是按键说明、耕地提示、商店、菜单这些亮底 ✓
            //   ⇒ 又是一个"共用样式的默认值一动就打坏另一边"的现场：所以这里**不动默认值**，
            //     而是在四个调用点上显式选边（与 v102 立下的规矩同一条 ✓）
            style.normal.textColor = new Color(0.26f, 0.19f, 0.13f);
            return style;
        }

        // ------------------------------------------------------------ v11 升级抽卡（老虎机）
        //
        // ★ v182：抽卡的两段动画 —— 【出现】与【融合】★
        //
        //  ── ① 出现（逐张错峰入场）──
        //    v66 那版是"从下方 150px 滑进来 + 淡入"，五个毛病（改前的样子见 Docs/captures/v182_before_settled.png）：
        //      · **没有缩放**（用户要的 0.86→1.0 一次都没有）
        //      · 位移 150px 太大（一张牌横穿半个面板）· 缓出是二次方（用户要三次方 `1-(1-t)³`）
        //      · **动画期间按钮照样可点** ⇒ 会点到一张还在半透明的牌 ✗
        //      · 落位闪光的判据是 `1-|k-1|*7`：k 一旦到 1 就**恒等于 1** ⇒ **永久闪光**，
        //        每张牌被一层 55% 白膜盖住，看上去灰白一片、牌面像"没画完"✗（这就是改前截图的观感）
        //    v182：第 i 张 delay = i×0.06s，单张 0.28s，缓出 `1-(1-t)³`：
        //      缩放 0.86→1.0（以卡心为基准）· Y 偏移 +18px→0 · alpha 0→1 —— 三条共用同一条缓出 ✓
        //      整批时长 = (n-1)×0.06 + 0.28（3 张 = 0.40s）；这段期间**控件全部 `GUI.enabled=false`** ✓
        //      落位闪光改成"落地后 0.125s 内衰减到 0"⇒ 会停，不再永久压一层白 ✓
        //
        //  ── ② 融合（选中的卡飞进玩家）──
        //    0.36s 飞行：选中的卡 1.0→1.12 并飞向**左侧属性栏**（`DrawVitals` 那块：生命/护盾/经验/金币）。
        //                未选中的卡 alpha 1→0，横向朝自己那一侧散开 8px、同时下散 8px
        //    0.12s 白闪：到点后白 0.7→0（同一块矩形同时随闪变亮而放大 96×64 → 144×96）
        //    ⇒ ApplyCard 延后到动画播完，延后的那个点在 `ConsumeInto`（为什么在那里，见那里的注释）
        //
        //  ── 计时为什么挂在 `Layout` 上 ──
        //    `Draw` 一帧会被 Layout / Repaint / 若干鼠标事件**多次**调用；
        //    直接 `+= Time.deltaTime` 会让动画跑成两三倍速 ⇒ 只在 Layout 推进一次 ✓（v66 立的规矩，沿用）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>逐张错峰的间隔（秒）。第 1 张 0，第 2 张 +0.06，第 3 张 +0.12…</summary>
        private const float CardStagger = 0.06f;
        /// <summary>单张出现时长（秒）。</summary>
        private const float CardAppearDur = 0.28f;
        /// <summary>出现的起始缩放（0.86 → 1.0）。</summary>
        private const float CardAppearFromScale = 0.86f;
        /// <summary>出现的起始 Y 偏移（+18px → 0）。</summary>
        private const float CardAppearFromY = 18f;
        /// <summary>落位闪光衰减到 0 的时长（秒）。</summary>
        private const float CardLandFlashDur = 0.125f;
        /// <summary>融合飞行时长（秒）。</summary>
        private const float FuseFlyDur = 0.36f;
        /// <summary>融合到点后的白闪时长（秒）。</summary>
        private const float FuseFlashDur = 0.12f;
        /// <summary>被选中的卡放大到的倍数。</summary>
        private const float FuseScaleTo = 1.12f;
        /// <summary>未选中的卡散开量（px）。</summary>
        private const float FuseScatter = 8f;

        /// <summary>整批出现动画的总时长（n 张牌）。</summary>
        private static float CardAppearTotal(int n) => Mathf.Max(0, n - 1) * CardStagger + CardAppearDur;

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v185：**召唤轮转** ＋ **同 id 合并**（用户第 ①②③④ 条）
        //
        //  时间线（全部挂在 `DrawLevelReward` 里，用 `Time.deltaTime` 推进）：
        //    召唤轮转 1.20s  →  出现 0.28s×n（原有，未改）  →  [同 id ⇒ 合并 0.30s]  →  可点选
        //                  →  选中后原有的融合飞走 0.36s + 白闪 0.12s（未改）
        //
        //  ── 为什么"召唤"能插在"出现"前面而不是改"出现" ──
        //    `_cardAppearT` 只是**晚一点开始走**（`!summoning` 才 += dt），
        //    出现动画的三条曲线（缩放/位移/alpha）一个字节没动 ⇒ 上一轮的数不会被推倒 ✓
        //
        //  ── 6 张是怎么来的（用户第 ③ 条：纯视觉，与真实结果无关）──
        //    6 张 = 真实那 n 张的**两轮复制**：`g % n` 取真实卡面，
        //    后半轮（`g >= n`）画得更淡、收进槽位时融掉 ⇒ 视觉上就是"6 张轮转，最后留 3 张" ✓
        //    真实结果自始至终只有 `RunSession.RewardCards` 里那 n 张，本文件**一个字段都没写回** ✓
        //
        //  ── 为什么不可能"锁死"（上一轮栽的坑）──
        //    三段计时都是 `Mathf.Min(t + dt, 阶段时长 × 2)` ——
        //    **上界写在赋值里**，不是靠某条 if 记得清 ⇒ 结构上到不了"永真" ✓
        //    再加一条退路：召唤阶段面板右上角有**永远可点**的「跳过召唤 ▶」✓
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>召唤轮转的整段时长（秒）。落在用户要的 0.9~1.2s。</summary>
        private const float SummonDur = 1.20f;
        /// <summary>轮转圈数。★ 至少 1.5 圈 ⇒ 取 1.75。</summary>
        private const float SummonTurns = 1.75f;
        /// <summary>轮转几张（用户：每次抽卡召唤 6 张）。</summary>
        private const int SummonGhosts = 6;
        /// <summary>轮转起点缩放（从中心爆开时很小）。</summary>
        private const float SummonGhostScale0 = 0.34f;
        /// <summary>轮转途中的缩放。★ 落位时再插值到 `CardAppearFromScale`（3 卡不叠在一起才读得出"6 张在转"）</summary>
        private const float SummonGhostScale1 = 0.56f;
        /// <summary>t 超过它就开始从轨道收进槽位。</summary>
        private const float SummonLandStart = 0.66f;
        /// <summary>落位白闪时长（秒）—— 盖住"幻影 → 真卡"的换手那一帧，也是"两张合成一张"的爆点。</summary>
        private const float SummonFlashDur = 0.16f;
        /// <summary>椭圆轨道横向半径（px）。要大于一张卡的宽度，6 张才散得开。</summary>
        private const float SummonOrbitRx = 340f;
        /// <summary>椭圆轨道纵向半径（px）。比横半径小 ⇒ 读成"在空间里绕"而不是平面圆。</summary>
        private const float SummonOrbitRy = 96f;
        /// <summary>轨道中心相对面板顶的距离（px）。卡行是 `panel.y + 190` ⇒ 取 196 略高一点。</summary>
        private const float SummonOrbitCy = 196f;
        /// <summary>轨道虚线圈的段数（只画点，不画线 ⇒ 不碰 `GUI.matrix`）。</summary>
        private const int SummonRingDots = 72;

        /// <summary>同 id 两卡合并的整段时长（秒）。用户要 ~0.3s。</summary>
        private const float MergeDur = 0.30f;
        /// <summary>0 → 这个时刻：两卡靠拢并放大。</summary>
        private const float MergeTogetherEnd = 0.45f;
        /// <summary>到这个时刻：白闪，且只画一张（"合成一张"）。</summary>
        private const float MergeFlashEnd = 0.62f;

        /// <summary>召唤轮转是否还在播。★ 判据是"计时还没到" —— 计时只会往前走 ⇒ 不可能永真。</summary>
        private bool Summoning => _summonT < SummonDur;

        /// <summary>同 id 合并是否还在播。`_mergeT &lt; 0` = 没有。</summary>
        private bool Merging => _mergeT >= 0f;

        /// <summary>融合动画是否正在进行（含白闪段）。`_fuseT &lt; 0` = 没有。</summary>
        private bool Fusing => _fuseT >= 0f;

        /// <summary>取消"同 id 合并"（换牌 / 换局 / 播完）。</summary>
        private void CancelMerge()
        {
            _mergeT = -1f;
            _mergeA = -1;
            _mergeB = -1;
        }

        /// <summary>卡的身份（判"是不是同一张"用）。`Def` 可能为空 ⇒ 返回 null，调用方自己挡。</summary>
        private static string CardIdOf(RunSession.LevelRewardCard c)
            => c.Def == null ? null : c.Def.id;

        /// <summary>取消融合（换牌 / 换局 / 兜底）。清掉飞行状态与"待交出"标记。</summary>
        private void CancelFuse()
        {
            _fuseT = -1f;
            _fusePick = -1;
            _fuseReady = false;
        }

        /// <summary>抽卡面板此刻是否真的在画 —— `ConsumeInto` 靠它决定拦不拦输入。</summary>
        private bool RewardUiLive()
            => _uiScope != null && _uiScope.Phase == PhaseState.LevelReward && !_uiScope.Paused;

        private void DrawLevelReward(RunSession s)
        {
            Color prevBg = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0, 0, _virtualW, _virtualH), White);
            GUI.color = prevBg;

            float w = Mathf.Min(_virtualW - 80f, 880f);
            float h = 420f;
            Rect r = new Rect((_virtualW - w) * 0.5f, (_virtualH - h) * 0.5f, w, h);
            Panel(r);
            int n = Mathf.Max(1, s.RewardCards.Count);
            float cardW = Mathf.Min(240f, (w - 80f) / n - 12f);
            float totalW = n * cardW + (n - 1) * 16f;
            float x0 = r.x + (w - totalW) * 0.5f;

            // ① 计时：一帧只推进一次（理由见方法头"计时为什么挂在 Layout 上"）
            float dt = Event.current != null && Event.current.type == EventType.Layout ? Time.deltaTime : 0f;

            // 换了一批牌就重放出现动画。判据里**必须带 `RewardDrawIndex` / `RewardRerollCount`**：
            //   v66 的判据是 `张数 × 100003 + 等级`，而"第 2 次抽卡"的张数与等级**都可能不变**
            //   ⇒ 第二抽的牌不会重放动画（这是"一帧就摆出来"的另一层原因）✗
            //   抽卡升级只涨 `RewardDrawIndex`、重抽只涨 `RewardRerollCount` ⇒ 两个都要进判据 ✓
            // ★ v185：判据里再补一项 **这一批牌的身份** ★
            //   原有四项（第几抽 / 张数 / 等级 / 重抽次数）在**真实玩法**里每次都会变 ⇒ 够用；
            //   但"张数与等级都不变的两次不同发牌"（调试 `ForceReward` 连打，或将来一次升级发两批）
            //   会算出**同一个 sig** ⇒ 新牌不播召唤 ✗ 与用户要的"每次抽卡都召唤 6 张"不符。
            //   ⇒ 把每张牌的 `Def.id` 与 `Amount` 混进去：牌变了就一定重放，牌没变就一定不重放 ✓
            int cardSig = 17;
            for (int i = 0; i < s.RewardCards.Count; i++)
            {
                RunSession.LevelRewardCard c = s.RewardCards[i];
                int idHash = c.Def == null || c.Def.id == null ? 0 : c.Def.id.GetHashCode();
                cardSig = cardSig * 31 + idHash * 7 + c.Amount;
            }
            int sig = s.RewardDrawIndex * 100003 + s.RewardCards.Count * 101
                    + s.Progress.Level + s.RewardRerollCount * 7919 + cardSig;
            if (sig != _cardAppearSig)
            {
                _cardAppearSig = sig;
                _cardAppearT = 0f;
                _summonT = 0f;              // ★ v185：新的一批牌 ⇒ 召唤重放
                CancelMerge();
                _mergeChecked = false;
                CancelFuse();
            }

            // ④ 召唤计时：★ 上界写在赋值里（`Min(..., SummonDur*2)`）★
            //    这一行就是"不会锁死"的结构性证明：计时只增，且有上界 ⇒ `Summoning` 必在 1 个阶段时长后转假 ✓
            _summonT = Mathf.Min(_summonT + dt, SummonDur * 2f);
            bool summoning = Summoning;

            // ① 出现计时：**召唤播完才开始走**（出现动画本身的三条曲线一个字节没动）
            if (!summoning)
                _cardAppearT = Mathf.Min(_cardAppearT + dt, CardAppearTotal(s.RewardCards.Count) * 2f);
            bool appearing = _cardAppearT < CardAppearTotal(s.RewardCards.Count);

            // ② 融合计时：播完只置一次 `_fuseReady`，由 `ConsumeInto` 把确认真正发出去
            if (Fusing)
            {
                if (_fusePick < 0 || _fusePick >= s.RewardCards.Count) CancelFuse();
                else
                {
                    _fuseT += dt;
                    if (_fuseT >= FuseFlyDur + FuseFlashDur)
                    {
                        _fuseReady = true;
                        // 兜底：交出去之后 1 秒牌还没换（不该发生）就别把面板锁死
                        if (_fuseT > FuseFlyDur + FuseFlashDur + 1f) CancelFuse();
                    }
                }
            }

            float fly = Fusing ? Mathf.Clamp01(_fuseT / FuseFlyDur) : 0f;
            float flyEase = 1f - (1f - fly) * (1f - fly) * (1f - fly);
            float flash = Fusing ? Mathf.Clamp01((_fuseT - FuseFlyDur) / FuseFlashDur) : 0f;

            // ③ 同 id 合并（用户第 ④ 条）：**只判一次**，且必须在"出现播完"之后
            if (Merging)
            {
                // ★ 上界写在赋值里（阶段时长 × 2）★ —— 与召唤、出现、融合同一条纪律
                _mergeT = Mathf.Min(_mergeT + dt, MergeDur * 2f);
                if (_mergeT >= MergeDur) CancelMerge();      // 播完即结束 ⇒ 解锁（正常路径）
            }
            else if (!summoning && !appearing && !_mergeChecked && !Fusing)
            {
                _mergeChecked = true;
                int a = -1, b = -1;
                for (int i = 0; i < s.RewardCards.Count && a < 0; i++)
                    for (int j = i + 1; j < s.RewardCards.Count; j++)
                    {
                        string ia = CardIdOf(s.RewardCards[i]);
                        if (!string.IsNullOrEmpty(ia) && ia == CardIdOf(s.RewardCards[j])) { a = i; b = j; break; }
                    }
                // 演示开关（**默认关**）：只换判据、不改动画。真实数据下 id 必不同 —— 见 `MergeDemo` 的注释
                if (a < 0 && MergeDemo && s.RewardCards.Count >= 2) { a = 0; b = 1; }
                if (a >= 0) { _mergeA = a; _mergeB = b; _mergeT = 0f; }
            }

            float mergeT = Merging ? Mathf.Clamp01(_mergeT / MergeDur) : 0f;
            // 合并中点 = 两张卡槽心的平均（用槽位几何算，不依赖循环里的局部变量）
            Vector2 mergeMid = new Vector2(
                (x0 + _mergeA * (cardW + 16f) + x0 + _mergeB * (cardW + 16f)) * 0.5f + cardW * 0.5f,
                r.y + 90f + 100f);

            // 融合终点 = **左侧属性栏**（`DrawVitals` 那块：生命/护盾/经验/金币）。
            // 该阶段它不绘制，但落点就是它在屏幕上的那个位置 —— 牌飞进"玩家的属性"里 ✓
            Vector2 fuseTo = new Vector2(146f, r.y + 46f);
            // 动画期间输入锁定（`GUI.enabled` + `ConsumeInto` 两层）：
            // ★ v185：召唤 / 出现 / 同 id 合并 / 融合飞走 —— 四段全锁，进来点不动，走了自动解锁 ✓
            bool locked = summoning || appearing || Merging || Fusing;
            _lastLocked = locked;                   // 只读探针：把实际用到的值留给 ConsumeInto 打印

            // ⑤ 召唤轮转（用户第 ①②③ 条）：6 张幻影绕椭圆轨道 → 缓出减速 → 收进 3 个固定槽位
            if (summoning)
            {
                DrawSummonOrbit(s, r, cardW, x0);

                // ★★ 退路：召唤期间面板右上角挂一个**永远可点**的「跳过召唤 ▶」★★
                //    `GUI.enabled` 在这里被**强制 true**、且它自己不读 `locked` ⇒
                //    哪怕上面任何一段动画出了想不到的问题，这一步都能把 `_summonT` 直接推到终点 ✓
                bool prevSkip = GUI.enabled;
                GUI.enabled = true;
                if (Btn(new Rect(r.xMax - 130f, r.y + 10f, 118f, 26f), "跳过召唤 ▶"))
                    _summonT = SummonDur;
                GUI.enabled = prevSkip;
            }

            // ★ 面板抬头**画在召唤之后**：幻影会飞到面板上半部，抬头先画就会被半透明卡面糊住。
            //   只挪了绘制顺序，字号/文案/位置一个字节没动 ✓
            Txt(new Rect(r.x, r.y + 10, w, 34), "升级奖励 · 抽卡", _title);
            Txt(new Rect(r.x, r.y + 48, w, 24),
                $"第 {Mathf.Min(s.RewardDrawIndex + 1, s.RewardDrawTotal)}/{Mathf.Max(1, s.RewardDrawTotal)} 次　（相同属性已融合）", _center);

            for (int i = 0; i < s.RewardCards.Count; i++)
            {
                RunSession.LevelRewardCard card = s.RewardCards[i];
                Rect baseRect = new Rect(x0 + i * (cardW + 16f), r.y + 90, cardW, 200);

                // ★ 召唤阶段**不画真卡**：这 1.2s 里画的全是 `DrawSummonOrbit` 的 6 张幻影
                //   （真卡一张都没被改，只是晚 1.2s 出场 —— 见 ① 的计时门）✓
                if (summoning) continue;
                // ③ 出现：缩放 / 位移 / alpha 三条共用同一条三次方缓出（`1-(1-t)³`）
                float k = Mathf.Clamp01((_cardAppearT - i * CardStagger) / CardAppearDur);
                float ease = 1f - (1f - k) * (1f - k) * (1f - k);
                float scale = Mathf.LerpUnclamped(CardAppearFromScale, 1f, ease);
                Vector2 center = baseRect.center;
                center.y += (1f - ease) * CardAppearFromY;
                float alpha = ease;

                // ④ 融合：选中的卡放大并飞走；未选中的卡散开并淡出
                if (Fusing)
                {
                    if (i == _fusePick)
                    {
                        scale = Mathf.LerpUnclamped(1f, FuseScaleTo, flyEase);
                        center = Vector2.LerpUnclamped(center, fuseTo, flyEase);
                        alpha = 1f - flash;                 // 到点即消失，接下来交给白闪
                    }
                    else
                    {
                        float side = i < _fusePick ? -1f : 1f;
                        center.x += side * FuseScatter * flyEase;
                        center.y += FuseScatter * flyEase;
                        alpha = 1f - fly;                   // 1 → 0
                    }
                }
                // ④b 同 id 合并（用户第 ④ 条，**纯表现**）：
                //     0 → 0.45  两张朝**中点**靠拢并放大到 1.16
                //     0.45 → 0.62 白闪，且**只画一张**（"合成一张"）
                //     0.62 → 1.0  各自回到自己的槽位（数据没动，两张都还能点）
                if (Merging && (i == _mergeA || i == _mergeB))
                {
                    float toMid = Mathf.Clamp01(mergeT / MergeTogetherEnd);
                    toMid = 1f - (1f - toMid) * (1f - toMid) * (1f - toMid);
                    float backK = Mathf.Clamp01((mergeT - MergeFlashEnd) / (1f - MergeFlashEnd));
                    float back = 1f - (1f - backK) * (1f - backK) * (1f - backK);
                    float toward = toMid * (1f - back);              // 靠拢量（回位时退回 0）
                    center = Vector2.LerpUnclamped(baseRect.center, mergeMid, toward);
                    scale = Mathf.LerpUnclamped(1f, 1.16f, toward);
                    alpha = 1f;
                    // 白闪那一段只留一张（另一张不画）⇒ 读成"两张合成一张"
                    if (i == _mergeB && mergeT >= MergeTogetherEnd && mergeT < MergeFlashEnd) alpha = 0f;
                }

                alpha = Mathf.Clamp01(alpha);
                if (alpha <= 0.002f) continue;              // 彻底看不见就整张不画（不留"幽灵"）

                // 以卡心为基准缩放（不是以左上角为基准，否则牌会"从左上角长出来"）
                Rect c = new Rect(center.x - baseRect.width * scale * 0.5f,
                                  center.y - baseRect.height * scale * 0.5f,
                                  baseRect.width * scale, baseRect.height * scale);

                Color topColor = card.Copies >= 3 ? new Color(1f, 0.82f, 0.25f)
                               : card.Copies == 2 ? new Color(0.72f, 0.42f, 0.95f)
                               : new Color(0.35f, 0.65f, 1f);

                Color pv = GUI.color;
                GUI.color = new Color(CardBg.r, CardBg.g, CardBg.b, CardBg.a * alpha);
                GUI.DrawTexture(c, White);
                GUI.color = new Color(topColor.r, topColor.g, topColor.b, alpha);
                GUI.DrawTexture(new Rect(c.x, c.y, c.width, 6), White);

                // 落位闪光：**落地之后**才开始衰减。
                // 旧式 `1-|k-1|*7` 在 k=1 时恒为 1 ⇒ 永久闪光、牌面被 55% 白膜盖住 ✗
                float landT = _cardAppearT - i * CardStagger - CardAppearDur;
                float land = landT > 0f ? Mathf.Clamp01(1f - landT / CardLandFlashDur) : 0f;
                if (land > 0.01f)
                {
                    GUI.color = new Color(1f, 1f, 0.95f, land * 0.55f * alpha);
                    GUI.DrawTexture(new Rect(c.x - 3f, c.y - 3f, c.width + 6f, c.height + 6f), White);
                }

                bool selected = s.RewardSelected == i;
                if (selected)
                {
                    // 选中脉冲：边框随正弦呼吸
                    float pulse = 0.55f + 0.25f * Mathf.Sin(Time.time * 6f);
                    GUI.color = new Color(0.55f, 0.85f, 0.40f, pulse * alpha);
                    GUI.DrawTexture(new Rect(c.x - 5f, c.y - 5f, c.width + 10f, c.height + 10f), White);
                    GUI.color = new Color(CardBg.r, CardBg.g, CardBg.b, CardBg.a * alpha);
                    GUI.DrawTexture(c, White);
                    GUI.color = new Color(topColor.r, topColor.g, topColor.b, alpha);
                    GUI.DrawTexture(new Rect(c.x, c.y, c.width, 6), White);
                }
                GUI.color = pv;

                // 文字（连同按钮字）按同一个 alpha 淡入 / 淡出。
                // ★ 卡面是 `CardBg`（暗棕）⇒ 卡名走**暗底样式**（`OnDarkTitle`/`OnDarkCenter`）★
                //   改前用 `_title`/`_center`（墨棕）—— 那时卡面被永久白膜抬成浅灰，墨棕字才"碰巧"能读；
                //   白膜修掉之后墨棕压墨棕就看不见了（v102 那条规矩的又一次现场）✗
                Color titlePrev = OnDarkTitle.normal.textColor;
                Color centerPrev = OnDarkCenter.normal.textColor;
                Color btnPrev = BtnText.normal.textColor;
                OnDarkTitle.normal.textColor = new Color(titlePrev.r, titlePrev.g, titlePrev.b, alpha);
                OnDarkCenter.normal.textColor = new Color(centerPrev.r, centerPrev.g, centerPrev.b, alpha);
                BtnText.normal.textColor = new Color(btnPrev.r, btnPrev.g, btnPrev.b, btnPrev.a * alpha);
                Txt(new Rect(c.x + 10, c.y + 16, c.width - 20, 30), card.Label, OnDarkTitle);
                if (card.Copies > 1)
                    Txt(new Rect(c.x + 10, c.y + 56, c.width - 20, 24), $"融合 ×{card.Copies}", OnDarkCenter);

                // ★ 动画期间这张卡的按钮**不可点**（`GUI.enabled=false`）★
                //   只靠"画成半透明但照旧可点"是防不住的 —— 用户点的正是那张还在淡入的牌 ✗
                bool prevEnabled = GUI.enabled;
                GUI.enabled = prevEnabled && !locked;
                if (Btn(new Rect(c.x + 14, c.y + 140, c.width - 28, 44),
                        selected ? $"已选中（{i + 1}）" : $"选中（{i + 1}）"))
                    PendingRewardSelect = i;
                GUI.enabled = prevEnabled;

                OnDarkTitle.normal.textColor = titlePrev;
                OnDarkCenter.normal.textColor = centerPrev;
                BtnText.normal.textColor = btnPrev;
            }

            // ④c 同 id 合并的白闪：正中那张的位置（`mergeMid`），0.45→0.62 段
            if (Merging && mergeT >= MergeTogetherEnd && mergeT < MergeFlashEnd)
            {
                float mf = 1f - (mergeT - MergeTogetherEnd) / (MergeFlashEnd - MergeTogetherEnd);
                float mw = cardW * 1.16f + 26f * (1f - mf);
                float mh = 200f * 1.16f + 20f * (1f - mf);
                Color pm = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.85f * mf);
                GUI.DrawTexture(new Rect(mergeMid.x - mw * 0.5f, mergeMid.y - mh * 0.5f, mw, mh), White);
                GUI.color = pm;
            }

            // ⑤ 白闪：飞到点的那一下（alpha 0.7→0，0.12s），矩形随闪变亮同时放大
            if (Fusing && fly >= 1f)
            {
                float fw = 96f + 48f * flash;
                float fh = 64f + 32f * flash;
                Color pf = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.7f * (1f - flash));
                GUI.DrawTexture(new Rect(fuseTo.x - fw * 0.5f, fuseTo.y - fh * 0.5f, fw, fh), White);
                GUI.color = pf;
            }

            // ⑥ 底部两个按钮：动画期间一并禁用（飞行中重抽会把牌换掉，确认会提前 ApplyCard）
            bool hasPick = s.RewardSelected >= 0 && s.RewardSelected < s.RewardCards.Count;
            bool prevEn = GUI.enabled;
            GUI.enabled = prevEn && !locked;
            Color pc = GUI.color;
            if (!hasPick) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            // 这里**照旧**只写 `PendingRewardPick`：真正的 ApplyCard 被延后到
            // `ConsumeInto`（下一帧 Update 里消费）→ 那里把它变成"起飞"而不是"直接确认"。
            // 键盘空格与 RunBot 走同一处，三条输入源因此共用同一段动画 ✓
            if (Btn(new Rect(r.x + w * 0.5f - 250, r.y + h - 62, 220, 42),
                hasPick ? "确认（空格）：" + s.RewardCards[s.RewardSelected].Label : "请先选一张卡") && hasPick)
                PendingRewardPick = s.RewardSelected;
            GUI.color = pc;

            // v13：重抽消耗金币（无限次，价格递增）
            int rerollCost = s.RewardRerollCost;
            bool canReroll = s.Gold >= rerollCost;
            Color pr = GUI.color;
            if (!canReroll) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            if (Btn(new Rect(r.x + w * 0.5f + 30, r.y + h - 62, 240, 42),
                $"重抽 {rerollCost} 金币（R·金币 {s.Gold}）") && canReroll)
                PendingRewardReroll = true;
            GUI.color = pr;
            GUI.enabled = prevEn;
        }

        /// <summary>
        /// ★ v185：**召唤轮转**的表现（用户第 ①②③ 条）—— 纯画，不写任何数据 ★
        ///
        /// 6 张幻影：`g % n` 取真实卡面 ⇒ 后 n 张是前 n 张的**复制**（用户允许的"同一批 3 张的复制"）。
        ///   "让 6 张看起来不同"由三处保证：① 在环上的角度不同 ② 前后景缩放/明暗不同
        ///   ③ 复制品更淡 + 白描边 ⇒ 一眼能分出"哪些是幻影" ✓
        /// 轨迹：椭圆 `(rx·cosθ, ry·sinθ)`，θ = 起点 + `SummonTurns·2π·缓出(t)`，
        ///   缓出 = `1-(1-t)³` ⇒ **越转越慢**，且 θ 单调递增 ⇒ 真转满 1.75 圈 ✓
        /// 落位：`t > SummonLandStart` 后把轨道点插值到**槽位中心**（同为三次方缓出）；
        ///   复制品在插值过程中 alpha → 0 ⇒ 视觉结果就是"6 张 → 停在 3 张" ✓
        /// </summary>
        private void DrawSummonOrbit(RunSession s, Rect panel, float cardW, float x0)
        {
            int n = Mathf.Max(1, s.RewardCards.Count);
            float t = Mathf.Clamp01(_summonT / SummonDur);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);       // ★ 缓出 `1-(1-t)³`
            Vector2 orbitC = new Vector2(panel.x + panel.width * 0.5f, panel.y + SummonOrbitCy);

            // 从**面板中心爆开**：前 16% 半径 0→1（用户要的"从屏幕外/中心出现"）
            float grow = Mathf.Clamp01(t / 0.16f);
            grow = 1f - (1f - grow) * (1f - grow);
            float rx = SummonOrbitRx * grow, ry = SummonOrbitRy * grow;

            // 收进槽位的进度（三次方缓出）
            float landK = Mathf.Clamp01((t - SummonLandStart) / (1f - SummonLandStart));
            float land = 1f - (1f - landK) * (1f - landK) * (1f - landK);

            Color pv = GUI.color;

            // ── 轨道虚线圈：**只画点，不碰 `GUI.matrix`**（少一处能出错的地方）──
            for (int d = 0; d < SummonRingDots; d++)
            {
                float a = Mathf.PI * 2f / SummonRingDots * d;
                float dz = Mathf.Sin(a);                            // +1 近 / -1 远
                float sz = 2.8f + 2.0f * (dz * 0.5f + 0.5f);
                Vector2 p = orbitC + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                GUI.color = new Color(0.86f, 0.94f, 1f, (0.34f + 0.30f * (dz * 0.5f + 0.5f)) * grow * (1f - land));
                GUI.DrawTexture(new Rect(p.x - sz * 0.5f, p.y - sz * 0.5f, sz, sz), White);
            }

            // ── 6 张幻影：**按深度分两趟画**（后景先画）⇒ 前后遮挡对得上，才像"在空间里绕" ──
            for (int pass = 0; pass < 2; pass++)
            {
                for (int g = 0; g < SummonGhosts; g++)
                {
                    float a0 = Mathf.PI * 2f / SummonGhosts * g;
                    float ang = a0 + SummonTurns * Mathf.PI * 2f * ease;  // ★ 转数由缓出推 ⇒ 减速 ✓
                    float dz = Mathf.Sin(ang);
                    // 第一趟只画后景（dz<0），第二趟只画前景
                    if (pass == 0 ? dz >= 0f : dz < 0f) continue;

                    int slot = g % n;
                    RunSession.LevelRewardCard card = s.RewardCards[slot];
                    bool dup = g >= n;                               // 复制品（视觉上的"另外 3 张"）
                    Vector2 orbit = orbitC + new Vector2(Mathf.Cos(ang) * rx, Mathf.Sin(ang) * ry);
                    Vector2 slotC = new Vector2(x0 + slot * (cardW + 16f) + cardW * 0.5f, panel.y + 190f);
                    Vector2 pos = Vector2.LerpUnclamped(orbit, slotC, land);

                    // ★ 缩放三段：中心爆开(0.34) → 环上(0.56) → **落位时插值到 `CardAppearFromScale`**
                    //   （最后一跳保证"幻影 → 真卡"尺寸**无缝**，用户看不出换手 ✓）
                    float sc = Mathf.LerpUnclamped(SummonGhostScale0, SummonGhostScale1, ease)
                             + 0.055f * dz * (1f - land);
                    sc = Mathf.LerpUnclamped(sc, CardAppearFromScale, land);

                    float al = Mathf.Clamp01(_summonT / 0.12f);      // 淡入，不从"凭空出现"开始
                    al *= 0.70f + 0.30f * (dz * 0.5f + 0.5f);        // 远景更淡
                    al = Mathf.LerpUnclamped(al, 1f, land);          // 落地都变实
                    if (dup) al *= 1f - land;                        // ★ 复制品收进槽位时融掉 ⇒ 6 → 3
                    al = Mathf.Clamp01(al);
                    if (al <= 0.004f) continue;

                    Rect c = new Rect(pos.x - cardW * sc * 0.5f, pos.y - 200f * sc * 0.5f,
                                      cardW * sc, 200f * sc);
                    Color top = card.Copies >= 3 ? new Color(1f, 0.82f, 0.25f)
                              : card.Copies == 2 ? new Color(0.72f, 0.42f, 0.95f)
                              : new Color(0.35f, 0.65f, 1f);

                    GUI.color = new Color(CardBg.r, CardBg.g, CardBg.b, CardBg.a * al);
                    GUI.DrawTexture(c, White);
                    GUI.color = new Color(top.r, top.g, top.b, al);
                    GUI.DrawTexture(new Rect(c.x, c.y, c.width, 5), White);
                    if (dup)
                    {
                        // 复制品：白描边 ⇒ 6 张一眼能分出"这不是真的那 3 张"
                        GUI.color = new Color(1f, 1f, 1f, 0.55f * al);
                        GUI.DrawTexture(new Rect(c.x - 2f, c.y - 2f, c.width + 4f, 2f), White);
                        GUI.DrawTexture(new Rect(c.x - 2f, c.yMax, c.width + 4f, 2f), White);
                        GUI.DrawTexture(new Rect(c.x - 2f, c.y - 2f, 2f, c.height + 4f), White);
                        GUI.DrawTexture(new Rect(c.xMax, c.y - 2f, 2f, c.height + 4f), White);
                    }

                    // 幻影的卡名：★ 字号跟着 `sc` 缩 ★
                    //   卡面只有 240×sc 宽，用真实卡的字号会**两头被裁**（实测："暴击率 +3%" 被裁成 "击率 +3"）
                    //   ⇒ 用 `OnDarkCenter`（居中的小号暗底样式）再按 sc 缩字号，还原两处 ✓
                    Color tp = OnDarkCenter.normal.textColor;
                    int fs0 = OnDarkCenter.fontSize;
                    OnDarkCenter.normal.textColor = new Color(tp.r, tp.g, tp.b, al);
                    // ★ v261：**去掉这里写死的 10px 字号下限** ★
                    //   旧式 `Mathf.Max(10, round(fs0 * sc))` 是个"绝对值下限"：
                    //   `sc` 是召唤动画的**相对**缩放（0..1），当它缩到 0.4 时字号该是 6，却被抬到 10
                    //   ⇒ 字比卡面还宽，两头被裁（正是样式注释里记的那次"裁成半句"）。
                    //   字号下限只应有一处，就是 `Fs()`（它按**屏幕口径**兜 6）；
                    //   这里保留**虚拟口径**的等比缩小，不再另立一个数 ✓
                    if (fs0 > 0) OnDarkCenter.fontSize = Mathf.Max(1, Mathf.RoundToInt(fs0 * sc));
                    Txt(new Rect(c.x + 4f, c.y + c.height * 0.16f, c.width - 8f, 24f), card.Label, OnDarkCenter);
                    OnDarkCenter.fontSize = fs0;
                    OnDarkCenter.normal.textColor = tp;
                }
            }

            // ── 落位白闪：盖住"幻影消失 / 真卡出现"的那一帧（否则会看到一闪而空）──
            //    这也是"两张相同的收成一张"那一下的爆点：复制品正好在这一段 alpha → 0 ✓
            //    ★ 画法：**亮边 + 淡填充**，不是整块白膜 ★
            //      整块 0.62 白把人牌洗成灰片（第一版就这样，截图取证）⇒ 卡面读不出来 ✗
            float flashK = Mathf.Clamp01((_summonT - (SummonDur - SummonFlashDur)) / SummonFlashDur);
            if (flashK > 0.01f)
            {
                for (int slot = 0; slot < n; slot++)
                {
                    Rect sr = new Rect(x0 + slot * (cardW + 16f), panel.y + 90f, cardW, 200f);
                    GUI.color = new Color(1f, 1f, 1f, 0.30f * flashK);      // 淡填充：卡面照旧能看
                    GUI.DrawTexture(new Rect(sr.x - 4f, sr.y - 4f, sr.width + 8f, sr.height + 8f), White);
                    GUI.color = new Color(1f, 1f, 1f, 0.95f * flashK);      // 亮边：这是"闪"的读数
                    float bw = 5f;
                    GUI.DrawTexture(new Rect(sr.x - 6f, sr.y - 6f, sr.width + 12f, bw), White);
                    GUI.DrawTexture(new Rect(sr.x - 6f, sr.yMax + 6f - bw, sr.width + 12f, bw), White);
                    GUI.DrawTexture(new Rect(sr.x - 6f, sr.y - 6f, bw, sr.height + 12f), White);
                    GUI.DrawTexture(new Rect(sr.xMax + 6f - bw, sr.y - 6f, bw, sr.height + 12f), White);
                }
            }
            GUI.color = pv;
        }

        // ------------------------------------------------------------ v10 商店（左：商品 / 左下：持有物资 / 右：属性）
        private void DrawShop(RunSession s)
        {
            float pad = 12f;
            float top = 84f;
            float bottom = _virtualH - 30f;
            float leftW = _virtualW * 0.50f;
            float rightX = pad + leftW + pad;
            float rightW = _virtualW - rightX - pad;
            // ★ v180（用户第 ④ 条）：上栏 0.62 → 0.58，商品行跟着矮一档 ★
            //   量出来的账（_virtualH = 800 时）：bottom-top = 686
            //     0.62 ⇒ shopH 425.32 ⇒ 每行 (425.32-78)/4 = **86.83**
            //     0.58 ⇒ shopH 397.88 ⇒ 每行 (397.88-78)/4 = **79.97**（矮 6.86px ✓ "略降"）
            //   省下的 27.4px **全部给左下那块（种子 / 道具）** ⇒ 种子格与描述区都更宽敞 ✓
            //   而**字号一处未动**（`_small`/`_bold`/`OnDark*` 的 fontSize 全在本文件别处，未改）✓
            float shopH = (bottom - top) * 0.58f;

            // 左·上：商店商品
            Rect shop = new Rect(pad, top, leftW, shopH);
            Panel(shop);
            Txt(new Rect(shop.x, shop.y + 2, shop.width, 30), $"商店　金币 {s.Gold}", _bold);
            // v83：商品改**一行一件的「左图右文」**。
            //
            // 旧版是 4 列窄卡 + 下方独立详情区，有两个毛病：
            //   ① 卡片只有 ~200px 宽，名字/品质/价格被挤成三行，长名字还会被裁
            //   ② 描述放不下，只能挪到下方——于是"看商品"和"看说明"要上下扫视，
            //      而且详情区谁都能触发（悬停哪张显示哪张），玩家经常看串
            // 现在一行一件：左边图标+名字+品质+价格，中间整块宽度给描述，右边操作按钮。
            // 菜单里的种子条目也用同一套排布，界面才是一套语言。
            int offerCount = Mathf.Min(4, s.Shop.Offers.Count);
            float rowTop = shop.y + 30f;
            float rowAreaH = shop.yMax - 48f - rowTop;
            // ★ v180（第 ⑤ 条"尺寸只由数据决定"）：行高**除以固定的 4**，不再除以**当前货位数** ★
            //   改前 `rowAreaH / offerCount`：`offerCount` 是 `min(4, Offers.Count)` ⇒
            //   货位一旦少一个（3 个），四行**同时长高 33%** ⇒ 整块货架当帧重排 = "跳" ✗
            //   `Offers.Count` 取决于运行时数据（刷新商店 / 卖出 / 其它任务改表），
            //   而"这块货架一共几个位置"是**布局常量** —— 两件事不能共用一个数 ✓
            //   ⇒ 常量 `ShopOfferRows = 4`：货位变少时**行高不变**，只是下面空出来 ✓ 不可能跳 ✓
            const int ShopOfferRows = 4;
            float rowH = rowAreaH / ShopOfferRows;
            for (int i = 0; i < offerCount; i++)
            {
                ShopOffer o = s.Shop.Offers[i];
                Rect row = new Rect(shop.x + 8f, rowTop + i * rowH, shop.width - 16f, rowH - 4f);
                Color prev = GUI.color;

                GUI.color = o.Kind == OfferKind.Sold ? new Color(0.17f, 0.14f, 0.11f, 0.95f) : CardBg;
                PaperFill(row);   // v96：卷轴纸纹
                ScrollFrame(row); // v105：卷轴的形（两端轴杆 + 压痕 + 长边暗线）
                GUI.color = QualityColor(o.Quality);
                GUI.DrawTexture(new Rect(row.x, row.y, 5f, row.height), White);   // 左侧品质色条
                GUI.color = prev;

                if (o.Kind == OfferKind.Sold)
                {
                    GUI.color = new Color(0.55f, 0.48f, 0.40f, 1f);
                    Txt(new Rect(row.x + 16f, row.y + row.height * 0.5f - 12f, row.width - 32f, 24f),
                        "已售出　—　刷新商店换货", _small);
                    GUI.color = prev;
                    continue;
                }

                // ── 左：图标 + 名字 + 品质 + 价格 ──
                // ★ v180（用户第 ④ 条）：三行的行距收紧 5/26/44 → 4/23/40，**字号一个字节都没动** ✓
                //   （行高 22/20/20 也没动 ⇒ 只是"那一摞字"整体上移 4px、彼此靠近 2px ✓
                //     右侧文字底边从 y+64 收到 y+60 ⇒ row.height 收到 80 时上下各留 10px ✓）
                float iconSize = Mathf.Clamp(row.height - 16f, 34f, 56f);
                Rect icon = new Rect(row.x + 12f, row.y + (row.height - iconSize) * 0.5f, iconSize, iconSize);
                DrawOfferIcon(icon, QualityColor(o.Quality), IconResOf(o));
                float tx = icon.xMax + 10f;
                Txt(new Rect(tx, row.y + 4f, 132f, 22f), o.Label(), OnDarkBold);
                Txt(new Rect(tx, row.y + 23f, 132f, 20f), GradeText(o), OnDark);
                Txt(new Rect(tx, row.y + 40f, 132f, 20f), $"价格 {o.Price}", OnDark);

                // ── 中：描述详情（整块宽度，可滚动）──
                float btnW = 106f;
                float descX = tx + 138f;
                float descW = row.xMax - btnW - 16f - descX;
                if (descW > 80f)
                {
                    // 内边距 5/10 → 4/8：与左边那摞字的上边对齐（都在 row.y+4 起）
                    Rect view = new Rect(descX, row.y + 4f, descW, row.height - 8f);
                    DrawInfoList(view, OfferDetailLines(s, o), i, _offerScrollPerRow, "商店详情" + i);
                }

                // ── 右：操作按钮 ──
                // v180：按钮行距收紧（-30/+2 → -28/0，两按钮各 28/26 高 ⇒ 中间 2px 缝）
                bool afford = s.Gold >= o.Price;
                // ★ v287：插件除"买得起"之外还有一道"**装得下**" ★
                //   判据走 `RunSession.CanTakePlugin`（与 `TryBuy` 的购买前置校验**同一个出口**）⇒
                //   灰掉的那一格点下去也不会尝试买，"按钮亮着但点下去说没有位置"这种两套真相不会出现 ✓
                //   ⚠️ 只改了灰化判据，按钮的矩形/字号/行距一个字节没动（三块 HUD 的布局不在本轮范围内）
                bool takable = o.Kind != OfferKind.Plugin || s.CanTakePlugin(o.Plugin, out _);
                bool buyable = afford && takable;
                Color pv = GUI.color;
                if (!buyable) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.7f);
                if (Btn(new Rect(row.xMax - btnW - 8f, row.y + row.height * 0.5f - 28f, btnW, 28f),
                        $"购买（{i + 1}）") && buyable)
                    PendingBuyOffer = i;
                GUI.color = pv;
                Color pl = GUI.color;
                if (o.Locked) GUI.color = new Color(1f, 0.85f, 0.35f, 1f);
                if (Btn(new Rect(row.xMax - btnW - 8f, row.y + row.height * 0.5f, btnW, 26f),
                        o.Locked ? "已锁定（⇧）" : "锁定（⇧）"))
                    PendingOfferLock = i;
                GUI.color = pl;

                if (row.Contains(Event.current.mousePosition)) _offerShown = i;
            }

            // ★ v261 防重叠：这行价码与右侧「刷新商店（R）」按钮**共用一条基线** ★
            //   改前：框宽写死 200 ⇒ 右边界 shop.x+210，而按钮左缘在 shop.x+120
            //         ⇒ 价码一长（三位数以上 + 全角空格）文字就钻到按钮底下，
            //           而按钮是后画的 ⇒ 看起来像"文字被截断"（用户报的"超框/重叠"之一）✗
            //   改后：宽度 = `CalcSize` 实测，再**夹到按钮左缘前 8px**（= 102）
            //         ⇒ 无论价码几位，这行只在自己的格里，按钮永远不被压 ✓
            //   ⚠️ 量的是 `L10n.S(...)` 之后的串 —— `Txt` 内部先翻译再上屏，
            //      拿原文量宽度在英文下会偏窄（同一个坑在 `RowsFor` 那边已经踩过一次）✓
            string rerollLabel = L10n.S($"刷新商店　{s.Shop.RerollCost(s.T)}");
            float rerollW = Mathf.Max(60f, _small.CalcSize(new GUIContent(rerollLabel)).x + 4f);
            float rerollMax = 102f;                      // (shop.x+120 按钮左缘) - (shop.x+10 起点) - 8
            Txt(new Rect(shop.x + 10, shop.yMax - 34, Mathf.Min(rerollW, rerollMax), 26),
                rerollLabel, _small);
            if (Btn(new Rect(shop.x + 120, shop.yMax - 38, 160, 32), "刷新商店（R）")) PendingReroll = true;
            // ★★ v198（用户原话："删除商店一键合成按键"）★★
            //   删掉的是这个按钮（原来占 `shop.x + 288 .. +438`，150×32）：
            //       if (Btn(new Rect(shop.x + 288, shop.yMax - 38, 150, 32), "一键合成（M）") && anyPair)
            //           PendingMergeAll = true;
            //   ⚠️ 标签上的"（M）"**从来没有对应的快捷键**（grep `mKey` = 0 处）⇒
            //      这个按钮是"一键合成"的**唯一**入口，删它就是把这个功能从商店里拿掉 ✓
            //   ⚠️ 功能本体 `SeedBag.AutoMergeAll` 留着：自检三条断言在调它（合成规则本身没删）✓
            //
            //   ── 布局：不留空洞 ──
            //   原来那一格夹在「刷新商店（R）」(120..280) 与「继续（空格）」(xMax−200..xMax−10) 中间；
            //   直接删会留一条 160px 的空白 ⇒ 把**继续**往左**吃进**那一格
            //   （起点 x+288 → 保留原右边界 xMax−10）⇒ 这一行仍然是"左按钮 + 右按钮"，
            //   中间没有缺口、右边也仍然贴住面板边 ✓（按钮变宽，而它是这一屏的主操作，宽一点更醒目）
            // v154：计时制下这里不再"推进回合"，只是**解除暂停继续游戏** ✓
            // 需求："商店中下一波按钮修改为继续"
            float nextW = shop.xMax - 10f - (shop.x + 288f);
            if (Btn(new Rect(shop.x + 288, shop.yMax - 38, nextW, 32), "继续（空格）")) PendingNextWave = true;

            // 左·下：持有物资（种子 / 道具 分开）
            Rect inv = new Rect(pad, top + shopH + pad, leftW, bottom - (top + shopH + pad));
            Panel(inv);
            float half = inv.width * 0.5f - 12f;
            Txt(new Rect(inv.x + 10, inv.y + 2, half, 26), $"种子（{s.Bag.OccupiedCount}/{s.Bag.SlotCount}）", _bold);
            // v16：种子格放大 + 名称下方展示功能与数值 + 滑动条（空间不足时滚动）
            float cellW = Mathf.Max(150f, half - 26f);
            // ★ v180（用户第 ④ 条）：种子格 220 → **196**，内边距同时在 `SeedCardPanel` 里收紧 ★
            //   ⚠️ 字号一个字节没动 —— 296/196 那套"由内容算高度"的账已经在 `SeedCardPanel` 里立着，
            //      这里只把**格子高度**从 220 收到 196（6 格共省 144px 的滚动内容）✓
            //   ⚠️ 196 不是拍的：`SeedCardPanel` 里 图标 60 + 名字 24 + 品质 20 + 等级 20 = 124（上界），
            //      右侧视口 = 196 - 36(按钮预留+8) - 12(上下内边距) = 148 ≥ 4 行 88 ⇒ 不用滚动 ✓
            //      按钮顶 = cell.yMax - 25，左列底 = cell.y + 10 + 60 + 3 + 24 + 20 = cell.y + 117 < cell.y + 171 ✓
            float cellH = 196f;
            Rect seedView = new Rect(inv.x + 8, inv.y + 28, half + 4, inv.height - 34f);
            float seedContentH = Mathf.Max(seedView.height, s.Bag.SlotCount * (cellH + 8f) + 8f);
            // ★ v180（第 ⑤ 条）：**滚动量存储必须装得下全部格子** ★
            //   改前 `_seedScrollPerCard` 建长 4，而袋子有 6 格 ⇒ `DrawInfoList` 里
            //   `scrollIndex < scrollStore.Length` 对第 5/6 格**恒为假** ⇒ 它们的滚动量每帧读回 0
            //   ⇒ 玩家在那两格里滚一下，下一帧就弹回顶部 = **看得见的"跳"** ✗
            //   ⇒ 按 `Bag.SlotCount` 扩容（只增不减，一次分配）✓
            EnsureScrollStore(ref _seedScrollPerCard, s.Bag.SlotCount);
            _clipDepth++;   // v178：裁剪区内文字走原路（Txt 的兜底分支）
            _seedScroll = GUI.BeginScrollView(seedView, _seedScroll, new Rect(0, 0, seedView.width - 18, seedContentH));
            for (int i = 0; i < s.Bag.SlotCount; i++)
            {
                SeedSlot slot = s.Bag[i];
                Rect cell = new Rect(2, i * (cellH + 8f), cellW, cellH);
                Color prev = GUI.color;
                GUI.color = slot.IsEmpty ? SlotBg : SlotBgFilled;
                if (i == s.MergeSelected) GUI.color = new Color(0.42f, 0.36f, 0.16f, 0.98f);
                PaperFill(cell);   // v96：卷轴纸纹（选中态也走同一张纹，只是着色不同）
                GUI.color = prev;
                if (slot.IsEmpty)
                {
                    Txt(new Rect(cell.x + 8, cell.y + 6, cell.width - 16, 24), $"第 {i + 1} 格　空", _small);
                    continue;
                }
                GUI.color = QualityColor(slot.Quality);
                GUI.DrawTexture(new Rect(cell.x, cell.y, cell.width, 5), White);
                GUI.color = prev;
                // v19：统一卡片——信息区自带滑动条，底部按钮独立成区（不再压文字）
                ButtonReserve = 28f;   // 底部按钮行的高度：合成 + 出售 并排
                SeedCardPanel(s, slot, cell, i, _seedScrollPerCard);
                ButtonReserve = 0f;

                // ── 底部按钮行：**合成** + 出售 ──
                // v44 曾把这一行删到只剩出售 ✗ 合成回归后两个按钮**并排**各占半行 ✓
                int otherSlot;
                bool canMerge = s.Bag.CanMergeWithOther(i, out otherSlot);
                bool maxed = slot.Level >= SeedSlot.MaxLevel;
                float btnY = cell.yMax - 25f;
                float btnW2 = (cell.width - 18f) * 0.5f;

                Color pm = GUI.color;
                if (!canMerge) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
                // 按钮文案直接说清"点了会变成几级"与"为什么点不动" ✗
                // 只写"合成"的话，玩家不知道要凑同种同级，只会反复点一个灰按钮 ✓
                string mergeText = canMerge ? $"合成 → {slot.Level + 1} 级"
                                 : maxed ? $"{SeedSlot.MaxLevel} 级封顶"
                                 : "合成（需同级）";
                if (Btn(new Rect(cell.x + 6f, btnY, btnW2, 20f), mergeText) && canMerge)
                    PendingSlotMerge = i;
                GUI.color = pm;

                // v19：只剩 1 个种子时禁止出售
                bool canSell = s.Bag.OccupiedCount > 1;
                Color ps = GUI.color;
                if (!canSell) GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.75f);
                if (Btn(new Rect(cell.x + cell.width - btnW2 - 6f, btnY, btnW2, 20f),
                    canSell ? $"出售 {slot.SellValueFor(s.WaveIndex)}" : "至少保留 1 个") && canSell) PendingSell = i;
                GUI.color = ps;
            }
            _clipDepth--;   // v178：离开裁剪区
            GUI.EndScrollView();

            float ix = inv.x + half + 20;
            Txt(new Rect(ix, inv.y + 2, half, 26), "道具", _bold);
            int shown = 0;
            foreach (var kv in s.ItemStacks)
            {
                ItemDef def = TableDatabase.GetItem(kv.Key);
                if (def == null) continue;
                Txt(new Rect(ix, inv.y + 30 + shown * 24, half, 22),
                    $"{s_rarityName(def.RarityTier)} {def.Name} ×{kv.Value}", _small);
                shown++;
                if (shown >= 8) break;
            }
            if (shown == 0) Txt(new Rect(ix, inv.y + 30, half, 22), "（还没有道具）", _small);

            // 右：★ v172 从"只有角色属性"改成"武器与插件 + 角色属性"★
            //
            // 用户标准（原话）："同步更新 UI 框，按照我说的标准，★ 预留更大的区域给角色武器，
            // 商店、菜单都是 ★"
            // ⇒ 商店是**买插件的地方**，而买插件时只有两件事要判断：
            //   ① 这把武器还剩几个槽（6 个格子画出来 ✓ 空槽也画 ✓）
            //   ② 这件插件装上会改哪一环（`WeaponSystem.ChannelOf` ✓ 框里的摘要行 ✓）
            // ⇒ 所以武器区是右栏的**主要部分**，属性退到下面（属性面板本身自带滚动 ✓ 矮也能用 ✓）
            //
            // 宽度为什么从 0.60 收到 0.50：右栏要给到 675px 才放得下"三把武器各一个 168px 的框"✗
            // 而左栏（货架 4 行 + 种子 / 道具）在 711px 下仍然够宽 ✓
            // ★ v179：武器区**按内容定高**（当时 `WeaponSectionH` = 84），省下的高度给角色属性 ★
            //   改前：`statsH = 130f` 写死、武器区吃掉剩余 ~660px ⇒
            //     ① 三把武器缩成一行格子后，那 660px 里会有 ~570px 的空白奶油板（"面板没画完"的观感 ✗）
            //     ② 而角色属性被压在 130px 里、只能靠滚动看（它自己带滚动条 ✓ 但没必要这么矮 ✗）
            //   ⇒ 两块高度互换：武器按内容定高、属性拿到剩下的全部 ✓ 两者都更合理 ✓
            //   ⚠️ v181：那个 84 的常量已被删（现在两个调用方都带 6 个槽）⇒ 商店这里是 114 ✓
            float weaponH = WeaponSlotSectionH;   // ★ v180：每把武器下面多一排 6 个插件槽（84 → 114）
            float statsH = bottom - top - weaponH - pad;
            // 标题同步去掉"与插件"：插件槽已按用户第 ② 条从这块 UI 删除 ⇒ 再写就是撒谎 ✓
            // ★ v181：商店**不加**数值行（`withStats: false`）⇒ 这一块的像素与 v180 完全一致 ✓
            //   理由：商店右栏每格 ~214px，"伤害 4 · 范围 3.2m · 攻速 0.70s"放不下；
            //   而用户这次要的是**菜单页**的完整显示（商店那版是别人正在改的区域，不动它）✓
            DrawWeaponSection(s, new Rect(rightX, top, rightW, weaponH), "我的武器", withStats: false, withSlots: true);
            Panel(new Rect(rightX, top + weaponH + pad, rightW, statsH));
            DrawStatsColumns(s, rightX, top + weaponH + pad, rightW, statsH, "角色属性");

            // ★ v180：布局探针（只读）★ —— 把这一帧算出的**全部布局入参与结果**记成一行。
            //   逐帧比对：同一份数据下这些数**一个字节都不许变** ✓
            //   记入参（Screen / Offers.Count / SlotCount / 滚动量）而不只记矩形，
            //   是为了能回答"变了以后为什么变"，而不只是"变了" ✓
            if (LayoutProbeOn)
            {
                _layoutProbeFrame++;
                var offers = new System.Text.StringBuilder();
                for (int i = 0; i < s.Shop.Offers.Count; i++)
                {
                    ShopOffer oo = s.Shop.Offers[i];
                    offers.Append(i).Append(':').Append(oo.Kind).Append('/').Append(oo.Price)
                          .Append(oo.Locked ? "/L" : "").Append(' ');
                }
                ProbeLine($"#{_layoutProbeFrame} scr={Screen.width}x{Screen.height}"
                    + $" mouse=({Event.current.mousePosition.x:0.##},{Event.current.mousePosition.y:0.##})"
                    + $" v={_virtualW:0.###}x{_virtualH:0.###} k={_k:0.###}"
                    + $" shop=({shop.x:0.###},{shop.y:0.###},{shop.width:0.###},{shop.height:0.###})"
                    + $" shopH={shopH:0.###} n={offerCount}/{s.Shop.Offers.Count} rowH={rowH:0.###}"
                    + $" inv=({inv.x:0.###},{inv.y:0.###},{inv.width:0.###},{inv.height:0.###}) half={half:0.###}"
                    + $" cell=({cellW:0.###}x{cellH:0.###})"
                    + $" seedView=({seedView.y:0.###}+{seedView.height:0.###}) contentH={seedContentH:0.###}"
                    + $" seedScroll=({_seedScroll.x:0.###},{_seedScroll.y:0.###}) slots={s.Bag.SlotCount}"
                    + $" weaponH={weaponH:0.###} statsH={statsH:0.###}"
                    + $" rowScroll={_offerScrollPerRow[0]:0.##}/{_offerScrollPerRow[1]:0.##}/{_offerScrollPerRow[2]:0.##}/{_offerScrollPerRow[3]:0.##}"
                    + $" statScroll={_statScroll.y:0.###} gold={s.Gold}"
                    + $" offerShown={_offerShown} off=[{offers}]");
            }
        }

        /// <summary>v49：绝对值型属性 → 相对开局基准的增量（开局显示 +0）。公开以便自检直接复核。</summary>
        public static string AddFlat(float current, float baseValue)
        {
            float d = current - baseValue;
            return $"{(d >= 0f ? "+" : "")}{d:0.#}";
        }

        /// <summary>v49：比率/百分点型属性 → 差值 ×100（开局显示 +0%）。</summary>
        public static string AddPoints(float current, float baseValue)
        {
            int pct = Mathf.RoundToInt((current - baseValue) * 100f);
            return $"{(pct >= 0 ? "+" : "")}{pct}%";
        }

        /// <summary>
        /// v49：**乘法**成长的属性 → 相对开局的**涨幅**。
        /// 用涨幅而不是百分点差，是为了跟道具/卡面上写的百分比一致：
        /// 例如"移动速度 +3%"是 ×1.03，基数 5.5 上只多 0.165，按差值显示会变成 "+0%"。
        ///
        /// ★ v185：这条注释原来拿**暴击率**当例子，那个例子是**错的** ★
        ///   `CritRate` 基数 0.05、卡面 `Add amount 3`、引擎走 `amount/100` **加法**
        ///   ⇒ 它属于 <see cref="AddPoints"/>，不属于这里（错例导致面板显示 +240%，见下方调用点注释）。
        ///   **判据只有一条**：`TableDatabase.IsFractionStat(stat)` 为真 ⇒ `AddPoints`；否则 ⇒ 本函数 ✓
        /// </summary>
        public static string AddRelative(float current, float baseValue)
        {
            if (baseValue <= 0.0001f) return AddPoints(current, baseValue);
            int pct = Mathf.RoundToInt((current / baseValue - 1f) * 100f);
            return $"{(pct >= 0 ? "+" : "")}{pct}%";
        }

        /// <summary>v49：面板改为全增量显示后，绝对值仍要可见——这里给出三把武器的实算伤害。
        /// ★ v186：去掉 `× ToolMult(品质)`（该函数已删）——式子只剩 `(基础 + 对应伤害) × 伤害倍数` ★</summary>
        private static string DamagePreviewLine(RunSession s, StatBlock st)
        {
            TuningDef t = TableDatabase.Tuning;
            // v162（A3）：三把武器各吃**自己的**伤害属性，公式 (基础 + 对应属性) × 伤害倍数
            float melee = st.Get(StatId.MeleeDamage);
            float ranged = st.Get(StatId.RangedDamage);
            float blastStat = st.Get(StatId.BlastDamage);
            float mult = st.Get(StatId.AtkMult);
            float critDmg = st.Get(StatId.CritDmg);
            float hoe = DamageResolver.WeaponDamage(t.tools.hoe.baseValue, melee, mult, false, critDmg, 0f);
            float can = DamageResolver.WeaponDamage(t.tools.can.baseValue, ranged, mult, false, critDmg, 0f);
            // v109：爆炸 = 肥料这件武器走一遍普通公式，再乘爆炸倍率（与运行时同一条路径）
            // ★ v195：爆炸倍率已删除 ⇒ 肥料与另外两把武器**同一个函数、同一条式子** ★
            float blast = DamageResolver.WeaponBlastDamage(t.tools.fertilizer.baseValue, blastStat, mult,
                false, critDmg, 0f);
            return $"锄 {hoe:0.#}　壶 {can:0.#}　肥爆 {blast:0.#}";
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v199（用户原话："为属性中存在上限的属性增加上限描述，'上限 + 实际上限值' 的形式"）★★
        //
        // 为什么不是"把 `CapsDef` 抄进 UI"：
        //   真正生效的上限**不只在** `caps` 里 —— `StatBlock.CapOf` 还有四条硬编码的
        //   （`HeartDropRate` 3 / `HeartHeal` 99 / `PigRate` 3 / `Luck` 1）以及两条由道具改写的
        //   （`AtkSpeedCapFixed*`，赌命手套"攻速上限 −200%"）⇒ 照 `caps` 抄一遍会印出**错的**上限 ✗
        // ⇒ 这里用一块**探针属性块**把值顶到天上再读回来，读到的就是 `CapOf` 钳完的结果：
        //   与游戏内走的是**同一个函数**，不复制任何一张表 ✓（`FloorOf` 同样在里面生效，
        //   而它只会把 0/1 那几条抬起来、抬不到 cap 之上 ⇒ 不影响读数 ✓）
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>探针用的"顶到天上"的值：比表里任何一个 cap 都大（表内最大 999）。</summary>
        private const float CapProbeValue = 1e7f;

        private StatBlock _capProbe;
        private TuningDef _capProbeTuning;

        /// <summary>★ v199：某条属性**实际生效的上限**（+∞ / ≥ `CapProbeValue` = 没有上限）。</summary>
        private float CapOfForHud(StatId id)
        {
            TuningDef t = TableDatabase.Tuning;
            if (_capProbe == null || !ReferenceEquals(_capProbeTuning, t))
            {
                _capProbeTuning = t;
                _capProbe = new StatBlock(t != null ? t.caps : null, t != null ? t.player : null);
            }
            _capProbe.SetBase(id, CapProbeValue);
            return _capProbe.Get(id);
        }

        /// <summary>
        /// ★ v212：**哪些属性才印「（上限 X）」** —— 用户原话："只有特定嘱咐过上限的才显示"。
        /// 白名单（**五项，逐条对应父任务点名的那五项**）：
        /// · `CritRate` / `Dodge`（用户点名的概率两项，**不含 `Luck`** —— 点名清单里没有它）
        /// · `HeartDropRate` / `PigRate`（**是等级映射**，硬上限等级 3，钳错了会落到 `*ByLevel` 表外）
        /// · `MaxPlots`（耕地上限，用户提过"每升 1 级 +1"、有硬顶）
        /// 其余一律不印：v210 放开的八项（三种伤害 / 攻击范围 / 爆炸范围 / 攻速 / 移速 / 击退）
        /// 与 `HeartHeal` 都是**哨兵 9999 = 无上限**，玩家不该看到"上限 9999" ✗
        /// ★ v212：`Luck` 从白名单里**移除**（上一版本把它算作"概率三项"，但那不是用户点名的清单）
        ///   ⇒ 幸运值那一行现在只印 `+N%`，不再印「（上限 100%）」✓
        /// </summary>
        private static bool CapShownFor(StatId id)
        {
            switch (id)
            {
                case StatId.CritRate:
                case StatId.Dodge:
                case StatId.HeartDropRate:
                case StatId.PigRate:
                case StatId.MaxPlots:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// ★ v199：行尾的「（上限 X）」后缀 —— **没有上限就返回空串**（用户："无上限的不印"）✓
        /// 单位按属性类别走，**不混用**：概率印 `%`／等级印 `级`／乘区印 `×`／其余印裸数 ✓
        /// （`internal` 是为了让只读探针能印出**屏幕上的原文**，见 `ProbeV199Quick`。）
        /// </summary>
        internal string CapSuffix(StatId id)
        {
            // ★ v212（用户原话："属性中所有我没提到有上限的（那些上限9999）全部不显示上限，
            //   只有特定嘱咐过上限的才显示"）★★
            //
            // 病因：v210 把八项 caps 和 HeartHeal 改成 **9999（哨兵 = 无上限）**，
            //   而这里的判据是 `cap >= CapProbeValue`，`CapProbeValue = **1e7（一千万）**`
            //   ⇒ `9999 < 1e7` ⇒ **哨兵被当成"真有上限"** ⇒ 那九项**照旧印「（上限 9999）」** ✗
            //
            // 改法：从"按数值判"改成**白名单判** —— 只有用户明确嘱咐过上限的属性才印后缀，
            //   其余一律空串。白名单 = 概率三项 + 等级两项 + 耕地上限一项。
            //   ★ 为什么用白名单而不是把 CapProbeValue 调小 ★
            //     `CapOfForHud` 内部拿 `CapProbeValue` 当"探针基数"去问 `StatBlock` 要钳位值
            //     （见 `_capProbe.SetBase(id, CapProbeValue)`），把它调小会让探针本身失真 ✗
            //   ★ 白名单是**显式**的：以后再加无上限属性，不用记得改任何阈值 ✓
            if (!CapShownFor(id)) return "";
            float cap = CapOfForHud(id);
            if (float.IsInfinity(cap) || cap >= CapProbeValue) return "";
            switch (id)
            {
                case StatId.CritRate:
                case StatId.Dodge:
                case StatId.Luck:
                    return $"（上限 {cap * 100f:0.#}%）";
                case StatId.HeartDropRate:
                case StatId.PigRate:
                    return $"（上限 {Mathf.RoundToInt(cap)} 级）";
                case StatId.AtkSpeed:
                case StatId.AtkRange:
                case StatId.MoveSpeed:
                case StatId.ExplodeRange:
                    return $"（上限 ×{cap:0.##}）";
                case StatId.MaxPlots:
                    return $"（上限 {Mathf.RoundToInt(cap)} 块）";
                default:
                    return $"（上限 {cap:0.##}）";
            }
        }

        /// <summary>主要 / 次要属性两段式展示（商店与菜单共用）。</summary>
        private void DrawStatsColumns(RunSession s, float x, float y, float w, float h, string title)
        {
            Txt(new Rect(x + 12, y + 2, w - 20, 28), title, _bold);
            Rect view = new Rect(x + 4, y + 32, w - 8, h - 40);
            float contentH = 620f;
            _statContentW = Mathf.Max(120f, view.width - 24f);
            _clipDepth++;   // v178：同上
            _statScroll = GUI.BeginScrollView(view, _statScroll, new Rect(0, 0, view.width - 18, contentH));
            StatBlock st = s.Progress.Stats;
            // v49：属性面板一律显示「相对开局基准的增量」——**开局全部是 +0 / +0%**。
            //      基准取自 tuning.player（开局值的唯一来源），不再在调用处写死数字：
            //      旧版把移动速度基准写成 5.0，基础移速改成 6.5 之后开局就显示成 "+150%"。
            //      乘法成长的属性按「相对涨幅」显示，这样与道具/卡面上写的 "+5%" 一致。
            PlayerBaseDef bs = TableDatabase.Tuning.player;
            float ly = 0f;
            ly = StatLine(ly, "— 主要属性 —", true);
            // v108：**最大生命 / 护盾用实际值**（需求）。
            // 其余属性是增量（"+5%"这种），但这两个是"我现在有多少血"——
            // 显示增量等于把玩家最需要的一个数藏起来，还得自己做加法。
            ly = StatLine(ly, $"最大生命　{st.Get(StatId.MaxHp):0}");
            ly = StatLine(ly, $"护盾　{st.Get(StatId.Shield):0}");
            // v162（A1/A2）：通用「攻击伤害」拆成三种独立伤害类型——锄头吃近战、水壶吃远程、肥料吃爆炸。
            // 三条各自独立成长，所以这里必须**分成三行**显示：
            // 合成一行的话，玩家买了只加近战的道具会看到"攻击伤害 +1"，
            // 然后发现水壶一点没变强——那是显示在撒谎。
            ly = StatLine(ly, $"近战伤害　{AddFlat(st.Get(StatId.MeleeDamage), bs.meleeDamage)}{CapSuffix(StatId.MeleeDamage)}");
            ly = StatLine(ly, $"远程伤害　{AddFlat(st.Get(StatId.RangedDamage), bs.rangedDamage)}{CapSuffix(StatId.RangedDamage)}");
            ly = StatLine(ly, $"爆炸伤害　{AddFlat(st.Get(StatId.BlastDamage), bs.blastDamage)}{CapSuffix(StatId.BlastDamage)}");
            // ★★ v195：**「爆炸倍率」那一栏连同整条属性一起删除** ★★
            //   用户原话："删除爆炸倍率这个参数删掉，肥料伤害=（基础伤害+爆炸伤害）*伤害倍率"
            //   ── 与 v173 / v194 两次反复的关系（写清楚，免得下一轮又加回来）──
            //     v173：删了**显示**（属性还在）⇒ 玩家报"没有爆炸倍率这个属性" ✓
            //     v194：把**显示**加回来（属性当时确实存在、4 个非 UI 读者）✓
            //     v195：属性**本身**被删除 ⇒ 显示、道具、公式三处一起消失 ✓ 这次不是显示问题
            //   所以"面板上找不到爆炸倍率"在 v195 之后是**正确的**：它不再存在 ✓
            // v108：「攻击倍率」→「伤害倍率」（需求）。它乘的是 (基础伤害 + 攻击伤害) 整块，不只攻击那一项。
            // v162：现在乘的是 (基础伤害 + 对应伤害属性) 整块——三种武器各乘一次，语义不变。
            ly = StatLine(ly, $"伤害倍率　{AddRelative(st.Get(StatId.AtkMult), bs.atkMult)}");
            // v162（A2）：爆炸范围**上移进主要属性区**（需求）。
            //   它直接决定肥料那条主输出线的体量，与三种伤害类型同属"我打得多疼"，
            //   留在次要属性区会让玩家以为它只是边角加成。
            ly = StatLine(ly, $"爆炸范围　{AddRelative(st.Get(StatId.ExplodeRange), bs.explodeRange / 100f)}{CapSuffix(StatId.ExplodeRange)}");
            ly = StatLine(ly, $"攻击速度　{AddRelative(st.Get(StatId.AtkSpeed), bs.atkSpeed)}{CapSuffix(StatId.AtkSpeed)}");
            ly = StatLine(ly, $"攻击范围　{AddRelative(st.Get(StatId.AtkRange), bs.atkRange)}{CapSuffix(StatId.AtkRange)}");
            ly = StatLine(ly, $"移动速度　{AddRelative(st.Get(StatId.MoveSpeed), bs.moveSpeed)}{CapSuffix(StatId.MoveSpeed)}");
            // ★ v185 修：暴击率**必须用 `AddPoints`**（百分点差），不能用 `AddRelative`（相对涨幅）★
            //
            //  病因（用户报"显示 +240%，真实只有 17%"）：
            //    `tuning.player.critRate = 0.05`（0..1 小数），而 `levelReward.stats` 里
            //    `CritRate` 写的是 `mode: Add, amount: 3` —— 语义 = **+3 个百分点**。
            //    引擎侧 `RunSession.PickRewardCard`（`TableDatabase.IsFractionStat(stat)` 那一支）
            //    已经把 `amount / 100` 加进去 ⇒ 4 张卡 = `0.05 + 0.12 = 0.17` = **17%** ✓
            //    而这里按"相对涨幅"算：`0.17 / 0.05 − 1 = 2.4` ⇒ 显示 **+240%** ✗
            //
            //  判据不是"哪个数看着小"，而是**引擎怎么加**：
            //    `IsFractionStat(s) => s == StatId.CritRate`（`TableDatabase.cs:508`，全工程唯一一个）
            //    ⇒ 只有它走 `amount/100` 的**加法**，其余走 `Percent` 的**乘法** ✓
            //  所以：`IsFractionStat` 为真 ⇒ `AddPoints`；其余 `Percent` ⇒ `AddRelative` 保留 ✓
            ly = StatLine(ly, $"暴击率　{AddPoints(st.Get(StatId.CritRate), bs.critRate)}{CapSuffix(StatId.CritRate)}");
            ly = StatLine(ly, $"暴击伤害　{AddRelative(st.Get(StatId.CritDmg), bs.critDmg)}");
            ly = StatLine(ly, $"闪避率　{AddPoints(st.Get(StatId.Dodge), bs.dodge)}{CapSuffix(StatId.Dodge)}");
            // ★ v191（用户需求①）：幸运值 —— **主要属性**，百分比形式，初始 5%★
            //
            // ★ 为什么用 `AddPoints`（百分点差）而不是 `AddRelative`（相对涨幅）★
            //   它与 `暴击率` 是**同一类属性**：单位都是 0..1 小数、卡面都写 `Add amount`（+N 个百分点）
            //   判据也只有一条：`TableDatabase.IsFractionStat(stat)` 为真 ⇒ 走 `AddPoints` ✓
            //   （`Luck` 已进那个判定，见 `TableDatabase.IsFractionStat`）
            //   用 `AddRelative` 的话：5% 抽到一张 +3% ⇒ `0.08 / 0.05 − 1` ⇒ 印成 **+60%** ✗
            //   那是"暴击率 +240%"那个 bug 的同一个形状（见下面那条注释）。
            //
            // 开局 = `AddPoints(0.05, 0.05)` = **+0%** ✓（用户要求"属性界面显示增量 0%"）
            ly = StatLine(ly, $"幸运值　{AddPoints(st.Get(StatId.Luck), bs.luck)}{CapSuffix(StatId.Luck)}");
            ly = StatLine(ly, "— 次要属性 —", true);
            ly = StatLine(ly, $"击退力　{AddFlat(st.Get(StatId.Knockback), bs.knockback)}{CapSuffix(StatId.Knockback)}");
            ly = StatLine(ly, $"经验倍数　{AddRelative(st.Get(StatId.ExpMult), bs.expMult)}");
            ly = StatLine(ly, $"货币倍数　{AddRelative(st.Get(StatId.GoldMult), bs.goldMult)}");
            // v110：爱心掉落率是**等级制**（低/中/高/MAX），不是百分比增量。
            // 显示成"低（10%）"——玩家既要知道现在在哪一级，也要知道这一级值多少。
            {
                int hl = Mathf.RoundToInt(st.Get(StatId.HeartDropRate));
                ly = StatLine(ly, $"爱心掉落率　{TuningDef.HeartDropLevelNameOf(hl)}（{TuningDef.HeartDropRateOf(hl) * 100f:0}%）{CapSuffix(StatId.HeartDropRate)}");
            }
            // v108：爱心恢复量同样用**实际值**（需求）——它是"捡一颗回几点血"，不是加成
            ly = StatLine(ly, $"爱心恢复量　{st.Get(StatId.HeartHeal):0.#}{CapSuffix(StatId.HeartHeal)}");
            // ★ v191（用户需求③）：小猪刷新率 —— **次要属性**，等级制（低/中/高/MAX）★
            //   逐字照抄上面"爱心掉落率"那一行的口径：既要知道现在在哪一级，也要知道这一级值多少 ✓
            //   （用户原话："与爱心掉落率相同的机制与数值" ⇒ 连显示形状也照抄，不另发明一种）
            //   ⚠️ 读的是**等级**（`Get` 返回 0..3 的整数语义），不是概率 ✗
            //     直接去乘 100 会得到"300%"这种东西 ✓
            {
                int pl = Mathf.RoundToInt(st.Get(StatId.PigRate));
                ly = StatLine(ly, $"小猪刷新率　{TuningDef.PigLevelNameOf(pl)}（{TuningDef.PigRateOf(pl) * 100f:0}%）{CapSuffix(StatId.PigRate)}");
            }
            // v108：删除「当前实算」整块（需求）。
            // 它原本是为了在"全屏增量"之后让绝对值仍可见而加的，
            // 但现在伤害/范围等都会在**各自的条目上直接写实算值**（见植物详情与武器行），
            // 这块就成了同一信息的第二个来源——两处数字一旦不同步，玩家不知道该信哪个。
            ly = StatLine(ly, "— 局面 —", true);
            ly = StatLine(ly, $"耕地　{s.World.Farm.PlotCount} 块");
            ly = StatLine(ly, $"耕地上限　{s.World.Farm.Capacity} 块（每升 1 级 +1）{CapSuffix(StatId.MaxPlots)}");
            // v161：计时制 —— 不再显示 "N / 20"，只显示当前是第几分钟 ✗
            ly = StatLine(ly, $"第 {s.WaveIndex} 分钟　游玩 {FmtTime(s.RunTime)}");
            ly = StatLine(ly, $"击杀　{s.Kills}");
            ly = StatLine(ly, $"金币　{s.Gold}");
            // v108：删除「生存植物」一栏（需求）—— 场上敌人数量已经能反映战况，
            // 而"生存植物/上限"这个比值玩家从不据此做决策。
            ly = StatLine(ly, $"场上敌人　{s.World.Enemies.Count}");
            ly = StatLine(ly, $"等级　{s.Progress.Level}");
            ly = StatLine(ly, $"用时　{s.RunTime:0}s");
            _clipDepth--;   // v178：离开裁剪区
            GUI.EndScrollView();
        }

        // ★ v173：原来这里有一个 `SeedCardLines(RunSession, SeedSlot)` —— **整段已删** ✗ ★
        //
        //   它是"商店条目 / 商店详情 / 开局选种"三处的种子卡正文，与主界面那 6 个格子走的
        //   `PlantCellInfoLines` **各写了一份** ⇒ 两份的字段集不一致：
        //   这一份 15 行（含 8 个战斗属性），那一份 7 项 ✗
        //   用户标准（v173）："植物只显示：名字、品质、等级、金币产出量、经验产出量、生长时间、特殊功能" ✓
        //   ★ v196 更新 ★：用户把属性表**再收一次** —— 只剩 6 项
        //     （名称 / 品质 / 等级 / 成熟时间 / 金币提供量 / 经验值提供量）⇒ 第 7 项「特殊功能」也下线了 ✓
        //     见 `PlantCellInfoLines` 上方那段说明 ✓
        //   ⇒ 现在**只有一个生产者**（`PlantCellInfoLines`），五处调用直接指向它 ✓
        //      "改了一处、另一处还在显示旧属性"从此在结构上不可能发生 ✓
        //
        //   连同删除的行（按**代码符号**列，便于回溯）：
        //     伤害算式（`SeedDef.BaseValue` × 品质倍率 + `StatId.MeleeDamage`）/
        //     `SeedDef.AttackInterval` / `SeedDef.BaseCritRate` + `StatId.CritRate` /
        //     `SeedDef.BaseCritDmg` / `SeedDef.Range` × `StatId.AtkRange` / `StatId.Knockback` /
        //     `SeedDef.BaseHp` / `SeedDef.BaseShield` / `SeedDef.MoveSpeed` /
        //     攻击模式（`RunSession.AtkTypeText`）/ 功能（`SeedDef.Desc`）/
        //     卖出返还（`SeedSlot.LevelYieldMult`，与上面"收益"是同一个数）✓
        //   ★ 植物自 v165 起**不参战** ⇒ 上面这些战斗数没有一个影响它在地里的行为 ✓
        //   ⚠️ `AttackType` / `AttackKind` / `Support` **不是**死字段：`RunSession.AtkTypeText` 等
        //      三个文案函数与自检仍在读（商店副标题在用）✓

        /// <summary>
        /// v19：统一的种子卡（商店 / 菜单 / 开局选种共用）。
        /// 结构：图标区 → 名称 → 稀有度 → **独立滑动条的信息区** → 底部按钮区（按钮不压文字）。
        /// </summary>
        /// <summary>
        /// 种子条目（菜单 / 种子选择 / 库存三处共用）。
        ///
        /// v84：改成与商店条目**同一套「左图右文」**——
        ///   左：图标 + 名字 + 品质（固定宽度）
        ///   右：整块剩余宽度给描述详情（自带滑动条）
        /// 顺带把品质色条从**顶部**挪到**左侧**：商店的行是左条，
        /// 两处不统一的话，同一屏里会出现两种"品质标记位置"，玩家得重新学一遍。
        ///
        /// 为什么这次敢动这个被三处复用的函数：新布局只依赖 `card` 这个矩形本身，
        /// 不再假设"卡片一定是纵向窄条"——三处调用点各自给不同的宽高都能正确排布。
        /// 旧版把图标居中、文字居中，宽度一变就散架，这才是它一直没被统一的原因。
        /// </summary>
        private void SeedCardPanel(RunSession s, SeedSlot slot, Rect card, int scrollIndex, float[] scrollStore)
        {
            Color pv0 = GUI.color;
            GUI.color = slot.IsEmpty ? SlotBg : SlotBgFilled;
            PaperFill(card);   // v96：卷轴纸纹（种子卡 / 选种卡 / 库存卡共用）
            ScrollFrame(card); // v105：卷轴的形
            GUI.color = QualityColor(slot.Quality);
            GUI.DrawTexture(new Rect(card.x, card.y, 5f, card.height), White);   // 左侧品质条
            GUI.color = pv0;

            // ── 左：图标 + 名字 + 品质（**纵向堆叠**）──
            // ★ 名字不能和图标并排 ★
            // 第一版是"图标在左、名字在右"，但卡片宽度只有 ~350px 时
            // 左列算出来只剩 40px —— 装不下"豌豆射手"四个字，文字直接压到描述区上。
            // 纵向堆叠对窄卡天然安全：名字拿到整列宽度，与卡片宽度解耦。
            float leftW = Mathf.Clamp(card.width * 0.30f, 96f, 156f);
            float iconS = Mathf.Clamp(leftW - 36f, 36f, 74f);
            // ★ v180（用户第 ④ 条）：内边距收紧 —— 顶 12 → 10、图标后 4 → 3、行距 26/46 → 23/42 ★
            //   ⚠️ **字号一个字节都没动**：`OnDarkCenter`（15）、`WrapSmall`（15）都没碰，
            //      行高 24/20/20 也没碰 ⇒ 只是"这一摞"整体上移 2px、彼此靠近 3px ✓
            //   ⇒ 左列底从 `card.y+140` 收到 `card.y+117` ⇒ 196 高的格子里按钮上方还留 54px ✓
            Rect icon = new Rect(card.x + (leftW - iconS) * 0.5f + 6f, card.y + 10f, iconS, iconS);
            DrawOfferIcon(icon, QualityColor(slot.Quality),
                slot.Def != null ? "icon_plant_" + slot.Def.Id : null);

            Txt(new Rect(card.x + 8f, icon.yMax + 3f, leftW - 12f, 24f), slot.Label, OnDarkCenter);
            Txt(new Rect(card.x + 8f, icon.yMax + 23f, leftW - 12f, 20f),
                s_rarityName(slot.Quality), OnDarkCenter);
            // ★ v196：这里原来还有第三行「{slot.Level} 级」★
            //   它和右侧信息区第 1 行「等级　N 级」讲的是同一件事 ⇒ 同一张卡上印两遍 ✗
            //   6 项收敛之后，等级归**信息区**（那样主界面格子、商店详情、种子卡三处**同一行同一写法**）
            //   ⇒ 左列只留 ① 名称 ② 品质 两项身份信息，等级那行随本次一起删 ✓
            //   左列因此从 `card.y+117` 收到 `card.y+97` ⇒ 更靠上的名字/品质与按钮之间留白更多 ✓

            // ── 右：描述详情（整块剩余宽度）──
            float bottomReserve = ButtonReserve > 0f ? ButtonReserve + 8f : 8f;
            // 上边距 8 → 6、下边距 16 → 12（与左侧那摞字的起始对齐）
            Rect descView = new Rect(card.x + leftW + 6f, card.y + 6f,
                Mathf.Max(60f, card.width - leftW - 14f), Mathf.Max(24f, card.height - bottomReserve - 12f));
            // v48/v51：统一走 DrawInfoList —— 自动换行 + 按实测行数算内容高度（超宽行会折行并计入高度）
            // ★ v198：走 `PlantCardLines` —— 4 行数值 ＋ **底部特殊功能**（用户原话"在底部显示拥有的特殊功能"）★
            //   这一格的视口本来就有滚动条（`DrawInfoList`），第 5 行超长时自动折行 + 可滚 ✓
            if (!slot.IsEmpty)
                DrawInfoList(descView, PlantCardLines(slot), scrollIndex, scrollStore, "种子卡");
            else
                Txt(descView, "空格", _small);
        }

        // ★ v177：这里原来有一个 `OnLight`（"亮底专用"的深棕字）。
        // 它唯一的调用方是 `DrawWeaponBox` —— 那一行原来填亮绿色板，所以要用深字压。
        // 板子去掉之后，武器框里已经没有任何"亮底"⇒ 它成了死代码，随本次改动一起删除 ✗
        // （深色底样式 `OnDark` / `OnDarkBold` / `OnDarkWrap` 仍在用：棕色卡片与植物格都还是暗底 ✓）

        /// <summary>
        /// v102：**暗面专用**的浅色文字样式。
        ///
        /// 把默认文字色改成墨棕之后（为亮底服务），画在**棕色卡片**上的那几处
        /// 变成了深棕压深棕 —— 商品行左列的名字/品质/价格、种子卡的名字几乎看不见。
        /// 这一次的教训和 v74 那次一模一样：**共用样式的默认色一改，两个方向总有一边被打坏**。
        /// 所以不再"挑一个能兼顾的颜色"，而是把两种底各给一个明确的样式：
        ///   亮底（奶油面板）→ 默认墨棕
        ///   暗底（棕色卡片）→ OnDark（暖白）
        /// 调用处**必须显式选边**，不能再依赖"默认值碰巧合适"。
        /// </summary>
        private GUIStyle _onDark;
        private GUIStyle OnDark
        {
            get
            {
                if (_onDark == null)
                {
                    _onDark = new GUIStyle(_small);
                    _onDark.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                }
                return _onDark;
            }
        }

        private GUIStyle _onDarkBold;
        private GUIStyle OnDarkBold
        {
            get
            {
                if (_onDarkBold == null)
                {
                    _onDarkBold = new GUIStyle(_bold);
                    _onDarkBold.normal.textColor = new Color(0.97f, 0.94f, 0.87f);
                }
                return _onDarkBold;
            }
        }

        private GUIStyle _onDarkCenter;
        private GUIStyle OnDarkCenter
        {
            get
            {
                if (_onDarkCenter == null)
                {
                    _onDarkCenter = new GUIStyle(_center);
                    _onDarkCenter.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                }
                return _onDarkCenter;
            }
        }

        /// <summary>
        /// ★ v182：暗底 **标题号**（`_title` 的字号 + 暖白）★
        ///
        /// 抽卡的卡面是 `CardBg`（暗棕，与商店商品卡同一个色）⇒ 卡名必须走暖白，
        /// 否则就是"墨棕压墨棕"，卡名读不出来。
        ///
        /// 为什么这一版以前不存在：卡面长期被一层**永久白膜**盖着（旧落位闪光的判据在 k=1 之后恒为 1），
        /// 白膜把它抬成了浅灰底 ⇒ 墨棕字"碰巧"看得见。v182 把白膜修掉之后，这个遮蔽问题就暴露了 ✓
        /// ⇒ 按 v102 立下的规矩补上这一版（亮底/暗底各一版、调用处显式选边），而不是把默认色改回去 ✗
        /// </summary>
        private GUIStyle _onDarkTitle;
        private GUIStyle OnDarkTitle
        {
            get
            {
                if (_onDarkTitle == null)
                {
                    _onDarkTitle = new GUIStyle(_title);
                    _onDarkTitle.normal.textColor = new Color(0.98f, 0.95f, 0.88f);
                }
                return _onDarkTitle;
            }
        }

        /// <summary>v48：种子卡信息区专用样式——自动换行，避免长句被裁掉右半。</summary>
        private GUIStyle _wrapSmall;
        private GUIStyle WrapSmall
        {
            get
            {
                if (_wrapSmall == null)
                {
                    _wrapSmall = new GUIStyle(_small) { wordWrap = true };
                    _wrapSmall.fontSize = _small.fontSize;
                    _wrapSmall.padding = new RectOffset(0, 0, 0, 0);
                    // v74：正文框底色改成奶油木色后，正文必须是深色。
                    // 只改这一个样式——_small 在多处复用（有深色卡、有奶油面板），
                    // 全局改色必然把其中一半改成看不见。WrapSmall 恰好只用在文本框区域。
                    _wrapSmall.normal.textColor = new Color(0.24f, 0.19f, 0.14f);
                }
                return _wrapSmall;
            }
        }

        /// <summary>
        /// v51：HUD 文本溢出审计开关。由 DebugApi.HudAudit() 置真，下一次 OnGUI 会把每个信息区
        /// 的每行文本实测宽度与可用宽度比对，超宽的记进 AuditFindings 并打日志。
        /// 有了它，"哪里显示不全"就不再靠肉眼找了。
        /// </summary>
        public static bool AuditRequested;
        public static readonly System.Collections.Generic.List<string> AuditFindings =
            new System.Collections.Generic.List<string>();

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v180：商店布局探针（**只读**，默认关闭）★
        //
        //  ── 为什么需要它 ✗（用户第 ⑤ 条的判据）──
        //   用户报"商店界面会抖动/跳"。**"看起来在抖"不是证据** ——
        //     · 可能是布局每帧在变 ✓
        //     · 也可能是内容（价格/文案）每帧在变、而布局没变 ✓
        //     · 还可能是场景在动、UI 静止 ✓
        //   ⇒ 必须把**布局用的每一个数**逐帧记下来，逐帧比对：
        //     同一份数据下这些数**一个字节都不许变**；变了就是抖动的那一环 ✓
        //
        //  ── 为什么记"输入"而不是只记"输出矩形" ✗ ──
        //   记矩形只能证明"变了"；记**算它的那些入参**才能证明"为什么变" ✓
        //   （`Screen.width` / `Offers.Count` / `Bag.SlotCount` / 三个滚动量 /
        //     `_virtualW/H` —— 这几个一旦有任何一个在跳，矩形必然跟着跳 ✓）
        //
        //  ── 为什么最多 60 帧 ✗ ──
        //   它由 `DebugApi.HudLayoutProbe` 打开、**读一次就够**。
        //   不设上限的话，开着不管会一直追加字符串 ⇒ 白吃内存 ✓
        // ══════════════════════════════════════════════════════════════════════════
        public static bool LayoutProbeOn;
        public static readonly System.Collections.Generic.List<string> LayoutProbe =
            new System.Collections.Generic.List<string>();
        private const int LayoutProbeCap = 60;
        private int _layoutProbeFrame;

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v181：**换局（重新开始）⇒ 清掉上一局残留的 UI 临时态** ★
        //
        //  ── 为什么需要它 ✗（用户第 ② 条原话："重新开始出现bug，没有全部初始化"）──
        //   玩法层其实**是**新的：`GameBootstrap.RestartRun` → `StartRun` →
        //   `Session?.Dispose()` + `new RunSession()` + `Start(seed)`（`GameBootstrap.cs:94-98/74-92`）✓
        //   ⇒ `World` / `Bag` / `Goods` / `Dots` / `Progress` **全是新对象** ✓
        //     （实测见交付报告的表：重开 3 次，玩法字段逐项等于新开一局，0 差异 ✓）
        //   而 `ImGuiHud` 这个对象**不重建** —— `GameBootstrap.Awake` 里 `Hud = new ImGuiHud()`
        //   （`GameBootstrap.cs:59`）只跑一次，活到退出 play ✓
        //   ⇒ 它的**实例态**（`PendingBuyOffer` / `_itemSelected` / 几个滚动量）与
        //     **静态态**（`LayoutProbeOn` / `AuditRequested`）会跨局活下来 ✗
        //
        //  ── 症状为什么是"没有全部初始化" ✗ ★（不是"少画了什么"，是**多按了一下**）★
        //   `Pending*` 是**一次性输入的寄存处**：`ImGuiHud.ConsumeInto` 每帧把它们搬进 `FrameInput`
        //   （`ConsumeInto` 最后无条件清零的是 `input` 那一侧，不是 HUD 这一侧 ✗）
        //   ⇒ 上一局点过的「买第 3 格」若在重开那一刻还没被消费掉，
        //     新局第一帧照样会把它当成一次**真的购买**发出去 ✓
        //   ⇒ 同一个坑还有一个更常见的版本：`LayoutProbeOn`（`static`）
        //     实测：打开探针 → 重开 → 探针**仍在记录**（60 帧上限还在被上一局的缓冲占着）✗
        //
        //  ── 为什么判据是"对象身份"而不是"让 `GameBootstrap` 通知我" ✗ ──
        //   `RestartRun` / `StartRun` 都在 `GameBootstrap.cs` 里，而它**不在本次可改范围**
        //   （别的任务正在改它）✓ ⇒ HUD 只能靠**自己看得见的东西**判断换局：
        //   `Draw(Session)` 传进来的那个 `RunSession` 换了身份 ✓
        //   ⚠️ 用 `ReferenceEquals` 而不是"比 `Seed`"：同 seed 重开也是新一局，状态同样要清 ✓
        //
        //  ── 为什么**不**清 `KeyHelpVisible` ✗ ──
        //   那是**玩家的偏好**（Tab 隐藏说明，本文件 v5 就有的开关），不是一局的临时态 ✓
        //   把它一起清掉 = 每重开一次就把玩家的设置改回去 ✗ 那不是"初始化"，是"多管闲事"✓
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>上一帧画的是哪一局（`ReferenceEquals` 判据的唯一来源）。</summary>
        private RunSession _uiScope;
        /// <summary>本局的重启探针是否已经打过一行（只读探针，每局一行，避免刷屏）。</summary>
        private bool _scopeProbed;

        private void SyncUiScope(RunSession s)
        {
            if (ReferenceEquals(s, _uiScope)) return;
            _uiScope = s;
            _scopeProbed = false;

            // ① 一次性输入 —— 不清就会在新局被消费成一次真的操作
            PendingBuyOffer = -1;
            PendingReroll = false;
            PendingInitialChoice = -1;
            PendingNextWave = false;
            PendingRestart = false;     // 这一次"重新开始"已经生效，不能留着让新局再重启一次
            PendingResume = false;
            PendingSell = -1;
            PendingSlotClick = -1;
            PendingRewardPick = -1;
            PendingOfferLock = -1;
            PendingRewardSelect = -1;
            PendingRewardReroll = false;
            PendingSlotMerge = -1;
            // v214：退出战斗 —— 请求位与确认态都不能跨局活着
            //   （否则"点了退出 → 上一局被别的原因结束 → 新局菜单一开就停在确认态"）
            PendingExitToTown = false;
            ExitToTownArmed = false;

            // ② 选择态 / 滚动量 / 悬停 / 发牌动画（上一局的 `_itemSelected` 在新局可能根本不存在）
            _itemSelected = null;
            _itemScroll = Vector2.zero;
            _invScroll = Vector2.zero;
            _seedScroll = Vector2.zero;
            _statScroll = Vector2.zero;
            System.Array.Clear(_offerScrollPerRow, 0, _offerScrollPerRow.Length);
            if (_seedScrollPerCard != null) System.Array.Clear(_seedScrollPerCard, 0, _seedScrollPerCard.Length);
            _offerShown = 0;
            // v182：出现/融合两段动画的计时与"待交出"标记 —— 上一局的牌在新局不该还在飞
            _cardAppearT = 0f;
            _cardAppearSig = -1;
            CancelFuse();
            // v185：召唤轮转与同 id 合并 —— 同理，新局从"召唤重新播"开始
            _summonT = 0f;
            CancelMerge();
            _mergeChecked = false;
            _tipOn = false;

            // ③ 静态开关与探针缓冲（`static` ⇒ 不在这里清就一路活到下一次域重载）
            LayoutProbeOn = false;
            LayoutProbe.Clear();
            AuditRequested = false;
            AuditFindings.Clear();
            _layoutProbeFrame = 0;
        }

        /// <summary>
        /// ★ v181：**只读重启探针**（默认关闭，不影响任何玩法状态）★
        ///
        /// 为什么要加它：用户第 ② 条要求"用**运行期探针**读数，别只读代码"✓
        /// 而"重新开始有没有全部初始化"正好是一道**代码读不出来**的题 ——
        /// 代码能证明 `Start()` 赋了哪些字段，证明不了 `ImGuiHud` 这个**不重建的对象**
        /// 与它的 `static` 还留着什么 ✗
        ///
        /// 通道复用现有的那条（**不新增任何编辑器接口**）：
        ///   `DebugApi.HudLayoutProbe("on")` → `LayoutProbeOn = true`
        ///   → 本方法每局打一行 `SCOPE …` → `DebugApi.HudLayoutProbe("")` 把整段缓冲读回 ✓
        ///   （`DebugApi.cs:1574-1597`，它是现成的、只读的、别人的代码 —— 我不改它 ✓）
        ///
        /// 记的是**字段 + 值**，不是"通过/失败"：判据留给读的人
        /// （"重开后 gold 应等于 25、plots 应等于 0/18"这种话写在交付报告里，而不是写死在这里 ✓）
        /// </summary>
        private void ProbeRunScope(RunSession s)
        {
            if (!LayoutProbeOn) return;
            if (LayoutProbe.Count >= LayoutProbeCap) return;
            // 同一局**只打一行**；但 `DebugApi.HudLayoutProbe("on")` 会**清空缓冲** ⇒
            // 缓冲为空时允许再打一次 ⇒ 同一局也能反复读数（否则验收时每读一次就得重开一局 ✗）✓
            // 本方法跑在 `Draw` 的最前面 ⇒ 它看到的 `Count==0` 一定是"刚被清空"，不是"还没轮到我" ✓
            if (_scopeProbed && LayoutProbe.Count > 0) return;
            _scopeProbed = true;
            int plugins = 0;
            for (int i = 0; i < AllWeapons.Length; i++)
                if (s.Goods != null) plugins += s.Goods.SlotsUsedOn(AllWeapons[i]);
            System.Text.StringBuilder b = new System.Text.StringBuilder("SCOPE ");
            b.Append("sid=").Append(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(s));
            b.Append(" seed=").Append(s.Seed);
            b.Append(" phase=").Append(s.Phase);
            b.Append(" wave=").Append(s.WaveIndex);
            b.Append(" t=").Append(s.RunTime.ToString("0.0"));
            b.Append(" gold=").Append(s.Gold);
            b.Append(" level=").Append(s.Progress != null ? s.Progress.Level : -1);
            b.Append(" kills=").Append(s.Kills);
            if (s.World != null && s.World.Farm != null)
            {
                b.Append(" plots=").Append(s.World.Farm.UsedPlotCount).Append('/').Append(s.World.Farm.MaxPlots);
                b.Append(" enemies=").Append(s.World.Enemies.Count);
                b.Append(" pickups=").Append(s.World.Pickups.Count);
            }
            if (s.Bag != null) b.Append(" bag=").Append(s.Bag.OccupiedCount).Append('/').Append(s.Bag.SlotCount);
            b.Append(" items=").Append(s.ItemStacks != null ? s.ItemStacks.Count : -1);
            b.Append(" plugins=").Append(plugins);
            if (s.Dots != null) b.Append(" dots=").Append(s.Dots.Count).Append(" zones=").Append(s.Dots.PoisonZoneCount);
            b.Append(" popups=").Append(s.Popups.Count);
            b.Append(" popupSeen(H/G/E)=").Append(s.PopupHarvestSeen).Append('/').Append(s.PopupGoldSeen).Append('/').Append(s.PopupExpSeen);
            b.Append(" popupKept(H/G/E)=").Append(s.PopupHarvestKept).Append('/').Append(s.PopupGoldKept).Append('/').Append(s.PopupExpKept);
            b.Append(" | HUD pend(buy,reroll,init,next,sell,slot,lock,pick,rsl,rr,slotMerge)=")
             .Append(PendingBuyOffer).Append(',').Append(PendingReroll ? 1 : 0).Append(',').Append(PendingInitialChoice)
             .Append(',').Append(PendingNextWave ? 1 : 0).Append(',').Append(PendingSell)
             .Append(',').Append(PendingSlotClick)
             .Append(',').Append(PendingOfferLock).Append(',').Append(PendingRewardPick)
             .Append(',').Append(PendingRewardSelect).Append(',').Append(PendingRewardReroll ? 1 : 0)
             .Append(',').Append(PendingSlotMerge);
            b.Append(" itemSel=").Append(string.IsNullOrEmpty(_itemSelected) ? "-" : _itemSelected);
            b.Append(" scroll(item,seed,stat,row0)=").Append(_itemScroll.y.ToString("0.0")).Append(',')
             .Append(_seedScroll.y.ToString("0.0")).Append(',').Append(_statScroll.y.ToString("0.0")).Append(',')
             .Append(_offerScrollPerRow[0].ToString("0.0"));
            b.Append(" static(probe,audit)=").Append(LayoutProbeOn ? 1 : 0).Append(',').Append(AuditRequested ? 1 : 0);
            b.Append(" keyhelp=").Append(KeyHelpVisible ? 1 : 0);
            ProbeLine(b.ToString());
        }

        /// <summary>探针的一行（只在 `LayoutProbeOn` 为真时被调用）。</summary>
        private void ProbeLine(string text)
        {
            if (LayoutProbe.Count >= LayoutProbeCap) return;
            LayoutProbe.Add(text);
        }

        /// <summary>
        /// ★ v180：让"每个格子的滚动量"数组装得下 `need` 个格子（只增不减）★
        ///
        /// 为什么必须做：`DrawInfoList` 的边界检查是
        /// `scrollIndex >= 0 &amp;&amp; scrollIndex &lt; scrollStore.Length` ⇒
        /// 数组短了不会报错、不会崩，只会**每帧静默返回 0** ✓
        /// 而"静默返回 0"在界面上就是"滚一下弹回顶部"——这正是用户说的"乱跳"之一 ✓
        /// ⇒ 数组长度必须**从数据来**（`Bag.SlotCount`），不能写死 ✗
        /// </summary>
        private static void EnsureScrollStore(ref float[] store, int need)
        {
            if (need <= 0) return;
            if (store != null && store.Length >= need) return;
            var next = new float[Mathf.Max(need, store == null ? 0 : store.Length * 2)];
            if (store != null) System.Array.Copy(store, next, store.Length);
            store = next;
        }

        /// <summary>一行文本在给定宽度下要占几行；审计期间记录超宽行。</summary>
        /// <remarks>
        /// v178：`WrapSmall` 的字号保持**虚拟口径**（屏幕整数字号只在 `Txt` 那一笔里临时挂上）⇒
        /// `CalcSize` 量出来仍是虚拟宽度，与 `availWidth` 同空间，**不需要任何换算** ✓
        /// （这也正是"字号只在绘制时换算"这个设计的收益：审计与排布的口径一个字节都没动 ✓）
        /// </remarks>
        private int RowsFor(string text, float availWidth, string where)
        {
            float avail = Mathf.Max(40f, availWidth);
            float need = WrapSmall != null ? WrapSmall.CalcSize(new GUIContent(text ?? "")).x : avail;
            int rows = Mathf.Max(1, Mathf.CeilToInt(need / avail));
            if (AuditRequested && need > avail + 0.5f)
                AuditFindings.Add($"{where}：需要 {need:0}px / 可用 {avail:0}px（折 {rows} 行）「{text}」");
            return rows;
        }

        /// <summary>
        /// v51：所有"信息列表"详情区的统一绘制口——自动换行、按实测行数算内容高度、带独立滚动条。
        /// 旧版每个详情区各写一遍，换行与内容高度算法不一致，于是有的地方被裁、有的滚不到底。
        /// </summary>
        private void DrawInfoList(Rect view, string[] lines, int scrollIndex, float[] scrollStore, string where)
        {
            if (lines == null || lines.Length == 0 || view.height <= 20f) return;
            const float lineH = 22f;
            float avail = Mathf.Max(40f, view.width - 16f);

            var heights = new float[lines.Length];
            float total = 0f;
            for (int k = 0; k < lines.Length; k++)
            {
                heights[k] = RowsFor(lines[k], avail, where + "#" + k) * lineH;
                total += heights[k];
            }
            float contentH = Mathf.Max(view.height, total + 8f);

            float sc = scrollStore != null && scrollIndex >= 0 && scrollIndex < scrollStore.Length
                ? scrollStore[scrollIndex] : 0f;

            // v71：正文框底。改这一处，全界面所有滚动正文区一起生效
            // （种子卡、商店详情、属性栏、菜单都走这里）。
            // 旧版是各调用点自己画一块纯色方块——颜色还不完全一致。
            UiSkin.Ensure();
            if (UiSkin.TextBoxStyle != null && UiSkin.TextBoxStyle.normal.background != null)
                GUI.Box(view, GUIContent.none, UiSkin.TextBoxStyle);

            _clipDepth++;   // v178：同上
            sc = GUI.BeginScrollView(view, new Vector2(0f, sc), new Rect(0, 0, view.width - 14f, contentH)).y;
            if (scrollStore != null && scrollIndex >= 0 && scrollIndex < scrollStore.Length) scrollStore[scrollIndex] = sc;

            float y = 0f;
            for (int k = 0; k < lines.Length; k++)
            {
                Txt(new Rect(2, y, avail, heights[k] - 2f), lines[k], WrapSmall);
                y += heights[k];
            }
            _clipDepth--;   // v178：离开裁剪区
            GUI.EndScrollView();
        }
        private static string StatName(string statId)
        {
            switch (statId)
            {
                case "MaxHp": return "最大生命";
                case "Shield": return "护盾";
                case "MeleeDamage": return "近战伤害";
                case "RangedDamage": return "远程伤害";
                case "BlastDamage": return "爆炸伤害";
                case "AtkMult": return "伤害倍率";
                // ★ v195：「爆炸倍率」这条属性连同它的名字一起删除（用户原话："删除爆炸倍率这个参数删掉"）★
                //   这里保留一行**映射到不存在属性**会变成死代码，所以整条删掉；
                //   名字表只服务当前存在的属性 ✓
                case "ExplodeRange": return "爆炸范围";
                case "AtkSpeed": return "攻击速度";
                case "AtkRange": return "攻击范围";
                case "MoveSpeed": return "移动速度";
                case "CritRate": return "暴击率";
                case "CritDmg": return "暴击伤害";
                case "Knockback": return "击退力";
                case "Dodge": return "闪避率";
                // v191：幸运值回归（用户需求①）。名字取**用户原话**"幸运值"，不缩成"幸运" ——
                //   面板、抽卡卡片、道具详情三处读的都是这张表，名字短一个字的后果是三处口径不一 ✓
                case "Luck": return "幸运值";
                case "ExpMult": return "经验倍数";
                case "GoldMult": return "货币倍数";
                case "HeartDropRate": return "爱心掉落率";
                // v191：新增次要属性（用户需求③）——与"爱心掉落率"同形同值，只是打的是小猪不是爱心
                case "PigRate": return "小猪刷新率";
                case "HeartHeal": return "爱心恢复量";
                // v166：`StatId.MaxPlots` 的语义已从"耕地块数"变成"**可种植株数**"（= 田地列数 6）——
                // 因为新玩法"一株植物独占一列"，而种子袋容量 = 这个值（`RunSession:1892` 每帧强制同步）✓
                // 显示名不改的话，玩家会在发牌界面看到"最大耕地 +1"，而它其实与"几块地"无关 ✗
                case "MaxPlots": return "可种植株数";
                default: return statId;
            }
        }

        /// <summary>
        /// 稀有度名。**必须读 Rarity 表，不要在这里另写一份**。
        ///
        /// v97：这里原本是 { 普通, 稀有, 史诗, 传奇, 神话 }，而 QualityColor 画的是
        /// 白/绿/蓝/紫/金 —— 两张表对同一个编号的解释**不一致**：
        ///   quality 3 → 颜色是紫，名字却是"传奇"（应为"史诗"）
        ///   quality 2 → 颜色是蓝，名字却是"史诗"（应为"稀有"）
        /// 玩家看到的就是"传奇显示成紫色"。
        /// 现在统一走 Rarity.NameOf，与 Defs.cs 的颜色表一一对应。
        /// </summary>
        private static string s_rarityName(int q) => Rarity.NameOf(q);

        /// <summary>
        /// ★★ v198：**持有上限的显示口径 —— 唯一实现** ★★
        ///
        /// 用户原话："道具的 4，5，6 上限修改为无限"。而 `items.json` 的 `maxStack` 是 `int`
        /// ⇒ 表里只能写**哨兵大数**（9999），"无限"是一个**显示层概念** ✓
        ///
        /// 所以：**不能只改 json 不改 UI** ✗ —— 那样玩家读到的是"持有上限 9999"，
        /// 与"无限"是两句话（而且 9999 这个数本身就在暗示"有上限"）。
        ///
        /// ★ 为什么这里不自己写 `>= 9999` ★
        /// 判据在 `ItemDef.IsUnlimitedStack`（数据层），本方法只负责**换成字符**；
        /// 各写一份就会出现"商店卡说 ∞、道具详情说 9999"的分歧 ——
        /// 本工程已经吃过一次"UI 自己另写一份名字表 ⇒ 传奇被画成紫色"的教训 ✓
        ///
        /// ⚠️ 插件（`PluginDef.maxStack`）**不走这里**：插件照旧显示数字上限（≤3，自检钉着）✓
        /// </summary>
        public static string MaxStackText(int maxStack)
            => ItemDef.IsUnlimitedStack(maxStack) ? "∞" : maxStack.ToString();

        /// <summary>
        /// v100：商店商品的**类别 + 品质**（需求"商店商品增加类别描述（植物/道具）"）。
        ///
        /// 旧版这一行对道具写"武器"、对种子写稀有度 —— 可这是**两个不同的维度**，
        /// 混在同一行会让人读成"它的品质叫武器"。现在统一成「类别 · 品质」，两个维度各自可读。
        /// </summary>
        private static string GradeText(ShopOffer o)
        {
            // 旧版这里是 `Tool ? "道具" : "植物"` —— 两个名字都是错的：
            //   ① 第三类早就不是工具升级了（改成插件）
            //   ② 种子不叫"植物"（"植物"是长在地里的那一株，种子袋里的是种子）
            string cat = o.Kind == OfferKind.Seed ? "种子"
                       : o.Kind == OfferKind.Plugin ? "插件"
                       : o.Kind == OfferKind.Item ? "道具"
                       : "商品";
            // ★ v185：种子还要报**合成等级**（1/2/3 级）★
            // 货架现在会卖 2 级 / 3 级种子（`ShopOffer.Level`），而这一行是商品卡上
            // "类别 · 品质"那句话 —— 不把等级写上来，同一种的 1/2/3 级卡就长得一模一样，
            // 玩家只能靠价格猜 ✗（价格差一倍多，猜错的代价是买错货 ✓）
            // 非种子商品没有等级轴 ⇒ 一个字都不动 ✓
            //
            // ⚠️ 写法只能用 **"Lv3"**，不能用 "· 3 级种子" ✗
            //   这一格的矩形是 `Rect(tx, row.y + 23f, **132f**, 20f)`（见 `DrawShop` 的调用）。
            //   第一版写的是 "种子 · 一般 · 3 级种子"：抓图
            //   `Docs/captures/shop-seed-levels-123.png` 里它**被 132px 裁掉尾巴**
            //   （"3 级种子"只露出前半截）⇒ 玩家读到的是残句 ✓
            //   换成 "Lv3" 后整行 ≈ 86px，留出余量 ✓ 而且与种子卡详情里的 `Lv 3/3`、
            //   种子袋卡片上的 `Lv` 是**同一个词** ⇒ 界面上只有一套说法 ✓
            // 1 级也照写 "Lv1"（不给缺省）：三档并排时，"写没写等级"不能被读成另一套信息 ✓
            string lv = o.Kind == OfferKind.Seed
                ? " Lv" + Mathf.Clamp(o.Level, SeedSlot.StartingLevel, SeedSlot.MaxLevel)
                : "";
            return cat + " · " + s_rarityName(o.Quality) + lv;
        }

        /// <summary>
        /// ★ v198：**商店里道具详情的两行 —— 顺序在这里定义一次** ★
        ///
        /// 用户原话："重新定义道具的数值展示栏，放在功能栏后，提前位置"。
        ///
        ///   改前 ✗ ① 功能：&lt;desc&gt; ② 道具类型 ③ 效果：&lt;属性 +N&gt; ④ 稀有度…持有上限 ⑤ 价格
        ///   改后 ✓ ① **数值**（属性 +N ＋ 持有上限） ② 功能：&lt;desc&gt;
        ///
        /// 为什么数值必须排第一 ✗ 不是审美：
        ///   货架上并排 4 件商品时，玩家做的是**比较**；能比较的只有数字。
        ///   改前数字在第 3 行（而且详情视口只有 68px ⇒ 它经常要滚一下才出现）✗
        ///
        /// 为什么**只留两行**（数值 ＋ 功能）✗ 这是照 v196 给种子定的同一把尺子量的 ★
        ///   `DrawShop` 的详情视口 = 68px，`DrawInfoList` 行高 22 ⇒ **3 行放得下、第 4 行开始被裁**。
        ///   而货架行的左列已经写着三样：名字 / "道具 · 一般"（类别·品质）/ "价格 30"。
        ///   ⇒ 被删掉的三行全是**重复**的：
        ///        「道具类型：属性增益道具」—— 56 件道具**逐字相同**，零信息 ✓
        ///        「稀有度：一般」———— 左列"道具 · 一般"里就有 ✓
        ///        「—— 价格 30 金币…」—— 左列"价格 30"里就有（v196 给种子删掉这行用的正是这条理由）✓
        ///   留 2 行 ⇒ 44px ＋ 8 内边距 = 52 ≤ 68 ⇒ **不顶滚动条、不裁半行** ✓
        ///   ⚠️ 改前那 5 行在 68px 里必然裁出半行字形（v198 第一版抓图取证：
        ///      `Library/yowei-shots/v198-a-shop-icons.png` 里"道具类型…"被切掉下半截）✗
        ///
        /// ★ 为什么拆成 `internal` 单独一个函数 ★
        ///   `SelfCheck` 要验的正是"数值栏在功能栏**之前**"这件事，
        ///   它必须读**真的会被画出去的那份字符串**（而不是照着这里再拼一遍）——
        ///   抄一份的那天就是两处开始分家的那天 ✓
        /// </summary>
        internal static string[] ItemDetailLines(ShopOffer o)
        {
            if (o == null || o.Item == null) return new string[0];
            return new[]
            {
                ItemStatLine(o.Item),
                "功能：" + o.Item.Desc,
            };
        }

        /// <summary>
        /// ★ v198：**道具的"数值展示栏" —— 唯一实现** ★
        ///
        /// 用户原话："重新定义道具的数值展示栏，放在功能栏后，提前位置"。
        ///
        /// ── 改前 ✗ ──
        ///   数值被拆在两行里：「效果：<属性> +N」（第 3 行）与「稀有度：… 持有上限 N」（第 4 行）。
        ///   而**功能那行在第 1 行** ⇒ 玩家在货架上比价时，最先读到的是风味文字，
        ///   真正能比较的数字在第三行、还可能被滚动框裁掉 ✓
        ///
        /// ── 改后 ✓ ──
        ///   **一行**说全："数值　属性 +N　·　持有上限 X"，并且排在**功能之前**（第 1 行）✓
        ///   （"持有上限"原来自己占第 4 行的一半，并进来之后那一行才腾得出来 ✓）
        ///
        /// ⚠️ 持有上限的口径只有一个：`MaxStackText`（∞ / 数字），这里不自己判 9999 ✓
        /// </summary>
        internal static string ItemStatLine(ItemDef it)
        {
            if (it == null) return "数值　—";
            // ★★ v216：**值为 0 的条目不写**（用户原话："有些道具存在最大生命值加 0 的描述，
            //   加 0 不必说明" ⇒ "在描述生成处过滤掉值为 0 的条目…因为同一处给所有道具用"）★★
            //
            // ── 病因（就出在这一行原来的实现上）──
            //   改前是 `StatName(it.Stat.ToString())` + `"+" + it.Add`，而
            //   `ItemDef.Stat` / `ItemDef.Add` 是**派生自 `Effects[0]`** 的，且
            //   `Stat` 在 `Effects` 为空时兜底成 `StatId.MaxHp`、`Add` 兜底成 `0f`
            //   ⇒ 7 件**纯机制**道具（`carrot_ring` / `bunny_pet` / `big_can` / `windup_gear` /
            //     `charge_coil` / `aegis_lamp` / `sharp_spray`）的表里根本没有 `effects`，
            //     数值栏就印成 **「数值　最大生命 +0　·　持有上限 ∞」** ✗
            //   （"最大生命"这个属性名是**兜底值**冒充出来的，不是任何人的设计）
            //
            // ── 改法（一处分叉，全表 86 件同样适用）──
            //   ① 先把 `effects` 里 **|Add| ≥ 1e-4** 的条目拼出来；
            //   ② 一条都没有时，退而取 `costs` 里非零的那几条（类别 2 的"代价"也是数值）；
            //   ③ 两边都没有 ⇒ 印「—」（纯机制道具：它的内容在下面「功能：」那一行里）✓
            //   ⇒ 再没有 `+0` 能被拼出来：进入拼接的每一条都**必然非零** ✓
            //
            // ⚠️ 保持的形状（`SelfCheck` 的 v198 断言盯着这两样，别改）：
            //   以「数值　」开头、且含 `MaxStackText(it.MaxStack)` 的持有上限文案 ✓
            string body = JoinBonusParts(it.Effects, +1f);
            if (body == null) body = JoinBonusParts(it.Costs, -1f);
            return $"数值　{body ?? "—"}　·　持有上限 {MaxStackText(it.MaxStack)}";
        }

        /// <summary>
        /// ★ v216：把一组增益/减益拼成「属性 +N　·　属性 +N%」，**跳过数值为 0 的条目**；
        /// 一条非零都没有 ⇒ 返回 null（调用点据此回落/印「—」）✓
        /// `sign` = +1 增益 / −1 减益（表里 `costs` 一律写正数，见 `RunSession.WriteBonus`）。
        /// </summary>
        private static string JoinBonusParts(ItemBonus[] list, float sign)
        {
            if (list == null || list.Length == 0) return null;
            string joined = null;
            for (int i = 0; i < list.Length; i++)
            {
                ItemBonus b = list[i];
                if (b == null) continue;
                if (Mathf.Abs(b.Add) < 0.0001f) continue;      // ★ 就是这一句在"过滤值为 0 的条目"★
                string piece = StatName(b.Stat.ToString()) + " " + BonusValueText(b, sign);
                joined = joined == null ? piece : joined + "　·　" + piece;
            }
            return joined;
        }

        /// <summary>
        /// 一条增益/减益的**数值写法**（带正负号）。四种单位分开写，不混用：
        /// · `Percent`（表里 `mode: Percent`）⇒ `+10%` / `-10%`（乘算，`RunSession.WriteBonus` 走 `1±p/100`）
        /// · `Dodge` ⇒ `+6%`：★★ v216 起 `Dodge` 在表里是**整数百分点**（`6` = 6%），
        ///   写进属性时 `/100`（见 `RunSession.DodgePercentPointScale`）⇒ 面板必须补 `%`，
        ///   否则同一个 `6` 在数值栏里读成"六点闪避"、在属性栏里读成"6%"✗ ★★
        /// · `CritRate` / `Luck` ⇒ `+4%` / `+30%`：★★ v287 ★★
        ///   这两项在表里写的是 **0..1 小数**（`TableDatabase.IsFractionStat`：`lucky_bead` 的
        ///   `Add CritRate 0.04` = +4 个百分点、`tycoon_coin` 的 `Add Luck 0.3` = +30 个百分点），
        ///   因为消费点是 `Rng.Chance(st.Get(StatId.CritRate))`，值域天生是 0..1 ✓
        ///   而这里原来照 `Add` 直印 ⇒ 数值栏出现「暴击率 +0.04」「幸运值 +0.3」这种
        ///   **小数点形式的几率**（同一行下面「功能：」却写着「暴击率 +4 个百分点（5% ⇒ 9%）」，
        ///   同一个数两种写法，玩家读成"加 0.04 点"）✗
        ///   判据只有一条、且与属性栏同源：`TableDatabase.IsFractionStat(stat)` ⇒ ×100 再补 `%`
        ///   （面板 `AddPoints` 用的正是同一个函数，见 `ImGuiHud.cs:1640` / `:1654`）✓
        /// · 其余 ⇒ 裸数 `+3` / `-3`（血量点、护盾点、道数…）
        ///
        /// ⚠️ 只改**显示**：`items.json` 的 `add` 一个字节没动，`RunSession.WriteBonus` 也没动 ✓
        /// </summary>
        private static string BonusValueText(ItemBonus b, float sign)
        {
            float v = sign * b.Add;
            bool percent = b.IsPercent || b.Stat == StatId.Dodge;
            if (!b.IsPercent && TableDatabase.IsFractionStat(b.Stat))
            {
                v *= 100f;      // 0..1 小数 ⇒ 百分点（0.04 ⇒ 4）
                percent = true;
            }
            string mag = v.ToString("0.##");
            return (v >= 0f ? "+" : "") + mag + (percent ? "%" : "");
        }

        /// <summary>v17：商品详情（卡底可滚动区域的全部说明）。</summary>
        private static string[] OfferDetailLines(RunSession s, ShopOffer o)
        {
            var list = new System.Collections.Generic.List<string>();
            if (o.Kind == OfferKind.Seed && o.Seed != null)
            {
                // ★ v185：把**商品等级**带进详情 ★
                // 不传的话 `SeedSlot.Level` 取默认 1 ⇒ 2 级种子卡会写
                // "等级　1 级 · 金币 4" —— 与它自己的价格和高等级完全不搭 ✗
                // 传进来之后详情里那一行就是 `等级　2 级`（与真的升到 2 级后一致 ✓）
                // ★ v196：「收获 ×1.35」那个后缀也随着 6 项收敛删掉了，等级只写「N 级」✓
                // ★ v196：**宽版**（`wide: true`）—— 这里是商店商品行，视口只有 68px 高
                //   4 行排不下（见 `PlantCellInfoLines` 的 `wide` 说明）⇒ 2 行 × 2 项 ✓
                var tmp = new SeedSlot { Def = o.Seed, Quality = o.Quality, Level = o.Level };
                // ★ v254：`withFunc: true` ⇒ 商店行变成「功能 + 等级」两行
                list.AddRange(PlantCellInfoLines(tmp, true, true));
            }
            else if (o.Kind == OfferKind.Item && o.Item != null)
            {
                list.AddRange(ItemDetailLines(o));
            }
            else if (o.Kind == OfferKind.Plugin && o.Plugin != null)
            {
                // ★ v287：装不下就把话说在**第一行**（灰化按钮之外的第二次提示）★
                //   理由：插件详情本来就有 6+ 行、处于滚动状态 ⇒ 提示写在末尾等于没写；
                //   而"为什么这个购买按钮是灰的"必须在玩家点之前就能看到 ✓
                //   文案直接取 `CanTakePlugin` 的 reason（"没有足够插件位置：锄头 插件位 6/6 已满…"）——
                //   与 `TryBuy` 失败后 `SetPrompt` 上屏的那一句**逐字相同**，不另写一份说法 ✓
                if (!s.CanTakePlugin(o.Plugin, out string whyTake)) list.Add("⚠ " + whyTake);
                // 旧版这一段是工具升级专用的（"同时耕作/播种的地块数 = 1 + 武器档位"）✗
                // 第三类改成插件后，再把工具的那句话挂上去就是在描述一个不存在的效果 ✓
                list.Add("功能：" + (string.IsNullOrEmpty(o.Plugin.description) ? "（未填写说明）" : o.Plugin.description));
                list.Add("效果：" + PluginEffectText(o.Plugin));
                list.Add("适用武器：" + PluginFitsText(o.Plugin));
                list.Add("叠加规则：" + StackRuleText(o.Plugin));
                list.Add($"稀有度：{s_rarityName(o.Quality)}");
                list.Add("购买后进入持有物，装到哪把武器由你决定");
            }
            else
            {
                list.Add(o.Subtitle);
            }
            // ★ v196：**种子的详情不再重复写价格** ★
            //   种子的价格已经印在同一行的左列（`DrawShop` 的 `价格 {o.Price}`，离这里不到 100px）⇒
            //   再挂一行"—— 价格 N 金币"就是同一张卡上写两遍 ✓
            //   而且它是**排版上的第 3 行**：视口 68px 只装得下 2 行（44px）+ 8 内边距 ⇒
            //   留着它就会顶出一条滚动条，把「经验」那一行挤到屏幕外 ✗（v196 第一版抓图就是这样）
            // ★ v198：**道具也归进这条规则**（同一条理由，同一个视口）★
            //   道具行的左列同样印着「价格 30」⇒ 再挂一行就是重复；
            //   而道具详情现在是 2 行（数值 ＋ 功能），加这一行就变 3 行 ⇒ 顶滚动条 +
            //   第 3 行被裁掉下半截字形（v198 第一版抓图取证：`v198-a-shop-icons.png`）✗
            //   ⇒ 只有**插件**保留这行：插件详情本身 6 行、视口怎么都装不下，它本来就靠滚动 ✓
            if (o.Kind == OfferKind.Plugin)
                list.Add($"—— 价格 {o.Price} 金币，购买后进入持有物");
            return list.ToArray();
        }

        /// <summary>
        /// 插件效果的**可读描述**（商店详情的「效果：」行 / 菜单插件槽的悬停框 / 战斗 HUD，
        /// 三处读的都是这**一个**出口）。
        ///
        /// ★★ v285 重写：**只声明功能，不声明数值** ★★
        /// ── 用户原话 ★
        ///   "所有插件在菜单和商店的拓展提示内容修改，只声明功能和层数，不声明数值"
        /// ── 改前印了什么 ✗ ★
        ///   这一行原来会拼 `通道 +0.4（40%），二段 0.35`，并给灼烧/减速/毒区补「持续 3 秒」
        ///   —— 每一个都是**具体数值**，正是本轮要删的东西 ✗
        /// ── 改后只剩什么 ✓ ★
        ///   `WeaponSystem.ChannelOf(effect)` 的那一句功能说法（例「剑气（多劈一道）」）——
        ///   它描述"改的是攻击的哪一环"，**不带任何面板数** ✓
        /// ── 唯一的说法来源仍然是 `ChannelOf` ✗ 不在这里再抄一份 ★
        ///   两处写同一件事必然分歧；而 `ChannelOf` 随枚举一起维护（自检钉着"每档都有说法"）✓
        /// ── 那些数去哪了 ✗ **没删，只是不上屏幕** ★
        ///   `plugins.json` 的 `amount` / `amount2` **一个字节没动** —— 它们是**结算**的输入
        ///   （`WeaponSystem.Apply` 与 `RunSession` 的三条攻击路径都读），屏幕只是不再复述 ✓
        ///   `PluginDotTable` 的持续秒数同理（仍是 3 秒），只是不再印在这一行 ✓
        /// ── 「层数」由谁给 ✗ ★
        ///   层数是本轮**保留**的信息，但它不是这一行的职责：
        ///   商店详情第 4 行 `StackRuleText`（"可叠加，上限 N"）、插件槽悬停的
        ///   "可叠层数："、菜单悬停的"（上限 N 层）"各给一次 ✓ 这里再写就是同一句话三遍 ✗
        /// </summary>
        private static string PluginEffectText(PluginAsset p)
        {
            if (p == null) return "—";
            return WeaponSystem.ChannelOf(p.effect);
        }

        /// <summary>
        /// `PluginEffectText` 的**只读公开出口**（给探针/报告引用同一段文案）。
        /// 为什么需要它：`PluginEffectText` 是 private，而"商店/HUD 上那行效果文字"
        /// 正是验收对象之一 ⇒ 探针必须读到**同一份实现**，
        /// 不能在探针里再拼一遍字符串（那就成了第二份真相）✓
        /// ⚠️ v285 起这一行**只剩功能、没有数值**（用户要求），探针拿到的也就是这句 ✓
        /// </summary>
        public static string PluginEffectLineForProbe(PluginAsset p) => PluginEffectText(p);

        /// <summary>
        /// ★ v172：这件插件**改的是攻击的哪一环** —— 直接读 `WeaponSystem.ChannelOf` ✗
        /// 不在这里再写一张对照表：两份说法必然分歧，而分歧的表现是"商店说改射程、武器栏说改范围"✓
        /// </summary>
        private static string PluginChannelText(PluginAsset p)
            => p == null ? "—" : WeaponSystem.ChannelOf(p.effect);

        /// <summary>插件的适用武器。空数组 = 任意武器 ✓ 与 `PluginAsset.Fits` 同一套判据 ✗</summary>
        private static string PluginFitsText(PluginAsset p)
        {
            if (p.allowedWeapons == null || p.allowedWeapons.Length == 0) return "任意武器";
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < p.allowedWeapons.Length; i++) names.Add(WeaponSlotName(p.allowedWeapons[i]));
            return string.Join(" / ", names);
        }

        private static string WeaponSlotName(WeaponSlot w)
        {
            switch (w)
            {
                case WeaponSlot.Hoe: return "锄头";
                case WeaponSlot.Can: return "水壶";
                case WeaponSlot.Fertilizer: return "肥料";
                default: return "未指定";
            }
        }

        private static string StackRuleText(PluginAsset p)
        {
            switch (p.stackRule)
            {
                case StackRule.Unique: return "唯一（只能装一个）";
                case StackRule.Multiplicative: return $"乘算叠加，上限 {p.maxStack}";
                default: return $"可叠加，上限 {p.maxStack}";
            }
        }

        /// <summary>v15：倍率类属性统一显示"增量整数%"（默认 0%）。</summary>
        private static string Pct(float value, float baseline)
        {
            int pct = Mathf.RoundToInt((value - baseline) * 100f);
            return $"{(pct >= 0 ? "+" : "")}{pct}%";
        }

        private float StatLine(float y, string text, bool header = false)
        {
            // v51：属性行也纳入溢出审计（面板宽度随窗口变化，长文案可能被裁）
            RowsFor(text, _statContentW, "属性面板");
            Txt(new Rect(4, y, _statContentW, 22), text, header ? _bold : _small);
            return y + (header ? 26f : 22f);
        }
        public void Draw(RunSession s)
        {
            // ★ v203：本帧的悬停记录先清零（每一趟 OnGUI 都重新算：Layout / Repaint 各算一遍，
            //   结果一致；不清零则"上一趟的 true"会漏到这一趟）★
            _hoverBtn = false;
            _hoverTip = false;
            _btnRects.Clear();
            _btnTexts.Clear();      // ★ v214：文本探针与矩形探针同生共死（同下标）

            if (s == null || s.World == null) return;
            // ★ v181：**换局（重新开始）先清上一局残留的 UI 临时态** ★
            //   判据与理由见 `SyncUiScope`；它必须是 Draw 的**第一条**语句 ——
            //   排在下面任何一条读取 HUD 状态的语句之后，那一帧就已经用着旧值了 ✗
            SyncUiScope(s);
            // v178：字号换算（Fs）在 EnsureStyles 里就要用 ⇒ 必须**先**把本帧缩放记下来
            _k = UiScale;
            EnsureStyles();
            if (!_keyHelpInit)
            {
                KeyHelpVisible = TableDatabase.Tuning.ui == null || TableDatabase.Tuning.ui.keyHelpDefault;
                _keyHelpInit = true;
            }

            float scale = _k;
            _virtualW = Screen.width / scale;
            _virtualH = Screen.height / scale;
            // ★ v181：只读重启探针（每局一行，只在 `LayoutProbeOn` 时写）★
            ProbeRunScope(s);
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            // v17：非战斗界面（商店 / 抽卡 / 菜单 / 结算）不再绘制战斗 HUD，避免半透明面板互相透字
            bool combatHud = s.Phase == PhaseState.Combat || s.Phase == PhaseState.Prepare;
            if (combatHud && !s.Paused)
            {
                DrawVitals(s);
                DrawKeyHelp(s);
                DrawWeaponPanel(s);
                // ★ v177：种子袋面板【按阶段门控】—— 战斗阶段整块不进入绘制 ★
                //
                // 需求原话："删除种子袋 UI（场景不显示）—— 种子袋面板不再在战斗场景里画出来
                //            （不是隐藏某一帧，而是整块不绘制）"
                //
                // ── 判据为什么是 `s.Phase == Prepare` 而不是"加个隐藏开关" ✗ ──
                //   隐藏开关 = 每帧仍然调用、仍然算 6 个格子的矩形、只是不落笔 ⇒ 那正是需求禁止的
                //   "隐藏某一帧" ✓ 这里条件不成立时**连方法都不进**（没有调用、没有测量、没有绘制）✓
                //
                // ── 为什么不是"彻底删掉" ✗（这一条我改过主意，理由有取证）──
                //   取证：`DrawPlantCell` 里**只有 `GUI.Label`，没有任何 `GUI.Button`**
                //   ⇒ 战斗里这块面板**本来就是只读的**，删掉它不会删掉任何可点入口 ✓
                //   但"一个面板**不显示**"和"它**不存在**"是两件事：
                //   前者的代价是零，后者会把"准备阶段看一眼我背了哪 6 个种子"这件事一起删掉 ✗
                //   ⇒ 所以保留它，只把它从**战斗**这个它不该出现的场合移走 ✓
                //
                // ── 玩家真正能改 `Bag.Selected` 的路在哪（实证，不是猜测）──
                //   `input.SelectSeed` **唯一**被赋正值的地方是 `SelfCheck.cs:2403/2405`（单测）——
                //   `ImGuiHud.ConsumeInto` 里根本没有这一行 ⇒ `RunSession:2897/3479` 是死路 ✓
                //   `input.SlotClicked` 来自 `PendingSlotClick`，而它**全工程从未被赋值** ⇒ `:2900` 也是死路 ✓
                //   真正活着的是 `:2909` —— **商店里点某一格的「合成 → N 级」**，
                //   `PendingSlotMerge`（`DrawShop`）→ `input.SlotMergeClicked` → `Bag.MergeSlot` 成功时
                //   顺手把 `Bag.Selected` 指到那一格 ✓
                //   ⇒ 所以"选种"的活入口在**商店**，不在这块面板上；门控它不切断任何一条活路 ✓
                // ★ v179（用户第 ④ 条）：**选种子弹窗打开期间，这块整块不画** ★
                //   改前：`Prepare` 阶段这两块**同时**出现 ⇒ 弹窗盖在植物袋面板上面，
                //     左边露出半截「植物（0/6）空格 / 商店买种子后自动种」（用户截图里就是它）✓
                //   用户的要求（原话）："选种子弹窗打开期间，不画 `DrawPlantPanel`。
                //     **用弹窗自己的可见状态做条件**，别动 ⑩ 定的阶段门控"✓
                //   ⇒ 所以这里写成"阶段门控 **且** 弹窗没开"：两件事各留一条，谁也不替谁 ✓
                //     · `s.Phase == PhaseState.Prepare` = ⑩ 定的阶段门控（**一个字没动** ✓）
                //     · `!SeedChoiceOpen(s)`         = 用户这次新加的**弹窗可见**条件 ✓
                //   判据只写一处：`SeedChoiceOpen` 同时被下面那个 `switch` 用来决定"画不画弹窗" ✓
                //   ⇒ 不可能出现"弹窗没画、而植物袋被它挡住了"这种两头不靠的状态 ✓
                //
                //   ⚠️ 后果（写明）：弹窗的可见条件**就是** `Prepare` ⇒ 本条件当前**恒为假** ✗
                //      ⇒ `DrawPlantPanel` 从此**一帧都不会被画**（方法保留，见方法自身的注释）✓
                //      这正是用户要的效果（两块不再叠），但它等价于"准备阶段不再有植物袋面板"✓
                if (s.Phase == PhaseState.Prepare && !SeedChoiceOpen(s)) DrawPlantPanel(s);
                DrawWave(s);
                DrawPlotPrompt(s);
            }

            // v18：暂停时只画菜单（否则选种/商店面板会从菜单背后透出来）
            // v48：绘制顺序 = 战斗 HUD（含底部弹药条）→ 浮动文字 → 菜单面板
            //      —— 浮动文字要在战斗画面上层，但不能盖住商店/抽卡/菜单。
            if (!s.Paused) DrawAmmoBar(s);
            // ★ v197：**闪避虚化**画在浮动文字**之前** —— 残影是"人身上的效果"，
            //   必须压在场景上、但在「闪避」那两个字**下面**（字要最清楚）✓
            //   它的门控与 `DrawPopups` 同一条（`!s.Paused`）：暂停时两样一起停，
            //   不会出现"菜单背后还在闪"这种自相矛盾 ✓
            if (!s.Paused) DrawEvadeGhost(s);
            // v89：雨滴顶部标记已按需求**删除**。
            // 删除的是**调用**，`DrawRainMarkers` 本身留着——
            // 它是一段完整可用的实现（蓝色箭头 + 同列去重 + 只在视野外提示的判据），
            // 将来若要改成别的提示形式，从它改比从零写快。
            if (!s.Paused) DrawPopups(s);
            // ★ v255：三件新插件的世界标记（伏雷 ◆ / 灼印 X / 护壤环）
            //   与浮动文字同层、同在 `!s.Paused` 门控里 ⇒ 暂停时一起停 ✓
            if (!s.Paused) DrawPluginMarkers(s);
            // ★ v175：商店车的**浮动「E」** ★ —— 与 `DrawPopups` 同层（都是"压在场景上的世界坐标文字"）✓
            //   ⚠️ 必须在 `combatHud && !s.Paused` 里 ✗ 否则商店界面背后会透出一个 E（那里车还在停靠 ✓）
            if (combatHud && !s.Paused) DrawShopTruckKey(s);

            if (!s.Paused)
            {
                // ★ v214：**菜单刚关掉（继续游戏 / Esc）⇒ 退出确认态作废** ★
                //   否则"点了退出 → 改主意 → 继续玩 → 再开菜单"时，按钮仍停在
                //   「确认退出？再点一次」—— 玩家眼里的**第一次**点击就真的把人退回主城了 ✗
                //
                //   ⚠️ 判据必须是**这一沿（true→false）**，不能是"每帧都清"✗
                //     每帧清的话：`GameBootstrap.DoExitToTown`（桥/自动化入口）在**没开菜单**时
                //     也走同一条处理函数 ⇒ 第一次调用把确认态置起、下一帧就被这里清掉
                //     ⇒ 第二次调用又只是"重新进确认态"，永远退不出去（实测踩到）✓
                //   沿判定：菜单真开过才需要清；没开过菜单的路径（自动化直调）不受影响 ✓
                if (_menuWasOpen)
                {
                    _menuWasOpen = false;
                    ExitToTownArmed = false;
                }
                switch (s.Phase)
                {
                    case PhaseState.Prepare: if (SeedChoiceOpen(s)) DrawSeedChoice(s); break;
                    case PhaseState.Shop: DrawShop(s); break;
                    case PhaseState.LevelReward:
                        // ★★ v306 纯插入守卫：训练场里**不画抽卡界面**（用户原话："不要出现抽卡"）★★
                        //
                        //  ── 守卫为 false（= 正常战斗）──
                        //    `TrainingMode.NoGacha` 初值 false，只由 `TrainingMode.Enable()` 置真，
                        //    而 `Enable()` 的唯一调用者是训练场景里的 `TrainingBootstrap.Awake`
                        //    ⇒ `break` 不执行 ⇒ 下一行 `DrawLevelReward(s);` 与下面那个 `break`
                        //      原样执行，**逐字节相同** ✓
                        //  ── 守卫为 true（训练场）──
                        //    `break` 直接出 `switch` ⇒ 本帧什么都不画。
                        //    ⚠️ 这是**第二道**：第一道在 `RunSession.OnLevelUp`（`SkipLevelUpChoice`
                        //    让阶段根本进不了 `LevelReward`）⇒ 正常流程下本行永远不会被走到，
                        //    留着它是为了"万一阶段被调试/探针推进来，画面也不出现抽卡" ✓
                        if (TrainingMode.NoGacha) break;
                        DrawLevelReward(s); break;
                    // v165：`PhaseState.Victory` 已删除 —— 计时制唯一的结束条件是玩家死亡，
                    // 所以结算画面只有一种（见 DrawResult）。
                    case PhaseState.Defeat: DrawResult(s); break;
                }
            }
            else { _menuWasOpen = true; DrawMenu(s); }

            // ★ v180：悬停详情**最后**画 —— 它必须压在商店 / 菜单 / 战斗 HUD 所有东西之上 ★
            //   仍在 1.35 矩阵里（它是"面板 + 文字"，和别的面板同一层，不是世界坐标）✓
            //   文字走 `Txt` ⇒ 字号口径与其他文字完全一致 ✓
            DrawTip();

            // v51：审计收尾——本帧画过的所有信息区里，把超宽行一次性报出来（由 DebugApi.HudAudit 触发）
            if (AuditRequested)
            {
                AuditRequested = false;
                Log.Info(AuditFindings.Count == 0
                    ? "HUD-AUDIT 通过：本帧画出的所有文本都放得下"
                    : $"HUD-AUDIT 发现 {AuditFindings.Count} 条超宽：\n" + string.Join("\n", AuditFindings));
            }

            GUI.matrix = prevMatrix;
        }

        /// <summary>
        /// v47：屏幕像素 → HUD 虚拟坐标系（Draw 整体套了 GUI.matrix 缩放，坐标必须除以 UiScale；
        /// 同时 Unity 的屏幕原点在左下、GUI 在左上，所以 Y 要翻）。
        /// 抽成纯函数是为了让这段换算能被自检直接断言——它一旦写错，浮动文字会跑到屏幕外且不会有任何报错。
        /// </summary>
        public static Vector2 WorldToHud(Vector3 screenPoint, float uiScale, float screenHeight)
        {
            float s = Mathf.Max(0.01f, uiScale);
            return new Vector2(screenPoint.x / s, (screenHeight - screenPoint.y) / s);
        }

        /// <summary>
        /// v47：把逻辑层的浮动文字画成"物体旁边"的提示（成熟 / 缺水 / 掉血 / 回血 / 收获 / 升级）。
        /// </summary>
        /// <summary>
        /// ★★ v255：三件新插件的**世界标记**（用户：「所有特效需要制作」）★★
        ///
        /// ── 为什么统一走 IMGUI 而不是 3D 网格 ──
        ///   三者都是"贴在某个世界坐标上的**图标**"，不是体积特效：
        ///     伏雷 = 土里的一枚 ◆（要能透过草看见）、灼印 = 敌人头顶的 X（要永远朝屏幕）、
        ///     护壤 = 玩家身上的环（要能盖住身体）。
        ///   走 IMGUI 的好处：**永远朝向相机**、不参与深度（不会埋进草里看不见）、
        ///   与弹字共用同一套缩放与投影 ⇒ 三者的大小关系是同一把尺子 ✓
        ///
        /// ── 统一的三件事 ──
        ///   ① 投影：`WorldToHud`（与弹字同一个漏斗，屏幕缩放一致）
        ///   ② 缩放：`UiScale`
        ///   ③ 颜色：**只在下面三处常量里定义一次**（改色不用翻四处）
        /// </summary>
        private void DrawPluginMarkers(RunSession s)
        {
            if (s == null || s.World == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            float k = UiScale;
            float t = Time.unscaledTime;

            // ── ① 伏雷 ◆：地面上的一枚暗红菱形 + 上膛/引信进度 ──
            int mineN = PluginField.MineCount;
            for (int i = 0; i < mineN; i++)
            {
                PluginField.Mine m = PluginField.MineAt(i);
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(m.Pos.x, 0.06f, m.Pos.y));
                if (sp.z <= 0f) continue;
                Vector2 hud = WorldToHud(sp, k, Screen.height);
                // 上膛中：小且虚；上膛后：按"引信剩余"呼吸，越接近自爆越亮
                bool armed = m.ArmLeft <= 0f;
                float life = armed ? Mathf.Clamp01(m.FuseLeft / PluginField.MineFuse) : 0f;
                float pulse = armed ? (0.55f + 0.45f * Mathf.Sin(t * (6f + 8f * (1f - life)))) : 0.35f;
                float px = Mathf.Clamp(30f * k, 14f, 64f);
                Texture2D tex = MarkerTex(ref _mineTex, MineDiamondRows);
                if (tex == null) continue;
                Color prev = GUI.color;
                // 描边（四向，与弹字同一手法：暗底上要能读出来）
                GUI.color = new Color(0.05f, 0.03f, 0.02f, 0.85f * pulse);
                float o = Mathf.Max(1f, px * 0.10f);
                GUI.DrawTexture(new Rect(hud.x - px * 0.5f - o, hud.y - px * 0.5f, px, px), tex);
                GUI.DrawTexture(new Rect(hud.x - px * 0.5f + o, hud.y - px * 0.5f, px, px), tex);
                GUI.DrawTexture(new Rect(hud.x - px * 0.5f, hud.y - px * 0.5f - o, px, px), tex);
                GUI.DrawTexture(new Rect(hud.x - px * 0.5f, hud.y - px * 0.5f + o, px, px), tex);
                // 本体：未上膛偏灰、上膛后转朱红并随引信变亮
                GUI.color = armed
                    ? new Color(1f, 0.34f + 0.26f * (1f - life), 0.16f, pulse)
                    : new Color(0.62f, 0.52f, 0.44f, 0.75f);
                GUI.DrawTexture(new Rect(hud.x - px * 0.5f, hud.y - px * 0.5f, px, px), tex);
                // 引信进度：菱形下面一小段横条（只在已上膛时画）
                if (armed && life > 0f)
                {
                    float bw = px * 1.1f, bh = Mathf.Max(2f, px * 0.10f);
                    GUI.color = new Color(0.10f, 0.06f, 0.04f, 0.85f);
                    GUI.DrawTexture(new Rect(hud.x - bw * 0.5f, hud.y + px * 0.55f, bw, bh), Texture2D.whiteTexture);
                    GUI.color = new Color(1f, 0.42f, 0.14f, 0.95f);
                    GUI.DrawTexture(new Rect(hud.x - bw * 0.5f, hud.y + px * 0.55f, bw * (1f - life), bh),
                                    Texture2D.whiteTexture);
                }
                GUI.color = prev;
            }

            // ── ② 灼印 X：敌人头顶一枚像素叉（朱红 + 暗描边）──
            Texture2D xTex = MarkerTex(ref _markXTex, MarkXRows);
            if (xTex != null)
            {
                for (int i = 0; i < s.World.Enemies.Count; i++)
                {
                    EnemyActor e = s.World.Enemies[i];
                    if (e == null || e.IsDead) continue;
                    if (!PluginField.HasMark(e.Id)) continue;
                    Vector3 sp = cam.WorldToScreenPoint(new Vector3(e.Pos.x, 2.35f, e.Pos.y));
                    if (sp.z <= 0f) continue;
                    Vector2 hud = WorldToHud(sp, k, Screen.height);
                    // 轻微呼吸 + 上下浮动：一眼能看出"这个印是活的、会消"
                    float bob = Mathf.Sin(t * 5f) * 3f * k;
                    float px = Mathf.Clamp(26f * k, 12f, 52f);
                    Color prev = GUI.color;
                    GUI.color = new Color(0.06f, 0.03f, 0.02f, 0.9f);
                    float o = Mathf.Max(1f, px * 0.09f);
                    for (int d = 0; d < 4; d++)
                    {
                        float dx = (d == 0 ? -o : d == 1 ? o : 0f);
                        float dy = (d == 2 ? -o : d == 3 ? o : 0f);
                        GUI.DrawTexture(new Rect(hud.x - px * 0.5f + dx, hud.y - px * 0.5f + bob + dy, px, px), xTex);
                    }
                    GUI.color = new Color(1f, 0.18f, 0.12f, 0.95f);
                    GUI.DrawTexture(new Rect(hud.x - px * 0.5f, hud.y - px * 0.5f + bob, px, px), xTex);
                    GUI.color = prev;

                    // ══════════════════════════════════════════════════════════════
                    // ★ v281：**层数角标** —— 每敌只画一次 X，层数用右上角一个数字表达 ★
                    //
                    // ── 为什么必须是"一个数字"而不是"每层再画一枚 X" ✗ ──
                    //   层数一旦与绘制次数挂钩，成本就是 O(敌人数 × 层数)：
                    //   一次肥料投掷能对同一只敌人盖 1+连环+子爆 次印（最坏 5 次，见
                    //   `PluginField.MarkMaxStacks` 的取数理由）⇒ 20 只敌人 × 5 = 100 次
                    //   `GUI.DrawTexture` **每个 OnGUI 事件**都画一遍（IMGUI 一帧至少两次：
                    //   Layout + Repaint，还有鼠标事件）⇒ 光标记就吃掉几百次绘制 ✗
                    //   现在：X 的绘制次数**与层数无关**（恒 5 次），角标只在 `Stacks >= 2`
                    //   时加 2 次（底片 + 数字）⇒ 最坏 7 次/敌，恒定 ✓
                    //
                    // ── 层数从哪读 ──
                    //   `PluginField.MarkStacks`（1..`MarkMaxStacks`）——**唯一的层数真值**，
                    //   绘制侧不自己数、也不缓存 ⇒ 不可能与逻辑层分家 ✓
                    // `Stacks == 1` 是常态（一枚印只被盖一次）⇒ 不画角标，外观与改前**逐位相同** ✓
                    // ══════════════════════════════════════════════════════════════
                    int stacks = PluginField.MarkStacks(e.Id);
                    if (stacks >= 2)
                    {
                        if (_markBadge == null)
                        {
                            _markBadge = new GUIStyle(GUI.skin.label)
                            {
                                alignment = TextAnchor.MiddleCenter,
                                fontStyle = FontStyle.Bold,
                                fontSize = 14
                            };
                        }
                        float bw = Mathf.Clamp(px * 0.62f, 9f, 26f);         // 底片边长
                        float bx = hud.x + px * 0.46f;
                        float by = hud.y - px * 0.52f + bob;
                        Color keepCol = GUI.color;
                        GUI.color = new Color(0.06f, 0.03f, 0.02f, 0.92f);
                        GUI.DrawTexture(new Rect(bx, by, bw, bw), Texture2D.whiteTexture);
                        GUI.color = keepCol;
                        // 数字用与 X 同一个朱红：一眼看出"这是同一颗印的信息"
                        Color bc = _markBadge.normal.textColor;
                        _markBadge.normal.textColor = new Color(1f, 0.42f, 0.30f, 0.98f);
                        Txt(new Rect(bx, by, bw, bw), stacks.ToString(), _markBadge);
                        _markBadge.normal.textColor = bc;
                    }
                }
            }

            // ── ③ 护壤环：玩家身上一圈青白脉动环（剩余时间越少越淡）──
            if (PluginField.WardActive)
            {
                PlayerActor p = s.World.Player;
                if (p != null)
                {
                    Vector3 sp = cam.WorldToScreenPoint(new Vector3(p.Pos.x, 0.05f, p.Pos.y));
                    if (sp.z > 0f)
                    {
                        Vector2 hud = WorldToHud(sp, k, Screen.height);
                        float a = Mathf.Clamp01(PluginField.WardRemain / 3f);
                        float pulse = 0.6f + 0.4f * Mathf.Sin(t * 7f);
                        float rad = Mathf.Clamp(46f * k, 18f, 120f);
                        int seg = 20;
                        Color prev = GUI.color;
                        GUI.color = new Color(0.55f, 1f, 0.92f, a * pulse * 0.9f);
                        for (int i = 0; i < seg; i++)
                        {
                            float ang = (i / (float)seg) * Mathf.PI * 2f + t * 1.6f;
                            float x = hud.x + Mathf.Cos(ang) * rad - 3f;
                            float y = hud.y + Mathf.Sin(ang) * rad * 0.45f - 3f;   // 压扁 = 贴地的一圈
                            GUI.DrawTexture(new Rect(x, y, 6f, 6f), Texture2D.whiteTexture);
                        }
                        GUI.color = prev;
                    }
                }
            }
        }

        private void DrawPopups(RunSession s)
        {
            if (s == null || s.Popups.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_popup == null)
            {
                _popup = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 18
                };
            }
            float scale = UiScale;
            for (int i = 0; i < s.Popups.Count; i++)
            {
                RunSession.FloatingText p = s.Popups[i];
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(p.Pos.x, 1.3f, p.Pos.y));
                if (sp.z <= 0f) continue;                              // 目标在相机背后
                float k = Mathf.Clamp01(p.Ttl / Mathf.Max(0.01f, p.Total));   // 1 = 刚出现，0 = 即将消失
                float age = 1f - k;                                    // 反过来：0 = 刚出现
                Vector2 hud = WorldToHud(sp, scale, Screen.height);
                // v122：x 改成按**实测文本宽度**算（见下面 ow 处）—— 原来写死 `-120`，
                // 而框宽 320，于是右边界到了 +200：文字实际中心偏右 40px，
                // 长文本还会被硬裁。这就是"部分弹字显示不完全"。
                float y = hud.y - age * 30f - 14f;                     // 随时间上飘

                // v48：字号改读表（默认 26）
                // v178：这里算的是**虚拟**字号 —— 屏幕整数字号由 `Txt` 在绘制那一刻统一换算 ✓
                float baseSize = TableDatabase.Tuning != null && TableDatabase.Tuning.ui != null
                    ? TableDatabase.Tuning.ui.popupFontSize : 26f;
                // ★★ **全部战斗弹字的唯一字号出口** ★★
                //   乘的是 `PluginHitFx.PopupFontScale`（当前 **0.769** ⇒ "普通伤害 = 20px"）——
                //   **唯一的那个基准**。本行是全工程弹字唯一算字号的地方：
                //     · 武器（锄头/水壶/肥料）的伤害与暴击 → `PopupKind.DamageToEnemy/Crit`
                //     · 玩家的红/蓝两行 → `DamageToPlayer / DamageToPlayerShield`
                //     · 植物 → `PlantThirst / PlantMature`（`Harvest` 在漏斗里就被滤掉）
                //     · bomb 拟声词 → `WorldView` 发的 `PopupKind.Bomb`
                //     · 插件特殊伤害（弹射/收束/剑气/回响/伏雷/灼印/吸血/感电/污水/海浪）
                //       → `PluginHitFx.Apply` 往 `p.Scale` 里叠相对倍率，**字号仍走本行**
                //     · 训练场 → `TrainingBootstrap` 复用同一个 `RunSession` 与同一个本方法
                //   ⇒ 没有任何一类弹字能绕过这个基准（世界空间只有 BOSS 血条的 `TextMesh`，
                //      那是有名字的血条、不是弹字，走 `BossBarFontPx` 另一套）
                //   `p.Scale` 里已经含"按种类 / 按插件 / 按数值大小"的逐档倍率，那些是相对关系、
                //   一个都不动；整体缩放只落在这一行 ⇒ 改字号只改这处、绝不逐个改。
                //   ⚠️ 图标尺寸 `iconPx` 与描边宽度都从 `drawSize` 派生 ⇒ 自动跟着缩 ✓
                int size = Mathf.RoundToInt(baseSize * PluginHitFx.PopupFontScale
                                            * (p.Scale <= 0f ? 1f : p.Scale));

                // v69：三段式动画（需求：显示要"爆炸感"、消失要缩小淡出）
                //   ① 爆开 0 → 9%   ：几乎瞬间冲到 1.55 倍
                //   ② 回落 9% → 26% ：1.55 弹回 1.0（有回弹才有"炸"的力度）
                //   ③ 稳定 26% → 68%：保持 1.0 不动，让人看清字
                //   ④ 收回 68% → 100%：缩到 0.45 并淡出到 0
                float pop;
                float alpha;
                if (age < 0.09f)
                {
                    float t = age / 0.09f;
                    pop = Mathf.Lerp(0.15f, 1.55f, t * t);             // 平方曲线 = 起手极快
                    alpha = Mathf.Clamp01(t * 1.6f);
                }
                else if (age < 0.26f)
                {
                    float t = (age - 0.09f) / 0.17f;
                    pop = Mathf.Lerp(1.55f, 1.0f, t);                  // 回落
                    alpha = 1f;
                }
                else if (age < 0.68f)
                {
                    pop = 1.0f;
                    alpha = 1f;
                }
                else
                {
                    float t = (age - 0.68f) / 0.32f;
                    pop = Mathf.Lerp(1.0f, 0.45f, t);                  // 慢慢缩小
                    alpha = 1f - t;                                    // 同步淡出
                }

                // ★★ 插件特殊伤害的弹字专属入场动画（本轮唯一插入点）★★
                //   · 弹跳 / 从大缩小 / 斜切 / 抖动 / 更快上浮 —— 全在 `PluginHitFx.Animate` 里
                //   · 不属于插件伤害的弹字：`Animate` 立即返回，`pop`/`dx`/`dy` 逐位不变 ✓
                //
                // ★★ v279：**这里不再推进命中特效**（原来这里有 `PluginHitFx.Pump(dt)`）★★
                //   病灶：`Pump` 挂在本方法的**循环体内**，而本方法第一行是
                //     `if (s == null || s.Popups.Count == 0) return;`
                //   ⇒ 弹字一消失（爆炸后 1.15s），特效推进就停；而特效的渲染体是
                //     **常驻 `LineRenderer`**（池里的 GameObject 一直在，只是 enabled 开关）
                //   ⇒ 它们停在最后一帧的形状/alpha 上继续画 ⇒ 用户报的
                //     "爆炸后画面定格了，但是时间可动、玩家可攻击" ✓
                //   现在推进在 `PluginHitFxHost.Update`（常驻宿主，`RuntimeInitializeOnLoadMethod`
                //   自建）⇒ 与 IMGUI、与弹字队列、与暂停菜单全都解耦；
                //   本方法只保留**绘制侧**该做的那一件事：算这一条字的入场动画 ✓
                float fxDx = 0f, fxDy = 0f, fxGhost = 0f;
                PluginHitFx.Animate(p.Plugin, age, ref pop, ref fxDx, ref fxDy, out fxGhost);

                int drawSize = Mathf.Max(6, Mathf.RoundToInt(size * pop));
                if (_popup.fontSize != drawSize) _popup.fontSize = drawSize;

                Color prev = _popup.normal.textColor;
                Color c = p.Color;
                c.a = alpha;

                // v122：**按实测文本宽度给框，并以世界点为中心**（修"显示不完全"）。
                //
                // 旧版写死 `x = hud.x - 120`、`ow = 320`：左 -120 右 +200，
                // 文字被居中后落在 hud.x + 40 —— 不是以弹出点为中心，而且长文本被裁。
                // 现在宽度由 `CalcSize` 实测（`_popup.fontSize` 已在上方设好），
                // 所以任何字号、任何长度的弹字都能完整显示。
                // v178：`CalcSize` 与 `drawSize` 都是**虚拟**口径（字号未乘缩放）⇒ 直接喂给虚拟布局 ✓
                Vector2 need = _popup.CalcSize(new GUIContent(p.Text ?? ""));
                // ══════════════════════════════════════════════════════════════════
                // ★ v188：**图标前缀**（小火苗 / 水滴 / 骷髅头）★
                //
                // ── 为什么不是 emoji ──
                //   本 HUD 的字体是 `Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 16)`（本文件 `:492`），
                //   IMGUI 的动态字体**只认单色字形**：`🔥💧💀` 属于 emoji 平面，
                //   雅黑没有这些字形，而且彩色 emoji 字形表（COLR/CBDT）在 IMGUI 里根本不参与 ✓
                //   ⇒ 画出来是**豆腐块 / 空白**，与"系统装没装 Segoe UI Emoji"无关 ✗
                // ── 做法：小贴图（程序化，无资源文件）──
                //   形状是 11×11 的位掩码（见 `PopupIconRows`），启动后**生成一次**并缓存；
                //   绘制走 `GUI.DrawTexture` + `GUI.color` 染色 ⇒ 颜色指数 = `PopupStyle.ColorOf(kind)`
                //   ＝文字的颜色 ⇒ 用户说的"图标颜色跟各自弹字色"是**同一个数**，不是抄一份 ✓
                //   与字体无关、与平台无关、与打包无关；分辨率为点采样 ⇒ 放大也不糊 ✓
                // ══════════════════════════════════════════════════════════════════
                // ★★ v282：**两套图标来源，一张表** ★★
                //   ① 插件特殊伤害（弹射/收束/剑气/回响/伏雷/灼印/吸血）——
                //      由 `ft.Plugin`（`PluginPopupStyle`）决定，问 `PluginHitFx.IconOf` ✓
                //      ★ 用户原话："剑气的弹字取消 / 符号，用一个剑加伤害的形式"、
                //        "以及回响等，全部加入图标" ⇒ 这些**不再靠 Unicode 字符** ✓
                //   ② 既有的按 kind 分派的（火苗/水滴/骷髅 + v282 的硬币）—— `PopupStyle.IconOf` ✓
                //   两者**共用**下面的 `PopupIconTex` 缓存与同一套"描边 + 染色"画法
                //   ⇒ 不可能出现"插件图标没有描边"这种两套画法 ✓
                //   ⚠️ 插件优先：一条剑气弹字的 `Kind` 仍是 `DamageToEnemy`（= 无图标），
                //     如果反过来先问 kind，插件图标就永远画不出来 ✗
                PopupIcon icon = PluginHitFx.IconOf(p.Plugin);
                if (icon == PopupIcon.None) icon = PopupStyle.IconOf(p.Kind);
                float iconPx = icon == PopupIcon.None ? 0f : Mathf.Max(8f, drawSize * 1.05f);
                float iconGap = icon == PopupIcon.None ? 0f : drawSize * 0.20f;
                float contentW = need.x + iconPx + iconGap;
                float ow = Mathf.Max(60f, contentW + 28f);
                float oh = Mathf.Max(drawSize + 14f, need.y + 8f);
                float x = hud.x - ow * 0.5f;
                // 图标+数字这一对整体以世界点为中心：内容左边界 = 整块居中后的左缘，
                // 文字再让出 `iconPx + iconGap` ⇒ 加图标不会让数字偏到一边 ✓
                float contentLeft = x + (ow - contentW) * 0.5f;
                float tCenterX = contentLeft + iconPx + iconGap + need.x * 0.5f;
                Rect tr = new Rect(tCenterX - need.x * 0.5f - 14f + fxDx, y + fxDy, need.x + 28f, oh);

                // 四向描边：草甸背景全是中低明度的花草石，单阴影会糊进去
                //
                // ★★ v253：**描边 alpha 从 `alpha*0.95` 改成 `alpha*alpha`，偏移改成跟字号走** ★★
                //
                // ── 病灶（用户报「弹字在农田上变黑」）──
                //   描边是**四遍**同一份字形，每遍 alpha 几乎等于本体（0.95·alpha vs alpha）。
                //   淡出段/爆开段 alpha<1 时，四遍在字形内部叠成一层近黑底：
                //     覆盖率 D = 1 − (1 − 0.95·alpha)^4
                //   本体随后再以**同样弱的** alpha 压上去 ⇒ 整个字形的有效亮度只有
                //   0.35~0.47（alpha≈0.3 时）。这个亮度正好夹在「农田木框/浅土 ≈0.75」与
                //   「草地 ≈0.25」之间 ⇒ **同一份像素在草地上比背景亮、在农田上比背景暗**，
                //   读起来就是"弹字到农田上变黑了" ✓（与"农田"没有任何专用代码相关，纯对比度）
                //   放大器：偏移写死 2.5 虚拟像素、不随字号走；淡出末段字号只剩 12~14px 时
                //   描边比笔画还宽 ⇒ 等于把字涂黑 ✓
                //
                // ── 修法 ──
                //   ① `alpha*alpha`：描边**先于**本体退场（alpha=1 时逐像素与改前相同）
                //   ② 偏移 = `max(1.2, drawSize*0.10)`：字号越小偏移越小，末段不再"涂黑"
                //   只碰这两处；颜色常量、字号、动画曲线、位置、寿命、绘制顺序全部不动 ✓
                //   ⚠️ 已知的**另一半**问题（未在本轮修）：`DrawPopups` 画在三个全屏黑遮罩
                //      （选种 0.58 / 抽卡 0.62 / 菜单 0.72）**之前** ⇒ 面板打开期间屏上的飘字
                //      会被乘一层黑。那个要改 `Draw()` 的顺序，属另一件事，已记在交付说明里 ✓
                float outlineA = alpha * alpha;
                float outlineOff = Mathf.Max(1.2f, drawSize * 0.10f);
                _popup.normal.textColor = new Color(0.06f, 0.05f, 0.04f, outlineA);
                Txt(new Rect(tr.x - outlineOff, tr.y, tr.width, tr.height), p.Text, _popup);
                Txt(new Rect(tr.x + outlineOff, tr.y, tr.width, tr.height), p.Text, _popup);
                Txt(new Rect(tr.x, tr.y - outlineOff, tr.width, tr.height), p.Text, _popup);
                Txt(new Rect(tr.x, tr.y + outlineOff, tr.width, tr.height), p.Text, _popup);
                _popup.normal.textColor = c;
                Txt(tr, p.Text, _popup);
                // ★ 回响的"延迟一次重影"：同一份字形再画一遍，偏移 4px、α 由 `Animate` 给
                //   （只给回响，`fxGhost` 在其余样式下恒为 0 ⇒ 不多一次绘制）
                if (fxGhost > 0.001f)
                {
                    _popup.normal.textColor = new Color(c.r, c.g, c.b, fxGhost);
                    Txt(new Rect(tr.x - 4f, tr.y - 4f, tr.width, tr.height), p.Text, _popup);
                }
                _popup.normal.textColor = prev;

                // 图标画在**文字之后**：描边字与图标互不覆盖（图标占的是文字左边的空位）
                if (icon != PopupIcon.None)
                {
                    Texture2D tex = PopupIconTex(icon);
                    if (tex != null)
                    {
                        var ir = new Rect(contentLeft, y + (oh - iconPx) * 0.5f, iconPx, iconPx);
                        float o = Mathf.Max(1f, iconPx * 0.10f);
                        Color prevGui = GUI.color;
                        GUI.color = new Color(0.06f, 0.05f, 0.04f, alpha * 0.95f);
                        GUI.DrawTexture(new Rect(ir.x - o, ir.y, ir.width, ir.height), tex);
                        GUI.DrawTexture(new Rect(ir.x + o, ir.y, ir.width, ir.height), tex);
                        GUI.DrawTexture(new Rect(ir.x, ir.y - o, ir.width, ir.height), tex);
                        GUI.DrawTexture(new Rect(ir.x, ir.y + o, ir.width, ir.height), tex);
                        GUI.color = new Color(c.r, c.g, c.b, alpha);   // ★ 图标色 = 弹字色
                        GUI.DrawTexture(ir, tex);
                        GUI.color = prevGui;
                    }
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        //  ★ v255：三件新插件的**世界标记**位图（伏雷 ◆ / 灼印 X）
        //
        //  与 v188 的弹字图标同一套做法：11×11 字符画 → 启动后生成一次 → 之后只读。
        //  `#` = 实心，其余透明。改形状时**用眼睛就能改**，不需要查 0/1 表 ✓
        // ──────────────────────────────────────────────────────────────────────
        /// <summary>灼印的像素「X」（用户点名：「一个 X」）</summary>
        private static readonly string[] MarkXRows =
        {
            "#.........#",
            ".#.......#.",
            "..#.....#..",
            "...#...#...",
            "....#.#....",
            ".....#.....",
            "....#.#....",
            "...#...#...",
            "..#.....#..",
            ".#.......#.",
            "#.........#",
        };

        /// <summary>伏雷的「◆」（菱形：埋在土里的东西，不该像火球）</summary>
        private static readonly string[] MineDiamondRows =
        {
            ".....#.....",
            "....###....",
            "...#####...",
            "..#######..",
            ".#########.",
            "###########",
            ".#########.",
            "..#######..",
            "...#####...",
            "....###....",
            ".....#.....",
        };

        private static Texture2D _markXTex, _mineTex;

        private static Texture2D MarkerTex(ref Texture2D cache, string[] rows)
        {
            if (cache != null) return cache;
            cache = MaskToTexture(rows, "PluginMarker");
            return cache;
        }

        /// <summary>
        /// ★ v255：**唯一的"字符画 → 贴图"生成器**（11×11 位掩码 → RGBA32 点采样）。
        ///
        /// 从 `PopupIconTex` 里抽出来的，不是新写的一份：弹字图标（火苗/水滴/骷髅）
        /// 与新的世界标记（伏雷 ◆ / 灼印 X）**共用同一台生成器** ⇒
        /// 两者的"点采样、不循环、不参与打包"这些性质不可能分家 ✓
        /// 掩码必须等宽；不等宽返回 null（调用方已判空）。
        /// </summary>
        private static Texture2D MaskToTexture(string[] rows, string name)
        {
            if (rows == null || rows.Length == 0) return null;
            int h = rows.Length;
            int w = rows[0].Length;
            for (int i = 0; i < h; i++) if (rows[i].Length != w) return null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Mask_" + name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                // 掩码第 0 行是**图像顶部**，纹理第 0 行是**底部** ⇒ 翻转
                string row = rows[h - 1 - y];
                for (int x = 0; x < w; x++)
                    px[y * w + x] = row[x] == '#' ? new Color32(255, 255, 255, 255)
                                                  : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  ★ v188：弹字图标的位掩码 + 纹理缓存
        //
        //  一张掩码 = 一行字符串（`#` = 实心，其余 = 透明）。写成字符画而不是二进制数组：
        //  改形状时**用眼睛就能改**，不需要反查 0/1 表 ✓
        //  纹理只在第一次用到时生成（三种共三份 11×11），之后全程只读 ⇒ 运行期零分配 ✓
        // ──────────────────────────────────────────────────────────────────────
        /// <summary>小火苗（灼烧）：尖端朝上、底部收口</summary>
        private static readonly string[] IconFlame =
        {
            ".....#.....",
            "....###....",
            "....###....",
            "...####....",
            "...####....",
            "..######...",
            "..#######..",
            ".########..",
            ".#########.",
            "..#######..",
            "...#####...",
        };

        /// <summary>水滴（减速）：尖端朝上、下圆</summary>
        private static readonly string[] IconDrop =
        {
            ".....#.....",
            "....###....",
            "....###....",
            "...#####...",
            "...#####...",
            "..#######..",
            ".#########.",
            ".#########.",
            ".#########.",
            "..#######..",
            "...#####...",
        };

        /// <summary>骷髅头（中毒）：颅骨 + 两个眼窝 + 牙</summary>
        private static readonly string[] IconSkull =
        {
            "..#######..",
            ".#########.",
            "###########",
            "###########",
            "##..###..##",
            "##..###..##",
            "###########",
            ".#########.",
            "..#.#.#.#..",
            "..#######..",
            "....###....",
        };

        /// <summary>已生成的图标纹理（生成一次）。</summary>
        private static readonly System.Collections.Generic.Dictionary<PopupIcon, Texture2D> _popupIconTex =
            new System.Collections.Generic.Dictionary<PopupIcon, Texture2D>();

        // ──────────────────────────────────────────────────────────────────────
        //  ★ v282：插件弹字图标（**取代 `↗ ◎ / ↯ ♥` 这些字面后缀**）
        //
        //  用户口径（逐条）：
        //    · "剑气的弹字取消 / 符号，用一个剑加伤害的形式"          → `IconSword`
        //    · "以及回响等，全部加入图标"                            → 其余六种
        //    · "敌人身上冒出一个金币样式图案加伤害，金色"            → `IconCoin`
        //      （币形按规格画：**圆 + 内圈 + 中间竖线**）
        //
        //  ★ 为什么继续用字符画位掩码、而不是 emoji / 系统图标 ✗
        //    与 v188 那三种（火苗/水滴/骷髅）**同一条纪律**：
        //    HUD 字体是雅黑，emoji 平面没有字形 ⇒ 豆腐块；且彩色的 COLR/CBDT 字形表
        //    IMGUI 根本不读 ⇒ 画贴图是唯一稳的路 ✓
        //  ★ 生成时机与代价 ★
        //    第一次用到时才 `MaskToTexture`（`SetPixels32` + `Apply`），之后进 `_popupIconTex`
        //    字典**只读** ⇒ 八张 11×11 一共 968 字节 + 一份常驻纹理，
        //    运行期**零分配、零 new**（弹字本来就每帧画，这里不多做任何事）✓
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 硬币（富豪的金币额外伤害）：**圆环 + 中间竖线**（用户规格"圆 + 内圈 + 中间竖线"）。
        /// 画成**空心**环而不是实心饼：实心饼在 11px 下就是一团白，看不出是硬币 ✓
        /// </summary>
        private static readonly string[] IconCoin =
        {
            "...#####...",
            ".##.....##.",
            "##.......##",
            "#.........#",
            "#....#....#",
            "#....#....#",
            "#....#....#",
            "#.........#",
            "##.......##",
            ".##.....##.",
            "...#####...",
        };

        /// <summary>箭头（弹射）：横杆 + 朝右的箭头（原来那一个 `↗` 字面后缀）。</summary>
        private static readonly string[] IconArrow =
        {
            "...........",
            "...........",
            ".....##....",
            ".......##..",
            ".........##",
            "###########",
            ".........##",
            ".......##..",
            ".....##....",
            "...........",
            "...........",
        };

        /// <summary>靶心（收束）：外环 + 空隙 + 实心靶心（原来那一个 `◎` 字面后缀）。</summary>
        private static readonly string[] IconTarget =
        {
            "...#####...",
            ".#########.",
            "###.....###",
            "##..###..##",
            "#..#####..#",
            "#..#####..#",
            "#..#####..#",
            "##..###..##",
            "###.....###",
            ".#########.",
            "...#####...",
        };

        /// <summary>
        /// 剑（剑气）：**尖头 + 剑身 + 护手 + 柄 + 剑首**，竖着画。
        /// 为什么竖着而不是斜着：11px 的对角线会碎成一串孤点，斜剑在这里读不出来 ✗
        /// </summary>
        private static readonly string[] IconSword =
        {
            ".....#.....",
            "....###....",
            "....###....",
            "....###....",
            "....###....",
            "....###....",
            "..#######..",
            "....###....",
            "....###....",
            "...#####...",
            "...........",
        };

        /// <summary>回声波纹（回响）：两道同心的开口弧（"余波还在扩散"）。</summary>
        private static readonly string[] IconEcho =
        {
            "...........",
            "....#..#...",
            "....##.##..",
            ".....##.##.",
            ".....##.##.",
            "......##.##",
            ".....##.##.",
            ".....##.##.",
            "....##.##..",
            "....#..#...",
            "...........",
        };

        /// <summary>闪电（伏雷）：折线（原来那一个 `↯` 字面后缀）。</summary>
        private static readonly string[] IconBolt =
        {
            "......##...",
            ".....##....",
            "....##.....",
            "...##......",
            "..######...",
            ".....##....",
            "....##.....",
            "...##......",
            "..##.......",
            ".##........",
            "##.........",
        };

        /// <summary>星火（灼印）：四角星（凹边菱形，"这一下被烙了一下"）。</summary>
        private static readonly string[] IconSpark =
        {
            ".....#.....",
            "....###....",
            "...#.#.#...",
            "..#..#..#..",
            ".#...#...#.",
            "#....#....#",
            ".#...#...#.",
            "..#..#..#..",
            "...#.#.#...",
            "....###....",
            ".....#.....",
        };

        /// <summary>心形（吸血）：两个圆瓣 + 收底（原来那一个 `♥` 字面后缀）。</summary>
        private static readonly string[] IconHeart =
        {
            "...........",
            ".###...###.",
            "#####.#####",
            "###########",
            "###########",
            "###########",
            ".#########.",
            "..#######..",
            "...#####...",
            "....###....",
            ".....#.....",
        };

        // ══════════════════════════════════════════════════════════════════════
        //  ★ v290：本批三张（感电 / 污水 / 海浪）
        //
        //  ★ 为什么继续"字符画 11×11 位掩码"，而不是用 Unicode 字符 ★
        //    与上面 8 张同一条纪律（v188 那三种 + v282 那八种都写在头顶那段注释里）：
        //    emoji 平面没有字形、IMGUI 的动态字体也不读彩色字形表 ⇒ 画出来是豆腐块 ✗
        //    `MaskToTexture` 只把 '#' 当成实心像素、其它字符当空 ⇒ 与字体、平台无关 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 分叉闪电（感电 `hoe_shock`）：**双叉** —— 主折线一道、侧面再分一支电弧。
        /// ★ 为什么与 `IconBolt`（伏雷）不同形 ★ 两张图标会在同一场战斗里同时出现
        ///   （锄头装了感电、肥料装了伏雷）⇒ 同形会让玩家读成"同一件插件打的" ✗
        /// </summary>
        private static readonly string[] IconShockArc =
        {
            "........#..",
            ".......##..",
            "......##...",
            ".#####.....",
            "....##.....",
            ".....##....",
            "...######..",
            ".......##..",
            "......##...",
            ".....##....",
            "....##.....",
        };

        /// <summary>
        /// 污水溅射（臭水沟 `can_sewer`）：**一摊往下淌的脏水** + 上方三滴。
        /// 读法：上半是"泼出来的三滴"，下半是"摊在地上的一滩"（下半用不均匀的边，避免读成实心方块）。
        /// </summary>
        private static readonly string[] IconSewage =
        {
            ".....#.....",
            "..#..#.....",
            ".....#..#..",
            ".###...###.",
            "###..###..#",
            "###########",
            "#.#########",
            "###########",
            "##########.",
            ".########..",
            "..####.##..",
        };

        /// <summary>
        /// 三道水纹（海浪 `can_wave`）：**同心、开口向右的三道弧** ⇒ "一层层推出去"。
        /// 与回响那两张（`IconEcho` 的两道同向弧）不同：这里三道**间距递增**，读成"扩散"而不是"回声"。
        /// </summary>
        private static readonly string[] IconWaveRing =
        {
            "...........",
            "....#......",
            "...#.#.....",
            "..#...#....",
            ".#.....#...",
            ".#......#..",
            ".#.....#...",
            "..#...#....",
            "...#.#.....",
            "....#......",
            "...........",
        };

        /// <summary>取（必要时生成）某个图标的纹理。</summary>
        private static Texture2D PopupIconTex(PopupIcon icon)
        {
            if (_popupIconTex.TryGetValue(icon, out Texture2D cached) && cached != null) return cached;
            string[] rows;
            switch (icon)
            {
                case PopupIcon.Flame: rows = IconFlame; break;
                case PopupIcon.Drop: rows = IconDrop; break;
                case PopupIcon.Skull: rows = IconSkull; break;
                // ★ v282：插件弹字图标 + 富豪的硬币（八张，与上面三种同一台生成器）
                case PopupIcon.Coin: rows = IconCoin; break;
                case PopupIcon.Arrow: rows = IconArrow; break;
                case PopupIcon.Target: rows = IconTarget; break;
                case PopupIcon.Sword: rows = IconSword; break;
                case PopupIcon.Echo: rows = IconEcho; break;
                case PopupIcon.Bolt: rows = IconBolt; break;
                case PopupIcon.Spark: rows = IconSpark; break;
                case PopupIcon.Heart: rows = IconHeart; break;
                // ★ v290：本批三张（与上面同一台生成器、同一个缓存字典）
                case PopupIcon.ShockArc: rows = IconShockArc; break;
                case PopupIcon.Sewage: rows = IconSewage; break;
                case PopupIcon.WaveRing: rows = IconWaveRing; break;
                default: return null;
            }
            // ★ v255：生成逻辑抽到 `MaskToTexture`（与伏雷/灼印的标记共用一台生成器）
            Texture2D tex = MaskToTexture(rows, "PopupIcon_" + icon);
            if (tex == null) return null;
            _popupIconTex[icon] = tex;
            return tex;
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★ v197：**闪避虚化**（用户需求①："闪避成功为玩家加上虚化特效"）★
        //
        // ── 为什么虚化画在 HUD（IMGUI）里，而不是去改玩家模型的材质 ✗ ──
        //   ① 玩家模型的表现层是 `Bootstrap/WorldView.SyncPlayer` + `ModelView`，
        //      它们**不在本轮可改范围**（另一路正在改 `WorldView` 的特效）✗
        //   ② 即使用不了那条路，**也不该**走"逐实例改 renderer.material.color.a"那种做法：
        //      同一个材质还被"受击闪白"（`SetTint`）与描边用着 ⇒ 两处抢同一个 alpha，
        //      谁后写谁生效，而且改完忘了还原就会**永久半透明** ✗（本项目吃过这类亏）
        //   ③ 虚化要的是"这个人这一刻在闪、在透、在散"，它天然是**屏幕层**的表达 ✓
        //   ⇒ 做法：`GUI.color` 染色 + 一张程序化生成的软边人形贴图，
        //     每帧只读 `PlayerActor.EvadeGhost`（逻辑层报出来的**剩余时间**），
        //     不写任何模型状态 ⇒ 用完即弃、不可能留下"半透明玩家" ✓
        //
        // ── 读数（自检与交付里引用的就是这三条）──
        //   · 颜色   `(0.86, 0.88, 0.94)` 冷调浅灰 —— 与灰色「闪避」弹字同族 ✓
        //   · 时长   `PlayerActor.EvadeGhostSeconds` = 0.40s（逻辑层给死，这里只读）✓
        //   · 形状   3 张残影，越往外越大越淡（t = 0 / 0.5 / 1）
        //             → 内层 α = 0.46 × fade，外层再乘 0.45 ⇒ 读起来是"人散开"而不是"贴了个方框"✓
        //   ⚠️ 它是**叠加**在模型上的一层半透明残影，**不是**"模型本身变透明"（如实记，见交付）✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>虚化贴图的尺寸（宽 × 高）。比例 0.67 ≈ 人形的身高比 ⇒ 直接按屏幕身高定尺寸即可。</summary>
        private const int EvadeGhostW = 40;
        private const int EvadeGhostH = 60;

        private static Texture2D _evadeGhostTex;

        /// <summary>
        /// 虚化用的**软边人形**贴图（40×60，程序化生成一次并缓存）。
        ///
        /// 为什么是"胶囊"而不是"方框"：IMGUI 只能画矩形，一个纯色矩形盖在人身上
        /// 读起来是"UI 出 bug 了"✗ —— 而"竖直胶囊 + 边缘羽化"在任何相机角度下都读成人形 ✓
        /// 白色 RGB + 只让 A 通道起雾 ⇒ 染色完全交给 `GUI.color`（一处定义颜色）✓
        /// </summary>
        private static Texture2D EvadeGhostTex
        {
            get
            {
                if (_evadeGhostTex != null) return _evadeGhostTex;
                var tex = new Texture2D(EvadeGhostW, EvadeGhostH, TextureFormat.RGBA32, false)
                {
                    name = "EvadeGhost",
                    // 双线性：这一张要的是"雾"，不是图标那种"方块"（对比 `PopupIconTex` 的 Point）
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
                var px = new Color32[EvadeGhostW * EvadeGhostH];
                float cx = EvadeGhostW * 0.5f;
                float yLo = EvadeGhostH * 0.16f;        // 胶囊柱体下沿（脚）
                float yHi = EvadeGhostH * 0.82f;        // 上沿（再往上就是头顶那个半圆）
                float rad = EvadeGhostW * 0.30f;        // 半径
                for (int y = 0; y < EvadeGhostH; y++)
                {
                    // 到"竖直轴线"上最近点的距离：柱体内为 0，两端各自向外量
                    float ax = y < yLo ? yLo - y : (y > yHi ? y - yHi : 0f);
                    for (int x = 0; x < EvadeGhostW; x++)
                    {
                        float dx = (x + 0.5f) - cx;
                        float d = Mathf.Sqrt(dx * dx + ax * ax) / rad;
                        float a = 1f - Mathf.SmoothStep(0.55f, 1f, d);          // 内实外雾
                        a *= Mathf.SmoothStep(0f, 0.09f, y / (float)EvadeGhostH);   // 脚底不硬切
                        px[y * EvadeGhostW + x] =
                            new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(false, false);
                _evadeGhostTex = tex;
                return tex;
            }
        }

        /// <summary>
        /// 第 <paramref name="layer"/> 张残影（0 = 最实的内层、2 = 最外的雾层）在剩余比例
        /// <paramref name="k"/>（1 = 刚闪避、0 = 结束）下的 alpha。
        ///
        /// ★ 为什么抽成 public static 纯函数 ★ 画面上"半透明"这件事在无头环境里看不见，
        ///   而它是用户需求的一半（"虚化特效 = 半透明/淡出"）。抽出来之后
        ///   探针/自检可以直接断言这条曲线（`ProbeV197` 打的就是它）⇒ "淡出"不是靠肉眼签收 ✓
        /// 曲线：`0.46 × k²`（后段掉得快，读起来是"散掉了"而不是"慢慢变灰"），
        /// 外层再乘 `(1 − 0.55 × t)`（越外越淡）✓
        /// </summary>
        public static float EvadeGhostAlpha(int layer, float k)
        {
            float fade = Mathf.Clamp01(k) * Mathf.Clamp01(k);
            float t = Mathf.Clamp(layer, 0, 2) / 2f;
            return 0.46f * fade * (1f - 0.55f * t);
        }

        /// <summary>第 <paramref name="layer"/> 张残影的放大倍率（越外越大；整条随时间继续向外扩）。</summary>
        public static float EvadeGhostGrow(int layer, float k)
        {
            float t = Mathf.Clamp(layer, 0, 2) / 2f;
            return 1f + 0.32f * t + 0.22f * (1f - Mathf.Clamp01(k));
        }

        /// <summary>
        /// 画出玩家的虚化残影（只读 `PlayerActor.EvadeGhost`，不写任何状态）。
        /// 每帧重算屏幕位置 ⇒ 边跑边闪避时残影**跟着人走**（这就是"虚化"而不是"原地留个印子"）✓
        /// </summary>
        private void DrawEvadeGhost(RunSession s)
        {
            if (s == null || s.World == null) return;
            PlayerActor p = s.World.Player;
            if (p == null || p.EvadeGhost <= 0f) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            Texture2D tex = EvadeGhostTex;
            if (tex == null) return;

            // k：1 = 刚闪避（最实），0 = 虚化结束（全透明）
            float k = Mathf.Clamp01(p.EvadeGhost / PlayerActor.EvadeGhostSeconds);
            // 淡出曲线不在这里算：它是 `EvadeGhostAlpha`（可被探针断言的那条），见上一处 ✓
            float scale = UiScale;

            // 三个世界点各投影一次：脚下 / 头顶 / 右手侧
            // 为什么要三个：近顶视相机下"头到脚"在屏幕上可能只有十几像素，
            //   只按身高定尺寸会让残影缩成一个点 ✗ ⇒ 再用"身体宽度"兜一个下限 ✓
            Vector3 wFoot = cam.WorldToScreenPoint(new Vector3(p.Pos.x, 0.05f, p.Pos.y));
            Vector3 wHead = cam.WorldToScreenPoint(new Vector3(p.Pos.x, 2.55f, p.Pos.y));
            Vector3 wSide = cam.WorldToScreenPoint(new Vector3(p.Pos.x + p.Radius * 2f, 0.90f, p.Pos.y));
            if (wFoot.z <= 0f || wHead.z <= 0f || wSide.z <= 0f) return;   // 在相机背后
            Vector2 hFoot = WorldToHud(wFoot, scale, Screen.height);
            Vector2 hHead = WorldToHud(wHead, scale, Screen.height);
            Vector2 hSide = WorldToHud(wSide, scale, Screen.height);
            float bodyH = Mathf.Abs(hFoot.y - hHead.y);
            float bodyW = Mathf.Abs(hSide.x - hFoot.x);
            float h = Mathf.Clamp(Mathf.Max(bodyH, bodyW * 1.9f), 24f, 360f);
            float w = h * (EvadeGhostW / (float)EvadeGhostH);
            float cx = hFoot.x;
            float cy = hFoot.y - bodyH * 0.5f;      // 以身体中点为锚（残影从身上长出来）

            Color prev = GUI.color;
            // 由外向内画：远的那几张先画，最实的一张最后盖上去
            for (int i = 2; i >= 0; i--)
            {
                float t = i / 2f;                                   // 0 / 0.5 / 1
                float a = EvadeGhostAlpha(i, k);                     // ★ 曲线在上一处，只定义一次
                if (a <= 0.004f) continue;
                float grow = EvadeGhostGrow(i, k);
                float gw = w * grow;
                float gh = h * grow;
                float rise = t * 9f;                                // 外层再往上飘一点
                GUI.color = new Color(0.86f, 0.88f, 0.94f, a);
                GUI.DrawTexture(new Rect(cx - gw * 0.5f, cy - gh * 0.5f - rise, gw, gh), tex);
            }
            GUI.color = prev;
        }

        /// <summary>
        /// ★ v175：商店车的**浮动互动提示**（车顶上方的「E」）★
        ///
        /// ── 用户原话 ──
        ///   "靠近商店车时，用【浮动文字「E」】展示互动按键，按 E 打开商店"
        ///   "现在商店车无效，没有互动键"
        ///
        /// ── 为什么"没有互动键"其实不是"没接线" ✗ ──
        /// E **早就接好了**（`GameBootstrap:199` → `input.ShopOpen` → `TickShopTruck` ✓ 我 grep 过）✗
        /// 而玩家感受不到的原因有两条，**都不是"接线"**：
        ///   ① 唯一的提示是 `SetPrompt("商店车到了…")` ✗ 它是**一次性播报**（1.2 秒就消失 ✓）
        ///      ⇒ 玩家走到车下时**屏幕上什么都没有** ✓
        ///   ② 旧时序绕场 35 秒、停靠 10 秒 ✗ 且车停在**场地顶边**（离玩家活动区很远）✓
        ///      ⇒ "车在等你"的那 10 秒里玩家**多半不在车旁** ⇒ 观察到的就是"按了没反应" ✓
        /// 本方法治的是 ①✗（② 在 `ShopTruck` 的时序与停靠位里治 ✓）
        ///
        /// ── ★ 判据只有一条：`s.TruckCanShop` ★ ──
        /// 它内部调引擎自己的 `Truck.CanShop(dist)` ✗ **不在这里复写距离算式** ✗
        /// ⇒ 这个提示**不可能**与"按下去真的会不会开店"不一致 ✓
        ///   （在 HUD 里再写一遍 `dist <= 6` 就是第二份真相：
        ///    将来改 `InteractRange` 时提示会开始撒谎 ✗ —— 本会话已经踩过同类坑 ✓）
        /// 而 `CanShop` 只在 `Phase.Dock` 为真 ⇒ 车**绕场 / 离场 / 不在场时本方法整段不画** ✓
        /// 用户要求的"绕场/离场时不要显示"是**自动成立**的，不需要额外的阶段判断 ✓
        ///
        /// ── 为什么画在**车顶上方**、而不是屏幕中央 ✗ ──
        /// "浮动文字"的语义是"贴着那个东西"✓ 玩家要能一眼把 E 与**哪辆车**对上 ✓
        /// 中央提示做不到这件事（场上还有敌人、地块、掉落 ✓）
        /// ⇒ 与 `DrawPopups`（掉血/成熟那些）用**同一套** `Camera.WorldToScreenPoint`
        ///   + `WorldToHud` 管线 ✓ 不新开渲染路径 ✓
        ///
        /// ⚠️ 车的世界位置走 `s.TruckWorldPos` ✗ —— 与 `WorldView.SyncShopTruck`（摆模型）
        ///    同一个入口 ✓ 否则会出现"看得见的车在 A、写着 E 的车在 B"✗
        /// </summary>
        private void DrawShopTruckKey(RunSession s)
        {
            if (s == null || s.World == null || s.World.Player == null) return;
            if (!s.TruckCanShop) return;                       // 车不在停靠期 / 玩家不够近 ⇒ 不画

            Camera cam = Camera.main;
            if (cam == null) return;

            float scale = UiScale;
            Vector3 wp = s.TruckWorldPos;
            // 车心 + 3.5m（车体约 2m 高，再留 1.5m 空隙）⇒ 提示浮在车**顶**上方 ✓
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(wp.x, wp.y + 3.5f, wp.z));
            if (sp.z <= 0f) return;                            // 车在相机背后 ✗（第三/第一人称转头时会发生）

            Vector2 hud = WorldToHud(sp, scale, Screen.height);
            const float badge = 46f;                           // 键帽边长（虚拟像素）
            float cx = hud.x;
            float cy = hud.y;

            // ★ 呼吸感：键帽轻微缩放，避免它在静止画面上"像贴图"✓
            float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 6.5f);
            float b = badge * pulse;

            Color prev = GUI.color;
            Rect box = new Rect(cx - b * 0.5f, cy - b * 0.5f, b, b);

            // 外发光（夜里也要看得见 —— 与 `DrawRainMarkers` 的光晕同路子 ✓）
            GUI.color = new Color(1f, 0.86f, 0.35f, 0.22f);
            GUI.DrawTexture(new Rect(box.x - 9f, box.y - 9f, box.width + 18f, box.height + 18f), White);
            // 深色描边 + 亮底键帽
            GUI.color = new Color(0.08f, 0.07f, 0.05f, 0.92f);
            GUI.DrawTexture(new Rect(box.x - 3f, box.y - 3f, box.width + 6f, box.height + 6f), White);
            GUI.color = new Color(0.98f, 0.93f, 0.72f, 0.97f);
            GUI.DrawTexture(box, White);

            Txt(new Rect(box.x, box.y + 1f, box.width, box.height), "E", ShopKeyStyle);

            // 键帽下方一个**下指小三角** ⇒ "按这个键" + "指的是下面那辆车" ✓
            // （用逐行递减宽度的横条堆出来 —— IMGUI 只能画矩形，与雨滴箭头同一手法 ✓）
            GUI.color = new Color(0.98f, 0.93f, 0.72f, 0.95f);
            for (int r = 0; r < 8; r++)
            {
                float w = Mathf.Lerp(16f, 2f, r / 7f);
                GUI.DrawTexture(new Rect(cx - w * 0.5f, box.yMax + 2f + r * 2.2f, w, 2.2f), White);
            }

            // 一句话：不写这个的话，"E"在中文玩家眼里仍可能只是个字母 ✓
            //
            // ⚠️ 必须先把 `GUI.color` 还原成白 ✗
            //   `GUI.Label` 的最终色 = `GUI.color` × `style.normal.textColor` ✓
            //   而上面那段为了画键帽把 `GUI.color` 染成了暖黄 ✗ 若直接画这句，
            //   白字会被再乘一次黄 ⇒ 描边与字一起偏色 ✓（`LabelOutlined` 也依赖自己的文字色 ✓）
            GUI.color = Color.white;
            LabelOutlined(new Rect(cx - 90f, box.yMax + 22f, 180f, 24f), "进商店", ShopHintStyle);
            GUI.color = prev;
        }

        /// <summary>键帽上的「E」——黑字粗体居中（底色是亮黄，所以不能走 Outlined 那套白字 ✓）。</summary>
        private GUIStyle _shopKey;
        private GUIStyle ShopKeyStyle
        {
            get
            {
                if (_shopKey == null)
                {
                    _shopKey = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        fontSize = 30
                    };
                    _shopKey.normal.textColor = new Color(0.08f, 0.07f, 0.05f);
                }
                return _shopKey;
            }
        }

        /// <summary>键帽下面那句"进商店"——白字描边（压在草地上，与 `DrawPopups` 同理由 ✓）。</summary>
        private GUIStyle _shopHint;
        private GUIStyle ShopHintStyle
        {
            get
            {
                if (_shopHint == null)
                {
                    _shopHint = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        fontSize = 18
                    };
                    _shopHint.normal.textColor = new Color(1f, 0.96f, 0.82f, 0.98f);
                }
                return _shopHint;
            }
        }

        /// <summary>
        /// v66：带描边的文字。用于**直接压在场景上**的文字（波次提示、中央提示、浮动文字）——
        /// 草甸背景里全是花草石，没有描边的文字会糊进去。
        /// 面板内的文字不需要它（面板本身有底）。
        /// </summary>
        private void LabelOutlined(Rect r, string text, GUIStyle style, float outlineAlpha = 0.85f)
        {
            Color prev = style.normal.textColor;
            Color c = prev;
            style.normal.textColor = new Color(0.06f, 0.05f, 0.04f, c.a * outlineAlpha);
            Txt(new Rect(r.x - 1.5f, r.y, r.width, r.height), text, style);
            Txt(new Rect(r.x + 1.5f, r.y, r.width, r.height), text, style);
            Txt(new Rect(r.x, r.y - 1.5f, r.width, r.height), text, style);
            Txt(new Rect(r.x, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = c;
            Txt(r, text, style);
            style.normal.textColor = prev;
        }

        /// <summary>v25：底部 UI 条——水壶储水量与肥料数量。</summary>
        /// <summary>
        /// v177：**去掉底板**（需求"充能不使用背景"）。
        /// 改前：`Panel(520×78)` 一块奶油底板 + 两根带深色底槽的条 + 深棕色说明文字。
        /// 改后：两根 `BarBare`（投影 + 描边 + 填充）+ 描边文字。
        /// 说明文字仍然**一字不改、宽度不改** ⇒ `RowsFor` 的 HUD-AUDIT 结论必然与改前一致
        /// （见下方那行 `RowsFor`，它用的是 `w - 20f`，与布局常量无关）。
        /// </summary>
        private void DrawAmmoBar(RunSession s)
        {
            // ★ v180：`78` / `12` 换成 `AmmoBarH` / `AmmoBarBottomGap` —— 与武器区**共用同一对常量** ★
            //   理由：武器区要"贴着它上方、不重叠"，靠的就是这两个数。
            //   两边各写一份的话，谁改了其中一处，重叠就回来了，而且**不会有任何报错** ✗
            //   ⇒ 一个数只写一次 ✓（数值与改前**完全相同**：520×78，底边距 12 ✓）
            float w = 520f, h = AmmoBarH;
            Rect r = new Rect(_virtualW * 0.5f - w * 0.5f, _virtualH - h - AmmoBarBottomGap, w, h);
            float halfW = (w - 30f) * 0.5f;
            // ★★ v198：水壶进度条的**分母**改成运行时上限 `s.WaterAmmoCap` ★★
            //   旧写法用常量 `RunSession.WaterAmmoMax`（= 100）⇒ 买了丰水壶之后
            //   真实上限是 `100 + 20 × 层数`，于是 `WaterAmmo` 会**超过分母** ⇒
            //   进度条满格并溢出显示（上一轮已经把它写进交付报告，作为已知显示债）✗
            //   现在分母 = 分子所用的同一个上限 ⇒ `WaterAmmo / Cap ≤ 1` 恒成立 ✓
            //   ⚠️ 只改**分母与文案**：`WaterAmmoMax` 这个 const 仍然是别处
            //      （`DebugApi` / 探针）按契约读的"基础值"，一个字节都没动 ✓
            int waterCap = Mathf.Max(1, s.WaterAmmoCap);
            BarBare(new Rect(r.x + 10f, r.y + 6f, halfW, 22f),
                s.WaterAmmo / (float)waterCap, new Color(0.3f, 0.62f, 0.95f, 0.95f),
                $"水壶储水 {s.WaterAmmo}/{waterCap}");
            BarBare(new Rect(r.x + 20f + halfW, r.y + 6f, halfW, 22f),
                s.FertAmmo / (float)RunSession.FertAmmoMax, new Color(0.85f, 0.62f, 0.25f, 0.95f),
                $"肥料 {s.FertAmmo}/{RunSession.FertAmmoMax}");
            // v51：这行原本是 50+ 字的单行，固定 500px 宽必然被裁；现在按实压缩到一行内
            //      （雨滴 / 小猪的提示在左侧按键说明面板里另有完整说明）
            string hint = "水壶：激光 + 补水　｜　肥料：投掷 + 催熟　｜　小猪 → 肥料";
            RowsFor(hint, w - 20f, "底部提示");
            HudLabel(new Rect(r.x + 10f, r.y + 30f, w - 20f, 40f), hint, HudBody);
        }
        private void EnsureStyles()
        {
            Style(ref _label, 17);
            Style(ref _small, 15);
            Style(ref _bold, 19, true);
            Style(ref _title, 30, true, TextAnchor.MiddleCenter);
            Style(ref _center, 17, false, TextAnchor.MiddleCenter);
            Style(ref _right, 15, false, TextAnchor.UpperRight);

            UiSkin.Ensure();

            if (_button == null)
            {
                _button = new GUIStyle(GUI.skin.button) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                if (Font != null) _button.font = Font;
            }

            // 把 UI 美术的九宫格按钮装进全局皮肤。
            // 这样做的理由：HUD 里有十几处 Btn(rect, text) 调用，逐个传样式既冗长又容易漏；
            // 而整个 IMGUI 界面都由本 HUD 独占绘制，改 GUI.skin.button 的作用域是可控的。
            // 资源缺失时保持 Unity 默认皮肤，界面不会因此打不开。
            if (UiSkin.Button != null && !_skinApplied)
            {
                _skinApplied = true;
                GUIStyle b = GUI.skin.button;
                b.normal.background = UiSkin.Button;
                b.hover.background = UiSkin.ButtonHover;
                b.active.background = UiSkin.ButtonPress;
                b.focused.background = UiSkin.ButtonHover;
                b.border = new RectOffset(14, 14, 14, 14);
                if (Font != null) b.font = Font;
            }
            if (UiSkin.Button != null)
            {
                _button.normal.background = UiSkin.Button;
                _button.hover.background = UiSkin.ButtonHover;
                _button.active.background = UiSkin.ButtonPress;
                _button.border = new RectOffset(14, 14, 14, 14);
            }

            // v71：滑动条样式。
            // IMGUI 的滚动条来自 GUI.skin 的 verticalScrollbar / horizontalScrollbar —— 不是
            // 由 GUI.BeginScrollView 的参数决定的，所以只能改皮肤。Unity 默认皮肤是深灰方块，
            // 在一整套奶油木色的界面里像外来物；这里换成图集里的凹槽 + 圆头滑块。
            // 注意 thumb 的 border 要跟着贴图走，否则九宫格拉伸会把圆头拉成方头。
            if (UiSkin.ScrollTrack != null && UiSkin.ScrollThumb != null)
            {
                SkinScrollTrack(GUI.skin.verticalScrollbar, vertical: true);
                SkinScrollThumb(GUI.skin.verticalScrollbarThumb, vertical: true);
                SkinScrollTrack(GUI.skin.horizontalScrollbar, vertical: false);
                SkinScrollThumb(GUI.skin.horizontalScrollbarThumb, vertical: false);
            }
        }

        /// <summary>滑块：圆头浅色条。border 必须跟贴图的圆角一致，否则九宫格拉伸会把圆头拉方。</summary>
        private static void SkinScrollThumb(GUIStyle st, bool vertical)
        {
            if (st == null) return;
            st.normal.background = UiSkin.ScrollThumb;
            st.hover.background = UiSkin.ScrollThumb;
            st.active.background = UiSkin.ScrollThumb;
            st.border = new RectOffset(7, 7, 7, 7);
            st.fixedWidth = vertical ? 14f : 0f;
            st.fixedHeight = vertical ? 0f : 14f;
        }

        /// <summary>轨道：深色凹槽，铺满整条。</summary>
        private static void SkinScrollTrack(GUIStyle st, bool vertical)
        {
            if (st == null) return;
            st.normal.background = UiSkin.ScrollTrack;
            st.hover.background = UiSkin.ScrollTrack;
            st.active.background = UiSkin.ScrollTrack;
            st.border = new RectOffset(6, 6, 6, 6);
            st.fixedWidth = vertical ? 14f : 0f;
            st.fixedHeight = vertical ? 0f : 14f;
        }

        private bool _skinApplied;

        /// <summary>
        /// ★ v182：出现动画的计时（秒）。只在 `EventType.Layout` 那一趟推进一次，
        /// 避免 IMGUI 一帧多次调用把动画跑快。**换一批牌才归零**，播完就停在 1 ⇒ 不会每帧重播。
        /// </summary>
        private float _cardAppearT;
        /// <summary>这一批牌的身份（第几抽 / 张数 / 等级 / 重抽次数）。变了就重放出现动画。</summary>
        private int _cardAppearSig = -1;
        /// <summary>融合动画的计时（秒）。`&lt; 0` = 没有在融合。</summary>
        private float _fuseT = -1f;
        /// <summary>正在飞的那张卡（下标）。</summary>
        private int _fusePick = -1;
        /// <summary>动画已播完、这次 ApplyCard 还没交给 RunSession（`ConsumeInto` 见它才发确认）。</summary>
        private bool _fuseReady;

        /// <summary>
        /// ★ v185：召唤轮转的计时（秒）。同样只在 `EventType.Layout` 那一趟推进。
        /// **赋值处自带上限 `SummonDur × 2`**（`DrawLevelReward` 里）⇒ 结构上不会停在"永真"。
        /// </summary>
        private float _summonT;
        /// <summary>同 id 合并的计时（秒）。`&lt; 0` = 没有在合并。</summary>
        private float _mergeT = -1f;
        /// <summary>合并的两张卡（下标）。</summary>
        private int _mergeA = -1;
        /// <summary>合并的两张卡（下标）。</summary>
        private int _mergeB = -1;
        /// <summary>这一批牌已经判过"要不要合并"了（只判一次，不每帧重找）。</summary>
        private bool _mergeChecked;

        /// <summary>`DrawLevelReward` 本帧算出的输入锁定值（**只读探针**用，不参与任何判定）。</summary>
        private bool _lastLocked;

        /// <summary>
        /// ★ 只读探针开关（**默认关**）★ —— 抽卡面板"退出被卡住"时读一行状态：
        /// `面板开/关 | appearing | Fusing | _fuseT | _fusePick | _fuseReady | locked | RewardPickConfirmed`
        ///
        /// 与 `RunSession.LogShopTruck` 同一套路：诊断能力留着，默认不刷日志。
        /// 打开方式：`GameBootstrap.ToggleRewardProbe()`（play 模式下由 `unity_drive send` 调用）。
        /// ⚠️ 它只 `Log.Info`，**不写任何字段** ⇒ 开着也不会改变行为 ✓
        /// </summary>
        public static bool RewardProbe;

        /// <summary>
        /// ★ v185：**只读**的"合并动画演示开关"（**默认关**）★
        ///
        /// 为什么要它：用户第 ④ 条是"若停下的 3 张里有**同 id** 的就播合并"，
        /// 而 `RunSession.DrawRewardCards`（`RunSession.cs:2036-2048`）**在发牌时已经按 `Def.id` 合并过**
        /// ⇒ 面板上那 3 张的 id **必然是互不相同**的 ⇒ 真实数据下这条分支永远不触发（用户也说了"可能没有"✓）。
        /// 那条分支要能被验收，就得有一条**不碰数据**的演示通路 ⇒ 这个开关只做一件事：
        /// 把"要不要合并"的判据换成"前两张"，**动画、时长、锁定、兜底一字不改** ✓
        ///
        /// ⚠️ 它**只读**：不写 `RewardCards`、不改 `Amount`/`Copies`、不影响 `ApplyCard` ✓
        /// ⚠️ 默认 false ⇒ 真实玩法里判据仍是 `Def.id` 相等 ✓
        /// 打开方式：`DebugApi` 的 send（与 `RewardProbe` 同一套路）。
        /// </summary>
        public static bool MergeDemo;

        // ------------------------------------------------------------ 绘制原语
        /// <summary>
        /// 商店报价 → 图标资源名。
        /// v72：道具也有图标了，所以这里不再只认种子——返回**完整的资源名**，
        /// 调用方拿去 Resources.Load 即可。返回 null 表示该报价没有图标，走占位色块。
        ///
        /// ★ v274：**补上插件这一支**（用户原话："护壤显示的就是一个纯绿色方块"）★
        /// ── 根因（改前取证）──
        ///   原来这里对 `OfferKind.Plugin` 直接 `return null`（旧第 3758 行）
        ///   ⇒ `DrawOfferIcon(icon, QualityColor(o.Quality), null)` 取不到图
        ///   ⇒ 落进 `DrawOfferTexture` 的 else 分支（旧第 3822-3827 行）
        ///     `GUI.color = quality; DrawTexture(...)` —— 画出来的正是**一块纯品质色方块** ✓
        ///   护壤 = `fert_ward`，品质 C（"一般"）⇒ 那块绿方块就是**品质色**，不是图标 ✗
        ///   而 `icon_plugin_fert_ward.png` 一直都在（v258 重绘过），只是**从没被问过** ✓
        /// ── 为什么补在这一层而不是改画法 ──
        ///   `DrawOfferTexture` 的 else 分支是**三条路（商店/种子卡/道具栏）共用的兜底**，
        ///   动它会把"真的没有图"也一起改掉；只有这里缺一支 ⇒ 补这一支 ✓
        /// </summary>
        private static string IconResOf(ShopOffer o)
        {
            if (o == null) return null;
            if (o.Kind == OfferKind.Seed && o.Seed != null) return "icon_plant_" + o.Seed.Id;
            if (o.Kind == OfferKind.Item && o.Item != null) return "icon_item_" + o.Item.Id;
            // ★ v274：插件（第三类商品）也有 `Art/UI/icon_plugin_<id>.png` ✓
            if (o.Kind == OfferKind.Plugin && o.Plugin != null) return "icon_plugin_" + o.Plugin.id;
            return null;
        }

        /// <summary>
        /// 卡片图标格：品质色打底 + 图标本体。
        ///
        /// v72：图标从"3D 渲染"换成了 **2D 手绘**（gen_icons2d.py / gen_item_icons.py）。
        /// 渲染图的问题不是不好看，是**缩到 64px 就糊成色块**，而且 16+15 张各有各的受光角度，
        /// 排在一起像 31 张不同的照片。2D 平涂 + 统一描边才像"一套"。
        /// 图标缺失时退回纯色块，界面不会因此变空。
        /// </summary>
        private void DrawOfferIcon(Rect icon, Color quality, string iconRes)
        {
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(iconRes))
            {
                // ★ v191：道具图标统一走 `ItemIconOf`（PNG → 程序化位掩码，两条路都缓存）★
                //   `IconResOf` 给道具返回的正是 `"icon_item_" + id` ⇒ 这里把前缀剥掉，
                //   换成"按 id 取图"⇒ 22 件新道具（没有 PNG）在商店货架上也有图标 ✓
                //   ⚠️ 种子 / 插件 / 植物仍走原来的 `Resources.Load`：**像素逐字节不变** ✓
                const string itemPrefix = "icon_item_";
                // ★ v274：插件同理走 `PluginIconOf`（按 id 缓存 —— 货架每帧重画，
                //   不缓存就是每帧 4 次 Resources.Load）。种子/植物仍走原来的 Resources.Load
                //   ⇒ 那两类的像素与改前逐字节相同 ✓
                const string pluginPrefix = "icon_plugin_";
                if (iconRes.Length > itemPrefix.Length
                    && iconRes.StartsWith(itemPrefix, System.StringComparison.Ordinal))
                    tex = ItemIconOf(iconRes.Substring(itemPrefix.Length));
                else if (iconRes.Length > pluginPrefix.Length
                    && iconRes.StartsWith(pluginPrefix, System.StringComparison.Ordinal))
                    tex = PluginIconOf(iconRes.Substring(pluginPrefix.Length));
                else
                    tex = Resources.Load<Texture2D>("Art/UI/" + iconRes);
            }
            DrawOfferTexture(icon, quality, tex);
        }

        /// <summary>
        /// ★ v184：图标格的**本体**（收下发好的贴图）★
        ///
        /// 为什么把"取图"和"画图"拆开 ✗ 不是重构洁癖：
        ///   菜单的道具栏要按 `itemId` 走**缓存**（见 `ItemIconOf`），
        ///   而 `DrawOfferIcon` 的签名是"资源名"，两者只能二选一 ——
        ///   ⇒ 画法留在一处（本函数），取图交给调用方 ⇒ 不会出现"两套画法"✓
        ///   ⚠️ 商店 / 种子卡 / 植物格走的仍是 `DrawOfferIcon` ⇒ **像素与改前完全一致** ✓
        /// </summary>
        private void DrawOfferTexture(Rect icon, Color quality, Texture2D tex)
        {
            Color prev = GUI.color;
            GUI.color = IconWellBg;
            GUI.DrawTexture(icon, White);

            if (tex != null)
            {
                // 品质描边：一圈品质色垫在图标后面，玩家一眼看出稀有度。
                // v71：从 2px 平边升级成**多层霓虹光晕**——由内到外逐层变淡变宽，
                //      读起来是"在发光"而不是"贴了一圈色纸"。层数固定 4 层，
                //      再多在 64px 的图标上就糊成一片了。
                for (int k = 4; k >= 1; k--)
                {
                    float e = k * 2.2f;
                    float a = 0.16f + 0.16f * (5 - k);
                    GUI.color = new Color(quality.r, quality.g, quality.b, a);
                    GUI.DrawTexture(new Rect(icon.x - e, icon.y - e, icon.width + e * 2f, icon.height + e * 2f), White);
                }
                GUI.color = new Color(quality.r * 0.7f, quality.g * 0.7f, quality.b * 0.7f, 1f);
                GUI.DrawTexture(new Rect(icon.x - 1f, icon.y - 1f, icon.width + 2f, icon.height + 2f), White);
                GUI.color = prev;
                GUI.DrawTexture(new Rect(icon.x, icon.y, icon.width, icon.height), White);   // 先垫深底
                GUI.DrawTexture(icon, tex, ScaleMode.ScaleToFit, true);
            }
            else
            {
                GUI.color = quality;
                GUI.DrawTexture(new Rect(icon.x + 6f, icon.y + 6f, icon.width - 12f, icon.height - 12f), White);
                GUI.color = prev;
            }
            GUI.color = prev;
        }

        // ────────────────────────────────────────────────────────────────────────
        //  ★ v184：**菜单道具图标**（用户原话："菜单的道具显示道具图标"）★
        //
        //  ── 资源在哪（**glob 过，不是猜的** ✓）──
        //    `Assets/Resources/Art/UI/icon_item_*.png` ⇒ 加载键 = `Art/UI/icon_item_<itemId>`
        //    （与商店同一套：`IconResOf` 给道具返回的正是 `"icon_item_" + o.Item.Id` ✓）
        //
        //  ── 为什么按 id 缓存 ✗ ──
        //    `Resources.Load` 每次调用都要走一遍资源索引；道具格一屏十几格、每帧重画 ⇒
        //    与 `PluginIconOf`（`ImGuiHud.cs:466`）同一条路子：**查一次，null 也存** ✓
        //    null 也存这一条是重点：缺图的道具不必每帧再查一次（本工程道具图标就**缺**大半 ✓）
        // ────────────────────────────────────────────────────────────────────────
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _itemIconCache =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        /// <summary>道具图标（缓存；缺图返回 null ⇒ 调用方退成品质色块，与商店/种子卡同一套退路 ✓）。</summary>
        private static Texture2D ItemIconOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (_itemIconCache.TryGetValue(itemId, out Texture2D cached)) return cached;
            Texture2D t = Resources.Load<Texture2D>("Art/UI/icon_item_" + itemId);
            // ★ v191：PNG 没有再退**程序化位掩码**（22 件新道具走这条，老道具的 7 张 PNG 不变）★
            //   为什么不补 22 张 PNG：见 `TableDatabase.ItemIconMasks` 上方那段说明 ✓
            if (t == null) t = ItemMaskTex(itemId);
            _itemIconCache[itemId] = t;
            return t;
        }

        // ──────────────────────────────────────────────────────────────────────
        //  ★ v191：道具的程序化图标（位掩码在 `TableDatabase.ItemIconMasks`）
        //
        //  与 v188 的弹字图标（`PopupIconTex`）**同一套做法**：字符画 → `Texture2D`
        //  （Point 采样）→ `GUI.DrawTexture`；而且**每件只生成一次**（缓存）⇒ 运行期零分配 ✓
        //
        //  ★ 颜色**烘进纹理**（正/负两大类由 `TableDatabase.ItemIconPalette` 决定）★
        //    不在绘制时用 `GUI.color` 染色，理由只有一个：商店货架 / 菜单道具栏 / 详情框
        //    **三处**都走 `DrawOfferTexture`，靠绘制态就得改三处，漏一处就出现
        //    "同一件道具两种颜色" ✗ 而"这张图是什么颜色"本来就是图的属性 ✓
        // ──────────────────────────────────────────────────────────────────────
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> _itemMaskTex =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        private static Texture2D ItemMaskTex(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (_itemMaskTex.TryGetValue(itemId, out Texture2D cached) && cached != null) return cached;

            string[] rows = TableDatabase.ItemIconMaskOf(itemId);
            if (rows == null) return null;
            int h = rows.Length;
            int w = TableDatabase.ItemIconSize;
            if (h != w) return null;                       // 掩码必须是 11×11
            for (int i = 0; i < h; i++)
                if (rows[i] == null || rows[i].Length != w) return null;   // 不等宽 ⇒ 不出图

            ItemDef def = TableDatabase.GetItem(itemId);
            TableDatabase.ItemIconPalette(def != null ? def.Category : 1, out Color main, out Color accent);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "ItemIcon_" + itemId,
                // 点采样 + 不循环：位掩码放大要"方块"，不要"糊"
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[w * h];
            Color32 cm = main, ca = accent, clear = new Color32(255, 255, 255, 0);
            for (int y = 0; y < h; y++)
            {
                // 掩码第 0 行是**图像顶部**，纹理第 0 行是**底部** ⇒ 翻转（与 `PopupIconTex` 一致）
                string row = rows[h - 1 - y];
                for (int x = 0; x < w; x++)
                {
                    char c = row[x];
                    px[y * w + x] = c == '#' ? cm : (c == '+' ? ca : clear);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            _itemMaskTex[itemId] = tex;
            return tex;
        }

        /// <summary>
        /// v71：顶部雨滴标记。
        ///
        /// 需求是"调整雨滴大小（顶上增加UI标记）"。为什么要标记：雨滴是缺水时的续命资源，
        /// 但它随机落在 40×40 场地的任何位置，俯视相机未必拍得到——玩家只看到下方弹
        /// "拾取雨滴 +20"的提示，却不知道该往哪走。
        /// 做法：在**屏幕顶部边缘**按落点的世界 x 打一个小水滴标记，
        /// 于是"在哪个方向"一眼可见，而不需要把相机挪过去。
        /// </summary>
        private void DrawRainMarkers(RunSession s)
        {
            if (s == null || s.World == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            float scale = UiScale;
            _rainMarkX.Clear();          // 每帧重建去重表（同帧内多枚标记之间也要去重）
            int shown = 0;
            for (int i = 0; i < s.World.Pickups.Count && shown < 10; i++)
            {
                Pickup p = s.World.Pickups[i];
                if (!p.IsRain) continue;
                Vector3 sp = cam.WorldToScreenPoint(new Vector3(p.Pos.x, 0.6f, p.Pos.y));
                if (sp.z <= 0f) continue;
                Vector2 hud = WorldToHud(sp, scale, Screen.height);

                // v77：**始终显示**，不再"只标屏幕外的"。
                // 旧条件是 `hud.y` 落在画面内就跳过——那是按"相机贴近角色"设计的。
                // 但 v75 把相机拉远之后，雨滴基本永远在视野内，于是这个提示**一次都不会出现**，
                // 等于功能被自己的相机改动废掉了。现在改为始终在顶部按落点的世界 x 打一枚箭头：
                // 它表达的是"那个方向有雨滴"，和看不看得见无关。
                // 去重：同一列附近只留一枚，否则 10 滴雨会在顶排挤成一条。
                bool tooClose = false;
                for (int k = 0; k < _rainMarkX.Count; k++)
                    if (Mathf.Abs(_rainMarkX[k] - hud.x) < 26f) { tooClose = true; break; }
                if (tooClose) continue;

                float x = Mathf.Clamp(hud.x, 20f, _virtualW - 20f);
                Color prev = GUI.color;

                // v75：改成**蓝色下指箭头**。
                // 旧版画的是一颗水滴图标 + 一条竖线，问题是"水滴"这个形状在 20px 下
                // 和背景里的雨滴拾取物长得一样，玩家会以为屏幕顶上掉了东西下来。
                // 箭头表达的是**方向**（"在那边"），语义和"这里有个雨滴"分得开。
                Color blue = new Color(0.30f, 0.68f, 1f, 1f);
                Color deep = new Color(0.10f, 0.32f, 0.62f, 1f);

                // 三角箭头：用逐行递减宽度的横条堆出来（IMGUI 只能画矩形）
                const int rows = 12;
                for (int r = 0; r < rows; r++)
                {
                    float w = Mathf.Lerp(22f, 2f, r / (float)(rows - 1));
                    GUI.color = r < 2 ? deep : blue;
                    GUI.DrawTexture(new Rect(x - w * 0.5f, 3f + r * 2f, w, 2.4f), White);
                }
                // 箭头底下加一圈光晕，夜里也看得见
                GUI.color = new Color(0.45f, 0.80f, 1f, 0.28f);
                GUI.DrawTexture(new Rect(x - 15f, 0f, 30f, 30f), White);
                GUI.color = prev;
                _rainMarkX.Add(x);
                shown++;
            }
        }

        /// <summary>本帧已画出的雨滴标记横坐标，用于去重（同一列附近只留一枚）。</summary>
        private readonly System.Collections.Generic.List<float> _rainMarkX =
            new System.Collections.Generic.List<float>();

        /// <summary>面板底：优先用 UI 美术的九宫格贴图，资源缺失时退回纯色。
        /// 九宫格让同一张 64×64 的图能画任意大小的窗口而不变形。
        ///
        /// ★ v223 UI 高级化（只改观感，不动布局）★
        ///   ① 外投影：面板**外侧**三圈同心半透明黑（1~4px 柔化）—— 九宫格贴图本身画不出面板外面，
        ///      所以投影只能在代码里画；不占布局，不改变任何 Rect 的取值 ✓
        ///   ② 进入动效：面板**第一次出现**的 0.16 s 内，底板做 1.02 → 1.00 缩放 + 0.35 → 1.00 淡入。
        ///      只动底板（文字图标由调用方按原 Rect 画）⇒ 不会出现"字先亮底板后亮"的错位感，
        ///      也不会因为动画把内容推到不该在的位置 ✓
        ///   ③ 金内衬：九宫格已有内衬，这里再压一道 1px 的暖金发丝线（alpha 0.20），
        ///      让暗面板/亮面板都被"框"住一次 —— 这是高级感的主要来源 ✓
        /// </summary>
        private void Panel(Rect r)
        {
            UiSkin.Ensure();
            Rect rr = PanelEnterRect(r, out float a);
            PanelShadow(rr, a);
            if (UiSkin.PanelStyle != null && UiSkin.PanelStyle.normal.background != null)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Box(rr, GUIContent.none, UiSkin.PanelStyle);
                GUI.color = prev;
            }
            else
            {
                Color prev = GUI.color;
                GUI.color = new Color(PanelBg.r, PanelBg.g, PanelBg.b, PanelBg.a * a);
                GUI.DrawTexture(rr, White);
                GUI.color = prev;
            }
            PanelLining(rr, a);
        }

        /// <summary>暗面板（商店 / 菜单用，和 HUD 的亮面板区分层级）。</summary>
        private void PanelDark(Rect r)
        {
            UiSkin.Ensure();
            Rect rr = PanelEnterRect(r, out float a);
            PanelShadow(rr, a * 1.15f);
            if (UiSkin.PanelDarkStyle != null && UiSkin.PanelDarkStyle.normal.background != null)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.Box(rr, GUIContent.none, UiSkin.PanelDarkStyle);
                GUI.color = prev;
            }
            else
            {
                Color prev = GUI.color;
                GUI.color = new Color(0.06f, 0.06f, 0.07f, 0.92f * a);
                GUI.DrawTexture(rr, White);
                GUI.color = prev;
            }
            PanelLining(rr, a);
        }

        // ───────────────────── v223：面板的投影 / 内衬 / 进入动效 ─────────────────────

        /// <summary>进入动效时长（需求给的 0.12~0.18 s 区间取中值）。</summary>
        private const float PanelEnterSec = 0.16f;

        /// <summary>以"面板尺寸"为键的出生时刻表（同一张面板每帧同一个 Rect ⇒ 只触发一次动效）。</summary>
        private readonly System.Collections.Generic.Dictionary<int, float> _panelBorn =
            new System.Collections.Generic.Dictionary<int, float>();

        private float _panelGcAt;

        /// <summary>
        /// 底板进入动效：返回"此刻该用的 Rect"并给出不透明度。
        /// 键取 (宽,高) 的整数哈希 —— 面板跟着鼠标走的提示框每帧换位置，
        /// 若按 Rect 全键就会**每帧重新触发**⇒ 永远停在 1.02/0.35 看起来像抖。按尺寸键就没有这个问题 ✓
        /// 不透明度下限 0.35（不是 0）⇒ 万一键撞上，最坏也只是"稍淡一下"，不会消失 ✓
        /// </summary>
        private Rect PanelEnterRect(Rect r, out float alpha)
        {
            alpha = 1f;
            // `PanelEnterSec` 是正常量（0.16）⇒ 原来那句 `if (PanelEnterSec <= 0f) return r;` 恒假、
            // 其后的 `return r;` 永远走不到（CS0162）。要支持"关掉入场动画"，把常量改成字段再说。

            int w = Mathf.Max(1, Mathf.RoundToInt(r.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(r.height));
            int key = unchecked(w * 73856093 ^ h * 19349663);

            float now = Time.unscaledTime;
            if (!_panelBorn.TryGetValue(key, out float born))
            {
                born = now;
                _panelBorn[key] = born;
                // 出生时刻表不会无限长，但加个保险：每 2 s 清一次已经过期的项
                if (now - _panelGcAt > 2f)
                {
                    _panelGcAt = now;
                    var dead = new System.Collections.Generic.List<int>();
                    foreach (var kv in _panelBorn)
                        if (now - kv.Value > 3f) dead.Add(kv.Key);
                    for (int i = 0; i < dead.Count; i++) _panelBorn.Remove(dead[i]);
                }
            }

            float k = Mathf.Clamp01((now - born) / PanelEnterSec);
            if (k >= 1f) return r;                       // 动画结束后**逐位**等于原 Rect（布局零风险）

            float e = 1f - (1f - k) * (1f - k);          // ease-out（先快后慢，像"落定"）
            alpha = Mathf.Lerp(0.35f, 1f, e);
            float s = Mathf.Lerp(1.02f, 1f, e);          // 1.02 → 1.00
            Vector2 c = r.center;
            return new Rect(c.x - r.width * s * 0.5f, c.y - r.height * s * 0.5f,
                            r.width * s, r.height * s);
        }

        /// <summary>面板外的柔化投影：三圈同心半透明黑（1/2/4 px）。画在面板**外面**，不占布局。</summary>
        private void PanelShadow(Rect r, float alpha)
        {
            if (alpha <= 0.01f) return;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.10f * alpha);
            GUI.DrawTexture(new Rect(r.x - 4f, r.y - 3f, r.width + 8f, r.height + 9f), White);
            GUI.color = new Color(0f, 0f, 0f, 0.16f * alpha);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 1.5f, r.width + 4f, r.height + 6f), White);
            GUI.color = new Color(0f, 0f, 0f, 0.22f * alpha);
            GUI.DrawTexture(new Rect(r.x + 0.5f, r.y + 1.2f, r.width, r.height + 2.4f), White);
            GUI.color = prev;
        }

        /// <summary>暖金发丝内衬（1px，alpha 很低）：九宫格已有内衬，这一道负责"把面板框住一次"。</summary>
        private void PanelLining(Rect r, float alpha)
        {
            if (alpha <= 0.01f) return;
            if (r.width < 24f || r.height < 24f) return;
            StrokeRect(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f),
                       new Color(0.86f, 0.72f, 0.40f, 0.20f * alpha), 1f);
        }

        /// <summary>
        /// 条：九宫格边框 + 九宫格填充。
        /// 填充放在 BeginGroup 里，超出的部分会被 Group 裁掉——这样填充条的
        /// 圆角端头不会被拉变形，同时进度可以任意比例。
        /// </summary>
        private void Bar(Rect r, float pct, Color fill, string text)
        {
            UiSkin.Ensure();
            bool skinned = UiSkin.BarFrameStyle != null && UiSkin.BarFrameStyle.normal.background != null;

            if (skinned)
            {
                GUI.Box(r, GUIContent.none, UiSkin.BarFrameStyle);
                float fw = r.width * Mathf.Clamp01(pct);
                if (fw > 1f)
                {
                    Color prevC = GUI.color;
                    GUI.color = fill;
                    GUI.BeginGroup(new Rect(r.x, r.y, fw, r.height));
                    GUI.Box(new Rect(0f, 0f, fw, r.height), GUIContent.none, UiSkin.BarFillStyle);
                    GUI.EndGroup();
                    GUI.color = prevC;
                }
            }
            else
            {
                Color prev = GUI.color;
                GUI.color = BarBg;
                GUI.DrawTexture(r, White);
                GUI.color = fill;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(pct), r.height), White);
                GUI.color = prev;
            }
            Txt(new Rect(r.x + 6, r.y, r.width, r.height), text, _small);
        }

        // ─────────────────────────── v177：无底板的三个绘制原语 ───────────────────────────

        /// <summary>
        /// 矩形描边（只有四条边，中间不填）。IMGUI 没有 stroke 原语，用四条 1px 级矩形拼。
        /// </summary>
        private void StrokeRect(Rect r, Color c, float t)
        {
            if (r.width <= t * 2f || r.height <= t * 2f) return;
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), White);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), White);
            GUI.DrawTexture(new Rect(r.x, r.y + t, t, r.height - t * 2f), White);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y + t, t, r.height - t * 2f), White);
            GUI.color = prev;
        }

        /// <summary>
        /// v177：**无底板的框**——中间是透明的（场景透出来），只有投影 + 内外两道描边。
        ///
        /// 两道描边不是装饰：外圈**暗**、内圈**亮**。亮底图上外圈暗线划出边界，
        /// 暗底图上内圈亮线划出边界 ⇒ 一张图在两种底上都有边界（这正是底板原来干的事）。
        /// </summary>
        private void BareFrame(Rect r, Color stroke, float weight = 1.5f, bool shadow = true)
        {
            if (shadow)
            {
                Color p = GUI.color;
                GUI.color = HudShadow;
                GUI.DrawTexture(new Rect(r.x + 1.5f, r.y + 2.5f, r.width, r.height), White);
                GUI.color = p;
            }
            StrokeRect(new Rect(r.x - 1.5f, r.y - 1.5f, r.width + 3f, r.height + 3f),
                new Color(0.04f, 0.03f, 0.02f, 0.55f), weight + 1.2f);
            StrokeRect(r, stroke, weight);
        }

        /// <summary>
        /// v177：**无底板的条**（血条 / 护盾 / 经验 / 水壶 / 肥料用它；商店与菜单仍走 `Bar`）。
        ///
        /// 与 `Bar` 的唯一区别是**不画底槽**（`BarFrameStyle` 那张深色九宫格）：
        ///   底槽 → 换成 <see cref="HudStrokeDim"/> 的 1.5px 描边（空的那一段仍然看得出有多长）
        ///   填充 → 保留（填充就是"数值本身"，需求要求保留）
        ///   文字 → 换成四向描边的 `HudLabel`（原来文字是压在底槽上的深棕色）
        ///
        /// 填充左右各内缩 1.5px：描边必须始终落在填充**之外**，
        /// 否则满血时填充会盖住描边，"条"就没有边界了（= 又变回一块无边界色块）。
        /// </summary>
        private void BarBare(Rect r, float pct, Color fill, string text)
        {
            UiSkin.Ensure();

            // ① 投影：整条向下 2.5px 的暗块。亮底图（麦田/沙地/正午草地）上靠它划出边界。
            Color prev = GUI.color;
            GUI.color = HudShadow;
            GUI.DrawTexture(new Rect(r.x + 1.5f, r.y + 2.5f, r.width, r.height), White);
            GUI.color = prev;

            // ② 填充 = 数值本身
            float fw = r.width * Mathf.Clamp01(pct) - 3f;
            if (fw > 0.5f)
            {
                Rect fr = new Rect(r.x + 1.5f, r.y + 1.5f, fw, r.height - 3f);
                GUI.color = fill;
                if (UiSkin.BarFillStyle != null && UiSkin.BarFillStyle.normal.background != null)
                    GUI.Box(fr, GUIContent.none, UiSkin.BarFillStyle);
                else
                    GUI.DrawTexture(fr, White);
                GUI.color = prev;
            }

            // ③ 外描边（暗底图上可见）+ 内侧压线（让条有厚度）
            StrokeRect(r, HudStrokeDim, 1.5f);
            StrokeRect(new Rect(r.x + 1.5f, r.y + 1.5f, r.width - 3f, r.height - 3f), HudInnerLine, 1f);

            // ④ 文字描边（压在场景上）
            HudLabel(new Rect(r.x + 8f, r.y, r.width - 8f, r.height), text, HudBody);
        }

        /// <summary>
        /// v177：**直接压在场景上的文字**——四向近黑描边 + 本体亮色。
        ///
        /// 与既有 `LabelOutlined` 的关系：同一个做法，多一个可调描边宽度。
        /// 30px 的计时器用 1.5px 描边是糊的（描边相对字高太细），必须能加粗 ⇒ 所以这里吃一个参数，
        /// 而不是复制一份写死 1.5 的 `LabelOutlined` ✗
        /// 本体色取 `style.normal.textColor` ⇒ 颜色只在**样式**里定一处，调用处不传色 ✓
        /// </summary>
        private void HudLabel(Rect r, string text, GUIStyle style, float outline = 1.5f, float outlineAlpha = 0.92f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Color prev = style.normal.textColor;
            style.normal.textColor = new Color(0.05f, 0.04f, 0.03f, prev.a * outlineAlpha);
            Txt(new Rect(r.x - outline, r.y, r.width, r.height), text, style);
            Txt(new Rect(r.x + outline, r.y, r.width, r.height), text, style);
            Txt(new Rect(r.x, r.y - outline, r.width, r.height), text, style);
            Txt(new Rect(r.x, r.y + outline, r.width, r.height), text, style);
            style.normal.textColor = prev;
            Txt(r, text, style);
        }

        // ── 压在场景上的文字样式（近白 + 由 HudLabel 加描边）──
        // 为什么不复用 `OnDark`（0.95,0.92,0.84）：它的亮度是给"棕色卡片"配的，
        // 压在正午草地上偏灰；这里要的是最亮的一档。
        private GUIStyle _hudBody, _hudBold, _hudTitle, _hudCenter, _hudTiny;
        private GUIStyle HudBody => HudStyle(ref _hudBody, _small, new Color(0.99f, 0.97f, 0.91f));
        private GUIStyle HudBold => HudStyle(ref _hudBold, _bold, new Color(1f, 0.98f, 0.92f));
        private GUIStyle HudTitle => HudStyle(ref _hudTitle, _title, new Color(1f, 0.98f, 0.90f));
        private GUIStyle HudCenter => HudStyle(ref _hudCenter, _center, new Color(1f, 0.96f, 0.86f));
        /// <summary>12px 档（插件空槽里的"空"字）。`Tiny` 本身是**深色**的——它是给品质色实心块配的；
        /// 空槽现在没有底了，深字会直接消失 ⇒ 必须另给一个压场景用的亮色版本 ✓</summary>
        private GUIStyle HudTiny => HudStyle(ref _hudTiny, Tiny, new Color(0.97f, 0.95f, 0.88f, 0.85f));

        private static GUIStyle HudStyle(ref GUIStyle slot, GUIStyle basis, Color text)
        {
            if (slot == null && basis != null)
            {
                slot = new GUIStyle(basis) { wordWrap = false };
                slot.normal.textColor = text;
            }
            return slot;
        }

        /// <summary>右对齐的一档（武器区的状态行要贴右边，与左边标题同一行 ⇒ 只占 24px 高）。</summary>
        private GUIStyle _hudRight;
        private GUIStyle HudRight
        {
            get
            {
                if (_hudRight == null)
                {
                    // 垂直居中（不是 UpperRight）：这一格只有 24px 高，贴顶会让字浮在格子上沿 ✓
                    _hudRight = new GUIStyle(_small)
                    {
                        alignment = TextAnchor.MiddleRight,
                        wordWrap = false
                    };
                    _hudRight.normal.textColor = new Color(0.99f, 0.97f, 0.91f);
                }
                return _hudRight;
            }
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v180：悬停详情（用户第 ② / ③ 条）★
        //
        //  ── 为什么不做"点击选中再显示"✗ ★
        //    ② 的原话是"**鼠标悬停** = 显示该插件详情"✓ ③ 是"悬停或选中查看"✓
        //    ⇒ 悬停是两处**都**接受的唯一形态 ⇒ 只实现悬停一条，两处共用 ✓
        //
        //  ── 为什么先收集、最后再画 ✗ ★
        //    悬停框要**压在所有东西上面**。若在画格子的那一刻顺手画，
        //    后面画的（下一把武器、下一行商品、底部按钮）会盖住它 ✗
        //    ⇒ 本帧先把要显示的**文字**存进 `_tipLines`，等 `Draw` 末尾统一画一次 ✓
        //      （存文字而不是存"回调"，是因为回调会在**错误的裁剪/矩阵状态**下执行 ✓）
        //
        //  ── 尺寸为什么由**实测文字**算 ✗（这正是"尺寸只由数据决定"的同一条）★
        //    框宽 = max(每行实测宽) + 内边距 ⇒ 文字永远放得下 ✓
        //    若反过来"先定一个 320 宽的框再塞字"，长插件名会被裁 ✗ 而 HUD-AUDIT 抓不到它
        //    （审计只查走 `RowsFor` 的那些行）⇒ 那是自找的一个"看不见的溢出" ✓
        //    实测宽度用**虚拟口径**（与 `RowsFor` 同一口径：`_small.fontSize` 是虚拟字号）✓
        // ══════════════════════════════════════════════════════════════════════════
        private readonly System.Collections.Generic.List<string> _tipLines =
            new System.Collections.Generic.List<string>(8);
        private Vector2 _tipAt;
        private bool _tipOn;

        /// <summary>请求在本帧末尾显示一个悬停框（同一帧只保留**第一个**请求 —— 鼠标只在一个地方）。</summary>
        private void RequestTip(Vector2 mouse, params string[] lines)
        {
            // ★ v203：悬停框被请求 = 鼠标正压在一个"有反映的" UI 元素上 ⇒ 记给光标用 ✓
            //   这条与 `Btn()` 里那条**并列**（不是替代）：增益图标 / 武器格没有 `GUI.Button`，
            //   但它们依然属于"鼠标在 UI 上"这件事 ✓
            _hoverTip = true;
            _hoverFrame = Time.frameCount;
            if (_tipOn) return;
            if (lines == null || lines.Length == 0) return;
            _tipOn = true;
            _tipAt = mouse;
            _tipLines.Clear();
            for (int i = 0; i < lines.Length; i++)
                if (!string.IsNullOrEmpty(lines[i])) _tipLines.Add(lines[i]);
            if (_tipLines.Count == 0) _tipOn = false;
        }

        /// <summary>画悬停框（`Draw` 末尾、仍在 1.35 矩阵里调用）。</summary>
        private void DrawTip()
        {
            if (!_tipOn) return;
            _tipOn = false;
            if (_tipLines.Count == 0) return;

            const float padX = 10f, padY = 6f, lineH = 22f, maxW = 460f;
            float need = 0f;
            for (int i = 0; i < _tipLines.Count; i++)
            {
                float w = _small.CalcSize(new GUIContent(_tipLines[i])).x;
                if (w > need) need = w;
            }
            float w2 = Mathf.Min(maxW, need + padX * 2f);
            float h2 = lineH * _tipLines.Count + padY * 2f;

            // 贴鼠标右下；越界就翻到另一侧（不裁字）
            float x = _tipAt.x + 16f;
            float y = _tipAt.y + 16f;
            if (x + w2 > _virtualW - 4f) x = Mathf.Max(4f, _tipAt.x - w2 - 12f);
            if (y + h2 > _virtualH - 4f) y = Mathf.Max(4f, _virtualH - h2 - 4f);

            Rect box = new Rect(x, y, w2, h2);
            // 底：暗色半透明 + 品质无关（它是"说明层"，不是"品质标记层"）
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.05f, 0.04f, 0.95f);
            GUI.DrawTexture(box, White);
            GUI.color = prev;
            StrokeRect(box, HudStroke, 1.5f);

            for (int i = 0; i < _tipLines.Count; i++)
                Txt(new Rect(box.x + padX, box.y + padY + i * lineH, w2 - padX * 2f, lineH),
                    _tipLines[i], OnDark);
        }

        /// <summary>图标：按名称取 UI 图集里的图标，缺失时返回 false（调用方跳过）。</summary>
        private static bool Icon(string id, Rect r)
        {
            UiSkin.Ensure();
            Texture2D t = null;
            switch (id)
            {
                case "hoe": t = UiSkin.IconHoe; break;
                case "can": t = UiSkin.IconCan; break;
                case "fertilizer": t = UiSkin.IconFert; break;
                case "coin": t = UiSkin.IconCoin; break;
                case "seed": t = UiSkin.IconSeed; break;
                case "heart": t = UiSkin.IconHeart; break;
                case "shield": t = UiSkin.IconShield; break;
                case "clock": t = UiSkin.IconClock; break;
                case "wave": t = UiSkin.IconWave; break;
                case "star": t = UiSkin.IconStar; break;
            }
            if (t == null) return false;
            GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true);
            return true;
        }

        // ------------------------------------------------------------ 左上：状态
        /// <summary>
        /// v177：**去掉底板**（需求"血条不使用背景"）。
        /// 改前：`Panel(10,10,330,122)` 一块奶油底板 + 三根带深色底槽的条。
        /// 改后：无面板、无底槽 —— 三根条各自「投影 + 描边 + 填充」，数值用四向描边文字。
        /// 布局坐标**一个都没动**（20/48/74/98 那四行仍是原值）⇒ 只是"底下那块纸"没了。
        /// </summary>
        private void DrawVitals(RunSession s)
        {
            PlayerActor p = s.World.Player;
            BarBare(new Rect(20, 20, 310, 24), p.Health.HpPct, new Color(0.85f, 0.25f, 0.25f),
                $"生命 {p.Health.Hp:0.#}/{p.Health.MaxHp:0.#}");
            float shieldPct = p.Health.MaxShield <= 0f ? 0f : p.Health.Shield / p.Health.MaxShield;
            float regDelay = TableDatabase.Tuning.player.shieldRegenDelay;
            string shieldState;
            if (p.IsInvulnerable) shieldState = $"无敌 {p.InvulnRemaining:0.0}s";
            else if (p.SinceDamaged >= regDelay) shieldState = "护盾回复中";
            else shieldState = $"{Mathf.Max(0f, regDelay - p.SinceDamaged):0.0}s 后回盾";
            BarBare(new Rect(20, 48, 310, 22), shieldPct, new Color(0.35f, 0.6f, 0.95f),
                $"护盾 {p.Health.Shield:0.#}/{p.Health.MaxShield:0.#}  {shieldState}");
            float expPct = s.Progress.Exp / Mathf.Max(1f, s.Progress.ExpToNext);
            BarBare(new Rect(20, 74, 310, 22), expPct, new Color(0.85f, 0.75f, 0.25f),
                $"Lv{s.Progress.Level}  经验 {s.Progress.Exp:0}/{s.Progress.ExpToNext:0}");
            HudLabel(new Rect(20, 98, 320, 24),
                $"金币 {s.Gold}　　耕地 {s.World.Farm.PlotCount} 块（上限 {s.World.Farm.Capacity}）", HudBody);
            // v66：删掉这里原本的"水壶 N/M　肥料 N/M"一行。
            // 它是旧版 UI 的残留——底部弹药条已经在显示同样的信息，这里是重复。
            // 顺带把面板高度从 142 收到 122：收回来的空间让按键说明面板不再与它重叠
            // （旧版按键面板起点 136 < 状态栏底 152，两块面板是叠在一起的）。

            // ★ v197：增益区（状态块的"第 5 行"）★
            //   挂在 `DrawVitals` 的**末尾**而不是主 `Draw` 里再插一行：
            //   这一块在语义上属于"左上状态区"，跟着它走 ⇒ 将来整块搬动只需搬这一个函数 ✓
            //   ⚠️ 上面四行的坐标**一个都没动**（需求：不要覆盖血条/金币）✓
            DrawBuffs(s);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ★ v197：左上「增益区」—— 健康值下方那一排图标小格
        //
        //  ★ 用户原话 ★
        //    "在左上角健康值下方开一个新区域显示拥有的增益效果，把刚刚设计的带增益的，
        //     获得增益时显示一个图标小格，鼠标停留在上面时展示增益内容"
        //
        //  ── ① 位置为什么是 y = 124 ✗（算出来的，不是估的）──
        //    左上状态块现有的占位（`DrawVitals`，一个坐标都没动）：
        //        生命 20..44 ｜ 护盾 48..70 ｜ 经验 74..96 ｜ 金币/耕地 **98..122**
        //    而 `DrawKeyHelp` 的文字框从 **146** 起（`y = 142` + 4；字形实际从 ~153 才有像素）
        //    ⇒ 唯一可用的空带 = **122..146**（需求同时禁止覆盖血条与金币）✓
        //    22px 的格子落在 **124..146**：上距金币行的字形底 ~7px、下沿正好触到按键说明的字框，
        //    而那个框的前 7px 是空白（垂直居中）⇒ 两块**字形**之间仍留 ~7px ✓
        //    ⚠️ 没有挪 `DrawKeyHelp` 的 `y = 142`：那是另一路可能正在改的地界，
        //       挪它等于把"加一排图标"变成"改整个左栏布局" ✗
        //
        //  ── ② 形态 ──
        //    格子 22px（需求 21~24）｜ 图标 = 11×11 掩码放大到 18×18（Point 采样 ⇒ 方块不糊）
        //    时长条 = 下缘 2.5px，宽度 ∝ `Remain / 峰值`（分母哪来的见 `SyncBuffPeak` 的注释）
        //    层数 = 右上角 `×12`（`HudTiny` + 四向描边 ⇒ 压在绿/橙图标上也读得清）
        //    底板 = 暗色 + `BareFrame` 的"投影 + 外暗内亮"两道描边 —— 与本文件其它格**同一套**
        //
        //  ── ③ 排布：一行最多 8 格；只有 1 个也不突兀 ★ ──
        //    起点 x = 20 —— **与血条 / 金币行 / 按键说明同一条左基准线**（"不突兀"的全部秘密）✓
        //    超出 8 条 ⇒ 前 7 格 + 第 8 格写 `+N`（不折行：折行会往下压到按键说明）✓
        //    0 条 ⇒ **什么都不画**（不留空盒子、不留占位）✓
        //
        //  ── ④ 图标 ✗ 一张新图都没造 ★ ──
        //    `BuffInstance.IconMaskIdx` → `TableDatabase.ItemIconMaskKeyAt(idx)` → **现有的**
        //    `ItemMaskTex(id)`（掩码 → 纹理；色调烘在图里、每件只生成一次）
        //    ⇒ 复用现有那条"字符画 → 纹理"的路，本区**没有**第二份掩码转纹理代码 ✓
        //    下标 = −1 / 越界 / 掩码缺失 ⇒ 暖色块兜底（与"道具缺图退回品质色块"同一条退路）✓
        //
        //  ── ⑤ 悬停 ✗ 不新写浮框 ★ ──
        //    递三行给**现有的** `RequestTip`：`Label` / `×Stacks` / `剩余 x.xs`
        //    它自带"跟随鼠标 + 越界翻边 + 按实测文字定宽"，且在 `Draw` 末尾统一画 ⇒ 层级最高 ✓
        //
        //  ── ⑥ 这一块**只读** ★ ──
        //    增益的产生 / 叠层 / 倒计时全在另一路的 `PlayerBuffs`（`Scripts/Prog/PlayerBuffs.cs`）。
        //    本区一行都不写游戏状态：不 Apply、不 Tick、不改 Remain ✓
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>增益小格边长（需求 21~24px；取 22 的理由见上方 ①）。</summary>
        public const float BuffChipSize = 22f;
        /// <summary>小格之间的间距。</summary>
        public const float BuffChipGap = 4f;
        /// <summary>一行最多几格（需求"最多 8 格"）。</summary>
        public const int BuffRowMax = 8;
        /// <summary>增益区的行顶 y（= 金币行底 122 + 2）。</summary>
        public const float BuffRowY = 124f;
        /// <summary>小格的左基准 x —— 与血条 / 金币行 / 按键说明同一条线。</summary>
        public const float BuffRowX = 20f;

        /// <summary>第 index 格的矩形（**唯一的排布公式**：绘制、悬停命中、探针坐标都读它）。</summary>
        public static Rect BuffChipAt(int index)
            => new Rect(BuffRowX + index * (BuffChipSize + BuffChipGap), BuffRowY, BuffChipSize, BuffChipSize);

        /// <summary>本帧实际画出的小格矩形（探针读它核对坐标；也用于"鼠标停在哪一格"）。</summary>
        public static readonly System.Collections.Generic.List<Rect> BuffChipRects =
            new System.Collections.Generic.List<Rect>(BuffRowMax);

        /// <summary>悬停浮框最近一次递出去的**三行原文**（探针据此证明"屏幕上到底写了什么"）。</summary>
        public static readonly string[] LastBuffTip = new string[3];

        /// <summary>
        /// 探针钩子（正常运行**恒 −1**）：把第 n 格当成"鼠标正停在上面"。
        /// ★ 只替换鼠标坐标、**不短路** `Contains` 判断 ⇒ 抓图走的仍是真人悬停的同一条路径 ✓
        /// </summary>
        public static int ForceBuffTipIndex = -1;

        /// <summary>每条增益"见过的最大剩余秒" —— 时长条的分母（见 `SyncBuffPeak`）。</summary>
        private readonly System.Collections.Generic.Dictionary<string, float> _buffPeak =
            new System.Collections.Generic.Dictionary<string, float>(System.StringComparer.Ordinal);
        private readonly System.Collections.Generic.List<string> _buffSeen =
            new System.Collections.Generic.List<string>(BuffRowMax);
        private readonly System.Collections.Generic.List<string> _buffPeakKeys =
            new System.Collections.Generic.List<string>(BuffRowMax+ 4);

        /// <summary>下标 → 道具 id 的缓存（null 也存：缺键不必每帧再翻一遍整张字典）。</summary>
        private static readonly System.Collections.Generic.Dictionary<int, string> _buffIconKeyCache =
            new System.Collections.Generic.Dictionary<int, string>(8);

        /// <summary>层数角标用的**右对齐** 12px 档（`HudTiny` 本身是左对齐）。</summary>
        private GUIStyle _hudTinyRight;
        private GUIStyle HudTinyRight
        {
            get
            {
                if (_hudTinyRight == null)
                {
                    GUIStyle basis = HudTiny;
                    if (basis == null) return null;
                    _hudTinyRight = new GUIStyle(basis) { alignment = TextAnchor.UpperRight, wordWrap = false };
                    // 11px（不是 12）：22px 的格子右上一格要放得下 `×60` 而不压到描边 ✓
                    _hudTinyRight.fontSize = 11;
                }
                return _hudTinyRight;
            }
        }

        // ── 另一路的增益表：反射找**一次**、之后只读缓存 ──
        private static bool _buffsProbeDone;
        private static System.Reflection.MemberInfo _buffsMember;

        /// <summary>
        /// 取当前局的增益表（**另一路的 `PlayerBuffs`**）。拿不到 ⇒ null（调用方什么都不画）。
        ///
        /// ★ 为什么用反射、而不是直接写 `s.Buffs` ★
        ///   本文件写下这一刻，`RunSession` 上那个成员**还没落定**（另一路正在接线）。
        ///   直接写 `s.Buffs` ⇒ 本文件当场编译不过 ✗
        ///   而需求要的正是"UI 先写好，等它出现自动生效"⇒ 按名字找一次并缓存：
        ///     · 它一出现 ⇒ 读到实例、照常画 ✓
        ///     · 它还没出现 ⇒ 返回 null，本区**不占位、不报错、不刷日志** ✓
        ///   ★ 只在第一次调用时找（`_buffsProbeDone`）⇒ 之后每帧都是"读一个已缓存的成员"，
        ///     没有逐帧 `GetProperty` 的开销 ✓
        ///   ★ 找到后**立即做类型校验**（`PropertyType == typeof(PlayerBuffs)`）：
        ///     名字撞车拿到别的东西时，宁可当"没有增益"，也不能把 UI 拖崩 ✓
        /// </summary>
        public static PlayerBuffs BuffsOf(RunSession s)
        {
            if (s == null) return null;
            if (!_buffsProbeDone)
            {
                _buffsProbeDone = true;
                const System.Reflection.BindingFlags F =
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                // 名字按可能性排序；`Buffs` 是契约里写的那个
                string[] names = { "Buffs", "PlayerBuffs", "BuffTable" };
                System.Type t = typeof(RunSession);
                for (int i = 0; i < names.Length && _buffsMember == null; i++)
                {
                    System.Reflection.PropertyInfo pi = t.GetProperty(names[i], F);
                    if (pi != null && pi.PropertyType == typeof(PlayerBuffs)) { _buffsMember = pi; break; }
                    System.Reflection.FieldInfo fi = t.GetField(names[i], F);
                    if (fi != null && fi.FieldType == typeof(PlayerBuffs)) { _buffsMember = fi; break; }
                }
            }
            if (_buffsMember == null) return null;
            try
            {
                System.Reflection.PropertyInfo prop = _buffsMember as System.Reflection.PropertyInfo;
                if (prop != null) return prop.GetValue(s, null) as PlayerBuffs;
                System.Reflection.FieldInfo field = _buffsMember as System.Reflection.FieldInfo;
                return field != null ? field.GetValue(s) as PlayerBuffs : null;
            }
            catch
            {
                return null;      // 半初始化 / 反射被拒 ⇒ 当作"没有增益"，绝不把 HUD 拖崩
            }
        }

        /// <summary>自检 / 探针用：忘掉"找过一次"的结论（另一路刚接线时重新探一次）。</summary>
        public static void ResetBuffsProbe()
        {
            _buffsProbeDone = false;
            _buffsMember = null;
        }

        /// <summary>下标 → 掩码纹理（复用 `ItemMaskTex`；非法下标 / 缺图 ⇒ null）。</summary>
        private static Texture2D BuffIconTex(int maskIdx)
        {
            if (maskIdx < 0) return null;
            if (!_buffIconKeyCache.TryGetValue(maskIdx, out string key))
            {
                key = TableDatabase.ItemIconMaskKeyAt(maskIdx);
                _buffIconKeyCache[maskIdx] = key;      // null 也存：缺图不必每帧再查
            }
            return key == null ? null : ItemMaskTex(key);
        }

        /// <summary>
        /// 峰值保持：给"剩余 / 时长"这条比例找分母。
        ///
        /// ⚠️ `BuffInstance` 的契约里**没有** `Duration`（只有 `Remain`）⇒ 分母只能自己认：
        ///   取"这条增益在本局里见过的最大 `Remain`"：刷新时它回到满值 ⇒ 时长条重新涨满；
        ///   之后线性掉到 0 ⇒ 读得出"还剩几成" ✓
        ///   为什么不另记一份时长表：那会与 `PlayerBuffs` 的时长成为**两份真相** ✗
        /// 增益消失时把峰值一起丢掉，否则同名增益下次"一出生就是半格" ✗
        /// </summary>
        private void SyncBuffPeak(System.Collections.Generic.IReadOnlyList<BuffInstance> list)
        {
            _buffSeen.Clear();
            for (int i = 0; i < list.Count; i++)
                if (list[i].Id != null) _buffSeen.Add(list[i].Id);

            for (int i = 0; i < _buffSeen.Count; i++)
            {
                string id = _buffSeen[i];
                float remain = 0f;
                for (int k = 0; k < list.Count; k++)
                    if (string.Equals(list[k].Id, id, System.StringComparison.Ordinal)) { remain = list[k].Remain; break; }
                if (!_buffPeak.TryGetValue(id, out float peak) || remain > peak) _buffPeak[id] = remain;
            }

            if (_buffPeak.Count != _buffSeen.Count)
            {
                _buffPeakKeys.Clear();
                foreach (System.Collections.Generic.KeyValuePair<string, float> kv in _buffPeak) _buffPeakKeys.Add(kv.Key);
                for (int i = 0; i < _buffPeakKeys.Count; i++)
                    if (!_buffSeen.Contains(_buffPeakKeys[i])) _buffPeak.Remove(_buffPeakKeys[i]);
            }
        }

        /// <summary>增益区（**只读**：不改 `PlayerBuffs` 的任何状态）。</summary>
        private void DrawBuffs(RunSession s)
        {
            BuffChipRects.Clear();
            PlayerBuffs buffs = BuffsOf(s);
            if (buffs == null) return;                       // ★ 另一路还没接线 ⇒ 本区不存在
            System.Collections.Generic.IReadOnlyList<BuffInstance> list = buffs.Active;
            if (list == null || list.Count == 0)
            {
                if (_buffPeak.Count > 0) _buffPeak.Clear();
                return;                                      // ★ 没有增益 ⇒ 一格都不画
            }

            SyncBuffPeak(list);

            // 排布：一行最多 8 格；超出 ⇒ 留第 8 格写 `+N`
            int total = list.Count;
            bool overflow = total > BuffRowMax;
            int shown = overflow ? BuffRowMax - 1 : total;

            // 鼠标：正常走 `Event.current`；探针钩子只换坐标，不改判定
            Event ev = Event.current;
            Vector2 realMouse = ev != null ? ev.mousePosition : new Vector2(-99999f, -99999f);

            for (int i = 0; i < shown; i++)
            {
                Rect r = BuffChipAt(i);
                BuffChipRects.Add(r);
                BuffInstance b = list[i];
                float peak = 0f;
                if (b.Id != null) _buffPeak.TryGetValue(b.Id, out peak);
                DrawBuffChip(r, b, peak);

                Vector2 mouse = ForceBuffTipIndex == i ? r.center : realMouse;
                if (r.Contains(mouse))
                {
                    LastBuffTip[0] = string.IsNullOrEmpty(b.Label) ? b.Id : b.Label;
                    LastBuffTip[1] = "×" + Mathf.Max(1, b.Stacks);
                    LastBuffTip[2] = $"剩余 {Mathf.Max(0f, b.Remain):0.0}s";
                    RequestTip(mouse, LastBuffTip[0], LastBuffTip[1], LastBuffTip[2]);
                }
            }

            if (overflow)
            {
                Rect r = BuffChipAt(shown);
                BuffChipRects.Add(r);
                int rest = total - shown;
                DrawBuffOverflowChip(r, rest);

                Vector2 mouse = ForceBuffTipIndex == shown ? r.center : realMouse;
                if (r.Contains(mouse)) RequestTip(mouse, $"还有 {rest} 条增益");
            }
        }

        /// <summary>一格：暗底板 + 图标 + 下缘时长条 + 右上角 `×N`。</summary>
        private void DrawBuffChip(Rect r, BuffInstance b, float peak)
        {
            Color prev = GUI.color;

            // ① 暗底板 —— 图标是绿/橙的位掩码，压在草地上对比度不够
            //    （与 `DrawTip` 的浮框同一个"说明层"底色，不新造一种色）✓
            GUI.color = new Color(0.06f, 0.05f, 0.04f, 0.78f);
            GUI.DrawTexture(r, White);
            GUI.color = prev;

            // ② 图标（内缩 2px；掩码纹理是 Point 采样 ⇒ 放到 18×18 是方块，不糊）
            Rect ir = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 5f);
            Texture2D icon = BuffIconTex(b.IconMaskIdx);
            if (icon != null)
            {
                GUI.DrawTexture(ir, icon, ScaleMode.StretchToFill, true);
            }
            else
            {
                // `IconMaskIdx = −1`（这条增益没指定图标）或掩码缺失 ⇒ 暖色块兜底
                GUI.color = new Color(0.96f, 0.78f, 0.36f, 0.92f);
                GUI.DrawTexture(new Rect(ir.x + 3f, ir.y + 3f, ir.width - 6f, ir.height - 6f), White);
                GUI.color = prev;
            }

            // ③ 剩余时长细条（下缘；宽度 ∝ Remain / 峰值）
            float pct = peak > 0.001f ? Mathf.Clamp01(b.Remain / peak) : 0f;
            Rect track = new Rect(r.x + 2f, r.yMax - 4f, r.width - 4f, 2.5f);
            GUI.color = new Color(1f, 0.95f, 0.78f, 0.20f);        // 底槽
            GUI.DrawTexture(track, White);
            GUI.color = HudStrokeCurrent;                          // 填充 = 已有时长条那档绿
            GUI.DrawTexture(new Rect(track.x, track.y, track.width * pct, track.height), White);
            GUI.color = prev;

            // ④ 投影 + 外暗内亮两道描边（与 `BareFrame` 同一条规则，格子才有"厚度"）
            BareFrame(r, HudStrokeDim, 1.2f);

            // ⑤ 层数角标 `×N`（右上，四向描边）
            //   ⚠️ 框顶从 `r.y` 起、字号 11（不是 12）：22px 的格子里 `×60` 要放得下，
            //      而且**不许溢出上边框**（实测 12px + −3 偏移会让角标压到描边上）✓
            HudLabel(new Rect(r.x + 1f, r.y + 0.5f, r.width - 2f, 12f),
                "×" + Mathf.Max(1, b.Stacks), HudTinyRight);
        }

        /// <summary>溢出的那一格：`+N`（告诉玩家"还有 N 条没画出来"，不折行）。</summary>
        private void DrawBuffOverflowChip(Rect r, int rest)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.05f, 0.04f, 0.78f);
            GUI.DrawTexture(r, White);
            GUI.color = prev;
            BareFrame(r, HudStrokeDim, 1.2f);
            HudLabel(new Rect(r.x, r.y + 3f, r.width, 16f), "+" + rest, HudCenter);
        }

        // ------------------------------------------------------------ 左侧：按键说明（Tab 开关）
        private void DrawKeyHelp(RunSession s)
        {
            float w = 300f;
            // v66：起点从 136 下移到 142 —— 状态栏面板现在收到 122 高（底边 132），
            // 旧值 136 与它只差 4px、视觉上是叠着的。
            float y = 142f;

            if (!KeyHelpVisible)
            {
                // 隐藏后仍保留 Tab 键的描述
                // ★ v179：这块也去掉了底板（原来是 `Panel` + 深色 `Txt`）★
                //   否则"按 Tab 收起"之后会剩一个**孤零零的米色空盒子**挂在草地上 ✗
                //   而"收起"这个动作的语义是"这块没了"⇒ 只剩一行描边文字才自洽 ✓
                HudLabel(new Rect(20, y + 3, w - 20, 24), "显示按键说明（Tab）", HudBody);
                return;
            }

            // v16：战斗操作提示——只留战斗相关，格式统一为「功能（按键）」，删除 Q/R 与重复的 Tab 描述
            string[] lines =
            {
                "移动（WASD）",
                "攻击 / 与地块互动（鼠标左键）",
                "切换武器（鼠标滚轮）",
                "锄头：收获成熟植物",
                "水壶：激光攻击 + 给土地补水",
                "肥料：投掷炸弹 + 炸到即催熟",
                "显示 / 隐藏本说明（Tab）",
                "菜单（Esc）"
            };
            // ⚠️ 原来这里还有一个 `h = 34 + 8×22 = 210`，**现在删掉了** ✗
            //   因为底板一撤，`h` 就没有任何接收者（只会换来一条 CS0219 "赋值未使用" ✗）
            //   而它记的那件事**仍然成立、也仍然重要**：
            //     起点 142 + 高 210 ⇒ 底边 **352**，避开了下方种子袋面板（起点 364）✓
            //   ⇒ 把它留在注释里，而不是留在代码里当死变量 ✓
            //   （这也正是本工程"删的是调用、留的是理由"的做法 ✓）

            // ══ ★ v179：**去掉底板与边框，只留文字** ★ ══
            //
            // ── 改了什么 ✗（不是"调淡一点"，是**整块底不画了**）──
            //   改前：`Panel(new Rect(10, y, w, h))` —— `UiSkin.PanelStyle` 的九宫格贴图，
            //         即用户截图里那块**米色圆角底板 + 边框** ✓
            //         文字用**深色** `_bold` / `_small`（它们是给奶油底配的 ✗）
            //   改后：**没有 `Panel` 调用** ⇒ 底与边框都不存在 ✓
            //         文字改走 `HudLabel`（四向近黑描边 + 亮色本体）⇒ 压在草地/夜色上都读得清 ✓
            //
            // ── 为什么必须**同时**换文字样式 ✗ ★（只删底板会直接看不见）★ ──
            //   `_small` / `_bold` 的正文色是**深棕**（本工程"亮底用深字"那一档 ✓）
            //   底一撤，深棕字直接压在草地上 ⇒ 对比度不足、几乎不可读 ✗
            //   ⇒ 这与 v177 删武器框底板时踩过的是**同一个坑**（那里也是"字色跟着底走"✓）
            //   所以：`HudBody` / `HudBold`（亮字 + 描边）**是这次改动的一部分**✗ 不是顺手改的 ✓
            //
            // ── 为什么**不动**任何坐标 ✗ ★（这一条是刻意的）★ ──
            //   `y = 142`、行距 22、`x = 20` 全都**保持原值** ✓
            //   ⇒ 改前改后的文字**逐行落在同一像素上** ⇒ before/after 两张图可以直接叠着比 ✓
            //   （`x = 20` 正好与上方状态栏的「金币…」那一行**左对齐** ✓
            //     它原来是 10(板) + 10(内缩) = 20，现在板没了，20 就是文字自己的左边 ✓ 不变 ✓）
            //
            // ── 用户那句"它是**文字列表**、不是条" ✗ ★（防止做成一格一格）★ ──
            //   所以这里是**一个 for 循环逐行画**，每行一个 `HudLabel` ✓
            //   没有 `BareFrame`、没有 `BarBare`、没有逐行背景 ⇒ 是列表，不是格子 ✓
            //   ⚠️ 没有逐行背景、也没有"一轮到下一行填一格"的框
            //      ⇒ 是列表，不是格子 ✓
            HudLabel(new Rect(20, y + 4, w - 20, 26), "战斗操作（Tab 隐藏）", HudBold);
            for (int i = 0; i < lines.Length; i++)
                HudLabel(new Rect(20, y + 30 + i * 22, w - 24, 22), lines[i], HudBody);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  ★ v172：武器 / 插件 / 植物 —— 三个界面共享的【一套读数 + 一套画法】★
        //
        //  ★ 用户要求（原话）★
        //    "放大武器区域，扩大至三大框，显示名称、伤害、范围、攻击速度、爆炸范围、暴击、
        //      暴击伤害、击退，以及插件槽"
        //    "同步更新 UI 框；★ 预留更大的区域给角色武器，商店、菜单都是 ★"
        //
        //  ★ 为什么先抽一层，而不是三个界面各画一遍 ★
        //  本工程已经吃过"同一个量在两处有两种说法"的亏（如稀有度配色 bug：颜色对、名字表两份 ✓）。
        //  武器一旦在【主界面 / 商店 / 菜单】各写一份，必然出现"商店说射程、主界面说范围" ✓
        //  ⇒ 所以：武器格只有一处（`DrawWeaponCell`）、插件槽只有一处（`DrawPluginSlotCell`）✓
        //    三个界面只是**给不同的矩形**（这与 v84 让商店行与种子卡共用一套排布是同一条路子 ✓）
        //
        // ★ v180：`WeaponReadout` / `WeaponReadoutOf`（一整层"伤害算式 + 8 项数值"的读数）**已删除** ✗ ★
        //   为什么删 ✗ 不是"嫌它长"：
        //   ① 它**没有任何调用者** —— v179 把 168px 详情框改成 40px 小格时就已经不用它了
        //      （当时的注释写着"留着，将来要加回去只需接一行调用"）；
        //   ② 它读的是**插件聚合层** `WeaponModifiers` 的**字段名**
        //      （`ApplyToAttack` / `RangePct` / `AttackSpeedPct` / `KnockbackFlat` / `ExplodeRadiusPct`）——
        //      而另一条任务正在**重做插件模型**，把这组字段整批换掉（剑气 / 灼烧 / 双击 / 多柱 / 子爆 …）✓
        //   ⇒ 留着它就是**在我这个文件里留一条随时会断的编译依赖** ✗
        //     而它服务的那块 UI（详情框）**已经不存在了** ⇒ 删掉它不减少任何玩家看得到的东西 ✓
        //   ⚠️ 代价（写明）：将来若要恢复"武器详情"，那 8 项读数需要**按新的插件模型重写**
        //      （不是恢复这段代码）—— 新模型里没有"伤害 +N%"这种全局修正，而是"多一道剑气 / 多一层灼烧" ✓
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 三把武器，按玩家切换的顺序（与 `PlayerActor.WeaponHoe/Can/Fertilizer` 对齐 ✓）。
        ///
        /// ⚠️ v180：删掉 `WeaponReadoutOf` 时**误删了这一行数组**（它原来紧挨着那个 struct）。
        ///    编译立刻报 `AllWeapons` 不存在 ⇒ 已按原样补回，**内容一个字节未改** ✓
        ///    （记在这里，是因为"删一段代码顺手带走过一行常量"是很容易再犯一次的事 ✓）
        /// </summary>
        private static readonly WeaponSlot[] AllWeapons =
        {
            WeaponSlot.Hoe, WeaponSlot.Can, WeaponSlot.Fertilizer
        };

        /// <summary>v48：武器类型按需求显示为三个词——锄头（近战）/ 水壶（远程）/ 肥料（爆炸）。</summary>
        private static string WeaponTypeText(string damageType, string kind)
        {
            if (string.Equals(damageType, "Ailment", System.StringComparison.OrdinalIgnoreCase)) return "爆炸";
            return string.Equals(kind, "Ranged", System.StringComparison.OrdinalIgnoreCase) ? "远程" : "近战";
        }

        // ------------------------------------------------------------ v172：武器框（三处共用）
        /// <summary>暗底 / 亮底两种**换行**样式（`WrapSmall` 是墨棕字，只适合亮底 ✗）。</summary>
        private GUIStyle _onDarkWrap;
        private GUIStyle OnDarkWrap
        {
            get
            {
                if (_onDarkWrap == null)
                {
                    _onDarkWrap = new GUIStyle(_small) { wordWrap = true };
                    _onDarkWrap.padding = new RectOffset(0, 0, 0, 0);
                    _onDarkWrap.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                }
                return _onDarkWrap;
            }
        }

        private GUIStyle _tiny;
        private GUIStyle Tiny
        {
            get
            {
                if (_tiny == null)
                {
                    _tiny = new GUIStyle(_small) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
                    _tiny.normal.textColor = new Color(0.10f, 0.08f, 0.06f);
                }
                return _tiny;
            }
        }

        /// <summary>插槽里的短名（2 个字）：整个名字塞不进 56px 的槽 ✓ 全名在下面那行摘要里 ✓</summary>
        private static string ShortPluginName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= 2 ? name : name.Substring(0, 2);
        }

        /// <summary>
        /// ★ 6 个插件槽 ★ —— ★ 空槽也要画 ★
        ///
        /// 为什么空槽必须画：玩家买插件时唯一要判断的事就是"这把武器还剩几个位置"（用户标准原文）✓
        /// 只画已装的 ⇒ 他得自己数"装了 3 个 ⇒ 6-3=3"✓ 多一步心算就是一次误购的机会 ✗
        ///
        /// 槽的着色 = 那件插件的**品质色**（与商店、持有物同一套 `QualityColor` ✓ 不另起一套配色 ✓）
        /// 叠层的（"磨刀石 ×3"）在槽里写 `×3` —— ★ 它仍然只占 1 格 ★
        /// （`SlotsUsedOn` 数的是**实例数**不是层数，`Goods.cs:430-442` 有明确说明 ✓）
        /// </summary>
        private void DrawPluginSlots(RunSession s, WeaponSlot slot, Rect area)
        {
            const float gap = 3f;
            const int n = PlayerGoods.MaxPluginsPerWeapon;
            float cw = (area.width - gap * (n - 1)) / n;
            float ch = area.height;
            int i = 0;
            foreach (PluginInstance pi in s.Goods.PluginsOn(slot))
            {
                if (i >= n) break;      // 上限 6 ⇒ 超出不会发生，防御而已
                DrawPluginSlotCell(pi, new Rect(area.x + i * (cw + gap), area.y, cw, ch), true);
                i++;
            }
            for (; i < n; i++)
                DrawPluginSlotCell(null, new Rect(area.x + i * (cw + gap), area.y, cw, ch), true);
        }

        /// <summary>
        /// ★ v180：**一个插件槽**（空槽 / 有插件，同一个出口）★
        ///
        /// ── 用户第 ② 条的三句话，逐句落到这里 ──
        ///   ① "**空槽 = 黑色**（纯黑或近黑，不要灰色）"
        ///      ⇒ 空槽填 `PluginSlotEmpty`（0.015/0.015/0.02）—— 不是 `SlotBg` 那种 0.27 的灰
        ///        ⚠️ 改前这里是 `StrokeRect` 只描边不填底（v177 的"去底板"），读出来是"没有格子"✗
        ///        现在要求"预留 6 个插槽格" ⇒ **格子必须在**，所以重新填底，用近黑 ✓
        ///   ② "**有插件 = 填该插件的图标**，并按插件品质着色（边框或底色取品质色）"
        ///      ⇒ 底 = 品质色压暗一档（让图标/短名压在上面仍可读）；
        ///         边 = **品质色本体**（1.5px）—— 二者同时是品质色，缩图/色弱都还能从边上认 ✓
        ///         图标 = `PluginIconOf` 取 `Art/UI/icon_plugin_&lt;id&gt;`；
        ///         **工程里没有这套图** ⇒ 退回"2 字短名"（与 v172 `DrawPluginSlots` 同一套做法 ✓）
        ///   ③ "**鼠标悬停** = 显示该插件详情（名字 + 效果一句话 + 可叠层数）"
        ///      ⇒ `RequestTip`：名字 + `PluginEffectText` + 叠层规则/当前层数 ✓
        ///
        /// ── 为什么"叠层的仍然只占 1 格" ✗ ★
        ///   `SlotsUsedOn` 数的是**实例数**不是层数（`Goods.cs` 有明确说明）✓
        ///   ⇒ 只能装 3 层的"磨刀石 ×3"在槽里仍然是一个格 + `×3` ✓
        ///     把它画成 3 格会让玩家以为"还能装 3 件" ✗ 那是界面在撒谎 ✓
        /// </summary>
        private void DrawPluginSlotCell(PluginInstance pi, Rect cell, bool allowTip)
        {
            Color prev = GUI.color;
            if (pi == null || pi.Asset == null)
            {
                // ① 空槽 = 近黑实心 + 一道细边（细边只为"数得出 6 个"，不是灰底）
                GUI.color = PluginSlotEmpty;
                GUI.DrawTexture(cell, White);
                GUI.color = prev;
                StrokeRect(cell, PluginSlotEdge, 1f);
                if (allowTip && cell.Contains(Event.current.mousePosition))
                    RequestTip(Event.current.mousePosition, "空插件槽", "商店里买插件 ⇒ 装到这把武器上");
                return;
            }

            Color qc = PluginQualityColor(pi.Asset.quality);
            // ② 底色 = 品质色压暗（保留品质信息，又不至于挡住宿图标/文字）
            GUI.color = new Color(qc.r * 0.45f, qc.g * 0.45f, qc.b * 0.45f, 1f);
            GUI.DrawTexture(cell, White);
            GUI.color = prev;
            StrokeRect(cell, qc, 1.5f);

            Texture2D icon = PluginIconOf(pi.Asset);
            Rect inner = new Rect(cell.x + 2f, cell.y + 2f, cell.width - 4f, cell.height - 4f);
            if (icon != null)
                GUI.DrawTexture(inner, icon, ScaleMode.ScaleToFit, true);
            else
                // ⚠️ 这里必须是**亮字**（`HudTiny`=12px 近白）：底色是"品质色压暗"，
                //    `Tiny`（深棕）压上去等于黑字压在深底上 —— 看得见格、看不见字 ✗
                Txt(new Rect(cell.x, cell.y + (cell.height - 18f) * 0.5f, cell.width, 18f),
                    ShortPluginName(pi.Asset.displayName), HudTiny);

            if (pi.Stacks > 1)
                Txt(new Rect(cell.x, cell.yMax - 18f, cell.width - 3f, 18f), "×" + pi.Stacks, HudTiny);

            if (allowTip && cell.Contains(Event.current.mousePosition))
            {
                int tier = QualityTierOf(pi.Asset.quality);
                RequestTip(Event.current.mousePosition,
                    pi.Asset.displayName + "　" + (tier >= 0 ? s_rarityName(tier) : "品质未知"),
                    "效果：" + PluginEffectText(pi.Asset),
                    (pi.Stacks > 1 ? $"已叠 {pi.Stacks} 层　" : "") + "可叠层数：" + StackRuleText(pi.Asset));
            }
        }

        /// <summary>
        /// "已装插件"摘要：★ 名字 · 改哪一环 ★ —— 通道名取 `WeaponSystem.ChannelOf`（唯一说法 ✓）
        /// 用户标准："还要能看到这件插件装上会改哪一环"✓ 这是**买之前**的判断依据 ✓
        /// </summary>
        private static string InstalledPluginSummary(RunSession s, WeaponSlot slot)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (PluginInstance pi in s.Goods.PluginsOn(slot))
            {
                if (pi.Asset == null) continue;
                if (n > 0) sb.Append("　");
                sb.Append(pi.Asset.displayName);
                if (pi.Stacks > 1) sb.Append('×').Append(pi.Stacks);
                sb.Append('·').Append(WeaponSystem.ChannelOf(pi.Asset.effect));
                n++;
                if (n >= PlayerGoods.MaxPluginsPerWeapon) break;
            }
            if (n == 0) return "插件槽全空（商店买插件 ⇒ 装到这把武器上）";
            int free = s.Goods.FreeSlotsOn(slot);
            return free > 0 ? sb.Append($"　（还可装 {free} 件）").ToString() : sb.Append("　（已满）").ToString();
        }

        /// <summary>
        /// ★ v179：一个武器格的**高度**（三处共用同一个值）★
        ///
        /// 改前是 `WeaponBoxH = 168f`（8 项数值 + 6 个插件槽 + 摘要行，共 156px 内容）✗
        /// 改后只放 **图标 26 + 名字 22** ⇒ 40px 足够（上下各 7px 内边距 ✓）
        /// 为什么不做"高/矮"两个变体：三个界面看到的是 **同一个格子** 才是一套语言 ✓
        /// </summary>
        private const float WeaponCellH = 40f;

        // ────────────────────────────────────────────────────────────────────────
        //  ★ v180：6 个插件槽（用户第 ② 条）★
        //
        //  高度从哪来（不是拍的 ✓）：
        //    槽 **26px** —— 里面要放"品质色底 + 2 字短名（12px 虚拟号）/ 图标"，
        //                   26 是 12px 字 + 上下各 7px 内边距（与武器格 40 = 22 字 + 上下各 9 同一条路子）
        //    上下留 4px —— 与武器格之间分开 4px（不然两排会读成一排 8 个东西）
        //  ⇒ 这一段额外吃掉 `PluginSlotH + PluginSlotGapY = 30px`
        //  ⇒ 商店的武器区总高 = `WeaponSlotSectionH = 32 + 40 + 4 + 26 + 12 = 114`（改前 84）
        //    ⚠️ 多出来的 30px 从**角色属性**那块里出（它自己带滚动条，矮也能用 ✓）
        //
        //  ★ v181：`WeaponSectionH`（= 84，v179 那个"只有标题 + 一格"的高度）**已删除** ✗ ★
        //    为什么删：v180 起**菜单也要 6 个插件槽**（用户这次原话：
        //      "菜单页每把武器要完整显示：武器名 ＋ 伤害/范围/攻速等关键数值 ＋ 6 个插件槽"）
        //    ⇒ 它唯一的两个调用方（菜单 84 / 商店 114）现在都带槽
        //    ⇒ 再留一个"无槽 84"的常量就是**没人读的死常量**（本文件的规矩：删的是调用、留的是理由）✓
        //    ⚠️ 菜单现在的总高是 `WeaponFullSectionH` = 134（标题 + 格 + **数值行** + 6 槽）✓
        // ────────────────────────────────────────────────────────────────────────
        private const float PluginSlotH = 26f;
        private const float PluginSlotGapY = 4f;
        private const float WeaponSlotSectionH = 32f + WeaponCellH + PluginSlotGapY + PluginSlotH + 12f;

        // ────────────────────────────────────────────────────────────────────────
        //  ★ v184：`WeaponFullSectionH`（= 134，v181 那条"底部横栏"的总高）**已删除** ✗ ★
        //
        //  为什么删：菜单的武器栏在 v184 从"页面底部一条 994×134 的横栏"改成了
        //  **左整列 610×690、每把一张卡**（见 `DrawMenu` / `DrawMenuWeaponCard`）⇒
        //  这个常量**没有任何读取方**了 ✓（本文件的规矩：删的是调用、留的是理由）
        //
        //  ⚠️ `WeaponStatsH` / `WeaponStatsGapY` / `WeaponStatLine` **保留** ✓
        //     它们仍被 `DrawWeaponRow(withStats: true)` 那条分支引用 ——
        //     而 `withStats` 现在只剩商店/战斗 HUD 两处传 `false`（菜单改走 `DrawMenuWeaponCard`）
        //     ⇒ 分支暂时没有 `true` 调用方，但它是 `DrawWeaponSection` 的**公开形状**，
        //       且 v184 的武器卡与它算的是**同一组数**（`WeaponStatLine` 那三个式子
        //       在 `DrawMenuWeaponCard` 里逐项复现 ⇒ 两处不会打架）✓
        // ────────────────────────────────────────────────────────────────────────
        /// <summary>武器数值行的高度（12px 字 + 上下各 3px）。</summary>
        private const float WeaponStatsH = 18f;
        /// <summary>武器格子与数值行之间的间距（同一格子的两行，贴紧）。</summary>
        private const float WeaponStatsGapY = 2f;

        // ★ v179：`DrawWeaponBox`（168px 的详情框）**已删除** ★
        //
        // 它画的是：名字 + 类型 / 插件 N/6 / `伤害 (3+1)×100% = 4` / `范围 攻速 击退`
        //           / `爆炸 暴击 暴伤` / 6 个插件槽 / `插件槽全空（…）`
        // 用户第 ② 条要求"**不做详情**，只显示图标 + 名字"⇒ 这一整块**不再是任何一种画法** ✓
        // 替代它的是下面的 `DrawWeaponCell`（40px 一格）✓
        //
        // ⚠️ 为什么把 `WeaponReadoutOf` / `DrawPluginSlots` / `InstalledPluginSummary` **留着** ✗ ★
        //   ① 它们不是"被注释掉的旧实现"，而是**一套完整的、有断言的读数层** ✓
        //      `WeaponReadoutOf` 里那几条"故意不统一"的细节（水壶不吃 `ToolMult`、
        //      击退系数 0.5/0.2/1.0）是**照抄运行时而得的**，重写一遍极容易写错 ✓
        //   ② 用户说的是"**这块 UI** 不做详情"，不是"这把武器没有细节" ✗
        //      将来若要加回去，只需要在 `DrawWeaponCell` 下面接一行调用 ✓
        //   ③ 编译器对"未被调用的 private 方法"**不告警** ⇒ 它不会污染构建 ✓
        //   ★ 代价（写明）：目前它们是**没有调用者的代码** ✓ 这一条我会在交付里报出来 ✓


        /// <summary>
        /// ★ v179：**一个紧凑武器格**（图标 + 名字 + 当前箭头）★
        ///
        /// ── 用户的改动要求（原话）──
        ///   "改成小格子 —— 每把武器**只显示图标 + 名字**，当前武器用**箭头**指明；
        ///    不要数值行、不要插件槽。三把武器排成一列或一行均可，但要**紧凑**。"
        ///   "（不做详情）"
        ///
        /// ── 改前是什么 ✗（不是"删几行"，是**整块详情作废**）──
        ///   改前每把武器一个 `WeaponBoxH = 168px` 的框，里面 8 项 + 6 个插件槽 + 摘要行：
        ///     名字 + 类型 / 插件 0/6 / `伤害 (3+1)×100% = 4` / `范围 3.2m 攻速 0.70s/次 击退 0.30`
        ///     / `爆炸 — 暴击 0% 暴伤 200%` / 6 个空格子 / `插件槽全空（商店买插件 → …）`
        ///   ⇒ 三把 = 168×3 + 间距 = **522px**，而一个格子真正要表达的东西只有"是哪一把"✓
        ///   改后：一格 **40px**，三格一行共 **132px**（含间距）⇒ 高度压到约 1/4 ✓
        ///
        /// ── "当前是哪一把"这个信息**没有减弱**（与 v177 同一套四通道 ✓）──
        ///   ① 描边色：亮绿 vs 暖白 ② 描边粗细 2.5px vs 1.2px
        ///   ③ 外侧投影只在当前那把上 ④ ★ 名字前的 ▶ 箭头（用户点名要的那个 ✓）
        ///   ⇒ 任一失效（色盲/缩图/压缩）都还剩三个 ✓
        ///
        /// ── 图标为什么"取不到就不留空位" ✗ ──
        ///   `Icon(...)` 在 `UiSkin` 里没有这张图时返回 **false** ✓
        ///   若仍按固定 42px 缩进写字，缺图时左边就是一块**空白** ⇒ 看着像排歪了 ✗
        ///   ⇒ 只有真的画上了图才右移文字 ✓（与"缺图不画空框"是同一条 ✓）
        /// </summary>
        private void DrawWeaponCell(RunSession s, WeaponSlot slot, Rect cell, bool current)
        {
            BareFrame(cell, current ? HudStrokeCurrent : HudStrokeDim,
                current ? 2.5f : 1.2f, shadow: current);

            const float iconSize = 26f;
            string iconId = slot == WeaponSlot.Hoe ? "hoe"
                          : slot == WeaponSlot.Can ? "can"
                          : "fertilizer";
            float textX = cell.x + 10f;
            float iconY = cell.y + (cell.height - iconSize) * 0.5f;
            if (Icon(iconId, new Rect(cell.x + 8f, iconY, iconSize, iconSize)))
                textX = cell.x + 8f + iconSize + 8f;

            // ★ v180：悬停 = 这把武器的插件总览（用户第 ③ 条"详情"）★
            //   菜单里**不画**那 6 个槽（③ 要"保留小格子形态"）⇒ 玩家要判断"这把还能装几件、
            //   现在装了什么"就只能从这里看。内容与 ② 是同一套三要素：名字 / 效果 / 层数 ✓
            if (cell.Contains(Event.current.mousePosition))
                RequestTip(Event.current.mousePosition, WeaponTipLines(s, slot));

            float textW = cell.xMax - 6f - textX;
            if (textW < 24f) return;                 // 太窄就不画（宁可不画，也不画半个字 ✗）
            GUIStyle style = current ? HudBold : HudBody;
            // `　`（全角空格）让三格的名字**左边界对齐**：当前那把有 ▶、其余留同宽空位 ✓
            // （若只给当前那把加 ▶，三格的字会各自错开 ⇒ 反而不像一排 ✓）
            HudLabel(new Rect(textX, cell.y + (cell.height - 22f) * 0.5f, textW, 22f),
                (current ? "▶ " : "　 ") + WeaponSlotName(slot), style);
        }

        /// <summary>
        /// ★ v180：一把武器的悬停详情（标题 + 每件插件一行 + 槽位占用）★
        ///
        /// 三要素与插件槽的悬停框**同源**：名字 / 效果一句话 / 层数 ✓
        /// 槽位占用写 "N/6" —— 玩家买插件前唯一要算的就是这个数（用户 v172 的标准）
        /// </summary>
        private static string[] WeaponTipLines(RunSession s, WeaponSlot slot)
        {
            var list = new System.Collections.Generic.List<string>();
            int used = s.Goods.SlotsUsedOn(slot);
            list.Add($"{WeaponSlotName(slot)}　插件 {used}/{PlayerGoods.MaxPluginsPerWeapon} 件");
            int n = 0;
            foreach (PluginInstance pi in s.Goods.PluginsOn(slot))
            {
                if (pi.Asset == null) continue;
                list.Add(pi.Asset.displayName
                         + (pi.Stacks > 1 ? "×" + pi.Stacks : "")
                         + "　" + PluginEffectText(pi.Asset)
                         + "　（上限 " + pi.Asset.maxStack + " 层）");
                n++;
                if (n >= PlayerGoods.MaxPluginsPerWeapon) break;
            }
            if (n == 0) list.Add("插件槽全空（商店买插件 ⇒ 装到这把武器上）");
            return list.ToArray();
        }

        /// <summary>
        /// ★ v179：三把武器排成**一行**（`DrawWeaponSection` / `DrawWeaponPanel` 共用）★
        ///
        /// 为什么是"一行"而不是"一列"：用户说"排成一列或一行均可，但要紧凑"✓
        /// 两处容器都是**竖长条**（商店右栏 675×660、战斗 HUD 380×—）✗
        /// 排成一列要吃掉 3×40 + 间距 = 132px 的**高度**，而这一行的宽度本来就有富余 ✓
        /// ⇒ 一行只吃 40px 高度，把纵向空间让给下面的东西（商店：角色属性；HUD：场景本身）✓
        ///
        /// ★ v180：`withSlots` —— 商店要那 6 个插件槽（用户第 ② 条），菜单不要（第 ③ 条只要小格子）★
        ///   为什么不做成两块函数：三把武器的**格子本身**必须一模一样 ✓
        ///   差别只有"格子下面那一排槽画不画"⇒ 用一个开关表达，判据只有一处 ✓
        ///
        /// ★ v181：`withStats` —— **菜单要"伤害 / 范围 / 攻速"这三个关键数值**（用户原话：
        ///   "菜单页每把武器要完整显示：武器名 ＋ 伤害/范围/攻速等关键数值 ＋ 6 个插件槽"）★
        ///   为什么又是一个开关而不是"菜单另写一份" ✗
        ///   与 `withSlots` 同一条理由：**格子必须一模一样**，多出来的只有"格子下面那一行字"✓
        ///   开关值：菜单 `true`（它宽，够放）/ 商店 `false` / 战斗 HUD `false` ✓
        ///   ⚠️ 商店与战斗 HUD **一个像素都没动**（它们的调用点仍传 `false`）——
        ///      那两处的宽度放不下一行"伤害 4 · 范围 3.2m · 攻速 0.70s"（HUD 每格只有 ~169px）✗
        ///      而它们**本来就不是**这次要求的对象（用户只说了菜单页）✓
        /// </summary>
        private void DrawWeaponRow(RunSession s, Rect area, bool withStats, bool withSlots)
        {
            const float gap = 6f;
            float cw = (area.width - gap * (AllWeapons.Length - 1)) / AllWeapons.Length;
            for (int i = 0; i < AllWeapons.Length; i++)
            {
                Rect cell = new Rect(area.x + i * (cw + gap), area.y, cw, area.height);
                DrawWeaponCell(s, AllWeapons[i], cell, IsCurrentWeapon(s, AllWeapons[i]));
                float y = cell.yMax;
                if (withStats)
                {
                    // 数值行**紧贴格子下沿**：它与"武器名"是同一个格子的两行，不是另一个框
                    Txt(new Rect(cell.x, y + WeaponStatsGapY, cell.width, WeaponStatsH),
                        WeaponStatLine(s, AllWeapons[i]), Tiny);
                    y += WeaponStatsGapY + WeaponStatsH;
                }
                if (!withSlots) continue;
                // 6 个槽挂在**这一把格子**下面 ⇒ 每把武器各自 6 格（用户第 ② 条第 1 句）
                // ★ v181：菜单这条栏里三格是**挨着**的（每格 ~320px，格间只有 6px）⇒
                //   三个槽带会首尾相接，18 个近黑格**连成一条**，读不出"每把 6 个" ✗
                //   ⇒ 菜单（`withStats: true` 的那一处）把槽带左右各内缩 4px ⇒ 组间 14px ✓
                //   ⚠️ 商店（`withStats: false`）内缩 0 ⇒ **像素与 v180 完全一致** ✓
                float inset = withStats ? 4f : 0f;
                DrawPluginSlots(s, AllWeapons[i],
                    new Rect(cell.x + inset, y + PluginSlotGapY, cell.width - inset * 2f, PluginSlotH));
            }
        }

        /// <summary>
        /// ★ v181：一把武器的三个**实算**关键数值 —— 伤害 / 范围 / 攻速 ★
        ///
        /// ── 每个数都必须与运行时**同一条路径**（否则界面就在撒谎，本文件的老规矩）──
        ///   伤害：`DamageResolver.AttackFormula(基础 + 对应伤害属性) × 伤害倍率`
        ///         —— 与 `DamagePreviewLine` 逐项相同；**三把武器同一个式子**
        ///         （v195 删掉爆炸倍率后，肥料不再有第二层乘区）✓
        ///       三种武器各吃**自己的**伤害属性：锄头=近战 / 水壶=远程 / 肥料=爆炸（v162 A3）✓
        ///   范围：`tool.range × StatId.AtkRange`
        ///         —— 与 `RunSession.WeaponRange`（`RunSession.cs:3549-3559`）同一个式子 ✓
        ///   攻速：`tool.cooldown ÷ AttackSpeed`（= **每次攻击的秒数**，不是"每秒几次"）
        ///         —— 与 `RunSession.SwingHoe:3738` / `WeaponCds[..]` 同一个式子 ✓
        ///   ⚠️ 攻速**不吃插件**（阶段 3 起插件不改玩家属性）⇒ 这里也只看 `AttackSpeed` ✓
        ///
        /// ── 摆法为什么是"一行三段 + `·` 分隔" ✗ ──
        ///   菜单的武器栏是**一条 994px 的宽栏**，每个格子 ~320px ⇒ 一行放得下 ✓
        ///   （实测：12px 字号下这一行约 200px，格内可用 ~300px ✓）
        /// </summary>
        private static string WeaponStatLine(RunSession s, WeaponSlot slot)
        {
            if (s == null || s.Progress == null || TableDatabase.Tuning == null) return "";
            TuningDef t = TableDatabase.Tuning;
            StatBlock st = s.Progress.Stats;
            ToolDef tool = slot == WeaponSlot.Can ? t.tools.can
                         : slot == WeaponSlot.Fertilizer ? t.tools.fertilizer
                         : t.tools.hoe;
            if (tool == null) return "";
            float dmgStat = slot == WeaponSlot.Can ? st.Get(StatId.RangedDamage)
                          : slot == WeaponSlot.Fertilizer ? st.Get(StatId.BlastDamage)
                          : st.Get(StatId.MeleeDamage);
            // ★ v186：与武器卡、与运行时**同一个函数**（品质那一层乘法已删除）★
            float dmg = DamageResolver.WeaponDamage(tool.baseValue, dmgStat,
                st.Get(StatId.AtkMult), false, st.Get(StatId.CritDmg), 0f);
            // ★ v195：爆炸倍率已删除 ⇒ 肥料与另外两把武器**同一个式子**，这里不再有第二层乘区 ★
            float range = tool.range * Mathf.Max(0.1f, st.Get(StatId.AtkRange));
            float cd = tool.cooldown / Mathf.Max(0.05f, st.Get(StatId.AtkSpeed));
            return $"伤害 {dmg:0.#} · 范围 {range:0.0}m · 攻速 {cd:0.00}s";
        }

        /// <summary>当前武器（三处高亮同一把 ⇒ 判据只写一次 ✓）。</summary>
        private static bool IsCurrentWeapon(RunSession s, WeaponSlot slot)
            => s.World != null && s.World.Player != null && (int)slot == s.World.Player.WeaponIndex;

        /// <summary>
        /// ★ v172：一块"武器"区（商店 / 菜单共用）★
        ///
        /// ★ v179：**三块详情框 → 一行三格** ★（原因见 `DrawWeaponCell`）
        ///   面板高度随之从"撑满整栏"变成**按内容定高** ✓
        ///   `GUI.BeginScrollView` **仍然没有加回来** ✓ —— v178 去掉它是因为
        ///   "裁剪区里的 `Txt` 只能走老路径（模糊）"✗ 而这一块**就是**最常看的文字之一 ✓
        ///
        /// ★ v180：`withSlots` ⇒ 高度取 `WeaponSlotSectionH`（商店 = 114）★
        ///   ⚠️ 高度由**常量**算出来 ⇒ 面板高度**只由数据决定**、不随内容或悬停变化 ✓
        ///
        /// ★ v181：`withStats` ⇒ 菜单要"伤害 / 范围 / 攻速"那一行 ★
        ///   高度常量：商店 `WeaponSlotSectionH` = 114／菜单 `WeaponFullSectionH` = 134
        ///   （v179 那个"无槽 84"的 `WeaponSectionH` 已删 —— 现在**没有**任何调用方是不带槽的 ✓）
        ///   战斗 HUD 不用这个函数（它自己算 `WeaponHudH` = 64）✓
        ///   标题后缀只在 `withSlots` 时加（"（每把 6 个插件槽）"）—— 菜单现在也是 `withSlots: true`
        ///   ⇒ 那句话对菜单同样成立，不再重复加一遍 ✓
        /// </summary>
        private void DrawWeaponSection(RunSession s, Rect area, string title, bool withStats, bool withSlots)
        {
            Panel(area);
            // ★ v180：标题补上"每把 6 槽"这句 —— 标题是**唯一**告诉玩家"这排槽是什么"的地方 ✓
            Txt(new Rect(area.x + 12, area.y + 3, area.width - 24, 26),
                withSlots ? title + $"（每把 {PlayerGoods.MaxPluginsPerWeapon} 个插件槽）" : title,
                _bold);
            DrawWeaponRow(s, new Rect(area.x + 10f, area.y + 32f, area.width - 20f, WeaponCellH), withStats, withSlots);
        }

        // ══════════════════════════════════════════════════════════════════════════
        //  ★ v180（用户第 ① 条）：战斗 HUD 武器区 —— **右侧 → 底部居中，横向一行** ★
        //
        //  ── 改前 ✗（不是"挪一下坐标"，是**换了它所在的那条带子**）──
        //    `Rect(_virtualW-380-14, 150, 380, 94)` ⇒ 贴在**画面右侧**、纵向从 150 开始。
        //    玩家看的是屏幕**中间偏下**（角色在正下方），武器在右上是"余光之外"✓
        //    而"我现在拿的是哪一把"是**每秒都要扫一眼**的信息 ⇒ 它必须在视线里 ✓
        //
        //  ── 新位置的三条硬约束（先量，再定）★
        //    ① 底部已有「水壶储水 / 肥料」两条进度条：`DrawAmmoBar` 的矩形是
        //       `(_virtualW/2-260, _virtualH-78-12, 520, 78)` ⇒ 占 y ∈ [_virtualH-90, _virtualH-12] ✓
        //       ⇒ 武器区**整块**必须落在 `_virtualH-90` **以上**，与它至少留 `WeaponStackGap` = 8px ✓
        //    ② `DrawPlotPrompt`（耕地提示，战斗里几乎常驻）改前占 y ∈ [_virtualH-166, _virtualH-120]
        //       —— 那正好是"充能条上方"，和武器区**必然撞** ✗
        //       ⇒ 所以把它整体上移一个武器区的高度（见 `DrawPlotPrompt` 的 `PlotPromptLiftY`）✓
        //         上移后占 y ∈ [_virtualH-272, _virtualH-226] ⇒ 与武器区顶 `_virtualH-162` 留 64px ✓
        //    ③ 宽度取 **520** —— 与充能条**同宽**。两者都居中 ⇒ 读起来是**一摞**底部 HUD，
        //       而不是"一条 520 的条 + 一条 380 的条各自居中"那种错位 ✓
        //
        //  ── 高度从 94 收到 64 的账（用户说"画法不变"，所以省的是**非格子**的部分）★
        //    改前 = 28（标题行）+ 40（三格）+ 26（状态行）= 94
        //    改后 = 24（标题行）+ 40（三格）            = 64
        //    · 标题行 28 → 24：它只有一行 `HudBold`（22 高的字框），28 是旧的内边距 ✓
        //    · **状态行并入标题行**（左=「武器（三把）」，右=状态）⇒ 省掉整行 26px ✓
        //    · 状态行**默认那句"按住鼠标左键：攻击 / 互动"不再画** ✗ ★（这条是本文件自己的规矩）★
        //      文件头 v5 写着"**所有键位说明只出现在左侧说明栏**，HUD 其它地方不提示按键"——
        //      而那句话就是一条键位说明，左侧说明栏里已经有「攻击 / 与地块互动（鼠标左键）」✓
        //      ⇒ 只在**真状态**（耕作中 / 切换前摇 / 锁定目标）出现的帧才画右侧那半句 ✓
        //      这样既没丢信息（键位还在左栏），又不让默认帧多一行 ✓
        //
        //  ── 三个格子**一个像素的画法都没改** ✗ ★
        //    仍是 `DrawWeaponRow(..., withSlots: false)` ⇒ 同一张 `BareFrame`、同一个 26px 图标、
        //    同一个「▶ + 名字」、同一套当前/非当前描边粗细 ✓（只是它们所在的矩形挪了地方 ✓）
        // ══════════════════════════════════════════════════════════════════════════

        /// <summary>充能条的底边距（`DrawAmmoBar` 与武器区**共用**，改一处两边一起动）。</summary>
        private const float AmmoBarBottomGap = 12f;
        /// <summary>充能条的**高度**（同上，共用一个常量 ⇒ 不可能对不上）。</summary>
        private const float AmmoBarH = 78f;
        /// <summary>武器区与充能条之间的空隙（8px：既是留白，也保证永不重叠）。</summary>
        private const float WeaponStackGap = 8f;
        /// <summary>武器区标题行高（右侧状态与它同一行）。</summary>
        private const float WeaponTitleH = 24f;
        /// <summary>战斗 HUD 武器区整块高度 = 24 + 40 = 64。</summary>
        private const float WeaponHudH = WeaponTitleH + WeaponCellH;
        /// <summary>武器区顶边（虚拟坐标）。充能条顶 = `_virtualH - 90` ⇒ 武器区顶 = 它再往上 8 + 64。</summary>
        private static float WeaponHudTop(float virtualH)
            => virtualH - AmmoBarBottomGap - AmmoBarH - WeaponStackGap - WeaponHudH;
        /// <summary>耕地提示整体上移的量 = 武器区高度 + 那条空隙（它原本就贴着充能条上方）。</summary>
        private const float PlotPromptLiftY = WeaponHudH + WeaponStackGap;

        private void DrawWeaponPanel(RunSession s)
        {
            PlayerActor p = s.World.Player;
            float w = 520f;                       // 与充能条同宽 ⇒ 一摞
            float x = _virtualW * 0.5f - w * 0.5f;  // 居中
            float y = WeaponHudTop(_virtualH);

            // v177：**去掉底板**（需求"切换武器不使用背景"）⇒ 标题与状态行都必须是描边文字，
            //       否则深棕色字会直接消失在草地上 ✓
            HudLabel(new Rect(x, y, 200f, WeaponTitleH), "武器（三把）", HudBold);

            // ★ v180：状态只在**真状态**出现的帧才画（默认那句是键位说明，左栏已有）
            //   ⇒ 默认帧右侧是空的；不画比画一句重复的键位提示更对 ✓
            string state = s.FarmingThisFrame
                ? "耕作中（无法攻击）"
                : p.SwitchWindup > 0f
                    ? $"切换前摇 {p.SwitchWindup:0.0}s（无法攻击）"
                    : (p.IsLocked ? "锁定目标中" : null);
            if (!string.IsNullOrEmpty(state))
                HudLabel(new Rect(x + w - 260f, y, 260f, WeaponTitleH), state, HudRight);

            DrawWeaponRow(s, new Rect(x, y + WeaponTitleH, w, WeaponCellH), withStats: false, withSlots: false);

            // ★ v180：探针（只读）—— 武器区 / 充能条 / 耕地提示三条的**实际 y 区间**同帧记一行。
            //   判据：三者两两不相交，且武器区整块在充能条**之上** ✓（这就是"不与进度条打架"的数值版）
            if (LayoutProbeOn)
            {
                float ammoTop = _virtualH - AmmoBarBottomGap - AmmoBarH;
                float plotY = _virtualH - 196f - PlotPromptLiftY;
                ProbeLine($"#hud v={_virtualW:0.###}x{_virtualH:0.###}"
                    + $" weapon=[{y:0.###},{y + WeaponHudH:0.###}] w={w:0.###} h={WeaponHudH:0.###}"
                    + $" ammo=[{ammoTop:0.###},{_virtualH - AmmoBarBottomGap:0.###}]"
                    + $" plot=[{plotY - 26f:0.###},{plotY + 76f:0.###}]"
                    + $" gapWeaponToAmmo={(ammoTop - (y + WeaponHudH)):0.###}"
                    + $" gapPlotToWeapon={(y - (plotY + 76f)):0.###}"
                    + $" lift={PlotPromptLiftY:0.###}");
            }
        }

        // ------------------------------------------------------------ v172：植物区（6 个中格子）
        // ★ v196：这里原来还有 `HasMechanicOf` / `PlantMechanicStage` / `PlantEffectLine` 三个函数 ★
        //   它们唯一的用途是产出植物信息卡的**第 7 项「特殊功能」**
        //   （有机制就写「机制〔阶段〕+ 说明」，没有机制写「平平无奇」）✗
        //   而用户把植物属性收敛成 **6 项**（名称 / 品质 / 等级 / 成熟时间 / 金币 / 经验）⇒
        //   第 7 项整条下线 ⇒ 三个**只做展示**的函数一起删（留着就是没人调的死代码）✓
        //   ⚠️ `PlantMechanics`（`Farm/` 里的行为解释器）**一个字没动**：删掉的是"怎么说"，
        //      不是"怎么算" —— 机制仍然照样影响生长 / 收获 / 缺水 ✓

        /// <summary>
        /// ★ v196：植物信息的**唯一生产者** ★ —— 主界面 6 格 / 菜单中栏 3×2 / 商店种子卡 /
        /// 商店商品详情 / 开局选种，五处全部走这一个函数 ✓
        ///
        /// 用户标准（原话）："植物类删除无关舍弃的描述，现在只有名称、品质、等级、成熟时间、
        /// 金币提供量、经验值提供量这些属性（耗水、价格为内定不展示），删除关于攻击、hp，攻击间隔" ✓
        ///
        /// ⇒ **6 项，一项不多一项不少**：
        ///   ① 名称　　　—— 卡片抬头（`DrawPlantCell` 的名称行 / `SeedCardPanel` 左列 /
        ///                   商店行的 `o.Label()`）✓ 五处本来就有，这里**不重复画** ✗
        ///   ② 品质　　　—— 同一处抬头（`s_rarityName(...)`）＋ 左侧那条 5px 品质色条 ✓
        ///   ③ 等级　　　—— 本函数第 1 行「等级　N 级」
        ///   ④ 成熟时间　—— 第 2 行「成熟　Ns」
        ///   ⑤ 金币提供量 —— 第 3 行「金币　G」
        ///   ⑥ 经验值提供量 第 4 行「经验　E」
        ///
        /// ★ 为什么名称与品质不在这个函数里 ★
        ///   五处的抬头几何**各不相同**（主界面格子是"图标 + 两行字"、种子卡是左列纵向堆叠、
        ///   商店行是左图右文）⇒ 硬塞进同一份字符串数组只会让三处一起变丑 ✓
        ///   而"只剩 6 项"这件事由 `SelfCheck` 的 `v196` 那条断言钉住
        ///   （行数 + 每行标签 + 一整张禁区词表）✓
        ///
        /// ★ 行数为什么仍是 4 行 ★
        ///   改前「Lv…／成熟…／金币·经验／特殊功能」4 行，改后「等级／成熟／金币／经验」4 行 ⇒
        ///   `DrawInfoList` 每行 22px 的高度账、四处卡片与格子的高度**一个像素都不用重算** ✓
        ///
        /// ★ v196 删掉的两样 ★
        ///   ① 第 7 项「特殊功能」那一行（见本文件上方那段说明）✗
        ///   ② 「收获 ×1.35」这个后缀 —— 它不在用户点名的 6 项里，
        ///      而它当初写上去只是为了解释"金币 5 为什么不是 4"，现在金币那一行自己就是对的口径 ✓
        ///
        /// ⚠️ **删展示 ≠ 删数据** ✗：`SeedDef` 上的 `BaseHp` / `BaseShield` / `AttackInterval` /
        ///   `BaseValue` / `Range` / `MoveSpeed` / `BaseCritRate` / `BaseCritDmg` **一个字节没动**，
        ///   仍被 `Combat/**` 与玩家三把武器读取 ✓
        ///   本函数**不展示**它们，不代表它们不存在 ✓
        /// ⚠️ `WaterMax` / `WaterDrain` / `Price`：用户明确"**内定不展示**" ⇒ 本函数一个字都不印 ✓
        ///   （商店行左边那个「价格 N」是**购买标价**，属于商品而不是植物属性 ⇒ 保留 ✓）
        /// ⚠️ `SeedDef.Desc`（那句"根圆叶小，腌着最好吃"）：**不再有任何展示点** ✓
        ///   它原来只挂在第 7 项里（v173 随 `SeedCardLines` 一起删的），本轮连数据一起清 ✓
        ///
        /// ⑤⑥ 金币 / 经验与 `Actors.HarvestPlot` 的发放公式同一式：
        /// `max(1, round(goldYield × 等级倍率 × 机制倍率))` 与 `max(1, round(expYield × 等级倍率))` ✓
        /// ★ v179：基数取 `SeedEconomy.CellGold/CellExp`（= 表里的 `goldYield` / `expYield`）★
        ///   —— 收益与售价脱钩后，**卡片必须跟着数据源走** ✓
        /// ★ v174（修复 B）★：那个"等级倍率"必须取**收获表**（`LevelHarvestMult`
        /// = 1.00/1.35/1.70），不是**卖出返还表**（`LevelYieldMult` = 1.00/1.50/2.00）✓
        /// 两张表都在 `RunSession.SeedSlot` 上，名字只差一个词 ⇒ 正是"看起来对、数不对"的温床 ✗
        ///
        /// ⚠️ 卡片**算不到**的两项（改前改后一样，不是这次引入的 ✗）：
        ///   ① 道具规则 `harvest_gold_pct`（`RunSession.SyncItemRules` 运行时才写入）
        ///   ② 焦渴绽放（`ThirstBloom`）的加成（`HarvestGoldMult` 传的 plot 是 `null`
        ///      ⇒ 取"水分充足"那档，卡片不承诺"把水用掉一半再收"多出来的那 60% ✓）
        /// ⇒ 卡片承诺的是"这一级、按机制、常态下给多少"，实得只会**不低于**它 ✓
        /// ★ 植物自 v165 起不参战 ⇒ 这里**一个战斗属性都没有** ✓
        /// </summary>
        /// <remarks>
        /// **`internal` 而不是 `private`**：`SelfCheck` 的 v196 断言必须读到**真的会被画出去的那份
        /// 字符串**（而不是照着这里再抄一份文案）—— 抄一份的那天就是两张表开始分家的那天 ✓
        /// </remarks>
        /// <param name="wide">
        /// ★ v196：**宽版 / 窄版两种排法，内容一模一样** ★
        ///
        /// 窄版（4 行，一列一项）：主界面植物格（文字区 171px）、种子卡（207px）、开局选种 ✓
        /// 宽版（2 行，每行两项）：**商店商品行**的详情区（483px 宽，而高度只有 68px）✓
        ///
        /// ★ 为什么必须有宽版 ✗ 不是"顺便优化一下"★
        ///   商店那一格的详情区是 `DrawInfoList`：**每行固定 22px**，
        ///   而 `DrawShop` 的行高 = `rowAreaH / 4` ⇒ 视口只有 68px。
        ///   4 行 = 88px + 8 ⇒ **超了 20px** ⇒ 界面上会多出一条滚动条，
        ///   第 4 行「经验」要滚一下才看得见 ✗（v196 第一版的抓图就是这样，取证见交付）
        ///   两项并一行 ⇒ 2 行 = 44 + 8 = 52 ≤ 68 ⇒ **一屏全见、没有滚动条、也不留洞** ✓
        ///   ⚠️ 不能改 `DrawInfoList` 的 `lineH = 22` —— 那是**全工程所有信息区共用的**，
        ///      为一个格子改它，等于同时改掉种子卡 / 属性栏 / 菜单每一块的行距 ✗
        /// </param>
        internal static string[] PlantCellInfoLines(SeedSlot slot, bool wide = false, bool withFunc = false)
        {
            var list = new System.Collections.Generic.List<string>();
            SeedDef d = slot.Def;
            if (d == null) { list.Add("（空格）"); return list.ToArray(); }

            // ④ 成熟时间 —— ★ 吃**等级速度倍率**（v174 修复 C，算法未动）★
            //
            // 权威（**同一式，不自己再算一遍** ✓）：
            //   `FarmGrid.BeginPlant` = `def.GrowTime / SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, Level)`
            //   自检也钉着这条：`GrowthTotal == radish.GrowTime / 1.30`（Lv3）✓
            // ⇒ 1 级 ×1.00（16s 不变）／2 级 ÷1.15 ≈ 13.9s／3 级 ÷1.30 ≈ 12.3s ✓
            float growSeconds = d.GrowTime
                                / SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, slot.Level);

            // ⑤ 金币提供量 / ⑥ 经验值提供量 —— 与 `Actors.HarvestPlot` 发放同一式
            //   （v174 修复 B：取**收获**表）
            //
            // ⚠️ 为什么**不是**去改 `Actors.HarvestPlot`：那里是权威 ✓
            //   错的是卡片这一次读取 —— 收获发放多少由策划定，UI 跟着它走 ✓
            float lvHarvest = slot.LevelHarvestMult;
            float gold = Mathf.Max(1f, Mathf.RoundToInt(SeedEconomy.CellGold(d) * lvHarvest
                                                        * PlantMechanics.HarvestGoldMult(d, null)));
            float exp = Mathf.Max(1f, Mathf.RoundToInt(SeedEconomy.CellExp(d) * lvHarvest));

            if (wide)
            {
                // ★★ v254：商店商品行要**看得见植物功能**（用户原话："购买栏看不到植物功能描述了"）★★
                //   改前这里只有「等级/成熟」「金币/经验」两行数字 —— 当年是怕 68px 视口装不下，
                //   于是把 `PlantFuncLine` 显式排除掉了 ⇒ 商店里只剩数字，看不出这株是干什么的 ✗
                //   现在改成两行：① 功能行（定位 + 当场数字）② 等级行
                //   ⇒ 行数不变（仍是 2 行），但"功能"第一眼就在最上面 ✓
                //   ⚠️ 功能行里的数字用的是**同一批** `growSeconds/gold/exp`
                //      ⇒ 不会和别处对不上（本工程不许有第二份真相）✓
                if (withFunc)
                {
                    list.Add(PlantFuncLine(d, growSeconds, gold, exp));
                    list.Add($"等级　{slot.Level} 级");
                    return list.ToArray();
                }
                // 宽版：2 行 × 2 项（其它调用方保持原样）
                list.Add($"等级　{slot.Level} 级　·　成熟　{growSeconds:0.#}s");
                list.Add($"金币　{gold:0}　·　经验　{exp:0}");
                return list.ToArray();
            }

            // 窄版：4 行 × 1 项（植物格 / 种子卡 / 开局选种）
            list.Add($"等级　{slot.Level} 级");          // ③ 等级 —— 用户口径：写「1 级 / 2 级 / 3 级」✓
            // ★ v208：紧跟在等级后面，写清"这个等级到底带来了什么" ★
            // 用户原话："检擦植物等级是否有提高收获，在植物信息栏著名等级带来的影响"
            // 数字**不另写一份**：收获倍率直接取 `slot.LevelHarvestMult`（同一个属性喂给 `Actors.HarvestPlot`），
            // 提速百分比由 `SeedEconomy.LevelSpeedMult` 反算 `1 − 1/倍率` ⇒ 卡片与结算不可能各说各话 ✓
            // 1 级显示「—」而不是隐藏：行数恒定，卡片不会一行多一行少（与「特殊功能　无」同一条规矩）✓
            list.Add(LevelEffectLine(slot));
            list.Add($"成熟　{growSeconds:0.#}s");
            list.Add($"金币　{gold:0}");
            list.Add($"经验　{exp:0}");
            // ★ v213：第 6 行 ——「一句话功能说明 ＋ 当场数字」（见 `PlantFuncLine`）★
            // 放在数值块之后：上面 3 行是"这一株给多少"，这一行是"它适合干什么" ✓
            // 三个数**不另算**，就是把上面那三个变量传进去 ⇒ 卡片不可能自相矛盾 ✓
            list.Add(PlantFuncLine(d, growSeconds, gold, exp));
            return list.ToArray();
        }

        /// <summary>
        /// ★ v208：**植物等级影响的唯一实现**（卡片那一行）★
        ///
        /// 格式：`等级影响　收获 ×1.35、成熟 −13%`
        ///   · 收获倍率 = `slot.LevelHarvestMult`（1.00 / 1.35 / 1.70，与结算**同一个属性**）
        ///   · 成熟 −13% = `1 − 1/LevelSpeedMult`（Lv1 0% / Lv2 13.0% / Lv3 23.1%）
        ///   两者都**从数据源算**，不在这里写死 0.35 / 0.15 —— 改表的时候卡片自动跟着变 ✓
        ///
        /// 为什么 1 级写「—」而不是不显示：行数恒定 ⇒ 卡片高度不跳；
        /// 而且"1 级没有任何加成"本身就是一条要读到的信息 ✓
        /// </summary>
        internal static string LevelEffectLine(SeedSlot slot)
        {
            if (slot == null) return "等级影响　—";
            float speed = SeedEconomy.LevelSpeedMult(SeedSlot.GrowSpeedPerLevel, slot.Level);
            if (slot.Level <= SeedSlot.StartingLevel || speed <= 0.0001f) return "等级影响　—";
            float faster = (1f - 1f / speed) * 100f;
            return $"等级影响　收获 ×{slot.LevelHarvestMult:0.00}、成熟 −{faster:0}%";
        }

        /// <summary>
        /// ★ v213：**植物卡「一句话功能说明」—— 唯一实现** ★
        ///
        /// 用户原话："每株的卡片必须有一句话功能说明 ＋ 成熟秒 ＋ 金币/经验产出 ＋ 等级影响"。
        ///
        /// ── 为什么要有这一行 ✗ 不是风味文字 ★
        ///   无机制的 22 株在卡面上**看不出区别**：都是「等级 / 成熟 / 金币 / 经验」四行数字，
        ///   玩家得自己拿 `8 秒 ÷ 3 金` 与 `56 秒 ÷ 24 金` 去心算才知道哪株是快钱、哪株是长线 ✗
        ///   这一行把**定位**直接说出来，而数字仍当场算 ✓
        ///
        /// ── 三个数为什么不在这里另算 ★
        ///   成熟秒 = 传进来的 `growSeconds`（与「成熟」行是同一个变量）
        ///   每格金币/经验 = 传进来的 `gold` / `exp`（与「金币」「经验」两行**同一个变量**）
        ///   ⇒ 复用实参、不重新调 `SeedEconomy` ⇒ 卡片上不可能出现"定位写 3 金、下面写 4 金" ✓
        ///   （本工程守这条已经很多年：UI 自己再算一遍 = 第二份真相）
        ///
        /// ── 文案表里为什么一个数字都没有 ★
        ///   `PlantRoleText` 只放**定位词**。数字一旦写进文案，下次调表（`goldYield` 改一格）它就开始撒谎 ✗
        ///   ⇒ 表里 30 条全是"金型株 / 验型株 / 耐旱株"这类**不含数**的说法 ✓
        ///
        /// ⚠️ 行数：窄版由 5 行变 **6 行**（`SelfCheck` 的 v196 期望值同步改 5 → 6，断言本身没删）✓
        /// ⚠️ 宽版（商店商品行，视口 68px）**不加**这一行 —— 那一格两行都勉强 ✓
        /// </summary>
        internal static string PlantFuncLine(SeedDef d, float growSeconds, float gold, float exp)
        {
            if (d == null) return "功能　—";
            return $"功能　{PlantRoleText(d.Id)}：{growSeconds:0.#} 秒一收，每格 {gold:0} 金 {exp:0} 验";
        }

        /// <summary>
        /// ★ v213：**每株植物的定位词 —— 唯一实现**（键 = `seeds.json` 的 `id`，30 株）★
        ///
        /// ⚠️ 这张表**故意不含任何数字**：数字由 `PlantFuncLine` 当场从数据算出来 ✓
        /// ⚠️ 新增种子必须在这里补一行；漏了会显示「（未填写定位）」，自检 v198 那条会点名 ✗
        /// （`SelfCheck` 读的就是本函数 —— 它验的是**真的会被画出去的那个词**，不是另抄一份）✓
        ///
        /// 定位的唯一依据是**数据事实**（`goldYield` / `expYield` / `growTime` 在同档里的位置）
        /// 与**机制事实**（`mechanic` 那一栏），与风味无关 ⇒ 30 条逐条可对照 `seeds.json` 复核 ✓
        /// </summary>
        /// <summary>★ v213：定位词**缺失时**的占位文案（`SelfCheck` 也读它判"忘了补一行"）★</summary>
        internal const string PlantRoleUnfilled = "（未填写定位）";

        internal static string PlantRoleText(string id)
        {
            switch (id)
            {
                // ── D 档（普通）──────────────────────────────────────────────
                case "radish_guard": return "前期开荒的快钱株";       // 8s、3 金 1 验
                case "pea_shooter": return "前期拉等级的验型株";       // 8s、1 金 3 验
                case "toxic_mushroom": return "D 档里量最大的一株";    // 16s、6 金 3 验
                case "frost_ivy": return "全田最快的小额周转株";       // 5s、1 金 1 验
                // ── C 档（一般）──────────────────────────────────────────────
                case "thorn_pumpkin": return "攒本金用的金型株";       // 27s、14 金 4 验
                case "stone_fist": return "补等级用的验型株";          // 27s、6 金 12 验
                case "chili_mage": return "周期最长、单次最厚的一株";  // 54s、26 金 14 验
                case "starvine": return "短周期快进快出的周转株";      // 13.5s、6 金 2 验
                // ── B 档（稀有）──────────────────────────────────────────────
                case "moontree": return "落一笔大现金的金型株";        // 48s、31 金 8 验
                case "moss_shield": return "金验最均衡的长周期株";     // 56s、24 金 23 验
                case "healing_clover": return "偏经验的验型株";        // 40s、14 金 18 验
                // ── A 档（史诗）──────────────────────────────────────────────
                case "life_spring": return "高价作物里的金型株";       // 42.5s、31 金 8 验
                case "char_flower": return "周期最短的均衡株";         // 34s、14 金 17 验
                case "holy_tree_guard": return "单格总收益最高的一株"; // 50s、31 金 17 验（和 48）
                case "shelter_ivy": return "站在它的田地上闪避更高的株";   // StandingDodge：判据是位置（在哪块地），不是"动不动"
                case "lucky_monarch": return "种着时收获更容易翻倍的株"; // FarmLuck
                case "dusk_berry": return "收完还你一口水的株";        // DewDrop：每次收获返还 12 点蓄水
                case "tide_gourd": return "漏水减半的省水株";          // WaterSave：×0.5
                case "hollow_vine": return "水分不到一半时收钱更多的株"; // ThirstBloom：水分 <50% 时金币 ×1.60（v219 起不再要求"缺水见底"）
                case "dune_cactus": return "缺水也照样长的耐旱株";     // ThirstTolerant：40% 速度
                // ── S 档（传奇）──────────────────────────────────────────────
                case "plague_mother": return "金币最厚的顶级金型株";   // 37s、32 金 8 验
                case "ironroot_colossus": return "经验最厚的顶级验型株"; // 44s、22 金 27 验
                case "warblade_bloom": return "收割后让你挥得更狠的株"; // HarvestAtkMult
                case "gale_vine": return "收割后让你挥得更快的株";     // HarvestAtkSpeed
                case "sunflower_crown": return "会给你送来大晴天的株"; // SunnyBloom
                case "rainlily": return "会替你把水壶灌满的株";        // RainCloud
                case "harvest_bell": return "单格金币有加成的丰收株";  // RichHarvest：×1.30
                case "star_ladder": return "收一格连带其它熟格一起收的株"; // ChainHarvest
                case "elder_oak": return "站得越久长得越快的株";       // ElderGrowth
                case "quick_reed": return "成熟速度再提速的株";        // Haste：×1.25
                // ── v252：6 株无机制新植物（各自的定位词按"它在档内的位置"写）──
                case "mint_sprout": return "D 档里周期偏长的稳收株";    // 22s、9 金 4 验
                case "cinder_pepper": return "D 档单次收益最厚的一株";  // 30s、13 金 6 验
                case "glass_melon": return "C 档偏金的长周期株";        // 60s、31 金 14 验
                case "frost_reed": return "C 档周期最长、验也最厚的株";  // 64s、34 金 15 验
                // ── v252 第三批：5 株无机制新植物 ──
                case "glow_moss": return "D 档里偏验的小额周转株";      // 16s、5 金 4 验
                case "salt_melon": return "C 档极端的金型株";           // 27s、4 金 14 验
                case "cloud_orchid": return "B 档偏验的长周期株";       // 40s、8 金 24 验
                case "mirror_bloom": return "A 档极端的金型株";         // 34s、24 金 7 验
                case "ember_reed": return "S 档少见的短周期均衡株";     // 34s、20 金 17 验
                // ── v284：12 株新植物（6 复用机制 + 6 新机制）──
                //   定位词只写"它在机制上的角色"，**不写数字**（数字由上面那行按表现算）。
                case "dew_jar_moss": return "让这一列漏水减半的省水株";   // WaterSave
                case "drizzle_bud": return "每次收获往回补水的株";       // DewDrop
                case "rush_fern": return "成熟速度更快的提速株";         // Haste
                case "plump_gourd": return "该格收获金币有加成的株";     // RichHarvest
                case "patch_lattice": return "田越多收钱越多的铺场株";   // PlotCountGold
                case "drought_lotus": return "缺水也照样长的耐旱株";     // ThirstTolerant
                case "brim_peony": return "水越满是给得越厚的株";        // FullWaterGold
                case "brave_sprout": return "自己越残血长得越快的株";    // LowHpGrow
                case "aged_bloom": return "同株待得越久越值钱的株";      // ElderGrowth
                case "chain_bloom": return "连着准时收会越收越厚的株";   // StreakHarvest
                case "nightdew_lily": return "夜里收获给得更多的株";     // NightGold
                case "hunter_bloom": return "击杀后短暂亢奋的株";        // KillRush
                default: return PlantRoleUnfilled;
            }
        }

        /// <summary>
        /// ★ v198：**植物卡的"特殊功能"行 —— 唯一实现** ★
        ///
        /// 用户原话："植物增加滑动条展示，**在底部显示拥有的特殊功能**"。
        ///
        /// ── 文字从哪来（**不另写一份文案**）──
        ///   `TableDatabase.MechShortDesc(def, level)` —— **卡片短版**（20 字左右）；
        ///   数值全部来自 `PlantMechanics` 的常量，并且按**这张卡的等级**算（1/2/3 级给的不一样）✓
        ///   为什么不直接用 `PlantMechanics.Describe`（长版）：它 50 多字，在 163px 宽的文字区里
        ///   折成 5 行、滚到底也只能看到半句的中间几截 ✗（理由与取证见 `MechShortDesc` 的注释）✓
        ///   为什么不在这个函数里自己拼一句话：拼的那天就是"卡片写 +3%、实际 +5%"的那天 ✓
        ///
        /// ── 没有机制的植物写什么 ──
        ///   用户给的是"显示「无」**或**干脆不显示"。选「无」：
        ///   16 株无机制植物与 6 株机制植物**行数一致**（都是 5 行）⇒ 卡片不会一行多一行少，
        ///   而"这株没有特殊功能"本身就是一条要读到的信息 ✓
        /// </summary>
        internal static string PlantSpecialFeatureLine(SeedSlot slot)
        {
            SeedDef d = slot != null ? slot.Def : null;
            if (d == null) return "";
            string text = TableDatabase.MechShortDesc(d, slot.Level);
            return string.IsNullOrEmpty(text) ? "特殊功能　无" : "特殊功能　" + text;
        }

        /// <summary>
        /// ★ v198：**植物卡要画的全部正文** = 4 行数值（`PlantCellInfoLines`）＋ **底部特殊功能** ★
        ///
        /// 为什么不把这一行塞进 `PlantCellInfoLines`：
        ///   v196 那条断言（`v196：植物信息卡只剩 6 项`）钉着"窄版恰好 4 行 / 宽版恰好 2 行，
        ///   且每行都以 等级/成熟/金币/经验 开头"—— 特殊功能行**不是**这四项之一，
        ///   而且它是长句（会折行）。塞进去等于把一条立着的断言改松 ✗
        ///   ⇒ 数值块**一个字不动**（断言继续按原口径跑 ✓），特殊功能行是本函数追加的第 5 行 ✓
        ///
        /// ⚠️ 宽版（商店商品行，视口 68px）**不加**这一行：那里两行都勉强，加第三行必顶出滚动条
        ///   而商店行的左列已经写着名字/类别/品质/价格 —— 特殊功能属于"种子卡 / 植物格"这一档信息 ✓
        /// </summary>
        internal static string[] PlantCardLines(SeedSlot slot)
        {
            string[] core = PlantCellInfoLines(slot);
            string special = PlantSpecialFeatureLine(slot);
            if (string.IsNullOrEmpty(special)) return core;
            var list = new System.Collections.Generic.List<string>(core.Length + 1);
            list.AddRange(core);
            list.Add(special);
            return list.ToArray();
        }

        /// <summary>
        /// ★★ v199：植物格正文行距 —— **改前是写死的 17px，那是这一格"滚到底还缺一行"的根因** ★★
        ///
        /// 用户原话："植物刚刚加入的滑动器上限不够，导致部分内容显示不完全"
        ///
        /// ── 取证（`Library/yowei-shots/v198-c-plant-card-bottom.png`，滚到底的那张）──
        ///   `_small` 是 **15 号**字，本文件其它所有换行正文（`DrawInfoList` 的 `lineH`、
        ///   `StatLine` 的行高）一律按 **22px** 排 ⇒ 17px 比字形本身的行高还小：
        ///   折行的第 2 行（例："特殊功能　收割狂热：攻击倍率 +3%/层" 的「层」）
        ///   被 `Txt` 的矩形底边切掉，而**内容总高也是按 17px 算的** ⇒
        ///   `contentH` 少算 ⇒ 滚动条 max 也跟着少 ⇒ **滚到最底仍然看到被裁的那一行** ✗
        ///   两件事同一个因：**行距口径与字形不一致** ✓
        ///
        /// ── 改法 ──
        ///   行距不再写死：取 `OnDarkWrap.lineHeight`（样式实测）与 22px 的**较大者**。
        ///   `Mathf.Max(22f, …)` 这一条是给"字体缺字形 / lineHeight 返回 0"兜底的 ✓
        ///   ⚠️ 量行数与画正文**必须用同一个 `lineH`** —— 量一次、存进 `hs[]`，画的时候只用 `hs[]` ✓
        /// </summary>
        private float PlantLineHeight
            => Mathf.Max(22f, OnDarkWrap.lineHeight + 2f);

        /// <summary>★ v199：只给探针读的"本帧实际行距"（改动前是 17，现在是这个值）。</summary>
        internal float PlantCellLineHeight => PlantLineHeight;

        /// <summary>★ v199：最近一帧每个植物格的布局读数（**只给探针读**；每帧覆盖，不会增长）。</summary>
        internal static readonly string[] PlantCellLayoutLog = new string[8];

        /// <summary>
        /// ★ v198：植物格正文 —— **装得下就直画，装不下就出竖向滚动条** ★
        ///
        /// ── 为什么不是无条件 `GUI.BeginScrollView` ──
        ///   `Txt` 在裁剪区里会走"兜底原路"（`_clipDepth &gt; 0`，见 `Txt` 上方那段说明）：
        ///   那里**不能**把矩阵换成 identity（否则裁剪框跟着缩成 1/1.35，实测会把字切掉）
        ///   ⇒ 裁剪区里的字是**被矩阵拉过一遍的**（ui.scale≠1 时略糊）。
        ///   `DrawInfoList` 已经接受这个代价，但植物格是**常驻界面**，
        ///   能不进裁剪区就不进 ⇒ 4 行（不折行）装得下时走"直画"（**清晰**）那条路 ✓
        ///
        /// ── 什么时候真的会滚 ──
        ///   5 行（4 数值 ＋ 特殊功能）里只要特殊功能那行折了行，就必然超出格高
        ///   （窄格 163px 下"特殊功能　收割狂热：…"约 700px ⇒ 5 行 ⇒ 内容 ≈ 119px &gt; 视口 73px）
        ///   ⇒ 机制植物的格子会有滚动条；无机制植物写「无」（一行）则多半不需要 ✓
        /// </summary>
        private void DrawPlantCellLines(Rect view, string[] lines, int index)
        {
            if (lines == null || lines.Length == 0 || view.height <= 8f || view.width <= 20f) return;

            // ① 先按"没有滚动条"量一遍
            // ★★ v199：**行高不再用"行数 × 常数"算，改用 `CalcHeight` 实测** ★★
            //   病因（取证：`v198-c-plant-card-bottom.png` 与 `v199-c-plant-zoom.png`）：
            //   旧版 `hs[k] = RowsFor(text, avail) * 17` —— `RowsFor` 是
            //   `ceil(整串虚拟宽 / 可用宽)`，而 `GUI.Label` 的折行是按**字形边界**断的
            //   （"特殊功能　收割狂热：…"实测折成 3 行，`ceil` 只算出 2 行）⇒
            //   内容总高少算一行 ⇒ 滚动条的 max 也少一行 ⇒ **滚到底最后一行仍被裁** ✓
            //   `GUI.Label` 在滚动区里**不会**被自己的 rect 裁（裁的是滚动区）⇒
            //   多出来的那一行只露出半截字形 ← 用户在画面上看到的就是这个 ✓
            //   `CalcHeight` 走的是 Unity 自己那套换行 ⇒ 量出来的就是**真正要占的高度** ✓
            float lineH = PlantLineHeight;                    // 单行下限（字体缺字形时兜底）
            float availNoBar = Mathf.Max(40f, view.width);
            var hs = new float[lines.Length];
            float total = 0f;
            for (int k = 0; k < lines.Length; k++)
            {
                RowsFor(lines[k], availNoBar, "植物格#" + k);   // 只为 HUD-AUDIT 记一条超宽，不参与高度
                hs[k] = Mathf.Max(lineH, OnDarkWrap.CalcHeight(new GUIContent(lines[k] ?? ""), availNoBar));
                total += hs[k];
            }
            if (total <= view.height + 0.5f)
            {
                float y = 0f;
                for (int k = 0; k < lines.Length; k++)
                {
                    Txt(new Rect(view.x, view.y + y, availNoBar, hs[k]), lines[k], OnDarkWrap);
                    y += hs[k];
                }
                return;
            }

            // ② 装不下 ⇒ 竖向滚动条（皮肤见 `SkinScrollTrack` / `SkinScrollThumb`）
            //    ⚠️ 宽度要减掉滚动条的宽度再量行数，否则折行数会与真实排布差一行
            //    ⚠️ 减 **18** 而不是 14（滚动条名义 `fixedWidth = 14`）——
            //      实测（`Library/yowei-shots/v198-b-plant-card-top.png` 放大 2.6 倍的抓图）：
            //      用 14 时**内容宽 = 视口宽**，Unity 仍会判"内容更宽"⇒ 每个植物格底部多出
            //      一条**横向滚动条**，它吃掉 14px 高度、把第 4 行「经验」裁掉半截 ✗
            //      留 4px 余量后横条消失、4 行完整 ✓（这是取证抓图定的，不是拍的）
            EnsureScrollStore(ref _plantCellScroll, index + 1);
            float avail = Mathf.Max(40f, view.width - 18f);
            float content = 4f;
            for (int k = 0; k < lines.Length; k++)
            {
                RowsFor(lines[k], avail, "植物格#" + k);
                hs[k] = Mathf.Max(lineH, OnDarkWrap.CalcHeight(new GUIContent(lines[k] ?? ""), avail));
                content += hs[k];
            }
            // ★ v199：`content` 已含上下各 4px 留白 ⇒ 滚到底时最后一行**整行字形**都在视口里 ✓
            float contentH = Mathf.Max(view.height, content + 4f);

            _clipDepth++;   // v178：裁剪区内文字走 `Txt` 的兜底分支
            Vector2 sc = GUI.BeginScrollView(view, new Vector2(0f, _plantCellScroll[index]),
                                             new Rect(0, 0, avail, contentH));
            _plantCellScroll[index] = sc.y;
            if (index >= 0 && index < PlantCellLayoutLog.Length)
                PlantCellLayoutLog[index] = $"#{index} 视口 {view.height:0.#}px／内容 {contentH:0.#}px"
                    + $"／滚动 max {contentH - view.height:0.#}px／当前 {sc.y:0.#}px"
                    + $"／末行底 {content - 4f:0.#}px";
            float yy = 0f;
            for (int k = 0; k < lines.Length; k++)
            {
                Txt(new Rect(0f, yy, avail, hs[k]), lines[k], OnDarkWrap);
                yy += hs[k];
            }
            _clipDepth--;
            GUI.EndScrollView();
        }

        /// <summary>
        /// ★ v198：**只给取证探针用** —— 把某个植物格的滚动量直接设到 `y`。
        ///
        /// 为什么需要它：用户要求的"底部显示特殊功能"在机制植物的格子里**在折线以下**
        /// （内容 119px &gt; 视口 73px）⇒ 默认截图只能看到前 4 行。
        /// 探针要把"滚到底之后长什么样"也拍下来当证据，而 IMGUI 的滚轮事件没法从脚本里注入 ⇒
        /// 直接写滚动量（`GUI.BeginScrollView` 下一帧会把它钳进 [0, max] 并存回去）✓
        /// ⚠️ 它**只改 UI 的滚动位置**，不进任何玩法路径、不影响任何数值 ✓
        /// </summary>
        public void DebugScrollPlantCell(int index, float y)
        {
            if (index < 0) return;
            EnsureScrollStore(ref _plantCellScroll, index + 1);
            _plantCellScroll[index] = y;
        }

        /// <summary>
        /// ★ v198：**只给取证探针用** —— 选中某件道具（菜单道具详情的截图要用）。
        ///
        /// 菜单里"点道具图标 ⇒ 详情框显示它"是个 `GUI.Button`，
        /// 探针没法点（它只驱动状态、不模拟鼠标）⇒ 直接把选中项写进去 ✓
        /// ⚠️ 只改 UI 的选中项，不进任何玩法路径 ✓ 找不到这件道具 ⇒ 不选 ✓
        /// </summary>
        public void DebugSelectItem(string itemId)
        {
            _itemSelected = string.IsNullOrEmpty(itemId) || TableDatabase.GetItem(itemId) == null
                ? null : itemId;
        }

        /// <summary>
        /// ★ v172：一个植物格（主界面 6 格 / 菜单中栏 3×2 共用）★
        /// ★ v196：项 = ① 名称 ② 品质（都在上面那两行抬头里）③ 等级 ④ 成熟时间 ⑤ 金币 ⑥ 经验
        /// —— 六项，一项不多一项不少 ✓（内容全部来自 `PlantCellInfoLines`，五处**同源** ✓）
        ///
        /// ★ v198：正文改成 `PlantCardLines`（4 行数值 ＋ **底部特殊功能**），并**可滚动** ★
        ///   用户原话："植物增加滑动条展示，在底部显示拥有的特殊功能"。
        ///   改前这里是 4 行定高、**一格滚动都没有** —— 第 5 行根本没地方放 ✓
        ///   现在正文整块交给 `DrawPlantCellLines`：装得下直画（清晰）、装不下就出滚动条 ✓
        ///   格高与图标位置**一个像素没动**（起点仍是 `pad + iconS + 4`）✓
        /// </summary>
        private void DrawPlantCell(RunSession s, SeedSlot slot, Rect cell, int scrollIndex)
        {
            Color pv = GUI.color;
            GUI.color = slot.IsEmpty ? SlotBg : SlotBgFilled;
            PaperFill(cell);
            ScrollFrame(cell);
            GUI.color = QualityColor(slot.Quality);
            GUI.DrawTexture(new Rect(cell.x, cell.y, 5f, cell.height), White);   // 左侧品质条
            GUI.color = pv;

            float pad = 5f;
            float iconS = 30f;
            Rect icon = new Rect(cell.x + pad + 3f, cell.y + pad, iconS, iconS);
            if (!slot.IsEmpty)
                DrawOfferIcon(icon, QualityColor(slot.Quality), slot.Def != null ? "icon_plant_" + slot.Def.Id : null);

            // ① 名称　② 品质
            Txt(new Rect(icon.xMax + 5f, cell.y + pad - 4f, cell.width - iconS - 18f, 20f),
                slot.IsEmpty ? "空格" : slot.Label, OnDarkBold);
            Txt(new Rect(icon.xMax + 5f, cell.y + pad + 14f, cell.width - iconS - 18f, 18f),
                slot.IsEmpty ? "商店买种子后自动种下" : s_rarityName(slot.Quality), OnDark);

            if (slot.IsEmpty) return;

            // ③④⑤⑥ ＋ 底部特殊功能 —— 可滚动正文（v198）
            //   起点仍是 `pad + iconS + 4`（与改前逐像素相同）：图标止于 y+35，正文从 y+39 起
            float ly = cell.y + pad + iconS + 4f;
            Rect info = new Rect(cell.x + pad, ly, cell.width - pad * 2f,
                                 Mathf.Max(10f, cell.yMax - 3f - ly));
            DrawPlantCellLines(info, PlantCardLines(slot), scrollIndex);
        }

        /// <summary>
        /// ★ v172：主界面**植物区** = 6 个中格子（2 列 × 3 行）★
        /// ⇒ 格数 = `Bag.SlotCount`（= `Progress.MaxPlots` = 田地列数 6 ✓ 新玩法"一株植物独占一列"✓）
        ///
        /// ★ v177：**只在准备阶段绘制**（调用点见 `Draw` 里的 `s.Phase == Prepare` 门控）★
        /// 战斗阶段**整块不进入绘制**（没有调用、没有测量、没有绘制）——
        /// 这是需求"删除种子袋 UI（场景不显示）"的落点。
        /// 面板本体保留：它是"我背了哪 6 个种子"的唯一一览，删掉它等于把这条信息一起删掉 ✗
        /// 而它**本来就没有按钮**（`DrawPlantCell` 全是 `GUI.Label`）⇒ 保不保留都不影响可点入口 ✓
        ///
        /// ★ 位置为什么是 (10, 354) ✗ ★
        ///   `DrawKeyHelp` 的底边 = 142 + 34 + 8×22 = 352 ✓
        ///   `DrawPlotPrompt` 的左边 = 中−310 = 401（虚宽 1422）✓ 本面板右边界 390 < 401 ⇒ 不叠 ✓
        ///   ⚠️ 准备阶段它会与居中的"选择初始种子"面板部分重叠（后者 x 241..1181）——
        ///      这是**既有**现象：那块模态自己带 0.58 黑遮罩，战斗 HUD 的其余面板（生命栏、
        ///      按键说明、武器栏）在准备阶段本来就被同一块遮罩压住一半 ✓ 不是本次改动引入的 ✓
        /// </summary>
        private void DrawPlantPanel(RunSession s)
        {
            float x = 10f, y = 354f, w = 380f, h = 440f;
            Panel(new Rect(x, y, w, h));
            Txt(new Rect(x + 10, y + 3, w - 20, 26),
                $"植物（{s.Bag.OccupiedCount}/{s.Bag.SlotCount}）　一株独占一列", _bold);

            float pad = 6f;
            float cw = (w - pad * 3f) / 2f;
            float ch = (h - 30f - pad * 4f) / 3f;
            for (int i = 0; i < s.Bag.SlotCount && i < 6; i++)
            {
                Rect cell = new Rect(x + pad + (i % 2) * (cw + pad), y + 30f + (i / 2) * (ch + pad), cw, ch);
                DrawPlantCell(s, s.Bag[i], cell, i);
            }
        }

        // ------------------------------------------------------------ 顶部：只显示阶段
        private void DrawWave(RunSession s)
        {
            // ★★ v300：训练场 ⇒ **整块顶部波次 UI 一帧都不画** ★★
            //   用户原话：「（训练场）没有波次等 UI」。这一块画的是：
            //     ① `PhaseLabel` 那一行 —— 「⏱ mm:ss　第 N 分钟　☠ BOSS mm:ss」（波次/计时/倒计时）
            //     ② 波次提示行（`s.Waves.Wave.Tip` 与「种植时间 Ns」）
            //   两者都属于"波次"这一件事 ⇒ 一起关掉（这也是用户点的"没有波次等 UI"）✓
            //   「BOSS 血条」不在这里 —— 它在 `WorldView.SyncBossBar`，而训练场里
            //   **根本不存在 `EnemyKind.Boss` 的敌人**（稻草人是 `Wander`）⇒ 那条血条
            //   结构性地不会出现（见交付报告 ②）✓
            //   守卫放在**方法最前面**：关着的时候连 `PhaseLabel` 的字符串都不会拼 ✓
            //   关掉开关时这一行不成立，本方法后续每一句原样执行 ⇒ 战斗 HUD 逐字节相同 ✓
            if (TrainingMode.NoWaves) return;
            float w = 640f;
            float x = _virtualW * 0.5f - w * 0.5f;
            // ══ v177：**去掉底板**（需求"倒计时不使用背景"）══
            // 改前：`Panel(x, 8, 640, 66)` 一块奶油底板，标题与提示都压在上面。
            // 改后：标题走 `HudLabel`（30px / 2px 描边），底下的"形态"改由一条**强调横线**承担。
            // 为什么保留一条线 ✗ 不是舍不得底板 ✗：计时器是顶部唯一的居中信息，
            // 去掉底板后它与远景（树冠/栅栏）之间没有任何"这是我的一块地方"的信号。
            // 一条 2px 的线给出**下边界**，而它覆盖的像素面积约为原底板的 1/40（66px 高 → 2px），
            // 中间仍然是透的 ⇒ 符合"靠描边而不是底板"这条要求 ✓
            string phaseText = PhaseLabel(s);
            // ★ v165：顶部不再挂任何"倒计时秒数" ★
            // 删掉的两条都是旧回合制的产物：
            //   ① `剩余 {Waves.Remaining}s` —— waves.json 的 duration（30~90 秒）波次倒计时
            //   ② `击败 BOSS 剩余 Ns`      —— bossTimeLimit(90s) 倒计时，而它在计时制下**不可达**
            //      （`CombatElapsed` 每分钟被 StartWave 归零，最大 60s ⇒ 永远走不到 90s），
            //      画出来就是一个永远不会到期、每分钟重置的假倒计时 ✗
            // 玩家真正需要的时间信息在 PhaseLabel 里已经有两处**更准**的：
            //   · mm:ss 游戏计时器
            //   · ☠ BOSS mm:ss（来自 BossSchedule，是真实的"下一次刷新"倒计时）
            HudLabel(new Rect(x, 8, w, 34), phaseText, HudTitle, 2f, 0.94f);
            // ★ v260：天气注释（☀ 晴天 / ☂ 雨天）贴在 `BOSS mm:ss` 之后 ★
            //   只读 `ProgMechanics.Weather`（天气的**掷骰/计时逻辑一个字没动**）；
            //   无天气时本方法**连测量都不做**、直接 return ⇒ 一个像素都不画、也不占位 ✓
            DrawWeatherTag(new Rect(x, 8, w, 34), phaseText);

            // 强调横线：中间实、两端淡（三段拼，IMGUI 没有渐变矩形）
            Color linePrev = GUI.color;
            const float lineY = 45f;
            GUI.color = new Color(1f, 0.95f, 0.80f, 0.16f);
            GUI.DrawTexture(new Rect(x + 30f, lineY + 0.5f, w * 0.5f - 30f, 1.5f), White);
            GUI.DrawTexture(new Rect(x + w * 0.5f, lineY + 0.5f, w * 0.5f - 30f, 1.5f), White);
            GUI.color = new Color(1f, 0.95f, 0.80f, 0.80f);
            GUI.DrawTexture(new Rect(x + w * 0.5f - 90f, lineY, 180f, 2.5f), White);
            GUI.color = linePrev;

            string tip;
            if (s.Phase == PhaseState.Combat && s.Waves.InGrace)
                tip = $"种植时间 {Mathf.CeilToInt(s.Waves.GraceLeft)}s —— 不会刷怪，先开垦播种！";
            else if (s.Waves.Wave != null && s.Phase == PhaseState.Combat)
                tip = s.Waves.Wave.Tip;
            else
                tip = "";
            RowsFor(tip, w, "波次提示");
            HudLabel(new Rect(x, 52, w, 24), tip, HudCenter);
        }

        /// <summary>天气注释与 `BOSS mm:ss` 之间的间隔（用户要求"约 12~16px"）⇒ 取中值 14。</summary>
        private const float WeatherTagGap = 14f;

        /// <summary>
        /// ★ v260：顶部时间栏右侧的**天气注释**（需求："晴天和雨天触发时在顶部时间栏旁注释"）★
        ///
        /// ── 位置不是写死的 ✗ ★
        ///   `PhaseLabel` 那一行是**居中对齐**的整串（`HudTitle` = 30px 粗体 / MiddleCenter），
        ///   宽度随"第 N 分钟 / BOSS mm:ss / 秒数进位"实时变化 ⇒ 写死一个 x 会让注释
        ///   随着分钟数忽远忽近 ✗ ⇒ 用 `CalcSize` **实测这一行此刻的宽度** `tw`，
        ///   注释左边界 = 行心 + `tw/2` + `WeatherTagGap` ✓
        ///   （量的是**虚拟**宽度：`HudTitle.fontSize` 保持虚拟 30，与 `Txt` / `RowsFor` 同一口径 ✓）
        ///
        /// ── 为什么把注释的矩形给成"刚好一个文字宽"的居中小格 ★
        ///   复用本行**现有**的 `HudTitle`（不克隆、不新建样式），它的 alignment 是 MiddleCenter；
        ///   矩形宽 = 实测文字宽 ⇒ MiddleCenter 恰好让字形左边界落在算出来的 `idealX` 上 ✓
        ///   这样既守住了"不新建样式"，也不用去改这个共享样式的 alignment（那会波及所有标题）✓
        ///
        /// ── 颜色全部从本行**现有**颜色派生（不引入新配色方案）★
        ///   基准 = `HudTitle.normal.textColor` ≈ (1, 0.98, 0.90, 1)，只压通道、**α 原样不动**：
        ///     · 晴 = (r, g×0.90, b×0.52) ≈ 暖黄
        ///     · 雨 = (r×0.58, g×0.82, b) ≈ 冷蓝
        ///   描边宽度/α 也沿用本行的 `2f / 0.94f` ⇒ 白芯 + 同款暗描边，亮度与相邻文字同档 ✓
        ///
        /// ── 为什么还要看 `WeatherIntensity01` ★
        ///   `ProgMechanics.Weather` 要等 `WeatherRemain` 归零才变 `None`，而太阳/雨幕是
        ///   0.8s 淡入淡出的（`WorldView.SyncWeatherFx` 用它）⇒ 强度 0 时天上已经干净了，
        ///   注释却还挂着，会读成"说晴但没太阳" ✗ ⇒ 与表现层同一判据、一起出现/消失 ✓
        /// </summary>
        private void DrawWeatherTag(Rect lineRect, string phaseText)
        {
            WeatherKind kind = ProgMechanics.Weather;
            if (kind == WeatherKind.None) return;                 // 无天气：不测量、不画、不占位
            if (ProgMechanics.WeatherIntensity01 <= 0.01f) return;

            string tag = kind == WeatherKind.Sunny ? "☀ 晴天" : "☂ 雨天";

            GUIStyle style = HudTitle;                            // ← 复用时间栏那一行的样式
            float tw = style.CalcSize(new GUIContent(L10n.S(phaseText ?? ""))).x;
            float tagW = style.CalcSize(new GUIContent(L10n.S(tag))).x;
            float idealX = lineRect.x + lineRect.width * 0.5f + tw * 0.5f + WeatherTagGap;

            Color prev = style.normal.textColor;
            style.normal.textColor = kind == WeatherKind.Sunny
                ? new Color(prev.r, prev.g * 0.90f, prev.b * 0.52f, prev.a)
                : new Color(prev.r * 0.58f, prev.g * 0.82f, prev.b, prev.a);
            HudLabel(new Rect(idealX, lineRect.y, tagW, lineRect.height), tag, style, 2f, 0.94f);
            style.normal.textColor = prev;                        // 与 `HudLabel` 同一收口方式：画完立刻还原
        }

        /// <summary>v154：秒 → mm:ss（计时制顶部计时器用）。</summary>
        private static string FmtTime(float seconds)
        {
            int t = Mathf.Max(0, (int)seconds);
            return (t / 60).ToString("00") + ":" + (t % 60).ToString("00");
        }

        private static string PhaseLabel(RunSession s)
        {
            switch (s.Phase)
            {
                case PhaseState.Prepare: return "准备阶段 · 选择初始种子";
                // v154：**计时制** —— 顶部不再显示"第 N/20 波"，改为计时器 + Boss 倒计时
                // 需求："中间做计时器计时游玩时间，计时器实时汇报下一波 boss 的刷新时间倒计时"
                case PhaseState.Combat:
                    return $"⏱ {FmtTime(s.RunTime)}　第 {s.WaveIndex} 分钟"
                         + (s.BossCountdownText() is string bc && bc.Length > 0 ? $"　☠ {bc}" : "");
                case PhaseState.Shop:
                    return $"⏱ {FmtTime(s.RunTime)}　第 {s.WaveIndex} 分钟　商店";
                case PhaseState.Defeat: return "守园失败";
                // v165：`case PhaseState.Victory` 已删除（枚举成员不存在了）——
                // 计时制唯一的结束条件是玩家死亡。
                default: return s.Phase.ToString();
            }
        }

        // ------------------------------------------------------------ 底部中：耕地提示（两条进度条）
        /// <summary>
        /// v177：**去掉底板**。
        ///
        /// 它不在用户点名的四类里（血条 / 倒计时 / 充能 / 切换武器），但它是**同一种形态**：
        /// 一对进度条，就挂在刚去底的充能条上方 20px 处。不改它的话，画面底部会形成
        /// "上面一块奶油板、下面一对裸条"的对照 —— 读起来像漏做了一块，而不是两种刻意的层级。
        ///
        /// ⇒ 规则统一成：**条 / 计时 / 武器框 = 无底板；成段的文字列表（左侧按键说明）= 保留底板**。
        ///    理由不是"哪块好看"，而是**这两类东西对底板的依赖不同**：
        ///    · 条有明确几何形状与填充色 ⇒ 描边+投影足够；
        ///    · 8 行文字没有形状，靠底板把"这一整块是一份说明"框出来，去掉就散在草地上了。
        ///    所以"所有统一不使用背景"落在**同类元素之间统一**，而不是把整屏都掀掉 ✓
        /// （若判定这一步超出范围，回退方法：把本方法里两个 `BarBare` 换回 `Bar`、
        ///   并在 y 处补一行 `Panel(new Rect(x, y, w, 88));` —— 两行改动，其余不动 ✓）
        /// </summary>
        private void DrawPlotPrompt(RunSession s)
        {
            if (s.Phase != PhaseState.Combat) return;
            float w = 620f;
            float x = _virtualW * 0.5f - w * 0.5f;
            // ★ v180：整块上移 `PlotPromptLiftY`（= 武器区 64 + 空隙 8 = 72）★
            //   为什么必须动它 ✗ ★：它改前就贴在充能条正上方（y = _virtualH-196，用到的 y 区间
            //   [_virtualH-222, _virtualH-120]）⇒ 武器区一旦落到 [_virtualH-162, _virtualH-98]，
            //   两者**必然重叠**（重叠区 = [_virtualH-162, _virtualH-120]）✗
            //   ⇒ 上移一个武器区高度后：本块占 [_virtualH-294, _virtualH-192]，
            //     武器区顶 `_virtualH-162` ⇒ 留 **30px** 空隙 ✓ 谁都不盖谁 ✓
            //   ⚠️ 块内所有相对偏移（+10/+30/+56、-26）**一个都没改** ⇒ 只是整体平移 ✓
            float y = _virtualH - 196f - PlotPromptLiftY;

            if (s.CurrentPlotIndex < 0) return;   // 没有目标地块 ⇒ 本来就没东西可画（旧版这里画一块空底板）

            FarmPlot plot = s.World.Farm.Slots[s.CurrentPlotIndex];
            string groupText = s.ActiveGroupCount > 1 ? $"　（同时 {s.ActiveGroupCount} 块）" : "";

            // v23：一条生长进度 + 一条蓄水（无掘地/播种/洒水）
            string growText = plot.State == PlotState.Grown
                ? "已成熟 → 用锄头攻击收获"
                : (plot.Thirsty ? $"缺水停长（蓄水 0/{plot.WaterMax}）" : $"生长中 {plot.GrowthRemaining:0}s");
            BarBare(new Rect(x + 10, y + 30, w - 20, 22), plot.GrowthProgress01,
                plot.State == PlotState.Grown ? new Color(0.95f, 0.78f, 0.25f, 0.95f) : new Color(0.45f, 0.75f, 0.35f, 0.95f),
                growText + groupText);
            BarBare(new Rect(x + 10, y + 56, w - 20, 20), plot.WaterMax <= 0 ? 0f : (float)plot.Water / plot.WaterMax,
                new Color(0.3f, 0.6f, 0.9f, 0.95f), $"蓄水 {plot.Water}/{plot.WaterMax}（水壶攻击土地补水）");
            // v93：删除"施肥（空格）0/0 次"弹窗 —— 旧版设计的残留。
            //
            // 旧版是"站在耕地上对着它连按空格施肥若干次才催熟"；肥料后来改成了**投掷爆炸**
            // （见 RunSession 的 ArmBlast / ExplodeAt），这条"按次数"的路径就没人驱动了。
            // 留下的证据很直白：面板上显示的是 `0 / 0 次` —— FertilizeTarget 恒为 0，
            // 进度条永远空着，玩家看到的是一个没有任何作用的提示框。
            //
            // 只删呈现，不删 s.FertilizeHits / s.FertilizeTarget 两个字段：
            // 它们在 RunSession 里仍参与判定与埋点，动它们要连带改逻辑层，不属于"删旧版 UI"的范围。
            //
            // 水壶的补水提示保留在上面那行"蓄水 x/y（水壶攻击土地补水）"——那才是现行机制。

            if (!string.IsNullOrEmpty(s.Prompt))
                HudLabel(new Rect(x, y - 26, w, 24), s.Prompt, HudCenter);   // v177：底板没了 ⇒ 提示文字也要描边
        }

        private static string StateLabel(FarmPlot plot)
        {
            switch (plot.State)
            {
                case PlotState.Empty: return "未开垦";
                case PlotState.Dug: return "已掘地";
                case PlotState.Growing: return "生长中";
                case PlotState.Grown: return "成株";
                default: return "锁定";
            }
        }

        private static string ActionLabel(FarmPlot plot)
        {
            switch (plot.NextAction)
            {
                case PlotAction.Dig: return "掘地 2.0s";
                case PlotAction.Sow: return "播种 1.0s";
                case PlotAction.Water: return "洒水（生长 -30%）";
                case PlotAction.Fertilize: return "施肥（生长 -50%）";
                default: return "无需操作";
            }
        }

        // ------------------------------------------------------------ 开局三选一（鼠标点击）
        /// <summary>
        /// ★ v179：选种卡片的**内容高度**（用户第 ⑤ 条"框太大"）★
        ///
        /// ── 病象（用户截图 + 我复现的那张 `base-prepare.png`）──
        ///   卡片高 **330**，而里面真正有东西的只有上边一小块：
        ///     左列图标 + 名字 + 品质（到 `card.y + ~118` 就结束了）
        ///     右侧"信息四行"（4 × 22 = 88）
        ///   可是右侧那个信息区是 `DrawInfoList` —— 它会**把整个视口填成一块文本框** ✗
        ///   视口高 = 330 − 44(按钮预留) − 16 = **270** ⇒ 88 的文字画在 270 的框里
        ///   ⇒ 中部一整片空 ✗ 正是用户说的"三张卡片的中部是一大片空的"✓
        ///
        /// ── 196 是怎么算出来的（★ 不是拍的 ★）──
        ///   左列（`SeedCardPanel` 里那套算法，**一个字没改**）：
        ///     顶 12 + 图标 `Clamp(96−36,36,74)`=60 + 间隔 4 + 名字 24 + 品质 20 = **120**
        ///     再加"等级"那一行 20（选种卡恒为 1 级 ⇒ 不画 ✓）= 140（上界）
        ///   右列：4 行 × 22 = **88**
        ///   底部：按钮预留 `ButtonReserve 36` + 8 = **44**
        ///   ⇒ 需要 max(120, 88) + 44 = **164**；按"带等级行"的上界 140 算 ⇒ **184**
        ///   ⇒ 取 **196**：比上界多 12 ⇒ 卡片永远够放、且没有多余的空框 ✓
        ///   校验：右侧视口 = 196 − 44 − 16 = **136** ≥ 88（四行）⇒ 不用滚动 ✓
        ///         左列底 140 < 按钮顶 196−32 = **164** ⇒ 不撞按钮 ✓
        ///
        /// ⚠️ 为什么**不改字号** ✗：用户明确说"别改文字大小（字号是刚调好的整数像素）"✓
        ///   所以这次只动矩形高度，样式与字号一处未碰 ✓
        /// </summary>
        private const float SeedChoiceCardH = 196f;

        /// <summary>
        /// ★ v179：选种弹窗的**总高** = 顶部留白 62 + 卡片 196 + 底部留白 22 = **280** ★
        /// 改前 420 ⇒ 整体矮了 140（×0.67）✓ 与卡片一起收，弹窗不会"卡片变矮、面板还那么高" ✗
        /// 宽度仍是 940：三张 296 的卡 + 22 左右留白 = 940，**一个像素没动** ✓
        /// </summary>
        private const float SeedChoicePanelH = 62f + SeedChoiceCardH + 22f;

        /// <summary>
        /// ★ v179：**开局选种弹窗"现在开着吗"的唯一判据** ★
        ///
        /// 为什么把它抽成一个函数（而不是在调用处各写一遍条件）✗
        ///   它有**两个**消费者，而两者的判断必须**永远一致**：
        ///     ① `switch` 里决定**画不画弹窗** ✓
        ///     ② 上面决定**画不画植物袋面板**（用户第 ④ 条）✓
        ///   两处各写一遍 `Phase == Prepare` 的话，将来任何一边改了条件，
        ///   就会出现"弹窗没画、植物袋也不画"（两块一起消失 ✗）或
        ///   "弹窗画了、植物袋也画"（叠在一起 ✗）—— 正是这次要修的那个现象 ✓
        ///
        /// 判据内容：弹窗由 `PhaseState.Prepare` 这个阶段**独占**（选完种子立刻进 Combat）✓
        /// `s.Paused` 也算"不显示"：暂停时整块战斗 HUD 与弹窗都不画、只画菜单 ✓
        /// （与调用处 `if (!s.Paused)` 是同一件事，这里写进来是为了让判据自洽 ✓）
        /// </summary>
        private static bool SeedChoiceOpen(RunSession s)
            => s != null && !s.Paused && s.Phase == PhaseState.Prepare;

        private void DrawSeedChoice(RunSession s)
        {
            // v94：整屏半透明黑遮罩（需求"为背景增加半透明黑色遮罩"）。
            //
            // 目的不是"把画面调暗"，而是**把还亮着的战场隔到后面去**：
            // 选种是开局第一件事，此时场上已经有敌人、飘字、霓虹在动，
            // 玩家一边读三个种子的属性一边被那些东西分神。
            // 遮罩画在卡片**之前**，所以卡片与文字仍然清晰。
            Color maskPrev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.58f);
            GUI.DrawTexture(new Rect(0f, 0f, _virtualW, _virtualH), White);
            GUI.color = maskPrev;

            float w = 940f, h = SeedChoicePanelH;
            Rect r = new Rect(_virtualW * 0.5f - w * 0.5f, _virtualH * 0.5f - h * 0.5f, w, h);
            Panel(r);
            Txt(new Rect(r.x, r.y + 10, w, 34), "选择初始种子（鼠标点击，或按数字键 1–3）", _title);
            for (int i = 0; i < s.InitialChoices.Length; i++)
            {
                SeedDef def = s.InitialChoices[i];
                int quality = i < s.InitialChoiceQuality.Length ? s.InitialChoiceQuality[i] : 0;
                // v48：卡片 264→296、面板 880→940，配合信息区自动换行，右侧不再被截断
                Rect card = new Rect(r.x + 22 + i * 306, r.y + 62, 296, SeedChoiceCardH);
                Color prev = GUI.color;
                GUI.color = new Color(0.14f, 0.16f, 0.13f, 0.95f);
                GUI.DrawTexture(card, White);
                GUI.color = QualityColor(quality);
                GUI.DrawTexture(new Rect(card.x, card.y, card.width, 5), White);
                GUI.color = prev;
                // v19：与商店同样的种子卡（含滑动条），只是按钮是"选择"
                var slot = new SeedSlot { Def = def, Quality = quality };
                ButtonReserve = 36f;
                SeedCardPanel(s, slot, card, i, _choiceScroll);
                ButtonReserve = 0f;
                if (Btn(new Rect(card.x + 10, card.yMax - 32, card.width - 20, 24), $"选择（{i + 1}）"))
                    PendingInitialChoice = i;
            }
        }

        // ------------------------------------------------------------ 商店（鼠标点击购买）
                // ------------------------------------------------------------ 结算
        /// <summary>
        /// 结算画面。v165：**只有"守园失败"一种**。
        ///
        /// 旧版是 `DrawResult(s, victory)`，而 `victory` 恒为 false
        /// （`PhaseState.Victory` 在工程里 0 命中，见 Defs.cs 的说明）✗
        ///
        /// 结算行原来写 `波次 {WaveIndex}/{TotalWaves}` —— `TotalWaves` 恒为配置里的 20，
        /// 于是第 20 分钟之后会显示"波次 21/20"这种假数据 ✗
        /// 计时制没有终点，该显示的是**坚持了多久** ✓
        /// </summary>
        private void DrawResult(RunSession s)
        {
            // ★ v218：**h 260 → 306** —— 多出来的 46px 给"返回主城"这一个按钮
            //   （用户原话："战斗模式死亡后，除了重新开始选项，再增加返回主城的选项"）
            //   "重新开始"整体上移 46px（仍在同一列居中），"返回主城"落在它下面 ✓
            float w = 520f, h = 306f;
            Rect r = new Rect(_virtualW * 0.5f - w * 0.5f, _virtualH * 0.5f - h * 0.5f, w, h);
            Panel(r);
            Txt(new Rect(r.x, r.y + 14, w, 40), L10n.T("combat.death"), _title);
            Txt(new Rect(r.x + 20, r.y + 66, w - 40, 120),
                // ★ 本地化（v251）：这 4 行是**带变量的拼接**，形状表命中不了 ⇒ 在调用点用
                //   `T(key, args)` 逐行套格式。**先翻变量**（`s.ResultText` 由逻辑层给），
                //   再让 `string.Format` 填进去 ✓ 与 `hint.harvestNow` 同一个套路。
                s.ResultText + "\n\n" +
                L10n.T("result.line1", FmtTime(s.RunTime), s.WaveIndex, s.Kills) + "\n" +
                L10n.T("result.line2", s.Gold, s.Progress.Level) + "\n" +
                // ★ v166：改成读**已占用的列数**（= 玩家实际种了几株）★
                // 为什么不再读植物实体数：新玩法里"一株植物"住在**一列地块**上 ✓
                // ★ T2 更新：`World.Plants` 这个列表**本身已经删除**（不只是"无生产者"）✗
                //   所以这里读 `Farm.PlantCount` 不再只是"口径更对"，而是**唯一读法** ✓
                // 玩家在结算面板上会永远看到"剩余植物 0"，而他明明种了 3 株 ✓ 那就是在撒谎 ✗
                // `Farm.PlantCount` = 有作物的列数 = 株数 ✓ `Farm.Columns` = 可种植株数上限 ✓
                L10n.T("result.line3", s.World.Farm.PlantCount, s.World.Farm.Columns,
                       s.World.Farm.PlotCount), _center);
            if (Btn(new Rect(r.x + w * 0.5f - 90, r.y + h - 102, 180, 36), "重新开始"))
                PendingRestart = true;

            // ★★ v218：**死亡界面加「返回主城」**（在"重新开始"下面）★★
            //   用户原话："战斗模式死亡后，除了重新开始选项，再增加返回主城的选项"
            //
            //   ── 复用哪条路径（不新写第二套退出）──
            //     `PendingExitToTown` → `GameBootstrap.Update` 消费 → `HandleExitToTown()`
            //     → `ExitToTownNow()`（清局 ＋ 存档结论 ＋ `LoadScene(Town)`）
            //     —— 与暂停菜单里那个「退出战斗（回到主城）」**同一条路**，一个字没复制 ✓
            //
            //   ── 为什么这里**一次点击就退出**（而菜单里要二次确认）──
            //     菜单里的确认是为了防"战斗中点错"；死亡界面本来就是终局画面，
            //     没有"正在打、误触代价大"这个前提 ⇒ 不设确认更顺手 ✓
            //     实现上只是**预先把它置成已确认**：`HandleExitToTown` 看到 `Armed=true`
            //     就直接走 `ExitToTownNow()` ⇒ 一次点击回主城 ✓（不改那条函数本身）
            if (Btn(new Rect(r.x + w * 0.5f - 90, r.y + h - 56, 180, 36), "返回主城"))
            {
                ExitToTownArmed = true;     // 预置确认态 ⇒ 一次点击生效
                PendingExitToTown = true;
            }
        }

        // ------------------------------------------------------------ v184 菜单（左：武器详情 右：道具＋植物 最后：属性）

        // ★ v184：深色卡面上的 12px 亮字（左对齐）★
        //   `Tiny`（12px）是**深棕**字 —— 它服务的是奶油底；武器卡是深色底 ✗
        //   所以这里另立一个（与 `OnDark` 系列同一条路子：**底变了就显式选边**，不改共用样式的默认值）
        //   ⚠️ 道具格**不用**它：道具格的底是按钮皮肤（亮底）⇒ 那里仍用 `Tiny`（深棕）✓
        private GUIStyle _menuTiny;
        private GUIStyle MenuTiny
        {
            get
            {
                if (_menuTiny == null)
                {
                    _menuTiny = new GUIStyle(_small) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
                    _menuTiny.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
                }
                return _menuTiny;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  ★ v216：水壶耗水的**显示**不再自带常量 ★
        //
        //  改前这里有两行「显示用」的镜像：`BeamWaterCostPerTick = 10` / `BeamWaterInterval = 0.5f`
        //  （v184 留下的，注释原文："这两个数是 `RunSession` 的 private const… UI 读不到，
        //    只能在这里留一份同值的镜像"）✗
        //  而 `RunSession.BeamAmmoCost` 早已改成 **20** ⇒ 镜像立刻变成谎话：
        //    卡面印「耗水 10/0.5s（20/秒）」，实际每 0.5 秒扣 **20**（40/秒）—— 差一倍 ✗
        //  更糟的是它**不吃道数**：装了多柱（2 道）实际扣 40/0.5s，卡面还是写着 10 ✗
        //  ⇒ 用户报的"多柱还有额外消耗"里，有一半是这条**显示**在骗人（另一半见下条注释）✓
        //
        //  改法：`RunSession` 那两行常量 `private` → `internal`，本文件直接读；
        //       一次的总耗走 `WeaponSystem.BeamWaterCostPerFire`（**与 `TickBeam` 扣水同一个纯函数**）
        //  ⇒ 全工程只剩**一个**水耗算式，"改了没跟上"不可能再发生 ✓
        // ════════════════════════════════════════════════════════════════════════

        // ════════════════════════════════════════════════════════════════════════
        //  ★ v184：菜单里**一把武器的一张卡**（用户第 ② 条"武器显示详情"）★
        //
        //  ── 用户原话 ──
        //    "武器显示详情，伤害的计算公式、以及所有的属性（暴击、击退、爆炸（肥料专属）等）"
        //    ⇒ 两个区块，一个区块对一句话：
        //      ① **伤害计算公式**：把**式子写出来**（含数字代入），不是只写结果
        //      ② **所有属性**逐个列，且**按武器只列它有的**：
        //         锄头 → 不列"耗水""爆炸范围"／水壶 → 不列"爆炸范围"／肥料 → 不列"耗水" ✓
        //
        //  ── 数值从哪来（**必须与运行时同一个来源**，否则界面就是在撒谎）──
        //    伤害      = `DamageResolver.AttackFormula`（**直接调用**，不另算一份 ✓）
        //                ★ v195：肥料也走这一条 —— 爆炸倍率删除后三把武器**同一个式子** ★
        //                —— 与 `WeaponStatLine`（商店那一行）逐项相同；与
        //                   `RunSession.SwingHoe / FireBeam / ExplodeAt` 是同一条公式 ✓
        //    暴击率/暴击伤害 = `StatBlock.Get(StatId.CritRate / CritDmg)`（`Stats.cs:87`）
        //    击退      = `StatBlock.Get(StatId.Knockback)` × **武器系数**
        //                （锄 0.5 `RunSession.cs:4025`／壶 0.2 `:4276`／肥 1.0 `:4557`，照抄 ✓）
        //    攻击速度  = `tool.cooldown ÷ max(0.05, StatId.AtkSpeed)`（每次的秒数；与 `WeaponCds` 同式 ✓）
        //    攻击范围  = `tool.range × max(0.1, StatId.AtkRange)`（与 `RunSession.WeaponRange:3761` 同式 ✓）
        //    爆炸范围  = `RunSession.BlastBaseRadius(tool.baseExplodeRange) × StatId.ExplodeRange`
        //                （**直接调公共函数**；与 `BlastRadiusNow:4408` 同式 ✓）
        //    充能速度  = 同"攻击速度"（肥料每次投掷的充能 = `WeaponCds[slot] = cooldown / fireRate`）
        //                ＋ 当前弹药 `FertAmmo / FertAmmoMax`
        //    耗水      = `WeaponSystem.BeamWaterCostPerFire(RunSession.BeamAmmoCost, m)` ÷
        //                `RunSession.BeamAmmoInterval`（★ v216：读真值、按道数乘，没有镜像 ✓）
        //    挥砍/自充 = `WeaponModifiers`（`s.ModsFor(slot)`，插件聚合层 ✓）—— 只在真的有时出现
        // ════════════════════════════════════════════════════════════════════════
        private void DrawMenuWeaponCard(RunSession s, WeaponSlot slot, Rect card)
        {
            if (s == null || s.Progress == null || TableDatabase.Tuning == null) return;
            TuningDef t = TableDatabase.Tuning;
            StatBlock st = s.Progress.Stats;
            ToolDef tool = slot == WeaponSlot.Can ? t.tools.can
                         : slot == WeaponSlot.Fertilizer ? t.tools.fertilizer
                         : t.tools.hoe;
            if (tool == null) return;

            bool cur = IsCurrentWeapon(s, slot);
            Color pv = GUI.color;
            // 卡底：与道具格**同一套深色**（奶油面板上的"卡"）；当前武器换成选中色
            GUI.color = cur ? ItemTileSel : SlotBg;
            GUI.DrawTexture(card, White);
            // 左侧色条：当前 = 亮绿，其余 = 木色（与商店行 / 植物卡"品质条在左"同一条规矩）
            GUI.color = cur ? new Color(0.55f, 0.95f, 0.45f) : new Color(0.62f, 0.50f, 0.34f);
            GUI.DrawTexture(new Rect(card.x, card.y, 5f, card.height), White);
            GUI.color = pv;

            // ── ① 名字行（图标 + ▶名字 + 插件占用）──
            // ★ v186：**这里不再显示品质** ★（用户原话："删除武器品质的概念，武器没有品质区分"）
            //   上一版是 `锄头　白 ×1.00` —— 那个"白"和那个"×1.00"既不是机制、
            //   也不再影响任何数值（`RunSession.ToolMult` 已删除）⇒ 卡面在撒谎的地方就删掉 ✓
            float lx = card.x + 12f;
            float lw = card.width - 22f;
            string iconId = slot == WeaponSlot.Hoe ? "hoe"
                          : slot == WeaponSlot.Can ? "can" : "fertilizer";
            float nameX = lx;
            if (Icon(iconId, new Rect(lx, card.y + 8f, 22f, 22f))) nameX = lx + 26f;
            Txt(new Rect(nameX, card.y + 5f, card.width - (nameX - card.x) - 122f, 24f),
                (cur ? "▶ " : "　 ") + WeaponSlotName(slot), OnDarkBold);
            Txt(new Rect(card.xMax - 120f, card.y + 8f, 108f, 20f),
                $"插件 {s.Goods.SlotsUsedOn(slot)}/{PlayerGoods.MaxPluginsPerWeapon}", OnDark);

            // ── ② 伤害公式：**只印一条**（用户需求④）──
            //     伤害 = (基础 X + 对应类型伤害 Y) × 伤害倍数 Z%
            //   ★ 不印暴击那条（用户："暴击效果 = 伤害 × 暴击伤害（这一条公式不显示，作为内部计算）"）
            //   ★ 不印"× 品质"（品质概念已删除）
            //   ★ 式中的 X / Y / Z 与实际结算**同一个函数**：
            //     `DamageResolver.WeaponDamage`（肥料是 `WeaponBlastDamage`）——
            //     运行时三条攻击路径调的正是它们：`RunSession.DoHoeSwing`（锄头）/ `FireBeam`（水壶）/
            //     `ExplodeAt`（肥料）—— 以**函数名**为准（行号会随后续提交漂移）
            //     ⚠️ 这里传 `crit: false` 是为了印"不暴击时的实际伤害"；
            //        暴击倍率不进式子，它只在"掷中暴击"那一刻于内部乘上（`RollCrit`）✓
            float dmgStatVal = slot == WeaponSlot.Can ? st.Get(StatId.RangedDamage)
                             : slot == WeaponSlot.Fertilizer ? st.Get(StatId.BlastDamage)
                             : st.Get(StatId.MeleeDamage);
            string dmgStatName = slot == WeaponSlot.Can ? "远程伤害"
                               : slot == WeaponSlot.Fertilizer ? "爆炸伤害" : "近战伤害";
            float baseVal = tool.baseValue;         // ★ 品质那一层乘法已删：基础值就是基础值
            float atkMult = st.Get(StatId.AtkMult);
            float critRate = st.Get(StatId.CritRate);
            float critDmg = st.Get(StatId.CritDmg);
            float shown;
            string f0;
            // ★ v195：**删掉"× 爆炸倍率"那一段**（用户原话："删除爆炸倍率这个参数删掉"）★
            //   现在三把武器印的是**同一条式子**，肥料不再有第二层乘区 ✓
            //   （若这里还印乘区、而公式里没有，卡面就又变成了"承诺引擎不给的东西"）
            {
                shown = DamageResolver.WeaponDamage(baseVal, dmgStatVal, atkMult, false, critDmg,
                                                    t.formula.minDamage);
                f0 = $"伤害 =(基础 {baseVal:0.#} + {dmgStatName} {dmgStatVal:0.#})× 伤害倍数 {atkMult:P0}"
                   + $" = {shown:0.#}";
            }

            // ── ③ 所有属性（**按武器只列它有的**）──
            float cd = tool.cooldown / Mathf.Max(0.05f, st.Get(StatId.AtkSpeed));
            float range = tool.range * Mathf.Max(0.1f, st.Get(StatId.AtkRange));
            float knockCoeff = slot == WeaponSlot.Hoe ? 0.5f : slot == WeaponSlot.Can ? 0.2f : 1f;
            WeaponModifiers m = s.ModsFor(slot);
            var attrs = new System.Collections.Generic.List<string>
            {
                $"暴击率 {critRate * 100f:0.#}%",
                $"暴击伤害 {critDmg * 100f:0.#}%",
                $"击退 {st.Get(StatId.Knockback) * knockCoeff:0.00}",
                $"攻击速度 {cd:0.00}s/次",
                $"攻击范围 {range:0.0}m",
            };
            if (slot == WeaponSlot.Fertilizer)
            {
                float blastRange = RunSession.BlastBaseRadius(tool.baseExplodeRange) * st.Get(StatId.ExplodeRange);
                attrs.Add($"爆炸范围 {blastRange:0.0}m");
                attrs.Add($"充能速度 {cd:0.00}s/次 · 弹药 {s.FertAmmo}/{RunSession.FertAmmoMax}");
            }
            else if (slot == WeaponSlot.Can)
            {
                // ★★ v216：**读真值**，并按道数乘 —— 与 `TickBeam` 扣水走同一个纯函数 ★★
                //   改前读的是本文件里的镜像 `BeamWaterCostPerTick = 10`（真值 20、且不吃道数）
                //   ⇒ 卡面「10/0.5s」而实际「20 × 道数 / 0.5s」✗
                //   判据（本单⑤）：2 道 = 2 × 单道（40）、3 道 = 3 × 单道（60）——
                //   卡面印的就是这个数，不再需要玩家自己乘 ✓
                int perFire = WeaponSystem.BeamWaterCostPerFire(RunSession.BeamAmmoCost, m);
                string jets = m.JetCount > 1
                    ? $"，{m.JetCount} 道 × {RunSession.BeamAmmoCost}"     // 每道同价，不额外加
                    : "";
                attrs.Add($"耗水 {perFire}/{RunSession.BeamAmmoInterval.ToString("0.##")}s"
                          + $"（{(perFire / RunSession.BeamAmmoInterval):0.#}/秒{jets}）");
                if (m.AutoChargePerSecond > 0f) attrs.Add($"自充 {m.AutoChargePerSecond:0.#}/秒");
            }
            else if (m.SwingCount > 1)
            {
                attrs.Add($"挥砍 {m.SwingCount} 刀/次");
            }

            // ── 排版：算总高再**垂直居中**（卡高由 `DrawMenu` 平分，四舍五入会有几像素零头）──
            // ★ v186：公式从两行变一行（暴击那行不印了）⇒ 这里同步 2f → 1f ✓
            const float lineH = 19f;
            int rowsN = Mathf.CeilToInt(attrs.Count / 2f);
            float needH = 5f + 26f + 2f + lineH * 1f + 4f + rowsN * lineH + 4f + 28f;
            float y = card.y + Mathf.Max(4f, (card.height - needH) * 0.5f);
            float fy = y + 28f;
            Txt(new Rect(lx, fy, lw, lineH), f0, MenuTiny);
            float ay = fy + lineH + 4f;
            float colW = lw * 0.5f;
            for (int i = 0; i < attrs.Count; i++)
            {
                int c = i / rowsN, r = i % rowsN;        // 竖着填满左列再填右列 ⇒ 读起来是"一张清单"
                Txt(new Rect(lx + c * colW, ay + r * lineH, colW, lineH), attrs[i], MenuTiny);
            }
            // ── ④ 6 个插件槽（与商店/战斗 HUD **同一个** `DrawPluginSlots` ✓）──
            DrawPluginSlots(s, slot, new Rect(lx, ay + rowsN * lineH + 4f, lw, 28f));
        }

        private void DrawMenu(RunSession s)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, _virtualW, _virtualH), White);
            GUI.color = prev;

            float pad = 16f, top = 70f, bottom = _virtualH - 40f;
            float h = bottom - top;
            // 属性栏宽 380 —— **与改前逐像素相同**（玩家读得最多的一栏，这次不动它 ✓）
            float statsW = 380f;
            float rightX = _virtualW - pad - statsW;

            // ══════════════════════════════════════════════════════════════════
            // ★★ v214：**退出战斗（回到主城）** ★★
            //   用户原话："战斗模式的菜单增加退出战斗（回到主城）"
            //
            //   ── 为什么放在**面板上方那条空带**（y 0…`top`）而不是武器卡标题行 ✗ ──
            //     标题行已经被「继续游戏（Esc）」与「重新开始（Shift+R）」占满
            //     （`weaponPanel.xMax-314 … xMax`，左边只剩 62px 且紧挨标题「武器（三把）」）；
            //     再塞一个"退出"要么压字、要么把两个 v184 定稿的按钮挪位置 ✗
            //     面板从 `top = 70` 才开始画 ⇒ 上面这条带子**本来就是空的**
            //     （暂停时连按键说明都不画：`DrawKeyHelp` 在 `combatHud && !s.Paused` 里）
            //     ⇒ 放这里既不挪动任何既有元素，又正好落在"暂停菜单顶部"这个最显眼处 ✓
            //
            //   ── 二次确认（用户要求；与登录侧「返回开始界面」同一形态）──
            //     第一次点：文本变「确认退出？再点一次」，**不切场景**；
            //     第二次点：置 `PendingExitToTown` ⇒ `GameBootstrap` 清局 + `LoadScene("Town")` ✓
            //     判定不写在这里 —— 按钮与桥入口共用 `GameBootstrap.HandleExitToTown` ✓
            {
                var exitBtn = new Rect(pad, top - 46f, 260f, 34f);
                if (Btn(exitBtn, ExitToTownArmed ? "确认退出？再点一次" : "退出战斗（回到主城）"))
                    PendingExitToTown = true;
                if (ExitToTownArmed)
                    Txt(new Rect(exitBtn.xMax + 10f, exitBtn.y + 7f, 420f, 22f),
                        "再点一次就回主城（本局结束、进度不保留；主城进度不受影响）", _small);
            }

            // ══════════════════════════════════════════════════════════════════
            //  ★ v184：**武器抬成左整列**（用户第 ① 条"武器栏目放大占比"）★
            //
            //  ── 改前 ✗（v181：武器只是页面底部一条 994×134 的横栏）──
            //    左栏 380（菜单按钮 124 ＋ 道具 380×416）／中栏 598×548（植物 3×2）
            //    ／右栏 380×690（属性）／武器 = 994×134
            //    ⇒ 武器只占整页 **11.7%**，而内容只有"伤害 5 · 范围 3.6m · 攻速 0.74s"一行
            //    ⇒ "伤害公式 + 所有属性"在这个形状里**放不下** ✓（所以不是"忘了画"）
            //
            //  ── 改后 ✓ ──
            //    武器 **610×690**（宽 42.9%／面积 = 改前的 3.2 倍）
            //    道具 368×269（右列上）／植物 368×405（右列下，3×2 仍一屏全见）
            //    属性 **380×690 一个像素没动** ✓
            //    原来那块 380×124 的"菜单"面板**取消** —— 两个按钮并进武器卡的标题行，
            //    省下来的整块面积给武器 ✓
            // ══════════════════════════════════════════════════════════════════

            // ── 左整列：武器（每把一张卡：名字 + 伤害公式 + 全属性 + 6 槽）──
            float weaponW = 610f;
            Rect weaponPanel = new Rect(pad, top, weaponW, h);
            Panel(weaponPanel);
            Txt(new Rect(weaponPanel.x + 12, weaponPanel.y + 4, 220, 28), "武器（三把）", _bold);
            if (Btn(new Rect(weaponPanel.xMax - 156, weaponPanel.y + 6, 144, 26), "继续游戏（Esc）")) PendingResume = true;
            if (Btn(new Rect(weaponPanel.xMax - 314, weaponPanel.y + 6, 150, 26), "重新开始（Shift+R）")) PendingRestart = true;

            float cardsTop = weaponPanel.y + 38f;
            float cardsH = weaponPanel.yMax - 10f - cardsTop;
            const float cardGap = 8f;
            float cardH = (cardsH - cardGap * 2f) / AllWeapons.Length;
            for (int i = 0; i < AllWeapons.Length; i++)
                DrawMenuWeaponCard(s, AllWeapons[i],
                    new Rect(weaponPanel.x + 10f, cardsTop + i * (cardH + cardGap), weaponW - 20f, cardH));

            // ── 右列：道具（上）＋ 植物（下）──
            float sideX = pad + weaponW + pad;
            float sideW = rightX - pad - sideX;
            // 植物 3×2 要放得下六项内容（v196：原为七项）⇒ 每格 ≥ 117 高 ⇒ 整块 405；剩下的高度给道具
            const float plantH = 405f;
            Rect itemPanel = new Rect(sideX, top, sideW, Mathf.Max(150f, h - plantH - pad));
            Rect plantPanel = new Rect(sideX, itemPanel.yMax + pad, sideW, plantH);
            // ── 右列上：道具（★ v184：**显示道具图标** ★）──
            Panel(itemPanel);
            // 详情框（点击道具图标后显示）—— 先算高度，tiles 区让开它
            // v51：描述与效果都可能超一行，这里用允许换行的样式并预留两行高度
            const float rowH = 20f;
            float descH = rowH * 2f;
            // ★ v184：124 → 106 ★
            //   原式 `14 + 4×20 + 30 = 124`（旁边那句"= 84"是旧注释写错了值 ✗ 已删）。
            //   124 里有 18px 是纯留白：框内真正用到的只有
            //   `+4 标题 22` ＋ `+28 描述 2 行 40` ＋ `+68 效果 2 行 40` = **106** ✓
            //   收掉这 18px ⇒ 道具格从 103 高变成 121 高 ⇒ **两整行**（2×56 + 8 = 120）不再被切 ✓
            const float itemDetailH = 106f;
            Txt(new Rect(itemPanel.x + 12, itemPanel.y + 6, itemPanel.width - 20, 28),
                $"道具（{s.ItemStacks.Count}）", _bold);
            Rect itemView = new Rect(itemPanel.x + 8, itemPanel.y + 34, itemPanel.width - 16,
                Mathf.Max(56f, itemPanel.height - 34f - itemDetailH - 8f));
            var itemIds = new System.Collections.Generic.List<string>(s.ItemStacks.Keys);
            const int perRow = 4;
            float tileW = (itemView.width - 24f) / perRow - 6f;
            const float tileH = 56f;
            int rows = Mathf.CeilToInt(itemIds.Count / (float)perRow);
            float ih = Mathf.Max(itemView.height, rows * (tileH + 8f) + 10f);
            _clipDepth++;   // v178：同上
            _itemScroll = GUI.BeginScrollView(itemView, _itemScroll, new Rect(0, 0, itemView.width - 18, ih));
            for (int i = 0; i < itemIds.Count; i++)
            {
                ItemDef def = TableDatabase.GetItem(itemIds[i]);
                if (def == null) continue;
                int col = i % perRow, row = i / perRow;
                Rect tile = new Rect(col * (tileW + 6f), row * (tileH + 8f), tileW, tileH);
                Color pv = GUI.color;
                GUI.color = _itemSelected == itemIds[i] ? ItemTileSel : SlotBg;
                GUI.DrawTexture(tile, White);
                GUI.color = QualityColor((int)def.Grade);
                GUI.DrawTexture(new Rect(tile.x, tile.y, tile.width, 4f), White);   // 顶部品质条（与改前一致）
                GUI.color = pv;
                // 点击区**先画**（与改前同一个顺序）：按钮皮肤是亮底，图标压在上面才看得见 ✓
                if (Btn(new Rect(tile.x + 2f, tile.y + 4f, tile.width - 4f, tile.height - 6f), ""))
                    _itemSelected = _itemSelected == itemIds[i] ? null : itemIds[i];
                // ★ v184：图标（`ItemIconOf` 走**缓存**；缺图时 `DrawOfferTexture` 自己退回品质色块 ✓）
                //   30px：道具格 76×56，要装"图标 + 名字"两行；品质由**顶部色条 + 图标光晕**表达，
                //   （点击后的详情框里仍写全"史诗　幸运四叶 ×1" ⇒ 没有丢信息 ✓）
                float iconS = Mathf.Min(30f, tileW - 12f);
                Rect icon = new Rect(tile.x + (tile.width - iconS) * 0.5f, tile.y + 3f, iconS, iconS);
                DrawOfferTexture(icon, QualityColor((int)def.Grade), ItemIconOf(def.Id));
                // 名字：按钮是**亮底** ⇒ 用 `Tiny`（12px 深棕，与改前"普通"同一档字 ✓）
                // ★ v261 防超框：写死 14 高 < `Tiny` 的**实测行高**（12px 系统字 ≈ 16~17 虚拟 px）
                //   ⇒ 下行笔画（"果""叶"这类带下缘的字）被切；宽度仍交给格子（超宽按格子截，
                //   那是设计上"名字比格子长"的正常取舍），但**高度改成实测**，只可能横向不到边，
                //   不会上下被斩 ✓
                string itemName = L10n.S(def.Name ?? "");
                float nameH = Mathf.Max(14f, Tiny.CalcSize(new GUIContent(itemName)).y);
                Txt(new Rect(tile.x + 2f, icon.yMax + 1f, tile.width - 4f, nameH), itemName, Tiny);
                // 右上角数量：★ v261 先量再定宽 ★
                //   旧版写死 `(xMax-34, y+3, 32, 14)`："×99" 勉强、三位数**一定出框**；
                //   14 高又在 12px 字的行高之下 ⇒ 上下也被切。
                //   现在宽度/高度都按 `CalcSize` 实测，并**贴着格子右缘往回长** ⇒ 数字变多只向左长 ✓
                string itemCount = L10n.S($"×{s.ItemStacks[itemIds[i]]}");
                Vector2 cntSize = Tiny.CalcSize(new GUIContent(itemCount));
                float cntW = Mathf.Max(24f, cntSize.x + 4f);
                float cntH = Mathf.Max(14f, cntSize.y);
                Txt(new Rect(tile.xMax - 4f - cntW, tile.y + 2f, cntW, cntH), itemCount, Tiny);
            }
            if (itemIds.Count == 0) Txt(new Rect(0, 0, itemView.width, 22f), "（还没有道具）", _small);
            _clipDepth--;   // v178：离开裁剪区
            GUI.EndScrollView();

            Rect detail = new Rect(itemPanel.x + 8, itemPanel.yMax - itemDetailH - 6f,
                itemPanel.width - 16, itemDetailH);
            Panel(detail);
            ItemDef sel = string.IsNullOrEmpty(_itemSelected) ? null : TableDatabase.GetItem(_itemSelected);
            if (sel == null)
                Txt(new Rect(detail.x + 8, detail.y + 6, detail.width - 16, 22), "点击道具图标查看详情", _small);
            else
            {
                float avail = detail.width - 16f;
                Txt(new Rect(detail.x + 8, detail.y + 4, avail, 22),
                    $"{s_rarityName(sel.RarityTier)}　{sel.Name} ×{s.ItemStacks[_itemSelected]}", _bold);
                // ★ v198：**数值栏在前、功能栏在后** ★
                //   用户原话："重新定义道具的数值展示栏，放在功能栏后，提前位置" ⇒
                //   改前：功能（desc）在 +28、数值（effect）在 +28+descH ✗
                //   改后：`ItemStatLine`（数值 ＋ 持有上限）在 +28、功能在 +28+descH ✓
                //   两处（商店商品详情 / 这里）读的是**同一个 `ItemStatLine`** ⇒ 不会出现两种说法 ✓
                //   ⚠️ 高度没变（`descH` 两行 40 ＋ 标题 22 ⇒ itemDetailH 106 装得下）✓
                string stat = ItemStatLine(sel);
                string desc = "功能：" + sel.Desc;
                RowsFor(stat, avail, "菜单道具数值");
                RowsFor(desc, avail, "菜单道具功能");
                Txt(new Rect(detail.x + 8, detail.y + 28, avail, descH), stat, WrapSmall);
                Txt(new Rect(detail.x + 8, detail.y + 28 + descH, avail, descH), desc, WrapSmall);
            }

            // ── 右列下：植物 3×2（与主界面**同一个** `DrawPlantCell`，六项内容一项不少 ✓）──
            //   ★ v196：改这里之前是"七项"，第 7 项「特殊功能」下线后是 6 项 ✓
            //     格子高度**没动**（`plantH = 405`，每格 115.7）—— 改前第 4 行那个
            //     32px 折行框本来就超出 6px，改后 4 行 × 17px 收在格子里 ✓
            Panel(plantPanel);
            Txt(new Rect(plantPanel.x + 12, plantPanel.y + 6, plantPanel.width - 20, 28),
                $"植物（{s.Bag.OccupiedCount}/{s.Bag.SlotCount}）　一株独占一列", _bold);
            Rect seedView = new Rect(plantPanel.x + 8, plantPanel.y + 38,
                plantPanel.width - 16, plantPanel.height - 46f);
            // ★ v184：**2 列 × 3 行**（改前是 3 列 × 2 行）★
            //   为什么必须换方向 ✗ 不是审美：
            //     右列只有 368 宽 ⇒ 3 列时每格 113、文字区只剩 103，
            //     而"Lv 1/3　收获 ×1.00"在 15px 下约 100px ⇒ `OnDarkWrap`（wordWrap）**触发换行**，
            //     两行挤进 17px 高的框里 = **上下叠字**（首次 after 图就是这样，取证见交付）
            //   ★ v196：那一行现在是「等级　1 级」（约 75px）⇒ 3 列也放得下了，
            //     但**不改回 3 列** —— 2×3 与主界面 `DrawPlantPanel` 同方向这件事比多一列更值 ✓
            //   2 列 ⇒ 每格 173、文字区 163 ⇒ 一行放得下 ✓
            //   ⇒ 6 格仍是**一屏全见**：3 行 × 116 + 2×6 间隙 + 标题 38 = 405 ✓
            //   ⇒ 与主界面 `DrawPlantPanel`（同样是 2 列 × 3 行）同方向 ✓
            const float gpad = 6f;
            float gw = (seedView.width - gpad) / 2f;
            float gh = (seedView.height - gpad * 2f) / 3f;
            for (int i = 0; i < s.Bag.SlotCount && i < 6; i++)
                DrawPlantCell(s, s.Bag[i],
                    new Rect(seedView.x + (i % 2) * (gw + gpad), seedView.y + (i / 2) * (gh + gpad), gw, gh), i);

            // 右：属性（主要 / 次要 分开，可滚动）—— **整列与改前逐像素相同**
            Panel(new Rect(rightX, top, statsW, h));
            DrawStatsColumns(s, rightX, top, statsW, h, "属性");
        }
        public void ConsumeInto(ref FrameInput input)
        {
            // ══════════════════════════════════════════════════════════════════════
            //  ★ v182：抽卡确认 —— **先播融合动画，动画播完才 ApplyCard** ★
            //
            //  ── 为什么拦在这里，而不是拦在确认按钮里 ✗ ──
            //    全工程会置 `RewardPickConfirmed = true` 的只有三处（grep 取证）：
            //      ① 本文件 `DrawLevelReward` 的确认按钮 → 只写 `PendingRewardPick`，下一帧才进 input
            //      ② `GameBootstrap.ReadInput` 的空格键（`:255-259`）—— **直接写 input，绕过按钮**
            //      ③ `RunBot.cs:203`（自动跑图）—— **同上**
            //    ②③ 压根不经过 `DrawLevelReward` ⇒ 只在按钮里挂动画的话，
            //    键盘与 bot 会把牌一口吃掉、一帧动画都不播 ✗
            //    而 `GameBootstrap.Update` 里的 `Hud.ConsumeInto(ref input)`（`:126`）是这三条路的
            //    **唯一汇合点** ⇒ 把延后放在这里，三条路就自动都有动画了 ✓
            //
            //  ── 这四段的顺序不能换 ──
            //    ① 先交"动画已播完"的那次确认（否则会被 ② 当成本帧的新确认吞掉）
            //    ② 再按"动画进行中"锁输入
            //    ③ 原有的一次性输入照旧搬运（与改前逐行相同）
            //    ④ 最后看本帧是否来了一次新确认 ⇒ 起飞
            // ══════════════════════════════════════════════════════════════════════
            bool rewardUi = RewardUiLive();
            // ★ v185：召唤 / 合并也进锁定判据（这两段是新增的，必须一起锁，不然召唤期间还能点）
            bool summoning = rewardUi && Summoning;
            bool merging = rewardUi && Merging;
            bool appearing = rewardUi && _cardAppearT < CardAppearTotal(_uiScope.RewardCards.Count);

            // ── 只读探针：把"**入帧那一刻**"的状态抓下来 ──
            //   ① 会把 `_fuseReady` 清成 false，跑到末尾再读就永远看不到"它本来是 true" ✗
            bool probe = RewardProbe && rewardUi;
            bool pReadyIn = _fuseReady;
            bool pFusingIn = Fusing;
            float pFuseTIn = _fuseT;
            int pFusePickIn = _fusePick;
            bool pLockedIn = _lastLocked;
            float pSummonTIn = _summonT;              // v185：召唤计时（>SummonDur 就该解锁）
            float pMergeTIn = _mergeT;                // v185：合并计时（<0 = 没在合并）

            // ── ① 取货：**入帧只判断一次** ──
            //   判据（动画是否播完）必须在这里读 —— 下一段 ② 会改动 `_fuseReady` 的兄弟状态，
            //   而"这次确认要不要交出去"只应该由**本帧开头**的事实决定 ✓
            //   ⚠️ 真正的**写入**放到 ② 之后（见 ①′）✗ 不是这里 ✗ —— 原因写在 ①′ 的注释里 ✓
            bool deliverPick = _fuseReady && rewardUi;
            if (deliverPick) _fuseReady = false;

            // ② 动画期间输入锁定：牌不可选、确认不生效、重抽不生效
            if ((Fusing || appearing || summoning || merging) && rewardUi)
            {
                input.RewardSelectSet = false;
                input.RewardSelect = -1;
                PendingRewardSelect = -1;
                if (Fusing)
                {
                    input.RewardPickConfirmed = false;
                    input.RewardPick = -1;
                    PendingRewardPick = -1;
                    PendingRewardReroll = false;
                }
            }
            bool pAfterLock = input.RewardPickConfirmed;      // 只读探针：② 之后确认还在吗

            // ── ①′ 交付：**必须排在 ② 之后** ★ v183 修的 bug ★ ──
            //
            // 症状（用户报的"抽卡界面退出被卡住"）：面板永远关不掉，卡片循环"飞一次、重来一次"。
            // 病因：① 写在 ② **之前** ⇒ ② 的 `if (Fusing) input.RewardPickConfirmed = false;`
            //   把 ① 刚写进去的那次确认**当成"动画进行中的新确认"吞掉** ✗
            //   而 `_fuseReady` 在同一帧已经被 ① 清成 false ⇒ **这次确认彻底丢失** ✓
            //   ⇒ `TickLevelReward` 永远收不到 `RewardPickConfirmed` ⇒ 面板永不关闭 ✓
            //   ⇒ 1.4 秒后融合兜底 `CancelFuse()` 解锁 ⇒ 玩家/机器人再点一次 ⇒ **同样的丢失** ⇒ 死循环 ✗
            // （探针读数见 v183 的交付记录：`fuseReady(入帧)=True ②后仍在=False 最终RewardPickConfirmed=False`）
            //
            // 修法：**读判据在 ② 之前，写结果在 ② 之后** —— 两件事拆开，谁也不用迁就谁 ✓
            //   顺手 `CancelFuse()`：动画已经播完且结果已交出，融合状态到此**有事可做地**结束，
            //   不再依赖"1 秒后兜底取消"（那条兜底只该处理异常，不该承担正常路径）✓
            //   ④ 用 `!deliverPick` 排除本帧这次交付 —— 否则它会把它当成"新来的确认"再吞一次 ✗
            if (deliverPick)
            {
                input.RewardPickConfirmed = true;
                input.RewardPick = _fusePick;
                CancelFuse();
            }

            if (PendingInitialChoice >= 0) input.InitialChoice = PendingInitialChoice;
            if (PendingBuyOffer >= 0) input.BuyOffer = PendingBuyOffer;
            if (PendingReroll) input.Reroll = true;
            if (PendingSell >= 0) input.SellSlot = PendingSell;
            if (PendingNextWave) input.Skip = true;
            if (PendingSlotClick >= 0) { input.SlotClicked = true; input.ClickedSlot = PendingSlotClick; }
            PendingInitialChoice = -1;
            PendingBuyOffer = -1;
            PendingReroll = false;
            PendingSell = -1;
            PendingNextWave = false;
            PendingSlotClick = -1;
            if (PendingSlotMerge >= 0) { input.SlotMergeClicked = true; input.SlotMerge = PendingSlotMerge; PendingSlotMerge = -1; }
            if (PendingOfferLock >= 0) { input.OfferLockToggled = true; input.ToggleOfferLock = PendingOfferLock; PendingOfferLock = -1; }
            if (PendingRewardSelect >= 0) { input.RewardSelectSet = true; input.RewardSelect = PendingRewardSelect; PendingRewardSelect = -1; }
            if (PendingRewardPick >= 0) { input.RewardPickConfirmed = true; input.RewardPick = PendingRewardPick; PendingRewardPick = -1; }
            if (PendingRewardReroll) { input.RewardReroll = true; PendingRewardReroll = false; }

            // ④ 本帧来了一次新确认 ⇒ 起飞。这一次确认**不直接**交给 RunSession：
            //    先把它记成 `_fusePick`，0.36s 飞行 + 0.12s 白闪之后的 ①′ 才真正发出去 ✓
            //    `!deliverPick`：本帧若已在 ①′ 交付过，那个 `true` 是**结果**不是**新输入** ✗
            if (rewardUi && !Fusing && !appearing && !deliverPick && input.RewardPickConfirmed)
            {
                if (input.RewardPick >= 0 && input.RewardPick < _uiScope.RewardCards.Count)
                {
                    _fusePick = input.RewardPick;
                    _fuseT = 0f;
                }
                input.RewardPickConfirmed = false;   // 一律吞掉：真要确认由 ①′ 在动画播完后发
                input.RewardPick = -1;
            }

            // ── 只读探针：一帧一行（`RewardProbe` 默认关，开着也不写任何字段）──
            //   这一行就是"卡住那一刻"的全部证据：面板开着、动画状态、以及
            //   **交给 RunSession 的最后那个 `RewardPickConfirmed`** 到底是真是假 ✓
            if (probe)
            {
                RunSession s = _uiScope;
                Vector3 tp = s.TruckWorldPos;
                Log.Info("[RewardProbe] 面板开"
                    + " | summonT=" + pSummonTIn.ToString("0.000")
                    + " mergeT=" + pMergeTIn.ToString("0.000")
                    + " appearing=" + appearing
                    + " Fusing=" + pFusingIn
                    + " fuseT=" + pFuseTIn.ToString("0.000")
                    + " fusePick=" + pFusePickIn
                    + " fuseReady(入帧)=" + pReadyIn
                    + " locked=" + pLockedIn
                    + " ②后仍在=" + pAfterLock
                    + " ①保管=" + deliverPick
                    + " 最终RewardPickConfirmed=" + input.RewardPickConfirmed
                    + " | Phase=" + s.Phase
                    + " 游戏时间=" + s.Clock.GameTime.ToString("0.00") + "s"
                    + " 车=(" + tp.x.ToString("0.000") + "," + tp.z.ToString("0.000") + ")"
                    + " 车状态=" + s.Truck.Current);
            }
        }
    }
}
