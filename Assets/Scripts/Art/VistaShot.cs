using System.Collections;
using System.IO;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★ 视觉验收抓拍器（美术/着色器/后期 lane 的自检工具，**不改任何画面**）★
    ///
    /// 触发方式（不依赖任何编辑器 API、不依赖 MCP 桥）：
    ///   往 `&lt;工程&gt;/Library/vistashot.txt` 写一行文本 ⇒ 下一帧抓一张 PNG 再把触发文件删掉。
    ///   文件内容 = 目标路径（相对路径按 `Library/vistashots/` 解析）；空文件 = 自动命名。
    ///
    /// 为什么用 `ScreenCapture` 而不是 `Camera.Render`：
    ///   交付要求"登录 UI 必须仍在最上层且可读" —— 只有抓**合成后的整帧**（含 IMGUI 的 OnGUI）
    ///   才看得出 UI 有没有被雾糊掉；`Camera.Render` 只拿到相机那一遍，UI 永远不在图里。
    ///
    /// 运行时开销：每 0.5 秒一次 `File.Exists`（不到 1 微秒），且**只在播放时**存在。
    /// </summary>
    public sealed class VistaShot : MonoBehaviour
    {
        private const string TriggerRel = "Library/vistashot.txt";
        private const string OutDir = "Library/vistashots";

        private float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("VISTA_SHOT");
            go.AddComponent<VistaShot>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;

            string root = Path.GetDirectoryName(Application.dataPath);
            string trigger = Path.Combine(root, TriggerRel);
            if (!File.Exists(trigger)) return;

            string want = "";
            try { want = File.ReadAllText(trigger).Trim(); } catch { want = ""; }
            try { File.Delete(trigger); } catch { }

            if (string.IsNullOrEmpty(want))
                want = Path.Combine(OutDir, "shot_" + System.DateTime.Now.ToString("HHmmss") + ".png");
            else if (!Path.IsPathRooted(want))
                want = Path.Combine(root, want);

            string dir = Path.GetDirectoryName(want);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 机位证据：把当前所有相机的方位打出来，图与日志一起看才说得清"这是哪个机位"
            LogCameras();

            StopAllCoroutines();
            StartCoroutine(Grab(want));
        }

        private IEnumerator Grab(string path)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path);
            // 写盘在帧末异步完成，等两帧再报 OK（否则日志会先于文件出现，读图时扑空）
            yield return null;
            yield return null;
            var fi = new FileInfo(path);
            Debug.Log(fi.Exists
                ? "抓拍 OK → " + path + "  (" + fi.Length + " B, " + Screen.width + "x" + Screen.height + ")"
                : "抓拍未落盘（还差一点）→ " + path);
        }

        private static void LogCameras()
        {
            Camera[] all = Camera.allCameras;   // 只在已启用（会画）的相机
            Debug.Log("[VISTA] 抓拍时的相机：" + all.Length + " 台（enabled 且 active）");
            for (int i = 0; i < all.Length; i++)
            {
                Camera c = all[i];
                Vector3 e = c.transform.eulerAngles;
                Debug.Log("[VISTA]   · " + c.gameObject.name
                    + " depth=" + c.depth.ToString("0")
                    + " pos=" + c.transform.position.x.ToString("0.0") + "," + c.transform.position.y.ToString("0.0") + "," + c.transform.position.z.ToString("0.0")
                    + " pitch<=" + (e.x > 180f ? e.x - 360f : e.x).ToString("0") + "°"
                    + " fov=" + c.fieldOfView.ToString("0") + " ortho=" + c.orthographic
                    + " clear=" + c.clearFlags);
            }
        }
    }
}
