using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal static class SurgeonEffectsBundleService
    {
        private sealed class ParticleTemplateCandidate
        {
            public GameObject Prefab { get; set; }
            public string AssetName { get; set; }
            public string RelativePath { get; set; }
            public string TemplateRootPath { get; set; }
            public string MaterialName { get; set; }
            public int Score { get; set; }
        }

        private sealed class TwitchControllerReferenceAssets
        {
            public Mesh OutlineMesh { get; set; }
            public Material BitsOutlineMaterial { get; set; }
            public Material SubOutlineMaterial { get; set; }
            public Material OutlineParticleMaterial { get; set; }
            public Material OutlineSubEmitterMaterial { get; set; }
            public Material OutlineBurstMaterial { get; set; }
            public Material TrailMaterial { get; set; }
        }

        private static ParticleSystem _cachedOutlineTemplate;
        private static bool _triedLoad = false;

        public static void ResetCachedTemplate()
        {
            try
            {
                if (_cachedOutlineTemplate != null)
                {
                    var templateRoot = _cachedOutlineTemplate.transform != null && _cachedOutlineTemplate.transform.root != null
                        ? _cachedOutlineTemplate.transform.root.gameObject
                        : _cachedOutlineTemplate.gameObject;
                    if (templateRoot != null)
                    {
                        UnityEngine.Object.Destroy(templateRoot);
                    }
                }
                _cachedOutlineTemplate = null;
                _triedLoad = false;
            }
            catch { }
        }

        public static ParticleSystem GetOutlineParticlesTemplate()
        {
            if (_cachedOutlineTemplate != null) return _cachedOutlineTemplate;
            if (_triedLoad) return _cachedOutlineTemplate;
            _triedLoad = true;

            try
            {
                string bundlePath = Path.Combine(Environment.CurrentDirectory, "UserData", "BeatSurgeon", "Effects", "surgeoneffects");
                if (!File.Exists(bundlePath))
                {
                    Plugin.Log.Warn("SurgeonEffectsBundleService: surgeoneffects bundle not found at " + bundlePath);
                    return null;
                }

                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    Plugin.Log.Warn("SurgeonEffectsBundleService: failed to load asset bundle: " + bundlePath);
                    return null;
                }

                try
                {
                    var cfg = BeatSurgeon.PluginConfig.Instance;
                    if (cfg?.ForceBuiltinOutlineTemplate == true)
                    {
                        LogUtils.Debug(() => "SurgeonEffectsBundleService: ForceBuiltinOutlineTemplate is set; skipping bundle assets.");
                        return null;
                    }

                    string[] assets = bundle.GetAllAssetNames();
                    if (assets == null || assets.Length == 0)
                    {
                        Plugin.Log.Warn("SurgeonEffectsBundleService: surgeoneffects bundle did not expose any assets.");
                        return null;
                    }

                    LogUtils.Debug(() => $"SurgeonEffectsBundleService: surgeoneffects bundle contains {assets.Length} assets.");

                    string preferredAssetName = (cfg?.PreferredOutlineAssetName ?? string.Empty).Trim();
                    string preferredEmitterName = NormalizeSelectionToken(cfg?.PreferredOutlineEmitterName);
                    if (string.IsNullOrWhiteSpace(preferredEmitterName))
                    {
                        preferredEmitterName = "outlineparticles";
                    }

                    LogUtils.Debug(() => $"SurgeonEffectsBundleService: preferred emitter selector='{preferredEmitterName}' asset='{preferredAssetName}'.");

                    var referencedTemplate = TryCreateReferencedOutlineTemplate(bundle, assets, preferredAssetName, preferredEmitterName, cfg);
                    if (referencedTemplate != null)
                    {
                        _cachedOutlineTemplate = referencedTemplate;
                        return _cachedOutlineTemplate;
                    }

                    if (!string.IsNullOrWhiteSpace(preferredAssetName) && !preferredAssetName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        var directEmitter = TryLoadLooseEmitter(bundle, preferredAssetName);
                        if (directEmitter != null)
                        {
                            _cachedOutlineTemplate = directEmitter;
                            return _cachedOutlineTemplate;
                        }
                    }

                    var prefabAssets = assets
                        .Where(a => a.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (prefabAssets.Length == 0)
                    {
                        Plugin.Log.Warn("SurgeonEffectsBundleService: no prefab assets found in surgeoneffects bundle.");
                        return null;
                    }

                    LogUtils.Debug(() => "SurgeonEffectsBundleService: prefab assets sample: " + string.Join(", ", prefabAssets.Take(8)));

                    ParticleTemplateCandidate bestCandidate = null;
                    foreach (var assetName in BuildAssetCandidateList(prefabAssets, preferredAssetName))
                    {
                        try
                        {
                            LogUtils.Debug(() => $"SurgeonEffectsBundleService: attempting candidate asset '{assetName}'");
                            var prefab = bundle.LoadAsset<GameObject>(assetName);
                            if (prefab == null)
                            {
                                continue;
                            }

                            var candidate = BuildBestPrefabCandidate(prefab, assetName, preferredEmitterName);
                            if (candidate == null)
                            {
                                LogUtils.Debug(() => $"SurgeonEffectsBundleService: asset '{assetName}' contains no usable particle emitters.");
                                continue;
                            }

                            LogUtils.Debug(() =>
                                $"SurgeonEffectsBundleService: asset '{assetName}' best emitter '{DisplayRelativePath(candidate.RelativePath)}' templateRoot='{DisplayRelativePath(candidate.TemplateRootPath)}' material='{candidate.MaterialName}' score={candidate.Score}");

                            if (bestCandidate == null || candidate.Score > bestCandidate.Score)
                            {
                                bestCandidate = candidate;
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Warn($"SurgeonEffectsBundleService: failed to inspect candidate '{assetName}': {ex.Message}");
                        }
                    }

                    if (bestCandidate != null)
                    {
                        var instance = UnityEngine.Object.Instantiate(bestCandidate.Prefab);
                        UnityEngine.Object.DontDestroyOnLoad(instance);
                        instance.SetActive(false);

                        var ps = ResolveParticleSystem(instance, bestCandidate.RelativePath, preferredEmitterName);
                        if (ps != null)
                        {
                            var standaloneTemplate = CreateStandaloneEmitterTemplate(ps, bestCandidate.AssetName, bestCandidate.RelativePath, bestCandidate.TemplateRootPath, bestCandidate.MaterialName, bestCandidate.Score);
                            UnityEngine.Object.Destroy(instance);
                            if (standaloneTemplate != null)
                            {
                                _cachedOutlineTemplate = standaloneTemplate;
                                return _cachedOutlineTemplate;
                            }

                            Plugin.Log.Warn(
                                $"SurgeonEffectsBundleService: failed to build standalone emitter from asset='{bestCandidate.AssetName}' path='{DisplayRelativePath(bestCandidate.RelativePath)}'.");
                            return null;
                        }

                        UnityEngine.Object.Destroy(instance);
                        Plugin.Log.Warn(
                            $"SurgeonEffectsBundleService: selected emitter path '{DisplayRelativePath(bestCandidate.RelativePath)}' could not be resolved on instantiated asset '{bestCandidate.AssetName}'.");
                    }

                    LogUtils.Debug(() => "SurgeonEffectsBundleService: no suitable outline emitter found in surgeoneffects bundle.");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn("SurgeonEffectsBundleService: error scanning bundle: " + ex.Message);
                }
                finally
                {
                    try { bundle.Unload(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("SurgeonEffectsBundleService: unexpected error: " + ex.Message);
            }

            return _cachedOutlineTemplate;
        }

        private static IEnumerable<string> BuildAssetCandidateList(IEnumerable<string> prefabAssets, string preferredAssetName)
        {
            var ordered = prefabAssets
                .OrderByDescending(ComputeAssetScore)
                .ToList();

            if (string.IsNullOrWhiteSpace(preferredAssetName))
            {
                return ordered;
            }

            string preferred = preferredAssetName.Trim();
            string matched = ordered.FirstOrDefault(a => string.Equals(a, preferred, StringComparison.OrdinalIgnoreCase))
                ?? ordered.FirstOrDefault(a => a.EndsWith(preferred, StringComparison.OrdinalIgnoreCase))
                ?? ordered.FirstOrDefault(a => a.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);

            if (string.IsNullOrWhiteSpace(matched))
            {
                LogUtils.Debug(() => $"SurgeonEffectsBundleService: preferred asset '{preferred}' not found in bundle asset list.");
                return ordered;
            }

            return new[] { matched }.Concat(ordered.Where(a => !string.Equals(a, matched, StringComparison.OrdinalIgnoreCase)));
        }

        private static int ComputeAssetScore(string assetName)
        {
            string lower = (assetName ?? string.Empty).ToLowerInvariant();
            int score = 0;
            if (lower.Contains("outline")) score += 200;
            if (lower.Contains("twitchcontroller")) score += 150;
            if (lower.Contains("assetbundlemap")) score += 100;
            if (lower.Contains("particle")) score += 25;
            if (lower.Contains("surgeonexplosion")) score -= 400;
            if (lower.Contains("explosion")) score -= 200;
            return score;
        }

        private static ParticleTemplateCandidate BuildBestPrefabCandidate(GameObject prefab, string assetName, string preferredEmitterName)
        {
            if (prefab == null)
            {
                return null;
            }

            ParticleTemplateCandidate best = null;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null)
                {
                    continue;
                }

                string relativePath = GetRelativePath(prefab.transform, ps.transform);
                var templateRoot = DetermineTemplateRoot(ps.transform, prefab.transform, ps);
                string templateRootPath = GetRelativePath(prefab.transform, templateRoot);
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                string materialName = renderer?.sharedMaterial?.name ?? string.Empty;
                int score = ComputeParticleScore(assetName, relativePath, materialName, preferredEmitterName, ps);

                if (!string.IsNullOrWhiteSpace(templateRootPath) && !string.Equals(templateRootPath, relativePath, StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }

                if (best == null || score > best.Score)
                {
                    best = new ParticleTemplateCandidate
                    {
                        Prefab = prefab,
                        AssetName = assetName,
                        RelativePath = relativePath,
                        TemplateRootPath = templateRootPath,
                        MaterialName = materialName,
                        Score = score
                    };
                }
            }

            return best;
        }

        private static int ComputeParticleScore(string assetName, string relativePath, string materialName, string preferredEmitterName, ParticleSystem particleSystem)
        {
            string haystack = $"{assetName}|{relativePath}|{materialName}|{particleSystem.gameObject.name}".ToLowerInvariant();
            int score = 0;

            if (!string.IsNullOrWhiteSpace(preferredEmitterName) && haystack.Contains(preferredEmitterName))
            {
                score += 2500;
            }

            if (haystack.Contains("outlineparticles")) score += 1500;
            if (haystack.Contains("outline")) score += 400;
            if (haystack.Contains("circleglow")) score += 250;
            if (haystack.Contains("glow")) score += 150;
            if (haystack.Contains("notecircle")) score += 120;
            if (haystack.Contains("sparkle")) score += 100;
            if (haystack.Contains("trail")) score += 80;
            if (haystack.Contains("subemittor")) score += 60;
            if (haystack.Contains("subhypercube")) score += 40;
            if (haystack.Contains("bitshypercube")) score += 40;
            if (haystack.Contains("particle")) score += 30;

            if (haystack.Contains("surgeonexplosion")) score -= 1200;
            if (haystack.Contains("explosion")) score -= 1000;
            if (haystack.Contains("bomb")) score -= 400;
            if (haystack.Contains("dragon")) score -= 300;
            if (haystack.Contains("heart")) score -= 250;
            if (haystack.Contains("firework")) score -= 250;
            if (haystack.Contains("burst")) score -= 200;
            if (haystack.Contains("subscriber")) score -= 150;
            if (haystack.Contains("follower")) score -= 150;
            if (haystack.Contains("donor")) score -= 150;
            if (haystack.Contains("message")) score -= 150;

            try
            {
                if (particleSystem.main.loop) score += 40;
            }
            catch { }

            try
            {
                if (particleSystem.emission.rateOverTimeMultiplier > 0f) score += 30;
            }
            catch { }

            try
            {
                var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
                if (renderer?.sharedMaterial != null) score += 20;
                if (renderer?.sharedMaterial?.mainTexture != null) score += 20;
            }
            catch { }

            return score;
        }

        private static ParticleSystem ResolveParticleSystem(GameObject rootInstance, string relativePath, string preferredEmitterName)
        {
            if (rootInstance == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                var child = rootInstance.transform.Find(relativePath);
                if (child != null)
                {
                    return child.GetComponent<ParticleSystem>() ?? child.GetComponentInChildren<ParticleSystem>(true);
                }
            }

            if (!string.IsNullOrWhiteSpace(preferredEmitterName))
            {
                var normalized = NormalizeSelectionToken(preferredEmitterName);
                foreach (var transform in rootInstance.GetComponentsInChildren<Transform>(true))
                {
                    if (NormalizeSelectionToken(transform.name) != normalized)
                    {
                        continue;
                    }

                    return transform.GetComponent<ParticleSystem>() ?? transform.GetComponentInChildren<ParticleSystem>(true);
                }
            }

            return rootInstance.GetComponent<ParticleSystem>() ?? rootInstance.GetComponentInChildren<ParticleSystem>(true);
        }

        private static ParticleSystem TryLoadLooseEmitter(AssetBundle bundle, string directAssetName)
        {
            if (bundle == null || string.IsNullOrWhiteSpace(directAssetName))
            {
                return null;
            }

            try
            {
                string directName = directAssetName.Trim();
                var directAsset = bundle.LoadAsset<GameObject>(directName);
                if (directAsset != null)
                {
                    var directInstance = UnityEngine.Object.Instantiate(directAsset);
                    UnityEngine.Object.DontDestroyOnLoad(directInstance);
                    directInstance.SetActive(false);
                    var directParticle = directInstance.GetComponent<ParticleSystem>() ?? directInstance.GetComponentInChildren<ParticleSystem>(true);
                    if (directParticle != null)
                    {
                        var template = CreateStandaloneEmitterTemplate(directParticle, directName, directAsset.name, string.Empty, directParticle.GetComponent<ParticleSystemRenderer>()?.sharedMaterial?.name ?? string.Empty, 9999);
                        UnityEngine.Object.Destroy(directInstance);
                        if (template != null)
                        {
                            LogUtils.Debug(() => $"SurgeonEffectsBundleService: loaded direct emitter asset '{directName}'.");
                            return template;
                        }
                    }

                    UnityEngine.Object.Destroy(directInstance);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"SurgeonEffectsBundleService: failed loading loose emitter '{directAssetName}': {ex.Message}");
            }

            return null;
        }

        private static ParticleSystem TryCreateReferencedOutlineTemplate(AssetBundle bundle, string[] assetNames, string preferredAssetName, string preferredEmitterName, PluginConfig cfg)
        {
            if (!ShouldUseReferencedOutlineTemplate(preferredAssetName, preferredEmitterName))
            {
                return null;
            }

            try
            {
                var refs = LoadTwitchControllerReferenceAssets(bundle, assetNames);
                if (refs?.OutlineParticleMaterial == null)
                {
                    Plugin.Log.Warn(
                        $"SurgeonEffectsBundleService: direct TwitchController references incomplete. particleMat='{refs?.OutlineParticleMaterial?.name ?? "<missing>"}'. Falling back to prefab scan.");
                    return null;
                }

                LogUtils.Debug(() =>
                    $"SurgeonEffectsBundleService: TwitchController refs materials='{refs.BitsOutlineMaterial.name},{refs.SubOutlineMaterial?.name ?? "<missing>"},{refs.OutlineParticleMaterial.name},{refs.OutlineSubEmitterMaterial?.name ?? "<missing>"},{refs.OutlineBurstMaterial?.name ?? "<missing>"},{refs.TrailMaterial?.name ?? "<missing>"}' emitters='{string.Join(",", BundleRegistry.TwitchControllerRefs.EmitterAndSubEmitterNames)}'.");

                string twitchControllerAssetName = ResolveBundleAssetName(assetNames, BundleRegistry.PrefabTwitchController);
                var twitchControllerPrefab = string.IsNullOrWhiteSpace(twitchControllerAssetName)
                    ? null
                    : bundle.LoadAsset<GameObject>(twitchControllerAssetName);
                if (twitchControllerPrefab == null)
                {
                    Plugin.Log.Warn("SurgeonEffectsBundleService: could not load TwitchController prefab for exact outline emitter path.");
                    return null;
                }

                var outlineRootAsset = twitchControllerPrefab.transform.Find(BundleRegistry.TwitchControllerRefs.BitsOutlineRootPath);
                if (outlineRootAsset == null)
                {
                    Plugin.Log.Warn($"SurgeonEffectsBundleService: exact outline root '{BundleRegistry.TwitchControllerRefs.BitsOutlineRootPath}' was not found on the loaded TwitchController prefab asset.");
                    return null;
                }

                try
                {
                    var templateRoot = UnityEngine.Object.Instantiate(outlineRootAsset.gameObject);
                    UnityEngine.Object.DontDestroyOnLoad(templateRoot);
                    templateRoot.transform.SetParent(null, false);
                    templateRoot.name = $"BeatSurgeonOutlineTemplate_{BundleRegistry.TwitchControllerRefs.OutlineNodeName}";
                    templateRoot.SetActive(false);

                    var meshRenderer = templateRoot.GetComponent<MeshRenderer>();
                    bool showOutlineRenderer = IsOutlineRendererVisible(cfg);
                    foreach (var renderer in templateRoot.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer == null)
                        {
                            continue;
                        }

                        if (renderer is ParticleSystemRenderer)
                        {
                            renderer.enabled = true;
                            renderer.forceRenderingOff = false;
                            continue;
                        }

                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                        renderer.forceRenderingOff = !showOutlineRenderer;
                        renderer.enabled = showOutlineRenderer;
                    }

                    if (meshRenderer != null && showOutlineRenderer)
                    {
                        meshRenderer.sharedMaterial = CreateAdjustedOutlineMaterial(meshRenderer.sharedMaterial ?? refs.BitsOutlineMaterial, cfg);
                    }

                    string emitterRelativePath = RemoveRootPrefix(
                        BundleRegistry.TwitchControllerRefs.BitsOutlineEmitterPath,
                        BundleRegistry.TwitchControllerRefs.BitsOutlineRootPath);
                    var particleSystem = ResolveParticleSystem(templateRoot, emitterRelativePath, NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.OutlineEmitterName));
                    if (particleSystem == null)
                    {
                        UnityEngine.Object.Destroy(templateRoot);
                        Plugin.Log.Warn($"SurgeonEffectsBundleService: exact outline emitter '{BundleRegistry.TwitchControllerRefs.OutlineEmitterName}' was not found in cloned outline subtree.");
                        return null;
                    }

                    ConfigurePrefabOutlineParticles(particleSystem, meshRenderer, refs.OutlineParticleMaterial, cfg);
                    foreach (var clonedParticleSystem in templateRoot.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        try
                        {
                            clonedParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        }
                        catch { }
                    }

                    LogUtils.Debug(() =>
                        $"SurgeonEffectsBundleService: using exact prefab outline emitter path='{BundleRegistry.TwitchControllerRefs.BitsOutlineEmitterPath}' root='{BundleRegistry.TwitchControllerRefs.BitsOutlineRootPath}' outlineRendererVisible={(meshRenderer != null && meshRenderer.enabled && !meshRenderer.forceRenderingOff ? "true" : "false")} hiddenNonParticleRenderers={templateRoot.GetComponentsInChildren<Renderer>(true).Count(r => r != null && !(r is ParticleSystemRenderer) && (!r.enabled || r.forceRenderingOff))} outlineMaterial='{meshRenderer?.sharedMaterial?.name ?? "<missing>"}' particleMaterial='{particleSystem.GetComponent<ParticleSystemRenderer>()?.sharedMaterial?.name}' density={Mathf.Max(0.1f, cfg?.OutlineParticleDensityMultiplier ?? 1f):0.##} brightness={GetConfiguredOutlineBrightness(cfg):0.##} alpha={GetConfiguredOutlineAlpha(cfg):0.##}.");

                    return particleSystem;
                }
                catch
                {
                    throw;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"SurgeonEffectsBundleService: failed building direct TwitchController outline template: {ex.Message}");
                return null;
            }
        }

        private static bool ShouldUseReferencedOutlineTemplate(string preferredAssetName, string preferredEmitterName)
        {
            string normalizedAsset = NormalizeSelectionToken(preferredAssetName);
            string normalizedEmitter = NormalizeSelectionToken(preferredEmitterName);

            bool assetMatches = string.IsNullOrWhiteSpace(normalizedAsset)
                || normalizedAsset == NormalizeSelectionToken(BundleRegistry.PrefabTwitchController)
                || normalizedAsset.EndsWith("twitchcontroller.prefab", StringComparison.OrdinalIgnoreCase)
                || normalizedAsset.Contains("twitchcontroller");

            bool emitterMatches = string.IsNullOrWhiteSpace(normalizedEmitter)
                || normalizedEmitter == NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.OutlineEmitterName)
                || normalizedEmitter.EndsWith(NormalizeSelectionToken(BundleRegistry.TwitchControllerRefs.OutlineEmitterName), StringComparison.OrdinalIgnoreCase);

            return assetMatches && emitterMatches;
        }

        private static TwitchControllerReferenceAssets LoadTwitchControllerReferenceAssets(AssetBundle bundle, string[] assetNames)
        {
            return new TwitchControllerReferenceAssets
            {
                OutlineMesh = TryLoadAssetByCandidates<Mesh>(bundle, assetNames, BundleRegistry.TwitchControllerRefs.OutlineMeshCandidates),
                BitsOutlineMaterial = TryLoadAssetByCandidates<Material>(bundle, assetNames, BundleRegistry.MatBitsHyperCube),
                SubOutlineMaterial = TryLoadAssetByCandidates<Material>(bundle, assetNames, BundleRegistry.MatSubHyperCube),
                OutlineParticleMaterial = TryLoadAssetByCandidates<Material>(bundle, assetNames, BundleRegistry.TwitchControllerRefs.OutlineParticleMaterialCandidates),
                OutlineSubEmitterMaterial = TryLoadAssetByCandidates<Material>(bundle, assetNames, BundleRegistry.TwitchControllerRefs.OutlineSubEmitterMaterialCandidates),
                OutlineBurstMaterial = TryLoadAssetByCandidates<Material>(bundle, assetNames, BundleRegistry.MatSaberBurnMarkSparkle),
                TrailMaterial = TryLoadAssetByCandidates<Material>(bundle, assetNames, BundleRegistry.MatTrail)
            };
        }

        private static T TryLoadAssetByCandidates<T>(AssetBundle bundle, string[] assetNames, params string[] candidates) where T : UnityEngine.Object
        {
            if (bundle == null || candidates == null || candidates.Length == 0)
            {
                return null;
            }

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    var asset = bundle.LoadAsset<T>(candidate);
                    if (asset != null)
                    {
                        return asset;
                    }

                    string match = assetNames?.FirstOrDefault(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase))
                        ?? assetNames?.FirstOrDefault(a => a.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrWhiteSpace(match))
                    {
                        asset = bundle.LoadAsset<T>(match);
                        if (asset != null)
                        {
                            return asset;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static string ResolveBundleAssetName(IEnumerable<string> assetNames, params string[] candidates)
        {
            if (assetNames == null || candidates == null)
            {
                return null;
            }

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var match = assetNames.FirstOrDefault(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase))
                    ?? assetNames.FirstOrDefault(a => a.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(match))
                {
                    return match;
                }
            }

            return null;
        }

        private static string GetRelativePath(Transform root, Transform child)
        {
            if (root == null || child == null)
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

        private static string DisplayRelativePath(string relativePath)
        {
            return string.IsNullOrWhiteSpace(relativePath) ? "<root>" : relativePath;
        }

        private static string NormalizeSelectionToken(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Replace("\\", "/").ToLowerInvariant();
        }

        private static Material CreateAdjustedOutlineMaterial(Material source, PluginConfig cfg)
        {
            if (source == null)
            {
                return null;
            }

            var clone = new Material(source)
            {
                name = $"{source.name}_BeatSurgeonOutline"
            };

            float brightness = GetConfiguredOutlineBrightness(cfg);
            float alpha = GetConfiguredOutlineAlpha(cfg);

            ApplyScaledMaterialColor(clone, "_Color", brightness, alpha);
            ApplyScaledMaterialColor(clone, "_BaseColor", brightness, alpha);
            ApplyScaledMaterialColor(clone, "_TintColor", brightness, alpha);
            ApplyScaledMaterialColor(clone, "_EmissionColor", brightness, alpha);

            try
            {
                if (alpha < 0.999f && clone.HasProperty("_Mode"))
                {
                    clone.SetFloat("_Mode", 2f);
                }

                if (clone.HasProperty("_SrcBlend")) clone.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (clone.HasProperty("_DstBlend")) clone.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (clone.HasProperty("_ZWrite")) clone.SetFloat("_ZWrite", 0f);
                clone.DisableKeyword("_ALPHATEST_ON");
                clone.EnableKeyword("_ALPHABLEND_ON");
                clone.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                clone.renderQueue = 3000;
            }
            catch { }

            return clone;
        }

        private static Material CreateAdjustedOutlineParticleMaterial(Material source)
        {
            if (source == null)
            {
                return null;
            }

            var clone = new Material(source)
            {
                name = $"{source.name}_BeatSurgeonOutlineParticles"
            };

            try
            {
                if (clone.HasProperty("_EnableCloseToCameraDisappear")) clone.SetFloat("_EnableCloseToCameraDisappear", 0f);
                if (clone.HasProperty("_ZWrite")) clone.SetFloat("_ZWrite", 0f);
                if (clone.HasProperty("_SrcBlend")) clone.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (clone.HasProperty("_DstBlend")) clone.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                if (clone.HasProperty("_TintColor")) clone.SetColor("_TintColor", Color.white);
                clone.renderQueue = 3100;
            }
            catch { }

            return clone;
        }

        private static void ApplyScaledMaterialColor(Material material, string propertyName, float brightness, float alpha)
        {
            if (material == null || !material.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                var color = material.GetColor(propertyName);
                material.SetColor(propertyName, new Color(color.r * brightness, color.g * brightness, color.b * brightness, color.a * alpha));
            }
            catch { }
        }

        private static float GetConfiguredOutlineBrightness(PluginConfig cfg)
        {
            return Mathf.Clamp01(cfg?.OutlineMaterialBrightness ?? 0f);
        }

        private static float GetConfiguredOutlineAlpha(PluginConfig cfg)
        {
            return Mathf.Clamp01(cfg?.OutlineMaterialAlpha ?? 0f);
        }

        private static bool IsOutlineRendererVisible(PluginConfig cfg)
        {
            return (cfg?.OutlineMaterialVisibilityEnabled ?? false)
                && GetConfiguredOutlineBrightness(cfg) > 0f
                && GetConfiguredOutlineAlpha(cfg) > 0f;
        }

        private static void ConfigurePrefabOutlineParticles(ParticleSystem particleSystem, MeshRenderer outlineRenderer, Material particleMaterialSource, PluginConfig cfg)
        {
            if (particleSystem == null)
            {
                return;
            }

            float density = Mathf.Max(0.1f, cfg?.OutlineParticleDensityMultiplier ?? 1f);

            var main = particleSystem.main;
            main.prewarm = true;
            main.maxParticles = Mathf.Max(main.maxParticles, Mathf.RoundToInt(main.maxParticles * density));

            var emission = particleSystem.emission;
            emission.rateOverTimeMultiplier *= density;

            try
            {
                var shape = particleSystem.shape;
                if (shape.shapeType == ParticleSystemShapeType.MeshRenderer && outlineRenderer != null)
                {
                    shape.meshRenderer = outlineRenderer;
                }
                shape.normalOffset = Mathf.Max(shape.normalOffset, 0.02f);
            }
            catch { }

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sortingFudge = 8f;
                if (renderer.sharedMaterial == null && particleMaterialSource != null)
                {
                    renderer.sharedMaterial = particleMaterialSource;
                }
            }
        }

        private static ParticleSystem CreateStandaloneEmitterTemplate(ParticleSystem source, string assetName, string relativePath, string templateRootPath, string materialName, int score)
        {
            if (source == null)
            {
                return null;
            }

            try
            {
                var cloneSource = DetermineTemplateRoot(source.transform, source.transform.root, source);
                var clone = UnityEngine.Object.Instantiate(cloneSource.gameObject);
                UnityEngine.Object.DontDestroyOnLoad(clone);
                clone.transform.SetParent(null, false);
                clone.name = $"BeatSurgeonOutlineTemplate_{cloneSource.name}";
                clone.SetActive(false);

                foreach (var particleSystem in clone.GetComponentsInChildren<ParticleSystem>(true))
                {
                    try
                    {
                        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    catch { }
                }

                string cloneRelativePath = string.IsNullOrWhiteSpace(templateRootPath)
                    ? string.Empty
                    : RemoveRootPrefix(relativePath, templateRootPath);
                var template = ResolveParticleSystem(clone, cloneRelativePath, NormalizeSelectionToken(source.gameObject.name));
                if (template != null)
                {
                    LogUtils.Debug(() =>
                        $"SurgeonEffectsBundleService: selected outline emitter asset='{assetName}' path='{DisplayRelativePath(relativePath)}' templateRoot='{DisplayRelativePath(templateRootPath)}' material='{materialName}' score={score} instanceRoot='{clone.name}'.");
                    return template;
                }

                UnityEngine.Object.Destroy(clone);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"SurgeonEffectsBundleService: failed to clone standalone emitter template: {ex.Message}");
            }

            return null;
        }

        private static Transform DetermineTemplateRoot(Transform particleTransform, Transform prefabRoot, ParticleSystem particleSystem)
        {
            Transform result = particleTransform;
            if (particleSystem == null)
            {
                return result;
            }

            try
            {
                var shape = particleSystem.shape;
                switch (shape.shapeType)
                {
                    case ParticleSystemShapeType.MeshRenderer:
                        if (shape.meshRenderer != null)
                        {
                            result = FindCommonAncestor(particleTransform, shape.meshRenderer.transform, prefabRoot);
                        }
                        break;
                    case ParticleSystemShapeType.SkinnedMeshRenderer:
                        if (shape.skinnedMeshRenderer != null)
                        {
                            result = FindCommonAncestor(particleTransform, shape.skinnedMeshRenderer.transform, prefabRoot);
                        }
                        break;
                    case ParticleSystemShapeType.SpriteRenderer:
                        if (shape.spriteRenderer != null)
                        {
                            result = FindCommonAncestor(particleTransform, shape.spriteRenderer.transform, prefabRoot);
                        }
                        break;
                }
            }
            catch { }

            if (result == particleTransform)
            {
                var current = particleTransform.parent;
                while (current != null && current != prefabRoot)
                {
                    if (current.GetComponent<MeshRenderer>() != null || current.GetComponent<MeshFilter>() != null || current.GetComponent<SkinnedMeshRenderer>() != null || current.GetComponent<SpriteRenderer>() != null)
                    {
                        result = current;
                        break;
                    }
                    current = current.parent;
                }
            }

            return result ?? particleTransform;
        }

        private static Transform FindCommonAncestor(Transform first, Transform second, Transform fallbackRoot)
        {
            if (first == null)
            {
                return second ?? fallbackRoot;
            }

            if (second == null)
            {
                return first;
            }

            var visited = new HashSet<Transform>();
            var current = first;
            while (current != null)
            {
                visited.Add(current);
                if (current == fallbackRoot)
                {
                    break;
                }
                current = current.parent;
            }

            current = second;
            while (current != null)
            {
                if (visited.Contains(current))
                {
                    return current;
                }
                if (current == fallbackRoot)
                {
                    break;
                }
                current = current.parent;
            }

            return fallbackRoot ?? first;
        }

        private static string RemoveRootPrefix(string relativePath, string templateRootPath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(templateRootPath))
            {
                return relativePath;
            }

            if (string.Equals(relativePath, templateRootPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var prefix = templateRootPath + "/";
            if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return relativePath.Substring(prefix.Length);
            }

            return relativePath;
        }
    }
}
