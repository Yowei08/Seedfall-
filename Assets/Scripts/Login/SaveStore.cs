using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>一个存档位的**显示用**摘要（缓存住，别在 OnGUI 里每帧读盘）。</summary>
    public struct SlotInfo
    {
        public bool Exists;
        public string Label;
        public long Coins;
        public double PlayedSeconds;
        public string SavedAtIso;
        public string Summary;      // TownSaveIO.Describe 的结果
        public string Error;
    }

    /// <summary>
    /// ★★ 6 个存档位的落盘层：写 / 读 / 删 / 复制 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、路径
    /// ══════════════════════════════════════════════════════════════════════
    ///   `Application.persistentDataPath/Seedfall/saves/slot_1.json` … `slot_6.json`
    /// 一个槽一个文件（不是一个大 JSON）：
    ///   · 删 / 复制 = 文件操作，不需要"读整个存档集合再切一段"⇒ 破坏面最小 ✓
    ///   · 某一位写坏了，另外 5 位仍然能读 ✓（单文件方案里一处坏 = 全丢）
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、写入的原子性
    /// ══════════════════════════════════════════════════════════════════════
    /// 先写 `slot_N.json.tmp`，`File.Replace/Move` 覆盖正式文件。
    /// 直接 `WriteAllText` 到正式文件时，如果进程在写的中间被杀（或磁盘满），
    /// 正式文件就是**半截 JSON** ⇒ 玩家丢档 ✗ 换名是原子的，最坏情况是"这次没存上，上次的还在" ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、缓存
    /// ══════════════════════════════════════════════════════════════════════
    /// `OnGUI` 一帧会被调好几次（Layout / Repaint / 事件），每次读 6 个文件 + `JsonUtility` 解析
    /// 是白白烧掉的帧时间。⇒ <see cref="Refresh"/> 只在"写了 / 删了 / 复制了 / 开机"时跑，
    /// 面板每帧读的是缓存 <see cref="Infos"/> ✓
    /// </summary>
    public static class SaveStore
    {
        public const int SlotCount = 6;

        public static string Dir => Path.Combine(Application.persistentDataPath, LoginSettings.FolderName, "saves");
        public static string PathOf(int slot) => Path.Combine(Dir, "slot_" + (slot + 1) + ".json");

        private static readonly SlotInfo[] _infos = new SlotInfo[SlotCount];
        private static bool _loaded;

        /// <summary>6 位的显示摘要（长度恒为 6）。</summary>
        public static SlotInfo[] Infos
        {
            get
            {
                if (!_loaded) Refresh();
                return _infos;
            }
        }

        public static string LastError { get; private set; }

        // ══════════════════════════════════════════════════════════════════
        // 读
        // ══════════════════════════════════════════════════════════════════

        public static bool Exists(int slot) => slot >= 0 && slot < SlotCount && File.Exists(PathOf(slot));

        /// <summary>读一个槽的完整内容。</summary>
        public static bool Read(int slot, out TownSaveData data, out string error)
        {
            data = null;
            error = null;
            if (slot < 0 || slot >= SlotCount) { error = "存档位越界：" + slot; return false; }
            string path = PathOf(slot);
            if (!File.Exists(path)) { error = "第 " + (slot + 1) + " 位没有存档"; return false; }
            try
            {
                string text = File.ReadAllText(path);
                data = JsonUtility.FromJson<TownSaveData>(text);
                if (data == null) throw new InvalidOperationException("解析结果为空");
                return true;
            }
            catch (Exception e)
            {
                error = $"读 {SlotName(slot)} 失败：{e.Message}";
                Log.Error(LoginSettings.Tag + error);
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 写 / 删 / 复制
        // ══════════════════════════════════════════════════════════════════

        /// <summary>把存档写进某个槽（覆盖）。</summary>
        public static bool Write(int slot, TownSaveData data, out string error)
        {
            error = null;
            if (slot < 0 || slot >= SlotCount) { error = "存档位越界：" + slot; return false; }
            if (data == null) { error = "没有内容可写"; return false; }
            try
            {
                Directory.CreateDirectory(Dir);
                string path = PathOf(slot);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _loaded = false;
                Log.Info(LoginSettings.Tag + $"已写入 {SlotName(slot)} ← {path}");
                return true;
            }
            catch (Exception e)
            {
                error = $"写 {SlotName(slot)} 失败：{e.Message}";
                Log.Error(LoginSettings.Tag + error);
                return false;
            }
        }

        /// <summary>删一个槽（**不二次确认** —— 确认在 UI/入口层做，这一层只做文件操作）。</summary>
        public static bool Delete(int slot, out string error)
        {
            error = null;
            if (slot < 0 || slot >= SlotCount) { error = "存档位越界：" + slot; return false; }
            try
            {
                string path = PathOf(slot);
                if (!File.Exists(path)) { error = "第 " + (slot + 1) + " 位本来就是空的"; return false; }
                File.Delete(path);
                string tmp = path + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
                _loaded = false;
                Log.Info(LoginSettings.Tag + $"已删除 {SlotName(slot)}（{path}）");
                return true;
            }
            catch (Exception e)
            {
                error = $"删 {SlotName(slot)} 失败：{e.Message}";
                Log.Error(LoginSettings.Tag + error);
                return false;
            }
        }

        /// <summary>复制 <paramref name="from"/> 到 <paramref name="to"/>（内容按新时间戳另存一份，名字带 "(副本)"）。</summary>
        public static bool Copy(int from, int to, out string error)
        {
            error = null;
            if (!Read(from, out TownSaveData data, out error)) return false;
            if (to < 0 || to >= SlotCount) { error = "存档位越界：" + to; return false; }
            if (File.Exists(PathOf(to))) { error = "第 " + (to + 1) + " 位不是空的，不能复制进去"; return false; }

            data.label = string.IsNullOrEmpty(data.label) ? "存档 " + (to + 1) : data.label + " (副本)";
            data.savedAtIso = DateTime.UtcNow.ToString("o");
            if (!Write(to, data, out error)) return false;
            Log.Info(LoginSettings.Tag + $"已复制 {SlotName(from)} → {SlotName(to)}");
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // 查询：空位 / 最新
        // ══════════════════════════════════════════════════════════════════

        /// <summary>第一个空位；满了返回 -1。</summary>
        public static int FirstFree
        {
            get
            {
                for (int i = 0; i < SlotCount; i++) if (!Exists(i)) return i;
                return -1;
            }
        }

        public static int UsedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SlotCount; i++) if (Exists(i)) n++;
                return n;
            }
        }

        /// <summary>最近保存的那一位（"继续游戏"读它）；一个都没有返回 -1。</summary>
        public static int Newest
        {
            get
            {
                int best = -1;
                DateTime bestTime = DateTime.MinValue;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (!Read(i, out TownSaveData d, out _)) continue;
                    DateTime t;
                    if (!DateTime.TryParse(d.savedAtIso, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out t)) t = File.GetLastWriteTimeUtc(PathOf(i));
                    if (best < 0 || t > bestTime) { best = i; bestTime = t; }
                }
                return best;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // 缓存刷新
        // ══════════════════════════════════════════════════════════════════

        /// <summary>重读 6 个槽的摘要。写 / 删 / 复制之后会自动标脏，下次访问时重读。</summary>
        public static void Refresh()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (!Exists(i)) { _infos[i] = default; continue; }
                if (Read(i, out TownSaveData d, out string err))
                {
                    _infos[i] = new SlotInfo
                    {
                        Exists = true,
                        Label = d.label,
                        Coins = d.coins,
                        PlayedSeconds = d.playedSeconds,
                        SavedAtIso = d.savedAtIso,
                        Summary = TownSaveIO.Describe(d),
                        Error = null,
                    };
                }
                else
                {
                    _infos[i] = new SlotInfo { Exists = true, Label = "?" + SlotName(i), Summary = "读取失败", Error = err };
                }
            }
            _loaded = true;
        }

        /// <summary>标记缓存过期（下次访问 `<see cref="Infos"/>` 时重读）。</summary>
        public static void Invalidate() => _loaded = false;

        // ══════════════════════════════════════════════════════════════════
        // 文本
        // ══════════════════════════════════════════════════════════════════

        public static string SlotName(int slot) => "存档位 " + (slot + 1);

        /// <summary>6 行读文本（调试入口 / 报告用）。</summary>
        public static string List()
        {
            var sb = new StringBuilder(768);
            sb.Append("落盘目录：").Append(Dir).Append('\n');
            for (int i = 0; i < SlotCount; i++)
            {
                sb.Append(SlotName(i)).Append(" [").Append(Path.GetFileName(PathOf(i))).Append("] ");
                if (!Exists(i)) { sb.Append("（空）\n"); continue; }
                if (Read(i, out TownSaveData d, out string err)) sb.Append(TownSaveIO.Describe(d)).Append('\n');
                else sb.Append("读取失败：").Append(err).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>某一位的一行摘要（面板一行显示用）。</summary>
        public static string Line(int slot)
        {
            SlotInfo info = Infos[slot];
            if (!info.Exists) return "（空位）";
            return info.Summary;
        }
    }
}
