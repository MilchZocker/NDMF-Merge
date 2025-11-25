using UnityEngine;

namespace NDMFMerge.Runtime
{
    [ExecuteInEditMode]
    [AddComponentMenu("NDMF Merge/CVR Merge Armature")]
    [DisallowMultipleComponent]
    public class CVRMergeArmature : MonoBehaviour
    {
        [Tooltip("Target armature to merge into (leave empty to auto-detect avatar root armature)")]
        public Transform targetArmature;
        
        [Space(10)]
        [Header("Bone Name Matching")]
        [Tooltip("Prefix to remove from bone names when matching (e.g., 'Outfit_' removes 'Outfit_Hips' -> 'Hips')")]
        public string prefix = "";
        
        [Tooltip("Suffix to remove from bone names when matching (e.g., '_Outfit' removes 'Hips_Outfit' -> 'Hips')")]
        public string suffix = "";
        
        [Space(10)]
        [Header("Options")]
        [Tooltip("Lock parent scale to prevent outfit scaling issues")]
        public bool lockParentScale = true;
        
        [Tooltip("Merge PhysBones components from this armature")]
        public bool mergePhysBones = true;
        
        [Tooltip("Merge Contact components from this armature")]
        public bool mergeContacts = true;
        
        private void OnValidate()
        {
            // Visual feedback in editor
            if (targetArmature != null && targetArmature.IsChildOf(transform))
            {
                Debug.LogWarning($"[CVR Merge Armature] Target armature should not be a child of this object!", this);
            }
        }
    }
}
