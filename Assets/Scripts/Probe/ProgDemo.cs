using System;
using System.IO;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v197：**机制植物 / 机制道具 / 天气的取证入口**（只读 + 造场景，不进任何玩法路径）★★
    ///
    /// ── 为什么需要一个自举的组件而不是静态类 ──
    ///   桥的 `drive` 只能"调某个**组件**上的方法"。`SelfCheck` / `ProgMechanics` 是静态类，
    ///   拿不到 ⇒ 与 `DebugApi.AutoCreate` 同一个做法：`AfterSceneLoad` 自举一个实例 ✓
    ///   （不复用 `DebugApi`：那个文件不在本轮的改动范围内，往里加方法会让它跟另一路撞车）
    ///
    /// ── 抓图为什么不用 `ScreenCapture.CaptureScreenshot` ──
    ///   用户硬约束：**`CaptureScreenshotAsTexture()` ＋ `EncodeToPNG()` ＋ `WriteAllBytes()`**，
    ///   并且要能读出"不是黑图"的判据（本类会把亮度 min/max/均值与亮像素占比一起打出来）✓
    ///   黑图标尺：`254,994` / `29,690` 字节 —— 低于这个量级就是黑图 ✓
    /// </summary>
    public sealed class ProgDemo : MonoBehaviour
    {
        /// <summary>抓图输出目录（在 `Library/` 下：不进版本库、不打包）。</summary>
        public const string ShotDir = "Library/yowei-shots";

        private static ProgDemo _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<ProgDemo>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("ProgDemo");
            go.AddComponent<ProgDemo>();
        }

        private void Awake() { _instance = this; }

        private RunSession Session
            => GameBootstrap.Live != null && GameBootstrap.Live.Count > 0 ? GameBootstrap.Live[0].Session : null;

        // ────────────────────────── 造场景 ──────────────────────────

        /// <summary>
        /// 关掉"选择初始种子"的模态并直接进战斗阶段（抓图时会被那块面板挡住田）。
        /// 走的是公开入口 `SetPhasePublic` / `StartWave`，不反射、不改私有状态 ✓
        /// </summary>
        public string StartCombat()
        {
            RunSession s = Session;
            if (s == null) return "ProgDemo：没有会话";
            if (s.Phase == PhaseState.Prepare)
            {
                if (s.InitialSeed == null && s.InitialChoices != null && s.InitialChoices.Length > 0)
                    s.InitialSeed = s.InitialChoices[0];
                s.SetPhasePublic(PhaseState.Combat);
                s.StartWave(1);
            }
            return "ProgDemo：阶段=" + s.Phase;
        }

        /// <summary>把 6 株机制植物各栽一列（同一列 3 格都会长同一株）。</summary>
        public string PlantMechSeeds()
        {
            RunSession s = Session;
            if (s == null) return "ProgDemo：没有会话";
            var sb = new System.Text.StringBuilder("ProgDemo 栽种：");
            string[] ids =
            {
                "warblade_bloom", "gale_vine", "sunflower_crown",
                "rainlily", "shelter_ivy", "lucky_monarch"
            };
            for (int i = 0; i < ids.Length; i++)
            {
                SeedDef def = TableDatabase.GetSeed(ids[i]);
                bool ok = s.AutoPlantSeed(def);
                sb.Append(' ').Append(ids[i]).Append(ok ? "=OK" : "=失败");
            }
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        /// <summary>把 10 件机制道具全部买到（各 1 件；`maxStack` 允许的再补满）。</summary>
        public string GiveMechItems()
        {
            RunSession s = Session;
            if (s == null) return "ProgDemo：没有会话";
            string[] ids =
            {
                "carrot_ring", "bunny_pet", "evade_helm", "big_can", "windup_gear",
                "charge_coil", "aegis_lamp", "gamble_glove", "sharp_spray", "tycoon_coin"
            };
            var sb = new System.Text.StringBuilder("ProgDemo 给道具：");
            for (int i = 0; i < ids.Length; i++)
            {
                ItemDef it = TableDatabase.GetItem(ids[i]);
                if (it == null) { sb.Append(' ').Append(ids[i]).Append("=查不到"); continue; }
                int give = Mathf.Clamp(it.MaxStack, 1, it.MaxStack);
                for (int k = 0; k < give; k++) s.ApplyItem(it);
                sb.Append(' ').Append(ids[i]).Append('×').Append(give);
            }
            s.AddGoldDirect(1500);      // 富豪那条要金币才看得见额外伤害
            Debug.Log(sb.ToString());
            return sb.ToString();
        }

        /// <summary>只给胡萝卜 + 兔子（用于抓"角色身边"的两张图）。</summary>
        public string GiveOrbitAndPet()
        {
            RunSession s = Session;
            if (s == null) return "ProgDemo：没有会话";
            ItemDef carrot = TableDatabase.GetItem("carrot_ring");
            ItemDef bunny = TableDatabase.GetItem("bunny_pet");
            for (int i = 0; i < 3; i++) s.ApplyItem(carrot);
            s.ApplyItem(bunny);
            return "ProgDemo：胡萝卜 ×3 + 兔子 ×1";
        }

        public string Sunny() { ProgMechanics.DebugForceWeather(WeatherKind.Sunny, 600f); return "ProgDemo：大晴天"; }
        public string Rain() { ProgMechanics.DebugForceWeather(WeatherKind.Rain, 600f); return "ProgDemo：雨天"; }
        public string ClearWeather() { ProgMechanics.DebugForceWeather(WeatherKind.None); return "ProgDemo：天气清除"; }

        /// <summary>把玩家治满（抓图期间站桩会被小怪打死；只是把血补回去，不改任何规则）。</summary>
        public string Heal()
        {
            RunSession s = Session;
            if (s == null || s.World == null || s.World.Player == null) return "ProgDemo：没有玩家";
            PlayerActor p = s.World.Player;
            p.Health.Heal(p.Health.MaxHp);
            if (s.Phase == PhaseState.Defeat) s.SetPhasePublic(PhaseState.Combat);
            return $"ProgDemo：血 {p.Health.Hp:0}/{p.Health.MaxHp:0}｜阶段 {s.Phase}";
        }

        /// <summary>往田里灌满水并让所有作物接近成熟（抓图时看得见"植株全长出来"）。</summary>
        public string RipenFarm()
        {
            RunSession s = Session;
            if (s == null) return "ProgDemo：没有会话";
            FarmPlot[] slots = s.World.Farm.Slots;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null || !p.Unlocked) continue;
                if (p.State == PlotState.Growing)
                {
                    // 用**公开 API** 推进生长（`TickGrowth2` 是成长唯一入口；
                    // `ApplyGrowthFactor` 是私有的，探针不该为了改一个数去反射）
                    p.TickGrowth2(600f, 1f, 0f);
                    p.AddWater(Mathf.Max(0, p.WaterMax - p.Water));
                    n++;
                }
            }
            return $"ProgDemo：催熟 {n} 格";
        }

        /// <summary>触发一次收割增益（植物机制 1/2）：对田里每一格调一次 RunSession.HarvestPlot。</summary>
        public string HarvestAll()
        {
            RunSession s = Session;
            if (s == null) return "ProgDemo：没有会话";
            FarmPlot[] slots = s.World.Farm.Slots;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                FarmPlot p = slots[i];
                if (p == null || !p.IsRipe) continue;
                if (s.HarvestPlot(p)) n++;
            }
            string buffs = "Buff 表：" + s.Buffs.Count + " 条";
            for (int i = 0; i < s.Buffs.Active.Count; i++)
            {
                BuffInstance b = s.Buffs.Active[i];
                buffs += $"\n  {b.Id}｜{b.Label}｜层 {b.Stacks}｜剩 {b.Remain:0.00}s｜图标 #{b.IconMaskIdx}";
            }
            Debug.Log("ProgDemo 收割 " + n + " 格｜" + buffs);
            return "ProgDemo 收割 " + n + " 格\n" + buffs;
        }

        // ────────────────────────── 抓图 ──────────────────────────

        /// <summary>
        /// 抓一张图（**用户硬约束的三件套**：`CaptureScreenshotAsTexture` + `EncodeToPNG` + `WriteAllBytes`）。
        /// 返回一行读数：路径 / 像素 / 字节 / 亮度 min-max-均值 / 亮像素比 ⇒ 能自证不是黑图 ✓
        /// </summary>
        public string Shot(string name)
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), ShotDir);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (tex == null) return "ProgDemo：CaptureScreenshotAsTexture 返回 null";
            string path = Path.Combine(dir, name + ".png");
            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(path, png);

            Color32[] px = tex.GetPixels32();
            int min = 255, max = 0;
            long sum = 0;
            int bright = 0;
            for (int i = 0; i < px.Length; i++)
            {
                int l = (px[i].r * 30 + px[i].g * 59 + px[i].b * 11) / 100;
                if (l < min) min = l;
                if (l > max) max = l;
                sum += l;
                if (l > 40) bright++;
            }
            double mean = px.Length > 0 ? (double)sum / px.Length : 0;
            int w = tex.width, h = tex.height;
            UnityEngine.Object.Destroy(tex);
            string line = $"ProgDemo 写图 {path}｜{w}×{h}｜{png.Length} 字节"
                        + $"｜亮度 min {min} max {max} 均值 {mean:0.#}｜亮像素 {bright}/{px.Length}"
                        + $"｜{(bright > px.Length / 20 ? "非黑图 ✓" : "疑似黑图 ✗")}";
            Debug.Log(line);
            return line;
        }

        /// <summary>
        /// 五张证据图里的第一张：田里的机制植物。
        /// ⚠️ 其余四张（胡萝卜环 / 兔子 / 大晴天 / 雨天）由调用方**分步**驱动：
        ///   每一张之间要换场景状态（给道具 / 切天气），一次跑完会得到五张同状态的图 ✗
        /// </summary>
        public string ShotPlants()
        {
            return Shot("v197-1-plants");
        }
    }
}
