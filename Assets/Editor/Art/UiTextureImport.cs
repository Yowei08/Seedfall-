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

        /// <summary>
        /// ★ v203：光标贴图的额外放行 ★
        ///
        /// `Cursor.SetCursor` 对贴图有两条硬要求（Unity 官方文档原话）：
        ///   "To use a texture, import it with `Read/Write` enabled."
        ///   "it must be in RGBA32 format, have alphaIsTransparency enabled, and have no mip chain."
        /// 而本类对 `Assets/Resources/Art/UI/**` 的统一规则是 **Default + 不可读**（九宫格 UI 的正常选择）
        /// ⇒ 光标图若跟着走统一规则，`SetCursor` **不报错、也不生效**：
        ///   屏幕上还是系统箭头，控制台干干净净。这正是本工程反复吃过的那种"不吭声的坏" ✗
        ///
        /// 所以 `cursor_*` 单独放行两条：
        ///   · isReadable = true   —— 文档要求
        ///   · filterMode = Point  —— 硬边像素画**必须**点采样：光标的实际尺寸由 OS 按 DPI 定，
        ///                            双线性会把它糊成一层灰边（而图标是 1:1 画在 UI 上的，所以图标继续用 Bilinear ✓）
        /// 其余（Clamp / 无 mip / 不压缩 / alphaIsTransparency）与统一规则一致，不重复设 ✓
        /// </summary>
        private static bool IsCursorTexture(string path)
        {
            if (path == null) return false;
            string name = System.IO.Path.GetFileName(path);
            return name.StartsWith("cursor_", System.StringComparison.OrdinalIgnoreCase);
        }

        private void OnPreprocessTexture()
        {
            if (assetPath == null || !assetPath.StartsWith(UiRoot)) return;
            TextureImporter ti = assetImporter as TextureImporter;
            if (ti == null) return;

            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;         // ★ 九宫格必须 Clamp
            ti.mipmapEnabled = false;                    // ★ UI 不需要 mip
            ti.filterMode = IsCursorTexture(assetPath) ? FilterMode.Point : FilterMode.Bilinear;
            ti.alphaIsTransparency = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 256;
            if (IsCursorTexture(assetPath))
            {
                ti.isReadable = true;                    // ★ `Cursor.SetCursor` 的硬要求（见方法注释）
            }

            TextureImporterPlatformSettings ps = ti.GetDefaultPlatformTextureSettings();
            ps.textureCompression = TextureImporterCompression.Uncompressed;   // ★ 墨线不能被压出色块
            ti.SetPlatformTextureSettings(ps);
        }
    }
}
