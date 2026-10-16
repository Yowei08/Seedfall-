// YoweiBridge — a tiny in-editor HTTP bridge for DeepSeek Harness.
//
// WITHOUT THIS FILE there is no connection. The DSH side can see that Unity.exe
// is running, but nothing it sends can reach the editor, because the editor
// opens no port of its own. This script adds one.
//
// INSTALL
//   1. Copy this file to  <your project>/Assets/Editor/YoweiBridge.cs
//   2. Return to Unity. It compiles automatically; the console prints
//      "[YoweiBridge] listening on http://127.0.0.1:17890/".
//   3. `unity_editor` can then drive play/pause/stop/step/refresh.
//
// REMOVE: delete the file. Nothing else is touched.
//
// ---------------------------------------------------------------------------
// THREADING, which is the whole design constraint here.
//
// HttpListener delivers on a background thread. Most Unity APIs — including
// Application.unityVersion, EditorApplication.isPlaying and
// SceneManager.GetActiveScene — are MAIN-THREAD ONLY. Calling them from the
// listener thread deadlocks that thread inside the request handler, so it never
// returns to GetContext(), every later request queues behind it, and the client
// sees a TIMEOUT rather than an error. That is exactly what the first version of
// this file did: the port was bound and listening, yet every request hung.
//
// So the status snapshot is built on the main thread and the listener thread
// only ever reads the cached string. Commands are dispatched to the main thread
// and never executed on the listener thread.
// ---------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class YoweiBridge
{
    /**
     * The first port tried. A SECOND EDITOR TAKES THE NEXT ONE, which is the point of the range: with a
     * single fixed port the second editor bound nothing, logged a warning and served no requests at all.
     */
    private const int PortFirst = 17890;

    /**
     * The last port tried. Small on purpose: ten editors is more than anyone runs, and a bounded walk
     * fails loudly instead of looping when something unrelated is squatting on all of them.
     */
    private const int PortLast = 17909;

    /** The port this instance actually bound, or 0. Every message uses this rather than the constant. */
    private static int boundPort = 0;

    /** Where this instance publishes its port, so the host can find it without guessing. */
    private static string publishedPath = null;

    /// <summary>
    /// Bumped whenever an endpoint is added or its payload changes shape.
    ///
    /// The host checks this. Without it, an older copy of this file answers 404 on
    /// a newer endpoint and every tool reports "bridge unreachable" — which reads
    /// exactly like Unity not running, and sends the reader to the wrong problem.
    ///
    /// IT MUST BE BUMPED IN BOTH HALVES. This constant and `BRIDGE_VERSION` in `index.js` are two
    /// numbers describing one protocol, and they are not generated from each other. Version 3 added
    /// `/quit`; the JavaScript half was raised to 3 while this stayed at 2, so the diagnostic reported
    /// "bridge out of date in 6 project(s)" for every project even though the bridge files were current
    /// — a mismatch that reads like a stale install and was actually a forgotten edit.
    /// </summary>
    private const int BridgeVersion = 3;
    private const double RefreshIntervalSeconds = 1.0;

    private static HttpListener listener;
    private static Thread worker;
    private static volatile bool running;

    // Written only on the main thread, read only on the listener thread.
    private static volatile string cachedStatus = "{\"ok\":false,\"detail\":\"not sampled yet\"}";
    private static double lastSample = -1.0;
    /** Wall-clock ticks when `cachedStatus` was last written, so its staleness can be read from the
     *  listener thread. `lastSample` is on the editor's clock and stops with it.
     *
     *  NOT `volatile`. C# refuses `volatile long` — CS0677, "a volatile field cannot be of the type
     *  long" — and that single error stopped the whole bridge compiling, which meant Unity kept running
     *  the PREVIOUS assembly while every later fix sat unread on disk. `Interlocked` gives the same
     *  guarantee on a type the language will take. */
    private static long statusAtTicks = 0;

    private static readonly HashSet<string> Allowed = new HashSet<string>
    {
        "play", "pause", "stop", "refresh", "step"
    };

    // ------------------------------------------------------------------ console
    // Unity raises log callbacks on whatever thread logged, so the buffer is
    // guarded. Entries are numbered so the client can ask for "everything since
    // N" instead of re-downloading the whole console every second.
    private const int LogCapacity = 600;

    private sealed class LogEntry
    {
        public long Seq;
        public string Type;
        public string Message;
        public string Stack;
        public long At;
    }

    private static readonly object logLock = new object();
    private static readonly List<LogEntry> logEntries = new List<LogEntry>();
    private static long logSeq = 0;
    private static long logDropped = 0;

    // -------------------------------------------------------------------- stats
    // A frame rate can only be measured from this loop: `update` fires once per
    // editor frame, so counting calls per second is the editor's own frame rate.
    private static int framesInWindow = 0;
    private static double fpsWindowStart = -1.0;
    private static int framesPerSecond = 0;

    static YoweiBridge()
    {
        // AN ASSET-IMPORT WORKER MUST NEVER OWN THIS PORT.
        //
        // NOT MY FINDING. This came from a copy of this file found in AIGame's Assets/Editor, newer and
        // larger than this one, and it explains a failure this file had without knowing it.
        //
        // Unity spawns `Unity.exe -batchMode -name AssetImportWorker0` as a CHILD of the editor, and that
        // worker loads this assembly, so this constructor runs there too. It binds 17890 first, and then
        // never runs an editor update pass — which makes every main-thread endpoint (/status, /scene,
        // /model, /test) stall forever while /log keeps answering, because /log only reads a buffer. The
        // editor's own attempt to bind then fails with "address already in use" and is swallowed by the
        // catch below, so the bridge looks alive and is permanently useless.
        //
        // Measured on that project: pid 19088 (AssetImportWorker0, child of the editor) held 17890 while
        // the editor's own listener reported IsListening=false and statusAtTicks=0 — the status cache had
        // never been written once.
        //
        // This is the shape of "sometimes it just is not connected": it is not a race and not a slow
        // machine, it is a second process quietly taking the port and never serving it. A batch-mode
        // editor has no use for a bridge at all, so it declines to start one.
        if (Application.isBatchMode) return;

        // THE WALK. Each port is tried in turn; the first that binds is this instance's.
        Exception lastError = null;
        for (int candidate = PortFirst; candidate <= PortLast && boundPort == 0; candidate++)
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://127.0.0.1:" + candidate + "/");
                listener.Start();
                boundPort = candidate;
            }
            catch (Exception error)
            {
                lastError = error;
                try
                {
                    if (listener != null) listener.Close();
                }
                catch
                {
                    // already unusable
                }
                listener = null;
            }
        }

        if (boundPort != 0)
        {
            running = true;
            worker = new Thread(Loop) { IsBackground = true, Name = "YoweiBridge" };
            worker.Start();
            PublishPort();
            Debug.Log("[YoweiBridge] listening on http://127.0.0.1:" + boundPort + "/");
        }
        else
        {
            // LogError, not LogWarning: without a port this bridge answers nothing that needs the main
            // thread, and a warning is filtered out of exactly the view someone opens when they are trying
            // to find out why a tool is stalling.
            Debug.LogError(
                "[YoweiBridge] could not bind any port in "
                + PortFirst + "-" + PortLast + ": "
                + (lastError == null ? "no port was free" : lastError.Message));
            running = false;
        }

        // Sample on the main thread, then keep it fresh from there.
        Application.logMessageReceivedThreaded += OnLog;
        EditorApplication.update += Tick;
        // AND THE MAIN-THREAD QUEUE. Registered here, once, from the main thread, so the subscription
        // cannot be lost the way a `delayCall` assignment posted from the listener thread could be.
        EditorApplication.update += DrainMainThread;
        EditorApplication.quitting += Stop;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
    }

    /** Runs on the MAIN thread, which is the only place these APIs are legal. */
    private static void Tick()
    {
        if (!running) return;

        // Counted on every update, before the throttle below: throttling first
        // would measure the throttle instead of the frame rate.
        double now = EditorApplication.timeSinceStartup;
        framesInWindow += 1;
        if (fpsWindowStart < 0.0) fpsWindowStart = now;
        double span = now - fpsWindowStart;
        if (span >= 1.0)
        {
            framesPerSecond = (int)Math.Round(framesInWindow / span);
            framesInWindow = 0;
            fpsWindowStart = now;
        }

        if (lastSample >= 0 && now - lastSample < RefreshIntervalSeconds) return;
        lastSample = now;
        cachedStatus = BuildStatus();
        System.Threading.Interlocked.Exchange(ref statusAtTicks, DateTime.UtcNow.Ticks);
    }

    private static void OnLog(string message, string stackTrace, LogType type)
    {
        var entry = new LogEntry
        {
            Type = type.ToString(),
            Message = message ?? string.Empty,
            Stack = stackTrace ?? string.Empty,
            // Wall clock, not EditorApplication.timeSinceStartup: this runs on
            // the logging thread, which is not the one allowed to read editor state.
            At = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds,
        };
        lock (logLock)
        {
            logSeq += 1;
            entry.Seq = logSeq;
            logEntries.Add(entry);
            while (logEntries.Count > LogCapacity)
            {
                logEntries.RemoveAt(0);
                logDropped += 1;
            }
        }
    }

    /** Serialise the entries newer than `since`. */
    private static string BuildLog(long since)
    {
        var builder = new StringBuilder();
        var copy = new List<LogEntry>();
        long latest;
        long dropped;
        lock (logLock)
        {
            latest = logSeq;
            dropped = logDropped;
            for (int i = 0; i < logEntries.Count; i++)
            {
                if (logEntries[i].Seq > since) copy.Add(logEntries[i]);
            }
        }

        builder.Append("{\"ok\":true");
        builder.Append(",\"seq\":").Append(latest);
        builder.Append(",\"dropped\":").Append(dropped);
        builder.Append(",\"entries\":[");
        for (int i = 0; i < copy.Count; i++)
        {
            if (i > 0) builder.Append(',');
            LogEntry entry = copy[i];
            builder.Append("{\"seq\":").Append(entry.Seq);
            builder.Append(",\"type\":\"").Append(Escape(entry.Type)).Append('"');
            builder.Append(",\"at\":").Append(entry.At);
            builder.Append(",\"message\":\"").Append(EscapeMultiline(entry.Message)).Append('"');
            builder.Append(",\"stack\":\"").Append(EscapeMultiline(entry.Stack)).Append('"');
            builder.Append('}');
        }
        builder.Append("]}");
        return builder.ToString();
    }

    /**
     * Escape for JSON while KEEPING line breaks as escapes.
     *
     * The shared Escape() turns newlines into spaces, which is right for a
     * one-line status field and wrong here: a stack trace flattened to one line
     * is unreadable, and the client parses the JSON, so \n arrives as a real
     * newline and the layout survives.
     */
    private static string EscapeMultiline(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    private static string BuildStatus()
    {
        var builder = new StringBuilder();
        try
        {
            builder.Append("{\"ok\":true");
            builder.Append(",\"version\":").Append(BridgeVersion);
            builder.Append(",\"unity\":\"").Append(Escape(Application.unityVersion)).Append('"');
            builder.Append(",\"project\":\"").Append(Escape(Application.dataPath)).Append('"');
            builder.Append(",\"playing\":").Append(EditorApplication.isPlaying ? "true" : "false");
            builder.Append(",\"paused\":").Append(EditorApplication.isPaused ? "true" : "false");
            builder.Append(",\"compiling\":").Append(EditorApplication.isCompiling ? "true" : "false");
            builder.Append(",\"updating\":").Append(EditorApplication.isUpdating ? "true" : "false");
            builder.Append(",\"scene\":\"").Append(Escape(SceneManager.GetActiveScene().name)).Append('"');
            builder.Append(",\"fps\":").Append(framesPerSecond);
            builder.Append(",\"gcBytes\":").Append(GC.GetTotalMemory(false));
            AppendUnityStats(builder);
            builder.Append('}');
        }
        catch (Exception error)
        {
            return "{\"ok\":false,\"detail\":\"" + Escape(error.Message) + "\"}";
        }
        return builder.ToString();
    }

    /**
     * Renderer counters from the editor's last drawn frame.
     *
     * UnityStats throws when nothing has rendered yet, and each property is
     * guarded separately so a missing counter leaves its field out rather than
     * failing the whole status. The client shows those as unknown instead of
     * inventing a zero.
     */
    /**
     * Renderer counters from the editor's last drawn frame.
     *
     * The members are looked up by NAME at runtime rather than referenced
     * directly. Unity 6000.4.6f1 has no `UnityStats.batches` and no
     * `UnityStats.renderers`, and a direct reference to a member that does not
     * exist is a COMPILE error — CS0117 — which no try/catch can contain, and
     * which breaks every project the bridge is installed into. The names below
     * therefore span several Unity versions on purpose: whichever ones this build
     * actually has are reported, the rest are skipped.
     */
    private static void AppendUnityStats(StringBuilder builder)
    {
        AppendStat(builder, "drawCalls", "drawCalls");
        AppendStat(builder, "batches", "batches");
        AppendStat(builder, "staticBatches", "staticBatches");
        AppendStat(builder, "dynamicBatches", "dynamicBatches");
        AppendStat(builder, "triangles", "triangles");
        AppendStat(builder, "vertices", "vertices");
        AppendStat(builder, "setPassCalls", "setPassCalls");
        AppendStat(builder, "renderers", "renderers");
        AppendStat(builder, "screenBytes", "screenBytes");
    }

    private static void AppendStat(StringBuilder builder, string field, string member)
    {
        try
        {
            var property = typeof(UnityStats).GetProperty(
                member,
                BindingFlags.Public | BindingFlags.Static);
            if (property == null) return;
            object value = property.GetValue(null, null);
            if (value == null) return;
            builder.Append(",\"").Append(field).Append("\":").Append(Convert.ToInt64(value));
        }
        catch
        {
            // No renderer has drawn yet, or the counter is not an integer on this
            // build. Either way the field is left out and the client shows it as
            // unknown rather than as a fabricated zero.
        }
    }

    // ------------------------------------------------------------- scene mode --
    /**
     * Create or open a scene, and put objects in it.
     *
     * Runs through RunOnMainThread from the listener, so a caller gets an answer
     * about what happened rather than a fire-and-forget acknowledgement. The
     * operations that mutate the project return what they did, because "created a
     * cube" is only useful if it says where.
     */
    private static string RunScene(string action, string name, string path, string kind, string script, float x, float y, float z, string parentName)
    {
        try
        {
            switch (action)
            {
                case "new":
                {
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                        UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                        UnityEditor.SceneManagement.NewSceneMode.Single);
                    if (!string.IsNullOrEmpty(path))
                    {
                        string wanted = NormalizeScenePath(path);
                        if (wanted == null) return "{\"ok\":false,\"error\":\"scene path must be inside Assets and end with .unity\"}";
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(wanted));
                        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, wanted);
                        AssetDatabase.Refresh();
                        return "{\"ok\":true,\"action\":\"new\",\"path\":\"" + Escape(Rel(wanted)) + "\"}";
                    }
                    return "{\"ok\":true,\"action\":\"new\",\"path\":\"\",\"unsaved\":true}";
                }

                case "open":
                {
                    if (string.IsNullOrEmpty(path)) return "{\"ok\":false,\"error\":\"path is required\"}";
                    string full = Absolute(path);
                    if (!System.IO.File.Exists(full)) return "{\"ok\":false,\"error\":\"no scene at " + Escape(path) + "\"}";
                    var opened = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(full, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    return "{\"ok\":true,\"action\":\"open\",\"path\":\"" + Escape(Rel(full)) + "\",\"name\":\"" + Escape(opened.name) + "\"}";
                }

                case "save":
                {
                    var current = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                    if (string.IsNullOrEmpty(current.path) && string.IsNullOrEmpty(path))
                    {
                        return "{\"ok\":false,\"error\":\"this scene has never been saved; pass a path\"}";
                    }
                    string target = !string.IsNullOrEmpty(path) ? NormalizeScenePath(path) : current.path;
                    if (target == null) return "{\"ok\":false,\"error\":\"scene path must be inside Assets and end with .unity\"}";
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(current, target);
                    AssetDatabase.Refresh();
                    return "{\"ok\":true,\"action\":\"save\",\"path\":\"" + Escape(Rel(target)) + "\"}";
                }

                case "list":
                {
                    var guids = AssetDatabase.FindAssets("t:Scene");
                    var builder = new StringBuilder();
                    builder.Append("{\"ok\":true,\"action\":\"list\",\"scenes\":[");
                    for (int i = 0; i < guids.Length && i < 400; i++)
                    {
                        string scenePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                        if (i > 0) builder.Append(',');
                        builder.Append("{\"path\":\"").Append(Escape(Rel(Absolute(scenePath)))).Append("\",\"name\":\"")
                               .Append(Escape(System.IO.Path.GetFileNameWithoutExtension(scenePath))).Append("\"}");
                    }
                    builder.Append("],\"count\":").Append(guids.Length).Append('}');
                    return builder.ToString();
                }

                case "create":
                {
                    var created = CreateObject(kind, name, script, x, y, z, parentName);
                    if (created == null) return "{\"ok\":false,\"error\":\"unknown kind: " + Escape(kind) + "\"}";
                    var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
                    // Marked dirty so the object survives being saved later, rather
                    // than living only in memory until something else saves.
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(active);
                    return "{\"ok\":true,\"action\":\"create\",\"name\":\"" + Escape(created.name) + "\",\"kind\":\""
                         + Escape(kind) + "\",\"scene\":\"" + Escape(active.name) + "\",\"objects\":" + CountObjects() + "}";
                }

                default:
                    return "{\"ok\":false,\"error\":\"unknown action: " + Escape(action) + "\"}";
            }
        }
        catch (Exception error)
        {
            return "{\"ok\":false,\"error\":\"" + Escape(error.Message) + "\"}";
        }
    }

    private static string NormalizeScenePath(string path)
    {
        string full = Absolute(path);
        string assets = Application.dataPath;
        if (!full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)) return null;
        if (!full.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return null;
        return full;
    }

    private static string Absolute(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        string cleaned = path.Replace('/', System.IO.Path.DirectorySeparatorChar);
        if (System.IO.Path.IsPathRooted(cleaned)) return cleaned;
        // Scripts and docs are addressed relative to Assets, which is how the rest
        // of this plugin talks about project paths.
        string withoutAssets = cleaned.StartsWith("Assets" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? cleaned.Substring(("Assets" + System.IO.Path.DirectorySeparatorChar).Length)
            : cleaned;
        return System.IO.Path.Combine(Application.dataPath, withoutAssets);
    }

    private static string Rel(string full)
    {
        if (string.IsNullOrEmpty(full)) return string.Empty;
        string assets = Application.dataPath;
        string normalized = full.Replace(System.IO.Path.DirectorySeparatorChar, '/');
        string prefix = assets.Replace(System.IO.Path.DirectorySeparatorChar, '/');
        return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? "Assets" + normalized.Substring(prefix.Length)
            : normalized;
    }

    private static GameObject CreateObject(string kind, string name, string script, float x, float y, float z, string parentName)
    {
        GameObject go;
        string label = string.IsNullOrEmpty(name) ? null : name;
        switch (kind)
        {
            case "cube": go = GameObject.CreatePrimitive(PrimitiveType.Cube); break;
            case "sphere": go = GameObject.CreatePrimitive(PrimitiveType.Sphere); break;
            case "capsule": go = GameObject.CreatePrimitive(PrimitiveType.Capsule); break;
            case "cylinder": go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); break;
            case "plane": go = GameObject.CreatePrimitive(PrimitiveType.Plane); break;
            case "quad": go = GameObject.CreatePrimitive(PrimitiveType.Quad); break;
            case "camera":
            {
                go = new GameObject(label ?? "Main Camera");
                go.AddComponent<Camera>();
                go.tag = "MainCamera";
                break;
            }
            case "light":
            {
                go = new GameObject(label ?? "Directional Light");
                var light = go.AddComponent<Light>();
                light.type = LightType.Directional;
                go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                break;
            }
            case "empty": go = new GameObject(label ?? "GameObject"); break;
            default: return null;
        }

        if (label != null) go.name = label;
        go.transform.position = new Vector3(x, y, z);

        if (!string.IsNullOrEmpty(parentName))
        {
            var parent = GameObject.Find(parentName);
            if (parent != null) go.transform.SetParent(parent.transform, true);
        }

        if (!string.IsNullOrEmpty(script))
        {
            var type = FindComponentType(script);
            if (type != null)
            {
                try { go.AddComponent(type); }
                catch (Exception error) { Debug.LogWarning("[YoweiBridge] could not add " + script + ": " + error.Message); }
            }
            else
            {
                Debug.LogWarning("[YoweiBridge] no MonoBehaviour named " + script + " was found; the object was created without it");
            }
        }

        return go;
    }

    /**
     * Resolve a component by simple name.
     *
     * Searched by reflection rather than through a static reference, because the
     * bridge is compiled into the Editor assembly and cannot name a type that the
     * user's scripts define.
     */
    private static Type FindComponentType(string simpleName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try { types = assemblies[i].GetTypes(); }
            catch { continue; }
            for (int j = 0; j < types.Length; j++)
            {
                var candidate = types[j];
                if (candidate == null || candidate.IsAbstract) continue;
                if (candidate.Name != simpleName) continue;
                if (!typeof(Component).IsAssignableFrom(candidate)) continue;
                return candidate;
            }
        }
        return null;
    }

    private static int CountObjects()
    {
        try
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            return scene.IsValid() ? scene.GetRootGameObjects().Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    // ------------------------------------------------------------- playtest ----
    /**
     * Run the current scene and report what it threw.
     *
     * A compile check proves the code is valid; only running it proves the game
     * works. This enters play mode, waits on the LISTENER thread (never the main
     * one, which has to keep rendering), then stops and counts the errors that
     * appeared in the window.
     */
    private static string RunPlaytest(int seconds, bool sampleFrames)
    {
        long cursor;
        lock (logLock) { cursor = logSeq; }

        RunOnMainThread(() =>
        {
            try { EditorApplication.isPlaying = true; }
            catch (Exception error) { Debug.LogWarning("[YoweiBridge] could not enter play mode: " + error.Message); }
        });

        // Let the domain reload and the first frames happen before timing.
        System.Threading.Thread.Sleep(2500);
        bool playing = false;
        RunOnMainThread(() => { playing = EditorApplication.isPlaying; });

        int frameStart = 0;
        if (sampleFrames) RunOnMainThread(() => { frameStart = Time.frameCount; });

        System.Threading.Thread.Sleep(Math.Max(1, Math.Min(seconds, 120)) * 1000);

        int frameEnd = 0;
        if (sampleFrames) RunOnMainThread(() => { frameEnd = Time.frameCount; });

        RunOnMainThread(() =>
        {
            try { EditorApplication.isPlaying = false; }
            catch (Exception error) { Debug.LogWarning("[YoweiBridge] could not leave play mode: " + error.Message); }
        });
        System.Threading.Thread.Sleep(1200);

        var copy = new List<LogEntry>();
        lock (logLock)
        {
            for (int i = 0; i < logEntries.Count; i++)
            {
                if (logEntries[i].Seq > cursor) copy.Add(logEntries[i]);
            }
        }

        int errors = 0;
        int warnings = 0;
        var builder = new StringBuilder();
        builder.Append("{\"ok\":true");
        builder.Append(",\"enteredPlayMode\":").Append(playing ? "true" : "false");
        builder.Append(",\"seconds\":").Append(seconds);
        if (sampleFrames && frameEnd >= frameStart)
        {
            builder.Append(",\"frames\":").Append(frameEnd - frameStart);
            builder.Append(",\"fps\":").Append(seconds > 0 ? (frameEnd - frameStart) / seconds : 0);
        }
        builder.Append(",\"entries\":[");
        bool first = true;
        for (int i = 0; i < copy.Count; i++)
        {
            var entry = copy[i];
            bool isError = entry.Type == "Error" || entry.Type == "Exception" || entry.Type == "Assert";
            bool isWarning = entry.Type == "Warning";
            if (isError) errors += 1;
            if (isWarning) warnings += 1;
            // Ordinary Log lines are dropped: a running game prints plenty of them
            // and they would bury the two lines that matter.
            if (!isError && !isWarning) continue;
            if (!first) builder.Append(',');
            // THIS LINE WAS REMOVED BY A BLANKET EDIT AND IS NOT OPTIONAL. The flag is the comma
            // separator; without it `!first` stays false for every entry and the array is emitted with no
            // commas at all — `[{...}{...}]`, which parses nowhere. Removed while deleting a DIFFERENT
            // `first`, in another method, by a search-and-replace that did not stop at the method boundary.
            first = false;
            builder.Append("{\"type\":\"").Append(Escape(entry.Type)).Append('"');
            builder.Append(",\"message\":\"").Append(EscapeMultiline(entry.Message)).Append('"');
            builder.Append(",\"stack\":\"").Append(EscapeMultiline(entry.Stack)).Append('"');
            builder.Append('}');
        }
        builder.Append(']');
        builder.Append(",\"errors\":").Append(errors);
        builder.Append(",\"warnings\":").Append(warnings);
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// Render the game camera into a JPEG and report where it went.
    ///
    /// The camera is rendered into a temporary RenderTexture and read back, rather than asking
    /// the editor to screenshot a window: a window screenshot needs the editor focused and
    /// composited, and this needs neither. `Camera.main` is the camera the game itself uses, so
    /// what comes back is the view a player would have.
    ///
    /// Falls back to any enabled camera when there is no `MainCamera` tag, and says so, because
    /// an empty scene with one camera is a normal state to be in and shouldn't look like failure.
    /// </summary>
    private static string RunCapture(int width, int height, int quality)
    {
        int w = width > 0 ? Mathf.Clamp(width, 64, 1920) : 640;
        int h = height > 0 ? Mathf.Clamp(height, 64, 1080) : 360;
        int q = quality > 0 ? Mathf.Clamp(quality, 20, 100) : 75;

        Camera camera = Camera.main;
        bool named = camera != null;
        if (camera == null)
        {
            Camera[] all = UnityEngine.Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].enabled && all[i].gameObject.activeInHierarchy)
                {
                    camera = all[i];
                    break;
                }
            }
        }
        if (camera == null)
        {
            return "{\"ok\":false,\"error\":\"no camera in the open scene\"}";
        }

        RenderTexture target = null;
        Texture2D readback = null;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            target = RenderTexture.GetTemporary(w, h, 24);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;

            RenderTexture.active = target;
            readback = new Texture2D(w, h, TextureFormat.RGB24, false);
            readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readback.Apply();

            byte[] jpeg = readback.EncodeToJPG(q);
            string directory = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "Library");
            if (!System.IO.Directory.Exists(directory)) System.IO.Directory.CreateDirectory(directory);
            string file = System.IO.Path.Combine(directory, "yowei-capture.jpg");
            System.IO.File.WriteAllBytes(file, jpeg);

            long stamp = System.DateTime.UtcNow.Ticks;
            // THE QUOTES ARE THE CALLER'S JOB. `Escape` escapes a value and does not wrap it, and these
            // two fields were written without the quotes — so the route answered
            //   {"ok":true,"file":D:\Unity\...\yowei-capture.jpg,...
            // which parses nowhere. That is why `/capture` could sit in this file, finished, and never be
            // called: it had never worked, and nothing had a reason to find out.
            return "{\"ok\":true,\"file\":\"" + Escape(file) + "\",\"bytes\":" + jpeg.Length
                + ",\"width\":" + w + ",\"height\":" + h
                + ",\"camera\":\"" + Escape(camera.name) + "\""
                + ",\"mainCamera\":" + (named ? "true" : "false")
                + ",\"playing\":" + (EditorApplication.isPlaying ? "true" : "false")
                + ",\"stamp\":" + stamp + "}";
        }
        catch (Exception error)
        {
            return "{\"ok\":false,\"error\":\"" + Escape(error.Message) + "\"}";
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
        }
    }

    /// <summary>
    /// Find an object by name or by path, INCLUDING one that is switched off.
    ///
    /// `GameObject.Find` skips inactive objects. That is the wrong half of the answer for a test: "the
    /// health bar should be hidden" is an assertion about an object that is deliberately inactive, and a
    /// finder that cannot see it reports "not found" — which reads as a missing object rather than as the
    /// state the test was checking.
    ///
    /// So the scene's roots are walked directly, which sees everything, and `GameObject.Find` is only the
    /// fast path for the common case of an active object.
    /// </summary>
    private static GameObject FindAny(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        GameObject direct = GameObject.Find(path);
        if (direct != null) return direct;

        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null) continue;
            if (roots[i].name == path) return roots[i];
            Transform found = WalkFor(roots[i].transform, path, "");
            if (found != null) return found.gameObject;
        }
        return null;
    }

    /// <summary>Depth-first search for a child whose relative path matches, inactive ones included.</summary>
    private static Transform WalkFor(Transform node, string wanted, string prefix)
    {
        if (node == null) return null;
        string here = prefix.Length == 0 ? node.name : prefix + "/" + node.name;
        if (here == wanted) return node;
        for (int i = 0; i < node.childCount; i++)
        {
            Transform hit = WalkFor(node.GetChild(i), wanted, here);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>A number as JSON, without the trailing zeros a default ToString would add.</summary>
    private static string Num(float value)
    {
        return value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What an object is doing right now: where it is, whether it is on, and what is on it.
    ///
    /// THE POINT IS THE RUNTIME. Everything else this bridge reports is about the project as it sits on
    /// disk; this is the only route that answers a question about the game WHILE IT IS RUNNING. A player
    /// who has fallen through the floor is at y = -400, and no amount of reading the scene file says so.
    ///
    /// In play mode the values are the live ones. Out of it they are the authored ones, and the answer
    /// says which, because "the player is at 0,1,0" means something different in each case.
    /// </summary>
    private static string RunProbe(string path)
    {
        GameObject target = FindAny(path);
        if (target == null)
        {
            return "{\"ok\":false,\"error\":\"" + Escape("no object named or pathed " + path + " in the open scene") + "\"}";
        }

        Transform t = target.transform;
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        b.Append("{\"ok\":true");
        b.Append(",\"name\":\"").Append(Escape(target.name)).Append('"');
        b.Append(",\"scene\":\"").Append(Escape(target.scene.IsValid() ? target.scene.name : "")).Append('"');
        b.Append(",\"playing\":").Append(EditorApplication.isPlaying ? "true" : "false");
        b.Append(",\"activeSelf\":").Append(target.activeSelf ? "true" : "false");
        b.Append(",\"activeInHierarchy\":").Append(target.activeInHierarchy ? "true" : "false");
        b.Append(",\"tag\":\"").Append(Escape(target.tag)).Append('"');
        b.Append(",\"layer\":").Append(target.layer);
        b.Append(",\"childCount\":").Append(t.childCount);
        b.Append(",\"path\":\"").Append(Escape(PathOf(t))).Append('"');

        b.Append(",\"position\":{\"x\":").Append(Num(t.position.x)).Append(",\"y\":").Append(Num(t.position.y)).Append(",\"z\":").Append(Num(t.position.z)).Append('}');
        b.Append(",\"localPosition\":{\"x\":").Append(Num(t.localPosition.x)).Append(",\"y\":").Append(Num(t.localPosition.y)).Append(",\"z\":").Append(Num(t.localPosition.z)).Append('}');
        b.Append(",\"rotation\":{\"x\":").Append(Num(t.eulerAngles.x)).Append(",\"y\":").Append(Num(t.eulerAngles.y)).Append(",\"z\":").Append(Num(t.eulerAngles.z)).Append('}');
        b.Append(",\"scale\":{\"x\":").Append(Num(t.localScale.x)).Append(",\"y\":").Append(Num(t.localScale.y)).Append(",\"z\":").Append(Num(t.localScale.z)).Append('}');

        // The components, by type name, with the fields a test is likely to ask about. Not every field of
        // every component: that would be an unreadable answer and would mostly be noise no assertion uses.
        b.Append(",\"components\":[");
        Component[] parts = target.GetComponents<Component>();
        bool firstPart = true;
        for (int i = 0; i < parts.Length; i++)
        {
            Component part = parts[i];
            if (part == null)
            {
                // A missing script. Worth saying, because it is a real fault and otherwise invisible.
                if (!firstPart) b.Append(',');
                firstPart = false;
                b.Append("{\"type\":\"(missing script)\"}");
                continue;
            }
            if (!firstPart) b.Append(',');
            firstPart = false;
            string type = part.GetType().Name;
            b.Append("{\"type\":\"").Append(Escape(type)).Append('"');

            Rigidbody body = part as Rigidbody;
            if (body != null)
            {
                b.Append(",\"velocity\":{\"x\":").Append(Num(body.linearVelocity.x)).Append(",\"y\":").Append(Num(body.linearVelocity.y)).Append(",\"z\":").Append(Num(body.linearVelocity.z)).Append('}');
                b.Append(",\"mass\":").Append(Num(body.mass));
                b.Append(",\"isKinematic\":").Append(body.isKinematic ? "true" : "false");
                b.Append(",\"useGravity\":").Append(body.useGravity ? "true" : "false");
            }
            Collider collider = part as Collider;
            if (collider != null)
            {
                b.Append(",\"isTrigger\":").Append(collider.isTrigger ? "true" : "false");
                b.Append(",\"enabled\":").Append(collider.enabled ? "true" : "false");
                b.Append(",\"bounds\":{\"x\":").Append(Num(collider.bounds.size.x)).Append(",\"y\":").Append(Num(collider.bounds.size.y)).Append(",\"z\":").Append(Num(collider.bounds.size.z)).Append('}');
            }
            // NO `UnityEngine.UI` HERE, ON PURPOSE.
            //
            // Reading a `Text` component's string was the one thing this would have offered, and it is not
            // worth the dependency: `com.unity.ugui` is present in every project on this machine and is not
            // guaranteed anywhere. A bridge that fails to COMPILE in a project without it cannot report
            // anything at all — not the console, not the scene, not the frame — which is a far worse
            // outcome than not being able to read one label. Numeric fields of any component are still
            // reachable through `component`, by reflection, with no package named.
            b.Append('}');
        }
        b.Append(']');
        b.Append('}');
        return b.ToString();
    }

    /// <summary>The object's path from its root, for a message that has to identify one object among many.</summary>
    private static string PathOf(Transform t)
    {
        string path = t.name;
        Transform walk = t.parent;
        while (walk != null)
        {
            path = walk.name + "/" + path;
            walk = walk.parent;
        }
        return path;
    }

    /// <summary>
    /// A public method by name, chosen rather than thrown over.
    ///
    /// WHY NOT `GetMethod(name, flags)`. On an overloaded name it throws `AmbiguousMatchException`, and
    /// `CompareTag` — the most ordinary method there is on a Transform or a GameObject — has two overloads
    /// in Unity 6. That exception escaped `RunInput`, so the queued result was never set, the route waited
    /// its full ten seconds and reported "the editor is busy — importing, compiling or entering play mode".
    /// A reflection detail surfaced as a fault in the editor, sending the reader to restart Unity.
    ///
    /// So the candidates are listed and one is picked: no parameters if there is such an overload, else a
    /// single one. That is the same set this can call, so the choice and the capability agree.
    /// </summary>
    private static System.Reflection.MethodInfo FindMethod(System.Type type, string name)
    {
        if (type == null || string.IsNullOrEmpty(name)) return null;
        System.Reflection.MethodInfo[] all;
        try
        {
            all = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        }
        catch (System.Exception)
        {
            return null;
        }
        System.Reflection.MethodInfo single = null;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].Name != name) continue;
            if (all[i].IsAbstract || all[i].ContainsGenericParameters) continue;
            int count = all[i].GetParameters().Length;
            if (count == 0) return all[i];
            if (count == 1 && single == null) single = all[i];
        }
        return single;
    }

    /// <summary>
    /// Do something to the game: click a UI element, call a method, or switch an object on or off.
    ///
    /// The three actions are ordered by how specific they are. `click` goes through the event system, so
    /// it is a real click and the button's own handler runs. `send` calls a method that already exists,
    /// which works whatever the game uses to read input. `setActive` puts the scene into a state.
    ///
    /// The body is JSON with `action` plus the fields that action needs, read with the same flat `Field`
    /// helper the rest of the bridge uses.
    /// </summary>
    private static string RunInput(string body)
    {
        string action = Field(body, "action").Trim().ToLowerInvariant();
        string target = Field(body, "path");

        if (action == "setactive")
        {
            GameObject go = FindAny(target);
            if (go == null) return "{\"ok\":false,\"error\":\"" + Escape("no object named " + target) + "\"}";
            bool on = Field(body, "value").Trim().ToLowerInvariant() != "false";
            bool before = go.activeSelf;
            go.SetActive(on);
            return "{\"ok\":true,\"action\":\"setActive\",\"path\":\"" + Escape(PathOf(go.transform))
                + "\",\"was\":" + (before ? "true" : "false") + ",\"now\":" + (go.activeSelf ? "true" : "false")
                + ",\"playing\":" + (EditorApplication.isPlaying ? "true" : "false") + "}";
        }

        if (action == "send")
        {
            GameObject go = FindAny(target);
            if (go == null) return "{\"ok\":false,\"error\":\"" + Escape("no object named " + target) + "\"}";
            string typeName = Field(body, "type");
            string methodName = Field(body, "method");
            if (methodName.Length == 0) return "{\"ok\":false,\"error\":\"method is required\"}";

            Component hit = null;
            System.Reflection.MethodInfo method = null;
            Component[] parts = go.GetComponents<Component>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == null) continue;
                System.Type t = parts[i].GetType();
                if (typeName.Length == 0 || t.Name == typeName || t.FullName == typeName)
                {
                    System.Reflection.MethodInfo candidate = FindMethod(t, methodName);
                    if (candidate != null)
                    {
                        hit = parts[i];
                        method = candidate;
                        break;
                    }
                }
            }
            if (hit == null)
            {
                return "{\"ok\":false,\"error\":\"" + Escape(
                    "no component on " + target + (typeName.Length > 0 ? " of type " + typeName : "") +
                    " has a public method called " + methodName) + "\"}";
            }

            // A parameterless method, or one taking a single number or string. Wider than that and the
            // caller should be writing a method of its own that takes no arguments.
            //
            // `method` WAS ALREADY RESOLVED by FindMethod, which exists because the obvious
            // `GetMethod(name, flags)` THROWS on an overloaded name: `AmbiguousMatchException`. It threw
            // for `Transform.CompareTag`, which has two overloads in Unity 6 — the exception escaped this
            // method, so the queue's result stayed null, the route waited out its ten seconds and answered
            // "the editor is busy — importing, compiling or entering play mode". A reflection detail
            // reported as a fault in the editor, pointing the reader at the wrong thing entirely.
            string value = Field(body, "value");
            string outcome;
            try
            {
                System.Reflection.ParameterInfo[] parameters = method.GetParameters();
                object returned;
                if (parameters.Length == 0)
                {
                    returned = method.Invoke(hit, null);
                }
                else if (parameters.Length == 1)
                {
                    System.Type want = parameters[0].ParameterType;
                    object argument;
                    if (want == typeof(string)) argument = value;
                    else if (want == typeof(bool)) argument = value.Trim().ToLowerInvariant() != "false";
                    else if (want == typeof(int)) argument = (int)ParseFloat(value, 0f);
                    else argument = ParseFloat(value, 0f);
                    returned = method.Invoke(hit, new object[] { argument });
                }
                else
                {
                    return "{\"ok\":false,\"error\":\"" + Escape(
                        methodName + " takes " + parameters.Length + " parameters; this can call a method taking none or one") + "\"}";
                }
                outcome = returned == null ? "void" : returned.ToString();
            }
            catch (System.Exception error)
            {
                // The method threw. That is a real answer — a game method raising is exactly the kind of
                // fault a test is looking for — so it is reported as an outcome rather than as a failure
                // of the bridge.
                System.Exception inner = error.InnerException != null ? error.InnerException : error;
                return "{\"ok\":true,\"action\":\"send\",\"path\":\"" + Escape(PathOf(go.transform))
                    + "\",\"type\":\"" + Escape(hit.GetType().Name) + "\",\"method\":\"" + Escape(methodName)
                    + "\",\"threw\":\"" + Escape(inner.GetType().Name + ": " + inner.Message) + "\"}";
            }
            return "{\"ok\":true,\"action\":\"send\",\"path\":\"" + Escape(PathOf(go.transform))
                + "\",\"type\":\"" + Escape(hit.GetType().Name) + "\",\"method\":\"" + Escape(methodName)
                + "\",\"returned\":\"" + Escape(outcome) + "\"}";
        }

        if (action == "click")
        {
            GameObject go = FindAny(target);
            if (go == null) return "{\"ok\":false,\"error\":\"" + Escape("no object named " + target) + "\"}";

            // FOUND BY REFLECTION, so this file compiles in a project without com.unity.ugui and simply
            // reports that clicking is unavailable there.
            System.Type eventSystemType = null;
            System.Type pointerType = null;
            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                if (eventSystemType == null) eventSystemType = assembly.GetType("UnityEngine.EventSystems.EventSystem");
                if (pointerType == null) pointerType = assembly.GetType("UnityEngine.EventSystems.IPointerClickHandler");
                if (eventSystemType != null && pointerType != null) break;
            }
            if (eventSystemType == null || pointerType == null)
            {
                return "{\"ok\":false,\"error\":\"no EventSystem in this project — clicking needs com.unity.ugui. Use `send` to call a method instead.\"}";
            }

            Component system = UnityEngine.Object.FindObjectOfType(eventSystemType) as Component;
            if (system == null)
            {
                return "{\"ok\":false,\"error\":\"the scene has no EventSystem object, so a click has nowhere to go\"}";
            }

            // The element's screen position, which is what a pointer event carries. A RectTransform knows
            // it; anything else has to be reached through the world camera.
            Vector2 screen;
            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                screen = RectTransformUtility.WorldToScreenPoint(null, rect.position);
            }
            else
            {
                Camera camera = Camera.main;
                if (camera == null) return "{\"ok\":false,\"error\":\"" + Escape(target + " is not a UI element and there is no Camera.main to project it through") + "\"}";
                screen = camera.WorldToScreenPoint(go.transform.position);
            }

            System.Reflection.MethodInfo raycast = eventSystemType.GetMethod("RaycastAll", new System.Type[] { typeof(Vector2), eventSystemType.GetNestedType("PointerEventData") });
            // `RaycastAll` takes a PointerEventData, which has to be constructed; the constructor takes the
            // EventSystem. Built by reflection for the same reason as everything else here.
            System.Type dataType = eventSystemType.GetNestedType("PointerEventData");
            object data = System.Activator.CreateInstance(dataType, new object[] { system });
            dataType.GetProperty("position").SetValue(data, screen, null);

            var hits = raycast.Invoke(system, new object[] { screen, data }) as System.Collections.IList;
            if (hits == null || hits.Count == 0)
            {
                return "{\"ok\":false,\"error\":\"" + Escape("nothing is under " + target + " at that position; nothing to click") + "\"}";
            }

            // Walk the hit stack and click the first handler, which is what the event system would do.
            int clicked = 0;
            string clickedName = "";
            for (int i = 0; i < hits.Count; i += 1)
            {
                object hit = hits[i];
                GameObject hitObject = hit.GetType().GetProperty("gameObject").GetValue(hit, null) as GameObject;
                if (hitObject == null) continue;
                Component[] onIt = hitObject.GetComponents<Component>();
                for (int k = 0; k < onIt.Length; k += 1)
                {
                    if (onIt[k] == null || !pointerType.IsInstanceOfType(onIt[k])) continue;
                    System.Reflection.MethodInfo execute = typeof(UnityEngine.EventSystems.ExecuteEvents).GetMethod("Execute");
                    System.Reflection.MethodInfo generic = execute.MakeGenericMethod(pointerType);
                    generic.Invoke(null, new object[] { hitObject, data, null });
                    clicked += 1;
                    clickedName = PathOf(hitObject.transform);
                    break;
                }
                if (clicked > 0) break;
            }

            if (clicked == 0)
            {
                return "{\"ok\":false,\"error\":\"" + Escape("something is under " + target + " but nothing there handles a click") + "\"}";
            }
            return "{\"ok\":true,\"action\":\"click\",\"clicked\":\"" + Escape(clickedName)
                + "\",\"at\":{\"x\":" + Num(screen.x) + ",\"y\":" + Num(screen.y) + "}"
                + ",\"playing\":" + (EditorApplication.isPlaying ? "true" : "false") + "}";
        }

        return "{\"ok\":false,\"error\":\"unknown action: " + Escape(action) + " — expected click, send or setActive\"}";
    }

    /// <summary>
    /// Run a list of conditions against the running scene and say which held.
    ///
    /// WHY A LITTLE LANGUAGE AND NOT A SCRIPT. The alternative is compiling and running C# the caller
    /// supplies, which turns "check that the player is on the ground" into arbitrary code execution inside
    /// the user's editor. A fixed set of conditions cannot do anything except look, and it cannot be
    /// talked into doing more.
    ///
    /// WHY LINES AND NOT JSON. The bridge parses flat string fields; it has no array parser. One condition
    /// per line needs no parser at all, is readable in a log, and is easy for a caller to write.
    ///
    ///   exists     Player
    ///   active     HUD/Health
    ///   inactive   HUD/GameOver
    ///   y          Player 1.1 0.3          -- position, axis, expected, tolerance
    ///   x          Player 5 0.5
    ///   z          Player 0 0.25
    ///   closer     Player Exit 2           -- two objects nearer than this
    ///   component  Player Rigidbody velocity.y 0 0.1
    ///   children   Player 3
    ///
    /// A line starting with `#` is a comment, so a test can explain itself.
    /// </summary>
    private static string RunAssert(string spec)
    {
        if (string.IsNullOrEmpty(spec))
        {
            return "{\"ok\":false,\"error\":\"no conditions were given\"}";
        }
        string[] rawLines = spec.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        b.Append("{\"ok\":true,\"playing\":").Append(EditorApplication.isPlaying ? "true" : "false");
        b.Append(",\"results\":[");
        int passed = 0;
        int failed = 0;
        // ONE FLAG, NOT TWO. `first` and `firstLine` both tracked "have I written a row yet", and
        // `first` was written and never read — which the compiler warned about, and which is also how
        // the CS0136 below it happened to be possible: a name that should not have been in scope was.
        bool firstLine = true;

        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            if (!firstLine) b.Append(',');
            firstLine = false;

            string[] parts = line.Split(new char[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            string kind = parts[0].ToLowerInvariant();
            string why = null;
            bool held = false;
            string actual = "";

            // Every branch sets `held`, `actual` and `why`. `why` is what a reader needs when it fails:
            // the value that was found, not just the value that was wanted.
            if (kind == "exists" && parts.Length >= 2)
            {
                GameObject go = FindAny(Join(parts, 1, parts.Length - 1));
                // An object that exists but is switched off still EXISTS; that is what `active` is for.
                held = go != null;
                actual = held ? "found" : "not found";
                why = held ? null : "no object named " + Join(parts, 1, parts.Length - 1);
            }
            else if ((kind == "active" || kind == "inactive") && parts.Length >= 2)
            {
                string wanted = Join(parts, 1, parts.Length - 1);
                GameObject go = FindAny(wanted);
                bool on = go != null && go.activeInHierarchy;
                held = kind == "active" ? on : (go != null && !on);
                actual = go == null ? "not found" : (on ? "active" : "inactive");
                why = held ? null : (go == null ? "no object named " + wanted : "it is " + actual);
            }
            else if ((kind == "x" || kind == "y" || kind == "z") && parts.Length >= 3)
            {
                // THE NUMBERS COME OFF THE END, THE NAME IS WHATEVER IS LEFT.
                //
                // Splitting on spaces and taking `parts[1]` as the name breaks on `Main Camera` — the most
                // ordinary object name there is — and reports "no object named Main", which reads as a
                // missing object rather than as a parsing fault. Found by running the conditions against
                // this plugin's own test project, whose camera is called exactly that.
                //
                // A name may contain spaces; a number may not. So the tolerance and the expected value are
                // read from the end, and everything between the condition and them is the name.
                // COUNT BACK FROM THE END, ONE PLACE PER THING.
                //
                //   [tolerance] expected  <- the two numbers, at the end
                //   the name is everything left after the condition word
                //
                // `back` is 1 when a tolerance is present and 0 when it is not, and every offset is
                // measured from it rather than from a single running total. The first version used a
                // `taken` counter and got the expected value one place too far in, which produced
                // "wanted NaN +/- 0.01, found 2.65" — a message that reports the right number and then
                // denies having asked for it.
                bool hasTol = parts.Length >= 4 && !float.IsNaN(ParseFloat(parts[parts.Length - 1], float.NaN));
                int back = hasTol ? 1 : 0;
                float tol = hasTol ? ParseFloat(parts[parts.Length - 1], 0.01f) : 0.01f;
                float want = ParseFloat(parts[parts.Length - 1 - back], float.NaN);
                string named = Join(parts, 1, parts.Length - 2 - back);
                GameObject go = FindAny(named);
                if (go == null)
                {
                    actual = "no object named " + named;
                }
                else
                {
                    Vector3 p = go.transform.position;
                    float got = kind == "x" ? p.x : kind == "y" ? p.y : p.z;
                    actual = kind + "=" + Num(got);
                    held = !float.IsNaN(want) && Mathf.Abs(got - want) <= tol;
                    why = held ? null : "wanted " + Num(want) + " +/- " + Num(tol) + ", found " + Num(got);
                }
            }
            else if (kind == "closer" && parts.Length >= 4)
            {
                // Same shape: the distance comes off the end, the two names are between.
                float want = ParseFloat(parts[parts.Length - 1], float.NaN);
                if (parts.Length == 4)
                {
                    GameObject onlyA = FindAny(parts[1]);
                    GameObject onlyC = FindAny(parts[2]);
                    if (onlyA == null || onlyC == null)
                    {
                        actual = (onlyA == null ? parts[1] : parts[2]) + " not found";
                    }
                    else
                    {
                        float got = Vector3.Distance(onlyA.transform.position, onlyC.transform.position);
                        actual = "distance=" + Num(got);
                        held = !float.IsNaN(want) && got <= want;
                        why = held ? null : "wanted within " + Num(want) + ", found " + Num(got);
                    }
                }
                else
                {
                    // Three or more name tokens: try every split and take the first pair that resolves.
                    //
                    // `firstName` AND `secondName`, NOT `first` AND `second`. The first version used the
                    // short names and did not compile: `first` is already declared in this method as the
                    // "is this the first row" flag, and C# forbids a nested declaration shadowing one from
                    // an enclosing scope — error CS0136. That single name cost more than a compile: the
                    // whole Assembly-CSharp-Editor failed to load in every project the bridge had been
                    // installed into, which takes the MCP bridge and every other editor script with it.
                    bool done = false;
                    for (int cut = 1; cut < parts.Length - 2 && !done; cut += 1)
                    {
                        string firstName = Join(parts, 1, cut);
                        string secondName = Join(parts, 1 + cut, parts.Length - 2 - cut);
                        GameObject a = FindAny(firstName);
                        GameObject c = FindAny(secondName);
                        if (a == null || c == null) continue;
                        float got = Vector3.Distance(a.transform.position, c.transform.position);
                        actual = "distance(" + firstName + ", " + secondName + ")=" + Num(got);
                        held = !float.IsNaN(want) && got <= want;
                        why = held ? null : "wanted within " + Num(want) + ", found " + Num(got);
                        done = true;
                    }
                    if (!done) actual = "could not read two object names out of: " + line;
                }
            }
            else if (kind == "children" && parts.Length >= 3)
            {
                string named = Join(parts, 1, parts.Length - 2);
                int want = (int)ParseFloat(parts[parts.Length - 1], -1);
                GameObject go = FindAny(named);
                if (go == null) actual = "no object named " + named;
                else
                {
                    int got = go.transform.childCount;
                    actual = "children=" + got;
                    held = want >= 0 && got == want;
                    why = held ? null : "wanted " + want + ", found " + got;
                }
            }
            else if (kind == "component" && parts.Length >= 5)
            {
                // component <path> <TypeName> <field> <expected> [tolerance]
                //
                // READ FROM THE END, and count carefully. The first version used a single `taken` offset
                // where two were needed, so `component Main Camera Camera fieldOfView 60 0.01` read
                // `Camera` as the expected value and reported "wanted NaN, found 55" — a message that
                // names a real number and then denies asking for one.
                //
                // From the end: [tolerance] expected, field, TypeName. The name is everything before that.
                // component <path> <TypeName> <field> <expected> [tolerance]
                //
                // COUNT BACK FROM THE END, ONE PLACE PER THING. From the end: [tolerance] expected, field,
                // TypeName — so the name is everything left after the condition word, and its length is
                // `len - 4 - back`.
                //
                // This branch has now been wrong twice, both times by one place, and both times the
                // message it produced was misleading rather than obviously broken: first "wanted NaN,
                // found 55" (the expected value read from the field's position) and then "no object named
                // Main Camera Camera" (the name one token too long). Written out with the arithmetic in
                // one place so the next change to it can be checked by counting.
                bool hasTol = parts.Length >= 6 && !float.IsNaN(ParseFloat(parts[parts.Length - 1], float.NaN));
                int back = hasTol ? 1 : 0;
                float tol = hasTol ? ParseFloat(parts[parts.Length - 1], 0.01f) : 0.01f;
                float want = ParseFloat(parts[parts.Length - 1 - back], float.NaN);
                string fieldName = parts[parts.Length - 2 - back];
                string typeName = parts[parts.Length - 3 - back];
                string named = Join(parts, 1, parts.Length - 4 - back);
                GameObject go = FindAny(named);
                if (go == null) actual = "no object named " + named;
                else
                {
                    Component found = null;
                    Component[] all = go.GetComponents<Component>();
                    for (int k = 0; k < all.Length; k++)
                    {
                        if (all[k] != null && (all[k].GetType().Name == typeName || all[k].GetType().FullName == typeName))
                        {
                            found = all[k];
                            break;
                        }
                    }
                    if (found == null) actual = "no " + typeName + " on " + named;
                    else
                    {
                        float got = ReadNumber(found, fieldName);
                        actual = fieldName + "=" + (float.IsNaN(got) ? "unreadable" : Num(got));
                        held = !float.IsNaN(want) && !float.IsNaN(got) && Mathf.Abs(got - want) <= tol;
                        why = held ? null : (float.IsNaN(got)
                            ? "no readable number at " + typeName + "." + fieldName
                            : "wanted " + Num(want) + " +/- " + Num(tol) + ", found " + Num(got));
                    }
                }
            }
            else
            {
                actual = "unknown condition";
                why = "expected one of: exists, active, inactive, x, y, z, closer, component, children";
            }

            if (held) passed++; else failed++;

            b.Append("{\"line\":").Append(i + 1);
            b.Append(",\"spec\":\"").Append(Escape(line)).Append('"');
            b.Append(",\"pass\":").Append(held ? "true" : "false");
            b.Append(",\"actual\":\"").Append(Escape(actual)).Append('"');
            if (why != null) b.Append(",\"why\":\"").Append(Escape(why)).Append('"');
            b.Append('}');
        }

        b.Append(']');
        b.Append(",\"passed\":").Append(passed);
        b.Append(",\"failed\":").Append(failed);
        b.Append('}');
        return b.ToString();
    }

    /// <summary>Rejoin the tokens of a name that contains spaces, from `start` to the end.</summary>
    private static string Join(string[] parts, int start, int count)
    {
        System.Text.StringBuilder b = new System.Text.StringBuilder();
        for (int i = start; i < start + count && i < parts.Length; i++)
        {
            if (b.Length > 0) b.Append(' ');
            b.Append(parts[i]);
        }
        return b.ToString();
    }

    private static float ParseFloat(string text, float fallback)
    {
        float value;
        if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return value;
        }
        return fallback;
    }

    /// <summary>
    /// Read a number out of a component by field or property name, following dots into sub-objects.
    ///
    /// Reflection, because the alternative is a hard-coded list of every field of every component type,
    /// which would be wrong the moment somebody writes a component of their own. A dot path like
    /// `velocity.y` walks into the property and then into its field, which is how a caller naturally
    /// writes it.
    ///
    /// Returns NaN when there is nothing numeric there, and the caller says "unreadable" rather than
    /// pretending a value was read.
    /// </summary>
    private static float ReadNumber(object target, string path)
    {
        if (target == null || string.IsNullOrEmpty(path)) return float.NaN;
        object current = target;
        string[] steps = path.Split('.');
        for (int i = 0; i < steps.Length; i++)
        {
            if (current == null) return float.NaN;
            System.Type type = current.GetType();
            System.Reflection.FieldInfo field = type.GetField(steps[i], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                current = field.GetValue(current);
                continue;
            }
            System.Reflection.PropertyInfo property = type.GetProperty(steps[i], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (property != null)
            {
                current = property.GetValue(current, null);
                continue;
            }
            return float.NaN;
        }
        if (current is float) return (float)current;
        if (current is int) return (float)(int)current;
        if (current is double) return (float)(double)current;
        if (current is bool) return ((bool)current) ? 1f : 0f;
        return float.NaN;
    }

    /// <summary>
    /// What is inside a model: its bones, its skinned meshes, and the clips the project has.
    ///
    /// An .fbx is a binary container. Nothing outside Unity can say what is in it without
    /// re-implementing a large part of the importer, and the editor has already done that work,
    /// so the answer comes from the imported object.
    ///
    /// The bone list is the SKELETON — `SkinnedMeshRenderer.bones`, the transforms that actually
    /// deform the mesh — and not every transform in the file, because a model can carry empty
    /// nodes and helpers that are not part of the rig.
    /// </summary>
    private static string RunModel(string askPath)
    {
        if (string.IsNullOrEmpty(askPath))
        {
            return "{\"ok\":false,\"error\":\"path is required\"}";
        }
        string assetPath = askPath.Replace('\\', '/');
        if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
        {
            assetPath = "Assets/" + assetPath.TrimStart('/');
        }
        GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (root == null)
        {
            return "{\"ok\":false,\"error\":\"" + Escape("nothing imported at " + assetPath) + "\"}";
        }

        var builder = new StringBuilder();
        builder.Append("{\"ok\":true");
        builder.Append(",\"path\":\"").Append(Escape(assetPath)).Append("\"");
        builder.Append(",\"name\":\"").Append(Escape(root.name)).Append("\"");

        SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Transform[] bones = null;
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] != null && skinned[i].bones != null && skinned[i].bones.Length > 0)
            {
                bones = skinned[i].bones;
                break;
            }
        }

        builder.Append(",\"skinned\":").Append(skinned.Length > 0 ? "true" : "false");
        builder.Append(",\"skinnedMeshes\":").Append(skinned.Length);

        // Depth as well as name, so the window can draw a tree rather than a list. The depth is
        // measured within the bone array's own root, which is what makes a hierarchy readable
        // even when the bones are not children of the prefab root.
        builder.Append(",\"bones\":[");
        int boneCount = bones == null ? 0 : bones.Length;
        var boneIndex = new Dictionary<Transform, int>();
        for (int i = 0; i < boneCount; i++)
        {
            if (bones[i] != null) boneIndex[bones[i]] = i;
        }
        // THE SEPARATOR IS NOT THE INDEX. `continue` below skips a null entry, so keying the comma off
        // `i > 0` puts one after nothing when the first bone is null — and the object was never closed
        // at all, which is why every bone ran into the next:
        //     {"name":Root,"depth":0,"parent":Armature,{"name":Hip,...
        bool firstBone = true;
        for (int i = 0; i < boneCount; i++)
        {
            Transform bone = bones[i];
            if (bone == null) continue;
            int depth = 0;
            Transform walk = bone.parent;
            while (walk != null)
            {
                if (boneIndex.ContainsKey(walk)) depth += 1;
                walk = walk.parent;
                if (depth > 64) break;
            }
            if (!firstBone) builder.Append(',');
            firstBone = false;
            builder.Append("{\"name\":\"").Append(Escape(bone.name)).Append("\"");
            builder.Append(",\"depth\":").Append(depth);
            builder.Append(",\"parent\":\"").Append(Escape(bone.parent != null ? bone.parent.name : "")).Append("\"");
            builder.Append('}');
        }
        builder.Append("]");

        // The vertex count comes from the meshes, which is what tells a placeholder from a real
        // asset at a glance.
        int vertices = 0;
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null && filters[i].sharedMesh != null) vertices += filters[i].sharedMesh.vertexCount;
        }
        for (int i = 0; i < skinned.Length; i++)
        {
            if (skinned[i] != null && skinned[i].sharedMesh != null) vertices += skinned[i].sharedMesh.vertexCount;
        }
        builder.Append(",\"vertices\":").Append(vertices);
        builder.Append(",\"meshes\":").Append(filters.Length + skinned.Length);

        // CLIPS PACKED INSIDE THIS FILE. An .fbx can carry its own animation, and those are read from
        // its sub-assets. The standalone .anim assets in the project are a different question and are
        // answered separately below — the comment that used to sit here claimed to cover both and read
        // only this one, which is why a project with twenty-six animations reported none.
        UnityEngine.Object[] representations = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
        AnimationClip[] clips = representations == null ? new AnimationClip[0] : FilterClips(representations);
        builder.Append(",\"clips\":[");
        for (int i = 0; i < clips.Length; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append("{\"name\":\"").Append(Escape(clips[i].name)).Append("\"");
            builder.Append(",\"seconds\":").Append(clips[i].length.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(",\"frames\":").Append(Mathf.RoundToInt(clips[i].frameRate * clips[i].length));
        }
        builder.Append("]");

        // AND THE ONES THE PROJECT HAS, which is a different question from the ones the file carries.
        int scanned;
        bool capped;
        List<AnimationClip> projectClips = FindProjectClips(root, out scanned, out capped);
        builder.Append(",\"projectClips\":[");
        for (int i = 0; i < projectClips.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append("{\"name\":\"").Append(Escape(projectClips[i].name)).Append("\"");
            builder.Append(",\"path\":\"").Append(Escape(AssetDatabase.GetAssetPath(projectClips[i]))).Append("\"");
            builder.Append(",\"seconds\":").Append(projectClips[i].length.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(",\"frames\":").Append(Mathf.RoundToInt(projectClips[i].frameRate * projectClips[i].length));
            builder.Append('}');
        }
        builder.Append("]");
        // HOW MANY WERE LOOKED AT, AND WHETHER THE LOOK STOPPED EARLY. A capped scan that reported only
        // its results would read as "that is all there is" — the same untruth as the comment above.
        builder.Append(",\"clipsScanned\":").Append(scanned);
        builder.Append(",\"clipsScanCapped\":").Append(capped ? "true" : "false");

        builder.Append("}");
        return builder.ToString();
    }

    /**
     * Standalone `.anim` assets in the project that fit this rig.
     *
     * A clip fits when the paths it animates address transforms this model has. Matching by name would
     * pair `Enemy1 Idle Clip` with anything called Enemy1 and miss a clip called `Idle`; the bindings
     * are the ground truth and they are already in the asset.
     *
     * The last segment of each binding path is the transform's own name, which is what a skeleton's
     * bones are keyed by. A clip counts when a third of its distinct paths match AND at least three do,
     * so a clip that happens to animate one object with a common name is not claimed by every rig.
     */
    private static List<AnimationClip> FindProjectClips(GameObject root, out int scanned, out bool capped)
    {
        var fits = new List<AnimationClip>();
        scanned = 0;
        capped = false;

        var names = new HashSet<string>(StringComparer.Ordinal);
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null) names.Add(all[i].name);
        }
        if (names.Count == 0) return fits;

        // A CEILING, because each clip costs a `GetCurveBindings` on the thread the editor is drawing
        // itself with, and a large project can hold thousands. The answer says when it stopped early.
        const int MaxClips = 400;
        string[] guids = AssetDatabase.FindAssets("t:AnimationClip");
        for (int i = 0; i < guids.Length; i++)
        {
            if (scanned >= MaxClips)
            {
                capped = true;
                break;
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            // The `.anim` extension is the test for "standalone". A clip that is a sub-asset of an .fbx
            // is already reported above and would otherwise be counted twice.
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) continue;
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) continue;
            scanned += 1;

            EditorCurveBinding[] bindings;
            try
            {
                bindings = AnimationUtility.GetCurveBindings(clip);
            }
            catch
            {
                continue;
            }
            if (bindings == null || bindings.Length == 0) continue;

            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int b = 0; b < bindings.Length; b++)
            {
                string bindingPath = bindings[b].path;
                if (string.IsNullOrEmpty(bindingPath)) continue;
                int slash = bindingPath.LastIndexOf('/');
                paths.Add(slash >= 0 ? bindingPath.Substring(slash + 1) : bindingPath);
            }
            if (paths.Count == 0) continue;

            int matched = 0;
            foreach (string name in paths)
            {
                if (names.Contains(name)) matched += 1;
            }
            if (matched >= 3 && matched * 3 >= paths.Count)
            {
                fits.Add(clip);
            }
        }
        return fits;
    }

    /// <summary>The animation clips among a model's sub-assets.</summary>
    private static AnimationClip[] FilterClips(UnityEngine.Object[] representations)
    {
        var clips = new List<AnimationClip>();
        for (int i = 0; i < representations.Length; i++)
        {
            AnimationClip clip = representations[i] as AnimationClip;
            if (clip == null) continue;
            // The importer adds a preview clip for some files; it is not a real animation.
            if (clip.name != null && clip.name.StartsWith("__preview__", StringComparison.Ordinal)) continue;
            clips.Add(clip);
        }
        return clips.ToArray();
    }

    private static void Stop()
    {
        running = false;
        try
        {
            Application.logMessageReceivedThreaded -= OnLog;
        }
        catch
        {
            // the domain is going away
        }
        try
        {
            EditorApplication.update -= Tick;
        EditorApplication.update -= DrainMainThread;
        }
        catch
        {
            // the domain is going away
        }
        try
        {
            if (listener != null)
            {
                listener.Stop();
                listener.Close();
            }
        }
        catch
        {
            // shutting down anyway
        }
        listener = null;
        UnpublishPort();
    }

    private static void Loop()
    {
        while (running && listener != null && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = listener.GetContext();
            }
            catch
            {
                break;
            }
            try
            {
                Handle(context);
            }
            catch (Exception error)
            {
                Respond(context, 500, "{\"ok\":false,\"error\":\"" + Escape(error.Message) + "\"}");
            }
        }
    }

    /**
     * Work waiting for the main thread.
     *
     * A concurrent queue rather than `EditorApplication.delayCall`, and the difference is not stylistic.
     * `delayCall` is a delegate FIELD the editor invokes and then clears; adding to it from the listener
     * thread races with that clearing, and a callback lost in the race is never run — the request waits
     * out its whole timeout and answers "the editor did not answer in time". Measured with the editor
     * behind another window: `/scene`, `/capture` and `/model` all failed, and all three answered the
     * moment the window was brought forward.
     *
     * Enqueue on a ConcurrentQueue is thread-safe by construction, so there is no race to lose.
     */
    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> mainThreadWork =
        new System.Collections.Concurrent.ConcurrentQueue<Action>();

    /**
     * Run whatever is waiting, on the main thread.
     *
     * Registered on `EditorApplication.update` — a subscription, not a field that gets cleared, so it
     * stays registered — and the update pass is the one the editor log shows running even when the
     * window is behind another one.
     */
    private static void DrainMainThread()
    {
        Action work;
        // Bounded per pass: a burst must not hold the editor's update for an unbounded time.
        int ran = 0;
        while (ran < 32 && mainThreadWork.TryDequeue(out work))
        {
            ran += 1;
            try
            {
                work();
            }
            catch (Exception error)
            {
                Debug.LogError("[YoweiBridge] main-thread work threw: " + error);
            }
        }
    }

    /** Listener thread. Reads the cache and never touches a Unity API. */
    private static void Handle(HttpListenerContext context)
    {
        string path = context.Request.Url != null ? context.Request.Url.AbsolutePath : "/";
        string method = context.Request.HttpMethod;

        if (path == "/status" && method == "GET")
        {
            Respond(context, 200, cachedStatus);
            return;
        }

        if (path == "/log" && method == "GET")
        {
            long since = 0;
            string query = context.Request.Url != null ? context.Request.Url.Query : string.Empty;
            int at = query.IndexOf("since=", StringComparison.Ordinal);
            if (at >= 0)
            {
                string rest = query.Substring(at + 6);
                int amp = rest.IndexOf('&');
                if (amp >= 0) rest = rest.Substring(0, amp);
                long.TryParse(rest, out since);
            }
            Respond(context, 200, BuildLog(since));
            return;
        }

        if (path == "/scene" && method == "POST")
        {
            string body;
            using (var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }
            string action = Field(body, "action");
            string name = Field(body, "name");
            string scenePath = Field(body, "path");
            string kind = Field(body, "kind");
            string script = Field(body, "script");
            string parentName = Field(body, "parent");
            float x = FieldFloat(body, "x");
            float y = FieldFloat(body, "y");
            float z = FieldFloat(body, "z");

            // The scene APIs are main-thread only, so the work is posted there and
            // the listener waits for the answer instead of replying "queued".
            string result = null;
            RunOnMainThread(() => { result = RunScene(action, name, scenePath, kind, script, x, y, z, parentName); });
            for (int waited = 0; result == null && waited < 150; waited++)
            {
                System.Threading.Thread.Sleep(100);
            }
            Respond(context, 200, result ?? StallAnswer());
            return;
        }

        if (path == "/test" && method == "POST")
        {
            string body;
            using (var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }
            int seconds = (int)FieldFloat(body, "seconds");
            if (seconds <= 0) seconds = 5;
            Respond(context, 200, RunPlaytest(seconds, true));
            return;
        }

        if (path == "/capture")
        {
            // The frame is written to Library/ and the path returned. Base64 in JSON would be
            // a third larger than the image and would have to be parsed to be used.
            string captureBody = "";
            if (method == "POST")
            {
                using (var captureReader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    captureBody = captureReader.ReadToEnd();
                }
            }
            int captureWidth = (int)FieldFloat(captureBody, "width");
            int captureHeight = (int)FieldFloat(captureBody, "height");
            int captureQuality = (int)FieldFloat(captureBody, "quality");
            string captureResult = null;
            RunOnMainThread(() => { captureResult = RunCapture(captureWidth, captureHeight, captureQuality); });
            for (int waited = 0; captureResult == null && waited < 100; waited++)
            {
                Thread.Sleep(50);
            }
            Respond(context, 200, captureResult ?? StallAnswer());
            return;
        }

        if (path == "/probe" && method == "POST")
        {
            string probeBody;
            using (var probeReader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                probeBody = probeReader.ReadToEnd();
            }
            string probePath = Field(probeBody, "path");
            string probeResult = null;
            RunOnMainThread(() => { probeResult = RunProbe(probePath); });
            for (int waited = 0; probeResult == null && waited < 200; waited++)
            {
                Thread.Sleep(50);
            }
            Respond(context, 200, probeResult ?? StallAnswer());
            return;
        }

        if (path == "/input" && method == "POST")
        {
            string inputBody;
            using (var inputReader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                inputBody = inputReader.ReadToEnd();
            }
            string inputResult = null;
            RunOnMainThread(() => { inputResult = RunInput(inputBody); });
            for (int waited = 0; inputResult == null && waited < 200; waited++)
            {
                Thread.Sleep(50);
            }
            Respond(context, 200, inputResult ?? StallAnswer());
            return;
        }

        if (path == "/assert" && method == "POST")
        {
            string assertBody;
            using (var assertReader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                assertBody = assertReader.ReadToEnd();
            }
            string spec = Field(assertBody, "spec");
            string assertResult = null;
            RunOnMainThread(() => { assertResult = RunAssert(spec); });
            for (int waited = 0; assertResult == null && waited < 200; waited++)
            {
                Thread.Sleep(50);
            }
            Respond(context, 200, assertResult ?? StallAnswer());
            return;
        }

        if (path == "/model" && method == "POST")
        {
            string modelBody;
            using (var modelReader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                modelBody = modelReader.ReadToEnd();
            }
            string modelPath = Field(modelBody, "path");
            string modelResult = null;
            RunOnMainThread(() => { modelResult = RunModel(modelPath); });
            for (int waited = 0; modelResult == null && waited < 200; waited++)
            {
                Thread.Sleep(50);
            }
            Respond(context, 200, modelResult ?? StallAnswer());
            return;
        }

        if (path == "/shutdown" && method == "POST")
        {
            // Answer first, then stop on the next editor tick: closing the
            // listener from inside its own request would cut the response off.
            Respond(context, 200, "{\"ok\":true,\"stopping\":true}");
            EditorApplication.delayCall += Stop;
            return;
        }

        if (path == "/quit" && method == "POST")
        {
            // CLOSING THE EDITOR IS A DIFFERENT ACT FROM ENDING THE CONNECTION, and the two are
            // separate routes because of it. `/shutdown` stops this listener and leaves the editor
            // open; this one asks Unity to quit. The panel's close button uses this, because a person
            // who presses X on a "Unity: connected" bar means "shut it down", not "unplug the cable".
            //
            // THE RESPONSE GOES OUT BEFORE THE QUIT IS SCHEDULED. Quitting ends the process, and doing
            // that inside its own request handler would cut the answer off — the caller would see a
            // dropped connection and report a failure for a quit that worked.
            //
            // UNITY'S OWN FILE -> QUIT IS INVOKED. `EditorApplication.Exit` is the obvious call and it
            // is the wrong one: Unity's own documentation says it "will exit right away, without
            // asking to save changes, so you may lose data". Closing somebody's editor from a plugin
            // is already a large act; silently discarding the scene they had open would be a
            // destructive one, and no button in this panel is worth a person's unsaved work. The menu
            // item is exactly what a human pressing File -> Quit gets, prompt included.
            //
            // "File/Quit" is the path in the versions this plugin targets; "File/Exit" is what some
            // builds and locales expose instead, so both are tried. `ExecuteMenuItem` returns false
            // when the path does not exist, which is what makes asking twice safe.
            Respond(context, 200, "{\"ok\":true,\"quitting\":true}");
            EditorApplication.delayCall += () =>
            {
                Stop();
                bool asked = false;
                foreach (string menu in new string[] { "File/Quit", "File/Exit" })
                {
                    try
                    {
                        if (EditorApplication.ExecuteMenuItem(menu))
                        {
                            asked = true;
                            break;
                        }
                    }
                    catch (Exception error)
                    {
                        Debug.LogWarning("[YoweiBridge] " + menu + " could not be invoked: " + error.Message);
                    }
                }
                if (!asked)
                {
                    // NOTHING FALLS BACK TO `Exit`. Leaving the editor open is the honest outcome when
                    // the only remaining way out would discard unsaved work; the host reports that the
                    // request was made and that Unity is still running, and the person closes it
                    // themselves with their work intact.
                    Debug.LogWarning("[YoweiBridge] no quit menu item was available, so Unity was left open rather than closed without saving");
                }
            };
            return;
        }

        if (path == "/command" && method == "POST")
        {
            string body;
            using (var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8))
            {
                body = reader.ReadToEnd();
            }
            string command = ExtractCommand(body);
            if (command == null || !Allowed.Contains(command))
            {
                Respond(context, 400, "{\"ok\":false,\"error\":\"command not allowed\"}");
                return;
            }
            // Acknowledge first, then dispatch: the editor APIs run on the main
            // thread and must not be called from here.
            RunOnMainThread(() => Run(command));
            Respond(context, 200, "{\"ok\":true,\"queued\":\"" + Escape(command) + "\"}");
            return;
        }

        Respond(context, 404, "{\"ok\":false,\"error\":\"not found\"}");
    }

    private static void Run(string command)
    {
        switch (command)
        {
            case "play":
                EditorApplication.isPlaying = true;
                break;
            case "pause":
                EditorApplication.isPaused = !EditorApplication.isPaused;
                break;
            case "stop":
                EditorApplication.isPlaying = false;
                break;
            case "step":
                EditorApplication.Step();
                break;
            case "refresh":
                AssetDatabase.Refresh();
                break;
        }
        // Force the next tick to republish, so a caller polling /status sees the
        // effect instead of a stale snapshot.
        lastSample = -1.0;
        Debug.Log("[YoweiBridge] ran " + command);
    }

    /**
     * Why a main-thread hand-off probably did not come back.
     *
     * The editor publishes `cachedStatus` from `Tick`, which is itself an update-pass callback, so the
     * age of that cache is a direct reading of whether the pass is running at all — and it can be read
     * from the listener thread without asking the main thread anything.
     */
    private static string StalledReason()
    {
        double age;
        long written = System.Threading.Interlocked.Read(ref statusAtTicks);
        age = written == 0 ? 9999.0 : (DateTime.UtcNow - new DateTime(written, DateTimeKind.Utc)).TotalSeconds;
        if (age > 4.0)
        {
            return "the Unity window is minimised or hidden, so its main thread is not running. "
                + "Restore the editor window and try again — nothing was lost. "
                + "(no status published for " + age.ToString("0") + "s)";
        }
        return "the editor is busy — importing, compiling or entering play mode. "
            + "Try again in a moment. (status is current, so the editor is running)";
    }

    /** The answer for a hand-off that did not come back, with the likely cause named. */
    private static string StallAnswer()
    {
        // THE QUOTES ARE PART OF THE VALUE.
        //
        // `Escape` escapes what is INSIDE a JSON string; it does not supply the delimiters. Without them
        // this emitted
        //     {"ok":false,"stalled":true,"error":the Unity window is minimised...}
        // which is not JSON at all, so the client reported a parse error and the real diagnosis —
        // restore the window — was the one thing it could not say.
        //
        // NOT MY FINDING EITHER: the same newer copy in AIGame's Assets/Editor carried this fix. Worth
        // naming that I made this exact mistake in `RunModel` earlier in the same session, fixed it
        // there, and then wrote this function the same way — which is what a rule about `Escape` is for.
        return "{\"ok\":false,\"stalled\":true,\"error\":\"" + Escape(StalledReason()) + "\"}";
    }

    /** Where the published port files live. Mirrored in `index.js`. */
    private static string BridgeDir()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return System.IO.Path.Combine(root, "YoweiSpace", "bridges");
    }

    /**
     * Say which port this bridge took, and which project it is for.
     *
     * A FILE RATHER THAN A CONVENTION. The host cannot know which editor is which from a port number, and
     * cannot assume it is the first to start. This names both facts, survives the two processes starting
     * in either order, and can be read by a person trying to work out what is going on.
     *
     * Paths use forward slashes so a file written on Windows reads the same everywhere the host runs.
     */
    private static void PublishPort()
    {
        try
        {
            string dir = BridgeDir();
            System.IO.Directory.CreateDirectory(dir);
            string project = System.IO.Path.GetDirectoryName(Application.dataPath);
            if (project != null) project = project.Replace('\\', '/');
            string json =
                "{\"port\":" + boundPort
                + ",\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id
                + ",\"project\":\"" + Escape(project) + "\""
                + ",\"unity\":\"" + Escape(Application.unityVersion) + "\""
                + ",\"at\":\"" + DateTime.UtcNow.ToString("o") + "\"}";
            publishedPath = System.IO.Path.Combine(dir, boundPort + ".json");
            System.IO.File.WriteAllText(publishedPath, json, Encoding.UTF8);
        }
        catch (Exception error)
        {
            // Not fatal: the host falls back to its configured port, which is what it used before.
            Debug.LogWarning("[YoweiBridge] could not publish the port: " + error.Message);
            publishedPath = null;
        }
    }

    /** Remove this instance's own record, and only its own. */
    private static void UnpublishPort()
    {
        try
        {
            if (publishedPath != null && System.IO.File.Exists(publishedPath))
            {
                System.IO.File.Delete(publishedPath);
            }
        }
        catch
        {
            // a stale file is better than a failed shutdown
        }
        publishedPath = null;
    }

    private static void RunOnMainThread(Action action)
    {
        mainThreadWork.Enqueue(action);
        // THE DOORBELL. A queue nobody looks at is a queue that sits there, and the update pass is
        // exactly what slows down when the window is not visible. This asks for a pass rather than
        // waiting for a repaint to bring one along.
        try
        {
            EditorApplication.QueuePlayerLoopUpdate();
        }
        catch
        {
            // Nothing to do: the update pass will come round on its own.
        }
    }

    private static string ExtractCommand(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        int at = body.IndexOf("\"command\"", StringComparison.Ordinal);
        if (at < 0) return null;
        int colon = body.IndexOf(':', at);
        if (colon < 0) return null;
        int first = body.IndexOf('"', colon + 1);
        if (first < 0) return null;
        int last = body.IndexOf('"', first + 1);
        if (last < 0) return null;
        return body.Substring(first + 1, last - first - 1).Trim().ToLowerInvariant();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }

    /** Read a string field out of a small JSON body. */
    private static string Field(string body, string key)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        string needle = "\"" + key + "\"";
        int at = body.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0) return string.Empty;
        int colon = body.IndexOf(':', at + needle.Length);
        if (colon < 0) return string.Empty;
        int start = body.IndexOf('"', colon + 1);
        if (start < 0) return string.Empty;
        var builder = new StringBuilder();
        for (int i = start + 1; i < body.Length; i++)
        {
            char ch = body[i];
            if (ch == '\\' && i + 1 < body.Length)
            {
                char next = body[i + 1];
                if (next == 'n') builder.Append('\n');
                else if (next == 't') builder.Append('\t');
                else if (next == 'r') builder.Append('\r');
                else builder.Append(next);
                i += 1;
                continue;
            }
            if (ch == '"') break;
            builder.Append(ch);
        }
        return builder.ToString();
    }

    /** Read a numeric field; 0 when absent, which is the right default for a position. */
    private static float FieldFloat(string body, string key)
    {
        if (string.IsNullOrEmpty(body)) return 0f;
        string needle = "\"" + key + "\"";
        int at = body.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0) return 0f;
        int colon = body.IndexOf(':', at + needle.Length);
        if (colon < 0) return 0f;
        int i = colon + 1;
        while (i < body.Length && (body[i] == ' ' || body[i] == '\t')) i++;
        int end = i;
        while (end < body.Length && (char.IsDigit(body[end]) || body[end] == '-' || body[end] == '+' || body[end] == '.' || body[end] == 'e' || body[end] == 'E')) end++;
        float value;
        if (end > i && float.TryParse(body.Substring(i, end - i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return value;
        }
        return 0f;
    }

    private static void Respond(HttpListenerContext context, int status, string json)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
        catch
        {
            // the client hung up; nothing to do
        }
    }
}
