namespace Seedfall
{
    /// <summary>
    /// ★★ 训练模式开关（v300）—— 战斗代码里**唯一**允许出现的训练相关判断 ★★
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 一、为什么是"静态开关"而不是"另写一套战斗"
    /// ══════════════════════════════════════════════════════════════════════
    ///   用户原话：「重新制作训练模式，沿用与战斗模式**一模一样**的操作、三种武器、各种 UI、
    ///   菜单、完全一样的人物和动画、与战斗一样的场景精细度和风格」。
    ///
    ///   "一模一样"只有一种可靠实现方式：**跑同一份代码**。
    ///   上一轮的训练场是"自包含实现"（`TrainingGame` 自己写了移动/攻击/相机/HUD），
    ///   于是它和战斗的差别是**结构性的** —— 手感、UI、动画每一样都是"另一个人再写一遍"，
    ///   不可能一致 ✓ 本轮改成：`Training.unity` 里挂**同一个 `GameBootstrap`**，
    ///   训练场与战斗的启动序列、输入、HUD、模型、动画、相机、耕地、商店车**全部是同一段代码**。
    ///
    ///   训练场与战斗的差别只剩下面这几个布尔值 ⇒ 差别可以被**逐个枚举**，
    ///   而"另一套实现"的差别无法枚举（它无边无际）✓
    ///
    /// ══════════════════════════════════════════════════════════════════════
    /// 二、默认值全部是 false ⇒ 战斗模式行为**逐字节不变**
    /// ══════════════════════════════════════════════════════════════════════
    ///   本类**不被任何战斗流程初始化**：只有 `TrainingBootstrap.Awake`
    ///   （`[DefaultExecutionOrder(-1000)]`，比 `GameBootstrap.Awake` 更早）会调
    ///   <see cref="Enable"/>。战斗场景（`SeedfallArena.unity`）里没有这个组件
    ///   ⇒ 全部开关恒为 false ⇒ 战斗侧每一处 `if (TrainingMode.Xxx)` 守卫都**不成立**，
    ///   原语句原样执行 ✓ （逐处论证见各守卫点的注释与交付报告 ②）
    ///
    /// ⚠️ 静态字段**跨场景残留**：从训练场回主城/回战斗时它们是 true ✗
    ///   ⇒ 由 <see cref="Disable"/> 在 `TrainingBootstrap.OnDestroy` 里复位，
    ///     并且 `Enable` 每次都**全量赋值**（不做增量），所以即便上一次没复位干净，
    ///     下一次进训练场也会被重新写成同一组值 ✓
    /// </summary>
    public static class TrainingMode
    {
        // ══════════════════════════════════════════════════════════════════
        // 开关（前五个是 v300 用户点名的；后三个是 v303 的三件事；默认全关）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// 训练模式总开关。**默认 false**。
        /// 它是"这一局是训练场"的唯一判据 —— 下面几个细分开关都由 <see cref="Enable"/> 一起置位。
        ///
        /// ⚠️ 与细分开关的分工：本字段只用于**日志与诊断读数**（"现在是不是训练场"），
        ///   战斗代码里的守卫**一律读细分开关**（`NoWaves` / `InfiniteGold` / …）。
        ///   理由是细分开关的语义能单独被证明（"关掉金币这一条 ⇒ 金币逻辑与改前逐字节相同"），
        ///   而一个总开关无法说明"它到底改了几件事" ✓
        /// </summary>
        public static bool Active;

        /// <summary>true = 不刷波次敌人（也不刷 BOSS）。默认 false。</summary>
        public static bool NoWaves;

        /// <summary>true = 金币每帧补满到 <see cref="GoldTopUp"/>。默认 false。</summary>
        public static bool InfiniteGold;

        /// <summary>true = 商店候选池跳过"主城已购买"的解锁门（等价"全解锁"）。默认 false。</summary>
        public static bool ShopSellsEverything;

        /// <summary>
        /// true = 稻草人的**身体**不由战斗的敌人表现层画（`ProceduralBugs` 跳过它），
        /// 改由训练场自己画稻草人。默认 false。
        /// 见 `ProceduralBugs.SyncEnemies` 里那行守卫与交付报告 ①。
        /// </summary>
        public static bool HideDummyBody;

        /// <summary>无限金币的补满目标值。</summary>
        public static int GoldTopUp = 999999;

        // ★★ v303：常驻仓库 / 三项补满 / 跳过升级选属性 —— 三个新开关，默认全 false ★★
        //
        //  ⚠️ 它们与上面五个同一条纪律：`Enable()` 只由 `TrainingBootstrap.Awake` 调，
        //     战斗场景里没有任何组件调它 ⇒ 字段初值（false）就是战斗模式看到的值。
        //     读取点分别在：
        //       · `WaterAlwaysFull` / `AmmoAlwaysFull` → 本工程的训练侧组件 `TrainingVault.Update`
        //         （**训练场专属组件**，战斗场景里根本不存在 ⇒ 这两条连"守卫不成立"这一步都不会走到）
        //       · `SkipLevelUpChoice` → 战斗文件 `RunSession.OnLevelUp` 里的一行守卫
        //         （false 时下一行 `SetPhase(PhaseState.LevelReward, 0f)` 原样执行，见交付报告 ⑤）

        /// <summary>
        /// true = 训练场里**耕地水分**每帧补满（`FarmPlot.Water = WaterMax`）。默认 false。
        /// 读取点：`TrainingVault.Refill`（训练场专属组件）。
        /// </summary>
        public static bool WaterAlwaysFull;

        /// <summary>
        /// true = 训练场里**水壶充能**（`RunSession.WaterAmmo`）与**肥料充能**（`FertAmmo`）
        /// 每帧补满。默认 false。读取点：`TrainingVault.Refill`（训练场专属组件）。
        /// </summary>
        public static bool AmmoAlwaysFull;

        /// <summary>
        /// true = 升级时**不弹「选属性」面板**（不进入 `PhaseState.LevelReward`），
        /// 改为自动选第一张卡（属性照常入账）。默认 false。
        /// 读取点：`RunSession.OnLevelUp` 里唯一的一行守卫。
        /// </summary>
        public static bool SkipLevelUpChoice;

        // ★★ v306：本轮（用户点名三件事）新增的三个开关 —— 默认全 false ★★
        //
        //  与上面八个同一条纪律：`Enable()` 只由 `TrainingBootstrap.Awake` 调，
        //  战斗场景里没有任何组件调它 ⇒ 字段初值（false）就是战斗模式看到的值。
        //  读取点全部是**纯插入守卫**，逐处论证见交付报告 ⑦：
        //    · `NoShopTruck` → `RunSession.TickShopTruck` 首行 + `WorldView.SyncShopTruck`
        //    · `NoGacha`     → `ImGuiHud` 的 `case PhaseState.LevelReward` 一行
        //    · `NoHeal`      → `HealthPool.Heal` 首行（**回血的唯一收口点**）+ `PlayerActor.Tick`
        //                      里"最大生命 +N 时同步补血"那一句

        /// <summary>
        /// true = **完全不生成 / 完全不显示商店车**（不推进状态机、不摆模型、不播 E 提示）。
        /// 默认 false。读取点：`RunSession.TickShopTruck`（首行 return）与
        /// `WorldView.SyncShopTruck`（强制隐藏）。
        /// </summary>
        public static bool NoShopTruck;

        /// <summary>
        /// true = **不出现抽卡界面**。默认 false。
        /// 读取点：`ImGuiHud` 的 `switch (s.Phase)` 里 `case PhaseState.LevelReward` 一行。
        /// 注意它与 <see cref="SkipLevelUpChoice"/> 是**两道**：后者让阶段永远进不去
        /// （所以面板根本不具备出现的条件），前者是"万一阶段被别的路径推进来，也不画"。
        /// </summary>
        public static bool NoGacha;

        /// <summary>
        /// true = **一切回血都不生效**（红心拾取 / 击杀回血 / 吸血 / 整点回满 / 生命回复 /
        /// 最大生命 +N 时的同步补血）。默认 false。
        /// 读取点是回血的**唯一收口点** `HealthPool.Heal` 的首行 —— 所有来源都汇到它。
        /// ⚠️ 护盾（`Shield`）不在此列：它不是"回血"，`RefillShield` / 护盾回复照旧。
        /// </summary>
        public static bool NoHeal;

        // ★★ v307：本轮（用户原话："训练模式修改开局不选择植物，空植物进入"）★★
        //
        //  与上面十一个同一条纪律：`Enable()` 只由 `TrainingBootstrap.Awake` 调，
        //  战斗场景里没有任何组件调它 ⇒ 字段初值（false）就是战斗模式看到的值。
        //  读取点两处，都是**纯插入守卫**（逐条论证见交付报告）：
        //    · `RunSession.TickPrepare`（首行）—— 跳过开局三选一，直接 `StartWave(1)` 进战斗
        //    · `TrainingVault.ApplyAll`（种子那一段）—— 新会话**不把清单里的植物装回去**

        /// <summary>
        /// true = **开局不带任何植物**：不进「开局三选一」、`SeedBag` 里不留种子、耕地上不自动种。
        /// 默认 false。
        ///
        /// 读取点：
        ///   · `RunSession.TickPrepare` 首行 —— 训练场里直接 `StartWave(1)`（阶段变 Combat）并 return，
        ///     于是后面那四句（`InitialSeed = …` / `Bag.AddSeed` / `AutoPlantSeed` / 遥测）**一句都不执行**
        ///   · `TrainingVault.ApplyAll` 的种子循环 —— 新会话不把 `WishSeeds` 种回去（并清空该清单）
        /// ⇒ 开局是"空手空袋空田"，植物**只能**由玩家在仓库面板里选（`TrainingVault.EquipSeed` 那条路不变）✓
        /// </summary>
        public static bool EmptyStart;

        // ★★ v308：本轮（用户原话："默认不要装备任何东西（插件存在异常装备）"）★★
        //
        //  ── 为什么不能复用 `EmptyStart` ──
        //    `EmptyStart` 的语义被写死成"**种子**开局不带"（`TickPrepare` 跳过开局三选一、
        //    `ApplyAll` 不把植物种回去），两个读取点都在种子链上。
        //    道具与插件是**另外两条链**（`ItemStacks` / `Goods.Plugins`），
        //    把它们的开关塞进 `EmptyStart` 会让"关掉种子"连"插件"一起改 ⇒ 一个开关两件事 ✗
        //    ⇒ 单独一个开关，只回答"开局该不该白送东西"这一件事 ✓

        /// <summary>
        /// true = 训练场**开局不带任何道具 / 插件**：
        /// 三把武器的插件位全空、背包里也没有 `WeaponSystem.DefaultOwnedPlugins` 那两件。
        /// 默认 false。
        ///
        /// 读取点（两处，都是**纯插入**守卫）：
        ///   · `RunSession.Start` —— `GrantDefaultOwned` 那一行前面的三元判断（false ⇒ 原样调用）
        ///   · `TrainingVault.ApplyAll` —— 新会话绑定时把 `Slot == None` 的待装插件清掉
        ///   ⚠️ `RunSession.Start` 是战斗与训练**共用**的一段，所以那一行的"false 时不变"
        ///      论证必须逐字成立：见 `RunSession.cs` 那一处注释 ①。
        /// </summary>
        public static bool CleanLoadout;

        // ══════════════════════════════════════════════════════════════════
        // 稻草人的敌人 id（数据在 `Assets/Resources/Data/enemies.json`）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>普通稻草人的敌人 id。</summary>
        public const string DummyId = "training_dummy";

        /// <summary>巨人稻草人的敌人 id（同一套数据，体型放大 —— 见训练场布置常量）。</summary>
        public const string DummyBossId = "training_dummy_boss";

        /// <summary>
        /// ★ v312：**靶场稻草人**（会开枪的那个，`kind = "Shooter"`）的敌人 id。
        ///
        /// ── 为什么它也进 <see cref="IsDummyId"/> ★★★ ──
        /// <see cref="IsDummyId"/> 的两个读者（`WorldView.cs:2350` / `ProceduralBugs.cs:3009`）
        /// 管的是**"这只敌人的身体归谁画"**：
        ///   · `kind = Wander` ⇒ 战斗表现层画的是**小猪 FBX**（`SyncPig`）✗
        ///   · `kind = Shooter` ⇒ 战斗表现层画的是**一只程序化虫怪**（`Acquire` + `Species`）✗
        /// 两条都**不是稻草人**。既然用户要的是"稻草人"，身体就必须由训练场自己画
        /// （`TrainingBootstrap.BuildScarecrow`）⇒ 它必须被这里认到，否则画出来的是一只紫色的虫 ✗
        ///
        /// ⚠️ 连带义务（不能只改这一行）：`TrainingBootstrap.SpawnAll` 必须**真的为它调一次
        ///    `BuildScarecrow`**，否则"身体被藏掉 + 没人画" ⇒ 它在地图上**完全看不见**
        ///    （只有子弹从空气里飞出来）。这条链的落点写在 `TrainingBootstrap` 的 v312 注释里，
        ///    自检口径：掷出 `IsDummyId(id)` 为真 ⇒ `SpawnAll` 里必定有一条生成它的语句 ✓
        /// </summary>
        public const string GunnerId = "training_gunner";

        /// <summary>这个敌人 id 是不是稻草人。**只读**，训练与战斗两侧共用同一个判据。</summary>
        public static bool IsDummyId(string id)
            => id == DummyId || id == DummyBossId || id == GunnerId;

        // ══════════════════════════════════════════════════════════════════
        // 置位 / 复位（唯一的两处写者）
        // ══════════════════════════════════════════════════════════════════

        /// <summary>进入训练场：**全量**写一次（不依赖上一次的值）。</summary>
        public static void Enable(int goldTopUp = 999999)
        {
            Active = true;
            NoWaves = true;
            InfiniteGold = true;
            ShopSellsEverything = true;
            HideDummyBody = true;
            WaterAlwaysFull = true;
            AmmoAlwaysFull = true;
            SkipLevelUpChoice = true;
            NoShopTruck = true;
            NoGacha = true;
            NoHeal = true;
            EmptyStart = true;          // ★ v307：训练场开局不带任何植物 ★
            CleanLoadout = true;        // ★ v308：训练场开局不带任何道具 / 插件 ★
            GoldTopUp = goldTopUp;
            // ★ v301：把"商店全解锁"的**唯一真相**同步给 `TrainingShop` ★
            //   上一轮两路各写了一份守卫（`TrainingMode.ShopSellsEverything` 与
            //   `TrainingShop.SellEverything`），同一个语义留两份是维护隐患 ⇒
            //   现在 `RunSession.IsInPool` 只读 `TrainingShop.SellEverything`，
            //   由这里负责把它一起置位（这样训练场里"仓库全解锁"与"按 B 打开商店"两条路
            //   用的是同一个开关，不会出现"开了训练场但商店只解锁一半"）。
            TrainingShop.SellEverything = true;
        }

        /// <summary>离开训练场：全部复位成"战斗模式的样子"。</summary>
        public static void Disable()
        {
            Active = false;
            NoWaves = false;
            InfiniteGold = false;
            ShopSellsEverything = false;
            HideDummyBody = false;
            WaterAlwaysFull = false;
            AmmoAlwaysFull = false;
            SkipLevelUpChoice = false;
            NoShopTruck = false;
            NoGacha = false;
            NoHeal = false;
            EmptyStart = false;         // ★ v307 ★
            CleanLoadout = false;       // ★ v308 ★
            GoldTopUp = 999999;
            TrainingShop.SellEverything = false;
        }

        /// <summary>一行读数（日志/探针用）。</summary>
        public static string Describe()
            => $"Active={Active} NoWaves={NoWaves} InfiniteGold={InfiniteGold}"
             + $" ShopSellsEverything={ShopSellsEverything} HideDummyBody={HideDummyBody}"
             + $" WaterAlwaysFull={WaterAlwaysFull} AmmoAlwaysFull={AmmoAlwaysFull}"
             + $" SkipLevelUpChoice={SkipLevelUpChoice}"
             + $" NoShopTruck={NoShopTruck} NoGacha={NoGacha} NoHeal={NoHeal}"
             + $" EmptyStart={EmptyStart} CleanLoadout={CleanLoadout}"
             + $" GoldTopUp={GoldTopUp}";
    }
}