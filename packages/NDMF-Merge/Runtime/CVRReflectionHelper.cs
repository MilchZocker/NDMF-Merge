using System;
using System.Reflection;
using UnityEngine;

namespace NDMFMerge.Runtime
{
    /// <summary>
    /// Helper class to access CVR CCK types via reflection since we can't reference the assembly
    /// </summary>
    public static class CVRReflectionHelper
    {
        private static Type _cvrAvatarType;
        private static Type _cvrSpawnableType;
        
        public static Type CVRAvatarType
        {
            get
            {
                if (_cvrAvatarType == null)
                {
                    _cvrAvatarType = FindType("ABI.CCK.Components.CVRAvatar");
                }
                return _cvrAvatarType;
            }
        }
        
        public static Type CVRSpawnableType
        {
            get
            {
                if (_cvrSpawnableType == null)
                {
                    _cvrSpawnableType = FindType("ABI.CCK.Components.CVRSpawnable");
                }
                return _cvrSpawnableType;
            }
        }
        
        private static Type FindType(string typeName)
        {
            // Search all loaded assemblies
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
        
        public static Component GetCVRAvatar(GameObject obj)
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
    }
}
