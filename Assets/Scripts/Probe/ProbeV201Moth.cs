using System.IO;
using System.Text;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ v201：**只读探针**（新 BOSS「飞蛾」三阶段 + 旧 BOSS 两个新招式）★★
    ///
    /// 它做四件事，全部是**读**（唯一副作用是往 `Docs/captures` 写 PNG）：
    ///   ① `State()`    —— 把场上那只 BOSS 的**运行时状态**报出来（形态 / 姿势 / 招式阶段 / 各计时器）
    ///   ② `Snap(name)` —— 用一台**临时相机**把 BOSS 渲染进 RenderTexture 再读回成 PNG
    ///                      （这就是"Focus+Snap（RenderTexture readback）"那一路：
    ///                       不依赖 Game 视图焦点、不 Screenshot、不会被 HUD 挡住 ——
    ///                       因为相机渲染**不包含** Screen Space - Overlay 的 UI ✓）
    ///   ③ `Nums()`     —— 三阶段全部数值（交付物①的机器可读版，直接从 `BossEnemy` 的常量读）
    ///   ④ `Schedule()` —— `BossSchedule` 的时刻表 / 种类 / 派生哈希，以及"不消费 World.Rng"的读数
    ///
    /// ★ 为什么探针要自己挂在场景里 ★
    /// 桥的 `/input send` 是"按名字找 GameObject，再反射调它的公开方法"。
    /// `DebugApi` 上**没有**"把相机对准 BOSS"的方法（那个文件不在本轮允许改动的清单里），
    /// 而 `[RuntimeInitializeOnLoadMethod]` 能在每次进播放模式时自动建一个这样的对象 ⇒
    /// 桥就能直接 `send path=MothProbe method=Snap` ✓ 不用碰任何既有文件。
    /// </summary>
    public sealed class ProbeV201Moth : MonoBehaviour
    {
        private const string CaptureDir = "Docs/captures";
        private static bool _booted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_booted) return;
            _booted = true;
            var go = new GameObject("MothProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<ProbeV201Moth>();
        }

        private static RunSession Session
            => FindAnyObjectByType<GameBootstrap>(FindObjectsInactive.Include)?.Session;

        private static EnemyActor FirstBoss()
            {
                RunSession s = Session;
                if (s == null || s.World == null) return null;
                World w = s.World;
                EnemyActor best = null;
                for (int i = 0; i < w.Enemies.Count; i++)
                {
                    EnemyActor e = w.Enemies[i];
                    if (e == null || e.Def == null || e.Def.Kind != EnemyKind.Boss) continue;
                    if (!e.Alive || e.Health.Hp <= 0f) continue;      // ★ 尸体不算：`KillAll` 之后它还在列表里 ★
                    // 多只时取血最多的那只（新刷的那只通常满血）
                    if (best == null || e.Health.Hp > best.Health.Hp) best = e;
                }
                return best;
            }

        // ── ① 运行时状态 ────────────────────────────────────────────────────
        public string State()
        {
            EnemyActor e = FirstBoss();
            if (e == null) return "场上没有 BOSS";
            var sb = new StringBuilder();
            sb.Append("BOSS ").Append(e.Def.Id).Append('#').Append(e.Id)
              .Append("｜血 ").Append(e.Health.Hp.ToString("0.0")).Append('/')
              .Append(e.Health.MaxHp.ToString("0.0"))
              .Append("（").Append(e.Health.HpPct.ToString("P0")).Append("）")
              .Append("｜位置 ").Append(e.Pos.x.ToString("0.00")).Append(',')
              .Append(e.Pos.y.ToString("0.00"));
            if (e is BossEnemy b)
            {
                sb.Append("｜姿势 ").Append(b.CastPose).Append("（age ").Append(b.CastAge.ToString("0.00")).Append("s）");
                sb.Append("｜不可选中 ").Append(b.Untargetable);
                if (b.IsMoth)
                {
                    sb.Append("｜形态 ").Append(b.MothPhaseIndex)
                      .Append("（").Append(FormName(b.MothPhaseIndex)).Append('）')
                      .Append("｜动作 ").Append(b.MothActName)
                      .Append("｜消失 ").Append(b.MothHidden)
                      .Append("｜连冲剩 ").Append(b.MothDashLeft)
                      .Append("｜弹幕 ").Append(b.MothVolleys).Append(" 波")
                      .Append("｜激光 ").Append(b.MothLasers).Append(" 次")
                      .Append("｜跳砸 ").Append(b.MothSlams).Append(" 次")
                      .Append("｜换形态 ").Append(b.MothFormSwitches).Append(" 次");
                }
                else
                {
                    sb.Append("｜吼叫剩 ").Append(b.RoarRingsLeft).Append(" 环")
                      .Append("（已发 ").Append(b.RoarRingsFired).Append("）")
                      .Append("｜蓄力剩 ").Append(b.BlastWindupRemaining.ToString("0.00")).Append("s")
                      .Append("｜已爆炸 ").Append(b.BlastCount).Append(" 次");
                }
            }
            return sb.ToString();
        }

        private static string FormName(int phase)
            => phase <= 0 ? "毛毛虫" : (phase == 1 ? "蛹" : "飞蛾");

        // ── ② RenderTexture 抓图（Focus+Snap）──────────────────────────────
        /// <summary>
        /// `spec` = `名字` 或 `名字,距离,高度,俯角`（默认 7.5 / 2.6 / 14）。
        /// 相机对准**那只 BOSS 的胸口高度**，绕它一圈取一个 3/4 侧后视角。
        /// </summary>
        public string Snap(string spec)
        {
            EnemyActor e = FirstBoss();
            if (e == null) return "场上没有 BOSS";
            string[] parts = (spec ?? string.Empty).Split(',');
            string name = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : "snap";
            float dist = parts.Length > 1 ? ParseF(parts[1], 8.5f) : 8.5f;
            float height = parts.Length > 2 ? ParseF(parts[2], 3.2f) : 3.2f;
            float pitch = parts.Length > 3 ? ParseF(parts[3], 9f) : 9f;

            const int W = 1280, H = 720;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2 };
            var go = new GameObject("MothProbeCam");
            Camera cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            Vector3 focus = new Vector3(e.Pos.x, 0.9f, e.Pos.y);
            // ★ 机位取景：**从玩家那一侧的反方向**看过来（远景里玩家不会挡在 BOSS 前面）★
            RunSession ses = Session;
            Vector2 away = Vector2.up;
            if (ses != null && ses.World != null && ses.World.Player != null)
            {
                Vector2 d2 = e.Pos - ses.World.Player.Pos;
                if (d2.sqrMagnitude > 0.01f) away = d2.normalized;
            }
            Vector3 dir = new Vector3(away.x, 0f, away.y);
            cam.transform.position = focus + dir * dist + Vector3.up * height;
            cam.transform.LookAt(focus, Vector3.up);
            cam.transform.Rotate(pitch, 0f, 0f);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;

            string dirPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", CaptureDir));
            Directory.CreateDirectory(dirPath);
            string file = Path.Combine(dirPath, name + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            Destroy(rt);
            Destroy(go);

            string info = "SNAP " + file + "｜" + State();
            Log.Info(info);
            return info;
        }

        private static float ParseF(string raw, float fallback)
            => float.TryParse((raw ?? string.Empty).Trim(), System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;

        // ── ③ 三阶段数值（交付物①）─────────────────────────────────────────
        public string Nums()
        {
            EnemyDef d = TableDatabase.GetEnemy("moth");
            var sb = new StringBuilder();
            sb.AppendLine("【飞蛾 BOSS 三阶段数值】" + (d != null ? $"（表：{d.Name}｜baseHp {d.BaseHp}｜半径 {d.Radius}｜jumpRadius {d.JumpRadius}）" : "（表里没有 moth）"));
            sb.AppendLine($"① 毛毛虫（HP > {BossEnemy.MothLarvaAbove:P0}）：爬行 moveSpeed {d?.MoveSpeed} m/s｜"
                        + $"冲刺 {BossEnemy.LarvaDashCount} 次 × 速度 {(d != null ? d.MoveSpeed * BossEnemy.LarvaDashSpeedMul : 0f):0.0} m/s"
                        + $"（前摇 {BossEnemy.LarvaDashWindup}s / 冲 {BossEnemy.LarvaDashSeconds}s / 间隔 {BossEnemy.LarvaDashGap}s / 冷却 {BossEnemy.LarvaDashCooldown}s）｜"
                        + $"弹幕 {BossEnemy.LarvaBarrageCount} 发/{BossEnemy.LarvaBarrageEvery}s 扇形 {BossEnemy.LarvaBarrageSpreadDeg:0}°");
            sb.AppendLine($"② 蛹（{BossEnemy.MothPupaAbove:P0} ~ {BossEnemy.MothLarvaAbove:P0}）：**不能移动**｜"
                        + $"持续弹幕 {BossEnemy.PupaBarrageCount} 发/{BossEnemy.PupaBarrageEvery}s（360° 螺旋）｜"
                        + $"激光 预警 {BossEnemy.PupaLaserWarnSeconds}s（末 {BossEnemy.PupaLaserLockTail}s 锁定）→ 落下 {BossEnemy.PupaLaserFallSeconds}s → 灼烧 {BossEnemy.PupaLaserBurnSeconds}s（每 {BossEnemy.PupaLaserBurnTick}s 一跳 ×{BossEnemy.PupaLaserBurnDamageRatio}）每 {BossEnemy.PupaLaserEvery}s 一次");
            sb.AppendLine($"③ 飞蛾（HP < {BossEnemy.MothPupaAbove:P0}）：弹幕 {BossEnemy.MothBarrageCount} 发/{BossEnemy.MothBarrageEvery}s｜"
                        + $"消失 {BossEnemy.MothVanishSeconds}s（不可选中 → 位置不动）→ 跳砸 前摇 {BossEnemy.MothSlamWindup}s 半径 {(d != null ? d.JumpRadius : 0f):0.0}m 伤害 ×{BossEnemy.MothSlamDamageRatio}，每 {BossEnemy.MothVanishEvery}s 一次｜"
                        + $"冲刺 {BossEnemy.MothDashCount} 次 × 速度 {(d != null ? d.MoveSpeed * BossEnemy.MothDashSpeedMul : 0f):0.0} m/s"
                        + $"（前摇 {BossEnemy.MothDashWindup}s / 冲 {BossEnemy.MothDashSeconds}s / 间隔 {BossEnemy.MothDashGap}s / 冷却 {BossEnemy.MothDashCooldown}s）");
            sb.AppendLine($"旧 BOSS ①吼叫：{BossEnemy.RoarRingCount} 环 × {BossEnemy.RoarRingInterval}s（每环 {BossEnemy.RoarBulletsPerRing} 发整圈，起手 {BossEnemy.RoarWindup}s）");
            sb.AppendLine($"旧 BOSS ②蓄力爆炸：蓄力 {BossEnemy.BlastChargeSeconds}s（= 预警圈时长）→ 半径 {BossEnemy.BlastRadius}m，伤害 = 跳砸 × {BossEnemy.BlastDamageMul}");
            if (d != null)
            {
                sb.AppendLine($"第 20 分钟（bossIndex 4）：血 {d.HpForWave(20, 4):0}｜弹幕 {d.ProjectileDamagePoints(20)} 血点｜跳砸 {d.JumpDamagePoints(20)} 血点｜接触 {d.TouchDamagePoints(20)} 血点");
            }
            return sb.ToString();
        }

        // ── ④ 时刻表 + 派生哈希 ────────────────────────────────────────────
        public string Schedule()
        {
            var sb = new StringBuilder();
            var sched = new BossSchedule();
            var clk = new Clock();
            sched.Reset(clk);
            sb.AppendLine("【BossSchedule（改后）】第 1 次 " + BossSchedule.FirstBossMinute + " 分（旧版）→ 第 2 次 "
                        + BossSchedule.SecondBossMinute + " 分（飞蛾）→ 之后随机 "
                        + BossSchedule.RandomGapMinMinutes + "~" + BossSchedule.RandomGapMaxMinutes + " 分（步长 "
                        + BossSchedule.RandomGapStepMinutes + "）");
            sb.AppendLine("时刻表预览（10 次）：" + sched.Preview(10, clk));
            sb.Append("种类序列：");
            for (int i = 1; i <= 10; i++)
            {
                if (i > 1) sb.Append(" → ");
                sb.Append(BossSchedule.IdOf(i)).Append('(').Append(BossSchedule.KindOf(i)).Append(')');
            }
            sb.AppendLine();
            sb.Append("派生哈希 DerivedHash(i)（i=1..10）：");
            for (int i = 1; i <= 10; i++)
            {
                if (i > 1) sb.Append(' ');
                sb.Append(BossSchedule.DerivedHash(i).ToString("x8"));
            }
            sb.AppendLine();
            // "不消费 World.Rng"的运行期读数：同一个种子，跑过 10 次刷新之后输出必须一致
            var a = new Pcg32(987654UL);
            var b = new Pcg32(987654UL);
            var c2 = new Clock();
            var s2 = new BossSchedule();
            s2.Reset(c2);
            for (int i = 0; i < 10; i++) { c2.Tick(60f); s2.OnSpawned(c2); }
            bool same = true;
            var outs = new StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                uint x = a.NextUInt(), y = b.NextUInt();
                if (x != y) same = false;
                if (i > 0) outs.Append(' ');
                outs.Append(x.ToString("x8"));
            }
            sb.Append("World.Rng 未被动过：").Append(same)
              .Append("（10 次刷新后 Pcg32 输出 ").Append(outs).Append("，对照组逐位相同）");
            return sb.ToString();
        }

        // ── ⑤ 三形态几何（证明三套模型真的不同）────────────────────────────
        public string Forms()
        {
            var sb = new StringBuilder();
            for (int f = 0; f < 3; f++)
            {
                ProceduralBugs.Shape s = ProceduralBugs.MothShapeOf("moth", f);
                sb.Append(FormName(f)).Append("：物种 ").Append(s.Kind)
                  .Append("｜unit ").Append(s.Unit.ToString("0.00"))
                  .Append("｜体长 ").Append(s.BodyLen.ToString("0.00")).Append('m')
                  .Append("（头 ").Append(s.HeadLen.ToString("0.00"))
                  .Append("/胸 ").Append(s.ThoraxLen.ToString("0.00"))
                  .Append("/腹 ").Append(s.AbdomenLen.ToString("0.00")).Append("）")
                  .Append("｜腹宽 ").Append(s.AbdomenR.ToString("0.00"))
                  .Append("｜腿 ").Append(s.LegLen.ToString("0.00")).Append('m')
                  .Append("｜翅 ").Append(s.WingLen.ToString("0.00")).Append('m')
                  .Append("｜触角 ").Append(s.AntennaLen.ToString("0.00")).Append('m')
                  .Append("｜腿频 ").Append(s.LegFreq.ToString("0.0")).Append("Hz")
                  .AppendLine();
            }
            sb.Append("网格统计（缓存键 id|形态，全场每形态只建一次）：");
            for (int f = 0; f < 3; f++)
            {
                sb.Append(' ').Append(FormName(f)).Append('=').Append(ProceduralBugs.MothMeshStats(f));
            }
            return sb.ToString();
        }
    }
}
