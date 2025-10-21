using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HullcamVDS;
using OfCourseIStillLoveYou.Client;
using UnityEngine;

namespace OfCourseIStillLoveYou
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class Core : MonoBehaviour
    {
        public static Dictionary<int, TrackingCamera> TrackedCameras = new Dictionary<int, TrackingCamera>();

        private static bool _lastDebugModeState = false;

        private void Awake()
        {
            GrpcClient.ConnectToServer(Settings.EndPoint, Settings.Port);
        }

        public static void Log(string message)
        {
            Debug.Log($"[OfCourseIStillLoveYou]: {message}");
        }

        public static List<MuMechModuleHullCamera> GetAllTrackingCameras()
        {
            List<MuMechModuleHullCamera> result = new List<MuMechModuleHullCamera>();

            if (!FlightGlobals.ready) return result;

            foreach (var vessel in FlightGlobals.VesselsLoaded)
            {
                result.AddRange(vessel.FindPartModulesImplementing<MuMechModuleHullCamera>());
            }

            return result;
        }

        void Update()
        {
            ToggleRender();
        }

        void LateUpdate()
        {
            Refresh();
            SyncDebugMode();
        }

        private static void SyncDebugMode()
        {
            bool currentDebugMode = DeferredWrapper.IsDebugModeEnabled();

            if (currentDebugMode != _lastDebugModeState)
            {
                Log($"Debug mode changed to {currentDebugMode}, syncing all cameras");

                foreach (var trackedCamera in TrackedCameras.Values)
                {
                    if (trackedCamera.Enabled)
                    {
                        trackedCamera.SyncDebugMode(currentDebugMode);
                    }
                }

                _lastDebugModeState = currentDebugMode;
            }
        }

        private void Refresh()
        {
            foreach (var trackedCamerasValue in TrackedCameras.Values.Where(trackedCamerasValue => trackedCamerasValue.Enabled))
            {
                if (!trackedCamerasValue.OddFrames) continue;

                trackedCamerasValue.CalculateSpeedAltitude();
                trackedCamerasValue.SendCameraImage();

                trackedCamerasValue.UpdateFireflyEffects();
            }
        }

        private void ToggleRender()
        {
            foreach (var trackedCamerasValue in TrackedCameras.Values.Where(trackedCamerasValue => trackedCamerasValue.Enabled))
            {
                trackedCamerasValue.ToogleCameras();
            }
        }
    }
}
