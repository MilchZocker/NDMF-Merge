using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
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
        
        // Preview detail foldouts
        private bool showPerOutfitDetails = false;
        private bool showComponentDetails = false;
        private bool showAnimatorDetails = false;

        private Vector2 conflictScrollPos;
        private Vector2 previewScrollPos;

        // Cache for statistics to prevent lag
        private MergeStatistics cachedStats;
        private bool statsNeedUpdate = true;

        // Color scheme
        private static readonly Color headerColor = new Color(0.8f, 0.9f, 1f, 0.3f);
        private static readonly Color sectionColor = new Color(0.9f, 0.95f, 1f, 0.2f);

        public override void OnInspectorGUI()
        {
            var merger = (CVRMergeArmature)target;
            serializedObject.Update();

            // --- Status Banner ---
            if (merger.GetCVRAvatar() == null)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox("⚠ Missing CVRAvatar Component! This script requires an avatar root.", MessageType.Error);
                EditorGUILayout.Space(3);
            }

            // --- Main Outfits List ---
            DrawSectionHeader("Outfits to Merge");
            DrawOutfitsList(merger);

            EditorGUILayout.Space(8);

            // --- Global Settings ---
            DrawSectionHeader("Global Settings");
            DrawGlobalBoneMatchingSection();
            EditorGUILayout.Space(5);
            DrawAnimatorImprovementsSection();

            EditorGUILayout.Space(8);

            // --- Conflict Detection ---
            DrawSectionHeader("Bone Conflict Detection");
            DrawBoneConflictsSection(merger);

            EditorGUILayout.Space(8);

            // --- Advanced & Preview ---
            DrawAdvancedSettings(merger);
            EditorGUILayout.Space(5);
            DrawPreviewInfo(merger);

            EditorGUILayout.Space(5);
            
            if (serializedObject.ApplyModifiedProperties())
            {
                statsNeedUpdate = true;
            }
        }

        private void DrawSectionHeader(string title)
        {
            GUILayout.Space(2);
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = headerColor;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;
            
            GUILayout.Label(title, EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawOutfitsList(CVRMergeArmature merger)
        {
            var outfitsProp = serializedObject.FindProperty("outfitsToMerge");
            
            for (int i = 0; i < outfitsProp.arraySize; i++)
            {
                var element = outfitsProp.GetArrayElementAtIndex(i);
                var outfitObj = element.FindPropertyRelative("outfit").objectReferenceValue as GameObject;
                var outfitName = outfitObj != null ? outfitObj.name : "Empty Slot";

                EditorGUILayout.Space(3);
                var originalColor = GUI.backgroundColor;
                GUI.backgroundColor = sectionColor;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = originalColor;

                // --- Header Row ---
                EditorGUILayout.BeginHorizontal();
                var isExpandedProp = element.FindPropertyRelative("isExpanded");
                isExpandedProp.boolValue = EditorGUILayout.Foldout(
                    isExpandedProp.boolValue, 
                    $"[{i + 1}] {outfitName}", 
                    true, 
                    EditorStyles.foldoutHeader
                );

                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    outfitsProp.DeleteArrayElementAtIndex(i);
                    statsNeedUpdate = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = originalColor;
                EditorGUILayout.EndHorizontal();

                // --- Expanded Content ---
                if (isExpandedProp.boolValue)
                {
                    EditorGUILayout.Space(3);
                    EditorGUI.indentLevel++;

                    // Outfit Reference
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("outfit"), new GUIContent("Outfit GameObject"));
                    if (EditorGUI.EndChangeCheck()) statsNeedUpdate = true;

                    EditorGUILayout.Space(8);
                    DrawSubsectionLabel("Naming Rules");
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("prefix"), new GUIContent("Bone Prefix to Strip"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("suffix"), new GUIContent("Bone Suffix to Strip"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("uniqueBonePrefix"), new GUIContent("Unique Bone Prefix"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("meshPrefix"), new GUIContent("Mesh Name Prefix"));

                    EditorGUILayout.Space(8);
                    DrawSubsectionLabel("Mesh Fixes");
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("boundsFixMode"), new GUIContent("Bounds Fix Mode"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("syncAnchorOverrides"), new GUIContent("Sync Probe Anchors"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("forceScaleToOne"), new GUIContent("Force Scale (1,1,1)"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("removeUnusedBones"), new GUIContent("Remove Unused Bones"));

                    EditorGUILayout.Space(8);
                    DrawSubsectionLabel("Bone Mappings (Per-Outfit)");
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("boneNameMappings"), new GUIContent("Custom Bone Maps"), true);

                    EditorGUILayout.Space(8);
                    DrawSubsectionLabel("Animator Merging");
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("mergeAnimator"), new GUIContent("Merge Animator (Basic)"));
                    EditorGUILayout.PropertyField(element.FindPropertyRelative("mergeAnimatorIncludingAAS"), new GUIContent("Merge Animator (+AAS)"));
                    if (EditorGUI.EndChangeCheck()) statsNeedUpdate = true;

                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(2);
                }

                EditorGUILayout.EndVertical();
            }

            // --- Add Button ---
            EditorGUILayout.Space(5);
            GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
            if (GUILayout.Button("+ Add Outfit Slot", GUILayout.Height(28)))
            {
                outfitsProp.arraySize++;
                statsNeedUpdate = true;
            }
            GUI.backgroundColor = Color.white;
        }

        private void DrawSubsectionLabel(string label)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11
            };
            EditorGUILayout.LabelField(label, style);
        }

        private void DrawGlobalBoneMatchingSection()
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.95f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            showGlobalBoneMatching = EditorGUILayout.Foldout(showGlobalBoneMatching, "🔗 Global Bone Matching", true, EditorStyles.foldoutHeader);
            
            if (showGlobalBoneMatching)
            {
                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableFuzzyBoneMatching"), new GUIContent("Enable Fuzzy Matching"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("globalBoneNameMappings"), new GUIContent("Global Bone Maps"), true);

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("enableLevenshteinBoneMatching"), new GUIContent("Levenshtein Distance Match"));
                if (serializedObject.FindProperty("enableLevenshteinBoneMatching").boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("maxLevenshteinDistance"), new GUIContent("Max Distance"));
                }
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimatorImprovementsSection()
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.95f, 0.95f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            showAnimatorImprovements = EditorGUILayout.Foldout(showAnimatorImprovements, "🎬 Animator Merging Options", true, EditorStyles.foldoutHeader);
            
            if (showAnimatorImprovements)
            {
                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("animatorRewritePaths"), new GUIContent("Rewrite Clip Paths"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("animatorMergeAvatarMasks"), new GUIContent("Merge Avatar Masks"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("animatorCombineLayersByName"), new GUIContent("Combine Layers By Name"));
                if (EditorGUI.EndChangeCheck()) statsNeedUpdate = true;
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBoneConflictsSection(CVRMergeArmature merger)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.98f, 0.9f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            EditorGUILayout.Space(3);

            // Detection Tolerances
            DrawSubsectionLabel("Detection Tolerances");
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("positionThreshold"), new GUIContent("Position Tolerance (m)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationThreshold"), new GUIContent("Rotation Tolerance (°)"));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("detectScaleConflicts"), new GUIContent("Detect Scale Conflicts"));
            if (merger.detectScaleConflicts)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("scaleThreshold"), GUIContent.none, GUILayout.Width(60));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(5);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultBoneConflictResolution"), new GUIContent("Default Resolution"));

            EditorGUILayout.Space(8);

            // Action Buttons
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.8f, 0.95f, 1f);
            if (GUILayout.Button("🔍 Detect Mismatches", GUILayout.Height(28)))
            {
                DetectBoneConflicts(merger);
                statsNeedUpdate = true;
            }
            GUI.backgroundColor = originalColor;

            if (merger.boneConflicts.Count > 0)
            {
                GUI.backgroundColor = new Color(1f, 0.8f, 0.8f);
                if (GUILayout.Button("Clear", GUILayout.Width(70), GUILayout.Height(28)))
                {
                    merger.boneConflicts.Clear();
                    statsNeedUpdate = true;
                    EditorUtility.SetDirty(merger);
                }
                GUI.backgroundColor = originalColor;
            }
            EditorGUILayout.EndHorizontal();

            // Conflicts List
            if (merger.boneConflicts.Count > 0)
            {
                EditorGUILayout.Space(8);
                
                GUI.backgroundColor = new Color(1f, 0.92f, 0.8f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = originalColor;
                
                EditorGUILayout.HelpBox($"⚠ Found {merger.boneConflicts.Count} bone mismatch(es)", MessageType.Warning);
                
                EditorGUILayout.Space(3);
                showBoneConflicts = EditorGUILayout.Foldout(showBoneConflicts, $"Resolve Conflicts ({merger.boneConflicts.Count})", true, EditorStyles.foldoutHeader);

                if (showBoneConflicts)
                {
                    EditorGUILayout.Space(3);
                    DrawBulkActions(merger);
                    
                    EditorGUILayout.Space(5);
                    conflictScrollPos = EditorGUILayout.BeginScrollView(conflictScrollPos, GUILayout.MaxHeight(400));
                    
                    var conflictListProp = serializedObject.FindProperty("boneConflicts");

                    for (int i = 0; i < conflictListProp.arraySize; i++)
                    {
                        var entryProp = conflictListProp.GetArrayElementAtIndex(i);
                        var entry = merger.boneConflicts[i];

                        GUI.backgroundColor = new Color(1f, 1f, 1f, 0.5f);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        GUI.backgroundColor = originalColor;

                        // Conflict Header
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"[{entry.outfitName}]", EditorStyles.boldLabel, GUILayout.Width(140));
                        EditorGUILayout.LabelField($"→ {entry.sourceBone.name}", GUILayout.MinWidth(100));

                        if (merger.detectScaleConflicts && entry.scaleDifference.magnitude > merger.scaleThreshold)
                        {
                            GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                            GUILayout.Label("SCALE", EditorStyles.miniLabel, GUILayout.Width(45));
                            GUI.backgroundColor = originalColor;
                        }
                        EditorGUILayout.EndHorizontal();

                        // Stats
                        EditorGUI.indentLevel++;
                        string stats = $"ΔPos: {entry.positionDifference.magnitude:F4}m | ΔRot: {entry.rotationDifference:F1}°";
                        if (merger.detectScaleConflicts) 
                            stats += $" | ΔScale: {entry.scaleDifference.magnitude:F3}";
                        
                        var miniStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
                        EditorGUILayout.LabelField(stats, miniStyle);
                        EditorGUI.indentLevel--;

                        EditorGUILayout.Space(2);

                        // Resolution
                        var resProp = entryProp.FindPropertyRelative("resolution");
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(resProp, new GUIContent("Resolution"));
                        if (EditorGUI.EndChangeCheck()) statsNeedUpdate = true;

                        BoneConflictResolution currentRes = (BoneConflictResolution)resProp.enumValueIndex;

                        if (currentRes == BoneConflictResolution.MergeIntoSelected)
                        {
                            var targetProp = entryProp.FindPropertyRelative("customTargetBone");
                            if (targetProp.objectReferenceValue == null) 
                                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);
                            
                            EditorGUILayout.PropertyField(targetProp, new GUIContent("→ Target Bone"));
                            GUI.backgroundColor = originalColor;
                        }
                        else if (currentRes == BoneConflictResolution.ConstraintToTarget)
                        {
                            EditorGUILayout.HelpBox("Bone stays separate, follows target via constraint.", MessageType.Info);
                        }

                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(2);
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(3);
            EditorGUILayout.EndVertical();
        }

        private void DrawBulkActions(CVRMergeArmature merger)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Bulk Actions:", EditorStyles.miniLabel, GUILayout.Width(75));
            
            if (GUILayout.Button("Constraint All", EditorStyles.miniButtonLeft, GUILayout.Height(20))) 
            {
                SetAllResolutions(merger, BoneConflictResolution.ConstraintToTarget);
                statsNeedUpdate = true;
            }
            if (GUILayout.Button("Force Merge All", EditorStyles.miniButtonMid, GUILayout.Height(20))) 
            {
                SetAllResolutions(merger, BoneConflictResolution.StillMerge);
                statsNeedUpdate = true;
            }
            if (GUILayout.Button("Rename All", EditorStyles.miniButtonRight, GUILayout.Height(20))) 
            {
                SetAllResolutions(merger, BoneConflictResolution.Rename);
                statsNeedUpdate = true;
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void SetAllResolutions(CVRMergeArmature merger, BoneConflictResolution res)
        {
            foreach (var c in merger.boneConflicts) c.resolution = res;
            EditorUtility.SetDirty(merger);
        }

        private void DrawAdvancedSettings(CVRMergeArmature merger)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.95f, 0.95f, 0.95f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "⚙ Advanced Settings", true, EditorStyles.foldoutHeader);
            
            if (showAdvancedSettings)
            {
                EditorGUILayout.Space(3);
                EditorGUI.indentLevel++;

                DrawSubsectionLabel("Exclusions");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedTransforms"), new GUIContent("Excluded Transforms"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedNamePatterns"), new GUIContent("Excluded Name Patterns"), true);

                EditorGUILayout.Space(8);
                DrawSubsectionLabel("Safety & Components");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("preventScaleDistortion"), new GUIContent("Prevent Scale Distortion"));
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeDynamicBones"), new GUIContent("Merge DynamicBones"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeMagicaCloth"), new GUIContent("Merge Magica Cloth"));
                if (EditorGUI.EndChangeCheck()) statsNeedUpdate = true;

                EditorGUILayout.Space(8);
                DrawSubsectionLabel("CVR Component Merging");
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAdvancedAvatarSetup"), new GUIContent("Merge Advanced Avatar Setup"));

                if (merger.mergeAdvancedAvatarSetup)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("generateAASControllerAtEnd"), new GUIContent("Generate Controller"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("advancedSettingsPrefix"), new GUIContent("Settings Prefix"));
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAdvancedPointerTrigger"), new GUIContent("Merge Pointer Trigger"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeParameterStream"), new GUIContent("Merge Parameter Stream"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAnimatorDriver"), new GUIContent("Merge Animator Driver"));
                if (EditorGUI.EndChangeCheck()) statsNeedUpdate = true;

                EditorGUILayout.Space(8);
                DrawSubsectionLabel("Global Animator Override");
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mergeAnimator"), new GUIContent("Enable Animator Merging"));
                if (EditorGUI.EndChangeCheck()) statsNeedUpdate = true;

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreviewInfo(CVRMergeArmature merger)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.9f, 1f, 0.9f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = originalColor;

            EditorGUILayout.BeginHorizontal();
            showPreviewStats = EditorGUILayout.Foldout(showPreviewStats, "📊 Merge Preview & Statistics", true, EditorStyles.foldoutHeader);
            
            if (showPreviewStats && GUILayout.Button("🔄", GUILayout.Width(30), GUILayout.Height(18)))
            {
                statsNeedUpdate = true;
            }
            EditorGUILayout.EndHorizontal();
            
            if (!showPreviewStats)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(3);

            var targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar == null)
            {
                EditorGUILayout.HelpBox("No CVRAvatar found. Cannot generate preview.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            Transform targetArmature = FindArmatureFromCVRAvatar(targetCVRAvatar);
            if (targetArmature == null)
            {
                EditorGUILayout.HelpBox("Could not locate Armature. Cannot generate preview.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            // Gather statistics (with caching)
            if (cachedStats == null || statsNeedUpdate)
            {
                cachedStats = GatherMergeStatistics(merger, targetArmature);
                statsNeedUpdate = false;
            }

            // Overview section (always visible when expanded)
            DrawPreviewOverview(cachedStats);

            EditorGUILayout.Space(5);

            // Detailed sections with smaller scroll view
            previewScrollPos = EditorGUILayout.BeginScrollView(previewScrollPos, GUILayout.MaxHeight(300));
            
            DrawPerOutfitDetails(merger, cachedStats);
            EditorGUILayout.Space(5);
            DrawComponentDetails(merger, cachedStats);
            EditorGUILayout.Space(5);
            DrawAnimatorDetails(merger, cachedStats);
            
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private class MergeStatistics
        {
            public int totalOutfits;
            public int validOutfits;
            public int totalMeshes;
            public int totalMaterials;
            public int bonesToMerge;
            public int bonesToConstrain;
            public int bonesToRename;
            public int uniqueBonesToAdd;
            public int conflictsResolved;
            public int conflictsUnresolved;
            public int totalVertices;
            public int totalTriangles;
            
            public Dictionary<string, OutfitStats> outfitStats = new Dictionary<string, OutfitStats>();
            
            public int dynamicBoneCount;
            public int magicaClothCount;
            public int magicaCloth2Count;
            public int aasComponentCount;
            public int pointerTriggerCount;
            public int parameterStreamCount;
            public int totalParameterStreamEntries;
            public int animatorDriverCount;
            
            // Advanced Avatar Settings details
            public int totalAASParameters;
            public Dictionary<string, int> aasParameterTypeCounts = new Dictionary<string, int>();
            
            // Advanced Safety (Tagging) details
            public int advancedSafetyCount;
            public Dictionary<string, int> safetyTagCounts = new Dictionary<string, int>();
            
            public int animatorsToMerge;
            public int totalAnimatorLayers;
            public int totalAnimatorParameters;
        }

        private class OutfitStats
        {
            public string name;
            public int meshCount;
            public int materialCount;
            public int vertexCount;
            public int triangleCount;
            public int bonesToMerge;
            public int uniqueBones;
            public int conflictedBones;
            public bool hasAnimator;
            public int animatorLayers;
            
            // CVR Component counts per outfit
            public int aasComponents;
            public int aasParameters;
            public Dictionary<string, int> aasTypes = new Dictionary<string, int>();
            public int advancedSafetyEntries;
            public int pointerTriggers;
            public int parameterStreams;
            public int parameterStreamEntries;
            public int animatorDrivers;
            public int dynamicBones;
            public int magicaCloths;
            public int magicaCloth2s;
        }

        private MergeStatistics GatherMergeStatistics(CVRMergeArmature merger, Transform targetArmature)
        {
            var stats = new MergeStatistics();
            stats.totalOutfits = merger.outfitsToMerge.Count;

            foreach (var outfit in merger.outfitsToMerge)
            {
                if (outfit.outfit == null) continue;
                
                stats.validOutfits++;
                var outfitStats = new OutfitStats { name = outfit.outfit.name };

                // Mesh statistics
                var meshes = outfit.outfit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                outfitStats.meshCount = meshes.Length;
                stats.totalMeshes += meshes.Length;

                HashSet<Material> uniqueMaterials = new HashSet<Material>();
                foreach (var smr in meshes)
                {
                    if (smr.sharedMesh != null)
                    {
                        outfitStats.vertexCount += smr.sharedMesh.vertexCount;
                        outfitStats.triangleCount += smr.sharedMesh.triangles.Length / 3;
                        stats.totalVertices += smr.sharedMesh.vertexCount;
                        stats.totalTriangles += smr.sharedMesh.triangles.Length / 3;
                    }

                    if (smr.sharedMaterials != null)
                    {
                        foreach (var mat in smr.sharedMaterials)
                        {
                            if (mat != null) uniqueMaterials.Add(mat);
                        }
                    }
                }
                
                outfitStats.materialCount = uniqueMaterials.Count;
                stats.totalMaterials += uniqueMaterials.Count;

                // Bone statistics
                var usedBones = GetBonesUsedByMeshes(outfit.outfit.transform);
                var conflictLookup = merger.boneConflicts
                    .Where(c => c.outfitName == outfit.outfit.name)
                    .ToDictionary(c => c.sourceBone);

                foreach (var bone in usedBones)
                {
                    if (conflictLookup.TryGetValue(bone, out var conflict))
                    {
                        outfitStats.conflictedBones++;
                        
                        if (conflict.resolution == BoneConflictResolution.StillMerge || 
                            conflict.resolution == BoneConflictResolution.MergeIntoSelected)
                        {
                            outfitStats.bonesToMerge++;
                            stats.bonesToMerge++;
                            stats.conflictsResolved++;
                        }
                        else if (conflict.resolution == BoneConflictResolution.ConstraintToTarget)
                        {
                            stats.bonesToConstrain++;
                            stats.conflictsResolved++;
                        }
                        else if (conflict.resolution == BoneConflictResolution.Rename)
                        {
                            outfitStats.uniqueBones++;
                            stats.bonesToRename++;
                            stats.conflictsResolved++;
                        }
                        else
                        {
                            stats.conflictsUnresolved++;
                        }
                    }
                    else
                    {
                        string boneName = bone.name;
                        if (FindBoneByName(targetArmature, boneName) != null)
                        {
                            outfitStats.bonesToMerge++;
                            stats.bonesToMerge++;
                        }
                        else
                        {
                            outfitStats.uniqueBones++;
                            stats.uniqueBonesToAdd++;
                        }
                    }
                }

                // Animator check
                var animator = outfit.outfit.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    outfitStats.hasAnimator = true;
                    
                    var controller = animator.runtimeAnimatorController as AnimatorController;
                    if (controller != null)
                    {
                        outfitStats.animatorLayers = controller.layers.Length;
                        
                        if ((outfit.mergeAnimator || outfit.mergeAnimatorIncludingAAS) && merger.mergeAnimator)
                        {
                            stats.animatorsToMerge++;
                            stats.totalAnimatorLayers += controller.layers.Length;
                            stats.totalAnimatorParameters += controller.parameters.Length;
                        }
                    }
                }

                // ========== CVR AVATAR COMPONENT DETECTION ==========
                var cvrAvatarType = System.Type.GetType("ABI.CCK.Components.CVRAvatar, Assembly-CSharp");
                
                if (cvrAvatarType != null)
                {
                    var cvrAvatar = outfit.outfit.GetComponent(cvrAvatarType);
                    
                    if (cvrAvatar != null)
                    {
                        // Advanced Avatar Settings
                        if (merger.mergeAdvancedAvatarSetup)
                        {
                            var avatarSettingsField = cvrAvatarType.GetField("avatarSettings");
                            if (avatarSettingsField != null)
                            {
                                var avatarSettings = avatarSettingsField.GetValue(cvrAvatar);
                                
                                if (avatarSettings != null)
                                {
                                    var avatarSettingsType = avatarSettings.GetType();
                                    var settingsField = avatarSettingsType.GetField("settings");
                                    
                                    if (settingsField != null)
                                    {
                                        var settingsList = settingsField.GetValue(avatarSettings) as System.Collections.IList;
                                        
                                        if (settingsList != null && settingsList.Count > 0)
                                        {
                                            outfitStats.aasComponents = settingsList.Count;
                                            stats.aasComponentCount += settingsList.Count;
                                            
                                            // Count by type
                                            foreach (var setting in settingsList)
                                            {
                                                if (setting == null) continue;
                                                
                                                var typeField = setting.GetType().GetField("type");
                                                if (typeField != null)
                                                {
                                                    var settingType = typeField.GetValue(setting)?.ToString() ?? "Unknown";
                                                    
                                                    if (!outfitStats.aasTypes.ContainsKey(settingType))
                                                        outfitStats.aasTypes[settingType] = 0;
                                                    outfitStats.aasTypes[settingType]++;
                                                    
                                                    if (!stats.aasParameterTypeCounts.ContainsKey(settingType))
                                                        stats.aasParameterTypeCounts[settingType] = 0;
                                                    stats.aasParameterTypeCounts[settingType]++;
                                                    
                                                    outfitStats.aasParameters++;
                                                    stats.totalAASParameters++;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        
                        // Advanced Safety (Advanced Tagging) - CORRECT FIELD NAME
                        var advancedTaggingField = cvrAvatarType.GetField("advancedTaggingList");
                        if (advancedTaggingField != null)
                        {
                            var taggingList = advancedTaggingField.GetValue(cvrAvatar) as System.Collections.IList;
                            
                            if (taggingList != null && taggingList.Count > 0)
                            {
                                outfitStats.advancedSafetyEntries = taggingList.Count;
                                stats.advancedSafetyCount += taggingList.Count;
                                
                                // Count individual tags
                                foreach (var entry in taggingList)
                                {
                                    if (entry == null) continue;
                                    
                                    var tagsField = entry.GetType().GetField("tags");
                                    if (tagsField != null)
                                    {
                                        var tagsValue = tagsField.GetValue(entry);
                                        if (tagsValue != null)
                                        {
                                            // Tags is an enum Flags, convert to string and parse
                                            var tagsString = tagsValue.ToString();
                                            
                                            // Handle flags enum - it can be comma-separated
                                            if (!string.IsNullOrEmpty(tagsString) && tagsString != "0")
                                            {
                                                var tagNames = tagsString.Split(new[] { ", " }, System.StringSplitOptions.RemoveEmptyEntries);
                                                foreach (var tagName in tagNames)
                                                {
                                                    var cleanTag = tagName.Trim();
                                                    if (!string.IsNullOrEmpty(cleanTag))
                                                    {
                                                        if (!stats.safetyTagCounts.ContainsKey(cleanTag))
                                                            stats.safetyTagCounts[cleanTag] = 0;
                                                        stats.safetyTagCounts[cleanTag]++;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // CVR Pointer - DIRECT COMPONENT
                if (merger.mergeAdvancedPointerTrigger)
                {
                    var pointerType = System.Type.GetType("ABI.CCK.Components.CVRPointer, Assembly-CSharp");
                    if (pointerType != null)
                    {
                        var pointers = outfit.outfit.GetComponentsInChildren(pointerType, true);
                        outfitStats.pointerTriggers = pointers.Length;
                        stats.pointerTriggerCount += pointers.Length;
                    }
                }

                // CVR Parameter Stream - DIRECT COMPONENT
                if (merger.mergeParameterStream)
                {
                    var psType = System.Type.GetType("ABI.CCK.Components.CVRParameterStream, Assembly-CSharp");
                    if (psType != null)
                    {
                        var paramStreams = outfit.outfit.GetComponentsInChildren(psType, true);
                        outfitStats.parameterStreams = paramStreams.Length;
                        stats.parameterStreamCount += paramStreams.Length;
                        
                        foreach (var comp in paramStreams)
                        {
                            var entriesField = psType.GetField("entries");
                            if (entriesField != null)
                            {
                                var entries = entriesField.GetValue(comp) as System.Collections.IList;
                                if (entries != null)
                                {
                                    outfitStats.parameterStreamEntries += entries.Count;
                                    stats.totalParameterStreamEntries += entries.Count;
                                }
                            }
                        }
                    }
                }

                // CVR Animator Driver - DIRECT COMPONENT (NOT StateMachineBehaviour!)
                if (merger.mergeAnimatorDriver)
                {
                    var driverType = System.Type.GetType("ABI.CCK.Components.CVRAnimatorDriver, Assembly-CSharp");
                    if (driverType != null)
                    {
                        var drivers = outfit.outfit.GetComponentsInChildren(driverType, true);
                        outfitStats.animatorDrivers = drivers.Length;
                        stats.animatorDriverCount += drivers.Length;
                    }
                }

                // DynamicBone
                if (merger.mergeDynamicBones)
                {
                    var dbType = System.Type.GetType("DynamicBone, Assembly-CSharp");
                    if (dbType != null)
                    {
                        var dynBones = outfit.outfit.GetComponentsInChildren(dbType, true);
                        outfitStats.dynamicBones = dynBones.Length;
                        stats.dynamicBoneCount += dynBones.Length;
                    }
                }

                // Magica Cloth 1 & 2
                if (merger.mergeMagicaCloth)
                {
                    var mcType = System.Type.GetType("MagicaCloth.MagicaCloth, Assembly-CSharp");
                    if (mcType != null)
                    {
                        var magica1 = outfit.outfit.GetComponentsInChildren(mcType, true);
                        outfitStats.magicaCloths = magica1.Length;
                        stats.magicaClothCount += magica1.Length;
                    }
                    
                    var mc2Type = System.Type.GetType("MagicaCloth2.MagicaCloth, Assembly-CSharp");
                    if (mc2Type != null)
                    {
                        var magica2 = outfit.outfit.GetComponentsInChildren(mc2Type, true);
                        outfitStats.magicaCloth2s = magica2.Length;
                        stats.magicaCloth2Count += magica2.Length;
                    }
                }

                stats.outfitStats[outfit.outfit.name] = outfitStats;
            }

            return stats;
        }

        private void DrawPreviewOverview(MergeStatistics stats)
        {
            GUI.backgroundColor = new Color(0.85f, 0.95f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            DrawSubsectionLabel("📋 Overview");
            EditorGUILayout.Space(2);

            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Outfits:", $"{stats.validOutfits} / {stats.totalOutfits} valid", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Meshes to Add:", $"{stats.totalMeshes}");
            EditorGUILayout.LabelField("Materials:", $"{stats.totalMaterials}");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Total Vertices:", $"{stats.totalVertices:N0}");
            EditorGUILayout.LabelField("Total Triangles:", $"{stats.totalTriangles:N0}");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            EditorGUILayout.LabelField("Bone Operations:", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("• Bones to Merge:", $"{stats.bonesToMerge}");
            EditorGUILayout.LabelField("• Unique Bones to Add:", $"{stats.uniqueBonesToAdd}");
            if (stats.bonesToConstrain > 0)
                EditorGUILayout.LabelField("• Bones to Constrain:", $"{stats.bonesToConstrain}");
            if (stats.bonesToRename > 0)
                EditorGUILayout.LabelField("• Bones to Rename:", $"{stats.bonesToRename}");
            EditorGUI.indentLevel--;

            if (stats.conflictsResolved > 0 || stats.conflictsUnresolved > 0)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Conflicts:", EditorStyles.boldLabel, GUILayout.Width(65));
                
                if (stats.conflictsUnresolved > 0)
                {
                    GUI.color = new Color(1f, 0.6f, 0.6f);
                    EditorGUILayout.LabelField($"⚠ {stats.conflictsUnresolved} unresolved", EditorStyles.boldLabel);
                    GUI.color = Color.white;
                }
                else
                {
                    GUI.color = new Color(0.6f, 1f, 0.6f);
                    EditorGUILayout.LabelField($"✓ All {stats.conflictsResolved} resolved", EditorStyles.boldLabel);
                    GUI.color = Color.white;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawPerOutfitDetails(CVRMergeArmature merger, MergeStatistics stats)
        {
            GUI.backgroundColor = new Color(0.95f, 0.9f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            showPerOutfitDetails = EditorGUILayout.Foldout(showPerOutfitDetails, "📦 Per-Outfit Details", true, EditorStyles.foldoutHeader);

            if (showPerOutfitDetails)
            {
                EditorGUI.indentLevel++;

                foreach (var outfit in merger.outfitsToMerge)
                {
                    if (outfit.outfit == null || !stats.outfitStats.ContainsKey(outfit.outfit.name)) continue;
                    
                    var outfitStat = stats.outfitStats[outfit.outfit.name];

                    EditorGUILayout.Space(2);
                    GUI.backgroundColor = new Color(1f, 1f, 1f, 0.3f);
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUI.backgroundColor = Color.white;

                    EditorGUILayout.LabelField($"🎭 {outfitStat.name}", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Meshes: {outfitStat.meshCount}", GUILayout.Width(100));
                    EditorGUILayout.LabelField($"Materials: {outfitStat.materialCount}", GUILayout.Width(110));
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"Verts: {outfitStat.vertexCount:N0}", GUILayout.Width(100));
                    EditorGUILayout.LabelField($"Tris: {outfitStat.triangleCount:N0}", GUILayout.Width(110));
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField($"Bones: {outfitStat.bonesToMerge} merge, {outfitStat.uniqueBones} unique");
                    
                    if (outfitStat.conflictedBones > 0)
                    {
                        GUI.color = new Color(1f, 0.8f, 0.6f);
                        EditorGUILayout.LabelField($"⚠ {outfitStat.conflictedBones} conflicted bones");
                        GUI.color = Color.white;
                    }

                    // CVR Components per outfit - ENHANCED WITH DETAILS
                    if (outfitStat.aasComponents > 0)
                    {
                        GUI.color = new Color(0.7f, 0.9f, 1f);
                        string typesBreakdown = string.Join(", ", outfitStat.aasTypes.Select(kvp => $"{kvp.Key}: {kvp.Value}"));
                        EditorGUILayout.LabelField($"✓ AAS: {outfitStat.aasComponents} settings");
                        EditorGUILayout.LabelField($"  Types: {typesBreakdown}", EditorStyles.miniLabel);
                        GUI.color = Color.white;
                    }
                    
                    if (outfitStat.advancedSafetyEntries > 0)
                    {
                        GUI.color = new Color(1f, 0.9f, 0.7f);
                        EditorGUILayout.LabelField($"🛡 Advanced Safety: {outfitStat.advancedSafetyEntries} entries");
                        GUI.color = Color.white;
                    }
                    
                    List<string> components = new List<string>();
                    if (outfitStat.pointerTriggers > 0) components.Add($"Pointer: {outfitStat.pointerTriggers}");
                    if (outfitStat.parameterStreams > 0) components.Add($"ParamStream: {outfitStat.parameterStreams} ({outfitStat.parameterStreamEntries} entries)");
                    if (outfitStat.animatorDrivers > 0) components.Add($"AnimDriver: {outfitStat.animatorDrivers}");
                    if (outfitStat.dynamicBones > 0) components.Add($"DynBone: {outfitStat.dynamicBones}");
                    if (outfitStat.magicaCloths > 0) components.Add($"Magica1: {outfitStat.magicaCloths}");
                    if (outfitStat.magicaCloth2s > 0) components.Add($"Magica2: {outfitStat.magicaCloth2s}");
                    
                    if (components.Count > 0)
                    {
                        EditorGUILayout.LabelField($"Components: {string.Join(", ", components)}", EditorStyles.miniLabel);
                    }

                    if (outfitStat.hasAnimator)
                    {
                        if ((outfit.mergeAnimator || outfit.mergeAnimatorIncludingAAS) && merger.mergeAnimator)
                        {
                            GUI.color = new Color(0.7f, 1f, 0.7f);
                            EditorGUILayout.LabelField($"✓ Animator ({outfitStat.animatorLayers} layers)");
                            GUI.color = Color.white;
                        }
                        else
                        {
                            GUI.color = new Color(1f, 0.9f, 0.7f);
                            EditorGUILayout.LabelField($"○ Animator (not merging)");
                            GUI.color = Color.white;
                        }
                    }

                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawComponentDetails(CVRMergeArmature merger, MergeStatistics stats)
        {
            GUI.backgroundColor = new Color(1f, 0.95f, 0.9f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            showComponentDetails = EditorGUILayout.Foldout(showComponentDetails, "🧩 Component Merging Summary", true, EditorStyles.foldoutHeader);

            if (showComponentDetails)
            {
                EditorGUI.indentLevel++;

                bool anyComponents = false;

                // Advanced Avatar Setup - DETAILED
                if (stats.aasComponentCount > 0 || merger.mergeAdvancedAvatarSetup)
                {
                    if (stats.aasComponentCount > 0)
                    {
                        GUI.color = new Color(0.7f, 1f, 0.7f);
                        EditorGUILayout.LabelField($"✓ Advanced Avatar Setup:", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"• Total Settings: {stats.aasComponentCount}");
                        EditorGUILayout.LabelField($"• Total Parameters: {stats.totalAASParameters}");
                        
                        if (stats.aasParameterTypeCounts.Count > 0)
                        {
                            EditorGUILayout.LabelField("• By Type:", EditorStyles.miniLabel);
                            EditorGUI.indentLevel++;
                            foreach (var kvp in stats.aasParameterTypeCounts.OrderByDescending(x => x.Value))
                            {
                                EditorGUILayout.LabelField($"{kvp.Key}: {kvp.Value}", EditorStyles.miniLabel);
                            }
                            EditorGUI.indentLevel--;
                        }
                        
                        EditorGUILayout.LabelField($"• Generate Controller: {(merger.generateAASControllerAtEnd ? "Yes" : "No")}");
                        if (!string.IsNullOrEmpty(merger.advancedSettingsPrefix))
                        {
                            EditorGUILayout.LabelField($"• Settings Prefix: \"{merger.advancedSettingsPrefix}\"");
                        }
                        EditorGUI.indentLevel--;
                        GUI.color = Color.white;
                        anyComponents = true;
                    }
                    else
                    {
                        GUI.color = new Color(1f, 0.9f, 0.7f);
                        EditorGUILayout.LabelField($"○ AAS enabled but no settings found");
                        GUI.color = Color.white;
                    }
                }

                // Advanced Safety - NEW SECTION
                if (stats.advancedSafetyCount > 0)
                {
                    GUI.color = new Color(1f, 0.95f, 0.7f);
                    EditorGUILayout.LabelField($"✓ Advanced Safety (Tagging):", EditorStyles.boldLabel);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField($"• Total Entries: {stats.advancedSafetyCount}");
                    
                    if (stats.safetyTagCounts.Count > 0)
                    {
                        EditorGUILayout.LabelField("• Tags Used:", EditorStyles.miniLabel);
                        EditorGUI.indentLevel++;
                        foreach (var kvp in stats.safetyTagCounts.OrderByDescending(x => x.Value))
                        {
                            EditorGUILayout.LabelField($"{kvp.Key}: {kvp.Value}", EditorStyles.miniLabel);
                        }
                        EditorGUI.indentLevel--;
                    }
                    
                    EditorGUI.indentLevel--;
                    GUI.color = Color.white;
                    anyComponents = true;
                }

                // CVR Pointer
                if (stats.pointerTriggerCount > 0)
                {
                    EditorGUILayout.LabelField($"• CVR Pointer: {stats.pointerTriggerCount} → {(merger.mergeAdvancedPointerTrigger ? "✓ Merging" : "✗ Skip")}");
                    anyComponents = true;
                }

                // CVR Parameter Stream
                if (stats.parameterStreamCount > 0)
                {
                    EditorGUILayout.LabelField($"• CVR Parameter Stream: {stats.parameterStreamCount} components ({stats.totalParameterStreamEntries} entries) → {(merger.mergeParameterStream ? "✓ Merging" : "✗ Skip")}");
                    anyComponents = true;
                }

                // CVR Animator Driver
                if (stats.animatorDriverCount > 0)
                {
                    EditorGUILayout.LabelField($"• CVR Animator Driver: {stats.animatorDriverCount} components → {(merger.mergeAnimatorDriver ? "✓ Merging" : "✗ Skip")}");
                    anyComponents = true;
                }

                // DynamicBone
                if (stats.dynamicBoneCount > 0)
                {
                    EditorGUILayout.LabelField($"• DynamicBone: {stats.dynamicBoneCount} → {(merger.mergeDynamicBones ? "✓ Merging" : "✗ Skip")}");
                    anyComponents = true;
                }

                // Magica Cloth
                if (stats.magicaClothCount > 0)
                {
                    EditorGUILayout.LabelField($"• Magica Cloth 1: {stats.magicaClothCount} → {(merger.mergeMagicaCloth ? "✓ Merging" : "✗ Skip")}");
                    anyComponents = true;
                }

                if (stats.magicaCloth2Count > 0)
                {
                    EditorGUILayout.LabelField($"• Magica Cloth 2: {stats.magicaCloth2Count} → {(merger.mergeMagicaCloth ? "✓ Merging" : "✗ Skip")}");
                    anyComponents = true;
                }

                if (!anyComponents)
                {
                    EditorGUILayout.LabelField("No special components detected.", EditorStyles.miniLabel);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawAnimatorDetails(CVRMergeArmature merger, MergeStatistics stats)
        {
            GUI.backgroundColor = new Color(0.95f, 0.9f, 1f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;

            showAnimatorDetails = EditorGUILayout.Foldout(showAnimatorDetails, "🎬 Animator Merging Details", true, EditorStyles.foldoutHeader);

            if (showAnimatorDetails)
            {
                EditorGUI.indentLevel++;

                if (!merger.mergeAnimator)
                {
                    GUI.color = new Color(1f, 0.8f, 0.6f);
                    EditorGUILayout.LabelField("⚠ Global Animator Merging is DISABLED", EditorStyles.boldLabel);
                    GUI.color = Color.white;
                }
                else if (stats.animatorsToMerge == 0)
                {
                    EditorGUILayout.LabelField("No animators will be merged.", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField($"Animators to Merge: {stats.animatorsToMerge}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"• Total Layers: {stats.totalAnimatorLayers}");
                    EditorGUILayout.LabelField($"• Total Parameters: {stats.totalAnimatorParameters}");

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Options:", EditorStyles.miniLabel);
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField($"• Rewrite Paths: {(merger.animatorRewritePaths ? "✓" : "✗")}");
                    EditorGUILayout.LabelField($"• Merge Masks: {(merger.animatorMergeAvatarMasks ? "✓" : "✗")}");
                    EditorGUILayout.LabelField($"• Combine Layers: {(merger.animatorCombineLayersByName ? "✓" : "✗")}");
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        // =================================================================================
        // CONFLICT DETECTION LOGIC
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
