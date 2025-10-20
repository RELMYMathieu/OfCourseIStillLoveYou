using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace OfCourseIStillLoveYou
{
    public static class ScattererWrapper
    {
        private static bool? _isScattererAvailable;
        private static Assembly _scattererAssembly;
        private static object _scattererInstance;
        private static FieldInfo _scaledSpaceCameraField;

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
                        Debug.Log("[OfCourseIStillLoveYou]: Scatterer not found - atmospheric effects disabled");
                        _isScattererAvailable = false;
                        return false;
                    }

                    Debug.Log($"[OfCourseIStillLoveYou]: Found Scatterer assembly: {_scattererAssembly.GetName().Name}");

                    var scattererType = _scattererAssembly.GetType("Scatterer.Scatterer");
                    if (scattererType == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Scatterer main type not found");
                        _isScattererAvailable = false;
                        return false;
                    }

                    var instanceProperty = scattererType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProperty != null)
                    {
                        _scattererInstance = instanceProperty.GetValue(null, null);
                    }

                    if (_scattererInstance == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Scatterer Instance not found");
                        _isScattererAvailable = false;
                        return false;
                    }

                    _scaledSpaceCameraField = scattererType.GetField("scaledSpaceCamera", BindingFlags.Public | BindingFlags.Instance);

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

        public static void ApplyScattererToCamera(Camera targetCamera, Camera referenceCamera = null)
        {
            if (!IsScattererAvailable)
            {
                Debug.Log("[OfCourseIStillLoveYou]: Scatterer not available, skipping atmospheric effects");
                return;
            }

            if (targetCamera == null)
            {
                Debug.LogWarning("[OfCourseIStillLoveYou]: Cannot apply Scatterer to null camera");
                return;
            }

            try
            {
                Camera scattererCamera = null;

                if (_scaledSpaceCameraField != null && _scattererInstance != null)
                {
                    scattererCamera = _scaledSpaceCameraField.GetValue(_scattererInstance) as Camera;
                }

                if (scattererCamera == null)
                {
                    scattererCamera = referenceCamera ?? Camera.allCameras.FirstOrDefault(c => c.name == "Camera ScaledSpace");
                }

                if (scattererCamera == null)
                {
                    Debug.LogWarning("[OfCourseIStillLoveYou]: No Scatterer camera found");
                    return;
                }

                CopyScattererCommandBuffers(scattererCamera, targetCamera);

                Debug.Log($"[OfCourseIStillLoveYou]: Applied Scatterer atmospheric effects to camera {targetCamera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to apply Scatterer: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void CopyScattererCommandBuffers(Camera source, Camera target)
        {
            try
            {
                var events = new[]
                {
                    CameraEvent.BeforeDepthTexture,
                    CameraEvent.AfterDepthTexture,
                    CameraEvent.BeforeGBuffer,
                    CameraEvent.AfterGBuffer,
                    CameraEvent.BeforeLighting,
                    CameraEvent.AfterLighting,
                    CameraEvent.BeforeForwardOpaque,
                    CameraEvent.AfterForwardOpaque,
                    CameraEvent.BeforeImageEffectsOpaque,
                    CameraEvent.AfterImageEffectsOpaque,
                    CameraEvent.BeforeImageEffects,
                    CameraEvent.AfterImageEffects,
                    CameraEvent.AfterEverything
                };

                int copiedBuffers = 0;

                foreach (var evt in events)
                {
                    var buffers = source.GetCommandBuffers(evt);
                    if (buffers != null && buffers.Length > 0)
                    {
                        foreach (var buffer in buffers)
                        {
                            if (IsScattererBuffer(buffer.name))
                            {
                                target.AddCommandBuffer(evt, buffer);
                                copiedBuffers++;
                                Debug.Log($"[OfCourseIStillLoveYou]: Copied Scatterer buffer '{buffer.name}' at {evt}");
                            }
                        }
                    }
                }

                if (copiedBuffers == 0)
                {
                    Debug.Log("[OfCourseIStillLoveYou]: No active Scatterer command buffers found (may be in space or wrong body)");
                }
                else
                {
                    Debug.Log($"[OfCourseIStillLoveYou]: Copied {copiedBuffers} Scatterer command buffers");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error copying Scatterer command buffers: {ex.Message}");
            }
        }

        private static bool IsScattererBuffer(string bufferName)
        {
            if (string.IsNullOrEmpty(bufferName))
                return false;

            var scattererKeywords = new[]
            {
                "Scatterer",
                "Ocean",
                "Underwater",
                "Godrays",
                "SMAA"
            };

            return scattererKeywords.Any(keyword => bufferName.Contains(keyword));
        }

        public static void RemoveScattererFromCamera(Camera camera)
        {
            if (!IsScattererAvailable || camera == null)
                return;

            try
            {
                var events = (CameraEvent[])Enum.GetValues(typeof(CameraEvent));
                int removedBuffers = 0;

                foreach (var evt in events)
                {
                    var buffers = camera.GetCommandBuffers(evt);
                    if (buffers != null && buffers.Length > 0)
                    {
                        foreach (var buffer in buffers.ToArray())
                        {
                            if (IsScattererBuffer(buffer.name))
                            {
                                camera.RemoveCommandBuffer(evt, buffer);
                                removedBuffers++;
                            }
                        }
                    }
                }

                Debug.Log($"[OfCourseIStillLoveYou]: Removed {removedBuffers} Scatterer buffers from camera {camera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OfCourseIStillLoveYou]: Error removing Scatterer from camera: {ex.Message}");
            }
        }
    }
}
