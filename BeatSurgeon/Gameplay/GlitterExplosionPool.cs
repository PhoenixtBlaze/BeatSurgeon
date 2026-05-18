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
        private sealed class GlitterExplosionInstanceData : MonoBehaviour
        {
            internal ParticleSystem[] CachedParticleSystems;
            internal Transform[] CachedTransforms;
            internal float EstimatedLifetime;
            internal int ActiveSpawnId;
        }

        private readonly struct PendingDespawn
        {
            internal PendingDespawn(int denomination, GameObject instance, int spawnId, float despawnTime)
            {
                Denomination = denomination;
                Instance = instance;
                SpawnId = spawnId;
                DespawnTime = despawnTime;
            }

            internal int Denomination { get; }
            internal GameObject Instance { get; }
            internal int SpawnId { get; }
            internal float DespawnTime { get; }
        }

        private static readonly LogUtil _log = LogUtil.GetLogger("GlitterExplosionPool");
        private static readonly Vector3 WarmActivationOffset = new Vector3(0f, -2048f, 0f);

        private const int MaxPoolPerDenomination = 16;
        internal const int RecommendedWarmPoolSizePerDenomination = 12;

        private static GlitterExplosionPool _instance;
        private static GameObject _go;

        private readonly Dictionary<int, Queue<GameObject>> _pools = new Dictionary<int, Queue<GameObject>>();
        private readonly List<PendingDespawn> _pendingDespawns = new List<PendingDespawn>();

        private Transform _gameplayVfxAnchor;
        private ParticleSystemRenderer _referenceBombParticleRenderer;
        private int _nextSpawnId;

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

        private void Update()
        {
            if (_pendingDespawns.Count == 0)
            {
                return;
            }

            float now = Time.time;
            while (_pendingDespawns.Count > 0 && _pendingDespawns[0].DespawnTime <= now)
            {
                PendingDespawn pendingDespawn = _pendingDespawns[0];
                _pendingDespawns.RemoveAt(0);

                if (pendingDespawn.Instance == null)
                {
                    continue;
                }

                GlitterExplosionInstanceData instanceData = GetInstanceData(pendingDespawn.Instance);
                if (instanceData == null || instanceData.ActiveSpawnId != pendingDespawn.SpawnId)
                {
                    continue;
                }

                ReturnToPool(pendingDespawn.Denomination, pendingDespawn.Instance, instanceData);
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        internal bool Prewarm(int denomination)
        {
            GameObject instance = CreatePreparedInstance(denomination, warmActivate: true);
            if (instance == null)
            {
                return false;
            }

            ReturnToPool(denomination, instance);
            return true;
        }

        internal bool EnsureWarmPoolSize(int denomination, int desiredPoolSize)
        {
            desiredPoolSize = Mathf.Clamp(desiredPoolSize, 0, MaxPoolPerDenomination);
            if (desiredPoolSize <= 0)
            {
                return true;
            }

            Queue<GameObject> pool = GetPool(denomination);
            while (pool.Count < desiredPoolSize)
            {
                GameObject instance = CreatePreparedInstance(denomination, warmActivate: true);
                if (instance == null)
                {
                    return false;
                }

                pool.Enqueue(instance);
            }

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

            GlitterExplosionInstanceData instanceData = GetInstanceData(emitterRoot);
            if (instanceData == null)
            {
                return false;
            }

            ResetParticleSystems(instanceData);

            Transform anchor = GetGameplayVfxAnchor();
            if (anchor != null && emitterRoot.transform.parent != anchor)
            {
                emitterRoot.transform.SetParent(anchor, false);
            }

            emitterRoot.transform.position = position;
            emitterRoot.transform.rotation = Quaternion.identity;
            emitterRoot.transform.localScale = Vector3.one;

            SyncLayer(instanceData, anchor);

            emitterRoot.SetActive(true);

            // Play all particle systems in the cloned emitter
            foreach (ParticleSystem particleSystem in instanceData.CachedParticleSystems)
            {
                if (particleSystem == null)
                {
                    continue;
                }

                try
                {
                    particleSystem.gameObject.SetActive(true);
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particleSystem.Play(true);
                }
                catch { }
            }

            float lifetime = EstimateLifetime(instanceData);
            int spawnId = AllocateSpawnId();
            instanceData.ActiveSpawnId = spawnId;
            EnqueueDespawn(denomination, emitterRoot, spawnId, lifetime);
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

            return CreatePreparedInstance(denomination, warmActivate: false);
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

        private GameObject CreatePreparedInstance(int denomination, bool warmActivate)
        {
            GameObject template = SurgeonEffectsBundleService.GetGlitterTemplate(denomination);
            if (template == null)
            {
                return null;
            }

            GameObject instance = Instantiate(template);
            instance.SetActive(false);
            PrepareEmitterInstance(instance);
            GetInstanceData(instance);

            if (warmActivate)
            {
                WarmActivateInstance(instance);
            }

            return instance;
        }

        private void WarmActivateInstance(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return;
            }

            GlitterExplosionInstanceData instanceData = GetInstanceData(emitterRoot);
            if (instanceData == null)
            {
                return;
            }

            try
            {
                ResetParticleSystems(instanceData);

                Transform anchor = GetGameplayVfxAnchor();
                if (anchor != null && emitterRoot.transform.parent != anchor)
                {
                    emitterRoot.transform.SetParent(anchor, false);
                }

                emitterRoot.transform.position = GetWarmActivationPosition();
                emitterRoot.transform.rotation = Quaternion.identity;
                emitterRoot.transform.localScale = Vector3.one;
                SyncLayer(instanceData, anchor);
                emitterRoot.SetActive(true);

                foreach (ParticleSystem particleSystem in instanceData.CachedParticleSystems)
                {
                    if (particleSystem == null)
                    {
                        continue;
                    }

                    try
                    {
                        particleSystem.gameObject.SetActive(true);
                        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        particleSystem.Play(true);
                        particleSystem.Simulate(0.05f, false, false, true);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("GlitterExplosionPool: warm activation failed: " + ex.Message);
            }
            finally
            {
                ResetParticleSystems(instanceData);
                instanceData.ActiveSpawnId = 0;
                emitterRoot.transform.SetParent(null, false);
                emitterRoot.SetActive(false);
            }
        }

        private Vector3 GetWarmActivationPosition()
        {
            Transform anchor = GetGameplayVfxAnchor();
            return anchor != null ? anchor.position + WarmActivationOffset : WarmActivationOffset;
        }

        private void ReturnToPool(int denomination, GameObject instance)
        {
            ReturnToPool(denomination, instance, GetInstanceData(instance));
        }

        private void ReturnToPool(int denomination, GameObject instance, GlitterExplosionInstanceData instanceData)
        {
            if (instance == null)
            {
                return;
            }

            ResetParticleSystems(instanceData);
            if (instanceData != null)
            {
                instanceData.ActiveSpawnId = 0;
            }

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

        private void EnqueueDespawn(int denomination, GameObject emitterRoot, int spawnId, float lifetime)
        {
            if (emitterRoot == null)
            {
                return;
            }

            PendingDespawn pendingDespawn = new PendingDespawn(
                denomination,
                emitterRoot,
                spawnId,
                Time.time + Mathf.Max(0f, lifetime));

            int insertIndex = _pendingDespawns.Count;
            while (insertIndex > 0 && _pendingDespawns[insertIndex - 1].DespawnTime > pendingDespawn.DespawnTime)
            {
                insertIndex--;
            }

            _pendingDespawns.Insert(insertIndex, pendingDespawn);
        }

        // ── Particle helpers ──────────────────────────────────────────────────

        private static void ResetParticleSystems(GlitterExplosionInstanceData instanceData)
        {
            if (instanceData == null || instanceData.CachedParticleSystems == null)
            {
                return;
            }

            foreach (ParticleSystem particleSystem in instanceData.CachedParticleSystems)
            {
                if (particleSystem == null)
                {
                    continue;
                }

                try
                {
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                catch { }
            }
        }

        private static float EstimateLifetime(GlitterExplosionInstanceData instanceData)
        {
            if (instanceData == null)
            {
                return 2f;
            }

            if (instanceData.EstimatedLifetime <= 0f)
            {
                instanceData.EstimatedLifetime = EstimateLifetime(instanceData.CachedParticleSystems);
            }

            return instanceData.EstimatedLifetime;
        }

        private static float EstimateLifetime(ParticleSystem[] particleSystems)
        {
            float max = 2f;
            if (particleSystems == null)
            {
                return max;
            }

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                if (particleSystem == null)
                {
                    continue;
                }

                try
                {
                    var main = particleSystem.main;
                    float delay = GetCurveMax(main.startDelay, 0f);
                    float life = GetCurveMax(main.startLifetime, 0.5f);
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

            VrVfxMaterialHelper.RepairShaders(root, "GlitterExplosionPool instance");

            ParticleSystemRenderer reference = GetReferenceBombRenderer();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows    = false;

                if (renderer is ParticleSystemRenderer psr)
                {
                    RebindShader(psr, reference);
                    SyncStereo(psr, reference);
                    HardenStereoCulling(psr);
                }
            }
        }

        private void PrepareEmitterInstance(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            PrepareRenderers(root);
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
                psr.alignment = reference.alignment;
                psr.normalDirection = reference.normalDirection;
                psr.allowRoll = reference.allowRoll;
                psr.maskInteraction = reference.maskInteraction;
                psr.enableGPUInstancing = reference.enableGPUInstancing;
                psr.sortingFudge = reference.sortingFudge;
                psr.renderingLayerMask = reference.renderingLayerMask;
                psr.lightProbeUsage = reference.lightProbeUsage;
                psr.reflectionProbeUsage = reference.reflectionProbeUsage;
                psr.motionVectorGenerationMode = reference.motionVectorGenerationMode;

                var activeVertexStreams = new List<ParticleSystemVertexStream>(reference.activeVertexStreamsCount);
                reference.GetActiveVertexStreams(activeVertexStreams);
                if (activeVertexStreams.Count > 0)
                {
                    psr.SetActiveVertexStreams(activeVertexStreams);
                }

                if (psr.trailMaterial != null)
                {
                    var activeTrailVertexStreams = new List<ParticleSystemVertexStream>(reference.activeTrailVertexStreamsCount);
                    reference.GetActiveTrailVertexStreams(activeTrailVertexStreams);
                    if (activeTrailVertexStreams.Count > 0)
                    {
                        psr.SetActiveTrailVertexStreams(activeTrailVertexStreams);
                    }
                }

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

        private static void HardenStereoCulling(ParticleSystemRenderer particleRenderer)
        {
            if (particleRenderer == null)
            {
                return;
            }

            try
            {
                particleRenderer.enabled = true;
                particleRenderer.forceRenderingOff = false;
                particleRenderer.allowOcclusionWhenDynamic = false;
                particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
                particleRenderer.receiveShadows = false;

                var localBounds = particleRenderer.localBounds;
                float minimumBoundsSize = EstimateMinimumBoundsSize(particleRenderer.GetComponent<ParticleSystem>());
                Vector3 expandedSize = new Vector3(
                    Mathf.Max(localBounds.size.x, minimumBoundsSize),
                    Mathf.Max(localBounds.size.y, minimumBoundsSize),
                    Mathf.Max(localBounds.size.z, minimumBoundsSize));
                particleRenderer.localBounds = new Bounds(localBounds.center, expandedSize);

                var particleSystem = particleRenderer.GetComponent<ParticleSystem>();
                if (particleSystem != null)
                {
                    var main = particleSystem.main;
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("GlitterExplosionPool: Failed to harden stereo culling for '" + particleRenderer.name + "': " + ex.Message);
            }
        }

        private static float EstimateMinimumBoundsSize(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return 12f;
            }

            try
            {
                var main = particleSystem.main;
                float lifetime = GetCurveMax(main.startLifetime, 1f);
                float speed = GetCurveMax(main.startSpeed, 2f);
                float size = main.startSize3D
                    ? Mathf.Max(
                        GetCurveMax(main.startSizeX, 0.5f),
                        GetCurveMax(main.startSizeY, 0.5f),
                        GetCurveMax(main.startSizeZ, 0.5f))
                    : GetCurveMax(main.startSize, 0.5f);

                return Mathf.Clamp(size + (speed * Mathf.Max(0.5f, lifetime)), 12f, 64f);
            }
            catch
            {
                return 12f;
            }
        }

        private void SyncLayer(GlitterExplosionInstanceData instanceData, Transform anchor)
        {
            if (instanceData == null || instanceData.CachedTransforms == null)
            {
                return;
            }

            if (anchor == null)
            {
                return;
            }

            int layer = anchor.gameObject.layer;
            foreach (Transform transform in instanceData.CachedTransforms)
            {
                if (transform != null)
                {
                    transform.gameObject.layer = layer;
                }
            }
        }

        private GlitterExplosionInstanceData GetInstanceData(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            GlitterExplosionInstanceData instanceData = root.GetComponent<GlitterExplosionInstanceData>();
            if (instanceData == null)
            {
                instanceData = root.AddComponent<GlitterExplosionInstanceData>();
            }

            if (instanceData.CachedParticleSystems == null || instanceData.CachedParticleSystems.Length == 0)
            {
                instanceData.CachedParticleSystems = root.GetComponentsInChildren<ParticleSystem>(true);
                instanceData.EstimatedLifetime = EstimateLifetime(instanceData.CachedParticleSystems);
            }

            if (instanceData.CachedTransforms == null || instanceData.CachedTransforms.Length == 0)
            {
                instanceData.CachedTransforms = root.GetComponentsInChildren<Transform>(true);
            }

            return instanceData;
        }

        private int AllocateSpawnId()
        {
            unchecked
            {
                _nextSpawnId++;
                if (_nextSpawnId == 0)
                {
                    _nextSpawnId = 1;
                }

                return _nextSpawnId;
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
                .Where(r => GetReferenceRendererScore(r) > 0)
                .OrderByDescending(GetReferenceRendererScore)
                .FirstOrDefault()
                ?? bombEffect.GetComponentsInChildren<ParticleSystemRenderer>(true)
                    .FirstOrDefault(r => r != null && r.renderMode != ParticleSystemRenderMode.Mesh)
                ?? bombEffect.GetComponentsInChildren<ParticleSystemRenderer>(true)
                    .FirstOrDefault();

            return _referenceBombParticleRenderer;
        }

        private static int GetReferenceRendererScore(ParticleSystemRenderer renderer)
        {
            if (renderer == null)
            {
                return int.MinValue;
            }

            string path = GetTransformPath(renderer.transform).ToLowerInvariant();
            string transformName = renderer.transform != null ? renderer.transform.name : string.Empty;
            string shaderName = renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                ? renderer.sharedMaterial.shader.name
                : string.Empty;
            string materialName = renderer.sharedMaterial != null ? renderer.sharedMaterial.name : string.Empty;

            int score = 0;

            if (shaderName.IndexOf("Custom/CustomParticles", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 2000;
            }

            if (transformName.IndexOf("ExplosionSparkles", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 1600;
            }

            if (transformName.IndexOf("Sparkle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 1000;
            }

            if (materialName.IndexOf("Sparkle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 750;
            }

            if (renderer.renderMode == ParticleSystemRenderMode.Stretch)
            {
                score += 500;
            }

            if (renderer.renderMode == ParticleSystemRenderMode.Mesh)
            {
                score -= 1000;
            }

            if (transformName.IndexOf("Debris", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith("/debrisps", StringComparison.OrdinalIgnoreCase))
            {
                score -= 1200;
            }

            if (shaderName.IndexOf("NoteHD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score -= 1200;
            }

            return score;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names.ToArray());
        }
    }
}
