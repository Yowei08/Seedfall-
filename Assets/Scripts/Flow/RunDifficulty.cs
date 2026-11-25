using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ v252：**难度选择（低 / 中 / 高）** —— 主城传送门前选定，进战斗后作用于敌人。
    ///
    /// ── 用户原话 ──
    ///   「降低游戏难度（前期敌人刷新速度和数量降低），在传送门进入战斗前增加难度选择功能和对应界面，
    ///     开放低、中、高（中：敌人基础血量加 20%，伤害加 20%，高：敌人基础血量加 50%，伤害加 50%）」
    ///
    /// ── 这个类负责什么、不负责什么 ──
    ///   ✓ 档位本身（枚举 + 两个乘数）、档位的读写、跨场景的持久化、给 UI 用的名字与说明
    ///   ✗ **不**负责界面（界面在 `TownHud` 的难度面板里）
    ///   ✗ **不**负责"乘到敌人身上"（那是 `EnemyDef.HpForWave` / `EnemyDef.DamageForWave`
    ///     这两个**唯一**出口乘以 <see cref="HpMult"/> / <see cref="DmgMult"/>）
    ///   ✗ **不**管刷怪节奏（那是 `tuning.json` 的 `waves.*` 与 `RunSession` 的刷怪段）
    ///
    /// ── 为什么存 `PlayerPrefs` 而不是一个纯静态字段 ──
    ///   主城 → 战斗是 `SceneManager.LoadScene` 换场景：静态字段**能**活过换场景，
    ///   但活不过域重载（脚本重新编译 / 编辑器重进 Play）—— 而"玩家选了高难度，
    ///   重编一次脚本就悄悄变回低难度"正是最让人说不清的那类 bug ✗
    ///   `PlayerPrefs` 两条都扛得住，代价只是读一次整数 ✓
    ///
    /// ── 默认值 ──
    ///   没存过 ⇒ **低（1.0 / 1.0）** ⇒ 与加这个功能之前**逐位相同** ✓
    ///   这条是硬要求：既有自检/回放/基准全部默认跑在低难度上，读数不许漂 ✓
    /// </summary>
    public static class RunDifficulty
    {
        /// <summary>三档。**顺序即 `PlayerPrefs` 里存的整数**，不要重排。</summary>
        public enum Level
        {
            Low = 0,    // 低：敌人基础血量 ×1.00、伤害 ×1.00（= 加本功能之前的读数）
            Mid = 1,    // 中：×1.20 / ×1.20
            High = 2,   // 高：×1.50 / ×1.50
        }

        public const string PrefsKey = "seedfall.difficulty";

        /// <summary>低档的两个乘数（= 1，所以低档是"无附加"，不是"另一个平衡"）。</summary>
        public const float LowHpMult = 1.00f, LowDmgMult = 1.00f;
        /// <summary>中档：血量 +20%、伤害 +20%。</summary>
        public const float MidHpMult = 1.20f, MidDmgMult = 1.20f;
        /// <summary>高档：血量 +50%、伤害 +50%。</summary>
        public const float HighHpMult = 1.50f, HighDmgMult = 1.50f;

        /// <summary>界面上的档位名（三档各一个词，词表键 `difficulty.low/mid/high`）。</summary>
        public static readonly string[] LevelNames = { "低", "中", "高" };

        private static bool _loaded;
        private static Level _current = Level.Low;

        /// <summary>当前档位（第一次读时从 `PlayerPrefs` 载入）。</summary>
        public static Level Current
        {
            get { EnsureLoaded(); return _current; }
        }

        /// <summary>敌人**基础血量**的全局乘数（`EnemyDef.HpForWave` 的唯一消费点）。</summary>
        public static float HpMult
        {
            get
            {
                switch (Current)
                {
                    case Level.Mid: return MidHpMult;
                    case Level.High: return HighHpMult;
                    default: return LowHpMult;
                }
            }
        }

        /// <summary>敌人**伤害**（碰撞 / 弹幕 / 跳砸三招）的全局乘数（`EnemyDef.DamageForWave` 的唯一消费点）。</summary>
        public static float DmgMult
        {
            get
            {
                switch (Current)
                {
                    case Level.Mid: return MidDmgMult;
                    case Level.High: return HighDmgMult;
                    default: return LowDmgMult;
                }
            }
        }

        public static bool IsHigh => Current == Level.High;

        /// <summary>切档并写盘。主城面板点按钮时调用。</summary>
        public static void Set(Level level)
        {
            EnsureLoaded();
            _current = level;
            PlayerPrefs.SetInt(PrefsKey, (int)level);
            PlayerPrefs.Save();
        }

        /// <summary>下一档（0→1→2→0）。面板上"点一下换一档"用它。</summary>
        public static Level Next()
        {
            return At(((int)Current + 1) % 3);
        }

        public static Level At(int index)
        {
            if (index <= 0) return Level.Low;
            if (index >= 2) return Level.High;
            return (Level)index;
        }

        /// <summary>
        /// 回到低档并**清掉存档**。给自检/无头回放用：
        /// 它们要求"不管上一次玩到哪一档，读到的都是加功能之前的读数"。
        /// </summary>
        public static void ResetForTests()
        {
            _loaded = true;
            _current = Level.Low;
            PlayerPrefs.SetInt(PrefsKey, (int)Level.Low);
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            int saved = PlayerPrefs.GetInt(PrefsKey, (int)Level.Low);
            _current = At(saved);
        }

        /// <summary>档位名（走词表：`difficulty.low` / `difficulty.mid` / `difficulty.high`）。</summary>
        public static string NameOf(Level level)
        {
            switch (level)
            {
                case Level.Mid: return L10n.T("difficulty.mid");
                case Level.High: return L10n.T("difficulty.high");
                default: return L10n.T("difficulty.low");
            }
        }

        /// <summary>档位效果的一行说明（面板按钮下面那行小字；值是档位自带的，不走词表也要能对上）。</summary>
        public static string EffectOf(Level level)
        {
            switch (level)
            {
                case Level.Mid: return L10n.T("difficulty.mid.fx");
                case Level.High: return L10n.T("difficulty.high.fx");
                default: return L10n.T("difficulty.low.fx");
            }
        }
    }
}
