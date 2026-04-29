using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using IPA.Utilities;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Spawns outline particle emitters on every note spawn while active.
    /// Uses the authored surgeoneffects outline emitter when available,
    /// with a small built-in fallback template.
    /// </summary>
    public class OutlineEmitterManager : MonoBehaviour
    {
        private static OutlineEmitterManager _instance;
        private static GameObject _go;

        private BeatmapObjectManager _beatmapObjectManager;
        private ParticleSystem _templateParticleSystem;
        private readonly Dictionary<NoteController, GameObject> _attached = new Dictionary<NoteController, GameObject>();
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();
        private const int MaxPoolSize = 64;
        private Coroutine _stopCoroutine;

        public static OutlineEmitterManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeonOutlineEmitterManagerGO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<OutlineEmitterManager>();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Start applying outline particle emitters to notes for durationSeconds.
        /// Returns false if not in a gameplay scene or required template not found.
        /// </summary>
        public bool StartOutlineEffect(float durationSeconds)
        {
            var gameplay = GameplayManager.GetInstance();
            if (gameplay == null || !gameplay.IsInMap)
            {
                Plugin.Log.Warn("OutlineEmitterManager: Not in a map.");
                return false;
            }

            if (!EnsureBeatmapObjectManager())
            {
                Plugin.Log.Warn("OutlineEmitterManager: Could not find BeatmapObjectManager.");
                return false;
            }

            if (!EnsureTemplate())
            {
                Plugin.Log.Warn("OutlineEmitterManager: Could not resolve outline particle template.");
                return false;
            }

            // Subscribe events
            _beatmapObjectManager.noteDidStartJumpEvent += NoteDidStartJumpEvent;
            _beatmapObjectManager.noteWasCutEvent += NoteWasCutEvent;
            _beatmapObjectManager.noteWasMissedEvent += NoteWasMissedEvent;

            if (_stopCoroutine != null)
            {
                StopCoroutine(_stopCoroutine);
                _stopCoroutine = null;
            }
            _stopCoroutine = StartCoroutine(StopAfterSecondsCoroutine(durationSeconds));

            LogUtils.Debug(() => "OutlineEmitterManager: Started outline emitter for " + durationSeconds + "s");
            return true;
        }

        private IEnumerator StopAfterSecondsCoroutine(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            StopOutlineEffect();
        }

        public void StopOutlineEffect()
        {
            if (_beatmapObjectManager != null)
            {
                _beatmapObjectManager.noteDidStartJumpEvent -= NoteDidStartJumpEvent;
                _beatmapObjectManager.noteWasCutEvent -= NoteWasCutEvent;
                _beatmapObjectManager.noteWasMissedEvent -= NoteWasMissedEvent;
            }

            foreach (var kv in _attached)
            {
                try { if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value); } catch { }
            }
            _attached.Clear();

            // Destroy pooled instances
            while (_pool.Count > 0)
            {
                var p = _pool.Dequeue();
                try { if (p != null) UnityEngine.Object.Destroy(p); } catch { }
            }

            if (_stopCoroutine != null)
            {
                StopCoroutine(_stopCoroutine);
                _stopCoroutine = null;
            }

            LogUtils.Debug(() => "OutlineEmitterManager: Stopped and cleaned up");
        }

        private bool EnsureBeatmapObjectManager()
        {
            if (_beatmapObjectManager != null) return true;

            // Mirror TwitchController's method of locating the BeatmapObjectManager 
            var spawnController = UnityEngine.Object.FindObjectOfType<BeatmapObjectSpawnController>();
            if (spawnController != null)
            {
                _beatmapObjectManager = spawnController.GetField<IBeatmapObjectSpawner, BeatmapObjectSpawnController>("_beatmapObjectSpawner") as BeatmapObjectManager;
                if (_beatmapObjectManager != null) return true;
            }

            var multiplayerLocalActiveClient = UnityEngine.Object.FindObjectOfType<MultiplayerLocalActiveClient>();
            if (multiplayerLocalActiveClient != null)
            {
                _beatmapObjectManager = multiplayerLocalActiveClient.GetField<BeatmapObjectManager, MultiplayerLocalActiveClient>("_beatmapObjectManager");
            }

            return _beatmapObjectManager != null;
        }

        private bool EnsureTemplate()
        {
            if (_templateParticleSystem != null) return true;

            try
            {
                var ps = SurgeonEffectsBundleService.GetOutlineParticlesTemplate();
                if (ps != null)
                {
                    return SetTemplate(ps);
                }
            }
            catch { }

            try
            {
                _templateParticleSystem = CreateDefaultOutlineTemplate();
                if (_templateParticleSystem != null)
                {
                    LogUtils.Debug(() => "OutlineEmitterManager: Created built-in default outline particle template.");
                    return SetTemplate(_templateParticleSystem);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("OutlineEmitterManager: failed to create default template: " + ex.Message);
            }

            return false;
        }

        private bool SetTemplate(ParticleSystem template)
        {
            _templateParticleSystem = template;
            RepairTemplateShaders();
            return _templateParticleSystem != null;
        }

        public bool TryAttachToNote(NoteController noteController)
        {
            if (noteController == null)
            {
                return false;
            }

            if (!EnsureTemplate())
            {
                Plugin.Log.Warn("OutlineEmitterManager: Could not resolve outline particle template for targeted note attach.");
                return false;
            }

            return TryAttachEmitter(noteController);
        }

        public void DetachFromNote(NoteController noteController)
        {
            CleanupFor(noteController);
        }


        
        private void RepairTemplateShaders()
        {
            try
            {
                if (_templateParticleSystem == null)
                {
                    return;
                }

                var rend = _templateParticleSystem.GetComponent<ParticleSystemRenderer>();
                if (rend == null)
                {
                    return;
                }

                VrVfxMaterialHelper.RepairShaders(_templateParticleSystem.gameObject, "OutlineEmitterManager template");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("OutlineEmitterManager: RepairTemplateShaders failed: " + ex.Message);
            }
        }
        

        private void NoteDidStartJumpEvent(NoteController noteController)
        {
            TryAttachEmitter(noteController);
        }

        private bool TryAttachEmitter(NoteController noteController)
        {
            if (noteController == null || _templateParticleSystem == null) return false;

            try
            {
                // Only attach to regular cube notes (ignore bombs)
                var noteData = noteController?.noteData;
                if (noteData == null) return false;
                if (noteData.colorType == ColorType.None) return false;

                // Prevent duplicate attachments
                if (_attached.ContainsKey(noteController)) return true;

                var go = GetOrCreatePooledInstance();

                // Parent to the note transform if available
                Transform parent = noteController.noteTransform != null ? noteController.noteTransform : noteController.transform;
                go.transform.SetParent(parent, false);
                SetLayerRecursively(go, 0);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.SetActive(true);
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    try
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.Simulate(0.35f, true, true, true);
                        ps.Play(true);
                    }
                    catch { }
                }
                _attached[noteController] = go;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("OutlineEmitterManager: spawn attach failed:" + ex.Message);
                return false;
            }
        }

        private static Texture2D CreateCircularParticleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size / 2f) / (size / 2f);
                    float dy = (y + 0.5f - size / 2f) / (size / 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }
        private void NoteWasCutEvent(NoteController noteController, in NoteCutInfo noteCutInfo)
        {
            CleanupFor(noteController);
        }


        private void NoteWasMissedEvent(NoteController noteController)
        {
            CleanupFor(noteController);
        }

        private void CleanupFor(NoteController noteController)
        {
            if (noteController == null) return;
            if (_attached.TryGetValue(noteController, out GameObject go))
            {
                try
                {
                    if (go != null)
                    {
                        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            try
                            {
                                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            }
                            catch { }
                        }
                        go.SetActive(false);
                        go.transform.SetParent(null);
                        if (_pool.Count < MaxPoolSize)
                            _pool.Enqueue(go);
                        else
                            UnityEngine.Object.Destroy(go);
                    }
                }
                catch { }
                _attached.Remove(noteController);
            }
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

            var templateRoot = _templateParticleSystem.transform.root != null
                ? _templateParticleSystem.transform.root.gameObject
                : _templateParticleSystem.gameObject;

            var newObj = UnityEngine.Object.Instantiate(templateRoot);
            UnityEngine.Object.DontDestroyOnLoad(newObj);
            newObj.SetActive(false);

            SetLayerRecursively(newObj, 0);

            string particlePath = GetRelativePath(_templateParticleSystem.transform.root, _templateParticleSystem.transform);
            Transform clonedParticleTransform = string.IsNullOrEmpty(particlePath)
                ? newObj.transform
                : newObj.transform.Find(particlePath);

            if (clonedParticleTransform != null)
            {
                VrVfxMaterialHelper.RepairShaders(clonedParticleTransform.gameObject, "OutlineEmitterManager instance");
            }
            else
            {
                VrVfxMaterialHelper.RepairShaders(newObj, "OutlineEmitterManager instance");
            }

            return newObj;
        }

        private static string GetRelativePath(Transform root, Transform child)
        {
            if (root == null || child == null || root == child)
            {
                return string.Empty;
            }

            var segments = new List<string>();
            var current = child;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }
        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
            }
        }

        private ParticleSystem CreateDefaultOutlineTemplate()
        {
            var go = new GameObject("BeatSurgeon_DefaultOutlineTemplate");
            UnityEngine.Object.DontDestroyOnLoad(go);

            var ps = go.AddComponent<ParticleSystem>();
            var renderer = go.GetComponent<ParticleSystemRenderer>();

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.8f;
            main.startLifetime = 0.6f;
            main.startSpeed = 0.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startColor = Color.white;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            try
            {
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
            }
            catch
            {
            }

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = false;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.5f),
                    new Keyframe(0.2f, 1f),
                    new Keyframe(1f, 0f)));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.6f, 0.9f, 1f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sortingFudge = 1f;
            }

            try
            {
                if (renderer != null)
                {
                    Texture mainTexture = null;
                    if (renderer.sharedMaterial != null)
                    {
                        mainTexture = renderer.sharedMaterial.mainTexture;
                    }

                    if (mainTexture == null)
                    {
                        mainTexture = CreateCircularParticleTexture(64);
                    }

                    Material safeMat = VrVfxMaterialHelper.CreateSafeParticleMaterial(renderer.sharedMaterial, mainTexture);
                    if (safeMat != null)
                    {
                        renderer.sharedMaterial = safeMat;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("OutlineEmitterManager: CreateDefaultOutlineTemplate material setup failed: " + ex.Message);
            }

            return ps;
        }
    }
}
