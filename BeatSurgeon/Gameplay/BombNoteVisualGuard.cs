using System.Collections.Generic;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Keeps all note body renderers hidden while a bomb visual is attached.
    /// Unlike the old time-limited watchdog, this runs until BombManager clears the bomb.
    /// </summary>
    internal sealed class BombNoteVisualGuard : MonoBehaviour
    {
        private struct TrackedRenderer
        {
            public MeshRenderer Renderer;
            public bool OriginallyEnabled;
        }

        private readonly List<TrackedRenderer> _hiddenRenderers = new List<TrackedRenderer>();
        private Transform _bombVisualRoot;

        public void Init(Transform noteRoot, Transform bombVisualRoot)
        {
            _bombVisualRoot = bombVisualRoot;
            _hiddenRenderers.Clear();

            var renderers = noteRoot.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var mr in renderers)
            {
                if (mr == null)
                {
                    continue;
                }

                if (_bombVisualRoot != null && mr.transform.IsChildOf(_bombVisualRoot))
                {
                    continue;
                }

                if (!ShouldHideRenderer(mr))
                {
                    continue;
                }

                _hiddenRenderers.Add(new TrackedRenderer
                {
                    Renderer = mr,
                    OriginallyEnabled = mr.enabled
                });
                mr.enabled = false;
            }

            enabled = true;
            LogUtils.Debug(() => $"BombNoteVisualGuard: Hiding {_hiddenRenderers.Count} note renderers under {noteRoot.name}");
        }

        private static bool ShouldHideRenderer(MeshRenderer mr)
        {
            string name = mr.name ?? string.Empty;
            string mat = mr.sharedMaterial?.name ?? string.Empty;

            if (name == "NoteCube" || mat.StartsWith("NoteHD"))
            {
                return true;
            }

            if (name.Contains("Arrow") || name.Contains("Circle"))
            {
                return true;
            }

            return false;
        }

        private void Update()
        {
            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                var tracked = _hiddenRenderers[i];
                if (tracked.Renderer != null && tracked.Renderer.enabled)
                {
                    tracked.Renderer.enabled = false;
                }
            }
        }

        public void Stop(bool restoreOriginalRenderers)
        {
            enabled = false;

            if (restoreOriginalRenderers)
            {
                for (int i = 0; i < _hiddenRenderers.Count; i++)
                {
                    var tracked = _hiddenRenderers[i];
                    if (tracked.Renderer != null)
                    {
                        tracked.Renderer.enabled = tracked.OriginallyEnabled;
                    }
                }
            }

            _hiddenRenderers.Clear();
            Destroy(this);
        }
    }
}
