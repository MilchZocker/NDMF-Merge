using UnityEngine;
using UnityEditor;
using NDMFMerge.Runtime;
using System.Collections.Generic;
using UnityEditorInternal;

namespace NDMFMerge.Editor
{
    [CustomEditor(typeof(CVRMergeArmature))]
    public class CVRMergeArmatureEditor : UnityEditor.Editor
    {
        private ReorderableList outfitsList;
        private bool showBoneConflicts = false;
        private bool showAdvancedSettings = false;
        private Vector2 conflictScrollPos;
        
        private void OnEnable()
        {
            outfitsList = new ReorderableList(serializedObject, serializedObject.FindProperty("outfitsToMerge"), true, true, true, true);
            
            outfitsList.drawHeaderCallback = (Rect rect) => {
                EditorGUI.LabelField(rect, "Outfits to Merge");
            };
            
            outfitsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {
                var element = outfitsList.serializedProperty.GetArrayElementAtIndex(index);
                rect.y += 2;
                
                float lineHeight = EditorGUIUtility.singleLineHeight;
                float spacing = 2;
                
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), 
                    element.FindPropertyRelative("outfit"), GUIContent.none);
                
                rect.y += lineHeight + spacing;
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), 
                    element.FindPropertyRelative("prefix"));
                
                rect.y += lineHeight + spacing;
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), 
                    element.FindPropertyRelative("suffix"));
                
                rect.y += lineHeight + spacing;
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), 
                    element.FindPropertyRelative("uniqueBonePrefix"));
                
                rect.y += lineHeight + spacing;
                EditorGUI.PropertyField(new Rect(rect.x, rect.y, rect.width, lineHeight), 
                    element.FindPropertyRelative("meshPrefix"));
            };
            
            outfitsList.elementHeightCallback = (int index) => {
                return (EditorGUIUtility.singleLineHeight + 2) * 5 + 4;
            };
        }
        
        public override void OnInspectorGUI()
        {
            var merger = (CVRMergeArmature)target;
            serializedObject.Update();
            
            EditorGUILayout.HelpBox(
                "Add this component to your avatar (with CVRAvatar component). " +
                "Add outfits to the list below and they will be merged during NDMF processing.",
                MessageType.Info);
            
            EditorGUILayout.Space();
            
            // Validate CVRAvatar
            var cvrAvatar = merger.GetCVRAvatar();
            if (cvrAvatar == null)
            {
                EditorGUILayout.HelpBox(
                    "✗ This GameObject must have a CVRAvatar component!",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "✓ CVRAvatar found",
                    MessageType.None);
            }
            
            EditorGUILayout.Space();
            
            // Outfits list
            outfitsList.DoLayoutList();
            
            EditorGUILayout.Space();
            
            // Bone Conflicts
            DrawBoneConflictsSection(merger);
            
            EditorGUILayout.Space();
            
            // Advanced Settings
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawAdvancedSettings(merger);
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space();
            
            DrawPreviewInfo(merger);
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void DrawBoneConflictsSection(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Bone Conflict Detection", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultBoneConflictResolution"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("conflictThreshold"));
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Detect Conflicts in All Outfits", GUILayout.Height(30)))
            {
                DetectBoneConflicts(merger);
            }
            if (merger.boneConflicts.Count > 0 && GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(30)))
            {
                merger.boneConflicts.Clear();
                EditorUtility.SetDirty(merger);
            }
            EditorGUILayout.EndHorizontal();
            
            if (merger.boneConflicts.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    $"Found {merger.boneConflicts.Count} bone conflicts across all outfits. Choose resolution for each:",
                    MessageType.Warning);
                
                showBoneConflicts = EditorGUILayout.Foldout(showBoneConflicts, $"Conflicts ({merger.boneConflicts.Count})", true);
                
                if (showBoneConflicts)
                {
                    conflictScrollPos = EditorGUILayout.BeginScrollView(conflictScrollPos, GUILayout.MaxHeight(400));
                    
                    for (int i = 0; i < merger.boneConflicts.Count; i++)
                    {
                        var conflict = merger.boneConflicts[i];
                        
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        
                        EditorGUILayout.LabelField($"[{conflict.outfitName}] Bone: {conflict.sourceBone.name}", EditorStyles.boldLabel);
                        
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"Position Δ: {conflict.positionDifference.magnitude:F4}m");
                        EditorGUILayout.LabelField($"Rotation Δ: {conflict.rotationDifference:F2}°");
                        EditorGUILayout.LabelField($"Scale Δ: {conflict.scaleDifference.magnitude:F4}");
                        EditorGUI.indentLevel--;
                        
                        conflict.resolution = (BoneConflictResolution)EditorGUILayout.EnumPopup("Resolution", conflict.resolution);
                        
                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(2);
                    }
                    
                    EditorGUILayout.EndScrollView();
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("All: Still Merge"))
                    {
                        foreach (var conflict in merger.boneConflicts)
                            conflict.resolution = BoneConflictResolution.StillMerge;
                        EditorUtility.SetDirty(merger);
                    }
                    if (GUILayout.Button("All: Rename"))
                    {
                        foreach (var conflict in merger.boneConflicts)
                            conflict.resolution = BoneConflictResolution.Rename;
                        EditorUtility.SetDirty(merger);
                    }
                    if (GUILayout.Button("All: Don't Merge"))
                    {
                        foreach (var conflict in merger.boneConflicts)
                            conflict.resolution = BoneConflictResolution.DontMerge;
                        EditorUtility.SetDirty(merger);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
        }
        
        private void DrawAdvancedSettings(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Exclusions", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedTransforms"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedNamePatterns"), true);
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Component Merging", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("lockParentScale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeDynamicBones"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeMagicaCloth"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("CVR Component Merging", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAdvancedAvatarSetup"));
            
            if (merger.mergeAdvancedAvatarSetup)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("advancedSettingsPrefix"));
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAdvancedPointerTrigger"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeParameterStream"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAnimatorDriver"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Animator Merging", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAnimator"));
        }
        
        private void DrawPreviewInfo(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            
            int totalOutfits = 0;
            int totalMeshes = 0;
            int totalBones = 0;
            
            foreach (var outfit in merger.outfitsToMerge)
            {
                if (outfit.outfit == null) continue;
                
                totalOutfits++;
                var smrs = outfit.outfit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                totalMeshes += smrs.Length;
                
                var bones = new HashSet<Transform>();
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
                totalBones += bones.Count;
            }
            
            EditorGUILayout.LabelField($"Outfits: {totalOutfits}");
            EditorGUILayout.LabelField($"Total SkinnedMeshRenderers: {totalMeshes}");
            EditorGUILayout.LabelField($"Total unique bones: {totalBones}");
        }
        
        private void DetectBoneConflicts(CVRMergeArmature merger)
        {
            merger.boneConflicts.Clear();
            
            var cvrAvatar = merger.GetCVRAvatar();
            if (cvrAvatar == null)
            {
                EditorUtility.DisplayDialog("Error", "No CVRAvatar component found on this GameObject.", "OK");
                return;
            }
            
            Transform targetArmature = FindArmatureFromCVRAvatar(cvrAvatar);
            if (targetArmature == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find armature in CVRAvatar.", "OK");
                return;
            }
            
            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;
                
                // Get bones used by meshes in this outfit
                var usedBones = GetBonesUsedByMeshes(outfitEntry.outfit.transform);
                Debug.Log($"[Conflict Detection] Found {usedBones.Count} used bones in {outfitEntry.outfit.name}");
                
                DetectConflictsRecursive(merger, outfitEntry, outfitEntry.outfit.transform, targetArmature, usedBones);
            }
            
            EditorUtility.SetDirty(merger);
            
            if (merger.boneConflicts.Count == 0)
            {
                EditorUtility.DisplayDialog("Success", "No bone conflicts detected in used bones!", "OK");
            }
            else
            {
                Debug.LogWarning($"Found {merger.boneConflicts.Count} conflicts in used bones");
            }
        }
        
        private HashSet<Transform> GetBonesUsedByMeshes(Transform root)
        {
            var usedBones = new HashSet<Transform>();
            var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            
            foreach (var smr in skinnedMeshes)
            {
                if (smr.bones != null)
                {
                    foreach (var bone in smr.bones)
                    {
                        if (bone != null)
                        {
                            usedBones.Add(bone);
                            
                            // Add all parents up to root
                            Transform current = bone.parent;
                            while (current != null && current != root)
                            {
                                usedBones.Add(current);
                                current = current.parent;
                            }
                        }
                    }
                }
                
                if (smr.rootBone != null)
                {
                    usedBones.Add(smr.rootBone);
                }
            }
            
            return usedBones;
        }
        
        private void DetectConflictsRecursive(CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform source, Transform target, HashSet<Transform> usedBones)
        {
            if (merger.IsExcluded(source)) return;
            
            // Skip if this bone is not used by any mesh
            if (!usedBones.Contains(source))
            {
                // Still check children
                foreach (Transform child in source)
                {
                    DetectConflictsRecursive(merger, outfitEntry, child, target, usedBones);
                }
                return;
            }
            
            string boneName = source.name;
            
            if (!string.IsNullOrEmpty(outfitEntry.prefix) && boneName.StartsWith(outfitEntry.prefix))
                boneName = boneName.Substring(outfitEntry.prefix.Length);
            if (!string.IsNullOrEmpty(outfitEntry.suffix) && boneName.EndsWith(outfitEntry.suffix))
                boneName = boneName.Substring(0, boneName.Length - outfitEntry.suffix.Length);
            
            Transform targetBone = FindBoneByName(target, boneName);
            
            if (targetBone != null)
            {
                Vector3 posDiff = source.position - targetBone.position;
                float rotDiff = Quaternion.Angle(source.rotation, targetBone.rotation);
                Vector3 scaleDiff = source.lossyScale - targetBone.lossyScale;
                
                bool hasConflict = posDiff.magnitude > merger.conflictThreshold || 
                                  rotDiff > merger.conflictThreshold * 57.3f || 
                                  scaleDiff.magnitude > merger.conflictThreshold;
                
                if (hasConflict)
                {
                    merger.boneConflicts.Add(new BoneConflictEntry
                    {
                        outfitName = outfitEntry.outfit.name,
                        sourceBone = source,
                        targetBone = targetBone,
                        resolution = merger.defaultBoneConflictResolution,
                        positionDifference = posDiff,
                        rotationDifference = rotDiff,
                        scaleDifference = scaleDiff
                    });
                }
            }
            
            foreach (Transform child in source)
            {
                DetectConflictsRecursive(merger, outfitEntry, child, target, usedBones);
            }
        }
        
        private Transform FindArmatureFromCVRAvatar(Component cvrAvatar)
        {
            if (cvrAvatar == null) return null;
            
            var animator = cvrAvatar.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                var rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (rootBone != null && rootBone.parent != null)
                {
                    return rootBone.parent;
                }
            }
            
            var root = cvrAvatar.transform;
            var names = new[] { "Armature", "armature", "Skeleton", "skeleton", "Root", "root" };
            
            foreach (var name in names)
            {
                var found = root.Find(name);
                if (found != null) return found;
            }
            
            foreach (Transform child in root)
            {
                if (child.childCount >= 3 && !child.GetComponent<SkinnedMeshRenderer>())
                    return child;
            }
            
            return null;
        }
        
        private Transform FindBoneByName(Transform root, string name)
        {
            if (root.name == name) return root;
            
            foreach (Transform child in root)
            {
                var result = FindBoneByName(child, name);
                if (result != null) return result;
            }
            
            return null;
        }
    }
}
