using UnityEngine;
using UnityEditor;
using NDMFMerge.Runtime;

namespace NDMFMerge.Editor
{
    [CustomEditor(typeof(CVRMergeArmature))]
    public class CVRMergeArmatureEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var merger = (CVRMergeArmature)target;
            
            EditorGUILayout.HelpBox(
                "This component merges an outfit armature into the base avatar armature at build time. " +
                "Place this on the root of your outfit's armature (e.g., 'Outfit_Armature').",
                MessageType.Info);
            
            EditorGUILayout.Space();
            
            DrawDefaultInspector();
            
            EditorGUILayout.Space();
            
            // Validation
            if (merger.targetArmature != null && merger.targetArmature.IsChildOf(merger.transform))
            {
                EditorGUILayout.HelpBox(
                    "Warning: Target armature should not be a child of this object!",
                    MessageType.Warning);
            }
            
            // Preview info
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            var smrs = merger.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            EditorGUILayout.LabelField($"SkinnedMeshRenderers to process: {smrs.Length}");
            
            var bones = new System.Collections.Generic.HashSet<Transform>();
            foreach (var smr in smrs)
            {
                if (smr.bones != null)
                {
                    foreach (var bone in smr.bones)
                    {
                        if (bone != null) bones.Add(bone);
                    }
                }
            }
            EditorGUILayout.LabelField($"Unique bones found: {bones.Count}");
        }
    }
}
