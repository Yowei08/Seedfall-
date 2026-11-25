using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Reflection;

namespace UnityEngine
{
    public class TextAsset
    {
        public string text;
    }

    public static class Resources
    {
        /// <summary>Resources/Data/l10n_xx.json 从磁盘读；只实现 L10n 用到的那一种路径。</summary>
        public static T Load<T>(string path) where T : TextAsset
        {
            string code = path.Substring(path.LastIndexOf('/') + 1);
            string file = Path.Combine(Program.ResourceRoot, "Data", code + ".json");
            if (!File.Exists(file)) return null;
            return new TextAsset { text = File.ReadAllText(file, Encoding.UTF8) } as T;
        }
    }

    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
                return (T)Map(typeof(T), doc.RootElement);
        }

        private static object Map(Type t, JsonElement e)
        {
            if (t == typeof(string)) return e.ValueKind == JsonValueKind.Null ? null : e.GetString();
            if (t == typeof(int)) return e.GetInt32();
            if (t == typeof(bool)) return e.GetBoolean();
            if (t.IsArray)
            {
                Type el = t.GetElementType();
                var arr = Array.CreateInstance(el, e.GetArrayLength());
                int i = 0;
                foreach (JsonElement x in e.EnumerateArray()) arr.SetValue(Map(el, x), i++);
                return arr;
            }
            object obj = Activator.CreateInstance(t);
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                JsonElement v;
                if (e.TryGetProperty(f.Name, out v)) f.SetValue(obj, Map(f.FieldType, v));
            }
            return obj;
        }
    }

    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, int> Ints = new Dictionary<string, int>();
        public static int Writes;
        public static int GetInt(string key, int def)
        {
            int v;
            return Ints.TryGetValue(key, out v) ? v : def;
        }
        public static void SetInt(string key, int value) { Writes++; Ints[key] = value; }
        public static void Save() { }
    }

    public enum SystemLanguage { Unknown, English, Chinese, Japanese, Korean, Russian }

    public static class Application
    {
        public static SystemLanguage systemLanguage { get { return SystemLanguage.Chinese; } }
    }

    public static class Debug
    {
        public static void LogWarning(object m) { Program.Warn.Add(m == null ? "null" : m.ToString()); }
        public static void Log(object m) { Program.Warn.Add(m == null ? "null" : m.ToString()); }
    }

    public enum RuntimeInitializeLoadType { BeforeSceneLoad, AfterSceneLoad }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { }
    }
}

public static class Program
{
    public static string ResourceRoot;
    public static readonly List<string> Warn = new List<string>();

    public static int Main(string[] args)
    {
        ResourceRoot = args[0];
        string outPath = args[1];
        var sb = new StringBuilder();

        sb.AppendLine("ResourceRoot = " + ResourceRoot);
        sb.AppendLine("zh.json exists = " + File.Exists(Path.Combine(ResourceRoot, "Data", "l10n_zh.json")));
        sb.AppendLine("en.json exists = " + File.Exists(Path.Combine(ResourceRoot, "Data", "l10n_en.json")));
        sb.AppendLine();

        sb.AppendLine("== L10n.Report() ==");
        sb.AppendLine(Seedfall.L10n.Report());
        sb.AppendLine();

        sb.AppendLine("== L10n.SelfTest() ==");
        sb.AppendLine(Seedfall.L10n.SelfTest());
        sb.AppendLine();

        // 逐条读数：同一批 key 在 6 门语言下的实际输出
        string[] keys = { "hud.gold", "ui.settings.title", "ui.settings.language", "combat.death", "ui.settings.subtitle" };
        string[] srcs =
        {
            "\u5b88\u56ed\u5931\u8d25",                          // 守园失败
            "\u91d1\u5e01\u30001234",                            // 金币　1234  (U+3000)
            "\u7b2c 3 \u5206\u949f\u3000\u6e38\u73a9 1:23",      // 第 3 分钟　游玩 1:23
            "\u4ed3\u5e93\u91cc\u4e00\u9897\u79cd\u5b50\u90fd\u6ca1\u6709 \u2014\u2014 \u79cd\u5b50\u53ea\u80fd\u9760\u6253 BOSS \u6389\u843d\uff08\u5317\u9762\u4f20\u9001\u95e8\u56de\u6218\u6597\uff09\u3002",
        };

        sb.AppendLine("== per-language readings ==");
        for (int i = 0; i < Seedfall.L10n.LangCount; i++)
        {
            Seedfall.Lang lang = Seedfall.L10n.At(i);
            Seedfall.L10n.Set(lang);
            sb.AppendLine("-- " + Seedfall.L10n.Code(lang) + "  (Current=" + Seedfall.L10n.Current +
                          ", IsRtl=" + Seedfall.L10n.IsRtl + ", fonts=" + string.Join("/", Seedfall.L10n.Fonts) + ")");
            foreach (string k in keys)
                sb.AppendLine("     T(" + k + ") = " + Quote(Seedfall.L10n.T(k)));
            foreach (string s in srcs)
                sb.AppendLine("     S(" + Quote(s) + ") = " + Quote(Seedfall.L10n.S(s)));
        }

        sb.AppendLine();
        sb.AppendLine("== persistence ==");
        Seedfall.L10n.Set(Seedfall.Lang.Ja);
        sb.AppendLine("after Set(Ja): PlayerPrefs writes=" + UnityEngine.PlayerPrefs.Writes +
                      " saved=" + UnityEngine.PlayerPrefs.GetInt(Seedfall.L10n.PrefsKey, -1));
        int before = UnityEngine.PlayerPrefs.Writes;
        Seedfall.L10n.SelfTest();
        sb.AppendLine("SelfTest wrote prefs " + (UnityEngine.PlayerPrefs.Writes - before) + " times (must be 0)");
        sb.AppendLine("after SelfTest: Current=" + Seedfall.L10n.Current +
                      " saved=" + UnityEngine.PlayerPrefs.GetInt(Seedfall.L10n.PrefsKey, -1));

        sb.AppendLine();
        sb.AppendLine("== warnings ==");
        if (Warn.Count == 0) sb.AppendLine("(none)");
        else foreach (string w in Warn) sb.AppendLine(w);

        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("written: " + outPath);
        return 0;
    }

    private static string Quote(string s)
    {
        if (s == null) return "<null>";
        return "\"" + s.Replace("\n", "\\n") + "\"";
    }
}
