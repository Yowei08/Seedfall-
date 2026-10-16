using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// UI 贴图的导入契约。
    ///
    /// 九宫格拉伸对导入设置有硬要求：
    ///   · wrapMode 必须是 Clamp——Repeat 会让边缘像素在拉伸时"绕回来"，出现一圈鬼影
    ///   · 必须关闭 mipmap——UI 按 1:1 绘制，mipmap 只会让边缘发糊
    ///   · 必须是 Point/Bilinear 而非三线性，且不压缩——压缩会在圆角和墨线上产生色块
    ///
    /// 这些设置在 Inspector 里很容易漏，所以放进导入流水线自动生效。
    /// </summary>
    public class UiTextureImport : AssetPostprocessor
    {
        private const string UiRoot = "Assets/Resources/Art/UI/";

        private void OnPreprocessTexture()
        {
            if (assetPath == null || !assetPath.StartsWith(UiRoot)) return;
            TextureImporter ti = assetImporter as TextureImporter;
            if (ti == null) return;

            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;         // ★ 九宫格必须 Clamp
            ti.mipmapEnabled = false;                    // ★ UI 不需要 mip
            ti.filterMode = FilterMode.Bilinear;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 256;

            TextureImporterPlatformSettings ps = ti.GetDefaultPlatformTextureSettings();
            ps.textureCompression = TextureImporterCompression.Uncompressed;   // ★ 墨线不能被压出色块
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
