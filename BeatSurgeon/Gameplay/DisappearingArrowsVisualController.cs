using System.Collections.Generic;
using BeatSurgeon.Twitch;
using UnityEngine;
using Unity.Profiling;

namespace BeatSurgeon.Gameplay
{
    public class DisappearingArrowsVisualController : MonoBehaviour
    {
        private static readonly ProfilerMarker UpdateProfiler = new ProfilerMarker("BeatSurgeon.DisappearingArrowsVisualController.Update");
        private static float _nextAudioSearchAt;

        // How long before the hit time the arrows/dots should disappear (driven by Plugin.Settings)
        private float hideLeadTime = 0.6f;

        private readonly List<MeshRenderer> _arrowRenderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _circleRenderers = new List<MeshRenderer>();

        private float _noteHitTime;
        private bool _initialized;
        private bool _overlaysHidden;
        // Prevents OnDisable from restoring arrow visibility when we intentionally
        // kept arrows hidden for a note that hasn't been hit yet after the effect ended.
        private bool _suppressOnDisableRestore;

        public static AudioTimeSyncController Audio { get; set; }

        public void Initialize(NoteControllerBase gameNote, float noteHitTime)
        {
            _noteHitTime = noteHitTime;
            hideLeadTime = EntitlementsState.HasVisualsAccess
                ? (Plugin.Settings?.DisappearFadeDuration ?? 0.3f)
                : 0.3f;
            _suppressOnDisableRestore = false;
            CacheRenderers(gameNote);
            _initialized = true;
            enabled = true;
        }

        private void CacheRenderers(NoteControllerBase gameNote)
        {

            _arrowRenderers.Clear();
            _circleRenderers.Clear();

            var allRenderers = gameNote.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in allRenderers)
            {
                if (mr == null) continue;

                string name = mr.name ?? string.Empty;
                if (name.Contains("Arrow"))
                    _arrowRenderers.Add(mr);
                else if (name.Contains("Circle"))
                    _circleRenderers.Add(mr);
            }
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
            using (UpdateProfiler.Auto())
            {
                if (!_initialized)
                    return;

                // If the effect window ended, restore overlays and stop running
                if (!DisappearingArrowsManager.DisappearingActive)
                {
                    // If this note's arrows are already hidden and it hasn't been hit yet,
                    // keep the arrows hidden rather than flashing them back on briefly.
                    // The note will be recycled naturally when it is hit or missed.
                    if (_overlaysHidden && Audio != null)
                    {
                        float timeUntilHit = _noteHitTime - Audio.songTime;
                        if (timeUntilHit > 0f)
                        {
                            _suppressOnDisableRestore = true;
                            enabled = false;
                            return;
                        }
                    }

                    SetOverlaysVisible(true);
                    enabled = false;
                    return;
                }

                // Shared throttled bind: avoid one expensive search per-note per-frame.
                if (Audio == null && Time.unscaledTime >= _nextAudioSearchAt)
                {
                    _nextAudioSearchAt = Time.unscaledTime + 0.5f;
                    Audio = Object.FindObjectOfType<AudioTimeSyncController>();
                }

                if (Audio == null)
                    return;

                float songTime = Audio.songTime;
                float remaining = _noteHitTime - songTime;
                bool shouldHide = remaining <= hideLeadTime;

                if (!_overlaysHidden && shouldHide)
                {
                    // Near the hit: hide arrows and dots, leaving the plain cube
                    SetOverlaysVisible(false);
                }
                else if (_overlaysHidden && !shouldHide)
                {
                    // Early in jump / pooled reuse while effect is active: show overlays again
                    SetOverlaysVisible(true);
                }
            }
        }

        private void OnDisable()
        {
            // Safety when pooled objects are disabled: restore overlays.
            // Skip if we deliberately kept arrows hidden for an in-flight note
            // so they don't flash back on right before the note is hit.
            if (_suppressOnDisableRestore)
            {
                _suppressOnDisableRestore = false;
                return;
            }
            SetOverlaysVisible(true);
        }
    }
}
