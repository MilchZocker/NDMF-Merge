using UnityEngine;
using UnityEditor;
using NDMFMerge.Runtime;
using System.Collections.Generic;

namespace NDMFMerge.Editor
{
    [CustomEditor(typeof(CVRMergeArmature))]
    public class CVRMergeArmatureEditor : UnityEditor.Editor
    {
        private SerializedProperty mergeModeProperty;
        private bool showAdvancedSettings = false;
        private bool showBoneConflicts = false;
        private bool showBrokenReferences = false;
        private Vector2 conflictScrollPos;
        private Vector2 brokenRefScrollPos;
        
        private void OnEnable()
        {
            mergeModeProperty = serializedObject.FindProperty("mergeMode");
        }
        
        public override void OnInspectorGUI()
        {
            var merger = (CVRMergeArmature)target;
            serializedObject.Update();
            
            EditorGUILayout.HelpBox(
                "Non-destructive armature and model merging for ChilloutVR avatars. " +
                "Processing occurs automatically during avatar upload.",
                MessageType.Info);
            
            EditorGUILayout.Space();
            
            EditorGUILayout.PropertyField(mergeModeProperty);
            
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Target Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetCVRAvatarObject"), 
                new GUIContent("Target Avatar Object", "GameObject with CVRAvatar component (leave empty to auto-detect)"));
            
            if (merger.targetCVRAvatarObject != null)
            {
                var cvrAvatar = merger.GetTargetCVRAvatar();
                if (cvrAvatar != null)
                {
                    EditorGUILayout.HelpBox($"✓ CVRAvatar found on: {cvrAvatar.gameObject.name}", MessageType.Info);
                    
                    var armature = FindArmatureFromCVRAvatar(cvrAvatar);
                    if (armature != null)
                    {
                        EditorGUILayout.HelpBox($"✓ Detected Armature: {armature.name}", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox("⚠ Could not detect armature in CVRAvatar!", MessageType.Warning);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("✗ No CVRAvatar component found on this GameObject!", MessageType.Error);
                }
            }
            
            EditorGUILayout.Space();
            
            if (merger.mergeMode == MergeMode.ArmatureMerge)
            {
                DrawArmatureMergeSettings(merger);
            }
            else
            {
                DrawModelMergeSettings(merger);
            }
            
            EditorGUILayout.Space();
            
            if (merger.mergeMode == MergeMode.ArmatureMerge)
            {
                DrawBoneConflictsSection(merger);
            }
            
            EditorGUILayout.Space();
            
            DrawBrokenReferencesSection(merger);
            
            EditorGUILayout.Space();
            
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                DrawAdvancedSettings(merger);
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space();
            
            DrawPreviewInfo(merger);
            DrawValidationWarnings(merger);
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void DrawArmatureMergeSettings(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Armature Merge Settings", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("prefix"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("suffix"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Exclusions", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedTransforms"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("excludedNamePatterns"), true);
        }
        
        private void DrawModelMergeSettings(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Model Merge Settings", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetBone"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("useCurrentOffset"));
            
            if (!merger.useCurrentOffset)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("positionOffset"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("rotationOffset"));
            }
            else if (merger.targetBone != null)
            {
                var posDiff = merger.transform.position - merger.targetBone.position;
                var rotDiff = (merger.transform.rotation * Quaternion.Inverse(merger.targetBone.rotation)).eulerAngles;
                
                EditorGUILayout.HelpBox(
                    $"Current offset from {merger.targetBone.name}:\n" +
                    $"Position: {posDiff}\n" +
                    $"Rotation: {rotDiff}",
                    MessageType.None);
            }
        }
        
        private void DrawBoneConflictsSection(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Bone Conflict Detection", EditorStyles.boldLabel);
            
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultBoneConflictResolution"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("conflictThreshold"));
            
            EditorGUILayout.Space(5);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Detect Conflicts", GUILayout.Height(30)))
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
                    $"Found {merger.boneConflicts.Count} bone conflicts. Choose resolution for each:",
                    MessageType.Warning);
                
                showBoneConflicts = EditorGUILayout.Foldout(showBoneConflicts, $"Conflicts ({merger.boneConflicts.Count})", true);
                
                if (showBoneConflicts)
                {
                    conflictScrollPos = EditorGUILayout.BeginScrollView(conflictScrollPos, GUILayout.MaxHeight(300));
                    
                    for (int i = 0; i < merger.boneConflicts.Count; i++)
                    {
                        var conflict = merger.boneConflicts[i];
                        
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        
                        EditorGUILayout.LabelField($"Bone: {conflict.sourceBone.name}", EditorStyles.boldLabel);
                        
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"Position Δ: {conflict.positionDifference.magnitude:F4}m");
                        EditorGUILayout.LabelField($"Rotation Δ: {conflict.rotationDifference:F2}°");
                        EditorGUILayout.LabelField($"Scale Δ: {conflict.scaleDifference.magnitude:F4}");
                        EditorGUI.indentLevel--;
                        
                        conflict.resolution = (BoneConflictResolution)EditorGUILayout.EnumPopup(
                            "Resolution", conflict.resolution);
                        
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
        
        private void DrawBrokenReferencesSection(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Animator Reference Checker", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Check Animator References", GUILayout.Height(30)))
            {
                CheckBrokenAnimatorReferences(merger);
            }
            if (merger.brokenReferences.Count > 0)
            {
                if (GUILayout.Button("Auto-Fix All", GUILayout.Width(100), GUILayout.Height(30)))
                {
                    AutoFixAllReferences(merger);
                }
                if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(30)))
                {
                    merger.brokenReferences.Clear();
                    EditorUtility.SetDirty(merger);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            if (merger.brokenReferences.Count > 0)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    $"Found {merger.brokenReferences.Count} broken animator references.",
                    MessageType.Warning);
                
                showBrokenReferences = EditorGUILayout.Foldout(showBrokenReferences, 
                    $"Broken References ({merger.brokenReferences.Count})", true);
                
                if (showBrokenReferences)
                {
                    brokenRefScrollPos = EditorGUILayout.BeginScrollView(brokenRefScrollPos, GUILayout.MaxHeight(300));
                    
                    for (int i = 0; i < merger.brokenReferences.Count; i++)
                    {
                        var brokenRef = merger.brokenReferences[i];
                        
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        
                        EditorGUILayout.LabelField($"Broken Path: {brokenRef.originalPath}", EditorStyles.boldLabel);
                        
                        EditorGUI.indentLevel++;
                        EditorGUILayout.LabelField($"Property: {brokenRef.fieldName}");
                        
                        if (!string.IsNullOrEmpty(brokenRef.suggestedPath))
                        {
                            EditorGUILayout.LabelField($"Suggested: {brokenRef.suggestedPath}", 
                                new GUIStyle(EditorStyles.label) { normal = { textColor = Color.green } });
                            
                            EditorGUILayout.BeginHorizontal();
                            
                            brokenRef.suggestedPath = EditorGUILayout.TextField("New Path:", brokenRef.suggestedPath);
                            
                            if (GUILayout.Button("Fix", GUILayout.Width(50)))
                            {
                                FixAnimatorReference(merger, brokenRef);
                            }
                            
                            EditorGUILayout.EndHorizontal();
                        }
                        else
                        {
                            EditorGUILayout.LabelField("No suggestion found", 
                                new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } });
                        }
                        
                        EditorGUI.indentLevel--;
                        
                        EditorGUILayout.EndVertical();
                        EditorGUILayout.Space(2);
                    }
                    
                    EditorGUILayout.EndScrollView();
                }
            }
        }
        
        private void DrawAdvancedSettings(CVRMergeArmature merger)
        {
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
            
            if (merger.mergeAnimator)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("animatorToMerge"), 
                    new GUIContent("Animator Override", "Leave empty to auto-detect from CVRAvatar or Animator component"));
                EditorGUI.indentLevel--;
            }
        }
        
        private void DrawPreviewInfo(CVRMergeArmature merger)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            
            var smrs = merger.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshRenderers = merger.GetComponentsInChildren<MeshRenderer>(true);
            
            EditorGUILayout.LabelField($"SkinnedMeshRenderers: {smrs.Length}");
            EditorGUILayout.LabelField($"MeshRenderers: {meshRenderers.Length}");
            
            if (merger.mergeMode == MergeMode.ArmatureMerge)
            {
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
                EditorGUILayout.LabelField($"Unique bones: {bones.Count}");
                EditorGUILayout.LabelField($"Excluded transforms: {merger.excludedTransforms.Count}");
            }
        }
        
        private void DrawValidationWarnings(CVRMergeArmature merger)
        {
            if (merger.mergeMode == MergeMode.ModelMerge && merger.targetBone == null)
            {
                EditorGUILayout.HelpBox(
                    "Model Merge mode requires a Target Bone to be set!",
                    MessageType.Error);
            }
            
            if (merger.targetCVRAvatarObject != null)
            {
                var cvrAvatar = merger.GetTargetCVRAvatar();
                if (cvrAvatar == null)
                {
                    EditorGUILayout.HelpBox(
                        "Target GameObject does not have a CVRAvatar component!",
                        MessageType.Error);
                }
            }
            
            if (merger.mergeMode == MergeMode.ArmatureMerge)
            {
                var smrs = merger.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No SkinnedMeshRenderers found. Consider using Model Merge mode instead.",
                        MessageType.Warning);
                }
            }
        }
        
        #region Bone Conflict Detection
        
        private void DetectBoneConflicts(CVRMergeArmature merger)
        {
            merger.boneConflicts.Clear();
            
            Component targetCVRAvatar = merger.GetTargetCVRAvatar();
            if (targetCVRAvatar == null)
            {
                targetCVRAvatar = FindCVRAvatarInScene();
            }
            
            if (targetCVRAvatar == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find target CVRAvatar. Please set a GameObject with CVRAvatar component.", "OK");
                return;
            }
            
            Transform targetArmature = FindArmatureFromCVRAvatar(targetCVRAvatar);
            if (targetArmature == null)
            {
                EditorUtility.DisplayDialog("Error", "Could not find armature in target CVRAvatar.", "OK");
                return;
            }
            
            DetectConflictsRecursive(merger, merger.transform, targetArmature);
            
            EditorUtility.SetDirty(merger);
            
            if (merger.boneConflicts.Count == 0)
            {
                EditorUtility.DisplayDialog("Success", "No bone conflicts detected!", "OK");
            }
        }
        
        private void DetectConflictsRecursive(CVRMergeArmature merger, Transform source, Transform target)
        {
            if (merger.IsExcluded(source))
                return;
            
            string boneName = source.name;
            
            if (!string.IsNullOrEmpty(merger.prefix) && boneName.StartsWith(merger.prefix))
                boneName = boneName.Substring(merger.prefix.Length);
            if (!string.IsNullOrEmpty(merger.suffix) && boneName.EndsWith(merger.suffix))
                boneName = boneName.Substring(0, boneName.Length - merger.suffix.Length);
            
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
                DetectConflictsRecursive(merger, child, target);
            }
        }
        
        #endregion
        
        #region Broken Reference Detection
        
        private void CheckBrokenAnimatorReferences(CVRMergeArmature merger)
        {
            merger.brokenReferences.Clear();
            
            var animator = merger.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                EditorUtility.DisplayDialog("Info", "No Animator found on this object.", "OK");
                return;
            }
            
            var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Error", "Animator controller is not an AnimatorController.", "OK");
                return;
            }
            
            CheckAnimatorController(controller, merger);
            
            EditorUtility.SetDirty(merger);
            
            if (merger.brokenReferences.Count == 0)
            {
                EditorUtility.DisplayDialog("Success", "No broken references found!", "OK");
            }
            else
            {
                Debug.LogWarning($"Found {merger.brokenReferences.Count} broken animator references");
            }
        }
        
        private void CheckAnimatorController(UnityEditor.Animations.AnimatorController controller, CVRMergeArmature merger)
        {
            foreach (var layer in controller.layers)
            {
                CheckStateMachine(layer.stateMachine, merger);
            }
        }
        
        private void CheckStateMachine(UnityEditor.Animations.AnimatorStateMachine stateMachine, CVRMergeArmature merger)
        {
            foreach (var state in stateMachine.states)
            {
                if (state.state.motion is AnimationClip clip)
                {
                    CheckAnimationClip(clip, merger);
                }
                else if (state.state.motion is UnityEditor.Animations.BlendTree blendTree)
                {
                    CheckBlendTree(blendTree, merger);
                }
            }
            
            foreach (var subMachine in stateMachine.stateMachines)
            {
                CheckStateMachine(subMachine.stateMachine, merger);
            }
        }
        
        private void CheckBlendTree(UnityEditor.Animations.BlendTree blendTree, CVRMergeArmature merger)
        {
            foreach (var child in blendTree.children)
            {
                if (child.motion is AnimationClip clip)
                {
                    CheckAnimationClip(clip, merger);
                }
                else if (child.motion is UnityEditor.Animations.BlendTree subTree)
                {
                    CheckBlendTree(subTree, merger);
                }
            }
        }
        
        private void CheckAnimationClip(AnimationClip clip, CVRMergeArmature merger)
        {
            if (clip == null) return;
            
            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in bindings)
            {
                var transform = merger.transform.Find(binding.path);
                if (transform == null && !string.IsNullOrEmpty(binding.path))
                {
                    var pathParts = binding.path.Split('/');
                    var targetName = pathParts[pathParts.Length - 1];
                    
                    var found = FindTransformByName(merger.transform, targetName);
                    
                    merger.brokenReferences.Add(new BrokenAnimatorReference
                    {
                        originalPath = binding.path,
                        suggestedPath = found != null ? GetRelativePath(merger.transform, found) : "",
                        componentReference = clip,
                        fieldName = binding.propertyName,
                        autoFixed = false
                    });
                }
            }
        }
        
        private void AutoFixAllReferences(CVRMergeArmature merger)
        {
            foreach (var brokenRef in merger.brokenReferences)
            {
                if (!string.IsNullOrEmpty(brokenRef.suggestedPath))
                {
                    FixAnimatorReference(merger, brokenRef);
                }
            }
            
            EditorUtility.DisplayDialog("Success", "Auto-fixed all references with suggestions!", "OK");
        }
        
        private void FixAnimatorReference(CVRMergeArmature merger, BrokenAnimatorReference brokenRef)
        {
            if (brokenRef.componentReference is AnimationClip clip)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                foreach (var binding in bindings)
                {
                    if (binding.path == brokenRef.originalPath && binding.propertyName == brokenRef.fieldName)
                    {
                        var curve = AnimationUtility.GetEditorCurve(clip, binding);
                        AnimationUtility.SetEditorCurve(clip, binding, null);
                        
                        var newBinding = binding;
                        var pathField = typeof(EditorCurveBinding).GetField("m_Path", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        pathField?.SetValue(newBinding, brokenRef.suggestedPath);
                        
                        AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                        
                        brokenRef.autoFixed = true;
                        EditorUtility.SetDirty(clip);
                        Debug.Log($"Fixed reference: {brokenRef.originalPath} -> {brokenRef.suggestedPath}");
                    }
                }
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private Component FindCVRAvatarInScene()
        {
            var cvrAvatarType = System.Type.GetType("ABI.CCK.Components.CVRAvatar, Assembly-CSharp");
            if (cvrAvatarType == null) return null;
            
            var allAvatars = FindObjectsOfType(cvrAvatarType);
            return allAvatars.Length > 0 ? allAvatars[0] as Component : null;
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
        
        private Transform FindTransformByName(Transform root, string name)
        {
            if (root.name == name) return root;
            
            foreach (Transform child in root)
            {
                var found = FindTransformByName(child, name);
                if (found != null) return found;
            }
            
            return null;
        }
        
        private string GetRelativePath(Transform root, Transform target)
        {
            var path = new List<string>();
            var current = target;
            
            while (current != root && current != null)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }
            
            return string.Join("/", path);
        }
        
        #endregion
    }
}
