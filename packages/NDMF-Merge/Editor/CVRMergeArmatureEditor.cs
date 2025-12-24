using UnityEngine;
using UnityEditor;
using NDMFMerge.Runtime;
using System.Collections.Generic;
using System.Linq;

namespace NDMFMerge.Editor
{
    [CustomEditor(typeof(CVRMergeArmature))]
    public class CVRMergeArmatureEditor : UnityEditor.Editor
    {
        private bool showBoneConflicts = false;
        private bool showAdvancedSettings = false;
        private bool showPreviewStats = false;
        private bool showGlobalBoneMatching = false;
        private bool showAnimatorImprovements = false;

        private Vector2 conflictScrollPos;

        public override void OnInspectorGUI()
        {
            var merger = (CVRMergeArmature)target;
            serializedObject.Update();

            // --- Status ---
            if (merger.GetCVRAvatar() == null)
            {
                EditorGUILayout.HelpBox("Missing CVRAvatar Component! This script requires an avatar root.", MessageType.Error);
            }

            // --- Main List ---
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Outfits to Merge", EditorStyles.boldLabel);
            DrawOutfitsList(merger);

            // --- Global Enhancements ---
            EditorGUILayout.Space(10);
            DrawGlobalBoneMatchingSection();
            EditorGUILayout.Space(10);
            DrawAnimatorImprovementsSection();

            // --- Sections ---
            EditorGUILayout.Space(10);
            DrawBoneConflictsSection(merger);
            EditorGUILayout.Space(10);
            DrawAdvancedSettings(merger);
            EditorGUILayout.Space(10);
            DrawPreviewInfo(merger);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawOutfitsList(CVRMergeArmature merger)
        {
            var outfitsProp = serializedObject.FindProperty("outfitsToMerge");
            for (int i = 0; i < outfitsProp.arraySize; i++)
            {
                var element = outfitsProp.GetArrayElementAtIndex(i);
                var outfitName = (element.FindPropertyRelative("outfit").objectReferenceValue as GameObject)?.name ?? "Empty Slot";

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                // --- Header Row with Foldout ---
                EditorGUILayout.BeginHorizontal();
                var isExpandedProp = element.FindPropertyRelative("isExpanded");
                isExpandedProp.boolValue = EditorGUILayout.Foldout(isExpandedProp.boolValue, outfitName, true, EditorStyles.foldoutHeader);

                // Delete Button
                if (GUILayout.Button("X", GUILayout.Width(22), GUILayout.Height(22)))
                {
                    outfitsProp.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();

                // --- Expanded Content ---
                if (isExpandedProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("outfit"));

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Naming Rules", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("prefix"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("suffix"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("uniqueBonePrefix"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("meshPrefix"));

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Per-Outfit Fixes", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("boundsFixMode"), new GUIContent("Bounds Fix"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("syncAnchorOverrides"), new GUIContent("Sync Anchor Overrides"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("forceScaleToOne"), new GUIContent("Force Scale (1,1,1)"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("removeUnusedBones"), new GUIContent("Remove Unused Bones"));

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Per-Outfit Bone Mappings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("boneNameMappings"), true);

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Animator Options", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("mergeAnimator"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("mergeAnimatorIncludingAAS"));
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }

            // --- Add Button ---
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Outfit Slot"))
            {
                outfitsProp.arraySize++;
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawGlobalBoneMatchingSection()
        {
            showGlobalBoneMatching = EditorGUILayout.Foldout(showGlobalBoneMatching, "Global Bone Matching", true);
            if (!showGlobalBoneMatching) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableFuzzyBoneMatching"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("globalBoneNameMappings"), true);

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLevenshteinBoneMatching"));
            if (serializedObject.FindProperty("enableLevenshteinBoneMatching").boolValue)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("maxLevenshteinDistance"));
            }
            EditorGUI.indentLevel--;
        }

        private void DrawAnimatorImprovementsSection()
        {
            showAnimatorImprovements = EditorGUILayout.Foldout(showAnimatorImprovements, "Animator Merging Improvements", true);
            if (!showAnimatorImprovements) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animatorRewritePaths"), new GUIContent("Rewrite Clip Paths"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animatorMergeAvatarMasks"), new GUIContent("Merge Avatar Masks (Union)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("animatorCombineLayersByName"), new GUIContent("Combine Layers By Name"));
            EditorGUI.indentLevel--;
        }

        private void DrawBoneConflictsSection(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Bone Mismatch & Conflict Detection", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Detection Tolerances", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("positionThreshold"), new GUIContent("Pos Tolerance (m)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationThreshold"), new GUIContent("Rot Tolerance (deg)"));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("detectScaleConflicts"), new GUIContent("Check Scale?"));
            if (merger.detectScaleConflicts)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("scaleThreshold"), GUIContent.none);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultBoneConflictResolution"), new GUIContent("Default Action"));

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Detect Mismatches", GUILayout.Height(24)))
            {
                DetectBoneConflicts(merger);
            }
            if (merger.boneConflicts.Count > 0 && GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(24)))
            {
                merger.boneConflicts.Clear();
                EditorUtility.SetDirty(merger);
            }
            EditorGUILayout.EndHorizontal();

            if (merger.boneConflicts.Count > 0)
            {
                EditorGUILayout.Space(5);
                GUI.backgroundColor = new Color(1f, 0.9f, 0.8f);
                EditorGUILayout.HelpBox($"Found {merger.boneConflicts.Count} mismatches.", MessageType.Warning);
                GUI.backgroundColor = Color.white;

                showBoneConflicts = EditorGUILayout.Foldout(showBoneConflicts, $"Resolve Conflicts ({merger.boneConflicts.Count})", true);

                if (showBoneConflicts)
                {
                    DrawBulkActions(merger);
                    EditorGUILayout.Space(2);

                    conflictScrollPos = EditorGUILayout.BeginScrollView(conflictScrollPos, GUILayout.MaxHeight(350));
                    var conflictListProp = serializedObject.FindProperty("boneConflicts");

                    for (int i = 0; i < conflictListProp.arraySize; i++)
                    {
                        var entryProp = conflictListProp.GetArrayElementAtIndex(i);
                        var entry = merger.boneConflicts[i];

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"{entry.outfitName}", EditorStyles.boldLabel, GUILayout.Width(120));
                        EditorGUILayout.LabelField($"→ {entry.sourceBone.name}");

                        if (merger.detectScaleConflicts && entry.scaleDifference.magnitude > merger.scaleThreshold)
                        {
                            GUI.color = new Color(1f, 0.6f, 0.6f);
                            GUILayout.Label("SCALE", EditorStyles.miniLabel, GUILayout.Width(40));
                            GUI.color = Color.white;
                        }
                        EditorGUILayout.EndHorizontal();

                        EditorGUI.indentLevel++;
                        string stats = $"ΔPos: {entry.positionDifference.magnitude:F4} | ΔRot: {entry.rotationDifference:F1}°";
                        if (merger.detectScaleConflicts) stats += $" | ΔScl: {entry.scaleDifference.magnitude:F3}";
                        EditorGUILayout.LabelField(stats, EditorStyles.miniLabel);
                        EditorGUI.indentLevel--;

                        var resProp = entryProp.FindPropertyRelative("resolution");
                        EditorGUILayout.PropertyField(resProp, new GUIContent("Action"));

                        BoneConflictResolution currentRes = (BoneConflictResolution)resProp.enumValueIndex;

                        if (currentRes == BoneConflictResolution.MergeIntoSelected)
                        {
                            var targetProp = entryProp.FindPropertyRelative("customTargetBone");
                            if (targetProp.objectReferenceValue == null) GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
                            EditorGUILayout.PropertyField(targetProp, new GUIContent("Target Bone"));
                            GUI.backgroundColor = Color.white;
                        }
                        else if (currentRes == BoneConflictResolution.ConstraintToTarget)
                        {
                            EditorGUILayout.HelpBox("Bone will remain separate and follow target via Constraint.", MessageType.Info);
                        }

                        EditorGUILayout.EndVertical();
                    }

                    EditorGUILayout.EndScrollView();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBulkActions(CVRMergeArmature merger)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Set All:", EditorStyles.miniLabel, GUILayout.Width(45));
            if (GUILayout.Button("Constraint", EditorStyles.miniButtonLeft)) SetAllResolutions(merger, BoneConflictResolution.ConstraintToTarget);
            if (GUILayout.Button("Force", EditorStyles.miniButtonMid)) SetAllResolutions(merger, BoneConflictResolution.StillMerge);
            if (GUILayout.Button("Rename", EditorStyles.miniButtonRight)) SetAllResolutions(merger, BoneConflictResolution.Rename);
            EditorGUILayout.EndHorizontal();
        }

        private void SetAllResolutions(CVRMergeArmature merger, BoneConflictResolution res)
        {
            foreach (var c in merger.boneConflicts) c.resolution = res;
            EditorUtility.SetDirty(merger);
        }

        private void DrawAdvancedSettings(CVRMergeArmature merger)
        {
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);
            if (!showAdvancedSettings) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Exclusions", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedTransforms"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedNamePatterns"), true);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Safety & Components", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("preventScaleDistortion"), new GUIContent("Lock Parent Scale"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeDynamicBones"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeMagicaCloth"));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("CVR Integrations", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAdvancedAvatarSetup"));

            if (merger.mergeAdvancedAvatarSetup)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("generateAASControllerAtEnd"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("advancedSettingsPrefix"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAdvancedPointerTrigger"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeParameterStream"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAnimatorDriver"));

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Global Overrides", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAnimator"), new GUIContent("Enable Animator Merging"));

            EditorGUI.indentLevel--;
        }

        private void DrawPreviewInfo(CVRMergeArmature merger)
        {
            showPreviewStats = EditorGUILayout.Foldout(showPreviewStats, "Preview Stats", true);
            if (!showPreviewStats) return;

            int totalOutfits = 0;
            int totalMeshes = 0;
            int uniqueBonesToAdd = 0;
            int bonesToMerge = 0;
            int bonesToConstraint = 0;

            var targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar != null)
            {
                var targetArmature = FindArmatureFromCVRAvatar(targetCVRAvatar);
                if (targetArmature != null)
                {
                    foreach (var outfit in merger.outfitsToMerge)
                    {
                        if (outfit.outfit == null) continue;
                        totalOutfits++;
                        totalMeshes += outfit.outfit.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;

                        var usedBones = GetBonesUsedByMeshes(outfit.outfit.transform);
                        var conflictLookup = merger.boneConflicts.Where(c => c.outfitName == outfit.outfit.name).ToDictionary(c => c.sourceBone);

                        foreach (var bone in usedBones)
                        {
                            if (conflictLookup.TryGetValue(bone, out var conflict))
                            {
                                if (conflict.resolution == BoneConflictResolution.StillMerge || conflict.resolution == BoneConflictResolution.MergeIntoSelected) bonesToMerge++;
                                else if (conflict.resolution == BoneConflictResolution.ConstraintToTarget) bonesToConstraint++;
                                else uniqueBonesToAdd++;
                            }
                            else
                            {
                                string boneName = bone.name;
                                if (FindBoneByName(targetArmature, boneName) != null) bonesToMerge++;
                                else uniqueBonesToAdd++;
                            }
                        }
                    }
                }
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Total Outfits:", $"{totalOutfits}");
            EditorGUILayout.LabelField("Skinned Meshes to Add:", $"{totalMeshes}");
            EditorGUILayout.LabelField("Bones to Merge:", $"{bonesToMerge}");
            EditorGUILayout.LabelField("Unique Bones to Add:", $"{uniqueBonesToAdd}");
            EditorGUILayout.LabelField("Bones to Constrain:", $"{bonesToConstraint}");
            EditorGUI.indentLevel--;
        }

        // =================================================================================
        // CONFLICT DETECTION LOGIC (unchanged)
        // =================================================================================

        private void DetectBoneConflicts(CVRMergeArmature merger)
        {
            merger.boneConflicts.Clear();
            var cvrAvatar = merger.GetCVRAvatar();

            if (cvrAvatar == null)
            {
                EditorUtility.DisplayDialog("Error", "No CVRAvatar found on root.", "OK");
                return;
            }

            Transform targetArmature = FindArmatureFromCVRAvatar(cvrAvatar);
            if (targetArmature == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not locate Armature in CVRAvatar.", "OK");
                return;
            }

            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;
                var usedBones = GetBonesUsedByMeshes(outfitEntry.outfit.transform);
                DetectConflictsRecursive(merger, outfitEntry, outfitEntry.outfit.transform, targetArmature, usedBones);
            }

            EditorUtility.SetDirty(merger);
        }

        private HashSet<Transform> GetBonesUsedByMeshes(Transform root)
        {
            var usedBones = new HashSet<Transform>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.bones != null)
                    foreach (var b in smr.bones) if (b) usedBones.Add(b);
                if (smr.rootBone) usedBones.Add(smr.rootBone);
            }
            return usedBones;
        }

        private void DetectConflictsRecursive(CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform source, Transform target, HashSet<Transform> usedBones)
        {
            if (merger.IsExcluded(source)) return;

            if (usedBones.Contains(source))
            {
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

                    bool hasConflict = posDiff.magnitude > merger.positionThreshold ||
                                      rotDiff > merger.rotationThreshold;

                    if (merger.detectScaleConflicts && scaleDiff.magnitude > merger.scaleThreshold)
                        hasConflict = true;

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
            }

            foreach (Transform child in source)
                DetectConflictsRecursive(merger, outfitEntry, child, target, usedBones);
        }

        private Transform FindArmatureFromCVRAvatar(Component cvrAvatar)
        {
            if (cvrAvatar == null) return null;
            var animator = cvrAvatar.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                var root = animator.GetBoneTransform(HumanBodyBones.Hips);
                return root ? root.parent : null;
            }
            var t = cvrAvatar.transform;
            foreach (var n in new[] { "Armature", "Skeleton", "Root" })
            {
                var f = t.Find(n);
                if (f) return f;
            }
            return t.Find("Armature");
        }

        private Transform FindBoneByName(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var r = FindBoneByName(child, name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
