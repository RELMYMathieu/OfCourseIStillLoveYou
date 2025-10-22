using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace OfCourseIStillLoveYou
{
    public static class EVEWrapper
    {
        private static bool? _isEVEAvailable;
        private static Assembly _eveAssembly;

        private static Type _wetSurfacesRendererType;
        private static Type _screenSpaceShadowsRendererType;
        private static Type _volumetricCloudsRendererType;
        private static Type _particleFieldRendererType;

        public static bool IsEVEAvailable
        {
            get
            {
                if (_isEVEAvailable.HasValue)
                    return _isEVEAvailable.Value;

                try
                {
                    var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();

                    _eveAssembly = allAssemblies.FirstOrDefault(a =>
                        !a.IsDynamic &&
                        a.GetTypes().Any(t => t.Namespace == "Atmosphere"));

                    if (_eveAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: EVE not found - water puddles disabled");
                        _isEVEAvailable = false;
                        return false;
                    }

                    Debug.Log($"[OfCourseIStillLoveYou]: Found EVE assembly: {_eveAssembly.GetName().Name}");

                    _wetSurfacesRendererType = _eveAssembly.GetType("Atmosphere.WetSurfacesPerCameraRenderer");
                    _screenSpaceShadowsRendererType = _eveAssembly.GetType("Atmosphere.ScreenSpaceShadowsRenderer");
                    _volumetricCloudsRendererType = _eveAssembly.GetType("Atmosphere.DeferredRaymarchedVolumetricCloudsRenderer");
                    _particleFieldRendererType = _eveAssembly.GetType("Atmosphere.ParticleField+ParticleFieldRenderer");

                    if (_wetSurfacesRendererType == null &&
                        _screenSpaceShadowsRendererType == null &&
                        _volumetricCloudsRendererType == null &&
                        _particleFieldRendererType == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: EVE component types not found - incompatible EVE version?");
                        _isEVEAvailable = false;
                        return false;
                    }

                    _isEVEAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: EVE integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking EVE availability: {ex.Message}");
                    _isEVEAvailable = false;
                    return false;
                }
            }
        }

        public static void ApplyEVEToCamera(Camera targetCamera, Camera referenceCamera = null)
        {
            if (!IsEVEAvailable)
            {
                Debug.Log("[OfCourseIStillLoveYou]: EVE not available, skipping water effects");
                return;
            }

            if (targetCamera == null)
            {
                Debug.LogWarning("[OfCourseIStillLoveYou]: Cannot apply EVE to null camera");
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
                    Debug.LogWarning("[OfCourseIStillLoveYou]: No reference camera found for EVE");
                    return;
                }

                AddEVEComponent(targetCamera, referenceCamera, _wetSurfacesRendererType, "WetSurfacesRenderer");
                AddEVEComponent(targetCamera, referenceCamera, _volumetricCloudsRendererType, "VolumetricCloudsRenderer");
                AddEVEComponent(targetCamera, referenceCamera, _particleFieldRendererType, "ParticleFieldRenderer");

                //CopyEVECommandBuffers(referenceCamera, targetCamera);

                Debug.Log($"[OfCourseIStillLoveYou]: Applied EVE water effects to camera {targetCamera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to apply EVE effects: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void AddEVEComponent(Camera targetCamera, Camera referenceCamera, Type componentType, string componentName)
        {
            if (componentType == null)
            {
                Debug.Log($"[OfCourseIStillLoveYou]: Skipping {componentName} - type not found");
                return;
            }

            try
            {
                var existingComponent = targetCamera.gameObject.GetComponent(componentType);
                if (existingComponent != null)
                {
                    Debug.Log($"[OfCourseIStillLoveYou]: {componentName} already exists on {targetCamera.name}");
                    return;
                }

                var referenceComponent = referenceCamera.gameObject.GetComponent(componentType);
                if (referenceComponent == null)
                {
                    Debug.Log($"[OfCourseIStillLoveYou]: No {componentName} on reference camera");
                    return;
                }

                var newComponent = targetCamera.gameObject.AddComponent(componentType);

                if (newComponent != null)
                {
                    CopyComponentFields(referenceComponent, newComponent);
                    Debug.Log($"[OfCourseIStillLoveYou]: Added {componentName} to {targetCamera.name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Could not add {componentName}: {ex.Message}");
            }
        }

        private static void CopyEVECommandBuffers(Camera source, Camera target)
        {
            try
            {
                var events = new[]
                {
                    CameraEvent.BeforeReflections,
                    CameraEvent.BeforeLighting,
                    CameraEvent.AfterLighting,
                    CameraEvent.BeforeImageEffects
                };

                foreach (var evt in events)
                {
                    var buffers = source.GetCommandBuffers(evt);
                    if (buffers != null && buffers.Length > 0)
                    {
                        foreach (var buffer in buffers)
                        {
                            if (buffer.name.Contains("EVE") ||
                                buffer.name.Contains("Wet") ||
                                buffer.name.Contains("Atmosphere"))
                            {
                                var existingBuffers = target.GetCommandBuffers(evt);
                                bool alreadyExists = existingBuffers.Any(b => b.name == buffer.name);

                                if (!alreadyExists)
                                {
                                    target.AddCommandBuffer(evt, buffer);
                                    Debug.Log($"[OfCourseIStillLoveYou]: Copied EVE CommandBuffer '{buffer.name}' at {evt}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error copying EVE CommandBuffers: {ex.Message}");
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

        public static void RemoveEVEFromCamera(Camera camera)
        {
            if (camera == null) return;

            try
            {
                RemoveComponentIfExists(camera, _wetSurfacesRendererType);
                RemoveComponentIfExists(camera, _screenSpaceShadowsRendererType);
                RemoveComponentIfExists(camera, _volumetricCloudsRendererType);
                RemoveComponentIfExists(camera, _particleFieldRendererType);

                /*
                var events = new[]
                {
                    CameraEvent.BeforeReflections,
                    CameraEvent.BeforeLighting,
                    CameraEvent.AfterLighting,
                    CameraEvent.BeforeImageEffects
                };

                foreach (var evt in events)
                {
                    var buffers = camera.GetCommandBuffers(evt);
                    foreach (var buffer in buffers.ToArray())
                    {
                        if (buffer.name.Contains("EVE") ||
                            buffer.name.Contains("Wet") ||
                            buffer.name.Contains("Atmosphere"))
                        {
                            camera.RemoveCommandBuffer(evt, buffer);
                        }
                    }
                }
                */

                Debug.Log($"[OfCourseIStillLoveYou]: Removed EVE effects from camera {camera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Error removing EVE effects: {ex.Message}");
            }
        }

        private static void RemoveComponentIfExists(Camera camera, Type componentType)
        {
            if (componentType == null) return;

            var component = camera.gameObject.GetComponent(componentType);
            if (component != null)
            {
                UnityEngine.Object.Destroy(component);
            }
        }

        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsEVEAvailable)
                return "EVE not available";

            var info = $"EVE Integration for {camera.name}:\n";

            CheckComponent(camera, _wetSurfacesRendererType, "WetSurfacesRenderer", ref info);
            CheckComponent(camera, _screenSpaceShadowsRendererType, "ScreenSpaceShadowsRenderer", ref info);
            CheckComponent(camera, _volumetricCloudsRendererType, "VolumetricCloudsRenderer", ref info);
            CheckComponent(camera, _particleFieldRendererType, "ParticleFieldRenderer", ref info);

            var bufferCount = 0;
            var events = Enum.GetValues(typeof(CameraEvent)).Cast<CameraEvent>();
            foreach (var evt in events)
            {
                var buffers = camera.GetCommandBuffers(evt);
                foreach (var buffer in buffers)
                {
                    if (buffer.name.Contains("EVE") || buffer.name.Contains("Wet"))
                    {
                        bufferCount++;
                        info += $"- CommandBuffer: '{buffer.name}' at {evt}\n";
                    }
                }
            }

            if (bufferCount == 0)
            {
                info += "- No EVE CommandBuffers found\n";
            }

            return info;
        }

        private static void CheckComponent(Camera camera, Type componentType, string componentName, ref string info)
        {
            if (componentType != null)
            {
                var component = camera.GetComponent(componentType);
                info += component != null
                    ? $"- {componentName}: Present\n"
                    : $"- {componentName}: Missing\n";
            }
        }
    }
}
