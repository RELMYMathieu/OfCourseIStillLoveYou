using HullcamVDS;
using System.Reflection;
using UnityEngine;
using static HullcamVDS.MovieTimeFilter;
using static KSP.UI.Screens.RDArchivesController;

namespace OfCourseIStillLoveYou
{
    public class CameraFilterNightVision : HullcamVDS.CameraFilterNightVision
    {
        public override bool Activate() { return true; }

        public override void Deactivate() { }

        public override void LateUpdate() { }
    }

    public class MovieTimeFilterWrapper : MonoBehaviour
    {

        public enum eFilterType { Flight, Map, Centre, TrackingStation };

        protected static eFilterType currentMode;

        private string moduleName = "";
        private HullcamVDS.CameraFilter cameraFilter = null;
        private HullcamVDS.CameraFilter.eCameraMode cameraMode;
        private eFilterType filterType;

        private float brightness = 1f;
        private float contrast = 2f;

        private float brightnessFactorFlightMode = 0.5f;
        private float contrastFactorFlightMode = 0.75f;

        private float brightnessFactorMapMode = 0.25f;
        private float contrastFactorMapMode = 0.75f;

        private bool title = true;
        private string titleFile = "dockingdisplay.png";
        private Texture2D titleTexture = null;

        private Material _shader = null;

        //private Rect guiRect = new Rect(100, 100, 400, 200);

        //public void OnGUI()
        //{
        //    guiRect = GUI.Window(987654, guiRect, DrawWindow, "Camera Filter");
        //}

        //private void DrawWindow(int id)
        //{
        //    GUILayout.BeginVertical();

        //    // --- Brightness ---
        //    GUILayout.BeginHorizontal();
        //    GUILayout.Label("Brightness", GUILayout.Width(80));

        //    brightness = GUILayout.HorizontalSlider(brightness, 0f, 2f, GUILayout.Width(200));

        //    string bStr = GUILayout.TextField(brightness.ToString("0.00"), GUILayout.Width(50));
        //    if (float.TryParse(bStr, out float bVal))
        //        brightness = Mathf.Clamp(bVal, 0f, 2f);

        //    GUILayout.EndHorizontal();

        //    // --- Contrast ---
        //    GUILayout.BeginHorizontal();
        //    GUILayout.Label("Contrast", GUILayout.Width(80));

        //    contrast = GUILayout.HorizontalSlider(contrast, 0f, 4f, GUILayout.Width(200));

        //    string cStr = GUILayout.TextField(contrast.ToString("0.00"), GUILayout.Width(50));
        //    if (float.TryParse(cStr, out float cVal))
        //        contrast = Mathf.Clamp(cVal, 0f, 4f);

        //    GUILayout.EndHorizontal();

        //    GUILayout.EndVertical();

        //    GUI.DragWindow();
        //}

        public MovieTimeFilterWrapper() { }

        private Material GetFilterShader(HullcamVDS.CameraFilter filter)
        {
            var shaderField = filter.GetType().GetField("mtShader", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (shaderField != null)
            {
                // Debug.Log("[OCISLY-CameraFilter] Get access to shader value");
                return (Material)shaderField.GetValue(filter);
            }
            else
            {
                // Debug.Log("[OCISLY-CameraFilter] Cannot get access to shader value");
                return null;
            }
        }

        private HullcamVDS.CameraFilter CreateFilter(HullcamVDS.CameraFilter.eCameraMode mode)
        {
            HullcamVDS.CameraFilter newFilter = null;

            if (mode == CameraFilter.eCameraMode.NightVision)
            {
                newFilter = new CameraFilterNightVision();
            }
            else
            {
                newFilter = HullcamVDS.CameraFilter.CreateFilter(mode);
                _shader = GetFilterShader(newFilter);
            }

            return newFilter;
        }

        public void Initialize(string module, eFilterType filtType, bool initializeCamera = true)
        {
            moduleName = module;
            filterType = filtType;


            if (initializeCamera)
            {
                cameraFilter = CreateFilter(cameraMode);
                cameraFilter.Activate();
            }
            currentMode = (filterType == eFilterType.Map ? eFilterType.Flight : filterType);

            if (titleFile != "")
                titleTexture = HullcamVDS.CameraFilter.LoadTextureFile(titleFile);
        }

        public void SetMode(HullcamVDS.CameraFilter.eCameraMode mode)
        {
            if (mode != cameraMode)
            {
                HullcamVDS.CameraFilter newFilter = CreateFilter(mode);
                if (newFilter != null && newFilter.Activate())
                {
                    if (cameraFilter != null)
                    {
                        cameraFilter.Save(moduleName);
                        cameraFilter.Deactivate();
                    }
                    cameraFilter = newFilter;
                    cameraFilter.Load(moduleName);
                    cameraMode = mode;
                }
            }
        }
        public void ToggleTitleMode()
        {
            title = !title;
        }

        public void RefreshTitleTexture()
        {
            if (titleTexture != null)
                MonoBehaviour.Destroy(titleTexture);
            titleTexture = null;
            if (titleFile != "")
                titleTexture = HullcamVDS.CameraFilter.LoadTextureFile(titleFile);
        }

        public HullcamVDS.CameraFilter GetFilter()
        {
            return cameraFilter;
        }

        public void SetFilter(HullcamVDS.CameraFilter filter)
        {
            cameraFilter = filter;
        }

        public HullcamVDS.CameraFilter.eCameraMode GetMode()
        {
            return cameraMode;
        }

        public void Update()
        {

        }
        public void LateUpdate()
        {
            if (cameraFilter != null)
                cameraFilter.LateUpdate();
        }

        private void UpdateFilterBrightnessViaShader()
        {
            if (_shader != null)
            {
                float currentBrightness = _shader.GetFloat("_Brightness");
                // Debug.Log($"[OCISLY-CameraFilter] Current brightness value {currentBrightness}");

                float newBrightness = currentBrightness;

                if (MapView.MapIsEnabled)
                {
                    newBrightness = Mathf.Clamp(currentBrightness * brightnessFactorMapMode, 0f, 2f);
                }
                else
                {
                    newBrightness = Mathf.Clamp(currentBrightness * brightnessFactorFlightMode, 0f, 2f);
                }

                // Debug.Log($"[OCISLY-CameraFilter] New brightness value {newBrightness}");
                _shader.SetFloat("_Brightness", newBrightness);
            }
            else
            {
                // Debug.Log("[OCISLY-CameraFilter] Cannot get access to shader value");
            }
        }

        private void UpdateFilterContrastViaShader()
        {
            if (_shader != null)
            {
                float currentContrast = _shader.GetFloat("_Contrast");
                // Debug.Log($"[OCISLY-CameraFilter] Current contrast value {currentContrast}");

                float newContrast = currentContrast;

                if (MapView.MapIsEnabled)
                {
                    newContrast = Mathf.Clamp(currentContrast * contrastFactorMapMode, 0f, 4f);
                }
                else
                {
                    newContrast = Mathf.Clamp(currentContrast * contrastFactorFlightMode, 0f, 4f);
                }

                // Debug.Log($"[OCISLY-CameraFilter] New contrast value {newContrast}");
                _shader.SetFloat("_Contrast", newContrast);
            }
            else
            {
                // Debug.Log("[OCISLY-CameraFilter] Cannot get access to shader value");
            }
        }

        private void OnRenderImage(RenderTexture source, RenderTexture target)
        {
            if (cameraFilter != null)
            {
                cameraFilter.RenderTitlePage(title, titleTexture);
                cameraFilter.RenderImageWithFilter(source, target);

                if (_shader != null)
                {
                    UpdateFilterBrightnessViaShader();
                    UpdateFilterContrastViaShader();

                    Graphics.Blit(source, target, _shader);
                }
            }
            else
            {
                Graphics.Blit(source, target);
            }
        }

        public static eFilterType LoadedScene()
        {
            if (currentMode == eFilterType.Flight && !MapView.MapIsEnabled)
                return eFilterType.Flight;
            else if (currentMode == eFilterType.Flight && MapView.MapIsEnabled)
                return eFilterType.Map;
            return currentMode;
        }
    }
}
