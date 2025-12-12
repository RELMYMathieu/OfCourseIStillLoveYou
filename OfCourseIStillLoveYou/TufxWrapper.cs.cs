using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OfCourseIStillLoveYou
{
    public static class TufxWrapper
    {
        private static bool? _isTufxAvailable;
        private static Type _postProcessLayerType;
        private static Type _postProcessVolumeType;
        private static Type _texturesUnlimitedFXLoaderType;
        private static MethodInfo _addOrGetComponentMethod;
        private static MethodInfo _initMethod;
        private static PropertyInfo _resourcesProperty;
        private static FieldInfo _volumeLayerField;
        private static FieldInfo _isGlobalField;
        private static FieldInfo _priorityField;

        public static bool IsTufxAvailable
        {
            get
            {
                if (_isTufxAvailable.HasValue)
                    return _isTufxAvailable.Value;

                try
                {
                    var tufxAssembly = AssemblyLoader.loadedAssemblies
                        .FirstOrDefault(a => a.name == "TUFX")?.assembly;

                    if (tufxAssembly == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: TUFX not found - post-processing disabled");
                        _isTufxAvailable = false;
                        return false;
                    }

                    _postProcessLayerType = tufxAssembly.GetType("UnityEngine.Rendering.PostProcessing.PostProcessLayer");
                    _postProcessVolumeType = tufxAssembly.GetType("UnityEngine.Rendering.PostProcessing.PostProcessVolume");
                    _texturesUnlimitedFXLoaderType = tufxAssembly.GetType("TUFX.TexturesUnlimitedFXLoader");

                    if (_postProcessLayerType == null || _postProcessVolumeType == null || _texturesUnlimitedFXLoaderType == null)
                    {
                        Debug.LogWarning("[OfCourseIStillLoveYou]: TUFX types not found - incompatible TUFX version?");
                        _isTufxAvailable = false;
                        return false;
                    }

                    _resourcesProperty = _texturesUnlimitedFXLoaderType.GetProperty("Resources",
                        BindingFlags.Public | BindingFlags.Static);
                    _initMethod = _postProcessLayerType.GetMethod("Init",
                        BindingFlags.Public | BindingFlags.Instance);
                    _volumeLayerField = _postProcessLayerType.GetField("volumeLayer",
                        BindingFlags.Public | BindingFlags.Instance);
                    _isGlobalField = _postProcessVolumeType.GetField("isGlobal",
                        BindingFlags.Public | BindingFlags.Instance);
                    _priorityField = _postProcessVolumeType.GetField("priority",
                        BindingFlags.Public | BindingFlags.Instance);

                    var extensionsType = typeof(GameObject).Assembly.GetType("UnityEngine.GameObjectExtensions")
                        ?? typeof(GameObject);
                    _addOrGetComponentMethod = extensionsType.GetMethod("AddOrGetComponent",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(GameObject), typeof(Type) },
                        null);

                    if (_addOrGetComponentMethod == null)
                    {
                        Debug.Log("[OfCourseIStillLoveYou]: Using fallback AddOrGetComponent");
                    }

                    _isTufxAvailable = true;
                    Debug.Log("[OfCourseIStillLoveYou]: TUFX integration enabled");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OfCourseIStillLoveYou]: Error checking TUFX availability: {ex.Message}");
                    _isTufxAvailable = false;
                    return false;
                }
            }
        }

        public static void AddPostProcessing(Camera camera)
        {
            if (!IsTufxAvailable)
            {
                Debug.Log("[OfCourseIStillLoveYou]: TUFX not available, skipping post-processing");
                return;
            }

            try
            {
                Component layer = AddOrGetComponent(camera.gameObject, _postProcessLayerType);

                if (layer == null)
                {
                    Debug.LogWarning("[OfCourseIStillLoveYou]: Failed to add PostProcessLayer - camera will work without TUFX");
                    return;
                }

                var resources = _resourcesProperty?.GetValue(null);
                if (resources != null && _initMethod != null)
                {
                    _initMethod.Invoke(layer, new[] { resources });
                }
                else
                {
                    Debug.LogWarning("[OfCourseIStillLoveYou]: TUFX resources not found - removing PostProcessLayer");
                    UnityEngine.Object.Destroy(layer);
                    return;
                }

                if (_volumeLayerField != null)
                {
                    LayerMask allLayers = ~0;
                    _volumeLayerField.SetValue(layer, allLayers);
                }

                Component volume = AddOrGetComponent(camera.gameObject, _postProcessVolumeType);

                if (volume == null)
                {
                    Debug.LogWarning("[OfCourseIStillLoveYou]: Failed to add PostProcessVolume - removing PostProcessLayer");
                    UnityEngine.Object.Destroy(layer);
                    return;
                }

                if (_isGlobalField != null)
                {
                    _isGlobalField.SetValue(volume, true);
                }

                if (_priorityField != null)
                {
                    _priorityField.SetValue(volume, 100);
                }

                Debug.Log($"[OfCourseIStillLoveYou]: Successfully added TUFX post-processing to camera {camera.name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OfCourseIStillLoveYou]: Failed to add TUFX post-processing: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    var layer = camera.gameObject.GetComponent(_postProcessLayerType);
                    if (layer != null) UnityEngine.Object.Destroy(layer);

                    var volume = camera.gameObject.GetComponent(_postProcessVolumeType);
                    if (volume != null) UnityEngine.Object.Destroy(volume);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        private static Component AddOrGetComponent(GameObject gameObject, Type componentType)
        {
            if (_addOrGetComponentMethod != null)
            {
                try
                {
                    return (Component)_addOrGetComponentMethod.Invoke(null, new object[] { gameObject, componentType });
                }
                catch
                {
                    // Fall through to manual implementation
                }
            }

            // Manual fallback implementation
            var existing = gameObject.GetComponent(componentType);
            if (existing != null)
                return existing;

            return gameObject.AddComponent(componentType);
        }
    }
}