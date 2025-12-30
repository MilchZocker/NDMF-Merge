using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NDMFMerge.Runtime
{
    [Serializable]
    public class UVValidationSettings
    {
        [Header("Logging")]
        [Tooltip("Enable verbose logging for UV validation operations.")]
        public bool verboseLogging = false;

        [Header("Settings")]
        [Tooltip("If missing UVs are detected, generate simple defaults.")]
        public bool fillMissingUVs = true;

        [Tooltip("Automatically fix overlapping UVs (basic heuristic).")]
        public bool autoFixOverlapping = false;

        [Tooltip("Automatically fix inverted UV winding if detected.")]
        public bool autoFixInverted = false;
    }

    [Serializable]
    public class MaterialConsolidationSettings
    {
        [Header("Logging")]
        [Tooltip("Enable verbose logging for material consolidation.")]
        public bool verboseLogging = false;

        [Header("Settings")]
        [Tooltip("Consolidate materials by shader + main texture.")]
        public bool consolidateByShaderAndTexture = true;

        [Tooltip("Reuse identical materials across outfits to reduce draw calls.")]
        public bool reuseExistingMaterials = true;

        [Tooltip("Merge duplicate materials found within the same outfit.")]
        public bool mergeDuplicateMaterials = true;

        [Header("Spec-Aligned Flags")]
        [Tooltip("Consolidate materials into shared instances.")]
        public bool consolidateMaterials = true;

        [Tooltip("Match materials by name similarity.")]
        public bool matchByName = true;

        [Tooltip("Match materials by shader type.")]
        public bool matchByShader = true;

        [Tooltip("Similarity threshold for name-based matching (0-1).")]
        [Range(0f, 1f)]
        public float nameSimilarityThreshold = 0.9f;
    }

    [Serializable]
    public class BlendShapeGenerationTask
    {
        [Tooltip("Enable this generation task.")]
        public bool enabled = true;

        [Tooltip("Source mesh to copy blend shapes from.")]
        public SkinnedMeshRenderer sourceGenerationMesh;

        [Tooltip("Blend shape names to generate (comma-separated). Leave empty to generate all.")]
        public string blendShapeNamesToGenerate = "";

        [Tooltip("Generate blend shapes on base body mesh.")]
        public bool generateOnBase = false;

        [Tooltip("Generate blend shapes on outfit meshes (specify which outfits below).")]
        public bool generateOnOutfits = false;

        [Tooltip("List of outfit names to generate blend shapes on.")]
        public List<string> targetOutfitNames = new List<string>();

        [Tooltip("Transfer mode for frame generation.")]
        public BlendShapeTransferMode transferMode = BlendShapeTransferMode.TransferFramesApproximate;

        [Tooltip("Max vertex-mapping distance for approximate frame transfer (meters).")]
        [Range(0f, 0.1f)]
        public float maxMappingDistance = 0.01f;

        [Tooltip("Use smart weight scaling when generating frames based on topology.")]
        public bool useSmartFrameGeneration = true;

        [Tooltip("If true, override existing blend shapes on the destination mesh. If false, skip generation for blend shapes that already exist.")]
        public bool overrideExisting = false;
    }

    [Serializable]
    public class BlendShapeTransferSettings
    {
        [Header("Logging")]
        [Tooltip("Enable verbose logging for blend shape transfer operations.")]
        public bool verboseLogging = false;

        [Header("Weight Transfer (Copy Values)")]
        [Tooltip("Enable blend shape weight transfer (copy current values between meshes).")]
        public bool enableWeightTransfer = false;

        [Tooltip("Direction of weight transfer: from outfit to base, base to outfit, or both.")]
        public BlendShapeTransferDirection weightTransferDirection = BlendShapeTransferDirection.OutfitToBase;

        [Tooltip("Match blend shapes by name for weight transfer.")]
        public bool matchByName = true;

        [Tooltip("Minimum weight threshold to consider for transfer.")]
        [Range(0f, 1f)]
        public float minWeightThreshold = 0.0f;

        [Tooltip("Use smart weight transfer that considers mesh topology similarity.")]
        public bool useSmartWeightTransfer = true;

        [Header("Blend Shape Generation (Create Frames)")]
        [Tooltip("List of blend shape generation tasks to execute.")]
        public List<BlendShapeGenerationTask> generationTasks = new List<BlendShapeGenerationTask>();
    }

    public enum BlendShapeTransferMode
    {
        CopyWeightsOnly = 0,
        TransferFramesApproximate = 1
    }

    public enum BlendShapeTransferDirection
    {
        [InspectorName("Outfit → Base")]
        OutfitToBase = 0,

        [InspectorName("Base → Outfit")]
        BaseToOutfit = 1,

        [InspectorName("Both Directions")]
        Bidirectional = 2
    }

    [Serializable]
    public class SemanticBoneMatchingSettings
    {
        [Header("Logging")]
        [Tooltip("Enable verbose logging for semantic bone matching.")]
        public bool verboseLogging = false;

        [Header("Settings")]
        [Tooltip("Enable semantic bone matching using synonyms (e.g., 'pelvis' -> 'Hips').")]
        public bool enable = false;

        [Tooltip("Synonym mappings applied before fuzzy/name matching.")]
        public List<BoneNameMapping> synonyms = new List<BoneNameMapping>();

        [Header("Pattern Fallbacks")]
        [Tooltip("Generic patterns to match common bones (wildcards * ? supported). Examples: 'hips', 'pelvis', 'upperarm', 'lowerarm', 'thigh', 'calf', 'shin', 'foot', 'toe'.")]
        public List<string> patterns = new List<string>();

        [Tooltip("Treat pattern matching as case-insensitive.")]
        public bool caseInsensitive = true;

        [Header("Left/Right Variants")]
        [Tooltip("Enable handling of left/right naming variations (e.g., '.L', '_L', 'Left').")]
        public bool enableLRVariations = true;

        [Tooltip("Tokens/patterns identifying LEFT side bones.")]
        public List<string> leftPatterns = new List<string> { ".L", "_L", " L", "Left", "left" };

        [Tooltip("Tokens/patterns identifying RIGHT side bones.")]
        public List<string> rightPatterns = new List<string> { ".R", "_R", " R", "Right", "right" };
    }

    [Serializable]
    public class BoneChainValidationSettings
    {
        [Header("Logging")]
        [Tooltip("Enable verbose logging for bone chain validation.")]
        public bool verboseLogging = false;

        [Header("Settings")]
        [Tooltip("Enable validation of common bone chains (e.g., spine, legs, arms).")]
        public bool enable = false;

        [Tooltip("Log warnings for missing or broken chains.")]
        public bool warnOnMissing = true;
    }

    [Serializable]
    public class PreMergeValidationSettings
    {
        [Header("Logging")]
        [Tooltip("Enable verbose logging for pre-merge validation.")]
        public bool verboseLogging = false;

        [Header("Settings")]
        [Tooltip("Check for missing bones referenced by meshes before merge.")]
        public bool checkMissingBones = true;

        [Tooltip("Check for null/invalid meshes or components.")]
        public bool checkMeshIntegrity = true;
    }

    [Serializable]
    public class PostMergeVerificationSettings
    {
        [Header("Logging")]
        [Tooltip("Enable verbose logging for post-merge verification.")]
        public bool verboseLogging = false;

        [Header("Settings")]
        [Tooltip("Verify bounds were applied and look sane after merge.")]
        public bool checkBounds = true;

        [Tooltip("Verify probe anchor settings were copied where requested.")]
        public bool checkProbes = true;
    }

    // ========================================
    // PRESET & TEMPLATE SYSTEM
    // ========================================
    [Serializable]
    public class MergePreset
    {
        public string presetName;
        public string description;
        public string outfitType;

        public BoundsFixMode boundsFixMode;
        public ProbeAnchorSyncMode probeAnchorSyncMode;
        public bool forceScaleToOne;
        public bool removeUnusedBones;
        public List<BoneNameMapping> boneNameMappings;
        public string uniqueBonePrefix;
        public string meshPrefix;
    }

    [Serializable]
    public class BoneTemplate
    {
        public string templateName;
        public string avatarBaseType;
        public List<BoneNameMapping> standardMappings = new List<BoneNameMapping>();
        public List<string> commonBoneNames = new List<string>();
    }

    [Serializable]
    public class OutfitTag
    {
        public string tagName;
        public Color tagColor = Color.white;
    }

    public enum BoneConflictResolution
    {
        [InspectorName("Force Merge (Snap)")]
        StillMerge,

        [InspectorName("Rename (Keep Separate)")]
        Rename,

        [InspectorName("Don't Merge (Delete/Ignore)")]
        DontMerge,

        [InspectorName("Merge Into Selected...")]
        MergeIntoSelected,

        [InspectorName("Constraint To Target (Safe)")]
        ConstraintToTarget
    }

    public enum BoundsFixMode
    {
        None = 0,
        CopyFromSelected = 1,
        RecalculateFromMesh = 2
    }

    public enum ProbeAnchorSyncMode
    {
        None = 0,
        AutoDetect = 1,
        CopyFromSelected = 2
    }

    [Serializable]
    public class BoneNameMapping
    {
        [Tooltip("Source/outfit bone name (after prefix/suffix stripping).")]
        public string from;

        [Tooltip("Target/avatar bone name to map into.")]
        public string to;
    }

    [Serializable]
    public class OutfitToMerge
    {
        // Used by Editor for the foldout state
        public bool isExpanded = false;

        [Tooltip("The outfit/armature GameObject to merge")]
        public GameObject outfit;

        [Tooltip("Prefix to remove from bone names when matching")]
        public string prefix = "";

        [Tooltip("Suffix to remove from bone names when matching")]
        public string suffix = "";

        [Tooltip("Prefix to add to unique bones (bones that don't exist in avatar)")]
        public string uniqueBonePrefix = "";

        [Tooltip("Prefix to add to all mesh GameObjects")]
        public string meshPrefix = "";

        [Space(6)]
        [Tooltip("Merge this outfit's Animator into the target. Skips AAS autogenerated layers.")]
        [InspectorName("Merge Animator (Basic)")]
        public bool mergeAnimator = false;

        [Tooltip("Merge this outfit's Animator into the target INCLUDING AAS autogenerated layers.")]
        [InspectorName("Merge Animator (+AAS Layers)")]
        public bool mergeAnimatorIncludingAAS = false;

        // =======================
        // NEW PER-OUTFIT OPTIONS
        // =======================

        [Header("Per-Outfit Fixes")]

        [Tooltip("Fix SkinnedMeshRenderer bounds to prevent frustum culling issues.")]
        public BoundsFixMode boundsFixMode = BoundsFixMode.None;

        [Tooltip("When set to 'CopyFromSelected', use this body SkinnedMeshRenderer as the reference for bounds/probe settings. If empty, a fallback body mesh is auto-detected.")]
        public SkinnedMeshRenderer referenceBodyMesh;

        [Tooltip("Sync probe anchor from body mesh: None (skip), AutoDetect (find body mesh automatically), or CopyFromSelected (use reference mesh).")]
        public ProbeAnchorSyncMode probeAnchorSyncMode = ProbeAnchorSyncMode.AutoDetect;

        [Tooltip("When set to 'CopyFromSelected', use this mesh as the reference for probe anchor. If empty, uses referenceBodyMesh or auto-detects.")]
        public SkinnedMeshRenderer referenceProbeAnchorMesh;

        [Tooltip("Force outfit root scale to (1,1,1) before merging.")]
        public bool forceScaleToOne = false;

        [Tooltip("After merge, delete outfit bones that have no vertex weights and no child objects/components.")]
        public bool removeUnusedBones = false;

        [Header("Per-Outfit Bone Mapping")]

        [Tooltip("Optional extra bone mappings for this specific outfit.")]
        public List<BoneNameMapping> boneNameMappings = new List<BoneNameMapping>();
    }

    [Serializable]
    public class BoneConflictEntry
    {
        [HideInInspector]
        public string outfitName;

        [Tooltip("The bone on the Outfit")]
        public Transform sourceBone;

        [Tooltip("The bone on the Avatar it tried to match with")]
        public Transform targetBone;

        [Tooltip("How to resolve this specific conflict")]
        public BoneConflictResolution resolution = BoneConflictResolution.StillMerge;

        [Tooltip("Required if 'Merge Into Selected' is chosen")]
        public Transform customTargetBone;

        // Debug info
        public Vector3 positionDifference;
        public float rotationDifference;
        public Vector3 scaleDifference;
    }

    [Serializable]
    public class BrokenAnimatorReference
    {
        public string originalPath;
        public string suggestedPath;
        public UnityEngine.Object componentReference;
        public string fieldName;
        public bool autoFixed;
    }

    [ExecuteInEditMode]
    [AddComponentMenu("NDMF Merge/CVR Merge Armature")]
    [DisallowMultipleComponent]
    public class CVRMergeArmature : MonoBehaviour
    {
        [Header("Outfits to Merge")]
        public List<OutfitToMerge> outfitsToMerge = new List<OutfitToMerge>();

        [Header("Global Tolerance Settings")]
        [Tooltip("Position difference (meters) to consider a conflict.")]
        public float positionThreshold = 0.001f;

        [Tooltip("Rotation difference (degrees) to consider a conflict.")]
        public float rotationThreshold = 0.5f;

        [Tooltip("If true, scale differences will be flagged as conflicts.")]
        public bool detectScaleConflicts = true;

        [Tooltip("Scale difference (vector magnitude) to consider a conflict.")]
        public float scaleThreshold = 0.01f;

        [Header("Bone Conflict Resolution")]
        [Tooltip("Default resolution when a mismatch is detected.")]
        public BoneConflictResolution defaultBoneConflictResolution = BoneConflictResolution.StillMerge;

        [Tooltip("Per-bone conflict resolution (Populated via 'Detect Mismatches' button)")]
        public List<BoneConflictEntry> boneConflicts = new List<BoneConflictEntry>();

        [Header("Exclusions")]
        public List<Transform> excludedTransforms = new List<Transform>();
        public List<string> excludedNamePatterns = new List<string>();

        [Space(10)]
        [Header("Component Merging Options")]
        [Tooltip("If checked, attempts to normalize parent scales before merging to prevent 'exploding' meshes.")]
        public bool preventScaleDistortion = true;

        [Tooltip("Merge DynamicBone components")]
        public bool mergeDynamicBones = true;

        [Tooltip("Merge Magica Cloth components")]
        public bool mergeMagicaCloth = true;

        [Header("CVR Component Merging")]
        public bool mergeAdvancedAvatarSetup = true;
        public bool generateAASControllerAtEnd = true;
        public string advancedSettingsPrefix = "";
        public bool mergeAdvancedPointerTrigger = true;
        public bool mergeParameterStream = true;
        public bool mergeAnimatorDriver = true;

        [Header("Animator Merging (Master)")]
        [Tooltip("Master switch. If disabled, no animators are merged regardless of per-outfit settings.")]
        public bool mergeAnimator = true;

        // =======================
        // NEW GLOBAL OPTIONS
        // =======================

        [Header("Bone Matching (Global)")]
        [Tooltip("If exact name matching fails, try mapping list and/or Levenshtein fuzzy matching.")]
        public bool enableFuzzyBoneMatching = true;

        [Tooltip("Global mapping dictionary for bone names (after prefix/suffix stripping).")]
        public List<BoneNameMapping> globalBoneNameMappings = new List<BoneNameMapping>();

        [Tooltip("Enable Levenshtein fuzzy match if no mapping and no exact match.")]
        public bool enableLevenshteinBoneMatching = false;

        [Tooltip("Maximum Levenshtein distance allowed for fuzzy matching.")]
        [Range(1, 10)]
        public int maxLevenshteinDistance = 1;

        [Header("Animator Merging (Global Improvements)")]
        [Tooltip("Rewrite AnimationClip binding paths to match the merged hierarchy.")]
        public bool animatorRewritePaths = true;

        [Tooltip("If multiple incoming layers share the same original name, merge their AvatarMasks (union).")]
        public bool animatorMergeAvatarMasks = true;

        [Tooltip("Merge layers by original layer name instead of always creating unique (outfit-prefixed) layer names.")]
        public bool animatorCombineLayersByName = false;

        [Header("Broken Animator References")]
        public List<BrokenAnimatorReference> brokenReferences = new List<BrokenAnimatorReference>();

        // =======================
        // GLOBAL OUTFIT DEFAULTS (with per-outfit override priority)
        // =======================
        [Header("Global Outfit Defaults")]
        [Tooltip("Global default prefix to strip from bone names. Individual outfits override if set.")]
        public string globalBonePrefix = "";

        [Tooltip("Global default suffix to strip from bone names. Individual outfits override if set.")]
        public string globalBoneSuffix = "";

        [Tooltip("Global default bounds fix mode. Individual outfit overrides if explicitly set.")]
        public BoundsFixMode globalBoundsFixMode = BoundsFixMode.None;

        [Tooltip("Global reference body mesh for bounds/probe settings when using CopyFromSelected mode.")]
        public SkinnedMeshRenderer globalReferenceBodyMesh;

        [Tooltip("Global default probe anchor sync mode. Individual outfit overrides if explicitly set.")]
        public ProbeAnchorSyncMode globalProbeAnchorSyncMode = ProbeAnchorSyncMode.AutoDetect;

        [Tooltip("Global reference mesh for probe anchor when using CopyFromSelected mode.")]
        public SkinnedMeshRenderer globalReferenceProbeAnchorMesh;

        [Tooltip("Global default: force outfit root scale to (1,1,1) before merging. Individual outfit overrides if explicitly set.")]
        public bool globalForceScaleToOne = false;

        [Tooltip("Global default: remove unused bones after merge. Individual outfit overrides if explicitly set.")]
        public bool globalRemoveUnusedBones = false;

        [Tooltip("Global default: merge outfit animators (basic mode). Individual outfit overrides if explicitly set.")]
        public bool globalMergeAnimator = false;

        [Tooltip("Global default: merge outfit animators including AAS layers. Individual outfit overrides if explicitly set.")]
        public bool globalMergeAnimatorIncludingAAS = false;

        // =======================
        // NEW TOOL & VALIDATION SETTINGS
        // =======================
        [Header("Mesh & UV Tools")]
        public UVValidationSettings uvValidationSettings = new UVValidationSettings();
        public MaterialConsolidationSettings materialConsolidationSettings = new MaterialConsolidationSettings();

        [Header("Blend Shape Transfer")]
        public BlendShapeTransferSettings blendShapeTransferSettings = new BlendShapeTransferSettings();

        [Header("Semantic Bone Matching")]
        public SemanticBoneMatchingSettings semanticBoneMatchingSettings = new SemanticBoneMatchingSettings();

        [Header("Bone Chain Validation")]
        public BoneChainValidationSettings boneChainValidationSettings = new BoneChainValidationSettings();

        [Header("Pre/Post Merge Validation")]
        public PreMergeValidationSettings preMergeValidationSettings = new PreMergeValidationSettings();
        public PostMergeVerificationSettings postMergeVerificationSettings = new PostMergeVerificationSettings();

        [Header("Inspector Utilities")]
        [Tooltip("Show a simplified hierarchy comparison preview in the inspector.")]
        public bool showHierarchyComparison = false;

        [Header("Preset & Template System")]
        public List<MergePreset> savedPresets = new List<MergePreset>();
        public List<BoneTemplate> boneTemplates = new List<BoneTemplate>();
        public List<OutfitTag> outfitTags = new List<OutfitTag>();

        // =======================
        // DEBUG & LOGGING SETTINGS
        // =======================
        [Header("Debug & Logging")]
        [Tooltip("Enable verbose logging for detailed merge operations")]
        public bool verboseLogging = false;

        [Tooltip("Log level: 0=Errors Only, 1=Warnings+Errors, 2=All Details")]
        [Range(0, 2)]
        public int logLevel = 2;

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
                    _cvrAvatarType = FindCVRType("ABI.CCK.Components.CVRAvatar");
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
                    _cvrSpawnableType = FindCVRType("ABI.CCK.Components.CVRSpawnable");
                }
                return _cvrSpawnableType;
            }
        }

        private static Type FindCVRType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }
            Debug.LogWarning($"Could not find type: {typeName}. Is CVR CCK installed?");
            return null;
        }

        public Component GetCVRAvatar()
        {
            if (CVRAvatarType == null) return null;
            return GetComponent(CVRAvatarType);
        }

        public static Component GetCVRAvatarFromGameObject(GameObject obj)
        {
            if (CVRAvatarType == null) return null;
            return obj.GetComponent(CVRAvatarType);
        }

        public static Component GetCVRAvatarInParent(Transform transform)
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

        public static bool IsCVRAvatar(Component component)
        {
            if (component == null || CVRAvatarType == null) return false;
            return component.GetType() == CVRAvatarType;
        }

        // ========================================
        // ORIGINAL CVRMERGEARMATURE METHODS
        // ========================================

        private void OnValidate()
        {
            if (GetCVRAvatar() == null && !Application.isPlaying)
            {
                Debug.LogWarning($"[CVR Merge Armature] This GameObject must have a CVRAvatar component!", this);
            }
        }

        public bool IsExcluded(Transform t)
        {
            if (excludedTransforms.Contains(t)) return true;
            foreach (var pattern in excludedNamePatterns)
            {
                if (MatchesPattern(t.name, pattern)) return true;
            }
            return false;
        }

        private bool MatchesPattern(string name, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(
                    name,
                    "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") + "$"
                );
            }
            catch { return false; }
        }

        // ========================================
        // PRESET SYSTEM METHODS
        // ========================================
        public void SavePreset(string presetName, string description, string outfitType)
        {
            var preset = new MergePreset
            {
                presetName = presetName,
                description = description,
                outfitType = outfitType,
                boundsFixMode = outfitsToMerge.Count > 0 ? outfitsToMerge[0].boundsFixMode : BoundsFixMode.None,
                probeAnchorSyncMode = outfitsToMerge.Count > 0 ? outfitsToMerge[0].probeAnchorSyncMode : ProbeAnchorSyncMode.AutoDetect,
                forceScaleToOne = outfitsToMerge.Count > 0 ? outfitsToMerge[0].forceScaleToOne : false,
                removeUnusedBones = outfitsToMerge.Count > 0 ? outfitsToMerge[0].removeUnusedBones : false,
                boneNameMappings = new List<BoneNameMapping>(globalBoneNameMappings),
                uniqueBonePrefix = outfitsToMerge.Count > 0 ? outfitsToMerge[0].uniqueBonePrefix : "",
                meshPrefix = outfitsToMerge.Count > 0 ? outfitsToMerge[0].meshPrefix : ""
            };
            
            savedPresets.Add(preset);
            Debug.Log($"[CVR Merge] Saved preset: {presetName}");
        }

        public void LoadPreset(MergePreset preset)
        {
            if (preset == null) return;
            
            foreach (var outfit in outfitsToMerge)
            {
                outfit.boundsFixMode = preset.boundsFixMode;
                outfit.probeAnchorSyncMode = preset.probeAnchorSyncMode;
                outfit.forceScaleToOne = preset.forceScaleToOne;
                outfit.removeUnusedBones = preset.removeUnusedBones;
                outfit.uniqueBonePrefix = preset.uniqueBonePrefix;
                outfit.meshPrefix = preset.meshPrefix;
            }
            
            globalBoneNameMappings = new List<BoneNameMapping>(preset.boneNameMappings);
            Debug.Log($"[CVR Merge] Loaded preset: {preset.presetName}");
        }

        public void DeletePreset(MergePreset preset)
        {
            savedPresets.Remove(preset);
        }
    }
}
