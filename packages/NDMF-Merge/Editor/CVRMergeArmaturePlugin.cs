using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.IO;
using UnityEngine;
using UnityEngine.Animations;
using UnityEditor;
using UnityEditor.Animations;
using nadena.dev.ndmf;
using NDMFMerge.Runtime;

#if CCK_ADDIN_MAGICACLOTHSUPPORT
using MagicaCloth;
#endif

[assembly: ExportsPlugin(typeof(NDMFMerge.Editor.CVRMergeArmaturePlugin))]

namespace NDMFMerge.Editor
{
    public class CVRMergeArmaturePlugin : Plugin<CVRMergeArmaturePlugin>
    {
        public override string QualifiedName => "dev.milchzocker.ndmf-merge";
        public override string DisplayName => "NDMF Merge";

        private const string NDMF_PREFIX = "[NDMF]";
        private const string ROOT_GEN_FOLDER = "Assets/NDMF Merge Generated";

        private StringBuilder mergeLog = new StringBuilder();
        private static readonly HashSet<int> _aasGeneratedForAvatarInstance = new HashSet<int>();
        private static readonly Dictionary<string, Type> _typeCache = new Dictionary<string, Type>();

        private static bool ShouldLogWarnings(CVRMergeArmature merger)
        {
            return merger != null && merger.loggingLevel != MergeLoggingLevel.ErrorsOnly;
        }

        private static bool ShouldLogInfo(CVRMergeArmature merger)
        {
            return merger != null && merger.loggingLevel == MergeLoggingLevel.AllDetails;
        }

        private static bool ShouldLogDetailed(CVRMergeArmature merger)
        {
            return merger != null
                   && merger.loggingLevel == MergeLoggingLevel.AllDetails
                   && merger.verboseMode == MergeVerboseMode.Detailed;
        }

        // ========================================
        // CVR REFLECTION HELPER METHODS (EMBEDDED)
        // ========================================
        private static Type _cvrAvatarType;
        private static Type _cvrSpawnableType;

        private static Type CVRAvatarType
        {
            get
            {
                if (_cvrAvatarType == null)
                {
                    _cvrAvatarType = FindTypeInLoadedAssembliesStatic("ABI.CCK.Components.CVRAvatar");
                }
                return _cvrAvatarType;
            }
        }

        private static Type CVRSpawnableType
        {
            get
            {
                if (_cvrSpawnableType == null)
                {
                    _cvrSpawnableType = FindTypeInLoadedAssembliesStatic("ABI.CCK.Components.CVRSpawnable");
                }
                return _cvrSpawnableType;
            }
        }

        private static Type FindTypeInLoadedAssembliesStatic(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            if (_typeCache.TryGetValue(typeName, out Type cachedType))
                return cachedType;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    _typeCache[typeName] = type;
                    return type;
                }
            }

            _typeCache[typeName] = null;
            return null;
        }

        private static Component GetCVRAvatarFromGameObject(GameObject obj)
        {
            if (CVRAvatarType == null) return null;
            return obj.GetComponent(CVRAvatarType);
        }

        private static Component GetCVRAvatarInParent(Transform transform)
        {
            if (CVRAvatarType == null) return null;
            var current = transform;
            while (current != null)
            {
                var avatar = current.GetComponent(CVRAvatarType);
                if (avatar != null) return avatar;
                current = current.parent;
            }
            return null;
        }

        private static bool IsCVRAvatar(Component component)
        {
            if (component == null || CVRAvatarType == null) return false;
            return component.GetType() == CVRAvatarType;
        }

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
                        try { ProcessMerger(ctx, merger); }
                        catch (Exception ex)
                        {
                            mergeLog.AppendLine($"ERROR: Failed to process merger: {ex.Message}");
                            Debug.LogError($"[NDMF Merge] Failed to process merger: {ex.Message}\n{ex.StackTrace}", merger);
                        }
                    }

                });

            InPhase(BuildPhase.Transforming)
                .Run("Merge Animators", ctx =>
                {
                    var mergeComponents = ctx.AvatarRootTransform.GetComponentsInChildren<CVRMergeArmature>(true);
                    foreach (var merger in mergeComponents)
                    {
                        if (merger == null || !merger.mergeAnimator) continue;
                        try { MergeAnimators(ctx, merger); }
                        catch (Exception ex) { Debug.LogError($"[NDMF Merge] Failed to merge animators: {ex.Message}\n{ex.StackTrace}", merger); }
                    }
                });

            InPhase(BuildPhase.Transforming)
                .Run("Generate AAS Controller At End", ctx =>
                {
                    AssetDatabase.StartAssetEditing();
                    try
                    {
                        var mergeComponents = ctx.AvatarRootTransform.GetComponentsInChildren<CVRMergeArmature>(true);

                        foreach (var merger in mergeComponents)
                        {
                            if (merger == null || !merger.generateAASControllerAtEnd || !merger.mergeAdvancedAvatarSetup) continue;

                            var cvrAvatar = merger.GetCVRAvatar();
                            if (cvrAvatar == null) continue;

                            int id = cvrAvatar.GetInstanceID();
                            if (_aasGeneratedForAvatarInstance.Contains(id)) continue;
                            _aasGeneratedForAvatarInstance.Add(id);

                            try { GenerateAASControllerAtEnd(cvrAvatar, ctx.AvatarRootTransform, merger); }
                            catch (Exception ex)
                            {
                                mergeLog.AppendLine($"AAS end-gen: CreateAASController invoke failed: {ex.Message}");
                                Debug.LogError($"[NDMF Merge] AAS controller generation failed: {ex.Message}\n{ex.StackTrace}", merger);
                            }
                        }

                        foreach (var merger in mergeComponents)
                            if (merger != null) UnityEngine.Object.DestroyImmediate(merger);

                        mergeLog.AppendLine("========== END MERGE LOG ==========");
                        Debug.Log(mergeLog.ToString());
                    }
                    finally
                    {
                        AssetDatabase.StopAssetEditing();
                        AssetDatabase.SaveAssets();
                    }
                });
        }

        private void ProcessMerger(BuildContext ctx, CVRMergeArmature merger)
        {
            var cvrAvatar = merger.GetCVRAvatar();
            if (cvrAvatar == null) { mergeLog.AppendLine("ERROR: No CVRAvatar found on avatar!"); return; }
            var clipReplacementMap = new Dictionary<AnimationClip, AnimationClip>();

            Transform targetArmature = FindArmatureFromCVRAvatar(cvrAvatar);
            bool hasArmatureModeOutfits = merger.outfitsToMerge != null && merger.outfitsToMerge.Any(o => o != null && o.outfit != null && o.mergeMode == OutfitMergeMode.Armature);
            if (targetArmature == null && hasArmatureModeOutfits)
            {
                mergeLog.AppendLine("ERROR: Could not find armature in CVRAvatar!");
                return;
            }

            if (targetArmature != null && ShouldLogInfo(merger))
                mergeLog.AppendLine($"Target armature: {targetArmature.name}");

            var armatureSettings = GetArmatureMergingSettings(merger);
            if (targetArmature != null &&
                armatureSettings.enableScaleNormalization &&
                armatureSettings.normalizationScope == ArmatureScaleNormalizationScope.BaseAndOutfits)
            {
                NormalizeTransformsBeforeMerge(targetArmature, merger, armatureSettings, "base armature", ctx.AvatarRootTransform);
            }

            // Per-outfit bounds reference will be selected individually (CopyFromSelected)
            SkinnedMeshRenderer bodyReferenceMesh = null;

            var clonedOutfits = new List<Transform>();
            var clonedOutfitByEntry = new Dictionary<OutfitToMerge, Transform>();
            var mergedBoneReferences = new Dictionary<int, Transform>();

            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;

                try
                {
                    var clonedOutfit = UnityEngine.Object.Instantiate(outfitEntry.outfit);
                    clonedOutfit.name = outfitEntry.outfit.name;
                    clonedOutfit.transform.SetParent(ctx.AvatarRootTransform, false);

                    // [NEW FEATURE] Force scale to (1,1,1)
                    if (outfitEntry.forceScaleToOne)
                    {
                        clonedOutfit.transform.localScale = Vector3.one;
                        if (ShouldLogInfo(merger))
                            mergeLog.AppendLine($"  Forced outfit root scale to (1,1,1)");
                    }

                    if (armatureSettings.enableScaleNormalization)
                        NormalizeTransformsBeforeMerge(clonedOutfit.transform, merger, armatureSettings, "outfit", clonedOutfit.transform);

                    if (ShouldLogInfo(merger))
                        mergeLog.AppendLine($"\n--- Merging outfit: {clonedOutfit.name} ---");
                    clonedOutfits.Add(clonedOutfit.transform);
                    clonedOutfitByEntry[outfitEntry] = clonedOutfit.transform;

                    if (outfitEntry.mergeMode == OutfitMergeMode.Object)
                    {
                        MergeOutfitAsObject(ctx, merger, outfitEntry, clonedOutfit.transform);
                    }
                    else
                    {
                        MergeOutfitWithoutDestroy(ctx, merger, outfitEntry, clonedOutfit.transform, targetArmature, bodyReferenceMesh, clonedOutfitByEntry, mergedBoneReferences, clipReplacementMap);
                    }
                }
                catch (Exception ex)
                {
                    mergeLog.AppendLine($"ERROR: Failed to merge outfit {outfitEntry.outfit.name}: {ex.Message}");
                }
            }

            if (merger.mergeAdvancedAvatarSetup && clonedOutfits.Count > 0)
            {
                // SAFETY: Get the CVRAvatar from within the cloned hierarchy, not the original
                var clonedCVRAvatar = ctx.AvatarRootTransform.GetComponentInChildren(cvrAvatar.GetType(), true);
                if (clonedCVRAvatar != null)
                {
                    if (ShouldLogInfo(merger))
                        mergeLog.AppendLine($"\n--- Merging Advanced Avatar Settings from {clonedOutfits.Count} outfits ---");
                    foreach (var outfitRoot in clonedOutfits)
                    {
                        if (outfitRoot == null) continue;
                        try
                        {
                            var outfitCVRAvatar = FindCVRAvatarComponent(outfitRoot);
                            if (outfitCVRAvatar != null) MergeAdvancedAvatarSettings(merger, outfitCVRAvatar, clonedCVRAvatar as Component, ctx.AvatarRootTransform);
                        }
                        catch (Exception ex)
                        {
                            mergeLog.AppendLine($"  ERROR merging settings from {outfitRoot.name}: {ex.Message}");
                            Debug.LogError($"[NDMF Merge] Advanced Avatar Settings merge error: {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                }
            }

            if (merger.mergeAnimatorDriver)
            {
                try { MergeAnimatorDriversWithSplit(ctx.AvatarRootTransform, merger); }
                catch (Exception ex)
                {
                    mergeLog.AppendLine($"  ERROR merging Animator Drivers: {ex.Message}");
                    Debug.LogError($"[NDMF Merge] Animator Driver merge failed: {ex.Message}\n{ex.StackTrace}");
                }
            }

            foreach (var outfitRoot in clonedOutfits)
                if (outfitRoot != null) UnityEngine.Object.DestroyImmediate(outfitRoot.gameObject);

            ApplyAnimationClipReplacementsInClone(ctx.AvatarRootTransform, clipReplacementMap, merger);

            try
            {
                RemapExternalReferencesUniversal(ctx.AvatarRootTransform, merger, mergedBoneReferences);
            }
            catch (Exception ex)
            {
                mergeLog.AppendLine($"  ERROR in universal reference remap: {ex.Message}");
                Debug.LogError($"[NDMF Merge] Universal reference remap failed: {ex.Message}\n{ex.StackTrace}");
            }

            try
            {
                RebuildMagicaData(ctx.AvatarRootTransform, merger);
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine("Post-merge finalization complete (universal reference remap + Magica rebuild).");
                // Post-merge verification
                RunPostMergeVerification(ctx, merger, merger.postMergeVerificationSettings);
                // Bone chain validation on final armature
                ValidateBoneChains(targetArmature, merger.boneChainValidationSettings, merger);
            }
            catch (Exception ex)
            {
                mergeLog.AppendLine($"  ERROR in post-merge finalization: {ex.Message}");
                Debug.LogError($"[NDMF Merge] Post-merge finalization failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static ArmatureMergingSettings GetArmatureMergingSettings(CVRMergeArmature merger)
        {
            return merger != null && merger.armatureMergingSettings != null
                ? merger.armatureMergingSettings
                : new ArmatureMergingSettings();
        }

        // [NEW FEATURE] Transform normalization to reduce distortion before merge.
        private void NormalizeTransformsBeforeMerge(Transform root, CVRMergeArmature merger, ArmatureMergingSettings settings, string subjectLabel, Transform skinnedMeshSearchRoot = null)
        {
            if (root == null || settings == null) return;

            if (ShouldLogInfo(merger))
            {
                mergeLog.AppendLine($"  [Scale Normalization] Starting ({settings.normalizationMode}) on {subjectLabel} '{root.name}'...");
            }
            if (ShouldLogDetailed(merger))
            {
                mergeLog.AppendLine($"  [Scale Normalization] Config: scaleAll={settings.applyScaleToAllTransforms}, protectSkeleton={settings.protectSkinnedSkeleton}, posStatic={settings.applyPositionToStaticMeshes}, rotStatic={settings.applyRotationToStaticMeshes}, posSkinned={settings.applyPositionToSkinnedMeshes}, rotSkinned={settings.applyRotationToSkinnedMeshes}");
            }

            var protectedTransforms = settings.protectSkinnedSkeleton
                ? BuildProtectedSkeletonTransforms(root, skinnedMeshSearchRoot ?? root)
                : new HashSet<Transform>();

            int scaledCount = 0;
            int skippedProtectedScaleCount = 0;
            int bakedStaticMeshCount = 0;
            int bakedSkinnedMeshCount = 0;
            int resetTransformCount = 0;

            bool isApplyMode = settings.normalizationMode == ArmatureScaleNormalizationMode.ApplyTransformLike;

            void RecursiveScaleNormalize(Transform t)
            {
                bool isProtected = protectedTransforms.Contains(t);
                if (t.localScale != Vector3.one)
                {
                    bool skipProtectedScale = isProtected && settings.protectSkinnedSkeleton && !settings.applyScaleToAllTransforms;
                    if (skipProtectedScale)
                    {
                        skippedProtectedScaleCount++;
                    }
                    else
                    {
                        var localScale = t.localScale;
                        foreach (Transform child in t)
                        {
                            child.localPosition = Vector3.Scale(child.localPosition, localScale);
                            child.localScale = Vector3.Scale(localScale, child.localScale);
                        }
                        t.localScale = Vector3.one;
                        scaledCount++;
                    }
                }

                foreach (Transform child in t) RecursiveScaleNormalize(child);
            }

            bool BakeMeshTransformsOnNode(Transform t)
            {
                if (t == null) return false;

                var meshFilter = t.GetComponent<MeshFilter>();
                var skinnedMesh = t.GetComponent<SkinnedMeshRenderer>();
                bool hasStaticMesh = meshFilter != null && meshFilter.sharedMesh != null;
                bool hasSkinnedMesh = skinnedMesh != null && skinnedMesh.sharedMesh != null;

                if (!hasStaticMesh && !hasSkinnedMesh) return false;

                bool applyPos = (hasStaticMesh && settings.applyPositionToStaticMeshes) || (hasSkinnedMesh && settings.applyPositionToSkinnedMeshes);
                bool applyRot = (hasStaticMesh && settings.applyRotationToStaticMeshes) || (hasSkinnedMesh && settings.applyRotationToSkinnedMeshes);
                bool applyScale = false;

                if (!applyPos && !applyRot && !applyScale) return false;

                var localPosition = applyPos ? t.localPosition : Vector3.zero;
                var localRotation = applyRot ? t.localRotation : Quaternion.identity;
                var localScale = applyScale ? t.localScale : Vector3.one;

                var bakeMatrix = Matrix4x4.TRS(localPosition, localRotation, localScale);
                if (bakeMatrix == Matrix4x4.identity) return false;

                bool bakedAny = false;
                if (hasStaticMesh && (settings.applyPositionToStaticMeshes || settings.applyRotationToStaticMeshes))
                {
                    if (BakeMeshFilterTransform(t, bakeMatrix))
                    {
                        bakedStaticMeshCount++;
                        bakedAny = true;
                    }
                }

                if (hasSkinnedMesh && (settings.applyPositionToSkinnedMeshes || settings.applyRotationToSkinnedMeshes))
                {
                    if (BakeSkinnedMeshTransform(skinnedMesh, bakeMatrix))
                    {
                        bakedSkinnedMeshCount++;
                        bakedAny = true;
                    }
                }

                if (!bakedAny) return false;

                ApplySelectedLocalTransformToChildren(t, localPosition, localRotation, localScale, applyPos, applyRot, applyScale);

                if (applyPos) t.localPosition = Vector3.zero;
                if (applyRot) t.localRotation = Quaternion.identity;
                if (applyScale) t.localScale = Vector3.one;

                resetTransformCount++;
                return true;
            }

            void RecursiveBakeMeshTransforms(Transform t)
            {
                BakeMeshTransformsOnNode(t);
                foreach (Transform child in t) RecursiveBakeMeshTransforms(child);
            }

            RecursiveScaleNormalize(root);
            if (isApplyMode)
                RecursiveBakeMeshTransforms(root);

            if (ShouldLogInfo(merger))
            {
                mergeLog.AppendLine($"  [Scale Normalization] Complete: scaled {scaledCount} transform(s), skipped {skippedProtectedScaleCount} protected skeleton transform(s)");
                if (isApplyMode)
                    mergeLog.AppendLine($"  [Scale Normalization] Apply Transform-like: baked static={bakedStaticMeshCount}, skinned={bakedSkinnedMeshCount}, resetTransforms={resetTransformCount}");
            }
        }

        private static void ApplySelectedLocalTransformToChildren(
            Transform t,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            bool applyPosition,
            bool applyRotation,
            bool applyScale)
        {
            if (t == null) return;

            var matrix = Matrix4x4.TRS(
                applyPosition ? localPosition : Vector3.zero,
                applyRotation ? localRotation : Quaternion.identity,
                applyScale ? localScale : Vector3.one);

            foreach (Transform child in t)
            {
                child.localPosition = matrix.MultiplyPoint3x4(child.localPosition);
                if (applyRotation)
                    child.localRotation = localRotation * child.localRotation;
                if (applyScale)
                    child.localScale = Vector3.Scale(localScale, child.localScale);
            }
        }

        private static HashSet<Transform> BuildProtectedSkeletonTransforms(Transform normalizeRoot, Transform skinnedMeshSearchRoot)
        {
            var protectedTransforms = new HashSet<Transform>();
            if (normalizeRoot == null || skinnedMeshSearchRoot == null) return protectedTransforms;

            var skinnedMeshes = skinnedMeshSearchRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinnedMeshes)
            {
                if (smr == null || smr.bones == null) continue;
                foreach (var bone in smr.bones)
                {
                    if (bone == null) continue;

                    // Only protect bones that are actually inside the normalization subtree.
                    if (!bone.IsChildOf(normalizeRoot) && bone != normalizeRoot)
                        continue;

                    var current = bone;
                    while (current != null)
                    {
                        if (!protectedTransforms.Add(current)) break;
                        if (current == normalizeRoot) break;
                        current = current.parent;
                    }
                }
            }

            return protectedTransforms;
        }

        private bool BakeMeshFilterTransform(Transform t, Matrix4x4 localMatrix)
        {
            if (t == null) return false;
            var meshFilter = t.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return false;

            var mesh = EnsureSceneMeshInstance(meshFilter, "NDMF Apply Transform");
            if (mesh == null) return false;

            var vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0) return false;

            var normalMatrix = localMatrix.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = localMatrix.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;

            var normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                mesh.normals = normals;
            }

            var tangents = mesh.tangents;
            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    var tangent = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                    tangent = normalMatrix.MultiplyVector(tangent).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
                }
                mesh.tangents = tangents;
            }

            mesh.RecalculateBounds();
            return true;
        }

        private bool BakeSkinnedMeshTransform(SkinnedMeshRenderer smr, Matrix4x4 localMatrix)
        {
            if (smr == null || smr.sharedMesh == null) return false;

            var mesh = EnsureSceneMeshInstance(smr, "NDMF Apply Transform");
            if (mesh == null) return false;

            var vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0) return false;

            var normalMatrix = localMatrix.inverse.transpose;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = localMatrix.MultiplyPoint3x4(vertices[i]);
            mesh.vertices = vertices;

            var normals = mesh.normals;
            if (normals != null && normals.Length == vertices.Length)
            {
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                mesh.normals = normals;
            }

            var tangents = mesh.tangents;
            if (tangents != null && tangents.Length == vertices.Length)
            {
                for (int i = 0; i < tangents.Length; i++)
                {
                    var tangent = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                    tangent = normalMatrix.MultiplyVector(tangent).normalized;
                    tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, tangents[i].w);
                }
                mesh.tangents = tangents;
            }

            var bindposes = mesh.bindposes;
            if (bindposes != null && bindposes.Length > 0)
            {
                var invBake = localMatrix.inverse;
                for (int i = 0; i < bindposes.Length; i++)
                    bindposes[i] = bindposes[i] * invBake;
                mesh.bindposes = bindposes;
            }

            mesh.RecalculateBounds();
            return true;
        }

        // [NEW FEATURE] Find body mesh for bounds copying
        private SkinnedMeshRenderer FindBodyReferenceMesh(Transform targetArmature, CVRMergeArmature merger = null)
        {
            var smrs = targetArmature.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var result = smrs.OrderByDescending(s => s.sharedMesh != null ? s.sharedMesh.vertexCount : 0).FirstOrDefault();
            if (merger != null && ShouldLogDetailed(merger) && result != null)
                mergeLog.AppendLine($"  [Body Reference] Found body mesh '{result.name}' with {result.sharedMesh?.vertexCount ?? 0} vertices");
            return result;
        }

        // ============================================================
        // UNIVERSAL POST-MERGE REMAPPER (Serialized + Reflection)
        // ============================================================
        private void RemapExternalReferencesUniversal(Transform avatarRoot, CVRMergeArmature merger, Dictionary<int, Transform> mergedBoneReferences)
        {
            if (avatarRoot == null) return;

            if (ShouldLogDetailed(merger))
                mergeLog.AppendLine("Remapping scene references with bone-aware merge mapping (universal v3)...");

            var allComponents = avatarRoot.GetComponentsInChildren<Component>(true);

            int changedComponents = 0;
            int serializedRemapped = 0, serializedNulled = 0;
            int reflectionRemapped = 0, reflectionNulled = 0;

            foreach (var comp in allComponents)
            {
                if (comp == null || comp is Transform) continue;

                bool compChanged = false;

                try
                {
                    var so = new SerializedObject(comp);
                    var prop = so.GetIterator();
                    bool enterChildren = true;
                    while (prop.Next(enterChildren))
                    {
                        enterChildren = true;

                        if (prop.propertyType != SerializedPropertyType.ObjectReference &&
                            prop.propertyType != SerializedPropertyType.ExposedReference)
                            continue;

                        if (!TryResolveSerializedObjectReference(prop, avatarRoot, mergedBoneReferences, out var newObj)) continue;

                        if (newObj != null)
                        {
                            prop.objectReferenceValue = newObj;
                            serializedRemapped++;
                            compChanged = true;
                        }
                        else
                        {
                            prop.objectReferenceValue = null;
                            serializedNulled++;
                            compChanged = true;
                        }
                    }

                    if (compChanged)
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(comp);
                    }
                }
                catch { }

                try
                {
                    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    if (ReflectionRemapObjectGraph(comp, avatarRoot, mergedBoneReferences, visited, ref reflectionRemapped, ref reflectionNulled))
                    {
                        compChanged = true;
                        EditorUtility.SetDirty(comp);
                    }
                }
                catch { }

                if (compChanged) changedComponents++;
            }

            if (ShouldLogDetailed(merger))
                mergeLog.AppendLine(
                    $"Universal ref remap done. Components changed: {changedComponents}.\n" +
                    $"  Serialized pass -> remapped: {serializedRemapped}, nulled: {serializedNulled}\n" +
                    $"  Reflection pass -> remapped: {reflectionRemapped}, nulled: {reflectionNulled}"
                );
        }

        private bool IsSceneObjectOutsideClone(UnityEngine.Object objRef, Transform avatarRoot, out Transform refTransform)
        {
            refTransform = null;

            if (objRef is GameObject go) refTransform = go.transform;
            else if (objRef is Component c) refTransform = c.transform;
            else if (objRef is Transform t) refTransform = t;

            if (refTransform == null) return false;
            if (refTransform.IsChildOf(avatarRoot)) return false;

            return true;
        }

        private bool TryResolveRemappedTransformReference(UnityEngine.Object objRef, Transform avatarRoot, Dictionary<int, Transform> mergedBoneReferences, out Transform mappedTransform)
        {
            mappedTransform = null;
            if (objRef == null) return false;

            if (TryResolveMergedBoneReference(objRef, objRef.GetInstanceID(), mergedBoneReferences, out mappedTransform))
                return true;

            if (!IsSceneObjectOutsideClone(objRef, avatarRoot, out var refTransform))
                return false;

            mappedTransform = TryResolveTransformInClone(avatarRoot, refTransform);
            return true;
        }

        private bool TryResolveMergedBoneReference(UnityEngine.Object objRef, int sourceInstanceId, Dictionary<int, Transform> mergedBoneReferences, out Transform mappedTransform)
        {
            mappedTransform = null;
            if (mergedBoneReferences == null || mergedBoneReferences.Count == 0) return false;

            if (objRef != null)
            {
                int instanceId = objRef.GetInstanceID();
                if (mergedBoneReferences.TryGetValue(instanceId, out mappedTransform) && mappedTransform != null)
                    return true;
            }

            if (sourceInstanceId != 0 && mergedBoneReferences.TryGetValue(sourceInstanceId, out mappedTransform) && mappedTransform != null)
                return true;

            return false;
        }

        private bool TryResolveSerializedObjectReference(SerializedProperty prop, Transform avatarRoot, Dictionary<int, Transform> mergedBoneReferences, out UnityEngine.Object newObj)
        {
            newObj = null;
            if (prop == null) return false;

            var objRef = prop.objectReferenceValue;
            int sourceInstanceId = prop.objectReferenceInstanceIDValue;

            if (TryResolveMergedBoneReference(objRef, sourceInstanceId, mergedBoneReferences, out var mergedBone))
            {
                newObj = objRef != null ? ConvertMappedObject(objRef, mergedBone) : mergedBone;
                return true;
            }

            if (objRef == null)
                return false;

            if (!IsSceneObjectOutsideClone(objRef, avatarRoot, out var refTransform))
                return false;

            var mappedTransform = TryResolveTransformInClone(avatarRoot, refTransform);
            newObj = mappedTransform != null ? ConvertMappedObject(objRef, mappedTransform) : null;
            return true;
        }

        private bool TryRemapSceneObjectReference(UnityEngine.Object objRef, Transform avatarRoot, Dictionary<int, Transform> mergedBoneReferences, out UnityEngine.Object newObj)
        {
            newObj = null;
            if (objRef == null) return false;

            if (TryResolveMergedBoneReference(objRef, objRef.GetInstanceID(), mergedBoneReferences, out var mergedBone))
            {
                newObj = ConvertMappedObject(objRef, mergedBone);
                return true;
            }

            if (!IsSceneObjectOutsideClone(objRef, avatarRoot, out var refTransform))
                return false;

            var mappedTransform = TryResolveTransformInClone(avatarRoot, refTransform);
            newObj = mappedTransform != null ? ConvertMappedObject(objRef, mappedTransform) : null;
            return true;
        }

        private void RegisterMergedBoneReferences(Dictionary<Transform, Transform> boneMap, Dictionary<int, Transform> mergedBoneReferences)
        {
            if (boneMap == null || mergedBoneReferences == null) return;

            foreach (var pair in boneMap)
            {
                if (pair.Key == null || pair.Value == null) continue;
                mergedBoneReferences[pair.Key.GetInstanceID()] = pair.Value;
            }
        }

        private UnityEngine.Object ConvertMappedObject(UnityEngine.Object oldObj, Transform mappedTransform)
        {
            if (oldObj is GameObject) return mappedTransform.gameObject;
            if (oldObj is Transform) return mappedTransform;

            if (oldObj is Component oldComp)
            {
                var mappedComp = mappedTransform.GetComponent(oldComp.GetType());
                if (mappedComp != null) return mappedComp;
            }

            return null;
        }

        private bool ReflectionRemapObjectGraph(object rootObj, Transform avatarRoot, Dictionary<int, Transform> mergedBoneReferences, HashSet<object> visited, ref int remappedCount, ref int nulledCount)
        {
            if (rootObj == null || !visited.Add(rootObj)) return false;

            bool changed = false;
            var type = rootObj.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
            if (rootObj is UnityEngine.Object) return false;

            if (rootObj is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    if (item == null) continue;

                    if (item is UnityEngine.Object listObj && TryRemapSceneObjectReference(listObj, avatarRoot, mergedBoneReferences, out var newObj))
                    {
                        if (newObj != null) { list[i] = newObj; remappedCount++; }
                        else { list[i] = null; nulledCount++; }

                        changed = true;
                        continue;
                    }

                    if (!(item is UnityEngine.Object))
                        changed |= ReflectionRemapObjectGraph(item, avatarRoot, mergedBoneReferences, visited, ref remappedCount, ref nulledCount);
                }
                return changed;
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fields = type.GetFields(flags);

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;

                bool isPublic = field.IsPublic;
                bool hasSerializeField = field.GetCustomAttribute<SerializeField>() != null;
                if (!isPublic && !hasSerializeField) continue;

                var fType = field.FieldType;
                var value = field.GetValue(rootObj);
                if (value == null) continue;

                if (typeof(UnityEngine.Object).IsAssignableFrom(fType))
                {
                    var uoVal = value as UnityEngine.Object;
                    if (uoVal != null && TryRemapSceneObjectReference(uoVal, avatarRoot, mergedBoneReferences, out var newObj))
                    {
                        if (newObj != null && fType.IsAssignableFrom(newObj.GetType()))
                        {
                            field.SetValue(rootObj, newObj);
                            remappedCount++;
                        }
                        else
                        {
                            field.SetValue(rootObj, null);
                            nulledCount++;
                        }

                        changed = true;
                        continue;
                    }
                    continue;
                }

                if (fType.IsArray)
                {
                    var arr = value as Array;
                    if (arr == null) continue;

                    for (int i = 0; i < arr.Length; i++)
                    {
                        var item = arr.GetValue(i);
                        if (item == null) continue;

                        if (item is UnityEngine.Object arrObj && TryRemapSceneObjectReference(arrObj, avatarRoot, mergedBoneReferences, out var newObj))
                        {
                            if (newObj != null) { arr.SetValue(newObj, i); remappedCount++; }
                            else { arr.SetValue(null, i); nulledCount++; }

                            changed = true;
                            continue;
                        }

                        if (!(item is UnityEngine.Object))
                            changed |= ReflectionRemapObjectGraph(item, avatarRoot, mergedBoneReferences, visited, ref remappedCount, ref nulledCount);
                    }
                    continue;
                }

                if (!fType.IsPrimitive && !fType.IsEnum && fType != typeof(string))
                {
                    changed |= ReflectionRemapObjectGraph(value, avatarRoot, mergedBoneReferences, visited, ref remappedCount, ref nulledCount);
                }
            }

            return changed;
        }

        private Transform TryResolveTransformInClone(Transform avatarRoot, Transform external)
        {
            if (avatarRoot == null || external == null) return null;

            var names = new List<string>();
            var cur = external;
            while (cur != null)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }
            names.Reverse();

            for (int start = 0; start < names.Count; start++)
            {
                var candidatePath = string.Join("/", names.Skip(start));
                var found = avatarRoot.Find(candidatePath);
                if (found != null) return found;
            }

            var byName = FindBoneByName(avatarRoot, external.name);
            if (byName != null) return byName;

            return null;
        }

        // ------------------------------------------------------------
        // POST-MERGE: Magica data rebuild (DIRECT API, ORDERED)
        // ------------------------------------------------------------
        private void RebuildMagicaData(Transform avatarRoot, CVRMergeArmature merger)
        {
#if CCK_ADDIN_MAGICACLOTHSUPPORT
            // SAFETY CHECK: avatarRoot should be the NDMF clone root, validated by caller
            var renderDeformers = avatarRoot.GetComponentsInChildren<MagicaRenderDeformer>(true).ToList();
            if (ShouldLogDetailed(merger))
                mergeLog.AppendLine($"Rebuilding MagicaRenderDeformer data: {renderDeformers.Count}");
            foreach (var rd in renderDeformers)
            {
                if (rd == null) continue;
                BuildManager.CreateComponent(rd);
                EditorUtility.SetDirty(rd);
            }

            var virtualDeformers = avatarRoot.GetComponentsInChildren<MagicaVirtualDeformer>(true).ToList();
            if (ShouldLogDetailed(merger))
                mergeLog.AppendLine($"Rebuilding MagicaVirtualDeformer data: {virtualDeformers.Count}");
            foreach (var vd in virtualDeformers)
            {
                if (vd == null) continue;
                BuildManager.CreateComponent(vd);
                EditorUtility.SetDirty(vd);
            }

            var boneCloths = avatarRoot.GetComponentsInChildren<MagicaBoneCloth>(true).ToList();
            if (ShouldLogDetailed(merger))
                mergeLog.AppendLine($"Rebuilding MagicaBoneCloth data: {boneCloths.Count}");
            foreach (var bc in boneCloths)
            {
                if (bc == null) continue;
                BuildManager.CreateComponent(bc);
                EditorUtility.SetDirty(bc);
            }

            var meshCloths = avatarRoot.GetComponentsInChildren<MagicaMeshCloth>(true).ToList();
            if (ShouldLogDetailed(merger))
                mergeLog.AppendLine($"Rebuilding MagicaMeshCloth data: {meshCloths.Count}");
            foreach (var mc in meshCloths)
            {
                if (mc == null) continue;
                BuildManager.CreateComponent(mc);
                EditorUtility.SetDirty(mc);
            }
#endif
        }

        // ------------------------------------------------------------
        // POST-MERGE: AAS controller generation
        // ------------------------------------------------------------
        private void GenerateAASControllerAtEnd(Component targetCVRAvatar, Transform avatarRootClone, CVRMergeArmature merger)
        {
            if (targetCVRAvatar == null) return;

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine("  [AAS End-Gen] Starting post-merge AAS controller generation...");
            
            // SAFETY CHECK: Only modify if component is part of the NDMF clone hierarchy
            if (avatarRootClone != null && !targetCVRAvatar.transform.IsChildOf(avatarRootClone))
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine("AAS end-gen: skipped - target CVRAvatar is not in clone hierarchy");
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine("  [AAS End-Gen] Skipped: target CVRAvatar is not in clone hierarchy");
                if (ShouldLogDetailed(merger))
                    mergeLog.AppendLine("AAS end-gen: Skipped - target CVRAvatar is not in clone hierarchy");
                return;
            }

            var advSettings = GetAdvancedAvatarSettings(targetCVRAvatar);
            if (advSettings == null)
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine("AAS end-gen: skipped - no avatarSettings found on target");
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine("  [AAS End-Gen] Skipped: no avatarSettings found on target");
                if (ShouldLogDetailed(merger))
                    mergeLog.AppendLine("AAS end-gen: No avatarSettings found on target.");
                return;
            }

            EnsureSettingsListExists(advSettings);
            var settingsList = GetSettingsList(advSettings);
            if (settingsList == null || settingsList.Count == 0)
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine("AAS end-gen: skipped - settings list empty");
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine("  [AAS End-Gen] Skipped: settings list empty");
                if (ShouldLogDetailed(merger))
                    mergeLog.AppendLine("AAS end-gen: settings list empty.");
                return;
            }

            EnsureRootFolder();

            string avatarName = targetCVRAvatar.gameObject.name;
            string safeAvatarName = SanitizeFileName(avatarName);
            string avatarAASFolder = $"{ROOT_GEN_FOLDER}/{safeAvatarName}_AAS";
            string controllerPath = $"{avatarAASFolder}/{safeAvatarName}_aas.controller";
            string overridePath = $"{avatarAASFolder}/{safeAvatarName}_aas_overrides.overrideController";
            string animFolder = $"{avatarAASFolder}/{safeAvatarName}_AAS_Anims";

            if (!AssetDatabase.IsValidFolder(avatarAASFolder))
                AssetDatabase.CreateFolder(ROOT_GEN_FOLDER, $"{safeAvatarName}_AAS");
            if (!AssetDatabase.IsValidFolder(animFolder))
                AssetDatabase.CreateFolder(avatarAASFolder, $"{safeAvatarName}_AAS_Anims");
            else
            {
                var animGUIDs = AssetDatabase.FindAssets("", new[] { animFolder });
                foreach (var guid in animGUIDs)
                    AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }

            RuntimeAnimatorController baseRuntime = null;
            RuntimeAnimatorController baseOverrideRuntime = null;

            {
                var fBase = advSettings.GetType().GetField("baseController");
                if (fBase != null) baseRuntime = fBase.GetValue(advSettings) as RuntimeAnimatorController;

                var fBaseOv = advSettings.GetType().GetField("baseOverrideController");
                if (fBaseOv != null) baseOverrideRuntime = fBaseOv.GetValue(advSettings) as RuntimeAnimatorController;
            }

            AnimatorController baseController = null;
            if (baseRuntime is AnimatorController ac)
                baseController = ac;
            else if (baseRuntime is AnimatorOverrideController aoc && aoc.runtimeAnimatorController is AnimatorController bac)
                baseController = bac;

            if (baseController == null)
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine("AAS end-gen: skipped - avatarSettings.baseController missing or not AnimatorController");
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine("  [AAS End-Gen] Skipped: base controller missing or not an AnimatorController");
                mergeLog.AppendLine("AAS end-gen: ERROR - avatarSettings.baseController missing or not AnimatorController.");
                return;
            }

            AnimatorController newController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (newController != null)
            {
                EditorUtility.CopySerialized(baseController, newController);
                newController.name = $"{safeAvatarName}_aas";
            }
            else
            {
                newController = UnityEngine.Object.Instantiate(baseController);
                newController.name = $"{safeAvatarName}_aas";
                AssetDatabase.CreateAsset(newController, controllerPath);
            }

            var baseParams = new HashSet<string>();
            foreach (var p in newController.parameters)
            {
                if (p == null || string.IsNullOrEmpty(p.name) || p.name.StartsWith("#")) continue;
                baseParams.Add(p.name);
            }

            int createdEntries = 0;
            foreach (var entry in settingsList)
            {
                if (entry == null) continue;
                string machineName = GetEntryMachineName(entry);
                if (string.IsNullOrEmpty(machineName) || baseParams.Contains(machineName)) continue;

                var settingProp = entry.GetType().GetProperty("setting");
                var settingObj = settingProp?.GetValue(entry);
                if (settingObj == null) continue;

                var setupMethod = settingObj.GetType().GetMethod(
                    "SetupAnimator",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (setupMethod == null) continue;

                try
                {
                    object[] args = { newController, machineName, animFolder, SanitizeFileName(machineName) };
                    setupMethod.Invoke(settingObj, args);
                    newController = args[0] as AnimatorController ?? newController;
                    createdEntries++;
                }
                catch (Exception ex)
                {
                    mergeLog.AppendLine($"AAS end-gen: SetupAnimator failed '{machineName}': {ex.Message}");
                }
            }

            EditorUtility.SetDirty(newController);

            SetAdvAnimator(advSettings, newController);

            AnimatorOverrideController overrideController = FixOrCreateOverrideController(
                advSettings,
                baseOverrideRuntime,
                newController,
                overridePath,
                safeAvatarName);

            AttachCreatedOverrideToAvatar(targetCVRAvatar, overrideController, avatarRootClone);
            TrySetActualControllerOnAvatar(targetCVRAvatar, newController, avatarRootClone);

            EditorUtility.SetDirty(targetCVRAvatar);

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [AAS End-Gen] Complete: added {createdEntries} AAS entries");
            if (ShouldLogDetailed(merger))
                mergeLog.AppendLine($"AAS end-gen: added {createdEntries} AAS entries.");
        }

        private void EnsureRootFolder()
        {
            if (!AssetDatabase.IsValidFolder(ROOT_GEN_FOLDER))
            {
                string[] split = ROOT_GEN_FOLDER.Split('/');
                if (split.Length > 1 && !AssetDatabase.IsValidFolder(ROOT_GEN_FOLDER))
                    AssetDatabase.CreateFolder(split[0], split[1]);
            }
        }

        private void SetAdvAnimator(object advSettings, AnimatorController animator)
        {
            if (advSettings == null) return;
            var f = advSettings.GetType().GetField("animator");
            if (f != null && typeof(AnimatorController).IsAssignableFrom(f.FieldType))
                f.SetValue(advSettings, animator);
        }

        private AnimatorOverrideController FixOrCreateOverrideController(
            object advSettings,
            RuntimeAnimatorController baseOverrideRuntime,
            AnimatorController generatedAnimator,
            string overridePath,
            string safeAvatarName)
        {
            AnimatorOverrideController overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(overridePath);

            if (overrides != null)
            {
                if (baseOverrideRuntime is AnimatorOverrideController baseOv)
                {
                    EditorUtility.CopySerialized(baseOv, overrides);
                    overrides.name = $"{safeAvatarName}_aas_overrides";
                }
                else
                {
                    var blank = new AnimatorOverrideController();
                    EditorUtility.CopySerialized(blank, overrides);
                    overrides.name = $"{safeAvatarName}_aas_overrides";
                    UnityEngine.Object.DestroyImmediate(blank);
                }
            }
            else
            {
                if (baseOverrideRuntime is AnimatorOverrideController baseOv)
                    overrides = UnityEngine.Object.Instantiate(baseOv);
                else
                    overrides = new AnimatorOverrideController();

                overrides.name = $"{safeAvatarName}_aas_overrides";
                AssetDatabase.CreateAsset(overrides, overridePath);
            }

            overrides.runtimeAnimatorController = generatedAnimator;
            EditorUtility.SetDirty(overrides);

            var fOv = advSettings.GetType().GetField("overrides");
            if (fOv != null && typeof(AnimatorOverrideController).IsAssignableFrom(fOv.FieldType))
                fOv.SetValue(advSettings, overrides);

            return overrides;
        }

        private void AttachCreatedOverrideToAvatar(Component avatarComp, AnimatorOverrideController overrideController, Transform avatarRootClone)
        {
            if (avatarComp == null || overrideController == null) return;
            
            // SAFETY CHECK: Only modify if component is part of the NDMF clone hierarchy
            if (avatarRootClone != null && !avatarComp.transform.IsChildOf(avatarRootClone))
            {
                return; // Silently skip if not in clone
            }
            
            Undo.RecordObject(avatarComp, "Attach created Override to Avatar");

            var f = avatarComp.GetType().GetField("overrides");
            if (f != null && typeof(AnimatorOverrideController).IsAssignableFrom(f.FieldType))
            {
                f.SetValue(avatarComp, overrideController);
                EditorUtility.SetDirty(avatarComp);
                return;
            }

            TrySetOverrideControllerOnAvatar(avatarComp, overrideController);
        }

        private void TrySetOverrideControllerOnAvatar(Component cvrAvatar, RuntimeAnimatorController controller)
        {
            if (cvrAvatar == null || controller == null) return;
            var t = cvrAvatar.GetType();

            var fields = new[] { "overrides", "animationOverrides", "avatarOverrideController" };
            foreach (var name in fields)
            {
                var f = t.GetField(name);
                if (f != null && typeof(RuntimeAnimatorController).IsAssignableFrom(f.FieldType))
                {
                    f.SetValue(cvrAvatar, controller);
                    EditorUtility.SetDirty(cvrAvatar);
                    return;
                }
            }
        }

        private void TrySetActualControllerOnAvatar(Component cvrAvatar, RuntimeAnimatorController controller, Transform avatarRootClone = null)
        {
            if (cvrAvatar == null || controller == null) return;
            
            // SAFETY CHECK: Only modify if component is part of the NDMF clone hierarchy (if clone context provided)
            if (avatarRootClone != null && !cvrAvatar.transform.IsChildOf(avatarRootClone))
            {
                return; // Silently skip if not in clone
            }
            
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

        private string SanitizeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unnamed";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        // ----------------------------
        // Outfit merge (Standard)
        // ----------------------------
        private void MergeOutfitAsObject(BuildContext ctx, CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform outfitRoot)
        {
            if (ctx == null || outfitRoot == null || outfitEntry == null) return;

            var objectSettings = outfitEntry.objectMergeSettings ?? new ObjectMergeSettings();
            var targetParent = objectSettings.targetParent != null
                ? ResolveTransformInCloneHierarchy(objectSettings.targetParent, ctx.AvatarRootTransform)
                : ctx.AvatarRootTransform;

            if (targetParent == null)
            {
                mergeLog.AppendLine($"  WARNING: Object mode target parent missing for {outfitRoot.name}, skipping.");
                return;
            }

            var childrenToMove = new List<Transform>();
            foreach (Transform child in outfitRoot)
                childrenToMove.Add(child);

            var placementMode = objectSettings.placementMode;
            var targetBone = objectSettings.targetBone != null
                ? ResolveTransformInCloneHierarchy(objectSettings.targetBone, ctx.AvatarRootTransform)
                : null;
            var referenceTransform = objectSettings.referenceTransform != null
                ? ResolveTransformInCloneHierarchy(objectSettings.referenceTransform, ctx.AvatarRootTransform)
                : null;

            Quaternion valueRotation = Quaternion.Euler(objectSettings.localRotationOffset);

            foreach (var child in childrenToMove)
            {
                switch (placementMode)
                {
                    case ObjectPlacementMode.PlaceUnderSelectedGameObject:
                        child.SetParent(targetParent, true);
                        break;

                    case ObjectPlacementMode.WeightMeshesToTargetBoneAtCurrentPosition:
                        child.SetParent(targetParent, true);
                        if (targetBone != null)
                            WeightMeshesToTargetBone(child, targetBone);
                        break;

                    case ObjectPlacementMode.PlaceUsingReference:
                        child.SetParent(targetParent, true);
                        if (referenceTransform != null)
                        {
                            child.position = referenceTransform.position;
                            child.rotation = referenceTransform.rotation;
                            child.localScale = referenceTransform.localScale;
                        }
                        break;

                    case ObjectPlacementMode.PlaceUsingValues:
                    default:
                        child.SetParent(targetParent, false);
                        child.localPosition = objectSettings.localPositionOffset;
                        child.localRotation = valueRotation;
                        child.localScale = objectSettings.localScaleOffset;
                        break;
                }
            }

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  Object mode merge complete: moved {childrenToMove.Count} object(s) to {targetParent.name}");
        }

        private void WeightMeshesToTargetBone(Transform root, Transform targetBone)
        {
            if (root == null || targetBone == null) return;

            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr == null) continue;

                smr.rootBone = targetBone;
                if (smr.bones == null || smr.bones.Length == 0)
                {
                    smr.bones = new[] { targetBone };
                }
            }
        }

        private void MergeOutfitWithoutDestroy(BuildContext ctx, CVRMergeArmature merger, OutfitToMerge outfitEntry, Transform outfitRoot, Transform targetArmature, SkinnedMeshRenderer bodyReferenceMesh, Dictionary<OutfitToMerge, Transform> clonedOutfitByEntry, Dictionary<int, Transform> mergedBoneReferences, Dictionary<AnimationClip, AnimationClip> clipReplacementMap)
        {
            var armatureSettings = GetArmatureMergingSettings(merger);

            Transform outfitArmature = FindArmatureInOutfit(outfitRoot);
            if (outfitArmature == null)
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine($"  WARNING: Could not find armature in outfit, using root");
                outfitArmature = outfitRoot;
            }
            else
            {
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine($"  Outfit armature: {outfitArmature.name}");
            }

            var usedBones = GetBonesUsedByMeshes(outfitRoot);
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  Used bones: {usedBones.Count}");

            if (usedBones.Count == 0)
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine($"  WARNING: No used bones found, skipping");
                return;
            }

            // Pre-merge validations and mesh prep
            if (merger.preMergeValidationSettings != null)
                RunPreMergeValidations(merger, outfitEntry, targetArmature, merger.preMergeValidationSettings);

            if (merger.uvValidationSettings != null)
                ValidateAndFixUVs(outfitRoot, merger.uvValidationSettings, merger);

            if (merger.materialConsolidationSettings != null)
                ConsolidateMaterials(outfitRoot, merger.materialConsolidationSettings, merger);

            if (!string.IsNullOrEmpty(outfitEntry.uniqueBonePrefix))
                ApplyUniqueBonePrefix(outfitArmature, targetArmature, outfitEntry, merger, usedBones);

            if (!string.IsNullOrEmpty(outfitEntry.meshPrefix))
                ApplyMeshPrefix(outfitRoot, outfitEntry.meshPrefix);

            var bonesToReparent = new List<(Transform source, Transform target)>();

            var bindposeCompensationBySourceBone = new Dictionary<Transform, Matrix4x4>();
            var boneMap = BuildBoneMappingWithConflicts(merger, outfitEntry, outfitArmature, targetArmature, usedBones, bonesToReparent, bindposeCompensationBySourceBone, outfitEntry.prefix, outfitEntry.suffix);
            ApplySemanticBoneMatchingAdjustments(boneMap, merger.semanticBoneMatchingSettings, merger);
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  Bone map entries: {boneMap.Count}");

            RegisterMergedBoneReferences(boneMap, mergedBoneReferences);

            var skinnedMeshes = outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  SkinnedMeshRenderers: {skinnedMeshes.Length}");

            // Determine reference body mesh for this outfit when needed
            SkinnedMeshRenderer referenceForThisOutfit = null;
            if (outfitEntry.boundsFixMode == BoundsFixMode.CopyFromSelected)
            {
                referenceForThisOutfit = outfitEntry.referenceBodyMesh != null
                    ? outfitEntry.referenceBodyMesh
                    : FindBodyReferenceMesh(targetArmature, merger);
            }

            foreach (var smr in skinnedMeshes)
            {
                if (smr == null) continue;
                if (!merger.IsExcluded(smr.transform))
                {
                    RemapSkinnedMeshRenderer(smr, boneMap, bindposeCompensationBySourceBone, armatureSettings, merger);

                    // [NEW FEATURE] Bounds Fix Mode
                    ApplyBoundsFix(smr, outfitEntry.boundsFixMode, referenceForThisOutfit, merger);

                    // [NEW FEATURE] Probe Anchor Sync Mode
                    if (outfitEntry.probeAnchorSyncMode != ProbeAnchorSyncMode.None)
                    {
                        SkinnedMeshRenderer probeReference = null;
                        
                        if (outfitEntry.probeAnchorSyncMode == ProbeAnchorSyncMode.CopyFromSelected)
                        {
                            probeReference = outfitEntry.referenceProbeAnchorMesh != null 
                                ? outfitEntry.referenceProbeAnchorMesh 
                                : referenceForThisOutfit;
                        }
                        else if (outfitEntry.probeAnchorSyncMode == ProbeAnchorSyncMode.AutoDetect)
                        {
                            probeReference = referenceForThisOutfit ?? FindBodyReferenceMesh(targetArmature, merger);
                        }
                        
                        if (probeReference != null)
                        {
                            smr.probeAnchor = probeReference.probeAnchor;
                            smr.lightProbeUsage = probeReference.lightProbeUsage;
                            smr.reflectionProbeUsage = probeReference.reflectionProbeUsage;
                        }
                    }
                }
            }

            MergeHierarchy(outfitArmature, targetArmature, boneMap, merger, usedBones);

            // [NEW FEATURE] Remove Unused Bones
            if (outfitEntry.removeUnusedBones)
            {
                RemoveUnusedBones(targetArmature, usedBones, merger);
            }

            foreach (var pair in bonesToReparent)
            {
                var source = pair.source;
                var target = pair.target;

                if (source == null || target == null) continue;

                source.SetParent(target, true);

                if (ShouldLogDetailed(merger))
                    mergeLog.AppendLine($"  [Reparent] Reparented {source.name} under {target.name} while preserving world pose");
            }

            foreach (var smr in skinnedMeshes)
            {
                if (smr == null) continue;
                if (!merger.IsExcluded(smr.transform))
                    smr.transform.SetParent(ctx.AvatarRootTransform, true);
            }

            MergeDynamicComponents(outfitRoot, boneMap, merger);

            Component targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar != null)
            {
                if (merger.mergeAdvancedPointerTrigger) MergeAdvancedPointerTrigger(outfitRoot);
                if (merger.mergeParameterStream) MergeParameterStream(outfitRoot, targetCVRAvatar, ctx.AvatarRootTransform, merger);
                if (merger.mergeAnimatorDriver) MergeAnimatorDriver(outfitRoot, targetCVRAvatar, ctx.AvatarRootTransform, merger);
            }

            var remainingChildren = new List<Transform>();
            foreach (Transform child in outfitRoot)
                if (child != null && child.parent == outfitRoot)
                    remainingChildren.Add(child);

            foreach (var child in remainingChildren)
                if (child != null && child.GetComponentInChildren<Component>(true) != null)
                    child.SetParent(ctx.AvatarRootTransform, true);

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  Outfit merge complete");

            // Post-step: Blend shape task pipeline (task-scoped direction + options)
            if (merger.blendShapeTransferSettings != null)
            {
                merger.blendShapeTransferSettings.EnsureMigrated();
                if (merger.blendShapeTransferSettings.tasks != null)
                {
                    foreach (var task in merger.blendShapeTransferSettings.tasks)
                    {
                        if (task == null || !task.enabled) continue;

                        TransferBlendShapes(ctx.AvatarRootTransform, outfitRoot, targetArmature, task, merger.blendShapeTransferSettings, merger);

                        if (task.enableGeneration)
                            GenerateBlendShapesFromTask(ctx.AvatarRootTransform, targetArmature, task, clonedOutfitByEntry, merger.GetCVRAvatar(), merger, merger.blendShapeTransferSettings, clipReplacementMap);
                    }
                }
            }
        }

        // [NEW FEATURE] Bounds Fix Implementation
        private void ApplyBoundsFix(SkinnedMeshRenderer smr, BoundsFixMode mode, SkinnedMeshRenderer bodyReference, CVRMergeArmature merger)
        {
            switch (mode)
            {
                case BoundsFixMode.None:
                    break;
                case BoundsFixMode.CopyFromSelected:
                    if (bodyReference != null)
                    {
                        smr.localBounds = bodyReference.localBounds;
                        if (ShouldLogDetailed(merger))
                            mergeLog.AppendLine($"    Applied bounds copy from selected body mesh to {smr.name}");
                    }
                    break;
                case BoundsFixMode.RecalculateFromMesh:
                    if (smr.sharedMesh != null)
                    {
                        smr.localBounds = smr.sharedMesh.bounds;
                        if (ShouldLogDetailed(merger))
                            mergeLog.AppendLine($"    Recalculated bounds for {smr.name}");
                    }
                    break;
            }
        }

        // [NEW FEATURE] Remove Unused Bones
        private void RemoveUnusedBones(Transform armature, HashSet<Transform> usedBones, CVRMergeArmature merger)
        {
            var toRemove = new List<Transform>();

            void CheckBone(Transform bone)
            {
                if (!usedBones.Contains(bone))
                {
                    // Check if bone has any components or non-bone children
                    bool hasComponents = bone.GetComponents<Component>().Length > 1; // More than Transform
                    bool hasChildren = false;

                    foreach (Transform child in bone)
                    {
                        if (usedBones.Contains(child))
                            hasChildren = true;
                        else
                            CheckBone(child);
                    }

                    if (!hasComponents && !hasChildren)
                        toRemove.Add(bone);
                }
                else
                {
                    foreach (Transform child in bone)
                        CheckBone(child);
                }
            }

            CheckBone(armature);

            foreach (var bone in toRemove)
                if (bone != null) UnityEngine.Object.DestroyImmediate(bone.gameObject);

            if (toRemove.Count > 0)
                if (ShouldLogDetailed(merger))
                    mergeLog.AppendLine($"  Removed {toRemove.Count} unused bones");
        }

        // ----------------------------
        // Animator merge [IMPROVED with NEW FEATURES]
        // ----------------------------
        private void MergeAnimators(BuildContext ctx, CVRMergeArmature merger)
        {
            var clipReplacementMap = new Dictionary<AnimationClip, AnimationClip>();
            var targetCVRAvatar = merger.GetCVRAvatar();
            if (targetCVRAvatar == null) return;

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine("  [Animator Merge] Starting animator merge...");
            
            // SAFETY CHECK: Only modify if target component is part of the NDMF clone hierarchy
            if (ctx.AvatarRootTransform != null && !targetCVRAvatar.transform.IsChildOf(ctx.AvatarRootTransform))
            {
                if (ShouldLogDetailed(merger))
                    mergeLog.AppendLine("  MergeAnimators: Skipped - target CVRAvatar is not in clone hierarchy");
                return;
            }

            var targetAnimator = targetCVRAvatar.GetComponent<Animator>();
            if (targetAnimator == null)
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine("  WARNING: Animator merge skipped - target avatar has no Animator component");
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine("  [Animator Merge] Skipped: no Animator component on target avatar");
                return;
            }

            var targetController = GetPreferredController(targetCVRAvatar);
            if (targetController == null)
            {
                if (ShouldLogWarnings(merger))
                    mergeLog.AppendLine("  WARNING: Target has no animator controller");
                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine("  [Animator Merge] Skipped: no animator controller available");
                return;
            }

            bool? forceWD = null;
            if (targetController.layers.Length > 0)
            {
                var state = targetController.layers[0].stateMachine.defaultState;
                if (state != null) forceWD = state.writeDefaultValues;
            }

            var newController = UnityEngine.Object.Instantiate(targetController);
            newController.name = $"{NDMF_PREFIX}{targetController.name}_Merged";

            int parametersAdded = 0;
            int layersAdded = 0;
            int combinedLayerGroups = 0;

            // Pass 1: Collect and Add Parameters FIRST
            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;
                if (!outfitEntry.mergeAnimator && !outfitEntry.mergeAnimatorIncludingAAS) continue;

                var srcController = GetControllerFromOutfit(outfitEntry.outfit, targetCVRAvatar.GetType());
                if (srcController == null) continue;

                foreach (var param in srcController.parameters)
                {
                    if (!newController.parameters.Any(p => p.name == param.name))
                    {
                        newController.AddParameter(param.name, param.type);
                        parametersAdded++;
                    }
                }
            }

            // [NEW FEATURE] Track layer names for combining
            var layersByOriginalName = new Dictionary<string, List<AnimatorControllerLayer>>();

            // Pass 2: Copy Layers
            foreach (var outfitEntry in merger.outfitsToMerge)
            {
                if (outfitEntry.outfit == null) continue;

                bool basic = outfitEntry.mergeAnimator;
                bool includeAAS = outfitEntry.mergeAnimatorIncludingAAS;
                if (!basic && !includeAAS) continue;

                var srcController = GetControllerFromOutfit(outfitEntry.outfit, targetCVRAvatar.GetType());
                if (srcController == null) continue;

                var paramNames = new HashSet<string>(srcController.parameters.Select(p => p.name));

                foreach (var layer in srcController.layers)
                {
                    if (layer == null) continue;

                    bool isAutoLayer = paramNames.Contains(layer.name);
                    if (basic && isAutoLayer && !includeAAS) continue;

                    var clonedSM = UnityEngine.Object.Instantiate(layer.stateMachine);

                    if (forceWD.HasValue)
                    {
                        ApplyWriteDefaultsRecursive(clonedSM, forceWD.Value);
                    }

                    // [NEW FEATURE] Animator Path Rewriting
                    if (merger.animatorRewritePaths)
                    {
                        RewriteAnimationPaths(clonedSM, outfitEntry.outfit.name, ctx.AvatarRootTransform, clipReplacementMap, merger);
                    }

                    string layerName;
                    // [NEW FEATURE] Combine Layers By Name
                    if (merger.animatorCombineLayersByName)
                    {
                        layerName = layer.name;

                        if (!layersByOriginalName.ContainsKey(layerName))
                            layersByOriginalName[layerName] = new List<AnimatorControllerLayer>();

                        layersByOriginalName[layerName].Add(new AnimatorControllerLayer
                        {
                            name = layerName,
                            stateMachine = clonedSM,
                            avatarMask = layer.avatarMask,
                            blendingMode = layer.blendingMode,
                            defaultWeight = layer.defaultWeight,
                            syncedLayerIndex = layer.syncedLayerIndex,
                            iKPass = layer.iKPass
                        });
                    }
                    else
                    {
                        layerName = $"{outfitEntry.outfit.name}_{layer.name}";

                        var newLayer = new AnimatorControllerLayer
                        {
                            name = layerName,
                            stateMachine = clonedSM,
                            avatarMask = layer.avatarMask,
                            blendingMode = layer.blendingMode,
                            defaultWeight = layer.defaultWeight,
                            syncedLayerIndex = layer.syncedLayerIndex,
                            iKPass = layer.iKPass
                        };

                        newController.AddLayer(newLayer);
                    }
                }
            }

            // [NEW FEATURE] Apply combined layers if enabled
            if (merger.animatorCombineLayersByName)
            {
                foreach (var kvp in layersByOriginalName)
                {
                    var layers = kvp.Value;
                    if (layers.Count == 1)
                    {
                        newController.AddLayer(layers[0]);
                        layersAdded++;
                    }
                    else
                    {
                        // Merge multiple layers with same name
                        var mergedLayer = layers[0];
                        combinedLayerGroups++;

                        // [NEW FEATURE] Merge Avatar Masks
                        if (merger.animatorMergeAvatarMasks && layers.Any(l => l.avatarMask != null))
                        {
                            mergedLayer.avatarMask = MergeAvatarMasks(layers.Select(l => l.avatarMask).Where(m => m != null).ToArray());
                        }

                        newController.AddLayer(mergedLayer);
                        layersAdded++;
                        if (ShouldLogDetailed(merger))
                            mergeLog.AppendLine($"  Combined {layers.Count} layers with name '{kvp.Key}'");
                    }
                }
            }

            TrySetActualControllerOnAvatar(targetCVRAvatar, newController, ctx.AvatarRootTransform);
            ApplyAnimationClipReplacementsInClone(ctx.AvatarRootTransform, clipReplacementMap, merger);

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animator Merge] Complete: added {parametersAdded} parameter(s), added {layersAdded} layer(s), combined {combinedLayerGroups} layer group(s)");
        }

        // [NEW FEATURE] Rewrite Animation Paths
        private void RewriteAnimationPaths(AnimatorStateMachine sm, string outfitName, Transform avatarRoot, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, CVRMergeArmature merger)
        {
            if (sm == null || avatarRoot == null) return;

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animator Path Rewrite] Starting for outfit '{outfitName}'...");

            int clipsVisited = 0;
            int clipsRewritten = 0;

            foreach (var state in sm.states)
            {
                if (state.state != null)
                {
                    if (RewriteMotionPaths(state.state, outfitName, avatarRoot, clipReplacementMap, ref clipsVisited, ref clipsRewritten, merger))
                        EditorUtility.SetDirty(state.state);
                }
            }
            foreach (var childSm in sm.stateMachines)
            {
                if (childSm.stateMachine != null)
                {
                    RewriteAnimationPaths(childSm.stateMachine, outfitName, avatarRoot, clipReplacementMap, merger);
                }
            }

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animator Path Rewrite] Complete: visited {clipsVisited} clip(s), rewritten {clipsRewritten} clip(s)");
        }

        private bool RewriteClipPaths(AnimationClip clip, string outfitName, Transform avatarRoot, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, CVRMergeArmature merger, out AnimationClip rewrittenClip)
        {
            rewrittenClip = clip;
            if (clip == null || avatarRoot == null) return false;

            if (clipReplacementMap != null && clipReplacementMap.TryGetValue(clip, out var existingCopy) && existingCopy != null)
                rewrittenClip = existingCopy;
            
            var curveBindings = AnimationUtility.GetCurveBindings(rewrittenClip);
            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(rewrittenClip);
            
            if (curveBindings.Length == 0 && objectBindings.Length == 0) return false;
            
            bool anyPathsRewritten = false;
            int rewrittenBindings = 0;
            int skippedBindings = 0;
            
            // Process float curve bindings
            foreach (var binding in curveBindings)
            {
                var curve = AnimationUtility.GetEditorCurve(rewrittenClip, binding);
                if (curve == null) continue;
                
                // Check if path needs rewriting (starts with outfit name)
                if (binding.path.StartsWith(outfitName + "/") || binding.path == outfitName)
                {
                    // Strip outfit name from path
                    string newPath = binding.path == outfitName ? "" : binding.path.Substring(outfitName.Length + 1);
                    
                    // Verify the new path exists in the merged hierarchy
                    Transform target = string.IsNullOrEmpty(newPath) ? avatarRoot : avatarRoot.Find(newPath);
                    
                    if (target != null)
                    {
                        rewrittenClip = GetOrCreateModifiedClipCopy(clip, clipReplacementMap);
                        if (rewrittenClip != clip)
                        {
                            curveBindings = AnimationUtility.GetCurveBindings(rewrittenClip);
                            objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(rewrittenClip);
                        }

                        // Remove old binding
                        AnimationUtility.SetEditorCurve(rewrittenClip, binding, null);
                        
                        // Create new binding with rewritten path
                        var newBinding = binding;
                        newBinding.path = newPath;
                        AnimationUtility.SetEditorCurve(rewrittenClip, newBinding, curve);
                        
                        anyPathsRewritten = true;
                        rewrittenBindings++;
                    }
                    else
                    {
                        skippedBindings++;
                    }
                }
            }
            
            // Process object reference bindings
            foreach (var binding in objectBindings)
            {
                var keyframes = AnimationUtility.GetObjectReferenceCurve(rewrittenClip, binding);
                if (keyframes == null || keyframes.Length == 0) continue;
                
                // Check if path needs rewriting (starts with outfit name)
                if (binding.path.StartsWith(outfitName + "/") || binding.path == outfitName)
                {
                    // Strip outfit name from path
                    string newPath = binding.path == outfitName ? "" : binding.path.Substring(outfitName.Length + 1);
                    
                    // Verify the new path exists in the merged hierarchy
                    Transform target = string.IsNullOrEmpty(newPath) ? avatarRoot : avatarRoot.Find(newPath);
                    
                    if (target != null)
                    {
                        rewrittenClip = GetOrCreateModifiedClipCopy(clip, clipReplacementMap);
                        if (rewrittenClip != clip)
                        {
                            curveBindings = AnimationUtility.GetCurveBindings(rewrittenClip);
                            objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(rewrittenClip);
                        }

                        // Remove old binding
                        AnimationUtility.SetObjectReferenceCurve(rewrittenClip, binding, null);
                        
                        // Create new binding with rewritten path
                        var newBinding = binding;
                        newBinding.path = newPath;
                        AnimationUtility.SetObjectReferenceCurve(rewrittenClip, newBinding, keyframes);
                        
                        anyPathsRewritten = true;
                        rewrittenBindings++;
                    }
                    else
                    {
                        skippedBindings++;
                    }
                }
            }
            
            if (anyPathsRewritten)
            {
                EditorUtility.SetDirty(rewrittenClip);
            }

            if (ShouldLogDetailed(merger) || (ShouldLogInfo(merger) && anyPathsRewritten))
                mergeLog.AppendLine($"    [Animator Path Rewrite] {rewrittenClip.name}: rewritten {rewrittenBindings} binding(s), skipped {skippedBindings}");

            return anyPathsRewritten;
        }

        private bool RewriteMotionPaths(AnimatorState animatorState, string outfitName, Transform avatarRoot, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, ref int clipsVisited, ref int clipsRewritten, CVRMergeArmature merger)
        {
            if (animatorState == null || animatorState.motion == null) return false;

            if (!RewriteMotionReference(animatorState.motion, outfitName, avatarRoot, clipReplacementMap, ref clipsVisited, ref clipsRewritten, merger, out var rewrittenMotion))
                return false;

            animatorState.motion = rewrittenMotion;
            return true;
        }

        private bool RewriteMotionReference(Motion motion, string outfitName, Transform avatarRoot, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, ref int clipsVisited, ref int clipsRewritten, CVRMergeArmature merger, out Motion rewrittenMotion)
        {
            rewrittenMotion = motion;
            if (motion == null) return false;

            if (motion is AnimationClip clip)
            {
                clipsVisited++;
                bool clipChanged = RewriteClipPaths(clip, outfitName, avatarRoot, clipReplacementMap, merger, out var rewrittenClip);
                rewrittenMotion = rewrittenClip;
                if (clipChanged)
                    clipsRewritten++;
                return clipChanged || rewrittenClip != clip;
            }

            if (motion is BlendTree blendTree)
            {
                var children = blendTree.children;
                var rewrittenChildren = new Motion[children.Length];
                bool anyChildChanged = false;

                for (int i = 0; i < children.Length; i++)
                {
                    rewrittenChildren[i] = children[i].motion;
                    if (RewriteMotionReference(children[i].motion, outfitName, avatarRoot, clipReplacementMap, ref clipsVisited, ref clipsRewritten, merger, out var childMotion))
                    {
                        rewrittenChildren[i] = childMotion;
                        anyChildChanged = true;
                    }
                }

                if (!anyChildChanged) return false;

                var clonedTree = UnityEngine.Object.Instantiate(blendTree);
                clonedTree.name = blendTree.name + " (NDMF Modified)";
                var clonedChildren = clonedTree.children;
                for (int i = 0; i < clonedChildren.Length; i++)
                {
                    var child = clonedChildren[i];
                    child.motion = rewrittenChildren[i];
                    clonedChildren[i] = child;
                }
                clonedTree.children = clonedChildren;
                rewrittenMotion = clonedTree;
                return true;
            }

            return false;
        }

        private AnimationClip GetOrCreateModifiedClipCopy(AnimationClip clip, Dictionary<AnimationClip, AnimationClip> clipReplacementMap)
        {
            if (clip == null) return null;

            if (clipReplacementMap != null && clipReplacementMap.TryGetValue(clip, out var existingCopy) && existingCopy != null)
                return existingCopy;

            var cloned = UnityEngine.Object.Instantiate(clip);
            cloned.name = clip.name + " (NDMF Modified)";
            if (clipReplacementMap != null)
                clipReplacementMap[clip] = cloned;
            return cloned;
        }

        // [NEW FEATURE] Merge Avatar Masks
        private AvatarMask MergeAvatarMasks(AvatarMask[] masks)
        {
            if (masks == null || masks.Length == 0) return null;

            var merged = new AvatarMask();

            // Union all transform paths
            var allPaths = new HashSet<string>();
            foreach (var mask in masks)
            {
                for (int i = 0; i < mask.transformCount; i++)
                {
                    allPaths.Add(mask.GetTransformPath(i));
                }
            }

            merged.transformCount = allPaths.Count;
            int idx = 0;
            foreach (var path in allPaths)
            {
                merged.SetTransformPath(idx, path);
                merged.SetTransformActive(idx, true);
                idx++;
            }

            return merged;
        }

        private AnimatorController GetControllerFromOutfit(GameObject outfit, Type avatarType)
        {
            var outfitAvatar = outfit.GetComponentInChildren(avatarType, true);
            if (outfitAvatar != null)
            {
                var ac = GetPreferredController(outfitAvatar);
                if (ac != null) return ac;
            }

            var anim = outfit.GetComponentInChildren<Animator>(true);
            return anim != null ? anim.runtimeAnimatorController as AnimatorController : null;
        }

        private void ApplyWriteDefaultsRecursive(AnimatorStateMachine sm, bool writeDefaults)
        {
            foreach (var state in sm.states)
            {
                if (state.state != null)
                    state.state.writeDefaultValues = writeDefaults;
            }
            foreach (var childSm in sm.stateMachines)
            {
                if (childSm.stateMachine != null)
                    ApplyWriteDefaultsRecursive(childSm.stateMachine, writeDefaults);
            }
        }

        private AnimatorController GetPreferredController(Component cvrAvatar)
        {
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

        // ----------------------------
        // Parameter Stream
        // ----------------------------
        private void MergeParameterStream(Transform sourceTransform, Component targetCVRAvatar, Transform avatarRootClone, CVRMergeArmature merger)
        {
            if (targetCVRAvatar == null || avatarRootClone == null) return;
            
            // SAFETY CHECK: Only modify if target component is part of the NDMF clone hierarchy
            if (!targetCVRAvatar.transform.IsChildOf(avatarRootClone))
            {
                return; // Silently skip if not in clone
            }
            
            var paramStreamType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRParameterStream");
            if (paramStreamType == null) return;

            var sourceStreams = sourceTransform.GetComponentsInChildren(paramStreamType, true);
            if (sourceStreams.Length == 0) return;

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Parameter Stream] Merging {sourceStreams.Length} parameter stream(s)...");

            var targetStream = targetCVRAvatar.GetComponent(paramStreamType);
            if (targetStream == null) targetStream = targetCVRAvatar.gameObject.AddComponent(paramStreamType);

            int entriesAdded = 0;
            foreach (var sourceStream in sourceStreams)
            {
                var entriesField = sourceStream.GetType().GetField("entries");
                if (entriesField == null) continue;

                var sourceEntries = entriesField.GetValue(sourceStream) as IList;
                var targetEntries = entriesField.GetValue(targetStream) as IList;

                if (sourceEntries == null || targetEntries == null) continue;

                foreach (var entry in sourceEntries)
                {
                    if (entry != null)
                    {
                        targetEntries.Add(entry);
                        entriesAdded++;
                    }
                }
            }
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Parameter Stream] Complete: added {entriesAdded} entries to target stream");
        }

        // ----------------------------
        // Animator Driver merge + split
        // ----------------------------
        private class DriverEntry
        {
            public Animator animator;
            public string param;
            public int type;
            public float value;
        }

        private void MergeAnimatorDriversWithSplit(Transform avatarRoot, CVRMergeArmature merger)
        {
            var driverType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAnimatorDriver");
            if (driverType == null) return;

            var allDrivers = avatarRoot.GetComponentsInChildren(driverType, true).Cast<Component>().ToList();
            if (allDrivers.Count == 0) return;
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animator Driver Split] Processing {allDrivers.Count} animator driver(s)...");

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
                    var tVal = (int)paramTypes[i];

                    float v = GetDriverValue(d, i + 1);

                    entries.Add(new DriverEntry { animator = a, param = p, type = tVal, value = v });
                }
            }

            foreach (var d in allDrivers)
                UnityEngine.Object.DestroyImmediate(d);

            if (entries.Count == 0) return;

            var containerGO = new GameObject("NDMF Merge Animator Drivers");
            containerGO.transform.SetParent(avatarRoot, false);

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
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animator Driver Split] Complete: created {driverIndex} merged driver component(s) from {entries.Count} entries");
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

        private void MergeAnimatorDriver(Transform sourceTransform, Component targetCVRAvatar, Transform avatarRootClone, CVRMergeArmature merger)
        {
            if (targetCVRAvatar == null || avatarRootClone == null) return;
            
            // SAFETY CHECK: Only modify if target component is part of the NDMF clone hierarchy
            if (!targetCVRAvatar.transform.IsChildOf(avatarRootClone))
            {
                return; // Silently skip if not in clone
            }
            
            var animatorDriverType = FindTypeInLoadedAssemblies("ABI.CCK.Components.CVRAnimatorDriver");
            if (animatorDriverType == null) return;

            var sourceDrivers = sourceTransform.GetComponentsInChildren(animatorDriverType, true);
            if (sourceDrivers.Length == 0) return;

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animator Driver] Merging {sourceDrivers.Length} animator driver(s)...");

            var targetDriver = targetCVRAvatar.GetComponent(animatorDriverType);
            if (targetDriver == null) targetDriver = targetCVRAvatar.gameObject.AddComponent(animatorDriverType);

            int entriesAdded = 0;
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
                    entriesAdded++;
                }
            }

            EditorUtility.SetDirty(targetDriver);
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animator Driver] Complete: added {entriesAdded} parameters to target driver");
        }
        // ----------------------------
        // Advanced Avatar Settings merge
        // ----------------------------
        private void MergeAdvancedAvatarSettings(CVRMergeArmature merger, Component sourceCVRAvatar, Component targetCVRAvatar, Transform avatarRootClone)
        {
            try
            {
                if (sourceCVRAvatar == null) return;

                // SAFETY CHECK: Only modify if target component is part of the NDMF clone hierarchy
                if (avatarRootClone != null && !targetCVRAvatar.transform.IsChildOf(avatarRootClone))
                {
                    if (ShouldLogDetailed(merger))
                        mergeLog.AppendLine("  Skipped merging Advanced Avatar Settings - target is not in clone hierarchy");
                    return;
                }

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

                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine($"  [Advanced Avatar Settings] Merging {sourceList.Count} setting(s) from {outfitName}...");

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

                    if (hasConflict)
                    {
                        skippedCount++;
                        continue;
                    }

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
                                var settingCopy = CopySettingObject(sourceSetting, outfitName, avatarRoot, merger);
                                if (settingCopy != null)
                                {
                                    settingProperty.SetValue(newEntry, settingCopy);
                                    targetList.Add(newEntry);
                                    mergedCount++;
                                }
                                else
                                {
                                    skippedCount++;
                                    if (ShouldLogDetailed(merger))
                                        mergeLog.AppendLine($"    Skipped '{machineName}' - failed to copy setting object");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        skippedCount++;
                        if (ShouldLogDetailed(merger))
                            mergeLog.AppendLine($"    Failed to merge '{machineName}': {ex.Message}");
                    }
                }

                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine($"  [Advanced Avatar Settings] Complete: merged {mergedCount} setting(s) from {outfitName} (skipped {skippedCount})");

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
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;
                if (field.Name == "setting" || field.Name.EndsWith("Settings") || field.Name == "reorderableList")
                    continue;

                field.SetValue(target, field.GetValue(source));
            }
        }

        private object CopySettingObject(object sourceSetting, string outfitName, Transform avatarRoot, CVRMergeArmature merger)
        {
            if (sourceSetting == null) return null;

            var settingType = sourceSetting.GetType();
            var newSetting = Activator.CreateInstance(settingType);

            var fields = settingType.GetFields(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;
                if (field.Name == "reorderableList") continue;

                var value = field.GetValue(sourceSetting);

                if (field.Name.Contains("Target") && value is IList list)
                {
                    var newList = (IList)Activator.CreateInstance(field.FieldType);
                    foreach (var item in list)
                        newList.Add(item == null ? null : CopyTargetEntryWithGameObjectRemap(item, outfitName, avatarRoot, merger));
                    field.SetValue(newSetting, newList);
                }
                else if (field.Name == "options" && value is IList optionsList)
                {
                    var newList = (IList)Activator.CreateInstance(field.FieldType);
                    foreach (var option in optionsList)
                        newList.Add(option == null ? null : CopyDropdownOption(option, outfitName, avatarRoot, merger));
                    field.SetValue(newSetting, newList);
                }
                else
                {
                    field.SetValue(newSetting, value);
                }
            }

            return newSetting;
        }

        private object CopyDropdownOption(object sourceOption, string outfitName, Transform avatarRoot, CVRMergeArmature merger)
        {
            var optionType = sourceOption.GetType();
            var newOption = Activator.CreateInstance(optionType);

            var fields = optionType.GetFields(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (field.IsNotSerialized) continue;

                var value = field.GetValue(sourceOption);

                if (field.Name == "gameObjectTargets" && value is IList list)
                {
                    var newList = (IList)Activator.CreateInstance(field.FieldType);
                    foreach (var item in list)
                        newList.Add(item == null ? null : CopyTargetEntryWithGameObjectRemap(item, outfitName, avatarRoot, merger));
                    field.SetValue(newOption, newList);
                }
                else
                {
                    field.SetValue(newOption, value);
                }
            }

            return newOption;
        }

        private object CopyTargetEntryWithGameObjectRemap(object sourceTarget, string outfitName, Transform avatarRoot, CVRMergeArmature merger)
        {
            var targetType = sourceTarget.GetType();
            var newTarget = Activator.CreateInstance(targetType);

            var fields = targetType.GetFields(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance);

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
                    if (ShouldLogWarnings(merger))
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
        // Remaining helpers
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
            if (ShouldLogDetailed(merger))
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

            if (ShouldLogDetailed(merger))
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
                if (ShouldLogDetailed(merger))
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

        // ============================================================
        // [NEW FEATURES] CONFLICT RESOLUTION with Fuzzy Matching & Mappings
        // ============================================================
        private Dictionary<Transform, Transform> BuildBoneMappingWithConflicts(
            CVRMergeArmature merger,
            OutfitToMerge outfitEntry,
            Transform sourceRoot,
            Transform target,
            HashSet<Transform> usedBones,
            List<(Transform, Transform)> bonesToReparent,
            Dictionary<Transform, Matrix4x4> bindposeCompensationBySourceBone,
            string prefix,
            string suffix)
        {
            var mapping = new Dictionary<Transform, Transform>();

            var conflictLookup = new Dictionary<Transform, BoneConflictEntry>();
            if (merger.boneConflicts != null)
            {
                foreach (var c in merger.boneConflicts)
                {
                    if (c != null && c.sourceBone != null)
                        conflictLookup[c.sourceBone] = c;
                }
            }

            // [NEW FEATURE] Build combined bone name mappings (global + semantic + per-outfit, later entries override earlier)
            var boneNameMappings = new Dictionary<string, string>();
            if (merger.enableFuzzyBoneMatching && merger.globalBoneNameMappings != null)
            {
                foreach (var map in merger.globalBoneNameMappings)
                {
                    if (!string.IsNullOrEmpty(map.from) && !string.IsNullOrEmpty(map.to))
                        boneNameMappings[map.from] = map.to;
                }
            }
            // Add semantic synonyms when enabled
            if (merger.semanticBoneMatchingSettings != null && merger.semanticBoneMatchingSettings.enable && merger.semanticBoneMatchingSettings.synonyms != null)
            {
                foreach (var syn in merger.semanticBoneMatchingSettings.synonyms)
                {
                    if (!string.IsNullOrEmpty(syn.from) && !string.IsNullOrEmpty(syn.to))
                        boneNameMappings[syn.from] = syn.to;
                }
            }
            // [NEW FEATURE] Per-outfit mappings override global
            if (outfitEntry.boneNameMappings != null)
            {
                foreach (var map in outfitEntry.boneNameMappings)
                {
                    if (!string.IsNullOrEmpty(map.from) && !string.IsNullOrEmpty(map.to))
                        boneNameMappings[map.from] = map.to;
                }
            }

            void MapBone(Transform sourceBone)
            {
                if (merger.IsExcluded(sourceBone)) return;

                if (!usedBones.Contains(sourceBone))
                {
                    foreach (Transform child in sourceBone) MapBone(child);
                    return;
                }

                bool mapped = false;

                // 1. Check for Explicit Conflicts
                if (conflictLookup.TryGetValue(sourceBone, out var conflict))
                {
                    switch (conflict.resolution)
                    {
                        case BoneConflictResolution.StillMerge:
                            if (conflict.targetBone != null)
                            {
                                mapping[sourceBone] = conflict.targetBone;
                                bindposeCompensationBySourceBone[sourceBone] = ComputeBindposeCompensationDelta(sourceBone, conflict.targetBone);
                                mapped = true;
                            }
                            break;

                        case BoneConflictResolution.MergeIntoSelected:
                            if (conflict.customTargetBone != null)
                            {
                                mapping[sourceBone] = conflict.customTargetBone;
                                bindposeCompensationBySourceBone[sourceBone] = ComputeBindposeCompensationDelta(sourceBone, conflict.customTargetBone);
                                mapped = true;
                            }
                            break;

                        case BoneConflictResolution.ConstraintToTarget:
                            if (conflict.targetBone != null)
                            {
                                bonesToReparent.Add((sourceBone, conflict.targetBone));
                                mapped = true;
                            }
                            break;

                        case BoneConflictResolution.Rename:
                            sourceBone.name = sourceBone.name + "_Renamed";
                            mapped = true;
                            break;

                        case BoneConflictResolution.DontMerge:
                            mapped = true;
                            break;
                    }
                }

                // 2. Standard Name Matching with NEW FEATURES
                if (!mapped)
                {
                    string boneName = sourceBone.name;

                    if (!string.IsNullOrEmpty(outfitEntry.uniqueBonePrefix) && boneName.StartsWith(outfitEntry.uniqueBonePrefix))
                    {
                        foreach (Transform child in sourceBone) MapBone(child);
                        return;
                    }

                    // Apply stripping rules
                    if (!string.IsNullOrEmpty(prefix) && boneName.StartsWith(prefix))
                        boneName = boneName.Substring(prefix.Length);
                    if (!string.IsNullOrEmpty(suffix) && boneName.EndsWith(suffix))
                        boneName = boneName.Substring(0, boneName.Length - suffix.Length);

                    // [NEW FEATURE] Apply bone name mappings (includes global + semantic + per-outfit)
                    if (boneNameMappings.ContainsKey(boneName))
                    {
                        boneName = boneNameMappings[boneName];
                        if (ShouldLogDetailed(merger))
                            mergeLog.AppendLine($"    Applied bone name mapping: {sourceBone.name} -> {boneName}");
                    }

                    // Try exact match
                    Transform targetBone = FindBoneByName(target, boneName);

                    // [NEW FEATURE] Fuzzy matching with Levenshtein
                    if (targetBone == null && merger.enableFuzzyBoneMatching && merger.enableLevenshteinBoneMatching)
                    {
                        targetBone = FindBoneByLevenshtein(target, boneName, merger.maxLevenshteinDistance);
                        if (targetBone != null)
                        {
                            if (ShouldLogDetailed(merger))
                                mergeLog.AppendLine($"    Fuzzy match: {sourceBone.name} -> {targetBone.name}");
                        }
                    }

                    // [NEW FEATURE] Semantic pattern fallback (after mappings/fuzzy)
                    var sem = merger.semanticBoneMatchingSettings;
                    if (targetBone == null && sem != null && sem.enable)
                    {
                        // If source bone matches any user-specified pattern, find a target bone by the same patterns
                        if (sem.patterns != null && sem.patterns.Count > 0 && MatchesAnyPattern(boneName, sem.patterns, sem.caseInsensitive))
                        {
                            targetBone = FindBoneByPatterns(target, sem.patterns, sem.caseInsensitive);
                            if (targetBone != null)
                                if (ShouldLogDetailed(merger))
                                    mergeLog.AppendLine($"    Pattern match: {sourceBone.name} -> {targetBone.name}");
                        }

                        // Left/Right variations support
                        if (targetBone == null && sem.enableLRVariations)
                        {
                            bool isLeft = sem.leftPatterns != null && sem.leftPatterns.Count > 0 && MatchesAnyPattern(boneName, sem.leftPatterns, sem.caseInsensitive);
                            bool isRight = sem.rightPatterns != null && sem.rightPatterns.Count > 0 && MatchesAnyPattern(boneName, sem.rightPatterns, sem.caseInsensitive);

                            if (isLeft)
                            {
                                // Prefer target bones that also match left tokens
                                targetBone = FindBoneByPatterns(target, sem.leftPatterns, sem.caseInsensitive) ?? targetBone;
                            }
                            else if (isRight)
                            {
                                targetBone = FindBoneByPatterns(target, sem.rightPatterns, sem.caseInsensitive) ?? targetBone;
                            }
                        }
                    }

                    if (targetBone != null)
                    {
                        mapping[sourceBone] = targetBone;
                    }
                }

                foreach (Transform child in sourceBone) MapBone(child);
            }

            MapBone(sourceRoot);
            return mapping;
        }

        // Converts source-bone weights to target-bone space by baking the source->target delta into bindposes.
        private static Matrix4x4 ComputeBindposeCompensationDelta(Transform sourceBone, Transform targetBone)
        {
            if (sourceBone == null || targetBone == null) return Matrix4x4.identity;
            return targetBone.worldToLocalMatrix * sourceBone.localToWorldMatrix;
        }

        // Simple wildcard matcher (* and ?) with optional case-insensitive
        private bool MatchesPatternName(string name, string pattern, bool caseInsensitive)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pattern)) return false;
            var n = caseInsensitive ? name.ToLowerInvariant() : name;
            var p = caseInsensitive ? pattern.ToLowerInvariant() : pattern;

            // Convert wildcard to regex
            string regex = "^" + System.Text.RegularExpressions.Regex.Escape(p)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(n, regex);
            }
            catch { return false; }
        }

        private bool MatchesAnyPattern(string name, List<string> patterns, bool caseInsensitive)
        {
            if (patterns == null) return false;
            foreach (var pat in patterns)
            {
                if (MatchesPatternName(name, pat, caseInsensitive)) return true;
            }
            return false;
        }

        private Transform FindBoneByPatterns(Transform root, List<string> patterns, bool caseInsensitive)
        {
            if (root == null) return null;

            Transform found = null;
            void Search(Transform t)
            {
                if (found != null) return;
                if (MatchesAnyPattern(t.name, patterns, caseInsensitive))
                {
                    found = t;
                    return;
                }

                foreach (Transform child in t)
                    Search(child);
            }

            Search(root);
            return found;
        }

        // [NEW FEATURE] Levenshtein Distance Algorithm
        private int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 0 : s2.Length;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int n = s1.Length;
            int m = s2.Length;
            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }

        // [NEW FEATURE] Find bone by Levenshtein distance
        private Transform FindBoneByLevenshtein(Transform root, string targetName, int maxDistance)
        {
            Transform bestMatch = null;
            int bestDistance = maxDistance + 1;

            void Search(Transform bone)
            {
                int distance = LevenshteinDistance(bone.name, targetName);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestMatch = bone;
                }

                foreach (Transform child in bone)
                    Search(child);
            }

            Search(root);

            return bestDistance <= maxDistance ? bestMatch : null;
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

        private void RemapSkinnedMeshRenderer(
            SkinnedMeshRenderer smr,
            Dictionary<Transform, Transform> boneMap,
            Dictionary<Transform, Matrix4x4> bindposeCompensationBySourceBone = null,
            ArmatureMergingSettings settings = null,
            CVRMergeArmature merger = null)
        {
            var bones = smr.bones;
            var newBones = new Transform[bones.Length];
            int remappedCount = 0;
            int compensatedBindposeCount = 0;
            int autoCompensatedBindposeCount = 0;
            bool changed = false;

            bool compensateBindposes = settings == null || settings.compensateBindposesOnBoneRemap;
            bool compensateAutoMappedBones = settings == null || settings.compensateAutoMappedBones;

            Mesh mesh = null;
            Matrix4x4[] bindposes = null;
            bool canAdjustBindposes = false;
            bool bindposesChanged = false;

            if (compensateBindposes && smr.sharedMesh != null)
            {
                if (smr.sharedMesh.bindposes != null && smr.sharedMesh.bindposes.Length == bones.Length)
                {
                    mesh = EnsureSceneMeshInstance(smr, "NDMF Bone Remap");
                    if (mesh != null)
                    {
                        bindposes = mesh.bindposes;
                        if (bindposes != null && bindposes.Length == bones.Length)
                            canAdjustBindposes = true;
                    }
                }
                else if (merger != null && ShouldLogDetailed(merger))
                {
                    mergeLog.AppendLine($"    [Bone Remap] Skipping bindpose compensation on mesh '{smr.name}' due to bindpose/bone count mismatch.");
                }
            }

            for (int i = 0; i < bones.Length; i++)
            {
                var sourceBone = bones[i];
                if (sourceBone != null && boneMap.TryGetValue(sourceBone, out var mappedBone))
                {
                    newBones[i] = mappedBone;
                    changed = true;
                    remappedCount++;

                    if (canAdjustBindposes)
                    {
                        Matrix4x4 delta = Matrix4x4.identity;
                        bool hasStoredDelta = bindposeCompensationBySourceBone != null && bindposeCompensationBySourceBone.TryGetValue(sourceBone, out delta);
                        if (!hasStoredDelta)
                        {
                            if (compensateAutoMappedBones)
                            {
                                // Fallback for name/fuzzy mappings that are not represented in explicit conflict entries.
                                delta = ComputeBindposeCompensationDelta(sourceBone, mappedBone);
                                autoCompensatedBindposeCount++;
                            }
                            else
                            {
                                continue;
                            }
                        }

                        // New skinning uses targetBone * bindposeNew. Bake source->target delta so vertices stay in place.
                        bindposes[i] = delta * bindposes[i];
                        bindposesChanged = true;
                        compensatedBindposeCount++;
                    }
                }
                else newBones[i] = sourceBone;
            }

            if (changed) smr.bones = newBones;
            if (bindposesChanged && mesh != null)
                mesh.bindposes = bindposes;

            if (merger != null && ShouldLogDetailed(merger) && remappedCount > 0)
                mergeLog.AppendLine($"    [Bone Remap] Remapped {remappedCount} bone(s) on mesh '{smr.name}'");
            if (merger != null && ShouldLogDetailed(merger) && compensatedBindposeCount > 0)
                mergeLog.AppendLine($"    [Bone Remap] Applied bindpose compensation on {compensatedBindposeCount} bone slot(s) for mesh '{smr.name}'");
            if (merger != null && ShouldLogDetailed(merger) && autoCompensatedBindposeCount > 0)
                mergeLog.AppendLine($"    [Bone Remap] {autoCompensatedBindposeCount} slot(s) used automatic source->target compensation (non-conflict mapping).");
                
            if (smr.rootBone != null && boneMap.TryGetValue(smr.rootBone, out var newRootBone))
                smr.rootBone = newRootBone;
        }

        private void MergeDynamicComponents(Transform sourceTransform, Dictionary<Transform, Transform> boneMap, CVRMergeArmature merger)
        {
            int dynamicBonesFound = 0;
            int magicaClothsFound = 0;

            // [FIX] Respect flags
            if (merger.mergeDynamicBones)
            {
                var dynamicBoneType = FindTypeInLoadedAssemblies("DynamicBone");
                if (dynamicBoneType != null)
                {
                    var dynamicBones = sourceTransform.GetComponentsInChildren(dynamicBoneType, true);
                    dynamicBonesFound = dynamicBones.Length;
                    if (ShouldLogInfo(merger) && dynamicBones.Length > 0)
                        mergeLog.AppendLine($"    [Dynamic Bones] Remapping {dynamicBones.Length} Dynamic Bone component(s)...");
                    else if (ShouldLogInfo(merger) && dynamicBones.Length == 0)
                        mergeLog.AppendLine("    [Dynamic Bones] None found");
                    foreach (var db in dynamicBones)
                    {
                        RemapComponentTransformField(db, "m_Root", boneMap, merger);
                        RemapComponentTransformList(db, "m_Exclusions", boneMap, merger);
                    }
                }
            }

            // [FIX] Respect flags
            if (merger.mergeMagicaCloth)
            {
                var magicaClothType = FindTypeInLoadedAssemblies("MagicaCloth.MagicaCloth");
                if (magicaClothType != null)
                {
                    var magicaCloths = sourceTransform.GetComponentsInChildren(magicaClothType, true);
                    magicaClothsFound = magicaCloths.Length;
                    if (ShouldLogInfo(merger) && magicaCloths.Length > 0)
                        mergeLog.AppendLine($"    [MagicaCloth] Remapping {magicaCloths.Length} MagicaCloth component(s)...");
                    else if (ShouldLogInfo(merger) && magicaCloths.Length == 0)
                        mergeLog.AppendLine("    [MagicaCloth] None found");
                    foreach (var mc in magicaCloths)
                        RemapComponentTransformList(mc, "clothTarget", boneMap, merger);
                }
            }

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Dynamic Component Remap] Complete: DynamicBone={dynamicBonesFound}, MagicaCloth={magicaClothsFound}");
        }

        private void RemapComponentTransformField(Component component, string fieldName, Dictionary<Transform, Transform> boneMap, CVRMergeArmature merger)
        {
            var type = component.GetType();
            var field = type.GetField(fieldName,
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance);

            if (field != null && field.FieldType == typeof(Transform))
            {
                var currentTransform = field.GetValue(component) as Transform;
                if (currentTransform != null && boneMap.TryGetValue(currentTransform, out var mapped))
                {
                    field.SetValue(component, mapped);
                    if (ShouldLogDetailed(merger))
                        mergeLog.AppendLine($"      Remapped {component.GetType().Name}.{fieldName}: {currentTransform.name} -> {mapped.name}");
                }
            }
        }

        private void RemapComponentTransformList(Component component, string fieldName, Dictionary<Transform, Transform> boneMap, CVRMergeArmature merger)
        {
            var type = component.GetType();
            var field = type.GetField(fieldName,
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance);

            if (field != null && typeof(IList).IsAssignableFrom(field.FieldType))
            {
                var list = field.GetValue(component) as IList;
                int remappedCount = 0;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i] is Transform t && boneMap.TryGetValue(t, out var mapped))
                        {
                            list[i] = mapped;
                            remappedCount++;
                        }
                    }
                }
                if (ShouldLogDetailed(merger) && remappedCount > 0)
                    mergeLog.AppendLine($"      Remapped {remappedCount} transform(s) in {component.GetType().Name}.{fieldName}");
            }
        }

        private void MergeAdvancedPointerTrigger(Transform sourceTransform)
        {
            if (sourceTransform == null) return;
            mergeLog.AppendLine("  [Advanced Pointer Trigger] Skipped: merge not implemented yet");
        }

        private Type FindTypeInLoadedAssemblies(string typeName)
        {
            return FindTypeInLoadedAssembliesStatic(typeName);
        }

        // ======================
        // Feature stub methods
        // ======================
        private void ValidateAndFixUVs(Transform outfitRoot, UVValidationSettings settings, CVRMergeArmature merger)
        {
            if (!settings.fillMissingUVs && !settings.autoFixOverlapping && !settings.autoFixInverted)
                return;
            
            bool verbose = ShouldLogDetailed(merger);
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine("  [UV Validation] Starting UV validation and fixing...");
            var skinnedMeshes = outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int meshesProcessed = 0, uvsGenerated = 0, overlapsFixed = 0, invertsFixed = 0;
            
            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMesh == null) continue;
                var mesh = EnsureSceneMeshInstance(smr, "NDMF Generated UV");
                bool meshModified = false;
                
                // Generate missing UVs on UV0 if needed
                if (settings.fillMissingUVs)
                {
                    var uv0 = new List<Vector2>();
                    mesh.GetUVs(0, uv0);
                    
                    if (uv0.Count == 0 || uv0.Count != mesh.vertexCount)
                    {
                        if (verbose)
                            mergeLog.AppendLine($"    Generating missing UVs for {smr.name}");
                        mesh.SetUVs(0, Enumerable.Repeat(Vector2.zero, mesh.vertexCount).ToArray());
                        uvsGenerated++;
                        meshModified = true;
                    }
                }
                
                // Fix overlapping UVs (simple unwrap heuristic)
                if (settings.autoFixOverlapping)
                {
                    var uv0 = new List<Vector2>();
                    mesh.GetUVs(0, uv0);
                    
                    if (HasOverlappingUVs(uv0, mesh.triangles))
                    {
                        if (verbose)
                            mergeLog.AppendLine($"    Fixing overlapping UVs for {smr.name}");
                        Unwrapping.GenerateSecondaryUVSet(mesh);
                        overlapsFixed++;
                        meshModified = true;
                    }
                }
                
                // Fix inverted UVs (detect clockwise winding)
                if (settings.autoFixInverted)
                {
                    var uv0 = new List<Vector2>();
                    mesh.GetUVs(0, uv0);
                    var triangles = mesh.triangles;
                    
                    if (HasInvertedUVs(uv0, triangles))
                    {
                        if (verbose)
                            mergeLog.AppendLine($"    Flipping inverted UVs for {smr.name}");
                        for (int i = 0; i < uv0.Count; i++)
                            uv0[i] = new Vector2(uv0[i].x, 1f - uv0[i].y);
                        mesh.SetUVs(0, uv0);
                        invertsFixed++;
                        meshModified = true;
                    }
                }
                
                if (meshModified)
                {
                    meshesProcessed++;
                    EditorUtility.SetDirty(mesh);
                }
            }
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [UV Validation] Complete: {meshesProcessed} meshes processed, {uvsGenerated} UVs generated, {overlapsFixed} overlaps fixed, {invertsFixed} inverts fixed");
        }

        private bool HasOverlappingUVs(List<Vector2> uvs, int[] triangles)
        {
            if (uvs.Count < 3 || triangles.Length < 3) return false;
            
            // Sample first 10 triangles for overlaps
            int checkCount = Mathf.Min(10, triangles.Length / 3);
            for (int i = 0; i < checkCount; i++)
            {
                int idx = i * 3;
                var uv0 = uvs[triangles[idx]];
                var uv1 = uvs[triangles[idx + 1]];
                var uv2 = uvs[triangles[idx + 2]];
                
                // Check if triangle area in UV space is near-zero (overlapping)
                float area = Mathf.Abs((uv1.x - uv0.x) * (uv2.y - uv0.y) - (uv2.x - uv0.x) * (uv1.y - uv0.y));
                if (area < 0.0001f) return true;
            }
            return false;
        }

        private bool HasInvertedUVs(List<Vector2> uvs, int[] triangles)
        {
            if (uvs.Count < 3 || triangles.Length < 3) return false;
            
            // Check winding order of first triangle
            var uv0 = uvs[triangles[0]];
            var uv1 = uvs[triangles[1]];
            var uv2 = uvs[triangles[2]];
            
            float cross = (uv1.x - uv0.x) * (uv2.y - uv0.y) - (uv2.x - uv0.x) * (uv1.y - uv0.y);
            return cross < 0; // Clockwise = inverted
        }

        private void ConsolidateMaterials(Transform outfitRoot, MaterialConsolidationSettings settings, CVRMergeArmature merger)
        {
            if (!settings.consolidateMaterials && !settings.mergeDuplicateMaterials)
                return;
            
            bool verbose = ShouldLogDetailed(merger);
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine("  [Material Consolidation] Starting material optimization...");
            var skinnedMeshes = outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            
            // Build material database
            var materialMap = new Dictionary<Material, Material>();
            var materialsByShader = new Dictionary<Shader, List<Material>>();
            int meshesUpdated = 0;
            
            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMaterials == null) continue;
                
                foreach (var mat in smr.sharedMaterials)
                {
                    if (mat == null || materialMap.ContainsKey(mat)) continue;
                    
                    if (!materialsByShader.ContainsKey(mat.shader))
                        materialsByShader[mat.shader] = new List<Material>();
                    materialsByShader[mat.shader].Add(mat);
                }
            }
            
            // Find duplicate materials
            int consolidatedCount = 0;
            foreach (var shaderGroup in materialsByShader.Values)
            {
                for (int i = 0; i < shaderGroup.Count; i++)
                {
                    for (int j = i + 1; j < shaderGroup.Count; j++)
                    {
                        var matA = shaderGroup[i];
                        var matB = shaderGroup[j];
                        
                        if (AreMaterialsSimilar(matA, matB, settings))
                        {
                            if (!materialMap.ContainsKey(matB))
                            {
                                materialMap[matB] = matA;
                                consolidatedCount++;
                                if (verbose)
                                    mergeLog.AppendLine($"    Consolidating '{matB.name}' -> '{matA.name}'");
                            }
                        }
                    }
                }
            }
            
            // Apply material remapping
            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMaterials == null) continue;
                
                var mats = smr.sharedMaterials;
                bool changed = false;
                
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && materialMap.TryGetValue(mats[i], out var replacement))
                    {
                        mats[i] = replacement;
                        changed = true;
                    }
                }
                
                if (changed)
                {
                    smr.sharedMaterials = mats;
                    EditorUtility.SetDirty(smr);
                    meshesUpdated++;
                    if (verbose)
                        mergeLog.AppendLine($"    Applied material remapping to mesh: {smr.name}");
                }
            }
            
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Material Consolidation] Complete: {consolidatedCount} materials consolidated across {meshesUpdated} mesh(es)");
        }

        private bool AreMaterialsSimilar(Material a, Material b, MaterialConsolidationSettings settings)
        {
            if (a.shader != b.shader) return false;
            
            // Match by name similarity
            if (settings.matchByName)
            {
                float similarity = CalculateNameSimilarity(a.name, b.name);
                if (similarity >= settings.nameSimilarityThreshold)
                    return true;
            }
            
            // Match by main texture
            if (settings.consolidateByShaderAndTexture)
            {
                var texA = a.GetTexture("_MainTex");
                var texB = b.GetTexture("_MainTex");
                if (texA == texB && texA != null)
                    return true;
            }
            
            return false;
        }

        private float CalculateNameSimilarity(string a, string b)
        {
            if (a == b) return 1f;
            
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            
            int maxLen = Mathf.Max(a.Length, b.Length);
            if (maxLen == 0) return 1f;
            
            int distance = LevenshteinDistance(a, b);
            return 1f - ((float)distance / maxLen);
        }

        private void TransferBlendShapes(Transform avatarRoot, Transform outfitRoot, Transform targetArmature, BlendShapeTransferTask task, BlendShapeTransferSettings settings, CVRMergeArmature merger)
        {
            if (settings == null || task == null || avatarRoot == null) return;
            
            // Weight Transfer (Copy current blend shape values)
            if (!task.enableWeightTransfer) return;

            bool verbose = ShouldLogDetailed(merger);
            if (verbose)
                mergeLog.AppendLine("  [Blend Shape Transfer] Starting blend shape weight transfer...");

            int appliedWeights = 0;

                if (task.direction == BlendShapeTransferDirection.MeshToMesh)
                {
                    var srcSMR = ResolveRendererInCloneHierarchy(task.weightTransferSourceMesh, avatarRoot);
                    var dstSMR = ResolveRendererInCloneHierarchy(task.weightTransferTargetMesh, avatarRoot);

                    if (srcSMR == null || dstSMR == null)
                    {
                        if (ShouldLogWarnings(merger))
                            mergeLog.AppendLine("  [Blend Shape Transfer] Mesh -> Mesh skipped: source or target mesh was not resolved in clone hierarchy.");
                        return;
                    }

                    var srcMesh = srcSMR.sharedMesh;
                    var dstMesh = dstSMR.sharedMesh;
                    if (srcMesh == null || dstMesh == null || srcMesh.blendShapeCount == 0 || dstMesh.blendShapeCount == 0) return;

                    appliedWeights += TransferBlendShapeWeightsBetweenMeshes(srcSMR, srcMesh, dstSMR, dstMesh, task);

                    if (verbose)
                        mergeLog.AppendLine($"  [Blend Shape Transfer] Mesh -> Mesh complete: applied {appliedWeights} weight(s)");
                    else if (appliedWeights > 0)
                        mergeLog.AppendLine($"  Blend shape weight transfer (Mesh -> Mesh): applied {appliedWeights} weight(s)");
                    return;
                }

                if (outfitRoot == null || targetArmature == null) return;
                
                // Determine transfer direction
                bool outfitToBase = task.direction == BlendShapeTransferDirection.OutfitToBase || 
                                    task.direction == BlendShapeTransferDirection.Bidirectional;
                bool baseToOutfit = task.direction == BlendShapeTransferDirection.BaseToOutfit || 
                                    task.direction == BlendShapeTransferDirection.Bidirectional;

                // Outfit -> Base transfer
                if (outfitToBase)
                {
                    var targetRenderers = targetArmature.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    foreach (var srcSMR in outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var srcMesh = srcSMR.sharedMesh;
                        if (srcMesh == null || srcMesh.blendShapeCount == 0) continue;

                        var dstSMR = FindMatchingTargetRendererFor(srcSMR, targetRenderers);
                        if (dstSMR == null) continue;

                        var dstMesh = dstSMR.sharedMesh;
                        if (dstMesh == null || dstMesh.blendShapeCount == 0) continue;

                        appliedWeights += TransferBlendShapeWeightsBetweenMeshes(srcSMR, srcMesh, dstSMR, dstMesh, task);
                    }
                }

                // Base -> Outfit transfer
                if (baseToOutfit)
                {
                    var outfitRenderers = outfitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    foreach (var srcSMR in targetArmature.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var srcMesh = srcSMR.sharedMesh;
                        if (srcMesh == null || srcMesh.blendShapeCount == 0) continue;

                        var dstSMR = FindMatchingTargetRendererFor(srcSMR, outfitRenderers);
                        if (dstSMR == null) continue;

                        var dstMesh = dstSMR.sharedMesh;
                        if (dstMesh == null || dstMesh.blendShapeCount == 0) continue;

                        appliedWeights += TransferBlendShapeWeightsBetweenMeshes(srcSMR, srcMesh, dstSMR, dstMesh, task);
                    }
                }

                if (verbose)
                    mergeLog.AppendLine($"  [Blend Shape Transfer] Complete: applied {appliedWeights} weight(s) ({task.direction})");
                else if (appliedWeights > 0)
                    mergeLog.AppendLine($"  Blend shape weight transfer ({task.direction}): applied {appliedWeights} weight(s)");
        }

        // Smart blend shape weight transfer with topology awareness
        private int TransferBlendShapeWeightsBetweenMeshes(SkinnedMeshRenderer srcSMR, Mesh srcMesh, SkinnedMeshRenderer dstSMR, Mesh dstMesh, BlendShapeTransferTask task)
        {
            int appliedCount = 0;

            // For each destination blend shape, try to pull weight from source by name
            for (int di = 0; di < dstMesh.blendShapeCount; di++)
            {
                string dstName = dstMesh.GetBlendShapeName(di);
                int si = task.matchByName
                    ? FindBlendShapeIndexByName(srcMesh, dstName)
                    : (di < srcMesh.blendShapeCount ? di : -1);
                if (si < 0) continue;

                float weight = srcSMR.GetBlendShapeWeight(si);
                
                // Apply smart weight transfer if enabled
                if (task.useSmartWeightTransfer)
                {
                    // Check if meshes have similar topology
                    float topologySimilarity = CalculateTopologySimilarity(srcMesh, dstMesh);
                    
                    // If topology is very different, scale the weight to be more conservative
                    if (topologySimilarity < 0.8f)
                    {
                        weight *= topologySimilarity;
                    }
                    
                    // Check vertex count similarity for additional safety
                    float vertexRatio = (float)dstMesh.vertexCount / Mathf.Max(1, srcMesh.vertexCount);
                    if (vertexRatio < 0.5f || vertexRatio > 2.0f)
                    {
                        // Very different vertex counts, be more conservative
                        weight *= 0.7f;
                    }
                }
                
                if (weight >= task.minWeightThreshold)
                {
                    dstSMR.SetBlendShapeWeight(di, weight);
                    appliedCount++;
                }
            }

            return appliedCount;
        }

        // Calculate topology similarity between two meshes
        private float CalculateTopologySimilarity(Mesh mesh1, Mesh mesh2)
        {
            if (mesh1 == null || mesh2 == null) return 0f;

            float vertexRatio = Mathf.Min((float)mesh1.vertexCount / Mathf.Max(1, mesh2.vertexCount),
                                          (float)mesh2.vertexCount / Mathf.Max(1, mesh1.vertexCount));
            
            float triangleRatio = Mathf.Min((float)mesh1.triangles.Length / Mathf.Max(1, mesh2.triangles.Length),
                                            (float)mesh2.triangles.Length / Mathf.Max(1, mesh1.triangles.Length));
            
            // Calculate bounds similarity
            float boundsSimilarity = 1.0f;
            if (mesh1.bounds.size.magnitude > 0.001f && mesh2.bounds.size.magnitude > 0.001f)
            {
                float sizeRatio = Mathf.Min(mesh1.bounds.size.magnitude / mesh2.bounds.size.magnitude,
                                           mesh2.bounds.size.magnitude / mesh1.bounds.size.magnitude);
                boundsSimilarity = sizeRatio;
            }

            // Weighted average: vertices are most important, then triangles, then bounds
            return (vertexRatio * 0.5f) + (triangleRatio * 0.3f) + (boundsSimilarity * 0.2f);
        }

        // [NEW FEATURE] Generate blend shapes from a task (new multi-task system)
        private void GenerateBlendShapesFromTask(Transform avatarRoot, Transform targetArmature, BlendShapeTransferTask task, Dictionary<OutfitToMerge, Transform> clonedOutfitByEntry, Component targetCVRAvatar, CVRMergeArmature merger, BlendShapeTransferSettings settings, Dictionary<AnimationClip, AnimationClip> clipReplacementMap)
        {
            if (task == null || task.sourceGenerationMesh == null) return;

            bool verbose = ShouldLogDetailed(merger);
            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Blend Shape Generation] Starting generation task from source: {task.sourceGenerationMesh.name}");

            var sourceRenderer = ResolveRendererInCloneHierarchy(task.sourceGenerationMesh, avatarRoot);
            if (sourceRenderer == null)
            {
                mergeLog.AppendLine("  Blend shape generation: source mesh was not resolved in clone hierarchy");
                return;
            }

            var sourceMesh = sourceRenderer.sharedMesh;
            if (sourceMesh == null || sourceMesh.blendShapeCount == 0)
            {
                mergeLog.AppendLine($"  Blend shape generation: source mesh has no blend shapes");
                return;
            }

            // Parse blend shape names to generate
            HashSet<string> targetBlendShapeNames = new HashSet<string>();
            if (!string.IsNullOrWhiteSpace(task.blendShapeNamesToGenerate))
            {
                var names = task.blendShapeNamesToGenerate.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var name in names)
                    targetBlendShapeNames.Add(name.Trim());
            }
            else
            {
                // Generate all blend shapes from source
                for (int i = 0; i < sourceMesh.blendShapeCount; i++)
                    targetBlendShapeNames.Add(sourceMesh.GetBlendShapeName(i));
            }

            int totalGenerated = 0;

            if (task.direction == BlendShapeTransferDirection.MeshToMesh)
            {
                var targetRenderer = ResolveRendererInCloneHierarchy(task.targetGenerationMesh, avatarRoot);
                if (targetRenderer == null || targetRenderer.sharedMesh == null)
                {
                    mergeLog.AppendLine("  Blend shape generation: Mesh -> Mesh target was not resolved in clone hierarchy");
                    if (ShouldLogInfo(merger))
                        mergeLog.AppendLine($"  [Blend Shape Generation] Complete: 0 total frame(s) created from {targetBlendShapeNames.Count} shapes");
                    return;
                }

                int generated = GenerateBlendShapesOnMesh(sourceRenderer, sourceMesh, targetRenderer, targetBlendShapeNames, task.maxMappingDistance, task.useSmartFrameGeneration, task.overrideExisting, task.transferMode);
                if (generated > 0)
                {
                    totalGenerated += generated;
                    mergeLog.AppendLine($"    Generated {generated} blend shape(s) on mesh target: {targetRenderer.name}");
                    if (task.copyGeneratedBlendshapeAnimations)
                        CopyGeneratedBlendshapeCurvesToTarget(targetCVRAvatar, avatarRoot, sourceRenderer, targetRenderer, targetBlendShapeNames, merger, settings, clipReplacementMap);
                }

                if (ShouldLogInfo(merger))
                    mergeLog.AppendLine($"  [Blend Shape Generation] Complete: {totalGenerated} total frame(s) created from {targetBlendShapeNames.Count} shapes");
                else if (totalGenerated > 0)
                    mergeLog.AppendLine($"  Blend shape generation complete: {totalGenerated} total frame(s) created");
                return;
            }

            // Build selected target names ("Base Avatar" + outfit names). Empty list => all outfits.
            var selectedNames = new HashSet<string>(task.targetOutfitNames ?? new List<string>());
            bool applyToAllTargets = task.applyToAllTargets;

            // Generate on base body if selected
            if ((applyToAllTargets || selectedNames.Contains("Base Avatar")) && avatarRoot != null)
            {
                // FIX: Use avatarRoot instead of targetArmature to only affect the NDMF clone
                var baseRenderers = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var targetSMR in baseRenderers)
                {
                    if (targetSMR.sharedMesh == null) continue;
                    
                    // Skip if this renderer belongs to a cloned outfit (not base)
                    bool isOutfitRenderer = false;
                    if (clonedOutfitByEntry != null)
                    {
                        foreach (var outfitClone in clonedOutfitByEntry.Values)
                        {
                            if (outfitClone != null && targetSMR.transform.IsChildOf(outfitClone))
                            {
                                isOutfitRenderer = true;
                                break;
                            }
                        }
                    }
                    
                    if (isOutfitRenderer) continue; // Skip outfit renderers when generating on base
                    
                    int generated = GenerateBlendShapesOnMesh(sourceRenderer, sourceMesh, targetSMR, targetBlendShapeNames, task.maxMappingDistance, task.useSmartFrameGeneration, task.overrideExisting, task.transferMode);
                    if (generated > 0)
                    {
                        totalGenerated += generated;
                        mergeLog.AppendLine($"    Generated {generated} blend shape(s) on base mesh: {targetSMR.name}");
                        if (task.copyGeneratedBlendshapeAnimations)
                            CopyGeneratedBlendshapeCurvesToTarget(targetCVRAvatar, avatarRoot, sourceRenderer, targetSMR, targetBlendShapeNames, merger, settings, clipReplacementMap);
                    }
                }
            }

            // Generate on outfit meshes by name
            if (clonedOutfitByEntry != null)
            {
                foreach (var kvp in clonedOutfitByEntry)
                {
                    var outfitEntry = kvp.Key;
                    var outfitClone = kvp.Value;
                    if (outfitEntry == null || outfitEntry.outfit == null || outfitClone == null) continue;

                    // If specific names selected, skip non-selected outfits
                    if (!applyToAllTargets && selectedNames.Count > 0 && !selectedNames.Contains(outfitEntry.outfit.name)) continue;

                    var outfitRenderers = outfitClone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    foreach (var targetSMR in outfitRenderers)
                    {
                        if (targetSMR.sharedMesh == null) continue;
                        int generated = GenerateBlendShapesOnMesh(sourceRenderer, sourceMesh, targetSMR, targetBlendShapeNames, task.maxMappingDistance, task.useSmartFrameGeneration, task.overrideExisting, task.transferMode);
                        if (generated > 0)
                        {
                            totalGenerated += generated;
                            mergeLog.AppendLine($"    Generated {generated} blend shape(s) on outfit '{outfitEntry.outfit.name}' mesh: {targetSMR.name}");
                            if (task.copyGeneratedBlendshapeAnimations)
                                CopyGeneratedBlendshapeCurvesToTarget(targetCVRAvatar, avatarRoot, sourceRenderer, targetSMR, targetBlendShapeNames, merger, settings, clipReplacementMap);

                        }
                    }
                }
            }

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Blend Shape Generation] Complete: {totalGenerated} total frame(s) created from {targetBlendShapeNames.Count} shapes");
            else if (totalGenerated > 0)
                mergeLog.AppendLine($"  Blend shape generation complete: {totalGenerated} total frame(s) created");
        }

        // Generate specific blend shapes from source mesh onto a target mesh
        private int GenerateBlendShapesOnMesh(SkinnedMeshRenderer srcSMR, Mesh srcMesh, SkinnedMeshRenderer dstSMR, HashSet<string> blendShapeNames, float maxMappingDistance, bool useSmartFrameGeneration, bool overrideExisting, BlendShapeTransferMode transferMode)
        {
            // === CRITICAL: Ensure we never modify the original asset mesh directly ===
            var dstMesh = EnsureSceneMeshInstance(dstSMR, "NDMF Generated");
            if (dstMesh == null)
            {
                mergeLog.AppendLine($"ERROR: Failed to create scene mesh instance for {dstSMR.name}");
                return 0;
            }

            // === Validate destination mesh ===
            if (dstMesh.vertexCount == 0)
            {
                mergeLog.AppendLine($"ERROR: Destination mesh {dstMesh.name} has zero vertices");
                return 0;
            }

            // === Validate source mesh ===
            if (srcMesh.vertexCount == 0)
            {
                mergeLog.AppendLine($"ERROR: Source mesh has zero vertices");
                return 0;
            }

            int addedCount = 0;
            float topologySimilarity = useSmartFrameGeneration ? CalculateTopologySimilarity(srcMesh, dstMesh) : 1f;

            // Build spatial hash for faster vertex mapping (O(n) instead of O(n^2))
            var srcVerts = srcMesh.vertices;
            float cellSize = Mathf.Max(0.01f, maxMappingDistance / 2f);
            var spatialHash = BuildSpatialHash(srcVerts, cellSize);

            var srcBounds = srcMesh.bounds;
            var dstBounds = dstMesh.bounds;
            Vector3[] srcVertsNormalized = null;
            if (transferMode == BlendShapeTransferMode.TransferFramesBoundsNormalized)
                srcVertsNormalized = NormalizeVerticesToBounds(srcVerts, srcBounds);

            int totalFramesProcessed = 0;
            int totalShapesProcessed = 0;

            mergeLog.AppendLine($"  [Blend Shape Generation] Starting on '{srcSMR.name}' -> '{dstSMR.name}' for {blendShapeNames.Count} shape(s)...");

            foreach (var shapeName in blendShapeNames)
            {
                try
                {
                    // Find blend shape in source
                    int srcIndex = FindBlendShapeIndexByName(srcMesh, shapeName);
                    if (srcIndex < 0) continue;

                    // Check if already exists on destination
                    int dstIndex = FindBlendShapeIndexByName(dstMesh, shapeName);
                    if (dstIndex >= 0 && !overrideExisting) continue; // Already present, skip if override is disabled
                    
                    // If override is enabled and shape exists, we'll add frames which will update/replace it

                    // Get frame count
                    int frameCount = srcMesh.GetBlendShapeFrameCount(srcIndex);
                    if (frameCount <= 0) continue;

                    totalShapesProcessed++;

                    // Transfer all frames for this blend shape
                    for (int frameIdx = 0; frameIdx < frameCount; frameIdx++)
                    {
                        // === Progress reporting for large operations ===
                        if (frameIdx % 10 == 0 && frameIdx > 0)
                        {
                            EditorUtility.DisplayProgressBar(
                                "Generating Blendshapes",
                                $"{shapeName} frame {frameIdx}/{frameCount} on {dstMesh.name}",
                                (float)frameIdx / Mathf.Max(1, frameCount));
                        }

                        var deltaVertices = new Vector3[srcMesh.vertexCount];
                        var deltaNormals = new Vector3[srcMesh.vertexCount];
                        var deltaTangents = new Vector3[srcMesh.vertexCount];
                        float frameWeight = srcMesh.GetBlendShapeFrameWeight(srcIndex, frameIdx);
                        srcMesh.GetBlendShapeFrameVertices(srcIndex, frameIdx, deltaVertices, deltaNormals, deltaTangents);

                        // Build approximate deltas for destination mesh
                        var dstVerts = dstMesh.vertices;
                        Vector3[] dstVertsNormalized = null;
                        if (transferMode == BlendShapeTransferMode.TransferFramesBoundsNormalized)
                            dstVertsNormalized = NormalizeVerticesToBounds(dstVerts, dstBounds);
                        var approxDeltaVertices = new Vector3[dstVerts.Length];
                        var approxDeltaNormals = new Vector3[dstVerts.Length];
                        var approxDeltaTangents = new Vector3[dstVerts.Length];

                        float maxDist = Mathf.Max(1e-6f, maxMappingDistance);

                        // Map each destination vertex to nearest source vertex (using spatial hash for efficiency)
                        for (int dvi = 0; dvi < dstVerts.Length; dvi++)
                        {
                            Vector3 dv = dstVerts[dvi];
                            if (transferMode == BlendShapeTransferMode.TransferFramesKNearestBlended)
                            {
                                var nearest = FindKNearestVerticesOptimized(dv, srcVerts, spatialHash, cellSize, maxDist, 3);
                                if (nearest.Count > 0)
                                {
                                    float invDistSum = 0f;
                                    for (int n = 0; n < nearest.Count; n++)
                                        invDistSum += 1f / Mathf.Max(1e-6f, nearest[n].distance);

                                    Vector3 v = Vector3.zero;
                                    Vector3 nrm = Vector3.zero;
                                    Vector3 tan = Vector3.zero;
                                    float nearestDist = nearest[0].distance;

                                    for (int n = 0; n < nearest.Count; n++)
                                    {
                                        float w = (1f / Mathf.Max(1e-6f, nearest[n].distance)) / Mathf.Max(1e-6f, invDistSum);
                                        int srcIdx = nearest[n].index;
                                        v += deltaVertices[srcIdx] * w;
                                        nrm += deltaNormals[srcIdx] * w;
                                        tan += deltaTangents[srcIdx] * w;
                                        if (nearest[n].distance < nearestDist) nearestDist = nearest[n].distance;
                                    }

                                    float distanceWeight = 1f - (nearestDist / maxDist);
                                    float smartWeight = topologySimilarity * distanceWeight;

                                    approxDeltaVertices[dvi] = v * smartWeight;
                                    approxDeltaNormals[dvi] = nrm * smartWeight;
                                    approxDeltaTangents[dvi] = tan * smartWeight;
                                }
                            }
                            else
                            {
                                int nearestIdx = -1;
                                float nearestDist = maxDist;

                                if (transferMode == BlendShapeTransferMode.TransferFramesBoundsNormalized && srcVertsNormalized != null && dstVertsNormalized != null)
                                {
                                    nearestIdx = FindNearestVertexLinear(dstVertsNormalized[dvi], srcVertsNormalized, maxDist);
                                    if (nearestIdx >= 0)
                                        nearestDist = Vector3.Distance(dstVertsNormalized[dvi], srcVertsNormalized[nearestIdx]);
                                }
                                else
                                {
                                    nearestIdx = FindNearestVertexOptimized(dv, srcVerts, spatialHash, cellSize, maxDist);
                                    if (nearestIdx >= 0)
                                        nearestDist = Vector3.Distance(dv, srcVerts[nearestIdx]);
                                }

                                if (nearestIdx >= 0)
                                {
                                    float distanceWeight = 1f - (nearestDist / maxDist);
                                    float smartWeight = topologySimilarity * distanceWeight;

                                    approxDeltaVertices[dvi] = deltaVertices[nearestIdx] * smartWeight;
                                    approxDeltaNormals[dvi] = deltaNormals[nearestIdx] * smartWeight;
                                    approxDeltaTangents[dvi] = deltaTangents[nearestIdx] * smartWeight;
                                }
                            }
                        }

                        // === Validate and filter delta arrays before adding ===
                        int affectedVertices = ValidateAndFilterDeltaArrays(approxDeltaVertices, approxDeltaNormals, approxDeltaTangents, dstMesh.vertexCount);

                        // === Validate array lengths match mesh vertex count ===
                        if (approxDeltaVertices.Length != dstMesh.vertexCount ||
                            approxDeltaNormals.Length != dstMesh.vertexCount ||
                            approxDeltaTangents.Length != dstMesh.vertexCount)
                        {
                            mergeLog.AppendLine($"ERROR: Delta array length mismatch for {shapeName} frame {frameIdx}");
                            mergeLog.AppendLine($"  Expected: {dstMesh.vertexCount}, Got: vertices={approxDeltaVertices.Length}, normals={approxDeltaNormals.Length}, tangents={approxDeltaTangents.Length}");
                            continue;
                        }

                        // Only add frame if at least one vertex has a meaningful delta
                        if (affectedVertices > 0)
                        {
                            try
                            {
                                dstMesh.AddBlendShapeFrame(shapeName, frameWeight, approxDeltaVertices, approxDeltaNormals, approxDeltaTangents);
                                addedCount++;
                                totalFramesProcessed++;
                            }
                            catch (Exception ex)
                            {
                                mergeLog.AppendLine($"ERROR: AddBlendShapeFrame failed for {shapeName} frame {frameIdx}: {ex.Message}");
                                mergeLog.AppendLine($"  Vertex count: dst={dstMesh.vertexCount}, delta={approxDeltaVertices.Length}");
                                mergeLog.AppendLine($"  Frame weight: {frameWeight}, Affected verts: {affectedVertices}");
                                Debug.LogError($"Blendshape generation crash prevented: {ex.Message}\n{ex.StackTrace}");
                                // Continue instead of crashing entire process
                                continue;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    mergeLog.AppendLine($"ERROR: Exception while processing blend shape {shapeName}: {ex.Message}");
                    Debug.LogError($"Blendshape processing error: {ex.Message}\n{ex.StackTrace}");
                    continue;
                }
            }

            EditorUtility.ClearProgressBar();

            if (totalShapesProcessed > 0)
            {
                mergeLog.AppendLine($"  Blend shapes: processed {totalShapesProcessed} shapes, added {totalFramesProcessed} frames to {dstMesh.name}");
            }

            return addedCount;
        }

        // === CRITICAL: Ensure the SkinnedMeshRenderer has a scene-local mesh instance before modification ===
        // If the current sharedMesh is an asset (persistent), clone it and reassign.
        private static Mesh EnsureSceneMeshInstance(SkinnedMeshRenderer smr, string suffix)
        {
            if (smr == null) return null;
            var mesh = smr.sharedMesh;
            if (mesh == null) return null;
            if (UnityEditor.EditorUtility.IsPersistent(mesh))
            {
                try
                {
                    var cloned = UnityEngine.Object.Instantiate(mesh);
                    if (cloned == null)
                    {
                        Debug.LogError($"Failed to instantiate mesh {mesh.name}");
                        return null;
                    }
                    cloned.name = mesh.name + " (" + (string.IsNullOrEmpty(suffix) ? "NDMF" : suffix) + ")";
                    smr.sharedMesh = cloned;
                    return cloned;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception while cloning mesh: {ex.Message}");
                    return null;
                }
            }
            return mesh;
        }

        // Ensure MeshFilter uses a scene-local mesh before geometry edits.
        private static Mesh EnsureSceneMeshInstance(MeshFilter meshFilter, string suffix)
        {
            if (meshFilter == null) return null;
            var mesh = meshFilter.sharedMesh;
            if (mesh == null) return null;
            if (UnityEditor.EditorUtility.IsPersistent(mesh))
            {
                try
                {
                    var cloned = UnityEngine.Object.Instantiate(mesh);
                    if (cloned == null)
                    {
                        Debug.LogError($"Failed to instantiate mesh {mesh.name}");
                        return null;
                    }
                    cloned.name = mesh.name + " (" + (string.IsNullOrEmpty(suffix) ? "NDMF" : suffix) + ")";
                    meshFilter.sharedMesh = cloned;
                    return cloned;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Exception while cloning mesh: {ex.Message}");
                    return null;
                }
            }
            return mesh;
        }

        // === BUILD SPATIAL HASH for efficient vertex lookup ===
        private Dictionary<Vector3Int, List<int>> BuildSpatialHash(Vector3[] vertices, float cellSize)
        {
            var hash = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < vertices.Length; i++)
            {
                var cell = new Vector3Int(
                    Mathf.FloorToInt(vertices[i].x / cellSize),
                    Mathf.FloorToInt(vertices[i].y / cellSize),
                    Mathf.FloorToInt(vertices[i].z / cellSize)
                );
                if (!hash.ContainsKey(cell))
                    hash[cell] = new List<int>();
                hash[cell].Add(i);
            }
            return hash;
        }

        private struct VertexDistance
        {
            public int index;
            public float distance;
        }

        private Vector3[] NormalizeVerticesToBounds(Vector3[] vertices, Bounds bounds)
        {
            var normalized = new Vector3[vertices.Length];
            Vector3 size = bounds.size;
            Vector3 min = bounds.min;

            float sx = Mathf.Max(1e-6f, size.x);
            float sy = Mathf.Max(1e-6f, size.y);
            float sz = Mathf.Max(1e-6f, size.z);

            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                normalized[i] = new Vector3(
                    (v.x - min.x) / sx,
                    (v.y - min.y) / sy,
                    (v.z - min.z) / sz);
            }

            return normalized;
        }

        private int FindNearestVertexLinear(Vector3 targetPos, Vector3[] vertices, float maxDistance)
        {
            int nearestIdx = -1;
            float nearestDist = maxDistance;
            for (int i = 0; i < vertices.Length; i++)
            {
                float dist = Vector3.Distance(targetPos, vertices[i]);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestIdx = i;
                }
            }
            return nearestIdx;
        }

        private AnimatorController EnsureMutableAnimatorControllerOnAvatar(Component cvrAvatar, Transform avatarRootClone)
        {
            if (cvrAvatar == null) return null;

            var current = GetPreferredController(cvrAvatar);
            if (current == null) return null;

            if (EditorUtility.IsPersistent(current))
            {
                var cloned = UnityEngine.Object.Instantiate(current);
                cloned.name = current.name + " (NDMF Generated)";
                TrySetActualControllerOnAvatar(cvrAvatar, cloned, avatarRootClone);
                return cloned;
            }

            return current;
        }

        private void ApplyAnimationClipReplacementsInClone(Transform avatarRoot, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, CVRMergeArmature merger)
        {
            if (avatarRoot == null || clipReplacementMap == null || clipReplacementMap.Count == 0) return;

            int componentRefsUpdated = 0;
            int controllerRefsUpdated = 0;
            var visitedControllers = new HashSet<int>();
            var controllerCloneMap = new Dictionary<RuntimeAnimatorController, RuntimeAnimatorController>();

            var components = avatarRoot.GetComponentsInChildren<Component>(true);
            foreach (var comp in components)
            {
                if (comp == null) continue;

                try
                {
                    var so = new SerializedObject(comp);
                    var prop = so.GetIterator();
                    bool enterChildren = true;
                    bool componentChanged = false;

                    while (prop.Next(enterChildren))
                    {
                        enterChildren = true;

                        if (prop.propertyType != SerializedPropertyType.ObjectReference &&
                            prop.propertyType != SerializedPropertyType.ExposedReference)
                            continue;

                        var objRef = prop.objectReferenceValue;
                        if (objRef is AnimationClip clip && clipReplacementMap.TryGetValue(clip, out var replacementClip) && replacementClip != null && replacementClip != clip)
                        {
                            prop.objectReferenceValue = replacementClip;
                            componentRefsUpdated++;
                            componentChanged = true;
                            continue;
                        }

                        if (objRef is RuntimeAnimatorController runtimeController)
                        {
                            var mutableController = GetOrCreateMutableRuntimeAnimatorControllerCopy(runtimeController, controllerCloneMap);
                            if (mutableController != runtimeController)
                            {
                                prop.objectReferenceValue = mutableController;
                                componentRefsUpdated++;
                                componentChanged = true;
                                runtimeController = mutableController;
                            }

                            controllerRefsUpdated += ReplaceAnimationClipReferencesInRuntimeAnimatorController(runtimeController, clipReplacementMap, visitedControllers, controllerCloneMap);
                        }
                    }

                    if (componentChanged)
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(comp);
                    }
                }
                catch { }
            }

            foreach (var animator in avatarRoot.GetComponentsInChildren<Animator>(true))
            {
                if (animator != null && animator.runtimeAnimatorController != null)
                {
                    var mutableController = GetOrCreateMutableRuntimeAnimatorControllerCopy(animator.runtimeAnimatorController, controllerCloneMap);
                    if (mutableController != animator.runtimeAnimatorController)
                    {
                        animator.runtimeAnimatorController = mutableController;
                        componentRefsUpdated++;
                        EditorUtility.SetDirty(animator);
                    }

                    controllerRefsUpdated += ReplaceAnimationClipReferencesInRuntimeAnimatorController(animator.runtimeAnimatorController, clipReplacementMap, visitedControllers, controllerCloneMap);
                }
            }

            if (CVRAvatarType != null)
            {
                var avatars = avatarRoot.GetComponentsInChildren(CVRAvatarType, true).OfType<Component>();
                foreach (var avatar in avatars)
                {
                    var preferredController = GetPreferredController(avatar);
                    if (preferredController != null)
                    {
                        var mutableController = GetOrCreateMutableRuntimeAnimatorControllerCopy(preferredController, controllerCloneMap);
                        if (mutableController != preferredController)
                        {
                            TrySetActualControllerOnAvatar(avatar, mutableController, avatarRoot);
                            componentRefsUpdated++;
                        }

                        controllerRefsUpdated += ReplaceAnimationClipReferencesInRuntimeAnimatorController(mutableController, clipReplacementMap, visitedControllers, controllerCloneMap);
                    }
                }
            }

            if (ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Animation Clip Copy] Rebound {clipReplacementMap.Count} modified clip(s), updated component refs: {componentRefsUpdated}, controller refs: {controllerRefsUpdated}");
        }

        private RuntimeAnimatorController GetOrCreateMutableRuntimeAnimatorControllerCopy(RuntimeAnimatorController controller, Dictionary<RuntimeAnimatorController, RuntimeAnimatorController> controllerCloneMap)
        {
            if (controller == null) return null;

            if (controllerCloneMap.TryGetValue(controller, out var existingCopy) && existingCopy != null)
                return existingCopy;

            RuntimeAnimatorController mutableController = controller;
            if (EditorUtility.IsPersistent(controller))
            {
                mutableController = UnityEngine.Object.Instantiate(controller);
                mutableController.name = controller.name + " (NDMF Modified)";
            }

            controllerCloneMap[controller] = mutableController;
            return mutableController;
        }

        private int ReplaceAnimationClipReferencesInRuntimeAnimatorController(RuntimeAnimatorController controller, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, HashSet<int> visitedControllers, Dictionary<RuntimeAnimatorController, RuntimeAnimatorController> controllerCloneMap)
        {
            if (controller == null || clipReplacementMap == null || clipReplacementMap.Count == 0) return 0;

            if (!visitedControllers.Add(controller.GetInstanceID()))
                return 0;

            if (controller is AnimatorOverrideController overrideController)
                return ReplaceAnimationClipReferencesInAnimatorOverrideController(overrideController, clipReplacementMap, visitedControllers, controllerCloneMap);

            if (controller is AnimatorController animatorController)
                return ReplaceAnimationClipReferencesInAnimatorController(animatorController, clipReplacementMap);

            return 0;
        }

        private int ReplaceAnimationClipReferencesInAnimatorOverrideController(AnimatorOverrideController overrideController, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, HashSet<int> visitedControllers, Dictionary<RuntimeAnimatorController, RuntimeAnimatorController> controllerCloneMap)
        {
            if (overrideController == null) return 0;

            int replaced = 0;
            if (overrideController.runtimeAnimatorController != null)
            {
                var mutableBaseController = GetOrCreateMutableRuntimeAnimatorControllerCopy(overrideController.runtimeAnimatorController, controllerCloneMap);
                if (mutableBaseController != overrideController.runtimeAnimatorController)
                {
                    overrideController.runtimeAnimatorController = mutableBaseController;
                    EditorUtility.SetDirty(overrideController);
                }

                replaced += ReplaceAnimationClipReferencesInRuntimeAnimatorController(overrideController.runtimeAnimatorController, clipReplacementMap, visitedControllers, controllerCloneMap);
            }

            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(overrides);

            bool changed = false;
            for (int i = 0; i < overrides.Count; i++)
            {
                var entry = overrides[i];
                if (entry.Value != null && clipReplacementMap.TryGetValue(entry.Value, out var replacementClip) && replacementClip != null && replacementClip != entry.Value)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(entry.Key, replacementClip);
                    replaced++;
                    changed = true;
                }
            }

            if (changed)
            {
                overrideController.ApplyOverrides(overrides);
                EditorUtility.SetDirty(overrideController);
            }

            return replaced;
        }

        private int ReplaceAnimationClipReferencesInAnimatorController(AnimatorController controller, Dictionary<AnimationClip, AnimationClip> clipReplacementMap)
        {
            if (controller == null) return 0;

            int replaced = 0;
            foreach (var layer in controller.layers)
            {
                if (layer?.stateMachine != null)
                    replaced += ReplaceAnimationClipReferencesInStateMachine(layer.stateMachine, clipReplacementMap);
            }

            if (replaced > 0)
                EditorUtility.SetDirty(controller);

            return replaced;
        }

        private int ReplaceAnimationClipReferencesInStateMachine(AnimatorStateMachine stateMachine, Dictionary<AnimationClip, AnimationClip> clipReplacementMap)
        {
            if (stateMachine == null) return 0;

            int replaced = 0;
            foreach (var state in stateMachine.states)
            {
                if (state.state == null || state.state.motion == null) continue;

                if (ReplaceAnimationClipReferencesInMotion(state.state.motion, clipReplacementMap, out var replacementMotion, out var motionReplacements))
                {
                    state.state.motion = replacementMotion;
                    EditorUtility.SetDirty(state.state);
                    replaced += motionReplacements;
                }
            }

            foreach (var childStateMachine in stateMachine.stateMachines)
            {
                if (childStateMachine.stateMachine != null)
                    replaced += ReplaceAnimationClipReferencesInStateMachine(childStateMachine.stateMachine, clipReplacementMap);
            }

            if (replaced > 0)
                EditorUtility.SetDirty(stateMachine);

            return replaced;
        }

        private bool ReplaceAnimationClipReferencesInMotion(Motion motion, Dictionary<AnimationClip, AnimationClip> clipReplacementMap, out Motion replacementMotion, out int replacedCount)
        {
            replacementMotion = motion;
            replacedCount = 0;
            if (motion == null || clipReplacementMap == null || clipReplacementMap.Count == 0) return false;

            if (motion is AnimationClip clip)
            {
                if (clipReplacementMap.TryGetValue(clip, out var replacementClip) && replacementClip != null && replacementClip != clip)
                {
                    replacementMotion = replacementClip;
                    replacedCount = 1;
                    return true;
                }

                return false;
            }

            if (motion is BlendTree blendTree)
            {
                var children = blendTree.children;
                var rewrittenChildren = new Motion[children.Length];
                bool anyChanged = false;

                for (int i = 0; i < children.Length; i++)
                {
                    rewrittenChildren[i] = children[i].motion;
                    if (ReplaceAnimationClipReferencesInMotion(children[i].motion, clipReplacementMap, out var childMotion, out var childReplacedCount))
                    {
                        rewrittenChildren[i] = childMotion;
                        replacedCount += childReplacedCount;
                        anyChanged = true;
                    }
                }

                if (!anyChanged) return false;

                var clonedTree = UnityEngine.Object.Instantiate(blendTree);
                clonedTree.name = blendTree.name + " (NDMF Modified)";
                var clonedChildren = clonedTree.children;
                for (int i = 0; i < clonedChildren.Length; i++)
                {
                    var child = clonedChildren[i];
                    child.motion = rewrittenChildren[i];
                    clonedChildren[i] = child;
                }
                clonedTree.children = clonedChildren;
                replacementMotion = clonedTree;
                return true;
            }

            return false;
        }

        private string GetPathRelativeToRoot(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            if (!target.IsChildOf(root)) return null;
            if (target == root) return string.Empty;

            var parts = new List<string>();
            var current = target;
            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private void CopyGeneratedBlendshapeCurvesToTarget(Component targetCVRAvatar, Transform avatarRoot, SkinnedMeshRenderer sourceRenderer, SkinnedMeshRenderer targetRenderer, HashSet<string> shapeNames, CVRMergeArmature merger, BlendShapeTransferSettings settings, Dictionary<AnimationClip, AnimationClip> clipReplacementMap)
        {
            if (targetCVRAvatar == null || avatarRoot == null || sourceRenderer == null || targetRenderer == null || shapeNames == null || shapeNames.Count == 0)
                return;

            if (!targetCVRAvatar.transform.IsChildOf(avatarRoot))
                return;

            var controller = EnsureMutableAnimatorControllerOnAvatar(targetCVRAvatar, avatarRoot);
            if (controller == null) return;

            string sourcePath = GetPathRelativeToRoot(avatarRoot, sourceRenderer.transform);
            string targetPath = GetPathRelativeToRoot(avatarRoot, targetRenderer.transform);
            if (targetPath == null) return;

            int copiedCurves = 0;
            foreach (var clip in controller.animationClips.Distinct())
            {
                if (clip == null) continue;
                bool clipChanged = false;

                var workingClip = clipReplacementMap != null && clipReplacementMap.TryGetValue(clip, out var existingCopy) && existingCopy != null
                    ? existingCopy
                    : clip;

                var bindings = AnimationUtility.GetCurveBindings(workingClip);
                foreach (var shapeName in shapeNames)
                {
                    if (string.IsNullOrWhiteSpace(shapeName)) continue;
                    string propertyName = "blendShape." + shapeName;

                    bool targetAlreadyExists = bindings.Any(b => b.type == typeof(SkinnedMeshRenderer) && b.path == targetPath && b.propertyName == propertyName);
                    if (targetAlreadyExists) continue;

                    EditorCurveBinding template = default;
                    if (string.IsNullOrEmpty(sourcePath)) continue;

                    template = bindings.FirstOrDefault(b => b.type == typeof(SkinnedMeshRenderer) && b.path == sourcePath && b.propertyName == propertyName);
                    if (string.IsNullOrEmpty(template.propertyName)) continue;

                    if (!clipChanged)
                    {
                        workingClip = GetOrCreateModifiedClipCopy(clip, clipReplacementMap);
                        bindings = AnimationUtility.GetCurveBindings(workingClip);
                        template = bindings.FirstOrDefault(b => b.type == typeof(SkinnedMeshRenderer) && b.path == sourcePath && b.propertyName == propertyName);
                        if (string.IsNullOrEmpty(template.propertyName)) continue;
                    }

                    var curve = AnimationUtility.GetEditorCurve(workingClip, template);
                    if (curve == null) continue;

                    var newBinding = template;
                    newBinding.path = targetPath;
                    AnimationUtility.SetEditorCurve(workingClip, newBinding, curve);
                    clipChanged = true;
                    copiedCurves++;
                }

                if (clipChanged)
                    EditorUtility.SetDirty(workingClip);
            }

            if (copiedCurves > 0 && ShouldLogInfo(merger))
                mergeLog.AppendLine($"  [Blend Shape Animation Copy] Added {copiedCurves} curve(s) for target mesh '{targetRenderer.name}'");
        }

        private List<VertexDistance> FindKNearestVerticesOptimized(Vector3 targetPos, Vector3[] vertices, Dictionary<Vector3Int, List<int>> spatialHash, float cellSize, float maxDistance, int k)
        {
            var best = new List<VertexDistance>(k);

            var cell = new Vector3Int(
                Mathf.FloorToInt(targetPos.x / cellSize),
                Mathf.FloorToInt(targetPos.y / cellSize),
                Mathf.FloorToInt(targetPos.z / cellSize));

            void ConsiderIndex(int idx)
            {
                float dist = Vector3.Distance(targetPos, vertices[idx]);
                if (dist > maxDistance) return;
                best.Add(new VertexDistance { index = idx, distance = dist });
            }

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var checkCell = new Vector3Int(cell.x + dx, cell.y + dy, cell.z + dz);
                        if (!spatialHash.TryGetValue(checkCell, out var cellIndices)) continue;
                        foreach (int idx in cellIndices)
                            ConsiderIndex(idx);
                    }
                }
            }

            if (best.Count == 0)
            {
                for (int i = 0; i < vertices.Length; i++)
                    ConsiderIndex(i);
            }

            best.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (best.Count > k)
                best.RemoveRange(k, best.Count - k);

            return best;
        }

        // === FIND NEAREST VERTEX using spatial hash (O(1) average vs O(n) linear) ===
        private int FindNearestVertexOptimized(Vector3 targetPos, Vector3[] vertices, Dictionary<Vector3Int, List<int>> spatialHash, float cellSize, float maxDistance)
        {
            var cell = new Vector3Int(
                Mathf.FloorToInt(targetPos.x / cellSize),
                Mathf.FloorToInt(targetPos.y / cellSize),
                Mathf.FloorToInt(targetPos.z / cellSize)
            );

            int nearestIdx = -1;
            float nearestDist = float.MaxValue;

            // Check cells in expanding radius
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var checkCell = new Vector3Int(cell.x + dx, cell.y + dy, cell.z + dz);
                        if (spatialHash.TryGetValue(checkCell, out var cellIndices))
                        {
                            foreach (int idx in cellIndices)
                            {
                                float dist = Vector3.Distance(targetPos, vertices[idx]);
                                if (dist < nearestDist && dist <= maxDistance)
                                {
                                    nearestDist = dist;
                                    nearestIdx = idx;
                                }
                            }
                        }
                    }
                }
            }

            return nearestIdx;
        }

        // === VALIDATE AND FILTER delta arrays to prevent NaN/Infinity crashes ===
        private int ValidateAndFilterDeltaArrays(Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents, int expectedLength)
        {
            int affectedVertices = 0;

            for (int i = 0; i < deltaVertices.Length && i < expectedLength; i++)
            {
                // Filter deltaVertices
                if (float.IsNaN(deltaVertices[i].x) || float.IsInfinity(deltaVertices[i].x) ||
                    float.IsNaN(deltaVertices[i].y) || float.IsInfinity(deltaVertices[i].y) ||
                    float.IsNaN(deltaVertices[i].z) || float.IsInfinity(deltaVertices[i].z))
                {
                    deltaVertices[i] = Vector3.zero;
                }

                // Filter deltaNormals
                if (float.IsNaN(deltaNormals[i].x) || float.IsInfinity(deltaNormals[i].x) ||
                    float.IsNaN(deltaNormals[i].y) || float.IsInfinity(deltaNormals[i].y) ||
                    float.IsNaN(deltaNormals[i].z) || float.IsInfinity(deltaNormals[i].z))
                {
                    deltaNormals[i] = Vector3.zero;
                }

                // Filter deltaTangents
                if (float.IsNaN(deltaTangents[i].x) || float.IsInfinity(deltaTangents[i].x) ||
                    float.IsNaN(deltaTangents[i].y) || float.IsInfinity(deltaTangents[i].y) ||
                    float.IsNaN(deltaTangents[i].z) || float.IsInfinity(deltaTangents[i].z))
                {
                    deltaTangents[i] = Vector3.zero;
                }

                // Count affected vertices
                if (deltaVertices[i].sqrMagnitude > 1e-6f)
                {
                    affectedVertices++;
                }
            }

            return affectedVertices;
        }

        private SkinnedMeshRenderer FindMatchingTargetRendererFor(SkinnedMeshRenderer source, SkinnedMeshRenderer[] candidates)
        {
            string srcMeshName = source.sharedMesh != null ? source.sharedMesh.name : null;
            string srcTransformName = source.transform.name;
            string srcRootBoneName = source.rootBone != null ? source.rootBone.name : null;

            // Prefer exact sharedMesh name match
            if (!string.IsNullOrEmpty(srcMeshName))
            {
                foreach (var c in candidates)
                {
                    if (c != null && c.sharedMesh != null && c.sharedMesh.name == srcMeshName)
                        return c;
                }
            }

            // Fallback: transform name match
            foreach (var c in candidates)
            {
                if (c != null && c.transform.name == srcTransformName)
                    return c;
            }

            // Fallback: root bone name match
            if (!string.IsNullOrEmpty(srcRootBoneName))
            {
                foreach (var c in candidates)
                {
                    if (c != null && c.rootBone != null && c.rootBone.name == srcRootBoneName)
                        return c;
                }
            }

            return null;
        }

        private SkinnedMeshRenderer ResolveRendererInCloneHierarchy(SkinnedMeshRenderer selected, Transform avatarRoot)
        {
            if (selected == null || avatarRoot == null) return null;
            if (selected.transform.IsChildOf(avatarRoot)) return selected;

            var candidates = avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            string selectedMeshName = selected.sharedMesh != null ? selected.sharedMesh.name : null;
            string selectedPath = GetTransformPath(selected.transform);

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                if (!string.Equals(candidate.name, selected.name, StringComparison.Ordinal)) continue;

                if (!string.IsNullOrEmpty(selectedMeshName))
                {
                    var candidateMeshName = candidate.sharedMesh != null ? candidate.sharedMesh.name : null;
                    if (!string.Equals(candidateMeshName, selectedMeshName, StringComparison.Ordinal))
                        continue;
                }

                string candidatePath = GetTransformPath(candidate.transform);
                if (candidatePath.EndsWith(selectedPath, StringComparison.Ordinal))
                    return candidate;
            }

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                if (!string.Equals(candidate.name, selected.name, StringComparison.Ordinal)) continue;
                if (selectedMeshName == null) return candidate;
                if (candidate.sharedMesh != null && string.Equals(candidate.sharedMesh.name, selectedMeshName, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        private string GetTransformPath(Transform t)
        {
            if (t == null) return string.Empty;
            var names = new List<string>();
            var current = t;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private Transform ResolveTransformInCloneHierarchy(Transform selected, Transform avatarRoot)
        {
            if (selected == null || avatarRoot == null) return null;
            if (selected.IsChildOf(avatarRoot)) return selected;

            string selectedPath = GetTransformPath(selected);
            var all = avatarRoot.GetComponentsInChildren<Transform>(true);

            foreach (var candidate in all)
            {
                if (candidate == null) continue;
                if (!string.Equals(candidate.name, selected.name, StringComparison.Ordinal)) continue;
                string candidatePath = GetTransformPath(candidate);
                if (candidatePath.EndsWith(selectedPath, StringComparison.Ordinal))
                    return candidate;
            }

            return all.FirstOrDefault(t => t != null && string.Equals(t.name, selected.name, StringComparison.Ordinal));
        }

        private int FindBlendShapeIndexByName(Mesh mesh, string name)
        {
            if (mesh == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < mesh.blendShapeCount; i++)
                if (mesh.GetBlendShapeName(i) == name) return i;
            return -1;
        }

        private void ApplySemanticBoneMatchingAdjustments(Dictionary<Transform, Transform> boneMap, SemanticBoneMatchingSettings settings, CVRMergeArmature merger)
        {
            if (settings == null || !settings.enable) return;
            
            bool verbose = ShouldLogDetailed(merger);
            
            if (verbose)
                mergeLog.AppendLine("  [Semantic Bone Matching] Applying advanced matching...");
            int matchCount = 0;
            
            // Apply synonym mappings first
            if (settings.synonyms != null && settings.synonyms.Count > 0)
            {
                var unmatchedBones = boneMap.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
                
                foreach (var bone in unmatchedBones)
                {
                    string boneName = settings.caseInsensitive ? bone.name.ToLowerInvariant() : bone.name;
                    
                    // Check each synonym
                    foreach (var synonym in settings.synonyms)
                    {
                        string fromName = settings.caseInsensitive ? synonym.from.ToLowerInvariant() : synonym.from;
                        if (boneName.Contains(fromName))
                        {
                            // Find target by synonym.to
                            var allTargets = boneMap.Values.Where(v => v != null).SelectMany(v => GetAllDescendants(v));
                            var match = allTargets.FirstOrDefault(t => 
                            {
                                string targetName = settings.caseInsensitive ? t.name.ToLowerInvariant() : t.name;
                                return targetName.Contains(settings.caseInsensitive ? synonym.to.ToLowerInvariant() : synonym.to);
                            });
                            
                            if (match != null)
                            {
                                boneMap[bone] = match;
                                matchCount++;
                                mergeLog.AppendLine($"    Synonym match: {bone.name} -> {match.name} (via '{synonym.from}' -> '{synonym.to}')");
                                break;
                            }
                        }
                    }
                }
            }
            
            // Apply Left/Right variation matching
            if (settings.enableLRVariations)
            {
                var unmatchedBones = boneMap.Where(kvp => kvp.Value == null).Select(kvp => kvp.Key).ToList();
                
                foreach (var bone in unmatchedBones)
                {
                    bool isLeft = settings.leftPatterns.Any(p => bone.name.Contains(p));
                    bool isRight = settings.rightPatterns.Any(p => bone.name.Contains(p));
                    
                    if (isLeft || isRight)
                    {
                        // Try to find target with same patterns
                        string baseName = bone.name;
                        foreach (var pattern in isLeft ? settings.leftPatterns : settings.rightPatterns)
                            baseName = baseName.Replace(pattern, "");
                        
                        var targetPatterns = isLeft ? settings.leftPatterns : settings.rightPatterns;
                        var allTargets = boneMap.Values.Where(v => v != null).SelectMany(v => GetAllDescendants(v));
                        
                        var match = allTargets.FirstOrDefault(t => 
                        {
                            string targetBase = t.name;
                            foreach (var pattern in targetPatterns)
                                targetBase = targetBase.Replace(pattern, "");
                            
                            return targetBase.Equals(baseName, settings.caseInsensitive ? 
                                StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
                        });
                        
                        if (match != null)
                        {
                            boneMap[bone] = match;
                            matchCount++;
                            mergeLog.AppendLine($"    L/R match: {bone.name} -> {match.name}");
                        }
                    }
                }
            }
            
            if (verbose)
                mergeLog.AppendLine($"  [Semantic Bone Matching] Complete: {matchCount} additional matches found");
        }

        private void ValidateBoneChains(Transform targetArmature, BoneChainValidationSettings settings, CVRMergeArmature merger)
        {
            if (settings == null || !settings.enable || targetArmature == null) return;
            
            bool verbose = ShouldLogDetailed(merger);
            
            if (verbose)
                mergeLog.AppendLine("  [Bone Chain Validation] Checking bone chains...");
            
            var commonChains = new[]
            {
                new[] { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head" },
                new[] { "LeftShoulder", "LeftArm", "LeftForeArm", "LeftHand" },
                new[] { "RightShoulder", "RightArm", "RightForeArm", "RightHand" },
                new[] { "LeftUpLeg", "LeftLeg", "LeftFoot" },
                new[] { "RightUpLeg", "RightLeg", "RightFoot" }
            };
            
            int brokenChains = 0;
            
            foreach (var chain in commonChains)
            {
                Transform current = targetArmature;
                for (int i = 0; i < chain.Length; i++)
                {
                    var bone = FindBoneByName(current, chain[i]);
                    if (bone == null)
                    {
                        if (settings.warnOnMissing)
                        {
                            mergeLog.AppendLine($"    WARNING: Missing bone '{chain[i]}' in chain at step {i}");
                            brokenChains++;
                        }
                        break;
                    }
                    current = bone;
                }
            }
            
            if (verbose)
                mergeLog.AppendLine($"  [Bone Chain Validation] Complete: {brokenChains} broken chains detected");
        }

        private void RunPreMergeValidations(CVRMergeArmature merger, OutfitToMerge outfit, Transform targetArmature, PreMergeValidationSettings settings)
        {
            if (settings == null) return;
            
            bool verbose = ShouldLogDetailed(merger);
            
            if (verbose)
                mergeLog.AppendLine("  [Pre-Merge Validation] Running checks...");
            int issues = 0;
            
            if (settings.checkMissingBones)
            {
                var usedBones = GetBonesUsedByMeshes(outfit.outfit.transform);
                foreach (var bone in usedBones)
                {
                    if (bone == null)
                    {
                        mergeLog.AppendLine("    WARNING: NULL bone reference detected");
                        issues++;
                    }
                }
            }
            
            if (settings.checkMeshIntegrity)
            {
                var meshes = outfit.outfit.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in meshes)
                {
                    if (smr.sharedMesh == null)
                    {
                        mergeLog.AppendLine($"    WARNING: Missing mesh on {smr.name}");
                        issues++;
                    }
                }
            }
            
            if (verbose)
                mergeLog.AppendLine($"  [Pre-Merge Validation] Complete: {issues} issues found");
        }

        private void RunPostMergeVerification(BuildContext ctx, CVRMergeArmature merger, PostMergeVerificationSettings settings)
        {
            if (settings == null || ctx?.AvatarRootTransform == null) return;
            
            bool verbose = ShouldLogDetailed(merger);
            
            if (verbose)
                mergeLog.AppendLine("[Post-Merge Verification] Running checks...");
            int warnings = 0;
            
            if (settings.checkBounds)
            {
                var allSmr = ctx.AvatarRootTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in allSmr)
                {
                    if (smr.localBounds.size.magnitude < 0.01f)
                    {
                        mergeLog.AppendLine($"  WARNING: Suspicious bounds on {smr.name}: {smr.localBounds.size}");
                        warnings++;
                    }
                }
            }
            
            if (settings.checkProbes)
            {
                var allSmr = ctx.AvatarRootTransform.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int withProbes = allSmr.Count(s => s.probeAnchor != null);
                if (verbose)
                    mergeLog.AppendLine($"  Probe anchors set: {withProbes}/{allSmr.Length}");
            }
            
            if (verbose)
                mergeLog.AppendLine($"[Post-Merge Verification] Complete: {warnings} warnings");
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // ========================================
        // HELPER METHODS
        // ========================================
        private IEnumerable<Transform> GetAllDescendants(Transform root)
        {
            yield return root;
            foreach (Transform child in root)
            {
                foreach (var descendant in GetAllDescendants(child))
                    yield return descendant;
            }
        }

        private SkinnedMeshRenderer FindClosestBodyMesh(SkinnedMeshRenderer outfitMesh, SkinnedMeshRenderer[] bodyMeshes)
        {
            // Prioritize by name similarity
            string outfitName = outfitMesh.name.ToLowerInvariant();
            foreach (var bodyMesh in bodyMeshes)
            {
                if (bodyMesh.name.ToLowerInvariant().Contains("body") || 
                    bodyMesh.name.ToLowerInvariant().Contains("base"))
                    return bodyMesh;
            }
            
            // Fallback: largest mesh by vertex count
            return bodyMeshes.OrderByDescending(m => m.sharedMesh?.vertexCount ?? 0).FirstOrDefault();
        }
    }
}


