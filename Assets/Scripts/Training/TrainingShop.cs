using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Seedfall
{
    /// <summary>
    /// ★★ 训练场仓库 = **售卖所有物品的商店车** + **插件替换** ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、用户原话与本文件的职责
    /// ══════════════════════════════════════════════════════════════════════
    ///   「仓库相当于售卖所有物品的商店车，然后额外增加可以替换插件」
    ///
    ///   拆成两件事，**都在本文件里**，战斗代码只让了一个默认不生效的守卫：
    ///     ① 商店候选池**不过解锁门** ⇒ `seeds.json`(51) / `items.json`(151) / `plugins.json`(28) 全部可买
    ///        —— 唯一改动：`RunSession.IsInPool` 里的一行
    ///           `if (TrainingShop.SellEverything) return true;`（默认 false ⇒ 战斗行为逐位不变）
    ///     ② 金币**无限**：`TopUpGold` 在开关打开时每帧把余额补回 <see cref="InfiniteGold"/>
    ///        （购买扣钱发生在 `ShopSystem.TryBuy` 末尾，下一帧就补回来 ⇒ 玩家看到的是"买不完"）
    ///     ③ 插件**替换**：本工程原本**没有**卸载入口（`RunSession.cs:2602` 的注释写着
    ///        "grep `Uninstall` = 0 处"）⇒ 见下面第四节，用 `PlayerGoods.Plugins` 这个**公开列表**
    ///        做纯数据移除 —— 不碰 `WeaponSystem` 一行（`Aggregate` 每次现算，删掉立即失效 ✓）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、界面为什么**一行 UI 都没写**
    /// ══════════════════════════════════════════════════════════════════════
    ///   用户要"各种 UI 与菜单一样"⇒ 本文件**不画任何面板**，只做两件事：
    ///     · 打开商店阶段（`ForceShopForDebug`）⇒ HUD 走**战斗同一个** `ImGuiHud.DrawShop`
    ///       （货架 / 图标 / 品质 / 价格 / 按钮 / 滚动条全是既有那一套）
    ///     · 其余反馈走**既有提示通道** `RunSession.SetPrompt`
    ///   ⇒ `ImGuiHud` 的布局尺寸一个字节没动（连那行标题都没改），也就不可能出现"训练场版商店"这种第二套 UI ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、入口键（只在战斗 HUD 那套输入之外另开的三个键，互不冲突）
    /// ══════════════════════════════════════════════════════════════════════
    ///   **B**       打开仓库商店（并重掷一次货架）；已在商店里再按 = 免费刷新
    ///   **P**       开/关「替换插件」光标（关着时 [ ] 与 Enter 无副作用）
    ///   **[** / **]** 在"已装插件"之间移动光标
    ///   **Enter**   把光标那一件**卸下**（回到背包待装，不是丢弃）
    ///   —— 卸下之后**直接去货架买另一件**：`TryBuy` 的插件分支本来就会 `AutoInstallPending`
    ///      装到空出来的槽里（`CanTakePlugin` 因为槽位腾出来了 ⇒ 按钮不再灰）⇒
    ///      "选武器 → 选要卸的 → 选要装的"这三步全落在**既有购买链**上，一行装配逻辑都不用新写 ✓
    ///   ⚠️ 与既有键位的分工：E（商店车）/ Q（换种）/ R（刷新）/ 空格（继续）/ Tab / 1-6 / Esc 全部不占用 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、插件替换：现有 API 有什么、缺什么、本文件补了什么
    /// ══════════════════════════════════════════════════════════════════════
    ///   现有（可复用，**不改**）：
    ///     · `WeaponSystem.Install(goods, asset, slot, out reason)` —— 装配的唯一入口
    ///       （Fits / 互斥 / 叠加 / 6 槽上限全在里面）
    ///     · `WeaponSystem.Aggregate(goods, slot)` —— **每次现算**，没有缓存 ⇒ 列表一改立即生效
    ///     · `PlayerGoods.Plugins`（`public readonly List<PluginInstance>`）—— 数据层是公开的
    ///   缺：**没有任何"卸下"方法**（`Uninstall` / `RemovePlugin` / `Detach` 在武器与插件相关代码里 0 处）
    ///   本文件补的两个动作（纯数据，`WeaponSystem` 零改动）：
    ///     · `TryUninstall(s, inst)`  —— 从 `Goods.Plugins` 移除该实例，并放回背包（`Slot == None`）
    ///     · `TryReplace(s, slot, remove, add)` —— 卸下 + 立即装新件；新件装不上 ⇒ **回滚**（原件装回去）
    ///   ⚠️ 为什么这不算"擅自大改 `WeaponSystem`"：一行都没进去。
    ///      移除实例是**持有关系**的变化，不是装配规则的变化 ⇒ 规则仍然只有 `Install` 那一份 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 五、开关是**默认关闭**的（这是本文件最重要的一个数）
    /// ══════════════════════════════════════════════════════════════════════
    ///   `SellEverything` 的初值是 `false`，且**只有**下面三条路能把它打开：
    ///     ① 玩家在 HUD 上按 B（本人明确要的入口键）
    ///     ② 代码调 `TrainingShop.Enable(…)`（训练场宿主接线用）
    ///     ③ 调试/自动化调同一个方法
    ///   ⇒ 不按 B、没人调用的那局战斗：`IsInPool` 走 `TownUnlocks.IsUnlocked`（原样），
    ///     `TopUpGold` 不执行，本文件的所有热键分支都不进入 ✓
    /// </summary>
    public static class TrainingShop
    {
        private const string Tag = "[仓库] ";

        /// <summary>
        /// ★★ 唯一的战斗侧守卫开关 ★★ —— **默认 false**。
        ///
        /// 只有它被打开时，`RunSession.IsInPool` 才直接 `return true`（候选池不过解锁门），
        /// 金币才会被补满，HB 上的热键才会生效。
        /// 默认 false ⇒ 未按 B / 未经 `Enable` 的一局与改前**逐位相同** ✓
        /// </summary>
        public static bool SellEverything;

        /// <summary>金币被补到的数（"无限金币"的落地方式：不足就补到这个数）。</summary>
        public const int InfiniteGold = 999999;

        /// <summary>是否处于「替换插件」光标模式（仅 UI 状态，不进存档）。</summary>
        public static bool ReplaceMode => _replaceMode;

        /// <summary>光标所在的那一件（没有可换的东西时为 null）。</summary>
        public static PluginInstance CursorPlugin { get; private set; }

        private static bool _replaceMode;
        private static int _cursor;
        /// <summary>复用容器：每帧重建一次"已装插件"清单，避免热路径反复 new。</summary>
        private static readonly List<PluginInstance> _installed = new List<PluginInstance>(24);

        // ══════════════════════════════════════════════════════════════════
        // 开关
        // ══════════════════════════════════════════════════════════════════

        /// <summary>打开仓库（训练场宿主或调试口调它）。</summary>
        public static void Enable(RunSession s = null)
        {
            if (SellEverything) return;
            SellEverything = true;
            TopUpGold(s);
            Log.Info(Tag + "已开启：商店候选池**不过解锁门**（全部商品可买）+ 金币无限。"
                         + "退出训练场请调 TrainingShop.Disable()。");
            s?.SetPrompt("仓库已开启：全部商品可买 · 金币无限", 2.5f);
        }

        /// <summary>
        /// 关闭仓库 —— 把 `SellEverything` 变回 false ⇒ `IsInPool` 立刻恢复"只看主城解锁表"。
        /// ⚠️ 已经买到手的东西**不会**被收回（那是本局数据，不是开关状态）✓
        /// </summary>
        public static void Disable(RunSession s = null)
        {
            if (!SellEverything) return;
            SellEverything = false;
            _replaceMode = false;
            _cursor = 0;
            CursorPlugin = null;
            Log.Info(Tag + "已关闭：候选池恢复按主城解锁表过滤（与战斗原样一致）。");
            s?.SetPrompt("仓库已关闭：恢复正常的解锁过滤", 2.5f);
        }

        // ══════════════════════════════════════════════════════════════════
        // 每帧驱动（v303 起**没有调用点** —— 见下）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 每帧做三件事：补金币、读训练场专用热键、维护"替换插件"光标。
        /// 开关关着时**第一行就返回** ⇒ 对正常战斗零成本、零副作用 ✓
        ///
        /// ⚠️ v303：它的**唯一调用者 `TrainingShopDriver` 已删除** ✗
        ///   原因是那一版"仓库"依赖商店车（`ForceShopForDebug` → `PhaseState.Shop`），
        ///   在战斗里按 B 打不开 —— 用户的原话是"训练场的仓库没有做"。
        ///   现在"打开仓库"归 `TrainingVault`（常驻 IMGUI 面板，与阶段机无关）。
        ///   本方法**留着不删**：它是"B/P[/]Enter 那一套热键"的唯一实现，
        ///   而 `TrainingVault` 用的是鼠标，两者语义不同 —— 将来若要恢复热键版仓库，
        ///   重新挂一个驱动器调它即可，不必重写。
        ///   （`SellEverything` 仍被 `RunSession.IsInPool` 与 `TrainingMode` 读写，
        ///     所以本类**不是**死代码，只是热键入口暂时无驱动器。）
        /// </summary>
        public static void Tick(RunSession s)
        {
            if (!SellEverything || s == null) return;
            TopUpGold(s);
            if (s.Paused) return;                      // 菜单开着不抢键（Esc 菜单里的键位归菜单）
            if (s.Phase == PhaseState.Defeat) return;  // 结算画面不抢键

            ReadKeys(s);
            SyncCursor(s);
        }

        /// <summary>
        /// **无限金币的唯一实现**：余额不足 <see cref="InfiniteGold"/> 就补到这个数。
        ///
        /// 为什么是"每帧补差额"而不是"一次性改成 int.MaxValue"：
        ///   ① 购买扣钱在 `ShopSystem.TryBuy` 的**末尾**（`s.Gold -= o.Price`）⇒
        ///      下一帧这里立刻补齐 ⇒ 玩家永远买得下，"买完就补满"与"每次打开补满"两条需求同时满足 ✓
        ///   ② 不用 `int.MaxValue` 是因为刷新价会一路涨到 `int.MaxValue` 附近
        ///      （`ShopSystem.RerollPrice` 的溢出保护），留出余量后所有价码都还付得起 ✓
        ///   ③ 走**公开 API** `AddGoldDirect`（= `Gold += Max(0, amount)`）⇒
        ///      不绕过任何既有记账 ✓
        /// </summary>
        public static void TopUpGold(RunSession s)
        {
            if (s == null) return;
            int lack = InfiniteGold - s.Gold;
            if (lack > 0) s.AddGoldDirect(lack);
        }

        /// <summary>
        /// **打开 / 重掷仓库**（B 键的落点）。
        ///
        /// 走的是既有的**唯一正式取货架路径** `ForceShopForDebug`
        /// （= `Shop.ResetForPhase()` → `Shop.Refresh(this)` → `SetPhase(Shop)`）⇒
        /// 三件事一次做完：刷新计数清零、按当前分钟重抽 4 个货位、进入商店阶段 ✓
        /// 不新写刷新逻辑 ⇒ 货架填充者仍然只有 `ShopSystem.Refresh` 一个 ✓
        /// </summary>
        public static bool OpenOrReroll(RunSession s)
        {
            if (s == null) return false;
            Enable(s);                     // B 本身就是"进仓库"的意思 ⇒ 顺手把开关打开（幂等）
            TopUpGold(s);
            s.ForceShopForDebug();
            _cursor = 0;
            SyncCursor(s);
            int n = s.Shop.Offers.Count;
            s.SetPrompt($"仓库：{n} 个货位已上新（全部商品可买 · 金币 {s.Gold}）", 2.5f);
            Log.Info(Tag + "货架已重掷｜" + StatusLine(s));
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // 插件替换（本工程原本没有"卸下"这个动作，这里是唯一实现）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>把一件**已装在武器上**的插件卸下来，放回背包（`Slot == None`，仍是玩家的）。</summary>
        public static bool TryUninstall(RunSession s, PluginInstance inst, out string reason)
        {
            reason = null;
            if (s == null || s.Goods == null) { reason = "插件栏不可用"; return false; }
            if (inst == null || inst.Asset == null) { reason = "没有选中插件"; return false; }
            if (inst.Slot == WeaponSlot.None) { reason = "这件插件还没装在任何武器上"; return false; }

            int idx = s.Goods.Plugins.IndexOf(inst);
            if (idx < 0) { reason = "背包里找不到这件插件"; return false; }

            WeaponSlot from = inst.Slot;
            // ★ 卸下 = 从持有列表移除一个实例 ★
            //   `WeaponSystem.Aggregate` 是**每次现算**的纯函数（没有缓存）⇒
            //   列表一变，这把武器的修正值当帧就回到"没有这件插件"的读数 ✓
            s.Goods.Plugins.RemoveAt(idx);

            // 训练场里"卸下"≠"丢弃"：原件回到背包待装，玩家可以再装回去
            if (s.Goods.Find(inst.Asset, WeaponSlot.None) == null)
                s.Goods.Plugins.Add(new PluginInstance(inst.Asset, WeaponSlot.None));

            int used = s.Goods.SlotsUsedOn(from);
            s.SetPrompt($"已卸下 {inst.Asset.displayName}（{RunSession.WeaponSlotName(from)}"
                        + $" {used}/{PlayerGoods.MaxPluginsPerWeapon}）→ 回到背包", 2.5f);
            Telemetry.Emit("training_plugin_uninstall", ("id", inst.Asset.id), ("slot", (int)from),
                ("used", used));
            return true;
        }

        /// <summary>
        /// **替换**：把 <paramref name="slot"/> 上的 <paramref name="remove"/> 换成 <paramref name="add"/>。
        ///
        ///   remove = null ⇒ 只装（等价于 `Install`）
        ///   add    = null ⇒ 只卸（等价于 `TryUninstall`）
        ///   两者都给   ⇒ 卸下 + 装新件；**新件装不上就回滚**（原件装回原位，绝不凭空少一件）
        ///
        /// 装新件仍然走 `WeaponSystem.Install`（唯一装配入口）⇒ 不复制任何装配规则 ✓
        /// </summary>
        public static bool TryReplace(RunSession s, WeaponSlot slot, PluginAsset remove, PluginAsset add,
                                     out string reason)
        {
            reason = null;
            if (s == null || s.Goods == null) { reason = "插件栏不可用"; return false; }
            if (slot == WeaponSlot.None) { reason = "先选一把武器"; return false; }

            if (remove == null)
            {
                if (add == null) { reason = "没有要换的东西"; return false; }
                if (!WeaponSystem.Install(s.Goods, add, slot, out reason)) return false;
                s.SetPrompt($"{add.displayName} → {RunSession.WeaponSlotName(slot)}"
                            + $"（{s.Goods.SlotsUsedOn(slot)}/{PlayerGoods.MaxPluginsPerWeapon}）", 2.5f);
                return true;
            }

            PluginInstance old = s.Goods.Find(remove, slot);
            if (old == null) { reason = $"这把武器上没有装 {remove.displayName}"; return false; }
            if (add != null && !add.Fits(slot))
            {
                reason = $"{add.displayName} 不适用 {RunSession.WeaponSlotName(slot)}";
                return false;
            }

            int idx = s.Goods.Plugins.IndexOf(old);
            if (idx < 0) { reason = "背包里找不到这件插件"; return false; }

            s.Goods.Plugins.RemoveAt(idx);

            if (add == null)
            {
                // 只卸下：按 TryUninstall 的同一规矩回背包
                if (s.Goods.Find(old.Asset, WeaponSlot.None) == null)
                    s.Goods.Plugins.Add(new PluginInstance(old.Asset, WeaponSlot.None));
                s.SetPrompt($"已卸下 {old.Asset.displayName} → 回到背包", 2.5f);
                return true;
            }

            if (WeaponSystem.Install(s.Goods, add, slot, out reason))
            {
                // 换下来的那件也回背包（玩家仍然拥有它）
                if (s.Goods.Find(old.Asset, WeaponSlot.None) == null)
                    s.Goods.Plugins.Add(new PluginInstance(old.Asset, WeaponSlot.None));
                s.SetPrompt($"{RunSession.WeaponSlotName(slot)}：{old.Asset.displayName} → {add.displayName}"
                            + $"（{s.Goods.SlotsUsedOn(slot)}/{PlayerGoods.MaxPluginsPerWeapon}）", 2.5f);
                Telemetry.Emit("training_plugin_replace", ("slot", (int)slot),
                    ("remove", old.Asset.id), ("add", add.id));
                return true;
            }

            // ── 回滚：新件装不上 ⇒ 原件装回原位 ──
            //   顺序只影响 `Plugins` 里的排布（HUD 按安装顺序列），插回原下标最不打扰 ✓
            s.Goods.Plugins.Insert(Mathf.Clamp(idx, 0, s.Goods.Plugins.Count), old);
            s.SetPrompt("换不上：" + reason, 2.5f);
            return false;
        }

        /// <summary>这把武器**现在还能装**的插件（只读候选，供调试面板/自动化用）。</summary>
        public static PluginAsset[] InstallableFor(WeaponSlot slot)
        {
            PluginAsset[] all = TableDatabase.Plugins;
            if (all == null || slot == WeaponSlot.None) return new PluginAsset[0];
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].Fits(slot)) n++;
            var pool = new PluginAsset[n];
            n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] != null && all[i].Fits(slot)) pool[n++] = all[i];
            return pool;
        }

        /// <summary>一行的状态读数（不画 UI，只给日志/提示用）。</summary>
        public static string StatusLine(RunSession s)
        {
            if (s == null) return "无会话";
            int installed = 0;
            for (int i = 0; i < s.Goods.Plugins.Count; i++)
                if (s.Goods.Plugins[i].Slot != WeaponSlot.None) installed++;
            return $"仓库{(SellEverything ? "开" : "关")}｜金币 {s.Gold}｜已装插件 {installed} 件"
                 + $"｜货位 {s.Shop.Offers.Count}｜阶段 {s.Phase}";
        }

        // ══════════════════════════════════════════════════════════════════
        // 输入（本文件独占的四个键：B / P / [ ] / Enter）
        // ══════════════════════════════════════════════════════════════════

        private static void ReadKeys(RunSession s)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // ── B：打开仓库（已在商店里 = 免费刷新货架）──
            if (kb.bKey.wasPressedThisFrame)
            {
                OpenOrReroll(s);
                return;                                  // 同一帧只处理一个键，避免连锁
            }

            // ── P：开关「替换插件」光标 ──
            if (kb.pKey.wasPressedThisFrame)
            {
                _replaceMode = !_replaceMode;
                _cursor = 0;
                SyncCursor(s);
                s.SetPrompt(_replaceMode
                    ? "替换插件：[ ] 选已装的 → Enter 卸下 → 去货架买新的装上"
                    : "替换插件：已关闭", 3f);
                return;
            }

            if (!_replaceMode) return;

            // ── [ / ]：在"已装插件"之间移动光标 ──
            int step = 0;
            if (kb.leftBracketKey.wasPressedThisFrame) step = -1;
            else if (kb.rightBracketKey.wasPressedThisFrame) step = +1;
            if (step != 0)
            {
                if (_installed.Count == 0) s.SetPrompt("这把武器/所有武器上都还没有插件", 2f);
                else
                {
                    _cursor = (_cursor + step + _installed.Count) % _installed.Count;
                    SyncCursor(s);
                    s.SetPrompt(CursorText(), 3f);
                }
                return;
            }

            // ── Enter：卸下光标那一件（回背包）──
            bool enter = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
            if (!enter) return;

            if (CursorPlugin == null) { s.SetPrompt("没有可卸下的插件（先按 P 进入替换）", 2f); return; }
            PluginInstance target = CursorPlugin;
            if (TryUninstall(s, target, out string reason))
            {
                _cursor = Mathf.Max(0, _cursor - 1);   // 清单短了一件 ⇒ 光标跟着退一格（再由 SyncCursor 夹住）
                SyncCursor(s);
                s.SetPrompt(CursorText(), 3f);
            }
            else Log.Warn(Tag + "卸下失败：" + reason);
#endif
        }

        /// <summary>重建"已装插件"清单并夹住光标（每次列表变化后调）。</summary>
        private static void SyncCursor(RunSession s)
        {
            _installed.Clear();
            CursorPlugin = null;
            if (s == null || s.Goods == null) return;
            for (int i = 0; i < s.Goods.Plugins.Count; i++)
            {
                PluginInstance pi = s.Goods.Plugins[i];
                if (pi != null && pi.Asset != null && pi.Slot != WeaponSlot.None) _installed.Add(pi);
            }
            if (_installed.Count == 0) { _cursor = 0; return; }
            _cursor = Mathf.Clamp(_cursor, 0, _installed.Count - 1);
            CursorPlugin = _installed[_cursor];
        }

        private static string CursorText()
        {
            if (CursorPlugin == null) return "替换插件：没有可卸下的东西";
            PluginInstance pi = CursorPlugin;
            string stacks = pi.Stacks > 1 ? " ×" + pi.Stacks : "";
            return $"替换插件 {_cursor + 1}/{_installed.Count}："
                 + $"{RunSession.WeaponSlotName(pi.Slot)}·{pi.Asset.displayName}{stacks}"
                 + "　［[ ] 换目标 · Enter 卸下］";
        }
    }

    }