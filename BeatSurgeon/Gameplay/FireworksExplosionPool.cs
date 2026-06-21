using BeatSurgeon.Twitch;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeatSurgeon.Gameplay
{
    public class FireworksExplosionPool : MonoBehaviour
    {
        private static FireworksExplosionPool _instance;
        private static GameObject _go;

        private GameObject _explosionPrefab;
        private readonly Queue<GameObject> _pool = new Queue<GameObject>();
        private readonly Dictionary<string, Queue<GameObject>> _emitterPools = new Dictionary<string, Queue<GameObject>>(StringComparer.OrdinalIgnoreCase);
        private Transform _gameplayVfxAnchor;
        private ParticleSystemRenderer _referenceBombParticleRenderer;

        public static FireworksExplosionPool Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _go = new GameObject("BeatSurgeonFireworksExplosionPool");
                DontDestroyOnLoad(_go);
                _instance = _go.AddComponent<FireworksExplosionPool>();
                return _instance;
            }
        }

        private void Awake()
        {
        }

        private void LoadAssetBundle()
        {
            // Full SurgeonExplosion prefab load is no longer required for per-emitter bomb VFX.
        }

        public void Spawn(Vector3 position, Color baseColor, int burstCount = 220, float life = 1.6f)
        {
            SpawnForSelection(BombExplosionEffectSettings.GetSelectedOption(), position, baseColor, life, attachToGameplayAnchor: true);
        }

        internal void SpawnPreview(string selection, Vector3 position)
        {
            Color fireworkColor = EntitlementsState.HasVisualsAccess
                ? (BeatSurgeon.Plugin.Settings?.BombGradientStart ?? Color.red)
                : Color.red;

            SpawnForSelection(
                BombExplosionEffectSettings.NormalizeOption(selection),
                position,
                fireworkColor,
                life: 2.0f,
                attachToGameplayAnchor: false);
        }

        internal void SpawnForSelection(string selection, Vector3 position, Color baseColor, float life, bool attachToGameplayAnchor)
        {
            string emitterName = BombExplosionEffectSettings.GetEmitterNameForOption(selection);
            if (!SpawnEmitterByName(emitterName, position, baseColor, life, attachToGameplayAnchor))
            {
                SpawnEmitterByName(
                    BundleRegistry.SurgeonExplosionRefs.SparkEmitterName,
                    position,
                    baseColor,
                    life,
                    attachToGameplayAnchor);
            }
        }

        private bool SpawnEmitterByName(string emitterName, Vector3 position, Color baseColor, float life, bool attachToGameplayAnchor)
        {
            GameObject explosion = GetOrCreateEmitterInstance(emitterName);
            if (explosion == null)
            {
                return false;
            }

            PlayExplosionInstance(explosion, emitterName, position, baseColor, life, attachToGameplayAnchor);
            return true;
        }

        private void SpawnFullExplosionPrefab(Vector3 position, Color baseColor, float life, bool attachToGameplayAnchor)
        {
            if (_explosionPrefab == null)
            {
                LoadAssetBundle();
                if (_explosionPrefab == null)
                {
                    return;
                }
            }

            GameObject explosion = GetOrCreateInstance();
            PlayExplosionInstance(explosion, null, position, baseColor, life, attachToGameplayAnchor);
        }

        private void PlayExplosionInstance(
            GameObject explosion,
            string emitterName,
            Vector3 position,
            Color baseColor,
            float life,
            bool attachToGameplayAnchor)
        {
            if (explosion == null)
            {
                return;
            }

            if (attachToGameplayAnchor)
            {
                AttachToGameplayVfxAnchor(explosion);
            }
            else if (explosion.transform.parent != null)
            {
                explosion.transform.SetParent(null, false);
            }

            bool preserveAuthoredAppearance = UsesAuthoredBombExplosionAppearance(emitterName, explosion);

            explosion.transform.position = position;
            if (!preserveAuthoredAppearance)
            {
                explosion.transform.rotation = Quaternion.identity;
                explosion.transform.localScale = Vector3.one;
            }

            if (attachToGameplayAnchor)
            {
                SyncToGameplayVfxLayer(explosion);
            }

            Gradient rainbowGradient = preserveAuthoredAppearance ? null : CreateRainbowGradient();
            explosion.SetActive(true);

            var systems = explosion.GetComponentsInChildren<ParticleSystem>(true);
            float maxScheduledDelay = 0f;
            foreach (var ps in systems)
            {
                if (ps == null || !ps.gameObject.activeSelf)
                {
                    continue;
                }

                if (!preserveAuthoredAppearance)
                {
                    var main = ps.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(rainbowGradient);
                }

                if (preserveAuthoredAppearance)
                {
                    PlayAuthoredParticleSystem(ps, ref maxScheduledDelay);
                }
                else
                {
                    PlayPreparedParticleSystem(ps, position, ref maxScheduledDelay);
                }
            }

            StartCoroutine(DespawnAfter(explosion, Mathf.Max(2.5f, maxScheduledDelay + life), attachToGameplayAnchor));
        }

        private static bool UsesAuthoredBombExplosionAppearance(string emitterName, GameObject explosion)
        {
            if (SurgeonEffectsBundleService.UsesBundleAuthoredBombExplosionEmitter(emitterName))
            {
                return true;
            }

            if (explosion == null)
            {
                return false;
            }

            foreach (ParticleSystem particleSystem in explosion.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particleSystem == null)
                {
                    continue;
                }

                string particleName = particleSystem.name ?? string.Empty;
                if (particleName.IndexOf(BundleRegistry.SurgeonExplosionRefs.SeismicShockwaveOuterEmitterName, StringComparison.OrdinalIgnoreCase) >= 0
                    || particleName.IndexOf(BundleRegistry.SurgeonExplosionRefs.SeismicShockwaveInnerEmitterName, StringComparison.OrdinalIgnoreCase) >= 0
                    || particleName.IndexOf(BundleRegistry.SurgeonExplosionRefs.LightningEmitterName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && renderer.renderMode == ParticleSystemRenderMode.Mesh)
                {
                    return true;
                }

                if (particleSystem.trails.enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PlayAuthoredParticleSystem(ParticleSystem particleSystem, ref float maxScheduledDelay)
        {
            if (particleSystem == null)
            {
                return;
            }

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particleSystem.Play(true);

            ParticleSystem.MainModule main = particleSystem.main;
            float startDelay = GetCurveMaximum(main.startDelay, 0f);
            float duration = Mathf.Max(main.duration, 0f);
            float lifetime = GetCurveMaximum(main.startLifetime, 1f);
            float totalTime = startDelay + duration + lifetime;
            if (totalTime > maxScheduledDelay)
            {
                maxScheduledDelay = totalTime;
            }
        }

        private void PlayPreparedParticleSystem(ParticleSystem particleSystem, Vector3 worldPosition, ref float maxScheduledDelay)
        {
            if (particleSystem == null)
            {
                return;
            }

            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float scheduledDelay = 0f;
            if (TryEmitConfiguredBursts(particleSystem, worldPosition, out scheduledDelay))
            {
                if (scheduledDelay > maxScheduledDelay)
                {
                    maxScheduledDelay = scheduledDelay;
                }

                return;
            }

            particleSystem.Play(true);

            var emission = particleSystem.emission;
            bool hasContinuousEmission = emission.enabled
                && (emission.rateOverTime.constantMax > 0f
                    || emission.rateOverTimeMultiplier > 0f
                    || emission.rateOverDistance.constantMax > 0f
                    || emission.rateOverDistanceMultiplier > 0f);

            if (!hasContinuousEmission)
            {
                int fallbackCount = Mathf.Clamp(
                    Mathf.RoundToInt(particleSystem.main.maxParticles * 0.35f),
                    32,
                    220);
                EmitParticles(particleSystem, worldPosition, fallbackCount);
            }
        }

        private static Gradient CreateRainbowGradient()
        {
            Gradient rainbowGradient = new Gradient();
            rainbowGradient.SetKeys(
                new GradientColorKey[] {
                    new GradientColorKey(Color.red, 0.0f),
                    new GradientColorKey(Color.yellow, 0.15f),
                    new GradientColorKey(Color.green, 0.3f),
                    new GradientColorKey(Color.cyan, 0.5f),
                    new GradientColorKey(Color.blue, 0.65f),
                    new GradientColorKey(Color.magenta, 0.8f),
                    new GradientColorKey(Color.red, 1.0f)
                },
                new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
            );
            return rainbowGradient;
        }

        private GameObject GetOrCreateEmitterInstance(string emitterName)
        {
            SurgeonEffectsBundleService.EnsureSurgeonEffectsBundleFresh();

            if (SurgeonEffectsBundleService.UsesBundleAuthoredBombExplosionEmitter(emitterName))
            {
                return SurgeonEffectsBundleService.CreateBombExplosionInstanceFromBundle(emitterName);
            }

            if (!_emitterPools.TryGetValue(emitterName, out Queue<GameObject> pool))
            {
                pool = new Queue<GameObject>();
                _emitterPools[emitterName] = pool;
            }

            while (pool.Count > 0)
            {
                var pooled = pool.Dequeue();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            return SurgeonEffectsBundleService.CreateBombExplosionInstanceFromBundle(emitterName);
        }

        private System.Collections.IEnumerator DespawnAfter(GameObject go, float seconds, bool attachToGameplayAnchor)
        {
            yield return new WaitForSeconds(seconds);
            if (go == null)
            {
                yield break;
            }

            go.SetActive(false);

            string emitterName = ResolveEmitterPoolName(go.name);
            if (SurgeonEffectsBundleService.UsesBundleAuthoredBombExplosionEmitter(emitterName))
            {
                Destroy(go);
                yield break;
            }

            if (attachToGameplayAnchor)
            {
                _pool.Enqueue(go);
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(emitterName))
            {
                if (!_emitterPools.TryGetValue(emitterName, out Queue<GameObject> pool))
                {
                    pool = new Queue<GameObject>();
                    _emitterPools[emitterName] = pool;
                }

                pool.Enqueue(go);
                yield break;
            }

            Destroy(go);
        }

        private static string ResolveEmitterPoolName(string instanceName)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                return null;
            }

            string[] knownEmitters = BundleRegistry.SurgeonExplosionRefs.BombExplosionEmitterNames;

            foreach (string emitterName in knownEmitters)
            {
                if (instanceName.IndexOf(emitterName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return emitterName;
                }
            }

            return null;
        }

        internal bool Prewarm()
        {
            if (_explosionPrefab == null)
            {
                LoadAssetBundle();
                if (_explosionPrefab == null)
                {
                    return false;
                }
            }

            GameObject explosion = GetOrCreateInstance();
            if (explosion == null)
            {
                return false;
            }

            PrepareExplosionInstance(explosion, null);
            explosion.transform.SetParent(null, false);
            explosion.SetActive(false);

            if (!_pool.Contains(explosion))
            {
                _pool.Enqueue(explosion);
            }

            return true;
        }

        private void SetLayerRecursively(GameObject obj, int newLayer)
        {
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        private GameObject GetOrCreateInstance()
        {
            while (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                if (pooled != null) return pooled;
            }

            var newObj = Instantiate(_explosionPrefab);
            newObj.SetActive(false);
            PrepareExplosionInstance(newObj, null);
            return newObj;
        }

        internal static void ClearCachedInstances()
        {
            if (_instance == null)
            {
                return;
            }

            _instance.ClearCachedInstancesInternal();
        }

        private void ClearCachedInstancesInternal()
        {
            foreach (Queue<GameObject> pool in _emitterPools.Values)
            {
                while (pool.Count > 0)
                {
                    GameObject pooled = pool.Dequeue();
                    if (pooled != null)
                    {
                        Destroy(pooled);
                    }
                }
            }

            _emitterPools.Clear();

            while (_pool.Count > 0)
            {
                GameObject pooled = _pool.Dequeue();
                if (pooled != null)
                {
                    Destroy(pooled);
                }
            }

            _referenceBombParticleRenderer = null;
            _gameplayVfxAnchor = null;
        }

        private void PrepareExplosionInstance(GameObject explosion, string emitterName)
        {
            if (explosion == null)
            {
                return;
            }

            if (SurgeonEffectsBundleService.UsesBundleAuthoredBombExplosionEmitter(emitterName)
                || UsesAuthoredBombExplosionAppearance(emitterName, explosion))
            {
                VrVfxMaterialHelper.PrepareBundleBombExplosionForVr(
                    explosion,
                    "FireworksExplosionPool authored instance");
                return;
            }

            VrVfxMaterialHelper.PrepareBombExplosionRenderers(
                explosion,
                "FireworksExplosionPool instance",
                GetReferenceBombParticleRenderer());
        }

        private void AttachToGameplayVfxAnchor(GameObject explosion)
        {
            if (explosion == null)
            {
                return;
            }

            Transform anchor = GetGameplayVfxAnchor();
            if (anchor == null)
            {
                return;
            }

            if (explosion.transform.parent != anchor)
            {
                explosion.transform.SetParent(anchor, false);
            }
        }

        private void SyncToGameplayVfxLayer(GameObject explosion)
        {
            if (explosion == null)
            {
                return;
            }

            Transform parent = explosion.transform.parent;
            if (parent == null)
            {
                return;
            }

            SetLayerRecursively(explosion, parent.gameObject.layer);
        }

        private Transform GetGameplayVfxAnchor()
        {
            if (_gameplayVfxAnchor != null)
            {
                return _gameplayVfxAnchor;
            }

            NoteCutCoreEffectsSpawner spawner = Resources.FindObjectsOfTypeAll<NoteCutCoreEffectsSpawner>().FirstOrDefault();
            if (spawner == null)
            {
                return null;
            }

            BombExplosionEffect bombExplosionEffect = spawner.GetComponentInChildren<BombExplosionEffect>(true);
            _gameplayVfxAnchor = bombExplosionEffect != null
                ? bombExplosionEffect.transform
                : spawner.transform;

            if (_gameplayVfxAnchor != null)
            {
                LogUtils.Debug(() =>
                    "FireworksExplosionPool: Using gameplay VFX anchor '"
                    + GetTransformPath(_gameplayVfxAnchor)
                    + "' layer="
                    + _gameplayVfxAnchor.gameObject.layer
                    + ".");
            }

            return _gameplayVfxAnchor;
        }

        private ParticleSystemRenderer GetReferenceBombParticleRenderer()
        {
            if (_referenceBombParticleRenderer != null)
            {
                return _referenceBombParticleRenderer;
            }

            Transform anchor = _gameplayVfxAnchor != null ? _gameplayVfxAnchor : GetGameplayVfxAnchor();
            if (anchor == null)
            {
                return null;
            }

            BombExplosionEffect bombExplosionEffect = anchor.GetComponent<BombExplosionEffect>();
            if (bombExplosionEffect == null)
            {
                return null;
            }

            var referenceRenderers = bombExplosionEffect.GetComponentsInChildren<ParticleSystemRenderer>(true);
            _referenceBombParticleRenderer = referenceRenderers
                .Where(renderer => GetReferenceRendererScore(renderer) > 0)
                .OrderByDescending(GetReferenceRendererScore)
                .FirstOrDefault()
                ?? referenceRenderers.FirstOrDefault(renderer => renderer != null && renderer.renderMode != ParticleSystemRenderMode.Mesh)
                ?? referenceRenderers.FirstOrDefault();

            if (_referenceBombParticleRenderer != null)
            {
                LogUtils.Debug(() =>
                    "FireworksExplosionPool: Using reference particle renderer '"
                    + GetTransformPath(_referenceBombParticleRenderer.transform)
                    + "' shader='"
                    + (_referenceBombParticleRenderer.sharedMaterial != null && _referenceBombParticleRenderer.sharedMaterial.shader != null
                        ? _referenceBombParticleRenderer.sharedMaterial.shader.name
                        : "<missing>")
                    + "'.");
            }

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

        private static void RebindParticleMaterialShader(ParticleSystemRenderer particleRenderer, ParticleSystemRenderer referenceRenderer)
        {
            if (particleRenderer == null || referenceRenderer == null)
            {
                return;
            }

            Shader referenceShader = referenceRenderer.sharedMaterial != null
                ? referenceRenderer.sharedMaterial.shader
                : null;
            if (referenceShader == null)
            {
                return;
            }

            try
            {
                var materials = particleRenderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || material.shader == referenceShader)
                    {
                        continue;
                    }

                    Texture mainTexture = material.mainTexture;
                    material.shader = referenceShader;
                    if (material.mainTexture == null && mainTexture != null)
                    {
                        material.mainTexture = mainTexture;
                    }
                }

                particleRenderer.sharedMaterials = materials;

                if (particleRenderer.trailMaterial != null && referenceRenderer.trailMaterial != null && referenceRenderer.trailMaterial.shader != null)
                {
                    particleRenderer.trailMaterial.shader = referenceRenderer.trailMaterial.shader;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("FireworksExplosionPool: Failed to rebind particle shader for '" + particleRenderer.name + "': " + ex.Message);
            }
        }

        private static void SyncStereoRendererState(ParticleSystemRenderer particleRenderer, ParticleSystemRenderer referenceRenderer)
        {
            if (particleRenderer == null || referenceRenderer == null)
            {
                return;
            }

            try
            {
                particleRenderer.alignment = referenceRenderer.alignment;
                particleRenderer.normalDirection = referenceRenderer.normalDirection;
                particleRenderer.allowRoll = referenceRenderer.allowRoll;
                particleRenderer.maskInteraction = referenceRenderer.maskInteraction;
                particleRenderer.enableGPUInstancing = referenceRenderer.enableGPUInstancing;
                particleRenderer.sortingFudge = referenceRenderer.sortingFudge;
                particleRenderer.renderingLayerMask = referenceRenderer.renderingLayerMask;
                particleRenderer.lightProbeUsage = referenceRenderer.lightProbeUsage;
                particleRenderer.reflectionProbeUsage = referenceRenderer.reflectionProbeUsage;
                particleRenderer.motionVectorGenerationMode = referenceRenderer.motionVectorGenerationMode;

                var activeVertexStreams = new List<ParticleSystemVertexStream>(referenceRenderer.activeVertexStreamsCount);
                referenceRenderer.GetActiveVertexStreams(activeVertexStreams);
                if (activeVertexStreams.Count > 0)
                {
                    particleRenderer.SetActiveVertexStreams(activeVertexStreams);
                }

                if (particleRenderer.trailMaterial != null)
                {
                    var activeTrailVertexStreams = new List<ParticleSystemVertexStream>(referenceRenderer.activeTrailVertexStreamsCount);
                    referenceRenderer.GetActiveTrailVertexStreams(activeTrailVertexStreams);
                    if (activeTrailVertexStreams.Count > 0)
                    {
                        particleRenderer.SetActiveTrailVertexStreams(activeTrailVertexStreams);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("FireworksExplosionPool: Failed to sync renderer state for '" + particleRenderer.name + "': " + ex.Message);
            }
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
                Plugin.Log.Warn("FireworksExplosionPool: Failed to harden stereo culling for '" + particleRenderer.name + "': " + ex.Message);
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
                float lifetime = GetCurveMaximum(main.startLifetime, 1f);
                float speed = GetCurveMaximum(main.startSpeed, 2f);
                float size = main.startSize3D
                    ? Mathf.Max(
                        GetCurveMaximum(main.startSizeX, 0.5f),
                        GetCurveMaximum(main.startSizeY, 0.5f),
                        GetCurveMaximum(main.startSizeZ, 0.5f))
                    : GetCurveMaximum(main.startSize, 0.5f);

                return Mathf.Clamp((speed * Mathf.Max(0.5f, lifetime)) + (size * 4f) + 2f, 12f, 36f);
            }
            catch
            {
                return 12f;
            }
        }

        private static float GetCurveMaximum(ParticleSystem.MinMaxCurve curve, float fallback)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constantMax;
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(curve.constantMin, curve.constantMax);
                default:
                    return Mathf.Max(fallback, curve.constantMax);
            }
        }

        private bool TryEmitConfiguredBursts(ParticleSystem particleSystem, Vector3 worldPosition, out float maxBurstTime)
        {
            maxBurstTime = 0f;
            if (particleSystem == null)
            {
                return false;
            }

            var emission = particleSystem.emission;
            int burstCount = emission.burstCount;
            if (burstCount <= 0)
            {
                return false;
            }

            var bursts = new ParticleSystem.Burst[burstCount];
            int configuredBursts = emission.GetBursts(bursts);
            if (configuredBursts <= 0)
            {
                return false;
            }

            bool emittedAny = false;
            for (int burstIndex = 0; burstIndex < configuredBursts; burstIndex++)
            {
                var burst = bursts[burstIndex];
                int cycleCount = Mathf.Max(1, burst.cycleCount);
                for (int cycleIndex = 0; cycleIndex < cycleCount; cycleIndex++)
                {
                    float delay = burst.time + (cycleIndex * Mathf.Max(0f, burst.repeatInterval));
                    int particleCount = EvaluateBurstCount(burst);
                    if (particleCount <= 0)
                    {
                        continue;
                    }

                    emittedAny = true;
                    if (delay <= 0f)
                    {
                        EmitParticles(particleSystem, worldPosition, particleCount);
                    }
                    else
                    {
                        StartCoroutine(EmitParticlesAfterDelay(particleSystem, worldPosition, particleCount, delay));
                        if (delay > maxBurstTime)
                        {
                            maxBurstTime = delay;
                        }
                    }
                }
            }

            return emittedAny;
        }

        private static int EvaluateBurstCount(ParticleSystem.Burst burst)
        {
            var count = burst.count;
            switch (count.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return Mathf.RoundToInt(count.constantMax);
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.RoundToInt((count.constantMin + count.constantMax) * 0.5f);
                default:
                    return Mathf.Max(1, burst.maxCount);
            }
        }

        private System.Collections.IEnumerator EmitParticlesAfterDelay(ParticleSystem particleSystem, Vector3 worldPosition, int particleCount, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (particleSystem == null || !particleSystem.gameObject.activeInHierarchy)
            {
                yield break;
            }

            EmitParticles(particleSystem, worldPosition, particleCount);
        }

        private static void EmitParticles(ParticleSystem particleSystem, Vector3 worldPosition, int particleCount)
        {
            if (particleSystem == null || particleCount <= 0)
            {
                return;
            }

            var emitParams = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = true
            };

            var main = particleSystem.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    emitParams.position = particleSystem.transform.InverseTransformPoint(worldPosition);
                    break;
                case ParticleSystemSimulationSpace.Custom:
                    emitParams.position = main.customSimulationSpace != null
                        ? main.customSimulationSpace.InverseTransformPoint(worldPosition)
                        : worldPosition;
                    break;
                default:
                    emitParams.position = worldPosition;
                    break;
            }

            particleSystem.Emit(emitParams, particleCount);
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            return transform.parent == null
                ? transform.name
                : GetTransformPath(transform.parent) + "/" + transform.name;
        }

        // Stubs
        public static void LoadAvailableTextures() { }
        public static List<string> GetAvailableTextureTypes() { return new List<string> { "Default" }; }
        public static void SetTextureType(string t) { }
    }
}
