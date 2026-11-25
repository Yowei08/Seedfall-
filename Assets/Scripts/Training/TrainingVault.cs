using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Seedfall
{
    /// <summary>
    /// ★★ 训练场**仓库面板**（v305 重做：三栏布局）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、用户原话（本文件的验收口径）
    /// ══════════════════════════════════════════════════════════════════════
    ///   「仓库 UI 重做，高级，左侧栏为所有游戏的物品道具，分为道具、植物、插件，
    ///     每一个都单独显示其图标和名称；中间上部为物品详细栏，选中的物品会详细展示其信息；
    ///     下方为道具背包，展示自己拥有的道具、可以卸下；右侧上部为武器和插件栏，可以卸下插件；
    ///     下方为植物栏、可以卸下；选择的植物自动种植在耕地上」
    ///
    ///   逐句对到实现（**每句只有一个落点**）：
    ///     · 左侧栏三分类 + 图标 + 名称   ⇒ `DrawLeft`（图标走 `IconOf`，见 五）
    ///     · 中上详情（完整 desc）         ⇒ `DrawDetail`（`_wrap` 开 wordWrap + 可滚）
    ///     · 中下道具背包 + 每行卸下       ⇒ `DrawBag`
    ///     · 右上武器 / 插件（3 把 × 6 格） ⇒ `DrawWeapons`（点插件格 = 卸下）
    ///     · 右下植物栏 + 卸下             ⇒ `DrawPlants`
    ///     · 选中植物自动种植             ⇒ `SelectOrEquip` → `SowOnPlot`（见 四）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、布局数值表（交付报告 ④；虚拟分辨率 1280 × 760）
    /// ══════════════════════════════════════════════════════════════════════
    ///   缩放：`k = clamp(min(Screen.height/900, Screen.width/1320), 0.55, 2.2)`，
    ///         `GUI.matrix = TRS(居中偏移, 单位, (k,k,1))` ⇒ 面板内所有坐标都是**虚拟像素**，
    ///         字号 / 行高 / 图标尺寸**一个数都不用跟着窗口变**（这是本轮"高级"的地基）
    ///        （h/900 与主城 / 登录 HUD 同口径；再按宽度兜一道，避免小窗口里横向溢出）
    ///
    ///   面板 (0,0,1280,760)；顶栏 y ∈ [0,56]；内容区 y ∈ [64,712]；底部状态条 y ∈ [714,760]
    ///     ★ v308 重排：区块内容左边距统一 `PadX = 16`；四个区块的标题/副标题行距统一
    ///        （标题 8..32、副标题 34..52、内容 58 起）
    ///     左栏  ( 20, 64, 306, 648)：页签 3×(87×30) @ (36 / 129 / 222, 72)
    ///                                列表视口 (36,112,274,588)；行高 40
    ///     中上  (340, 64, 600, 372)：★★ v309 分区重做 ★★
    ///                                图标 (356,72,96,96) / 名称 (462,68,462,28)
    ///                                档位 (462,96,462,18) / 状态 (462,114,462,18)
    ///                                按钮行 y=136 h=26（装上 x=462 w=96 / 卸下 x=566 w=80）
    ///                                "将装到：…" (658,140,266,20)  ← 替代改前那 3 颗武器按钮
    ///                                分隔 y=168
    ///                                「数　值」(356,172,568,16)
    ///                                数值视口 (356,190,568,102)：**一条一行**，6 行 × 17，超出滚动
    ///                                分隔 y=296
    ///                                「说　明」(356,300,568,16)
    ///                                desc 视口 (356,320,568,106)（改前 166；说明区照样可滚 ⇒ 不裁字）
    ///     中下  (340,448,600,264)：标题 (356,456,420,24)；副标题 y=482 h=18
    ///                                视口 (356,506,568,194)；行高 44
    ///     右上  (954, 64, 306, 372)：标题 (970,72,274,24) / 副标题 (970,98,274,18)
    ///                                三把武器块各 (970, 122|226|330, 274, 96)
    ///                                格 88×32，3 列 × 2 行（x = 970 / 1063 / 1156）
    ///     右下  (954,448,306,264)：标题 (970,456,274,24)；副标题 y=482 h=18
    ///                                视口 (970,506,274,194)；行高 44
    ///     底条  (0,714,1280,46)：半透明深色底 + 顶沿 1px 细金线；正文 (16,727,1248,20)
    ///
    ///   横向账：20 + 306 + 14 + 600 + 14 + 306 + 20 = 1280
    ///   纵向账：64 + 372 + 12 + 264 = 712（顶栏 56 + 内容 648 + 间隙 2 + 底条 46）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、装备 / 卸下全部走**战斗自己的装配入口**（"所有内容都生效"的唯一原因）
    /// ══════════════════════════════════════════════════════════════════════
    ///   道具（191 件）：`RunSession.ApplyItem(ItemDef)`（唯一"买到手"入口）
    ///     卸下必须三步：`ItemStacks.Remove(id)`（**删键，不能写 0**：`ReapplyItems` 里是
    ///     `Mathf.Max(1, kv.Value)`）+ `Progress.Stats.RemoveModifiersFrom("item:"+id)`
    ///     （**必须先按来源撤一次**：`ReapplyItems` 的撤销步骤遍历的是**当前字典里的键**，
    ///     键已删的那件永远撤不掉，会留一条幽灵增益）+ `ReapplyItems` / `SyncItemRules` /
    ///     `ProgMechanics.ApplyCapsToTable`。见 `UnequipItem` 的三步注释。
    ///   插件（55 件）：`WeaponSystem.Install(PlayerGoods, PluginAsset, WeaponSlot, out reason)`
    ///     （唯一装配入口：Fits / 互斥 / 叠加 / 每把武器 6 槽全在里面）；
    ///     生效侧 `WeaponSystem.Aggregate` **每次现算无缓存** ⇒ 装上当帧生效、卸下当帧失效。
    ///   植物（51 种）：`SeedBag.AddSeed(def, null, def.RarityTier, out why)` 入袋
    ///     + **`RunSession.AutoPlantSeed(def, level)`（`RunSession.cs:3865`）种到耕地**（见 四）。
    ///   能力边界（如实记）：种子袋 6 格（= 田地列数）、每把武器 6 个插件位、道具单件最多叠 20 层；
    ///     这三条都是**战斗自己的规则**，本文件不改。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、"选中植物 ⇒ 自动种到耕地"走的是哪条战斗 API（交付报告 ⑥）
    /// ══════════════════════════════════════════════════════════════════════
    ///   走的正是**商店买到种子**那一条，一行代码都没有另写：
    ///     `RunSession.cs:2223`（商店买种）`s.AutoPlantSeed(o.Seed, o.Level);`
    ///     `RunSession.AutoPlantSeed(SeedDef, int level)` 定义在 `RunSession.cs:3865`：
    ///       遍历 `World.Farm.Slots` 找第一块 `State == Empty` 的地块
    ///       → `plot.BeginPlant(T.farm, def, SeedSlot.WaterMaxFor(def, level), def.WaterDrain, level)`
    ///       → `SetPrompt(...)` / `Telemetry.Emit("auto_plant", ...)`
    ///     而 `FarmPlot.BeginPlant`（`FarmGrid.cs:248`）在**列首**种下时会把同列其余行一起补齐
    ///     （`FillColumnFrom`）⇒ "一次调用 = 占一列"，与"种子袋 1 格 = 1 列"完全同构。
    ///   本文件在 `SelectOrEquip` 里按次序做三件事：入袋 → `AutoPlantSeed` → 刷新右栏。
    ///   卸下（`UnequipSeed`）**只把种子从袋里拿走**，不碰地里的作物：地里那一列由**战斗自己**在
    ///   下一次 `SyncPlotsToSeeds` 释放（`ownedBySeed` 里没有它 ⇒ owned = 0 ⇒
    ///   `plot.RemoveGrowing()`，`RunSession.cs:5416-5431`）。本文件不复制这条规则。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、图标解析与缓存（交付报告 ⑤）
    /// ══════════════════════════════════════════════════════════════════════
    ///   资源名规则与 `ImGuiHud.IconResOf`（`ImGuiHud.cs:4160`）**完全一致**：
    ///     道具 `icon_item_<ItemDef.Id>` / 植物 `icon_plant_<SeedDef.Id>` / 插件 `icon_plugin_<PluginAsset.id>`
    ///   加载键 = `"Art/UI/" + 资源名`（`Assets/Resources/Art/UI/*.png`，实测 179 / 41 / 55 张）。
    ///   缓存分两层，**都是懒创建**：
    ///     ① 资源名 `string`：左栏按表下标建三张 `string[]`；背包 / 右栏按 id 走三张 `Dictionary`
    ///        ⇒ 绘制时**一次字符串拼接都不做**
    ///     ② `Texture2D`：`Dictionary<string,Texture2D>`，`Resources.Load` 只走一次；
    ///        **取不到也存 null**（缺图不必每帧再查）⇒ 退化成品阶色块（`DrawIcon` 的兜底色）。
    ///   两处 `new` 都不在静态字段初始化器里（`TrainingBootstrap.Mpb` 那段事故的纪律）。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 六、每帧分配 ≈ 0 的做法
    /// ══════════════════════════════════════════════════════════════════════
    ///   所有"会变的字符串"都进**版本号缓存**：任何装备 / 卸下 / 换页 / 换选中都会 `Touch()`
    ///   把 `_rev` 加一，下一帧发现版本变了才重建（每帧只做 `int` 比较）：
    ///     `_rowName/_rowSub/_rowMark`（左栏三类列表）、`_bagItemIdx/_bagItemStacks/_bagItemLabel`、
    ///     `_plantSlots/_plantLabel`、`_dName/_dGrade/_dState/_dNum/_dNumCount/_dDesc/_descH`（详情）、
    ///     `_weapLabel`（三把武器的 n/6）、`_headCount`、`_goldText`、三张页签文字。
    ///   列表本身走**定长复用缓冲**（`int[]` / `string[]`），不每次 new。
    ///   唯一的逐帧三角函数在提示动画里（`TrainingWarehouse.DrawHint`），与 UI 无关。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 七、为什么它对正常战斗**零影响**
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① 本组件**只由 `TrainingBootstrap.Awake` 创建**（`TrainingBootstrap.cs:187`）
    ///      ⇒ `Training.unity` 之外**不存在**它 ⇒ 战斗场景里 `Update` / `OnGUI` 一行都不跑
    ///   ② `Update` / `OnGUI` 第一行 `if (!TrainingMode.Active) return;`（双保险）
    ///   ③ 本文件**不写任何战斗数据**，除非用户在这个面板里点了条目；唯一的自动写入是 `Refill`，
    ///      整段被 `TrainingMode.WaterAlwaysFull` / `AmmoAlwaysFull` 包着，两者初值 false
    ///   ④ 战斗侧只多了一行**纯插入**（`GameBootstrap.ReadInput`
    ///      `if (TrainingVault.ConsumesMouse) input.Fire = false;`），
    ///      而 `ConsumesMouse => TrainingMode.Active && IsOpen` 在战斗场景里第一项就是 false
    ///      ⇒ `input.Fire` 保持上一行的值 ⇒ **行为不变**（逐条论证见交付报告 ⑦）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 八、★ v310：左栏**排序 / 分组**，以及"显示序 → 表下标"的唯一换算
    /// ══════════════════════════════════════════════════════════════════════
    ///   用户原话：「为仓库增加排序，按照品质排序，插件按照所属武器分类」。
    ///
    ///   ── 三条排序规则（全部**稳定**、可复现，一次随机数都不用）──
    ///     · 道具 / 植物：品质从高到低（S → A → B → C → D）；
    ///       同品质**保持表内原顺序**（插入排序天然稳定：比较用严格小于，相等就不动）
    ///     · 插件：先按**所属武器**分组（锄头 → 水壶 → 肥料 → 通用/多武器，见 `PluginGroupOf`），
    ///       组内按品质从高到低；同组同品质内仍保持表内原顺序
    ///     · 空条目（表里 null）沉到所在组末尾（品质按 −1 算，不参与"高到低"的正序）
    ///
    ///   ── 分组怎么显示：**插分组标题行**（而不是每行副标题前加武器标记）──
    ///     插件页在每组第一行之前插一条 24px 的标题行（「锄　头 · 10 件」）。
    ///     选它的理由：副标题里本来已经有"适用 锄头"，但那只是**逐行重复**，读不出"组从哪开始"；
    ///     28 件连成一片时，"我要找水壶的"只能一行行扫。标题行给的是**边界 + 数量**，
    ///     不重复任何一行的字。代价是 3 条标题共 72px 的额外内容高度（视口本来就能滚，一行都不裁）。
    ///
    ///   ── 最容易出错的那一步：**显示序 ≠ 表下标** ──
    ///     排序后"第 i 行"不再等于"表的第 i 条"。点了 A 却装上 B，就错在这里。
    ///     本文件的处理是**只保留一个换算点**，而不是在每个按下标取数的地方各转一次：
    ///       `_rowIdx[i]`   = 第 i 行来自表的第几条（分组标题行 = −1）  ← 行模型里的唯一真相
    ///       `_rowY[i]`     = 第 i 行的顶部 y（含标题行矮一截的高度差）
    ///       `_rowTotal`    = 内容总高（滚动视口照它算）
    ///       `OrderOf(c,i)` = 显示序 → 表下标（`EnsureOrder` 里排好的定长数组）
    ///     于是**选中 / 详情 / 上装 / 卸下全部活在"表下标"空间**：
    ///       `_selItems / _selPlants / _selPlugins`（`SelOf` / `SetSel`）存的是**表下标**而不是行号
    ///       ⇒ `EnsureDetail` / `DrawDetail` / `BuyOrEquip` / `UnequipSelected` /
    ///         `IconKeyOf` / `TierColorOf` / `DrawBag` 的**语义一个字都没改**（它们本来就吃表下标）。
    ///     ⚠️ 全文件只有 `EnsureRows`（建行模型）与 `DrawLeft`（画行 + 点击）知道"行号"这件事；
    ///        谁将来再加一处"按下标取数据"，必须问一句"我要的是行号还是表下标"。
    ///
    ///   ── v307 那个 `IndexOutOfRangeException` 的修复**没有被破坏** ──
    ///     页签点击仍可能**在本帧**改掉 `_cat`，所以 `DrawLeft` 里页签之后照旧**再调一次**
    ///     `EnsureRows`（内部幂等）；而且 `EnsureRows` 的缓存键新增了**表长**（`_rowLen`）——
    ///     类别变了 / 表长变了 / `_rev` 变了，任一条都重算 ⇒
    ///     "`n` 是新类别的条数、数组还是旧类别的长度"这种越界不可能再出现。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrainingVault : MonoBehaviour
    {
        private const string Tag = "[仓库] ";

        // ══════════════════════════════════════════════════════════════════
        // 面板开关
        // ══════════════════════════════════════════════════════════════════

        /// <summary>面板是否打开（**与 `Phase` 无关** —— 这就是"常驻"的落地方式）。</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// 面板开着 ⇒ 本帧的鼠标左键**不该**再被当成"攻击"。
        /// 战斗侧唯一的读者是 `GameBootstrap.ReadInput` 里那一行纯插入；
        /// 战斗场景里 `TrainingMode.Active == false` ⇒ 恒 false ⇒ 那一行等于 `if (false)`。
        /// </summary>
        public static bool ConsumesMouse => TrainingMode.Active && IsOpen;

        // ══════════════════════════════════════════════════════════════════
        // 布局常量（交付报告 ④ 的数就是这一段）
        // ══════════════════════════════════════════════════════════════════

        private const float PanelW = 1280f;
        private const float PanelH = 760f;
        private const float BodyTop = 64f;

        /// <summary>内容区下边界 = 底部状态条的**上边线**（★ v308：740 → 712，见 `StatusBarY`）。</summary>
        private const float BodyBottom = 712f;

        /// <summary>
        /// 底部状态条（★ v308 新增）：从 <see cref="StatusBarY"/> 一直铺到面板底 760。
        /// 改前那行状态文字是 `new Rect(20, 742, 1240, 16)` —— 贴着面板下沿 2px，悬空、字又淡。
        /// 现在给它一条**自己的底色**（半透明深色 + 顶部一条细金线），文字垂直居中。
        /// </summary>
        private const float StatusBarY = 714f;

        private const float ColLeftX = 20f, ColLeftW = 306f;
        private const float ColMidX = 340f, ColMidW = 600f;
        private const float ColRightX = 954f, ColRightW = 306f;
        private const float TopBlockH = 372f;

        /// <summary>★ v308：448 + 264 = 712 = <see cref="BodyBottom"/>（改前 448 + 292 = 740，会压住状态条）。</summary>
        private const float BotBlockY = 448f, BotBlockH = 264f;

        /// <summary>
        /// ★ v308：**全面板统一的区块内容左边距**（16px）。
        /// 改前是四种：标题 16 / 页签 10 / 列表视口 10 / 插件格 6 ⇒ 四个区块的字左右参差。
        /// </summary>
        private const float PadX = 16f;

        private const float RowHLeft = 40f;
        private const float RowHBag = 44f;

        /// <summary>★ v308：91 → 87 —— 左边距统一成 16 之后，306 − 16×2 = 274 只放得下 3×87 + 2×6。</summary>
        private const float TabW = 87f, TabH = 30f, TabGap = 6f;

        // ══════════════════════════════════════════════════════════════════
        // 面板状态（静态：一关一开不该丢滚动位置与页签）
        // ══════════════════════════════════════════════════════════════════

        private enum Cat { Items = 0, Plants = 1, Plugins = 2 }

        private static Cat _cat = Cat.Items;
        /// <summary>
        /// 三个**表下标**的选择（★ v310：不是行号；`−1` = 还没选过）。
        ///
        /// 为什么默认是 `−1` 而不是 `0`：排序后"表第 0 条"可能排在列表中间（比如它是 D 档），
        /// 那样一打开面板就是"高亮在中间某行"的怪样子。`−1` 让 `SelOf` 落到
        /// **排序后的第一行**（品质最高的那件）—— 一打开，选中的就是列表最上面那一条。
        /// </summary>
        private static int _selItems = -1, _selPlants = -1, _selPlugins = -1;

        // ★ v308：`_slotPick`（"插件目标武器"，改前由详情栏那 3 颗按钮写）**已删除** ★
        //   用户原话："删掉详细栏中（锄头、水壶、肥料）的选择按键"。
        //   目标武器改为**纯自动**：`PreviewTargetSlot` 按 锄头 → 水壶 → 肥料 的固定顺序
        //   挑第一把"适配且还有空格位"的武器（与 `PlayerGoods.FreeSlotsOn` 同一判据）。
        //   ⚠️ 删字段而不是留着不用：私有静态字段"只赋值不读"会多一条 CS0414 警告，
        //      而本工程的编译基线是 **0 错误 2 警告**（两条都在 `TownPortalFx`）。

        private static string _status = "";

        private static Vector2 _scrollLeft, _scrollDesc, _scrollNums, _scrollBag, _scrollPlants;

        /// <summary>任何装备 / 卸下 / 切换都会 +1；所有文本缓存据此判断"要不要重建"。</summary>
        private static int _rev;

        private static void Touch() { _rev++; }

        // ══════════════════════════════════════════════════════════════════
        // 训练场自己的库存清单（**独立于存档**：不读、不写任何存档字段）
        //   只活在本进程内存里；进训练场时 `ApplyAll` 把它重新装回这一局。
        // ══════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, int> WishItems = new Dictionary<string, int>(200);
        private static readonly Dictionary<string, PluginWish> WishPlugins = new Dictionary<string, PluginWish>(64);
        private static readonly List<string> WishSeeds = new List<string>(8);

        /// <summary>一条"想要的插件"：装在哪把武器 + 想要几层。</summary>
        private struct PluginWish
        {
            public WeaponSlot Slot;
            public int Stacks;
        }

        /// <summary>单件道具在仓库 UI 里最多叠的层数（见类注释 三 的边界）。</summary>
        private const int MaxVaultItemStacks = 20;

        // ══════════════════════════════════════════════════════════════════
        // 运行期
        // ══════════════════════════════════════════════════════════════════

        private GameBootstrap _boot;
        private RunSession _bound;      // 这一份清单已经装进哪个会话（重开局就重装）

        private bool _stylesReady;
        private GUIStyle _title, _head, _small, _tiny, _bold, _wrap, _rowLabel, _btn, _btnSmall, _mark;

        // ★ v308 新增的三个样式：都只回答"对齐 / 颜色"，字号跟现有的 _bold / _tiny / _small 一致
        private GUIStyle _num;        // 顶栏金币（右对齐 · 金色 · 15px 加粗）
        private GUIStyle _tinyR;      // 顶栏已装计数（右对齐 · 11px · 与 _tiny 同色）
        private GUIStyle _bar;        // 底部状态条正文（13px · 比 _tiny 亮一档）
        private GUIStyle _warn;       // 详情栏"三把武器插件位已满"（13px · 警示橙）

        /// <summary>★ v309：详情栏「数值」区的一行（13px · 切开溢出 · 行高由 `DtNumRowH` 控）。</summary>
        private GUIStyle _numRow;

        /// <summary>
        /// ★ v310：左栏"分组标题行"（插件页的「锄　头 · 10 件」）。
        /// 只回答"这是哪一组 / 有几件"：12px 加粗、暖金、`Clip`（一行放不下就切，不换行顶破行高）。
        /// 它**不是按钮**、不参与选中 —— 分组标题不是一件能装上的东西。
        /// </summary>
        private GUIStyle _groupHdr;

        private Font _font;

        private static readonly WeaponSlot[] WeaponOrder =
            { WeaponSlot.Hoe, WeaponSlot.Can, WeaponSlot.Fertilizer };

        private static readonly Color[] TierColors =
        {
            new Color(0.74f, 0.76f, 0.80f),   // D 普通
            new Color(0.46f, 0.80f, 0.54f),   // C 一般
            new Color(0.40f, 0.66f, 0.96f),   // B 稀有
            new Color(0.74f, 0.50f, 0.96f),   // A 史诗
            new Color(0.99f, 0.78f, 0.34f)    // S 传奇
        };

        // ══════════════════════════════════════════════════════════════════
        // 生命周期
        // ══════════════════════════════════════════════════════════════════

        private void Update()
        {
            // ① 双保险：本组件只活在训练场，但万一将来被挂到别处，也不许做任何事
            if (!TrainingMode.Active) return;

            if (_boot == null) _boot = GetComponent<GameBootstrap>();
            RunSession s = _boot != null ? _boot.Session : null;
            if (s == null || _boot == null || !_boot.Ready) return;

            _last = s;      // 只给 `SetOpen` 的日志读数用（不参与任何判定）

            ReadKeys(s);

            // ② 新会话（Shift+R 重开一局 / 从主城再进训练场）⇒ 把清单重新装回去
            if (!ReferenceEquals(_bound, s))
            {
                _bound = s;
                ApplyAll(s);
                Touch();
                Log.Info(Tag + "已把库存清单装进新会话：" + Describe(s));
            }

            // ③ 三项补满
            Refill(s);
        }

        private void OnDestroy()
        {
            // 静态清单**故意保留**：离开训练场再回来，玩家的自定义还在（"常驻"的另一半含义）。
            IsOpen = false;
        }

        // ══════════════════════════════════════════════════════════════════
        // 输入（B 开 / 关；E 由 `TrainingWarehouse` 处理）
        // ══════════════════════════════════════════════════════════════════

        private static void ReadKeys(RunSession s)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) { _bHeld = false; return; }
            var bk = kb.bKey;
            if (bk == null) { _bHeld = false; return; }
            // 上升沿锁存（写法与 `EscKey.Pressed` 完全一致）：`isPressed` 的上升沿 ∪ `wasPressedThisFrame`
            // ⇒ 严格**包含**裸 `wasPressedThisFrame` 能读到的每一次，不漏帧
            bool down = bk.isPressed;
            bool edge = (down && !_bHeld) || bk.wasPressedThisFrame;
            _bHeld = down;
            if (!edge) return;
            // Esc 菜单开着且面板没开 ⇒ 不抢键（那时 B 该归菜单）
            if (s != null && s.Paused && !IsOpen) return;
            Toggle(s);
#endif
        }

        /// <summary>B 键的上升沿锁存（上一帧是否按住）。</summary>
        private static bool _bHeld;

        /// <summary>最近一次的会话（只用于日志读数，不参与任何判定）。</summary>
        private static RunSession _last;

        /// <summary>开关面板。</summary>
        public static void Toggle(RunSession s = null) => SetOpen(!IsOpen, s);

        /// <summary>显式开 / 关面板（**唯一写 `IsOpen` 的地方**）。重复设同一个值不做任何事。</summary>
        public static void SetOpen(bool on, RunSession s = null)
        {
            if (IsOpen == on) return;
            IsOpen = on;
            RunSession cur = s != null ? s : _last;
            if (on)
            {
                _status = "左栏点一条 = **只选中**（详情在中间栏，生效要用那儿的按钮）· B / E 关闭";
                Touch();
                Log.Info(Tag + "面板打开（三栏仓库）｜" + Describe(cur));
            }
            else Log.Info(Tag + "面板关闭｜" + Describe(cur));
        }

        // ══════════════════════════════════════════════════════════════════
        // 三项补满（用户原话："水分、水壶充能、肥料充能全部为满"）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 每帧把 **耕地水分 / 水壶充能 / 肥料充能** 补到满。全部走战斗既有的公开 API：
        ///   `RunSession.AddWaterAmmo/AddFertAmmo`（各自 `Mathf.Clamp` 到自己那个上限）、
        ///   水壶上限读 `s.WaterAmmoCap`（= 基础 100 + 丰水壶每层 20，**不写死 100**）、
        ///   耕地读 `FarmPlot.Water/WaterMax` 用 `FarmPlot.AddWater` 补差值。
        /// 开关初值 false ⇒ 即便本组件被误挂，也不会自动补满。
        /// </summary>
        private static void Refill(RunSession s)
        {
            if (TrainingMode.AmmoAlwaysFull)
            {
                int waterCap = s.WaterAmmoCap;
                if (s.WaterAmmo < waterCap) s.AddWaterAmmo(waterCap - s.WaterAmmo);
                if (s.FertAmmo < RunSession.FertAmmoMax)
                    s.AddFertAmmo(RunSession.FertAmmoMax - s.FertAmmo);
            }

            if (TrainingMode.WaterAlwaysFull && s.World != null && s.World.Farm != null)
            {
                FarmGrid farm = s.World.Farm;
                for (int i = 0; i < farm.PlotCount; i++)
                {
                    FarmPlot p = farm.Plot(i);
                    if (p == null || p.Water >= p.WaterMax) continue;
                    p.AddWater(p.WaterMax - p.Water);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 装备 / 卸下（三个分类各一对）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>把清单里的**全部**条目装进这一局（新会话时调一次；幂等）。</summary>
        private static void ApplyAll(RunSession s)
        {
            if (s == null) return;

            // ★★ v308：新会话先把**背包里的待装插件**清掉（用户原话："默认不要装备任何东西"）★★
            //
            //  ── 为什么新会话里会有"待装插件" ──
            //    ① `RunSession.Start` 的 `WeaponSystem.GrantDefaultOwned` 会给**每一局**（含训练场）
            //       白送 `hoe_warmth` / `can_ripen` 两件，形态是 `Slot == None`（背包待装）。
            //       本轮已在 `RunSession.Start` 加了 `TrainingMode.CleanLoadout` 守卫 ⇒ 训练场不再白送；
            //       但守卫是"以后不送"，**已经在别处发生的**那一份仍要在这里兜底清掉 ✓
            //    ② `TrainingShop.TryUninstall` 会把玩家卸下的插件放回背包（`Slot == None`）——
            //       那种"包里躺一件"是本局内的中间态，下一局本来就不会被还原（`WishPlugins` 只记装上的）
            //       ⇒ 在这里一并清掉，语义与改前一致（新局从"干净"开始）✓
            //
            //  ── 为什么只清 `Slot == None` ──
            //    下面 `WishPlugins` 那一段马上会把玩家自己选过的插件**装到具体武器上**
            //    （`EnsurePluginOn` 走 `WeaponSystem.Install`，落点是 `WeaponSlot.Hoe/Can/Fertilizer`）。
            //    先清后装 ⇒ 玩家的选择一件不少，而"没被选择过的白送件"一件不留 ✓
            //    ⚠️ 开关默认 false ⇒ 战斗场景里 `TrainingBootstrap` 不存在、本方法不会被调到，
            //       而 `CleanLoadout` 也恒为 false ⇒ 这一行在战斗中等于 `if (false)` ✓
            if (TrainingMode.CleanLoadout) PurgePendingPlugins(s);

            foreach (KeyValuePair<string, int> kv in WishItems)
            {
                ItemDef def = TableDatabase.GetItem(kv.Key);
                if (def == null) continue;
                EnsureItemStacks(s, def, kv.Value);
            }
            foreach (KeyValuePair<string, PluginWish> kv in WishPlugins)
            {
                PluginAsset asset = FindPlugin(kv.Key);
                if (asset != null) EnsurePluginOn(s, asset, kv.Value.Slot, kv.Value.Stacks);
            }
            // ★★ v307：**新会话不再把植物装回去**（用户原话："训练模式修改开局不选择植物，空植物进入"）★★
            //
            //  ── 改前为什么必须改 ──
            //    改前这一段会在**每一次新会话**（首进训练场 / Shift+R 重开）把 `WishSeeds` 里的植物
            //    重新入袋 + 重新种到耕地上 ⇒ 玩家看到的"开局"永远带着上一轮选的植物，
            //    与"空植物进入"直接冲突 ✗
            //    （注：`RunSession.Start` 里的 `Bag = new SeedBag(...)` 每局都是空袋、
            //      耕地也是新的 ⇒ **本段是训练场里唯一的"开局自动种植物"来源**；
            //      另一处是 `RunSession.TickPrepare` 的开局三选一，已由 `TrainingMode.EmptyStart` 关掉）
            //
            //  ── 为什么连清单也清 ──
            //    留着 `WishSeeds` 只会让 `Describe` 报"植物 N 种"而地上袋里一个都没有（两份真相）✗
            //    清掉之后：袋、地、清单三处都是"空"；玩家**当帧**在面板里选的植物照旧走
            //    `EquipSeed` → `Bag.AddSeed` + `SyncPlotsToSeeds`（**仓库功能一点没坏** ✓）
            //
            //  ── 为什么包在 `if (TrainingMode.EmptyStart)` 里而不是直接删掉 ──
            //    与全工程同一条纪律：训练行为都要能被一个**默认 false** 的开关解释。
            //    把 `EmptyStart` 排回 false，这一段就恢复成改前的行为（清单原样装回去）✓
            //    ⚠️ `ApplyAll` 只活在本文件，本文件只由 `TrainingBootstrap.Awake` 创建
            //      ⇒ 战斗场景里连这个方法都不会被调到 ✓
            if (!TrainingMode.EmptyStart)
            {
                for (int i = 0; i < WishSeeds.Count; i++)
                {
                    SeedDef def = TableDatabase.GetSeed(WishSeeds[i]);
                    if (def == null) continue;
                    EnsureSeedInBag(s, def);
                    SowOnPlot(s, def, false);     // 新会话里按清单把植物也种回去（静默，不刷状态栏）
                }
            }
            else WishSeeds.Clear();
        }

        /// <summary>
        /// 清掉**背包里的待装插件**（`Slot == None`），返回清掉几件。
        /// ★ v308：只由 `ApplyAll` 在 `TrainingMode.CleanLoadout == true` 时调 ★
        ///   已经装到某把武器上的实例（`Slot != None`）**一件都不动** —— 它们要么是玩家
        ///   在仓库面板里自己选的（`WishPlugins` 马上会重新装回来），要么是 `TrainingShop`
        ///   替玩家装的，两者都不该被"开局清空"波及 ✓
        /// </summary>
        private static int PurgePendingPlugins(RunSession s)
        {
            if (s == null || s.Goods == null) return 0;
            int n = 0;
            for (int i = s.Goods.Plugins.Count - 1; i >= 0; i--)
            {
                PluginInstance pi = s.Goods.Plugins[i];
                if (pi == null || pi.Slot != WeaponSlot.None) continue;
                s.Goods.Plugins.RemoveAt(i);
                n++;
            }
            if (n > 0)
                Log.Info(Tag + "开局清空：清掉背包里 " + n + " 件待装插件（训练场不带默认插件）");
            return n;
        }

        // ── 道具 ──────────────────────────────────────────────────────────

        /// <summary>道具层数对齐到 <paramref name="stacks"/>（只增，走 `ApplyItem`）。</summary>
        private static void EnsureItemStacks(RunSession s, ItemDef def, int stacks)
        {
            if (s == null || def == null) return;
            int want = Mathf.Clamp(stacks, 0, ItemCap(def));
            s.ItemStacks.TryGetValue(def.Id, out int cur);
            for (int n = cur; n < want; n++) s.ApplyItem(def);   // ← 战斗既有的"买到手"入口
        }

        /// <summary>装备一层（叠到上限就不再叠）。</summary>
        private static void EquipItem(RunSession s, ItemDef def)
        {
            if (s == null || def == null) return;
            int cap = ItemCap(def);
            WishItems.TryGetValue(def.Id, out int want);
            s.ItemStacks.TryGetValue(def.Id, out int cur);
            if (cur >= cap || want >= cap)
            {
                _status = def.Name + "：已到仓库上限 ×" + cap;
                return;
            }
            WishItems[def.Id] = want + 1;
            s.ApplyItem(def);
            _status = def.Name + " ×" + (cur + 1) + " 已装备（属性 / 机制本帧生效）";
        }

        /// <summary>整件卸下。**必须分三步**（少一步都会"看着卸了、数值没变"）。</summary>
        private static void UnequipItem(RunSession s, ItemDef def)
        {
            if (s == null || def == null) return;
            WishItems.Remove(def.Id);
            if (!s.ItemStacks.ContainsKey(def.Id)) { _status = def.Name + "：本来就没装"; return; }

            // ① 卸下必须"删键"而不是"写 0"：
            //    `ReapplyItems` 里是 `Mathf.Max(1, kv.Value)` ⇒ 写 0 会被当成 1 层继续生效
            s.ItemStacks.Remove(def.Id);

            // ② 顺序很关键：**先按来源撤销那一条，再让 `ReapplyItems` 全量重写其余**
            //    病因：`ReapplyItems` 的撤销步骤是"遍历**当前字典里的键**去 Remove"
            //    ⇒ 键已被删掉的那件道具，它的 `StatModifier`（source = `item:<id>`）永远撤不掉，
            //    `Progress.Stats` 里会留一条幽灵增益 ⇒ 玩家看到"卸下了但数值没变"
            s.Progress.Stats.RemoveModifiersFrom("item:" + def.Id);
            s.ReapplyItems();
            s.SyncItemRules();
            ProgMechanics.ApplyCapsToTable(s);      // 与 `ApplyItem` 内部同一个上限重算（改上限的那几件）

            _status = def.Name + " 已卸下（属性 / 机制 / 上限都回到没装过它的状态）";
        }

        private static int ItemCap(ItemDef def)
            => def == null ? 1 : Mathf.Clamp(def.MaxStack, 1, MaxVaultItemStacks);

        // ── 插件 ──────────────────────────────────────────────────────────

        /// <summary>保证这件插件装在这一把武器上，且层数达到 <paramref name="stacks"/>（幂等）。</summary>
        private static void EnsurePluginOn(RunSession s, PluginAsset asset, WeaponSlot slot, int stacks)
        {
            if (s == null || s.Goods == null || asset == null) return;
            if (slot == WeaponSlot.None) return;
            if (!asset.Fits(slot)) return;
            RemovePluginElsewhere(s, asset, slot);
            PluginInstance have = s.Goods.Find(asset, slot);
            if (have == null)
            {
                if (!WeaponSystem.Install(s.Goods, asset, slot, out string why))
                {
                    Log.Warn(Tag + "装不上 " + asset.displayName + " → "
                                + RunSession.WeaponSlotName(slot) + "：" + why);
                    return;
                }
                have = s.Goods.Find(asset, slot);
                if (have == null) return;
            }
            int want = Mathf.Clamp(stacks, 1, Mathf.Max(1, asset.maxStack));
            while (have.Stacks < want && have.CanStackMore) have.Stacks++;
        }

        /// <summary>
        /// 装一层插件（详情栏「装上 +」）。
        /// 目标武器：★ v308 起由 <see cref="PickTargetSlot"/> **自动**决定（锄头 → 水壶 → 肥料的
        /// 第一把适配且还有空位的），改前是玩家用详情栏那 3 颗按钮手选（已按用户要求删除）。
        /// </summary>
        private static void EquipPlugin(RunSession s, PluginAsset asset)
        {
            if (s == null || s.Goods == null || asset == null) return;
            WeaponSlot target = PickTargetSlot(s, asset);
            RemovePluginElsewhere(s, asset, target);

            PluginInstance have = s.Goods.Find(asset, target);
            if (have == null)
            {
                // 清单**只在装成功之后**才记：装不上（格满 / 互斥）不该留下一条"下次再试"的假状态
                if (!WeaponSystem.Install(s.Goods, asset, target, out string why))
                {
                    _status = "装不上：" + why;
                    return;
                }
                have = s.Goods.Find(asset, target);
                if (have == null) { _status = "装上了但列表里找不到它（不该发生）"; return; }
                WishPlugins[asset.id] = new PluginWish { Slot = target, Stacks = have.Stacks };
                _status = asset.displayName + " → " + RunSession.WeaponSlotName(target)
                        + "（" + s.Goods.SlotsUsedOn(target) + "/" + PlayerGoods.MaxPluginsPerWeapon + "）";
                return;
            }

            // 已在位 ⇒ 再点就**叠一层**（叠多层的规矩与 `PlayerGoods.Acquire` 一致）
            int cap = Mathf.Max(1, asset.maxStack);
            if (asset.exclusivePerWeapon)
            {
                WishPlugins[asset.id] = new PluginWish { Slot = target, Stacks = 1 };
                _status = asset.displayName + " 是互斥插件（同一武器只算一件），已装在 "
                        + RunSession.WeaponSlotName(target);
                return;
            }
            if (!have.CanStackMore || have.Stacks >= cap)
            {
                _status = asset.displayName + " 已到安装上限 ×" + cap;
                return;
            }
            have.Stacks++;      // `WeaponSystem.Aggregate` 每帧现算 ⇒ 下一帧伤害就按新层数
            WishPlugins[asset.id] = new PluginWish { Slot = target, Stacks = have.Stacks };
            _status = asset.displayName + " ×" + have.Stacks + "/" + cap
                    + "（" + RunSession.WeaponSlotName(target) + "）";
        }

        /// <summary>
        /// 这件插件**会**装到哪把武器；`WeaponSlot.None` = 三把都装不下（不适配，或 6 格全满）。
        ///
        /// ★★ v308：目标的决定方式从"玩家点按钮选"改成**纯自动** ★★
        ///   用户原话："删掉详细栏中（锄头、水壶、肥料）的选择按键"。
        ///   规则（与 `PlayerGoods.FreeSlotsOn` / `WeaponSystem.Install` 的判据**同一份**）：
        ///     按 `WeaponOrder` 的固定顺序 锄头 → 水壶 → 肥料，
        ///     取**第一把**"`asset.Fits(w)` 成立，且（这把武器上已有同款 ⇒ 可以叠层）或（还有空格位）"的。
        ///   · 顺序固定 ⇒ 同一件插件每次点「装上 +」落点都一样，不会两把武器来回跳 ✓
        ///   · 三把都满 ⇒ 返回 `None` ⇒ 详情栏显示「三把武器插件位已满」并把「装上 +」灰化 ✓
        /// </summary>
        private static WeaponSlot PreviewTargetSlot(RunSession s, PluginAsset asset)
        {
            if (asset == null || s == null || s.Goods == null) return WeaponSlot.None;
            for (int i = 0; i < WeaponOrder.Length; i++)
            {
                WeaponSlot w = WeaponOrder[i];
                if (!asset.Fits(w)) continue;
                if (s.Goods.Find(asset, w) != null || s.Goods.FreeSlotsOn(w) > 0) return w;
            }
            return WeaponSlot.None;
        }

        /// <summary>
        /// 「装上 +」真正的落点。与 <see cref="PreviewTargetSlot"/> 同一份判据 ——
        /// 两者**只在"三把全满"这一种情形下分叉**：显示层要显示"已满"，而安装层必须给
        /// `WeaponSystem.Install` 一个具体武器让它自己回一句"装不上：<原因>"（那一句会进状态条）。
        /// 这样"按钮灰化"与"真按下去"两条路的措辞都由**战斗自己的装配入口**决定，不会两套真相 ✓
        /// </summary>
        private static WeaponSlot PickTargetSlot(RunSession s, PluginAsset asset)
        {
            WeaponSlot w = PreviewTargetSlot(s, asset);
            return w != WeaponSlot.None ? w : WeaponSlot.Hoe;
        }

        /// <summary>把某把武器上的一件插件卸掉（右上格子点击走这里）。</summary>
        private static void UnequipPluginSlot(RunSession s, PluginInstance pi)
        {
            if (s == null || s.Goods == null || pi == null) return;
            s.Goods.Plugins.Remove(pi);       // `Aggregate` 没有缓存 ⇒ 当帧失效
            bool still = pi.Asset != null && s.Goods.Find(pi.Asset, pi.Slot) != null;
            if (!still && pi.Asset != null) WishPlugins.Remove(pi.Asset.id);
            _status = (pi.Asset != null ? pi.Asset.displayName : "插件")
                    + " 已从 " + RunSession.WeaponSlotName(pi.Slot) + " 卸下"
                    + (still ? "（同类还有）" : "");
        }

        /// <summary>把这件插件从**所有**武器上卸掉（详情里的「卸下」走这里）。</summary>
        private static void UnequipPlugin(RunSession s, PluginAsset asset)
        {
            if (s == null || s.Goods == null || asset == null) return;
            WishPlugins.Remove(asset.id);
            int removed = 0;
            for (int i = s.Goods.Plugins.Count - 1; i >= 0; i--)
            {
                PluginInstance pi = s.Goods.Plugins[i];
                if (pi == null || pi.Asset == null || pi.Asset.id != asset.id) continue;
                s.Goods.Plugins.RemoveAt(i);
                removed++;
            }
            _status = removed > 0
                ? asset.displayName + " 已卸下（" + removed + " 个实例）"
                : asset.displayName + "：本来就没装";
        }

        /// <summary>同一件插件若装在**别的**武器上 ⇒ 移到目标武器（清单说它在哪就只在哪）。</summary>
        private static void RemovePluginElsewhere(RunSession s, PluginAsset asset, WeaponSlot keep)
        {
            if (s == null || s.Goods == null || asset == null) return;
            for (int i = s.Goods.Plugins.Count - 1; i >= 0; i--)
            {
                PluginInstance pi = s.Goods.Plugins[i];
                if (pi == null || pi.Asset == null || pi.Asset.id != asset.id) continue;
                if (pi.Slot == keep) continue;
                s.Goods.Plugins.RemoveAt(i);
            }
        }

        private static PluginAsset FindPlugin(string id)
        {
            PluginAsset[] all = TableDatabase.Plugins;
            if (all == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }

        /// <summary>
        /// 这件插件**真的装在武器上**的实例条数（`Slot != None`）。左栏"已装"标记与详情状态行都读它。
        ///
        /// ★★ v308：判据加了 `Slot != WeaponSlot.None` —— 这是"异常装备"的第二个来源 ★★
        ///   改前（v307 及以前）本方法**不看 Slot**，把背包里的"待装件"也数成"已装"。
        ///   而 `RunSession.Start` 给每一局白送 `hoe_warmth` / `can_ripen`（形态是背包待装）
        ///   ⇒ 一进训练场打开仓库，插件列表上就写着「已装 ×1」、详情写着「★ 已装 1 处」，
        ///   而"武器与插件"面板里六个格子全空 ⇒ 玩家看到的就是"插件自己装上了" ✗
        ///   现在：装上的才算装上；背包里的由 <see cref="PluginCountPending"/> 单独说 ✓
        /// </summary>
        private static int PluginCountInstalled(RunSession s, PluginAsset asset)
        {
            if (s == null || s.Goods == null || asset == null) return 0;
            int n = 0;
            for (int i = 0; i < s.Goods.Plugins.Count; i++)
            {
                PluginInstance pi = s.Goods.Plugins[i];
                if (pi != null && pi.Asset != null && pi.Asset.id == asset.id
                    && pi.Slot != WeaponSlot.None) n++;
            }
            return n;
        }

        /// <summary>这件插件**躺在背包里待装**的条数（`Slot == None`）。★ v308 新增 ★</summary>
        private static int PluginCountPending(RunSession s, PluginAsset asset)
        {
            if (s == null || s.Goods == null || asset == null) return 0;
            int n = 0;
            for (int i = 0; i < s.Goods.Plugins.Count; i++)
            {
                PluginInstance pi = s.Goods.Plugins[i];
                if (pi != null && pi.Asset != null && pi.Asset.id == asset.id
                    && pi.Slot == WeaponSlot.None) n++;
            }
            return n;
        }

        // ── 植物 ──────────────────────────────────────────────────────────

        private static void EnsureSeedInBag(RunSession s, SeedDef def)
        {
            if (s == null || s.Bag == null || def == null) return;
            if (s.Bag.Find(def.Id, null) != null) return;
            if (!s.Bag.AddSeed(def, null, def.RarityTier, out string why))
                Log.Warn(Tag + "种子入袋失败 " + def.Name + "：" + why);
        }

        /// <summary>
        /// 详情栏「种下」：入袋 + 让**战斗自己的对齐入口**把它种到耕地（见 `SowOnPlot`）。
        /// ⚠️ v306：本方法**不再**由左栏点击调用 —— 只有详情栏按钮会进来（见 `SelectOrEquip`）。
        /// </summary>
        private static void EquipSeed(RunSession s, SeedDef def)
        {
            if (s == null || def == null) return;
            bool already = s.Bag.Find(def.Id, null) != null;
            if (already)
            {
                SowOnPlot(s, def, true);
                return;
            }
            if (!s.Bag.AddSeed(def, null, def.RarityTier, out string why))
            {
                _status = "入袋失败：" + why;    // 通常是"种子袋已满"（6 格 = 田地列数）
                return;
            }
            if (!WishSeeds.Contains(def.Id)) WishSeeds.Add(def.Id);
            SowOnPlot(s, def, true);
        }

        /// <summary>
        /// 把"种子袋 ⇄ 耕地"对齐一次，走的是**战斗自己的** `RunSession.SyncPlotsToSeeds`。
        ///
        /// ══════════════════════════════════════════════════════════════════
        /// ★★ v306："植物实时更新没有匹配上"的根因与修法 ★★
        /// ══════════════════════════════════════════════════════════════════
        /// ── 改前为什么会不匹配（四条，前三条是"两份真相"，第四条是"释放不掉"）──
        ///   ① **显示侧读错了来源**（主因）：`EnsurePlants` / 左栏"已种"标记 / 详情状态行
        ///      读的都是 `s.Bag`（**种子袋**）⇒ 那是"我买了什么种子"，不是"地里长了什么"。
        ///      袋与地一旦有一刻不一致，面板立刻就是错的 ✗
        ///   ② **对齐点太稀**：唯一的对齐入口是 `RunSession.SyncPlotsToSeeds`，
        ///      它只被 `StartWave` 调（`RunSession.cs:5571`）。而训练场里 `NoWaves`
        ///      **只拦刷怪**（`RunSession.cs:683` / `882`），`StartWave` 本身照跑 ——
        ///      所以它其实每分钟跑一次 ⇒ 任何不一致**最长存活 60 秒** ✓
        ///      玩家看到的就是"种了没显示 / 卸了地里还在"，过一会儿又自己变了 ✗
        ///   ③ **1 种子 → N 列**：改前的"种下"走 `AutoPlantSeed(def,level)`，
        ///      它找的是**第一条空地**、与"袋里哪一格"毫无关系 ⇒
        ///      同一株点两次 = 地里占两列、袋里只有一格（正是 `SyncPlotsToSeeds`
        ///      "1 种子 : 1 地块"契约要防的那件事）✗
        ///   ④ **熟透的列永远释放不掉**：`SyncPlotsToSeeds` ③ 段用的是 `plot.RemoveGrowing()`，
        ///      而它在 `State != Growing` 时**直接 return false** ⇒
        ///      **`Grown`（已成熟）的那一列会一直留在原地** ✗
        ///      这一条与"每分钟对齐一次"叠加之后就是"卸下了、地里还在，而且永远在" ✓
        ///   ⇒ 于是"仓库右下植物栏"（读袋）与"耕地上真实作物"是**两份互不同步的真相** ✓
        ///
        /// ── 现在的口径（用户要求：以耕地为唯一真相）──
        ///   ① **显示**：`EnsurePlants` **每帧**从 `World.Farm` 反查（列首的 `CropDef` / `State`），
        ///      左栏"已种"标记与详情状态行同样改读耕地；本文件不再维护"我种了什么"的第二份清单
        ///      （`WishSeeds` 只剩"跨会话记忆"这一个用途，不参与任何绘制判据）✓
        ///   ② **写**：种下 / 卸下都**立刻**调一次 `SyncPlotsToSeeds` ——
        ///      把"最长 60 秒才对齐"变成"当帧对齐"，且"袋里有几个这种种子 ⇒ 地里就有几列"
        ///      这条**战斗自己的规则**在训练场里也成立，不会再出现 1 种子 : 2 列 ✓
        ///   ③ 幂等：`SyncPlotsToSeeds` 内部按"owned vs have"算差值，重复调用不会多补多删 ✓
        ///   ④ 熟透的列由 `UnequipPlantColumn` / `UnequipSeed` 里的 `OnPlantDied()` 补上 ✓
        /// </summary>
        private static void SowOnPlot(RunSession s, SeedDef def, bool verbose)
        {
            if (s == null || def == null) return;
            int before = PlantedColumnsOf(s, def.Id);
            s.SyncPlotsToSeeds(out int released, out int planted);
            int after = PlantedColumnsOf(s, def.Id);
            Touch();
            if (!verbose) return;
            _status = after > before
                ? def.Name + " 已种到耕地（现在占 " + after + " 列）"
                : after > 0
                    ? def.Name + " 已经种在耕地上了（占 " + after + " 列）"
                    : def.Name + " 已入袋，但耕地没有空列了（先把别的植物卸下）";
            if (released > 0 || planted > 0)
                Log.Info(Tag + "耕地对齐（SyncPlotsToSeeds）：释放 " + released + " 块、补种 " + planted
                             + " 块｜" + def.Name + " 占 " + after + " 列");
        }

        /// <summary>某个种子在耕地上**占了几列**（列首的 `CropSeedId` 是唯一判据）。</summary>
        private static int PlantedColumnsOf(RunSession s, string seedId)
        {
            FarmGrid farm = s != null && s.World != null ? s.World.Farm : null;
            if (farm == null || string.IsNullOrEmpty(seedId)) return 0;
            int n = 0;
            for (int col = 0; col < farm.Columns; col++)
            {
                FarmPlot head = farm.ColumnHead(col);
                if (head == null) continue;
                if (head.State == PlotState.Empty || head.State == PlotState.Locked) continue;
                if (head.CropSeedId == seedId) n++;
            }
            return n;
        }

        /// <summary>
        /// 卸下**一列**植物（右下植物栏每行那个「卸下」按钮走这里）。
        ///
        /// ★ v306：与改前的 `UnequipSeed(s, def)` 是**两条不同的语义** ★
        ///   改前按"种子品种"卸 ⇒ 袋里同名种子的所有槽位一起卖，地里一列都不动 ✗
        ///   现在按"耕地上的**那一列**"卸 ⇒ 玩家看到哪一列就卸哪一列 ✓
        ///
        /// ── 两步，缺一不可 ──
        ///   ① **清空那一列**：走战斗自己的整列复位原语 `FarmPlot.OnPlantDied()`
        ///      （`ClearColumn` + `ResetToEmpty`）。
        ///      ⚠️ **不能用 `RemoveGrowing()`**：它在 `State != Growing` 时**直接 return false**
        ///         ⇒ **熟透（`Grown`）的那一列会原封不动留在原地** ——
        ///         这正是"卸下之后地里还在"的另一半病根（主因见 `SowOnPlot` 的 ①）✓
        ///   ② **袋里把这个种子拿掉**：否则下一次 `SyncPlotsToSeeds` 会按
        ///      "袋里还有 ⇒ 地不够 ⇒ 补种"把它**种回来** ✗
        ///   然后统一调一次 `SyncPlotsToSeeds` 收尾（幂等），让"袋 ⇄ 地"再对一次 ✓
        /// </summary>
        private static void UnequipPlantColumn(RunSession s, int column)
        {
            if (s == null || s.World == null || s.World.Farm == null) return;
            FarmGrid farm = s.World.Farm;
            FarmPlot head = farm.ColumnHead(column);
            if (head == null || head.CropDef == null)
            {
                _status = "那一列已经是空地";
                Touch();
                return;
            }
            string seedId = !string.IsNullOrEmpty(head.CropSeedId) ? head.CropSeedId
                          : (head.CropDef != null ? head.CropDef.Id : null);
            string name = head.CropDef != null ? head.CropDef.Name : "作物";

            // ① 整列复位（逐格调；列首那一次会把同列其它行一起清掉）
            for (int row = 0; row < farm.Rows; row++)
            {
                FarmPlot p = farm.Slot(column, row);
                if (p == null || p.State == PlotState.Empty || p.State == PlotState.Locked) continue;
                p.OnPlantDied();
            }

            // ② 袋里摘掉这个种子（只摘一个槽位：一列 ↔ 一个槽位）
            int sold = RemoveOneSeedFromBag(s, seedId);
            if (seedId != null) WishSeeds.Remove(seedId);

            // ③ 战斗自己的对齐入口收尾
            s.SyncPlotsToSeeds(out int released, out int planted);
            Touch();
            _status = name + " 已从耕地第 " + (column + 1) + " 列卸下"
                    + (sold > 0 ? "（种子袋同步 −1 格）" : "（种子袋里本来就没有它）")
                    + (released + planted > 0 ? "｜对齐：释放 " + released + " 补种 " + planted : "");
        }

        /// <summary>从种子袋里摘掉**一个**该品种的槽位（走 `SeedBag.Sell` 的清空路径，不领 refund）。</summary>
        private static int RemoveOneSeedFromBag(RunSession s, string seedId)
        {
            if (s == null || s.Bag == null || string.IsNullOrEmpty(seedId)) return 0;
            for (int i = s.Bag.SlotCount - 1; i >= 0; i--)
            {
                SeedSlot slot = s.Bag[i];
                if (slot == null || slot.IsEmpty || slot.Def == null || slot.Def.Id != seedId) continue;
                if (s.Bag.Sell(i, s.WaveIndex, out _)) return 1;
            }
            return 0;
        }

        /// <summary>整品种卸下（详情栏「卸下」按钮用）：把袋里该品种的槽位全摘掉，再让对齐入口释放那几列。</summary>
        private static void UnequipSeed(RunSession s, SeedDef def)
        {
            if (s == null || s.Bag == null || def == null) return;
            WishSeeds.Remove(def.Id);
            int removed = 0;
            for (int i = s.Bag.SlotCount - 1; i >= 0; i--)
            {
                SeedSlot slot = s.Bag[i];
                if (slot == null || slot.IsEmpty || slot.Def == null || slot.Def.Id != def.Id) continue;
                // 走 `SeedBag.Sell` 的"清空槽位"。**不领取 refund**（训练场金币无限，
                // 少一次记账比多一个"卖种子倒给钱"的副作用安全）。
                if (s.Bag.Sell(i, s.WaveIndex, out _)) removed++;
            }
            // ★ v306：改前这里就结束了（"地里那一列由战斗的回合同步释放"）——
            //   而那个"回合同步"（`StartWave` → `SyncPlotsToSeeds`）**最长要等 60 秒**才来一次
            //   （`NoWaves` 只拦刷怪，见 `SowOnPlot` 的 ②）⇒ 这 60 秒里玩家看到的
            //   就是"卸下了、地里还长着" ✗
            //   现在**当场**调一次：`owned = 0` ⇒ `SyncPlotsToSeeds` ③ 段把这几列释放掉 ✓
            //   ⚠️ 它内部用的是 `RemoveGrowing()`，对**熟透**的列无效 ⇒ 熟透的那几列这里补一次
            //      `OnPlantDied()`（与 `UnequipPlantColumn` 用的同一个原语）。
            s.SyncPlotsToSeeds(out int released, out int planted);
            int forced = 0;
            FarmGrid farm = s.World != null ? s.World.Farm : null;
            if (farm != null)
            {
                for (int col = 0; col < farm.Columns; col++)
                {
                    FarmPlot head = farm.ColumnHead(col);
                    if (head == null || head.State == PlotState.Empty || head.State == PlotState.Locked) continue;
                    if (head.CropSeedId != def.Id) continue;
                    for (int row = 0; row < farm.Rows; row++)
                    {
                        FarmPlot p = farm.Slot(col, row);
                        if (p == null || p.State == PlotState.Empty) continue;
                        p.OnPlantDied();
                    }
                    forced++;
                }
            }
            Touch();
            _status = removed > 0
                ? def.Name + " 已卸下（袋 −" + removed + " 格，耕地 −" + (released + forced) + " 列）"
                : def.Name + "：本来就不在袋里";
        }

        // ══════════════════════════════════════════════════════════════════
        // 选择 / 点击总入口
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 当前选中的是**表的第几条**（★ v310：不是"第几行"）。
        ///
        /// 为什么选择存"表下标"而不是"行号"—— 这是本次排序改动的**核心决定**：
        ///   排序后行号会随品质/分组重排，而**表下标永远指向同一件东西**。
        ///   存表下标，选中项在换页签、换 `_rev`、重排后都还是同一件；
        ///   而且所有"按下标取数据"的函数（`EnsureDetail` / `DrawDetail` / `BuyOrEquip` /
        ///   `UnequipSelected` / `IconKeyOf` / `TierColorOf`）本来就吃表下标 ⇒ **一个都不用改语义**。
        ///   唯一的换算点是行模型 `_rowIdx`（见 `EnsureRows`）：显示序 → 表下标只在那里发生一次。
        /// </summary>
        private static int SelOf(Cat c)
        {
            int v = c == Cat.Items ? _selItems
                  : c == Cat.Plants ? _selPlants
                  : _selPlugins;
            // −1 = 还没选过 ⇒ 落在**排序后的第一行**（品质最高那件），与左栏顶端一致
            if (v < 0) v = OrderOf(c, 0);
            return Mathf.Max(0, v);
        }

        /// <summary>把"选中"设成**表下标** <paramref name="i"/>（不是行号，见 <see cref="SelOf"/>）。</summary>
        private static void SetSel(Cat c, int i)
        {
            if (c == Cat.Items) _selItems = i;
            else if (c == Cat.Plants) _selPlants = i;
            else _selPlugins = i;
            _scrollDesc = Vector2.zero;
            _scrollNums = Vector2.zero;     // ★ v309：数值区也有自己的滚动条
            Touch();
        }

        private static int CountOf(Cat c)
        {
            if (c == Cat.Items) return TableDatabase.Items != null ? TableDatabase.Items.Length : 0;
            if (c == Cat.Plants) return TableDatabase.Seeds != null ? TableDatabase.Seeds.Length : 0;
            return TableDatabase.Plugins != null ? TableDatabase.Plugins.Length : 0;
        }

        /// <summary>
        /// ★★ v306：左栏点一条 = **只选中**（用户原话："商店点击物品时只作为选中，不要直接添加"）★★
        ///
        /// ── 改前（v305）是什么样 ──
        ///   本方法体里除了 `SetSel` 还有一次 `EquipItem` / `EquipSeed` / `EquipPlugin`
        ///   ⇒ **点一下就买了 / 装了 / 种了**，玩家没有"先看看这是什么"的机会 ✗
        ///   而中间详情栏明明已经有「装上 +」/「卸下」两个按钮 —— 一次点击做了两件事，
        ///   其中一件还是按钮已有的那件 ✓
        ///
        /// ── 现在（v306）──
        ///   只做 `SetSel`（刷新中间详情栏）+ 一行状态提示。
        ///   **添加只由中间详情栏的按钮触发**（`DrawDetail` → `BuyOrEquip`），
        ///   而 `BuyOrEquip` / `UnequipSelected` 调用的仍然是改前那几个装备函数，
        ///   一个都没有被改语义、也没有被删 ✓
        ///
        /// ★ v310：`idx` 是**表下标**（不是行号）—— `DrawLeft` 已经把行号换成表下标再传进来
        ///   （`SelectOrEquip(s, _cat, _rowIdx[i])`）⇒ 点了「剑气」就选中剑气，
        ///   排序不会让这一下选中隔壁那件。
        /// </summary>
        private static void SelectOrEquip(RunSession s, Cat c, int idx)
        {
            SetSel(c, idx);
            _status = "已选中（详情在中间栏）· 要生效请按详情栏里的「"
                    + (c == Cat.Plants ? "种下" : c == Cat.Plugins ? "装上 +" : "装上 +")
                    + "」";
            Touch();
        }

        // ══════════════════════════════════════════════════════════════════
        // 缓存（版本号驱动；交付报告 ⑤ / 类注释 六）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>1×1 白纹理（懒创建：**绝不在静态初始化器里 new Unity 对象**）。</summary>
        private static Texture2D _white;

        private static Texture2D White()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                {
                    name = "TRAIN_VaultWhite",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            return _white;
        }

        /// <summary>图标纹理缓存（**取不到也存 null**：缺图不必每帧再查一次）。</summary>
        private static readonly Dictionary<string, Texture2D> _icons =
            new Dictionary<string, Texture2D>(320);

        private static Texture2D IconOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_icons.TryGetValue(key, out Texture2D t)) return t;
            t = Resources.Load<Texture2D>("Art/UI/" + key);
            _icons[key] = t;
            return t;
        }

        // ── 资源名（按表下标 / 按 id；首次用到时建一次 ⇒ 绘制时零拼接）──

        private static string[] _keyItems, _keySeeds, _keyPlugins;
        private static readonly Dictionary<string, string> _keyItemById = new Dictionary<string, string>(200);
        private static readonly Dictionary<string, string> _keySeedById = new Dictionary<string, string>(64);
        private static readonly Dictionary<string, string> _keyPluginById = new Dictionary<string, string>(64);

        private static string ItemIconKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_keyItemById.TryGetValue(id, out string k)) return k;
            k = "icon_item_" + id;
            _keyItemById[id] = k;
            return k;
        }

        private static string SeedIconKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_keySeedById.TryGetValue(id, out string k)) return k;
            k = "icon_plant_" + id;
            _keySeedById[id] = k;
            return k;
        }

        private static string PluginIconKey(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_keyPluginById.TryGetValue(id, out string k)) return k;
            k = "icon_plugin_" + id;
            _keyPluginById[id] = k;
            return k;
        }

        private static void EnsureIconKeys()
        {
            ItemDef[] items = TableDatabase.Items;
            if (items != null && (_keyItems == null || _keyItems.Length != items.Length))
            {
                var a = new string[items.Length];
                for (int i = 0; i < items.Length; i++)
                    a[i] = items[i] != null ? ItemIconKey(items[i].Id) : null;
                _keyItems = a;
                if (_tabLabel == null) _tabLabel = new string[3];
                _tabLabel[0] = "道具 " + items.Length;
            }
            SeedDef[] seeds = TableDatabase.Seeds;
            if (seeds != null && (_keySeeds == null || _keySeeds.Length != seeds.Length))
            {
                var a = new string[seeds.Length];
                for (int i = 0; i < seeds.Length; i++)
                    a[i] = seeds[i] != null ? SeedIconKey(seeds[i].Id) : null;
                _keySeeds = a;
                if (_tabLabel == null) _tabLabel = new string[3];
                _tabLabel[1] = "植物 " + seeds.Length;
            }
            PluginAsset[] plugins = TableDatabase.Plugins;
            if (plugins != null && (_keyPlugins == null || _keyPlugins.Length != plugins.Length))
            {
                var a = new string[plugins.Length];
                for (int i = 0; i < plugins.Length; i++)
                    a[i] = plugins[i] != null ? PluginIconKey(plugins[i].id) : null;
                _keyPlugins = a;
                if (_tabLabel == null) _tabLabel = new string[3];
                _tabLabel[2] = "插件 " + plugins.Length;
            }
        }

        /// <summary>三张页签的文字（表长是静态的 ⇒ 建一次就够）。</summary>
        private static string[] _tabLabel;

        // ══════════════════════════════════════════════════════════════════
        // ★ v310：排序（显示序 → 表下标）
        //   三条规则见类注释 八；这里只有"排序"这一件事，不碰任何会话状态 ⇒
        //   同一张表排出来的顺序**永远相同**（稳定插入排序，不用比较器 / 不用随机）。
        //   缓存键 = 表长：表不长就一个字节都不重排；运行期每帧只做 3 次长度比较。
        // ══════════════════════════════════════════════════════════════════

        /// <summary>插件"不属于任何单把武器"那一组（任意武器 / 两把以上 / 表里写错）。</summary>
        private const int PluginGroupMisc = 3;
        /// <summary>插件分组的组数（锄头 / 水壶 / 肥料 / 通用）。</summary>
        private const int PluginGroupCount = 4;

        /// <summary>显示序 → 表下标（三类各一张；`null` / 长度不符时按"与表同序"兜底）。</summary>
        private static int[] _ordItems, _ordPlants, _ordPlugins;
        /// <summary>建这三张表时的表长（`−1` = 还没建；表长变了就重排）。</summary>
        private static int _ordLenItems = -1, _ordLenPlants = -1, _ordLenPlugins = -1;
        /// <summary>每个武器分组里有多少件插件（标题行上印"· 10 件"）。</summary>
        private static readonly int[] _groupCount = new int[PluginGroupCount];

        /// <summary>显示序 → 表下标（**唯一换算入口**；越界按原样返回，绝不抛）。</summary>
        private static int OrderOf(Cat c, int i)
        {
            int[] ord = c == Cat.Items ? _ordItems
                      : c == Cat.Plants ? _ordPlants
                      : _ordPlugins;
            return ord != null && i >= 0 && i < ord.Length ? ord[i] : i;
        }

        /// <summary>品质档（−1 ⇒ 空条目，排到该组最后）。</summary>
        private static int TierOfItem(ItemDef d) => d != null ? d.RarityTier : -1;
        private static int TierOfSeed(SeedDef d) => d != null ? d.RarityTier : -1;
        private static int TierOfPlugin(PluginAsset p) => p != null ? (int)p.quality : -1;

        /// <summary>
        /// 插件属于哪一组：锄头(0) → 水壶(1) → 肥料(2) → 通用/多武器(3)。
        /// 判据只看 `allowedWeapons`（安装判据 `PluginAsset.Fits` 读的也是它）：
        ///   · 空数组 = 任意武器 ⇒ 通用组
        ///   · 恰好一把 ⇒ 那一组
        ///   · 两把以上 ⇒ 通用/多武器组（"不能归给某一把"，所以单独一组）
        ///   · 只有 `None` ⇒ 通用组（表里写了个不合法的槽位，不崩、照常可读）
        /// </summary>
        private static int PluginGroupOf(PluginAsset p)
        {
            if (p == null) return PluginGroupMisc;
            WeaponSlot[] w = p.allowedWeapons;
            if (w == null || w.Length != 1) return PluginGroupMisc;
            switch (w[0])
            {
                case WeaponSlot.Hoe: return 0;
                case WeaponSlot.Can: return 1;
                case WeaponSlot.Fertilizer: return 2;
                default: return PluginGroupMisc;
            }
        }

        /// <summary>分组标题行的文字（「锄　头 · 10 件」；字间留全角空格，与页签「仓 库」同风格）。</summary>
        private static string PluginGroupTitle(int g, int count)
        {
            switch (g)
            {
                case 0: return "锄　头 · " + count + " 件";
                case 1: return "水　壶 · " + count + " 件";
                case 2: return "肥　料 · " + count + " 件";
                default: return "通用 / 多武器 · " + count + " 件";
            }
        }

        /// <summary>
        /// 建三条排序（**只在表长变了时跑**）。三张表都是"下标数组"，
        /// 排序只动下标 ⇒ 表本身一条数据都不动（本文件仍然不写任何战斗数据）。
        /// </summary>
        private static void EnsureOrder()
        {
            // ── ① 道具：品质从高到低（S→D），同品质保持表内原顺序 ──
            ItemDef[] items = TableDatabase.Items;
            int ni = items != null ? items.Length : 0;
            if (_ordItems == null || _ordItems.Length != ni)
            {
                _ordItems = ni > 0 ? new int[ni] : System.Array.Empty<int>();
                _ordLenItems = -1;
            }
            if (_ordLenItems != ni)
            {
                _ordLenItems = ni;
                for (int i = 0; i < ni; i++) _ordItems[i] = i;
                for (int i = 1; i < ni; i++)
                {
                    int v = _ordItems[i];
                    int rv = TierOfItem(items[v]);
                    int j = i - 1;
                    // 严格小于 ⇒ 相等不动 ⇒ **稳定**（同品质的行顺序与表里一模一样）
                    while (j >= 0 && TierOfItem(items[_ordItems[j]]) < rv)
                    {
                        _ordItems[j + 1] = _ordItems[j];
                        j--;
                    }
                    _ordItems[j + 1] = v;
                }
            }

            // ── ② 植物：同一条规则 ──
            SeedDef[] seeds = TableDatabase.Seeds;
            int ns = seeds != null ? seeds.Length : 0;
            if (_ordPlants == null || _ordPlants.Length != ns)
            {
                _ordPlants = ns > 0 ? new int[ns] : System.Array.Empty<int>();
                _ordLenPlants = -1;
            }
            if (_ordLenPlants != ns)
            {
                _ordLenPlants = ns;
                for (int i = 0; i < ns; i++) _ordPlants[i] = i;
                for (int i = 1; i < ns; i++)
                {
                    int v = _ordPlants[i];
                    int rv = TierOfSeed(seeds[v]);
                    int j = i - 1;
                    while (j >= 0 && TierOfSeed(seeds[_ordPlants[j]]) < rv)
                    {
                        _ordPlants[j + 1] = _ordPlants[j];
                        j--;
                    }
                    _ordPlants[j + 1] = v;
                }
            }

            // ── ③ 插件：先按武器分组、组内品质从高到低 ──
            PluginAsset[] plugins = TableDatabase.Plugins;
            int np = plugins != null ? plugins.Length : 0;
            if (_ordPlugins == null || _ordPlugins.Length != np)
            {
                _ordPlugins = np > 0 ? new int[np] : System.Array.Empty<int>();
                _ordLenPlugins = -1;
            }
            if (_ordLenPlugins != np)
            {
                _ordLenPlugins = np;
                for (int i = 0; i < np; i++) _ordPlugins[i] = i;
                for (int i = 1; i < np; i++)
                {
                    int v = _ordPlugins[i];
                    int gv = PluginGroupOf(plugins[v]);
                    int tv = TierOfPlugin(plugins[v]);
                    int j = i - 1;
                    // `v` 该排在 `ord[j]` 前面 ⇔ 组更靠前，或同组里品质更高
                    while (j >= 0)
                    {
                        int gj = PluginGroupOf(plugins[_ordPlugins[j]]);
                        if (gv < gj || (gv == gj && tv > TierOfPlugin(plugins[_ordPlugins[j]])))
                        {
                            _ordPlugins[j + 1] = _ordPlugins[j];
                            j--;
                        }
                        else break;
                    }
                    _ordPlugins[j + 1] = v;
                }
                for (int g = 0; g < PluginGroupCount; g++) _groupCount[g] = 0;
                for (int i = 0; i < np; i++) _groupCount[PluginGroupOf(plugins[_ordPlugins[i]])]++;
            }
        }

        // ── 左栏行模型（名称 / 副标题 / 已装标记 / 表下标 / 顶部 y）──
        //
        // ★ v310：行号**不再等于**表下标（见类注释 八）。三张文字表按**行号**索引，
        //   `_rowIdx[i]` 回答"这一行来自表的第几条"（分组标题行 = −1），
        //   `_rowY[i]` 是这一行的顶部 y（标题行矮一截），`_rowTotal` 是内容总高。
        //   ⇒ 绘制循环不用再自己乘行高，`contentH` 也照 `_rowTotal` 取。

        private const float RowHGroup = 24f;     // 插件页分组标题行

        private static string[] _rowName, _rowSub, _rowMark;
        private static int[] _rowIdx;
        private static float[] _rowY;
        private static int _rowCount;
        private static float _rowTotal;
        private static Cat _rowCat = (Cat)(-1);
        private static int _rowRev = -1;
        /// <summary>建缓存时的**表长**（★ v310 新增的第三个缓存键：表换了长度必须重排重画）。</summary>
        private static int _rowLen = -1;

        private void EnsureRows(RunSession s)
        {
            int tableLen = CountOf(_cat);
            if (_rowCat == _cat && _rowRev == _rev && _rowLen == tableLen && _rowName != null) return;
            _rowCat = _cat;
            _rowRev = _rev;
            _rowLen = tableLen;

            EnsureOrder();      // 三条排序（表长没变就只是几次 int 比较）

            // 容量 = 表长 + 4：插件页最多 4 条分组标题（锄头 / 水壶 / 肥料 / 通用）
            int cap = tableLen + 4;
            if (_rowName == null || _rowName.Length < cap)
            {
                _rowName = new string[cap];
                _rowSub = new string[cap];
                _rowMark = new string[cap];
                _rowIdx = new int[cap];
                _rowY = new float[cap];
            }

            int r = 0;
            float y = 0f;

            if (_cat == Cat.Items)
            {
                for (int i = 0; i < tableLen; i++)
                {
                    int t = OrderOf(Cat.Items, i);      // ★ 显示序 → 表下标
                    ItemDef d = TableDatabase.Items[t];
                    _rowIdx[r] = t;
                    _rowY[r] = y;
                    y += RowHLeft;
                    if (d == null) { _rowName[r] = ""; _rowSub[r] = ""; _rowMark[r] = ""; r++; continue; }
                    _rowName[r] = d.Name;
                    _rowSub[r] = "档位 " + GradeText(d.Grade) + " · " + Rarity.NameOf(d.RarityTier);
                    int have = 0;
                    if (s != null) s.ItemStacks.TryGetValue(d.Id, out have);
                    _rowMark[r] = have > 0 ? "已装 ×" + have : "";
                    r++;
                }
            }
            else if (_cat == Cat.Plants)
            {
                for (int i = 0; i < tableLen; i++)
                {
                    int t = OrderOf(Cat.Plants, i);     // ★ 显示序 → 表下标
                    SeedDef d = TableDatabase.Seeds[t];
                    _rowIdx[r] = t;
                    _rowY[r] = y;
                    y += RowHLeft;
                    if (d == null) { _rowName[r] = ""; _rowSub[r] = ""; _rowMark[r] = ""; r++; continue; }
                    _rowName[r] = d.Name;
                    _rowSub[r] = "档位 " + GradeText(d.Grade) + " · 成熟 " + d.GrowTime.ToString("0.#") + "s";
                    // ★ v306：判据从"袋里有没有"改成"**地里有没有**"（以耕地为唯一真相，见 `EnsurePlants`）
                    int cols = PlantedColumnsOf(s, d.Id);
                    _rowMark[r] = cols > 0 ? "已种×" + cols + "列" : "";
                    r++;
                }
            }
            else
            {
                int lastGroup = -1;
                for (int i = 0; i < tableLen; i++)
                {
                    int t = OrderOf(Cat.Plugins, i);    // ★ 显示序 → 表下标
                    PluginAsset p = TableDatabase.Plugins[t];

                    // ── 分组标题行：组号一变就插一条（排序已把同组的连在一起）──
                    int g = PluginGroupOf(p);
                    if (g != lastGroup)
                    {
                        lastGroup = g;
                        _rowName[r] = PluginGroupTitle(g, _groupCount[g]);
                        _rowSub[r] = "";
                        _rowMark[r] = "";
                        _rowIdx[r] = -1;                // −1 ⇒ 这一行是标题，不是条目
                        _rowY[r] = y;
                        y += RowHGroup;
                        r++;
                    }

                    _rowIdx[r] = t;
                    _rowY[r] = y;
                    y += RowHLeft;
                    if (p == null) { _rowName[r] = ""; _rowSub[r] = ""; _rowMark[r] = ""; r++; continue; }
                    _rowName[r] = p.displayName;
                    _rowSub[r] = "品质 " + SeedEconomy.QualityName(p.quality) + " · 适用 " + FitsText(p);
                    int cnt = PluginCountInstalled(s, p);
                    int pend = PluginCountPending(s, p);
                    // ★ v308：待装（背包）**不再**报成"已装"，两者分开写
                    _rowMark[r] = cnt > 0 ? "已装 ×" + cnt
                                : pend > 0 ? "背包 ×" + pend
                                : "";
                    r++;
                }
            }

            _rowCount = r;
            _rowTotal = y;
        }

        // ── 中下：道具背包行 ──

        private static int[] _bagItemIdx;
        private static string[] _bagItemLabel;
        private static int _bagItemCount = -1;
        private static int _bagRev = -1;

        private void EnsureBag(RunSession s)
        {
            if (_bagRev == _rev && _bagItemCount >= 0) return;
            _bagRev = _rev;
            ItemDef[] items = TableDatabase.Items;
            if (items == null) { _bagItemCount = 0; return; }
            if (_bagItemIdx == null || _bagItemIdx.Length < items.Length)
            {
                _bagItemIdx = new int[items.Length];
                _bagItemLabel = new string[items.Length];
            }
            int n = 0;
            if (s != null)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] == null) continue;
                    if (!s.ItemStacks.TryGetValue(items[i].Id, out int have) || have <= 0) continue;
                    _bagItemIdx[n] = i;
                    _bagItemLabel[n] = "×" + have + " 层 · 上限 " + ItemCap(items[i]);
                    n++;
                }
            }
            _bagItemCount = n;
        }

        // ── 右下：植物栏行（★ v306：**每帧从耕地反查**，本文件不再维护第二份清单）──

        /// <summary>第 i 行对应耕地上的**哪一列**（列号；`FarmGrid.ColumnHead(col)` 取回它）。</summary>
        private static int[] _plantSlots;
        /// <summary>该行的文本缓存（只在"内容真的变了"时重建，见 <see cref="_plantSig"/>）。</summary>
        private static string[] _plantLabel;
        /// <summary>该行文本的签名 = 列号 / 等级 / 状态 / 生长百分比 四件事的组合。</summary>
        private static int[] _plantSig;
        private static int _plantCount = -1;

        /// <summary>
        /// ★★ v306：植物栏 = **耕地的镜像**（用户原话："以耕地为唯一真相"）★★
        ///
        /// ── 改前（v305）读的是什么 ──
        ///   `s.Bag[i]`（**种子袋**的槽位）⇒ 那是"我买了什么种子"，不是"地里长了什么"。
        ///   训练场里 `StartWave` 永不运行（`NoWaves`）⇒ 袋与地**从来没有被对齐过**
        ///   ⇒ 袋里 1 格、地里 2 列；或者袋里 0 格、地里还长着 ✗（根因三条见 `SowOnPlot`）
        ///
        /// ── 现在读的是什么 ──
        ///   `World.Farm`：逐**列**看列首 `FarmPlot`，判据只有战斗自己那两个字段
        ///     · `State`（`Empty` / `Locked` = 这一列没东西）
        ///     · `CropDef`（品种）、`Level`、`GrowthProgress01`（生长百分比）
        ///   ⇒ "这一栏里显示的"与"地里长着的"**是同一组字段**，不可能不一致 ✓
        ///
        /// ── 每帧都跑，但每帧分配 ≈ 0 ──
        ///   成员（第几列）**每帧重算**（几次比较，无分配）；
        ///   文本只在"列号 / 等级 / 状态 / 百分比"这四件事**变了**时才重建
        ///   （百分比是整数 ⇒ 生长一整轮最多 100 次重建，而不是 60 帧/秒 × N 行）✓
        /// </summary>
        private void EnsurePlants(RunSession s)
        {
            FarmGrid farm = s != null && s.World != null ? s.World.Farm : null;
            if (farm == null) { _plantCount = 0; return; }

            int cap = Mathf.Max(1, farm.Columns);
            if (_plantSlots == null || _plantSlots.Length < cap)
            {
                _plantSlots = new int[cap];
                _plantLabel = new string[cap];
                _plantSig = new int[cap];
                for (int i = 0; i < cap; i++) _plantSig[i] = int.MinValue;
            }

            int n = 0;
            for (int col = 0; col < farm.Columns; col++)
            {
                FarmPlot head = farm.ColumnHead(col);
                if (head == null) continue;
                // ★ 唯一真相就是这两个字段 —— 本文件不记"我种过什么"
                if (head.State == PlotState.Empty || head.State == PlotState.Locked) continue;
                if (head.CropDef == null) continue;

                int pct = Mathf.Clamp(Mathf.RoundToInt(head.GrowthProgress01 * 100f), 0, 100);
                int sig = col * 1000000 + Mathf.Clamp(head.Level, 0, 9) * 10000
                        + (int)head.State * 100 + pct;

                _plantSlots[n] = col;
                if (_plantSig[n] != sig)
                {
                    _plantSig[n] = sig;
                    _plantLabel[n] = "Lv" + head.Level + " · " + PlotStateText(head.State)
                                   + " " + pct + "%"
                                   + (head.Thirsty ? " · 缺水" : "");
                }
                n++;
            }
            // 行数变少时把残余签名作废，避免下次"碰巧同一个 sig"导致不重建文本
            for (int i = n; i < cap; i++) _plantSig[i] = int.MinValue;
            _plantCount = n;
        }

        /// <summary>地块状态的中文读数（只给植物栏用）。</summary>
        private static string PlotStateText(PlotState st)
        {
            switch (st)
            {
                case PlotState.Grown: return "已成熟";
                case PlotState.Growing: return "生长中";
                case PlotState.Seeded: return "已播种";
                case PlotState.Dug: return "已翻土";
                case PlotState.Locked: return "未开垦";
                default: return "空地";
            }
        }

        // ── 中上：详情文本 ──

        private static Cat _dCat = (Cat)(-1);
        private static int _dSel = -1, _dRev = -1;

        /// <summary>
        /// ★★ v309：详情栏**分区重做**之后的三段文本 + 数值区的定长缓冲 ★★
        ///
        ///   名称 20px（`_dName`）／档位一行（`_dGrade`）／状态一行（`_dState`）
        ///   数值区 **一行一条**（`_dNum` + `_dNumCount`，最多 `MaxNumRows` 行可见，超出滚动）
        ///   说明区全文（`_dDesc`，wordWrap + 滚动，高度按宽度量好存 `_descH`）
        ///
        /// ★ 改前是什么样 ★
        ///   数值是 `_dNums` **一个** 38px 高的单行 `_small` label：条目一多就被裁掉，
        ///   而且里面混着 `id=xxx`、`amount 0.5`、`effect Shock`、`stackRule Stackable`
        ///   这类**内部字段名**（用户原话："不要提示一些与玩家体验无关的内容"）。
        ///
        /// ★ 缓存纪律不变 ★ 全部只在 `EnsureDetail` 里重建，判据仍是 `_rev`（类注释 六）⇒
        ///   每帧只做一次 `int` 比较；`_dNum` 是**定长复用**缓冲，只有行数变大时才 `Array.Resize`。
        /// </summary>
        private static string _dName, _dGrade, _dState, _dDesc;
        private static string[] _dNum = new string[MaxNumRows];
        private static int _dNumCount;
        private static float _descH;

        /// <summary>数值区一次最多显示几行（超出的走它自己的滚动条）。</summary>
        private const int MaxNumRows = 6;

        // ── 详情栏分区偏移（相对 `area` 原点；`area` = 600 × 372）─────────────
        //   36  图标 96×96 → 8..104              38  名称 20px → 4..32
        //   39  档位 13px → 32..50               40  状态 14px bold → 50..68
        //   41  按钮行 h26 → 72..98（右侧"将装到：…"同一行）
        //   104 ───── 分隔线 1 ─────
        //   108  「数　值」标签 → 124
        //   126  数值视口 568 × 102（6 行 × 17）→ 228
        //   232 ───── 分隔线 2 ─────
        //   236  「说　明」标签 → 252
        //   256  说明视口 568 × 106（到底 362，留 10px 内边距）
        private const float DtIconX = PadX, DtIconY = 8f, DtIconS = 96f;
        private const float DtTextX = 122f, DtTextW = 462f;
        private const float DtNameY = 4f, DtNameH = 28f;
        private const float DtGradeY = 32f;
        private const float DtStateY = 50f;
        private const float DtBtnY = 72f, DtBtnH = 26f;
        private const float DtDiv1Y = 104f;
        private const float DtNumLblY = 108f;
        private const float DtNumTopY = 126f, DtNumRowH = 17f;
        private const float DtNumH = MaxNumRows * DtNumRowH;
        private const float DtDiv2Y = 232f;
        private const float DtDescLblY = 236f;
        private const float DtDescTopY = 256f;

        /// <summary>
        /// ★ v308：删掉详情栏那 3 颗"锄头 / 水壶 / 肥料"按钮之后，用来**代替它们**的两件事：
        ///   `_dTarget`   = 一行文字（"将装到：锄头（2/6）" / "三把武器插件位已满 —— …"）；
        ///                  `WeaponSlot.None` 时给"已满"那句。
        ///   `_canInstall`= 这**一件**插件当前装不装得上（只对「装上 +」按钮生效：false ⇒ 灰化）。
        /// 两者与 `_dState` 同样走 `_rev` 版本号缓存 ⇒ 不在 `OnGUI` 里拼字符串 ✓
        /// </summary>
        private static string _dTarget = "";
        private static bool _canInstall = true;

        private void EnsureDetail(RunSession s, float descW)
        {
            int sel = SelOf(_cat);
            if (_dCat == _cat && _dSel == sel && _dRev == _rev && _dDesc != null) return;
            _dCat = _cat; _dSel = sel; _dRev = _rev;

            NumReset();
            _dTarget = "";          // 只有插件栏写它；切到别栏时清掉，绝不留上一件的"将装到"
            _canInstall = true;

            if (_cat == Cat.Items)
            {
                ItemDef d = TableDatabase.Items != null && sel < TableDatabase.Items.Length
                    ? TableDatabase.Items[sel] : null;
                if (d == null) { _dName = "—"; _dGrade = ""; _dState = ""; _dDesc = "表里没有这一条"; }
                else
                {
                    int have = 0;
                    if (s != null) s.ItemStacks.TryGetValue(d.Id, out have);
                    _dName = d.Name;
                    // 档位（D..S）与稀有度（普通..传奇）是**同一个档位的两套叫法** ⇒ 一行写完，不重复
                    _dGrade = "档位 " + GradeText(d.Grade) + "（" + Rarity.NameOf(d.RarityTier) + "）";
                    _dState = have > 0
                        ? "★ 已装备 ×" + have + "（本仓最多 " + ItemCap(d) + " 件）"
                        : "未装备（点下面「装上 +」）";

                    // ── 数值：**一条一行**（改前挤在一个 38px 的单行 label 里，长了就被裁掉）──
                    //   ① `effects` 每条一条 ② `costs` 每条一条（前缀「代价 · 」，值取负号）
                    //   ③ `rule` 一条（`ItemRules.Describe` 的中文，`N` 换成表里的 `ruleValue`）
                    //   ④ 成长（只有 `growPerWave ≠ 0` 的几件才有）⑤ 价格
                    PushBonusRows(d.Effects, +1f, "");
                    PushBonusRows(d.Costs, -1f, "代价 · ");
                    string rule = RuleRow(d);
                    if (rule != null) NumRow(rule);
                    if (d.GrowPerWave != 0f)
                        NumRow("成长 · 每回合 " + (d.GrowPerWave > 0f ? "+" : "") + d.GrowPerWave.ToString("0.##"));
                    NumRow("价格 · " + d.Price + " 金币");

                    // 功能全文：items.json 的 `desc` 一个字不裁（`_wrap` + 独立滚动条）
                    _dDesc = string.IsNullOrEmpty(d.Desc)
                        ? "（items.json 里这一条没有 desc —— 它的作用就是上方「数值」那几条）" : d.Desc;
                }
            }
            else if (_cat == Cat.Plants)
            {
                SeedDef d = TableDatabase.Seeds != null && sel < TableDatabase.Seeds.Length
                    ? TableDatabase.Seeds[sel] : null;
                if (d == null) { _dName = "—"; _dGrade = ""; _dState = ""; _dDesc = "表里没有这一条"; }
                else
                {
                    bool inBag = s != null && s.Bag != null && s.Bag.Find(d.Id, null) != null;
                    int cols = PlantedColumnsOf(s, d.Id);
                    _dName = d.Name;
                    _dGrade = "档位 " + GradeText(d.Grade) + "（" + Rarity.NameOf(d.RarityTier) + "）";
                    // ★ v306：状态行以**耕地**为准（袋里有几格只是"能不能再种"的信息）
                    _dState = cols > 0
                        ? "★ 已在耕地占 " + cols + " 列" + (inBag ? "（种子袋 " + s.Bag.OccupiedCount + "/" + s.Bag.SlotCount + "）" : "")
                        : (inBag ? "在种子袋里，但耕地上一列都没有（点「种下」对齐）"
                                 : "未种植（点下面「种下」= 入袋并种到耕地）");

                    // 机制的中文名与中文说明都取自 `PlantMechanics.Describe`（那份文案的唯一来源），
                    // 表里的 `mechanic` 是内部键（`Haste` / `DewDrop` …）⇒ **一个字都不上屏**。
                    string mechDesc = string.IsNullOrEmpty(d.Mechanic) ? "" : PlantMechanics.Describe(d.Mechanic);
                    string mechName = MechName(d.Mechanic, mechDesc);

                    // ── 数值：一条一行 ──
                    NumRow("成熟 · " + d.GrowTime.ToString("0.#") + " 秒");
                    // ★ v310：写清"每格**每次**收获" —— 一列有 N 格，每收一次结一次这份钱
                    NumRow("每格每次收获 · 金币 " + SeedEconomy.CellGold(d) + " · 经验 " + SeedEconomy.CellExp(d));
                    NumRow("蓄水 · 上限 " + d.WaterMax + "，每 5 秒 -" + d.WaterDrain);
                    NumRow("种子售价 · " + d.Price + " 金币");
                    NumRow("机制 · " + mechName);

                    // 说明区只放**机制那一句**（数值已在上面，不重复第二遍）。
                    // `seeds.json` 自 v196 起没有 desc 字段 ⇒ 这里是"表内字段 + PlantMechanics"拼的，
                    // 但不写"本段由 XXX 拼出"这种实现说明（与玩家体验无关）。
                    _dDesc = string.IsNullOrEmpty(mechDesc)
                        ? "这一株没有特殊机制 —— 它的差别全在上方「数值」那几条。"
                        : mechDesc;
                }
            }
            else
            {
                PluginAsset p = TableDatabase.Plugins != null && sel < TableDatabase.Plugins.Length
                    ? TableDatabase.Plugins[sel] : null;
                if (p == null) { _dName = "—"; _dGrade = ""; _dState = ""; _dDesc = "表里没有这一条"; }
                else
                {
                    int cnt = PluginCountInstalled(s, p);
                    int pend = PluginCountPending(s, p);
                    _dName = p.displayName;
                    _dGrade = "品质 " + SeedEconomy.QualityName(p.quality)
                            + "（" + Rarity.NameOf((int)p.quality) + "）";
                    // ★ v308：已装 / 背包 分开说（改前把背包件也算"已装"，见 `PluginCountInstalled`）
                    _dState = cnt > 0
                        ? "★ 已装 " + cnt + " 处（每把武器 6 格）"
                          + (pend > 0 ? " · 背包还有 " + pend + " 件待装" : "")
                        : (pend > 0 ? "背包 ×" + pend + " —— 还没装到任何武器（点「装上 +」）"
                                    : "未安装（点下面「装上 +」）");

                    // ── 数值：一条一行 ──
                    //   改前是 `amount 0.5　amount2 0　叠加 Stackable　层数上限 3　id=xxx` ——
                    //   四个内部字段名 + 一个原始枚举串，玩家读不出任何东西。
                    //   ★ v310：「主参数 0.4　次参数 0.35」换成 `ParamRows` 的**语义说明表**
                    //     （每个 effect 说清"这个数是什么、单位是什么、作用于谁"，28 条全覆盖）
                    NumRow("适用武器 · " + FitsText(p));
                    NumRow("叠加 · " + StackRuleLabel(p));
                    ParamRows(p);
                    NumRow("商店价 · " + p.basePrice + " 金币");

                    // 说明 = **效果的中文含义** + plugins.json 的 `description` 全文。
                    // 效果的唯一说法来源是 `WeaponSystem.ChannelOf`（自检钉住"每档都有说法"）⇒
                    // 本文件不另写第二份效果名表 ✓
                    string eff = WeaponSystem.ChannelOf(p.effect);
                    bool noEff = string.IsNullOrEmpty(eff) || eff == "无";
                    _dDesc = "效果：" + (noEff ? "（表里这一条没写效果）" : eff)
                           + "\n\n"
                           + (string.IsNullOrEmpty(p.description)
                                ? "（plugins.json 里这一条没有 description）" : p.description);

                    // ★★ v308：详情栏那 3 颗"锄头 / 水壶 / 肥料"按钮已删 ⇒ 换成**这一行文字** ★★
                    //   `_dTarget`   = "将装到：锄头（2/6）" / "三把武器插件位已满 —— 先卸下一格"
                    //   `_canInstall`= 三把里是否至少有一把能装（false ⇒「装上 +」按钮灰化）
                    //   两者都在这里算（`_rev` 版本号缓存）⇒ 每帧只做一次 int 比较，不拼字符串 ✓
                    WeaponSlot fit = PreviewTargetSlot(s, p);
                    _canInstall = fit != WeaponSlot.None;
                    _dTarget = s == null || s.Goods == null
                        ? "（会话未就绪，等一帧）"
                        : _canInstall
                            ? "将装到：" + RunSession.WeaponSlotName(fit)
                              + "（" + s.Goods.SlotsUsedOn(fit)
                              + "/" + PlayerGoods.MaxPluginsPerWeapon + "）"
                            : "三把武器插件位已满 —— 先卸下一格";
                }
            }

            _descH = _wrap != null ? _wrap.CalcHeight(new GUIContent(_dDesc), Mathf.Max(60f, descW)) : 220f;
        }

        // ── 右上武器 / 顶栏计数（避免每帧拼字符串）──

        private static string[] _weapLabel = new string[3];
        private static string _headCount = "";
        private static int _weapRev = -1;

        private void EnsureWeaponCounts(RunSession s)
        {
            if (_weapRev == _rev) return;
            _weapRev = _rev;
            for (int i = 0; i < WeaponOrder.Length; i++)
            {
                int used = s != null && s.Goods != null ? s.Goods.SlotsUsedOn(WeaponOrder[i]) : 0;
                _weapLabel[i] = RunSession.WeaponSlotName(WeaponOrder[i]) + " " + used
                              + "/" + PlayerGoods.MaxPluginsPerWeapon;
            }
            _headCount = s == null ? ""
                : "道具 " + s.ItemStacks.Count + " 种 · 插件 " + InstalledPlugins(s) + " 件"
                + " · 植物 " + PlantedColumnCount(s) + "/" + FarmColumnCap(s) + " 列（耕地实况）";
        }

        /// <summary>耕地上真正长着东西的列数（`EnsurePlants` 的同一个判据）。</summary>
        private static int PlantedColumnCount(RunSession s)
        {
            FarmGrid farm = s != null && s.World != null ? s.World.Farm : null;
            if (farm == null) return 0;
            int n = 0;
            for (int col = 0; col < farm.Columns; col++)
            {
                FarmPlot head = farm.ColumnHead(col);
                if (head == null) continue;
                if (head.State == PlotState.Empty || head.State == PlotState.Locked) continue;
                if (head.CropDef == null) continue;
                n++;
            }
            return n;
        }

        private static int FarmColumnCap(RunSession s)
        {
            FarmGrid farm = s != null && s.World != null ? s.World.Farm : null;
            return farm != null ? farm.Columns : 0;
        }

        // ── 顶栏金币 ──

        private static string _goldText = "金币 —";
        private static int _goldShown = int.MinValue;

        private void EnsureGold(RunSession s)
        {
            int g = s != null ? s.Gold : -1;
            if (_goldShown == g) return;
            _goldShown = g;
            _goldText = g < 0 ? "金币 —" : "金币 " + g;
        }

        // ══════════════════════════════════════════════════════════════════
        // 文本小工具
        // ══════════════════════════════════════════════════════════════════

        private static readonly string[] GradeLetters = { "D", "C", "B", "A", "S" };
        private static string GradeText(SeedGrade g) => GradeLetters[Mathf.Clamp((int)g, 0, 4)];

        // ══════════════════════════════════════════════════════════════════
        // ★★ v309：数值区「一行一条」的拼装（全部只在 `EnsureDetail` 里跑一次）★★
        // ══════════════════════════════════════════════════════════════════

        /// <summary>清空数值区（每次重建详情时先调一次）。</summary>
        private static void NumReset() { _dNumCount = 0; }

        /// <summary>
        /// 往数值区加一行。缓冲是**定长复用**的（`_dNum` 初长 `MaxNumRows`），
        /// 只有行数超过它时才 `Array.Resize`（+8）—— 正常条目（≤6 行）一次分配都不会发生。
        /// </summary>
        private static void NumRow(string text)
        {
            if (_dNumCount >= _dNum.Length) System.Array.Resize(ref _dNum, _dNum.Length + 8);
            _dNum[_dNumCount++] = text;
        }

        /// <summary>
        /// 把一组增益 / 减益压成"一条一行"：`近战伤害（锄头）+2` / `攻击速度（三把武器）-3%` /
        /// `代价 · 最大生命 -5`。
        /// ⚠️ 值为 0 的条目**跳过**（与 `ImGuiHud.JoinBonusParts`（`ImGuiHud.cs:2566`）同一判据）——
        ///    7 件纯机制道具的 `effects` 是空的，`ItemDef.Add` 会兜成 0 ⇒ 不跳就会印出「最大生命 +0」。
        /// ★ v310：括号里的"作用于什么"由 `StatScope` 给（用户原话："说明数值是作用于什么"）——
        ///    `近战伤害 +2` 改前读不出是"哪把武器的近战"，现在写清「锄头」。
        /// </summary>
        private static void PushBonusRows(ItemBonus[] list, float sign, string prefix)
        {
            if (list == null) return;
            for (int i = 0; i < list.Length; i++)
            {
                ItemBonus b = list[i];
                if (b == null) continue;
                if (Mathf.Abs(b.Add) < 0.0001f) continue;
                string label = StatLabel(b.Stat);
                string scope = StatScope(b.Stat);
                NumRow(prefix + (scope == null ? label : label + "（" + scope + "）") + " " + ValText(b, sign));
            }
        }

        /// <summary>
        /// ★ v310：这条增益/代价**作用在哪把武器上**（`null` = 不写括号）。
        ///
        /// 判据只有 `StatId` 一个输入，逐条对照 items.json 的 desc 与 `RunSession` 的结算点：
        ///   · `MeleeDamage`   → 锄头   （v162 起三种伤害属性各自对应一把武器，`Defs.cs:38-41`）
        ///   · `RangedDamage`  → 水壶
        ///   · `BlastDamage`   → 肥料
        ///   · `ExplodeRange`  → 肥料   （爆炸半径是肥料独有的量）
        ///   · `AtkMult`       → 三把武器（`(武器基础 + 对应伤害属性) × 伤害倍率`，三把同一个式子；
        ///                        items.json 里 `gale_vane` 的 desc 原话就是"三把武器一起受益"）
        ///   · `AtkSpeed`      → 三把武器（items.json 里 `hot_blood_powder` 的 desc 原话"三把武器一起变快"）
        ///   · `AtkRange`      → 锄头·水壶（items.json 里 `thunder_ledger` 的 desc 同时给出锄头与水壶两个数）
        ///   · 其余（生命 / 护盾 / 移速 / 暴击 / 掉落 / 种植数 …）→ `null`：
        ///     名字本身已经说清作用对象，再加一层括号只是噪音
        /// </summary>
        private static string StatScope(StatId s)
        {
            switch (s)
            {
                case StatId.MeleeDamage: return "锄头";
                case StatId.RangedDamage: return "水壶";
                case StatId.BlastDamage: return "肥料";
                case StatId.ExplodeRange: return "肥料";
                case StatId.AtkMult: return "三把武器";
                case StatId.AtkSpeed: return "三把武器";
                case StatId.AtkRange: return "锄头·水壶";
                default: return null;
            }
        }

        /// <summary>
        /// ★ 属性的中文名 —— 与 `ImGuiHud.StatName`（`ImGuiHud.cs:2367`）**逐字同源** ★
        ///
        /// 为什么这里有一份拷贝：那是 `private static`，本任务的改动范围限定在本文件
        /// ⇒ 拿不到就只能照抄一份，**改属性名时必须两处一起改**（两张表一旦漂移，
        /// 面板与仓库就会把同一个属性叫成两个名字 —— 本工程吃过"传奇被画成紫色"那次教训）。
        /// 未知属性回落枚举名（与 `ImGuiHud.StatName` 的 default 分支一致）。
        /// </summary>
        private static string StatLabel(StatId s)
        {
            switch (s)
            {
                case StatId.MaxHp: return "最大生命";
                case StatId.Shield: return "护盾";
                case StatId.MeleeDamage: return "近战伤害";
                case StatId.RangedDamage: return "远程伤害";
                case StatId.BlastDamage: return "爆炸伤害";
                case StatId.AtkMult: return "伤害倍率";
                case StatId.ExplodeRange: return "爆炸范围";
                case StatId.AtkSpeed: return "攻击速度";
                case StatId.AtkRange: return "攻击范围";
                case StatId.MoveSpeed: return "移动速度";
                case StatId.CritRate: return "暴击率";
                case StatId.CritDmg: return "暴击伤害";
                case StatId.Knockback: return "击退力";
                case StatId.Dodge: return "闪避率";
                case StatId.Luck: return "幸运值";
                case StatId.ExpMult: return "经验倍数";
                case StatId.GoldMult: return "货币倍数";
                case StatId.HeartDropRate: return "爱心掉落率";
                case StatId.PigRate: return "小猪刷新率";
                case StatId.HeartHeal: return "爱心恢复量";
                case StatId.MaxPlots: return "可种植株数";
                default: return s.ToString();
            }
        }

        /// <summary>
        /// 一条增益 / 减益的**数值写法**（带正负号）。四种单位分开算，口径与
        /// `ImGuiHud.BonusValueText`（`ImGuiHud.cs:2592`）**逐条相同**：
        ///   · `mode: Percent`（`IsPercent`）⇒ `-3%`（乘算，`RunSession.WriteBonus` 走 `1±p/100`）
        ///   · `Dodge` ⇒ 表里是**整数百分点**（`6` = 6%，写入时 /100）⇒ 补 `%`
        ///   · `CritRate` / `Luck` ⇒ 表里是 **0..1 小数**（`IsFractionStat` 那一条，
        ///     `TableDatabase.cs:579`）⇒ **×100 再补 `%`**（`0.04` 印成 `+4%`，不是 `+0.04`）
        ///   · 其余 ⇒ 裸点数 `+2` / `-5`
        /// `sign` = +1 增益 / −1 代价（表里 `costs` 一律写正数）。
        /// </summary>
        private static string ValText(ItemBonus b, float sign)
        {
            float v = sign * b.Add;
            bool pct = b.IsPercent || b.Stat == StatId.Dodge;
            if (!b.IsPercent && TableDatabase.IsFractionStat(b.Stat))
            {
                v *= 100f;
                pct = true;
            }
            return (v >= 0f ? "+" : "") + v.ToString("0.##") + (pct ? "%" : "");
        }

        /// <summary>
        /// 道具的机制那一行（`ItemRules.Describe` 的中文说明；`N` 换成表里的 `ruleValue`）。
        /// 没有机制（或键没登记）⇒ null（调用点不添这一行）。
        /// 改前这一行印的是 `规则 reroll_discount`、`作用于: prog:keyrush` 之类的**内部键**。
        /// </summary>
        private static string RuleRow(ItemDef d)
        {
            if (d == null || !d.HasRule) return null;
            string txt = ItemRules.Describe(d.Rule);
            if (string.IsNullOrEmpty(txt)) return null;
            if (d.RuleValue > 0f) txt = txt.Replace("N", d.RuleValue.ToString("0.##"));
            return "机制 · " + txt;
        }

        /// <summary>插件的叠加规则（`StackRule` 的中文说法 + `maxStack` 层数），一句说完。</summary>
        private static string StackRuleLabel(PluginAsset p)
        {
            if (p == null) return "—";
            int cap = Mathf.Max(1, p.maxStack);
            string rule;
            switch (p.stackRule)
            {
                case StackRule.Unique: rule = "唯一，只能装 1 个"; break;
                case StackRule.Multiplicative: rule = "可叠（乘算），最多 " + cap + " 层"; break;
                default: rule = "可叠，最多 " + cap + " 层"; break;
            }
            if (p.exclusivePerWeapon) rule += " · 同一把武器只算一件";
            return rule;
        }

        /// <summary>
        /// ★★ v310：插件参数的**语义说明表**（用户原话："功能详细说明数值是作用于什么，
        /// 而不是一个参数"）★★
        ///
        /// ── 改前是什么样 ──
        ///   数值区印的是 `主参数 0.4（比例 40%）　次参数 0.35` ——
        ///   玩家读不出"这 0.4 是什么的 40%"，而且"主参数 / 次参数"本身就是内部叫法。
        ///
        /// ── 现在 ──
        ///   每个 effect 一行或两行：`参数叫什么 · 数值 + 作用对象 + 单位`。
        ///   数值**仍然从表里那一格算**（`amount` / `amount2`），不是抄在文案里的死数 ——
        ///   改表就跟着改，卡面不会与生效值脱钩。
        ///
        /// ── 名字与单位从哪来（唯一真相）──
        ///   逐条对着 `WeaponSystem.Aggregate` 里**消费它的那一行**，例：
        ///     `case Echo: m.EchoPct += total;`            ⇒ `amount` = 延迟结算的**比例**
        ///                 `m.EchoDelay = Max(…, amount2);`⇒ `amount2` = **延迟秒数**
        ///     `case Shock: m.ShockTargets = 常量 × stacks` ⇒ 表里 `amount` = **每层目标数**（镜像值）
        ///     `case Wave:  m.WaveRadius = 基础 × (1 + 50%×(层−1))` ⇒ `amount2` = 浪的**基础半径（米）**
        ///   7 个 v290 机制的数值真相在 `PluginExtra2` 顶部常量区，表里的两列是**镜像值**
        ///   （`PluginExtra2.cs:39-42` 写明）⇒ 这里读表即可，数值与常量为同一组数。
        ///
        /// ── 三条硬约束 ──
        ///   ① **不出现内部字段名**：屏上不出现 `amount` / `amount2` / `effect` / 原始枚举串；
        ///   ② 28 个 effect **全覆盖**（与 `WeaponSystem.ChannelOf` 同一集合，自检钉着"每档都有说法"），
        ///      `None` / 未登记值走兜底，不打印任何键名；
        ///   ③ 只在 `EnsureDetail` 里跑一次（`_rev` 缓存）⇒ 不违反"每帧分配 ≈ 0"。
        ///
        /// ⚠️ 这张表是**训练场专属**的（本文件私有），战斗侧一个字都不改。
        /// </summary>
        private static void ParamRows(PluginAsset p)
        {
            if (p == null) { NumRow("参数 · —"); return; }

            // ── 数值小工具：三个 **static 局部函数**（不捕获任何东西 ⇒ 编译成普通方法调用，
            //   不产生委托 / 闭包分配；比例 ×100 是 `Goods.cs:72-75` 的成文约定，不在这里另立一条）
            static string Pct(float v) => (v * 100f).ToString("0.#") + "%";
            static string Int(float v) => Mathf.RoundToInt(v).ToString();
            static string Dec(float v) => v.ToString("0.##");

            switch (p.effect)
            {
                // ══════════════════ 锄头 ══════════════════
                case PluginEffectKind.SwordWave:
                    NumRow("剑气道数 · 每层 +" + Int(p.amount) + " 道（射程 = 近战范围 ×2，只打挥击框外的敌人）");
                    break;

                case PluginEffectKind.BurnOnHit:
                    NumRow("灼烧伤害 · 每层每秒 = 近战伤害的 " + Pct(p.amount) + "，共烧 5 秒");
                    // 层数来自"装了几层"（`Aggregate` 里 `BurnLayersPerHit += stacks`），表里那格是镜像值
                    // ⇒ 显示取 `max(1, 表值)`：层数至少 1，绝不印出"附 0 层"这种死句子
                    NumRow("附着层数 · 每次命中附 " + Int(Mathf.Max(1f, p.amount2)) + " 层（时长不叠，每秒伤害按层乘）");
                    break;

                case PluginEffectKind.DoubleStrike:
                    NumRow("额外刀数 · 每层 +" + Int(p.amount) + " 刀（每刀独立判暴击，第 k 刀延迟 0.12×k 秒）");
                    break;

                case PluginEffectKind.Lifesteal:
                    NumRow("吸血比例 · 造成伤害的 " + Pct(p.amount) + " 转成自己的生命（对持续伤害不生效）");
                    break;

                case PluginEffectKind.Echo:
                    NumRow("延迟伤害 · 这一刀的 " + Pct(p.amount) + "，在同一个敌人身上再结算一次");
                    NumRow("延迟时间 · " + Dec(p.amount2) + " 秒后补这一下（时长不随层数变长）");
                    break;

                case PluginEffectKind.Focus:
                    NumRow("触发节奏 · 对同一个敌人每命中 " + Int(p.amount) + " 次触发一次（脱手即清零）");
                    NumRow("处决伤害 · 触发那一下追加本次伤害的 " + Pct(p.amount2));
                    break;

                case PluginEffectKind.Shock:
                    NumRow("电击目标数 · 每层 +" + Int(p.amount) + " 个（3 层 = 9 个，以主目标为圆心 3.5 米内）");
                    NumRow("电击伤害 · 每个被电到的敌人吃这一刀伤害的 " + Pct(p.amount2));
                    break;

                case PluginEffectKind.Frenzy:
                    NumRow("加伤 · 目标已残血时，这一击 +" + Pct(p.amount) + "（按层叠加）");
                    NumRow("生效血量 · 只在「目标」生命低于 " + Pct(p.amount2) + " 时生效（不是你自己残血）");
                    break;

                case PluginEffectKind.Giant:
                    NumRow("判定形状 · 从「面前半圆」改成「环绕玩家一整圈」");
                    NumRow("内径 · 外径的 " + Pct(p.amount) + "（外径沿用近战范围，只此一件、不可叠层）");
                    break;

                // ══════════════════ 水壶 ══════════════════
                case PluginEffectKind.MultiJet:
                    NumRow("额外水柱 · 每层 +" + Int(p.amount) + " 道，呈扇形铺开（每道独立扣水）");
                    break;

                case PluginEffectKind.SlowOnHit:
                    NumRow("减速 · 每层 −" + Pct(p.amount) + " 移速（叠满也只在 −60% 处封底）");
                    NumRow("持续伤害 · 被减速的敌人每秒受远程伤害的 " + Pct(p.amount2) + "，共 5 秒");
                    break;

                case PluginEffectKind.ThickJet:
                    NumRow("水柱宽度 · +" + Pct(p.amount) + "（每层再叠一份；只改宽不改长，射程仍是水壶自己的）");
                    break;

                case PluginEffectKind.AutoCharge:
                    NumRow("自动回水 · 每秒 +" + Dec(p.amount) + " 点水量（回的是弹药，上限 = 水壶容量）");
                    break;

                case PluginEffectKind.Ricochet:
                    NumRow("弹射伤害 · 水花跳到下一个敌人时，吃这一束的 " + Pct(p.amount));
                    NumRow("跳跃距离 · " + Dec(p.amount2) + " 米内找目标（找不到就只结算原目标）");
                    break;

                case PluginEffectKind.Ripen:
                    NumRow("催熟 · 水柱扫过「已种下但没成熟」的地块时，每次推进 " + Dec(p.amount) + " 秒成长");
                    break;

                case PluginEffectKind.Tide:
                    NumRow("推开距离 · " + Dec(p.amount) + " 米（沿「玩家 → 敌人」方向，0.25 秒内推完、期间敌人不能自主移动）");
                    break;

                case PluginEffectKind.Sewer:
                    NumRow("掉皮节奏 · 每累计消耗 " + Int(p.amount) + " 点水，在玩家身边掉 1 个香蕉皮");
                    NumRow("香蕉皮伤害 · 踩到的敌人吃水壶伤害的 " + Pct(p.amount2) + "（判定半径 1.7 米）");
                    break;

                case PluginEffectKind.Wave:
                    NumRow("移速 · 喷水的瞬间 +" + Pct(p.amount) + " 移速，持续 1 秒");
                    NumRow("浪半径 · " + Dec(p.amount2) + " 米（每层再 +50% 宽；被扫到的敌人吃水壶伤害的 30%）");
                    break;

                // ══════════════════ 肥料 ══════════════════
                case PluginEffectKind.SubBurst:
                    NumRow("子爆个数 · 每层 +" + Int(p.amount) + " 个（落在主爆外的六角方向上）");
                    NumRow("子爆半径 · 主爆半径的 " + Pct(p.amount2) + "（子爆伤害同样按这个比例走）");
                    break;

                case PluginEffectKind.PoisonZone:
                    NumRow("毒区伤害 · 每层每秒 = 爆炸伤害的 " + Pct(p.amount) + "（毒区在地上留 5 秒，后走进来照样掉）");
                    break;

                case PluginEffectKind.FreeBurst:
                    NumRow("免耗概率 · 每次投掷有 " + Pct(p.amount) + " 不扣肥料充能（按层叠加，满 100% 即必不耗）");
                    break;

                case PluginEffectKind.ChainBurst:
                    NumRow("额外爆炸 · 每层 +" + Int(p.amount) + " 次（半径与伤害都等同主爆）");
                    NumRow("第二爆点 · 落在主爆心外 " + Dec(p.amount2) + " 米处");
                    break;

                case PluginEffectKind.Mine:
                    NumRow("踩中倍率 · 敌人走进感应圈（2.2 米）踩到时，按 " + Pct(p.amount) + " 伤害引爆 —— 这是主用法");
                    NumRow("超时倍率 · 没人踩时：落地 0.6 秒上膛 → 6 秒引信烧完自己炸，按 " + Pct(p.amount2) + " 伤害");
                    break;

                case PluginEffectKind.Ward:
                    NumRow("护盾 · 爆炸时「玩家自己」站进范围内才给 " + Dec(p.amount) + " 点额外护盾");
                    NumRow("护盾时长 · " + Dec(p.amount2) + " 秒（只给玩家，不是给作物）");
                    break;

                case PluginEffectKind.Mark:
                    NumRow("放大伤害 · 被炸到的敌人下一次受的任何伤害 ×(1 + " + Pct(p.amount) + ")");
                    NumRow("标记时长 · 最多挂 " + Dec(p.amount2) + " 秒（打一下就消耗掉）");
                    break;

                case PluginEffectKind.BlackHole:
                    NumRow("拉拢 · 爆炸结算之前把范围内的敌人吸向爆心（拉到半径的 55% 处），前摇 0.25 秒");
                    break;

                // ── 兜底：表里没写效果 / 写了没登记的键 ──
                case PluginEffectKind.None:
                default:
                    NumRow("参数 · 这一条在表里没有可调参数");
                    break;
            }
        }

        /// <summary>
        /// 植物机制的可读名 —— 取 `PlantMechanics.Describe` 那一句「：」之前的半句
        /// （那份文案是机制中文名的**唯一来源**，表里的 `mechanic` 是内部键，不上屏）。
        /// </summary>
        private static string MechName(string mechanic, string desc)
        {
            if (string.IsNullOrEmpty(mechanic)
                || string.Equals(mechanic, PlantMechanics.None, System.StringComparison.OrdinalIgnoreCase))
                return "无（纯数值差异）";
            if (string.IsNullOrEmpty(desc)) return "（无说明）";
            int i = desc.IndexOf('：');
            return i > 0 ? desc.Substring(0, i) : desc;
        }

        private static string FitsText(PluginAsset p)
        {
            if (p == null) return "—";
            if (p.allowedWeapons == null || p.allowedWeapons.Length == 0) return "任意";
            var sb = new System.Text.StringBuilder(24);
            for (int i = 0; i < p.allowedWeapons.Length; i++)
            {
                if (i > 0) sb.Append('/');
                sb.Append(RunSession.WeaponSlotName(p.allowedWeapons[i]));
            }
            return sb.ToString();
        }

        private static int InstalledPlugins(RunSession s)
        {
            if (s == null || s.Goods == null) return 0;
            int n = 0;
            for (int i = 0; i < s.Goods.Plugins.Count; i++)
                if (s.Goods.Plugins[i] != null && s.Goods.Plugins[i].Slot != WeaponSlot.None) n++;
            return n;
        }

        /// <summary>一行诊断读数（日志用；不画在面板上）。</summary>
        public static string Describe(RunSession s)
        {
            if (s == null) return "无会话";
            return "清单 道具 " + WishItems.Count + " 种 / 插件 " + WishPlugins.Count
                 + " 件 / 植物 " + WishSeeds.Count + " 种"
                 + "｜会话 道具 " + s.ItemStacks.Count + " 种 / 插件 " + InstalledPlugins(s) + " 件"
                 + " / 植物 " + s.Bag.OccupiedCount + "/" + s.Bag.SlotCount
                 + "｜耕地 " + PlantedColumnCount(s) + "/" + FarmColumnCap(s) + " 列（唯一真相）"
                 + "｜水壶 " + s.WaterAmmo + "/" + s.WaterAmmoCap
                 + " 肥料 " + s.FertAmmo + "/" + RunSession.FertAmmoMax
                 + "｜开关 " + TrainingMode.Describe();
        }

        // ══════════════════════════════════════════════════════════════════
        // 面板（IMGUI）
        // ══════════════════════════════════════════════════════════════════

        private void OnGUI()
        {
            // 与 `Update` 同一条双保险：训练开关没开就一帧都不画
            if (!TrainingMode.Active || !IsOpen) return;
            if (!_stylesReady) BuildStyles();

            // 压在 HUD 之上：本工程的 IMGUI 约定是 **`GUI.depth` 越小越靠上**
            //   `ImGuiHud` 从不改 depth（= 0）⇒ 本面板取 −5：盖住战斗 HUD，仍在 `LoginLoading`(−1000) 之上
            int prevDepth = GUI.depth;
            GUI.depth = -5;

            RunSession s = _boot != null ? _boot.Session : null;

            // ① 全屏压暗（画在设置 `GUI.matrix` 之前 ⇒ 用屏幕像素）
            Color prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), White());
            GUI.color = prevColor;

            // ② 虚拟分辨率：面板内所有坐标都是一个 1280×760 的固定画布
            float k = Mathf.Clamp(Mathf.Min(Screen.height / 900f, Screen.width / 1320f), 0.55f, 2.2f);
            Matrix4x4 prevMatrix = GUI.matrix;
            float ox = (Screen.width - PanelW * k) * 0.5f;
            float oy = (Screen.height - PanelH * k) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0f), Quaternion.identity,
                                       new Vector3(k, k, 1f));

            // ③ 缓存（版本号驱动：每帧只做 int 比较）
            EnsureIconKeys();
            EnsureRows(s);
            EnsureBag(s);
            EnsurePlants(s);
            EnsureWeaponCounts(s);
            EnsureGold(s);
            EnsureDetail(s, 546f);      // desc 视口宽 568 − 22（滚动条 + 内边距）

            // ④ 画
            DrawPanel(s);

            GUI.matrix = prevMatrix;
            GUI.depth = prevDepth;
        }

        private void DrawPanel(RunSession s)
        {
            var panel = new Rect(0f, 0f, PanelW, PanelH);
            Fill(panel, new Color(0.07f, 0.07f, 0.08f, 0.97f));
            Frame(panel, new Color(1f, 0.84f, 0.45f, 0.32f));
            Fill(new Rect(0f, 0f, PanelW, 56f), new Color(0.13f, 0.11f, 0.09f, 1f));
            Fill(new Rect(0f, 54f, PanelW, 2f), new Color(1f, 0.82f, 0.42f, 0.55f));

            DrawHeader(s);

            DrawLeft(s, new Rect(ColLeftX, BodyTop, ColLeftW, BodyBottom - BodyTop));
            DrawDetail(s, new Rect(ColMidX, BodyTop, ColMidW, TopBlockH));
            DrawBag(s, new Rect(ColMidX, BotBlockY, ColMidW, BotBlockH));
            DrawWeapons(s, new Rect(ColRightX, BodyTop, ColRightW, TopBlockH));
            DrawPlants(s, new Rect(ColRightX, BotBlockY, ColRightW, BotBlockH));

            // ── 底部状态条（★ v308 重做）──────────────────────────────────────
            //  改前：`GUI.Label(new Rect(20f, 742f, 1240f, 16f), …)` —— 面板下沿是 760，
            //        这行字**贴着边**（只剩 2px），字色又是最淡的 `_tiny`（0.68/0.66/0.62）⇒
            //        "悬空的一行淡字"，很难读。用户原话："调整仓库 UI 外面的文字位置和效果"。
            //  现在：① 先铺一块**自己的底色**（半透明深色，与顶栏 56px 那条对称）
            //        ② 顶沿再加**一条细金线**（1px，与顶栏 y=54 那条同色系、更淡一档）
            //        ③ 文字 13px、垂直居中、左边距 = 全局 `PadX`(16)
            //  ⚠️ 只改 rect / 颜色 / 多一条底色，一个字的内容没动 ✓
            Fill(new Rect(1f, StatusBarY, PanelW - 2f, PanelH - StatusBarY - 1f),
                 new Color(0.05f, 0.05f, 0.06f, 0.94f));
            Fill(new Rect(1f, StatusBarY, PanelW - 2f, 1f),
                 new Color(1f, 0.84f, 0.45f, 0.30f));
            GUI.Label(new Rect(PadX, StatusBarY + 13f, PanelW - PadX * 2f, 20f),
                string.IsNullOrEmpty(_status)
                    ? "B / E 关闭 · 左栏点条目 = 只选中 · 生效请按详情栏的按钮（训练场不要钱）"
                    : _status, _bar);
        }

        /// <summary>
        /// 顶栏（★ v308 重排）。改前五个 rect 的高度/基线各不相同（10 / 18 / 8 / 26），
        /// 而且"仓 库"标题（24..244）与副标题（150 起）**水平重叠 94px** ⇒ 挤在一起。
        /// 现在四个文本块垂直中心**统一在 y = 28**、互不重叠，并且左归左、右归右：
        ///   标题 x=24..156（24px 加粗）｜ 副标题 166..606（13px）｜ 已装计数右对齐到 950 ｜
        ///   金币右对齐到 1140 ｜ 关闭按钮 1156..1260
        /// </summary>
        private void DrawHeader(RunSession s)
        {
            GUI.Label(new Rect(24f, 8f, 132f, 40f), "仓 库", _title);
            GUI.Label(new Rect(166f, 17f, 440f, 22f),
                "训练场专用 · 全部道具 / 植物 / 插件免费 · 选中后按详情栏按钮生效", _small);

            // 已装计数：右对齐（改前是 `new Rect(700f, 8f, 300f, 20f)` 左对齐 + 基线上浮 10px）
            if (!string.IsNullOrEmpty(_headCount))
                GUI.Label(new Rect(620f, 17f, 330f, 22f), _headCount, _tinyR);
            // 金币：右对齐（改前 `new Rect(1000f, 26f, 160f, 22f)` 左对齐 —— 数字一变宽就抖）
            GUI.Label(new Rect(960f, 15f, 180f, 26f), _goldText, _num);

            if (GUI.Button(new Rect(1156f, 12f, 104f, 32f), "关闭 (E)", _btn))
                SetOpen(false);
        }

        // ── 左栏：分类页签 + 列表（图标 + 名称）──────────────────────────

        private void DrawLeft(RunSession s, Rect area)
        {
            Plate(area);

            // ★ v308：页签左边距 10 → `PadX`(16)，与其它四个区块的字左边缘对齐。
            //   视口跟着从 `area.x+10 / area.width-20` 移到 `area.x+16 / area.width-32`，
            //   纵向 46 → 48（页签底边 8+30=38，留 10px 行距）。
            float tx = area.x + PadX;
            float ty = area.y + 8f;
            if (TabButton(new Rect(tx, ty, TabW, TabH), 0, _cat == Cat.Items))
            { _cat = Cat.Items; SetSel(Cat.Items, SelOf(Cat.Items)); }
            tx += TabW + TabGap;
            if (TabButton(new Rect(tx, ty, TabW, TabH), 1, _cat == Cat.Plants))
            { _cat = Cat.Plants; SetSel(Cat.Plants, SelOf(Cat.Plants)); }
            tx += TabW + TabGap;
            if (TabButton(new Rect(tx, ty, TabW, TabH), 2, _cat == Cat.Plugins))
            { _cat = Cat.Plugins; SetSel(Cat.Plugins, SelOf(Cat.Plugins)); }

            var view = new Rect(area.x + PadX, area.y + 48f, area.width - PadX * 2f, area.height - 60f);

            // ★★ v307 修崩溃（v310 仍然生效）：`EnsureRows` 是在 `OnGUI` 开头按**当时的** `_cat`
            //   建缓存的，而上面三颗页签按钮会**在本帧就把 `_cat` 改掉** ⇒ 如果不在这里重算，
            //   紧随其后的行模型（改前是 `n = CountOf(_cat)`）会是**新类别**的条数，而
            //   `_rowName` / `_rowY` 还是**旧类别**的长度 ⇒ 越界（用户报的"打开植物栏直接报错"
            //   就是 植物 51 条 ↔ 道具 151 条 之间切换：从植物切到道具时 n=151 而数组只有 51）★
            //   修法：页签变更后**当场重算一次**（`EnsureRows` 内部有 `_rowCat != _cat` 判据，
            //   同一帧重复调用是幂等的，不会白算）。
            //   ★ v310 加固：缓存键多了**表长**（`_rowLen`）⇒ 连"表被换掉长度"这条也一并覆盖。
            EnsureRows(s);

            int n = _rowCount;      // ★ v310：行数由行模型给（含分组标题行），不再 = 表长
            // 兜底：万一将来又有人改了缓存策略，也绝不让它越界（宁少画一行，不崩整个面板）
            if (_rowName != null && n > _rowName.Length) n = _rowName.Length;
            if (_rowY == null || _rowIdx == null) n = 0;
            float contentW = view.width - 20f;
            // ★ v310：内容高照**行模型**取（标题行 24 / 条目行 40 混排，不再一律乘 RowHLeft）
            float contentH = Mathf.Max(view.height - 2f, _rowTotal + 4f);
            _scrollLeft = GUI.BeginScrollView(view, _scrollLeft, new Rect(0f, 0f, contentW, contentH));

            int sel = SelOf(_cat);      // ★ 表下标（不是行号）——见类注释 八

            for (int i = 0; i < n; i++)
            {
                float ry = _rowY[i];
                float rh = (i + 1 < n ? _rowY[i + 1] : _rowTotal) - ry;

                // ── 分组标题行（`_rowIdx[i] < 0`）：插件页的「锄　头 · 10 件」 ──
                //   不给按钮、不参与选中（它不是一件东西），只负责把组与组切开
                if (_rowIdx[i] < 0)
                {
                    Fill(new Rect(2f, ry + 3f, contentW - 4f, rh - 6f),
                         new Color(0.17f, 0.15f, 0.12f, 0.95f));
                    Fill(new Rect(2f, ry + 3f, 2f, rh - 6f), new Color(1f, 0.82f, 0.42f, 0.55f));
                    GUI.Label(new Rect(10f, ry + 3f, contentW - 16f, rh - 6f), _rowName[i], _groupHdr);
                    continue;
                }

                var rowR = new Rect(2f, ry + 2f, contentW - 4f, rh - 4f);
                int t = _rowIdx[i];                 // ★ 这一行的**表下标**
                bool on = t == sel;

                Color prevBg = GUI.backgroundColor;
                if (on) GUI.backgroundColor = new Color(0.38f, 0.52f, 0.42f);
                bool clicked = GUI.Button(rowR, GUIContent.none);
                GUI.backgroundColor = prevBg;

                // 图标 / 品阶色 / 点击后的选中，**全部走表下标**（`IconKeyOf` / `TierColorOf` 吃的是表下标）
                Color tier = TierColorOf(_cat, t);
                DrawIcon(new Rect(rowR.x + 4f, rowR.y + 3f, 30f, 30f), IconKeyOf(_cat, t), tier);
                GUI.Label(new Rect(rowR.x + 40f, rowR.y + 1f, 152f, 18f), _rowName[i], _rowLabel);
                GUI.Label(new Rect(rowR.x + 40f, rowR.y + 19f, 152f, 15f), _rowSub[i], _tiny);
                if (!string.IsNullOrEmpty(_rowMark[i]))
                    // ★ v308：宽 68 → 56 —— 改前右边界 194+68=262 越出整行（252），末字被裁。
                    GUI.Label(new Rect(rowR.x + 194f, rowR.y + 8f, 56f, 20f), _rowMark[i], _mark);
                if (on) Fill(new Rect(rowR.x, rowR.y, 2f, rowR.height), new Color(1f, 0.82f, 0.42f, 0.9f));

                if (clicked) SelectOrEquip(s, _cat, t);
            }
            GUI.EndScrollView();
        }

        private bool TabButton(Rect r, int which, bool on)
        {
            Color prev = GUI.backgroundColor;
            if (on) GUI.backgroundColor = new Color(0.42f, 0.56f, 0.44f);
            string text = _tabLabel != null && _tabLabel[which] != null ? _tabLabel[which] : "…";
            bool clicked = GUI.Button(r, text, _btn);
            GUI.backgroundColor = prev;
            return clicked && !on;
        }

        private string IconKeyOf(Cat c, int i)
        {
            if (c == Cat.Items) return _keyItems != null && i < _keyItems.Length ? _keyItems[i] : null;
            if (c == Cat.Plants) return _keySeeds != null && i < _keySeeds.Length ? _keySeeds[i] : null;
            return _keyPlugins != null && i < _keyPlugins.Length ? _keyPlugins[i] : null;
        }

        private Color TierColorOf(Cat c, int i)
        {
            int tier = 0;
            if (c == Cat.Items && TableDatabase.Items != null && i >= 0 && i < TableDatabase.Items.Length
                && TableDatabase.Items[i] != null) tier = TableDatabase.Items[i].RarityTier;
            else if (c == Cat.Plants && TableDatabase.Seeds != null && i >= 0 && i < TableDatabase.Seeds.Length
                && TableDatabase.Seeds[i] != null) tier = TableDatabase.Seeds[i].RarityTier;
            else if (c == Cat.Plugins && TableDatabase.Plugins != null && i >= 0 && i < TableDatabase.Plugins.Length
                && TableDatabase.Plugins[i] != null) tier = (int)TableDatabase.Plugins[i].quality;
            return TierColors[Mathf.Clamp(tier, 0, TierColors.Length - 1)];
        }

        // ── 中上：详情（★★ v309 分区重做 ★★）─────────────────────────────
        //
        //   改前：图标 + 三行字 + 按钮 + **一个 38px 的单行 label**（`_dNums`，里面是
        //         `增益 MeleeDamage+2　代价 —　规则 reroll_discount　id=xxx` 这种东西）
        //         + 「说明」+ desc 视口 568×166。数值长了直接被裁，而且大半是内部字段名。
        //
        //   改后：三段，自上而下（分区偏移见 `Dt*` 常量）：
        //     [96 图标] 名称 20px ／ 档位 ／ 状态
        //                按钮行（装上 + / 卸下）+ 插件那行「将装到：…」
        //     ─────────── 分隔线 1（y = 104）
        //     数　值：**一条一行**，视口 568×102（6 行 × 17），超出走它自己的滚动条
        //     ─────────── 分隔线 2（y = 232）
        //     说　明：`_dDesc` 全文，wordWrap + 滚动，视口 568×106
        //
        //   ⚠️ desc 视口从 166 收到 106：372 高的卡片里，数值区拿走 6 行之后只能这么分。
        //      说明区本来就带滚动条（内容高按宽度量好存 `_descH`）⇒ **全文一个字都不裁**。
        private void DrawDetail(RunSession s, Rect area)
        {
            Plate(area);

            Color tier = TierColorOf(_cat, SelOf(_cat));
            DrawIcon(new Rect(area.x + DtIconX, area.y + DtIconY, DtIconS, DtIconS),
                     IconKeyOf(_cat, SelOf(_cat)), tier);

            GUI.Label(new Rect(area.x + DtTextX, area.y + DtNameY, DtTextW, DtNameH), _dName, _head);
            GUI.Label(new Rect(area.x + DtTextX, area.y + DtGradeY, DtTextW, 18f), _dGrade, _small);
            GUI.Label(new Rect(area.x + DtTextX, area.y + DtStateY, DtTextW, 18f), _dState, _bold);

            // 按钮行（放在数值/说明上方：不会被长文本顶出屏幕）
            float by = area.y + DtBtnY;
            bool prevEnabled = GUI.enabled;
            Color prevBg = GUI.backgroundColor;

            // ★★ v308：「装上 +」的可用性 = `s != null` **且** 插件这一栏三把武器还有位置 ★★
            //   `_canInstall` 由 `EnsureDetail` 按 `PreviewTargetSlot` 算好（版本号缓存）。
            //   三把全满时按钮灰化 —— 与下面那一行"三把武器插件位已满"的提示同一份判据，
            //   不可能出现"提示说满了、按钮还能点"或反过来的两套真相 ✓
            bool canApply = s != null && (_cat != Cat.Plugins || _canInstall);

            GUI.enabled = canApply;
            GUI.backgroundColor = new Color(0.44f, 0.62f, 0.46f);
            // ★ v306：植物那一栏的按钮叫「种下」（同一个 `BuyOrEquip`，只是文案贴合语义）
            if (GUI.Button(new Rect(area.x + DtTextX, by, 96f, DtBtnH),
                           _cat == Cat.Plants ? "种下" : "装上 +", _btnSmall)) BuyOrEquip(s);

            GUI.enabled = s != null;      // 卸下：与"装得上装不上"无关
            GUI.backgroundColor = new Color(0.66f, 0.48f, 0.44f);
            if (GUI.Button(new Rect(area.x + DtTextX + 104f, by, 80f, DtBtnH), "卸下", _btnSmall)) UnequipSelected(s);
            GUI.backgroundColor = prevBg;
            GUI.enabled = prevEnabled;

            // ★★ v308：**删掉**原来那 3 颗「锄头 / 水壶 / 肥料」按钮（3×92 像素）★★
            //   改成**一行文字**说清"这一件会装到哪把武器"，落点由 `PreviewTargetSlot` 自动挑
            //   （锄头 → 水壶 → 肥料的第一把适配且还有空位的）；三把全满时这行写"已满"、
            //   颜色切到 `_warn`，同时上面「装上 +」按钮已灰化 ⇒ 点不动也看得懂 ✓
            //   位置：与按钮同一行的右侧（x+318 起，右边界留 `PadX`），垂直中心 = 按钮中心 ✓
            if (_cat == Cat.Plugins)
            {
                GUI.Label(new Rect(area.x + 318f, area.y + DtBtnY + 4f, area.width - 318f - PadX, 20f),
                          _dTarget, _canInstall ? _small : _warn);
            }

            // ── 数　值 ──────────────────────────────────────────────────
            Fill(new Rect(area.x + PadX, area.y + DtDiv1Y, 568f, 1f), new Color(1f, 0.84f, 0.45f, 0.22f));
            GUI.Label(new Rect(area.x + PadX, area.y + DtNumLblY, 568f, 16f), "数　值", _small);

            var numView = new Rect(area.x + PadX, area.y + DtNumTopY, 568f, DtNumH);
            float numContentH = Mathf.Max(numView.height - 2f, _dNumCount * DtNumRowH + 6f);
            _scrollNums = GUI.BeginScrollView(numView, _scrollNums,
                new Rect(0f, 0f, numView.width - 20f, numContentH));
            for (int i = 0; i < _dNumCount; i++)
                GUI.Label(new Rect(2f, 2f + i * DtNumRowH, numView.width - 24f, DtNumRowH),
                          _dNum[i], _numRow);
            GUI.EndScrollView();

            // ── 说　明 ──────────────────────────────────────────────────
            Fill(new Rect(area.x + PadX, area.y + DtDiv2Y, 568f, 1f), new Color(1f, 0.84f, 0.45f, 0.22f));
            GUI.Label(new Rect(area.x + PadX, area.y + DtDescLblY, 568f, 16f), "说　明", _small);

            // 完整 desc：wordWrap + 可滚动（高度已按宽度量好，见 `EnsureDetail`）
            var view = new Rect(area.x + PadX, area.y + DtDescTopY, 568f, area.yMax - (area.y + DtDescTopY) - 10f);
            float contentH = Mathf.Max(view.height - 2f, _descH + 8f);
            _scrollDesc = GUI.BeginScrollView(view, _scrollDesc,
                new Rect(0f, 0f, view.width - 20f, contentH));
            GUI.Label(new Rect(2f, 2f, view.width - 24f, contentH), _dDesc, _wrap);
            GUI.EndScrollView();
        }

        private static void BuyOrEquip(RunSession s)
        {
            if (s == null) return;
            int sel = SelOf(_cat);
            if (_cat == Cat.Items && TableDatabase.Items != null && sel < TableDatabase.Items.Length)
                EquipItem(s, TableDatabase.Items[sel]);
            else if (_cat == Cat.Plants && TableDatabase.Seeds != null && sel < TableDatabase.Seeds.Length)
                EquipSeed(s, TableDatabase.Seeds[sel]);
            else if (_cat == Cat.Plugins && TableDatabase.Plugins != null && sel < TableDatabase.Plugins.Length)
                EquipPlugin(s, TableDatabase.Plugins[sel]);
            Touch();
        }

        private static void UnequipSelected(RunSession s)
        {
            if (s == null) return;
            int sel = SelOf(_cat);
            if (_cat == Cat.Items && TableDatabase.Items != null && sel < TableDatabase.Items.Length)
                UnequipItem(s, TableDatabase.Items[sel]);
            else if (_cat == Cat.Plants && TableDatabase.Seeds != null && sel < TableDatabase.Seeds.Length)
                UnequipSeed(s, TableDatabase.Seeds[sel]);
            else if (_cat == Cat.Plugins && TableDatabase.Plugins != null && sel < TableDatabase.Plugins.Length)
                UnequipPlugin(s, TableDatabase.Plugins[sel]);
            Touch();
        }

        // ── 中下：道具背包（已拥有 + 每行卸下）────────────────────────────

        private void DrawBag(RunSession s, Rect area)
        {
            Plate(area);
            // ★ v308：标题 (10+22=32) 与副标题 (30+16=46) **改前重叠 2px** ⇒ 改成
            //   标题 8..32、副标题 34..52、视口 58 起（三段的行距统一为 2 / 6）
            GUI.Label(new Rect(area.x + PadX, area.y + 8f, 420f, 24f), "道具背包（已拥有）", _head);
            GUI.Label(new Rect(area.x + PadX, area.y + 34f, 460f, 18f),
                "点「卸下」把这一件从本局移除（属性 / 机制 / 上限同步回退）", _tiny);

            var view = new Rect(area.x + PadX, area.y + 58f, area.width - PadX * 2f, area.height - 70f);
            float contentW = view.width - 20f;
            int n = Mathf.Max(0, _bagItemCount);
            float contentH = Mathf.Max(view.height - 2f, n * RowHBag + 4f);
            _scrollBag = GUI.BeginScrollView(view, _scrollBag, new Rect(0f, 0f, contentW, contentH));

            if (n == 0)
                GUI.Label(new Rect(4f, 6f, contentW - 8f, 22f), "（背包是空的 —— 去左栏选中一件，再按详情栏「装上 +」）", _small);

            for (int i = 0; i < n; i++)
            {
                int idx = _bagItemIdx[i];
                float ry = 2f + i * RowHBag;
                var rowR = new Rect(2f, ry, contentW - 4f, RowHBag - 4f);
                Fill(rowR, new Color(0.13f, 0.13f, 0.14f, 0.85f));

                DrawIcon(new Rect(rowR.x + 4f, rowR.y + 5f, 30f, 30f), IconKeyOf(Cat.Items, idx),
                         TierColorOf(Cat.Items, idx));
                ItemDef d = TableDatabase.Items[idx];
                GUI.Label(new Rect(rowR.x + 40f, rowR.y + 3f, 320f, 18f), d != null ? d.Name : "", _rowLabel);
                GUI.Label(new Rect(rowR.x + 40f, rowR.y + 21f, 320f, 15f), _bagItemLabel[i], _tiny);

                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.66f, 0.48f, 0.44f);
                if (GUI.Button(new Rect(rowR.x + contentW - 100f, rowR.y + 5f, 88f, 26f), "卸下", _btnSmall))
                {
                    if (d != null && s != null) { UnequipItem(s, d); Touch(); }
                }
                GUI.backgroundColor = prevBg;
            }
            GUI.EndScrollView();
        }

        // ── 右上：武器与插件（3 把 × 6 格，点格子卸下）─────────────────────

        private void DrawWeapons(RunSession s, Rect area)
        {
            Plate(area);
            // ★ v308：标题 / 副标题行距同其它区块（重叠 2px → 分两行）；三把武器块整体下移 8px，
            //   块高 100 → 96、格高 34 → 32、行距 38 → 36 ⇒ 最下面那块底边 362，仍在上限 372 之内 ✓
            //   （改前 50 + 108×2 + 100 = 366，本来就比 372 只差 6px，给不出行距）
            GUI.Label(new Rect(area.x + PadX, area.y + 8f, 274f, 24f), "武器与插件", _head);
            GUI.Label(new Rect(area.x + PadX, area.y + 34f, 274f, 18f),
                "每把武器 6 格 · 点插件格 = 卸下", _tiny);

            float by = area.y + 58f;
            for (int w = 0; w < WeaponOrder.Length; w++)
            {
                WeaponSlot slot = WeaponOrder[w];
                var blockR = new Rect(area.x + PadX, by, area.width - PadX * 2f, 96f);
                Fill(blockR, new Color(0.11f, 0.11f, 0.12f, 0.9f));
                GUI.Label(new Rect(blockR.x + 6f, blockR.y + 3f, 160f, 18f),
                    RunSession.WeaponSlotName(slot), _bold);
                GUI.Label(new Rect(blockR.x + blockR.width - 74f, blockR.y + 3f, 68f, 18f),
                    _weapLabel[w], _mark);

                for (int c = 0; c < 6; c++)
                {
                    int col = c % 3, row = c / 3;
                    var cell = new Rect(blockR.x + col * 93f, blockR.y + 22f + row * 36f, 88f, 32f);
                    DrawPluginCell(s, slot, c, cell);
                }
                by += 104f;
            }
        }

        /// <summary>
        /// 一格插件位。<paramref name="cellIndex"/> = 该武器上第几条实例（按 `Goods.Plugins` 的安装顺序）。
        /// 有插件 ⇒ 图标 + 名称 + 「卸下」，**点整格 = 卸下**（用户要的"可以卸下插件"）。
        /// </summary>
        private void DrawPluginCell(RunSession s, WeaponSlot slot, int cellIndex, Rect cell)
        {
            PluginInstance pi = null;
            if (s != null && s.Goods != null)
            {
                int seen = 0;
                for (int i = 0; i < s.Goods.Plugins.Count; i++)
                {
                    PluginInstance p = s.Goods.Plugins[i];
                    if (p == null || p.Slot != slot) continue;
                    if (seen == cellIndex) { pi = p; break; }
                    seen++;
                }
            }

            if (pi == null || pi.Asset == null)
            {
                Fill(cell, new Color(0.08f, 0.08f, 0.09f, 0.9f));
                Frame(cell, new Color(1f, 1f, 1f, 0.06f));
                return;
            }

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.52f, 0.36f, 0.34f);
            bool clicked = GUI.Button(cell, GUIContent.none);
            GUI.backgroundColor = prevBg;

            Color tier = TierColors[Mathf.Clamp((int)pi.Asset.quality, 0, TierColors.Length - 1)];
            DrawIcon(new Rect(cell.x + 3f, cell.y + 7f, 20f, 20f), PluginIconKey(pi.Asset.id), tier);
            GUI.Label(new Rect(cell.x + 26f, cell.y + 2f, 58f, 15f), pi.Asset.displayName, _tiny);
            GUI.Label(new Rect(cell.x + 26f, cell.y + 17f, 58f, 15f),
                pi.Stacks > 1 ? "×" + pi.Stacks + " 卸下" : "卸下", _tiny);

            if (clicked)
            {
                UnequipPluginSlot(s, pi);
                Touch();
            }
        }

        // ── 右下：植物栏（★ v306：耕地实况 + 按列卸下）─────────────────────

        private void DrawPlants(RunSession s, Rect area)
        {
            Plate(area);
            // ★ v308：与 `DrawBag` 同一套行距（标题 8..32 / 副标题 34..52 / 视口 58 起）
            GUI.Label(new Rect(area.x + PadX, area.y + 8f, 274f, 24f), "植物栏（耕地实况）", _head);
            GUI.Label(new Rect(area.x + PadX, area.y + 34f, 274f, 18f),
                "每帧从耕地反查 · 卸下 = 铲掉那一列", _tiny);

            var view = new Rect(area.x + PadX, area.y + 58f, area.width - PadX * 2f, area.height - 70f);
            float contentW = view.width - 20f;
            int n = Mathf.Max(0, _plantCount);
            float contentH = Mathf.Max(view.height - 2f, n * RowHBag + 4f);
            _scrollPlants = GUI.BeginScrollView(view, _scrollPlants, new Rect(0f, 0f, contentW, contentH));

            if (n == 0)
                GUI.Label(new Rect(4f, 6f, contentW - 8f, 22f),
                    "（耕地是空的 —— 左栏「植物」里选中一株，再按详情栏「种下」）", _small);

            FarmGrid farm = s != null && s.World != null ? s.World.Farm : null;
            for (int i = 0; i < n; i++)
            {
                int col = _plantSlots[i];
                FarmPlot head = farm != null ? farm.ColumnHead(col) : null;
                SeedDef d = head != null ? head.CropDef : null;

                float ry = 2f + i * RowHBag;
                var rowR = new Rect(2f, ry, contentW - 4f, RowHBag - 4f);
                Fill(rowR, new Color(0.13f, 0.13f, 0.14f, 0.85f));

                int tier = d != null ? d.RarityTier : 0;
                DrawIcon(new Rect(rowR.x + 4f, rowR.y + 5f, 30f, 30f),
                         d != null ? SeedIconKey(d.Id) : null,
                         TierColors[Mathf.Clamp(tier, 0, TierColors.Length - 1)]);
                GUI.Label(new Rect(rowR.x + 40f, rowR.y + 3f, 128f, 18f),
                    (d != null ? d.Name : "") + " · 第" + (col + 1) + "列", _rowLabel);
                GUI.Label(new Rect(rowR.x + 40f, rowR.y + 21f, 130f, 15f), _plantLabel[i], _tiny);

                // 生长进度条：直接读地块的 `GrowthProgress01`，**逐帧**、零分配
                if (head != null)
                {
                    float pr = Mathf.Clamp01(head.GrowthProgress01);
                    Fill(new Rect(rowR.x + 40f, rowR.yMax - 7f, 120f, 4f),
                         new Color(0.20f, 0.20f, 0.22f, 0.9f));
                    Fill(new Rect(rowR.x + 40f, rowR.yMax - 7f, 120f * pr, 4f),
                         new Color(0.52f, 0.86f, 0.52f, 0.95f));
                }

                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.66f, 0.48f, 0.44f);
                if (GUI.Button(new Rect(rowR.x + contentW - 84f, rowR.y + 5f, 72f, 26f), "卸下", _btnSmall))
                {
                    if (s != null) UnequipPlantColumn(s, col);
                }
                GUI.backgroundColor = prevBg;
            }
            GUI.EndScrollView();
        }

        // ══════════════════════════════════════════════════════════════════
        // 画图小工具（都不分配：`White()` 是静态缓存的 1×1 纹理）
        // ══════════════════════════════════════════════════════════════════

        private static void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White());
            GUI.color = prev;
        }

        private static void Frame(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }

        /// <summary>一块"卡片"底板：深色填充 + 暖色细边 + 左上角一道高光。</summary>
        private static void Plate(Rect r)
        {
            Fill(r, new Color(0.10f, 0.10f, 0.11f, 0.96f));
            Frame(r, new Color(1f, 0.84f, 0.45f, 0.16f));
            Fill(new Rect(r.x + 1f, r.y + 1f, Mathf.Min(96f, r.width - 2f), 2f),
                 new Color(1f, 0.82f, 0.42f, 0.5f));
        }

        /// <summary>
        /// 图标格：品阶色打底 → 图标本体（`ScaleToFit`，不拉伸）→ 品阶色描边。
        /// 取不到图（`tex == null`）时就是一块品阶色块 —— 界面不会因此变空。
        /// </summary>
        private static void DrawIcon(Rect r, string iconKey, Color tier)
        {
            Texture2D tex = IconOf(iconKey);
            Color prev = GUI.color;

            GUI.color = new Color(tier.r * 0.20f, tier.g * 0.20f, tier.b * 0.20f, 1f);
            GUI.DrawTexture(r, White());
            if (tex != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            }
            GUI.color = new Color(tier.r, tier.g, tier.b, 0.85f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), White());
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), White());
            GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), White());
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), White());
            GUI.color = prev;
        }

        // ══════════════════════════════════════════════════════════════════
        // 样式（第一次画时建一次；只在 OnGUI 里建 —— GUI.skin 只在那里有效）
        // ══════════════════════════════════════════════════════════════════

        private void BuildStyles()
        {
            _stylesReady = true;

            if (_font == null)
            {
                string[] cands = null;
                try { cands = L10n.Fonts; } catch { cands = null; }
                if (cands == null || cands.Length == 0) cands = new[] { "Microsoft YaHei", "SimHei", "Arial" };
                for (int i = 0; i < cands.Length; i++)
                {
                    Font f = Font.CreateDynamicFontFromOSFont(cands[i], 18);
                    if (f != null) { _font = f; break; }
                }
            }

            _title = new GUIStyle(GUI.skin.label)
            { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _title.normal.textColor = new Color(1f, 0.92f, 0.70f);

            _head = new GUIStyle(GUI.skin.label)
            { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _head.normal.textColor = new Color(0.98f, 0.94f, 0.84f);

            _bold = new GUIStyle(GUI.skin.label)
            { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _bold.normal.textColor = new Color(0.92f, 0.88f, 0.78f);

            _small = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            _small.normal.textColor = new Color(0.80f, 0.78f, 0.72f);

            _tiny = new GUIStyle(GUI.skin.label)
            { fontSize = 11, alignment = TextAnchor.MiddleLeft };
            _tiny.normal.textColor = new Color(0.68f, 0.66f, 0.62f);
            _tiny.clipping = TextClipping.Clip;

            _rowLabel = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            _rowLabel.normal.textColor = new Color(0.94f, 0.92f, 0.86f);
            _rowLabel.clipping = TextClipping.Clip;

            _mark = new GUIStyle(GUI.skin.label)
            { fontSize = 11, alignment = TextAnchor.MiddleRight };
            _mark.normal.textColor = new Color(0.62f, 0.92f, 0.66f);

            _btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            _btnSmall = new GUIStyle(GUI.skin.button) { fontSize = 12 };

            _wrap = new GUIStyle(GUI.skin.label)
            { fontSize = 13, wordWrap = true };
            _wrap.normal.textColor = new Color(0.86f, 0.85f, 0.80f);

            // ── ★ v308 新增四个（只动"对齐 / 颜色"，字号沿用上面已有的档位）──────────────
            //   `_num`  顶栏金币：与 `_bold` 同字号，但**右对齐 + 金色**（改前用 `_bold` 左对齐，
            //           金币位数一变整行就往右长，`960,15` 那个 rect 会被撑出面板边）
            _num = new GUIStyle(GUI.skin.label)
            { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _num.normal.textColor = new Color(1f, 0.86f, 0.55f);

            //   `_tinyR` 顶栏"已装计数"：与 `_tiny` 完全同色同字号，只是**右对齐**
            //           （它归在面板右半边、与金币上下同基线，左对齐会让两块文字互相顶）
            _tinyR = new GUIStyle(GUI.skin.label)
            { fontSize = 11, alignment = TextAnchor.MiddleRight };
            _tinyR.normal.textColor = new Color(0.68f, 0.66f, 0.62f);
            _tinyR.clipping = TextClipping.Clip;

            //   `_bar` 底部状态条正文：13px、比 `_tiny` 亮一档（0.68 → 0.90）——
            //         它现在铺在深色底条上，不再"悬空淡字"，读数一眼能看清
            _bar = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            _bar.normal.textColor = new Color(0.90f, 0.88f, 0.82f);
            _bar.clipping = TextClipping.Clip;

            //   `_warn` 详情栏"三把武器插件位已满"：警示橙，只在这一种状态下用
            _warn = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            _warn.normal.textColor = new Color(1f, 0.64f, 0.40f);
            _warn.clipping = TextClipping.Clip;

            //   `_numRow` ★ v309 详情栏数值区的"一条一行"：13px、垂直居中、**切开溢出**
            //          （自己在滚动视口里，溢出会画到视口外面去；`Clip` 把它夹在行内）
            _numRow = new GUIStyle(GUI.skin.label)
            { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            _numRow.normal.textColor = new Color(0.86f, 0.85f, 0.80f);
            _numRow.clipping = TextClipping.Clip;

            //   `_groupHdr` ★ v310 左栏分组标题行（「锄　头 · 10 件」）：
            //          12px 加粗、暖金（与页签/分隔线同色系但更亮一档），**不换行**（Clip）
            _groupHdr = new GUIStyle(GUI.skin.label)
            { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _groupHdr.normal.textColor = new Color(1f, 0.86f, 0.55f);
            _groupHdr.clipping = TextClipping.Clip;

            if (_font != null)
            {
                _title.font = _font;
                _head.font = _font;
                _bold.font = _font;
                _small.font = _font;
                _tiny.font = _font;
                _rowLabel.font = _font;
                _mark.font = _font;
                _btn.font = _font;
                _btnSmall.font = _font;
                _wrap.font = _font;
                // ★ v308：新样式必须**一起换字体**，否则中文会落回 GUI.skin.label 的默认
                //   字体（Arial 没有中文字形）⇒ 顶栏金币 / 底栏状态条变成一串方块 ✗
                _num.font = _font;
                _tinyR.font = _font;
                _bar.font = _font;
                _warn.font = _font;
                _numRow.font = _font;   // v309
                _groupHdr.font = _font; // v310
            }
        }
    }
}