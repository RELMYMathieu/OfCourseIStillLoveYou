using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OfCourseIStillLoveYou
{
    public static class ScattererWrapper
    {
        private static bool? _isScattererAvailable;
        private static Assembly _scattererAssembly;

        public static bool IsScattererAvailable
        {
            get
            {
                if (_isScattererAvailable.HasValue)
                    return _isScattererAvailable.Value;

                try
                {
                    var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();

                    _scattererAssembly = allAssemblies.FirstOrDefault(a =>
                        !a.IsDynamic &&
                        a.GetName().Name.Equals("scatterer", StringComparison.OrdinalIgnoreCase));

                    if (_scattererAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Scatterer not found");
                        _isScattererAvailable = false;
                        return false;
                    }

                    _isScattererAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: Scatterer integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking Scatterer availability: {ex.Message}");
                    _isScattererAvailable = false;
                    return false;
                }
            }
        }

        public static void ApplyScattererToCamera(Camera targetCamera)
        {
            if (!IsScattererAvailable)
            {
                return;
            }

            if (targetCamera == null)
            {
                Debug.LogWarning("[OfCourseIStillLoveYou]: Cannot apply Scatterer to null camera");
                return;
            }

            try
            {
                int originalMask = targetCamera.cullingMask;

                targetCamera.cullingMask |= (1 << 9);
                targetCamera.cullingMask |= (1 << 15);

                if (originalMask != targetCamera.cullingMask)
                {
                    Debug.Log($"[OfCourseIStillLoveYou]: Updated {targetCamera.name} culling mask for Scatterer (added layers 9, 15)");
                }

                Debug.Log($"[OfCourseIStillLoveYou]: Scatterer will auto-register to {targetCamera.name} when objects are visible");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to apply Scatterer: {ex.Message}");
            }
        }

        public static void ForceEnableScattererComponents(Camera camera)
        {
            if (!IsScattererAvailable || camera == null)
                return;

            try
            {
                var scatteringBufferType = _scattererAssembly?.GetType("Scatterer.ScatteringCommandBuffer");
                var oceanBufferType = _scattererAssembly?.GetType("Scatterer.OceanCommandBuffer");

                if (scatteringBufferType != null)
                {
                    var component = camera.GetComponent(scatteringBufferType);
                    if (component != null)
                    {
                        var enableMethod = scatteringBufferType.GetMethod("EnableForThisFrame", BindingFlags.Public | BindingFlags.Instance);
                        if (enableMethod != null)
                        {
                            enableMethod.Invoke(component, null);
                        }
                    }
                }

                if (oceanBufferType != null)
                {
                    var component = camera.GetComponent(oceanBufferType);
                    if (component != null)
                    {
                        var enableMethod = oceanBufferType.GetMethod("EnableForThisFrame", BindingFlags.Public | BindingFlags.Instance);
                        if (enableMethod != null)
                        {
                            enableMethod.Invoke(component, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error forcing Scatterer components: {ex.Message}");
            }
        }

        public static void RemoveScattererFromCamera(Camera camera)
        {
            if (!IsScattererAvailable || camera == null)
                return;

            try
            {
                var scatteringBufferType = _scattererAssembly?.GetType("Scatterer.ScatteringCommandBuffer");
                var oceanBufferType = _scattererAssembly?.GetType("Scatterer.OceanCommandBuffer");

                if (scatteringBufferType != null)
                {
                    var component = camera.GetComponent(scatteringBufferType);
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                        Debug.Log($"[OfCourseIStillLoveYou]: Removed ScatteringCommandBuffer from {camera.name}");
                    }
                }

                if (oceanBufferType != null)
                {
                    var component = camera.GetComponent(oceanBufferType);
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                        Debug.Log($"[OfCourseIStillLoveYou]: Removed OceanCommandBuffer from {camera.name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error removing Scatterer from camera: {ex.Message}");
            }
        }
    }
}
