using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal sealed class GlitterLoopEmitterManager : MonoBehaviour
    {
        private readonly struct LoopEmitterProfile
        {
            internal LoopEmitterProfile(Color particleColor, float particleSpeed, float emissionRate, int initialBurstCount)
            {
                ParticleColor = particleColor;
                ParticleSpeed = particleSpeed;
                EmissionRate = emissionRate;
                InitialBurstCount = initialBurstCount;
            }

            internal Color ParticleColor { get; }
            internal float ParticleSpeed { get; }
            internal float EmissionRate { get; }
            internal int InitialBurstCount { get; }
        }

        private static GlitterLoopEmitterManager _instance;
        private static GameObject _go;

        private readonly Dictionary<NoteController, GameObject> _attached = new Dictionary<NoteController, GameObject>();
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();

        private ParticleSystem _templateParticleSystem;
        private string _templateParticlePath = string.Empty;

        private const int MaxPoolSize = 64;

        internal static GlitterLoopEmitterManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _go = new GameObject("BeatSurgeonGlitterLoopEmitterManagerGO");
                    UnityEngine.Object.DontDestroyOnLoad(_go);
                    _instance = _go.AddComponent<GlitterLoopEmitterManager>();
                }

                return _instance;
            }
        }

        internal bool Prewarm()
        {
            if (!EnsureTemplate())
            {
                return false;
            }

            GameObject instance = GetOrCreatePooledInstance();
            if (instance == null)
            {
                return false;
            }

            CleanupInstance(instance, returnToPool: true);
            return true;
        }

        internal bool TryAttachToNote(NoteController noteController, int denomination)
        {
            if (noteController == null)
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
                Plugin.Log.Warn("GlitterLoopEmitterManager: Could not resolve BitsHyperCubeBurst loop template for targeted note attach.");
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

                StopAllParticleSystems(emitterRoot);

                ParticleSystem primaryParticleSystem = ResolvePrimaryParticleSystem(emitterRoot);
                if (primaryParticleSystem == null)
                {
                    CleanupInstance(emitterRoot, returnToPool: false);
                    Plugin.Log.Warn("GlitterLoopEmitterManager: primary loop particle system could not be resolved on attached instance.");
                    return false;
                }

                ConfigureLoopParticleSystem(primaryParticleSystem, denomination);
                primaryParticleSystem.Clear(true);
                primaryParticleSystem.Play(true);

                _attached[noteController] = emitterRoot;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("GlitterLoopEmitterManager: attach failed: " + ex.Message);
                CleanupInstance(emitterRoot, returnToPool: false);
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
                CleanupInstance(emitterRoot, returnToPool: true);
                _attached.Remove(noteController);
            }
        }

        private bool EnsureTemplate()
        {
            if (_templateParticleSystem != null)
            {
                return true;
            }

            _templateParticleSystem = SurgeonEffectsBundleService.GetTwitchBitBurstLoopTemplate();
            if (_templateParticleSystem == null)
            {
                return false;
            }

            _templateParticlePath = GetRelativePath(_templateParticleSystem.transform.root, _templateParticleSystem.transform);
            return true;
        }

        private GameObject GetOrCreatePooledInstance()
        {
            while (_pool.Count > 0)
            {
                GameObject pooledInstance = _pool.Dequeue();
                if (pooledInstance != null)
                {
                    return pooledInstance;
                }
            }

            var templateRoot = _templateParticleSystem.transform.root != null
                ? _templateParticleSystem.transform.root.gameObject
                : _templateParticleSystem.gameObject;

            var clone = UnityEngine.Object.Instantiate(templateRoot);
            UnityEngine.Object.DontDestroyOnLoad(clone);
            clone.SetActive(false);
            SetLayerRecursively(clone, 0);
            return clone;
        }

        private ParticleSystem ResolvePrimaryParticleSystem(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(_templateParticlePath))
            {
                return emitterRoot.GetComponent<ParticleSystem>()
                    ?? emitterRoot.GetComponentInChildren<ParticleSystem>(true);
            }

            Transform particleTransform = emitterRoot.transform.Find(_templateParticlePath);
            if (particleTransform != null)
            {
                return particleTransform.GetComponent<ParticleSystem>()
                    ?? particleTransform.GetComponentInChildren<ParticleSystem>(true);
            }

            return emitterRoot.GetComponentInChildren<ParticleSystem>(true);
        }

        private static void ConfigureLoopParticleSystem(ParticleSystem particleSystem, int denomination)
        {
            if (particleSystem == null)
            {
                return;
            }

            try
            {
                LoopEmitterProfile profile = GetLoopEmitterProfile(denomination);
                var main = particleSystem.main;
                main.loop = true;
                main.playOnAwake = false;
                main.startColor = profile.ParticleColor;
                main.startSpeed = profile.ParticleSpeed;

                var emission = particleSystem.emission;
                emission.enabled = true;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(profile.EmissionRate);

                if (profile.InitialBurstCount > 0)
                {
                    particleSystem.Emit(profile.InitialBurstCount);
                }
            }
            catch { }
        }

        private static LoopEmitterProfile GetLoopEmitterProfile(int denomination)
        {
            switch (denomination)
            {
                case 1:
                    return new LoopEmitterProfile(Color.white, 2f, 10f, 8);
                case 100:
                    return new LoopEmitterProfile(new Color(0.5724139f, 0f, 1f, 1f), 5f, 50f, 16);
                case 1000:
                    return new LoopEmitterProfile(Color.green, 10f, 100f, 24);
                case 5000:
                    return new LoopEmitterProfile(Color.blue, 15f, 500f, 48);
                case 10000:
                    return new LoopEmitterProfile(Color.red, 20f, 1000f, 64);
                default:
                    return new LoopEmitterProfile(Color.white, 2f, 10f, 8);
            }
        }

        private static void StopAllParticleSystems(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return;
            }

            foreach (var particleSystem in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                try
                {
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                catch { }
            }
        }

        private void CleanupInstance(GameObject emitterRoot, bool returnToPool)
        {
            if (emitterRoot == null)
            {
                return;
            }

            StopAllParticleSystems(emitterRoot);
            emitterRoot.SetActive(false);
            emitterRoot.transform.SetParent(null, false);

            if (returnToPool && _pool.Count < MaxPoolSize)
            {
                _pool.Enqueue(emitterRoot);
            }
            else
            {
                UnityEngine.Object.Destroy(emitterRoot);
            }
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
            {
                return;
            }

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
            }
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
            return string.Join("/", segments.ToArray());
        }
    }
}