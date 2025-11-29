using System;
using System.Collections;
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

            var clonedOutfits = new List<Transform>();

            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;

                try
                {
                    var clonedOutfit = UnityEngine.Object.Instantiate(outfitEntry.outfit);
                    clonedOutfit.name = outfitEntry.outfit.name;
                    clonedOutfit.transform.SetParent(ctx.AvatarRootTransform, false);

                    mergeLog.AppendLine($"\n--- Merging outfit: {clonedOutfit.name} ---");

                    clonedOutfits.Add(clonedOutfit.transform);

                    MergeOutfitWithoutDestroy(ctx, merger, outfitEntry, clonedOutfit.transform, targetArmature);
                }
                catch (Exception ex)
                {
                    mergeLog.AppendLine($"ERROR: Failed to merge outfit {outfitEntry.outfit.name}: {ex.Message}");
                }
            }

            if (merger.mergeAdvancedAvatarSetup && clonedOutfits.Count > 0)
            {
                mergeLog.AppendLine($"\n--- Merging Advanced Avatar Settings from {clonedOutfits.Count} outfits ---");

                foreach (var outfitRoot in clonedOutfits)
                {
                    if (outfitRoot == null) continue;

                    try
                    {
                        var outfitCVRAvatar = FindCVRAvatarComponent(outfitRoot);
                        if (outfitCVRAvatar != null)
                        {
                            MergeAdvancedAvatarSettings(merger, outfitCVRAvatar, cvrAvatar);
                        }
                    }
                    catch (Exception ex)
                    {
                        mergeLog.AppendLine($"  ERROR merging settings from {outfitRoot.name}: {ex.Message}");
                        Debug.LogError($"[NDMF Merge] Advanced Avatar Settings merge error: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }

            // After all outfits are merged: rebuild animator drivers cleanly if enabled
            if (merger.mergeAnimatorDriver)
            {
                try
                {
                    MergeAnimatorDriversWithSplit(ctx.AvatarRootTransform);
                }
                catch (Exception ex)
                {
                    mergeLog.AppendLine($"  ERROR merging Animator Drivers: {ex.Message}");
                    Debug.LogError($"[NDMF Merge] Animator Driver merge failed: {ex.Message}\n{ex.StackTrace}");
                }
            }

            foreach (var outfitRoot in clonedOutfits)
            {
                if (outfitRoot != null)
                    UnityEngine.Object.DestroyImmediate(outfitRoot.gameObject);
            }

            UnityEngine.Object.DestroyImmediate(merger);
        }

        // ----------------------------
        // Outfit merge (unchanged)
        // ----------------------------
        private void MergeOutfitWithoutDestroy(BuildContext ctx, CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform outfitRoot, Transform targetArmature)
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
                return;
            }

            if (!string.IsNullOrEmpty(outfitEntry.uniqueBonePrefix))
                ApplyUniqueBonePrefix(outfitArmature, targetArmature, outfitEntry, merger, usedBones);

            if (!string.IsNullOrEmpty(outfitEntry.meshPrefix))
                ApplyMeshPrefix(outfitRoot, outfitEntry.meshPrefix);

            var boneMap = BuildBoneMappingWithConflicts(merger, outfitEntry, outfitArmature, targetArmature, usedBones);
            mergeLog.AppendLine($"  Bone map entries: {boneMap.Count}");

            var skinnedMeshes = outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            mergeLog.AppendLine($"  SkinnedMeshRenderers: {skinnedMeshes.Length}");

            foreach (var smr in skinnedMeshes)
            {
                if (smr == null) continue;
                if (!merger.IsExcluded(smr.transform))
                    RemapSkinnedMeshRenderer(smr, boneMap);
            }

            MergeHierarchy(outfitArmature, targetArmature, boneMap, merger, usedBones);

            foreach (var smr in skinnedMeshes)
            {
                if (smr == null) continue;
                if (!merger.IsExcluded(smr.transform))
                    smr.transform.SetParent(ctx.AvatarRootTransform, true);
            }

            var constraints = outfitRoot.GetComponentsInChildren<IConstraint>(true);
            foreach (var constraint in constraints)
            {
                if (!merger.IsExcluded(((Component)constraint).transform))
                    RemapConstraint(constraint, boneMap);
            }

            MergeDynamicComponents(outfitRoot, boneMap, merger);

            Component targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar != null)
            {
                if (merger.mergeAdvancedPointerTrigger) MergeAdvancedPointerTrigger(outfitRoot);
                if (merger.mergeParameterStream) MergeParameterStream(outfitRoot, targetCVRAvatar);
                if (merger.mergeAnimatorDriver) MergeAnimatorDriver(outfitRoot, targetCVRAvatar); // legacy add; split done later
            }

            var remainingChildren = new List<Transform>();
            foreach (Transform child in outfitRoot)
                if (child != null && child.parent == outfitRoot)
                    remainingChildren.Add(child);

            foreach (var child in remainingChildren)
                if (child != null && child.GetComponentInChildren<Component>(true) != null)
                    child.SetParent(ctx.AvatarRootTransform, true);

            mergeLog.AppendLine($"  Outfit merge complete");
        }

        // ----------------------------
        // Animator merge (UPDATED)
        // ----------------------------
        private void MergeAnimators(BuildContext ctx, CVRMergeArmature merger)
        {
            var targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar == null) return;

            var targetAnimator = targetCVRAvatar.GetComponent<Animator>();
            if (targetAnimator == null) return;

            var targetController = GetPreferredController(targetCVRAvatar);
            if (targetController == null)
            {
                mergeLog.AppendLine("  WARNING: Target has no animator controller");
                return;
            }

            var newController = UnityEngine.Object.Instantiate(targetController);
            newController.name = targetController.name + "_Merged";

            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;

                bool basic = outfitEntry.mergeAnimator;
                bool includeAAS = outfitEntry.mergeAnimatorIncludingAAS;

                if (!basic && !includeAAS) continue;

                var outfitAvatar = outfitEntry.outfit.GetComponentInChildren(targetCVRAvatar.GetType(), true);
                RuntimeAnimatorController srcController = null;

                if (outfitAvatar != null)
                    srcController = GetPreferredController(outfitAvatar);
                else
                {
                    var anim = outfitEntry.outfit.GetComponentInChildren<Animator>(true);
                    srcController = anim != null ? anim.runtimeAnimatorController : null;
                }

                var srcAC = srcController as AnimatorController;
                if (srcAC == null) continue;

                // Identify AAS autogenerated layers (layer.name == any parameter name)
                var paramNames = new HashSet<string>(srcAC.parameters.Select(p => p.name));

                foreach (var layer in srcAC.layers)
                {
                    if (layer == null) continue;

                    bool isAutoLayer = paramNames.Contains(layer.name);
                    if (basic && isAutoLayer && !includeAAS)
                        continue; // skip autogenerated layers in basic mode

                    var clonedSM = UnityEngine.Object.Instantiate(layer.stateMachine);

                    var newLayer = new AnimatorControllerLayer
                    {
                        name = $"{outfitEntry.outfit.name}_{layer.name}",
                        stateMachine = clonedSM,
                        avatarMask = layer.avatarMask,
                        blendingMode = layer.blendingMode,
                        defaultWeight = layer.defaultWeight,
                        syncedLayerIndex = layer.syncedLayerIndex,
                        iKPass = layer.iKPass
                    };

                    newController.AddLayer(newLayer);
                }

                foreach (var param in srcAC.parameters)
                {
                    if (!newController.parameters.Any(p => p.name == param.name))
                        newController.AddParameter(param.name, param.type);
                }
            }

            targetAnimator.runtimeAnimatorController = newController;
            TrySetActualControllerOnAvatar(targetCVRAvatar, newController);
        }

        private AnimatorController GetPreferredController(Component cvrAvatar)
        {
            // Priority: Actual/Generated controller if exists, else Base, else Animator component controller.
            var t = cvrAvatar.GetType();
            var candidates = new[]
            {
                "actualAnimatorController",
                "actualAnimator",
                "generatedAnimatorController",
                "generatedAnimator",
                "actualController",
                "baseAnimatorController",
                "baseAnimator"
            };

            foreach (var name in candidates)
            {
                var f = t.GetField(name);
                if (f != null && typeof(RuntimeAnimatorController).IsAssignableFrom(f.FieldType))
                {
                    var val = f.GetValue(cvrAvatar) as RuntimeAnimatorController;
                    if (val != null) return val as AnimatorController;
                }
                var p = t.GetProperty(name);
                if (p != null && typeof(RuntimeAnimatorController).IsAssignableFrom(p.PropertyType))
                {
                    var val = p.GetValue(cvrAvatar) as RuntimeAnimatorController;
                    if (val != null) return val as AnimatorController;
                }
            }

            var anim = cvrAvatar.GetComponent<Animator>();
            return anim != null ? anim.runtimeAnimatorController as AnimatorController : null;
        }

        private void TrySetActualControllerOnAvatar(Component cvrAvatar, RuntimeAnimatorController controller)
        {
            var t = cvrAvatar.GetType();
            var candidates = new[]
            {
                "actualAnimatorController",
                "actualAnimator",
                "generatedAnimatorController",
                "generatedAnimator",
                "actualController"
            };

            foreach (var name in candidates)
            {
                var f = t.GetField(name);
                if (f != null && typeof(RuntimeAnimatorController).IsAssignableFrom(f.FieldType))
                {
                    f.SetValue(cvrAvatar, controller);
                    EditorUtility.SetDirty(cvrAvatar);
                    return;
                }
                var p = t.GetProperty(name);
                if (p != null && p.CanWrite && typeof(RuntimeAnimatorController).IsAssignableFrom(p.PropertyType))
                {
                    p.SetValue(cvrAvatar, controller);
                    EditorUtility.SetDirty(cvrAvatar);
                    return;
                }
            }
        }

        // ----------------------------
        // Parameter Stream (SAFE MERGE)
        // ----------------------------
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

                var sourceEntries = entriesField.GetValue(sourceStream) as IList;
                var targetEntries = entriesField.GetValue(targetStream) as IList;

                if (sourceEntries == null || targetEntries == null) continue;

                foreach (var entry in sourceEntries)
                    if (entry != null) targetEntries.Add(entry);
            }
        }

        // ----------------------------
        // Animator Driver merge (UPDATED + SPLIT)
        // ----------------------------
        private class DriverEntry
        {
            public Animator animator;
            public string param;
            public int type;
            public float value;
        }

        private void MergeAnimatorDriversWithSplit(Transform avatarRoot)
        {
            var driverType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAnimatorDriver");
            if (driverType == null) return;

            var allDrivers = avatarRoot.GetComponentsInChildren(driverType, true).Cast<Component>().ToList();
            if (allDrivers.Count == 0) return;

            var entries = new List<DriverEntry>();

            foreach (var d in allDrivers)
            {
                var animatorsField = driverType.GetField("animators");
                var paramsField = driverType.GetField("animatorParameters");
                var typesField = driverType.GetField("animatorParameterType");
                if (animatorsField == null || paramsField == null || typesField == null) continue;

                var animators = animatorsField.GetValue(d) as IList;
                var paramNames = paramsField.GetValue(d) as IList;
                var paramTypes = typesField.GetValue(d) as IList;

                if (animators == null || paramNames == null || paramTypes == null) continue;

                int count = Math.Min(animators.Count, 16);

                for (int i = 0; i < count; i++)
                {
                    var a = animators[i] as Animator;
                    var p = paramNames[i] as string;
                    var t = (int)paramTypes[i];

                    float v = GetDriverValue(d, i + 1);

                    entries.Add(new DriverEntry { animator = a, param = p, type = t, value = v });
                }
            }

            // Destroy old drivers
            foreach (var d in allDrivers)
                UnityEngine.Object.DestroyImmediate(d);

            if (entries.Count == 0) return;

            // Create container
            var containerGO = new GameObject("NDMF Merge Animator Drivers");
            containerGO.transform.SetParent(avatarRoot, false);

            // Rebuild drivers in chunks of 16
            int idx = 0;
            int driverIndex = 0;
            while (idx < entries.Count)
            {
                var chunk = entries.Skip(idx).Take(16).ToList();

                var driverComp = containerGO.AddComponent(driverType);
                driverComp.name = $"CVRAnimatorDriver_Merged_{driverIndex++}";

                var animatorsField = driverType.GetField("animators");
                var paramsField = driverType.GetField("animatorParameters");
                var typesField = driverType.GetField("animatorParameterType");

                var animList = (IList)Activator.CreateInstance(animatorsField.FieldType);
                var paramList = (IList)Activator.CreateInstance(paramsField.FieldType);
                var typeList = (IList)Activator.CreateInstance(typesField.FieldType);

                for (int i = 0; i < chunk.Count; i++)
                {
                    animList.Add(chunk[i].animator);
                    paramList.Add(chunk[i].param);
                    typeList.Add(chunk[i].type);
                    SetDriverValue(driverComp, i + 1, chunk[i].value);
                }

                animatorsField.SetValue(driverComp, animList);
                paramsField.SetValue(driverComp, paramList);
                typesField.SetValue(driverComp, typeList);

                EditorUtility.SetDirty(driverComp);

                idx += chunk.Count;
            }
        }

        private float GetDriverValue(Component driver, int slot1to16)
        {
            var f = driver.GetType().GetField($"animatorParameter{slot1to16:00}");
            if (f == null) return 0f;
            return (float)f.GetValue(driver);
        }

        private void SetDriverValue(Component driver, int slot1to16, float value)
        {
            var f = driver.GetType().GetField($"animatorParameter{slot1to16:00}");
            if (f == null) return;
            f.SetValue(driver, value);
        }

        // Legacy per-outfit merge: just appends entries; final split rebuilds them.
        private void MergeAnimatorDriver(Transform sourceTransform, Component targetCVRAvatar)
        {
            var animatorDriverType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAnimatorDriver");
            if (animatorDriverType == null) return;

            var sourceDrivers = sourceTransform.GetComponentsInChildren(animatorDriverType, true);
            if (sourceDrivers.Length == 0) return;

            // Ensure at least one driver on target so entries exist
            var targetDriver = targetCVRAvatar.GetComponent(animatorDriverType);
            if (targetDriver == null) targetDriver = targetCVRAvatar.gameObject.AddComponent(animatorDriverType);

            foreach (var sourceDriver in sourceDrivers)
            {
                var animatorsField = sourceDriver.GetType().GetField("animators");
                var paramsField = sourceDriver.GetType().GetField("animatorParameters");
                var typesField = sourceDriver.GetType().GetField("animatorParameterType");
                if (animatorsField == null || paramsField == null || typesField == null) continue;

                var srcAnim = animatorsField.GetValue(sourceDriver) as IList;
                var srcParams = paramsField.GetValue(sourceDriver) as IList;
                var srcTypes = typesField.GetValue(sourceDriver) as IList;

                var tgtAnim = animatorsField.GetValue(targetDriver) as IList;
                var tgtParams = paramsField.GetValue(targetDriver) as IList;
                var tgtTypes = typesField.GetValue(targetDriver) as IList;

                if (srcAnim == null || srcParams == null || srcTypes == null ||
                    tgtAnim == null || tgtParams == null || tgtTypes == null) continue;

                int count = Math.Min(srcAnim.Count, 16);
                for (int i = 0; i < count; i++)
                {
                    tgtAnim.Add(srcAnim[i]);
                    tgtParams.Add(srcParams[i]);
                    tgtTypes.Add(srcTypes[i]);

                    // Value float fields will be re-collected/split later;
                    // we don't set them here to avoid overflow.
                }
            }

            EditorUtility.SetDirty(targetDriver);
        }

        // ----------------------------
        // Advanced Avatar Settings merge (kept from previous fix)
        // ----------------------------
        private void MergeAdvancedAvatarSettings(CVRMergeArmature merger, Component sourceCVRAvatar, Component targetCVRAvatar)
        {
            try
            {
                if (sourceCVRAvatar == null) return;

                var sourceSettings = GetAdvancedAvatarSettings(sourceCVRAvatar);
                var targetSettings = GetAdvancedAvatarSettings(targetCVRAvatar);

                if (sourceSettings == null) return;
                if (targetSettings == null) targetSettings = CreateAdvancedAvatarSettings(targetCVRAvatar);

                EnsureSettingsListExists(targetSettings);

                var sourceList = GetSettingsList(sourceSettings);
                var targetList = GetSettingsList(targetSettings);

                if (sourceList == null || targetList == null) return;

                int mergedCount = 0;
                int skippedCount = 0;

                var avatarRoot = targetCVRAvatar.transform;
                var outfitRoot = sourceCVRAvatar.transform;
                var outfitName = outfitRoot.name;

                mergeLog.AppendLine($"  Processing settings from {outfitName} (count: {sourceList.Count})");

                foreach (var sourceEntry in sourceList)
                {
                    if (sourceEntry == null) continue;

                    var machineName = GetEntryMachineName(sourceEntry);
                    if (string.IsNullOrEmpty(machineName)) continue;

                    var finalMachineName = machineName;
                    if (!string.IsNullOrEmpty(merger.advancedSettingsPrefix))
                        finalMachineName = merger.advancedSettingsPrefix + machineName;

                    bool hasConflict = false;
                    foreach (var targetEntry in targetList)
                    {
                        if (GetEntryMachineName(targetEntry) == finalMachineName)
                        {
                            hasConflict = true;
                            break;
                        }
                    }

                    if (!hasConflict)
                    {
                        try
                        {
                            var entryType = sourceEntry.GetType();
                            var newEntry = Activator.CreateInstance(entryType);

                            CopyBasicFields(sourceEntry, newEntry);

                            if (!string.IsNullOrEmpty(merger.advancedSettingsPrefix))
                            {
                                SetEntryMachineName(newEntry, finalMachineName);
                                var entryName = GetEntryName(sourceEntry);
                                if (!string.IsNullOrEmpty(entryName))
                                    SetEntryName(newEntry, merger.advancedSettingsPrefix + entryName);
                            }

                            var settingProperty = entryType.GetProperty("setting");
                            if (settingProperty != null)
                            {
                                var sourceSetting = settingProperty.GetValue(sourceEntry);
                                if (sourceSetting != null)
                                {
                                    var settingCopy = CopySettingObject(sourceSetting, outfitName, avatarRoot);
                                    if (settingCopy != null)
                                    {
                                        settingProperty.SetValue(newEntry, settingCopy);
                                        targetList.Add(newEntry);
                                        mergedCount++;
                                    }
                                    else
                                    {
                                        skippedCount++;
                                        mergeLog.AppendLine($"    Skipped '{machineName}' - failed to copy setting object");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            skippedCount++;
                            mergeLog.AppendLine($"    Failed to merge '{machineName}': {ex.Message}");
                        }
                    }
                    else
                    {
                        skippedCount++;
                    }
                }

                if (mergedCount > 0 || skippedCount > 0)
                    mergeLog.AppendLine($"  Merged {mergedCount} settings from {outfitName} (skipped {skippedCount})");

                if (mergedCount > 0)
                {
                    EditorUtility.SetDirty(targetCVRAvatar);
                    if (targetSettings is UnityEngine.Object settingsObj)
                        EditorUtility.SetDirty(settingsObj);

                    var usesAdvField = targetCVRAvatar.GetType().GetField("avatarUsesAdvancedSettings");
                    if (usesAdvField != null && usesAdvField.FieldType == typeof(bool))
                        usesAdvField.SetValue(targetCVRAvatar, true);
                }

                SetSettingsInitialized(targetSettings, true);
            }
            catch (Exception ex)
            {
                mergeLog.AppendLine($"  ERROR merging Advanced Avatar Settings: {ex.Message}");
                Debug.LogError($"[NDMF Merge] Advanced Avatar Settings merge failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void EnsureSettingsListExists(object advancedSettings)
        {
            if (advancedSettings == null) return;
            var field = advancedSettings.GetType().GetField("settings");
            if (field == null) return;

            if (field.GetValue(advancedSettings) == null)
            {
                var newList = Activator.CreateInstance(field.FieldType);
                field.SetValue(advancedSettings, newList);
            }
        }

        private void CopyBasicFields(object source, object target)
        {
            var type = source.GetType();
            var fields = type.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;
                if (field.Name == "setting" || field.Name.EndsWith("Settings") || field.Name == "reorderableList")
                    continue;

                field.SetValue(target, field.GetValue(source));
            }
        }

        private object CopySettingObject(object sourceSetting, string outfitName, Transform avatarRoot)
        {
            if (sourceSetting == null) return null;

            var settingType = sourceSetting.GetType();
            var newSetting = Activator.CreateInstance(settingType);

            var fields = settingType.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;
                if (field.Name == "reorderableList") continue;

                var value = field.GetValue(sourceSetting);

                if (field.Name.Contains("Target") && value is IList list)
                {
                    var newList = (IList)Activator.CreateInstance(field.FieldType);
                    foreach (var item in list)
                        newList.Add(item == null ? null : CopyTargetEntryWithGameObjectRemap(item, outfitName, avatarRoot));
                    field.SetValue(newSetting, newList);
                }
                else if (field.Name == "options" && value is IList optionsList)
                {
                    var newList = (IList)Activator.CreateInstance(field.FieldType);
                    foreach (var option in optionsList)
                        newList.Add(option == null ? null : CopyDropdownOption(option, outfitName, avatarRoot));
                    field.SetValue(newSetting, newList);
                }
                else
                {
                    field.SetValue(newSetting, value);
                }
            }

            return newSetting;
        }

        private object CopyDropdownOption(object sourceOption, string outfitName, Transform avatarRoot)
        {
            var optionType = sourceOption.GetType();
            var newOption = Activator.CreateInstance(optionType);

            var fields = optionType.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;

                var value = field.GetValue(sourceOption);

                if (field.Name == "gameObjectTargets" && value is IList list)
                {
                    var newList = (IList)Activator.CreateInstance(field.FieldType);
                    foreach (var item in list)
                        newList.Add(item == null ? null : CopyTargetEntryWithGameObjectRemap(item, outfitName, avatarRoot));
                    field.SetValue(newOption, newList);
                }
                else
                {
                    field.SetValue(newOption, value);
                }
            }

            return newOption;
        }

        private object CopyTargetEntryWithGameObjectRemap(object sourceTarget, string outfitName, Transform avatarRoot)
        {
            var targetType = sourceTarget.GetType();
            var newTarget = Activator.CreateInstance(targetType);

            var fields = targetType.GetFields(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            var treePathField = targetType.GetField("treePath");
            string treePath = treePathField?.GetValue(sourceTarget) as string;

            string normalizedPath = treePath;
            if (!string.IsNullOrEmpty(normalizedPath))
            {
                var parts = normalizedPath.Split('/');
                if (parts.Length > 0 && parts[0] == outfitName)
                    normalizedPath = string.Join("/", parts, 1, parts.Length - 1);
            }

            GameObject resolvedGO = null;

            var goField = targetType.GetField("gameObject");
            var sourceGO = goField?.GetValue(sourceTarget) as GameObject;

            if (sourceGO != null && sourceGO.transform != null && sourceGO.transform.IsChildOf(avatarRoot))
            {
                resolvedGO = sourceGO;
                normalizedPath = GetRelativePath(avatarRoot, sourceGO.transform);
            }
            else if (!string.IsNullOrEmpty(normalizedPath))
            {
                resolvedGO = FindGameObjectByPath(avatarRoot, normalizedPath);
                if (resolvedGO == null)
                    mergeLog.AppendLine($"    WARNING: Could not resolve AAS target path: {normalizedPath}");
            }

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;

                if (field.Name == "gameObject")
                    field.SetValue(newTarget, resolvedGO);
                else if (field.Name == "treePath")
                    field.SetValue(newTarget, normalizedPath);
                else
                    field.SetValue(newTarget, field.GetValue(sourceTarget));
            }

            return newTarget;
        }

        private string GetRelativePath(Transform root, Transform t)
        {
            var stack = new Stack<string>();
            var cur = t;
            while (cur != null && cur != root)
            {
                stack.Push(cur.name);
                cur = cur.parent;
            }
            return string.Join("/", stack);
        }

        private GameObject FindGameObjectByPath(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            Transform current = root;
            var parts = path.Split('/');
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;

                Transform found = null;
                foreach (Transform child in current)
                {
                    if (child.name == part)
                    {
                        found = child;
                        break;
                    }
                }
                if (found == null) return null;
                current = found;
            }
            return current.gameObject;
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

        private IList GetSettingsList(object advancedSettings)
        {
            if (advancedSettings == null) return null;
            var field = advancedSettings.GetType().GetField("settings");
            return field?.GetValue(advancedSettings) as IList;
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

        // ----------------------------
        // Remaining helpers from your plugin (unchanged)
        // ----------------------------

        private Transform FindArmatureInOutfit(Transform root)
        {
            var smr = root.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null && smr.rootBone != null)
            {
                Transform armature = smr.rootBone;
                while (armature.parent != null && armature.parent != root)
                    armature = armature.parent;
                return armature;
            }

            var names = new[] { "Armature", "armature", "Skeleton", "skeleton", "Root", "root" };
            foreach (var name in names)
            {
                var found = root.Find(name);
                if (found != null) return found;
            }

            foreach (Transform child in root)
                if (child.childCount >= 3 && !child.GetComponent<SkinnedMeshRenderer>())
                    return child;

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
                    usedBones.Add(smr.rootBone);
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
                    foreach (Transform child in sourceNode) children.Add(child);
                    foreach (var child in children) MergeNode(child, targetParent);
                    return;
                }

                bool isUsedBone = usedBones.Contains(sourceNode);
                if (!isUsedBone)
                {
                    skippedCount++;
                    var children = new List<Transform>();
                    foreach (Transform child in sourceNode) children.Add(child);
                    foreach (var child in children) MergeNode(child, targetParent);
                    return;
                }

                if (boneMap.TryGetValue(sourceNode, out var targetNode))
                {
                    mergedCount++;
                    var children = new List<Transform>();
                    foreach (Transform child in sourceNode) children.Add(child);
                    foreach (var child in children) MergeNode(child, targetNode);
                    toDestroy.Add(sourceNode.gameObject);
                }
                else
                {
                    movedCount++;
                    var children = new List<Transform>();
                    foreach (Transform child in sourceNode) children.Add(child);
                    sourceNode.SetParent(targetParent, true);
                    foreach (var child in children) MergeNode(child, sourceNode);
                }
            }

            if (boneMap.ContainsKey(source))
            {
                var rootChildren = new List<Transform>();
                foreach (Transform child in source) rootChildren.Add(child);
                foreach (var child in rootChildren) MergeNode(child, target);
                toDestroy.Add(source.gameObject);
            }
            else
            {
                if (usedBones.Contains(source))
                {
                    var rootChildren = new List<Transform>();
                    foreach (Transform child in source) rootChildren.Add(child);
                    source.SetParent(target, true);
                    movedCount++;
                    foreach (var child in rootChildren) MergeNode(child, source);
                }
                else
                {
                    var rootChildren = new List<Transform>();
                    foreach (Transform child in source) rootChildren.Add(child);
                    foreach (var child in rootChildren) MergeNode(child, target);
                }
            }

            foreach (var obj in toDestroy)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);

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
                    bonesToRename.Add(bone);

                foreach (Transform child in bone) CheckBone(child);
            }

            CheckBone(root);

            foreach (var bone in bonesToRename)
                bone.name = outfitEntry.uniqueBonePrefix + bone.name;

            if (bonesToRename.Count > 0)
                mergeLog.AppendLine($"  Prefixed {bonesToRename.Count} unique bones with '{outfitEntry.uniqueBonePrefix}'");
        }

        private void ApplyMeshPrefix(Transform root, string prefix)
        {
            var meshes = root.GetComponentsInChildren<MeshRenderer>(true);
            var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            int count = 0;
            foreach (var mesh in meshes)
                if (!mesh.gameObject.name.StartsWith(prefix))
                {
                    mesh.gameObject.name = prefix + mesh.gameObject.name;
                    count++;
                }

            foreach (var smr in skinnedMeshes)
                if (!smr.gameObject.name.StartsWith(prefix))
                {
                    smr.gameObject.name = prefix + smr.gameObject.name;
                    count++;
                }

            if (count > 0)
                mergeLog.AppendLine($"  Prefixed {count} meshes with '{prefix}'");
        }

        private Dictionary<Transform, Transform> BuildBoneMappingWithConflicts(CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform sourceRoot, Transform target, HashSet<Transform> usedBones)
        {
            var mapping = new Dictionary<Transform, Transform>();

            void MapBone(Transform sourceBone)
            {
                if (merger.IsExcluded(sourceBone)) return;
                if (!usedBones.Contains(sourceBone))
                {
                    foreach (Transform child in sourceBone) MapBone(child);
                    return;
                }

                string boneName = sourceBone.name;

                if (!string.IsNullOrEmpty(outfitEntry.uniqueBonePrefix) && boneName.StartsWith(outfitEntry.uniqueBonePrefix))
                {
                    foreach (Transform child in sourceBone) MapBone(child);
                    return;
                }

                if (!string.IsNullOrEmpty(outfitEntry.prefix) && boneName.StartsWith(outfitEntry.prefix))
                    boneName = boneName.Substring(outfitEntry.prefix.Length);
                if (!string.IsNullOrEmpty(outfitEntry.suffix) && boneName.EndsWith(outfitEntry.suffix))
                    boneName = boneName.Substring(0, boneName.Length - outfitEntry.suffix.Length);

                Transform targetBone = FindBoneByName(target, boneName);
                if (targetBone != null) mapping[sourceBone] = targetBone;

                foreach (Transform child in sourceBone) MapBone(child);
            }

            MapBone(sourceRoot);
            return mapping;
        }

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
                if (child.childCount >= 3 && !child.GetComponent<SkinnedMeshRenderer>())
                    return child;

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

        private Component FindCVRAvatarComponent(Transform root)
        {
            var cvrAvatarType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAvatar");
            return cvrAvatarType != null ? root.GetComponent(cvrAvatarType) : null;
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
                else newBones[i] = bones[i];
            }

            if (changed) smr.bones = newBones;
            if (smr.rootBone != null && boneMap.TryGetValue(smr.rootBone, out var newRootBone))
                smr.rootBone = newRootBone;
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
                        RemapComponentTransformList(mc, "clothTarget", boneMap);
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
                    field.SetValue(component, mapped);
            }
        }

        private void RemapComponentTransformList(Component component, string fieldName, Dictionary<Transform, Transform> boneMap)
        {
            var type = component.GetType();
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null && typeof(IList).IsAssignableFrom(field.FieldType))
            {
                var list = field.GetValue(component) as IList;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] is Transform t && boneMap.TryGetValue(t, out var mapped))
                            list[i] = mapped;
            }
        }

        private void MergeAdvancedPointerTrigger(Transform sourceTransform)
        {
            // unchanged (placeholder)
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
    }
}
