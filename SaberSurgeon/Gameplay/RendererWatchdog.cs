using System.Collections.Generic;
using UnityEngine;

namespace SaberSurgeon.Gameplay
{
    public class RendererWatchdog : MonoBehaviour
    {
        private class Entry { public MeshRenderer mr; public bool lastEnabled; }
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
                _entries.Add(new Entry { mr = r, lastEnabled = r.enabled });
            }

            _endTime = Time.unscaledTime + seconds;
            enabled = true;

            Plugin.Log.Info($"RendererWatchdog: Tracking {_entries.Count} renderers for {seconds:0.00}s under {root.name}");
        }

        private void Update()
        {
            if (Time.unscaledTime > _endTime)
            {
                Plugin.Log.Info("RendererWatchdog: End of tracking window");
                enabled = false;
                Destroy(this);
                return;
            }

            foreach (var e in _entries)
            {
                if (e.mr == null) continue;
                if (e.mr.name == "NoteCube")
                {
                    //Plugin.Log.Warn($"RendererWatchdog: Forcing {e.mr.name} back to disabled on bomb note");
                    e.mr.enabled = false;
                    e.lastEnabled = false;
                    continue;
                }
                if (e.mr.enabled != e.lastEnabled)
                {
                    Plugin.Log.Warn($"RendererWatchdog: '{e.mr.name}' enabled changed {e.lastEnabled} -> {e.mr.enabled} (path: {GetPath(e.mr.transform)})");
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
