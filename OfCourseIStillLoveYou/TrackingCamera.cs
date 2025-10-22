using System;
using System.Linq;
using System.Threading.Tasks;
using HullcamVDS;
using OfCourseIStillLoveYou.Client;
using UnityEngine;
using UnityEngine.Rendering;

namespace OfCourseIStillLoveYou
{
    public class TrackingCamera
    {
        private const float ButtonHeight = 18;
        private const float Gap = 2;
        private const float Line = ButtonHeight + Gap;
        private const float ButtonWidth = 3 * ButtonHeight + 4 * Gap;
        private const float MaxCameraSize = 360;
        private const string Altitude = "ALTITUDE: ", Km = " KM", Speed = "SPEED: ", Kmh = " KM/H";

        private static readonly float controlsStartY = 22;
        private static readonly Font TelemetryFont = Font.CreateDynamicFontFromOSFont("Bahnschrift Semibold", 17);

        private static readonly GUIStyle ButtonStyle = new GUIStyle(HighLogic.Skin.button)
        { fontSize = 10, wordWrap = true };


        private static readonly GUIStyle TelemetryGuiStyle = new GUIStyle()
        { alignment = TextAnchor.MiddleCenter, normal = new GUIStyleState() { textColor = Color.white }, fontStyle = FontStyle.Bold, font = TelemetryFont };


        public static Texture2D ResizeTexture =
            GameDatabase.Instance.GetTexture("OfCourseIStillLoveYou/Textures/" + "resizeSquare", false);

        private readonly MuMechModuleHullCamera _hullcamera;


        private float _initialCamImageWidthSize = 360;
        private float _initialCamImageHeightSize = 360;
        private float _adjCamImageWidthSize = 360;
        private float _adjCamImageHeightSize = 360;

        private readonly Camera[] _cameras = new Camera[3];
        private float _windowHeight;

        private Rect _windowRect;
        private float _windowWidth;
        public RenderTexture TargetCamRenderTexture;
        private readonly Texture2D _texture2D = new Texture2D(Settings.Width, Settings.Height, TextureFormat.ARGB32, false);

        public bool OddFrames;
        private byte[] _jpgTexture;

        private bool _lastDebugModeState = false;
        private bool _diagnosticRun = false;

        public void SyncDebugMode(bool debugModeEnabled)
        {
            foreach (var camera in _cameras)
            {
                if (camera != null)
                {
                    if (!debugModeEnabled)
                    {
                        DeferredWrapper.ForceRemoveDebugMode(camera);
                    }
                    else
                    {
                        DeferredWrapper.ToggleCameraDebugMode(camera, debugModeEnabled);
                    }
                }
            }

            _lastDebugModeState = debugModeEnabled;
        }

        public void ToogleCameras()
        {
            OddFrames = !OddFrames;
            foreach (var camera in this._cameras)
            {
                if (camera != null)
                {
                    camera.enabled = OddFrames;
                }
            }
        }

        public void SendCameraImage()
        {
            if (!OddFrames) return;

            foreach (var camera in _cameras)
            {
                if (camera != null && camera.enabled)
                {
                    ScattererWrapper.ForceEnableScattererComponents(camera);
                    ScattererOceanHelper.UpdateOceanForCamera(camera);
                }
            }

            if (Time.frameCount % 300 == 0)
            {
                LogDetailedCameraStatus();
                Debug.Log("[OCISLY] ===== Periodic Status Check =====");
                LogCommandBufferStatus();
            }

            if (!StreamingEnabled) return;

            Graphics.CopyTexture(TargetCamRenderTexture, _texture2D);

            AsyncGPUReadback.Request(_texture2D, 0,
                request =>
                {
                    Task.Run(() => _texture2D.LoadRawTextureData(request.GetData<byte>()))
                        .ContinueWith(previous => _jpgTexture = _texture2D.EncodeToJPG())
                        .ContinueWith(previous =>
                            GrpcClient.SendCameraTextureAsync(new CameraData
                            {
                                CameraId = Id.ToString(),
                                CameraName = Name,
                                Speed = SpeedString,
                                Altitude = AltitudeString,
                                Texture = _jpgTexture
                            }));
                }
            );
        }

        public TrackingCamera(int id, MuMechModuleHullCamera hullcamera)
        {
            Id = id;
            _hullcamera = hullcamera;

            TargetCamRenderTexture = new RenderTexture(Settings.Width, Settings.Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1
            };

            TargetCamRenderTexture.Create();

            CalculateInitialSize();

            _windowWidth = _adjCamImageWidthSize + 3 * ButtonHeight + 16 + 2 * Gap;
            _windowHeight = _adjCamImageHeightSize + 23;
            _windowRect = new Rect(Screen.width - _windowWidth, Screen.height - _windowHeight, _windowWidth,
                _windowHeight);
            SetCameras();

            Enabled = true;
        }

        private void CalculateInitialSize()
        {
            if (Settings.Width > Settings.Height)
            {
                _adjCamImageHeightSize = Settings.Height * MaxCameraSize / Settings.Width;
                _initialCamImageHeightSize = _adjCamImageHeightSize;
                _adjCamImageWidthSize = 360;


            }
            else
            {
                _adjCamImageWidthSize = Settings.Width * MaxCameraSize / Settings.Height;
                _initialCamImageWidthSize = _adjCamImageWidthSize;
                _adjCamImageHeightSize = 360;
            }

            Debug.Log($"OCISLY:_adjCamImageHeightSize = {_adjCamImageHeightSize} _adjCamImageWidthSize = {_adjCamImageWidthSize}");
        }

        public string Name { get; private set; }

        public Vessel Vessel => _hullcamera?.vessel;

        public int Id { get; }

        public bool Enabled { get; set; }

        public float TargetWindowScaleMax { get; set; } = 3f;

        public float TargetWindowScaleMin { get; set; } = 0.5f;


        public bool ResizingWindow { get; set; }

        public float TargetWindowScale { get; set; } = 1;
        public string AltitudeString { get; private set; }
        public string SpeedString { get; private set; }
        public bool StreamingEnabled { get; private set; }

        private Camera FindCamera(string cameraName)
        {
            foreach (var cam in Camera.allCameras)
                if (cam.name == cameraName)
                    return cam;

            Debug.Log("Couldn't find " + cameraName);
            return null;
        }

        private void SetCameras()
        {
            var cam1Obj = new GameObject("OCISLY_NearCamera");
            var partNearCamera = cam1Obj.AddComponent<Camera>();

            var mainCamera = Camera.allCameras.FirstOrDefault(cam => cam.name == "Camera 00");
            partNearCamera.CopyFrom(mainCamera);


            partNearCamera.name = "jrNear";
            cam1Obj.name = "OCISLY_NearCamera";
            partNearCamera.transform.parent = _hullcamera.cameraTransformName.Length <= 0
                ? _hullcamera.part.transform
                : _hullcamera.part.FindModelTransform(_hullcamera.cameraTransformName);
            partNearCamera.transform.localRotation =
                Quaternion.LookRotation(_hullcamera.cameraForward, _hullcamera.cameraUp);
            partNearCamera.transform.localPosition = _hullcamera.cameraPosition;
            partNearCamera.fieldOfView = 50;
            partNearCamera.targetTexture = TargetCamRenderTexture;
            partNearCamera.allowHDR = true;
            partNearCamera.allowMSAA = true;
            partNearCamera.enabled = true;

            Debug.Log($"[OCISLY] Created near camera: {partNearCamera.name} (GameObject: {cam1Obj.name})");

            _cameras[0] = partNearCamera;
            _cameras[0].allowHDR = true;
            cam1Obj.AddComponent<CanvasHack>();

            //Deferred rendering
            DeferredWrapper.EnableDeferredRendering(partNearCamera);
            DeferredWrapper.SyncDebugMode(partNearCamera);

            //TUFX
            AddTufxPostProcessing();

            //Parallax for near camera
            ParallaxWrapper.ApplyParallaxToCamera(partNearCamera, mainCamera);

            var cam2Obj = new GameObject("OCISLY_ScaledCamera");
            var partScaledCamera = cam2Obj.AddComponent<Camera>();
            var mainSkyCam = FindCamera("Camera ScaledSpace");

            partScaledCamera.CopyFrom(mainSkyCam);
            partScaledCamera.name = "jrScaled";

            partScaledCamera.transform.parent = mainSkyCam.transform;
            partScaledCamera.transform.localRotation = Quaternion.identity;
            partScaledCamera.transform.localPosition = Vector3.zero;
            partScaledCamera.transform.localScale = Vector3.one;
            partScaledCamera.fieldOfView = 50;
            partScaledCamera.targetTexture = TargetCamRenderTexture;
            partScaledCamera.allowHDR = true;
            partScaledCamera.allowMSAA = true;
            partScaledCamera.enabled = true;
            _cameras[1] = partScaledCamera;

            DeferredWrapper.EnableDeferredRendering(partScaledCamera);
            DeferredWrapper.SyncDebugMode(partScaledCamera);

            ParallaxWrapper.ApplyParallaxToCamera(partScaledCamera, mainSkyCam);

            var camRotator = cam2Obj.AddComponent<TgpCamRotator>();
            camRotator.NearCamera = partNearCamera;
            cam2Obj.AddComponent<CanvasHack>();

            //galaxy camera
            var galaxyCamObj = new GameObject("OCISLY_GalaxyCamera");
            var galaxyCam = galaxyCamObj.AddComponent<Camera>();
            var mainGalaxyCam = FindCamera("GalaxyCamera");

            galaxyCam.CopyFrom(mainGalaxyCam);
            galaxyCam.name = "jrGalaxy";
            galaxyCam.transform.parent = mainGalaxyCam.transform;
            galaxyCam.transform.position = Vector3.zero;
            galaxyCam.transform.localRotation = Quaternion.identity;
            galaxyCam.transform.localScale = Vector3.one;
            galaxyCam.fieldOfView = 50;
            galaxyCam.targetTexture = TargetCamRenderTexture;
            galaxyCam.allowHDR = true;
            galaxyCam.allowMSAA = true;
            galaxyCam.enabled = true;
            _cameras[2] = galaxyCam;

            DeferredWrapper.EnableDeferredRendering(galaxyCam);
            DeferredWrapper.SyncDebugMode(galaxyCam);

            ParallaxWrapper.ApplyParallaxToCamera(galaxyCam, mainGalaxyCam);

            //Scatterer for all cameras
            ScattererWrapper.ApplyScattererToCamera(partNearCamera);
            ScattererWrapper.ApplyScattererToCamera(partScaledCamera);
            ScattererWrapper.ApplyScattererToCamera(galaxyCam);

            var camRotatorgalaxy = galaxyCamObj.AddComponent<TgpCamRotator>();
            camRotatorgalaxy.NearCamera = partNearCamera;
            galaxyCamObj.AddComponent<CanvasHack>();

            foreach (var t in _cameras)
                t.enabled = false;

            _lastDebugModeState = DeferredWrapper.IsDebugModeEnabled();

            Debug.Log("[OCISLY] === Main Camera Components ===");
            foreach (var component in mainCamera.GetComponents<Component>())
            {
                Debug.Log($"[OCISLY] - {component.GetType().FullName}");
            }

            Debug.Log("[OCISLY] === Main Camera CommandBuffers ===");
            foreach (CameraEvent evt in System.Enum.GetValues(typeof(CameraEvent)))
            {
                var buffers = mainCamera.GetCommandBuffers(evt);
                if (buffers.Length > 0)
                {
                    foreach (var buffer in buffers)
                    {
                        Debug.Log($"[OCISLY] - {evt}: {buffer.name}");
                    }
                }
            }

            //EVE for all cameras
            EVEWrapper.ApplyEVEToCamera(partNearCamera, mainCamera);
            EVEWrapper.ApplyEVEToCamera(partScaledCamera, mainSkyCam);
            EVEWrapper.ApplyEVEToCamera(galaxyCam, mainGalaxyCam);

            Debug.Log("[OCISLY] Verifying camera names:");
            Debug.Log($"[OCISLY] - _cameras[0].name = {_cameras[0].name}");
            Debug.Log($"[OCISLY] - _cameras[1].name = {_cameras[1].name}");
            Debug.Log($"[OCISLY] - _cameras[2].name = {_cameras[2].name}");

            if (ScattererWrapper.IsScattererAvailable)
            {
                ScattererOceanHelper.FindOceanNode(_hullcamera.vessel.mainBody.name);
            }
        }

        private void AddTufxPostProcessing()
        {
            try
            {
                TufxWrapper.AddPostProcessing(_cameras[0]);
            }
            catch
            {
                // ignored
            }
        }

        public void CreateGui()
        {
            if (!Enabled) return;

            if (_hullcamera == null || _hullcamera.vessel == null)
            {
                Disable();
                return;
            }

            Name = _hullcamera.vessel.GetDisplayName() + "." + _hullcamera.cameraName;

            _windowRect = GUI.Window(Id, _windowRect, WindowTargetCam, Name);

            if (Time.timeSinceLevelLoad > 2f && !_diagnosticRun)
            {
                _diagnosticRun = true;
                LogDetailedCameraStatus();
            }
        }

        public void CheckIfResizing()
        {
            if (!Enabled) return;

            if (Event.current.type == EventType.MouseUp)
                if (ResizingWindow)
                    ResizingWindow = false;
        }

        private void WindowTargetCam(int windowId)
        {
            if (!Enabled) return;

            _adjCamImageWidthSize = _initialCamImageWidthSize * TargetWindowScale;
            _adjCamImageHeightSize = _initialCamImageHeightSize * TargetWindowScale;

            GUI.DragWindow(new Rect(0, 0, _windowHeight - 18, 30));
            if (GUI.Button(new Rect(_windowWidth - 18, 2, 20, 16), "X", GUI.skin.button))
            {
                Disable();

                return;
            }

            var imageRect = DrawTexture();

            // Right side control buttons
            DrawSideControlButtons(imageRect);

            DrawTelemetry(imageRect);


            //resizing
            var resizeRect =
                new Rect(_windowWidth - 18, _windowHeight - 18, 16, 16);


            GUI.DrawTexture(resizeRect, ResizeTexture, ScaleMode.StretchToFill, true);

            if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && imageRect.Contains(Event.current.mousePosition))
            {
                MinimalUi = !MinimalUi;
                ResizeTargetWindow();
            }

            if (Event.current.type == EventType.MouseDown && resizeRect.Contains(Event.current.mousePosition))
                ResizingWindow = true;

            if (Event.current.type == EventType.Repaint && ResizingWindow)
                if (Math.Abs(Mouse.delta.x) > 1 || Math.Abs(Mouse.delta.y) > 0.1f)
                {
                    var diff = Mouse.delta.x + Mouse.delta.y;
                    UpdateTargetScale(diff);
                    ResizeTargetWindow();
                }

            //ResetZoomKeys();
            RepositionWindow(ref _windowRect);
        }

        private Rect DrawTexture()
        {
            var imageRect = new Rect(2, 20, _adjCamImageWidthSize, _adjCamImageHeightSize);


            GUI.DrawTexture(imageRect, TargetCamRenderTexture, ScaleMode.StretchToFill, false);
            return imageRect;
        }

        private void DrawTelemetry(Rect imageRect)
        {
            if (MinimalUi) return;

            var dataStyle = new GUIStyle(TelemetryGuiStyle)
            {
                fontSize = (int)Mathf.Clamp(16 * TargetWindowScale, 9, 17),
            };

            var targetRangeRect = new Rect(imageRect.x,
                _adjCamImageHeightSize * 0.94f - (int)Mathf.Clamp(18 * TargetWindowScale, 9, 18), _adjCamImageWidthSize,
                (int)Mathf.Clamp(18 * TargetWindowScale, 10, 18));


            GUI.Label(targetRangeRect, String.Concat(AltitudeString, Environment.NewLine, SpeedString), dataStyle);
        }

        public bool MinimalUi { get; set; }

        private void DrawSideControlButtons(Rect imageRect)
        {
            if (MinimalUi) return;

            var startX = imageRect.width + 3 * Gap;
            var streamingRect = new Rect(startX, controlsStartY, ButtonWidth, ButtonHeight + Line);

            if (!StreamingEnabled)
            {
                if (GUI.Button(streamingRect, "Enable streaming", ButtonStyle)) StreamingEnabled = true;
            }
            else
            {
                if (GUI.Button(streamingRect, "Disable streaming", ButtonStyle)) StreamingEnabled = false;
            }
        }

        public void CalculateSpeedAltitude()
        {
            var altitudeInKm = (float)Math.Round(_hullcamera.vessel.altitude / 1000f, 1);
            var speed = (int)Math.Round(_hullcamera.vessel.speed * 3.6f, 0);

            AltitudeString = string.Concat(Altitude, altitudeInKm.ToString("0.0"), Km);
            SpeedString = string.Concat(Speed, speed, Kmh);
        }

        private void UpdateTargetScale(float diff)
        {
            var scaleDiff = diff / (_windowRect.width + _windowRect.height) * 100 * .01f;
            TargetWindowScale += Mathf.Abs(scaleDiff) > .01f ? scaleDiff : scaleDiff > 0 ? .01f : -.01f;

            TargetWindowScale += Mathf.Abs(scaleDiff) > .01f ? scaleDiff : scaleDiff > 0 ? .01f : -.01f;
            TargetWindowScale = Mathf.Clamp(TargetWindowScale,
                TargetWindowScaleMin,
                TargetWindowScaleMax);
        }


        private void ResizeTargetWindow()
        {
            if (MinimalUi)
            {
                _windowWidth = _initialCamImageWidthSize * TargetWindowScale + 2 * Gap;
            }
            else
            {
                _windowWidth = _initialCamImageWidthSize * TargetWindowScale + 3 * ButtonHeight + 16 + 2 * Gap;
            }
            _windowHeight = _initialCamImageHeightSize * TargetWindowScale + 23;
            _windowRect = new Rect(_windowRect.x, _windowRect.y, _windowWidth, _windowHeight);
        }

        internal static void RepositionWindow(ref Rect windowPosition)
        {
            // This method uses Gui point system.
            if (windowPosition.x < 0) windowPosition.x = 0;
            if (windowPosition.y < 0) windowPosition.y = 0;

            if (windowPosition.xMax > Screen.width)
                windowPosition.x = Screen.width - windowPosition.width;
            if (windowPosition.yMax > Screen.height)
                windowPosition.y = Screen.height - windowPosition.height;
        }

        public void Disable()
        {
            Enabled = false;
            StreamingEnabled = false;
            this.TargetCamRenderTexture.Release();

            foreach (var camera in _cameras)
            {
                if (camera != null)
                {
                    // Disable Deferred Rendering
                    DeferredWrapper.ForceRemoveDebugMode(camera);
                    DeferredWrapper.DisableDeferredRendering(camera);

                    // Disable Parallax and EVE wrappers
                    ParallaxWrapper.RemoveParallaxFromCamera(camera);
                    EVEWrapper.RemoveEVEFromCamera(camera);

                    // Disable Scatterer wrapper
                    ScattererWrapper.RemoveScattererFromCamera(camera);

                    // Disable Firefly wrapper and cleanup tracking
                    FireflyWrapper.RemoveFireflyFromCamera(camera);
                    FireflyWrapper.CleanupCamera(camera);

                    camera.enabled = false;
                }
            }
        }

        private void LogDetailedCameraStatus()
        {
            if (_cameras == null) return;

            Debug.Log("[OCISLY] ===== Detailed Camera Status Check =====");

            foreach (var cam in _cameras)
            {
                if (cam == null) continue;

                Debug.Log($"[OCISLY] === {cam.name} ===");
                Debug.Log($"[OCISLY] Enabled: {cam.enabled}, Layer 9: {(cam.cullingMask & (1 << 9)) != 0}, Layer 15: {(cam.cullingMask & (1 << 15)) != 0}");

                Debug.Log(ScattererWrapper.GetDiagnosticInfo(cam));
                ScattererWrapper.EnsureOceanRenderingSetup(cam);
            }
        }

        private void LogCommandBufferStatus()
        {
            foreach (var cam in _cameras)
            {
                if (cam == null) continue;

                Debug.Log($"[OCISLY] ===== {cam.name} Active CommandBuffers =====");

                var allEvents = (CameraEvent[])System.Enum.GetValues(typeof(CameraEvent));
                int totalBuffers = 0;

                foreach (var evt in allEvents)
                {
                    var buffers = cam.GetCommandBuffers(evt);
                    if (buffers != null && buffers.Length > 0)
                    {
                        foreach (var buffer in buffers)
                        {
                            totalBuffers++;
                            Debug.Log($"[OCISLY]   {evt}: {buffer.name}");
                        }
                    }
                }

                if (totalBuffers == 0)
                {
                    Debug.Log($"[OCISLY]   NO COMMAND BUFFERS on {cam.name}!");
                }
                else
                {
                    Debug.Log($"[OCISLY]   Total: {totalBuffers} command buffers");
                }
            }
        }

        public void UpdateFireflyEffects()
        {
            foreach (var cam in _cameras)
            {
                if (cam != null)
                {
                    FireflyWrapper.UpdateFireflyForCamera(cam, _hullcamera.vessel);
                }
            }
        }

        public void RenderParallaxScatters()
        {
            // Parallax requires explicit render calls to display scatter objects (grass, rocks, trees)
            // This is called every other frame (when OddFrames is true) from Core.Refresh()
            ParallaxWrapper.RenderParallaxToCustomCameras(_cameras);
        }
    }
}
