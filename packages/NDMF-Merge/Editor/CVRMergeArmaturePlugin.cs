using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using UnityEditor.Animations;
using nadena.dev.ndmf;
using NDMFMerge.Runtime;

[assembly: ExportsPlugin(typeof(NDMFMerge.Editor.CVRMergeArmaturePlugin))]

namespace NDMFMerge.Editor
{
    public class CVRMergeArmaturePlugin : Plugin<CVRMergeArmaturePlugin>
    {
        public override string QualifiedName => "dev.milchzocker.ndmf-merge";
        public override string DisplayName => "NDMF Merge";
        
        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving)
                .Run("Merge Armatures", ctx =>
                {
                    var mergeComponents = ctx.AvatarRootTransform
                        .GetComponentsInChildren<CVRMergeArmature>(true);
                    
                    foreach (var merge in mergeComponents)
                    {
                        try
                        {
                            if (merge.mergeMode == MergeMode.ArmatureMerge)
                            {
                                MergeArmature(ctx, merge);
                            }
                            else
                            {
                                MergeModel(ctx, merge);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Failed to merge {merge.gameObject.name}: {ex.Message}\n{ex.StackTrace}", merge);
                        }
                    }
                });
            
            InPhase(BuildPhase.Transforming)
                .Run("Merge Animators", ctx =>
                {
                    var mergeComponents = ctx.AvatarRootTransform
                        .GetComponentsInChildren<CVRMergeArmature>(true);
                    
                    foreach (var merge in mergeComponents)
                    {
                        if (merge.mergeAnimator)
                        {
                            MergeAnimator(ctx, merge);
                        }
                    }
                });
        }
        
        #region Model Merge
        
        private void MergeModel(BuildContext ctx, CVRMergeArmature merger)
        {
            Debug.Log($"[NDMF Merge] Model Merge: {merger.gameObject.name} -> {merger.targetBone?.name}");
            
            if (merger.targetBone == null)
            {
                Debug.LogError($"[NDMF Merge] Model Merge requires a target bone!", merger);
                return;
            }
            
            Vector3 finalPosition;
            Quaternion finalRotation;
            
            if (merger.useCurrentOffset)
            {
                var targetWorld = merger.targetBone.localToWorldMatrix;
                var mergerWorld = merger.transform.localToWorldMatrix;
                var relativeMatrix = targetWorld.inverse * mergerWorld;
                
                finalPosition = relativeMatrix.GetColumn(3);
                finalRotation = Quaternion.LookRotation(
                    relativeMatrix.GetColumn(2),
                    relativeMatrix.GetColumn(1)
                );
            }
            else
            {
                finalPosition = merger.positionOffset;
                finalRotation = Quaternion.Euler(merger.rotationOffset);
            }
            
            merger.transform.SetParent(merger.targetBone, false);
            merger.transform.localPosition = finalPosition;
            merger.transform.localRotation = finalRotation;
            
            Debug.Log($"[NDMF Merge] Model attached with offset: pos={finalPosition}, rot={finalRotation.eulerAngles}");
            
            MergeDynamicComponents(merger, new Dictionary<Transform, Transform>());
            MergeCVRComponents(ctx, merger);
            
            UnityEngine.Object.DestroyImmediate(merger);
        }
        
        #endregion
        
        #region Armature Merge
        
        private void MergeArmature(BuildContext ctx, CVRMergeArmature merger)
        {
            Debug.Log($"[NDMF Merge] Armature Merge: {merger.gameObject.name}");
            
            Component targetCVRAvatar = merger.GetTargetCVRAvatar();
            if (targetCVRAvatar == null)
            {
                targetCVRAvatar = FindCVRAvatarComponent(ctx.AvatarRootTransform);
            }
            
            if (targetCVRAvatar == null)
            {
                Debug.LogError($"[NDMF Merge] Could not find target CVRAvatar!", merger);
                return;
            }
            
            Transform targetArmature = FindArmatureFromCVRAvatar(targetCVRAvatar);
            
            if (targetArmature == null)
            {
                Debug.LogError($"[NDMF Merge] Could not find armature in target CVRAvatar!", merger);
                return;
            }
            
            Debug.Log($"[NDMF Merge] Target armature: {targetArmature.name}");
            
            var boneMap = BuildBoneMappingWithConflicts(merger, targetArmature);
            Debug.Log($"[NDMF Merge] Mapped {boneMap.Count} bones");
            
            var skinnedMeshes = merger.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinnedMeshes)
            {
                if (!merger.IsExcluded(smr.transform))
                {
                    RemapSkinnedMeshRenderer(smr, boneMap, targetArmature);
                }
            }
            Debug.Log($"[NDMF Merge] Remapped {skinnedMeshes.Length} SkinnedMeshRenderers");
            
            var constraints = merger.GetComponentsInChildren<IConstraint>(true);
            foreach (var constraint in constraints)
            {
                if (!merger.IsExcluded(((Component)constraint).transform))
                {
                    RemapConstraint(constraint, boneMap);
                }
            }
            
            MergeDynamicComponents(merger, boneMap);
            MergeCVRComponents(ctx, merger);
            MoveNonBoneChildren(merger.transform, ctx.AvatarRootTransform, boneMap, merger);
            CleanupMergedBones(merger.transform, boneMap, merger);
            
            Debug.Log($"[NDMF Merge] Completed merging {merger.gameObject.name}");
        }
        
        private Transform FindArmatureFromCVRAvatar(Component cvrAvatar)
        {
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
        
        private Dictionary<Transform, Transform> BuildBoneMappingWithConflicts(CVRMergeArmature merger, Transform target)
        {
            var mapping = new Dictionary<Transform, Transform>();
            
            void MapBone(Transform sourceBone)
            {
                if (merger.IsExcluded(sourceBone))
                {
                    Debug.Log($"  Excluded: {sourceBone.name}");
                    return;
                }
                
                string boneName = sourceBone.name;
                
                if (!string.IsNullOrEmpty(merger.prefix) && boneName.StartsWith(merger.prefix))
                    boneName = boneName.Substring(merger.prefix.Length);
                if (!string.IsNullOrEmpty(merger.suffix) && boneName.EndsWith(merger.suffix))
                    boneName = boneName.Substring(0, boneName.Length - merger.suffix.Length);
                
                Transform targetBone = FindBoneByName(target, boneName);
                
                if (targetBone != null)
                {
                    var (hasConflict, posDiff, rotDiff, scaleDiff) = CheckTransformConflict(
                        sourceBone, targetBone, merger.conflictThreshold);
                    
                    if (hasConflict)
                    {
                        var resolution = merger.defaultBoneConflictResolution;
                        var conflictEntry = merger.boneConflicts.Find(c => 
                            c.sourceBone == sourceBone && c.targetBone == targetBone);
                        
                        if (conflictEntry != null)
                        {
                            resolution = conflictEntry.resolution;
                        }
                        
                        switch (resolution)
                        {
                            case BoneConflictResolution.StillMerge:
                                mapping[sourceBone] = targetBone;
                                Debug.LogWarning($"  Conflict (merged anyway): {sourceBone.name} -> {targetBone.name}");
                                break;
                                
                            case BoneConflictResolution.Rename:
                                sourceBone.name = sourceBone.name + "_Merged";
                                Debug.LogWarning($"  Conflict (renamed): {sourceBone.name}");
                                break;
                                
                            case BoneConflictResolution.DontMerge:
                                Debug.LogWarning($"  Conflict (skipped): {sourceBone.name}");
                                break;
                        }
                    }
                    else
                    {
                        mapping[sourceBone] = targetBone;
                        Debug.Log($"  Mapped: {sourceBone.name} -> {targetBone.name}");
                    }
                }
                else
                {
                    Debug.LogWarning($"  No match: {sourceBone.name} (looking for: {boneName})");
                }
                
                foreach (Transform child in sourceBone)
                {
                    MapBone(child);
                }
            }
            
            MapBone(merger.transform);
            return mapping;
        }
        
        private (bool hasConflict, Vector3 posDiff, float rotDiff, Vector3 scaleDiff) CheckTransformConflict(
            Transform source, Transform target, float threshold)
        {
            Vector3 posDiff = source.position - target.position;
            float rotDiff = Quaternion.Angle(source.rotation, target.rotation);
            Vector3 scaleDiff = source.lossyScale - target.lossyScale;
            
            bool hasConflict = posDiff.magnitude > threshold || 
                              rotDiff > threshold * 57.3f || 
                              scaleDiff.magnitude > threshold;
            
            return (hasConflict, posDiff, rotDiff, scaleDiff);
        }
        
        #endregion
        
        #region CVR Component Merging
        
        private void MergeCVRComponents(BuildContext ctx, CVRMergeArmature merger)
        {
            Component targetCVRAvatar = merger.GetTargetCVRAvatar();
            if (targetCVRAvatar == null)
            {
                targetCVRAvatar = FindCVRAvatarComponent(ctx.AvatarRootTransform);
            }
            
            if (targetCVRAvatar == null)
            {
                Debug.LogWarning("[NDMF Merge] No target CVRAvatar found for CVR component merging");
                return;
            }
            
            if (merger.mergeAdvancedAvatarSetup)
            {
                MergeAdvancedAvatarSettings(merger, targetCVRAvatar);
            }
            
            if (merger.mergeAdvancedPointerTrigger)
            {
                MergeAdvancedPointerTrigger(merger, targetCVRAvatar);
            }
            
            if (merger.mergeParameterStream)
            {
                MergeParameterStream(merger, targetCVRAvatar);
            }
            
            if (merger.mergeAnimatorDriver)
            {
                MergeAnimatorDriver(merger, targetCVRAvatar);
            }
        }
        
        private void MergeAdvancedAvatarSettings(CVRMergeArmature merger, Component targetCVRAvatar)
        {
            Debug.Log("[NDMF Merge] Merging Advanced Avatar Settings");
            
            var sourceCVRAvatar = FindCVRAvatarComponent(merger.transform);
            if (sourceCVRAvatar == null)
            {
                Debug.LogWarning("[NDMF Merge] No source CVRAvatar found");
                return;
            }
            
            var sourceSettings = GetAdvancedAvatarSettings(sourceCVRAvatar);
            var targetSettings = GetAdvancedAvatarSettings(targetCVRAvatar);
            
            if (sourceSettings == null)
            {
                Debug.Log("[NDMF Merge] Source has no Advanced Avatar Settings");
                return;
            }
            
            if (targetSettings == null)
            {
                targetSettings = CreateAdvancedAvatarSettings(targetCVRAvatar);
            }
            
            var sourceList = GetSettingsList(sourceSettings);
            var targetList = GetSettingsList(targetSettings);
            
            if (sourceList == null || targetList == null)
            {
                Debug.LogError("[NDMF Merge] Failed to access settings lists");
                return;
            }
            
            int mergedCount = 0;
            
            foreach (var sourceEntry in sourceList)
            {
                if (sourceEntry == null) continue;
                
                var machineName = GetEntryMachineName(sourceEntry);
                if (string.IsNullOrEmpty(machineName)) continue;
                
                if (!string.IsNullOrEmpty(merger.advancedSettingsPrefix))
                {
                    SetEntryMachineName(sourceEntry, merger.advancedSettingsPrefix + machineName);
                    var entryName = GetEntryName(sourceEntry);
                    if (!string.IsNullOrEmpty(entryName))
                    {
                        SetEntryName(sourceEntry, merger.advancedSettingsPrefix + entryName);
                    }
                }
                
                bool hasConflict = false;
                foreach (var targetEntry in targetList)
                {
                    if (GetEntryMachineName(targetEntry) == GetEntryMachineName(sourceEntry))
                    {
                        hasConflict = true;
                        Debug.LogWarning($"[NDMF Merge] Skipping duplicate entry: {GetEntryMachineName(sourceEntry)}");
                        break;
                    }
                }
                
                if (!hasConflict)
                {
                    targetList.Add(sourceEntry);
                    mergedCount++;
                }
            }
            
            Debug.Log($"[NDMF Merge] Merged {mergedCount} Advanced Avatar Settings entries");
            SetSettingsInitialized(targetSettings, false);
        }
        
        private void MergeAdvancedPointerTrigger(CVRMergeArmature merger, Component targetCVRAvatar)
        {
            Debug.Log("[NDMF Merge] Merging Advanced Avatar Pointer/Trigger");
            
            var pointerType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRPointer");
            var advancedTriggerType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAdvancedAvatarSettingsTrigger");
            
            int mergedCount = 0;
            
            if (pointerType != null)
            {
                var pointers = merger.GetComponentsInChildren(pointerType, true);
                mergedCount += pointers.Length;
            }
            
            if (advancedTriggerType != null)
            {
                var triggers = merger.GetComponentsInChildren(advancedTriggerType, true);
                mergedCount += triggers.Length;
            }
            
            if (mergedCount > 0)
                Debug.Log($"[NDMF Merge] Preserved {mergedCount} Pointer/Trigger components");
        }
        
        private void MergeParameterStream(CVRMergeArmature merger, Component targetCVRAvatar)
        {
            Debug.Log("[NDMF Merge] Merging Parameter Stream");
            
            var paramStreamType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRParameterStream");
            if (paramStreamType == null) return;
            
            var sourceStreams = merger.GetComponentsInChildren(paramStreamType, true);
            if (sourceStreams.Length == 0) return;
            
            var targetStream = targetCVRAvatar.GetComponent(paramStreamType);
            if (targetStream == null)
            {
                targetStream = targetCVRAvatar.gameObject.AddComponent(paramStreamType);
                Debug.Log("[NDMF Merge] Created new CVRParameterStream on target");
            }
            
            foreach (var sourceStream in sourceStreams)
            {
                MergeParameterStreamEntries(sourceStream, targetStream);
            }
        }
        
        private void MergeParameterStreamEntries(Component source, Component target)
        {
            var entriesField = source.GetType().GetField("entries");
            if (entriesField == null) return;
            
            var sourceEntries = entriesField.GetValue(source) as System.Collections.IList;
            var targetEntries = entriesField.GetValue(target) as System.Collections.IList;
            
            if (sourceEntries == null || targetEntries == null) return;
            
            foreach (var entry in sourceEntries)
            {
                if (entry != null)
                {
                    targetEntries.Add(entry);
                }
            }
            
            Debug.Log($"[NDMF Merge] Merged {sourceEntries.Count} parameter stream entries");
        }
        
        private void MergeAnimatorDriver(CVRMergeArmature merger, Component targetCVRAvatar)
        {
            Debug.Log("[NDMF Merge] Merging Animator Driver");
            
            var animatorDriverType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAnimatorDriver");
            if (animatorDriverType == null) return;
            
            var sourceDrivers = merger.GetComponentsInChildren(animatorDriverType, true);
            if (sourceDrivers.Length == 0) return;
            
            var targetDrivers = targetCVRAvatar.GetComponents(animatorDriverType);
            Component targetDriver = targetDrivers.Length > 0 ? targetDrivers[0] : null;
            
            if (targetDriver == null)
            {
                targetDriver = targetCVRAvatar.gameObject.AddComponent(animatorDriverType);
                Debug.Log("[NDMF Merge] Created new CVRAnimatorDriver on target");
            }
            
            foreach (var sourceDriver in sourceDrivers)
            {
                MergeAnimatorDriverEntries(sourceDriver, targetDriver, targetCVRAvatar);
            }
        }
        
        private void MergeAnimatorDriverEntries(Component source, Component target, Component targetCVRAvatar)
        {
            var entriesField = source.GetType().GetField("entries");
            if (entriesField == null) return;
            
            var sourceEntries = entriesField.GetValue(source) as System.Collections.IList;
            var targetEntries = entriesField.GetValue(target) as System.Collections.IList;
            
            if (sourceEntries == null || targetEntries == null) return;
            
            var animatorDriverType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAnimatorDriver");
            
            foreach (var entry in sourceEntries)
            {
                if (entry == null) continue;
                
                var driversField = entry.GetType().GetField("parameterDrivers");
                var drivers = driversField?.GetValue(entry) as System.Collections.IList;
                
                if (drivers != null && drivers.Count > 16)
                {
                    Debug.LogWarning($"[NDMF Merge] Entry has {drivers.Count} parameters, splitting into multiple drivers");
                    
                    for (int i = 0; i < drivers.Count; i += 16)
                    {
                        var chunk = new List<object>();
                        for (int j = i; j < Math.Min(i + 16, drivers.Count); j++)
                        {
                            chunk.Add(drivers[j]);
                        }
                        
                        var newDriver = targetCVRAvatar.gameObject.AddComponent(animatorDriverType);
                        var newEntries = entriesField.GetValue(newDriver) as System.Collections.IList;
                        
                        var clonedEntry = CloneObject(entry);
                        var clonedDriversField = clonedEntry.GetType().GetField("parameterDrivers");
                        var clonedDriversList = Activator.CreateInstance(typeof(List<>).MakeGenericType(drivers[0].GetType()));
                        foreach (var driver in chunk)
                        {
                            ((System.Collections.IList)clonedDriversList).Add(driver);
                        }
                        clonedDriversField?.SetValue(clonedEntry, clonedDriversList);
                        
                        newEntries.Add(clonedEntry);
                    }
                }
                else
                {
                    targetEntries.Add(entry);
                }
            }
            
            Debug.Log($"[NDMF Merge] Merged animator driver entries");
        }
        
        private object CloneObject(object obj)
        {
            var type = obj.GetType();
            var clone = Activator.CreateInstance(type);
            
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | 
                                                 System.Reflection.BindingFlags.NonPublic | 
                                                 System.Reflection.BindingFlags.Instance))
            {
                field.SetValue(clone, field.GetValue(obj));
            }
            
            return clone;
        }
        
        #endregion
        
        #region Animator Merge
        
        private void MergeAnimator(BuildContext ctx, CVRMergeArmature merger)
        {
            Debug.Log($"[NDMF Merge] Starting Animator Merge for {merger.gameObject.name}");
            
            DetectAndFixBrokenAnimatorReferences(merger);
            
            RuntimeAnimatorController sourceController = merger.animatorToMerge;
            
            if (sourceController == null)
            {
                var sourceCVRAvatar = FindCVRAvatarComponent(merger.transform);
                if (sourceCVRAvatar != null)
                {
                    sourceController = GetAnimatorFromCVRAvatar(sourceCVRAvatar);
                }
                
                if (sourceController == null)
                {
                    var animator = merger.GetComponent<Animator>();
                    if (animator != null)
                    {
                        sourceController = animator.runtimeAnimatorController;
                    }
                }
            }
            
            if (sourceController == null)
            {
                Debug.Log("[NDMF Merge] No animator to merge");
                return;
            }
            
            Component targetCVRAvatar = merger.GetTargetCVRAvatar();
            if (targetCVRAvatar == null)
            {
                targetCVRAvatar = FindCVRAvatarComponent(ctx.AvatarRootTransform);
            }
            
            if (targetCVRAvatar == null)
            {
                Debug.LogError("[NDMF Merge] No target CVRAvatar found for animator merge");
                return;
            }
            
            var targetAnimator = targetCVRAvatar.GetComponent<Animator>();
            if (targetAnimator == null)
            {
                Debug.LogError("[NDMF Merge] Target CVRAvatar has no Animator component");
                return;
            }
            
            var baseController = targetAnimator.runtimeAnimatorController as AnimatorController;
            if (baseController == null)
            {
                Debug.LogError("[NDMF Merge] Target animator is not an AnimatorController");
                return;
            }
            
            var mergeController = sourceController as AnimatorController;
            if (mergeController == null)
            {
                Debug.LogError("[NDMF Merge] Source animator is not an AnimatorController");
                return;
            }
            
            var newController = UnityEngine.Object.Instantiate(baseController);
            newController.name = baseController.name + "_Merged";
            
            foreach (var layer in mergeController.layers)
            {
                var newLayer = new AnimatorControllerLayer
                {
                    name = $"{merger.gameObject.name}_{layer.name}",
                    stateMachine = UnityEngine.Object.Instantiate(layer.stateMachine),
                    avatarMask = layer.avatarMask,
                    blendingMode = layer.blendingMode,
                    defaultWeight = layer.defaultWeight,
                    syncedLayerIndex = layer.syncedLayerIndex,
                    iKPass = layer.iKPass
                };
                
                newController.AddLayer(newLayer);
            }
            
            foreach (var param in mergeController.parameters)
            {
                if (!newController.parameters.Any(p => p.name == param.name))
                {
                    newController.AddParameter(param.name, param.type);
                }
            }
            
            targetAnimator.runtimeAnimatorController = newController;
            
            Debug.Log($"[NDMF Merge] Merged {mergeController.layers.Length} animator layers and {mergeController.parameters.Length} parameters");
        }
        
        private RuntimeAnimatorController GetAnimatorFromCVRAvatar(Component cvrAvatar)
        {
            var overridesField = cvrAvatar.GetType().GetField("overrides");
            if (overridesField != null)
            {
                var overrides = overridesField.GetValue(cvrAvatar) as RuntimeAnimatorController;
                if (overrides != null) return overrides;
            }
            
            var settingsField = cvrAvatar.GetType().GetField("avatarSettings");
            if (settingsField != null)
            {
                var settings = settingsField.GetValue(cvrAvatar);
                if (settings != null)
                {
                    var baseControllerField = settings.GetType().GetField("baseController");
                    if (baseControllerField != null)
                    {
                        return baseControllerField.GetValue(settings) as RuntimeAnimatorController;
                    }
                }
            }
            
            return null;
        }
        
        private void DetectAndFixBrokenAnimatorReferences(CVRMergeArmature merger)
        {
            merger.brokenReferences.Clear();
            
            var animator = merger.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return;
            
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null) return;
            
            foreach (var layer in controller.layers)
            {
                CheckStateMachine(layer.stateMachine, merger);
            }
            
            if (merger.brokenReferences.Count > 0)
            {
                Debug.LogWarning($"[NDMF Merge] Found {merger.brokenReferences.Count} broken animator references");
            }
        }
        
        private void CheckStateMachine(AnimatorStateMachine stateMachine, CVRMergeArmature merger)
        {
            foreach (var state in stateMachine.states)
            {
                if (state.state.motion is AnimationClip clip)
                {
                    CheckAnimationClip(clip, merger);
                }
                else if (state.state.motion is BlendTree blendTree)
                {
                    CheckBlendTree(blendTree, merger);
                }
            }
            
            foreach (var subMachine in stateMachine.stateMachines)
            {
                CheckStateMachine(subMachine.stateMachine, merger);
            }
        }
        
        private void CheckBlendTree(BlendTree blendTree, CVRMergeArmature merger)
        {
            foreach (var child in blendTree.children)
            {
                if (child.motion is AnimationClip clip)
                {
                    CheckAnimationClip(clip, merger);
                }
                else if (child.motion is BlendTree subTree)
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
        
        #region Dynamic Component Merging
        
        private void MergeDynamicComponents(CVRMergeArmature merger, Dictionary<Transform, Transform> boneMap)
        {
            if (merger.mergeDynamicBones)
            {
                var dynamicBoneType = FindTypeInLoadedAssemblies("DynamicBone");
                if (dynamicBoneType != null)
                {
                    var dynamicBones = merger.GetComponentsInChildren(dynamicBoneType, true);
                    foreach (var db in dynamicBones)
                    {
                        RemapComponentTransformField(db, "m_Root", boneMap);
                        RemapComponentTransformList(db, "m_Exclusions", boneMap);
                    }
                    if (dynamicBones.Length > 0)
                        Debug.Log($"[NDMF Merge] Remapped {dynamicBones.Length} DynamicBones");
                }
            }
            
            if (merger.mergeMagicaCloth)
            {
                var magicaClothType = FindTypeInLoadedAssemblies("MagicaCloth.MagicaCloth");
                if (magicaClothType != null)
                {
                    var magicaCloths = merger.GetComponentsInChildren(magicaClothType, true);
                    foreach (var mc in magicaCloths)
                    {
                        RemapComponentTransformList(mc, "clothTarget", boneMap);
                    }
                    if (magicaCloths.Length > 0)
                        Debug.Log($"[NDMF Merge] Remapped {magicaCloths.Length} MagicaCloth v1 components");
                }
            }
            
            if (merger.mergeMagicaCloth)
            {
                var magicaCloth2Type = FindTypeInLoadedAssemblies("MagicaCloth2.MagicaCloth");
                if (magicaCloth2Type != null)
                {
                    var magicaCloths2 = merger.GetComponentsInChildren(magicaCloth2Type, true);
                    if (magicaCloths2.Length > 0)
                        Debug.Log($"[NDMF Merge] Found {magicaCloths2.Length} MagicaCloth v2 components");
                }
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private Component FindCVRAvatarComponent(Transform root)
        {
            var cvrAvatarType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAvatar");
            if (cvrAvatarType == null) return null;
            
            var current = root;
            while (current != null)
            {
                var component = current.GetComponent(cvrAvatarType);
                if (component != null) return component;
                current = current.parent;
            }
            
            return null;
        }
        
        private object GetAdvancedAvatarSettings(Component cvrAvatar)
        {
            if (cvrAvatar == null) return null;
            var field = cvrAvatar.GetType().GetField("avatarSettings");
            return field?.GetValue(cvrAvatar);
        }
        
        private object CreateAdvancedAvatarSettings(Component cvrAvatar)
        {
            var settingsType = FindTypeInLoadedAssemblies("ABI.CCK.Scripts.CVRAdvancedAvatarSettings");
            if (settingsType == null) return null;
            
            var newSettings = Activator.CreateInstance(settingsType);
            var field = cvrAvatar.GetType().GetField("avatarSettings");
            field?.SetValue(cvrAvatar, newSettings);
            
            return newSettings;
        }
        
        private System.Collections.IList GetSettingsList(object advancedSettings)
        {
            if (advancedSettings == null) return null;
            var field = advancedSettings.GetType().GetField("settings");
            return field?.GetValue(advancedSettings) as System.Collections.IList;
        }
        
        private string GetEntryMachineName(object entry)
        {
            var field = entry.GetType().GetField("machineName");
            return field?.GetValue(entry) as string;
        }
        
        private void SetEntryMachineName(object entry, string value)
        {
            var field = entry.GetType().GetField("machineName");
            field?.SetValue(entry, value);
        }
        
        private string GetEntryName(object entry)
        {
            var field = entry.GetType().GetField("name");
            return field?.GetValue(entry) as string;
        }
        
        private void SetEntryName(object entry, string value)
        {
            var field = entry.GetType().GetField("name");
            field?.SetValue(entry, value);
        }
        
        private void SetSettingsInitialized(object advancedSettings, bool value)
        {
            var field = advancedSettings.GetType().GetField("initialized");
            field?.SetValue(advancedSettings, value);
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
        
        private void RemapSkinnedMeshRenderer(SkinnedMeshRenderer smr, 
            Dictionary<Transform, Transform> boneMap, Transform newRoot)
        {
            var bones = smr.bones;
            var newBones = new Transform[bones.Length];
            bool changed = false;
            
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && boneMap.TryGetValue(bones[i], out var mappedBone))
                {
                    newBones[i] = mappedBone;
                    changed = true;
                }
                else
                {
                    newBones[i] = bones[i];
                }
            }
            
            if (changed)
            {
                smr.bones = newBones;
            }
            
            if (smr.rootBone != null && boneMap.TryGetValue(smr.rootBone, out var newRootBone))
            {
                smr.rootBone = newRootBone;
            }
        }
        
        private void RemapConstraint(IConstraint constraint, Dictionary<Transform, Transform> boneMap)
        {
            for (int i = 0; i < constraint.sourceCount; i++)
            {
                var source = constraint.GetSource(i);
                if (source.sourceTransform != null && 
                    boneMap.TryGetValue(source.sourceTransform, out var mapped))
                {
                    source.sourceTransform = mapped;
                    constraint.SetSource(i, source);
                }
            }
        }
        
        private void RemapComponentTransformField(Component component, string fieldName, 
            Dictionary<Transform, Transform> boneMap)
        {
            var type = component.GetType();
            var field = type.GetField(fieldName, 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            if (field != null && field.FieldType == typeof(Transform))
            {
                var currentTransform = field.GetValue(component) as Transform;
                if (currentTransform != null && boneMap.TryGetValue(currentTransform, out var mapped))
                {
                    field.SetValue(component, mapped);
                }
            }
        }
        
        private void RemapComponentTransformList(Component component, string fieldName, 
            Dictionary<Transform, Transform> boneMap)
        {
            var type = component.GetType();
            var field = type.GetField(fieldName, 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            if (field != null && typeof(System.Collections.IList).IsAssignableFrom(field.FieldType))
            {
                var list = field.GetValue(component) as System.Collections.IList;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is Transform t && boneMap.TryGetValue(t, out var mapped))
                        {
                            list[i] = mapped;
                        }
                    }
                }
            }
        }
        
        private Type FindTypeInLoadedAssemblies(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null) return type;
            }
            return null;
        }
        
        private void MoveNonBoneChildren(Transform mergedRoot, Transform avatarRoot, 
            Dictionary<Transform, Transform> boneMap, CVRMergeArmature merger)
        {
            var toMove = new List<Transform>();
            
            foreach (Transform child in mergedRoot)
            {
                if (!boneMap.ContainsKey(child) && 
                    !merger.IsExcluded(child) && 
                    ShouldPreserveHierarchy(child))
                {
                    toMove.Add(child);
                }
            }
            
            foreach (var child in toMove)
            {
                child.SetParent(avatarRoot, true);
            }
        }
        
        private bool ShouldPreserveHierarchy(Transform transform)
        {
            var components = transform.GetComponentsInChildren<Component>(true);
            return components.Any(c => 
                c != null && 
                !(c is Transform) && 
                !(c is CVRMergeArmature));
        }
        
        private void CleanupMergedBones(Transform mergedRoot, Dictionary<Transform, Transform> boneMap, 
            CVRMergeArmature merger)
        {
            var mergeComponent = mergedRoot.GetComponent<CVRMergeArmature>();
            if (mergeComponent != null)
            {
                UnityEngine.Object.DestroyImmediate(mergeComponent);
            }
            
            var toCleanup = new List<GameObject>();
            
            void CheckBone(Transform bone)
            {
                if (merger.IsExcluded(bone))
                    return;
                
                var components = bone.GetComponents<Component>();
                bool canDelete = boneMap.ContainsKey(bone) && 
                    components.All(c => c is Transform || c == null);
                
                if (canDelete && bone.childCount == 0)
                {
                    toCleanup.Add(bone.gameObject);
                }
                
                var children = new List<Transform>();
                foreach (Transform child in bone)
                {
                    children.Add(child);
                }
                
                foreach (var child in children)
                {
                    CheckBone(child);
                }
            }
            
            CheckBone(mergedRoot);
            
            foreach (var obj in toCleanup)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
        
        #endregion
    }
}
