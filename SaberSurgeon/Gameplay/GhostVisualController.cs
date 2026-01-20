using System.Collections.Generic;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    public class GhostVisualController : MonoBehaviour
    {
        // How long before hit arrows/dots should disappear
        public float hideLeadTime = 0.15f;

        private readonly List<MeshRenderer> _cubeRenderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _arrowRenderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _circleRenderers = new List<MeshRenderer>();

        private float _noteHitTime;
        private bool _initialized;
        private bool _overlaysHidden;

        public static AudioTimeSyncController Audio { get; set; }

        public void Initialize(NoteControllerBase gameNote, float noteHitTime)
        {
            _noteHitTime = noteHitTime;
            CacheRenderers(gameNote);

            // From the start of the jump, cubes are never visible
            HideCubes();

            _initialized = true;

            enabled = true;
        }

        private void CacheRenderers(NoteControllerBase gameNote)
        {
            _cubeRenderers.Clear();
            _arrowRenderers.Clear();
            _circleRenderers.Clear();

            var allRenderers = gameNote.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in allRenderers)
            {
                if (mr == null)
                    continue;

                string name = mr.name ?? string.Empty;
                string mat = mr.sharedMaterial?.name ?? string.Empty;

                bool isCube = name == "NoteCube" || mat.StartsWith("NoteHD");
                bool isArrow = name.Contains("Arrow");
                bool isCircle = name.Contains("Circle");

                if (isCube) _cubeRenderers.Add(mr);
                else if (isArrow) _arrowRenderers.Add(mr);
                else if (isCircle) _circleRenderers.Add(mr);
            }
        }

        private void HideCubes()
        {
            foreach (var mr in _cubeRenderers)
                if (mr != null) mr.enabled = false;
        }

        private void ShowCubes()
        {
            foreach (var mr in _cubeRenderers)
                if (mr != null) mr.enabled = true;
        }

        private void SetOverlaysVisible(bool visible)
        {
            foreach (var mr in _arrowRenderers)
                if (mr != null) mr.enabled = visible;

            foreach (var mr in _circleRenderers)
                if (mr != null) mr.enabled = visible;

            _overlaysHidden = !visible;
        }

        private void Update()
        {
            if (!_initialized)
                return;

            // If ghost was turned off, restore everything and stop running
            if (!GhostNotesManager.GhostActive)
            {
                ShowCubes();
                SetOverlaysVisible(true);
                enabled = false;
                return;
            }

            // Lazy-bind AudioTimeSyncController once we are in a level
            if (Audio == null)
            {
                var audios = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>();
                if (audios != null && audios.Length > 0)
                {
                    Audio = audios[0];
                    Plugin.Log.Info("GhostVisualController: bound AudioTimeSyncController from Update()");
                }
            }

            if (Audio == null)
                return;

            float songTime = Audio.songTime;
            float remaining = _noteHitTime - songTime;

            bool shouldHideOverlays = remaining <= hideLeadTime;

            if (!_overlaysHidden && shouldHideOverlays)
            {
                // Near the hit: hide arrows and dots too
                SetOverlaysVisible(false);
            }
            else if (_overlaysHidden && !shouldHideOverlays)
            {
                // Early in jump / pool reuse while ghost active: show overlays again
                SetOverlaysVisible(true);
            }

            // Cubes stay hidden the whole time while ghost is active
            HideCubes();
        }

        private void OnDisable()
        {
            // Safety when pooled objects are disabled: restore visuals
            ShowCubes();
            SetOverlaysVisible(true);
        }
    }
}
