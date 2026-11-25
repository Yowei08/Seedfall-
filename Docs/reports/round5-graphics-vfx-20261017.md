# 第五轮 · 画质（①②③④⑤）与 插件×特效（⑥⑦）施工报告

> 落档时间 2026-10-17 · 由协调者从两条子任务报告中整理录入
> 第五轮十项里，⑨⑩ 见 `ui-combat-hud-v177.md`（同目录），⑧ 像素字仍待落地

---

## 一、① 抗锯齿 ✗ **MSAA 4×**

### 选型依据

- 渲染器是 **Forward+**（`Assets/Settings/PC_Renderer.asset` 的 `m_RenderingMode: 2`）。Forward+ **支持 MSAA**，Deferred 不支持 ⇒ 前提成立。
- 本作是**硬边卡通 ＋ 黑色描边**。FXAA / SMAA 都是后处理，会把**描边与色阶一起抹软** ⇒ 直接和"清晰度"要求对打。MSAA 只在**几何边缘**做多采样，内部色阶（toon 分界、墨线）保持硬。
- URP 17.4 里 SMAA/TAA 确实存在（`AntialiasingMode` 有 `SubpixelMorphologicalAntiAliasing` / `TemporalAntiAliasing`，`SmaaPostProcessPass.cs`），但 TAA 有拖影、SMAA 会磨平墨线。

### 实测（同一次 play 会话内只改 `PC_RPAsset.m_MSAA`，不触发域重载）

| `m_MSAA` | fps（3 次取样，中位） | drawCalls | triangles | SetPass |
|---|---|---|---|---|
| 4 | 27 / 35 / 40（中位 **35**） | 10243–10636 | ~2.03M | ~1145 |
| 1 | 39 / 40 / 42（中位 **40**） | 10616–10720 | ~2.03M | ~1143 |

场景状态几乎相同（triangles 差 **0.4%**、drawCalls 同级）⇒ 这组**可比**。

**结论：4× MSAA 的代价 ≈ 12.5% 帧率（40 → 35），`drawCalls` / `SetPass` 完全不变**（MSAA 是带宽成本，不是绘制次数成本，符合预期）。**保留 4×。**

清晰度另外靠：`m_RenderScale` **保持 1.0**（没有降分辨率换帧数）、`anisotropicTextures: 2`（Force Enable）、阴影贴图 2048 → **4096**。

---

## 二、② 光影升级 ✗ 三条实质改动

1. **光照模型从半兰伯特改成真 N·L**
   旧式 `dot*0.5+0.5` 把背光面抬到 **0.5**，而阈值 `_ShadeThreshold` 是 **0.44**（`m_ShadeThreshold = 0.42` 默认定标）⇒ `dot > −0.12` 全算亮部 ⇒ **约 84% 的球面是亮的**，明暗分界几乎看不见。**这是"画面平"的根因。**
2. **新增环境光（间接光）项**
   旧版**完全没有间接光**：主光一落到 0，模型就是纯 `_ShadeColor`；昼夜循环写进 `RenderSettings.ambientLight` 的值对着色器**毫无影响**。现在用 URP 的 `SampleSHVertex(N)` 取间接漫反射 ⇒ 夜里是冷蓝环境、黄昏是暖橙。
3. **SSAO 首次真正接进 toon 材质**
   `PC_Renderer` 的 SSAO 一直开着，但 toon shader 从没采样过 ⇒ 只有地面（URP/Lit）吃到 AO，角色/敌人/植物/装饰一点没吃到。另修 `DepthNormals` 输出**原始法线**（URP 要**八面体编码**）⇒ 旧 SSAO 解出的法线是错的。

顺带修：`_SHADOWS_SOFT` 在 URP 17 已拆成 `_SHADOWS_SOFT_LOW / _MEDIUM / _HIGH`，旧 shader 只声明旧名 ⇒ **那个变体永不被选中** ⇒ `m_SoftShadowQuality = 3` **形同虚设，影子一直是硬边**。

---

## 三、③ 所有特效照亮 ✗ 两个根因

### 根因（致命的那条）

渲染器是 **Forward+** ⇒ 附加光源走**簇（cluster）索引**。旧 shader **没有 `_CLUSTER_LIGHT_LOOP`** ⇒

**`GetAdditionalLightsCount()` 返回 0 ⇒ 附加光源循环一次都不执行，而且不报错。**

这就是"火焰 / 子弹 / 爆炸不照亮"的真正原因 —— 在此之前，给特效挂多少个 `Light` 都不会被采样。

第二条：`_SHADOWS_SOFT` 的变体名（见 ②）。

### 实现

- toon 前向 Pass 改用 `LIGHT_LOOP_BEGIN / LIGHT_LOOP_END`；补 `_ADDITIONAL_LIGHTS` / `_ADDITIONAL_LIGHT_SHADOWS` / `_SCREEN_SPACE_OCCLUSION` / `_CLUSTER_LIGHT_LOOP`。
- 光池 **4 → 8**。
- 新增 `NightLighting.IlluminateBeam(...)`：激光**专用灯，不占池** —— 持续激光 60/s 会把 8 格全霸占。
- 补上原先漏的 **激光 / 敌人出生预警 / Boss 技能** 三处。

### 判据（引擎自己打印）

`NightLighting.VfxLightReport()` 实测输出：

```
⇒ 正在照亮的特效光源 3/8｜总强度 5.40｜最远射程 3.20m
```

3 盏 `intensity = 1.8` / `range = 3.20` —— 正好是新增的**出生预警**参数，证明它真的在亮，而不是"加了代码没生效"。

---

## 四、④⑤ 阴影 ✗ 根因不是"阴影太弱"，是**没有投影这一步**

### 根因

**两个 toon shader 都没有 `LightMode=ShadowCaster` 的 Pass。** URP 阴影贴图只渲染声明了该 Pass 的材质 ⇒ 整场一个影子都没有，**且不报错**。而**每个模型都经 `ModelView.cs:425 ToonLook.Apply`** ⇒ 玩家 / 各敌人 / 植物 / 装饰 / 建筑**全都不投影**。

**而"谁在关阴影"的答案是：没有任何代码。** 全 `Assets/Scripts` 对 `shadowCastingMode | ShadowCastingMode | receiveShadows | castShadows` **0 处赋值** ⇒ 全部沿用 `Renderer` 默认 `On`。因此"逐项列 `On → On`"是一张空表 —— 不给。

### 被推翻的旧注释

`NightLighting.Setup()` 里 `QualitySettings.shadowDistance = 140` —— **URP 完全忽略这个字段**（连同 `shadowCascades` / `shadowResolution` / `shadowProjection`），那个调整**一个字节都没影响渲染**。已把 `LiveDump` 改成读 **RPAsset 真值**，并同时打印 `QualitySettings（URP 下无效，仅留档）`。

### 改动

| 项 | 改前 | 改后 |
|---|---|---|
| `Seedfall/Toon` pass 数 | 4 | **6**（补 `ShadowCaster` ＋ `DepthOnly`） |
| `Seedfall/ToonSimple` pass 数 | 1 | **4**（再补 `DepthNormals`） |
| `m_ShadowDistance` | 50 | **80**（场地 40×40m、对角线 **56.6m**、相机 60° 俯视 22m ⇒ 50 确实漏角） |
| `m_MainLightShadowmapResolution` | 2048 | **4096** |
| `m_ShadowCascadeCount` | 4 | 4 |
| `m_SoftShadowsSupported` / `m_SoftShadowQuality` | 1 / 3 | 1 / 3（现在**真的生效**） |
| bias（depth / normal） | 0.1 / 0.5 | **0.15 / 0.8** |

**顶点零变形，与 forward pass 逐位一致 ⇒ 影子不会与模型错位。**

### 最硬的量化证据

```
triangles  414,715  →  1,319,099   （×3.2）
```

⇒ **阴影贴图真正开始画所有投影体。**

另有独立复验：`Docs/captures/capture-101444.png` —— 栅栏 / 木箱 / 木桶 / 树 / 岩石**都有斜向投影**，树下地面一大片阴影。

---

## 五、⑥⑦ 插件 × 武器特效 ✗ 通路原本**完全没有接**

### 根因

| 位置 | 改动前的事实 |
|---|---|
| `MeleeSwingEvent` / `BeamEvent` / `FertilizerBlastEvent` / `FertilizerTelegraphEvent` | 只携带**几何信息**（起点、终点、半径、张角），**没有任何颜色字段** |
| `WorldView.OnMeleeSwing` / `OnBeam` / `OnFertBlast` | 颜色**全部硬编码**（如 `new Color(1f,0.96f,0.62f)` / `new Color(0.4f,0.95f,1f)` / `new Color(1f,0.62f,0.2f)`） |
| `VfxMat` / `Mat` | 每调用一次就 `new Material`，**从不销毁** |

⇒ **无论是否安装插件，画面上一定完全相同。** 这不是"特效不够好"，而是**没有形成通路**。

### 新增接入层 `Assets/Scripts/Combat/PluginFx.cs`

纯函数 `PluginFx.For(in WeaponModifiers, WeaponSlot) → FxSpec`，把 17 个插件字段翻译成 **8 个表现通道**：

```
Core / Rim（颜色）· Scale（尺寸）· Glow（发光）· Sparks（火花数）
Rings（冲击环数）· Trail（拖尾）· Shake（震屏）
```

三条设计约束：
1. **空槽 = 改动前的硬编码外观** ⇒ 所以截图差异**只可能来自插件**；
2. 每种 effect **至少改一个看得见的通道**；
3. 纯函数、可离线断言。

接线点：`FertilizerTelegraph` / `MeleeSwing` / `Beam` / `FertilizerBlast` 四个事件各加一个 `FxSpec Fx` 字段，由 `RunSession` 的七个发布点填入（`SwingHoe` · `FireBeam` · `ArmBlast` → `FertilizerTelegraphEvent` · `ExplodeAt` → `FertilizerBlastEvent` · `FireSwordWaves` · `PluginChain` · `DebugSwing`）。消费点：`WorldView.OnMeleeSwing` / `OnBeam` / `OnFertBlast` / `OnFertTelegraph`。

### 判据（可自动化，不看图）

`DebugApi.PluginFxAudit()`，三条：

1. `PluginFx.Diff(空槽, 只装这一件)` **必须非空** —— 比较的正是 `WorldView` 真正拿来画的 8 个通道；
2. 同武器内**签名不得重复**（量化后的 FNV-1a）⇒ 排除"两件插件画出同一个样子"；
3. **`SourceCount > 0`** ⇒ 否则"压根没装上"会被误判成"装了但没变化"。

实测原文（节选）：

```
PLUGINFX 插件×特效审计（判据：Diff(空槽, 只装这一件) 必须非空）
── 锄头｜空槽基线 FFF49E/FFD859 尺寸×1.00 发光×1.00（火花0 环0 拖尾0 震屏×1.00）
  ✓ hoe_whetstone   伤害    色 FFF6A9/FFD859 尺寸×1.11 发光×1.12 火花0 环0 拖尾0.00 震屏×1.00｜变的是：Core Scale Glow
  ✓ hoe_frostedge   命中减速 色 A4E3DF/D4E9BC 尺寸×1.00 发光×1.22 火花1 环0 拖尾0.32 震屏×1.00｜变的是：Core Rim Glow Sparks Trail
  ✓ hoe_embershard  命中灼烧 色 FF8243/FFDA47 尺寸×1.08 发光×1.42 火花2 环0 拖尾0.00 震屏×1.00｜变的是：Core Rim Scale Glow Sparks
  …（30 条全列）
── 结论：30/30 件插件都让特效发生了变化 ✓ 全部通过
```

**30 / 30，无例外需要点名。**

### 顺带修掉的三条真缺陷

1. **材质泄漏**：`VfxMat` / `Mat` 每次 `new Material` 且从不销毁 —— 水壶持续激光约 **60 份/秒** 原生内存净增长。已改 `VfxMatCached` / `MatCached`（按量化颜色缓存，键量化到 `Color32`）+ 逐实例材质显式销毁（用 `Destroy(m, lifetime)` 延时重载，不销毁正在渲染的那一份）。
2. **淡出从未生效的前置隐患**：`TickBlasts` 用 `rend.material`（可能复制材质）逐帧读写 ⇒ 改为结构体持有 `Material` + 显式 `_BaseColor` 写入（`sharedMaterial`）。
3. **色相被截断**：着色器 `_CoreBoost = 1.35` 把挥击弧**烧成黄白**，插件给的冰蓝 / 炭红读不出来。已给弧类特效用 `1.0`，弹丸 / 爆炸保留 `1.35`。

### 截图

含 HUD、`插件 0/6 → 1/6`：`Docs/captures/hoe_A0_empty.png` / `hoe_B1_frostedge.png` / `hoe_C2_embershard.png` / `h1_can_scaldjet.png` / `h2_fert_empty.png` / `h2_fert_scorch.png` / `h2_fert_echocan.png`。

> **肥料组 3 张未肉眼核对**（只有审计数值证明）—— 如实标注。
> 无效中间产物：`fx01-03`（黑图）、`fx10-12`（撞上选种子弹窗）仍在 `Docs/captures`，应挪入 `v175\invalid\`。

---

## 六、涉及文件

```
Assets/Shaders/SeedfallToon.shader          ShadowCaster + DepthOnly + DepthNormals(八面体) + 前向关键字 + 光照模型
Assets/Shaders/SeedfallToonSimple.shader    同上（pass 数 1 → 4）
Assets/Settings/PC_RPAsset.asset            m_MSAA 1→4 · ShadowDistance 50→80 · Shadowmap 2048→4096 · bias 0.15/0.8
Assets/Scripts/Art/NightLighting.cs         光池 4→8 · IlluminateBeam · VfxLightReport · 读 RPAsset 真值
Assets/Scripts/Combat/PluginFx.cs           新增：FxSpec + PluginFx.For/Diff/Describe（需在 csproj Compile 列表）
Assets/Scripts/Actors/Actors.cs             四个特效事件加 FxSpec 字段
Assets/Scripts/Flow/RunSession.cs           七个发布点填 FxSpec（只动攻击/伤害入口）
Assets/Scripts/Bootstrap/WorldView.cs       VfxMatCached / 材质销毁 / FxSpec 消费 / 三处 Illuminate
Assets/Scripts/Debug/DebugApi.cs            PluginFxAudit / VfxLightReport 入口
```

---

## 七、验证

```
Seedfall.Runtime.csproj  -t:Rebuild  → 0 error（10 warning，与改动前基线同一批）
Seedfall.Tests.csproj    -t:Rebuild  → 0 error
DebugApi.SelfCheck()                 → 自检 PASS：425/425 通过
```

---

## 八、未做到 / 未验证（如实）

1. **⑧ 像素字没做** —— 见另一条任务；根因已查清（`ui.scale = 1.35` 的矩阵缩放把整数字号变成分数像素）。
2. **干净的改前/改后 fps 对比没取成** —— 改前那次与改后那次不是同一条件（另有一次 play 重叠）。只认 ① 的 AA 同会话对照。
3. **玩家/敌人近景"脚下有影子"的图没单独拍** —— `m5_final_combat.png` 里敌人被升级文字和爆炸特效盖住。环境侧已由 `capture-101444.png` 独立复验。
4. **逐项确认每类单位的 `ShadowCastingMode` 前后值做不到** —— 没有任何代码设置过它（默认就是 `On`），给的空表不如不给。
5. **`m5_before_shadow.png` 抓在选种子界面（Prepare）** ⇒ 只能看地面明暗，不能当 fps 对照组。
6. **`SeedfallVfx.shader` 故意不加 `ShadowCaster`** —— 特效不投影是对的，它的"照亮"靠光池。
7. **未加 CAS / 锐化 Renderer Feature** —— 避免与现有 `ScreenOutlineFeature` 的 pass 顺序打架。
8. **材质计数趋势未做长时间验证** —— 需要 profiler 会话。
