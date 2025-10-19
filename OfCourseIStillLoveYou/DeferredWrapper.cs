using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace OfCourseIStillLoveYou
{
    public static class DeferredWrapper
    {
        private static bool? _isDeferredAvailable;
        private static Type _forwardRenderingCompatibilityType;
        private static MethodInfo _forwardCompatibilityInitMethod;

        public static bool IsDeferredAvailable
        {
            get
            {
                if (_isDeferredAvailable.HasValue)
                    return _isDeferredAvailable.Value;

                try
                {
                    var deferredAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "Deferred")?.assembly;

                    if (deferredAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Deferred not found - deferred rendering disabled");
                        _isDeferredAvailable = false;
                        return false;
                    }

                    _forwardRenderingCompatibilityType = deferredAssembly.GetType("Deferred.ForwardRenderingCompatibility");

                    if (_forwardRenderingCompatibilityType == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Deferred types not found - incompatible Deferred version?");
                        _isDeferredAvailable = false;
                        return false;
                    }

                    _forwardCompatibilityInitMethod = _forwardRenderingCompatibilityType.GetMethod("Init",
                        BindingFlags.Public | BindingFlags.Instance);

                    _isDeferredAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: Deferred integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking Deferred availability: {ex.Message}");
                    _isDeferredAvailable = false;
                    return false;
                }
            }
        }

        public static void EnableDeferredRendering(Camera camera)
        {
            if (!IsDeferredAvailable)
            {
                Debug.Log("[OfCourseIStillLoveYou]: Deferred not available, skipping deferred rendering setup");
                return;
            }

            if (camera == null)
            {
                Debug.LogWarning("[OfCourseIStillLoveYou]: Cannot enable deferred rendering on null camera");
                return;
            }

            try
            {
                camera.renderingPath = RenderingPath.DeferredShading;
                Debug.Log($"[OfCourseIStillLoveYou]: Enabled deferred rendering on camera {camera.name}");

                AddForwardRenderingCompatibility(camera);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to enable deferred rendering: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    camera.renderingPath = RenderingPath.Forward;
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        private static void AddForwardRenderingCompatibility(Camera camera)
        {
            if (_forwardRenderingCompatibilityType == null)
                return;

            try
            {
                var existingComponent = camera.gameObject.GetComponent(_forwardRenderingCompatibilityType);
                if (existingComponent != null)
                {
                    Debug.Log($"[OfCourseIStillLoveYou]: ForwardRenderingCompatibility already exists on {camera.name}");
                    return;
                }

                var component = camera.gameObject.AddComponent(_forwardRenderingCompatibilityType);

                if (component != null && _forwardCompatibilityInitMethod != null)
                {
                    // Initialize with renderQueue value (15-20 based on Deferred's usage)
                    // 15 for regular cameras, 20 for IVA cameras
                    _forwardCompatibilityInitMethod.Invoke(component, new object[] { 15 });
                    Debug.Log($"[OfCourseIStillLoveYou]: Added ForwardRenderingCompatibility to {camera.name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Could not add ForwardRenderingCompatibility: {ex.Message}");
            }
        }

        public static void DisableDeferredRendering(Camera camera)
        {
            if (camera == null)
                return;

            try
            {
                camera.renderingPath = RenderingPath.Forward;

                if (_forwardRenderingCompatibilityType != null)
                {
                    var component = camera.gameObject.GetComponent(_forwardRenderingCompatibilityType);
                    if (component != null)
                    {
                        UnityEngine.Object.Destroy(component);
                    }
                }

                Debug.Log($"[OfCourseIStillLoveYou]: Disabled deferred rendering on camera {camera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Error disabling deferred rendering: {ex.Message}");
            }
        }
    }
}
