using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BeatSurgeon.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace BeatSurgeon.Gameplay
{
    internal sealed class BitParticleEmitterPool : MonoBehaviour
    {
        private static readonly LogUtil _log = LogUtil.GetLogger("BitParticleEmitterPool");
        private static readonly int[] SupportedWarmupDenominations = { 10000, 5000, 1000, 100, 1 };
        private const int MaxPoolPerDenomination = 16;
        private const float MaxReturnTravelDistance = 12f;
        private const float ReturnMotionUnitsPerSecond = 5f;
        private const float MinReturnMotionDuration = 0.55f;
        private const float MaxReturnMotionDuration = 1.8f;
        private static bool _loggedBurstMotionSnapshot;
        private static bool _loggedFollowerLineTargetSnapshot;
        private static readonly HashSet<int> _loggedMissingSpecialBranchDenominations = new HashSet<int>();

        private readonly struct BitBurstProfile
        {
            internal BitBurstProfile(string primaryName, string specialName, Color burstColor, float burstSpeed, int burstParticles, int specialEmitCount, bool specialUsesPlay)
            {
                PrimaryName = primaryName;
                SpecialName = specialName;
                BurstColor = burstColor;
                BurstSpeed = burstSpeed;
                BurstParticles = burstParticles;
                SpecialEmitCount = specialEmitCount;
                SpecialUsesPlay = specialUsesPlay;
            }

            internal string PrimaryName { get; }
            internal string SpecialName { get; }
            internal Color BurstColor { get; }
            internal float BurstSpeed { get; }
            internal int BurstParticles { get; }
            internal int SpecialEmitCount { get; }
            internal bool SpecialUsesPlay { get; }
        }

        private static BitParticleEmitterPool _instance;
        private static GameObject _go;

        private readonly Dictionary<int, GameObject> _templateRoots = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Queue<GameObject>> _pools = new Dictionary<int, Queue<GameObject>>();

        private Transform _gameplayVfxAnchor;
        private ParticleSystemRenderer _referenceBombParticleRenderer;

        public static BitParticleEmitterPool Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _go = new GameObject("BeatSurgeonBitParticleEmitterPool");
                UnityEngine.Object.DontDestroyOnLoad(_go);
                _instance = _go.AddComponent<BitParticleEmitterPool>();
                return _instance;
            }
        }

        internal static IReadOnlyList<int> WarmupDenominations => SupportedWarmupDenominations;

        internal bool PrewarmDenomination(int denomination)
        {
            if (!TryEnsureTemplateRoot(denomination, out GameObject templateRoot))
            {
                return false;
            }

            GameObject emitterRoot = GetOrCreateInstance(denomination, templateRoot);
            if (emitterRoot == null)
            {
                return false;
            }

            PrepareEmitterInstance(emitterRoot);
            emitterRoot.transform.SetParent(null, false);
            emitterRoot.SetActive(false);

            Queue<GameObject> pool = GetPool(denomination);
            if (!pool.Contains(emitterRoot) && pool.Count < MaxPoolPerDenomination)
            {
                pool.Enqueue(emitterRoot);
            }

            return true;
        }

        public bool Spawn(int denomination, Vector3 position, Vector3 returnTarget)
        {
            if (!TryEnsureTemplateRoot(denomination, out GameObject templateRoot))
            {
                return false;
            }

            GameObject emitterRoot = GetOrCreateInstance(denomination, templateRoot);
            if (emitterRoot == null)
            {
                return false;
            }

            ResetBurstState(emitterRoot);
            emitterRoot.transform.SetParent(null, false);
            emitterRoot.transform.rotation = Quaternion.identity;
            emitterRoot.transform.localScale = Vector3.one;
            Vector3 spawnAnchorOffset = ResolveEmissionAnchorLocalOffset(emitterRoot, denomination);
            emitterRoot.transform.position = position - spawnAnchorOffset;
            SyncToGameplayVfxLayer(emitterRoot);
            float lifetime = EstimateEmitterLifetime(emitterRoot);
            ConfigureBurstMotion(emitterRoot, position, returnTarget, lifetime);
            emitterRoot.SetActive(true);

            if (!TriggerBitBurst(emitterRoot, denomination))
            {
                emitterRoot.SetActive(false);
                emitterRoot.transform.SetParent(null, false);
                return false;
            }

            StartCoroutine(DespawnAfter(denomination, emitterRoot, lifetime));
            return true;
        }

        internal static Vector3 ResolveReturnTarget(GameNoteController noteController, Vector3 origin)
        {
            if (TryResolveFollowerCanvasLineTarget(origin, out Vector3 followerLineTarget))
            {
                return followerLineTarget;
            }

            if (noteController != null)
            {
                try
                {
                    Vector3 jumpStartTarget = (noteController.worldRotation * noteController.jumpStartPos) - new Vector3(0f, 0.15f, 0f);
                    Vector3 jumpStartDelta = jumpStartTarget - origin;
                    if (IsFinite(jumpStartTarget) && jumpStartDelta.sqrMagnitude > 0.04f)
                    {
                        return jumpStartTarget;
                    }
                }
                catch
                {
                }

                try
                {
                    Vector3 moveVec = noteController.moveVec;
                    if (IsFinite(moveVec) && moveVec.sqrMagnitude > 0.04f)
                    {
                        return origin - moveVec.normalized * 8f;
                    }
                }
                catch
                {
                }
            }

            Vector3 forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            if (!IsFinite(forward) || forward.sqrMagnitude <= 0.04f)
            {
                forward = Vector3.forward;
            }

            return origin + forward.normalized * 8f;
        }

        private static bool TryResolveFollowerCanvasLineTarget(Vector3 origin, out Vector3 target)
        {
            target = Vector3.zero;

            GameObject followerCanvasTemplate = SurgeonEffectsBundleService.GetFollowerCanvasTemplate();
            if (followerCanvasTemplate == null)
            {
                return false;
            }

            Transform lineAnchor = followerCanvasTemplate.transform.Find(BundleRegistry.TwitchControllerRefs.FollowerLineName)
                ?? FindDescendantByNormalizedName(followerCanvasTemplate.transform, BundleRegistry.TwitchControllerRefs.FollowerLineName);
            Transform startAnchor = followerCanvasTemplate.transform.Find(BundleRegistry.TwitchControllerRefs.FollowerStartName)
                ?? FindDescendantByNormalizedName(followerCanvasTemplate.transform, BundleRegistry.TwitchControllerRefs.FollowerStartName);
            Transform endAnchor = followerCanvasTemplate.transform.Find(BundleRegistry.TwitchControllerRefs.FollowerEndName)
                ?? FindDescendantByNormalizedName(followerCanvasTemplate.transform, BundleRegistry.TwitchControllerRefs.FollowerEndName);

            if (startAnchor != null && IsFinite(startAnchor.position))
            {
                target = startAnchor.position;
            }
            else if (lineAnchor != null)
            {
                target = lineAnchor.position;
            }

            if (!IsFinite(target))
            {
                return false;
            }

            if (!_loggedFollowerLineTargetSnapshot)
            {
                _loggedFollowerLineTargetSnapshot = true;
                _log.Info(
                    "Follower start target snapshot"
                    + " | origin=" + origin.ToString("F2")
                    + " line=" + (lineAnchor != null ? lineAnchor.position.ToString("F2") : "<missing>")
                    + " start=" + (startAnchor != null ? startAnchor.position.ToString("F2") : "<missing>")
                    + " end=" + (endAnchor != null ? endAnchor.position.ToString("F2") : "<missing>")
                    + " target=" + target.ToString("F2"));
            }

            return true;
        }

        private bool TryEnsureTemplateRoot(int denomination, out GameObject templateRoot)
        {
            templateRoot = null;
            if (_templateRoots.TryGetValue(denomination, out templateRoot) && templateRoot != null)
            {
                return true;
            }

            var template = SurgeonEffectsBundleService.GetTwitchBitBurstTemplate();
            if (template == null)
            {
                _log.Warn("No bit burst template found for denomination=" + denomination);
                return false;
            }

            templateRoot = template;

            _templateRoots[denomination] = templateRoot;
            return true;
        }

        private GameObject GetOrCreateInstance(int denomination, GameObject templateRoot)
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

            var instance = Instantiate(templateRoot);
            instance.SetActive(false);
            PrepareEmitterInstance(instance);
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

        private void PrepareEmitterInstance(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return;
            }

            ResetBurstState(emitterRoot);
            DisableRejectedBranches(emitterRoot);
            EnsureBurstMotionDriver(emitterRoot);
            DisableBurstAutoplay(emitterRoot);

            ParticleSystemRenderer referenceRenderer = GetReferenceBombParticleRenderer();
            foreach (var renderer in emitterRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is ParticleSystemRenderer particleRenderer))
                {
                    continue;
                }

                RebindParticleMaterialShader(particleRenderer, referenceRenderer);
                SyncStereoRendererState(particleRenderer, referenceRenderer);
                HardenStereoCulling(particleRenderer);
            }
        }

        private static void DisableBurstAutoplay(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return;
            }

            foreach (ParticleSystem particleSystem in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particleSystem == null)
                {
                    continue;
                }

                try
                {
                    var main = particleSystem.main;
                    main.playOnAwake = false;
                }
                catch
                {
                }
            }
        }

        private bool TriggerBitBurst(GameObject emitterRoot, int denomination)
        {
            if (emitterRoot == null)
            {
                return false;
            }

            BitBurstProfile profile = GetBurstProfile(denomination);
            ParticleSystem rootBurst = ResolveRootBurstParticleSystem(emitterRoot);
            ParticleSystem primary = FindParticleSystemByName(emitterRoot.transform, profile.PrimaryName);
            if (primary == null)
            {
                _log.Warn("Primary bit particle system not found for denomination=" + denomination + " name=" + profile.PrimaryName);
                return false;
            }

            if (rootBurst != null)
            {
                ConfigureRootBurstParticleSystem(rootBurst, profile);
            }

            Transform specialRoot = null;
            ParticleSystem special = null;
            if (!string.IsNullOrWhiteSpace(profile.SpecialName))
            {
                specialRoot = FindDescendantByNormalizedName(emitterRoot.transform, profile.SpecialName)
                    ?? FindDescendantByHintTokens(emitterRoot.transform, denomination.ToString(), "special");
                special = specialRoot != null
                    ? specialRoot.GetComponent<ParticleSystem>() ?? specialRoot.GetComponentInChildren<ParticleSystem>(true)
                    : null;

                if (special == null)
                {
                    LogMissingSpecialBranchOnce(emitterRoot, denomination, profile.SpecialName);
                }
            }

            TryEmitParticle(primary, 1);

            if (specialRoot != null && special != null)
            {
                if (profile.SpecialUsesPlay)
                {
                    specialRoot.gameObject.SetActive(true);
                    foreach (var childParticle in specialRoot.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        try
                        {
                            ConfigureAuxiliaryBurstParticleSystem(childParticle, profile);
                            childParticle.gameObject.SetActive(true);
                            childParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                            childParticle.Play(true);
                        }
                        catch { }
                    }
                }
                else if (profile.SpecialEmitCount > 0)
                {
                    ConfigureAuxiliaryBurstParticleSystem(special, profile);
                    TryEmitParticle(special, profile.SpecialEmitCount);
                }
            }

            if (rootBurst != null)
            {
                TryEmitParticle(rootBurst, profile.BurstParticles);
            }

            LogBurstMotionSnapshot(emitterRoot, denomination, primary, profile.SpecialName);

            return true;
        }

        private static void TryEmitParticle(ParticleSystem particleSystem, int count)
        {
            if (particleSystem == null || count <= 0)
            {
                return;
            }

            try
            {
                particleSystem.gameObject.SetActive(true);
                PlayParticleSystem(particleSystem);
                particleSystem.Emit(count);
            }
            catch { }
        }

        private static void PlayParticleSystem(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return;
            }

            try
            {
                particleSystem.gameObject.SetActive(true);
                particleSystem.Play(true);
            }
            catch { }
        }

        private static void ResetBurstState(GameObject emitterRoot)
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

            BeatSurgeonBurstMotionDriver motionDriver = emitterRoot.GetComponent<BeatSurgeonBurstMotionDriver>();
            if (motionDriver != null)
            {
                motionDriver.ResetState();
            }

            Transform subBurstRoot = FindDescendantByNormalizedName(
                emitterRoot.transform,
                BundleRegistry.TwitchControllerRefs.SubBurstEmitterName);
            if (subBurstRoot != null)
            {
                subBurstRoot.gameObject.SetActive(false);
            }

            SetSpecialParticleActive(emitterRoot.transform, BundleRegistry.TwitchControllerRefs.OneBitParticleSpecialName, false);
            SetSpecialParticleActive(emitterRoot.transform, BundleRegistry.TwitchControllerRefs.HundredBitParticleSpecialName, false);
            SetSpecialParticleActive(emitterRoot.transform, BundleRegistry.TwitchControllerRefs.ThousandBitParticleSpecialName, false);
            SetSpecialParticleActive(emitterRoot.transform, BundleRegistry.TwitchControllerRefs.FiveThousandBitParticleSpecialName, false);
            SetSpecialParticleActive(emitterRoot.transform, BundleRegistry.TwitchControllerRefs.TenThousandBitParticleSpecialName, false);
        }

        private static void DisableRejectedBranches(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                string normalizedName = NormalizeSelectionToken(transform.name);
                if (normalizedName.Contains("dragon") || normalizedName.Contains("armature"))
                {
                    transform.gameObject.SetActive(false);
                }
            }
        }

        private static void EnsureBurstMotionDriver(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return;
            }

            if (emitterRoot.GetComponent<BeatSurgeonBurstMotionDriver>() == null)
            {
                emitterRoot.AddComponent<BeatSurgeonBurstMotionDriver>();
            }
        }

        private static void ConfigureBurstMotion(GameObject emitterRoot, Vector3 startPosition, Vector3 returnTarget, float lifetime)
        {
            if (emitterRoot == null)
            {
                return;
            }

            BeatSurgeonBurstMotionDriver motionDriver = emitterRoot.GetComponent<BeatSurgeonBurstMotionDriver>();
            if (motionDriver == null)
            {
                return;
            }

            Vector3 clampedTarget = ClampReturnTarget(startPosition, returnTarget);
            float duration = CalculateReturnMotionDuration(startPosition, clampedTarget, lifetime);
            motionDriver.Configure(startPosition, clampedTarget, duration);
        }

        private static Vector3 ClampReturnTarget(Vector3 startPosition, Vector3 returnTarget)
        {
            Vector3 delta = returnTarget - startPosition;
            if (!IsFinite(returnTarget) || delta.sqrMagnitude <= 0.04f)
            {
                return startPosition;
            }

            float clampedDistance = Mathf.Min(delta.magnitude, MaxReturnTravelDistance);
            return startPosition + (delta.normalized * clampedDistance);
        }

        private static float CalculateReturnMotionDuration(Vector3 startPosition, Vector3 targetPosition, float lifetime)
        {
            float distance = Vector3.Distance(startPosition, targetPosition);
            if (distance <= 0.2f)
            {
                return 0f;
            }

            float unclampedDuration = distance / ReturnMotionUnitsPerSecond;
            float clampedDuration = Mathf.Clamp(unclampedDuration, MinReturnMotionDuration, MaxReturnMotionDuration);
            return Mathf.Min(clampedDuration, Mathf.Max(MinReturnMotionDuration, lifetime - 0.1f));
        }

        private static void LogBurstMotionSnapshot(GameObject emitterRoot, int denomination, ParticleSystem primary, string specialName)
        {
            if (_loggedBurstMotionSnapshot || emitterRoot == null)
            {
                return;
            }

            _loggedBurstMotionSnapshot = true;

            ParticleSystem rootBurst = emitterRoot.GetComponent<ParticleSystem>();
            ParticleSystem special = string.IsNullOrWhiteSpace(specialName)
                ? null
                : FindParticleSystemByName(emitterRoot.transform, specialName);
            Transform subBurst = FindDescendantByNormalizedName(emitterRoot.transform, BundleRegistry.TwitchControllerRefs.SubBurstEmitterName);
            ParticleSystem subBurstParticle = subBurst?.GetComponent<ParticleSystem>()
                ?? subBurst?.GetComponentInChildren<ParticleSystem>(true);
            BeatSurgeonBurstMotionDriver motionDriver = emitterRoot.GetComponent<BeatSurgeonBurstMotionDriver>();

            _log.Info(
                "Burst snapshot denomination=" + denomination
                + " | root=" + DescribeParticleSystem(rootBurst)
                + " | primary=" + DescribeParticleSystem(primary)
                + " | special=" + DescribeParticleSystem(special)
                + " | subBurst=" + DescribeParticleSystem(subBurstParticle)
                + " | motion=" + DescribeMotionDriver(motionDriver));
        }

        private static string DescribeParticleSystem(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return "<missing>";
            }

            try
            {
                ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                string renderMode = renderer != null ? renderer.renderMode.ToString() : "<no-renderer>";
                string simulationSpace = particleSystem.main.simulationSpace.ToString();
                string shapeType = particleSystem.shape.shapeType.ToString();
                return GetTransformPath(particleSystem.transform)
                    + " active=" + particleSystem.gameObject.activeSelf
                    + " isPlaying=" + particleSystem.isPlaying
                    + " playOnAwake=" + particleSystem.main.playOnAwake
                    + " color=" + DescribeStartColor(particleSystem)
                    + " sim=" + simulationSpace
                    + " shape=" + shapeType
                    + " render=" + renderMode;
            }
            catch (Exception ex)
            {
                return particleSystem.name + " error=" + ex.GetType().Name;
            }
        }

        private static string DescribeMotionDriver(BeatSurgeonBurstMotionDriver motionDriver)
        {
            if (motionDriver == null)
            {
                return "<missing>";
            }

            return "enabled=" + motionDriver.enabled
                + " progress=" + motionDriver.Progress.ToString("F2")
                + " start=" + motionDriver.StartPosition.ToString("F2")
                + " target=" + motionDriver.TargetPosition.ToString("F2")
                + " duration=" + motionDriver.Duration.ToString("F2")
                + " systems=" + motionDriver.TrackedSystemCount;
        }

        private static string DescribeStartColor(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return "<missing>";
            }

            try
            {
                Color color = particleSystem.main.startColor.color;
                return "(" + color.r.ToString("F2")
                    + "," + color.g.ToString("F2")
                    + "," + color.b.ToString("F2")
                    + "," + color.a.ToString("F2") + ")";
            }
            catch
            {
                return "<dynamic>";
            }
        }

        private static void SetSpecialParticleActive(Transform root, string particleName, bool active)
        {
            Transform child = FindDescendantByNormalizedName(root, particleName);
            if (child != null)
            {
                child.gameObject.SetActive(active);
            }
        }

        private static ParticleSystem FindParticleSystemByName(Transform root, string particleName)
        {
            Transform child = FindDescendantByNormalizedName(root, particleName);
            if (child == null)
            {
                return null;
            }

            return child.GetComponent<ParticleSystem>() ?? child.GetComponentInChildren<ParticleSystem>(true);
        }

        private static Transform FindDescendantByHintTokens(Transform root, params string[] hintTokens)
        {
            if (root == null || hintTokens == null || hintTokens.Length == 0)
            {
                return null;
            }

            string[] normalizedHints = hintTokens
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .Select(NormalizeSelectionToken)
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();
            if (normalizedHints.Length == 0)
            {
                return null;
            }

            Transform bestMatch = null;
            int bestScore = int.MinValue;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                string normalizedName = NormalizeSelectionToken(transform.name);
                int score = 0;
                bool matchedAllHints = true;
                for (int index = 0; index < normalizedHints.Length; index++)
                {
                    if (!normalizedName.Contains(normalizedHints[index]))
                    {
                        matchedAllHints = false;
                        break;
                    }

                    score += normalizedHints[index].Length;
                }

                if (!matchedAllHints)
                {
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = transform;
                }
            }

            return bestMatch;
        }

        private static Transform FindDescendantByNormalizedName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
            {
                return null;
            }

            string normalizedTarget = NormalizeSelectionToken(targetName);
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (NormalizeSelectionToken(transform.name) == normalizedTarget)
                {
                    return transform;
                }
            }

            return null;
        }

        private static string NormalizeSelectionToken(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }

        private static void ConfigureAuxiliaryBurstParticleSystem(ParticleSystem particleSystem, BitBurstProfile profile)
        {
            if (particleSystem == null)
            {
                return;
            }

            try
            {
                var main = particleSystem.main;
                main.playOnAwake = false;
                main.startColor = profile.BurstColor;
            }
            catch
            {
            }
        }

        private static void LogMissingSpecialBranchOnce(GameObject emitterRoot, int denomination, string expectedSpecialName)
        {
            if (emitterRoot == null || !_loggedMissingSpecialBranchDenominations.Add(denomination))
            {
                return;
            }

            string nearbyBranches = string.Join(
                ", ",
                emitterRoot.transform
                    .GetComponentsInChildren<Transform>(true)
                    .Select(transform => transform.name)
                    .Where(name =>
                    {
                        string normalizedName = NormalizeSelectionToken(name);
                        return normalizedName.Contains(denomination.ToString())
                            || normalizedName.Contains("special")
                            || normalizedName.Contains("subhypercube")
                            || normalizedName.Contains("burst");
                    })
                    .Distinct()
                    .Take(20));

            _log.Warn(
                "Missing special bit burst branch for denomination="
                + denomination
                + " expected='"
                + expectedSpecialName
                + "' root='"
                + emitterRoot.name
                + "' nearby="
                + (string.IsNullOrWhiteSpace(nearbyBranches) ? "<none>" : nearbyBranches));
        }

        private static BitBurstProfile GetBurstProfile(int denomination)
        {
            switch (denomination)
            {
                case 1:
                    return new BitBurstProfile(
                        BundleRegistry.TwitchControllerRefs.OneBitParticleName,
                        BundleRegistry.TwitchControllerRefs.OneBitParticleSpecialName,
                        Color.white,
                        2f,
                        100,
                        1000,
                        false);
                case 100:
                    return new BitBurstProfile(
                        BundleRegistry.TwitchControllerRefs.HundredBitParticleName,
                        BundleRegistry.TwitchControllerRefs.HundredBitParticleSpecialName,
                        new Color(0.5724139f, 0f, 1f, 1f),
                        5f,
                        250,
                        1000,
                        false);
                case 1000:
                    return new BitBurstProfile(
                        BundleRegistry.TwitchControllerRefs.ThousandBitParticleName,
                        BundleRegistry.TwitchControllerRefs.ThousandBitParticleSpecialName,
                        Color.green,
                        10f,
                        750,
                        1000,
                        false);
                case 5000:
                    return new BitBurstProfile(
                        BundleRegistry.TwitchControllerRefs.FiveThousandBitParticleName,
                        BundleRegistry.TwitchControllerRefs.FiveThousandBitParticleSpecialName,
                        Color.blue,
                        15f,
                        2000,
                        1000,
                        false);
                case 10000:
                    return new BitBurstProfile(
                        BundleRegistry.TwitchControllerRefs.TenThousandBitParticleName,
                        BundleRegistry.TwitchControllerRefs.TenThousandBitParticleSpecialName,
                        Color.red,
                        20f,
                        5000,
                        0,
                        true);
                default:
                    return new BitBurstProfile(
                        BundleRegistry.TwitchControllerRefs.OneBitParticleName,
                        BundleRegistry.TwitchControllerRefs.OneBitParticleSpecialName,
                        Color.white,
                        2f,
                        100,
                        1000,
                        false);
            }
        }

        private static ParticleSystem ResolveRootBurstParticleSystem(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return null;
            }

            ParticleSystem rootBurst = emitterRoot.GetComponent<ParticleSystem>();
            if (rootBurst != null)
            {
                return rootBurst;
            }

            return emitterRoot.GetComponentsInChildren<ParticleSystem>(true)
                .FirstOrDefault(ps => ps != null && ps.transform == emitterRoot.transform);
        }

        private static void ConfigureRootBurstParticleSystem(ParticleSystem particleSystem, BitBurstProfile profile)
        {
            if (particleSystem == null)
            {
                return;
            }

            try
            {
                var main = particleSystem.main;
                main.loop = false;
                main.playOnAwake = false;
                main.startColor = profile.BurstColor;
                main.startSpeed = profile.BurstSpeed;
            }
            catch
            {
            }
        }

        private static Vector3 ResolveEmissionAnchorLocalOffset(GameObject emitterRoot, int denomination)
        {
            if (emitterRoot == null)
            {
                return Vector3.zero;
            }

            BitBurstProfile profile = GetBurstProfile(denomination);
            var anchorOffsets = new List<Vector3>(6);

            AppendEmissionAnchor(anchorOffsets, emitterRoot.transform, ResolveRootBurstParticleSystem(emitterRoot));
            AppendEmissionAnchor(anchorOffsets, emitterRoot.transform, FindParticleSystemByName(emitterRoot.transform, profile.PrimaryName));

            if (!string.IsNullOrWhiteSpace(profile.SpecialName))
            {
                AppendEmissionAnchor(anchorOffsets, emitterRoot.transform, FindParticleSystemByName(emitterRoot.transform, profile.SpecialName));
            }

            if (anchorOffsets.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            for (int index = 0; index < anchorOffsets.Count; index++)
            {
                sum += anchorOffsets[index];
            }

            return sum / anchorOffsets.Count;
        }

        private static void AppendEmissionAnchor(List<Vector3> anchorOffsets, Transform root, ParticleSystem particleSystem)
        {
            if (anchorOffsets == null || root == null || particleSystem == null)
            {
                return;
            }

            try
            {
                Vector3 emissionWorldPoint = particleSystem.transform.position;
                var shape = particleSystem.shape;
                emissionWorldPoint = particleSystem.transform.TransformPoint(shape.position);
                if (IsFinite(emissionWorldPoint))
                {
                    anchorOffsets.Add(root.InverseTransformPoint(emissionWorldPoint));
                }
            }
            catch
            {
            }
        }

        private IEnumerator DespawnAfter(int denomination, GameObject emitterRoot, float lifetime)
        {
            yield return new WaitForSeconds(lifetime);

            if (emitterRoot == null)
            {
                yield break;
            }

            foreach (var particleSystem in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                try
                {
                    particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                catch { }
            }

            emitterRoot.SetActive(false);
            emitterRoot.transform.SetParent(null, false);

            Queue<GameObject> pool = GetPool(denomination);
            if (pool.Count < MaxPoolPerDenomination)
            {
                pool.Enqueue(emitterRoot);
            }
            else
            {
                Destroy(emitterRoot);
            }
        }

        private static float EstimateEmitterLifetime(GameObject emitterRoot)
        {
            float maxLifetime = 1.5f;
            if (emitterRoot == null)
            {
                return maxLifetime;
            }

            foreach (var particleSystem in emitterRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                try
                {
                    var main = particleSystem.main;
                    float startDelay = GetCurveMaximum(main.startDelay, 0f);
                    float startLifetime = GetCurveMaximum(main.startLifetime, 0.5f);
                    float duration = Mathf.Max(main.duration, 0.1f);
                    maxLifetime = Mathf.Max(maxLifetime, startDelay + duration + startLifetime + 0.1f);
                }
                catch { }
            }

            return Mathf.Clamp(maxLifetime, 1.5f, 6f);
        }

        private void SyncToGameplayVfxLayer(GameObject emitterRoot)
        {
            if (emitterRoot == null)
            {
                return;
            }

            Transform anchor = GetGameplayVfxAnchor();
            if (anchor == null)
            {
                return;
            }

            SetLayerRecursively(emitterRoot, anchor.gameObject.layer);
        }

        private void SetLayerRecursively(GameObject root, int layer)
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
                    "BitParticleEmitterPool: Using gameplay VFX anchor '"
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
                    "BitParticleEmitterPool: Using reference particle renderer '"
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
            if (shaderName.IndexOf("Custom/CustomParticles", StringComparison.OrdinalIgnoreCase) >= 0) score += 2000;
            if (transformName.IndexOf("ExplosionSparkles", StringComparison.OrdinalIgnoreCase) >= 0) score += 1600;
            if (transformName.IndexOf("Sparkle", StringComparison.OrdinalIgnoreCase) >= 0) score += 1000;
            if (materialName.IndexOf("Sparkle", StringComparison.OrdinalIgnoreCase) >= 0) score += 750;
            if (renderer.renderMode == ParticleSystemRenderMode.Stretch) score += 500;
            if (renderer.renderMode == ParticleSystemRenderMode.Mesh) score -= 1000;
            if (transformName.IndexOf("Debris", StringComparison.OrdinalIgnoreCase) >= 0 || path.EndsWith("/debrisps", StringComparison.OrdinalIgnoreCase)) score -= 1200;
            if (shaderName.IndexOf("NoteHD", StringComparison.OrdinalIgnoreCase) >= 0) score -= 1200;

            return score;
        }

        private static void RebindParticleMaterialShader(ParticleSystemRenderer particleRenderer, ParticleSystemRenderer referenceRenderer)
        {
            if (particleRenderer == null)
            {
                return;
            }

            Shader referenceShader = referenceRenderer != null && referenceRenderer.sharedMaterial != null
                ? referenceRenderer.sharedMaterial.shader
                : null;

            try
            {
                var materials = particleRenderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    return;
                }

                bool changed = false;
                Texture rendererFallbackTexture = referenceRenderer != null && referenceRenderer.sharedMaterial != null
                    ? VrVfxMaterialHelper.GetBestAvailableTexture(referenceRenderer.sharedMaterial)
                    : null;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null)
                    {
                        if (referenceRenderer?.sharedMaterial != null)
                        {
                            Material fallbackMaterial = VrVfxMaterialHelper.CreateSafeParticleMaterial(referenceRenderer.sharedMaterial, rendererFallbackTexture);
                            if (fallbackMaterial != null)
                            {
                                materials[i] = fallbackMaterial;
                                changed = true;
                            }
                        }

                        continue;
                    }

                    if (VrVfxMaterialHelper.HasUsableShader(material))
                    {
                        continue;
                    }

                    Texture fallbackTexture = VrVfxMaterialHelper.GetBestAvailableTexture(material) ?? rendererFallbackTexture;
                    Material safeMaterial = VrVfxMaterialHelper.CreateSafeParticleMaterial(material, fallbackTexture);
                    if (safeMaterial != null)
                    {
                        materials[i] = safeMaterial;
                        changed = true;
                        continue;
                    }

                    if (referenceShader != null)
                    {
                        Texture mainTexture = material.mainTexture;
                        material.shader = referenceShader;
                        if (material.mainTexture == null && mainTexture != null)
                        {
                            material.mainTexture = mainTexture;
                        }

                        changed = true;
                    }
                }

                if (changed)
                {
                    particleRenderer.sharedMaterials = materials;
                }

                if (particleRenderer.trailMaterial != null && !VrVfxMaterialHelper.HasUsableShader(particleRenderer.trailMaterial))
                {
                    Texture fallbackTexture = VrVfxMaterialHelper.GetBestAvailableTexture(particleRenderer.trailMaterial)
                        ?? VrVfxMaterialHelper.GetBestAvailableTexture(referenceRenderer?.trailMaterial)
                        ?? rendererFallbackTexture;
                    Material safeTrailMaterial = VrVfxMaterialHelper.CreateSafeParticleMaterial(particleRenderer.trailMaterial, fallbackTexture);
                    if (safeTrailMaterial != null)
                    {
                        particleRenderer.trailMaterial = safeTrailMaterial;
                    }
                    else if (referenceRenderer?.trailMaterial != null && referenceRenderer.trailMaterial.shader != null)
                    {
                        particleRenderer.trailMaterial.shader = referenceRenderer.trailMaterial.shader;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("BitParticleEmitterPool: Failed to rebind particle shader for '" + particleRenderer.name + "': " + ex.Message);
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
                particleRenderer.maskInteraction = referenceRenderer.maskInteraction;
                particleRenderer.enableGPUInstancing = referenceRenderer.enableGPUInstancing;
                particleRenderer.sortingFudge = referenceRenderer.sortingFudge;
                particleRenderer.renderingLayerMask = referenceRenderer.renderingLayerMask;
                particleRenderer.lightProbeUsage = referenceRenderer.lightProbeUsage;
                particleRenderer.reflectionProbeUsage = referenceRenderer.reflectionProbeUsage;
                particleRenderer.motionVectorGenerationMode = referenceRenderer.motionVectorGenerationMode;

                var currentVertexStreams = new List<ParticleSystemVertexStream>(particleRenderer.activeVertexStreamsCount);
                particleRenderer.GetActiveVertexStreams(currentVertexStreams);
                if (currentVertexStreams.Count == 0)
                {
                    var activeVertexStreams = new List<ParticleSystemVertexStream>(referenceRenderer.activeVertexStreamsCount);
                    referenceRenderer.GetActiveVertexStreams(activeVertexStreams);
                    if (activeVertexStreams.Count > 0)
                    {
                        particleRenderer.SetActiveVertexStreams(activeVertexStreams);
                    }
                }

                if (particleRenderer.trailMaterial != null)
                {
                    var currentTrailVertexStreams = new List<ParticleSystemVertexStream>(particleRenderer.activeTrailVertexStreamsCount);
                    particleRenderer.GetActiveTrailVertexStreams(currentTrailVertexStreams);
                    if (currentTrailVertexStreams.Count == 0)
                    {
                        var activeTrailVertexStreams = new List<ParticleSystemVertexStream>(referenceRenderer.activeTrailVertexStreamsCount);
                        referenceRenderer.GetActiveTrailVertexStreams(activeTrailVertexStreams);
                        if (activeTrailVertexStreams.Count > 0)
                        {
                            particleRenderer.SetActiveTrailVertexStreams(activeTrailVertexStreams);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("BitParticleEmitterPool: Failed to sync renderer state for '" + particleRenderer.name + "': " + ex.Message);
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
                Plugin.Log.Warn("BitParticleEmitterPool: Failed to harden stereo culling for '" + particleRenderer.name + "': " + ex.Message);
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

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y)
                && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z)
                && !float.IsInfinity(value.z);
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            var segments = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                segments.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", segments.ToArray());
        }

        private sealed class BeatSurgeonBurstMotionDriver : MonoBehaviour
        {
            private static readonly string[] TrackedParticleNames =
            {
                NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.OneBitParticleName),
                NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.HundredBitParticleName),
                NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.ThousandBitParticleName),
                NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.FiveThousandBitParticleName),
                NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.TenThousandBitParticleName)
            };

            private sealed class TrackedParticleSystemState
            {
                internal ParticleSystem ParticleSystem;
                internal ParticleSystem.Particle[] Buffer;
            }

            private readonly List<TrackedParticleSystemState> _trackedParticleSystems = new List<TrackedParticleSystemState>();
            private Vector3 _startPosition;
            private Vector3 _targetPosition;
            private float _duration;
            private float _elapsed;
            private bool _hasMotion;

            internal Vector3 StartPosition => _startPosition;
            internal Vector3 TargetPosition => _targetPosition;
            internal float Duration => _duration;
            internal float Progress => _duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);
            internal int TrackedSystemCount => _trackedParticleSystems.Count;

            internal void Configure(Vector3 startPosition, Vector3 targetPosition, float duration)
            {
                _startPosition = startPosition;
                _targetPosition = targetPosition;
                _duration = duration;
                _elapsed = 0f;
                transform.position = startPosition;
                RefreshTrackedParticleSystems();
                _hasMotion = duration > 0f
                    && (targetPosition - startPosition).sqrMagnitude > 0.04f
                    && _trackedParticleSystems.Count > 0;
                enabled = _hasMotion;
            }

            internal void ResetState()
            {
                _startPosition = Vector3.zero;
                _targetPosition = Vector3.zero;
                _duration = 0f;
                _elapsed = 0f;
                _hasMotion = false;
                _trackedParticleSystems.Clear();
                enabled = false;
            }

            private void LateUpdate()
            {
                if (!_hasMotion)
                {
                    enabled = false;
                    return;
                }

                _elapsed += Time.deltaTime;
                float t = _duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / _duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                for (int i = 0; i < _trackedParticleSystems.Count; i++)
                {
                    ApplyParticleAttraction(_trackedParticleSystems[i], eased);
                }

                if (t >= 1f)
                {
                    _hasMotion = false;
                    enabled = false;
                }
            }

            private void RefreshTrackedParticleSystems()
            {
                _trackedParticleSystems.Clear();

                foreach (ParticleSystem particleSystem in GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (particleSystem == null)
                    {
                        continue;
                    }

                    if (!ShouldTrackParticleSystem(particleSystem))
                    {
                        continue;
                    }

                    try
                    {
                        var main = particleSystem.main;
                        if (main.simulationSpace != ParticleSystemSimulationSpace.World)
                        {
                            main.simulationSpace = ParticleSystemSimulationSpace.World;
                        }

                        int maxParticles = Mathf.Max(128, main.maxParticles);
                        _trackedParticleSystems.Add(new TrackedParticleSystemState
                        {
                            ParticleSystem = particleSystem,
                            Buffer = new ParticleSystem.Particle[maxParticles]
                        });
                    }
                    catch
                    {
                    }
                }
            }

            private static bool ShouldTrackParticleSystem(ParticleSystem particleSystem)
            {
                if (particleSystem == null || particleSystem.transform == null)
                {
                    return false;
                }

                string normalizedName = NormalizeSelectionToken(particleSystem.transform.name);
                for (int index = 0; index < TrackedParticleNames.Length; index++)
                {
                    if (normalizedName == TrackedParticleNames[index])
                    {
                        return true;
                    }
                }

                return false;
            }

            private void ApplyParticleAttraction(TrackedParticleSystemState trackedState, float easedProgress)
            {
                ParticleSystem particleSystem = trackedState?.ParticleSystem;
                if (particleSystem == null || !particleSystem.gameObject.activeInHierarchy)
                {
                    return;
                }

                int requiredSize;
                try
                {
                    requiredSize = Mathf.Max(128, particleSystem.main.maxParticles);
                }
                catch
                {
                    requiredSize = trackedState.Buffer != null ? trackedState.Buffer.Length : 128;
                }

                if (trackedState.Buffer == null || trackedState.Buffer.Length < requiredSize)
                {
                    trackedState.Buffer = new ParticleSystem.Particle[requiredSize];
                }

                int particleCount;
                try
                {
                    particleCount = particleSystem.GetParticles(trackedState.Buffer);
                }
                catch
                {
                    return;
                }

                if (particleCount <= 0)
                {
                    return;
                }

                for (int i = 0; i < particleCount; i++)
                {
                    float startLifetime = Mathf.Max(0.01f, trackedState.Buffer[i].startLifetime);
                    float lifeProgress = 1f - (trackedState.Buffer[i].remainingLifetime / startLifetime);
                    float attraction = Mathf.Clamp01(Mathf.Max(lifeProgress, easedProgress));
                    Vector3 particlePosition = trackedState.Buffer[i].position;
                    Vector3 delta = _targetPosition - particlePosition;
                    float distance = delta.magnitude;
                    if (distance <= 0.001f)
                    {
                        continue;
                    }

                    float moveStep = Mathf.Lerp(0.25f, 14f, attraction) * Time.deltaTime;
                    Vector3 direction = delta / distance;
                    trackedState.Buffer[i].position = Vector3.MoveTowards(particlePosition, _targetPosition, moveStep);
                    trackedState.Buffer[i].velocity += direction * (Mathf.Lerp(0.5f, 8f, attraction) * Time.deltaTime);
                }

                try
                {
                    particleSystem.SetParticles(trackedState.Buffer, particleCount);
                }
                catch
                {
                }
            }
        }
    }

}