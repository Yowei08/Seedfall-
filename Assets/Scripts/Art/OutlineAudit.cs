#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Seedfall
{
    /// <summary>
    /// ★ v221：**描边开关的验收读数**（只在编辑器里跑，不进包）★
    ///
    /// 为什么需要它：用户报"战斗模式下的场景被异常施加了两次描边"。
    /// 两条描边各自都在别的文件里，**屏幕上不可能靠肉眼分清楚哪一条还在画**：
    ///   · 物体描边 —— `SeedfallToon.shader` 的反向外壳，值落在**每份材质**上（`_OutlinePixels`）
    ///   · 屏幕描边 —— `ScreenOutlineFeature`，值落在**渲染器资产**里（`m_Active` / `strength`），
    ///                 而"这一帧到底有没有入队"只有运行期才知道
    /// ⇒ 本文件在**每次进场景**时把两边一起打出来：
    ///     场景名 · 相机（正交/尺寸/机位）· ScreenOutlineFeature 的 m_Active/strength/本场景画不画
    ///     · 物体描边的三件套与"有多少渲染器在用"
    /// 这样"改前 2 条 / 改后 1 条"是可引用的读数，而不是一句"看着像好点了" ✓
    ///
    /// 触发点：`SceneManager.sceneLoaded`（编辑器里 play 时生效）+ 菜单项手动重打。
    /// </summary>
    [InitializeOnLoad]
    public static class OutlineAudit
    {
        private const string Tag = "[OutlineAudit] ";

        /// <summary>与 `ScreenOutlineInstaller.RendererPaths` 同一份清单。</summary>
        private static readonly string[] RendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset",
        };

        /// <summary>已经打过读数的场景名（同一次 play 里不重复刷屏）。</summary>
        private static string _reported;
        /// <summary>当前场景已经"稳定"了几帧。</summary>
        private static int _settle;
        private static string _watching;

        static OutlineAudit()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _reported = null; _watching = null; _settle = 0;
            }
        }

        /// <summary>
        /// ★ 触发方式是**轮询活动场景**，不是 `SceneManager.sceneLoaded` ★
        /// 实测（v221 第一版）：在编辑器里给 `sceneLoaded` 挂回调**没有触发**（一次都没进），
        /// 于是整份读数一条都没打出来。轮询只依赖 `EditorApplication.update`（编辑器必然在跑），
        /// 而且"活动场景"本身就决定 `ScreenOutlineFeature` 的取舍
        ///（主城是 Login 场景 + Town 增量加载 ⇒ 活动场景 = Login）✓
        /// </summary>
        private static void Tick()
        {
            if (!EditorApplication.isPlaying) { _watching = null; _settle = 0; _probeSpawned = false; return; }

            // ★ v222：先把实验台挂出来（固定名字 ⇒ 桥能一条命令往返）★
            // 它只在编辑器里存在，且只提供"读/改描边参数"这两件事，不进包 ✓
            if (!_probeSpawned)
            {
                _probeSpawned = true;
                if (GameObject.Find(OutlineProbe.ProbeName) == null)
                {
                    var go = new GameObject(OutlineProbe.ProbeName);
                    go.AddComponent<OutlineProbe>();
                    Object.DontDestroyOnLoad(go);
                }
            }

            string now = SceneManager.GetActiveScene().name;
            if (now != _watching) { _watching = now; _settle = 0; }

            // 进场景后再等若干帧：相机与材质都是 Awake/Start 之后才定，早打会读到默认值
            if (++_settle < 40) return;
            if (now == _reported) return;

            _reported = now;
            Debug.Log(Report(now));
        }

        private static bool _probeSpawned;

        [MenuItem("Seedfall/渲染/打印描边开关读数")]
        private static void PrintMenu()
        {
            Debug.Log(Report(SceneManager.GetActiveScene().name));
        }

        /// <summary>把"两条描边当前的开关"拼成一段可引用的读数。</summary>
        public static string Report(string sceneName)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Tag).Append("═══ 描边开关读数 ═══ 场景=")
              .Append(string.IsNullOrEmpty(sceneName) ? SceneManager.GetActiveScene().name : sceneName)
              .Append("（play=").Append(EditorApplication.isPlaying).Append("）\n");

            // ── 相机 ──
            Camera cam = Camera.main;
            if (cam == null)
            {
                sb.Append("  相机：**Camera.main 为 null**\n");
            }
            else
            {
                Vector3 p = cam.transform.position;
                sb.Append("  相机：").Append(cam.name)
                  .Append(" ｜正交=").Append(cam.orthographic)
                  .Append(" ｜size=").Append(cam.orthographicSize.ToString("0.00"))
                  .Append(" ｜fov=").Append(cam.fieldOfView.ToString("0.0"))
                  .Append(" ｜机位=(").Append(p.x.ToString("0.0")).Append(',')
                  .Append(p.y.ToString("0.0")).Append(',').Append(p.z.ToString("0.0")).Append(")\n");
            }

            // ── ② 屏幕描边（渲染器资产里那个 Feature）──
            sb.Append("  ② **屏幕描边**（SeedfallScreenOutline / 后期）：\n");
            int found = 0;
            for (int i = 0; i < RendererPaths.Length; i++)
            {
                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPaths[i]);
                if (data == null) { sb.Append("     · ").Append(RendererPaths[i]).Append(" → 资产缺失\n"); continue; }
                found += ReportRenderer(sb, data);
            }
            if (found == 0) sb.Append("     · **没有任何渲染器装了它** ⇒ 屏幕描边根本没画\n");

            // ── ① 物体描边（材质上的三件套）──
            sb.Append("  ① **物体描边**（SeedfallToon 反向外壳 / 每个模型自己一条）：\n");
            ReportObjectOutline(sb);

            return sb.ToString();
        }

        private static int ReportRenderer(System.Text.StringBuilder sb, ScriptableRendererData data)
        {
            int n = 0;
            // `m_RendererFeatures` 是 URP 的私有列表，走序列化读（公开 API 只有运行时那份）
            var so = new SerializedObject(data);
            SerializedProperty list = so.FindProperty("m_RendererFeatures");
            if (list == null) return 0;
            for (int i = 0; i < list.arraySize; i++)
            {
                var feature = list.GetArrayElementAtIndex(i).objectReferenceValue as ScreenOutlineFeature;
                if (feature == null) continue;
                n++;
                sb.Append("     · [").Append(data.name).Append("] ").Append(feature.Readout()).Append('\n');
            }
            return n;
        }

        private static void ReportObjectOutline(System.Text.StringBuilder sb)
        {
            Renderer[] all = Object.FindObjectsByType<Renderer>();
            int toon = 0, simple = 0;
            // ★ 两个 shader **各取一个样本** ★
            // 第一版只留了一份样本，实测抓到的是 `Seedfall/ToonSimple`（`_OutlinePixels` 恒 0）
            // ⇒ 读出来像"物体描边没开"，与事实相反 ✗ 两个都打才能自证 ✓
            Material mToon = null, mSimple = null;
            for (int i = 0; i < all.Length; i++)
            {
                Material m = all[i] != null ? all[i].sharedMaterial : null;
                if (m == null || m.shader == null) continue;
                string sh = m.shader.name;
                if (sh == "Seedfall/Toon") { toon++; if (mToon == null) mToon = m; }
                else if (sh == "Seedfall/ToonSimple") { simple++; if (mSimple == null) mSimple = m; }
            }
            sb.Append("     用 Seedfall/Toon 的渲染器 ").Append(toon)
              .Append(" 个 ｜Seedfall/ToonSimple ").Append(simple).Append(" 个\n");
            AppendMat(sb, "完整版 Seedfall/Toon ", mToon);
            AppendMat(sb, "简化版 Seedfall/ToonSimple", mSimple);
            sb.Append("     （`_OutlinePixels` 是**屏幕像素兜底**；Simple 版按设计恒 0 —— 像素墨线只挂在完整版上）\n");
        }

        private static void AppendMat(System.Text.StringBuilder sb, string label, Material m)
        {
            if (m == null) { sb.Append("     ").Append(label).Append("：场景里没有\n"); return; }
            float width = m.HasProperty("_OutlineWidth") ? m.GetFloat("_OutlineWidth") : -1f;
            float pixels = m.HasProperty("_OutlinePixels") ? m.GetFloat("_OutlinePixels") : -1f;
            Color col = m.HasProperty("_OutlineColor") ? m.GetColor("_OutlineColor") : Color.white;
            sb.Append("     ").Append(label).Append("：").Append(m.name)
              .Append(" ｜_OutlineColor=RGB(").Append(col.r.ToString("0.00")).Append(',')
              .Append(col.g.ToString("0.00")).Append(',').Append(col.b.ToString("0.00"))
              .Append(") ｜_OutlineWidth=").Append(width.ToString("0.000"))
              .Append(" m ｜_OutlinePixels=").Append(pixels.ToString("0.00")).Append(" px\n");
        }
    }
}
#endif
