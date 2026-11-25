using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// **全工程的音频总线** —— 唯一的播放点。
    ///
    /// ════════════════════════════════════════════════════════════════════════
    /// ★ 为什么会有这个文件（它之前**一行播放代码都没有**）★
    ///
    /// 接手时的真实状态（grep 取证，不是推测）：
    ///   · `Assets/Resources/Audio/` 下面躺着 **21 个 WAV**（3 BGM + 18 音效）✓
    ///   · `Assets/Scripts/**` 里 `AudioSource` / `PlayOneShot` / `AudioClip` 的出现次数 = **0** ✗
    ///   · 场景与预制体里序列化的 `AudioSource` = **0 个** ✗
    ///   · 只有 `GameBootstrap.ConfigureCamera()` 在"`Camera.main == null` 时才补一个
    ///     `AudioListener`"——而场景里的相机本来就在 ⇒ 连这一行都不会执行 ✓
    ///   ⇒ 用户说"音乐和音效没有增加"是**字面成立**的：资源在，但没有任何东西去播它 ✗
    ///
    /// ⇒ 所以不是"音量偏小"（不存在可调的 `volume`），而是**播放这一层整个缺失**。
    ///   本文件补的就是这一层。
    ///
    /// ★ 三条设计约束（缺一条就会退回"资源在、听不见"）★
    ///   ① **不许进 `GameBootstrap`**（那是别人正在改的文件）⇒ 自己 bootstrap：
    ///      `[RuntimeInitializeOnLoadMethod]` + 惰性建源，不依赖任何人的初始化顺序 ✓
    ///   ② **编辑模式一律不出声、不建对象** ⇒ `PlayCue` 第一行就是 `Application.isPlaying` ✓
    ///      于是 `SelfCheck.RunAll()`（编辑模式跑）读到的世界与改动之前**逐位相同** ✓
    ///   ③ **音频绝不反哺逻辑** ⇒ 本类不读不写 `World.Rng`、不改任何状态，
    ///      只用 `Time.unscaledTime` 做节流 ⇒ 同种子同输入仍然必得同结果 ✓
    ///
    /// ★ 音量链（为什么是这三个数）★
    ///   `Clip(-12 LUFS)` × `SfxVolume(0.85)` × `volumeScale(1.0)`         = 主音效
    ///   `Clip(-12 LUFS)` × `SfxVolume(0.85)` × `PluginGain(0.562 ≈ -5dB)` = 插件音效
    ///   ⇒ 两者**只差 `PluginGain` 这一级**，所以"-5 dB"是字面意义上的相对电平 ✓
    ///   详见 `PluginGainDb` 上面那一段。
    /// ════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static class GameAudio
    {
        // ─────────────────────────── 音量 ───────────────────────────

        /// <summary>主音量（0..1）。留在这里当**唯一**的总闸，将来接设置界面只改它。</summary>
        public const float MasterVolume = 1.00f;

        /// <summary>
        /// BGM 音量。**0.62**，不是 0.3 也不是 1.0 —— 理由：
        ///   · 三首 BGM 母带是 **−16 / −13.5 / −21 LUFS**（`report.txt` A 段），
        ///     而音效统一 **−12 LUFS** ⇒ 在**同一个 `volume`** 下 BGM 本来就比音效低 3.5~9 dB ✓
        ///   · 再乘 0.62（−4.2 dB）⇒ 战斗 BGM 落在约 −17.7 LUFS，比音效低 ≈5.7 dB ✓
        ///     "听得见但不抢戏"这个目标就落在这个区间（−3~−8 dB）✓
        ///   · 门槛检查：`0.62 > 0.5` ✓（任务里"BGM 被压到 &lt; 0.5 就调上来"的那条线）
        /// </summary>
        public const float BgmVolume = 0.62f;

        /// <summary>
        /// 音效音量。**0.85**（−1.4 dB），不是 1.0 —— 理由：
        ///   · 音效母带已经是 −12 LUFS 的满刻度响度，peak 也只到 −1 dBFS，
        ///     再乘 1.0 就没有任何余量给"多声叠加"（多柱 3 下、连环 3 爆）✓
        ///   · 0.85 给出 1.4 dB 的相加余量，同时比 BGM 高 ≈2.7 dB（相对关系保持）✓
        ///   · 门槛检查：`0.85 > 0.6` ✓（任务里"SFX &lt; 0.6 就调上来"的那条线）
        /// </summary>
        public const float SfxVolume = 0.85f;

        /// <summary>
        /// 插件音效相对主音效的电平：**−5 dB**。
        ///
        /// ★ 为什么必须是"低一档"而不是同电平 ★
        /// 12 条插件音效与那 18 条走的是**同一条母带链**（同一个 `master_sfx`），
        /// 所以它们的 LUFS 一样是 −12 ⇒ 同 `volumeScale` 下**一样响** ✗
        /// 而插件音效是**叠在它自己的主音效之上**触发的（挥击 + 灼烧、激光 + 减速），
        /// 同电平就是两把声音互相盖 ⇒ 玩家听到的是一团糊，而不是"多了一个效果" ✗
        ///
        /// ★ 为什么取 −5 而不是 −6 / −4 ★
        ///   · −4 dB (0.63)：两件插件同时触发时为 0.63×√2 = 0.89 ⇒ 只差 1 dB，
        ///     仍然会盖掉主音效的尾巴 ⇒ 太吵 ✓
        ///   · −6 dB (0.50)：正好"一半"，原则上没问题，但 S 档插件（吸血 / 自充）是整个
        ///     构筑里**最贵**的那几件，它是玩家最想听见的反馈 ⇒ 再压低容易听不见 ✓
        ///   · −5 dB (0.562)：两件叠加 = 0.562×√2 = 0.79（−2.0 dB）⇒ 仍在主音效之下 ✓
        ///     同时比 −6 dB 响 1 dB，S 档听得见 ✓
        ///   ⇒ 落在任务给的 **4~6 dB** 区间中部，且两两叠加时仍不越顶 ✓
        /// </summary>
        public const float PluginGainDb = -5f;

        /// <summary>`PluginGainDb` 的线性形式（≈0.5623），给 `PlayOneShot` 用。</summary>
        public static readonly float PluginGain = Mathf.Pow(10f, PluginGainDb / 20f);

        // ─────────────────────── 防刷屏（冷却）───────────────────────

        /// <summary>
        /// **同一条音效的最小重触发间隔**（秒）—— 任务要求的 0.1 s 是**下限**，
        /// 这里取 0.12 s，给"一帧里连触两次"留 1 个固定帧的余量 ✓
        ///
        /// ★ 为什么必须有它 ★
        /// `PluginPostHit` 是**逐个敌人**调用的，`FireBeam` / `TickBeam` 是**每个固定帧**
        /// 调用的（60 Hz），`ExplodeAt` 在连环下**同一帧**调两次 ⇒
        /// 没有冷却的话：多柱 = 每秒 60 声、"连环"= 一帧两声叠成一记爆音 ✗
        /// 有了它，同一件插件在 0.1 s 内最多响一次 ✓（`Suppressed` 记的就是被吞掉的次数）
        /// </summary>
        public const float PluginCooldown = 0.12f;

        // ─────────────────────── 句柄 / 缓存 ───────────────────────

        private static AudioSource _sfx;
        private static AudioSource _bgm;
        private static bool _dead;          // 建源失败过一次就不再重试（避免每帧 new GameObject）
        private static bool _installed;     // 事件订阅只做一次（防"关掉域重载"时的重复订阅）
        private static string _bgmName;

        private static readonly Dictionary<string, AudioClip> ClipCache =
            new Dictionary<string, AudioClip>();

        // ─────────────────── 自证用的计数器（只读）───────────────────

        /// <summary>真的播出去多少次（不含被冷却吞掉的）。</summary>
        public static int Played { get; private set; }
        /// <summary>被冷却吞掉多少次 —— 它**大于 0** 就证明防刷屏在工作 ✓</summary>
        public static int Suppressed { get; private set; }
        /// <summary>`Resources.Load` 没找到的次数 —— 大于 0 说明 WAV 没打进工程/路径写错。</summary>
        public static int Missing { get; private set; }

        // ─────────────────────────── cue 表 ───────────────────────────
        //
        // 一张表管住"名字 / 冷却 / 缓存"，播放时只做下标运算 ⇒ 无字典查找、无字符串拼接
        // （字符串拼接在 60 Hz 的调用点上是纯 GC ✗）。

        /// <summary>12 件插件各一条，下标 = `(int)PluginEffectKind - 1`。</summary>
        private const int PluginCues = PluginFx.KindCount;

        private const int MainCueFirst = PluginCues;

        private const int CueHoeSwing = MainCueFirst + 0;
        private const int CueBeam = MainCueFirst + 1;
        private const int CueBlast = MainCueFirst + 2;
        private const int CueEnemyDie = MainCueFirst + 3;
        private const int CueEnemySpawn = MainCueFirst + 4;
        private const int CueLevelUp = MainCueFirst + 5;
        private const int CueHarvest = MainCueFirst + 6;
        private const int CueCoin = MainCueFirst + 7;
        private const int CueShopOpen = MainCueFirst + 8;
        private const int CueDefeat = MainCueFirst + 9;

        private const int MainCues = 10;
        private const int TotalCues = MainCueFirst + MainCues;

        /// <summary>`Resources` 下的完整路径（不含扩展名）。下标与上面的 cue 常量一致。</summary>
        private static readonly string[] CueResource = new string[TotalCues];
        private static readonly float[] CueCooldown = new float[TotalCues];
        private static readonly AudioClip[] CueClip = new AudioClip[TotalCues];
        private static readonly float[] LastAt = new float[TotalCues];

        static GameAudio()
        {
            // 插件 12 条：名字与 `Tools/audio_gen/sfx.py` 的 PLUGIN_SFX 一一对应
            Cue("Audio/Sfx/sfx_pl_swordwave", 0, PluginCooldown);
            Cue("Audio/Sfx/sfx_pl_burn", 1, PluginCooldown);
            Cue("Audio/Sfx/sfx_pl_doublestrike", 2, PluginCooldown);
            Cue("Audio/Sfx/sfx_pl_lifesteal", 3, PluginCooldown);
            // ★ 下面三条是"持续型"（按住水壶 / 每秒回水），触发点每帧都在：
            //   冷却取到 ≈ 片段本身长度，免得它们变成一条 60 Hz 的振荡器 ✗
            //   （仍然 ≥ `PluginCooldown`，所以"0.1 s 内只响一次"依旧成立 ✓）
            Cue("Audio/Sfx/sfx_pl_multijet", 4, 0.30f);
            Cue("Audio/Sfx/sfx_pl_slow", 5, PluginCooldown);
            Cue("Audio/Sfx/sfx_pl_thickjet", 6, 0.30f);
            Cue("Audio/Sfx/sfx_pl_autocharge", 7, 0.32f);
            Cue("Audio/Sfx/sfx_pl_subburst", 8, PluginCooldown);
            Cue("Audio/Sfx/sfx_pl_poison", 9, PluginCooldown);
            Cue("Audio/Sfx/sfx_pl_freeburst", 10, PluginCooldown);
            Cue("Audio/Sfx/sfx_pl_chain", 11, PluginCooldown);

            // 主音效 10 条（这一批是**顺带补齐**的：它们原来同样没有播放点，
            // 见类注释里"21 个 WAV / 0 个 AudioSource"那段）
            Cue("Audio/Sfx/sfx_hoe_swing", CueHoeSwing, 0.09f);
            Cue("Audio/Sfx/sfx_laser", CueBeam, 0.12f);
            Cue("Audio/Sfx/sfx_blast", CueBlast, 0.06f);
            Cue("Audio/Sfx/sfx_enemy_die", CueEnemyDie, 0.05f);
            Cue("Audio/Sfx/sfx_enemy_spawn", CueEnemySpawn, 0.15f);
            Cue("Audio/Sfx/sfx_levelup", CueLevelUp, 0.30f);
            Cue("Audio/Sfx/sfx_harvest", CueHarvest, 0.08f);
            Cue("Audio/Sfx/sfx_coin", CueCoin, 0.05f);
            Cue("Audio/Sfx/sfx_shop_open", CueShopOpen, 0.30f);
            Cue("Audio/Sfx/sfx_defeat", CueDefeat, 1.00f);

            // 起手把 `LastAt` 拉到很早 ⇒ "开局第一声"不会被自己的初始值 0 当成"刚刚播过" ✗
            for (int i = 0; i < TotalCues; i++) LastAt[i] = -999f;
        }

        /// <summary>往表里登记一条 cue。下标是**绝对** cue 下标（插件 0..11，主音效 12..21）。</summary>
        private static void Cue(string resource, int cue, float cooldown)
        {
            CueResource[cue] = resource;
            CueCooldown[cue] = cooldown;
        }

        // ─────────────────────────── 对外 API ───────────────────────────

        /// <summary>
        /// **一件插件触发了** —— 12 个调用点各一句，就是任务要求的"每件插件一个音效"。
        ///
        /// ★ 为什么参数是 `PluginEffectKind` 而不是插件 id 字符串 ★
        /// 12 个调用点自己就知道是哪件插件（`m.BurnLayersPerHit &gt; 0` 就是灼烧），
        /// 传枚举 = **编译期就能查出错别字**；传字符串则要等到运行时才发现"没这个文件" ✗
        /// 与 `PluginFx.For()` 用同一套枚举 ⇒ 表现层的"看得见"和"听得见"由同一个值驱动 ✓
        /// </summary>
        public static void PlayPlugin(PluginEffectKind kind)
        {
            int cue;
            switch (kind)
            {
                case PluginEffectKind.SwordWave: cue = 0; break;
                case PluginEffectKind.BurnOnHit: cue = 1; break;
                case PluginEffectKind.DoubleStrike: cue = 2; break;
                case PluginEffectKind.Lifesteal: cue = 3; break;
                case PluginEffectKind.MultiJet: cue = 4; break;
                case PluginEffectKind.SlowOnHit: cue = 5; break;
                case PluginEffectKind.ThickJet: cue = 6; break;
                case PluginEffectKind.AutoCharge: cue = 7; break;
                case PluginEffectKind.SubBurst: cue = 8; break;
                case PluginEffectKind.PoisonZone: cue = 9; break;
                case PluginEffectKind.FreeBurst: cue = 10; break;
                case PluginEffectKind.ChainBurst: cue = 11; break;
                default: return;                     // `None` 没有声音（它本来就不是一种效果）
            }
            PlayCue(cue);
        }

        // ─────────────────────────── 播放核心 ───────────────────────────

        /// <summary>
        /// 播一条 cue。**所有声音都从这里出去**（插件 / 主音效共一条路径）⇒
        /// 冷却、音量、缺失处理只写一份 ✓
        /// </summary>
        private static void PlayCue(int cue)
        {
            // ★ 编辑模式（自检 / 单测 / 编辑器预览）一律不出声、不建对象 ★
            //   少这一行的话，`SelfCheck` 里那些插件夹具会真的触发播放，
            //   往当前打开的场景里塞一个 "GameAudio" 物体 ✗（自检必须是纯的）
            if (!Application.isPlaying) return;
            if (cue < 0 || cue >= TotalCues) return;

            float now = Time.unscaledTime;
            if (now - LastAt[cue] < CueCooldown[cue]) { Suppressed++; return; }
            LastAt[cue] = now;

            EnsureSources();
            if (_sfx == null) return;

            AudioClip clip = Clip(cue);
            if (clip == null) { Missing++; return; }

            _sfx.PlayOneShot(clip, cue < PluginCues ? PluginGain : 1f);
            Played++;
        }

        /// <summary>惰性取 clip：第一次 `Resources.Load`，之后走缓存（**连 null 也缓存**）。</summary>
        private static AudioClip Clip(int cue)
        {
            AudioClip c = CueClip[cue];
            if (c != null) return c;
            string path = CueResource[cue];
            if (CacheTryGet(path, out c)) return c;
            c = Resources.Load<AudioClip>(path);
            ClipCache[path] = c;
            CueClip[cue] = c;
            return c;
        }

        private static bool CacheTryGet(string path, out AudioClip clip)
            => ClipCache.TryGetValue(path, out clip);

        /// <summary>
        /// 建两个 2D `AudioSource`（BGM / SFX）挂在一个 `DontDestroyOnLoad` 的根上。
        /// ★ 不用 `FindAnyObjectByType` 之类去找现成的源 ★ —— 工程里**一个都没有**
        ///   （grep 取证见类注释），所以这里不是"复用"而是"补建" ✓
        /// </summary>
        private static void EnsureSources()
        {
            if (_sfx != null || _dead) return;
            try
            {
                var root = new GameObject("GameAudio (auto)");
                Object.DontDestroyOnLoad(root);

                _sfx = root.AddComponent<AudioSource>();
                _sfx.playOnAwake = false;
                _sfx.loop = false;
                _sfx.spatialBlend = 0f;                 // 2D：本工程是俯视卡通风，不做 3D 声像
                _sfx.volume = Mathf.Clamp01(MasterVolume * SfxVolume);

                _bgm = root.AddComponent<AudioSource>();
                _bgm.playOnAwake = false;
                _bgm.loop = true;
                _bgm.spatialBlend = 0f;
                _bgm.volume = Mathf.Clamp01(MasterVolume * BgmVolume);
            }
            catch (System.Exception ex)
            {
                _dead = true;
                Log.Warn("音频源建不起来，本次运行不出声：" + ex.Message);
            }
        }

        // ─────────────────────────── BGM ───────────────────────────

        private const string BgmFarmDay = "bgm_farm_day";
        private const string BgmCombat = "bgm_combat";

        /// <summary>
        /// 换 BGM（同名且正在播 = 什么都不做，不会重头开始）。
        ///
        /// ★ 三首里只用了两首 ★ `bgm_night` 没有接：本工程**没有"夜晚"这个相位**
        ///   （`PhaseState` = Boot/Prepare/Combat/LevelReward/Shop/Defeat/Paused），
        ///   昼夜是另一套（`Art/DayNightCycle.cs`），把"哪一刻算夜里"从那里读出来
        ///   属于**另一个人的设计决定** ⇒ 这里不猜，留给他接 ✓（报告里已列出）
        /// </summary>
        public static void PlayBgm(string track)
        {
            if (!Application.isPlaying) return;
            if (string.IsNullOrEmpty(track)) return;
            if (track == _bgmName && _bgm != null && _bgm.isPlaying) return;

            EnsureSources();
            if (_bgm == null) return;

            string path = "Audio/Music/" + track;
            AudioClip c;
            if (!CacheTryGet(path, out c))
            {
                c = Resources.Load<AudioClip>(path);
                ClipCache[path] = c;
            }
            if (c == null) { Missing++; return; }

            _bgm.clip = c;
            _bgm.loop = true;
            _bgm.Play();
            _bgmName = track;
        }

        /// <summary>停 BGM（失败局用）。</summary>
        public static void StopBgm()
        {
            _bgmName = null;
            if (_bgm != null) _bgm.Stop();
        }

        // ─────────────────────── 事件接线 ───────────────────────
        //
        // ★ 为什么走 `Bus` 而不是去改每个玩法文件 ★
        // 这些事件**已经在发了**（`WorldView` 订阅它们画特效），所以音频只是
        // **再加一个订阅者**，与表现层完全同构 ✓ 玩法文件一行不动 ✓
        //
        // ★ 为什么 `AfterSceneLoad` ★
        // 要赶在第一帧玩法事件之前订阅好；`BeforeSceneLoad` 也可以，但那时
        // `TableDatabase` 还没 `Load`，订阅本身不依赖表，取更晚的那个更稳 ✓

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_installed) return;      // 关掉域重载时静态量会留下来 ⇒ 防重复订阅（双倍音量）✗
            _installed = true;

            Bus.Subscribe<MeleeSwingEvent>(OnMeleeSwing);
            Bus.Subscribe<BeamEvent>(OnBeam);
            Bus.Subscribe<FertilizerBlastEvent>(OnFertBlast);
            Bus.Subscribe<EnemyKilledEvent>(OnEnemyKilled);
            Bus.Subscribe<SpawnWarningEvent>(OnSpawnWarning);
            Bus.Subscribe<LevelUpEvent>(OnLevelUp);
            Bus.Subscribe<TreeMatureEvent>(OnTreeMature);
            Bus.Subscribe<GoldChangedEvent>(OnGoldChanged);
            Bus.Subscribe<PhaseChangedEvent>(OnPhase);

            PlayBgm(BgmFarmDay);

            // ★ 一行启动日志 ★ 我**听不到声音**，所以要有一个不靠耳朵也能确认"总线真的起来了"的证据：
            //   它同时把音量链念出来（BGM / SFX / 插件相对电平），以后调音量只看这一行就够 ✓
            Log.Info(Describe() + "；已订阅 9 个 Bus 事件；BGM 起播 " + BgmFarmDay);
        }

        private static void OnMeleeSwing(MeleeSwingEvent e) => PlayCue(CueHoeSwing);
        private static void OnBeam(BeamEvent e) => PlayCue(CueBeam);
        private static void OnFertBlast(FertilizerBlastEvent e) => PlayCue(CueBlast);
        private static void OnEnemyKilled(EnemyKilledEvent e) => PlayCue(CueEnemyDie);
        private static void OnSpawnWarning(SpawnWarningEvent e) => PlayCue(CueEnemySpawn);
        private static void OnLevelUp(LevelUpEvent e) => PlayCue(CueLevelUp);
        private static void OnTreeMature(TreeMatureEvent e) => PlayCue(CueHarvest);
        private static void OnGoldChanged(GoldChangedEvent e) => PlayCue(CueCoin);

        private static void OnPhase(PhaseChangedEvent e)
        {
            switch (e.To)
            {
                case PhaseState.Combat:
                    PlayBgm(BgmCombat);
                    break;
                case PhaseState.Prepare:
                case PhaseState.Shop:
                case PhaseState.LevelReward:
                    PlayBgm(BgmFarmDay);
                    break;
                case PhaseState.Defeat:
                    StopBgm();
                    PlayCue(CueDefeat);
                    break;
            }
        }

        /// <summary>一行摘要，给日志/调试入口直接用（不出声也能看出接线对不对）。</summary>
        public static string Describe()
            => $"音频：播放 {Played} 次，冷却吞掉 {Suppressed} 次，缺文件 {Missing} 次；"
             + $"BGM={(_bgmName ?? "无")}；SFX×{SfxVolume:0.00} BGM×{BgmVolume:0.00} 插件{PluginGainDb:+0;-0} dB";
    }
}
