using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// ★★ 主城的主角：**只有移动 + 不穿透建筑**，手上不持任何东西 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么不是复用 `WorldView` 里那个玩家
    /// ══════════════════════════════════════════════════════════════════════
    /// 战斗侧的玩家是 `ModelView` 实例 + `WorldView.AttachTools` 挂三把武器 + `Actors.cs` 里的
    /// `PlayerActor` 驱动（攻击/闪避/受击/死亡）。主城要的恰好是"把这些全摘掉"：
    ///   · 模型的**几何与姿态**直接复用 `PlayerBean`（用户点名的 `Art/PlayerBean`）✓
    ///   · **不调** `WorldView.AttachTools` / `PlayerBean.MountTool` ⇒ 手上永远是空的 ✓
    ///   · **不接**任何战斗输入（没有攻击、没有切武器、没有 Q/E 技能）✓
    /// ⇒ 所以这里是一个**独立的**移动器，而不是把战斗的玩家搬过来关几个开关。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、射线检测碰撞（★ 用户点名要的那一条 ★）
    /// ══════════════════════════════════════════════════════════════════════
    /// 用户原话：「玩家在主城加入**射线检测碰撞**，**禁止穿透主城所有建筑**」。
    ///
    /// 做法：**每帧一次 `Physics.CapsuleCast`（胶囊扫掠）**，而不是"给玩家挂 Rigidbody 让物理推"。
    ///   ① 为什么不挂 Rigidbody：主城没有重力、没有坡度、没有推挤 —— 挂上 Rigidbody 只会
    ///      把一个"查一下有没有墙"的问题变成"物理求解器会不会把我弹飞"的问题，
    ///      而且玩家会被建筑的**接触面法线**反推（贴着墙走会抖）。射线是**只读**的 ✓
    ///   ② 为什么是 Capsule 而不是 Ray：Ray 是一条线 —— 玩家是 1.1 米高的豆豆人，
    ///      一条线从脚底射出去会从门框、窗台、台阶之间**穿过去**，人却卡在墙里。
    ///      胶囊扫掠的半径 = 玩家身宽（0.34 米），扫过的就是"人能不能过去"这件事本身 ✓
    ///   ③ 命中之后**沿墙面滑动**（`Vector3.ProjectOnPlane`），贴着墙走不会停死 ——
    ///      这是"挡住"与"卡住"的区别，用户要的是前者 ✓
    ///   ④ 只查**建筑本体**：主城的 Collider 只在建筑零件上（见 `TownKit` 第三节），
    ///      草地 / 道路 / 田圃 / 作物 / 装饰模型都没有 Collider ⇒ 都能走上去 ✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 三、坐标系
    /// ══════════════════════════════════════════════════════════════════════
    /// 主城是俯视视角，镜头在 -Z 一侧朝 +Z 看（见 `TownBootstrap.BuildCamera`）。
    /// ⇒ 屏幕上的"上"就是世界 +Z，"右"就是世界 +X ⇒ `W` → +Z、`S` → -Z、`A` → -X、`D` → +X。
    /// 模型正面 = 本地 +Z（`PlayerBean` 的约定），所以 `yaw = atan2(dir.x, dir.z)`。
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 四、脚底高度
    /// ══════════════════════════════════════════════════════════════════════
    /// 路面与耕地都是 **0.04 米厚的薄板**（上表面 y ≈ 0.02），草地平面在 y = 0。
    /// 主角脚底固定在 `GroundY = 0.02` —— 走在草地上会"浮"2cm，但那 2cm 在俯视下
    /// 不到一个像素，换来的是**全程不需要贴地射线**（碰撞只查建筑）✓
    /// </summary>
    public sealed class TownAvatar
    {
        /// <summary>移动速度（米/秒）。比战斗侧慢一点 —— 主城是看和选的地方，不是跑位的地方。</summary>
        public const float WalkSpeed = 5.2f;

        /// <summary>脚底高度（= 路面 / 耕地上表面）。见类注释第四节。</summary>
        public const float GroundY = 0.02f;

        /// <summary>身体半径（米）。胶囊扫掠用这个半径 ⇒ "人能不能过去"由它决定。</summary>
        public const float BodyRadius = 0.34f;

        /// <summary>身体高度（米）。豆豆人本体约 1.2 米，取 1.10 让头顶不会顶到门楣。</summary>
        public const float BodyHeight = 1.10f;

        /// <summary>贴墙留的皮厚（米）：命中点再退 4 厘米，避免与墙面 z-fighting / 抖动。</summary>
        private const float Skin = 0.04f;

        /// <summary>转身速度（度/秒）。720 ⇒ 90° 约 0.13 秒，不拖影也不瞬转。</summary>
        private const float TurnDegPerSecond = 720f;

        /// <summary>活动半径（米，以城心为圆心的圆形边界，见 `TownLayout.WalkRadius`）。</summary>
        public float BoundsRadius = TownLayout.WalkRadius;

        public GameObject Go => _bean != null ? _bean.Go : null;

        public Vector3 Position { get; private set; }
        public float Yaw { get; private set; }
        public bool Moving { get; private set; }

        /// <summary>这一帧的位移有没有被建筑挡下（`TownStatus` 会报它）。</summary>
        public bool Blocked { get; private set; }

        /// <summary>最近一次被挡住的读数：撞到谁、什么形状、法线朝哪。</summary>
        public string LastHit { get; private set; } = "(还没撞过建筑)";

        /// <summary>累计被挡住的帧数（跑一段路之后用它证明"确实挡住了"，而不是恰好没走到）。</summary>
        public int BlockedFrames { get; private set; }

        private PlayerBean _bean;
        private ModelView.State _state = ModelView.State.Idle;
        private float _stateTime;

        /// <summary>建一个豆豆人。**不挂武器**（见类注释第一节）。</summary>
        public static TownAvatar Build(Transform parent, Vector3 spawn, float yaw)
        {
            var a = new TownAvatar();
            a._bean = PlayerBean.Build(parent);
            a._bean.Go.name = "TownPlayer";
            a.Position = new Vector3(spawn.x, GroundY, spawn.z);
            a.Yaw = yaw;
            a._bean.Go.transform.position = a.Position;
            a._bean.Go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // 静态摆一个待机姿态，避免第一帧是"半截 pose"
            a._bean.Pose(ModelView.State.Idle, 0f, 0.2f, 0f);
            return a;
        }

        /// <summary>
        /// 推一帧。<paramref name="move"/> 是已归一化的摇杆量（W/A/S/D 合成，见 `TownBootstrap.ReadMove`）。
        /// </summary>
        public void Tick(Vector2 move, float dt)
        {
            if (dt <= 0f) return;

            Vector2 m = Vector2.ClampMagnitude(move, 1f);
            Moving = m.sqrMagnitude > 1e-4f;
            Blocked = false;

            if (Moving)
            {
                Vector3 dir = new Vector3(m.x, 0f, m.y).normalized;
                float dist = WalkSpeed * dt;
                Vector3 next = MoveWithCollision(Position, dir, dist);

                // 读数：位移被削减过 ⇒ 这一帧确实被建筑挡了（不是"恰好走了这么远"）
                float moved = new Vector2(next.x - Position.x, next.z - Position.z).magnitude;
                if (moved < dist - 1e-3f)
                {
                    Blocked = true;
                    BlockedFrames++;
                }
                Position = next;

                // 朝向仍然按**输入方向**转（撞墙时角色面朝墙，符合直觉）
                float targetYaw = Mathf.Atan2(m.x, m.y) * Mathf.Rad2Deg;
                Yaw = Mathf.MoveTowardsAngle(Yaw, targetYaw, TurnDegPerSecond * dt);
            }

            // 圆形边界（整座城是一个圆，边界也是圆的 —— 用矩形会把四个角切出来）
            Vector2 flat = new Vector2(Position.x, Position.z);
            if (flat.sqrMagnitude > BoundsRadius * BoundsRadius)
                flat = flat.normalized * BoundsRadius;
            Position = new Vector3(flat.x, GroundY, flat.y);

            ModelView.State st = Moving ? ModelView.State.Walk : ModelView.State.Idle;
            if (st != _state) { _state = st; _stateTime = 0f; }
            _stateTime += dt;

            _bean.Go.transform.position = Position;
            _bean.Go.transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
            // walkRate：走 = 1（标准步频），站 = 0.2（`Pose` 内部会夹到 ≥0.15，站姿不会抖）
            _bean.Pose(st, _stateTime, Moving ? 1f : 0.2f, dt);

            // ★ v213：皮肤"实时改变"的**唯一挂钩点** ★
            //   `TownBootstrap.Update` 每帧调本方法一次；
            //   所以"点「穿上」⇒ 下一帧模型就变"这件事只能从这里发出去。
            //   开销：`Refresh` 第一行是比较一版号（int），没换装备的那一帧到此为止 ✓
            TownSkins.Refresh(_bean.Go);
        }

        /// <summary>
        /// 一帧的移动求解：胶囊扫掠 → 命中就退到命中点 → 沿墙面滑动 → 再扫一次（最多 2 段）。
        /// 返回这一帧**实际能到达**的位置（可能比 <paramref name="dist"/> 短）。
        /// </summary>
        private Vector3 MoveWithCollision(Vector3 start, Vector3 dir, float dist)
        {
            Vector3 p = start;
            float left = dist;

            for (int pass = 0; pass < 2 && left > 1e-4f; pass++)
            {
                Vector3 c1 = new Vector3(p.x, GroundY + BodyRadius, p.z);
                Vector3 c2 = new Vector3(p.x, GroundY + BodyHeight - BodyRadius, p.z);

                // ★ 这就是"射线检测碰撞"：一条扫掠的胶囊，只认建筑（建筑以外没有 Collider）★
                if (!Physics.CapsuleCast(c1, c2, BodyRadius, dir, out RaycastHit hit, left + Skin,
                                         ~0, QueryTriggerInteraction.Ignore))
                {
                    p += dir * left;
                    left = 0f;
                    break;
                }

                float allowed = Mathf.Max(0f, hit.distance - Skin);
                p += dir * allowed;
                left -= allowed;

                Vector3 slide = Vector3.ProjectOnPlane(dir, hit.normal);
                if (slide.sqrMagnitude < 1e-6f)
                {
                    // 正对着墙 ⇒ 停住（滑动方向退化成 0，再算下去就是原地空转）
                    LastHit = $"{hit.collider.name}（{ShapeOf(hit.collider)}）法线 ({hit.normal.x:0.00},{hit.normal.y:0.00},{hit.normal.z:0.00})";
                    left = 0f;
                    break;
                }
                dir = slide.normalized;
                LastHit = $"{hit.collider.name}（{ShapeOf(hit.collider)}）法线 ({hit.normal.x:0.00},{hit.normal.y:0.00},{hit.normal.z:0.00})";
            }

            p.y = GroundY;
            return p;
        }

        /// <summary>碰撞体的形状名（读数里要"贴合模型的形状"，不是一个笼统的"有碰撞"）。</summary>
        public static string ShapeOf(Collider c)
        {
            if (c == null) return "无";
            if (c is BoxCollider b)
                return $"Box {c.transform.lossyScale.x * b.size.x:0.00}×{c.transform.lossyScale.y * b.size.y:0.00}×{c.transform.lossyScale.z * b.size.z:0.00}";
            if (c is SphereCollider s)
                return $"Sphere r={s.radius * c.transform.lossyScale.x:0.00}";
            if (c is CapsuleCollider cap)
                return $"Capsule r={cap.radius:0.00} h={cap.height:0.00}";
            if (c is MeshCollider mc)
                return $"Mesh(convex={mc.convex}) tris={(mc.sharedMesh != null ? mc.sharedMesh.triangles.Length / 3 : 0)}";
            return c.GetType().Name;
        }

        /// <summary>
        /// 从 <paramref name="from"/> 朝 <paramref name="dir"/> 扫一段，返回"第一个撞到的东西"的读数。
        /// **与移动用的是同一条 CapsuleCast** ⇒ 这个读数是移动判定的直接证据，不是另写一套近似。
        /// </summary>
        public static string CastProbe(Vector3 from, Vector3 dir, float dist)
        {
            Vector3 c1 = new Vector3(from.x, GroundY + BodyRadius, from.z);
            Vector3 c2 = new Vector3(from.x, GroundY + BodyHeight - BodyRadius, from.z);
            bool hitAny = Physics.CapsuleCast(c1, c2, BodyRadius, dir.normalized, out RaycastHit hit, dist,
                                              ~0, QueryTriggerInteraction.Ignore);
            if (!hitAny) return "没撞到（可通行）";
            Vector3 stop = new Vector3(from.x, GroundY, from.z) + dir.normalized * Mathf.Max(0f, hit.distance - Skin);
            return $"{hit.collider.name}（{ShapeOf(hit.collider)}）距离 {hit.distance:0.00} 米 ⇒ 停在 ({stop.x:0.00},{stop.z:0.00})";
        }

        /// <summary>瞬移（自动化入口 `TownBootstrap.Teleport` 用；也是"开局摆位"的唯一写法）。</summary>
        public void Teleport(Vector3 p)
        {
            Position = new Vector3(p.x, GroundY, p.z);
            if (_bean != null && _bean.Go != null) _bean.Go.transform.position = Position;
        }

        /// <summary>把"被挡帧数"清零（每个碰撞用例开始前调一次，两次读数才有可比性）。</summary>
        public void ResetBlockedCounter()
        {
            BlockedFrames = 0;
            Blocked = false;
            LastHit = "(还没撞过建筑)";
        }

        /// <summary>当前朝向的单位向量（地面平面）。小地图的朝向刻线用它。</summary>
        public Vector2 Facing
        {
            get
            {
                float r = Yaw * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            }
        }
    }
}
