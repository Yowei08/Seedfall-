# Tools/mesh_gen —— v179 三种攻击特效的程序化网格

三种攻击（锄头挥击 / 水壶水柱 / 肥料爆炸）的**实体颗粒**网格。
共同语言：**低面数、棱面、硬边、鲜明色块**，全部 flat shading，**都不是 2D billboard**。

## 一句话重跑

```powershell
cd D:\Unity\project\Seedfall守园者
& C:\Users\18462\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe Tools\mesh_gen\gen_fx_meshes.py
& C:\Users\18462\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe Tools\mesh_gen\verify_counts.py
```

`gen_*` 退出码恒 0；`verify_counts.py` **退出码 0 = 全部 PASS**，非 0 可直接进 CI。

## 三个脚本

| 文件 | 作用 |
|---|---|
| `gen_fx_meshes.py` | 生成四族网格的 OBJ 预览 + `out/fx_mesh_manifest.json`（顶点/三角数、朝外率） |
| `verify_counts.py` | **从 `WorldView.cs` 读回真实表**重建一遍，与 OBJ 逐项比对；外加两条静态约束检查 |
| `fx_signature_audit.py` | 12 条 `FxSpec` 签名的**两两比对**（从 `PluginFx.cs` 实解析，不手抄） |

## ★ 为什么运行时的网格构造不在这个目录 ★

`Tools/` 在 `Assets/` **之外**。Unity 只编译 `Assets/` 与 `Packages/`，
而 `Seedfall.Runtime.csproj` 用的是**显式** `<Compile Include="Assets\...">`（不是通配）
⇒ 放在 `Tools/` 里的 `.cs` **不会被任何一方编译**，`WorldView` 也就引用不到。

所以分工是：

```
WorldView.cs  ........ 运行时构造（唯一能被编译的位置）
gen_fx_meshes.py ..... 同一套拓扑的离线权威副本（出 OBJ 给人看）
verify_counts.py ..... 把上面两边**钉在一起**：C# 改了、这里没跟上就 FAIL
```

## 四族网格

| 族 | 形态 | 三角 / 顶点 | 用在哪 |
|---|---|---|---|
| `shard{0..5}` | 八面体 + 各轴拉伸 + 确定性抖动 | 8 / 24 | 挥击碎片、爆炸碎片 |
| `frustum6` | 六棱台（后端 0.72 / 前端 1.0） | 24 / 72 | 水柱的每一段 |
| `icoshell1` | 二十面体**一级细分**球壳 | 80 / 240 | 爆炸的球壳 + 白闪 |
| `disc18` | 贴地圆盘（法线 +Y） | 18 / 54 | 地面湿痕 |

全部 flat shading：每个三角形**独占 3 个顶点**，再由 `RecalculateNormals`
得到**逐面法线** ⇒ 棱面、硬边、没有平滑过渡。

## `verify_counts.py` 的 20 项检查（当前 20/20 PASS）

- 四族的**顶点/三角数**：C# 重建 == OBJ == 规格期望
- 绕序正确性：所有面**朝外率 100%**（内翻的面会让网格看起来发黑/破洞）
- 湿痕圆盘法线朝 **+Y**（贴地才看得见）
- 静态约束①：三种攻击的代码路径里**没有 `new Material(`**（全走 `VfxMatCached`）
- 静态约束②：三种攻击里没有 `Quad` / `Sprite` / billboard

## 素材寿命与光的参数（写在 `WorldView.cs`）

| 攻击 | 颗粒数 | 光 |
|---|---|---|
| 锄头挥击 | `clamp(9 + Rings - Sparks/2, 8, 12)` → 空槽 **9** 块 | `IlluminateRamp` 0.15s / 3 采样 / 强度 2.2→0.33 |
| 水壶水柱 | `clamp(4 + min(2,Rings), 4, 6)` → 空槽 **4** 段 | `IlluminateBeam` 专用光束灯（强度 2.6）＋ 落点 `IlluminateRamp` |
| 肥料爆炸 | 球壳 80 面 ＋ `clamp(24 + Sparks, 20, 30)` → 空槽 **24** 块 | 1 帧白闪 ＋ `IlluminateRamp` 0.10s ＋ 原有 0.32s 暖光 |

## 与材质缓存的契约

`VfxMatCached` 的共享材质**永不逐实例改写**，所以这些颗粒的"淡出"一律靠**缩放**
（`MeshBit.S0 → S1`），不碰 alpha；需要白闪就**换另一份已缓存的材质**
（`VfxMatCached(Color.white, Color.white)`），而不是去改当前那份。

`MeshBit` 因此**不持有材质**，`TickFxMeshes` 只 `Object.Destroy(Go)`，**绝不** `Destroy(Mat)`
—— 这是第⑤轮那个 ~60 份/秒 泄漏不会再回来的原因。
