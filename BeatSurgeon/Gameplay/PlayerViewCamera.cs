using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Resolves the player's actual view camera when Camera.main is missing or
    /// displaced by Camera2 / avatar / spectator stacks. Main-thread only.
    /// </summary>
    public static class PlayerViewCamera
    {
        private const float RescanIntervalSeconds = 1f;

        private static readonly string[] ExcludeNameFragments =
        {
            "Cam2_",
            "Cam2",
            "Avatar",
            "Spectator",
            "Desktop",
            "Mirror",
            "Preview",
            "Recorder",
            "Viewport_THIS_IS_NORMAL",
        };

        private static Camera _cached;
        private static float _nextRescanUnscaledTime;
        private static bool _lastScanFoundNothing;
        private static Camera[] _cameraBuffer = new Camera[16];

        public static bool TryGet(out Camera camera)
        {
            if (IsUsable(_cached))
            {
                camera = _cached;
                return true;
            }

            // Cache invalid: rescan immediately. Throttle only when the last scan found nothing.
            float now = Time.unscaledTime;
            if (_lastScanFoundNothing && now < _nextRescanUnscaledTime)
            {
                camera = null;
                return false;
            }

            camera = Resolve();
            _cached = camera;
            _lastScanFoundNothing = camera == null;
            _nextRescanUnscaledTime = now + RescanIntervalSeconds;
            return camera != null;
        }

        public static bool TryGetTransform(out Transform transform)
        {
            if (TryGet(out Camera camera))
            {
                transform = camera.transform;
                return true;
            }

            transform = null;
            return false;
        }

        private static Camera Resolve()
        {
            Camera main = Camera.main;
            if (IsUsable(main) && !IsExcluded(main))
            {
                return main;
            }

            int count = Camera.allCamerasCount;
            if (count <= 0)
            {
                return null;
            }

            if (_cameraBuffer.Length < count)
            {
                _cameraBuffer = new Camera[count];
            }

            int filled = Camera.GetAllCameras(_cameraBuffer);
            Camera bestStereo = null;
            Camera bestMainTag = null;
            Camera bestAny = null;

            for (int i = 0; i < filled; i++)
            {
                Camera candidate = _cameraBuffer[i];
                if (!IsUsable(candidate) || IsExcluded(candidate))
                {
                    continue;
                }

                if (bestAny == null)
                {
                    bestAny = candidate;
                }

                if (candidate.stereoTargetEye != StereoTargetEyeMask.None && bestStereo == null)
                {
                    bestStereo = candidate;
                }

                if (candidate.CompareTag("MainCamera") && bestMainTag == null)
                {
                    bestMainTag = candidate;
                }
            }

            if (bestStereo != null)
            {
                return bestStereo;
            }

            if (bestMainTag != null)
            {
                return bestMainTag;
            }

            return bestAny;
        }

        private static bool IsUsable(Camera camera)
        {
            return camera != null
                && camera.enabled
                && camera.gameObject != null
                && camera.gameObject.activeInHierarchy;
        }

        private static bool IsExcluded(Camera camera)
        {
            string name = camera.name;
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < ExcludeNameFragments.Length; i++)
            {
                if (name.IndexOf(ExcludeNameFragments[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
