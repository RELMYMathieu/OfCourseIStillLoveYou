using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace OfCourseIStillLoveYou
{
    public static class FireflyWrapper
    {
        private static bool? _isFireflyAvailable;
        private static Assembly _fireflyAssembly;
        private static Type _cameraManagerType;
        private static PropertyInfo _instanceProperty;
        private static FieldInfo _cameraBuffersField;

        private static HashSet<Camera> _camerasWithBuffers = new HashSet<Camera>();

        public static bool IsFireflyAvailable
        {
            get
            {
                if (_isFireflyAvailable.HasValue)
                    return _isFireflyAvailable.Value;

                try
                {
                    _fireflyAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name.Equals("Firefly", StringComparison.OrdinalIgnoreCase))?.assembly;

                    if (_fireflyAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Firefly not found - re-entry effects disabled");
                        _isFireflyAvailable = false;
                        return false;
                    }

                    _cameraManagerType = _fireflyAssembly.GetType("Firefly.CameraManager");

                    if (_cameraManagerType == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Firefly CameraManager type not found - incompatible Firefly version?");
                        _isFireflyAvailable = false;
                        return false;
                    }

                    _instanceProperty = _cameraManagerType.GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static);

                    _cameraBuffersField = _cameraManagerType.GetField("cameraBuffers",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (_instanceProperty == null || _cameraBuffersField == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: Firefly CameraManager members not found");
                        _isFireflyAvailable = false;
                        return false;
                    }

                    _isFireflyAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: Firefly integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking Firefly availability: {ex.Message}");
                    _isFireflyAvailable = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Checks if a vessel should have Firefly effects based on atmospheric conditions.
        /// Mirrors Firefly's internal logic for when to show effects.
        /// Thanks to MirageDev's Firefly source code for reference!
        /// </summary>
        public static bool ShouldHaveFireflyEffects(Vessel vessel)
        {
            if (!IsFireflyAvailable || vessel == null)
                return false;

            if (!vessel.mainBody.atmosphere)
                return false;

            if (!vessel.loaded || vessel.packed)
                return false;

            if (vessel.altitude > vessel.mainBody.atmosphereDepth)
                return false;

            return true;
        }

        /// <summary>
        /// Main update method, keeps the Firefly effects in sync with vessel atmosphere status.
        /// Adds buffers when vessel enters atmosphere, removes when it exits.
        /// </summary>
        public static void UpdateFireflyForCamera(Camera targetCamera, Vessel vessel)
        {
            if (!IsFireflyAvailable || targetCamera == null)
                return;

            bool shouldHaveEffects = ShouldHaveFireflyEffects(vessel);
            bool hasBuffers = _camerasWithBuffers.Contains(targetCamera);

            if (shouldHaveEffects && HasActiveEffects())
            {
                if (!hasBuffers)
                {
                    ApplyFireflyToCamera(targetCamera);
                }
            }
            else if (hasBuffers)
            {
                RemoveFireflyFromCamera(targetCamera);
            }
        }

        /// <summary>
        /// Applies all active Firefly command buffers to the target camera.
        /// </summary>
        public static void ApplyFireflyToCamera(Camera targetCamera)
        {
            if (!IsFireflyAvailable || targetCamera == null)
                return;

            // Avoids a catastrophe
            if (_camerasWithBuffers.Contains(targetCamera))
                return;

            try
            {
                var instance = _instanceProperty.GetValue(null);
                if (instance == null) return;

                var cameraBuffers = _cameraBuffersField.GetValue(instance);
                if (cameraBuffers == null) return;

                var buffersList = cameraBuffers as System.Collections.IList;
                if (buffersList == null || buffersList.Count == 0) return;

                int buffersAdded = 0;
                foreach (var item in buffersList)
                {
                    var itemType = item.GetType();
                    var keyProperty = itemType.GetProperty("Key");
                    var valueProperty = itemType.GetProperty("Value");

                    if (keyProperty == null || valueProperty == null) continue;

                    var cameraEvent = (CameraEvent)keyProperty.GetValue(item);
                    var commandBuffer = (CommandBuffer)valueProperty.GetValue(item);

                    if (commandBuffer == null) continue;

                    var existingBuffers = targetCamera.GetCommandBuffers(cameraEvent);
                    if (existingBuffers.Contains(commandBuffer))
                        continue;

                    targetCamera.AddCommandBuffer(cameraEvent, commandBuffer);
                    buffersAdded++;
                }

                if (buffersAdded > 0)
                {
                    _camerasWithBuffers.Add(targetCamera);
                    Debug.Log($"[OfCourseIStillLoveYou]: Added {buffersAdded} Firefly command buffers to {targetCamera.name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to apply Firefly effects: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Checks if Firefly has any active command buffers (i.e., if any vessel has re-entry effects).
        /// </summary>
        public static bool HasActiveEffects()
        {
            if (!IsFireflyAvailable)
                return false;

            try
            {
                var instance = _instanceProperty.GetValue(null);
                if (instance == null) return false;

                var cameraBuffers = _cameraBuffersField.GetValue(instance);
                if (cameraBuffers == null) return false;

                var buffersList = cameraBuffers as System.Collections.IList;
                if (buffersList == null) return false;

                return buffersList.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Removes all Firefly command buffers from the target camera.
        /// </summary>
        public static void RemoveFireflyFromCamera(Camera camera)
        {
            if (!IsFireflyAvailable || camera == null)
                return;

            // Don't remove if not tracked in the first place...
            if (!_camerasWithBuffers.Contains(camera))
                return;

            try
            {
                var instance = _instanceProperty.GetValue(null);
                if (instance == null) return;

                var cameraBuffers = _cameraBuffersField.GetValue(instance);
                if (cameraBuffers == null) return;

                var buffersList = cameraBuffers as System.Collections.IList;
                if (buffersList == null) return;

                int buffersRemoved = 0;
                foreach (var item in buffersList)
                {
                    var itemType = item.GetType();
                    var keyProperty = itemType.GetProperty("Key");
                    var valueProperty = itemType.GetProperty("Value");

                    if (keyProperty == null || valueProperty == null) continue;

                    var cameraEvent = (CameraEvent)keyProperty.GetValue(item);
                    var commandBuffer = (CommandBuffer)valueProperty.GetValue(item);

                    if (commandBuffer == null) continue;

                    var existingBuffers = camera.GetCommandBuffers(cameraEvent);
                    if (existingBuffers.Contains(commandBuffer))
                    {
                        camera.RemoveCommandBuffer(cameraEvent, commandBuffer);
                        buffersRemoved++;
                    }
                }

                _camerasWithBuffers.Remove(camera);

                if (buffersRemoved > 0)
                {
                    Debug.Log($"[OfCourseIStillLoveYou]: Removed {buffersRemoved} Firefly command buffers from {camera.name}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Error removing Firefly effects: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up tracking for destroyed cameras.
        /// Should be called when cameras are destroyed.
        /// Indeed is.
        /// </summary>
        public static void CleanupCamera(Camera camera)
        {
            if (camera != null)
            {
                _camerasWithBuffers.Remove(camera);
            }
        }

        /// <summary>
        /// Gets diagnostic information about Firefly integration for a camera.
        /// </summary>
        public static string GetDiagnosticInfo(Camera camera)
        {
            if (!IsFireflyAvailable)
                return "Firefly not available";

            var info = $"Firefly Integration for {camera.name}:\n";
            info += $"- Tracked as having buffers: {_camerasWithBuffers.Contains(camera)}\n";

            try
            {
                var instance = _instanceProperty.GetValue(null);
                if (instance == null)
                {
                    info += "- CameraManager instance not found\n";
                    return info;
                }

                var cameraBuffers = _cameraBuffersField.GetValue(instance);
                if (cameraBuffers == null)
                {
                    info += "- Command buffers list not found\n";
                    return info;
                }

                var buffersList = cameraBuffers as System.Collections.IList;
                if (buffersList == null)
                {
                    info += "- Could not cast buffers list\n";
                    return info;
                }

                info += $"- Total Firefly command buffers in manager: {buffersList.Count}\n";

                int buffersOnCamera = 0;
                foreach (var item in buffersList)
                {
                    var itemType = item.GetType();
                    var keyProperty = itemType.GetProperty("Key");
                    var valueProperty = itemType.GetProperty("Value");

                    if (keyProperty == null || valueProperty == null) continue;

                    var cameraEvent = (CameraEvent)keyProperty.GetValue(item);
                    var commandBuffer = (CommandBuffer)valueProperty.GetValue(item);

                    if (commandBuffer == null) continue;

                    var existingBuffers = camera.GetCommandBuffers(cameraEvent);
                    if (existingBuffers.Contains(commandBuffer))
                    {
                        info += $"  * {commandBuffer.name} at {cameraEvent}\n";
                        buffersOnCamera++;
                    }
                }

                if (buffersOnCamera == 0)
                {
                    info += "- No Firefly command buffers found on this camera\n";
                }
            }
            catch (Exception ex)
            {
                info += $"- Error getting diagnostic info: {ex.Message}\n";
            }

            return info;
        }
    }
}
