using System.Collections.Generic;
using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    /// <summary>
    /// Monitors renderer state changes and logs conflicts (non-invasive monitoring).
    /// Does NOT override state changes from other systems; only reports them.
    /// </summary>
    public class RendererWatchdog : MonoBehaviour
    {
        private class Entry
        {
            public MeshRenderer mr;
            public bool lastEnabled;
            public bool isManaged; // true if SaberSurgeon is managing this renderer
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private float _endTime;
        private Transform _root;
        private Transform _ignoreRoot;

        public void Init(Transform root, float seconds, Transform ignoreRoot = null)
        {
            _root = root;
            _ignoreRoot = ignoreRoot;
            _entries.Clear();

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (_ignoreRoot != null && r.transform.IsChildOf(_ignoreRoot)) continue;

                // Mark NoteCube as managed by SaberSurgeon (bomb effect)
                bool isManaged = r.name == "NoteCube";

                _entries.Add(new Entry
                {
                    mr = r,
                    lastEnabled = r.enabled,
                    isManaged = isManaged
                });
            }

            _endTime = Time.unscaledTime + seconds;
            enabled = true;

            LogUtils.Debug($"RendererWatchdog: Tracking {_entries.Count} renderers for {seconds:0.00}s under {root.name}");
        }

        private void Update()
        {
            if (Time.unscaledTime > _endTime)
            {
                LogUtils.Debug("RendererWatchdog: End of tracking window");
                enabled = false;
                Destroy(this);
                return;
            }

            foreach (var e in _entries)
            {
                if (e.mr == null) continue;

                // Only enforce state on renderers SaberSurgeon manages
                if (e.isManaged && e.mr.name == "NoteCube")
                {
                    if (e.mr.enabled != false) // Should stay disabled during bomb
                    {
                        LogUtils.Debug($"RendererWatchdog: NoteCube state changed by external source, re-enforcing disable");
                        e.mr.enabled = false;
                    }
                    e.lastEnabled = false;
                    continue;
                }

                // For non-managed renderers, just report conflicts (don't override)
                if (e.mr.enabled != e.lastEnabled)
                {
                    LogUtils.Warn($"RendererWatchdog: External mod modified '{e.mr.name}' (enabled: {e.lastEnabled} → {e.mr.enabled})");
                    e.lastEnabled = e.mr.enabled;
                }
            }
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
