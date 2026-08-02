using System;
using System.Collections.Generic;
using System.Linq;
using AssetBundleLoadingTools.Utilities;
using UnityEngine;

namespace BeatSurgeon.Gameplay
{
    internal static class VrVfxMaterialHelper
    {
        private const string InvalidShaderName = "ShaderBundleInternal/Invalid";
        private static readonly string[] DeterministicTransparentParticleShaders =
        {
            "Particles/Alpha Blended",
            "Particles/Standard Unlit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Sprites/Default"
        };

        private static readonly string[] DeterministicTransparentVisualShaders =
        {
            "Unlit/Transparent",
            "Sprites/Default",
            "Legacy Shaders/Transparent/Diffuse",
            "Unlit/Texture"
        };

        private static readonly string[] RejectedFallbackShaderTokens =
        {
            "screendisplacement",
            "distortion",
            "obstacle",
            "mirror",
            "water"
        };

        private static readonly string[] ForcedSafeParticleShaderNames =
        {
            // Bundle shaders that do not support Beat Saber's Single Pass Instanced protocol.
            "Custom/SimpleLightning",
            "Custom/TeslaLightning",
            // SeismicParticle/ProceduralBand is intentionally NOT force-replaced. Shockwave's
            // authored ring/fire-band look depends on that procedural shader; swapping it for
            // Custom/SimpleLit in gameplay made the explosion spawn but render invisible.
            // Sprites/Default does not use the SPI eye-index TexCoord that Beat Saber's
            // Custom/CustomParticles emits. Child glow particles on Lightning use this shader
            // and must be replaced so they render in both eyes.
            "Sprites/Default",
            // Standard Unity fallback shaders assigned at menu-preload time when no SPI-capable
            // reference renderer is available. ApplyStereoStateAtPlay replaces them at play time.
            "Particles/Standard Unlit",
            "Particles/Alpha Blended",
            "Legacy Shaders/Particles/Alpha Blended"
        };

        internal static readonly string[] SuppressedBundleRepairShaderNames =
        {
            "SeismicParticle/ProceduralBand",
            "Custom/SimpleLightning",
            "Custom/TeslaLightning"
        };

        private static Material _safeParticleMaterialBase;

        internal static void RepairShaders(GameObject root, string context)
        {
            RepairShaders(root, context, null);
        }

        internal static void RepairShaders(GameObject root, string context, string[] suppressedMissingShaderNames)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                var result = ShaderRepair.FixShadersOnGameObject(root);
                if (!result.AllShadersReplaced && result.MissingShaderNames.Count > 0)
                {
                    string[] missingShaderNames = result.MissingShaderNames
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (!ShouldSuppressRepairWarning(missingShaderNames, suppressedMissingShaderNames))
                    {
                        Plugin.Log.Warn(context + ": shader repair missing replacements for " + string.Join(", ", missingShaderNames));
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn(context + ": shader repair failed: " + ex.Message);
            }
        }

        internal static void RepairShader(Material material, string context, string[] suppressedMissingShaderNames = null)
        {
            if (material == null)
            {
                return;
            }

            try
            {
                var result = ShaderRepair.FixShaderOnMaterial(material);
                string[] missingShaderNames = result.MissingShaderNames
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (!result.AllShadersReplaced
                    && missingShaderNames.Length > 0
                    && !ShouldSuppressRepairWarning(missingShaderNames, suppressedMissingShaderNames))
                {
                    Plugin.Log.Warn(context + ": shader repair missing replacements for " + string.Join(", ", missingShaderNames));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn(context + ": shader repair failed: " + ex.Message);
            }
        }

        internal static Material CreatePreparedParticleMaterial(Material sourceMaterial, string context, Texture fallbackTexture = null)
        {
            if (sourceMaterial == null)
            {
                return CreateSafeParticleMaterial(null, fallbackTexture);
            }

            Texture resolvedFallbackTexture = fallbackTexture ?? GetBestAvailableTexture(sourceMaterial);
            Material repairedClone = new Material(sourceMaterial)
            {
                name = sourceMaterial.name + "_BeatSurgeonPrepared"
            };

            RepairShader(repairedClone, context);
            if (CanPreserveSourceMaterial(repairedClone))
            {
                ApplySharedParticleDefaults(repairedClone, resolvedFallbackTexture, preserveTint: true);
                return repairedClone;
            }

            try
            {
                UnityEngine.Object.Destroy(repairedClone);
            }
            catch { }

            return CreateSafeParticleMaterial(sourceMaterial, resolvedFallbackTexture);
        }

        internal static Material CreatePreparedVisualMaterial(Material sourceMaterial, string context, Texture fallbackTexture = null)
        {
            if (sourceMaterial == null)
            {
                return CreateSafeVisualMaterial(null, fallbackTexture);
            }

            Texture resolvedFallbackTexture = fallbackTexture ?? GetBestAvailableTexture(sourceMaterial);

            if (ShouldSkipKnownUnsupportedVisualRepair(sourceMaterial, context))
            {
                Material skippedRepairSafeMaterial = CreateSafeVisualMaterial(sourceMaterial, resolvedFallbackTexture);
                if (skippedRepairSafeMaterial != null)
                {
                    Plugin.Log.Info(
                        context
                        + ": skipping AssetBundleLoadingTools shader repair for known unsupported material '"
                        + sourceMaterial.name
                        + "' shader='"
                        + (sourceMaterial.shader != null ? sourceMaterial.shader.name : "<missing>")
                        + "'; using visual-safe fallback material '"
                        + skippedRepairSafeMaterial.name
                        + "' shader='"
                        + (skippedRepairSafeMaterial.shader != null ? skippedRepairSafeMaterial.shader.name : "<missing>")
                        + "' texture='"
                        + (GetBestAvailableTexture(skippedRepairSafeMaterial) != null ? GetBestAvailableTexture(skippedRepairSafeMaterial).name : "<missing>")
                        + "'.");
                }

                return skippedRepairSafeMaterial;
            }

            Material repairedClone = new Material(sourceMaterial)
            {
                name = sourceMaterial.name + "_BeatSurgeonPreparedVisual"
            };

            RepairShader(repairedClone, context, new[] { "Unlit/TrailShader" });
            if (CanPreserveSourceMaterial(repairedClone))
            {
                ApplySharedVisualDefaults(repairedClone, resolvedFallbackTexture);
                return repairedClone;
            }

            try
            {
                UnityEngine.Object.Destroy(repairedClone);
            }
            catch { }

            Material safeMaterial = CreateSafeVisualMaterial(sourceMaterial, resolvedFallbackTexture);
            if (safeMaterial != null)
            {
                Plugin.Log.Info(
                    context
                    + ": using visual-safe fallback material '"
                    + safeMaterial.name
                    + "' shader='"
                    + (safeMaterial.shader != null ? safeMaterial.shader.name : "<missing>")
                    + "' texture='"
                    + (GetBestAvailableTexture(safeMaterial) != null ? GetBestAvailableTexture(safeMaterial).name : "<missing>")
                    + "'.");
            }

            return safeMaterial;
        }

        internal static Material CreateForcedSafeParticleMaterial(Material sourceMaterial, Texture fallbackTexture = null)
        {
            Texture resolvedFallbackTexture = fallbackTexture ?? SafeGetBestAvailableTexture(sourceMaterial);

            Material material = CreateDeterministicTransparentParticleMaterial(sourceMaterial, "_BeatSurgeonForcedSafe");
            if (material == null)
            {
                Material baseMaterial = GetSafeParticleMaterialBase();
                if (baseMaterial != null)
                {
                    material = new Material(baseMaterial);
                    if (sourceMaterial != null)
                    {
                        material.name = sourceMaterial.name + "_BeatSurgeonForcedSafeFallback";
                        CopyCommonParticleProperties(sourceMaterial, material);
                    }
                }
                else
                {
                    Shader shader = Shader.Find("Particles/Standard Unlit")
                        ?? Shader.Find("Particles/Alpha Blended")
                        ?? Shader.Find("Sprites/Default");
                    if (shader == null)
                    {
                        return null;
                    }

                    material = new Material(shader)
                    {
                        name = sourceMaterial != null
                            ? sourceMaterial.name + "_BeatSurgeonForcedSafeFallback"
                            : "BeatSurgeonForcedSafeParticle"
                    };

                    if (sourceMaterial != null)
                    {
                        CopyCommonParticleProperties(sourceMaterial, material);
                    }
                }
            }

            ApplySharedParticleDefaults(material, resolvedFallbackTexture, preserveTint: false);
            ApplyDeterministicParticleBlend(material);
            return material;
        }

        internal static Material CreateSafeParticleMaterial(Material sourceMaterial, Texture fallbackTexture = null)
        {
            if (CanPreserveSourceMaterial(sourceMaterial))
            {
                Material preservedMaterial = new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + "_BeatSurgeonVrSafe"
                };

                ApplySharedParticleDefaults(preservedMaterial, fallbackTexture, preserveTint: true);
                return preservedMaterial;
            }

            Material material = null;
            Material baseMaterial = GetSafeParticleMaterialBase();

            if (baseMaterial != null)
            {
                material = new Material(baseMaterial);
                if (sourceMaterial != null)
                {
                    material.name = sourceMaterial.name + "_BeatSurgeonVrSafeFallback";
                    CopyCommonParticleProperties(sourceMaterial, material);
                }
            }
            else if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + "_BeatSurgeonFallback"
                };
            }
            else
            {
                Shader shader = Shader.Find("Particles/Standard Unlit")
                    ?? Shader.Find("Particles/Alpha Blended")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    material = new Material(shader);
                }
            }

            ApplySharedParticleDefaults(material, fallbackTexture, preserveTint: false);
            return material;
        }

        /// <summary>
        /// Creates a play-time replacement material for billboard particles whose shaders are not
        /// SPI-capable.  The replacement is a clone of the reference renderer's material
        /// (typically Custom/CustomParticles) so SPI vertex streams work correctly.  The authored
        /// tint is recovered from <paramref name="sourceMaterial"/> and force-applied to
        /// <c>_TintColor</c>, overwriting the reference clone's default value.  This ensures that
        /// colours preserved via the <c>_Color</c> property (written by the _TintColor → _Color
        /// bridge in CopyCommonParticleProperties during menu-time prep) are correctly restored.
        /// </summary>
        internal static Material CreateForcedSafeBillboardReplacement(
            Material sourceMaterial,
            ParticleSystemRenderer referenceRenderer,
            ParticleSystem ownerParticleSystem = null)
        {
            if (referenceRenderer?.sharedMaterial == null || !HasUsableShader(referenceRenderer.sharedMaterial))
            {
                return null;
            }

            // Sprites/Default encodes appearance in particle vertex color with no atlas texture.
            // Cloning the vanilla bomb reference material would apply the Sparkle texture and
            // make authored Lightning/glow particles look like vanilla bomb sparkles.
            if (UsesSpritesDefaultShader(sourceMaterial?.shader))
            {
                return CreateAuthoredSpiParticleMaterial(
                    sourceMaterial,
                    referenceRenderer.sharedMaterial.shader,
                    referenceMaterial: null,
                    vertexColorDrivenTintOverride: GetApproximateAuthoredStartColor(ownerParticleSystem));
            }

            Texture fallbackTexture = GetBestAvailableTexture(sourceMaterial);
            bool sourceUsesAuthoredTexture = HasAuthoredParticleTexture(sourceMaterial);
            Color resolvedTint = sourceUsesAuthoredTexture
                ? Color.white
                : ownerParticleSystem != null
                    ? ResolveAuthoredSpiVisibleColor(
                        sourceMaterial,
                        GetApproximateAuthoredStartColor(ownerParticleSystem))
                    : SafeGetMaterialColor(sourceMaterial);

            Material safemat = new Material(referenceRenderer.sharedMaterial)
            {
                name = (sourceMaterial?.name ?? "VfxParticle") + "_BeatSurgeonBillboardVfx"
            };

            if (sourceMaterial != null)
            {
                CopyCommonParticleProperties(sourceMaterial, safemat);
            }

            if (!LooksLikeUnsetVisibleColor(resolvedTint))
            {
                ApplySpiVisibleTint(safemat, resolvedTint);
            }

            if (fallbackTexture != null && MaterialSupportsTextureAssignment(safemat))
            {
                TryAssignTexture(safemat, fallbackTexture);
            }

            ApplyMinimalVrParticleTweaks(safemat);
            return safemat;
        }

        /// <summary>
        /// Builds an SPI-capable material from the authored source without inheriting the vanilla
        /// bomb reference material's texture/tint defaults.
        /// </summary>
        internal static Material CreateAuthoredSpiParticleMaterial(
            Material sourceMaterial,
            Shader spiShader,
            Material referenceMaterial = null,
            Color? vertexColorDrivenTintOverride = null)
        {
            if (sourceMaterial == null || spiShader == null)
            {
                return null;
            }

            int sourceRenderQueue = sourceMaterial.renderQueue;
            Material spiMaterial = new Material(spiShader)
            {
                name = sourceMaterial.name + "_BeatSurgeonAuthoredSpi"
            };

            CopyCommonParticleProperties(sourceMaterial, spiMaterial);

            bool sourceUsesSpritesDefault = UsesSpritesDefaultShader(sourceMaterial.shader);
            Texture authoredTexture = GetBestAvailableTexture(sourceMaterial);
            if (authoredTexture == null)
            {
                if (sourceUsesSpritesDefault)
                {
                    // Preserve hard-sprite / vertex-color look; do not borrow vanilla Sparkle.
                    authoredTexture = Texture2D.whiteTexture;
                }
                else if (referenceMaterial != null)
                {
                    authoredTexture = GetBestAvailableTexture(referenceMaterial);
                }
            }

            if (authoredTexture != null && MaterialSupportsTextureAssignment(spiMaterial))
            {
                TryAssignTexture(spiMaterial, authoredTexture);
            }

            Color resolvedTint = ResolveAuthoredSpiVisibleColor(
                sourceMaterial,
                vertexColorDrivenTintOverride);
            ApplySpiVisibleTint(spiMaterial, resolvedTint);

            ApplyMinimalVrParticleTweaks(spiMaterial);
            if (sourceRenderQueue > 0)
            {
                spiMaterial.renderQueue = sourceRenderQueue;
            }

            return spiMaterial;
        }

        /// <summary>
        /// Builds a play-time material for atlas-textured billboard emitters (Flame, Hearts, Sparks).
        /// Do not clone vanilla Custom/CustomParticles here: that shader path is tuned for grayscale
        /// sparkle masks * tint (Lightning/vanilla bombs). Authored Flame/Heart atlases are full RGB
        /// and render as flat white silhouettes under that path even when _MainTex is assigned.
        /// Use the same deterministic transparent particle material as bit-atlas emitters so texture
        /// RGB is preserved, then rely on SyncParticleRendererStereoState for SPI streams.
        /// </summary>
        internal static Material CreateTexturedSpiParticleMaterial(
            Material sourceMaterial,
            ParticleSystemRenderer referenceRenderer)
        {
            if (sourceMaterial == null)
            {
                return null;
            }

            Texture authoredTexture = GetBestAvailableTexture(sourceMaterial);
            Material spiMaterial = CreateForcedSafeParticleMaterial(sourceMaterial, authoredTexture);
            if (spiMaterial == null)
            {
                // Last-resort: bare SPI shader with authored texture. Prefer color fidelity over this.
                Shader spiShader = referenceRenderer?.sharedMaterial != null
                    ? referenceRenderer.sharedMaterial.shader
                    : null;
                spiMaterial = CreateAuthoredSpiParticleMaterial(
                    sourceMaterial,
                    spiShader,
                    referenceMaterial: null,
                    vertexColorDrivenTintOverride: Color.white);
            }

            if (spiMaterial == null)
            {
                return null;
            }

            spiMaterial.name = sourceMaterial.name + "_BeatSurgeonTexturedSpi";
            if (authoredTexture != null && MaterialSupportsTextureAssignment(spiMaterial))
            {
                TryAssignTexture(spiMaterial, authoredTexture);
            }

            ApplySpiVisibleTint(spiMaterial, Color.white);
            ApplyMinimalVrParticleTweaks(spiMaterial);
            return spiMaterial;
        }

        /// <summary>
        /// Prepares trail-based particle emitters (Lightning) for SPI stereo without altering
        /// authored render mode or layout. Clones the SPI material from the authored source,
        /// assigns it to both particle and trail slots, then syncs only stereo vertex streams.
        /// </summary>
        internal static void PrepareTrailParticleStereoAtPlay(
            ParticleSystemRenderer particleRenderer,
            ParticleSystemRenderer referenceRenderer)
        {
            if (particleRenderer == null || referenceRenderer == null)
            {
                return;
            }

            ParticleSystem particleSystem = particleRenderer.GetComponent<ParticleSystem>();
            if (particleSystem == null || !particleSystem.trails.enabled)
            {
                return;
            }

            Material sourceMaterial = particleRenderer.sharedMaterial;
            bool sourceUsesSpritesDefault = UsesSpritesDefaultShader(sourceMaterial?.shader);
            bool sourceUsesAuthoredTexture = HasAuthoredParticleTexture(sourceMaterial);
            Color authoredStartColor = GetApproximateAuthoredStartColor(particleSystem);
            // Prefer an SPI-capable shader. Owned Sparks may still be Particles/Standard Unlit
            // until ExplosionSparkles bootstrap runs; never bake that into Lightning trails.
            Shader spiShader = ResolveSpiCapableParticleShader(referenceRenderer);
            ParticleSystemRenderer stereoStreamReference = ResolveStereoStreamReference(referenceRenderer);
            Material spiMaterial;
            if (sourceUsesSpritesDefault)
            {
                spiMaterial = CreateAuthoredSpiParticleMaterial(
                    sourceMaterial,
                    spiShader,
                    referenceMaterial: null,
                    vertexColorDrivenTintOverride: authoredStartColor);
            }
            else if (sourceUsesAuthoredTexture)
            {
                spiMaterial = CreateTexturedSpiParticleMaterial(sourceMaterial, stereoStreamReference);
            }
            else
            {
                spiMaterial = CreateAuthoredSpiParticleMaterial(
                    sourceMaterial,
                    spiShader,
                    referenceMaterial: stereoStreamReference != null
                        ? stereoStreamReference.sharedMaterial
                        : referenceRenderer.sharedMaterial,
                    vertexColorDrivenTintOverride: authoredStartColor);
            }
            if (spiMaterial != null)
            {
                particleRenderer.sharedMaterial = spiMaterial;
                particleRenderer.trailMaterial = spiMaterial;
            }

            particleRenderer.enableGPUInstancing = false;
            SyncParticleRendererStereoState(
                particleRenderer,
                stereoStreamReference,
                preserveAuthoredLayout: true,
                preserveAuthoredVertexStreams: sourceUsesAuthoredTexture);
            HardenParticleRendererStereoCulling(particleRenderer);

            Texture appliedTexture = particleRenderer.sharedMaterial != null
                ? GetBestAvailableTexture(particleRenderer.sharedMaterial)
                : null;
            Color appliedTint = particleRenderer.sharedMaterial != null
                && particleRenderer.sharedMaterial.HasProperty("_TintColor")
                ? particleRenderer.sharedMaterial.GetColor("_TintColor")
                : Color.white;
            Plugin.Log.Info(
                "VrVfxMaterialHelper: Prepared authored trail stereo for '"
                + particleRenderer.name
                + "' materialPath="
                + (sourceUsesSpritesDefault
                    ? "spritesDefault"
                    : sourceUsesAuthoredTexture
                        ? "texturedRgbSafe"
                        : "authoredSpi")
                + " shader='"
                + (particleRenderer.sharedMaterial != null && particleRenderer.sharedMaterial.shader != null
                    ? particleRenderer.sharedMaterial.shader.name
                    : "<missing>")
                + "' texture='"
                + (appliedTexture != null ? appliedTexture.name : "<missing>")
                + "' tint=("
                + appliedTint.r.ToString("F2")
                + ","
                + appliedTint.g.ToString("F2")
                + ","
                + appliedTint.b.ToString("F2")
                + ","
                + appliedTint.a.ToString("F2")
                + ") startColor=("
                + authoredStartColor.r.ToString("F2")
                + ","
                + authoredStartColor.g.ToString("F2")
                + ","
                + authoredStartColor.b.ToString("F2")
                + ","
                + authoredStartColor.a.ToString("F2")
                + ") renderMode="
                + particleRenderer.renderMode
                + " trailMaterial="
                + (particleRenderer.trailMaterial != null ? "set" : "null")
                + ".");
        }

        /// <summary>
        /// Prepares mesh-mode particle emitters (Shockwave rings) for SPI stereo.
        /// SeismicParticle/ProceduralBand is authored without Single Pass Instanced support, so it
        /// renders in one eye. Replace it with Custom/SimpleLit (same path as the bomb mesh) while
        /// baking ProceduralBand's _Tint / _Alpha / _Intensity into a visible transparent color so
        /// the rings stay cyan/blue instead of disappearing like the earlier bare SimpleLit swap.
        /// </summary>
        internal static void PrepareMeshParticleStereoAtPlay(
            ParticleSystemRenderer particleRenderer,
            ParticleSystemRenderer referenceRenderer)
        {
            if (particleRenderer == null
                || particleRenderer.renderMode != ParticleSystemRenderMode.Mesh)
            {
                return;
            }

            EnsureMeshParticleRendererReady(particleRenderer);

            Material[] materials = particleRenderer.sharedMaterials;
            if (materials != null && materials.Length > 0)
            {
                bool replacedMaterial = false;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material sourceMaterial = materials[index];
                    if (sourceMaterial == null || !NeedsMeshParticleSpiReplacement(sourceMaterial.shader))
                    {
                        continue;
                    }

                    Material spiMaterial = CreateMeshParticleSpiMaterial(sourceMaterial);
                    if (spiMaterial == null)
                    {
                        continue;
                    }

                    materials[index] = spiMaterial;
                    replacedMaterial = true;
                }

                if (replacedMaterial)
                {
                    particleRenderer.sharedMaterials = materials;
                }
            }

            particleRenderer.enableGPUInstancing = false;
            if (referenceRenderer != null)
            {
                try
                {
                    particleRenderer.renderingLayerMask = referenceRenderer.renderingLayerMask;
                    particleRenderer.lightProbeUsage = referenceRenderer.lightProbeUsage;
                    particleRenderer.reflectionProbeUsage = referenceRenderer.reflectionProbeUsage;
                    particleRenderer.motionVectorGenerationMode = referenceRenderer.motionVectorGenerationMode;
                }
                catch { }
            }

            HardenParticleRendererStereoCulling(particleRenderer);

            Material applied = particleRenderer.sharedMaterial;
            Color appliedColor = Color.white;
            if (applied != null)
            {
                if (applied.HasProperty("_Color"))
                {
                    appliedColor = applied.GetColor("_Color");
                }
                else if (applied.HasProperty("_SimpleColor"))
                {
                    appliedColor = applied.GetColor("_SimpleColor");
                }
            }

            Plugin.Log.Info(
                "VrVfxMaterialHelper: Prepared mesh particle stereo for '"
                + particleRenderer.name
                + "' shader='"
                + (applied != null && applied.shader != null ? applied.shader.name : "<missing>")
                + "' color=("
                + appliedColor.r.ToString("F2")
                + ","
                + appliedColor.g.ToString("F2")
                + ","
                + appliedColor.b.ToString("F2")
                + ","
                + appliedColor.a.ToString("F2")
                + ") mesh="
                + (particleRenderer.mesh != null ? particleRenderer.mesh.name : "<missing>")
                + ".");
        }

        private static bool NeedsMeshParticleSpiReplacement(Shader shader)
        {
            if (shader == null || !shader.isSupported || IsBrokenShaderName(shader.name))
            {
                return true;
            }

            string shaderName = shader.name ?? string.Empty;
            return string.Equals(shaderName, "SeismicParticle/ProceduralBand", StringComparison.OrdinalIgnoreCase)
                || shaderName.IndexOf("ProceduralBand", StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("FogLighting", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Material CreateMeshParticleSpiMaterial(Material sourceMaterial)
        {
            Shader spiShader = Shader.Find("Custom/SimpleLit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                ?? Shader.Find("Standard");
            if (spiShader == null)
            {
                return null;
            }

            Color visibleColor = ResolveProceduralBandVisibleColor(sourceMaterial);
            Material spiMaterial = new Material(spiShader)
            {
                name = (sourceMaterial != null ? sourceMaterial.name : "MeshParticle") + "_BeatSurgeonMeshSpi"
            };

            if (spiMaterial.HasProperty("_Color"))
            {
                spiMaterial.SetColor("_Color", visibleColor);
            }

            if (spiMaterial.HasProperty("_SimpleColor"))
            {
                spiMaterial.SetColor("_SimpleColor", visibleColor);
            }

            if (spiMaterial.HasProperty("_BaseColor"))
            {
                spiMaterial.SetColor("_BaseColor", visibleColor);
            }

            if (spiMaterial.HasProperty("_TintColor"))
            {
                spiMaterial.SetColor("_TintColor", visibleColor);
            }

            if (spiMaterial.HasProperty("_EmissionColor"))
            {
                // Keep a mild emission so high-intensity shockwave rings stay readable after
                // clamping HDR ProceduralBand intensity into SDR SimpleLit color space.
                Color emission = visibleColor * 0.75f;
                emission.a = visibleColor.a;
                spiMaterial.SetColor("_EmissionColor", emission);
            }

            ApplyDeterministicParticleBlend(spiMaterial);
            ApplyMinimalVrParticleTweaks(spiMaterial);
            return spiMaterial;
        }

        private static Color ResolveProceduralBandVisibleColor(Material sourceMaterial)
        {
            if (sourceMaterial == null)
            {
                return new Color(0.05f, 0.86f, 1f, 0.95f);
            }

            Color tint = Color.white;
            if (sourceMaterial.HasProperty("_Tint"))
            {
                tint = sourceMaterial.GetColor("_Tint");
            }
            else if (sourceMaterial.HasProperty("_Color"))
            {
                tint = sourceMaterial.GetColor("_Color");
            }
            else if (sourceMaterial.HasProperty("_TintColor"))
            {
                tint = sourceMaterial.GetColor("_TintColor");
            }

            float alpha = 1f;
            if (sourceMaterial.HasProperty("_Alpha"))
            {
                alpha = Mathf.Clamp01(sourceMaterial.GetFloat("_Alpha"));
            }
            else
            {
                alpha = Mathf.Clamp01(tint.a);
            }

            float intensity = 1f;
            if (sourceMaterial.HasProperty("_Intensity"))
            {
                intensity = Mathf.Max(0.1f, sourceMaterial.GetFloat("_Intensity"));
            }

            // ProceduralBand uses HDR intensity (often 3-6.5). SimpleLit is SDR, so compress.
            float gain = Mathf.Clamp(intensity * 0.35f, 0.55f, 1.75f);
            Color visible = new Color(
                Mathf.Clamp01(tint.r * gain),
                Mathf.Clamp01(tint.g * gain),
                Mathf.Clamp01(tint.b * gain),
                alpha);

            if (LooksLikeUnsetVisibleColor(visible) || visible.maxColorComponent < 0.05f)
            {
                return new Color(0.05f, 0.86f, 1f, Mathf.Max(alpha, 0.7f));
            }

            return visible;
        }

        private static void EnsureColorVertexStream(List<ParticleSystemVertexStream> vertexStreams)
        {
            if (vertexStreams == null)
            {
                return;
            }

            if (!vertexStreams.Contains(ParticleSystemVertexStream.Color))
            {
                vertexStreams.Add(ParticleSystemVertexStream.Color);
            }
        }

        internal static Material CreateSafeVisualMaterial(Material sourceMaterial, Texture fallbackTexture = null)
        {
            Texture resolvedFallbackTexture = fallbackTexture ?? GetBestAvailableTexture(sourceMaterial);
            Shader shader = FindDeterministicTransparentVisualShader();
            Material material = null;

            if (shader != null)
            {
                material = new Material(shader)
                {
                    name = sourceMaterial != null
                        ? sourceMaterial.name + "_BeatSurgeonVrSafeVisual"
                        : "BeatSurgeonVrSafeVisual"
                };

                if (sourceMaterial != null)
                {
                    CopyCommonVisualProperties(sourceMaterial, material);
                }
            }
            else if (sourceMaterial != null)
            {
                material = new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + "_BeatSurgeonVrSafeVisualFallback"
                };
            }

            ApplySharedVisualDefaults(material, resolvedFallbackTexture);
            return material;
        }

        internal static bool HasUsableShader(Material sourceMaterial)
        {
            return CanPreserveSourceMaterial(sourceMaterial);
        }

        private static bool UsesSpritesDefaultShader(Shader shader)
        {
            return shader != null
                && string.Equals(shader.name, "Sprites/Default", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAuthoredParticleTexture(Material sourceMaterial)
        {
            if (sourceMaterial == null || UsesSpritesDefaultShader(sourceMaterial.shader))
            {
                return false;
            }

            Texture texture = GetBestAvailableTexture(sourceMaterial);
            return texture != null && texture != Texture2D.whiteTexture;
        }

        /// <summary>
        /// Approximates the visible base color a particle system authors via its Start Color
        /// module, so it can be baked into SPI material tint when Custom/CustomParticles does
        /// not reproduce vertex-color multiplication reliably.
        /// </summary>
        private static Color GetApproximateAuthoredStartColor(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return Color.white;
            }

            try
            {
                ParticleSystem.MinMaxGradient startColor = particleSystem.main.startColor;
                switch (startColor.mode)
                {
                    case ParticleSystemGradientMode.Color:
                        return startColor.color;
                    case ParticleSystemGradientMode.TwoColors:
                        return Color.Lerp(startColor.colorMin, startColor.colorMax, 0.5f);
                    case ParticleSystemGradientMode.Gradient:
                        return startColor.gradient != null ? startColor.gradient.Evaluate(0f) : Color.white;
                    case ParticleSystemGradientMode.TwoGradients:
                        Color gradientMinColor = startColor.gradientMin != null ? startColor.gradientMin.Evaluate(0f) : Color.white;
                        Color gradientMaxColor = startColor.gradientMax != null ? startColor.gradientMax.Evaluate(0f) : Color.white;
                        return Color.Lerp(gradientMinColor, gradientMaxColor, 0.5f);
                    case ParticleSystemGradientMode.RandomColor:
                        return startColor.gradient != null ? startColor.gradient.Evaluate(0.5f) : Color.white;
                    default:
                        return Color.white;
                }
            }
            catch
            {
                return Color.white;
            }
        }

        private static Color ResolveAuthoredSpiVisibleColor(
            Material sourceMaterial,
            Color? particleStartColor)
        {
            if (HasAuthoredParticleTexture(sourceMaterial))
            {
                // Atlas-textured emitters (Flame, Hearts, Sparks) are authored via texture only.
                if (sourceMaterial != null
                    && TryGetPreferredVisibleColor(sourceMaterial, out Color materialTint)
                    && !LooksLikeUnsetVisibleColor(materialTint))
                {
                    return materialTint;
                }

                return Color.white;
            }

            Color startColor = particleStartColor ?? Color.white;
            bool sourceUsesSpritesDefault = UsesSpritesDefaultShader(sourceMaterial?.shader);

            Color materialTintFromSource = Color.white;
            if (sourceMaterial != null && TryGetPreferredVisibleColor(sourceMaterial, out Color preferredTint))
            {
                materialTintFromSource = preferredTint;
            }

            if (sourceUsesSpritesDefault || LooksLikeUnsetVisibleColor(materialTintFromSource))
            {
                return startColor;
            }

            return new Color(
                materialTintFromSource.r * startColor.r,
                materialTintFromSource.g * startColor.g,
                materialTintFromSource.b * startColor.b,
                Mathf.Clamp01(materialTintFromSource.a * startColor.a));
        }

        private static void ApplySpiVisibleTint(Material material, Color tint)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_TintColor"))
            {
                material.SetColor("_TintColor", tint);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", tint);
            }
        }

        internal static bool ShouldForceSafeParticleShader(Shader shader)
        {
            return ShouldForceSafeParticleShader(shader != null ? shader.name : null);
        }

        internal static bool ShouldForceSafeParticleShader(string shaderName)
        {
            if (IsBrokenShaderName(shaderName))
            {
                return true;
            }

            return ForcedSafeParticleShaderNames.Any(forcedName =>
                string.Equals(shaderName, forcedName, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool IsBrokenShader(Shader shader)
        {
            return shader == null || !shader.isSupported || IsBrokenShaderName(shader.name);
        }

        private static bool IsBrokenShaderName(string shaderName)
        {
            return string.IsNullOrWhiteSpace(shaderName)
                || string.Equals(shaderName, InvalidShaderName, StringComparison.OrdinalIgnoreCase);
        }

        internal static void PrepareBombExplosionRenderers(
            GameObject root,
            string context,
            ParticleSystemRenderer referenceRenderer = null)
        {
            if (root == null)
            {
                return;
            }

            ParticleSystemRenderer reference = referenceRenderer ?? FindVanillaParticleRenderer();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (renderer is ParticleSystemRenderer particleRenderer)
                {
                    bool preservedAuthoredLayout = PrepareBombExplosionParticleRenderer(particleRenderer, reference, context);
                    if (!preservedAuthoredLayout)
                    {
                        SyncParticleRendererStereoState(particleRenderer, reference);
                    }

                    HardenParticleRendererStereoCulling(particleRenderer);
                    EnsureMeshParticleRendererReady(particleRenderer);
                }
            }
        }

        internal static void PrepareBundleBombExplosionForVr(GameObject root, string context)
        {
            if (root == null)
            {
                return;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (renderer is ParticleSystemRenderer particleRenderer)
                {
                    HardenParticleRendererStereoCulling(particleRenderer);
                    EnsureMeshParticleRendererReady(particleRenderer);
                }
            }
        }

        internal static bool PrepareBombExplosionParticleRenderer(
            ParticleSystemRenderer particleRenderer,
            ParticleSystemRenderer referenceRenderer,
            string context)
        {
            if (particleRenderer == null)
            {
                return false;
            }

            bool preservedAuthoredLayout = false;

            try
            {
                Material[] materials = particleRenderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    Material fallbackMaterial = CreateBillboardBombExplosionMaterial(
                        null,
                        referenceRenderer,
                        null,
                        Color.white,
                        context,
                        out bool preservedFallbackMaterial);
                    preservedAuthoredLayout |= preservedFallbackMaterial;
                    if (fallbackMaterial != null)
                    {
                        particleRenderer.sharedMaterial = fallbackMaterial;
                    }

                    return preservedAuthoredLayout;
                }

                Material[] preparedMaterials = new Material[materials.Length];
                for (int index = 0; index < materials.Length; index++)
                {
                    preparedMaterials[index] = PrepareBombExplosionMaterial(
                        materials[index],
                        context,
                        referenceRenderer,
                        particleRenderer,
                        out bool preservedMaterial)
                        ?? CreateBillboardBombExplosionMaterial(
                            materials[index],
                            referenceRenderer,
                            SafeGetBestAvailableTexture(materials[index]),
                            SafeGetMaterialColor(materials[index]),
                            context,
                            out preservedMaterial);
                    preservedAuthoredLayout |= preservedMaterial;
                }

                particleRenderer.sharedMaterials = preparedMaterials;

                Material preparedTrailMaterial = PrepareBombExplosionMaterial(
                    particleRenderer.trailMaterial,
                    context + " trail",
                    referenceRenderer,
                    particleRenderer,
                    out bool preservedTrailMaterial);
                if (preparedTrailMaterial != null)
                {
                    particleRenderer.trailMaterial = preparedTrailMaterial;
                    preservedAuthoredLayout |= preservedTrailMaterial;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn(context + ": failed preparing bomb explosion particle renderer '" + particleRenderer.name + "': " + ex.Message);
            }

            return preservedAuthoredLayout;
        }

        private static Material PrepareBombExplosionMaterial(
            Material sourceMaterial,
            string context,
            ParticleSystemRenderer referenceRenderer,
            ParticleSystemRenderer ownerRenderer,
            out bool preservedAuthoredMaterial)
        {
            preservedAuthoredMaterial = false;
            bool isMeshRenderer = ownerRenderer != null
                && ownerRenderer.renderMode == ParticleSystemRenderMode.Mesh;
            Texture fallbackTexture = SafeGetBestAvailableTexture(sourceMaterial);
            Color fallbackColor = SafeGetMaterialColor(sourceMaterial);

            Material preparedMaterial = isMeshRenderer
                ? CreateMeshBombExplosionMaterial(sourceMaterial, fallbackTexture, fallbackColor, out preservedAuthoredMaterial)
                : CreateBillboardBombExplosionMaterial(
                    sourceMaterial,
                    referenceRenderer,
                    fallbackTexture,
                    fallbackColor,
                    context,
                    out preservedAuthoredMaterial);

            if (preparedMaterial == null)
            {
                return null;
            }

            if (preservedAuthoredMaterial)
            {
                ApplyMinimalVrParticleTweaks(preparedMaterial);
            }
            else
            {
                ApplySharedParticleDefaults(preparedMaterial, fallbackTexture, preserveTint: true);
            }

            return preparedMaterial;
        }

        private static Material CreateMeshBombExplosionMaterial(
            Material sourceMaterial,
            Texture fallbackTexture,
            Color fallbackColor,
            out bool preservedAuthoredMaterial)
        {
            preservedAuthoredMaterial = false;
            if (CanPreserveSourceMaterial(sourceMaterial))
            {
                preservedAuthoredMaterial = true;
                return new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + "_BeatSurgeonMeshVfx"
                };
            }

            Shader shader = Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Particles/Alpha Blended")
                ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader == null)
            {
                return CreateBillboardBombExplosionMaterial(
                    sourceMaterial,
                    null,
                    fallbackTexture,
                    fallbackColor,
                    "mesh fallback",
                    out preservedAuthoredMaterial);
            }

            Material material = new Material(shader)
            {
                name = (sourceMaterial != null ? sourceMaterial.name : "MeshParticle") + "_BeatSurgeonMeshVfx"
            };

            if (sourceMaterial != null)
            {
                CopyCommonParticleProperties(sourceMaterial, material);
            }

            ApplyFallbackVisibleColor(material, fallbackColor, fallbackTexture);
            ApplyDeterministicParticleBlend(material);
            return material;
        }

        private static Material CreateBillboardBombExplosionMaterial(
            Material sourceMaterial,
            ParticleSystemRenderer referenceRenderer,
            Texture fallbackTexture,
            Color fallbackColor,
            string context,
            out bool preservedAuthoredMaterial)
        {
            preservedAuthoredMaterial = false;
            if (CanPreserveSourceMaterial(sourceMaterial))
            {
                preservedAuthoredMaterial = true;
                return new Material(sourceMaterial)
                {
                    name = sourceMaterial.name + "_BeatSurgeonBillboardVfx"
                };
            }

            Material referenceMaterial = referenceRenderer != null ? referenceRenderer.sharedMaterial : null;
            if (referenceMaterial != null && HasUsableShader(referenceMaterial))
            {
                Material material = new Material(referenceMaterial)
                {
                    name = (sourceMaterial != null ? sourceMaterial.name : "BillboardParticle") + "_BeatSurgeonBillboardVfx"
                };

                if (sourceMaterial != null)
                {
                    CopyCommonParticleProperties(sourceMaterial, material);
                }

                ApplyFallbackVisibleColor(material, fallbackColor, fallbackTexture);
                return material;
            }

            Material forcedSafeMaterial = CreateForcedSafeParticleMaterial(sourceMaterial, fallbackTexture);
            if (forcedSafeMaterial != null && IsBrokenShader(forcedSafeMaterial.shader))
            {
                Plugin.Log.Warn(context + ": bomb explosion billboard material still has a broken shader after forced-safe fallback.");
                return null;
            }

            ApplyFallbackVisibleColor(forcedSafeMaterial, fallbackColor, fallbackTexture);
            return forcedSafeMaterial;
        }

        private static void ApplyFallbackVisibleColor(Material material, Color fallbackColor, Texture fallbackTexture)
        {
            if (material == null)
            {
                return;
            }

            if (fallbackTexture != null && MaterialSupportsTextureAssignment(material))
            {
                TryAssignTexture(material, fallbackTexture);
            }

            if (fallbackColor.a <= 0.01f && fallbackColor.maxColorComponent <= 0.01f)
            {
                fallbackColor = Color.white;
            }

            TrySetColorIfDefault(material, "_TintColor", fallbackColor);
            TrySetColorIfDefault(material, "_Color", fallbackColor);
            TrySetColorIfDefault(material, "_BaseColor", fallbackColor);
        }

        private static Color SafeGetMaterialColor(Material sourceMaterial)
        {
            if (sourceMaterial == null || IsBrokenShader(sourceMaterial.shader))
            {
                return Color.white;
            }

            if (TryGetPreferredVisibleColor(sourceMaterial, out Color preferredColor))
            {
                return preferredColor;
            }

            return Color.white;
        }

        private static Texture SafeGetBestAvailableTexture(Material sourceMaterial)
        {
            if (sourceMaterial == null)
            {
                return null;
            }

            if (!IsBrokenShader(sourceMaterial.shader))
            {
                return GetBestAvailableTexture(sourceMaterial);
            }

            string[] commonTextureProperties =
            {
                "_MainTex",
                "_BaseMap",
                "_EmissionMap",
                "_AlphaTex",
                "_MaskTex",
                "_DetailAlbedoMap"
            };

            foreach (string propertyName in commonTextureProperties)
            {
                try
                {
                    if (!sourceMaterial.HasProperty(propertyName))
                    {
                        continue;
                    }

                    Texture texture = sourceMaterial.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
                catch { }
            }

            return null;
        }

        private static void EnsureMeshParticleRendererReady(ParticleSystemRenderer particleRenderer)
        {
            if (particleRenderer == null || particleRenderer.renderMode != ParticleSystemRenderMode.Mesh)
            {
                return;
            }

            try
            {
                particleRenderer.enabled = true;
                particleRenderer.forceRenderingOff = false;

                if (particleRenderer.mesh == null)
                {
                    MeshFilter meshFilter = particleRenderer.GetComponent<MeshFilter>();
                    if (meshFilter != null && meshFilter.sharedMesh != null)
                    {
                        particleRenderer.mesh = meshFilter.sharedMesh;
                    }
                }

                if (particleRenderer.meshCount > 0)
                {
                    Mesh[] meshes = new Mesh[particleRenderer.meshCount];
                    particleRenderer.GetMeshes(meshes);
                    if (meshes.All(mesh => mesh == null))
                    {
                        Plugin.Log.Warn(
                            "VrVfxMaterialHelper: mesh particle renderer '"
                            + particleRenderer.name
                            + "' has no assigned mesh.");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn(
                    "VrVfxMaterialHelper: failed preparing mesh particle renderer '"
                    + particleRenderer.name
                    + "': "
                    + ex.Message);
            }
        }

        internal static bool UsesBillboardStyleVertexStreams(ParticleSystemRenderMode renderMode)
        {
            return renderMode == ParticleSystemRenderMode.Billboard
                || renderMode == ParticleSystemRenderMode.Stretch
                || renderMode == ParticleSystemRenderMode.HorizontalBillboard
                || renderMode == ParticleSystemRenderMode.VerticalBillboard;
        }

        internal static void SyncParticleRendererStereoState(
            ParticleSystemRenderer particleRenderer,
            ParticleSystemRenderer referenceRenderer,
            bool preserveAuthoredLayout = false,
            bool preserveAuthoredVertexStreams = false)
        {
            if (particleRenderer == null || referenceRenderer == null)
            {
                return;
            }

            try
            {
                bool syncLayout = UsesBillboardStyleVertexStreams(particleRenderer.renderMode)
                    && UsesBillboardStyleVertexStreams(referenceRenderer.renderMode);
                bool renderModesMatch = particleRenderer.renderMode == referenceRenderer.renderMode;
                bool keepAuthoredVertexStreams = preserveAuthoredVertexStreams
                    || (preserveAuthoredLayout && !renderModesMatch);

                if (syncLayout && !preserveAuthoredLayout)
                {
                    particleRenderer.alignment = referenceRenderer.alignment;
                    particleRenderer.normalDirection = referenceRenderer.normalDirection;
                    particleRenderer.allowRoll = referenceRenderer.allowRoll;
                }

                if (!preserveAuthoredLayout)
                {
                    particleRenderer.maskInteraction = referenceRenderer.maskInteraction;
                    particleRenderer.sortingFudge = referenceRenderer.sortingFudge;
                    particleRenderer.renderingLayerMask = referenceRenderer.renderingLayerMask;
                    particleRenderer.lightProbeUsage = referenceRenderer.lightProbeUsage;
                    particleRenderer.reflectionProbeUsage = referenceRenderer.reflectionProbeUsage;
                    particleRenderer.motionVectorGenerationMode = referenceRenderer.motionVectorGenerationMode;
                }

                ParticleSystem particleSystemForTrails = particleRenderer.GetComponent<ParticleSystem>();
                bool hasTrails = particleSystemForTrails != null && particleSystemForTrails.trails.enabled;
                // GPU instancing on trail ribbons breaks SPI stereo for effects like Lightning.
                particleRenderer.enableGPUInstancing = hasTrails
                    ? false
                    : referenceRenderer.enableGPUInstancing;

                if (!syncLayout)
                {
                    return;
                }

                if (keepAuthoredVertexStreams)
                {
                    var authoredVertexStreams = new List<ParticleSystemVertexStream>(particleRenderer.activeVertexStreamsCount);
                    particleRenderer.GetActiveVertexStreams(authoredVertexStreams);
                    if (authoredVertexStreams.Count > 0)
                    {
                        EnsureColorVertexStream(authoredVertexStreams);
                        particleRenderer.SetActiveVertexStreams(authoredVertexStreams);
                    }

                    if (hasTrails)
                    {
                        Material sharedMaterial = particleRenderer.sharedMaterial;
                        if (particleRenderer.trailMaterial == null && sharedMaterial != null)
                        {
                            particleRenderer.trailMaterial = sharedMaterial;
                        }

                        var authoredTrailVertexStreams = new List<ParticleSystemVertexStream>(particleRenderer.activeTrailVertexStreamsCount);
                        particleRenderer.GetActiveTrailVertexStreams(authoredTrailVertexStreams);
                        if (authoredTrailVertexStreams.Count == 0)
                        {
                            authoredTrailVertexStreams = new List<ParticleSystemVertexStream>(authoredVertexStreams);
                        }

                        EnsureColorVertexStream(authoredTrailVertexStreams);
                        if (authoredTrailVertexStreams.Count > 0)
                        {
                            particleRenderer.SetActiveTrailVertexStreams(authoredTrailVertexStreams);
                        }
                    }

                    return;
                }

                var activeVertexStreams = new List<ParticleSystemVertexStream>(referenceRenderer.activeVertexStreamsCount);
                referenceRenderer.GetActiveVertexStreams(activeVertexStreams);
                if (activeVertexStreams.Count > 0)
                {
                    EnsureColorVertexStream(activeVertexStreams);
                    particleRenderer.SetActiveVertexStreams(activeVertexStreams);
                }

                if (hasTrails)
                {
                    Material sharedMaterial = particleRenderer.sharedMaterial;
                    if (particleRenderer.trailMaterial == null && sharedMaterial != null)
                    {
                        particleRenderer.trailMaterial = sharedMaterial;
                    }

                    ParticleSystemRenderer trailReference = referenceRenderer;
                    var activeTrailVertexStreams = new List<ParticleSystemVertexStream>(trailReference.activeTrailVertexStreamsCount);
                    trailReference.GetActiveTrailVertexStreams(activeTrailVertexStreams);
                    if (activeTrailVertexStreams.Count == 0)
                    {
                        activeTrailVertexStreams = new List<ParticleSystemVertexStream>(trailReference.activeVertexStreamsCount);
                        trailReference.GetActiveVertexStreams(activeTrailVertexStreams);
                    }

                    if (activeTrailVertexStreams.Count == 0)
                    {
                        activeTrailVertexStreams = new List<ParticleSystemVertexStream>(activeVertexStreams);
                    }

                    EnsureColorVertexStream(activeTrailVertexStreams);
                    if (activeTrailVertexStreams.Count > 0)
                    {
                        particleRenderer.SetActiveTrailVertexStreams(activeTrailVertexStreams);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("VrVfxMaterialHelper: failed syncing stereo renderer state for '" + particleRenderer.name + "': " + ex.Message);
            }
        }

        internal static void HardenParticleRendererStereoCulling(ParticleSystemRenderer particleRenderer)
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
                particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                particleRenderer.receiveShadows = false;

                Bounds localBounds = particleRenderer.localBounds;
                float minimumBoundsSize = EstimateMinimumParticleBoundsSize(particleRenderer.GetComponent<ParticleSystem>());
                Vector3 expandedSize = new Vector3(
                    Mathf.Max(localBounds.size.x, minimumBoundsSize),
                    Mathf.Max(localBounds.size.y, minimumBoundsSize),
                    Mathf.Max(localBounds.size.z, minimumBoundsSize));
                particleRenderer.localBounds = new Bounds(localBounds.center, expandedSize);

                ParticleSystem particleSystem = particleRenderer.GetComponent<ParticleSystem>();
                if (particleSystem != null)
                {
                    ParticleSystem.MainModule main = particleSystem.main;
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("VrVfxMaterialHelper: failed hardening stereo culling for '" + particleRenderer.name + "': " + ex.Message);
            }
        }

        private static float EstimateMinimumParticleBoundsSize(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return 12f;
            }

            try
            {
                ParticleSystem.MainModule main = particleSystem.main;
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

        internal static Texture GetBestAvailableTexture(Material sourceMaterial)
        {
            if (sourceMaterial == null)
            {
                return null;
            }

            if (IsBrokenShader(sourceMaterial.shader))
            {
                return SafeGetBestAvailableTexture(sourceMaterial);
            }

            try
            {
                if (sourceMaterial.HasProperty("_MainTex"))
                {
                    Texture mainTexture = sourceMaterial.GetTexture("_MainTex");
                    if (mainTexture != null)
                    {
                        return mainTexture;
                    }
                }
            }
            catch { }

            string[] commonTextureProperties =
            {
                "_MainTex",
                "_BaseMap",
                "_EmissionMap",
                "_AlphaTex",
                "_MaskTex",
                "_DetailAlbedoMap"
            };

            foreach (string propertyName in commonTextureProperties)
            {
                try
                {
                    if (!sourceMaterial.HasProperty(propertyName))
                    {
                        continue;
                    }

                    Texture texture = sourceMaterial.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
                catch { }
            }

            try
            {
                foreach (string propertyName in sourceMaterial.GetTexturePropertyNames())
                {
                    Texture texture = sourceMaterial.GetTexture(propertyName);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool CanPreserveSourceMaterial(Material sourceMaterial)
        {
            return sourceMaterial != null
                && sourceMaterial.shader != null
                && sourceMaterial.shader.isSupported
                && !string.IsNullOrWhiteSpace(sourceMaterial.shader.name)
                && !string.Equals(sourceMaterial.shader.name, InvalidShaderName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldSkipKnownUnsupportedVisualRepair(Material sourceMaterial, string context)
        {
            if (sourceMaterial == null)
            {
                return false;
            }

            if (!string.Equals(sourceMaterial.name, "TrailMaterial", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string shaderName = sourceMaterial.shader != null ? sourceMaterial.shader.name : null;
            if (!string.Equals(shaderName, "Unlit/TrailShader", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(context)
                && context.IndexOf("TrailCube", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static void CopyCommonParticlePropertiesPublic(Material sourceMaterial, Material destinationMaterial)
        {
            CopyCommonParticleProperties(sourceMaterial, destinationMaterial);
        }

        private static void CopyCommonParticleProperties(Material sourceMaterial, Material destinationMaterial)
        {
            if (sourceMaterial == null || destinationMaterial == null)
            {
                return;
            }

            TryCopyTexture(sourceMaterial, destinationMaterial, "_MainTex");
            TryCopyTexture(sourceMaterial, destinationMaterial, "_BaseMap");
            TryCopyColor(sourceMaterial, destinationMaterial, "_TintColor");
            TryCopyColor(sourceMaterial, destinationMaterial, "_Color");
            TryCopyColor(sourceMaterial, destinationMaterial, "_BaseColor");
            TryCopyColor(sourceMaterial, destinationMaterial, "_EmissionColor");
            TryCopyFloat(sourceMaterial, destinationMaterial, "_InvFade");
            TryCopyFloat(sourceMaterial, destinationMaterial, "_Cutoff");

            Texture bestTexture = GetBestAvailableTexture(sourceMaterial);
            if (bestTexture != null && MaterialSupportsTextureAssignment(destinationMaterial))
            {
                TryAssignTexture(destinationMaterial, bestTexture);
            }

            TryCopyTextureScaleAndOffset(sourceMaterial, destinationMaterial, "_MainTex");
            TryCopyTextureScaleAndOffset(sourceMaterial, destinationMaterial, "_BaseMap");

            // When the source shader defines _TintColor but the destination shader does not
            // (e.g. Particles/Standard Unlit used as a menu-time fallback), the authored tint
            // would be silently dropped. Preserve it in _Color so it can be recovered when
            // the material is later replaced with Custom/CustomParticles at play time.
            try
            {
                if (sourceMaterial.HasProperty("_TintColor")
                    && !destinationMaterial.HasProperty("_TintColor")
                    && destinationMaterial.HasProperty("_Color"))
                {
                    Color srcTint = sourceMaterial.GetColor("_TintColor");
                    bool tintIsUnset = srcTint.maxColorComponent <= 0.01f || srcTint.a <= 0.01f
                        || (Mathf.Abs(srcTint.r - 1f) <= 0.01f && Mathf.Abs(srcTint.g - 1f) <= 0.01f
                            && Mathf.Abs(srcTint.b - 1f) <= 0.01f && Mathf.Abs(srcTint.a - 1f) <= 0.01f);
                    if (!tintIsUnset)
                    {
                        destinationMaterial.SetColor("_Color", srcTint);
                    }
                }
            }
            catch { }
        }

        private static void CopyCommonVisualProperties(Material sourceMaterial, Material destinationMaterial)
        {
            if (sourceMaterial == null || destinationMaterial == null)
            {
                return;
            }

            CopyCommonParticleProperties(sourceMaterial, destinationMaterial);
            TryCopyTexture(sourceMaterial, destinationMaterial, "_EmissionMap");
            TryCopyTexture(sourceMaterial, destinationMaterial, "_MaskTex");
            TryCopyTexture(sourceMaterial, destinationMaterial, "_DetailAlbedoMap");
            TryCopyColor(sourceMaterial, destinationMaterial, "_EmissionColor");
            TryCopyFloat(sourceMaterial, destinationMaterial, "_Cutoff");
            CopyPreferredVisibleColor(sourceMaterial, destinationMaterial);
        }

        private static void CopyPreferredVisibleColor(Material sourceMaterial, Material destinationMaterial)
        {
            if (sourceMaterial == null || destinationMaterial == null)
            {
                return;
            }

            if (!TryGetPreferredVisibleColor(sourceMaterial, out Color preferredColor))
            {
                return;
            }

            TrySetColorIfDefault(destinationMaterial, "_TintColor", preferredColor);
            TrySetColorIfDefault(destinationMaterial, "_Color", preferredColor);
            TrySetColorIfDefault(destinationMaterial, "_BaseColor", preferredColor);
        }

        private static void ApplySharedParticleDefaults(Material material, Texture fallbackTexture, bool preserveTint)
        {
            if (material == null)
            {
                return;
            }

            if (fallbackTexture == null)
            {
                fallbackTexture = SafeGetBestAvailableTexture(material);
            }

            if (fallbackTexture != null && MaterialSupportsTextureAssignment(material))
            {
                TryAssignTexture(material, fallbackTexture);
            }

            ApplyMinimalVrParticleTweaks(material);

            if (!preserveTint)
            {
                try
                {
                    if (material.HasProperty("_TintColor"))
                    {
                        Color tint = material.GetColor("_TintColor");
                        if (tint.maxColorComponent <= 0f || tint.a <= 0f)
                        {
                            material.SetColor("_TintColor", Color.white);
                        }
                    }

                    NormalizeVisibleColor(material, "_Color");
                    NormalizeVisibleColor(material, "_BaseColor");
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn("VrVfxMaterialHelper: Failed to normalize particle tint: " + ex.Message);
                }
            }
        }

        private static void ApplyMinimalVrParticleTweaks(Material material)
        {
            if (material == null)
            {
                return;
            }

            try
            {
                if (material.HasProperty("_EnableCloseToCameraDisappear"))
                {
                    material.SetFloat("_EnableCloseToCameraDisappear", 0f);
                }

                if (material.HasProperty("_ZWrite"))
                {
                    material.SetFloat("_ZWrite", 0f);
                }

                if (material.renderQueue < 3000)
                {
                    material.renderQueue = 3100;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("VrVfxMaterialHelper: Failed to apply minimal VR particle tweaks: " + ex.Message);
            }
        }

        private static bool MaterialSupportsTextureAssignment(Material material)
        {
            if (material == null)
            {
                return false;
            }

            return material.HasProperty("_MainTex") || material.HasProperty("_BaseMap");
        }

        private static void ApplySharedVisualDefaults(Material material, Texture fallbackTexture)
        {
            if (material == null)
            {
                return;
            }

            if (fallbackTexture == null)
            {
                fallbackTexture = GetBestAvailableTexture(material);
            }

            if (MaterialSupportsTextureAssignment(material))
            {
                TryAssignTexture(material, fallbackTexture);
            }

            try
            {
                if (material.HasProperty("_ZWrite"))
                {
                    material.SetFloat("_ZWrite", 0f);
                }

                NormalizeVisibleColor(material, "_TintColor");
                NormalizeVisibleColor(material, "_Color");
                NormalizeVisibleColor(material, "_BaseColor");

                if (material.renderQueue < 3000)
                {
                    material.renderQueue = 3100;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("VrVfxMaterialHelper: Failed to apply shared visual defaults: " + ex.Message);
            }
        }

        private static void TryAssignTexture(Material material, Texture texture)
        {
            if (material == null || texture == null || !MaterialSupportsTextureAssignment(material))
            {
                return;
            }

            string[] commonTextureProperties =
            {
                "_MainTex",
                "_BaseMap"
            };

            foreach (string propertyName in commonTextureProperties)
            {
                try
                {
                    if (material.HasProperty(propertyName))
                    {
                        material.SetTexture(propertyName, texture);
                    }
                }
                catch { }
            }
        }

        private static Material GetSafeParticleMaterialBase()
        {
            if (_safeParticleMaterialBase != null)
            {
                return _safeParticleMaterialBase;
            }

            ParticleSystemRenderer renderer = FindVanillaParticleRenderer();
            if (renderer?.sharedMaterial == null)
            {
                LogUtils.Debug(() => "VrVfxMaterialHelper: Could not find a deterministic vanilla particle material for VR-safe VFX.");
                return null;
            }

            _safeParticleMaterialBase = new Material(renderer.sharedMaterial)
            {
                name = renderer.sharedMaterial.name + "_BeatSurgeonParticleBase"
            };

            LogUtils.Debug(() =>
                "VrVfxMaterialHelper: Using vanilla particle material '"
                + renderer.sharedMaterial.name
                + "' from '"
                + GetTransformPath(renderer.transform)
                + "' as the VR-safe base.");

            return _safeParticleMaterialBase;
        }

        private static ParticleSystemRenderer FindVanillaParticleRenderer()
        {
            // Prefer bootstrapped owned Sparks or ExplosionSparkles (reference-only).
            ParticleSystemRenderer spi = BeatSurgeonOwnedVfxSpace.TryGetSpiStereoReferenceRenderer();
            if (spi != null && spi.sharedMaterial != null)
            {
                return spi;
            }

            ParticleSystemRenderer owned = BeatSurgeonOwnedVfxSpace.TryGetOwnedSpiReferenceRenderer();
            if (owned != null && owned.sharedMaterial != null)
            {
                return owned;
            }

            return Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>()
                .Where(IsUsableVanillaParticleRenderer)
                .OrderByDescending(GetRendererScore)
                .FirstOrDefault();
        }

        /// <summary>
        /// Resolves a Single Pass Instanced capable particle shader for trail emitters (Lightning).
        /// Rejects menu-safe fallbacks like Particles/Standard Unlit that render in one eye.
        /// </summary>
        private static Shader ResolveSpiCapableParticleShader(ParticleSystemRenderer referenceRenderer)
        {
            Shader candidate = referenceRenderer != null && referenceRenderer.sharedMaterial != null
                ? referenceRenderer.sharedMaterial.shader
                : null;
            if (IsSpiCapableParticleShader(candidate))
            {
                return candidate;
            }

            Shader customParticles = Shader.Find("Custom/CustomParticles");
            if (customParticles != null)
            {
                return customParticles;
            }

            ParticleSystemRenderer sparkles = BeatSurgeonOwnedVfxSpace.TryFindVanillaExplosionSparklesRenderer();
            if (sparkles != null
                && sparkles.sharedMaterial != null
                && IsSpiCapableParticleShader(sparkles.sharedMaterial.shader))
            {
                return sparkles.sharedMaterial.shader;
            }

            ParticleSystemRenderer spi = BeatSurgeonOwnedVfxSpace.TryGetSpiStereoReferenceRenderer();
            if (spi != null
                && spi.sharedMaterial != null
                && IsSpiCapableParticleShader(spi.sharedMaterial.shader))
            {
                return spi.sharedMaterial.shader;
            }

            return candidate;
        }

        private static ParticleSystemRenderer ResolveStereoStreamReference(ParticleSystemRenderer referenceRenderer)
        {
            if (referenceRenderer != null
                && referenceRenderer.sharedMaterial != null
                && IsSpiCapableParticleShader(referenceRenderer.sharedMaterial.shader))
            {
                return referenceRenderer;
            }

            ParticleSystemRenderer spi = BeatSurgeonOwnedVfxSpace.TryGetSpiStereoReferenceRenderer();
            if (spi != null)
            {
                return spi;
            }

            ParticleSystemRenderer sparkles = BeatSurgeonOwnedVfxSpace.TryFindVanillaExplosionSparklesRenderer();
            if (sparkles != null)
            {
                return sparkles;
            }

            return referenceRenderer;
        }

        private static bool IsSpiCapableParticleShader(Shader shader)
        {
            if (shader == null)
            {
                return false;
            }

            // Forced-safe list are known non-SPI (or menu fallback) particle shaders.
            if (ShouldForceSafeParticleShader(shader))
            {
                return false;
            }

            string shaderName = shader.name ?? string.Empty;
            return shaderName.IndexOf("Custom/CustomParticles", StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("Custom/SimpleLit", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsUsableVanillaParticleRenderer(ParticleSystemRenderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
            {
                return false;
            }

            string path = GetTransformPath(renderer.transform).ToLowerInvariant();
            string transformName = renderer.transform != null ? renderer.transform.name : string.Empty;
            string shaderName = renderer.sharedMaterial.shader.name;

            // Prefer not to use NoteCut particles for generic material bases, but ExplosionSparkles
            // is allowed via TryFindVanillaExplosionSparklesRenderer / TryGetSpiStereoReferenceRenderer.
            if (path.Contains("notecutcoreeffectsspawner")
                || path.Contains("bombexplosioneffect")
                || transformName.IndexOf("ExplosionSparkles", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return !path.Contains("beatsurgeon")
                && !path.Contains("surgeonexplosion")
                && !path.Contains("outlineparticles")
                && !path.Contains("twitch")
                && !path.Contains("subscriber")
                && !path.Contains("follower")
                && !path.Contains("bitshypercube")
                && !IsRejectedFallbackShader(shaderName);
        }

        private static int GetRendererScore(ParticleSystemRenderer renderer)
        {
            string path = GetTransformPath(renderer.transform).ToLowerInvariant();
            int score = 0;

            // Penalize any remaining NoteCut adjacency; prefer environment / saber dust sparks.
            if (path.Contains("notecut")) score -= 2000;
            if (path.Contains("shockwave")) score -= 400;
            if (path.Contains("saber")) score += 300;
            if (path.Contains("spark")) score += 150;
            if (path.Contains("burn")) score += 100;
            if (path.Contains("dust")) score += 100;
            if (path.Contains("core")) score += 50;
            if (renderer.sharedMaterial != null && GetBestAvailableTexture(renderer.sharedMaterial) != null) score += 25;
            if (renderer.sharedMaterial != null && IsLikelyParticleShader(renderer.sharedMaterial.shader?.name)) score += 100;

            return score;
        }

        private static Material CreateDeterministicTransparentParticleMaterial(Material sourceMaterial, string nameSuffix)
        {
            Shader shader = FindDeterministicTransparentParticleShader();
            if (shader == null)
            {
                return null;
            }

            Material material = new Material(shader)
            {
                name = sourceMaterial != null
                    ? sourceMaterial.name + nameSuffix
                    : "BeatSurgeonForcedSafeParticle"
            };

            if (sourceMaterial != null)
            {
                CopyCommonParticleProperties(sourceMaterial, material);
            }

            return material;
        }

        private static Shader FindDeterministicTransparentVisualShader()
        {
            foreach (string shaderName in DeterministicTransparentVisualShaders)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static Shader FindDeterministicTransparentParticleShader()
        {
            foreach (string shaderName in DeterministicTransparentParticleShaders)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    return shader;
                }
            }

            return null;
        }

        private static void ApplyDeterministicParticleBlend(Material material)
        {
            if (material == null)
            {
                return;
            }

            TrySetFloat(material, "_Mode", 2f);
            TrySetFloat(material, "_Surface", 1f);
            TrySetFloat(material, "_Blend", 0f);
            TrySetFloat(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            TrySetFloat(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            TrySetFloat(material, "_AlphaClip", 0f);
            TrySetFloat(material, "_Cutoff", 0f);

            if (material.renderQueue < 3000)
            {
                material.renderQueue = 3100;
            }
        }

        private static bool IsRejectedFallbackShader(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return true;
            }

            string normalizedShaderName = shaderName.ToLowerInvariant();
            return RejectedFallbackShaderTokens.Any(normalizedShaderName.Contains);
        }

        private static bool IsLikelyParticleShader(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName))
            {
                return false;
            }

            string normalizedShaderName = shaderName.ToLowerInvariant();
            return normalizedShaderName.Contains("particle")
                || normalizedShaderName.Contains("particles")
                || normalizedShaderName.Contains("sprite")
                || normalizedShaderName.Contains("alpha");
        }

        private static void NormalizeVisibleColor(Material material, string propertyName)
        {
            if (material == null || !material.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                Color color = material.GetColor(propertyName);
                if (color.maxColorComponent <= 0f || color.a <= 0f)
                {
                    material.SetColor(propertyName, Color.white);
                }
            }
            catch { }
        }

        private static void TrySetFloat(Material material, string propertyName, float value)
        {
            if (material == null || !material.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                material.SetFloat(propertyName, value);
            }
            catch { }
        }

        private static void TryCopyTexture(Material sourceMaterial, Material destinationMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !destinationMaterial.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                Texture texture = sourceMaterial.GetTexture(propertyName);
                if (texture != null)
                {
                    destinationMaterial.SetTexture(propertyName, texture);
                }
            }
            catch { }
        }

        private static void TryCopyTextureScaleAndOffset(Material sourceMaterial, Material destinationMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !destinationMaterial.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                destinationMaterial.SetTextureScale(propertyName, sourceMaterial.GetTextureScale(propertyName));
                destinationMaterial.SetTextureOffset(propertyName, sourceMaterial.GetTextureOffset(propertyName));
            }
            catch { }
        }

        private static bool TryGetPreferredVisibleColor(Material material, out Color color)
        {
            string[] propertyNames =
            {
                "_TintColor",
                "_BaseColor",
                "_Color",
                "_EmissionColor"
            };

            foreach (string propertyName in propertyNames)
            {
                if (material == null || !material.HasProperty(propertyName))
                {
                    continue;
                }

                try
                {
                    Color candidate = material.GetColor(propertyName);
                    if (!LooksLikeUnsetVisibleColor(candidate))
                    {
                        color = candidate;
                        return true;
                    }
                }
                catch { }
            }

            color = default;
            return false;
        }

        private static void TrySetColorIfDefault(Material material, string propertyName, Color color)
        {
            if (material == null || !material.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                if (LooksLikeUnsetVisibleColor(material.GetColor(propertyName)))
                {
                    material.SetColor(propertyName, color);
                }
            }
            catch { }
        }

        private static bool LooksLikeUnsetVisibleColor(Color color)
        {
            const float epsilon = 0.01f;

            bool isTransparentOrBlack = color.maxColorComponent <= epsilon || color.a <= epsilon;
            bool isOpaqueWhite = Mathf.Abs(color.r - 1f) <= epsilon
                && Mathf.Abs(color.g - 1f) <= epsilon
                && Mathf.Abs(color.b - 1f) <= epsilon
                && Mathf.Abs(color.a - 1f) <= epsilon;

            return isTransparentOrBlack || isOpaqueWhite;
        }

        private static bool ShouldSuppressRepairWarning(string[] missingShaderNames, string[] suppressedMissingShaderNames)
        {
            if (missingShaderNames == null || missingShaderNames.Length == 0 || suppressedMissingShaderNames == null || suppressedMissingShaderNames.Length == 0)
            {
                return false;
            }

            return missingShaderNames.All(missingShaderName =>
                suppressedMissingShaderNames.Any(suppressedName =>
                    string.Equals(missingShaderName, suppressedName, StringComparison.OrdinalIgnoreCase)));
        }

        private static void TryCopyColor(Material sourceMaterial, Material destinationMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !destinationMaterial.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                destinationMaterial.SetColor(propertyName, sourceMaterial.GetColor(propertyName));
            }
            catch { }
        }

        private static void TryCopyFloat(Material sourceMaterial, Material destinationMaterial, string propertyName)
        {
            if (!sourceMaterial.HasProperty(propertyName) || !destinationMaterial.HasProperty(propertyName))
            {
                return;
            }

            try
            {
                destinationMaterial.SetFloat(propertyName, sourceMaterial.GetFloat(propertyName));
            }
            catch { }
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
    }
}