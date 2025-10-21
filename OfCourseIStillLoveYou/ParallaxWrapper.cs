using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace OfCourseIStillLoveYou
{
    public static class ParallaxWrapper
    {
        private static bool? _isParallaxAvailable;
        private static Assembly _parallaxAssembly;

        private static Type _scatterManagerType;
        private static Type _scatterRendererType;

        private static FieldInfo _instanceField;
        private static FieldInfo _activeScatterRenderersField;
        private static MethodInfo _renderInCamerasMethod;

        public static bool IsParallaxAvailable
        {
            get
            {
                if (_isParallaxAvailable.HasValue)
                    return _isParallaxAvailable.Value;

                try
                {
                    Debug.Log("[OfCourseIStillLoveYou]: Searching for Parallax assembly...");

                    _parallaxAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "Parallax")?.assembly;

                    if (_parallaxAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Assembly 'Parallax' not found in AssemblyLoader. Searching all assemblies...");

                        var allAssemblies = AssemblyLoader.loadedAssemblies.Select(a => a.name).ToArray();
                        Debug.Log("[OfCourseIStillLoveYou]: Available assemblies: " + string.Join(", ", allAssemblies));

                        _parallaxAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => !a.IsDynamic && a.GetName().Name.Contains("Parallax"));

                        if (_parallaxAssembly != null)
                        {
                            Debug.Log($"[OfCourseIStillLoveYou]: Found Parallax via AppDomain: {_parallaxAssembly.GetName().Name}");
                        }
                    }
                    else
                    {
                        Debug.Log($"[OfCourseIStillLoveYou]: Found Parallax assembly: {_parallaxAssembly.GetName().Name}");
                    }

                    if (_parallaxAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Parallax-Continued not found - terrain effects disabled");
                        _isParallaxAvailable = false;
                        return false;
                    }

                    _scatterManagerType = _parallaxAssembly.GetType("Parallax.ScatterManager");
                    Debug.Log($"[OfCourseIStillLoveYou]: ScatterManager type: {(_scatterManagerType != null ? "FOUND" : "NOT FOUND")}");

                    _scatterRendererType = _parallaxAssembly.GetType("Parallax.ScatterRenderer");
                    Debug.Log($"[OfCourseIStillLoveYou]: ScatterRenderer type: {(_scatterRendererType != null ? "FOUND" : "NOT FOUND")}");

                    if (_scatterManagerType == null || _scatterRendererType == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Parallax types not found - incompatible Parallax version?");
                        _isParallaxAvailable = false;
                        return false;
                    }

                    _instanceField = _scatterManagerType.GetField("Instance",
                        BindingFlags.Public | BindingFlags.Static);
                    Debug.Log($"[OfCourseIStillLoveYou]: Instance field: {(_instanceField != null ? "FOUND" : "NOT FOUND")}");

                    _activeScatterRenderersField = _scatterManagerType.GetField("activeScatterRenderers",
                        BindingFlags.Public | BindingFlags.Instance);
                    Debug.Log($"[OfCourseIStillLoveYou]: activeScatterRenderers field: {(_activeScatterRenderersField != null ? "FOUND" : "NOT FOUND")}");

                    _renderInCamerasMethod = _scatterRendererType.GetMethod("RenderInCameras",
                        BindingFlags.Public | BindingFlags.Instance);
                    Debug.Log($"[OfCourseIStillLoveYou]: RenderInCameras method: {(_renderInCamerasMethod != null ? "FOUND" : "NOT FOUND")}");

                    if (_instanceField == null || _activeScatterRenderersField == null || _renderInCamerasMethod == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Parallax members not found");
                        _isParallaxAvailable = false;
                        return false;
                    }

                    _isParallaxAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: Parallax-Continued integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking Parallax availability: {ex.Message}\n{ex.StackTrace}");
                    _isParallaxAvailable = false;
                    return false;
                }
            }
        }

        public static void RenderParallaxToCustomCameras(params Camera[] cameras)
        {
            if (!IsParallaxAvailable || cameras == null || cameras.Length == 0)
            {
                Debug.Log($"[OfCourseIStillLoveYou]: Parallax render skipped - Available:{IsParallaxAvailable} Cameras:{cameras?.Length ?? 0}");
                return;
            }

            try
            {
                var instance = _instanceField.GetValue(null);
                if (instance == null)
                {
                    Debug.LogWarning("[OfCourseIStillLoveYou]: ScatterManager instance is null");
                    return;
                }

                var activeRenderers = _activeScatterRenderersField.GetValue(instance) as System.Collections.IList;
                if (activeRenderers == null || activeRenderers.Count == 0)
                {
                    Debug.LogWarning($"[OfCourseIStillLoveYou]: No active renderers - List null:{activeRenderers == null} Count:{activeRenderers?.Count ?? 0}");
                    return;
                }

                Debug.Log($"[OfCourseIStillLoveYou]: Rendering {activeRenderers.Count} Parallax scatters to {cameras.Length} cameras");

                foreach (var renderer in activeRenderers)
                {
                    if (renderer != null)
                    {
                        _renderInCamerasMethod.Invoke(renderer, new object[] { cameras });
                    }
                }

                Debug.Log("[OfCourseIStillLoveYou]: Parallax render complete");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to render Parallax scatter: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static void ApplyParallaxToCamera(Camera targetCamera, Camera referenceCamera = null)
        {
            if (!IsParallaxAvailable || targetCamera == null)
                return;

            try
            {
                int originalMask = targetCamera.cullingMask;
                targetCamera.cullingMask |= (1 << 15);

                if (originalMask != targetCamera.cullingMask)
                {
                    Debug.Log($"[OfCourseIStillLoveYou]: Updated {targetCamera.name} culling mask for Parallax (layer 15)");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to apply Parallax: {ex.Message}");
            }
        }

        public static void RemoveParallaxFromCamera(Camera camera)
        {
            if (camera == null) return;

            Debug.Log($"[OfCourseIStillLoveYou]: Parallax cleanup for {camera.name} (no action needed - scatter rendering is global)");
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsParallaxAvailable)
                return "Parallax not available";

            var info = $"Parallax Integration for {camera.name}:\n";

            info += $"- Culling mask includes layer 15: {(camera.cullingMask & (1 << 15)) != 0}\n";

            try
            {
                var instance = _instanceField.GetValue(null);
                if (instance == null)
                {
                    info += "- ScatterManager instance not found\n";
                    return info;
                }

                var activeRenderers = _activeScatterRenderersField.GetValue(instance) as System.Collections.IList;
                if (activeRenderers == null)
                {
                    info += "- Active renderers list not found\n";
                    return info;
                }

                info += $"- Active scatter renderers: {activeRenderers.Count}\n";
            }
            catch (Exception ex)
            {
                info += $"- Error: {ex.Message}\n";
            }

            return info;
        }
    }
}
