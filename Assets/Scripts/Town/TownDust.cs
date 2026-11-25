using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v224：脚步灰尘（**对象池 + 方形扁片 + 放大淡出**）★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么单独一个文件
    /// ══════════════════════════════════════════════════════════════════════
    /// `TownLife` 已经有六样活物了，再往里塞"池 + 材质属性块"会把那个文件变成没人敢碰的长文件 ✗
    /// 这里只做一件事：**玩家走动时在脚下留下会散开的方形尘土**。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、三条硬要求（逐条对应实现）
    /// ══════════════════════════════════════════════════════════════════════
    ///   ① **共享 1 份材质 ＋ 1 个网格**：网格 = `TownKit.Prim(Quad)`（内建四边形，全场一份）；
    ///      材质 = 一份透明材质（URP 粒子/Unlit 优先，逐级兜底，见 `MakeMaterial`）；
    ///      每个实例的**透明度与缩放**走 `MaterialPropertyBlock` ⇒ **没有 per-instance 材质** ✓
    ///   ② **池 24 个、预建、循环复用**：`Build` 时一次性建好并 `SetActive(false)`；
    ///      到点**回收**（失活回池），不 Destroy、不 Instantiate ✓
    ///   ③ **只在这个人真的在走时生成**：`Tick` 收 `moving`，静止 ⇒ 不生成（读数 `LiveCount == 0`）；
    ///      生成间隔 0.12~0.18 秒；每颗**停在生成点**（不跟随玩家）；
    ///      生命周期 0.4~0.6 秒：缩放 `1 → 1.6`、alpha `0.5 → 0` ✓
    ///
    /// ⚠️ 战斗侧（`Flow/**` / `Combat/**`）不许我改 ⇒ 这轮**只做主城**；战斗那边由另一路接同一个类
    ///    （只要在它的玩家脚下每帧调一次 `TownDust.Tick(pos, moving, dt)` 即可）✓
    /// </summary>
    public static class TownDust
    {
        /// <summary>池大小（预建个数）。</summary>
        public const int PoolSize = 24;

        /// <summary>生成间隔（秒）。</summary>
        public const float SpawnInterval = 0.15f;

        /// <summary>一颗尘土的存活时间（秒）。</summary>
        public const float Lifetime = 0.5f;

        /// <summary>起始/结束缩放（1 → 1.6）。</summary>
        public const float ScaleFrom = 1.0f, ScaleTo = 1.6f;

        /// <summary>起始/结束透明度（0.5 → 0）。</summary>
        public const float AlphaFrom = 0.5f, AlphaTo = 0f;

        /// <summary>尘土的基础尺寸（米）—— "大颗粒"。</summary>
        public const float BaseSize = 0.16f;

        private sealed class Puff
        {
            public Transform T;
            public MeshRenderer Mr;
            public float Age;
            public bool Active;
        }

        private static readonly Puff[] Pool = new Puff[PoolSize];
        private static Material _mat;
        private static Mesh _mesh;
        private static Transform _root;
        private static float _cooldown;
        private static int _cursor;
        private static MaterialPropertyBlock _mpb;

        /// <summary>当前活着（可见）的尘土数量 —— 静止时应当是 0。</summary>
        public static int LiveCount { get; private set; }

        /// <summary>上一帧有没有生成新的（读数用）。</summary>
        public static bool SpawnedLast { get; private set; }

        /// <summary>累计生成过多少颗（读数用）。</summary>
        public static int SpawnTotal { get; private set; }

        /// <summary>材质实际用的 shader 名（诊断：为空/null 就说明没建起来）。</summary>
        public static string ShaderName { get; private set; } = "(还没建)";

        /// <summary>把池建在 <paramref name="parent"/> 下（一次性；重复调用会直接返回）。</summary>
        public static void Build(Transform parent)
        {
            if (_root != null) return;
            _mpb = new MaterialPropertyBlock();
            _mat = MakeMaterial();
            _mesh = TownKit.Prim(PrimitiveType.Quad);
            Hlpr(parent);
            Log.Info($"[Town] 脚步灰尘：池 {PoolSize} 个 · 网格=Quad(共享) · 材质 1 份（shader={ShaderName}）· " +
                     $"生成间隔 {SpawnInterval:0.00}s · 生命 {Lifetime:0.00}s · 缩放 {ScaleFrom:0.0}→{ScaleTo:0.1} · " +
                     $"alpha {AlphaFrom:0.0}→{AlphaTo:0.0}");
        }

        private static void Hlpr(Transform parent)
        {
            var go = new GameObject("FX_FootDust");
            go.transform.SetParent(parent, false);
            _root = go.transform;

            for (int i = 0; i < PoolSize; i++)
            {
                var p = new GameObject("DUST_" + (i + 1).ToString("00"));
                p.transform.SetParent(_root, false);
                var mf = p.AddComponent<MeshFilter>();
                mf.sharedMesh = _mesh;
                var mr = p.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                p.SetActive(false);
                Pool[i] = new Puff { T = p.transform, Mr = mr, Age = 0f, Active = false };
            }
        }

        /// <summary>透明材质（整池共用 1 份）。URP 优先、逐级兜底；全都没有就退回 ToonLook 池。</summary>
        private static Material MakeMaterial()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null)
            {
                ShaderName = "(没有可用透明 shader ⇒ 退回 ToonLook 池)";
                return ToonLook.Get(new Color(0.82f, 0.78f, 0.70f, 0.5f), 0f, true);
            }
            ShaderName = sh.name;
            var m = new Material(sh) { name = "TownDustShared" };
            if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.82f, 0.78f, 0.70f, AlphaFrom));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.82f, 0.78f, 0.70f, AlphaFrom));
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);          // URP：Transparent
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);              // URP：Alpha
            m.renderQueue = 3000;
            return m;
        }

        /// <summary>
        /// 每帧调一次：<paramref name="moving"/> 为真且冷却到了才生成一颗，**生成点固定在脚下**；
        /// 同时推进所有活着的尘土的"放大 + 淡出"，到点回收。
        /// </summary>
        public static void Tick(Vector3 footPos, bool moving, float dt)
        {
            if (_root == null) return;
            if (dt <= 0f) return;

            SpawnedLast = false;
            _cooldown -= dt;
            if (moving && _cooldown <= 0f)
            {
                _cooldown = SpawnInterval;
                Spawn(footPos);
            }
            else if (!moving && _cooldown < 0f)
            {
                _cooldown = 0f;      // 站着：冷却不预支，起步反应不迟钝
            }

            int live = 0;
            for (int i = 0; i < PoolSize; i++)
            {
                Puff p = Pool[i];
                if (p == null || !p.Active) continue;
                p.Age += dt;
                float k = Mathf.Clamp01(p.Age / Lifetime);
                if (k >= 1f)
                {
                    p.Active = false;
                    p.T.gameObject.SetActive(false);     // 回收（不销毁）
                    continue;
                }
                live++;
                float s = BaseSize * Mathf.Lerp(ScaleFrom, ScaleTo, k);
                p.T.localScale = new Vector3(s, s, s);
                float a = Mathf.Lerp(AlphaFrom, AlphaTo, k);
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                p.Mr.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", new Color(0.82f, 0.78f, 0.70f, a));
                _mpb.SetColor("_BaseColor", new Color(0.82f, 0.78f, 0.70f, a));
                p.Mr.SetPropertyBlock(_mpb);
            }
            LiveCount = live;
        }

        private static void Spawn(Vector3 footPos)
        {
            for (int n = 0; n < PoolSize; n++)
            {
                int i = (_cursor + n) % PoolSize;
                Puff p = Pool[i];
                if (p == null || p.Active) continue;
                _cursor = (i + 1) % PoolSize;
                p.Active = true;
                p.Age = 0f;
                p.T.position = new Vector3(footPos.x, 0.06f, footPos.z);   // 固定在地上，**不跟随**
                p.T.rotation = Quaternion.Euler(90f, 0f, 0f);              // Quad 朝上
                p.T.localScale = Vector3.one * BaseSize;
                p.T.gameObject.SetActive(true);
                SpawnTotal++;
                SpawnedLast = true;
                return;
            }
        }

        /// <summary>读数：池大小 / 当前活着的 / 累计生成 / 材质 shader。</summary>
        public static string Probe()
        {
            string s = $"灰尘池 {PoolSize} 个｜当前可见 {LiveCount} 个｜累计生成 {SpawnTotal} 颗" +
                       $"｜间隔 {SpawnInterval:0.00}s · 生命 {Lifetime:0.00}s" +
                       $"｜缩放 {ScaleFrom:0.0}→{ScaleTo:0.1} · alpha {AlphaFrom:0.0}→{AlphaTo:0.0}" +
                       $"｜材质 1 份（shader={ShaderName}）· 网格 1 份（Quad）";
            Log.Info("[Town] DUST " + s);
            return s;
        }
    }
}
