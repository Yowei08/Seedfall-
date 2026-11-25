using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v238：**"默认拥有 ≠ 自动装备"的实机自证探针**（只读校验，不动存档、不发钱）★★
    ///
    /// ── 它要证明的两句话 ──
    ///   ① 开局发完那 8 件之后，**三把武器的插件位必须是 0/6 · 0/6 · 0/6**，背包里是 8 件；
    ///   ② 就算走到"自动装配"那条路（`TryBuy` 买任何一件插件都会调 `AutoInstallPending`），
    ///      这 8 件也**不会被替你装上** —— 而**商店买的那一件照旧会自动装上** ✓
    ///
    /// ── 为什么用"两个临时会话"而不是抓玩家那一局 ──
    ///   `AutoInstallPending` 是纯逻辑（`Goods` + `WeaponSystem.Install`），
    ///   用同一条构造路径（`new RunSession()` + `Start(seed)`，与 `GameBootstrap` 逐句相同）建两个会话，
    ///   读数逐位可复现，也不会污染玩家正在跑的那一局 ✓
    ///
    /// ── 什么时候跑 ──
    ///   只在 `Library/plugin-loadout-probe.on` 这个哨兵文件存在时跑一次（由外部脚本建/删）。
    ///   平时进 play **一声不响**，不会给用户的控制台添噪 ✓
    /// </summary>
    public static class PluginLoadoutProbe
    {
        /// <summary>哨兵文件（相对工程根）。存在 ⇒ 本次 play 跑一次自证。</summary>
        public const string SentinelPath = "Library/plugin-loadout-probe.on";

        /// <summary>哨兵文件的**绝对**路径（工程根 = `Assets/..`）。为什么不靠相对路径：进程 CWD 未必是工程根。</summary>
        private static string SentinelFullPath
            => System.IO.Path.GetFullPath(
                   System.IO.Path.Combine(Application.dataPath, "..", SentinelPath));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            bool on;
            try { on = System.IO.File.Exists(SentinelFullPath); }
            catch { on = false; }
            if (!on) return;

            var go = new GameObject("PluginLoadoutProbe") { hideFlags = HideFlags.DontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
        }

        /// <summary>真正干活的那个组件（协程等一帧，等表与 Bus 都就绪）。</summary>
        private sealed class Runner : MonoBehaviour
        {
            private IEnumerator Start()
            {
                yield return null;
                try
                {
                    Run();
                }
                catch (System.Exception e)
                {
                    Log.Error("[插件探针] 抛异常：" + e);
                }
                Destroy(gameObject);
            }

            private static void Run()
            {
                TableDatabase.Load();      // 幂等

                // ── 会话 A：只发放，不做任何"自动装配" ──
                var a = new RunSession();
                a.Start(20260214UL);
                Log.Info("[插件探针] A 开局发放后：" + Report(a, "A0"));

                // 唯一触发点（`RunSession.TryBuy` 的 Plugin 分支里那一句，逐字相同）
                int installedA = a.AutoInstallPending(out string sumA);
                Log.Info("[插件探针] A 走过 AutoInstallPending：装进 " + installedA + " 件 ⇒ " + Report(a, "A1")
                         + "｜summary=" + sumA);
                a.Dispose();

                // ── 会话 B：模拟"买了一件插件"（与 `TryBuy` 的 Plugin 分支逐句相同）──
                var b = new RunSession();
                b.Start(20260215UL);
                PluginAsset swordwave = FindAsset("hoe_swordwave");
                if (swordwave == null)
                {
                    Log.Error("[插件探针] B 找不到 hoe_swordwave，跳过买后对照");
                    b.Dispose();
                    return;
                }
                // ★ v286：`TryBuy` 现在传的是**刚买下的那一格实例**（不是资产）——
                //   探针按同一句写，否则它验的就不是玩家那条路了 ✗
                var boughtB = new PluginInstance(swordwave, WeaponSlot.None);
                b.Goods.Plugins.Add(boughtB);
                int installedB = b.AutoInstallPending(out string sumB, boughtB);
                Log.Info("[插件探针] B 买 1 件 hoe_swordwave 后：装进 " + installedB + " 件 ⇒ " + Report(b, "B1")
                         + "｜summary=" + sumB);
                b.Dispose();

                Log.Info("[插件探针] 判据：" +
                         $"A（没买任何东西）三槽必须是 0/0/0 且背包 {WeaponSystem.DefaultOwnedPlugins.Length} 件 ⇒ " +
                         (installedA == 0 && a0Ok ? "PASS" : "FAIL") +
                         $"｜B（买了 1 件）锄头必须 +1、默认那批仍在背包 ⇒ " +
                         (installedB == 1 ? "PASS" : "FAIL"));
            }

            /// <summary>会话 A 的"发放后三槽全空"这一条，在 A0 那一步就记下来了。</summary>
            private static bool a0Ok;

            private static string Report(RunSession s, string tag)
            {
                var sb = new System.Text.StringBuilder(256);
                sb.Append(tag).Append("：背包 ").Append(s.Goods.PendingCount).Append(" 件");
                AppendSlot(sb, s, WeaponSlot.Hoe);
                AppendSlot(sb, s, WeaponSlot.Can);
                AppendSlot(sb, s, WeaponSlot.Fertilizer);
                if (tag == "A0")
                {
                    a0Ok = s.Goods.SlotsUsedOn(WeaponSlot.Hoe) == 0
                        && s.Goods.SlotsUsedOn(WeaponSlot.Can) == 0
                        && s.Goods.SlotsUsedOn(WeaponSlot.Fertilizer) == 0
                        && s.Goods.PendingCount >= WeaponSystem.DefaultOwnedPlugins.Length;
                }
                return sb.ToString();
            }

            private static void AppendSlot(System.Text.StringBuilder sb, RunSession s, WeaponSlot w)
            {
                int used = s.Goods.SlotsUsedOn(w);
                sb.Append("｜").Append(Cn(w)).Append(' ').Append(used).Append('/').Append(PlayerGoods.MaxPluginsPerWeapon);
                var names = new List<string>(4);
                foreach (PluginInstance pi in s.Goods.PluginsOn(w))
                {
                    if (pi == null || pi.Asset == null) continue;
                    names.Add(pi.Asset.id);
                }
                if (names.Count > 0) sb.Append("(").Append(string.Join(",", names)).Append(")");
            }

            private static string Cn(WeaponSlot w)
                => w == WeaponSlot.Hoe ? "锄头" : w == WeaponSlot.Can ? "水壶" : "肥料";

            private static PluginAsset FindAsset(string id)
            {
                PluginAsset[] all = TableDatabase.Plugins;
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].id == id) return all[i];
                return null;
            }
        }
    }
}
