#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Seedfall
{
    /// <summary>
    /// 动画片段合并（美术流水线的一环）。
    ///
    /// 问题：Blender 的 FBX 导出器为**每一个被动画的物体**写一个 take，
    /// 于是 player_idle.fbx 进 Unity 后变成 7 条片段：
    ///     Arm_L_player|player_idle、Body_player|player_idle、Head_player|player_idle …
    /// 每条只含那一个部件的曲线。单独播任何一条，都只有一只胳膊在动。
    ///
    /// 做法：导入完成后把同名的 7 条合并成一条**完整** AnimationClip，
    /// 写成独立 .asset（Assets/Resources/Art/AnimMerged/），而不是塞回 FBX 当子资产——
    /// 子资产会在 Library 重建时被冲掉并重复添加。
    ///
    /// 为什么不用骨骼：那需要重做一套 armature + 骨骼绑定；这里用「合并」达到同样效果，
    /// 且不改变已经验证过的木偶层级（部件路径在合并后依然对得上）。
    /// </summary>
    public class AnimationMerge : AssetPostprocessor
    {
        private const string AnimRoot = "Assets/Resources/Art/Anim/";
        private const string MergedRoot = "Assets/Resources/Art/AnimMerged";

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported == null || imported.Length == 0) return;
            bool touched = false;
            for (int i = 0; i < imported.Length; i++)
            {
                string p = imported[i].Replace('\\', '/');
                if (!p.StartsWith(AnimRoot) || !p.EndsWith(".fbx")) continue;
                if (MergeOne(p)) touched = true;
            }
            if (touched) AssetDatabase.Refresh();
        }

        private static bool MergeOne(string fbxPath)
        {
            Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            if (all == null) return false;

            var parts = new List<AnimationClip>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] is AnimationClip c) parts.Add(c);
            if (parts.Count == 0) return false;

            string name = Path.GetFileNameWithoutExtension(fbxPath);   // 例如 player_idle
            bool loop = name.EndsWith("_idle") || name.EndsWith("_walk");

            AnimationClip merged = new AnimationClip { name = name, frameRate = parts[0].frameRate };

            int curves = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(parts[i]);
                for (int b = 0; b < bindings.Length; b++)
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(parts[i], bindings[b]);
                    if (curve == null) continue;
                    // 路径跨片段不冲突（各片段指向不同部件），直接覆盖式合并
                    AnimationUtility.SetEditorCurve(merged, bindings[b], curve);
                    curves++;
                }
            }
            if (curves == 0) return false;

            AnimationClipSettings st = AnimationUtility.GetAnimationClipSettings(parts[0]);
            st.loopTime = loop;
            st.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(merged, st);

            if (!Directory.Exists(MergedRoot)) Directory.CreateDirectory(MergedRoot);
            string outPath = MergedRoot + "/" + name + ".asset";
            AssetDatabase.DeleteAsset(outPath);
            AssetDatabase.CreateAsset(merged, outPath);

            Debug.Log($"[Seedfall] 动画合并：{name} ← {parts.Count} 条片段 / {curves} 条曲线 / 循环 {loop}");
            return true;
        }
    }
}
#endif
