using UnityEngine;
using System;
using System.Collections.Generic;

namespace NDMFMerge.Runtime
{
    public enum MergeMode
    {
        ArmatureMerge,
        ModelMerge
    }
    
    public enum BoneConflictResolution
    {
        StillMerge,
        Rename,
        DontMerge
    }
    
    [Serializable]
    public class OutfitToMerge
    {
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
    }
    
    [Serializable]
    public class BoneConflictEntry
    {
        [HideInInspector]
        public string outfitName;
        public Transform sourceBone;
        public Transform targetBone;
        public BoneConflictResolution resolution = BoneConflictResolution.StillMerge;
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
        [Tooltip("List of outfits/armatures to merge into this avatar")]
        public List<OutfitToMerge> outfitsToMerge = new List<OutfitToMerge>();
        
        [Header("Bone Conflict Resolution")]
        [Tooltip("Default resolution for new conflicts")]
        public BoneConflictResolution defaultBoneConflictResolution = BoneConflictResolution.StillMerge;
        
        [Tooltip("Transform difference threshold to detect conflicts (world space)")]
        public float conflictThreshold = 0.001f;
        
        [Tooltip("Per-bone conflict resolution (detected at build time)")]
        public List<BoneConflictEntry> boneConflicts = new List<BoneConflictEntry>();
        
        [Header("Exclusions")]
        [Tooltip("Bones/transforms to exclude from merging (will be kept as-is)")]
        public List<Transform> excludedTransforms = new List<Transform>();
        
        [Tooltip("Exclude transforms by name pattern (supports wildcards: * and ?)")]
        public List<string> excludedNamePatterns = new List<string>();
        
        [Space(10)]
        [Header("Component Merging Options")]
        [Tooltip("Lock parent scale to prevent outfit scaling issues")]
        public bool lockParentScale = true;
        
        [Tooltip("Merge DynamicBone components")]
        public bool mergeDynamicBones = true;
        
        [Tooltip("Merge Magica Cloth components")]
        public bool mergeMagicaCloth = true;
        
        [Header("CVR Component Merging")]
        [Tooltip("Merge CVR Advanced Avatar Settings (toggles, sliders, etc.)")]
        public bool mergeAdvancedAvatarSetup = false;
        
        [Tooltip("Prefix for merged advanced settings entries (e.g., 'Outfit_')")]
        public string advancedSettingsPrefix = "";
        
        [Tooltip("Merge CVR Advanced Avatar Pointer/Trigger components")]
        public bool mergeAdvancedPointerTrigger = true;
        
        [Tooltip("Merge CVR Parameter Stream")]
        public bool mergeParameterStream = true;
        
        [Tooltip("Merge CVR Animator Driver")]
        public bool mergeAnimatorDriver = true;
        
        [Header("Animator Merging")]
        [Tooltip("Merge animator controller into target avatar")]
        public bool mergeAnimator = true;
        
        [Header("Broken Animator References")]
        [Tooltip("Detected broken animator references")]
        public List<BrokenAnimatorReference> brokenReferences = new List<BrokenAnimatorReference>();
        
        /// <summary>
        /// Gets the CVRAvatar component from this GameObject
        /// </summary>
        public Component GetCVRAvatar()
        {
            var cvrAvatarType = FindCVRAvatarType();
            if (cvrAvatarType == null) return null;
            
            return GetComponent(cvrAvatarType);
        }
        
        private System.Type FindCVRAvatarType()
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("ABI.CCK.Components.CVRAvatar");
                if (type != null) return type;
            }
            return null;
        }
        
        private void OnValidate()
        {
            // Validate this has CVRAvatar component
            var cvrAvatar = GetCVRAvatar();
            if (cvrAvatar == null)
            {
                Debug.LogWarning($"[CVR Merge Armature] This GameObject must have a CVRAvatar component!", this);
            }
        }
        
        public bool IsExcluded(Transform t)
        {
            if (excludedTransforms.Contains(t))
                return true;
            
            foreach (var pattern in excludedNamePatterns)
            {
                if (MatchesPattern(t.name, pattern))
                    return true;
            }
            
            return false;
        }
        
        private bool MatchesPattern(string name, string pattern)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                name,
                "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$"
            );
        }
    }
}
