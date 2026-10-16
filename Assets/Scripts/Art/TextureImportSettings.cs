using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 地表 / 墙体贴图的导入契约。
    ///
    /// ★ wrapMode 必须是 Repeat ★
    /// 默认是 Clamp——一张 256 的图铺在 40m 的地面上，超出第一张之后边缘像素被拉成条纹，
    /// 看起来像地面被"刷"了一片，而不是平铺。这是贴图导入最容易漏的一步。
    ///
    /// 生成脚本：Tools/AssetPipeline/gen_textures.py（可复现，见 manifest.json）
    /// </summary>
    public class TextureImportSettings : AssetPostprocessor
    {
        private const string TexRoot = "Assets/Resources/Art/Textures/";

        /// <summary>
        /// v96：需要**平铺**的 UI 贴图（目前只有卷轴纸纹）。
        ///
        /// ★ 这里必须按文件名精确匹配，不能用目录 ★
        /// `Art/UI/` 下还有 57 个图标，它们是按 1:1 绘制的：
        /// 给它们套上 Repeat + mipmap 会改变采样（缩放时取到邻格、远景被糊），
        /// 把已经验收过的图标弄坏。只有明确要平铺的那几张才该走这条规则。
        /// </summary>
        private const string TileUiFolder = "Assets/Resources/Art/UI/";
        private const string TileUiName = "ui_scroll_paper";

        private void OnPreprocessTexture()
        {
            bool isTexRoot = assetPath != null && assetPath.StartsWith(TexRoot);
            bool isTileUi = assetPath != null && assetPath.StartsWith(TileUiFolder)
                            && System.IO.Path.GetFileNameWithoutExtension(assetPath) == TileUiName;
            if (!isTexRoot && !isTileUi) return;

            TextureImporter ti = assetImporter as TextureImporter;
            if (ti == null) return;

            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Repeat;      // 平铺
            ti.filterMode = FilterMode.Bilinear;
            ti.anisoLevel = 4;                         // 斜视角下不糊
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.sRGBTexture = true;

            if (isTileUi)
            {
                // UI 纸纹是屏幕空间近 1:1 平铺的，mipmap 用不上；
                // 关掉还能避免小尺寸下被采到更粗的 mip 而"发灰"。
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 256;
            }
            else
            {
                ti.mipmapEnabled = true;               // 地表/墙体：远处不闪
                ti.maxTextureSize = 512;
            }
        }
    }
}
