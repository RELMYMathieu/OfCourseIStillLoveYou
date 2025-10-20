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

        private static Type _parallaxCameraManagerType;
        private static Type _reflectionRendererType;
        private static Type _oceanReflectionManagerType;

        private static MethodInfo _registerCameraMethod;
        private static MethodInfo _unregisterCameraMethod;
        private static FieldInfo _commandBuffersField;

        public static bool IsParallaxAvailable
        {
            get
            {
                if (_isParallaxAvailable.HasValue)
                    return _isParallaxAvailable.Value;

                try
                {
                    _parallaxAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "ParallaxContinued")?.assembly;

                    if (_parallaxAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Parallax-Continued not found - terrain effects disabled");
                        _isParallaxAvailable = false;
                        return false;
                    }

                    _parallaxCameraManagerType = FindType("ParallaxCameraManager")
                        ?? FindType("CameraManager")
                        ?? FindType("Parallax.CameraManager");

                    _reflectionRendererType = FindType("ReflectionRenderer")
                        ?? FindType("Parallax.ReflectionRenderer");

                    _oceanReflectionManagerType = FindType("OceanReflectionManager")
                        ?? FindType("Parallax.OceanReflectionManager");

                    if (_parallaxCameraManagerType != null)
                    {
                        _registerCameraMethod = _parallaxCameraManagerType.GetMethod("RegisterCamera",
                            BindingFlags.Public | BindingFlags.Static);
                        _unregisterCameraMethod = _parallaxCameraManagerType.GetMethod("UnregisterCamera",
                            BindingFlags.Public | BindingFlags.Static);
                    }

                    _isParallaxAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: Parallax-Continued integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking Parallax availability: {ex.Message}");
                    _isParallaxAvailable = false;
                    return false;
                }
            }
        }

        private static Type FindType(string typeName)
        {
            if (_parallaxAssembly == null) return null;

            var type = _parallaxAssembly.GetType(typeName);
            if (type != null) return type;

            return _parallaxAssembly.GetTypes()
                .FirstOrDefault(t => t.Name.Contains(typeName) || t.FullName.Contains(typeName));
        }

        public static void ApplyParallaxToCamera(Camera targetCamera, Camera referenceCamera = null)
        {
            if (!IsParallaxAvailable)
            {
                Debug.Log("[OfCourseIStillLoveYou]: Parallax not available, skipping terrain effects");
                return;
            }

            if (targetCamera == null)
            {
                Debug.LogWarning("[OfCourseIStillLoveYou]: Cannot apply Parallax to null camera");
                return;
            }

            try
            {
                if (referenceCamera == null)
                {
                    referenceCamera = Camera.allCameras.FirstOrDefault(c => c.name == "Camera 00");
                }

                if (referenceCamera == null)
                {
                    Debug.LogWarning("[OfCourseIStillLoveYou]: No reference camera found for Parallax");
                    return;
                }

                RegisterWithParallaxManager(targetCamera);

                CopyCommandBuffers(referenceCamera, targetCamera);

                CopyParallaxComponents(referenceCamera, targetCamera);

                Debug.Log($"[OfCourseIStillLoveYou]: Applied Parallax terrain effects to camera {targetCamera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to apply Parallax: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void RegisterWithParallaxManager(Camera camera)
        {
            if (_registerCameraMethod != null)
            {
                try
                {
                    _registerCameraMethod.Invoke(null, new object[] { camera });
                    Debug.Log($"[OfCourseIStillLoveYou]: Registered {camera.name} with Parallax manager");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OfCourseIStillLoveYou]: Could not register camera with Parallax: {ex.Message}");
                }
            }
        }

        private static void CopyCommandBuffers(Camera source, Camera target)
        {
            try
            {
                var events = new[]
                {
                    CameraEvent.BeforeDepthTexture,
                    CameraEvent.BeforeGBuffer,
                    CameraEvent.AfterGBuffer,
                    CameraEvent.BeforeImageEffects,
                    CameraEvent.AfterImageEffects,
                    CameraEvent.BeforeLighting,
                    CameraEvent.AfterLighting
                };

                foreach (var evt in events)
                {
                    var buffers = source.GetCommandBuffers(evt);
                    if (buffers != null && buffers.Length > 0)
                    {
                        foreach (var buffer in buffers)
                        {
                            if (buffer.name.Contains("Parallax") ||
                                buffer.name.Contains("Terrain") ||
                                buffer.name.Contains("Reflection") ||
                                buffer.name.Contains("Ocean"))
                            {
                                var existingBuffers = target.GetCommandBuffers(evt);
                                bool alreadyExists = existingBuffers.Any(b => b.name == buffer.name);

                                if (!alreadyExists)
                                {
                                    target.AddCommandBuffer(evt, buffer);
                                    Debug.Log($"[OfCourseIStillLoveYou]: Copied CommandBuffer '{buffer.name}' at {evt}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error copying CommandBuffers: {ex.Message}");
            }
        }

        private static void CopyParallaxComponents(Camera source, Camera target)
        {
            if (_parallaxAssembly == null) return;

            try
            {
                var sourceComponents = source.GetComponents<Component>();

                foreach (var component in sourceComponents)
                {
                    if (component == null) continue;

                    var componentType = component.GetType();

                    if (componentType.Assembly == _parallaxAssembly)
                    {
                        var existing = target.gameObject.GetComponent(componentType);
                        if (existing == null)
                        {
                            var newComponent = target.gameObject.AddComponent(componentType);

                            CopyComponentFields(component, newComponent);

                            Debug.Log($"[OfCourseIStillLoveYou]: Copied Parallax component: {componentType.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error copying Parallax components: {ex.Message}");
            }
        }

        private static void CopyComponentFields(Component source, Component target)
        {
            var type = source.GetType();

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                try
                {
                    if (!field.IsLiteral && !field.IsInitOnly)
                    {
                        field.SetValue(target, field.GetValue(source));
                    }
                }
                catch
                {
                }
            }

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                try
                {
                    if (property.CanWrite && property.CanRead)
                    {
                        property.SetValue(target, property.GetValue(source));
                    }
                }
                catch
                {
                }
            }
        }

        public static void RemoveParallaxFromCamera(Camera camera)
        {
            if (camera == null) return;

            try
            {
                if (_unregisterCameraMethod != null)
                {
                    _unregisterCameraMethod.Invoke(null, new object[] { camera });
                }

                if (_parallaxAssembly != null)
                {
                    var components = camera.GetComponents<Component>();
                    foreach (var component in components)
                    {
                        if (component != null && component.GetType().Assembly == _parallaxAssembly)
                        {
                            UnityEngine.Object.Destroy(component);
                        }
                    }
                }

                var events = new[]
                {
                    CameraEvent.BeforeDepthTexture,
                    CameraEvent.BeforeGBuffer,
                    CameraEvent.AfterGBuffer,
                    CameraEvent.BeforeImageEffects,
                    CameraEvent.AfterImageEffects,
                    CameraEvent.BeforeLighting,
                    CameraEvent.AfterLighting
                };

                foreach (var evt in events)
                {
                    var buffers = camera.GetCommandBuffers(evt);
                    foreach (var buffer in buffers)
                    {
                        if (buffer.name.Contains("Parallax") ||
                            buffer.name.Contains("Terrain") ||
                            buffer.name.Contains("Reflection") ||
                            buffer.name.Contains("Ocean"))
                        {
                            camera.RemoveCommandBuffer(evt, buffer);
                        }
                    }
                }

                Debug.Log($"[OfCourseIStillLoveYou]: Removed Parallax from camera {camera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Error removing Parallax: {ex.Message}");
            }
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsParallaxAvailable)
                return "Parallax not available";

            var info = $"Parallax Integration for {camera.name}:\n";

            if (_parallaxAssembly != null)
            {
                var components = camera.GetComponents<Component>()
                    .Where(c => c != null && c.GetType().Assembly == _parallaxAssembly)
                    .ToList();

                info += $"- Parallax components: {components.Count}\n";
                foreach (var comp in components)
                {
                    info += $"  * {comp.GetType().Name}\n";
                }
            }

            var bufferCount = 0;
            var events = Enum.GetValues(typeof(CameraEvent)).Cast<CameraEvent>();
            foreach (var evt in events)
            {
                var buffers = camera.GetCommandBuffers(evt);
                foreach (var buffer in buffers)
                {
                    if (buffer.name.Contains("Parallax"))
                    {
                        bufferCount++;
                        info += $"- CommandBuffer: '{buffer.name}' at {evt}\n";
                    }
                }
            }

            if (bufferCount == 0)
            {
                info += "- No Parallax CommandBuffers found\n";
            }

            return info;
        }
    }
}
