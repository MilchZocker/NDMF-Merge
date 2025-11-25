using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using nadena.dev.ndmf;
using NDMFMerge.Runtime;

[assembly: ExportsPlugin(typeof(NDMFMerge.Editor.CVRMergeArmaturePlugin))]

namespace NDMFMerge.Editor
{
    public class CVRMergeArmaturePlugin : Plugin<CVRMergeArmaturePlugin>
    {
        public override string QualifiedName => "dev.milchzocker.ndmf-merge";
        public override string DisplayName => "NDMF Merge Armature";
        
        protected override void Configure()
        {
            // Run during Resolving phase, before avatar processing
            InPhase(BuildPhase.Resolving)
                .Run("Merge Armatures", ctx =>
                {
                    var mergeComponents = ctx.AvatarRootTransform
                        .GetComponentsInChildren<CVRMergeArmature>(true);
                    
                    foreach (var merge in mergeComponents)
                    {
                        try
                        {
                            MergeArmature(ctx, merge);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Failed to merge armature {merge.gameObject.name}: {ex.Message}", merge);
                        }
                    }
                });
        }
        
        private void MergeArmature(BuildContext ctx, CVRMergeArmature merger)
        {
            Debug.Log($"[NDMF Merge] Processing {merger.gameObject.name}");
            
            // Find target armature
            Transform targetArmature = merger.targetArmature;
            if (targetArmature == null)
            {
                targetArmature = FindAvatarArmature(ctx.AvatarRootTransform);
            }
            
            if (targetArmature == null)
            {
                Debug.LogError($"[NDMF Merge] Could not find target armature for {merger.gameObject.name}", merger);
                return;
            }
            
            Debug.Log($"[NDMF Merge] Target armature: {targetArmature.name}");
            
            // Build bone mapping
            var boneMap = BuildBoneMapping(merger.transform, targetArmature, merger.prefix, merger.suffix);
            Debug.Log($"[NDMF Merge] Mapped {boneMap.Count} bones");
            
            // Remap skinned mesh renderers
            var skinnedMeshes = merger.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinnedMeshes)
            {
                RemapSkinnedMeshRenderer(smr, boneMap, targetArmature);
            }
            Debug.Log($"[NDMF Merge] Remapped {skinnedMeshes.Length} SkinnedMeshRenderers");
            
            // Remap constraints
            var constraints = merger.GetComponentsInChildren<IConstraint>(true);
            foreach (var constraint in constraints)
            {
                RemapConstraint(constraint, boneMap);
            }
            
            // Remap PhysBones via reflection
            if (merger.mergePhysBones)
            {
                RemapPhysBones(merger, boneMap);
            }
            
            // Remap Contacts via reflection
            if (merger.mergeContacts)
            {
                RemapContacts(merger, boneMap);
            }
            
            // Move non-bone children to avatar root
            MoveNonBoneChildren(merger.transform, ctx.AvatarRootTransform, boneMap);
            
            // Cleanup merged bones
            CleanupMergedBones(merger.transform, boneMap);
            
            Debug.Log($"[NDMF Merge] Completed merging {merger.gameObject.name}");
        }
        
        private Dictionary<Transform, Transform> BuildBoneMapping(
            Transform source, Transform target, string prefix, string suffix)
        {
            var mapping = new Dictionary<Transform, Transform>();
            
            void MapBone(Transform sourceBone)
            {
                string boneName = sourceBone.name;
                
                // Remove prefix/suffix
                if (!string.IsNullOrEmpty(prefix) && boneName.StartsWith(prefix))
                    boneName = boneName.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(suffix) && boneName.EndsWith(suffix))
                    boneName = boneName.Substring(0, boneName.Length - suffix.Length);
                
                // Find matching bone in target
                Transform targetBone = FindBoneByName(target, boneName);
                
                if (targetBone != null)
                {
                    mapping[sourceBone] = targetBone;
                    Debug.Log($"  Mapped: {sourceBone.name} -> {targetBone.name}");
                }
                else
                {
                    Debug.LogWarning($"  No match found for bone: {sourceBone.name} (looking for: {boneName})");
                }
                
                // Recurse to children
                foreach (Transform child in sourceBone)
                {
                    MapBone(child);
                }
            }
            
            MapBone(source);
            return mapping;
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
            
            // Update root bone
            if (smr.rootBone != null && boneMap.TryGetValue(smr.rootBone, out var newRootBone))
            {
                smr.rootBone = newRootBone;
            }
            
            // Reparent the renderer if needed
            if (smr.transform.parent != null && boneMap.ContainsKey(smr.transform.parent))
            {
                // This renderer was parented to a bone, don't move it yet
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
        
        private void RemapPhysBones(CVRMergeArmature merger, Dictionary<Transform, Transform> boneMap)
        {
            // Use reflection to find and remap PhysBones
            var physBoneType = FindTypeInLoadedAssemblies("DynamicBone") ?? 
                              FindTypeInLoadedAssemblies("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBone");
            
            if (physBoneType == null) return;
            
            var physBones = merger.GetComponentsInChildren(physBoneType, true);
            foreach (var pb in physBones)
            {
                RemapComponentTransformField(pb, "m_RootTransform", boneMap);
                RemapComponentTransformField(pb, "rootTransform", boneMap);
            }
        }
        
        private void RemapContacts(CVRMergeArmature merger, Dictionary<Transform, Transform> boneMap)
        {
            // Use reflection to find and remap Contact components
            var contactType = FindTypeInLoadedAssemblies("VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver") ??
                             FindTypeInLoadedAssemblies("VRC.SDK3.Dynamics.Contact.Components.VRCContactSender");
            
            if (contactType == null) return;
            
            var contacts = merger.GetComponentsInChildren(contactType, true);
            foreach (var contact in contacts)
            {
                RemapComponentTransformField(contact, "rootTransform", boneMap);
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
            Dictionary<Transform, Transform> boneMap)
        {
            var toMove = new List<Transform>();
            
            // Find all children that aren't bones and contain actual components
            foreach (Transform child in mergedRoot)
            {
                if (!boneMap.ContainsKey(child) && ShouldPreserveHierarchy(child))
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
            // Check if this transform or any child has components we care about
            var components = transform.GetComponentsInChildren<Component>(true);
            return components.Any(c => 
                c != null && 
                !(c is Transform) && 
                !(c is CVRMergeArmature));
        }
        
        private void CleanupMergedBones(Transform mergedRoot, Dictionary<Transform, Transform> boneMap)
        {
            // Remove the merged armature component
            var mergeComponent = mergedRoot.GetComponent<CVRMergeArmature>();
            if (mergeComponent != null)
            {
                UnityEngine.Object.DestroyImmediate(mergeComponent);
            }
            
            // Remove bones that were successfully merged and have no other components
            var toCleanup = new List<GameObject>();
            
            void CheckBone(Transform bone)
            {
                var components = bone.GetComponents<Component>();
                bool canDelete = boneMap.ContainsKey(bone) && 
                    components.All(c => c is Transform || c == null);
                
                if (canDelete && bone.childCount == 0)
                {
                    toCleanup.Add(bone.gameObject);
                }
                
                // Check children first (bottom-up)
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
        
        private Transform FindAvatarArmature(Transform root)
        {
            // Look for common armature names
            var names = new[] { "Armature", "armature", "Skeleton", "skeleton", "Root", "root" };
            
            foreach (var name in names)
            {
                var found = root.Find(name);
                if (found != null)
                {
                    Debug.Log($"[NDMF Merge] Found armature by name: {found.name}");
                    return found;
                }
            }
            
            // Fallback: find first transform with multiple children (likely armature)
            foreach (Transform child in root)
            {
                if (child.childCount >= 3 && !child.GetComponent<SkinnedMeshRenderer>())
                {
                    Debug.Log($"[NDMF Merge] Auto-detected armature: {child.name}");
                    return child;
                }
            }
            
            return null;
        }
    }
}
