using System.Collections.Generic;
using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    public class DisappearingArrowsVisualController : MonoBehaviour
    {
        // How long before the hit arrows/dots should disappear
        public float hideLeadTime = 0.6f;

        private readonly List<MeshRenderer> _arrowRenderers = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _circleRenderers = new List<MeshRenderer>();

        private float _noteHitTime;
        private bool _initialized;
        private bool _overlaysHidden;

        public static AudioTimeSyncController Audio { get; set; }

        public void Initialize(GameNoteController gameNote, float noteHitTime)
        {
            _noteHitTime = noteHitTime;
            CacheRenderers(gameNote);
            _initialized = true;
            enabled = true;
        }

        private void CacheRenderers(GameNoteController gameNote)
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
            if (!_initialized)
                return;

            // If the effect window ended, restore overlays and stop running
            if (!DisappearingArrowsManager.DisappearingActive)
            {
                SetOverlaysVisible(true);
                enabled = false;
                return;
            }

            // Lazy-bind AudioTimeSyncController once
            if (Audio == null)
            {
                var audios = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>();
                if (audios != null && audios.Length > 0)
                {
                    Audio = audios[0];
                    Plugin.Log.Info("DisappearingArrowsVisualController: bound AudioTimeSyncController from Update()");
                }
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

        private void OnDisable()
        {
            // Safety when pooled objects are disabled: restore overlays
            SetOverlaysVisible(true);
        }
    }
}
