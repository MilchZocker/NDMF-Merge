using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        
        private StringBuilder mergeLog = new StringBuilder();
        
        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving)
                .Run("Merge Armatures", ctx =>
                {
                    mergeLog.Clear();
                    mergeLog.AppendLine("========== NDMF MERGE LOG ==========");
                    
                    var mergeComponents = ctx.AvatarRootTransform.GetComponentsInChildren<CVRMergeArmature>(true);
                    
                    mergeLog.AppendLine($"Found {mergeComponents.Length} merge components for {ctx.AvatarRootTransform.name}");
                    
                    foreach (var merger in mergeComponents)
                    {
                        if (merger == null) continue;
                        
                        try
                        {
                            ProcessMerger(ctx, merger);
                        }
                        catch (Exception ex)
                        {
                            mergeLog.AppendLine($"ERROR: Failed to process merger: {ex.Message}");
                            Debug.LogError($"[NDMF Merge] Failed to process merger: {ex.Message}\n{ex.StackTrace}", merger);
                        }
                    }
                    
                    mergeLog.AppendLine("========== END MERGE LOG ==========");
                    Debug.Log(mergeLog.ToString());
                });
            
            InPhase(BuildPhase.Transforming)
                .Run("Merge Animators", ctx =>
                {
                    var mergeComponents = ctx.AvatarRootTransform.GetComponentsInChildren<CVRMergeArmature>(true);
                    
                    foreach (var merger in mergeComponents)
                    {
                        if (merger == null || !merger.mergeAnimator) continue;
                        
                        try
                        {
                            MergeAnimators(ctx, merger);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[NDMF Merge] Failed to merge animators: {ex.Message}\n{ex.StackTrace}", merger);
                        }
                    }
                });
        }
        
        private void ProcessMerger(BuildContext ctx, CVRMergeArmature merger)
        {
            var cvrAvatar = merger.GetCVRAvatar();
            if (cvrAvatar == null)
            {
                mergeLog.AppendLine("ERROR: No CVRAvatar found on avatar!");
                return;
            }
            
            Transform targetArmature = FindArmatureFromCVRAvatar(cvrAvatar);
            if (targetArmature == null)
            {
                mergeLog.AppendLine("ERROR: Could not find armature in CVRAvatar!");
                return;
            }
            
            mergeLog.AppendLine($"Target armature: {targetArmature.name}");
            
            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;
                
                try
                {
                    var clonedOutfit = UnityEngine.Object.Instantiate(outfitEntry.outfit);
                    clonedOutfit.name = outfitEntry.outfit.name;
                    clonedOutfit.transform.SetParent(ctx.AvatarRootTransform, false);
                    
                    mergeLog.AppendLine($"\n--- Merging outfit: {clonedOutfit.name} ---");
                    
                    MergeOutfit(ctx, merger, outfitEntry, clonedOutfit.transform, targetArmature);
                }
                catch (Exception ex)
                {
                    mergeLog.AppendLine($"ERROR: Failed to merge outfit {outfitEntry.outfit.name}: {ex.Message}");
                }
            }
            
            UnityEngine.Object.DestroyImmediate(merger);
        }
        
        private void MergeOutfit(BuildContext ctx, CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform outfitRoot, Transform targetArmature)
        {
            Transform outfitArmature = FindArmatureInOutfit(outfitRoot);
            if (outfitArmature == null)
            {
                mergeLog.AppendLine($"  WARNING: Could not find armature in outfit, using root");
                outfitArmature = outfitRoot;
            }
            else
            {
                mergeLog.AppendLine($"  Outfit armature: {outfitArmature.name}");
            }
            
            var usedBones = GetBonesUsedByMeshes(outfitRoot);
            mergeLog.AppendLine($"  Used bones: {usedBones.Count}");
            
            if (usedBones.Count == 0)
            {
                mergeLog.AppendLine($"  WARNING: No used bones found, skipping");
                UnityEngine.Object.DestroyImmediate(outfitRoot.gameObject);
                return;
            }
            
            if (!string.IsNullOrEmpty(outfitEntry.uniqueBonePrefix))
            {
                ApplyUniqueBonePrefix(outfitArmature, targetArmature, outfitEntry, merger, usedBones);
            }
            
            if (!string.IsNullOrEmpty(outfitEntry.meshPrefix))
            {
                ApplyMeshPrefix(outfitRoot, outfitEntry.meshPrefix);
            }
            
            var boneMap = BuildBoneMappingWithConflicts(merger, outfitEntry, outfitArmature, targetArmature, usedBones);
            mergeLog.AppendLine($"  Bone map entries: {boneMap.Count}");
            
            var skinnedMeshes = outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            mergeLog.AppendLine($"  SkinnedMeshRenderers: {skinnedMeshes.Length}");
            
            // IMPORTANT: Remap meshes BEFORE merging hierarchy (which destroys bones)
            foreach (var smr in skinnedMeshes)
            {
                if (smr == null) continue;
                
                if (!merger.IsExcluded(smr.transform))
                {
                    RemapSkinnedMeshRenderer(smr, boneMap);
                }
            }
            
            // Now merge hierarchy (this will destroy merged bones)
            MergeHierarchy(outfitArmature, targetArmature, boneMap, merger, usedBones);
            
            // Move meshes to avatar root AFTER hierarchy is merged
            foreach (var smr in skinnedMeshes)
            {
                if (smr == null) continue;
                
                if (!merger.IsExcluded(smr.transform))
                {
                    smr.transform.SetParent(ctx.AvatarRootTransform, true);
                }
            }
            
            var constraints = outfitRoot.GetComponentsInChildren<IConstraint>(true);
            foreach (var constraint in constraints)
            {
                if (!merger.IsExcluded(((Component)constraint).transform))
                {
                    RemapConstraint(constraint, boneMap);
                }
            }
            
            MergeDynamicComponents(outfitRoot, boneMap, merger);
            MergeCVRComponents(ctx, merger, outfitRoot);
            
            var remainingChildren = new List<Transform>();
            foreach (Transform child in outfitRoot)
            {
                if (child != null && child.parent == outfitRoot)
                {
                    remainingChildren.Add(child);
                }
            }
            
            foreach (var child in remainingChildren)
            {
                if (child != null && child.GetComponentInChildren<Component>(true) != null)
                {
                    child.SetParent(ctx.AvatarRootTransform, true);
                }
            }
            
            UnityEngine.Object.DestroyImmediate(outfitRoot.gameObject);
            mergeLog.AppendLine($"  Outfit merge complete");
        }
        
        private Transform FindArmatureInOutfit(Transform root)
        {
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null && smr.rootBone != null)
            {
                Transform armature = smr.rootBone;
                while (armature.parent != null && armature.parent != root)
                {
                    armature = armature.parent;
                }
                return armature;
            }
            
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
        
        private void MergeHierarchy(Transform source, Transform target, Dictionary<Transform, Transform> boneMap, CVRMergeArmature merger, HashSet<Transform> usedBones)
        {
            mergeLog.AppendLine($"  MergeHierarchy: source={source.name}, target={target.name}");
            
            int mergedCount = 0;
            int movedCount = 0;
            int skippedCount = 0;
            List<GameObject> toDestroy = new List<GameObject>();
            
            void MergeNode(Transform sourceNode, Transform targetParent)
            {
                if (sourceNode == null || targetParent == null) return;
                
                if (merger.IsExcluded(sourceNode))
                {
                    skippedCount++;
                    var children = new List<Transform>();
                    foreach (Transform child in sourceNode)
                    {
                        children.Add(child);
                    }
                    foreach (var child in children)
                    {
                        MergeNode(child, targetParent);
                    }
                    return;
                }
                
                bool isUsedBone = usedBones.Contains(sourceNode);
                
                if (!isUsedBone)
                {
                    skippedCount++;
                    var children = new List<Transform>();
                    foreach (Transform child in sourceNode)
                    {
                        children.Add(child);
                    }
                    foreach (var child in children)
                    {
                        MergeNode(child, targetParent);
                    }
                    return;
                }
                
                Transform targetNode = null;
                
                if (boneMap.TryGetValue(sourceNode, out targetNode))
                {
                    // Bone is mapped - merge children to target and destroy source
                    mergedCount++;
                    
                    var children = new List<Transform>();
                    foreach (Transform child in sourceNode)
                    {
                        children.Add(child);
                    }
                    
                    // Process all children with the target bone as parent
                    foreach (var child in children)
                    {
                        MergeNode(child, targetNode);
                    }
                    
                    // Mark source bone for destruction since it's merged
                    toDestroy.Add(sourceNode.gameObject);
                }
                else
                {
                    // Unique bone - move it to target hierarchy
                    movedCount++;
                    
                    var children = new List<Transform>();
                    foreach (Transform child in sourceNode)
                    {
                        children.Add(child);
                    }
                    
                    // Reparent this bone to target
                    sourceNode.SetParent(targetParent, true);
                    
                    // Process children with this bone as parent
                    foreach (var child in children)
                    {
                        MergeNode(child, sourceNode);
                    }
                }
            }
            
            // Process the source armature root
            if (boneMap.ContainsKey(source))
            {
                // Root maps to target - merge children and destroy root
                var rootChildren = new List<Transform>();
                foreach (Transform child in source)
                {
                    rootChildren.Add(child);
                }
                
                foreach (var child in rootChildren)
                {
                    MergeNode(child, target);
                }
                
                // Destroy the source armature root
                toDestroy.Add(source.gameObject);
            }
            else
            {
                if (usedBones.Contains(source))
                {
                    // Root is unique - move it
                    var rootChildren = new List<Transform>();
                    foreach (Transform child in source)
                    {
                        rootChildren.Add(child);
                    }
                    
                    source.SetParent(target, true);
                    movedCount++;
                    
                    foreach (var child in rootChildren)
                    {
                        MergeNode(child, source);
                    }
                }
                else
                {
                    // Root not used - merge children only
                    var rootChildren = new List<Transform>();
                    foreach (Transform child in source)
                    {
                        rootChildren.Add(child);
                    }
                    
                    foreach (var child in rootChildren)
                    {
                        MergeNode(child, target);
                    }
                }
            }
            
            // Destroy all merged bones
            foreach (var obj in toDestroy)
            {
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            
            mergeLog.AppendLine($"    Merged: {mergedCount}, Moved: {movedCount}, Skipped: {skippedCount}, Destroyed: {toDestroy.Count}");
        }
        
        private void ApplyUniqueBonePrefix(Transform root, Transform targetArmature, OutfitToMerge outfitEntry, CVRMergeArmature merger, HashSet<Transform> usedBones)
        {
            var bonesToRename = new List<Transform>();
            
            void CheckBone(Transform bone)
            {
                if (merger.IsExcluded(bone)) return;
                if (!usedBones.Contains(bone)) return;
                
                string boneName = bone.name;
                
                if (!string.IsNullOrEmpty(outfitEntry.prefix) && boneName.StartsWith(outfitEntry.prefix))
                    boneName = boneName.Substring(outfitEntry.prefix.Length);
                if (!string.IsNullOrEmpty(outfitEntry.suffix) && boneName.EndsWith(outfitEntry.suffix))
                    boneName = boneName.Substring(0, boneName.Length - outfitEntry.suffix.Length);
                
                var targetBone = FindBoneByName(targetArmature, boneName);
                
                if (targetBone == null && !boneName.StartsWith(outfitEntry.uniqueBonePrefix))
                {
                    bonesToRename.Add(bone);
                }
                
                foreach (Transform child in bone)
                {
                    CheckBone(child);
                }
            }
            
            CheckBone(root);
            
            foreach (var bone in bonesToRename)
            {
                bone.name = outfitEntry.uniqueBonePrefix + bone.name;
            }
            
            if (bonesToRename.Count > 0)
            {
                mergeLog.AppendLine($"  Prefixed {bonesToRename.Count} unique bones with '{outfitEntry.uniqueBonePrefix}'");
            }
        }
        
        private void ApplyMeshPrefix(Transform root, string prefix)
        {
            var meshes = root.GetComponentsInChildren<MeshRenderer>(true);
            var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            
            int count = 0;
            
            foreach (var mesh in meshes)
            {
                if (!mesh.gameObject.name.StartsWith(prefix))
                {
                    mesh.gameObject.name = prefix + mesh.gameObject.name;
                    count++;
                }
            }
            
            foreach (var smr in skinnedMeshes)
            {
                if (!smr.gameObject.name.StartsWith(prefix))
                {
                    smr.gameObject.name = prefix + smr.gameObject.name;
                    count++;
                }
            }
            
            if (count > 0)
            {
                mergeLog.AppendLine($"  Prefixed {count} meshes with '{prefix}'");
            }
        }
        
        private Dictionary<Transform, Transform> BuildBoneMappingWithConflicts(CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform sourceRoot, Transform target, HashSet<Transform> usedBones)
        {
            var mapping = new Dictionary<Transform, Transform>();
            var mappingDetails = new StringBuilder();
            
            mappingDetails.AppendLine($"  Bone Mapping Details:");
            mappingDetails.AppendLine($"    Source root: {sourceRoot.name}");
            mappingDetails.AppendLine($"    Target root: {target.name}");
            
            int matchedCount = 0;
            int uniqueCount = 0;
            int conflictCount = 0;
            
            void MapBone(Transform sourceBone)
            {
                if (merger.IsExcluded(sourceBone)) return;
                if (!usedBones.Contains(sourceBone))
                {
                    foreach (Transform child in sourceBone)
                    {
                        MapBone(child);
                    }
                    return;
                }
                
                string boneName = sourceBone.name;
                
                if (!string.IsNullOrEmpty(outfitEntry.uniqueBonePrefix) && boneName.StartsWith(outfitEntry.uniqueBonePrefix))
                {
                    foreach (Transform child in sourceBone)
                    {
                        MapBone(child);
                    }
                    return;
                }
                
                if (!string.IsNullOrEmpty(outfitEntry.prefix) && boneName.StartsWith(outfitEntry.prefix))
                    boneName = boneName.Substring(outfitEntry.prefix.Length);
                if (!string.IsNullOrEmpty(outfitEntry.suffix) && boneName.EndsWith(outfitEntry.suffix))
                    boneName = boneName.Substring(0, boneName.Length - outfitEntry.suffix.Length);
                
                Transform targetBone = FindBoneByName(target, boneName);
                
                if (targetBone != null)
                {
                    var (hasConflict, _, _, _) = CheckTransformConflict(sourceBone, targetBone, merger.conflictThreshold);
                    
                    if (hasConflict)
                    {
                        conflictCount++;
                        var resolution = merger.defaultBoneConflictResolution;
                        var conflictEntry = merger.boneConflicts.Find(c => 
                            c.sourceBone.name == sourceBone.name && c.targetBone == targetBone && c.outfitName == sourceRoot.name);
                        
                        if (conflictEntry != null)
                        {
                            resolution = conflictEntry.resolution;
                        }
                        
                        switch (resolution)
                        {
                            case BoneConflictResolution.StillMerge:
                                mapping[sourceBone] = targetBone;
                                matchedCount++;
                                break;
                                
                            case BoneConflictResolution.Rename:
                                string newName = !string.IsNullOrEmpty(outfitEntry.uniqueBonePrefix) 
                                    ? outfitEntry.uniqueBonePrefix + sourceBone.name 
                                    : sourceBone.name + "_Merged";
                                sourceBone.name = newName;
                                uniqueCount++;
                                break;
                                
                            case BoneConflictResolution.DontMerge:
                                break;
                        }
                    }
                    else
                    {
                        mapping[sourceBone] = targetBone;
                        matchedCount++;
                    }
                }
                else
                {
                    uniqueCount++;
                }
                
                foreach (Transform child in sourceBone)
                {
                    MapBone(child);
                }
            }
            
            MapBone(sourceRoot);
            
            mappingDetails.AppendLine($"    Matched bones: {matchedCount}");
            mappingDetails.AppendLine($"    Unique bones: {uniqueCount}");
            mappingDetails.AppendLine($"    Conflicts: {conflictCount}");
            
            mergeLog.Append(mappingDetails.ToString());
            
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
        
        #region CVR Component Merging
        
        private void MergeCVRComponents(BuildContext ctx, CVRMergeArmature merger, Transform sourceTransform)
        {
            Component targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar == null) return;
            
            if (merger.mergeAdvancedAvatarSetup)
            {
                MergeAdvancedAvatarSettings(merger, sourceTransform, targetCVRAvatar);
            }
            
            if (merger.mergeAdvancedPointerTrigger)
            {
                MergeAdvancedPointerTrigger(sourceTransform);
            }
            
            if (merger.mergeParameterStream)
            {
                MergeParameterStream(sourceTransform, targetCVRAvatar);
            }
            
            if (merger.mergeAnimatorDriver)
            {
                MergeAnimatorDriver(sourceTransform, targetCVRAvatar);
            }
        }
        
        private void MergeAdvancedAvatarSettings(CVRMergeArmature merger, Transform sourceTransform, Component targetCVRAvatar)
        {
            var sourceCVRAvatar = FindCVRAvatarComponent(sourceTransform);
            if (sourceCVRAvatar == null) return;
            
            var sourceSettings = GetAdvancedAvatarSettings(sourceCVRAvatar);
            var targetSettings = GetAdvancedAvatarSettings(targetCVRAvatar);
            
            if (sourceSettings == null) return;
            if (targetSettings == null) targetSettings = CreateAdvancedAvatarSettings(targetCVRAvatar);
            
            var sourceList = GetSettingsList(sourceSettings);
            var targetList = GetSettingsList(targetSettings);
            
            if (sourceList == null || targetList == null) return;
            
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
                        SetEntryName(sourceEntry, merger.advancedSettingsPrefix + entryName);
                }
                
                bool hasConflict = false;
                foreach (var targetEntry in targetList)
                {
                    if (GetEntryMachineName(targetEntry) == GetEntryMachineName(sourceEntry))
                    {
                        hasConflict = true;
                        break;
                    }
                }
                
                if (!hasConflict)
                {
                    targetList.Add(sourceEntry);
                    mergedCount++;
                }
            }
            
            if (mergedCount > 0)
            {
                mergeLog.AppendLine($"  Merged {mergedCount} Advanced Avatar Settings");
            }
            
            SetSettingsInitialized(targetSettings, false);
        }
        
        private void MergeAdvancedPointerTrigger(Transform sourceTransform)
        {
            var pointerType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRPointer");
            var advancedTriggerType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAdvancedAvatarSettingsTrigger");
            
            int count = 0;
            if (pointerType != null) count += sourceTransform.GetComponentsInChildren(pointerType, true).Length;
            if (advancedTriggerType != null) count += sourceTransform.GetComponentsInChildren(advancedTriggerType, true).Length;
        }
        
        private void MergeParameterStream(Transform sourceTransform, Component targetCVRAvatar)
        {
            var paramStreamType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRParameterStream");
            if (paramStreamType == null) return;
            
            var sourceStreams = sourceTransform.GetComponentsInChildren(paramStreamType, true);
            if (sourceStreams.Length == 0) return;
            
            var targetStream = targetCVRAvatar.GetComponent(paramStreamType);
            if (targetStream == null) targetStream = targetCVRAvatar.gameObject.AddComponent(paramStreamType);
            
            foreach (var sourceStream in sourceStreams)
            {
                var entriesField = sourceStream.GetType().GetField("entries");
                if (entriesField == null) continue;
                
                var sourceEntries = entriesField.GetValue(sourceStream) as System.Collections.IList;
                var targetEntries = entriesField.GetValue(targetStream) as System.Collections.IList;
                
                if (sourceEntries != null && targetEntries != null)
                {
                    foreach (var entry in sourceEntries)
                    {
                        if (entry != null) targetEntries.Add(entry);
                    }
                }
            }
        }
        
        private void MergeAnimatorDriver(Transform sourceTransform, Component targetCVRAvatar)
        {
            var animatorDriverType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAnimatorDriver");
            if (animatorDriverType == null) return;
            
            var sourceDrivers = sourceTransform.GetComponentsInChildren(animatorDriverType, true);
            if (sourceDrivers.Length == 0) return;
            
            var targetDrivers = targetCVRAvatar.GetComponents(animatorDriverType);
            Component targetDriver = targetDrivers.Length > 0 ? targetDrivers[0] : targetCVRAvatar.gameObject.AddComponent(animatorDriverType);
            
            foreach (var sourceDriver in sourceDrivers)
            {
                var entriesField = sourceDriver.GetType().GetField("entries");
                if (entriesField == null) continue;
                
                var sourceEntries = entriesField.GetValue(sourceDriver) as System.Collections.IList;
                var targetEntries = entriesField.GetValue(targetDriver) as System.Collections.IList;
                
                if (sourceEntries != null && targetEntries != null)
                {
                    foreach (var entry in sourceEntries)
                    {
                        if (entry != null) targetEntries.Add(entry);
                    }
                }
            }
        }
        
        #endregion
        
        #region Animator Merge
        
        private void MergeAnimators(BuildContext ctx, CVRMergeArmature merger)
        {
            Component targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar == null) return;
            
            var targetAnimator = targetCVRAvatar.GetComponent<Animator>();
            if (targetAnimator == null) return;
            
            var baseController = targetAnimator.runtimeAnimatorController as AnimatorController;
            if (baseController == null) return;
            
            var newController = UnityEngine.Object.Instantiate(baseController);
            newController.name = baseController.name + "_Merged";
            
            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;
                
                var animator = outfitEntry.outfit.GetComponent<Animator>();
                if (animator == null || animator.runtimeAnimatorController == null) continue;
                
                var mergeController = animator.runtimeAnimatorController as AnimatorController;
                if (mergeController == null) continue;
                
                foreach (var layer in mergeController.layers)
                {
                    var newLayer = new AnimatorControllerLayer
                    {
                        name = $"{outfitEntry.outfit.name}_{layer.name}",
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
            }
            
            targetAnimator.runtimeAnimatorController = newController;
        }
        
        #endregion
        
        #region Dynamic Component Merging
        
        private void MergeDynamicComponents(Transform sourceTransform, Dictionary<Transform, Transform> boneMap, CVRMergeArmature merger)
        {
            if (merger.mergeDynamicBones)
            {
                var dynamicBoneType = FindTypeInLoadedAssemblies("DynamicBone");
                if (dynamicBoneType != null)
                {
                    var dynamicBones = sourceTransform.GetComponentsInChildren(dynamicBoneType, true);
                    foreach (var db in dynamicBones)
                    {
                        RemapComponentTransformField(db, "m_Root", boneMap);
                        RemapComponentTransformList(db, "m_Exclusions", boneMap);
                    }
                }
            }
            
            if (merger.mergeMagicaCloth)
            {
                var magicaClothType = FindTypeInLoadedAssemblies("MagicaCloth.MagicaCloth");
                if (magicaClothType != null)
                {
                    var magicaCloths = sourceTransform.GetComponentsInChildren(magicaClothType, true);
                    foreach (var mc in magicaCloths)
                    {
                        RemapComponentTransformList(mc, "clothTarget", boneMap);
                    }
                }
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private Transform FindArmatureFromCVRAvatar(Component cvrAvatar)
        {
            var animator = cvrAvatar.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                var rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (rootBone != null && rootBone.parent != null)
                    return rootBone.parent;
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
        
        private Component FindCVRAvatarComponent(Transform root)
        {
            var cvrAvatarType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAvatar");
            return cvrAvatarType != null ? root.GetComponent(cvrAvatarType) : null;
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
        
        private void RemapSkinnedMeshRenderer(SkinnedMeshRenderer smr, Dictionary<Transform, Transform> boneMap)
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
            
            if (changed) smr.bones = newBones;
            
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
                if (source.sourceTransform != null && boneMap.TryGetValue(source.sourceTransform, out var mapped))
                {
                    source.sourceTransform = mapped;
                    constraint.SetSource(i, source);
                }
            }
        }
        
        private void RemapComponentTransformField(Component component, string fieldName, Dictionary<Transform, Transform> boneMap)
        {
            var type = component.GetType();
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field != null && field.FieldType == typeof(Transform))
            {
                var currentTransform = field.GetValue(component) as Transform;
                if (currentTransform != null && boneMap.TryGetValue(currentTransform, out var mapped))
                {
                    field.SetValue(component, mapped);
                }
            }
        }
        
        private void RemapComponentTransformList(Component component, string fieldName, Dictionary<Transform, Transform> boneMap)
        {
            var type = component.GetType();
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
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
        
        #endregion
    }
}
