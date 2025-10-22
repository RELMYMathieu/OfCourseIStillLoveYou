using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OfCourseIStillLoveYou
{
    public static class ScattererOceanHelper
    {
        private static Assembly _scattererAssembly;
        private static Type _oceanNodeType;
        private static MethodInfo _updateCameraSpecificUniformsMethod;
        private static object _oceanNodeInstance;
        private static bool _initialized = false;

        public static bool Initialize()
        {
            if (_initialized)
                return _oceanNodeInstance != null;

            try
            {
                _scattererAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => !a.IsDynamic && a.GetName().Name.Equals("scatterer", StringComparison.OrdinalIgnoreCase));

                if (_scattererAssembly == null)
                {
                    Debug.Log("[OCISLY-Scatterer] Assembly not found");
                    _initialized = true;
                    return false;
                }

                // Find OceanNode type
                _oceanNodeType = _scattererAssembly.GetType("Scatterer.OceanNode");
                if (_oceanNodeType == null)
                {
                    Debug.LogWarning("[OCISLY-Scatterer] OceanNode type not found");
                    _initialized = true;
                    return false;
                }

                // Find the ocean camera update hook type
                var oceanCameraUpdateHookType = _scattererAssembly.GetType("Scatterer.OceanCameraUpdateHook");
                if (oceanCameraUpdateHookType != null)
                {
                    _updateCameraSpecificUniformsMethod = oceanCameraUpdateHookType.GetMethod("updateCameraSpecificUniforms",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                _initialized = true;
                Debug.Log("[OCISLY-Scatterer] Ocean helper initialized");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OCISLY-Scatterer] Error initializing ocean helper: {ex.Message}");
                _initialized = true;
                return false;
            }
        }

        public static void FindOceanNode(string celestialBodyName)
        {
            if (!_initialized && !Initialize())
                return;

            try
            {
                // Find all OceanNode instances
                var allOceanNodes = UnityEngine.Object.FindObjectsOfType(_oceanNodeType);

                foreach (var oceanNode in allOceanNodes)
                {
                    // Check if this is the ocean for our celestial body... yeah?
                    var prolandManagerField = _oceanNodeType.GetField("prolandManager", BindingFlags.Public | BindingFlags.Instance);
                    if (prolandManagerField != null)
                    {
                        var prolandManager = prolandManagerField.GetValue(oceanNode);
                        if (prolandManager != null)
                        {
                            var parentCelestialBodyField = prolandManager.GetType().GetField("parentCelestialBody", BindingFlags.Public | BindingFlags.Instance);
                            if (parentCelestialBodyField != null)
                            {
                                var celestialBody = parentCelestialBodyField.GetValue(prolandManager) as CelestialBody;
                                if (celestialBody != null && celestialBody.name == celestialBodyName)
                                {
                                    _oceanNodeInstance = oceanNode;
                                    Debug.Log($"[OCISLY-Scatterer] Found ocean node for {celestialBodyName}");
                                    return;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OCISLY-Scatterer] Error finding ocean node: {ex.Message}");
            }
        }

        public static void UpdateOceanForCamera(Camera camera)
        {
            if (_oceanNodeInstance == null || _updateCameraSpecificUniformsMethod == null)
                return;

            try
            {
                // Get the OceanCameraUpdateHook component from the ocean water mesh
                var oceanCameraUpdateHookType = _scattererAssembly.GetType("Scatterer.OceanCameraUpdateHook");
                if (oceanCameraUpdateHookType == null)
                    return;

                // Find all OceanCameraUpdateHook components
                var hooks = UnityEngine.Object.FindObjectsOfType(oceanCameraUpdateHookType);

                foreach (var hook in hooks)
                {
                    var oceanNodeField = oceanCameraUpdateHookType.GetField("oceanNode", BindingFlags.Public | BindingFlags.Instance);
                    if (oceanNodeField != null)
                    {
                        var oceanNode = oceanNodeField.GetValue(hook);
                        if (oceanNode == _oceanNodeInstance)
                        {
                            // Get the ocean material
                            var oceanMaterialField = _oceanNodeType.GetField("m_oceanMaterial", BindingFlags.Public | BindingFlags.Instance);
                            if (oceanMaterialField != null)
                            {
                                var oceanMaterial = oceanMaterialField.GetValue(_oceanNodeInstance) as Material;
                                if (oceanMaterial != null)
                                {
                                    // Call updateCameraSpecificUniforms
                                    _updateCameraSpecificUniformsMethod.Invoke(hook, new object[] { oceanMaterial, camera });
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OCISLY-Scatterer] Error updating ocean for camera: {ex.Message}");
            }
        }
    }
}

// im going insane, please help me... or not, i guess