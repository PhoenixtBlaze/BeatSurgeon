using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BeatSurgeon.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// Pools and spawns the denomination-matched Glitter emitters that live inside
    /// the SurgeonExplosion prefab (Glitter1, Glitter100, Glitter1000, Glitter5000,
    /// Glitter10000).  One instance is emitted at the note cut point.
    /// </summary>
    internal sealed class GlitterExplosionPool : MonoBehaviour
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("GlitterExplosionPool");

        private const int MaxPoolPerDenomination = 16;

        private static GlitterExplosionPool _instance;
        private static GameObject _go;

        private readonly Dictionary<int, Queue<GameObject>> _pools = new Dictionary<int, Queue<GameObject>>();

        private Transform _gameplayVfxAnchor;
        private ParticleSystemRenderer _referenceBombParticleRenderer;

        internal static GlitterExplosionPool Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _go = new GameObject("BeatSurgeonGlitterExplosionPool");
                UnityEngine.Object.DontDestroyOnLoad(_go);
                _instance = _go.AddComponent<GlitterExplosionPool>();
                return _instance;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        internal bool Prewarm(int denomination)
        {
            GameObject instance = GetOrCreateInstance(denomination);
            if (instance == null)
            {
                return false;
            }

            ReturnToPool(denomination, instance);
            return true;
        }

        /// <summary>
        /// Spawn the Glitter emitter that matches <paramref name="denomination"/>
        /// at <paramref name="position"/> in world space.
        /// </summary>
        internal bool Spawn(int denomination, Vector3 position)
        {
            GameObject emitterRoot = GetOrCreateInstance(denomination);
            if (emitterRoot == null)
            {
                _log.Warn("GlitterExplosionPool.Spawn: no template for denomination=" + denomination);
                return false;
            }

            ResetParticleSystems(emitterRoot);

            Transform anchor = GetGameplayVfxAnchor();
            if (anchor != null && emitterRoot.transform.parent != anchor)
            {
                emitterRoot.transform.SetParent(anchor, false);
            }

            emitterRoot.transform.position = position;
            emitterRoot.transform.rotation = Quaternion.identity;
            emitterRoot.transform.localScale = Vector3.one;

            SyncLayer(emitterRoot);
            PrepareRenderers(emitterRoot);

            emitterRoot.SetActive(true);

            // Play all particle systems in the cloned emitter
            foreach (var ps in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                try
                {
                    ps.gameObject.SetActive(true);
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ps.Play(true);
                }
                catch { }
            }

            float lifetime = EstimateLifetime(emitterRoot);
            StartCoroutine(DespawnAfter(denomination, emitterRoot, lifetime));
            return true;
        }

        // ── Pool helpers ──────────────────────────────────────────────────────

        private GameObject GetOrCreateInstance(int denomination)
        {
            Queue<GameObject> pool = GetPool(denomination);
            while (pool.Count > 0)
            {
                var pooled = pool.Dequeue();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            // Ask SurgeonEffectsBundleService for the template (cached after first load)
            GameObject template = SurgeonEffectsBundleService.GetGlitterTemplate(denomination);
            if (template == null)
            {
                return null;
            }

            var instance = Instantiate(template);
            instance.SetActive(false);
            return instance;
        }

        private Queue<GameObject> GetPool(int denomination)
        {
            if (!_pools.TryGetValue(denomination, out var pool))
            {
                pool = new Queue<GameObject>();
                _pools[denomination] = pool;
            }

            return pool;
        }

        private void ReturnToPool(int denomination, GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            ResetParticleSystems(instance);
            instance.transform.SetParent(null, false);
            instance.SetActive(false);

            Queue<GameObject> pool = GetPool(denomination);
            if (pool.Count < MaxPoolPerDenomination)
            {
                pool.Enqueue(instance);
            }
            else
            {
                Destroy(instance);
            }
        }

        private IEnumerator DespawnAfter(int denomination, GameObject emitterRoot, float lifetime)
        {
            yield return new WaitForSeconds(lifetime);

            if (emitterRoot == null)
            {
                yield break;
            }

            ReturnToPool(denomination, emitterRoot);
        }

        // ── Particle helpers ──────────────────────────────────────────────────

        private static void ResetParticleSystems(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                try
                {
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                catch { }
            }
        }

        private static float EstimateLifetime(GameObject root)
        {
            float max = 2f;
            if (root == null)
            {
                return max;
            }

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                try
                {
                    var main = ps.main;
                    float delay    = GetCurveMax(main.startDelay, 0f);
                    float life     = GetCurveMax(main.startLifetime, 0.5f);
                    float duration = Mathf.Max(main.duration, 0.1f);
                    max = Mathf.Max(max, delay + duration + life + 0.1f);
                }
                catch { }
            }

            return Mathf.Clamp(max, 2f, 6f);
        }

        private static float GetCurveMax(ParticleSystem.MinMaxCurve curve, float fallback)
        {
            try
            {
                return Mathf.Max(curve.constantMin, curve.constantMax, curve.constant);
            }
            catch
            {
                return fallback;
            }
        }

        // ── Rendering / layer helpers ─────────────────────────────────────────

        private void PrepareRenderers(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            ParticleSystemRenderer reference = GetReferenceBombRenderer();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows    = false;

                if (renderer is ParticleSystemRenderer psr)
                {
                    RebindShader(psr, reference);
                    SyncStereo(psr, reference);
                }
            }
        }

        private static void RebindShader(ParticleSystemRenderer psr, ParticleSystemRenderer reference)
        {
            if (psr == null)
            {
                return;
            }

            Shader refShader = reference != null && reference.sharedMaterial != null
                ? reference.sharedMaterial.shader
                : null;
            if (refShader == null)
            {
                return;
            }

            try
            {
                var mats = psr.sharedMaterials;
                if (mats == null || mats.Length == 0)
                {
                    return;
                }

                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                    {
                        continue;
                    }

                    if (mats[i].shader == null || !mats[i].shader.isSupported)
                    {
                        mats[i].shader = refShader;
                        changed = true;
                    }
                }

                if (changed)
                {
                    psr.sharedMaterials = mats;
                }
            }
            catch { }
        }

        private static void SyncStereo(ParticleSystemRenderer psr, ParticleSystemRenderer reference)
        {
            if (psr == null || reference == null)
            {
                return;
            }

            try
            {
                psr.allowOcclusionWhenDynamic = false;
            }
            catch { }

            try
            {
                psr.shadowCastingMode = ShadowCastingMode.Off;
                psr.receiveShadows    = false;
            }
            catch { }
        }

        private void SyncLayer(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Transform anchor = GetGameplayVfxAnchor();
            if (anchor == null)
            {
                return;
            }

            int layer = anchor.gameObject.layer;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = layer;
            }
        }

        private Transform GetGameplayVfxAnchor()
        {
            if (_gameplayVfxAnchor != null)
            {
                return _gameplayVfxAnchor;
            }

            var spawner = Resources.FindObjectsOfTypeAll<NoteCutCoreEffectsSpawner>().FirstOrDefault();
            if (spawner == null)
            {
                return null;
            }

            var bombEffect = spawner.GetComponentInChildren<BombExplosionEffect>(true);
            _gameplayVfxAnchor = bombEffect != null ? bombEffect.transform : spawner.transform;
            return _gameplayVfxAnchor;
        }

        private ParticleSystemRenderer GetReferenceBombRenderer()
        {
            if (_referenceBombParticleRenderer != null)
            {
                return _referenceBombParticleRenderer;
            }

            Transform anchor = GetGameplayVfxAnchor();
            if (anchor == null)
            {
                return null;
            }

            var bombEffect = anchor.GetComponent<BombExplosionEffect>();
            if (bombEffect == null)
            {
                return null;
            }

            _referenceBombParticleRenderer = bombEffect
                .GetComponentsInChildren<ParticleSystemRenderer>(true)
                .Where(r => r != null && r.renderMode != ParticleSystemRenderMode.Mesh)
                .FirstOrDefault();

            return _referenceBombParticleRenderer;
        }
    }
}
