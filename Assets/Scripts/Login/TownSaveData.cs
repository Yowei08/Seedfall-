using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 一个存档位里的全部内容（`JsonUtility` 直接序列化）★★
    ///
    /// 字段分四组，对应"要把主城的什么东西留下来"：
    ///   ① 身份 / 时间：`label`、`savedAtIso`、`playedSeconds`（存档位列表上显示的那三样）
    ///   ② 金币：`coins`
    ///   ③ 田地与仓库：`plants`（**id + 数量**，跨表改动的真相源）、`warehouse`（位置数组，冗余镜像）、
    ///      `plotSeedIds` / `plotSeed`（36 格，同样 id 为准 + 位置镜像）、`plotElapsed`、`plotMature`
    ///   ④ 拥有的东西：`skins` / `equippedSkin` / `items` / `itemCounts`
    ///
    /// ★ 为什么仓库和田地**同时**存 id 与位置 ★
    /// `TownState.Warehouse` 是**按种子表顺序**的 `int[]`。表一改（加一条、调顺序），
    /// 只存位置的老存档就会把"南瓜"读成"萝卜" —— 这类错误不报错、只是数值全错 ✗
    /// ⇒ 以 id 为准（`plants` / `plotSeedIds`），位置数组只用来**人眼核对与快速恢复**；
    ///   读档时 id 优先、id 缺失才回退到位置数组 ✓
    /// </summary>
    [Serializable]
    public sealed class TownSaveData
    {
        public int version = 1;

        // ① 身份 / 时间
        public string label = "";
        public string savedAtIso = "";
        public double playedSeconds;

        // ② 金币
        public long coins;

        // ③ 仓库与田地
        public string[] plants = Array.Empty<string>();       // 种子 id（仓库里有货的）
        public int[] plantCounts = Array.Empty<int>();        // 与 plants 对齐的数量
        public int[] warehouse = Array.Empty<int>();          // 位置镜像（长度 = 表条数）
        public string[] plotSeedIds = Array.Empty<string>();  // 36 格，"" = 空地
        public int[] plotSeed = Array.Empty<int>();           // 位置镜像（-1 = 空地）
        public double[] plotElapsed = Array.Empty<double>();
        public bool[] plotMature = Array.Empty<bool>();

        // ④ 拥有的东西
        public string[] skins = Array.Empty<string>();          // 拥有的皮肤 id（主城 TownSkins）
        public string[] equippedSkins = Array.Empty<string>();  // 穿着中的皮肤 id（槽位由皮肤表决定）
        public string[] items = Array.Empty<string>();
        public int[] itemCounts = Array.Empty<int>();

        // ⑤ ★ v254：BOSS 掉落的**幂等键**（发过哪几次击杀）★
        //
        // 为什么这个字段必须进存档（而不是只放内存）：
        //   BOSS 掉落是"在战斗里（Login 场景已被卸载、`TownState` 只剩内存）**当场**并进这份存档"的
        //   ⇒ 判断"这一击发过没有"的真值源只能是**文件本身**。
        //   键与那一颗种子写在**同一次原子写**里 ⇒ 磁盘上"有键"⇔"有那颗种子"，
        //   不存在"记了账没给货 / 给了货没记账"的中间态 ✓
        // 老存档没有这个字段 ⇒ `JsonUtility` 反序列化后是 null ⇒ 各处都按"空数组"兜底（不炸、不误判）✓
        public string[] bossSeedGrants = Array.Empty<string>();
    }

    /// <summary>
    /// ★★ `TownState` ↔ `TownSaveData` 的读写 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、读（`Capture`）—— 全部走公开属性，零反射
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownState.Coins` / `Warehouse` / `PlotSeed` / `PlotElapsed` / `PlotMature` 都是公开 getter ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、写（`Restore`）—— **只能反射**，理由写在这里，不是偷懒
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownState` 的属性全是 `{ get; private set; }`，并且**没有** Restore/SetCoins 之类的入口；
    /// 公开 API 只有 `Plant` / `Clear` / `AddSeed` / `TryBuy` / `Advance`：
    ///   · `AddSeed` 只能往上加，仓库里那 8 个初始赠种（`StarterStock`）去不掉 ⇒ 读档后仓库必然偏高 ✗
    ///   · `Coins` **只增不减**（唯一减少是 `TryBuy`），没有任何办法把金币设成存档值 ✗
    ///   · `PlotElapsed` 没有 setter，读档后成熟进度只能从 0 重新长 ✗
    /// ⇒ 三条都做不到"精确恢复"，所以这里用**反射写属性 setter**（不是写字段名，是找 `GetSetMethod(true)`），
    ///   抗重命名（属性改名会直接报"找不到属性"，不会静默写错）✓
    ///
    /// 为什么不去给 `TownState` 加一个 `Restore()`：`Assets/Scripts/Town/**` 这一轮**另有任务在改**
    /// （`TownBootstrap.cs` 明令禁改），往同一个类里加方法会制造冲突面 ⇒ 读档全部收在 `Scripts/Login/**` ✓
    /// 代价：反射一旦失效（属性被删/改名）就是读档失败 —— 所以失败一律 `Log.Error` + 返回 false + 面板红字，
    /// 绝不"读了但没生效"。
    /// </summary>
    public static class TownSaveIO
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>玩家开局就有的三件工具（与战斗里 1/2/3 号武器一致）。</summary>
        public static readonly string[] StarterItems = { "hoe", "watering_can", "fertilizer" };

        // ══════════════════════════════════════════════════════════════════
        // 读
        // ══════════════════════════════════════════════════════════════════

        /// <summary>把当前主城状态拍成存档。**不碰磁盘**。</summary>
        public static TownSaveData Capture(TownState state, double playedSeconds, string label)
        {
            var data = new TownSaveData
            {
                label = label,
                savedAtIso = DateTime.UtcNow.ToString("o"),
                playedSeconds = playedSeconds,
                coins = state != null ? state.Coins : 0L,
                // 即使状态为空，这些"玩家拥有的东西"也要写成 schema 完整的空数组，
                // 而不是留 null —— 读回来时 `deserialize` 才不会给出 null 数组（下游要少一层判空）
                skins = Array.Empty<string>(),
                equippedSkins = Array.Empty<string>(),
                items = (string[])StarterItems.Clone(),
                itemCounts = new int[StarterItems.Length],
            };
            for (int i = 0; i < data.itemCounts.Length; i++) data.itemCounts[i] = 1;
            if (state == null)
            {
                data.plants = Array.Empty<string>();
                data.plantCounts = Array.Empty<int>();
                data.warehouse = Array.Empty<int>();
                data.plotSeedIds = Array.Empty<string>();
                data.plotSeed = Array.Empty<int>();
                data.plotElapsed = Array.Empty<double>();
                data.plotMature = Array.Empty<bool>();
                return data;
            }

            int n = state.Warehouse.Length;
            data.warehouse = new int[n];
            Array.Copy(state.Warehouse, data.warehouse, n);

            // 仓库 → id + 数量（只存有货的；这个数组就是"拥有的植物"）
            var ids = new List<string>(n);
            var counts = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                if (state.Warehouse[i] <= 0) continue;
                TownSeedDef def = TownData.SeedAt(i);
                ids.Add(def != null ? def.id : ("#" + i));
                counts.Add(state.Warehouse[i]);
            }
            data.plants = ids.ToArray();
            data.plantCounts = counts.ToArray();

            int plots = TownState.PlotCount;
            data.plotSeed = new int[plots];
            data.plotSeedIds = new string[plots];
            data.plotElapsed = new double[plots];
            data.plotMature = new bool[plots];
            for (int p = 0; p < plots; p++)
            {
                int si = state.PlotSeed[p];
                data.plotSeed[p] = si;
                data.plotElapsed[p] = state.PlotElapsed[p];
                data.plotMature[p] = state.PlotMature[p];
                if (si < 0) { data.plotSeedIds[p] = ""; continue; }
                TownSeedDef def = TownData.SeedAt(si);
                data.plotSeedIds[p] = def != null ? def.id : "";
            }

            // 皮肤：另一路的 TownSkins（反射接入，拿不到就留空，不影响存档的其余部分）
            TownSkinBridge.Capture(data);

            // ★ v254：BOSS 掉落的幂等键 ★
            //   **只补键、不补颗数** —— 那一颗已经在上面 `state.Warehouse` 的捕获范围里了；
            //   这里只负责"发过哪些击杀"这个标记不丢（否则下次读档会以为没发过）。
            //   `state == null` 的那条早退分支**不写键**：存档不能声称发过一颗而仓库里没有它 ✓
            TownDropLedger.CaptureTo(data);
            return data;
        }

        // ══════════════════════════════════════════════════════════════════
        // 写
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把存档灌回**当前** `TownState`（没有就新建）。
        /// 返回 false 时 `error` 有原文 —— 调用方必须把它显示出来，不能当成"读档成功"继续跑。
        /// </summary>
        public static bool Restore(TownSaveData data, out string error)
        {
            error = null;
            if (data == null) { error = "存档内容为空"; return false; }

            try
            {
                TownData.Load();
            }
            catch (Exception e)
            {
                error = "主城配置表载入失败：" + (TownData.LastError ?? e.Message);
                Log.Error(LoginSettings.Tag + error);
                return false;
            }

            TownState state = TownState.Ensure();
            int seedCount = Mathf.Max(1, TownData.Seeds.Length);

            // ── 仓库 ──
            var warehouse = new int[seedCount];
            bool ok = true;

            if (data.plants != null && data.plants.Length > 0 && data.plants.Length == data.plantCounts?.Length)
            {
                // id 为准
                for (int i = 0; i < data.plants.Length; i++)
                {
                    int si = TownData.IndexOf(data.plants[i]);
                    if (si < 0)
                    {
                        Log.Warn(LoginSettings.Tag + $"存档里的种子 {data.plants[i]} 不在当前 town_seeds.json 里，已跳过");
                        continue;
                    }
                    warehouse[si] = Mathf.Clamp(data.plantCounts[i], 0, TownState.WarehouseCap);
                }
            }
            else if (data.warehouse != null && data.warehouse.Length == seedCount)
            {
                // 回退：位置数组（长度必须与当前表一致，否则位置对不上，宁可不读）
                Array.Copy(data.warehouse, warehouse, seedCount);
                for (int i = 0; i < seedCount; i++)
                    warehouse[i] = Mathf.Clamp(warehouse[i], 0, TownState.WarehouseCap);
            }
            else
            {
                Log.Warn(LoginSettings.Tag + "存档里既没有 plants 也没有长度匹配的 warehouse，仓库恢复为空");
            }

            // ── 田地 ──
            int plots = TownState.PlotCount;
            var plotSeed = new int[plots];
            var plotElapsed = new double[plots];
            var plotMature = new bool[plots];
            for (int p = 0; p < plots; p++)
            {
                plotSeed[p] = -1;
                string id = data.plotSeedIds != null && p < data.plotSeedIds.Length ? data.plotSeedIds[p] : null;
                if (!string.IsNullOrEmpty(id))
                {
                    plotSeed[p] = TownData.IndexOf(id);
                    if (plotSeed[p] < 0)
                        Log.Warn(LoginSettings.Tag + $"存档第 {p + 1} 号耕地上的 {id} 不在当前表里，按空地处理");
                }
                else if (data.plotSeed != null && p < data.plotSeed.Length && data.plotSeed[p] >= 0
                         && data.plotSeed[p] < seedCount)
                {
                    plotSeed[p] = data.plotSeed[p];                 // 回退：位置数组
                }

                if (data.plotElapsed != null && p < data.plotElapsed.Length)
                    plotElapsed[p] = Math.Max(0.0, data.plotElapsed[p]);
                if (data.plotMature != null && p < data.plotMature.Length)
                    plotMature[p] = data.plotMature[p];
            }

            // ── 逐个写回（每个 setter 单独判，失败就报是哪一个）──
            ok &= SetProperty(state, "Coins", data.coins < 0 ? 0L : data.coins, ref error);
            ok &= SetProperty(state, "Warehouse", warehouse, ref error);
            ok &= SetProperty(state, "PlotSeed", plotSeed, ref error);
            ok &= SetProperty(state, "PlotElapsed", plotElapsed, ref error);
            ok &= SetProperty(state, "PlotMature", plotMature, ref error);
            ok &= SetField(state, "_coinFraction", 0.0, ref error);

            if (!ok)
            {
                Log.Error(LoginSettings.Tag + "读档失败：" + error);
                return false;
            }

            state.Recompute();      // 派生量（已种/成熟/每秒产出/仓库总数）必须重算

            // ★★ v254：BOSS 掉的种子**必须能带回主城** ★★
            //   这一段就在"仓库被存档整份覆盖"的**下一行**：
            //   ① 以存档里的 `bossSeedGrants` 为准重建掉落台账（哪几次击杀已经发过）；
            //   ② 战斗里"加了但没来得及写盘"的那一颗，这里**补回内存仓库**（刚才的覆盖把它抹掉了）；
            //   ③ 还有待发的就再试一次落盘。
            //   没有这一段：战斗里打 BOSS 掉的那颗会在回主城读档时被**静默抹掉**（用户报的那个 bug）。
            TownDropLedger.AfterRestore(data);

            state.Recompute();      // 补发可能改了仓库 ⇒ 派生量再算一次（幂等，纯重算）

            // 皮肤（另一路的状态，反射还原；失败只记日志 —— 金币/田地/仓库已经恢复了，不该被它连累）
            if (TownSkinBridge.Restore(data, out string skinNote))
                Log.Info(LoginSettings.Tag + "读档：" + skinNote);
            else
                Log.Warn(LoginSettings.Tag + "读档：皮肤未还原 —— " + skinNote);

            Log.Info(LoginSettings.Tag + "读档完成 ← " + state.Snapshot());
            return true;
        }

        /// <summary>
        /// 把主城状态清空成"开局"（新游戏用）。
        ///
        /// ★★ 为什么**不能**用公开的 `TownState.Discard()` + `Ensure()` ★★
        /// `TownState.Current` 是静态单例，但 `TownBootstrap` 在 `Awake` 里把当时的实例**存进了自己的字段**
        /// （`private TownState _state`）⇒ `Discard()` 只是把静态属性换成一个新对象，
        /// `TownBootstrap._state` 还指着**旧对象**。
        /// 实测后果（这一次真的踩到了）：新游戏之后在主城种地，田里长出来了，
        /// 可 `TownState.Current.Snapshot()` 仍是 `planted=0 warehouse=[8,8,6,4,2,1,0,0]` —— **两个世界** ✗
        /// 存档存的是 `Current` ⇒ 存下去的是空的；而画面上玩家明明种了 4 块地。
        ///
        /// ⇒ 正确做法：**不换对象，只把它的内容改回初始值**（与 `Initialize()` 逐字段等价）：
        ///   `Coins=0 / _coinFraction=0 / Warehouse=开局赠种 / 36 格清空`，然后 `Recompute()`。
        /// 开局赠种从 `TownState` 的私有常量 `StarterStock` 反射读 —— 这样表里的赠种改了，这边自动跟随 ✓
        /// </summary>
        public static void ResetToNewGame()
        {
            string error = null;
            try
            {
                TownData.Load();
            }
            catch (Exception e)
            {
                error = "主城配置表载入失败：" + (TownData.LastError ?? e.Message);
                Log.Error(LoginSettings.Tag + error);
                return;
            }

            TownState state = TownState.Ensure();
            int seedCount = Mathf.Max(1, TownData.Seeds.Length);
            int[] starter = StarterStockOf();
            var warehouse = new int[seedCount];
            for (int i = 0; i < seedCount; i++)
            {
                int stock = starter != null && i < starter.Length ? starter[i] : 0;
                warehouse[i] = Mathf.Clamp(stock, 0, TownState.WarehouseCap);
            }

            int plots = TownState.PlotCount;
            var plotSeed = new int[plots];
            for (int i = 0; i < plots; i++) plotSeed[i] = -1;
            var plotElapsed = new double[plots];
            var plotMature = new bool[plots];

            bool ok = SetProperty(state, "Coins", 0L, ref error)
                      && SetProperty(state, "Warehouse", warehouse, ref error)
                      && SetProperty(state, "PlotSeed", plotSeed, ref error)
                      && SetProperty(state, "PlotElapsed", plotElapsed, ref error)
                      && SetProperty(state, "PlotMature", plotMature, ref error)
                      && SetField(state, "_coinFraction", 0.0, ref error);
            if (!ok)
            {
                Log.Error(LoginSettings.Tag + "新游戏重置失败：" + error);
                return;
            }

            state.Recompute();
            // ★ v254：新游戏 ⇒ 掉落台账也要清（否则上一个档的"发过哪几次击杀"会跟着进新档，
            //   让新档里那几次击杀的种子永远发不出来 —— 那是第二个静默坑）
            TownDropLedger.ResetAll();
            Log.Info(LoginSettings.Tag + "新游戏：主城状态已就地重置 ← " + state.Snapshot());
        }

        /// <summary>`TownState.StarterStock`（"开局送几颗种子"的私有常量，见 `Initialize`）。</summary>
        private static int[] StarterStockOf()
        {
            try
            {
                FieldInfo f = typeof(TownState).GetField("StarterStock", BindingFlags.Static | BindingFlags.NonPublic);
                return f != null ? f.GetValue(null) as int[] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool SetProperty(object target, string name, object value, ref string error)
        {
            PropertyInfo p = target.GetType().GetProperty(name, Flags);
            if (p == null) { error = $"TownState 上找不到属性 {name}（类被改过？）"; return false; }
            MethodInfo setter = p.GetSetMethod(true);
            if (setter == null) { error = $"TownState.{name} 没有 setter"; return false; }
            try
            {
                if (value != null && p.PropertyType != value.GetType())
                {
                    error = $"TownState.{name} 类型不是 {value.GetType().Name}（现 {p.PropertyType.Name}）";
                    return false;
                }
                setter.Invoke(target, new[] { value });
                return true;
            }
            catch (Exception e)
            {
                error = $"写 TownState.{name} 失败：{(e.InnerException != null ? e.InnerException.Message : e.Message)}";
                return false;
            }
        }

        private static bool SetField(object target, string name, object value, ref string error)
        {
            FieldInfo f = target.GetType().GetField(name, Flags);
            if (f == null) { error = $"TownState 上找不到字段 {name}"; return false; }
            try
            {
                f.SetValue(target, value);
                return true;
            }
            catch (Exception e)
            {
                error = $"写 TownState.{name} 失败：{(e.InnerException != null ? e.InnerException.Message : e.Message)}";
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 摘要（存档位那一行显示的内容就是它）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>一眼看懂的存档摘要：名字 / 金币 / 时长 / 田地 / 植物 / 道具 / 皮肤 / 保存时间。</summary>
        public static string Describe(TownSaveData d)
        {
            if (d == null) return "（空）";
            int planted = 0, mature = 0;
            if (d.plotSeedIds != null)
                for (int i = 0; i < d.plotSeedIds.Length; i++)
                    if (!string.IsNullOrEmpty(d.plotSeedIds[i])) { planted++; if (d.plotMature != null && i < d.plotMature.Length && d.plotMature[i]) mature++; }
            int plantKinds = d.plants != null ? d.plants.Length : 0;
            int plantTotal = 0;
            if (d.plantCounts != null) for (int i = 0; i < d.plantCounts.Length; i++) plantTotal += d.plantCounts[i];
            // ★ v252：整串改成**具名键 + 参数**（`L10n.T(key, args)`）★
            //   改前是 8 段中文用 `+` 拼起来 —— 那种串**永远进不了词表**（形状对不上），
            //   于是英文/日文界面上它就是中文（`Docs/captures/v252-shop-ja2` 那张图底下那行就是证据）✓
            //   现在：语序交给译文，数字交给 `string.Format` ⇒ 三种语言各自排得顺 ✓
            // ★ v252：**存档标签是"写盘时的中文"**（`LoginBootstrap` 里 `"主城存档 " + (位+1)`），
            //   于是英文/日文界面上那一段永远是中文（截图 `v252-login-ja3` 里的"主城存档 1"）。
            //   标签本身不能改——老存档里存的已经是那句话；**显示时**认一下这个模式，
            //   命中就换成当前语言的模板 ✓ 认不出来（玩家自定义/更早的版本）就原样显示 ✓
            string label = d.label ?? "";
            const string mainSlotPrefix = "主城存档 ";
            if (label.StartsWith(mainSlotPrefix, StringComparison.Ordinal))
                label = L10n.T("save.labelMainSlot", label.Substring(mainSlotPrefix.Length).Trim());

            return L10n.T("save.summary",
                           label,
                           d.coins.ToString("N0"),
                           Duration(d.playedSeconds),
                           planted.ToString(), TownState.PlotCount.ToString(), mature.ToString(),
                           plantKinds.ToString(), plantTotal.ToString(),
                           (d.items?.Length ?? 0).ToString(),
                           (d.skins?.Length ?? 0).ToString(), (d.equippedSkins?.Length ?? 0).ToString(),
                           LocalTime(d.savedAtIso));
        }

        /// <summary>秒 → "1h 02m 03s"。</summary>
        public static string Duration(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s";
            if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds:00}s";
            return $"{t.Seconds}s";
        }

        /// <summary>ISO-8601（UTC）→ 本地时间文本；解析失败原样返回。</summary>
        public static string LocalTime(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return "?";
            if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t))
                return t.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            return iso;
        }

        /// <summary>字段清单（报告里那段"存档 JSON 的字段清单"就是这个方法打印的）。</summary>
        public static string FieldList()
        {
            var sb = new StringBuilder(512);
            sb.Append("version(int) label(string) savedAtIso(string) playedSeconds(double)\n");
            sb.Append("coins(long)\n");
            sb.Append("plants(string[]=种子id) plantCounts(int[]) warehouse(int[]=位置镜像)\n");
            sb.Append("plotSeedIds(string[36]) plotSeed(int[36]) plotElapsed(double[36]) plotMature(bool[36])\n");
            sb.Append("skins(string[]=皮肤id) equippedSkins(string[]) items(string[]) itemCounts(int[])\n");
            sb.Append("bossSeedGrants(string[]=BOSS 掉落的幂等键，v254)");
            return sb.ToString();
        }
    }
}
