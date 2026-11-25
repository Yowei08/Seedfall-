using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Seedfall
{
    /// <summary>按键上下文。**主城与战斗是两套独立绑定**（用户明确要求区分）。</summary>
    public enum InputContext
    {
        Town = 0,
        Combat = 1,
    }

    /// <summary>一个可改键的语义动作。</summary>
    public sealed class BindableAction
    {
        public InputContext Context;
        public string Id = "";
        public string Label = "";
        public string DefaultPath = "";
        public InputAction Action;

        /// <summary>当前**实际生效**的路径（有改键就是改键值，否则是默认值）。</summary>
        public string Path
        {
            get
            {
                if (Action == null || Action.bindings.Count == 0) return DefaultPath;
                return Action.bindings[0].effectivePath;
            }
        }

        public bool IsDefault => Path == DefaultPath;

        /// <summary>这个路径在 Input System 里能不能解析到真实控件（改键写错时会 false）。</summary>
        public bool Resolves => !string.IsNullOrEmpty(Path) && InputSystem.FindControl(Path) != null;
    }

    /// <summary>
    /// ★★ 按键绑定层：**用 Input System 的 `InputAction` / `InputActionMap` 承载所有按键**，两套上下文 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是"代码建 map"，而不是 `.inputactions` 资产
    /// ══════════════════════════════════════════════════════════════════════
    /// ① 资产文件是二进制序列化 + 资产 GUID，改它要动 `.meta`，与"只加 `Scripts/Login/**`"的范围冲突 ✗
    /// ② 改键要落进**存档 JSON**（`settings.json` 的 `bindings` 数组），代码建的 map 天然以"路径字符串"
    ///    为真相源，写出去 / 读回来就是同一份字符串 ✓
    /// ③ 默认值直接照抄工程**现状**（见第二节），不是拍脑袋定的 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、默认绑定 = 工程现状（grep 取证，不是猜的）
    /// ══════════════════════════════════════════════════════════════════════
    ///   · 主城 `TownBootstrap.ReadInput`：WASD 移动、E 交互、Esc 关面板
    ///   · 战斗 `GameBootstrap.ReadInput`：WASD 移动、E 商店、鼠标左键攻击、
    ///     数字 1~6 切武器（`ReadDigit`）、Esc 暂停、空格跳过、R 刷新/重开
    ///   ⇒ 本表把它们的语义抄下来，作为两套上下文的**默认键**：改键改的就是这张表。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、**本轮的边界**：这张表还没有"接管"主城/战斗的读取
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownBootstrap.ReadInput` 读的是 `Keyboard.current.wKey.isPressed`，`GameBootstrap` 同理 ——
    /// 那是**硬编码设备读取**，两个文件本轮都在禁改范围（`TownBootstrap.cs` 明令不许动）。
    /// ⇒ 所以这张表当前的真实作用是：**设置的真相源 + 可改键 + 可持久化 + 可被 `ProbeInput` 证明接到真设备**，
    ///   而"主城跟着我的改键走"需要下一次改那两个文件（接线点：把 `kb.wKey.isPressed`
    ///   换成 `LoginInput.Held(InputContext.Town, "move_up")`）。这件事写进报告的"没做到"一节 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、改键怎么验（`ProbeInput`，见 `LoginBootstrap`）
    /// ══════════════════════════════════════════════════════════════════════
    /// 用 `InputSystem.QueueStateEvent(Keyboard.current, KeyboardState)` 往设备里塞一帧"按键按下"，
    /// 再看 `action.IsPressed()` —— 三件事同时被证：
    ///   ① 路径确实解析到了真实控件（`Resolves`）② 动作确实跟着设备状态翻转
    ///   ③ 主城那份硬编码读取（`Keyboard.current.wKey.isPressed`）也在同一帧翻转 ⇒ 同一台设备、同一帧 ✓
    /// </summary>
    public static class LoginInput
    {
        private struct ActionDef
        {
            public InputContext Context;
            public string Id;
            public string Label;
            public string DefaultPath;

            public ActionDef(InputContext context, string id, string label, string defaultPath)
            {
                Context = context;
                Id = id;
                Label = label;
                DefaultPath = defaultPath;
            }
        }

        /// <summary>两套上下文的动作表（顺序 = 设置面板里的显示顺序）。</summary>
        private static readonly ActionDef[] Defs =
        {
            // ── 主城 ──
            new ActionDef(InputContext.Town, "move_up",     "向上移动",  "<Keyboard>/w"),
            new ActionDef(InputContext.Town, "move_down",   "向下移动",  "<Keyboard>/s"),
            new ActionDef(InputContext.Town, "move_left",   "向左移动",  "<Keyboard>/a"),
            new ActionDef(InputContext.Town, "move_right",  "向右移动",  "<Keyboard>/d"),
            new ActionDef(InputContext.Town, "interact",    "交互 / 种植", "<Keyboard>/e"),
            // 主城**没有战斗**（`TownBootstrap.ReadInput` 里根本没有攻击键的读取）⇒ 这条只是占位，
            // 让"主城/战斗两套上下文"结构完整、玩家可以把这一格改掉而不影响任何东西 ✓
            new ActionDef(InputContext.Town, "attack",      "攻击（主城无战斗 · 占位）", "<Mouse>/leftButton"),
            new ActionDef(InputContext.Town, "switch_weapon", "切换武器", "<Keyboard>/q"),
            new ActionDef(InputContext.Town, "settings",    "设置（Esc）", "<Keyboard>/escape"),

            // ── 战斗 ──
            new ActionDef(InputContext.Combat, "move_up",     "向上移动",  "<Keyboard>/w"),
            new ActionDef(InputContext.Combat, "move_down",   "向下移动",  "<Keyboard>/s"),
            new ActionDef(InputContext.Combat, "move_left",   "向左移动",  "<Keyboard>/a"),
            new ActionDef(InputContext.Combat, "move_right",  "向右移动",  "<Keyboard>/d"),
            new ActionDef(InputContext.Combat, "interact",    "交互 / 商店", "<Keyboard>/e"),
            new ActionDef(InputContext.Combat, "attack",      "攻击",      "<Mouse>/leftButton"),
            new ActionDef(InputContext.Combat, "switch_weapon", "切换武器", "<Keyboard>/2"),
            new ActionDef(InputContext.Combat, "settings",    "菜单 / 暂停（Esc）", "<Keyboard>/escape"),
            // ★ 用户要的"战斗中刷新 / 继续 这些键也要进设置" ★
            // 默认值照抄 `GameBootstrap.ReadInput` 的真实读取（grep 取证，不是猜的）：
            //   R = 商店刷新 / 卡牌重抽 / （配 Shift）重开一局；空格 = 跳过 / 选卡确认；Tab = 开关键位说明
            new ActionDef(InputContext.Combat, "refresh",     "商店刷新 / 重抽（R）", "<Keyboard>/r"),
            new ActionDef(InputContext.Combat, "skip",        "跳过 / 继续 / 确认（空格）", "<Keyboard>/space"),
            new ActionDef(InputContext.Combat, "restart",     "重开一局（Shift + R）", "<Keyboard>/leftShift"),
            new ActionDef(InputContext.Combat, "keyhelp",     "按键说明开关（Tab）", "<Keyboard>/tab"),
        };

        private static readonly List<BindableAction> Actions = new List<BindableAction>(Defs.Length);
        private static InputActionMap _townMap;
        private static InputActionMap _combatMap;
        private static bool _built;

        private static BindableAction _rebind;
        private static bool _waitRelease;

        /// <summary>正在等玩家按键的那个动作（面板上显示"请按键…"用）。没有则为 null。</summary>
        public static BindableAction Rebinding => _rebind;

        /// <summary>最近一次改键的日志（面板 / 读数用）。</summary>
        public static string LastRebindLog { get; private set; } = "（还没有改过键）";

        public static bool Built => _built;

        // ══════════════════════════════════════════════════════════════════
        // 建表
        // ══════════════════════════════════════════════════════════════════

        /// <summary>建两张 map 并套上设置里的改键值。**可重复调用**（内部先销毁旧的）。</summary>
        public static void Build(GameSettings settings)
        {
            Dispose();
            _townMap = new InputActionMap("Town");
            _combatMap = new InputActionMap("Combat");

            for (int i = 0; i < Defs.Length; i++)
            {
                ActionDef d = Defs[i];
                InputActionMap map = d.Context == InputContext.Town ? _townMap : _combatMap;
                InputAction action = map.AddAction(d.Id, InputActionType.Button, d.DefaultPath);
                Actions.Add(new BindableAction
                {
                    Context = d.Context,
                    Id = d.Id,
                    Label = d.Label,
                    DefaultPath = d.DefaultPath,
                    Action = action,
                });
            }

            _townMap.Enable();
            _combatMap.Enable();
            _built = true;

            ApplyOverrides(settings);
        }

        /// <summary>把设置里存的改键值套上去（不合法 / 解析不到的路径跳过并记日志，不炸）。</summary>
        private static void ApplyOverrides(GameSettings settings)
        {
            if (settings == null || settings.bindings == null) return;
            for (int i = 0; i < settings.bindings.Length; i++)
            {
                BindingEntry e = settings.bindings[i];
                if (e == null || string.IsNullOrEmpty(e.action) || string.IsNullOrEmpty(e.path)) continue;
                InputContext ctx = e.context == "combat" ? InputContext.Combat : InputContext.Town;
                BindableAction a = Find(ctx, e.action);
                if (a == null || a.Action == null) continue;
                if (InputSystem.FindControl(e.path) == null)
                {
                    Log.Warn(LoginSettings.Tag + $"设置里的绑定 {e.context}/{e.action}={e.path} 解析不到控件，已跳过");
                    continue;
                }
                a.Action.ApplyBindingOverride(0, e.path);
            }
        }

        /// <summary>销毁两张 map（域重载 / 退出 play 前调一次）。</summary>
        public static void Dispose()
        {
            _rebind = null;
            _waitRelease = false;
            Actions.Clear();
            if (_townMap != null) { _townMap.Disable(); _townMap.Dispose(); _townMap = null; }
            if (_combatMap != null) { _combatMap.Disable(); _combatMap.Dispose(); _combatMap = null; }
            _built = false;
        }

        // ══════════════════════════════════════════════════════════════════
        // 查询
        // ══════════════════════════════════════════════════════════════════

        public static BindableAction Find(InputContext ctx, string id)
        {
            for (int i = 0; i < Actions.Count; i++)
                if (Actions[i].Context == ctx && Actions[i].Id == id) return Actions[i];
            return null;
        }

        /// <summary>按序号取动作（面板逐行画用；序号 = <see cref="Defs"/> 顺序）。</summary>
        public static BindableAction At(int index)
            => index >= 0 && index < Actions.Count ? Actions[index] : null;

        public static int Count => Actions.Count;

        /// <summary>按下这一帧（`WasPressedThisFrame`）。</summary>
        public static bool Pressed(InputContext ctx, string id)
        {
            BindableAction a = Find(ctx, id);
            return a != null && a.Action != null && a.Action.WasPressedThisFrame();
        }

        /// <summary>按住（`IsPressed`）。</summary>
        public static bool Held(InputContext ctx, string id)
        {
            BindableAction a = Find(ctx, id);
            return a != null && a.Action != null && a.Action.IsPressed();
        }

        // ══════════════════════════════════════════════════════════════════
        // 改键
        // ══════════════════════════════════════════════════════════════════

        /// <summary>进入"请按键"状态。先等当前按着的东西（包括点按钮那次左键）松开，再收下一个键。</summary>
        public static void BeginRebind(InputContext ctx, string id)
        {
            BindableAction a = Find(ctx, id);
            if (a == null) { Log.Warn(LoginSettings.Tag + $"没有这个动作：{ctx}/{id}"); return; }
            _rebind = a;
            _waitRelease = true;
            LastRebindLog = $"等待按键：{ContextName(ctx)}/{a.Label}（按 Esc 取消）";
            Log.Info(LoginSettings.Tag + LastRebindLog);
        }

        public static void CancelRebind()
        {
            if (_rebind == null) return;
            LastRebindLog = "已取消改键";
            _rebind = null;
            _waitRelease = false;
            Log.Info(LoginSettings.Tag + LastRebindLog);
        }

        /// <summary>每帧调一次（由 `LoginBootstrap.Update` 驱动）。返回 true = 这一帧收完了一个键。</summary>
        public static bool Tick()
        {
            if (_rebind == null) return false;

            if (_waitRelease)
            {
                if (AnyHeld()) return false;
                _waitRelease = false;
                return false;
            }

            string path = DetectPressedPath();
            if (path == null) return false;
            if (path.Length == 0) { CancelRebind(); return false; }     // 空串 = 按了 Esc

            BindableAction target = _rebind;
            SetPath(target.Context, target.Id, path);
            _rebind = null;
            return true;
        }

        /// <summary>直接把某个动作改成指定路径（面板点选 / 自动化测试都走这条）。</summary>
        public static bool SetPath(InputContext ctx, string id, string path)
        {
            BindableAction a = Find(ctx, id);
            if (a == null || a.Action == null) return false;
            if (string.IsNullOrEmpty(path)) return false;
            if (InputSystem.FindControl(path) == null)
            {
                LastRebindLog = $"❌ {path} 解析不到控件，未改键";
                Log.Warn(LoginSettings.Tag + LastRebindLog);
                return false;
            }
            a.Action.ApplyBindingOverride(0, path);
            CaptureInto(LoginSettings.Current);
            LoginSettings.Save();
            LastRebindLog = $"{ContextName(ctx)}/{a.Label} → {path}";
            Log.Info(LoginSettings.Tag + "改键：" + LastRebindLog);
            return true;
        }

        /// <summary>全部恢复默认（两套上下文一起重置，并落盘）。</summary>
        public static void ResetAll()
        {
            for (int i = 0; i < Actions.Count; i++)
                if (Actions[i].Action != null) Actions[i].Action.RemoveAllBindingOverrides();
            LoginSettings.Current.bindings = Array.Empty<BindingEntry>();
            LoginSettings.Save();
            LastRebindLog = "两套上下文已全部恢复默认键";
            Log.Info(LoginSettings.Tag + LastRebindLog);
        }

        /// <summary>把当前生效的绑定写进设置对象（随 `settings.json` 一起持久化）。</summary>
        public static void CaptureInto(GameSettings settings)
        {
            if (settings == null) return;
            var list = new BindingEntry[Actions.Count];
            for (int i = 0; i < Actions.Count; i++)
            {
                BindableAction a = Actions[i];
                list[i] = new BindingEntry
                {
                    context = a.Context == InputContext.Town ? "town" : "combat",
                    action = a.Id,
                    path = a.Path,
                };
            }
            settings.bindings = list;
        }

        // ══════════════════════════════════════════════════════════════════
        // 设备层：找"刚按下的那一个键"
        // ══════════════════════════════════════════════════════════════════

        private static bool AnyHeld()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            Mouse m = Mouse.current;
            if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed)) return true;
            return false;
        }

        /// <summary>返回刚按下的控件路径；"按了 Esc" 用空串表示取消；没按 = null。</summary>
        private static string DetectPressedPath()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                var keys = kb.allKeys;
                for (int i = 0; i < keys.Count; i++)
                {
                    KeyControl k = keys[i];
                    if (k == null || !k.wasPressedThisFrame) continue;
                    if (k.keyCode == Key.Escape) return string.Empty;
                    return "<Keyboard>/" + k.name;
                }
            }
            Mouse m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame) return "<Mouse>/leftButton";
                if (m.rightButton.wasPressedThisFrame) return "<Mouse>/rightButton";
                if (m.middleButton.wasPressedThisFrame) return "<Mouse>/middleButton";
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // 读数
        // ══════════════════════════════════════════════════════════════════

        public static string ContextName(InputContext ctx)
            => ctx == InputContext.Town ? "主城" : "战斗";

        /// <summary>两套上下文全部绑定的读数（面板 / 报告用）。</summary>
        public static string Describe(bool onlyContext = false, InputContext ctx = InputContext.Town)
        {
            var sb = new StringBuilder(512);
            if (!_built) return "输入层未建立";
            for (int i = 0; i < Actions.Count; i++)
            {
                BindableAction a = Actions[i];
                if (onlyContext && a.Context != ctx) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(ContextName(a.Context)).Append('/').Append(a.Label)
                  .Append(" = ").Append(a.Path)
                  .Append(a.Resolves ? "" : "  [解析不到]")
                  .Append(a.IsDefault ? "" : "  [已改键]")
                  .Append(a.Action != null && a.Action.IsPressed() ? "  ◀ 正按住" : "");
            }
            return sb.ToString();
        }

        /// <summary>一行读数：改键落没落盘、当前有几条不是默认。</summary>
        public static string Summary()
        {
            int changed = 0;
            for (int i = 0; i < Actions.Count; i++) if (!Actions[i].IsDefault) changed++;
            return $"输入层={(_built ? "已建立" : "未建立")}｜动作数={Actions.Count}（主城 8 / 战斗 8）｜已改键={changed}｜" +
                   $"最近改键={LastRebindLog}";
        }
    }
}
