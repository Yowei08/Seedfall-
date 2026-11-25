using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 农田装饰的**材质守护**（v238）—— 每 90 帧校验一次 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 为什么必须是"一个独立组件 + 协程"，而不是写在 `TownBootstrap.Update` 里
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① **晚于本组件**：`Art/TownArtDresser.cs`（`[DefaultExecutionOrder(300)]`）在 `Start` 里
    ///      才遍历渲染器改材质 / 塞 `_MainTex`，而农田装饰在 `TownBootstrap.Awake` 就建完了
    ///      ⇒ **先建后污染** ⇒ "只建一次 / 只修一次"必然漏。
    ///   ② **`Update` 会整帧不跑**：`Login/LoginBootstrap.cs:197` 在登录阶段执行
    ///      `_town.enabled = false`，主城当登录页背景时 `TownBootstrap.Update` 一帧都不执行
    ///      （实测：在登录阶段停 15 秒，一次校验都没跑）。
    ///      而 Unity 的**协程不受组件 `enabled` 影响**（只有 `SetActive(false)` / 销毁才停）
    ///      ⇒ 校验放进 `Start` 起的协程里，登录背景里照跑、进城后照跑 ✓
    ///   ③ 本组件挂在 `TOWN/TOWN_FarmDecorGuard` 上（**不是** `TownBootstrap` 那个对象），
    ///      所以它自己的 `Update` 也不受 `_town.enabled = false` 影响 —— 两条通道都留，
    ///      任何一条被将来的改动掐掉，另一条还在（诊断里的"校验已跑 N 次"能证明它在跑）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TownFarmDecorGuard : MonoBehaviour
    {
        /// <summary>两次校验之间的帧数。</summary>
        public const int SweepIntervalFrames = 90;

        private void Start()
        {
            Arm();
        }

        /// <summary>运行中已起过协程就不再起第二条（`Arm()` 可以被外部重复调用）。</summary>
        private bool _armed;

        /// <summary>
        /// 起校验协程（幂等）。
        /// ★ 为什么要暴露出来：**域重载**（play 里改脚本）会把协程一起干掉，
        ///   而 `Start` 不会重跑 ⇒ 校验会静默停摆。`TownFarmDecor.EnsureRegistry()` 在
        ///   从场景认回物件之后调一次本法，把校验重新接上 ✓
        /// </summary>
        public void Arm()
        {
            if (_armed) return;
            _armed = true;
            StartCoroutine(SweepLoop());
        }

        private System.Collections.IEnumerator SweepLoop()
        {
            // 等美术通道（order 300 的 `Start`）把材质改完，再扫第一遍 —— 首轮**有没有都打一行**
            yield return null;
            yield return null;
            TownFarmDecor.Repair(true);

            while (true)
            {
                for (int i = 0; i < SweepIntervalFrames; i++) yield return null;   // ★ 每 90 帧 ★
                TownFarmDecor.Repair(false);
            }
        }

        /// <summary>第二条通道：本组件自己的 `Update`（同样每 90 帧，与协程互不影响）。</summary>
        private void Update()
        {
            if (Time.frameCount % SweepIntervalFrames == 0) TownFarmDecor.Repair(false);
        }

        // ══════════════════════════════════════════════════════════════════
        // 截图取景入口（**只给验收用**，正常流程不调用）
        // ══════════════════════════════════════════════════════════════════
        //
        // 为什么放在这里：登录阶段 `TownBootstrap.Update` 整帧不跑、相机会被登录机位写死，
        // 验收要"从上面看一眼农田"时没有任何入口能把相机摆过去。
        // 本组件始终是激活的 ⇒ 在这里提供一个取景方法，并在 `LateUpdate` 里**保持**这个机位
        // （否则登录流程的下一帧会把相机写回去）。`FarmShot("off")` 释放。

        private bool _holdCam;
        private Vector3 _holdPos;
        private Quaternion _holdRot;
        private float _holdSize;

        /// <summary>
        /// 把主相机摆到农田正上方（俯视）。参数：`"距离,俯角,正交半高,焦点x,焦点z"`（都可省略，
        /// 默认 26 / 58 / 8.6 / `FarmCenter`）；传 `"off"` 释放机位。
        /// </summary>
        public string FarmShot(string csv)
        {
            string text = (csv ?? "").Trim();
            if (text.Equals("off", System.StringComparison.OrdinalIgnoreCase))
            {
                _holdCam = false;
                return "机位已释放";
            }

            Camera cam = Camera.main;
            if (cam == null) return "❌ 没有主相机（Camera.main = null）";

            float dist = 26f, pitch = 58f, size = 8.6f;
            Vector2 c = TownLayout.FarmCenter;
            string[] p = text.Split(',');
            if (p.Length >= 1 && float.TryParse(p[0], out float d) && d > 1f) dist = d;
            if (p.Length >= 2 && float.TryParse(p[1], out float a)) pitch = Mathf.Clamp(a, 10f, 89f);
            if (p.Length >= 3 && float.TryParse(p[2], out float s) && s > 0.5f) size = s;
            if (p.Length >= 4 && float.TryParse(p[3], out float fx)) c.x = fx;
            if (p.Length >= 5 && float.TryParse(p[4], out float fz)) c.y = fz;

            Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
            _holdPos = new Vector3(c.x, 0.40f, c.y) + (rot * Vector3.back) * dist;
            _holdRot = rot;
            _holdSize = size;
            _holdCam = true;
            ApplyCam(cam);

            Log.Info($"[Town] 农田取景：距离 {dist:0.0} 俯角 {pitch:0.0}° 半高 {size:0.00} · " +
                     $"机位 ({_holdPos.x:0.00},{_holdPos.y:0.00},{_holdPos.z:0.00}) · 看 ({c.x:0.00},{c.y:0.00})");
            return $"农田取景就位：机位 ({_holdPos.x:0.0},{_holdPos.y:0.0},{_holdPos.z:0.0}) · 半高 {size:0.0} · 看 ({c.x:0.0},{c.y:0.0})";
        }

        private void LateUpdate()
        {
            if (!_holdCam) return;
            Camera cam = Camera.main;
            if (cam != null) ApplyCam(cam);       // 保持机位（登录流程会每帧写回它自己的取景）
        }

        private void ApplyCam(Camera cam)
        {
            cam.orthographic = true;
            cam.orthographicSize = _holdSize;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;
            cam.transform.position = _holdPos;
            cam.transform.rotation = _holdRot;
        }
    }
}
