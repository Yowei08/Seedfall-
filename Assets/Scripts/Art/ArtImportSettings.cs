using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 模型导入契约（程序化美术流水线的一部分）。
    ///
    /// 这个文件记录的是**实测出来的** FBX 边界事实，不是推测。踩过的坑按顺序：
    ///
    /// ① 轴向：Unity 从 FBX 导入后，根节点带 `rotation = 270°绕X`。这**不是冗余**——
    ///    网格是 Z-up 存的，靠它转成 Y-up。早期我误判为"导出器多转了一次"，
    ///    在游戏代码里把 rotation 抹成 identity，结果模型全部倒下。
    ///    所以 `bakeAxisConversion = false`：**保留**这个旋转，不要让 Unity 烘进网格。
    ///
    /// ② 单位：根节点还带 `scale = 100`。这也不是冗余——网格按 1/100 存储，
    ///    乘回去才是米制。同样地，游戏代码不许直接给 localScale 赋值，
    ///    只能调 ModelView.SetScaleFactor（乘在原始缩放上）。
    ///
    /// ③ ★ Plants 是例外 ★
    ///    实测：`Plants/pea_shooter` 的网格包围盒是 (0.684, 0.684, **0.521**)，
    ///    而 `Environment/tree` 是 (0.028, 0.019, **0.042**)、`Characters/player` 是
    ///    (0.007, 0.007, **0.01**)。
    ///    也就是说**植物的网格已经是米制**（0.52 ≈ 建模的 0.59m 植物），
    ///    但 FBX 头里的单位声明照样让 Unity 挂了 scale=100 的根。
    ///    两者叠加 = 100 倍：一个 0.5m 的植物变成 52m，
    ///    再乘上表现倍率就成了 86 米——大到能把相机整个埋进去
    ///    （症状：截图整屏都是土壤色，诊断里出现 `MDL_pea_shooter 尺寸 136×86×136`）。
    ///    这里按目录把这份额外的单位换算压回去，与网格的实际单位对齐。
    ///
    /// 放在 AssetPostprocessor 里而不是靠 Inspector 手点：任何重新导入都会自动生效。
    /// 靠人记着的约定迟早会丢——这个项目已经因为"改了导出参数但没重新导入"浪费过两轮。
    /// </summary>
    public class ArtImportSettings : AssetPostprocessor
    {
        private const string ArtRoot = "Assets/Resources/Art/";
        private const string PlantsRoot = "Assets/Resources/Art/Plants/";

        private void OnPreprocessModel()
        {
            if (assetPath == null || !assetPath.StartsWith(ArtRoot)) return;
            ModelImporter mi = assetImporter as ModelImporter;
            if (mi == null) return;

            // 保留导入器算出的轴向旋转（网格是 Z-up，见上面的 ①）
            mi.bakeAxisConversion = false;
            mi.useFileScale = false;

            // 见上面的 ③：植物网格已是米制，压掉 FBX 单位带来的那 100 倍
            mi.globalScale = assetPath.StartsWith(PlantsRoot) ? 0.01f : 1f;

            mi.importNormals = ModelImporterNormals.Calculate;
            mi.importBlendShapes = false;
            mi.importAnimation = true;         // 后续动画要进来
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        }
    }
}
