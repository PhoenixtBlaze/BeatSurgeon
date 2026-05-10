using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal sealed class SubBurstNoteManager : MonoBehaviour
    {
        private static SubBurstNoteManager _instance;
        private static GameObject _go;

        private readonly Dictionary<NoteController, GameObject> _attached = new Dictionary<NoteController, GameObject>();
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        private volatile int _remainingCount;
        private volatile bool _pendingScan;
        private GameObject _templateRoot;

        private const int MaxPoolSize = 32;

        internal static SubBurstNoteManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeonSubBurstNoteManagerGO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<SubBurstNoteManager>();
                }

                return _instance;
            }
        }

        internal bool HasRemainingCount => _remainingCount > 0;

        /// <summary>
        /// Called on level end to clear stale NoteController references that were never
        /// cut or missed. Beat Saber pools NoteController objects across scenes, so without
        /// this, a reused controller key would be found in _attached and the next note
        /// attached to it would be silently skipped.
        /// </summary>
        internal void Reset()
        {
            _remainingCount = 0;
            _pendingScan = false;

            foreach (var kvp in _attached)
            {
                if (kvp.Value != null)
                {
                    CleanupInstance(kvp.Value);
                }
            }

            _attached.Clear();
        }

        internal void Activate(int count = 20)
        {
            _remainingCount = count;
            _pendingScan = true;
            Plugin.Log.Info("SubBurstNoteManager: Activated count=" + count);
        }

        private void Update()
        {
            if (!_pendingScan)
            {
                return;
            }

            _pendingScan = false;

            // Attach to any GameNoteControllers already active in the scene when !test fires.
            // TryAttachToNote guards against double-attachment via _attached dictionary.
            var activeNotes = FindObjectsOfType<GameNoteController>();
            foreach (var note in activeNotes)
            {
                if (_remainingCount <= 0)
                {
                    break;
                }

                TryAttachToNote(note);
            }
        }

        internal bool Prewarm()
        {
            if (!EnsureTemplate())
            {
                return false;
            }

            GameObject inst = GetOrCreatePooledInstance();
            if (inst == null)
            {
                return false;
            }

            ReturnToPool(inst);
            return true;
        }

        internal bool TryAttachToNote(GameNoteController noteController)
        {
            if (noteController == null || _remainingCount <= 0)
            {
                return false;
            }

            var noteData = noteController.noteData;
            if (noteData == null || noteData.colorType == ColorType.None || noteData.gameplayType != NoteData.GameplayType.Normal)
            {
                return false;
            }

            if (_attached.ContainsKey(noteController))
            {
                return true;
            }

            if (!EnsureTemplate())
            {
                Plugin.Log.Warn("SubBurstNoteManager: sub burst template not available.");
                return false;
            }

            GameObject emitterRoot = GetOrCreatePooledInstance();
            if (emitterRoot == null)
            {
                return false;
            }

            try
            {
                Transform parent = noteController.noteTransform != null ? noteController.noteTransform : noteController.transform;
                emitterRoot.transform.SetParent(parent, false);
                SetLayerRecursively(emitterRoot, noteController.gameObject.layer);
                emitterRoot.transform.localPosition = Vector3.zero;
                emitterRoot.transform.localRotation = Quaternion.identity;
                emitterRoot.transform.localScale = Vector3.one;
                emitterRoot.SetActive(true);

                foreach (var ps in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
                {
                    try
                    {
                        // Only activate the root BitsHyperCubeBurst PS; disable all child systems
                        // (bit-atlas children and SubHyperCubeBurst) so they don't produce unwanted visuals.
                        bool isRoot = ps.gameObject == emitterRoot;
                        if (isRoot)
                        {
                            ps.gameObject.SetActive(true);
                            var main = ps.main;
                            main.loop = true;
                            main.playOnAwake = false;
                            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.Play(false);
                        }
                        else
                        {
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.gameObject.SetActive(false);
                        }
                    }
                    catch { }
                }

                _attached[noteController] = emitterRoot;
                _remainingCount--;
                Plugin.Log.Info("SubBurstNoteManager: attached to note remaining=" + _remainingCount);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("SubBurstNoteManager: attach failed: " + ex.Message);
                CleanupInstance(emitterRoot);
                return false;
            }
        }

        internal void DetachFromNote(NoteController noteController)
        {
            if (noteController == null)
            {
                return;
            }

            if (_attached.TryGetValue(noteController, out GameObject emitterRoot))
            {
                _attached.Remove(noteController);
                emitterRoot.transform.SetParent(null, false);
                StartCoroutine(ReleaseAfterDelay(emitterRoot, 1.5f));
            }
        }

        /// <summary>
        /// Fire a one-shot BitsHyperCubeBurst (root PS only, no children) at the given
        /// world position. Used for glitter/bits note cuts.
        /// </summary>
        internal void SpawnOneShotBurst(Vector3 worldPosition)
        {
            if (!EnsureTemplate())
            {
                return;
            }

            GameObject emitterRoot = GetOrCreatePooledInstance();
            if (emitterRoot == null)
            {
                return;
            }

            try
            {
                emitterRoot.transform.SetParent(null, false);
                emitterRoot.transform.position = worldPosition;
                emitterRoot.SetActive(true);

                foreach (var ps in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
                {
                    try
                    {
                        bool isRoot = ps.gameObject == emitterRoot;
                        if (isRoot)
                        {
                            ps.gameObject.SetActive(true);
                            var main = ps.main;
                            main.loop = false;
                            main.playOnAwake = false;
                            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.Play(false);
                        }
                        else
                        {
                            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            ps.gameObject.SetActive(false);
                        }
                    }
                    catch { }
                }

                StartCoroutine(ReleaseAfterDelay(emitterRoot, 3.0f));
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("SubBurstNoteManager.SpawnOneShotBurst failed: " + ex.Message);
                CleanupInstance(emitterRoot);
            }
        }

        private IEnumerator ReleaseAfterDelay(GameObject emitterRoot, float delay)
        {
            yield return new WaitForSeconds(delay);
            CleanupInstance(emitterRoot);
        }

        private bool EnsureTemplate()
        {
            if (_templateRoot != null)
            {
                return true;
            }

            _templateRoot = SurgeonEffectsBundleService.GetTwitchBitBurstTemplate();
            return _templateRoot != null;
        }

        private GameObject GetOrCreatePooledInstance()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            var clone = UnityEngine.Object.Instantiate(_templateRoot);
            UnityEngine.Object.DontDestroyOnLoad(clone);
            clone.SetActive(false);

            foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                try
                {
                    var main = ps.main;
                    main.playOnAwake = false;
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                catch { }
            }

            return clone;
        }

        private void CleanupInstance(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return;
            }

            foreach (var ps in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                try { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); }
                catch { }
            }

            emitterRoot.transform.SetParent(null, false);
            emitterRoot.SetActive(false);
            ReturnToPool(emitterRoot);
        }

        private void ReturnToPool(GameObject inst)
        {
            if (_pool.Count < MaxPoolSize)
            {
                _pool.Enqueue(inst);
            }
            else
            {
                UnityEngine.Object.Destroy(inst);
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
        }
    }
}
