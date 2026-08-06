using System;
using System.Collections.Generic;
using System.Linq;
using IPA.Loader;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    /// <summary>
    /// BeatSurgeon-owned gameplay VFX space that never parents under
    /// vanilla <see cref="NoteCutCoreEffectsSpawner"/> / <see cref="BombExplosionEffect"/>.
    /// ParticleOverdrive and other note-cut particle mods therefore do not share our hierarchy.
    /// SPI stereo reference is our surgeoneffects Sparks host, optionally bootstrapped
    /// (material + vertex streams only) from vanilla ExplosionSparkles as a one-shot read.
    /// Live SPI references never point at NoteCut / ExplosionSparkles after bootstrap.
    /// </summary>
    internal static class BeatSurgeonOwnedVfxSpace
    {
        private const string CustomParticlesShaderName = "Custom/CustomParticles";
        private const string ParticleOverdrivePluginId = "ParticleOverdrive";

        private static Transform _root;
        private static ParticleSystemRenderer _ownedSpiReference;
        private static bool _triedOwnedSpiReference;
        private static GameObject _ownedSpiReferenceHost;
        private static bool _bootstrappedOwnedSpiFromVanilla;
        private static bool _loggedVanillaSparklesMissing;
        private static bool _loggedParticleOverdrivePresence;
        private static bool? _particleOverdriveEnabled;

        internal static Transform GetRoot()
        {
            if (_root != null)
            {
                return _root;
            }

            GameObject rootGo = new GameObject("BeatSurgeon_OwnedVfxRoot");
            UnityEngine.Object.DontDestroyOnLoad(rootGo);
            _root = rootGo.transform;
            LogParticleOverdrivePresenceOnce();
            return _root;
        }

        /// <summary>
        /// True when ParticleOverdrive is among IPA enabled plugins.
        /// </summary>
        internal static bool IsParticleOverdriveEnabled()
        {
            if (_particleOverdriveEnabled.HasValue)
            {
                return _particleOverdriveEnabled.Value;
            }

            try
            {
                _particleOverdriveEnabled = PluginManager.EnabledPlugins != null
                    && PluginManager.EnabledPlugins.Any(plugin =>
                        plugin != null
                        && (string.Equals(plugin.Id, ParticleOverdrivePluginId, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(plugin.Name, "Particle Overdrive", StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                _particleOverdriveEnabled = false;
            }

            return _particleOverdriveEnabled.Value;
        }

        /// <summary>
        /// Returns a ParticleSystemRenderer from BeatSurgeon's own Sparks host.
        /// Never returns a vanilla NoteCut / ExplosionSparkles renderer.
        /// </summary>
        internal static ParticleSystemRenderer TryGetOwnedSpiReferenceRenderer()
        {
            if (_ownedSpiReference != null)
            {
                TryBootstrapOwnedSpiFromVanillaExplosionSparkles();
                return _ownedSpiReference;
            }

            if (_triedOwnedSpiReference)
            {
                return null;
            }

            _triedOwnedSpiReference = true;

            try
            {
                GameObject host = SurgeonEffectsBundleService.CreateBombExplosionInstanceFromBundle(
                    BundleRegistry.SurgeonExplosionRefs.SparkEmitterName);
                if (host == null)
                {
                    return null;
                }

                // Keep a hidden host so the renderer stays valid for the session.
                _ownedSpiReferenceHost = host;
                UnityEngine.Object.DontDestroyOnLoad(_ownedSpiReferenceHost);
                _ownedSpiReferenceHost.name = "BeatSurgeon_OwnedSpiReference_Sparks";
                _ownedSpiReferenceHost.transform.SetParent(GetRoot(), false);
                _ownedSpiReferenceHost.transform.localPosition = new Vector3(0f, -4096f, 0f);
                _ownedSpiReferenceHost.SetActive(false);

                ParticleSystemRenderer[] renderers =
                    _ownedSpiReferenceHost.GetComponentsInChildren<ParticleSystemRenderer>(true);
                ParticleSystemRenderer best = null;
                int bestScore = int.MinValue;
                for (int i = 0; i < renderers.Length; i++)
                {
                    ParticleSystemRenderer renderer = renderers[i];
                    if (renderer == null || renderer.sharedMaterial == null)
                    {
                        continue;
                    }

                    int score = ScoreOwnedSpiRenderer(renderer);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = renderer;
                    }
                }

                _ownedSpiReference = best ?? (renderers.Length > 0 ? renderers[0] : null);
                if (_ownedSpiReference != null)
                {
                    Plugin.Log.Info(
                        "BeatSurgeonOwnedVfxSpace: using owned SPI reference '"
                        + _ownedSpiReference.name
                        + "' from surgeoneffects Sparks (not parenting under NoteCut).");
                    TryBootstrapOwnedSpiFromVanillaExplosionSparkles();
                }

                return _ownedSpiReference;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("BeatSurgeonOwnedVfxSpace: failed resolving owned SPI reference: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Best SPI stereo reference for material/stream sync.
        /// Always returns the owned Sparks host (bootstrapped when possible).
        /// Never returns a live vanilla NoteCut / ExplosionSparkles renderer — that tree is
        /// mutated by ParticleOverdrive (maxParticles, size/lifetime multipliers).
        /// </summary>
        internal static ParticleSystemRenderer TryGetSpiStereoReferenceRenderer()
        {
            ParticleSystemRenderer owned = TryGetOwnedSpiReferenceRenderer();
            if (owned != null)
            {
                // Ensure bootstrap has run once ExplosionSparkles is resident.
                if (!IsSpiCapableRenderer(owned))
                {
                    TryBootstrapOwnedSpiFromVanillaExplosionSparkles();
                }

                return owned;
            }

            // Owned host unavailable (missing surgeoneffects Sparks). Do not fall back to
            // vanilla ExplosionSparkles as a live reference — PO patches that hierarchy.
            return null;
        }

        /// <summary>
        /// Read-only lookup of vanilla ExplosionSparkles for one-shot SPI shader/stream bootstrap.
        /// Never used as a parenting anchor or long-lived stereo reference.
        /// </summary>
        internal static ParticleSystemRenderer TryFindVanillaExplosionSparklesRenderer()
        {
            try
            {
                ParticleSystemRenderer[] renderers = Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>();
                ParticleSystemRenderer best = null;
                int bestScore = int.MinValue;
                for (int i = 0; i < renderers.Length; i++)
                {
                    ParticleSystemRenderer renderer = renderers[i];
                    if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                    {
                        continue;
                    }

                    string transformName = renderer.transform != null ? renderer.transform.name : string.Empty;
                    if (transformName.IndexOf("ExplosionSparkles", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    // Skip our own clones / menu junk.
                    string path = GetTransformPath(renderer.transform);
                    if (path.IndexOf("BeatSurgeon", StringComparison.OrdinalIgnoreCase) >= 0
                        || path.IndexOf("_BundleInstance", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    int score = 1000;
                    string shaderName = renderer.sharedMaterial.shader.name;
                    if (shaderName.IndexOf(CustomParticlesShaderName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 2000;
                    }

                    if (path.IndexOf("NoteCutCoreEffectsSpawner", StringComparison.OrdinalIgnoreCase) >= 0
                        || path.IndexOf("BombExplosionEffect", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 500;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = renderer;
                    }
                }

                return best;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("BeatSurgeonOwnedVfxSpace: ExplosionSparkles lookup failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// True when the renderer lives under vanilla note-cut / bomb-explosion particle trees
        /// that ParticleOverdrive and similar mods patch.
        /// </summary>
        internal static bool IsVanillaNoteCutParticleHierarchy(ParticleSystemRenderer renderer)
        {
            if (renderer == null || renderer.transform == null)
            {
                return false;
            }

            string path = GetTransformPath(renderer.transform);
            string transformName = renderer.transform.name ?? string.Empty;
            return path.IndexOf("NoteCutCoreEffectsSpawner", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("BombExplosionEffect", StringComparison.OrdinalIgnoreCase) >= 0
                || transformName.IndexOf("ExplosionSparkles", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void Reset()
        {
            _ownedSpiReference = null;
            _triedOwnedSpiReference = false;
            _bootstrappedOwnedSpiFromVanilla = false;
            _loggedVanillaSparklesMissing = false;
            _loggedParticleOverdrivePresence = false;
            // Keep _particleOverdriveEnabled — plugin set does not change mid-session.

            if (_ownedSpiReferenceHost != null)
            {
                UnityEngine.Object.Destroy(_ownedSpiReferenceHost);
                _ownedSpiReferenceHost = null;
            }

            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root.gameObject);
                _root = null;
            }
        }

        private static void LogParticleOverdrivePresenceOnce()
        {
            if (_loggedParticleOverdrivePresence)
            {
                return;
            }

            _loggedParticleOverdrivePresence = true;
            if (!IsParticleOverdriveEnabled())
            {
                return;
            }

            Plugin.Log.Info(
                "BeatSurgeonOwnedVfxSpace: ParticleOverdrive detected — "
                + "BeatSurgeon VFX stays on BeatSurgeon_OwnedVfxRoot; "
                + "vanilla ExplosionSparkles is read once for SPI material/streams only "
                + "(never as live emit/parent/reference).");
        }

        private static void TryBootstrapOwnedSpiFromVanillaExplosionSparkles()
        {
            TryBootstrapOwnedSpiFromVanillaExplosionSparkles(null);
        }

        private static void TryBootstrapOwnedSpiFromVanillaExplosionSparkles(ParticleSystemRenderer preferredVanilla)
        {
            if (_bootstrappedOwnedSpiFromVanilla || _ownedSpiReference == null)
            {
                return;
            }

            ParticleSystemRenderer vanilla = preferredVanilla ?? TryFindVanillaExplosionSparklesRenderer();
            if (vanilla == null || vanilla.sharedMaterial == null || vanilla.sharedMaterial.shader == null)
            {
                if (!_loggedVanillaSparklesMissing)
                {
                    _loggedVanillaSparklesMissing = true;
                    Plugin.Log.Info(
                        "BeatSurgeonOwnedVfxSpace: ExplosionSparkles not resident yet; "
                        + "owned Sparks SPI bootstrap deferred (menu-safe).");
                }

                return;
            }

            try
            {
                // Material + vertex streams only. Do not copy MainModule (PO sets maxParticles /
                // size / lifetime multipliers on the vanilla particle systems).
                Material bootMaterial = new Material(vanilla.sharedMaterial)
                {
                    name = (vanilla.sharedMaterial.name ?? "ExplosionSparkles") + "_BeatSurgeonOwnedSpiBootstrap"
                };
                _ownedSpiReference.sharedMaterial = bootMaterial;
                _ownedSpiReference.trailMaterial = bootMaterial;

                var vertexStreams = new List<ParticleSystemVertexStream>(
                    Mathf.Max(4, vanilla.activeVertexStreamsCount));
                vanilla.GetActiveVertexStreams(vertexStreams);
                if (vertexStreams.Count > 0)
                {
                    _ownedSpiReference.SetActiveVertexStreams(vertexStreams);
                }

                var trailStreams = new List<ParticleSystemVertexStream>(
                    Mathf.Max(4, vanilla.activeTrailVertexStreamsCount));
                vanilla.GetActiveTrailVertexStreams(trailStreams);
                if (trailStreams.Count == 0)
                {
                    trailStreams = new List<ParticleSystemVertexStream>(vertexStreams);
                }

                if (trailStreams.Count > 0)
                {
                    _ownedSpiReference.SetActiveTrailVertexStreams(trailStreams);
                }

                _ownedSpiReference.enableGPUInstancing = false;
                _bootstrappedOwnedSpiFromVanilla = true;

                Plugin.Log.Info(
                    "BeatSurgeonOwnedVfxSpace: bootstrapped owned Sparks SPI from ExplosionSparkles '"
                    + vanilla.name
                    + "' shader='"
                    + bootMaterial.shader.name
                    + "' streams="
                    + vertexStreams.Count
                    + " trailStreams="
                    + trailStreams.Count
                    + " (one-shot material/streams only; ParticleOverdrive MainModule ignored).");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn(
                    "BeatSurgeonOwnedVfxSpace: failed bootstrapping owned Sparks from ExplosionSparkles: "
                    + ex.Message);
            }
        }

        private static bool IsSpiCapableRenderer(ParticleSystemRenderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
            {
                return false;
            }

            string shaderName = renderer.sharedMaterial.shader.name;
            if (shaderName.IndexOf(CustomParticlesShaderName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // Bootstrapped hosts are marked capable even if shader name differs slightly.
            return _bootstrappedOwnedSpiFromVanilla && ReferenceEquals(renderer, _ownedSpiReference);
        }

        private static int ScoreOwnedSpiRenderer(ParticleSystemRenderer renderer)
        {
            int score = 0;
            string shaderName = renderer.sharedMaterial != null && renderer.sharedMaterial.shader != null
                ? renderer.sharedMaterial.shader.name
                : string.Empty;
            string transformName = renderer.transform != null ? renderer.transform.name : string.Empty;

            if (shaderName.IndexOf(CustomParticlesShaderName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 2000;
            }

            if (transformName.IndexOf("Spark", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 500;
            }

            if (renderer.renderMode != ParticleSystemRenderMode.Mesh)
            {
                score += 200;
            }

            return score;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var segments = new List<string>();
            Transform current = transform;
            while (current != null && segments.Count < 12)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
