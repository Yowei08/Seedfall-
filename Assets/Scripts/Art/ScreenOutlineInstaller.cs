#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Seedfall
{
    /// <summary>
    /// 把屏幕空间描边注册进 URP 渲染器（PC / Mobile）。
    ///
    /// 为什么用脚本而不是让人去 Inspector 点：
    /// 这类"管线装配"一旦漏做，表现会**静默降级**——描边不出现，但不报任何错，很难查。
    /// 放进编辑器初始化里自动补齐，谁重新导入工程都不会丢。
    ///
    /// 安全措施：改动前把渲染器资产备份成 .bak，并提供 Remove 以便一键回退
    /// （改坏 URP 资产会让整个画面渲染异常，必须留有退路）。
    /// </summary>
    [InitializeOnLoad]
    public static class ScreenOutlineInstaller
    {
        private const string FeatureName = "SeedfallScreenOutline";

        private static readonly string[] RendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset",
        };

        static ScreenOutlineInstaller()
        {
            EditorApplication.delayCall += () =>
            {
                foreach (string p in RendererPaths) Install(p, false);
            };
        }

        [MenuItem("Seedfall/渲染/安装屏幕空间描边")]
        private static void InstallMenu()
        {
            foreach (string p in RendererPaths) Install(p, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Seedfall/渲染/移除屏幕空间描边")]
        private static void RemoveMenu()
        {
            foreach (string p in RendererPaths) Remove(p);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static bool Install(string path, bool verbose)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data == null)
            {
                if (verbose) Debug.LogWarning("[Seedfall] 找不到渲染器资产：" + path);
                return false;
            }

            var so = new SerializedObject(data);
            SerializedProperty list = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            if (list == null || map == null)
            {
                if (verbose) Debug.LogWarning("[Seedfall] 渲染器资产结构不符，跳过：" + path);
                return false;
            }

            // 已经装过就什么都不做（幂等）
            for (int i = 0; i < list.arraySize; i++)
            {
                Object existing = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (existing is ScreenOutlineFeature) return false;
            }

            try
            {
                // 备份，留退路
                string bak = path + ".bak";
                if (!File.Exists(bak)) File.Copy(path, bak);

                // ★ 顺序很关键 ★
                // 先把子资产写进去并**存盘**，再去读 localId 与写引用列表。
                // 反过来的话（先 AddObjectToAsset 再 ImportAsset）——重导入会从磁盘重新读文件，
                // 而刚加的子资产还没落盘，于是被丢掉，引用列表就写成空。
                var feature = ScriptableObject.CreateInstance<ScreenOutlineFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();

                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
                if (localId == 0)
                {
                    Debug.LogError("[Seedfall] 未能取得子资产 localId，放弃注册：" + path);
                    return false;
                }

                so.Update();
                list = so.FindProperty("m_RendererFeatures");
                map = so.FindProperty("m_RendererFeatureMap");
                if (list == null || map == null) return false;

                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();

                Debug.Log($"[Seedfall] 屏幕空间描边已注册到 {Path.GetFileName(path)}（localId={localId}）");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Seedfall] 注册屏幕空间描边失败：" + e);
                return false;
            }
        }

        public static void Remove(string path)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data == null) return;

            var so = new SerializedObject(data);
            SerializedProperty list = so.FindProperty("m_RendererFeatures");
            SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
            if (list == null || map == null) return;

            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                Object existing = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (!(existing is ScreenOutlineFeature)) continue;

                if (i < map.arraySize) map.DeleteArrayElementAtIndex(i);
                list.DeleteArrayElementAtIndex(i);
                if (existing != null) Object.DestroyImmediate(existing, true);
                Debug.Log("[Seedfall] 已从 " + Path.GetFileName(path) + " 移除屏幕空间描边");
            }
            so.ApplyModifiedProperties();
        }
    }
}
#endif
