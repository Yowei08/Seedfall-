using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 虫怪：**程序化生成的虫形网格 + 关节动画**（v181）。
    ///
    /// 它取代 `WorldView.EnemyModelId()` 那一套 FBX 映射（`imp` / `imp_archer` / `brute` / `pig`，
    /// BOSS 干脆没有模型、退回一个立方体）⇒ 现在**每一种怪物都是一只虫** ✓
    ///
    /// ── 为什么运行时构造写在 `Assets/` 里，而不是 `Tools/mesh_gen/` ──
    /// 与 `Tools/mesh_gen` 那一套特效网格**同一条分工**（照既有先例做）：
    ///   · `Assets/Scripts/Model/ProceduralBugs.cs` = 运行时构造（唯一能被 Unity / csproj 编译的位置）
    ///   · `Tools/mesh_gen/gen_bug_meshes.py`       = 同一套拓扑的**离线权威副本**（出 OBJ 给人看）
    ///   · `Tools/mesh_gen/verify_bug_meshes.py`    = 把两边**钉在一起**：C# 改了、这边没跟上就 FAIL
    /// 本文件里标了 `=== 权威表 ===` 的常数与公式，Python 侧逐字抄了一遍 ⇒ 改一边必须改另一边 ✓
    ///
    /// ── 三条硬约束（都来自需求，也都有对应的检查）──
    /// ① 每种虫一个虫形网格：头 / 胸 / 腹三段体节（每段 6~8 面）+ 6 条腿（三对）+ 一对触角，
    ///    全部 **flat shading**（每个三角形独占 3 个顶点 ⇒ `RecalculateNormals` 得到逐面法线）✓
    /// ② 形状按 **id 的 crc32** 派生 ⇒ 同一 id 每次生成同一只虫，不同 id 长得不一样 ✓
    /// ③ **禁止每只虫 `new Material`** —— 材质一律走 `ToonLook.Get(...)`（已有的按颜色缓存池）；
    ///    网格一律走 `_assets` 缓存（每种虫只建一次，全局共用）✓
    /// </summary>
    public static class ProceduralBugs
    {
        // ══════════════════════════════════════════════════════════════════════
        // 一、确定性：crc32 + xorshift32
        //
        // ★ 用自带的两个小函数，不用 `UnityEngine.Random` ★
        //   网格是**缓存**的：第一次生成的结果会被全局共用 ⇒ 生成过程必须可重放。
        //   `Random` 有全局状态，谁先调它谁影响别人 ⇒ 同一 id 在不同战局里会长得不一样 ✗
        //   离线副本（Python）也必须算出**同一个**结果 ⇒ 两个函数都选了最短的可移植实现 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>标准 CRC-32（IEEE 802.3，poly 0xEDB88320，init/xorout 全 1）。按 **UTF-8 字节** 计算。</summary>
        public static uint Crc32(string text)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < bytes.Length; i++)
            {
                crc ^= bytes[i];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1u) != 0u ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>xorshift32 —— 参数派生的唯一随机源（与 Python 侧逐位一致）。</summary>
        public struct Rng32
        {
            private uint _s;
            public Rng32(uint seed) { _s = seed == 0u ? 0x9E3779B9u : seed; }
            public uint Next()
            {
                uint x = _s;
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                _s = x;
                return x;
            }
            /// <summary>[0,1) —— 取高 24 位，避开低位的相关性。</summary>
            public float Next01() => (Next() >> 8) * (1f / 16777216f);
        }

        // ══════════════════════════════════════════════════════════════════════
        // 二、=== 权威表 === 形状参数（按 id 派生）
        //
        // 每个量都只由 `Crc32(id)` 决定 ⇒ 同一 id 恒定、不同 id 不同 ✓
        // `Unit` 是**体型单位**：BOSS = 2.5、其余 = 1.0 ⇒ BOSS 恒为普通虫的 **2.5 倍** ✓
        // `uN` 是第 N 次 `Next01()`（取值顺序固定，Python 侧同序）。
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>BOSS 相对普通虫的体型倍数（需求：约 2.5 倍）。</summary>
        public const float BossUnit = 2.5f;

        /// <summary>
        /// ★ v196：**基础兵 `grunt` 的体型系数** ★ —— 用户原话：「制作一个更小的虫类（体型更小）」
        ///
        /// ── 为什么是"一个乘数"，而不是重写一套锹形虫几何 ──
        /// 轮廓（宽胸 / 长头 / 肥腹 / 大颚钳 / 鞘翅 / 棒状触角 / 6 条三段腿）是上一轮刚立起来的
        /// 可辨识特征，这一轮要的只是**更小** ⇒ 重写几何等于把刚定的比例重新掷骰子 ✗
        /// 而 `ShapeOf` 里**每一个**长度 / 半径 / 体轴高度都是 `基数 × unit × 抖动`（共 18 个式子）
        /// ⇒ 把 `unit` 从 1 换成 0.72，就得到**整只虫等比缩小 28%**，比例一个都不变 ✓
        ///   · 体节 / 腿 / 触角 / 大颚 / 鞘翅**一起**缩 ⇒ 不会出现"身子缩了腿没缩"的错位 ✓
        ///   · `Shape.Unit` 字段跟着变成 0.72 ⇒ 动画里所有 `s.Unit * 常数`（攻击前冲、死亡下沉、
        ///     走路起伏）与霓虹描边宽度也一起缩 ⇒ **相对观感不变** ✓
        ///   · `Sides / Carapace / Horn / TailSpike / Hue / LegFreq` **一个都不动**：
        ///     它们不是长度 ⇒ 面数不变（拓扑与顶点数逐位不变）、步频不变 ✓
        ///
        /// ── 0.72 是怎么定的 ──
        /// 改前世界包围盒（验收台 `Focus` 读数，已含 `CharScale 2.6`）= **高 2.01m × 跨 2.91m**
        /// （高度是**触角梢**顶起来的，不是体节 ⇒ 这一只的"看着多大"由触角决定 ✓）
        /// 对一只"基础兵"来说这已接近小怪里的天花板 ✗
        /// 取 0.72 ⇒ 高 **1.45m** × 跨 **2.10m**：仍明显大于玩家、又给 BOSS 留出 3.47 倍的落差 ✓
        /// ★ `enemies.json` 的 `radius 0.45` **不动**（理由见交付报告）★ ——
        ///   缩完的体宽 0.79m 刚好落在直径 0.9m 的碰撞圆内，比改前（体宽 1.09m 溢出圆外）更贴 ✓
        /// </summary>
        public const float StagScale = 0.72f;

        /// <summary>
        /// ★ v191：**物种**（用户要求"设计多种不同的昆虫类怪物"）★
        ///
        /// 为什么是一个显式枚举而不是继续让 crc 掷骰子决定外形：
        ///   · 需求要的是**可辨识的轮廓**——「锹形虫 / 蛾 / 蚱蜢 / 瓢虫」各自有名字、各自有身体构成
        ///     （头胸腹比例、腿的数量与形态、翅膀的数量与形状全都不同）✓
        ///   · 交给 crc 会得到"同一只虫换了个参数"，那正是需求要否掉的 ✗
        ///   · 物种由 **id** 决定 ⇒ 同一 id 永远是同一种虫（可重放）✓
        ///
        /// ★ `Giant` 是 BOSS：它**继续走 v181 那一整套几何**（三段体节 + 鞘翅 + 大颚 + 头角 + 尾刺），
        ///   本轮一个字节都不动（用户原话："boss 暂时不调整"）★ ⇒ 它的形状参数仍只由 u0..u16 派生，
        ///   新增的物种参数走**第二条随机流**，不改变 u0..u16 的取值顺序 ⇒ BOSS 逐位不变 ✓
        /// </summary>
        public enum Species
        {
            /// <summary>基础兵 `grunt` = **锹形虫**（大颚钳 + 短棒状触角 + 鞘翅）。</summary>
            Stag = 0,
            /// <summary>远程兵 `shooter` = **蛾**（4 片翅 + 羽状触角 + 口器，无鞘翅无大颚）。</summary>
            Moth = 1,
            /// <summary>跳跃者 `jumper` = **蚱蜢**（4 条小步足 + 2 条巨大后腿 + 复眼 + 小覆翅）。</summary>
            Hopper = 2,
            /// <summary>稀有猪 `pig` = **瓢虫**（半球壳 + 极短腿 + 小头）。★ 场上仍走旧版 FBX，见 `_pigs` ★</summary>
            Ladybug = 3,
            /// <summary>BOSS = **巨型甲虫**：v181 原样，本轮不动。</summary>
            Giant = 4,

            // ══════════════════════════════════════════════════════════════════
            // ★ v201：**飞蛾 BOSS 的三种形态**（一个 BOSS，三段外形）★
            //
            // 为什么不复用 `Species.Moth`（那是远程兵 `shooter`）：
            //   三者要**一眼可辨**（需求："毛毛虫（节肢/多足）、蛹（纺锤/丝）、飞蛾（大翅/羽毛触角）"），
            //   而 `Moth` 是"普通大小的蛾"，和 BOSS 蛾差 2.4 倍体型 + 两对更大的翅 ⇒ 必须是两个物种 ✓
            //
            // ★ 关键：这三个物种**不参与 `ShapeOf`** ★
            //   形状由 `MothShapeOf(id, form)` 单独派生（第三条随机流 `Crc32(id+"#moth"+form)`）
            //   ⇒ `ShapeOf` 的 u0..u16 / w0..w7 取值顺序**一个都没动** ⇒ 既有 5 个 id 逐位不变 ✓
            //   ⇒ `gen_bug_meshes.py` 的参数钉（`PINNED_METHODS` = ShapeOf/HipHeight/HipOf）
            //     与 `boss_frozen.txt` 的 sha256（`5ddccf30262f72b1`）自然都不受影响 ✓
            // ══════════════════════════════════════════════════════════════════

            /// <summary>飞蛾 BOSS 第一形态：**毛毛虫**（长腹 + 6 条短多节腿 + 短触角，贴地）。</summary>
            Larva = 5,
            /// <summary>飞蛾 BOSS 第二形态：**蛹**（纺锤形壳 + 背面的丝壳，腿缩成残足）。</summary>
            Pupa = 6,
            /// <summary>飞蛾 BOSS 第三形态：**飞蛾**（两对大翅 + 羽状触角 + 悬停）。</summary>
            MothQueen = 7,
        }

        /// <summary>id → 物种。★ BOSS 优先判 ★ 其余由 id 决定（纯函数）。</summary>
        public static Species SpeciesOf(string id, bool boss)
        {
            // ★ v201：飞蛾 BOSS —— **必须先判** `moth`（它在数据表里 `kind = Boss`，
            //   走 `if (boss) return Giant` 那一支就会变成"巨型甲虫" ✗）
            if (id == "moth") return Species.MothQueen;
            if (boss) return Species.Giant;
            if (id == "pig") return Species.Ladybug;
            if (id == "shooter") return Species.Moth;
            if (id == "jumper") return Species.Hopper;
            return Species.Stag;
        }

        /// <summary>
        /// ★ v201：飞蛾 BOSS 的**形态号 → 物种**。0=毛毛虫 1=蛹 2=飞蛾（由 `BossEnemy.MothPhaseOf` 给）。
        /// ★ 唯一实现 ★ —— 表现层（`SyncEnemies`）与探针/截图都读它，不许各写一份映射。
        /// </summary>
        public static Species MothFormOf(int phase)
            => phase <= 0 ? Species.Larva : (phase == 1 ? Species.Pupa : Species.MothQueen);

        /// <summary>★ v201：这三个物种是不是"飞蛾 BOSS 的某一形态"（姿势叠加 / 换模型都用它判）。</summary>
        public static bool IsMothForm(Species kind)
            => kind == Species.Larva || kind == Species.Pupa || kind == Species.MothQueen;

        /// <summary>一只虫的全部形状参数（模型空间，单位米）。</summary>
        public struct Shape
        {
            public string Id;
            public bool Boss;
            public Species Kind;        // v191：物种（BOSS = Giant）
            public float Unit;          // 体型单位（1 或 2.5）
            public float HeadLen;       // 头节长度
            public float ThoraxLen;     // 胸节长度
            public float AbdomenLen;    // 腹节长度
            public float HeadR;         // 头节半径
            public float ThoraxR;       // 胸节半径
            public float AbdomenR;      // 腹节最粗处半径（"胖瘦"就在这里）
            public float LegLen;        // 腿的水平伸展
            public float LegThick;      // 腿根半径
            public float AntennaLen;    // 触角长度
            public float BodyY;         // 体轴离地高度
            public int Sides;           // 体节面数：6 或 8
            public bool Carapace;       // 有没有背甲
            public bool Horn;           // 有没有头角
            public bool TailSpike;      // 有没有尾刺
            public float Hue;           // [0,1) 色相偏移
            public float LegFreq;       // 腿频（Hz，4~8）

            // ── ★ v191：物种专属比例（第二条随机流派生 ⇒ 不影响上面任何一项，BOSS 逐位不变）──
            public float WingLen;       // 蛾：前翅长
            public float WingSpan;      // 蛾：前翅弦长（宽）
            public float HindLen;       // 蚱蜢：后腿股节长
            public float HindThick;     // 蚱蜢：后腿股节粗
            public float DomeR;         // 瓢虫：半球壳半径
            public float DomeH;         // 瓢虫：半球壳高
            public float PincerLen;     // 锹形虫：大颚长
            public float EyeR;          // 蚱蜢：复眼半径

            public float BodyLen => HeadLen + ThoraxLen + AbdomenLen;
        }

        /// <summary>按 id 派生形状。**纯函数**：同一组入参永远同一结果。</summary>
        public static Shape ShapeOf(string id, bool boss)
        {
            string key = id ?? "bug";
            // ★ v196：物种**先算出来** —— 它决定几何乘数（见 `StagScale`）★
            Species kind = SpeciesOf(key, boss);
            // 几何乘数 = 体型单位：BOSS `2.5` ／ 其余 `1` ／ **基础兵 `0.72`**（"更小的虫类"）
            // ★ 这一行是本轮唯一改动的算式 ⇒ BOSS 走 `boss ? BossUnit` 那一支，
            //   得到的仍是**逐位相同**的 `2.5f` ⇒ 它下面 18 个式子一个字节都没变 ✓
            float unit = boss ? BossUnit : (kind == Species.Stag ? StagScale : 1f);
            var r = new Rng32(Crc32(key));
            float u0 = r.Next01(), u1 = r.Next01(), u2 = r.Next01(), u3 = r.Next01(), u4 = r.Next01();
            float u5 = r.Next01(), u6 = r.Next01(), u7 = r.Next01(), u8 = r.Next01(), u9 = r.Next01();
            float u10 = r.Next01(), u11 = r.Next01(), u12 = r.Next01(), u13 = r.Next01();
            float u14 = r.Next01(), u15 = r.Next01(), u16 = r.Next01();

            // ★ v191：物种专属比例走**第二条流**（`Crc32(key) ^ 常数`）★
            //   为什么另起一条：上面 u0..u16 的**取值顺序**决定 BOSS 的每一个数 ⇒
            //   在这条流里多取一次，BOSS 的 9 个体节尺寸就会全变 ✗（那就等于"动了 BOSS"）
            var r2 = new Rng32(Crc32(key) ^ 0x91E3BEEFu);
            float w0 = r2.Next01(), w1 = r2.Next01(), w2 = r2.Next01(), w3 = r2.Next01();
            float w4 = r2.Next01(), w5 = r2.Next01(), w6 = r2.Next01(), w7 = r2.Next01();

            return new Shape
            {
                Id = key,
                Boss = boss,
                Kind = kind,
                Unit = unit,          // ★ v196：基础兵 = 0.72（几何乘数本身就是"体型倍数"）
                // ── 体节长度比（三段各自独立取 ⇒ 每只虫的三段比例都不同）──
                HeadLen = 0.130f * unit * (0.85f + 0.45f * u0),
                ThoraxLen = 0.250f * unit * (0.85f + 0.45f * u1),
                AbdomenLen = 0.320f * unit * (0.75f + 0.95f * u2),
                // ── 粗细 / 腹部胖瘦（★ 体量要够：太细就只剩"腿"，三段体节读不出来）──
                HeadR = 0.095f * unit * (0.85f + 0.45f * u3),
                ThoraxR = 0.128f * unit * (0.85f + 0.45f * u4),
                AbdomenR = 0.140f * unit * (0.80f + 0.50f * u5) * (0.75f + 0.55f * u6),
                // ── 腿长 / 腿粗 / 触角（★ 腿长**短于**体长：甲虫式而不是蜘蛛式）──
                LegLen = 0.215f * unit * (0.80f + 0.60f * u7),
                LegThick = 0.030f * unit * (0.80f + 0.60f * u8),
                AntennaLen = 0.180f * unit * (0.70f + 0.90f * u9),
                // ── 体轴高度（腿要把脚落到 y=0，所以它是"腿根高度 ÷ 0.55"）──
                BodyY = 0.320f * unit * (0.85f + 0.35f * u10),
                // ── 每段面数：6 或 8（需求"每段 6~8 面"）──
                Sides = u11 > 0.55f ? 8 : 6,
                // ── 有无甲壳 / 角 / 尾刺：三个独立特征 ⇒ 组合出 8 种外形 ──
                //   ★ BOSS 强制有背甲与头角 ★ —— 它是"巨型甲虫"（大颚 + 两片鞘翅 + 头角），
                //   这三样是规格指定的，不能交给 crc 去掷骰子 ✓
                Carapace = boss || u12 < 0.50f,
                Horn = boss || u13 < 0.40f,
                TailSpike = u14 < 0.45f,
                Hue = u15,
                // ── 腿频：4~8 Hz（需求给的范围），由 id 决定 ⇒ 每只虫步频不同 ──
                LegFreq = 4f + 4f * u16,

                // ── ★ v191：物种专属比例（只对非 BOSS 有意义；BOSS 不读它们）──
                WingLen = 0.46f * unit * (0.90f + 0.20f * w0),
                WingSpan = 0.30f * unit * (0.85f + 0.30f * w1),
                HindLen = 0.40f * unit * (0.88f + 0.24f * w2),
                HindThick = 0.052f * unit * (0.85f + 0.30f * w3),
                DomeR = 0.285f * unit * (0.90f + 0.20f * w4),
                DomeH = 0.235f * unit * (0.90f + 0.20f * w5),
                PincerLen = 0.215f * unit * (0.85f + 0.30f * w6),
                EyeR = 0.050f * unit * (0.85f + 0.30f * w7),
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        // ★★ v201：飞蛾 BOSS 的**三种形态形状**（第三条随机流）★★
        //
        // ── 为什么另起一个函数，而不是往 `ShapeOf` 里加分支 ──
        // `ShapeOf` 的数值字面量被 `Tools/mesh_gen/gen_bug_meshes.py` 的
        // `PINNED_METHODS`（= ShapeOf / HipHeight / HipOf）**逐个钉住**，
        // 而那份清单是 `verify_bug_meshes.py --check` 的判据之一 ⇒
        // 在里面加一个 `0.24f` 都会让校验点名 FAIL（"C# 改了：多了 ['0.24']"）✗
        // ⇒ 新形态的全部常数住在这里：**新方法、新字面量**，与那三条钉无关 ✓
        //
        // ── 三种形态的几何分工（需求：毛毛虫=节肢/多足，蛹=纺锤/丝，飞蛾=大翅/羽毛触角）──
        //   毛毛虫：**长腹 + 粗腹 + 6 条短而多节的腿**（腿频 9Hz ⇒ 窸窸窣窣地爬）；
        //           `BodyMesh` 的三段体节在"腹部拉长到 0.95×unit"时读起来就是毛毛虫的分节 ✓
        //   蛹    ：**纺锤**（腹宽 0.40 > 胸宽 0.22，头尾都收）+ **背壳当丝茧**（`Carapace = true`
        //           ⇒ `AccentMesh` 会在背上盖一片壳）；腿缩成 0.09 的残足、不移动 ✓
        //   飞蛾  ：沿用 `MothBody / MothWing / MothAntenna`（那是远程兵蛾已经验证过的一套），
        //           但 `unit = 2.4` ⇒ **同一套比例放大 2.4 倍**，再加两片更大的前翅 ✓
        //
        // ── 髋圈（腿根位置）为什么不用 `DomeR` ──
        // 飞蛾三形态的 `Boss = true` ⇒ `BugView.Build` 走 `LegHip(s, i)`（BOSS 那条），
        // 它只用 `ThoraxLen / ThoraxR / BodyY` —— 这三个字段三种形态都设了 ⇒ 腿位正确 ✓
        // （`HipOf` 的 `default` 分支用 `DomeR`，那条只服务非 BOSS；本轮**没有**改 `HipOf`
        //  的 switch —— 那会改它的数值字面量集合，`gen_bug_meshes.py` 的参数钉会点名 FAIL ✗）
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>飞蛾 BOSS 三形态的体型单位（与 `ShapeOf` 的 `unit` 同一含义）。</summary>
        public static float MothUnitOf(int form) => form == 0 ? 1.15f : (form == 1 ? 1.70f : 2.40f);

        /// <summary>
        /// 飞蛾 BOSS 某一形态的 `Shape`。**纯函数**：`(id, form)` → 同一只虫永远同一形状。
        /// 与 `ShapeOf` 完全独立的随机流（`Crc32(id + "#moth" + form)`）⇒ 互不影响 ✓
        /// </summary>
        public static Shape MothShapeOf(string id, int form)
        {
            string key = id ?? "moth";
            Species kind = MothFormOf(form);
            float unit = MothUnitOf(form);
            var r = new Rng32(Crc32(key + "#moth" + form));
            // 12 个独立抖动（取值顺序固定 ⇒ 同一 form 可重放）
            float a0 = r.Next01(), a1 = r.Next01(), a2 = r.Next01(), a3 = r.Next01();
            float a4 = r.Next01(), a5 = r.Next01(), a6 = r.Next01(), a7 = r.Next01();
            float a8 = r.Next01(), a9 = r.Next01(), a10 = r.Next01(), a11 = r.Next01();

            var s = new Shape
            {
                Id = key,
                Boss = true,                     // ★ 它确实是 BOSS（体型/描边/材质都按 BOSS 的档走）★
                Kind = kind,
                Unit = unit,
                Sides = kind == Species.Pupa ? 8 : 6,
                Hue = kind == Species.Larva ? 0.16f : (kind == Species.Pupa ? 0.62f : 0.78f),
                LegFreq = kind == Species.Larva ? 9.0f : (kind == Species.Pupa ? 2.5f : 5.5f),
                Carapace = kind == Species.Pupa, // 蛹：背上那片壳 = 丝茧
                Horn = false,
                TailSpike = kind == Species.Larva,
            };

            if (kind == Species.Larva)
            {
                // ── 毛毛虫：长腹 + 粗腹 + 短腿 ──
                s.HeadLen = 0.16f * unit * (0.90f + 0.20f * a0);
                s.ThoraxLen = 0.26f * unit * (0.90f + 0.20f * a1);
                s.AbdomenLen = 0.95f * unit * (0.88f + 0.24f * a2);   // ★ 拉长：分节感全靠它
                s.HeadR = 0.115f * unit * (0.90f + 0.20f * a3);
                s.ThoraxR = 0.150f * unit * (0.90f + 0.20f * a4);
                s.AbdomenR = 0.190f * unit * (0.90f + 0.20f * a5);
                s.LegLen = 0.230f * unit * (0.90f + 0.20f * a6);      // 短腿（多足感来自 6 条 + 9Hz）
                s.LegThick = 0.026f * unit;
                s.AntennaLen = 0.075f * unit;
                s.BodyY = 0.240f * unit * (0.95f + 0.10f * a7);       // 贴地爬
                s.DomeR = 0.180f * unit;                              // = 髋圈半径（见上方说明）
                s.DomeH = 0f;
                s.WingLen = 0f; s.WingSpan = 0f;                      // 无翅
            }
            else if (kind == Species.Pupa)
            {
                // ── 蛹：纺锤（腹宽 > 胸宽，头尾收）──
                s.HeadLen = 0.13f * unit * (0.90f + 0.20f * a0);
                s.ThoraxLen = 0.30f * unit * (0.90f + 0.20f * a1);
                s.AbdomenLen = 0.62f * unit * (0.90f + 0.20f * a2);
                s.HeadR = 0.090f * unit * (0.90f + 0.20f * a3);
                s.ThoraxR = 0.170f * unit * (0.90f + 0.20f * a4);
                s.AbdomenR = 0.300f * unit * (0.90f + 0.20f * a5);    // ★ 纺锤最粗处
                s.LegLen = 0.085f * unit;                              // 残足（几乎看不出腿）
                s.LegThick = 0.030f * unit;
                s.AntennaLen = 0.050f * unit;                          // 缩在壳里
                s.BodyY = 0.300f * unit * (0.95f + 0.10f * a7);
                s.DomeR = 0.230f * unit;
                s.DomeH = 0.100f * unit;
                s.WingLen = 0.10f * unit; s.WingSpan = 0.08f * unit;   // 翅芽（贴在壳侧）
            }
            else
            {
                // ── 飞蛾：与 `ShapeOf` 的非 BOSS 公式同族（`MothBody` 就是按它们设计的），只把 unit 换成 2.4 ──
                s.HeadLen = 0.130f * unit * (0.85f + 0.45f * a0);
                s.ThoraxLen = 0.250f * unit * (0.85f + 0.45f * a1);
                s.AbdomenLen = 0.320f * unit * (0.75f + 0.95f * a2);
                s.HeadR = 0.095f * unit * (0.85f + 0.45f * a3);
                s.ThoraxR = 0.128f * unit * (0.85f + 0.45f * a4);
                s.AbdomenR = 0.140f * unit * (0.80f + 0.50f * a5) * (0.75f + 0.55f * a6);
                s.LegLen = 0.215f * unit * (0.80f + 0.60f * a7);
                s.LegThick = 0.030f * unit * (0.80f + 0.60f * a8);
                s.AntennaLen = 0.260f * unit * (0.80f + 0.40f * a9);  // ★ 羽毛触角更长
                s.BodyY = 0.330f * unit * (0.85f + 0.35f * a10);
                s.DomeR = 0.285f * unit;
                s.DomeH = 0.235f * unit;
                s.WingLen = 0.560f * unit * (0.95f + 0.10f * a11);    // ★ 大翅（比远程兵蛾的 0.46 更大）
                s.WingSpan = 0.360f * unit * (0.95f + 0.10f * a2);
            }
            return s;
        }

        // ══════════════════════════════════════════════════════════════════════
        // 三、=== 权威表 === 网格构造器（三角汤 + 逐面外法线）
        //
        // 全部零件都归结成**一个** `Tube()`：任意轴、椭圆截面、可选端盖。
        // 腿 / 触角 / 大颚 / 鞘翅 / 头角 / 尾刺 / 三段体节 都是它的不同调用 ⇒
        // 离线副本也只需要实现这一个函数就能复现全部拓扑 ✓
        // ══════════════════════════════════════════════════════════════════════

        private sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>(768);
            public readonly List<int> T = new List<int>(768);

            /// <summary>
            /// 加一个三角形；若它的几何法线与 `outward` 反向就**换两个顶点**。
            /// 这一行就是"外法线率 100%"的来源 —— 不靠人肉推理绕序，靠它保证 ✓
            /// （镜像零件因此是安全的：左右各建一份网格，绕序由本函数自动纠正 ✓）
            /// </summary>
            public void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, outward) < 0f) { Vector3 t = b; b = c; c = t; }
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
            }

            /// <summary>一段管（任意轴、椭圆截面）。`ru` 沿 `u` 轴、`rv` 沿 `v` 轴。</summary>
            public void Tube(Vector3 c0, Vector3 c1,
                             float ru0, float rv0, float ru1, float rv1,
                             int sides, float twist, bool cap0, bool cap1)
            {
                Vector3 axis = c1 - c0;
                if (axis.sqrMagnitude < 1e-12f) return;
                axis = axis.normalized;
                Vector3 reference = Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right;
                Vector3 u = Vector3.Cross(reference, axis).normalized;
                Vector3 v = Vector3.Cross(axis, u).normalized;

                Vector3[] ra = new Vector3[sides];
                Vector3[] rb = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float t = twist + (i / (float)sides) * Mathf.PI * 2f;
                    float cs = Mathf.Cos(t), sn = Mathf.Sin(t);
                    ra[i] = c0 + u * (cs * ru0) + v * (sn * rv0);
                    rb[i] = c1 + u * (cs * ru1) + v * (sn * rv1);
                }

                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    // 外法线的参考方向 = 该棱柱面的径向（椭圆上取该角的法向）✓
                    float tm = twist + ((i + 0.5f) / sides) * Mathf.PI * 2f;
                    float cm = Mathf.Cos(tm), sm = Mathf.Sin(tm);
                    Vector3 outward = u * (cm * (ru0 + ru1) * 0.5f) + v * (sm * (rv0 + rv1) * 0.5f);
                    if (outward.sqrMagnitude < 1e-12f) outward = ra[i] - c0;
                    TriOut(ra[i], ra[j], rb[j], outward);
                    TriOut(ra[i], rb[j], rb[i], outward);
                }
                if (cap0)
                    for (int i = 0; i < sides; i++)
                        TriOut(c0, ra[(i + 1) % sides], ra[i], -axis);
                if (cap1)
                    for (int i = 0; i < sides; i++)
                        TriOut(c1, rb[i], rb[(i + 1) % sides], axis);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(V);
                mesh.SetTriangles(T, 0);
                mesh.RecalculateNormals();   // 每三角形独占 3 顶点 ⇒ 逐面法线 = 硬边 ✓
                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>
            /// ★ v191：一片**扁平的锥形板**（翅 / 覆翅 / 触角棒上的鳃叶）。
            ///
            /// 与 `Tube` 的区别只有一处：横截面的 `u` 轴**由调用方给**（`uAxis`，即"弦向"），
            /// 而 `Tube` 是按轴自动挑一个参考向量。翅要有明确的弦向（前缘朝前）才会像翅膀，
            /// 自动挑的参考向量会给出一片"歪着张的板" ✗
            /// ★ `Tube` 一个字没动（BOSS 全用它）★ 这里是并列的新函数，不共享状态 ✓
            /// </summary>
            public void Blade(Vector3 c0, Vector3 c1, Vector3 uAxis,
                              float ru0, float rv0, float ru1, float rv1,
                              int sides, bool cap0, bool cap1)
            {
                Vector3 axis = c1 - c0;
                if (axis.sqrMagnitude < 1e-12f) return;
                axis = axis.normalized;
                Vector3 u = Vector3.ProjectOnPlane(uAxis, axis);
                if (u.sqrMagnitude < 1e-10f)
                    u = Vector3.Cross(Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right, axis);
                u = u.normalized;
                Vector3 v = Vector3.Cross(axis, u).normalized;

                Vector3[] ra = new Vector3[sides];
                Vector3[] rb = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float t = (i / (float)sides) * Mathf.PI * 2f;
                    float cs = Mathf.Cos(t), sn = Mathf.Sin(t);
                    ra[i] = c0 + u * (cs * ru0) + v * (sn * rv0);
                    rb[i] = c1 + u * (cs * ru1) + v * (sn * rv1);
                }
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    float tm = ((i + 0.5f) / sides) * Mathf.PI * 2f;
                    float cm = Mathf.Cos(tm), sm = Mathf.Sin(tm);
                    Vector3 outward = u * (cm * (ru0 + ru1) * 0.5f) + v * (sm * (rv0 + rv1) * 0.5f);
                    if (outward.sqrMagnitude < 1e-12f) outward = ra[i] - c0;
                    TriOut(ra[i], ra[j], rb[j], outward);
                    TriOut(ra[i], rb[j], rb[i], outward);
                }
                if (cap0)
                    for (int i = 0; i < sides; i++)
                        TriOut(c0, ra[(i + 1) % sides], ra[i], -axis);
                if (cap1)
                    for (int i = 0; i < sides; i++)
                        TriOut(c1, rb[i], rb[(i + 1) % sides], axis);
            }

            /// <summary>
            /// ★ v191：**半球顶**（瓢虫的壳 / 蚱蜢的复眼）。
            /// `rings` 层纬环 × `sides` 段经环，顶上一圈三角扇，底缘可选封盘。
            /// 每个三角形仍由 `TriOut` 保证外法线 ⇒ 与其余零件同一套"flat shading + 硬边"✓
            /// </summary>
            public void Dome(Vector3 center, float rx, float rz, float height, int rings, int sides, bool capBottom)
            {
                if (rings < 1 || sides < 3) return;
                int r = Mathf.Max(1, rings);
                int sd = Mathf.Max(3, sides);
                Vector3 apex = center + new Vector3(0f, height, 0f);
                Vector3[] prev = null;
                Vector3[] first = null;
                for (int ring = 0; ring <= r; ring++)
                {
                    float phi = (ring / (float)r) * Mathf.PI * 0.5f;
                    float c = Mathf.Cos(phi);
                    var cur = new Vector3[sd];
                    for (int i = 0; i < sd; i++)
                    {
                        float th = (i / (float)sd) * Mathf.PI * 2f;
                        cur[i] = center + new Vector3(c * Mathf.Cos(th) * rx,
                                                      Mathf.Sin(phi) * height,
                                                      c * Mathf.Sin(th) * rz);
                    }
                    if (first == null) first = cur;
                    if (prev != null)
                    {
                        for (int i = 0; i < sd; i++)
                        {
                            int j = (i + 1) % sd;
                            float thm = ((i + 0.5f) / sd) * Mathf.PI * 2f;
                            Vector3 outward = new Vector3(Mathf.Cos(thm), 0.30f, Mathf.Sin(thm));
                            if (ring == r)
                            {
                                // 顶环退化成一个点 ⇒ 用三角扇收口，不产生零面积三角形
                                TriOut(prev[i], prev[j], apex, outward);
                            }
                            else
                            {
                                TriOut(prev[i], prev[j], cur[j], outward);
                                TriOut(prev[i], cur[j], cur[i], outward);
                            }
                        }
                    }
                    prev = cur;
                }
                if (capBottom && first != null)
                {
                    for (int i = 0; i < sd; i++)
                        TriOut(center, first[(i + 1) % sd], first[i], Vector3.down);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 四、=== 权威表 === 各零件几何
        //
        // 模型空间约定：**+Y 向上、+Z 向前、原点在脚底平面中心**（与 `WorldView.YawTo` 的 0°=+Z 对齐）。
        // 三段体节沿 Z 排布：头在 +Z 端，腹在 -Z 端。
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>一条腿的髋关节位置（模型空间）。顺序：0/1 前对、2/3 中对、4/5 后对；**偶数在右、奇数在左**。</summary>
        public static Vector3 LegHip(Shape s, int index)
        {
            bool right = (index % 2) == 0;
            int row = index / 2;                          // 0 前 / 1 中 / 2 后
            float z = (row - 1) * s.ThoraxLen * 0.62f;    // 沿胸节前后排开
            float x = (right ? 1f : -1f) * s.ThoraxR * 0.92f;
            return new Vector3(x, s.BodyY * 0.55f, z);
        }

        /// <summary>体节主网格：头 / 胸 / 腹 三段。</summary>
        private static Mesh BodyMesh(Shape s)
        {
            var b = new Builder();
            int n = s.Sides;
            float y = s.BodyY;
            const float flat = 0.86f;                     // 虫子略扁（横截面不是正圆）
            float thoraxFront = s.ThoraxLen * 0.5f;
            float thoraxBack = -s.ThoraxLen * 0.5f;

            // ── 胸节：最粗，头与腹都接在它两端（内部接缝不封盖，省 2n 个三角）──
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y, thoraxFront),
                   s.ThoraxR * 0.95f, s.ThoraxR * 0.95f * flat,
                   s.ThoraxR * 0.90f, s.ThoraxR * 0.90f * flat,
                   n, 0f, false, false);

            // ── 头节：向前收窄，正面封盖 ──
            b.Tube(new Vector3(0f, y, thoraxFront), new Vector3(0f, y * 1.02f, thoraxFront + s.HeadLen),
                   s.ThoraxR * 0.88f, s.ThoraxR * 0.88f * flat,
                   s.HeadR, s.HeadR * flat,
                   n, 0.26f, false, true);

            // ── 腹节：两段 ⇒ 中段鼓起来（"胖瘦"由 AbdomenR 决定），尾端封口 ──
            float waist = thoraxBack - s.AbdomenLen * 0.55f;
            float tail = thoraxBack - s.AbdomenLen;
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y * 0.99f, waist),
                   s.ThoraxR * 0.92f, s.ThoraxR * 0.92f * flat,
                   s.AbdomenR * 1.06f, s.AbdomenR * 1.06f * flat,
                   n, 0.13f, false, false);
            b.Tube(new Vector3(0f, y * 0.99f, waist), new Vector3(0f, y * 0.95f, tail),
                   s.AbdomenR * 1.06f, s.AbdomenR * 1.06f * flat,
                   s.AbdomenR * 0.26f, s.AbdomenR * 0.26f * flat,
                   n, 0.13f, false, true);

            return b.Build("BUG_body_" + s.Id);
        }

        /// <summary>重色零件网格：背甲 / 头角 / 尾刺（同一只虫共用一个渲染器）。没有特征时返回 null。</summary>
        private static Mesh AccentMesh(Shape s)
        {
            var b = new Builder();
            float y = s.BodyY;
            float thoraxFront = s.ThoraxLen * 0.5f;
            float thoraxBack = -s.ThoraxLen * 0.5f;
            float tail = thoraxBack - s.AbdomenLen;

            if (s.Carapace)
            {
                // 背甲：一片压扁的壳盖在胸节背上（rv 小 ⇒ 扁平）
                // ★ 比胸节略窄（1.02 而不是 1.18）★ —— 盖过去会变成"一块板子压在虫子上"，
                //   露不出两侧的体节轮廓 ⇒ 三段体节就读不出来了 ✗
                b.Tube(new Vector3(0f, y + s.ThoraxR * 0.60f, thoraxBack * 0.92f),
                       new Vector3(0f, y + s.ThoraxR * 0.50f, thoraxFront * 0.92f),
                       s.ThoraxR * 1.02f, s.ThoraxR * 0.28f,
                       s.ThoraxR * 1.02f, s.ThoraxR * 0.28f,
                       s.Sides, 0.52f, true, true);
            }
            if (s.Horn)
            {
                // 头角：从头顶往前上翘
                float z0 = thoraxFront + s.HeadLen * 0.55f;
                b.Tube(new Vector3(0f, y + s.HeadR * 0.45f, z0),
                       new Vector3(0f, y + s.HeadR * 2.10f, z0 + s.HeadLen * 0.62f),
                       s.HeadR * 0.34f, s.HeadR * 0.34f,
                       s.HeadR * 0.03f, s.HeadR * 0.03f,
                       4, 0.4f, true, true);
            }
            if (s.TailSpike)
            {
                // 尾刺：从腹部末端往后下扎
                b.Tube(new Vector3(0f, y * 0.95f, tail),
                       new Vector3(0f, y * 0.62f, tail - s.AbdomenLen * 0.42f),
                       s.AbdomenR * 0.28f, s.AbdomenR * 0.28f,
                       0.004f, 0.004f,
                       4, 0.2f, true, true);
            }
            return b.V.Count > 0 ? b.Build("BUG_accent_" + s.Id) : null;
        }

        /// <summary>一条腿（股节 + 胫节两段）。髋关节在原点，脚落在模型空间 y=0 上。</summary>
        private static Mesh LegMesh(Shape s)
        {
            var b = new Builder();
            float reach = s.LegLen;
            float hipY = s.BodyY * 0.55f;                 // 髋高 ⇒ 脚要在 -hipY 处落地
            float t = s.LegThick;
            Vector3 knee = new Vector3(reach * 0.52f, reach * 0.16f, 0f);
            Vector3 foot = new Vector3(reach * 1.00f, -hipY, 0f);
            b.Tube(Vector3.zero, knee, t, t * 0.85f, t * 0.72f, t * 0.62f, 4, 0.4f, true, true);
            b.Tube(knee, foot, t * 0.72f, t * 0.62f, t * 0.30f, t * 0.26f, 4, 0.4f, true, true);
            return b.Build("BUG_leg_" + s.Id);
        }

        /// <summary>一根触角（两段）。**沿 +Y/+Z 弯（不含 X）** ⇒ 左右只靠节点的偏航，不需要镜像 ✓</summary>
        private static Mesh AntennaMesh(Shape s)
        {
            var b = new Builder();
            float a = s.AntennaLen;
            float t = Mathf.Max(0.006f, s.LegThick * 0.34f);
            Vector3 elbow = new Vector3(0f, a * 0.62f, a * 0.22f);
            Vector3 tip = new Vector3(0f, a * 1.00f, a * 0.86f);
            b.Tube(Vector3.zero, elbow, t, t, t * 0.72f, t * 0.72f, 3, 0.5f, true, true);
            b.Tube(elbow, tip, t * 0.72f, t * 0.72f, t * 0.26f, t * 0.26f, 3, 0.5f, true, true);
            return b.Build("BUG_antenna_" + s.Id);
        }

        /// <summary>BOSS 的一侧鞘翅：从铰链（原点）向侧后方铺开的扁平壳。`right` 决定镜像（按几何镜像建网格）。</summary>
        private static Mesh ElytraMesh(Shape s, bool right)
        {
            var b = new Builder();
            float sign = right ? 1f : -1f;
            float half = s.ThoraxR * 0.40f;
            float len = s.ThoraxLen + s.AbdomenLen * 0.92f;
            b.Tube(new Vector3(sign * (half + s.ThoraxR * 0.80f), 0f, s.ThoraxLen * 0.42f),
                   new Vector3(sign * (half + s.ThoraxR * 0.68f), -s.Unit * 0.012f, s.ThoraxLen * 0.42f - len),
                   s.ThoraxR * 0.80f, s.ThoraxR * 0.17f,
                   s.ThoraxR * 0.54f, s.ThoraxR * 0.12f,
                   6, 0.5f, true, true);
            return b.Build("BUG_elytra_" + s.Id + (right ? "_r" : "_l"));
        }

        /// <summary>BOSS 的一侧大颚：向前内弯的两段钳。`right` 决定镜像。</summary>
        private static Mesh MandibleMesh(Shape s, bool right)
        {
            var b = new Builder();
            float sign = right ? 1f : -1f;
            float a = s.HeadR * 1.35f;
            Vector3 knee = new Vector3(sign * a * 0.42f, -a * 0.16f, a * 0.72f);
            Vector3 tip = new Vector3(sign * a * 0.06f, -a * 0.30f, a * 1.30f);
            float t = s.HeadR * 0.26f;
            b.Tube(Vector3.zero, knee, t, t * 0.9f, t * 0.80f, t * 0.72f, 4, 0.3f, true, true);
            b.Tube(knee, tip, t * 0.80f, t * 0.72f, t * 0.20f, t * 0.18f, 4, 0.3f, true, true);
            return b.Build("BUG_mandible_" + s.Id + (right ? "_r" : "_l"));
        }

        // ══════════════════════════════════════════════════════════════════════
        // 四之二、★★ v191：**4 种昆虫的零件几何**（BOSS 不在这里，它用上面那一套）★★
        //
        // 需求：「设计多种不同的昆虫类怪物 … 每个 id 有**可辨识的轮廓** … 差异化的身体构成
        //       （头/胸/腹/触角/腿/翅 的数量与比例不同），不是同一个壳换颜色」
        // ⇒ 每个物种的**零件集合本身**就不同：
        //     · `grunt`   锹形虫：3 体节 + **一对大颚钳** + **一对鞘翅** + **棒状触角** + 6 腿（3 段腿）
        //     · `shooter` 蛾    ：肥腹 + **4 片翅**（前翅 + 后翅）+ **羽状触角** + **口器** + 6 条细短腿
        //     · `jumper`  蚱蜢：长身 + **2 条巨大后腿** + 4 条小步足 + **复眼×2** + **覆翅×2** + 短触角
        //     · `pig`     瓢虫：**半球壳** + 小头 + 极短腿×6 + 短触角（★ 场上仍走旧版 FBX，见 `_pigs`）
        // 所有零件仍只用 `Builder.Tube`（+ 新增的 `Blade` / `Dome`）⇒ 拓扑仍可被离线副本复现 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>腿根离地高度（脚要落在 y=0 ⇒ 腿网格里脚在 -hipY 处）。每个物种不同。</summary>
        public static float HipHeight(Shape s)
        {
            switch (s.Kind)
            {
                case Species.Stag: return s.BodyY * 0.55f;
                case Species.Moth: return s.BodyY * 0.48f;
                case Species.Hopper: return s.BodyY * 0.52f;
                case Species.Ladybug: return s.DomeR * 0.42f;
                default: return s.BodyY * 0.55f;      // Giant 走 `LegHip`，这里只是兜底
            }
        }

        /// <summary>一条腿的髋关节位置（物种化）。0/1 前对、2/3 中对、4/5 后对；偶数在右。</summary>
        public static Vector3 HipOf(Shape s, int index)
        {
            bool right = (index % 2) == 0;
            float sx = right ? 1f : -1f;
            float y = HipHeight(s);
            switch (s.Kind)
            {
                case Species.Stag:
                {
                    int row = index / 2;
                    float z = (row - 1) * s.ThoraxLen * 0.52f + s.ThoraxLen * 0.08f;
                    return new Vector3(sx * s.ThoraxR * 0.92f, y, z);
                }
                case Species.Moth:
                {
                    int row = index / 2;
                    float z = (row - 1) * s.ThoraxLen * 0.44f;
                    return new Vector3(sx * s.ThoraxR * 0.72f, y, z);
                }
                case Species.Hopper:
                {
                    if (index >= 4)
                    {
                        // 后腿的髋在腹部前端，比步足更靠外、更靠后（股节从这里向后上扬起）
                        float hx = index == 4 ? 1f : -1f;
                        return new Vector3(hx * s.ThoraxR * 0.78f, y, -s.ThoraxLen * 0.34f);
                    }
                    int row = index / 2;                       // 0 前 / 1 中
                    float z = s.ThoraxLen * (0.46f - row * 0.50f);
                    return new Vector3(sx * s.ThoraxR * 0.82f, y, z);
                }
                default:                                        // Ladybug
                {
                    int row = index / 2;
                    float z = (row - 1) * s.DomeR * 0.54f;
                    return new Vector3(sx * s.DomeR * 0.68f, y, z);
                }
            }
        }

        // ── ① 锹形虫（grunt）────────────────────────────────────────────────

        /// <summary>锹形虫：宽胸 + 长头 + 肥腹，胸背一块前胸背板。</summary>
        private static Mesh StagBody(Shape s)
        {
            var b = new Builder();
            int n = 8;
            float y = s.BodyY;
            const float flat = 0.84f;
            float thoraxFront = s.ThoraxLen * 0.62f;
            float thoraxBack = -s.ThoraxLen * 0.38f;
            float headY = y + s.Unit * 0.020f;
            float headTip = thoraxFront + s.HeadLen * 1.30f;
            float tail = thoraxBack - s.AbdomenLen * 1.02f;
            float waist = thoraxBack - s.AbdomenLen * 0.50f;

            // 胸节（最粗，两端开口接别的体节）
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y + s.Unit * 0.006f, thoraxFront),
                   s.ThoraxR * 0.98f, s.ThoraxR * 0.98f * flat,
                   s.ThoraxR * 0.92f, s.ThoraxR * 0.92f * flat,
                   n, 0f, false, false);

            // 头：向前**略微下倾**，正面封盖（锹形虫的头是一块扁铲）
            b.Tube(new Vector3(0f, y + s.Unit * 0.006f, thoraxFront), new Vector3(0f, headY, headTip),
                   s.ThoraxR * 0.90f, s.ThoraxR * 0.90f * flat,
                   s.HeadR * 1.18f, s.HeadR * 1.18f * 0.66f,
                   n, 0.22f, false, true);

            // 腹节两段：中段鼓起，尾端收口
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y * 0.99f, waist),
                   s.ThoraxR * 0.94f, s.ThoraxR * 0.94f * flat,
                   s.AbdomenR * 1.04f, s.AbdomenR * 1.04f * flat,
                   n, 0.11f, false, false);
            b.Tube(new Vector3(0f, y * 0.99f, waist), new Vector3(0f, y * 0.94f, tail),
                   s.AbdomenR * 1.04f, s.AbdomenR * 1.04f * flat,
                   s.AbdomenR * 0.22f, s.AbdomenR * 0.22f * flat,
                   n, 0.11f, false, true);

            return b.Build("STAG_body_" + s.Id);
        }

        /// <summary>锹形虫的重色零件：前胸背板 + 两根内齿（大颚本身是独立零件）。</summary>
        private static Mesh StagAccent(Shape s)
        {
            var b = new Builder();
            float y = s.BodyY;
            float thoraxFront = s.ThoraxLen * 0.62f;
            float thoraxBack = -s.ThoraxLen * 0.38f;

            // 前胸背板：盖在胸节背上的一弧度薄壳（比胸节**宽** ⇒ 侧缘露出来 ⇒ 轮廓更硬）
            b.Tube(new Vector3(0f, y + s.ThoraxR * 0.52f, thoraxBack * 0.86f),
                   new Vector3(0f, y + s.ThoraxR * 0.40f, thoraxFront * 0.92f),
                   s.ThoraxR * 1.16f, s.ThoraxR * 0.34f,
                   s.ThoraxR * 1.10f, s.ThoraxR * 0.30f,
                   8, 0.40f, true, true);

            // 颈鞘：胸与头之间一圈窄环（读得出"两截"）
            b.Tube(new Vector3(0f, y + s.Unit * 0.012f, thoraxFront * 0.98f),
                   new Vector3(0f, y + s.Unit * 0.014f, thoraxFront * 0.98f + s.Unit * 0.030f),
                   s.ThoraxR * 0.72f, s.ThoraxR * 0.62f,
                   s.ThoraxR * 0.66f, s.ThoraxR * 0.58f,
                   8, 0.2f, true, true);

            return b.Build("STAG_accent_" + s.Id);
        }

        /// <summary>锹形虫的一侧大颚：两段前伸内弯的钳 + 一枚内齿。</summary>
        private static Mesh StagPincer(Shape s, bool right)
        {
            var b = new Builder();
            float sign = right ? 1f : -1f;
            float L = s.PincerLen;
            float t = Mathf.Max(0.010f, s.HeadR * 0.24f);
            Vector3 knee = new Vector3(sign * L * 0.30f, -L * 0.06f, L * 0.52f);
            Vector3 tip = new Vector3(sign * L * 0.05f, -L * 0.12f, L * 1.00f);
            b.Tube(Vector3.zero, knee, t, t, t * 0.80f, t * 0.80f, 5, 0.3f, true, true);
            b.Tube(knee, tip, t * 0.80f, t * 0.80f, t * 0.18f, t * 0.18f, 5, 0.3f, true, true);
            // 内齿：朝身体中线（-sign 方向）扎出去的一小截
            b.Tube(new Vector3(sign * L * 0.20f, -L * 0.07f, L * 0.62f),
                   new Vector3(sign * L * 0.02f, -L * 0.10f, L * 0.68f),
                   t * 0.55f, t * 0.55f, 0.003f, 0.003f, 4, 0.4f, true, true);
            return b.Build("STAG_pincer_" + s.Id + (right ? "_r" : "_l"));
        }

        /// <summary>锹形虫的一侧鞘翅：盖住整条腹部的硬壳（比腹部略窄 ⇒ 两侧腹部露一圈）。</summary>
        private static Mesh StagElytra(Shape s, bool right)
        {
            var b = new Builder();
            float sign = right ? 1f : -1f;
            float zBack = -s.ThoraxLen * 0.30f;
            float zTip = zBack - s.AbdomenLen * 1.36f;
            float r = s.AbdomenR;
            b.Tube(new Vector3(sign * s.ThoraxR * 0.52f, s.BodyY + s.ThoraxR * 0.46f, zBack),
                   new Vector3(sign * s.ThoraxR * 0.30f, s.BodyY + r * 0.60f, zTip),
                   r * 0.62f, r * 0.40f,
                   r * 0.30f, r * 0.20f,
                   6, 0.5f, true, true);
            return b.Build("STAG_elytra_" + s.Id + (right ? "_r" : "_l"));
        }

        /// <summary>锹形虫的触角：柄 + 肘 + **三片鳃叶组成的棒**（这是它最好认的特征之一）。</summary>
        private static Mesh StagAntenna(Shape s)
        {
            var b = new Builder();
            float a = s.AntennaLen * 1.10f;
            float t = Mathf.Max(0.006f, s.LegThick * 0.32f);
            Vector3 elbow = new Vector3(0f, a * 0.34f, a * 0.42f);
            Vector3 stem = new Vector3(0f, a * 0.46f, a * 0.84f);
            b.Tube(Vector3.zero, elbow, t, t, t * 0.85f, t * 0.85f, 4, 0.4f, true, true);
            b.Tube(elbow, stem, t * 0.85f, t * 0.85f, t * 0.62f, t * 0.62f, 4, 0.4f, true, true);
            // 三片鳃叶：从棒的末端张开（扇面在 YZ 平面里 ⇒ 左右只靠节点的偏航区分）
            for (int i = 0; i < 3; i++)
            {
                float fan = (i - 1) * 0.55f;
                Vector3 dir = new Vector3(0f, Mathf.Cos(fan) * a * 0.34f, Mathf.Sin(fan) * a * 0.30f);
                b.Blade(stem, stem + dir + new Vector3(0f, a * 0.06f, a * 0.10f), Vector3.forward,
                        a * 0.13f, t * 0.30f, a * 0.07f, t * 0.16f, 4, true, true);
            }
            return b.Build("STAG_antenna_" + s.Id);
        }

        /// <summary>锹形虫的腿：**三段**（股节 + 胫节 + 跗爪），比其余物种粗壮。</summary>
        private static Mesh StagLeg(Shape s)
        {
            var b = new Builder();
            float reach = s.LegLen * 1.05f;
            float hipY = HipHeight(s);
            float t = s.LegThick * 1.15f;
            Vector3 knee = new Vector3(reach * 0.48f, reach * 0.14f, 0f);
            Vector3 ankle = new Vector3(reach * 0.86f, -hipY * 0.55f, 0f);
            Vector3 foot = new Vector3(reach * 1.00f, -hipY, 0f);
            b.Tube(Vector3.zero, knee, t, t * 0.9f, t * 0.78f, t * 0.72f, 5, 0.4f, true, true);
            b.Tube(knee, ankle, t * 0.78f, t * 0.72f, t * 0.52f, t * 0.48f, 5, 0.2f, true, true);
            b.Tube(ankle, foot, t * 0.52f, t * 0.48f, t * 0.20f, t * 0.16f, 4, 0.2f, true, true);
            return b.Build("STAG_leg_" + s.Id);
        }

        // ── ② 蛾（shooter）──────────────────────────────────────────────────

        /// <summary>蛾：肥而下垂的长腹 + 粗胸 + 小头 + 卷曲口器。</summary>
        private static Mesh MothBody(Shape s)
        {
            var b = new Builder();
            int n = 8;
            float y = s.BodyY;
            const float flat = 0.92f;
            float thoraxFront = s.ThoraxLen * 0.34f;
            float thoraxBack = -s.ThoraxLen * 0.34f;
            float headTip = thoraxFront + s.HeadLen * 0.78f;
            float abdomenEnd = thoraxBack - s.AbdomenLen * 0.96f;
            float bulge = thoraxBack - s.AbdomenLen * 0.38f;
            float rA = s.AbdomenR * 1.42f;

            // 胸：粗厚、有绒毛感（面数 8）
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y + s.Unit * 0.008f, thoraxFront),
                   s.ThoraxR * 1.02f, s.ThoraxR * 1.02f * flat,
                   s.ThoraxR * 0.94f, s.ThoraxR * 0.94f * flat,
                   n, 0f, false, false);

            // 头：小，正前方封盖
            b.Tube(new Vector3(0f, y + s.Unit * 0.008f, thoraxFront), new Vector3(0f, y * 1.02f, headTip),
                   s.ThoraxR * 0.74f, s.ThoraxR * 0.74f * flat,
                   s.HeadR * 0.92f, s.HeadR * 0.92f * flat,
                   n, 0.24f, false, true);

            // 腹：两段，**向后下垂**（蛾的标志），中段最胖
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y * 0.96f, bulge),
                   s.ThoraxR * 0.86f, s.ThoraxR * 0.86f * flat,
                   rA, rA * flat,
                   n, 0.13f, false, false);
            b.Tube(new Vector3(0f, y * 0.96f, bulge), new Vector3(0f, y * 0.80f, abdomenEnd),
                   rA, rA * flat,
                   rA * 0.16f, rA * 0.16f * flat,
                   n, 0.13f, false, true);

            // 口器：从头的腹面往前下卷成一个小钩
            float pr = Mathf.Max(0.006f, s.HeadR * 0.10f);
            Vector3 p0 = new Vector3(0f, y * 1.00f - s.HeadR * 0.30f, headTip * 0.96f);
            Vector3 p1 = new Vector3(0f, y * 1.00f - s.HeadR * 1.05f, headTip + s.HeadLen * 0.34f);
            Vector3 p2 = new Vector3(0f, y * 1.00f - s.HeadR * 1.60f, headTip + s.HeadLen * 0.06f);
            b.Tube(p0, p1, pr, pr, pr * 0.8f, pr * 0.8f, 4, 0.3f, true, true);
            b.Tube(p1, p2, pr * 0.8f, pr * 0.8f, pr * 0.45f, pr * 0.45f, 4, 0.3f, true, true);

            return b.Build("MOTH_body_" + s.Id);
        }

        /// <summary>蛾的一片翅：`fore` = 前翅（大）/ 后翅（小）。扁平的锥形叶片。</summary>
        private static Mesh MothWing(Shape s, bool right, bool fore)
        {
            var b = new Builder();
            float sign = right ? 1f : -1f;
            float y = s.BodyY;
            Vector3 root, tip;
            float chord, thick;
            if (fore)
            {
                root = new Vector3(sign * s.ThoraxR * 0.40f, y + s.ThoraxR * 0.66f, s.ThoraxLen * 0.18f);
                tip = new Vector3(sign * (s.WingLen + s.ThoraxR * 0.40f), y + s.ThoraxR * 1.10f,
                                  s.ThoraxLen * 0.18f - s.WingLen * 0.74f);
                chord = s.WingSpan;
                thick = s.Unit * 0.013f;
            }
            else
            {
                root = new Vector3(sign * s.ThoraxR * 0.34f, y + s.ThoraxR * 0.20f, -s.ThoraxLen * 0.16f);
                tip = new Vector3(sign * (s.WingLen * 0.70f + s.ThoraxR * 0.30f), y + s.ThoraxR * 0.44f,
                                  -s.ThoraxLen * 0.16f - s.WingLen * 0.86f);
                chord = s.WingSpan * 0.62f;
                thick = s.Unit * 0.010f;
            }
            // 弦向 = 世界 +Z（前），厚度沿翅面法线 ⇒ 一片"薄板"，不是一根"细杆"
            b.Blade(root, tip, Vector3.forward,
                    chord * 0.50f, thick * 0.50f,
                    chord * 0.13f, thick * 0.30f,
                    6, true, true);
            return b.Build("MOTH_wing_" + s.Id + (fore ? "_fore" : "_hind") + (right ? "_r" : "_l"));
        }

        /// <summary>蛾的触角：**三段 S 形**向后上方扫（羽状感来自长度与弯曲，不额外加毛）。</summary>
        private static Mesh MothAntenna(Shape s)
        {
            var b = new Builder();
            float a = s.AntennaLen * 1.45f;
            float t = Mathf.Max(0.005f, s.LegThick * 0.26f);
            Vector3 p1 = new Vector3(0f, a * 0.52f, a * 0.20f);
            Vector3 p2 = new Vector3(0f, a * 0.74f, -a * 0.10f);
            Vector3 p3 = new Vector3(0f, a * 0.70f, -a * 0.46f);
            b.Tube(Vector3.zero, p1, t, t, t * 0.85f, t * 0.85f, 4, 0.4f, true, true);
            b.Tube(p1, p2, t * 0.85f, t * 0.85f, t * 0.66f, t * 0.66f, 4, 0.4f, true, true);
            b.Tube(p2, p3, t * 0.66f, t * 0.66f, t * 0.24f, t * 0.24f, 3, 0.4f, true, true);
            return b.Build("MOTH_antenna_" + s.Id);
        }

        /// <summary>蛾的腿：细而短（蛾的足不是用来跑的），两段。</summary>
        private static Mesh MothLeg(Shape s)
        {
            var b = new Builder();
            float reach = s.LegLen * 0.62f;
            float hipY = HipHeight(s);
            float t = s.LegThick * 0.55f;
            Vector3 knee = new Vector3(reach * 0.50f, reach * 0.18f, 0f);
            Vector3 foot = new Vector3(reach, -hipY, 0f);
            b.Tube(Vector3.zero, knee, t, t, t * 0.70f, t * 0.70f, 4, 0.4f, true, true);
            b.Tube(knee, foot, t * 0.70f, t * 0.70f, t * 0.22f, t * 0.22f, 4, 0.4f, true, true);
            return b.Build("MOTH_leg_" + s.Id);
        }

        // ── ③ 蚱蜢（jumper）────────────────────────────────────────────────

        /// <summary>蚱蜢：长身高头 + 细长腹（贴地）+ 背上两片覆翅。</summary>
        private static Mesh HopperBody(Shape s)
        {
            var b = new Builder();
            int n = 8;
            float y = s.BodyY;
            float thoraxFront = s.ThoraxLen * 0.40f;
            float thoraxBack = -s.ThoraxLen * 0.26f;
            float headTip = thoraxFront + s.HeadLen * 1.05f;
            float tail = thoraxBack - s.AbdomenLen * 1.00f;

            // 头：**高**（rv > ru ⇒ 竖起来的马脸），正面封盖
            b.Tube(new Vector3(0f, y, thoraxFront), new Vector3(0f, y + s.HeadR * 0.18f, headTip),
                   s.HeadR * 1.10f, s.HeadR * 1.42f,
                   s.HeadR * 0.80f, s.HeadR * 1.10f,
                   n, 0.20f, false, true);

            // 胸：矮而宽
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y, thoraxFront),
                   s.ThoraxR * 1.02f, s.ThoraxR * 0.86f,
                   s.ThoraxR * 0.92f, s.ThoraxR * 0.80f,
                   n, 0f, false, false);

            // 腹：细长、末端上翘一点 ⇒ 与"肥腹蛾"形成对比
            b.Tube(new Vector3(0f, y, thoraxBack), new Vector3(0f, y * 0.96f, tail),
                   s.ThoraxR * 0.90f, s.ThoraxR * 0.78f,
                   s.AbdomenR * 0.30f, s.AbdomenR * 0.26f,
                   n, 0.09f, false, true);

            return b.Build("HOPPER_body_" + s.Id);
        }

        /// <summary>蚱蜢的复眼：一侧一个半球（挂在头侧，不随动画单独转）。</summary>
        private static Mesh HopperEye(Shape s, bool right)
        {
            var b = new Builder();
            float r = s.EyeR;
            b.Dome(new Vector3(0f, 0f, 0f), r, r, r * 0.92f, 3, 8, true);
            return b.Build("HOPPER_eye_" + s.Id + (right ? "_r" : "_l"));
        }

        /// <summary>蚱蜢的一侧覆翅：沿背贴着的一块窄长薄片（不飞，只挡风）。</summary>
        private static Mesh HopperTegmen(Shape s, bool right)
        {
            var b = new Builder();
            float sign = right ? 1f : -1f;
            float y = s.BodyY;
            Vector3 root = new Vector3(sign * s.ThoraxR * 0.22f, y + s.ThoraxR * 0.62f, s.ThoraxLen * 0.30f);
            Vector3 tip = new Vector3(sign * s.ThoraxR * 0.34f, y + s.ThoraxR * 0.34f, -s.AbdomenLen * 1.05f);
            b.Blade(root, tip, Vector3.forward, s.ThoraxR * 0.52f, s.Unit * 0.007f,
                    s.ThoraxR * 0.18f, s.Unit * 0.004f, 4, true, true);
            return b.Build("HOPPER_tegmen_" + s.Id + (right ? "_r" : "_l"));
        }

        /// <summary>
        /// 蚱蜢的一侧**后腿**：股节鼓成一个大梭形向后上方扬起，胫节再折回来落地。
        /// 这根"&gt;"形的粗腿就是它一眼可辨的轮廓。
        /// </summary>
        private static Mesh HopperHindLeg(Shape s, bool right)
        {
            var b = new Builder();
            float hipY = HipHeight(s);
            float L = s.HindLen;
            float t = s.HindThick;
            Vector3 knee = new Vector3(L * 0.46f, L * 0.62f, -L * 0.34f);
            Vector3 ankle = new Vector3(L * 0.66f, -hipY * 0.30f, -L * 0.10f);
            Vector3 foot = new Vector3(L * 0.74f, -hipY, L * 0.22f);
            // 股节：**鼓起来**（中段比两端粗得多 ⇒ 蚱蜢的大腿）
            b.Tube(Vector3.zero, knee, t * 1.00f, t * 0.92f, t * 0.72f, t * 0.66f, 6, 0.4f, true, true);
            Vector3 mid = Vector3.Lerp(Vector3.zero, knee, 0.42f);
            b.Tube(mid, Vector3.Lerp(mid, knee, 0.55f), t * 1.42f, t * 1.30f, t * 1.05f, t * 0.95f, 6, 0.4f, true, true);
            // 胫节 + 脚
            b.Tube(knee, ankle, t * 0.52f, t * 0.48f, t * 0.34f, t * 0.30f, 5, 0.2f, true, true);
            b.Tube(ankle, foot, t * 0.34f, t * 0.30f, t * 0.16f, t * 0.14f, 4, 0.2f, true, true);
            return b.Build("HOPPER_hindleg_" + s.Id + (right ? "_r" : "_l"));
        }

        /// <summary>蚱蜢的步足：细、短（前中两对，共 4 条）。</summary>
        private static Mesh HopperLeg(Shape s)
        {
            var b = new Builder();
            float reach = s.LegLen * 0.72f;
            float hipY = HipHeight(s);
            float t = s.LegThick * 0.62f;
            Vector3 knee = new Vector3(reach * 0.52f, reach * 0.10f, 0f);
            Vector3 foot = new Vector3(reach, -hipY, 0f);
            b.Tube(Vector3.zero, knee, t, t * 0.9f, t * 0.68f, t * 0.64f, 4, 0.4f, true, true);
            b.Tube(knee, foot, t * 0.68f, t * 0.64f, t * 0.20f, t * 0.18f, 4, 0.4f, true, true);
            return b.Build("HOPPER_leg_" + s.Id);
        }

        /// <summary>蚱蜢的触角：短、直、朝前上方（与蛾的长弯羽状触角明显不同）。</summary>
        private static Mesh HopperAntenna(Shape s)
        {
            var b = new Builder();
            float a = s.AntennaLen * 0.60f;
            float t = Mathf.Max(0.005f, s.LegThick * 0.22f);
            b.Tube(Vector3.zero, new Vector3(0f, a * 0.30f, a * 0.96f), t, t, t * 0.55f, t * 0.55f, 4, 0.4f, true, true);
            return b.Build("HOPPER_antenna_" + s.Id);
        }

        // ── ④ 瓢虫（pig）───────────────────────────────────────────────────

        /// <summary>瓢虫：一个**半球壳**（不是三段体节），底下接小头。</summary>
        private static Mesh LadybugShell(Shape s)
        {
            var b = new Builder();
            float rim = s.DomeR * 0.30f;                       // 壳缘离地
            float r = s.DomeR;
            // 壳：经 12 段 × 纬 4 层 ⇒ 圆而矮的半球（flat shading，逐面法线）
            b.Dome(new Vector3(0f, rim, 0f), r, r * 1.06f, s.DomeH, 4, 12, true);
            // 头：从壳前沿下方伸出一个小锥
            float hx = s.HeadR * 0.86f;
            b.Tube(new Vector3(0f, rim + s.DomeH * 0.16f, r * 0.86f),
                   new Vector3(0f, rim * 0.72f, r * 0.86f + s.HeadLen * 0.92f),
                   hx, hx * 0.80f,
                   hx * 0.72f, hx * 0.58f,
                   8, 0.25f, false, true);
            return b.Build("LADY_shell_" + s.Id);
        }

        /// <summary>瓢虫的重色零件：头冠（深色）+ 壳上两对斑点（读取"瓢虫"的那几对点）。</summary>
        private static Mesh LadybugAccent(Shape s)
        {
            var b = new Builder();
            float rim = s.DomeR * 0.30f;
            float r = s.DomeR;
            // 头冠：盖在头上面的一片深色小壳
            b.Tube(new Vector3(0f, rim + s.DomeH * 0.22f, r * 0.86f),
                   new Vector3(0f, rim * 0.80f, r * 0.90f + s.HeadLen * 0.70f),
                   s.HeadR * 0.92f, s.HeadR * 0.72f,
                   s.HeadR * 0.62f, s.HeadR * 0.46f,
                   6, 0.3f, true, true);
            // 斑点：**按壳面反解高度**再放一枚小扁顶（贴着曲面 ⇒ 读得出"瓢虫"的那几对点）
            // ★ 第一版把高度写成 `top − DomeH·k` ⇒ 算出来的 y 比壳面**低** ⇒ 四枚斑点全埋进壳里、
            //   一张图都看不见（v191 实测）✗
            //   现在用椭球方程反解 y：`y = rim + DomeH·√(1 − (x/rx)² − (z/rz)²)`，
            //   再压低 3% ⇒ 底边藏进壳内、只有上半个露出来 ✓
            float rx = r, rz = r * 1.06f;
            float spotR = r * 0.19f;
            for (int i = 0; i < 2; i++)
            {
                for (int k = 0; k < 2; k++)
                {
                    float sx = k == 0 ? 1f : -1f;
                    float px = sx * r * 0.40f;
                    float pz = (i == 0 ? 1f : -1f) * r * 0.40f;
                    float h2 = 1f - (px / rx) * (px / rx) - (pz / rz) * (pz / rz);
                    if (h2 <= 0.05f) continue;
                    float py = rim + s.DomeH * Mathf.Sqrt(h2) - s.DomeH * 0.03f;
                    b.Dome(new Vector3(px, py, pz), spotR, spotR, spotR * 0.75f, 2, 6, true);
                }
            }
            return b.Build("LADY_accent_" + s.Id);
        }

        /// <summary>瓢虫的腿：极短（壳底到地面就这么点距离），两段。</summary>
        private static Mesh LadybugLeg(Shape s)
        {
            var b = new Builder();
            float hipY = HipHeight(s);
            float reach = Mathf.Max(s.LegLen * 0.50f, hipY * 0.90f);
            float t = s.LegThick * 0.52f;
            Vector3 knee = new Vector3(reach * 0.58f, hipY * 0.18f, 0f);
            Vector3 foot = new Vector3(reach, -hipY, 0f);
            b.Tube(Vector3.zero, knee, t, t, t * 0.66f, t * 0.66f, 4, 0.4f, true, true);
            b.Tube(knee, foot, t * 0.66f, t * 0.66f, t * 0.20f, t * 0.20f, 4, 0.4f, true, true);
            return b.Build("LADY_leg_" + s.Id);
        }

        /// <summary>瓢虫的触角：两小截，短到几乎只够露个头。</summary>
        private static Mesh LadybugAntenna(Shape s)
        {
            var b = new Builder();
            float a = s.AntennaLen * 0.34f;
            float t = Mathf.Max(0.005f, s.LegThick * 0.20f);
            b.Tube(Vector3.zero, new Vector3(0f, a * 0.36f, a * 0.92f), t, t, t * 0.50f, t * 0.50f, 3, 0.4f, true, true);
            return b.Build("LADY_antenna_" + s.Id);
        }

        // ══════════════════════════════════════════════════════════════════════
        // 五、资源缓存：**每种虫只建一次**网格与材质
        //
        // ★ 禁止每只虫 new Material ★ 材质走 `ToonLook.Get()` —— 那是全局按
        //   (颜色, 描边粗细) 缓存的池子（`ToonLook.Cache`）✓
        //   ⇒ 5 种虫 × 3 份材质 = **最多 15 份**，跨战局复用、永不销毁 ✓
        //   逐实例的染色 / 霓虹一律写 MaterialPropertyBlock（`_TintMul` / `_NeonColor` / `_NeonWidth`），
        //   与 `ModelView` 同一套约定 ⇒ 共享材质**永不逐实例改写** ✓
        // ══════════════════════════════════════════════════════════════════════

        private sealed class Assets
        {
            public Shape Shape;
            /// <summary>BOSS / 旧几何：三段体节 + 重色件 + 腿 + 触角 + 鞘翅 + 大颚。</summary>
            public Mesh Body, Accent, Leg, Antenna, ElytraR, ElytraL, MandibleR, MandibleL;
            // ── ★ v191：物种零件槽（含义随物种变，见各自 build 分支）──
            public Mesh WingFR, WingFL;      // 蛾 2 片前翅 / 锹形虫 2 片鞘翅 / 蚱蜢 2 片覆翅
            public Mesh WingHR, WingHL;      // 蛾 2 片后翅（其余物种为 null）
            public Mesh PincerR, PincerL;    // 锹形虫 2 只大颚钳
            public Mesh HindR, HindL;        // 蚱蜢 2 条巨大后腿
            public Mesh EyeR, EyeL;          // 蚱蜢 2 只复眼
            // ★ v191：**材质不在这里** ★ —— 见 `Materials()` 上方的说明
        }

        private static readonly Dictionary<string, Assets> _assets = new Dictionary<string, Assets>();

        /// <summary>本工程全部怪物 id（顺序 = `enemies.json`；BOSS 是最后一项）。</summary>
        public static readonly string[] TypeIds = { "pig", "grunt", "shooter", "jumper", "boss" };

        /// <summary>
        /// 按 (id, boss) 取**网格**（只缓存几何，**不缓存材质**）。
        ///
        /// ★ v191 为什么把材质从这里摘出去 ★
        /// v181 的 `Assets` 把三份材质也一起缓存了，而键只有 `id` ⇒ 谁第一个调用
        /// `GetAssets(id, boss, color)`，它的颜色就被永久记住 ✗
        /// 而验收探针（`MeshStats` / `MeshBounds` / `MeshFingerprint`）只需要网格，
        /// 传的是 `Color.white` ⇒ **只要报告先跑一次，场上所有虫就都变成白色** ✗
        /// （v191 实测到了：先 `Report()` 再摆样，5 只虫全白，见 Docs/captures 的改前那两张）
        /// ⇒ 现在几何与材质分开：网格缓存只按 (id, boss)，材质每次按 `defColor` 现取
        /// （`ToonLook.Get` 自己按 (颜色, 描边) 缓存，所以**仍然是"最多 5×3 份"、仍然不 new**）✓
        /// </summary>
        private static Assets GetMeshes(string id, bool boss)
        {
            // ★ v201：飞蛾 BOSS **三种形态共用同一个 id** ⇒ 缓存键必须带上形态，
            //   否则"毛毛虫先生成、蛹再要"就会拿到毛毛虫那套几何（第二份真相的经典形状）✗
            string key = id + "|" + (int)FormFor(id, boss);
            if (_assets.TryGetValue(key, out Assets hit) && hit != null) return hit;

            Shape s = id == "moth" ? MothShapeOf(id, FormIndexOf(FormFor(id, boss))) : ShapeOf(id, boss);
            return BuildAssets(key, s);
        }

        /// <summary>★ v201：按**显式形态**取网格（表现层换形态时用这一条）。</summary>
        private static Assets GetMeshes(string id, bool boss, Species form)
        {
            string key = id + "|" + (int)form;
            if (_assets.TryGetValue(key, out Assets hit) && hit != null) return hit;
            Shape s = id == "moth" ? MothShapeOf(id, FormIndexOf(form)) : ShapeOf(id, boss);
            return BuildAssets(key, s);
        }

        /// <summary>该 (id, boss) 的**默认**形态：飞蛾 BOSS 默认取第三形态（统计 / 报告路径用它）。</summary>
        private static Species FormFor(string id, bool boss)
            => id == "moth" ? Species.MothQueen : SpeciesOf(id, boss);

        /// <summary>`Species` → 飞蛾三形态号（0/1/2）；其余物种返回 -1。</summary>
        private static int FormIndexOf(Species form)
            => form == Species.Larva ? 0 : (form == Species.Pupa ? 1 : (form == Species.MothQueen ? 2 : -1));

        private static Assets BuildAssets(string key, Shape s)
        {
            var a = new Assets { Shape = s };

            // ── ★ v191：按物种建零件 ★ ──
            // ★ BOSS 分支就是原来那 8 行，一个字没动（含调用顺序）★
            switch (s.Kind)
            {
                case Species.Giant:
                    a.Body = BodyMesh(s);
                    a.Accent = AccentMesh(s);
                    a.Leg = LegMesh(s);
                    a.Antenna = AntennaMesh(s);
                    a.ElytraR = ElytraMesh(s, true);
                    a.ElytraL = ElytraMesh(s, false);
                    a.MandibleR = MandibleMesh(s, true);
                    a.MandibleL = MandibleMesh(s, false);
                    break;

                case Species.Stag:                                          // 锹形虫
                    a.Body = StagBody(s);
                    a.Accent = StagAccent(s);
                    a.Leg = StagLeg(s);
                    a.Antenna = StagAntenna(s);
                    a.WingFR = StagElytra(s, true);
                    a.WingFL = StagElytra(s, false);
                    a.PincerR = StagPincer(s, true);
                    a.PincerL = StagPincer(s, false);
                    break;

                case Species.Moth:                                          // 蛾
                    a.Body = MothBody(s);
                    a.Leg = MothLeg(s);
                    a.Antenna = MothAntenna(s);
                    a.WingFR = MothWing(s, true, true);
                    a.WingFL = MothWing(s, false, true);
                    a.WingHR = MothWing(s, true, false);
                    a.WingHL = MothWing(s, false, false);
                    break;

                case Species.Hopper:                                        // 蚱蜢
                    a.Body = HopperBody(s);
                    a.Leg = HopperLeg(s);
                    a.Antenna = HopperAntenna(s);
                    a.WingFR = HopperTegmen(s, true);
                    a.WingFL = HopperTegmen(s, false);
                    a.HindR = HopperHindLeg(s, true);
                    a.HindL = HopperHindLeg(s, false);
                    a.EyeR = HopperEye(s, true);
                    a.EyeL = HopperEye(s, false);
                    break;

                // ══ ★ v201：飞蛾 BOSS 三形态 ★ ══
                //   · 毛毛虫 / 蛹 复用**既有的通用零件构造器**（`BodyMesh` / `AccentMesh` /
                //     `LegMesh` / `AntennaMesh`）—— 它们本来就是"三段体节 + 6 条腿 + 触角"，
                //     靠 `MothShapeOf` 的比例把它读成毛毛虫 / 纺锤 ✓ **零新增几何代码** ⇒
                //     `boss_frozen.txt` 与离线副本的那套拓扑判据完全不受影响 ✓
                //   · 飞蛾形态复用 `MothBody / MothWing / MothAntenna / MothLeg`（远程兵蛾那一套，
                //     已验证过），只把 `unit` 换成 2.4 ⇒ 同一套比例放大 2.4 倍 ✓
                case Species.Larva:                                         // 毛毛虫
                    a.Body = BodyMesh(s);
                    a.Accent = AccentMesh(s);
                    a.Leg = LegMesh(s);
                    a.Antenna = AntennaMesh(s);
                    break;

                case Species.Pupa:                                          // 蛹
                    a.Body = BodyMesh(s);
                    a.Accent = AccentMesh(s);
                    a.Leg = LegMesh(s);
                    a.Antenna = AntennaMesh(s);
                    a.WingFR = MothWing(s, true, true);
                    a.WingFL = MothWing(s, false, true);
                    break;

                case Species.MothQueen:                                     // 飞蛾（BOSS）
                    a.Body = MothBody(s);
                    a.Leg = MothLeg(s);
                    a.Antenna = MothAntenna(s);
                    a.WingFR = MothWing(s, true, true);
                    a.WingFL = MothWing(s, false, true);
                    a.WingHR = MothWing(s, true, false);
                    a.WingHL = MothWing(s, false, false);
                    break;

                default:                                                    // 瓢虫
                    a.Body = LadybugShell(s);
                    a.Accent = LadybugAccent(s);
                    a.Leg = LadybugLeg(s);
                    a.Antenna = LadybugAntenna(s);
                    break;
            }

            _assets[key] = a;
            return a;
        }

        /// <summary>
        /// 三份**共享**材质（本体 / 重色件 / 腿）。**不缓存**在这里原因见 `GetMeshes` 的说明；
        /// 内部走 `ToonLook.Get(...)`，那本身就是按 (颜色, 描边) 缓存的池子 ⇒ 全场仍是最多 15 份 ✓
        /// </summary>
        private static void Materials(Shape s, Color defColor,
                                      out Material body, out Material accent, out Material leg)
        {
            // 颜色：以数据表的 `color` 为底，按 id 的色相偏移**轻轻**推一点
            // ★ 第一版把偏移量给到 0.30 且底色用 1.0 亮度 ⇒ 数据色被淹掉
            //   （射手本该是紫、跳跳本该是橙，结果一律成了灰绿）✗
            //   现在：数据色是主角（85%），偏移只负责"同屏多只不撞色" ✓
            Color hue = Color.HSVToRGB(s.Hue, 0.30f, 0.85f);
            Color baseColor = Color.Lerp(defColor, hue, s.Boss ? 0.22f : 0.15f);
            baseColor.a = 1f;
            // ★ 深色零件：背甲 / 头角 / 尾刺 / 大颚 / 鞘翅 / 覆翅 / 复眼 ★
            //   第一版把它们做成了"骨白"，结果 BOSS 整块胸节被一片浅色壳盖住、
            //   远看就是一块米白色的板子（不像甲虫）✗
            //   改成本体色的**压暗版**：壳是深色硬壳，本体色仍是主角 ✓
            Color accentColor = Color.Lerp(baseColor * 0.32f, new Color(0.16f, 0.13f, 0.12f), 0.55f);
            accentColor.a = 1f;
            Color legColor = Color.Lerp(baseColor * 0.45f, new Color(0.13f, 0.11f, 0.10f), 0.60f);
            legColor.a = 1f;

            // 描边粗细按体型换算（与 `ToonLook.Apply` 同一条规则：最大边长 1.6%，夹 4mm~5cm）
            float outline = Mathf.Clamp(s.Unit * 0.6f * 0.016f, 0.004f, 0.05f);
            body = ToonLook.Get(baseColor, outline);
            accent = ToonLook.Get(accentColor, outline);
            leg = ToonLook.Get(legColor, outline * 0.7f);
        }

        /// <summary>已建好的类型数（诊断用：证明"每种只建一次"）。</summary>
        public static int CachedTypeCount => _assets.Count;

        /// <summary>
        /// ★ v201：飞蛾某一形态（0=毛毛虫 1=蛹 2=飞蛾）的网格统计 —— 探针用。
        /// 返回 `体节/腿/前翅` 三个零件的 `顶点v/三角t`（没有的零件记为 `-`）。
        /// ★ 读的就是 `GetMeshes` 的缓存 ⇒ 顺便证明"三种形态各自只建一次、键含形态" ✓
        /// </summary>
        public static string MothMeshStats(int form)
        {
            Assets a = GetMeshes("moth", true, MothFormOf(form));
            return Fmt(a.Body) + "/" + Fmt(a.Leg) + "/" + Fmt(a.WingFR);
            string Fmt(Mesh m) => m == null ? "-" : (m.vertexCount + "v" + (m.triangles.Length / 3) + "t");
        }

        /// <summary>一种虫的网格统计（顶点 / 三角，离线副本与报告用）。</summary>
        public static string MeshStats(string id, bool boss)
        {
            Assets a = GetMeshes(id, boss);
            var sb = new System.Text.StringBuilder();
            sb.Append(id).Append(boss ? "/boss" : "/bug");
            sb.Append(" body=").Append(Count(a.Body));
            sb.Append(" accent=").Append(a.Accent == null ? "-" : Count(a.Accent));
            sb.Append(" leg=").Append(Count(a.Leg));
            sb.Append(" antenna=").Append(Count(a.Antenna));
            if (boss)
            {
                // ★ BOSS 这一行逐字不变 ★
                sb.Append(" elytra_r=").Append(Count(a.ElytraR));
                sb.Append(" elytra_l=").Append(Count(a.ElytraL));
                sb.Append(" mandible_r=").Append(Count(a.MandibleR));
                sb.Append(" mandible_l=").Append(Count(a.MandibleL));
            }
            else
            {
                // ★ v191：物种零件（数量与名称都随物种变 ⇒ 报告里一眼能看出"构成不同"）
                if (a.WingFR != null) sb.Append(" wing_f_r=").Append(Count(a.WingFR));
                if (a.WingFL != null) sb.Append(" wing_f_l=").Append(Count(a.WingFL));
                if (a.WingHR != null) sb.Append(" wing_h_r=").Append(Count(a.WingHR));
                if (a.WingHL != null) sb.Append(" wing_h_l=").Append(Count(a.WingHL));
                if (a.PincerR != null) sb.Append(" pincer_r=").Append(Count(a.PincerR));
                if (a.PincerL != null) sb.Append(" pincer_l=").Append(Count(a.PincerL));
                if (a.HindR != null) sb.Append(" hindleg_r=").Append(Count(a.HindR));
                if (a.HindL != null) sb.Append(" hindleg_l=").Append(Count(a.HindL));
                if (a.EyeR != null) sb.Append(" eye_r=").Append(Count(a.EyeR));
                if (a.EyeL != null) sb.Append(" eye_l=").Append(Count(a.EyeL));
                sb.Append(" kind=").Append(a.Shape.Kind);
            }
            return sb.ToString();
            string Count(Mesh m) => m == null ? "-" : (m.vertexCount + "v/" + (m.triangles.Length / 3) + "t");
        }

        // ══════════════════════════════════════════════════════════════════════
        // 5.5、★ v191 验收探针：网格**几何指纹** ★
        //
        // 报告里原有的 `crc32=` 是 **id 字符串** 的 crc32（`Crc32(id)`）—— id 不同它必然不同，
        // 所以两种虫长得一模一样时它也照样"全不同" ⇒ **它证明不了网格不同** ✗
        // 这里算的是**顶点数据本身**：把每个零件的顶点坐标逐个折进同一个 crc32 ✓
        //   · 换颜色 / 换名字 / 换 id 都不会让它变 ✓
        //   · 顶点位置、数量、顺序、拓扑 任一不同 ⇒ 指纹必然不同 ✓
        // BOSS 的指纹在本轮（v191 昆虫重做）前后必须**逐位相同** —— 它一个字节都不许动 ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>把一组顶点的坐标折成 crc32（同数据同结果；不同几何几乎必然不同）。</summary>
        private static uint FoldMesh(Mesh m)
        {
            if (m == null) return 0u;
            Vector3[] vs = m.vertices;
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < vs.Length; i++)
            {
                FoldFloat(ref crc, vs[i].x);
                FoldFloat(ref crc, vs[i].y);
                FoldFloat(ref crc, vs[i].z);
            }
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>把一个 float 的 4 个字节折进 crc32（与 `Crc32` 同一个多项式）。</summary>
        private static void FoldFloat(ref uint crc, float v)
        {
            byte[] b = System.BitConverter.GetBytes(v);
            for (int i = 0; i < 4; i++)
            {
                crc ^= b[i];
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1u) != 0u ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        /// <summary>
        /// 一种虫的**几何指纹**（每个零件一行 + 全部零件的合并指纹）。
        /// 判据用法：5 个 id 的 `all=` 必须两两不同（证明不是同一个壳换颜色）；
        /// BOSS 的 `all=` 与 `parts=` 在改前改后必须逐位相同（证明没碰）✓
        /// </summary>
        public static string MeshFingerprint(string id, bool boss)
        {
            Assets a = GetMeshes(id, boss);
            var sb = new System.Text.StringBuilder();
            uint all = 0xFFFFFFFFu;
            AppendAll(a.Body);
            AppendAll(a.Leg);
            AppendAll(a.Antenna);
            AppendAll(a.Accent);
            if (boss)
            {
                AppendAll(a.ElytraR); AppendAll(a.ElytraL);
                AppendAll(a.MandibleR); AppendAll(a.MandibleL);
            }
            else
            {
                AppendAll(a.WingFR); AppendAll(a.WingFL);
                AppendAll(a.WingHR); AppendAll(a.WingHL);
                AppendAll(a.PincerR); AppendAll(a.PincerL);
                AppendAll(a.HindR); AppendAll(a.HindL);
                AppendAll(a.EyeR); AppendAll(a.EyeL);
            }

            sb.Append(id).Append(" fp=0x").Append((all ^ 0xFFFFFFFFu).ToString("X8"));
            sb.Append(" parts:");
            Part("body", a.Body); Part("accent", a.Accent); Part("leg", a.Leg); Part("antenna", a.Antenna);
            if (boss)
            {
                Part("elytra_r", a.ElytraR); Part("elytra_l", a.ElytraL);
                Part("mandible_r", a.MandibleR); Part("mandible_l", a.MandibleL);
            }
            else
            {
                Part("wing_f_r", a.WingFR); Part("wing_f_l", a.WingFL);
                Part("wing_h_r", a.WingHR); Part("wing_h_l", a.WingHL);
                Part("pincer_r", a.PincerR); Part("pincer_l", a.PincerL);
                Part("hindleg_r", a.HindR); Part("hindleg_l", a.HindL);
                Part("eye_r", a.EyeR); Part("eye_l", a.EyeL);
            }
            return sb.ToString();

            // 合并指纹 = 各零件顶点依次折进同一条流（顺序固定 ⇒ 可重放）
            void AppendAll(Mesh m)
            {
                if (m == null) return;
                Vector3[] vs = m.vertices;
                for (int i = 0; i < vs.Length; i++)
                {
                    FoldFloat(ref all, vs[i].x);
                    FoldFloat(ref all, vs[i].y);
                    FoldFloat(ref all, vs[i].z);
                }
            }
            void Part(string name, Mesh m)
            {
                if (m == null) return;
                sb.Append(' ').Append(name).Append("=0x").Append(FoldMesh(m).ToString("X8"))
                  .Append('/').Append(m.vertexCount).Append('v');
            }
        }

        /// <summary>一种虫每个零件的网格包围盒（模型空间，未乘显示缩放）。离线副本逐项比对用。</summary>
        public static string MeshBounds(string id, bool boss)
        {
            Assets a = GetMeshes(id, boss);
            var sb = new System.Text.StringBuilder();
            sb.Append(id);
            Append(sb, "body", a.Body);
            Append(sb, "leg", a.Leg);
            Append(sb, "antenna", a.Antenna);
            Append(sb, "accent", a.Accent);
            if (boss)
            {
                // ★ BOSS 这一段逐字不变（顺序也一样）★
                Append(sb, "elytra_r", a.ElytraR);
                Append(sb, "elytra_l", a.ElytraL);
                Append(sb, "mandible_r", a.MandibleR);
                Append(sb, "mandible_l", a.MandibleL);
            }
            else
            {
                // ★ v191：物种零件（顺序固定：前翅 / 后翅 / 钳 / 后腿 / 眼）
                Append(sb, "wing_f_r", a.WingFR);
                Append(sb, "wing_f_l", a.WingFL);
                Append(sb, "wing_h_r", a.WingHR);
                Append(sb, "wing_h_l", a.WingHL);
                Append(sb, "pincer_r", a.PincerR);
                Append(sb, "pincer_l", a.PincerL);
                Append(sb, "hindleg_r", a.HindR);
                Append(sb, "hindleg_l", a.HindL);
                Append(sb, "eye_r", a.EyeR);
                Append(sb, "eye_l", a.EyeL);
            }
            return sb.ToString();

            void Append(System.Text.StringBuilder b, string part, Mesh m)
            {
                if (m == null) return;
                Bounds bd = m.bounds;
                b.Append(' ').Append(part).Append("=min(")
                 .Append(bd.min.x.ToString("0.0000")).Append(',').Append(bd.min.y.ToString("0.0000"))
                 .Append(',').Append(bd.min.z.ToString("0.0000")).Append(")max(")
                 .Append(bd.max.x.ToString("0.0000")).Append(',').Append(bd.max.y.ToString("0.0000"))
                 .Append(',').Append(bd.max.z.ToString("0.0000")).Append(')');
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // 六、表现体：关节层级 + 动画
        //
        // 层级（动画只动 `Anim` 以下的关节，**不动 `Go`** —— 位置与朝向由玩法逻辑写）：
        //   Go            摆位（位置 / 朝向 / 统一缩放）
        //    └ Anim       整体前后位移（冲锋突进用）
        //       ├ BodyRoot 体节俯仰 + 上下起伏
        //       │   ├ Body / Accent / Antenna×2
        //       │   ├ [BOSS] Elytra×2 / Mandible×2        ← v181 原样
        //       │   ├ [蛾]   WingFore×2 / WingHind×2
        //       │   ├ [锹形] Elytra×2 / Pincer×2
        //       │   └ [蚱蜢] Tegmen×2 / Eye×2
        //       └ Leg0..5  锹形/蛾/瓢虫 = 6 条步足；蚱蜢 = 4 条步足 + 2 条巨大后腿
        // ══════════════════════════════════════════════════════════════════════

        public sealed class BugView
        {
            public GameObject Go { get; private set; }
            public string Id { get; private set; }
            public bool Boss { get; private set; }
            public Shape S { get; private set; }

            /// <summary>物种（v191）。BOSS = `Species.Giant`。</summary>
            public Species Kind => S.Kind;

            /// <summary>体节网格在模型空间里的高度（米，未乘显示缩放）。特写机位按它取景。</summary>
            public float ModelHeight { get; private set; }

            private Transform _anim, _bodyRoot;
            private readonly Transform[] _legs = new Transform[6];
            private readonly float[] _legPhase = new float[6];
            private readonly float[] _legSwing = new float[6];
            private readonly Quaternion[] _legBase = new Quaternion[6];
            /// <summary>true = 蚱蜢的巨大后腿（动画完全不同：绕髋上扬 / 弹开）。</summary>
            private readonly bool[] _legHind = new bool[6];
            private Transform[] _elytra;
            private Transform[] _mandibles;
            private Transform[] _antennae;
            private Vector3[] _elytraRest;
            private Quaternion[] _antennaRest;
            /// <summary>v191：翅 / 覆翅 / 鞘翅（蛾 4 片、锹形 2 片、蚱蜢 2 片）。</summary>
            private Transform[] _wings;
            private Quaternion[] _wingRest;
            /// <summary>v191：锹形虫的大颚钳。</summary>
            private Transform[] _pincers;
            private Renderer[] _rends;
            private MaterialPropertyBlock _mpb;

            private float _phase;
            private float _frozenPhase = -1f;
            private float _stateTime;
            private ModelView.State _state = ModelView.State.Idle;
            private float _legAmpDeg = 26f;
            private float _liftAmpDeg = 12f;
            private int _atkKind = -1;
            private float _atkAge = -1f;

            private Color _tint = Color.white;
            private Color _neon = new Color(0f, 0f, 0f, 0f);
            private float _neonWidth = 0.02f;

            /// <summary>当前状态（`ModelView.State` 的那一套，不另起）。</summary>
            public ModelView.State Current => _state;

            /// <summary>累积相位（秒）。截图/诊断读它。</summary>
            public float Phase => _phase;

            /// <summary>当前状态已经过的秒数。</summary>
            public float StateTime => _stateTime;

            /// <summary>当前攻击姿势（0=无 1=环形弹幕 2=冲锋蓄力 3=冲锋突进）。</summary>
            public int AttackPose => _atkKind;

            internal static BugView Build(Transform parent, string id, bool boss, Color defColor, float displayScale)
                => Build(parent, id, boss, defColor, displayScale, FormFor(id, boss));

            /// <summary>★ v201：按**显式形态**建体（飞蛾三形态用这一条；其余物种与旧调用等价）。</summary>
            internal static BugView Build(Transform parent, string id, bool boss, Color defColor,
                                          float displayScale, Species form)
            {
                Assets a = GetMeshes(id, boss, form);
                Shape s = a.Shape;
                // ★ v191：材质按 `defColor` **现取**（不跟着网格缓存走）—— 见 `GetMeshes` 的说明 ★
                Materials(s, defColor, out Material matBody, out Material matAccent, out Material matLeg);

                var view = new BugView { Id = id, Boss = boss, S = s };
                view.Go = new GameObject("BUG_" + id);
                view.Go.transform.SetParent(parent, false);
                view.Go.transform.localRotation = Quaternion.identity;
                view.Go.transform.localScale = Vector3.one * displayScale;

                view._anim = Child(view.Go.transform, "Anim", Vector3.zero);
                view._bodyRoot = Child(view._anim, "BodyRoot", Vector3.zero);

                var rends = new List<Renderer>(16);
                rends.Add(Part(view._bodyRoot, "Body", a.Body, matBody));
                if (a.Accent != null) rends.Add(Part(view._bodyRoot, "Accent", a.Accent, matAccent));
                view.ModelHeight = a.Body.bounds.max.y;

                float headZ = s.ThoraxLen * 0.5f;
                float headY = s.BodyY * 1.02f;

                // ── 一对触角：挂在头节前上两侧，靠**偏航**分左右（网格只含 Y/Z ⇒ 无需镜像）──
                view._antennae = new Transform[2];
                view._antennaRest = new Quaternion[2];
                for (int i = 0; i < 2; i++)
                {
                    bool right = i == 0;
                    Transform t = Child(view._bodyRoot, right ? "AntennaR" : "AntennaL",
                        AntennaAnchor(s, right));
                    view._antennaRest[i] = AntennaRest(s, right);
                    t.localRotation = view._antennaRest[i];
                    rends.Add(Part(t, "AntennaMesh", a.Antenna, matLeg));
                    view._antennae[i] = t;
                }

                if (boss && s.Kind == Species.Giant)
                {
                    // ══ ★ BOSS：v181 原样（鞘翅 + 大颚）★ ══
                    // ★ v201：判据加了 `&& s.Kind == Species.Giant` —— 飞蛾在数据表里也是 `Boss`，
                    //   但它的零件是翅 / 触角，不是鞘翅 / 大颚；不加这一条它会走进"巨型甲虫"分支 ✗
                    //   对**旧版 BOSS** 而言这一条恒为真（`SpeciesOf("boss", true) = Giant`）
                    //   ⇒ 下面整段逐字不变 ✓
                    // ── 两片鞘翅：铰链在胸节背侧，绕 Z 轴外翻张开 ──
                    view._elytra = new Transform[2];
                    view._elytraRest = new Vector3[2];
                    for (int i = 0; i < 2; i++)
                    {
                        bool right = i == 0;
                        Vector3 rest = new Vector3((right ? 1f : -1f) * s.ThoraxR * 0.30f,
                                                   s.BodyY + s.ThoraxR * 0.55f, 0f);
                        Transform t = Child(view._bodyRoot, right ? "ElytraR" : "ElytraL", rest);
                        view._elytraRest[i] = rest;
                        // 鞘翅用**本体材质**：甲虫的鞘翅就是背甲本身，用深色会让它消失在一片黑里
                        rends.Add(Part(t, "ElytraMesh", right ? a.ElytraR : a.ElytraL, matBody));
                        view._elytra[i] = t;
                    }
                    // ── 一对大颚：挂在头节前端两侧，绕 Y 轴开合 ──
                    view._mandibles = new Transform[2];
                    for (int i = 0; i < 2; i++)
                    {
                        bool right = i == 0;
                        Transform t = Child(view._bodyRoot, right ? "MandibleR" : "MandibleL",
                            new Vector3((right ? 1f : -1f) * s.HeadR * 0.62f, headY - s.HeadR * 0.15f,
                                        headZ + s.HeadLen * 1.02f));
                        rends.Add(Part(t, "MandibleMesh", right ? a.MandibleR : a.MandibleL, matAccent));
                        view._mandibles[i] = t;
                    }
                }
                else
                {
                    // ══ ★ v191：物种零件 ★ ══
                    switch (s.Kind)
                    {
                        case Species.Stag:
                        {
                            // 两片鞘翅（铰链在胸背，绕 Z 外翻）
                            view._wings = new Transform[2];
                            view._wingRest = new Quaternion[2];
                            for (int i = 0; i < 2; i++)
                            {
                                bool right = i == 0;
                                Transform t = Child(view._bodyRoot, right ? "ElytraR" : "ElytraL", Vector3.zero);
                                view._wingRest[i] = Quaternion.identity;
                                t.localRotation = view._wingRest[i];
                                rends.Add(Part(t, "ElytraMesh", right ? a.WingFR : a.WingFL, matAccent));
                                view._wings[i] = t;
                            }
                            // 一对大颚（挂在头端两侧，绕 Y 开合）
                            view._pincers = new Transform[2];
                            for (int i = 0; i < 2; i++)
                            {
                                bool right = i == 0;
                                float hx = PincerAnchorX(s, right);
                                Transform t = Child(view._bodyRoot, right ? "PincerR" : "PincerL",
                                    new Vector3(hx, s.BodyY + s.Unit * 0.020f,
                                                s.ThoraxLen * 0.62f + s.HeadLen * 1.18f));
                                rends.Add(Part(t, "PincerMesh", right ? a.PincerR : a.PincerL, matAccent));
                                view._pincers[i] = t;
                            }
                            break;
                        }
                        case Species.Moth:
                        {
                            // ★ 4 片翅：前翅大、后翅小，铰链在胸部两侧，绕 Z 上抬 / 下压 = 扇翅
                            view._wings = new Transform[4];
                            view._wingRest = new Quaternion[4];
                            for (int i = 0; i < 4; i++)
                            {
                                bool right = (i % 2) == 0;
                                bool fore = i < 2;
                                Mesh mesh = fore ? (right ? a.WingFR : a.WingFL) : (right ? a.WingHR : a.WingHL);
                                Transform t = Child(view._bodyRoot,
                                    (right ? "WingR" : "WingL") + (fore ? "Fore" : "Hind"), Vector3.zero);
                                view._wingRest[i] = Quaternion.identity;
                                t.localRotation = view._wingRest[i];
                                rends.Add(Part(t, fore ? "WingForeMesh" : "WingHindMesh", mesh, matBody));
                                view._wings[i] = t;
                            }
                            break;
                        }
                        // ══ ★ v201：飞蛾 BOSS 的二 / 三形态 ★ ══
                        //   毛毛虫（Larva）走 `default` 以外的最小集合：只有体节 + 6 腿 + 触角
                        //   ⇒ 不需要任何翅 / 鞘翅 / 大颚（`break` 掉就什么都不建）✓
                        case Species.Pupa:
                        {
                            // 蛹：只留一对**翅芽**（贴在壳侧、几乎不动）—— 让"它其实是一只蛾"有预告 ✓
                            if (a.WingFR != null)
                            {
                                view._wings = new Transform[2];
                                view._wingRest = new Quaternion[2];
                                for (int i = 0; i < 2; i++)
                                {
                                    bool right = i == 0;
                                    Transform t = Child(view._bodyRoot, right ? "WingBudR" : "WingBudL", Vector3.zero);
                                    view._wingRest[i] = Quaternion.Euler(0f, (right ? 1f : -1f) * 14f, 0f);
                                    t.localRotation = view._wingRest[i];
                                    rends.Add(Part(t, "WingBudMesh", right ? a.WingFR : a.WingFL, matAccent));
                                    view._wings[i] = t;
                                }
                            }
                            break;
                        }
                        case Species.MothQueen:
                        {
                            // ★ 飞蛾 BOSS：与远程兵蛾同一套 4 片翅的挂法，但它更大、且用**本体材质**
                            //   （BOSS 的翅要能被红色霓虹描边读出来，用深色件会看不见）✓
                            view._wings = new Transform[4];
                            view._wingRest = new Quaternion[4];
                            for (int i = 0; i < 4; i++)
                            {
                                bool right = (i % 2) == 0;
                                bool fore = i < 2;
                                Mesh mesh = fore ? (right ? a.WingFR : a.WingFL) : (right ? a.WingHR : a.WingHL);
                                Transform t = Child(view._bodyRoot,
                                    (right ? "WingR" : "WingL") + (fore ? "Fore" : "Hind"), Vector3.zero);
                                view._wingRest[i] = Quaternion.identity;
                                t.localRotation = view._wingRest[i];
                                rends.Add(Part(t, fore ? "WingForeMesh" : "WingHindMesh", mesh, matBody));
                                view._wings[i] = t;
                            }
                            break;
                        }
                        case Species.Hopper:
                        {
                            // 2 片覆翅（贴在背上，几乎不动）+ 2 只复眼
                            view._wings = new Transform[2];
                            view._wingRest = new Quaternion[2];
                            for (int i = 0; i < 2; i++)
                            {
                                bool right = i == 0;
                                Transform t = Child(view._bodyRoot, right ? "TegmenR" : "TegmenL", Vector3.zero);
                                view._wingRest[i] = Quaternion.identity;
                                t.localRotation = view._wingRest[i];
                                rends.Add(Part(t, "TegmenMesh", right ? a.WingFR : a.WingFL, matAccent));
                                view._wings[i] = t;
                            }
                            for (int i = 0; i < 2; i++)
                            {
                                bool right = i == 0;
                                Transform t = Child(view._bodyRoot, right ? "EyeR" : "EyeL",
                                    EyeAnchor(s, right));
                                rends.Add(Part(t, "EyeMesh", right ? a.EyeR : a.EyeL, matAccent));
                            }
                            break;
                        }
                    }
                }

                // ── 六条腿 ──
                //   锹形 / 蛾 / 瓢虫：6 条步足（三对）
                //   蚱蜢：0..3 = 4 条步足，4..5 = **2 条巨大后腿**（网格与动画都不同）
                //   BOSS：v181 原样（`LegHip` + 三角步态）
                var legRng = new Rng32(Crc32(id) ^ 0x51ED2701u);
                int legCount = 6;
                for (int i = 0; i < legCount; i++)
                {
                    bool hind = !boss && s.Kind == Species.Hopper && i >= 4;
                    Vector3 hip = boss ? LegHip(s, i) : HipOf(s, i);
                    Transform t = Child(view._anim, (hind ? "HindLeg" : "Leg") + i, hip);
                    bool right = (i % 2) == 0;
                    view._legBase[i] = right ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
                    // 三对腿**交替**：A = {1,2,5}、B = {0,3,4} ⇒ 三角步态
                    float basePhase = (i == 1 || i == 2 || i == 5) ? Mathf.PI : 0f;
                    view._legPhase[i] = basePhase + (legRng.Next01() - 0.5f) * 0.35f;
                    view._legHind[i] = hind;
                    t.localRotation = view._legBase[i];
                    Mesh legMesh = a.Leg;
                    if (hind) legMesh = right ? a.HindR : a.HindL;
                    rends.Add(Part(t, "LegMesh", legMesh, hind ? matAccent : matLeg));
                    view._legs[i] = t;
                }

                view._rends = rends.ToArray();
                view._mpb = new MaterialPropertyBlock();
                view._neonWidth = Mathf.Clamp(s.Unit * 0.02f, 0.012f, 0.09f);
                view.PushBlock();
                view.Tick(0f);
                return view;
            }

            /// <summary>触角挂点（物种化：蛾的触角长在头顶、瓢虫的几乎贴着壳缘）。</summary>
            private static Vector3 AntennaAnchor(Shape s, bool right)
            {
                float sx = right ? 1f : -1f;
                switch (s.Kind)
                {
                    case Species.Moth:
                        return new Vector3(sx * s.HeadR * 0.50f, s.BodyY + s.ThoraxR * 0.72f,
                                           s.ThoraxLen * 0.34f + s.HeadLen * 0.60f);
                    case Species.Hopper:
                        return new Vector3(sx * s.HeadR * 0.40f, s.BodyY + s.HeadR * 0.92f,
                                           s.ThoraxLen * 0.40f + s.HeadLen * 0.78f);
                    case Species.Ladybug:
                        return new Vector3(sx * s.HeadR * 0.46f, s.DomeR * 0.30f + s.DomeH * 0.10f,
                                           s.DomeR * 0.86f + s.HeadLen * 0.30f);
                    default:   // Stag
                        return new Vector3(sx * s.HeadR * 0.50f, s.BodyY + s.HeadR * 0.86f,
                                           s.ThoraxLen * 0.62f + s.HeadLen * 0.98f);
                }
            }

            /// <summary>触角静息朝向（物种化）。</summary>
            private static Quaternion AntennaRest(Shape s, bool right)
            {
                float sx = right ? 1f : -1f;
                switch (s.Kind)
                {
                    case Species.Moth: return Quaternion.Euler(-26f, sx * 12f, 0f);
                    case Species.Hopper: return Quaternion.Euler(8f, sx * 20f, 0f);
                    case Species.Ladybug: return Quaternion.Euler(-12f, sx * 26f, 0f);
                    default: return Quaternion.Euler(-18f, sx * 16f, 0f);   // Stag
                }
            }

            /// <summary>锹形虫大颚钳的挂点横向位置。</summary>
            private static float PincerAnchorX(Shape s, bool right)
            {
                return (right ? 1f : -1f) * s.HeadR * 0.72f;
            }

            /// <summary>蚱蜢复眼的挂点。</summary>
            private static Vector3 EyeAnchor(Shape s, bool right)
            {
                float sx = right ? 1f : -1f;
                return new Vector3(sx * s.HeadR * 0.86f,
                                   s.BodyY + s.HeadR * 0.42f,
                                   s.ThoraxLen * 0.40f + s.HeadLen * 0.50f);
            }

            private static Transform Child(Transform parent, string name, Vector3 localPos)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = localPos;
                return go.transform;
            }

            private static Renderer Part(Transform parent, string name, Mesh mesh, Material mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;               // ← 共享材质：不 new、不实例化 ✓
                return mr;
            }

            /// <summary>池里取出来复用时把表现状态归零。</summary>
            internal void ResetForReuse()
            {
                _phase = 0f;
                _frozenPhase = -1f;
                _stateTime = 0f;
                _state = ModelView.State.Idle;
                _legAmpDeg = 26f;
                _liftAmpDeg = 12f;
                _atkKind = -1;
                _atkAge = -1f;
                _tint = Color.white;
                _neon = new Color(0f, 0f, 0f, 0f);
                PushBlock();
                Tick(0f);
            }

            // ── 状态机：**沿用 `ModelView.State`** ─────────────────────────────
            public void Play(ModelView.State st)
            {
                if (_state == st) return;
                _state = st;
                _stateTime = 0f;
                switch (st)
                {
                    case ModelView.State.Walk: _legAmpDeg = 26f; _liftAmpDeg = 12f; break;
                    case ModelView.State.Idle: _legAmpDeg = 4.5f; _liftAmpDeg = 2.0f; break;
                    case ModelView.State.Attack: _legAmpDeg = 16f; _liftAmpDeg = 6f; break;
                    case ModelView.State.Hit: _legAmpDeg = 30f; _liftAmpDeg = 16f; break;
                    case ModelView.State.Death: _legAmpDeg = 2f; _liftAmpDeg = 0f; break;
                    default: _legAmpDeg = 20f; _liftAmpDeg = 8f; break;
                }
            }

            public void SetFacing(float yawDegrees)
            {
                Go.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            }

            /// <summary>锁定相位（截图 / 诊断用）。负数恢复自由播放。</summary>
            public void FreezePhase(float phase)
            {
                _frozenPhase = phase;
                if (phase >= 0f) { _phase = phase; Tick(0f); }
            }

            /// <summary>BOSS 攻击姿势。kind：0=无 1=环形弹幕 2=冲锋蓄力 3=冲锋突进。</summary>
            public void SetAttackPose(int kind, float age)
            {
                _atkKind = kind;
                _atkAge = age;
            }

            public void SetTint(Color mul)
            {
                if (_tint == mul) return;
                _tint = mul;
                PushBlock();
            }

            public void SetNeon(Color color, float widthMeters = 0f)
            {
                bool same = _neon == color && (widthMeters <= 0.001f || Mathf.Approximately(_neonWidth, widthMeters));
                if (same) return;
                _neon = color;
                if (widthMeters > 0.001f) _neonWidth = widthMeters;
                PushBlock();
            }

            private void PushBlock()
            {
                if (_rends == null) return;
                for (int i = 0; i < _rends.Length; i++)
                {
                    Renderer r = _rends[i];
                    if (r == null) continue;
                    _mpb.Clear();
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor(TintMulId, _tint);
                    _mpb.SetColor(NeonColorId, _neon);
                    _mpb.SetFloat(NeonWidthId, _neonWidth);
                    r.SetPropertyBlock(_mpb);
                }
            }

            // ── 动画 ────────────────────────────────────────────────────────
            /// <summary>
            /// 推进相位并写关节。**只动 Anim 以下的关节，不动 Go** ✓
            ///
            /// `stepMeters` = 这一帧世界位置实际挪了多少米（`SyncEnemies` 喂）。
            /// 省略或传负数 ⇒ 相位照旧**按时间**推进（验收台 `Pose()` / `BugShowcase` / `Build` 那条路）✓
            /// </summary>
            public void Tick(float dt, float stepMeters = -1f)
            {
                // ★ v196：相位增量 = `dt × (实际速度 ÷ 设计速度)` —— 见下方"相位 ∝ 位移"那一段 ★
                if (dt > 0f && _frozenPhase < 0f) _phase += dt * PhaseRate(stepMeters, dt);
                _stateTime += dt;
                UpdateWalkBlend(dt);

                Shape s = S;
                if (s.LegFreq <= 0.001f) return;

                // ★ v191：非 BOSS 走**按物种**的那一套动画；BOSS 原样走下面 v181 的代码 ★
                if (s.Kind != Species.Giant) { TickNimble(s, dt); return; }

                float w = Mathf.PI * 2f * s.LegFreq;
                _swingAmpNow = _legAmpDeg;      // 探针口径：BOSS 的振幅不参与走/停过渡
                for (int i = 0; i < 6; i++)
                {
                    Transform leg = _legs[i];
                    if (leg == null) continue;
                    float p = w * _phase + _legPhase[i];
                    float swing = _legAmpDeg * Mathf.Sin(p);
                    float lift = _liftAmpDeg * Mathf.Max(0f, Mathf.Cos(p));
                    _legSwing[i] = swing;
                    leg.localRotation = _legBase[i] * Quaternion.Euler(0f, swing, lift);
                }

                // ── 身体轻微起伏（2 倍腿频：每对腿落地一次）──
                float bob = Mathf.Sin(w * _phase * 2f) * (0.010f + 0.012f * Mathf.Abs(Mathf.Sin(w * _phase)));
                bob *= s.Unit;
                // ★ v191-b：待机时的起伏衰减也走 `_motionW`（原来是一帧从 1 跳到 0.35）★
                if (_state == ModelView.State.Death) bob *= 0.15f;
                else bob *= Mathf.Lerp(0.35f, 1f, _motionW);
                float breathe = _state == ModelView.State.Idle ? Mathf.Sin(_phase * 1.7f) * 0.004f * s.Unit : 0f;

                // ── BOSS 攻击姿势（覆盖体节俯仰 / 升降 / 鞘翅 / 大颚 / 触角 / 整体位移）──
                float pitchDeg = 0f, drop = 0f, elytraDeg = 0f, abdomenDeg = 0f,
                      antennaDeg = 0f, mandibleDeg = 0f, lean = 0f;
                if (_atkKind > 0 && _atkAge >= 0f)
                    PoseOf(_atkKind, _atkAge, s, out pitchDeg, out drop, out elytraDeg,
                           out abdomenDeg, out antennaDeg, out mandibleDeg, out lean);

                _bodyRoot.localPosition = new Vector3(0f, bob + breathe - drop, 0f);
                _bodyRoot.localRotation = Quaternion.Euler(pitchDeg, 0f, 0f);
                _anim.localPosition = new Vector3(0f, 0f, lean);

                if (_elytra != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Transform t = _elytra[i];
                        if (t == null) continue;
                        float roll = (i == 0 ? 1f : -1f) * elytraDeg;      // 外翻 = 外侧向上
                        t.localRotation = Quaternion.Euler(0f, 0f, roll);
                        t.localPosition = _elytraRest[i] + new Vector3(0f, abdomenDeg * 0.0016f * s.Unit, 0f);
                    }
                }
                if (_mandibles != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Transform t = _mandibles[i];
                        if (t == null) continue;
                        // 右颚内合 = 负偏航；左颚相反 ⇒ 两个一起张/合
                        t.localRotation = Quaternion.Euler(0f, (i == 0 ? -1f : 1f) * mandibleDeg, 0f);
                    }
                }
                if (_antennae != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Transform t = _antennae[i];
                        if (t == null) continue;
                        // 正 = 向后下压，负 = 向前上扬（绕 X）
                        t.localRotation = Quaternion.Euler(antennaDeg, 0f, 0f) * _antennaRest[i];
                    }
                }
            }

            // ══════════════════════════════════════════════════════════════════
            // ★ v196：**相位增量与实际位移成正比** —— "脚贴地不滑"的根治 ★
            //
            // ── 病根（量出来的，不是推断）──
            // 旧实现是 `_phase += dt` ⇒ 相位**只跟时间走**，与敌人这一帧挪了几米毫无关系 ✗
            // 于是"相位 ÷ 位移"这个比会随移速漂 —— 验收探针（`ProbeV191Motion`，锁实例 id，900 帧）
            // 里三只**移速完全相同**的 `grunt` 读出 **9.9 / 15.8 / 19.5 rad/m**（差一倍）：
            // 区别只在于"这一段里它到底走了多少帧"✓
            // ⇒ 减速 / 被挤住时腿还在按原速倒腾 ⇒ 脚在原地刨地 = **打滑** ✓
            //
            // ── 为什么选这条，而不是"把 τ 再加大"──
            // `WalkBlendTau` 只作用在**振幅**上（走 26° ↔ 停 0.72° 那次切换），
            // 它压不动"时间驱动的相位" ⇒ 打滑照旧；而 τ 一加大，起步/收腿就变"肉"（有延迟）✗
            // 相位 ∝ 位移是**定义上的根治**：一个步态周期走多远是常数，脚自然不打滑 ✓
            //
            // ── 实现：`rate = 实际速度 ÷ 设计速度` ──
            //   `_phase += dt × rate`，`rate = (位移/dt) ÷ Def.MoveSpeed`
            //   · 按设计速度走（没减速、没被挤住）⇒ rate = **1** ⇒ 与改前**逐位相同** ✓
            //     改的只是"慢下来以后怎么办"，**正常行走一个数都不变** ✓
            //   · 被冻 / 减速 / 挤在同伴身上走不动 ⇒ rate 跟着掉 ⇒ 腿慢下来 ⇒ 不打滑 ✓
            //   · 位移比设计速度还大（击退 / 蚱蜢起跳）⇒ 夹到 `PhaseDriveMax`，
            //     不许腿倒腾成一片模糊 ✗
            //   · `stepMeters < 0`（没喂位移：`Pose()` / `BugShowcase` / `Build` 那条路）
            //     ⇒ rate = 1 ⇒ **摆样、截图、`REPORT` 的腿相位证据都与改前逐位相同** ✓
            //   · `dt == 0`（冻结相位截图）⇒ 相位原地不动 ✓
            //
            // ── 为什么只在 `Walk` 态耦合，且不碰 BOSS ──
            // `Idle` 的呼吸 / 触角摆动 / 大颚开合是**时间驱动**的表演动作；
            // 站着位移 = 0 ⇒ 若也耦合，整只虫会**冻住** ✗（比打滑难看得多）
            // 受击 / 攻击 / 死亡同理（"一下就变"的姿态不该跟着位移慢放）✓
            // BOSS 的模型与动画本轮**都不在范围内**（用户："boss 暂时不调整"）⇒ 它的 rate 恒为 1 ✓
            // ══════════════════════════════════════════════════════════════════

            /// <summary>
            /// 相位最多能比"设计步频"快多少倍。★ **取 1.0：只许变慢，不许变快** ★
            ///
            /// ── 为什么上限是 1，而不是 1.6 之类 ──
            /// 比"设计速度"更快的那一头只有三种来源：击退、蚱蜢起跳、BOSS 冲锋。
            /// 它们都是**一两帧的位移尖峰**（实测蚱蜢一次起跳单帧 3.6m ⇒ 要跟上得 22 个步态周期/帧，
            /// 那是物理上不存在的）⇒ 让它"尽量快"只会得到腿上的一片模糊 ✗
            /// 而夹在 1 之后有一条**可以证明**的性质：
            ///   腿角 = 振幅 × sin(ω·相位)，`d²角/dt² ∝ rate²` ⇒ `rate ≤ 1` ⇒
            ///   **`max|Δ²|` 在数学上不可能比改前更大**（`rate = 1` 时逐位相同）✓
            /// 这正是验收要的"降或持平"，而且是推出来的，不是量出来碰巧的 ✓
            /// </summary>
            private const float PhaseDriveMax = 1f;

            /// <summary>
            /// **设计步行速度**（米/秒）。由 `SyncEnemies` 每帧从 `EnemyDef.MoveSpeed` 喂进来 ——
            /// 不在这里猜：`Shape` 里没有"移速"这个概念，移速是数据表的事实 ✓
            /// `< 0` = 从没喂过（验收台 / 摆样）⇒ 相位照旧按时间走 ✓
            /// </summary>
            private float _gaitSpeed = -1f;

            /// <summary>喂设计步行速度（`SyncEnemies` 每帧一次）。验收台不调 ⇒ 退回时间驱动。</summary>
            internal void SetGaitSpeed(float mps)
            {
                if (mps > 0.01f) _gaitSpeed = mps;
            }

            /// <summary>本帧相位该按**几倍时间速度**推进（1 = 与改前完全一样）。</summary>
            private float PhaseRate(float stepMeters, float dt)
            {
                if (_state != ModelView.State.Walk)                 // 见上方"为什么只在 Walk 态"
                {
                    _speedEma = -1f;                                // 离开 Walk：下一次重新播种（不吃旧速度）
                    return 1f;
                }
                if (S.Kind == Species.Giant) return 1f;             // BOSS 不动
                if (stepMeters < 0f || dt <= 1e-5f || _gaitSpeed <= 0.01f) return 1f;

                // ★ 单帧位移**必须先低通**（实测教训，与本仓库 `_dirEma` 同一课）★
                //   第一版直接拿 `位移/dt` 当速率 ⇒ 逐帧读数一跳，速率就一跳 ⇒ 相位被注入噪声：
                //   `grunt` 某个实例的 `max|Δ²|` 反而从 **11.99° 涨到 17.64°** ✗
                //   噪声来源很具体：敌人之间**互相挤压**（分离约束）每帧把位置推 1~5cm，
                //   方向还随机 ⇒ 瞬时速度在 2.5~3.5 m/s 之间抖，而 dt 只有 17ms ✓
                float v = stepMeters / dt;
                if (_speedEma < 0f) _speedEma = v;                  // 第一帧直接吃（与 `SmoothFacing` 同一条约定）
                else _speedEma += (v - _speedEma) * (1f - Mathf.Exp(-dt / SpeedEmaTau));
                return Mathf.Clamp(_speedEma / _gaitSpeed, 0f, PhaseDriveMax);
            }

            /// <summary>
            /// 观测速度的低通时间常数（秒）。80ms ⇒ 60fps 时 α≈0.19、等效记忆约 5 帧：
            /// 足够把"互相挤压"的那点抖动抹平，又短到"被冻住 → 腿停下来"在 3~4 帧内看得出 ✓
            /// </summary>
            private const float SpeedEmaTau = 0.08f;

            /// <summary>上一帧的低通速度（米/秒；< 0 = 还没播种）。</summary>
            private float _speedEma = -1f;

            // ══════════════════════════════════════════════════════════════════
            // ★ v191-b：**走 ↔ 停 的幅度不再"一帧切换"** ★
            //
            // ── 病根（实测，不是推断）──
            // 旧实现按**状态**直接取幅度：`swingAmp = walk ? 26° : idle ? 26°×0.16 : …`
            // ⇒ 敌人**走到位的那一刻**（`Moved()` 判据翻转），腿角在**一帧内**从
            //   `26°·sin(p)` 跳到 `4.2°·sin(p)` ✗ 而 `p`（相位）是连续的 ⇒ 差多少全看运气：
            //   同一相位下逐点对拍（`BugShowcase.Pose("grunt:walk:t")` vs `Pose("grunt:idle:t")`）：
            //     t=0.03  L0 21.3° → 0.6°   L1 −23.6° → −0.7°   ⇒ **跳 22.9°**
            //     t=0.06  L0 23.4° → 0.6°                        ⇒ **跳 22.8°**
            //     t=0.12  L0 −21.8° → −0.6° L4 −23.7° → −0.7°    ⇒ **跳 23.0°**
            //   ⇒ 最大跳变 = 22°~23°（= `_legAmpDeg × (1 − 0.16)` 的极值）✓
            //   实测同一帧的活体读数（`MOTION2`，dt=21ms）：
            //     grunt 实例#50 腿 max|Δ|=**25.48°** / max|Δ²|=32.21°，状态翻转=2（正是"走→停"）
            //
            // ── 为什么"停"的位置一定会撞上大跳变 ──
            // 敌人停下来的条件是"够近了"，而那是**位置**判据 ⇒ 它随时、与腿相位无关地发生
            // ⇒ 玩家看到的不是"缓下来"，而是**腿被瞬间按平** ⇒ 这就是"移动动画抽搐" ✓
            //
            // ── 修法：把"走权重"做成一个**一阶过渡**（τ = 75ms）──
            //   `_motionW`：1 = 走，0 = 停。走/停两态的**所有**姿态量都走 `BlendWalkIdle()`
            //   ⇒ 腿振幅、身体起伏、鞘翅、触角、大颚**一起**过渡，而不是各写一套延迟 ✓
            //   · τ=75ms ⇒ 60fps 时 α=0.20（约 11 帧到 90%）、20fps 时 α=0.49（约 4 帧）
            //     —— 比"一帧"慢一个量级，又短到看不出"慢慢收腿" ✓
            //   · `dt == 0`（验收冻结 `Tick(0f)`）⇒ **直接到位**：
            //     `Pose()` 截图与 `LegAngles()` 读数必须还是**该状态本身**的姿势，
            //     否则"锁相位截图"就变成拍一个过渡中的中间态 ✗
            //   · 攻击 / 受击 / 死亡：`_motionW` **冻结**（不朝它们过渡）——
            //     它们的腿幅度本来就是 0 或 1.15×，与走/停无关 ✓
            // ══════════════════════════════════════════════════════════════════

            /// <summary>走↔停的幅度过渡时间常数（秒）。见上方那段推导。</summary>
            private const float WalkBlendTau = 0.075f;

            /// <summary>当前"走"的权重（1 = 走，0 = 停）。逐帧朝目标逼近，见 `UpdateWalkBlend`。</summary>
            private float _motionW = 1f;

            /// <summary>本帧实际用的腿摆振幅（度）。`SwingAmpDeg` 读它（探针口径）。</summary>
            private float _swingAmpNow = 26f;

            /// <summary>
            /// ★ v191-b：腿摆振幅的**三个终值**（度）—— 它们是"改前行为"的**逐位复现** ★
            ///
            /// 旧代码的三个来源（`Play()` 写的基数 × `TickNimble` 的档位系数）：
            ///   走   `_legAmpDeg`(26) × 1.00  = **26**        ／ 抬腿 12 × 1.00  = **12**
            ///   停   `_legAmpDeg`(4.5) × 0.16 = **0.72**      ／ 抬腿 2.0 × 0.18 = **0.36**
            ///   受击 `_legAmpDeg`(30) × 1.15  = **34.5**      ／ 抬腿 16 × 1.30  = **20.8**
            /// —— 注意"停"那一档**乘了两次**（4.5 已经是待机基数，0.16 又缩一次）。
            /// 这不是笔误：`LegAngles()` 的实测待机摆角就是 ±0.6°（对应振幅 0.72）✓
            /// 本轮**一个数都没改**，只是让"停 ↔ 走"在 26 与 0.72 之间**过渡**而不是瞬切 ✓
            /// </summary>
            private const float AmpSwingWalk = 26f, AmpSwingIdle = 4.5f * 0.16f, AmpSwingHit = 30f * 1.15f;
            private const float AmpLiftWalk = 12f, AmpLiftIdle = 2.0f * 0.18f, AmpLiftHit = 16f * 1.3f;

            /// <summary>把 `_motionW` 朝当前状态的目标值推进一格（`Tick` 每帧调一次）。</summary>
            private void UpdateWalkBlend(float dt)
            {
                float target = _state == ModelView.State.Walk ? 1f
                             : _state == ModelView.State.Idle ? 0f
                             : _motionW;                 // 其它状态：冻结权重，不参与过渡
                if (dt <= 0f) { _motionW = target; return; }
                _motionW += (target - _motionW) * (1f - Mathf.Exp(-dt / WalkBlendTau));
            }

            /// <summary>
            /// 走/停两态之间的**平滑插值**：给同一个姿态参数的两套取值，返回按 `_motionW` 混合的那一个。
            /// `other` 是攻击/受击/死亡等第三类状态的取值（照旧瞬时切换 —— 它们本来就该"一下就变"）✓
            /// </summary>
            private float BlendWalkIdle(float idleValue, float walkValue, float other)
            {
                if (_state == ModelView.State.Walk || _state == ModelView.State.Idle)
                    return Mathf.Lerp(idleValue, walkValue, _motionW);
                return other;
            }

            /// <summary>
            /// ★ v191：**非 BOSS 的按物种动画**（走 / 待机 / 攻击 / 受击 / 死亡 各有各的姿态）。
            ///
            /// 需求："动画 —— 走/待机/攻击/受击/死亡，各 id 姿态不同" ⇒ 所以这里**不是一个通用循环**，
            /// 而是四种物种各自的一套曲线（见下面 `switch` 里每一条的分工）：
            ///   · 锹形虫：大颚**开合** + 鞘翅微微抬起（甲虫走路的那点动静）
            ///   · 蛾    ：**扇翅**（4 片,前翅相位领先后翅）+ 身体悬停起伏 + 触角后扫
            ///   · 蚱蜢  ：4 条步足小步 + **后腿蓄力/弹开** + 身体前倾
            ///   · 瓢虫  ：**壳**左右摇晃（短腿快倒腾）+ 待机时壳轻微起伏
            /// 死亡时四种都瘫成不同的形状（甲虫侧倒、蛾翅下垂、蚱蜢蜷腿、瓢虫翻壳）✓
            ///
            /// ★ 只写 `Anim` 以下关节，不碰 `Go` ★ —— 位置/朝向仍由玩法逻辑独占 ✓
            /// </summary>
            private void TickNimble(Shape s, float dt)
            {
                bool idle = _state == ModelView.State.Idle;
                bool atk = _state == ModelView.State.Attack;
                bool hit = _state == ModelView.State.Hit;
                bool dead = _state == ModelView.State.Death;

                // 每个物种的步频不同（瓢虫短腿倒腾得最快、蛾最慢）
                float freqMul = s.Kind == Species.Ladybug ? 1.55f
                              : s.Kind == Species.Hopper ? 1.15f
                              : s.Kind == Species.Moth ? 0.80f : 1.0f;
                float w = Mathf.PI * 2f * s.LegFreq * freqMul;
                float p = w * _phase;

                // ── 各状态的基准量（物种再在此基础上叠加）──
                // ★ v191-b：走↔停之间用 `_motionW` 过渡，不再一帧切 26°→0.72° ★
                //
                // ★ 为什么这里用**三个常量**而不是接着用 `_legAmpDeg`（实测踩过，必须写下来）★
                // `Play()` 会按状态改写 `_legAmpDeg`（走 26 / 停 4.5 / 受击 30），
                // 而旧表达式 `idle ? _legAmpDeg * 0.16f` 又乘一次 0.16
                // ⇒ 实际两端是 **26** 与 **4.5 × 0.16 = 0.72**（相差 36 倍）
                // ⇒ 只把"0.16 这一档"做成过渡是**没用的**：`_legAmpDeg` 自己就在 `Play()` 里跳了一次
                //   实测：那样改完 `振幅 max|Δ|` 仍然是 **22.24°@f60**（一点没降）✗
                // ⇒ 所以这里把**最终振幅**的三个取值写成常量，过渡直接作用在它们之间 ✓
                float swingAmp, liftAmp;
                if (dead || atk) { swingAmp = 0f; liftAmp = 0f; }
                else if (hit) { swingAmp = AmpSwingHit; liftAmp = AmpLiftHit; }
                else
                {
                    swingAmp = Mathf.Lerp(AmpSwingIdle, AmpSwingWalk, _motionW);
                    liftAmp = Mathf.Lerp(AmpLiftIdle, AmpLiftWalk, _motionW);
                }
                _swingAmpNow = swingAmp;

                float pitch = 0f, roll = 0f, lift = 0f, lean = 0f;
                float antennaDeg = 0f, wingDeg = 0f, pincerDeg = 0f;

                // ── ① 腿（步足）──
                for (int i = 0; i < 6; i++)
                {
                    Transform leg = _legs[i];
                    if (leg == null) continue;
                    float pi = p + _legPhase[i];
                    if (_legHind[i])
                    {
                        // 蚱蜢后腿：绕 Z 轴收起（负）/ 弹开（正），走路时小幅脉动
                        float pulse = BlendWalkIdle(3f * Mathf.Sin(_phase * 2.1f), 9f * Mathf.Sin(pi), 0f);
                        if (atk) pulse += 34f * (1f - Mathf.Clamp01(_stateTime / 0.30f));   // 弹射瞬间
                        if (hit) pulse -= 16f;
                        if (dead) pulse = -26f;
                        _legSwing[i] = pulse;
                        // 与步足同一条约定：左侧节点本身带 180° 偏航 ⇒ 左右**不需要**取反，
                        // 否则一侧后腿会朝下、一侧朝上（看着像断了）✗
                        leg.localRotation = _legBase[i] * Quaternion.Euler(0f, 0f, pulse);
                        continue;
                    }
                    float swing = swingAmp * Mathf.Sin(pi);
                    float liftLeg = liftAmp * Mathf.Max(0f, Mathf.Cos(pi));
                    if (dead)
                    {
                        // 死亡：腿收到身下（各物种收法不同）
                        swing = s.Kind == Species.Hopper ? 22f : 12f;
                        liftLeg = s.Kind == Species.Moth ? -8f : 6f;
                    }
                    _legSwing[i] = swing;
                    leg.localRotation = _legBase[i] * Quaternion.Euler(0f, swing, liftLeg);
                }

                // ── ② 身体姿态 + 物种特征动作 ──
                switch (s.Kind)
                {
                    case Species.Stag:                       // 锹形虫
                        {
                            // ★ v191-b：下面四行原来是 `walk ? … : idle ? … : …` 的三元套，
                            //   走↔停**一帧切换** ⇒ 身体俯仰 / 起伏 / 鞘翅 / 触角 / 大颚跟着一起跳 ✗
                            //   现在统一走 `BlendWalkIdle(idle 值, walk 值, 其它状态值)` ✓
                            pitch = BlendWalkIdle(0f, 2.5f + 1.5f * Mathf.Sin(p * 2f), 0f);
                            float bob = Mathf.Sin(p * 2f) * BlendWalkIdle(0.004f, 0.014f, 0f) * s.Unit;
                            lift = bob;
                            // 大颚：待机时缓慢开合、走路时小幅摆动、攻击时张到最大
                            pincerDeg = BlendWalkIdle(7f + 7f * Mathf.Sin(_phase * 1.9f),
                                                      5f + 4f * Mathf.Sin(p * 2f),
                                                      atk ? 40f : hit ? 26f : 0f);
                            if (dead) pincerDeg = 0f;
                            // 鞘翅（甲虫走路时鞘翅会微微抬起 → 读得出"活着"）
                            wingDeg = BlendWalkIdle(1.5f + 1.0f * Mathf.Sin(_phase * 1.4f),
                                                    5f + 3f * Mathf.Sin(p * 2f),
                                                    atk ? 18f : hit ? 26f : 0f);
                            if (dead) { wingDeg = 30f; roll = 38f; lift = -0.02f * s.Unit; }
                            antennaDeg = BlendWalkIdle(3f * Mathf.Sin(_phase * 2.3f),
                                                       6f * Mathf.Sin(p * 2f),
                                                       atk ? 26f : hit ? 34f : 0f);
                            if (dead) antennaDeg = 40f;
                            if (atk) { pitch -= 12f; lean = 0.020f * s.Unit; }
                            if (hit) pitch -= 10f;
                            break;
                        }

                    case Species.Moth:                       // 蛾
                        {
                            // 扇翅：前翅领先、后翅跟随；待机慢扇，走路快扇，攻击全张
                            // ★ v191-b：走↔停走 `BlendWalkIdle`（理由见 `_motionW` 那段）★
                            float flap = Mathf.Sin(p * 1.15f);
                            float flapH = Mathf.Sin(p * 1.15f + 0.55f);
                            wingDeg = BlendWalkIdle(8f + 7f * flap, 24f + 20f * flap,
                                                    atk ? 62f : hit ? -14f : 0f);
                            if (dead) wingDeg = -46f;
                            // 悬停起伏：蛾的身体起伏比甲虫大（它在飘）
                            float hover = Mathf.Sin(p) * BlendWalkIdle(0.012f, 0.022f, 0f) * s.Unit;
                            lift = hover;
                            pitch = BlendWalkIdle(0.8f * Mathf.Sin(_phase * 1.3f), -3f, 0f);
                            if (atk) { pitch = -20f; lean = 0.045f * s.Unit; }
                            if (hit) { pitch = 12f; lift = -0.014f * s.Unit; }
                            if (dead) { roll = 46f; lift = -0.045f * s.Unit; }
                            // 羽状触角：后扫 / 上扬（与锹形虫的棒状触角动法不同）
                            antennaDeg = BlendWalkIdle(4f * Mathf.Sin(_phase * 1.6f),
                                                       8f * Mathf.Sin(p),
                                                       atk ? -34f : hit ? 22f : 0f);
                            if (dead) antennaDeg = 26f;
                            break;
                        }

                    case Species.Hopper:                     // 蚱蜢
                        {
                            // 走路 = 一颠一颠的（后腿脉动把身体顶起来）
                            float hop = BlendWalkIdle(0.003f * Mathf.Sin(_phase * 1.5f) * s.Unit,
                                                      Mathf.Abs(Mathf.Sin(p)) * 0.028f * s.Unit, 0f);
                            lift = hop;
                            pitch = BlendWalkIdle(0f, 4f, 0f);
                            if (atk) { pitch = -26f; lean = 0.085f * s.Unit; lift += 0.030f * s.Unit; }
                            if (hit) { pitch = 14f; lift -= 0.018f * s.Unit; }
                            if (dead) { roll = 66f; lift = -0.030f * s.Unit; }
                            // 覆翅几乎不张（它不飞）——只有受击/攻击时抖一下
                            wingDeg = atk ? 16f : hit ? 22f : idle ? 1.5f * Mathf.Sin(_phase * 1.7f) : 0f;
                            if (dead) wingDeg = 12f;
                            antennaDeg = BlendWalkIdle(5f * Mathf.Sin(_phase * 2.6f),
                                                       7f * Mathf.Sin(p * 1.5f),
                                                       atk ? 18f : hit ? 26f : 0f);
                            if (dead) antennaDeg = 30f;
                            break;
                        }

                    // ══ ★ v201：飞蛾 BOSS 三形态各自的动画 ★ ══
                    case Species.Larva:                       // 毛毛虫：一伸一缩地爬（蠕动）
                        {
                            // 蠕动 = 体轴前后来回 pitch + 身体轻微下沉/抬起（与"甲虫小步走"完全不同）
                            float wave = Mathf.Sin(p * 0.85f);
                            pitch = BlendWalkIdle(1.0f * wave, 4.5f * wave, 0f);
                            lift = BlendWalkIdle(0.006f * wave, 0.016f * wave, 0f) * s.Unit;
                            roll = BlendWalkIdle(0.8f * Mathf.Sin(p * 0.5f), 2.2f * Mathf.Sin(p * 0.5f), 0f);
                            if (atk) { pitch = -18f; lean = 0.05f * s.Unit; }       // 冲刺前压
                            if (hit) { pitch = 12f; lift -= 0.01f * s.Unit; }
                            if (dead) { roll = 84f; pitch = -6f; lift = -0.02f * s.Unit; }
                            antennaDeg = BlendWalkIdle(5f * Mathf.Sin(_phase * 2.4f),
                                                       9f * Mathf.Sin(p * 1.6f),
                                                       atk ? 26f : hit ? 20f : 0f);
                            break;
                        }
                    case Species.Pupa:                        // 蛹：**不能移动** ⇒ 只有"壳里的东西在动"
                        {
                            // 呼吸 + 偶尔的抽动（攻击时整只鼓起然后一颤）
                            float breathe = Mathf.Sin(_phase * 1.4f);
                            pitch = BlendWalkIdle(0.6f * breathe, 0.6f * breathe, 0f);
                            lift = BlendWalkIdle(0.004f * breathe, 0.004f * breathe, 0f) * s.Unit;
                            roll = 0f;
                            if (atk) { pitch = -8f; lift = 0.02f * s.Unit; }         // 放出激光时绷紧
                            if (hit) { pitch = 6f; }
                            if (dead) { roll = 62f; pitch = -10f; lift = -0.015f * s.Unit; }
                            antennaDeg = 6f * Mathf.Sin(_phase * 1.1f);
                            // 翅芽：待机时几乎不动，攻击/受击时抖一下 —— "快了，快出来了"
                            wingDeg = atk ? 10f : (hit ? 14f : 1.2f * Mathf.Sin(_phase * 1.3f));
                            break;
                        }
                    case Species.MothQueen:                   // 飞蛾（BOSS）：悬停 + 扇翅
                        {
                            float hover = Mathf.Sin(_phase * 2.2f);
                            pitch = BlendWalkIdle(1.5f * hover, 3.0f * hover, 0f);
                            lift = BlendWalkIdle(0.010f * hover, 0.018f * hover, 0f) * s.Unit;
                            roll = BlendWalkIdle(1.2f * Mathf.Sin(_phase * 1.7f), 2.4f * Mathf.Sin(_phase * 1.7f), 0f);
                            // 扇翅：待机 = 缓慢大扇（悬停），走路 = 更快；攻击 = 张开到接近水平
                            wingDeg = BlendWalkIdle(22f + 10f * Mathf.Sin(_phase * 2.6f),
                                                    34f + 12f * Mathf.Sin(_phase * 4.2f),
                                                    atk ? 62f : hit ? 40f : 0f);
                            if (dead) { roll = 78f; wingDeg = -6f; lift = -0.03f * s.Unit; }
                            antennaDeg = BlendWalkIdle(-8f + 6f * Mathf.Sin(_phase * 2.0f),
                                                       4f * Mathf.Sin(_phase * 3.1f),
                                                       atk ? -40f : hit ? 24f : 0f);
                            break;
                        }
                    default:                                 // 瓢虫
                        {
                            // 短腿快倒腾 + 壳左右摇（"滚圆的东西在挪"）
                            roll = BlendWalkIdle(1.2f * Mathf.Sin(_phase * 1.5f), 4.5f * Mathf.Sin(p), 0f);
                            lift = Mathf.Sin(p * 2f) * BlendWalkIdle(0.003f, 0.008f, 0f) * s.Unit;
                            pitch = BlendWalkIdle(0f, 1.5f, 0f);
                            if (atk) { pitch = -22f; lean = 0.030f * s.Unit; }
                            if (hit) { pitch = 10f; lift -= 0.012f * s.Unit; }
                            if (dead) { roll = 74f; pitch = -8f; lift = -0.022f * s.Unit; }
                            antennaDeg = BlendWalkIdle(4f * Mathf.Sin(_phase * 2.2f),
                                                       6f * Mathf.Sin(p * 1.7f),
                                                       atk ? 20f : hit ? 24f : 0f);
                            if (dead) antennaDeg = 34f;
                            break;
                        }
                }

                // ══════════════════════════════════════════════════════════════
                // ★ v201：**飞蛾 BOSS 的攻击姿势叠加**（4=吼叫 5=蓄力爆炸 6=冲刺 7=激光 8=消失/跳砸）
                //
                // 为什么叠在"物种基线"之上，而不是替换它：
                //   基线那一套负责"它在活着"（悬停 / 呼吸 / 扇翅），攻击姿势负责"它要干什么"。
                //   两者相加 ⇒ 不会出现"放招时整只僵住"✓
                // 读数来源：`BossEnemy.CastPose` / `CastAge`（`SyncEnemies` 每帧喂）——
                //   与旧 BOSS 的姿势通道**同一条**，没有第二个姿势来源 ✓
                // ══════════════════════════════════════════════════════════════
                if (_atkKind > 0 && _atkAge >= 0f && IsMothForm(s.Kind))
                {
                    PoseOf(_atkKind, _atkAge, s, out float ppitch, out float pdrop, out float pwing,
                           out float pabd, out float pant, out float pmand, out float plean);
                    pitch += ppitch;
                    lift += pdrop + pabd * 0.0016f * s.Unit;   // 腹部上抬（与 BOSS 同一约定）
                    wingDeg += pwing;
                    antennaDeg += pant;
                    lean += plean;
                    _ = pmand;                                  // 飞蛾没有大颚（保留输出位，便于与 BOSS 对齐）
                }

                _bodyRoot.localPosition = new Vector3(0f, lift, 0f);
                _bodyRoot.localRotation = Quaternion.Euler(pitch, 0f, roll);
                _anim.localPosition = new Vector3(0f, 0f, lean);

                // ── ③ 翅 / 鞘翅 / 覆翅 ──
                if (_wings != null)
                {
                    for (int i = 0; i < _wings.Length; i++)
                    {
                        Transform t = _wings[i];
                        if (t == null) continue;
                        bool right = (i % 2) == 0;
                        float rollWing = (right ? 1f : -1f) * wingDeg;
                        // 蛾的前后翅略微分相位（不是一块板在动）
                        if (s.Kind == Species.Moth && i >= 2) rollWing *= 0.74f;
                        t.localRotation = _wingRest[i] * Quaternion.Euler(0f, 0f, rollWing);
                    }
                }

                // ── ④ 锹形虫的大颚钳 ──
                if (_pincers != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Transform t = _pincers[i];
                        if (t == null) continue;
                        bool right = i == 0;
                        t.localRotation = Quaternion.Euler(0f, (right ? -1f : 1f) * pincerDeg, 0f);
                    }
                }

                // ── ⑤ 触角（统一的"上下压 / 上扬"约定，与 BOSS 同一套正负号）──
                if (_antennae != null)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        Transform t = _antennae[i];
                        if (t == null) continue;
                        t.localRotation = Quaternion.Euler(antennaDeg, 0f, 0f) * _antennaRest[i];
                    }
                }
            }

            /// <summary>
            /// === 权威表 === BOSS 的 M=2 种攻击姿势 —— 每条都是**独立可辨认**的曲线，改的都不止一个参数。
            ///
            /// 1 = **环形弹幕**（`BossEnemy.RingBarrage`）：后仰蓄力 → 前甩释放。
            ///     关键帧 `u≈0.25`：体节后仰 24° · 鞘翅张开 62° · 腹部上抬 · 触角上扬 46° · 大颚半张 · 整体后坐
            /// 2 = **冲锋蓄力**（`BossEnemy.Charge` 的 windup）：压低收翅、大颚张到最大、触角后收并颤动。
            ///     关键帧 `u≈0.8`：下沉 22% · 大颚 52° · 鞘翅夹紧 -6° · 前倾 15° · 整体前移
            /// 3 = **冲锋突进**（`_chargeTime` 段）：前倾冲出、大颚咬合、鞘翅半张兜风、触角压低。
            ///     关键帧 `age≈0`：前倾 30° · 整体前冲 0.10×unit · 鞘翅 26° · 大颚 3°
            /// </summary>
            private static void PoseOf(int kind, float age, Shape s,
                                       out float pitchDeg, out float drop, out float elytraDeg,
                                       out float abdomenDeg, out float antennaDeg,
                                       out float mandibleDeg, out float lean)
            {
                pitchDeg = 0f; drop = 0f; elytraDeg = 0f; abdomenDeg = 0f;
                antennaDeg = 0f; mandibleDeg = 0f; lean = 0f;

                if (kind == 1)
                {
                    float u = Mathf.Clamp01(age / 0.95f);
                    float rear = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u / 0.50f));   // 0→1→0
                    float kick = Mathf.Clamp01((u - 0.52f) / 0.18f) *
                                 (1f - Mathf.Clamp01((u - 0.70f) / 0.30f));
                    pitchDeg = -24f * rear + 16f * kick;      // 先后仰再前甩
                    drop = -0.055f * s.Unit * rear;           // 负的下沉 = 抬身
                    elytraDeg = 62f * rear - 8f * kick;       // 鞘翅张开 → 甩击时回收
                    abdomenDeg = 26f * rear;                  // 腹部上抬
                    antennaDeg = -46f * rear;                 // 触角上扬（负 = 上扬）
                    mandibleDeg = 10f + 22f * rear;           // 大颚半张
                    lean = -0.045f * s.Unit * rear;           // 整体后坐
                }
                else if (kind == 2)
                {
                    float u = Mathf.Clamp01(age / 1.00f);
                    float crouch = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.55f));
                    float shake = 0.6f * crouch * Mathf.Sin(age * 42f);
                    pitchDeg = 15f * crouch;                  // 头压低前倾
                    drop = 0.075f * s.Unit * crouch;          // 下沉
                    elytraDeg = -6f * crouch;                 // 收翅（负 = 夹紧）
                    abdomenDeg = -10f * crouch;
                    antennaDeg = 30f * crouch + shake;        // 触角后收并颤动
                    mandibleDeg = 52f * crouch;               // 大颚张到最大
                    lean = 0.030f * s.Unit * crouch;
                }
                else if (kind == 3)
                {
                    float u = Mathf.Clamp01(age / 0.55f);
                    float lunge = 1f - u;
                    pitchDeg = 30f * lunge;                   // 前倾冲出
                    drop = 0.020f * s.Unit * lunge;
                    elytraDeg = 26f * lunge;                  // 半张兜风
                    abdomenDeg = 12f * lunge;
                    antennaDeg = 55f * lunge;                 // 触角压低贴背
                    mandibleDeg = 3f;                         // 咬合（闭合）
                    lean = 0.100f * s.Unit * lunge;           // 整体前冲
                }
                // ══════════════════════════════════════════════════════════════
                // ★★ v201：新增 5 个姿势（旧 BOSS 2 招 + 飞蛾 3 形态的招）★★
                //   每个都是**独立可辨认**的曲线，且与 1/2/3 一样只动关节、不动位置。
                //   ⚠️ 1/2/3 三段**一个数都没改**（旧 BOSS 的观感与旧录像逐位一致）✓
                // ══════════════════════════════════════════════════════════════
                else if (kind == 4)
                {
                    // ── 4 = 旧 BOSS「吼叫」：仰头张口 → 5 个环，每一环顶一下 ──
                    //   环节拍 1.0s 来自 `BossEnemy.RoarRingInterval`；这里用它把"每一环"做成一次脉冲
                    float u = Mathf.Clamp01(age / 0.45f);                        // 起手：仰头
                    float head = Mathf.SmoothStep(0f, 1f, u);
                    float beat = age - 0.45f;                                    // 起手之后才开始打环
                    float pulse = beat <= 0f ? 0f
                        : Mathf.Max(0f, Mathf.Sin(beat * (2f * Mathf.PI) / 1.0f));  // 1 秒一顶
                    pitchDeg = -26f * head - 6f * pulse;      // 后仰（负 = 抬头）
                    drop = -0.040f * s.Unit * head;           // 抬身
                    elytraDeg = 46f * head;                   // 鞘翅全张（ roar 的姿态）
                    abdomenDeg = 18f * head + 8f * pulse;
                    antennaDeg = -50f * head;                 // 触角上扬
                    mandibleDeg = 30f + 22f * pulse;          // 张到最大，随环脉动
                    lean = -0.030f * s.Unit * head;           // 整体后坐
                }
                else if (kind == 5)
                {
                    // ── 5 = 旧 BOSS「蓄力爆炸」：越蓄越沉、越抖、大颚咬死 ──
                    //   u 从 0 到 1（对应 `BlastChargeSeconds` 1.8s），最后 0.25 有一个"鼓起"
                    float u = Mathf.Clamp01(age / 1.80f);
                    float sink = Mathf.SmoothStep(0f, 1f, u);
                    float tremor = 0.9f * u * Mathf.Sin(age * 46f);   // 越蓄抖得越厉害
                    float bulge = Mathf.Clamp01((u - 0.75f) / 0.25f);
                    pitchDeg = 20f * sink + tremor;           // 头深深压低
                    drop = 0.105f * s.Unit * sink - 0.030f * s.Unit * bulge;
                    elytraDeg = -10f * sink - 22f * bulge;    // 收翅 → 绷开
                    abdomenDeg = -14f * sink;
                    antennaDeg = 38f * sink + tremor;         // 贴背
                    mandibleDeg = 8f;                         // 咬死（闭合）
                    lean = 0.042f * s.Unit * sink;
                }
                else if (kind == 6)
                {
                    // ── 6 = 飞蛾「冲刺」：两段合一（蓄力时收翅压低，突进时张开拉直）──
                    //   age < 前摇 ⇒ 收；之后 ⇒ 张。（前摇/冲刺时长由 `BossEnemy` 的常量给）
                    float u = Mathf.Clamp01(age / 0.55f);
                    float open = Mathf.Clamp01((age - 0.35f) / 0.24f);
                    float crouch = Mathf.SmoothStep(0f, 1f, u) * (1f - open);
                    pitchDeg = 16f * crouch + 26f * open;     // 蓄力压前 → 突进更前
                    drop = 0.070f * s.Unit * crouch;
                    elytraDeg = -18f * crouch + 40f * open;   // 收翅 → 展开（这个是"飞蛾"的翅）
                    abdomenDeg = -8f * crouch + 10f * open;
                    antennaDeg = 34f * crouch - 30f * open;
                    mandibleDeg = 0f;
                    lean = 0.035f * s.Unit * crouch + 0.090f * s.Unit * open;
                }
                else if (kind == 7)
                {
                    // ── 7 = 蛹「激光」：壳里绷紧 → 落下时整只往前一顶 ──
                    float u = Mathf.Clamp01(age / 1.10f);     // 预警 1.10s
                    float tense = Mathf.SmoothStep(0f, 1f, u);
                    float fire = Mathf.Clamp01((age - 1.10f) / 0.22f);   // 落下 0.22s
                    pitchDeg = -12f * tense + 18f * (1f - fire) * (fire > 0f ? 1f : 0f);
                    drop = 0.030f * s.Unit * tense - 0.050f * s.Unit * fire;
                    elytraDeg = 8f * tense + 14f * fire;
                    abdomenDeg = 10f * tense;
                    antennaDeg = 20f * tense - 24f * fire;
                    mandibleDeg = 0f;
                    lean = -0.020f * s.Unit * tense + 0.045f * s.Unit * fire;
                }
                else if (kind == 8)
                {
                    // ── 8 = 飞蛾「消失 → 跳砸」：收缩（消失）→ 猛地展开砸下 ──
                    float u = Mathf.Clamp01(age / 1.60f);     // 消失 1.6s
                    float shrink = Mathf.SmoothStep(0f, 1f, u);
                    float slam = Mathf.Clamp01((age - 1.60f) / 0.50f);   // 跳砸前摇 0.5s
                    pitchDeg = 24f * shrink - 34f * slam;     // 缩成一团 → 俯冲
                    drop = -0.020f * s.Unit * shrink + 0.060f * s.Unit * slam;
                    elytraDeg = -34f * shrink + 58f * slam;   // 收翅（消失）→ 全张（砸下）
                    abdomenDeg = -18f * shrink + 16f * slam;
                    antennaDeg = 40f * shrink - 44f * slam;
                    mandibleDeg = 0f;
                    lean = -0.010f * s.Unit * shrink + 0.070f * s.Unit * slam;
                }
            }

            /// <summary>
            /// ★ v191-b：**本帧腿摆动的振幅**（度）★ —— 给逐帧探针读。
            ///
            /// 为什么量"振幅"而不是量"腿角"：腿角 = 振幅 × `sin(相位)`，而相位在 6~9Hz 上跑
            /// ⇒ 腿角每帧本来就会变 20°~65°（取决于 dt）**无法用来判"抽没抽"** ✗
            /// 而"走↔停"的唯一差别就是**振幅**（26° ↔ 4.2°）⇒ 振幅的逐帧 Δ 才是那一刀的指纹 ✓
            /// </summary>
            public float SwingAmpDeg => _swingAmpNow;

            /// <summary>
            /// ★ v196：第 `i` 条腿**本帧的摆角**（度，正 = 向前）—— 只读探针用。
            ///
            /// 为什么开这个口子而不是让探针去 parse `LegAngles()` 的字符串：
            /// 探针每帧要 6 个浮点 × 11 只实例 × 900 帧，parse 会产生几万次字符串分配，
            /// 而这台编辑器的 GC 已经高到 `gcBytes` 一度到 0.9GB ⇒ 探针不该再添乱 ✗
            /// ★ 读的是**同一份** `_legSwing`（与 `LegAngles()` 完全一致）⇒ 不产生第二份真值 ✓
            /// </summary>
            public float LegSwing(int i) => (i >= 0 && i < 6) ? _legSwing[i] : 0f;

            /// <summary>诊断：六条腿当前的偏摆角（度，正数 = 向前）。"腿真的在摆"的数字证据。</summary>
            public string LegAngles()
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 6; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append('L').Append(i).Append('=').Append(_legSwing[i].ToString("0.0"));
                }
                return sb.ToString();
            }
        }

        private static readonly int TintMulId = Shader.PropertyToID("_TintMul");
        private static readonly int NeonColorId = Shader.PropertyToID("_NeonColor");
        private static readonly int NeonWidthId = Shader.PropertyToID("_NeonWidth");

        // ══════════════════════════════════════════════════════════════════════
        // 七、对象池 + 与玩法的接线
        //
        // ★ 池 ★ 死掉的虫不 `Destroy` 层级，而是**停用**后放回按 id 分的栈，
        //   下一波同种虫直接取出复用 ⇒ 波次推进时不再持续新建 GameObject / Renderer ✓
        //   网格与材质本来就已经共享（第五段），池省的是 GameObject 与组件 ✓
        // ══════════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, Stack<BugView>> _pool = new Dictionary<string, Stack<BugView>>();
        private static readonly Dictionary<int, BugView> _live = new Dictionary<int, BugView>();
        private static readonly List<int> _dead = new List<int>(64);
        private static readonly Dictionary<int, Vector3> _lastPos = new Dictionary<int, Vector3>();
        private static bool? _active;

        // ★ v182：**小猪（`EnemyKind.Wander`）不在接管名单里** —— 它走旧版 FBX + 旧动画 ★
        //
        // 需求原话："小猪回调旧版动画和模型" ⇒ 只把 `pig` 一种**排除**，其余 4 种照旧是虫怪 ✓
        // 实现放在这边（接管方）而不是 `WorldView.SyncEnemies`，理由是那个挂钩点是
        // **整段一次性**的（`if (ProceduralBugs.SyncEnemies(...)) return;`），它没有"按种类分流"
        // 的口子；而 `WorldView` 不在本轮允许改动的清单里 ✗
        // ⇒ 在接管方这里把小猪**按旧路径的样子重建一遍**：同一份资产（`Characters/pig`）、
        //   同一组片段（`Art/AnimMerged/pig_*`）、与旧分支逐条同款的摆位 / 朝向 / 受击 / 染色规则 ✓
        private static readonly Dictionary<int, ModelView> _pigs = new Dictionary<int, ModelView>();
        private static readonly List<int> _deadPigs = new List<int>(16);

        /// <summary>旧版小猪模型的资源 id（`Resources/Art/Characters/pig.fbx` + `Art/Anim/pig_idle`）。</summary>
        private const string PigModelId = "pig";

        /// <summary>从池里取一只（没有就新建）。</summary>
        public static BugView Acquire(Transform parent, string id, bool boss, Color defColor, float displayScale)
            => Acquire(parent, id, boss, defColor, displayScale, FormFor(id, boss));

        /// <summary>★ v201：按**显式形态**取（池也按 `id|形态` 分栈 ⇒ 换形态时不会取到别的形态）。</summary>
        public static BugView Acquire(Transform parent, string id, bool boss, Color defColor,
                                      float displayScale, Species form)
        {
            string key = id + "|" + (int)form;
            if (_pool.TryGetValue(key, out Stack<BugView> stack))
            {
                while (stack.Count > 0)
                {
                    BugView hit = stack.Pop();
                    if (hit == null || hit.Go == null) continue;      // 场景重建时被销毁的
                    hit.Go.SetActive(true);
                    hit.Go.transform.SetParent(parent, false);
                    hit.Go.transform.localScale = Vector3.one * displayScale;
                    hit.ResetForReuse();
                    return hit;
                }
            }
            return BugView.Build(parent, id, boss, defColor, displayScale, form);
        }

        /// <summary>还回池里（不销毁）。★ v201：池键 = `id|形态`（同一只敌人在不同形态间各占一个栈）★</summary>
        public static void Release(BugView view)
        {
            if (view == null || view.Go == null) return;
            view.Go.SetActive(false);
            string key = view.Id + "|" + (int)view.Kind;
            if (!_pool.TryGetValue(key, out Stack<BugView> stack))
            {
                stack = new Stack<BugView>(8);
                _pool[key] = stack;
            }
            stack.Push(view);
        }

        /// <summary>池里现有的空闲实例数（诊断用）。</summary>
        public static int PooledCount
        {
            get
            {
                int n = 0;
                foreach (var kv in _pool) n += kv.Value.Count;
                return n;
            }
        }

        /// <summary>活动实例数（诊断用）。</summary>
        public static int LiveCount => _live.Count;

        /// <summary>清空所有活动实例（场景重建时调用）。</summary>
        public static void ClearLive()
        {
            foreach (var kv in _live) Release(kv.Value);
            _live.Clear();
            _dead.Clear();
            // 小猪是旧版 `ModelView`，没有池 —— 直接销毁它自己的 GameObject
            foreach (var kv in _pigs)
            {
                if (kv.Value != null && kv.Value.Go != null) Object.Destroy(kv.Value.Go);
            }
            _pigs.Clear();
            _deadPigs.Clear();
            _lastPos.Clear();
            _dirEma.Clear();
            _yawSmooth.Clear();
        }

        /// <summary>
        /// 本帧是否在移动（阈值 5cm/s，与 `WorldView.Moved` 同一判据）。同时记下方向与**位移米数**。
        /// ★ v196：多一个 `dist`（这一帧实际挪了几米）⇒ 喂给 `BugView.Tick(dt, dist)` 做"相位 ∝ 位移" ✓
        /// </summary>
        private static bool Moved(int id, Vector3 pos, float dt, out Vector3 dir, out float dist)
        {
            bool moved = false;
            dir = Vector3.zero;
            dist = 0f;
            if (_lastPos.TryGetValue(id, out Vector3 prev) && dt > 0.0001f)
            {
                Vector3 d = pos - prev;
                dist = d.magnitude;              // ★ v196：位移米数（相位 ∝ 位移的输入）★
                // ★ v191：**"在不在动"仍按瞬时速度判，但"朝哪"不能吃单帧位移** ★
                //
                // ── 病根（实测，`EMOTION` 探针，锁实例 id 后的读数）──
                // 蚱蜢 `jumper`：`朝向 maxΔ = 172.26°`，而**那一帧只位移 3.654m→0.028m**；
                //   另有 f96/f184 两处同样是 **172.3°/171.0° 配 0.028m** ✗
                // 瓢虫/小猪 `pig`：`朝向 maxΔ = 55.58°`（位移 0.126m），次大 33.3°（0.046m）✓ 同型
                // ⇒ 一帧走 2.8cm 的**向量方向**本身就是噪声：`atan2` 对这么短的基线没有分辨力 ✓
                //   而蚱蜢的"一颠一颠"让单帧位移**来回** ⇒ 方向在两帧之间翻 180°
                //   ⇒ 身体跟着翻 ⇒ 画面上就是"抽"（★ 弹跳本身是设计，翻面不是 ★）
                //
                // ── 修法：位移向量先过一道低通，再拿它算朝向 ──
                // `e ← e + (d − e)·α`，α = 1 − e^(−dt/τ)（τ=`HeadDirTau`）
                //   · 低通对"来回"的分量**相互抵消** ⇒ 弹跳不再翻面 ✓
                //     而**净位移**（真的在走）会留下来 ⇒ 走向仍然对 ✓
                //   · 不动时 e → 0 ⇒ 调用方的 `sqrMagnitude > 0.0004` 判据自动"保持上一帧朝向"
                //     （与 v80 对小猪那条规定一致：站着不许乱转）✓
                //   · ⚠️ **只改朝向的来源**，`moved`（Walk/Idle 迁移）仍按瞬时速度算 ✗ 不动它 ✓
                //     —— 否则"一颠一颠"里那几帧会被判成 Idle，腿的振幅 26°↔4.5° 来回跳，反而更抽 ✗
                float tau = HeadDirTau;
                float a = 1f - Mathf.Exp(-Mathf.Max(dt, 1e-4f) / tau);
                Vector3 e = _dirEma.TryGetValue(id, out Vector3 prevE) ? prevE + (d - prevE) * a : d;
                _dirEma[id] = e;

                moved = d.sqrMagnitude / (dt * dt) > 0.0025f;
                if (moved) dir = e;
            }
            _lastPos[id] = pos;
            return moved;
        }

        /// <summary>
        /// 朝向用位移向量的低通时间常数（秒）。0.10 ⇒ 60fps 时 α≈0.15、等效记忆约 6~7 帧。
        /// 选它的依据：蚱蜢弹跳的一个来回约 4~6 帧 ⇒ 要把它抵消掉，记忆至少得覆盖一个来回 ✓
        /// 再长就会让"绕圈走"的敌人朝内侧偏（滞后），而 0.10s 的滞后在 60fps 下约 6 帧内收敛，看不出 ✓
        /// </summary>
        private const float HeadDirTau = 0.10f;

        /// <summary>每个敌人**平滑后的位移方向**（朝向来源；见 `Moved`）。</summary>
        private static readonly Dictionary<int, Vector3> _dirEma = new Dictionary<int, Vector3>();

        /// <summary>
        /// ★ v191：**朝向角本身的低通**（最短弧）—— 给"一帧转多少度"加一道硬上限 ★
        ///
        /// ── 为什么"位移向量低通"（`_dirEma`）不够（实测，不是推断）──
        /// 加完 `_dirEma` 后重测（`EMOTION`，同锚点同实例）：
        ///   瓢虫/小猪 `pig` 朝向 maxΔ 55.58° → **28.86°**（次大 33.3° → 10.5°）✓ 有救
        ///   蚱蜢 `jumper`   朝向 maxΔ 172.26° → **172.17°** ✗ 毫无改善
        /// 原因：蚱蜢那 172° **不是噪声，是它真的转过去了** —— 起跳帧位移 3.654m
        /// （它跳过了玩家位置），方向本来就反了；而 `_dirEma` 只按时间常数混入，
        /// 剩下的部分照样一帧就到位 ⇒ 位移低通只能压"抖动"，压不住"真转向的速度" ✓
        /// ⇒ 要满足"连续 3 帧内符号不翻转"，必须在**角度域**上限速：
        ///   `yaw ← yaw_prev + ΔAngle(yaw_prev, target) × α`，α = 1 − e^(−dt/τ) ✓
        ///   · 172° 的落差在 α≈0.19（τ=80ms@60fps）下 ⇒ 第一帧只走 ~26°，约 6 帧转完 ✓
        ///   · 而**小幅**的朝向调整（几度）几乎无变化 ⇒ 敌人不会看起来"迟钝" ✓
        ///   · ⚠️ 只限**朝向**：位置是玩法给的，一毫米都不改
        ///     （蚱蜢的跳、扑、一颠一颠全部原样保留）✓
        /// </summary>
        private const float HeadYawTau = 0.080f;

        /// <summary>每个敌人**平滑后的朝向**（度；见 `HeadYawTau` 那段）。</summary>
        private static readonly Dictionary<int, float> _yawSmooth = new Dictionary<int, float>();

        /// <summary>
        /// 把"目标朝向"过一道最短弧低通后返回**该写进 transform 的那一个角度**。
        /// 两条敌人路径（虫怪 / 旧版小猪）共用，保证它们不会各写一套。
        /// </summary>
        private static float SmoothFacing(int id, float targetYaw, float dt)
        {
            if (!_yawSmooth.TryGetValue(id, out float cur))
            {
                _yawSmooth[id] = targetYaw;      // 第一次见：直接吃目标（否则新生敌人会从 0° 转过来）
                return targetYaw;
            }
            float tau = HeadYawTau;
            float a = tau <= 1e-6f ? 1f : 1f - Mathf.Exp(-Mathf.Max(dt, 1e-4f) / tau);
            float next = cur + Mathf.DeltaAngle(cur, targetYaw) * Mathf.Clamp01(a);
            _yawSmooth[id] = next;
            return next;
        }

        /// <summary>敌人霓虹描边色（与 `WorldView.NeonEnemy` 同值：红）。</summary>
        private static readonly Color NeonEnemy = new Color(1f, 0.13f, 0.10f, 1.00f);

        /// <summary>
        /// 与玩法接线：把 `World.Enemies` 全部画成虫，并回收死掉的。
        /// **返回 true 表示本次同步已由虫怪接手** ⇒ `WorldView.SyncEnemies` 直接 return（最小改动挂钩）。
        ///
        /// 返回 false 只发生在**第一次**调用时着色器还没就绪 ⇒ 退回原来那套 FBX / 图元路径。
        /// ★ 这个判断是**一次性**的（`_active` 缓存）★ ——
        ///   否则第一帧 false、第二帧 true 会让老路径留下一批不会被清理的 GameObject ✗
        /// </summary>
        public static bool SyncEnemies(Transform root, World w, float dt, float displayScale)
        {
            if (root == null || w == null) return false;
            if (_active == null)
            {
                _active = ToonLook.ToonShader != null;
                if (_active == false) Log.Warn("虫怪：Seedfall/Toon 着色器没找到 ⇒ 怪物保留原来的模型路径");
                else Log.Info("虫怪：程序化虫形表现已接手 " + (TypeIds.Length - 1)
                              + " 种怪物（pig 除外 —— 小猪走旧版 FBX/动画）");
            }
            if (_active == false) return false;

            for (int i = 0; i < w.Enemies.Count; i++)
            {
                EnemyActor e = w.Enemies[i];
                if (e == null) continue;

                // ★★ v300：训练场的稻草人 ⇒ **不由本表现层画身体** ★★
                //   病因（读代码即可复现）：下一行把 `EnemyKind.Wander` 一刀切给 `SyncPig`，
                //   而它建的是 `Characters/pig`（`pig_idle.fbx`，ProceduralBugs.cs:3139）
                //   ⇒ 不跳过的话，训练场里站着的会是 10 只**小猪**，而不是稻草人 ✗
                //   跳过之后稻草人由 `TrainingBootstrap.BuildScarecrow` 程序化绘制，
                //   而"受击闪白 + 伤害弹字"这两样**仍然走战斗既有链路**
                //   （`Actor.HitFlash` / `Actor.OnHitLanded` 的 `Popup`）✓
                //   关掉开关时这一行不成立，下一个语句仍是原来的
                //   `if (e.Def.Kind == EnemyKind.Wander) { SyncPig(...); continue; }` ⇒ 逐字节相同 ✓
                if (TrainingMode.HideDummyBody && TrainingMode.IsDummyId(e.Def.Id)) continue;

                // ★ v182：小猪**不走虫怪**（见 `_pigs` 上方的说明）★
                if (e.Def.Kind == EnemyKind.Wander) { SyncPig(root, e, dt, displayScale); continue; }

                bool boss = e.Def.Kind == EnemyKind.Boss;

                // ★★ v201：**飞蛾换形态 ⇒ 换一套几何** ★★
                //   · 形态号来自玩法层唯一的那一处：`BossEnemy.MothPhaseIndex`（0/1/2，非飞蛾 = -1）
                //   · 换形态 = 把旧视图还回池 + 按新形态取一个；**不 new GameObject 以外的任何东西**
                //     （网格与材质都是缓存/池里的，见 `_assets` / `ToonLook`）✓
                //   · 旧 BOSS（`boss` 为真但 `MothPhaseIndex = -1`）走的仍是 `FormFor(id,true) = Giant`
                //     ⇒ 与改前**逐位相同** ✓
                Species form = FormOf(e, boss);
                if (_live.TryGetValue(e.Id, out BugView cur) && cur != null && cur.Go != null
                    && cur.Kind != form)
                {
                    Release(cur);
                    _live.Remove(e.Id);
                }

                if (!_live.TryGetValue(e.Id, out BugView view) || view == null || view.Go == null)
                {
                    view = Acquire(root, e.Def.Id, boss, e.Def.Color, displayScale, form);
                    _live[e.Id] = view;
                }

                // ★ v201：**消失期**把整个渲染体关掉（不可选中 + 看得见的"它不见了"）✓
                bool wantVisible = !(e is BossEnemy mb && mb.MothHidden);
                if (view.Go.activeSelf != wantVisible) view.Go.SetActive(wantVisible);

                // ── 摆位：位置 / 朝向由逻辑给，动画不碰这一层 ──
                Vector3 pos = new Vector3(e.Pos.x, 0f, e.Pos.y);
                view.Go.transform.position = pos;
                bool moving = Moved(e.Id, pos, dt, out Vector3 dir, out float step);
                if (dir.sqrMagnitude > 0.0004f)
                    // ★ v191：朝向过"最短弧低通"再写 —— 见 `HeadYawTau`（位置一点不动）★
                    view.SetFacing(SmoothFacing(e.Id,
                        Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, dt));

                // ── 状态机：沿用 ModelView.State ──
                if (e.HitFlash > 0f) view.Play(ModelView.State.Hit);
                else if (!e.Alive) view.Play(ModelView.State.Death);
                else view.Play(moving ? ModelView.State.Walk : ModelView.State.Idle);

                // ── BOSS 的攻击姿势（M=2 种，由 BossEnemy 的表现信号驱动）──
                if (boss && e is BossEnemy b) view.SetAttackPose(b.CastPose, b.CastAge);
                else view.SetAttackPose(0, -1f);

                // ★ v196：喂**设计步行速度** ⇒ `PhaseRate` 才能算出"实际速度 ÷ 设计速度"
                //   （`enemies.json` 的 `moveSpeed` 就是这个数 ⇒ 数据驱动，不在代码里写死）★
                view.SetGaitSpeed(e.Def.MoveSpeed);
                view.Tick(dt, step);

                // ★ v191：抽搐探针（只读）—— 抄的是**本帧渲染出来的**世界位置/朝向 ✓
                if (_emRemaining > 0) EmCapture(e.Id, e.Def.Id, view.Go != null ? view.Go.transform : null,
                                                (int)view.Current, view.Phase);

                // ── 状态染色（与 ModelView 同一条乘法约定）──
                Color mul = Color.white;
                if (e.Status.Frozen) mul = new Color(0.55f, 0.85f, 1.35f);
                else if (e.Status.Slowed) mul = new Color(0.72f, 0.92f, 1.18f);
                else if (e.Status.Burning) mul = new Color(1.35f, 0.70f, 0.40f);
                else if (e.Status.Poisoned) mul = new Color(0.70f, 1.20f, 0.55f);
                mul *= Mathf.Lerp(0.45f, 1f, e.Health.HpPct);
                if (e.HitFlash > 0f) mul = Color.Lerp(mul, Color.white * 1.7f, 0.85f);
                view.SetTint(mul);
                // 小猪（Wander）走不到这里（上面已 `continue`）—— 它由 `SyncPig` 负责且不给红边
                view.SetNeon(NeonEnemy);
            }

            // ── 回收：这一帧不在 `w.Enemies` 里的实例还回池 ──
            _dead.Clear();
            foreach (var kv in _live)
            {
                if (!StillInWorld(w, kv.Key)) _dead.Add(kv.Key);
            }
            for (int i = 0; i < _dead.Count; i++)
            {
                if (_live.TryGetValue(_dead[i], out BugView gone)) Release(gone);
                _live.Remove(_dead[i]);
                _lastPos.Remove(_dead[i]);
                _dirEma.Remove(_dead[i]);
                _yawSmooth.Remove(_dead[i]);
            }

            // ── 小猪：不在场上就销毁它的旧版模型（它没有池）──
            _deadPigs.Clear();
            foreach (var kv in _pigs)
            {
                if (!StillInWorld(w, kv.Key)) _deadPigs.Add(kv.Key);
            }
            for (int i = 0; i < _deadPigs.Count; i++)
            {
                if (_pigs.TryGetValue(_deadPigs[i], out ModelView pig) && pig != null && pig.Go != null)
                    Object.Destroy(pig.Go);
                _pigs.Remove(_deadPigs[i]);
                _lastPos.Remove(_deadPigs[i]);
                _dirEma.Remove(_deadPigs[i]);
                _yawSmooth.Remove(_deadPigs[i]);
            }
            EmAdvanceFrame();               // ★ v191：全部敌人抄完 ⇒ 推进抽搐探针的帧号
            return true;
        }

        private static bool StillInWorld(World w, int id)
        {
            for (int i = 0; i < w.Enemies.Count; i++)
                if (w.Enemies[i] != null && w.Enemies[i].Id == id) return true;
            return false;
        }

        /// <summary>
        /// ★ v201：**这只敌人现在该用哪套几何**（表现层的唯一判据）。
        ///   飞蛾（`BossEnemy.MothPhaseIndex ≥ 0`）→ 三形态之一；
        ///   其余（含旧 BOSS）→ `FormFor(id, boss)`，与改前**逐字等价** ✓
        /// </summary>
        private static Species FormOf(EnemyActor e, bool boss)
        {
            if (e is BossEnemy b && b.MothPhaseIndex >= 0) return MothFormOf(b.MothPhaseIndex);
            return FormFor(e.Def.Id, boss);
        }

        /// <summary>
        /// ★ v182：小猪（`EnemyKind.Wander`）的**旧版路径** ★ —— 与虫怪完全无关。
        ///
        /// 它逐条复刻 `WorldView.SyncEnemies` 里 `_enemyModels` 那条旧分支现在的行为
        ///（那份代码是 v76/v80 定下来的，本函数只是把它搬到"接管方"这一侧，
        ///  因为旧分支已经不可能被走到：挂钩点会整段 return ✗）：
        ///   · 模型：`ModelView.Spawn("Characters","pig")` —— 会优先取 `Art/Anim/pig_idle` 预制体
        ///     （带完整关节层级，`SampleAnimation` 才打得中）✓
        ///   · 尺寸：`ScaleToHeight(RawHeight × displayScale)` —— 与旧分支同一句（v177 的结论：
        ///     敌人的 FBX 单位不可靠，用"目标米制高"入口与单位无关）✓
        ///   · 朝向：**面向自己的移动方向**（中立单位不盯玩家，v80）✓
        ///   · 霓虹：**不给红边**（红边是"敌人"的语言，v76）✓
        ///   · 状态 / 受击 / 染色：与旧分支逐字同款 ✓
        /// </summary>
        private static void SyncPig(Transform root, EnemyActor e, float dt, float displayScale)
        {
            if (!_pigs.TryGetValue(e.Id, out ModelView pig) || pig == null || pig.Go == null)
            {
                pig = ModelView.Spawn("Characters", PigModelId, root);
                if (pig == null)
                {
                    // 取不到资产就什么都别做（旧路径原本会退回胶囊；这里至少不凭空造一个
                    // 与虫怪混在一起的占位体，而是把原因报一次）
                    Log.Warn("小猪：取不到 Characters/pig（Resources/Art/Anim/pig_idle.fbx）⇒ 这一只没有模型");
                    return;
                }
                pig.ScaleToHeight(pig.RawHeight * displayScale);
                pig.SetNeon(new Color(0f, 0f, 0f, 0f));
                _pigs[e.Id] = pig;
            }

            Vector3 pos = new Vector3(e.Pos.x, 0f, e.Pos.y);
            pig.Go.transform.position = pos;
            bool moving = Moved(e.Id, pos, dt, out Vector3 dir, out _);   // 小猪走旧版 FBX ⇒ 不需要位移
            // 中立单位：永远面向**自己的移动方向**；没在动就保持上一帧朝向（不归零，否则会抽风式乱转）
            if (dir.sqrMagnitude > 0.0004f)
                pig.SetFacing(SmoothFacing(e.Id,
                    Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, dt));

            pig.Tick(dt);
            if (e.HitFlash > 0f) pig.PlayForced(ModelView.State.Hit);
            else if (moving) pig.Play(ModelView.State.Walk);
            else if (pig.Current == ModelView.State.Walk) pig.Play(ModelView.State.Idle);
            pig.SetHitFlash(e.HitFlash > 0f, dt);

            // ★ v191：小猪走**旧版 FBX 路径**，但它也在同一张探针表里 ⇒ 两条路**同帧可比** ✓
            // 它没有 `_phase`（`ModelView` 按 `_stateTime` 播）⇒ 相位一律传 0，
            // 报告里"相位/位移"对它会读成 0.0 rad/m，那正是"这一路没有时间驱动相位"的事实 ✓
            if (_emRemaining > 0) EmCapture(e.Id, e.Def.Id, pig.Go != null ? pig.Go.transform : null,
                                            (int)pig.Current, 0f);

            Color mul = Color.white;
            if (e.Status.Frozen) mul = new Color(0.55f, 0.85f, 1.35f);
            else if (e.Status.Slowed) mul = new Color(0.72f, 0.92f, 1.18f);
            else if (e.Status.Burning) mul = new Color(1.35f, 0.70f, 0.40f);
            else if (e.Status.Poisoned) mul = new Color(0.70f, 1.20f, 0.55f);
            mul *= Mathf.Lerp(0.45f, 1f, e.Health.HpPct);
            if (e.HitFlash > 0f) mul = Color.Lerp(mul, Color.white * 1.7f, 0.85f);
            pig.SetTint(mul);
        }

        /// <summary>当前在场上、且走**旧版模型路径**的小猪数量（诊断/截图验收用）。</summary>
        public static int PigCount => _pigs.Count;

        /// <summary>
        /// ★ v191-b：按**实例 id** 取虫怪渲染体（探针用；没有则 null）★
        ///
        /// 为什么探针不自己再找一遍 Transform：`_live` 就是"哪只敌人 ↔ 哪个渲染体"的**唯一真相** ✓
        /// 探针另建一张 id→Transform 的表 = 第二份真值，池回收时两边必然分叉 ✗
        /// </summary>
        public static BugView ViewOf(int enemyId)
            => _live.TryGetValue(enemyId, out BugView v) ? v : null;

        /// <summary>按实例 id 取**旧版小猪**模型（`SyncPig` 那条路；没有则 null）。</summary>
        public static ModelView PigViewOf(int enemyId)
            => _pigs.TryGetValue(enemyId, out ModelView m) ? m : null;

        // ══════════════════════════════════════════════════════════════════════
        // ★ v191：**敌人移动抽搐探针**（只读）★
        //
        // ── 要回答的问题 ──
        // "敌人移动抽搐"是对**位移/朝向**的描述，不是对骨骼的描述 ⇒ 所以要量的不是
        // `TickNimble` 里的关节角，而是每一帧**屏幕上那个 Transform 的世界位置与朝向** ✓
        // 两把尺子：
        //   · 位置 `Δ`：最大帧位移 ÷ 中位帧位移 —— 判据 **≤ 3**（尖峰/瞬移）
        //   · 朝向 `Δ`：连续帧的符号翻转 —— 判据**连续 3 帧内不翻转**（左右摆头）
        // 再加一把，验"相位该跟位移走、不该跟时间走"：
        //   · `相位/位移` 比：Σ|Δphase| ÷ Σ|Δpos|（米）。若相位是**时间驱动**的，
        //     这个比会**随移速变化**（减速时变大 = 脚在原地倒腾 = 打滑）✓
        //
        // ── 为什么四种一起采 ──
        // 它们在同一帧里同时在场上 ⇒ 一趟采样拿到四条曲线，省三趟；
        // 而且**同帧可比**（同一帧序、同一 dt）⇒ 读数之间可以直接横向对照 ✓
        //   `grunt` 锹形虫（走 `TickNimble`）／`jumper` 蚱蜢（有后腿弹开，**设计上就是一颠一颠**）
        //   ／`pig` 瓢虫…不，`pig` 是**旧版 FBX 路径**（`SyncPig`）⇒ 两条路并存，正好对照
        //   ／`shooter` 蛾（补一个，四种全覆盖）
        //
        // ⚠️ 只读：只写自己的静态数组，不碰任何玩法字段 / Transform ✓
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>探针盯的四种（与 `Resources/Data/enemies.json` 的 `id` 同字）。</summary>
        private static readonly string[] EmIds = { "grunt", "pig", "jumper", "shooter" };

        /// <summary>采样窗口上限（帧）。</summary>
        private const int EmMaxFrames = 900;

        private static int _emRemaining;
        private static int _emCount;
        private static string _emTag = "";
        private static string _emReport = "";
        private static int _emSeenMask;                       // 本帧哪几格采到了
        /// <summary>
        /// ★ 每格锁定的**敌人实例 id** ★
        ///
        /// 为什么必须锁实例（实测教训）：按**物种**记槽时，同一物种的第二只在刷出后会顶替槽位，
        /// 于是"上一帧的 A、这一帧的 B"被算成一次位移 ⇒ 实测读出 `grunt 位置 maxΔ=12.43m`、
        /// `shooter 25.79m`，四种敌人还在**同一帧**一起跳 ✗ 那是探针的错，不是游戏的错
        /// ⇒ 一旦某格绑定了 id，就只认那一个；它离场后该格作废（不再采别的实例）✓
        /// </summary>
        private static readonly int[] _emBound = { -1, -1, -1, -1 };
        private static readonly Vector2[] _emPos = new Vector2[EmIds.Length * EmMaxFrames];
        private static readonly float[] _emYaw = new float[EmIds.Length * EmMaxFrames];
        private static readonly float[] _emPhase = new float[EmIds.Length * EmMaxFrames];
        private static readonly int[] _emState = new int[EmIds.Length * EmMaxFrames];
        private static readonly bool[] _emHas = new bool[EmIds.Length * EmMaxFrames];

        /// <summary>布防：采 `frames` 帧后打一行 `EMOTION …` 到控制台。`tag` 用来区分改前/改后。</summary>
        public static void ArmEnemyMotionProbe(int frames, string tag)
        {
            _emTag = string.IsNullOrEmpty(tag) ? "emotion" : tag;
            _emCount = 0;
            _emRemaining = Mathf.Clamp(frames, 8, EmMaxFrames);
            _emReport = "";
            System.Array.Clear(_emHas, 0, _emHas.Length);
            for (int i = 0; i < _emBound.Length; i++) _emBound[i] = -1;
        }

        /// <summary>采样中？（`DebugApi`/`BugShowcase` 判断"现在布防会不会被覆盖"）</summary>
        public static bool EnemyMotionArmed => _emRemaining > 0;

        /// <summary>最近一次 `EMOTION …` 报告原文。</summary>
        public static string LastEnemyMotionReport => _emReport;

        private static int EmSlot(string defId)
        {
            for (int i = 0; i < EmIds.Length; i++) if (EmIds[i] == defId) return i;
            return -1;
        }

        /// <summary>把一只敌人**本帧渲染出来的**世界位置/朝向抄进 trace（`SyncEnemies` 里逐只调）。</summary>
        private static void EmCapture(int enemyId, string defId, Transform go, int state, float phase)
        {
            if (_emRemaining <= 0 || go == null) return;
            // ★ 探针绝不许拖垮游戏循环 ★ —— 它挂在每帧都走的那条路上，
            // 一个越界就能让玩法整帧抛出去（而"诊断代码把被诊断的对象搞坏"是最坏的一种 bug）✓
            try
            {
                int slot = EmSlot(defId);
                if (slot < 0) return;
                if (_emBound[slot] < 0) _emBound[slot] = enemyId;      // 锁定这只实例
                if (_emBound[slot] != enemyId) return;                 // 别的同种敌人 != 被我量到的那只
                if (_emCount < 0 || _emCount >= EmMaxFrames) return;
                int i = slot * EmMaxFrames + _emCount;
                Vector3 p = go.position;
                _emPos[i] = new Vector2(p.x, p.z);
                // 朝向用**最短弧**存（0..360 会出 359→1 的假跳变 ✗）
                _emYaw[i] = Mathf.DeltaAngle(0f, go.eulerAngles.y);
                _emPhase[i] = phase;
                _emState[i] = state;
                _emHas[i] = true;
                _emSeenMask |= 1 << slot;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Seedfall] EMOTION 采样异常：" + ex.Message); _emRemaining = 0; }
        }

        /// <summary>本帧所有敌人已抄完 ⇒ 推进帧号；窗口满就出报告（`SyncEnemies` 末尾调一次）。</summary>
        private static void EmAdvanceFrame()
        {
            if (_emRemaining <= 0) return;
            _emSeenMask = 0;
            _emCount++;
            _emRemaining--;
            if (_emRemaining <= 0)
            {
                try { LogEnemyMotionReport(_emTag); }
                catch (System.Exception ex)
                {
                    _emReport = "EMOTION " + _emTag + " 报告异常：" + ex.Message;
                    Debug.LogWarning("[Seedfall] " + _emReport);
                }
            }
        }

        /// <summary>把 trace 算成报告：位置 Δ / 朝向 Δ / 相位-位移比。</summary>
        private static void LogEnemyMotionReport(string tag)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[Seedfall] EMOTION ").Append(tag).Append(" | 窗口=").Append(_emCount).Append("帧");
            for (int slot = 0; slot < EmIds.Length; slot++)
            {
                int n = _emCount, got = 0;
                float maxD = 0f, medD = 0f, maxYaw = 0f, medYaw = 0f;
                int maxDFrame = -1, maxYawFrame = -1, yawFlips = 0, posFlips = 0;
                float sumPhase = 0f, sumPos = 0f;
                float minRatio = float.MaxValue, maxRatio = 0f;
                var dList = new System.Collections.Generic.List<float>();
                var ydList = new System.Collections.Generic.List<float>();
                // ★ 大朝向跳变的两种来源，必须分开数 ★
                //   位移**大**时的转角 = 真转向（或设计上的扑/跳）✓ 不该动它
                //   位移**小**时的转角 = `atan2` 拿一根很短的向量算出来的噪声 ✗ 那才是"抽头"
                int yawNoise = 0, yawReal = 0;
                var topYaw = new System.Collections.Generic.List<(float d, int f, float pos, int st)>();
                float prevDx = 0f, prevDyaw = 0f;
                bool hasPrevPos = false;
                for (int i = 0; i < n; i++)
                {
                    int k = slot * EmMaxFrames + i;
                    if (!_emHas[k]) continue;
                    got++;
                    if (i == 0 || !_emHas[k - 1])
                    {
                        // 上一帧不在场（刚刷出来 / 中间缺帧）⇒ 不能算 Δ，也不能留旧的一阶值
                        // （留着旧值会让"缺帧前后的两帧"被当成一次反向 ✗）
                        hasPrevPos = false; prevDx = 0f; prevDyaw = 0f;
                        continue;
                    }
                    int kp = slot * EmMaxFrames + i - 1;
                    Vector2 dp = _emPos[k] - _emPos[kp];
                    float ad = dp.magnitude;
                    float dyaw = Mathf.DeltaAngle(_emYaw[kp], _emYaw[k]);
                    dList.Add(ad);
                    ydList.Add(Mathf.Abs(dyaw));
                    if (ad > maxD) { maxD = ad; maxDFrame = i; }
                    if (Mathf.Abs(dyaw) > maxYaw) { maxYaw = Mathf.Abs(dyaw); maxYawFrame = i; }
                    // 反向：|Δ| 足够大才计（否则 0.01° 的量化台阶会被数成"翻转"，见 SmoothAttackPose 的注）
                    if (hasPrevPos && Mathf.Abs(dyaw) > 0.5f && Mathf.Abs(prevDyaw) > 0.5f
                        && (dyaw > 0f) != (prevDyaw > 0f)) yawFlips++;
                    // 位置"反向"：把位移投影到主方向上看是否掉头（瞬移会表现为单帧尖峰，不一定反向）
                    if (hasPrevPos && Mathf.Abs(dp.x) > 1e-5f && Mathf.Abs(prevDx) > 1e-5f
                        && (dp.x > 0f) != (prevDx > 0f)) posFlips++;
                    float dphase = Mathf.Abs(_emPhase[k] - _emPhase[kp]);
                    sumPhase += dphase; sumPos += ad;
                    // 大朝向跳变归类（阈值 10°）：位移 < 1cm 的算噪声
                    if (Mathf.Abs(dyaw) > 10f)
                    {
                        if (ad < 0.010f) yawNoise++; else yawReal++;
                        topYaw.Add((Mathf.Abs(dyaw), i, ad, _emState[k]));
                    }
                    if (ad > 1e-4f)
                    {
                        float r = dphase / ad;
                        if (r < minRatio) minRatio = r;
                        if (r > maxRatio) maxRatio = r;
                    }
                    prevDx = dp.x; prevDyaw = dyaw;
                    hasPrevPos = true;
                }
                if (got < 4) { sb.Append("\n    ").Append(EmIds[slot].PadRight(7)).Append(" 没采到（窗口里不在场）"); continue; }
                dList.Sort(); ydList.Sort();
                medD = dList.Count > 0 ? dList[dList.Count / 2] : 0f;
                medYaw = ydList.Count > 0 ? ydList[ydList.Count / 2] : 0f;
                float spike = medD > 1e-5f ? maxD / medD : 0f;
                sb.Append("\n    ").Append(EmIds[slot].PadRight(7))
                  .Append(" 位置 maxΔ=").Append(maxD.ToString("0.0000")).Append("m@f").Append(maxDFrame)
                  .Append(" 中位=").Append(medD.ToString("0.0000"))
                  .Append(" 峰/中=").Append(spike.ToString("0.0")).Append(spike <= 3f ? "✓" : "✗")
                  .Append(" | 朝向 maxΔ=").Append(maxYaw.ToString("0.00")).Append("°@f").Append(maxYawFrame)
                  .Append(" 中位=").Append(medYaw.ToString("0.00")).Append("°")
                  .Append(" 反向=").Append(yawFlips).Append("次")
                  .Append(" | 大转角 真的").Append(yawReal).Append("/噪声").Append(yawNoise)
                  .Append(" | 相位/位移=").Append((sumPos > 1e-4f ? sumPhase / sumPos : 0f).ToString("0.0"))
                  .Append("rad/m");
                if (minRatio < float.MaxValue * 0.5f)
                    sb.Append("(区间 ").Append(minRatio.ToString("0.0")).Append("~")
                      .Append(maxRatio.ToString("0.0")).Append(")");
                // 最大的几处转角，附当时的位移与状态 —— "设计上的扑跳"和"抽头"在这里分得开
                topYaw.Sort((a, b) => b.d.CompareTo(a.d));
                int tops = Mathf.Min(4, topYaw.Count);
                if (tops > 0)
                {
                    sb.Append("\n            最大转角：");
                    for (int j = 0; j < tops; j++)
                    {
                        var t = topYaw[j];
                        sb.Append(" f").Append(t.f).Append(' ').Append(t.d.ToString("0.0")).Append("°(位移")
                          .Append(t.pos.ToString("0.000")).Append("m,st").Append(t.st).Append(')');
                    }
                }
            }
            _emReport = sb.ToString();
            Debug.Log(_emReport);
            // ★ 同时落盘 ★ —— 桥只能按"场景里某个组件"取返回值，而槽位/场景重载会把这个组件
            // 连同它的引用一起换掉（实测：布防成功、1 秒后取报告就 "no object named BugShowcase"）
            // ⇒ 结论不该依赖一个会消失的对象，写文件最稳 ✓
            // 写在 `Library/` 下：不污染 `Assets/`（不会被 Unity 导入），但仍然好找 ✓
            try
            {
                string dir = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(Application.dataPath, "..", "Library"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "emotion-report.txt"), _emReport);
            }
            catch (System.Exception) { /* 写不了文件不影响控制台那一份 */ }
            _emRemaining = 0;               // 采完自动撤防（不调 Arm，那条会把刚算好的计数清零）
        }
    }
}
